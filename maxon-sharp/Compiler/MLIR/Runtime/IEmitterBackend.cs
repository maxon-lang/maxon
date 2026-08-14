namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// Virtual register set for platform-independent runtime code generation.
/// Each platform backend maps these to physical registers.
/// </summary>
public enum VReg {
  // Argument registers (mapped to calling convention order)
  Arg0, Arg1, Arg2, Arg3, Arg4, Arg5,
  // Scratch registers (caller-saved, free to clobber)
  Scratch0, Scratch1, Scratch2, Scratch3,
  // Return value (aliased to Scratch0/RAX/X0 on most platforms)
  Ret = Scratch0,
}

/// <summary>
/// Condition codes for conditional branches.
/// </summary>
public enum Condition {
  Equal,        // ZF=1
  NotEqual,     // ZF=0
  Less,         // SF!=OF (signed)
  LessEqual,    // ZF=1 || SF!=OF
  Greater,      // ZF=0 && SF==OF
  GreaterEqual, // SF==OF
  Above,        // CF=0 && ZF=0 (unsigned)
  Below,        // CF=1 (unsigned)
  AboveEqual,   // CF=0 (unsigned)
  BelowEqual,   // CF=1 || ZF=1 (unsigned)
}

/// <summary>
/// Platform-independent interface for emitting machine code.
/// RuntimeEmitter writes algorithms once using VRegs and this interface;
/// each platform (x86, ARM64) provides a concrete backend.
/// </summary>
public interface IEmitterBackend {

  // ---- Function structure ----

  /// <summary>Emit function prologue: save frame pointer, allocate stack frame, spill args.</summary>
  void FunctionStart(string name, int argCount, int frameSize);

  /// <summary>Emit function epilogue: restore frame, return.</summary>
  void FunctionEnd();

  // ---- Register operations ----

  void MovRegReg(VReg dest, VReg src);
  void MovRegImm(VReg dest, long imm);

  /// <summary>Zero a register (XOR reg,reg on x86; MOV reg,#0 on ARM64).</summary>
  void ZeroReg(VReg reg);

  // ---- Memory: local stack frame slots ----

  /// <summary>Load 8-byte value from stack frame slot into register.
  /// Slot 0 = first spilled arg, slot 1 = second, etc.
  /// Negative slots are scratch space below the spilled args.</summary>
  void LoadLocal(VReg dest, int slotIndex);

  /// <summary>Store 8-byte value from register into stack frame slot.</summary>
  void StoreLocal(int slotIndex, VReg src);

  // ---- Memory: indirect (base register + offset) ----

  /// <summary>Load 8 bytes: dest = [base + offset]</summary>
  void LoadIndirect(VReg dest, VReg baseReg, int offset);

  /// <summary>Store 8 bytes: [base + offset] = src</summary>
  void StoreIndirect(VReg baseReg, int offset, VReg src);

  // ---- Globals (mutable data section) ----

  void LoadGlobal(VReg dest, string globalLabel);
  void StoreGlobal(string globalLabel, VReg src);

  /// <summary>Load address of mutable global into register (LEA / ADRP+ADD).</summary>
  void LeaGlobal(VReg dest, string globalLabel);

  // ---- Symdata (read-only data section: strings, tables) ----

  /// <summary>Load address of symdata label into register.</summary>
  void LeaSymdata(VReg dest, string symdataLabel);

  /// <summary>
  /// Load the absolute runtime address of a CODE label (a runtime/emitted function) into
  /// <paramref name="dest"/> (RIP-relative LEA on x86; ADRP+ADD on ARM64). Distinct from
  /// <see cref="LeaSymdata"/> / <see cref="LeaGlobal"/>, which address the read-only and mutable data
  /// sections; this addresses <c>.text</c>. The debug agent uses it to read <c>&amp;mrt_start</c> (the
  /// text base) so a driver-supplied code offset can be turned into an absolute breakpoint address —
  /// the same base the panic symbolizer subtracts.
  /// </summary>
  void LeaFuncAddr(VReg dest, string codeLabel);

  // ---- Arithmetic ----

