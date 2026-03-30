using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

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
  private const int CLOCK_UPTIME_RAW = 0x08; // macOS monotonic clock

  // Internal KqCtx filter for async connect completion (not a real kqueue filter)
  private const int KQCTX_CONNECT = -3;

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

  /// Call mm_raw_alloc with X0 = size. Zeros X1 (scope) when mm-trace is enabled
  /// so that internal callers don't pass garbage as the scope argument.
  private void EmitCallMmRawAlloc() {
    if (Compiler.MmTrace) EmitMovRegImm(ARM64Register.X1, 0);
    EmitBranchLink("mm_raw_alloc");
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
  /// Callers must sign-extend W0→X0 after libc calls that return int,
  /// since Apple ARM64 zero-extends 32-bit return values.
  private void EmitBranchOnLibcError(string errorLabel) {
    // CMP X0, #0 (SUBS XZR, X0, #0)
    EmitWord(0xF100001F);
    // B.LT errorLabel
    _condBranchFixups.Add((_code.Count, errorLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt));
  }

  // --- Runtime functions ---

  public void EmitRuntimeFunctions() {
    EmitMaxonWriteStdout();
    EmitMaxonWriteStderr();
    EmitManagedWrite("maxon_managed_write_stdout", 1);
    EmitManagedWrite("maxon_managed_write_stderr", 2);
    EmitMaxonExit();
    EmitWriteCstrToStderr();
    EmitMaxonPanic();
    EmitMaxonPanicPrintFrame();
    EmitMaxonBoundsCheck();
    EmitMaxonI64ToString();
    EmitMaxonU64ToString();
    EmitMaxonF64ToString();
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
    rawRt.EmitCurrentTimeMs();
    // DebugStream functions are emitted from 4-ARM64CodeEmitter.cs
    EmitMaxonFileSize();
    EmitMaxonFileRead();
    EmitMaxonFileClose();
    EmitMaxonFileDelete();
    EmitMaxonCommandLineCount();
    EmitMaxonCommandLineArg();
    EmitMaxonDirectoryExists();
    EmitMaxonCreateDirectory();
    EmitMaxonGetCurrentDirectory();

    // Additional runtime functions
    EmitMaxonBoolToString();
    EmitMaxonI64ToStringFmt();
    EmitMaxonF64ToStringFmt();
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
    EmitMaxonU64ToStringFmt();
    EmitMaxonSleep();

    // Green thread runtime for async/await
    EmitGreenThreadRuntime();

    // Process management (macOS POSIX implementation)
    EmitMaxonProcessCreate();
    EmitMaxonProcessWait();
    EmitMaxonProcessGetExitCode();
    EmitMaxonProcessClose();
    EmitMaxonProcessCreateWithCapture();
    EmitMaxonProcessReadPipe();
    EmitMaxonProcessGetHandle();
    EmitMaxonProcessCloseCapture();
    EmitMaxonProcessReadStdout();
    EmitMaxonProcessReadStderr();
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
  private void EmitMaxonPanic() {
    DefineRdata("__newline", [(byte)'\n']);
    // Ensure __symdata_base label exists at offset 0 of symdata for name resolution
    if (!_symdataLabels.ContainsKey("__symdata_base"))
      _symdataLabels["__symdata_base"] = 0;

    EmitRuntimeFunctionStart("mrt_panic", 1, 0x60);

    // Save X19 (callee-saved) so we can use it
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X19, ARM64Register.X29, 72, 8);

    // Step 1: Print the panic message (already ends with \n)
    EmitReloadArg(0); // X0 = msg_ptr
    EmitBranchLink("rt_write_cstr_stderr");

    // Step 2: Compute text_base = address of mrt_start
    EmitAdrpAddFixup(ARM64Register.X0, _funcAddrAdrpFixups, "mrt_start");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    // Step 3: Load symtab pointer and count
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__symtab");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // Load count = [symtab_ptr] (first 8 bytes)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 56, 8);

    // Load symdata_base
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__symdata_base");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);

    // Step 4: Print "Stack trace:\n"
    DefineSymdata("__panic_stacktrace", System.Text.Encoding.UTF8.GetBytes("Stack trace:\n\0"));
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__panic_stacktrace");
    EmitBranchLink("rt_write_cstr_stderr");

    // Step 5: Print first frame (the function that called panic)
    // [x29+8] = saved LR = return addr back to the function that called panic
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 8, 8); // X0 = return addr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // X1 = text_base
    EmitAluRegReg(0xCB000000, ARM64Register.X0, ARM64Register.X0, ARM64Register.X1); // X0 = ret_addr - text_base
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // text_offset
    EmitBranchLink("mrt_panic_print_frame");

    // Step 6: Initialize stack walk
    // current_frame = [x29] (panic's caller's saved X29 — from our STP prologue)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 0, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // current_frame
    EmitMovRegImm(ARM64Register.X0, 32);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // counter

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

    // Get return address: [frame_fp + 8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 8, 8); // X1 = return addr
    _condBranchFixups.Add((_code.Count, "rt_panic_walk_done"));
    EmitWord(0xB4000001); // CBZ X1, rt_panic_walk_done

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

    // Exit with code 1
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitCallImport("_exit");
    EmitWord(0xD4200000); // BRK #0
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

  // --- maxon_i64_to_string(value, buf) -> len ---
  // Converts i64 to decimal string in buffer, returns length
  private void EmitMaxonI64ToString() {
    EmitRuntimeFunctionStart("maxon_i64_to_string", 2, 0x50);

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

  private void EmitMaxonU64ToString() {
    // For now, redirect to i64 version (handles positive numbers the same way)
    DefineLabel("maxon_u64_to_string");
    EmitBranch("maxon_i64_to_string");
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

  /// <summary>
  /// maxon_i64_to_string_fmt(value, buffer, fmt_ptr, fmt_len) -> length
  /// X0 = value, X1 = buffer (>= 72 bytes), X2 = fmt_ptr, X3 = fmt_len
  /// Format: [0][width][type] where type = d/x/X/b/o
  /// Stack layout (positive offsets from x29):
  ///   [+16] = value, [+24] = buffer, [+32] = fmt_ptr, [+40] = fmt_len
  ///   [+48] = fill_char, [+56] = min_width, [+64] = type_char
  ///   [+72] = digit_start, [+80] = write_pos/end, [+88] = is_negative
  /// </summary>
  private void EmitMaxonI64ToStringFmt() {
    var noFmtLabel = $"__i64fmt_nofmt_{_uniqueLabelCounter}";
    var parseWidthLabel = $"__i64fmt_parsewidth_{_uniqueLabelCounter}";
    var parseTypeLabel = $"__i64fmt_parsetype_{_uniqueLabelCounter}";
    var positiveLabel = $"__i64fmt_positive_{_uniqueLabelCounter}";
    var hexLowerLabel = $"__i64fmt_hexlower_{_uniqueLabelCounter}";
    var hexUpperLabel = $"__i64fmt_hexupper_{_uniqueLabelCounter}";
    var decimalLabel = $"__i64fmt_decimal_{_uniqueLabelCounter}";
    var hexConvertLabel = $"__i64fmt_hexconv_{_uniqueLabelCounter}";
    var decConvertLabel = $"__i64fmt_decconv_{_uniqueLabelCounter}";
    var reverseLabel = $"__i64fmt_reverse_{_uniqueLabelCounter}";
    var reverseDoneLabel = $"__i64fmt_revdone_{_uniqueLabelCounter}";
    var padLabel = $"__i64fmt_pad_{_uniqueLabelCounter}";
    var doneLabel = $"__i64fmt_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_i64_to_string_fmt", 4, 0x70);

    // Default: fill=' ', width=0, type=0(decimal)
    EmitMovRegImm(ARM64Register.X4, (long)' ');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 48, 8); // fill
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 56, 8); // width
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X4, ARM64Register.X29, 64, 8); // type

    // If fmt_len == 0, skip parsing
    EmitReloadArg(3); // fmt_len -> X3
    _condBranchFixups.Add((_code.Count, noFmtLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X3)); // CBZ X3, noFmtLabel

    // Parse format string
    EmitReloadArg(2); // fmt_ptr -> X2
    EmitReloadArg(3); // fmt_len -> X3
    // X4 = current position in fmt string
    EmitMovRegImm(ARM64Register.X4, 0);

    // Check for '0' fill
    EmitWord(0x39400000 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5)); // LDRB W5, [X2]
    EmitMovRegImm(ARM64Register.X6, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, X6
    _condBranchFixups.Add((_code.Count, parseWidthLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE parseWidth
    // fill = '0'
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X6, ARM64Register.X29, 48, 8);
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);

    // Parse width digits
    DefineLabel(parseWidthLabel);
    // While pos < len and char is digit
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, parseTypeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE parseType
    // Load char
    EmitWord(0x38606800 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5)); // LDRB W5, [X2, X4]
    // Check if digit: char >= '0' && char <= '9'
    EmitMovRegImm(ARM64Register.X6, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, '0'
    _condBranchFixups.Add((_code.Count, parseTypeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT parseType
    EmitMovRegImm(ARM64Register.X6, (long)'9');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, '9'
    _condBranchFixups.Add((_code.Count, parseTypeLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Gt)); // B.GT parseType
    // width = width * 10 + (char - '0')
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X6, ARM64Register.X29, 56, 8); // load width
    EmitMovRegImm(ARM64Register.X7, 10);
    EmitWord(0x9B007C00 | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X6)); // MUL X6, X6, X7
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, (long)'0', isAdd: false); // char - '0'
    EmitWord(0x8B000000 | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X6)); // ADD X6, X6, X5
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X6, ARM64Register.X29, 56, 8); // store width
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: true);
    EmitBranch(parseWidthLabel);

    // Parse type character
    DefineLabel(parseTypeLabel);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X3
    _condBranchFixups.Add((_code.Count, noFmtLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE noFmt (no type char)
    EmitWord(0x38606800 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5)); // LDRB W5, [X2, X4]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 64, 8); // store type

    DefineLabel(noFmtLabel);
    // Now convert the value based on type
    EmitReloadArg(0); // value -> X0
    EmitReloadArg(1); // buf -> X1
    // Save buf start
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X1); // X3 = write position

    // Check type: 'x' (0x78) or 'X' (0x58) = hex
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 64, 8);
    EmitMovRegImm(ARM64Register.X6, (long)'x');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5));
    _condBranchFixups.Add((_code.Count, hexLowerLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq));
    EmitMovRegImm(ARM64Register.X6, (long)'X');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5));
    _condBranchFixups.Add((_code.Count, hexUpperLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq));
    EmitBranch(decimalLabel);

    // --- Hex lower ---
    DefineLabel(hexLowerLabel);
    EmitMovRegImm(ARM64Register.X5, (long)'a');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // hex_base = 'a'
    EmitBranch(hexConvertLabel);

    // --- Hex upper ---
    DefineLabel(hexUpperLabel);
    EmitMovRegImm(ARM64Register.X5, (long)'A');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // hex_base = 'A'

    // --- Hex conversion ---
    DefineLabel(hexConvertLabel);
    // Save digit start
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 72, 8);
    // X0 = value (treat as unsigned)
    // Convert loop: extract nibble, write digit
    var hexLoopLabel = $"__i64fmt_hexloop_{_uniqueLabelCounter - 1}";
    DefineLabel(hexLoopLabel);
    // digit = X0 & 0xF
    EmitWord(0x92400C00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X4)); // AND X4, X0, #0xF
    // X0 = X0 >> 4
    EmitWord(0xD344FC00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // LSR X0, X0, #4
    // if digit < 10: char = digit + '0', else char = digit - 10 + hex_base
    EmitMovRegImm(ARM64Register.X6, 10);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, 10
    var hexAlphaLabel = $"__i64fmt_hexalpha_{_uniqueLabelCounter - 1}";
    var hexWriteLabel = $"__i64fmt_hexwrite_{_uniqueLabelCounter - 1}";
    _condBranchFixups.Add((_code.Count, hexAlphaLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE hexAlpha
    // Digit 0-9
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, (long)'0', isAdd: true);
    EmitBranch(hexWriteLabel);
    DefineLabel(hexAlphaLabel);
    // Digit A-F
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 10, isAdd: false);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X6, ARM64Register.X29, 88, 8); // hex_base
    EmitWord(0x8B000000 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X4) << 5) | Reg(ARM64Register.X4)); // ADD X4, X4, X6
    DefineLabel(hexWriteLabel);
    // STRB W4, [X3]
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4));
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // Continue if X0 != 0
    _condBranchFixups.Add((_code.Count, hexLoopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, hexLoop
    EmitBranch(reverseLabel);

    // --- Decimal conversion ---
    DefineLabel(decimalLabel);
    // Mark not negative
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // is_negative = 0

    // Check negative
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, positiveLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive

    // Write '-'
    EmitMovRegImm(ARM64Register.X4, (long)'-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB '-'
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    // Negate
    EmitWord(0xCB000000 | (Reg(ARM64Register.X0) << 16) | (31u << 5) | Reg(ARM64Register.X0)); // NEG X0, X0
    EmitMovRegImm(ARM64Register.X5, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X5, ARM64Register.X29, 88, 8); // is_negative = 1

    DefineLabel(positiveLabel);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 72, 8); // digit start

    // Decimal convert loop
    DefineLabel(decConvertLabel);
    EmitMovRegImm(ARM64Register.X1, 10);
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // UDIV X2, X0, X1
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X4)); // MSUB X4, X2, X1, X0
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, (long)'0', isAdd: true);
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB digit
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2);
    _condBranchFixups.Add((_code.Count, decConvertLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, decConvert

    // --- Reverse digits ---
    DefineLabel(reverseLabel);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 80, 8); // save end pos
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 72, 8); // start
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, 1, isAdd: false); // end - 1

    DefineLabel(reverseLabel + "_loop");
    EmitWord(0xEB00001F | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X5) << 5)); // CMP X5, X6
    _condBranchFixups.Add((_code.Count, reverseDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs)); // B.HS done

    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X5]
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X8)); // LDRB W8, [X6]
    EmitWord(0x39000000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X8)); // STRB W8, [X5]
    EmitWord(0x39000000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X6]
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch(reverseLabel + "_loop");

    DefineLabel(reverseDoneLabel);
    // --- Padding ---
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 80, 8); // end
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf start
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // X4 = end - start = current length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 56, 8); // min_width
    EmitWord(0xEB00001F | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X4) << 5)); // CMP X4, X5
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE done (already wide enough)

    // Need to pad. For zero-padding, shift digits right and insert fill chars.
    // For space-padding, also shift right and insert.
    // pad_count = min_width - current_length
    EmitWord(0xCB000000 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X6)); // X6 = pad_count = width - len

    // Check if fill is '0' and is_negative — if so, shift after the '-'
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X29, 48, 8); // fill char
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X8, ARM64Register.X29, 88, 8); // is_negative

    // Shift existing content right by pad_count bytes (from end-1 down to start)
    // memmove: for i = len-1 down to 0: buf[i+pad_count] = buf[i]
    // Use X9 = src index (from len-1 down to 0)
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X4, 1, isAdd: false); // X9 = len - 1
    var shiftLoopLabel = $"__i64fmt_shift_{_uniqueLabelCounter - 1}";
    var shiftDoneLabel = $"__i64fmt_shiftdone_{_uniqueLabelCounter - 1}";
    DefineLabel(shiftLoopLabel);
    // if X9 < 0, done shifting
    EmitWord(0xF100001F | (Reg(ARM64Register.X9) << 5)); // CMP X9, #0
    _condBranchFixups.Add((_code.Count, shiftDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT shiftDone
    // Load byte at buf[X9]
    EmitWord(0x38696800 | (Reg(ARM64Register.X9) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X10)); // LDRB W10, [X1, X9]
    // dst = X9 + pad_count
    EmitWord(0x8B000000 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X9) << 5) | Reg(ARM64Register.X11)); // ADD X11, X9, X6
    // Store byte at buf[X11]
    EmitWord(0x382B6800 | (Reg(ARM64Register.X11) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X10)); // STRB W10, [X1, X11]
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: false);
    EmitBranch(shiftLoopLabel);

    DefineLabel(shiftDoneLabel);
    // Fill the gap with fill char
    // Determine fill start: if zero-pad and negative, start at 1 (after '-'), else 0
    EmitMovRegImm(ARM64Register.X9, 0); // fill start index
    EmitMovRegImm(ARM64Register.X10, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X10) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP fill, '0'
    var fillStartLabel = $"__i64fmt_fillstart_{_uniqueLabelCounter - 1}";
    _condBranchFixups.Add((_code.Count, fillStartLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE fillStart (not zero-pad)
    // Zero pad: if negative, start fill at index 1
    _condBranchFixups.Add((_code.Count, fillStartLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X8)); // CBZ X8, fillStart (not negative)
    EmitMovRegImm(ARM64Register.X9, 1);

    DefineLabel(fillStartLabel);
    // X9 = fill index, fill until X9 == X6 + fill_start
    EmitWord(0x8B000000 | (Reg(ARM64Register.X9) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X10)); // X10 = pad_count + fill_start_offset...
    // Actually just fill from X9 to X9+pad_count
    EmitWord(0x8B000000 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X9) << 5) | Reg(ARM64Register.X10)); // X10 = end = start + pad_count
    var fillLoopLabel = $"__i64fmt_fillloop_{_uniqueLabelCounter - 1}";
    DefineLabel(fillLoopLabel);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X10) << 16) | (Reg(ARM64Register.X9) << 5)); // CMP X9, X10
    _condBranchFixups.Add((_code.Count, padLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE padDone
    // Store fill char
    EmitWord(0x38296800 | (Reg(ARM64Register.X9) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X1, X9]
    EmitAddSubImm(ARM64Register.X9, ARM64Register.X9, 1, isAdd: true);
    EmitBranch(fillLoopLabel);

    DefineLabel(padLabel);
    // Update length and null-terminate
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 56, 8); // width (= new length)
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X5);
    EmitWord(0x8B000000 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X1) << 5) | Reg(ARM64Register.X4)); // X4 = buf + length
    EmitMovRegImm(ARM64Register.X7, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X4) << 5) | Reg(ARM64Register.X7)); // STRB 0, [X4]
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X3);
    EmitRuntimeFunctionEnd();

    DefineLabel(doneLabel);
    // No padding needed, just null-terminate and return length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 80, 8); // end
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buf start
    EmitWord(0xCB000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // X0 = end - start
    // Null terminate
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X4)); // STRB 0, [end]
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_f64_to_string(value_in_D0, buffer_in_X0) -> length_in_X0
  /// Converts a double to string like "3.14159" or "-0.5" or "0.0".
  /// Stack layout (runtime function, positive offsets from x29):
  ///   [+16] = buffer (saved X0), [+24] = is_negative
  ///   [+32] = integer_part, [+40] = int_str_length
  ///   [+48] = scratch
  /// D0 is NOT saved by EmitRuntimeFunctionStart, so use it immediately.
  /// </summary>
  private void EmitMaxonF64ToString() {
    var notZeroLabel = $"__f64str_notzero_{_uniqueLabelCounter}";
    var positiveLabel = $"__f64str_positive_{_uniqueLabelCounter}";
    var noSignLabel = $"__f64str_nosign_{_uniqueLabelCounter}";
    var fracLoopLabel = $"__f64str_fracloop_{_uniqueLabelCounter}";
    var stripLoopLabel = $"__f64str_striploop_{_uniqueLabelCounter}";
    var stripDoneLabel = $"__f64str_stripdone_{_uniqueLabelCounter}";
    var epilogueLabel = $"__f64str_epilogue_{_uniqueLabelCounter}";
    var fracOkLabel = $"__f64str_frac_ok_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_f64_to_string", 1, 0x60);
    // Note: only 1 GPR arg (X0 = buffer). D0 has the float but isn't saved by the framework.

    // Save D0 to [x29, #48] using STR D0, [X29, #48]
    EmitWord(0xFD000000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #48]

    // Check if value == 0.0
    // FCMP D0, #0.0
    EmitWord(0x1E602008); // FCMP D0, #0.0
    _condBranchFixups.Add((_code.Count, notZeroLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE notZero

    // Write "0.0\0"
    EmitReloadArg(0); // X0 = buffer
    EmitMovRegImm(ARM64Register.X1, (long)'0');
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB '0', [X0]
    EmitMovRegImm(ARM64Register.X1, (long)'.');
    EmitWord(0x39000400 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB '.', [X0, #1]
    EmitMovRegImm(ARM64Register.X1, (long)'0');
    EmitWord(0x39000800 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB '0', [X0, #2]
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000C00 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB 0, [X0, #3]
    EmitMovRegImm(ARM64Register.X0, 3);
    EmitBranch(epilogueLabel);

    // Handle sign
    DefineLabel(notZeroLabel);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // is_negative = 0

    // Reload D0 from stack (since we might have clobbered it... actually we didn't, but be safe)
    EmitWord(0xFD400000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #48]

    // FCMP D0, #0.0
    EmitWord(0x1E602008);
    _condBranchFixups.Add((_code.Count, positiveLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive (not negative)

    // Negative: negate D0 = -D0 via FNEG D0, D0
    EmitWord(0x1E614000); // FNEG D0, D0
    // Save negated value
    EmitWord(0xFD000000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #48]
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 24, 8); // is_negative = 1

    DefineLabel(positiveLabel);
    // D0 = |value|. Extract integer part.
    // FCVTZS X1, D0 (truncate to signed integer)
    EmitWord(0x9E780001); // FCVTZS X1, D0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 32, 8); // integer_part

    // Call maxon_i64_to_string(integer_value, buffer + is_negative)
    EmitReloadArg(0); // X0 = buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 24, 8); // is_negative
    EmitWord(0x8B000000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // X1 = buf + is_negative
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = integer_part
    EmitBranchLink("maxon_i64_to_string");
    // X0 = int string length
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // int_str_length

    // If negative, write '-' at buffer[0] and add 1 to length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // is_negative
    _condBranchFixups.Add((_code.Count, noSignLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, noSign
    EmitReloadArg(0); // buffer
    EmitMovRegImm(ARM64Register.X2, (long)'-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB '-', [buffer]
    // length += 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    DefineLabel(noSignLabel);
    // Append '.' at buffer[length]
    EmitReloadArg(0); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 40, 8); // length
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // X2 = buf + len
    EmitMovRegImm(ARM64Register.X3, (long)'.');
    EmitWord(0x39000000 | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X3)); // STRB '.', [buf+len]

    // Extract fractional part: frac = |value| - integer_part
    // Reload |value| into D0
    EmitWord(0xFD400000 | ((48u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #48]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // integer_part
    // SCVTF D1, X0 (convert int to double)
    EmitWord(0x9E620001); // SCVTF D1, X0
    // FSUB D0, D0, D1 (fractional part)
    EmitWord(0x1E613800); // FSUB D0, D0, D1

    // Multiply by 1e6 for 6 decimal places
    // Load 1000000.0 into D1 via FMOV from X register
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(1000000.0));
    // FMOV Dd, Xn = 0x9E670000 | (Rn << 5) | Rd
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0
    // FMUL D0, D0, D1
    EmitWord(0x1E610800); // FMUL D0, D0, D1

    // Add 0.5 for rounding
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(0.5));
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0
    // FADD D0, D0, D1
    EmitWord(0x1E612800); // FADD D0, D0, D1

    // FCVTZS X0, D0 (truncate to integer = fractional digits)
    EmitWord(0x9E780000); // FCVTZS X0, D0

    // Clamp to 999999 if rounding pushed it over
    EmitMovRegImm(ARM64Register.X1, 1000000);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, 1000000
    _condBranchFixups.Add((_code.Count, fracOkLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT fracOk
    EmitMovRegImm(ARM64Register.X0, 999999);
    DefineLabel(fracOkLabel);

    // Write 6 fractional digits at buf + int_len + 1 (after the '.')
    // We write them from position 5 down to 0 (dividing by 10 each time)
    // Save frac_digits (in X0) to [x29, #56] before we clobber X0 with buffer pointer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // save frac_digits
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 40, 8); // X3 = int len
    EmitReloadArg(0); // X0 = buffer
    EmitWord(0x8B000000 | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // X3 = buf + int_len
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true); // X3 = buf + int_len + 1 (past '.')
    // Reload frac_digits into X0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // X0 = frac_digits

    // X0 = frac_digits, X3 = write base
    // We need to write from X3[0] to X3[5]
    // Algorithm: for i=5 down to 0: digit = X0 % 10, X0 /= 10, buf[i] = '0'+digit
    EmitMovRegImm(ARM64Register.X4, 6); // loop counter

    DefineLabel(fracLoopLabel);
    EmitMovRegImm(ARM64Register.X1, 10);
    // UDIV X2, X0, X1
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    // MSUB X5, X2, X1, X0 (remainder)
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5));
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, (long)'0', isAdd: true);
    // Store at X3[X4-1]
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X4, 1, isAdd: false);
    EmitWord(0x38266800 | (Reg(ARM64Register.X6) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X5)); // STRB W5, [X3, X6]
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2); // quotient
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: false);
    _condBranchFixups.Add((_code.Count, fracLoopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X4)); // CBNZ X4, loop

    // Strip trailing zeros (keep at least 1 fractional digit)
    // X6 = pointer to last digit = X3+5
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X3, 5, isAdd: true);

    DefineLabel(stripLoopLabel);
    // if X6 <= X3, stop
    EmitWord(0xEB00001F | (Reg(ARM64Register.X3) << 16) | (Reg(ARM64Register.X6) << 5)); // CMP X6, X3
    _condBranchFixups.Add((_code.Count, stripDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ls)); // B.LS stripDone
    // LDRB W7, [X6]
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7));
    EmitMovRegImm(ARM64Register.X8, (long)'0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP W7, '0'
    _condBranchFixups.Add((_code.Count, stripDoneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE stripDone
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch(stripLoopLabel);

    DefineLabel(stripDoneLabel);
    // Null-terminate at X6+1
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitMovRegImm(ARM64Register.X7, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // STRB 0, [X6]

    // Total length = X6 - buffer
    EmitReloadArg(0); // buffer
    EmitWord(0xCB000000 | (Reg(ARM64Register.X0) << 16) | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X0)); // X0 = X6 - buffer

    DefineLabel(epilogueLabel);
    EmitRuntimeFunctionEnd();

  }

  // --- maxon_f64_to_string_fmt(D0=value, X0=buffer, X1=fmtPtr, X2=fmtLen) -> X0=length ---
  // Format: [0][width][.precision] — e.g., ".2" (2 decimal places), "8.2" (width 8, 2 dp)
  // Stack layout (runtime function saves X0-X2 at [x29,#16..#32]):
  //   [x29, #40] = saved D0 (float value)
  //   [x29, #48] = is_negative
  //   [x29, #56] = min_width
  //   [x29, #64] = precision (default -1 = not specified)
  //   [x29, #72] = integer_part
  //   [x29, #80] = int_str_length (includes sign)
  //   [x29, #88] = frac_digits
  //   [x29, #96] = fill_char (' ' or '0')
  //   [x29, #104] = content_len
  //   [x29, #112] = parse_index / scratch
  private void EmitMaxonF64ToStringFmt() {
    var lbl = $"__f64fmt_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    EmitRuntimeFunctionStart("maxon_f64_to_string_fmt", 3, 0xC0);
    // D0 has float value, not saved by framework
    // Save D0 to [x29, #40]
    EmitWord(0xFD000000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #40]

    // Initialize defaults: width=0, precision=-1, fill=' '
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // width = 0
    EmitMovRegImm(ARM64Register.X0, -1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // precision = -1
    EmitMovRegImm(ARM64Register.X0, ' ');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8); // fill = ' '

    // If fmt_len == 0, delegate to default
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8); // X0 = fmtLen from [x29,#32]
    _condBranchFixups.Add((_code.Count, $"{lbl}_default"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X0)); // CBZ X0, default

    // X5 = fmtPtr, X6 = parse_index, X7 = fmt_len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 24, 8); // X5 = fmtPtr from [x29,#24]
    EmitMovRegImm(ARM64Register.X6, 0); // parse_index
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X29, 32, 8); // X7 = fmtLen from [x29,#32]

    // Check for '0' fill char
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5]
    EmitMovRegImm(ARM64Register.X1, '0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '0'
    _condBranchFixups.Add((_code.Count, $"{lbl}_parse_width"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE parse_width
    // Check if next char is '.', meaning '0' is not a fill
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP fmt_len, 1
    _condBranchFixups.Add((_code.Count, $"{lbl}_parse_width"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ls)); // B.LS parse_width
    EmitWord(0x39400400 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5, #1]
    EmitMovRegImm(ARM64Register.X1, '.');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '.'
    _condBranchFixups.Add((_code.Count, $"{lbl}_parse_width"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ parse_width (not a fill)
    // It's a '0' fill
    EmitMovRegImm(ARM64Register.X0, '0');
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 96, 8); // fill = '0'
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true); // advance fmtPtr
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true); // advance parse_index

    // Parse width digits
    DefineLabel($"{lbl}_parse_width");
    EmitMovRegImm(ARM64Register.X8, 0); // accumulated width

    DefineLabel($"{lbl}_width_loop");
    EmitWord(0xEB00001F | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X6) << 5)); // CMP parse_index, fmt_len
    _condBranchFixups.Add((_code.Count, $"{lbl}_width_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE width_done
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5]
    EmitMovRegImm(ARM64Register.X1, '.');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '.'
    _condBranchFixups.Add((_code.Count, $"{lbl}_dot"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ dot
    EmitMovRegImm(ARM64Register.X1, '0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '0'
    _condBranchFixups.Add((_code.Count, $"{lbl}_width_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT width_done
    EmitMovRegImm(ARM64Register.X1, '9');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP X0, '9'
    _condBranchFixups.Add((_code.Count, $"{lbl}_width_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Gt)); // B.GT width_done
    // digit: width = width * 10 + (ch - '0')
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, '0', isAdd: false); // X0 = digit
    EmitMovRegImm(ARM64Register.X1, 10);
    // MADD X8, X8, X1, X0 = X8*10 + X0
    EmitWord(0x9B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X8) << 5) | Reg(ARM64Register.X8)); // MADD X8, X8, X1, X0
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitBranch($"{lbl}_width_loop");

    // Found '.', save width, parse precision
    DefineLabel($"{lbl}_dot");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X8, ARM64Register.X29, 56, 8); // save width
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true); // skip '.'
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitMovRegImm(ARM64Register.X8, 0); // accumulated precision

    DefineLabel($"{lbl}_prec_loop");
    EmitWord(0xEB00001F | (Reg(ARM64Register.X7) << 16) | (Reg(ARM64Register.X6) << 5)); // CMP parse_index, fmt_len
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE prec_done
    EmitWord(0x39400000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X0)); // LDRB W0, [X5]
    EmitMovRegImm(ARM64Register.X1, '0');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt));
    EmitMovRegImm(ARM64Register.X1, '9');
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5));
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_done"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Gt));
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, '0', isAdd: false);
    EmitMovRegImm(ARM64Register.X1, 10);
    EmitWord(0x9B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X8) << 5) | Reg(ARM64Register.X8)); // MADD X8, X8, X1, X0
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: true);
    EmitBranch($"{lbl}_prec_loop");

    DefineLabel($"{lbl}_prec_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X8, ARM64Register.X29, 64, 8); // save precision
    EmitBranch($"{lbl}_convert");

    DefineLabel($"{lbl}_width_done");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X8, ARM64Register.X29, 56, 8); // save width

    // ---- Convert ----
    DefineLabel($"{lbl}_convert");
    // If precision == -1 and width == 0, use default
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8); // precision
    EmitMovRegImm(ARM64Register.X1, -1);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP precision, -1
    _condBranchFixups.Add((_code.Count, $"{lbl}_has_precision"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE has_precision
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // width
    _condBranchFixups.Add((_code.Count, $"{lbl}_has_width_no_prec"));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, has_width_no_prec

    // Default: delegate to maxon_f64_to_string
    DefineLabel($"{lbl}_default");
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitReloadArg(0); // X0 = buffer
    // maxon_f64_to_string(D0=value, X0=buffer) -> X0=length
    EmitBranchLink("maxon_f64_to_string");
    EmitBranch($"{lbl}_epilogue");

    // Has width but no precision: default format then pad
    DefineLabel($"{lbl}_has_width_no_prec");
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitReloadArg(0);
    EmitBranchLink("maxon_f64_to_string");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // save content_len
    EmitBranch($"{lbl}_apply_width");

    // Has precision
    DefineLabel($"{lbl}_has_precision");
    // Clamp precision to 20
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    EmitMovRegImm(ARM64Register.X1, 20);
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP precision, 20
    _condBranchFixups.Add((_code.Count, $"{lbl}_prec_ok"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE prec_ok
    EmitMovRegImm(ARM64Register.X0, 20);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    DefineLabel($"{lbl}_prec_ok");

    // Handle sign
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // is_negative = 0

    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitWord(0x1E602008); // FCMP D0, #0.0
    _condBranchFixups.Add((_code.Count, $"{lbl}_positive"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE positive

    // Negative: negate
    EmitWord(0x1E614000); // FNEG D0, D0
    EmitWord(0xFD000000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // STR D0, [X29, #40]
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // is_negative = 1

    DefineLabel($"{lbl}_positive");
    // Extract integer part
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40]
    EmitWord(0x9E780000); // FCVTZS X0, D0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 72, 8); // integer_part

    // Call maxon_i64_to_string(integer_value, buffer + is_negative)
    EmitReloadArg(0); // X0 = buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 48, 8); // is_negative
    EmitWord(0x8B000000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // X1 = buf + is_negative
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 72, 8); // X0 = integer_part
    EmitBranchLink("maxon_i64_to_string");
    // X0 = int string length
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8); // save int_str_length

    // If negative, write '-' and add 1 to length
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8);
    _condBranchFixups.Add((_code.Count, $"{lbl}_no_sign"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1)); // CBZ X1, no_sign
    EmitReloadArg(0);
    EmitMovRegImm(ARM64Register.X2, '-');
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB '-', [buffer]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 80, 8);

    DefineLabel($"{lbl}_no_sign");
    // If precision == 0, no decimal point
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 64, 8);
    _condBranchFixups.Add((_code.Count, $"{lbl}_has_frac"));
    EmitWord(0xB5000000 | Reg(ARM64Register.X0)); // CBNZ X0, has_frac
    // content_len = int_str_length, null-terminate
    EmitReloadArg(0); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 80, 8); // int_str_length
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // X0 = buf + len
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB 0, [X0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // content_len
    EmitBranch($"{lbl}_apply_width");

    DefineLabel($"{lbl}_has_frac");
    // Write '.' after integer part
    EmitReloadArg(0); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 80, 8); // int_str_length
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // X3 = buf + int_str_length
    EmitMovRegImm(ARM64Register.X2, '.');
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X2)); // STRB '.', [X3]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true); // X3 = write pos after '.'
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X3, ARM64Register.X29, 112, 8); // save write_pos

    // Compute fractional part: (|value| - int_part) * 10^precision + 0.5, truncate
    EmitWord(0xFD400000 | ((40u / 8) << 10) | (Reg(ARM64Register.X29) << 5) | 0u); // LDR D0, [X29, #40] = |value|
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 72, 8); // integer_part
    EmitWord(0x9E620001); // SCVTF D1, X0
    EmitWord(0x1E613800); // FSUB D0, D0, D1 (fractional part)

    // Multiply by 10^precision: loop
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(10.0));
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0 (D1 = 10.0)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 64, 8); // precision

    DefineLabel($"{lbl}_mul_loop");
    _condBranchFixups.Add((_code.Count, $"{lbl}_mul_done"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X4)); // CBZ X4, mul_done
    EmitWord(0x1E610800); // FMUL D0, D0, D1
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: false);
    EmitBranch($"{lbl}_mul_loop");

    DefineLabel($"{lbl}_mul_done");
    // Add 0.5 for rounding
    EmitMovRegImm(ARM64Register.X0, BitConverter.DoubleToInt64Bits(0.5));
    EmitWord(0x9E670000 | (Reg(ARM64Register.X0) << 5) | 1u); // FMOV D1, X0
    EmitWord(0x1E612800); // FADD D0, D0, D1
    EmitWord(0x9E780000); // FCVTZS X0, D0 = frac_digits

    // Write precision digits right-to-left at write_pos
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 88, 8); // save frac_digits
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 112, 8); // write_pos
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 64, 8); // precision (loop counter)

    DefineLabel($"{lbl}_frac_write");
    _condBranchFixups.Add((_code.Count, $"{lbl}_frac_written"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X4)); // CBZ X4, frac_written
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X4, 1, isAdd: false); // counter--
    // digit = frac % 10, frac /= 10
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 88, 8); // frac
    EmitMovRegImm(ARM64Register.X1, 10);
    // UDIV X2, X0, X1
    EmitWord(0x9AC00800 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2));
    // MSUB X5, X2, X1, X0 (remainder = X0 - X2*X1)
    EmitWord(0x9B008000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 10) | (Reg(ARM64Register.X2) << 5) | Reg(ARM64Register.X5));
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, '0', isAdd: true); // digit char
    // Store at write_pos[counter] (X4 is already decremented)
    EmitWord(0x38206800 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X5)); // STRB W5, [X3, X4]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X29, 88, 8); // frac = quotient
    EmitBranch($"{lbl}_frac_write");

    DefineLabel($"{lbl}_frac_written");
    // Null-terminate at write_pos + precision
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 112, 8); // write_pos
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 64, 8); // precision
    EmitWord(0x8B000000 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X0)); // X0 = write_pos + precision
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB 0, [X0]

    // content_len = int_str_length + 1 (dot) + precision
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 80, 8); // int_str_length
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true); // + dot
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 64, 8); // precision
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // X0 = int_len + 1 + prec
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // content_len

    // ---- Apply width padding ----
    DefineLabel($"{lbl}_apply_width");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // width
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 104, 8); // content_len
    EmitWord(0xEB00001F | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5)); // CMP width, content_len
    _condBranchFixups.Add((_code.Count, $"{lbl}_no_pad"));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le)); // B.LE no_pad (width <= content_len)

    // pad_count = width - content_len
    // Shift content right by pad_count, then fill left with fill_char
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 104, 8); // content_len
    // Memmove right: copy from end backwards
    EmitReloadArg(0); // X0 = buffer
    // src_end = buffer + content_len - 1
    EmitWord(0x8B000000 | (Reg(ARM64Register.X2) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X3)); // X3 = buf + content_len
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: false); // X3 = src_end
    // dst_end = buffer + width - 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 56, 8); // width
    EmitWord(0x8B000000 | (Reg(ARM64Register.X4) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X5)); // X5 = buf + width
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: false); // X5 = dst_end
    // count = content_len
    EmitMovRegReg(ARM64Register.X6, ARM64Register.X2);

    DefineLabel($"{lbl}_shift_loop");
    _condBranchFixups.Add((_code.Count, $"{lbl}_shift_done"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X6)); // CBZ X6, shift_done
    EmitWord(0x39400000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X3]
    EmitWord(0x39000000 | (Reg(ARM64Register.X5) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X5]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: false);
    EmitAddSubImm(ARM64Register.X5, ARM64Register.X5, 1, isAdd: false);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch($"{lbl}_shift_loop");

    DefineLabel($"{lbl}_shift_done");
    // Fill padding with fill_char
    EmitReloadArg(0); // X0 = buffer
    EmitMovRegReg(ARM64Register.X3, ARM64Register.X0); // X3 = fill ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, 56, 8); // width
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X5, ARM64Register.X29, 104, 8); // content_len
    EmitWord(0xCB000000 | (Reg(ARM64Register.X5) << 16) | (Reg(ARM64Register.X4) << 5) | Reg(ARM64Register.X6)); // X6 = width - content_len
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X7, ARM64Register.X29, 96, 8); // fill_char

    DefineLabel($"{lbl}_fill_loop");
    _condBranchFixups.Add((_code.Count, $"{lbl}_fill_done"));
    EmitWord(0xB4000000 | Reg(ARM64Register.X6)); // CBZ X6, fill_done
    EmitWord(0x39000000 | (Reg(ARM64Register.X3) << 5) | Reg(ARM64Register.X7)); // STRB W7, [X3]
    EmitAddSubImm(ARM64Register.X3, ARM64Register.X3, 1, isAdd: true);
    EmitAddSubImm(ARM64Register.X6, ARM64Register.X6, 1, isAdd: false);
    EmitBranch($"{lbl}_fill_loop");

    DefineLabel($"{lbl}_fill_done");
    // Null-terminate at buffer + width
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 56, 8); // width
    EmitWord(0x8B000000 | (Reg(ARM64Register.X1) << 16) | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X0)); // X0 = buf + width
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitWord(0x39000000 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X2)); // STRB 0
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8); // return width
    EmitBranch($"{lbl}_epilogue");

    DefineLabel($"{lbl}_no_pad");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 104, 8); // return content_len

    DefineLabel($"{lbl}_epilogue");
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
    EmitMovRegImm(ARM64Register.X3, 0); // no scope
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
  // If capacity != 0, buffer is already writable — return it as-is.
  // If capacity == 0, allocate byteLen bytes via mm_raw_alloc, copy from old buffer, return new buffer.
  // The old rdata buffer is NOT freed (capacity==0 identifies rdata/stack).
  private void EmitMaxonCowCheck() {
    // Args: X0=buffer, X1=capacity, X2=byteLen, X3=managedPtr
    // [x29+16]=buffer, [x29+24]=capacity, [x29+32]=byteLen, [x29+40]=managedPtr
    // [x29+48]=new_buffer (scratch), [x29+56]=scope=NULL (trace only)
    EmitRuntimeFunctionStart("maxon_cow_check", 4, Compiler.MmTrace ? 0x50 : 0x40);

    // Check capacity != 0 → already writable
    EmitReloadArg(1); // X1 = capacity
    // CBNZ X1, rt_cow_writable
    _condBranchFixups.Add((_code.Count, "rt_cow_writable"));
    EmitWord(0xB5000001); // CBNZ X1, <fixup>

    // Check byteLen == 0 → nothing to copy, skip COW
    EmitReloadArg(2); // X2 = byteLen
    // CBZ X2, rt_cow_writable
    _condBranchFixups.Add((_code.Count, "rt_cow_writable"));
    EmitWord(0xB4000002); // CBZ X2, <fixup>

    // COW path: allocate byteLen bytes, copy old buffer
    EmitReloadArg(2); // X2 = byteLen
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X2); // X0 = byteLen (arg for mm_raw_alloc)
    EmitCallMmRawAlloc();
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
    EmitMovRegImm(ARM64Register.X0, 0); // return 0 on error (match X86 behavior)

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1);
    EmitReloadArg(0); // X0 = handle (fd)
    var doneLabel = $"__fclose_noop_{_uniqueLabelCounter++}";
    // Skip if handle is 0
    EmitWord(0xF100001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ done
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

  // __destruct___ManagedFile(user_ptr)
  private void EmitFileDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedFile", 1, 0x30);
    EmitReloadArg(0); // X0 = user_ptr

    var doneLabel = $"__dtor_file_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // Load fd from [user_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    // Zero the fd for idempotency
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8);

    // If fd <= 0, skip close
    EmitWord(0xF100003F | (Reg(ARM64Register.X1) << 5)); // CMP X1, #0
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Le));

    // close(fd)
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitCallImport("close");

    DefineLabel(doneLabel);
    EmitRuntimeFunctionEnd();
  }

  // --- Command line functions ---

  // maxon_command_line_count() -> argc (including argv[0])
  private void EmitMaxonCommandLineCount() {
    DefineLabel("maxon_command_line_count");
    // Frameless leaf: load argc from global and return
    EmitAdrpAddFixup(ARM64Register.X0, _globalAdrpFixups, "__argc_global");
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
    EmitAdrpAddFixup(ARM64Register.X1, _globalAdrpFixups, "__argc_global");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X1, 0, 8); // X1 = argc
    // CMP X0, X1 — if index >= argc, return empty
    EmitWord(0xEB01001F | (Reg(ARM64Register.X0) << 5)); // CMP X0, X1
    _condBranchFixups.Add((_code.Count, emptyLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ge)); // B.GE empty

    // Load argv[index]: argv base + index*8
    EmitAdrpAddFixup(ARM64Register.X1, _globalAdrpFixups, "__argv_global");
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

    // Allocate buffer via mm_alloc (so mm_free can reclaim it properly)
    // mm_alloc(size, destructor=0, tag=0, scope=0)
    EmitMovRegImm(ARM64Register.X1, 0); // destructor = 0
    EmitMovRegImm(ARM64Register.X2, 0); // tag = 0
    EmitMovRegImm(ARM64Register.X3, 0); // scope = 0
    EmitBranchLink("mm_alloc"); // X0 = managed buffer
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
    // Allocate 1-byte empty string via mm_alloc
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitMovRegImm(ARM64Register.X1, 0); // destructor = 0
    EmitMovRegImm(ARM64Register.X2, 0); // tag = 0
    EmitMovRegImm(ARM64Register.X3, 0); // scope = 0
    EmitBranchLink("mm_alloc");
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitWord(0x39000001 | (Reg(ARM64Register.X0) << 5) | Reg(ARM64Register.X1)); // STRB W1, [X0, #0]

    DefineLabel(doneLabel);
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

  // --- maxon_managed_dir_open_search(pattern_cstring) -> block_ptr or 0 ---
  // On macOS: strips trailing "/*" or "\*" from pattern, opens directory with open(),
  // does initial getdirentries64 read, skips "." and "..".
  private void EmitMaxonManagedDirOpenSearch() {
    EmitRuntimeFunctionStart("maxon_managed_dir_open_search", 1, 0x40);
    EmitReloadArg(0); // X0 = pattern cstring

    // Save pattern pointer
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

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
    // Save new buffer at [x29+24] (replaces original pattern pointer)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);
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
    EmitBranchLink("mm_alloc");

    // Store block_ptr at [dir_ptr + 0]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // block_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);

    // Save dir_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Do initial read: call maxon_find_next_file(block_ptr) to populate first entry
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // block_ptr
    EmitBranchLink("maxon_find_next_file");

    // Restore and return dir_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Return dir_ptr
    _branchFixups.Add((_code.Count, $"__dir_open_done_{_uniqueLabelCounter}"));
    EmitWord(0x14000000);

    DefineLabel(openFailLabel);
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

    // Skip "." and ".." entries
    // Load first byte
    EmitWord(0x39400000 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6]
    EmitMovRegImm(ARM64Register.X8, 0x2E); // '.'
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP byte, '.'
    _condBranchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE found (not a dot entry)

    // First char is '.', check second char
    EmitWord(0x39400400 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6, #1]
    // If second char is 0, it's "." → skip
    EmitWord(0xF100001F | (Reg(ARM64Register.X7) << 5)); // CMP byte2, #0
    _condBranchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ retry

    // If second char is '.', check third
    EmitWord(0xEB00001F | (Reg(ARM64Register.X8) << 16) | (Reg(ARM64Register.X7) << 5)); // CMP byte2, '.'
    _condBranchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Ne)); // B.NE found (not "..")

    // Second is '.', check third char
    EmitWord(0x39400800 | (Reg(ARM64Register.X6) << 5) | Reg(ARM64Register.X7)); // LDRB W7, [X6, #2]
    EmitWord(0xF100001F | (Reg(ARM64Register.X7) << 5)); // CMP byte3, #0
    _condBranchFixups.Add((_code.Count, retryLabel));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Eq)); // B.EQ retry (it's "..")

    // It's a name starting with ".." but not ".." itself → it's a valid entry
    _branchFixups.Add((_code.Count, foundLabel));
    EmitWord(0x14000000);

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

    // Check for error (carry set) or EOF (X0 = 0)
    EmitBranchOnLibcError(eofLabel);
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

  // GreenThread struct offsets (same layout as x86 for consistency)
  private const int GtOffSp = 0x00;
  private const int GtOffFp = 0x08;
  private const int GtOffStatus = 0x10;
  private const int GtOffStackBase = 0x18;
  private const int GtOffStackSize = 0x20;
  private const int GtOffResult = 0x28;
  private const int GtOffWaiter = 0x30;
  private const int GtOffNext = 0x38;
  private const int GtOffFuncPtr = 0x40;
  private const int GtOffArgBuf = 0x48;
  private const int GtOffStackGuard = 0x50;
  private const int GtOffThrew = 0x58;
  private const int GtOffCancelFlag = 0x78;
  private const int GtOffAllNext = 0x88;
  private const int GtOffTraceId = 0xA0;
  private const int GtOffIoYielded = 0xA8; // ioYielded flag for I/O completion protocol
  private const int GtStructSize = 0xB0; // 176 bytes

  private const int GtStatusReady = 0;
  private const int GtStatusRunning = 1;
  private const int GtStatusCompleted = 2;
  private const int GtStatusWaiting = 3;

  private const int GtInitialStackSize = 0x800; // 2KB (grows on demand via __gt_morestack)
  private const int GtStackGuardMargin = 0x3A0; // 928 bytes
  private const int PSystemStackSize = 0x2000; // 8KB system stack per P for morestack

  // I/O result fields in GreenThread struct (used by async I/O)
  private const int GtOffIoResultVal = 0x60; // raw result value
  private const int GtOffIoErrorCode = 0x70; // 0=success, non-zero=error

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

  // P (ProcContext) struct offsets — per-worker scheduler state (GMP model)
  private const int POffCurrentGt = 0x18;
  private const int POffId = 0x20;
  private const int POffRng = 0x28;
  private const int POffIdleFlag = 0x30;
  private const int POffWakeSemaphore = 0x38;
  private const int POffStatus = 0x48;  // 0=unstarted, 1=active (atomic CAS)
  private const int POffPendingWaiter = 0x50;
  private const int POffFreeListHead = 0x60;
  private const int POffFreeListLen = 0x68;
  private const int POffSystemStackSP = 0x70; // system stack pointer for __gt_morestack
  private const int POffMainThread = 0x78;  // Inline GreenThread (168 bytes)
  private const int PStructSize = 0x78 + GtStructSize; // 296 bytes (0x128)

  // Free list max size before freeing to heap
  private const int MaxFreeListLen = 64;

  private void EmitGreenThreadRuntime() {
    // GMP scheduler globals
    DefineGlobal("__sched_procs", 8, 0);           // P*[] array pointer
    DefineGlobal("__sched_num_procs", 8, 0);       // number of P structs allocated
    DefineGlobal("__sched_max_procs", 8, 0);       // max worker threads (CPU count)
    DefineGlobal("__sched_active_workers", 8, 0);   // atomic count of running workers
    DefineGlobal("__sched_shutdown_flag", 8, 0);     // 1 = shutdown requested
    DefineGlobal("__sched_tls_key", 8, 0);           // pthread_key_t for P*
    DefineGlobal("__sched_global_lock", 8, 0);       // os_unfair_lock for global queue
    DefineGlobal("__sched_all_lock", 8, 0);          // os_unfair_lock for all-threads list
    DefineGlobal("__sched_timer_lock", 8, 0);        // os_unfair_lock for timer heap
    DefineGlobal("__sched_io_lock", 8, 0);           // os_unfair_lock for I/O request queue

    // Global run queue (shared across workers, protected by __sched_global_lock)
    DefineGlobal("__gt_run_queue_head", 8, 0);
    DefineGlobal("__gt_run_queue_tail", 8, 0);

    // Thread tracking
    DefineGlobal("__gt_live_count", 8, 0);           // atomic count of non-completed GTs
    DefineGlobal("__gt_all_head", 8, 0);             // all-threads list (protected by __sched_all_lock)

    // Sync I/O request queue (protected by __sched_io_lock)
    DefineGlobal("__io_sync_req_head", 8, 0);
    DefineGlobal("__io_sync_req_tail", 8, 0);
    DefineGlobal("__io_sync_req_semaphore", 8, 0);   // dispatch_semaphore_t to wake I/O worker

    // I/O completion queue (posted by sync worker, drained by scheduler)
    DefineGlobal("__io_done_head", 8, 0);
    DefineGlobal("__io_done_tail", 8, 0);
    DefineGlobal("__io_done_lock", 8, 0);            // os_unfair_lock for done queue

    // Timer heap globals (protected by __sched_timer_lock)
    DefineGlobal("__gt_timer_heap", TimerHeapCapacity * TimerEntrySize, 0); // 256 entries * 16 bytes
    DefineGlobal("__gt_timer_count", 8, 0);

    // kqueue globals (kqueue is thread-safe on macOS)
    DefineGlobal("__io_kqueue_fd", 8, 0);
    DefineGlobal("__io_kevent_buf", 32 * 32, 0); // 32 kevent structs * 32 bytes each

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
    // Scheduler functions migrated to RuntimeEmitter (shared x86/ARM64)
    var schedRt = new Runtime.RuntimeEmitter(CreateBackend());
    schedRt.EmitGtEnqueue();
    schedRt.EmitGtDequeue();
    schedRt.EmitGtStealWork();
    EmitSchedWorkerLoop();
    EmitGtSpawn();
    EmitGtTrampoline();
    EmitGtContextSwitch();
    EmitGtAwait();
    EmitGtTryAwait();
    EmitGtYield();
    EmitGtCancel();
    EmitGtCleanup();
    EmitGtProcessPendingWaiter();
    schedRt.EmitGtTimerAdd();
    schedRt.EmitGtTimerCheck();
    EmitGtMorestack();
    EmitGtPanicIo();
    EmitIoRuntime();
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
    EmitMovRegImm(ARM64Register.X0, 58); // _SC_NPROCESSORS_ONLN = 58 on macOS
    EmitCallImport("sysconf");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // [x29+32] = max_procs
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_max_procs");
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_num_procs");

    // Step 3: Allocate P*[] array — mm_raw_alloc(max_procs * 8)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // LSL X0, X0, #3  (multiply by 8)
    EmitWord(0xD37DF000);
    EmitCallMmRawAlloc();
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

    // Allocate P[i] struct
    EmitMovRegImm(ARM64Register.X0, PStructSize);
    EmitCallMmRawAlloc();
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

    // Create wake semaphore: dispatch_semaphore_create(0)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitCallImport("dispatch_semaphore_create");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X9, ARM64Register.X29, 48, 8); // P[i]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffWakeSemaphore, 8);

    // Allocate system stack for morestack: mmap(NULL, 8KB, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitMovRegImm(ARM64Register.X1, PSystemStackSize);
    EmitMovRegImm(ARM64Register.X2, 3);       // PROT_READ|PROT_WRITE
    EmitMovRegImm(ARM64Register.X3, 0x1002);  // MAP_ANON|MAP_PRIVATE
    EmitMovRegImm(ARM64Register.X4, -1);
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitCallImport("mmap");
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
    EmitMovRegImm(ARM64Register.X1, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, POffStatus, 8);

    // Set TLS: pthread_setspecific(__sched_tls_key, P[0])
    EmitGlobalLoadReg(ARM64Register.X0, "__sched_tls_key");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // P[0]
    EmitCallImport("pthread_setspecific");

    // Set active_workers = 1
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitGlobalStoreReg(ARM64Register.X0, "__sched_active_workers");

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

    // Step 7: Create I/O sync request semaphore
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitCallImport("dispatch_semaphore_create");
    EmitGlobalStoreReg(ARM64Register.X0, "__io_sync_req_semaphore");

    // Step 8: Spawn the I/O sync worker thread
    // pthread_create(&tid, NULL, __io_sync_worker_loop, NULL)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true); // X0 = &tid (unused, on stack)
    EmitMovRegImm(ARM64Register.X1, 0); // attr = NULL
    EmitAdrpAddFixup(ARM64Register.X2, _funcAddrAdrpFixups, "__io_sync_worker_loop"); // function pointer
    EmitMovRegImm(ARM64Register.X3, 0); // arg = NULL
    EmitCallImport("pthread_create");

    // Step 9: Initialize slab allocator
    EmitBranchLink("__slab_init");

    EmitRuntimeFunctionEnd();
  }

  // __gt_enqueue, __gt_dequeue, and __gt_steal_work are now emitted by RuntimeEmitter.Scheduler.cs

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

    // Got a GT — save it, set status = Running, context-switch to it
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
    EmitLoadP(ARM64Register.X9);
    // P->idleFlag = 1
    EmitMovRegImm(ARM64Register.X0, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffIdleFlag, 8);
    // dispatch_semaphore_wait(P->wakeSemaphore, timeout)
    // timeout = dispatch_time(DISPATCH_TIME_NOW, 100ms * NSEC_PER_MSEC)
    EmitMovRegImm(ARM64Register.X0, 0); // DISPATCH_TIME_NOW = 0
    EmitMovRegImm(ARM64Register.X1, 100_000_000); // 100ms in nanoseconds
    EmitCallImport("dispatch_time");
    // X0 = timeout value; now call dispatch_semaphore_wait
    EmitMovRegReg(ARM64Register.X1, ARM64Register.X0); // X1 = timeout
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, POffWakeSemaphore, 8);
    EmitCallImport("dispatch_semaphore_wait");
    // Clear idle flag
    EmitLoadP(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, POffIdleFlag, 8);
    EmitBranch("__sched_worker_loop_top");

    // --- Exit ---
    DefineLabel("__sched_worker_loop_exit");
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

    // Allocate stack via mmap(NULL, 64KB, PROT_READ|PROT_WRITE, MAP_ANON|MAP_PRIVATE, -1, 0)
    EmitMovRegImm(ARM64Register.X0, 0);                    // addr = NULL
    EmitMovRegImm(ARM64Register.X1, GtInitialStackSize);    // length = 64KB
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

    // Add to all-threads list: gt.all_next = __gt_all_head; __gt_all_head = gt
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_all_head");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffAllNext, 8);
    EmitGlobalStoreReg(ARM64Register.X9, "__gt_all_head");

    // Increment live thread count
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitGlobalStoreReg(ARM64Register.X0, "__gt_live_count");

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
    var frameSize = 0x70; // 112 bytes
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

    // Load args from buffer into AAPCS64 calling convention registers (X0-X7)
    // Args are at [arg_buf + 8 + i*8]
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X11, ARM64Register.X29, 40, 8); // X11 = arg_count
    for (int i = 0; i < 8; i++) {
      var skipLabel = $"__gt_tramp_skip_arg{i}";
      // if arg_count <= i, skip
      EmitCmpImm(ARM64Register.X11, i + 1);
      EmitBranchCond(ARM64ConditionCode.Lt, skipLabel); // skip if arg_count < i+1
      // Load arg[i] from [arg_buf + 8 + i*8]
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 32, 8); // X10 = arg_buf
      EmitLoadStoreUnsignedImm(0xF9400000, AbiArgRegs[i], ARM64Register.X10, 8 + i * 8, 8);
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

    // Store result (X0) and threw flag (X1) to gt struct
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X29, 56, 8); // save threw
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffResult, 8);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 56, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffThrew, 8);

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

    // Reload current gt
    EmitLoadCurrentGt(ARM64Register.X9);

    // Set status = completed
    EmitMovRegImm(ARM64Register.X0, GtStatusCompleted);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // Wake waiter if any
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X9, GtOffWaiter, 8);
    EmitCbz(ARM64Register.X10, "__gt_tramp_no_waiter");
    // waiter.status = ready
    EmitMovRegImm(ARM64Register.X0, GtStatusReady);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffStatus, 8);
    // enqueue waiter
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10);
    EmitBranchLink("__gt_enqueue");
    DefineLabel("__gt_tramp_no_waiter");

    // Yield to next thread (never returns for completed threads)
    EmitBranchLink("__gt_yield_completed");
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
    // safe for __io_complete_gt (on another thread) to enqueue this GT.
    EmitMovRegImm(ARM64Register.X9, 1);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffIoYielded, 8);

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

    // promise.waiter = current
    EmitReloadArg(0); // X0 = promise
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffWaiter, 8);

    // Dequeue next runnable thread
    DefineLabel("__gt_await_spin");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__gt_await_has_next");
    // No runnable thread — process pending I/O and timers, then retry
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
    EmitBranch("__gt_await_spin");

    DefineLabel("__gt_await_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save next

    // Set next.status = running
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Context switch: from=current, to=next
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // to = next
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

    // We resume here after being woken up
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

    // Not yet completed: block current thread
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // promise.waiter = current
    EmitReloadArg(0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X0, GtOffWaiter, 8);

    // Dequeue next runnable thread
    DefineLabel("__gt_try_await_spin");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__gt_try_await_has_next");
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
    EmitBranch("__gt_try_await_spin");

    DefineLabel("__gt_try_await_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // save next

    // Set next.status = running
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Context switch
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

    // Resume here
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

    // Live threads exist but nobody runnable — process pending I/O, timers, brief park, then retry
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__gt_timer_check");
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
    // Signal I/O worker semaphore so it checks shutdown and exits
    EmitGlobalLoadReg(ARM64Register.X0, "__io_sync_req_semaphore");
    EmitCallImport("dispatch_semaphore_signal");

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
    EmitBranch("__gt_cleanup_drain");

    // Run queue empty — check if any threads still alive
    DefineLabel("__gt_cleanup_check_live");
    EmitGlobalLoadReg(ARM64Register.X0, "__gt_live_count");
    EmitCbz(ARM64Register.X0, "__gt_cleanup_done");
    // Threads still alive but nothing runnable — process pending I/O and timers
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
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
    // Wake all worker threads so they exit (shutdown flag is set)
    EmitGlobalLoadReg(ARM64Register.X10, "__sched_procs");
    EmitGlobalLoadReg(ARM64Register.X11, "__sched_num_procs");
    EmitMovRegImm(ARM64Register.X12, 1);
    DefineLabel("__gt_cleanup_wake_loop");
    // CMP X12, X11
    EmitWord(0xEB0B019F);
    EmitBranchCond(ARM64ConditionCode.Hs, "__gt_cleanup_ret");
    // LDR X13, [X10, X12, LSL #3]
    EmitWord(0xF86C794D);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X13, POffWakeSemaphore, 8);
    EmitCbz(ARM64Register.X0, "__gt_cleanup_wake_next");
    EmitCallImport("dispatch_semaphore_signal");
    DefineLabel("__gt_cleanup_wake_next");
    EmitAddSubImm(ARM64Register.X12, ARM64Register.X12, 1, isAdd: true);
    EmitBranch("__gt_cleanup_wake_loop");
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
    EmitIoCompleteGt();
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
    //   [x29+16] = req ptr
    //   [x29+24] = waiter GT ptr
    //   [x29+32] = op code
    //   [x29+40] = result value / temp
    //   [x29+48 .. x29+191] = stat64 buffer (144 bytes) / sockaddr_in for net_connect
    //   [x29+64] = net_connect: socket fd
    //   [x29+72] = net_connect: resolved IP
    //   [x29+80] = net_connect: port
    //   [x29+88] = net_send/recv: args struct ptr
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

    // Dispatch on op code
    EmitCmpImm(ARM64Register.X2, SyncOpFileExists);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_exists");
    EmitCmpImm(ARM64Register.X2, SyncOpFileDelete);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_op_file_delete");
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
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitBranch("__io_op_done");

    // --- SyncOpGetCwd: alloc buf, open(".") + fcntl(F_GETPATH, buf), close ---
    DefineLabel("__io_op_get_cwd");
    // Allocate 1024-byte path buffer via mm_alloc (stdlib frees with mm_free)
    EmitMovRegImm(ARM64Register.X0, 1024);
    EmitMovRegImm(ARM64Register.X1, 0); // no destructor
    EmitMovRegImm(ARM64Register.X2, 0); // no tag
    EmitBranchLink("mm_alloc");
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
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoErrorCode, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X9);
    EmitBranchLink("__gt_enqueue");

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

    // Wait for a request to be enqueued (blocks until signaled)
    EmitGlobalLoadReg(ARM64Register.X0, "__io_sync_req_semaphore");
    EmitMovRegImm(ARM64Register.X1, -1); // DISPATCH_TIME_FOREVER = ~0ULL
    EmitCallImport("dispatch_semaphore_wait");

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
    // The sync worker may complete and call __io_complete_gt immediately after signal;
    // ioYielded must be 0 at that point so the spin-wait blocks until the context switch
    // saves our registers. __gt_context_switch sets ioYielded=1 after the save.
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffIoYielded, 8);

    // Enqueue the request
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // req
    EmitBranchLink("__io_enqueue_sync_req");

    // Signal the I/O worker thread that a new request is available
    EmitGlobalLoadReg(ARM64Register.X0, "__io_sync_req_semaphore");
    EmitCallImport("dispatch_semaphore_signal");

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

    // Yield: try to dequeue next runnable thread
    DefineLabel("__io_submit_sync_try_dequeue");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "__io_submit_sync_has_next");

    // No runnable thread: process pending I/O and timers inline
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
    EmitBranch("__io_submit_sync_try_dequeue");

    DefineLabel("__io_submit_sync_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save next

    // Set next.status = running
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Context switch: from=current, to=next
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

    // Resume here after being re-enqueued by __io_complete_gt
    DefineLabel("__io_submit_sync_resume");

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

    // kevent(kq, changelist, 1, NULL, 0, NULL)
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 72, isAdd: true);
    EmitMovRegImm(ARM64Register.X2, 1);
    EmitMovRegImm(ARM64Register.X3, 0);
    EmitMovRegImm(ARM64Register.X4, 0);
    EmitMovRegImm(ARM64Register.X5, 0);
    EmitCallImport("kevent");

    // Set GT status = waiting
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // Yield: dequeue next runnable thread
    DefineLabel("rt_ntc_try_dequeue");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, "rt_ntc_has_next");
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
    // Check if our IO completed (main thread: poll_kqueue sets status=ready without enqueue)
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X0, GtStatusWaiting);
    EmitBranchCond(ARM64ConditionCode.Ne, "rt_ntc_resumed");
    EmitBranch("rt_ntc_try_dequeue");

    DefineLabel("rt_ntc_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 104, 8); // save next GT
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 104, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

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
  /// maxon_net_close(socket_handle_x0) → void. Idempotent: does nothing if handle is 0.
  /// Delegates to __io_submit_sync(SyncOpNetClose, handle, 0) to yield and process I/O.
  /// </summary>
  private void EmitNetClose() {
    EmitRuntimeFunctionStart("maxon_net_close", 1, 0x30);
    EmitMovRegImm(ARM64Register.X0, SyncOpNetClose);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 16, 8); // handle
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitBranchLink("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedSocket(user_ptr_x0) → void.
  /// Called by mm_decref when refcount hits 0. Reads _handle at [user_ptr+0],
  /// calls close() if non-zero, then zeros the handle for idempotency.
  /// </summary>
  private void EmitNetSocketDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedSocket", 1, 0x30);

    EmitReloadArg(0);                                                                  // X0 = user_ptr
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 0, 8);   // X1 = _handle
    EmitCbz(ARM64Register.X1, "rt_nsd_done");

    // Zero the handle before closing (idempotency)
    EmitReloadArg(0);                                                                  // X0 = user_ptr
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 0, 8);   // [ptr+0] = 0

    // close(handle) — X1 still has the handle
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitCallImport("close");

    DefineLabel("rt_nsd_done");
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

  // =============================================================================
  // Process management — macOS POSIX implementation
  //
  // Uses posix_spawn with /bin/sh -c to execute commands.
  // Capture variant uses pipe() + posix_spawn_file_actions for stdout/stderr.
  //
  // Capture struct layout (24 bytes, heap-allocated via mm_alloc):
  //   +0x00: pid       (pid_t, stored as 64-bit)
  //   +0x08: stdoutFd  (read end of stdout pipe)
  //   +0x10: stderrFd  (read end of stderr pipe)
  // =============================================================================

  /// <summary>
  /// maxon_process_create(cmd_cstring, cwd_cstring) -> pid (or 0 on failure).
  /// Spawns /bin/sh -c "cmd" using posix_spawn.
  /// Stack layout (0x60):
  ///   [x29+16] = cmd (arg0)
  ///   [x29+24] = cwd (arg1)
  ///   [x29+32] = pid (local)
  ///   [x29+40] = argv[0] ptr to "/bin/sh"
  ///   [x29+48] = argv[1] ptr to "-c"
  ///   [x29+56] = argv[2] = cmd
  ///   [x29+64] = argv[3] = NULL
  ///   [x29+72] = scratch
  /// </summary>
  private void EmitMaxonProcessCreate() {
    DefineSymdata("__rt_str_bin_sh", System.Text.Encoding.UTF8.GetBytes("/bin/sh\0"));
    DefineSymdata("__rt_str_dash_c", System.Text.Encoding.UTF8.GetBytes("-c\0"));
    // Global to store last waitpid status (shared between processWait and processGetExitCode)
    DefineGlobal("__proc_last_status", 8, 0);

    EmitRuntimeFunctionStart("maxon_process_create", 2, 0x60);

    // Store 0 at pid slot [x29+32]
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8);

    // Build argv array on stack: ["/bin/sh", "-c", cmd, NULL]
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__rt_str_bin_sh");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // argv[0]

    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__rt_str_dash_c");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // argv[1]

    EmitReloadArg(0); // X0 = cmd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 56, 8); // argv[2] = cmd

    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 64, 8); // argv[3] = NULL

    // posix_spawn(&pid, "/bin/sh", NULL, NULL, argv, NULL)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 32, isAdd: true); // &pid
    EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__rt_str_bin_sh"); // path
    EmitMovRegImm(ARM64Register.X2, 0); // file_actions = NULL
    EmitMovRegImm(ARM64Register.X3, 0); // attrp = NULL
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X29, 40, isAdd: true); // argv
    EmitMovRegImm(ARM64Register.X5, 0); // envp = NULL
    EmitCallImport("posix_spawn");

    // posix_spawn returns 0 on success
    EmitCbnz(ARM64Register.X0, "__proc_create_fail");

    // Return pid
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    EmitRuntimeFunctionEnd();

    DefineLabel("__proc_create_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_wait(pid, timeoutMs) -> 0=completed, 1=timeout, -1=error.
  /// Uses waitpid with WNOHANG polling for timeout support.
  /// Stack layout (0x40):
  ///   [x29+16] = pid (arg0)
  ///   [x29+24] = timeoutMs (arg1)
  ///   [x29+32] = status (local)
  ///   [x29+40] = elapsed (local)
  /// </summary>
  private void EmitMaxonProcessWait() {
    EmitRuntimeFunctionStart("maxon_process_wait", 2, 0x40);

    // If timeout == 0, use blocking wait (INFINITE)
    EmitReloadArg(1); // X0 = timeoutMs
    EmitCbz(ARM64Register.X0, "__proc_wait_blocking");

    // Polling wait with timeout
    // elapsed = 0
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    DefineLabel("__proc_wait_poll_loop");
    // waitpid(pid, &status, WNOHANG)
    EmitReloadArg(0); // X0 = pid
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 32, isAdd: true); // X1 = &status
    EmitMovRegImm(ARM64Register.X2, 1); // WNOHANG = 1
    EmitCallImport("waitpid");

    // X0 > 0: child exited
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Gt, "__proc_wait_done");

    // X0 < 0: error
    EmitBranchCond(ARM64ConditionCode.Lt, "__proc_wait_error");

    // X0 == 0: not yet done, check timeout
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // elapsed
    EmitReloadArg(1); // X1 = timeoutMs
    // Compare elapsed >= timeout
    EmitWord(0xEB01001F); // CMP X0, X1
    EmitBranchCond(ARM64ConditionCode.Ge, "__proc_wait_timeout");

    // Sleep 1ms using nanosleep({0, 1000000}, NULL)
    // We need to put timespec on stack: [x29+32] is reusable scratch? No, status is there.
    // Use sub-sp approach: push timespec
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 16, isAdd: false);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.Sp, 0, 8); // tv_sec = 0
    EmitMovRegImm(ARM64Register.X0, 1_000_000); // 1ms in nanoseconds
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.Sp, 8, 8); // tv_nsec = 1000000
    EmitMovRegReg(ARM64Register.X0, ARM64Register.Sp); // X0 = &timespec
    EmitMovRegImm(ARM64Register.X1, 0); // X1 = NULL (no remaining)
    EmitCallImport("nanosleep");
    EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, 16, isAdd: true);

    // elapsed += 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8);
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X0, 1, isAdd: true);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    EmitBranch("__proc_wait_poll_loop");

    // Blocking wait (no timeout)
    DefineLabel("__proc_wait_blocking");
    EmitReloadArg(0); // X0 = pid
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 32, isAdd: true); // X1 = &status
    EmitMovRegImm(ARM64Register.X2, 0); // options = 0 (blocking)
    EmitCallImport("waitpid");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Lt, "__proc_wait_error");

    DefineLabel("__proc_wait_done");
    // Store status in global for processGetExitCode to read
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 32, 4); // load status (32-bit)
    EmitGlobalStoreReg(ARM64Register.X0, "__proc_last_status");
    EmitMovRegImm(ARM64Register.X0, 0); // 0 = completed
    EmitRuntimeFunctionEnd();

    DefineLabel("__proc_wait_timeout");
    EmitMovRegImm(ARM64Register.X0, 1); // 1 = timeout
    EmitRuntimeFunctionEnd();

    DefineLabel("__proc_wait_error");
    EmitMovRegImm(ARM64Register.X0, -1); // -1 = error
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_get_exit_code(pid) -> exit code.
  /// Reads the status stored by processWait from __proc_last_status global,
  /// then extracts the exit code using WEXITSTATUS(status) = (status >> 8) & 0xFF.
  /// </summary>
  private void EmitMaxonProcessGetExitCode() {
    DefineLabel("maxon_process_get_exit_code");

    // Load status from global
    EmitGlobalLoadReg(ARM64Register.X0, "__proc_last_status");

    // WEXITSTATUS(status) = (status >> 8) & 0xFF
    // UBFX X0, X0, #8, #8  (extract bits 8..15)
    EmitWord(0xD3483C00 | Reg(ARM64Register.X0)); // UBFX X0, X0, #8, #8

    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// maxon_process_close(pid) -> void.
  /// On POSIX, there's no handle to close for processes. This is a no-op.
  /// </summary>
  private void EmitMaxonProcessClose() {
    DefineLabel("maxon_process_close");
    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// maxon_process_create_with_capture(cmd_cstring, cwd_cstring) -> capture_ptr.
  /// Creates two pipes (stdout, stderr), spawns /bin/sh -c "cmd" with
  /// stdout/stderr redirected to pipes, returns heap struct with pid + read fds.
  ///
  /// Stack layout (0xA0):
  ///   [x29+16]  = cmd (arg0)
  ///   [x29+24]  = cwd (arg1)
  ///   [x29+32]  = stdout_pipe[0] (read end)
  ///   [x29+36]  = stdout_pipe[1] (write end)
  ///   [x29+40]  = stderr_pipe[0] (read end)
  ///   [x29+44]  = stderr_pipe[1] (write end)
  ///   [x29+48]  = pid
  ///   [x29+56]  = file_actions (posix_spawn_file_actions_t, opaque, ~128 bytes on macOS)
  ///   [x29+56..x29+120] = file_actions storage (64 bytes should be enough, it's a pointer internally)
  ///   [x29+120] = capture_ptr (result)
  ///   [x29+128] = argv[0] "/bin/sh"
  ///   [x29+136] = argv[1] "-c"
  ///   [x29+144] = argv[2] = cmd
  ///   [x29+152] = argv[3] = NULL
  /// </summary>
  private void EmitMaxonProcessCreateWithCapture() {
    EmitRuntimeFunctionStart("maxon_process_create_with_capture", 2, 0xA0);

    // Zero out pipe arrays and pid
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // stdout_pipe
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // stderr_pipe
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // pid

    // pipe(stdout_pipe) - creates stdout_pipe[0]=read, [1]=write
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 32, isAdd: true);
    EmitCallImport("pipe");
    EmitCbnz(ARM64Register.X0, "__pcwc_fail");

    // pipe(stderr_pipe)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 40, isAdd: true);
    EmitCallImport("pipe");
    EmitCbnz(ARM64Register.X0, "__pcwc_fail_close_stdout");

    // posix_spawn_file_actions_init(&file_actions)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_init");

    // posix_spawn_file_actions_adddup2(&file_actions, stdout_pipe[1], STDOUT_FILENO=1)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    // Load stdout_pipe[1] (32-bit int at offset 36)
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 36, 4);
    EmitMovRegImm(ARM64Register.X2, 1); // STDOUT_FILENO
    EmitCallImport("posix_spawn_file_actions_adddup2");

    // posix_spawn_file_actions_adddup2(&file_actions, stderr_pipe[1], STDERR_FILENO=2)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 44, 4); // stderr_pipe[1]
    EmitMovRegImm(ARM64Register.X2, 2); // STDERR_FILENO
    EmitCallImport("posix_spawn_file_actions_adddup2");

    // posix_spawn_file_actions_addclose(&file_actions, stdout_pipe[0]) - close read end in child
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 32, 4);
    EmitCallImport("posix_spawn_file_actions_addclose");

    // posix_spawn_file_actions_addclose(&file_actions, stderr_pipe[0])
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 40, 4);
    EmitCallImport("posix_spawn_file_actions_addclose");

    // posix_spawn_file_actions_addclose(&file_actions, stdout_pipe[1]) - close write end in child after dup2
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 36, 4);
    EmitCallImport("posix_spawn_file_actions_addclose");

    // posix_spawn_file_actions_addclose(&file_actions, stderr_pipe[1])
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 44, 4);
    EmitCallImport("posix_spawn_file_actions_addclose");

    // Build argv: ["/bin/sh", "-c", cmd, NULL]
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__rt_str_bin_sh");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 128, 8);
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__rt_str_dash_c");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 136, 8);
    EmitReloadArg(0); // cmd
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 144, 8);
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 152, 8);

    // posix_spawn(&pid, "/bin/sh", &file_actions, NULL, argv, NULL)
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 48, isAdd: true); // &pid
    EmitAdrpAddFixup(ARM64Register.X1, _symdataAdrpFixups, "__rt_str_bin_sh"); // path
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X29, 56, isAdd: true); // &file_actions
    EmitMovRegImm(ARM64Register.X3, 0); // attrp = NULL
    EmitAddSubImm(ARM64Register.X4, ARM64Register.X29, 128, isAdd: true); // argv
    EmitMovRegImm(ARM64Register.X5, 0); // envp = NULL
    EmitCallImport("posix_spawn");

    // posix_spawn_file_actions_destroy(&file_actions)
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 120, 8); // save spawn result
    EmitAddSubImm(ARM64Register.X0, ARM64Register.X29, 56, isAdd: true);
    EmitCallImport("posix_spawn_file_actions_destroy");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 120, 8); // restore spawn result

    // Check posix_spawn result
    EmitCbnz(ARM64Register.X0, "__pcwc_fail_close_all");

    // Close write ends of pipes in parent
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 36, 4); // stdout_pipe[1]
    EmitCallImport("close");
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 44, 4); // stderr_pipe[1]
    EmitCallImport("close");

    // Allocate capture struct (24 bytes) via mm_alloc
    EmitMovRegImm(ARM64Register.X0, 24);
    EmitMovRegImm(ARM64Register.X1, 0); // destructor
    EmitMovRegImm(ARM64Register.X2, 0); // tag
    EmitMovRegImm(ARM64Register.X3, 0); // scope
    EmitBranchLink("mm_alloc");
    // Save capture_ptr
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 120, 8);

    // Fill capture struct
    // [capture+0] = pid
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 0, 8);
    // [capture+8] = stdout_pipe[0] (read fd, zero-extended from 32-bit)
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 32, 4);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 8, 8);
    // [capture+16] = stderr_pipe[0] (read fd)
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X1, ARM64Register.X29, 40, 4);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, 16, 8);

    // Return capture_ptr
    EmitRuntimeFunctionEnd();

    // Error paths
    DefineLabel("__pcwc_fail_close_all");
    // Close all four pipe fds
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 36, 4);
    EmitCallImport("close");
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 44, 4);
    EmitCallImport("close");

    DefineLabel("__pcwc_fail_close_stdout");
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 32, 4);
    EmitCallImport("close");
    EmitLoadStoreUnsignedImm(0xB9400000, ARM64Register.X0, ARM64Register.X29, 40, 4);
    EmitCallImport("close");

    DefineLabel("__pcwc_fail");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_pipe(fd) -> cstring_ptr (heap-allocated, null-terminated).
  /// Reads all data from fd into a growing buffer, then closes fd.
  /// Stack layout (0x50):
  ///   [x29+16] = fd (arg0)
  ///   [x29+24] = buffer ptr
  ///   [x29+32] = capacity
  ///   [x29+40] = total_read
  ///   [x29+48] = bytes_read (scratch)
  /// </summary>
  private void EmitMaxonProcessReadPipe() {
    EmitRuntimeFunctionStart("maxon_process_read_pipe", 1, 0x50);

    // Allocate initial 4KB buffer
    EmitMovRegImm(ARM64Register.X0, 4096);
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitMovRegImm(ARM64Register.X3, 0);
    EmitBranchLink("mm_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8); // buffer
    EmitMovRegImm(ARM64Register.X0, 4096);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // capacity
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // total_read = 0

    DefineLabel("__read_pipe_loop");
    // Check if buffer is full (total_read >= capacity - 1)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // total_read
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 32, 8); // capacity
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X1, 1, isAdd: false); // capacity - 1
    EmitWord(0xEB01001F); // CMP X0, X1
    EmitBranchCond(ARM64ConditionCode.Lt, "__read_pipe_do_read");

    // Grow buffer: capacity *= 2
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 32, 8);
    // LSL X0, X0, #1
    EmitWord(0xD37FF800 | Reg(ARM64Register.X0)); // LSL X0, X0, #1
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 32, 8); // new capacity

    // Allocate new buffer
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitMovRegImm(ARM64Register.X3, 0);
    EmitBranchLink("mm_alloc");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // new_buffer (temp in scratch)

    // memcpy(new_buffer, old_buffer, total_read)
    // X0 = new_buffer (already in X0? no, need to reload)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8); // dst = new_buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // src = old_buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // count = total_read
    EmitBranchLink("maxon_memcpy");

    // Free old buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitBranchLink("mm_free");

    // Update buffer ptr to new buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 48, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 24, 8);

    DefineLabel("__read_pipe_do_read");
    // read(fd, buffer + total_read, capacity - total_read - 1)
    EmitReloadArg(0); // X0 = fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 24, 8); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, 40, 8); // total_read
    // X1 = buffer + total_read
    EmitWord(0x8B020021); // ADD X1, X1, X2
    // X2 = capacity - total_read - 1
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X3, ARM64Register.X29, 32, 8); // capacity
    EmitWord(0xCB020062); // SUB X2, X3, X2 (capacity - total_read)
    EmitAddSubImm(ARM64Register.X2, ARM64Register.X2, 1, isAdd: false); // -1 for null terminator
    EmitCallImport("read");

    // X0 = bytes read. If <= 0, done.
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, "__read_pipe_done");

    // total_read += bytes_read
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 40, 8);
    EmitWord(0x8B000020); // ADD X0, X1, X0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8);

    EmitBranch("__read_pipe_loop");

    DefineLabel("__read_pipe_done");
    // Close fd
    EmitReloadArg(0);
    EmitCallImport("close");

    // Null-terminate buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8); // buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 40, 8); // total_read
    // Store 0 at buffer[total_read]
    EmitWord(0x8B010000); // ADD X0, X0, X1
    EmitMovRegImm(ARM64Register.X1, 0);
    // STRB W1, [X0]
    EmitWord(0x39000001); // STRB W1, [X0, #0]

    // Return buffer
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 24, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_get_handle(capture_ptr) -> pid.
  /// </summary>
  private void EmitMaxonProcessGetHandle() {
    DefineLabel("maxon_process_get_handle");
    // X0 = capture_ptr
    EmitCbz(ARM64Register.X0, "__pgh_null");
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X0, 0, 8); // [capture+0] = pid
    EmitWord(0xD65F03C0); // RET
    DefineLabel("__pgh_null");
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitWord(0xD65F03C0); // RET
  }

  /// <summary>
  /// maxon_process_close_capture(capture_ptr) -> void.
  /// Frees the capture struct via mm_free.
  /// </summary>
  private void EmitMaxonProcessCloseCapture() {
    EmitRuntimeFunctionStart("maxon_process_close_capture", 1, 0x30);
    EmitReloadArg(0); // X0 = capture_ptr
    EmitCbz(ARM64Register.X0, "__pcc_null");
    EmitBranchLink("mm_free");
    DefineLabel("__pcc_null");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_stdout(capture_ptr) -> cstring_ptr.
  /// Reads stdout pipe from capture struct, returns null-terminated heap string.
  /// Returns pointer to empty string if capture_ptr is null or fd is 0.
  /// </summary>
  private void EmitMaxonProcessReadStdout() {
    DefineSymdata("__rt_empty_cstring", [0x00]);

    EmitRuntimeFunctionStart("maxon_process_read_stdout", 1, 0x30);
    EmitReloadArg(0); // X0 = capture_ptr
    EmitCbz(ARM64Register.X0, "__prso_empty");

    // Load stdout fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 8, 8);
    EmitCbz(ARM64Register.X1, "__prso_empty");

    // Zero the fd in capture to prevent double-read
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 8, 8);

    // Read pipe: X0 = fd
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("maxon_process_read_pipe");
    // X0 = cstring ptr, return it directly
    EmitRuntimeFunctionEnd();

    DefineLabel("__prso_empty");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__rt_empty_cstring");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_stderr(capture_ptr) -> cstring_ptr.
  /// Same as read_stdout but uses offset +16 (stderr fd).
  /// </summary>
  private void EmitMaxonProcessReadStderr() {
    EmitRuntimeFunctionStart("maxon_process_read_stderr", 1, 0x30);
    EmitReloadArg(0); // X0 = capture_ptr
    EmitCbz(ARM64Register.X0, "__prse_empty");

    // Load stderr fd
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, 16, 8);
    EmitCbz(ARM64Register.X1, "__prse_empty");

    // Zero the fd in capture
    EmitMovRegImm(ARM64Register.X2, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, 16, 8);

    // Read pipe
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X1);
    EmitBranchLink("maxon_process_read_pipe");
    EmitRuntimeFunctionEnd();

    DefineLabel("__prse_empty");
    EmitAdrpAddFixup(ARM64Register.X0, _symdataAdrpFixups, "__rt_empty_cstring");
    EmitRuntimeFunctionEnd();
  }

  // ===========================================================================================
  // Inline code-gen helpers (not standalone functions — emit instructions inline)
  // ===========================================================================================

  /// <summary>
  /// Emit clock_gettime(CLOCK_UPTIME_RAW) and convert result to milliseconds.
  /// Uses stack at [x29+timespecOffset] and [x29+timespecOffset+8] for the timespec struct.
  /// Result: X2 = monotonic milliseconds. Clobbers X0, X1, X2, X3, X4.
  /// </summary>
  private void EmitClockGetTimeMs(int timespecOffset) {
    EmitMovRegImm(ARM64Register.X0, CLOCK_UPTIME_RAW);
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, timespecOffset, isAdd: true);
    EmitCallImport("clock_gettime");

    // Convert timespec to ms: tv_sec * 1000 + tv_nsec / 1000000
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X2, ARM64Register.X29, timespecOffset, 8); // tv_sec
    EmitMovRegImm(ARM64Register.X3, 1000);
    EmitWord(0x9B037C42); // MUL X2, X2, X3
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X4, ARM64Register.X29, timespecOffset + 8, 8); // tv_nsec
    EmitMovRegImm(ARM64Register.X3, 1000000);
    EmitWord(0x9AC30884); // UDIV X4, X4, X3
    EmitWord(0x8B040042); // ADD X2, X2, X4 → now_ms
  }

  // ===========================================================================================
  // Trivial runtime functions
  // ===========================================================================================

  // mm_raw_alloc_260: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  /// <summary>
  /// maxon_u64_to_string_fmt: redirect to maxon_i64_to_string_fmt
  /// (unsigned values with sign bit clear are handled correctly).
  /// </summary>
  private void EmitMaxonU64ToStringFmt() {
    DefineLabel("maxon_u64_to_string_fmt");
    EmitBranch("maxon_i64_to_string_fmt");
  }

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
  /// Uses clock_gettime(CLOCK_UPTIME_RAW) for monotonic millisecond timestamps.
  /// Adds (deadline, gt) to the timer array, then yields.
  /// </summary>
  private void EmitMaxonSleep() {
    // Stack: [x29+16] = ms, [x29+24] = deadline, [x29+32] = dequeued GT
    EmitRuntimeFunctionStart("maxon_sleep", 1, 0x50);

    // Get monotonic time in ms → X2
    EmitClockGetTimeMs(40);

    // deadline = now_ms + sleep_ms
    EmitReloadArg(0); // X0 = ms
    // ADD X0, X2, X0
    EmitWord(0x8B000040);
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
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
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
  private void EmitGtProcessPendingWaiter() {
    EmitRuntimeFunctionStart("__gt_process_pending_waiter", 0, 0x20);

    // Load P->pendingWaiter
    EmitLoadP(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, POffPendingWaiter, 8);
    EmitCbz(ARM64Register.X0, "__gt_ppw_done");

    // Clear P->pendingWaiter
    EmitMovRegImm(ARM64Register.X1, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X9, POffPendingWaiter, 8);
    // Enqueue the waiter
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

  /// <summary>
  /// __io_complete_gt(gt_x0, result_x1, error_x2, bytes_x3):
  /// Store IO results into the GT struct, set status=ready, and re-enqueue.
  /// </summary>
  private void EmitIoCompleteGt() {
    EmitRuntimeFunctionStart("__io_complete_gt", 4, 0x30);

    EmitReloadArg(0); // X0 = gt
    EmitReloadArg(1); // X1 = result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffIoResultVal, 8);
    EmitReloadArg(2); // X2 = error
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X2, ARM64Register.X0, GtOffIoErrorCode, 8);

    // Set status = ready
    EmitMovRegImm(ARM64Register.X1, GtStatusReady);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);

    // Skip enqueue for mainThread (stackBase == 0)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X1, "__io_complete_gt_done");

    // Spin-wait until ioYielded == 1 (context switch has saved from-GT's registers).
    // Without this, the I/O worker thread could enqueue the GT while its SP is still
    // being saved, causing another worker to load stale register state.
    DefineLabel("__io_complete_gt_spin");
    EmitReloadArg(0); // reload gt
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X0, GtOffIoYielded, 8);
    EmitCbnz(ARM64Register.X1, "__io_complete_gt_enqueue");
    EmitWord(0xD503203F); // YIELD hint (ARM64 equivalent of x86 PAUSE)
    EmitBranch("__io_complete_gt_spin");

    DefineLabel("__io_complete_gt_enqueue");
    EmitReloadArg(0); // reload gt for enqueue
    EmitBranchLink("__gt_enqueue");

    DefineLabel("__io_complete_gt_done");
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
    // Thread safety note: __io_poll_kqueue uses the global __io_kevent_buf buffer.
    // kqueue itself is thread-safe on macOS, but the shared buffer is not protected
    // by a lock. This is safe for now because all green threads (and thus all callers
    // of this function) run on the main OS thread. If green threads are ever distributed
    // across multiple OS threads, __io_kevent_buf access must be synchronized.
    EmitRuntimeFunctionStart("__io_poll_kqueue", 0, 0x60);

    // Check if kqueue fd is valid (> 0)
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitCmpImm(ARM64Register.X0, 0);
    EmitBranchCond(ARM64ConditionCode.Le, "__io_poll_kqueue_ret");

    // Build zero timeout on stack
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 40, 8); // tv_sec = 0
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // tv_nsec = 0

    // kevent(kq, NULL, 0, __io_kevent_buf, 32, &zero_timeout) → nready
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitMovRegImm(ARM64Register.X1, 0); // changelist = NULL
    EmitMovRegImm(ARM64Register.X2, 0); // nchanges = 0
    EmitGlobalLeaReg(ARM64Register.X3, "__io_kevent_buf"); // eventlist
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

    // kevent_ptr = &__io_kevent_buf[index * 32]
    EmitGlobalLeaReg(ARM64Register.X1, "__io_kevent_buf");
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

    // Reload waiter GT and store result
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X10, ARM64Register.X29, 64, 8); // waiter GT
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 40, 8); // result
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffIoResultVal, 8);

    // Set error = 0 and status = ready
    EmitMovRegImm(ARM64Register.X0, 0);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffIoErrorCode, 8);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X10, GtOffStatus, 8);

    // Enqueue waiter (if stackBase != 0 and waiter != current GT)
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X10, GtOffStackBase, 8);
    EmitCbz(ARM64Register.X0, "__io_poll_kqueue_free_ctx");
    // Skip enqueue if waiter == P->currentGt (avoids double-enqueue when
    // a sleeping GT runs the scheduler loop and its own kqueue event fires)
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitCmpRegReg(ARM64Register.X0, ARM64Register.X10);
    EmitBranchCond(ARM64ConditionCode.Eq, "__io_poll_kqueue_free_ctx");
    EmitMovRegReg(ARM64Register.X0, ARM64Register.X10);
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

    // kevent(kq, changelist, nchanges=1, eventlist=NULL, nevents=0, timeout=NULL)
    EmitGlobalLoadReg(ARM64Register.X0, "__io_kqueue_fd");
    EmitAddSubImm(ARM64Register.X1, ARM64Register.X29, 56, isAdd: true); // changelist
    EmitMovRegImm(ARM64Register.X2, 1); // nchanges
    EmitMovRegImm(ARM64Register.X3, 0); // eventlist = NULL
    EmitMovRegImm(ARM64Register.X4, 0); // nevents = 0
    EmitMovRegImm(ARM64Register.X5, 0); // timeout = NULL
    EmitCallImport("kevent");

    // Set current GT status = waiting
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitMovRegImm(ARM64Register.X0, GtStatusWaiting);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);

    // Yield: try to dequeue next runnable thread
    DefineLabel($"{functionName}_try_dequeue");
    EmitBranchLink("__gt_dequeue");
    EmitCbnz(ARM64Register.X0, $"{functionName}_has_next");

    // No runnable thread: process pending I/O inline, then retry
    EmitBranchLink("__gt_process_pending_waiter");
    EmitBranchLink("__io_check_completions");
    EmitBranchLink("__io_poll_kqueue");
    EmitBranchLink("__gt_timer_check");
    // Check if our IO completed (poll_kqueue sets status=ready for main thread without enqueue)
    EmitLoadCurrentGt(ARM64Register.X9);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X9, GtOffStatus, 8);
    EmitCmpImm(ARM64Register.X0, GtStatusWaiting);
    EmitBranchCond(ARM64ConditionCode.Ne, $"{functionName}_resumed");
    EmitBranch($"{functionName}_try_dequeue");

    DefineLabel($"{functionName}_has_next");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 48, 8); // save next
    EmitMovRegImm(ARM64Register.X1, GtStatusRunning);
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X1, ARM64Register.X0, GtOffStatus, 8);
    EmitLoadCurrentGt(ARM64Register.X0);
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X1, ARM64Register.X29, 48, 8);
    EmitLoadP(ARM64Register.X9);
    EmitMovRegReg(ARM64Register.X2, ARM64Register.X9); // X2 = P*
    EmitBranchLink("__gt_context_switch");

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

}
