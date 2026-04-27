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
  /// Full memory barrier (MFENCE on x86, DMB ISH on ARM64). Orders all prior memory
  /// accesses before all subsequent memory accesses on this core. Used for Dekker-style
  /// protocols where a store must be globally visible before a later unrelated load.
  /// </summary>
  void FullBarrier();

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

  // ---- Platform-specific labels ----

  /// <summary>Label of the platform write-null-terminated-cstr-to-stderr function.
  /// x86: "maxon_write_stderr"; ARM64: "rt_write_cstr_stderr".</summary>
  string WriteStderrLabel { get; }

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
}
