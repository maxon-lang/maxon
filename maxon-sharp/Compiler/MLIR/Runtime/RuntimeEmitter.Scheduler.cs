namespace MaxonSharp.Compiler.Mlir.Runtime;

/// <summary>
/// Scheduler functions emitted once for both platforms via IEmitterBackend.
/// Implements the GMP (goroutine-machine-processor) scheduling model:
/// enqueue, dequeue (with fairness), work stealing, and timer heap operations.
/// </summary>
public partial class RuntimeEmitter {

  // Scheduler constants
  public const int MaxLocalQueueLen  = 256;
  public const int MaxFreeListLen    = 64;
  public const int FairnessInterval  = 61;
  public const int TimerHeapCapacity = 256;
  public const int TimerEntrySize    = 16;
  public const int TimerOffDeadline  = 0;
  public const int TimerOffGt        = 8;
  public const int GtInitialStackSize   = 65536;
  public const int GtStackGuardMargin   = 4096;

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

  // =========================================================================
  // __gt_enqueue(gt): Add a GreenThread to the scheduling system.
  // =========================================================================
  //
  // Priority: runnext slot -> local queue -> global queue (overflow).
  // After enqueueing, tries to wake an idle worker or spawn a new one.
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
    _b.FunctionStart("__gt_enqueue", 1, 0x60);

    // gt.next = 0
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.ZeroReg(VReg.Scratch1);
    _b.StoreIndirect(VReg.Scratch0, GtOffNext, VReg.Scratch1);

    // Load P* from TLS (may be NULL if called from I/O worker)
    _b.LoadCurrentP(VReg.Scratch1);
    _b.StoreLocal(1, VReg.Scratch1);

    // If P* == NULL: go straight to global queue
    _b.JumpIfZero(VReg.Scratch1, "__gt_enqueue_global");

