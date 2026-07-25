namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Authoritative layout for the GreenThread (gt) and ProcContext (P) runtime structs,
/// shared by RuntimeEmitter and every CodeEmitter backend. Single source of truth —
/// do not duplicate these constants in backend files.
///
/// GreenThread struct (216 bytes = 0xD8):
///   0x00  saved SP/RSP             (per-arch name; same offset)
///   0x08  saved FP/RBP             (per-arch name; same offset)
///   0x10  status                   0=ready, 1=running, 2=completed, 3=waiting
///   0x18  stack_base               low address of mmap'd / VirtualAlloc'd stack
///   0x20  stack_size               current stack allocation size
///   0x28  result                   return value when completed
///   0x30  waiter                   ptr to GT waiting on this one
///   0x38  next                     linked-list chain (runqueues, free list)
///   0x40  func_ptr                 target function address
///   0x48  arg_buf                  ptr to argument buffer
///   0x50  stackguard               lowest valid stack address (triggers __gt_morestack)
///   0x58  threw                    0=success, 1=threw (for async error returns)
///   0x60  io_result_val            raw result value (bytes transferred, error code, etc.)
///   0x68  io_result_len            byte count for read results
///   0x70  io_error_code            0=success, non-zero=OS error
///   0x78  cancel_flag              0=live, 1=cancel-requested
///   0x80  io_handle                HANDLE/fd of in-flight I/O (for cancel)
///   0x88  all_next                 next ptr in global all-threads list
///   0x90  tib_stack_base           saved TIB StackBase (Win32: gs:[0x08]); unused on macOS
///   0x98  tib_stack_limit          saved TIB StackLimit (Win32: gs:[0x10]); unused on macOS
///   0xA0  trace_id                 async trace ID (only meaningful with --async-trace)
///   0xA8  io_yielded               0=not yet yielded, 1=context switch complete (IOCP/kqueue sync)
///   0xB0  fault_rip                RIP/PC of faulting instruction (set by fault handler)
///   0xB8  fault_msg                ptr to static panic message string (set by fault handler)
///   0xC0  fault_redirect_rip       RIP to resume at (epilog writes into ucontext)
///   0xC8  fault_redirect_rsp       SP to resume at
///   0xD0  fault_redirect_fp        FP to resume at
///
/// ProcContext struct (344 bytes = 0x158):
///   0x00  local_queue_head         per-P run queue (no lock needed)
///   0x08  local_queue_tail
///   0x10  local_queue_len
///   0x18  current_gt               currently-running GT on this processor
///   0x20  id                       processor ID
///   0x28  rng                      xorshift64 PRNG state (for fairness)
///   0x30  idle_flag                0=busy, 1=idle (for wakeup protocol)
///   0x38  wake_event               Win32 Event handle (Windows) / ptr to {mutex,cond,count}
///                                  lock block (macOS) — see WakeLockBlk* below (SetEvent /
///                                  pthread_cond_signal to wake the parked worker)
///   0x40  os_thread_handle         OS thread handle
///   0x48  status                   0=unused, 1=active
///   0x50  pending_waiter           GT to wake after context-switch (deferred from trampoline)
///   0x58  runnext                  single-slot highest-priority GT
///   0x60  free_list_head           GT free list head (capped at MaxFreeListLen)
///   0x68  free_list_len            GT free list count
///   0x70  system_stack_sp          saved OS thread RSP (for system calls / morestack)
///   0x78  remote_free_head         atomic head of the Mimalloc-style MPSC remote-free queue
///                                  (cross-thread slab frees push here; owner drains on alloc slow path)
///   0x80  main_thread              inline GT struct (replaces global __gt_main_thread)
/// </summary>
public static class GtLayout {