  void AddRegImm(VReg dest, long imm);
  void SubRegImm(VReg dest, long imm);
  void AddRegReg(VReg dest, VReg src);
  void SubRegReg(VReg dest, VReg src);
  void MulRegReg(VReg dest, VReg src);
  void ShlRegImm(VReg dest, int shift);
  void ShrRegImm(VReg dest, int shift);
  void ShrRegReg(VReg dest, VReg count);
  void ShlRegReg(VReg dest, VReg count);
  void AndRegReg(VReg dest, VReg src);
  void OrRegReg(VReg dest, VReg src);
  void XorRegReg(VReg dest, VReg src);

  // ---- Bit manipulation ----

  /// <summary>Find index of lowest set bit: dest = tzcnt(src). Sets ZF if src==0.
  /// On x86: BSF dest, src. On ARM64: RBIT+CLZ (gives 64 if src==0).</summary>
  void BitScanForward(VReg dest, VReg src);

  /// <summary>Clear bit at bitIndex in memory at [base + offset].
  /// On x86: BTR [base+offset], bitIndex. On ARM64: load/bic/store sequence.</summary>
  void BitTestAndReset(VReg baseReg, int offset, VReg bitIndex);

  /// <summary>Set bit at bitIndex in memory at [base + offset].
  /// On x86: BTS [base+offset], bitIndex. On ARM64: load/orr/store sequence.</summary>
  void BitTestAndSet(VReg baseReg, int offset, VReg bitIndex);

  // ---- Comparison & branching ----

  void CmpRegReg(VReg left, VReg right);
  void CmpRegImm(VReg reg, long imm);
  void TestRegReg(VReg left, VReg right);
  void Jump(string label);
  void JumpIf(Condition cond, string label);

  /// <summary>Branch if register == 0 (CBZ on ARM64, TEST+JZ on x86).</summary>
  void JumpIfZero(VReg reg, string label);

  /// <summary>Branch if register != 0 (CBNZ on ARM64, TEST+JNZ on x86).</summary>
  void JumpIfNonZero(VReg reg, string label);

  // ---- Calls ----

  /// <summary>Call an internal runtime function by label.</summary>
  void Call(string label);

  /// <summary>Call an OS/libc function. Backend resolves platform-specific import.</summary>
  void CallImport(string function);

  /// <summary>Call OS function on system stack (safe from green thread stacks).</summary>
  void CallImportOnSystemStack(string function);

  /// <summary>Call via function pointer in register.</summary>
  void CallIndirect(VReg target);

  // ---- Return value ----

  /// <summary>
  /// Move <paramref name="src"/> into the platform's return register (RAX on x86, X0 on ARM64),
  /// then emit a function return. Call instead of FunctionEnd() when the function has a return value.
  /// </summary>
  void ReturnValue(VReg src);

  // ---- OS memory allocation ----

  /// <summary>
  /// Allocate <paramref name="size"/> bytes from the OS, zero-initialized. Returns pointer in <paramref name="dest"/>.
  /// On Windows: VirtualAlloc(NULL, size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE).
  /// On macOS:   mmap(NULL, size, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0).
  /// Clobbers Arg0..Arg5.
  /// </summary>
  void OsAllocPages(VReg dest, VReg size);

  /// <summary>
  /// Attempt to allocate <paramref name="size"/> bytes using large/huge pages.
  /// Returns the pointer in <paramref name="dest"/>, or NULL if large pages are unavailable.
  /// On Windows: VirtualAlloc with MEM_COMMIT|MEM_RESERVE|MEM_LARGE_PAGES, PAGE_READWRITE.
  ///             Size must be a multiple of the large page size (2MB on x86-64).
  ///             Requires SeLockMemoryPrivilege; returns NULL if the call fails.
  /// On macOS:   mmap with MAP_ANON|MAP_PRIVATE|MAP_SUPERPAGE. Falls back to NULL if unsupported.
  /// Clobbers Arg0..Arg5.
  /// </summary>
  void OsAllocLargePages(VReg dest, VReg size);

