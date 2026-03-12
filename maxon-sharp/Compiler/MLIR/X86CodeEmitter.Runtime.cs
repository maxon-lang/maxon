using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

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
    EmitMaxonBoundsCheck();
    EmitMaxonCowCheck();
    EmitMaxonToCString();
    EmitMaxonWriteStdout();
    EmitMaxonWriteStderr();
    EmitMaxonManagedWriteStdout();
    EmitMaxonManagedWriteStderr();
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
    EmitMaxonFileSize();
    EmitMaxonFileRead();
    EmitMaxonFileClose();
    EmitMaxonFileDelete();
    EmitMaxonFindFilename();
    EmitMaxonFindNextFile();
    EmitMaxonDirectoryExists();
    EmitMaxonCreateDirectory();
    EmitMaxonGetCurrentDirectory();
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
    EmitMaxonStrlen();
    EmitMaxonMemcpy();
    EmitMaxonMemcmp();
    EmitMmRawAlloc();
    EmitMmRawRealloc();
    EmitMmRawFree();
    EmitNetTcpConnect();
    EmitNetSend();
    EmitNetRecv();
    EmitNetClose();
    EmitNetSocketDestructor();
    EmitManagedFileOpenRead();
    EmitManagedFileOpenWrite();
    EmitFileExists();
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
    // Tail-call maxon_panic so its stack walk sees the *caller's* frame, not ours
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = msg_r8 (saved at [rbp-0x18])
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitJmp("maxon_panic");
    DefineLabel("rt_bounds_ok");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cow_check(buffer_in_rcx, capacity_in_rdx, byteLen_in_r8, managedPtr_in_r9) -> new_buffer_in_rax
  /// If capacity != 0, buffer is already writable — return it as-is.
  /// If capacity == 0, allocate byteLen bytes via mm_raw_alloc, copy from old buffer, return new buffer.
  /// The old rdata buffer is NOT freed (capacity==0 identifies rdata).
  /// managedPtr is the owning managed struct's heap pointer, used for --mm-trace output.
  /// </summary>
  private void EmitMaxonCowCheck() {
    // Args: rcx=buffer, rdx=capacity, r8=byteLen, r9=managedPtr
    // Stack: [rbp-0x08]=buffer, [rbp-0x10]=capacity, [rbp-0x18]=byteLen,
    //        [rbp-0x20]=managedPtr, [rbp-0x28]=new_buffer
    EmitRuntimeFunctionStart("maxon_cow_check", 4, 0x60);
    // TEST rdx, rdx (check capacity)
    EmitBytes(0x48, 0x85, 0xD2);
    EmitJcc("nz", "rt_cow_writable");
    // If byteLen == 0, nothing to copy — skip COW (e.g. empty array)
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = byteLen
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_cow_writable");
    // COW path: allocate byteLen bytes, copy old buffer
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // RCX = byteLen
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
    EmitCallImport("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = handle
    // WriteFile(handle, buffer, nNumberOfBytesToWrite, &lpNumberOfBytesWritten, lpOverlapped)
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);  // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x10, 8);   // arg3: length
    // arg4: LEA R9, [rbp-0x20] (&bytesWritten)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // arg5: lpOverlapped = NULL at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
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
    EmitCallImport("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitMovRegMem(X86Register.R8, -0x10, 8);
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
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
    EmitCallImport("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = handle
    // WriteFile(handle, buffer, nNumberOfBytesToWrite, &bytesWritten, lpOverlapped)
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);  // arg2: buffer
    EmitMovRegMem(X86Register.R8, -0x10, 8);   // arg3: length
    // arg4: LEA R9, [rbp-0x20] (&bytesWritten)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // arg5: lpOverlapped = NULL at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");
    // Return bytes written
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitRuntimeFunctionEnd();
  }

  private void EmitMaxonManagedWriteStdout() =>
    EmitMaxonManagedWrite("maxon_managed_write_stdout", -11); // STD_OUTPUT_HANDLE

  private void EmitMaxonManagedWriteStderr() =>
    EmitMaxonManagedWrite("maxon_managed_write_stderr", -12); // STD_ERROR_HANDLE

  /// <summary>
  /// maxon_panic(cstr_ptr_in_rcx): write message to stderr, print stack trace, then ExitProcess(1).
  /// Stack layout:
  ///   [rbp-0x08] = cstr_ptr (panic message)
  ///   [rbp-0x10] = text_base (absolute address of _start)
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

    EmitRuntimeFunctionStart("maxon_panic", 1, 0x60);

    // Print the panic message
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "maxon_write_stderr"));
    EmitDword(0);

    // Compute text_base = absolute address of _start
    EmitLeaFuncAddr(X86Register.Rax, "_start");
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
    _relCallFixups.Add((_code.Count, "maxon_panic_print_frame"));
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
    _relCallFixups.Add((_code.Count, "maxon_panic_print_frame"));
    EmitDword(0);

    EmitJmp("rt_panic_walk_loop");

    DefineLabel("rt_panic_walk_done");
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitCallImport("kernel32.dll", "ExitProcess");
    EmitByte(0xCC); // int3
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Helper: looks up [rbp-0x38] (text_offset) in the symbol table and prints "  in funcName\n".
  /// Shares maxon_panic's stack frame (accesses [rbp-0x18], [rbp-0x30], [rbp-0x38]).
  /// </summary>
  private void EmitMaxonPanicPrintFrame() {
    DefineLabel("maxon_panic_print_frame");

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
    EmitCallImport("kernel32.dll", "GetCommandLineW");

    // Step 2: Parse into argv array
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    // RDX = &argc (pointer to 4-byte int)
    EmitLeaRegMem(X86Register.Rdx, -0x08);
    EmitCallImport("shell32.dll", "CommandLineToArgvW");

    // Step 3: Save argv pointer for LocalFree, and save argc before the call clobbers registers
    EmitMovMemReg(-0x10, X86Register.Rax, 8);
    // argc is a 32-bit int at [rbp-0x08]; load as 4 bytes (zero-extends to 64-bit)
    EmitMovRegMem(X86Register.Rax, -0x08, 4);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // Step 4: Free the argv array
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallImport("kernel32.dll", "LocalFree");

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
    EmitCallImport("kernel32.dll", "GetCommandLineW");

    // Step 2: Parse into argv array
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    // RDX = &argc -> LEA RDX, [rbp-0x30]
    EmitLeaRegMem(X86Register.Rdx, -0x30);
    EmitCallImport("shell32.dll", "CommandLineToArgvW");

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
    EmitCallImport("kernel32.dll", "LocalFree");
    // Allocate 1-byte buffer with null terminator
    EmitMovRegImm(X86Register.Rcx, 1);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);     // destructor = 0
    EmitTagZero(X86Register.R8);
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_cmdline_arg");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
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

    // Save required size
    EmitMovMemReg(-0x20, X86Register.Rax, 8);

    // Step 6: Allocate buffer via mm_alloc
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);         // destructor = 0
    EmitTagZero(X86Register.R8);    // R8 = tag
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_cmdline_arg");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);

    // Save buffer pointer
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    // Step 7: Convert wide string to UTF-8 into the allocated buffer
    // WideCharToMultiByte(CP_UTF8, 0, wideStr, -1, buffer, size, NULL, NULL)
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

    // Step 8: Free the argv array from CommandLineToArgvW
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitCallImport("kernel32.dll", "LocalFree");

    // Step 9: Return buffer pointer
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    DefineLabel("rt_cla_return");
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
    EmitCallImport("kernel32.dll", "GetFileSizeEx");
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
  /// maxon_file_close(handle) -> void
  /// Routes CloseHandle through the sync worker to avoid green thread stack crashes.
  /// Idempotent: no-op if handle is zero.
  /// Stack: [rbp-8]=handle
  /// </summary>
  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1, 0x20);
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);         // handle
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("z", "rt_fc_noop");
    EmitMovRegImm(X86Register.Rcx, SyncOpCloseHandle); // op = 9
    EmitXorRegReg(X86Register.R8, X86Register.R8);     // arg1 = 0
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
  /// maxon_get_current_directory() -> cstring pointer
  /// Allocates a buffer, calls GetCurrentDirectoryA, returns pointer to the path.
  /// </summary>
  private void EmitMaxonGetCurrentDirectory() {
    EmitRuntimeFunctionStart("maxon_get_current_directory", 0, 0x40);
    // Allocate 260 bytes (MAX_PATH) for the buffer
    EmitMovRegImm(X86Register.Rcx, 260);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);     // destructor = 0
    EmitTagZero(X86Register.R8); // R8 = tag
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_get_cwd");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = buffer
    // GetCurrentDirectoryA(nBufferLength=260, lpBuffer=buffer)
    EmitMovRegImm(X86Register.Rcx, 260);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitCallImport("kernel32.dll", "GetCurrentDirectoryA");
    // Return buffer pointer
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Process runtime functions
  // ==========================================================================

  // Stack layout for maxon_process_create:
  //   [rbp-0x08] = cmd (arg1)
  //   [rbp-0x10] = cwd (arg2)
  //   [rbp-0x28]..[rbp-0x11] = PROCESS_INFORMATION (24 bytes, base at rbp-0x28)
  //     PI+0x00 [rbp-0x28] = hProcess
  //     PI+0x08 [rbp-0x20] = hThread
  //     PI+0x10 [rbp-0x18] = dwProcessId
  //     PI+0x14 [rbp-0x14] = dwThreadId
  //   [rbp-0x90]..[rbp-0x29] = STARTUPINFOA (104 bytes, base at rbp-0x90)
  //     SI+0x00 [rbp-0x90] = cb (DWORD) = 104
  //     SI+0x3C [rbp-0x54] = dwFlags
  //     SI+0x50 [rbp-0x40] = hStdInput
  //     SI+0x58 [rbp-0x38] = hStdOutput
  //     SI+0x60 [rbp-0x30] = hStdError
  private const int SiBase = -0x90;
  private const int PiBase = -0x28;

  /// <summary>
  /// maxon_process_create(cstring_cmd, cstring_cwd) -> hProcess handle, or 0 on failure.
  /// Uses CreateProcessA. If cwd is empty string, passes NULL for lpCurrentDirectory.
  /// </summary>
  private void EmitMaxonProcessCreate() {
    EmitRuntimeFunctionStart("maxon_process_create", 2, 0x100);

    // CreateProcessA requires zeroed STARTUPINFOA and PROCESS_INFORMATION structs
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitLeaRegMem(X86Register.Rdi, SiBase);
    EmitMovRegImm(X86Register.Rcx, 13);                  // STARTUPINFOA: 104 bytes = 13 qwords
    EmitBytes(0xF3, 0x48, 0xAB); // REP STOSQ
    EmitLeaRegMem(X86Register.Rdi, PiBase);
    EmitMovRegImm(X86Register.Rcx, 3);                   // PROCESS_INFORMATION: 24 bytes = 3 qwords
    EmitBytes(0xF3, 0x48, 0xAB); // REP STOSQ

    // Required by CreateProcessA: cb must equal sizeof(STARTUPINFOA)
    EmitMovMemDwordImm(SiBase, 104);

    EmitNullIfEmptyCwd(-0x10, "rt_pc_cwd_ok");
    EmitCallCreateProcessA(-0x10, inheritHandles: false, SiBase, PiBase);

    // Check result (non-zero = success)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_pc_fail");

    // Close the thread handle (we only need the process handle)
    EmitMovRegMem(X86Register.Rcx, PiBase + 0x08, 8);    // hThread
    EmitCallImport("kernel32.dll", "CloseHandle");

    // Return hProcess
    EmitMovRegMem(X86Register.Rax, PiBase, 8);
    EmitJmp("rt_pc_done");

    DefineLabel("rt_pc_fail");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);     // return 0 on failure

    DefineLabel("rt_pc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_wait(handle, timeout_ms) -> 0=completed, 1=timeout, -1=error.
  /// timeout_ms of 0 means INFINITE (0xFFFFFFFF).
  /// </summary>
  private void EmitMaxonProcessWait() {
    EmitRuntimeFunctionStart("maxon_process_wait", 2, 0x30);

    // Convert timeout: if 0, use INFINITE (0xFFFFFFFF)
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);            // RDX = timeout_ms
    EmitTestRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitJcc("nz", "rt_pw_has_timeout");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);          // INFINITE
    DefineLabel("rt_pw_has_timeout");

    // WaitForSingleObject(handle, dwMilliseconds)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);            // arg1: handle
    // RDX already has timeout
    EmitCallImport("kernel32.dll", "WaitForSingleObject");

    // Map Win32 wait result to Maxon convention: 0=completed, 1=timeout, -1=error
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_pw_ok");                             // RAX==0 -> completed
    EmitBytes(0x3D, 0x02, 0x01, 0x00, 0x00);             // CMP EAX, 0x102 (WAIT_TIMEOUT)
    EmitJcc("e", "rt_pw_timeout");
    // Error
    EmitMovRegImm(X86Register.Rax, -1);
    EmitJmp("rt_pw_done");
    DefineLabel("rt_pw_timeout");
    EmitMovRegImm(X86Register.Rax, 1);
    EmitJmp("rt_pw_done");
    DefineLabel("rt_pw_ok");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);     // return 0

    DefineLabel("rt_pw_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_get_exit_code(handle) -> exit code, or -1 on failure.
  /// </summary>
  private void EmitMaxonProcessGetExitCode() {
    EmitRuntimeFunctionStart("maxon_process_get_exit_code", 1, 0x30);

    // GetExitCodeProcess writes a DWORD; ensure upper bytes are zero for clean 64-bit read
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);

    // GetExitCodeProcess(handle, &exitCode)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);            // arg1: handle
    EmitLeaRegMem(X86Register.Rdx, -0x10);               // arg2: &exitCode
    EmitCallImport("kernel32.dll", "GetExitCodeProcess");

    // Check result (non-zero = success)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("nz", "rt_pgec_ok");
    EmitMovRegImm(X86Register.Rax, -1);                  // return -1 on failure
    EmitJmp("rt_pgec_done");

    DefineLabel("rt_pgec_ok");
    EmitMovRegMem(X86Register.Rax, -0x10, 4);            // return exitCode (DWORD, zero-extended)

    DefineLabel("rt_pgec_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_close(handle) -> void. Calls CloseHandle.
  /// </summary>
  private void EmitMaxonProcessClose() {
    EmitRuntimeFunctionStart("maxon_process_close", 1);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);            // arg1: handle
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Process capture runtime functions
  // ==========================================================================

  // Heap struct returned by maxon_process_create_with_capture:
  //   +0x00: hProcess (HANDLE)
  //   +0x08: hStdoutRead (HANDLE)
  //   +0x10: hStderrRead (HANDLE)
  private const int CaptureStructSize = 24;

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

  /// <summary>If cwd at [rbp+cwdSlot] is empty string, overwrite with NULL for CreateProcessA.</summary>
  private void EmitNullIfEmptyCwd(int cwdSlot, string okLabel) {
    EmitMovRegMem(X86Register.Rax, cwdSlot, 8);
    EmitBytes(0x80, 0x38, 0x00); // CMP byte [RAX], 0
    EmitJcc("ne", okLabel);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(cwdSlot, X86Register.Rax, 8);
    DefineLabel(okLabel);
  }

  /// <summary>Set up and call CreateProcessA. Args at [rbp-0x08]=cmd, [rbp+cwdSlot]=cwd.</summary>
  private void EmitCallCreateProcessA(int cwdSlot, bool inheritHandles, int siBase, int piBase) {
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);    // lpApplicationName = NULL
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);            // lpCommandLine = cmd
    EmitXorRegReg(X86Register.R8, X86Register.R8);       // lpProcessAttributes = NULL
    EmitXorRegReg(X86Register.R9, X86Register.R9);       // lpThreadAttributes = NULL
    // arg5: bInheritHandles
    var inheritVal = inheritHandles ? 0x01 : 0x00;
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, (byte)inheritVal, 0x00, 0x00, 0x00);
    // arg6: dwCreationFlags = 0
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00);
    // arg7: lpEnvironment = NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00);
    // arg8: lpCurrentDirectory
    EmitMovRegMem(X86Register.Rax, cwdSlot, 8);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x38);             // MOV [rsp+0x38], RAX
    // arg9: lpStartupInfo
    EmitLeaRegMem(X86Register.Rax, siBase);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x40);             // MOV [rsp+0x40], RAX
    // arg10: lpProcessInformation
    EmitLeaRegMem(X86Register.Rax, piBase);
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x48);             // MOV [rsp+0x48], RAX
    EmitCallImport("kernel32.dll", "CreateProcessA");
  }

  /// <summary>Store a qword from [rbp+sourceSlot] into heap struct at [rbp+structSlot]+fieldOffset.</summary>
  private void EmitStoreToCaptureField(int sourceSlot, int structSlot, int fieldOffset) {
    EmitMovRegMem(X86Register.Rcx, sourceSlot, 8);
    EmitMovRegMem(X86Register.Rax, structSlot, 8);
    EmitMovIndirectMemReg(X86Register.Rax, fieldOffset, X86Register.Rcx);
  }

  /// <summary>
  /// maxon_process_create_with_capture(cstring_cmd, cstring_cwd) -> ptr to capture struct, or 0.
  /// Creates a process with stdout/stderr redirected through pipes.
  /// </summary>
  private void EmitMaxonProcessCreateWithCapture() {
    const int stdoutRead = -0x18, stdoutWrite = -0x20;
    const int stderrRead = -0x28, stderrWrite = -0x30;
    const int saBase = -0x48; // SECURITY_ATTRIBUTES: nLength at +0, lpSecDesc at +8, bInherit at +16
    const int resultSlot = -0x50;
    const int siBase = -0xB8; // STARTUPINFOA (104 bytes)
    const int piBase = -0xD0; // PROCESS_INFORMATION (24 bytes)

    EmitRuntimeFunctionStart("maxon_process_create_with_capture", 2, 0x140);

    // CreateProcessA and CreatePipe require zeroed structs; zero all local slots at once
    // but stop before args at -0x10/-0x08 (23 qwords covers piBase through stdoutRead)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitLeaRegMem(X86Register.Rdi, piBase);
    EmitMovRegImm(X86Register.Rcx, 23);
    EmitBytes(0xF3, 0x48, 0xAB); // REP STOSQ

    // Set up SECURITY_ATTRIBUTES: nLength=24, lpSecurityDescriptor=NULL, bInheritHandle=TRUE
    EmitMovMemDwordImm(saBase, 24);         // SA.nLength = 24
    EmitMovMemDwordImm(saBase + 16, 1);     // SA.bInheritHandle = TRUE

    // CreatePipe for stdout: CreatePipe(&stdoutRead, &stdoutWrite, &sa, 0)
    EmitLeaRegMem(X86Register.Rcx, stdoutRead);
    EmitLeaRegMem(X86Register.Rdx, stdoutWrite);
    EmitLeaRegMem(X86Register.R8, saBase);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreatePipe");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_pcwc_fail");

    // CreatePipe for stderr: CreatePipe(&stderrRead, &stderrWrite, &sa, 0)
    EmitLeaRegMem(X86Register.Rcx, stderrRead);
    EmitLeaRegMem(X86Register.Rdx, stderrWrite);
    EmitLeaRegMem(X86Register.R8, saBase);
    EmitXorRegReg(X86Register.R9, X86Register.R9);
    EmitCallImport("kernel32.dll", "CreatePipe");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_pcwc_fail_close_stdout");

    // SetHandleInformation on read ends: clear HANDLE_FLAG_INHERIT so they aren't inherited
    EmitMovRegMem(X86Register.Rcx, stdoutRead, 8);
    EmitMovRegImm(X86Register.Rdx, 1);                   // HANDLE_FLAG_INHERIT
    EmitXorRegReg(X86Register.R8, X86Register.R8);       // dwFlags = 0 (clear inherit)
    EmitCallImport("kernel32.dll", "SetHandleInformation");

    EmitMovRegMem(X86Register.Rcx, stderrRead, 8);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitXorRegReg(X86Register.R8, X86Register.R8);
    EmitCallImport("kernel32.dll", "SetHandleInformation");

    // Configure STARTUPINFOA to redirect child process stdout/stderr through our pipes
    EmitMovMemDwordImm(siBase, 104);                     // cb = sizeof(STARTUPINFOA)
    EmitMovMemDwordImm(siBase + 0x3C, 0x100);           // dwFlags = STARTF_USESTDHANDLES
    EmitMovRegMem(X86Register.Rax, stdoutWrite, 8);
    EmitMovMemReg(siBase + 0x58, X86Register.Rax, 8);   // hStdOutput = stdout pipe write end
    EmitMovRegMem(X86Register.Rax, stderrWrite, 8);
    EmitMovMemReg(siBase + 0x60, X86Register.Rax, 8);   // hStdError = stderr pipe write end

    EmitNullIfEmptyCwd(-0x10, "rt_pcwc_cwd_ok");
    EmitCallCreateProcessA(-0x10, inheritHandles: true, siBase, piBase);

    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_pcwc_fail_close_all");

    // Close pipe write ends (parent doesn't need them)
    EmitMovRegMem(X86Register.Rcx, stdoutWrite, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitMovRegMem(X86Register.Rcx, stderrWrite, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");

    // Close thread handle
    EmitMovRegMem(X86Register.Rcx, piBase + 0x08, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");

    // Allocate result struct (24 bytes)
    EmitMovRegImm(X86Register.Rcx, CaptureStructSize);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);           // destructor = 0
    EmitTagZero(X86Register.R8);   // R8 = tag
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_capture");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    EmitMovMemReg(resultSlot, X86Register.Rax, 8);

    // Populate capture struct: hProcess, stdoutRead, stderrRead
    EmitStoreToCaptureField(piBase, resultSlot, 0x00);
    EmitStoreToCaptureField(stdoutRead, resultSlot, 0x08);
    EmitStoreToCaptureField(stderrRead, resultSlot, 0x10);

    // Return struct pointer
    EmitMovRegMem(X86Register.Rax, resultSlot, 8);
    EmitJmp("rt_pcwc_done");

    // Error paths: clean up handles
    DefineLabel("rt_pcwc_fail_close_all");
    EmitMovRegMem(X86Register.Rcx, stderrRead, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitMovRegMem(X86Register.Rcx, stderrWrite, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    DefineLabel("rt_pcwc_fail_close_stdout");
    EmitMovRegMem(X86Register.Rcx, stdoutRead, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitMovRegMem(X86Register.Rcx, stdoutWrite, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    DefineLabel("rt_pcwc_fail");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);

    DefineLabel("rt_pcwc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_pipe(pipe_handle) -> cstring_ptr.
  /// Reads all data from a pipe into a heap-allocated null-terminated buffer.
  /// Uses a 4KB initial buffer with realloc growth strategy.
  /// Stack: [rbp-0x08]=pipe_handle, [rbp-0x10]=buffer, [rbp-0x18]=capacity,
  ///        [rbp-0x20]=total_read, [rbp-0x28]=bytes_read (for ReadFile)
  /// </summary>
  private void EmitMaxonProcessReadPipe() {
    EmitRuntimeFunctionStart("maxon_process_read_pipe", 1, 0x50);

    // Allocate initial buffer (4096 bytes)
    EmitMovRegImm(X86Register.Rcx, 4096);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);     // destructor = 0
    EmitTagZero(X86Register.R8); // R8 = tag
    if (Compiler.MmTrace) EmitLeaRegSymdataRel(X86Register.R9, "__mm_scope_pipe_read");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);           // buffer
    EmitMovRegImm(X86Register.Rax, 4096);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);           // capacity = 4096
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);           // total_read = 0

    // Read loop
    DefineLabel("rt_prp_loop");
    // ReadFile writes to bytesRead; reset so we can detect EOF (bytesRead==0)
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);

    // Check if we need to grow buffer: if total_read + 4096 > capacity, realloc
    EmitMovRegMem(X86Register.Rax, -0x20, 8);           // total_read
    EmitAddRegImm(X86Register.Rax, 4096);
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // capacity
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("le", "rt_prp_read");

    // Grow: new capacity = capacity * 2
    EmitMovRegMem(X86Register.Rdx, -0x18, 8);
    EmitBytes(0x48, 0xD1, 0xE2);                        // SHL RDX, 1 (double)
    EmitMovMemReg(-0x18, X86Register.Rdx, 8);           // update capacity
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);           // old buffer
    // RDX already has new size
    EmitTagZero(X86Register.R8); // R8 = tag
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_realloc")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);           // update buffer

    DefineLabel("rt_prp_read");
    // ReadFile(pipeHandle, buffer + total_read, 4096, &bytesRead, NULL)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);           // arg1: pipe handle
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);           // buffer
    EmitMovRegMem(X86Register.Rax, -0x20, 8);           // total_read
    EmitBytes(0x48, 0x01, 0xC2);                         // ADD RDX, RAX (buffer + total_read)
    EmitMovRegImm(X86Register.R8, 4096);                 // arg3: bytes to read
    EmitLeaRegMem(X86Register.R9, -0x28);               // arg4: &bytesRead
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00); // [rsp+0x20] = NULL
    EmitCallImport("kernel32.dll", "ReadFile");

    // If ReadFile returns 0 or bytesRead == 0, we're done
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_prp_done");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);           // bytesRead
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_prp_done");

    // total_read += bytesRead
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitAddRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovMemReg(-0x20, X86Register.Rcx, 8);
    EmitJmp("rt_prp_loop");

    DefineLabel("rt_prp_done");
    // Ensure buffer has room for null terminator: if total_read >= capacity, realloc
    EmitMovRegMem(X86Register.Rax, -0x20, 8);           // total_read
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);           // capacity
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("b", "rt_prp_nullterm");                     // total_read < capacity → skip realloc
    // Realloc to total_read + 1
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);           // old buffer
    EmitMovRegMem(X86Register.Rdx, -0x20, 8);           // total_read
    EmitAddRegImm(X86Register.Rdx, 1);                  // new size = total_read + 1
    EmitMovMemReg(-0x18, X86Register.Rdx, 8);           // update capacity
    EmitTagZero(X86Register.R8); // R8 = tag
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_realloc")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8);           // update buffer
    DefineLabel("rt_prp_nullterm");
    // Null-terminate: buffer[total_read] = 0
    EmitMovRegMem(X86Register.Rax, -0x10, 8);           // buffer
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);           // total_read
    EmitBytes(0xC6, 0x04, 0x08, 0x00);                  // MOV byte [RAX+RCX], 0

    // Close the pipe handle
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");

    // Return buffer pointer
    EmitMovRegMem(X86Register.Rax, -0x10, 8);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_get_handle(capture_struct_ptr) -> hProcess handle.
  /// Extracts hProcess from the capture struct. Returns 0 if capture_ptr is null.
  /// </summary>
  private void EmitMaxonProcessGetHandle() {
    EmitRuntimeFunctionStart("maxon_process_get_handle", 1);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    // Null capture_ptr: RAX is already 0 from the TEST, so just skip to done
    EmitJcc("z", "rt_pgh_done");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, 0x00);
    DefineLabel("rt_pgh_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_close_capture(capture_struct_ptr) -> void.
  /// Closes the hProcess handle inside the capture struct, then frees the struct.
  /// </summary>
  private void EmitMaxonProcessCloseCapture() {
    EmitRuntimeFunctionStart("maxon_process_close_capture", 1, 0x20);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_pcc_done");
    // Close hProcess handle at [capture+0x00]
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, 0x00);
    EmitCallImport("kernel32.dll", "CloseHandle");
    // Free the capture struct
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);
    DefineLabel("rt_pcc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_stdout(capture_struct_ptr) -> cstring_ptr.
  /// Reads stdout pipe from capture struct, returns null-terminated heap string.
  /// Returns empty string if capture_ptr is null or pipe handle is 0.
  /// </summary>
  private void EmitMaxonProcessReadStdout() {
    EmitRuntimeFunctionStart("maxon_process_read_stdout", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);           // capture struct
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_prso_empty");
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, 0x08); // hStdoutRead
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("nz", "rt_prso_read");
    DefineLabel("rt_prso_empty");
    // Return pointer to static empty string
    EmitLeaRegSymdataRel(X86Register.Rax, "__rt_empty_cstring");
    EmitJmp("rt_prso_done");
    DefineLabel("rt_prso_read");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_process_read_pipe")); EmitDword(0);
    // Zero the pipe handle in capture struct to prevent double-close
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);           // reload capture struct
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rcx, 0x08, X86Register.Rdx); // captureStruct[0x08] = 0
    DefineLabel("rt_prso_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_stderr(capture_struct_ptr) -> cstring_ptr.
  /// Reads stderr pipe from capture struct, returns null-terminated heap string.
  /// Returns empty string if capture_ptr is null or pipe handle is 0.
  /// </summary>
  private void EmitMaxonProcessReadStderr() {
    EmitRuntimeFunctionStart("maxon_process_read_stderr", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);           // capture struct
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", "rt_prse_empty");
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, 0x10); // hStderrRead
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("nz", "rt_prse_read");
    DefineLabel("rt_prse_empty");
    // Return pointer to static empty string
    EmitLeaRegSymdataRel(X86Register.Rax, "__rt_empty_cstring");
    EmitJmp("rt_prse_done");
    DefineLabel("rt_prse_read");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_process_read_pipe")); EmitDword(0);
    // Zero the pipe handle in capture struct to prevent double-close
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);           // reload capture struct
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rcx, 0x10, X86Register.Rdx); // captureStruct[0x10] = 0
    DefineLabel("rt_prse_done");
    EmitRuntimeFunctionEnd();
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

  /// <summary>
  /// mm_raw_alloc(size_rcx) -> ptr_rax
  /// Simple HeapAlloc wrapper: GetProcessHeap, HeapAlloc(heap, HEAP_ZERO_MEMORY, size).
  /// Returns raw pointer with no refcount header.
  /// </summary>
  private void EmitMmRawAlloc() {
    EmitRuntimeFunctionStart("mm_raw_alloc", 1, 0x30);

    // size==0: empty managed memory buffer — return null without heap call.
    // The destructor guards capacity==0 before calling mm_raw_free, so null is safe.
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = size
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_raw_alloc_size_ok");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax); // RAX = 0 (null)
    EmitRuntimeFunctionEnd();
    DefineLabel("mm_raw_alloc_size_ok");

    // GetProcessHeap() -> RAX = heap handle
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);    // arg1: hHeap
    EmitMovRegImm(X86Register.Rdx, 0x08);                // arg2: HEAP_ZERO_MEMORY = 0x08
    EmitMovRegMem(X86Register.R8, -0x08, 8);             // arg3: size (saved from RCX by prologue)
    EmitCallImport("kernel32.dll", "HeapAlloc");
    // RAX = allocated pointer
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_raw_realloc(ptr_rcx, new_size_rdx, managedPtr_r8) -> new_ptr_rax
  /// Allocates a new buffer, copies old data, poisons the old buffer with 0xDE, then frees it.
  /// This guarantees that any dangling pointers into the old buffer will read garbage,
  /// making use-after-realloc bugs deterministic rather than dependent on OS behavior.
  /// managedPtr is the owning managed struct's heap pointer, used for old size computation
  /// and --mm-trace output.
  /// </summary>
  private void EmitMmRawRealloc() {
    EmitRuntimeFunctionStart("mm_raw_realloc", 3, 0x60);
    // [rbp-0x08] = old_ptr, [rbp-0x10] = new_size, [rbp-0x18] = managedPtr
    // [rbp-0x28] = new_ptr, [rbp-0x30] = old_byte_size

    // Panic if new_size == 0 (capacity * element_size = 0 is a bug)
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // RAX = new_size
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "mm_raw_realloc_size_ok");
    EmitLeaRegSymdataRel(X86Register.Rcx, "__mm_panic_realloc_zero_size");
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_panic")); EmitDword(0);
    DefineLabel("mm_raw_realloc_size_ok");

    // Step 1: Allocate new buffer via mm_raw_alloc(new_size)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = new_size
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_alloc")); EmitDword(0);
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save new_ptr

    // Step 2: Compute old_byte_size = managedPtr->capacity * managedPtr->element_size
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = managedPtr
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, 16); // RCX = [managedPtr+16] = capacity
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, 24); // RDX = [managedPtr+24] = element_size
    EmitBytes(0x48, 0x0F, 0xAF, 0xCA); // IMUL RCX, RDX → RCX = old_byte_size
    EmitMovMemReg(-0x30, X86Register.Rcx, 8); // save old_byte_size

    // Step 3: memcpy(new_ptr, old_ptr, old_byte_size)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = new_ptr (dst)
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // RDX = old_ptr (src)
    EmitMovRegMem(X86Register.R8, -0x30, 8);  // R8  = old_byte_size (count)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_memcpy")); EmitDword(0);

    // Step 4: Poison old buffer with 0xDE (REP STOSB: RDI=dst, RCX=count, AL=fill)
    EmitMovRegMem(X86Register.Rdi, -0x08, 8); // RDI = old_ptr
    EmitMovRegMem(X86Register.Rcx, -0x30, 8); // RCX = old_byte_size
    EmitMovRegImm(X86Register.Rax, 0xDE);      // AL = 0xDE
    EmitBytes(0xF3, 0xAA);                     // REP STOSB

    // Step 5: Free old buffer
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = old_ptr
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_raw_free")); EmitDword(0);

    // Return new_ptr
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    DefineLabel("mm_raw_realloc_done");
    if (Compiler.MmTrace) {
      EmitMovMemReg(-0x28, X86Register.Rax, 8); // save new_ptr
      EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
      EmitMovMemReg(-0x30, X86Register.Rcx, 8); // [rbp-0x30] = 0 (no scope)
      EmitInlineTrace("__mm_tag_realloc", "mm_raw_realloc_trace", -0x18, -0x30, sizeSlot: -0x10);
      EmitMovRegMem(X86Register.Rax, -0x28, 8); // restore new_ptr
    }
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// mm_raw_free(ptr_rcx)
  /// Simple HeapFree wrapper: GetProcessHeap, HeapFree(heap, 0, ptr).
  /// </summary>
  private void EmitMmRawFree() {
    EmitRuntimeFunctionStart("mm_raw_free", 1, 0x30);
    // GetProcessHeap() -> RAX = heap handle
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);    // arg1: hHeap
    EmitMovRegImm(X86Register.Rdx, 0);                   // arg2: flags = 0
    EmitMovRegMem(X86Register.R8, -0x08, 8);             // arg3: ptr
    EmitCallImport("kernel32.dll", "HeapFree");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Calls GetProcessHeap then a Heap function with the heap handle in RCX and flags in RDX.
  /// Remaining args (R8, R9) must be set by the caller before calling this helper.
  /// </summary>
  private void EmitHeapCall(string heapFunction, int flags, int rbpSlotR8, int rbpSlotR9 = 0) {
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegImm(X86Register.Rdx, flags);
    EmitMovRegMem(X86Register.R8, rbpSlotR8, 8);
    if (rbpSlotR9 != 0)
      EmitMovRegMem(X86Register.R9, rbpSlotR9, 8);
    EmitCallImport("kernel32.dll", heapFunction);
  }

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
  private void EmitCallRuntimeLabel(string label) {
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
    EmitCallImport("ws2_32.dll", "WSAStartup");
    // Set init flag
    EmitMovRegImm(X86Register.Rax, 1);
    EmitSymdataStoreI64(X86Register.Rax, "__net_wsa_init");
    DefineLabel(skipLabel);
  }

  /// <summary>
  /// maxon_net_tcp_connect(cstring_host, port) → managed __ManagedSocket ptr, or -1 (DNS fail), -2 (connect fail).
  /// Delegates to __io_submit_sync(SyncOpNetConnect, host, port) so that blocking DNS/connect
  /// runs on the sync worker's OS thread. The sync worker returns a raw socket handle (or -1/-2).
  /// We wrap it in mm_alloc here (on the green thread) since mm_alloc is not thread-safe.
  /// Stack: [rbp-8]=host, [rbp-16]=port, [rbp-24]=raw handle
  /// </summary>
  private void EmitNetTcpConnect() {
    EmitRuntimeFunctionStart("maxon_net_tcp_connect", 2, 0x30);
    EmitMovRegImm(X86Register.Rcx, SyncOpNetConnect);   // op = 10
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);           // arg0 = cstring host
    EmitMovRegMem(X86Register.R8, -0x10, 8);            // arg1 = port
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = raw socket handle or -1 (DNS fail) or -2 (connect fail)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("l", "rt_ntc_fail");                         // negative = error, return as-is
    EmitMovMemReg(-0x18, X86Register.Rax, 8);            // save raw handle
    // Allocate __ManagedSocket via mm_alloc(8, destructor_ptr, tag_index=0)
    EmitMovRegImm(X86Register.Rcx, 8);
    EmitLeaFuncAddr(X86Register.Rdx, "__destruct___ManagedSocket");
    EmitTagZero(X86Register.R8);
    EmitCallRuntimeLabel("mm_alloc");
    // RAX = managed user pointer; store socket handle at [ptr+0]
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitBytes(0x48, 0x89, 0x08);                         // MOV [RAX], RCX
    EmitJmp("rt_ntc_done");
    DefineLabel("rt_ntc_fail");
    // RAX already has -1 or -2
    DefineLabel("rt_ntc_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_send(socket_handle, buffer_ptr, length) → bytes_sent or -1.
  /// Delegates to __io_submit_sync(SyncOpNetSend, handle, args_struct) so that
  /// blocking send() runs on the sync worker's OS thread.
  /// Stack: [rbp-8]=handle, [rbp-16]=buf, [rbp-24]=length
  /// </summary>
  private void EmitNetSend() {
    EmitRuntimeFunctionStart("maxon_net_send", 3, 0x30);
    // Allocate 16-byte args struct {buf_ptr, length}
    EmitMovRegImm(X86Register.Rcx, 16);
    EmitCallRuntimeLabel("mm_raw_alloc");
    // Store buf_ptr and length in struct
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);     // buf_ptr
    EmitBytes(0x48, 0x89, 0x08);                   // MOV [RAX], RCX
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);     // length
    EmitBytes(0x48, 0x89, 0x48, 0x08);             // MOV [RAX+8], RCX
    // Submit sync op
    EmitMovRegReg(X86Register.R8, X86Register.Rax); // arg1 = args struct ptr
    EmitMovRegImm(X86Register.Rcx, SyncOpNetSend);  // op
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);       // arg0 = socket handle
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = bytes sent or SOCKET_ERROR (-1)
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_recv(socket_handle, buffer_ptr, capacity) → bytes_received, 0=closed, -1=error.
  /// Delegates to __io_submit_sync(SyncOpNetRecv, handle, args_struct) so that
  /// blocking recv() runs on the sync worker's OS thread.
  /// Stack: [rbp-8]=handle, [rbp-16]=buf, [rbp-24]=capacity
  /// </summary>
  private void EmitNetRecv() {
    EmitRuntimeFunctionStart("maxon_net_recv", 3, 0x30);
    // Allocate 16-byte args struct {buf_ptr, capacity}
    EmitMovRegImm(X86Register.Rcx, 16);
    EmitCallRuntimeLabel("mm_raw_alloc");
    // Store buf_ptr and capacity in struct
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);     // buf_ptr
    EmitBytes(0x48, 0x89, 0x08);                   // MOV [RAX], RCX
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);     // capacity
    EmitBytes(0x48, 0x89, 0x48, 0x08);             // MOV [RAX+8], RCX
    // Submit sync op
    EmitMovRegReg(X86Register.R8, X86Register.Rax); // arg1 = args struct ptr
    EmitMovRegImm(X86Register.Rcx, SyncOpNetRecv);  // op
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);       // arg0 = socket handle
    EmitCallRuntimeLabel("__io_submit_sync");
    // RAX = bytes received, 0 = closed, -1 = error
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_net_close(socket_handle) → void. Idempotent: does nothing if handle is 0.
  /// Delegates to __io_submit_sync(SyncOpNetClose, handle, 0) so that
  /// closesocket() runs on the sync worker's OS thread.
  /// </summary>
  private void EmitNetClose() {
    EmitRuntimeFunctionStart("maxon_net_close", 1, 0x20);
    EmitMovRegImm(X86Register.Rcx, SyncOpNetClose);    // op
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);          // arg0 = socket handle
    EmitXorRegReg(X86Register.R8, X86Register.R8);     // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __destruct___ManagedSocket(user_ptr) → void.
  /// Called by mm_decref when refcount hits 0. Reads _handle at [user_ptr+0],
  /// calls closesocket if non-zero, then zeros the handle for idempotency.
  /// </summary>
  private void EmitNetSocketDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedSocket", 1, 0x30);

    EmitMovRegMem(X86Register.Rax, -0x08, 8);     // RAX = user_ptr
    EmitBytes(0x48, 0x8B, 0x08);                   // MOV RCX, [RAX] = _handle
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_nsd_done");
    // Zero the handle before closing (idempotency)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);     // RAX = user_ptr
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitBytes(0x48, 0x89, 0x10);                   // MOV [RAX], RDX = 0
    // closesocket(handle) — RCX already has the handle
    EmitCallImport("ws2_32.dll", "closesocket");

    DefineLabel("rt_nsd_done");
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
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
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
  /// Called by mm_decref when refcount hits 0. Reads _handle at [user_ptr+0],
  /// routes CloseHandle through sync worker, then zeros the handle for idempotency.
  /// </summary>
  private void EmitFileDestructor() {
    EmitRuntimeFunctionStart("__destruct___ManagedFile", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);         // RAX = user_ptr
    EmitBytes(0x48, 0x8B, 0x08);                       // MOV RCX, [RAX] = _handle
    EmitTestRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitJcc("z", "rt_mfd_done");
    // Zero the handle before closing (idempotency)
    EmitMovRegMem(X86Register.Rax, -0x08, 8);         // RAX = user_ptr
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitBytes(0x48, 0x89, 0x10);                       // MOV [RAX], RDX = 0
    // Route CloseHandle through sync worker (RCX = handle from above)
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx);  // arg0 = handle
    EmitMovRegImm(X86Register.Rcx, SyncOpCloseHandle); // op = 9
    EmitXorRegReg(X86Register.R8, X86Register.R8);     // arg1 = 0
    EmitCallRuntimeLabel("__io_submit_sync");
    DefineLabel("rt_mfd_done");
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
    EmitCallImport("kernel32.dll", "FindFirstFileA");

    // Check for INVALID_HANDLE_VALUE (-1)
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ne", "rt_mdos_found");

    // Not found: decref block (frees since rc drops to 0), return 0
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
    EmitCallImport("kernel32.dll", "FindClose");
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
    EmitCallImport("kernel32.dll", "FindClose");
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

  private const int GtOffRsp        = 0x00;
  private const int GtOffRbp        = 0x08;
  private const int GtOffStatus     = 0x10;
  private const int GtOffStackBase  = 0x18;
  private const int GtOffStackSize  = 0x20;
  private const int GtOffResult     = 0x28;
  private const int GtOffWaiter     = 0x30;
  private const int GtOffNext       = 0x38;
  private const int GtOffFuncPtr    = 0x40;
  private const int GtOffArgBuf     = 0x48;
  private const int GtOffStackGuard = 0x50;
  // I/O and async fields (added for async filesystem support)
  private const int GtOffThrew       = 0x58; // 0=success, 1=threw (for async throws)
  private const int GtOffIoResultVal = 0x60; // raw result value (bytes transferred, etc.)
  private const int GtOffIoResultLen = 0x68; // byte count for read results
  private const int GtOffIoErrorCode = 0x70; // 0=success, non-zero=Win32 error
  private const int GtOffCancelFlag  = 0x78; // 0=live, 1=cancel-requested
  private const int GtOffIoHandle    = 0x80; // HANDLE of in-flight I/O (for CancelIoEx)
  private const int GtOffAllNext     = 0x88; // next ptr in global all-threads list
  private const int GtOffTibStackBase = 0x90; // saved TIB StackBase (gs:[0x08])
  private const int GtOffTibStackLimit= 0x98; // saved TIB StackLimit (gs:[0x10])
  private const int GtOffTraceId      = 0xA0; // async trace ID (only meaningful when --async-trace)
  private const int GtStructSize     = 0xA8; // 168 bytes

  private const int GtStatusRunning  = 1;
  private const int GtStatusCompleted= 2;
  private const int GtStatusWaiting  = 3;

  private const int GtInitialStackSize = 0x10000; // 64KB
  private const int GtStackGuardMargin = 0x4000;  // 16KB guard zone

  private void EmitGreenThreadRuntime() {
    // Define scheduler globals in .data section
    // __gt_main_thread is an inline 88-byte struct, initialized to zero
    // (stackguard=0 means main thread's stack check always passes)
    DefineGlobal("__gt_main_thread", GtStructSize, 0);
    // __gt_current starts pointing to __gt_main_thread (set by __gt_init)
    DefineGlobal("__gt_current", 8, 0);
    DefineGlobal("__gt_run_queue_head", 8, 0);
    DefineGlobal("__gt_run_queue_tail", 8, 0);
    DefineGlobal("__gt_live_count", 8, 0); // count of non-completed green threads (excludes main thread)
    DefineGlobal("__gt_all_head", 8, 0);   // head of all-live-threads singly-linked list

    if (Compiler.AsyncTrace) {
      DefineGlobal("__gt_trace_counter", 8, 0);
      DefineSymdata("__at_tag_spawn", "spawn #\0"u8.ToArray());
      DefineSymdata("__at_tag_await", "await #\0"u8.ToArray());
      DefineSymdata("__at_tag_await_yield", " [yield]\0"u8.ToArray());
      DefineSymdata("__at_tag_await_imm", " [immediate]\0"u8.ToArray());
      DefineSymdata("__at_tag_try_await", "try_await #\0"u8.ToArray());
      DefineSymdata("__at_tag_cancel", "cancel #\0"u8.ToArray());
      DefineSymdata("__at_tag_io_yield", "io_yield #\0"u8.ToArray());
      DefineSymdata("__at_tag_io_resume", "io_resume #\0"u8.ToArray());
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
      DefineSymdata("__at_io_op_close_handle", " [close_handle]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_connect", " [net_connect]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_send", " [net_send]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_recv", " [net_recv]\0"u8.ToArray());
      DefineSymdata("__at_io_op_net_close", " [net_close]\0"u8.ToArray());
    }

    EmitGtInit();
    EmitGtSpawn();
    EmitGtTrampoline();
    EmitGtCancel();
    EmitGtContextSwitch();
    EmitGtAwait();
    EmitGtTryAwait();
    EmitGtYield();
    EmitGtEnqueue();
    EmitGtDequeue();
    EmitGtCleanup();
    EmitGtMorestack();
  }

  /// <summary>
  /// __gt_init(): Initialize the main thread's GreenThread struct.
  /// Called from _start before main.
  /// Sets status=running, stackguard=0, stores to __gt_current.
  /// </summary>
  private void EmitGtInit() {
    EmitRuntimeFunctionStart("__gt_init", 0, 0x30);

    // LEA rax, [rip + __gt_main_thread]
    EmitGlobalLeaReg(X86Register.Rax, "__gt_main_thread");
    // Set status = running (1)
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    // stackguard is already 0 from zero-initialized .data

    // Save the OS thread's TIB StackBase and StackLimit into the main thread struct.
    // When we context-switch away from main, these values will be saved/restored
    // so that Win32 APIs on green threads see correct stack bounds.
    // MOV RCX, gs:[0x08]  (TIB StackBase)
    EmitBytes(0x65, 0x48, 0x8B, 0x0C, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffTibStackBase, X86Register.Rcx);
    // MOV RCX, gs:[0x10]  (TIB StackLimit)
    EmitBytes(0x65, 0x48, 0x8B, 0x0C, 0x25, 0x10, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffTibStackLimit, X86Register.Rcx);

    // Store to __gt_current
    EmitGlobalStoreReg(X86Register.Rax, "__gt_current");

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

    // Allocate GreenThread struct via mm_raw_alloc
    EmitMovRegImm(X86Register.Rcx, GtStructSize);
    EmitCallRuntimeLabel("mm_raw_alloc");
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save gt_ptr

    // Allocate initial stack via VirtualAlloc(NULL, size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);        // lpAddress = NULL
    EmitMovRegImm(X86Register.Rdx, GtInitialStackSize);       // dwSize = 64KB
    EmitMovRegImm(X86Register.R8, 0x3000);                    // flAllocationType = MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                      // flProtect = PAGE_READWRITE
    EmitCallImport("kernel32.dll", "VirtualAlloc");
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

    // Add to all-threads list (prepend at head): gt.all_next = __gt_all_head; __gt_all_head = gt
    EmitMovRegMem(gt, -0x20, 8); // reload gt
    EmitGlobalLoadReg(X86Register.Rax, "__gt_all_head");
    EmitMovIndirectMemReg(gt, GtOffAllNext, X86Register.Rax);
    EmitGlobalStoreReg(gt, "__gt_all_head");

    // Increment live thread count
    EmitGlobalLoadReg(X86Register.Rax, "__gt_live_count");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__gt_live_count");

    // Enqueue: call __gt_enqueue(gt)
    EmitMovRegReg(X86Register.Rcx, gt);
    EmitCallRuntimeLabel("__gt_enqueue");

    // Return gt_ptr as the promise
    EmitMovRegMem(X86Register.Rax, -0x20, 8);

    if (Compiler.AsyncTrace) {
      // Assign trace ID: counter++, store in gt.trace_id
      EmitMovMemReg(-0x30, X86Register.Rax, 8); // save gt_ptr (RAX)
      EmitGlobalLoadReg(X86Register.Rcx, "__gt_trace_counter");
      EmitAddRegImm(X86Register.Rcx, 1);
      EmitGlobalStoreReg(X86Register.Rcx, "__gt_trace_counter");
      EmitMovRegMem(X86Register.Rax, -0x20, 8); // gt_ptr
      EmitMovIndirectMemReg(X86Register.Rax, GtOffTraceId, X86Register.Rcx);
      // Trace: "spawn #N\n"
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_spawn");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x20, 8);
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
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
    // Set up a frame for our local use
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x60); // local frame

    // Load current GreenThread
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
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

    // Load args from buffer into calling convention registers
    // Args are at [arg_buf + 8 + i*8]
    // Calling convention: rcx, rdx, r8, r9, rsi, rdi, rax, rbx
    // We load up to 8 args (matching calling convention register count)
    // Use R10 for arg_count to avoid clobbering calling convention registers
    EmitMovRegMem(X86Register.R10, -0x20, 8); // R10 = arg_count (persists across loop)
    for (int i = 0; i < 8; i++) {
      var skipLabel = $"__gt_tramp_skip_arg{i}";
      // if arg_count <= i, skip
      EmitCmpRegImm(X86Register.R10, i + 1);
      EmitJcc("l", skipLabel); // skip if arg_count < i+1
      // Load arg[i] from [arg_buf + 8 + i*8]
      EmitMovRegMem(X86Register.R11, -0x18, 8); // R11 = arg_buf
      EmitMovRegIndirectMem(_abiArgRegs[i], X86Register.R11, 8 + i * 8);
      DefineLabel(skipLabel);
    }

    // Free arg_buf via mm_raw_free before calling target
    // Save loaded args on stack first
    for (int i = 0; i < 8; i++) {
      EmitMovMemReg(-0x28 - i * 8, _abiArgRegs[i], 8);
    }
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // rcx = arg_buf
    EmitCallRuntimeLabel("mm_raw_free");
    // Restore args
    for (int i = 0; i < 8; i++) {
      EmitMovRegMem(_abiArgRegs[i], -0x28 - i * 8, 8);
    }

    // Call target function
    EmitMovRegMem(X86Register.R10, -0x10, 8); // R10 = func_ptr
    // call r10
    EmitBytes(0x41, 0xFF, 0xD2); // CALL R10

    // Store result to gt.result and error flag to gt.threw
    // RDX holds the error flag (0=success, non-zero=threw) from throwing functions.
    // For non-throwing functions, RDX is undefined but gt.threw is initialized to 0
    // by __gt_spawn, so we unconditionally store RDX — harmless for non-throwing calls.
    EmitMovMemReg(-0x58, X86Register.Rax, 8); // [rbp-0x58] = result (save)
    EmitMovMemReg(-0x60, X86Register.Rdx, 8); // [rbp-0x60] = threw (save)
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitMovRegMem(X86Register.Rax, -0x58, 8);
    EmitMovIndirectMemReg(X86Register.R10, GtOffResult, X86Register.Rax);
    EmitMovRegMem(X86Register.Rax, -0x60, 8);
    EmitMovIndirectMemReg(X86Register.R10, GtOffThrew, X86Register.Rax);

    // Decrement live thread count
    EmitGlobalLoadReg(X86Register.Rax, "__gt_live_count");
    EmitSubRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__gt_live_count");

    // Remove from all-threads list: walk __gt_all_head to find this thread and unlink
    // R10 = current gt (still loaded above)
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

    // Reload R10 = current gt (list walk may have modified RCX/RDX)
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");

    // Set status = completed
    EmitMovRegImm(X86Register.Rax, GtStatusCompleted);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax);

    // Wake waiter if any: if (gt.waiter != 0) { waiter.status = ready; enqueue(waiter) }
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R10, GtOffWaiter);
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", "__gt_tramp_no_waiter");
    // waiter.status = ready
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R11, GtOffStatus, X86Register.Rax);
    // enqueue waiter
    EmitMovRegReg(X86Register.Rcx, X86Register.R11);
    EmitCallRuntimeLabel("__gt_enqueue");
    DefineLabel("__gt_tramp_no_waiter");

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

    // Save TIB StackBase (gs:[0x08]) and StackLimit (gs:[0x10]) to 'from'.
    // Win32 APIs use these for stack overflow detection (__chkstk) and SEH;
    // without updating them, calling Win32 on a green thread's stack crashes.
    // MOV RAX, gs:[0x08]
    EmitBytes(0x65, 0x48, 0x8B, 0x04, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffTibStackBase, X86Register.Rax);
    // MOV RAX, gs:[0x10]
    EmitBytes(0x65, 0x48, 0x8B, 0x04, 0x25, 0x10, 0x00, 0x00, 0x00);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffTibStackLimit, X86Register.Rax);

    // Restore RSP and RBP from 'to'
    EmitMovRegIndirectMem(X86Register.Rsp, X86Register.Rdx, GtOffRsp);
    EmitMovRegIndirectMem(X86Register.Rbp, X86Register.Rdx, GtOffRbp);

    // Restore TIB StackBase and StackLimit from 'to'
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdx, GtOffTibStackBase);
    // MOV gs:[0x08], RAX
    EmitBytes(0x65, 0x48, 0x89, 0x04, 0x25, 0x08, 0x00, 0x00, 0x00);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rdx, GtOffTibStackLimit);
    // MOV gs:[0x10], RAX
    EmitBytes(0x65, 0x48, 0x89, 0x04, 0x25, 0x10, 0x00, 0x00, 0x00);

    // Update __gt_current = to
    EmitGlobalStoreReg(X86Register.Rdx, "__gt_current");

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
    EmitGlobalLoadReg(X86Register.R11, "__gt_current"); // R11 = current
    // current.status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R11, GtOffStatus, X86Register.Rax);

    // promise.waiter = current
    EmitMovRegMem(X86Register.R10, -0x08, 8); // R10 = promise
    EmitMovIndirectMemReg(X86Register.R10, GtOffWaiter, X86Register.R11);

    // Dequeue next runnable thread
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__gt_await_has_next");

    // No runnable thread: wait for I/O completions to wake a thread
    DefineLabel("__gt_await_wait");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF); // INFINITE
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_await_wait"); // still nothing → keep waiting

    DefineLabel("__gt_await_has_next");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save next

    // Set next.status = running
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);

    // Context switch: from=current, to=next
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current"); // from = current
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);          // to = next
    EmitCallRuntimeLabel("__gt_context_switch");

    // We resume here after being woken up
    // Fall through to __gt_await_done

    DefineLabel("__gt_await_done");

    if (Compiler.AsyncTrace) {
      // Trace: "await #N [immediate]\n" or "await #N [yield]\n"
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
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
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
    EmitCallImport("kernel32.dll", "VirtualFree");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result

    DefineLabel("__gt_await_skip_free_stack");
    // Save result, free the GreenThread struct
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // rcx = promise (gt struct)
    EmitCallRuntimeLabel("mm_raw_free");
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
    EmitGlobalLoadReg(X86Register.R11, "__gt_current"); // R11 = current
    // current.status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R11, GtOffStatus, X86Register.Rax);

    // promise.waiter = current
    EmitMovRegMem(X86Register.R10, -0x08, 8); // R10 = promise
    EmitMovIndirectMemReg(X86Register.R10, GtOffWaiter, X86Register.R11);

    // Dequeue next runnable thread
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__gt_try_await_has_next");

    // No runnable thread: wait for I/O completions to wake a thread
    DefineLabel("__gt_try_await_wait");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF); // INFINITE
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_try_await_wait"); // still nothing → keep waiting

    DefineLabel("__gt_try_await_has_next");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save next

    // Set next.status = running
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);

    // Context switch: from=current, to=next
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current"); // from = current
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);          // to = next
    EmitCallRuntimeLabel("__gt_context_switch");

    // We resume here after being woken up
    DefineLabel("__gt_try_await_done");

    if (Compiler.AsyncTrace) {
      // Trace: "try_await #N [immediate]\n" or "try_await #N [yield]\n"
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
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
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
    EmitCallImport("kernel32.dll", "VirtualFree");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // restore threw

    DefineLabel("__gt_try_await_skip_free_stack");
    // Save result + threw, free the GreenThread struct
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result
    EmitMovMemReg(-0x18, X86Register.Rdx, 8); // save threw
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // rcx = promise (gt struct)
    EmitCallRuntimeLabel("mm_raw_free");
    EmitMovRegMem(X86Register.Rax, -0x10, 8); // restore result
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // restore threw

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_yield(): Yield current thread — enqueue self and switch to next.
  /// Called from __gt_yield_completed for completed threads.
  /// </summary>
  private void EmitGtYield() {
    // __gt_yield_completed: for threads that are done (don't enqueue self)
    DefineLabel("__gt_yield_completed");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x30);

    // Drain any I/O completions first — may re-enqueue blocked threads
    EmitCallRuntimeLabel("__io_check_completions");

    // Try to dequeue next runnable thread
    EmitCallRuntimeLabel("__gt_dequeue");
    // If no more threads, switch back to main thread
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "__gt_yield_has_next");

    // No more threads: switch back to main thread
    EmitGlobalLeaReg(X86Register.Rdx, "__gt_main_thread");
    // If current IS the main thread, just return
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitCmpRegReg(X86Register.Rcx, X86Register.Rdx);
    EmitJcc("z", "__gt_yield_return");
    // Switch to main
    EmitMovRegImm(X86Register.Rax, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rdx, GtOffStatus, X86Register.Rax);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp("__gt_yield_return");

    DefineLabel("__gt_yield_has_next");
    // next.status = running
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save next
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);

    // context switch: from=current, to=next
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitCallRuntimeLabel("__gt_context_switch");

    DefineLabel("__gt_yield_return");
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
  }

  /// <summary>
  /// __gt_enqueue(gt_rcx): Add a GreenThread to the tail of the run queue.
  /// </summary>
  private void EmitGtEnqueue() {
    EmitRuntimeFunctionStart("__gt_enqueue", 1, 0x20);
    // [rbp-0x08] = gt

    // gt.next = 0
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffNext, X86Register.Rax);

    // if tail == NULL: head = tail = gt
    EmitGlobalLoadReg(X86Register.Rax, "__gt_run_queue_tail");
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "__gt_enqueue_append");

    // Empty queue: set both head and tail
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitGlobalStoreReg(X86Register.R10, "__gt_run_queue_head");
    EmitGlobalStoreReg(X86Register.R10, "__gt_run_queue_tail");
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_enqueue_append");
    // tail.next = gt
    EmitGlobalLoadReg(X86Register.Rax, "__gt_run_queue_tail");
    EmitMovRegMem(X86Register.R10, -0x08, 8);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffNext, X86Register.R10);
    // tail = gt
    EmitGlobalStoreReg(X86Register.R10, "__gt_run_queue_tail");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_dequeue() -> GreenThread* in RAX (or NULL if queue empty).
  /// Removes and returns the head of the run queue.
  /// </summary>
  private void EmitGtDequeue() {
    EmitRuntimeFunctionStart("__gt_dequeue", 0, 0x20);

    EmitGlobalLoadReg(X86Register.Rax, "__gt_run_queue_head");
    // If head == NULL, return NULL
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("nz", "__gt_dequeue_nonempty");
    // Return NULL
    EmitRuntimeFunctionEnd();

    DefineLabel("__gt_dequeue_nonempty");
    // head = head.next
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffNext);
    EmitGlobalStoreReg(X86Register.Rcx, "__gt_run_queue_head");
    // If new head == NULL, clear tail too
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "__gt_dequeue_done");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitGlobalStoreReg(X86Register.Rcx, "__gt_run_queue_tail");
    DefineLabel("__gt_dequeue_done");
    // Clear the dequeued node's next pointer
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffNext, X86Register.Rcx);
    // RAX = dequeued gt
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
      // Trace: "cancel #N\n"
      EmitMovMemReg(-0x08, X86Register.Rcx, 8); // save rcx (gt ptr)
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_cancel");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // gt ptr
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
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
    EmitCallImport("kernel32.dll", "CancelIoEx");
    DefineLabel("__gt_cancel_no_io");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// __gt_cleanup(): Called from _start after main returns.
  /// Step 1: Cancel all live green threads (sets cancel_flag, aborts in-flight I/O).
  /// Step 2: Drain — run queued threads to completion; wait on I/O completions if threads
  ///         are still alive but the run queue is empty (I/O-blocked threads).
  /// Requires __io_init to have been called before main (I/O subsystem must be alive).
  /// </summary>
  private void EmitGtCleanup() {
    EmitRuntimeFunctionStart("__gt_cleanup", 0, 0x30);

    // --- Step 1: Cancel all live threads ---
    // Walk __gt_all_head linked list and call __gt_cancel on each
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_all_head");
    DefineLabel("__gt_cleanup_cancel_loop");
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("z", "__gt_cleanup_drain");
    // Save RCX (current gt) across call, then advance to next
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // save current gt
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rcx, GtOffAllNext); // next ptr
    EmitCallRuntimeLabel("__gt_cancel"); // __gt_cancel(rcx=gt)
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // reload current gt
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rcx, GtOffAllNext); // advance
    EmitJmp("__gt_cleanup_cancel_loop");

    // --- Step 2: Drain run queue ---
    DefineLabel("__gt_cleanup_drain");
    // Check for I/O completions first (may re-enqueue I/O-blocked threads)
    EmitCallRuntimeLabel("__io_check_completions");

    // Try to dequeue a runnable thread
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_check_live");

    // Run the thread: set status=running, context switch to it
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save next
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    // Resume here when thread completes/yields back
    EmitJmp("__gt_cleanup_drain");

    // Run queue empty — check if any threads still alive (I/O-blocked)
    DefineLabel("__gt_cleanup_check_live");
    EmitGlobalLoadReg(X86Register.Rax, "__gt_live_count");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_done"); // live_count == 0 → all done

    // Threads still alive but nothing runnable — wait for next I/O completion.
    // Guard: only wait if I/O subsystem is initialized (__io_done_event != 0).
    EmitGlobalLoadReg(X86Register.Rax, "__io_done_event");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__gt_cleanup_done"); // no I/O subsystem + stuck threads = bail
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // hHandle = __io_done_event
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);       // dwMilliseconds = INFINITE
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    EmitJmp("__gt_cleanup_drain");

    DefineLabel("__gt_cleanup_done");
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

    // Push callee-saved registers first, then all argument registers.
    // Push order: RBP, RBX, R12, R13, R14, R15, RCX, RDX, R8, R9, RSI, RDI, RAX
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

    // Stack alignment: CALL (8) + 13 pushes (104) = 112 bytes.
    // RSP at morestack entry is 16-byte aligned (two CALLs from aligned state).
    // 112 mod 16 = 0, so RSP after pushes is aligned. But 104 mod 16 = 8, so
    // RSP is actually misaligned by 8. We need SUB 0x28 (40 = 32 shadow + 8 pad).
    EmitSubRegImm(X86Register.Rsp, 0x28);

    // Load current GreenThread
    EmitGlobalLoadReg(X86Register.Rbx, "__gt_current"); // RBX = gt (callee-saved, preserved across calls)

    // R12 = old_base, R13 = old_size (callee-saved, preserved)
    EmitMovRegIndirectMem(X86Register.R12, X86Register.Rbx, GtOffStackBase);
    EmitMovRegIndirectMem(X86Register.R13, X86Register.Rbx, GtOffStackSize);

    // R14 = new_size = old_size * 2
    EmitMovRegReg(X86Register.R14, X86Register.R13);
    EmitBytes(0x49, 0xD1, 0xE6); // SHL R14, 1

    // VirtualAlloc(NULL, new_size, MEM_COMMIT|MEM_RESERVE, PAGE_READWRITE)
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovRegReg(X86Register.Rdx, X86Register.R14);
    EmitMovRegImm(X86Register.R8, 0x3000);
    EmitMovRegImm(X86Register.R9, 0x04);
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    EmitMovRegReg(X86Register.R15, X86Register.Rax); // R15 = new_base

    // Copy old stack to TOP of new stack:
    // dst = new_base + new_size - old_size
    // src = old_base
    // count = old_size
    EmitMovRegReg(X86Register.Rcx, X86Register.R15);  // dst = new_base
    EmitAddRegReg(X86Register.Rcx, X86Register.R14);   // + new_size
    EmitSubRegReg(X86Register.Rcx, X86Register.R13);   // - old_size
    EmitMovRegReg(X86Register.Rdx, X86Register.R12);   // src = old_base
    EmitMovRegReg(X86Register.R8, X86Register.R13);     // count = old_size
    EmitCallRuntimeLabel("maxon_memcpy");

    // Compute offset = (new_base + new_size - old_size) - old_base
    EmitMovRegReg(X86Register.Rax, X86Register.R15);  // new_base
    EmitAddRegReg(X86Register.Rax, X86Register.R14);   // + new_size
    EmitSubRegReg(X86Register.Rax, X86Register.R13);   // - old_size
    EmitSubRegReg(X86Register.Rax, X86Register.R12);   // - old_base = offset
    // RAX = offset

    // Adjust RSP by offset (moves to the corresponding position on the new stack)
    EmitAddRegReg(X86Register.Rsp, X86Register.Rax);

    // Adjust RBP if it's within the old stack range [old_base, old_base + old_size)
    EmitMovRegReg(X86Register.Rcx, X86Register.R12);
    EmitAddRegReg(X86Register.Rcx, X86Register.R13); // RCX = old_top
    EmitCmpRegReg(X86Register.Rbp, X86Register.R12);
    EmitJcc("b", "__gt_morestack_skip_rbp");
    EmitCmpRegReg(X86Register.Rbp, X86Register.Rcx);
    EmitJcc("ae", "__gt_morestack_skip_rbp");
    EmitAddRegReg(X86Register.Rbp, X86Register.Rax);
    DefineLabel("__gt_morestack_skip_rbp");

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

    // Update GreenThread struct
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

    // VirtualFree(old_base, 0, MEM_RELEASE)
    EmitMovRegReg(X86Register.Rcx, X86Register.R12);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovRegImm(X86Register.R8, 0x8000);
    EmitCallImport("kernel32.dll", "VirtualFree");

    // Tear down frame and restore all registers from the new stack.
    // After ADD RSP, 0x28: stack layout (bottom to top) is:
    //   [RSP+0]=RAX [RSP+8]=RDI [RSP+16]=RSI [RSP+24]=R9 [RSP+32]=R8
    //   [RSP+40]=RDX [RSP+48]=RCX [RSP+56]=R15 [RSP+64]=R14 [RSP+72]=R13
    //   [RSP+80]=R12 [RSP+88]=RBX [RSP+96]=RBP
    // Fix the saved RBP value to the adjusted RBP before popping.
    EmitAddRegImm(X86Register.Rsp, 0x28);
    EmitMovIndirectMemReg(X86Register.Rsp, 96, X86Register.Rbp); // fix saved RBP

    // Pop volatile (argument) registers in reverse push order
    EmitPopReg(X86Register.Rax);
    EmitPopReg(X86Register.Rdi);
    EmitPopReg(X86Register.Rsi);
    EmitPopReg(X86Register.R9);
    EmitPopReg(X86Register.R8);
    EmitPopReg(X86Register.Rdx);
    EmitPopReg(X86Register.Rcx);

    // Pop callee-saved registers
    EmitPopReg(X86Register.R15);
    EmitPopReg(X86Register.R14);
    EmitPopReg(X86Register.R13);
    EmitPopReg(X86Register.R12);
    EmitPopReg(X86Register.Rbx);
    EmitPopReg(X86Register.Rbp);

    // The relocated return address is now at [RSP]. 'ret' will pop it and jump there.
    EmitByte(0xC3); // ret
  }

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

  private const int AsyncCtxSize     = 0x38; // 56 bytes (OVERLAPPED=32 + 3 fields)
  private const int AsyncCtxOffWaiter   = 0x20;

  private const int SyncReqSize      = 0x28; // 40 bytes
  private const int SyncReqOffOp     = 0x00;
  private const int SyncReqOffArg0   = 0x08;
  private const int SyncReqOffArg1   = 0x10;
  private const int SyncReqOffWaiter = 0x18;
  private const int SyncReqOffNext   = 0x20;

  private const int IoCompSize       = 0x28; // 40 bytes
  private const int IoCompOffWaiter  = 0x00;
  private const int IoCompOffResult  = 0x08;
  private const int IoCompOffLen     = 0x10;
  private const int IoCompOffError   = 0x18;
  private const int IoCompOffNext    = 0x20;

  // Sync op codes (must match SyncOpXxx used in stubs)
  private const long SyncOpFileExists  = 0;
  private const long SyncOpFileDelete  = 1;
  private const long SyncOpFindFirst   = 2;
  private const long SyncOpFindNext    = 3;
  private const long SyncOpDirExists   = 4;
  private const long SyncOpDirCreate   = 5;
  private const long SyncOpGetCwd      = 6;
  private const long SyncOpFileOpenRead  = 7;
  private const long SyncOpFileOpenWrite = 8;
  private const long SyncOpCloseHandle   = 9;
  private const long SyncOpNetConnect  = 10;
  private const long SyncOpNetSend     = 11;
  private const long SyncOpNetRecv     = 12;
  private const long SyncOpNetClose    = 13;
  private const long SyncOpShutdown    = 0xFF;

  private void EmitIoRuntime() {
    // I/O globals
    DefineGlobal("__io_iocp",              8,  0); // HANDLE: I/O completion port
    DefineGlobal("__io_completion_thread", 8,  0); // HANDLE: IOCP drain thread
    DefineGlobal("__io_sync_worker",       8,  0); // HANDLE: sync-op worker thread
    DefineGlobal("__io_sync_cs",          40,  0); // CRITICAL_SECTION (40 bytes on x64)
    DefineGlobal("__io_sync_req_event",    8,  0); // manual-reset event: sync work available
    DefineGlobal("__io_sync_req_head",     8,  0); // SyncRequest* head
    DefineGlobal("__io_sync_req_tail",     8,  0); // SyncRequest* tail
    DefineGlobal("__io_done_cs",          40,  0); // CRITICAL_SECTION for done queue
    DefineGlobal("__io_done_event",        8,  0); // auto-reset event: completion available
    DefineGlobal("__io_done_head",         8,  0); // IoCompletion* head
    DefineGlobal("__io_done_tail",         8,  0); // IoCompletion* tail

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
    EmitRuntimeFunctionStart("__io_init", 0, 0x40);

    // CreateIoCompletionPort(INVALID_HANDLE_VALUE=-1, NULL, 0, 1) → __io_iocp
    EmitMovRegImm(X86Register.Rcx, unchecked((long)0xFFFFFFFFFFFFFFFF)); // INVALID_HANDLE_VALUE
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // ExistingCompletionPort = NULL
    EmitXorRegReg(X86Register.R8,  X86Register.R8);  // CompletionKey = 0
    EmitMovRegImm(X86Register.R9,  1);               // NumberOfConcurrentThreads = 1
    EmitCallImport("kernel32.dll", "CreateIoCompletionPort");
    EmitGlobalStoreReg(X86Register.Rax, "__io_iocp");

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
    EmitXorRegReg(X86Register.R8,  X86Register.R8);  // dwCompletionKey = 0
    EmitXorRegReg(X86Register.R9,  X86Register.R9);  // lpOverlapped = NULL
    EmitCallImport("kernel32.dll", "PostQueuedCompletionStatus");

    // Send shutdown SyncRequest to sync worker: op=0xFF, then SetEvent
    EmitMovRegImm(X86Register.Rcx, SyncReqSize);
    EmitCallRuntimeLabel("mm_raw_alloc"); // HEAP_ZERO_MEMORY — already zeroed
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

    // ctx = (AsyncOpContext*)overlapped
    // Allocate IoCompletion and fill it
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save ctx
    EmitMovRegImm(X86Register.Rcx, IoCompSize);
    EmitCallRuntimeLabel("mm_raw_alloc");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // save comp

    // comp->waiter_gt = ctx->waiter_gt
    EmitMovRegMem(X86Register.Rcx, -0x20, 8); // ctx
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rcx, AsyncCtxOffWaiter);
    EmitMovRegMem(X86Register.Rax, -0x28, 8); // comp
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffWaiter, X86Register.Rdx);

    // comp->result_val = bytes_transferred (from [rbp-0x08])
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // bytes
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffResult, X86Register.Rdx);

    // comp->result_len = bytes_transferred
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffLen, X86Register.Rdx);

    // comp->error_code = 0 (GQCS returns success if it filled overlapped)
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffError, X86Register.Rdx);

    // comp->next = 0
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffNext, X86Register.Rdx);

    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallRuntimeLabel("mm_raw_free");

    // Enqueue comp to done queue and signal done event
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // comp
    EmitCallRuntimeLabel("__io_enqueue_completion");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitCallImport("kernel32.dll", "SetEvent");

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
  private void EmitIoSyncWorkerLoop() {
    DefineLabel("__io_sync_worker_loop");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x260);
    // Locals: [rbp-0x08]=req ptr, [rbp-0x10]=result, [rbp-0x18]=comp ptr
    // Extended frame for net_connect: WSADATA(408b), hints(48b), sockaddr_in(16b)

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
    EmitJmp("__io_sync_op_done"); // result in RAX (0=failure, nonzero=success)

    // --- findFirst: FindFirstFileA(arg0=pattern, arg1=WIN32_FIND_DATA*) → HANDLE ---
    DefineLabel("__io_sync_op_find_first");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // pattern
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, SyncReqOffArg1); // WIN32_FIND_DATA*
    EmitCallImport("kernel32.dll", "FindFirstFileA");
    EmitJmp("__io_sync_op_done"); // INVALID_HANDLE_VALUE on failure

    // --- findNext: FindNextFileA(arg0=HANDLE, arg1=WIN32_FIND_DATA*) → BOOL ---
    DefineLabel("__io_sync_op_find_next");
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, SyncReqOffArg0); // HANDLE
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rax, SyncReqOffArg1); // WIN32_FIND_DATA*
    EmitCallImport("kernel32.dll", "FindNextFileA");
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
    EmitJmp("__io_sync_op_done");

    // --- getCwd: GetCurrentDirectoryA(260, heap_buf) → raw ptr ---
    DefineLabel("__io_sync_op_get_cwd");
    // Allocate 260-byte buffer via HeapAlloc
    EmitCallRuntimeLabel("mm_raw_alloc_260");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save buf
    EmitMovRegImm(X86Register.Rcx, 260); // nBufferLength
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // lpBuffer
    EmitCallImport("kernel32.dll", "GetCurrentDirectoryA");
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
    EmitMovRegImm(X86Register.Rax, -1);
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

    // socket(AF_INET=2, SOCK_STREAM=1, IPPROTO_TCP=6)
    EmitMovRegImm(X86Register.Rcx, 2);
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitMovRegImm(X86Register.R8, 6);
    EmitCallImport("ws2_32.dll", "socket");

    // Check for INVALID_SOCKET (-1)
    EmitBytes(0x48, 0x83, 0xF8, 0xFF);            // CMP RAX, -1
    EmitJcc("ne", "__io_sync_ntc_sock_ok");
    EmitMovRegImm(X86Register.Rax, -2);
    EmitJmp("__io_sync_op_done");

    DefineLabel("__io_sync_ntc_sock_ok");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);     // save socket handle

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
    EmitCallRuntimeLabel("mm_raw_free");
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
    EmitCallRuntimeLabel("mm_raw_free");
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

    // --- Common: post completion and continue ---
    DefineLabel("__io_sync_op_done");
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // save result

    // Allocate IoCompletion
    EmitMovRegImm(X86Register.Rcx, IoCompSize);
    EmitCallRuntimeLabel("mm_raw_alloc");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // comp

    // Fill comp
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // req
    EmitMovRegIndirectMem(X86Register.Rdx, X86Register.Rcx, SyncReqOffWaiter);
    EmitMovRegMem(X86Register.Rax, -0x18, 8);
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffWaiter, X86Register.Rdx);

    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // result
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffResult, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffLen, X86Register.Rdx); // len = result for simplicity
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffError, X86Register.Rdx); // error = 0
    EmitMovIndirectMemReg(X86Register.Rax, IoCompOffNext, X86Register.Rdx);  // next = 0

    // Free req
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallRuntimeLabel("mm_raw_free");

    // Enqueue comp and signal done event
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitCallRuntimeLabel("__io_enqueue_completion");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitCallImport("kernel32.dll", "SetEvent");

    EmitJmp("__io_sync_worker_top");

    // --- Shutdown: free req, exit thread ---
    DefineLabel("__io_sync_worker_shutdown");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitCallRuntimeLabel("mm_raw_free");
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
    EmitCallRuntimeLabel("mm_raw_free");

    // Re-enqueue waiter (set status=ready, add to run queue)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // gt
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.Rcx, GtOffStatus, X86Register.Rax); // status = ready
    EmitCallRuntimeLabel("__gt_enqueue");

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
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffCancelFlag);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", "__io_submit_sync_cancelled");

    // Allocate SyncRequest
    EmitMovRegImm(X86Register.Rcx, SyncReqSize);
    EmitCallRuntimeLabel("mm_raw_alloc");
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save req

    // Fill: op, arg0, arg1, waiter_gt, next=0
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // op
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffOp, X86Register.Rdx);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // arg0
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffArg0, X86Register.Rdx);
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // arg1
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffArg1, X86Register.Rdx);
    EmitGlobalLoadReg(X86Register.Rdx, "__gt_current");
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffWaiter, X86Register.Rdx);
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx);
    EmitMovIndirectMemReg(X86Register.Rax, SyncReqOffNext, X86Register.Rdx);

    // Set current thread status = waiting
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitMovRegImm(X86Register.Rdx, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rdx);

    // Enqueue the request and signal the sync worker
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallRuntimeLabel("__io_enqueue_sync_req");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitCallImport("kernel32.dll", "SetEvent");

    if (Compiler.AsyncTrace) {
      // Trace: "io_yield #N [op_name]\n"
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_yield");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitGlobalLoadReg(X86Register.Rax, "__gt_current");
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitIoTraceOpSuffix("yield");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
    }

    // Yield: dequeue next runnable thread
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("z", "__io_submit_sync_wait"); // no runnable thread → wait on event

    // Context switch to next thread
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp("__io_submit_sync_resume");

    // No runnable thread: wait on done event, drain completions, try dequeue again
    DefineLabel("__io_submit_sync_wait");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("z", "__io_submit_sync_wait"); // keep waiting if still nothing runnable
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);
    EmitCallRuntimeLabel("__gt_context_switch");

    // Resume here after being re-enqueued by __io_check_completions
    DefineLabel("__io_submit_sync_resume");

    if (Compiler.AsyncTrace) {
      // Trace: "io_resume #N [op_name]\n"
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_io_resume");
      EmitCallRuntimeLabel("mm_trace_print_tag");
      EmitGlobalLoadReg(X86Register.Rax, "__gt_current");
      EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, GtOffTraceId);
      EmitCallRuntimeLabel("mm_trace_print_i64");
      EmitIoTraceOpSuffix("resume");
      EmitLeaRegSymdataRel(X86Register.Rcx, "__at_tag_nl");
      EmitCallRuntimeLabel("mm_trace_print_tag");
    }

    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoResultVal);
    EmitRuntimeFunctionEnd();

    DefineLabel("__io_submit_sync_cancelled");
    // Store ERROR_OPERATION_ABORTED and return 0 without yielding
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
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
      (SyncOpCloseHandle,   "__at_io_op_close_handle"),
      (SyncOpNetConnect,    "__at_io_op_net_connect"),
      (SyncOpNetSend,       "__at_io_op_net_send"),
      (SyncOpNetRecv,       "__at_io_op_net_recv"),
      (SyncOpNetClose,      "__at_io_op_net_close"),
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
    EmitIoEnqueueCompletion();
    EmitIoDequeueCompletion();
    EmitMmRawAlloc260();
    EmitIoPanicIo();
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
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffCancelFlag);
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", $"{labelPrefix}_cancelled");

    // Allocate AsyncOpContext (64 bytes): OVERLAPPED at offset 0 + our fields
    EmitMovRegImm(X86Register.Rcx, AsyncCtxSize);
    EmitCallRuntimeLabel("mm_raw_alloc");
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // save ctx (mm_raw_alloc uses HEAP_ZERO_MEMORY — already zeroed)

    // ctx->waiter_gt = __gt_current
    EmitGlobalLoadReg(X86Register.Rdx, "__gt_current");
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovIndirectMemReg(X86Register.Rax, AsyncCtxOffWaiter, X86Register.Rdx);

    // Store handle in gt->io_handle so CancelIoEx can reach it
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rdx);

    // Set current thread status = waiting
    EmitMovRegImm(X86Register.Rax, GtStatusWaiting);
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rax);

    // Call ReadFile/WriteFile:
    //   handle=rcx, buf=rdx, size=r8, lpBytesTransferred=NULL, overlapped=ctx
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // handle
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // buf
    EmitMovRegMem(X86Register.R8,  -0x18, 8); // size
    EmitXorRegReg(X86Register.R9, X86Register.R9); // lpBytesTransferred = NULL
    EmitMovRegMem(X86Register.Rax, -0x20, 8);
    EmitMovMemRspReg(0x20, X86Register.Rax);   // lpOverlapped = ctx (5th arg at [rsp+0x20])
    EmitCallImport("kernel32.dll", ioFuncName);

    // If FALSE: check GetLastError — if not IO_PENDING, it's a sync error
    EmitBytes(0x48, 0x85, 0xC0); // TEST RAX, RAX
    EmitJcc("nz", $"{labelPrefix}_yield"); // TRUE → sync success (with FILE_SKIP_COMPLETION)
    // FALSE — check error code
    EmitCallImport("kernel32.dll", "GetLastError");
    EmitMovRegImm(X86Register.Rcx, 997); // ERROR_IO_PENDING
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", $"{labelPrefix}_yield"); // IO_PENDING → normal async path, yield

    // Sync error: clear io_handle, store error, return 0
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rcx); // clear io_handle
    EmitMovIndirectMemReg(X86Register.R10, GtOffStatus, X86Register.Rcx); // status = ready (0)
    // Store the error code
    EmitCallImport("kernel32.dll", "GetLastError");
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoErrorCode, X86Register.Rax);
    // Free ctx
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallRuntimeLabel("mm_raw_free");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitRuntimeFunctionEnd();

    // Yield: dequeue next runnable thread
    DefineLabel($"{labelPrefix}_yield");
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("z", $"{labelPrefix}_wait");

    // Context switch to next thread
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);
    EmitCallRuntimeLabel("__gt_context_switch");
    EmitJmp($"{labelPrefix}_resume");

    // No runnable thread: wait on done event
    DefineLabel($"{labelPrefix}_wait");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_done_event");
    EmitMovRegImm(X86Register.Rdx, 0xFFFFFFFF);
    EmitCallImport("kernel32.dll", "WaitForSingleObject");
    EmitCallRuntimeLabel("__io_check_completions");
    EmitCallRuntimeLabel("__gt_dequeue");
    EmitBytes(0x48, 0x85, 0xC0);
    EmitJcc("z", $"{labelPrefix}_wait");
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitMovRegImm(X86Register.Rcx, GtStatusRunning);
    EmitMovIndirectMemReg(X86Register.Rax, GtOffStatus, X86Register.Rcx);
    EmitGlobalLoadReg(X86Register.Rcx, "__gt_current");
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);
    EmitCallRuntimeLabel("__gt_context_switch");

    // Resume: clear io_handle, return bytes transferred
    DefineLabel($"{labelPrefix}_resume");
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitMovIndirectMemReg(X86Register.R10, GtOffIoHandle, X86Register.Rax); // clear io_handle
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.R10, GtOffIoResultVal);
    EmitRuntimeFunctionEnd();

    DefineLabel($"{labelPrefix}_cancelled");
    EmitGlobalLoadReg(X86Register.R10, "__gt_current");
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
    EmitCallImport("kernel32.dll", "EnterCriticalSection");
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
    EmitCallImport("kernel32.dll", "LeaveCriticalSection");
    EmitRuntimeFunctionEnd();
  }

  private void EmitIoDequeuesSyncReq() {
    // __io_dequeue_sync_req(): Dequeue one SyncRequest under lock; returns ptr or NULL
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
    // if new head == NULL: reset tail and reset event
    EmitBytes(0x48, 0x85, 0xC9); // TEST RCX, RCX
    EmitJcc("nz", "__io_dequeue_sync_unlock");
    EmitGlobalStoreReg(X86Register.Rcx, "__io_sync_req_tail"); // tail = NULL
    // ResetEvent(__io_sync_req_event) — queue is now empty; unlock CS first
    EmitGlobalLeaReg(X86Register.Rcx, "__io_sync_cs");
    EmitCallImport("kernel32.dll", "LeaveCriticalSection");
    EmitGlobalLoadReg(X86Register.Rcx, "__io_sync_req_event");
    EmitCallImport("kernel32.dll", "ResetEvent");
    EmitJmp("__io_dequeue_sync_ret");
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

  private void EmitMmRawAlloc260() {
    // mm_raw_alloc_260(): Allocate exactly 260 bytes of raw memory (for GetCurrentDirectoryA)
    EmitRuntimeFunctionStart("mm_raw_alloc_260", 0, 0x20);
    EmitMovRegImm(X86Register.Rcx, 260);
    EmitCallRuntimeLabel("mm_raw_alloc");
    EmitRuntimeFunctionEnd();
  }

  private void EmitIoPanicIo() {
    // __gt_panic_io(): Called on unknown sync op code — should never happen
    EmitRuntimeFunctionStart("__gt_panic_io", 0, 0x20);
    var msgLabel = "__io_panic_msg";
    DefineRdata(msgLabel, System.Text.Encoding.ASCII.GetBytes("PANIC: unknown I/O op code\0"));
    EmitLeaRegRipRel(X86Register.Rcx, msgLabel);
    EmitCallRuntimeLabel("maxon_panic");
    EmitRuntimeFunctionEnd();
  }
}
