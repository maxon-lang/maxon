namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Authoritative layout for the GreenThread (gt) and ProcContext (P) runtime structs,
/// shared by RuntimeEmitter and every CodeEmitter backend. Single source of truth —
/// do not duplicate these constants in backend files.
///
/// GreenThread struct (232 bytes = 0xE8):
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
///   0xA8  io_yielded               1=suspended OFF ITS OWN STACK, so another M may be handed it;
///                                  0=running (or mid-suspend, context not yet saved). Written by
///                                  __gt_context_switch alone — 1 on the outgoing GT once its
///                                  context is saved, 0 on the incoming one — plus the initial 1
///                                  __gt_spawn gives a GT that has not run yet
///   0xB0  fault_rip                RIP/PC of faulting instruction (set by fault handler)
///   0xB8  fault_msg                ptr to static panic message string (set by fault handler)
///   0xC0  fault_redirect_rip       RIP to resume at (epilog writes into ucontext)
///   0xC8  fault_redirect_rsp       SP to resume at
///   0xD0  fault_redirect_fp        FP to resume at
///   0xD8  park_state               async-I/O wakeup ownership (see the Netpoll* constants)
///   0xE0  await_handoff            AWAIT completion ownership, on the PROMISE (see AwaitHandoff*)
///
/// ProcContext struct (368 bytes = 0x170):
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
///   0x80  main_thread              inline GT struct (replaces global __gt_main_thread); NEVER linked
///                                  into __gt_all_head, so any enumeration of live green threads must
///                                  walk the __sched_procs array as well as that list
///   0x168 pending_sync_req         deferred sync-I/O request handed off by a parking worker GT
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
  // Ownership of an in-flight async-I/O wakeup — see the Netpoll* constants below. Appended after
  // the fault block so no existing offset shifts.
  public const int GtOffParkState = 0xD8;
  // Ownership of an AWAITED green thread's completion hand-off — see the AwaitHandoff* constants.
  // It lives on the PROMISE, not on the awaiter, which is the one structural difference from
  // GtOffParkState and the reason it cannot share that word; see AwaitHandoffNil.
  public const int GtOffAwaitHandoff = 0xE0;
  public const int GtStructSize = 0xE8;      // 232 bytes

  // ---- Backtrace walk limits (shared by every frame-chain walk: both backends' mrt_panic and
  //      mrt_fault_backtrace, and the debug agent's __dbg_walk_frames) ----

  // Cap the frame-pointer-chain walk so a corrupt or cyclic chain can't spin the
  // backtrace forever.
  public const int MaxBacktraceFrames = 32;

  // Sane upper bound on a single stack's span, used ONLY when a stack's real extent is
  // not recorded anywhere — an OS thread's, whose bounds nothing owns. A frame pointer
  // outside [sp, sp + this) is treated as corrupt and ends the walk instead of
  // dereferencing an unmapped page.
  //
  // ⚠ It is NOT a substitute for a green thread's own extent, which __gt_stack_high
  // returns whenever there is one. A green-thread stack is GtInitialStackSize, and the
  // spawn trampoline's frame pointer sits at the very TOP of it, so a walk bounded by
  // this window reads the frame link one word past the end and faults.
  public const long FaultStackWindowBytes = 0x4000000; // 64 MiB

  // A frame link is TWO words — the caller's saved frame pointer at [fp] and the return
  // address at [fp + 8] — so a walk's upper bound must leave room for BOTH before it
  // dereferences fp, not merely for fp itself. Every frame-chain walk states the test as
  // `fp + FrameLinkBytes <= stackHigh`, and it is one constant because it is one fact.
  public const int FrameLinkBytes = 16;

  // POSIX sleeps in microseconds and Win32 in milliseconds; IEmitterBackend.OsSleepMillis presents
  // the coarser of the two, so the POSIX side scales by this.
  public const int MicrosPerMilli = 1000;

  // ---- GT status values ----
  public const int GtStatusReady = 0;
  public const int GtStatusRunning = 1;
  public const int GtStatusCompleted = 2;
  public const int GtStatusWaiting = 3;

  // ---- Async-I/O park state (GtOffParkState) — a port of Go's netpoll pd.rg/pd.wg ----
  //
  // WHO OWNS THE WAKEUP. `status` says an I/O finished and `ioYielded` says a context has been
  // saved; NEITHER says whether the completer or the waiter is responsible for resuming it.
  // Deciding that from two independent words is a race whatever order they are written in — the
  // completer reads "still running, it will self-detect" in the same instant the waiter reads
  // "still waiting, I may park", and the wakeup is lost. This word settles it with ONE atomic
  // operation per side, so exactly one of them acts.
  //
  // ⚠⚠ ONCE A WAITER HAS ARMED THIS WORD, THIS WORD IS THE ONLY THING IT MAY ASK. Not `status`,
  // not `io_result_val` — nothing a completer writes into the GT. A waiter that left the park on
  // `status` ran __netpoll_park_done, which swaps the word to `Nil`, RELEASING a word its completer
  // was still in flight toward. The late claim then landed on the waiter's NEXT park and that
  // park's commit failed against a completion that never happened. `__netpoll_woken` exists so the
  // question has exactly one form; see RuntimeEmitter.Netpoll.cs. (Reproduced:
  // MAXON_GT_CLAIM_DELAY_MS=5 took the park driver from 5/5 clean to 0/5, every failure leaking.)
  //
  // ⚠ IT DOES NOT REPLACE `status` OR `ioYielded`, AND MUST NOT — it narrows who may READ them, not
  // what they mean. Go keeps `g.atomicstatus` separate from `pd.rg` because they answer different
  // questions — WHAT IS THIS THREAD DOING versus WHO OWNS THIS WAKEUP — and so do we: `status` still
  // carries readiness (with `io_result_val` behind its release fence) for every reader that is not a
  // waiter deciding whether to leave a park, and `ioYielded` still says whether the register context
  // has been saved.
  //
  // The first four names and their VALUES are Go's:
  //
  //   Nil       none of the below: this GT is not waiting on an async-I/O completion.
  //   Ready     a completer has CLAIMED the wakeup AND PUBLISHED its results. It either enqueued the
  //             GT (claimed from Parked) or left the GT to self-detect (claimed from Wait). This is
  //             the ONLY state a waiter may leave a park on.
  //   Wait      the GT has armed a registration and MAY park; it is still running and can still
  //             abort. A completer that claims this does NOT enqueue — the waiter's own commit CAS
  //             will fail, or its next __netpoll_woken will return 1, and it resumes itself. This is
  //             also the terminal state of a GT that has no schedulable stack: __netpoll_commit
  //             refuses to move the word for a stackBase==0 GT, so the main OS thread is `Wait` for
  //             its whole park and every completer correctly declines to enqueue it.
  //   Parked    the GT has COMMITTED to park (the commit CAS Wait -> Parked won) and is executing
  //             straight-line code into __gt_context_switch. A completer that claims this owns the
  //             enqueue, and may safely spin for ioYielded==1 because nothing on that path can turn
  //             the waiter around — the termination argument __gt_ppw_spin already makes for its own
  //             parker, and the one the I/O park path could not make until this word existed, because
  //             its parker COULD turn around.
  //   Claiming  a completer has TAKEN the wakeup and is writing io_result_val / io_error_code /
  //             status right now. OWNED, NOT YET PUBLISHABLE: __netpoll_woken declines it,
  //             __netpoll_park_done waits it out, __netpoll_claim declines it, and the recovery net
  //             skips it. Only the store-release of `Ready` at the end of __netpoll_claim_done opens
  //             the gate.
  //
  // ⭐ Parked is a CONSTANT where Go stores a `*g`. Go's word lives in a per-fd pollDesc, so it has
  // to name WHICH goroutine parked; ours lives in the GT itself, so "which" is the word's own
  // address and the fourth state degenerates to a sentinel. That is the first of the two structural
  // differences between this state machine and netpoll.go's, and it is why `netpollunblock`'s
  // return value — Go's `*g` — is here just "the GT you were given, or nothing".
  //
  // ⭐⭐ Claiming is the SECOND, and it exists because OUR COMPLETER HAS SOMETHING TO PUBLISH AND
  // GO'S DOES NOT. In netpoll.go the transition to `pdReady` IS the entire notification: a goroutine
  // that observes it reads nothing else, so "owned" and "publishable" are the same instant and four
  // states suffice. Our completers carry io_result_val / io_error_code / status alongside the word,
  // so those are two DIFFERENT instants and the word has to be able to say which one it is in.
  //
  // Claiming BEFORE publishing is necessary and not sufficient, which is why this is a fifth state
  // and not merely a reordering. Were the claim to go straight to `Ready`, a waiter claimed from
  // `Wait` — which is still RUNNING — would see __netpoll_woken() == 1 immediately and leave the
  // park BEFORE its results were stored; the completer would then write io_result_val into a GT that
  // had already moved on to its next operation, which is the class comment's defect with the roles
  // swapped. `Claiming` is what makes the ownership visible without making it consumable.
  //
  // ⭐ ITS NUMBER IS APPENDED, NOT INSERTED. Keeping Go's four at Go's values is what lets anyone
  // diffing this against netpoll.go read pdNil/pdReady/pdWait/`*g` across unchanged; renumbering
  // Parked to make room would have broken that correspondence for the one constant that already
  // carries the biggest structural deviation. Nothing here tests the word by ORDER, so appending
  // costs no property: Go's `old > pdWait` throw is spelled as two explicit equality tests in
  // __netpoll_park_done. Nor could any placement have preserved it — `old > pdWait` means "invalid
  // at park_done", and Claiming is not invalid there, it is a state to WAIT OUT. It needs its own
  // arm wherever it appears, at any number.
  public const int NetpollNil = 0;
  public const int NetpollReady = 1;
  public const int NetpollWait = 2;
  public const int NetpollParked = 3;
  public const int NetpollClaiming = 4;

  // ---- AWAIT completion hand-off (GtOffAwaitHandoff), the SAME QUESTION one object over ----
  //
  // WHO ENQUEUES THE AWAITER. An awaiter publishes `promise.waiter = self` and then keeps running
  // its own scheduling loop; a completing child that reads that field cannot tell an awaiter that
  // is ABOUT TO PARK from one that will notice the completion itself and carry on. Deciding it from
  // `ioYielded` is the netpoll guard (c) failure verbatim — "still running and WILL self-detect"
  // and "still running and is about to park" are the same instant. This word settles it with ONE
  // atomic operation per side, so exactly one of them acts.
  //
  //   Nil        nothing has happened yet.
  //   Parked     the awaiter has COMMITTED to a park and is executing straight-line code into
  //              __gt_context_switch. A completer that loses its CAS against this owns the enqueue,
  //              and may safely wait for ioYielded==1 because nothing on that path can turn the
  //              awaiter around — the same termination argument __netpoll_commit's Deviation 2 makes.
  //   Completed  the child has completed. An awaiter whose commit CAS loses against this reads
  //              `promise.status` — published BEFORE this word — sees `completed`, and resumes
  //              itself. The completer enqueues nothing.
  //
  // ⚠⚠ IT IS A SECOND WORD RATHER THAN A SECOND USE OF GtOffParkState, AND THE REASON IS NOT
  // TIDINESS. Two independent reasons, either one fatal:
  //
  //   THE AWAITER'S OWN park_state IS TAKEN. The awaiter is a green thread like any other and may
  //   arm an I/O park at any time; a hand-off living there would collide with the very mechanism it
  //   was modelled on.
  //
  //   AND IT MUST BE PER-PROMISE ANYWAY, BECAUSE THE AWAITER SELF-DETECTS ON `promise.status` AND
  //   THE NETPOLL RELEASE RULE THEREFORE CANNOT HOLD HERE. A waiter that may learn of its
  //   completion through a channel other than the word can LEAVE, and then arm the word again for
  //   something else — so a completer's late decision would land on that NEXT registration.
  //   RuntimeEmitter.Netpoll.cs's class comment records what that costs: a wakeup delivered to the
  //   wrong park, and a read on an fd nobody signalled. A promise, by contrast, completes exactly
  //   once and is awaited exactly once (`await` is linear, E3100), so a decision taken on ITS word
  //   has nowhere else to land. That is what makes self-detecting on `status` sound here and
  //   unsound there — the same property, asked of a different object.
  //
  // ⚠ THE AWAITER SELF-DETECTS ON `status`, SO THE COMPLETER MUST PUBLISH `status` (and `result` /
  // `threw` before it) BEFORE IT TOUCHES THIS WORD. That is claim-then-publish INVERTED, and
  // correctly so: netpoll's completer publishes results the waiter reads THROUGH the word, whereas
  // ours publishes them through `status`, which the awaiter was already reading. The ordering rule
  // is the same one in both places — a waiter must never be released toward a result that is not
  // there yet.
  //
  // ⭐ AND THE MAIN OS THREAD IS OUTSIDE THE HANDSHAKE ENTIRELY, on exactly the predicate
  // __netpoll_commit and __gt_process_pending_waiter already use: a GT with `stackBase == 0` has no
  // schedulable stack, is never enqueued by anything, and so must never publish `Parked` — a
  // completer that saw it would wait for an ioYielded that a running thread never sets. It is
  // handed to P->pendingWaiter all the same, because that is the slot __gt_process_pending_waiter
  // turns into a SetEvent on its wake handle.
  public const int AwaitHandoffNil = 0;
  public const int AwaitHandoffParked = 1;
  public const int AwaitHandoffCompleted = 2;

  // ---- Stack growth ----
  //
  // A green-thread stack has TWO parts, and both are counted TWICE — once in the ALLOCATION and once
  // in the GUARD — so __gt_morestack maintains the split for free across every grow and relocate:
  //
  //   base                  base+GtOsFaultReserve        stackguard              base+size
  //     |<-- GtOsFaultReserve -->|<-- GtUncheckedFrameMargin -->|<-- checked frames -->|
  //         the OS writes here      leaf frames the prologue       every frame the
  //                                 check does not cover           prologue check placed
  //
  // The prologue check refuses to place a frame below `stackguard`, so RSP never drops below
  // base+GtOsFaultReserve, and the reserve below it is still intact when the OS needs it.

  /// <summary>
  /// Stack the OPERATING SYSTEM writes BELOW RSP when it delivers an exception, before a single
  /// instruction of any handler runs — so no Maxon-side accounting can see it and nothing but a
  /// reserve can protect it. On Windows there is no alternate stack for a vectored exception
  /// handler: KiUserExceptionDispatcher builds its frame on the faulting thread's own stack.
  ///
  /// MEASURED on this project's reference host (Windows 10.0.26200, x64) with a native VEH probe,
  /// three ways — a plain thread stack, a thread with live AVX state, and RSP inside a small
  /// VirtualAlloc'd region with the TIB's StackBase/StackLimit repointed at it (a green thread's
  /// exact configuration). All three agree:
  ///
  ///   EXCEPTION_RECORD          152 B   at faultRsp-552
  ///   CONTEXT                  1232 B   at faultRsp-1816  (no XSAVE area is appended: the number is
  ///                                     identical with YMM state live)
  ///   ntdll dispatch frames             KiUserExceptionDispatcher / RtlDispatchException /
  ///                                     RtlpCallVectoredHandlers
  ///   ------------------------------------------------------------------------------------------
  ///   faultRsp - VEH entry RSP 2577 B   consumed before ANY handler code runs
  ///
  /// On top of that sits the runtime's own handler chain, measured by decoding the emitted
  /// prologues out of a built binary: 216 B for the fault path (__gt_fault_handler_thunk ->
  /// __gt_fault_handler) and 1336 B for the debug agent's deepest trap path
  /// (__dbg_trap_handler_thunk -> __dbg_on_breakpoint -> __dbg_park_loop -> __dbg_gt_backtrace ->
  /// __dbg_gt_scan -> __dbg_gt_record -> __dbg_gt_frames -> __dbg_walk_frames -> __dbg_frame_ra ->
  /// __dbg_text_offset). Every Win32 call those paths make is emitted through
  /// EmitCallImportOnSystemStack, which switches to the P's 64 KB system stack, so the kernel side
  /// of VirtualProtect/FlushInstructionCache costs the green-thread stack one 8-byte push.
  ///
  ///   worst case = 2577 + 1336 = 3913 B
  ///
  /// The reserve is 6 KB: that measurement rounded up to a page, plus a page and a half of margin for
  /// the two things a measurement on one host cannot cover — another Windows build's dispatcher
  /// frames, and the next rung that deepens the agent's park-loop chain (P4d-2a alone deepened it by
  /// 736 B). The margin is free: 6 KB is exactly what makes GtInitialStackSize two 4 KB pages, and
  /// VirtualAlloc commits whole pages, so a 4 KB reserve would cost the same memory for less safety.
  ///
  /// macOS/arm64 pays nothing for it — a 16 KB page holds the whole stack either way — and it is
  /// defence in depth there rather than dead weight: the fault and (as of this rung) trap handlers
  /// run SA_ONSTACK on a per-thread sigaltstack, but that is host-unverifiable from Windows.
  /// </summary>
  public const int GtOsFaultReserve = 0x1800;    // 6 KB

  /// Worst-case UNCHECKED stack consumption (PUSH RBP + CALL return address, through leaf functions
  /// whose zero-sized frames emit no check) between successive prologue stack checks. 928 bytes, the
  /// value Go carries in _StackGuard for the same reason.
  public const int GtUncheckedFrameMargin = 0x3A0;

  /// The Maxon half of a fresh green-thread stack — everything but the OS reserve, and the only part
  /// __gt_morestack's doubling is about. Unchanged at 2 KB, so a fresh green thread still gets exactly
  /// the same GtMaxonStackSize - GtUncheckedFrameMargin = 1120 bytes of frames before its first
  /// relocation as it did before the reserve existed: the reserve is purely additive.
  public const int GtMaxonStackSize = 0x800;

  // Both totals are the SUM, which is what makes the reserve survive a grow: __gt_morestack rewrites
  // stackguard as new_base + GtStackGuardMargin, so the reserve is re-established below every
  // relocated stack without the relocation code knowing it exists.
  public const int GtInitialStackSize = GtMaxonStackSize + GtOsFaultReserve;        // 0x2000, 8 KB
  public const int GtStackGuardMargin = GtUncheckedFrameMargin + GtOsFaultReserve;  // 0x1BA0

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
  public const int POffPendingSyncReq = POffMainThread + GtStructSize;   // 0x168
  // Deferred BACK-of-queue re-enqueue for a green thread that yielded cooperatively
  // (maxon_yield's worker arm). Same "register-after-park" shape as POffPendingSyncReq
  // above and for the same reason — the GT may not become discoverable until
  // __gt_context_switch has saved it — but a SEPARATE slot from POffPendingWaiter,
  // because the two want opposite ends of the queue. A woken awaiter goes through
  // __gt_enqueue and lands in P->runnext, which is right: it is the thread this
  // processor was waiting on. A YIELDER routed the same way lands in the slot it just
  // vacated and __gt_dequeue hands it straight back — a yield to nobody. This one is
  // drained by __gt_dequeue into __gt_enqueue_back.
  //
  // ⚠ Written ONLY by the GT currently running on this P, and drained ONLY by that same
  // P's own __gt_dequeue (which reads P from TLS / X28). No other M can see it, which is
  // a stronger property than POffPendingWaiter has and is what makes a single slot enough.
  public const int POffPendingYielder = POffPendingSyncReq + 8;           // 0x170
  public const int PStructSize = POffPendingYielder + 8;                  // 0x178 = 376 bytes

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
