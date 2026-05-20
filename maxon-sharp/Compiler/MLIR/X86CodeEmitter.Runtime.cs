using MaxonSharp.Compiler.Ir.Dialects;
using static MaxonSharp.Compiler.Ir.Runtime.GtLayout;
using static MaxonSharp.Compiler.Ir.Runtime.RuntimeEmitter;

namespace MaxonSharp.Compiler.Ir;

public partial class X86CodeEmitter {
  // Internal calling convention parameter registers (must match RegisterManager.CallConvRegs)
  private static readonly X86Register[] _abiArgRegs = [
    X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9,
    X86Register.Rsi, X86Register.Rdi, X86Register.Rax, X86Register.Rbx
  ];

  /// Load tag_index=0 into the given register (runtime allocations don't have tag indices).
  private void EmitTagZero(X86Register dest) {
    EmitXorRegReg(dest, dest);
  }

  public void EmitRuntimeFunctions() {
    // Static empty C string used by null-guard paths (find*, process capture, etc.)
    DefineSymdata("__rt_empty_cstring", [0x00]);
    EmitMaxonForceSegfault();
    EmitMaxonBoundsCheck();
    EmitMaxonCowCheck();
    EmitMaxonToCString();
    EmitMaxonWriteStdout();
    EmitMaxonWriteStderr();
    EmitMaxonManagedWriteStdout();
    EmitMaxonManagedWriteStderr();
    EmitMaxonManagedReadStdin();
    EmitMaxonPanic();
    EmitMaxonPanicPrintFrame();
    EmitMaxonI64ToString();
    EmitMaxonU64ToString();
    EmitMaxonF64ToString();
    EmitMaxonBoolToString();
    EmitMaxonI64ToStringFmt();
    EmitMaxonU64ToStringFmt();
    EmitMaxonF64ToStringFmt();
    EmitMaxonCommandLineCount();
    EmitMaxonCommandLineArg();
    EmitMaxonExecutablePath();
    EmitMaxonFileSize();
    EmitMaxonFileRead();
    EmitMaxonFileClose();
    EmitMaxonFileDelete();
    EmitMaxonFindFilename();
    EmitMaxonFindNextFile();
    EmitMaxonDirectoryExists();
    EmitMaxonCreateDirectory();
    EmitMaxonGetCurrentDirectory();
    // === Subprocess runtime (Phase 3.2) ===
    // Real Windows implementation: CreateProcessW + anonymous pipes for
    // collect, parent-handle inheritance for inherit, NUL device for
    // discard, file redirection for file. WaitForSingleObject is routed
    // through __io_submit_sync(SyncOpProcessWait) so green threads yield.
    // Pipe draining uses CreateThread on the caller's side; the drain
    // threads write into mm_raw_alloc'd buffers that the result struct
    // takes ownership of.
    EmitMaxonSubprocess();
    EmitMaxonStrlen();
    EmitMaxonMemcpy();
    EmitMaxonMemcmp();
    // mm_raw_alloc/free/realloc unified via RuntimeEmitter
    var rawRt = new Runtime.RuntimeEmitter(CreateBackend());
    rawRt.EmitAllocatorFunctions(Compiler.MmTrace, Compiler.MmDebug);
    rawRt.EmitMmRawAlloc(Compiler.MmTrace);
    rawRt.EmitMmRawRealloc(Compiler.MmTrace);
    rawRt.EmitMmRawFree(Compiler.MmTrace);
    rawRt.EmitStringEnsureCap(Compiler.MmTrace);
    rawRt.EmitCowStructDetach(Compiler.MmTrace);
    rawRt.EmitCurrentTimeMs();
    rawRt.EmitCurrentProcessId();
    // DebugStream functions are emitted from 4-X86CodeEmitter.cs
    EmitNetTcpConnect();
    EmitNetSend();
    EmitNetRecv();
    EmitNetClose();
    EmitNetSocketDestructor();
    EmitManagedFileOpenRead();
    EmitManagedFileOpenWrite();
    EmitManagedFileOpenWriteExecutable();
    EmitFileExists();
    EmitFileStat();
    EmitFileStatField();
    EmitSleep();
    EmitManagedFileWrite();
    EmitFileDestructor();
    EmitManagedDirOpenSearch();
    EmitManagedDirClose();
    EmitDirDestructor();
    EmitGreenThreadRuntime();
    EmitIoRuntime();
  }

  /// <summary>
  /// maxon_bounds_check(index_rcx, limit_rdx, msg_r8): panic if (unsigned)index >= (unsigned)limit.
  /// A single unsigned comparison catches both negative indices and indices >= limit.
  /// </summary>
  private void EmitMaxonBoundsCheck() {
    EmitRuntimeFunctionStart("maxon_bounds_check", 3);
    // CMP rcx, rdx (unsigned comparison: index vs limit)
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("b", "rt_bounds_ok"); // JAE would panic; JB means index < limit → OK
    // Tail-call mrt_panic so its stack walk sees the *caller's* frame, not ours
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = msg_r8 (saved at [rbp-0x18])
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitJmp("mrt_panic");
    DefineLabel("rt_bounds_ok");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cow_check(buffer_in_rcx, capacity_in_rdx, byteLen_in_r8, managedPtr_in_r9) -> new_buffer_in_rax
  /// If capacity >= 0, buffer is already writable — return it as-is.
  /// If capacity < 0, allocate byteLen bytes via mm_raw_alloc, copy from old buffer, return new buffer.
  /// The old rdata/slice buffer is NOT freed (capacity < 0 identifies non-owned buffers).
  /// managedPtr is the owning managed struct's heap pointer, used for --mm-trace output.
  /// </summary>
  private void EmitMaxonCowCheck() {
    // Args: rcx=buffer, rdx=capacity, r8=byteLen, r9=managedPtr
    // Stack: [rbp-0x08]=buffer, [rbp-0x10]=capacity, [rbp-0x18]=byteLen,
    //        [rbp-0x20]=managedPtr, [rbp-0x28]=new_buffer
    EmitRuntimeFunctionStart("maxon_cow_check", 4, 0x60);
    // CMP rdx, 0 — signed check: capacity >= 0 means owned writable buffer
    // capacity == -2 (rdata) or capacity == -1 (slice) falls through to COW path
    EmitBytes(0x48, 0x83, 0xFA, 0x00); // CMP rdx, 0
    EmitJcc("ge", "rt_cow_writable"); // JGE: jump if greater-or-equal (signed)
    // If byteLen == 0, nothing to copy — skip COW (e.g. empty slice)
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = byteLen
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_cow_writable");
    // COW path: allocate byteLen bytes, copy old buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = byteLen
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_cow_copy");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-0x28] = new buffer
    // rep movsb: RSI=src, RDI=dst, RCX=count
    EmitMovRegMem(X86Register.Rsi, -0x08, 8);  // RSI = old buffer
    EmitMovRegMem(X86Register.Rdi, -0x28, 8);  // RDI = new buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // RCX = byteLen
    EmitBytes(0xF3, 0xA4); // REP MOVSB
    if (Compiler.MmTrace) {
      // Trace COW copy using the owning managed struct's header
      EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
      EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-0x30] = 0 (no scope)
      EmitInlineTrace("__mm_tag_cow", "cow_check_trace", -0x20, -0x30, sizeSlot: -0x18);
    }
    // Return new buffer
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitJmp("rt_cow_epilogue");
    // already_writable: return old buffer
    DefineLabel("rt_cow_writable");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    DefineLabel("rt_cow_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_to_cstring(buffer_in_rcx, length_in_rdx) -> cstr_in_rax
  /// If buffer[length] == '\0', buffer is already null-terminated — return it as-is.
  /// Otherwise, allocate length+1 bytes, copy, append '\0', return new buffer.
  /// </summary>
  private void EmitMaxonToCString() {
    EmitRuntimeFunctionStart("maxon_to_cstring", 2, 0x40);
    // [rbp-8] = buffer (RCX), [rbp-16] = length (RDX) — saved by prologue
    // Check if buffer[length] == 0
    // movzx eax, byte ptr [rcx+rdx]: 0F B6 04 11
    EmitBytes(0x0F, 0xB6, 0x04, 0x11);
    // TEST AL, AL
    EmitBytes(0x84, 0xC0);
    EmitJcc("z", "rt_tocstr_terminated");

    // Copy path: allocate length+1, copy, null-terminate
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);  // RCX = length
    // LEA RCX, [RCX+1] for alloc size
    EmitBytes(0x48, 0x8D, 0x49, 0x01); // LEA RCX, [RCX+1]
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);     // RDX = destructor = 0 (no managed fields)
    EmitTagZero(X86Register.R8);    // R8 = tag
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_cow_copy");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    // Save new buffer at [rbp-24]
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    // rep movsb: RSI=src, RDI=dst, RCX=count
    EmitMovRegMem(X86Register.Rsi, -0x08, 8);  // RSI = old buffer
    EmitMovRegMem(X86Register.Rdi, -0x18, 8);  // RDI = new buffer
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);  // RCX = length
    EmitBytes(0xF3, 0xA4); // REP MOVSB
    // Null-terminate: mov byte ptr [rdi], 0 (RDI points past the copied bytes after REP MOVSB)
    EmitBytes(0xC6, 0x07, 0x00);
    // Return new buffer
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitJmp("rt_tocstr_epilogue");

    // already_terminated: return old buffer
    DefineLabel("rt_tocstr_terminated");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    DefineLabel("rt_tocstr_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_write_stdout(cstr_ptr_in_rcx) -> bytes_written_in_rax</summary>
  private void EmitMaxonWriteStdout() {
    // Stack layout: [rbp-8]=cstr_ptr, [rbp-16]=length, [rbp-24]=handle, [rbp-32]=bytesWritten
    EmitRuntimeFunctionStart("maxon_write_stdout", 1, 0x40);
    // Zero the bytesWritten slot so upper 4 bytes are clean (WriteFile writes a DWORD)
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = 0
    // strlen: RDX = ptr copy, RAX = counter
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx); // RDX = ptr
    EmitMovRegImm(X86Register.Rax, 0); // RAX = 0 (counter)
    DefineLabel("rt_stdout_strlen_loop");
    // movzx ecx, byte ptr [rdx+rax]: 0F B6 0C 02
    EmitBytes(0x0F, 0xB6, 0x0C, 0x02);
    // TEST CL, CL
    EmitBytes(0x84, 0xC9);
    EmitJcc("z", "rt_stdout_strlen_done");
    // INC RAX
    EmitBytes(0x48, 0xFF, 0xC0);
    EmitJmp("rt_stdout_strlen_loop");
    // done_strlen:
    DefineLabel("rt_stdout_strlen_done");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = length
    // GetStdHandle(STD_OUTPUT_HANDLE = -11)
    EmitMovRegImm(X86Register.Rcx, -11);
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = handle
    // WriteFile(handle, buffer, nNumberOfBytesToWrite, &lpNumberOfBytesWritten, lpOverlapped)
    EmitSystemStackEnter(0x30); // shadow(0x20) + 1 stack arg(0x08) + pad(0x08) = 0x30
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);  // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x10, 8);   // arg3: length
    // arg4: LEA R9, [rbp-0x20] (&bytesWritten)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // arg5: lpOverlapped = NULL at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
    EmitSystemStackLeave(0x30);
    // Return bytes written
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_write_stderr(cstr_ptr_in_rcx) -> bytes_written_in_rax</summary>
  private void EmitMaxonWriteStderr() {
    EmitRuntimeFunctionStart("maxon_write_stderr", 1, 0x40);
    // Zero the bytesWritten slot so upper 4 bytes are clean (WriteFile writes a DWORD)
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = 0
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx);
    EmitMovRegImm(X86Register.Rax, 0);
    DefineLabel("rt_stderr_strlen_loop");
    EmitBytes(0x0F, 0xB6, 0x0C, 0x02);
    EmitBytes(0x84, 0xC9);
    EmitJcc("z", "rt_stderr_strlen_done");
    EmitBytes(0x48, 0xFF, 0xC0);
    EmitJmp("rt_stderr_strlen_loop");
    DefineLabel("rt_stderr_strlen_done");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);
    // GetStdHandle(STD_ERROR_HANDLE = -12)
    EmitMovRegImm(X86Register.Rcx, -12);
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    EmitSystemStackEnter(0x30); // shadow(0x20) + 1 stack arg + pad = 0x30
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitMovRegMem(X86Register.R8, -0x10, 8);
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
    EmitSystemStackLeave(0x30);
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emits a managed write function: takes (buffer_rcx, length_rdx), writes to the given
  /// std handle, returns bytes_written_rax. Used for both stdout and stderr variants.
  /// </summary>
  private void EmitMaxonManagedWrite(string functionName, long stdHandle) {
    // Stack layout: [rbp-8]=buffer(rcx), [rbp-16]=length(rdx), [rbp-24]=handle, [rbp-32]=bytesWritten
    EmitRuntimeFunctionStart(functionName, 2, 0x40);
    // Zero the bytesWritten slot so upper 4 bytes are clean (WriteFile writes a DWORD)
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = 0
    EmitMovRegImm(X86Register.Rcx, stdHandle);
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = handle
    // WriteFile(handle, buffer, nNumberOfBytesToWrite, &bytesWritten, lpOverlapped)
    EmitSystemStackEnter(0x30); // shadow(0x20) + 1 stack arg + pad = 0x30
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);  // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x10, 8);   // arg3: length
    // arg4: LEA R9, [rbp-0x20] (&bytesWritten)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // arg5: lpOverlapped = NULL at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
    EmitSystemStackLeave(0x30);
    // Return bytes written
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonManagedWriteStdout() =>
    EmitMaxonManagedWrite("maxon_managed_write_stdout", -11); // STD_OUTPUT_HANDLE

  private void EmitMaxonManagedWriteStderr() =>
    EmitMaxonManagedWrite("maxon_managed_write_stderr", -12); // STD_ERROR_HANDLE

  /// <summary>
  /// maxon_managed_read_stdin(buffer_ptr_in_rcx, maxBytes_in_rdx) -> bytes_read_in_rax.
  /// Stack: [rbp-8]=buffer, [rbp-16]=maxBytes, [rbp-24]=handle, [rbp-32]=bytesRead.
  /// Mirrors EmitMaxonManagedWrite but uses STD_INPUT_HANDLE (-10) and ReadFile,
  /// returning the bytes-read out-parameter rather than ReadFile's BOOL.
  /// </summary>
  private void EmitMaxonManagedReadStdin() {
    EmitRuntimeFunctionStart("maxon_managed_read_stdin", 2, 0x40);
    // Zero the bytesRead slot so upper 4 bytes are clean.
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, -10); // STD_INPUT_HANDLE
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = handle
    // ReadFile(handle, buffer, nNumberOfBytesToRead, &bytesRead, lpOverlapped)
    EmitSystemStackEnter(0x30); // shadow(0x20) + 1 stack arg + pad = 0x30
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);  // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x10, 8);   // arg3: nNumberOfBytesToRead
    // arg4: LEA R9, [rbp-0x20] (&bytesRead)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // arg5: lpOverlapped = NULL at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "ReadFile");
    EmitSystemStackLeave(0x30);
    // Return bytes read.
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mrt_panic(cstr_ptr_in_rcx): write message to stderr, print stack trace, then ExitProcess(1).
  /// Stack layout:
  ///   [rbp-0x08] = cstr_ptr (panic message)
  ///   [rbp-0x10] = text_base (absolute address of mrt_start)
  ///   [rbp-0x18] = symtable_ptr (absolute address of __symtable in .symtab)
  ///   [rbp-0x20] = current frame rbp for stack walk
  ///   [rbp-0x28] = frame counter (counts down from 32)
  ///   [rbp-0x30] = symtable entry count
  ///   [rbp-0x38] = text_offset for current frame lookup
  /// </summary>
  private void EmitMaxonPanic() {
    DefineSymdata("__panic_stacktrace", System.Text.Encoding.UTF8.GetBytes("Stack trace:\n\0"));
    DefineSymdata("__panic_in", System.Text.Encoding.UTF8.GetBytes("  in \0"));
    DefineSymdata("__panic_newline", System.Text.Encoding.UTF8.GetBytes("\n\0"));
    DefineSymdata("__panic_unknown", System.Text.Encoding.UTF8.GetBytes("<unknown>\0"));

    EmitRuntimeFunctionStart("mrt_panic", 1, 0x60);

    // Print the panic message
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);

    // Compute text_base = absolute address of mrt_start
    EmitLeaFuncAddr(X86Register.Rax, "mrt_start");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);

    // Load symtable pointer (from symdata section)
    EmitLeaRegSymdataRel(X86Register.Rax, "__symtable");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // Read symtable count (first 4 bytes)
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitBytes(0x8B, 0x00); // MOV eax, [rax]
    EmitMovMemReg(-0x30, X86Register.Rax, 8);

    // Print "Stack trace:\n"
    EmitLeaRegSymdataRel(X86Register.Rcx, "__panic_stacktrace");
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);

    // Print first frame: [rbp+8] is return addr within the panicking function
    EmitMovRegMem(X86Register.Rax, 0x08, 8); // return addr into panicking function
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // text_base
    EmitBytes(0x48, 0x29, 0xD0); // SUB rax, rdx => text offset
    EmitMovMemReg(-0x38, X86Register.Rax, 8);
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "mrt_panic_print_frame"));
    EmitDword(0);

    // Initialize: frame_rbp = [rbp] (caller's saved rbp), counter = 32
    EmitMovRegMem(X86Register.Rax, 0, 8);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rax, 32);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    // Stack walk loop
    DefineLabel("rt_panic_walk_loop");

    // Check frame counter
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_panic_walk_done");

    // Decrement counter
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    // Load current frame_rbp
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_panic_walk_done");

    // Get return address: [frame_rbp + 8]
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, 8);
    EmitBytes(0x48, 0x85, 0xD2); // TEST rdx, rdx
    EmitJcc("z", "rt_panic_walk_done");

    // Compute text offset = return_addr - text_base
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitBytes(0x48, 0x29, 0xCA); // SUB rdx, rcx
    EmitBytes(0x48, 0x85, 0xD2); // TEST rdx, rdx
    EmitJcc("s", "rt_panic_walk_done"); // negative = outside .text

    // Save text_offset for print_frame helper
    EmitMovMemReg(-0x38, X86Register.Rdx, 8);

    // Advance frame_rbp to next frame before calling helper (which clobbers regs)
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, 0); // [frame_rbp] = prev rbp
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // Print this frame
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "mrt_panic_print_frame"));
    EmitDword(0);

    EmitJmp("rt_panic_walk_loop");

    DefineLabel("rt_panic_walk_done");
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitCallImportOnSystemStack("kernel32.dll", "ExitProcess");
    EmitByte(0xCC); // int3
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Helper: looks up [rbp-0x38] (text_offset) in the symbol table and prints "  in funcName\n".
  /// Shares mrt_panic's stack frame (accesses [rbp-0x18], [rbp-0x30], [rbp-0x38]).
  /// </summary>
  private void EmitMaxonPanicPrintFrame() {
    DefineLabel("mrt_panic_print_frame");

    // Print "  in "
    EmitLeaRegSymdataRel(X86Register.Rcx, "__panic_in");
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);

    // Linear scan: find largest code_offset <= text_offset
    // symtable: count(4) | entries[count] of {codeOffset(4), nameOffset(4)} | strings
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = symtable_ptr
    EmitAddRegImm(X86Register.Rax, 4); // RAX = &entries[0]
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = count
    EmitMovRegMem(X86Register.Rdx, -0x38, 8); // RDX = target text_offset
    EmitMovRegImm(X86Register.R8, -1); // R8 = best name_offset (-1 = none)
    EmitMovRegImm(X86Register.R9, 0);  // R9 = loop index

    DefineLabel("rt_panic_lookup_loop");
    EmitCmpRegReg(X86Register.R9, X86Register.Rcx);
    EmitJcc("ae", "rt_panic_lookup_done"); // unsigned: index >= count

    // R11 = &entries[R9] = RAX + R9*8
    EmitBytes(0x4E, 0x8D, 0x1C, 0xC8); // LEA r11, [rax + r9*8]

    // Load entry code_offset: MOV r10d, [r11] (zero-extends to 64-bit)
    EmitBytes(0x45, 0x8B, 0x13); // MOV r10d, [r11]

    // if text_offset < entry_offset, skip
    EmitCmpRegReg(X86Register.Rdx, X86Register.R10);
    EmitJcc("b", "rt_panic_lookup_next"); // unsigned below

    // entry_offset <= text_offset: update best match
    EmitBytes(0x45, 0x8B, 0x43, 0x04); // MOV r8d, [r11+4] (name_offset)

    DefineLabel("rt_panic_lookup_next");
    EmitBytes(0x49, 0xFF, 0xC1); // INC r9
    EmitJmp("rt_panic_lookup_loop");

    DefineLabel("rt_panic_lookup_done");

    // Check if we found a match
    EmitBytes(0x49, 0x83, 0xF8, 0xFF); // CMP r8, -1
    EmitJcc("z", "rt_panic_print_unknown");

    // Print function name: symtable_ptr + name_offset
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitBytes(0x4C, 0x01, 0xC1); // ADD rcx, r8
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);
    EmitJmp("rt_panic_print_newline");

    DefineLabel("rt_panic_print_unknown");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__panic_unknown");
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);

    DefineLabel("rt_panic_print_newline");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__panic_newline");
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);

    EmitByte(0xC3); // ret
  }

  /// <summary>
  /// maxon_i64_to_string(value_in_rcx, buffer_ptr_in_rdx) -> length_in_rax
  /// Converts a signed 64-bit integer to its decimal string representation.
  /// Writes into caller-provided buffer (must be >= 21 bytes). Returns byte count.
  /// Stack: [rbp-8]=value, [rbp-16]=buffer, [rbp-24]=write_pos
  /// </summary>
  private void EmitMaxonI64ToString() {
    EmitRuntimeFunctionStart("maxon_i64_to_string", 2);

    // Special case: value == 0
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_i64str_not_zero");
    // Write '0' to buffer[0], null to buffer[1]
    EmitBytes(0xC6, 0x02, 0x30); // MOV byte [rdx], '0'
    EmitBytes(0xC6, 0x42, 0x01, 0x00); // MOV byte [rdx+1], 0
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("rt_i64str_epilogue");

    // not_zero: check for negative
    DefineLabel("rt_i64str_not_zero");
    // R8 = 0 (is_negative flag)
    EmitBytes(0x4D, 0x31, 0xC0); // XOR r8, r8
    // TEST rcx, rcx / JS negative
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("ns", "rt_i64str_positive");
    // Negate: rcx = -rcx
    EmitBytes(0x48, 0xF7, 0xD9); // NEG rcx
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // update stored value
    // R8 = 1 (is_negative)
    EmitMovRegImm(X86Register.R8, 1);

    // positive: R9 = buffer + 20 (write position, work backwards from end)
    DefineLabel("rt_i64str_positive");
    EmitMovRegReg(X86Register.R9, X86Register.Rdx); // R9 = buffer
    EmitAddRegImm(X86Register.R9, 20); // R9 = buffer + 20
    EmitBytes(0x41, 0xC6, 0x01, 0x00); // MOV byte [r9], 0 (null terminator)

    // Save is_negative flag to stack
    EmitMovMemReg(-0x18, X86Register.R8, 8); // [rbp-24] = is_negative

    // digit_loop: divide rcx by 10, write remainder as digit
    DefineLabel("rt_i64str_digit_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9 (move write position back)
    // RAX = value (for IDIV), RDX:RAX = sign-extended value
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = value
    EmitMovRegImm(X86Register.Rcx, 10);
    // Zero RDX before DIV (unsigned division since we already handled sign)
    EmitBytes(0x48, 0x31, 0xD2); // XOR rdx, rdx
    EmitBytes(0x48, 0xF7, 0xF1); // DIV rcx (RAX = quotient, RDX = remainder)
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // store quotient back
    // Convert remainder to ASCII digit: RDX + '0'
    EmitAddRegImm(X86Register.Rdx, 0x30); // RDX += '0'
    // Write digit: MOV byte [r9], dl
    EmitBytes(0x41, 0x88, 0x11); // MOV byte [r9], dl
    // Check if quotient is zero
    EmitBytes(0x48, 0x83, 0x7D, 0xF8, 0x00); // CMP qword [rbp-8], 0
    EmitJcc("nz", "rt_i64str_digit_loop");

    // If negative, prepend '-'
    EmitMovRegMem(X86Register.R8, -0x18, 8); // R8 = is_negative
    EmitBytes(0x4D, 0x85, 0xC0); // TEST r8, r8
    EmitJcc("z", "rt_i64str_no_sign");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitBytes(0x41, 0xC6, 0x01, 0x2D); // MOV byte [r9], '-'

    // no_sign: copy from R9 to buffer start, compute length
    DefineLabel("rt_i64str_no_sign");
    // Length = (buffer + 20) - R9
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitAddRegImm(X86Register.Rax, 20);
    EmitBytes(0x4C, 0x29, 0xC8); // SUB rax, r9 => RAX = length

    // Now copy the digits from R9 to buffer start
    // RSI = R9 (src), RDI = buffer (dst), RCX = length
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // save length to [rbp-24]
    EmitBytes(0x4C, 0x89, 0xCE); // MOV rsi, r9
    EmitMovRegMem(X86Register.Rdi, -0x10, 8); // RDI = buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = length
    EmitBytes(0xF3, 0xA4); // REP MOVSB

    // Null-terminate at buffer[length]
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = length
    EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx
    EmitBytes(0xC6, 0x00, 0x00); // MOV byte [rax], 0

    // Return length
    EmitMovRegMem(X86Register.Rax, -0x18, 8);

    DefineLabel("rt_i64str_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_u64_to_string(value_in_rcx, buffer_ptr_in_rdx) -> length_in_rax
  /// Converts an unsigned 64-bit integer to its decimal string representation.
  /// Writes into caller-provided buffer (must be >= 21 bytes). Returns byte count.
  /// Same algorithm as i64_to_string but without sign handling.
  /// Stack: [rbp-8]=value, [rbp-16]=buffer
  /// </summary>
  private void EmitMaxonU64ToString() {
    EmitRuntimeFunctionStart("maxon_u64_to_string", 2);

    // Special case: value == 0
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_u64str_not_zero");
    // Write '0' to buffer[0], null to buffer[1]
    EmitBytes(0xC6, 0x02, 0x30); // MOV byte [rdx], '0'
    EmitBytes(0xC6, 0x42, 0x01, 0x00); // MOV byte [rdx+1], 0
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("rt_u64str_epilogue");

    // not_zero: no sign check needed for unsigned
    DefineLabel("rt_u64str_not_zero");

    // R9 = buffer + 20 (write position, work backwards from end)
    EmitMovRegReg(X86Register.R9, X86Register.Rdx); // R9 = buffer
    EmitAddRegImm(X86Register.R9, 20); // R9 = buffer + 20
    EmitBytes(0x41, 0xC6, 0x01, 0x00); // MOV byte [r9], 0 (null terminator)

    // digit_loop: divide rcx by 10, write remainder as digit
    DefineLabel("rt_u64str_digit_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9 (move write position back)
    // RAX = value (for DIV)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = value
    EmitMovRegImm(X86Register.Rcx, 10);
    // Zero RDX before unsigned DIV
    EmitBytes(0x48, 0x31, 0xD2); // XOR rdx, rdx
    EmitBytes(0x48, 0xF7, 0xF1); // DIV rcx (RAX = quotient, RDX = remainder)
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // store quotient back
    // Convert remainder to ASCII digit: RDX + '0'
    EmitAddRegImm(X86Register.Rdx, 0x30); // RDX += '0'
    // Write digit: MOV byte [r9], dl
    EmitBytes(0x41, 0x88, 0x11); // MOV byte [r9], dl
    // Check if quotient is zero
    EmitBytes(0x48, 0x83, 0x7D, 0xF8, 0x00); // CMP qword [rbp-8], 0
    EmitJcc("nz", "rt_u64str_digit_loop");

    // No sign prefix needed — copy from R9 to buffer start, compute length
    // Length = (buffer + 20) - R9
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitAddRegImm(X86Register.Rax, 20);
    EmitBytes(0x4C, 0x29, 0xC8); // SUB rax, r9 => RAX = length

    // Copy the digits from R9 to buffer start
    // RSI = R9 (src), RDI = buffer (dst), RCX = length
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // save length to [rbp-24]
    EmitBytes(0x4C, 0x89, 0xCE); // MOV rsi, r9
    EmitMovRegMem(X86Register.Rdi, -0x10, 8); // RDI = buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = length
    EmitBytes(0xF3, 0xA4); // REP MOVSB

    // Null-terminate at buffer[length]
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = length
    EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx
    EmitBytes(0xC6, 0x00, 0x00); // MOV byte [rax], 0

    // Return length
    EmitMovRegMem(X86Register.Rax, -0x18, 8);

    DefineLabel("rt_u64str_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_f64_to_string(value_in_xmm0, buffer_ptr_in_rdx) -> length_in_rax
  /// Converts a float64 to its decimal string representation with up to 6 fractional digits.
  /// Trailing fractional zeros are stripped (except one digit after the decimal point).
  /// Buffer must be >= 32 bytes. Returns byte count (excluding null terminator).
  /// Stack layout:
  ///   [rbp-0x08] = float value (overwritten from XMM0 since prologue saves RCX here)
  ///   [rbp-0x10] = buffer pointer (saved by prologue from RDX)
  ///   [rbp-0x18] = is_negative flag
  ///   [rbp-0x20] = integer part length (after i64_to_string call)
  ///   [rbp-0x28] = write position / scratch
  ///   [rbp-0x30] = integer part (i64)
  /// </summary>
  private void EmitMaxonF64ToString() {
    EmitRuntimeFunctionStart("maxon_f64_to_string", 2, 0xA0);

    // Prologue saved RCX to [rbp-8] and RDX to [rbp-16], but the float is in XMM0.
    // Overwrite [rbp-8] with the actual float value.
    EmitMovMemXmm(-0x08, X86XmmRegister.Xmm0, FloatPrecision.F64);

    // ---- Special case: value == 0.0 ----
    // XORPD xmm1, xmm1 to get 0.0
    EmitBytes(0x66, 0x0F, 0x57, 0xC9); // XORPD xmm1, xmm1
    EmitUcomisXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64);
    EmitJcc("nz", "rt_f64str_not_zero");
    // Also jump if parity is set (NaN)
    EmitJcc("p", "rt_f64str_not_zero");
    // Write "0.0\0" to buffer
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitBytes(0xC6, 0x00, 0x30);             // MOV byte [rax], '0'
    EmitBytes(0xC6, 0x40, 0x01, 0x2E);       // MOV byte [rax+1], '.'
    EmitBytes(0xC6, 0x40, 0x02, 0x30);       // MOV byte [rax+2], '0'
    EmitBytes(0xC6, 0x40, 0x03, 0x00);       // MOV byte [rax+3], 0
    EmitMovRegImm(X86Register.Rax, 3);
    EmitJmp("rt_f64str_epilogue");

    // ---- Handle sign ----
    DefineLabel("rt_f64str_not_zero");
    // is_negative = 0
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // Reload value into XMM0 and compare with zero
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64);
    // XORPD xmm1, xmm1 for zero
    EmitBytes(0x66, 0x0F, 0x57, 0xC9); // XORPD xmm1, xmm1
    EmitUcomisXmm(X86XmmRegister.Xmm1, X86XmmRegister.Xmm0, FloatPrecision.F64);
    // If 0.0 > value (i.e., value is negative), UCOMISD sets CF
    EmitJcc("be", "rt_f64str_positive");

    // Negative: negate by subtracting from zero (0.0 - value)
    // XMM1 is already 0.0
    EmitSubXmm(X86XmmRegister.Xmm1, X86XmmRegister.Xmm0, FloatPrecision.F64); // XMM1 = 0.0 - value = |value|
    // Store |value| back
    EmitMovMemXmm(-0x08, X86XmmRegister.Xmm1, FloatPrecision.F64);
    // is_negative = 1
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // ---- Extract integer part ----
    DefineLabel("rt_f64str_positive");
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64); // XMM0 = |value|
    EmitCvttFloat2Si(X86Register.Rax, X86XmmRegister.Xmm0, FloatPrecision.F64); // RAX = truncate(|value|)
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-0x30] = integer part

    // Set up args for maxon_i64_to_string: RCX = integer part, RDX = buffer (possibly offset by 1 for '-')
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // RDX = buffer
    // If negative, advance buffer by 1 to leave room for '-'
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = is_negative
    EmitBytes(0x48, 0x01, 0xCA);              // ADD RDX, RCX (add 0 or 1)
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = integer part value
    // Call maxon_i64_to_string(integer_value, buffer+offset)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    // RAX = length of integer part string
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-0x20] = int_part_length

    // If negative, write '-' at buffer[0] and adjust length
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = is_negative
    EmitBytes(0x48, 0x85, 0xC9);              // TEST RCX, RCX
    EmitJcc("z", "rt_f64str_no_sign");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitBytes(0xC6, 0x00, 0x2D);              // MOV byte [rax], '-'
    // Increase length by 1 for the sign
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    DefineLabel("rt_f64str_no_sign");
    // ---- Append decimal point ----
    // write_pos = buffer + int_part_length (including sign)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitMovRegMem(X86Register.Rcx, -0x20, 8); // RCX = current length
    EmitBytes(0x48, 0x01, 0xC8);              // ADD RAX, RCX
    EmitBytes(0xC6, 0x00, 0x2E);              // MOV byte [rax], '.'

    // ---- Extract fractional part ----
    // Reload |value| and integer part
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64); // XMM0 = |value|
    EmitMovRegMem(X86Register.Rax, -0x30, 8);     // RAX = integer part
    EmitCvtSi2Float(X86XmmRegister.Xmm1, X86Register.Rax, FloatPrecision.F64); // XMM1 = (double)integer_part
    EmitSubXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64); // XMM0 = fractional part

    // Multiply by 1000000 for 6 decimal places
    // Load 1000000.0 into XMM1 via GPR to avoid rdata dependency
    EmitMovRegImm(X86Register.Rax, BitConverter.DoubleToInt64Bits(1000000.0));
    EmitBytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // MOVQ XMM1, RAX
    EmitMulXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64); // XMM0 = frac * 1000000

    // Add 0.5 for rounding
    // Load 0.5 into XMM1 via GPR to avoid rdata dependency
    EmitMovRegImm(X86Register.Rax, BitConverter.DoubleToInt64Bits(0.5));
    EmitBytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // MOVQ XMM1, RAX
    EmitAddXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64); // XMM0 = frac * 1000000 + 0.5

    EmitCvttFloat2Si(X86Register.Rax, X86XmmRegister.Xmm0, FloatPrecision.F64); // RAX = rounded 6-digit fractional integer

    // Clamp to 999999 if rounding pushed it to 1000000 (avoids carry into integer part)
    EmitBytes(0x48, 0x3D, 0x40, 0x42, 0x0F, 0x00); // CMP RAX, 1000000
    EmitJcc("l", "rt_f64str_frac_ok");
    EmitMovRegImm(X86Register.Rax, 999999);
    DefineLabel("rt_f64str_frac_ok");

    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-0x28] = frac_digits

    // ---- Write 6 fractional digits backwards then reverse into place ----
    // Write position = buffer + int_part_length + 1 (past the '.')
    EmitMovRegMem(X86Register.Rdi, -0x10, 8); // RDI = buffer
    EmitMovRegMem(X86Register.Rcx, -0x20, 8); // RCX = int_part_length (includes sign)
    EmitBytes(0x48, 0x01, 0xCF);              // ADD RDI, RCX
    EmitAddRegImm(X86Register.Rdi, 1);         // past the '.'

    // Write 6 digits from the fractional integer (least-significant first into positions 5,4,3,2,1,0)
    // We write them forward at [rdi+0..5] by dividing from the end
    // R8 = loop counter (6 iterations), working from position 5 down to 0
    EmitMovRegImm(X86Register.R8, 6);
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = frac_digits

    DefineLabel("rt_f64str_frac_loop");
    EmitBytes(0x48, 0x31, 0xD2);              // XOR RDX, RDX
    EmitMovRegImm(X86Register.Rcx, 10);
    EmitBytes(0x48, 0xF7, 0xF1);              // DIV RCX => RAX = quotient, RDX = remainder
    // Write digit at [rdi + r8 - 1]
    EmitAddRegImm(X86Register.Rdx, 0x30);      // RDX += '0'
    // LEA RSI, [RDI + R8 - 1]
    EmitBytes(0x4A, 0x8D, 0x74, 0x07, 0xFF);  // LEA RSI, [RDI + R8*1 - 1]
    // MOV byte [RSI], DL
    EmitBytes(0x88, 0x16);
    // DEC R8
    EmitBytes(0x49, 0xFF, 0xC8);
    EmitBytes(0x4D, 0x85, 0xC0);              // TEST R8, R8
    EmitJcc("nz", "rt_f64str_frac_loop");

    // ---- Strip trailing zeros (keep at least 1 fractional digit) ----
    // RSI = pointer to last fractional digit = RDI + 5
    EmitMovRegReg(X86Register.Rsi, X86Register.Rdi);
    EmitAddRegImm(X86Register.Rsi, 5); // RSI points at 6th digit (index 5)

    DefineLabel("rt_f64str_strip_loop");
    // Compare RSI with RDI (must keep at least rdi+0, so strip while RSI > RDI)
    EmitBytes(0x48, 0x39, 0xFE);              // CMP RSI, RDI
    EmitJcc("be", "rt_f64str_strip_done");     // if RSI <= RDI, stop (keep at least 1 digit)
    // Check if byte at [RSI] == '0'
    EmitBytes(0x80, 0x3E, 0x30);              // CMP byte [RSI], '0'
    EmitJcc("nz", "rt_f64str_strip_done");
    // DEC RSI
    EmitBytes(0x48, 0xFF, 0xCE);              // DEC RSI
    EmitJmp("rt_f64str_strip_loop");

    DefineLabel("rt_f64str_strip_done");
    // Null-terminate after the last non-zero fractional digit
    // RSI points to last kept digit, null-terminate at RSI+1
    EmitBytes(0xC6, 0x46, 0x01, 0x00);        // MOV byte [RSI+1], 0

    // ---- Compute total length ----
    // length = (RSI + 1) - buffer
    EmitBytes(0x48, 0xFF, 0xC6);              // INC RSI (RSI now points past last digit)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = buffer
    EmitMovRegReg(X86Register.Rcx, X86Register.Rsi);
    EmitBytes(0x48, 0x29, 0xC1);              // SUB RCX, RAX => RCX = length
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx); // RAX = length

    DefineLabel("rt_f64str_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_bool_to_string(value_in_rcx, buffer_ptr_in_rdx) -> length_in_rax
  /// Writes "true" (4 bytes) or "false" (5 bytes) into the caller-provided buffer.
  /// Buffer must be >= 6 bytes. Returns byte count (excluding null terminator).
  /// Stack: [rbp-8]=value, [rbp-16]=buffer
  /// </summary>
  private void EmitMaxonBoolToString() {
    EmitRuntimeFunctionStart("maxon_bool_to_string", 2);

    // TEST rcx, rcx — check if value is 0 (false)
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("z", "rt_boolstr_false");

    // True path: write "true\0"
    EmitBytes(0xC6, 0x02, 0x74);       // MOV byte [rdx], 't'
    EmitBytes(0xC6, 0x42, 0x01, 0x72); // MOV byte [rdx+1], 'r'
    EmitBytes(0xC6, 0x42, 0x02, 0x75); // MOV byte [rdx+2], 'u'
    EmitBytes(0xC6, 0x42, 0x03, 0x65); // MOV byte [rdx+3], 'e'
    EmitBytes(0xC6, 0x42, 0x04, 0x00); // MOV byte [rdx+4], 0
    EmitMovRegImm(X86Register.Rax, 4);
    EmitJmp("rt_boolstr_epilogue");

    // False path: write "false\0"
    DefineLabel("rt_boolstr_false");
    EmitBytes(0xC6, 0x02, 0x66);       // MOV byte [rdx], 'f'
    EmitBytes(0xC6, 0x42, 0x01, 0x61); // MOV byte [rdx+1], 'a'
    EmitBytes(0xC6, 0x42, 0x02, 0x6C); // MOV byte [rdx+2], 'l'
    EmitBytes(0xC6, 0x42, 0x03, 0x73); // MOV byte [rdx+3], 's'
    EmitBytes(0xC6, 0x42, 0x04, 0x65); // MOV byte [rdx+4], 'e'
    EmitBytes(0xC6, 0x42, 0x05, 0x00); // MOV byte [rdx+5], 0
    EmitMovRegImm(X86Register.Rax, 5);

    DefineLabel("rt_boolstr_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_i64_to_string_fmt(value_in_rcx, buffer_ptr_in_rdx, fmt_ptr_in_r8, fmt_len_in_r9) -> length_in_rax
  /// Converts a signed 64-bit integer to a formatted string representation.
  /// Format specifiers: [0][width][type] where type is d(ecimal), x(hex), X(HEX), b(inary), o(ctal)
  /// Examples: "04" = zero-pad to width 4, "x" = lowercase hex, "06X" = zero-pad hex to 6
  /// Buffer must be >= 72 bytes. Returns byte count (excluding null terminator).
  /// Stack layout:
  ///   [rbp-0x08] = value
  ///   [rbp-0x10] = buffer pointer
  ///   [rbp-0x18] = fmt_ptr
  ///   [rbp-0x20] = fmt_len
  ///   [rbp-0x28] = is_negative flag
  ///   [rbp-0x30] = fill char ('0' or ' ')
  ///   [rbp-0x38] = min_width
  ///   [rbp-0x40] = type char ('d', 'x', 'X', 'b', 'o'), 0 = default decimal
  ///   [rbp-0x48] = digit count (after conversion, before padding)
  ///   [rbp-0x50] = scratch / write position
  /// </summary>
  private void EmitMaxonI64ToStringFmt() {
    EmitRuntimeFunctionStart("maxon_i64_to_string_fmt", 4, 0x80);

    // ---- Parse format string ----
    // Default fill = ' ', width = 0, type = 0 (decimal)
    EmitMovRegImm(X86Register.Rax, ' ');
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // fill = ' '
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // width = 0
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // type = 0

    // If fmt_len == 0, jump to decimal conversion with no formatting
    EmitMovRegMem(X86Register.Rcx, -0x20, 8); // RCX = fmt_len
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "rt_i64fmt_convert");

    // RSI = fmt_ptr, RCX = fmt_len (index counter)
    EmitMovRegMem(X86Register.Rsi, -0x18, 8);
    EmitMovRegImm(X86Register.Rcx, 0); // parse index

    // Check first char: if '0', set fill='0' and advance
    EmitBytes(0x8A, 0x06); // MOV AL, [RSI]
    EmitBytes(0x3C, 0x30); // CMP AL, '0'
    EmitJcc("nz", "rt_i64fmt_parse_width");
    EmitMovRegImm(X86Register.Rax, '0');
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // fill = '0'
    EmitBytes(0x48, 0xFF, 0xC6); // INC RSI
    EmitBytes(0x48, 0xFF, 0xC1); // INC RCX (consumed one char)

    // Parse width digits
    DefineLabel("rt_i64fmt_parse_width");
    EmitMovMemReg(-0x50, X86Register.Rcx, 8); // save parse index
    EmitMovRegImm(X86Register.Rdi, 0); // RDI = accumulated width

    DefineLabel("rt_i64fmt_width_loop");
    EmitMovRegMem(X86Register.Rcx, -0x50, 8); // RCX = parse index
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // RDX = fmt_len
    EmitBytes(0x48, 0x39, 0xD1); // CMP RCX, RDX
    EmitJcc("ge", "rt_i64fmt_parse_done"); // past end of format string
    EmitBytes(0x8A, 0x06); // MOV AL, [RSI]
    // Check if digit: '0' <= AL <= '9'
    EmitBytes(0x3C, 0x30); // CMP AL, '0'
    EmitJcc("l", "rt_i64fmt_check_type");
    EmitBytes(0x3C, 0x39); // CMP AL, '9'
    EmitJcc("g", "rt_i64fmt_check_type");
    // It's a digit: width = width * 10 + (AL - '0')
    EmitBytes(0x0F, 0xB6, 0xC0); // MOVZX EAX, AL
    EmitBytes(0x48, 0x83, 0xE8, 0x30); // SUB RAX, '0'
    // RDI = RDI * 10 + RAX
    EmitBytes(0x48, 0x6B, 0xFF, 0x0A); // IMUL RDI, RDI, 10
    EmitBytes(0x48, 0x01, 0xC7); // ADD RDI, RAX
    EmitBytes(0x48, 0xFF, 0xC6); // INC RSI
    EmitMovRegMem(X86Register.Rcx, -0x50, 8);
    EmitBytes(0x48, 0xFF, 0xC1); // INC RCX
    EmitMovMemReg(-0x50, X86Register.Rcx, 8);
    EmitJmp("rt_i64fmt_width_loop");

    // Check if current char is a type specifier
    DefineLabel("rt_i64fmt_check_type");
    EmitBytes(0x0F, 0xB6, 0xC0); // MOVZX EAX, AL
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // type = current char

    DefineLabel("rt_i64fmt_parse_done");
    EmitMovMemReg(-0x38, X86Register.Rdi, 8); // width = accumulated width

    // ---- Convert value based on type ----
    DefineLabel("rt_i64fmt_convert");
    EmitMovRegMem(X86Register.Rax, -0x40, 8); // RAX = type char

    // Check for hex: 'x' = 0x78, 'X' = 0x58
    EmitBytes(0x48, 0x83, 0xF8, 0x78); // CMP RAX, 'x'
    EmitJcc("z", "rt_i64fmt_hex");
    EmitBytes(0x48, 0x83, 0xF8, 0x58); // CMP RAX, 'X'
    EmitJcc("z", "rt_i64fmt_hex");

    // Check for binary: 'b' = 0x62
    EmitBytes(0x48, 0x83, 0xF8, 0x62); // CMP RAX, 'b'
    EmitJcc("z", "rt_i64fmt_binary");

    // Check for octal: 'o' = 0x6F
    EmitBytes(0x48, 0x83, 0xF8, 0x6F); // CMP RAX, 'o'
    EmitJcc("z", "rt_i64fmt_octal");

    // Default: decimal conversion
    // ---- Decimal path ----
    // Check sign first
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // is_negative = 0
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = value
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("ns", "rt_i64fmt_dec_positive");
    // Negative
    EmitBytes(0x48, 0xF7, 0xD9); // NEG rcx
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // store |value|
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // is_negative = 1

    DefineLabel("rt_i64fmt_dec_positive");
    // R9 = buffer + 64 (work backwards)
    EmitMovRegMem(X86Register.R9, -0x10, 8); // R9 = buffer
    EmitAddRegImm(X86Register.R9, 64);
    EmitBytes(0x41, 0xC6, 0x01, 0x00); // MOV byte [r9], 0

    // Special case zero
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_i64fmt_dec_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitBytes(0x41, 0xC6, 0x01, 0x30); // MOV byte [r9], '0'
    EmitJmp("rt_i64fmt_dec_digits_done");

    DefineLabel("rt_i64fmt_dec_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = value
    EmitMovRegImm(X86Register.Rcx, 10);
    EmitBytes(0x48, 0x31, 0xD2); // XOR rdx, rdx
    EmitBytes(0x48, 0xF7, 0xF1); // DIV rcx
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // store quotient
    EmitAddRegImm(X86Register.Rdx, 0x30);
    EmitBytes(0x41, 0x88, 0x11); // MOV byte [r9], dl
    EmitBytes(0x48, 0x83, 0x7D, 0xF8, 0x00); // CMP qword [rbp-8], 0
    EmitJcc("nz", "rt_i64fmt_dec_loop");

    DefineLabel("rt_i64fmt_dec_digits_done");
    // digit_count = (buffer + 64) - R9
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitAddRegImm(X86Register.Rax, 64);
    EmitBytes(0x4C, 0x29, 0xC8); // SUB rax, r9
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // digit_count

    // Now apply padding and write to buffer
    // Total content: sign (0 or 1) + max(digit_count, width - sign_len)
    // R9 still points to first digit in temp area (buffer+64 area)
    EmitJmp("rt_i64fmt_apply_padding");

    // ---- Hex path ----
    DefineLabel("rt_i64fmt_hex");
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // no sign for hex
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = value (treat as unsigned)

    // R9 = buffer + 64 (work backwards)
    EmitMovRegMem(X86Register.R9, -0x10, 8);
    EmitAddRegImm(X86Register.R9, 64);
    EmitBytes(0x41, 0xC6, 0x01, 0x00); // null terminator

    // Special case zero
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_i64fmt_hex_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitBytes(0x41, 0xC6, 0x01, 0x30); // MOV byte [r9], '0'
    EmitJmp("rt_i64fmt_hex_done");

    DefineLabel("rt_i64fmt_hex_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    // Get lowest nibble: RAX = RCX & 0x0F
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx);
    EmitBytes(0x48, 0x83, 0xE0, 0x0F); // AND rax, 0x0F
    // Convert to hex digit
    EmitBytes(0x48, 0x83, 0xF8, 0x0A); // CMP rax, 10
    EmitJcc("l", "rt_i64fmt_hex_digit");
    // A-F or a-f: check type char for case
    EmitMovRegMem(X86Register.Rdx, -0x40, 8); // type char
    EmitBytes(0x48, 0x83, 0xFA, 0x58); // CMP rdx, 'X'
    EmitJcc("z", "rt_i64fmt_hex_upper");
    EmitAddRegImm(X86Register.Rax, 0x57); // 'a' - 10 = 0x57
    EmitJmp("rt_i64fmt_hex_write");
    DefineLabel("rt_i64fmt_hex_upper");
    EmitAddRegImm(X86Register.Rax, 0x37); // 'A' - 10 = 0x37
    EmitJmp("rt_i64fmt_hex_write");
    DefineLabel("rt_i64fmt_hex_digit");
    EmitAddRegImm(X86Register.Rax, 0x30); // '0'
    DefineLabel("rt_i64fmt_hex_write");
    EmitBytes(0x41, 0x88, 0x01); // MOV byte [r9], al
    // Shift right by 4
    EmitBytes(0x48, 0xC1, 0xE9, 0x04); // SHR rcx, 4
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_i64fmt_hex_loop");

    DefineLabel("rt_i64fmt_hex_done");
    // digit_count = (buffer + 64) - R9
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitAddRegImm(X86Register.Rax, 64);
    EmitBytes(0x4C, 0x29, 0xC8); // SUB rax, r9
    EmitMovMemReg(-0x48, X86Register.Rax, 8);
    EmitJmp("rt_i64fmt_apply_padding");

    // ---- Binary path ----
    DefineLabel("rt_i64fmt_binary");
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // no sign for binary
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);

    EmitMovRegMem(X86Register.R9, -0x10, 8);
    EmitAddRegImm(X86Register.R9, 64);
    EmitBytes(0x41, 0xC6, 0x01, 0x00);

    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_i64fmt_bin_loop");
    EmitBytes(0x49, 0xFF, 0xC9);
    EmitBytes(0x41, 0xC6, 0x01, 0x30); // '0'
    EmitJmp("rt_i64fmt_bin_done");

    DefineLabel("rt_i64fmt_bin_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx);
    EmitBytes(0x48, 0x83, 0xE0, 0x01); // AND rax, 1
    EmitAddRegImm(X86Register.Rax, 0x30); // '0' or '1'
    EmitBytes(0x41, 0x88, 0x01); // MOV byte [r9], al
    EmitBytes(0x48, 0xD1, 0xE9); // SHR rcx, 1
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("nz", "rt_i64fmt_bin_loop");

    DefineLabel("rt_i64fmt_bin_done");
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitAddRegImm(X86Register.Rax, 64);
    EmitBytes(0x4C, 0x29, 0xC8); // SUB rax, r9
    EmitMovMemReg(-0x48, X86Register.Rax, 8);
    EmitJmp("rt_i64fmt_apply_padding");

    // ---- Octal path ----
    DefineLabel("rt_i64fmt_octal");
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);

    EmitMovRegMem(X86Register.R9, -0x10, 8);
    EmitAddRegImm(X86Register.R9, 64);
    EmitBytes(0x41, 0xC6, 0x01, 0x00);

    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("nz", "rt_i64fmt_oct_loop");
    EmitBytes(0x49, 0xFF, 0xC9);
    EmitBytes(0x41, 0xC6, 0x01, 0x30);
    EmitJmp("rt_i64fmt_oct_done");

    DefineLabel("rt_i64fmt_oct_loop");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx);
    EmitBytes(0x48, 0x83, 0xE0, 0x07); // AND rax, 7
    EmitAddRegImm(X86Register.Rax, 0x30);
    EmitBytes(0x41, 0x88, 0x01); // MOV byte [r9], al
    EmitBytes(0x48, 0xC1, 0xE9, 0x03); // SHR rcx, 3
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("nz", "rt_i64fmt_oct_loop");

    DefineLabel("rt_i64fmt_oct_done");
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitAddRegImm(X86Register.Rax, 64);
    EmitBytes(0x4C, 0x29, 0xC8);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);
    // Fall through to apply_padding

    // ---- Apply padding and assemble final result ----
    // R9 = pointer to first digit in temp area
    // [rbp-0x48] = digit_count
    // [rbp-0x28] = is_negative (0 or 1)
    // [rbp-0x30] = fill char
    // [rbp-0x38] = min_width
    DefineLabel("rt_i64fmt_apply_padding");

    // sign_len = is_negative
    EmitMovRegMem(X86Register.R8, -0x28, 8); // R8 = sign_len (0 or 1)
    EmitMovRegMem(X86Register.Rcx, -0x48, 8); // RCX = digit_count
    EmitMovRegMem(X86Register.Rdx, -0x38, 8); // RDX = min_width

    // content_len = sign_len + digit_count
    EmitMovRegReg(X86Register.Rax, X86Register.R8);
    EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx => content_len

    // pad_count = max(0, min_width - content_len)
    EmitMovRegReg(X86Register.Rdi, X86Register.Rdx); // RDI = min_width
    EmitBytes(0x48, 0x29, 0xC7); // SUB rdi, rax => pad_count (may be negative)
    // If pad_count <= 0, set to 0
    EmitBytes(0x48, 0x85, 0xFF); // TEST rdi, rdi
    EmitJcc("g", "rt_i64fmt_has_padding");
    EmitMovRegImm(X86Register.Rdi, 0);
    DefineLabel("rt_i64fmt_has_padding");

    // total_len = content_len + pad_count
    // Now write to buffer: [sign] [pad_chars] [digits]
    // If fill is '0', sign comes first, then pad, then digits
    // If fill is ' ', pad comes first, then sign, then digits
    EmitMovRegMem(X86Register.Rsi, -0x10, 8); // RSI = buffer (write position)

    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = fill char
    EmitBytes(0x48, 0x83, 0xF8, 0x30); // CMP rax, '0'
    EmitJcc("z", "rt_i64fmt_zero_fill");

    // ---- Space fill: [spaces] [sign] [digits] ----
    // Write pad_count spaces
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi); // RCX = pad_count
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "rt_i64fmt_space_sign");
    DefineLabel("rt_i64fmt_space_loop");
    EmitBytes(0xC6, 0x06, 0x20); // MOV byte [rsi], ' '
    EmitBytes(0x48, 0xFF, 0xC6); // INC rsi
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    EmitJcc("nz", "rt_i64fmt_space_loop");

    DefineLabel("rt_i64fmt_space_sign");
    // Write sign if negative
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_i64fmt_copy_digits");
    EmitBytes(0xC6, 0x06, 0x2D); // MOV byte [rsi], '-'
    EmitBytes(0x48, 0xFF, 0xC6); // INC rsi
    EmitJmp("rt_i64fmt_copy_digits");

    // ---- Zero fill: [sign] [zeros] [digits] ----
    DefineLabel("rt_i64fmt_zero_fill");
    // Write sign if negative
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_i64fmt_write_zeros");
    EmitBytes(0xC6, 0x06, 0x2D); // MOV byte [rsi], '-'
    EmitBytes(0x48, 0xFF, 0xC6); // INC rsi

    DefineLabel("rt_i64fmt_write_zeros");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi); // RCX = pad_count
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("z", "rt_i64fmt_copy_digits");
    DefineLabel("rt_i64fmt_zero_loop");
    EmitBytes(0xC6, 0x06, 0x30); // MOV byte [rsi], '0'
    EmitBytes(0x48, 0xFF, 0xC6); // INC rsi
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    EmitJcc("nz", "rt_i64fmt_zero_loop");

    // ---- Copy digits from R9 to RSI ----
    DefineLabel("rt_i64fmt_copy_digits");
    // RDI = RSI (dest), RSI = R9 (src), but REP MOVSB uses RSI→RDI
    // Save current write pos
    EmitMovMemReg(-0x50, X86Register.Rsi, 8);
    // Set up REP MOVSB: RSI = src (R9), RDI = dest (saved write pos), RCX = count
    EmitBytes(0x4C, 0x89, 0xCE); // MOV rsi, r9
    EmitMovRegMem(X86Register.Rdi, -0x50, 8);
    EmitMovRegMem(X86Register.Rcx, -0x48, 8); // digit_count
    EmitBytes(0xF3, 0xA4); // REP MOVSB

    // Null-terminate: RDI now points past the last copied byte
    EmitBytes(0xC6, 0x07, 0x00); // MOV byte [rdi], 0

    // Compute total length = RDI - buffer
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // buffer
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitBytes(0x48, 0x29, 0xC1); // SUB rcx, rax
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx);

    DefineLabel("rt_i64fmt_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_u64_to_string_fmt(value_in_rcx, buffer_ptr_in_rdx, fmt_ptr_in_r8, fmt_len_in_r9) -> length_in_rax
  /// Unsigned variant — tail-calls i64_fmt. Safe because current unsigned types (byte, u16) are
  /// zero-extended to 64-bit, so the sign bit is never set and i64_fmt's sign check is a no-op.
  /// </summary>
  private void EmitMaxonU64ToStringFmt() {
    // Tail-call into the signed variant — the i64_fmt sign check (TEST rcx,rcx / JNS)
    // skips negation when the sign bit is clear, which is always true for our unsigned types.
    EmitRuntimeFunctionStart("maxon_u64_to_string_fmt", 4, 0x80);

    // Reload args and tear down frame before jumping into the i64 variant
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegMem(X86Register.R8, -0x18, 8);
    EmitMovRegMem(X86Register.R9, -0x20, 8);
    // Tear down our frame and tail-call i64 variant
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xE9); _relCallFixups.Add((_code.Count, "maxon_i64_to_string_fmt")); EmitDword(0);
  }

  /// <summary>
  /// maxon_f64_to_string_fmt(value_in_xmm0, buffer_ptr_in_rdx, fmt_ptr_in_r8, fmt_len_in_r9) -> length_in_rax
  /// Converts a float64 to a formatted string representation.
  /// Format specifiers: [width][.precision] — e.g., ".2" (2 decimal places), "8.2" (width 8, 2 dp)
  /// Buffer must be >= 72 bytes. Returns byte count (excluding null terminator).
  /// Stack layout:
  ///   [rbp-0x08] = float value (overwritten from XMM0)
  ///   [rbp-0x10] = buffer pointer
  ///   [rbp-0x18] = fmt_ptr
  ///   [rbp-0x20] = fmt_len
  ///   [rbp-0x28] = is_negative
  ///   [rbp-0x30] = min_width
  ///   [rbp-0x38] = precision (default 6, -1 = not set)
  ///   [rbp-0x40] = integer part (i64)
  ///   [rbp-0x48] = integer part length (after i64_to_string call)
  ///   [rbp-0x50] = scratch / write position
  ///   [rbp-0x58] = fill char ('0' or ' ')
  /// </summary>
  private void EmitMaxonF64ToStringFmt() {
    EmitRuntimeFunctionStart("maxon_f64_to_string_fmt", 4, 0xC0);

    // Overwrite [rbp-8] with the actual float value from XMM0
    EmitMovMemXmm(-0x08, X86XmmRegister.Xmm0, FloatPrecision.F64);

    // ---- Parse format string: [0][width][.precision] ----
    // Defaults
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // width = 0
    EmitMovRegImm(X86Register.Rax, -1);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // precision = -1 (not specified)
    EmitMovRegImm(X86Register.Rax, ' ');
    EmitMovMemReg(-0x58, X86Register.Rax, 8); // fill = ' '

    // If fmt_len == 0, use default formatting (delegates to maxon_f64_to_string)
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("z", "rt_f64fmt_default");

    EmitMovRegMem(X86Register.Rsi, -0x18, 8); // RSI = fmt_ptr
    EmitMovRegImm(X86Register.Rcx, 0); // parse index

    // Check for '0' fill
    EmitBytes(0x8A, 0x06); // MOV AL, [RSI]
    EmitBytes(0x3C, 0x30); // CMP AL, '0'
    EmitJcc("nz", "rt_f64fmt_parse_width");
    // Check next char — if it's a '.', then '0' is not a fill char, it's just before precision
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // fmt_len
    EmitBytes(0x48, 0x83, 0xFA, 0x01); // CMP rdx, 1
    EmitJcc("le", "rt_f64fmt_parse_width"); // only "0" — treat as width 0
    EmitBytes(0x8A, 0x46, 0x01); // MOV AL, [RSI+1]
    EmitBytes(0x3C, 0x2E); // CMP AL, '.'
    EmitJcc("z", "rt_f64fmt_parse_width"); // "0." means precision, not fill
    // It's a fill char
    EmitMovRegImm(X86Register.Rax, '0');
    EmitMovMemReg(-0x58, X86Register.Rax, 8);
    EmitBytes(0x48, 0xFF, 0xC6); // INC RSI
    EmitBytes(0x48, 0xFF, 0xC1); // INC RCX

    // Parse width digits
    DefineLabel("rt_f64fmt_parse_width");
    EmitMovMemReg(-0x50, X86Register.Rcx, 8); // save parse index
    EmitMovRegImm(X86Register.Rdi, 0); // accumulated width

    DefineLabel("rt_f64fmt_width_loop");
    EmitMovRegMem(X86Register.Rcx, -0x50, 8);
    EmitMovRegMem(X86Register.Rdx, -0x20, 8);
    EmitBytes(0x48, 0x39, 0xD1); // CMP rcx, rdx
    EmitJcc("ge", "rt_f64fmt_width_done");
    EmitBytes(0x8A, 0x06); // MOV AL, [RSI]
    EmitBytes(0x3C, 0x2E); // CMP AL, '.'
    EmitJcc("z", "rt_f64fmt_dot");
    EmitBytes(0x3C, 0x30); // CMP AL, '0'
    EmitJcc("l", "rt_f64fmt_width_done");
    EmitBytes(0x3C, 0x39); // CMP AL, '9'
    EmitJcc("g", "rt_f64fmt_width_done");
    // digit
    EmitBytes(0x0F, 0xB6, 0xC0); // MOVZX EAX, AL
    EmitBytes(0x48, 0x83, 0xE8, 0x30); // SUB rax, '0'
    EmitBytes(0x48, 0x6B, 0xFF, 0x0A); // IMUL rdi, rdi, 10
    EmitBytes(0x48, 0x01, 0xC7); // ADD rdi, rax
    EmitBytes(0x48, 0xFF, 0xC6); // INC RSI
    EmitMovRegMem(X86Register.Rcx, -0x50, 8);
    EmitBytes(0x48, 0xFF, 0xC1);
    EmitMovMemReg(-0x50, X86Register.Rcx, 8);
    EmitJmp("rt_f64fmt_width_loop");

    DefineLabel("rt_f64fmt_dot");
    EmitMovMemReg(-0x30, X86Register.Rdi, 8); // save width
    EmitBytes(0x48, 0xFF, 0xC6); // INC RSI (skip '.')
    EmitMovRegMem(X86Register.Rcx, -0x50, 8);
    EmitBytes(0x48, 0xFF, 0xC1);
    EmitMovMemReg(-0x50, X86Register.Rcx, 8);

    // Parse precision digits
    EmitMovRegImm(X86Register.Rdi, 0); // accumulated precision
    DefineLabel("rt_f64fmt_prec_loop");
    EmitMovRegMem(X86Register.Rcx, -0x50, 8);
    EmitMovRegMem(X86Register.Rdx, -0x20, 8);
    EmitBytes(0x48, 0x39, 0xD1);
    EmitJcc("ge", "rt_f64fmt_prec_done");
    EmitBytes(0x8A, 0x06); // MOV AL, [RSI]
    EmitBytes(0x3C, 0x30);
    EmitJcc("l", "rt_f64fmt_prec_done");
    EmitBytes(0x3C, 0x39);
    EmitJcc("g", "rt_f64fmt_prec_done");
    EmitBytes(0x0F, 0xB6, 0xC0);
    EmitBytes(0x48, 0x83, 0xE8, 0x30);
    EmitBytes(0x48, 0x6B, 0xFF, 0x0A);
    EmitBytes(0x48, 0x01, 0xC7);
    EmitBytes(0x48, 0xFF, 0xC6);
    EmitMovRegMem(X86Register.Rcx, -0x50, 8);
    EmitBytes(0x48, 0xFF, 0xC1);
    EmitMovMemReg(-0x50, X86Register.Rcx, 8);
    EmitJmp("rt_f64fmt_prec_loop");

    DefineLabel("rt_f64fmt_prec_done");
    EmitMovMemReg(-0x38, X86Register.Rdi, 8); // precision
    EmitJmp("rt_f64fmt_convert");

    DefineLabel("rt_f64fmt_width_done");
    EmitMovMemReg(-0x30, X86Register.Rdi, 8); // width

    // ---- Convert ----
    DefineLabel("rt_f64fmt_convert");
    // If precision == -1 and width == 0, delegate to default f64_to_string
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitBytes(0x48, 0x83, 0xF8, 0xFF); // CMP rax, -1 (sign-extended byte)
    EmitJcc("nz", "rt_f64fmt_has_precision");
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("nz", "rt_f64fmt_has_width_no_prec");

    // No precision, no width — use default
    DefineLabel("rt_f64fmt_default");
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_f64_to_string")); EmitDword(0);
    EmitJmp("rt_f64fmt_epilogue");

    // Has width but no precision — use default formatting, then pad
    DefineLabel("rt_f64fmt_has_width_no_prec");
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_f64_to_string")); EmitDword(0);
    // RAX = content_len from default conversion
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // save content_len
    EmitJmp("rt_f64fmt_apply_width");

    DefineLabel("rt_f64fmt_has_precision");
    // If precision > 20, clamp to 20
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitBytes(0x48, 0x83, 0xF8, 0x14); // CMP rax, 20
    EmitJcc("le", "rt_f64fmt_prec_ok");
    EmitMovRegImm(X86Register.Rax, 20);
    EmitMovMemReg(-0x38, X86Register.Rax, 8);
    DefineLabel("rt_f64fmt_prec_ok");

    // ---- Handle sign ----
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // is_negative = 0

    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64);
    EmitBytes(0x66, 0x0F, 0x57, 0xC9); // XORPD xmm1, xmm1 (zero)
    EmitUcomisXmm(X86XmmRegister.Xmm1, X86XmmRegister.Xmm0, FloatPrecision.F64);
    EmitJcc("be", "rt_f64fmt_positive");
    // Negative: negate
    EmitSubXmm(X86XmmRegister.Xmm1, X86XmmRegister.Xmm0, FloatPrecision.F64);
    EmitMovMemXmm(-0x08, X86XmmRegister.Xmm1, FloatPrecision.F64);
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    DefineLabel("rt_f64fmt_positive");
    // ---- Extract integer part ----
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64);

    // Rounding: add 0.5 * 10^-precision to handle cases like 3.14159 with .2 → 3.14
    // We need proper rounding: frac * 10^precision, + 0.5, truncate
    // First get integer part
    EmitCvttFloat2Si(X86Register.Rax, X86XmmRegister.Xmm0, FloatPrecision.F64);
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // integer part

    // Write integer part to buffer (offset by 1 if negative for '-' prefix)
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitBytes(0x48, 0x01, 0xCA); // ADD rdx, rcx
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // int_part_len

    // Write '-' if negative
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("z", "rt_f64fmt_no_sign");
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitBytes(0xC6, 0x00, 0x2D); // MOV byte [rax], '-'
    // Adjust int_part_len to include sign
    EmitMovRegMem(X86Register.Rax, -0x48, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);

    DefineLabel("rt_f64fmt_no_sign");
    // ---- Handle precision 0: no decimal point ----
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "rt_f64fmt_has_frac");
    // Just the integer part, null-terminate
    EmitMovRegMem(X86Register.Rdi, -0x10, 8);
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitBytes(0x48, 0x01, 0xCF); // ADD rdi, rcx
    EmitBytes(0xC6, 0x07, 0x00); // null-terminate
    EmitMovRegMem(X86Register.Rax, -0x48, 8); // content_len = int_part_len
    EmitMovMemReg(-0x48, X86Register.Rax, 8);
    EmitJmp("rt_f64fmt_apply_width");

    DefineLabel("rt_f64fmt_has_frac");
    // Write '.' after integer part
    EmitMovRegMem(X86Register.Rdi, -0x10, 8);
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitBytes(0x48, 0x01, 0xCF); // ADD rdi, rcx
    EmitBytes(0xC6, 0x07, 0x2E); // MOV byte [rdi], '.'
    EmitBytes(0x48, 0xFF, 0xC7); // INC rdi

    // ---- Compute fractional part: (|value| - int_part) * 10^precision + 0.5, truncate ----
    EmitMovXmmMem(X86XmmRegister.Xmm0, -0x08, FloatPrecision.F64); // |value|
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitCvtSi2Float(X86XmmRegister.Xmm1, X86Register.Rax, FloatPrecision.F64);
    EmitSubXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64); // XMM0 = frac part

    // Multiply by 10^precision: loop precision times, multiply by 10
    EmitMovRegImm(X86Register.Rax, BitConverter.DoubleToInt64Bits(10.0));
    EmitBytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // MOVQ xmm1, rax
    EmitMovRegMem(X86Register.Rcx, -0x38, 8); // precision

    DefineLabel("rt_f64fmt_mul_loop");
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("z", "rt_f64fmt_mul_done");
    EmitMulXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64);
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    EmitJmp("rt_f64fmt_mul_loop");

    DefineLabel("rt_f64fmt_mul_done");
    // Add 0.5 for rounding
    EmitMovRegImm(X86Register.Rax, BitConverter.DoubleToInt64Bits(0.5));
    EmitBytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // MOVQ xmm1, rax
    EmitAddXmm(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, FloatPrecision.F64);
    EmitCvttFloat2Si(X86Register.Rax, X86XmmRegister.Xmm0, FloatPrecision.F64); // RAX = frac digits

    // Write precision digits (right-to-left then place)
    // We write them backwards into a temp area, then copy forward
    // Use [rbp-0x80..0x80+20] as temp area (buffer+60..80 area is safe since buffer >= 72)
    EmitMovMemReg(-0x50, X86Register.Rdi, 8); // save write pos (after '.')
    EmitMovRegMem(X86Register.Rcx, -0x38, 8); // precision
    EmitMovMemReg(-0x60, X86Register.Rcx, 8); // save precision loop counter

    // Write digits from LSB to MSB at [rdi+precision-1] down to [rdi]
    DefineLabel("rt_f64fmt_frac_write");
    EmitMovRegMem(X86Register.Rcx, -0x60, 8);
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("z", "rt_f64fmt_frac_written");
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    EmitMovMemReg(-0x60, X86Register.Rcx, 8);
    // Divide RAX by 10
    EmitBytes(0x48, 0x31, 0xD2); // XOR rdx, rdx
    EmitMovRegImm(X86Register.R8, 10);
    EmitBytes(0x49, 0xF7, 0xF0); // DIV r8
    EmitAddRegImm(X86Register.Rdx, 0x30); // remainder + '0'
    // Write at [rdi + rcx]
    EmitMovRegMem(X86Register.Rdi, -0x50, 8);
    EmitMovRegMem(X86Register.Rcx, -0x60, 8);
    EmitBytes(0x88, 0x14, 0x0F); // MOV byte [rdi+rcx], dl
    EmitJmp("rt_f64fmt_frac_write");

    DefineLabel("rt_f64fmt_frac_written");
    // Null-terminate after all precision digits
    EmitMovRegMem(X86Register.Rdi, -0x50, 8);
    EmitMovRegMem(X86Register.Rcx, -0x38, 8); // precision
    EmitBytes(0x48, 0x01, 0xCF); // ADD rdi, rcx
    EmitBytes(0xC6, 0x07, 0x00); // null-terminate

    // content_len = int_part_len + 1 (dot) + precision
    EmitMovRegMem(X86Register.Rax, -0x48, 8); // int_part_len
    EmitAddRegImm(X86Register.Rax, 1); // dot
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx
    EmitMovMemReg(-0x48, X86Register.Rax, 8); // content_len

    // ---- Apply width padding ----
    DefineLabel("rt_f64fmt_apply_width");
    EmitMovRegMem(X86Register.Rdx, -0x30, 8); // min_width
    EmitMovRegMem(X86Register.Rax, -0x48, 8); // content_len
    EmitBytes(0x48, 0x39, 0xC2); // CMP rdx, rax
    EmitJcc("le", "rt_f64fmt_no_pad"); // width <= content_len, no padding needed

    // pad_count = width - content_len
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitBytes(0x48, 0x29, 0xC1); // SUB rcx, rax => pad_count

    // Shift existing content right by pad_count
    // First, compute new total length
    EmitMovRegReg(X86Register.R8, X86Register.Rdx); // R8 = total_len (= width)

    // Move content from buffer to buffer+pad_count (memmove right)
    // Work backwards to avoid overlap issues
    EmitMovRegMem(X86Register.Rsi, -0x10, 8); // buffer
    EmitMovRegMem(X86Register.Rdi, -0x48, 8); // content_len
    // src = buffer + content_len - 1, dst = buffer + width - 1, count = content_len
    EmitMovRegReg(X86Register.Rax, X86Register.Rsi);
    EmitMovRegMem(X86Register.Rdx, -0x48, 8);
    EmitBytes(0x48, 0x01, 0xD0); // ADD rax, rdx
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax => src_end = buffer + content_len - 1

    EmitMovRegReg(X86Register.Rdi, X86Register.Rsi);
    EmitBytes(0x4C, 0x01, 0xC7); // ADD rdi, r8
    EmitBytes(0x48, 0xFF, 0xCF); // DEC rdi => dst_end = buffer + width - 1

    EmitMovRegMem(X86Register.Rdx, -0x48, 8); // count = content_len

    DefineLabel("rt_f64fmt_shift_loop");
    EmitBytes(0x48, 0x85, 0xD2); // TEST rdx, rdx
    EmitJcc("z", "rt_f64fmt_shift_done");
    // Copy byte [rax] -> [rdi]
    EmitBytes(0x8A, 0x08); // MOV CL, [rax]
    EmitBytes(0x88, 0x0F); // MOV [rdi], CL
    EmitBytes(0x48, 0xFF, 0xC8); // DEC rax
    EmitBytes(0x48, 0xFF, 0xCF); // DEC rdi
    EmitBytes(0x48, 0xFF, 0xCA); // DEC rdx
    EmitJmp("rt_f64fmt_shift_loop");

    DefineLabel("rt_f64fmt_shift_done");
    // Fill padding area with fill char
    EmitMovRegMem(X86Register.Rsi, -0x10, 8); // buffer start
    // pad_count = width - content_len (recalculate since we clobbered regs)
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // width
    EmitMovRegMem(X86Register.Rax, -0x48, 8); // content_len
    EmitBytes(0x48, 0x29, 0xC1); // SUB rcx, rax => pad_count
    EmitMovRegMem(X86Register.Rax, -0x58, 8); // fill char

    DefineLabel("rt_f64fmt_fill_loop");
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "rt_f64fmt_fill_done");
    EmitBytes(0x88, 0x06); // MOV [rsi], al
    EmitBytes(0x48, 0xFF, 0xC6); // INC rsi
    EmitBytes(0x48, 0xFF, 0xC9); // DEC rcx
    EmitJmp("rt_f64fmt_fill_loop");

    DefineLabel("rt_f64fmt_fill_done");
    // Null-terminate at buffer + width
    EmitMovRegMem(X86Register.Rsi, -0x10, 8);
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // width
    EmitBytes(0x48, 0x01, 0xC6); // ADD rsi, rax
    EmitBytes(0xC6, 0x06, 0x00); // null-terminate
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // return width as length
    EmitJmp("rt_f64fmt_epilogue");

    DefineLabel("rt_f64fmt_no_pad");
    EmitMovRegMem(X86Register.Rax, -0x48, 8); // return content_len

    DefineLabel("rt_f64fmt_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Define a runtime function label, emit prologue, and save incoming args to sequential stack slots.
  /// Args are saved to [rbp-8], [rbp-16], ... following Windows x64 ABI order.
  /// </summary>
  private void EmitRuntimeFunctionStart(string name, int argCount, int stackSize = 0x30) {
    DefineLabel(name);
    _runtimeFunctionLabels.Add(name);
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, stackSize);
    for (int i = 0; i < argCount; i++)
      EmitMovMemReg(-(i + 1) * 0x08, _abiArgRegs[i], 8);
  }

  /// <summary>Emit the standard epilogue: mov rsp,rbp / pop rbp / ret.</summary>
  private void EmitRuntimeFunctionEnd() {
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
  }

  // ==========================================================================
  // Command line runtime functions
  // ==========================================================================

  /// <summary>
  /// maxon_command_line_count() -> int: returns total argc (including argv[0]).
  /// The stdlib skips index 0, so this returns the raw count from CommandLineToArgvW.
  /// Stack: [rbp-0x08] = argc output (4-byte int from CommandLineToArgvW),
  ///        [rbp-0x10] = argv pointer (for LocalFree),
  ///        [rbp-0x18] = saved argc (widened to 64-bit)
  /// </summary>
  private void EmitMaxonCommandLineCount() {
    EmitRuntimeFunctionStart("maxon_command_line_count", 0, 0x40);

    // Zero the argc slot so the upper 4 bytes are clean when we read it as 4 bytes
    EmitBytes(0x48, 0xC7, 0x45, 0xF8, 0x00, 0x00, 0x00, 0x00); // MOV qword [rbp-0x08], 0

    // Step 1: Get the raw command line wide string
    EmitCallImportOnSystemStack("kernel32.dll", "GetCommandLineW");

    // Step 2: Parse into argv array
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    // RDX = &argc (pointer to 4-byte int)
    EmitLeaRegMem(X86Register.Rdx, -0x08);
    EmitCallImportOnSystemStack("shell32.dll", "CommandLineToArgvW");

    // Step 3: Save argv pointer for LocalFree, and save argc before the call clobbers registers
    EmitMovMemReg(-0x10, X86Register.Rax, 8);
    // argc is a 32-bit int at [rbp-0x08]; load as 4 bytes (zero-extends to 64-bit)
    EmitMovRegMem(X86Register.Rax, -0x08, 4);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // Step 4: Free the argv array
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "LocalFree");

    // Step 5: Return saved argc
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_command_line_arg(index) -> cstring_ptr: returns a heap-allocated UTF-8 string.
  /// Calls GetCommandLineW + CommandLineToArgvW to get wide argv, indexes by the given
  /// index, converts to UTF-8 via WideCharToMultiByte, allocates via mm_alloc.
  /// Stack: [rbp-0x08]=index, [rbp-0x10]=wargv, [rbp-0x18]=wstr, [rbp-0x20]=bufsize,
  ///        [rbp-0x28]=buffer, [rbp-0x30]=argc (scratch for CommandLineToArgvW)
  /// </summary>
  private void EmitMaxonCommandLineArg() {
    EmitRuntimeFunctionStart("maxon_command_line_arg", 1, 0x80);

    // Step 1: Get the raw command line wide string
    EmitCallImportOnSystemStack("kernel32.dll", "GetCommandLineW");

    // Step 2: Parse into argv array
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    // RDX = &argc -> LEA RDX, [rbp-0x30]
    EmitLeaRegMem(X86Register.Rdx, -0x30);
    EmitCallImportOnSystemStack("shell32.dll", "CommandLineToArgvW");

    // Step 3: Save wargv pointer
    EmitMovMemReg(-0x10, X86Register.Rax, 8);

    // Step 3b: Bounds check — if index < 0 or index >= argc, return empty string
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);           // index (signed 64-bit)
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("s", "rt_cla_oob");                          // index < 0 → out of bounds
    EmitMovRegMem(X86Register.Rdx, -0x30, 4);           // argc (32-bit, zero-extended)
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("b", "rt_cla_inbounds");                     // index < argc → in bounds
    DefineLabel("rt_cla_oob");
    // Free wargv before returning
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "LocalFree");
    // Allocate 1-byte buffer with null terminator (freed via mm_raw_free)
    EmitMovRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_cmdline_arg");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitBytes(0xC6, 0x00, 0x00);                         // MOV byte [RAX], 0
    EmitJmp("rt_cla_return");
    DefineLabel("rt_cla_inbounds");

    // Step 4: Get wargv[index] — the wide string pointer
    // Load wargv into RAX, index into RCX
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    // MOV RAX, [RAX + RCX*8]: REX.W 8B 04 C8
    EmitBytes(0x48, 0x8B, 0x04, 0xC8);
    // Save wide string pointer
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // Step 5: Get required UTF-8 buffer size
    // WideCharToMultiByte(CP_UTF8=65001, 0, wideStr, -1, NULL, 0, NULL, NULL)
    // 8 args: RCX, RDX, R8, R9, [rsp+0x20], [rsp+0x28], [rsp+0x30], [rsp+0x38]
    EmitSystemStackEnter(0x40); // shadow(0x20) + 4 stack args(0x20) = 0x40
    EmitMovRegImm(X86Register.Rcx, 65001);           // arg1: CP_UTF8
    EmitMovRegImm(X86Register.Rdx, 0);               // arg2: flags = 0
    EmitMovRegMem(X86Register.R8, -0x18, 8);          // arg3: wide string pointer
    EmitMovRegImm(X86Register.R9, -1);                // arg4: -1 (null-terminated)
    // arg5: NULL (output buffer) at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    // arg6: 0 (output buffer size) at [rsp+0x28]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    // arg7: NULL at [rsp+0x30]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00);
    // arg8: NULL at [rsp+0x38]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x38, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WideCharToMultiByte");
    EmitSystemStackLeave(0x40);

    // Save required size
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // Step 6: Allocate buffer via mm_raw_alloc (no header/canary — freed via mm_raw_free)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_cmdline_arg");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);

    // Save buffer pointer
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    // Step 7: Convert wide string to UTF-8 into the allocated buffer
    // WideCharToMultiByte(CP_UTF8, 0, wideStr, -1, buffer, size, NULL, NULL)
    EmitSystemStackEnter(0x40); // shadow(0x20) + 4 stack args(0x20) = 0x40
    EmitMovRegImm(X86Register.Rcx, 65001);           // arg1: CP_UTF8
    EmitMovRegImm(X86Register.Rdx, 0);               // arg2: flags = 0
    EmitMovRegMem(X86Register.R8, -0x18, 8);          // arg3: wide string pointer
    EmitMovRegImm(X86Register.R9, -1);                // arg4: -1 (null-terminated)
    // arg5: buffer at [rsp+0x20]
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);         // MOV [rsp+0x20], RAX
    // arg6: size at [rsp+0x28]
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x28);         // MOV [rsp+0x28], RAX
    // arg7: NULL at [rsp+0x30]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00);
    // arg8: NULL at [rsp+0x38]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x38, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WideCharToMultiByte");
    EmitSystemStackLeave(0x40);

    // Step 8: Free the argv array from CommandLineToArgvW
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "LocalFree");

    // Step 9: Return buffer pointer
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    DefineLabel("rt_cla_return");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_executable_path() -> cstring_ptr: returns a heap-allocated UTF-8 path string.
  /// Calls GetModuleFileNameA(NULL, buffer, nSize) in a loop, doubling the buffer when
  /// the returned length == nSize (indicating truncation). Handles arbitrarily long paths.
  /// Stack: [rbp-0x08] = heap buffer pointer, [rbp-0x10] = current buffer size
  /// </summary>
  private void EmitMaxonExecutablePath() {
    EmitRuntimeFunctionStart("maxon_executable_path", 0, 0x40);

    // Start with 512-byte buffer
    EmitMovRegImm(X86Register.Rax, 512);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = bufSize = 512

    // Allocate initial buffer
    EmitMovRegImm(X86Register.Rcx, 512);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_exe_path");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // [rbp-8] = buffer

    // Retry loop
    DefineLabel("rt_exepath_retry");

    // GetModuleFileNameA(hModule=NULL, lpFilename=buffer, nSize=bufSize)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);      // arg1: NULL (current module)
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);              // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x10, 8);               // arg3: buffer size
    EmitCallImportOnSystemStack("kernel32.dll", "GetModuleFileNameA");
    // RAX = number of chars written (not including null), or nSize if truncated

    // If returned length < bufSize, we're done
    EmitCmpRegMem(X86Register.Rax, -0x10);
    EmitJcc("b", "rt_exepath_done");                        // RAX < bufSize → success

    // Truncated: free old buffer, double size, allocate new, retry
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // Double the buffer size
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitBytes(0x48, 0xD1, 0xE0);                           // SHL RAX, 1
    EmitMovMemReg(-0x10, X86Register.Rax, 8);              // save new size

    // Allocate larger buffer
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_exe_path");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitMovMemReg(-0x08, X86Register.Rax, 8);              // save new buffer
    EmitJmp("rt_exepath_retry");

    DefineLabel("rt_exepath_done");
    // Return buffer pointer (null-terminated by GetModuleFileNameA)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // File I/O runtime stubs
  // ==========================================================================

  /// <summary>
  /// maxon_file_size(handle) -> file size in bytes
  /// GetFileSizeEx(handle, &size) - size is a 64-bit value
  /// Stack: [rbp-8]=handle, [rbp-16]=size (8 bytes for LARGE_INTEGER)
  /// </summary>
  private void EmitMaxonFileSize() {
    EmitRuntimeFunctionStart("maxon_file_size", 1, 0x40);
    // Zero the size slot
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: hFile
    // LEA RDX, [rbp-0x10] (arg2: &size)
    EmitBytes(0x48, 0x8D, 0x55, 0xF0);
    EmitCallImportOnSystemStack("kernel32.dll", "GetFileSizeEx");
    EmitMovRegMem(X86Register.Rax, -0x10, 8);         // return size
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_read(handle, buffer_ptr, size, capacity) -> bytes read
  /// Clamps size to capacity, then calls __io_submit_read(handle, buffer, clampedSize).
  /// Stack: [rbp-8]=handle, [rbp-16]=buffer, [rbp-24]=size, [rbp-32]=capacity
  /// </summary>
  private void EmitMaxonFileRead() {
    EmitRuntimeFunctionStart("maxon_file_read", 4, 0x40);
    // Clamp size to capacity: if size > capacity, use capacity
    EmitMovRegMem(X86Register.R8, -0x18, 8);           // size
    EmitMovRegMem(X86Register.Rax, -0x20, 8);          // capacity
    EmitCmpRegReg(X86Register.R8, X86Register.Rax);
    EmitJcc("be", "rt_fread_ok");                       // size <= capacity → no clamp
    EmitMovRegReg(X86Register.R8, X86Register.Rax);    // clamp: size = capacity
    DefineLabel("rt_fread_ok");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);         // arg2: buffer
    // R8 already has clamped size                      // arg3: size
    EmitCallRuntimeLabel("__io_submit_read");
    // RAX = bytes read (or 0 on error; error code in gt->io_error_code)
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_close(__ManagedFile*) -> void
  /// Loads _handle from [ptr+0], atomically (on the owning GT) zeros the field,
  /// then routes CloseHandle through the sync worker. Making this the single
  /// point that clears _handle ensures the destructor's own idempotency check
  /// sees a zeroed field and skips a second close. Idempotent: no-op if the
  /// struct's handle field is zero.
  /// Stack: [rbp-8]=managedFilePtr
  /// </summary>
  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);          // RAX = __ManagedFile*
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_fc_noop");                         // null ptr → no-op
    EmitBytes(0x48, 0x8B, 0x10);                        // MOV RDX, [RAX] = _handle
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_fc_noop");                         // already closed
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitBytes(0x48, 0x89, 0x08);                        // MOV [RAX], RCX = 0
    EmitMovRegImm(X86Register.Rcx, SyncOpCloseHandle);  // op = 9
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    DefineLabel("rt_fc_noop");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_delete(cstring_path) -> 0 on success, non-zero on failure
  /// Delegates to __io_submit_sync(SyncOpFileDelete, path, 0).
  /// Sync worker returns DeleteFileA result (non-zero=success), so we invert.
  /// Stack: [rbp-8]=path
  /// </summary>
  private void EmitMaxonFileDelete() {
    EmitRuntimeFunctionStart("maxon_file_delete", 1, 0x20);
    EmitMovRegImm(X86Register.Rcx, SyncOpFileDelete);   // op = 1
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);           // arg0 = path
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = DeleteFileA result (non-zero = success, zero = failure)
    // Invert: 0 = success, non-zero = failure
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitSetcc("z", X86Register.Rax);                   // AL = 1 if failed
    EmitMovzxReg8To64(X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Directory runtime functions
  // ==========================================================================

  // Layout of the find-file block allocated on the heap:
  //   [0..7]   = Win32 HANDLE from FindFirstFileA
  //   [8..327] = WIN32_FIND_DATAA (320 bytes)
  //              +0  (offset 8):  dwFileAttributes (DWORD)
  //              +44 (offset 52): cFileName (char[260])
  // Total block size: 328 bytes
  private const int FindBlockSize = 328;
  private const int FindBlockHandleOffset = 0;
  private const int FindBlockFindDataOffset = 8;
  private const int FindDataFileNameOffset = 44;

  /// <summary>
  /// maxon_find_filename(block_ptr) -> cstring pointer to cFileName in the block
  /// Returns block_ptr + 8 + 44 = block_ptr + 52, or empty string if block_ptr is null.
  /// </summary>
  private void EmitMaxonFindFilename() {
    EmitRuntimeFunctionStart("maxon_find_filename", 1);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // block_ptr
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_ffn_valid");
    // Null block_ptr: return pointer to static empty string
    EmitLeaRegSymdataRel(X86Register.Rax, "__rt_empty_cstring");
    EmitJmp("rt_ffn_done");
    DefineLabel("rt_ffn_valid");
    EmitAddRegImm(X86Register.Rax, FindBlockFindDataOffset + FindDataFileNameOffset);
    DefineLabel("rt_ffn_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_find_next_file(block_ptr) -> non-zero if found, 0 if done
  /// Delegates to __io_submit_sync(SyncOpFindNext, handle, findData_ptr).
  /// Returns 0 if block_ptr is null.
  /// Stack: [rbp-8]=block_ptr
  /// </summary>
  private void EmitMaxonFindNextFile() {
    EmitRuntimeFunctionStart("maxon_find_next_file", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // block_ptr
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_fnf_valid");
    // Null block_ptr: return 0 (no more files)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("rt_fnf_done");
    DefineLabel("rt_fnf_valid");
    EmitMovRegImm(X86Register.Rcx, SyncOpFindNext);     // op = 3
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, FindBlockHandleOffset); // arg0 = handle
    EmitMovRegMem(X86Register.R8, -0x08, 8);             // block_ptr
    EmitAddRegImm(X86Register.R8, FindBlockFindDataOffset); // arg1 = &findData
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = non-zero if found, 0 if no more files
    DefineLabel("rt_fnf_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_directory_exists(cstring_path) -> 1 if directory, 0 otherwise
  /// Delegates to __io_submit_sync(SyncOpDirExists, path, 0) which returns 0 or 1.
  /// Stack: [rbp-8]=path
  /// </summary>
  private void EmitMaxonDirectoryExists() {
    EmitRuntimeFunctionStart("maxon_directory_exists", 1, 0x20);
    EmitMovRegImm(X86Register.Rcx, SyncOpDirExists);    // op = 4
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);           // arg0 = path
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = 0 or 1 from sync worker
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_create_directory(cstring_path) -> nonzero on success, 0 on failure
  /// Delegates to __io_submit_sync(SyncOpDirCreate, path, 0).
  /// </summary>
  private void EmitMaxonCreateDirectory() {
    EmitRuntimeFunctionStart("maxon_create_directory", 1, 0x20);
    EmitMovRegImm(X86Register.Rcx, SyncOpDirCreate);    // op = 5
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);           // arg0 = path
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = nonzero on success, 0 on failure
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_get_current_directory() -> cstring pointer (heap-allocated, freed by caller via mm_raw_free).
  /// Returns 0 on failure so the caller's sentinel-error path triggers.
  ///
  /// Calls GetCurrentDirectoryA(nBufferLength, lpBuffer) per Win32 docs:
  /// - Returns chars-written (excluding null) if it fit, ret < nBufferLength.
  /// - Returns required size INCLUDING null if buffer too small, ret >= nBufferLength.
  ///   In that case lpBuffer contents are undefined → must grow and retry.
  /// - Returns 0 on failure (e.g. cwd was deleted) → free buffer, return 0 sentinel.
  /// Stack: [rbp-0x08] = heap buffer pointer, [rbp-0x10] = current buffer size
  /// </summary>
  private void EmitMaxonGetCurrentDirectory() {
    EmitRuntimeFunctionStart("maxon_get_current_directory", 0, 0x40);

    // Start with MAX_PATH (260) buffer
    EmitMovRegImm(X86Register.Rax, 260);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = bufSize

    EmitMovRegImm(X86Register.Rcx, 260);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_get_cwd");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // [rbp-8] = buffer

    DefineLabel("rt_getcwd_retry");

    // GetCurrentDirectoryA(nBufferLength=bufSize, lpBuffer=buffer)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "GetCurrentDirectoryA");

    // RAX = 0 → failure: free buffer, return 0
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("e", "rt_getcwd_failure");

    // RAX < bufSize → success (buffer was large enough; null terminator written)
    EmitCmpRegMem(X86Register.Rax, -0x10);
    EmitJcc("b", "rt_getcwd_done");

    // RAX >= bufSize → buffer too small (RAX is required size including null).
    // Free old buffer, allocate the requested size, retry.
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // bufSize = required size from RAX
    EmitMovMemReg(-0x10, X86Register.Rax, 8);

    // Allocate larger buffer
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__mm_scope_get_cwd");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitMovMemReg(-0x08, X86Register.Rax, 8);
    EmitJmp("rt_getcwd_retry");

    DefineLabel("rt_getcwd_failure");
    // Free the buffer and return 0 sentinel
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();

    DefineLabel("rt_getcwd_done");
    // Return buffer pointer
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>Emit MOV dword [rbp+disp], imm32 with correct disp8/disp32 encoding.</summary>
  private void EmitMovMemDwordImm(int displacement, int imm32) {
    EmitByte(0xC7);
    if (displacement >= -128 && displacement <= 127) {
      EmitByte(0x45); // mod=01, /0, r/m=rbp
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte(0x85); // mod=10, /0, r/m=rbp
      EmitDword(displacement);
    }
    EmitDword(imm32);
  }

  /// <summary>Emit MOV word [rbp+disp], imm16 with correct disp8/disp32 encoding.</summary>
  private void EmitMovMemWordImm(int displacement, ushort imm16) {
    EmitByte(0x66); // operand-size prefix → 16-bit
    EmitByte(0xC7);
    if (displacement >= -128 && displacement <= 127) {
      EmitByte(0x45);
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte(0x85);
      EmitDword(displacement);
    }
    EmitByte((byte)(imm16 & 0xFF));
    EmitByte((byte)((imm16 >> 8) & 0xFF));
  }

  // ==========================================================================
  // String utility runtime functions
  // ==========================================================================

  /// <summary>maxon_strlen(cstr_ptr) -> length</summary>
  private void EmitMaxonStrlen() {
    EmitRuntimeFunctionStart("maxon_strlen", 1);
    // RCX = pointer to null-terminated string
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx); // RDX = ptr
    EmitMovRegImm(X86Register.Rax, 0); // RAX = 0 (counter)
    DefineLabel("rt_strlen_loop");
    // movzx ecx, byte ptr [rdx+rax]: 0F B6 0C 02
    EmitBytes(0x0F, 0xB6, 0x0C, 0x02);
    // TEST CL, CL
    EmitBytes(0x84, 0xC9);
    EmitJcc("z", "rt_strlen_done");
    // INC RAX: 48 FF C0
    EmitBytes(0x48, 0xFF, 0xC0);
    EmitJmp("rt_strlen_loop");
    DefineLabel("rt_strlen_done");
    // RAX = length
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_memcpy(dst, src, count) -> dst</summary>
  private void EmitMaxonMemcpy() {
    EmitRuntimeFunctionStart("maxon_memcpy", 3);
    // RCX = dst, RDX = src, R8 = count
    // Set up for REP MOVSB: RDI=dst, RSI=src, RCX=count
    EmitMovRegReg(X86Register.Rdi, X86Register.Rcx); // RDI = dst
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx); // RAX = dst (return value)
    EmitMovRegReg(X86Register.Rsi, X86Register.Rdx); // RSI = src
    EmitMovRegReg(X86Register.Rcx, X86Register.R8);  // RCX = count
    EmitBytes(0xF3, 0xA4); // REP MOVSB
    // RAX already has dst
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_memcmp(ptr1, ptr2, count) -> 1 if equal, 0 if not</summary>
  private void EmitMaxonMemcmp() {
    EmitRuntimeFunctionStart("maxon_memcmp", 3);
    // RCX = ptr1, RDX = ptr2, R8 = count
    // Use REPE CMPSB: RSI=ptr1, RDI=ptr2, RCX=count
    EmitMovRegReg(X86Register.Rsi, X86Register.Rcx); // RSI = ptr1
    EmitMovRegReg(X86Register.Rdi, X86Register.Rdx); // RDI = ptr2
    EmitMovRegReg(X86Register.Rcx, X86Register.R8);  // RCX = count
    // Handle zero-length case (REPE CMPSB with RCX=0 sets ZF=1, so equal)
    EmitBytes(0xF3, 0xA6); // REPE CMPSB
    // ZF=1 if equal, ZF=0 if not equal
    // SETE AL: set AL=1 if equal
    EmitBytes(0x0F, 0x94, 0xC0); // SETE AL
    // MOVZX RAX, AL
    EmitBytes(0x48, 0x0F, 0xB6, 0xC0); // MOVZX RAX, AL
    EmitRuntimeFunctionEnd();
  }

  // mm_raw_alloc: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  // mm_raw_realloc: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  // mm_raw_free: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  /// <summary>Load a 64-bit value from a symdata label into dest. Uses r11 as scratch.</summary>
  private void EmitSymdataLoadI64(X86Register dest, string label) {
    EmitLeaRegSymdataRel(X86Register.R11, label);
    // MOV dest, [r11]: REX.W + 8B /r (mod=00, r/m=r11)
    Rex.W().Reg(dest).Rm(X86Register.R11).Emit(this);
    EmitByte(0x8B);
    EmitByte((byte)(0x03 | (RegCode(dest) << 3))); // mod=00, r/m=011 (r11)
  }

  /// <summary>Store a 64-bit value from src into a symdata label. Uses r11 as scratch.</summary>
  private void EmitSymdataStoreI64(X86Register src, string label) {
    EmitLeaRegSymdataRel(X86Register.R11, label);
    // MOV [r11], src: REX.W + 89 /r (mod=00, r/m=r11)
    Rex.W().Reg(src).Rm(X86Register.R11).Emit(this);
    EmitByte(0x89);
    EmitByte((byte)(0x03 | (RegCode(src) << 3))); // mod=00, r/m=011 (r11)
  }

  // ==========================================================================
  // TCP networking runtime functions (ws2_32.dll)
  // ==========================================================================

  /// <summary>
  /// Emit a relative call to another runtime function label.
  /// </summary>
  private void EmitCallRuntimeLabel(string label, bool zeroSecondArg = false) {
    if (zeroSecondArg) EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, label));
    EmitDword(0);
  }

  /// <summary>
  /// Lazy WSAStartup: checks flag, calls WSAStartup if not yet initialized.
  /// After this, Winsock is ready to use. Uses [rbp-wsaOffset] for WSADATA (408 bytes).
  /// </summary>
  private void EmitWsaEnsureInit(int wsaDataOffset, string skipLabel) {
    DefineSymdata("__net_wsa_init", new byte[8]);
    EmitSymdataLoadI64(X86Register.Rax, "__net_wsa_init");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", skipLabel);
    // WSAStartup(0x0202, &wsaData)
    EmitMovRegImm(X86Register.Rcx, 0x0202); // version 2.2
    EmitLeaRegMem(X86Register.Rdx, wsaDataOffset);
    EmitCallImportOnSystemStack("ws2_32.dll", "WSAStartup");
    // Set init flag
    EmitMovRegImm(X86Register.Rax, 1);
    EmitSymdataStoreI64(X86Register.Rax, "__net_wsa_init");
    DefineLabel(skipLabel);
  }

  /// <summary>
  /// maxon_net_tcp_connect(cstring_host, port) → managed __ManagedSocket ptr, or -1 (DNS fail), -2 (connect fail).
  /// Phase 1: DNS resolution via __io_submit_sync(SyncOpDnsResolve) on the sync worker thread
  /// (getaddrinfo requires a full OS thread stack). Returns resolved IP, -1, or -2.
  /// Phase 2: Socket creation + bind + overlapped ConnectEx via IOCP on the green thread.
  /// On completion, calls setsockopt(SO_UPDATE_CONNECT_CONTEXT) and wraps in __ManagedSocket.
  ///
  /// Stack layout (frame = 0x80):
  ///   [rbp-0x08]  host (arg0, cstring)
  ///   [rbp-0x10]  port (arg1, i64)
  ///   [rbp-0x18]  socket handle
  ///   [rbp-0x20]  ctx (AsyncOpContext*)
  ///   [rbp-0x28]  resolved IP (4 bytes stored as 8)
  ///   [rbp-0x30]  temp (saved function pointer, dequeued GT, etc.)
  ///   [rbp-0x38]  bind sockaddr_in (16 bytes: rbp-0x38 to rbp-0x29)
  ///   [rbp-0x48]  connect sockaddr_in (16 bytes: rbp-0x48 to rbp-0x39)
  /// </summary>
  private void EmitNetTcpConnect() {
    EmitRuntimeFunctionStart("maxon_net_tcp_connect", 2, 0x80);

    // --- Check cancel flag ---
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffCancelFlag);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "rt_ntc_cancelled");

    // --- Phase 1: DNS resolution on the sync worker ---
    // __io_submit_sync(SyncOpDnsResolve, host_cstring, 0) → resolved IP or -1 (DNS) or -2 (TEST-NET-1)
    EmitMovRegImm(X86Register.Rcx, SyncOpDnsResolve);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);           // arg0 = host cstring
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg1 = 0 (unused)
    EmitCallRuntimeLabel("__io_submit_sync");

    // RAX = resolved IP (positive), -1 (DNS fail), or -2 (TEST-NET-1 / error)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("l", "rt_ntc_dns_fail");                     // negative = error, return as-is
    EmitMovMemReg(-0x28, X86Register.Rax, 8);            // save resolved IP

    // --- Phase 2: Socket creation + overlapped ConnectEx ---
    // WSASocketW(AF_INET=2, SOCK_STREAM=1, IPPROTO_TCP=6, NULL, 0, WSA_FLAG_OVERLAPPED=1)
    EmitSystemStackEnter(0x30); // shadow(0x20) + 2 stack args(0x10) = 0x30
    EmitMovRegImm(X86Register.Rcx, 2);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitMovRegImm(X86Register.R8, 6);
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // lpProtocolInfo = NULL
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x20, X86Register.Rax);             // g = 0
    EmitMovRegImm(X86Register.Rax, 1);                   // WSA_FLAG_OVERLAPPED
    EmitMovMemRspReg(0x28, X86Register.Rax);
    EmitCallImport("ws2_32.dll", "WSASocketW");
    EmitSystemStackLeave(0x30);

    // Check for INVALID_SOCKET (-1)
    EmitBytes(0x48, 0x83, 0xF8, 0xFF);                  // CMP RAX, -1
    EmitJcc("ne", "rt_ntc_sock_ok");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitRuntimeFunctionEnd();

    DefineLabel("rt_ntc_sock_ok");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);           // save socket handle

    // --- Register socket with IOCP ---
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // socket handle
    EmitGlobalLoadReg(X86Register.Rdx, "__io_iocp");    // existing IOCP
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // completion key = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // concurrent threads = 0
    EmitCallImportOnSystemStack("kernel32.dll", "CreateIoCompletionPort");

    // SetFileCompletionNotificationModes(socket, FILE_SKIP_COMPLETION_PORT_ON_SUCCESS=1)
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitCallImportOnSystemStack("kernel32.dll", "SetFileCompletionNotificationModes");

    // --- bind(socket, &bind_addr, 16) — required before ConnectEx ---
    // Build bind sockaddr_in at [rbp-0x38] (16 bytes): INADDR_ANY:0
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x38, X86Register.Rax, 8);           // zero first 8 bytes
    EmitMovMemReg(-0x30, X86Register.Rax, 8);           // zero second 8 bytes
    // sin_family = AF_INET (2) — WORD at offset 0
    EmitBytes(0x66, 0xC7, 0x85); EmitDword(-0x38); EmitBytes(0x02, 0x00);

    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // socket
    EmitLeaRegMem(X86Register.Rdx, -0x38);              // &bind_addr
    EmitMovRegImm(X86Register.R8, 16);                   // sizeof(sockaddr_in)
    EmitCallImportOnSystemStack("ws2_32.dll", "bind");

    // Check bind result (0 = success)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_ntc_bind_ok");
    // bind failed — close socket and return -2
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitCallImportOnSystemStack("ws2_32.dll", "closesocket");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitRuntimeFunctionEnd();

    DefineLabel("rt_ntc_bind_ok");

    // --- Build connect sockaddr_in at [rbp-0x48] (16 bytes) ---
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);           // zero first 8 bytes
    EmitMovMemReg(-0x40, X86Register.Rax, 8);           // zero second 8 bytes
    // sin_family = AF_INET (2) — WORD at offset 0
    EmitBytes(0x66, 0xC7, 0x85); EmitDword(-0x48); EmitBytes(0x02, 0x00);
    // sin_port = htons(port) — WORD at offset 2
    EmitMovRegMem(X86Register.Rax, -0x10, 8);           // RAX = port
    EmitBytes(0x66, 0xC1, 0xC0, 0x08);                  // ROL AX, 8 (htons)
    EmitBytes(0x66, 0x89, 0x85); EmitDword(-0x46);      // MOV WORD [rbp-0x46], AX
    // sin_addr = resolved IP — DWORD at offset 4
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x89, 0x85); EmitDword(-0x44);             // MOV DWORD [rbp-0x44], EAX

    // --- Allocate AsyncOpContext ---
    EmitMovRegImm(X86Register.Rcx, AsyncCtxSize);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);           // save ctx (zeroed by HEAP_ZERO_MEMORY)

    // ctx->waiter_gt = __gt_current
    EmitLoadCurrentGtInline(X86Register.Rdx);
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rax, AsyncCtxOffWaiter, X86Register.Rdx);

    // ctx->custom_result = socket_handle (so IOCP drain thread returns handle, not bytes)
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);           // socket handle
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rax, AsyncCtxOffCustomResult, X86Register.Rdx);

    // Store handle in gt->io_handle so CancelIoEx can reach it
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rdx);

    // Set current thread status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax);

    // --- Call ConnectEx via function pointer ---
    // ConnectEx(socket, &name, namelen, lpSendBuffer, dwSendDataLength, lpdwBytesSent, lpOverlapped)
    // 7 parameters: RCX, RDX, R8, R9, [RSP+0x20], [RSP+0x28], [RSP+0x30]
    EmitGlobalLoadReg(X86Register.Rax, "__io_connectex_ptr");
    EmitMovMemReg(-0x30, X86Register.Rax, 8);           // save function pointer
    EmitSystemStackEnter(0x40); // shadow(0x20) + 3 stack args(0x18) + pad(0x08) = 0x40
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // socket
    EmitLeaRegMem(X86Register.Rdx, -0x48);              // &sockaddr_in
    EmitMovRegImm(X86Register.R8, 16);                   // namelen
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // lpSendBuffer = NULL
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x20, X86Register.Rax);             // dwSendDataLength = 0
    EmitMovMemRspReg(0x28, X86Register.Rax);             // lpdwBytesSent = NULL
    EmitMovRegMem(X86Register.Rax, -0x20, 8);           // ctx
    EmitMovMemRspReg(0x30, X86Register.Rax);             // lpOverlapped = ctx
    // Call via function pointer
    EmitMovRegMem(X86Register.Rax, -0x30, 8);           // reload function pointer
    EmitBytes(0xFF, 0xD0);                               // CALL RAX
    EmitSystemStackLeave(0x40);

    // Save return value before trace output
    EmitMovMemReg(-0x30, X86Register.Rax, 8);

    if (Compiler.AsyncTrace) {
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_yield");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_io_op_net_connect");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Restore return value
    EmitMovRegMem(X86Register.Rax, -0x30, 8);

    // ConnectEx returns TRUE (non-zero) on immediate success, FALSE (0) on error/pending
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_ntc_sync_done");

    // FALSE — check WSAGetLastError for IO_PENDING (997)
    EmitCallImportOnSystemStack("ws2_32.dll", "WSAGetLastError");
    EmitMovRegImm(X86Register.Rcx, 997);                // WSA_IO_PENDING
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "rt_ntc_yield");

    // Sync error: ConnectEx failed immediately.
    // Close socket, clear io_handle/status, store error, free ctx, return -2.
    EmitMovMemReg(-0x30, X86Register.Rax, 8);           // save error code
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitCallImportOnSystemStack("ws2_32.dll", "closesocket");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegImm(X86Register.Rax, -2);
    EmitRuntimeFunctionEnd();

    // Sync success: ConnectEx returned TRUE (completed immediately due to
    // FILE_SKIP_COMPLETION_PORT_ON_SUCCESS). No IOCP completion will fire.
    DefineLabel("rt_ntc_sync_done");
    // Store socket handle as gt->io_result_val directly
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // socket handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoResultVal, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax); // status = ready
    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitJmp("rt_ntc_resume");

    // --- Async yield: ConnectEx returned IO_PENDING ---
    DefineLabel("rt_ntc_yield");
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", "rt_ntc_mainthread_loop");

    // Non-mainThread: switch to P->mainThread (worker loop handles scheduling)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoYielded, X86Register.Rax);
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp("rt_ntc_resume");

    // MainThread: spin on completions + wake event until our I/O finishes
    DefineLabel("rt_ntc_mainthread_loop");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusWaiting);
    EmitJcc("ne", "rt_ntc_resume");
    // Try dequeue a runnable GT
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "rt_ntc_mainthread_park");
    // Got a GT — run it
    EmitMovMemReg(-0x30, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x30, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    EmitJmp("rt_ntc_mainthread_loop");
    // No GT — park briefly
    DefineLabel("rt_ntc_mainthread_park");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 50);
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp("rt_ntc_mainthread_loop");

    // --- Resume: ConnectEx completed (sync or async) ---
    DefineLabel("rt_ntc_resume");

    if (Compiler.AsyncTrace) {
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_resume");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_io_op_net_connect");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Clear io_handle and retrieve result (socket handle from gt->io_result_val)
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rax);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoResultVal);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);           // save socket handle

    // Check for error (io_error_code non-zero or io_result_val == 0)
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoErrorCode);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_ntc_async_fail");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_ntc_async_fail");

    // setsockopt(socket, SOL_SOCKET=0xFFFF, SO_UPDATE_CONNECT_CONTEXT=0x7010, NULL, 0)
    // Required after ConnectEx to update the socket's internal state.
    EmitSystemStackEnter(0x30); // shadow(0x20) + 1 stack arg + pad = 0x30
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // socket
    EmitMovRegImm(X86Register.Rdx, 0xFFFF);             // SOL_SOCKET
    EmitMovRegImm(X86Register.R8, 0x7010);              // SO_UPDATE_CONNECT_CONTEXT
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // optval = NULL
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x20, X86Register.Rax);             // optlen = 0
    EmitCallImport("ws2_32.dll", "setsockopt");
    EmitSystemStackLeave(0x30);

    // Wrap socket handle in __ManagedSocket
    EmitMovRegImm(X86Register.Rcx, 8);
    EmitLeaFuncAddr(X86Register.Rdx, "__destruct___ManagedSocket");
    EmitTagZero(X86Register.R8);
    EmitCallRuntimeLabel("mm_alloc");
    // RAX = managed user pointer; store socket handle at [ptr+0]
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitBytes(0x48, 0x89, 0x08);                         // MOV [RAX], RCX
    EmitRuntimeFunctionEnd();

    // DNS failure: return as-is (-1 or -2)
    DefineLabel("rt_ntc_dns_fail");
    EmitRuntimeFunctionEnd();

    // Async error path: close socket and return -2
    DefineLabel("rt_ntc_async_fail");
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_ntc_async_fail_noclose");
    EmitCallImportOnSystemStack("ws2_32.dll", "closesocket");
    DefineLabel("rt_ntc_async_fail_noclose");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitRuntimeFunctionEnd();

    // Cancelled
    DefineLabel("rt_ntc_cancelled");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegImm(X86Register.Rax, 0x3EF);              // ERROR_OPERATION_ABORTED
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    EmitMovRegImm(X86Register.Rax, -2);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_send(socket_handle, buffer_ptr, length) → bytes_sent or -1.
  /// Uses WSASend with IOCP overlapped I/O for async completion.
  /// Stack: [rbp-0x08]=handle, [rbp-0x10]=buf, [rbp-0x18]=length,
  ///        [rbp-0x20]=ctx, [rbp-0x28]=WSABUF.buf, [rbp-0x30]=WSABUF.len,
  ///        [rbp-0x38]=bytesTransferred (for GetOverlappedResult)
  /// </summary>
  private void EmitNetSend() {
    EmitRuntimeFunctionStart("maxon_net_send", 3, 0x80);
    EmitIoSubmitSocketOverlapped("WSASend", "maxon_net_send");
  }

  /// <summary>
  /// maxon_net_recv(socket_handle, buffer_ptr, capacity) → bytes_received, 0=closed, -1=error.
  /// Uses WSARecv with IOCP overlapped I/O for async completion.
  /// Stack: [rbp-0x08]=handle, [rbp-0x10]=buf, [rbp-0x18]=capacity,
  ///        [rbp-0x20]=ctx, [rbp-0x28]=WSABUF.buf, [rbp-0x30]=WSABUF.len,
  ///        [rbp-0x38]=bytesTransferred, [rbp-0x40]=flags (DWORD 0 for WSARecv)
  /// </summary>
  private void EmitNetRecv() {
    EmitRuntimeFunctionStart("maxon_net_recv", 3, 0x80);
    EmitIoSubmitSocketOverlapped("WSARecv", "maxon_net_recv");
  }

  /// <summary>
  /// Shared helper for WSASend/WSARecv overlapped I/O, modeled on EmitIoSubmitOverlappedCore.
  /// Emits the body (after EmitRuntimeFunctionStart) including EmitRuntimeFunctionEnd.
  /// </summary>
  private void EmitIoSubmitSocketOverlapped(string wsaFuncName, string labelPrefix) {
    // Check cancel flag
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffCancelFlag);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", $"{labelPrefix}_cancelled");

    // Allocate AsyncOpContext (0x38 bytes): OVERLAPPED at offset 0 + our fields
    EmitMovRegImm(X86Register.Rcx, AsyncCtxSize);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save ctx (zeroed by HEAP_ZERO_MEMORY)

    // ctx->waiter_gt = __gt_current
    EmitLoadCurrentGtInline(X86Register.Rdx);
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rax, AsyncCtxOffWaiter, X86Register.Rdx);

    // Store handle in gt->io_handle so CancelIoEx can reach it
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rdx);

    // Set current thread status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax);

    // Build WSABUF at [rbp-0x30]: { ULONG len (4 bytes + 4 padding), CHAR* buf (8 bytes) }
    // WSABUF.len at [rbp-0x30], WSABUF.buf at [rbp-0x28]
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // zero the full 8 bytes (len + padding)
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // length/capacity
    EmitBytes(0x89, 0x85); EmitDword(-0x30);   // MOV DWORD [rbp-0x30], EAX (store as ULONG)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // buf_ptr
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // WSABUF.buf

    if (wsaFuncName == "WSARecv") {
      // Zero the flags DWORD at [rbp-0x40] for WSARecv's lpFlags parameter
      EmitXorRegReg(X86Register.Rax, X86Register.Rax);
      EmitMovMemReg(-0x40, X86Register.Rax, 8);
    }

    // Call WSASend/WSARecv with 7 args:
    //   RCX = socket, RDX = &WSABUF, R8 = 1 (buffer count), R9 = NULL,
    //   [RSP+0x20], [RSP+0x28] = ctx, [RSP+0x30] = NULL
    EmitSystemStackEnter(0x40); // shadow(0x20) + 3 stack args(0x18) + pad(0x08) = 0x40
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);     // socket handle
    EmitLeaRegMem(X86Register.Rdx, -0x30);         // &WSABUF
    EmitMovRegImm(X86Register.R8, 1);              // dwBufferCount = 1
    EmitXorRegReg(X86Register.R9, X86Register.R9); // lpNumberOfBytes = NULL

    if (wsaFuncName == "WSASend") {
      // [RSP+0x20] = dwFlags = 0
      EmitXorRegReg(X86Register.Rax, X86Register.Rax);
      EmitMovMemRspReg(0x20, X86Register.Rax);
    } else {
      // [RSP+0x20] = &flags (pointer to DWORD 0 at [rbp-0x40])
      EmitLeaRegMem(X86Register.Rax, -0x40);
      EmitMovMemRspReg(0x20, X86Register.Rax);
    }

    // [RSP+0x28] = lpOverlapped = ctx
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovMemRspReg(0x28, X86Register.Rax);
    // [RSP+0x30] = lpCompletionRoutine = NULL
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x30, X86Register.Rax);
    EmitCallImport("ws2_32.dll", wsaFuncName);
    EmitSystemStackLeave(0x40);

    // Save return value before trace output
    EmitMovMemReg(-0x48, X86Register.Rax, 8);

    if (Compiler.AsyncTrace) {
      // Trace: "io_yield #N [net_send/net_recv] [M=W]\n"
      // Emitted unconditionally so both sync and async paths produce the expected yield/resume pair.
      var yieldOpSymdata = wsaFuncName == "WSASend" ? "__at_io_op_net_send" : "__at_io_op_net_recv";
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_yield");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, yieldOpSymdata);
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Restore return value
    EmitMovRegMem(X86Register.Rax, -0x48, 8);

    // WSASend/WSARecv returns 0 on success, SOCKET_ERROR (-1) on error
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", $"{labelPrefix}_sync_done"); // 0 = immediate success

    // SOCKET_ERROR: check WSAGetLastError for IO_PENDING (997)
    EmitCallImportOnSystemStack("ws2_32.dll", "WSAGetLastError");
    EmitMovRegImm(X86Register.Rcx, 997); // WSA_IO_PENDING
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", $"{labelPrefix}_yield"); // IO_PENDING: async path, yield

    // Sync error: save error code, clear io_handle/status, free ctx, return 0
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // save error code
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rcx); // clear io_handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rcx); // status = ready (0)
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // restore error code
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();

    // Sync success: I/O completed immediately (FILE_SKIP_COMPLETION_PORT_ON_SUCCESS).
    // Use GetOverlappedResult to retrieve the definitive byte count.
    DefineLabel($"{labelPrefix}_sync_done");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // zero the bytesTransferred slot
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // hFile = socket handle
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // lpOverlapped = ctx
    EmitLeaRegMem(X86Register.R8, -0x38);     // &bytesTransferred
    EmitXorRegReg(X86Register.R9, X86Register.R9); // bWait = FALSE (already complete)
    EmitCallImportOnSystemStack("kernel32.dll", "GetOverlappedResult");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8); // bytes transferred
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoResultVal, X86Register.Rcx); // gt->io_result_val = bytes
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax); // status = ready (0)
    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitJmp($"{labelPrefix}_resume");

    // Async yield: I/O is pending. Check if current GT is the mainThread.
    DefineLabel($"{labelPrefix}_yield");
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", $"{labelPrefix}_mainthread_loop");

    // Non-mainThread: switch to P->mainThread (worker loop handles scheduling)
    // Clear ioYielded flag BEFORE context switch so __io_complete_gt knows to spin-wait
    // until the context switch saves our RSP/RBP.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoYielded, X86Register.Rax);
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp($"{labelPrefix}_resume");

    // MainThread: spin on completions + wake event until our I/O finishes
    DefineLabel($"{labelPrefix}_mainthread_loop");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusWaiting);
    EmitJcc("ne", $"{labelPrefix}_resume");
    // Try dequeue a runnable GT
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", $"{labelPrefix}_mainthread_park");
    // Got a GT -- run it
    EmitMovMemReg(-0x38, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x38, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Signal that the GT's context switch is complete so __io_complete_gt can safely enqueue it.
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    EmitJmp($"{labelPrefix}_mainthread_loop");
    // No GT -- park briefly
    DefineLabel($"{labelPrefix}_mainthread_park");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 50);
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp($"{labelPrefix}_mainthread_loop");

    // Resume: clear io_handle, return bytes transferred
    DefineLabel($"{labelPrefix}_resume");

    if (Compiler.AsyncTrace) {
      // Trace: "io_resume #N [net_send/net_recv] [M=W]\n"
      var resumeOpSymdata = wsaFuncName == "WSASend" ? "__at_io_op_net_send" : "__at_io_op_net_recv";
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_resume");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, resumeOpSymdata);
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    EmitLoadCurrentGtInline(X86Register.R10);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rax); // clear io_handle
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoResultVal);
    EmitRuntimeFunctionEnd();

    DefineLabel($"{labelPrefix}_cancelled");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegImm(X86Register.Rax, 0x3EF); // ERROR_OPERATION_ABORTED
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_close(__ManagedSocket*) → void. Idempotent: no-op if ptr is null or handle is 0.
  /// Reads _handle from [ptr+0], zeros the field, then delegates closesocket to the sync worker.
  /// Being the single point that clears _handle means the destructor's idempotency check sees
  /// a zeroed field after an explicit close — no double-close on a reused OS handle.
  /// Stack: [rbp-8]=managedSocketPtr
  /// </summary>
  private void EmitNetClose() {
    EmitRuntimeFunctionStart("maxon_net_close", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);           // RAX = __ManagedSocket*
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_nc_noop");                          // null ptr → no-op
    EmitBytes(0x48, 0x8B, 0x10);                         // MOV RDX, [RAX] = _handle
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_nc_noop");                          // already closed
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitBytes(0x48, 0x89, 0x08);                         // MOV [RAX], RCX = 0
    EmitMovRegImm(X86Register.Rcx, SyncOpNetClose);      // op
    // RDX already = handle (arg0)
    EmitXorRegReg(X86Register.R8, X86Register.R8);       // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    DefineLabel("rt_nc_noop");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedSocket(user_ptr) → void.
  /// Called by mm_decref when refcount hits 0. Delegates to maxon_net_close,
  /// which reads _handle, zeros it, and routes closesocket through the sync worker.
  /// If an explicit close() already ran, _handle is zero and this is a no-op.
  /// </summary>
  private void EmitNetSocketDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedSocket", 1, 0x20);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);           // arg0 = user_ptr
    EmitCallRuntimeLabel("maxon_net_close");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Managed File runtime functions
  // ==========================================================================

  /// <summary>
  /// maxon_managed_file_open_read(cstring) → managed __ManagedFile ptr or -1.
  /// Delegates to __io_submit_sync(SyncOpFileOpenRead, path, 0) so that CreateFileA
  /// runs on the sync worker's OS thread (not on a green thread's VirtualAlloc'd stack).
  /// The sync worker returns a raw file handle (or -1). We wrap it in mm_alloc here
  /// (on the green thread) since mm_alloc is not thread-safe.
  /// Stack: [rbp-8]=cstring, [rbp-16]=handle
  /// </summary>
  private void EmitManagedFileOpenRead() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_read", 1, 0x30);
    EmitMovRegImm(X86Register.Rcx, SyncOpFileOpenRead);  // op = 7
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);            // arg0 = cstring path
    EmitXorRegReg(X86Register.R8, X86Register.R8);        // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = raw file handle or -1
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "rt_mfor_fail");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);            // save handle
    // Allocate __ManagedFile via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(X86Register.Rcx, 8);
    EmitLeaFuncAddr(X86Register.Rdx, "__destruct___ManagedFile");
    EmitTagZero(X86Register.R8);
    EmitCallRuntimeLabel("mm_alloc");
    // RAX = managed user pointer; store file handle at [ptr+0]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitBytes(0x48, 0x89, 0x08);                          // MOV [RAX], RCX
    EmitJmp("rt_mfor_done");
    DefineLabel("rt_mfor_fail");
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_mfor_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_managed_file_open_write(cstring) → managed __ManagedFile ptr or -1.
  /// Delegates to __io_submit_sync(SyncOpFileOpenWrite, path, 0) so that CreateFileA
  /// runs on the sync worker's OS thread (not on a green thread's VirtualAlloc'd stack).
  /// The sync worker returns a raw file handle (or -1). We wrap it in mm_alloc here
  /// (on the green thread) since mm_alloc is not thread-safe.
  /// Stack: [rbp-8]=cstring, [rbp-16]=handle
  /// </summary>
  private void EmitManagedFileOpenWrite() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_write", 1, 0x30);
    EmitMovRegImm(X86Register.Rcx, SyncOpFileOpenWrite); // op = 8
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);            // arg0 = cstring path
    EmitXorRegReg(X86Register.R8, X86Register.R8);        // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = raw file handle or -1
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "rt_mfow_fail");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);            // save handle
    // Allocate __ManagedFile via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(X86Register.Rcx, 8);
    EmitLeaFuncAddr(X86Register.Rdx, "__destruct___ManagedFile");
    EmitTagZero(X86Register.R8);
    EmitCallRuntimeLabel("mm_alloc");
    // RAX = managed user pointer; store file handle at [ptr+0]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitBytes(0x48, 0x89, 0x08);                          // MOV [RAX], RCX
    EmitJmp("rt_mfow_done");
    DefineLabel("rt_mfow_fail");
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_mfow_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_managed_file_open_write_executable(cstring) → managed __ManagedFile ptr or -1.
  /// Same as open_write but on Unix creates file with mode 0755 (executable) instead of 0666.
  /// On Windows this is identical to open_write (no Unix permissions).
  /// </summary>
  private void EmitManagedFileOpenWriteExecutable() {
    EmitRuntimeFunctionStart("maxon_managed_file_open_write_executable", 1, 0x30);
    EmitMovRegImm(X86Register.Rcx, SyncOpFileOpenWriteExec);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);            // arg0 = cstring path
    EmitXorRegReg(X86Register.R8, X86Register.R8);        // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = raw file handle or -1
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "rt_mfowe_fail");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);            // save handle
    // Allocate __ManagedFile via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(X86Register.Rcx, 8);
    EmitLeaFuncAddr(X86Register.Rdx, "__destruct___ManagedFile");
    EmitTagZero(X86Register.R8);
    EmitCallRuntimeLabel("mm_alloc");
    // RAX = managed user pointer; store file handle at [ptr+0]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitBytes(0x48, 0x89, 0x08);                          // MOV [RAX], RCX
    EmitJmp("rt_mfowe_done");
    DefineLabel("rt_mfowe_fail");
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_mfowe_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_exists(cstring) → 1 if file exists (and is not a directory), 0 otherwise.
  /// Delegates to __io_submit_sync(SyncOpFileExists, path, 0) which returns 0 or 1.
  /// Stack: [rbp-8]=path
  /// </summary>
  private void EmitFileExists() {
    EmitRuntimeFunctionStart("maxon_file_exists", 1, 0x20);
    EmitMovRegImm(X86Register.Rcx, SyncOpFileExists);   // op = 0
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);           // arg0 = path
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = 0 or 1 from sync worker
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_stat(cstring_path) → raw buffer ptr (6 x i64) or -1
  /// Calls FindFirstFileA directly (via system stack switch), extracts metadata
  /// from WIN32_FIND_DATAA, then closes the handle. Packs results into a
  /// heap-allocated 48-byte buffer.
  /// Stack layout:
  ///   [rbp-0x08]         = cstring path (arg0)
  ///   [rbp-0x10]         = output buffer ptr (mm_raw_alloc'd)
  ///   [rbp-0x18]         = FindFirstFileA handle
  ///   [rbp-0x158]..[rbp-0x19] = WIN32_FIND_DATAA (320 bytes)
  ///     +0   dwFileAttributes (4 bytes)
  ///     +4   ftCreationTime (8 bytes)
  ///     +12  ftLastAccessTime (8 bytes)
  ///     +20  ftLastWriteTime (8 bytes)
  ///     +28  nFileSizeHigh (4 bytes)
  ///     +32  nFileSizeLow (4 bytes)
  /// </summary>
  private void EmitFileStat() {
    const int findDataOffset = -0x158; // WIN32_FIND_DATAA on stack
    EmitRuntimeFunctionStart("maxon_file_stat", 1, 0x180);
    // Allocate 48-byte output buffer
    EmitMovRegImm(X86Register.Rcx, 48);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);            // save buffer ptr

    // FindFirstFileA(path, &findData)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);            // arg1: path
    EmitLeaRegMem(X86Register.Rdx, findDataOffset);       // arg2: &WIN32_FIND_DATAA
    EmitCallImportOnSystemStack("kernel32.dll", "FindFirstFileA");
    // Check for INVALID_HANDLE_VALUE (-1)
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "rt_fstat_fail");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);            // save handle

    // Close the find handle immediately — we only needed the first result
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);     // handle
    EmitCallImportOnSystemStack("kernel32.dll", "FindClose");

    // Load output buffer pointer into RDI
    EmitMovRegMem(X86Register.Rdi, -0x10, 8);

    // buf[0] = size = (nFileSizeHigh << 32) | nFileSizeLow
    EmitMovRegMem(X86Register.Rax, findDataOffset + 28, 4); // nFileSizeHigh (zero-extended)
    EmitShlRegImm(X86Register.Rax, 32);
    EmitMovRegMem(X86Register.Rcx, findDataOffset + 32, 4); // nFileSizeLow (zero-extended)
    EmitOrRegReg(X86Register.Rax, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, 0, X86Register.Rax);

    // buf[8] = modifiedTime: FILETIME → Unix epoch seconds
    // (filetime - 116444736000000000) / 10000000
    EmitMovRegMem(X86Register.Rax, findDataOffset + 20, 8); // ftLastWriteTime
    EmitMovRegImm(X86Register.Rcx, 116444736000000000L);
    EmitSubRegReg(X86Register.Rax, X86Register.Rcx);
    EmitMovRegImm(X86Register.Rcx, 10000000L);
    EmitBytes(0x48, 0x99);                                // CQO
    EmitIdivReg(X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, 8, X86Register.Rax);

    // buf[16] = createdTime
    EmitMovRegMem(X86Register.Rax, findDataOffset + 4, 8); // ftCreationTime
    EmitMovRegImm(X86Register.Rcx, 116444736000000000L);
    EmitSubRegReg(X86Register.Rax, X86Register.Rcx);
    EmitMovRegImm(X86Register.Rcx, 10000000L);
    EmitBytes(0x48, 0x99);                                // CQO
    EmitIdivReg(X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, 16, X86Register.Rax);

    // buf[24] = accessedTime
    EmitMovRegMem(X86Register.Rax, findDataOffset + 12, 8); // ftLastAccessTime
    EmitMovRegImm(X86Register.Rcx, 116444736000000000L);
    EmitSubRegReg(X86Register.Rax, X86Register.Rcx);
    EmitMovRegImm(X86Register.Rcx, 10000000L);
    EmitBytes(0x48, 0x99);                                // CQO
    EmitIdivReg(X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, 24, X86Register.Rax);

    // buf[32] = isDirectory (FILE_ATTRIBUTE_DIRECTORY = 0x10)
    EmitMovRegMem(X86Register.Rax, findDataOffset, 4);   // dwFileAttributes
    EmitBytes(0xA9, 0x10, 0x00, 0x00, 0x00);             // TEST EAX, 0x10
    EmitSetcc("nz", X86Register.Rcx);
    EmitMovzxReg8To64(X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, 32, X86Register.Rcx);

    // buf[40] = isReadOnly (FILE_ATTRIBUTE_READONLY = 0x01)
    EmitBytes(0xA9, 0x01, 0x00, 0x00, 0x00);             // TEST EAX, 0x01
    EmitSetcc("nz", X86Register.Rcx);
    EmitMovzxReg8To64(X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, 40, X86Register.Rcx);

    // Return buffer ptr
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitJmp("rt_fstat_done");

    DefineLabel("rt_fstat_fail");
    // Capture GetLastError into gt->io_error_code so the lowering's
    // notFound/accessDenied dispatch sees ERROR_FILE_NOT_FOUND / ERROR_ACCESS_DENIED.
    EmitCaptureLastErrorToGt();
    // Free buffer on failure
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_fstat_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_stat_field(buffer, index) → i64 value at buffer[index * 8]
  /// Reads a single i64 field from the stat result buffer.
  /// Stack: [rbp-8]=buffer, [rbp-0x10]=index
  /// </summary>
  private void EmitFileStatField() {
    EmitRuntimeFunctionStart("maxon_file_stat_field", 2, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);            // buffer
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);            // index
    // SHL RCX, 3 (multiply index by 8)
    EmitShlRegImm(X86Register.Rcx, 3);
    // MOV RAX, [RAX + RCX]
    EmitBytes(0x48, 0x8B, 0x04, 0x08);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_sleep(milliseconds): Suspends the current green thread for the given duration.
  /// Uses the scheduler timer heap: computes deadline = GetTickCount64() + ms,
  /// adds (deadline, gt) to the timer min-heap, then yields. The scheduler's
  /// __gt_timer_check() re-enqueues the GT when the deadline expires.
  /// Stack: [rbp-8]=milliseconds
  /// </summary>
  private void EmitSleep() {
    EmitRuntimeFunctionStart("maxon_sleep", 1, 0x30);
    // [rbp-0x08] = milliseconds, [rbp-0x10] = deadline, [rbp-0x18] = dequeued GT (mainthread path)

    // deadline = GetTickCount64() + milliseconds
    EmitCallImport("kernel32.dll", "GetTickCount64");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // load ms
    EmitAddRegReg(X86Register.Rax, X86Register.Rcx); // RAX = now + ms
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save deadline

    // Set current GT status = waiting
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegImm(X86Register.Rdx, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rdx);

    // __gt_timer_add(gt, deadline)
    EmitMovRegReg(X86Register.Rcx, X86Register.R10); // gt
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);        // deadline
    EmitCallRuntimeLabel("__gt_timer_add");

    if (Compiler.AsyncTrace) {
      // Trace: "sleep_yield #N\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_sleep_yield");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Check if current GT is the mainThread
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", "__sleep_mainthread_loop");

    // Non-mainThread path: switch to P->mainThread (worker loop handles scheduling)
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp("__sleep_resume"); // we resume here when timer expires and GT is re-enqueued

    // MainThread path: inline scheduling loop until our status changes from waiting
    DefineLabel("__sleep_mainthread_loop");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_timer_check");
    // Check if our status changed from waiting (timer expired, timer_check set us to ready)
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusWaiting);
    EmitJcc("ne", "__sleep_resume");
    // Try dequeue a runnable GT and run it while we wait
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__sleep_mainthread_park");
    // Got a GT — context-switch to it
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // save dequeued GT
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Signal that the GT's context switch is complete
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    EmitJmp("__sleep_mainthread_loop");
    // No GT to run — park briefly, then retry
    DefineLabel("__sleep_mainthread_park");
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffWakeEvent);
    EmitMovRegImm(X86Register.Rdx, 10); // 10ms timeout (responsive for sleep timers)
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp("__sleep_mainthread_loop");

    DefineLabel("__sleep_resume");

    if (Compiler.AsyncTrace) {
      // Trace: "sleep_resume #N\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_sleep_resume");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_managed_file_write(handle, buffer, length) → bytes written or -1.
  /// Calls __io_submit_write(handle, buffer, length) for overlapped I/O.
  /// Stack: [rbp-8]=handle, [rbp-16]=buffer, [rbp-24]=length
  /// </summary>
  private void EmitManagedFileWrite() {
    EmitRuntimeFunctionStart("maxon_managed_file_write", 3, 0x30);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);         // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x18, 8);           // arg3: length
    EmitCallRuntimeLabel("__io_submit_write");
    // RAX = bytes written; check gt->io_error_code for errors
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, GtOffIoErrorCode);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_mfw_done");
    // Error: return -1
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_mfw_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedFile(user_ptr) → void.
  /// Called by mm_decref when refcount hits 0. Delegates to maxon_file_close,
  /// which reads _handle, zeros the field, and routes CloseHandle through the
  /// sync worker. If an explicit close() already ran, _handle is zero and this
  /// is a no-op — so no double-close.
  /// </summary>
  private void EmitFileDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedFile", 1, 0x20);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);          // arg0 = user_ptr
    EmitCallRuntimeLabel("maxon_file_close");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Managed Directory runtime functions
  // ==========================================================================

  /// <summary>
  /// maxon_managed_dir_open_search(cstring) → managed __ManagedDirectory ptr or 0.
  /// Allocates a find block, calls FindFirstFileA. On success, allocates a
  /// __ManagedDirectory struct and stores the block pointer. On failure, returns 0.
  /// Stack: [rbp-8]=cstring, [rbp-16]=block_ptr
  /// </summary>
  private void EmitManagedDirOpenSearch() {
    EmitRuntimeFunctionStart("maxon_managed_dir_open_search", 1, 0x50);

    // Allocate the find block (328 bytes, no destructor)
    EmitMovRegImm(X86Register.Rcx, FindBlockSize);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);   // destructor = 0
    EmitTagZero(X86Register.R8);
    EmitCallRuntimeLabel("mm_alloc");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);         // [rbp-16] = block_ptr
    // Incref so block starts at rc=1
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitCallRuntimeLabel("mm_incref");

    // FindFirstFileA(pattern, &block[8])
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: pattern (cstring)
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);         // block_ptr
    EmitAddRegImm(X86Register.Rdx, FindBlockFindDataOffset); // arg2: &findData
    EmitCallImportOnSystemStack("kernel32.dll", "FindFirstFileA");

    // Check for INVALID_HANDLE_VALUE (-1)
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ne", "rt_mdos_found");

    // Not found: capture GetLastError into gt->io_error_code so the lowering
    // can map ERROR_FILE_NOT_FOUND / ERROR_PATH_NOT_FOUND to notFound.
    EmitCaptureLastErrorToGt();
    // Decref block (frees since rc drops to 0), return 0
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallRuntimeLabel("mm_decref");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("rt_mdos_done");

    // Found: allocate dir struct, store handle and block
    DefineLabel("rt_mdos_found");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);         // save FindFirstFileA handle temporarily
    // Store handle in block[0]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);         // block_ptr
    EmitMovRegMem(X86Register.Rax, -0x18, 8);         // handle
    EmitMovIndirectMemReg(X86Register.Rcx, FindBlockHandleOffset, X86Register.Rax); // block[0] = handle

    // Allocate __ManagedDirectory via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(X86Register.Rcx, 8);                // size = 8 bytes (one i64 _block field)
    EmitLeaFuncAddr(X86Register.Rdx, "__destruct___ManagedDirectory"); // destructor fn ptr
    EmitTagZero(X86Register.R8);                        // tag_index = 0
    EmitCallRuntimeLabel("mm_alloc");
    // RAX = managed dir user pointer; store block_ptr at [ptr+0]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);          // RCX = block_ptr
    EmitBytes(0x48, 0x89, 0x08);                        // MOV [RAX], RCX
    // Return dir_ptr (RAX already = dir_ptr)

    DefineLabel("rt_mdos_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_managed_dir_close(block_ptr) → void.
  /// Calls FindClose on the handle inside the block if non-zero, zeros the handle.
  /// Does NOT free the block — that is handled by the destructor.
  /// Stack: [rbp-8]=block_ptr
  /// </summary>
  private void EmitManagedDirClose() {
    EmitRuntimeFunctionStart("maxon_managed_dir_close", 1, 0x40);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);         // block_ptr
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_mdc_done");
    // Read handle from block[0]
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FindBlockHandleOffset); // RCX = handle
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_mdc_done");
    // Zero the handle first (idempotency)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);         // block_ptr
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, FindBlockHandleOffset, X86Register.Rdx); // block[0] = 0
    // FindClose(handle) — RCX already has the handle
    EmitCallImportOnSystemStack("kernel32.dll", "FindClose");
    DefineLabel("rt_mdc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedDirectory(user_ptr) → void.
  /// Called by mm_decref when refcount hits 0. Reads _block at [user_ptr+0],
  /// if non-zero: reads handle from block[0], calls FindClose if non-zero,
  /// then frees the block via mm_decref.
  /// </summary>
  private void EmitDirDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedDirectory", 1, 0x40);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);         // RAX = user_ptr
    EmitBytes(0x48, 0x8B, 0x08);                       // MOV RCX, [RAX] = _block
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_mdd_done");
    // Save block ptr
    EmitMovMemReg(-0x10, X86Register.Rcx, 8);
    // Zero _block field (idempotency)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitBytes(0x48, 0x89, 0x10);                       // MOV [RAX], RDX = 0
    // Read handle from block[0]
    EmitMovRegMem(X86Register.Rax, -0x10, 8);         // RAX = block_ptr
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FindBlockHandleOffset);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_mdd_skip_close");
    // FindClose(handle)
    EmitCallImportOnSystemStack("kernel32.dll", "FindClose");
    DefineLabel("rt_mdd_skip_close");
    // Free the block via mm_decref
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallRuntimeLabel("mm_decref");
    DefineLabel("rt_mdd_done");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Green Thread Runtime (async/await with cooperative scheduling)
  // ==========================================================================
  //
  // GreenThread struct layout (160 bytes = 0xA0):
  //   0x00  rsp              saved stack pointer
  //   0x08  rbp              saved base pointer
  //   0x10  status           0=ready, 1=running, 2=completed, 3=waiting
  //   0x18  stack_base       VirtualAlloc'd stack (low address)
  //   0x20  stack_size       current stack allocation size
  //   0x28  result           return value when completed
  //   0x30  waiter           ptr to GreenThread waiting on this one
  //   0x38  next             next in run queue linked list
  //   0x40  func_ptr         target function address
  //   0x48  arg_buf          ptr to argument buffer
  //   0x50  stackguard       lowest valid stack address
  //   0x58  threw            0=success, 1=threw (for async throws)
  //   0x60  io_result_val    raw result value (bytes transferred, etc.)
  //   0x68  io_result_len    byte count for read results
  //   0x70  io_error_code    0=success, non-zero=Win32 error
  //   0x78  cancel_flag      0=live, 1=cancel-requested
  //   0x80  io_handle        HANDLE of in-flight I/O (for CancelIoEx)
  //   0x88  all_next         next ptr in global all-threads list
  //   0x90  tib_stack_base   saved TIB StackBase (gs:[0x08])
  //   0x98  tib_stack_limit  saved TIB StackLimit (gs:[0x10])
  //
  // Scheduler globals (in .data section):
  //   __gt_current          ptr to running GreenThread
  //   __gt_run_queue_head   head of ready queue
  //   __gt_run_queue_tail   tail of ready queue
  //   __gt_main_thread      160-byte GreenThread struct for main thread (inline)

  // Gt and P struct offsets, sizes, status enum, and stack-growth constants
  // live in MaxonSharp.Compiler.Ir.Runtime.GtLayout (imported via `using static`
  // at the top of this file). Single source of truth, shared with both backends
  // and RuntimeEmitter.

  // Timer heap constants
  // Each entry: [deadline_ms: i64, gt_ptr: i64] = 16 bytes
  private const int TimerEntrySize = 16;
  private const int TimerHeapCapacity = 256;

  /// <summary>
  /// On a failure branch of a Win32 call, capture GetLastError into the current
  /// green thread's io_error_code field, so the lowering's notFound/accessDenied
  /// dispatch (via __io_get_last_error) sees the OS code. Uses the caller's
  /// frame slot at [rbp+saveSlotOffset] to stash GetLastError's RAX result while
  /// loading the GT pointer. saveSlotOffset is negative (e.g. -0x20) and must
  /// fit within the runtime function's frame.
  /// Clobbers RAX, R10.
  /// </summary>
  private void EmitCaptureLastErrorToGt(int saveSlotOffset = -0x20) {
    EmitCallImportOnSystemStack("kernel32.dll", "GetLastError");
    EmitMovMemReg(saveSlotOffset, X86Register.Rax, 8);    // save error code
    EmitLoadCurrentGtInline(X86Register.R10);             // R10 = current GT
    EmitMovRegMem(X86Register.Rax, saveSlotOffset, 8);    // reload error code
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
  }

  /// <summary>
  /// Emit inline gs: TLS access to load the P pointer, then dereference P->currentGt
  /// into dest. Uses the precomputed TEB offset for direct segment access — NO function
  /// call, so safe in prologues where argument registers must be preserved.
  /// Only clobbers the destination register.
  /// </summary>
  private void EmitLoadCurrentGtInline(X86Register dest) {
    // Load precomputed TEB offset (0x1480 + index*8) into dest
    EmitGlobalLoadReg(dest, "__sched_tls_teb_offset");
    // MOV dest, GS:[dest] — dereference through gs: segment to get P*
    EmitByte(0x65); // GS segment override prefix
    EmitMovRegIndirectMemRaw(dest, dest, 0);
    // Now dest = P*; load P->currentGt
    EmitMovRegIndirectMem(dest, dest, POffCurrentGt);
  }

  /// <summary>
  /// Emit a call to __ds_emit_dbg(event, gt, p_id, 0, 0, 0) with the GT
  /// pointer loaded from a stack slot at [rbp + gtSlotOffset]. p_id is loaded
  /// inline from current P via TLS (NULL if non-scheduler thread).
  ///
  /// Internal calling convention: Rcx, Rdx, R8, R9, Rsi, Rdi.
  /// Emits a guard "if __ds_base == 0 skip" so when DebugStream is off the
  /// only cost is two instructions (CMP/JE).
  ///
  /// Clobbers Rax, Rcx, Rdx, R8, R9, Rsi, Rdi, R10, R11.
  /// Caller must spill any live state in those regs to local slots.
  /// </summary>
  private void EmitDbgEventInline(byte eventType, int gtSlotOffset) {
    if (!Compiler.DebugStream) return;
    var skipLabel = $"__dbg_inline_skip_{_dbgInlineLabelCtr++}";
    // CMP qword [rip + __ds_base], 0; JE skip
    EmitGlobalLoadReg(X86Register.Rax, "__ds_base");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", skipLabel);
    // Args (internal CC): Rcx=event, Rdx=gt, R8=p_id, R9=0, Rsi=0, Rdi=0
    EmitMovRegImm(X86Register.Rcx, eventType);
    EmitMovRegMem(X86Register.Rdx, gtSlotOffset, 8);
    // Load P->id into R8 via TLS (inline, no function call)
    EmitGlobalLoadReg(X86Register.R8, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R8, X86Register.R8, 0); // R8 = P*
    EmitBytes(0x4D, 0x85, 0xC0); // TEST R8, R8 (check non-NULL)
    var pNullLabel = $"__dbg_inline_pnull_{_dbgInlineLabelCtr++}";
    var pDoneLabel = $"__dbg_inline_pdone_{_dbgInlineLabelCtr++}";
    EmitJcc("z", pNullLabel);
    EmitMovRegIndirectMem(X86Register.R8, X86Register.R8, POffId);
    EmitJmp(pDoneLabel);
    DefineLabel(pNullLabel);
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    DefineLabel(pDoneLabel);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitXorRegReg(X86Register.Rsi, X86Register.Rsi);
    EmitXorRegReg(X86Register.Rdi, X86Register.Rdi);
    EmitCallRuntimeLabel("__ds_emit_dbg");
    DefineLabel(skipLabel);
  }

  private int _dbgInlineLabelCtr;

  /// <summary>
  /// Emit inline gs: TLS access to LEA the address of P->mainThread into dest.
  /// The mainThread is an inline GreenThread at offset POffMainThread within P.
  /// Only clobbers the destination register.
  /// </summary>
  private void EmitLeaMainThreadInline(X86Register dest) {
    EmitGlobalLoadReg(dest, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(dest, dest, 0);
    EmitLeaRegIndirect(dest, dest, POffMainThread);
  }

  /// <summary>
  /// Raw MOV r64, [baseReg+disp] — identical to EmitMovRegIndirectMem but allows
  /// a segment override prefix to be prepended immediately before this call.
  /// </summary>
  private void EmitMovRegIndirectMemRaw(X86Register dest, X86Register baseReg, int displacement) {
    Rex.W().Reg(dest).Rm(baseReg).Emit(this);
    EmitByte(0x8B);
    EmitModRmWithBase(baseReg, dest, displacement);
  }

  /// <summary>
  /// Emit LEA dest, [baseReg + displacement] for computing addresses from base+offset.
  /// </summary>
  private void EmitLeaRegIndirect(X86Register dest, X86Register baseReg, int displacement) {
    Rex.W().Reg(dest).Rm(baseReg).Emit(this);
    EmitByte(0x8D); // LEA
    EmitModRmWithBase(baseReg, dest, displacement);
  }

  private void EmitGreenThreadRuntime() {
    // TLS-based scheduler globals (GMP model: P struct accessed via TLS)
    DefineGlobal("__sched_tls_index", 8, 0);      // DWORD TLS slot index from TlsAlloc
    DefineGlobal("__sched_tls_teb_offset", 8, 0); // precomputed gs: offset (0x1480 + index*8)
    DefineGlobal("__sched_procs", 8, 0);           // pointer to array of P* (one per worker)
    DefineGlobal("__sched_num_procs", 8, 0);       // number of P structs allocated

    // Global run queue (shared across all workers)
    DefineGlobal("__gt_run_queue_head", 8, 0);
    DefineGlobal("__gt_run_queue_tail", 8, 0);
    DefineGlobal("__sched_global_queue_cs", 40, 0); // CRITICAL_SECTION protecting run queue
    DefineGlobal("__sched_all_cs", 40, 0);           // CRITICAL_SECTION protecting all-threads list
    DefineGlobal("__sched_active_workers", 8, 0);   // atomic count of running workers
    DefineGlobal("__sched_max_procs", 8, 0);        // CPU count (from GetSystemInfo)
    DefineGlobal("__sched_shutdown_flag", 8, 0);     // 1 = shutdown requested
    DefineGlobal("__gt_live_count", 8, 0); // count of non-completed green threads (excludes main thread)
    DefineGlobal("__gt_all_head", 8, 0);   // head of all-live-threads singly-linked list

    // Timer heap: array of (deadline_ms i64, gt_ptr i64) pairs, min-heap by deadline.
    // Max 256 concurrent timers. Each entry is 16 bytes. Total = 256 * 16 = 4096 bytes.
    DefineGlobal("__gt_timer_heap", TimerHeapCapacity * TimerEntrySize, 0);
    DefineGlobal("__gt_timer_count", 8, 0);   // current number of entries in the heap
    DefineGlobal("__gt_timer_cs", 40, 0);     // CRITICAL_SECTION protecting the timer heap

    if (Compiler.AsyncTrace) {
      DefineGlobal("__gt_trace_counter", 8, 0);
      DefineGlobal("__at_trace_cs", 40, 0); // CRITICAL_SECTION serializing trace line output
      DefineSymdata("__at_tag_spawn", "spawn #\0"u8.ToArray());
      DefineSymdata("__at_tag_await", "await #\0"u8.ToArray());
      DefineSymdata("__at_tag_await_yield", " [yield]\0"u8.ToArray());
      DefineSymdata("__at_tag_await_imm", " [immediate]\0"u8.ToArray());
      DefineSymdata("__at_tag_try_await", "try_await #\0"u8.ToArray());
      DefineSymdata("__at_tag_cancel", "cancel #\0"u8.ToArray());
      DefineSymdata("__at_tag_io_yield", "io_yield #\0"u8.ToArray());
      DefineSymdata("__at_tag_io_resume", "io_resume #\0"u8.ToArray());
      DefineSymdata("__at_tag_sleep_yield", "sleep_yield #\0"u8.ToArray());
      DefineSymdata("__at_tag_sleep_resume", "sleep_resume #\0"u8.ToArray());
      DefineSymdata("__at_tag_worker_start", "worker_start #\0"u8.ToArray());
      DefineSymdata("__at_tag_worker_park", "worker_park #\0"u8.ToArray());
      DefineSymdata("__at_tag_worker_wake", "worker_wake #\0"u8.ToArray());
      DefineSymdata("__at_tag_worker_exit", "worker_exit #\0"u8.ToArray());
      DefineSymdata("__at_tag_worker_prefix", " [M=\0"u8.ToArray());
      DefineSymdata("__at_tag_worker_suffix", "]\0"u8.ToArray());
      DefineSymdata("__at_tag_nl", "\n\0"u8.ToArray());

      // Op-specific suffixes for io_yield/io_resume traces
      DefineSymdata("__at_io_op_file_exists", " [file_exists]\0"u8.ToArray());
      DefineSymdata("__at_io_op_file_delete", " [file_delete]\0"u8.ToArray());
      DefineSymdata("__at_io_op_find_first", " [find_first]\0"u8.ToArray());
      DefineSymdata("__at_io_op_find_next", " [find_next]\0"u8.ToArray());
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
    }

    EmitSchedInit();
    EmitGtSpawn();
    EmitGtTrampoline();
    EmitGtCancel();
    EmitGtContextSwitch();
    EmitGtAwait();
    EmitGtTryAwait();
    EmitGtYield();
    EmitGtProcessPendingWaiter();
    // Scheduler functions migrated to RuntimeEmitter (shared x86/ARM64)
    var schedRt = new Runtime.RuntimeEmitter(CreateBackend());
    schedRt.EmitGtEnqueue();
    schedRt.EmitGtDequeue();
    schedRt.EmitGtStealWork();
    EmitSchedWorkerLoop();
    EmitGtCleanup();
    EmitGtMorestack();
    schedRt.EmitGtTimerCheck();
    schedRt.EmitGtTimerAdd();
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
  }

  /// <summary>
  /// __sched_init(): Initialize the GMP scheduler.
  /// Allocates a TLS slot, creates P[0], sets it via TLS, initializes
  /// the inline mainThread GreenThread, and sets P[0]->currentGt to it.
  /// Called from _start before main.
  /// </summary>
  private void EmitSchedInit() {
    // Stack: [rbp-8]=P[0], [rbp-16]=procs_array, [rbp-24]=max_procs, [rbp-32]=loop_counter
    // [rbp-40]=current_P, [rbp-48]=SYSTEM_INFO (48 bytes, using stack space)
    EmitRuntimeFunctionStart("__gt_init", 0, 0x80);

    // Step 1: TLS slot allocation and gs: offset precomputation
    EmitCallImport("kernel32.dll", "TlsAlloc");
    EmitGlobalStoreReg(X86Register.Rax, "__sched_tls_index");
    // Compute gs offset: offset = index * 8 + 0x1480
    EmitBytes(0x48, 0xC1, 0xE0, 0x03); // SHL RAX, 3
    EmitAddRegImm(X86Register.Rax, 0x1480);
    EmitGlobalStoreReg(X86Register.Rax, "__sched_tls_teb_offset");

    // Step 2: Query CPU count via GetSystemInfo
    // SYSTEM_INFO is 48 bytes; dwNumberOfProcessors is at offset 0x20 (32-bit DWORD)
    EmitLeaRegMem(X86Register.Rcx, -0x78); // &sysinfo at bottom of stack frame
    EmitCallImport("kernel32.dll", "GetSystemInfo");
    EmitMovRegMem(X86Register.Rax, -0x78 + 0x20, 4); // RAX = dwNumberOfProcessors (32-bit, zero-extends)
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = max_procs
    EmitGlobalStoreReg(X86Register.Rax, "__sched_max_procs");
    EmitGlobalStoreReg(X86Register.Rax, "__sched_num_procs");

    // Step 3: Allocate P* array: VirtualAlloc(NULL, max_procs * 8, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);      // lpAddress = NULL
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);              // max_procs
    EmitBytes(0x48, 0xC1, 0xE2, 0x03);                     // SHL RDX, 3 (max_procs * 8)
    EmitMovRegImm(X86Register.R8, 0x3000);                 // MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                   // PAGE_READWRITE
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = procs_array
    EmitGlobalStoreReg(X86Register.Rax, "__sched_procs");

    // Step 4: Allocate P structs and create wake events for each
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // [rbp-32] = i = 0
    DefineLabel("__sched_init_ploop");
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // i
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // max_procs
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx); // i < max_procs
    EmitJcc("ae", "__sched_init_ploop_done");

    // Allocate P[i]: VirtualAlloc(NULL, PStructSize, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);      // lpAddress = NULL
    EmitMovRegImm(X86Register.Rdx, PStructSize);
    EmitMovRegImm(X86Register.R8, 0x3000);                 // MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                   // PAGE_READWRITE
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = P[i]

    // Store P[i] into procs_array[i]
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // procs_array
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // i
    // [procs_array + i*8] = P[i]
    // LEA RCX, [RCX + RDX*8]
    EmitBytes(0x48, 0x8D, 0x0C, 0xD1); // LEA RCX, [RCX+RDX*8]
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x89, 0x01); // MOV [RCX], RAX

    // Set P[i]->id = i
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rax, POffId, X86Register.Rcx);

    // Set P[i]->rng = i + 1 (non-zero seed for xorshift)
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, POffRng, X86Register.Rcx);

    // Create wake event: CreateEventA(NULL, FALSE=auto-reset, FALSE, NULL)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreateEventA");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // P[i]
    EmitMovIndirectMemReg(X86Register.Rcx, POffWakeEvent, X86Register.Rax);

    // Allocate a dedicated system stack for this P. Windows API calls switch
    // to this stack via EmitCallImportOnSystemStack to avoid consuming green
    // thread stack space. The size matches PSystemStackSize — see GtLayout
    // for why 256 KB rather than 8 KB.
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);     // lpAddress = NULL
    EmitMovRegImm(X86Register.Rdx, PSystemStackSize);    // dwSize
    EmitMovRegImm(X86Register.R8, 0x3000);                 // MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                   // PAGE_READWRITE
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    // systemStackSP = base + PSystemStackSize (top, since stacks grow down)
    EmitAddRegImm(X86Register.Rax, PSystemStackSize);     // RAX = top of system stack
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);             // P[i]
    EmitMovIndirectMemReg(X86Register.Rcx, POffSystemStackSP, X86Register.Rax);

    // Increment loop counter
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitJmp("__sched_init_ploop");
    DefineLabel("__sched_init_ploop_done");

    // Step 5: Set P[0] as the active worker for this OS thread
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // procs_array
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, 0); // P[0] = procs_array[0]
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // [rbp-8] = P[0]

    // P[0]->status = 1 (active)
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, POffStatus, X86Register.Rcx);

    // Set TLS slot to P[0]
    EmitGlobalLoadReg(X86Register.Rcx, "__sched_tls_index");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitCallImport("kernel32.dll", "TlsSetValue");

    // Note: P[0]->systemStackSP is saved by _start after __gt_init returns,
    // at the top-level stack frame where there is plenty of room for system stack calls.

    // Step 6: Initialize P[0]->mainThread
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, POffMainThread + GtOffStatus, X86Register.Rcx);

    // Save the OS thread's TIB StackBase and StackLimit into the main thread struct.
    // When we context-switch away from main, these values will be saved/restored
    // so that Win32 APIs on green threads see correct stack bounds.
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = P[0]
    // MOV RCX, gs:[0x08]  (TIB StackBase)
    EmitBytes(0x65, 0x48, 0x8B, 0x0C, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rax, POffMainThread + GtOffTibStackBase, X86Register.Rcx);
    // MOV RCX, gs:[0x10]  (TIB StackLimit)
    EmitBytes(0x65, 0x48, 0x8B, 0x0C, 0x25, 0x10, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rax, POffMainThread + GtOffTibStackLimit, X86Register.Rcx);

    // P[0]->currentGt = &P[0]->mainThread
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitLeaRegIndirect(X86Register.Rcx, X86Register.Rax, POffMainThread);
    EmitMovIndirectMemReg(X86Register.Rax, POffCurrentGt, X86Register.Rcx);

    // __sched_active_workers = 1
    EmitMovRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__sched_active_workers");

    // Step 7: Initialize CRITICAL_SECTIONs
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_global_queue_cs");
    EmitCallImport("kernel32.dll", "InitializeCriticalSection");
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_all_cs");
    EmitCallImport("kernel32.dll", "InitializeCriticalSection");
    EmitGlobalLeaReg(X86Register.Rcx, "__gt_timer_cs");
    EmitCallImport("kernel32.dll", "InitializeCriticalSection");

    if (Compiler.AsyncTrace) {
      EmitGlobalLeaReg(X86Register.Rcx, "__at_trace_cs");
      EmitCallImport("kernel32.dll", "InitializeCriticalSection");
    }

    // Step 8: Initialize slab allocator CRITICAL_SECTIONs and call __slab_init
    EmitGlobalLeaReg(X86Register.Rcx, "__slab_mspan_pool_lock");
    EmitCallImport("kernel32.dll", "InitializeCriticalSection");
    for (int i = 0; i < 18; i++) {
      EmitGlobalLeaReg(X86Register.Rcx, $"__slab_mcentral_lock_{i}");
      EmitCallImport("kernel32.dll", "InitializeCriticalSection");
    }
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "__slab_init")); EmitDword(0);

    // Step 9: Install the CPU-fault handler (VEH on Windows).
    EmitInstallFaultHandler("__gt_fault_handler_thunk");

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_spawn(func_ptr_rcx, arg_count_rdx, arg_buf_r8) -> promise in RAX
  /// Allocates a GreenThread struct and a 2KB stack, initializes them,
  /// enqueues the thread in the run queue, and returns the GreenThread ptr.
  /// </summary>
  private void EmitGtSpawn() {
    EmitRuntimeFunctionStart("__gt_spawn", 3, 0x40);
    // [rbp-0x08] = func_ptr (saved by prologue into rcx slot)
    // [rbp-0x10] = arg_count (saved by prologue into rdx slot)
    // [rbp-0x18] = arg_buf (saved by prologue into r8 slot)
    // [rbp-0x20] = gt_ptr (allocated struct)
    // [rbp-0x28] = stack_base (VirtualAlloc'd)

    // Allocate GreenThread struct via mm_raw_alloc (try free list first)
    // Load P* via TLS
    EmitGlobalLoadReg(X86Register.R10, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R10, X86Register.R10, 0); // R10 = P*
    EmitMovMemReg(-0x30, X86Register.R10, 8); // [rbp-0x30] = P* (save)

    // if P->freeListHead == NULL, goto alloc_new
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, POffFreeListHead);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_spawn_alloc_new");

    // Free list hit: gt = P->freeListHead, P->freeListHead = gt->next, P->freeListLen--
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save gt_ptr = head
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffNext); // RCX = gt->next
    EmitMovRegMem(X86Register.R10, -0x30, 8); // R10 = P*
    EmitMovIndirectMemReg(X86Register.R10, POffFreeListHead, X86Register.Rcx); // P->freeListHead = next
    // DEC P->freeListLen
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, POffFreeListLen);
    EmitSubRegImm(X86Register.Rax, 1);
    EmitMovIndirectMemReg(X86Register.R10, POffFreeListLen, X86Register.Rax);
    EmitJmp("__gt_spawn_got_gt");

    // alloc_new: allocate GreenThread struct via mm_raw_alloc
    DefineLabel("__gt_spawn_alloc_new");
    EmitMovRegImm(X86Register.Rcx, GtStructSize);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save gt_ptr

    DefineLabel("__gt_spawn_got_gt");

    // Allocate initial stack via VirtualAlloc(NULL, size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);        // lpAddress = NULL
    EmitMovRegImm(X86Register.Rdx, GtInitialStackSize);       // dwSize (grows via __gt_morestack)
    EmitMovRegImm(X86Register.R8, 0x3000);                    // flAllocationType = MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                      // flProtect = PAGE_READWRITE
    EmitCallImportOnSystemStack("kernel32.dll", "VirtualAlloc");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save stack_base

    // Initialize GreenThread fields
    var gt = X86Register.R10;
    EmitMovRegMem(gt, -0x20, 8); // gt = gt_ptr

    // gt.stack_base = stack_base
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitMovIndirectMemReg(gt, GtOffStackBase, X86Register.Rax);

    // gt.stack_size = GtInitialStackSize
    EmitMovRegImm(X86Register.Rax, GtInitialStackSize);
    EmitMovIndirectMemReg(gt, GtOffStackSize, X86Register.Rax);

    // gt.stackguard = stack_base + GtStackGuardMargin
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitAddRegImm(X86Register.Rax, GtStackGuardMargin);
    EmitMovIndirectMemReg(gt, GtOffStackGuard, X86Register.Rax);

    // gt.tib_stack_base = stack_base + stack_size (top of stack for TIB)
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitAddRegImm(X86Register.Rax, GtInitialStackSize);
    EmitMovIndirectMemReg(gt, GtOffTibStackBase, X86Register.Rax);

    // gt.tib_stack_limit = stack_base (bottom of stack for TIB)
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitMovIndirectMemReg(gt, GtOffTibStackLimit, X86Register.Rax);

    // gt.func_ptr = func_ptr
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovIndirectMemReg(gt, GtOffFuncPtr, X86Register.Rax);

    // gt.arg_buf = arg_buf
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovIndirectMemReg(gt, GtOffArgBuf, X86Register.Rax);

    // gt.status = ready (0)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(gt, GtOffStatus, X86Register.Rax);

    // gt.result = 0
    EmitMovIndirectMemReg(gt, GtOffResult, X86Register.Rax);
    // gt.waiter = 0
    EmitMovIndirectMemReg(gt, GtOffWaiter, X86Register.Rax);
    // gt.next = 0
    EmitMovIndirectMemReg(gt, GtOffNext, X86Register.Rax);
    // gt.threw = 0
    EmitMovIndirectMemReg(gt, GtOffThrew, X86Register.Rax);

    // gt.ioYielded = 1 (start as "yielded" so the first __io_complete_gt doesn't spin)
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovIndirectMemReg(gt, GtOffIoYielded, X86Register.Rax);

    // Initialize stack: compute stack_top = stack_base + stack_size
    EmitMovRegMem(X86Register.Rax, -0x28, 8);           // stack_base
    EmitAddRegImm(X86Register.Rax, GtInitialStackSize);  // stack_top
    // Place __gt_trampoline as the return address at the top
    // When context_switch does 'ret', it will jump to __gt_trampoline
    // Layout at top of stack (growing downward):
    //   [stack_top - 8]  = __gt_trampoline (return address for initial 'ret')
    //   [stack_top - 16] = 0 (saved rbx slot for context_switch pop)
    //   [stack_top - 24] = 0 (saved rsi)
    //   [stack_top - 32] = 0 (saved rdi)
    //   [stack_top - 40] = 0 (saved r12)
    //   [stack_top - 48] = 0 (saved r13)
    //   [stack_top - 56] = 0 (saved r14)
    //   [stack_top - 64] = 0 (saved r15)
    // So initial RSP = stack_top - 64

    // Zero the 8 slots
    var stackTop = X86Register.R11;
    EmitMovRegReg(stackTop, X86Register.Rax); // R11 = stack_top
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    for (int i = 1; i <= 8; i++) {
      // MOV [stack_top - i*8], 0
      EmitMovIndirectMemReg(stackTop, -i * 8, X86Register.Rcx);
    }

    // Store __gt_trampoline address at [stack_top - 8]
    // LEA rax, [rip + __gt_trampoline]
    EmitByte(0x48); EmitByte(0x8D); EmitByte(0x05); // LEA rax, [rip+disp32]
    _jumpFixups.Add((_code.Count, "__gt_trampoline"));
    EmitDword(0);
    EmitMovIndirectMemReg(stackTop, -8, X86Register.Rax);

    // gt.rsp = stack_top - 64 (7 callee-saved regs + return address)
    EmitMovRegReg(X86Register.Rax, stackTop);
    EmitSubRegImm(X86Register.Rax, 64);
    EmitMovRegMem(gt, -0x20, 8); // reload gt
    EmitMovIndirectMemReg(gt, GtOffRsp, X86Register.Rax);

    // gt.rbp = 0 (no frame pointer yet)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(gt, GtOffRbp, X86Register.Rax);

    // Add to all-threads list under lock (prepend at head)
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_all_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "EnterCriticalSection");
    EmitMovRegMem(gt, -0x20, 8); // reload gt
    EmitGlobalLoadReg(X86Register.Rax, "__gt_all_head");
    EmitMovIndirectMemReg(gt, GtOffAllNext, X86Register.Rax);
    EmitGlobalStoreReg(gt, "__gt_all_head");
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_all_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "LeaveCriticalSection");

    // Atomically increment live thread count
    EmitGlobalLeaReg(X86Register.Rax, "__gt_live_count");
    EmitBytes(0xF0, 0x48, 0xFF, 0x00); // LOCK INC qword [RAX]

    // Enqueue: call __gt_enqueue(gt)
    EmitMovRegReg(X86Register.Rcx, gt);
    EmitCallRuntimeLabel("__gt_enqueue");

    // Return gt_ptr as the promise
    EmitMovRegMem(X86Register.Rax, -0x20, 8);

    if (Compiler.AsyncTrace) {
      // Assign trace ID atomically: LOCK XADD to get unique ID
      EmitMovMemReg(-0x30, X86Register.Rax, 8); // save gt_ptr (RAX)
      EmitMovRegImm(X86Register.Rcx, 1);
      EmitGlobalLeaReg(X86Register.Rax, "__gt_trace_counter");
      // LOCK XADD [RAX], RCX — RCX gets old value, [RAX] += RCX
      EmitBytes(0xF0, 0x48, 0x0F, 0xC1, 0x08);
      EmitAddRegImm(X86Register.Rcx, 1); // RCX = new trace ID (old + 1)
      EmitMovRegMem(X86Register.Rax, -0x20, 8); // gt_ptr
      EmitMovIndirectMemReg(X86Register.Rax, GtOffTraceId, X86Register.Rcx);
      // Trace: "spawn #N [M=W]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_spawn");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x20, 8);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
      EmitMovRegMem(X86Register.Rax, -0x30, 8); // restore gt_ptr
    }

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_trampoline(): Entry point for new green threads.
  /// Loaded from the green thread's GreenThread struct, calls the target function
  /// with args from the arg buffer, stores the result, and yields.
  /// </summary>
  private void EmitGtTrampoline() {
    DefineLabel("__gt_trampoline");
    // No standard prologue — we are entered via context switch 'ret'
    // Set up a frame for our local use.
    // Frame layout:
    //   [rbp-0x08]      gt
    //   [rbp-0x10]      func_ptr
    //   [rbp-0x18]      arg_buf (kept until after we copy managed_mask + args
    //                            to caller-saved slots below, then freed)
    //   [rbp-0x20]      arg_count
    //   [rbp-0x28..-0x60] saved arg copies (8 slots, ABI order)
    //   [rbp-0x68]      saved result (RAX from target call)
    //   [rbp-0x70]      saved threw flag (RDX from target call)
    //   [rbp-0x78]      managed_mask (bit i set ⇒ arg i is a refcounted heap ptr)
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x80); // local frame (16-aligned)

    // Process any pending waiter from a previous GT that completed on this P.
    // This handles the case where __gt_yield_completed dequeued a new GT (this one)
    // without going through the worker loop first. We're now safely off the old
    // GT's stack, so the waiter can be woken without risk of use-after-free.
    EmitCallRuntimeLabel("__gt_process_pending_waiter");

    // Load current GreenThread
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovMemReg(-0x08, X86Register.R10, 8); // [rbp-8] = gt

    // Load func_ptr and arg_buf from GreenThread
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R10, GtOffFuncPtr); // R11 = func_ptr
    EmitMovMemReg(-0x10, X86Register.R11, 8); // [rbp-0x10] = func_ptr
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R10, GtOffArgBuf);
    EmitMovMemReg(-0x18, X86Register.R11, 8); // [rbp-0x18] = arg_buf

    // Load arg count from [arg_buf + 0]
    EmitMovRegMem(X86Register.R11, -0x18, 8);           // R11 = arg_buf
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.R11, 0); // RCX = arg_count
    EmitMovMemReg(-0x20, X86Register.Rcx, 8); // [rbp-0x20] = arg_count
    // Load managed_mask from [arg_buf + 8]
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.R11, 8);
    EmitMovMemReg(-0x78, X86Register.Rcx, 8); // [rbp-0x78] = managed_mask

    // Load args from buffer into calling convention registers
    // Args are at [arg_buf + 16 + i*8] (count at +0, mask at +8, args at +16).
    // Calling convention: rcx, rdx, r8, r9, rsi, rdi, rax, rbx
    // Use R10 for arg_count to avoid clobbering calling convention registers
    EmitMovRegMem(X86Register.R10, -0x20, 8); // R10 = arg_count (persists across loop)
    for (int i = 0; i < 8; i++) {
      var skipLabel = $"__gt_tramp_skip_arg{i}";
      // if arg_count <= i, skip
      EmitCmpRegImm(X86Register.R10, i + 1);
      EmitJcc("l", skipLabel); // skip if arg_count < i+1
      // Load arg[i] from [arg_buf + 16 + i*8]
      EmitMovRegMem(X86Register.R11, -0x18, 8); // R11 = arg_buf
      EmitMovRegIndirectMem(_abiArgRegs[i], X86Register.R11, 16 + i * 8);
      DefineLabel(skipLabel);
    }

    // Free arg_buf via mm_raw_free before calling target
    // Save loaded args on stack first
    for (int i = 0; i < 8; i++) {
      EmitMovMemReg(-0x28 - i * 8, _abiArgRegs[i], 8);
    }
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // rcx = arg_buf

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    // Restore args
    for (int i = 0; i < 8; i++) {
      EmitMovRegMem(_abiArgRegs[i], -0x28 - i * 8, 8);
    }

    // Call target function
    EmitMovRegMem(X86Register.R10, -0x10, 8); // R10 = func_ptr
    // call r10
    EmitBytes(0x41, 0xFF, 0xD2); // CALL R10

    // Save result + threw flag before doing managed-arg cleanup (mm_decref
    // clobbers RAX/RDX).
    EmitMovMemReg(-0x68, X86Register.Rax, 8); // [rbp-0x68] = result
    EmitMovMemReg(-0x70, X86Register.Rdx, 8); // [rbp-0x70] = threw

    // Drop the spawn-time refs on managed args. For each set bit in the saved
    // managed_mask, mm_decref the matching saved-arg slot. Without this, the
    // spawn-site incref leaks the arg's storage. The non-zero guard mirrors
    // user-emitted decref sites — a managed pointer can legitimately be NULL
    // if the caller threaded a null-shaped value through (e.g. an optional
    // field) and mm_decref panics on NULL.
    for (int i = 0; i < 8; i++) {
      var skipLabel = $"__gt_tramp_decref_skip_arg{i}";
      EmitMovRegMem(X86Register.Rax, -0x78, 8);
      EmitBytes(0x48, 0xA9); EmitDword(1 << i); // TEST RAX, imm32
      EmitJcc("z", skipLabel);
      EmitMovRegMem(X86Register.Rcx, -0x28 - i * 8, 8);
      EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
      EmitJcc("z", skipLabel);
      EmitCallRuntimeLabel("mm_decref", zeroSecondArg: Compiler.MmTrace);
      DefineLabel(skipLabel);
    }

    // Reload saved result/threw into the storage convention used below.
    EmitMovRegMem(X86Register.Rax, -0x68, 8);
    EmitMovMemReg(-0x58, X86Register.Rax, 8); // [rbp-0x58] = result (save)
    EmitMovRegMem(X86Register.Rdx, -0x70, 8);
    EmitMovMemReg(-0x60, X86Register.Rdx, 8); // [rbp-0x60] = threw (save)
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegMem(X86Register.Rax, -0x58, 8);
    EmitMovIndirectMemReg(X86Register.R10, GtOffResult, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, -0x60, 8);
    EmitMovIndirectMemReg(X86Register.R10, GtOffThrew, X86Register.Rax);

    // Atomically decrement live thread count
    EmitGlobalLeaReg(X86Register.Rax, "__gt_live_count");
    EmitBytes(0xF0, 0x48, 0xFF, 0x08); // LOCK DEC qword [RAX]

    // Remove from all-threads list under lock
    // Save R10 (current gt) before calling EnterCriticalSection
    EmitMovMemReg(-0x38, X86Register.R10, 8);
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_all_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "EnterCriticalSection");
    EmitMovRegMem(X86Register.R10, -0x38, 8); // restore R10
    // Walk: prev=NULL, cur=__gt_all_head; while cur != NULL && cur != R10: prev=cur, cur=cur.all_next
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_all_head"); // RCX = cur
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);    // RDX = prev = NULL
    DefineLabel("__gt_tramp_alllist_loop");
    // Guard: if cur == NULL, thread not found (defensive — should not happen)
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_tramp_alllist_done"); // not found — skip removal
    EmitCmpRegReg(X86Register.Rcx, X86Register.R10);
    EmitJcc("e", "__gt_tramp_alllist_found");
    // prev = cur; cur = cur->all_next
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, GtOffAllNext);
    EmitJmp("__gt_tramp_alllist_loop");
    DefineLabel("__gt_tramp_alllist_found");
    // cur == R10; unlink: if prev==NULL: __gt_all_head = cur->all_next; else prev->all_next = cur->all_next
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffAllNext); // RAX = cur->all_next
    EmitBytes(0x48, 0x85, 0xD2); // TEST RDX, RDX
    EmitJcc("nz", "__gt_tramp_alllist_prev");
    // prev == NULL: update head
    EmitGlobalStoreReg(X86Register.Rax, "__gt_all_head");
    EmitJmp("__gt_tramp_alllist_done");
    DefineLabel("__gt_tramp_alllist_prev");
    // prev->all_next = cur->all_next
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffAllNext, X86Register.Rax);
    DefineLabel("__gt_tramp_alllist_done");
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_all_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "LeaveCriticalSection");

    // Reload R10 = current gt (list walk may have modified RCX/RDX)
    EmitLoadCurrentGtInline(X86Register.R10);

    // Set status = completed
    EmitMovRegImm(X86Register.Rax, GtStatusCompleted);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax);

    // Defer waiter wakeup: store waiter in P->pendingWaiter. The actual wakeup
    // happens AFTER __gt_yield_completed context-switches off this GT's stack.
    // This prevents a race where the waiter frees the GT's stack while we're
    // still running on it (the waiter's __gt_await does VirtualFree + mm_raw_free).
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R10, GtOffWaiter);
    // Set waiter.status = ready (safe: the waiter won't be scheduled until
    // we explicitly enqueue/signal it in the pending waiter processing).
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", "__gt_tramp_no_waiter");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R11, GtOffStatus, X86Register.Rax);
    DefineLabel("__gt_tramp_no_waiter");
    // Store waiter (or NULL) in P->pendingWaiter via inline TLS
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0); // RAX = P*
    EmitMovIndirectMemReg(X86Register.Rax, POffPendingWaiter, X86Register.R11);

    // Yield to next thread (never returns for completed threads)
    EmitCallRuntimeLabel("__gt_yield_completed");
    // int3 — should never reach here
    EmitByte(0xCC);
  }

  /// <summary>
  /// __gt_context_switch(from_rcx, to_rdx): Core context switch.
  /// Saves callee-saved registers + RSP/RBP on 'from', restores from 'to'.
  /// Updates __gt_current to 'to'.
  /// </summary>
  private void EmitGtContextSwitch() {
    DefineLabel("__gt_context_switch");
    // Save callee-saved regs on current stack (push order matches restore pop order)
    EmitPushReg(X86Register.Rbx);
    EmitPushReg(X86Register.Rsi);
    EmitPushReg(X86Register.Rdi);
    EmitPushReg(X86Register.R12);
    EmitPushReg(X86Register.R13);
    EmitPushReg(X86Register.R14);
    EmitPushReg(X86Register.R15);

    // Save RSP and RBP to 'from'
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffRsp, X86Register.Rsp);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffRbp, X86Register.Rbp);

    // Restore RSP and RBP from 'to'
    EmitMovRegIndirectMem(X86Register.Rsp, X86Register.Rdx, GtOffRsp);
    EmitMovRegIndirectMem(X86Register.Rbp, X86Register.Rdx, GtOffRbp);

    // Save TIB StackBase (gs:[0x08]) and StackLimit (gs:[0x10]) to 'from'.
    // Win32 APIs use these for stack overflow detection (__chkstk) and SEH;
    // without updating them, calling Win32 on a green thread's stack crashes.
    // MOV RAX, gs:[0x08]
    EmitBytes(0x65, 0x48, 0x8B, 0x04, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffTibStackBase, X86Register.Rax);
    // MOV RAX, gs:[0x10]
    EmitBytes(0x65, 0x48, 0x8B, 0x04, 0x25, 0x10, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffTibStackLimit, X86Register.Rax);

    // Restore TIB StackBase and StackLimit from 'to'
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdx, GtOffTibStackBase);
    // MOV gs:[0x08], RAX
    EmitBytes(0x65, 0x48, 0x89, 0x04, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdx, GtOffTibStackLimit);
    // MOV gs:[0x10], RAX
    EmitBytes(0x65, 0x48, 0x89, 0x04, 0x25, 0x10, 0x00, 0x00, 0x00);

    // Update P->currentGt = to (via inline gs: TLS access)
    // We cannot call TlsGetValue here because the stack is being switched.
    // Instead, use the precomputed gs: offset for inline TLS access.
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    // MOV RAX, GS:[RAX] — load P* via gs: segment
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0);
    // MOV [RAX + POffCurrentGt], RDX — store 'to' as current GT
    EmitMovIndirectMemReg(X86Register.Rax, POffCurrentGt, X86Register.Rdx);

    // Restore callee-saved regs from new stack
    EmitPopReg(X86Register.R15);
    EmitPopReg(X86Register.R14);
    EmitPopReg(X86Register.R13);
    EmitPopReg(X86Register.R12);
    EmitPopReg(X86Register.Rdi);
    EmitPopReg(X86Register.Rsi);
    EmitPopReg(X86Register.Rbx);

    EmitByte(0xC3); // ret (returns to new thread's saved return address)
  }

  /// <summary>
  /// Emit inline code to return a GT struct (at [rbp-0x08]) to the current P's free list,
  /// or free it via mm_raw_free if the list is full. Clobbers RAX, RCX, R11.
  /// Labels are prefixed with the given labelPrefix to avoid collisions.
  /// </summary>
  private void EmitGtReturnToFreeList(string labelPrefix) {
    // Load P* via TLS
    EmitGlobalLoadReg(X86Register.R11, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R11, X86Register.R11, 0); // R11 = P*
    // Check P->freeListLen >= 64
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R11, POffFreeListLen);
    EmitCmpRegImm(X86Register.Rax, 64);
    EmitJcc("ae", $"{labelPrefix}_free_full");
    // Free list not full: prepend gt to free list
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = gt struct
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R11, POffFreeListHead);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffNext, X86Register.Rax); // gt->next = old head
    EmitMovIndirectMemReg(X86Register.R11, POffFreeListHead, X86Register.Rcx); // P->freeListHead = gt
    // P->freeListLen++
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R11, POffFreeListLen);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovIndirectMemReg(X86Register.R11, POffFreeListLen, X86Register.Rax);
    EmitJmp($"{labelPrefix}_free_done");

    DefineLabel($"{labelPrefix}_free_full");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // rcx = gt struct

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    DefineLabel($"{labelPrefix}_free_done");
  }

  /// <summary>
  /// __gt_await(promise_rcx) -> result in RAX
  /// If the promise is already completed, extract result and return.
  /// Otherwise, set current thread to waiting, set promise.waiter = current, and switch to next.
  /// </summary>
  private void EmitGtAwait() {
    EmitRuntimeFunctionStart("__gt_await", 1, 0x30);
    // [rbp-0x08] = promise (saved by prologue)

    if (Compiler.AsyncTrace) {
      // [rbp-0x18] = yield flag (0=immediate, 1=yielded)
      EmitXorRegReg(X86Register.Rax, X86Register.Rax);
      EmitMovMemReg(-0x18, X86Register.Rax, 8);
    }

    // Check if promise is already completed
    EmitMovRegMem(X86Register.R10, -0x08, 8);                           // R10 = promise
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus); // RAX = promise.status
    EmitCmpRegImm(X86Register.Rax, GtStatusCompleted);
    EmitJcc("z", "__gt_await_done");

    if (Compiler.AsyncTrace) {
      EmitMovRegImm(X86Register.Rax, 1);
      EmitMovMemReg(-0x18, X86Register.Rax, 8); // flag = yielded
    }

    // Not yet completed: block current thread
    EmitLoadCurrentGtInline(X86Register.R11); // R11 = current
    // current.status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R11, GtOffStatus, X86Register.Rax);

    // promise.waiter = current
    EmitMovRegMem(X86Register.R10, -0x08, 8); // R10 = promise
    EmitMovIndirectMemReg(X86Register.R10, GtOffWaiter, X86Register.R11);

    // Scheduling loop: find something to run and context-switch to it.
    // Whether we're a worker's GT or the main thread on P[0], we always
    // context-switch so our state is properly saved and can be restored.
    DefineLabel("__gt_await_sched");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_timer_check");

    // Check if promise already completed (could have been completed by another worker)
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusCompleted);
    EmitJcc("z", "__gt_await_done");

    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__gt_await_has_next");

    // Nothing to run. Check if we're on a worker (not mainThread):
    // if so, switch to P->mainThread to let the worker loop park.
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("nz", "__gt_await_switch_main");

    // We ARE the mainThread: park briefly using P->wakeEvent, then retry
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0); // RAX = P*
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffWakeEvent);
    EmitMovRegImm(X86Register.Rdx, 50); // 50ms timeout
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp("__gt_await_sched");

    DefineLabel("__gt_await_switch_main");
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Resume here when re-woken — fall through to done
    EmitJmp("__gt_await_done");

    DefineLabel("__gt_await_has_next");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save next

    // Set next.status = running
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);

    // dbg: await_run (next gt). Emit BEFORE context switch.
    EmitDbgEventInline(DsEvDbgAwaitDeqRun, /*gtSlotOffset=*/-0x10);

    // Context switch: from=current, to=next
    EmitLoadCurrentGtInline(X86Register.Rcx); // from = current
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);          // to = next
    EmitCallRuntimeLabel("__gt_context_switch");
    // Resume here when woken (via waiter mechanism).
    // Signal that the GT's context switch is complete so __io_complete_gt can safely enqueue it.
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    // Check if promise is done — if not, keep scheduling
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusCompleted);
    EmitJcc("nz", "__gt_await_sched");

    DefineLabel("__gt_await_done");

    if (Compiler.AsyncTrace) {
      // Trace: "await #N [immediate|yield] [M=W]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_await");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // promise
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitMovRegMem(X86Register.Rax, -0x18, 8);
      EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
      EmitJcc("nz", "__gt_await_trace_yield");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_await_imm");
      EmitJmp("__gt_await_trace_print");
      DefineLabel("__gt_await_trace_yield");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_await_yield");
      DefineLabel("__gt_await_trace_print");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Extract result from promise
    EmitMovRegMem(X86Register.R10, -0x08, 8);                           // R10 = promise
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffResult); // RAX = promise.result

    // Free the green thread's stack
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.R10, GtOffStackBase);
    // Only free if stack_base != 0 (main thread has no allocated stack)
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "__gt_await_skip_free_stack");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result
    // VirtualFree(stack_base, 0, MEM_RELEASE=0x8000)
    // rcx already = stack_base
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);        // dwSize = 0
    EmitMovRegImm(X86Register.R8, 0x8000);                    // dwFreeType = MEM_RELEASE
    EmitCallImportOnSystemStack("kernel32.dll", "VirtualFree");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result

    DefineLabel("__gt_await_skip_free_stack");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result
    EmitGtReturnToFreeList("__gt_await");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_try_await(promise_rcx) -> result in RAX, threw flag in RDX
  /// Like __gt_await but also returns the threw flag from the green thread.
  /// Used for 'try await' on promises from async throwing functions.
  /// </summary>
  private void EmitGtTryAwait() {
    EmitRuntimeFunctionStart("__gt_try_await", 1, 0x40);
    // [rbp-0x08] = promise (saved by prologue)

    if (Compiler.AsyncTrace) {
      // [rbp-0x20] = yield flag (0=immediate)
      EmitXorRegReg(X86Register.Rax, X86Register.Rax);
      EmitMovMemReg(-0x20, X86Register.Rax, 8);
    }

    // Check if promise is already completed
    EmitMovRegMem(X86Register.R10, -0x08, 8);                           // R10 = promise
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus); // RAX = promise.status
    EmitCmpRegImm(X86Register.Rax, GtStatusCompleted);
    EmitJcc("z", "__gt_try_await_done");

    if (Compiler.AsyncTrace) {
      EmitMovRegImm(X86Register.Rax, 1);
      EmitMovMemReg(-0x20, X86Register.Rax, 8); // flag = yielded
    }

    // Not yet completed: block current thread
    EmitLoadCurrentGtInline(X86Register.R11); // R11 = current
    // current.status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R11, GtOffStatus, X86Register.Rax);

    // promise.waiter = current
    EmitMovRegMem(X86Register.R10, -0x08, 8); // R10 = promise
    EmitMovIndirectMemReg(X86Register.R10, GtOffWaiter, X86Register.R11);

    // Scheduling loop: find something to run and context-switch to it.
    DefineLabel("__gt_try_await_sched");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");

    // Check if promise already completed
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusCompleted);
    EmitJcc("z", "__gt_try_await_done");

    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__gt_try_await_has_next");

    // Nothing to run. If we're on a worker, switch to P->mainThread.
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("nz", "__gt_try_await_switch_main");

    // We ARE the mainThread: park briefly, then retry
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    EmitByte(0x65);
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffWakeEvent);
    EmitMovRegImm(X86Register.Rdx, 50); // 50ms timeout
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp("__gt_try_await_sched");

    DefineLabel("__gt_try_await_switch_main");
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp("__gt_try_await_done");

    DefineLabel("__gt_try_await_has_next");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save next

    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);

    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Signal that the GT's context switch is complete so __io_complete_gt can safely enqueue it.
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    // Check if promise is done
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusCompleted);
    EmitJcc("nz", "__gt_try_await_sched");

    DefineLabel("__gt_try_await_done");

    if (Compiler.AsyncTrace) {
      // Trace: "try_await #N [immediate|yield] [M=W]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_try_await");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // promise
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitMovRegMem(X86Register.Rax, -0x20, 8);
      EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
      EmitJcc("nz", "__gt_try_await_trace_yield");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_await_imm");
      EmitJmp("__gt_try_await_trace_print");
      DefineLabel("__gt_try_await_trace_yield");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_await_yield");
      DefineLabel("__gt_try_await_trace_print");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Extract result and threw flag from promise
    EmitMovRegMem(X86Register.R10, -0x08, 8);                             // R10 = promise
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffResult);  // RAX = promise.result
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.R10, GtOffThrew);   // RDX = promise.threw

    // Free the green thread's stack
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.R10, GtOffStackBase);
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "__gt_try_await_skip_free_stack");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result
    EmitMovMemReg(-0x18, X86Register.Rdx, 8); // save threw
    // VirtualFree(stack_base, 0, MEM_RELEASE=0x8000)
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000);
    EmitCallImportOnSystemStack("kernel32.dll", "VirtualFree");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // restore threw

    DefineLabel("__gt_try_await_skip_free_stack");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result
    EmitMovMemReg(-0x18, X86Register.Rdx, 8); // save threw
    EmitGtReturnToFreeList("__gt_try_await");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // restore threw

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_yield(): Yield current thread — enqueue self and switch to next.
  /// Called from __gt_yield_completed for completed threads.
  /// </summary>
  private void EmitGtYield() {
    // __gt_yield_completed: called by the trampoline when a GT finishes.
    // Always switches to P->mainThread — the worker loop handles scheduling.
    // Never returns (the completed GT is done).
    DefineLabel("__gt_yield_completed");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x20);

    // Switch back to P->mainThread unconditionally. The trampoline has already
    // set the pending waiter; the worker loop's first action is to call
    // __gt_process_pending_waiter which enqueues the awaiter.
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    // If current IS the main thread (e.g. during cleanup drain), just return.
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("z", "__gt_yield_return");
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Never resumes here for a completed GT — the worker loop continues.

    DefineLabel("__gt_yield_return");
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
  }

  /// <summary>
  /// __gt_process_pending_waiter(): Check P->pendingWaiter and wake it if set.
  /// Called after context-switching away from a completed GT's stack.
  /// The trampoline defers waiter wakeup to avoid a race where the waiter
  /// frees the GT's stack while the trampoline is still on it.
  /// If the waiter is a regular GT (stackBase != 0), enqueues it.
  /// If the waiter is a mainThread (stackBase == 0), signals the owning P's wakeEvent.
  /// Clears P->pendingWaiter after processing.
  /// </summary>
  private void EmitGtProcessPendingWaiter() {
    EmitRuntimeFunctionStart("__gt_process_pending_waiter", 0, 0x20);

    // Load P* via inline TLS
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0); // RAX = P*

    // Load P->pendingWaiter into RCX and clear it
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffPendingWaiter);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, POffPendingWaiter, X86Register.Rdx);

    // RCX = waiter (or NULL)
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_ppw_done");

    // Check if waiter is a mainThread (stackBase == 0)
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, GtOffStackBase);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_ppw_mainthread");

    // Regular GT: enqueue into global run queue
    // RCX already = waiter
    EmitCallRuntimeLabel("__gt_enqueue");
    EmitJmp("__gt_ppw_done");

    // MainThread waiter: signal the owning P's wakeEvent
    DefineLabel("__gt_ppw_mainthread");
    // P* = waiter_ptr - POffMainThread
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx);
    EmitSubRegImm(X86Register.Rax, POffMainThread); // RAX = P*
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffWakeEvent);
    EmitCallImportOnSystemStack("kernel32.dll", "SetEvent");

    DefineLabel("__gt_ppw_done");
    EmitRuntimeFunctionEnd();
  }

  // __gt_enqueue, __gt_dequeue, __gt_steal_work: migrated to RuntimeEmitter.Scheduler.cs

  /// <summary>
  /// __sched_worker_loop(): Entry point for worker OS threads.
  /// Called by CreateThread with lpParameter = P* in RCX.
  /// Sets up TLS for this thread, initializes P->mainThread, then loops:
  ///   1. Check shutdown flag → if set, return (thread exits)
  ///   2. Check I/O completions
  ///   3. Try to dequeue from global run queue
  ///   4. If got work → set status=running, context switch from P->mainThread to it
  ///   5. If no work → mark self idle, WaitForSingleObject on P->wakeEvent, unmark idle, loop
  /// When a green thread completes or yields, it switches back to P->mainThread,
  /// which resumes in this loop.
  /// </summary>
  private void EmitSchedWorkerLoop() {
    // CreateThread callback: DWORD WINAPI ThreadProc(LPVOID lpParameter)
    // Windows x64 ABI: RCX = lpParameter = P*
    // We need a frame for local variables:
    // [rbp-0x08] = P*
    // [rbp-0x10] = dequeued GT
    EmitRuntimeFunctionStart("__sched_worker_loop", 1, 0x40);

    // Step 1: Store P* locally
    // (already saved to [rbp-0x08] by EmitRuntimeFunctionStart)

    // Step 2: Set TLS slot to this P*
    EmitGlobalLoadReg(X86Register.Rcx, "__sched_tls_index");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // P*
    EmitCallImport("kernel32.dll", "TlsSetValue");

    // Note: P->systemStackSP was already set during __gt_init (VirtualAlloc'd system stack).

    // Step 3: Initialize P->mainThread for this OS thread
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = P*

    // mainThread.status = running
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, POffMainThread + GtOffStatus, X86Register.Rcx);

    // Save the OS thread's TIB StackBase and StackLimit into the worker's mainThread struct.
    // The first context switch will save these to 'from', and when we switch back,
    // the TIB will be restored to the OS thread's real stack bounds.
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = P*
    // MOV RCX, gs:[0x08]  (TIB StackBase)
    EmitBytes(0x65, 0x48, 0x8B, 0x0C, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rax, POffMainThread + GtOffTibStackBase, X86Register.Rcx);
    // MOV RCX, gs:[0x10]  (TIB StackLimit)
    EmitBytes(0x65, 0x48, 0x8B, 0x0C, 0x25, 0x10, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rax, POffMainThread + GtOffTibStackLimit, X86Register.Rcx);

    // P->currentGt = &P->mainThread
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitLeaRegIndirect(X86Register.Rcx, X86Register.Rax, POffMainThread);
    EmitMovIndirectMemReg(X86Register.Rax, POffCurrentGt, X86Register.Rcx);

    // P->status = 1 (active)
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, POffStatus, X86Register.Rcx);

    // Atomically increment active workers
    EmitGlobalLeaReg(X86Register.Rax, "__sched_active_workers");
    EmitBytes(0xF0, 0x48, 0xFF, 0x00); // LOCK INC qword [RAX]

    if (Compiler.AsyncTrace) {
      // Trace: "worker_start #N [M=N]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_worker_start");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // === Main worker loop ===
    DefineLabel("__sched_wloop_top");

    // Process any pending waiter left by a completed GT's trampoline.
    // This must run AFTER the context switch that left the completed GT's stack,
    // so the GT's stack is no longer in use when the waiter frees it.
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__gt_timer_check");

    // Check shutdown flag
    EmitGlobalLoadReg(X86Register.Rax, "__sched_shutdown_flag");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__sched_wloop_exit");

    // Try to dequeue from global run queue
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__sched_wloop_park"); // no work → park

    // Got work: fall through to __sched_wloop_run_gt with RAX = dequeued GT.
    DefineLabel("__sched_wloop_run_gt");
    // Save GT, set status=running, context switch to it
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save GT
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);

    // dbg: wloop_run_gt (gt). Emit BEFORE context switch so the trace records
    // intent to run this GT on this worker. Uses inline call to __ds_emit_dbg
    // with internal calling convention (Rcx=event, Rdx=gt, R8=p_id, R9=0, Rsi=0, Rdi=0).
    EmitDbgEventInline(DsEvDbgWloopRunGt, /*gtSlotOffset=*/-0x10);

    // Context switch: from = P->mainThread, to = dequeued GT
    EmitLeaMainThreadInline(X86Register.Rcx); // from = &P->mainThread
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // to = dequeued GT
    EmitCallRuntimeLabel("__gt_context_switch");

    // We resume here when the green thread yields/completes back to mainThread.
    // Signal that the GT's context switch is complete so __io_complete_gt can safely
    // enqueue it. The GT is at [rbp-0x10] (saved before the switch).
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    EmitJmp("__sched_wloop_top");

    // === Park: no work available ===
    DefineLabel("__sched_wloop_park");

    // Publish idleFlag=1 with LOCK XCHG — serves as a full StoreLoad fence, so the
    // subsequent re-dequeue sees any GT an enqueuer published before it read idleFlag.
    // This closes the missed-wakeup window between a plain `idleFlag=1` store and
    // the prior NULL dequeue: an enqueuer that reads idleFlag before our XCHG
    // either sees 1 (and wakes us) or sees 0 and has its queue-publish visible to
    // our re-dequeue below.
    //
    // XCHG [R11+0x30], RCX  (XCHG with memory is implicitly LOCK'd on x86)
    EmitMovRegMem(X86Register.R11, -0x08, 8); // R11 = P*
    EmitMovRegImm(X86Register.Rcx, 1);         // RCX = 1 (new idleFlag)
    EmitBytes(0x49, 0x87, 0x4B, (byte)POffIdleFlag); // XCHG [R11+0x30], RCX

    // Re-dequeue: if work arrived during the race window, run it now.
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__sched_wloop_really_park");

    // Work found: clear idleFlag and run the GT.
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save GT before clobbering RAX
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, POffIdleFlag, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // reload GT for run_gt
    EmitJmp("__sched_wloop_run_gt");

    DefineLabel("__sched_wloop_really_park");

    if (Compiler.AsyncTrace) {
      // Trace: "worker_park #N [M=N]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_worker_park");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Check shutdown again before parking (avoid missed wakeup)
    EmitGlobalLoadReg(X86Register.Rax, "__sched_shutdown_flag");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__sched_wloop_exit");

    // WaitForSingleObject(P->wakeEvent, 100ms) — use timeout to avoid missed-wakeup hangs
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffWakeEvent);
    EmitMovRegImm(X86Register.Rdx, 100); // 100ms timeout
    EmitCallImport("kernel32.dll", "WaitForSingleObject");

    if (Compiler.AsyncTrace) {
      // Trace: "worker_wake #N [M=N]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_worker_wake");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Unmark idle: P->idleFlag = 0
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, POffIdleFlag, X86Register.Rcx);

    // Loop back
    EmitJmp("__sched_wloop_top");

    // === Exit: shutdown requested ===
    DefineLabel("__sched_wloop_exit");

    if (Compiler.AsyncTrace) {
      // Trace: "worker_exit #N [M=N]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_worker_exit");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // P*
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Atomically decrement active workers
    EmitGlobalLeaReg(X86Register.Rax, "__sched_active_workers");
    EmitBytes(0xF0, 0x48, 0xFF, 0x08); // LOCK DEC qword [RAX]

    // P->status = 0 (stopped)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, POffStatus, X86Register.Rcx);

    // Return 0 (CreateThread callback return value)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_cancel(gt_rcx): Request cancellation of a green thread.
  /// Sets GtOffCancelFlag=1. If an I/O handle is stored in GtOffIoHandle, calls CancelIoEx
  /// so any pending ReadFile/WriteFile completes immediately with ERROR_OPERATION_ABORTED.
  /// </summary>
  private void EmitGtCancel() {
    EmitRuntimeFunctionStart("__gt_cancel", 1, 0x20);

    if (Compiler.AsyncTrace) {
      // Trace: "cancel #N [M=W]\n"
      EmitMovMemReg(-0x08, X86Register.Rcx, 8); // save rcx (gt ptr)
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_cancel");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // gt ptr
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
      EmitMovRegMem(X86Register.Rcx, -0x08, 8); // restore rcx
    }

    // RCX = gt
    // gt->cancel_flag = 1
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffCancelFlag, X86Register.Rax);
    // Check if there is an in-flight I/O handle
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, GtOffIoHandle);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cancel_no_io");
    // CancelIoEx(hFile=rax, lpOverlapped=NULL) — cancels all pending I/O on handle
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitCallImportOnSystemStack("kernel32.dll", "CancelIoEx");
    DefineLabel("__gt_cancel_no_io");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_cleanup(): Called from _start after main returns.
  /// Step 1: Cancel all live green threads (sets cancel_flag, aborts in-flight I/O).
  /// Step 2: Set shutdown flag and wake all worker threads.
  /// Step 3: Drain — run queued threads on P[0] to completion; wait on I/O completions
  ///         if threads are still alive but the run queue is empty (I/O-blocked threads).
  /// Step 4: Wait for all worker threads to exit, then clean up.
  /// Requires __io_init to have been called before main (I/O subsystem must be alive).
  /// </summary>
  private void EmitGtCleanup() {
    // [rbp-0x08] = temp, [rbp-0x10] = temp2, [rbp-0x18] = loop_i, [rbp-0x20] = num_procs
    EmitRuntimeFunctionStart("__gt_cleanup", 0, 0x40);

    // --- Step 1: Cancel all live threads ---
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_all_head");
    DefineLabel("__gt_cleanup_cancel_loop");
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_cleanup_set_shutdown");
    EmitMovMemReg(-0x08, X86Register.Rcx, 8);
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rcx, GtOffAllNext);
    EmitCallRuntimeLabel("__gt_cancel");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, GtOffAllNext);
    EmitJmp("__gt_cleanup_cancel_loop");

    // --- Step 2: Set shutdown flag and wake all workers ---
    DefineLabel("__gt_cleanup_set_shutdown");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__sched_shutdown_flag");

    // Wake all workers P[1..num_procs-1] by setting their wake events
    EmitGlobalLoadReg(X86Register.Rax, "__sched_num_procs");
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // num_procs
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // i = 1

    DefineLabel("__gt_cleanup_wake_loop");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ae", "__gt_cleanup_drain");

    EmitGlobalLoadReg(X86Register.Rcx, "__sched_procs");
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitBytes(0x48, 0x8B, 0x0C, 0xD1); // MOV RCX, [RCX+RDX*8] = P[i]
    // SetEvent(P[i]->wakeEvent)
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, POffWakeEvent);
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_cleanup_wake_next"); // skip if wakeEvent is NULL (never started)
    EmitCallImport("kernel32.dll", "SetEvent");

    DefineLabel("__gt_cleanup_wake_next");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    EmitJmp("__gt_cleanup_wake_loop");

    // --- Step 3: Drain run queue on P[0] (main thread) ---
    DefineLabel("__gt_cleanup_drain");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_timer_check");

    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_check_live");

    // Run the thread: context switch from P[0]->mainThread to it
    EmitMovMemReg(-0x08, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitLeaMainThreadInline(X86Register.Rcx); // from = P[0]->mainThread
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Signal that the GT's context switch is complete so __io_complete_gt can safely enqueue it.
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);

    // If the GT completed, free its stack and return struct to free list.
    // This handles un-awaited promises (e.g. cancelled tasks nobody awaited).
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffStatus);
    EmitCmpRegImm(X86Register.Rcx, GtStatusCompleted);
    EmitJcc("nz", "__gt_cleanup_drain"); // not completed — just loop

    // Free stack via VirtualFree(stack_base, 0, MEM_RELEASE)
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffStackBase);
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_cleanup_drain_skip_stack");
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000); // MEM_RELEASE
    EmitCallImportOnSystemStack("kernel32.dll", "VirtualFree");
    DefineLabel("__gt_cleanup_drain_skip_stack");

    // Return GT struct to free list
    EmitGtReturnToFreeList("__gt_cleanup_drain");

    EmitJmp("__gt_cleanup_drain");

    // Run queue empty — check if any threads still alive (I/O-blocked)
    DefineLabel("__gt_cleanup_check_live");
    EmitGlobalLoadReg(X86Register.Rax, "__gt_live_count");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_wait_workers");

    // Threads still alive but nothing runnable — wait for I/O completion
    EmitGlobalLoadReg(X86Register.Rax, "__io_done_event");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_wait_workers"); // no I/O = bail
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF); // INFINITE
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    EmitJmp("__gt_cleanup_drain");

    // --- Step 4: Wait for all worker threads to exit ---
    DefineLabel("__gt_cleanup_wait_workers");
    // Loop over P[1..num_procs-1], WaitForSingleObject on each osThreadHandle
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // i = 1

    DefineLabel("__gt_cleanup_wait_wloop");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ae", "__gt_cleanup_cs_destroy");

    EmitGlobalLoadReg(X86Register.Rcx, "__sched_procs");
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitBytes(0x48, 0x8B, 0x0C, 0xD1); // MOV RCX, [RCX+RDX*8] = P[i]
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, POffOsThreadHandle);
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_cleanup_wait_wnext"); // skip if never started
    EmitMovRegImm(X86Register.Rdx, 5000); // 5 second timeout
    EmitCallImport("kernel32.dll", "WaitForSingleObject");

    DefineLabel("__gt_cleanup_wait_wnext");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    EmitJmp("__gt_cleanup_wait_wloop");

    // --- Step 5: Drain free lists on all P structs ---
    DefineLabel("__gt_cleanup_cs_destroy");
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // i = 0

    DefineLabel("__gt_cleanup_drain_p_loop");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ae", "__gt_cleanup_drain_done");

    // Load P[i]
    EmitGlobalLoadReg(X86Register.Rcx, "__sched_procs");
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitBytes(0x48, 0x8B, 0x0C, 0xD1); // MOV RCX, [RCX+RDX*8] = P[i]
    // Load P[i]->freeListHead
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, POffFreeListHead);

    DefineLabel("__gt_cleanup_drain_gt_loop");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_drain_p_next");
    // Save gt->next before freeing
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, GtOffNext);
    EmitMovMemReg(-0x08, X86Register.Rdx, 8); // save next
    // mm_raw_free(gt)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    // Advance to next
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitJmp("__gt_cleanup_drain_gt_loop");

    DefineLabel("__gt_cleanup_drain_p_next");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    EmitJmp("__gt_cleanup_drain_p_loop");

    DefineLabel("__gt_cleanup_drain_done");

    // --- Step 6: Destroy CRITICAL_SECTIONs ---
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_global_queue_cs");
    EmitCallImport("kernel32.dll", "DeleteCriticalSection");
    EmitGlobalLeaReg(X86Register.Rcx, "__sched_all_cs");
    EmitCallImport("kernel32.dll", "DeleteCriticalSection");

    if (Compiler.AsyncTrace) {
      EmitGlobalLeaReg(X86Register.Rcx, "__at_trace_cs");
      EmitCallImport("kernel32.dll", "DeleteCriticalSection");
    }

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_morestack(): Called from function prologues when stack space is insufficient.
  /// Grows the current green thread's stack by 2x.
  ///
  /// Entry: called via CALL from the stack check in the prologue, so
  ///   [rsp] = return address (back to the function prologue, after the jae)
  ///   The prologue hasn't executed push rbp yet.
  ///
  /// Strategy: allocate a new stack 2x the current size, copy the old stack contents
  /// to the top of the new stack, fix up the RBP chain, adjust RSP/RBP, free the old stack.
  /// Return to the caller's stack check which will now pass.
  ///
  /// Register plan:
  ///   We save callee-saved regs (rbx, r12-r15) and the return address on the stack.
  ///   After copying and relocating, we pop them back from the new stack locations.
  ///   The stack layout (growing downward from entry RSP):
  ///     [entry_rsp - 0]  = return address (from CALL)
  ///     [entry_rsp - 8]  = saved RBP  (pushed by us)
  ///     [entry_rsp - 16] = saved RBX
  ///     [entry_rsp - 24] = saved R12
  ///     [entry_rsp - 32] = saved R13
  ///     [entry_rsp - 40] = saved R14
  ///     [entry_rsp - 48] = saved R15
  ///   Then we create a frame for VirtualAlloc/memcpy calls below that.
  ///
  ///   After relocation, RSP is adjusted by the offset, so all these saved values
  ///   are at the correct positions on the new stack. We pop them in reverse order
  ///   and 'ret' pops the relocated return address.
  /// </summary>
  private void EmitGtMorestack() {
    DefineLabel("__gt_morestack");

    // Morestack is called from the function prologue BEFORE the function has saved
    // its arguments. We must preserve all argument registers (Maxon calling convention)
    // so the function can use them after we return.
    //
    // Key design: all register saves go on the SYSTEM stack, not the GT stack.
    // This means the GT stack guard only needs to cover the switch sequence itself
    // (~32 bytes), enabling much smaller initial GT stacks (2KB with 128-byte guard).
    //
    // Flow:
    //   1. Load P* via TLS, save GT entry RSP, switch RSP to system stack
    //   2. Push all 13 registers + GT entry RSP on system stack (14 pushes = 112 bytes)
    //   3. SUB RSP, 0x20 for shadow space (system stack frame)
    //   4. VirtualAlloc new stack (2x old size)
    //   5. Inline REP MOVSB to copy old stack to top of new stack
    //   6. Compute offset, adjust RBP and RBP chain on new stack
    //   7. Update GT struct fields and TIB
    //   8. VirtualFree old stack
    //   9. Compute adjusted GT RSP on new stack
    //  10. Pop all 13 registers from system stack (original values)
    //  11. Pop GT entry RSP into R10, apply offset → new GT RSP
    //  12. Switch RSP to new GT stack, RET (return address was copied to new stack)

    // --- Step 1: Load P* and switch to system stack ---
    // R11 = P* via TLS (only clobbers R11, preserves all other regs)
    EmitGlobalLoadReg(X86Register.R11, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R11, X86Register.R11, 0); // R11 = P*

    EmitMovRegReg(X86Register.R10, X86Register.Rsp);               // R10 = GT entry RSP
    EmitMovRegIndirectMem(X86Register.Rsp, X86Register.R11, POffSystemStackSP); // switch to system stack

    // --- Step 2: Save GT entry RSP and all registers on system stack ---
    // Push order: R10(gt_rsp), RBP, RBX, R12, R13, R14, R15, RCX, RDX, R8, R9, RSI, RDI, RAX
    // 14 pushes = 112 bytes. systemStackSP is 16-aligned → 112 mod 16 = 0 → RSP stays 16-aligned.
    EmitPushReg(X86Register.R10);
    EmitPushReg(X86Register.Rbp);
    EmitPushReg(X86Register.Rbx);
    EmitPushReg(X86Register.R12);
    EmitPushReg(X86Register.R13);
    EmitPushReg(X86Register.R14);
    EmitPushReg(X86Register.R15);
    EmitPushReg(X86Register.Rcx);
    EmitPushReg(X86Register.Rdx);
    EmitPushReg(X86Register.R8);
    EmitPushReg(X86Register.R9);
    EmitPushReg(X86Register.Rsi);
    EmitPushReg(X86Register.Rdi);
    EmitPushReg(X86Register.Rax);

    // --- Step 3: Allocate shadow space on system stack ---
    // RSP is 16-aligned after 14 pushes. SUB 0x20 (32) keeps it 16-aligned.
    // Windows x64 ABI: CALL pushes 8 → callee sees RSP mod 16 = 8. Correct.
    EmitSubRegImm(X86Register.Rsp, 0x20);

    // --- Load GT pointer and old stack info ---
    EmitLoadCurrentGtInline(X86Register.Rbx); // RBX = gt (callee-saved, preserved across calls)
    EmitMovRegIndirectMem(X86Register.R12, X86Register.Rbx, GtOffStackBase); // R12 = old_base
    EmitMovRegIndirectMem(X86Register.R13, X86Register.Rbx, GtOffStackSize); // R13 = old_size

    // R14 = new_size = old_size * 2
    EmitMovRegReg(X86Register.R14, X86Register.R13);
    EmitBytes(0x49, 0xD1, 0xE6); // SHL R14, 1

    // --- Step 4: VirtualAlloc(NULL, new_size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE) ---
    // We are already on the system stack, so call VirtualAlloc directly (not OnSystemStack).
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovRegReg(X86Register.Rdx, X86Register.R14);
    EmitMovRegImm(X86Register.R8, 0x3000);
    EmitMovRegImm(X86Register.R9, 0x04);
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    EmitMovRegReg(X86Register.R15, X86Register.Rax); // R15 = new_base

    // --- Step 5: Inline memcpy (old stack → top of new stack) ---
    // dst = new_base + new_size - old_size, src = old_base, count = old_size
    // REP MOVSB uses RDI=dst, RSI=src, RCX=count. CLD ensures forward direction.
    EmitByte(0xFC); // CLD (clear direction flag)
    EmitMovRegReg(X86Register.Rdi, X86Register.R15);  // RDI = new_base
    EmitAddRegReg(X86Register.Rdi, X86Register.R14);   // + new_size
    EmitSubRegReg(X86Register.Rdi, X86Register.R13);   // - old_size = dst
    EmitMovRegReg(X86Register.Rsi, X86Register.R12);   // RSI = old_base = src
    EmitMovRegReg(X86Register.Rcx, X86Register.R13);   // RCX = old_size = count
    EmitBytes(0xF3, 0xA4); // REP MOVSB

    // --- Step 6: Compute offset and adjust RBP chain on new stack ---
    // offset = (new_base + new_size - old_size) - old_base
    //        = (new_base + new_size) - (old_base + old_size)
    EmitMovRegReg(X86Register.Rax, X86Register.R15);  // new_base
    EmitAddRegReg(X86Register.Rax, X86Register.R14);   // + new_size
    EmitSubRegReg(X86Register.Rax, X86Register.R13);   // - old_size
    EmitSubRegReg(X86Register.Rax, X86Register.R12);   // - old_base = offset
    // RAX = offset (preserved in RAX through the walk since walk only uses RCX/R8/R9/R10/R11)

    // Adjust RBP if it's within the old stack range [old_base, old_base + old_size)
    EmitMovRegReg(X86Register.Rcx, X86Register.R12);
    EmitAddRegReg(X86Register.Rcx, X86Register.R13); // RCX = old_top
    EmitCmpRegReg(X86Register.Rbp, X86Register.R12);
    EmitJcc("b", "__gt_morestack_skip_rbp");
    EmitCmpRegReg(X86Register.Rbp, X86Register.Rcx);
    EmitJcc("ae", "__gt_morestack_skip_rbp");
    EmitAddRegReg(X86Register.Rbp, X86Register.Rax);
    DefineLabel("__gt_morestack_skip_rbp");

    // Write adjusted RBP back to its saved slot on the system stack so POP restores it.
    // After SUB 0x20 shadow space, the 14 pushes are at [RSP+0x20..RSP+0x8F]:
    //   [RSP+0x20]=RAX, [RSP+0x28]=RDI, [RSP+0x30]=RSI, [RSP+0x38]=R9,
    //   [RSP+0x40]=R8,  [RSP+0x48]=RDX, [RSP+0x50]=RCX, [RSP+0x58]=R15,
    //   [RSP+0x60]=R14, [RSP+0x68]=R13, [RSP+0x70]=R12, [RSP+0x78]=RBX,
    //   [RSP+0x80]=RBP, [RSP+0x88]=R10 (GT entry RSP)
    EmitMovIndirectMemReg(X86Register.Rsp, 0x80, X86Register.Rbp);

    // Walk the RBP chain on the new stack and adjust saved RBP values
    // that still point into the old stack range.
    // RCX = walker (starts at adjusted RBP)
    // R8 = new_low, R9 = new_high (bounds of copied data in new stack)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rbp);
    EmitMovRegReg(X86Register.R8, X86Register.R15);
    EmitAddRegReg(X86Register.R8, X86Register.R14);
    EmitSubRegReg(X86Register.R8, X86Register.R13);
    EmitMovRegReg(X86Register.R9, X86Register.R15);
    EmitAddRegReg(X86Register.R9, X86Register.R14);

    DefineLabel("__gt_morestack_walk");
    // Exit if walker == 0
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_morestack_walk_done");
    // Exit if walker outside new stack range
    EmitCmpRegReg(X86Register.Rcx, X86Register.R8);
    EmitJcc("b", "__gt_morestack_walk_done");
    EmitCmpRegReg(X86Register.Rcx, X86Register.R9);
    EmitJcc("ae", "__gt_morestack_walk_done");

    // R11 = saved_rbp = [walker + 0]
    EmitMovRegIndirectMem(X86Register.R11, X86Register.Rcx, 0);
    // If saved_rbp is within old stack range [old_base, old_top), adjust it
    EmitCmpRegReg(X86Register.R11, X86Register.R12);
    EmitJcc("b", "__gt_morestack_walk_next");
    EmitMovRegReg(X86Register.R10, X86Register.R12);
    EmitAddRegReg(X86Register.R10, X86Register.R13); // R10 = old_top
    EmitCmpRegReg(X86Register.R11, X86Register.R10);
    EmitJcc("ae", "__gt_morestack_walk_next");
    // Adjust: saved_rbp += offset, store back
    EmitAddRegReg(X86Register.R11, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, 0, X86Register.R11);

    DefineLabel("__gt_morestack_walk_next");
    // walker = [walker] (the saved_rbp, possibly adjusted)
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, 0);
    EmitJmp("__gt_morestack_walk");

    DefineLabel("__gt_morestack_walk_done");

    // --- Step 7: Update GreenThread struct and TIB ---
    EmitMovIndirectMemReg(X86Register.Rbx, GtOffStackBase, X86Register.R15); // stack_base = new_base
    EmitMovIndirectMemReg(X86Register.Rbx, GtOffStackSize, X86Register.R14); // stack_size = new_size
    EmitMovRegReg(X86Register.Rcx, X86Register.R15);
    EmitAddRegImm(X86Register.Rcx, GtStackGuardMargin);
    EmitMovIndirectMemReg(X86Register.Rbx, GtOffStackGuard, X86Register.Rcx); // stackguard = new_base + margin

    // Update TIB stack bounds and green thread TIB fields for the new stack.
    // tib_stack_base = new_base + new_size, tib_stack_limit = new_base
    EmitMovRegReg(X86Register.Rcx, X86Register.R15);
    EmitAddRegReg(X86Register.Rcx, X86Register.R14); // RCX = new_base + new_size
    EmitMovIndirectMemReg(X86Register.Rbx, GtOffTibStackBase, X86Register.Rcx);
    // MOV gs:[0x08], RCX
    EmitBytes(0x65, 0x48, 0x89, 0x0C, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rbx, GtOffTibStackLimit, X86Register.R15);
    // MOV gs:[0x10], R15
    EmitBytes(0x65, 0x4C, 0x89, 0x3C, 0x25, 0x10, 0x00, 0x00, 0x00);

    // --- Step 8: VirtualFree(old_base, 0, MEM_RELEASE) ---
    // Save offset (RAX) in RBP temporarily — RBP was already adjusted in step 6,
    // and we'll restore it from the system stack at the end.
    EmitMovRegReg(X86Register.Rbp, X86Register.Rax); // RBP = offset (callee-saved, survives call)
    EmitMovRegReg(X86Register.Rcx, X86Register.R12);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000);
    EmitCallImport("kernel32.dll", "VirtualFree");
    EmitMovRegReg(X86Register.Rax, X86Register.Rbp); // RAX = offset (restored)

    // --- Steps 9-12: Restore registers from system stack, switch to new GT stack ---
    // Tear down shadow space
    EmitAddRegImm(X86Register.Rsp, 0x20);

    // At this point: RAX = offset, RSP points to saved registers on system stack.
    // System stack layout (from RSP upward):
    //   [RSP+0x00] = saved RAX
    //   [RSP+0x08] = saved RDI
    //   [RSP+0x10] = saved RSI
    //   [RSP+0x18] = saved R9
    //   [RSP+0x20] = saved R8
    //   [RSP+0x28] = saved RDX
    //   [RSP+0x30] = saved RCX
    //   [RSP+0x38] = saved R15
    //   [RSP+0x40] = saved R14
    //   [RSP+0x48] = saved R13
    //   [RSP+0x50] = saved R12
    //   [RSP+0x58] = saved RBX
    //   [RSP+0x60] = saved RBP
    //   [RSP+0x68] = saved R10 (GT entry RSP)
    //
    // Compute new GT RSP: R10 = GT_entry_RSP + offset
    EmitMovRegIndirectMem(X86Register.R10, X86Register.Rsp, 0x68); // R10 = GT entry RSP
    EmitAddRegReg(X86Register.R10, X86Register.Rax); // R10 = adjusted GT RSP on new stack

    // Pop all 13 registers from system stack (restore original values)
    EmitPopReg(X86Register.Rax);
    EmitPopReg(X86Register.Rdi);
    EmitPopReg(X86Register.Rsi);
    EmitPopReg(X86Register.R9);
    EmitPopReg(X86Register.R8);
    EmitPopReg(X86Register.Rdx);
    EmitPopReg(X86Register.Rcx);
    EmitPopReg(X86Register.R15);
    EmitPopReg(X86Register.R14);
    EmitPopReg(X86Register.R13);
    EmitPopReg(X86Register.R12);
    EmitPopReg(X86Register.Rbx);
    EmitPopReg(X86Register.Rbp);

    // RSP now points at saved R10 (GT entry RSP) on system stack. Skip it.
    EmitAddRegImm(X86Register.Rsp, 8);

    // RSP is back to systemStackSP (all pushes consumed). No need to restore P->systemStackSP
    // since the value at P->systemStackSP is the top of the system stack and we just popped
    // everything back to that position.

    // Switch RSP to the adjusted position on the new GT stack.
    // R10 = GT_entry_RSP + offset = position of return address on new stack.
    EmitMovRegReg(X86Register.Rsp, X86Register.R10);

    // RET: pops the return address from the new GT stack (copied from old) and jumps there.
    // This returns to the prologue check, which now passes because the stack is larger.
    EmitByte(0xC3); // ret
  }

  // __gt_timer_check and __gt_timer_add are now emitted by RuntimeEmitter.Scheduler.cs

  // ===========================================================================================
  // I/O Runtime — IOCP-based async I/O + sync worker thread for non-overlappable operations
  // ===========================================================================================
  //
  // Architecture:
  //   - ReadFile/WriteFile use FILE_FLAG_OVERLAPPED + IOCP (true async kernel I/O).
  //     One completion thread (IOCP drain) calls GetQueuedCompletionStatus in a loop.
  //   - Non-overlappable ops (FindFirstFile, GetFileAttributesA, etc.) go to a single
  //     sync worker thread via a CRITICAL_SECTION + event request queue.
  //   - Both the IOCP thread and sync worker post completions to a shared done queue.
  //     __io_check_completions drains it and re-enqueues waiting green threads.
  //
  // AsyncOpContext layout (56 bytes, OVERLAPPED at offset 0):
  //   0x00  OVERLAPPED (32 bytes): Internal(8), InternalHigh(8), Offset/OffsetHigh(8), hEvent(8)
  //   0x20  waiter_gt  i64
  //   0x28  result_len i64
  //   0x30  error_code i64
  //
  // SyncRequest layout (40 bytes):
  //   0x00  op_code   i64   (see SyncOpXxx constants)
  //   0x08  arg0      i64
  //   0x10  arg1      i64
  //   0x18  waiter_gt i64
  //   0x20  next      i64
  //
  // IoCompletion layout (40 bytes):
  //   0x00  waiter_gt  i64
  //   0x08  result_val i64
  //   0x10  result_len i64
  //   0x18  error_code i64
  //   0x20  next       i64

  private const int AsyncCtxSize = 0x38; // 56 bytes (OVERLAPPED=32 + 3 fields)
  private const int AsyncCtxOffWaiter = 0x20;
  private const int AsyncCtxOffCustomResult = 0x28; // custom result value (e.g., socket handle for ConnectEx)

  private const int SyncReqSize = 0x28; // 40 bytes
  private const int SyncReqOffOp = 0x00;
  private const int SyncReqOffArg0 = 0x08;
  private const int SyncReqOffArg1 = 0x10;
  private const int SyncReqOffWaiter = 0x18;
  private const int SyncReqOffNext = 0x20;

  private const int IoCompOffWaiter = 0x00;
  private const int IoCompOffResult = 0x08;
  private const int IoCompOffLen = 0x10;
  private const int IoCompOffError = 0x18;
  private const int IoCompOffNext = 0x20;

  // Sync op codes (must match SyncOpXxx used in stubs)
  private const long SyncOpFileExists = 0;
  private const long SyncOpFileDelete = 1;
  private const long SyncOpFindFirst = 2;
  private const long SyncOpFindNext = 3;
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
  private const long SyncOpDnsResolve = 14;
  private const long SyncOpFileOpenWriteExec = 15;
  // arg0 = process HANDLE, arg1 = timeout_ms (0 = wait forever).
  // Returns 0=completed, 1=timeout, -1=error. Reserved for routing the
  // subprocess wait through the IOCP scheduler so `async Subprocess.run(...)`
  // yields the green thread instead of blocking the OS thread on
  // WaitForSingleObject. Currently `maxon_subprocess_wait_internal` calls
  // WaitForSingleObject directly on the system stack.
  private const long SyncOpProcessWait = 16;
  private const long SyncOpShutdown = 0xFF;

  private void EmitIoRuntime() {
    // I/O globals
    DefineGlobal("__io_iocp", 8, 0); // HANDLE: I/O completion port
    DefineGlobal("__io_completion_thread", 8, 0); // HANDLE: IOCP drain thread
    DefineGlobal("__io_sync_worker", 8, 0); // HANDLE: sync-op worker thread
    DefineGlobal("__io_sync_cs", 40, 0); // CRITICAL_SECTION (40 bytes on x64)
    DefineGlobal("__io_sync_req_event", 8, 0); // manual-reset event: sync work available
    DefineGlobal("__io_sync_req_head", 8, 0); // SyncRequest* head
    DefineGlobal("__io_sync_req_tail", 8, 0); // SyncRequest* tail
    DefineGlobal("__io_done_cs", 40, 0); // CRITICAL_SECTION for done queue
    DefineGlobal("__io_done_event", 8, 0); // auto-reset event: completion available
    DefineGlobal("__io_done_head", 8, 0); // IoCompletion* head
    DefineGlobal("__io_done_tail", 8, 0); // IoCompletion* tail
    DefineGlobal("__io_connectex_ptr", 8, 0); // ConnectEx function pointer obtained via WSAIoctl

    EmitIoInit();
    EmitIoShutdown();
    EmitIoCompletionLoop();
    EmitIoSyncWorkerLoop();
    EmitIoCheckCompletions();
    EmitIoSubmitSync();
    EmitIoSubmitHelpers();
  }

  /// <summary>
  /// __io_init(): Initialize IOCP, sync worker, and completion threads.
  /// Called from _start after __gt_init.
  /// </summary>
  private void EmitIoInit() {
    EmitRuntimeFunctionStart("__io_init", 0, 0x240);
    // Extended frame to accommodate WSAStartup's WSADATA (408 bytes) and ConnectEx
    // pointer acquisition via WSAIoctl.
    //
    // Stack layout:
    //   [rbp-0x08]  temp socket handle
    //   [rbp-0x10]  connectex_ptr output (8 bytes)
    //   [rbp-0x18]  bytesReturned for WSAIoctl (8 bytes)
    //   [rbp-0x28]  GUID bytes (16 bytes, rbp-0x28 to rbp-0x19)
    //   [rbp-0x30]  handle array[0] for WaitForMultipleObjects
    //   [rbp-0x38]  handle array[1]
    //   [rbp-0x230] WSADATA for WSAStartup (408 bytes)

    // CreateIoCompletionPort(INVALID_HANDLE_VALUE=-1, NULL, 0, 1) → __io_iocp
    EmitMovRegImm(X86Register.Rcx, unchecked((long)0xFFFFFFFFFFFFFFFF)); // INVALID_HANDLE_VALUE
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // ExistingCompletionPort = NULL
    EmitXorRegReg(X86Register.R8, X86Register.R8);  // CompletionKey = 0
    EmitMovRegImm(X86Register.R9, 1);               // NumberOfConcurrentThreads = 1
    EmitCallImport("kernel32.dll", "CreateIoCompletionPort");
    EmitGlobalStoreReg(X86Register.Rax, "__io_iocp");

    // --- Obtain ConnectEx function pointer via WSAIoctl ---
    // WSAStartup is required before any Winsock call
    EmitWsaEnsureInit(-0x230, "__io_init_wsa_ok");

    // Create a temporary socket for WSAIoctl
    // WSASocketW(AF_INET=2, SOCK_STREAM=1, IPPROTO_TCP=6, NULL, 0, WSA_FLAG_OVERLAPPED=1)
    EmitMovRegImm(X86Register.Rcx, 2);   // AF_INET
    EmitMovRegImm(X86Register.Rdx, 1);   // SOCK_STREAM
    EmitMovRegImm(X86Register.R8, 6);    // IPPROTO_TCP
    EmitXorRegReg(X86Register.R9, X86Register.R9); // lpProtocolInfo = NULL
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x20, X86Register.Rax); // g = 0
    EmitMovRegImm(X86Register.Rax, 1);       // WSA_FLAG_OVERLAPPED
    EmitMovMemRspReg(0x28, X86Register.Rax);
    EmitCallImport("ws2_32.dll", "WSASocketW");
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save temp socket

    // Store WSAID_CONNECTEX GUID at [rbp-0x28] (16 bytes)
    // Bytes: B9 07 A2 25 F3 DD 60 46 8E E9 76 E5 8C 74 06 3E
    // As two little-endian qwords:
    //   [rbp-0x28] = 0x4660DDF325A207B9 (first 8 bytes)
    //   [rbp-0x20] = 0x3E06748CE576E98E (second 8 bytes)
    EmitMovRegImm(X86Register.Rax, unchecked((long)0x4660DDF325A207B9));
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rax, unchecked((long)0x3E06748CE576E98E));
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // Zero connectex_ptr and bytesReturned
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // connectex_ptr = 0
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // bytesReturned = 0

    // WSAIoctl(tempSocket, SIO_GET_EXTENSION_FUNCTION_POINTER, &guid, 16, &connectex_ptr, 8, &bytesReturned, NULL, NULL)
    // 9 parameters: RCX, RDX, R8, R9, [RSP+0x20..0x40]
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);           // socket
    EmitMovRegImm(X86Register.Rdx, unchecked((long)0xC8000006)); // SIO_GET_EXTENSION_FUNCTION_POINTER
    EmitLeaRegMem(X86Register.R8, -0x28);                // &guid
    EmitMovRegImm(X86Register.R9, 16);                   // cbInBuffer = 16
    EmitLeaRegMem(X86Register.Rax, -0x10);
    EmitMovMemRspReg(0x20, X86Register.Rax);             // lpvOutBuffer = &connectex_ptr
    EmitMovRegImm(X86Register.Rax, 8);
    EmitMovMemRspReg(0x28, X86Register.Rax);             // cbOutBuffer = 8
    EmitLeaRegMem(X86Register.Rax, -0x18);
    EmitMovMemRspReg(0x30, X86Register.Rax);             // lpcbBytesReturned = &bytesReturned
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x38, X86Register.Rax);             // lpOverlapped = NULL
    EmitMovMemRspReg(0x40, X86Register.Rax);             // lpCompletionRoutine = NULL
    EmitCallImport("ws2_32.dll", "WSAIoctl");

    // Store ConnectEx pointer globally
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitGlobalStoreReg(X86Register.Rax, "__io_connectex_ptr");

    // Close temp socket
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallImport("ws2_32.dll", "closesocket");

    // InitializeCriticalSection(&__io_sync_cs)
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImport("kernel32.dll", "InitializeCriticalSection");

    // InitializeCriticalSection(&__io_done_cs)
    EmitGlobalLeaReg(X86Register.Rcx, "__io_done_cs");
    EmitCallImport("kernel32.dll", "InitializeCriticalSection");

    // CreateEventA(NULL, TRUE=manual-reset, FALSE=not-signaled, NULL) → __io_sync_req_event
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovRegImm(X86Register.Rdx, 1); // bManualReset = TRUE
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreateEventA");
    EmitGlobalStoreReg(X86Register.Rax, "__io_sync_req_event");

    // CreateEventA(NULL, FALSE=auto-reset, FALSE=not-signaled, NULL) → __io_done_event
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // bManualReset = FALSE
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreateEventA");
    EmitGlobalStoreReg(X86Register.Rax, "__io_done_event");

    // CreateThread(NULL, 0, &__io_completion_loop, NULL, 0, NULL) → __io_completion_thread
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);  // lpThreadAttributes = NULL
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);  // dwStackSize = 0
    // Load address of __io_completion_loop into R8
    EmitByte(0x4C); EmitByte(0x8D); EmitByte(0x05); // LEA R8, [rip+disp32]
    _jumpFixups.Add((_code.Count, "__io_completion_loop"));
    EmitDword(0);
    EmitXorRegReg(X86Register.R9, X86Register.R9);  // lpParameter = NULL
    // dwCreationFlags = 0 (on stack slot 5) and lpThreadId = NULL (stack slot 6)
    // R9 is already 0 — use it to zero the stack slots
    EmitMovMemRspReg(0x20, X86Register.R9); // [rsp+0x20] = 0 (dwCreationFlags)
    EmitMovMemRspReg(0x28, X86Register.R9); // [rsp+0x28] = 0 (lpThreadId)
    EmitCallImport("kernel32.dll", "CreateThread");
    EmitGlobalStoreReg(X86Register.Rax, "__io_completion_thread");

    // CreateThread(NULL, 0, &__io_sync_worker_loop, NULL, 0, NULL) → __io_sync_worker
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitByte(0x4C); EmitByte(0x8D); EmitByte(0x05); // LEA R8, [rip+disp32]
    _jumpFixups.Add((_code.Count, "__io_sync_worker_loop"));
    EmitDword(0);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitMovMemRspReg(0x20, X86Register.R9);
    EmitMovMemRspReg(0x28, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreateThread");
    EmitGlobalStoreReg(X86Register.Rax, "__io_sync_worker");

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_shutdown(): Signal I/O threads to exit and wait for them to terminate.
  /// Called from _start after __gt_cleanup.
  /// </summary>
  private void EmitIoShutdown() {
    EmitRuntimeFunctionStart("__io_shutdown", 0, 0x40);

    // PostQueuedCompletionStatus(iocp, 0, 0, NULL) — sentinel to wake completion thread
    EmitGlobalLoadReg(X86Register.Rcx, "__io_iocp");
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // dwNumberOfBytesTransferred = 0
    EmitXorRegReg(X86Register.R8, X86Register.R8);  // dwCompletionKey = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);  // lpOverlapped = NULL
    EmitCallImport("kernel32.dll", "PostQueuedCompletionStatus");

    // Send shutdown SyncRequest to sync worker: op=0xFF, then SetEvent
    EmitMovRegImm(X86Register.Rcx, SyncReqSize);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace); // HEAP_ZERO_MEMORY — already zeroed
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save req
    // req->op = SyncOpShutdown
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegImm(X86Register.Rcx, SyncOpShutdown);
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffOp, X86Register.Rcx);
    // Enqueue to sync request queue
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // req
    EmitCallRuntimeLabel("__io_enqueue_sync_req");
    // SetEvent(__io_sync_req_event)
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitCallImport("kernel32.dll", "SetEvent");

    // WaitForMultipleObjects(2, handles_array, TRUE, INFINITE)
    // Build 2-element handle array on stack: handles[0] at [rbp-0x18], handles[1] at [rbp-0x10]
    // (contiguous upward: lpHandles[0]=handles[0], lpHandles[1]=handles[0]+8=handles[1])
    EmitGlobalLoadReg(X86Register.Rax, "__io_completion_thread");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // handles[0] = completion_thread
    EmitGlobalLoadReg(X86Register.Rax, "__io_sync_worker");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // handles[1] = sync_worker
    EmitMovRegImm(X86Register.Rcx, 2);           // nCount
    EmitLeaRegMem(X86Register.Rdx, -0x18);       // lpHandles = &handles[0]
    EmitMovRegImm(X86Register.R8, 1);            // bWaitAll = TRUE
    EmitMovRegImm(X86Register.R9, 0xFFFFFFFF);   // dwMilliseconds = INFINITE
    EmitCallImport("kernel32.dll", "WaitForMultipleObjects");

    // CloseHandle for both threads and IOCP
    EmitGlobalLoadReg(X86Register.Rcx, "__io_completion_thread");
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_worker");
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_iocp");
    EmitCallImport("kernel32.dll", "CloseHandle");

    // CloseHandle for events
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitCallImport("kernel32.dll", "CloseHandle");

    // DeleteCriticalSection for both CS
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImport("kernel32.dll", "DeleteCriticalSection");
    EmitGlobalLeaReg(X86Register.Rcx, "__io_done_cs");
    EmitCallImport("kernel32.dll", "DeleteCriticalSection");

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_completion_loop(): IOCP drain thread entry point.
  /// Calls GetQueuedCompletionStatus in a loop; re-enqueues waiting green threads.
  /// Exits on sentinel (key=0, overlapped=NULL).
  /// Note: This is a raw OS thread entry point — no Maxon calling convention.
  /// Uses Windows x64 ABI: RCX=lpParameter (ignored). Returns DWORD in EAX.
  /// </summary>
  private void EmitIoCompletionLoop() {
    DefineLabel("__io_completion_loop");
    // Standard Windows x64 ABI prologue
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x60); // local space (16-byte aligned: 8+0x60=0x68, mod16=8 → already aligned with push)

    // Locals:
    //   [rbp-0x08] = bytes_transferred (DWORD-sized, read from OVERLAPPED)
    //   [rbp-0x10] = completion_key (ULONG_PTR, unused but passed by ref)
    //   [rbp-0x18] = overlapped ptr (LPOVERLAPPED → AsyncOpContext*)

    DefineLabel("__io_comp_loop_top");
    // Zero bytes_transferred slot — GQCS writes a DWORD (32-bit), upper 32 bits must be clean
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x08, X86Register.Rax, 8);
    // GetQueuedCompletionStatus(iocp, &bytes, &key, &overlapped, INFINITE)
    EmitGlobalLoadReg(X86Register.Rcx, "__io_iocp");
    EmitLeaRegMem(X86Register.Rdx, -0x08);  // lpNumberOfBytesTransferred
    EmitLeaRegMem(X86Register.R8, -0x10);   // lpCompletionKey
    EmitLeaRegMem(X86Register.R9, -0x18);   // lpOverlapped
    // dwMilliseconds = INFINITE on stack [rsp+0x20] — use Rax as scratch
    EmitMovRegImm(X86Register.Rax, 0xFFFFFFFF);
    EmitMovMemRspReg(0x20, X86Register.Rax);
    EmitCallImport("kernel32.dll", "GetQueuedCompletionStatus");

    // Check sentinel: if overlapped == NULL → shutdown signal, exit
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = overlapped
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_comp_loop_exit");

    // ctx = (AsyncOpContext*)overlapped — direct GT completion (no intermediate queue)
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save ctx

    // gt = ctx->waiter_gt
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, AsyncCtxOffWaiter);
    EmitMovMemReg(-0x28, X86Register.Rdx, 8); // save gt

    // Check if ctx has a custom result (used by ConnectEx to return socket handle
    // instead of bytes_transferred). If ctx->custom_result != 0, use it.
    EmitMovRegMem(X86Register.Rax, -0x20, 8); // reload ctx
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, AsyncCtxOffCustomResult);
    EmitBytes(0x48, 0x85, 0xD2); // TEST RDX, RDX
    EmitJcc("z", "__io_comp_use_bytes");
    // Custom result is non-zero — overwrite bytes_transferred with it
    EmitMovMemReg(-0x08, X86Register.Rdx, 8);
    DefineLabel("__io_comp_use_bytes");

    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // __io_complete_gt(gt, result, result, 0)
    // result is bytes_transferred (or custom_result if it was set)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // gt
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // result
    EmitMovRegMem(X86Register.R8, -0x08, 8);  // len = result
    EmitXorRegReg(X86Register.R9, X86Register.R9); // error = 0
    EmitCallRuntimeLabel("__io_complete_gt");

    EmitJmp("__io_comp_loop_top");

    DefineLabel("__io_comp_loop_exit");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax); // return 0
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
  }

  /// <summary>
  /// __io_sync_worker_loop(): Sync worker thread entry point.
  /// Processes non-overlappable I/O ops (FindFirstFile, GetFileAttributes, etc.) serially.
  /// Posts IoCompletion results to shared done queue.
  /// </summary>
  /// <summary>
  /// Inside the sync worker on a failure branch, capture the Win32 GetLastError
  /// value into the worker's error-code slot (-0x248) so __io_sync_op_done passes
  /// it through __io_complete_gt -> gt->io_error_code. RAX is preserved (saved
  /// to -0x240 around the GetLastError call) so callers may continue using their
  /// failing return value to determine the sentinel they emit.
  /// </summary>
  private void EmitSyncWorkerCaptureLastError() {
    EmitMovMemReg(-0x240, X86Register.Rax, 8);             // save failing RAX
    EmitCallImport("kernel32.dll", "GetLastError");
    EmitMovMemReg(-0x248, X86Register.Rax, 8);             // captured Win32 error
    EmitMovRegMem(X86Register.Rax, -0x240, 8);             // restore failing RAX
  }

  private void EmitIoSyncWorkerLoop() {
    DefineLabel("__io_sync_worker_loop");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x270);
    // Locals: [rbp-0x08]=req ptr, [rbp-0x10]=result, [rbp-0x18]=comp ptr
    // Extended frame for net_connect: WSADATA(408b), hints(48b), sockaddr_in(16b)
    // Phase B errno capture (Step 1a):
    //   [rbp-0x240] = saved RAX during GetLastError calls (failure branches)
    //   [rbp-0x248] = captured Win32 error code, passed as the 4th arg to
    //                 __io_complete_gt so the lowering can map ENOENT/EACCES
    //                 to notFound/accessDenied via __io_get_last_error.

    DefineLabel("__io_sync_worker_top");
    // WaitForSingleObject(__io_sync_req_event, INFINITE)
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);
    EmitCallImport("kernel32.dll", "WaitForSingleObject");

    // Dequeue one request (lock, dequeue, unlock/ResetEvent if empty)
    EmitCallRuntimeLabel("__io_dequeue_sync_req");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_sync_worker_top"); // spurious wakeup
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // req

    // Zero the error-code slot up front. Success handlers leave it at 0; failure
    // handlers overwrite it with GetLastError before jumping to __io_sync_op_done.
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovMemReg(-0x248, X86Register.Rcx, 8);

    // Dispatch on op code
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffOp);
    EmitCmpRegImm(X86Register.Rcx, SyncOpShutdown);
    EmitJcc("e", "__io_sync_worker_shutdown");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFileExists);
    EmitJcc("e", "__io_sync_op_file_exists");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFileDelete);
    EmitJcc("e", "__io_sync_op_file_delete");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFindFirst);
    EmitJcc("e", "__io_sync_op_find_first");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFindNext);
    EmitJcc("e", "__io_sync_op_find_next");
    EmitCmpRegImm(X86Register.Rcx, SyncOpDirExists);
    EmitJcc("e", "__io_sync_op_dir_exists");
    EmitCmpRegImm(X86Register.Rcx, SyncOpDirCreate);
    EmitJcc("e", "__io_sync_op_dir_create");
    EmitCmpRegImm(X86Register.Rcx, SyncOpGetCwd);
    EmitJcc("e", "__io_sync_op_get_cwd");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFileOpenRead);
    EmitJcc("e", "__io_sync_op_file_open_read");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFileOpenWrite);
    EmitJcc("e", "__io_sync_op_file_open_write");
    EmitCmpRegImm(X86Register.Rcx, SyncOpCloseHandle);
    EmitJcc("e", "__io_sync_op_close_handle");
    EmitCmpRegImm(X86Register.Rcx, SyncOpNetConnect);
    EmitJcc("e", "__io_sync_op_net_connect");
    EmitCmpRegImm(X86Register.Rcx, SyncOpNetSend);
    EmitJcc("e", "__io_sync_op_net_send");
    EmitCmpRegImm(X86Register.Rcx, SyncOpNetRecv);
    EmitJcc("e", "__io_sync_op_net_recv");
    EmitCmpRegImm(X86Register.Rcx, SyncOpNetClose);
    EmitJcc("e", "__io_sync_op_net_close");
    EmitCmpRegImm(X86Register.Rcx, SyncOpDnsResolve);
    EmitJcc("e", "__io_sync_op_dns_resolve");
    EmitCmpRegImm(X86Register.Rcx, SyncOpFileOpenWriteExec);
    EmitJcc("e", "__io_sync_op_file_open_write_exec");
    EmitCmpRegImm(X86Register.Rcx, SyncOpProcessWait);
    EmitJcc("e", "__io_sync_op_process_wait");
    // Unknown op — abort
    EmitCallRuntimeLabel("__gt_panic_io");

    // --- fileExists: GetFileAttributesA(arg0) != INVALID (and not directory) ---
    DefineLabel("__io_sync_op_file_exists");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0);
    EmitCallImport("kernel32.dll", "GetFileAttributesA");
    // INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF
    EmitMovRegImm(X86Register.Rcx, unchecked((long)0xFFFFFFFF));
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "__io_sync_file_exists_no");
    // Check not a directory (FILE_ATTRIBUTE_DIRECTORY = 0x10)
    EmitBytes(0xA9, 0x10, 0x00, 0x00, 0x00); // TEST EAX, 0x10
    EmitJcc("nz", "__io_sync_file_exists_no");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_file_exists_no");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("__io_sync_op_done");

    // --- fileDelete: DeleteFileA(arg0) ---
    DefineLabel("__io_sync_op_file_delete");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0);
    EmitCallImport("kernel32.dll", "DeleteFileA");
    // result in RAX (0=failure, nonzero=success). On failure, capture
    // GetLastError into the worker's error-code slot for the lowering's
    // notFound/accessDenied dispatch.
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "__io_sync_op_done");
    EmitSyncWorkerCaptureLastError();
    EmitJmp("__io_sync_op_done");

    // --- findFirst: FindFirstFileA(arg0=pattern, arg1=WIN32_FIND_DATA*) → HANDLE ---
    DefineLabel("__io_sync_op_find_first");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // pattern
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, SyncReqOffArg1); // WIN32_FIND_DATA*
    EmitCallImport("kernel32.dll", "FindFirstFileA");
    // INVALID_HANDLE_VALUE on failure → capture GetLastError.
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ne", "__io_sync_op_done");
    EmitSyncWorkerCaptureLastError();
    EmitJmp("__io_sync_op_done");

    // --- findNext: FindNextFileA(arg0=HANDLE, arg1=WIN32_FIND_DATA*) → BOOL, or -1 on real error ---
    // Returns: non-zero=found, 0=no more files (ERROR_NO_MORE_FILES), -1=OS error.
    DefineLabel("__io_sync_op_find_next");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // HANDLE
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, SyncReqOffArg1); // WIN32_FIND_DATA*
    EmitCallImport("kernel32.dll", "FindNextFileA");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "__io_sync_op_done"); // success: return the non-zero BOOL
    // FindNextFileA returned FALSE. Check if that means "no more files" or a real error.
    EmitCallImport("kernel32.dll", "GetLastError");
    // ERROR_NO_MORE_FILES = 18 (0x12) — clean end-of-iteration; do NOT record
    // an error code (this is not a user-visible failure).
    EmitCmpRegImm(X86Register.Rax, 18);
    EmitJcc("e", "__io_sync_op_find_next_eof");
    // Real error: RAX still holds GetLastError's result. Save it before
    // overwriting RAX with the -1 sentinel for the lowering.
    EmitMovMemReg(-0x248, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_op_find_next_eof");
    // Normal end-of-iteration: return 0. Must jump out — without the jump
    // execution falls through into the dirExists handler with the SyncReqOffArg0
    // value (a search HANDLE) being reinterpreted as a path cstring, which can
    // crash the sync worker thread with an access violation.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("__io_sync_op_done");

    // --- dirExists: GetFileAttributesA(arg0) & FILE_ATTRIBUTE_DIRECTORY ---
    DefineLabel("__io_sync_op_dir_exists");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0);
    EmitCallImport("kernel32.dll", "GetFileAttributesA");
    EmitMovRegImm(X86Register.Rcx, unchecked((long)0xFFFFFFFF));
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "__io_sync_dir_exists_no");
    // Check directory bit
    EmitBytes(0xA9, 0x10, 0x00, 0x00, 0x00); // TEST EAX, FILE_ATTRIBUTE_DIRECTORY
    EmitJcc("z", "__io_sync_dir_exists_no");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_dir_exists_no");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("__io_sync_op_done");

    // --- dirCreate: CreateDirectoryA(arg0, NULL) ---
    DefineLabel("__io_sync_op_dir_create");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // lpSecurityAttributes = NULL
    EmitCallImport("kernel32.dll", "CreateDirectoryA");
    // CreateDirectoryA returns 0 on failure. Capture GetLastError so the
    // lowering can map ERROR_ALREADY_EXISTS / access-denied to specific variants.
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "__io_sync_op_done");
    EmitSyncWorkerCaptureLastError();
    EmitJmp("__io_sync_op_done");

    // --- getCwd: GetCurrentDirectoryA(260, heap_buf) → raw ptr, or 0 on failure ---
    DefineLabel("__io_sync_op_get_cwd");
    EmitCallRuntimeLabel("mm_raw_alloc_260");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save buf
    EmitMovRegImm(X86Register.Rcx, 260); // nBufferLength
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // lpBuffer
    EmitCallImport("kernel32.dll", "GetCurrentDirectoryA");
    // GetCurrentDirectoryA returns 0 on failure. Capture GetLastError, free
    // the buffer, and return 0.
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "__io_sync_op_get_cwd_ok");
    EmitSyncWorkerCaptureLastError();
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // buf to free
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax); // return 0
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_op_get_cwd_ok");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // result = buf ptr
    EmitJmp("__io_sync_op_done");

    // --- fileOpenRead: CreateFileA(GENERIC_READ, OPEN_EXISTING, FILE_FLAG_OVERLAPPED) + IOCP ---
    // Returns raw file handle (or -1). mm_alloc wrapping happens on the green thread.
    DefineLabel("__io_sync_op_file_open_read");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // path
    EmitMovRegImm(X86Register.Rdx, unchecked((long)0x80000000)); // GENERIC_READ
    EmitMovRegImm(X86Register.R8, 1);                  // FILE_SHARE_READ
    EmitXorRegReg(X86Register.R9, X86Register.R9);     // lpSecurityAttributes = NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x03, 0x00, 0x00, 0x00); // [rsp+0x20] = OPEN_EXISTING (3)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x40); // [rsp+0x28] = FILE_FLAG_OVERLAPPED (0x40000000)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // [rsp+0x30] = NULL
    EmitCallImport("kernel32.dll", "CreateFileA");
    // Check for INVALID_HANDLE_VALUE (-1)
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "__io_sync_file_open_read_fail");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);          // save handle
    // Associate file handle with IOCP
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);           // arg1: file handle
    EmitGlobalLoadReg(X86Register.Rdx, "__io_iocp");     // arg2: existing IOCP
    EmitXorRegReg(X86Register.R8, X86Register.R8);       // arg3: completion key = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);       // arg4: 0
    EmitCallImport("kernel32.dll", "CreateIoCompletionPort");
    // SetFileCompletionNotificationModes(handle, FILE_SKIP_COMPLETION_PORT_ON_SUCCESS=1)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitCallImport("kernel32.dll", "SetFileCompletionNotificationModes");
    // Return raw handle
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_file_open_read_fail");
    // RAX = INVALID_HANDLE_VALUE (-1). Capture GetLastError so the lowering
    // can map ERROR_FILE_NOT_FOUND → notFound, ERROR_ACCESS_DENIED → accessDenied.
    EmitSyncWorkerCaptureLastError();
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");

    // --- fileOpenWrite: CreateFileA(GENERIC_WRITE, CREATE_ALWAYS, FILE_FLAG_OVERLAPPED) + IOCP ---
    // Returns raw file handle (or -1). mm_alloc wrapping happens on the green thread.
    DefineLabel("__io_sync_op_file_open_write");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // path
    EmitMovRegImm(X86Register.Rdx, 0x40000000);         // GENERIC_WRITE
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // dwShareMode = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // lpSecurityAttributes = NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x02, 0x00, 0x00, 0x00); // [rsp+0x20] = CREATE_ALWAYS (2)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x40); // [rsp+0x28] = FILE_FLAG_OVERLAPPED (0x40000000)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // [rsp+0x30] = NULL
    EmitCallImport("kernel32.dll", "CreateFileA");
    // Check for INVALID_HANDLE_VALUE (-1)
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "__io_sync_file_open_write_fail");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);          // save handle
    // Associate file handle with IOCP
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitGlobalLoadReg(X86Register.Rdx, "__io_iocp");
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreateIoCompletionPort");
    // SetFileCompletionNotificationModes(handle, FILE_SKIP_COMPLETION_PORT_ON_SUCCESS=1)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitCallImport("kernel32.dll", "SetFileCompletionNotificationModes");
    // Return raw handle
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_file_open_write_fail");
    EmitSyncWorkerCaptureLastError();
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");

    // --- SyncOpFileOpenWriteExec: same as file_open_write on Windows (no Unix permissions) ---
    DefineLabel("__io_sync_op_file_open_write_exec");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // path
    EmitMovRegImm(X86Register.Rdx, 0x40000000);         // GENERIC_WRITE
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // dwShareMode = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // lpSecurityAttributes = NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x02, 0x00, 0x00, 0x00); // [rsp+0x20] = CREATE_ALWAYS (2)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x40); // [rsp+0x28] = FILE_FLAG_OVERLAPPED (0x40000000)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // [rsp+0x30] = NULL
    EmitCallImport("kernel32.dll", "CreateFileA");
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", "__io_sync_file_open_write_exec_fail");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);          // save handle
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitGlobalLoadReg(X86Register.Rdx, "__io_iocp");
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreateIoCompletionPort");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitCallImport("kernel32.dll", "SetFileCompletionNotificationModes");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_file_open_write_exec_fail");
    EmitSyncWorkerCaptureLastError();
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");

    // --- SyncOpProcessWait: WaitForSingleObject(arg0=hProcess, arg1=timeout_ms) ---
    // Yield-aware process wait: a green thread that needs to wait on a child
    // process can enqueue this op and switch to the scheduler instead of
    // blocking the OS thread on WaitForSingleObject. The sync worker thread
    // does the blocking wait on a dedicated OS thread, then signals completion
    // back to the waiting green thread. Currently UNUSED — wait_collect calls
    // WaitForSingleObject directly. Reserved for a future yield-aware path.
    // The mapping matches maxon_subprocess_wait_internal's direct path:
    // 0=completed, 1=timeout (also terminates the child to avoid zombies),
    // -1=error. timeout_ms=0 means INFINITE.
    DefineLabel("__io_sync_op_process_wait");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // hProcess
    EmitMovMemReg(-0x28, X86Register.Rcx, 8);                                // save handle for terminate fallback
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, SyncReqOffArg1); // timeout_ms
    // Translate 0 → INFINITE per the legacy contract.
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("nz", "__io_sync_pw_has_timeout");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);
    DefineLabel("__io_sync_pw_has_timeout");
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    // RAX = WAIT_OBJECT_0 (0) | WAIT_TIMEOUT (0x102) | WAIT_FAILED (0xFFFFFFFF)
    // WaitForSingleObject's documented return is a DWORD; some build modes
    // don't zero-extend RAX after the indirect call, so test the low 32 bits
    // explicitly to avoid being misled by garbage in RAX[63:32].
    EmitBytes(0x85, 0xC0);                               // TEST EAX, EAX
    EmitJcc("z", "__io_sync_pw_completed");
    EmitBytes(0x3D, 0x02, 0x01, 0x00, 0x00);             // CMP EAX, 0x102 (WAIT_TIMEOUT)
    EmitJcc("e", "__io_sync_pw_timeout");
    // Anything else is an error — capture errno and report -1.
    EmitSyncWorkerCaptureLastError();
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_pw_timeout");
    // Terminate the stuck child so it doesn't leak. Best-effort: ignore
    // TerminateProcess's return code, then surface 1 (timeout) to the caller.
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);            // hProcess
    EmitMovRegImm(X86Register.Rdx, 1);                   // exit code = 1
    EmitCallImport("kernel32.dll", "TerminateProcess");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_pw_completed");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("__io_sync_op_done");

    // --- closeHandle: CloseHandle(arg0) ---
    DefineLabel("__io_sync_op_close_handle");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // handle
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitJmp("__io_sync_op_done");

    // --- netConnect: WSAStartup + getaddrinfo + socket + connect → raw handle or -1/-2 ---
    // Stack layout within sync worker frame (0x260 total):
    //   [rbp-0x08]  = req ptr (sync worker)
    //   [rbp-0x10]  = result (sync worker)
    //   [rbp-0x18]  = comp ptr (sync worker)
    //   [rbp-0x20]  = addrinfo* result
    //   [rbp-0x28]  = socket handle
    //   [rbp-0x30]  = resolved IP
    //   [rbp-0x38]  = saved host cstring
    //   [rbp-0x40]  = saved port
    //   [rbp-0x48..rbp-0x78] = hints (48 bytes, base at rbp-0x78)
    //   [rbp-0x78..rbp-0x88] (unused gap)
    //   [rbp-0x90..rbp-0xA0] = sockaddr_in (16 bytes, base at rbp-0xA0)
    //   [rbp-0xA0..rbp-0x238] = WSADATA (408 bytes, base at rbp-0x238)
    DefineLabel("__io_sync_op_net_connect");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // cstring host
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // save host
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg1); // port
    EmitMovMemReg(-0x40, X86Register.Rcx, 8); // save port

    // Lazy WSAStartup — WSADATA at [rbp-0x238]
    EmitWsaEnsureInit(-0x238, "__io_sync_ntc_wsa_ok");

    // Zero the hints struct (48 bytes = 6 qwords at [rbp-0x78])
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    for (int i = 0; i < 6; i++)
      EmitMovMemReg(-0x78 + i * 8, X86Register.Rax, 8);

    // hints.ai_family = AF_INET (2) at offset 4
    EmitMovMemDwordImm(-0x78 + 4, 2);
    // hints.ai_socktype = SOCK_STREAM (1) at offset 8
    EmitMovMemDwordImm(-0x78 + 8, 1);
    // hints.ai_protocol = IPPROTO_TCP (6) at offset 12
    EmitMovMemDwordImm(-0x78 + 12, 6);

    // getaddrinfo(host, NULL, &hints, &result)
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);   // host cstring
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // NULL
    EmitLeaRegMem(X86Register.R8, -0x78);        // &hints
    EmitLeaRegMem(X86Register.R9, -0x20);        // &result
    EmitCallImport("ws2_32.dll", "getaddrinfo");

    // Check result (0 = success)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__io_sync_ntc_dns_ok");
    // DNS resolution failed → return -1
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");

    DefineLabel("__io_sync_ntc_dns_ok");

    // Extract IP from first addrinfo result
    EmitMovRegMem(X86Register.Rax, -0x20, 8);    // RAX = addrinfo* result
    EmitBytes(0x48, 0x8B, 0x40, 0x20);           // MOV RAX, [RAX+0x20] = ai_addr
    EmitBytes(0x8B, 0x40, 0x04);                  // MOV EAX, [RAX+4] = sin_addr
    EmitMovMemReg(-0x30, X86Register.Rax, 8);     // save resolved IP

    // freeaddrinfo(result)
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallImport("ws2_32.dll", "freeaddrinfo");

    // TEST-NET-1 (192.0.2.1) simulates connect failure for testing
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x3D); EmitDword(0x010200C0);       // CMP EAX, 192.0.2.1 (network byte order)
    EmitJcc("ne", "__io_sync_ntc_not_testnet");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_ntc_not_testnet");

    // WSASocketW(AF_INET=2, SOCK_STREAM=1, IPPROTO_TCP=6, NULL, 0, WSA_FLAG_OVERLAPPED=0x01)
    EmitMovRegImm(X86Register.Rcx, 2);   // af = AF_INET
    EmitMovRegImm(X86Register.Rdx, 1);   // type = SOCK_STREAM
    EmitMovRegImm(X86Register.R8, 6);    // protocol = IPPROTO_TCP
    EmitXorRegReg(X86Register.R9, X86Register.R9); // lpProtocolInfo = NULL
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemRspReg(0x20, X86Register.Rax); // g = 0
    EmitMovRegImm(X86Register.Rax, 1);       // dwFlags = WSA_FLAG_OVERLAPPED
    EmitMovMemRspReg(0x28, X86Register.Rax);
    EmitCallImport("ws2_32.dll", "WSASocketW");

    // Check for INVALID_SOCKET (-1)
    EmitBytes(0x48, 0x83, 0xF8, 0xFF);            // CMP RAX, -1
    EmitJcc("ne", "__io_sync_ntc_sock_ok");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitJmp("__io_sync_op_done");

    DefineLabel("__io_sync_ntc_sock_ok");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);     // save socket handle

    // Register socket with IOCP: CreateIoCompletionPort(socket, __io_iocp, 0, 0)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);           // socket handle
    EmitGlobalLoadReg(X86Register.Rdx, "__io_iocp");     // existing IOCP
    EmitXorRegReg(X86Register.R8, X86Register.R8);       // completion key = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);       // concurrent threads = 0
    EmitCallImport("kernel32.dll", "CreateIoCompletionPort");
    // SetFileCompletionNotificationModes(socket, FILE_SKIP_COMPLETION_PORT_ON_SUCCESS=1)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitCallImport("kernel32.dll", "SetFileCompletionNotificationModes");

    // Build sockaddr_in at [rbp-0xA0] (16 bytes)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0xA0, X86Register.Rax, 8);
    EmitMovMemReg(-0x98, X86Register.Rax, 8);
    // sin_family = AF_INET (2) — WORD at offset 0
    EmitBytes(0x66, 0xC7, 0x85); EmitDword(-0xA0); EmitBytes(0x02, 0x00);
    // sin_port = htons(port) — WORD at offset 2
    EmitMovRegMem(X86Register.Rax, -0x40, 8);     // RAX = port
    EmitBytes(0x66, 0xC1, 0xC0, 0x08);            // ROL AX, 8 (htons)
    EmitBytes(0x66, 0x89, 0x85); EmitDword(-0x9E); // MOV WORD [rbp-0x9E], AX
    // sin_addr = resolved IP — DWORD at offset 4
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x89, 0x85); EmitDword(-0x9C);       // MOV DWORD [rbp-0x9C], EAX

    // connect(socket, &sockaddr_in, 16)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);     // socket
    EmitLeaRegMem(X86Register.Rdx, -0xA0);        // &sockaddr_in
    EmitMovRegImm(X86Register.R8, 16);             // sizeof(sockaddr_in)
    EmitCallImport("ws2_32.dll", "connect");

    // Check result (0 = success)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__io_sync_ntc_conn_ok");
    // Connect failed — close socket and return -2
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitCallImport("ws2_32.dll", "closesocket");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitJmp("__io_sync_op_done");

    DefineLabel("__io_sync_ntc_conn_ok");
    // Return raw socket handle
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitJmp("__io_sync_op_done");

    // --- netSend: send(arg0=handle, arg1->buf, arg1->len, 0) → bytes sent ---
    DefineLabel("__io_sync_op_net_send");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // socket handle
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, SyncReqOffArg1); // args struct ptr
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save args struct for freeing
    EmitBytes(0x48, 0x8B, 0x50, 0x00);        // MOV RDX, [RAX+0] = buf_ptr
    EmitBytes(0x4C, 0x8B, 0x40, 0x08);        // MOV R8, [RAX+8] = length
    EmitXorRegReg(X86Register.R9, X86Register.R9); // flags = 0
    EmitCallImport("ws2_32.dll", "send");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save result
    // Free args struct
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // restore result
    EmitJmp("__io_sync_op_done");

    // --- netRecv: recv(arg0=handle, arg1->buf, arg1->len, 0) → bytes received ---
    DefineLabel("__io_sync_op_net_recv");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // socket handle
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, SyncReqOffArg1); // args struct ptr
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save args struct for freeing
    EmitBytes(0x48, 0x8B, 0x50, 0x00);        // MOV RDX, [RAX+0] = buf_ptr
    EmitBytes(0x4C, 0x8B, 0x40, 0x08);        // MOV R8, [RAX+8] = capacity
    EmitXorRegReg(X86Register.R9, X86Register.R9); // flags = 0
    EmitCallImport("ws2_32.dll", "recv");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save result
    // Free args struct
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // restore result
    EmitJmp("__io_sync_op_done");

    // --- netClose: closesocket(arg0=handle) ---
    DefineLabel("__io_sync_op_net_close");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // handle
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "__io_sync_ntc_close_skip");
    EmitCallImport("ws2_32.dll", "closesocket");
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_ntc_close_skip");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("__io_sync_op_done");

    // --- dnsResolve: getaddrinfo(arg0=host, NULL, &hints, &result) → resolved IP or -1/-2 ---
    // DNS resolution only — no socket creation. Returns 4-byte IP packed in qword,
    // or -1 (DNS failure), or -2 (TEST-NET-1 sentinel).
    // Uses sync worker's stack: hints at [rbp-0x78], result at [rbp-0x20], host at [rbp-0x38]
    DefineLabel("__io_sync_op_dns_resolve");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // cstring host
    EmitMovMemReg(-0x38, X86Register.Rcx, 8); // save host

    // Lazy WSAStartup — WSADATA at [rbp-0x238]
    EmitWsaEnsureInit(-0x238, "__io_sync_dns_wsa_ok");

    // Zero the hints struct (48 bytes = 6 qwords at [rbp-0x78])
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    for (int i = 0; i < 6; i++)
      EmitMovMemReg(-0x78 + i * 8, X86Register.Rax, 8);
    // hints.ai_family = AF_INET (2) at offset 4
    EmitMovMemDwordImm(-0x78 + 4, 2);
    // hints.ai_socktype = SOCK_STREAM (1) at offset 8
    EmitMovMemDwordImm(-0x78 + 8, 1);
    // hints.ai_protocol = IPPROTO_TCP (6) at offset 12
    EmitMovMemDwordImm(-0x78 + 12, 6);

    // getaddrinfo(host, NULL, &hints, &result)
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);   // host cstring
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // NULL
    EmitLeaRegMem(X86Register.R8, -0x78);        // &hints
    EmitLeaRegMem(X86Register.R9, -0x20);        // &result
    EmitCallImport("ws2_32.dll", "getaddrinfo");

    // Check result (0 = success)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__io_sync_dns_ok");
    // DNS resolution failed → return -1
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("__io_sync_op_done");

    DefineLabel("__io_sync_dns_ok");

    // Extract IP from first addrinfo result
    EmitMovRegMem(X86Register.Rax, -0x20, 8);    // RAX = addrinfo* result
    EmitBytes(0x48, 0x8B, 0x40, 0x20);           // MOV RAX, [RAX+0x20] = ai_addr
    EmitBytes(0x8B, 0x40, 0x04);                  // MOV EAX, [RAX+4] = sin_addr
    EmitMovMemReg(-0x30, X86Register.Rax, 8);     // save resolved IP

    // freeaddrinfo(result)
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallImport("ws2_32.dll", "freeaddrinfo");

    // TEST-NET-1 (192.0.2.1) simulates connect failure for testing
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x3D); EmitDword(0x010200C0);       // CMP EAX, 192.0.2.1 (network byte order)
    EmitJcc("ne", "__io_sync_dns_not_testnet");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitJmp("__io_sync_op_done");
    DefineLabel("__io_sync_dns_not_testnet");

    // Return resolved IP (4-byte value in qword)
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitJmp("__io_sync_op_done");

    // --- Common: direct GT completion (no intermediate queue) ---
    DefineLabel("__io_sync_op_done");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result

    // gt = req->waiter
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rcx, SyncReqOffWaiter);
    EmitMovMemReg(-0x18, X86Register.Rdx, 8); // save gt

    // Free req
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // __io_complete_gt(gt, result, result, error)
    // error is the captured Win32 error code (0 on success); failure handlers
    // populate -0x248 with GetLastError before jumping here.
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // gt
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // result
    EmitMovRegMem(X86Register.R8, -0x10, 8);  // len = result
    EmitMovRegMem(X86Register.R9, -0x248, 8); // error = captured code
    EmitCallRuntimeLabel("__io_complete_gt");

    EmitJmp("__io_sync_worker_top");

    // --- Shutdown: free req, exit thread ---
    DefineLabel("__io_sync_worker_shutdown");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
  }

  /// <summary>
  /// __io_check_completions(): Drain the IoCompletion done queue and re-enqueue green threads.
  /// Called from __gt_cleanup drain loop and __gt_yield_completed.
  /// Non-blocking: returns immediately if no completions are pending.
  /// </summary>
  private void EmitIoCheckCompletions() {
    EmitRuntimeFunctionStart("__io_check_completions", 0, 0x30);

    DefineLabel("__io_check_comp_loop");
    // Directly dequeue — don't poll the event. The event is used only for blocking waits;
    // after WaitForSingleObject(INFINITE) the event is already consumed (auto-reset), so
    // polling here would always return WAIT_TIMEOUT and nothing would be drained.
    EmitCallRuntimeLabel("__io_dequeue_completion");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_check_comp_ret"); // queue empty → done

    // comp = RAX; write fields into waiter green thread and re-enqueue it
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save comp

    // gt = comp->waiter_gt
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, IoCompOffWaiter);
    EmitMovMemReg(-0x10, X86Register.Rcx, 8); // save gt

    // gt->io_result_val = comp->result_val
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, IoCompOffResult);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoResultVal, X86Register.Rdx);

    // gt->io_result_len = comp->result_len
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, IoCompOffLen);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoResultLen, X86Register.Rdx);

    // gt->io_error_code = comp->error_code
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, IoCompOffError);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoErrorCode, X86Register.Rdx);

    // Free comp
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // Set waiter status = ready
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // gt
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffStatus, X86Register.Rax); // status = ready

    // Only enqueue regular GTs (stackBase != 0). MainThread GTs (stackBase == 0)
    // are driven by inline scheduling loops and must NOT be in the global run queue.
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, GtOffStackBase);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_check_comp_skip_enqueue");
    EmitCallRuntimeLabel("__gt_enqueue");
    DefineLabel("__io_check_comp_skip_enqueue");

    EmitJmp("__io_check_comp_loop");

    DefineLabel("__io_check_comp_ret");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_submit_sync(op_rcx, arg0_rdx, arg1_r8): Submit a non-overlappable I/O request.
  /// Yields the current green thread until the sync worker processes the request.
  /// Returns result value in RAX.
  /// </summary>
  private void EmitIoSubmitSync() {
    EmitRuntimeFunctionStart("__io_submit_sync", 3, 0x40);
    // [rbp-0x08] = op, [rbp-0x10] = arg0, [rbp-0x18] = arg1

    // Check cancel flag — abort immediately if cancelled
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffCancelFlag);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__io_submit_sync_cancelled");

    // Allocate SyncRequest
    EmitMovRegImm(X86Register.Rcx, SyncReqSize);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save req

    // Fill: op, arg0, arg1, waiter_gt, next=0
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // op
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffOp, X86Register.Rdx);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // arg0
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffArg0, X86Register.Rdx);
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // arg1
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffArg1, X86Register.Rdx);
    EmitLoadCurrentGtInline(X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffWaiter, X86Register.Rdx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffNext, X86Register.Rdx);

    // Set current thread status = waiting and clear ioYielded BEFORE enqueueing.
    // The sync worker may complete and call __io_complete_gt immediately after SetEvent;
    // ioYielded must be 0 at that point so the spinwait blocks until our context switch
    // saves RSP/RBP. Only after the worker loop resumes does it set ioYielded=1.
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegImm(X86Register.Rdx, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rdx);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoYielded, X86Register.Rax);

    // Enqueue the request and signal the sync worker
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallRuntimeLabel("__io_enqueue_sync_req");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitCallImportOnSystemStack("kernel32.dll", "SetEvent");

    if (Compiler.AsyncTrace) {
      // Trace: "io_yield #N [op_name] [M=W]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_yield");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitIoTraceOpSuffix("yield");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    // Check if current GT is the mainThread (self-switch would be a no-op)
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", "__io_submit_sync_mainthread_loop");

    // Non-mainThread path: switch to P->mainThread (worker loop handles scheduling)
    // (ioYielded already cleared above before enqueueing)
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp("__io_submit_sync_resume");

    // MainThread path: we ARE the mainThread, can't switch to ourselves.
    // Spin on completions + wake event until our I/O finishes.
    DefineLabel("__io_submit_sync_mainthread_loop");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    // Drain completions (may re-enqueue GTs including us)
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_timer_check");
    // Check if our status changed from "waiting" (sync worker completed our request)
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusWaiting);
    EmitJcc("ne", "__io_submit_sync_resume"); // I/O done, proceed to resume
    // Try dequeue a runnable GT and run it while we wait
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_submit_sync_mainthread_park");
    // Got a GT — context-switch to it, come back when someone switches back to us
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save dequeued GT
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Signal that the GT's context switch is complete so __io_complete_gt can safely enqueue it.
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    EmitJmp("__io_submit_sync_mainthread_loop"); // re-check after returning
    // No GT to run — park on done event briefly, then retry
    DefineLabel("__io_submit_sync_mainthread_park");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 50); // 50ms timeout
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp("__io_submit_sync_mainthread_loop");

    // Resume here after being re-enqueued by __io_check_completions
    DefineLabel("__io_submit_sync_resume");

    if (Compiler.AsyncTrace) {
      // Trace: "io_resume #N [op_name] [M=W]\n"
      EmitAtTraceLock();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_resume");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitLoadCurrentGtInline(X86Register.Rax);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitIoTraceOpSuffix("resume");
      EmitAtTraceWorkerSuffix();
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitAtTraceUnlock();
    }

    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoResultVal);
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_submit_sync_cancelled");
    // Store ERROR_OPERATION_ABORTED and return 0 without yielding
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegImm(X86Register.Rax, 0x3EF); // ERROR_OPERATION_ABORTED
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emits a compare chain that reads the sync op code from [rbp-0x08] and prints
  /// the matching operation name suffix (e.g., " [net_connect]") for async trace output.
  /// </summary>
  private void EmitIoTraceOpSuffix(string context) {
    var doneLabel = $"__io_trace_op_done_{context}";
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // load op code

    var ops = new (long opCode, string symdata)[] {
      (SyncOpFileExists,    "__at_io_op_file_exists"),
      (SyncOpFileDelete,    "__at_io_op_file_delete"),
      (SyncOpFindFirst,     "__at_io_op_find_first"),
      (SyncOpFindNext,      "__at_io_op_find_next"),
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
      (SyncOpDnsResolve,    "__at_io_op_net_connect"),

    };

    foreach (var (opCode, symdata) in ops) {
      var skipLabel = $"__io_trace_skip_{context}_{opCode}";
      EmitCmpRegImm(X86Register.Rax, opCode);
      EmitJcc("ne", skipLabel);
      EmitLeaRegSymdataRel(X86Register.Rcx, symdata);
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitJmp(doneLabel);
      DefineLabel(skipLabel);
    }

    DefineLabel(doneLabel);
  }

  /// <summary>
  /// Acquires the trace CRITICAL_SECTION so the entire trace line is written atomically.
  /// Must be paired with EmitAtTraceUnlock(). Clobbers RCX.
  /// </summary>
  private void EmitAtTraceLock() {
    EmitGlobalLeaReg(X86Register.Rcx, "__at_trace_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "EnterCriticalSection");
  }

  /// <summary>
  /// Releases the trace CRITICAL_SECTION after a complete trace line has been written.
  /// Must be paired with EmitAtTraceLock(). Clobbers RCX.
  /// </summary>
  private void EmitAtTraceUnlock() {
    EmitGlobalLeaReg(X86Register.Rcx, "__at_trace_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "LeaveCriticalSection");
  }

  /// <summary>
  /// Emits " [M=N]" (without newline) into the async trace output, where N is the current worker's P->id.
  /// Reads P via the inline TLS path; clobbers RAX and RCX.
  /// Call this immediately before emitting the newline tag to annotate which worker produced each trace line.
  /// </summary>
  private void EmitAtTraceWorkerSuffix() {
    EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_worker_prefix"); // " [M="
    EmitCallRuntimeLabel("mm_trace_print_tag");
    // Load P->id via inline TLS (clobbers RAX only)
    EmitGlobalLoadReg(X86Register.Rax, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.Rax, X86Register.Rax, 0); // RAX = P*
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffId); // RCX = P->id
    EmitCallRuntimeLabel("mm_trace_print_i64");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_worker_suffix"); // "]"
    EmitCallRuntimeLabel("mm_trace_print_tag");
  }

  /// <summary>
  /// __io_submit_async(handle_rcx, buf_rdx, size_r8, offset_r9): Submit overlapped ReadFile/WriteFile.
  /// Yields the current green thread until IOCP completes the I/O.
  /// The direction (read vs write) is encoded in the handle's open mode.
  /// Callers use separate stub wrappers for read vs write.
  /// Returns bytes transferred in RAX.
  /// Note: The actual read/write call is in the per-stub wrappers; this function handles
  /// the OVERLAPPED context setup, yielding, and resumption pattern.
  /// </summary>
  private void EmitIoSubmitHelpers() {
    // Note: actual ReadFile/WriteFile calls are in the stubs (maxon_file_read, maxon_managed_file_write)
    // which call the helpers below.
    EmitIoSubmitRead();
    EmitIoSubmitWrite();
    EmitIoEnqueueSyncReq();
    EmitIoDequeuesSyncReq();
    EmitIoCompleteGt();
    EmitIoGetLastError();
    EmitIoEnqueueCompletion();
    EmitIoDequeueCompletion();
    new Runtime.RuntimeEmitter(CreateBackend()).EmitMmRawAlloc260(Compiler.MmTrace);
    EmitIoPanicIo();
  }

  /// <summary>
  /// __io_get_last_error() -> i64 in RAX: returns gt->io_error_code for the current
  /// green thread. Used by the lowering to map raw OS error codes (Win32 GetLastError
  /// values captured by the sync worker, or POSIX errno on macOS) to method-specific
  /// error-enum ordinals (notFound / accessDenied / etc).
  /// </summary>
  private void EmitIoGetLastError() {
    EmitRuntimeFunctionStart("__io_get_last_error", 0, 0x20);
    // R10 = current GT pointer
    EmitLoadCurrentGtInline(X86Register.R10);
    // RAX = gt->io_error_code
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoErrorCode);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_submit_read(handle_rcx, buf_rdx, size_r8): Overlapped ReadFile + yield.
  /// </summary>
  private void EmitIoSubmitRead() {
    EmitRuntimeFunctionStart("__io_submit_read", 3, 0x50);
    // [rbp-0x08]=handle, [rbp-0x10]=buf, [rbp-0x18]=size
    EmitIoSubmitOverlappedCore("ReadFile", "__io_submit_read");
  }

  /// <summary>
  /// __io_submit_write(handle_rcx, buf_rdx, size_r8): Overlapped WriteFile + yield.
  /// </summary>
  private void EmitIoSubmitWrite() {
    EmitRuntimeFunctionStart("__io_submit_write", 3, 0x50);
    EmitIoSubmitOverlappedCore("WriteFile", "__io_submit_write");
  }

  private void EmitIoSubmitOverlappedCore(string ioFuncName, string labelPrefix) {
    // Check cancel flag
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffCancelFlag);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", $"{labelPrefix}_cancelled");

    // Allocate AsyncOpContext (64 bytes): OVERLAPPED at offset 0 + our fields
    EmitMovRegImm(X86Register.Rcx, AsyncCtxSize);
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: Compiler.MmTrace);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save ctx (mm_raw_alloc uses HEAP_ZERO_MEMORY — already zeroed)

    // ctx->waiter_gt = __gt_current
    EmitLoadCurrentGtInline(X86Register.Rdx);
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rax, AsyncCtxOffWaiter, X86Register.Rdx);

    // Store handle in gt->io_handle so CancelIoEx can reach it
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rdx);

    // Set current thread status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax);

    // Call ReadFile/WriteFile:
    //   handle=rcx, buf=rdx, size=r8, lpBytesTransferred=NULL, overlapped=ctx
    EmitSystemStackEnter(0x30); // shadow(0x20) + 1 stack arg + pad = 0x30
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // handle
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // buf
    EmitMovRegMem(X86Register.R8, -0x18, 8); // size
    EmitXorRegReg(X86Register.R9, X86Register.R9); // lpBytesTransferred = NULL
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovMemRspReg(0x20, X86Register.Rax);   // lpOverlapped = ctx (5th arg at [rsp+0x20])
    EmitCallImport("kernel32.dll", ioFuncName);
    EmitSystemStackLeave(0x30);

    // If TRUE: sync success (FILE_SKIP_COMPLETION). I/O is done immediately,
    // no IOCP completion will arrive. Restore status and go straight to resume.
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", $"{labelPrefix}_sync_done");
    // FALSE — check error code
    EmitCallImportOnSystemStack("kernel32.dll", "GetLastError");
    EmitMovRegImm(X86Register.Rcx, 997); // ERROR_IO_PENDING
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", $"{labelPrefix}_yield"); // IO_PENDING → async path, yield

    // Sync error: RAX still holds the error code from GetLastError above.
    // Save it, clear io_handle/status, store error, free ctx, return 0.
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save error code
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rcx); // clear io_handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rcx); // status = ready (0)
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // restore error code
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();

    // Sync success: I/O completed immediately, no yield needed.
    // Use GetOverlappedResult to retrieve the definitive byte count.
    DefineLabel($"{labelPrefix}_sync_done");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // zero the bytes slot (DWORD + padding)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // hFile
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // lpOverlapped = ctx
    EmitLeaRegMem(X86Register.R8, -0x30);     // &bytesTransferred at [rbp-0x30]
    EmitXorRegReg(X86Register.R9, X86Register.R9); // bWait = FALSE (already complete)
    EmitCallImportOnSystemStack("kernel32.dll", "GetOverlappedResult");
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // bytes transferred
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoResultVal, X86Register.Rcx); // gt->io_result_val = bytes
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax); // status = ready (0)
    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);

    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitJmp($"{labelPrefix}_resume");

    // Async yield: I/O is pending. Check if current GT is the mainThread.
    DefineLabel($"{labelPrefix}_yield");
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitLeaMainThreadInline(X86Register.Rdx);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", $"{labelPrefix}_mainthread_loop");

    // Non-mainThread: switch to P->mainThread (worker loop handles scheduling)
    // Clear ioYielded flag BEFORE context switch so __io_complete_gt knows to spin-wait
    // until the context switch saves our RSP/RBP (preventing a race where the IOCP thread
    // enqueues us before our state is saved).
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoYielded, X86Register.Rax);
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp($"{labelPrefix}_resume");

    // MainThread: spin on completions + wake event until our I/O finishes
    DefineLabel($"{labelPrefix}_mainthread_loop");
    EmitCallRuntimeLabel("__gt_process_pending_waiter");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffStatus);
    EmitCmpRegImm(X86Register.Rax, GtStatusWaiting);
    EmitJcc("ne", $"{labelPrefix}_resume");
    // Try dequeue a runnable GT
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", $"{labelPrefix}_mainthread_park");
    // Got a GT — run it
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitLoadCurrentGtInline(X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Signal that the GT's context switch is complete so __io_complete_gt can safely enqueue it.
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // RAX = the GT that just yielded
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffIoYielded, X86Register.Rcx);
    EmitJmp($"{labelPrefix}_mainthread_loop");
    // No GT — park briefly
    DefineLabel($"{labelPrefix}_mainthread_park");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 50);
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitJmp($"{labelPrefix}_mainthread_loop");

    // Resume: clear io_handle, return bytes transferred
    DefineLabel($"{labelPrefix}_resume");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rax); // clear io_handle
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoResultVal);
    EmitRuntimeFunctionEnd();

    DefineLabel($"{labelPrefix}_cancelled");
    EmitLoadCurrentGtInline(X86Register.R10);
    EmitMovRegImm(X86Register.Rax, 0x3EF); // ERROR_OPERATION_ABORTED
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  private void EmitIoEnqueueSyncReq() {
    // __io_enqueue_sync_req(req_rcx): Add to sync request queue under lock
    EmitRuntimeFunctionStart("__io_enqueue_sync_req", 1, 0x30);
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // save req
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "EnterCriticalSection");
    // if head == NULL: head = tail = req; else tail->next = req; tail = req
    EmitGlobalLoadReg(X86Register.Rax, "__io_sync_req_head");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__io_enqueue_sync_nonempty");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitGlobalStoreReg(X86Register.Rax, "__io_sync_req_head");
    EmitGlobalStoreReg(X86Register.Rax, "__io_sync_req_tail");
    EmitJmp("__io_enqueue_sync_done");
    DefineLabel("__io_enqueue_sync_nonempty");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_tail"); // tail
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // req
    EmitMovIndirectMemReg(X86Register.Rcx, SyncReqOffNext, X86Register.Rdx); // tail->next = req
    EmitGlobalStoreReg(X86Register.Rdx, "__io_sync_req_tail");
    DefineLabel("__io_enqueue_sync_done");
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImportOnSystemStack("kernel32.dll", "LeaveCriticalSection");
    EmitRuntimeFunctionEnd();
  }

  private void EmitIoDequeuesSyncReq() {
    // __io_dequeue_sync_req(): Dequeue one SyncRequest under lock; returns ptr or NULL.
    //
    // Critical: ResetEvent MUST be called INSIDE the CS when the queue empties,
    // otherwise a submitter's SetEvent between our LeaveCS and ResetEvent gets
    // clobbered, leaving work queued with no wake signal:
    //   us:        LeaveCS
    //   submitter: EnterCS, enqueue, LeaveCS, SetEvent  ← event signaled
    //   us:        ResetEvent                            ← event RESET, signal lost
    //   worker:    WFSO → hangs forever with work queued
    EmitRuntimeFunctionStart("__io_dequeue_sync_req", 0, 0x30);
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImport("kernel32.dll", "EnterCriticalSection");
    EmitGlobalLoadReg(X86Register.Rax, "__io_sync_req_head"); // head
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // spill dequeued node — Win32 calls clobber RAX
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_dequeue_sync_empty");
    // head = head->next
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffNext);
    EmitGlobalStoreReg(X86Register.Rcx, "__io_sync_req_head");
    // if new head == NULL: reset tail and reset event (all under the CS)
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("nz", "__io_dequeue_sync_unlock");
    EmitGlobalStoreReg(X86Register.Rcx, "__io_sync_req_tail"); // tail = NULL
    // ResetEvent INSIDE the CS so no submitter's SetEvent can be clobbered.
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitCallImport("kernel32.dll", "ResetEvent");
    EmitJmp("__io_dequeue_sync_unlock");
    DefineLabel("__io_dequeue_sync_empty");
    // head was NULL — just unlock
    DefineLabel("__io_dequeue_sync_unlock");
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImport("kernel32.dll", "LeaveCriticalSection");
    DefineLabel("__io_dequeue_sync_ret");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // reload dequeued node after Win32 calls
    // Clear next ptr on dequeued node so it doesn't form dangling list links
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_dequeue_sync_done");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffNext, X86Register.Rcx);
    DefineLabel("__io_dequeue_sync_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __io_complete_gt(gt_rcx, result_rdx, len_r8, error_r9): Write I/O result fields directly
  /// into a green thread and re-enqueue it. Called from IOCP drain thread and sync worker thread
  /// to bypass the intermediate IoCompletion done queue, eliminating the race where any worker
  /// could dequeue the completion.
  /// </summary>
  private void EmitIoCompleteGt() {
    EmitRuntimeFunctionStart("__io_complete_gt", 4, 0x30);
    // params saved by prologue: [rbp-0x08]=gt, [rbp-0x10]=result, [rbp-0x18]=len, [rbp-0x20]=error

    // gt->io_result_val = result
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // gt
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // result
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoResultVal, X86Register.Rdx);

    // gt->io_result_len = len
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // len
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoResultLen, X86Register.Rdx);

    // gt->io_error_code = error
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // error
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffIoErrorCode, X86Register.Rdx);

    // gt->status = ready (0)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffStatus, X86Register.Rax);

    // Only enqueue regular GTs (stackBase != 0). MainThread GTs (stackBase == 0)
    // are driven by inline scheduling loops and must NOT be in the global run queue.
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, GtOffStackBase);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_complete_gt_signal"); // mainThread: skip enqueue but still signal

    // Spin-wait until the GT's context switch has completed. The I/O submit path
    // clears ioYielded before the context switch, and the worker loop sets it to 1
    // after the switch returns. Without this, the IOCP/sync thread could enqueue
    // the GT while its RSP/RBP are still being saved, causing another worker to
    // load stale register state.
    DefineLabel("__io_complete_gt_spin");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // reload gt
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, GtOffIoYielded);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__io_complete_gt_enqueue");
    // PAUSE hint for spin-wait (reduces power and improves performance on HT cores)
    EmitBytes(0xF3, 0x90); // PAUSE
    EmitJmp("__io_complete_gt_spin");

    DefineLabel("__io_complete_gt_enqueue");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // reload gt for __gt_enqueue
    EmitCallRuntimeLabel("__gt_enqueue");

    // Signal events so parked threads wake up immediately.
    DefineLabel("__io_complete_gt_signal");

    // Signal __io_done_event so the mainthread park loop and cleanup drain loop
    // wake up and re-check. Without this the main thread sleeps up to 50ms
    // per I/O completion, causing massive latency for sync I/O chains.
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__io_complete_gt_wake_p0");
    EmitCallImport("kernel32.dll", "SetEvent");

    // Also wake P[0]'s main thread via its wakeEvent — it may be parked in
    // __gt_await waiting on wakeEvent with a timeout.
    DefineLabel("__io_complete_gt_wake_p0");
    EmitGlobalLoadReg(X86Register.Rax, "__sched_procs");
    EmitBytes(0x48, 0x8B, 0x00); // MOV RAX, [RAX] — load procs[0] = P[0]*
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, POffWakeEvent);
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__io_complete_gt_skip");
    EmitCallImport("kernel32.dll", "SetEvent");

    DefineLabel("__io_complete_gt_skip");

    EmitRuntimeFunctionEnd();
  }

  private void EmitIoEnqueueCompletion() {
    // __io_enqueue_completion(comp_rcx): Add IoCompletion to done queue under lock
    EmitRuntimeFunctionStart("__io_enqueue_completion", 1, 0x30);
    EmitMovMemReg(-0x08, X86Register.Rcx, 8);
    EmitGlobalLeaReg(X86Register.Rcx, "__io_done_cs");
    EmitCallImport("kernel32.dll", "EnterCriticalSection");
    EmitGlobalLoadReg(X86Register.Rax, "__io_done_head");
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("nz", "__io_enqueue_comp_nonempty");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitGlobalStoreReg(X86Register.Rax, "__io_done_head");
    EmitGlobalStoreReg(X86Register.Rax, "__io_done_tail");
    EmitJmp("__io_enqueue_comp_done");
    DefineLabel("__io_enqueue_comp_nonempty");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_tail");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitMovIndirectMemReg(X86Register.Rcx, IoCompOffNext, X86Register.Rdx);
    EmitGlobalStoreReg(X86Register.Rdx, "__io_done_tail");
    DefineLabel("__io_enqueue_comp_done");
    EmitGlobalLeaReg(X86Register.Rcx, "__io_done_cs");
    EmitCallImport("kernel32.dll", "LeaveCriticalSection");
    EmitRuntimeFunctionEnd();
  }

  private void EmitIoDequeueCompletion() {
    // __io_dequeue_completion(): Dequeue one IoCompletion under lock; returns ptr or NULL
    EmitRuntimeFunctionStart("__io_dequeue_completion", 0, 0x30);
    EmitGlobalLeaReg(X86Register.Rcx, "__io_done_cs");
    EmitCallImport("kernel32.dll", "EnterCriticalSection");
    EmitGlobalLoadReg(X86Register.Rax, "__io_done_head");
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // spill dequeued node — LeaveCriticalSection clobbers RAX
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("z", "__io_dequeue_comp_unlock");
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, IoCompOffNext);
    EmitGlobalStoreReg(X86Register.Rcx, "__io_done_head");
    EmitBytes(0x48, 0x85, 0xC9);
    EmitJcc("nz", "__io_dequeue_comp_unlock");
    EmitGlobalStoreReg(X86Register.Rcx, "__io_done_tail");
    DefineLabel("__io_dequeue_comp_unlock");
    EmitGlobalLeaReg(X86Register.Rcx, "__io_done_cs");
    EmitCallImport("kernel32.dll", "LeaveCriticalSection");
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // reload dequeued node after Win32 call
    // Clear next on dequeued node (so it doesn't form dangling list links)
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("z", "__io_dequeue_comp_done");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffNext, X86Register.Rcx);
    DefineLabel("__io_dequeue_comp_done");
    EmitRuntimeFunctionEnd();
  }

  // mm_raw_alloc_260: now emitted by RuntimeEmitter.MemoryManager.cs (unified x86/ARM64)

  private void EmitIoPanicIo() {
    // __gt_panic_io(): Called on unknown sync op code — should never happen
    EmitRuntimeFunctionStart("__gt_panic_io", 0, 0x20);
    var msgLabel = "__io_panic_msg";
    DefineRdata(msgLabel, System.Text.Encoding.ASCII.GetBytes("PANIC: unknown I/O op code\0"));
    EmitLeaRegRipRel(X86Register.Rcx, msgLabel);
    EmitCallRuntimeLabel("mrt_panic");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Inline trace helpers -- emit x86 machine code sequences for trace output.
  // Used by COW and other runtime functions that need trace output inline.
  // ==========================================================================

  /// <summary>
  /// Emit the scope annotation suffix: prints " [scope]" if [rbp+scopeSlot] is non-null,
  /// then prints a newline.
  /// </summary>
  private void EmitMmTraceScopeAndNewline(string skipLabel, int scopeSlot = -0x10) {
    EmitMovRegMem(X86Register.Rax, scopeSlot, 8); // scope_cstr
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", skipLabel);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_lbracket");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, scopeSlot, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rbracket");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    DefineLabel(skipLabel);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_newline");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
  }

  /// Inline helper: emit code to print "Type #N" from user_ptr at [rbp + ptrSlot].
  private void EmitTraceTagAndId(int ptrSlot) {
    EmitMovRegMem(X86Register.Rcx, ptrSlot, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_packed_tag")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_hash");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, ptrSlot, 8);
    EmitBytes(0x48, 0x8B, 0x48, 0xE8); // MOV rcx, [rax-24]
    EmitBytes(0x48, 0xC1, 0xE9, 0x10); // SHR rcx, 16
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
  }

  /// Inline helper: emit code to print " rc=N" from user_ptr at [rbp + ptrSlot].
  private void EmitTraceRc(int ptrSlot, int rcSubtract = 0) {
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_rc_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, ptrSlot, 8);
    EmitBytes(0x48, 0x8B, 0x48, 0xF8); // MOV rcx, [rax-8]
    if (rcSubtract > 0) EmitSubRegImm(X86Register.Rcx, rcSubtract);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
  }

  /// Inline helper: emit code to print " size=N" from size value at [rbp + sizeSlot].
  private void EmitTraceSize(int sizeSlot) {
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_tag_size_eq");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitMovRegMem(X86Register.Rcx, sizeSlot, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_i64")); EmitDword(0);
  }

  /// <summary>
  /// Emit inline trace output: indent + tagLabel + "TypeName #N rc=R [scope]\n".
  /// </summary>
  private void EmitInlineTrace(string tagLabel, string uniquePrefix, int ptrSlot, int scopeSlot,
      bool printRc = true, int rcSubtract = 0, int? sizeSlot = null) {
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_indent")); EmitDword(0);
    EmitLeaRegSymdataRel(X86Register.Rcx, tagLabel);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_trace_print_tag")); EmitDword(0);
    EmitTraceTagAndId(ptrSlot);
    if (printRc) EmitTraceRc(ptrSlot, rcSubtract);
    if (sizeSlot.HasValue) EmitTraceSize(sizeSlot.Value);
    EmitMmTraceScopeAndNewline($"{uniquePrefix}_no_scope", scopeSlot);
  }

  // ===========================================================================
  // Fault handler glue for x64-Windows. See RuntimeEmitter.FaultHandler for the
  // shared logic; this file emits the platform-specific install / prolog / epilog.
  // ===========================================================================

  // EXCEPTION_POINTERS struct (winnt.h).
  private const int ExceptionPointersOffExceptionRecord = 0x00;
  private const int ExceptionPointersOffContextRecord = 0x08;

  // x64 CONTEXT struct (winnt.h).
  private const int ContextOffRsp = 0x098;
  private const int ContextOffRbp = 0x0A0;
  private const int ContextOffRip = 0x0F8;

  // NTSTATUS exception codes. Spelled as `0xC0000005L` (not `(int)0xC0000005`) so
  // EmitMovRegImm picks the zero-extending MOV r32 form — the OS-loaded code is also
  // a zero-extended DWORD, so equality compares both sides bit-identically.
  private const long ExceptionCodeAccessViolation = 0xC0000005L;
  private const long ExceptionCodeIntDivideByZero = 0xC0000094L;
  private const long ExceptionCodeIntOverflow = 0xC0000095L;
  private const long ExceptionCodeStackOverflow = 0xC00000FDL;

  // VEH return values.
  private const int VehContinueSearch = 0;
  private const int VehContinueExecution = -1;

  internal void EmitInstallFaultHandler(string thunkLabel) {
    // Publish the absolute address of __gt_fault_diagnostic into a global so the
    // shared handler can use it as the redirect RIP. The runtime assembler can only
    // LEA via RIP-relative; the OS needs an absolute value to resume at.
    EmitLeaFuncAddr(X86Register.Rax, "__gt_fault_diagnostic");
    EmitGlobalStoreReg(X86Register.Rax, "__gt_fault_diagnostic_addr");

    // AddVectoredExceptionHandler(first=1, handler=thunk).
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitLeaFuncAddr(X86Register.Rdx, thunkLabel);
    EmitCallImport("kernel32.dll", "AddVectoredExceptionHandler");
  }

  internal void EmitFaultHandlerProlog(string thunkLabel, string sharedHandlerLabel) {
    // LONG thunk(EXCEPTION_POINTERS* p);  Win64 calling convention: RCX = p.
    // EmitRuntimeFunctionStart spills RCX to [rbp-0x08]; the epilog reloads it from
    // there because no callee-saved register survives the inner call.
    EmitRuntimeFunctionStart(thunkLabel, 1, 0x40);

    // RAX = p->ExceptionRecord; EAX = ExceptionRecord->ExceptionCode (DWORD at +0).
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rcx, ExceptionPointersOffExceptionRecord);
    EmitBytes(0x8B, 0x00); // MOV EAX, [RAX]

    EmitMovRegImm(X86Register.Rdx, ExceptionCodeAccessViolation);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rdx);
    EmitJcc("e", "__gt_ftp_av");
    EmitMovRegImm(X86Register.Rdx, ExceptionCodeIntDivideByZero);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rdx);
    EmitJcc("e", "__gt_ftp_div");
    EmitMovRegImm(X86Register.Rdx, ExceptionCodeIntOverflow);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rdx);
    EmitJcc("e", "__gt_ftp_intovf");
    EmitMovRegImm(X86Register.Rdx, ExceptionCodeStackOverflow);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rdx);
    EmitJcc("e", "__gt_ftp_stkovf");

    // Default: this is an exception we don't try to convert. Return
    // EXCEPTION_CONTINUE_SEARCH (0) immediately and let the OS handle it.
    EmitMovRegImm(X86Register.Rax, VehContinueSearch);
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_ftp_av");
    EmitMovRegImm(X86Register.Rcx, FaultCodeNilDeref);
    EmitJmp("__gt_ftp_code_chosen");
    DefineLabel("__gt_ftp_div");
    EmitMovRegImm(X86Register.Rcx, FaultCodeDivZero);
    EmitJmp("__gt_ftp_code_chosen");
    DefineLabel("__gt_ftp_intovf");
    EmitMovRegImm(X86Register.Rcx, FaultCodeIntOverflow);
    EmitJmp("__gt_ftp_code_chosen");
    DefineLabel("__gt_ftp_stkovf");
    EmitMovRegImm(X86Register.Rcx, FaultCodeStackOverflow);
    // Fall through to code_chosen.

    DefineLabel("__gt_ftp_code_chosen");
    // RCX = faultCode. Pack (rip, rsp, rbp) from ContextRecord into the shared
    // handler's argument registers (RDX, R8, R9). For access violations we
    // also stash the bad VA (ExceptionInformation[1]) into a global so the
    // diagnostic printer can include it in its output. Globals aren't ideal
    // when two threads can fault concurrently, but two concurrent fatal AVs
    // already produces interleaved output today; the diagnostic exit is
    // racing-by-design and we accept the small loss of fidelity in exchange
    // for not threading another argument through the shared-handler ABI.
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = EXCEPTION_POINTERS*
    EmitMovRegIndirectMem(X86Register.R11, X86Register.Rax, ExceptionPointersOffExceptionRecord);
    // ExceptionInformation[1] is at offset 0x28 of EXCEPTION_RECORD on x64
    // (4 ExceptionCode + 4 ExceptionFlags + 8 chain ptr + 8 ExceptionAddress
    // + 4 NumberParameters + 4 pad + 8 ExceptionInformation[0] = 40 bytes,
    // then [1] at +40 = 0x28).
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, 0x28);
    EmitGlobalStoreReg(X86Register.R11, "__gt_fault_last_addr");

    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, ExceptionPointersOffContextRecord);
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, ContextOffRip);
    EmitMovRegIndirectMem(X86Register.R8, X86Register.Rax, ContextOffRsp);
    EmitMovRegIndirectMem(X86Register.R9, X86Register.Rax, ContextOffRbp);

    EmitCallRuntimeLabel(sharedHandlerLabel);
    // RAX = sentinel (0 = recover, FaultCodeDontRecover = chain). Fall through.
  }

  internal void EmitFaultHandlerEpilog() {
    // RAX = sentinel from the shared handler. Nonzero means "don't recover" — any
    // value other than 0 routes here, so the shared handler is free to grow its
    // sentinel set without changing the epilog.
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__gt_fte_dont_recover");

    EmitLoadCurrentGtInline(X86Register.R11);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.R11, GtOffFaultRedirectRip);
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.R11, GtOffFaultRedirectRsp);
    EmitMovRegIndirectMem(X86Register.R8, X86Register.R11, GtOffFaultRedirectFp);

    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, ExceptionPointersOffContextRecord);
    EmitMovIndirectMemReg(X86Register.Rax, ContextOffRip, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, ContextOffRsp, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, ContextOffRbp, X86Register.R8);

    EmitMovRegImm(X86Register.Rax, VehContinueExecution);
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_fte_dont_recover");
    EmitMovRegImm(X86Register.Rax, VehContinueSearch);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_force_segfault(): deliberately dereferences address 0 to trigger a CPU
  /// access-violation fault. Used by spec tests to exercise the fault-handling path
  /// (VEH on Windows, SIGSEGV handler on macOS) and the last-resort UEF on Windows.
  /// Never returns — the load always faults, so the epilogue is unreachable.
  /// </summary>
  private void EmitMaxonForceSegfault() {
    EmitRuntimeFunctionStart("maxon_force_segfault", 0, 0x20);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, 0);
    EmitRuntimeFunctionEnd();
  }

  // ============================================================================
  // Phase 3.2 — Subprocess runtime (Windows X86 backend).
  //
  // Strategy
  // --------
  // CreateProcessW spawns the child with three configurable stdio kinds per
  // stream (kind = 0 discard, 1 inherit, 2 collect, 3 file). Collect mode
  // hands the parent the read end of an anonymous pipe; a per-stream drain
  // thread spawned by `maxon_subprocess_wait_collect` blocks on ReadFile
  // until the child exits and Windows closes its write end. Wait routes
  // through __io_submit_sync(SyncOpProcessWait) so a green-thread caller
  // yields to the scheduler rather than blocking the OS thread.
  //
  // Handle struct (mm_raw_alloc'd, lifetime = release_handle):
  //   +0x00  hProcess              child process handle
  //   +0x08  hStdoutRead           parent's read end of stdout pipe (0 if !collect)
  //   +0x10  hStderrRead           parent's read end of stderr pipe (0 if !collect)
  //   +0x18  hStdinWrite           parent's write end of stdin pipe  (0 if !bytes)
  //   +0x20  hStdoutDrainThread    OS thread that drains hStdoutRead
  //   +0x28  hStderrDrainThread    OS thread that drains hStderrRead
  //   +0x30  hStdinFeedThread      OS thread that writes the stdin payload
  //   +0x38  stdoutBufPtr          mm_raw_alloc'd buffer of captured stdout
  //   +0x40  stdoutBufLen
  //   +0x48  stderrBufPtr
  //   +0x50  stderrBufLen
  //   +0x58  stdinPayloadPtr       mm_raw_alloc'd copy of stdin bytes (for feed thread)
  //   +0x60  stdinPayloadLen
  //   +0x68  stdoutLimit
  //   +0x70  stderrLimit
  //   +0x78  flags                 from spawn (bit 0 hideWindow, bit 1 newGroup, bit 2 detach)
  //   +0x80  pid                   GetProcessId(hProcess) cached at spawn time
  //   +0x88  startTimeMs           maxon_current_time_ms() at spawn time
  //   +0x90  drainCtxStdoutPtr     DrainCtx struct (freed by release_handle)
  //   +0x98  drainCtxStderrPtr
  //   +0xA0  feedCtxStdinPtr
  // total = 0xA8 (168 bytes)
  //
  // DrainCtx (mm_raw_alloc'd, lifetime = release_handle):
  //   +0x00  hRead                 the pipe handle to drain
  //   +0x08  bufPtrOut             address inside handle struct where bufPtr lands
  //   +0x10  bufLenOut             address inside handle struct where bufLen lands
  //   +0x18  limit                 max bytes to retain (older bytes are dropped)
  // total = 0x20
  //
  // FeedCtx (mm_raw_alloc'd, lifetime = release_handle):
  //   +0x00  hWrite                pipe write end into child stdin
  //   +0x08  dataPtr               payload to write
  //   +0x10  dataLen               payload length
  // total = 0x18 (round to 0x20)
  //
  // Result struct (mm_raw_alloc'd by wait_collect, freed by result_release):
  //   +0x00  statusKind            0 exited, 1 signalled, 2 timedOut
  //   +0x08  statusCode            exit code or NTSTATUS
  //   +0x10  stdoutCstringPtr      mm_raw_alloc'd cstring (transferred from handle)
  //   +0x18  stdoutLen
  //   +0x20  stderrCstringPtr
  //   +0x28  stderrLen
  //   +0x30  durationMs
  // total = 0x38 (round to 0x40)
  //
  // managedIsNull contract: callers pass a __ManagedMemory struct pointer;
  // we treat "null" as "length field is zero" because the parser always
  // wraps cstrings into managed structs (so the outer pointer is never 0).
  // ============================================================================

  // Handle struct field offsets
  private const int SubpOffHProcess = 0x00;
  private const int SubpOffHStdoutRead = 0x08;
  private const int SubpOffHStderrRead = 0x10;
  private const int SubpOffHStdinWrite = 0x18;
  private const int SubpOffHStdoutDrain = 0x20;
  private const int SubpOffHStderrDrain = 0x28;
  private const int SubpOffHStdinFeed = 0x30;
  private const int SubpOffStdoutBufPtr = 0x38;
  private const int SubpOffStdoutBufLen = 0x40;
  private const int SubpOffStderrBufPtr = 0x48;
  private const int SubpOffStderrBufLen = 0x50;
  private const int SubpOffStdinPayloadPtr = 0x58;
  private const int SubpOffStdinPayloadLen = 0x60;
  private const int SubpOffStdoutLimit = 0x68;
  private const int SubpOffStderrLimit = 0x70;
  private const int SubpOffFlags = 0x78;
  private const int SubpOffPid = 0x80;
  private const int SubpOffStartTime = 0x88;
  private const int SubpOffDrainCtxStdout = 0x90;
  private const int SubpOffDrainCtxStderr = 0x98;
  private const int SubpOffFeedCtxStdin = 0xA0;
  // hJob: Job Object that owns the child + any descendants it spawns. Used so
  // a timeout-driven kill walks the whole process tree (e.g. `cmd /c ping`
  // terminates the orphaned ping along with cmd). The job is created with
  // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so CloseHandle in release_handle is a
  // sufficient cleanup if the caller never explicitly killed the tree.
  private const int SubpOffHJob = 0xA8;
  private const int SubpHandleStructSize = 0xB0;

  // DrainCtx field offsets
  private const int DrainCtxOffHRead = 0x00;
  private const int DrainCtxOffBufPtrOut = 0x08;
  private const int DrainCtxOffBufLenOut = 0x10;
  private const int DrainCtxOffLimit = 0x18;
  private const int DrainCtxSize = 0x20;

  // FeedCtx field offsets
  private const int FeedCtxOffHWrite = 0x00;
  private const int FeedCtxOffDataPtr = 0x08;
  private const int FeedCtxOffDataLen = 0x10;
  private const int FeedCtxSize = 0x20;

  // Result struct field offsets
  private const int SubpResOffStatusKind = 0x00;
  private const int SubpResOffStatusCode = 0x08;
  private const int SubpResOffStdoutPtr = 0x10;
  private const int SubpResOffStdoutLen = 0x18;
  private const int SubpResOffStderrPtr = 0x20;
  private const int SubpResOffStderrLen = 0x28;
  private const int SubpResOffDurationMs = 0x30;
  private const int SubpResultStructSize = 0x40;

  // Stdio kind ints (must match stdlib/Subprocess.maxon contract). The
  // discard kind is implicit (any value that isn't 1/2/3 is treated as
  // discard by the spawn switch), so we don't define an explicit constant
  // for it to keep the unused-private-member analyzer happy.
  private const int StdioKindInherit = 1;
  private const int StdioKindCollect = 2;
  private const int StdioKindFile = 3;

  // Flags bits
  private const int SpawnFlagHideWindow = 1;
  private const int SpawnFlagNewGroup = 2;
  private const int SpawnFlagDetach = 4;

  private void EmitMaxonSubprocess() {
    // Global slot holding the most recent runtime-side error string (cstring
    // ptr). Set by spawn/wait failure paths, read by last_error_message.
    // The string is statically allocated (no free needed) — callers copy
    // it into managed memory via the cstring_to_managed lowering.
    DefineGlobal("__subp_last_error", 8, 0);
    // Scratch buffer used by FormatMessageA to render a Windows error code into
    // a human-readable string when CreateProcess / CreateFile / CreatePipe
    // fails. 512 bytes is enough for any system message.
    DefineGlobal("__subp_last_error_buf", 512, 0);
    DefineSymdata("__subp_err_search_path", "SearchPathW failed\0"u8.ToArray());
    DefineSymdata("__subp_err_create_process", "CreateProcessW failed\0"u8.ToArray());
    DefineSymdata("__subp_err_create_pipe", "CreatePipe failed\0"u8.ToArray());
    DefineSymdata("__subp_err_create_file", "CreateFileW failed\0"u8.ToArray());
    DefineSymdata("__subp_err_argv", "argv parse failed\0"u8.ToArray());
    DefineSymdata("__subp_err_alloc", "allocation failed\0"u8.ToArray());
    DefineSymdata("__subp_err_unsupported", "unsupported stdio kind\0"u8.ToArray());
    DefineSymdata("__subp_err_invalid_handle", "invalid subprocess handle\0"u8.ToArray());
    DefineSymdata("__subp_err_wait_failed", "WaitForSingleObject failed\0"u8.ToArray());
    DefineSymdata("__subp_err_signal_unsupported", "signal not supported on this platform\0"u8.ToArray());
    DefineSymdata("__subp_nul_devname", "NUL\0\0"u8.ToArray()); // ANSI "NUL", padded
    // UTF-16 "NUL\0" for CreateFileW
    DefineSymdata("__subp_nul_devname_w", new byte[] { (byte)'N', 0, (byte)'U', 0, (byte)'L', 0, 0, 0 });

    EmitMaxonManagedIsNull();
    EmitMaxonSubprocessLastErrorMessage();
    EmitMaxonSubprocessResolveOnPath();
    EmitMaxonSubprocessBuildCmdlineWide();
    EmitMaxonSubprocessSpawnCore();
    EmitMaxonSubprocessSpawn();
    EmitMaxonSubprocessDetach();
    EmitMaxonSubprocessGetPid();
    EmitMaxonSubprocessKill();
    EmitMaxonSubprocessSendSignal();
    EmitMaxonSubprocessReleaseHandle();
    EmitMaxonSubprocessDrainThreadEntry();
    EmitMaxonSubprocessFeedThreadEntry();
    EmitMaxonSubprocessWaitInternal();
    EmitMaxonSubprocessWaitCollect();
    EmitMaxonSubprocessResultStatusKind();
    EmitMaxonSubprocessResultStatusCode();
    EmitMaxonSubprocessResultStdout();
    EmitMaxonSubprocessResultStderr();
    EmitMaxonSubprocessResultDurationMs();
    EmitMaxonSubprocessResultRelease();
  }

  // --------------------------------------------------------------------------
  // managedIsNull(mm_buffer) — the MaxonCallRuntime lowering unwraps a
  // __ManagedMemory arg down to its buffer pointer (see
  // MaxonToStandardConversion.cs:2083), so we receive *just* the buffer
  // address, not the struct. The struct itself is never null because
  // RuntimeCallToManaged always wraps results in a freshly-allocated
  // __ManagedMemory. So we treat "null" as "the underlying buffer starts
  // with NUL" — i.e. an empty cstring. resolve_on_path and
  // last_error_message both return a freshly-allocated 1-byte "\0" for
  // their not-found sentinel (an mm_raw_alloc, not the rdata
  // __rt_empty_cstring symdata — RuntimeCallToManaged's matching
  // mm_raw_free would underflow __mm_raw_alloc_count on rdata pointers),
  // which round-trips through this check.
  // --------------------------------------------------------------------------
  private void EmitMaxonManagedIsNull() {
    EmitRuntimeFunctionStart("maxon_managed_is_null", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);                    // RAX = buffer ptr
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_min_null");
    // movzx ecx, byte [rax]  — peek the first byte.
    EmitBytes(0x0F, 0xB6, 0x08);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_min_null");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);             // not null → 0
    EmitJmp("rt_subp_min_done");
    DefineLabel("rt_subp_min_null");
    EmitMovRegImm(X86Register.Rax, 1);
    DefineLabel("rt_subp_min_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // last_error_message() — return a freshly-allocated copy of the cstring
  // stored in __subp_last_error (or of __rt_empty_cstring if none pending).
  // The fresh allocation is required because RuntimeCallToManaged's
  // matching mm_raw_free in the lowering would underflow
  // __mm_raw_alloc_count if we returned a static rdata pointer.
  //
  // NB: mm_raw_free on a static rdata pointer would corrupt the heap free
  // list. To avoid that, we always strdup the message into a fresh
  // mm_raw_alloc'd buffer; the parser-driven mm_raw_free then frees the
  // duplicate. The static rdata sits unchanged.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessLastErrorMessage() {
    EmitRuntimeFunctionStart("maxon_subprocess_last_error_message", 0, 0x40);
    EmitGlobalLoadReg(X86Register.Rax, "__subp_last_error");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_subp_lem_have_msg");
    EmitLeaRegSymdataRel(X86Register.Rax, "__rt_empty_cstring");
    DefineLabel("rt_subp_lem_have_msg");
    EmitMovMemReg(-0x08, X86Register.Rax, 8);                    // save source cstring

    // strlen(source)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_strlen")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);                    // save length

    // mm_raw_alloc(length + 1)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitAddRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);                    // save destination

    // memcpy(dst, src, length + 1)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitMovRegMem(X86Register.R8, -0x10, 8);
    EmitAddRegImm(X86Register.R8, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_memcpy")); EmitDword(0);

    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // resolveOnPath(name_mm) — SearchPathW with PATHEXT iteration. Returns
  // an mm_raw_alloc'd UTF-8 cstring of the absolute path, or
  // __rt_empty_cstring if not found. Length 0 → managedIsNull reports
  // null. The lookup is best-effort: it tries the raw name, then appends
  // each ;-separated extension from PATHEXT.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessResolveOnPath() {
    // Stack:
    //   [rbp-0x08]  managed name struct ptr (arg)
    //   [rbp-0x10]  utf8 name cstring (buffer at managed+0)
    //   [rbp-0x18]  utf8 name length
    //   [rbp-0x20]  utf16 name buffer (mm_raw_alloc'd; null terminated)
    //   [rbp-0x28]  utf16 result buffer (MAX_PATH wchars = 520 bytes; mm_raw_alloc'd)
    //   [rbp-0x30]  utf8 result cstring (mm_raw_alloc'd)
    //   [rbp-0x38]  required wide size (chars, includes null)
    //   [rbp-0x40]  required utf8 size (bytes, includes null)
    EmitRuntimeFunctionStart("maxon_subprocess_resolve_on_path", 1, 0x60);

    // MaxonCallRuntimeOp lowering passes the __ManagedMemory's BUFFER POINTER
    // (a UTF-8 cstring), not the struct. Compute its length with maxon_strlen.
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_empty");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);                    // utf8 buffer ptr
    // Empty cstring (first byte 0) → not found.
    EmitBytes(0x0F, 0xB6, 0x08);                                  // movzx ecx, byte [rax]
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_rop_empty");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_strlen")); EmitDword(0);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);                    // length (bytes)

    // MultiByteToWideChar(CP_UTF8, 0, utf8, len, NULL, 0) → required wchars
    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);                       // CP_UTF8
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);             // flags
    EmitMovRegMem(X86Register.R8, -0x10, 8);                     // utf8 ptr
    EmitMovRegMem(X86Register.R9, -0x18, 4);                     // length (DWORD)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00); // out=NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00); // outSize=0
    EmitCallImport("kernel32.dll", "MultiByteToWideChar");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_empty");
    EmitMovMemReg(-0x38, X86Register.Rax, 8);                    // required wchars (excludes null)

    // Allocate utf16 buffer = (wchars+1)*2 bytes
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitShlRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_empty");
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // Convert utf8 → utf16 into the buffer
    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x10, 8);
    EmitMovRegMem(X86Register.R9, -0x18, 4);
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);                      // [rsp+0x20] = wide buf
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x28);                      // [rsp+0x28] = wide capacity
    EmitCallImport("kernel32.dll", "MultiByteToWideChar");
    EmitSystemStackLeave(0x40);
    // Null-terminate at offset wchars*2
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitShlRegImm(X86Register.Rcx, 1);
    EmitBytes(0x66, 0xC7, 0x04, 0x08, 0x00, 0x00);                // MOV word [RAX+RCX], 0

    // Allocate result wide buffer (MAX_PATH = 260 wchars = 520 bytes, plus
    // padding for PATHEXT extension; pick 4096 to cover long paths).
    EmitMovRegImm(X86Register.Rcx, 4096);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_free_wide_empty");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    // SearchPathW(NULL, lpFileName, NULL, nBufferLength=2048, lpBuffer, NULL)
    // The 2048 is wchars (4096 bytes); we deliberately request the full
    // buffer so SearchPathW can return long absolute paths.
    EmitSystemStackEnter(0x40);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);             // lpPath = NULL (PATH)
    EmitMovRegMem(X86Register.Rdx, -0x20, 8);                    // lpFileName
    EmitXorRegReg(X86Register.R8, X86Register.R8);               // lpExtension = NULL
    EmitMovRegImm(X86Register.R9, 2048);                          // nBufferLength wchars
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);                      // lpBuffer
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00); // lpFilePart=NULL
    EmitCallImport("kernel32.dll", "SearchPathW");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_free_both_empty");
    EmitMovMemReg(-0x38, X86Register.Rax, 8);                    // wchars written (excludes null)

    // WideCharToMultiByte(CP_UTF8, 0, wide, wchars, NULL, 0, NULL, NULL) → utf8 bytes
    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x28, 8);
    EmitMovRegMem(X86Register.R9, -0x38, 4);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00); // out=NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00); // outSize=0
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // defaultChar
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x38, 0x00, 0x00, 0x00, 0x00); // usedDefault
    EmitCallImport("kernel32.dll", "WideCharToMultiByte");
    EmitSystemStackLeave(0x40);
    EmitMovMemReg(-0x40, X86Register.Rax, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_free_both_empty");

    // Allocate utf8 buffer (size + 1 for nul)
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rop_free_both_empty");
    EmitMovMemReg(-0x30, X86Register.Rax, 8);

    // Convert wide → utf8
    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x28, 8);
    EmitMovRegMem(X86Register.R9, -0x38, 4);
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);                      // out buf
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x28);                      // out size
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x38, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WideCharToMultiByte");
    EmitSystemStackLeave(0x40);
    // Null-terminate
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitBytes(0xC6, 0x04, 0x08, 0x00);                            // MOV byte [RAX+RCX], 0

    // Free intermediate wide buffers
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitJmp("rt_subp_rop_done");

    DefineLabel("rt_subp_rop_free_both_empty");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel("rt_subp_rop_free_wide_empty");
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    DefineLabel("rt_subp_rop_empty");
    // The RuntimeCallToManaged lowering always issues a matching
    // mm_raw_free on whatever pointer we return. Returning the static
    // __rt_empty_cstring sentinel would underflow __mm_raw_alloc_count
    // and trip the leak detector at exit. Allocate a fresh 1-byte zero
    // cstring so the alloc/free pair balances out. managedIsNull peeks
    // the first byte to detect "not found", which still reports null.
    EmitMovRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitBytes(0xC6, 0x00, 0x00);                                  // MOV byte [RAX], 0

    DefineLabel("rt_subp_rop_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // get_pid(handle) — read the cached pid from the handle struct.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessGetPid() {
    EmitRuntimeFunctionStart("maxon_subprocess_get_pid", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_gp_bad");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, SubpOffPid);
    EmitJmp("rt_subp_gp_done");
    DefineLabel("rt_subp_gp_bad");
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_subp_gp_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // kill(handle, force) — TerminateProcess(hProcess, force? 9 : 15).
  // Returns 0 on success, -1 on failure.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessKill() {
    EmitRuntimeFunctionStart("maxon_subprocess_kill", 2, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);                    // handle struct
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_kill_bad");
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SubpOffHProcess);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);                    // force flag
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_subp_kill_soft");
    EmitMovRegImm(X86Register.Rdx, 9);                            // SIGKILL-ish exit code
    EmitJmp("rt_subp_kill_call");
    DefineLabel("rt_subp_kill_soft");
    EmitMovRegImm(X86Register.Rdx, 15);                           // SIGTERM-ish
    DefineLabel("rt_subp_kill_call");
    EmitCallImportOnSystemStack("kernel32.dll", "TerminateProcess");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_kill_bad");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("rt_subp_kill_done");
    DefineLabel("rt_subp_kill_bad");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_invalid_handle");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_subp_kill_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // send_signal(handle, sig) — Windows: SIGINT (2) and SIGBREAK (21) map to
  // GenerateConsoleCtrlEvent against the process group (requires spawn flag
  // bit 1 CREATE_NEW_PROCESS_GROUP). Other signals are not portable; return -1.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessSendSignal() {
    EmitRuntimeFunctionStart("maxon_subprocess_send_signal", 2, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sig_unsupported");
    // sig
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCmpRegImm(X86Register.Rcx, 2);                            // SIGINT
    EmitJcc("e", "rt_subp_sig_ctrl_c");
    EmitCmpRegImm(X86Register.Rcx, 21);                           // SIGBREAK (Windows-only)
    EmitJcc("e", "rt_subp_sig_ctrl_break");
    EmitJmp("rt_subp_sig_unsupported");

    DefineLabel("rt_subp_sig_ctrl_c");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);             // CTRL_C_EVENT = 0
    EmitJmp("rt_subp_sig_emit");

    DefineLabel("rt_subp_sig_ctrl_break");
    EmitMovRegImm(X86Register.Rcx, 1);                            // CTRL_BREAK_EVENT

    DefineLabel("rt_subp_sig_emit");
    // pid (process group id) = handle->pid
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, SubpOffPid);
    EmitCallImportOnSystemStack("kernel32.dll", "GenerateConsoleCtrlEvent");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sig_unsupported");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("rt_subp_sig_done");

    DefineLabel("rt_subp_sig_unsupported");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_signal_unsupported");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitMovRegImm(X86Register.Rax, -1);

    DefineLabel("rt_subp_sig_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // release_handle(handle) — CloseHandle on everything we own, then free
  // the heap-allocated struct + drain ctx + feed ctx + captured buffers
  // (only if no wait_collect was performed; once wait_collect returns,
  // buffer ownership transfers to the result struct).
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessReleaseHandle() {
    EmitRuntimeFunctionStart("maxon_subprocess_release_handle", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rh_done");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);                    // save handle ptr

    EmitSubprocessCloseHandleField(SubpOffHProcess);
    EmitSubprocessCloseHandleField(SubpOffHStdoutRead);
    EmitSubprocessCloseHandleField(SubpOffHStderrRead);
    EmitSubprocessCloseHandleField(SubpOffHStdinWrite);
    EmitSubprocessCloseHandleField(SubpOffHStdoutDrain);
    EmitSubprocessCloseHandleField(SubpOffHStderrDrain);
    EmitSubprocessCloseHandleField(SubpOffHStdinFeed);
    // Close the Job Object last so KILL_ON_JOB_CLOSE has a chance to fire
    // (closing the job's last handle terminates any still-running
    // descendants). Closing hProcess first is fine — hJob owns the
    // descendant tree independently.
    EmitSubprocessCloseHandleField(SubpOffHJob);

    // Free any retained captured buffers (only present if wait_collect
    // wasn't called or hadn't run when release_handle fires). stdout/stderr
    // buffers are VirtualAlloc'd by the drain thread, so they need
    // VirtualFree rather than mm_raw_free.
    EmitSubprocessVirtualFreeFieldIfNonZero(SubpOffStdoutBufPtr);
    EmitSubprocessVirtualFreeFieldIfNonZero(SubpOffStderrBufPtr);
    EmitSubprocessFreeFieldIfNonZero(SubpOffStdinPayloadPtr);
    EmitSubprocessFreeFieldIfNonZero(SubpOffDrainCtxStdout);
    EmitSubprocessFreeFieldIfNonZero(SubpOffDrainCtxStderr);
    EmitSubprocessFreeFieldIfNonZero(SubpOffFeedCtxStdin);

    // Free the handle struct itself.
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    DefineLabel("rt_subp_rh_done");
    EmitRuntimeFunctionEnd();
  }

  /// CloseHandle on a non-zero field of the handle struct at [rbp-0x10],
  /// then zero the field. Each call expands inline; the field offset is the
  /// only thing that varies, so the helper keeps the body of release_handle
  /// scannable.
  private void EmitSubprocessCloseHandleField(int fieldOff) {
    var skipLabel = $"rt_subp_rh_skip_{fieldOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, fieldOff, X86Register.Rcx);
    DefineLabel(skipLabel);
  }

  /// VirtualFree(MEM_RELEASE) a non-zero pointer at handle->fieldOff, then
  /// zero the slot. Used for drain-thread buffers, which are reserved with
  /// VirtualAlloc rather than mm_raw_alloc.
  private void EmitSubprocessVirtualFreeFieldIfNonZero(int fieldOff) {
    var skipLabel = $"rt_subp_rh_vfree_skip_{fieldOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitSystemStackEnter(0x30);
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000);                         // MEM_RELEASE
    EmitCallImport("kernel32.dll", "VirtualFree");
    EmitSystemStackLeave(0x30);
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, fieldOff, X86Register.Rcx);
    DefineLabel(skipLabel);
  }

  /// mm_raw_free the non-zero pointer at handle->fieldOff, then zero the
  /// slot so a double-release is a no-op.
  private void EmitSubprocessFreeFieldIfNonZero(int fieldOff) {
    var skipLabel = $"rt_subp_rh_free_skip_{fieldOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, fieldOff, X86Register.Rcx);
    DefineLabel(skipLabel);
  }

  // --------------------------------------------------------------------------
  // Drain thread entry: lpThreadStartRoutine for CreateThread. Receives a
  // DrainCtx* in RCX (Win64 ABI). Reads from hRead in 4 KiB chunks into a
  // growable mm_raw_alloc'd buffer; stops on ReadFile failure or EOF.
  // Writes the final buffer ptr + length back through bufPtrOut/bufLenOut
  // before returning. Bytes beyond `limit` are silently dropped — the
  // current write position simply doesn't advance once limit is hit.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessDrainThreadEntry() {
    // Stack:
    //   [rbp-0x08]  drain ctx ptr
    //   [rbp-0x10]  buf (VirtualAlloc'd reserve)
    //   [rbp-0x18]  capacity
    //   [rbp-0x20]  length so far
    //   [rbp-0x28]  bytesRead (scratch for ReadFile)
    //   [rbp-0x30]  hRead (cached)
    //   [rbp-0x38]  limit (cached)
    // Need a bigger frame to hold chunkSize at -0x40.
    EmitRuntimeFunctionStart("__subp_drain_thread", 1, 0x80);

    // Drain runs on a plain CreateThread OS thread that the GMP scheduler
    // never sees. We deliberately avoid mm_raw_alloc / __slab_alloc here:
    // those allocators are per-P (mcache) and locking them from outside
    // the worker pool risks corrupting the running scheduler's cache. Use
    // VirtualAlloc directly. The buffer's lifetime extends past this
    // thread — wait_collect transfers ownership to the result struct,
    // which calls VirtualFree at result_release time.

    // Cache hRead and limit (the ctx pointer survives — we still need it
    // at the end for the writeback).
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, DrainCtxOffHRead);
    EmitMovMemReg(-0x30, X86Register.Rcx, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, DrainCtxOffLimit);
    EmitMovMemReg(-0x38, X86Register.Rcx, 8);

    // Reserve+commit the maximum buffer up front so realloc isn't needed.
    // Use the limit (capped to 16 MiB / a 16 MB cap that the stdlib
    // assigns by default). If limit is 0, fall back to 64 KiB.
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "__subp_drain_alloc_use_limit");
    EmitMovRegImm(X86Register.Rax, 0x10000);                      // 64 KiB default
    EmitMovMemReg(-0x38, X86Register.Rax, 8);
    DefineLabel("__subp_drain_alloc_use_limit");
    // VirtualAlloc(NULL, size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
    EmitSystemStackEnter(0x40);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rdx, -0x38, 8);                     // size = limit
    EmitMovRegImm(X86Register.R8, 0x3000);                         // MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                           // PAGE_READWRITE
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    EmitSystemStackLeave(0x40);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);                     // buf
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);                     // capacity = limit
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);                     // length = 0

    DefineLabel("__subp_drain_loop");
    // Read up to (capacity - length) bytes, capped at 4 KiB per ReadFile so
    // we don't ask for more than the OS pipe buffer can deliver in one go.
    EmitMovRegMem(X86Register.Rax, -0x18, 8);                    // capacity
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);                    // length
    EmitBytes(0x48, 0x29, 0xC8);                                  // SUB RAX, RCX → remaining
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("le", "__subp_drain_eof");                            // out of room → stop
    EmitMovRegImm(X86Register.Rcx, 4096);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("le", "__subp_drain_have_chunk");
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx);
    DefineLabel("__subp_drain_have_chunk");
    EmitMovMemReg(-0x40, X86Register.Rax, 8);                    // chunkSize for ReadFile

    // ReadFile(hRead, buf+length, chunkSize, &bytesRead, NULL)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitSystemStackEnter(0x30);
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);                    // hRead
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);                    // buf
    EmitMovRegMem(X86Register.Rax, -0x20, 8);                    // length
    EmitBytes(0x48, 0x01, 0xC2);                                  // ADD RDX, RAX
    EmitMovRegMem(X86Register.R8, -0x40, 8);                     // chunkSize
    EmitLeaRegMem(X86Register.R9, -0x28);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00); // overlapped=NULL
    EmitCallImport("kernel32.dll", "ReadFile");
    EmitSystemStackLeave(0x30);

    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__subp_drain_eof");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__subp_drain_eof");

    // length += bytesRead, capped at limit (if limit > 0)
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitAddRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegMem(X86Register.Rdx, -0x38, 8);
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "__subp_drain_store_len");
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("le", "__subp_drain_store_len");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdx);
    DefineLabel("__subp_drain_store_len");
    EmitMovMemReg(-0x20, X86Register.Rcx, 8);
    EmitJmp("__subp_drain_loop");

    DefineLabel("__subp_drain_eof");
    // Writeback: *bufPtrOut = buf, *bufLenOut = length
    EmitMovRegMem(X86Register.Rax, -0x08, 8);                    // ctx
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, DrainCtxOffBufPtrOut);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovIndirectMemReg(X86Register.Rcx, 0, X86Register.Rdx);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, DrainCtxOffBufLenOut);
    EmitMovRegMem(X86Register.Rdx, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rcx, 0, X86Register.Rdx);

    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // Feed thread entry: writes the stdin payload into the child's stdin pipe,
  // then closes the write end so the child sees EOF. Single WriteFile loop
  // for arbitrarily-large payloads (Windows pipes block when full, which is
  // fine here because the child's read is what unblocks us).
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessFeedThreadEntry() {
    EmitRuntimeFunctionStart("__subp_feed_thread", 1, 0x40);
    // [rbp-0x08] ctx ptr; [rbp-0x10] dataPtr; [rbp-0x18] remaining; [rbp-0x20] hWrite; [rbp-0x28] bytesWritten

    // Feed thread only calls WriteFile and CloseHandle — no allocator
    // calls — so it doesn't need a scheduler TLS binding.

    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FeedCtxOffHWrite);
    EmitMovMemReg(-0x20, X86Register.Rcx, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FeedCtxOffDataPtr);
    EmitMovMemReg(-0x10, X86Register.Rcx, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FeedCtxOffDataLen);
    EmitMovMemReg(-0x18, X86Register.Rcx, 8);

    DefineLabel("__subp_feed_loop");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__subp_feed_done");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    EmitSystemStackEnter(0x30);
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegMem(X86Register.R8, -0x18, 4);
    EmitLeaRegMem(X86Register.R9, -0x28);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
    EmitSystemStackLeave(0x30);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__subp_feed_done");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "__subp_feed_done");

    // dataPtr += written; remaining -= written
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitAddRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovMemReg(-0x10, X86Register.Rcx, 8);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitSubRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovMemReg(-0x18, X86Register.Rcx, 8);
    EmitJmp("__subp_feed_loop");

    DefineLabel("__subp_feed_done");
    // Close the write end so the child sees EOF on its stdin.
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    // Also clear the write-end slot in the handle struct so release_handle
    // doesn't double-close it. ctx doesn't carry that pointer, but the
    // parent zeroed hStdinWrite in the handle struct after spawn finished —
    // wait_collect re-reads the struct anyway. Simpler: the parent stores
    // null into handle->hStdinWrite right after starting this thread.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // subprocess_wait_internal(hProcess, timeoutMs) — wait on a single process
  // handle. Internal helper used by wait_collect; not exposed as a builtin.
  // Returns 0=completed, 1=timeout, -1=error. On timeout, calls
  // TerminateProcess(hProcess, 1) before returning 1 so the caller can
  // assume the child is no longer running.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessWaitInternal() {
    EmitRuntimeFunctionStart("maxon_subprocess_wait_internal", 2, 0x30);

    // Convert timeout: if 0, use INFINITE (0xFFFFFFFF) — caller convention is
    // "0 = wait forever", matching the legacy Process API.
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("nz", "rt_swi_has_timeout");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);
    DefineLabel("rt_swi_has_timeout");

    EmitMovRegMem(X86Register.Rcx, -0x08, 8);            // arg1: handle
    // RDX already has timeout
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");

    // Map Win32 wait result to Maxon convention: 0=completed, 1=timeout, -1=error
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_swi_ok");                            // RAX==0 -> completed
    EmitBytes(0x3D, 0x02, 0x01, 0x00, 0x00);              // CMP EAX, 0x102 (WAIT_TIMEOUT)
    EmitJcc("e", "rt_swi_timeout");
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("rt_swi_done");

    DefineLabel("rt_swi_timeout");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);            // arg1: handle
    EmitMovRegImm(X86Register.Rdx, 1);                   // arg2: exit code
    EmitCallImportOnSystemStack("kernel32.dll", "TerminateProcess");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("rt_swi_done");

    DefineLabel("rt_swi_ok");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);

    DefineLabel("rt_swi_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // wait_collect(handle, timeoutMs) — wait for the child to exit, drain
  // stdout/stderr concurrently, build a result struct. Returns the result
  // pointer, or -1 if the handle is bad (status fields encode timeouts
  // and abnormal exits; -1 is reserved for "we couldn't even attempt the
  // wait").
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessWaitCollect() {
    // Stack:
    //   [rbp-0x08]  handle struct ptr
    //   [rbp-0x10]  timeout ms
    //   [rbp-0x18]  result struct ptr
    //   [rbp-0x20]  waitOutcome (0=completed, 1=timeout, -1=error)
    //   [rbp-0x28]  exitCode (DWORD widened)
    //   [rbp-0x30]  endTimeMs
    EmitRuntimeFunctionStart("maxon_subprocess_wait_collect", 2, 0x50);

    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_wc_bad_handle");

    // maxon_subprocess_wait_internal(hProcess, timeout_ms) returns
    // 0=completed, 1=timeout, -1=error. Calls WaitForSingleObject directly
    // on the OS thread system stack; on timeout it issues
    // TerminateProcess(hProcess, 1) before returning 1.
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SubpOffHProcess);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitCallRuntimeLabel("maxon_subprocess_wait_internal");
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // On timeout, wait_internal only kills the immediate child via
    // TerminateProcess(hProcess, 1). That's insufficient when the child has spawned
    // descendants (e.g. `cmd /c ping ...` — terminating cmd leaves ping
    // running and holding the stdout pipe open, so the drain thread's
    // ReadFile blocks until ping's natural exit). Killing the Job Object
    // walks the entire descendant tree, releases the pipes, and lets the
    // drain threads exit cleanly.
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitCmpRegImm(X86Register.Rax, 1);
    EmitJcc("ne", "rt_subp_wc_no_kill_tree");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SubpOffHJob);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_wc_no_kill_tree");
    EmitMovRegImm(X86Register.Rdx, 1);                            // exit code for killed descendants
    EmitCallImportOnSystemStack("kernel32.dll", "TerminateJobObject");
    DefineLabel("rt_subp_wc_no_kill_tree");

    // Join drain threads (they exit when ReadFile returns 0; the child's
    // exit closes its end of each pipe, signalling EOF to ReadFile).
    EmitSubprocessJoinThreadAndClose(SubpOffHStdoutDrain);
    EmitSubprocessJoinThreadAndClose(SubpOffHStderrDrain);
    EmitSubprocessJoinThreadAndClose(SubpOffHStdinFeed);

    // Close our copies of the read pipe handles (drain thread held the
    // only producer; ReadFile is done).
    EmitSubprocessCloseHandleAtField(SubpOffHStdoutRead);
    EmitSubprocessCloseHandleAtField(SubpOffHStderrRead);
    // Stdin write end might still be open if there was no feed thread
    // (kind != bytes) — release_handle will close it.

    // Determine status kind/code.
    // waitOutcome:
    //   0  → GetExitCodeProcess; if (exitCode >> 28) == 0xC then signalled, else exited.
    //   1  → timedOut (statusKind=2, statusCode=124 — sysexits-style)
    //   -1 → exited (statusKind=0, statusCode=-1) — we couldn't get a real code.
    EmitMovRegImm(X86Register.Rcx, SubpResultStructSize);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_capture_result");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    // Zero the result struct
    EmitMovRegReg(X86Register.Rdi, X86Register.Rax);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovRegImm(X86Register.Rcx, SubpResultStructSize / 8);
    EmitBytes(0xF3, 0x48, 0xAB);                                  // REP STOSQ

    // Decode wait outcome
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitCmpRegImm(X86Register.Rax, 1);
    EmitJcc("e", "rt_subp_wc_timedout");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("s", "rt_subp_wc_err");

    // GetExitCodeProcess
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SubpOffHProcess);
    EmitLeaRegMem(X86Register.Rdx, -0x28);
    EmitCallImportOnSystemStack("kernel32.dll", "GetExitCodeProcess");

    // Heuristic: NTSTATUS abnormal exits have the top nibble 0xC.
    // exitCode >> 28 == 0xC  →  signalled
    EmitMovRegMem(X86Register.Rax, -0x28, 4);                    // DWORD
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitShrRegImm(X86Register.Rdx, 28);
    EmitCmpRegImm(X86Register.Rdx, 0xC);
    EmitJcc("e", "rt_subp_wc_signalled");
    // exited
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusKind, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusCode, X86Register.Rax);
    EmitJmp("rt_subp_wc_status_done");

    DefineLabel("rt_subp_wc_signalled");
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusKind, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusCode, X86Register.Rax);
    EmitJmp("rt_subp_wc_status_done");

    DefineLabel("rt_subp_wc_timedout");
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegImm(X86Register.Rdx, 2);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusKind, X86Register.Rdx);
    EmitMovRegImm(X86Register.Rdx, 124);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusCode, X86Register.Rdx);
    EmitJmp("rt_subp_wc_status_done");

    DefineLabel("rt_subp_wc_err");
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusKind, X86Register.Rdx);
    EmitMovRegImm(X86Register.Rdx, -1);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffStatusCode, X86Register.Rdx);
    // Fall through

    DefineLabel("rt_subp_wc_status_done");

    // Transfer captured buffers from the handle into the result. The
    // handle zeroes its own slots so release_handle doesn't double-free.
    EmitSubprocessTransferBuf(SubpOffStdoutBufPtr, SubpOffStdoutBufLen,
                              SubpResOffStdoutPtr, SubpResOffStdoutLen);
    EmitSubprocessTransferBuf(SubpOffStderrBufPtr, SubpOffStderrBufLen,
                              SubpResOffStderrPtr, SubpResOffStderrLen);

    // Compute durationMs = current_time_ms - startTime
    EmitCallRuntimeLabel("maxon_current_time_ms");
    EmitMovMemReg(-0x30, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SubpOffStartTime);
    EmitMovRegMem(X86Register.Rdx, -0x30, 8);
    EmitSubRegReg(X86Register.Rdx, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovIndirectMemReg(X86Register.Rcx, SubpResOffDurationMs, X86Register.Rdx);

    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitJmp("rt_subp_wc_done");

    DefineLabel("rt_subp_wc_bad_handle");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_invalid_handle");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitMovRegImm(X86Register.Rax, -1);

    DefineLabel("rt_subp_wc_done");
    EmitRuntimeFunctionEnd();
  }

  /// WaitForSingleObject(handle->fieldOff, INFINITE) if nonzero, then
  /// CloseHandle it and zero the slot.
  private void EmitSubprocessJoinThreadAndClose(int fieldOff) {
    var skipLabel = $"rt_subp_wc_join_skip_{fieldOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);
    EmitCallImportOnSystemStack("kernel32.dll", "WaitForSingleObject");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, fieldOff, X86Register.Rcx);
    DefineLabel(skipLabel);
  }

  /// CloseHandle on handle->fieldOff if nonzero, then zero the slot.
  private void EmitSubprocessCloseHandleAtField(int fieldOff) {
    var skipLabel = $"rt_subp_wc_close_skip_{fieldOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, fieldOff, X86Register.Rcx);
    DefineLabel(skipLabel);
  }

  /// Transfer a captured (bufPtr, bufLen) pair from the handle struct
  /// (at [rbp-0x08]) into the result struct (at [rbp-0x18]). The drain
  /// thread's buffer is VirtualAlloc'd (out-of-pool — the drain thread
  /// can't safely touch the slab allocator), so we mm_raw_alloc a
  /// matching buffer on the main thread, memcpy the bytes, then
  /// VirtualFree the original. After this the result struct owns a
  /// regular mm_raw_alloc'd buffer that release-time mm_raw_free can
  /// handle uniformly.
  private void EmitSubprocessTransferBuf(int hBufOff, int hLenOff, int rBufOff, int rLenOff) {
    var emptyLabel = $"rt_subp_wc_xfer_empty_{hBufOff:x2}";
    var doneLabel = $"rt_subp_wc_xfer_done_{hBufOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, hBufOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", emptyLabel);

    // Save (src, len) into scratch slots so we can re-load them across
    // the mm_raw_alloc / memcpy / VirtualFree calls.
    EmitMovMemReg(-0x38, X86Register.Rcx, 8);                    // src buf
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, hLenOff);
    EmitMovMemReg(-0x40, X86Register.Rdx, 8);                    // len
    // If len is 0 we can short-circuit to the empty result without
    // touching the allocator. Still VirtualFree the buffer.
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("nz", $"rt_subp_wc_xfer_nonempty_{hBufOff:x2}");
    // Free the empty drain buffer and emit the empty sentinel.
    EmitSystemStackEnter(0x30);
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000);                       // MEM_RELEASE
    EmitCallImport("kernel32.dll", "VirtualFree");
    EmitSystemStackLeave(0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, hBufOff, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, hLenOff, X86Register.Rcx);
    EmitJmp(emptyLabel);

    DefineLabel($"rt_subp_wc_xfer_nonempty_{hBufOff:x2}");
    // mm_raw_alloc(len + 1) — extra byte makes the result usable as a
    // null-terminated cstring by subprocessResultStream* readers.
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_pipe_buffer");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);                    // dest
    // memcpy(dest, src, len)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegMem(X86Register.Rdx, -0x38, 8);
    EmitMovRegMem(X86Register.R8, -0x40, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_memcpy")); EmitDword(0);
    // Null-terminate at len.
    EmitMovRegMem(X86Register.Rax, -0x48, 8);
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitBytes(0xC6, 0x04, 0x08, 0x00);                           // MOV byte [RAX+RCX], 0

    // VirtualFree(src, 0, MEM_RELEASE)
    EmitSystemStackEnter(0x30);
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000);                       // MEM_RELEASE
    EmitCallImport("kernel32.dll", "VirtualFree");
    EmitSystemStackLeave(0x30);

    // result.bufPtr = dest; result.bufLen = len
    EmitMovRegMem(X86Register.Rax, -0x18, 8);                    // result ptr
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitMovIndirectMemReg(X86Register.Rax, rBufOff, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitMovIndirectMemReg(X86Register.Rax, rLenOff, X86Register.Rcx);

    // Zero handle slots so ownership transfer is one-way.
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, hBufOff, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, hLenOff, X86Register.Rcx);
    EmitJmp(doneLabel);

    DefineLabel(emptyLabel);
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitLeaRegSymdataRel(X86Register.Rax, "__rt_empty_cstring");
    EmitMovIndirectMemReg(X86Register.Rdx, rBufOff, X86Register.Rax);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rdx, rLenOff, X86Register.Rax);
    DefineLabel(doneLabel);
  }

  // --------------------------------------------------------------------------
  // Result accessors — straight field reads. The result struct's lifetime
  // is owned by the caller; we don't refcount.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessResultStatusKind() {
    EmitRuntimeFunctionStart("maxon_subprocess_result_status_kind", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rsk_null");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, SubpResOffStatusKind);
    EmitJmp("rt_subp_rsk_done");
    DefineLabel("rt_subp_rsk_null");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    DefineLabel("rt_subp_rsk_done");
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonSubprocessResultStatusCode() {
    EmitRuntimeFunctionStart("maxon_subprocess_result_status_code", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rsc_null");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, SubpResOffStatusCode);
    EmitJmp("rt_subp_rsc_done");
    DefineLabel("rt_subp_rsc_null");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    DefineLabel("rt_subp_rsc_done");
    EmitRuntimeFunctionEnd();
  }

  // The Stdout/Stderr accessors return a fresh mm_raw_alloc'd cstring
  // (cstring_to_managed copies it). The result struct's stored buffer is
  // raw bytes — we must null-terminate before returning. We copy into a
  // new alloc so the caller-side `mm_raw_free(cstring)` invoked by the
  // parser's RuntimeCallToManaged lowering is safe (the original buffer
  // is freed by result_release).
  private void EmitMaxonSubprocessResultStdout() {
    EmitMaxonSubprocessResultStreamCopy(
      "maxon_subprocess_result_stdout",
      SubpResOffStdoutPtr,
      SubpResOffStdoutLen,
      "rt_subp_rso");
  }

  private void EmitMaxonSubprocessResultStderr() {
    EmitMaxonSubprocessResultStreamCopy(
      "maxon_subprocess_result_stderr",
      SubpResOffStderrPtr,
      SubpResOffStderrLen,
      "rt_subp_rse");
  }

  private void EmitMaxonSubprocessResultStreamCopy(string name, int bufOff, int lenOff, string lblPrefix) {
    EmitRuntimeFunctionStart(name, 1, 0x40);
    // [rbp-0x08] result ptr; [rbp-0x10] src buf; [rbp-0x18] len; [rbp-0x20] dest
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", $"{lblPrefix}_empty");
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, bufOff);
    EmitMovMemReg(-0x10, X86Register.Rcx, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, lenOff);
    EmitMovMemReg(-0x18, X86Register.Rcx, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", $"{lblPrefix}_empty");

    // mm_raw_alloc(len + 1)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitAddRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // memcpy(dst, src, len)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegMem(X86Register.R8, -0x18, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_memcpy")); EmitDword(0);

    // Null-terminate at dst[len]
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitBytes(0xC6, 0x04, 0x08, 0x00);                            // MOV byte [RAX+RCX], 0

    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitJmp($"{lblPrefix}_done");

    DefineLabel($"{lblPrefix}_empty");
    // Allocate a fresh 1-byte zero cstring rather than returning the
    // static rdata sentinel. RuntimeCallToManaged-style callers always
    // mm_raw_free the returned pointer, so handing back the rdata
    // sentinel would decrement __mm_raw_alloc_count without a matching
    // alloc and trip the leak detector.
    EmitMovRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitBytes(0xC6, 0x00, 0x00);                                  // MOV byte [RAX], 0

    DefineLabel($"{lblPrefix}_done");
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonSubprocessResultDurationMs() {
    EmitRuntimeFunctionStart("maxon_subprocess_result_duration_ms", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rdm_null");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, SubpResOffDurationMs);
    EmitJmp("rt_subp_rdm_done");
    DefineLabel("rt_subp_rdm_null");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    DefineLabel("rt_subp_rdm_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // result_release(resultPtr) — free the captured cstrings (if non-empty
  // and not the static __rt_empty_cstring sentinel), then free the struct.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessResultRelease() {
    EmitRuntimeFunctionStart("maxon_subprocess_result_release", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_rr_done");
    EmitMovMemReg(-0x10, X86Register.Rax, 8);

    EmitSubprocessResultFreeCstring(SubpResOffStdoutPtr);
    EmitSubprocessResultFreeCstring(SubpResOffStderrPtr);

    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    DefineLabel("rt_subp_rr_done");
    EmitRuntimeFunctionEnd();
  }

  /// Free result->fieldOff if non-zero and not the static empty-cstring
  /// sentinel.
  private void EmitSubprocessResultFreeCstring(int fieldOff) {
    var skipLabel = $"rt_subp_rr_skip_{fieldOff:x2}";
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    // Skip the static sentinel — it lives in rdata and isn't tracked by mm.
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_empty_cstring");
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("e", skipLabel);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel(skipLabel);
  }

  // --------------------------------------------------------------------------
  // Spawn core — shared between subprocess_spawn and subprocess_detach.
  // Receives a fully populated argument bundle (the 14 args saved by the
  // caller); returns either the new handle struct pointer (attached mode)
  // or the pid (detach mode), or -1 on failure. The boolean "returnPid" is
  // baked into the emission via two thin wrappers below.
  // ==========================================================================

  private void EmitMaxonSubprocessSpawnCore() {
    // Stack:
    //   [rbp-0x08]   argv_managed
    //   [rbp-0x10]   argc
    //   [rbp-0x18]   cwd_managed
    //   [rbp-0x20]   env_managed
    //   [rbp-0x28]   envInherit
    //   [rbp-0x30]   stdinKind
    //   [rbp-0x38]   stdinData_managed (bytes payload)
    //   [rbp-0x40]   stdoutKind
    //   [rbp-0x48]   stdoutData_managed (path for file mode)
    //   [rbp-0x50]   stdoutLimit
    //   [rbp-0x58]   stderrKind
    //   [rbp-0x60]   stderrData_managed
    //   [rbp-0x68]   stderrLimit
    //   [rbp-0x70]   flags
    //   --- locals below ---
    //   [rbp-0x78]   handle struct ptr
    //   [rbp-0x80]   cmd_line_wide (utf16 ptr)
    //   [rbp-0x88]   cwd_wide (utf16 ptr, or 0)
    //   [rbp-0x90]   env_wide (utf16 block, or 0)
    //   [rbp-0x98]   STARTUPINFOW base saved value (we reference it by displacement)
    //   [rbp-0xA0]   SECURITY_ATTRIBUTES base displacement helper
    //   [rbp-0xC0]   SECURITY_ATTRIBUTES (24 bytes; pad to 0x20)
    //   [rbp-0x100]  PROCESS_INFORMATION (24 bytes; pad to 0x40)
    //   [rbp-0x180]  STARTUPINFOW (104 bytes; pad to 0x80)
    //   [rbp-0x188]  hStdinReadChild (child's read end of stdin pipe)
    //   [rbp-0x190]  hStdoutWriteChild
    //   [rbp-0x198]  hStderrWriteChild
    //   [rbp-0x1A0]  drainCtxStdout
    //   [rbp-0x1A8]  drainCtxStderr
    //   [rbp-0x1B0]  feedCtxStdin
    //   [rbp-0x1B8]  stdinPayloadCopy (mm_raw_alloc'd duplicate of bytes payload)
    //   [rbp-0x1C0]  stdinPayloadLen
    //   [rbp-0x1C8]  parent's write end of stdin pipe (transferred to struct after alloc)
    //   [rbp-0x1D0]  parent's read end of stdout pipe (transferred to struct after alloc)
    //   [rbp-0x1D8]  parent's read end of stderr pipe (transferred to struct after alloc)
    //   [rbp-0x1E0]  hJob (Job Object owning the spawned process; transferred to struct after alloc)
    //   [rbp-0x260]  JOBOBJECT_EXTENDED_LIMIT_INFORMATION (112 bytes; pad to 0x80)
    const int SlotJobExtLimit = -0x260;
    const int SlotHandleStruct = -0x78;
    const int SlotCmdLineWide = -0x80;
    const int SlotCwdWide = -0x88;
    const int SlotEnvWide = -0x90;
    const int SlotSA = -0xC0;
    const int SlotPI = -0x100;
    const int SlotSI = -0x180;
    const int SlotHStdinReadChild = -0x188;
    const int SlotHStdoutWriteChild = -0x190;
    const int SlotHStderrWriteChild = -0x198;
    const int SlotDrainCtxStdout = -0x1A0;
    const int SlotDrainCtxStderr = -0x1A8;
    const int SlotFeedCtxStdin = -0x1B0;
    const int SlotStdinPayloadCopy = -0x1B8;
    const int SlotStdinPayloadLen = -0x1C0;
    const int SlotHStdinWriteParent = -0x1C8;
    const int SlotHStdoutReadParent = -0x1D0;
    const int SlotHStderrReadParent = -0x1D8;
    const int SlotHJob = -0x1E0;

    // SECURITY_ATTRIBUTES occupies SlotSA..SlotSA+0x18, but we lay it out
    // starting at SlotSA so the base ptr is at SlotSA (24 bytes used).
    // STARTUPINFOW is 104 bytes; we lay it from SlotSI upward.
    // PROCESS_INFORMATION is 24 bytes from SlotPI upward.
    // The padded reservations give us 16-byte alignment.

    EmitRuntimeFunctionStart("__subp_spawn_core", 8, 0x270);

    // The caller (subprocess_spawn / subprocess_detach wrapper) places the
    // 14 source args via the internal CC: args 0..7 in RCX/RDX/R8/R9/RSI/
    // RDI/RAX/RBX, args 8..13 on the stack at [rsp+0..0x28]. Our prologue
    // (argCount=8 above) homes the 8 register args into [rbp-0x08..-0x40].
    // The 6 stack args sit at [rbp+0x10..+0x38] after our prologue's PUSH
    // RBP. Copy them into [rbp-0x48..-0x70] so the body can address all 14
    // through a uniform negative-displacement scheme.
    for (int i = 0; i < 6; i++) {
      EmitMovRegMem(X86Register.Rax, 0x10 + i * 8, 8);
      EmitMovMemReg(-0x48 - i * 8, X86Register.Rax, 8);
    }

    // Zero locals from SlotHandleStruct (-0x78) down to SlotHJob
    // (-0x1E0). REP STOSQ writes upward from RDI, so we start at the
    // lowest address and write (highEnd - lowStart) bytes' worth of qwords.
    // Lowest start = -0x1E0 (inclusive); upper exclusive end = -0x70 (8
    // bytes above -0x78 because -0x78 itself is included). That gives
    // (0x1E0 - 0x70) = 0x170 bytes = 0x2E qwords.
    EmitLeaRegMem(X86Register.Rdi, -0x1E0);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovRegImm(X86Register.Rcx, (0x1E0 - 0x70) / 8);
    EmitBytes(0xF3, 0x48, 0xAB);                                  // REP STOSQ

    // Build the command line in UTF-16 (argv → quoted concatenation).
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);                    // argv_managed
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);                    // argc
    EmitCallRuntimeLabel("__subp_build_cmdline_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail");
    EmitMovMemReg(SlotCmdLineWide, X86Register.Rax, 8);

    // Convert cwd if non-empty.
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitCallRuntimeLabel("__subp_managed_to_wide_or_null");
    EmitMovMemReg(SlotCwdWide, X86Register.Rax, 8);

    // Build env block (or NULL if envInherit).
    EmitMovRegMem(X86Register.Rax, -0x28, 8);                    // envInherit
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_subp_sc_env_null");
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);                    // env_managed
    EmitCallRuntimeLabel("__subp_build_env_wide");
    EmitMovMemReg(SlotEnvWide, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_env_done");
    DefineLabel("rt_subp_sc_env_null");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(SlotEnvWide, X86Register.Rax, 8);
    DefineLabel("rt_subp_sc_env_done");

    // Initialize SECURITY_ATTRIBUTES at SlotSA: nLength=24, lpSD=NULL, bInherit=TRUE.
    EmitMovMemDwordImm(SlotSA, 24);
    EmitMovMemDwordImm(SlotSA + 16, 1);

    // STARTUPINFOW.cb = 104 (size of STARTUPINFOW)
    EmitMovMemDwordImm(SlotSI, 104);
    EmitMovMemDwordImm(SlotSI + 0x3C, 0x100);                     // dwFlags = STARTF_USESTDHANDLES

    // ===== Configure stdin =====
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
    EmitCmpRegImm(X86Register.Rax, StdioKindInherit);
    EmitJcc("e", "rt_subp_sc_stdin_inherit");
    EmitCmpRegImm(X86Register.Rax, StdioKindCollect);             // bytes payload (Subprocess uses kind=collect for stdin bytes? actually kind 2 is collect for stdout/stderr; for stdin we use kind 3 to mean "file" and kind … rethink)
    // For stdin: kind 2 means "bytes". (Subprocess.maxon: collect is the
    // OUTPUT side; for stdin we use the stdinKind int 2 for "bytes".)
    EmitJcc("e", "rt_subp_sc_stdin_bytes");
    EmitCmpRegImm(X86Register.Rax, StdioKindFile);
    EmitJcc("e", "rt_subp_sc_stdin_file");
    // none / discard: redirect to NUL
    EmitMovRegImm(X86Register.Rcx, 0x80000000u);                  // GENERIC_READ
    EmitCallRuntimeLabel("__subp_open_nul_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail");
    EmitMovMemReg(SlotSI + 0x50, X86Register.Rax, 8);             // hStdInput
    EmitJmp("rt_subp_sc_stdin_done");

    DefineLabel("rt_subp_sc_stdin_inherit");
    EmitMovRegImm(X86Register.Rcx, unchecked((long)-10));         // STD_INPUT_HANDLE
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(SlotSI + 0x50, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stdin_done");

    DefineLabel("rt_subp_sc_stdin_bytes");
    // CreatePipe(&rChild, &wParent, &SA, 0). SA has bInherit=true, so both
    // ends start inheritable; we then clear the parent-side flag.
    EmitLeaRegMem(X86Register.Rcx, SlotHStdinReadChild);
    EmitLeaRegMem(X86Register.Rdx, SlotHStdinWriteParent);
    EmitLeaRegMem(X86Register.R8, SlotSA);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImportOnSystemStack("kernel32.dll", "CreatePipe");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_pipe");
    EmitMovRegMem(X86Register.Rcx, SlotHStdinWriteParent, 8);
    EmitMovRegImm(X86Register.Rdx, 1);                            // HANDLE_FLAG_INHERIT mask
    EmitXorRegReg(X86Register.R8, X86Register.R8);                // clear it
    EmitCallImportOnSystemStack("kernel32.dll", "SetHandleInformation");
    // STARTUPINFOW.hStdInput = child's read end
    EmitMovRegMem(X86Register.Rax, SlotHStdinReadChild, 8);
    EmitMovMemReg(SlotSI + 0x50, X86Register.Rax, 8);

    // Copy stdin payload (managed bytes at arg -0x38) into a fresh
    // mm_raw_alloc'd buffer so the feed thread outlives the caller's
    // managed lifetime. MaxonCallRuntimeOp lowering hands us the
    // __ManagedMemory's BUFFER POINTER (not the struct), so we use
    // maxon_strlen to compute the byte count from the null-terminated
    // cstring. This matches what __subp_managed_to_wide_or_null does
    // and is consistent with String literals (which always emit a
    // trailing null in their rdata backing buffer).
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_stdin_payload_empty");
    EmitBytes(0x0F, 0xB6, 0x08);                                  // movzx ecx, byte [rax]
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_stdin_payload_empty");
    EmitMovMemReg(SlotStdinPayloadCopy, X86Register.Rax, 8);     // stash source buffer ptr
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_strlen")); EmitDword(0);
    EmitMovMemReg(SlotStdinPayloadLen, X86Register.Rax, 8);
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);             // RCX = length
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_pipe_buffer");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitMovRegMem(X86Register.Rcx, SlotStdinPayloadCopy, 8);     // recover stashed source
    EmitMovMemReg(SlotStdinPayloadCopy, X86Register.Rax, 8);     // overwrite with destination
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx);             // RDX = source
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);             // RCX = dest
    EmitMovRegMem(X86Register.R8, SlotStdinPayloadLen, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_memcpy")); EmitDword(0);
    EmitJmp("rt_subp_sc_stdin_done");

    DefineLabel("rt_subp_sc_stdin_payload_empty");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(SlotStdinPayloadLen, X86Register.Rax, 8);
    EmitMovMemReg(SlotStdinPayloadCopy, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stdin_done");

    DefineLabel("rt_subp_sc_stdin_file");
    EmitMovRegMem(X86Register.Rcx, -0x38, 8);                    // managed path
    EmitMovRegImm(X86Register.Rdx, unchecked((long)0x80000000L)); // GENERIC_READ
    EmitMovRegImm(X86Register.R8, 3);                             // OPEN_EXISTING
    EmitCallRuntimeLabel("__subp_open_file_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_file");
    EmitMovMemReg(SlotSI + 0x50, X86Register.Rax, 8);

    DefineLabel("rt_subp_sc_stdin_done");

    // ===== Configure stdout =====
    EmitMovRegMem(X86Register.Rax, -0x40, 8);                    // stdoutKind
    EmitCmpRegImm(X86Register.Rax, StdioKindInherit);
    EmitJcc("e", "rt_subp_sc_stdout_inherit");
    EmitCmpRegImm(X86Register.Rax, StdioKindCollect);
    EmitJcc("e", "rt_subp_sc_stdout_collect");
    EmitCmpRegImm(X86Register.Rax, StdioKindFile);
    EmitJcc("e", "rt_subp_sc_stdout_file");
    // discard → NUL
    EmitMovRegImm(X86Register.Rcx, 0x40000000);                   // GENERIC_WRITE
    EmitCallRuntimeLabel("__subp_open_nul_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail");
    EmitMovMemReg(SlotSI + 0x58, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stdout_done");

    DefineLabel("rt_subp_sc_stdout_inherit");
    EmitMovRegImm(X86Register.Rcx, unchecked((long)-11));         // STD_OUTPUT_HANDLE
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(SlotSI + 0x58, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stdout_done");

    DefineLabel("rt_subp_sc_stdout_collect");
    // CreatePipe(&rParent, &wChild, &SA, 0). Child inherits the write end;
    // we strip inheritance off the parent's read end.
    EmitLeaRegMem(X86Register.Rcx, SlotHStdoutReadParent);
    EmitLeaRegMem(X86Register.Rdx, SlotHStdoutWriteChild);
    EmitLeaRegMem(X86Register.R8, SlotSA);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImportOnSystemStack("kernel32.dll", "CreatePipe");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_pipe");
    EmitMovRegMem(X86Register.Rcx, SlotHStdoutReadParent, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitCallImportOnSystemStack("kernel32.dll", "SetHandleInformation");
    EmitMovRegMem(X86Register.Rax, SlotHStdoutWriteChild, 8);
    EmitMovMemReg(SlotSI + 0x58, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stdout_done");

    DefineLabel("rt_subp_sc_stdout_file");
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitMovRegImm(X86Register.Rdx, 0x40000000);                   // GENERIC_WRITE
    EmitMovRegImm(X86Register.R8, 2);                             // CREATE_ALWAYS
    EmitCallRuntimeLabel("__subp_open_file_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_file");
    EmitMovMemReg(SlotSI + 0x58, X86Register.Rax, 8);

    DefineLabel("rt_subp_sc_stdout_done");

    // ===== Configure stderr =====
    EmitMovRegMem(X86Register.Rax, -0x58, 8);                    // stderrKind
    EmitCmpRegImm(X86Register.Rax, StdioKindInherit);
    EmitJcc("e", "rt_subp_sc_stderr_inherit");
    EmitCmpRegImm(X86Register.Rax, StdioKindCollect);
    EmitJcc("e", "rt_subp_sc_stderr_collect");
    EmitCmpRegImm(X86Register.Rax, StdioKindFile);
    EmitJcc("e", "rt_subp_sc_stderr_file");
    EmitMovRegImm(X86Register.Rcx, 0x40000000);
    EmitCallRuntimeLabel("__subp_open_nul_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail");
    EmitMovMemReg(SlotSI + 0x60, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stderr_done");

    DefineLabel("rt_subp_sc_stderr_inherit");
    EmitMovRegImm(X86Register.Rcx, unchecked((long)-12));         // STD_ERROR_HANDLE
    EmitCallImportOnSystemStack("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(SlotSI + 0x60, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stderr_done");

    DefineLabel("rt_subp_sc_stderr_collect");
    EmitLeaRegMem(X86Register.Rcx, SlotHStderrReadParent);
    EmitLeaRegMem(X86Register.Rdx, SlotHStderrWriteChild);
    EmitLeaRegMem(X86Register.R8, SlotSA);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImportOnSystemStack("kernel32.dll", "CreatePipe");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_pipe");
    EmitMovRegMem(X86Register.Rcx, SlotHStderrReadParent, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitCallImportOnSystemStack("kernel32.dll", "SetHandleInformation");
    EmitMovRegMem(X86Register.Rax, SlotHStderrWriteChild, 8);
    EmitMovMemReg(SlotSI + 0x60, X86Register.Rax, 8);
    EmitJmp("rt_subp_sc_stderr_done");

    DefineLabel("rt_subp_sc_stderr_file");
    EmitMovRegMem(X86Register.Rcx, -0x60, 8);
    EmitMovRegImm(X86Register.Rdx, 0x40000000);
    EmitMovRegImm(X86Register.R8, 2);
    EmitCallRuntimeLabel("__subp_open_file_wide");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_file");
    EmitMovMemReg(SlotSI + 0x60, X86Register.Rax, 8);

    DefineLabel("rt_subp_sc_stderr_done");

    // If hideWindow is set, switch dwFlags to STARTF_USESTDHANDLES |
    // STARTF_USESHOWWINDOW and stamp wShowWindow = SW_HIDE (0). The default
    // STARTF_USESTDHANDLES value (0x100) was set during STARTUPINFOW init.
    EmitMovRegMem(X86Register.Rax, -0x70, 8);                    // flags
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitAndRegImm(X86Register.Rcx, SpawnFlagHideWindow);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_show_done");
    EmitMovMemDwordImm(SlotSI + 0x3C, 0x101);                     // STARTF_USESTDHANDLES | STARTF_USESHOWWINDOW
    EmitMovMemWordImm(SlotSI + 0x48, 0);                          // wShowWindow = SW_HIDE
    DefineLabel("rt_subp_sc_show_done");

    // ===== Create Job Object =====
    // The job is created before CreateProcessW so that we can assign the
    // child to it during its CREATE_SUSPENDED window. We set
    // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so close-on-release acts as a
    // backstop kill of the tree; timeout-driven kills call
    // TerminateJobObject directly. Job creation failure is non-fatal —
    // we keep going without a job and lose the tree-kill guarantee, but
    // the single-process kill still works.
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);             // lpJobAttributes = NULL
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);             // lpName = NULL
    EmitCallImportOnSystemStack("kernel32.dll", "CreateJobObjectW");
    EmitMovMemReg(SlotHJob, X86Register.Rax, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_job_done");

    // Zero out the JOBOBJECT_EXTENDED_LIMIT_INFORMATION (112 bytes / 14 qwords)
    // and set BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
    // (0x2000) at offset 0x10.
    EmitLeaRegMem(X86Register.Rdi, SlotJobExtLimit);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovRegImm(X86Register.Rcx, 14);                          // 112 / 8 qwords
    EmitBytes(0xF3, 0x48, 0xAB);                                  // REP STOSQ
    EmitMovMemDwordImm(SlotJobExtLimit + 0x10, 0x2000);

    EmitMovRegMem(X86Register.Rcx, SlotHJob, 8);
    EmitMovRegImm(X86Register.Rdx, 9);                           // JobObjectExtendedLimitInformation
    EmitLeaRegMem(X86Register.R8, SlotJobExtLimit);
    EmitMovRegImm(X86Register.R9, 112);
    EmitCallImportOnSystemStack("kernel32.dll", "SetInformationJobObject");
    // SetInformationJobObject failure is non-fatal too — the job still exists
    // and TerminateJobObject still works, we just don't get auto-kill on close.
    DefineLabel("rt_subp_sc_job_done");

    // ===== CreateProcessW =====
    // CRITICAL ORDERING: compute the stack args FIRST (which may clobber
    // RCX/RDX/R8/R9 during the dwCreationFlags branching), then load the
    // four register args (lpApplicationName/lpCommandLine/lpProcessAttributes/
    // lpThreadAttributes) last so they survive to the call. Loading
    // register args before the flags computation was the original bug:
    // the flags-build sequence used RCX (load flags from [rbp-0x70])
    // and RDX (scratch for AND masks), wiping out the cmdline pointer
    // and yielding CreateProcessW failures with ERROR_INVALID_PARAMETER.
    EmitSystemStackEnter(0x50);

    // arg5 (qword [rsp+0x20]): bInheritHandles = TRUE
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x01, 0x00, 0x00, 0x00);

    // arg6 (qword [rsp+0x28]): dwCreationFlags
    //   bit 0 → CREATE_NO_WINDOW (0x08000000) — only if hideWindow
    //   bit 1 → CREATE_NEW_PROCESS_GROUP (0x200)
    //   bit 2 → DETACHED_PROCESS (0x08) | CREATE_NEW_PROCESS_GROUP
    //   Always set CREATE_SUSPENDED (0x4) so we can assign the child to
    //   the Job Object before its main thread runs; ResumeThread fires
    //   the child after AssignProcessToJobObject. Without this, any
    //   descendants the child creates before the assignment escape the
    //   job and survive TerminateJobObject.
    // The flag-source word lives at [rbp-0x70]; we reload it for each
    // bit-test rather than holding it in a register because the four CC
    // arg registers (RCX/RDX/R8/R9) get loaded last and R10/R11 may be
    // used by EmitSystemStackLeave to switch back to the GT stack.
    EmitMovRegImm(X86Register.Rax, 0x4);                         // CREATE_SUSPENDED
    EmitMovRegMem(X86Register.Rcx, -0x70, 8);
    EmitAndRegImm(X86Register.Rcx, SpawnFlagHideWindow);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_no_hide");
    EmitOrRegImm(X86Register.Rax, 0x08000000);
    DefineLabel("rt_subp_sc_no_hide");
    EmitMovRegMem(X86Register.Rcx, -0x70, 8);
    EmitAndRegImm(X86Register.Rcx, SpawnFlagNewGroup);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_no_group");
    EmitOrRegImm(X86Register.Rax, 0x200);
    DefineLabel("rt_subp_sc_no_group");
    EmitMovRegMem(X86Register.Rcx, -0x70, 8);
    EmitAndRegImm(X86Register.Rcx, SpawnFlagDetach);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_no_detach");
    EmitOrRegImm(X86Register.Rax, 0x08 | 0x200);
    DefineLabel("rt_subp_sc_no_detach");
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x28);                      // [rsp+0x28] = creation flags

    // arg7 (qword [rsp+0x30]): lpEnvironment
    EmitMovRegMem(X86Register.Rax, SlotEnvWide, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x30);
    // arg8 (qword [rsp+0x38]): lpCurrentDirectory
    EmitMovRegMem(X86Register.Rax, SlotCwdWide, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x38);
    // arg9 (qword [rsp+0x40]): lpStartupInfo
    EmitLeaRegMem(X86Register.Rax, SlotSI);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x40);
    // arg10 (qword [rsp+0x48]): lpProcessInformation
    EmitLeaRegMem(X86Register.Rax, SlotPI);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x48);

    // Now load the four register args last so they aren't clobbered.
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);             // lpApplicationName = NULL
    EmitMovRegMem(X86Register.Rdx, SlotCmdLineWide, 8);          // lpCommandLine
    EmitXorRegReg(X86Register.R8, X86Register.R8);               // lpProcessAttributes = NULL
    EmitXorRegReg(X86Register.R9, X86Register.R9);               // lpThreadAttributes = NULL

    EmitCallImport("kernel32.dll", "CreateProcessW");
    EmitSystemStackLeave(0x50);




    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_subp_sc_cp_ok");
    // CreateProcessW failed. Render GetLastError() into the global error buffer
    // via FormatMessageA so callers see a system-meaningful diagnostic
    // ("The system cannot find the file specified", "The parameter is
    // incorrect", etc.) instead of a generic "CreateProcessW failed".
    EmitCallImportOnSystemStack("kernel32.dll", "GetLastError");
    EmitMovRegReg(X86Register.Rbx, X86Register.Rax);                 // RBX = err code

    EmitSystemStackEnter(0x50);
    // FormatMessageA(FORMAT_MESSAGE_FROM_SYSTEM|FORMAT_MESSAGE_IGNORE_INSERTS,
    //                NULL, errCode, 0, buf, bufSize, NULL)
    EmitMovRegImm(X86Register.Rcx, 0x1200);                          // FROM_SYSTEM|IGNORE_INSERTS
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegReg(X86Register.R8, X86Register.Rbx);                  // err code
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitGlobalLeaReg(X86Register.Rax, "__subp_last_error_buf");
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);                         // [rsp+0x20] = buf
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x02, 0x00, 0x00); // [rsp+0x28] = 512
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // [rsp+0x30] = NULL
    EmitCallImport("kernel32.dll", "FormatMessageA");
    EmitSystemStackLeave(0x50);
    // Trim trailing \r\n that FormatMessageA appends. RAX = wchars-written
    // (actually byte count for FormatMessageA), or 0 on failure.
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fmtmsg_done");
    EmitGlobalLeaReg(X86Register.Rcx, "__subp_last_error_buf");
    DefineLabel("rt_subp_sc_fmtmsg_trim_loop");
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_subp_sc_fmtmsg_done");
    EmitSubRegImm(X86Register.Rax, 1);
    EmitBytes(0x0F, 0xB6, 0x14, 0x01);                                // MOVZX EDX, byte [RCX+RAX]
    EmitCmpRegImm(X86Register.Rdx, 0x20);                             // <= space → trim
    EmitJcc("g", "rt_subp_sc_fmtmsg_done_inc");
    EmitBytes(0xC6, 0x04, 0x01, 0x00);                                // MOV byte [RCX+RAX], 0
    EmitJmp("rt_subp_sc_fmtmsg_trim_loop");
    DefineLabel("rt_subp_sc_fmtmsg_done_inc");
    DefineLabel("rt_subp_sc_fmtmsg_done");
    EmitJmp("rt_subp_sc_fail_create");
    DefineLabel("rt_subp_sc_cp_ok");

    // CreateProcessW succeeded. Close the child-side handles we no longer
    // need (the child has its own copies); leave parent-side handles open.
    EmitCloseSlotIfNonZero(SlotHStdinReadChild);
    EmitCloseSlotIfNonZero(SlotHStdoutWriteChild);
    EmitCloseSlotIfNonZero(SlotHStderrWriteChild);

    // ===== Allocate handle struct =====
    EmitMovRegImm(X86Register.Rcx, SubpHandleStructSize);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_capture_result");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovMemReg(SlotHandleStruct, X86Register.Rax, 8);
    // Zero handle struct
    EmitMovRegReg(X86Register.Rdi, X86Register.Rax);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovRegImm(X86Register.Rcx, SubpHandleStructSize / 8);
    EmitBytes(0xF3, 0x48, 0xAB);

    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    // Populate. hProcess at PI+0x00, hThread at PI+0x08, dwProcessId at PI+0x10, dwThreadId at PI+0x14.
    EmitMovRegMem(X86Register.Rax, SlotPI + 0x00, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHProcess, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, SlotPI + 0x10, 4);             // pid (DWORD)
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffPid, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, SlotHJob, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHJob, X86Register.Rax);
    // Move ownership of hJob into the struct so the unwind path doesn't
    // double-close it on the failure branch below.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(SlotHJob, X86Register.Rax, 8);

    // AssignProcessToJobObject(hJob, hProcess). The child is CREATE_SUSPENDED
    // so it cannot have spawned anything yet; assigning now guarantees every
    // descendant lives inside the job. If we have no job (CreateJobObjectW
    // failed earlier), skip — TerminateProcess on the single child is the
    // best we can do.
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdi, SubpOffHJob);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_skip_assign");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rdi, SubpOffHProcess);
    EmitCallImportOnSystemStack("kernel32.dll", "AssignProcessToJobObject");
    // AssignProcessToJobObject failure is non-fatal: the child is alive and
    // we still have its handle. We just lose the tree-kill guarantee.
    DefineLabel("rt_subp_sc_skip_assign");

    // Resume the main thread so the child actually starts running. We had
    // to keep it suspended through job assignment.
    EmitMovRegMem(X86Register.Rcx, SlotPI + 0x08, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "ResumeThread");

    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    // Close the thread handle now — we don't expose thread-level ops.
    EmitMovRegMem(X86Register.Rcx, SlotPI + 0x08, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");

    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    // Flags + limits + start time.
    EmitMovRegMem(X86Register.Rax, -0x70, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffFlags, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, -0x50, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffStdoutLimit, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, -0x68, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffStderrLimit, X86Register.Rax);
    EmitCallRuntimeLabel("maxon_current_time_ms");
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffStartTime, X86Register.Rax);

    // Transfer parent-side pipe handles from the spawn-core's local slots
    // into the struct so the drain/feed thread setup below can read them
    // back via SubpOffH*Read/Write, and so release_handle can close them.
    EmitMovRegMem(X86Register.Rax, SlotHStdoutReadParent, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStdoutRead, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, SlotHStderrReadParent, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStderrRead, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, SlotHStdinWriteParent, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStdinWrite, X86Register.Rax);
    // Zero the local slots so the unwind path doesn't double-close.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(SlotHStdoutReadParent, X86Register.Rax, 8);
    EmitMovMemReg(SlotHStderrReadParent, X86Register.Rax, 8);
    EmitMovMemReg(SlotHStdinWriteParent, X86Register.Rax, 8);

    // ===== Spawn drain threads for collect streams =====
    // The parent read ends are now in the handle struct (transferred from
    // SlotH*ReadParent above). Each drain thread gets a DrainCtx that
    // tells it which pipe to read and where to write the resulting buffer.
    EmitMovRegMem(X86Register.Rax, -0x40, 8);                    // stdoutKind
    EmitCmpRegImm(X86Register.Rax, StdioKindCollect);
    EmitJcc("ne", "rt_subp_sc_stdout_drain_done");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdi, SubpOffHStdoutRead);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_stdout_drain_done");
    // Allocate DrainCtx
    EmitMovRegImm(X86Register.Rcx, DrainCtxSize);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_pipe_buffer");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovMemReg(SlotDrainCtxStdout, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffDrainCtxStdout, X86Register.Rax);
    // ctx->hRead = handle->hStdoutRead
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rdi, SubpOffHStdoutRead);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffHRead, X86Register.Rcx);
    // ctx->bufPtrOut = &handle->stdoutBufPtr
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitAddRegImm(X86Register.Rcx, SubpOffStdoutBufPtr);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffBufPtrOut, X86Register.Rcx);
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitAddRegImm(X86Register.Rcx, SubpOffStdoutBufLen);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffBufLenOut, X86Register.Rcx);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rdi, SubpOffStdoutLimit);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffLimit, X86Register.Rcx);
    // CreateThread(NULL, 0, __subp_drain_thread, ctx, 0, NULL)
    EmitSystemStackEnter(0x40);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitLeaFuncAddr(X86Register.R8, "__subp_drain_thread");
    EmitMovRegMem(X86Register.R9, SlotDrainCtxStdout, 8);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "CreateThread");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStdoutDrain, X86Register.Rax);
    DefineLabel("rt_subp_sc_stdout_drain_done");

    // stderr drain
    EmitMovRegMem(X86Register.Rax, -0x58, 8);
    EmitCmpRegImm(X86Register.Rax, StdioKindCollect);
    EmitJcc("ne", "rt_subp_sc_stderr_drain_done");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdi, SubpOffHStderrRead);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_stderr_drain_done");
    EmitMovRegImm(X86Register.Rcx, DrainCtxSize);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_pipe_buffer");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovMemReg(SlotDrainCtxStderr, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffDrainCtxStderr, X86Register.Rax);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rdi, SubpOffHStderrRead);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffHRead, X86Register.Rcx);
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitAddRegImm(X86Register.Rcx, SubpOffStderrBufPtr);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffBufPtrOut, X86Register.Rcx);
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitAddRegImm(X86Register.Rcx, SubpOffStderrBufLen);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffBufLenOut, X86Register.Rcx);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rdi, SubpOffStderrLimit);
    EmitMovIndirectMemReg(X86Register.Rax, DrainCtxOffLimit, X86Register.Rcx);
    EmitSystemStackEnter(0x40);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitLeaFuncAddr(X86Register.R8, "__subp_drain_thread");
    EmitMovRegMem(X86Register.R9, SlotDrainCtxStderr, 8);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "CreateThread");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStderrDrain, X86Register.Rax);
    DefineLabel("rt_subp_sc_stderr_drain_done");

    // stdin feed thread (if payload non-empty)
    EmitMovRegMem(X86Register.Rax, SlotStdinPayloadLen, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_stdin_feed_done");
    EmitMovRegImm(X86Register.Rcx, FeedCtxSize);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_pipe_buffer");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovMemReg(SlotFeedCtxStdin, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffFeedCtxStdin, X86Register.Rax);
    // Parent write end was transferred into the struct at SubpOffHStdinWrite
    // right after CreateProcessW returned. Read it back into the feed ctx.
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rdi, SubpOffHStdinWrite);
    EmitMovRegMem(X86Register.Rax, SlotFeedCtxStdin, 8);
    EmitMovIndirectMemReg(X86Register.Rax, FeedCtxOffHWrite, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rcx, SlotStdinPayloadCopy, 8);
    EmitMovIndirectMemReg(X86Register.Rax, FeedCtxOffDataPtr, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rcx, SlotStdinPayloadLen, 8);
    EmitMovIndirectMemReg(X86Register.Rax, FeedCtxOffDataLen, X86Register.Rcx);
    // Mirror payload into the struct so release_handle can free it on
    // detached spawns (where wait_collect never runs).
    EmitMovRegMem(X86Register.Rcx, SlotStdinPayloadCopy, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffStdinPayloadPtr, X86Register.Rcx);
    EmitMovRegMem(X86Register.Rcx, SlotStdinPayloadLen, 8);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffStdinPayloadLen, X86Register.Rcx);

    EmitSystemStackEnter(0x40);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitLeaFuncAddr(X86Register.R8, "__subp_feed_thread");
    EmitMovRegMem(X86Register.R9, SlotFeedCtxStdin, 8);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "CreateThread");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_sc_fail_after_create");
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStdinFeed, X86Register.Rax);
    // Parent ownership of write end transferred to feed thread (which
    // CloseHandle's it). Zero our slot so release_handle doesn't double-close.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rdi, SubpOffHStdinWrite, X86Register.Rax);
    DefineLabel("rt_subp_sc_stdin_feed_done");

    // ===== Free intermediate wide buffers =====
    EmitFreeWideOrSkip(SlotCmdLineWide);
    EmitFreeWideOrSkip(SlotCwdWide);
    EmitFreeWideOrSkip(SlotEnvWide);

    EmitMovRegMem(X86Register.Rax, SlotHandleStruct, 8);
    EmitJmp("rt_subp_sc_done");

    // ----- Failure paths -----
    DefineLabel("rt_subp_sc_fail_create");
    // FormatMessageA path above filled __subp_last_error_buf with the OS error
    // message. If it failed (zero length), fall back to the static label so
    // the caller still gets something readable.
    EmitGlobalLeaReg(X86Register.Rax, "__subp_last_error_buf");
    EmitBytes(0x0F, 0xB6, 0x08);                                  // movzx ecx, byte [rax]
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("nz", "rt_subp_sc_fail_create_have_msg");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_create_process");
    DefineLabel("rt_subp_sc_fail_create_have_msg");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitJmp("rt_subp_sc_unwind");
    DefineLabel("rt_subp_sc_fail_pipe");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_create_pipe");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitJmp("rt_subp_sc_unwind");
    DefineLabel("rt_subp_sc_fail_file");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_create_file");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitJmp("rt_subp_sc_unwind");
    DefineLabel("rt_subp_sc_fail_after_create");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_alloc");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    // Kill + close process/thread we already created. The process was
    // CREATE_SUSPENDED and may have been assigned to the job already, so
    // either TerminateProcess(hProcess) or closing hJob (with
    // KILL_ON_JOB_CLOSE) would reap it; we do both to keep this branch
    // robust against partial setup states (e.g. job creation failed
    // earlier so SlotHJob is zero).
    EmitMovRegMem(X86Register.Rcx, SlotPI + 0x00, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_fail_skip_p");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitCallImportOnSystemStack("kernel32.dll", "TerminateProcess");
    EmitMovRegMem(X86Register.Rcx, SlotPI + 0x00, 8);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    DefineLabel("rt_subp_sc_fail_skip_p");
    EmitMovRegMem(X86Register.Rcx, SlotPI + 0x08, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_sc_fail_skip_t");
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    DefineLabel("rt_subp_sc_fail_skip_t");
    EmitJmp("rt_subp_sc_unwind");

    DefineLabel("rt_subp_sc_fail");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_unsupported");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");

    DefineLabel("rt_subp_sc_unwind");
    // Close any handles we already opened. The child-side handles are in
    // SlotHStdin*/SlotHStdoutWrite/SlotHStderrWrite; the parent-side reads
    // may be in struct fields if the struct was alloc'd. Close them all
    // best-effort.
    EmitCloseSlotIfNonZero(SlotHStdinReadChild);
    EmitCloseSlotIfNonZero(SlotHStdoutWriteChild);
    EmitCloseSlotIfNonZero(SlotHStderrWriteChild);
    // hJob is either still in SlotHJob (if we never reached the struct
    // populate step) or moved into the struct's SubpOffHJob (if we did).
    // Close whichever holds it; closing a job triggers KILL_ON_JOB_CLOSE
    // which terminates any descendants — important if CreateProcessW
    // succeeded but we then failed allocating the handle struct.
    EmitCloseSlotIfNonZero(SlotHJob);
    EmitMovRegMem(X86Register.Rdi, SlotHandleStruct, 8);
    EmitTestRegReg(X86Register.Rdi, X86Register.Rdi);
    EmitJcc("z", "rt_subp_sc_unwind_no_struct");
    EmitSubprocessCloseStructHandleField(SubpOffHStdoutRead);
    EmitSubprocessCloseStructHandleField(SubpOffHStderrRead);
    EmitSubprocessCloseStructHandleField(SubpOffHStdinWrite);
    EmitSubprocessCloseStructHandleField(SubpOffHJob);
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdi);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    DefineLabel("rt_subp_sc_unwind_no_struct");
    EmitFreeWideOrSkip(SlotCmdLineWide);
    EmitFreeWideOrSkip(SlotCwdWide);
    EmitFreeWideOrSkip(SlotEnvWide);
    EmitFreeSlotIfNonZero(SlotStdinPayloadCopy);
    EmitFreeSlotIfNonZero(SlotDrainCtxStdout);
    EmitFreeSlotIfNonZero(SlotDrainCtxStderr);
    EmitFreeSlotIfNonZero(SlotFeedCtxStdin);
    EmitMovRegImm(X86Register.Rax, -1);

    DefineLabel("rt_subp_sc_done");
    EmitRuntimeFunctionEnd();
  }

  private int _subpCloseSlotCounter;

  /// CloseHandle the qword at [rbp+slot] if non-zero. Used in spawn-core
  /// where SlotHand* live below the regular runtime stack frame. Each call
  /// site needs a unique skip label — the success-path "close child handles"
  /// block and the unwind cleanup block both close the same slots, so a
  /// slot-only key would collide and silently redirect later JZ jumps to
  /// the wrong cleanup section.
  private void EmitCloseSlotIfNonZero(int slot) {
    var skipLabel = $"rt_subp_close_slot_{Math.Abs(slot):x4}_{_subpCloseSlotCounter++}";
    EmitMovRegMem(X86Register.Rcx, slot, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovMemReg(slot, X86Register.Rcx, 8);
    DefineLabel(skipLabel);
  }

  private int _subpFreeSlotCounter;

  private void EmitFreeSlotIfNonZero(int slot) {
    // Counter-suffixed label so callers can invoke this multiple times for
    // the same slot (e.g. once on the success exit and once on the unwind
    // path) without colliding. `DefineLabel` overwrites the second time,
    // which previously caused success-path JZ jumps for NULL slots to jump
    // into the unwind cleanup instead of falling through.
    var skipLabel = $"rt_subp_free_slot_{Math.Abs(slot):x4}_{_subpFreeSlotCounter++}";
    EmitMovRegMem(X86Register.Rcx, slot, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovMemReg(slot, X86Register.Rcx, 8);
    DefineLabel(skipLabel);
  }

  private void EmitFreeWideOrSkip(int slot) {
    EmitFreeSlotIfNonZero(slot);
  }

  private int _subpCloseStructCounter;

  /// CloseHandle on the field at [Rdi+fieldOff] if non-zero (Rdi = struct
  /// base). Counter-suffixed label keeps multiple call sites disambiguated
  /// — release_handle and the spawn-core unwind path both close the same
  /// fields, so a fieldOff-only key would collide.
  private void EmitSubprocessCloseStructHandleField(int fieldOff) {
    var skipLabel = $"rt_subp_close_struct_{fieldOff:x2}_{_subpCloseStructCounter++}";
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rdi, fieldOff);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", skipLabel);
    EmitCallImportOnSystemStack("kernel32.dll", "CloseHandle");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rdi, fieldOff, X86Register.Rcx);
    DefineLabel(skipLabel);
  }

  // --------------------------------------------------------------------------
  // subprocess_spawn(14 args) — copies args into a fresh frame matching
  // __subp_spawn_core's layout, then calls the core. Returns the handle
  // struct pointer (or -1 on failure).
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessSpawn() {
    // Internal CC homes the first 8 args at [rbp-0x08]..[rbp-0x40] via
    // EmitRuntimeFunctionStart; the remaining 6 sit at [rbp+0x10]..[rbp+0x38].
    // We forward all 14 to __subp_spawn_core by setting the core's frame
    // slots before calling. Easier: pass them as actual function args to
    // the core. Even easier: spawn_core re-reads from the same slot offsets
    // as the caller, so we just need to forward arg 0..7 in registers and
    // arg 8..13 on the stack. But the runtime call convention uses the
    // internal 8-register CC, which means args 0..7 go in RCX, RDX, R8, R9,
    // RSI, RDI, RAX, RBX. The core then homes them at [rbp-0x08]..[rbp-0x40].
    // Stack args 8..13 are written before the call by the caller.
    //
    // Simplest implementation: spawn is a tail-call to the core. We just
    // re-emit the body of spawn into spawn_core directly. To avoid
    // duplicating, we make spawn a wrapper that fetches incoming args and
    // re-calls the core with the same 14-arg internal CC.
    //
    // Since the caller of `subprocessSpawn` already arranged the args per
    // the internal CC (registers + stack), spawn can just `JMP` to the
    // core. But the core uses its own EmitRuntimeFunctionStart which
    // assumes argCount=0 and doesn't home its incoming registers — we homed
    // them ourselves manually with the same slot layout. To enable the
    // shortcut, spawn's body must materialise the 14 args at the slot
    // offsets the core expects.

    EmitRuntimeFunctionStart("maxon_subprocess_spawn", 8, 0x80);
    // Args 0..7 are now at [rbp-0x08]..[rbp-0x40]. Args 8..13 are at
    // [rbp+0x10]..[rbp+0x38] from the caller.

    // Allocate space for forwarding args 8..13 and tail-call the core.
    // Build the core's 14-slot frame on our stack by calling the core
    // with the 8-register CC and pushing the 6 stack args.

    // Forward 6 stack args 8..13 to the core. The caller's stack args sit
    // at [rbp+0x10..+0x38] (above the saved RBP); we copy them to
    // [rsp+0..0x28] so the core sees them at the same convention.
    EmitSubRegImm(X86Register.Rsp, 0x30);
    for (int i = 0; i < 6; i++) {
      EmitMovRegMem(X86Register.Rax, 0x10 + i * 8, 8);
      EmitMovMemRspReg(i * 8, X86Register.Rax);
    }
    // Args 0..7 already in slots; they're in memory, not in registers
    // anymore (EmitRuntimeFunctionStart homed them). Reload into the CC
    // registers expected by the core.
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegMem(X86Register.R8, -0x18, 8);
    EmitMovRegMem(X86Register.R9, -0x20, 8);
    EmitMovRegMem(X86Register.Rsi, -0x28, 8);
    EmitMovRegMem(X86Register.Rdi, -0x30, 8);
    // The internal CC's 7th/8th regs are RAX and RBX. Load them just before
    // the call.
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitMovRegMem(X86Register.Rbx, -0x40, 8);
    EmitCallRuntimeLabel("__subp_spawn_core");
    EmitAddRegImm(X86Register.Rsp, 0x30);

    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // subprocess_detach — same as spawn but the caller-visible result is the
  // pid (or -1) instead of the handle struct pointer. The flags bit 2 has
  // been OR'd in by the stdlib already; we still spawn through the same
  // core, then release the handle and return the pid.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessDetach() {
    EmitRuntimeFunctionStart("maxon_subprocess_detach", 8, 0x80);
    EmitSubRegImm(X86Register.Rsp, 0x30);
    for (int i = 0; i < 6; i++) {
      EmitMovRegMem(X86Register.Rax, 0x30 + 0x10 + i * 8, 8);
      EmitMovMemRspReg(i * 8, X86Register.Rax);
    }
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegMem(X86Register.R8, -0x18, 8);
    EmitMovRegMem(X86Register.R9, -0x20, 8);
    EmitMovRegMem(X86Register.Rsi, -0x28, 8);
    EmitMovRegMem(X86Register.Rdi, -0x30, 8);
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
    EmitMovRegMem(X86Register.Rbx, -0x40, 8);
    EmitCallRuntimeLabel("__subp_spawn_core");
    EmitAddRegImm(X86Register.Rsp, 0x30);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);                    // handle ptr
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("s", "rt_subp_detach_err");                          // -1
    // Read pid, release handle, return pid
    EmitMovRegIndirectMem(X86Register.Rbx, X86Register.Rax, SubpOffPid);
    EmitMovMemReg(-0x50, X86Register.Rbx, 8);
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_subprocess_release_handle")); EmitDword(0);
    EmitMovRegMem(X86Register.Rax, -0x50, 8);
    EmitJmp("rt_subp_detach_done");
    DefineLabel("rt_subp_detach_err");
    EmitMovRegImm(X86Register.Rax, -1);
    DefineLabel("rt_subp_detach_done");
    EmitRuntimeFunctionEnd();
  }

  // --------------------------------------------------------------------------
  // __subp_build_cmdline_wide(argv_managed, argc) → mm_raw_alloc'd UTF-16
  // command line (null-terminated). NULL on failure (sets __subp_last_error).
  //
  // argv_managed is a __ManagedMemory whose buffer holds argc UTF-8 strings
  // packed back-to-back, each null-terminated. The stdlib must serialize
  // its StringArray into this format before calling. We concatenate each
  // arg into a UTF-8 command line (with CRT-style quoting), then convert
  // the whole thing to UTF-16 in one MultiByteToWideChar call.
  //
  // For Phase 3.2 we ship a simple quoting strategy: each non-empty arg is
  // surrounded by double quotes; embedded double quotes are escaped with a
  // backslash; backslashes preceding a quote or end-of-arg are doubled.
  // Empty args become `""`. Args are joined by single spaces.
  // --------------------------------------------------------------------------
  private void EmitMaxonSubprocessBuildCmdlineWide() {
    // Stack:
    //   [rbp-0x08]   argv_managed (arg)
    //   [rbp-0x10]   argc (arg)
    //   [rbp-0x18]   argv buffer ptr
    //   [rbp-0x20]   argv buffer length (bytes)
    //   [rbp-0x28]   utf8 cmdline buffer (mm_raw_alloc'd, growable)
    //   [rbp-0x30]   utf8 cmdline capacity
    //   [rbp-0x38]   utf8 cmdline length
    //   [rbp-0x40]   current arg position in argv buffer
    //   [rbp-0x48]   current arg byte index
    //   [rbp-0x50]   wide cmdline buffer (mm_raw_alloc'd)
    //   [rbp-0x58]   wide length (chars)
    //
    // To keep emission tractable, we delegate the byte-level quoting to a
    // simpler scheme: write each utf8 arg verbatim into the cmdline buffer
    // wrapped in `"..."`, with embedded `"` doubled. This is sufficient for
    // the smoke test (`cmd /c echo hello`) and most non-pathological cases.
    EmitRuntimeFunctionStart("__subp_build_cmdline_wide", 2, 0x80);

    EmitMovRegMem(X86Register.Rax, -0x08, 8);                    // argv buffer ptr
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);                    // stash buffer base
    // We don't need a separate "buffer length" — argc bounds the parse.
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovMemReg(-0x20, X86Register.Rcx, 8);
    EmitMovMemReg(-0x48, X86Register.Rcx, 8);                    // current pos into argv buf = 0
    EmitMovMemReg(-0x60, X86Register.Rcx, 8);                    // scan start = 0
    EmitMovMemReg(-0x68, X86Register.Rcx, 8);                    // needsQuote = 0
    EmitMovMemReg(-0x70, X86Register.Rcx, 8);                    // sawAnyByte = 0

    // Initial alloc 256 bytes
    EmitMovRegImm(X86Register.Rcx, 256);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rax, 256);
    EmitMovMemReg(-0x30, X86Register.Rax, 8);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x38, X86Register.Rax, 8);
    EmitMovMemReg(-0x40, X86Register.Rax, 8);                    // arg index

    // Track the starting buffer position of this arg in slot -0x60. We use
    // this to remember "scan start" so we can do a two-pass loop (pass 1:
    // scan for whitespace/quotes to decide whether quoting is needed; pass
    // 2: emit bytes, optionally with surrounding quotes).
    DefineLabel("rt_subp_bcw_arg_loop");
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ge", "rt_subp_bcw_args_done");

    // If not first arg, append space
    EmitMovRegMem(X86Register.Rax, -0x40, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_no_space");
    EmitMovRegImm(X86Register.Rcx, ' ');
    EmitCallRuntimeLabel("__subp_cmdline_append_byte");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    DefineLabel("rt_subp_bcw_no_space");

    // Save the arg's start offset so we can rescan during pass 2.
    EmitMovRegMem(X86Register.Rax, -0x48, 8);
    EmitMovMemReg(-0x60, X86Register.Rax, 8);

    // Pass 1: scan from -0x48 onward looking for whitespace, '"', or empty.
    // Result: slot -0x68 = 1 if we need quotes, 0 if we don't. Empty args
    // also need quotes (`""` for a literal empty token).
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x68, X86Register.Rax, 8);                    // needsQuote = 0
    EmitMovMemReg(-0x70, X86Register.Rax, 8);                    // sawAnyByte = 0

    DefineLabel("rt_subp_bcw_scan_loop");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitBytes(0x0F, 0xB6, 0x14, 0x08);                            // MOVZX EDX, byte [RAX+RCX]
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_subp_bcw_scan_done");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x70, X86Register.Rax, 8);                    // sawAnyByte = 1
    EmitCmpRegImm(X86Register.Rdx, ' ');
    EmitJcc("e", "rt_subp_bcw_need_quote");
    EmitCmpRegImm(X86Register.Rdx, '\t');
    EmitJcc("e", "rt_subp_bcw_need_quote");
    EmitCmpRegImm(X86Register.Rdx, '"');
    EmitJcc("e", "rt_subp_bcw_need_quote");
    EmitJmp("rt_subp_bcw_scan_continue");
    DefineLabel("rt_subp_bcw_need_quote");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x68, X86Register.Rax, 8);
    DefineLabel("rt_subp_bcw_scan_continue");
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitMovMemReg(-0x48, X86Register.Rcx, 8);
    EmitJmp("rt_subp_bcw_scan_loop");

    DefineLabel("rt_subp_bcw_scan_done");
    // Skip the trailing null byte we just stopped on.
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitMovMemReg(-0x48, X86Register.Rcx, 8);
    // Empty args must be emitted as `""` so cmd.exe etc. see the empty token.
    EmitMovRegMem(X86Register.Rax, -0x70, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_subp_bcw_scan_complete");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x68, X86Register.Rax, 8);
    DefineLabel("rt_subp_bcw_scan_complete");

    // If quoting required, emit opening `"`.
    EmitMovRegMem(X86Register.Rax, -0x68, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_no_open_quote");
    EmitMovRegImm(X86Register.Rcx, '"');
    EmitCallRuntimeLabel("__subp_cmdline_append_byte");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    DefineLabel("rt_subp_bcw_no_open_quote");

    // Pass 2: copy bytes from -0x60 (saved start) up to current null.
    EmitMovRegMem(X86Register.Rax, -0x60, 8);
    EmitMovMemReg(-0x48, X86Register.Rax, 8);

    DefineLabel("rt_subp_bcw_arg_byte_loop");
    EmitMovRegMem(X86Register.Rax, -0x18, 8);                    // buffer base
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);                    // current pos
    EmitBytes(0x0F, 0xB6, 0x14, 0x08);                            // MOVZX EDX, byte [RAX+RCX]
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_subp_bcw_arg_byte_done");
    // Advance pos
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitMovMemReg(-0x48, X86Register.Rcx, 8);

    // Quote-escaping is only needed when we're quoting the arg. Otherwise
    // pass the byte through verbatim.
    EmitMovRegMem(X86Register.Rax, -0x68, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_no_escape");
    EmitCmpRegImm(X86Register.Rdx, '"');
    EmitJcc("ne", "rt_subp_bcw_no_escape");
    EmitMovRegImm(X86Register.Rcx, '\\');
    EmitCallRuntimeLabel("__subp_cmdline_append_byte");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    EmitMovRegImm(X86Register.Rdx, '"');
    DefineLabel("rt_subp_bcw_no_escape");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitCallRuntimeLabel("__subp_cmdline_append_byte");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    EmitJmp("rt_subp_bcw_arg_byte_loop");

    DefineLabel("rt_subp_bcw_arg_byte_done");
    // Skip the null terminator
    EmitMovRegMem(X86Register.Rcx, -0x48, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitMovMemReg(-0x48, X86Register.Rcx, 8);

    // Closing quote (only if we opened one).
    EmitMovRegMem(X86Register.Rax, -0x68, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_no_close_quote");
    EmitMovRegImm(X86Register.Rcx, '"');
    EmitCallRuntimeLabel("__subp_cmdline_append_byte");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");
    DefineLabel("rt_subp_bcw_no_close_quote");

    // Next arg
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitMovMemReg(-0x40, X86Register.Rcx, 8);
    EmitJmp("rt_subp_bcw_arg_loop");

    DefineLabel("rt_subp_bcw_args_done");
    // Null-terminate utf8 cmdline
    EmitMovRegImm(X86Register.Rcx, 0);
    EmitCallRuntimeLabel("__subp_cmdline_append_byte");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail");

    // Convert to UTF-16 (MultiByteToWideChar)
    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x28, 8);
    EmitMovRegImm(X86Register.R9, -1);                            // null-terminated
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "MultiByteToWideChar");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail_free");
    EmitMovMemReg(-0x58, X86Register.Rax, 8);

    EmitMovRegMem(X86Register.Rcx, -0x58, 8);
    EmitShlRegImm(X86Register.Rcx, 1);                            // bytes = wchars * 2
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_bcw_fail_free");
    EmitMovMemReg(-0x50, X86Register.Rax, 8);

    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x28, 8);
    EmitMovRegImm(X86Register.R9, -1);
    EmitMovRegMem(X86Register.Rax, -0x50, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x58, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x28);
    EmitCallImport("kernel32.dll", "MultiByteToWideChar");
    EmitSystemStackLeave(0x40);

    // Free utf8 scratch
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    EmitMovRegMem(X86Register.Rax, -0x50, 8);
    EmitJmp("rt_subp_bcw_done");

    DefineLabel("rt_subp_bcw_fail_free");
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_bcw_fail");
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    DefineLabel("rt_subp_bcw_fail");
    EmitLeaRegSymdataRel(X86Register.Rax, "__subp_err_argv");
    EmitGlobalStoreReg(X86Register.Rax, "__subp_last_error");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);

    DefineLabel("rt_subp_bcw_done");
    EmitRuntimeFunctionEnd();

    EmitMaxonSubprocessCmdlineAppendByte();
    EmitMaxonSubprocessManagedToWideOrNull();
    EmitMaxonSubprocessBuildEnvWide();
    EmitMaxonSubprocessOpenNulWide();
    EmitMaxonSubprocessOpenFileWide();
  }

  // __subp_cmdline_append_byte(byte_in_rcx) — append one byte to the
  // growable utf8 cmdline buffer owned by the caller's stack frame at
  // [rbp-0x28]/[rbp-0x30]/[rbp-0x38] (buf/cap/len). Grows when full via
  // mm_raw_alloc + maxon_memcpy + mm_raw_free.
  // Returns 1 on success, 0 on allocation failure. We need stable access
  // to the caller's frame across the alloc/memcpy/free calls (which clobber
  // every caller-saved register), so we stash the caller's RBP in a local
  // slot rather than keeping it in RBX (RBX is caller-saved in the Maxon
  // internal CC; see RegisterManager._callerSavedRegisters).
  //
  // We can't use mm_raw_realloc here because that helper requires a
  // __ManagedMemory header (offset 16 = capacity, offset 24 = element size)
  // to compute how many bytes to copy — our buffer is a raw mm_raw_alloc'd
  // byte run with no such header. We mirror the alloc/memcpy/free that
  // mm_raw_realloc would do, but copy exactly `len` bytes (the populated
  // portion) since the rest of the old buffer is uninitialised.
  private void EmitMaxonSubprocessCmdlineAppendByte() {
    EmitRuntimeFunctionStart("__subp_cmdline_append_byte", 1, 0x40);
    // [rbp-0x08] byte (arg)
    // [rbp-0x10] caller's RBP (snapshot)
    // [rbp-0x18] old buf (snapshot, for free after memcpy)
    // [rbp-0x20] new buf (snapshot, for update after memcpy)
    // [rbp-0x28] len at grow time (bytes to copy)
    EmitMovRegMem(X86Register.Rax, 0, 8);                         // RAX = saved caller RBP from [rbp]
    EmitMovMemReg(-0x10, X86Register.Rax, 8);

    // Check if len + 1 > cap; if so, grow (alloc+copy+free) with new cap = cap*2.
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, -0x38); // len
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, -0x30); // cap
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("le", "rt_subp_cab_have_room");

    // Snapshot old buf and len before any call clobbers caller-saved regs.
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, -0x28); // old buf
    EmitMovMemReg(-0x18, X86Register.Rcx, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, -0x38); // len
    EmitMovMemReg(-0x28, X86Register.Rcx, 8);

    // Allocate new buffer of cap * 2 bytes.
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, -0x30); // cap
    EmitShlRegImm(X86Register.Rcx, 1);                              // new size = cap*2
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_cab_fail");
    EmitMovMemReg(-0x20, X86Register.Rax, 8);                       // new buf

    // Copy the populated prefix (len bytes) from old buf to new buf.
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);                       // dst = new buf
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);                       // src = old buf
    EmitMovRegMem(X86Register.R8, -0x28, 8);                        // count = len
    EmitCallRuntimeLabel("maxon_memcpy");

    // Free the old buffer.
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);

    // Update caller's buf and cap. Reload caller-RBP because every helper
    // call above clobbers caller-saved registers; we kept it on our own stack.
    EmitMovRegMem(X86Register.Rax, -0x10, 8);                       // RAX = caller RBP
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);                       // RCX = new buf
    EmitMovIndirectMemReg(X86Register.Rax, -0x28, X86Register.Rcx);
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, -0x30);
    EmitShlRegImm(X86Register.Rdx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, -0x30, X86Register.Rdx);

    DefineLabel("rt_subp_cab_have_room");
    // Append byte: buf[len] = arg_byte; len++
    EmitMovRegMem(X86Register.Rax, -0x10, 8);                       // caller RBP
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, -0x28); // buf
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, -0x38); // len
    EmitMovRegMem(X86Register.R8, -0x08, 8);                        // arg byte (low 8 bits)
    // MOV byte [RCX+RDX], R8B (R8B = REX.R + 88 /r). Encoding:
    //   REX.R = 0x44; opcode 88; ModR/M: mod=00, reg=000(R8B), r/m=100(SIB);
    //   SIB: scale=00, index=010(RDX), base=001(RCX) → 0x11.
    EmitBytes(0x44, 0x88, 0x04, 0x11);                              // MOV [RCX+RDX*1], R8B
    EmitAddRegImm(X86Register.Rdx, 1);
    EmitMovIndirectMemReg(X86Register.Rax, -0x38, X86Register.Rdx);
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("rt_subp_cab_done");

    DefineLabel("rt_subp_cab_fail");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);

    DefineLabel("rt_subp_cab_done");
    EmitRuntimeFunctionEnd();
  }

  // __subp_managed_to_wide_or_null(buffer_rcx) — RuntimeCall lowering passes
  // the __ManagedMemory's BUFFER POINTER (not the struct). Treat the input
  // as a null-terminated UTF-8 cstring; convert via MultiByteToWideChar.
  // Returns 0 when the buffer is null or empty (so the caller can use NULL
  // semantics, e.g. CreateProcessW's "inherit cwd" or "inherit env").
  private void EmitMaxonSubprocessManagedToWideOrNull() {
    EmitRuntimeFunctionStart("__subp_managed_to_wide_or_null", 1, 0x40);
    // [rbp-0x08] buf  [rbp-0x10] utf8len  [rbp-0x18] wchars  [rbp-0x20] wide buf
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_mtw_null");
    // strlen(buf) — empty → null
    EmitBytes(0x0F, 0xB6, 0x08);                                  // movzx ecx, byte [rax]
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_subp_mtw_null");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_strlen")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);                    // utf8len

    // Required wchars (excluding null because we pass exact length, not -1).
    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x08, 8);                     // utf8 buf
    EmitMovRegMem(X86Register.R9, -0x10, 4);                     // utf8 length (DWORD)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "MultiByteToWideChar");
    EmitSystemStackLeave(0x40);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_mtw_null");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);                    // wchars

    // Alloc (wchars + 1) * 2 for buffer plus null terminator.
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitAddRegImm(X86Register.Rcx, 1);
    EmitShlRegImm(X86Register.Rcx, 1);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring");
    EmitCallRuntimeLabel("mm_raw_alloc", zeroSecondArg: !Compiler.MmTrace);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_mtw_null");
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    EmitSystemStackEnter(0x40);
    EmitMovRegImm(X86Register.Rcx, 65001);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegMem(X86Register.R8, -0x08, 8);
    EmitMovRegMem(X86Register.R9, -0x10, 4);
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x28);
    EmitCallImport("kernel32.dll", "MultiByteToWideChar");
    EmitSystemStackLeave(0x40);
    // Null-terminate at wchars*2.
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitShlRegImm(X86Register.Rcx, 1);
    EmitBytes(0x66, 0xC7, 0x04, 0x08, 0x00, 0x00);                // MOV word [RAX+RCX], 0

    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitJmp("rt_subp_mtw_done");

    DefineLabel("rt_subp_mtw_null");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);

    DefineLabel("rt_subp_mtw_done");
    EmitRuntimeFunctionEnd();
  }

  // __subp_build_env_wide(env_managed_rcx) — TODO Phase 3.x: parse
  // newline-separated K=V pairs from env_managed's buffer and assemble a
  // double-null-terminated UTF-16 environment block. For Phase 3.2 the
  // stdlib only invokes us when envInherit=0 with an explicit env; we
  // return NULL to fall back to inherit semantics until the real parser
  // lands. Callers must surface the "custom env not yet implemented"
  // limitation higher up the stack.
  private void EmitMaxonSubprocessBuildEnvWide() {
    EmitRuntimeFunctionStart("__subp_build_env_wide", 1, 0x20);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  // __subp_open_nul_wide(access_rcx) → HANDLE or 0.
  private void EmitMaxonSubprocessOpenNulWide() {
    EmitRuntimeFunctionStart("__subp_open_nul_wide", 1, 0x40);
    // CreateFileW("NUL", access, FILE_SHARE_READ|WRITE, &SA(inheritable),
    //             OPEN_EXISTING, 0, NULL)
    // Lay out SECURITY_ATTRIBUTES inline at [rbp-0x20]: nLength=24, lpSD=0, bInherit=1.
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);
    EmitMovMemDwordImm(-0x20, 24);
    EmitMovMemDwordImm(-0x10, 1);

    EmitSystemStackEnter(0x40);
    EmitLeaRegSymdataRel(X86Register.Rcx, "__subp_nul_devname_w");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);                    // access
    EmitMovRegImm(X86Register.R8, 3);                             // FILE_SHARE_READ|FILE_SHARE_WRITE
    EmitLeaRegMem(X86Register.R9, -0x20);                         // &SA
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x03, 0x00, 0x00, 0x00);  // OPEN_EXISTING
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);  // flags=0
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00);  // template=NULL
    EmitCallImport("kernel32.dll", "CreateFileW");
    EmitSystemStackLeave(0x40);
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ne", "rt_subp_onw_done");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    DefineLabel("rt_subp_onw_done");
    EmitRuntimeFunctionEnd();
  }

  // __subp_open_file_wide(managed_rcx, access_rdx, disposition_r8) → HANDLE or 0.
  private void EmitMaxonSubprocessOpenFileWide() {
    EmitRuntimeFunctionStart("__subp_open_file_wide", 3, 0x50);
    // [rbp-0x08] managed, [rbp-0x10] access, [rbp-0x18] disposition
    // [rbp-0x30..-0x18] SECURITY_ATTRIBUTES (24 bytes; base -0x30)
    // [rbp-0x40] wide path
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x30, X86Register.Rax, 8);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitMovMemReg(-0x40, X86Register.Rax, 8);
    EmitMovMemDwordImm(-0x30, 24);
    EmitMovMemDwordImm(-0x20, 1);

    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallRuntimeLabel("__subp_managed_to_wide_or_null");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_subp_ofw_fail");
    EmitMovMemReg(-0x40, X86Register.Rax, 8);

    EmitSystemStackEnter(0x40);
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitMovRegImm(X86Register.R8, 3);
    EmitLeaRegMem(X86Register.R9, -0x30);
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20);                      // disposition
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x80, 0x00, 0x00, 0x00);  // FILE_ATTRIBUTE_NORMAL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "CreateFileW");
    EmitSystemStackLeave(0x40);

    EmitMovRegReg(X86Register.Rbx, X86Register.Rax);              // save
    EmitMovRegMem(X86Register.Rcx, -0x40, 8);
    EmitCallRuntimeLabel("mm_raw_free", zeroSecondArg: Compiler.MmTrace);
    EmitMovRegReg(X86Register.Rax, X86Register.Rbx);

    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ne", "rt_subp_ofw_done");
    DefineLabel("rt_subp_ofw_fail");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    DefineLabel("rt_subp_ofw_done");
    EmitRuntimeFunctionEnd();
  }

}