  /// <summary>
  /// Release memory previously allocated via OsAllocPages.
  /// On Windows: VirtualFree(ptr, 0, MEM_RELEASE) — size ignored.
  /// On macOS:   munmap(ptr, size).
  /// Clobbers Arg0..Arg5.
  /// </summary>
  void OsFreePages(VReg ptr, VReg size);

  // ---- Bulk memory ----

  /// <summary>
  /// Fill <paramref name="count"/> qwords at <paramref name="destAddr"/> with <paramref name="value"/>.
  /// On x86: REP STOSQ (requires Scratch0=RAX, Arg0=RCX, Arg5=RDI).
  /// On ARM64: tight STR post-index loop.
  /// Clobbers destAddr (advances past the filled region) and count (decremented to 0).
  /// </summary>
  void FillMemoryQwords(VReg destAddr, VReg value, VReg count);

  // ---- Atomics ----

  /// <summary>Atomic increment of [baseAddr + offset]. (LOCK INC / LDAXR+ADD+STLXR)</summary>
  void AtomicInc(VReg baseAddr, int offset);

  /// <summary>Atomic decrement of [baseAddr + offset]. Sets zero flag when result is 0.</summary>
  void AtomicDec(VReg baseAddr, int offset);

  /// <summary>Atomic exchange-add: old = [baseAddr+offset]; [baseAddr+offset] += val; val = old.</summary>
  void AtomicXadd(VReg baseAddr, int offset, VReg val);

  /// <summary>
  /// Atomic compare-and-swap on a 64-bit memory location:
  ///   if [destBase+offset] == expected: [destBase+offset] = desired; success=1
  ///   else:                              expected = [destBase+offset]; success=0
  /// Success/failure is written into VReg.Scratch3 (not flags), so callers can do
  /// arbitrary IR ops (e.g. LoadIndirect) before branching on the result.
  /// x86 clobbers VReg.Scratch0 (= RAX) via LOCK CMPXCHG's implicit operand — the
  /// expected value is moved into RAX and the post-instruction RAX holds the
  /// observed memory value (equal to <paramref name="expected"/> on success, or
  /// the actual current value on failure).
  /// Callers must NOT pass VReg.Scratch0 or VReg.Scratch3 as <paramref name="expected"/>
  /// or <paramref name="desired"/>, and must not have anything live in Scratch0
  /// across the call.
  /// </summary>
  void AtomicCAS(VReg destBase, int offset, VReg expected, VReg desired);

  /// <summary>
  /// Emit the architecture's spin-wait hint (PAUSE on x86, YIELD on ARM64): tells the core that
  /// this loop is waiting on another core rather than doing work, so it can drop the pipeline
  /// speculation and, on SMT, hand the sibling thread the front end. The hand-emitted spins in this
  /// runtime already use it (__gt_ppw_spin and EmitAwaitedStackVacatedGate, both in
  /// ARM64CodeEmitter.Runtime.cs); shared code needs the same instruction under a portable name, and
  /// the park protocol's two waits — __netpoll_claim_done's ioYielded gate and __netpoll_park_done's
  /// Claiming gate — are what ask for it.
  /// </summary>
  void SpinHint();

  /// <summary>
  /// Full memory barrier (MFENCE on x86, DMB ISH on ARM64). Orders all prior memory
  /// accesses before all subsequent memory accesses on this core. Used for Dekker-style
  /// protocols where a store must be globally visible before a later unrelated load.
  /// </summary>
  void FullBarrier();

  /// <summary>
  /// Load-acquire from [baseReg + offset]: the load is ordered before all subsequent
  /// memory accesses on this core (LDAR on ARM64; a plain load on x86, whose TSO model
  /// already gives every load acquire semantics). Pairs with StoreRelease on the same
  /// location for lockless single-word publish/observe (e.g. mspan owning_p routing).
  /// </summary>
  void LoadAcquire(VReg dest, VReg baseReg, int offset);

  /// <summary>
  /// Store-release to [baseReg + offset]: all prior memory accesses on this core are
  /// ordered before this store becomes visible (STLR on ARM64; a plain store on x86 TSO).
  /// Pairs with LoadAcquire on the same location.
  /// </summary>
  void StoreRelease(VReg baseReg, int offset, VReg src);

  // ---- Labels & data ----

