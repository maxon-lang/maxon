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
/// The completer does not test a flag and guess; it CASes to <c>Claiming</c>, and what it finds
/// tells it exactly what to do: <c>Wait</c> means the waiter has not committed and will abort
/// itself, <c>Parked</c> means the enqueue is the completer's job. Exactly one side acts, always.
///
/// ⭐⭐⭐ THE RELEASE RULE, WHICH IS WHAT MAKES THAT SINGLE OPERATION SINGLE: <b>a waiter that has
/// armed the park word may learn its I/O completed ONLY through that word</b> — never through
/// <c>status</c>, never through <c>io_result_val</c>, never through anything a completer writes into
/// the GT. Go has no choice about this and neither do we: in <c>netpoll.go</c> a goroutine learns
/// readiness from <c>pd.rg</c> alone, because the completer's transition to <c>pdReady</c> IS the
/// publication and there is no other channel to observe.
///
/// ⚠ WE BRIEFLY HAD ANOTHER CHANNEL, AND IT COST A WAKEUP. Every completer here USED TO publish
/// <c>io_result_val</c> and <c>status = ready</c> and THEN claim the word, so for the handful of
/// instructions in between, <c>status</c> said "done" while the word was still unclaimed. A waiter
/// that self-detected on <c>status</c> in that gap left the park and ran <c>__netpoll_park_done</c>,
/// which accepts <c>Wait</c> and swaps the word to <c>Nil</c> — RELEASING a word a completer was
/// still in flight toward. It then armed its NEXT I/O, the late completer's CAS landed on THAT
/// park's <c>Wait</c>, marked it <c>Ready</c> and reported "nothing to wake", and the next park's
/// commit CAS failed against a completion that had never happened: a read on an fd nobody signalled.
/// Reproducible on demand — <c>MAXON_GT_CLAIM_DELAY_MS=5</c> took the park driver from 5/5 clean to
/// 0/5, every failure leaking and four in five tripping the recovery net below.
///
/// ⭐⭐⭐⭐ SO THE ORDER IS INVERTED: <b>CLAIM, THEN PUBLISH, THEN RELEASE.</b> The release rule alone
/// silenced the waiter, but it left the completer's ownership INVISIBLE — the word said <c>Parked</c>
/// for the whole of a healthy publish, which is indistinguishable from a wakeup nobody owns, and the
/// recovery net below (whose suspicion condition IS that window) therefore raced live completers and
/// re-created the very defect one level up: it rescued a merely-SLOW completer, whose late claim then
/// landed on the waiter's NEXT park. Three steps, and every one of them is load-bearing:
///
///   1. CLAIM   — <c>__netpoll_claim</c> CASes <c>Wait|Parked -&gt; Claiming</c>. From here the
///                completer OWNS the GT's result fields, and the word says so.
///   2. PUBLISH — the caller writes <c>io_result_val</c> / <c>io_result_len</c> /
///                <c>io_error_code</c> / <c>status</c>. It writes them ONLY because step 1 won.
///   3. RELEASE — <c>__netpoll_claim_done</c> store-releases <c>Ready</c>, and only now may a waiter
///                leave the park.
///
/// ⚠ STEP 3 IS WHY THIS IS A FIFTH STATE AND NOT A REORDERING. If step 1 stored <c>Ready</c>
/// directly, a waiter claimed from <c>Wait</c> — which is STILL RUNNING — would see
/// <c>__netpoll_woken() == 1</c> at once and leave the park before step 2 stored anything, and step 2
/// would then write into a GT that had moved on to its next operation. <c>Claiming</c> is the state
/// that says "owned, not yet publishable"; see <see cref="GtLayout.NetpollClaiming"/>.
///
/// ⭐ AND THE INVARIANT IT BUYS, WHICH IS THE WHOLE POINT: <b>readiness is published ONLY while the
/// word says a completer owns it.</b> So <c>Parked</c> + published-readiness — the recovery net's
/// suspicion condition — is unreachable during a healthy completion, by construction rather than by
/// timing, and a net hit is a real regression again.
///
/// ⚠ THE COMPLETER NEVER BLOCKS ON THE DECISION, and that is load-bearing rather than incidental.
/// On macOS the kqueue drain (<c>__io_poll_kqueue</c>) runs INLINE inside every scheduler idle turn,
/// so a decision that waited on another thread would stall every other pending completion — the
/// livelock <c>__io_op_done</c>'s own comment warns about. <see cref="EmitNetpollClaim"/> is a pure
/// CAS loop that waits for nobody, which is precisely why the protocol fits at a site where a spin
/// gate does not. (It does spin AFTER claiming <c>Parked</c>, for <c>ioYielded</c> — see
/// <see cref="EmitNetpollClaimDone"/> for why that one terminates and the old one could not.)
///
/// ⚠ DEVIATIONS FROM netpoll.go ARE MARKED "DEVIATION" AT THEIR SITE. There are four, and each has
/// a reason recorded next to it. A partial port of Go has already cost this project a real bug (the
/// green-thread stack took Go's <c>_StackMin</c> and the 928-byte guard but dropped
/// <c>_StackSystem</c>, so a CPU fault inside <c>async</c> dies with the panic lost), so silent
/// divergence is exactly the failure this file exists to prevent.
/// </summary>
public partial class RuntimeEmitter {

  // ---- Globals ----

  /// <summary>
  /// How many wakeups the recovery net has had to rescue. THE POINT OF THE NET IS THAT THIS STAYS 0:
  /// under a correct protocol no GT can ever be readied-but-unclaimed, so a non-zero value is not a
  /// statistic, it is a bug report. It is a counter rather than a log line because the net runs from
  /// scheduler idle turns where an I/O of its own would be absurd; <c>__netpoll_recovered</c> is
  /// readable from a debugger, from the debug agent's global reads, and by a test binary.
  /// </summary>
  public const string NetpollRecoveredLabel = "__netpoll_recovered";

  /// <summary>
  /// Coarse-clock stamp of the last recovery scan, so the walk is time-gated — and, because it is
  /// advanced by a CAS rather than a store, the scan's MUTEX as well as its clock. See
  /// <see cref="EmitNetpollRecover"/>: without the atomicity two Ms scan in the same instant and the
  /// two-sighting rule stops meaning "a scan interval apart".
  /// </summary>
  private const string NetpollLastScanMsLabel = "__netpoll_last_scan_ms";

