using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// THE ASYNC-I/O PARK PROTOCOL — a port of Go's <c>runtime/netpoll.go</c>, emitted ONCE for every
/// target.
///
/// ⭐ WHY THIS FILE EXISTS AT ALL, AND WHY IT IS NOT IN A BACKEND. The question this code answers is
/// "when an I/O completes, who is responsible for resuming the green thread that was waiting on it —
/// the completer, or the waiter itself?". That question has nothing to do with kqueue, IOCP or epoll:
/// it is the same question on every platform, and Go answers it in ONE portable file
/// (<c>netpoll.go</c>) with the per-platform files (<c>netpoll_kqueue.go</c>,
/// <c>netpoll_windows.go</c>, <c>netpoll_epoll.go</c>) supplying only readiness DISCOVERY. This tree
/// had it the other way round: the handshake was hand-rolled separately in
/// <c>ARM64CodeEmitter.Runtime.cs</c> and <c>X86CodeEmitter.Runtime.cs</c>, and the predictable
/// happened — x64 got the arm-before-mark ordering right and carried an <c>OPEN #66</c> comment
/// about the hazard, arm64 got it wrong, and the two were only found to disagree months later, by a
/// wedge. Linux async I/O is not written yet; when it is, epoll must inherit this protocol rather
/// than become the third independent derivation of it.
///
/// ⭐⭐ THE STATE MACHINE, AND THE ONE THING THAT MAKES IT WORK. One word per waiter
/// (<see cref="GtLayout.GtOffParkState"/>) holds EITHER a sentinel OR "the waiter is parked", so
/// "has the I/O fired?" and "am I committed to parking?" are decided by a SINGLE atomic operation
/// instead of by two independent words with a gap between them. The parker does not publish
/// "I am parked" and hope; it CASes <c>Wait -&gt; Parked</c>, and <b>that CAS is allowed to fail</b>
/// — a completer got there first, so the park is ABORTED and the green thread simply keeps running.
/// The completer does not test a flag and guess; it CASes to <c>Ready</c>, and what it finds tells
/// it exactly what to do: <c>Wait</c> means the waiter has not committed and will abort itself,
/// <c>Parked</c> means the enqueue is the completer's job. Exactly one side acts, always.
///
/// ⚠ THE COMPLETER NEVER BLOCKS ON THE DECISION, and that is load-bearing rather than incidental.
/// On macOS the kqueue drain (<c>__io_poll_kqueue</c>) runs INLINE inside every scheduler idle turn,
/// so a decision that waited on another thread would stall every other pending completion — the
/// livelock <c>__io_op_done</c>'s own comment warns about. <c>netpollunblock</c> is a pure CAS loop
/// that waits for nobody, which is precisely why the protocol fits at a site where a spin gate does
/// not. (It does spin AFTER claiming <c>Parked</c>, for <c>ioYielded</c> — see
/// <see cref="EmitNetpollUnblock"/> for why that one terminates and the old one could not.)
///
/// ⚠ DEVIATIONS FROM netpoll.go ARE MARKED "DEVIATION" AT THEIR SITE. There are three, and each has
/// a reason recorded next to it. A partial port of Go has already cost this project a real bug (the
/// green-thread stack took Go's <c>_StackMin</c> and the 928-byte guard but dropped
/// <c>_StackSystem</c>, so a CPU fault inside <c>async</c> dies with the panic lost), so silent
/// divergence is exactly the failure this file exists to prevent.
/// </summary>
public partial class RuntimeEmitter {

  // ---- Globals ----

  /// <summary>
  /// How many wakeups the recovery net has had to rescue. THE POINT OF THE NET IS THAT THIS STAYS 0:
  /// under a correct protocol nothing can ever readied-but-unclaimed, so a non-zero value is not a
  /// statistic, it is a bug report. It is a counter rather than a log line because the net runs from
  /// scheduler idle turns where an I/O of its own would be absurd; <c>__netpoll_recovered</c> is
  /// readable from a debugger, from the debug agent's global reads, and by a test binary.
  /// </summary>
  public const string NetpollRecoveredLabel = "__netpoll_recovered";