  // ---- GreenThread (gt) struct offsets ----
  public const int GtOffRsp = 0x00;          // saved stack pointer (x86 spelling)
  public const int GtOffSp = 0x00;           // alias for ARM64 spelling
  public const int GtOffRbp = 0x08;          // saved frame pointer (x86 spelling)
  public const int GtOffFp = 0x08;           // alias for ARM64 spelling
  public const int GtOffStatus = 0x10;
  public const int GtOffStackBase = 0x18;
  public const int GtOffStackSize = 0x20;
  public const int GtOffResult = 0x28;
  public const int GtOffWaiter = 0x30;
  public const int GtOffNext = 0x38;
  public const int GtOffFuncPtr = 0x40;
  public const int GtOffArgBuf = 0x48;
  public const int GtOffStackGuard = 0x50;
  public const int GtOffThrew = 0x58;
  public const int GtOffIoResultVal = 0x60;
  public const int GtOffIoResultLen = 0x68;
  public const int GtOffIoErrorCode = 0x70;
  public const int GtOffCancelFlag = 0x78;
  public const int GtOffIoHandle = 0x80;
  public const int GtOffAllNext = 0x88;
  public const int GtOffTibStackBase = 0x90;
  public const int GtOffTibStackLimit = 0x98;
  public const int GtOffTraceId = 0xA0;
  public const int GtOffIoYielded = 0xA8;
  // ---- Fault diagnostic fields (populated by __gt_fault_handler before redirecting
  //      to the diagnostic printer; read by __gt_fault_diagnostic and the per-backend
  //      fault-handler epilog) ----
  public const int GtOffFaultRip = 0xB0;     // RIP/PC of the faulting instruction
  public const int GtOffFaultMsg = 0xB8;     // ptr to a static null-terminated panic message
  public const int GtOffFaultRedirectRip = 0xC0;  // resume RIP/PC; epilog writes this back into ucontext
  public const int GtOffFaultRedirectRsp = 0xC8;  // resume SP
  public const int GtOffFaultRedirectFp = 0xD0;   // resume FP/RBP
  public const int GtStructSize = 0xD8;      // 216 bytes

  // ---- Backtrace walk limits (shared by every backend's mrt_fault_backtrace) ----

  // Cap the frame-pointer-chain walk so a corrupt or cyclic chain can't spin the
  // backtrace forever.
  public const int MaxBacktraceFrames = 32;

  // Sane upper bound on a single stack's span — larger than any OS thread or grown
  // green-thread stack. A faulting-thread frame pointer outside
  // [fault_sp, fault_sp + this) is treated as corrupt and ends the walk instead of
  // dereferencing an unmapped page.
  public const long FaultStackWindowBytes = 0x4000000; // 64 MiB

  // ---- GT status values ----
  public const int GtStatusReady = 0;
  public const int GtStatusRunning = 1;
  public const int GtStatusCompleted = 2;
  public const int GtStatusWaiting = 3;

  // ---- Stack growth ----
  public const int GtInitialStackSize = 0x800;   // 2 KB; grows on demand via __gt_morestack.
                                                 // Matches Go's _StackMin on amd64.
  public const int GtStackGuardMargin = 0x3A0;   // 928 bytes; matches Go's _StackGuard on amd64.
                                                 // Covers worst-case unchecked stack consumption
                                                 // (PUSH RBP + CALL overhead) between successive
                                                 // prologue stack checks.

  // ---- ProcContext status values (POffStatus) ----
  // A P struct is allocated for every possible processor at __gt_init, so "does this P exist" and "is
  // this P live" are different questions and only this field answers the second. Every reader must
  // filter on it: the debug agent's green-thread enumeration walks the __sched_procs array to reach
  // each P's INLINE main-thread GT, and an unused P's inline GT is zeroed, not a thread.
  public const int PStatusUnused = 0;
  public const int PStatusActive = 1;

  // ---- ProcContext (P) struct offsets ----
  public const int POffLocalQueueHead = 0x00;
  public const int POffLocalQueueTail = 0x08;
  public const int POffLocalQueueLen = 0x10;
  public const int POffCurrentGt = 0x18;
  public const int POffId = 0x20;
  public const int POffRng = 0x28;
  public const int POffIdleFlag = 0x30;
  public const int POffWakeEvent = 0x38;        // Windows Event handle
  public const int POffWakeSemaphore = 0x38;    // alias: macOS ptr to wake lock block (see WakeLockBlk*)
  public const int POffOsThreadHandle = 0x40;
  public const int POffStatus = 0x48;
  public const int POffPendingWaiter = 0x50;
  public const int POffRunnext = 0x58;
  public const int POffFreeListHead = 0x60;
  public const int POffFreeListLen = 0x68;
  public const int POffSystemStackSP = 0x70;
  // Atomic head of the per-P Mimalloc-style MPSC remote-free queue. Cross-thread
  // slab frees push freed slots here via AtomicCAS; the owning P drains the list
  // on its next __slab_alloc slow path. Zero-initialised at P alloc time (memzero).
  public const int POffRemoteFreeHead = 0x78;
  public const int POffMainThread = 0x80;
  // Deferred sync-I/O request handed off by a parking worker GT (Go gopark's
  // "register-after-park"): __io_submit_sync stores the request here and switches to
  // this P's scheduler; the scheduler enqueues it (via __gt_process_pending_sync_req)
  // only AFTER the GT is fully parked (ioYielded=1), so no completer can ever observe
  // the request while its waiter still runs — the double-schedule is structurally
  // impossible. Appended after the inline mainThread GT so no existing offset shifts.
  public const int POffPendingSyncReq = POffMainThread + GtStructSize;   // 0x158
  public const int PStructSize = POffPendingSyncReq + 8;                  // 0x160 = 352 bytes