    // --- Try runnext slot ---
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffRunnext);
    _b.JumpIfNonZero(VReg.Scratch2, "__gt_enqueue_displace_runnext");

    // Runnext empty: P->runnext = gt
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch1, POffRunnext, VReg.Scratch0);
    _b.Jump("__gt_enqueue_wake");

    // --- Runnext occupied: displace old to local queue ---
    _b.DefineLabel("__gt_enqueue_displace_runnext");
    // Scratch2 = old runnext, Scratch1 = P*
    _b.ZeroReg(VReg.Scratch3);
    _b.StoreIndirect(VReg.Scratch2, GtOffNext, VReg.Scratch3);
    // P->runnext = new gt
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.StoreIndirect(VReg.Scratch1, POffRunnext, VReg.Scratch0);
    // Reuse slot 0 for the displaced GT
    _b.StoreLocal(0, VReg.Scratch2);

    // --- Local queue ---
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffLocalQueueLen);
    _b.CmpRegImm(VReg.Scratch2, MaxLocalQueueLen);
    _b.JumpIf(Condition.AboveEqual, "__gt_enqueue_global");

    // Append to local queue tail
    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffLocalQueueTail);
    _b.JumpIfNonZero(VReg.Scratch2, "__gt_enqueue_local_append");

    // Local queue empty: head = tail = gt
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueHead, VReg.Scratch0);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueTail, VReg.Scratch0);
    _b.Jump("__gt_enqueue_local_inc");

    _b.DefineLabel("__gt_enqueue_local_append");
    _b.StoreIndirect(VReg.Scratch2, GtOffNext, VReg.Scratch0);
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueTail, VReg.Scratch0);

    _b.DefineLabel("__gt_enqueue_local_inc");
    _b.LoadLocal(VReg.Scratch1, 1);
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffLocalQueueLen);
    _b.AddRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, POffLocalQueueLen, VReg.Scratch2);
    _b.Jump("__gt_enqueue_wake");

    // --- Global queue ---
    _b.DefineLabel("__gt_enqueue_global");
    _b.LockAcquire(_b.SchedLockLabel);

    _b.LoadLocal(VReg.Scratch0, 0);
    _b.LoadGlobal(VReg.Scratch1, "__gt_run_queue_tail");
    _b.JumpIfNonZero(VReg.Scratch1, "__gt_enqueue_global_append");

    _b.StoreGlobal("__gt_run_queue_head", VReg.Scratch0);
    _b.StoreGlobal("__gt_run_queue_tail", VReg.Scratch0);
    _b.Jump("__gt_enqueue_global_unlock");

    _b.DefineLabel("__gt_enqueue_global_append");
    _b.StoreIndirect(VReg.Scratch1, GtOffNext, VReg.Scratch0);
    _b.StoreGlobal("__gt_run_queue_tail", VReg.Scratch0);

    _b.DefineLabel("__gt_enqueue_global_unlock");
    _b.LockRelease(_b.SchedLockLabel);

    // --- Wake phase ---
    _b.DefineLabel("__gt_enqueue_wake");
    _b.LoadGlobal(VReg.Scratch0, "__sched_shutdown_flag");
    _b.JumpIfNonZero(VReg.Scratch0, "__gt_enqueue_wake_done");

    // Scan P[1..num_procs-1] for idle workers
    _b.LoadGlobal(VReg.Scratch0, "__sched_num_procs");
    _b.StoreLocal(2, VReg.Scratch0);
    _b.MovRegImm(VReg.Scratch1, 1);
    _b.StoreLocal(3, VReg.Scratch1);

    _b.DefineLabel("__gt_enqueue_wake_loop");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, "__gt_enqueue_wake_spawn");

    // Load P[i]
    _b.LoadGlobal(VReg.Scratch1, "__sched_procs");
    _b.LoadLocal(VReg.Scratch2, 3);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch1, 0);

    // Check idleFlag
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffIdleFlag);
    _b.JumpIfZero(VReg.Scratch2, "__gt_enqueue_wake_next");

    // Found idle worker: clear flag, wake it
    _b.ZeroReg(VReg.Scratch2);
    _b.StoreIndirect(VReg.Scratch1, POffIdleFlag, VReg.Scratch2);
    _b.WakeWorker(VReg.Scratch1);
    _b.Jump("__gt_enqueue_wake_done");

    _b.DefineLabel("__gt_enqueue_wake_next");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump("__gt_enqueue_wake_loop");

    // --- No idle worker: try to spawn ---
    _b.DefineLabel("__gt_enqueue_wake_spawn");
    _b.LoadGlobal(VReg.Scratch0, "__sched_active_workers");
    _b.LoadGlobal(VReg.Scratch1, "__sched_max_procs");
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, "__gt_enqueue_wake_done");

    _b.MovRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);

    _b.DefineLabel("__gt_enqueue_spawn_scan");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.LoadLocal(VReg.Scratch1, 2);
    _b.CmpRegReg(VReg.Scratch0, VReg.Scratch1);
    _b.JumpIf(Condition.AboveEqual, "__gt_enqueue_wake_done");

    // Load P[i]
    _b.LoadGlobal(VReg.Scratch1, "__sched_procs");
    _b.LoadLocal(VReg.Scratch2, 3);
    _b.ShlRegImm(VReg.Scratch2, 3);
    _b.AddRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch1, 0);

    // Check P[i]->status == 0
    _b.LoadIndirect(VReg.Scratch2, VReg.Scratch1, POffStatus);
    _b.JumpIfNonZero(VReg.Scratch2, "__gt_enqueue_spawn_next");

    // Set status = 1
    _b.MovRegImm(VReg.Scratch2, 1);
    _b.StoreIndirect(VReg.Scratch1, POffStatus, VReg.Scratch2);
    _b.StoreLocal(4, VReg.Scratch1);

    // Spawn worker thread
    _b.LoadLocal(VReg.Scratch1, 4);
    _b.SpawnWorker(VReg.Scratch1);
    _b.Jump("__gt_enqueue_wake_done");

    _b.DefineLabel("__gt_enqueue_spawn_next");
    _b.LoadLocal(VReg.Scratch0, 3);
    _b.AddRegImm(VReg.Scratch0, 1);
    _b.StoreLocal(3, VReg.Scratch0);
    _b.Jump("__gt_enqueue_spawn_scan");

    _b.DefineLabel("__gt_enqueue_wake_done");
    _b.FunctionEnd();
  }

  // =========================================================================
  // __gt_dequeue() -> GT* in Ret (or NULL).
  // =========================================================================
  //
  // GMP dequeue: runnext -> local (with 1/61 fairness) -> global -> steal.
  //
  // Stack slots:
  //   0 = P*
  //   1 = result GT* (used during global dequeue)
  //
  // Frame size: 0x40
  // =========================================================================

  public void EmitGtDequeue() {
    _b.FunctionStart("__gt_dequeue", 0, 0x40);

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
    _b.ReturnValue(VReg.Scratch0);

    // --- 2. Fairness check: xorshift64 on P->rng ---
    _b.DefineLabel("__gt_dequeue_check_fairness");
    _b.LoadLocal(VReg.Scratch1, 0);
    EmitXorshift64(VReg.Scratch0, VReg.Scratch1);

    // Check if (rng % 61) == 0
    _b.UDivRemainder(VReg.Scratch2, VReg.Scratch0, FairnessInterval);
    _b.JumpIfZero(VReg.Scratch2, "__gt_dequeue_global");

    // --- 3. Local queue ---
    _b.DefineLabel("__gt_dequeue_local");
    _b.LoadLocal(VReg.Scratch1, 0);
    _b.LoadIndirect(VReg.Scratch0, VReg.Scratch1, POffLocalQueueLen);
    _b.JumpIfZero(VReg.Scratch0, "__gt_dequeue_global");

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
    _b.ReturnValue(VReg.Scratch0);

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
    _b.CmpRegImm(VReg.Scratch1, 1);
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
    _b.ReturnValue(VReg.Scratch0);

    _b.DefineLabel("__gt_steal_unlock_skip");
    _b.LockRelease(_b.SchedLockLabel);
    _b.Jump("__gt_steal_loop");

    _b.DefineLabel("__gt_steal_fail");
    _b.ZeroReg(VReg.Scratch0);
    _b.ReturnValue(VReg.Scratch0);
  }

  // =========================================================================
  // __gt_timer_check(): Check and fire expired timers from the min-heap.
  // =========================================================================
  //
  // Stack slots:
  //   0 = now_ms
  //   1 = heap_base address
  //   2 = saved gt (to enqueue)
  //   3 = sift-down loop variable i
  //   4,5 = scratch for timespec (ARM64 only, used by GetCurrentTimeMs)
  //
  // Frame size: 0x50
  // =========================================================================

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

    // Get current time in ms
    _b.GetCurrentTimeMs(VReg.Scratch0, 4);    // result in Scratch0, uses slots 4-5 on ARM64
    _b.StoreLocal(0, VReg.Scratch0);          // save now_ms

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
    // Compare deadline vs now
    _b.LoadLocal(VReg.Scratch2, 0);           // now_ms
    _b.CmpRegReg(VReg.Scratch1, VReg.Scratch2);
    _b.JumpIf(Condition.Above, "__gt_timer_check_unlock"); // deadline > now

    // Save gt from heap[0]
    _b.LoadLocal(VReg.Scratch0, 1);
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, TimerOffGt);
    _b.StoreLocal(2, VReg.Scratch1);

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
    // Skip enqueue if mainThread (stackBase == 0)
    _b.LoadIndirect(VReg.Scratch1, VReg.Scratch0, GtOffStackBase);
    _b.JumpIfZero(VReg.Scratch1, "__gt_timer_check_skip_enqueue");
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
}