  /// <summary>Coarse-clock stamp of the last recovery scan, so the walk is time-gated.</summary>
  private const string NetpollLastScanMsLabel = "__netpoll_last_scan_ms";

  /// <summary>
  /// The GT the PREVIOUS recovery scan suspected. A candidate must be seen twice, a scan interval
  /// apart, before the net acts on it — see <see cref="EmitNetpollRecover"/> for why one sighting
  /// is not enough.
  /// </summary>
  private const string NetpollSuspectLabel = "__netpoll_suspect";

  /// <summary>
  /// FAULT INJECTION, in milliseconds; 0 = off. See <see cref="EmitNetpollInjectDelay"/>.
  /// </summary>
  private const string NetpollParkDelayMsLabel = "__netpoll_park_delay_ms";

  private const string NetpollParkDelayEnvSymdata = "__netpoll_park_delay_env";

  /// <summary>
  /// The environment variable that arms the fault injection. Named in one place because the harness
  /// in <c>scripts/</c> and the emitted code have to agree and nothing else would make them.
  /// </summary>
  public const string NetpollParkDelayEnvName = "MAXON_GT_PARK_DELAY_MS";

  /// <summary>
  /// Minimum gap between recovery scans. Go's sysmon re-polls the netpoller when it has not run for
  /// <c>10e6</c> ns; this is the same 10 ms, on the coarse tick the scheduler already reads.
  /// </summary>
  private const int NetpollRecoverIntervalMs = 10;

  // ---- Panic messages. Go's spellings, because they name Go's invariants. ----

  private const string NetpollDoubleWaitMsg = "__netpoll_msg_double_wait";
  private const string NetpollCorruptMsg = "__netpoll_msg_corrupt";
  private const string NetpollUnblockStateMsg = "__netpoll_msg_unblock_state";

  /// <summary>
  /// Printed to stderr the FIRST time the recovery net has to rescue a wakeup. A counter alone is
  /// not observability — nothing reads a global in a released binary — and a net that rescues
  /// silently is precisely a net that MASKS the regression it was built to expose. Once, not every
  /// time: the condition is systemic, so a per-occurrence line would bury the run's real output.
  /// </summary>
  private const string NetpollRecoveredMsg = "__netpoll_msg_recovered";

  /// <summary>
  /// Define the protocol's globals and panic strings. Called from each backend's globals block, so
  /// both binaries carry the same words at the same names.
  /// </summary>
  public void EmitNetpollGlobals() {
    _b.DefineGlobal(NetpollRecoveredLabel, 8, 0);
    _b.DefineGlobal(NetpollLastScanMsLabel, 8, 0);
    _b.DefineGlobal(NetpollSuspectLabel, 8, 0);
    _b.DefineGlobal(NetpollParkDelayMsLabel, 8, 0);

    _b.DefineSymdata(NetpollParkDelayEnvSymdata,
      System.Text.Encoding.UTF8.GetBytes(NetpollParkDelayEnvName + "\0"));
    _b.DefineSymdata(NetpollDoubleWaitMsg,
      System.Text.Encoding.UTF8.GetBytes("runtime: double wait\n\0"));
    _b.DefineSymdata(NetpollCorruptMsg,
      System.Text.Encoding.UTF8.GetBytes("runtime: corrupted parkstate\n\0"));
    _b.DefineSymdata(NetpollUnblockStateMsg,
      System.Text.Encoding.UTF8.GetBytes("runtime: netpollunblock claimed an impossible state\n\0"));
    _b.DefineSymdata(NetpollRecoveredMsg, System.Text.Encoding.UTF8.GetBytes(
      "warning: async-I/O wakeup recovered by the netpoll safety net — the park protocol lost one; "
      + "count is in " + NetpollRecoveredLabel + "\n\0"));
  }

