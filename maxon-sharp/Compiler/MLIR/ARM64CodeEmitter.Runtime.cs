using MaxonSharp.Compiler.Ir.Dialects;
using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;
using static MaxonSharp.Compiler.Ir.Runtime.SubprocessStdin;

namespace MaxonSharp.Compiler.Ir;

public partial class ARM64CodeEmitter {

  // AAPCS64 argument registers
  private static readonly ARM64Register[] AbiArgRegs = [
    ARM64Register.X0, ARM64Register.X1, ARM64Register.X2, ARM64Register.X3,
    ARM64Register.X4, ARM64Register.X5, ARM64Register.X6, ARM64Register.X7
  ];

  private const int F_GETPATH = 50; // fcntl F_GETPATH on macOS

  // Timer heap layout: each entry is 16 bytes {i64 deadline_ms, ptr gt}
  private const int TimerEntrySize = 16;
  private const int TimerHeapCapacity = 256;

  // IoCompletion node: only next pointer used (results stored directly in GT struct)
  private const int IoCompOffNext = 0x20;

  // kqueue-based async IO context: {i64 fd, ptr buf, i64 len, ptr waiter_gt, i16 filter}
  private const int KqCtxSize = 0x28; // 40 bytes
  private const int KqCtxOffFd = 0x00;
  private const int KqCtxOffBuf = 0x08;
  private const int KqCtxOffLen = 0x10;
  private const int KqCtxOffWaiter = 0x18;
  private const int KqCtxOffFilter = 0x20;

  // macOS kqueue constants
  private const int EVFILT_READ = -1;
  private const int EVFILT_WRITE = -2;
  private const int EV_ADD = 0x0001;
  private const int EV_ONESHOT = 0x0010;

  // Internal KqCtx filter for async connect completion (not a real kqueue filter)
  private const int KQCTX_CONNECT = -3;

  // sysconf(_SC_NPROCESSORS_ONLN) — number of online logical CPUs on macOS.
  private const int ScNprocessorsOnln = 58;

  // __gt_init's local max_procs slot: [x29+32]. Class-scoped so the inline
  // EmitReadMaxProcsEnvOverride helper stays coupled to EmitSchedInit's frame.
  private const int GtInitMaxProcsSlotOffset = 32;

  // macOS fcntl / socket constants
  private const int F_SETFL = 4;
  private const int O_NONBLOCK = 0x0004;
  private const int SOL_SOCKET = 0xFFFF;
  private const int SO_ERROR = 0x1007;

  // --- Runtime function prologue/epilogue helpers ---

  private void EmitRuntimeFunctionStart(string name, int argCount, int stackSize = 0x30) {
    DefineLabel(name);
    _runtimeFunctionLabels.Add(name);
    _currentRuntimeStackSize = stackSize;
    // STP x29, x30, [sp, #-stackSize]!
    var imm7 = (uint)((-stackSize / 8) & 0x7F);
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    // MOV x29, sp
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);
    // Save arguments to stack
    for (int i = 0; i < argCount && i < 8; i++) {
      EmitLoadStoreUnsignedImm(0xF9000000, AbiArgRegs[i], ARM64Register.X29, 16 + i * 8, 8);
    }
  }

  private void EmitRuntimeFunctionEnd() {
    // MOV sp, x29
    EmitWord(0x91000000 | (29u << 5) | 31u);
    // LDP x29, x30, [sp], #stackSize
    var imm7 = (uint)((_currentRuntimeStackSize / 8) & 0x7F);
    EmitWord(0xA8C00000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    // RET
    EmitWord(0xD65F03C0);
  }

  /// <summary>
  /// maxon_force_segfault(): deliberately dereferences address 0 to trigger a CPU
  /// access-violation fault. Used by spec tests to exercise the SIGSEGV / EXC_BAD_ACCESS
  /// fault-handling path. Never returns — the load always faults, so the epilogue is
  /// unreachable.
  /// </summary>
  private void EmitMaxonForceSegfault() {
    EmitRuntimeFunctionStart("maxon_force_segfault", 0, 0x20);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_parallel_boundary(): a no-op CPU-parallel scheduling checkpoint.
  /// Backs `__Builtins.parallelBoundary()`. Emitted as a real (empty-bodied)
  /// runtime function — a bare prologue/epilogue — rather than expanding to zero
  /// IR, because a call to it must leave an op in the caller's body for the E3073
  /// async-yielding analysis to recognize as a legitimate yield point (see
  /// SemanticCheckPass.YieldingRuntimeEntries / maxon_parallel_boundary). Does nothing today; a
  /// future scheduler could hang a cooperative-yield check here.
  /// </summary>
  private void EmitMaxonParallelBoundary() {
    EmitRuntimeFunctionStart("maxon_parallel_boundary", 0, 0x20);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cpu_count() -> i64: logical processor count, clamped to >= 1.
  /// Backs `__Builtins.cpuCount()`. Queries sysconf(_SC_NPROCESSORS_ONLN)
  /// directly rather than reading __sched_max_procs, so it is valid to call
  /// BEFORE __gt_init runs and stays independent of the scheduler. macOS needs no
  /// system-stack switch for this call (see CallImportOnSystemStack). Returns the
  /// value in X0, matching maxon_current_process_id's zero-arg i64 return ABI.
  /// </summary>
  private void EmitMaxonCpuCount() {
    const string clampedLabel = "__maxon_cpu_count_clamped";

    EmitRuntimeFunctionStart("maxon_cpu_count", 0, 0x20);
    EmitMovRegImm(ARM64Register.X0, ScNprocessorsOnln);
    EmitCallImport("sysconf"); // result in X0 (or -1 on error)

    // Clamp to >= 1. sysconf returns -1 on error and could report 0 on a
    // pathological host; a signed compare handles both (they are < 1).
    EmitCmpImm(ARM64Register.X0, 1);
    EmitBranchCond(ARM64ConditionCode.Ge, clampedLabel);
    EmitMovRegImm(ARM64Register.X0, 1);
    DefineLabel(clampedLabel);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_sched_max_active_workers() -> i64: the high-water mark of concurrently
  /// active worker Ms (>= 1). Backs `__Builtins.schedMaxActiveWorkers()`. Reads
  /// the __sched_max_active_workers global maintained by __gt_init (init = 1) and
  /// __sched_worker_loop (raised on each worker entry). Returns in X0, matching
  /// maxon_cpu_count's zero-arg i64 return ABI.
  /// </summary>
  private void EmitMaxonSchedMaxActiveWorkers() {
    EmitRuntimeFunctionStart("maxon_sched_max_active_workers", 0, 0x20);
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_max_active_workers");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mrt_div_by_zero(): ARM64 SDIV/UDIV yield 0 on a zero divisor rather than faulting,
  /// unlike x86's hardware #DE. The backend emits an explicit divisor==0 check before each
  /// divide/remainder and branches here when it trips. Hands off to mrt_panic, which prints
  /// the message and the symbolized backtrace and exits 1 — so a divide by zero reports the
  /// same way here as it does on x86, where the CPU trap's fault diagnostic prints the same
  /// message and walks the same kind of frame chain. Never returns.
  ///
  /// A TRAMPOLINE, deliberately: no prologue, and B rather than BL. mrt_panic symbolizes its
  /// own return address as frame 0 and walks from its caller's saved FP, so leaving x29/x30
  /// untouched hands it the DIVIDE SITE's frame — the trace starts at the function that
  /// divided. Giving this function a frame of its own would insert `in mrt_div_by_zero` at
  /// the top of every such trace.
  /// </summary>
  private void EmitMaxonDivByZero() {
    // mrt_panic prints its message as a complete line; the shared __gt_panic_msg_div_zero
    // has no newline because x86's diagnostic appends ` at rip=…` fields to it.
    DefineSymdata("__gt_panic_msg_div_zero_line",
      System.Text.Encoding.UTF8.GetBytes(Runtime.RuntimeEmitter.DivZeroPanicText + "\n\0"));

    DefineLabel("mrt_div_by_zero");
    _runtimeFunctionLabels.Add("mrt_div_by_zero");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__gt_panic_msg_div_zero_line");
    EmitBranch("mrt_panic");
  }

  // Reload argument from stack
  private void EmitReloadArg(int argIndex) {
    EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[argIndex], ARM64Register.X29, 16 + argIndex * 8, 8);
  }

  // --- Apple ARM64 variadic call helpers ---
  // On Apple ARM64, variadic function arguments are passed on the stack, not in registers.
  // Functions like open(path, flags, ...) and fcntl(fd, cmd, ...) require this.

  /// Push one 8-byte variadic argument onto the stack (16-byte aligned).
  /// Call EmitVariadicCleanup after the function call to restore SP.
  private void EmitPushVariadicArg(ARM64Register reg) {
    // SUB SP, SP, #16
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 16, isAdd: false);
    // STR reg, [SP, #0]
    EmitLoadStoreUnsignedImm(0xF9000000, reg, ARM64Register.Sp, 0, 8);
  }

  /// Restore SP after a variadic function call.
  private void EmitVariadicCleanup(int bytes = 16) {
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, bytes, isAdd: true);
  }

  /// LSL Xd, Xn, #shift (logical shift left by an immediate 0..63). Alias of
  /// UBFM Xd, Xn, #(-shift mod 64), #(63-shift).
  private void EmitLslImm(ARM64Register dest, ARM64Register src, int shift) {
    uint immr = (uint)((64 - shift) & 63);
    uint imms = (uint)(63 - shift);
    EmitWord(0xD3400000u | (immr << 16) | (imms << 10) | ((uint)Reg(src) << 5) | (uint)Reg(dest));
  }

  /// LSR Xd, Xn, #shift (logical shift right by an immediate 0..63). Alias of
  /// UBFM Xd, Xn, #shift, #63.
  private void EmitLsrImm(ARM64Register dest, ARM64Register src, int shift) {
    EmitWord(0xD3400000u | ((uint)shift << 16) | (63u << 10) | ((uint)Reg(src) << 5) | (uint)Reg(dest));
  }

  /// Call mm_raw_alloc with X0 = size. Zeros X1 (scope) when mm-trace is enabled
  /// so that internal callers don't pass garbage as the scope argument.
  private void EmitCallMmRawAlloc() {
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_raw_alloc");
  }

  /// Emit mmap(NULL, X1, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0).
  /// The byte count must already be in X1; the base pointer is returned in X0.
  /// Process-lifetime runtime memory (the P*[] array, the P structs, and the
  /// per-P system stacks) uses this rather than mm_raw_alloc so it is OS-backed
  /// and never counted by the MM leak checker — mirroring the x86 backend, which
  /// VirtualAlloc's the very same structures (see X86 __gt_init).
  private void EmitMmapAnon() {
    EmitMovRegImm(ARM64Register.X0, 0);       // addr = NULL
    EmitMovRegImm(ARM64Register.X2, 3);       // PROT_READ|PROT_WRITE
    EmitMovRegImm(ARM64Register.X3, 0x1002);  // MAP_ANON|MAP_PRIVATE
    EmitMovRegImm(ARM64Register.X4, -1);      // fd = -1
    EmitMovRegImm(ARM64Register.X5, 0);       // offset = 0
    EmitCallImport("mmap");
  }

  // --- macOS wake lock block (Go semasleep/semawakeup, replaces dispatch_semaphore) ---
  // Each parked worker P (and the I/O sync worker) owns a {pthread_mutex_t, pthread_cond_t,
  // long count} block, mmap'd here and pointed to from the 8-byte wake slot. dispatch_semaphore
  // is Mach-port-backed and not async-signal-safe, which wedged processes in macOS 'UE' state
  // on kill; a pthread mutex+cond mirrors Go's os_darwin.go and parks/wakes cleanly.

  /// Allocate and initialise a wake lock block; the block pointer is returned in X0.
  /// mmap zero-fills the page (count starts 0 = parked), then pthread_mutex_init and
  /// pthread_cond_init run with NULL attrs. Carves its own 0x10 SP scratch to home the
  /// block pointer across the two init calls (which clobber X0..X18). The default
  /// initialisers match PTHREAD_MUTEX_INITIALIZER / PTHREAD_COND_INITIALIZER.
  private void EmitCreateWakeLockBlock() {
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x10, isAdd: false);

    EmitMovRegImm(ARM64Register.X1, WakeLockBlkSize);
    EmitMmapAnon();
    EmitStoreToSp(0x00, ARM64Register.X0); // home block ptr

    // pthread_mutex_init(&blk->mutex, NULL)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, WakeLockBlkOffMutex, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("pthread_mutex_init");

    // pthread_cond_init(&blk->cond, NULL)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.Sp, 0x00, 8); // reload blk
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, WakeLockBlkOffCond, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("pthread_cond_init");

    // Return block ptr in X0.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.Sp, 0x00, 8);
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x10, isAdd: true);
  }

  /// Go semasleep: park on the wake lock block at <paramref name="blkReg"/> until count>0.
  ///   pthread_mutex_lock(&blk->mutex);
  ///   while (count == 0) { timed ? cond_timedwait_relative_np(&cond,&mutex,&ts{0,100ms})
  ///                              : cond_wait(&cond,&mutex); }
  ///   count--; pthread_mutex_unlock(&blk->mutex);
  /// The while-loop re-checks count (cond can wake spuriously, unlike dispatch_semaphore);
  /// the counting `count` field preserves the signal-before-wait property. When timed, a
  /// 100ms timeout just loops back to re-check (the missed-wakeup safety net the old
  /// dispatch_time(NOW,100ms) park provided). Carves 0x20 SP scratch: [SP+0]=blk pointer
  /// homed across calls, [SP+0x10..+0x18]=relative timespec {0,100000000}.
  /// Clobbers X0..X18 and the scratch; the caller must reload P afterwards.
  private void EmitSemaSleep(ARM64Register blkReg, bool timed) {
    var prefix = $"__sema_sleep_{_code.Count}";
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: false);
    if (blkReg != ARM64Register.X0)
      EmitMovRegReg(ARM64Register.X0, blkReg);
    EmitStoreToSp(0x00, ARM64Register.X0); // home blk

    // pthread_mutex_lock(&blk->mutex)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, WakeLockBlkOffMutex, isAdd: true);
    EmitCallImport("pthread_mutex_lock");

    DefineLabel($"{prefix}_check");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8);   // blk
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffCount, 8);         // count
    EmitCbnz(ARM64Register.X0, $"{prefix}_have");

    if (timed) {
      // Build relative timespec {tv_sec=0, tv_nsec=100_000_000} on the frame.
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitStoreToSp(0x10, ARM64Register.X0);
      EmitMovRegImm(ARM64Register.X0, 100_000_000);
      EmitStoreToSp(0x18, ARM64Register.X0);
      // pthread_cond_timedwait_relative_np(&blk->cond, &blk->mutex, &ts)
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8); // blk
      EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffCond, isAdd: true);
      EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, WakeLockBlkOffMutex, isAdd: true);
      EmitAddSubImm(ARM64Register.X2, ARM64Register.Sp, 0x10, isAdd: true);
      EmitCallImport("pthread_cond_timedwait_relative_np");
      // On ETIMEDOUT (nonzero return) return to the caller WITHOUT a wake so it re-drives its
      // loop (re-check queues / drive inline I/O) and re-parks. This is the sysmon-style
      // periodic wake that recovers from any missed wakeup — without it a parked worker only
      // ever wakes on an explicit count++, so a lost signal strands pending work forever.
      EmitCbnz(ARM64Register.X0, $"{prefix}_unlock");
    } else {
      // pthread_cond_wait(&blk->cond, &blk->mutex)
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8); // blk
      EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffCond, isAdd: true);
      EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, WakeLockBlkOffMutex, isAdd: true);
      EmitCallImport("pthread_cond_wait");
    }
    EmitBranch($"{prefix}_check"); // re-check count (signal or spurious wakeup)

    DefineLabel($"{prefix}_have");
    // count-- (consume the wake). The timeout path skips this and just unlocks.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8);   // blk
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffCount, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitStoreIndirect(ARM64Register.X9, WakeLockBlkOffCount, ARM64Register.X0, 8);

    DefineLabel($"{prefix}_unlock");
    // pthread_mutex_unlock(&blk->mutex)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8);   // blk (reload — timeout path skipped _have)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffMutex, isAdd: true);
    EmitCallImport("pthread_mutex_unlock");

    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: true);
  }

  /// Go semawakeup: wake a parker on the wake lock block at <paramref name="blkReg"/>.
  ///   pthread_mutex_lock(&blk->mutex); count++; pthread_cond_signal(&blk->cond);
  ///   pthread_mutex_unlock(&blk->mutex);
  /// count++ before the signal preserves the dispatch_semaphore early-signal-not-lost
  /// property: a wake delivered before the parker reaches its while(count==0) test is
  /// seen (the parker returns immediately). Carves 0x10 SP scratch to home the block
  /// pointer across the calls. Clobbers X0..X18 and the scratch.
  private void EmitSemaWakeup(ARM64Register blkReg) {
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x10, isAdd: false);
    if (blkReg != ARM64Register.X0)
      EmitMovRegReg(ARM64Register.X0, blkReg);
    EmitStoreToSp(0x00, ARM64Register.X0); // home blk

    // pthread_mutex_lock(&blk->mutex)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, WakeLockBlkOffMutex, isAdd: true);
    EmitCallImport("pthread_mutex_lock");

    // count++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8);   // blk
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffCount, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitStoreIndirect(ARM64Register.X9, WakeLockBlkOffCount, ARM64Register.X0, 8);

    // pthread_cond_signal(&blk->cond)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffCond, isAdd: true);
    EmitCallImport("pthread_cond_signal");

    // pthread_mutex_unlock(&blk->mutex)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.Sp, 0x00, 8);   // blk
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, WakeLockBlkOffMutex, isAdd: true);
    EmitCallImport("pthread_mutex_unlock");

    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x10, isAdd: true);
  }

  // --- GMP scheduler TLS helpers ---

  /// Emit code to load P* (ProcContext) into the given register.
  /// X28 is the dedicated per-thread P* register.
  private void EmitLoadP(ARM64Register dest) {
    EmitMovRegReg(dest, ARM64Register.X28); // X28 = P* (dedicated register)
  }

  /// Emit code to load the current GreenThread pointer into dest.
  /// LDR dest, [X28, #POffCurrentGt]
  private void EmitLoadCurrentGt(ARM64Register dest) {
    EmitLoadStoreUnsignedImm(0xF9400000, dest, ARM64Register.X28, POffCurrentGt, 8);
  }

  /// <summary>
  /// Drive one turn of the scheduler and the I/O engine inline on the CALLER'S OWN STACK.
  /// Every cooperative idle-spin in this runtime — __gt_await, __gt_try_await, __gt_yield,
  /// __gt_cleanup, __io_submit_sync's main-thread arm, maxon_sleep's main-thread park, the
  /// kqueue park handshake, and maxon_yield (which reaches it from the shared emitter as
  /// IEmitterBackend.DriveSchedulerAndIo) — must drive exactly these four, in this order, or the
  /// GTs parked on whichever engine it omits never wake.
  ///
  /// ⚠ IT IS ONE SEQUENCE BECAUSE THE SEVEN SITES MUST AGREE AND NOTHING ELSE MAKES THEM.
  /// It was seven verbatim copies; the agreement was carried only by a comment on one of them
  /// ("Every other scheduler idle-spin (await, sleep, io_submit) polls kqueue here too"), which
  /// is the failure mode this project keeps paying for: adding a fifth engine to one copy and
  /// not the others is not a compile error, it is a subset of GTs that hang.
  ///
  /// The deliberate NON-caller is __sched_worker_park, which drives only __io_check_completions;
  /// its comment gives the reason (polling kqueue from an idle worker lets it double-schedule a
  /// spinning waiter). That divergence is a decision and stays spelled out at its site.
  ///
  /// __netpoll_recover is LAST and is time-gated to once per 10 ms inside itself (Go's sysmon
  /// interval): it is a safety net for a condition the park protocol makes unreachable, so it must
  /// cost nothing on the turns where there is nothing to find. This is the right host for it
  /// precisely because the wedge it guards against leaves this loop RUNNING — the captured stacks
  /// of the bug that opened this rung show the parent spinning here while a GT slept forever.
  /// </summary>
  private void EmitDriveSchedulerAndIo() {
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
    EmitBranchLink("__netpoll_recover");
  }

  /// <summary>
  /// Hand this M back to its scheduler: <c>__gt_context_switch(from = current GT, to =
  /// &amp;P.mainThread, p = P)</c>. Callers must have established that the current GT is not the
  /// inline mainThread itself (<c>stackBase != 0</c>) — switching to yourself changes nothing and
  /// returns immediately — and must have arranged their own wakeup, because this queues nobody.
  ///
  /// It deliberately does NOT stamp <c>mainThread.status</c>: a GT's own park loop owns that field,
  /// and the mainThread is never dequeued, so there is no arriving state to restore. See the x86
  /// twin's summary for the measured cost of getting that wrong.
  ///
  /// ⚠ THE FIVE OTHER SITES IN THIS FILE THAT SWITCH *TO* <c>&amp;P.mainThread</c> STILL SPELL IT
  /// INLINE (grep <c>POffMainThread</c> for the ones passing it as <c>to</c> in X1) and were
  /// deliberately left alone:
  /// converging them means editing park code no host in this loop can execute, and one of them sits
  /// inside <c>__gt_trampoline</c>, whose frame offsets DebugSamples pins. This exists because
  /// <c>maxon_yield</c> is emitted from the SHARED emitter and needs the operation under a portable
  /// name (<see cref="Runtime.IEmitterBackend.SwitchToMainThread"/>); it is the same four
  /// instructions those sites emit, read off <c>__gt_await_idle</c>'s worker-GT arm.
  /// </summary>
  private void EmitSwitchToMainThread() {
    EmitLoadCurrentGt(ARM64Register.X0);                                              // from = self
    EmitLoadP(ARM64Register.X9);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, POffMainThread, isAdd: true);   // to = &P.mainThread
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9);                                // p = P
    EmitBranchLink("__gt_context_switch");
  }

  // --- os_unfair_lock helpers ---

  /// Emit os_unfair_lock_lock(&lock_global). Clobbers X0.
  private void EmitLockAcquire(string lockGlobal) {
    EmitGlobalLeaReg(ARM64Register.X0, lockGlobal);
    EmitCallImport("os_unfair_lock_lock");
  }

  /// Emit os_unfair_lock_unlock(&lock_global). Clobbers X0.
  private void EmitLockRelease(string lockGlobal) {
    EmitGlobalLeaReg(ARM64Register.X0, lockGlobal);
    EmitCallImport("os_unfair_lock_unlock");
  }

  /// Acquire the trace output lock (only when AsyncTrace is enabled).
  /// Uses a simple LDAXR/STLXR spin lock instead of os_unfair_lock
  /// because os_unfair_lock deadlocks for unknown reasons on this binary's data section.
  private void EmitTraceAcquire() {
    if (!Compiler.AsyncTrace) return;
    var spinLabel = $"__trace_lock_spin_{_code.Count}";
    // X16 = &__sched_trace_lock
    EmitGlobalLeaReg(ARM64Register.X16, "__sched_trace_lock");
    DefineLabel(spinLabel);
    // LDAXR X17, [X16]
    EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17));
    // CBNZ X17, spin (already locked)
    EmitCbnz(ARM64Register.X17, spinLabel);
    // STLXR W17, X17(=1), [X16]  — try to set lock to 1
    EmitMovRegImm(ARM64Register.X17, 1);
    EmitWord(0xC800FC00 | (Reg(ARM64Register.X16) << 5) | (17u << 16) | Reg(ARM64Register.X17));
    // CBNZ W17, spin (CAS failed)
    _condBranchFixups.Add((_code.Count, spinLabel));
    EmitWord(0x35000000 | 17u); // CBNZ W17
  }

  /// Release the trace output lock (only when AsyncTrace is enabled).
  private void EmitTraceRelease() {
    if (!Compiler.AsyncTrace) return;
    // Store 0 to __sched_trace_lock with release semantics
    // STLR XZR, [X16] — X16 was set by EmitTraceAcquire, but may have been clobbered.
    // Reload the address:
    EmitGlobalLeaReg(ARM64Register.X16, "__sched_trace_lock");
    // STLR XZR, [X16]  (store-release of 0)
    EmitWord(0xC89FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.Xzr));
  }

  // --- Libc error checking ---

  /// Branch to errorLabel if libc call returned negative (X0 < 0).
  ///
  /// A 64-bit compare on the raw X0 is CORRECT for Darwin's int-returning libc entry points,
  /// MEASURED on macOS 15 / arm64 rather than inferred: the error return goes through _cerror,
  /// which leaves X0 = 0xFFFFFFFFFFFFFFFF, and the success return carries the kernel's own
  /// 64-bit value with clean high bits (kevent EV_ADD ok → 0x0, one ready event → 0x1, socket
  /// and open failures → 0xFFFFFFFFFFFFFFFF). This comment previously claimed the opposite —
  /// "Apple ARM64 zero-extends 32-bit return values, callers must sign-extend W0→X0" — which
  /// would make every `CMP X0, #0` error check in this file dead code, including
  /// EmitMarkWaitingAndArmKevent's. It does not; the checks work. Stated with the measurement
  /// because a reader who believes the old claim "fixes" a check that was never broken.
  private void EmitBranchOnLibcError(string errorLabel) {
    // CMP X0, #0 (SUBS XZR, X0, #0)
    EmitWord(0xF100001F);
    // B.LT errorLabel
    _condBranchFixups.Add((_code.Count, errorLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt));
  }

  /// <summary>
  /// On a failure branch of a libc call, capture the current errno value into
  /// the current green thread's io_error_code field (offset GtOffIoErrorCode).
  /// Uses Darwin's __error() libc function: it returns a pointer to the TLS
  /// errno variable, which we then dereference (W-sized load) and store.
  /// Clobbers X0, X9.
  /// </summary>
  private void EmitCaptureErrnoToGt() {
    // X0 = &errno (TLS pointer)
    EmitCallImport("__error");
    // W0 = *errno (32-bit errno value)
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X0, 0, 4);
    // X9 = current GT
    EmitLoadCurrentGt(ARM64Register.X9);
    // gt->io_error_code = X0 (zero-extended from W0)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
  }

  /// <summary>
  /// Sync-worker variant of EmitCaptureErrnoToGt: stashes the errno value into
  /// a frame-local scratch slot. The dispatcher's __io_op_done tail then reads
  /// this slot and writes it through to the waiter's gt->io_error_code (so the
  /// running thread, which is NOT the waiter, doesn't accidentally clobber its
  /// own errno-tracking field). slotFrameOffset is positive for SP-relative
  /// access (e.g. 200 → [x29+200] in __io_check_completions).
  /// Clobbers X0.
  /// </summary>
  private void EmitCaptureErrnoToFrameSlot(int slotFrameOffset) {
    EmitCallImport("__error");
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X0, 0, 4);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, slotFrameOffset, 8);
  }

  // --- Runtime functions ---

  public void EmitRuntimeFunctions() {
    EmitMaxonForceSegfault();
    EmitMaxonParallelBoundary();
    EmitMaxonCpuCount();
    EmitMaxonSchedMaxActiveWorkers();
    EmitMaxonDivByZero();
    EmitMaxonWriteStdout();
    EmitMaxonWriteStderr();
    EmitManagedWrite("maxon_managed_write_stdout", 1);
    EmitManagedWrite("maxon_managed_write_stderr", 2);
    EmitMaxonManagedReadStdin();
    EmitMaxonExit();
    EmitWriteCstrToStderr();
    EmitMaxonPanic();
    EmitMaxonFaultBacktrace();
    EmitMaxonPanicPrintFrame();
    EmitMaxonBoundsCheck();
    EmitMaxonIntegerToString("maxon_i64_to_string", signed: true);
    EmitMaxonIntegerToString("maxon_u64_to_string", signed: false);
    EmitMaxonMemcpy();
    EmitMaxonMemcmp();
    EmitMaxonStrlen();
    EmitMaxonToCstring();
    EmitMaxonCowCheck();
    // mm_raw_alloc/free/realloc unified via RuntimeEmitter
    var rawRt = new Runtime.RuntimeEmitter(CreateBackend());
    rawRt.EmitAllocatorFunctions(Compiler.MmTrace);
    rawRt.EmitMmRawAlloc(Compiler.MmTrace);
    rawRt.EmitMmRawRealloc(Compiler.MmTrace);
    rawRt.EmitMmRawFree(Compiler.MmTrace);
    rawRt.EmitStringEnsureCap(Compiler.MmTrace);
    rawRt.EmitCowStructDetach(Compiler.MmTrace);
    rawRt.EmitCurrentTimeMs();
    rawRt.EmitCurrentTimeNanos();
    rawRt.EmitCurrentUnixTimeSeconds();
    rawRt.EmitThreadCpuTicks();
    rawRt.EmitEnterBackgroundPriority();
    rawRt.EmitCurrentProcessId();
    // DebugStream functions are emitted from 4-ARM64CodeEmitter.cs
    EmitMaxonFileSize();
    EmitMaxonFileRead();
    EmitMaxonFileClose();
    EmitMaxonFileDelete();
    EmitMaxonFileRename();
    EmitMaxonCommandLineCount();
    EmitMaxonCommandLineArg();
    EmitMaxonOsEnvironmentEntryPosix();
    EmitMaxonExecutablePath();
    EmitMaxonDirectoryExists();
    EmitMaxonStdoutWantsAnsiColor();
    EmitMaxonCreateDirectory();
    EmitMaxonGetCurrentDirectory();
    if (Compiler.Coverage) EmitCoverageWriteFile();

    // Additional runtime functions
    EmitMaxonBoolToString();
    EmitNetTcpConnect();
    EmitManagedFileOpenRead();
    EmitManagedFileOpenWrite();
    EmitManagedFileOpenWriteExecutable();
    EmitManagedFileWrite();
    EmitManagedFileRead();
    EmitManagedFileClose();
    EmitFileDestructor();
    EmitMaxonManagedDirOpenSearch();
    EmitMaxonManagedDirClose();
    EmitDestructManagedDirectory();
    EmitMaxonFileExists();
    new Runtime.RuntimeEmitter(CreateBackend()).EmitMmRawAlloc260(Compiler.MmTrace);
    EmitMaxonSleep();

    // Green thread runtime for async/await
    EmitGreenThreadRuntime();

    // === Subprocess stubs (Phase 3.1) ===
    // No-op stubs so the new __Builtins.subprocess* intrinsics link. The real
    // posix_spawn / kqueue / pidfd implementations land in Phase 3.3 — see
    // lets-rewrite-our-process-maxon-humming-galaxy.md for the full contract.
    EmitMaxonSubprocessStubs();
    EmitNetSend();
    EmitNetRecv();
    EmitNetClose();
    EmitNetSocketDestructor();
    EmitMaxonFindFilename();
    EmitMaxonFindNextFile();

    // maxon_file_stat(cstr_path) -> ptr to 48-byte buffer or -1 on failure
    // Buffer layout: [size(8), modifiedTime(8), createdTime(8), accessedTime(8), isDir(8), isReadOnly(8)]
    // Uses POSIX stat() on macOS. struct stat is 144 bytes on macOS ARM64.
    EmitRuntimeFunctionStart("maxon_file_stat", 1, 0xC0);
    // Allocate 48-byte output buffer
    EmitMovRegImm(ARM64Register.X0, 48);
    EmitCallMmRawAlloc();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save buf ptr

    // stat(path, &statbuf) — statbuf at [x29+48] (144 bytes, fits in 0xC0 frame)
    EmitReloadArg(0); // X0 = path
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // X1 = &statbuf
    EmitCallImport("stat");
    // Check return: if X0 != 0, fail
    EmitCbnz(ARM64Register.X0, "rt_fstat_fail");

    // Load output buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 24, 8); // X9 = buf

    // buf[0] = st_size: at offset 96 in macOS ARM64 struct stat
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48 + 96, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 0, 8);

    // buf[8] = st_mtime (modifiedTime): at offset 48, seconds field
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48 + 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 8, 8);

    // buf[16] = st_birthtimespec (createdTime): at offset 80, seconds field
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48 + 80, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 16, 8);

    // buf[24] = st_atime (accessedTime): at offset 32, seconds field
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48 + 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 24, 8);

    // buf[32] = isDirectory: st_mode at offset 4 (u16), check S_IFDIR (0040000 = 0x4000)
    EmitLoadStoreUnsignedImm(0x79400000, ARM64Register.X0, ARM64Register.X29, 48 + 4, 2); // LDRH st_mode
    EmitMovRegImm(ARM64Register.X1, 0xF000); // file type mask
    // AND X0, X0, X1
    EmitAluRegReg(0x8A000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1);
    EmitMovRegImm(ARM64Register.X1, 0x4000); // S_IFDIR
    EmitWord(0xEB01001F); // CMP X0, X1
    EmitWord(0x9A9F17E0); // CSET X0, EQ
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 32, 8);

    // buf[40] = isReadOnly: check !(st_mode & S_IWUSR) where S_IWUSR = 0200 = 0x80
    EmitLoadStoreUnsignedImm(0x79400000, ARM64Register.X0, ARM64Register.X29, 48 + 4, 2); // LDRH st_mode
    EmitMovRegImm(ARM64Register.X1, 0x80); // S_IWUSR
    // TST X0, X1 (= ANDS XZR, X0, X1)
    EmitWord(0xEA01001F);
    EmitWord(0x9A9F17E0); // CSET X0, EQ (read-only if write bit NOT set)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 40, 8);

    // Return buffer ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranch("rt_fstat_done");

    DefineLabel("rt_fstat_fail");
    // Capture errno BEFORE mm_raw_free (the free path may itself touch errno).
    EmitCaptureErrnoToGt();
    // Free buffer on failure
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_raw_free");
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel("rt_fstat_done");
    EmitRuntimeFunctionEnd();

    // maxon_file_stat_field(buffer, index) -> i64 value at buffer[index * 8]
    EmitRuntimeFunctionStart("maxon_file_stat_field", 2, 0x20);
    EmitReloadArg(0); // X0 = buffer
    EmitReloadArg(1); // X1 = index
    // LDR X0, [X0, X1, LSL #3]
    EmitWord(0xF8617800);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_write_stdout(buf, len) ---
  // X0 = buffer ptr, X1 = length
  private void EmitMaxonWriteStdout() {
    EmitRuntimeFunctionStart("maxon_write_stdout", 2);
    // write() syscall expects (fd, buf, len) in X0-X2 but IR args arrive in X0-X1
    EmitReloadArg(0);
    var buf = ARM64Register.X0;
    EmitReloadArg(1);
    var len = ARM64Register.X1;
    EmitMovRegReg(ARM64Register.X2, len);
    EmitMovRegReg(ARM64Register.X1, buf);
    EmitMovRegImm(ARM64Register.X0, 1); // stdout fd
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_write_stderr(buf, len) ---
  private void EmitMaxonWriteStderr() {
    EmitRuntimeFunctionStart("maxon_write_stderr", 2);
    EmitReloadArg(0);
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, 2); // stderr fd
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// `__cov_write_file(x0=path, x1=buf, x2=len)` — the arm64 twin of the x64 writer (see
  /// X86CodeEmitter.Runtime.cs for why this has one body per backend rather than living in the
  /// platform-neutral layer). `open` is variadic in its mode argument on Apple arm64, which is the
  /// same reason `__io_op_file_open_write` pushes it rather than passing it in X2.
  ///
  /// Every failure returns quietly: instrumentation must not change what the measured program does,
  /// and an absent file is the driver's cue to report "no coverage data was written".
  /// </summary>
  private void EmitCoverageWriteFile() {
    // [x29+16] path, [x29+24] buf, [x29+32] len, [x29+40] fd, [x29+48] bytes written so far.
    EmitRuntimeFunctionStart(Runtime.RuntimeEmitter.CoverageWriteFileLabel, 3, 0x40);

    const int pathSlot = 16, bufSlot = 24, lenSlot = 32, fdSlot = 40, writtenSlot = 48;
    const long fileMode0644 = 0x1A4;
    var doneLabel = "rt_cov_write_done";
    var loopLabel = "rt_cov_write_loop";
    var closeLabel = "rt_cov_write_close";

    // open(path, O_WRONLY|O_CREAT|O_TRUNC, 0644)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, pathSlot, 8);
    EmitMovRegImm(ARM64Register.X1, O_WRONLY_CREAT_TRUNC);
    EmitMovRegImm(ARM64Register.X2, fileMode0644);
    EmitPushVariadicArg(ARM64Register.X2);
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, fdSlot, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, doneLabel);

    EmitMovRegImm(ARM64Register.X9, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X29, writtenSlot, 8);

    DefineLabel(loopLabel);
    // write(fd, buf + written, len - written); a short write loops, a failed or empty one stops.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, writtenSlot, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, lenSlot, 8);
    EmitAluRegReg(0xCB000000, ARM64Register.X2, ARM64Register.X10, ARM64Register.X9); // X2 = len - written
    EmitCmpImm(ARM64Register.X2, 0);
    EmitBranchCond(ARM64ConditionCode.Le, closeLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, bufSlot, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X1, ARM64Register.X10, ARM64Register.X9); // X1 = buf + written
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, fdSlot, 8);
    EmitCallImport("write");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, closeLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, writtenSlot, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X9, ARM64Register.X9, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X29, writtenSlot, 8);
    EmitBranch(loopLabel);

    DefineLabel(closeLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, fdSlot, 8);
    EmitCallImport("close");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_write_stdout/stderr(buf_ptr, length) ---
  // Thin wrappers that rearrange IR args (X0=buf, X1=len) into write() syscall order (X0=fd, X1=buf, X2=len).

  private void EmitManagedWrite(string name, int fd) {
    EmitRuntimeFunctionStart(name, 2);
    EmitReloadArg(0);
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, fd);
    EmitCallImport("write");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_read_stdin(buf_ptr, maxBytes) -> bytes_read ---
  // Calls read(fd=0, buf, maxBytes) which returns the number of bytes actually
  // read in X0 (matches the C# bootstrap's i64 contract for the runtime helper).
  private void EmitMaxonManagedReadStdin() {
    EmitRuntimeFunctionStart("maxon_managed_read_stdin", 2);
    EmitReloadArg(0);
    EmitReloadArg(1);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, 0); // stdin fd
    EmitCallImport("read");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_exit(code) ---
  private void EmitMaxonExit() {
    DefineLabel("maxon_exit");
    // X0 already has exit code
    EmitCallImport("_exit");
    EmitWord(0xD4200000); // BRK #0
  }

  // --- rt_write_cstr_stderr(cstr_ptr in X0) ---
  // Computes strlen of null-terminated string, writes to stderr fd 2.
  private void EmitWriteCstrToStderr() {
    EmitRuntimeFunctionStart("rt_write_cstr_stderr", 1, 0x20);
    // [x29+16] = cstr_ptr (arg 0)

    // Compute strlen: scan for null byte
    EmitReloadArg(0); // X0 = cstr_ptr
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = scan pointer
    DefineLabel("rt_write_cstr_stderr_strlen_loop");
    // LDRB W2, [X1], #1
    EmitWord(0x38401422);
    // CBNZ W2, loop
    _condBranchFixups.Add((_code.Count, "rt_write_cstr_stderr_strlen_loop"));
    EmitWord(0x35000002); // CBNZ W2, <fixup>
    // X1 now past null. len = X1 - cstr_ptr - 1
    EmitReloadArg(0); // X0 = cstr_ptr
    EmitAluRegReg(0xCB000000, ARM64Register.X2, ARM64Register.X1, ARM64Register.X0); // X2 = X1 - X0
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false); // exclude null

    // write(2, cstr_ptr, len)
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = buf
    // X2 = len (already set)
    EmitMovRegImm(ARM64Register.X0, 2); // fd = stderr
    EmitCallImport("write");

    EmitRuntimeFunctionEnd();
  }

  // --- mrt_panic(msg_ptr) ---
  // Prints panic message, walks stack, prints stack trace, exits with code 1.
  // Stack layout (all positive offsets within the allocated frame):
  //   [x29+0]  = saved X29
  //   [x29+8]  = saved X30
  //   [x29+16] = msg_ptr (arg 0)
  //   [x29+24] = text_base (addr of mrt_start)
  //   [x29+32] = symtab_ptr (addr of __symtab)
  //   [x29+40] = current_frame_fp
  //   [x29+48] = frame_counter
  //   [x29+56] = symtab_count
  //   [x29+64] = text_offset (current frame)
  //   [x29+72] = saved X19 (callee-saved)
  //   [x29+80] = symdata_base (addr of __symdata_base)
  //   [x29+88] = stack_high (exclusive upper bound of the panicking thread's stack)
  /// <summary>
  /// Emit the stack-trace preamble shared by mrt_panic and mrt_fault_backtrace: cache
  /// text_base (&amp;mrt_start) @ [x29+24], the symtab pointer @ [x29+32] and its entry count
  /// @ [x29+56], symdata_base @ [x29+80], then print "Stack trace:\n". Both callers use the
  /// identical [x29+*] contract mrt_panic_print_frame reads back through its caller's saved
  /// X29, so their frames must keep these four slots at these offsets.
  ///
  /// The FP-chain WALK that follows is deliberately NOT shared: mrt_fault_backtrace
  /// range-validates a possibly-corrupt chain (a low bound + strict ascent) because a fault is
  /// exactly the case where the chain may be the thing that broke, while mrt_panic trusts an
  /// otherwise well-formed one. What they DO share is the upper bound — __gt_stack_high — because
  /// running off the TOP of a green-thread stack is not a corrupt-chain question: the trampoline's
  /// frame pointer is legitimately the topmost word, so every walk meets it.
  /// </summary>
  private void EmitStackTraceHeader() {
    if (!_symdataLabels.ContainsKey("__panic_stacktrace"))
      DefineSymdata("__panic_stacktrace", System.Text.Encoding.UTF8.GetBytes("Stack trace:\n\0"));
    // Name resolution measures every offset from symdata's base. Registered here rather
    // than in one caller so this stays self-contained for the other.
    if (!_symdataLabels.ContainsKey("__symdata_base"))
      _symdataLabels["__symdata_base"] = 0;

    // text_base = address of mrt_start
    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, "mrt_start");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // symtab pointer, and count = [symtab_ptr] (first 8 bytes)
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__symtab");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 56, 8);

    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__symdata_base");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);

    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_stacktrace");
    EmitBranchLink("rt_write_cstr_stderr");
  }

  private void EmitMaxonPanic() {
    DefineRdata("__newline", [(byte)'\n']);

    EmitRuntimeFunctionStart("mrt_panic", 1, 0x60);

    // Save X19 (callee-saved) so we can use it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X19, ARM64Register.X29, 72, 8);

    // Step 1: Print the panic message (already ends with \n)
    EmitReloadArg(0); // X0 = msg_ptr
    EmitBranchLink("rt_write_cstr_stderr");

    // Steps 2-4: cache text_base/symtab/symdata_base, print "Stack trace:"
    EmitStackTraceHeader();

    // Step 5: Print first frame (the function that called panic)
    // [x29+8] = saved LR = return addr back to the function that called panic
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 8, 8); // X0 = return addr
    // A return address points to the instruction AFTER the call. When that call
    // is the function's last instruction (e.g. a noreturn panic call), the return
    // address lands at the start of the NEXT function, so symbolizing it directly
    // names the wrong function. Subtract 1 so the address falls inside the caller.
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false); // X0 = ret_addr - 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // X1 = text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1); // X0 = ret_addr - text_base
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // text_offset
    EmitBranchLink("mrt_panic_print_frame");

    // Step 6: Initialize stack walk
    // current_frame = [x29] (panic's caller's saved X29 — from our STP prologue)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // current_frame
    EmitMovRegImm(ARM64Register.X0, MaxBacktraceFrames);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // counter

    // stack_high: this walk runs on the panicking thread's own stack, and on a GREEN thread that
    // stack ENDS — the spawn trampoline's frame pointer is its topmost word, so reading that frame's
    // return address without a bound runs one word off the end and turns a panic into a fault.
    EmitMovRegReg(ARM64Register.X0, ARM64Register.Sp);
    EmitBranchLink("__gt_stack_high_current");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 88, 8); // stack_high

    // Stack walk loop
    DefineLabel("rt_panic_walk_loop");

    // Check counter
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000000); // CBZ X0, rt_panic_walk_done

    // Decrement counter
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);

    // Load current frame pointer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000000); // CBZ X0, rt_panic_walk_done

    // frame_fp + FrameLinkBytes > stack_high → done. Both words of the link are read (the return
    // address here, the previous frame pointer when advancing), so the bound has to leave room for
    // both; `frame < high` alone reads a word AT high, which is one past a green thread's stack.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 88, 8); // stack_high
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X0, FrameLinkBytes, isAdd: true);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X2) << 5)); // CMP
    EmitBranchCond(ARM64ConditionCode.Hi, "rt_panic_walk_done");

    // Get return address: [frame_fp + 8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 8, 8); // X1 = return addr
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000001); // CBZ X1, rt_panic_walk_done

    // Symbolize ret_addr - 1 so a call as a function's final instruction resolves
    // to the calling function rather than the next one (see Step 5 above).
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: false); // X1 = ret_addr - 1

    // Compute text_offset = return_addr - text_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 24, 8); // X2 = text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X2); // X1 = ret_addr - text_base

    // Check not negative (outside .text) — use CMP + B.LT for condBranchFixup compatibility
    EmitWord(0xF100001F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_advance"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT rt_panic_walk_advance

    // Save text_offset
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 64, 8);

    // Advance frame_fp BEFORE calling print_frame (which clobbers regs)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = current_frame
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8); // X0 = [current_frame] = prev frame
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // update current_frame

    // Print this frame
    EmitBranchLink("mrt_panic_print_frame");

    _branchFixups.Add((_code.Count, "rt_panic_walk_loop"));
    EmitWord(0x14000000); // B rt_panic_walk_loop

    DefineLabel("rt_panic_walk_advance");
    // Advance frame even on skip (negative offset)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    _branchFixups.Add((_code.Count, "rt_panic_walk_loop"));
    EmitWord(0x14000000); // B rt_panic_walk_loop

    DefineLabel("rt_panic_walk_done");

    // Restore X19
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X19, ARM64Register.X29, 72, 8);

    EmitCoverageAbortDump();

    // Exit with code 1
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitCallImport("_exit");
    EmitWord(0xD4200000); // BRK #0
  }

  /// Under `--coverage`, dump the counters as an ABORTED run just before a fatal exit — the arm64
  /// twin of the x64 hook, on the same panic path and for the same reason.
  private void EmitCoverageAbortDump() {
    if (!Compiler.Coverage) return;
    EmitMovRegImm(ARM64Register.X0, Debug.MxcovFormat.StatusAborted);
    EmitBranchLink(Runtime.RuntimeEmitter.CoverageDumpLabel);
  }

  /// <summary>
  /// mrt_fault_backtrace(): print "Stack trace:\n" + a symbolized backtrace for a CPU
  /// fault, then return (the diagnostic exits afterward). Frame 0 is the faulting
  /// instruction, symbolized from P-&gt;currentGt-&gt;fault_rip; the remaining frames come from
  /// walking the faulting thread's saved-FP chain (__gt_fault_last_rbp upward),
  /// range-validated against [__gt_fault_last_rsp, __gt_stack_high(...)) and required to
  /// strictly ascend so a corrupt FP degrades to a short trace instead of faulting a second
  /// time inside the handler. Reuses mrt_panic_print_frame via its caller-frame contract,
  /// so the slots below must match mrt_panic's. Mirrors the x86 mrt_fault_backtrace so both
  /// architectures print an identical trace. Takes no args.
  ///
  /// Our own FP is meaningless here (the handler redirected us with FP=0), which is why the
  /// walk starts from the stashed globals rather than x29.
  ///
  /// Stack layout (positive offsets from x29):
  ///   [+24] = text_base (addr of mrt_start)
  ///   [+32] = symtab_ptr                      (read by mrt_panic_print_frame)
  ///   [+40] = current frame fp
  ///   [+48] = frame counter (counts down)
  ///   [+56] = symtab count                    (read by mrt_panic_print_frame)
  ///   [+64] = text_offset                     (read by mrt_panic_print_frame)
  ///   [+80] = symdata_base                    (read by mrt_panic_print_frame)
  ///   [+88] = stack_low  (fault SP)
  ///   [+96] = stack_high (the end of the stack the fault SP is on)
  /// </summary>
  private void EmitMaxonFaultBacktrace() {
    EmitRuntimeFunctionStart("mrt_fault_backtrace", 0, 0x70);

    EmitStackTraceHeader();

    // ---- Frame 0: the faulting instruction (P->currentGt->fault_rip) ----
    // No ret_addr-1 bias here: fault_rip IS the faulting instruction, not a return
    // address, so it symbolizes to the correct function directly.
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffFaultRip, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 24, 8); // text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X2); // text_offset
    // textsize = symtab_ptr - text_base. One UNSIGNED compare rejects both a
    // negative offset (wraps huge) and one past the end of .text.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 32, 8);
    EmitAluRegReg(0xCB000000, ARM64Register.X3, ARM64Register.X3, ARM64Register.X2);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X1) << 5)); // CMP
    EmitBranchCond(ARM64ConditionCode.Hs, "rt_fbt_after_pc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 64, 8);
    EmitBranchLink("mrt_panic_print_frame");

    DefineLabel("rt_fbt_after_pc");

    // ---- Frames 1..N: walk the saved-FP chain ----
    // stack_high is the END of the stack the fault RSP is on. On a GREEN thread that is an exact
    // bound and it MATTERS: the spawn trampoline's frame pointer is the topmost word of the stack,
    // so a walk bounded by the fallback window reads its return address one word past the end and
    // faults a second time inside the fault handler.
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_fault_last_rsp");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 88, 8); // stack_low
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8);
    EmitBranchLink("__gt_stack_high_current");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8); // stack_high

    EmitGlobalLoadReg(ARM64Register.X0, "__gt_fault_last_rbp");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // current frame
    EmitMovRegImm(ARM64Register.X0, MaxBacktraceFrames);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // counter

    DefineLabel("rt_fbt_walk_loop");
    // counter == 0 → done, else counter--
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitCbz(ARM64Register.X0, "rt_fbt_walk_done");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);

    // frame == 0 → done
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitCbz(ARM64Register.X0, "rt_fbt_walk_done");

    // frame below stack_low, or its two-word link running past stack_high → done (corrupt, or one
    // past the end of a green thread's stack; either way do not deref it).
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 88, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP
    EmitBranchCond(ARM64ConditionCode.Lo, "rt_fbt_walk_done");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 96, 8);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X0, FrameLinkBytes, isAdd: true);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X2) << 5)); // CMP
    EmitBranchCond(ARM64ConditionCode.Hi, "rt_fbt_walk_done");

    // ret_addr = [frame + 8] (saved LR)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, 8, 8);
    EmitCbz(ARM64Register.X2, "rt_fbt_walk_done");

    // Symbolize ret_addr - 1 so a call as a function's final instruction resolves to the
    // calling function rather than the next one (matches mrt_panic's walk bias).
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 24, 8); // text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X2, ARM64Register.X2, ARM64Register.X3);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 32, 8); // symtab_ptr
    EmitAluRegReg(0xCB000000, ARM64Register.X4, ARM64Register.X4, ARM64Register.X3); // textsize
    EmitWord(0xEB00001F | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5)); // CMP
    EmitBranchCond(ARM64ConditionCode.Hs, "rt_fbt_walk_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 64, 8); // text_offset

    // Advance the frame BEFORE printing (print_frame clobbers scratch), with an ascending
    // guard: next = [frame]; if next <= frame, use 0 so the walk stops after this frame.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X0, 0, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP next, frame
    EmitBranchCond(ARM64ConditionCode.Hi, "rt_fbt_adv_ok");
    EmitMovRegImm(ARM64Register.X5, 0);
    DefineLabel("rt_fbt_adv_ok");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 40, 8);

    EmitBranchLink("mrt_panic_print_frame");
    EmitBranch("rt_fbt_walk_loop");

    DefineLabel("rt_fbt_walk_done");
    EmitRuntimeFunctionEnd();
  }

  // --- mrt_panic_print_frame ---
  // Looks up text_offset (from panic's frame) in the symbol table and prints "  in funcName\n".
  // Accesses panic's frame through saved X29 chain.
  // Stack layout:
  //   [x29+0]  = saved X29 (= panic's X29)
  //   [x29+8]  = saved X30 (return into panic)
  //   [x29+16] = symtab_ptr
  //   [x29+24] = count
  //   [x29+32] = text_offset
  //   [x29+40] = symdata_base
  private void EmitMaxonPanicPrintFrame() {
    DefineSymdata("__panic_in", System.Text.Encoding.UTF8.GetBytes("  in \0"));
    DefineSymdata("__panic_unknown", System.Text.Encoding.UTF8.GetBytes("<unknown>\0"));

    EmitRuntimeFunctionStart("mrt_panic_print_frame", 0, 0x30);

    // Load caller's (panic's) frame pointer to access its locals
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X19, ARM64Register.X29, 0, 8); // X19 = panic's x29

    // Print "  in "
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_in");
    EmitBranchLink("rt_write_cstr_stderr");

    // Load symtab_ptr, symtab_count, text_offset, symdata_base from panic's frame
    // panic's layout: [+32]=symtab_ptr, [+56]=count, [+64]=text_offset, [+80]=symdata_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X19, 32, 8); // X0 = symtab_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X19, 56, 8); // X1 = count
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X19, 64, 8); // X2 = text_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X19, 80, 8); // X3 = symdata_base

    // Save to our locals for after the lookup
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // [x29+16] = symtab_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // [x29+24] = count
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8); // [x29+32] = text_offset
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8); // [x29+40] = symdata_base

    // Linear scan symtab: entries start at symtab_ptr + 8 (skip count)
    // Each entry: (name_offset: i64, code_offset: i64) = 16 bytes
    // Find largest code_offset <= text_offset
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X0, 8, isAdd: true); // X3 = &entries[0]
    EmitMovRegImm(ARM64Register.X4, 0);  // X4 = loop index
    EmitMovRegImm(ARM64Register.X5, -1); // X5 = best_name_offset (-1 = none)
    EmitMovRegImm(ARM64Register.X8, -1); // X8 = best_code_offset (-1 = none, will be < any valid offset when unsigned)

    DefineLabel("rt_panic_lookup_loop");
    // if index >= count, done
    EmitWord(0xEB01009F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X1
    _condBranchFixups.Add((_code.Count, "rt_panic_lookup_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // X6 = &entries[index] = X3 + index * 16
    // LSL X7, X4, #4 (multiply by 16) = UBFM X7, X4, #60, #59
    EmitWord(0xD37CEC87 | (Reg(ARM64Register.X4) << 5)); // LSL X7, X4, #4
    EmitAluRegReg(0x8B000000, ARM64Register.X6, ARM64Register.X3, ARM64Register.X7); // X6 = X3 + X7

    // Load code_offset: [X6 + 8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X6, 8, 8); // X7 = code_offset

    // if text_offset < code_offset, skip this entry
    EmitWord(0xEB07005F | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X2) << 5)); // CMP X2, X7
    _condBranchFixups.Add((_code.Count, "rt_panic_lookup_next"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO next

    // code_offset <= text_offset: only update if code_offset > best_code_offset
    // CMP X7, X8 (new code_offset vs best_code_offset)
    EmitAluRegReg(0xEB000000, ARM64Register.Xzr, ARM64Register.X7, ARM64Register.X8); // CMP X7, X8 = SUBS XZR, X7, X8
    _condBranchFixups.Add((_code.Count, "rt_panic_lookup_next"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE next (signed: if X7 <= X8, skip)

    // New best match
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X6, 0, 8); // X5 = name_offset
    EmitMovRegReg(ARM64Register.X8, ARM64Register.X7); // X8 = best_code_offset

    DefineLabel("rt_panic_lookup_next");
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true); // index++
    _branchFixups.Add((_code.Count, "rt_panic_lookup_loop"));
    EmitWord(0x14000000); // B loop

    DefineLabel("rt_panic_lookup_done");

    // Check if we found a match (X5 != -1)
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, -1
    _condBranchFixups.Add((_code.Count, "rt_panic_print_unknown"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ unknown

    // Print function name: symdata_base + name_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = symdata_base
    EmitAluRegReg(0x8B000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X5); // X0 = symdata_base + name_offset
    EmitBranchLink("rt_write_cstr_stderr");
    _branchFixups.Add((_code.Count, "rt_panic_print_newline"));
    EmitWord(0x14000000); // B print_newline

    DefineLabel("rt_panic_print_unknown");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_unknown");
    EmitBranchLink("rt_write_cstr_stderr");

    DefineLabel("rt_panic_print_newline");
    // Print "\n" using write directly
    EmitAdrpAddFixup(ARM64Register.X1, _rdataAdrpFixups, "__newline");
    EmitMovRegImm(ARM64Register.X2, 1);
    EmitMovRegImm(ARM64Register.X0, 2);
    EmitCallImport("write");

    EmitRuntimeFunctionEnd();
  }

  // --- maxon_bounds_check(index, limit, msg_ptr) ---
  // Frameless helper: args in X0=index, X1=limit, X2=msg_ptr.
  // If in bounds (index < limit unsigned), returns immediately.
  // If out of bounds, tail-calls mrt_panic with msg_ptr in X0,
  // preserving the caller's frame pointer chain for clean stack traces.
  private void EmitMaxonBoundsCheck() {
    DefineLabel("maxon_bounds_check");
    // CMP X0 (index), X1 (limit)
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
    // B.LO ok (unsigned lower = in bounds)
    var okLabel = $"__bounds_ok_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, okLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO

    // Out of bounds — tail-call mrt_panic with msg_ptr in X0
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    // B mrt_panic (not BL — tail call, preserves LR and frame chain)
    _branchFixups.Add((_code.Count, "mrt_panic"));
    EmitWord(0x14000000); // B <imm26>

    DefineLabel(okLabel);
    // RET
    EmitWord(0xD65F03C0);
  }

  /// <summary>
  /// maxon_i64_to_string / maxon_u64_to_string (value, buf) -> len. Decimal, into a caller-owned
  /// buffer of at least 21 bytes, which is the whole i64/u64 range plus a sign — so this conversion
  /// is bounded BY CONSTRUCTION and no input can overrun it.
  ///
  /// ⚠ ONE C# METHOD EMITS BOTH, because the two differ ONLY in whether the sign is peeled off, and
  /// spelling them separately is what let them drift: `maxon_u64_to_string` used to be a bare
  /// `B maxon_i64_to_string`, so an `int(0 to u64.max)` with bit 63 set printed `18446744073709551615`
  /// on x64 and `-1` here — one program, two answers, decided by the target.
  ///
  /// The digit loop is UDIV either way. Negating i64.min leaves i64.min, and reading THAT as unsigned
  /// is exactly 2^63 = |i64.min|, so the signed path needs no special case for it.
  /// </summary>
  private void EmitMaxonIntegerToString(string name, bool signed) {
    EmitRuntimeFunctionStart(name, 2, 0x50);

    EmitReloadArg(0); // value
    EmitReloadArg(1); // buf

    // Handle negative: if value < 0, write '-', negate
    var positiveLabel = $"__i64_positive_{_uniqueLabelCounter}";
    var convertLabel = $"__i64_convert_{_uniqueLabelCounter}";
    var reverseLabel = $"__i64_reverse_{_uniqueLabelCounter}";
    var doneLabel = $"__i64_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // X0 = value, X1 = buf
    // Save buf pointer to [x29, #32]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8);
    // X3 = current write position = buf
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X1);
    // Save original buf as start position [x29, #40]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);

    // An unsigned reading has no sign to peel: bit 63 is a VALUE bit, not a minus.
    if (signed) {
      // Check if negative
      EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
      _condBranchFixups.Add((_code.Count, positiveLabel));
      EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive

      // Negative: write '-'
      EmitMovRegImm(ARM64Register.X4, (long)'-');
      EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB W4, [X3]
      EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
      // Save updated position
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 40, 8);
      // Negate value
      EmitWord(0xCB000000 | (Reg(ARM64Register.X0) << 16) | (31u << 5) | Reg(ARM64Register.X0)); // NEG X0, X0
    }

    DefineLabel(positiveLabel);
    // X0 = absolute value, X3 = write position
    // We'll write digits in reverse, then reverse them
    // Save digit start position [x29, #48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 48, 8);

    // Convert loop: divide by 10, store remainder as digit
    DefineLabel(convertLabel);
    EmitMovRegImm(ARM64Register.X1, 10);
    // UDIV X2, X0, X1 (quotient)
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    // MSUB X4, X2, X1, X0 (remainder = value - quotient * 10)
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X4));
    // digit = remainder + '0'
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, (long)'0', isAdd: true);
    // STRB W4, [X3]
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4));
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // X0 = quotient
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    // Continue if quotient != 0
    _condBranchFixups.Add((_code.Count, convertLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, convertLabel

    // Now reverse the digits from [digit_start..X3)
    // Save end position
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 56, 8); // end pos
    // X5 = start, X6 = end-1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, 1, isAdd: false);

    DefineLabel(reverseLabel);
    // if start >= end-1, done
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, X6
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    // Swap bytes
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X5]
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X8)); // LDRB W8, [X6]
    EmitWord(0x39000000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X8)); // STRB W8, [X5]
    EmitWord(0x39000000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X6]
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch(reverseLabel);

    DefineLabel(doneLabel);
    // Null-terminate the string
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 56, 8); // end pos
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB WZR, [X3] (null terminator)
    // Return length = end - buf_start
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // buf start
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // SUB X0, X3, X1
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_bool_to_string(value, buffer) -> length
  /// X0 = value (0=false, nonzero=true), X1 = buffer (>= 6 bytes)
  /// Returns length in X0 (4 for "true", 5 for "false")
  /// </summary>
  private void EmitMaxonBoolToString() {
    var falseLabel = $"__boolstr_false_{_uniqueLabelCounter}";
    var epilogueLabel = $"__boolstr_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_bool_to_string", 2, 0x30);
    EmitReloadArg(0); // value
    EmitReloadArg(1); // buf

    // CBZ X0, falseLabel
    _condBranchFixups.Add((_code.Count, falseLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0));

    // True path: write "true\0"
    EmitMovRegImm(ARM64Register.X2, (long)'t');
    EmitWord(0x39000000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1]
    EmitMovRegImm(ARM64Register.X2, (long)'r');
    EmitWord(0x39000400 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #1]
    EmitMovRegImm(ARM64Register.X2, (long)'u');
    EmitWord(0x39000800 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #2]
    EmitMovRegImm(ARM64Register.X2, (long)'e');
    EmitWord(0x39000C00 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #3]
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39001000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2)); // STRB W2, [X1, #4]
    EmitMovRegImm(ARM64Register.X0, 4);
    EmitBranch(epilogueLabel);

    // False path: write "false\0"
    DefineLabel(falseLabel);
    EmitMovRegImm(ARM64Register.X2, (long)'f');
    EmitWord(0x39000000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'a');
    EmitWord(0x39000400 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'l');
    EmitWord(0x39000800 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'s');
    EmitWord(0x39000C00 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, (long)'e');
    EmitWord(0x39001000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39001400 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    EmitMovRegImm(ARM64Register.X0, 5);

    DefineLabel(epilogueLabel);
    EmitRuntimeFunctionEnd();
  }


  // --- maxon_memcpy(dst, src, len) ---
  private void EmitMaxonMemcpy() {
    EmitRuntimeFunctionStart("maxon_memcpy", 3);
    EmitReloadArg(0); // dst
    EmitReloadArg(1); // src
    EmitReloadArg(2); // len

    var loopLabel = $"__memcpy_rt_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__memcpy_rt_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // X0=dst, X1=src, X2=len
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, done

    DefineLabel(loopLabel);
    // LDRB W3, [X1], #1
    EmitWord(0x38401423);
    // STRB W3, [X0], #1
    EmitWord(0x38001403);
    // SUB X2, X2, #1
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    _condBranchFixups.Add((_code.Count, loopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2, loop

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_memcmp(a, b, len) -> i64 ---
  private void EmitMaxonMemcmp() {
    EmitRuntimeFunctionStart("maxon_memcmp", 3);
    EmitReloadArg(0); // a
    EmitReloadArg(1); // b
    EmitReloadArg(2); // len

    var loopLabel = $"__memcmp_loop_{_uniqueLabelCounter}";
    var doneEqLabel = $"__memcmp_eq_{_uniqueLabelCounter}";
    var doneLtLabel = $"__memcmp_lt_{_uniqueLabelCounter}";
    var doneLabel = $"__memcmp_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    _condBranchFixups.Add((_code.Count, doneEqLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, equal

    DefineLabel(loopLabel);
    EmitWord(0x39400000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // LDRB W3, [X0]
    EmitWord(0x39400000 | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X4)); // LDRB W4, [X1]
    EmitWord(0xEB00001F | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X3) << 5)); // CMP X3, X4
    _condBranchFixups.Add((_code.Count, doneLtLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO lt
    _condBranchFixups.Add((_code.Count, $"__memcmp_gt_{_uniqueLabelCounter}"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hi)); // B.HI gt

    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    _condBranchFixups.Add((_code.Count, loopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2, loop

    DefineLabel(doneEqLabel);
    EmitMovRegImm(ARM64Register.X0, 1); // 1 = equal (matches x86 SETE convention)
    EmitBranch(doneLabel);

    DefineLabel(doneLtLabel);
    EmitMovRegImm(ARM64Register.X0, 0); // 0 = not equal
    EmitBranch(doneLabel);

    DefineLabel($"__memcmp_gt_{_uniqueLabelCounter}");
    EmitMovRegImm(ARM64Register.X0, 0); // 0 = not equal

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_strlen(str) -> len ---
  private void EmitMaxonStrlen() {
    EmitRuntimeFunctionStart("maxon_strlen", 1);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // save start

    var loopLabel = $"__strlen_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__strlen_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(loopLabel);
    EmitWord(0x39400000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // LDRB W2, [X0]
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x34000000 | Reg(ARM64Register.X2)); // CBZ W2, done
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitBranch(loopLabel);

    DefineLabel(doneLabel);
    // len = X0 - X1
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_to_cstring(buf, len) -> ptr ---
  // Returns a null-terminated C string. If buffer[length] is already 0,
  // returns the original buffer (no allocation). Otherwise allocates a copy.
  private void EmitMaxonToCstring() {
    EmitRuntimeFunctionStart("maxon_to_cstring", 2, 0x40);
    EmitReloadArg(0); // X0 = buf
    EmitReloadArg(1); // X1 = len
    // Check if already null-terminated: LDRB W2, [X0, X1]
    EmitWord(0x38616802); // LDRB W2, [X0, X1]
    EmitCbz(ARM64Register.X2, "rt_tocstr_already_terminated");

    // Not terminated — allocate len+1 bytes via mm_alloc
    EmitReloadArg(1); // X1 = len
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0); // no destructor
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X3, _symdataAdrpFixups, "__mm_scope_cow_copy");
    else EmitMovRegImm(ARM64Register.X3, 0); // no scope
    EmitBranchLink("mm_alloc");
    // Save allocated ptr [x29, #32]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // Copy: memcpy(allocated, buf, len)
    EmitReloadArg(0); // buf
    EmitReloadArg(1); // len
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitBranchLink("maxon_memcpy");
    // Null terminate: buf[len] = 0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitReloadArg(1); // len
    // ADD X2, X0, X1 (ptr + len)
    EmitAluRegReg(0x8B000000, ARM64Register.X2, ARM64Register.X0, ARM64Register.X1);
    // STRB WZR, [X2]
    EmitWord(0x39000000 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.Xzr));
    // Return allocated ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitBranch("rt_tocstr_epilogue");

    // Already terminated: return original buffer
    DefineLabel("rt_tocstr_already_terminated");
    EmitReloadArg(0); // X0 = original buf

    DefineLabel("rt_tocstr_epilogue");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_cow_check(buffer, capacity, byteLen, managedPtr) -> new_buffer ---
  // If capacity >= 0, buffer is already writable — return it as-is.
  // If capacity < 0, allocate byteLen bytes via mm_raw_alloc, copy from old buffer, return new buffer.
  // The old rdata/slice buffer is NOT freed (capacity < 0 identifies non-owned buffers).
  private void EmitMaxonCowCheck() {
    // Args: X0=buffer, X1=capacity, X2=byteLen, X3=managedPtr
    // [x29+16]=buffer, [x29+24]=capacity, [x29+32]=byteLen, [x29+40]=managedPtr
    // [x29+48]=new_buffer (scratch), [x29+56]=scope=NULL (trace only)
    EmitRuntimeFunctionStart("maxon_cow_check", 4, Compiler.MmTrace ? 0x50 : 0x40);

    // Check capacity >= 0 (signed) → already writable
    // capacity == -2 (rdata) or capacity == -1 (slice) falls through to COW path
    EmitReloadArg(1); // X1 = capacity
    // CMP X1, #0
    EmitWord(0xF100003F); // CMP X1, #0 (SUBS XZR, X1, #0)
    // B.GE rt_cow_writable
    _condBranchFixups.Add((_code.Count, "rt_cow_writable"));
    EmitWord(0x5400000A); // B.GE <fixup>

    // NOTE: do NOT skip COW when byteLen == 0. An empty rdata/slice buffer
    // (capacity < 0) must still be detached from the static/borrowed buffer so
    // the caller (`__managed_mem_grow`) reallocs an OWNED heap pointer. Skipping
    // here left the rdata pointer in place with capacity still -2, and the
    // subsequent `mm_realloc` of that static pointer read a nonexistent header
    // and faulted (e.g. pushing to `b""` / `"".toByteArray()`). With byteLen==0
    // the COW path allocates nothing (mm_raw_alloc(0) -> NULL) and copies
    // nothing, but cow returns a non-rdata buffer so capacity is reset to the
    // length and grow's realloc starts from a clean owned/NULL pointer.
    // Mirrors the x86 emitter's maxon_cow_check.
    // COW path: allocate byteLen bytes, copy old buffer
    EmitReloadArg(2); // X2 = byteLen
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2); // X0 = byteLen (arg for mm_raw_alloc)
    if (Compiler.MmTrace) EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__mm_scope_cow_copy");
    EmitBranchLink("mm_raw_alloc");
    // Save new buffer at [x29+48]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);

    // memcpy(new_buffer, old_buffer, byteLen): X0=dst, X1=src, X2=count
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8); // X0 = new_buffer (dst)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // X1 = old_buffer (src)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = byteLen (count)
    EmitBranchLink("maxon_memcpy");

    // Trace COW copy
    if (Compiler.MmTrace) {
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // scope=NULL
      // ptrSlot=40 (managedPtr), scopeSlot=56, sizeSlot=32 (byteLen)
      EmitInlineTrace("__mm_tag_cow", "cow_check_trace", ptrSlot: 40, scopeSlot: 56, sizeSlot: 32);
    }

    // Return new buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    _branchFixups.Add((_code.Count, "rt_cow_epilogue"));
    EmitWord(0x14000000); // B rt_cow_epilogue

    // already_writable: return old buffer
    DefineLabel("rt_cow_writable");
    EmitReloadArg(0); // X0 = buffer
    DefineLabel("rt_cow_epilogue");
    EmitRuntimeFunctionEnd();
  }

  // --- Raw memory functions (using mmap/munmap) ---

  // mm_raw_alloc: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  // mm_raw_realloc: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)
  // mm_raw_free: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  // --- File operations ---

  private void EmitMaxonFileSize() {
    // maxon_file_size(handle) -> i64
    // Use fstat (handle is fd, not path)
    EmitRuntimeFunctionStart("maxon_file_size", 1, 0xC0); // need room for stat struct on stack
    EmitReloadArg(0); // X0 = fd
    // fstat64(fd, &statbuf)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 0x40, isAdd: true); // stat buf at x29+64
    EmitCallImport("fstat");
    // Check for error
    var failLabel = $"__fsize_fail_{_uniqueLabelCounter}";
    var okLabel = $"__fsize_ok_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitBranchOnLibcError(failLabel);
    _branchFixups.Add((_code.Count, okLabel));
    EmitWord(0x14000000); // B ok
    DefineLabel(failLabel);
    // Capture errno into gt->io_error_code so the lowering can map it to a
    // specific __ManagedFileError variant (notFound / accessDenied / etc).
    EmitCaptureErrnoToGt();
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
    DefineLabel(okLabel);
    // st_size is at offset 96 in macOS stat64 struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 0x40 + 96, 8);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonFileRead() {
    // maxon_file_read(handle, buffer, size, capacity) -> bytes_read
    // Clamps size to capacity, then calls read(fd, buf, clampedSize)
    EmitRuntimeFunctionStart("maxon_file_read", 4, 0x40);

    // Clamp size to capacity: if size > capacity, use capacity
    EmitReloadArg(2); // X2 = size
    EmitReloadArg(3); // X3 = capacity
    // CMP X2, X3
    EmitWord(0xEB03005F | (Reg(ARM64Register.X2) << 5)); // CMP X2, X3 (SUBS XZR, X2, X3)
    var clampOk = $"__fread_clamp_ok_{_uniqueLabelCounter++}";
    _condBranchFixups.Add((_code.Count, clampOk));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ls)); // B.LS (unsigned <=)
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X3); // size = capacity
    DefineLabel(clampOk);

    // read(fd, buf, clampedSize): X0=fd, X1=buf, X2=size
    // X2 already has clamped size, save it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8); // save clamped size
    EmitReloadArg(1); // X1 = buffer
    EmitReloadArg(0); // X0 = handle (fd)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = clamped size
    EmitCallImport("read");

    var errorLabel = $"__fread_err_{_uniqueLabelCounter}";
    var doneLabel = $"__fread_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitBranchOnLibcError(errorLabel);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000); // B done

    DefineLabel(errorLabel);
    // Capture errno into gt->io_error_code for the lowering's per-variant dispatch.
    EmitCaptureErrnoToGt();
    EmitMovRegImm(ARM64Register.X0, 0); // return 0 on error (match X86 behavior)

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_file_close(__ManagedFile*): loads fd from [ptr+0], zeros the field,
  // then closes. Single point that clears _handle so the destructor's idempotency
  // check sees a zeroed field after an explicit close — no double-close.
  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1);
    EmitReloadArg(0); // X0 = __ManagedFile*
    var doneLabel = $"__fclose_noop_{_uniqueLabelCounter++}";
    // Null-ptr guard
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done
    // Load fd from [X0+0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    // Skip if fd <= 0 (treat 0 and -1 as already-closed)
    EmitWord(0xF100003F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE done
    // Zero _handle before close
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8);
    // close(fd)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitCallImport("close");
    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonFileDelete() {
    EmitRuntimeFunctionStart("maxon_file_delete", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileDelete);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // maxon_file_rename(cstring_old, cstring_new) -> 0 on success, -1 on failure.
  // Delegates to __io_submit_sync(SyncOpFileRename, old, new); the sync worker
  // runs rename(2), which atomically replaces the destination. arg0/arg1 carry
  // the two paths, so no third argument slot is needed.
  private void EmitMaxonFileRename() {
    EmitRuntimeFunctionStart("maxon_file_rename", 2, 0x30);
    // EmitReloadArg(i) loads AbiArgRegs[i], so EmitReloadArg(1) lands the new
    // path in X1 (NOT X0). Reload both args first, then shuffle: __io_submit_sync
    // wants arg0 (old) in X1 and arg1 (new) in X2. The earlier code reloaded
    // arg1 into X1 and then copied the stale X0 (still the old path) into X2,
    // submitting rename(new, old) — the not-yet-existent new path as the source,
    // so rename(2) returned ENOENT and every atomic cache write failed.
    EmitReloadArg(0);                                  // X0 = old path
    EmitReloadArg(1);                                  // X1 = new path
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1); // arg1 = new path
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // arg0 = old path
    EmitMovRegImm(ARM64Register.X0, SyncOpFileRename);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- Managed File I/O ---

  // __ManagedFile layout: [+0] = fd (i64), total 8 bytes
  // O_WRONLY=1, O_CREAT=0x200, O_TRUNC=0x400
  private const long O_WRONLY_CREAT_TRUNC = 0x601; // O_WRONLY | O_CREAT | O_TRUNC

  // maxon_managed_file_open_read(cstring_path) -> managed file ptr or -1
  // Delegates open() to __io_submit_sync(SyncOpFileOpenRead, path, 0), then allocs ManagedFile.
  private void EmitManagedFileOpenRead() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_read", 1, 0x30);
    EmitReloadArg(0); // X0 = path
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileOpenRead);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    // X0 = fd or -1
    var failLabel = $"__fopen_read_fail_{_uniqueLabelCounter}";
    var doneLabel = $"__fopen_read_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Check for failure (fd == -1 means unsigned max)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, 1, isAdd: true);
    EmitCbz(ARM64Register.X1, failLabel); // if fd+1 == 0, fd was -1

    // Save fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Allocate __ManagedFile struct (8 bytes)
    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedFile");
    EmitMovRegImm(ARM64Register.X2, 0);
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X3, 0); // null trace scope (mm_alloc is arity-4 in trace mode; leaving X3 garbage faults the scope print)
    EmitBranchLink("mm_alloc");

    // Store fd at [file_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    EmitBranch(doneLabel);

    DefineLabel(failLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_open_write(cstring_path) -> managed file ptr or -1
  // Delegates open() to __io_submit_sync(SyncOpFileOpenWrite, path, 0), then allocs ManagedFile.
  private void EmitManagedFileOpenWrite() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_write", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileOpenWrite);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    // X0 = fd or -1
    var failLabel = $"__fopen_write_fail_{_uniqueLabelCounter}";
    var doneLabel = $"__fopen_write_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, 1, isAdd: true);
    EmitCbz(ARM64Register.X1, failLabel);

    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save fd

    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedFile");
    EmitMovRegImm(ARM64Register.X2, 0);
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X3, 0); // null trace scope (mm_alloc is arity-4 in trace mode; leaving X3 garbage faults the scope print)
    EmitBranchLink("mm_alloc");

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    EmitBranch(doneLabel);

    DefineLabel(failLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_open_write_executable(cstring_path) -> managed file ptr or -1
  // Same as open_write but creates file with mode 0755 (executable) instead of 0666.
  private void EmitManagedFileOpenWriteExecutable() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_write_executable", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpFileOpenWriteExec);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    // X0 = fd or -1
    var failLabel = $"__fopen_write_exec_fail_{_uniqueLabelCounter}";
    var doneLabel = $"__fopen_write_exec_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, 1, isAdd: true);
    EmitCbz(ARM64Register.X1, failLabel);

    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save fd

    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedFile");
    EmitMovRegImm(ARM64Register.X2, 0);
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X3, 0); // null trace scope (mm_alloc is arity-4 in trace mode; leaving X3 garbage faults the scope print)
    EmitBranchLink("mm_alloc");

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    EmitBranch(doneLabel);

    DefineLabel(failLabel);
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_write(handle, buffer, length) -> bytes written or -1
  private void EmitManagedFileWrite() {
    EmitRuntimeFunctionStart("maxon_managed_file_write", 3, 0x30);
    EmitReloadArg(0); // X0 = fd (raw handle)
    EmitReloadArg(1); // X1 = buffer
    EmitReloadArg(2); // X2 = length

    EmitCallImport("write");

    var errorLabel = $"__fwrite_err_{_uniqueLabelCounter}";
    var doneLabel = $"__fwrite_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitBranchOnLibcError(errorLabel);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(errorLabel);
    // Capture errno into gt->io_error_code for per-variant dispatch.
    EmitCaptureErrnoToGt();
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_read(handle, buffer, length) -> bytes read or -1
  private void EmitManagedFileRead() {
    EmitRuntimeFunctionStart("maxon_managed_file_read", 3, 0x30);
    EmitReloadArg(0); // X0 = fd (raw handle)
    EmitReloadArg(1); // X1 = buffer
    EmitReloadArg(2); // X2 = length

    EmitCallImport("read");

    var errorLabel = $"__fread_err_{_uniqueLabelCounter}";
    var doneLabel = $"__fread_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitBranchOnLibcError(errorLabel);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(errorLabel);
    // Capture errno into gt->io_error_code for per-variant dispatch.
    EmitCaptureErrnoToGt();
    EmitMovRegImm(ARM64Register.X0, -1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_managed_file_close(handle_ptr)
  // Delegates close() to __io_submit_sync(SyncOpCloseHandle, fd, 0).
  private void EmitManagedFileClose() {
    EmitRuntimeFunctionStart("maxon_managed_file_close", 1, 0x30);
    EmitReloadArg(0); // X0 = fd (raw handle)

    var doneLabel = $"__fclose_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Skip if fd <= 0
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le));

    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = fd (arg0)
    EmitMovRegImm(ARM64Register.X0, SyncOpCloseHandle); // X0 = op
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // __destruct___ManagedFile(user_ptr): delegates to maxon_file_close, which
  // handles the load/zero/close sequence. If an explicit close() already ran,
  // _handle is zero and this is a no-op.
  private void EmitFileDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedFile", 1, 0x20);
    EmitReloadArg(0); // X0 = user_ptr
    EmitBranchLink("maxon_file_close");
    EmitRuntimeFunctionEnd();
  }

  // --- Command line functions ---

  // maxon_command_line_count() -> argc (including argv[0])
  private void EmitMaxonCommandLineCount() {
    DefineLabel("maxon_command_line_count");
    // Frameless leaf: load argc from global and return
    EmitAdrpAddGlobalFixup(ARM64Register.X0, "__argc_global", 0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8); // LDR X0, [X0]
    EmitWord(0xD65F03C0); // RET
  }

  // maxon_command_line_arg(index) -> heap-allocated C string copy of argv[index]
  // Stack: arg0 at [x29+16], [x29+24]=argv_str, [x29+32]=alloc_size
  private void EmitMaxonCommandLineArg() {
    EmitRuntimeFunctionStart("maxon_command_line_arg", 1, 0x50);
    EmitReloadArg(0); // X0 = index

    var emptyLabel = $"__cla_empty_{_uniqueLabelCounter}";
    var doneLabel = $"__cla_done_{_uniqueLabelCounter}";
    var lenLoopLabel = $"__cla_strlen_{_uniqueLabelCounter}";
    var lenDoneLabel = $"__cla_len_done_{_uniqueLabelCounter}";
    var copyLoopLabel = $"__cla_copy_{_uniqueLabelCounter}";
    var copyDoneLabel = $"__cla_copy_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Load argc, bounds check
    EmitAdrpAddGlobalFixup(ARM64Register.X1, "__argc_global", 0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, 0, 8); // X1 = argc
    // CMP X0, X1 — if index >= argc, return empty
    EmitWord(0xEB01001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, X1
    _condBranchFixups.Add((_code.Count, emptyLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE empty

    // Load argv[index]: argv base + index*8
    EmitAdrpAddGlobalFixup(ARM64Register.X1, "__argv_global", 0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, 0, 8); // X1 = argv
    // LSL X0, X0, #3 = UBFM X0, X0, #61, #60
    EmitWord(0xD37DF000 | Reg(ARM64Register.X0) | (Reg(ARM64Register.X0) << 5));
    // LDR X2, [X1, X0] — register offset
    EmitWord(0xF8606820 | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X2));
    // X2 = argv[index] C string
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 24, 8); // save argv_str at [x29+24]

    // strlen: scan until null
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X2); // X3 = scan ptr
    DefineLabel(lenLoopLabel);
    EmitWord(0x38401464 | (Reg(ARM64Register.X3) << 5)); // LDRB W4, [X3], #1
    _condBranchFixups.Add((_code.Count, lenDoneLabel));
    EmitWord(0x34000000 | Reg(ARM64Register.W4)); // CBZ W4, len_done
    _branchFixups.Add((_code.Count, lenLoopLabel));
    EmitWord(0x14000000); // B strlen_loop
    DefineLabel(lenDoneLabel);

    // X3 = one past null, length = X3 - X2 (includes null)
    EmitWord(0xCB020060 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // SUB X0, X3, X2
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save alloc_size at [x29+32]

    // Allocate buffer via mm_raw_alloc (no header/canary — freed via mm_raw_free)
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace); // X0 = raw buffer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save dest ptr at [x29+40]

    // Copy argv_str to new buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // X1 = src
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // X2 = length

    DefineLabel(copyLoopLabel);
    _condBranchFixups.Add((_code.Count, copyDoneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2, copy_done
    EmitWord(0x38401424 | (Reg(ARM64Register.X1) << 5)); // LDRB W4, [X1], #1
    EmitWord(0x38001404 | (Reg(ARM64Register.X0) << 5)); // STRB W4, [X0], #1
    EmitWord(0xD1000442 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X2)); // SUB X2, X2, #1
    _branchFixups.Add((_code.Count, copyLoopLabel));
    EmitWord(0x14000000); // B copy_loop
    DefineLabel(copyDoneLabel);

    // Return saved dest ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = dest
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000); // B done

    DefineLabel(emptyLabel);
    // Allocate 1-byte empty string via mm_raw_alloc (freed via mm_raw_free)
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000001 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB W1, [X0, #0]

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // maxon_os_environment_entry(index) -> heap-allocated cstring copy of environ[index], or a
  // freshly allocated "" once the index is past the last entry.
  //
  // maxon_command_line_arg's twin, and deliberately so: same ownership (RuntimeCallToManaged wraps
  // the cstring and mm_raw_free's this buffer), same out-of-range answer, same bytes-are-bytes
  // boundary. `stdlib/Subprocess.maxon` walks it from 0 until the empty answer and reassembles the
  // entries into the block a spawn is given, so there is no count entry beside it — the environment
  // has no empty entry a real one could be confused with.
  //
  // POSIX needs no encoding step at either end: environ[i] is already the UTF-8 "NAME=VALUE" cstring
  // the stdlib wants, and __subp_build_envp hands the reassembled block straight back to
  // posix_spawnp. (The Windows emitter converts, because its environment is UTF-16 — see
  // X86CodeEmitter.Runtime's EmitMaxonOsEnvironmentEntry.)
  //
  // Stack: [x29+0x18]=index counted down, [x29+0x20]=cursor into environ, [x29+0x28]=entry,
  //        [x29+0x30]=byte count including the NUL, [x29+0x38]=destination
  private void EmitMaxonOsEnvironmentEntryPosix() {
    EmitRuntimeFunctionStart("maxon_os_environment_entry", 1, 0x40);
    int n = _uniqueLabelCounter++;
    string scanLabel = $"__oee_scan_{n}";
    string foundLabel = $"__oee_found_{n}";
    string emptyLabel = $"__oee_empty_{n}";
    string doneLabel = $"__oee_done_{n}";

    EmitReloadArg(0);
    EmitStoreToStack(0x18, ARM64Register.X0, 8);
    EmitCallImport("_NSGetEnviron");
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitStoreToStack(0x20, ARM64Register.X0, 8);

    DefineLabel(scanLabel);
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitCmpImm(ARM64Register.X1, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, emptyLabel);
    EmitStoreToStack(0x28, ARM64Register.X1, 8);
    EmitLoadFromStack(ARM64Register.X2, 0x18, 8);
    EmitCmpImm(ARM64Register.X2, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, foundLabel);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false);
    EmitStoreToStack(0x18, ARM64Register.X2, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 8, isAdd: true);
    EmitStoreToStack(0x20, ARM64Register.X0, 8);
    EmitBranch(scanLabel);

    DefineLabel(foundLabel);
    EmitLoadFromStack(ARM64Register.X0, 0x28, 8);
    EmitBranchLink("maxon_strlen");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitStoreToStack(0x30, ARM64Register.X0, 8);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x38, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x28, 8);
    EmitLoadFromStack(ARM64Register.X2, 0x30, 8);
    EmitBranchLink("maxon_memcpy");
    EmitLoadFromStack(ARM64Register.X0, 0x38, 8);
    EmitBranch(doneLabel);

    DefineLabel(emptyLabel);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_executable_path() -> heap-allocated cstring with absolute executable path ---
  // macOS: calls _NSGetExecutablePath(buf, &bufsize) in a retry loop.
  // If the initial buffer is too small, _NSGetExecutablePath returns -1 and updates *bufsize
  // to the required size. We then free, realloc, and retry.
  // Stack frame: [x29+16]=heap buffer, [x29+24]=bufsize (8 bytes)
  private void EmitMaxonExecutablePath() {
    EmitRuntimeFunctionStart("maxon_executable_path", 0, 0x30);

    var retryLabel = $"__exepath_retry_{_uniqueLabelCounter}";
    var doneLabel = $"__exepath_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Start with 512-byte buffer
    EmitMovRegImm(ARM64Register.X0, 512);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // [x29+24] = bufsize = 512

    // Allocate initial heap buffer
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace); // X0 = buffer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // [x29+16] = buffer

    // Retry loop
    DefineLabel(retryLabel);

    // _NSGetExecutablePath(buf, &bufsize)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = buf
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 24, isAdd: true); // X1 = &bufsize
    EmitCallImport("_NSGetExecutablePath");
    // X0 = 0 on success, -1 if buffer too small (bufsize updated to required size)

    EmitCbz(ARM64Register.X0, doneLabel); // success → done

    // Buffer too small: free old buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // Allocate new buffer with the required size from *bufsize
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = required size
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace); // X0 = new buffer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save new buffer

    _branchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x14000000); // B retry

    DefineLabel(doneLabel);
    // Buffer now contains the null-terminated path. Return it directly — it's already heap-allocated.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_stdout_wants_ansi_color() -> 0 on this lane ---
  //
  // ⛔⛔ THIS LANE HAS NOT IMPLEMENTED TERMINAL DETECTION, AND 0 IS THE CONSERVATIVE HALF OF THE ANSWER
  // RATHER THAN A WRONG ONE. The x86 emitter asks three questions (GetFileType on the stdout handle,
  // NO_COLOR, TERM); the POSIX spelling of the first is `isatty(1)` and of the other two `getenv` — the
  // idiom EmitReadSlabFlagEnv already uses one screen up — so what is owed here is a transcription, not a
  // design. Until it is written, this answers "not a terminal", which is what `--color=auto` resolved to
  // on EVERY lane before the x86 half landed: a run on this target simply prints no colour.
  //
  // ⚠ It must never be made to answer 1 by default. The whole point of the question is that a REDIRECTED
  // stream is being captured, and a lane that guessed `yes` would put escape sequences into every golden,
  // every log file and every pipe — which is the one failure mode the detection exists to prevent.
  private void EmitMaxonStdoutWantsAnsiColor() {
    EmitRuntimeFunctionStart("maxon_stdout_wants_ansi_color", 0, 0x20);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_directory_exists(cstring_path) -> 1 if directory, 0 otherwise ---
  // Delegates to __io_submit_sync(SyncOpDirExists, path, 0).
  private void EmitMaxonDirectoryExists() {
    EmitRuntimeFunctionStart("maxon_directory_exists", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpDirExists);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_file_exists(cstring_path) -> 1 if file exists (not directory), 0 otherwise ---
  // Delegates to __io_submit_sync(SyncOpFileExists, path, 0).
  private void EmitMaxonFileExists() {
    EmitRuntimeFunctionStart("maxon_file_exists", 1, 0x30);
    EmitReloadArg(0); // X0 = path
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = path (arg0)
    EmitMovRegImm(ARM64Register.X0, SyncOpFileExists);  // X0 = op
    EmitMovRegImm(ARM64Register.X2, 0);                  // X2 = arg1
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_create_directory(cstring_path) -> nonzero on success, 0 on failure ---
  // Delegates to __io_submit_sync(SyncOpDirCreate, path, 0).
  private void EmitMaxonCreateDirectory() {
    EmitRuntimeFunctionStart("maxon_create_directory", 1, 0x30);
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X0, SyncOpDirCreate);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_get_current_directory() -> cstring pointer ---
  // Delegates to __io_submit_sync(SyncOpGetCwd, 0, 0).
  // The dispatch handler does open(".")+fcntl(F_GETPATH)+alloc+copy.
  private void EmitMaxonGetCurrentDirectory() {
    DefineSymdata("__dot_path", [(byte)'.', (byte)0]);
    EmitRuntimeFunctionStart("maxon_get_current_directory", 0, 0x30);
    EmitMovRegImm(ARM64Register.X0, SyncOpGetCwd);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  // macOS directory search block layout (used by managed dir search):
  // offset 0:  fd (8 bytes) - file descriptor from open()
  // offset 8:  buf_offset (8 bytes) - current offset within read buffer
  // offset 16: buf_valid (8 bytes) - bytes of valid data in buffer
  // offset 24: basep (8 bytes) - base for getdirentries64
  // offset 32: d_name_buf (256 bytes) - copy of current entry filename
  // offset 288: read_buf (4096 bytes) - getdirentries64 read buffer
  // Total: 4384 bytes → round up to 4384

  private const int DirBlockFd = 0;
  private const int DirBlockBufOffset = 8;
  private const int DirBlockBufValid = 16;
  private const int DirBlockBasep = 24;
  private const int DirBlockNameBuf = 32;
  private const int DirBlockReadBuf = 288;
  private const int DirBlockSize = 4384;

  // macOS dirent64 struct offsets
  private const int DirentReclen = 16; // d_reclen, 2 bytes
  private const int DirentNamelen = 18; // d_namlen, 2 bytes
  private const int DirentName = 21;   // d_name, variable

  private const long DotByte = 0x2E; // '.'

  // --- maxon_managed_dir_open_search(pattern_cstring) -> block_ptr or 0 ---
  // On macOS: strips trailing "/*" or "\*" from pattern, opens directory with open(),
  // does initial getdirentries64 read, skips "." and "..".
  private void EmitMaxonManagedDirOpenSearch() {
    EmitRuntimeFunctionStart("maxon_managed_dir_open_search", 1, 0x40);
    EmitReloadArg(0); // X0 = pattern cstring

    // Save pattern pointer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // [x29+40] tracks a freshly-allocated stripped-path copy to free after open()
    // (0 = the pattern was used as-is and aliases the caller's buffer; never free that).
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X29, 40, 8);

    // Strip trailing "/*" or "\*" from pattern
    // Find length of pattern first
    EmitBranchLink("maxon_strlen"); // X0 = len
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save len

    // Check if len >= 2 and last two chars are "/*" or "\*"
    var noStripLabel = $"__dir_nostrip_{_uniqueLabelCounter}";
    var stripDoneLabel = $"__dir_stripdone_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitMovRegImm(ARM64Register.X1, 2);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP len, 2
    _condBranchFixups.Add((_code.Count, noStripLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lo)); // B.LO nostrip

    // Load last char (pattern[len-1])
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // pattern
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // len
    EmitAluRegReg(0xCB000000, ARM64Register.X3, ARM64Register.X2, ARM64Register.Xzr); // X3 = len
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: false); // X3 = len-1
    // LDRB W4, [X1, X3]
    EmitWord(0x38606800 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X4));

    // Check if last char is '*' (42)
    EmitMovRegImm(ARM64Register.X5, 42);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP last, '*'
    _condBranchFixups.Add((_code.Count, noStripLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE nostrip

    // Strip by allocating a mutable copy of path without trailing "/*"
    // len-2 = path length without separator+'*'
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X2, 2, isAdd: false); // X3 = len-2
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X3, 1, isAdd: true); // alloc len-2+1
    EmitCallMmRawAlloc();
    // Save new buffer at [x29+24] (replaces original pattern pointer) and record it
    // at [x29+40] so it is freed once open() has consumed it.
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    // Copy original path bytes: memcpy(new_buf, original, len-2)
    EmitReloadArg(0); // X0 = original pattern
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // len
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 2, isAdd: false); // X2 = len-2
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = src
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = dst
    EmitBranchLink("maxon_memcpy");
    // Null-terminate the copy
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 2, isAdd: false);
    // STRB WZR, [X0, X1]
    EmitWord(0x38216800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.Xzr));
    _branchFixups.Add((_code.Count, stripDoneLabel));
    EmitWord(0x14000000); // B stripdone

    DefineLabel(noStripLabel);
    DefineLabel(stripDoneLabel);

    // Open directory: open(path, O_RDONLY|O_DIRECTORY)
    // O_RDONLY = 0, O_DIRECTORY = 0x100000
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // path
    EmitMovRegImm(ARM64Register.X1, 0x100000); // O_RDONLY | O_DIRECTORY
    EmitMovRegImm(ARM64Register.X2, 0); // mode (unused for open)
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    // Sign-extend W0→X0: open() returns int (32-bit), need sign extension for error check
    EmitWord(0x93407C00); // SXTW X0, W0

    // Free the stripped-path copy now that open() has consumed it. Done before the
    // error branch so it frees on both the success and failure paths. The open()
    // result is stashed across the free (mm_raw_free clobbers X0); [x29+40] is 0
    // when no copy was made (pattern aliases the caller's buffer — must not free).
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // stash open result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // X0 = stripped copy (or 0)
    var noFreeLabel = $"__dir_nofree_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, noFreeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ nofree
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel(noFreeLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // restore open result

    // Check if open failed
    var openFailLabel = $"__dir_openfail_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitBranchOnLibcError(openFailLabel);

    // Save fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Allocate block via mm_alloc
    EmitMovRegImm(ARM64Register.X0, DirBlockSize);
    EmitMovRegImm(ARM64Register.X1, 0); // no destructor
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X3, 0); // null trace scope
    EmitBranchLink("mm_alloc");

    // Save block ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Initialize block: set fd, zero out buf_offset and buf_valid
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // fd
    // STR fd, [block + DirBlockFd]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockFd, 8);
    // Zero out buf_offset, buf_valid, basep
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockBufOffset, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockBufValid, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockBasep, 8);

    // Now allocate the __ManagedDirectory struct (8 bytes: one field = block_ptr)
    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedDirectory");
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X3, 0); // null trace scope
    EmitBranchLink("mm_alloc");

    // Store block_ptr at [dir_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // block_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    // Save dir_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Advance-first: open does NOT pre-load an entry. block starts with no
    // current entry; the first maxon_find_next_file() advances to the first
    // REAL entry (it already skips "."/".."), returning 1 if found or 0 at
    // end-of-iteration. This keeps the runtime the sole owner of dot-filtering.

    // Restore and return dir_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Return dir_ptr
    _branchFixups.Add((_code.Count, $"__dir_open_done_{_uniqueLabelCounter}"));
    EmitWord(0x14000000);

    DefineLabel(openFailLabel);
    // Capture errno into gt->io_error_code so the lowering can map it to
    // notFound / accessDenied for ManagedDirectory.openSearch.
    EmitCaptureErrnoToGt();
    EmitMovRegImm(ARM64Register.X0, 0);

    DefineLabel($"__dir_open_done_{_uniqueLabelCounter}");
    _uniqueLabelCounter++;
    EmitRuntimeFunctionEnd();
  }

  // --- __destruct___ManagedDirectory(ptr) ---
  // Destructor: closes fd and frees block
  private void EmitDestructManagedDirectory() {
    EmitRuntimeFunctionStart("__destruct___ManagedDirectory", 1, 0x30);
    EmitReloadArg(0); // X0 = user_ptr

    // Load block_ptr = [user_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    var doneLabel = $"__dtor_dir_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // If block_ptr == 0, skip
    EmitWord(0xF100003F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done

    // Save block_ptr and user_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // save block
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save user_ptr

    // Close fd: close([block + 0])
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X1, DirBlockFd, 8);
    var skipCloseLabel = $"__dtor_dir_skipclose_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, skipCloseLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE skip (fd <= 0 means invalid)
    EmitCallImport("close");
    DefineLabel(skipCloseLabel);

    // Free block directly — block has no refcount (allocated with mm_alloc but
    // never increffed; it's an internal resource, not a managed reference)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranchLink("mm_free");

    // Zero block field in user struct for idempotency
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_managed_dir_close(block_ptr) ---
  // Close the fd in the block and zero it
  private void EmitMaxonManagedDirClose() {
    EmitRuntimeFunctionStart("maxon_managed_dir_close", 1, 0x30);
    EmitReloadArg(0); // X0 = block_ptr

    var doneLabel = $"__dirclose_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done

    // Save block ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Load and close fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, DirBlockFd, 8);
    var skipLabel = $"__dirclose_skip_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, skipLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE skip
    EmitCallImport("close");
    DefineLabel(skipLabel);

    // Zero the fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, DirBlockFd, 8);

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_find_next_file(block_ptr) -> nonzero if found, 0 if done ---
  // Reads next directory entry from block, skipping "." and "..".
  // Copies filename to block's name buffer.
  private void EmitMaxonFindNextFile() {
    EmitRuntimeFunctionStart("maxon_find_next_file", 1, 0x40);
    EmitReloadArg(0); // X0 = block_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save block

    var retryLabel = $"__findnext_retry_{_uniqueLabelCounter}";
    var readMoreLabel = $"__findnext_readmore_{_uniqueLabelCounter}";
    var foundLabel = $"__findnext_found_{_uniqueLabelCounter}";
    var doneLabel = $"__findnext_done_{_uniqueLabelCounter}";
    var eofLabel = $"__findnext_eof_{_uniqueLabelCounter}";
    var errorLabel = $"__findnext_error_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    DefineLabel(retryLabel);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block

    // Check if buf_offset >= buf_valid (need to read more)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, DirBlockBufOffset, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, DirBlockBufValid, 8);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5)); // CMP offset, valid
    _condBranchFixups.Add((_code.Count, readMoreLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE readmore

    // Have data: parse current dirent
    // entry_ptr = block + DirBlockReadBuf + buf_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, DirBlockBufOffset, 8);
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X0, DirBlockReadBuf, isAdd: true); // X3 = &read_buf
    EmitAluRegReg(0x8B000000, ARM64Register.X3, ARM64Register.X3, ARM64Register.X1); // X3 = entry_ptr

    // Read d_reclen (uint16_t at offset 16)
    EmitWord(0x79400000 | ((DirentReclen / 2) << 10) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4));

    // Advance buf_offset += d_reclen
    EmitAluRegReg(0x8B000000, ARM64Register.X5, ARM64Register.X1, ARM64Register.X4); // new offset
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X0, DirBlockBufOffset, 8);

    // Get d_name pointer = entry_ptr + 21
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, DirentName, isAdd: true);

    // Skip "." and "..". The runtime owns this filtering on every target — the
    // stdlib's Directory.list does none (`while next() != 0` pushes whatever it
    // gets), so a dot leaking out of here reaches callers as a real entry, and a
    // ".." one escapes upward out of the tree being walked. Mirrors x86's
    // rt_fnf_dotcheck: only a NUL-terminated "." or ".." is a pseudo-entry, so
    // ".x" and "..x" are real names and must survive.
    //
    // Safe against the read-more loop because buf_offset was already advanced
    // above: branching to retry moves to the NEXT entry rather than re-reading
    // this one.

    // byte0 != '.' → real entry
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6]
    EmitMovRegImm(ARM64Register.X8, DotByte);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP byte0, '.'
    _condBranchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE found

    // byte0 == '.': inspect byte1.
    EmitWord(0x39400400 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6, #1]
    EmitWord(0xF100001F | (Reg(ARM64Register.X7) << 5)); // CMP byte1, #0
    _condBranchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ retry — "." → skip

    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP byte1, '.'
    _condBranchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE found — ".x" → real

    // byte0 == '.' && byte1 == '.': ".." only if byte2 is NUL.
    EmitWord(0x39400800 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6, #2]
    EmitWord(0xF100001F | (Reg(ARM64Register.X7) << 5)); // CMP byte2, #0
    _condBranchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ retry — ".." → skip
    // "..x" → real entry, falls through.

    DefineLabel(foundLabel);
    // Copy filename to name buffer in block
    // dest = block + DirBlockNameBuf, src = X6 (d_name), len = d_namlen
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, DirBlockNameBuf, isAdd: true); // dest
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X6); // src = d_name
    // Read d_namlen (uint16_t at dirent offset 18)
    EmitWord(0x79400000 | ((DirentNamelen / 2) << 10) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X2));
    // Include null terminator
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: true);
    EmitBranchLink("maxon_memcpy");

    // Return 1 (found)
    EmitMovRegImm(ARM64Register.X0, 1);
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(readMoreLabel);
    // Need to read more: getdirentries64(fd, buf, bufsize, &basep)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, DirBlockFd, 8); // fd

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 24, 8); // block (temp)
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X3, DirBlockReadBuf, isAdd: true); // buf
    EmitMovRegImm(ARM64Register.X2, 4096); // bufsize
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, DirBlockBasep, isAdd: true); // &basep

    EmitCallImport("__getdirentries64");

    // Check for error (carry set / errno) → return -1 (real OS error, not end of directory).
    EmitBranchOnLibcError(errorLabel);
    // Check for EOF: X0 = 0 bytes read
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, eofLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ eof

    // Update buf_valid and reset buf_offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // block
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, DirBlockBufValid, 8);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, DirBlockBufOffset, 8);

    // Retry with new data
    _branchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x14000000);

    DefineLabel(eofLabel);
    EmitMovRegImm(ARM64Register.X0, 0); // no more entries
    _branchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x14000000);

    DefineLabel(errorLabel);
    // Real OS error from getdirentries64 — capture errno before returning -1.
    // (Distinct from the EOF path above, which is not an error.)
    EmitCaptureErrnoToGt();
    EmitMovRegImm(ARM64Register.X0, -1); // real OS error

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- maxon_find_filename(block_ptr) -> cstring pointer to filename ---
  // Returns pointer to the name buffer within the block.
  private void EmitMaxonFindFilename() {
    DefineLabel("maxon_find_filename");
    // If block_ptr is null, return empty string
    var validLabel = $"__findname_valid_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, validLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE valid

    // Return empty string
    DefineSymdata("__empty_cstr", [(byte)0]);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__empty_cstr");
    EmitWord(0xD65F03C0); // RET

    DefineLabel(validLabel);
    // Return block + DirBlockNameBuf
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, DirBlockNameBuf, isAdd: true);
    EmitWord(0xD65F03C0); // RET
  }

  // ============================================================================
  // Green Thread Runtime for async/await (ARM64/macOS)
  // ============================================================================

  // Gt and P struct offsets, sizes, status enum, and stack-growth constants
  // live in MaxonSharp.Compiler.Ir.Runtime.GtLayout (imported via `using static`
  // at the top of this file). Single source of truth, shared with both backends
  // and RuntimeEmitter.

  // SyncRequest layout (40 bytes) — queued I/O operations
  private const int SyncReqSize = 0x28; // 40 bytes
  private const int SyncReqOffOp = 0x00;
  private const int SyncReqOffArg0 = 0x08;
  private const int SyncReqOffArg1 = 0x10;
  private const int SyncReqOffWaiter = 0x18;
  private const int SyncReqOffNext = 0x20;

  // Sync op codes (must match x86 values)
  private const long SyncOpFileExists = 0;
  private const long SyncOpFileDelete = 1;
  private const long SyncOpDirExists = 4;
  private const long SyncOpDirCreate = 5;
  private const long SyncOpGetCwd = 6;
  private const long SyncOpFileOpenRead = 7;
  private const long SyncOpFileOpenWrite = 8;
  private const long SyncOpCloseHandle = 9;
  private const long SyncOpNetConnect = 10;
  private const long SyncOpNetSend = 11;
  private const long SyncOpNetRecv = 12;
  private const long SyncOpNetClose = 13;
  private const long SyncOpFileOpenWriteExec = 14;
  private const long SyncOpFileRename = 15;

  // P (ProcContext) struct offsets and MaxFreeListLen live in GtLayout / RuntimeEmitter.Scheduler.

  private void EmitGreenThreadRuntime() {
    // GMP scheduler globals
    DefineGlobal("__sched_procs", 8, 0);           // P*[] array pointer
    DefineGlobal("__sched_num_procs", 8, 0);       // number of P structs allocated
    DefineGlobal("__sched_max_procs", 8, 0);       // max worker threads (CPU count)

    DefineGlobal("__sched_active_workers", 8, 0);   // atomic count of running workers
    DefineGlobal("__sched_max_active_workers", 8, 0); // high-water mark of active_workers (only grows)
    DefineGlobal("__sched_shutdown_flag", 8, 0);     // 1 = shutdown requested
    DefineGlobal("__sched_tls_key", 8, 0);           // pthread_key_t for P*
    // __sched_global_lock and __sched_timer_lock are accessed via the backend
    // recursive LockAcquire/LockRelease, each a 24-byte { lock(8), owner(8), count(8) }
    // struct (see ARM64CodeEmitter.Backend.LockAcquire). They MUST be 24 bytes, not 8:
    // an 8-byte global lets LockAcquire's owner/count writes spill into the next
    // global, which under the multi-OS-thread scheduler corrupts adjacent scheduler
    // state (e.g. the timer lock's count overlapped __gt_run_queue_head — the bug this
    // sizing fixes). __sched_io_lock is an 8-byte os_unfair_lock (EmitLockAcquire path)
    // sized 24 only for uniformity (harmless slack).
    DefineGlobal("__sched_global_lock", 24, 0);      // recursive spinlock for global queue
    // Guards __gt_all_head + __gt_live_count. (The two comments here used to say it was UNUSED and
    // that __sched_global_lock guarded the list — both false since __gt_trampoline and __gt_spawn
    // took it, and __netpoll_recover walks the list under it.)
    DefineGlobal("__sched_all_lock", 24, 0);         // recursive spinlock for the all-threads list
    DefineGlobal("__sched_timer_lock", 24, 0);       // recursive spinlock for timer heap
    DefineGlobal("__sched_io_lock", 24, 0);          // os_unfair_lock for I/O request queue (8B suffices)

    // Global run queue (shared across workers, protected by __sched_global_lock)
    DefineGlobal("__gt_run_queue_head", 8, 0);
    DefineGlobal("__gt_run_queue_tail", 8, 0);

    // Thread tracking
    DefineGlobal("__gt_live_count", 8, 0);           // atomic count of non-completed GTs
    DefineGlobal("__gt_all_head", 8, 0);             // all-threads list (protected by __sched_all_lock)

    // Sync I/O request queue (protected by __sched_io_lock)
    DefineGlobal("__io_sync_req_head", 8, 0);
    DefineGlobal("__io_sync_req_tail", 8, 0);
    DefineGlobal("__io_sync_req_semaphore", 8, 0);   // ptr to wake lock block (mutex+cond+count) to wake I/O worker

    // I/O completion queue (posted by sync worker, drained by scheduler)
    DefineGlobal("__io_done_head", 8, 0);
    DefineGlobal("__io_done_tail", 8, 0);
    DefineGlobal("__io_done_lock", 8, 0);            // os_unfair_lock for done queue

    // Timer heap globals (protected by __sched_timer_lock)
    DefineGlobal("__gt_timer_heap", TimerHeapCapacity * TimerEntrySize, 0); // 256 entries * 16 bytes
    DefineGlobal("__gt_timer_count", 8, 0);

    // kqueue globals (kqueue is thread-safe on macOS)
    DefineGlobal("__io_kqueue_fd", 8, 0);
    // Base of the per-P kevent eventlist buffers (each 32 events * 32 bytes = 1 KiB),
    // mmap'd in __gt_init sized to max_procs. Per-P (not a single shared buffer) so
    // that concurrent __io_poll_kqueue calls on different worker OS threads don't
    // race on one eventlist — EV_ONESHOT delivers each event to exactly one kevent()
    // call, so disjoint buffers guarantee no two Ms process (and double-free) the
    // same KqCtx. __io_poll_kqueue indexes this by the calling P's id.
    DefineGlobal("__io_kevent_bufs_base", 8, 0);

    // Scheduler functions migrated to RuntimeEmitter (shared x86/ARM64). Declared HERE, ahead of the
    // globals block, so the park protocol's globals and its functions come out of ONE emitter.
    //
    // ⚠ THAT IS STRUCTURAL, NOT TIDINESS: RuntimeEmitter.UniqueLabel counts per INSTANCE, so a
    // throwaway emitter for the globals would start its counter at 0 and mint `__netpoll_*_0` names
    // that collide with this one's the moment either side grows a label. It was harmless only
    // because EmitNetpollGlobals mints none today — a property of what that method happens to do,
    // which nothing checks and nothing would preserve.
    var schedRt = new Runtime.RuntimeEmitter(CreateBackend());

    // The async-I/O park protocol's own globals — see RuntimeEmitter.Netpoll.cs, which owns the
    // protocol for every target.
    schedRt.EmitNetpollGlobals();

    // Trace lock always defined to keep data layout stable (only used when AsyncTrace is enabled)
    DefineGlobal("__gt_trace_counter", 8, 0);
    DefineGlobal("__sched_trace_lock", 8, 0);

    if (Compiler.AsyncTrace) {
      DefineSymdata("__at_tag_spawn", "spawn #\0"u8.ToArray());
      DefineSymdata("__at_tag_await", "await #\0"u8.ToArray());
      DefineSymdata("__at_tag_await_yield", " [yield]\0"u8.ToArray());
      DefineSymdata("__at_tag_await_imm", " [immediate]\0"u8.ToArray());
      DefineSymdata("__at_tag_try_await", "try_await #\0"u8.ToArray());
      DefineSymdata("__at_tag_cancel", "cancel #\0"u8.ToArray());
      DefineSymdata("__at_tag_nl", "\n\0"u8.ToArray());
      DefineSymdata("__at_tag_io_yield", "io_yield #\0"u8.ToArray());
      DefineSymdata("__at_tag_io_resume", "io_resume #\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_exists", " [file_exists]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_delete", " [file_delete]\0"u8.ToArray());
      DefineSymdata("__at_io_op_dir_exists", " [dir_exists]\0"u8.ToArray());
      DefineSymdata("__at_io_op_dir_create", " [dir_create]\0"u8.ToArray());
      DefineSymdata("__at_io_op_get_cwd", " [get_cwd]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_open_read", " [file_open_read]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_open_write", " [file_open_write]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_open_write_exec", " [file_open_write_exec]\0"u8.ToArray());
      DefineSymdata("__at_io_op_close_handle", " [close_handle]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_connect", " [net_connect]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_send", " [net_send]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_recv", " [net_recv]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_close", " [net_close]\0"u8.ToArray());
      DefineSymdata("__at_tag_sleep_yield", "sleep_yield #\0"u8.ToArray());
      DefineSymdata("__at_tag_sleep_resume", "sleep_resume #\0"u8.ToArray());
    }

    DefineSymdata("__io_panic_msg", "PANIC: unknown I/O op code\n\0"u8.ToArray());


    EmitGtInit();
    schedRt.EmitGtStackHigh();
    schedRt.EmitGtStackHighCurrent();
    schedRt.EmitGtEnqueue();
    schedRt.EmitGtDequeue();
    schedRt.EmitGtStealWork();
    schedRt.EmitNetpollFunctions();
    EmitSchedWorkerLoop();
    EmitGtSpawn();
    EmitGtTrampoline();
    EmitGtContextSwitch();
    EmitGtAwait();
    EmitGtIsComplete();
    EmitGtTryAwait();
    EmitGtYield();
    EmitGtCancel();
    EmitGtCleanup();
    EmitGtProcessPendingSyncReq();
    EmitGtProcessPendingWaiter();
    schedRt.EmitMaxonYield();
    schedRt.EmitGtTimerAdd();
    schedRt.EmitGtTimerCheck();
    EmitGtMorestack();
    EmitGtPanicIo();
    EmitIoRuntime();
    // Fault handler (CPU faults: nil deref, divide-by-zero, stack overflow).
    schedRt.EmitFaultHandlerData();
    schedRt.EmitGtFaultDiagnosticAddrGlobal();
    schedRt.EmitGtFaultHandler();
    schedRt.EmitGtFaultDiagnostic();
    // Per-backend thunk that the OS calls. Prolog defines the function entry,
    // extracts the fault context and calls the shared __gt_fault_handler.
    // Epilog continues from there and emits the function exit (returns to OS).
    EmitFaultHandlerProlog("__gt_fault_handler_thunk", "__gt_fault_handler");
    EmitFaultHandlerEpilog();
    // Debug agent trap-handler thunk + the two `.text`-patch primitives — emitted into every binary
    // unless --no-debug-agent. __dbg_init arms the thunk (sigaction SIGTRAP) only when MAXON_DEBUG is
    // set; the patch primitives are called by __dbg_set_bp / __dbg_clear_bp / the single-step-over.
    if (!Compiler.NoDebugAgent) {
      EmitDbgTrapHandlerThunk();
      EmitDbgArmBp();
      EmitDbgDisarmBp();
    }
    // Go-style dieFromSignal handler for SIGTERM/SIGINT so the process terminates
    // cleanly on kill instead of wedging in macOS uninterruptible ('UE') state.
    EmitDieFromSignalThunk("__gt_die_from_signal_thunk");
  }

  /// <summary>
  /// __gt_init(): Initialize the main thread's GreenThread struct.
  /// Called from mrt_start before main. Sets status=running, sets X28 = P[0].
  /// </summary>
  private void EmitGtInit() {
    // __gt_init(): Initialize the GMP scheduler.
    // Allocates TLS key, queries CPU count, allocates P structs with wake semaphores,
    // sets P[0] as the main thread's P, initializes kqueue.
    // Stack: [x29+16]=P[0], [x29+24]=procs_array, [x29+32]=max_procs, [x29+40]=loop_i, [x29+48]=current_P
    EmitRuntimeFunctionStart("__gt_init", 0, 0x80);

    // Step 1: Allocate TLS key — pthread_key_create(&__sched_tls_key, NULL)
    EmitGlobalLeaReg(ARM64Register.X0, "__sched_tls_key");
    EmitMovRegImm(ARM64Register.X1, 0); // destructor = NULL
    EmitCallImport("pthread_key_create");

    // Step 2: Query CPU count — sysconf(_SC_NPROCESSORS_ONLN)
    EmitMovRegImm(ARM64Register.X0, ScNprocessorsOnln);
    EmitCallImport("sysconf");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // [x29+32] = max_procs
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_max_procs");
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_num_procs");

    // Step 2b: apply an optional MAXON_MAX_PROCS override (clamp down only).
    EmitReadMaxProcsEnvOverride();

    // Step 2c: arm the async-I/O park protocol's fault injection from the environment (0 = off).
    EmitBranchLink("__netpoll_init");

    // Step 3: Allocate P*[] array — mmap(max_procs * 8). OS-backed (see
    // EmitMmapAnon) to match x86's VirtualAlloc and stay off the MM leak ledger.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // LSL X0, X0, #3  (multiply by 8)
    EmitWord(0xD37DF000);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = byte count for mmap
    EmitMmapAnon();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // [x29+24] = procs_array
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_procs");

    // Step 4: Loop to allocate P structs
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // [x29+40] = i = 0
    DefineLabel("__sched_init_ploop");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // i
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // max_procs
    // CMP X0, X1
    EmitWord(0xEB01001F);
    EmitBranchCond(ARM64ConditionCode.Hs, "__sched_init_ploop_done"); // i >= max_procs → done

    // Allocate P[i] struct — mmap (OS-backed; see EmitMmapAnon)
    EmitMovRegImm(ARM64Register.X1, PStructSize);
    EmitMmapAnon();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // [x29+48] = P[i]

    // Store P[i] into procs_array[i]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // procs_array
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // i
    // X1 = procs_array + i * 8: ADD X1, X1, X2, LSL #3
    EmitWord(0x8B020C21);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, 0, 8);

    // Set P[i]->id = i
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 48, 8); // P[i]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // i
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffId, 8);

    // Set P[i]->rng = i + 1 (non-zero xorshift64 seed)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffRng, 8);

    // Create wake lock block: {pthread_mutex_t, pthread_cond_t, count} (Go semasleep/
    // semawakeup, replacing dispatch_semaphore_create(0)). count starts 0 (parked).
    EmitCreateWakeLockBlock();
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 48, 8); // P[i]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffWakeSemaphore, 8);

    // Allocate system stack via mmap (OS-backed; see EmitMmapAnon)
    EmitMovRegImm(ARM64Register.X1, PSystemStackSize);
    EmitMmapAnon();
    // Store top of system stack (base + size) in P->systemStackSP
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, PSystemStackSize, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 48, 8); // P[i]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffSystemStackSP, 8);

    // Increment loop counter
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__sched_init_ploop");
    DefineLabel("__sched_init_ploop_done");

    // Step 5: Set P[0] as the active worker for the main OS thread
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // procs_array
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8);   // P[0]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // [x29+16] = P[0]

    // P[0]->status = 1 (active)
    EmitMovRegImm(ARM64Register.X1, PStatusActive);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, POffStatus, 8);

    // Set TLS: pthread_setspecific(__sched_tls_key, P[0])
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_tls_key");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // P[0]
    EmitCallImport("pthread_setspecific");

    // Set active_workers = 1; high-water mark starts at 1 (P[0] is live).
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_active_workers");
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_max_active_workers");

    // Initialize P[0].mainThread: status = Running, stackBase = 0 (already zero from alloc)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // P[0]
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, POffMainThread, isAdd: true);   // X0 = &P[0].mainThread
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // P[0]->currentGt = &P[0].mainThread
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffCurrentGt, 8);

    // Set X28 = P[0] for the main OS thread
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X28, ARM64Register.X29, 16, 8); // X28 = P[0] from stack

    // Step 6: Initialize kqueue
    EmitCallImport("kqueue");
    EmitGlobalStoreReg(ARM64Register.X0, "__io_kqueue_fd");

    // Allocate the per-P kevent eventlist buffers: max_procs * 1 KiB. Each worker's
    // __io_poll_kqueue reads into its own P->id slice so concurrent polls never share
    // an eventlist (see __io_kevent_bufs_base).
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // max_procs
    EmitLslImm(ARM64Register.X1, ARM64Register.X1, 10);                                // * 1024
    EmitMmapAnon();
    EmitGlobalStoreReg(ARM64Register.X0, "__io_kevent_bufs_base");

    // Step 7: Create I/O sync request wake lock block (mutex+cond+count)
    EmitCreateWakeLockBlock();
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_semaphore");

    // Install signal handlers BEFORE spawning any worker thread, so a fault on a
    // worker (e.g. during its first park) — or in the dieFromSignal install itself —
    // is CAUGHT and diagnosed instead of crashing silently before any handler exists.
    // Step 10: Install the CPU-fault handler (sigaction on macOS).
    EmitInstallFaultHandler("__gt_fault_handler_thunk");

    // Step 11: Install the dieFromSignal handler for SIGTERM/SIGINT so the process
    // terminates cleanly on kill (no wedge in idle workers / munmap).
    EmitInstallDieFromSignalHandler("__gt_die_from_signal_thunk");

    // Step 8: Spawn the I/O sync worker thread
    // pthread_create(&tid, NULL, __io_sync_worker_loop, NULL)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true); // X0 = &tid (unused, on stack)
    EmitMovRegImm(ARM64Register.X1, 0); // attr = NULL
    EmitAdrpAddFixup(ARM64Register.X2, _funcAddrAdrpFixups, "__io_sync_worker_loop"); // function pointer
    EmitMovRegImm(ARM64Register.X3, 0); // arg = NULL
    EmitCallImport("pthread_create");

    // Cache the MAXON_SLAB_GLOBAL_LOCK / MAXON_SLAB_STATS flags before the
    // allocator is first usable (PLAN 1a.1 / 1a.2). Must precede any allocation;
    // nothing allocates before __gt_init returns, so here is safely early.
    EmitReadSlabFlagsEnv();

    // Step 9: Initialize slab allocator
    EmitBranchLink("__slab_init");

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emitted inline within __gt_init. Reads the MAXON_SLAB_GLOBAL_LOCK and
  /// MAXON_SLAB_STATS environment variables and, when either names a non-empty
  /// value, sets its runtime-cached enable flag (RuntimeEmitter.
  /// SlabGlobalLockEnabledLabel / SlabStatsEnabledLabel) to 1. Presence of any
  /// non-empty value enables the flag; unset or empty leaves it 0. getenv returns
  /// a pointer to the value (NULL if unset), needing no stack buffer; __gt_init
  /// runs on the main OS thread so a direct call needs no system-stack switch.
  /// </summary>
  private void EmitReadSlabFlagsEnv() {
    DefineSymdata("__slab_global_lock_env_name", "MAXON_SLAB_GLOBAL_LOCK\0"u8.ToArray());
    DefineSymdata("__slab_stats_env_name", "MAXON_SLAB_STATS\0"u8.ToArray());

    EmitReadSlabFlagEnv("__slab_global_lock_env_name",
      Runtime.RuntimeEmitter.SlabGlobalLockEnabledLabel, "glock");
    EmitReadSlabFlagEnv("__slab_stats_env_name",
      Runtime.RuntimeEmitter.SlabStatsEnabledLabel, "sstats");
  }

  /// <summary>
  /// Set <paramref name="flagGlobal"/> to 1 when the environment variable named by
  /// <paramref name="nameSym"/> holds a non-empty value. getenv returns NULL when
  /// unset and a pointer to the value otherwise; we additionally require the first
  /// byte to be non-NUL so an empty value ("VAR=") leaves the flag off, matching
  /// the Windows GetEnvironmentVariableA "chars-copied > 0" semantics.
  /// </summary>
  private void EmitReadSlabFlagEnv(string nameSym, string flagGlobal, string tag) {
    var doneLabel = $"__gt_init_slabflag_{tag}_done";

    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, nameSym);
    EmitCallImport("getenv");
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, done — unset, leave the flag at 0

    // Non-null value: require a non-empty string (first byte != 0).
    EmitLoadStoreUnsignedImm(0x39400000, ARM64Register.X1, ARM64Register.X0, 0, 1); // LDRB W1, [X0]
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, done — empty value, leave the flag at 0

    EmitMovRegImm(ARM64Register.X1, 1);
    EmitGlobalStoreReg(ARM64Register.X1, flagGlobal);
    DefineLabel(doneLabel);
  }

  /// <summary>
  /// Emitted inline within __gt_init (uses that function's frame). Reads the
  /// MAXON_MAX_PROCS environment variable and, when it names a positive integer
  /// strictly less than the detected CPU count, lowers the detected count to that
  /// value — both scheduler globals (__sched_max_procs / __sched_num_procs) AND
  /// __gt_init's local max_procs slot ([x29+GtInitMaxProcsSlotOffset]), so the
  /// rest of init allocates exactly that many P structs. MAXON_MAX_PROCS=1
  /// therefore forces single-threaded scheduling: the __gt_enqueue worker-spawn
  /// gate (active_workers < max_procs) can never fire, giving deterministic traces
  /// and a single-threaded validation harness. Unset / empty / non-numeric values
  /// are ignored, and values >= the detected count are ignored (this only clamps
  /// down). getenv returns a pointer to the value string, so no stack buffer is
  /// needed. Uses X9..X12 as scratch (freshly reloaded by the following init steps).
  /// </summary>
  private void EmitReadMaxProcsEnvOverride() {
    const string overrideDoneLabel = "__gt_init_maxprocs_done";

    DefineSymdata("__maxprocs_env_name", "MAXON_MAX_PROCS\0"u8.ToArray());
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__maxprocs_env_name");
    EmitCallImport("getenv");
    EmitParseUnsignedCstrIntoX9(ARM64Register.X0);

    // Apply only when 1 <= parsed < detected max_procs (clamp down only).
    EmitCmpImm(ARM64Register.X9, 1);
    EmitBranchCond(ARM64ConditionCode.Lt, overrideDoneLabel); // parsed < 1 -> ignore
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X29, GtInitMaxProcsSlotOffset, 8); // detected max_procs
    EmitCmpRegReg(ARM64Register.X9, ARM64Register.X11);
    EmitBranchCond(ARM64ConditionCode.Ge, overrideDoneLabel); // parsed >= detected -> ignore

    // Commit: local slot + both scheduler globals all take the clamped value.
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X29, GtInitMaxProcsSlotOffset, 8);
    EmitGlobalStoreReg(ARM64Register.X9, "__sched_max_procs");
    EmitGlobalStoreReg(ARM64Register.X9, "__sched_num_procs");

    DefineLabel(overrideDoneLabel);
  }

  /// <summary>
  /// X9 = the unsigned decimal value of the null-terminated string at <paramref name="ptrReg"/>, or
  /// 0 when the pointer is NULL or the string does not start with a digit. Parsing stops at the
  /// first non-digit byte (the NUL terminator included), so a trailing suffix is ignored rather than
  /// rejected — every caller treats 0 as "leave the default alone", which is also what a malformed
  /// value should get. X10/X11/X12 are scratch.
  ///
  /// It takes a POINTER rather than an environment variable name because the two callers get that
  /// pointer differently — __gt_init calls getenv inline on the main OS thread, while the shared
  /// IEmitterBackend.ReadEnvUnsigned has to present the same answer as Windows'
  /// GetEnvironmentVariableA-into-a-buffer — and the decimal parse is the only part they share.
  /// </summary>
  private void EmitParseUnsignedCstrIntoX9(ARM64Register ptrReg) {
    const int asciiZero = '0';
    const int maxDecimalDigit = 9;
    const int decimalRadix = 10;
    var parseLoopLabel = $"__cstr_parse_loop_{_uniqueLabelCounter}";
    var parseDoneLabel = $"__cstr_parse_done_{_uniqueLabelCounter++}";

    // X10 = cursor, X9 = accumulator, X11 = digit, X12 = radix(10).
    EmitMovRegReg(ARM64Register.X10, ptrReg);
    EmitMovRegImm(ARM64Register.X9, 0);
    _condBranchFixups.Add((_code.Count, parseDoneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X10)); // CBZ X10, done — NULL pointer
    EmitMovRegImm(ARM64Register.X12, decimalRadix);

    DefineLabel(parseLoopLabel);
    EmitLoadStoreUnsignedImm(0x39400000, ARM64Register.X11, ARM64Register.X10, 0, 1); // LDRB W11, [X10]
    EmitAddSubImm(ARM64Register.X11, ARM64Register.X11, asciiZero, isAdd: false);      // digit = c - '0'
    EmitCmpImm(ARM64Register.X11, maxDecimalDigit);
    EmitBranchCond(ARM64ConditionCode.Hi, parseDoneLabel); // unsigned > 9 -> non-digit -> stop
    // acc = acc*10 + digit  (MADD X9, X9, X12, X11)
    EmitWord(0x9B000000 | (Reg(ARM64Register.X12) << 16) | (Reg(ARM64Register.X11) << 10)
      | (Reg(ARM64Register.X9) << 5) | Reg(ARM64Register.X9));
    EmitAddSubImm(ARM64Register.X10, ARM64Register.X10, 1, isAdd: true); // ++cursor
    EmitBranch(parseLoopLabel);

    DefineLabel(parseDoneLabel);
  }

  // __gt_enqueue, __gt_dequeue, and __gt_steal_work are now emitted by RuntimeEmitter.Scheduler.cs

  /// <summary>
  /// Atomically increment the 64-bit word at the address in <paramref name="addrReg"/>.
  /// Uses LDAXR/ADD/STLXR with X16/X17/W15 scratch (mirrors the backend AtomicInc).
  /// </summary>
  private void EmitAtomicIncReg(ARM64Register addrReg) {
    if (addrReg != ARM64Register.X16)
      EmitMovRegReg(ARM64Register.X16, addrReg);
    var retry = $"__sched_ainc_retry_{_uniqueLabelCounter++}";
    DefineLabel(retry);
    EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // LDAXR X17, [X16]
    EmitAddSubImm(ARM64Register.X17, ARM64Register.X17, 1, isAdd: true);           // ADD X17, X17, #1
    EmitWord(0xC800FC00 | (15u << 16) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // STLXR W15, X17, [X16]
    _condBranchFixups.Add((_code.Count, retry));
    EmitWord(0x35000000 | 15u); // CBNZ W15, retry
  }

  /// <summary>
  /// Atomically decrement the 64-bit word at the address in <paramref name="addrReg"/>.
  /// Uses LDAXR/SUBS/STLXR with X16/X17/W15 scratch (mirrors the backend AtomicDec).
  /// </summary>
  private void EmitAtomicDecReg(ARM64Register addrReg) {
    if (addrReg != ARM64Register.X16)
      EmitMovRegReg(ARM64Register.X16, addrReg);
    var retry = $"__sched_adec_retry_{_uniqueLabelCounter++}";
    DefineLabel(retry);
    EmitWord(0xC85FFC00 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // LDAXR X17, [X16]
    EmitWord(0xF1000000 | (1u << 10) | (Reg(ARM64Register.X17) << 5) | Reg(ARM64Register.X17)); // SUBS X17, X17, #1
    EmitWord(0xC800FC00 | (15u << 16) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X17)); // STLXR W15, X17, [X16]
    _condBranchFixups.Add((_code.Count, retry));
    EmitWord(0x35000000 | 15u); // CBNZ W15, retry
  }

  /// <summary>
  /// __sched_worker_loop(arg_x0=P*): Entry point for worker OS threads.
  /// pthread signature: void* (*)(void* arg)
  /// Sets TLS, then loops: process pending waiters, check timers, dequeue+run GTs, park when idle.
  /// Stack: [x29+16]=P*
  /// </summary>
  private void EmitSchedWorkerLoop() {
    EmitRuntimeFunctionStart("__sched_worker_loop", 1, 0x40);
    // [x29+16] = P* (passed as arg)

    // Set TLS: pthread_setspecific(__sched_tls_key, P*)
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_tls_key");
    EmitReloadArg(0); // X1 = P*
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    // Oops — need key in X0, P* in X1. Let me redo:
    EmitReloadArg(0); // X0 = P*  (re-read from stack since we clobbered)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save P* to stack
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_tls_key"); // X0 = key
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // X1 = P*
    EmitCallImport("pthread_setspecific");

    // Initialize P->mainThread: status = Running
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // P*
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, POffMainThread, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    // P->currentGt = &P->mainThread
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffCurrentGt, 8);

    // Set X28 = P* for this worker thread
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X28, ARM64Register.X0);

    // Install a fresh altstack for THIS worker OS thread. The main-thread install
    // in EmitInstallFaultHandler only covers the main thread; without a per-worker
    // altstack a fault on this M runs the SA_ONSTACK handler on a possibly-exhausted
    // stack and escalates to a fatal SIGILL. Does not clobber X28 (=P*).
    EmitInstallThreadSigaltstack();

    // Atomically increment active_workers (mirrors x86's LOCK INC on worker entry).
    // Balances the decrement at __sched_worker_loop_exit so the count stays accurate
    // across spawn cycles. The spawn-scan gate (active_workers >= max_procs) relies
    // on this.
    EmitGlobalLeaReg(ARM64Register.X9, "__sched_active_workers");
    EmitAtomicIncReg(ARM64Register.X9);

    // Update the active-worker high-water mark (PLAN Track-0 validation: "≥2
    // workers ran"). Monotonic, so a benign race here only ever loses a transient
    // sample; this worker's own increment is already visible in the reload. X9/X10
    // are dead here (the loop below reloads P from stack/TLS).
    EmitGlobalLoadReg(ARM64Register.X9, "__sched_active_workers");    // current count
    EmitGlobalLoadReg(ARM64Register.X10, "__sched_max_active_workers"); // current peak
    EmitCmpRegReg(ARM64Register.X9, ARM64Register.X10);
    EmitBranchCond(ARM64ConditionCode.Ls, "__sched_wloop_hiwater_done"); // cur <= peak -> skip
    EmitGlobalStoreReg(ARM64Register.X9, "__sched_max_active_workers");
    DefineLabel("__sched_wloop_hiwater_done");

    // --- Main worker loop ---
    DefineLabel("__sched_worker_loop_top");

    // Process pending waiter
    EmitBranchLink("__gt_process_pending_waiter");

    // Check timers
    EmitBranchLink("__gt_timer_check");

    // Check shutdown flag
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_shutdown_flag");
    EmitCbnz(ARM64Register.X0, "__sched_worker_loop_exit");

    // Try to dequeue a GT
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__sched_worker_park");

    // Got a GT — fall through to run_gt with X0 = GT.
    DefineLabel("__sched_worker_run_gt");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save GT to [x29+24]
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    // Load P* (clobbers X0)
    EmitLoadP(ARM64Register.X9);
    // P->currentGt = GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // reload GT
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffCurrentGt, 8);
    // Context switch: from = P->mainThread, to = GT, P* = X9
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, POffMainThread, isAdd: true); // X0 = from = &P->mainThread
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // X1 = to = GT
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");
    // Returned from context switch (GT completed or yielded)
    EmitBranch("__sched_worker_loop_top");

    // --- Park: no work available ---
    DefineLabel("__sched_worker_park");
    // Before parking, drive inline sync I/O. A worker GT that just parked handed off its sync
    // request (registered at the loop top by __gt_process_pending_sync_req); process it here
    // so the GT is re-enqueued and the re-dequeue below finds it. Without this, once every M
    // is idle nobody would drive the inline sync I/O and parked GTs would stall until the
    // 100ms timed-park re-drive. Only the sync completion queue is drained here — kqueue
    // (network) waiters cooperatively spin and drive __io_poll_kqueue themselves, so polling
    // kqueue from an idle worker here would just let another M process a spinning waiter's
    // EVFILT event via __io_poll_kqueue's (ungated) re-enqueue and double-schedule it.
    EmitBranchLink("__io_check_completions");

    // Publish idleFlag=1 with a store, then DMB ISH to close the missed-wakeup window:
    // an enqueuer that reads idleFlag after its queue-publish either sees 1 (and signals
    // our semaphore) or sees 0 with its queue-publish already globally visible, in which
    // case our re-dequeue below picks it up. The DMB pairs with the enqueuer-side DMB
    // between its queue-publish and its idleFlag-load in __gt_enqueue's wake scan.
    EmitLoadP(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffIdleFlag, 8);
    EmitDmbIsh();

    // Re-dequeue: any GT an enqueuer published before our idleFlag=1 retired is now visible.
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__sched_worker_really_park");

    // Work found during the race window — clear idleFlag and run it.
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save GT
    EmitLoadP(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffIdleFlag, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // reload GT
    EmitBranch("__sched_worker_run_gt");

    DefineLabel("__sched_worker_really_park");
    // Re-check shutdown before blocking: an enqueuer of __sched_shutdown_flag
    // that fired before our idleFlag store would skip our semaphore signal.
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_shutdown_flag");
    EmitCbnz(ARM64Register.X0, "__sched_worker_loop_exit");

    // Park on the wake lock block (Go semasleep) with a 100ms timed wait — the
    // missed-wakeup safety net: a timeout just loops back to re-check count. Load
    // the block pointer from P->wakeSemaphore first.
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X9, POffWakeSemaphore, 8);
    EmitSemaSleep(ARM64Register.X9, timed: true);
    // Clear idle flag
    EmitLoadP(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffIdleFlag, 8);
    EmitBranch("__sched_worker_loop_top");

    // --- Exit ---
    DefineLabel("__sched_worker_loop_exit");
    // Atomically decrement active_workers and mark this P stopped so the slot can be
    // re-spawned later (mirrors the x86 __sched_wloop_exit cleanup). A subsequent
    // __gt_enqueue spawn-scan finds status==0 and can reuse this P. Clearing idleFlag
    // too prevents a stale wake-scan from signalling a now-dead M.
    EmitGlobalLeaReg(ARM64Register.X9, "__sched_active_workers");
    EmitAtomicDecReg(ARM64Register.X9); // active_workers--
    EmitLoadP(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, PStatusUnused);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffStatus, 8);   // status = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffIdleFlag, 8);  // idleFlag = 0
    EmitMovRegImm(ARM64Register.X0, 0); // return NULL
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_spawn(func_ptr_x0, arg_count_x1, arg_buf_x2) -> promise in X0
  /// Allocates a GreenThread struct and a 64KB stack, initializes them,
  /// enqueues the thread, and returns the GreenThread ptr.
  /// </summary>
  private void EmitGtSpawn() {
    EmitRuntimeFunctionStart("__gt_spawn", 3, 0x50);
    // [x29+16] = func_ptr, [x29+24] = arg_count, [x29+32] = arg_buf
    // [x29+40] = gt_ptr (local), [x29+48] = stack_base (local)

    // Try to recycle a GT from P's free list before allocating
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, POffFreeListHead, 8);
    EmitCbz(ARM64Register.X0, "__gt_spawn_alloc_new");
    // Pop from free list: head = head->next, len--
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffNext, 8); // X1 = next
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, POffFreeListHead, 8); // head = next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, POffFreeListLen, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, POffFreeListLen, 8);
    EmitBranch("__gt_spawn_have_gt");

    DefineLabel("__gt_spawn_alloc_new");
    // Allocate GreenThread struct via mm_raw_alloc
    EmitMovRegImm(ARM64Register.X0, GtStructSize);
    EmitCallMmRawAlloc();

    DefineLabel("__gt_spawn_have_gt");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save gt_ptr

    // Allocate stack via mmap(NULL, GtInitialStackSize, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0).
    // mmap rounds up to a page, and macOS/arm64 pages are 16 KB, so the OS fault reserve folded into
    // GtInitialStackSize costs this target nothing at all.
    EmitMovRegImm(ARM64Register.X0, 0);                    // addr = NULL
    EmitMovRegImm(ARM64Register.X1, GtInitialStackSize);    // length (grows via __gt_morestack)
    EmitMovRegImm(ARM64Register.X2, 3);                     // prot = PROT_READ|PROT_WRITE
    EmitMovRegImm(ARM64Register.X3, 0x1002);                // flags = MAP_ANON|MAP_PRIVATE
    EmitMovRegImm(ARM64Register.X4, -1);                    // fd = -1
    EmitMovRegImm(ARM64Register.X5, 0);                     // offset = 0
    EmitCallImport("mmap");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save stack_base

    // Initialize GreenThread fields
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // X9 = gt_ptr

    // gt.stack_base = stack_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);

    // gt.stack_size = GtInitialStackSize
    EmitMovRegImm(ARM64Register.X0, GtInitialStackSize);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackSize, 8);

    // gt.stackguard = stack_base + GtStackGuardMargin
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, GtStackGuardMargin, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackGuard, 8);

    // gt.func_ptr = func_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // reload arg0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffFuncPtr, 8);

    // gt.arg_buf = arg_buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // reload arg2
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffArgBuf, 8);

    // gt.status = ready (0), gt.result = 0, gt.waiter = 0, gt.next = 0, gt.threw = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffWaiter, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffNext, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffThrew, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffCancelFlag, 8);
    // gt.ioYielded = 1 (safe to enqueue — no pending context switch on a new GT)
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);

    // Initialize stack: compute stack_top = stack_base + stack_size
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 48, 8); // stack_base
    EmitAddSubImm(ARM64Register.X10, ARM64Register.X10, GtInitialStackSize, isAdd: true); // X10 = stack_top

    // Context switch expects the stack to have callee-saved regs + FP/LR in this order:
    //   [SP+0]   X29, X30 (LR = __gt_trampoline)
    //   [SP+16]  D14, D15
    //   [SP+32]  D12, D13
    //   [SP+48]  D10, D11
    //   [SP+64]  D8,  D9
    //   [SP+80]  X27, X28
    //   [SP+96]  X25, X26
    //   [SP+112] X23, X24
    //   [SP+128] X21, X22
    //   [SP+144] X19, X20
    // Total = 160 bytes. SP = stack_top - 160.

    // Zero all 160 bytes at top of stack
    EmitAddSubImm(ARM64Register.X11, ARM64Register.X10, 160, isAdd: false); // X11 = stack_top - 160
    EmitMovRegImm(ARM64Register.X0, 0);
    for (int i = 0; i < 20; i++) {
      // STR XZR, [X11, #i*8]
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.Xzr, ARM64Register.X11, i * 8, 8);
    }

    // Store __gt_trampoline address at [stack_top - 160 + 8] (the X30/LR slot)
    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, "__gt_trampoline");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X11, 8, 8); // LR slot

    // gt.sp = stack_top - 160
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // reload gt_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X9, GtOffSp, 8);

    // gt.fp = 0 (no frame pointer yet)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffFp, 8);

    // Add to all-threads list + bump live_count UNDER __sched_all_lock. These two globals
    // (__gt_all_head, __gt_live_count) are shared across every worker M: a completing GT's
    // trampoline concurrently unlinks itself + decrements live_count on another M. Without
    // the lock the list push (read-head / store-all_next / store-head) and the read-add-store
    // counter tear, corrupting the all-list — which recycles a still-live GT struct and
    // surfaces as garbage await results / SIGILL. Mirrors the self-hosted __sched_all_cs.
    // The lock call clobbers caller-saved regs, so reload gt from its frame slot ([x29+40]).
    EmitLockAcquire("__sched_all_lock");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // reload gt_ptr
    // Add to all-threads list: gt.all_next = __gt_all_head; __gt_all_head = gt
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_all_head");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffAllNext, 8);
    EmitGlobalStoreReg(ARM64Register.X9, "__gt_all_head");

    // Increment live thread count
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_live_count");
    EmitLockRelease("__sched_all_lock");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // reload gt_ptr

    // Enqueue: __gt_enqueue(gt)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("__gt_enqueue");

    // Return gt_ptr as the promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      // Save gt_ptr
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
      // Assign trace ID: counter++
      EmitGlobalLoadReg(ARM64Register.X1, "__gt_trace_counter");
      EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
      EmitGlobalStoreReg(ARM64Register.X1, "__gt_trace_counter");
      // Store trace ID in gt
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // gt_ptr
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, GtOffTraceId, 8);
      // Trace: "spawn #N\n"
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_spawn");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
      // Restore gt_ptr
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    }
    EmitTraceRelease();

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_trampoline(): Entry point for new green threads.
  /// Entered via context switch RET. Loads target function + args, calls it,
  /// stores result, and yields.
  /// </summary>
  private void EmitGtTrampoline() {
    DefineLabel("__gt_trampoline");
    // No standard prologue — we are entered via context switch LDP/RET
    // Set up a frame for local use
    // STP x29, x30, [sp, #-0x70]!
    var frameSize = 0x90; // 144 bytes (adds slots: managed_mask@112, result@120, threw@128)
    var imm7 = unchecked((uint)(-frameSize / 8)) & 0x7Fu;
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Load current GreenThread via TLS
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X29, 16, 8); // [x29+16] = gt

    // Load func_ptr and arg_buf from GreenThread
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffFuncPtr, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X29, 24, 8); // [x29+24] = func_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffArgBuf, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X29, 32, 8); // [x29+32] = arg_buf

    // Load arg count from [arg_buf + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X10, 0, 8); // X11 = arg_count
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 40, 8); // [x29+40] = arg_count

    // Load managed_mask from [arg_buf + 8] now, before arg_buf is freed below;
    // the post-call decref loop walks it to drop the spawn-time increfs.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 32, 8); // X10 = arg_buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X10, 8, 8);   // X11 = managed_mask
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 112, 8); // [x29+112] = managed_mask

    // Load args from buffer into AAPCS64 calling convention registers (X0-X7).
    // Args are at [arg_buf + 16 + i*8] — count at +0, managed_mask at +8, args
    // start at +16. See LowerAsyncCall (Compiler/MLIR/Conversion) for the
    // matching producer-side layout and the spawn-site incref that this
    // trampoline's mm_decref-by-mask (the loop after the call below) drops once
    // the function returns.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X29, 40, 8); // X11 = arg_count
    for (int i = 0; i < 8; i++) {
      var skipLabel = $"__gt_tramp_skip_arg{i}";
      // if arg_count <= i, skip
      EmitCmpImm(ARM64Register.X11, i + 1);
      EmitBranchCond(ARM64ConditionCode.Lt, skipLabel); // skip if arg_count < i+1
      // Load arg[i] from [arg_buf + 16 + i*8]
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 32, 8); // X10 = arg_buf
      EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[i], ARM64Register.X10, 16 + i * 8, 8);
      DefineLabel(skipLabel);
    }

    // Free arg_buf via mm_raw_free before calling target
    // Save loaded args on stack first
    for (int i = 0; i < 8; i++) {
      EmitLoadStoreUnsignedImm(0xF9000000, AbiArgRegs[i], ARM64Register.X29, 48 + i * 8, 8);
    }
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = arg_buf
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    // Restore args
    for (int i = 0; i < 8; i++) {
      EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[i], ARM64Register.X29, 48 + i * 8, 8);
    }

    // Call target function
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 24, 8); // X9 = func_ptr
    // BLR X9
    EmitWord(0xD63F0120);

    // Save result (X0) + threw (X1) to dedicated slots: mm_decref below clobbers
    // X0/X1, and the decref loop reads the saved-arg slots [x29+48..104], so
    // result/threw must not reuse those (they aliased args 0/1 pre-fix).
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 120, 8); // [x29+120] = result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 128, 8); // [x29+128] = threw

    // Drop the spawn-time incref on managed args (mirrors the x64 trampoline's
    // managed-mask decref loop). For each set bit in managed_mask, mm_decref the
    // matching saved-arg slot, skipping NULL (mm_decref panics on NULL). Without
    // this the spawn-site incref in LowerAsyncCall leaks the arg — the per-worker
    // drain-Promise leak that tripped the parent's exit leak gate.
    for (int i = 0; i < 8; i++) {
      var decrefSkip = $"__gt_tramp_decref_skip_arg{i}";
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 112, 8); // X1 = managed_mask
      EmitMovRegImm(ARM64Register.X2, 1L << i);
      EmitAluRegReg(0x8A000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X2);   // AND X1, X1, X2 (test bit i)
      EmitCbz(ARM64Register.X1, decrefSkip);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48 + i * 8, 8); // X0 = saved arg i
      EmitCbz(ARM64Register.X0, decrefSkip);
      EmitBranchLink("mm_decref", zeroSecondArg: Compiler.MmTrace);
      DefineLabel(decrefSkip);
    }

    // Store result + threw to the gt struct.
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 120, 8); // result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 128, 8); // threw
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffThrew, 8);

    // Decrement live_count + unlink from the all-threads list UNDER __sched_all_lock —
    // pairs with the spawn-side push/increment on other worker Ms (see EmitGtSpawn). The
    // lock call clobbers caller-saved regs, so reload current GT after acquiring it.
    EmitLockAcquire("__sched_all_lock");

    // Decrement live thread count
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: false);
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_live_count");

    // Remove from all-threads list
    // Walk: prev=NULL(X2), cur=__gt_all_head(X1); find cur == gt(X9) and unlink
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitGlobalLoadReg(ARM64Register.X1, "__gt_all_head"); // X1 = cur
    EmitMovRegImm(ARM64Register.X2, 0);                   // X2 = prev = NULL
    DefineLabel("__gt_tramp_alllist_loop");
    EmitCbz(ARM64Register.X1, "__gt_tramp_alllist_done"); // not found
    EmitCmpRegReg(ARM64Register.X1, ARM64Register.X9);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_tramp_alllist_found");
    // prev = cur; cur = cur->all_next
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, GtOffAllNext, 8);
    EmitBranch("__gt_tramp_alllist_loop");

    DefineLabel("__gt_tramp_alllist_found");
    // cur == X9; unlink: if prev==NULL: head = cur->all_next; else prev->all_next = cur->all_next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffAllNext, 8); // X0 = cur->all_next
    EmitCbnz(ARM64Register.X2, "__gt_tramp_alllist_prev");
    // prev == NULL: update head
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_all_head");
    EmitBranch("__gt_tramp_alllist_done");
    DefineLabel("__gt_tramp_alllist_prev");
    // prev->all_next = cur->all_next
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X2, GtOffAllNext, 8);
    DefineLabel("__gt_tramp_alllist_done");
    EmitLockRelease("__sched_all_lock");

    // Reload current gt
    EmitLoadCurrentGt(ARM64Register.X9);

    // Reset ioYielded=0 BEFORE publishing status=completed (ordered by the store-store
    // barrier below). An awaiter that observes status=completed then blocks on
    // ioYielded==1 before munmapping this GT's stack; ioYielded flips to 1 only inside
    // the final __gt_context_switch below, after our register context is saved and we
    // leave the stack. Without this reset a stale ioYielded=1 left by an earlier I/O
    // yield would let the awaiter free the stack while we still run on it — the crash in
    // __io_poll_kqueue's epilogue at shutdown. Mirrors the ioYielded clear in
    // __gt_await / __io_submit_sync.
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);
    EmitDmbIsh();

    // Set status = completed
    EmitMovRegImm(ARM64Register.X0, GtStatusCompleted);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // Dekker StoreLoad barrier: order `gt.status = completed` (above) before the waiter
    // LOAD (below). This is the completer half of a Dekker handshake with __gt_await; on
    // arm64's weak memory both our status store and the awaiter's waiter store can stay
    // buffered, so without the fence the completer reads waiter==0 while the awaiter reads
    // status!=completed — the lost-wakeup double-miss that strands the awaited GT.
    EmitDmbIsh();

    // Defer the waiter into P.pendingWaiter — do NOT enqueue it here. We are still running
    // on this completed GT's stack; enqueueing now would let another worker M dequeue and
    // resume the waiter (restoring an unsaved register block / freeing this stack) before we
    // leave it. __gt_process_pending_waiter (run by the scheduler we switch to below, off
    // this stack) re-enqueues a real GT waiter under the ioYielded gate; a main-thread waiter
    // (stackBase==0) is skipped there and rechecks promise.status in its own await loop.
    // Mirrors the self-hosted emitArm64GtTrampoline.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffWaiter, 8); // X10 = gt.waiter
    EmitLoadP(ARM64Register.X1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X1, POffPendingWaiter, 8);

    // context_switch(from = gt, to = &P.mainThread, p = P) — return to this M's scheduler.
    // For a worker M that scheduler is __sched_worker_loop; for the main OS thread (P[0]) it
    // is main() itself, which resumes inside its await recheck loop. Never returns (this GT
    // is completed and is never switched back in).
    EmitLoadCurrentGt(ARM64Register.X0); // from = gt
    EmitLoadP(ARM64Register.X9);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, POffMainThread, isAdd: true); // to = &P.mainThread
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // p = P
    EmitBranchLink("__gt_context_switch");
    // Should never reach here
    EmitWord(0xD4200000); // BRK #0
  }

  /// <summary>
  /// __gt_context_switch(from_x0, to_x1, p_x2): Core context switch.
  /// Saves callee-saved registers on 'from', restores from 'to'.
  /// Updates P->currentGt (via X2) and sets X28 = P* for the new thread.
  /// X2 = P* (ProcContext pointer), must be passed by all call sites.
  /// </summary>
  private void EmitGtContextSwitch() {
    DefineLabel("__gt_context_switch");
    // No standard prologue — this is a naked function

    // Save callee-saved registers on current stack (push order)
    // We save in this order so LDP restores in reverse:
    EmitStpPreIndex(ARM64Register.X19, ARM64Register.X20);
    EmitStpPreIndex(ARM64Register.X21, ARM64Register.X22);
    EmitStpPreIndex(ARM64Register.X23, ARM64Register.X24);
    EmitStpPreIndex(ARM64Register.X25, ARM64Register.X26);
    EmitStpPreIndex(ARM64Register.X27, ARM64Register.X28);
    EmitStpFpPreIndex(ARM64FloatRegister.D8, ARM64FloatRegister.D9);
    EmitStpFpPreIndex(ARM64FloatRegister.D10, ARM64FloatRegister.D11);
    EmitStpFpPreIndex(ARM64FloatRegister.D12, ARM64FloatRegister.D13);
    EmitStpFpPreIndex(ARM64FloatRegister.D14, ARM64FloatRegister.D15);
    EmitStpPreIndex(ARM64Register.X29, ARM64Register.X30);

    // Save SP to from.sp: MOV X9, SP; STR X9, [X0, #GtOffSp]
    EmitMovRegReg(ARM64Register.X9, ARM64Register.Sp);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffSp, 8);

    // from-GT's context is now fully saved. Set ioYielded=1 to signal that it's
    // safe for a completer on another thread (__netpoll_claim_done's post-claim spin, __io_op_done,
    // __gt_process_pending_waiter) to enqueue this GT.
    //
    // StoreStore barrier FIRST. Everything that makes the GT resumable — the callee-saved
    // register block pushed above and the from.sp store — must be GLOBALLY VISIBLE before the
    // flag that advertises it. arm64 is weakly ordered, so without this fence another M can
    // observe ioYielded==1 while from.sp is still the value saved at the PREVIOUS suspension,
    // enqueue the GT on that stale evidence, and resume it onto a stack it no longer owns.
    // Every reader of this flag (__gt_timer_check's park gate, __gt_process_pending_waiter,
    // __io_op_done, EmitAwaitedStackVacatedGate) already fences on the acquire side; this is
    // the release half that was missing.
    EmitDmbIsh();
    EmitMovRegImm(ARM64Register.X9, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffIoYielded, 8);

    // to-GT is about to run: clear its ioYielded so the flag means exactly "suspended
    // off-stack (safe to enqueue)" and never lingers stale-1 on a running GT. Without this,
    // a GT that switched off once (ioYielded=1) and was later resumed keeps ioYielded=1 while
    // running; the ioYielded==1 gates (__io_op_done / __gt_process_pending_waiter /
    // __netpoll_claim_done's post-claim spin) would then enqueue a still-running GT and a second M
    // would resume it onto the same stack — the double-schedule.
    EmitMovRegImm(ARM64Register.X9, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X1, GtOffIoYielded, 8);

    // Restore SP from to.sp: LDR X9, [X1, #GtOffSp]; MOV SP, X9
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X1, GtOffSp, 8);
    EmitMovRegReg(ARM64Register.Sp, ARM64Register.X9);

    // Update P->currentGt = to (X2 = P*)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X2, POffCurrentGt, 8);

    // Restore callee-saved registers from new stack (reverse order)
    EmitLdpPostIndex(ARM64Register.X29, ARM64Register.X30);
    EmitLdpFpPostIndex(ARM64FloatRegister.D14, ARM64FloatRegister.D15);
    EmitLdpFpPostIndex(ARM64FloatRegister.D12, ARM64FloatRegister.D13);
    EmitLdpFpPostIndex(ARM64FloatRegister.D10, ARM64FloatRegister.D11);
    EmitLdpFpPostIndex(ARM64FloatRegister.D8, ARM64FloatRegister.D9);
    EmitLdpPostIndex(ARM64Register.X27, ARM64Register.X28);
    EmitLdpPostIndex(ARM64Register.X25, ARM64Register.X26);
    EmitLdpPostIndex(ARM64Register.X23, ARM64Register.X24);
    EmitLdpPostIndex(ARM64Register.X21, ARM64Register.X22);
    EmitLdpPostIndex(ARM64Register.X19, ARM64Register.X20);

    // Set X28 = P* for the new thread (overrides restored X28)
    EmitMovRegReg(ARM64Register.X28, ARM64Register.X2);

    // RET (returns to new thread's saved LR)
    EmitWord(0xD65F03C0);
  }

  /// <summary>
  /// Block until the awaited (completed) GT has switched off its own stack before the
  /// awaiter munmaps that stack. The completion trampoline resets ioYielded=0 before
  /// publishing status=completed and the final __gt_context_switch sets ioYielded=1 only
  /// after saving the GT's context and leaving its stack — so ioYielded==1 here means
  /// "off-stack, safe to free". Without this gate a worker M can still be running on the
  /// stack (e.g. driving I/O in __io_poll_kqueue) when the awaiter unmaps it, faulting on
  /// the next stack access. gtReg holds the awaited GT and is preserved; X3 is scratch;
  /// labelBase must be unique per call site. Mirrors the __gt_ppw_spin ioYielded gate.
  /// </summary>
  private void EmitAwaitedStackVacatedGate(ARM64Register gtReg, string labelBase) {
    EmitDmbIsh(); // order the earlier status==completed load before the ioYielded load
    DefineLabel(labelBase + "_spin");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, gtReg, GtOffIoYielded, 8);
    EmitCbnz(ARM64Register.X3, labelBase + "_ready");
    EmitWord(0xD503203F); // YIELD
    EmitBranch(labelBase + "_spin");
    DefineLabel(labelBase + "_ready");
    EmitDmbIsh();
  }

  /// <summary>
  /// __gt_await(promise_x0) -> result in X0
  /// If the promise is already completed, extract result and return.
  /// Otherwise, set current to waiting, set promise.waiter = current, switch to next.
  /// </summary>
  private void EmitGtAwait() {
    EmitRuntimeFunctionStart("__gt_await", 1, 0x40);
    // [x29+16] = promise (arg 0)

    if (Compiler.AsyncTrace) {
      // [x29+32] = yield flag (0=immediate, 1=yielded)
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    }

    // Check if promise is already completed
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_await_done");

    if (Compiler.AsyncTrace) {
      EmitMovRegImm(ARM64Register.X0, 1);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    }

    // Not yet completed: block current thread
    EmitLoadCurrentGt(ARM64Register.X9);
    // current.status = waiting
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    // Clear ioYielded = 0 BEFORE publishing promise.waiter, so the completing GT's
    // trampoline gate (__gt_tramp_waiter_spin) blocks until __gt_context_switch has saved
    // our registers and set ioYielded=1 again. Without this the completer could enqueue us
    // while our context is still in-flight on this M's stack. Mirrors __io_submit_sync.
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);

    // promise.waiter = current
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffWaiter, 8);
    // Dekker StoreLoad barrier: order `promise.waiter = current` before any subsequent
    // recheck/observation of promise.status, pairing with the trampoline's barrier so the
    // completer cannot read waiter==0 while we'd read status!=completed (lost wakeup).
    EmitDmbIsh();

    // Recheck loop: run other runnable GTs and, after each (and when idle), recheck
    // promise.status. The completing GT defers us into P.pendingWaiter and switches back to
    // its scheduler (off its dying stack). A worker-GT awaiter is re-enqueued under the
    // ioYielded gate (so we resume via the context switch below and recheck); the main OS
    // thread (stackBase==0) is NEVER enqueued and instead drives its own progress by polling
    // promise.status here. Mirrors the self-hosted emitArm64GtAwait recheck loop.
    DefineLabel("__gt_await_loop");
    EmitDriveSchedulerAndIo();
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__gt_await_idle");

    // Run the dequeued GT: save it, status=running, context_switch(from=current, to=g).
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0); // from = current
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // to = next
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");
    // Resumed: the GT we ran switched back to us. Mark it ioYielded=1 (off-stack signal).
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // next
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffIoYielded, 8);
    EmitBranch("__gt_await_recheck");

    // Idle: nothing runnable on this M. A worker-GT awaiter (stackBase!=0) yields its M back
    // to the scheduler (P.mainThread) — __gt_context_switch saves our context and sets
    // ioYielded=1, releasing the process_pending_waiter gate so a worker can re-enqueue us
    // once our awaited child completes; we resume here and recheck. The main OS thread
    // (stackBase==0) has no scheduler GT to switch to, so it briefly sleeps and rechecks.
    DefineLabel("__gt_await_idle");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, "__gt_await_main_park");
    // Worker GT: switch back to this M's scheduler loop.
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9); // from = self
    EmitLoadP(ARM64Register.X9);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, POffMainThread, isAdd: true); // to = &P.mainThread
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // P*
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__gt_await_recheck");

    // Main OS thread: nanosleep(200us) then recheck (parked, not core-burning).
    DefineLabel("__gt_await_main_park");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 200000); // 200us in ns
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // tv_nsec
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 40, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("nanosleep");
    EmitBranch("__gt_await_recheck");

    // Recheck: if promise still not completed, loop; else fall into the extract path.
    DefineLabel("__gt_await_recheck");
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Ne, "__gt_await_loop");

    // Extract result
    DefineLabel("__gt_await_done");

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      // Trace output
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await");
      EmitBranchLink("mm_trace_print_tag");
      EmitReloadArg(0); // promise
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
      EmitCbnz(ARM64Register.X0, "__gt_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_imm");
      EmitBranch("__gt_await_trace_print");
      DefineLabel("__gt_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_yield");
      DefineLabel("__gt_await_trace_print");
      EmitBranchLink("mm_trace_print_tag");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    // Extract result from promise
    EmitReloadArg(0); // X0 = promise
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);

    // Free the green thread's stack via munmap
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X1, "__gt_await_skip_free_stack");
    // Wait for the completed GT to leave its stack before unmapping it (X9 = awaited GT).
    EmitAwaitedStackVacatedGate(ARM64Register.X9, "__gt_await_vacate");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackBase, 8); // reload stack_base (gate preserves X9)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    // munmap(stack_base, stack_size)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1); // X0 = stack_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackSize, 8); // X1 = stack_size
    EmitCallImport("munmap");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result

    DefineLabel("__gt_await_skip_free_stack");
    // Recycle GT to free list or free it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    EmitLoadP(ARM64Register.X10);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X10, POffFreeListLen, 8);
    EmitCmpImm(ARM64Register.X1, MaxFreeListLen);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_await_free_gt");
    // Prepend to free list
    EmitReloadArg(0); // X0 = gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X10, POffFreeListHead, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, GtOffNext, 8); // gt->next = old head
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, POffFreeListHead, 8); // head = gt
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X10, POffFreeListLen, 8);
    EmitBranch("__gt_await_recycle_done");
    DefineLabel("__gt_await_free_gt");
    EmitReloadArg(0); // X0 = promise (gt struct ptr)
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel("__gt_await_recycle_done");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_is_complete(promise_x0) -> 1 if the GT has reached completed status,
  /// 0 otherwise. Non-blocking peek used by the spec-test dispatcher to find the
  /// first-ready promise out of N concurrent drains without head-of-line
  /// blocking on a slow worker. Mirrors x86 EmitGtIsComplete.
  /// </summary>
  private void EmitGtIsComplete() {
    EmitRuntimeFunctionStart("__gt_is_complete", 1, 0x20);
    // [x29+16] = promise (arg 0)
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8); // X1 = promise.status
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_is_complete_yes");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__gt_is_complete_done");
    DefineLabel("__gt_is_complete_yes");
    EmitMovRegImm(ARM64Register.X0, 1);
    DefineLabel("__gt_is_complete_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_try_await(promise_x0) -> result in X0, threw flag in X1
  /// Like __gt_await but also returns the threw flag.
  /// </summary>
  private void EmitGtTryAwait() {
    EmitRuntimeFunctionStart("__gt_try_await", 1, 0x50);
    // [x29+16] = promise (arg 0)

    if (Compiler.AsyncTrace) {
      // [x29+40] = yield flag
      EmitMovRegImm(ARM64Register.X0, 0);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    }

    // Check if promise is already completed
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_try_await_done");

    if (Compiler.AsyncTrace) {
      EmitMovRegImm(ARM64Register.X0, 1);
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    }

    // Not yet completed: block current thread (recheck-loop design — see EmitGtAwait).
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    // Clear ioYielded=0 before publishing the waiter (process_pending_waiter gate handshake).
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);

    // promise.waiter = current
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffWaiter, 8);
    EmitDmbIsh(); // Dekker StoreLoad barrier (see EmitGtAwait)

    // Recheck loop: run other GTs and recheck promise.status. A worker-GT awaiter yields its
    // M to the scheduler; the main OS thread (stackBase==0) polls. See EmitGtAwait.
    DefineLabel("__gt_try_await_loop");
    EmitDriveSchedulerAndIo();
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__gt_try_await_idle");
    // Run the dequeued GT.
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0); // from = current
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // to = next
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9);
    EmitBranchLink("__gt_context_switch");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // next
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffIoYielded, 8);
    EmitBranch("__gt_try_await_recheck");

    DefineLabel("__gt_try_await_idle");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, "__gt_try_await_main_park");
    // Worker GT: yield M back to the scheduler.
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9); // from = self
    EmitLoadP(ARM64Register.X9);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, POffMainThread, isAdd: true); // to = &P.mainThread
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9);
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__gt_try_await_recheck");

    // Main OS thread: nanosleep(200us) then recheck.
    DefineLabel("__gt_try_await_main_park");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 200000); // 200us in ns
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // tv_nsec
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 48, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("nanosleep");
    EmitBranch("__gt_try_await_recheck");

    DefineLabel("__gt_try_await_recheck");
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Ne, "__gt_try_await_loop");

    // Extract result + threw flag
    DefineLabel("__gt_try_await_done");

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_try_await");
      EmitBranchLink("mm_trace_print_tag");
      EmitReloadArg(0);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
      EmitCbnz(ARM64Register.X0, "__gt_try_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_imm");
      EmitBranch("__gt_try_await_trace_print");
      DefineLabel("__gt_try_await_trace_yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_await_yield");
      DefineLabel("__gt_try_await_trace_print");
      EmitBranchLink("mm_trace_print_tag");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    // Extract result and threw flag from promise
    EmitReloadArg(0); // X0 = promise
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffThrew, 8);

    // Free the green thread's stack
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X2, "__gt_try_await_skip_free_stack");
    // Wait for the completed GT to leave its stack before unmapping it (X9 = awaited GT).
    EmitAwaitedStackVacatedGate(ARM64Register.X9, "__gt_try_await_vacate");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X9, GtOffStackBase, 8); // reload stack_base (gate preserves X9)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // save threw
    // munmap(stack_base, stack_size)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackSize, 8);
    EmitCallImport("munmap");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // restore threw

    DefineLabel("__gt_try_await_skip_free_stack");
    // Recycle GT to free list or free it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // save threw
    EmitLoadP(ARM64Register.X10);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X10, POffFreeListLen, 8);
    EmitCmpImm(ARM64Register.X2, MaxFreeListLen);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_try_await_free_gt");
    // Prepend to free list
    EmitReloadArg(0); // X0 = gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X10, POffFreeListHead, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X0, GtOffNext, 8); // gt->next = old head
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, POffFreeListHead, 8); // head = gt
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X10, POffFreeListLen, 8);
    EmitBranch("__gt_try_await_recycle_done");
    DefineLabel("__gt_try_await_free_gt");
    EmitReloadArg(0); // X0 = promise
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel("__gt_try_await_recycle_done");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // restore threw

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_yield_completed / __gt_yield: Yield current thread.
  /// __gt_yield_completed: for completed threads (don't enqueue self).
  /// </summary>
  private void EmitGtYield() {
    DefineLabel("__gt_yield_completed");
    // Set up a frame
    var imm7 = unchecked((uint)(-0x30 / 8)) & 0x7Fu;
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u); // STP x29, x30, [sp, #-0x30]!
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Try to dequeue next runnable thread
    DefineLabel("__gt_yield_completed_spin");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__gt_yield_has_next");

    // No more threads runnable — check if there are live threads with pending timers/IO
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitCbz(ARM64Register.X0, "__gt_yield_switch_main"); // no live threads, go to main

    // Live threads exist but nobody runnable — process pending I/O, timers, brief park, then retry.
    // __io_poll_kqueue is essential: a completed GT spinning here may be the only context
    // left to drain kqueue, and sibling GTs parked in __io_submit_read (e.g. streaming
    // subprocess line reads) only wake once their EVFILT_READ event is polled. Every other
    // scheduler idle-spin (await, sleep, io_submit) polls kqueue here too.
    EmitDriveSchedulerAndIo();
    // Brief nanosleep(1ms) to avoid burning CPU
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 1000000); // 1ms
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // tv_nsec = 1ms
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 16, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("nanosleep");
    EmitBranch("__gt_yield_completed_spin");

    // No live threads: switch back to main thread
    DefineLabel("__gt_yield_switch_main");
    // Load P->mainThread address into X1
    EmitLoadP(ARM64Register.X9);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, POffMainThread, isAdd: true);
    // If current IS the main thread, just return
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_yield_return");
    // Switch to main
    EmitMovRegImm(ARM64Register.X2, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X1, GtOffStatus, 8);
    // from=current(X0), to=main(X1)
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__gt_yield_return");

    DefineLabel("__gt_yield_has_next");
    // next.status = running
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // context switch: from=current, to=next
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

    DefineLabel("__gt_yield_return");
    // Epilogue
    EmitMovRegReg(ARM64Register.Sp, ARM64Register.X29);
    var imm7Post = (0x30u / 8) & 0x7Fu;
    EmitWord(0xA8C00000 | (imm7Post << 15) | (30u << 10) | (31u << 5) | 29u); // LDP x29, x30, [sp], #0x30
    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// __gt_cancel(gt_x0): Request cancellation of a green thread.
  /// Sets cancel_flag=1. No CancelIoEx on macOS.
  /// </summary>
  private void EmitGtCancel() {
    EmitRuntimeFunctionStart("__gt_cancel", 1, 0x30);

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save gt ptr
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_cancel");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // gt ptr
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    // X0 = gt (reload from arg)
    EmitReloadArg(0);
    // gt->cancel_flag = 1
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffCancelFlag, 8);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_cleanup(): Called from mrt_start after main returns.
  /// Cancels all live green threads, then drains the run queue.
  /// </summary>
  private void EmitGtCleanup() {
    EmitRuntimeFunctionStart("__gt_cleanup", 0, 0x30);

    // --- Step 0: Set shutdown flag and wake I/O worker so it exits ---
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_shutdown_flag");
    // Wake the I/O worker (semawakeup) so it checks shutdown and exits
    EmitGlobalLoadReg(ARM64Register.X9, "__io_sync_req_semaphore");
    EmitSemaWakeup(ARM64Register.X9);

    // --- Step 1: Cancel all live threads ---
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_all_head");
    DefineLabel("__gt_cleanup_cancel_loop");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_drain");
    // Save current gt and next across call
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffAllNext, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // save next
    // __gt_cancel(X0=gt) -- X0 already set
    EmitBranchLink("__gt_cancel");
    // Advance to next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranch("__gt_cleanup_cancel_loop");

    // --- Step 2: Drain run queue ---
    DefineLabel("__gt_cleanup_drain");
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_check_live");

    // Run the thread: set status=running, context switch to it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");
    // Resume here when thread completes/yields back
    // Signal that context switch is complete so a completer can safely enqueue
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = gt
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffIoYielded, 8);

    // If the GT completed, free its stack and return struct to free list
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X1, GtStatusCompleted);
    EmitBranchCond(ARM64ConditionCode.Ne, "__gt_cleanup_drain"); // not completed — just loop

    // Free stack via munmap(stack_base, stack_size)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X1, "__gt_cleanup_drain_skip_stack");
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1); // X0 = stack_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // reload gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, GtOffStackSize, 8); // X1 = stack_size
    EmitCallImport("munmap");
    DefineLabel("__gt_cleanup_drain_skip_stack");

    // Return GT struct to free list or free it
    EmitLoadP(ARM64Register.X10);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X10, POffFreeListLen, 8);
    EmitCmpImm(ARM64Register.X1, MaxFreeListLen);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_cleanup_drain_free_gt");
    // Prepend to free list
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X10, POffFreeListHead, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, GtOffNext, 8); // gt->next = old head
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, POffFreeListHead, 8); // head = gt
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X10, POffFreeListLen, 8);
    EmitBranch("__gt_cleanup_drain");
    DefineLabel("__gt_cleanup_drain_free_gt");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = gt
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitBranch("__gt_cleanup_drain");

    // Run queue empty — check if any threads still alive
    DefineLabel("__gt_cleanup_check_live");
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_done");
    // Threads still alive but nothing runnable — process pending I/O and timers
    EmitDriveSchedulerAndIo();
    // Brief nanosleep to avoid burning CPU
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 1000000); // 1ms
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // tv_nsec = 1ms
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 16, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("nanosleep");
    EmitBranch("__gt_cleanup_drain");

    DefineLabel("__gt_cleanup_done");
    // Wake all worker threads (semawakeup) so they exit (shutdown flag is set).
    // EmitSemaWakeup clobbers X0..X18, so the loop index is homed in [x29+24]
    // and procs/num_procs are reloaded from their (immutable here) globals each pass.
    EmitMovRegImm(ARM64Register.X12, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X12, ARM64Register.X29, 24, 8); // i = 1
    DefineLabel("__gt_cleanup_wake_loop");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X12, ARM64Register.X29, 24, 8); // i
    EmitGlobalLoadReg(ARM64Register.X11, "__sched_num_procs");
    EmitCmpRegReg(ARM64Register.X12, ARM64Register.X11);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_cleanup_drain_free_lists");
    EmitGlobalLoadReg(ARM64Register.X10, "__sched_procs");
    // LDR X13, [X10, X12, LSL #3]
    EmitWord(0xF86C794D);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X13, POffWakeSemaphore, 8);
    EmitCbz(ARM64Register.X9, "__gt_cleanup_wake_next");
    EmitSemaWakeup(ARM64Register.X9);
    DefineLabel("__gt_cleanup_wake_next");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X12, ARM64Register.X29, 24, 8); // i
    EmitAddSubImm(ARM64Register.X12, ARM64Register.X12, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X12, ARM64Register.X29, 24, 8); // i++
    EmitBranch("__gt_cleanup_wake_loop");

    // --- Drain free lists on all P structs ---
    DefineLabel("__gt_cleanup_drain_free_lists");
    // Use [X29+16] = next gt, [X29+24] = loop index i
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // i = 0
    DefineLabel("__gt_cleanup_drain_p_loop");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X12, ARM64Register.X29, 24, 8); // X12 = i
    EmitGlobalLoadReg(ARM64Register.X11, "__sched_num_procs");
    // CMP X12, X11
    EmitWord(0xEB0B019F);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_cleanup_ret");
    // Load P[i]
    EmitGlobalLoadReg(ARM64Register.X10, "__sched_procs");
    // LDR X13, [X10, X12, LSL #3] = P[i]
    EmitWord(0xF86C794D);
    // Load P[i]->freeListHead
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X13, POffFreeListHead, 8);

    DefineLabel("__gt_cleanup_drain_gt_loop");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_drain_p_next");
    // Save gt->next before freeing
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffNext, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 16, 8); // save next
    // mm_raw_free(X0=gt)
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    // Advance to next (reload from stack — call clobbered registers)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitBranch("__gt_cleanup_drain_gt_loop");

    DefineLabel("__gt_cleanup_drain_p_next");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X12, ARM64Register.X29, 24, 8); // X12 = i
    EmitAddSubImm(ARM64Register.X12, ARM64Register.X12, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X12, ARM64Register.X29, 24, 8); // save i
    EmitBranch("__gt_cleanup_drain_p_loop");

    DefineLabel("__gt_cleanup_ret");
    EmitRuntimeFunctionEnd();
  }

  // =====================================================================
  // Async I/O Runtime — deferred execution model for macOS
  // =====================================================================

  /// <summary>
  /// Emits all async I/O runtime functions.
  /// On macOS, I/O is processed inline by the scheduler (no OS worker threads).
  /// Green threads submit I/O requests and yield; the scheduler executes pending
  /// I/O when no threads are runnable, then re-enqueues the waiters.
  /// </summary>
  private void EmitIoRuntime() {
    EmitIoInit();
    EmitIoShutdown();
    EmitIoEnqueueSyncReq();
    EmitIoDequeueSyncReq();
    EmitIoEnqueueCompletion();
    EmitIoDequeueCompletion();
    EmitIoGetLastError();
    EmitIoPollKqueue();
    EmitIoCheckCompletions();
    EmitIoSyncWorkerLoop();
    EmitIoSubmitSync();
    EmitIoSubmitRead();
    EmitIoSubmitWrite();
    EmitNetParseOctet();
    // Only emit DNS resolver (with dylib imports) if the program uses networking.
    // Check if maxon_net_tcp_connect is referenced by looking for it in branch fixups.
    if (_branchFixups.Any(f => f.target == "maxon_net_tcp_connect")) {
      EmitDnsCallback();
      EmitNetResolveHost();
    } else {
      EmitNetResolveHostIpOnly();
    }
  }

  /// <summary>
  /// __io_enqueue_sync_req(req_x0): Append a SyncRequest to the request queue.
  /// </summary>
  private void EmitIoEnqueueSyncReq() {
    EmitRuntimeFunctionStart("__io_enqueue_sync_req", 1, 0x30);

    // req.next = 0 (safe outside lock, req is private to caller)
    EmitReloadArg(0);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);

    // Acquire lock to protect sync request queue
    EmitLockAcquire("__sched_io_lock");

    // if tail == NULL: head = tail = req
    EmitGlobalLoadReg(ARM64Register.X1, "__io_sync_req_tail");
    EmitCbnz(ARM64Register.X1, "__io_enqueue_sync_append");

    EmitReloadArg(0);
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_head");
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_tail");
    EmitLockRelease("__sched_io_lock");
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_enqueue_sync_append");
    EmitGlobalLoadReg(ARM64Register.X1, "__io_sync_req_tail");
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, SyncReqOffNext, 8); // tail.next = req
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_tail"); // tail = req
    EmitLockRelease("__sched_io_lock");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_dequeue_sync_req() -> SyncRequest* in X0 (or NULL if queue empty).
  /// </summary>
  private void EmitIoDequeueSyncReq() {
    EmitRuntimeFunctionStart("__io_dequeue_sync_req", 0, 0x30);

    // Acquire lock to protect sync request queue
    EmitLockAcquire("__sched_io_lock");

    EmitGlobalLoadReg(ARM64Register.X0, "__io_sync_req_head");
    EmitCbnz(ARM64Register.X0, "__io_dequeue_sync_nonempty");
    // Empty → return NULL
    EmitLockRelease("__sched_io_lock");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_dequeue_sync_nonempty");
    // new_head = head.next
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);
    EmitGlobalStoreReg(ARM64Register.X1, "__io_sync_req_head");
    EmitCbnz(ARM64Register.X1, "__io_dequeue_sync_done");
    // Queue now empty, clear tail
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitGlobalStoreReg(ARM64Register.X1, "__io_sync_req_tail");
    DefineLabel("__io_dequeue_sync_done");
    // Save return value before lock release clobbers X0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    // Clear dequeued node's next
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);
    // Release lock, restore return value
    EmitLockRelease("__sched_io_lock");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_check_completions(): Process pending sync I/O requests inline.
  /// Non-blocking: returns immediately if no requests are pending.
  /// </summary>
  private void EmitIoCheckCompletions() {
    // Frame: 0x100 to accommodate stat64 buffer and networking locals
    // Locals:
    //   [x29+16]  = req ptr
    //   [x29+24]  = waiter GT ptr
    //   [x29+32]  = op code
    //   [x29+40]  = result value / temp
    //   [x29+48 .. x29+191] = stat64 buffer (144 bytes) / sockaddr_in for net_connect
    //   [x29+64]  = net_connect: socket fd
    //   [x29+72]  = net_connect: resolved IP
    //   [x29+80]  = net_connect: port
    //   [x29+88]  = net_send/recv: args struct ptr
    //   [x29+200] = captured POSIX errno (Phase B errno→variant dispatch).
    //               Failure handlers populate this before jumping to __io_op_done,
    //               which writes it through to the waiter's gt->io_error_code so
    //               the lowering can map ENOENT/EACCES via __io_get_last_error.
    // Note: getcwd path buffer is heap-allocated instead of stack-allocated
    EmitRuntimeFunctionStart("__io_check_completions", 0, 0x100);

    // Process sync request queue
    DefineLabel("__io_check_comp_loop");
    EmitBranchLink("__io_dequeue_sync_req");
    EmitCbz(ARM64Register.X0, "__io_check_comp_ret"); // queue empty → done

    // Save req ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    // Load and save waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, SyncReqOffWaiter, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    // Load and save op code
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X0, SyncReqOffOp, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 32, 8);
    // Zero the error-code scratch slot. Failure handlers overwrite it; success
    // handlers leave it at 0 so __io_op_done writes 0 to gt->io_error_code.
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 200, 8);

    // Dispatch on op code
    EmitCmpImm(ARM64Register.X2, SyncOpFileExists);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_exists");
    EmitCmpImm(ARM64Register.X2, SyncOpFileDelete);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_delete");
    EmitCmpImm(ARM64Register.X2, SyncOpFileRename);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_rename");
    EmitCmpImm(ARM64Register.X2, SyncOpDirExists);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_dir_exists");
    EmitCmpImm(ARM64Register.X2, SyncOpDirCreate);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_dir_create");
    EmitCmpImm(ARM64Register.X2, SyncOpGetCwd);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_get_cwd");
    EmitCmpImm(ARM64Register.X2, SyncOpFileOpenRead);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_open_read");
    EmitCmpImm(ARM64Register.X2, SyncOpFileOpenWrite);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_open_write");
    EmitCmpImm(ARM64Register.X2, SyncOpCloseHandle);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_close_handle");
    EmitCmpImm(ARM64Register.X2, SyncOpNetConnect);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_connect");
    EmitCmpImm(ARM64Register.X2, SyncOpNetSend);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_send");
    EmitCmpImm(ARM64Register.X2, SyncOpNetRecv);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_recv");
    EmitCmpImm(ARM64Register.X2, SyncOpNetClose);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_net_close");
    EmitCmpImm(ARM64Register.X2, SyncOpFileOpenWriteExec);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_open_write_exec");

    // Unknown op → result = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpFileExists: stat64(path, &buf), check not directory ---
    DefineLabel("__io_op_file_exists");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // path
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // &stat_buf
    EmitCallImport("stat");
    EmitBranchOnLibcError("__io_op_file_exists_fail");
    // Load st_mode (uint16 at stat_buf + 4)
    // LDRH W1, [X29, #52]
    EmitWord(0x79400000 | ((52u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X1));
    // AND W1, W1, #0xF000
    EmitMovRegImm(ARM64Register.X2, 0xF000);
    EmitWord(0x0A020000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X1));
    // CMP W1, #0x4000 (S_IFDIR)
    EmitMovRegImm(ARM64Register.X2, 0x4000);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5));
    // CSINC X0, XZR, XZR, EQ → 1 if not dir, 0 if dir
    EmitWord(0x9A9F07E0 | (CondCode(ARM64ConditionCode.Eq) << 12) | Reg(ARM64Register.X0));
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_file_exists_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpFileDelete: unlink(path) ---
    DefineLabel("__io_op_file_delete");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitCallImport("unlink");
    EmitBranchOnLibcError("__io_op_file_delete_fail");
    EmitMovRegImm(ARM64Register.X0, 0); // success (0 per spec)
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_file_delete_fail");
    EmitCaptureErrnoToFrameSlot(200); // capture errno for notFound/accessDenied
    EmitMovRegImm(ARM64Register.X0, -1); // failure
    EmitBranch("__io_op_done");

    // --- SyncOpFileRename: rename(old, new) ---
    DefineLabel("__io_op_file_rename");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // old path
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X9, SyncReqOffArg1, 8); // new path
    EmitCallImport("rename");
    EmitBranchOnLibcError("__io_op_file_rename_fail");
    EmitMovRegImm(ARM64Register.X0, 0); // success (0 per spec)
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_file_rename_fail");
    EmitCaptureErrnoToFrameSlot(200); // capture errno for notFound/accessDenied
    EmitMovRegImm(ARM64Register.X0, -1); // failure
    EmitBranch("__io_op_done");

    // --- SyncOpDirExists: stat64, check S_IFDIR ---
    DefineLabel("__io_op_dir_exists");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true);
    EmitCallImport("stat");
    EmitBranchOnLibcError("__io_op_dir_exists_fail");
    // LDRH W1, [X29, #52]
    EmitWord(0x79400000 | ((52u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X1));
    EmitMovRegImm(ARM64Register.X2, 0xF000);
    EmitWord(0x0A020000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X1));
    EmitMovRegImm(ARM64Register.X2, 0x4000);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X1) << 5));
    // CSINC X0, XZR, XZR, NE → 1 if IS dir, 0 if not dir
    EmitWord(0x9A9F07E0 | (CondCode(ARM64ConditionCode.Ne) << 12) | Reg(ARM64Register.X0));
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_dir_exists_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpDirCreate: mkdir(path, 0777) ---
    DefineLabel("__io_op_dir_create");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, 0x1FF); // 0777
    EmitCallImport("mkdir");
    EmitBranchOnLibcError("__io_op_dir_create_fail");
    EmitMovRegImm(ARM64Register.X0, 1); // success
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_dir_create_fail");
    EmitCaptureErrnoToFrameSlot(200);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpGetCwd: alloc buf, open(".") + fcntl(F_GETPATH, buf), close ---
    DefineLabel("__io_op_get_cwd");
    // Allocate 1024-byte path buffer via mm_raw_alloc (freed via mm_raw_free)
    EmitMovRegImm(ARM64Register.X0, 1024);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save buf ptr
    // open(".", O_RDONLY)
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__dot_path");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_get_cwd_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save fd (reuse stat buf area)
    // fcntl(fd, F_GETPATH, buf)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // buf
    EmitMovRegImm(ARM64Register.X1, F_GETPATH);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("fcntl");
    EmitVariadicCleanup();
    // close(fd)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitCallImport("close");
    // Return the heap-allocated buffer ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__io_op_done");
    DefineLabel("__io_op_get_cwd_fail");
    // Capture errno BEFORE mm_free (free path may itself touch errno).
    EmitCaptureErrnoToFrameSlot(200);
    // Free the allocated buffer on failure
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranchLink("mm_free");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpFileOpenRead: open(path, O_RDONLY) ---
    DefineLabel("__io_op_file_open_read");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, 0); // O_RDONLY
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_file_open_read_fail");
    EmitBranch("__io_op_done"); // X0 = fd
    DefineLabel("__io_op_file_open_read_fail");
    EmitCaptureErrnoToFrameSlot(200);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpFileOpenWrite: open(path, O_WRONLY|O_CREAT|O_TRUNC, 0666) ---
    DefineLabel("__io_op_file_open_write");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, O_WRONLY_CREAT_TRUNC);
    EmitMovRegImm(ARM64Register.X2, 0x1B6); // 0666
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_file_open_write_fail");
    EmitBranch("__io_op_done"); // X0 = fd
    DefineLabel("__io_op_file_open_write_fail");
    EmitCaptureErrnoToFrameSlot(200);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpFileOpenWriteExec: open(path, O_WRONLY|O_CREAT|O_TRUNC, 0755) ---
    DefineLabel("__io_op_file_open_write_exec");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitMovRegImm(ARM64Register.X1, O_WRONLY_CREAT_TRUNC);
    EmitMovRegImm(ARM64Register.X2, 0x1ED); // 0755
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("open");
    EmitVariadicCleanup();
    EmitBranchOnLibcError("__io_op_file_open_write_exec_fail");
    EmitBranch("__io_op_done"); // X0 = fd
    DefineLabel("__io_op_file_open_write_exec_fail");
    EmitCaptureErrnoToFrameSlot(200);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpCloseHandle: close(fd) ---
    DefineLabel("__io_op_close_handle");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8);
    EmitCallImport("close");
    EmitMovRegImm(ARM64Register.X0, 0); // always return 0
    EmitBranch("__io_op_done");

    // --- SyncOpNetConnect: resolve hostname, socket(), connect() → fd or -1/-2 ---
    // req.arg0 = cstring hostname, req.arg1 = port
    // Uses stack: [x29+48..x29+63] = sockaddr_in (16 bytes)
    //             [x29+64] = socket fd, [x29+72] = resolved IP, [x29+80] = port
    DefineLabel("__io_op_net_connect");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg1, 8); // port
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8); // save port

    // Resolve hostname → IP
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // hostname
    EmitBranchLink("__net_resolve_host");
    // X0 = IP in network byte order, or 0 on failure
    EmitCbz(ARM64Register.X0, "__io_op_ntc_dns_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8); // save IP

    // Check TEST-NET-1 (192.0.2.1 = 0xC00002.01 in network byte order = 0x010200C0 as 32-bit)
    EmitMovRegImm(ARM64Register.X1, 0x010200C0);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_ntc_testnet_fail");

    // socket(AF_INET=2, SOCK_STREAM=1, 0)
    EmitMovRegImm(ARM64Register.X0, 2);  // AF_INET
    EmitMovRegImm(ARM64Register.X1, 1);  // SOCK_STREAM
    EmitMovRegImm(ARM64Register.X2, 0);  // protocol
    EmitCallImport("socket");
    EmitBranchOnLibcError("__io_op_ntc_connect_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // save socket fd

    // Build sockaddr_in at [x29+48] (16 bytes)
    // Zero 16 bytes
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    // sin_len=16, sin_family=AF_INET=2 → STRH at [x29+48]
    EmitMovRegImm(ARM64Register.X0, 0x0210); // len=16, family=2
    EmitWord(0x79000000 | ((48u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    // sin_port = htons(port) at [x29+50]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8); // port
    // htons: reverse bytes of 16-bit value. port is in X0.
    // REV16 W0, W0 then AND to 16 bits
    EmitWord(0x5AC00400 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // REV16 W0, W0
    EmitWord(0x79000000 | ((50u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STRH [X29, #50]
    // sin_addr = resolved IP at [x29+52]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 72, 8); // IP
    EmitWord(0xB9000000 | ((52u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STR W0, [X29, #52]

    // connect(socket, &sockaddr_in, 16)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // socket
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // &sockaddr
    EmitMovRegImm(ARM64Register.X2, 16);
    EmitCallImport("connect");
    EmitBranchOnLibcError("__io_op_ntc_close_connect_fail");

    // Success: return socket fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitBranch("__io_op_done");

    DefineLabel("__io_op_ntc_close_connect_fail");
    // Close socket, return -2
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitCallImport("close");
    DefineLabel("__io_op_ntc_connect_fail");
    DefineLabel("__io_op_ntc_testnet_fail");
    EmitMovRegImm(ARM64Register.X0, -2);
    EmitBranch("__io_op_done");

    DefineLabel("__io_op_ntc_dns_fail");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitBranch("__io_op_done");

    // --- SyncOpNetSend: write(handle, buf, len) → bytes written ---
    // req.arg0 = socket handle, req.arg1 = args struct ptr {buf_ptr, length}
    DefineLabel("__io_op_net_send");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, SyncReqOffArg0, 8); // socket handle
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X9, SyncReqOffArg1, 8); // args struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 88, 8); // save args for freeing
    // Load buf_ptr and length from args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X11, 0, 8); // buf_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X11, 8, 8); // length
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10); // fd
    EmitCallImport("write");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result
    // Free args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8);
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    // On error, X0 would be negative.
    // Restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__io_op_done");

    // --- SyncOpNetRecv: read(handle, buf, capacity) → bytes read ---
    // req.arg0 = socket handle, req.arg1 = args struct ptr {buf_ptr, capacity}
    DefineLabel("__io_op_net_recv");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, SyncReqOffArg0, 8); // socket handle
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X9, SyncReqOffArg1, 8); // args struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 88, 8); // save args for freeing
    // Load buf_ptr and capacity from args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X11, 0, 8); // buf_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X11, 8, 8); // capacity
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10); // fd
    EmitCallImport("read");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result
    // Free args struct
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8);
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    // Restore result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitBranch("__io_op_done");

    // --- SyncOpNetClose: close(handle) ---
    DefineLabel("__io_op_net_close");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 16, 8); // req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, SyncReqOffArg0, 8); // handle
    EmitCbz(ARM64Register.X0, "__io_op_net_close_skip");
    EmitCallImport("close");
    DefineLabel("__io_op_net_close_skip");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- Common completion: store result, re-enqueue waiter, free req ---
    DefineLabel("__io_op_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result

    // Inline completion: set result, error, status, and enqueue waiter
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 24, 8); // waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoResultVal, 8);
    // io_error_code := captured errno slot (0 on success, errno on failure).
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 200, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
    // Publish io_result/io_error before status=ready so a waiter that observes ready
    // (via the io_submit_sync self-check below) reads the finished result on weak memory.
    EmitDmbIsh();
    EmitMovRegImm(ARM64Register.X0, GtStatusReady);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitDmbIsh();

    // Guarded enqueue. Enqueueing a waiter that is NOT suspended off-stack lets a second M resume
    // it onto its live stack (the double-schedule that frees/corrupts an in-use GT stack). Skip the
    // enqueue when:
    //   (a) waiter is the main OS thread (stackBase==0) — it polls status in its await loop;
    //   (b) waiter == currentGt — it is driving this completion in its own io_submit_sync
    //       spin and self-detects status=ready;
    //   (c) waiter.ioYielded==0 — it is still running/spinning (on this or another M) and
    //       self-detects. __gt_context_switch clears ioYielded on resume and sets it on
    //       switch-off, so ioYielded==1 means exactly "suspended, needs an enqueue to run".
    //
    // ⚠ THIS PATH KEEPS THE OLD THREE-WAY GUARD AND DOES NOT USE THE PARK WORD, WHICH IS A DECISION.
    // The guard's failure mode is (c) being unable to tell "will self-detect" from "about to park" —
    // and on THIS path there is no such instant, because __io_submit_sync registers AFTER the park
    // (Go's gopark hand-off): a worker GT stores its request into P->pendingSyncReq and switches
    // off, and the scheduler enqueues it only once the GT is provably parked, so no completer can
    // observe the request while its waiter still runs. The main OS thread never parks at all — it
    // spins in __io_submit_sync_main_spin. A wakeup therefore cannot be lost here, so arming a park
    // word would buy nothing and cost a second protocol on the same GT field.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, "__io_op_done_skip_enqueue");
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_done_skip_enqueue");
    // Only a genuinely suspended waiter (ioYielded==1, switched off its stack) needs an
    // enqueue to resume; a still-running/spinning waiter (ioYielded==0) self-detects
    // status=ready in its io_submit_sync loop. Non-blocking snapshot — do NOT gate here:
    // io_op_done runs inside the io_check_completions loop that DRIVES I/O, so blocking it
    // stalls every other pending completion and cascades into a livelock.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);
    EmitCbz(ARM64Register.X0, "__io_op_done_skip_enqueue");
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("__gt_enqueue");
    DefineLabel("__io_op_done_skip_enqueue");

    // Free req
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8);
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // Loop
    EmitBranch("__io_check_comp_loop");

    DefineLabel("__io_check_comp_ret");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_sync_worker_loop(void* arg) -> void*: Dedicated pthread for sync I/O.
  /// Waits on __io_sync_req_semaphore, then calls __io_check_completions to process
  /// pending requests. Loops until shutdown flag is set.
  /// </summary>
  private void EmitIoSyncWorkerLoop() {
    EmitRuntimeFunctionStart("__io_sync_worker_loop", 1, 0x20);

    // I/O worker has no P* — set X28 = 0 so enqueue routes to global queue
    EmitMovRegImm(ARM64Register.X28, 0);

    DefineLabel("__io_sync_worker_loop_top");

    // Wait for a request to be enqueued (blocks forever until signaled) on the
    // I/O sync wake lock block (Go semasleep, no timeout — pthread_cond_wait).
    EmitGlobalLoadReg(ARM64Register.X9, "__io_sync_req_semaphore");
    EmitSemaSleep(ARM64Register.X9, timed: false);

    // Check shutdown flag
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_shutdown_flag");
    EmitCbnz(ARM64Register.X0, "__io_sync_worker_loop_exit");

    // The ioYielded protocol prevents enqueueing a GT whose context is still being saved.
    // However, __io_check_completions calls mm_alloc and other non-thread-safe functions,
    // so the worker stays as a wake-only loop. Scheduler loops process I/O inline.

    // Loop back
    EmitBranch("__io_sync_worker_loop_top");

    DefineLabel("__io_sync_worker_loop_exit");
    // Return NULL (pthread entry point must return void*)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_submit_sync(op_x0, arg0_x1, arg1_x2) -> result in X0.
  /// Submits an I/O request, yields the current green thread, and returns
  /// the result after the scheduler processes the request.
  /// </summary>
  private void EmitIoSubmitSync() {
    EmitRuntimeFunctionStart("__io_submit_sync", 3, 0x50);
    // [x29+16] = op, [x29+24] = arg0, [x29+32] = arg1

    // Check cancel flag
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffCancelFlag, 8);
    EmitCbnz(ARM64Register.X0, "__io_submit_sync_cancelled");

    // Allocate SyncRequest
    EmitMovRegImm(ARM64Register.X0, SyncReqSize);
    EmitCallMmRawAlloc();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save req

    // Fill: op, arg0, arg1, waiter=current, next=0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // op
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffOp, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // arg0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffArg0, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // arg1
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffArg1, 8);
    EmitLoadCurrentGt(ARM64Register.X1); // clobbers X0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // reload req
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffWaiter, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, SyncReqOffNext, 8);

    // Set current.status = waiting and clear ioYielded BEFORE enqueueing.
    // __io_op_done may run on another M the instant the request is visible; ioYielded must be 0 at
    // that point so its guard (c) declines the enqueue until the context switch has saved our
    // registers. __gt_context_switch sets ioYielded=1 after the save.
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      // Trace: "io_yield #N [op_name]\n"
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_io_yield");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadCurrentGt(ARM64Register.X9);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitIoTraceOpSuffix("yield");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    // The main OS thread (stackBase==0) cannot park — it IS its P's scheduler — so it
    // enqueues the request and spins. A worker GT parks (Go gopark): it hands the request to
    // its P's scheduler and switches off, and the scheduler enqueues the request only once we
    // are fully parked (ioYielded=1), so no completer can ever resume us while we still run.
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, "__io_submit_sync_main");

    // --- Worker GT: register-after-park. Hand off the request, then switch to this P's
    // scheduler (&P.mainThread). __gt_context_switch saves our context and sets ioYielded=1;
    // __gt_process_pending_sync_req (run by the scheduler) then enqueues the request with us
    // already parked. We resume at __io_submit_sync_resume once __io_op_done re-enqueues us. ---
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // req
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffPendingSyncReq, 8);
    EmitLoadCurrentGt(ARM64Register.X0);              // from = current
    EmitLoadP(ARM64Register.X9);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X9, POffMainThread, isAdd: true); // to = &P.mainThread
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // p = P
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__io_submit_sync_resume");

    // --- Main OS thread: enqueue the request + wake the sync worker, then spin driving I/O
    // and self-detecting completion (status WAITING→ready set by __io_op_done, which skips the
    // enqueue for a stackBase==0 waiter). ---
    DefineLabel("__io_submit_sync_main");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // req
    EmitBranchLink("__io_enqueue_sync_req");
    EmitGlobalLoadReg(ARM64Register.X9, "__io_sync_req_semaphore");
    EmitSemaWakeup(ARM64Register.X9);
    DefineLabel("__io_submit_sync_main_spin");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    // Acquire half of __io_op_done's release fence — __io_submit_sync_resume below returns
    // gt.io_result_val, and arm64 does not order that load behind this one just because a
    // branch on this one sits between them.
    //
    // ⚠ THIS ONE STAYS A HAND-ROLLED FENCE WHERE THE PARK PATH'S BECAME AN ACQUIRE INSIDE
    // __netpoll_woken, because this spin has no park word to acquire ON: __io_submit_sync uses
    // register-after-park and never arms one (see __io_op_done's guard note). A `status` read plus a
    // fence is exactly right here and exactly wrong there; the difference is whether an ownership
    // word exists to pair with, not a difference of taste.
    EmitDmbIsh();
    EmitCmpImm(ARM64Register.X0, GtStatusReady);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_submit_sync_resume");
    EmitDriveSchedulerAndIo();
    EmitBranch("__io_submit_sync_main_spin");

    // Resume here — worker GT via __io_op_done re-enqueue, main thread via its self-check.
    DefineLabel("__io_submit_sync_resume");
    // Stamp status=running (parity with the enqueue-resume path; needed for the main self-
    // check which reaches here with status still ready).
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      // Trace: "io_resume #N [op_name]\n"
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_io_resume");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadCurrentGt(ARM64Register.X9);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitIoTraceOpSuffix("resume");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    // Return gt.io_result_val
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffIoResultVal, 8);
    EmitRuntimeFunctionEnd();

    // Cancelled path
    DefineLabel("__io_submit_sync_cancelled");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, 995); // generic error code
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emits trace op suffix for io_yield/io_resume: prints " [op_name]" based on
  /// the op code saved at [x29+16].
  /// </summary>
  private void EmitIoTraceOpSuffix(string context) {
    var doneLabel = $"__io_trace_op_done_{context}";
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // load op code

    var ops = new (long opCode, string symdata)[] {
      (SyncOpFileExists,    "__at_io_op_file_exists"),
      (SyncOpFileDelete,    "__at_io_op_file_delete"),
      (SyncOpDirExists,     "__at_io_op_dir_exists"),
      (SyncOpDirCreate,     "__at_io_op_dir_create"),
      (SyncOpGetCwd,        "__at_io_op_get_cwd"),
      (SyncOpFileOpenRead,  "__at_io_op_file_open_read"),
      (SyncOpFileOpenWrite, "__at_io_op_file_open_write"),
      (SyncOpFileOpenWriteExec, "__at_io_op_file_open_write_exec"),
      (SyncOpCloseHandle,   "__at_io_op_close_handle"),
      (SyncOpNetConnect,    "__at_io_op_net_connect"),
      (SyncOpNetSend,       "__at_io_op_net_send"),
      (SyncOpNetRecv,       "__at_io_op_net_recv"),
      (SyncOpNetClose,      "__at_io_op_net_close"),
    };

    foreach (var (opCode, symdata) in ops) {
      var skipLabel = $"__io_trace_skip_{context}_{opCode}";
      EmitCmpImm(ARM64Register.X0, opCode);
      EmitBranchCond(ARM64ConditionCode.Ne, skipLabel);
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, symdata);
      EmitBranchLink("mm_trace_print_tag");
      EmitBranch(doneLabel);
      DefineLabel(skipLabel);
    }

    DefineLabel(doneLabel);
  }

  /// <summary>
  /// Emits inline async trace: "io_yield #N [op_name]\n" or "io_resume #N [op_name]\n".
  /// Clobbers X0, X9. Caller must save any live registers before calling this.
  /// </summary>
  private void EmitIoTraceInline(string phase, string opSymdata) {
    if (!Compiler.AsyncTrace) return;
    EmitTraceAcquire();
    var tag = phase == "yield" ? "__at_tag_io_yield" : "__at_tag_io_resume";
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, tag);
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
    EmitBranchLink("mm_trace_print_i64");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, opSymdata);
    EmitBranchLink("mm_trace_print_tag");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
    EmitBranchLink("mm_trace_print_tag");
    EmitTraceRelease();
  }

  // =====================================================================
  // Networking runtime functions — macOS ARM64
  // =====================================================================

  /// <summary>
  /// maxon_net_tcp_connect(cstring_x0, port_x1) → managed __ManagedSocket ptr, or -1 (DNS fail), -2 (connect fail).
  /// Performs async connect: DNS resolve (sync), socket + O_NONBLOCK, non-blocking connect,
  /// then kqueue EVFILT_WRITE wait for completion.
  /// Stack: [x29+16]=host, [x29+24]=port, [x29+32]=socket_fd, [x29+40]=resolved_ip,
  ///        [x29+48..63]=sockaddr_in (16B), [x29+64]=ctx_ptr, [x29+72..103]=kevent (32B),
  ///        [x29+104]=next_gt, [x29+112..119]=getsockopt err buf, [x29+120..127]=socklen buf
  /// </summary>
  private void EmitNetTcpConnect() {
    EmitRuntimeFunctionStart("maxon_net_tcp_connect", 2, 0xC0);

    // --- Async trace: io_yield [net_connect] ---
    EmitIoTraceInline("yield", "__at_io_op_net_connect");

    // --- DNS resolve ---
    EmitReloadArg(0); // X0 = hostname cstring
    EmitBranchLink("__net_resolve_host");
    EmitCbz(ARM64Register.X0, "rt_ntc_dns_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save IP

    // Check TEST-NET-1 (192.0.2.1 = 0x010200C0 as 32-bit little-endian)
    EmitMovRegImm(ARM64Register.X1, 0x010200C0);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "rt_ntc_testnet_fail");

    // --- socket(AF_INET=2, SOCK_STREAM=1, 0) ---
    EmitMovRegImm(ARM64Register.X0, 2);  // AF_INET
    EmitMovRegImm(ARM64Register.X1, 1);  // SOCK_STREAM
    EmitMovRegImm(ARM64Register.X2, 0);  // protocol
    EmitCallImport("socket");
    EmitBranchOnLibcError("rt_ntc_connect_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save fd

    // --- fcntl(fd, F_SETFL, O_NONBLOCK) ---
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // fd
    EmitMovRegImm(ARM64Register.X1, F_SETFL);
    EmitMovRegImm(ARM64Register.X2, O_NONBLOCK);
    EmitPushVariadicArg(ARM64Register.X2); // Apple ARM64: variadic arg on stack
    EmitCallImport("fcntl");
    EmitVariadicCleanup();

    // --- Build sockaddr_in at [x29+48] ---
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    // sin_len=16, sin_family=AF_INET=2
    EmitMovRegImm(ARM64Register.X0, 0x0210);
    EmitWord(0x79000000 | ((48u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STRH
    // sin_port = htons(port)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // port
    EmitWord(0x5AC00400 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // REV16 W0, W0
    EmitWord(0x79000000 | ((50u / 2) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STRH [X29, #50]
    // sin_addr = resolved IP
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // IP
    EmitWord(0xB9000000 | ((52u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STR W0, [X29, #52]

    // --- non-blocking connect(fd, &sockaddr, 16) ---
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // fd
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true);
    EmitMovRegImm(ARM64Register.X2, 16);
    EmitCallImport("connect");
    // 0 = immediate success, -1 = in progress (EINPROGRESS) or error
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, "rt_ntc_connected");

    // --- connect returned -1: register kqueue EVFILT_WRITE and yield ---
    // Allocate KqCtx
    EmitMovRegImm(ARM64Register.X0, KqCtxSize);
    EmitCallMmRawAlloc();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // save ctx

    // Fill ctx: fd, buf=0, len=0, waiter=current, filter=KQCTX_CONNECT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, KqCtxOffFd, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, KqCtxOffBuf, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, KqCtxOffLen, 8);
    EmitLoadCurrentGt(ARM64Register.X1); // clobbers X0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // reload ctx
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, KqCtxOffWaiter, 8);
    EmitMovRegImm(ARM64Register.X1, KQCTX_CONNECT);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, KqCtxOffFilter, 8);

    // Build kevent struct at [x29+72] (32 bytes)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 88, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8);
    // ident = fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8);
    // filter=EVFILT_WRITE, flags=EV_ADD|EV_ONESHOT (packed 32-bit at kevent+8)
    var filterAndFlags = (uint)(unchecked((ushort)EVFILT_WRITE) | ((EV_ADD | EV_ONESHOT) << 16));
    EmitMovRegImm(ARM64Register.X0, (long)filterAndFlags);
    EmitWord(0xB9000000 | ((80u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STR W0, [X29, #80]
    // udata = ctx ptr (kevent+24 = offset 96)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8);

    EmitMarkWaitingAndArmKevent(72, "maxon_net_tcp_connect");

    EmitGtParkForIoCompletion("rt_ntc", 104);

    // Resumed: io_result_val set by __io_poll_kqueue
    DefineLabel("rt_ntc_resumed");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffIoResultVal, 8);
    EmitBranch("rt_ntc_check_result");

    // Immediate connect success
    DefineLabel("rt_ntc_connected");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // fd

    DefineLabel("rt_ntc_check_result");
    // X0 = fd (≥0) or error (<0)
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "rt_ntc_fail");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save handle

    // Clear O_NONBLOCK now that connect is done — kqueue still fires events,
    // but read()/write() will block until completion (no partial reads)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // fd
    EmitMovRegImm(ARM64Register.X1, F_SETFL);
    EmitMovRegImm(ARM64Register.X2, 0); // flags = 0 (blocking)
    EmitPushVariadicArg(ARM64Register.X2);
    EmitCallImport("fcntl");
    EmitVariadicCleanup();

    // Async trace: io_resume [net_connect]
    EmitIoTraceInline("resume", "__at_io_op_net_connect");

    // Allocate __ManagedSocket via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(ARM64Register.X0, 8);
    EmitAdrpAddFixup(ARM64Register.X1, _funcAddrAdrpFixups, "__destruct___ManagedSocket");
    EmitMovRegImm(ARM64Register.X2, 0);
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X3, 0); // null trace scope
    EmitBranchLink("mm_alloc");

    // Store socket handle at [managed_ptr+0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitBranch("rt_ntc_done");

    DefineLabel("rt_ntc_fail");
    // X0 already has error code (-1 or -2) — save across trace call
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitIoTraceInline("resume", "__at_io_op_net_connect");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitBranch("rt_ntc_done");

    DefineLabel("rt_ntc_close_connect_fail");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitCallImport("close");
    DefineLabel("rt_ntc_connect_fail");
    DefineLabel("rt_ntc_testnet_fail");
    EmitMovRegImm(ARM64Register.X0, -2);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitIoTraceInline("resume", "__at_io_op_net_connect");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitBranch("rt_ntc_done");

    DefineLabel("rt_ntc_dns_fail");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitIoTraceInline("resume", "__at_io_op_net_connect");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    DefineLabel("rt_ntc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_send(socket_handle_x0, buffer_ptr_x1, length_x2) → bytes_sent or -1.
  /// Uses kqueue EVFILT_WRITE via __io_submit_write for async I/O.
  /// Stack: [x29+16]=handle, [x29+24]=buf, [x29+32]=length
  /// </summary>
  private void EmitNetSend() {
    EmitRuntimeFunctionStart("maxon_net_send", 3, 0x40);

    EmitIoTraceInline("yield", "__at_io_op_net_send");

    // Call __io_submit_write(fd, buf, len)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // len
    EmitBranchLink("__io_submit_write");
    // X0 = bytes written or -1
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save result

    EmitIoTraceInline("resume", "__at_io_op_net_send");

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // restore result
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_recv(socket_handle_x0, buffer_ptr_x1, capacity_x2) → bytes_received, 0=closed, -1=error.
  /// Uses kqueue EVFILT_READ via __io_submit_read for async I/O.
  /// Stack: [x29+16]=handle, [x29+24]=buf, [x29+32]=capacity
  /// </summary>
  private void EmitNetRecv() {
    EmitRuntimeFunctionStart("maxon_net_recv", 3, 0x40);

    EmitIoTraceInline("yield", "__at_io_op_net_recv");

    // Call __io_submit_read(fd, buf, capacity)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // capacity
    EmitBranchLink("__io_submit_read");
    // X0 = bytes read, 0 = closed, -1 = error
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save result

    EmitIoTraceInline("resume", "__at_io_op_net_recv");

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // restore result
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_close(__ManagedSocket*_x0) → void. Idempotent: no-op if ptr is null or handle is 0.
  /// Reads _handle from [ptr+0], zeros the field, then delegates close() to the sync worker.
  /// Being the single point that clears _handle ensures the destructor's idempotency check
  /// sees a zeroed field after an explicit close — no double-close on a reused fd.
  /// </summary>
  private void EmitNetClose() {
    EmitRuntimeFunctionStart("maxon_net_close", 1, 0x30);
    EmitReloadArg(0); // X0 = __ManagedSocket*
    var doneLabel = $"__nclose_noop_{_uniqueLabelCounter++}";

    // Null-ptr guard
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done

    // Load _handle; skip if <= 0 (uninitialized or already closed)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitWord(0xF100003F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE done

    // Zero _handle before submitting close — the sync worker then sees a single outstanding close.
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8);

    // __io_submit_sync(SyncOpNetClose, handle, 0) — routes close() through the sync worker
    // so it participates in the async I/O model consistently with file close paths.
    // X1 still holds the handle (arg0); set op in X0 and reuse zeroed X2 as arg1.
    EmitMovRegImm(ARM64Register.X0, SyncOpNetClose);
    EmitBranchLink("__io_submit_sync");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedSocket(user_ptr_x0) → void.
  /// Called by mm_decref when refcount hits 0. Delegates to maxon_net_close,
  /// which reads _handle, zeros it, and closes. If an explicit close() already ran,
  /// _handle is zero and this is a no-op.
  /// </summary>
  private void EmitNetSocketDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedSocket", 1, 0x20);
    EmitReloadArg(0); // X0 = user_ptr
    EmitBranchLink("maxon_net_close");
    EmitRuntimeFunctionEnd();
  }

  // =====================================================================
  // DNS resolver — uses DNSServiceGetAddrInfo (non-blocking OS resolver)
  // =====================================================================

  // Global to receive DNS callback result (single-threaded, safe)
  // Layout: [0]=resolved_ip (4 bytes as i64), [8]=error_flag
  private const string DnsResultGlobal = "__dns_result";

  /// <summary>
  /// __net_resolve_host(cstring_x0) → IP in network byte order in X0, or 0 on failure.
  /// Phase 1: tries to parse as dotted-decimal IP.
  /// Phase 2: uses DNSServiceGetAddrInfo (non-blocking macOS DNS resolver).
  /// Stack: [x29+16]=cstring, [x29+24]=dns_ref_ptr(8 bytes on stack), [x29+32]=dns_ref,
  ///        [x29+40]=fd, [x29+48..x29+175]=fd_set(128 bytes), [x29+72]=result_ip
  /// </summary>
  private void EmitNetResolveHost() {
    // Define global for DNS callback result
    DefineGlobal(DnsResultGlobal, 16, 0); // [0]=ip, [8]=error

    EmitRuntimeFunctionStart("__net_resolve_host", 1, 0xC0);
    // [x29+16] = cstring

    // --- Phase 1: Try to parse as IP address ---
    EmitReloadArg(0); // X0 = cstring
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = scan ptr
    DefineLabel("__nrh_ip_scan");
    EmitWord(0x38401422); // LDRB W2, [X1], #1
    EmitCbz(ARM64Register.X2, "__nrh_is_ip"); // null terminator → it's an IP
    EmitCmpImm(ARM64Register.X2, 46); // '.'
    EmitBranchCond(ARM64ConditionCode.Eq, "__nrh_ip_scan");
    EmitCmpImm(ARM64Register.X2, 48); // '0'
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_is_hostname");
    EmitCmpImm(ARM64Register.X2, 57); // '9'
    EmitBranchCond(ARM64ConditionCode.Gt, "__nrh_is_hostname");
    EmitBranch("__nrh_ip_scan");

    // --- Parse IP: "a.b.c.d" → network byte order ---
    DefineLabel("__nrh_is_ip");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8);
    EmitReloadArg(0);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 72, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 73, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 74, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 75, 1);
    EmitWord(0xB9400000 | ((72u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    EmitRuntimeFunctionEnd();

    // --- Phase 2: Hostname → DNSServiceGetAddrInfo ---
    DefineLabel("__nrh_is_hostname");

    // Clear DNS result global: ip=0, error=1
    EmitGlobalLeaReg(ARM64Register.X9, DnsResultGlobal);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 0, 8); // ip = 0
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 8, 8); // error = 1

    // DNSServiceGetAddrInfo(&ref, flags=0, ifindex=0, protocol=IPv4=1, hostname, callback, context=NULL)
    // Initialize ref slot to NULL before the call
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // ref = NULL
    // Set args in careful order to avoid clobbering
    EmitMovRegImm(ARM64Register.X6, 0);  // context = NULL
    EmitAdrpAddFixup(ARM64Register.X5, _funcAddrAdrpFixups, "__dns_callback"); // X5 = callback
    EmitReloadArg(0);                     // X0 = hostname (from [x29+16])
    EmitMovRegReg(ARM64Register.X4, ARM64Register.X0); // X4 = hostname
    EmitMovRegImm(ARM64Register.X3, 1);  // kDNSServiceProtocol_IPv4
    EmitMovRegImm(ARM64Register.X2, 0);  // interfaceIndex = 0 (any)
    EmitMovRegImm(ARM64Register.X1, 0);  // flags = 0
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 32, isAdd: true); // X0 = &ref
    EmitCallImport("DNSServiceGetAddrInfo");
    // X0 = error code (DNSServiceErrorType = int32_t, in W0)
    // Zero-extend W0 to X0 so CBNZ works correctly on 64-bit register
    EmitWord(0x2A0003E0); // MOV W0, W0 (ORR W0, WZR, W0 — zero-extends)
    EmitCbnz(ARM64Register.X0, "__nrh_dns_fail");

    // Load the ref from stack
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // dns_ref

    // DNSServiceRefSockFD(ref) → fd
    EmitCallImport("DNSServiceRefSockFD");
    // Zero-extend W0 to X0 (DNSServiceRefSockFD returns int32_t in W0)
    EmitWord(0x2A0003E0); // MOV W0, W0 (ORR W0, WZR, W0 — zero-extends)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save fd
    // Check fd >= 0
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_dns_dealloc_fail");

    // select(fd+1, &readfds, NULL, NULL, &timeout) to wait for DNS response
    // Build fd_set at [x29+48]: zero 128 bytes then set bit for our fd
    EmitMovRegImm(ARM64Register.X0, 0);
    for (int i = 0; i < 16; i++) { // 16 * 8 = 128 bytes
      EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48 + i * 8, 8);
    }
    // Set bit for fd: fd_set[fd/64] |= (1 << (fd%64))
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // fd
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitWord(0x9AC02021); // LSLV X1, X1, X0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 48, 8); // fd_set[0] = 1<<fd

    // Build timeval at [x29+176]: tv_sec=2, tv_usec=0
    EmitMovRegImm(ARM64Register.X0, 2);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 176, 8); // tv_sec
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 184, 8); // tv_usec

    // select(nfds=fd+1, readfds=&fd_set, writefds=NULL, errfds=NULL, timeout=&timeval)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // fd
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true); // nfds = fd+1
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 48, isAdd: true); // &readfds
    EmitMovRegImm(ARM64Register.X2, 0); // writefds = NULL
    EmitMovRegImm(ARM64Register.X3, 0); // errfds = NULL
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X29, 176, isAdd: true); // &timeout
    EmitCallImport("select");
    // X0 = number of ready fds (0 = timeout, -1 = error)
    EmitCmpImm(ARM64Register.X0, 1);
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_dns_dealloc_fail"); // timeout or error

    // DNSServiceProcessResult(ref) — fires the callback synchronously
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // dns_ref
    EmitCallImport("DNSServiceProcessResult");
    // Zero-extend W0 to X0 (DNSServiceProcessResult returns int32_t in W0)
    EmitWord(0x2A0003E0); // MOV W0, W0 (ORR W0, WZR, W0 — zero-extends)
    EmitCbnz(ARM64Register.X0, "__nrh_dns_dealloc_fail");

    // DNSServiceRefDeallocate(ref)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitCallImport("DNSServiceRefDeallocate");

    // Read result from global
    EmitGlobalLoadReg(ARM64Register.X0, DnsResultGlobal); // ip
    EmitRuntimeFunctionEnd();

    // Failure: deallocate ref and return 0
    DefineLabel("__nrh_dns_dealloc_fail");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitCallImport("DNSServiceRefDeallocate");
    DefineLabel("__nrh_dns_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __dns_callback: DNSServiceGetAddrInfoReply callback.
  /// Called by DNSServiceProcessResult when DNS resolves.
  /// Extracts IPv4 address from sockaddr and stores in __dns_result global.
  /// Args: X0=sdRef, X1=flags, X2=ifIndex, X3=errorCode, X4=hostname, X5=address, X6=ttl, X7=context
  /// </summary>
  private void EmitDnsCallback() {
    DefineLabel("__dns_callback");
    // STP x29, x30, [sp, #-0x30]!
    var imm7 = (uint)((-0x30 / 8) & 0x7F);
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Check errorCode (W3, int32_t) — 0 = success
    // Zero-extend W3 to X3 for 64-bit CBNZ
    EmitWord(0x2A0303E3); // MOV W3, W3 (zero-extends)
    EmitCbnz(ARM64Register.X3, "__dns_cb_done");

    // Check address pointer (X5) — must not be NULL
    EmitCbz(ARM64Register.X5, "__dns_cb_done");

    // X5 points to sockaddr. For IPv4 (sockaddr_in on macOS):
    //   [0] = sin_len (1 byte)
    //   [1] = sin_family (1 byte, should be AF_INET=2)
    //   [2..3] = sin_port (2 bytes)
    //   [4..7] = sin_addr (4 bytes, network byte order) ← this is what we want

    // Load sin_addr at [X5+4] as a 32-bit word
    EmitWord(0xB9400000 | ((4u / 4) << 10) | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDR W0, [X5, #4]

    // Store to __dns_result global: [0]=ip
    EmitGlobalLeaReg(ARM64Register.X9, DnsResultGlobal);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 0, 8); // store ip
    // Clear error flag
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, 8, 8); // error = 0

    DefineLabel("__dns_cb_done");
    // LDP x29, x30, [sp], #0x30
    EmitWord(0xA8C00000 | ((0x30u / 8) << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// IP-only resolve stub (no DNS imports). Used when program doesn't use networking.
  /// Returns parsed IP or 0.
  /// </summary>
  private void EmitNetResolveHostIpOnly() {
    EmitRuntimeFunctionStart("__net_resolve_host", 1, 0x60);
    // Scan for IP: all digits and dots
    EmitReloadArg(0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    DefineLabel("__nrh_ipo_scan");
    EmitWord(0x38401422); // LDRB W2, [X1], #1
    EmitCbz(ARM64Register.X2, "__nrh_ipo_is_ip");
    EmitCmpImm(ARM64Register.X2, 46);
    EmitBranchCond(ARM64ConditionCode.Eq, "__nrh_ipo_scan");
    EmitCmpImm(ARM64Register.X2, 48);
    EmitBranchCond(ARM64ConditionCode.Lt, "__nrh_ipo_fail");
    EmitCmpImm(ARM64Register.X2, 57);
    EmitBranchCond(ARM64ConditionCode.Gt, "__nrh_ipo_fail");
    EmitBranch("__nrh_ipo_scan");

    DefineLabel("__nrh_ipo_is_ip");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitReloadArg(0);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 24, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 25, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 26, 1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("__net_parse_octet");
    EmitLoadStoreUnsignedImm(0x39000000, ARM64Register.X0, ARM64Register.X29, 27, 1);
    EmitWord(0xB9400000 | ((24u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    EmitRuntimeFunctionEnd();

    DefineLabel("__nrh_ipo_fail");
    EmitMovRegImm(ARM64Register.X0, 0); // hostname without DNS = fail
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __net_parse_octet(cstring_x0) → octet value in X0, next char ptr in X1.
  /// Parses decimal digits from cstring until '.' or null terminator.
  /// </summary>
  private void EmitNetParseOctet() {
    DefineLabel("__net_parse_octet");
    // STP x29, x30, [sp, #-0x20]!
    var imm7 = (uint)((-0x20 / 8) & 0x7F);
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // X0 = input ptr, result in X0, next ptr in X1
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = scan ptr
    EmitMovRegImm(ARM64Register.X0, 0);                 // X0 = accumulated value

    DefineLabel("__npo_loop");
    // LDRB W2, [X1]
    EmitWord(0x39400022); // LDRB W2, [X1]
    // Check for null or '.'
    EmitCbz(ARM64Register.X2, "__npo_done");
    EmitCmpImm(ARM64Register.X2, 46); // '.'
    EmitBranchCond(ARM64ConditionCode.Eq, "__npo_dot");
    // value = value * 10 + (char - '0')
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 48, isAdd: false); // char - '0'
    // X0 = X0 * 10: MUL approach: X3 = 10, X0 = X0 * X3
    EmitMovRegImm(ARM64Register.X3, 10);
    EmitWord(0x9B037C00 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.Xzr) << 10) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // MADD X0, X0, X3, XZR
    EmitWord(0x8B020000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // ADD X0, X0, X2
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true);
    EmitBranch("__npo_loop");

    DefineLabel("__npo_dot");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: true); // skip '.'

    DefineLabel("__npo_done");
    // X0 = value, X1 = next ptr
    // LDP x29, x30, [sp], #0x20
    EmitWord(0xA8C00000 | ((0x20u / 8) << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitWord(0xD65F03C0); // RET
  }

  // ===========================================================================================
  // Trivial runtime functions
  // ===========================================================================================

  // mm_raw_alloc_260: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  /// <summary>
  /// __gt_panic_io(): Panic with IO error message.
  /// </summary>
  private void EmitGtPanicIo() {
    EmitRuntimeFunctionStart("__gt_panic_io", 0, 0x20);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__io_panic_msg");
    EmitBranchLink("mrt_panic");
    // mrt_panic does not return
  }

  /// <summary>
  /// maxon_sleep(ms_x0): Suspends the current green thread for the given duration.
  /// Computes deadline = now_nanos + ms*1e6, adds (deadline, gt) to the timer heap,
  /// then yields. __gt_timer_check re-enqueues the GT once the deadline has passed.
  ///
  /// The deadline is anchored to the monotonic HIGH-RESOLUTION clock (CLOCK_MONOTONIC via
  /// maxon_current_time_nanos), matching __gt_timer_check. A coarse tick-derived deadline
  /// could expire before `ms` of real time had elapsed -- see GtLayout.TimerNanosPerMilli.
  /// </summary>
  private void EmitMaxonSleep() {
    // Stack: [x29+16] = ms, [x29+24] = deadline, [x29+32] = dequeued GT
    EmitRuntimeFunctionStart("maxon_sleep", 1, 0x50);

    // deadline = maxon_current_time_nanos() + ms * 1e6. Calling the runtime function rather
    // than re-emitting clock_gettime keeps this in lockstep with __gt_timer_check, which
    // reads the clock through the same backend hook.
    EmitBranchLink("maxon_current_time_nanos");
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X0); // X2 = now_nanos

    EmitReloadArg(0); // X0 = ms
    EmitMovRegImm(ARM64Register.X3, TimerNanosPerMilli);
    EmitWord(0x9B037C00); // MUL X0, X0, X3  → X0 = ms * 1e6
    EmitWord(0x8B000040); // ADD X0, X2, X0  → X0 = now_nanos + ms*1e6
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save deadline

    // Set current GT status = waiting
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // __gt_timer_add(gt=current, deadline)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // deadline
    EmitBranchLink("__gt_timer_add");

    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_sleep_yield");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadCurrentGt(ARM64Register.X9);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    // Check if current GT is the mainThread (stackBase == 0)
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, "__sleep_mainthread_loop");

    // Non-mainThread: yield to next runnable
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__sleep_mainthread_loop"); // no one to run, fall through to park loop

    // Got a next thread — context switch to it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__sleep_resume"); // resumed when timer expires

    // MainThread park loop: inline scheduling until our status changes from waiting
    DefineLabel("__sleep_mainthread_loop");
    EmitDriveSchedulerAndIo();
    // Check if our status changed
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X0, GtStatusWaiting);
    EmitBranchCond(ARM64ConditionCode.Ne, "__sleep_resume");
    // Try dequeue a runnable GT and run it while we wait
    EmitBranchLink("__gt_dequeue");
    EmitCbz(ARM64Register.X0, "__sleep_mainthread_park");
    // Got a GT — context-switch to it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");
    EmitBranch("__sleep_mainthread_loop");

    // No GT to run — brief nanosleep then retry
    DefineLabel("__sleep_mainthread_park");
    // nanosleep({0, 1000000}, NULL) = 1ms
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 1000000);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // tv_nsec = 1ms
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 40, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0); // rem = NULL
    EmitCallImport("nanosleep");
    EmitBranch("__sleep_mainthread_loop");

    DefineLabel("__sleep_resume");
    EmitTraceAcquire();
    if (Compiler.AsyncTrace) {
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_sleep_resume");
      EmitBranchLink("mm_trace_print_tag");
      EmitLoadCurrentGt(ARM64Register.X9);
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffTraceId, 8);
      EmitBranchLink("mm_trace_print_i64");
      EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__at_tag_nl");
      EmitBranchLink("mm_trace_print_tag");
    }
    EmitTraceRelease();

    EmitRuntimeFunctionEnd();
  }

  // __gt_timer_add and __gt_timer_check are now emitted by RuntimeEmitter.Scheduler.cs

  // ===========================================================================================
  // Stack growth (__gt_morestack)
  // ===========================================================================================

  /// <summary>
  /// __gt_morestack: Called when a function prologue detects the stack guard is about to be hit.
  /// Allocates a new stack 2x the current size, copies the old stack content, adjusts FP chain,
  /// and switches to the new stack. Must run on P's system stack since the GT stack is full.
  /// Called with: X30 (LR) = return address to retry the function prologue.
  /// X28 = P* (always valid for green threads).
  /// </summary>
  private void EmitGtMorestack() {
    DefineLabel("__gt_morestack");
    // Called via BL from a function prologue's stack guard check.
    // X30 = return-to-prologue addr. X16 = original caller's LR. SP = old GT stack.
    //
    // Copy-based stack growth: allocate 2x stack, copy old content to top of new,
    // walk FP chain adjusting saved X29 pointers, munmap old stack, return on new stack.
    // Uses callee-saved X19-X22 as scratch (survive across libc calls).

    // Save return addr and old SP in scratch regs before switching stacks
    EmitMovRegReg(ARM64Register.X15, ARM64Register.X30); // return-to-prologue
    EmitMovRegReg(ARM64Register.X17, ARM64Register.Sp);  // old GT SP

    // Switch to per-P system stack
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X28, POffSystemStackSP, 8);
    EmitWord(0x9100013F); // MOV SP, X9 (ADD SP, X9, #0)

    // System frame: 0xB0 = 176 bytes (includes space for saving X0-X7 arg regs).
    var frameSize = 0xB0;
    var imm7 = unchecked((uint)(-frameSize / 8)) & 0x7Fu;
    EmitWord(0xA9800000 | (imm7 << 15) | (30u << 10) | (31u << 5) | 29u);
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Save scratch regs and callee-saved regs we'll use
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X15, ARM64Register.X29, 16, 8); // [+16] return addr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X17, ARM64Register.X29, 24, 8); // [+24] old GT SP
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X16, ARM64Register.X29, 32, 8); // [+32] original LR

    // Save X0-X7 (function argument registers clobbered by mmap/memmove/munmap calls)
    for (int i = 0; i < 8; i++)
      EmitLoadStoreUnsignedImm(0xF9000000, AbiArgRegs[i], ARM64Register.X29, 80 + i * 8, 8);
    // STP X19, X20, [X29, #40]
    EmitWord(0xA9000000 | (5u << 15) | (20u << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X19));
    // STP X21, X22, [X29, #56]
    EmitWord(0xA9000000 | (7u << 15) | (22u << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X21));

    // --- Load old stack info into callee-saved regs ---
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X19, ARM64Register.X9, GtOffStackBase, 8); // X19 = old_base
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X20, ARM64Register.X9, GtOffStackSize, 8); // X20 = old_size

    // --- Allocate new stack (2x) ---
    // X21 = new_size = old_size * 2
    // ADD X21, X20, X20
    EmitWord(0x8B140295);
    // mmap(NULL, new_size, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X21);
    EmitMovRegImm(ARM64Register.X2, 3);
    EmitMovRegImm(ARM64Register.X3, 0x1002);
    EmitMovRegImm(ARM64Register.X4, -1);
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitCallImport("mmap");
    EmitMovRegReg(ARM64Register.X22, ARM64Register.X0); // X22 = new_base

    // --- Copy old stack to top of new stack ---
    // dest = new_base + new_size - old_size
    // ADD X0, X22, X21; SUB X0, X0, X20
    EmitWord(0x8B1502C0); // ADD X0, X22, X21
    EmitWord(0xCB140000); // SUB X0, X0, X20
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X19); // src = old_base
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X20); // len = old_size
    EmitCallImport("memcpy");

    // --- Compute offset = dest - old_base ---
    // Recompute dest from callee-saved regs (X9 was clobbered by memcpy)
    // dest = X22 + X21 - X20
    // ADD X9, X22, X21
    EmitWord(0x8B1502C9); // ADD X9, X22, X21
    // SUB X9, X9, X20
    EmitWord(0xCB140129); // SUB X9, X9, X20
    // X9 = dest. offset = dest - X19
    // SUB X10, X9, X19 → X10 = offset
    EmitWord(0xCB13012A); // SUB X10, X9, X19


    // --- Precise FP-chain walk: adjust ONLY saved X29 values ---
    // ADD X12, X19, X20 → old_top
    EmitWord(0x8B14026C);

    // Step 1: Adjust the GT X29 in the system frame
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X29, 0, 8);
    EmitWord(0xEB13017F); // CMP X11, X19
    EmitBranchCond(ARM64ConditionCode.Lo, "__gt_morestack_fp_done");
    EmitWord(0xEB0C017F); // CMP X11, X12
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_morestack_fp_done");
    EmitWord(0x8B0A016B); // ADD X11, X11, X10
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X11, ARM64Register.X29, 0, 8);

    // Step 2: Walk the chain from adjusted FP. X11 = current frame on new stack.
    DefineLabel("__gt_morestack_fp_walk");
    // Load saved FP at [walker, #0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X14, ARM64Register.X11, 0, 8);
    EmitCbz(ARM64Register.X14, "__gt_morestack_fp_done"); // 0 = chain end
    // Check if saved_FP in [old_base, old_top)
    EmitWord(0xEB1301DF); // CMP X14, X19
    EmitBranchCond(ARM64ConditionCode.Lo, "__gt_morestack_fp_next");
    EmitWord(0xEB0C01DF); // CMP X14, X12
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_morestack_fp_next");
    // Adjust and store back
    EmitWord(0x8B0A01CE); // ADD X14, X14, X10
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X14, ARM64Register.X11, 0, 8);
    DefineLabel("__gt_morestack_fp_next");
    // Follow chain: walker = [walker, #0] (adjusted or not)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X11, 0, 8);
    EmitCbz(ARM64Register.X11, "__gt_morestack_fp_done");
    // Bounds check: walker must be in new stack copied region [X9, X9+X20)
    EmitWord(0xEB09017F); // CMP X11, X9
    EmitBranchCond(ARM64ConditionCode.Lo, "__gt_morestack_fp_done");
    // ADD X13, X9, X20
    EmitWord(0x8B14012D);
    EmitWord(0xEB0D017F); // CMP X11, X13
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_morestack_fp_done");
    EmitBranch("__gt_morestack_fp_walk");

    DefineLabel("__gt_morestack_fp_done");

    // --- Update GT fields ---
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X22, ARM64Register.X9, GtOffStackBase, 8); // new_base
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X21, ARM64Register.X9, GtOffStackSize, 8); // new_size
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X22, GtStackGuardMargin, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStackGuard, 8);

    // Update gt.sp if it points into the old stack (stale from last context switch)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffSp, 8);
    // ADD X12, X19, X20 → old_top (recompute, X12 may be stale)
    EmitWord(0x8B14026C);
    // CMP X0, X19 (old_base)
    EmitWord(0xEB13001F);
    EmitBranchCond(ARM64ConditionCode.Lo, "__gt_morestack_skip_sp");
    // CMP X0, X12 (old_top)
    EmitWord(0xEB0C001F);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_morestack_skip_sp");
    // ADD X0, X0, X10 (adjust by offset)
    EmitWord(0x8B0A0000);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffSp, 8);
    DefineLabel("__gt_morestack_skip_sp");

    // Free old stack — safe now that array literal buffers are always heap-allocated,
    // so no embedded stack pointers exist besides saved X29 (adjusted by FP walk).
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X19); // old_base
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X20); // old_size
    EmitCallImport("munmap");

    // --- Compute new SP and adjusted X29, then return ---
    // Recompute offset from callee-saved regs (X10 was clobbered by munmap)
    // offset = (X22 + X21 - X20) - X19
    // ADD X10, X22, X21
    EmitWord(0x8B1502CA); // ADD X10, X22, X21
    // SUB X10, X10, X20
    EmitWord(0xCB14014A); // SUB X10, X10, X20
    // SUB X10, X10, X19
    EmitWord(0xCB13014A); // SUB X10, X10, X19

    // new_SP = old_SP + offset
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X17, ARM64Register.X29, 24, 8); // old SP
    // ADD X17, X17, X10
    EmitWord(0x8B0A0231); // ADD X17, X17, X10

    // Load adjusted GT X29 from system frame (already adjusted by FP walk)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X29, 0, 8); // adjusted X29

    // Restore X0-X7 (function argument registers)
    for (int i = 0; i < 8; i++)
      EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[i], ARM64Register.X29, 80 + i * 8, 8);

    // Restore callee-saved regs
    // LDP X19, X20, [X29, #40]
    EmitWord(0xA9400000 | (5u << 15) | (20u << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X19));
    // LDP X21, X22, [X29, #56]
    EmitWord(0xA9400000 | (7u << 15) | (22u << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X21));
    // Restore scratch regs
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X15, ARM64Register.X29, 16, 8); // return addr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X16, ARM64Register.X29, 32, 8); // original LR

    // Destroy system frame (restores system X29/X30, we'll overwrite X29)
    EmitMovRegReg(ARM64Register.Sp, ARM64Register.X29);
    var imm7Post = (uint)(frameSize / 8) & 0x7Fu;
    EmitWord(0xA8C00000 | (imm7Post << 15) | (30u << 10) | (31u << 5) | 29u);

    // Set X29 to the adjusted GT FP (overwrite the system frame's X29)
    EmitMovRegReg(ARM64Register.X29, ARM64Register.X11);

    // Switch SP to new GT stack
    EmitWord(0x9100023F); // MOV SP, X17 (ADD SP, X17, #0)

    // Return to prologue (MOV X30, X16; STP X29, X30, ...)
    EmitWord(0xD65F01E0); // RET X15
  }

  // ===========================================================================================
  // Scheduler enhancements
  // ===========================================================================================

  /// <summary>
  /// __gt_process_pending_waiter(): Load P->pendingWaiter via TLS, clear it,
  /// and re-enqueue if non-null.
  /// </summary>
  /// <summary>
  /// __gt_process_pending_sync_req(): enqueue a sync request handed off by a worker GT that
  /// just parked (Go gopark's register-after-park). Run by the scheduler the GT switched to,
  /// so the GT is fully parked (ioYielded=1) before the request becomes discoverable to any
  /// completer — the request's waiter can therefore never be enqueued while still running.
  /// Same-P, single-M: only the parked GT's own scheduler reads this slot. No-op when empty,
  /// so it is safe to run from every scheduler drain point.
  /// </summary>
  private void EmitGtProcessPendingSyncReq() {
    EmitRuntimeFunctionStart("__gt_process_pending_sync_req", 0, 0x20);
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, POffPendingSyncReq, 8);
    EmitCbz(ARM64Register.X0, "__gt_ppsr_done");
    // Clear the slot, then enqueue the request (X0 = req) and wake the I/O sync worker so a
    // parked M is roused to drive it. The waiter is parked, so __io_op_done will enqueue it.
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, POffPendingSyncReq, 8);
    EmitBranchLink("__io_enqueue_sync_req");
    EmitGlobalLoadReg(ARM64Register.X9, "__io_sync_req_semaphore");
    EmitSemaWakeup(ARM64Register.X9);
    DefineLabel("__gt_ppsr_done");
    EmitRuntimeFunctionEnd();
  }

  private void EmitGtProcessPendingWaiter() {
    EmitRuntimeFunctionStart("__gt_process_pending_waiter", 0, 0x20);
    // [x29+16] = homed waiter (across the gate spin / __gt_enqueue call)

    // Drain any sync request handed off by a GT that parked onto this scheduler (this runs
    // at every scheduler drain point, so a parked worker GT's request is always registered).
    EmitBranchLink("__gt_process_pending_sync_req");

    // w = P->pendingWaiter; if null → done.
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, POffPendingWaiter, 8);
    EmitCbz(ARM64Register.X0, "__gt_ppw_done");

    // Clear P->pendingWaiter.
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, POffPendingWaiter, 8);

    // A main-thread waiter (stackBase==0) is never enqueued — it has no schedulable GT
    // stack and resumes by polling promise.status in its own __gt_await recheck loop.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X1, "__gt_ppw_done");

    // Home w across the gate spin and the enqueue call.
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);

    // ioYielded gate: spin until w.ioYielded == 1 so we never enqueue a GT whose register
    // context is still being saved on its own M (another M could then resume it onto a
    // half-saved register block — garbage / SIGILL). The awaiter cleared ioYielded=0 before
    // publishing promise.waiter, and __gt_context_switch sets it to 1 after the save; the
    // await idle path guarantees the awaiter always reaches a context switch, so this
    // terminates. Same bound as __netpoll_claim_done's post-claim spin.
    DefineLabel("__gt_ppw_spin");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // w
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffIoYielded, 8);
    EmitCbnz(ARM64Register.X1, "__gt_ppw_ready");
    EmitWord(0xD503203F); // YIELD
    EmitBranch("__gt_ppw_spin");

    DefineLabel("__gt_ppw_ready");
    EmitDmbIsh();
    // w.status = ready; __gt_enqueue(w).
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // w
    EmitMovRegImm(ARM64Register.X1, GtStatusReady);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitBranchLink("__gt_enqueue");

    DefineLabel("__gt_ppw_done");
    EmitRuntimeFunctionEnd();
  }

  // ===========================================================================================
  // kqueue-based async I/O
  // ===========================================================================================

  /// <summary>
  /// __io_init(): Create kqueue file descriptor. Called from _start after __gt_init.
  /// </summary>
  private void EmitIoInit() {
    EmitRuntimeFunctionStart("__io_init", 0, 0x30);
    EmitCallImport("kqueue");
    EmitGlobalStoreReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_shutdown(): Close kqueue fd.
  /// </summary>
  private void EmitIoShutdown() {
    EmitRuntimeFunctionStart("__io_shutdown", 0, 0x20);
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitCallImport("close");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_enqueue_completion(comp_x0): Add an IoCompletion to the done queue.
  /// Standard linked-list append (same pattern as sync request queue).
  /// </summary>
  private void EmitIoEnqueueCompletion() {
    EmitRuntimeFunctionStart("__io_enqueue_completion", 1, 0x30);

    // comp.next = 0
    EmitReloadArg(0);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, IoCompOffNext, 8);

    // if tail == NULL: head = tail = comp
    EmitGlobalLoadReg(ARM64Register.X1, "__io_done_tail");
    EmitCbnz(ARM64Register.X1, "__io_enqueue_comp_append");

    EmitReloadArg(0);
    EmitGlobalStoreReg(ARM64Register.X0, "__io_done_head");
    EmitGlobalStoreReg(ARM64Register.X0, "__io_done_tail");
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_enqueue_comp_append");
    EmitGlobalLoadReg(ARM64Register.X1, "__io_done_tail");
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X1, IoCompOffNext, 8); // tail.next = comp
    EmitGlobalStoreReg(ARM64Register.X0, "__io_done_tail"); // tail = comp
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_dequeue_completion() -> IoCompletion* in X0 (or NULL if empty).
  /// </summary>
  private void EmitIoDequeueCompletion() {
    EmitRuntimeFunctionStart("__io_dequeue_completion", 0, 0x30);

    EmitGlobalLoadReg(ARM64Register.X0, "__io_done_head");
    EmitCbnz(ARM64Register.X0, "__io_dequeue_comp_nonempty");
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_dequeue_comp_nonempty");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, IoCompOffNext, 8);
    EmitGlobalStoreReg(ARM64Register.X1, "__io_done_head");
    EmitCbnz(ARM64Register.X1, "__io_dequeue_comp_done");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitGlobalStoreReg(ARM64Register.X1, "__io_done_tail");
    DefineLabel("__io_dequeue_comp_done");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, IoCompOffNext, 8);
    EmitRuntimeFunctionEnd();
  }

  // ⚠ __io_complete_gt IS DELIBERATELY ABSENT FROM THIS BACKEND. macOS has no IOCP drain thread and
  // no sync worker that completes on its own: every completion on this target is published by the
  // scheduler-inline __io_poll_kqueue (async) or __io_op_done (sync), each of which does its own
  // publish-and-claim. The label was emitted here anyway and NEVER BRANCHED TO — not one
  // EmitBranchLink in this file named it — so it was ~40 bytes of a THIRD independent derivation of
  // the handshake, sitting where a future reader would reasonably copy it. It also still carried the
  // pre-netpoll unbounded `ioYielded` spin, which under the park protocol can no longer terminate:
  // a `Wait` parker is entitled to abort and never set the flag, so a live caller would have hung.
  // x86's twin IS live and DOES claim through __netpoll_claim / __netpoll_claim_done; that is the
  // one to read.

  /// <summary>
  /// __io_get_last_error() -> i64 in X0: returns gt->io_error_code for the current
  /// green thread. Used by the lowering to map raw OS error codes (Win32 GetLastError
  /// values captured by the sync worker on x86, or POSIX errno on macOS) to method-
  /// specific error-enum ordinals (notFound / accessDenied / etc).
  /// </summary>
  private void EmitIoGetLastError() {
    EmitRuntimeFunctionStart("__io_get_last_error", 0, 0x20);
    // X9 = current GT pointer
    EmitLoadCurrentGt(ARM64Register.X9);
    // X0 = gt->io_error_code
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_poll_kqueue(): Non-blocking poll of kqueue for ready events.
  /// For each ready event, dispatches based on KqCtx filter:
  ///   EVFILT_READ (-1)  → read(fd, buf, len), result = bytes read
  ///   EVFILT_WRITE (-2) → write(fd, buf, len), result = bytes written
  ///   KQCTX_CONNECT (-3) → getsockopt(SO_ERROR) check, result = fd or -2
  /// Stores result in waiting GT, sets status=ready, and re-enqueues.
  /// </summary>
  private void EmitIoPollKqueue() {
    // Stack: [x29+16] = nready, [x29+24] = loop index, [x29+32] = kevent ptr
    //        [x29+40..55] = zero timeout / reused for result and getsockopt buffers
    //        [x29+56] = saved ctx ptr, [x29+64] = saved waiter GT ptr
    //        [x29+72] = park state the wakeup was claimed FROM (__netpoll_claim -> __netpoll_claim_done)
    // Thread safety: green threads run across multiple worker OS threads, so the
    // kevent eventlist must NOT be shared. Each call reads into the calling P's own
    // 1 KiB slice of __io_kevent_bufs_base (computed below into [x29+0x50]); kqueue's
    // EV_ONESHOT delivery guarantees each event reaches exactly one kevent() call, so
    // disjoint per-P buffers ensure no two OS threads ever process — and double-free —
    // the same KqCtx.
    EmitRuntimeFunctionStart("__io_poll_kqueue", 0, 0x60);

    // Check if kqueue fd is valid (> 0)
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, "__io_poll_kqueue_ret");

    // Per-P eventlist buffer = __io_kevent_bufs_base + P->id * 1024  -> [x29+0x50]
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, POffId, 8);
    EmitLslImm(ARM64Register.X10, ARM64Register.X10, 10);
    EmitGlobalLoadReg(ARM64Register.X11, "__io_kevent_bufs_base");
    EmitAluRegReg(0x8B000000, ARM64Register.X10, ARM64Register.X11, ARM64Register.X10);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X29, 0x50, 8);

    // Build zero timeout on stack
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // tv_sec = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // tv_nsec = 0

    // kevent(kq, NULL, 0, __io_kevent_buf, 32, &zero_timeout) → nready
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitMovRegImm(ARM64Register.X1, 0); // changelist = NULL
    EmitMovRegImm(ARM64Register.X2, 0); // nchanges = 0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 0x50, 8); // eventlist = per-P buffer
    EmitMovRegImm(ARM64Register.X4, 32); // nevents = 32
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X29, 40, isAdd: true); // timeout
    EmitCallImport("kevent");

    // if nready <= 0: no events, return
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, "__io_poll_kqueue_ret");

    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8); // save nready

    // Loop index = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    DefineLabel("__io_poll_kqueue_loop");
    // if index >= nready: done
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // index
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // nready
    // CMP X0, X1
    EmitWord(0xEB01001F);
    EmitBranchCond(ARM64ConditionCode.Hs, "__io_poll_kqueue_ret");

    // kevent_ptr = &buffer[index * 32]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 0x50, 8); // per-P buffer
    // X0 already = index
    EmitMovRegImm(ARM64Register.X2, 32);
    EmitWord(0x9B027C00); // MUL X0, X0, X2
    EmitWord(0x8B000020); // ADD X0, X1, X0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // save kevent_ptr

    // Load udata (KqCtx ptr) from kevent at offset 24
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X0, 24, 8); // udata = ctx
    EmitCbz(ARM64Register.X9, "__io_poll_kqueue_next"); // skip if udata is NULL

    // Save ctx and waiter to stack (survives function calls)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X29, 56, 8); // save ctx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, KqCtxOffWaiter, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X29, 64, 8); // save waiter GT

    // Load filter to dispatch
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, KqCtxOffFilter, 8);

    // Dispatch: KQCTX_CONNECT (-3) needs special handling (getsockopt check).
    // EVFILT_READ (-1) and EVFILT_WRITE (-2) are notification-only — the actual
    // read()/write() is done by the resumed GT in __io_submit_read/write.
    EmitCmpImm(ARM64Register.X10, KQCTX_CONNECT);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_poll_kqueue_connect");

    // EVFILT_READ / EVFILT_WRITE: just wake up the GT (result=0, actual I/O done by caller)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_poll_kqueue_complete");

    // KQCTX_CONNECT: check getsockopt(fd, SOL_SOCKET, SO_ERROR)
    DefineLabel("__io_poll_kqueue_connect");
    // Initialize error buffer: [x29+40] = 0 (error value), [x29+44] = 4 (socklen)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitMovRegImm(ARM64Register.X0, 4);
    EmitWord(0xB9000000 | ((48u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // STR W0, [X29, #48] = socklen=4
    // getsockopt(fd, SOL_SOCKET, SO_ERROR, &err, &errlen)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 56, 8); // reload ctx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, KqCtxOffFd, 8); // fd
    EmitMovRegImm(ARM64Register.X1, SOL_SOCKET);
    EmitMovRegImm(ARM64Register.X2, SO_ERROR);
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X29, 40, isAdd: true); // &err
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X29, 48, isAdd: true); // &errlen
    EmitCallImport("getsockopt");
    // Load error value
    EmitWord(0xB9400000 | ((40u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0)); // LDR W0, [X29, #40]
    EmitCbnz(ARM64Register.X0, "__io_poll_kqueue_connect_err");
    // Success: result = fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 56, 8); // ctx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, KqCtxOffFd, 8);
    EmitBranch("__io_poll_kqueue_complete");

    DefineLabel("__io_poll_kqueue_connect_err");
    // Error: close socket, result = -2
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 56, 8); // ctx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, KqCtxOffFd, 8);
    EmitCallImport("close");
    EmitMovRegImm(ARM64Register.X0, -2);

    DefineLabel("__io_poll_kqueue_complete");
    // X0 = result (bytes transferred, fd, or error code)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save result

    // ⭐ STEP 1 — CLAIM THE WAKEUP (Go's netpollunblock, first half), BEFORE a single result field is
    // written. This one call replaces the three-way guard that used to stand here — main-thread,
    // self, and the ioYielded snapshot — and it replaces it because that third guard was UNABLE to do
    // its job: "ioYielded == 0" cannot distinguish a waiter that will self-detect from one that is
    // about to park, since those are the same instant. `Wait` versus `Parked` is exactly that
    // distinction, and it is decided atomically. See RuntimeEmitter.Netpoll.cs, which owns the state
    // machine for every target.
    //
    // ⚠ IT COMES FIRST, AND THAT ORDER IS THE FIX RATHER THAN A TIDY-UP. Publishing before claiming
    // left the word reading `Parked` for the whole of a healthy publish, which is indistinguishable
    // from a lost wakeup — so the recovery net raced live completers, and each rescue handed the
    // completer's late claim to the waiter's NEXT park. Claiming first makes the ownership visible,
    // and a claim we LOSE now means we write nothing at all: another party owns this GT's fields.
    // The getsockopt/close side effects above are deliberately outside the claim — they are the
    // kernel's business, not the waiter's, and must happen however the claim goes.
    //
    // Still non-blocking as far as the DECISION goes, which is what this site requires: it runs
    // inside the loop that DRIVES I/O, and a decision that waited on another thread would stall
    // every other pending completion — the livelock __io_op_done documents. It waits only AFTER it
    // has claimed a GT that has already committed to parking, and that wait is bounded by a
    // scheduling quantum because a committed parker runs straight-line to ioYielded=1.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // waiter GT
    EmitBranchLink("__netpoll_claim");
    EmitCbz(ARM64Register.X0, "__io_poll_kqueue_free_ctx");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8); // claimedFrom

    // STEP 2 — PUBLISH. The waiter comes out of the frame: X10 has been clobbered by every call
    // since it was homed at [x29+64], the claim above included.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 64, 8); // waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffIoResultVal, 8);

    // Set error = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffIoErrorCode, 8);

    // StoreStore RELEASE: publish io_result_val / io_error_code BEFORE the status that
    // advertises them. arm64 lets stores to different addresses retire out of order, so without
    // this a reader that observes status=ready can read the PREVIOUS value of io_result_val — 0 on a
    // first connect, which rt_ntc_check_result accepts as a valid fd and wraps in a __ManagedSocket.
    // A silent wrong answer, not a hang. __io_op_done, the sync-side twin of this completion, has
    // always taken this fence and says why; the kqueue side did not.
    //
    // ⚠ IT IS NOT MADE REDUNDANT BY THE STLR IN __netpoll_claim_done BELOW, though that one orders
    // all three of these stores before the word goes `Ready`. `status` is read by paths that never
    // armed a park word at all (__gt_timer_check's gate, the debug agent), and the release rule binds
    // only readers of the word. This fence is what those readers stand on.
    EmitDmbIsh();

    EmitMovRegImm(ARM64Register.X0, GtStatusReady);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffStatus, 8);

    // ⭐ STEP 3 — RELEASE the word and find out whether the enqueue is ours. Nothing below may read
    // or write this GT except through the returned pointer.
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 72, 8); // claimedFrom
    EmitBranchLink("__netpoll_claim_done");
    EmitCbz(ARM64Register.X0, "__io_poll_kqueue_free_ctx");
    EmitBranchLink("__gt_enqueue");

    DefineLabel("__io_poll_kqueue_free_ctx");
    // Free ctx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // ctx
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    DefineLabel("__io_poll_kqueue_next");
    // index++
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranch("__io_poll_kqueue_loop");

    DefineLabel("__io_poll_kqueue_ret");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Publish status=waiting, arm the one-shot kqueue registration built at [x29+keventSlot],
  /// and verify the kernel accepted it. Shared by every kqueue submit site; siteName names the
  /// submitting runtime function and keeps this helper's labels and panic text unique.
  ///
  /// THE ORDER IS LOAD-BEARING: the netpoll arm and the status store MUST both precede the kevent.
  /// EVFILT_READ/WRITE are level-triggered and the kqueue is process-wide, so on a pipe that
  /// already holds data the event is deliverable the instant kevent() publishes the registration
  /// and ANY other M polling that kqueue can reap it. A completer that reaps stores status=ready
  /// and then claims the park word; if we armed the registration first, that claim could land on a
  /// `Nil` word — "this GT is not waiting on anything" — and be declined, while our own store then
  /// overwrote the published status=ready with `waiting`. The GT would park with no registration,
  /// no run-queue entry and no future event, and the wakeup would be lost forever. x86 has always
  /// marked before it armed (EmitIoSubmitOverlappedCore, OPEN #66) and so does __io_submit_sync
  /// one screen up; the kqueue sites were the outlier.
  ///
  /// The DMB is what makes the reorder mean anything. Program order alone does not settle a
  /// write-write race: our status store and the completer's are to the SAME word, so what
  /// decides the winner is which reaches the coherence point last, and arm64 is free to hold
  /// ours in a store buffer across the syscall — the same lost wakeup with the instructions
  /// already in the right order. The fence forces it out before the SVC publishes the knote.
  ///
  /// It is NOT an acquire/release pair with anything: the completer never READS status, it
  /// overwrites it. The fence left in __io_poll_kqueue is a different mechanism for a different
  /// hazard — a StoreStore ordering io_result_val before the status that advertises it. Two
  /// fences, two reasons; do not collapse them into one story.
  ///
  /// ⚠ THERE WERE THREE UNTIL THE NETPOLL PORT, and this comment said so for one rung after it
  /// stopped being true. __io_poll_kqueue's StoreLoad existed to order its status store before its
  /// own `ioYielded` LOAD, and that load no longer happens there: it moved inside
  /// __netpoll_claim_done, behind a claim whose LDAXR/STLXR supplies the same pairing. The fence
  /// went with the load it was fencing.
  /// </summary>
  private void EmitMarkWaitingAndArmKevent(int keventSlot, string siteName) {
    // netpoll: take ownership of this GT's wakeup BEFORE the registration a completer can see.
    // From the kevent() below onwards a completer may claim it; finding `Wait` it declines the
    // enqueue, because we are still running and can still abort. See RuntimeEmitter.Netpoll.cs.
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitBranchLink("__netpoll_arm");

    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitDmbIsh();

    // kevent(kq, changelist, nchanges=1, eventlist=NULL, nevents=0, timeout=NULL)
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, keventSlot, isAdd: true);
    EmitMovRegImm(ARM64Register.X2, 1);
    EmitMovRegImm(ARM64Register.X3, 0);
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitCallImport("kevent");

    // A registration the kernel REFUSED parks this GT on an event that can never be delivered,
    // with exactly the signature of the lost wakeup above — a silently hung worker. Unchecked,
    // one wedge would have two indistinguishable causes and every future investigation would
    // have to rule this one out by reading. Say so instead.
    var armedLabel = $"{siteName}_kevent_armed";
    var msgLabel = $"__io_panic_msg_{siteName}_kevent";
    DefineSymdata(msgLabel, System.Text.Encoding.UTF8.GetBytes(
      $"PANIC: kevent(EV_ADD) rejected the registration in {siteName}\n\0"));
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Ge, armedLabel);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, msgLabel);
    EmitBranchLink("mrt_panic"); // never returns

    DefineLabel(armedLabel);
  }

  /// <summary>
  /// Branch to target when a completer has CLAIMED the current GT's wakeup. Every point in the park
  /// handshake that must not commit to the next step without re-asking asks with this, and this is
  /// the ONLY question any of them is allowed to ask — see RuntimeEmitter.Netpoll.cs's release rule.
  ///
  /// ⚠ IT USED TO READ `status`, AND THAT WAS THE SECOND CHANNEL THE PROTOCOL COULD NOT AFFORD.
  /// A completer writes io_result_val and status while it OWNS the word but has not released it, so
  /// `status != waiting` is true for a window in which the word is not yet `Ready`; a waiter that
  /// left the park there ran __netpoll_park_done, released a word its completer was still in flight
  /// toward, and handed the late claim to its NEXT park. Measured, not reasoned:
  /// MAXON_GT_CLAIM_DELAY_MS=5 took scripts/netpoll-park-driver from 5/5 clean to 0/5.
  ///
  /// ⚠ THE DMB IS GONE WITH THE LOAD IT WAS FENCING, NOT BECAUSE THE ORDERING STOPPED MATTERING.
  /// It was the acquire half of the completer's release fence, hand-rolled because a control
  /// dependency orders a later STORE on arm64 and never a later LOAD. That acquire MOVED INSIDE
  /// __netpoll_woken, which LDARs the very word __netpoll_claim_done's STLR released — a genuine
  /// acquire/release pair on one location, which is strictly stronger than a fence standing in for
  /// one. This is the SECOND fence retired for exactly this reason; the first was
  /// __io_poll_kqueue's StoreLoad, whose `ioYielded` load moved inside __netpoll_claim_done.
  ///
  /// ⚠ THIS IS A BL, NOT A LOAD: it clobbers the whole caller-saved set, not just X0/X9 as the
  /// status read did. Every value a caller needs across it must already be homed in the frame —
  /// which at all three call sites in EmitGtParkForIoCompletion it is (nextGtSlot is stored before
  /// the first ask and reloaded from the frame after).
  /// </summary>
  private void EmitBranchIfNetpollWoken(string target) {
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitBranchLink("__netpoll_woken");
    EmitCbnz(ARM64Register.X0, target);
  }

  /// <summary>
  /// Park the current GT until the kqueue registration it just armed completes, running other
  /// runnable GTs meanwhile. nextGtSlot is a caller-owned 8-byte stack slot used to home the
  /// successor GT across calls. Every path out of the park converges on {labelPrefix}_park_done
  /// and FALLS THROUGH to whatever the caller emits next, which must be its resume path.
  ///
  /// Shared by __io_submit_read/write and maxon_net_tcp_connect. They carried two copies of
  /// this handshake and only one of them had the pre-park re-check, which is precisely the
  /// drift the handshake exists to prevent; one copy is the fix for that.
  /// </summary>
  private void EmitGtParkForIoCompletion(string labelPrefix, int nextGtSlot) {
    DefineLabel($"{labelPrefix}_try_dequeue");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, $"{labelPrefix}_has_next");

    // Nothing runnable: drive the scheduler and the I/O engine inline on our own stack, then
    // self-detect. We are still `Wait`, so a completer that reaps our event claims the word,
    // declines the enqueue and leaves the wakeup for us to find right here — which is safe
    // precisely because we never leave this loop without re-asking the word. The main OS
    // thread, which is never enqueued at all, only ever resumes this way.
    EmitDriveSchedulerAndIo();
    EmitBranchIfNetpollWoken($"{labelPrefix}_park_done");
    EmitBranch($"{labelPrefix}_try_dequeue");

    DefineLabel($"{labelPrefix}_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, nextGtSlot, 8);

    // Cheap pre-check: if a completer has already claimed our wakeup there is no point taking the
    // commit atomic. This is Go's `CAS(pdReady -> pdNil)` self-detect arm, in the shape a word that
    // lives in the GT allows, and the commit below is what actually decides — but it reads the SAME
    // word the commit does, so taking it is a decision about the same fact, not a second opinion
    // about a different one.
    EmitBranchIfNetpollWoken($"{labelPrefix}_woke_while_parking");

    // Fault injection: widen the commit window on demand. Inert unless the environment armed it,
    // and it must sit exactly HERE — after the last self-detect and before the commit — so a
    // completer is guaranteed to reap while we are still able to abort.
    EmitBranchLink("__netpoll_inject_delay");

    // ⭐ COMMIT THE PARK, AND LET IT FAIL (Go's netpollblockcommit). Everything from here to
    // __gt_context_switch's ioYielded=1 is straight-line code, so a completer that claims `Parked`
    // knows we cannot turn around and may wait for the context save. If the commit fails, a
    // completer has already taken the wakeup, so we must NOT park: abort exactly as if the
    // pre-check above had caught it.
    //
    // ⚠ "TAKEN" IS NOT "PUBLISHED": the CAS fails against `Claiming` too, and that means the results
    // are still going in. The abort is safe only because it converges on {labelPrefix}_park_done,
    // whose __netpoll_park_done waits `Claiming` out before the caller's resume path reads
    // io_result_val. That is one more reason this park has a single exit.
    //
    // The MAIN OS THREAD never commits — it has no schedulable stack and nothing ever enqueues it,
    // so "committed to park" is not a state it can be in — and __netpoll_commit is where that is
    // now decided, for every backend at once. It answers non-zero without touching the word, we
    // switch to the successor below exactly as a committed GT does, and we resume by asking the
    // word each time round. The stackBase==0 test that used to branch AROUND this call is gone with
    // it: one rule, one place. See RuntimeEmitter.Netpoll.cs's EmitNetpollCommit.
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitBranchLink("__netpoll_commit");
    EmitCbz(ARM64Register.X0, $"{labelPrefix}_woke_while_parking");

    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, nextGtSlot, 8);
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, nextGtSlot, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

    // Switched back in — "resumed" still does not imply "completed", and the two callers differ.
    EmitBranchIfNetpollWoken($"{labelPrefix}_park_done");

    // The MAIN OS THREAD gets here routinely: it is P0's scheduler GT, so every idle worker GT
    // and every finishing __gt_trampoline switches back to &P.mainThread as a matter of course,
    // and without this re-check it fell straight through to the read() with the fd not ready.
    // It never committed, so it simply goes round again.
    //
    // stackBase is the right question here even though __netpoll_commit now owns it for COMMITTING,
    // because this asks something else: "which of the two non-woken states am I in?". Past the
    // commit, stackBase != 0 means the CAS won and the word is `Parked`; stackBase == 0 means the
    // commit declined and the word is still `Wait`. The panic below depends on exactly that.
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, $"{labelPrefix}_try_dequeue");

    // A COMMITTED WORKER GT cannot: the only two things that enqueue a `Parked` GT are
    // __netpoll_claim/__netpoll_claim_done and the recovery net, and BOTH reach the enqueue only by
    // winning a CAS off `Parked` and leaving the word `Ready` — which the ask above would have seen.
    // (The claim pair passes through `Claiming` on the way, and the ask declines that too, so a GT
    // resuming here cannot be one whose completer is merely mid-publish.) Resuming here therefore
    // means some OTHER resumer picked up a parked I/O waiter — the hazard B1 could only describe
    // ("properties of today's code, not invariants"), now a checked one. Say so instead of looping:
    // a second commit attempt would find `Parked` rather than `Wait`, fail, and fall through to an
    // I/O read on an fd that was never signalled — a silent wrong answer.
    var resumeMsg = $"__io_panic_msg_{labelPrefix}_resume";
    DefineSymdata(resumeMsg, System.Text.Encoding.UTF8.GetBytes(
      $"PANIC: {labelPrefix} resumed a committed I/O park with no completion\n\0"));
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, resumeMsg);
    EmitBranchLink("mrt_panic"); // never returns

    // Our I/O completed while we were picking a successor, or a completer claimed the wakeup out
    // from under the commit CAS. Either way we must NOT park: hand the GT we dequeued back to the
    // run queue — another M can have it — and go straight to the I/O. Its status is untouched here
    // (the running store is on the parking path only), so it goes back exactly as __gt_dequeue
    // produced it.
    DefineLabel($"{labelPrefix}_woke_while_parking");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, nextGtSlot, 8);
    EmitBranchLink("__gt_enqueue");

    // Single exit, so the park word is released on every path out of the park — Go's
    // `old := gpp.Swap(pdNil)` with its "corrupted polldesc" check. Falls through into
    // resumedLabel, which the caller defines next.
    DefineLabel($"{labelPrefix}_park_done");
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitBranchLink("__netpoll_park_done");
  }

  /// <summary>
  /// __io_submit_read(fd_x0, buf_x1, len_x2): Register EVFILT_READ with kqueue, yield GT.
  /// When kqueue signals readiness, __io_check_completions performs the actual read()
  /// and resumes the GT.
  /// </summary>
  private void EmitIoSubmitRead() {
    EmitIoSubmitReadWrite("__io_submit_read", EVFILT_READ);
  }

  /// <summary>
  /// __io_submit_write(fd_x0, buf_x1, len_x2): Register EVFILT_WRITE with kqueue, yield GT.
  /// </summary>
  private void EmitIoSubmitWrite() {
    EmitIoSubmitReadWrite("__io_submit_write", EVFILT_WRITE);
  }

  /// <summary>
  /// Common implementation for __io_submit_read and __io_submit_write.
  /// Allocates a KqCtx, registers with kqueue via kevent(), sets GT status=waiting, yields.
  /// </summary>
  private void EmitIoSubmitReadWrite(string functionName, int filter) {
    // Stack: [x29+16]=fd, [x29+24]=buf, [x29+32]=len, [x29+40]=ctx, [x29+48]=next GT
    // [x29+56..119] = struct kevent (64 bytes, we use 32 for one event but align to 64)
    EmitRuntimeFunctionStart(functionName, 3, 0x80);

    // Check cancel flag
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffCancelFlag, 8);
    EmitCbnz(ARM64Register.X0, $"{functionName}_cancelled");

    // Allocate KqCtx
    EmitMovRegImm(ARM64Register.X0, KqCtxSize);
    EmitCallMmRawAlloc();
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // save ctx

    // Fill ctx: fd, buf, len, waiter=current, filter
    // Note: EmitReloadArg(i) loads into AbiArgRegs[i] (X0, X1, X2, ...)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // X9 = ctx
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, KqCtxOffFd, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // X0 = buf
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, KqCtxOffBuf, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = len
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, KqCtxOffLen, 8);
    EmitLoadCurrentGt(ARM64Register.X0); // clobbers X9
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 40, 8); // reload ctx
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, KqCtxOffWaiter, 8);
    EmitMovRegImm(ARM64Register.X0, filter);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, KqCtxOffFilter, 8);

    // Build struct kevent on stack at [x29+56]:
    //   ident (8) = fd, filter (2) = EVFILT_READ/WRITE, flags (2) = EV_ADD|EV_ONESHOT,
    //   fflags (4) = 0, data (8) = 0, udata (8) = ctx_ptr
    // struct kevent total = 32 bytes
    // Zero the kevent area first
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);

    // ident = fd (at offset 0)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    // filter (int16 at offset 8) + flags (uint16 at offset 10)
    // Pack: filter | (flags << 16) as a 32-bit store at offset 8
    var filterAndFlags = (uint)((ushort)filter | ((EV_ADD | EV_ONESHOT) << 16));
    EmitMovRegImm(ARM64Register.X0, (long)filterAndFlags);
    // STR W0, [X29, #64] — store 32-bit value at kevent+8
    EmitWord(0xB9000000 | ((64u / 4) << 10) | (Reg(ARM64Register.X29) << 5) | Reg(ARM64Register.X0));
    // udata = ctx_ptr (at offset 24)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // ctx
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8); // kevent+24 = udata

    EmitMarkWaitingAndArmKevent(56, functionName);

    EmitGtParkForIoCompletion(functionName, 48);

    // Resumed after kqueue notification (via context switch or direct)
    DefineLabel($"{functionName}_resumed");
    // Perform the actual I/O now that kqueue told us the fd is ready
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 32, 8); // len
    if (filter == EVFILT_READ)
      EmitCallImport("read");
    else
      EmitCallImport("write");
    // X0 = bytes transferred or -1
    EmitRuntimeFunctionEnd();

    // Cancelled path
    DefineLabel($"{functionName}_cancelled");
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, 995);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Inline trace helpers -- emit ARM64 machine code sequences for trace output.
  // Used by COW and other runtime functions that need trace output inline.
  // ==========================================================================

  /// <summary>
  /// Print "TypeName " from packed tag at [x29+ptrSlot], then " #N" where N = alloc_id.
  /// </summary>
  private void EmitTraceTagAndId(int ptrSlot) {
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, ptrSlot, 8);
    EmitBranchLink("mm_trace_print_packed_tag");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_hash");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, ptrSlot, 8);
    // LDUR X0, [X0, #-24]
    EmitWord(0xF85E8000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    // LSR X0, X0, #16
    EmitWord(0xD350FC00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    EmitBranchLink("mm_trace_print_i64");
  }

  /// <summary>
  /// Print " rc=N" from user_ptr at [x29+ptrSlot]. rcSubtract adjusts displayed value.
  /// </summary>
  private void EmitTraceRc(int ptrSlot, int rcSubtract = 0) {
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_rc_eq");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, ptrSlot, 8);
    // LDUR X0, [X0, #-8]
    EmitWord(0xF85F8000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0));
    if (rcSubtract > 0) EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, rcSubtract, isAdd: false);
    EmitBranchLink("mm_trace_print_i64");
  }

  /// <summary>
  /// Print " size=N" from size value at [x29+sizeSlot].
  /// </summary>
  private void EmitTraceSize(int sizeSlot) {
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_size_eq");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, sizeSlot, 8);
    EmitBranchLink("mm_trace_print_i64");
  }

  /// <summary>
  /// Print " [scope]" if scope is non-null, then print newline.
  /// </summary>
  private void EmitMmTraceScopeAndNewline(string skipLabel, int scopeSlot) {
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, scopeSlot, 8);
    _condBranchFixups.Add((_code.Count, skipLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, skip
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_lbracket");
    EmitBranchLink("mm_trace_print_tag");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, scopeSlot, 8);
    EmitBranchLink("mm_trace_print_tag");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_rbracket");
    EmitBranchLink("mm_trace_print_tag");
    DefineLabel(skipLabel);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__mm_tag_newline");
    EmitBranchLink("mm_trace_print_tag");
  }

  /// <summary>
  /// Emit inline trace: indent + tag + "TypeName #N rc=R [scope]\n".
  /// </summary>
  private void EmitInlineTrace(string tagLabel, string uniquePrefix, int ptrSlot, int scopeSlot,
      bool printRc = true, int rcSubtract = 0, int? sizeSlot = null) {
    EmitBranchLink("mm_trace_print_indent");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, tagLabel);
    EmitBranchLink("mm_trace_print_tag");
    EmitTraceTagAndId(ptrSlot);
    if (printRc) EmitTraceRc(ptrSlot, rcSubtract);
    if (sizeSlot.HasValue) EmitTraceSize(sizeSlot.Value);
    EmitMmTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  // ===========================================================================
  // Fault handler glue for arm64-macOS. See RuntimeEmitter.FaultHandler for the
  // shared logic; this file emits the platform-specific install / prolog / epilog.
  // Layout constants are sourced from <sys/_types/_ucontext64.h>,
  // <arm/_mcontext.h>, and <sys/signal.h> on current macOS.
  // ===========================================================================

  // ucontext_t.uc_mcontext lives at +0x30 (after onstack/sigmask/uc_stack/uc_link/uc_mcsize).
  private const int UcontextOffMcontext = 0x30;

  // mcontext64.__ss begins at +0x10 (after __es, which is arm_exception_state64:
  // __far u64 + __esr u32 + __exception u32 = 16 bytes). Within __ss:
  // __fp=+0xE8, __sp=+0xF8, __pc=+0x100. Final offsets relative to mcontext64
  // (verified against offsetof() on macOS arm64):
  private const int McontextOffSsFp = 0xF8;
  private const int McontextOffSsSp = 0x108;
  private const int McontextOffSsPc = 0x110;

  // siginfo_t.si_code is the 32-bit field at +0x08 (after si_signo).
  private const int SiginfoOffSiCode = 0x08;

  // sigaction struct (Darwin): sa_handler@+0, sa_tramp@+0x08, sa_mask@+0x10, sa_flags@+0x14.
  // sa_tramp left NULL — the kernel substitutes libsystem_platform's __sigtramp by default.
  private const int SigactionOffHandler = 0x00;
  private const int SigactionOffFlags = 0x14;

  // sigaltstack (stack_t): ss_sp@+0, ss_size@+0x08, ss_flags@+0x10.
  private const int SigstackOffSp = 0x00;
  private const int SigstackOffSize = 0x08;
  private const int SigstackOffFlags = 0x10;

  // SA flags and signal numbers (Darwin).
  private const long SaSiginfo = 0x40;
  private const long SaOnstack = 0x01;
  private const long SaRestart = 0x02;
  private const int SignalSegv = 11;
  private const int SignalBus = 10;
  private const int SignalFpe = 8;
  // SIGTRAP — a BRK #0 raises it. The debug agent owns this signal (distinct from the fault
  // handler's SEGV/BUS/FPE), so its handler never contends with the fault path.
  private const int SignalTrap = 5;
  // Terminating signals handled by the Go-style dieFromSignal thunk so the process
  // exits cleanly (correct WIFSIGNALED status) instead of wedging in macOS 'UE' state.
  private const int SignalTerm = 15;
  private const int SignalInt = 2;
  // sigprocmask(how) — Darwin: SIG_BLOCK=1, SIG_UNBLOCK=2, SIG_SETMASK=3.
  private const int SigUnblock = 2;
  // signal(sig, SIG_DFL) resets a signal to its default disposition. Used by the debug trap handler's
  // chain path (an unknown BRK, or a fault after agent shutdown): reset SIGTRAP to default and
  // re-raise so the process terminates via the default disposition instead of re-executing the BRK
  // forever — the arm64 counterpart of the x86 handler returning EXCEPTION_CONTINUE_SEARCH.
  private const int SigDfl = 0;

  // Debug-agent `.text` patching (P3b). macOS/arm64 pages are 16 KB and a 4-byte aligned instruction
  // never spans a page boundary, so one page covers the BRK. PROT_READ|WRITE to patch it in,
  // PROT_READ|EXEC to restore W^X. pageBase = addr & ~(pageSize-1), computed as (addr >> 14) << 14.
  private const long DbgArm64PageSize = 0x4000;
  private const int DbgArm64PageShift = 14;
  private const long DbgProtReadWrite = 0x3;
  private const long DbgProtReadExec = 0x5;
  private const int DbgBreakpointPatchLen = 4; // a BRK is one 4-byte instruction.

  // 32 KB altstack — enough room for a handful of diagnostic frames.
  private const long SigaltstackSize = 0x8000;

  // FPE_INTDIV; FPE_INTOVF is the only other code we'd see, so a single sentinel
  // is enough to distinguish.
  private const long FpeIntDiv = 7;

  internal void EmitInstallFaultHandler(string thunkLabel) {
    // Publish &__gt_fault_diagnostic for the shared handler's redirect target.
    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, "__gt_fault_diagnostic");
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_fault_diagnostic_addr");

    EmitInstallSignalHandler(thunkLabel, SaSiginfo | SaOnstack | SaRestart,
        SignalSegv, SignalFpe, SignalBus);

    // SA_ONSTACK is only half of the contract — the calling thread also needs an altstack to be
    // switched TO, and this install runs on the main thread. Worker Ms register their own.
    EmitInstallThreadSigaltstack();
  }

  /// Install a fresh sigaltstack for the calling OS thread.
  ///
  /// sigaction (installed once on the main thread in EmitInstallFaultHandler) is
  /// process-global, but sigaltstack is PER-THREAD on POSIX. A SA_ONSTACK handler
  /// with no altstack registered for THIS pthread runs on the (possibly exhausted,
  /// and on a green thread always TINY) thread stack. So EVERY thread that can run
  /// Maxon code needs one: the main thread (from EmitInstallFaultHandler) and each
  /// worker M (from __sched_worker_loop) — without it a transient SEGV/BUS/FPE on a
  /// worker thread is mis-handled and escalates to a fatal SIGILL (process-wide)
  /// instead of a clean panic + exit. mmap leaks for the thread's lifetime (correct:
  /// it must outlive every fault). Mirrors the self-hosted emitArm64SchedWorkerLoop
  /// per-thread altstack (Arm64MacosGreenThread.maxon).
  ///
  /// Clobbers X0, X1 and the call-clobbered set; callers must not have live values
  /// in those across this call. X28 (=P*) is NOT touched.
  private void EmitInstallThreadSigaltstack() {
    // Carve 0x20 bytes of scratch under SP for the sigaltstack struct (stack_t).
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: false);

    EmitMovRegImm(ARM64Register.X1, SigaltstackSize);
    EmitMmapAnon();
    EmitStoreToSp(SigstackOffSp, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X1, SigaltstackSize);
    EmitStoreToSp(SigstackOffSize, ARM64Register.X1);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitStoreIndirect(ARM64Register.Sp, SigstackOffFlags, ARM64Register.X1, 4);

    EmitMovRegReg(ARM64Register.X0, ARM64Register.Sp); // &ss
    EmitMovRegImm(ARM64Register.X1, 0);                // oss = NULL
    EmitCallImport("sigaltstack");

    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: true);
  }

  internal void EmitFaultHandlerProlog(string thunkLabel, string sharedHandlerLabel) {
    // void thunk(int sig, siginfo_t* info, void* ucontext);  AAPCS64: X0/X1/X2.
    // EmitRuntimeFunctionStart spills X0..X2 at [fp+16], [fp+24], [fp+32].
    EmitRuntimeFunctionStart(thunkLabel, 3, 0x40);

    EmitLoadFromStack(ARM64Register.X0, 16, 8);
    EmitCmpImm(ARM64Register.X0, SignalSegv);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_ftp_segv");
    EmitCmpImm(ARM64Register.X0, SignalBus);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_ftp_bus");
    EmitCmpImm(ARM64Register.X0, SignalFpe);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_ftp_fpe");

    // Unrecognised signal — return without rewriting. The kernel will retry the
    // faulting instruction; the OS default disposition takes over on the next
    // delivery once we've exhausted any pending custom handlers.
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_ftp_segv");
    EmitMovRegImm(ARM64Register.X0, FaultCodeNilDeref);
    EmitBranch("__gt_ftp_code_chosen");
    DefineLabel("__gt_ftp_bus");
    EmitMovRegImm(ARM64Register.X0, FaultCodeNilDeref);
    EmitBranch("__gt_ftp_code_chosen");
    DefineLabel("__gt_ftp_fpe");
    EmitLoadFromStack(ARM64Register.X1, 24, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X1, SiginfoOffSiCode, 4);
    EmitCmpImm(ARM64Register.X1, FpeIntDiv);
    EmitBranchCond(ARM64ConditionCode.Eq, "__gt_ftp_fpe_div");
    EmitMovRegImm(ARM64Register.X0, FaultCodeIntOverflow);
    EmitBranch("__gt_ftp_code_chosen");
    DefineLabel("__gt_ftp_fpe_div");
    EmitMovRegImm(ARM64Register.X0, FaultCodeDivZero);

    DefineLabel("__gt_ftp_code_chosen");
    // X0 = faultCode. Load (pc, sp, fp) from mcontext->__ss into (X1, X2, X3).
    EmitLoadFromStack(ARM64Register.X4, 32, 8);
    EmitLoadIndirect(ARM64Register.X4, ARM64Register.X4, UcontextOffMcontext, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X4, McontextOffSsPc, 8);
    EmitLoadIndirect(ARM64Register.X2, ARM64Register.X4, McontextOffSsSp, 8);
    EmitLoadIndirect(ARM64Register.X3, ARM64Register.X4, McontextOffSsFp, 8);

    EmitBranchLink(sharedHandlerLabel);
    // X0 = sentinel. Fall through to epilog.
  }

  internal void EmitFaultHandlerEpilog() {
    // X0 = sentinel from the shared handler. Anything nonzero means "don't recover".
    EmitCbnz(ARM64Register.X0, "__gt_fte_dont_recover");

    // Guard the raw-X28 deref: the io-sync worker pthread runs with X28=0 (it has no
    // P*), and a fault delivered before a worker M finishes setting X28 would also see
    // a zero/garbage P. Dereferencing P->currentGt through a null/garbage P here would
    // recurse inside the handler and escalate to a fatal SIGILL. If X28==0 (or
    // P->currentGt==0) there is no recoverable redirect — take the SIG_DFL path.
    EmitCbz(ARM64Register.X28, "__gt_fte_dont_recover");

    // Recover: copy gt.fault_redirect_* into mcontext->__ss.{pc,sp,fp}.
    EmitLoadIndirect(ARM64Register.X9, ARM64Register.X28, POffCurrentGt, 8);
    EmitCbz(ARM64Register.X9, "__gt_fte_dont_recover");
    EmitLoadIndirect(ARM64Register.X10, ARM64Register.X9, GtOffFaultRedirectRip, 8);
    EmitLoadIndirect(ARM64Register.X11, ARM64Register.X9, GtOffFaultRedirectRsp, 8);
    EmitLoadIndirect(ARM64Register.X12, ARM64Register.X9, GtOffFaultRedirectFp, 8);

    EmitLoadFromStack(ARM64Register.X13, 32, 8);
    EmitLoadIndirect(ARM64Register.X13, ARM64Register.X13, UcontextOffMcontext, 8);
    EmitStoreIndirect(ARM64Register.X13, McontextOffSsPc, ARM64Register.X10, 8);
    EmitStoreIndirect(ARM64Register.X13, McontextOffSsSp, ARM64Register.X11, 8);
    EmitStoreIndirect(ARM64Register.X13, McontextOffSsFp, ARM64Register.X12, 8);

    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_fte_dont_recover");
    // signal(sig, SIG_DFL); raise(sig); — fatal signals exit via default disposition.
    EmitLoadFromStack(ARM64Register.X0, 16, 8);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("signal");
    EmitLoadFromStack(ARM64Register.X0, 16, 8);
    EmitCallImport("raise");
    EmitRuntimeFunctionEnd();
  }

  /// Go dieFromSignal handler for the asynchronous terminating signals SIGTERM(15)
  /// and SIGINT(2). One thunk serves both — it reads the signal number from x0
  /// (spilled at [fp+16]). The body is strictly async-signal-safe (sigprocmask,
  /// sigaction, raise only) — it must NOT touch the pthread wake primitive (could
  /// self-deadlock if the interrupted M holds the wake mutex), X28/P->currentGt, or
  /// the mcontext. It unblocks the signal, resets it to SIG_DFL, then re-raises so
  /// the kernel's default action (terminate) fires with the correct disposition,
  /// rather than the process wedging in idle workers / munmap on kill. Mirrors Go's
  /// runtime.dieFromSignal (os_darwin / signal_unix.go).
  internal void EmitDieFromSignalThunk(string thunkLabel) {
    // void thunk(int sig, siginfo_t* info, void* ucontext); AAPCS64 X0/X1/X2 spilled
    // at [fp+16/+24/+32]. Carve 0x20 SP scratch: [SP+0]=sigset mask (4B), [SP+0x10..]
    // = zeroed struct sigaction (sa_handler=SIG_DFL).
    EmitRuntimeFunctionStart(thunkLabel, 3, 0x40);
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: false);

    // mask = 1 << (sig - 1)  (Darwin sigset_t is a single 32-bit word)
    EmitLoadFromStack(ARM64Register.X1, 16, 8);                                 // sig
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: false);         // sig - 1
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitWord(0x9AC02000 | ((uint)Reg(ARM64Register.X1) << 16)
             | ((uint)Reg(ARM64Register.X0) << 5) | (uint)Reg(ARM64Register.X0)); // LSLV X0, X0, X1
    EmitStoreIndirect(ARM64Register.Sp, 0x00, ARM64Register.X0, 4);             // mask (32-bit)

    // sigprocmask(SIG_UNBLOCK, &mask, NULL) — make the signal deliverable
    EmitMovRegImm(ARM64Register.X0, SigUnblock);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.Sp);                          // &mask
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitCallImport("sigprocmask");

    // Zero the struct sigaction at [SP+0x10] and set sa_handler = SIG_DFL (= 0).
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitStoreToSp(0x10, ARM64Register.X0);                                      // sa_handler
    EmitStoreToSp(0x18, ARM64Register.X0);                                      // sa_tramp
    EmitStoreToSp(0x10 + SigactionOffFlags, ARM64Register.X0);                  // sa_mask + sa_flags (8B zero)

    // sigaction(sig, &act, NULL) — reset to default disposition
    EmitLoadFromStack(ARM64Register.X0, 16, 8);                                 // sig
    EmitAddSubImm(ARM64Register.X1, ARM64Register.Sp, 0x10, isAdd: true);       // &act
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitCallImport("sigaction");

    // raise(sig) — re-deliver; kernel default action now terminates the process.
    EmitLoadFromStack(ARM64Register.X0, 16, 8);
    EmitCallImport("raise");

    // Unreached (raise terminates), but unwind the scratch for well-formedness.
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: true);
    EmitRuntimeFunctionEnd();
  }

  /// Install the dieFromSignal thunk for SIGTERM(15) and SIGINT(2). These are ASYNCHRONOUS signals
  /// that arrive on whatever thread the kernel picks and whose handler only sets the process on its
  /// way out, so the normal stack really is fine and SA_ONSTACK would buy nothing. Process-global, so
  /// installing once on the main thread in __gt_init covers every M. Shares its sigaction machinery
  /// with the fault and trap installs via <see cref="EmitInstallSignalHandler"/>.
  internal void EmitInstallDieFromSignalHandler(string thunkLabel) {
    EmitInstallSignalHandler(thunkLabel, SaSiginfo | SaRestart, SignalTerm, SignalInt);
  }

  /// <summary>
  /// Install the debug agent's trap handler: sigaction(SIGTRAP, &amp;thunk). SIGTRAP is distinct from
  /// the fault handler's SEGV/BUS/FPE, so the two coexist by owning different signals — the POSIX
  /// analogue of the Windows VEH chaining. Called only from __dbg_init.
  ///
  /// SA_ONSTACK, for the same reason the fault handler has it: the hazard is not an OVERFLOWED stack,
  /// it is a TINY stack meeting a LARGE signal frame. A green-thread stack is GtMaxonStackSize of
  /// Maxon frames, and Darwin/arm64's siginfo + ucontext + mcontext carries 528 bytes of NEON state
  /// alone before the handler's own frames — so the kernel must be switched to the per-thread
  /// altstack here exactly as it is for a fault. (This handler ran on the green-thread stack until
  /// P4d-GT-STACK; the Windows twin of that exposure is what GtOsFaultReserve exists for, VEH having
  /// no altstack to switch to.)
  /// </summary>
  internal void EmitInstallTrapHandler(string thunkLabel) {
    EmitInstallSignalHandler(thunkLabel, SaSiginfo | SaOnstack | SaRestart, SignalTrap);
  }

  /// <summary>
  /// Register <paramref name="thunkLabel"/> as a handler for each of <paramref name="signals"/> with
  /// <paramref name="flags"/>. Shared by all three installs — the fault handler (SEGV/FPE/BUS), the
  /// dieFromSignal thunk (SIGTERM/SIGINT) and the debug agent's trap handler (SIGTRAP): identical
  /// sigaction machinery, different signal set, and SA_ONSTACK is the one flag they do not all agree
  /// on, so it is a PARAMETER rather than a second copy of this code. Process-global, so installing
  /// once on the main thread covers every M.
  /// </summary>
  private void EmitInstallSignalHandler(string thunkLabel, long flags, params int[] signals) {
    // Carve 0x20 of SP scratch for the struct sigaction (24 bytes).
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: false);

    // Zero the struct, set sa_handler = &thunk, sa_flags = SA_SIGINFO|SA_RESTART.
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitStoreToSp(0x00, ARM64Register.X0); // sa_handler (overwritten below)
    EmitStoreToSp(0x08, ARM64Register.X0); // sa_tramp
    EmitStoreToSp(0x10, ARM64Register.X0); // sa_mask + sa_flags

    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, thunkLabel);
    EmitStoreToSp(SigactionOffHandler, ARM64Register.X0);

    EmitMovRegImm(ARM64Register.X0, flags);
    EmitStoreIndirect(ARM64Register.Sp, SigactionOffFlags, ARM64Register.X0, 4);

    foreach (var sig in signals) {
      EmitMovRegImm(ARM64Register.X0, sig);
      EmitMovRegReg(ARM64Register.X1, ARM64Register.Sp);
      EmitMovRegImm(ARM64Register.X2, 0);
      EmitCallImport("sigaction");
    }

    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 0x20, isAdd: true);
  }

  /// <summary>
  /// The debug agent's SIGTRAP handler thunk (P3b). A `BRK #0` raises a synchronous SIGTRAP whose
  /// ucontext pc points AT the BRK. Dispatch:
  ///
  ///   * A temp-bp step in progress (pc == __dbg_step_temp_addr): the single-stepped instruction is
  ///     complete and we are stopped AT the temp address. Remove the temp bp, re-arm the original (if
  ///     any), then split on the disposition the park loop left in __dbg_step_mode: OverBp (continue)
  ///     resumes silently; User (a source step) publishes a step stop and re-parks, planting the next
  ///     temp bp if the next command is also a step. This is macOS's stand-in for x86's trap flag:
  ///     userspace has no hardware single-step, so the agent plants a temporary BRK at pc+4 and lets it
  ///     fire.
  ///   * A known breakpoint (pc in the table): publish a stop event, park until continue/step, then begin
  ///     single-step-over — restore the original word at pc and plant the temp bp at pc+4. Returning
  ///     with pc unchanged re-executes the restored instruction, then hits the temp bp. The setup is the
  ///     same whether the release was continue or step; __dbg_step_mode decides what the temp-bp hit does.
  ///   * Anything else (an unowned BRK, or a fault after the agent detached — __dbg_base == 0):
  ///     CHAIN to the default disposition (signal(SIGTRAP, SIG_DFL); raise). This is what resolves the
  ///     P3a re-trap-loop residual: a bare return would re-execute the BRK forever, so an unhandled
  ///     BRK must terminate via the default action instead.
  ///
  /// sigaction handler ABI: void handler(int sig, siginfo_t* info, void* ucontext) — X0/X1/X2.
  ///
  /// ⚠ macOS/arm64 host-unverifiable: this project's arm64 target cannot be run here. The mechanism
  /// mirrors the verified x86 path; the temp-bp-at-pc+4 step assumes the breakpointed instruction
  /// falls through (true for a function-entry/prologue breakpoint, the --bp-test target). A
  /// branch-first-instruction breakpoint would need displaced stepping — a P4 residual.
  /// </summary>
  internal void EmitDbgTrapHandlerThunk() {
    EmitRuntimeFunctionStart("__dbg_trap_handler_thunk", 3, 0x60);

    const int slotMcontext = 0x38;
    const int slotPc = 0x40;
    const int slotTemp = 0x48;

    // mcontext = *(ucontext + UcontextOffMcontext); pc = mcontext->__ss.__pc.
    EmitLoadFromStack(ARM64Register.X4, 32, 8);
    EmitLoadIndirect(ARM64Register.X4, ARM64Register.X4, UcontextOffMcontext, 8);
    EmitStoreToSp(slotMcontext, ARM64Register.X4);
    EmitLoadIndirect(ARM64Register.X5, ARM64Register.X4, McontextOffSsPc, 8);
    EmitStoreToSp(slotPc, ARM64Register.X5);

    // Shutdown guard: detached agent (segment unmapped, table stale) → chain to default.
    EmitGlobalLoadReg(ARM64Register.X6, "__dbg_base");
    EmitCbz(ARM64Register.X6, "__dbg_th_chain");

    // Temp-bp step in progress and this trap is it? (pc == step_temp_addr)
    EmitGlobalLoadReg(ARM64Register.X6, Runtime.RuntimeEmitter.DbgStepTempAddrGlobal);
    EmitCbz(ARM64Register.X6, "__dbg_th_check_known");
    EmitLoadFromStack(ARM64Register.X5, slotPc, 8);
    EmitCmpRegReg(ARM64Register.X6, ARM64Register.X5);
    EmitBranchCond(ARM64ConditionCode.Ne, "__dbg_th_check_known");

    // Temp-bp hit: the single-stepped instruction completed and we are stopped AT the temp address
    // (= the prior pc + 4). newPc == slotPc here (the guard above only fell through when pc equals the
    // temp address), so slotPc IS the location we stopped at.
    // Disarm the temp bp (restore the real instruction at newPc).
    EmitGlobalLoadReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepTempAddrGlobal);
    EmitGlobalLoadReg(ARM64Register.X1, Runtime.RuntimeEmitter.DbgStepTempOrigGlobal);
    EmitBranchLink("__dbg_disarm_bp");
    // Re-arm the original breakpoint we stepped over, if any (step_addr != 0 — a user step from a non-bp
    // location has step_addr 0, so arming it unconditionally would patch address 0).
    EmitGlobalLoadReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepAddrGlobal);
    EmitCbz(ARM64Register.X0, "__dbg_th_ss_no_rearm");
    EmitBranchLink("__dbg_arm_bp");
    DefineLabel("__dbg_th_ss_no_rearm");
    // Clear the step state (temp + addr).
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepTempAddrGlobal);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepAddrGlobal);

    // Disposition: a user step publishes a stop + parks; step-over-a-bp (continue) resumes silently.
    EmitGlobalLoadReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepModeGlobal);
    EmitMovRegImm(ARM64Register.X1, Runtime.RuntimeEmitter.DbgStepModeUser);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "__dbg_th_userstep");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepModeGlobal);  // mode = None
    EmitRuntimeFunctionEnd();

    // User step: publish a step stop at newPc (= slotPc), then park. Reset mode to None first (the park
    // loop re-sets it from the releasing command).
    DefineLabel("__dbg_th_userstep");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepModeGlobal);
    EmitLoadFromStack(ARM64Register.X4, slotMcontext, 8);
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);                             // arg0 = newPc (abs)
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X4, McontextOffSsSp, 8);   // arg1 = sp
    EmitLoadIndirect(ARM64Register.X2, ARM64Register.X4, McontextOffSsFp, 8);   // arg2 = fp
    EmitBranchLink("__dbg_on_step");                    // publish reason=step + park until next command
    // Park returned. If the next command is a step, plant the next single-step. On continue, if a bp is
    // armed at newPc — the user STEPPED onto it, so the bp-hit path's step-over never ran (step_addr is
    // 0) — step over it the same way; a plain resume would re-trap on the BRK still sitting here (a
    // double-report at the same PC).
    EmitGlobalLoadReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepModeGlobal);
    EmitMovRegImm(ARM64Register.X1, Runtime.RuntimeEmitter.DbgStepModeUser);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Eq, "__dbg_th_userstep_plant");
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitBranchLink("__dbg_bp_slot");                    // X0 = idx (-1 if no bp at newPc)
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "__dbg_th_userstep_resume");   // no bp: resume

    // Plant a single-step-OVER from newPc: disarm any bp there (prepare_step_at sets step_addr for the
    // post-step re-arm), then plant a temp bp at newPc + 4 (arm64 has no hardware trap flag). The
    // follow-up temp-bp hit re-arms and, per __dbg_step_mode, publishes (User) or resumes silently
    // (OverBp). Shared by the post-park re-step and a continue that resumes onto a breakpoint.
    DefineLabel("__dbg_th_userstep_plant");
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitBranchLink("__dbg_prepare_step_at");
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, DbgBreakpointPatchLen, isAdd: true);
    EmitStoreToSp(slotTemp, ARM64Register.X0);
    EmitBranchLink("__dbg_arm_bp");                     // X0 = temp's original word
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepTempOrigGlobal);
    EmitLoadFromStack(ARM64Register.X0, slotTemp, 8);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepTempAddrGlobal);
    DefineLabel("__dbg_th_userstep_resume");
    EmitRuntimeFunctionEnd();

    DefineLabel("__dbg_th_check_known");
    // found = __dbg_on_breakpoint(pc, sp, fp).
    EmitLoadFromStack(ARM64Register.X4, slotMcontext, 8);
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X4, McontextOffSsSp, 8);
    EmitLoadIndirect(ARM64Register.X2, ARM64Register.X4, McontextOffSsFp, 8);
    EmitBranchLink("__dbg_on_breakpoint");
    EmitCbz(ARM64Register.X0, "__dbg_th_chain");

    // The driver may have CLEARED this breakpoint while parked. __dbg_clear_bp already restored the
    // instruction, so re-check slot PRESENCE (not orig != 0): if it is gone, resume at pc with no
    // disarm and no temp-bp single-step (the word is already the real instruction).
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitBranchLink("__dbg_bp_slot");                // X0 = idx (-1 if cleared while parked)
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "__dbg_th_bp_cleared");

    // Begin single-step-over: restore the original instruction at pc, plant a temp bp at pc+4.
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitBranchLink("__dbg_bp_orig_of_addr");        // X0 = orig
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitBranchLink("__dbg_disarm_bp");
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, DbgBreakpointPatchLen,
      isAdd: true);                                 // temp = pc + 4 (the next instruction)
    EmitStoreToSp(slotTemp, ARM64Register.X0);
    EmitBranchLink("__dbg_arm_bp");                 // X0 = temp's original word
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepTempOrigGlobal);
    EmitLoadFromStack(ARM64Register.X0, slotTemp, 8);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepTempAddrGlobal);
    EmitLoadFromStack(ARM64Register.X0, slotPc, 8);
    EmitGlobalStoreReg(ARM64Register.X0, Runtime.RuntimeEmitter.DbgStepAddrGlobal);
    EmitRuntimeFunctionEnd();

    // Breakpoint cleared while parked: word already restored, pc unchanged — just resume.
    DefineLabel("__dbg_th_bp_cleared");
    EmitRuntimeFunctionEnd();

    DefineLabel("__dbg_th_chain");
    // signal(SIGTRAP, SIG_DFL); raise(SIGTRAP) — terminate via default disposition (no re-trap loop).
    EmitLoadFromStack(ARM64Register.X0, 16, 8);     // sig
    EmitMovRegImm(ARM64Register.X1, SigDfl);
    EmitCallImport("signal");
    EmitLoadFromStack(ARM64Register.X0, 16, 8);
    EmitCallImport("raise");
    EmitRuntimeFunctionEnd();
  }

  // Shared frame layout of the two `.text`-patch primitives below: the absolute patch address is the
  // spilled first arg, and (for arm) the original word the second slot. The W^X helpers depend on the
  // abs slot, so both primitives must use this frame.
  private const int DbgArm64PatchAbsSlot = 16;
  private const int DbgArm64PatchOrigSlot = 24;

  /// <summary>mprotect the page holding the patch address (page-aligned down) to <paramref name="prot"/>.
  /// Assumes the patch frame layout above.</summary>
  private void EmitDbgMprotectCodePage(long prot) {
    EmitLoadFromStack(ARM64Register.X0, DbgArm64PatchAbsSlot, 8);
    EmitLsrImm(ARM64Register.X0, ARM64Register.X0, DbgArm64PageShift);
    EmitLslImm(ARM64Register.X0, ARM64Register.X0, DbgArm64PageShift);
    EmitMovRegImm(ARM64Register.X1, DbgArm64PageSize);
    EmitMovRegImm(ARM64Register.X2, prot);
    EmitCallImport("mprotect");
  }

  /// <summary>sys_icache_invalidate(abs, 4) so the CPU fetches the patched instruction.</summary>
  private void EmitDbgIcacheInvalidate() {
    EmitLoadFromStack(ARM64Register.X0, DbgArm64PatchAbsSlot, 8);
    EmitMovRegImm(ARM64Register.X1, DbgBreakpointPatchLen);
    EmitCallImport("sys_icache_invalidate");
  }

  /// <summary>
  /// __dbg_arm_bp(X0 = abs) -> X0 = the original 4-byte word. W^X patch: mprotect the containing page
  /// to READ|WRITE, save the word at `abs` and overwrite it with `BRK #0`, mprotect back to READ|EXEC,
  /// and invalidate the instruction cache. The arm64 mirror of the x86 primitive; called from
  /// __dbg_set_bp and from the trap thunk's re-arm. Host-unverifiable (arm64 target cannot run here);
  /// on a hardened-runtime binary the mprotect may need the JIT entitlement (allow-jit / MAP_JIT).
  /// Frame: [x29+16] = abs (spilled arg), [x29+24] = orig.
  /// </summary>
  internal void EmitDbgArmBp() {
    EmitRuntimeFunctionStart("__dbg_arm_bp", 1, 0x40);

    EmitDbgMprotectCodePage(DbgProtReadWrite);

    // orig = *(u32*)abs ; *(u32*)abs = BRK #0
    EmitLoadFromStack(ARM64Register.X2, DbgArm64PatchAbsSlot, 8);
    EmitLoadIndirect(ARM64Register.X3, ARM64Register.X2, 0, DbgBreakpointPatchLen);
    EmitStoreToSp(DbgArm64PatchOrigSlot, ARM64Register.X3);
    EmitMovRegImm(ARM64Register.X4, Runtime.RuntimeEmitter.DbgArm64BreakpointWord);
    EmitStoreIndirect(ARM64Register.X2, 0, ARM64Register.X4, DbgBreakpointPatchLen);

    EmitDbgMprotectCodePage(DbgProtReadExec);
    EmitDbgIcacheInvalidate();

    EmitLoadFromStack(ARM64Register.X0, DbgArm64PatchOrigSlot, 8);     // return orig
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __dbg_disarm_bp(X0 = abs, X1 = orig) — the dual of __dbg_arm_bp: W^X-patch the saved original
  /// word back over the BRK and invalidate the icache. Leaves `.text` executable and pristine.
  /// Frame: [x29+16] = abs, [x29+24] = orig (spilled args).
  /// </summary>
  internal void EmitDbgDisarmBp() {
    EmitRuntimeFunctionStart("__dbg_disarm_bp", 2, 0x40);

    EmitDbgMprotectCodePage(DbgProtReadWrite);

    // *(u32*)abs = orig
    EmitLoadFromStack(ARM64Register.X2, DbgArm64PatchAbsSlot, 8);
    EmitLoadFromStack(ARM64Register.X3, DbgArm64PatchOrigSlot, 8);
    EmitStoreIndirect(ARM64Register.X2, 0, ARM64Register.X3, DbgBreakpointPatchLen);

    EmitDbgMprotectCodePage(DbgProtReadExec);
    EmitDbgIcacheInvalidate();

    EmitRuntimeFunctionEnd();
  }

  // ============================================================================
  // Phase 3.1 — Subprocess runtime stubs (cross-target mirror of X86).
  //
  // Each function below emits a placeholder body that returns a sentinel
  // "unimplemented" value so user-level calls link cleanly without crashing.
  // The real Unix implementations (posix_spawn, kqueue / pidfd integration,
  // WIFEXITED/WIFSIGNALED decoding, PATH lookup with access(X_OK), etc.) land
  // in Phase 3.3 — see lets-rewrite-our-process-maxon-humming-galaxy.md for
  // the full contract.
  //
  // Sentinel return values mirror the X86 stubs:
  //   - int64 handle/pid functions return -1.
  //   - __ManagedMemory-returning functions return 0 (NULL).
  //   - Result-struct accessors return 0 (unreachable via the stubs, since
  //     subprocessWaitCollect never produces a non-error result).
  //   - Void functions just return.
  //
  // Arg counts must match the parser intrinsic table in 2-Parser.cs exactly.
  // ============================================================================

  private void EmitMaxonSubprocessStubs() {
    // --- Real posix implementations (attached spawn → wait-collect → decode) ---
    // posix_spawn + pipe capture of stdout/stderr, waitpid status decode. The
    // result struct holds the raw captured byte buffers; the stdout/stderr
    // accessors return a fresh mm_raw_alloc'd cstring (RuntimeCallToManaged
    // wraps it into a String and mm_raw_free's the cstring). No dependency on
    // compiled stdlib symbols. Covers the Subprocess.run contract used by the
    // spec runner (stdin=none → /dev/null, stdout/stderr=collect → pipe).
    EmitSubpDrainPass();
    EmitSubpBuildArgv();
    EmitSubpBuildEnvp();
    EmitMaxonSubprocessSpawnPosix();
    EmitMaxonSubprocessWaitCollectPosix();
    EmitMaxonSubprocessGetPidPosix();
    EmitSubprocessResultAccessor("maxon_subprocess_result_status_kind", 0);
    EmitSubprocessResultAccessor("maxon_subprocess_result_status_code", 8);
    EmitSubpResultStreamCopy("maxon_subprocess_result_stdout", 16, 24);
    EmitSubpResultStreamCopy("maxon_subprocess_result_stderr", 32, 40);
    EmitSubprocessResultAccessor("maxon_subprocess_result_duration_ms", 48);
    EmitMaxonSubprocessResultReleasePosix();
    EmitMaxonSubprocessReleaseHandlePosix();
    EmitMaxonManagedIsNullPosix();

    // --- Streaming subprocess API (persistent-worker pool, parallel spec runner) ---
    EmitSubpStreamEmitLine();
    EmitSubpStreamReadLine();
    EmitMaxonSubprocessSpawnStreamingPosix();
    EmitMaxonSubprocessWriteStdinAllPosix();
    EmitSubpStreamReadLineWrapper("maxon_subprocess_read_stdout_line", SubpHOffOutFd, SubpHOffOutBuf);
    EmitSubpStreamReadLineWrapper("maxon_subprocess_read_stderr_line", SubpHOffErrFd, SubpHOffErrBuf);
    EmitMaxonSubprocessCloseStdinPosix();
    EmitMaxonSubprocessWaitExitPosix();
    EmitMaxonSubprocessKillPosix();
    EmitMaxonSubprocessLastErrorMessagePosix();

    // --- Still stubbed (not on the spec-runner path; gated host-only specs) ---
    // resolve_on_path never resolves PATH here — it returns a fresh empty cstring
    // (NOT NULL, to honour the C# cstring→managed ABI; managedIsNull reads it as a
    // miss all the same). That is fine because the spawn does the PATH search: the
    // stdlib turns the miss into the bare name, and posix_spawnp resolves it
    // execvp-style. So `Executable.name("dotnet")` launches without a real resolver
    // (see EmitMaxonSubprocessResolveOnPathPosix and the posix_spawnp call sites).
    EmitMaxonSubprocessResolveOnPathPosix();
    EmitSubprocessIntStub("maxon_subprocess_send_signal", 2, returnValue: -1);
    EmitSubprocessIntStub("maxon_subprocess_detach", 14, returnValue: -1);
  }

  /// Emit a stub that loads `returnValue` into x0 and returns. argCount is
  /// ignored (passed as 0 to EmitRuntimeFunctionStart) because the stub
  /// doesn't read its arguments and the prologue helper would otherwise
  /// home incoming-arg registers using the AAPCS64 8-entry table — fine for
  /// args 0..7 but unused beyond that.
  private void EmitSubprocessIntStub(string name, int argCount, long returnValue) {
    _ = argCount;
    EmitRuntimeFunctionStart(name, 0, 0x30);
    EmitMovRegImm(ARM64Register.X0, returnValue);
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Posix subprocess runtime (arm64-macos)
  //
  // Handle struct (mm_raw_alloc, 0x20):  +0 pid  +8 outReadFd  +16 errReadFd  +24 argv[]
  // Result struct (mm_raw_alloc, 0x38):  +0 statusKind  +8 statusCode
  //   +16 stdoutBuf  +24 stdoutLen  +32 stderrBuf  +40 stderrLen  +48 durationMs
  // outReadFd/errReadFd hold -1 when that stream is not collected.
  // stdoutBuf/stderrBuf are mm_raw_alloc'd raw byte buffers (0/len 0 when empty).
  // result_stdout/result_stderr return a FRESH mm_raw_alloc'd cstring copy
  // (RuntimeCallToManaged wraps it into a String and mm_raw_free's the cstring).
  // result_release frees the raw buffers + the result struct.
  // ==========================================================================

  // --- drain-pass tunables and the POSIX constants it names ---

  /// Bytes moved per drain pass, per stream. One macOS pipe buffer's worth, so a
  /// ready stream empties in a single read and the pass moves on to its sibling
  /// instead of starving it.
  private const long SubpDrainChunk = 64 * 1024;
  /// POLLIN, plus the `struct pollfd` field offsets the drain pass fills in
  /// ({ int fd; short events; short revents; }).
  private const long PollInEvent = 0x0001;
  private const int PollFdOffFd = 0x00;
  private const int PollFdOffEvents = 0x04;
  private const int PollFdOffRevents = 0x06;
  /// EINTR. A poll or read cut short by a signal has neither moved bytes nor seen
  /// end-of-stream, so it is a no-progress pass and emphatically NOT EOF —
  /// conflating the two is the one way this loop can truncate a child's output.
  private const long ErrnoEintr = 4;
  /// SIGKILL, and the exit code the stdlib's timedOut status carries.
  private const long SignalKill = 9;
  private const long SubpTimedOutExitCode = 124;
  /// WNOHANG.
  private const long WaitNoHang = 1;
  /// Once the deadline has SIGKILLed the child, how much longer the loop keeps
  /// draining what the dead child already wrote before it gives up. Without a
  /// bound, a grandchild holding the pipe write end open would hang the wait for
  /// ever; without the grace, the bytes the child managed to write are lost.
  private const long SubpKillDrainGraceMs = 250;

  /// Per-stream drain state, a 4-quad block in the caller's frame that
  /// `__subp_drain_pass` reads and updates. `cap` is the caller's capture ceiling;
  /// the mapping behind `buf` is always `cap + SubpDrainChunk` bytes, the spare
  /// chunk being the bit bucket that keeps a child draining once the ceiling is hit.
  private const int SubpStateBuf = 0x00;
  private const int SubpStateLen = 0x08;
  private const int SubpStateDone = 0x10;
  private const int SubpStateCap = 0x18;

  // maxon_subprocess_wait_collect frame slots (arg homes occupy 0x10 and 0x18).
  private const int SubpWcHandle = 0x20;
  /// maxon_subprocess_release_handle's frame slot for the handle pointer.
  private const int SubpRhHandle = 0x18;
  private const int SubpWcStartMs = 0x28;   // reused for elapsedMs after the loop
  private const int SubpWcDeadline = 0x30;  // absolute ms; 0 means "wait for ever"
  private const int SubpWcTimedOut = 0x38;
  private const int SubpWcReaped = 0x40;
  private const int SubpWcKilled = 0x48;
  private const int SubpWcStatus = 0x50;    // waitpid's 4-byte status word
  private const int SubpWcProgress = 0x58;
  private const int SubpWcTimespec = 0x60;  // tv_sec @ +0, tv_nsec @ +8
  private const int SubpWcOutState = 0x70;
  private const int SubpWcErrState = 0x90;
  private const int SubpWcOutBuf = 0xB0;
  private const int SubpWcErrBuf = 0xB8;
  private const int SubpWcKind = 0xC0;
  private const int SubpWcCode = 0xC8;
  private const int SubpWcFrame = 0xD0;
  private const string SubpCaptureOomMessage = "__subp_msg_capture_oom";

  /// Result accessor that copies a captured byte buffer into a fresh
  /// mm_raw_alloc'd, NUL-terminated cstring (RuntimeCallToManaged contract).
  /// bufOff/lenOff are the result-struct field offsets. Empty/null → 1-byte "".
  private void EmitSubpResultStreamCopy(string name, int bufOff, int lenOff) {
    EmitRuntimeFunctionStart(name, 1, 0x40);
    // locals: 0x18 srcBuf, 0x20 len, 0x28 dst
    int n = _uniqueLabelCounter++;
    string empty = $"__subp_rsc_empty_{n}";
    string done = $"__subp_rsc_done_{n}";
    EmitReloadArg(0);                                   // result
    EmitCbz(ARM64Register.X0, empty);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, bufOff, 8); // srcBuf
    EmitStoreToStack(0x18, ARM64Register.X1, 8);
    EmitLoadIndirect(ARM64Register.X2, ARM64Register.X0, lenOff, 8); // len
    EmitStoreToStack(0x20, ARM64Register.X2, 8);
    EmitCbz(ARM64Register.X1, empty);                  // srcBuf == 0
    EmitCbz(ARM64Register.X2, empty);                  // len == 0
    // dst = mm_raw_alloc(len + 1)
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x28, ARM64Register.X0, 8);
    // memcpy(dst, srcBuf, len)
    EmitLoadFromStack(ARM64Register.X0, 0x28, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x18, 8);
    EmitLoadFromStack(ARM64Register.X2, 0x20, 8);
    EmitBranchLink("maxon_memcpy");
    // dst[len] = 0
    EmitLoadFromStack(ARM64Register.X0, 0x28, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x20, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1); // dst+len
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1);  // STRB wzr
    EmitLoadFromStack(ARM64Register.X0, 0x28, 8);                  // return dst
    EmitBranch(done);
    DefineLabel(empty);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);          // 1-byte cstring
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1); // [dst] = 0
    DefineLabel(done);
    EmitRuntimeFunctionEnd();
  }

  /// __subp_drain_pass(fd, state) -> bytesMoved. ONE BOUNDED PASS over a live
  /// child's pipe: it never waits. `poll(fd, POLLIN, 0)` asks whether the
  /// descriptor has something for us, and only then does `read` run — so the read
  /// cannot block, and "nothing ready yet" (poll returns 0) stays distinguishable
  /// from "end of stream" (read returns 0). That distinction is the whole point:
  /// conflating them either truncates the capture or spins for ever.
  ///
  /// ⚠ THIS REPLACED A READ-TO-EOF DRAIN, AND THE OLD SHAPE COULD NOT BE FIXED IN
  /// PLACE. Its caller waited for the child before reading anything, so a child
  /// writing more than one pipe buffer (65,536 bytes on macOS) blocked in write()
  /// while the parent slept in waitpid() — a deadlock that surfaced as the
  /// caller's timeout, measured at exactly the pipe-buffer boundary. A bounded
  /// pass is what lets the caller interleave both streams with the wait.
  ///
  /// `poll` rather than O_NONBLOCK because `fcntl(fd, F_SETFL, ...)` is VARIADIC,
  /// and Apple ARM64 passes variadic arguments on the stack (see
  /// EmitPushVariadicArg) — a per-call-site frame dance for a flag that `poll`
  /// gives us with a fixed three-register signature.
  private void EmitSubpDrainPass() {
    EmitRuntimeFunctionStart("__subp_drain_pass", 2, 0x50);
    // args: fd@0x10, state@0x18 ; locals: pollfd@0x20, dst@0x28, count@0x30,
    //   advance@0x38 (0 = the bytes land in the bit bucket), n@0x40
    const int slotPollFd = 0x20;
    const int slotDst = 0x28;
    const int slotCount = 0x30;
    const int slotAdvance = 0x38;
    const int slotRead = 0x40;
    int n = _uniqueLabelCounter++;
    string noProgress = $"__subp_dp_none_{n}";
    string markDone = $"__subp_dp_done_{n}";
    string errnoCheck = $"__subp_dp_errno_{n}";
    string bitBucket = $"__subp_dp_bucket_{n}";
    string chunkCapped = $"__subp_dp_capped_{n}";
    string doRead = $"__subp_dp_read_{n}";
    string kept = $"__subp_dp_kept_{n}";

    // A stream already at EOF, or one that never had a pipe, costs one load.
    EmitReloadArg(1);                                                // X1 = state
    EmitLoadIndirect(ARM64Register.X9, ARM64Register.X1, SubpStateDone, 8);
    EmitCbnz(ARM64Register.X9, noProgress);
    EmitReloadArg(0);                                                // X0 = fd
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, markDone);                 // no pipe on this stream

    // poll({fd, POLLIN}, 1, timeout 0)
    EmitStoreToStack(slotPollFd + PollFdOffFd, ARM64Register.X0, 4);
    EmitMovRegImm(ARM64Register.X9, PollInEvent);
    EmitStoreToStack(slotPollFd + PollFdOffEvents, ARM64Register.X9, 2);
    EmitStoreToStack(slotPollFd + PollFdOffRevents, ARM64Register.Xzr, 2);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, slotPollFd, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 1);                              // nfds
    EmitMovRegImm(ARM64Register.X2, 0);                              // timeout: return at once
    EmitCallImport("poll");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, noProgress);               // nothing ready yet
    EmitBranchCond(ARM64ConditionCode.Lt, errnoCheck);

    // Ready. Any revents at all (POLLIN, POLLHUP, POLLERR) means read() returns
    // without blocking, and the read is what tells EOF from bytes — POLLHUP alone
    // is not reliable across platforms, so we never branch on revents.
    EmitReloadArg(1);
    EmitLoadIndirect(ARM64Register.X9, ARM64Register.X1, SubpStateLen, 8);   // total
    EmitLoadIndirect(ARM64Register.X10, ARM64Register.X1, SubpStateCap, 8);  // cap
    EmitLoadIndirect(ARM64Register.X11, ARM64Register.X1, SubpStateBuf, 8);  // buf
    EmitAluRegReg(0xCB000000, ARM64Register.X12, ARM64Register.X10, ARM64Register.X9); // room
    EmitCmpImm(ARM64Register.X12, 0);
    EmitBranchCond(ARM64ConditionCode.Le, bitBucket);
    EmitMovRegImm(ARM64Register.X13, SubpDrainChunk);
    EmitCmpRegReg(ARM64Register.X12, ARM64Register.X13);
    EmitBranchCond(ARM64ConditionCode.Le, chunkCapped);
    EmitMovRegReg(ARM64Register.X12, ARM64Register.X13);
    DefineLabel(chunkCapped);
    EmitStoreToStack(slotCount, ARM64Register.X12, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X14, ARM64Register.X11, ARM64Register.X9); // buf + total
    EmitStoreToStack(slotDst, ARM64Register.X14, 8);
    EmitMovRegImm(ARM64Register.X15, 1);
    EmitStoreToStack(slotAdvance, ARM64Register.X15, 8);
    EmitBranch(doRead);

    // The capture ceiling caps what is KEPT, never what is READ: a runtime that
    // stopped reading at the ceiling would leave the child blocked on a full pipe
    // for ever. Past it the bytes land in the spare chunk mapped beyond `cap` and
    // the collected length simply does not advance.
    DefineLabel(bitBucket);
    EmitMovRegImm(ARM64Register.X12, SubpDrainChunk);
    EmitStoreToStack(slotCount, ARM64Register.X12, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X14, ARM64Register.X11, ARM64Register.X10); // buf + cap
    EmitStoreToStack(slotDst, ARM64Register.X14, 8);
    EmitStoreToStack(slotAdvance, ARM64Register.Xzr, 8);

    DefineLabel(doRead);
    EmitReloadArg(0);                                                // fd
    EmitLoadFromStack(ARM64Register.X1, slotDst, 8);
    EmitLoadFromStack(ARM64Register.X2, slotCount, 8);
    EmitCallImport("read");
    EmitStoreToStack(slotRead, ARM64Register.X0, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, markDone);                 // 0 = end of stream
    EmitBranchCond(ARM64ConditionCode.Lt, errnoCheck);
    EmitLoadFromStack(ARM64Register.X9, slotAdvance, 8);
    EmitCbz(ARM64Register.X9, kept);
    EmitReloadArg(1);
    EmitLoadIndirect(ARM64Register.X9, ARM64Register.X1, SubpStateLen, 8);
    EmitLoadFromStack(ARM64Register.X10, slotRead, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X9, ARM64Register.X9, ARM64Register.X10);
    EmitStoreIndirect(ARM64Register.X1, SubpStateLen, ARM64Register.X9, 8);
    DefineLabel(kept);
    // Discarded bytes still count as progress: they moved, so the caller must come
    // straight round again rather than sleeping while the child waits on the pipe.
    EmitLoadFromStack(ARM64Register.X0, slotRead, 8);
    EmitRuntimeFunctionEnd();

    // A poll or read that failed: EINTR is a no-progress pass, anything else ends
    // the stream. Shared by both call sites so the rule is written down once.
    DefineLabel(errnoCheck);
    EmitCallImport("__error");
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 0, 4);
    EmitCmpImm(ARM64Register.X0, ErrnoEintr);
    EmitBranchCond(ARM64ConditionCode.Eq, noProgress);

    DefineLabel(markDone);
    EmitReloadArg(1);
    EmitMovRegImm(ARM64Register.X9, 1);
    EmitStoreIndirect(ARM64Register.X1, SubpStateDone, ARM64Register.X9, 8);

    DefineLabel(noProgress);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  // StdinKindBytes, StdinKindHold, StdinKindDelayed, StdinDelayedFeedMs and
  // StdinKindsWantingPipe come from Runtime/SubprocessStdinContract.cs, shared
  // with X86CodeEmitter — see the `using static` at the head of this file. They
  // lived here AND there until the two copies had already drifted in spelling
  // (kind 2 was `StdinKindBytes` here and `StdioKindCollect` there), and that
  // file records why a second copy is a wrong answer rather than a build break.

  // Stays local: it is not part of the contract, only the arithmetic that splits
  // `StdinDelayedFeedMs` into a `timespec`'s two fields on this target. The nanos
  // half of that conversion is `TimerNanosPerMilli`.
  private const int StdinDelayedMillisPerSecond = 1000;

  /// Fall through when stdinKind (spawn local 0xC8) asks for a parent↔child pipe,
  /// and branch to `skip` when it does not. THE ONE PLACE that question is asked:
  /// the pipe, the child's dup2 and the parent's post-spawn step must agree about
  /// it exactly, and three copies of the test is three chances for one of them to
  /// learn about a new kind and the others not to.
  private void EmitSubpStdinPipeWanted(string skip, string wanted) {
    EmitLoadFromStack(ARM64Register.X0, 0xC8, 8);
    foreach (int pipeKind in StdinKindsWantingPipe) {
      EmitCmpImm(ARM64Register.X0, pipeKind);
      EmitBranchCond(ARM64ConditionCode.Eq, wanted);
    }
    EmitBranch(skip);
    DefineLabel(wanted);
  }

  /// maxon_subprocess_spawn(argvBlob, argc, cwd, env, envInherit, stdinKind,
  ///   stdinData, stdoutKind, stdoutData, stdoutLimit, stderrKind, stderrData,
  ///   stderrLimit, flags) -> handle | -1.
  private void EmitMaxonSubprocessSpawnPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_spawn", 14, 0x100);
    int n = _uniqueLabelCounter++;
    string skipOutPipe = $"__subp_sp_skopipe_{n}";
    string skipErrPipe = $"__subp_sp_skepipe_{n}";
    string skipOutDup = $"__subp_sp_skodup_{n}";
    string skipErrDup = $"__subp_sp_skedup_{n}";
    string skipStdin = $"__subp_sp_skstdin_{n}";
    string skipInPipe = $"__subp_sp_skipipe_{n}";
    string skipStdinPipe = $"__subp_sp_skstdinpipe_{n}";
    string skipWriteStdin = $"__subp_sp_skwrstdin_{n}";
    string holdStdin = $"__subp_sp_holdstdin_{n}";
    string delaySleep = $"__subp_sp_delaysleep_{n}";
    string feedStdinNow = $"__subp_sp_feedstdin_{n}";
    string skipChdir = $"__subp_sp_skchdir_{n}";
    string inheritEnv = $"__subp_sp_envinh_{n}";
    string envReady = $"__subp_sp_envrdy_{n}";
    string envpNotOwned = $"__subp_sp_envfree_{n}";
    string spawnFail = $"__subp_sp_fail_{n}";
    string skipCloseOut = $"__subp_sp_skcout_{n}";
    string skipCloseErr = $"__subp_sp_skcerr_{n}";

    // Locals: 0x50 outPipe(rd@0x50,wr@0x54) 0x58 errPipe(rd@0x58,wr@0x5C)
    //   0x60 fa  0x68 pid  0x70 handle  0x78 argv  0x80-0x8F "/dev/null"
    //   0x90 bufStart  0x98 blobLen  0xA0 envp  0xA8 outKind  0xB0 errKind  0xB8 argc
    //   0xC0 inPipe(rd@0xC0,wr@0xC4)  0xC8 stdinKind  0xD0 stdinData
    //   0xD8 ownedEnvp (the vector THIS spawn built, or 0 when envp is the process's own environ)
    //   0xE0 posix_spawnp result, held across the ownedEnvp free
    //   0xE8 heldStdinFd — the stdin write end this handle KEEPS (StdinKindHold), or -1
    //   0xF0/0xF8 timespec for StdinKindDelayed's nanosleep (the frame's last 16 bytes)
    // Args 8..13 (stack): [x29 + 0x100 + (i-8)*8]; stderrKind=arg10 @ 0x110.

    // Default pipe read/write fds to -1 (4-byte) so non-collect/non-bytes streams
    // record -1 and the spawn-fail cleanup never closes a garbage descriptor.
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitStoreToStack(0x50, ARM64Register.X0, 4);
    EmitStoreToStack(0x58, ARM64Register.X0, 4);
    EmitStoreToStack(0xC0, ARM64Register.X0, 4);
    EmitStoreToStack(0xC4, ARM64Register.X0, 4);
    // A sync spawn keeps no stdin descriptor unless it was asked for `hold`, and the
    // handle field is 8 bytes wide, so the "none" answer is written here at that width.
    EmitStoreToStack(0xE8, ARM64Register.X0, 8);
    // Nothing owned yet, on every path out — the free below is unconditional and reads this slot.
    EmitStoreToStack(0xD8, ARM64Register.Xzr, 8);

    // --- argv build ---
    // arg0 (argvBlob.managed) is passed as the BUFFER pointer directly (the
    // __ManagedMemory→buffer extraction the C# backend applies to intrinsic
    // args), holding argc NUL-separated strings back-to-back. There is no
    // length operand; argc bounds the parse (mirrors x86 __subp_build_cmdline).
    EmitReloadArg(0);                                              // bufStart (buffer ptr) → x0
    EmitStoreToStack(0x90, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8);                 // argc (arg1 home) → x0
    EmitStoreToStack(0xB8, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x90, 8);
    EmitLoadFromStack(ARM64Register.X1, 0xB8, 8);
    EmitBranchLink("__subp_build_argv");                          // x0 = argv[]
    EmitStoreToStack(0x78, ARM64Register.X0, 8);

    // --- stdio kinds ---
    EmitLoadFromStack(ARM64Register.X0, 0x48, 8);                // stdoutKind (arg7 home) → x0
    EmitStoreToStack(0xA8, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x110, 8);               // stderrKind (stack arg10)
    EmitStoreToStack(0xB0, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x38, 8);                // stdinKind (arg5 home)
    EmitStoreToStack(0xC8, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x40, 8);                // stdinData cstr (arg6 home)
    EmitStoreToStack(0xD0, ARM64Register.X0, 8);

    // pipes for collect(2)
    EmitLoadFromStack(ARM64Register.X0, 0xA8, 8);
    EmitCmpImm(ARM64Register.X0, 2);
    EmitBranchCond(ARM64ConditionCode.Ne, skipOutPipe);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x50, isAdd: true);
    EmitCallImport("pipe");
    DefineLabel(skipOutPipe);
    EmitLoadFromStack(ARM64Register.X0, 0xB0, 8);
    EmitCmpImm(ARM64Register.X0, 2);
    EmitBranchCond(ARM64ConditionCode.Ne, skipErrPipe);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x58, isAdd: true);
    EmitCallImport("pipe");
    DefineLabel(skipErrPipe);

    // stdin pipe: `bytes` (the parent feeds a payload after spawn), `hold` (the
    // parent feeds nothing, ever) and `delayed` (the parent feeds late) all need one.
    EmitSubpStdinPipeWanted(skipInPipe, $"__subp_sp_wantipipe_{n}");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0xC0, isAdd: true);
    EmitCallImport("pipe");
    DefineLabel(skipInPipe);

    // file actions init
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_init");

    // stdout dup2 → fd1 + close read end (collect)
    EmitLoadFromStack(ARM64Register.X0, 0xA8, 8);
    EmitCmpImm(ARM64Register.X0, 2);
    EmitBranchCond(ARM64ConditionCode.Ne, skipOutDup);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0x54, 4);               // outPipe write
    EmitMovRegImm(ARM64Register.X2, 1);
    EmitCallImport("posix_spawn_file_actions_adddup2");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0x50, 4);               // outPipe read
    EmitCallImport("posix_spawn_file_actions_addclose");
    DefineLabel(skipOutDup);

    // stderr dup2 → fd2 + close read end (collect)
    EmitLoadFromStack(ARM64Register.X0, 0xB0, 8);
    EmitCmpImm(ARM64Register.X0, 2);
    EmitBranchCond(ARM64ConditionCode.Ne, skipErrDup);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0x5C, 4);               // errPipe write
    EmitMovRegImm(ARM64Register.X2, 2);
    EmitCallImport("posix_spawn_file_actions_adddup2");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0x58, 4);               // errPipe read
    EmitCallImport("posix_spawn_file_actions_addclose");
    DefineLabel(skipErrDup);

    // stdin bytes(2) / hold(4) / delayed(5) → dup2(inPipe.read → fd0) + close write end in child
    EmitSubpStdinPipeWanted(skipStdinPipe, $"__subp_sp_wantidup_{n}");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0xC0, 4);               // inPipe read
    EmitMovRegImm(ARM64Register.X2, 0);                          // → fd 0
    EmitCallImport("posix_spawn_file_actions_adddup2");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0xC4, 4);               // inPipe write (child closes)
    EmitCallImport("posix_spawn_file_actions_addclose");
    DefineLabel(skipStdinPipe);

    // stdin none(0) → /dev/null
    EmitLoadFromStack(ARM64Register.X0, 0x38, 8);               // stdinKind (arg5 home) → x0
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, skipStdin);
    EmitMovRegImm(ARM64Register.X0, 0x6C756E2F7665642F);        // "/dev/nul"
    EmitStoreToStack(0x80, ARM64Register.X0, 8);
    EmitMovRegImm(ARM64Register.X0, 0x6C);                       // "l\0"
    EmitStoreToStack(0x88, ARM64Register.X0, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);                          // fildes 0
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X29, 0x80, isAdd: true); // path
    EmitMovRegImm(ARM64Register.X3, 0);                          // O_RDONLY
    EmitMovRegImm(ARM64Register.X4, 0);                          // mode
    EmitCallImport("posix_spawn_file_actions_addopen");
    DefineLabel(skipStdin);

    // cwd addchdir (non-empty)
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8);               // cwd cstr (arg2 home) → x0
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, 0, 1);  // first byte
    EmitCmpImm(ARM64Register.X1, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, skipChdir);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);           // path
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_addchdir_np");
    DefineLabel(skipChdir);

    // The child's environment: the caller's own block when it supplied one, otherwise this process's
    // environ. The block is the self-delimiting NUL-separated form `stdlib/Subprocess.maxon`
    // assembles; __subp_build_envp turns it into the vector posix_spawnp takes WITHOUT copying any
    // bytes, so what is owned here is the vector alone.
    //
    // The test is `== EnvSourceParent` rather than "non-zero" so an unrecognised value falls to the
    // caller-built-block path — see `Runtime/SubprocessContract.EnvSourceParent`.
    EmitLoadFromStack(ARM64Register.X0, 0x30, 8);               // envInherit (arg4 home)
    EmitCmpImm(ARM64Register.X0, Runtime.SubprocessContract.EnvSourceParent);
    EmitBranchCond(ARM64ConditionCode.Eq, inheritEnv);
    EmitLoadFromStack(ARM64Register.X0, 0x28, 8);               // env block buffer (arg3 home)
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, inheritEnv);
    EmitBranchLink("__subp_build_envp");
    EmitStoreToStack(0xA0, ARM64Register.X0, 8);
    EmitStoreToStack(0xD8, ARM64Register.X0, 8);
    EmitBranch(envReady);
    DefineLabel(inheritEnv);
    EmitCallImport("_NSGetEnviron");
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitStoreToStack(0xA0, ARM64Register.X0, 8);
    DefineLabel(envReady);

    // posix_spawnp(&pid, file, &fa, NULL, argv, envp) — like posix_spawn but does an
    // execvp-style PATH search when `file` has no slash. `Executable.name(n)` whose
    // resolver missed reaches the spawn as a bare argv[0] (the stdlib's documented
    // fallback), so PATH resolution has to happen here or a bare name like "dotnet"
    // never launches. With a slash `file` is used verbatim, matching posix_spawn for
    // `Executable.path`, so absolute-path spawns are unchanged.
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x68, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0x90, 8);               // file = argv[0]
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X29, 0x60, isAdd: true);
    EmitMovRegImm(ARM64Register.X3, 0);
    EmitLoadFromStack(ARM64Register.X4, 0x78, 8);               // argv
    EmitLoadFromStack(ARM64Register.X5, 0xA0, 8);               // envp
    EmitCallImport("posix_spawnp");

    // The kernel has copied the vector, so release it here — on the failure path as much as on the
    // success one, which is why the result is parked first rather than tested first.
    EmitStoreToStack(0xE0, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0xD8, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, envpNotOwned);
    EmitBranchLink("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel(envpNotOwned);
    EmitLoadFromStack(ARM64Register.X0, 0xE0, 8);

    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, spawnFail);

    // success: destroy fa, close parent write ends
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_destroy");
    EmitLoadFromStack(ARM64Register.X0, 0xA8, 8);
    EmitCmpImm(ARM64Register.X0, 2);
    EmitBranchCond(ARM64ConditionCode.Ne, skipCloseOut);
    EmitLoadFromStack(ARM64Register.X0, 0x54, 4);
    EmitCallImport("close");
    DefineLabel(skipCloseOut);
    EmitLoadFromStack(ARM64Register.X0, 0xB0, 8);
    EmitCmpImm(ARM64Register.X0, 2);
    EmitBranchCond(ARM64ConditionCode.Ne, skipCloseErr);
    EmitLoadFromStack(ARM64Register.X0, 0x5C, 4);
    EmitCallImport("close");
    DefineLabel(skipCloseErr);

    // stdin bytes(2) / hold(4) / delayed(5): the parent's copy of the child's READ
    // end is dead for all three, so it is closed before they diverge. `bytes` then
    // writes its payload and closes the write end, so the child sees EOF on fd0;
    // `hold` writes nothing and keeps the write end, which is the whole of what it
    // asks for — a child that reads fd0 BLOCKS in the kernel until the handle is
    // released; `delayed` waits and then does exactly what `bytes` does, so the
    // child's read blocks and then completes. Bounded spec payloads fit the pipe
    // buffer, so the single write() can't block.
    EmitSubpStdinPipeWanted(skipWriteStdin, $"__subp_sp_wantiwr_{n}");
    EmitLoadFromStack(ARM64Register.X0, 0xC0, 4);               // inPipe read (parent's copy)
    EmitCallImport("close");
    EmitLoadFromStack(ARM64Register.X0, 0xC8, 8);
    EmitCmpImm(ARM64Register.X0, StdinKindHold);
    EmitBranchCond(ARM64ConditionCode.Eq, holdStdin);

    // `delayed`: hold the pipe open, unwritten, for StdinDelayedFeedMs and then
    // fall into the `bytes` write below. The child's stdin already IS this pipe,
    // so the wait is the child's own read blocking in the kernel.
    //
    // ⚠ SPENT ON THIS THREAD RATHER THAN A pthread, and that costs the caller
    // nothing: `maxon_subprocess_wait_collect` WAITS FOR THE CHILD BEFORE IT
    // DRAINS EITHER PIPE, so between here and the child's exit this thread has
    // no work of its own — a feed thread would buy a stack and a lifetime to
    // manage for zero wall-clock. (x64/Windows spends it on the feed thread that
    // path already has, which is that lane's equally free answer.) A child whose
    // stdout could exceed the pipe buffer would deadlock — but it would deadlock
    // for the same reason without any delay at all, because nobody reads it
    // until it exits.
    //
    // The sleep is RESTATED each iteration rather than resumed from a remainder,
    // so no second timespec is needed: a signal-interrupted wait restarts, which
    // makes it slightly longer and never shorter, and the contract is "about a
    // second".
    EmitCmpImm(ARM64Register.X0, StdinKindDelayed);
    EmitBranchCond(ARM64ConditionCode.Ne, feedStdinNow);
    DefineLabel(delaySleep);
    EmitMovRegImm(ARM64Register.X0, StdinDelayedFeedMs / StdinDelayedMillisPerSecond);
    EmitStoreToStack(0xF0, ARM64Register.X0, 8);                 // tv_sec
    EmitMovRegImm(ARM64Register.X0, (StdinDelayedFeedMs % StdinDelayedMillisPerSecond) * TimerNanosPerMilli);
    EmitStoreToStack(0xF8, ARM64Register.X0, 8);                 // tv_nsec
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0xF0, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);                          // no remainder wanted
    EmitCallImport("nanosleep");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, delaySleep);
    DefineLabel(feedStdinNow);

    // strlen(stdinData) → x9 (byte scan; payload carries no embedded NUL).
    EmitLoadFromStack(ARM64Register.X10, 0xD0, 8);              // p = stdinData
    EmitMovRegImm(ARM64Register.X9, 0);                          // len = 0
    string inStrlenLoop = $"__subp_sp_inlen_{n}";
    string inStrlenDone = $"__subp_sp_inlend_{n}";
    DefineLabel(inStrlenLoop);
    EmitLoadIndirect(ARM64Register.X16, ARM64Register.X10, 0, 1);
    EmitCbz(ARM64Register.X16, inStrlenDone);
    EmitAddSubImm(ARM64Register.X10, ARM64Register.X10, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: true);
    EmitBranch(inStrlenLoop);
    DefineLabel(inStrlenDone);
    // write(inPipe.write, stdinData, len)
    EmitLoadFromStack(ARM64Register.X0, 0xC4, 4);               // inPipe write
    EmitLoadFromStack(ARM64Register.X1, 0xD0, 8);              // stdinData
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9);          // len
    EmitCallImport("write");
    // close the write end → child sees EOF on fd0.
    EmitLoadFromStack(ARM64Register.X0, 0xC4, 4);
    EmitCallImport("close");
    EmitBranch(skipWriteStdin);

    // `hold`: record the write end on the handle instead of closing it, so it stays
    // open for exactly as long as the handle does and release_handle is the single
    // place that ends it — on the timeout path and on every error path alike.
    // SIGN-EXTENDED, like every other descriptor that reaches the handle: the slot
    // is 4 bytes and still holds -1 if `pipe()` failed, and a zero-extending load
    // would turn that into 4294967295 — a positive number release_handle's `fd >= 0`
    // test would accept and hand to close().
    DefineLabel(holdStdin);
    EmitLoadIndirectSignExtend(ARM64Register.X0, ARM64Register.X29, 0xC4, 4);
    EmitStoreToStack(0xE8, ARM64Register.X0, 8);
    DefineLabel(skipWriteStdin);

    // build handle (unified layout, shared with the streaming path)
    EmitMovRegImm(ARM64Register.X0, SubpHandleSize);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);          // handle (no calls follow)
    EmitLoadFromStack(ARM64Register.X1, 0x68, 4);               // pid (int)
    EmitStoreIndirect(ARM64Register.X9, SubpHOffPid, ARM64Register.X1, 8);
    EmitLoadIndirectSignExtend(ARM64Register.X1, ARM64Register.X29, 0x50, 4); // outReadFd
    EmitStoreIndirect(ARM64Register.X9, SubpHOffOutFd, ARM64Register.X1, 8);
    EmitLoadIndirectSignExtend(ARM64Register.X1, ARM64Register.X29, 0x58, 4); // errReadFd
    EmitStoreIndirect(ARM64Register.X9, SubpHOffErrFd, ARM64Register.X1, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x78, 8);              // argv
    EmitStoreIndirect(ARM64Register.X9, SubpHOffArgv, ARM64Register.X1, 8);
    // Capture ceilings (stack args 9 and 12), which wait_collect sizes its capture
    // buffers from. Non-collect streams carry 0 and never reach the drain anyway.
    EmitLoadFromStack(ARM64Register.X1, 0x108, 8);             // stdoutLimit
    EmitStoreIndirect(ARM64Register.X9, SubpHOffOutLimit, ARM64Register.X1, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x120, 8);             // stderrLimit
    EmitStoreIndirect(ARM64Register.X9, SubpHOffErrLimit, ARM64Register.X1, 8);
    // Unified-handle tail: sync spawns have no streaming line buffers, so zero the
    // line-buffer quads (release_handle then skips them). stdinWriteFd is -1 for
    // every sync spawn BUT `hold`, which is defined by keeping that descriptor —
    // hence the slot (frame +0xE8, written by the stdin setup above) rather than
    // the immediate this used to be, so release_handle closes it there exactly as
    // it does for a streaming handle.
    EmitLoadFromStack(ARM64Register.X1, 0xE8, 8);
    EmitStoreIndirect(ARM64Register.X9, SubpHOffStdinFd, ARM64Register.X1, 8);
    foreach (int off in new[] { 0x28, 0x30, 0x38, 0x40, 0x48, 0x50, 0x58, 0x60 })
      EmitStoreIndirect(ARM64Register.X9, off, ARM64Register.Xzr, 8);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitRuntimeFunctionEnd();

    // failure: capture errno, destroy fa, close pipe ends, free argv, return -1
    DefineLabel(spawnFail);
    EmitCaptureErrnoToGt();
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_destroy");
    EmitSubpCloseFdSlotIfValid(0x50, n, "a");
    EmitSubpCloseFdSlotIfValid(0x54, n, "b");
    EmitSubpCloseFdSlotIfValid(0x58, n, "c");
    EmitSubpCloseFdSlotIfValid(0x5C, n, "d");
    EmitSubpCloseFdSlotIfValid(0xC0, n, "e");
    EmitSubpCloseFdSlotIfValid(0xC4, n, "f");
    EmitLoadFromStack(ARM64Register.X0, 0x78, 8);
    EmitBranchLink("mm_raw_free");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// Close the 4-byte fd at frame slot `disp` if it is >= 0 (sign-extended).
  private void EmitSubpCloseFdSlotIfValid(int disp, int uniq, string tag) {
    string skip = $"__subp_sp_skfd_{tag}_{uniq}";
    EmitLoadIndirectSignExtend(ARM64Register.X0, ARM64Register.X29, disp, 4);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, skip);
    EmitCallImport("close");
    DefineLabel(skip);
  }

  /// maxon_subprocess_wait_collect(handle, timeoutMs) -> result | -1.
  ///
  /// ⭐ ONE INTERLEAVED LOOP: drain stdout, drain stderr, then poll for the child.
  /// Each of the three is bounded and none of them waits, so no one of them can
  /// starve the other two. THE WAIT USED TO RUN FIRST, and that was a deadlock:
  /// a child writing more than a pipe buffer (65,536 bytes on macOS, MEASURED to
  /// the byte) blocked in write() while this function slept in waitpid(), until
  /// the caller's timeout SIGKILLed it. `seq 1 20000` — 108,894 bytes — reproduced
  /// it every time. `maxon-shv2`'s buildSubpWaitCollect drains the same way for the
  /// same reason; the x64 twin gets there differently, with an OS thread per stream.
  ///
  /// ⚠ THE LOOP FINISHES ONLY WHEN BOTH STREAMS ARE AT EOF **AND** THE CHILD IS
  /// REAPED. Either condition alone is a truncation bug: a child can close its
  /// pipes and go on running, and a child can exit with bytes still in a pipe.
  ///
  /// timeoutMs > 0 arms an absolute wall-clock deadline (not an iteration count, so
  /// it does not dilate under load). On expiry the child is SIGKILLed and the
  /// deadline is re-armed SubpKillDrainGraceMs later, so the bytes the child had
  /// already written are still collected before the timedOut/124 status is
  /// reported; the second expiry gives up. The kill only ever runs while the child
  /// is UNREAPED, so the pid cannot have been recycled onto an unrelated process.
  /// timeoutMs <= 0 waits for ever. durationMs is the wall-clock elapsed
  /// (endMs - startMs), matching the x64/Windows twin.
  private void EmitMaxonSubprocessWaitCollectPosix() {
    // mrt_panic prints its argument as a complete line, so the text carries its own
    // newline (see EmitMaxonDivByZero).
    DefineSymdata(SubpCaptureOomMessage,
      System.Text.Encoding.UTF8.GetBytes("subprocess output capture: cannot reserve the requested capture limit\n\0"));

    EmitRuntimeFunctionStart("maxon_subprocess_wait_collect", 2, SubpWcFrame);
    int n = _uniqueLabelCounter++;
    string badHandle = $"__subp_wc_bad_{n}";
    string noDeadline = $"__subp_wc_nodl_{n}";
    string loopTop = $"__subp_wc_loop_{n}";
    string afterReap = $"__subp_wc_reapd_{n}";
    string deadlineCheck = $"__subp_wc_dl_{n}";
    string idleCheck = $"__subp_wc_idle_{n}";
    string giveUp = $"__subp_wc_give_{n}";
    string reapDone = $"__subp_wc_rdone_{n}";
    string signaled = $"__subp_wc_sig_{n}";
    string statusDone = $"__subp_wc_sdone_{n}";
    string timedOutStatus = $"__subp_wc_tos_{n}";

    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, badHandle);
    EmitStoreToStack(SubpWcHandle, ARM64Register.X0, 8);
    EmitStoreToStack(SubpWcTimedOut, ARM64Register.Xzr, 8);
    EmitStoreToStack(SubpWcReaped, ARM64Register.Xzr, 8);
    EmitStoreToStack(SubpWcKilled, ARM64Register.Xzr, 8);
    EmitSubpInitStreamState(SubpWcOutState, SubpHOffOutFd, SubpHOffOutLimit, n, "o");
    EmitSubpInitStreamState(SubpWcErrState, SubpHOffErrFd, SubpHOffErrLimit, n, "e");

    // Absolute deadline, so re-arming it after the kill is one add.
    EmitBranchLink("maxon_current_time_ms");
    EmitStoreToStack(SubpWcStartMs, ARM64Register.X0, 8);
    EmitStoreToStack(SubpWcDeadline, ARM64Register.Xzr, 8);
    EmitReloadArg(1);                                            // timeoutMs
    EmitCmpImm(ARM64Register.X1, 0);
    EmitBranchCond(ARM64ConditionCode.Le, noDeadline);
    EmitLoadFromStack(ARM64Register.X0, SubpWcStartMs, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1);
    EmitStoreToStack(SubpWcDeadline, ARM64Register.X0, 8);
    DefineLabel(noDeadline);

    DefineLabel(loopTop);
    EmitStoreToStack(SubpWcProgress, ARM64Register.Xzr, 8);
    EmitSubpDrainPassInto(SubpWcOutState, SubpHOffOutFd);
    EmitSubpDrainPassInto(SubpWcErrState, SubpHOffErrFd);

    // Reap without waiting, once.
    EmitLoadFromStack(ARM64Register.X0, SubpWcReaped, 8);
    EmitCbnz(ARM64Register.X0, afterReap);
    EmitLoadFromStack(ARM64Register.X0, SubpWcHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, SubpHOffPid, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, SubpWcStatus, isAdd: true);
    EmitMovRegImm(ARM64Register.X2, WaitNoHang);
    EmitCallImport("waitpid");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, afterReap);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitStoreToStack(SubpWcReaped, ARM64Register.X0, 8);
    DefineLabel(afterReap);

    EmitLoadFromStack(ARM64Register.X0, SubpWcOutState + SubpStateDone, 8);
    EmitCbz(ARM64Register.X0, deadlineCheck);
    EmitLoadFromStack(ARM64Register.X0, SubpWcErrState + SubpStateDone, 8);
    EmitCbz(ARM64Register.X0, deadlineCheck);
    EmitLoadFromStack(ARM64Register.X0, SubpWcReaped, 8);
    EmitCbnz(ARM64Register.X0, reapDone);                        // both EOF and reaped

    DefineLabel(deadlineCheck);
    EmitLoadFromStack(ARM64Register.X0, SubpWcDeadline, 8);
    EmitCbz(ARM64Register.X0, idleCheck);                        // 0 = wait for ever
    EmitBranchLink("maxon_current_time_ms");
    EmitLoadFromStack(ARM64Register.X1, SubpWcDeadline, 8);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Lt, idleCheck);
    EmitLoadFromStack(ARM64Register.X1, SubpWcKilled, 8);
    EmitCbnz(ARM64Register.X1, giveUp);                          // the post-kill grace ran out
    EmitLoadFromStack(ARM64Register.X1, SubpWcReaped, 8);
    EmitCbnz(ARM64Register.X1, giveUp);                          // never signal a reaped pid
    EmitLoadFromStack(ARM64Register.X1, SubpWcHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X1, SubpHOffPid, 8);
    EmitMovRegImm(ARM64Register.X1, SignalKill);
    EmitCallImport("kill");
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitStoreToStack(SubpWcTimedOut, ARM64Register.X0, 8);
    EmitStoreToStack(SubpWcKilled, ARM64Register.X0, 8);
    EmitBranchLink("maxon_current_time_ms");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, SubpKillDrainGraceMs, isAdd: true);
    EmitStoreToStack(SubpWcDeadline, ARM64Register.X0, 8);

    // A pass that moved bytes goes straight round again; one that moved none yields.
    DefineLabel(idleCheck);
    EmitLoadFromStack(ARM64Register.X0, SubpWcProgress, 8);
    EmitCbnz(ARM64Register.X0, loopTop);
    EmitStoreToStack(SubpWcTimespec, ARM64Register.Xzr, 8);      // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 1000000);                    // tv_nsec = 1ms
    EmitStoreToStack(SubpWcTimespec + 8, ARM64Register.X0, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, SubpWcTimespec, isAdd: true);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitCallImport("nanosleep");
    EmitBranch(loopTop);

    // Gave up on a stream some other process still holds open: the child is dead
    // (we SIGKILLed it) but unreaped, so a blocking waitpid returns at once.
    DefineLabel(giveUp);
    EmitLoadFromStack(ARM64Register.X0, SubpWcReaped, 8);
    EmitCbnz(ARM64Register.X0, reapDone);
    EmitLoadFromStack(ARM64Register.X0, SubpWcHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, SubpHOffPid, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, SubpWcStatus, isAdd: true);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitCallImport("waitpid");
    DefineLabel(reapDone);

    // Close both read ends and null the handle fields, so the shared
    // release_handle (which closes any fd >= 0) can't double-close a descriptor
    // the OS may have recycled for another concurrent worker's pipe.
    EmitSubpCloseAndClearFd(SubpHOffOutFd, n, "o");
    EmitSubpCloseAndClearFd(SubpHOffErrFd, n, "e");

    // decode status (timedOutFlag → timedOut(2)/124 sentinel; else POSIX status)
    EmitLoadFromStack(ARM64Register.X0, SubpWcTimedOut, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, timedOutStatus);
    EmitLoadFromStack(ARM64Register.X9, SubpWcStatus, 4);
    EmitMovRegImm(ARM64Register.X10, 0x7f);
    EmitAluRegReg(0x8A000000, ARM64Register.X11, ARM64Register.X9, ARM64Register.X10); // lowsig
    EmitCmpImm(ARM64Register.X11, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, signaled);
    EmitMovRegImm(ARM64Register.X12, 0);                         // exited kind=0
    EmitStoreToStack(SubpWcKind, ARM64Register.X12, 8);
    EmitLsrImm(ARM64Register.X12, ARM64Register.X9, 8);
    EmitMovRegImm(ARM64Register.X13, 0xff);
    EmitAluRegReg(0x8A000000, ARM64Register.X12, ARM64Register.X12, ARM64Register.X13); // code = (status>>8)&0xff
    EmitStoreToStack(SubpWcCode, ARM64Register.X12, 8);
    EmitBranch(statusDone);
    DefineLabel(signaled);
    EmitMovRegImm(ARM64Register.X12, 1);                         // signalled kind=1
    EmitStoreToStack(SubpWcKind, ARM64Register.X12, 8);
    EmitStoreToStack(SubpWcCode, ARM64Register.X11, 8);          // code = lowsig
    EmitBranch(statusDone);
    DefineLabel(timedOutStatus);
    EmitMovRegImm(ARM64Register.X12, 2);                         // timedOut kind=2
    EmitStoreToStack(SubpWcKind, ARM64Register.X12, 8);
    EmitMovRegImm(ARM64Register.X12, SubpTimedOutExitCode);
    EmitStoreToStack(SubpWcCode, ARM64Register.X12, 8);
    DefineLabel(statusDone);

    EmitSubpFinishStream(SubpWcOutState, SubpWcOutBuf, n, "o");
    EmitSubpFinishStream(SubpWcErrState, SubpWcErrBuf, n, "e");

    // Wall-clock elapsed = now - startMs, computed BEFORE the result alloc: X9
    // holds the result pointer through the store loop with no call between, and
    // maxon_current_time_ms is a call — computing it here and stashing elapsed in
    // the now-dead startMs slot keeps the alloc→store window call-free.
    EmitBranchLink("maxon_current_time_ms");                     // X0 = endMs
    EmitLoadFromStack(ARM64Register.X1, SubpWcStartMs, 8);
    EmitAluRegReg(0xCB000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1);
    EmitStoreToStack(SubpWcStartMs, ARM64Register.X0, 8);        // elapsedMs (reuses the slot)

    // build result (0x38): kind, code, stdoutBuf, stdoutLen, stderrBuf, stderrLen, duration
    EmitMovRegImm(ARM64Register.X0, 0x38);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);          // result (no calls follow)
    EmitLoadFromStack(ARM64Register.X1, SubpWcKind, 8);
    EmitStoreIndirect(ARM64Register.X9, 0, ARM64Register.X1, 8);  // statusKind
    EmitLoadFromStack(ARM64Register.X1, SubpWcCode, 8);
    EmitStoreIndirect(ARM64Register.X9, 8, ARM64Register.X1, 8);  // statusCode
    EmitLoadFromStack(ARM64Register.X1, SubpWcOutBuf, 8);
    EmitStoreIndirect(ARM64Register.X9, 16, ARM64Register.X1, 8); // stdoutBuf
    EmitLoadFromStack(ARM64Register.X1, SubpWcOutState + SubpStateLen, 8);
    EmitStoreIndirect(ARM64Register.X9, 24, ARM64Register.X1, 8); // stdoutLen
    EmitLoadFromStack(ARM64Register.X1, SubpWcErrBuf, 8);
    EmitStoreIndirect(ARM64Register.X9, 32, ARM64Register.X1, 8); // stderrBuf
    EmitLoadFromStack(ARM64Register.X1, SubpWcErrState + SubpStateLen, 8);
    EmitStoreIndirect(ARM64Register.X9, 40, ARM64Register.X1, 8); // stderrLen
    EmitLoadFromStack(ARM64Register.X1, SubpWcStartMs, 8);       // elapsedMs
    EmitStoreIndirect(ARM64Register.X9, 48, ARM64Register.X1, 8); // durationMs
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitRuntimeFunctionEnd();

    DefineLabel(badHandle);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// Set up one stream's 4-quad drain state in the wait_collect frame. A stream
  /// with no pipe (inherit/discard/file leave the fd at -1) is DONE before the
  /// first pass. A collected one reserves the CALLER'S capture limit — the value
  /// `OutputDestination.collect(limit)` carried through spawn into the handle —
  /// plus one chunk of bit bucket past it. The mapping is MAP_ANON, so the pages
  /// behind a 16 MiB default limit are only committed as the child fills them.
  private void EmitSubpInitStreamState(int stateSlot, int fdOff, int limitOff, int uniq, string tag) {
    string skip = $"__subp_wc_nostream_{tag}_{uniq}";
    string mapped = $"__subp_wc_mapped_{tag}_{uniq}";

    EmitStoreToStack(stateSlot + SubpStateBuf, ARM64Register.Xzr, 8);
    EmitStoreToStack(stateSlot + SubpStateLen, ARM64Register.Xzr, 8);
    EmitStoreToStack(stateSlot + SubpStateCap, ARM64Register.Xzr, 8);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitStoreToStack(stateSlot + SubpStateDone, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X9, SubpWcHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, fdOff, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, skip);
    EmitStoreToStack(stateSlot + SubpStateDone, ARM64Register.Xzr, 8);
    EmitLoadFromStack(ARM64Register.X9, SubpWcHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, limitOff, 8);
    EmitStoreToStack(stateSlot + SubpStateCap, ARM64Register.X0, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X0, SubpDrainChunk, isAdd: true);
    EmitMmapAnon();                                              // X1 = length
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Gt, mapped);               // MAP_FAILED is -1
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, SubpCaptureOomMessage);
    EmitBranch("mrt_panic");
    DefineLabel(mapped);
    EmitStoreToStack(stateSlot + SubpStateBuf, ARM64Register.X0, 8);
    DefineLabel(skip);
  }

  /// One bounded drain pass over `stateSlot`'s stream, accumulating the bytes it
  /// moved into the loop's progress counter.
  private void EmitSubpDrainPassInto(int stateSlot, int fdOff) {
    EmitLoadFromStack(ARM64Register.X9, SubpWcHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, fdOff, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, stateSlot, isAdd: true);
    EmitBranchLink("__subp_drain_pass");
    EmitLoadFromStack(ARM64Register.X9, SubpWcProgress, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X9, ARM64Register.X9, ARM64Register.X0);
    EmitStoreToStack(SubpWcProgress, ARM64Register.X9, 8);
  }

  /// Close the handle's read end for one stream and null the field, so the shared
  /// release_handle cannot close a descriptor the OS has since recycled.
  private void EmitSubpCloseAndClearFd(int fdOff, int uniq, string tag) {
    EmitSubpCloseHandleFd(SubpWcHandle, fdOff, uniq, tag);
    EmitLoadFromStack(ARM64Register.X9, SubpWcHandle, 8);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitStoreIndirect(ARM64Register.X9, fdOff, ARM64Register.X0, 8);
  }

  /// Copy one stream's captured bytes into a right-sized mm_raw_alloc'd buffer
  /// (the result struct's shape), then release the scratch mapping — whose length
  /// is the capture ceiling plus the bit-bucket chunk EmitSubpInitStreamState added.
  private void EmitSubpFinishStream(int stateSlot, int outSlot, int uniq, string tag) {
    string skipCopy = $"__subp_wc_skcopy_{tag}_{uniq}";
    string skipUnmap = $"__subp_wc_skunmap_{tag}_{uniq}";

    EmitStoreToStack(outSlot, ARM64Register.Xzr, 8);
    EmitLoadFromStack(ARM64Register.X0, stateSlot + SubpStateLen, 8);
    EmitCbz(ARM64Register.X0, skipCopy);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(outSlot, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X1, stateSlot + SubpStateBuf, 8);
    EmitLoadFromStack(ARM64Register.X2, stateSlot + SubpStateLen, 8);
    EmitBranchLink("maxon_memcpy");                              // X0 is still the destination
    DefineLabel(skipCopy);
    EmitLoadFromStack(ARM64Register.X0, stateSlot + SubpStateBuf, 8);
    EmitCbz(ARM64Register.X0, skipUnmap);
    EmitLoadFromStack(ARM64Register.X1, stateSlot + SubpStateCap, 8);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, SubpDrainChunk, isAdd: true);
    EmitCallImport("munmap");
    DefineLabel(skipUnmap);
  }

  private void EmitMaxonSubprocessGetPidPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_get_pid", 1, 0x30);
    int n = _uniqueLabelCounter++;
    string bad = $"__subp_gp_bad_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, bad);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitRuntimeFunctionEnd();
    DefineLabel(bad);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// Result accessor: return the field at `fieldOffset` (or 0 for a null result).
  private void EmitSubprocessResultAccessor(string name, int fieldOffset) {
    EmitRuntimeFunctionStart(name, 1, 0x30);
    int n = _uniqueLabelCounter++;
    string bad = $"__subp_acc_bad_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, bad);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, fieldOffset, 8);
    EmitRuntimeFunctionEnd();
    DefineLabel(bad);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonSubprocessResultReleasePosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_result_release", 1, 0x30);
    int n = _uniqueLabelCounter++;
    string done = $"__subp_rr_done_{n}";
    string noOut = $"__subp_rr_noout_{n}";
    string noErr = $"__subp_rr_noerr_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, done);
    EmitStoreToStack(0x18, ARM64Register.X0, 8);
    // free stdoutBuf (+16)
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 16, 8);
    EmitCbz(ARM64Register.X0, noOut);
    EmitBranchLink("mm_raw_free");
    DefineLabel(noOut);
    // free stderrBuf (+32)
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 32, 8);
    EmitCbz(ARM64Register.X0, noErr);
    EmitBranchLink("mm_raw_free");
    DefineLabel(noErr);
    // free the result struct
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8);
    EmitBranchLink("mm_raw_free");
    DefineLabel(done);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonSubprocessReleaseHandlePosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_release_handle", 1, 0x30);
    int n = _uniqueLabelCounter++;
    string done = $"__subp_rh_done_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, done);
    EmitStoreToStack(SubpRhHandle, ARM64Register.X0, 8);
    // Close any still-open descriptor (streaming handles keep stdout/stderr read
    // ends and possibly stdin write end open; sync handles already nulled theirs).
    EmitSubpCloseHandleFd(SubpRhHandle, SubpHOffOutFd, n, "ro");
    EmitSubpCloseHandleFd(SubpRhHandle, SubpHOffErrFd, n, "re");
    EmitSubpCloseHandleFd(SubpRhHandle, SubpHOffStdinFd, n, "ri");
    // Free any streaming line buffers (NULL on sync handles).
    EmitSubpReleaseFreeField(SubpHOffOutBuf + SubpQuadBuf, n, "bo");
    EmitSubpReleaseFreeField(SubpHOffErrBuf + SubpQuadBuf, n, "be");
    // Free argv, then the struct itself.
    EmitSubpReleaseFreeField(SubpHOffArgv, n, "av");
    EmitLoadFromStack(ARM64Register.X0, SubpRhHandle, 8);
    EmitBranchLink("mm_raw_free");
    DefineLabel(done);
    EmitRuntimeFunctionEnd();
  }

  /// Close the descriptor in handle field `fdOff` if it is >= 0. `handleSlot` is
  /// the caller's frame slot holding the handle pointer, which differs per
  /// function; everything else about the close is the same wherever it happens.
  private void EmitSubpCloseHandleFd(int handleSlot, int fdOff, int uniq, string tag) {
    string skip = $"__subp_skfd_{tag}_{uniq}";
    EmitLoadFromStack(ARM64Register.X9, handleSlot, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, fdOff, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, skip);
    EmitCallImport("close");
    DefineLabel(skip);
  }

  /// mm_raw_free the pointer at release_handle's handle field `off` if non-null.
  private void EmitSubpReleaseFreeField(int off, int uniq, string tag) {
    string skip = $"__subp_rh_skfree_{tag}_{uniq}";
    EmitLoadFromStack(ARM64Register.X9, SubpRhHandle, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, off, 8);
    EmitCbz(ARM64Register.X0, skip);
    EmitBranchLink("mm_raw_free");
    DefineLabel(skip);
  }

  /// maxon_managed_is_null(mm_buffer) -> 1 when null, 0 otherwise. The
  /// MaxonCallRuntime lowering unwraps the __ManagedMemory arg to its buffer
  /// pointer (MaxonToStandardConversion.cs), so we receive the buffer address,
  /// not the struct — which RuntimeCallToManaged never leaves null. Mirror the
  /// x64 EmitMaxonManagedIsNull contract: "null" means the buffer is absent OR
  /// starts with NUL (an empty cstring — the resolve_on_path / last_error_message
  /// not-found sentinel round-trips through here).
  private void EmitMaxonManagedIsNullPosix() {
    EmitRuntimeFunctionStart("maxon_managed_is_null", 1, 0x30);
    int n = _uniqueLabelCounter++;
    string isNull = $"__subp_in_null_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, isNull);                            // null buffer ptr → null
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, 0, 1);   // first byte
    EmitCbz(ARM64Register.X1, isNull);                            // leading NUL (empty) → null
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
    DefineLabel(isNull);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Streaming subprocess runtime (arm64-macos)
  //
  // Backs StreamingSubprocess (stdlib/Subprocess.maxon) — a long-lived child
  // whose stdin/stdout/stderr the parent drives line-by-line. The parallel
  // spec-test worker pool (runAllSpecTestsParallel) spawns N of these, feeds
  // `JOB:` lines to stdin, and drains result lines from stdout via async green
  // threads. Reads are cooperative: __io_submit_read parks the GT on kqueue
  // EVFILT_READ and resumes it when the pipe is readable, so N drain GTs make
  // progress concurrently without pinning OS threads.
  //
  // Unified handle struct (mm_raw_alloc, SubpHandleSize), shared with the
  // synchronous Subprocess.run path so the single maxon_subprocess_release_handle
  // cleans up either shape:
  //   +0x00 pid           +0x08 stdoutReadFd   +0x10 stderrReadFd   +0x18 argv[]
  //   +0x20 stdinWriteFd  (-1 for sync spawns other than `hold` / once close_stdin runs)
  //   +0x28 stdoutBuf     +0x30 stdoutLen      +0x38 stdoutCap      +0x40 stdoutEof
  //   +0x48 stderrBuf     +0x50 stderrLen      +0x58 stderrCap      +0x60 stderrEof
  //   +0x68 stdoutLimit   +0x70 stderrLimit    (the caller's capture ceilings)
  // Sync spawns leave the line-buffer fields 0, and stdinWriteFd -1 unless they were
  // asked for `StdinKindHold` (which is DEFINED by keeping that descriptor open for
  // the child's whole life); release_handle closes any fd >= 0 and frees any
  // non-null line buffer + argv + the struct.
  // The two limits carry OutputDestination.collect(limit) from the spawn call
  // through to wait_collect, which is where the capture buffer is sized. They
  // mirror the x64 handle's SubpOffStdoutLimit/SubpOffStderrLimit; before they
  // existed this lane sized every capture from an 8 MiB constant that sat
  // SILENTLY BELOW the stdlib's own 16 MiB default, so a 10 MiB capture truncated
  // with no diagnostic.
  // ==========================================================================

  private const int SubpHOffPid = 0x00;
  private const int SubpHOffOutFd = 0x08;
  private const int SubpHOffErrFd = 0x10;
  private const int SubpHOffArgv = 0x18;
  private const int SubpHOffStdinFd = 0x20;
  private const int SubpHOffOutBuf = 0x28;
  private const int SubpHOffErrBuf = 0x48;
  private const int SubpHOffOutLimit = 0x68;
  private const int SubpHOffErrLimit = 0x70;
  private const int SubpHandleSize = 0x78;
  // Line-buffer quad sub-offsets, relative to OutBuf/ErrBuf base.
  private const int SubpQuadBuf = 0;
  private const int SubpQuadLen = 8;
  private const int SubpQuadCap = 16;
  private const int SubpQuadEof = 24;
  private const int SubpStreamLineInitCap = 4096;
  // Re-grow the line buffer once free space drops below this so a single
  // __io_submit_read can deliver a useful chunk.
  private const int SubpStreamLineGrowFloor = 512;

  /// __subp_build_argv(bufStart_x0, argc_x1) -> argv[] in x0.
  /// Allocates (argc+1) pointers, points argv[0..argc-1] at the argc
  /// NUL-separated tokens packed in bufStart, NULL-terminates argv[argc].
  /// Shared by the sync (maxon_subprocess_spawn) and streaming spawn paths.
  /// Emit a walk over a NUL-separated, NUL-terminated environment block — the self-delimiting form
  /// `stdlib/Subprocess.maxon` assembles and hands to the spawn contract's `env` slot.
  ///
  /// X10 is the cursor and holds the first byte of one entry each time <paramref name="onEntry"/>
  /// runs; X11 and X16 are the walk's own scratch. The walk exists as ONE emitter because
  /// `__subp_build_envp` performs it twice — once to count the entries and once to fill the vector —
  /// and a second hand-written copy is where the two would come to disagree about where an entry
  /// ends.
  private void EmitEnvBlockWalk(string tag, System.Action onEntry) {
    int n = _uniqueLabelCounter++;
    string loopLabel = $"__subp_envw_{tag}_{n}";
    string scanLabel = $"__subp_envw_{tag}_scan_{n}";
    string doneLabel = $"__subp_envw_{tag}_done_{n}";

    DefineLabel(loopLabel);
    EmitLoadIndirect(ARM64Register.X16, ARM64Register.X10, 0, 1);
    EmitCmpImm(ARM64Register.X16, Runtime.SubprocessContract.BlobTokenTerminator);
    EmitBranchCond(ARM64ConditionCode.Eq, doneLabel);
    onEntry();
    // Advance past this entry's own bytes and then past its NUL.
    EmitMovRegReg(ARM64Register.X11, ARM64Register.X10);
    DefineLabel(scanLabel);
    EmitLoadIndirect(ARM64Register.X16, ARM64Register.X11, 0, 1);
    EmitAddSubImm(ARM64Register.X11, ARM64Register.X11, 1, isAdd: true);
    EmitCmpImm(ARM64Register.X16, Runtime.SubprocessContract.BlobTokenTerminator);
    EmitBranchCond(ARM64ConditionCode.Ne, scanLabel);
    EmitMovRegReg(ARM64Register.X10, ARM64Register.X11);
    EmitBranch(loopLabel);
    DefineLabel(doneLabel);
  }

  /// __subp_build_envp(block) -> char *const envp[] — the NUL-terminated vector posix_spawnp takes,
  /// built over the caller's environment block. Returns an mm_raw_alloc'd vector of POINTERS INTO
  /// THE BLOCK: nothing is copied, so the block must outlive the spawn (it does — it is a
  /// __ManagedMemory the stdlib holds across the call) and only the vector is freed afterwards.
  ///
  /// Two walks and one allocation between them, rather than one walk into a guessed-at capacity:
  /// the entry count is what sizes the vector and there is no bound on it worth guessing.
  /// Stack: [x29+0x20]=block, [x29+0x28]=entry count, [x29+0x30]=vector
  private void EmitSubpBuildEnvp() {
    EmitRuntimeFunctionStart("__subp_build_envp", 1, 0x40);
    EmitReloadArg(0);
    EmitStoreToStack(0x20, ARM64Register.X0, 8);

    // Count.
    EmitMovRegImm(ARM64Register.X9, 0);
    EmitLoadFromStack(ARM64Register.X10, 0x20, 8);
    EmitEnvBlockWalk("count", () => {
      EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: true);
    });
    EmitStoreToStack(0x28, ARM64Register.X9, 8);

    // (count + 1) * 8 — one slot per entry and the NULL that ends the vector.
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X9, 1, isAdd: true);
    EmitLslImm(ARM64Register.X0, ARM64Register.X0, 3);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x30, ARM64Register.X0, 8);

    // Fill.
    EmitMovRegImm(ARM64Register.X9, 0);
    EmitLoadFromStack(ARM64Register.X10, 0x20, 8);
    EmitLoadFromStack(ARM64Register.X14, 0x30, 8);
    EmitEnvBlockWalk("fill", () => {
      EmitLslImm(ARM64Register.X17, ARM64Register.X9, 3);
      EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X14, ARM64Register.X17);
      EmitStoreIndirect(ARM64Register.X17, 0, ARM64Register.X10, 8);
      EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: true);
    });

    EmitLoadFromStack(ARM64Register.X13, 0x28, 8);
    EmitLoadFromStack(ARM64Register.X14, 0x30, 8);
    EmitLslImm(ARM64Register.X17, ARM64Register.X13, 3);
    EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X14, ARM64Register.X17);
    EmitStoreIndirect(ARM64Register.X17, 0, ARM64Register.Xzr, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x30, 8);
    EmitRuntimeFunctionEnd();
  }

  private void EmitSubpBuildArgv() {
    EmitRuntimeFunctionStart("__subp_build_argv", 2, 0x40);
    int n = _uniqueLabelCounter++;
    string walkLoop = $"__subp_ba_walk_{n}";
    string walkNext = $"__subp_ba_wnext_{n}";
    string walkDone = $"__subp_ba_wdone_{n}";
    // slots: 0x20 bufStart, 0x28 argc, 0x30 argv
    EmitReloadArg(0); EmitStoreToStack(0x20, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8); EmitStoreToStack(0x28, ARM64Register.X0, 8); // argc (arg1 home)
    // argv = mm_raw_alloc((argc+1)*8)
    EmitLoadFromStack(ARM64Register.X0, 0x28, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLslImm(ARM64Register.X0, ARM64Register.X0, 3);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x30, ARM64Register.X0, 8);
    // argv[0] = bufStart
    EmitLoadFromStack(ARM64Register.X1, 0x20, 8);
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.X1, 8);
    // walk: x9=idx, x10=p, x13=argc, x14=argv
    EmitMovRegImm(ARM64Register.X9, 1);
    EmitLoadFromStack(ARM64Register.X10, 0x20, 8);
    EmitLoadFromStack(ARM64Register.X13, 0x28, 8);
    EmitLoadFromStack(ARM64Register.X14, 0x30, 8);
    DefineLabel(walkLoop);
    EmitCmpRegReg(ARM64Register.X9, ARM64Register.X13);
    EmitBranchCond(ARM64ConditionCode.Ge, walkDone);
    EmitLoadIndirect(ARM64Register.X16, ARM64Register.X10, 0, 1);
    EmitCmpImm(ARM64Register.X16, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, walkNext);
    EmitAddSubImm(ARM64Register.X15, ARM64Register.X10, 1, isAdd: true);
    EmitLslImm(ARM64Register.X17, ARM64Register.X9, 3);
    EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X14, ARM64Register.X17);
    EmitStoreIndirect(ARM64Register.X17, 0, ARM64Register.X15, 8);
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: true);
    DefineLabel(walkNext);
    EmitAddSubImm(ARM64Register.X10, ARM64Register.X10, 1, isAdd: true);
    EmitBranch(walkLoop);
    DefineLabel(walkDone);
    EmitLslImm(ARM64Register.X17, ARM64Register.X13, 3);
    EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X14, ARM64Register.X17);
    EmitStoreIndirect(ARM64Register.X17, 0, ARM64Register.Xzr, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x30, 8);
    EmitRuntimeFunctionEnd();
  }

  /// maxon_subprocess_spawn_streaming(argvBlob, argc, cwd, flags) -> handle | -1.
  /// posix_spawn the child with three pipes (parent writes stdin, reads
  /// stdout/stderr). `flags` is ignored: streaming spawns never detach and
  /// always inherit the environment.
  private void EmitMaxonSubprocessSpawnStreamingPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_spawn_streaming", 4, 0x100);
    int n = _uniqueLabelCounter++;
    string skipChdir = $"__subp_ss_skchdir_{n}";
    string spawnFail = $"__subp_ss_fail_{n}";
    // Locals: 0x40 inPipe(rd@0x40,wr@0x44) 0x48 outPipe(rd@0x48,wr@0x4C)
    //   0x50 errPipe(rd@0x50,wr@0x54) 0x60 fa 0x68 pid 0x70 handle 0x78 argv
    //   0x90 bufStart 0x98 argc 0xA0 envp

    // Pre-seed pipe fd slots to -1 so the failure path closes only real fds.
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitStoreToStack(0x40, ARM64Register.X0, 8);
    EmitStoreToStack(0x48, ARM64Register.X0, 8);
    EmitStoreToStack(0x50, ARM64Register.X0, 8);

    // argv = __subp_build_argv(bufStart, argc)
    EmitReloadArg(0); EmitStoreToStack(0x90, ARM64Register.X0, 8);   // bufStart (also path)
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8); EmitStoreToStack(0x98, ARM64Register.X0, 8); // argc
    EmitLoadFromStack(ARM64Register.X0, 0x90, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x98, 8);
    EmitBranchLink("__subp_build_argv");
    EmitStoreToStack(0x78, ARM64Register.X0, 8);

    // three pipes
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x40, isAdd: true); EmitCallImport("pipe");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x48, isAdd: true); EmitCallImport("pipe");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x50, isAdd: true); EmitCallImport("pipe");

    // file actions
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_init");

    // dup2 child ends: inRd->0, outWr->1, errWr->2
    EmitSubpStreamAddDup2(0x40, 0);
    EmitSubpStreamAddDup2(0x4C, 1);
    EmitSubpStreamAddDup2(0x54, 2);
    // close all six originals in the child so it only keeps 0/1/2
    foreach (int slot in new[] { 0x40, 0x44, 0x48, 0x4C, 0x50, 0x54 }) {
      EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
      EmitLoadFromStack(ARM64Register.X1, slot, 4);
      EmitCallImport("posix_spawn_file_actions_addclose");
    }

    // chdir if cwd non-empty
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8);                    // cwd cstr
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, 0, 1);
    EmitCmpImm(ARM64Register.X1, 0);
    EmitBranchCond(ARM64ConditionCode.Eq, skipChdir);
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_addchdir_np");
    DefineLabel(skipChdir);

    // environ
    EmitCallImport("_NSGetEnviron");
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, 0, 8);
    EmitStoreToStack(0xA0, ARM64Register.X0, 8);

    // posix_spawnp(&pid, file, &fa, NULL, argv, envp) — like posix_spawn but does an
    // execvp-style PATH search when `file` has no slash, so `Executable.name(n)` that
    // reaches the spawn as a bare argv[0] resolves against PATH (streaming path). With
    // a slash `file` is used verbatim, matching posix_spawn for `Executable.path`.
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x68, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, 0x90, 8);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X29, 0x60, isAdd: true);
    EmitMovRegImm(ARM64Register.X3, 0);
    EmitLoadFromStack(ARM64Register.X4, 0x78, 8);
    EmitLoadFromStack(ARM64Register.X5, 0xA0, 8);
    EmitCallImport("posix_spawnp");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, spawnFail);

    // success: destroy fa, close child ends held by parent (inRd, outWr, errWr)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_destroy");
    EmitLoadFromStack(ARM64Register.X0, 0x40, 4); EmitCallImport("close"); // inRd
    EmitLoadFromStack(ARM64Register.X0, 0x4C, 4); EmitCallImport("close"); // outWr
    EmitLoadFromStack(ARM64Register.X0, 0x54, 4); EmitCallImport("close"); // errWr

    // build unified handle
    EmitMovRegImm(ARM64Register.X0, SubpHandleSize);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x70, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X9, 0x70, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x68, 4);                    // pid
    EmitStoreIndirect(ARM64Register.X9, SubpHOffPid, ARM64Register.X1, 8);
    EmitLoadIndirectSignExtend(ARM64Register.X1, ARM64Register.X29, 0x48, 4); // outPipe[rd]
    EmitStoreIndirect(ARM64Register.X9, SubpHOffOutFd, ARM64Register.X1, 8);
    EmitLoadIndirectSignExtend(ARM64Register.X1, ARM64Register.X29, 0x50, 4); // errPipe[rd]
    EmitStoreIndirect(ARM64Register.X9, SubpHOffErrFd, ARM64Register.X1, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x78, 8);                    // argv
    EmitStoreIndirect(ARM64Register.X9, SubpHOffArgv, ARM64Register.X1, 8);
    EmitLoadIndirectSignExtend(ARM64Register.X1, ARM64Register.X29, 0x44, 4); // inPipe[wr]
    EmitStoreIndirect(ARM64Register.X9, SubpHOffStdinFd, ARM64Register.X1, 8);
    EmitSubpStreamInitLineBuf(SubpHOffOutBuf);
    EmitSubpStreamInitLineBuf(SubpHOffErrBuf);
    // A streaming child is read line-by-line, never wait_collect'd, so it has no
    // capture ceiling — but mm_raw_alloc does not zero, and a garbage limit read
    // as a buffer size is not a fault this handle should be able to carry.
    EmitLoadFromStack(ARM64Register.X9, 0x70, 8);
    EmitStoreIndirect(ARM64Register.X9, SubpHOffOutLimit, ARM64Register.Xzr, 8);
    EmitStoreIndirect(ARM64Register.X9, SubpHOffErrLimit, ARM64Register.Xzr, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x70, 8);
    EmitRuntimeFunctionEnd();

    // failure: destroy fa, close any open pipe fd, free argv, return -1
    DefineLabel(spawnFail);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_destroy");
    EmitSubpCloseFdSlotIfValid(0x40, n, "sa");
    EmitSubpCloseFdSlotIfValid(0x44, n, "sb");
    EmitSubpCloseFdSlotIfValid(0x48, n, "sc");
    EmitSubpCloseFdSlotIfValid(0x4C, n, "sd");
    EmitSubpCloseFdSlotIfValid(0x50, n, "se");
    EmitSubpCloseFdSlotIfValid(0x54, n, "sf");
    EmitLoadFromStack(ARM64Register.X0, 0x78, 8);
    EmitBranchLink("mm_raw_free");
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// posix_spawn_file_actions_adddup2(&fa@x29+0x60, pipeFd@slot, targetFd).
  private void EmitSubpStreamAddDup2(int fdSlot, int targetFd) {
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 0x60, isAdd: true);
    EmitLoadFromStack(ARM64Register.X1, fdSlot, 4);
    EmitMovRegImm(ARM64Register.X2, targetFd);
    EmitCallImport("posix_spawn_file_actions_adddup2");
  }

  /// Initialise the {buf, len, cap, eof} line-buffer quad at handle+bufOff
  /// (handle ptr in X9, preserved). Allocates an initial-capacity buffer.
  private void EmitSubpStreamInitLineBuf(int bufOff) {
    EmitMovRegImm(ARM64Register.X0, SubpStreamLineInitCap);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitLoadFromStack(ARM64Register.X9, 0x70, 8);                    // reload handle (alloc clobbers)
    EmitStoreIndirect(ARM64Register.X9, bufOff + SubpQuadBuf, ARM64Register.X0, 8);
    EmitStoreIndirect(ARM64Register.X9, bufOff + SubpQuadLen, ARM64Register.Xzr, 8);
    EmitMovRegImm(ARM64Register.X1, SubpStreamLineInitCap);
    EmitStoreIndirect(ARM64Register.X9, bufOff + SubpQuadCap, ARM64Register.X1, 8);
    EmitStoreIndirect(ARM64Register.X9, bufOff + SubpQuadEof, ARM64Register.Xzr, 8);
  }

  /// maxon_subprocess_write_stdin_all(handle, dataCstr) -> 0 | -1.
  /// Blocking write loop; the streaming protocol's job lines are small and the
  /// child drains stdin promptly, so this never blocks the OS thread in practice.
  private void EmitMaxonSubprocessWriteStdinAllPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_write_stdin_all", 2, 0x40);
    int n = _uniqueLabelCounter++;
    string bad = $"__subp_wsa_bad_{n}";
    string ok = $"__subp_wsa_ok_{n}";
    string slenLoop = $"__subp_wsa_slen_{n}";
    string slenDone = $"__subp_wsa_slend_{n}";
    string loop = $"__subp_wsa_loop_{n}";
    // slots: 0x20 fd, 0x28 cursor, 0x30 remaining
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, bad);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, SubpHOffStdinFd, 8);
    EmitCmpImm(ARM64Register.X1, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, bad);
    EmitStoreToStack(0x20, ARM64Register.X1, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8);                   // data cstr (arg1)
    EmitCbz(ARM64Register.X0, ok);                                  // null data -> nothing to write
    EmitStoreToStack(0x28, ARM64Register.X0, 8);                    // cursor = data
    // remaining = strlen(data) (inline)
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    EmitMovRegImm(ARM64Register.X10, 0);
    DefineLabel(slenLoop);
    EmitAluRegReg(0x8B000000, ARM64Register.X11, ARM64Register.X9, ARM64Register.X10);
    EmitLoadIndirect(ARM64Register.X12, ARM64Register.X11, 0, 1);
    EmitCbz(ARM64Register.X12, slenDone);
    EmitAddSubImm(ARM64Register.X10, ARM64Register.X10, 1, isAdd: true);
    EmitBranch(slenLoop);
    DefineLabel(slenDone);
    EmitStoreToStack(0x30, ARM64Register.X10, 8);
    DefineLabel(loop);
    EmitLoadFromStack(ARM64Register.X0, 0x30, 8);
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, ok);
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8);                   // fd
    EmitLoadFromStack(ARM64Register.X1, 0x28, 8);                   // cursor
    EmitLoadFromStack(ARM64Register.X2, 0x30, 8);                   // remaining
    EmitCallImport("write");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, bad);
    EmitLoadFromStack(ARM64Register.X1, 0x28, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X0);
    EmitStoreToStack(0x28, ARM64Register.X1, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x30, 8);
    EmitAluRegReg(0xCB000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X0);
    EmitStoreToStack(0x30, ARM64Register.X1, 8);
    EmitBranch(loop);
    DefineLabel(ok);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
    DefineLabel(bad);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// __subp_stream_emit_line(quadBase_x0, lineLen_x1, consume_x2) -> cstring.
  /// Copies quad.buf[0..lineLen) into a fresh NUL-terminated mm_raw_alloc'd
  /// cstring, then shifts the remaining quad.buf[consume..len) down to the front
  /// and updates quad.len. consume is normally lineLen+1 (drop the LF) or lineLen.
  private void EmitSubpStreamEmitLine() {
    EmitRuntimeFunctionStart("__subp_stream_emit_line", 3, 0x70);
    int n = _uniqueLabelCounter++;
    string shiftLoop = $"__subp_el_shift_{n}";
    string shiftDone = $"__subp_el_shdone_{n}";
    // slots: 0x28 quadBase, 0x30 lineLen, 0x38 consume, 0x40 result, 0x48 buf, 0x50 len
    EmitReloadArg(0); EmitStoreToStack(0x28, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8); EmitStoreToStack(0x30, ARM64Register.X0, 8); // lineLen (arg1)
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8); EmitStoreToStack(0x38, ARM64Register.X0, 8); // consume (arg2)
    EmitLoadFromStack(ARM64Register.X9, 0x28, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, SubpQuadBuf, 8); EmitStoreToStack(0x48, ARM64Register.X0, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, SubpQuadLen, 8); EmitStoreToStack(0x50, ARM64Register.X0, 8);
    // result = mm_raw_alloc(lineLen+1)
    EmitLoadFromStack(ARM64Register.X0, 0x30, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x40, ARM64Register.X0, 8);
    // maxon_memcpy(result, buf, lineLen)
    EmitLoadFromStack(ARM64Register.X0, 0x40, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x48, 8);
    EmitLoadFromStack(ARM64Register.X2, 0x30, 8);
    EmitBranchLink("maxon_memcpy");
    // result[lineLen] = 0
    EmitLoadFromStack(ARM64Register.X0, 0x40, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x30, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1);
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1);
    // remaining = len - consume ; shift buf[consume..len) down to buf[0..)
    EmitLoadFromStack(ARM64Register.X12, 0x38, 8);                  // consume
    EmitLoadFromStack(ARM64Register.X13, 0x50, 8);                  // len
    EmitAluRegReg(0xCB000000, ARM64Register.X13, ARM64Register.X13, ARM64Register.X12); // remaining
    EmitLoadFromStack(ARM64Register.X10, 0x48, 8);                  // buf
    EmitMovRegImm(ARM64Register.X14, 0);                            // i
    DefineLabel(shiftLoop);
    EmitCmpRegReg(ARM64Register.X14, ARM64Register.X13);
    EmitBranchCond(ARM64ConditionCode.Ge, shiftDone);
    EmitAluRegReg(0x8B000000, ARM64Register.X15, ARM64Register.X10, ARM64Register.X12); // buf+consume
    EmitAluRegReg(0x8B000000, ARM64Register.X15, ARM64Register.X15, ARM64Register.X14); // +i
    EmitLoadIndirect(ARM64Register.X16, ARM64Register.X15, 0, 1);
    EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X10, ARM64Register.X14); // buf+i
    EmitStoreIndirect(ARM64Register.X17, 0, ARM64Register.X16, 1);
    EmitAddSubImm(ARM64Register.X14, ARM64Register.X14, 1, isAdd: true);
    EmitBranch(shiftLoop);
    DefineLabel(shiftDone);
    // quad.len = remaining
    EmitLoadFromStack(ARM64Register.X9, 0x28, 8);
    EmitStoreIndirect(ARM64Register.X9, SubpQuadLen, ARM64Register.X13, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x40, 8);
    EmitRuntimeFunctionEnd();
  }

  /// __subp_stream_read_line(maxBytes_x0, fd_x1, quadBase_x2) -> cstring.
  /// Cooperative buffered line reader. Returns the next line (LF stripped) from
  /// the {buf,len,cap,eof} quad as a fresh NUL-terminated cstring, refilling via
  /// __io_submit_read (kqueue-parked yield) when no full line is buffered. Empty
  /// cstring == EOF with nothing left. Lines longer than maxBytes are truncated
  /// and the remainder is delivered on the next call.
  private void EmitSubpStreamReadLine() {
    EmitRuntimeFunctionStart("__subp_stream_read_line", 3, 0x70);
    int n = _uniqueLabelCounter++;
    string loop = $"__subp_rl_loop_{n}";
    string scanLoop = $"__subp_rl_scan_{n}";
    string scanDone = $"__subp_rl_scandone_{n}";
    string noNl = $"__subp_rl_nonl_{n}";
    string checkEof = $"__subp_rl_eof_{n}";
    string needMore = $"__subp_rl_more_{n}";
    string doRead = $"__subp_rl_read_{n}";
    string gotEof = $"__subp_rl_goteof_{n}";
    // slots: 0x28 maxBytes, 0x30 fd, 0x38 quadBase, 0x40 (idx/newCap/n), 0x48 newBuf
    EmitReloadArg(0); EmitStoreToStack(0x28, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8); EmitStoreToStack(0x30, ARM64Register.X0, 8); // fd (arg1)
    EmitLoadFromStack(ARM64Register.X0, 0x20, 8); EmitStoreToStack(0x38, ARM64Register.X0, 8); // quadBase (arg2)

    DefineLabel(loop);
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X10, ARM64Register.X9, SubpQuadBuf, 8);  // buf
    EmitLoadIndirect(ARM64Register.X11, ARM64Register.X9, SubpQuadLen, 8);  // len
    // scan buf[0..len) for LF -> X3 = idx, or len if none
    EmitMovRegImm(ARM64Register.X3, 0);
    DefineLabel(scanLoop);
    EmitCmpRegReg(ARM64Register.X3, ARM64Register.X11);
    EmitBranchCond(ARM64ConditionCode.Ge, scanDone);
    EmitAluRegReg(0x8B000000, ARM64Register.X4, ARM64Register.X10, ARM64Register.X3);
    EmitLoadIndirect(ARM64Register.X5, ARM64Register.X4, 0, 1);
    EmitCmpImm(ARM64Register.X5, 0x0A);
    EmitBranchCond(ARM64ConditionCode.Eq, scanDone);
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitBranch(scanLoop);
    DefineLabel(scanDone);
    EmitCmpRegReg(ARM64Register.X3, ARM64Register.X11);
    EmitBranchCond(ARM64ConditionCode.Ge, noNl);                    // no LF found
    // LF at idx X3: emit it only if idx < maxBytes (else fall to truncate)
    EmitLoadFromStack(ARM64Register.X1, 0x28, 8);                   // maxBytes
    EmitCmpRegReg(ARM64Register.X3, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Ge, noNl);
    EmitStoreToStack(0x40, ARM64Register.X3, 8);                    // idx
    EmitLoadFromStack(ARM64Register.X0, 0x38, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x40, 8);
    EmitLoadFromStack(ARM64Register.X2, 0x40, 8);
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: true); // consume = idx+1
    EmitBranchLink("__subp_stream_emit_line");
    EmitRuntimeFunctionEnd();

    DefineLabel(noNl);
    // truncate: if len >= maxBytes return first maxBytes
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, SubpQuadLen, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x28, 8);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchCond(ARM64ConditionCode.Lt, checkEof);
    EmitLoadFromStack(ARM64Register.X0, 0x38, 8);
    EmitLoadFromStack(ARM64Register.X1, 0x28, 8);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitBranchLink("__subp_stream_emit_line");
    EmitRuntimeFunctionEnd();

    DefineLabel(checkEof);
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, SubpQuadEof, 8);
    EmitCbz(ARM64Register.X0, needMore);
    // EOF: emit whatever remains (possibly empty)
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X9, SubpQuadLen, 8);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X1);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("__subp_stream_emit_line");
    EmitRuntimeFunctionEnd();

    DefineLabel(needMore);
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X10, ARM64Register.X9, SubpQuadCap, 8); // cap
    EmitLoadIndirect(ARM64Register.X11, ARM64Register.X9, SubpQuadLen, 8); // len
    EmitAluRegReg(0xCB000000, ARM64Register.X12, ARM64Register.X10, ARM64Register.X11); // space
    EmitCmpImm(ARM64Register.X12, SubpStreamLineGrowFloor);
    EmitBranchCond(ARM64ConditionCode.Ge, doRead);
    // grow: newCap = cap*2
    EmitLslImm(ARM64Register.X0, ARM64Register.X10, 1);
    EmitStoreToStack(0x40, ARM64Register.X0, 8);                    // newCap
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreToStack(0x48, ARM64Register.X0, 8);                    // newBuf
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X9, SubpQuadBuf, 8); // oldBuf
    EmitLoadIndirect(ARM64Register.X2, ARM64Register.X9, SubpQuadLen, 8); // len
    EmitLoadFromStack(ARM64Register.X0, 0x48, 8);
    EmitBranchLink("maxon_memcpy");
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X9, SubpQuadBuf, 8);
    EmitBranchLink("mm_raw_free");
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x48, 8); EmitStoreIndirect(ARM64Register.X9, SubpQuadBuf, ARM64Register.X0, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x40, 8); EmitStoreIndirect(ARM64Register.X9, SubpQuadCap, ARM64Register.X0, 8);
    DefineLabel(doRead);
    // n = __io_submit_read(fd, buf+len, cap-len). Cooperative: parks the drain GT
    // on kqueue EVFILT_READ and yields, so the dispatcher's N per-worker drain GTs
    // multiplex without one OS thread blocking in read() (which deadlocks the
    // cooperative scheduler — it has no sysmon to retake a P from a blocked M).
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadIndirect(ARM64Register.X10, ARM64Register.X9, SubpQuadBuf, 8);
    EmitLoadIndirect(ARM64Register.X11, ARM64Register.X9, SubpQuadLen, 8);
    EmitLoadIndirect(ARM64Register.X12, ARM64Register.X9, SubpQuadCap, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X1, ARM64Register.X10, ARM64Register.X11); // dst
    EmitAluRegReg(0xCB000000, ARM64Register.X2, ARM64Register.X12, ARM64Register.X11); // space
    EmitLoadFromStack(ARM64Register.X0, 0x30, 8);                   // fd
    EmitBranchLink("__io_submit_read");
    EmitStoreToStack(0x40, ARM64Register.X0, 8);                    // n
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, gotEof);
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitLoadFromStack(ARM64Register.X0, 0x40, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X9, SubpQuadLen, 8);
    EmitAluRegReg(0x8B000000, ARM64Register.X1, ARM64Register.X1, ARM64Register.X0);
    EmitStoreIndirect(ARM64Register.X9, SubpQuadLen, ARM64Register.X1, 8);
    EmitBranch(loop);
    DefineLabel(gotEof);
    EmitLoadFromStack(ARM64Register.X9, 0x38, 8);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitStoreIndirect(ARM64Register.X9, SubpQuadEof, ARM64Register.X0, 8);
    EmitBranch(loop);
  }

  /// Emit a read_stdout_line / read_stderr_line wrapper around the shared inner
  /// reader, selecting the fd field + line-buffer quad for the chosen stream.
  private void EmitSubpStreamReadLineWrapper(string name, int fdOff, int bufOff) {
    EmitRuntimeFunctionStart(name, 2, 0x40);
    int n = _uniqueLabelCounter++;
    string emptyRet = $"__subp_rlw_empty_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, emptyRet);
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);              // handle
    EmitLoadFromStack(ARM64Register.X0, 0x18, 8);                   // maxBytes (arg1 home)
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X9, fdOff, 8); // fd
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X9, bufOff, isAdd: true); // quadBase
    EmitBranchLink("__subp_stream_read_line");
    EmitRuntimeFunctionEnd();
    DefineLabel(emptyRet);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1);
    EmitRuntimeFunctionEnd();
  }

  /// maxon_subprocess_close_stdin(handle): close the parent write end of the
  /// child's stdin so the child sees EOF, then mark the slot -1 (idempotent;
  /// release_handle skips it).
  private void EmitMaxonSubprocessCloseStdinPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_close_stdin", 1, 0x30);
    int n = _uniqueLabelCounter++;
    string done = $"__subp_cs_done_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, done);
    EmitStoreToStack(0x18, ARM64Register.X0, 8);
    EmitLoadIndirect(ARM64Register.X1, ARM64Register.X0, SubpHOffStdinFd, 8);
    EmitCmpImm(ARM64Register.X1, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, done);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitCallImport("close");
    EmitLoadFromStack(ARM64Register.X9, 0x18, 8);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitStoreIndirect(ARM64Register.X9, SubpHOffStdinFd, ARM64Register.X0, 8);
    DefineLabel(done);
    EmitRuntimeFunctionEnd();
  }

  /// maxon_subprocess_wait_exit(handle, timeoutMs) -> exitCode | -1.
  /// Blocking waitpid (timeoutMs is treated as "wait forever", matching the
  /// streaming pool's shutdown use where the child exits promptly on stdin EOF).
  private void EmitMaxonSubprocessWaitExitPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_wait_exit", 2, 0x40);
    int n = _uniqueLabelCounter++;
    string bad = $"__subp_we_bad_{n}";
    string signaled = $"__subp_we_sig_{n}";
    // slots: 0x20 handle, 0x28 status(4)
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, bad);
    EmitStoreToStack(0x20, ARM64Register.X0, 8);
    EmitLoadIndirect(ARM64Register.X0, ARM64Register.X0, SubpHOffPid, 8);  // pid
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 0x28, isAdd: true); // &status
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitCallImport("waitpid");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, bad);
    EmitLoadFromStack(ARM64Register.X9, 0x28, 4);                   // status
    EmitMovRegImm(ARM64Register.X10, 0x7f);
    EmitAluRegReg(0x8A000000, ARM64Register.X11, ARM64Register.X9, ARM64Register.X10); // lowsig
    EmitCmpImm(ARM64Register.X11, 0);
    EmitBranchCond(ARM64ConditionCode.Ne, signaled);
    EmitLsrImm(ARM64Register.X12, ARM64Register.X9, 8);
    EmitMovRegImm(ARM64Register.X13, 0xff);
    EmitAluRegReg(0x8A000000, ARM64Register.X0, ARM64Register.X12, ARM64Register.X13); // (status>>8)&0xff
    EmitRuntimeFunctionEnd();
    DefineLabel(signaled);
    EmitMovRegImm(ARM64Register.X0, 128);
    EmitAluRegReg(0x8B000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X11); // 128 + sig
    EmitRuntimeFunctionEnd();
    DefineLabel(bad);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// maxon_subprocess_kill(handle, signal) -> 0 | -1. Sends `signal` to the
  /// child pid; used by the parallel runner's watchdog to evict a wedged worker.
  private void EmitMaxonSubprocessKillPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_kill", 2, 0x30);
    int n = _uniqueLabelCounter++;
    string bad = $"__subp_kill_bad_{n}";
    EmitReloadArg(0);
    EmitCbz(ARM64Register.X0, bad);
    EmitLoadIndirect(ARM64Register.X9, ARM64Register.X0, SubpHOffPid, 8); // pid
    EmitReloadArg(1);                                              // X1 = signal
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);            // X0 = pid
    EmitCallImport("kill");
    EmitRuntimeFunctionEnd();
    DefineLabel(bad);
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitRuntimeFunctionEnd();
  }

  /// maxon_subprocess_last_error_message() -> fresh empty cstring.
  /// The streaming/spawn error paths surface a thrown SubprocessError; we return
  /// a non-null empty cstring (never NULL) so String.init never faults.
  private void EmitMaxonSubprocessLastErrorMessagePosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_last_error_message", 0, 0x30);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1);
    EmitRuntimeFunctionEnd();
  }

  /// maxon_subprocess_resolve_on_path(name_cstr) -> fresh empty cstring.
  /// arm64-macos never resolves PATH (the parallel runner spawns absolute
  /// Executable.path tokens), so this reports "not found" for every name. It
  /// returns a freshly mm_raw_alloc'd 1-byte "\0" — NOT NULL — to honour the
  /// RuntimeCallToManaged ABI shared with x64 (rt_subp_rop_empty): the lowering
  /// always cstring→managed-wraps the return (maxon_strlen would fault on NULL)
  /// and mm_raw_free's it (the static __rt_empty_cstring would underflow the
  /// alloc count). managedIsNull then reports the leading-NUL buffer as null, so
  /// resolveByName falls back to the bare name. Mirrors last_error_message.
  private void EmitMaxonSubprocessResolveOnPathPosix() {
    EmitRuntimeFunctionStart("maxon_subprocess_resolve_on_path", 1, 0x30);
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitBranchLink("mm_raw_alloc", zeroSecondArg: true);
    EmitStoreIndirect(ARM64Register.X0, 0, ARM64Register.Xzr, 1);
    EmitRuntimeFunctionEnd();
  }

}