  void DefineLabel(string label);
  void DefineGlobal(string label, int size, long initValue);
  void DefineSymdata(string label, byte[] data);

  // ---- Locking ----

  /// <summary>Acquire a platform lock (EnterCriticalSection / os_unfair_lock_lock).</summary>
  void LockAcquire(string lockGlobal);

  /// <summary>Release a platform lock.</summary>
  void LockRelease(string lockGlobal);

  // ---- TLS ----

  /// <summary>Load the current P* (processor context) from TLS into dest.</summary>
  void LoadCurrentP(VReg dest);

  // ---- Scheduler platform helpers ----

  /// <summary>
  /// Get current time in milliseconds into dest register.
  /// Windows: GetTickCount64 (returns ms directly).
  /// macOS: clock_gettime(CLOCK_UPTIME_RAW) converted to ms.
  /// The <paramref name="scratchSlot"/> parameter provides a stack slot index for
  /// platforms that need scratch space (ARM64 uses two slots for a timespec struct).
  /// On x86 this parameter is ignored.
  /// Clobbers Arg0..Arg4 and Scratch0..Scratch2.
  /// </summary>
  void GetCurrentTimeMs(VReg dest, int scratchSlot);

  /// <summary>
  /// Get the current monotonic HIGH-RESOLUTION time, in nanoseconds, into dest.
  /// Windows: QueryPerformanceFrequency + QueryPerformanceCounter, scaling the
  ///          counter's ticks to nanoseconds (its period is typically 100 ns).
  /// macOS:   clock_gettime(CLOCK_MONOTONIC) -> tv_sec * 1e9 + tv_nsec.
  /// This is a separate entry point from <see cref="GetCurrentTimeMs"/> rather than a
  /// finer-grained replacement for it, because the two read genuinely different counters:
  /// the ms clock is the coarse scheduler tick (GetTickCount64, ~15.6 ms granularity),
  /// which is cheaper and is what the green-thread timer heap wants, while this one is
  /// the performance counter a profiler wants. Callers pick by what they measure.
  /// The <paramref name="scratchSlot"/> parameter provides the first of TWO consecutive
  /// stack slots used as the API's out-parameter buffer (the two LARGE_INTEGERs on
  /// Windows, a timespec on POSIX).
  /// Clobbers Arg0..Arg4 and Scratch0..Scratch2.
  /// </summary>
  void GetCurrentTimeNanos(VReg dest, int scratchSlot);

  /// <summary>
  /// Get the current WALL-CLOCK time, in whole seconds since the Unix epoch (1970-01-01 UTC),
  /// into dest.
  /// Windows: GetSystemTimeAsFileTime -> rebase from the 1601 FILETIME epoch and scale
  ///          its 100 ns ticks down to seconds.
  /// macOS:   clock_gettime(CLOCK_REALTIME) -> tv_sec, which already counts from the epoch.
  /// This is the ONLY calendar time source. <see cref="GetCurrentTimeMs"/> and
  /// <see cref="GetCurrentTimeNanos"/> are both MONOTONIC — their absolute values are
  /// meaningless (milliseconds since boot, performance-counter ticks) and only differences
  /// between two readings mean anything. Neither can answer "what is today's date"; only
  /// this can. The trade is the usual one: a wall clock can jump backwards when the system
  /// clock is adjusted, so it must never be used to measure a duration.
  /// The <paramref name="scratchSlot"/> parameter provides a stack slot used as the API's
  /// out-parameter buffer (a FILETIME on Windows, a timespec on POSIX).
  /// Clobbers Arg0..Arg4 and Scratch0..Scratch2.
  /// </summary>
  void GetCurrentUnixTimeSeconds(VReg dest, int scratchSlot);