  /// <summary>
  /// Emit every function of the park protocol. Both backends call this; the LABELS are the interface
  /// a platform's readiness plumbing binds to, and there are only four of them:
  ///
  ///   <c>__netpoll_arm(gt)</c>        — before publishing a registration the completer can see.
  ///   <c>__netpoll_commit(gt) -&gt; ok</c> — the last instruction that can still change its mind.
  ///   <c>__netpoll_park_done(gt)</c>  — after the park ends, however it ended.
  ///   <c>__netpoll_unblock(gt) -&gt; gt|0</c> — the completer's whole decision.
  ///
  /// A new platform supplies readiness discovery (epoll_wait, kevent, GetQueuedCompletionStatus) and
  /// calls these four. It writes no state machine of its own.
  /// </summary>
  public void EmitNetpollFunctions() {
    EmitNetpollArm();
    EmitNetpollCommit();
    EmitNetpollParkDone();
    EmitNetpollUnblock();
    EmitNetpollInjectDelay();
    EmitNetpollRecover();
    EmitNetpollInit();
  }

  // ⚠ EVERY READ OF THE PARK WORD IS A LoadAcquire, AND THAT IS NOT DECORATION. Each reader goes on
  // to read something the writer published BEFORE it — the completer publishes io_result_val and
  // status before it claims; the parker's context is saved before ioYielded — and on a weakly
  // ordered machine a control dependency orders a later STORE, never a later LOAD. The CAS supplies
  // the same pairing on its own (arm64's is LDAXR/STLXR, so its release half orders the claimer's
  // prior stores and its acquire half orders the loser's subsequent loads), which is why the old
  // two-word handshake's hand-rolled Dekker fence could be retired with it rather than kept beside it.

  /// <summary>
  /// Panic with <paramref name="msgSymdata"/>. The equivalent of Go's <c>throw</c>: an invariant
  /// this protocol asserts rather than tolerates, so it says which one and stops, instead of
  /// carrying on into a lost wakeup that would be diagnosed weeks later from a wedge.
  /// </summary>
  private void EmitNetpollThrow(string msgSymdata) {
    _b.LeaSymdata(VReg.Arg0, msgSymdata);
    _b.Call("mrt_panic"); // never returns
  }