  // ---- macOS wake lock block (Go semasleep/semawakeup primitive) ----
  // On macOS the worker park/wake (and the I/O sync worker's wake) use a Go-style
  // {pthread_mutex_t mutex; pthread_cond_t cond; long count} block instead of a
  // libdispatch dispatch_semaphore (which is Mach-port-backed and not
  // async-signal-safe — it wedged processes in the uninterruptible 'UE' state on
  // kill). The block is mmap'd separately and a POINTER to it lives in the 8-byte
  // POffWakeSemaphore slot (and in __io_sync_req_semaphore), so the P struct layout
  // is unchanged. Darwin arm64 sizes: pthread_mutex_t = 64 bytes, pthread_cond_t =
  // 48 bytes. count is the semaphore counter, preserving the signal-before-wait
  // (early-wakeup-not-lost) property the counting dispatch_semaphore provided.
  public const int WakeLockBlkOffMutex = 0x00;   // pthread_mutex_t (64 bytes)
  public const int WakeLockBlkOffCond = 0x40;    // pthread_cond_t (48 bytes)
  public const int WakeLockBlkOffCount = 0x70;   // long semaphore count (0 = worker parked)
  public const int WakeLockBlkSize = 0x78;       // 120 bytes; mmap rounds up to a page

  // ---- Per-P system stack size ----
  // Used for two purposes:
  //   1. __gt_morestack scratch frame during GT stack relocation.
  //   2. Windows API calls invoked from a green thread via
  //      EmitCallImportOnSystemStack / EmitSystemStackEnter, with TIB stack
  //      bounds repointed at this region. Heavyweight kernel calls
  //      (CreateProcessW, CreateFileW, ...) consume tens of kilobytes of
  //      stack for RPC marshalling and security probes. 8 KB was observed
  //      to fault inside CreateFileW from a green thread; 64 KB covers
  //      worst-case CreateProcessW with margin.
  public const int PSystemStackSize = 0x10000;   // 64 KB

  // ---- Per-P GT free-list cap (returned to mm_raw_alloc once exceeded) ----
  public const int MaxFreeListLen = 64;

  // ---- Timer-heap deadline unit ----
  // Timer-heap deadlines are absolute NANOSECONDS read from the monotonic
  // high-resolution clock (maxon_current_time_nanos -> QPC / CLOCK_MONOTONIC),
  // NOT the coarse OS tick.
  //
  // They used to be GetTickCount64 milliseconds, and that made sleep(N) return
  // EARLY. GetTickCount64 only advances every ~15.6 ms, so a deadline computed
  // as `GetTickCount64() + N` is anchored to a tick edge that may already be up
  // to a full tick in the past. The comparison in __gt_timer_check reads the
  // same quantized counter, so the deadline can compare "expired" up to 15.6 ms
  // before N ms of real time has actually passed -- sleep(30) was observed
  // returning in 16 ms. You cannot see a 15.6 ms error with a 15.6 ms clock,
  // which is why this survived until a nanosecond clock existed to catch it.
  //
  // Both the deadline computation (maxon_sleep) and the comparison against it
  // (__gt_timer_check) must therefore use the nanosecond clock.
  public const long TimerNanosPerMilli = 1_000_000L;

  // ---- Fault codes ----
  // Platform-neutral codes that the per-backend fault-handler prolog maps from
  // OS-specific exception/signal codes, then passes to the shared __gt_fault_handler
  // in Arg0. Returned values from the shared handler also reuse this enum:
  // FaultCodeDontRecover signals "let the OS default handler take over".
  public const long FaultCodeNilDeref = 1;       // Win EXCEPTION_ACCESS_VIOLATION; mac SIGSEGV/SIGBUS
  public const long FaultCodeDivZero = 2;        // Win EXCEPTION_INT_DIVIDE_BY_ZERO; mac SIGFPE/FPE_INTDIV
  public const long FaultCodeIntOverflow = 3;    // Win EXCEPTION_INT_OVERFLOW; mac SIGFPE/FPE_INTOVF
  public const long FaultCodeStackOverflow = 4;  // Win EXCEPTION_STACK_OVERFLOW; mac SIGSEGV at stackguard
  public const long FaultCodeOther = 5;          // any other catchable fault we want to diagnose
  public const long FaultCodeDontRecover = -1;   // sentinel: hand control back to OS default disposition
}