  /// <summary>
  /// Get the CPU time consumed by the CALLING THREAD, into dest.
  /// Windows: QueryThreadCycleTime(GetCurrentThread(), &amp;out) — TSC ticks.
  /// macOS:   clock_gettime(CLOCK_THREAD_CPUTIME_ID) -> tv_sec * 1e9 + tv_nsec — nanoseconds.
  /// This is the fourth clock, and the only one that is not a clock at all: it advances only
  /// while this thread is actually scheduled. The three above all count wall time, so a
  /// duration measured with them includes every other process on the machine; this one
  /// cannot see preemption, which is what makes it usable for cost measurement on a box
  /// that is doing something else.
  /// ⚠ THE UNIT IS PLATFORM-DEFINED AND THE PLATFORMS DO NOT AGREE — TSC ticks on Windows,
  /// nanoseconds on POSIX — because there is no reliable way to normalize the first into the
  /// second (QueryPerformanceFrequency is the performance counter's rate, NOT the TSC's).
  /// Callers therefore compare ratios, which are unit-free, or absolutes within one platform.
  /// It is also NOT a retired-instruction count and not reproducible to the digit: it still
  /// moves with turbo, thermal throttling and cache pressure from other cores.
  /// The <paramref name="scratchSlot"/> parameter provides the first of TWO consecutive stack
  /// slots used as the API's out-parameter buffer (a ULONG64 on Windows, a timespec on POSIX).
  /// Clobbers Arg0..Arg4 and Scratch0..Scratch2.
  /// </summary>
  void GetThreadCpuTicks(VReg dest, int scratchSlot);

  /// <summary>
  /// Get current process ID into dest register (zero-extended).
  /// Windows: GetCurrentProcessId.
  /// macOS / POSIX: getpid.
  /// Stable for the process's lifetime; differs across concurrent
  /// processes. Used by stdlib helpers that need to disambiguate
  /// filesystem temp paths or other shared-resource names across
  /// sibling subprocesses spawned by a parent. The Win32 / POSIX
  /// callouts have no parameters and do not require scratch slots.
  /// </summary>
  void GetCurrentProcessId(VReg dest);

  /// <summary>
  /// Wake an idle worker thread.
  /// Windows: SetEvent(p->wakeEvent) where POffWakeEvent = 0x38.
  /// macOS: dispatch_semaphore_signal(p->wakeSemaphore) where POffWakeSemaphore = 0x38.
  /// Clobbers Arg0..Arg1.
  /// </summary>
  void WakeWorker(VReg p);

  /// <summary>
  /// Spawn a new worker OS thread for P[i].
  /// Windows: CreateThread with __sched_worker_loop as entry point.
  /// macOS: pthread_create with __sched_worker_loop as entry point.
  /// Stores the thread handle in p->osThreadHandle (offset 0x40).
  /// Clobbers Arg0..Arg5.
  /// </summary>
  void SpawnWorker(VReg p);

  /// <summary>
  /// Drive one turn of EVERY engine a parked green thread can be waiting on — the pending-waiter
  /// hand-off, the I/O completion queue, the timer heap, the netpoll recovery net, and on backends
  /// that poll rather than being pushed to, the kernel event queue — inline on the CALLER'S OWN
  /// STACK. The exact set differs by backend (x86's completion port is drained by a dedicated IOCP
  /// thread; arm64 must poll kqueue itself), which is why this is one named OPERATION here and one
  /// sequence inside each backend rather than a list any caller assembles.
  ///
  /// ⚠ A COOPERATIVE SPIN THAT DOES NOT CALL THIS IS A HANG, NOT A SLOWDOWN. Under
  /// <c>MAXON_MAX_PROCS=1</c> the spinning M is the only one there is, so an engine it declines to
  /// poll is one whose parked GTs never wake. Every idle loop in the runtime already drives it;
  /// <c>maxon_yield</c> is the one that a USER program can spin on, which is why the operation had
  /// to become reachable from shared code at all.
  ///
  /// Clobbers the call-clobbered set — it is a run of calls.
  /// </summary>
  void DriveSchedulerAndIo();

  /// <summary>
  /// Hand this M back to its scheduler: <c>__gt_context_switch(from = current GT, to =
  /// &amp;P-&gt;mainThread)</c>. The caller must already have established that the current GT is NOT
  /// the P's inline mainThread — a self-switch is a no-op that silently keeps running — and must
  /// have arranged its own wakeup, because nothing here queues it.
  ///
  /// ⚠ IT MUST NOT WRITE <c>mainThread.status</c>, and x86's implementation carries the measured
  /// history of why (a GT's own park loop is the sole owner of its status field; seven sites
  /// stamped it anyway and cut a main-thread <c>sleep(300)</c> to 0 ms). Naming the operation is
  /// what keeps that rule in ONE place per backend now that a shared emitter needs it too.
  ///
  /// Clobbers the call-clobbered set.
  /// </summary>
  void SwitchToMainThread();