  /// <summary>
  /// <c>__netpoll_arm(gt)</c> — Go's <c>netpollblock</c> PREPARE loop, and it must be called BEFORE
  /// the registration the completer can see is published (before <c>kevent(EV_ADD)</c>, before the
  /// overlapped <c>ReadFile</c>, before <c>epoll_ctl</c>). From this instruction on, a completer may
  /// claim this GT's wakeup.
  ///
  /// ⚠ DEVIATION 1 — GO'S LOOP DEGENERATES TO ONE CAS HERE, AND THE SELF-DETECT ARM IS GONE.
  /// Go's loop is:
  /// <code>
  ///   for {
  ///     if CAS(pdReady -&gt; pdNil) { return true }   // a notification was already pending
  ///     if CAS(pdNil -&gt; pdWait)  { break }
  ///     if load() != pdReady &amp;&amp; != pdNil { throw("runtime: double wait") }
  ///   }
  /// </code>
  /// The <c>pdReady</c> arm exists because Go's word lives in a per-fd <c>pollDesc</c> that OUTLIVES
  /// the goroutine, so a notification can be sitting in it before anyone parks. Ours lives in the GT
  /// and is reset by <c>__netpoll_park_done</c> at the end of every park, and readiness itself is
  /// carried by <c>status</c>/<c>io_result_val</c> — this word carries ownership and nothing else.
  /// So the state here is always <c>Nil</c>, the first CAS can never succeed, and the loop collapses
  /// to the second CAS plus Go's own throw. THE SELF-DETECT DID NOT DISAPPEAR: it is the
  /// <c>status</c> re-check on the park path, which is a fast path only — the commit CAS is what
  /// decides.
  /// </summary>
  private void EmitNetpollArm() {
    _b.FunctionStart("__netpoll_arm", 1, 0x40);

    var okLabel = UniqueLabel("netpoll_arm_ok");

    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.MovRegImm(VReg.Scratch2, NetpollNil);     // expected
    _b.MovRegImm(VReg.Arg1, NetpollWait);        // desired
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfNonZero(VReg.Scratch3, okLabel);

    // A GT that is already Wait/Parked/Ready is one that armed a registration and never ran
    // __netpoll_park_done — a missing park_done at some submit site, which would otherwise present
    // itself much later as a wakeup delivered to the wrong park.
    EmitNetpollThrow(NetpollDoubleWaitMsg);

    _b.DefineLabel(okLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// <c>__netpoll_commit(gt)</c> -&gt; 1 when the park is COMMITTED, 0 when it must be ABORTED.
  /// Go's <c>netpollblockcommit</c>, and the whole point of the protocol.
  ///
  /// Go runs this as <c>gopark</c>'s <c>unlockf</c>, on the system stack AFTER <c>mcall</c> has
  /// switched off the goroutine's stack, and honours a false return by resuming the goroutine. That
  /// resume mechanism was READ, not assumed — <c>park_m</c>, verbatim:
  /// <code>
  ///   if fn := _g_.m.waitunlockf; fn != nil {
  ///     ok := fn(gp, _g_.m.waitlock)
  ///     ...
  ///     if !ok {
  ///       casgstatus(gp, _Gwaiting, _Grunnable)
  ///       execute(gp, true) // Schedule it back, never returns.
  ///     }
  ///   }
  /// </code>
  /// — i.e. the abort re-enters the goroutine's saved context FROM g0, which is only possible
  /// because the stack switch already happened. (<c>gopark</c>'s doc says the same thing in one
  /// line: "If unlockf returns false, the goroutine is resumed".)
  ///
  /// ⚠ DEVIATION 2 — WE COMMIT BEFORE THE CONTEXT SWITCH, NOT INSIDE IT, AND THE COMPLETER PAYS FOR
  /// IT WITH A BOUNDED SPIN. Go can commit after the stack switch because <c>mcall</c> gives it a
  /// system stack to run <c>unlockf</c> on and <c>execute()</c> to undo the park from there. Our
  /// <c>__gt_context_switch</c> is a naked register save/restore shared by every yield path in the
  /// runtime; there is no g0 to run a commit function on and no re-entry point to resume an aborted
  /// park from. So the commit is the LAST instruction before the switch, the abort is an ordinary
  /// branch taken while we are still running, and the window between "committed" and "context
  /// saved" is handed to the completer as a spin on <c>ioYielded</c> — which is exactly the gate
  /// <c>__io_complete_gt</c> and <c>__gt_process_pending_waiter</c> already stand on, and which only
  /// became sound for THIS path once a committed parker could no longer turn around. Everything
  /// between this CAS and <c>ioYielded = 1</c> is straight-line code with no call and no lock, so
  /// the spin is bounded by a scheduling quantum, never by another green thread's progress.
  /// </summary>
  private void EmitNetpollCommit() {
    _b.FunctionStart("__netpoll_commit", 1, 0x40);

    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.MovRegImm(VReg.Scratch2, NetpollWait);    // expected
    _b.MovRegImm(VReg.Arg1, NetpollParked);      // desired
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);

    // Scratch3 is already 1 on success and 0 on failure, which IS the return value.
    _b.ReturnValue(VReg.Scratch3);
  }

  /// <summary>
  /// <c>__netpoll_park_done(gt)</c> — Go's tail of <c>netpollblock</c>:
  /// <code>
  ///   old := gpp.Swap(pdNil)
  ///   if old &gt; pdWait { throw("runtime: corrupted polldesc") }
  /// </code>
  /// Call it once per park, on EVERY exit from the park — the committed path, the aborted path, and
  /// the paths where the I/O completed synchronously and no park ever happened. Missing one leaves
  /// the word non-<c>Nil</c> and the GT's NEXT <c>__netpoll_arm</c> throws "double wait", which is
  /// the whole reason that throw is worth its four instructions.
  ///
  /// ⚠ DEVIATION 3 — THE VALID SET IS NARROWER THAN GO'S, BECAUSE OUR STATE MACHINE IS. Go accepts
  /// <c>pdNil</c> here as well as <c>pdReady</c>, because a timeout or a <c>Close</c> can reset the
  /// word under a parked goroutine; we have neither, so the only ways a park can end are "a
  /// completer claimed it" (<c>Ready</c>) and "nobody claimed it and we aborted" (<c>Wait</c>).
  /// <c>Parked</c> means we resumed from a committed park that nobody ever claimed — Go's
  /// "corrupted polldesc", exactly. <c>Nil</c> means we are ending a park we never armed. Both are
  /// the same class of bug and get the same throw.
  /// </summary>
  private void EmitNetpollParkDone() {
    _b.FunctionStart("__netpoll_park_done", 1, 0x40);

    var retryLabel = UniqueLabel("netpoll_park_done_retry");
    var okLabel = UniqueLabel("netpoll_park_done_ok");

    // Swap(Nil), spelled as a CAS loop: there is no atomic-exchange primitive in IEmitterBackend and
    // this is not a hot path (once per completed I/O), so a loop over the primitive that DOES exist
    // is better than a fifth atomic every backend would have to implement.
    _b.DefineLabel(retryLabel);
    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, GtOffParkState);
    _b.MovRegImm(VReg.Arg1, NetpollNil);
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, retryLabel);

