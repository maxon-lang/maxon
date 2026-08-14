using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;

namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Scheduler functions emitted once for both platforms via IEmitterBackend.
/// Implements the GMP (goroutine-machine-processor) scheduling model:
/// enqueue, dequeue (with fairness), work stealing, and timer heap operations.
/// </summary>
public partial class RuntimeEmitter {

  // Scheduler constants (MaxFreeListLen and PSystemStackSize live in GtLayout)
  public const int MaxLocalQueueLen = 256;
  public const int FairnessInterval = 61;
  public const int TimerHeapCapacity = 256;
  public const int TimerEntrySize = 16;
  public const int TimerOffDeadline = 0;
  public const int TimerOffGt = 8;

  /// <summary>
  /// Emit the xorshift64 PRNG update inline: reads P->rng, applies the three XOR shifts,
  /// writes the updated value back. Returns the result in <paramref name="dest"/>.
  /// <paramref name="pReg"/> must contain P* and will not be clobbered.
  /// Clobbers Scratch2.
  /// </summary>
  private void EmitXorshift64(VReg dest, VReg pReg) {
    _b.LoadIndirect(dest, pReg, POffRng);
    // x ^= x << 13
    _b.MovRegReg(VReg.Scratch2, dest);
    _b.ShlRegImm(VReg.Scratch2, 13);
    _b.XorRegReg(dest, VReg.Scratch2);
    // x ^= x >> 7
    _b.MovRegReg(VReg.Scratch2, dest);
    _b.ShrRegImm(VReg.Scratch2, 7);
    _b.XorRegReg(dest, VReg.Scratch2);
    // x ^= x << 17
    _b.MovRegReg(VReg.Scratch2, dest);
    _b.ShlRegImm(VReg.Scratch2, 17);
    _b.XorRegReg(dest, VReg.Scratch2);
    // Store back
    _b.StoreIndirect(pReg, POffRng, dest);
  }

  /// <summary>
  /// <paramref name="dest"/> = P-&gt;currentGt, or 0 when this OS thread owns no processor (an I/O worker,
  /// or anything running before the scheduler exists).
  ///
  /// The ONE guarded statement of it. The unguarded per-backend loads (EmitLoadCurrentGtInline /
  /// EmitLoadCurrentGt) are right where a processor is guaranteed — a Maxon function prologue, or the
  /// fault handler, which has already refused a null currentGt — but the panic path and the debug agent
  /// both have to be able to answer "there is no green thread" rather than dereference null, and each
  /// writing its own guard is how the two would come to disagree about what "no thread" looks like.
  /// Clobbers only <paramref name="dest"/>.
  /// </summary>
  private void EmitLoadCurrentGtOrZero(VReg dest) {
    var doneLabel = UniqueLabel("cur_gt_or_zero_done");

    _b.LoadCurrentP(dest);
    _b.JumpIfZero(dest, doneLabel);                       // no P: dest already holds 0, which IS "none"
    _b.LoadIndirect(dest, dest, POffCurrentGt);

    _b.DefineLabel(doneLabel);
  }

  /// <summary>
  /// __gt_stack_high_current(sp) -> <see cref="EmitGtStackHigh"/> for the thread the CALLER is running
  /// on. Not a pass-through: it answers a different question ("where does MY stack end") and it is the
  /// question the panic and fault backtraces ask, on both architectures, from hand-emitted code that
  /// would otherwise each need its own copy of the guarded currentGt load.
  /// </summary>
  public void EmitGtStackHighCurrent() {
    _b.FunctionStart("__gt_stack_high_current", 1, 0x40);

    EmitLoadCurrentGtOrZero(VReg.Arg0);
    _b.LoadLocal(VReg.Arg1, 0);
    _b.Call("__gt_stack_high");
    _b.FunctionEnd();
  }

  /// <summary>
  /// __gt_stack_high(gt, sp) -> the EXCLUSIVE upper bound of the stack <c>sp</c> is running on.
  ///
  /// The one place a stack's extent is decided, because getting it wrong FAULTS rather than merely
  /// reporting badly, and four walkers ask the question: mrt_panic, mrt_fault_backtrace (both
  /// architectures) and the debug agent's shared frame walk. A green-thread stack is
  /// GtInitialStackSize and the spawn trampoline's frame pointer sits at its very TOP, so a walker
  /// bounded by FaultStackWindowBytes reads the frame link one word past the end — inside a fault or
  /// trap handler, where a second fault is the end of the process.
  ///
  /// Three cases, and only the first has a real answer:
  ///   * a green thread with a recorded extent, containing sp -> stackBase + stackSize, exact;
  ///   * a thread with NO stack of its own (a processor's inline main-thread GT runs on the OS
  ///     thread's stack) -> the sane window. The test is stackBase == 0, which is the SAME test the
  ///     rest of the runtime already spells that way — __io_complete_gt, __gt_signal_waiter and
  ///     EmitCallImportOnSystemStack among a dozen others. A second field would be a second opinion
  ///     about what "no thread of its own" looks like, and the two would agree only by accident.
  ///   * an sp OUTSIDE that thread's stack — a fault taken while switched to the P's system stack —
  ///     -> also the sane window, because the green thread's extent would reject every frame and
  ///     silently produce an EMPTY trace, which reads as "no frames" rather than "wrong stack".
  /// </summary>
  public void EmitGtStackHigh() {
    _b.FunctionStart("__gt_stack_high", 2, 0x40);

    const int slotGt = 0;
    const int slotSp = 1;
    var unknownLabel = UniqueLabel("gt_stack_high_unknown");

    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.JumpIfZero(VReg.Scratch0, unknownLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtOffStackBase);
    _b.JumpIfZero(VReg.Scratch2, unknownLabel);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffStackSize);