  /// <summary>
  /// The GT the PREVIOUS recovery scan suspected. A candidate must be seen twice, a scan interval
  /// apart, before the net acts on it — see <see cref="EmitNetpollRecover"/> for why one sighting
  /// is not enough.
  ///
  /// ⚠ THIS HOLDS A GT POINTER ACROSS 10 ms WITH NO LOCK AND NO OWNERSHIP, AND THAT IS SAFE FOR ONE
  /// REASON ONLY: <b>the stale value is COMPARED, never DEREFERENCED.</b> The next scan re-walks the
  /// live list under <c>__sched_all_lock</c> and produces its own candidate; this word's whole
  /// contribution is the equality test between the two. Every load through the pointer —
  /// <c>ioYielded</c>, the claiming CAS, the enqueue — is made on THAT walk's candidate, which the
  /// lock proves is live. Nothing here rests on a <c>Parked</c> GT being un-freeable, which would be
  /// the weaker and less obviously true argument; it rests on the pointer never being followed.
  ///
  /// The residual is a false POSITIVE, not a use-after-free: a GT can be freed and its address
  /// recycled by <c>__gt_spawn</c>'s free list, and the recycled GT can be a genuine two-sighting
  /// candidate on the second sighting while the first sighting was some other thread entirely. That
  /// is one spurious sighting out of the two the test needs, so it can only ever let a real
  /// unclaimed-<c>Parked</c> GT be rescued a scan earlier than it should be — and an early rescue is
  /// now harmless, which it was not before <c>Claiming</c> existed. The net's CAS expects
  /// <c>Parked</c>, and a completer that has begun on this GT has already moved the word off it, so
  /// the net and any live completer are mutually exclusive for the completer's WHOLE completion
  /// rather than only for its final store. Nothing is double-scheduled and no late claim survives to
  /// land on the GT's next park.
  /// </summary>
  private const string NetpollSuspectLabel = "__netpoll_suspect";

  /// <summary>
  /// FAULT INJECTION on the PARKER's side, in milliseconds; 0 = off. Widens the gap between the
  /// waiter's last self-detect and its commit CAS. See <see cref="EmitNetpollDelayFn"/>.
  /// </summary>
  private const string NetpollParkDelayMsLabel = "__netpoll_park_delay_ms";

  private const string NetpollParkDelayEnvSymdata = "__netpoll_park_delay_env";

  /// <summary>
  /// The environment variable that arms the fault injection. Named in one place because the harness
  /// in <c>scripts/</c> and the emitted code have to agree and nothing else would make them.
  /// </summary>
  public const string NetpollParkDelayEnvName = "MAXON_GT_PARK_DELAY_MS";

  /// <summary>
  /// FAULT INJECTION on the COMPLETER's side, in milliseconds; 0 = off. It widens the interval in
  /// which the completer OWNS the park word but has not yet released it — and specifically the TAIL
  /// of that interval, after the caller's publish and before
  /// <see cref="EmitNetpollClaimDone"/>'s store-release.
  ///
  /// ⚠ THE TAIL, NOT THE HEAD, AND THE DIFFERENCE IS THE WHOLE VALUE OF THE ARM. Stretching the head
  /// (right after the claiming CAS) produces a window in which `status` still says `waiting`, so
  /// every observer declines for a reason that predates the fifth state — a green run there proves
  /// nothing this rung changed. See the comment at the call site.
  ///
  /// ⭐ WHY A SECOND KNOB EXISTS: A HANDSHAKE HAS TWO SIDES AND ONLY ONE OF THEM WAS INSTRUMENTED.
  /// <see cref="NetpollParkDelayMsLabel"/> widens the parker's window; nothing widened the
  /// COMPLETER's. An instrument that can only stretch one side can only find bugs on one side, and
  /// this one found the other side's — twice. First, with the waiter still self-detecting on
  /// <c>status</c>, 5 ms took the park driver from 5/5 clean to 0/5. Then, with that closed, 5 ms
  /// took it to 1/10 by a DIFFERENT route: the recovery net could not tell a slow completer from a
  /// lost wakeup, so it raced live ones (see <see cref="EmitNetpollRecover"/>).
  ///
  /// ⚠ IT IS THE SAME WINDOW, RE-AIMED, NOT A DIFFERENT ONE. It used to be documented as the gap
  /// between "I published <c>status = ready</c>" and "I claimed the park word", because that was the
  /// order completers ran in. Claim-then-publish did not remove that interval, it INVERTED it: the
  /// instructions between a completer's first and last act on a GT are the same instructions, and
  /// what changed is that the word now says who owns them. Stretching it is still exactly "hold a
  /// completer mid-completion and see what the rest of the runtime does".
  /// </summary>
  private const string NetpollClaimDelayMsLabel = "__netpoll_claim_delay_ms";

  private const string NetpollClaimDelayEnvSymdata = "__netpoll_claim_delay_env";

  /// <summary>Environment variable arming the completer-side injection. See above.</summary>
  public const string NetpollClaimDelayEnvName = "MAXON_GT_CLAIM_DELAY_MS";

  /// <summary>
  /// Minimum gap between recovery scans. Go's sysmon re-polls the netpoller when it has not run for
  /// <c>10e6</c> ns; this is the same 10 ms, on the coarse tick the scheduler already reads.
  /// </summary>
  private const int NetpollRecoverIntervalMs = 10;

  // ---- Panic messages. Go's spellings, because they name Go's invariants. ----

  private const string NetpollDoubleWaitMsg = "__netpoll_msg_double_wait";
  private const string NetpollCorruptMsg = "__netpoll_msg_corrupt";
  private const string NetpollClaimStateMsg = "__netpoll_msg_claim_state";

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
    _b.DefineGlobal(NetpollClaimDelayMsLabel, 8, 0);