  /// <summary>
  /// Unsigned divide remainder: dest = dividend % divisor (divisor is immediate).
  /// Clobbers Scratch0..Scratch2 as needed.
  /// </summary>
  void UDivRemainder(VReg dest, VReg dividend, long divisor);

  /// <summary>
  /// Unsigned divide remainder with register divisor: dest = dividend % divisor.
  /// On x86: XOR RDX,RDX; MOV RAX,dividend; DIV divisor_reg → remainder in RDX.
  /// On ARM64: UDIV + MSUB.
  /// Clobbers Scratch0..Scratch2 as needed. The divisor register must not be
  /// Scratch0 or Scratch2 (RAX/RDX on x86).
  /// </summary>
  void UDivRemainderReg(VReg dest, VReg dividend, VReg divisor);

  // ---- Shared memory (debugstream) ----

  /// <summary>
  /// Open an existing named shared memory segment and map it read-write.
  /// name_ptr = pointer to null-terminated name, size = bytes to map.
  /// Returns the mapped base pointer in dest (NULL on failure).
  /// Windows: OpenFileMappingA + MapViewOfFile.
  /// macOS: shm_open + mmap.
  /// Clobbers Arg0..Arg5, Scratch3.
  /// </summary>
  void OsOpenAndMapSharedMemory(VReg dest, VReg name_ptr, VReg size);

  /// <summary>
  /// Unmap a shared memory region. Does not destroy the named segment (monitor owns that).
  /// Windows: UnmapViewOfFile(base).
  /// macOS: munmap(base, size).
  /// Clobbers Arg0..Arg1.
  /// </summary>
  void OsUnmapSharedMemory(VReg base_ptr, VReg size);

  /// <summary>
  /// Sleep the calling OS thread for <paramref name="millis"/> milliseconds.
  /// Windows: Sleep(dwMilliseconds). macOS / POSIX: usleep(millis * 1000).
  /// Milliseconds rather than microseconds because that is the coarser of the two platforms'
  /// granularities and a resolution neither can miss. Clobbers Arg0..Arg5 and Scratch0..Scratch2.
  /// </summary>
  void OsSleepMillis(VReg millis);

  /// <summary>
  /// <paramref name="dest"/> = the unsigned decimal value of the environment variable whose
  /// null-terminated NAME is at symdata label <paramref name="nameSymdata"/>, or 0 when it is
  /// unset, empty, or does not begin with a digit. Parsing stops at the first non-digit byte, so a
  /// trailing suffix is ignored rather than rejected — every caller treats 0 as "leave the default
  /// alone", which is also what a malformed value should get.
  ///
  /// <paramref name="scratchSlot"/> names a FOUR-SLOT (32-byte) value buffer on Windows, because
  /// GetEnvironmentVariableA copies into caller memory; POSIX ignores it, because getenv returns a
  /// pointer into the environment block. Same convention as <see cref="GetCurrentTimeMs"/>'s
  /// out-parameter slot.
  ///
  /// ⚠ THE BUFFER GROWS TOWARD RBP, SO IT OCCUPIES SLOTS <paramref name="scratchSlot"/> DOWN TO
  /// <paramref name="scratchSlot"/>−3, NOT UP TO +3 — a caller that reserves the wrong three
  /// neighbours will be overwritten by a call that reports success. x86's slot N is at
  /// <c>rbp−(N+1)*8</c> (see <see cref="LeaLocal"/>), so ADDRESSES rise as the slot index falls, and
  /// a 32-byte buffer based at slot N covers N, N−1, N−2, N−3. Slot 0 is therefore NEVER a legal
  /// argument: its buffer would run over the saved RBP and the return address. The two callers in
  /// tree pass 4 and 8, which cover 4..1 and 8..5 and are disjoint.
  /// Clobbers Arg0..Arg5 and Scratch0..Scratch3.
  /// </summary>
  void ReadEnvUnsigned(VReg dest, string nameSymdata, int scratchSlot);