    _b.LoadLocal(VReg.Scratch3, slotSp);
    _b.CmpRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.JumpIf(Condition.Below, unknownLabel);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch1);            // top = base + size
    _b.CmpRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.JumpIf(Condition.AboveEqual, unknownLabel);

    _b.MovRegReg(VReg.Ret, VReg.Scratch2);
    _b.FunctionEnd();

    _b.DefineLabel(unknownLabel);
    _b.LoadLocal(VReg.Ret, slotSp);
    _b.AddRegImm(VReg.Ret, FaultStackWindowBytes);
    _b.FunctionEnd();
  }

  // =========================================================================
  // __gt_enqueue(gt) / __gt_enqueue_back(gt): Add a GreenThread to the scheduling system.
  // =========================================================================
  //
  // TWO ENTRY POINTS, TWO ENDS OF THE QUEUE, ONE SOURCE.
  //
  //   __gt_enqueue      — runnext slot -> local queue -> global queue (overflow). The FRONT.
  //                       Right for a spawn and for a wake: the thread this processor was
  //                       just waiting on is the one it should run next.
  //   __gt_enqueue_back  — straight to the global queue tail. The BACK, and the only thing a
  //                       cooperative YIELD can mean. Go draws exactly this line: goready
  //                       reaches runnext, while Gosched routes through globrunqput.
  //
  // ⛔ THE FRONT ENTRY CANNOT SERVE A YIELD, MEASURED. A worker GT's yield defers its own
  // re-enqueue until it is off its stack, so the enqueue happens from the scheduler side —
  // and `__gt_enqueue` puts it in P->runnext, the slot that GT just vacated (or DISPLACES
  // whoever holds it, which is worse). The next statement on every scheduler path is
  // `__gt_dequeue`, which checks runnext FIRST and unconditionally. Round trip; nobody else
  // ran. A thousand yields from one green thread left a sibling that had never run still
  // unrun, on the bootstrap, against shv2 answering correctly for the same program.
  //
  // After enqueueing, both tries to wake an idle worker or spawn a new one.
  //
  // Stack slots:
  //   0 = gt (arg, later reused for displaced GT)
  //   1 = P*
  //   2 = num_procs
  //   3 = loop counter i
  //   4 = saved P[i] during spawn
  //
  // Frame size: 0x60
  // =========================================================================

  public void EmitGtEnqueue() {
    EmitGtEnqueueEntry("__gt_enqueue", backOfQueue: false);
    EmitGtEnqueueEntry("__gt_enqueue_back", backOfQueue: true);
  }

  /// <summary>
  /// One spelling of the enqueue, emitted twice under different names. <paramref name="name"/> is both
  /// the runtime symbol and the prefix every internal label is minted from, so the two copies cannot
  /// collide; the front entry keeps the exact label names it has always had.
  ///
  /// <paramref name="backOfQueue"/> ELIDES the runnext and local-queue sections rather than branching
  /// past them — a back enqueue has no use for either, and emitting unreachable code so the two
  /// functions "look the same" would be the wrong kind of sameness. What both share, and what actually
  /// had to stay one text, is the global-tail append and the whole wake/spawn phase below it.
  /// </summary>
  private void EmitGtEnqueueEntry(string name, bool backOfQueue) {
    _b.FunctionStart(name, 1, 0x60);

    // gt.next = 0
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch1);

    // Load P* from TLS (may be NULL if called from I/O worker)
    _b.LoadCurrentP(VReg.Scratch1);
    _b.StoreLocal(1, VReg.Scratch1);

    if (backOfQueue) {
      // The global queue is the back, and it is where this entry always goes — P or no P.
      _b.Jump($"{name}_global");
    } else {
      EmitGtEnqueueFrontOfQueue(name);
    }

    // --- Global queue ---
    _b.DefineLabel($"{name}_global");
    _b.LockAcquire(_b.SchedLockLabel);

    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadGlobal(VReg.Scratch1, "__gt_run_queue_tail");
    _b.JumpIfNonZero(VReg.Scratch1, $"{name}_global_append");

    _b.StoreGlobal("__gt_run_queue_head", VReg.Scratch0);
    _b.StoreGlobal("__gt_run_queue_tail", VReg.Scratch0);
    _b.Jump($"{name}_global_unlock");

    _b.DefineLabel($"{name}_global_append");
    _b.StoreIndirect(VReg.Scratch1, GtOffNext, VReg.Scratch0);
    _b.StoreGlobal("__gt_run_queue_tail", VReg.Scratch0);

    _b.DefineLabel($"{name}_global_unlock");
    _b.LockRelease(_b.SchedLockLabel);

    // dbg: enqueue (gt, kind=global) — emitted after unlock so we don't extend
    // the critical section. The store to the global queue is already complete,
    // so the only window is "store visible but event not yet logged" which is
    // benign for diagnostics.
    _b.LoadLocal(VReg.Scratch0, 0);
    EmitDbgEnqueue(VReg.Scratch0, DsDbgQueueGlobal, 0);

    EmitGtEnqueueWake(name);
    _b.FunctionEnd();
  }

  /// <summary>
  /// The FRONT half: runnext, then this P's local queue, falling through to the caller's global path
  /// when the local queue is full. Reached only from <c>__gt_enqueue</c>; a P is guaranteed non-NULL
  /// on entry only after the check this emits first.
  /// </summary>
  private void EmitGtEnqueueFrontOfQueue(string name) {
    // If P* == NULL: go straight to global queue
    _b.JumpIfZero(VReg.Scratch1, $"{name}_global");

    // --- Try runnext slot ---
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffRunnext);
    _b.JumpIfNonZero(VReg.Scratch2, $"{name}_displace_runnext");

    // Runnext empty: P->runnext = gt
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch1, POffRunnext, VReg.Scratch0);
    // dbg: runnext_set (gt, P) — Scratch0=gt already; pass zero for arg2..arg4
    EmitDbgRunnextSet(VReg.Scratch0);
    _b.Jump($"{name}_wake");

    // --- Runnext occupied: displace old to local queue ---
    _b.DefineLabel($"{name}_displace_runnext");
    // Scratch2 = old runnext, Scratch1 = P*
    _b.ZeroReg(VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch2, GtOffNext, VReg.Scratch3);
    // P->runnext = new gt
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.StoreIndirect(VReg.Scratch1, POffRunnext, VReg.Scratch0);
    // Reuse slot 0 for the displaced GT
    _b.StoreLocal(0, VReg.Scratch2);
    // dbg: runnext_displace (displaced=Scratch2, new_gt=Scratch0)
    EmitDbgRunnextDisplace(VReg.Scratch2, VReg.Scratch0);

    // --- Local queue ---
    // Take __sched_global_queue_cs to serialize against __gt_steal_work, which
    // also walks this P's local queue under the same lock. Without this, a
    // thief and the owner can both pop the same head node (verified by trace:
    // dbg_dequeue ... kind=local and dbg_dequeue ... kind=steal_first on the
    // same gt within the same millisecond, immediately followed by SIGSEGV).
    _b.LockAcquire(_b.SchedLockLabel);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffLocalQueueLen);
    _b.CmpRegImm(VReg.Scratch2, MaxLocalQueueLen);
    _b.JumpIf(Condition.AboveEqual, $"{name}_local_full_unlock");

    // Append to local queue tail
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffLocalQueueTail);
    _b.JumpIfNonZero(VReg.Scratch2, $"{name}_local_append");

    // Local queue empty: head = tail = gt
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueHead, VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueTail, VReg.Scratch0);
    _b.Jump($"{name}_local_inc");

    _b.DefineLabel($"{name}_local_append");
    _b.StoreIndirect(VReg.Scratch2, GtOffNext, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueTail, VReg.Scratch0);

    _b.DefineLabel($"{name}_local_inc");
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffLocalQueueLen);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueLen, VReg.Scratch2);
    _b.LockRelease(_b.SchedLockLabel);
    // dbg: enqueue (gt, kind=local). Reload gt — P->id will be loaded by helper.
    _b.LoadLocal(VReg.Scratch0, 0);
    EmitDbgEnqueue(VReg.Scratch0, DsDbgQueueLocal, /*ownerPid=self*/0);
    _b.Jump($"{name}_wake");

    // Local queue full: release lock then fall through to the caller's global path.
    _b.DefineLabel($"{name}_local_full_unlock");
    _b.LockRelease(_b.SchedLockLabel);
  }

  /// <summary>
  /// The wake/spawn phase both entry points end in: hand the newly-runnable GT to an idle worker, or
  /// start one. Identical for either end of the queue — what changed is where the thread went, not
  /// who should be told about it.
  /// </summary>
  private void EmitGtEnqueueWake(string name) {
    _b.DefineLabel($"{name}_wake");
    // Full memory barrier between our queue-publish above and the idleFlag reads below.
    // Closes the Dekker-style missed-wakeup race against __sched_wloop_park's
    // "store idleFlag=1; re-dequeue" sequence: without this barrier, a StoreLoad
    // reorder on x86 (or general reordering on ARM64) could let us observe idleFlag=0
    // (stale) while our queue-publish isn't yet globally visible, so the worker re-dequeues
    // empty and parks with work queued.
    _b.FullBarrier();
    _b.LoadGlobal(VReg.Scratch0, "__sched_shutdown_flag");
    _b.JumpIfNonZero(VReg.Scratch0, $"{name}_wake_done");

    // Scan P[1..num_procs-1] for idle workers
    _b.LoadGlobal(VReg.Scratch0, "__sched_num_procs");
    _b.StoreLocal(2, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(3, VReg.Scratch1);

    _b.DefineLabel($"{name}_wake_loop");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, $"{name}_wake_spawn");

    // Load P[i]
    _b.LoadGlobal(VReg.Scratch1, "__sched_procs");
    _b.LoadLocal(VReg.Scratch2, 3);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch1, 0);

    // Check idleFlag
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffIdleFlag);
    _b.JumpIfZero(VReg.Scratch2, $"{name}_wake_next");

    // Found idle worker: clear flag, wake it
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, POffIdleFlag, VReg.Scratch2);
    _b.WakeWorker(VReg.Scratch1);
    _b.Jump($"{name}_wake_done");

    _b.DefineLabel($"{name}_wake_next");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump($"{name}_wake_loop");

    // --- No idle worker: try to spawn ---
    _b.DefineLabel($"{name}_wake_spawn");
    _b.LoadGlobal(VReg.Scratch0, "__sched_active_workers");
    _b.LoadGlobal(VReg.Scratch1, "__sched_max_procs");
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, $"{name}_wake_done");

    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);

    _b.DefineLabel($"{name}_spawn_scan");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, $"{name}_wake_done");

    // Load P[i]
    _b.LoadGlobal(VReg.Scratch1, "__sched_procs");
    _b.LoadLocal(VReg.Scratch2, 3);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch1, 0);

    // Check P[i]->status == PStatusUnused
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffStatus);
    _b.JumpIfNonZero(VReg.Scratch2, $"{name}_spawn_next");

    // Claim it: status = PStatusActive
    _b.MovRegImm(VReg.Scratch2, PStatusActive);
    _b.StoreIndirect(VReg.Scratch1, POffStatus, VReg.Scratch2);
    _b.StoreLocal(4, VReg.Scratch1);

    // Spawn worker thread
    _b.LoadLocal(VReg.Scratch1, 4);
    _b.SpawnWorker(VReg.Scratch1);
    _b.Jump($"{name}_wake_done");

    _b.DefineLabel($"{name}_spawn_next");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump($"{name}_spawn_scan");

    _b.DefineLabel($"{name}_wake_done");
  }

  // =========================================================================
  // __gt_dequeue() -> GT* in Ret (or NULL).
  // =========================================================================
  //
  // GMP dequeue: runnext -> local (with 1/61 fairness) -> global -> steal.
  //
  // ⭐ THIS IS THE ONE PLACE THE SCHEDULER DECIDES WHAT RUNS NEXT, which is why the debug agent's
  // per-thread hold (P4d-2b) is applied to its RESULT rather than anywhere else: a thread the debugger
  // owns is one this function declines to hand back. `__gt_dequeue` is therefore split in two — a
  // dispatcher and the unchanged body, `__gt_dequeue_ready` — so the filter wraps the answer instead of
  // being threaded through the four places the body returns one.
  //
  // ⚠ THE DARK COST IS A WHOLE CALL FRAME, NOT A BRANCH. "One load and a not-taken branch" is what this
  // shape reads like and is not what it emits: with no debugger attached the path through here is the
  // dispatcher's own prologue (push rbp / mov / sub rsp,0x40 / push rbx,rsi,rdi), the `__dbg_base` load,
  // a test, a TAKEN jump, `call __gt_dequeue_ready`, and the matching epilogue — 17 instructions and 8
  // stack accesses, disassembled out of the emitted `.text`, around a body that is byte-for-byte what it
  // was. Stated here because this is the one place that can state it, and because the emitted cost of a
  // hot path is exactly the kind of claim that must not be inferred from what the source looks like.
  //
  // WHAT IT COSTS, MEASURED (2026-07-25, x64-windows, idle 16-core host, MAXON_MAX_PROCS=1): +0.055 ns
  // per dequeue, 95% CI [-0.37, +0.48] ns — smaller than the +0.084 ns/dequeue that a NULL CONTROL (the
  // same binary in both arms) reports as this harness's own bias. 300 randomized-order pairs of a
  // 1.2M-dequeue scheduling benchmark at 8.4M dequeues/s, outlier-trimmed. So it is not free, and it is
  // below the floor of the sharpest in-situ instrument available.
  //
  // It is deliberately NOT a frameless tail jump (`jmp __gt_dequeue_ready`, 4 instructions instead of
  // 17, and what would make the sentence above unnecessary): the gain is unmeasurable, and shrinking
  // this function shifts every runtime code offset after it — including the spawn-trampoline frames that
  // DebugSamples/threads.expected.txt (0xf8e3) and gtcontrol.expected.txt (0xf8fc) pin. (Those two
  // numbers read 0xf818 / 0xf831 here until 2026-08-13; the goldens had moved and the citation had not,
  // which is the failure mode of quoting a value that lives in another file.)
  //
  // See RuntimeEmitter.DebugAgent.cs's green-thread control section for what the filter does with the
  // thread it takes.
  // =========================================================================

  /// <summary>
  /// Put a green thread that yielded cooperatively onto the BACK of the run queue, if one is waiting.
  /// Emitted at the top of <c>__gt_dequeue</c>'s body — i.e. immediately before the scheduler decides
  /// what runs next, which is the one moment at which "behind everyone currently runnable" is a
  /// well-defined place to be.
  ///
  /// ⭐ THE DEFERRAL AND THE END OF THE QUEUE ARE TWO SEPARATE REQUIREMENTS, AND ONLY MEETING BOTH IS A
  /// YIELD. The deferral is a memory-safety rule: a GT may not become discoverable to another M until
  /// <c>__gt_context_switch</c> has saved its registers, or that M resumes it onto a stale SP. The end
  /// of the queue is the SEMANTIC rule. <c>maxon_yield</c> originally met the first through
  /// <c>P-&gt;pendingWaiter</c> and silently failed the second, because that channel drains into
  /// <c>__gt_enqueue</c> — which lands the thread in <c>P-&gt;runnext</c>, the slot it had just
  /// vacated, and <c>__gt_dequeue</c> takes runnext first. The yielder was handed straight back to
  /// itself: a thousand yields, and a sibling green thread that had never run still had not run.
  ///
  /// ⚠ THE DRAIN BELONGS HERE RATHER THAN IN A PARK LOOP, and the reason is coverage, not taste.
  /// <c>__gt_dequeue</c> is the ONE place this runtime decides what runs next — the worker loop, all
  /// five main-thread park loops, <c>__gt_cleanup</c>'s exit drain and <c>maxon_yield</c>'s own
  /// main-thread arm all reach it — so a yielder cannot be stranded by a scheduler path that forgot to
  /// look. Draining anywhere else would be a roster to keep in step, which is this codebase's most
  /// expensive recurring shape.
  ///
  /// ⚠ IT IS SAFE TO READ THE SLOT UNSYNCHRONISED because it is per-P and single-writer: only the GT
  /// currently running on this P writes it, only this P's own <c>__gt_dequeue</c> reads it, and the
  /// context switch between those two events orders them. No other M can see the slot at all — unlike
  /// <c>P-&gt;pendingWaiter</c>, which a completer on another M can also target.
  ///
  /// Costs a load and a not-taken branch on the hot dequeue path. That is strictly less than the
  /// debug-agent dispatcher already sitting in front of this function, whose 17 instructions measured
  /// +0.055 ns/dequeue — below the noise floor of the sharpest in-situ instrument available.
  /// Clobbers the call-clobbered set on the taken path only; it is emitted before anything is live.
  /// </summary>
  private void EmitDrainPendingYielder() {
    var noYielderLabel = UniqueLabel("no_pending_yielder");

    _b.LoadCurrentP(VReg.Scratch1);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, POffPendingYielder);
    _b.JumpIfZero(VReg.Scratch0, noYielderLabel);

    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, POffPendingYielder, VReg.Scratch2);
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__gt_enqueue_back");

    _b.DefineLabel(noYielderLabel);
  }

  public void EmitGtDequeue() {
    if (!Compiler.NoDebugAgent) {
      _b.FunctionStart("__gt_dequeue", 0, 0x40);

      var plainLabel = UniqueLabel("gt_dequeue_undebugged");

      _b.LoadGlobal(VReg.Scratch1, "__dbg_base");
      _b.JumpIfZero(VReg.Scratch1, plainLabel);
      // Attached: the agent decides. `__dbg_base` is the right gate because it is cleared in exactly one
      // place — __dbg_shutdown, at process exit — so no thread can be left held by a detach that happens
      // while the program is still running.
      _b.Call("__dbg_gt_dequeue_filtered");
      _b.FunctionEnd();

      _b.DefineLabel(plainLabel);
      _b.Call("__gt_dequeue_ready");
      _b.FunctionEnd();
    }

    // With the agent omitted (--no-debug-agent) there is nothing to filter, so the body IS the whole
    // function and keeps the name every caller uses.
    _b.FunctionStart(Compiler.NoDebugAgent ? "__gt_dequeue" : "__gt_dequeue_ready", 0, 0x40);

    EmitDrainPendingYielder();

    // Load P*
    _b.LoadCurrentP(VReg.Scratch1);
    _b.StoreLocal(0, VReg.Scratch1);

    // --- 1. Check runnext ---
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, POffRunnext);
    _b.JumpIfZero(VReg.Scratch0, "__gt_dequeue_check_fairness");

    // Got runnext: clear slot, clear gt.next, return
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, POffRunnext, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch2);
    // dbg: runnext_take (gt) — spill gt across the helper, then reload to return
    _b.StoreLocal(2, VReg.Scratch0);
    EmitDbgRunnextTake(VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);

    // --- 2. Fairness check: xorshift64 on P->rng ---
    _b.DefineLabel("__gt_dequeue_check_fairness");
    _b.LoadLocal(VReg.Scratch1, 0);
    EmitXorshift64(VReg.Scratch0, VReg.Scratch1);

    // Check if (rng % 61) == 0
    _b.UDivRemainder(VReg.Scratch2, VReg.Scratch0, FairnessInterval);
    _b.JumpIfZero(VReg.Scratch2, "__gt_dequeue_global");

    // --- 3. Local queue ---
    // Take __sched_global_queue_cs to serialize against __gt_steal_work, which
    // walks this P's local queue under the same lock. Without this lock, a
    // thief stealing from us and our own pop both advance head, double-running
    // the head node on two workers (verified via dbg_dequeue trace).
    _b.DefineLabel("__gt_dequeue_local");
    _b.LockAcquire(_b.SchedLockLabel);
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, POffLocalQueueLen);
    _b.JumpIfZero(VReg.Scratch0, "__gt_dequeue_local_empty_unlock");

    // Dequeue from head
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, POffLocalQueueHead);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtOffNext);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueHead, VReg.Scratch2);
    // len--
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, POffLocalQueueLen);
    _b.SubRegImm(VReg.Scratch3, 1);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueLen, VReg.Scratch3);
    // If new head == NULL, clear tail
    _b.JumpIfNonZero(VReg.Scratch2, "__gt_dequeue_local_done");
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueTail, VReg.Scratch2);

    _b.DefineLabel("__gt_dequeue_local_done");
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch2);
    // Save dequeued GT before LockRelease (LeaveCriticalSection clobbers caller-saved regs).
    _b.StoreLocal(2, VReg.Scratch0);
    _b.LockRelease(_b.SchedLockLabel);
    // dbg: dequeue (gt, kind=local)
    _b.LoadLocal(VReg.Scratch0, 2);
    EmitDbgDequeue(VReg.Scratch0, DsDbgQueueLocal, 0);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);

    // Local queue empty under lock: release and fall through to global path.
    _b.DefineLabel("__gt_dequeue_local_empty_unlock");
    _b.LockRelease(_b.SchedLockLabel);
    _b.Jump("__gt_dequeue_global");

    // --- 4. Global queue ---
    _b.DefineLabel("__gt_dequeue_global");
    _b.LockAcquire(_b.SchedLockLabel);

    _b.LoadGlobal(VReg.Scratch0, "__gt_run_queue_head");
    _b.JumpIfNonZero(VReg.Scratch0, "__gt_dequeue_global_nonempty");

    // Empty
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(1, VReg.Scratch0);
    _b.Jump("__gt_dequeue_global_unlock");

    _b.DefineLabel("__gt_dequeue_global_nonempty");
    _b.StoreLocal(1, VReg.Scratch0);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffNext);
    _b.StoreGlobal("__gt_run_queue_head", VReg.Scratch1);
    _b.JumpIfNonZero(VReg.Scratch1, "__gt_dequeue_global_unlock");
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreGlobal("__gt_run_queue_tail", VReg.Scratch1);

    _b.DefineLabel("__gt_dequeue_global_unlock");
    _b.LockRelease(_b.SchedLockLabel);

    _b.LoadLocal(VReg.Scratch0, 1);
    _b.JumpIfZero(VReg.Scratch0, "__gt_dequeue_steal");

    // Got from global: clear next, return
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch1);
    // dbg: dequeue (gt, kind=global)
    _b.StoreLocal(2, VReg.Scratch0);
    EmitDbgDequeue(VReg.Scratch0, DsDbgQueueGlobal, 0);
    _b.LoadLocal(VReg.Scratch0, 2);
    _b.ReturnValue(VReg.Scratch0);

    // --- 5. Work stealing ---
    _b.DefineLabel("__gt_dequeue_steal");
    _b.Call("__gt_steal_work");
    // Result in Ret
    _b.FunctionEnd();
  }

  // =========================================================================
  // __gt_steal_work() -> GT* in Ret (or NULL).
  // =========================================================================
  //
  // Tries to steal half of a random victim's local queue.
  //
  // Stack slots:
  //   0 = our P*
  //   1 = attempt counter
  //   2 = num_procs
  //   3 = victim P*
  //   4 = first stolen GT (return value)
  //   5 = steal count n
  //   6 = walk counter
  //   7 = last stolen pointer
  //
  // Frame size: 0x70
  // =========================================================================

  public void EmitGtStealWork() {
    _b.FunctionStart("__gt_steal_work", 0, 0x70);

    // Load our P*
    _b.LoadCurrentP(VReg.Scratch1);
    _b.StoreLocal(0, VReg.Scratch1);

    // attempts = num_procs
    _b.LoadGlobal(VReg.Scratch0, "__sched_num_procs");
    _b.StoreLocal(1, VReg.Scratch0);
    _b.StoreLocal(2, VReg.Scratch0);

    // === Steal loop ===
    _b.DefineLabel("__gt_steal_loop");
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.JumpIfZero(VReg.Scratch0, "__gt_steal_fail");

    // attempts--
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(1, VReg.Scratch0);

    // xorshift64 on P->rng
    _b.LoadLocal(VReg.Scratch1, 0);
    EmitXorshift64(VReg.Scratch0, VReg.Scratch1);
    // Scratch0 = rng value, Scratch1 = P*

    // victim_idx = rng % num_procs (register divisor)
    _b.LoadLocal(VReg.Scratch3, 2);           // num_procs in Scratch3
    _b.UDivRemainderReg(VReg.Scratch2, VReg.Scratch0, VReg.Scratch3);
    // Scratch2 = victim_idx

    // victim P* = procs[victim_idx]
    _b.LoadGlobal(VReg.Scratch0, "__sched_procs");
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0);
    _b.StoreLocal(3, VReg.Scratch0);          // save victim P*

    // Skip self
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.Equal, "__gt_steal_loop");

    // Skip inactive workers
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, POffStatus);
    _b.CmpRegImm(VReg.Scratch1, PStatusActive);
    _b.JumpIf(Condition.NotEqual, "__gt_steal_loop");

    // Lock
    _b.LockAcquire(_b.SchedLockLabel);

    // Check victim's local queue length (need at least 2)
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, POffLocalQueueLen);
    _b.CmpRegImm(VReg.Scratch1, 2);
    _b.JumpIf(Condition.Less, "__gt_steal_unlock_skip");

    // n = len / 2
    _b.ShrRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(5, VReg.Scratch1);          // save n

    // first = victim->localQueueHead
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, POffLocalQueueHead);
    _b.StoreLocal(4, VReg.Scratch1);          // save first stolen (return value)

    // Walk n-1 more nodes to find the split point
    _b.StoreLocal(7, VReg.Scratch1);          // walk pointer = first
    _b.LoadLocal(VReg.Scratch2, 5);           // n
    _b.SubRegImm(VReg.Scratch2, 1);           // walk n-1
    _b.StoreLocal(6, VReg.Scratch2);          // walk counter

    _b.DefineLabel("__gt_steal_walk");
    _b.LoadLocal(VReg.Scratch2, 6);
    _b.JumpIfZero(VReg.Scratch2, "__gt_steal_walk_done");
    _b.LoadLocal(VReg.Scratch0, 7);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, GtOffNext);
    _b.StoreLocal(7, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch2, 6);
    _b.SubRegImm(VReg.Scratch2, 1);
    _b.StoreLocal(6, VReg.Scratch2);
    _b.Jump("__gt_steal_walk");

    _b.DefineLabel("__gt_steal_walk_done");
    // slot 7 = last stolen node
    // new_victim_head = last_stolen->next
    _b.LoadLocal(VReg.Scratch0, 7);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffNext);
    // Terminate stolen chain: last_stolen->next = NULL
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch2);

    // Update victim: head = new_head, len -= n
    _b.LoadLocal(VReg.Scratch0, 3);           // victim P*
    _b.StoreIndirect(VReg.Scratch0, POffLocalQueueHead, VReg.Scratch1);
    // If new head == NULL, clear tail
    _b.JumpIfNonZero(VReg.Scratch1, "__gt_steal_victim_nonempty");
    _b.ZeroReg(VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.StoreIndirect(VReg.Scratch0, POffLocalQueueTail, VReg.Scratch1);

    _b.DefineLabel("__gt_steal_victim_nonempty");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, POffLocalQueueLen);
    _b.LoadLocal(VReg.Scratch2, 5);           // n
    _b.SubRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, POffLocalQueueLen, VReg.Scratch1);

    // Add stolen items (except first) to our local queue
    _b.LoadLocal(VReg.Scratch0, 4);           // first stolen
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffNext); // second stolen
    _b.JumpIfZero(VReg.Scratch1, "__gt_steal_got_one");

    // We have extra stolen items [second..last] to add to our local queue
    _b.LoadLocal(VReg.Scratch0, 0);           // our P*
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, POffLocalQueueTail);
    _b.JumpIfNonZero(VReg.Scratch2, "__gt_steal_local_nonempty");
    // Our local queue empty: head = second
    _b.StoreIndirect(VReg.Scratch0, POffLocalQueueHead, VReg.Scratch1);
    _b.Jump("__gt_steal_set_tail");

    _b.DefineLabel("__gt_steal_local_nonempty");
    // old_tail->next = second
    _b.StoreIndirect(VReg.Scratch2, GtOffNext, VReg.Scratch1);

    _b.DefineLabel("__gt_steal_set_tail");
    // tail = last_stolen
    _b.LoadLocal(VReg.Scratch1, 7);           // last stolen
    _b.LoadLocal(VReg.Scratch0, 0);           // our P*
    _b.StoreIndirect(VReg.Scratch0, POffLocalQueueTail, VReg.Scratch1);
    // len += (n - 1)
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, POffLocalQueueLen);
    _b.LoadLocal(VReg.Scratch2, 5);           // n
    _b.SubRegImm(VReg.Scratch2, 1);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch0, POffLocalQueueLen, VReg.Scratch1);

    _b.DefineLabel("__gt_steal_got_one");
    _b.LockRelease(_b.SchedLockLabel);

    // Return first stolen with cleared next
    _b.LoadLocal(VReg.Scratch0, 4);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch1);
    // dbg: dequeue (first_stolen, kind=steal_first). The dispatched event
    // includes our P->id (loaded by helper); the formatter shows the GT and
    // owning P. Note: the rest of the chain (second..last_stolen) entered our
    // local queue without an explicit per-element enqueue event, so emit a
    // single steal_chain enqueue tagged with last_stolen if we stole >1.
    _b.StoreLocal(8, VReg.Scratch0); // save first_stolen for return
    EmitDbgDequeue(VReg.Scratch0, DsDbgQueueStealFirst, 0);
    // If n > 1, also log a steal_chain event for the chain we appended.
    _b.LoadLocal(VReg.Scratch1, 5); // n
    _b.CmpRegImm(VReg.Scratch1, 1);
    var noChainLabel = UniqueLabel("steal_no_chain");
    _b.JumpIf(Condition.BelowEqual, noChainLabel);
    _b.LoadLocal(VReg.Scratch0, 7); // last_stolen
    EmitDbgEnqueue(VReg.Scratch0, DsDbgQueueStealChain, 0);
    _b.DefineLabel(noChainLabel);
    _b.LoadLocal(VReg.Scratch0, 8);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel("__gt_steal_unlock_skip");
    _b.LockRelease(_b.SchedLockLabel);
    _b.Jump("__gt_steal_loop");

    _b.DefineLabel("__gt_steal_fail");
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>
  /// <c>maxon_yield()</c> — the runtime behind <c>Runtime.yield()</c>: give up the M cooperatively,
  /// let something else run, come back. Takes nothing, returns nothing, cannot throw.
  ///
  /// ⭐ IT IS NOT <c>sleep(0)</c>, AND THE DIFFERENCE IS THE TIMER HEAP. <c>maxon_sleep</c> publishes
  /// a (deadline, gt) entry into a 256-slot global min-heap and waits for <c>__gt_timer_check</c> to
  /// fire it; a spin that yields thousands of times a second through that door would churn a shared,
  /// locked, FIXED-CAPACITY structure to express "I have nothing to wait for". This consumes no
  /// timer slot, takes the timer lock only through the poll it drives, and has no deadline to miss.
  ///
  /// ⭐⭐ THE FIRST THING IT DOES IS <see cref="IEmitterBackend.DriveSchedulerAndIo"/>, AND THAT IS
  /// THE WHOLE POINT OF THE FUNCTION. The canonical caller is <c>while not done { Runtime.yield() }</c>
  /// standing over outstanding I/O, and under <c>MAXON_MAX_PROCS=1</c> the spinning M is the only one
  /// in the process: an engine nobody polls is one whose parked GTs never wake, so a yield that
  /// merely rescheduled would turn that loop into a hang rather than a wait.
  ///
  /// Two arms, split on the question every park site in this runtime asks — does this GT have a
  /// schedulable stack of its own? <c>stackBase == 0</c> is how <c>__gt_process_pending_waiter</c>
  /// and <c>__netpoll_commit</c> already spell it, and it is the same predicate x86's
  /// <c>EmitJumpIfMainThread</c> spells as <c>gt == &amp;P-&gt;mainThread</c>:
  ///
  ///   * a P's inline <b>mainThread</b> is never on a run queue and cannot switch to itself, so it
  ///     BECOMES the scheduler for one turn: run one ready GT inline and resume when that GT parks
  ///     or completes. With nothing runnable it simply returns, which is the specified "if nothing
  ///     else is runnable, the caller continues" — and is also the whole of what a `main` with no
  ///     green threads at all ever does here.
  ///   * a <b>worker GT</b> hands its M back to that M's scheduler loop.
  ///
  /// ⚠⚠ THE WORKER ARM MAY NOT ENQUEUE ITSELF AND THEN SWITCH, WHICH IS THE OBVIOUS SPELLING OF
  /// "back of the run queue, then switch" AND IS A USE-AFTER-FREE. Between the enqueue and
  /// <c>__gt_context_switch</c>'s register save, the GT is visible to every other M's
  /// <c>__gt_dequeue</c> — and dequeue has no <c>ioYielded</c> gate, because by contract nothing
  /// reaches a run queue while still running. A second M resumes it onto the SP saved at its
  /// PREVIOUS suspension: two Ms on one stack, the <c>--workers&gt;=5</c> crash
  /// <c>__gt_timer_check</c>'s park gate documents. So the re-enqueue is DEFERRED: the arm publishes
  /// itself into <c>P-&gt;pendingYielder</c> and switches to <c>&amp;P-&gt;mainThread</c>, and
  /// <see cref="EmitDrainPendingYielder"/> — at the top of <c>__gt_dequeue</c>, on the other side of
  /// that switch — performs the enqueue.
  ///
  /// ⛔ IT DELIBERATELY DOES NOT USE <c>P-&gt;pendingWaiter</c>, WHICH IS THE OTHER DEFERRED-ENQUEUE
  /// CHANNEL AND WAS THIS FUNCTION'S FIRST, WRONG ANSWER. That channel drains into
  /// <c>__gt_enqueue</c>, whose whole job is the FRONT of the queue: the yielder landed in
  /// <c>P-&gt;runnext</c> — the slot it had just vacated — and the next statement on every scheduler
  /// path is <c>__gt_dequeue</c>, which takes runnext first. The yield handed the M back to itself.
  /// The deferral was right; the end of the queue was not. See <see cref="EmitDrainPendingYielder"/>
  /// for why the two are separate requirements.
  ///
  /// ⚠ <c>pendingWaiter</c> STILL HAS TO BE DRAINED BY THE MAIN ARM ON RESUME, for the reason it
  /// always did: the GT that arm switches to may COMPLETE, and its trampoline stores its own awaiter
  /// in that one-entry slot before switching back to us. Returning to user code without draining
  /// would strand that awaiter until the next park — a wakeup deferred by an unbounded amount of user
  /// computation. Every other main-thread park loop drains at its loop top; this one has no loop, so
  /// it drains at the one place it resumes.
  ///
  /// ⚠ THE ONE "RUN A DEQUEUED GT" SPELLING THIS FILE ADDS OMITS <c>next.ioYielded = 1</c>, WHICH
  /// SEVEN ARM64 COPIES SET AND SIX X86 COPIES DO NOT — a live divergence between the backends that
  /// nobody had written down. It is safe to omit because <c>__gt_context_switch</c> is itself the
  /// only writer of that word and already publishes <c>from.ioYielded = 1</c> after saving the
  /// outgoing GT: when <c>next</c> later switches away, the flag is set by the switch, not by
  /// whoever ran it. arm64's copies set it on RESUME, where it is a redundant re-assertion of what
  /// the switch already did. Stated here because it is the newest site and had no other home; the
  /// thirteen spellings are noted, not converged.
  /// </summary>
  public void EmitMaxonYield() {
    _b.FunctionStart("maxon_yield", 0, 0x40);

    const int slotGt = 0;
    const int slotNext = 1;
    var doneLabel = UniqueLabel("maxon_yield_done");
    var handBackLabel = UniqueLabel("maxon_yield_hand_back");

    // No processor, no green thread to reschedule — return rather than dereference. Every Maxon
    // frame runs after __gt_init, so what this actually covers is a call from an OS thread that
    // owns no P (the IOCP drain thread, the sync-I/O workers).
    EmitLoadCurrentGtOrZero(VReg.Scratch0);
    _b.JumpIfZero(VReg.Scratch0, doneLabel);
    _b.StoreLocal(slotGt, VReg.Scratch0);

    _b.DriveSchedulerAndIo();

    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffStackBase);
    _b.JumpIfNonZero(VReg.Scratch1, handBackLabel);

    // --- mainThread arm: be the scheduler for one turn ---
    _b.Call("__gt_dequeue");
    _b.JumpIfZero(VReg.Ret, doneLabel);
    _b.StoreLocal(slotNext, VReg.Ret);

    // It came off a run queue, so we are the one who marks it running — the convention that does
    // NOT apply to &P->mainThread (see IEmitterBackend.SwitchToMainThread).
    _b.MovRegImm(VReg.Scratch1, GtStatusRunning);
    _b.StoreIndirect(VReg.Ret, GtOffStatus, VReg.Scratch1);

    _b.LoadCurrentP(VReg.Arg2);
    _b.LoadLocal(VReg.Arg0, slotGt);
    _b.LoadLocal(VReg.Arg1, slotNext);
    _b.Call("__gt_context_switch");

    // Resumed: that GT parked or completed. Drain whatever its completion left behind.
    _b.Call("__gt_process_pending_waiter");
    _b.Jump(doneLabel);

    // --- worker-GT arm: deferred BACK-of-queue self re-enqueue, then hand the M back ---
    _b.DefineLabel(handBackLabel);
    _b.LoadCurrentP(VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch0, slotGt);
    _b.StoreIndirect(VReg.Scratch1, POffPendingYielder, VReg.Scratch0);
    _b.SwitchToMainThread();

    _b.DefineLabel(doneLabel);
    _b.FunctionEnd();
  }

  // =========================================================================
  // __gt_timer_check(): Check and fire expired timers from the min-heap.
  // =========================================================================
  //
  // Stack slots:
  //   0 = now_nanos
  //   1 = heap_base address
  //   2 = saved gt (to enqueue)
  //   3 = sift-down loop variable i
  //   4,5 = out-param buffer for GetCurrentTimeNanos (QPC ticks + QPF frequency
  //         on Windows, struct timespec on POSIX -- two slots on both)
  //
  // Frame size: 0x50
  // =========================================================================

  /// <summary>
  /// Jump to <paramref name="targetLabel"/> when <paramref name="gtReg"/> is the GT currently
  /// running on this P — i.e. "this timer belongs to US". Both of __gt_timer_check's park-gate
  /// decisions turn on that one question, and it is the kind of test that must not drift
  /// between its two uses: the gate lets a self-owned timer through, and the fire path relies
  /// on that same answer to skip the enqueue. Written once so they cannot disagree.
  /// Clobbers Scratch2; leaves <paramref name="gtReg"/> intact.
  /// </summary>
  private void EmitJumpIfCurrentGt(VReg gtReg, string targetLabel) {
    _b.LoadCurrentP(VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch2, POffCurrentGt);
    _b.CmpRegReg(VReg.Scratch2, gtReg);
    _b.JumpIf(Condition.Equal, targetLabel);
  }

  public void EmitGtTimerCheck() {
    _b.FunctionStart("__gt_timer_check", 0, 0x50);

    // Fast path: if count == 0, return immediately
    _b.LoadGlobal(VReg.Scratch0, "__gt_timer_count");
    _b.JumpIfZero(VReg.Scratch0, "__gt_timer_check_ret");

    // Acquire timer lock
    _b.LockAcquire(_b.TimerLockLabel);

    // Reload count (may have changed while acquiring lock)
    _b.LoadGlobal(VReg.Scratch0, "__gt_timer_count");
    _b.JumpIfZero(VReg.Scratch0, "__gt_timer_check_unlock");

    // Deadlines are absolute nanoseconds (see GtLayout.TimerNanosPerMilli): read the
    // same high-resolution clock maxon_sleep anchored them to. Reading the coarse tick
    // here instead is what let a deadline compare "expired" up to 15.6 ms early.
    // Only paid when the heap is non-empty -- the count==0 fast path returns above.
    _b.GetCurrentTimeNanos(VReg.Scratch0, 4); // result in Scratch0, uses slots 4-5
    _b.StoreLocal(0, VReg.Scratch0);          // save now_nanos

    // Cache heap base address
    _b.LeaGlobal(VReg.Scratch0, "__gt_timer_heap");
    _b.StoreLocal(1, VReg.Scratch0);

    // --- Main loop: check heap[0] ---
    _b.DefineLabel("__gt_timer_check_loop");
    _b.LoadGlobal(VReg.Scratch0, "__gt_timer_count");
    _b.JumpIfZero(VReg.Scratch0, "__gt_timer_check_unlock");

    // Load heap[0].deadline
    _b.LoadLocal(VReg.Scratch0, 1);           // heap_base
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, TimerOffDeadline);
    // Compare deadline vs now (both absolute nanoseconds)
    _b.LoadLocal(VReg.Scratch2, 0);           // now_nanos
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Above, "__gt_timer_check_unlock"); // deadline > now -> not yet due

    // Save gt from heap[0]
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, TimerOffGt);
    _b.StoreLocal(2, VReg.Scratch1);

    // --- PARK GATE: never fire a GT that is still running on some M's stack ---
    //
    // maxon_sleep publishes into this GLOBAL heap (__gt_timer_add) and only THEN parks:
    // it still has to __gt_dequeue a successor and __gt_context_switch away, and when the
    // dequeue comes up empty it runs the scheduler INLINE on its own stack (the
    // __sleep_mainthread_loop park loop, which can block in kevent for milliseconds).
    // Throughout that stretch the entry is visible to every other M polling here. Firing it
    // would enqueue a GT that is mid-execution; a third M then dequeues it and context
    // switches in, restoring the STALE gt.sp saved at its previous suspension — so two Ms
    // run one GT on two different stacks. That is the --workers>=5 crash: caught under lldb
    // with P0->currentGt == P4->currentGt, one of them executing on the pre-__gt_morestack
    // stack the other had already relocated and munmapped.
    //
    // ioYielded==1 is this runtime's existing "parked, off-stack, safe to hand to another M"
    // signal — the same gate __gt_process_pending_waiter, __io_op_done and
    // EmitAwaitedStackVacatedGate already stand on. The timer was the one wakeup path that
    // never got it.
    //
    // The gate is checked BEFORE the pop, so a not-yet-parked GT stays in the heap and fires
    // on a later poll. Popping first and skipping the enqueue instead would LOSE the wakeup:
    // nothing would ever put that GT back on a run queue.
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, GtOffIoYielded);
    _b.JumpIfNonZero(VReg.Scratch2, "__gt_timer_check_may_fire");

    // Not parked. The one safe exception is "it is US": a sleeping GT polls this from its own
    // park loop, and nothing else can be handed a GT that is this P's currentGt. Without this
    // exception a GT in that loop could never fire its OWN deadline — it would wait for itself
    // to park, and it cannot park until it wakes.
    //
    // P is non-NULL here. Every caller of __gt_timer_check is a scheduler or green-thread
    // context that owns a P (__sched_worker_loop, the __gt_await / __gt_try_await / sleep park
    // loops, __io_poll_kqueue's return path); the one P-less thread, __io_sync_worker_loop,
    // calls neither this nor __io_poll_kqueue. That is why this needs no NULL check while
    // __gt_enqueue, which IS reachable from that thread, carries one.
    EmitJumpIfCurrentGt(VReg.Scratch1, "__gt_timer_check_may_fire");

    // Running on ANOTHER M. Leave heap[0] untouched and stop scanning: this is a min-heap, so
    // every remaining deadline is later than one we are declining to fire. Head-of-line delay
    // is bounded by how long that M takes to park (the dequeue/switch window) or by its own
    // next park-loop poll, which fires it through the exception above.
    _b.Jump("__gt_timer_check_unlock");

    _b.DefineLabel("__gt_timer_check_may_fire");

    // count--
    _b.LoadGlobal(VReg.Scratch0, "__gt_timer_count");
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal("__gt_timer_count", VReg.Scratch0);

    // If count is now 0, skip sift-down (heap is empty)
    _b.JumpIfZero(VReg.Scratch0, "__gt_timer_check_fire");

    // Move heap[count] to heap[0]
    // src = heap_base + count * 16
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0); // count (new, decremented)
    _b.ShlRegImm(VReg.Scratch1, 4);           // count * 16
    _b.LoadLocal(VReg.Scratch0, 1);           // heap_base
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch0); // &heap[count]
    // Copy 16 bytes: heap[0] = heap[count]
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 0);  // deadline
    _b.StoreIndirect(VReg.Scratch0, 0, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, 8);  // gt
    _b.StoreIndirect(VReg.Scratch0, 8, VReg.Scratch2);

    // Sift-down from index 0
    _b.ZeroReg(VReg.Scratch0);
    _b.StoreLocal(3, VReg.Scratch0);          // i = 0

    _b.DefineLabel("__gt_timer_sift_down");
    _b.LoadLocal(VReg.Scratch0, 3);           // i
    // left = 2*i + 1
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0);
    _b.ShlRegImm(VReg.Scratch1, 1);
    _b.AddRegImm(VReg.Scratch1, 1);           // left = 2*i + 1

    // if left >= count: done
    _b.LoadGlobal(VReg.Scratch2, "__gt_timer_count");
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.AboveEqual, "__gt_timer_check_fire");

    // smallest = left (stored in slot 3 temporarily; we'll update at the end)
    // For the sift-down we need to track: i, left, right, smallest, and load deadlines.
    // We have 4 scratch registers: Scratch0-3.
    // Strategy:
    //   Scratch0 = i (from slot)
    //   Scratch1 = left
    //   Scratch2 = count (already loaded)
    //   Scratch3 = right
    // We'll store smallest in Scratch1 initially.
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1);
    _b.AddRegImm(VReg.Scratch3, 1);           // right = left + 1

    // Load heap_base for address calculations
    // We need 5+ values but only have 4 scratch regs. Use stack for intermediates.
    // Save i, left, right to stack, then compute with freed registers.
    // Actually, let's use a simpler approach: compute addresses one at a time.

    // Check if right < count
    _b.CmpRegReg(VReg.Scratch3, VReg.Scratch2);
    _b.JumpIf(Condition.AboveEqual, "__gt_timer_sift_cmp_parent");

    // Compare heap[right].deadline vs heap[left].deadline
    _b.LoadLocal(VReg.Scratch0, 1);           // heap_base
    // &heap[right]
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.ShlRegImm(VReg.Scratch2, 4);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch0); // &heap[right]
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch2, 0); // heap[right].deadline
    // &heap[left]
    _b.MovRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.ShlRegImm(VReg.Scratch0, 4);
    _b.LoadLocal(VReg.Scratch3, 1);           // heap_base (reload, Scratch3 was right)
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch3); // &heap[left]
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch0, 0); // heap[left].deadline

    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch0); // right vs left deadline
    _b.JumpIf(Condition.AboveEqual, "__gt_timer_sift_cmp_parent"); // right >= left, smallest = left
    // smallest = right; reload right index
    _b.LoadLocal(VReg.Scratch0, 3);           // i
    _b.ShlRegImm(VReg.Scratch0, 1);
    _b.AddRegImm(VReg.Scratch0, 1);           // left
    _b.AddRegImm(VReg.Scratch0, 1);           // right
    _b.MovRegReg(VReg.Scratch1, VReg.Scratch0); // smallest = right

    _b.DefineLabel("__gt_timer_sift_cmp_parent");
    // Scratch1 = smallest index
    // Compare heap[smallest].deadline vs heap[i].deadline
    _b.LoadLocal(VReg.Scratch0, 1);           // heap_base
    _b.MovRegReg(VReg.Scratch2, VReg.Scratch1); // smallest
    _b.ShlRegImm(VReg.Scratch2, 4);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch0); // &heap[smallest]
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch2, 0); // heap[smallest].deadline

    _b.LoadLocal(VReg.Scratch2, 3);           // i
    _b.ShlRegImm(VReg.Scratch2, 4);
    _b.AddRegReg(VReg.Scratch2, VReg.Scratch0); // &heap[i]
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch2, 0); // heap[i].deadline

    _b.CmpRegReg(VReg.Scratch3, VReg.Scratch0); // smallest vs i
    _b.JumpIf(Condition.AboveEqual, "__gt_timer_check_fire"); // heap property restored

    // Swap heap[i] and heap[smallest]
    // Scratch2 = &heap[i] (still valid from above)
    // Recompute &heap[smallest]
    _b.LoadLocal(VReg.Scratch0, 1);           // heap_base
    _b.MovRegReg(VReg.Scratch3, VReg.Scratch1); // smallest
    _b.ShlRegImm(VReg.Scratch3, 4);
    _b.AddRegReg(VReg.Scratch3, VReg.Scratch0); // &heap[smallest]
    // Now Scratch2 = &heap[i], Scratch3 = &heap[smallest]
    // Swap deadline (offset 0)
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch2, 0); // i.deadline
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch3, 0);     // smallest.deadline (use Arg0 as temp)
    _b.StoreIndirect(VReg.Scratch2, 0, VReg.Arg0);
    _b.StoreIndirect(VReg.Scratch3, 0, VReg.Scratch0);
    // Swap gt (offset 8)
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch2, 8);
    _b.LoadIndirect(VReg.Arg0, VReg.Scratch3, 8);
    _b.StoreIndirect(VReg.Scratch2, 8, VReg.Arg0);
    _b.StoreIndirect(VReg.Scratch3, 8, VReg.Scratch0);

    // i = smallest
    _b.StoreLocal(3, VReg.Scratch1);
    _b.Jump("__gt_timer_sift_down");

    // --- Fire the expired GT ---
    _b.DefineLabel("__gt_timer_check_fire");
    _b.LoadLocal(VReg.Scratch0, 2);           // gt
    // Set status = ready
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, GtOffStatus, VReg.Scratch1);
    // dbg: status->ready, then timer_fire
    EmitDbgStatusStore(VReg.Scratch0, /*old*/-1, /*new=ready*/0, DsStatusSiteTimerFireReady);
    _b.LoadLocal(VReg.Scratch0, 2);           // reload gt (helper clobbered)
    EmitDbgTimerFire(VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch0, 2);           // reload again
    // Skip enqueue if mainThread (stackBase == 0)
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffStackBase);
    _b.JumpIfZero(VReg.Scratch1, "__gt_timer_check_skip_enqueue");
    // Skip enqueue if the GT is US — the park-gate exception above. status=ready is the whole
    // wakeup: maxon_sleep's park loop rechecks its own status every iteration and resumes
    // inline. Enqueueing would publish a GT that is running on this very stack, which is the
    // double-schedule the gate exists to prevent. Same contract as the mainThread skip.
    EmitJumpIfCurrentGt(VReg.Scratch0, "__gt_timer_check_skip_enqueue");
    // Enqueue the expired GT (first arg = gt in slot 0... but we need to set up arg)
    // Call convention: arg0 goes in Arg0 register. On x86 that's RCX, on ARM64 that's X0.
    // The Call("__gt_enqueue") expects the argument in the platform's first arg register.
    // Since __gt_enqueue is a function with 1 arg, we need gt in Arg0.
    _b.MovRegReg(VReg.Arg0, VReg.Scratch0);
    _b.Call("__gt_enqueue");
    _b.DefineLabel("__gt_timer_check_skip_enqueue");

    _b.Jump("__gt_timer_check_loop");

    _b.DefineLabel("__gt_timer_check_unlock");
    _b.LockRelease(_b.TimerLockLabel);

    _b.DefineLabel("__gt_timer_check_ret");
    _b.FunctionEnd();
  }

  // =========================================================================
  // __gt_timer_add(gt, deadline): Add a GT to the timer min-heap.
  //
  // `deadline` is an absolute nanosecond instant on the monotonic clock (see
  // GtLayout.TimerNanosPerMilli). This function only ever compares deadlines
  // against each other for the sift-up, so it is agnostic to the unit -- but
  // every caller must agree with __gt_timer_check on what it is.
  // =========================================================================
  //
  // Stack slots:
  //   0 = gt (arg0)
  //   1 = deadline (arg1)
  //   2 = i (insertion index)
  //   3 = parent index
  //   4 = heap_base
  //
  // Frame size: 0x40
  // =========================================================================

  public void EmitGtTimerAdd() {
    _b.FunctionStart("__gt_timer_add", 2, 0x40);

    // Lock
    _b.LockAcquire(_b.TimerLockLabel);

    // i = count; count++
    _b.LoadGlobal(VReg.Scratch0, "__gt_timer_count");
    _b.StoreLocal(2, VReg.Scratch0);          // save i
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreGlobal("__gt_timer_count", VReg.Scratch0);

    // Cache heap base
    _b.LeaGlobal(VReg.Scratch0, "__gt_timer_heap");
    _b.StoreLocal(4, VReg.Scratch0);

    // &heap[i] = base + i * 16
    _b.LoadLocal(VReg.Scratch1, 2);           // i
    _b.ShlRegImm(VReg.Scratch1, 4);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch1); // &heap[i]
    // Store deadline and gt
    _b.LoadLocal(VReg.Scratch1, 1);           // deadline
    _b.StoreIndirect(VReg.Scratch0, TimerOffDeadline, VReg.Scratch1);
    _b.LoadLocal(VReg.Scratch1, 0);           // gt
    _b.StoreIndirect(VReg.Scratch0, TimerOffGt, VReg.Scratch1);

    // Sift up
    _b.DefineLabel("__gt_timer_sift_up");
    _b.LoadLocal(VReg.Scratch0, 2);           // i
    _b.JumpIfZero(VReg.Scratch0, "__gt_timer_add_done"); // at root

    // parent = (i - 1) / 2
    _b.SubRegImm(VReg.Scratch0, 1);
    _b.ShrRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);          // save parent

    // Load heap base
    _b.LoadLocal(VReg.Scratch3, 4);           // heap_base

    // &heap[i]
    _b.LoadLocal(VReg.Scratch0, 2);           // i
    _b.ShlRegImm(VReg.Scratch0, 4);
    _b.AddRegReg(VReg.Scratch0, VReg.Scratch3); // &heap[i]

    // &heap[parent]
    _b.LoadLocal(VReg.Scratch1, 3);           // parent
    _b.ShlRegImm(VReg.Scratch1, 4);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch3); // &heap[parent]

    // Compare deadlines
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, TimerOffDeadline); // heap[i].deadline
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, TimerOffDeadline); // heap[parent].deadline
    _b.CmpRegReg(VReg.Scratch2, VReg.Scratch3);
    _b.JumpIf(Condition.AboveEqual, "__gt_timer_add_done"); // i >= parent, done

    // Swap heap[i] and heap[parent]
    // Scratch0 = &heap[i], Scratch1 = &heap[parent]
    // Scratch2 = heap[i].deadline, Scratch3 = heap[parent].deadline
    // Swap deadlines
    _b.StoreIndirect(VReg.Scratch0, TimerOffDeadline, VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch1, TimerOffDeadline, VReg.Scratch2);
    // Swap gt pointers
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, TimerOffGt);
    _b.LoadIndirect(VReg.Scratch3, VReg.Scratch1, TimerOffGt);
    _b.StoreIndirect(VReg.Scratch0, TimerOffGt, VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch1, TimerOffGt, VReg.Scratch2);

    // i = parent
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.StoreLocal(2, VReg.Scratch0);
    _b.Jump("__gt_timer_sift_up");

    _b.DefineLabel("__gt_timer_add_done");
    _b.LockRelease(_b.TimerLockLabel);
    _b.FunctionEnd();
  }

  // ===========================================================================================
  // THE AWAIT COMPLETION HAND-OFF
  // ===========================================================================================

  private const string AwaitHandoffCorruptMsg = "__gt_await_msg_handoff_corrupt";

  private const string AwaitHandoffNoWaiterMsg = "__gt_await_msg_handoff_no_waiter";

  /// <summary>
  /// The hand-off's panic strings. Emitted from a backend's globals block, beside
  /// <see cref="EmitNetpollGlobals"/> and from the same <c>RuntimeEmitter</c> instance.
  /// </summary>
  public void EmitGtAwaitHandoffGlobals() {
    _b.DefineSymdata(AwaitHandoffCorruptMsg, System.Text.Encoding.UTF8.GetBytes(
      "runtime: corrupted await handoff\n\0"));
    _b.DefineSymdata(AwaitHandoffNoWaiterMsg, System.Text.Encoding.UTF8.GetBytes(
      "runtime: await handoff parked with no waiter\n\0"));
  }

  /// <summary>
  /// THE AWAIT COMPLETION HAND-OFF — who enqueues an awaiter when the green thread it awaits
  /// finishes, the awaiter itself or the completing child. Two functions, one word
  /// (<see cref="GtLayout.GtOffAwaitHandoff"/>, on the PROMISE), one atomic operation per side.
  ///
  /// ⭐ IT IS THE SAME QUESTION <c>RuntimeEmitter.Netpoll.cs</c> ANSWERS FOR I/O, AND THE ANSWER
  /// HAS THE SAME SHAPE ON PURPOSE. An awaiter publishes <c>promise.waiter = self</c> and then
  /// keeps running its own scheduling loop, so a completer reading that field faces exactly the
  /// question <c>netpoll</c>'s guard (c) could not answer: is this waiter about to park, or is it
  /// about to notice the completion itself? Those two are the SAME INSTANT, and no snapshot of
  /// <c>ioYielded</c> separates them. The word does: the awaiter CASes
  /// <c>Nil -&gt; Parked</c> as the last branch before its context switch, the completer CASes
  /// <c>Nil -&gt; Completed</c> after publishing <c>status</c>, and exactly one of them wins.
  ///
  /// ⚠ WHAT IT REPLACES ON x86 WAS NOT A WEAKER GUARD, IT WAS NO GUARD.
  /// <c>__gt_process_pending_waiter</c> enqueued the deferred awaiter unconditionally, so a child
  /// completing on another M while its awaiter was still executing put a RUNNING green thread in
  /// the run queue, and a third M resumed it onto the SP saved at its previous suspension — one
  /// green thread on two Ms. Reproduced deterministically by widening the awaiter's window with
  /// <c>MAXON_GT_PARK_DELAY_MS</c> (see <see cref="EmitGtAwaitCommitPark"/>): a nil-deref inside
  /// the re-entered thread, every run.
  ///
  /// ⚠ AND WHY THIS IS NOT arm64's <c>__gt_ppw_spin</c> PORTED. That gate waits for
  /// <c>w.ioYielded == 1</c> and rests its termination on "the await idle path guarantees the
  /// awaiter always reaches a context switch" — which is TRUE of arm64's await loop, whose status
  /// recheck sits AFTER the switch, and FALSE of this one, whose recheck sits before it. An
  /// awaiter here can see its promise completed and leave <c>__gt_await</c> for user code without
  /// ever switching again, at <c>ioYielded == 0</c> for as long as it likes, so a transplanted
  /// spin trades a rare double-schedule for a rare unbounded wait. The word supplies the missing
  /// premise instead of assuming it: <c>Parked</c> is published only where the run to
  /// <c>__gt_context_switch</c> IS straight-line, so the wait that follows it is the bounded one
  /// (<see cref="EmitStackVacatedGate"/>).
  /// </summary>
  public void EmitGtAwaitHandoffFunctions() {
    EmitGtAwaitCommitPark();
    EmitGtAwaitHandoffClaim();
  }

  /// <summary>
  /// <c>__gt_await_commit_park(promise)</c> -&gt; non-zero when the caller may park, 0 when it must
  /// NOT. The awaiter's half, and the LAST instruction that can still change its mind — the
  /// counterpart of <c>__netpoll_commit</c>, and it carries the same two rules.
  ///
  /// ⚠ IT MUST BE THE LAST BRANCH BEFORE <c>__gt_context_switch</c> — nothing between this call and
  /// the switch may turn the caller around, because a completer that loses its CAS against the
  /// <c>Parked</c> this publishes waits for the caller's <c>ioYielded</c>. Calls in between are
  /// tolerable (<c>__gt_dequeue</c> is one) as long as none of them can decide not to park and none
  /// can wait on another green thread's progress; a BRANCH back would be a hang.
  ///
  /// ⚠ A ZERO RETURN MEANS "THE CHILD COMPLETED", so every abort path must reach a recheck of
  /// <c>promise.status</c> rather than reading a result straight out. The completer publishes
  /// <c>status</c> (and <c>result</c> / <c>threw</c> before it) ahead of the CAS this loses to, and
  /// a failing <c>AtomicCAS</c> is an acquire on that same word, so the recheck is guaranteed to
  /// see <c>completed</c> — which is what makes "loop back to the top" a terminating abort rather
  /// than a spin.
  ///
  /// ⚠ THE MAIN OS THREAD NEVER COMMITS, on the <c>stackBase == 0</c> predicate
  /// <c>__netpoll_commit</c> and <c>__gt_process_pending_waiter</c> already use. It has no
  /// schedulable stack and nothing ever enqueues it, so publishing <c>Parked</c> for it would hand
  /// a completer a wait for an <c>ioYielded</c> that a running thread never sets. It still gets its
  /// wakeup: see <see cref="EmitGtAwaitHandoffClaim"/>'s main-thread arm.
  /// </summary>
  private void EmitGtAwaitCommitPark() {
    _b.FunctionStart("__gt_await_commit_park", 1, 0x40);

    var proceedLabel = UniqueLabel("await_commit_proceed");
    var abortLabel = UniqueLabel("await_commit_abort");
    var abortOkLabel = UniqueLabel("await_commit_abort_ok");

    // No P, or no schedulable stack: nothing can enqueue this caller, so it must not publish
    // `Parked`. Both answers are "go on"; see the note above for why they are different STATES.
    EmitLoadCurrentGtOrZero(VReg.Scratch1);
    _b.JumpIfZero(VReg.Scratch1, proceedLabel);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, GtOffStackBase);
    _b.JumpIfZero(VReg.Scratch2, proceedLabel);

    // ⭐ FAULT INJECTION, THE PARKER'S HALF, and this is the SECOND parker it serves — the knob is
    // defined as the gap between a parker's last self-detect and its commit CAS, and an awaiter has
    // exactly that gap. Widen it and the completer lands squarely inside the window that used to be
    // unguarded here; see EmitGtAwaitHandoffFunctions for the reproduction.
    _b.Call(NetpollParkDelayFn);

    _b.LoadLocal(VReg.Scratch1, 0);                    // promise
    _b.MovRegImm(VReg.Scratch2, AwaitHandoffNil);      // expected
    _b.MovRegImm(VReg.Arg1, AwaitHandoffParked);       // desired
    _b.AtomicCAS(VReg.Scratch1, GtOffAwaitHandoff, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, abortLabel);

    _b.DefineLabel(proceedLabel);
    _b.MovRegImm(VReg.Scratch0, 1);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(abortLabel);
    // Re-read rather than trust the CAS's observed value in a register: the two backends agree only
    // that `expected` survives, not where a failure leaves what it saw. The word cannot move again —
    // `Completed` is terminal and this GT is its only other writer — so the re-read is exact.
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadAcquire(VReg.Scratch2, VReg.Scratch1, GtOffAwaitHandoff);
    _b.CmpRegImm(VReg.Scratch2, AwaitHandoffCompleted);
    _b.JumpIf(Condition.Equal, abortOkLabel);

    // `Parked` here means a second awaiter committed on one promise, which `await` being linear
    // (E3100) says cannot happen. Say so and stop, rather than spin on a recheck that will never
    // come true.
    EmitRuntimeThrow(AwaitHandoffCorruptMsg);

    _b.DefineLabel(abortOkLabel);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  /// <summary>
  /// <c>__gt_await_handoff_claim(promise)</c> -&gt; the GT to hand to <c>P-&gt;pendingWaiter</c>, or
  /// 0. The completer's half, called by the completion trampoline AFTER it has published
  /// <c>result</c>, <c>threw</c> and <c>status = completed</c> and BEFORE it stores anything into
  /// the pending-waiter slot.
  ///
  /// ⚠ THE PUBLISH COMES FIRST HERE AND LAST IN <c>netpoll</c>, AND THE RULE BEHIND BOTH IS ONE
  /// RULE: a waiter must never be released toward a result that is not there yet.
  /// <c>netpoll</c>'s waiter learns of its completion THROUGH the word, so the word must be
  /// released last; ours learns through <c>status</c>, which it was already reading, so
  /// <c>status</c> must be published before the word can send it there. Reversing either is the
  /// same defect.
  ///
  /// ⚠ THE WIN ARM STILL READS <c>promise.waiter</c>, AND THAT IS NOT A SECOND CHANNEL. Winning
  /// says only that no awaiter has COMMITTED; there may still be one registered and running, and if
  /// it is the main OS thread it is parked on a wake handle that nothing else will signal. Reading
  /// the field there is safe because the awaiter fences between publishing it and its first
  /// <c>status</c> recheck: if this load misses the store, that recheck cannot miss the
  /// <c>status</c> published above — the standard two-sided argument, and the reason that fence is
  /// not decoration.
  /// </summary>
  private void EmitGtAwaitHandoffClaim() {
    _b.FunctionStart("__gt_await_handoff_claim", 1, 0x40);

    var parkedLabel = UniqueLabel("await_handoff_parked");
    var noneLabel = UniqueLabel("await_handoff_none");
    var handOverLabel = UniqueLabel("await_handoff_hand_over");
    var waiterOkLabel = UniqueLabel("await_handoff_waiter_ok");

    _b.LoadLocal(VReg.Scratch1, 0);                       // promise
    _b.MovRegImm(VReg.Scratch2, AwaitHandoffNil);         // expected
    _b.MovRegImm(VReg.Arg1, AwaitHandoffCompleted);       // desired
    _b.AtomicCAS(VReg.Scratch1, GtOffAwaitHandoff, VReg.Scratch2, VReg.Arg1);
    _b.JumpIfZero(VReg.Scratch3, parkedLabel);

    // Won: nobody has committed to a park on this promise. An awaiter that exists is still running
    // and resumes itself off the `status` we published; it must NOT be enqueued.
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, GtOffWaiter);
    _b.JumpIfZero(VReg.Scratch0, noneLabel);
    // ...unless it is the main OS thread, which self-detects by POLLING and is therefore waiting on
    // its P's wake handle right now. It is never enqueued — __gt_process_pending_waiter turns this
    // same slot into a SetEvent for it — so handing it over costs nothing and saves a full park
    // timeout per await.
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch0, GtOffStackBase);
    _b.JumpIfNonZero(VReg.Scratch2, noneLabel);
    _b.Jump(handOverLabel);

    _b.DefineLabel(noneLabel);
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel(parkedLabel);
    // Lost: the word reads `Parked`. The awaiter committed, and the CAS it won is a release for the
    // `promise.waiter` it stored before it, so this load cannot miss.
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, GtOffWaiter);
    _b.JumpIfNonZero(VReg.Scratch0, waiterOkLabel);
    EmitRuntimeThrow(AwaitHandoffNoWaiterMsg);

    _b.DefineLabel(waiterOkLabel);
    // Home the awaiter over the promise — which is not needed again — because the gate below reads
    // its subject from a local slot. Then wait for the context save. Bounded; see
    // EmitStackVacatedGate.
    _b.StoreLocal(0, VReg.Scratch0);
    EmitStackVacatedGate(0);
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ReturnValue(VReg.Scratch0);

    // The main OS thread: hand it over WITHOUT the gate, which would never open — it is running,
    // not parked off its stack, and nothing will ever enqueue it.
    _b.DefineLabel(handOverLabel);
    _b.ReturnValue(VReg.Scratch0);
  }
}