    _b.CmpRegImm(VReg.Scratch2, NetpollReady);
    _b.JumpIf(Condition.Equal, okLabel);
    _b.CmpRegImm(VReg.Scratch2, NetpollWait);
    _b.JumpIf(Condition.Equal, okLabel);

    EmitNetpollThrow(NetpollCorruptMsg);

    _b.DefineLabel(okLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// <c>__netpoll_unblock(gt)</c> -&gt; the GT to enqueue, or 0 — Go's <c>netpollunblock</c> with
  /// <c>ioready = true</c> (our completers only ever run because an I/O finished; there is no
  /// timeout or close path that unblocks with <c>ioready = false</c>).
  ///
  /// <code>
  ///   for {
  ///     old := gpp.Load()
  ///     if old == pdReady { return nil }
  ///     if old == pdNil &amp;&amp; !ioready { return nil }
  ///     if gpp.CompareAndSwap(old, pdReady) {
  ///       if old == pdWait { return nil }
  ///       return (*g)(old)
  ///     }
  ///   }
  /// </code>
  ///
  /// ⭐ THIS ONE FUNCTION REPLACES THE THREE-WAY GUARD every completer in this runtime used to spell
  /// for itself, and it subsumes all three rather than dropping any:
  ///   (a) <c>stackBase == 0</c> — the main OS thread. It has no schedulable stack, so it never
  ///       commits; a completer finds <c>Wait</c> and declines, and it self-detects in its own await
  ///       loop, exactly as before.
  ///   (b) <c>waiter == P-&gt;currentGt</c> — the waiter is driving this very poll from its own park
  ///       loop. It has not committed either, so again <c>Wait</c>, again decline.
  ///   (c) <c>waiter.ioYielded == 0</c> — "still running, it will self-detect". This is the guard
  ///       that lost wakeups: it could not tell "still running and WILL self-detect" from "still
  ///       running and is about to park", because those two are the same instant. <c>Wait</c> versus
  ///       <c>Parked</c> is exactly that distinction, decided atomically.
  ///
  /// ⚠ DEVIATION — Go's <c>pdNil</c> arm STORES <c>pdReady</c> and returns nil, carrying the
  /// readiness forward for the next <c>netpollblock</c> to consume. We return without touching the
  /// word. Same reason as <see cref="EmitNetpollArm"/>'s: Go's word outlives the goroutine and is
  /// the only place a pending notification could live, whereas ours belongs to the GT and the
  /// readiness is already published in <c>status</c>/<c>io_result_val</c>. Storing <c>Ready</c> into
  /// a <c>Nil</c> word here would leave a stale claim that the GT's NEXT park would trip over.
  /// </summary>
  private void EmitNetpollUnblock() {
    _b.FunctionStart("__netpoll_unblock", 1, 0x40);

    var loopLabel = UniqueLabel("netpoll_unblock_loop");
    var spinLabel = UniqueLabel("netpoll_unblock_spin");
    var enqueueLabel = UniqueLabel("netpoll_unblock_enqueue");
    var noneLabel = UniqueLabel("netpoll_unblock_none");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, GtOffParkState);

    // Already claimed by another completer, or the waiter has already left the park and consumed
    // the readiness itself. Either way there is nothing for us to do.
    _b.CmpRegImm(VReg.Scratch2, NetpollReady);
    _b.JumpIf(Condition.Equal, noneLabel);
    _b.CmpRegImm(VReg.Scratch2, NetpollNil);
    _b.JumpIf(Condition.Equal, noneLabel);

    _b.MovRegImm(VReg.Arg1, NetpollReady);
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, loopLabel);     // lost the race — re-read and decide again