  /// <summary>
  /// Yield the current OS thread's remaining time slice (Windows: SwitchToThread; POSIX: sched_yield).
  /// The debug agent's park loop calls this between polls of the control mailbox so a stop-the-world
  /// pause does not busy-spin a core while the driver decides what to do next. Clobbers the call-clobbered
  /// register set.
  /// </summary>
  void OsYield();

  // ---- Platform-specific labels ----

  /// <summary>Label of the platform write-null-terminated-cstr-to-stderr function.
  /// x86: "maxon_write_stderr"; ARM64: "rt_write_cstr_stderr".</summary>
  string WriteStderrLabel { get; }

  /// <summary>
  /// Label of the embedded runtime symbol table, which the panic backtrace reads and which sits
  /// immediately past `.text` — so <c>&amp;SymbolTableLabel − &amp;mrt_start</c> is the exact `.text`
  /// size bound the symbolizer trusts. The name differs by backend (x86: "__symtable"; ARM64:
  /// "__symtab"), so the debug agent's set-breakpoint bounds check reads it from here rather than
  /// hard-coding one spelling.
  /// </summary>
  string SymbolTableLabel { get; }

  // ---- Local address / byte memory ----

  /// <summary>Load address of a stack frame slot into dest.
  /// x86: LEA R(dest), [RBP - (slotIndex+1)*8]
  /// ARM64: ADD R(dest), X29, #(16 + slotIndex*8)</summary>
  void LeaLocal(VReg dest, int slotIndex);

  /// <summary>Store the low byte of src into [baseReg + offset].</summary>
  void StoreIndirectByte(VReg baseReg, int offset, VReg src);

  /// <summary>Load a byte (zero-extended to 64 bits) from [baseReg + offset] into dest.</summary>
  void LoadIndirectByte(VReg dest, VReg baseReg, int offset);

  // ---- Platform info ----

  bool IsWindows { get; }
  bool IsMacOS { get; }

  /// <summary>Label name for the global scheduler lock (protects global run queue).
  /// x86: CRITICAL_SECTION label; ARM64: os_unfair_lock label.</summary>
  string SchedLockLabel { get; }

  /// <summary>Label name for the global timer lock (protects timer heap).
  /// x86: CRITICAL_SECTION label; ARM64: os_unfair_lock label.</summary>
  string TimerLockLabel { get; }

  /// <summary>
  /// Take / release the lock protecting the all-live-green-threads list (<c>__gt_all_head</c> and
  /// <c>__gt_live_count</c>), which __gt_spawn and the completion trampoline mutate.
  ///
  /// ⚠ A PAIR OF METHODS RATHER THAN A LABEL FOR <see cref="LockAcquire"/>, and the difference is
  /// not cosmetic: the two backends guard this list with DIFFERENT PRIMITIVES. x86 uses a
  /// CRITICAL_SECTION, which is what <see cref="LockAcquire"/> emits anyway; ARM64 uses a plain
  /// <c>os_unfair_lock</c>, while its <see cref="LockAcquire"/> is a RECURSIVE spinlock over a
  /// <c>{ lock, owner, count }</c> triple. Handing <c>__sched_all_lock</c> to that would put two
  /// incompatible protocols on one word and exclude nobody — a lock that silently does not lock.
  /// Naming the operation instead of the label lets each backend spell what its own mutators
  /// already spell.
  /// </summary>
  void AllThreadsLockAcquire();

  /// <summary>Release the lock taken by <see cref="AllThreadsLockAcquire"/>.</summary>
  void AllThreadsLockRelease();

  // ---- Fault handler (CPU faults: nil deref, divide-by-zero, stack overflow) ----

