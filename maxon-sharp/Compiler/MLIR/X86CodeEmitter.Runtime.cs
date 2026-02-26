using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {
  // Internal calling convention parameter registers (must match RegisterManager.CallConvRegs)
  private static readonly X86Register[] _abiArgRegs = [
    X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9,
    X86Register.Rsi, X86Register.Rdi, X86Register.Rax, X86Register.Rbx
  ];

  public void EmitRuntimeFunctions() {
    EmitMaxonCowCheck();
    EmitMaxonToCString();
    EmitMaxonWriteStdout();
    EmitMaxonWriteStderr();
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
    EmitMaxonFileOpenRead();
    EmitMaxonFileSize();
    EmitMaxonFileRead();
    EmitMaxonFileClose();
    EmitMaxonFileDelete();
    EmitMaxonWriteFile();
    EmitMaxonWriteFileBinary();
    EmitMaxonFindFirstFile();
    EmitMaxonFindFilename();
    EmitMaxonFindNextFile();
    EmitMaxonFindClose();
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
    EmitMaxonProcessReadStdout();
    EmitMaxonProcessReadStderr();
    EmitMaxonStrlen();
    EmitMaxonMemcpy();
    EmitMaxonMemcmp();
  }

  /// <summary>
  /// maxon_cow_check(buffer_in_rcx, capacity_in_rdx, length_in_r8, elemSize_in_r9) -> new_buffer_in_rax
  /// If capacity != 0, buffer is already writable — return it as-is.
  /// If capacity == 0, allocate length*elemSize bytes, copy from old buffer, return new buffer.
  /// </summary>
  private void EmitMaxonCowCheck() {
    // Args: rcx=buffer, rdx=capacity, r8=length, r9=elemSize, rsi=managedPtr
    // Stack: [rbp-0x08]=buffer, [rbp-0x10]=capacity, [rbp-0x18]=length,
    //        [rbp-0x20]=elemSize, [rbp-0x28]=managedPtr,
    //        [rbp-0x30]=byteLen, [rbp-0x38]=new_buffer
    EmitRuntimeFunctionStart("maxon_cow_check", 5, 0x60);
    // TEST rdx, rdx (check capacity)
    EmitBytes(0x48, 0x85, 0xD2);
    EmitJcc("nz", "rt_cow_writable");
    // COW path: compute byteLen = length * elemSize
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = length
    EmitBytes(0x49, 0x0F, 0xAF, 0xC1);       // IMUL RAX, R9 (byteLen = length * elemSize)
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-0x30] = byteLen
    // mm_alloc_in(size=byteLen, parent_ptr=managedPtr, tag="Buffer")
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // RCX = byteLen
    EmitMovRegMem(X86Register.Rdx, -0x28, 8);        // RDX = managedPtr
    EmitLeaRegSymdataRel(X86Register.R8, "__rt_tag_buffer"); // R8 = tag
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc_in")); EmitDword(0);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // [rbp-0x38] = new buffer
    // rep movsb: RSI=src, RDI=dst, RCX=count
    EmitMovRegMem(X86Register.Rsi, -0x08, 8);  // RSI = old buffer
    EmitMovRegMem(X86Register.Rdi, -0x38, 8);  // RDI = new buffer
    EmitMovRegMem(X86Register.Rcx, -0x30, 8);  // RCX = byteLen
    EmitBytes(0xF3, 0xA4); // REP MOVSB
    // Return new buffer
    EmitMovRegMem(X86Register.Rax, -0x38, 8);
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
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cstring"); // RDX = tag
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

    // Step 4: Get wargv[index] — the wide string pointer
    // Load index into RCX
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
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_cmdline_arg"); // RDX = tag
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
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // File I/O runtime stubs
  // ==========================================================================

  /// <summary>
  /// maxon_file_open_read(cstring_path) -> handle or -1
  /// CreateFileA(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, 0, NULL)
  /// Stack: [rbp-8]=path
  /// </summary>
  private void EmitMaxonFileOpenRead() {
    EmitRuntimeFunctionStart("maxon_file_open_read", 1, 0x50);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);        // arg1: lpFileName
    EmitMovRegImm(X86Register.Rdx, 0x80000000);       // arg2: GENERIC_READ
    EmitMovRegImm(X86Register.R8, 1);                  // arg3: FILE_SHARE_READ
    EmitXorRegReg(X86Register.R9, X86Register.R9);     // arg4: lpSecurityAttributes = NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x03, 0x00, 0x00, 0x00); // [rsp+0x20] = OPEN_EXISTING (3)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00); // [rsp+0x28] = 0
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // [rsp+0x30] = NULL
    EmitCallImport("kernel32.dll", "CreateFileA");
    // CreateFileA returns INVALID_HANDLE_VALUE (-1) on failure, which is what we want
    EmitRuntimeFunctionEnd();
  }

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
  /// maxon_file_read(handle, buffer_ptr, size) -> bytes read
  /// ReadFile(handle, buffer, size, &bytesRead, NULL)
  /// Stack: [rbp-8]=handle, [rbp-16]=buffer, [rbp-24]=size, [rbp-32]=bytesRead
  /// </summary>
  private void EmitMaxonFileRead() {
    EmitRuntimeFunctionStart("maxon_file_read", 3, 0x40);
    // Zero the bytesRead slot
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: hFile
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);         // arg2: lpBuffer
    EmitMovRegMem(X86Register.R8, -0x18, 8);           // arg3: nNumberOfBytesToRead
    // LEA R9, [rbp-0x20] (arg4: &bytesRead)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // [rsp+0x20] = NULL (lpOverlapped)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "ReadFile");
    EmitMovRegMem(X86Register.Rax, -0x20, 8);         // return bytesRead
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_close(handle) -> void
  /// CloseHandle(handle)
  /// Stack: [rbp-8]=handle
  /// </summary>
  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: hObject
    EmitCallImport("kernel32.dll", "CloseHandle");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_file_delete(cstring_path) -> 0 on success, non-zero on failure
  /// DeleteFileA returns non-zero on success (inverted from our convention)
  /// Stack: [rbp-8]=path
  /// </summary>
  private void EmitMaxonFileDelete() {
    EmitRuntimeFunctionStart("maxon_file_delete", 1, 0x30);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);         // arg1: lpFileName
    EmitCallImport("kernel32.dll", "DeleteFileA");
    // DeleteFileA: non-zero = success, zero = failure
    // We need: 0 = success, non-zero = failure (inverted)
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitSetcc("z", X86Register.Rax);                   // AL = 1 if failed (ZF set = RAX was 0)
    EmitMovzxReg8To64(X86Register.Rax);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emits CreateFileA call for writing: CreateFileA(pathSlot, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, 0, NULL)
  /// Result handle is in RAX. Branches to failLabel on INVALID_HANDLE_VALUE.
  /// </summary>
  private void EmitCreateFileForWrite(int pathSlot, string failLabel) {
    EmitMovRegMem(X86Register.Rcx, pathSlot, 8);       // arg1: lpFileName
    EmitMovRegImm(X86Register.Rdx, 0x40000000);        // arg2: GENERIC_WRITE
    EmitXorRegReg(X86Register.R8, X86Register.R8);      // arg3: dwShareMode = 0
    EmitXorRegReg(X86Register.R9, X86Register.R9);      // arg4: lpSecurityAttributes = NULL
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x02, 0x00, 0x00, 0x00); // [rsp+0x20] = CREATE_ALWAYS (2)
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x28, 0x00, 0x00, 0x00, 0x00); // [rsp+0x28] = 0
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x30, 0x00, 0x00, 0x00, 0x00); // [rsp+0x30] = NULL
    EmitCallImport("kernel32.dll", "CreateFileA");
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("e", failLabel);
  }

  /// <summary>
  /// Emits WriteFile + CloseHandle. Jumps to writeFailLabel if WriteFile fails (handle still needs closing).
  /// </summary>
  private void EmitWriteFileAndClose(int handleSlot, int bufferSlot, int lengthSlot, int bytesWrittenSlot, string writeFailLabel) {
    EmitMovRegMem(X86Register.Rcx, handleSlot, 8);     // arg1: handle
    EmitMovRegMem(X86Register.Rdx, bufferSlot, 8);     // arg2: buffer
    EmitMovRegMem(X86Register.R8, lengthSlot, 8);       // arg3: length
    // LEA R9, [rbp+bytesWrittenSlot] (&bytesWritten)
    EmitBytes(0x4C, 0x8D, 0x4D, (byte)(bytesWrittenSlot & 0xFF));
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00); // [rsp+0x20] = NULL
    EmitCallImport("kernel32.dll", "WriteFile");
    EmitTestRegReg(X86Register.Rax, X86Register.Rax);
    EmitJcc("z", writeFailLabel);
    EmitMovRegMem(X86Register.Rcx, handleSlot, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
  }

  /// <summary>
  /// maxon_write_file(cstring_path, cstring_content) -> 0 on success, non-zero on failure
  /// Stack: [rbp-8]=path, [rbp-16]=content, [rbp-24]=handle, [rbp-32]=length/bytesWritten
  /// </summary>
  private void EmitMaxonWriteFile() {
    EmitRuntimeFunctionStart("maxon_write_file", 2, 0x50);
    EmitCreateFileForWrite(-0x08, "rt_wf_fail");
    EmitMovMemReg(-0x18, X86Register.Rax, 8);         // [rbp-24] = handle
    // strlen on content to get length
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);         // RDX = content ptr
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);    // RAX = 0 (counter)
    DefineLabel("rt_wf_strlen_loop");
    EmitBytes(0x0F, 0xB6, 0x0C, 0x02);                // MOVZX ECX, byte [RDX+RAX]
    EmitBytes(0x84, 0xC9);                              // TEST CL, CL
    EmitJcc("z", "rt_wf_strlen_done");
    EmitBytes(0x48, 0xFF, 0xC0);                        // INC RAX
    EmitJmp("rt_wf_strlen_loop");
    DefineLabel("rt_wf_strlen_done");
    EmitMovMemReg(-0x20, X86Register.Rax, 8);         // [rbp-32] = length
    EmitWriteFileAndClose(-0x18, -0x10, -0x20, -0x20, "rt_wf_write_fail");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);    // return 0 (success)
    EmitJmp("rt_wf_done");
    DefineLabel("rt_wf_write_fail");
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    DefineLabel("rt_wf_fail");
    EmitMovRegImm(X86Register.Rax, 1);
    DefineLabel("rt_wf_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_write_file_binary(cstring_path, buffer_ptr, length) -> 0 on success, non-zero on failure
  /// Stack: [rbp-8]=path, [rbp-16]=buffer_ptr, [rbp-24]=length, [rbp-32]=handle, [rbp-40]=bytesWritten
  /// </summary>
  private void EmitMaxonWriteFileBinary() {
    EmitRuntimeFunctionStart("maxon_write_file_binary", 3, 0x50);
    EmitCreateFileForWrite(-0x08, "rt_wfb_fail");
    EmitMovMemReg(-0x20, X86Register.Rax, 8);         // [rbp-32] = handle
    EmitWriteFileAndClose(-0x20, -0x10, -0x18, -0x28, "rt_wfb_write_fail");
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);    // return 0 (success)
    EmitJmp("rt_wfb_done");
    DefineLabel("rt_wfb_write_fail");
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitCallImport("kernel32.dll", "CloseHandle");
    DefineLabel("rt_wfb_fail");
    EmitMovRegImm(X86Register.Rax, 1);
    DefineLabel("rt_wfb_done");
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
  /// maxon_find_first_file(cstring_pattern) -> block_ptr or 0
  /// Allocates a block [handle(8) + WIN32_FIND_DATAA(320)], calls FindFirstFileA.
  /// Returns block pointer on success, 0 if not found.
  /// Stack: [rbp-8]=pattern, [rbp-16]=block_ptr
  /// </summary>
  private void EmitMaxonFindFirstFile() {
    EmitRuntimeFunctionStart("maxon_find_first_file", 1, 0x40);
    // Allocate block
    EmitMovRegImm(X86Register.Rcx, FindBlockSize);
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_find_data"); // RDX = tag
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_alloc")); EmitDword(0);
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-16] = block_ptr
    // FindFirstFileA(pattern, &block[8])
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // arg1: pattern
    EmitMovRegMem(X86Register.Rdx, -0x10, 8); // block_ptr
    EmitAddRegImm(X86Register.Rdx, FindBlockFindDataOffset); // arg2: &findData
    EmitCallImport("kernel32.dll", "FindFirstFileA");
    // Check for INVALID_HANDLE_VALUE (-1)
    EmitMovRegImm(X86Register.Rcx, -1);
    EmitCmpRegReg(X86Register.Rax, X86Register.Rcx);
    EmitJcc("ne", "rt_fff_found");
    // Not found: free block, return 0
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("rt_fff_done");
    // Found: store handle in block[0], return block_ptr
    DefineLabel("rt_fff_found");
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // block_ptr
    EmitMovIndirectMemReg(X86Register.Rcx, FindBlockHandleOffset, X86Register.Rax); // block[0] = handle
    EmitMovRegReg(X86Register.Rax, X86Register.Rcx); // return block_ptr
    DefineLabel("rt_fff_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_find_filename(block_ptr) -> cstring pointer to cFileName in the block
  /// Returns block_ptr + 8 + 44 = block_ptr + 52
  /// </summary>
  private void EmitMaxonFindFilename() {
    EmitRuntimeFunctionStart("maxon_find_filename", 1);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // block_ptr
    EmitAddRegImm(X86Register.Rax, FindBlockFindDataOffset + FindDataFileNameOffset);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_find_next_file(block_ptr) -> non-zero if found, 0 if done
  /// Calls FindNextFileA(handle, &findData).
  /// Stack: [rbp-8]=block_ptr
  /// </summary>
  private void EmitMaxonFindNextFile() {
    EmitRuntimeFunctionStart("maxon_find_next_file", 1, 0x40);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // block_ptr
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FindBlockHandleOffset); // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8); // block_ptr
    EmitAddRegImm(X86Register.Rdx, FindBlockFindDataOffset); // arg2: &findData
    EmitCallImport("kernel32.dll", "FindNextFileA");
    // RAX = non-zero if found, 0 if no more files
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_find_close(block_ptr) -> void
  /// Calls FindClose(handle), then frees the block.
  /// Stack: [rbp-8]=block_ptr
  /// </summary>
  private void EmitMaxonFindClose() {
    EmitRuntimeFunctionStart("maxon_find_close", 1, 0x40);
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // block_ptr
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, FindBlockHandleOffset); // arg1: handle
    EmitCallImport("kernel32.dll", "FindClose");
    // Free block
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "mm_free")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_directory_exists(cstring_path) -> 1 if directory, 0 otherwise
  /// Calls GetFileAttributesA, checks FILE_ATTRIBUTE_DIRECTORY (0x10).
  /// Stack: [rbp-8]=path
  /// </summary>
  private void EmitMaxonDirectoryExists() {
    EmitRuntimeFunctionStart("maxon_directory_exists", 1, 0x40);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // arg1: path
    EmitCallImport("kernel32.dll", "GetFileAttributesA");
    // Check for INVALID_FILE_ATTRIBUTES (0xFFFFFFFF / -1 as DWORD)
    EmitBytes(0x83, 0xF8, 0xFF); // CMP EAX, -1 (sign-extended imm8)
    EmitJcc("ne", "rt_direx_check_attr");
    // INVALID_FILE_ATTRIBUTES: return 0
    EmitXorRegReg(X86Register.Rax, X86Register.Rax);
    EmitJmp("rt_direx_done");
    // Check FILE_ATTRIBUTE_DIRECTORY bit (0x10)
    DefineLabel("rt_direx_check_attr");
    EmitBytes(0xA8, 0x10); // TEST AL, 0x10
    EmitSetcc("nz", X86Register.Rax);
    EmitMovzxReg8To64(X86Register.Rax);
    DefineLabel("rt_direx_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_create_directory(cstring_path) -> nonzero on success, 0 on failure
  /// Calls CreateDirectoryA(path, NULL).
  /// </summary>
  private void EmitMaxonCreateDirectory() {
    EmitRuntimeFunctionStart("maxon_create_directory", 1, 0x40);
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // arg1: path
    EmitXorRegReg(X86Register.Rdx, X86Register.Rdx); // arg2: lpSecurityAttributes = NULL
    EmitCallImport("kernel32.dll", "CreateDirectoryA");
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
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_dir_buffer"); // RDX = tag
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
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_capture_result"); // RDX = tag
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
    EmitLeaRegSymdataRel(X86Register.Rdx, "__rt_tag_pipe_buffer"); // RDX = tag
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
    EmitLeaRegSymdataRel(X86Register.R8, "__rt_tag_pipe_buffer"); // R8 = tag
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
  /// Extracts hProcess from the capture struct.
  /// </summary>
  private void EmitMaxonProcessGetHandle() {
    EmitRuntimeFunctionStart("maxon_process_get_handle", 1);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitMovRegIndirectMem(X86Register.Rax, X86Register.Rax, 0x00);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_stdout(capture_struct_ptr) -> cstring_ptr.
  /// Reads stdout pipe from capture struct, returns null-terminated heap string.
  /// </summary>
  private void EmitMaxonProcessReadStdout() {
    EmitRuntimeFunctionStart("maxon_process_read_stdout", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);           // capture struct
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, 0x08); // hStdoutRead
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_process_read_pipe")); EmitDword(0);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_process_read_stderr(capture_struct_ptr) -> cstring_ptr.
  /// Reads stderr pipe from capture struct, returns null-terminated heap string.
  /// </summary>
  private void EmitMaxonProcessReadStderr() {
    EmitRuntimeFunctionStart("maxon_process_read_stderr", 1, 0x30);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);           // capture struct
    EmitMovRegIndirectMem(X86Register.Rcx, X86Register.Rax, 0x10); // hStderrRead
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_process_read_pipe")); EmitDword(0);
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
}