    // Scratch2 still holds the state we claimed FROM: the CAS leaves it alone on both backends.
    _b.CmpRegImm(VReg.Scratch2, NetpollWait);
    _b.JumpIf(Condition.Equal, noneLabel);       // not committed: it will abort its own park
    _b.CmpRegImm(VReg.Scratch2, NetpollParked);
    _b.JumpIf(Condition.Equal, spinLabel);

    // Nil and Ready both returned above, so nothing else can reach here.
    EmitNetpollThrow(NetpollUnblockStateMsg);

    // The enqueue is ours. Wait for the context save to finish before handing this GT to another M:
    // ioYielded goes to 1 inside __gt_context_switch, after the callee-saved block and gt.sp are
    // stored, and enqueueing before that lets a second M resume the GT onto a stale SP.
    //
    // ⭐ WHY THIS SPIN TERMINATES AND THE OLD DESIGN'S COULD NOT. We claimed `Parked`, so the waiter
    // has already committed, and everything from its commit CAS to `ioYielded = 1` is straight-line
    // code — no call, no lock, no branch back. It cannot decide to keep running, which is precisely
    // what it COULD do before this word existed, and precisely why the completer had to guess with a
    // non-blocking snapshot (guard (c)) instead of waiting. The bound is a scheduling quantum, the
    // same bound __io_complete_gt_spin and __gt_ppw_spin already accept.
    _b.DefineLabel(spinLabel);
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadAcquire(VReg.Scratch0, VReg.Scratch1, GtOffIoYielded);
    _b.JumpIfNonZero(VReg.Scratch0, enqueueLabel);
    _b.SpinHint();
    _b.Jump(spinLabel);