  /// <summary>
  /// Emit code at process startup that registers <paramref name="thunkLabel"/> as the
  /// CPU-fault handler.
  /// Windows: AddVectoredExceptionHandler(1, thunkLabel).
  /// macOS:   sigaction(SIGSEGV/SIGFPE/SIGBUS, sa_sigaction=thunkLabel, ...).
  /// Caller is responsible for emitting the thunk function body via
  /// EmitFaultHandlerProlog / EmitFaultHandlerEpilog and the shared __gt_fault_handler.
  /// Clobbers Arg0..Arg5.
  /// </summary>
  void InstallFaultHandler(string thunkLabel);

  /// <summary>
  /// Emit code that registers <paramref name="thunkLabel"/> as the debug agent's TRAP handler,
  /// chaining with — never shadowing — the CPU-fault handler.
  /// Windows: AddVectoredExceptionHandler(1, thunkLabel) — installed AFTER the fault handler, so it
  ///          sits at the FRONT of the VEH chain and defers what it does not own (P3a: everything)
  ///          to the fault thunk by returning EXCEPTION_CONTINUE_SEARCH.
  /// macOS:   sigaction(SIGTRAP, thunkLabel) — a signal distinct from the fault handler's
  ///          SIGSEGV/SIGFPE/SIGBUS, so the two never contend.
  /// Called only from __dbg_init, so it runs only when MAXON_DEBUG activated the agent.
  /// Clobbers Arg0..Arg5.
  /// </summary>
  void OsInstallTrapHandler(string thunkLabel);

  /// <summary>
  /// Emit the entry trampoline of the fault handler thunk. Called by the OS with
  /// platform-specific arguments; this method's job is to extract the fault context
  /// (faultCode, faultRip, faultRsp, faultFp) into VReg Arg0..Arg3 and tail-call the
  /// shared label <paramref name="sharedHandlerLabel"/> (which is __gt_fault_handler).
  ///
  /// Windows VEH callback signature: LONG handler(EXCEPTION_POINTERS* p) — RCX = p.
  /// macOS sigaction handler signature: void handler(int sig, siginfo_t*, ucontext_t*) — X0=sig, X1=info, X2=uctx.
  ///
  /// Internally this method also defines <paramref name="thunkLabel"/> as the function
  /// entry, prepares for the epilog (saving the OS-context pointer in a callee-saved reg),
  /// and emits any platform glue needed to safely call the shared handler.
  ///
  /// Fault codes (Arg0) are the platform-neutral GtLayout.FaultCode* values.
  /// </summary>
  void EmitFaultHandlerProlog(string thunkLabel, string sharedHandlerLabel);

  /// <summary>
  /// Emit the exit path of the fault handler thunk. The shared handler returned in
  /// VReg.Ret one of:
  ///   0                              — recover: read (rip, rsp, fp) from
  ///                                    P->currentGt->fault_redirect_{rip,rsp,fp}
  ///                                    and rewrite the OS-provided context with them.
  ///   GtLayout.FaultCodeDontRecover  — chain to OS default (don't recover).
  ///
  /// On the recover path:
  ///   Windows: return EXCEPTION_CONTINUE_EXECUTION (1).
  ///   macOS:   return from the sigaction handler (kernel reads the rewritten ucontext).
  ///
  /// On the don't-recover path:
  ///   Windows: return EXCEPTION_CONTINUE_SEARCH (0) — let the OS default handler run.
  ///   macOS:   restore SIG_DFL for this signal and re-raise; process dies via default disposition.
  /// </summary>
  void EmitFaultHandlerEpilog();

  /// <summary>
  /// Emit a symbolized stack trace after the fault panic line. Called by the shared
  /// EmitGtFaultDiagnostic just before process exit, running on the redirected stack.
  /// BOTH backends implement it as a call to their own mrt_fault_backtrace, which walks
  /// the faulting thread's saved-frame-pointer chain (stashed in __gt_fault_last_rbp /
  /// __gt_fault_last_rsp, bounded above by __gt_stack_high_current) and resolves each
  /// return address against the embedded symbol table, mirroring the ordinary mrt_panic
  /// trace. It stays a backend method rather than a shared emit because the walk itself
  /// is hand-encoded per architecture — see EmitStackTraceHeader (ARM64CodeEmitter.Runtime.cs)
  /// for why those four walks are four and not one.
  /// </summary>
  void EmitFaultBacktrace();
}