    _b.DefineSymdata(NetpollParkDelayEnvSymdata,
      System.Text.Encoding.UTF8.GetBytes(NetpollParkDelayEnvName + "\0"));
    _b.DefineSymdata(NetpollClaimDelayEnvSymdata,
      System.Text.Encoding.UTF8.GetBytes(NetpollClaimDelayEnvName + "\0"));
    _b.DefineSymdata(NetpollDoubleWaitMsg,
      System.Text.Encoding.UTF8.GetBytes("runtime: double wait\n\0"));
    _b.DefineSymdata(NetpollCorruptMsg,
      System.Text.Encoding.UTF8.GetBytes("runtime: corrupted parkstate\n\0"));
    _b.DefineSymdata(NetpollClaimStateMsg, System.Text.Encoding.UTF8.GetBytes(
      "runtime: netpoll claim_done given a state nothing can be claimed from\n\0"));
    _b.DefineSymdata(NetpollRecoveredMsg, System.Text.Encoding.UTF8.GetBytes(
      "warning: async-I/O wakeup recovered by the netpoll safety net — the park protocol lost one; "
      + "count is in " + NetpollRecoveredLabel + "\n\0"));
  }

  /// <summary>
  /// Emit every function of the park protocol. Both backends call this; the LABELS are the interface
  /// a platform's readiness plumbing binds to, and there are only six of them:
  ///
  ///   <c>__netpoll_arm(gt)</c>        — before publishing a registration the completer can see.
  ///   <c>__netpoll_woken(gt) -&gt; 0|1</c> — "has a completer CLAIMED AND PUBLISHED my wakeup?", the
  ///                                     ONLY question a waiter that armed is allowed to ask.
  ///   <c>__netpoll_commit(gt) -&gt; ok</c> — the last instruction that can still change its mind.
  ///   <c>__netpoll_park_done(gt)</c>  — after the park ends, however it ended.
  ///   <c>__netpoll_claim(gt) -&gt; 0|from</c> — the completer's decision, taken BEFORE it publishes.
  ///   <c>__netpoll_claim_done(gt, from) -&gt; gt|0</c> — publish over, release the word and say who
  ///                                     owes the enqueue.
  ///
  /// A new platform supplies readiness discovery (epoll_wait, kevent, GetQueuedCompletionStatus) and
  /// calls these six. It writes no state machine of its own.
  ///
  /// ⚠ THE LAST TWO EXIST BECAUSE DISCOVERY AND OWNERSHIP ARE DIFFERENT QUESTIONS AND EVERY PLATFORM
  /// CONFLATES THEM IF LET. A backend always has some earlier, cheaper-looking sign that the I/O
  /// finished — <c>status</c> here, a dequeued IOCP packet, a reaped kevent — and acting on THAT is
  /// what reopens the window in the class comment. So the question is a label, not a load: a
  /// platform that discovers readiness some other way must still ask it this way, and must ask it
  /// BEFORE it writes anything into the GT.
  ///
  /// ⚠ WHY OWNERSHIP IS TWO CALLS AND NOT ONE, given that one call taking
  /// <c>(gt, resultVal, resultLen, errorCode)</c> and owning the publish too would make the ordering
  /// impossible to get wrong — the shape this was weighed against. Because the CONTENT of
  /// the publish is not the protocol's business and differs per site: <c>__io_poll_kqueue</c> writes
  /// two fields, <c>__io_complete_gt</c> three, and <c>__io_check_completions</c> three copied out
  /// of a queued <c>IoCompletion</c>. A single signature wide enough for all of them would force the
  /// kqueue site to supply an <c>io_result_len</c> it does not have — turning a field it deliberately
  /// leaves alone into one it zeroes — so the union of every site's fields would become the protocol's
  /// API. The seam is therefore ORDERING (protocol) versus CONTENT (site), which is also why the
  /// backends' publishing code is untouched by this design.
  /// </summary>
  public void EmitNetpollFunctions() {
    EmitNetpollArm();
    EmitNetpollWoken();
    EmitNetpollCommit();
    EmitNetpollParkDone();
    EmitNetpollClaim();
    EmitNetpollClaimDone();
    EmitNetpollDelayFn(NetpollParkDelayFn, NetpollParkDelayMsLabel);
    EmitNetpollDelayFn(NetpollClaimDelayFn, NetpollClaimDelayMsLabel);
    EmitNetpollRecover();
    EmitNetpollInit();
  }

  // ⚠ EVERY READ OF THE PARK WORD IS A LoadAcquire, AND THAT IS NOT DECORATION. Each reader goes on
  // to read something the writer stored BEFORE it — the completer writes io_result_val and status
  // before it releases the word; the parker's context is saved before ioYielded — and on a weakly
  // ordered machine a control dependency orders a later STORE, never a later LOAD. Two writers pair
  // with those acquires and both are genuine release operations on this one location:
  // __netpoll_claim_done's StoreRelease (arm64 STLR), which is what publishes the results, and every
  // AtomicCAS on the word (arm64 LDAXR/STLXR, whose release half orders the claimer's prior stores
  // and whose acquire half orders the loser's subsequent loads). That is why the old two-word
  // handshake's hand-rolled Dekker fence could be retired with it rather than kept beside it.

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
  /// and is reset by <c>__netpoll_park_done</c> at the end of every park, so the state here is
  /// always <c>Nil</c>, the first CAS can never succeed, and the loop collapses to the second CAS
  /// plus Go's own throw.
  ///
  /// THE SELF-DETECT DID NOT DISAPPEAR — it moved to <see cref="EmitNetpollWoken"/>, which is the
  /// same question asked of the same word. It used to be a <c>status</c> re-check on the park path,
  /// justified as "a fast path only"; that justification was wrong, because a fast path that can
  /// RELEASE the word is not a fast path, it is a second claimant. See the class comment.
  /// </summary>
  private void EmitNetpollArm() {
    _b.FunctionStart("__netpoll_arm", 1, 0x40);

    var okLabel = UniqueLabel("netpoll_arm_ok");

    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.MovRegImm(VReg.Scratch2, NetpollNil);     // expected
    _b.MovRegImm(VReg.Arg1, NetpollWait);        // desired
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfNonZero(VReg.Scratch3, okLabel);

    // Any state but Nil is a GT that armed a registration and never ran __netpoll_park_done — a
    // missing park_done at some submit site, or (for Claiming) a completer that took the wakeup and
    // never released it. Either would otherwise present itself much later as a wakeup delivered to
    // the wrong park.
    EmitNetpollThrow(NetpollDoubleWaitMsg);

    _b.DefineLabel(okLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// <c>__netpoll_woken(gt)</c> -&gt; 1 when a completer has claimed this GT's wakeup AND FINISHED
  /// PUBLISHING it, else 0.
  ///
  /// ⭐ THE ONE CHANNEL. A waiter that has run <see cref="EmitNetpollArm"/> asks THIS and nothing
  /// else — not <c>status</c>, which its completer writes while the word says <c>Claiming</c>, i.e.
  /// while the rest of the result is still going in. See the class comment for what a waiter that
  /// leaves the park inside that window goes on to do to the NEXT park's word. There is no CAS here
  /// and no side effect: this asks who owns the wakeup, it does not take it.
  ///
  /// ⚠ IT TESTS <c>== Ready</c> AND MUST NOT BE LOOSENED TO "ANYTHING A COMPLETER HAS TOUCHED".
  /// <c>Claiming</c> answers 0 precisely because it means "owned, not yet publishable": a waiter that
  /// left the park on it would race the very stores it is about to read.
  ///
  /// ⚠ THE <c>LoadAcquire</c> IS LOAD-BEARING, NOT HOUSE STYLE. It is the acquire half that pairs
  /// with <see cref="EmitNetpollClaimDone"/>'s <c>StoreRelease</c> of <c>Ready</c> (arm64 STLR), and
  /// it is what orders the completer's prior <c>io_result_val</c> / <c>io_error_code</c> /
  /// <c>status</c> stores before the LOADS every caller makes of them once this returns 1 — which is
  /// precisely why the release is the LAST thing a completer does. That pairing is STRONGER than
  /// what it replaces — a plain <c>status</c> load with a hand-rolled <c>DMB ISH</c> after it —
  /// because it is an acquire on the very word the completer released, rather than a fence hoping
  /// to stand in for one.
  /// </summary>
  private void EmitNetpollWoken() {
    _b.FunctionStart("__netpoll_woken", 1, 0x40);

    var wokenLabel = UniqueLabel("netpoll_woken_yes");

    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, GtOffParkState);
    _b.CmpRegImm(VReg.Scratch2, NetpollReady);
    _b.JumpIf(Condition.Equal, wokenLabel);

    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(wokenLabel);
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>
  /// <c>__netpoll_commit(gt)</c> -&gt; 0 when the park must be ABORTED, non-zero when the caller may
  /// proceed to its context switch. Go's <c>netpollblockcommit</c>, and the whole point of the
  /// protocol.
  ///
  /// ⚠ THE NON-ZERO RETURN HAS TWO MEANINGS AND THE CALLER NEEDS NEITHER, WHICH IS WHY THIS IS
  /// SPELLED OUT RATHER THAN LEFT TO THE NAME. A GT with a schedulable stack that wins the CAS is
  /// <c>Parked</c>: it is COMMITTED, a completer that claims it owns the enqueue, and it must not
  /// come back round the park loop. A GT with <c>stackBase == 0</c> — the main OS thread, which has
  /// no schedulable stack and is never enqueued by anything — is NOT committed and its word stays
  /// <c>Wait</c>: it may still switch away, but it resumes by self-detecting through
  /// <see cref="EmitNetpollWoken"/>, and a completer that claims its <c>Wait</c> correctly declines
  /// the enqueue. Both answers are "go on", so both are non-zero; they are different STATES, and a
  /// caller that ever needs to tell them apart must ask the word, not this return value.
  ///
  /// ⚠ A ZERO RETURN MEANS "A COMPLETER HAS TAKEN THE WAKEUP", NOT "THE RESULTS ARE THERE", AND THAT
  /// PUTS AN OBLIGATION ON THE ABORT PATH. The CAS fails against <c>Claiming</c> exactly as it fails
  /// against <c>Ready</c>, and <c>Claiming</c> means the completer is still writing
  /// <c>io_result_val</c>. So every abort must reach <see cref="EmitNetpollParkDone"/> — which waits
  /// the state out — BEFORE it reads a result field. Both backends satisfy this structurally by
  /// aborting into the same single-exit resume path the committed park uses, and that is the reason
  /// to keep it single-exit.
  ///
  /// ⭐ THAT GUARD LIVES HERE BECAUSE IT IS ONE RULE AND WAS SPELLED TWICE. arm64 tested
  /// <c>stackBase == 0</c> and branched around the commit; x86 tested
  /// <c>currentGt == &amp;P-&gt;mainThread</c>, five times, in five submit families — the same
  /// predicate in two spellings, which is the shape every drift in this handshake has taken so far.
  /// (<c>__gt_process_pending_waiter</c> already spelled it <c>stackBase == 0</c> too, and said so:
  /// "Check if waiter is a mainThread (stackBase == 0)".) x86's five tests do NOT disappear, because
  /// they answer a second question this cannot — "can I context-switch away at all, or would that be
  /// a self-switch?" — but the commit rule is no longer one of the things they carry.
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
  /// <c>__gt_process_pending_waiter</c> already stands on for the await hand-off, and which only
  /// became sound for THIS path once a committed parker could no longer turn around. Everything
  /// between this CAS and <c>ioYielded = 1</c> is straight-line code with no call and no lock, so
  /// the spin is bounded by a scheduling quantum, never by another green thread's progress. That is
  /// the same bound <c>__gt_ppw_spin</c> already accepts for the await hand-off, and the one
  /// <c>__io_op_done</c> deliberately refuses to take (it snapshots instead of spinning, because it
  /// runs inside the loop that drives I/O).
  /// </summary>
  private void EmitNetpollCommit() {
    _b.FunctionStart("__netpoll_commit", 1, 0x40);

    var noStackLabel = UniqueLabel("netpoll_commit_no_stack");

    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, GtOffStackBase);
    _b.JumpIfZero(VReg.Scratch2, noStackLabel);

    _b.MovRegImm(VReg.Scratch2, NetpollWait);    // expected
    _b.MovRegImm(VReg.Arg1, NetpollParked);      // desired
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);

    // Scratch3 is already 1 on success and 0 on failure, which IS the return value.
    _b.ReturnValue(VReg.Scratch3);

    // No schedulable stack: leave the word at `Wait` so a completer declines the enqueue, and tell
    // the caller to go on anyway. See the two-meanings note above.
    _b.DefineLabel(noStackLabel);
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);
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
  ///
  /// ⚠⚠ <c>Claiming</c> IS NEITHER VALID NOR INVALID — IT IS NOT YET AN ANSWER, SO THIS WAITS IT OUT.
  /// A completer that claimed our <c>Wait</c> is mid-publish; releasing the word to <c>Nil</c>
  /// underneath it is EXACTLY the bug in the class comment, with this function in the role it played
  /// there — the word would go back to <c>Nil</c>, the GT would arm its next I/O, and the completer's
  /// store-release of <c>Ready</c> would land on THAT park. So the swap is not attempted until the
  /// word has left <c>Claiming</c>. (Reachable on the ordinary abort path: our commit CAS
  /// <c>Wait -&gt; Parked</c> fails precisely because a completer has taken <c>Wait -&gt; Claiming</c>,
  /// and the abort runs straight here.)
  ///
  /// The spin is bounded for the same reason <see cref="EmitNetpollClaimDone"/>'s is: everything
  /// between a claim and its release is straight-line stores in the completer, so the bound is a
  /// scheduling quantum — plus <see cref="NetpollClaimDelayMsLabel"/> when the fault injection is
  /// armed, which is a deliberate sleep and bounded by construction. It cannot self-deadlock either:
  /// claim → publish → release is one straight-line region inside a single call frame, so no thread
  /// can be inside it and in this function at once, and the completer waited on is always another M.
  /// </summary>
  private void EmitNetpollParkDone() {
    _b.FunctionStart("__netpoll_park_done", 1, 0x40);

    var retryLabel = UniqueLabel("netpoll_park_done_retry");
    var swapLabel = UniqueLabel("netpoll_park_done_swap");
    var okLabel = UniqueLabel("netpoll_park_done_ok");

    // Swap(Nil), spelled as a CAS loop: there is no atomic-exchange primitive in IEmitterBackend and
    // this is not a hot path (once per completed I/O), so a loop over the primitive that DOES exist
    // is better than a fifth atomic every backend would have to implement.
    _b.DefineLabel(retryLabel);
    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, GtOffParkState);
    _b.CmpRegImm(VReg.Scratch2, NetpollClaiming);
    _b.JumpIf(Condition.NotEqual, swapLabel);

    // A completer owns the word and has not published yet. Let it finish; do not take the word off it.
    _b.SpinHint();
    _b.Jump(retryLabel);

    _b.DefineLabel(swapLabel);
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
  /// <c>__netpoll_claim(gt)</c> -&gt; the state the wakeup was claimed FROM (<c>Wait</c> or
  /// <c>Parked</c>), or 0 when it was not ours to claim. STEP 1 of the completer's three, and it must
  /// run BEFORE the caller writes a single one of the GT's result fields.
  ///
  /// Together with <see cref="EmitNetpollClaimDone"/> this is Go's <c>netpollunblock</c> with
  /// <c>ioready = true</c> (our completers only ever run because an I/O finished; there is no timeout
  /// or close path that unblocks with <c>ioready = false</c>):
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
  /// ⚠ DEVIATION 4 — GO CASES TO <c>pdReady</c>; WE CAS TO <c>Claiming</c> AND PUBLISH <c>Ready</c>
  /// LATER, WHICH IS WHY THIS IS TWO FUNCTIONS WHERE GO HAS ONE. Go's completer has nothing to
  /// publish, so its claim and its publication are the same store; ours has <c>io_result_val</c> and
  /// friends to write first. See <see cref="GtLayout.NetpollClaiming"/> for why splitting the two
  /// needs a state rather than just an ordering.
  ///
  /// ⭐ THESE TWO FUNCTIONS REPLACE THE THREE-WAY GUARD every completer in this runtime used to spell
  /// for itself, and they subsume all three rather than dropping any:
  ///   (a) <c>stackBase == 0</c> — the main OS thread. It has no schedulable stack, so it never
  ///       commits; a completer finds <c>Wait</c> and declines the enqueue, and it self-detects in
  ///       its own await loop, exactly as before. This is no longer a property the protocol merely
  ///       HAPPENS to have: <see cref="EmitNetpollCommit"/> enforces it, refusing to move the word
  ///       for a GT with no stack, so "a stackless GT is always <c>Wait</c>" is checked, not argued.
  ///   (b) <c>waiter == P-&gt;currentGt</c> — the waiter is driving this very poll from its own park
  ///       loop. It has not committed either, so again <c>Wait</c>, again decline. This one IS a
  ///       consequence rather than a rule: a GT cannot be inside a completer and past its own commit
  ///       CAS at the same time, since everything between that CAS and the context switch is
  ///       straight-line code with no call.
  ///   (c) <c>waiter.ioYielded == 0</c> — "still running, it will self-detect". This is the guard
  ///       that lost wakeups: it could not tell "still running and WILL self-detect" from "still
  ///       running and is about to park", because those two are the same instant. <c>Wait</c> versus
  ///       <c>Parked</c> is exactly that distinction, decided atomically.
  ///
  /// ⚠ DEVIATION — Go's <c>pdNil</c> arm STORES <c>pdReady</c> and returns nil, carrying the
  /// readiness forward for the next <c>netpollblock</c> to consume. We return without touching the
  /// word. Same reason as <see cref="EmitNetpollArm"/>'s: Go's word outlives the goroutine and is the
  /// only place a pending notification could live, whereas ours belongs to the GT. Storing
  /// <c>Ready</c> into a <c>Nil</c> word here would leave a stale claim that the GT's NEXT park would
  /// trip over.
  ///
  /// ⚠ RETURNING 0 MEANS "PUBLISH NOTHING", AND THAT IS STRICTER THAN THE OLD ORDER WAS. Completers
  /// used to write the result fields unconditionally and only then ask whose wakeup it was; now the
  /// ask comes first and a lost race means the GT is not touched at all. <c>Nil</c> is "not waiting
  /// on anything", <c>Ready</c> is "already claimed and published", <c>Claiming</c> is "claimed by
  /// someone still publishing" — in all three another party owns the outcome, and writing
  /// <c>io_result_val</c> into a GT that has moved on is the hazard, not a harmless duplicate.
  ///
  /// ⚠ A COMPLETER THAT CLAIMS AND NEVER CALLS <see cref="EmitNetpollClaimDone"/> WEDGES THE GT IN
  /// <c>Claiming</c> FOREVER: its waiter's <c>__netpoll_woken</c> answers 0 for ever and its
  /// <c>__netpoll_park_done</c> spins for ever. That is the price of splitting the claim from the
  /// release, and it is the same bargain <see cref="EmitNetpollArm"/> / <see cref="EmitNetpollParkDone"/>
  /// already strike on the waiter's side — an unpaired <c>arm</c> throws "double wait" at the GT's
  /// next park. Every path out of a completer between these two calls must reach the second one.
  /// </summary>
  private void EmitNetpollClaim() {
    _b.FunctionStart("__netpoll_claim", 1, 0x40);

    const int slotClaimedFrom = 1;
    var loopLabel = UniqueLabel("netpoll_claim_loop");
    var takeLabel = UniqueLabel("netpoll_claim_take");
    var noneLabel = UniqueLabel("netpoll_claim_none");

    _b.DefineLabel(loopLabel);
    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, GtOffParkState);

    // Wait and Parked are the only two states a wakeup can be taken FROM; see the note above for why
    // the other three mean "touch nothing".
    _b.CmpRegImm(VReg.Scratch2, NetpollWait);
    _b.JumpIf(Condition.Equal, takeLabel);
    _b.CmpRegImm(VReg.Scratch2, NetpollParked);
    _b.JumpIf(Condition.NotEqual, noneLabel);

    _b.DefineLabel(takeLabel);
    _b.MovRegImm(VReg.Arg1, NetpollClaiming);
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, loopLabel);     // lost the race — re-read and decide again

    // Scratch2 still holds the state we claimed FROM: the CAS leaves it alone on both backends.
    _b.StoreLocal(slotClaimedFrom, VReg.Scratch2);

    _b.LoadLocal(VReg.Scratch0, slotClaimedFrom);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(noneLabel);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>
  /// <c>__netpoll_claim_done(gt, claimedFrom)</c> -&gt; the GT to enqueue, or 0. STEPS 3 and 4 of the
  /// completer's four: release the word, then decide whose job the enqueue is. <c>gt</c> and
  /// <c>claimedFrom</c> are exactly what the matching <see cref="EmitNetpollClaim"/> was given and
  /// returned.
  ///
  /// ⭐ THE STORE-RELEASE IS THE GATE, AND IT IS THE ONLY THING THAT OPENS IT. Every result the
  /// caller published since the claim becomes visible to a waiter at this instruction and not before:
  /// <see cref="EmitNetpollWoken"/>'s <c>LoadAcquire</c> pairs with this <c>StoreRelease</c> on the
  /// very same word, which is a genuine acquire/release pair on one location rather than a fence
  /// hoping to stand in for one. It is a plain store rather than a CAS because nothing else can be
  /// racing us: <c>Claiming</c> is claimable by nobody, so this word has exactly one writer.
  ///
  /// ⚠ ORDER WITHIN THIS FUNCTION IS ITSELF LOAD-BEARING: the enqueue decision reads
  /// <c>claimedFrom</c>, but the RELEASE must not wait on the <c>ioYielded</c> spin below it. A
  /// waiter claimed from <c>Parked</c> is asleep and cannot set <c>ioYielded</c> any faster for our
  /// waiting, but a DIFFERENT GT's <c>__netpoll_park_done</c> may be spinning on this very word, and
  /// holding the release behind an unrelated spin would stretch that wait for no reason.
  /// </summary>
  private void EmitNetpollClaimDone() {
    _b.FunctionStart("__netpoll_claim_done", 2, 0x40);

    var validLabel = UniqueLabel("netpoll_claim_done_valid");
    var spinLabel = UniqueLabel("netpoll_claim_done_spin");
    var enqueueLabel = UniqueLabel("netpoll_claim_done_enqueue");
    var noneLabel = UniqueLabel("netpoll_claim_done_none");

    // Validate BEFORE releasing: a claimedFrom that is neither state __netpoll_claim can return is a
    // caller bug, and releasing on the way to the panic would leave a `Ready` word behind a message
    // saying the protocol is broken.
    _b.LoadLocal(VReg.Scratch2, 1);              // claimedFrom
    _b.CmpRegImm(VReg.Scratch2, NetpollParked);
    _b.JumpIf(Condition.Equal, validLabel);
    _b.CmpRegImm(VReg.Scratch2, NetpollWait);
    _b.JumpIf(Condition.Equal, validLabel);

    EmitNetpollThrow(NetpollClaimStateMsg);

    _b.DefineLabel(validLabel);

    // ⭐ FAULT INJECTION, THE COMPLETER'S HALF — AND IT SITS HERE, NOT AT THE CLAIM, BECAUSE ONLY
    // HERE DOES IT DISCRIMINATE. Held at the claim, the injected window has `status` still reading
    // `waiting` and the word already reading `Claiming`, so __netpoll_recover declines for TWO
    // independent reasons and a green run cannot say which one held — including the reason that was
    // already true before this state existed. Held HERE, the caller's publish has happened: `status`
    // says ready, and the ONLY thing standing between the recovery net and a false positive is that
    // the word reads `Claiming` rather than `Parked`. That is precisely the change the fifth state
    // makes, so this is the one placement where the arm tests it. It re-tests the waiter's half at
    // the same instant, for the same reason: a waiter that still self-detected on `status` would
    // leave the park inside this window, which is the defect __netpoll_woken closed.
    _b.Call(NetpollClaimDelayFn);

    _b.LoadLocal(VReg.Scratch1, 0);              // gt
    _b.MovRegImm(VReg.Scratch0, NetpollReady);
    _b.StoreRelease(VReg.Scratch1, GtOffParkState, VReg.Scratch0);

    // Claimed from `Wait`: the waiter never committed, is still running, and resumes by asking the
    // word itself — either its commit CAS fails or its next __netpoll_woken returns 1. Only a
    // `Parked` claim owns the enqueue.
    _b.LoadLocal(VReg.Scratch2, 1);
    _b.CmpRegImm(VReg.Scratch2, NetpollWait);
    _b.JumpIf(Condition.Equal, noneLabel);

    // The enqueue is ours. Wait for the context save to finish before handing this GT to another M:
    // ioYielded goes to 1 inside __gt_context_switch, after the callee-saved block and gt.sp are
    // stored, and enqueueing before that lets a second M resume the GT onto a stale SP.
    //
    // ⭐ WHY THIS SPIN TERMINATES AND THE OLD DESIGN'S COULD NOT. We claimed `Parked`, so the waiter
    // has already committed, and everything from its commit CAS to `ioYielded = 1` is straight-line
    // code — no call, no lock, no branch back. It cannot decide to keep running, which is precisely
    // what it COULD do before this word existed, and precisely why the completer had to guess with a
    // non-blocking snapshot (guard (c)) instead of waiting. The bound is a scheduling quantum, the
    // same bound __gt_ppw_spin already accepts for the await hand-off.
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
  /// The two fault-injection entry points. Named here rather than spelled at their call sites
  /// because the emitted label and the function that emits it are one fact.
  /// </summary>
  private const string NetpollParkDelayFn = "__netpoll_inject_delay";

  private const string NetpollClaimDelayFn = "__netpoll_inject_claim_delay";

  /// <summary>
  /// <c>&lt;funcLabel&gt;()</c> — FAULT INJECTION, and the only instrument that can discriminate
  /// this protocol from the one it replaces.
  ///
  /// ⭐ WHY IT IS EMITTED CODE AND NOT AN ARGUMENT. The lost wakeup this protocol closes fires about
  /// once in 14,000 suite runs. At that rate a green suite is not evidence, five hundred clean runs
  /// are not evidence, and neither is a careful reading — the previous frequent bug in this same
  /// handshake was fixed by a method (reproduce, capture, probe) that is simply unavailable here.
  /// So the window is not measured, it is made ENORMOUS on demand: a few milliseconds at the exact
  /// instruction the race needs turns "an admissible interleaving" into "every run". The acceptance
  /// for this rung is that the OLD protocol wedges under injection and the new one does not.
  ///
  /// ⚠ THERE ARE TWO SUCH INSTRUCTIONS, ONE PER SIDE, WHICH IS WHY THIS TAKES ITS GLOBAL AS A
  /// PARAMETER. <see cref="NetpollParkDelayFn"/> sits between the parker's last self-detect and its
  /// commit CAS; <see cref="NetpollClaimDelayFn"/> sits inside <see cref="EmitNetpollClaim"/>, on the
  /// far side of the winning CAS, so it holds a completer mid-completion with the word reading
  /// <c>Claiming</c>. Widening only the first can only ever falsify the parker's half of the protocol.
  ///
  /// It SHIPS rather than hiding behind a build flag, deliberately: unset, each costs one
  /// predictable-not-taken load and branch, and it is the only way a future change to this handshake
  /// can be shown not to have reopened the window. A build flag would have cost the same and been
  /// unavailable in the binary anyone actually runs.
  /// </summary>
  private void EmitNetpollDelayFn(string funcLabel, string delayMsGlobal) {
    _b.FunctionStart(funcLabel, 0, 0x40);

    var doneLabel = UniqueLabel("netpoll_inject_done");

    _b.LoadGlobal(VReg.Scratch0, delayMsGlobal);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.OsSleepMillis(VReg.Arg0);

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  /// <summary>
  /// <c>__netpoll_init()</c> — read both injection delays out of the environment once, at scheduler
  /// init, so the hot path is a global load rather than a <c>getenv</c>.
  /// </summary>
  private void EmitNetpollInit() {
    _b.FunctionStart("__netpoll_init", 0, 0x80);

    // Each ReadEnvUnsigned owns FOUR consecutive slots as its Windows value buffer (POSIX ignores
    // them), so the two reads must not share a base — same scratchSlot convention GetCurrentTimeMs
    // and friends use.
    _b.ReadEnvUnsigned(VReg.Scratch0, NetpollParkDelayEnvSymdata, 4);
    _b.StoreGlobal(NetpollParkDelayMsLabel, VReg.Scratch0);

    _b.ReadEnvUnsigned(VReg.Scratch0, NetpollClaimDelayEnvSymdata, 8);
    _b.StoreGlobal(NetpollClaimDelayMsLabel, VReg.Scratch0);

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
  /// ⭐⭐ "UNREACHABLE" IS NOW A PROPERTY OF THE STATE MACHINE, NOT OF TIMING, AND THAT IS THE WHOLE
  /// REASON THIS SCAN IS WORTH RUNNING. Readiness is published ONLY between
  /// <see cref="EmitNetpollClaim"/>'s winning CAS and <see cref="EmitNetpollClaimDone"/>'s release,
  /// i.e. only while the word reads <c>Claiming</c> — so no healthy completion is ever <c>Parked</c>
  /// with <c>status != waiting</c>, for any duration, on any schedule. The condition above therefore
  /// SELECTS lost wakeups instead of merely outrunning healthy ones.
  ///
  /// ⚠ IT DID NOT USED TO, AND THE COST WAS MEASURED. Completers used to publish and THEN claim, so a
  /// perfectly healthy completion spent its whole publish looking exactly like the condition above.
  /// The A/B, on one build, under <c>MAXON_GT_CLAIM_DELAY_MS=5</c> — which is DEFINED as the knob
  /// that widens a completer's window: net armed ⇒ <b>1/10 clean</b>, the failures leaking 1-4
  /// allocations each and one wedging outright; net inert ⇒ <b>10/10 clean</b>, same park protocol.
  /// The net was not rescuing anything real. It was rescuing completers that were merely SLOW, and
  /// each "rescue" then handed that completer's late claim to the waiter's NEXT park — the class
  /// comment's defect one level up, which "the CAS makes them mutually exclusive" never covered,
  /// because mutual exclusion on THIS park says nothing about the next one.
  ///
  /// The instrument and the detector were aimed at one interval, so arming the first necessarily
  /// tripped the second, at ANY confirmation interval — which is exactly why widening
  /// <see cref="NetpollRecoverIntervalMs"/> was never the fix. Claim-then-publish is: the interval
  /// still exists and the knob still widens it, but the word now NAMES it, and this scan skips it.
  ///
  /// ⚠ A CANDIDATE IS STILL SEEN TWICE, A SCAN INTERVAL APART, AND THAT REQUIREMENT STAYS. It is no
  /// longer load-bearing against a healthy completion — those are excluded by construction now — but
  /// it costs one word and one comparison, and it is what keeps a single unlucky read of a GT
  /// mid-transition from being reported as a lost wakeup. A counter whose whole job is to be exactly
  /// 0 does not get to cry wolf even once.
  ///
  /// ⚠⚠ WHICH IS ALSO WHY THE TIME GATE IS A CAS AND NOT A LOAD-COMPARE-STORE. It was the latter, on
  /// a global every M runs through, so two Ms could pass it in the same instant — both scan, both
  /// find the same GT, and the SECOND one reads the FIRST one's <c>__netpoll_suspect</c> and
  /// "confirms" a candidate seen twice MICROSECONDS apart, collapsing "a scan interval apart" onto
  /// nothing. Measured at the time: the net fired on EVERY run of the park driver at 12 Ps and on
  /// NONE at <c>MAXON_MAX_PROCS=1</c>. The CAS makes the gate the scan's mutex as well as its clock:
  /// exactly one M owns each interval, so <c>__netpoll_suspect</c> has one writer at a time. Losing
  /// the CAS means another M is scanning right now — there is nothing to wait for.
  ///
  /// ⚠ A GT WEDGED IN <c>Claiming</c> IS A REAL FAILURE THIS SCAN DELIBERATELY DOES NOT REPORT. A
  /// completer that claimed and then stalled or died leaves its waiter unwakeable, and the walk below
  /// passes straight over it. Counting it was considered and rejected: the only instrument that can
  /// produce one on purpose is <see cref="NetpollClaimDelayMsLabel"/>, which is DEFINED as the knob
  /// that widens the <c>Claiming</c> window — so a <c>Claiming</c>-based detector would recreate the
  /// exact instrument-versus-detector collision this pass exists to remove, one state along. It needs
  /// a signal the injection cannot forge, and that is not this scan.
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
    const int slotNow = 2;
    var retLabel = UniqueLabel("netpoll_recover_ret");
    var walkLabel = UniqueLabel("netpoll_recover_walk");
    var nextLabel = UniqueLabel("netpoll_recover_next");
    var unlockLabel = UniqueLabel("netpoll_recover_unlock");
    var confirmLabel = UniqueLabel("netpoll_recover_confirm");
    var claimLabel = UniqueLabel("netpoll_recover_claim");
    var enqueueLabel = UniqueLabel("netpoll_recover_enqueue");

    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(slotCandidate, VReg.Scratch0);

    // Time gate, AND the scan's mutex — one CAS is both, see the summary. The walk is O(live GTs)
    // and every scheduler idle turn on every M calls this, so without the gate the net would cost
    // more than the hazard; without the ATOMICITY the two-sighting rule would not mean "still
    // unclaimed 10 ms later", which is the only thing that makes it a detector.
    // Slots 4-5 are GetCurrentTimeMs's out-parameter buffer, and it clobbers Scratch0..Scratch2.
    _b.GetCurrentTimeMs(VReg.Scratch0, 4);
    _b.StoreLocal(slotNow, VReg.Scratch0);

    _b.LeaGlobal(VReg.Scratch1, NetpollLastScanMsLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 0);
    _b.LoadLocal(VReg.Scratch0, slotNow);
    _b.SubRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.CmpRegImm(VReg.Scratch0, NetpollRecoverIntervalMs);
    _b.JumpIf(Condition.Below, retLabel);

    // Claim the interval. Losing means another M passed the same gate first and is scanning now.
    _b.LoadLocal(VReg.Arg1, slotNow);
    _b.AtomicCAS(VReg.Scratch1, 0, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, retLabel);

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
    // GT. No transient state of a live completer survives a scan interval.
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
    // Parked -> Ready in ONE CAS, skipping `Claiming`, and that is right rather than sloppy: the
    // intermediate state exists to say "results are going in", and the net has no results to write.
    // Expecting `Parked` is also what makes the rescue safe now that `Claiming` exists — a completer
    // that has begun on this GT has already moved the word, so this CAS cannot take it from under one
    // however slowly it is publishing, and the net therefore cannot produce the late claim it used to.
    _b.LoadLocal(VReg.Scratch1, slotCandidate);
    _b.MovRegImm(VReg.Scratch2, NetpollParked);
    _b.MovRegImm(VReg.Arg1, NetpollReady);
    _b.AtomicCAS(VReg.Scratch1, GtOffParkState, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, retLabel);      // a real completer got there first: nothing to fix

    // ⚠ ATOMIC, because EVERY M runs this scan and two of them can confirm the same GT in the same
    // interval — the CAS above makes only one of them the claimer, but a load/add/store here would
    // still let a second M's scan overwrite the increment of a first M that was rescheduled mid
    // read-modify-write. An UNDERCOUNTING regression detector is worse than none: this number's
    // whole job is to be exactly 0, and a count that can silently drop 1 to 0 cannot do it. Xadd
    // rather than Inc because the "say so once" test below needs the value we replaced, and reading
    // the counter back after an increment is the same race one level down.
    _b.LeaGlobal(VReg.Scratch1, NetpollRecoveredLabel);
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.AtomicXadd(VReg.Scratch1, 0, VReg.Scratch0);   // Scratch0 = the count BEFORE this rescue

    // Say so, once, on the first rescue — the M that observed 0. See NetpollRecoveredMsg.
    _b.JumpIfNonZero(VReg.Scratch0, enqueueLabel);
    _b.LeaSymdata(VReg.Arg0, NetpollRecoveredMsg);
    _b.Call(_b.WriteStderrLabel);

    _b.DefineLabel(enqueueLabel);
    _b.LoadLocal(VReg.Arg0, slotCandidate);
    _b.Call("__gt_enqueue");

    _b.DefineLabel(retLabel);
    _b.FunctionEnd();
  }
}