    _b.DefineLabel(enqueueLabel);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(noneLabel);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>
  /// <c>__netpoll_inject_delay()</c> — FAULT INJECTION, and the only instrument that can discriminate
  /// this protocol from the one it replaces.
  ///
  /// ⭐ WHY IT IS EMITTED CODE AND NOT AN ARGUMENT. The lost wakeup this protocol closes fires about
  /// once in 14,000 suite runs. At that rate a green suite is not evidence, five hundred clean runs
  /// are not evidence, and neither is a careful reading — the previous frequent bug in this same
  /// handshake was fixed by a method (reproduce, capture, probe) that is simply unavailable here.
  /// So the window is not measured, it is made ENORMOUS on demand: called at the exact instruction
  /// between the last self-detect and the commit CAS, a few milliseconds here turns "an admissible
  /// interleaving" into "every run". The acceptance for this rung is that the OLD protocol wedges
  /// under injection and the new one does not.
  ///
  /// It SHIPS rather than hiding behind a build flag, deliberately: unset, it costs one
  /// predictable-not-taken load and branch per I/O park, and it is the only way a future change to
  /// this handshake can be shown not to have reopened the window. A build flag would have cost the
  /// same and been unavailable in the binary anyone actually runs.
  /// </summary>
  private void EmitNetpollInjectDelay() {
    _b.FunctionStart("__netpoll_inject_delay", 0, 0x40);

    var doneLabel = UniqueLabel("netpoll_inject_done");

    _b.LoadGlobal(VReg.Scratch0, NetpollParkDelayMsLabel);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.OsSleepMillis(VReg.Arg0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// <c>__netpoll_init()</c> — read the injection delay out of the environment once, at scheduler
  /// init, so the hot path is a global load rather than a <c>getenv</c>.
  /// </summary>
  private void EmitNetpollInit() {
    _b.FunctionStart("__netpoll_init", 0, 0x60);

    // Slots 4-5 are the env read's scratch buffer on Windows and unused on POSIX, matching the
    // scratchSlot convention GetCurrentTimeMs and friends already use.
    _b.ReadEnvUnsigned(VReg.Scratch0, NetpollParkDelayEnvSymdata, 4);
    _b.StoreGlobal(NetpollParkDelayMsLabel, VReg.Scratch0);

    _b.FunctionEnd();
  }

  /// <summary>
  /// <c>__netpoll_recover()</c> — THE RECOVERY NET, Go's sysmon re-poll in the shape our state
  /// machine makes possible.
  ///
  /// Go's sysmon re-runs the netpoller when it has not run recently, so a wakeup that went missing
  /// becomes a hiccup rather than a dead run. We cannot re-run the poll: a kqueue <c>EV_ONESHOT</c>
  /// registration is spent once reaped, and an IOCP packet is dequeued once. But this word makes the
  /// failure DIRECTLY VISIBLE instead, which the old two-word handshake never could:
  ///
  ///     parkState == Parked  AND  status != waiting
  ///
  /// means a completer published readiness and did NOT claim the wakeup — a GT committed to a park
  /// that nobody owns. Under the protocol above that is unreachable, which is the point: the net is
  /// a REGRESSION DETECTOR that also happens to rescue the run. It claims the wakeup itself with the
  /// same CAS a real completer would use, so it can never double-schedule against one.
  ///
  /// ⚠ A CANDIDATE MUST BE SEEN TWICE, A SCAN INTERVAL APART, and that is not caution — one sighting
  /// is genuinely ambiguous. A real completer publishes <c>status = ready</c> and THEN claims, so for
  /// the handful of instructions in between, a perfectly healthy completion looks exactly like the
  /// condition above. Acting on one sighting would still be SAFE (the CAS makes the net and the
  /// completer mutually exclusive, so the GT is enqueued exactly once either way) — but it would
  /// increment <c>__netpoll_recovered</c> for a completion that was never lost, and a counter that
  /// cries wolf is worth nothing as a regression detector. Ten milliseconds of persistence is
  /// several orders of magnitude wider than that window and far narrower than a hang.
  ///
  /// ⚠ IT IS ALSO DELIBERATELY NOT A SPIN. A candidate whose context is not yet saved
  /// (<c>ioYielded == 0</c>) is LEFT ALONE for the next scan rather than waited on: this runs inside
  /// scheduler idle turns, the condition it looks for is by construction impossible, and a safety
  /// net that can block the scheduler is worse than the hazard. For the same reason it takes at most
  /// ONE candidate per scan and does its enqueue after releasing the all-threads lock.
  /// </summary>
  private void EmitNetpollRecover() {
    _b.FunctionStart("__netpoll_recover", 0, 0x60);

    const int slotCandidate = 0;
    const int slotCursor = 1;
    var retLabel = UniqueLabel("netpoll_recover_ret");
    var walkLabel = UniqueLabel("netpoll_recover_walk");
    var nextLabel = UniqueLabel("netpoll_recover_next");
    var unlockLabel = UniqueLabel("netpoll_recover_unlock");
    var confirmLabel = UniqueLabel("netpoll_recover_confirm");
    var claimLabel = UniqueLabel("netpoll_recover_claim");
    var enqueueLabel = UniqueLabel("netpoll_recover_enqueue");

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotCandidate, VReg.Scratch0);

    // Time gate. The walk is O(live GTs) and every scheduler idle turn calls this, so without the
    // gate the net would cost more than the hazard. It is also what makes the two-sighting rule
    // mean "still unclaimed 10 ms later". Slots 4-5 are GetCurrentTimeMs's out-parameter buffer.
    _b.GetCurrentTimeMs(VReg.Scratch0, 4);
    _b.LoadGlobal(VReg.Scratch1, NetpollLastScanMsLabel);
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch0);
    _b.SubRegReg(VReg.Scratch2, VReg.Scratch1);
    _b.CmpRegImm(VReg.Scratch2, NetpollRecoverIntervalMs);
    _b.JumpIf(Condition.Below, retLabel);
    _b.StoreGlobal(NetpollLastScanMsLabel, VReg.Scratch0);

    // The all-threads list is mutated under this lock by __gt_spawn and the completion trampoline,
    // and a GT unlinked mid-walk would send the cursor into freed memory. The debug agent walks it
    // unlocked, but it does so from a stopped world; we are running alongside every other M.
    _b.AllThreadsLockAcquire();
    _b.LoadGlobal(VReg.Scratch0, "__gt_all_head");
    _b.StoreLocal(slotCursor, VReg.Scratch0);

    _b.DefineLabel(walkLabel);
    _b.LoadLocal(VReg.Scratch0, slotCursor);
    _b.JumpIfZero(VReg.Scratch0, unlockLabel);

    // Acquire, because the `status` load below must not be satisfied out of a line older than this
    // one: on a weakly ordered machine a control dependency orders a later store, never a later load.
    _b.LoadAcquire(VReg.Scratch1, VReg.Scratch0, GtOffParkState);
    _b.CmpRegImm(VReg.Scratch1, NetpollParked);
    _b.JumpIf(Condition.NotEqual, nextLabel);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffStatus);
    _b.CmpRegImm(VReg.Scratch1, GtStatusWaiting);
    _b.JumpIf(Condition.Equal, nextLabel);
    // Committed to a park, and something has already declared the I/O finished. Nobody owns it.
    _b.StoreLocal(slotCandidate, VReg.Scratch0);
    _b.Jump(unlockLabel);

    _b.DefineLabel(nextLabel);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, GtOffAllNext);
    _b.StoreLocal(slotCursor, VReg.Scratch0);
    _b.Jump(walkLabel);

    _b.DefineLabel(unlockLabel);
    _b.AllThreadsLockRelease();

    // Publish this scan's candidate (possibly none) and act only if the PREVIOUS scan saw the same
    // GT. A completer's publish-then-claim window cannot survive a scan interval.
    _b.LoadLocal(VReg.Scratch0, slotCandidate);
    _b.LoadGlobal(VReg.Scratch1, NetpollSuspectLabel);
    _b.StoreGlobal(NetpollSuspectLabel, VReg.Scratch0);
    _b.JumpIfZero(VReg.Scratch0, retLabel);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.Equal, confirmLabel);
    _b.Jump(retLabel);

    _b.DefineLabel(confirmLabel);
    // Only rescue a GT whose context is actually saved; otherwise leave it for the next scan, which
    // will still find it suspected.
    _b.LoadLocal(VReg.Scratch0, slotCandidate);
    _b.LoadAcquire(VReg.Scratch1, VReg.Scratch0, GtOffIoYielded);
    _b.JumpIfNonZero(VReg.Scratch1, claimLabel);
    _b.Jump(retLabel);

    _b.DefineLabel(claimLabel);
    _b.LoadLocal(VReg.Scratch1, slotCandidate);
    _b.MovRegImm(VReg.Scratch2, NetpollParked);
    _b.MovRegImm(VReg.Arg1, NetpollReady);
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, retLabel);      // a real completer got there first: nothing to fix

    _b.LoadGlobal(VReg.Scratch0, NetpollRecoveredLabel);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal(NetpollRecoveredLabel, VReg.Scratch0);

    // Say so, once, on the first rescue. See NetpollRecoveredMsg.
    _b.CmpRegImm(VReg.Scratch0, 1);
    _b.JumpIf(Condition.NotEqual, enqueueLabel);
    _b.LeaSymdata(VReg.Arg0, NetpollRecoveredMsg);
    _b.Call(_b.WriteStderrLabel);

    _b.DefineLabel(enqueueLabel);
    _b.LoadLocal(VReg.Arg0, slotCandidate);
    _b.Call("__gt_enqueue");

    _b.DefineLabel(retLabel);
    _b.FunctionEnd();
  }
}
