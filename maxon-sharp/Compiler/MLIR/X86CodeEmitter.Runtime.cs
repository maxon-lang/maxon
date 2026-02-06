using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {
  // Internal calling convention parameter registers (must match RegisterManager.CallConvRegs)
  private static readonly X86Register[] _abiArgRegs = [
    X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9,
    X86Register.Rsi, X86Register.Rdi, X86Register.Rax, X86Register.Rbx
  ];

  public void EmitRuntimeFunctions() {
    EmitMaxonAlloc();
    EmitMaxonRealloc();
    EmitMaxonFree();
    EmitMaxonCowCheck();
    EmitMaxonToCString();
    EmitMaxonWriteStdout();
    EmitMaxonI64ToString();
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
    EmitMaxonProcessCreate();
    EmitMaxonProcessWait();
    EmitMaxonProcessGetExitCode();
    EmitMaxonProcessClose();
    EmitMaxonStrlen();
    EmitMaxonMemcpy();
  }

  /// <summary>maxon_alloc(size_in_rcx) -> ptr_in_rax</summary>
  private void EmitMaxonAlloc() {
    EmitRuntimeFunctionStart("maxon_alloc", 1);
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x08); // HEAP_ZERO_MEMORY
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_realloc(ptr_in_rcx, new_size_in_rdx) -> new_ptr_in_rax
  /// If ptr is NULL, falls back to HeapAlloc (HeapReAlloc doesn't accept NULL).
  /// Returns a new pointer — caller must update its buffer field.
  /// </summary>
  private void EmitMaxonRealloc() {
    EmitRuntimeFunctionStart("maxon_realloc", 2);
    // If ptr == 0, use HeapAlloc instead
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("nz", "rt_realloc_realloc_path");
    // Alloc path
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x10); // HEAP_ZERO_MEMORY
    EmitJmp("rt_realloc_epilogue");
    // Realloc path
    DefineLabel("rt_realloc_realloc_path");
    EmitHeapCall("HeapReAlloc", 0x08, rbpSlotR8: -0x08, rbpSlotR9: -0x10); // HEAP_ZERO_MEMORY
    DefineLabel("rt_realloc_epilogue");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_free(ptr_in_rcx) — null-safe, skips free if ptr is 0</summary>
  private void EmitMaxonFree() {
    EmitRuntimeFunctionStart("maxon_free", 1);
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", "rt_free_skip");
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x08);
    DefineLabel("rt_free_skip");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cow_check(buffer_in_rcx, capacity_in_rdx, length_in_r8, elemSize_in_r9) -> new_buffer_in_rax
  /// If capacity != 0, buffer is already writable — return it as-is.
  /// If capacity == 0, allocate length*elemSize bytes, copy from old buffer, return new buffer.
  /// </summary>
  private void EmitMaxonCowCheck() {
    EmitRuntimeFunctionStart("maxon_cow_check", 4, 0x40);
    // TEST rdx, rdx (check capacity)
    EmitBytes(0x48, 0x85, 0xD2);
    EmitJcc("nz", "rt_cow_writable");
    // COW path: compute byteLen = length * elemSize, allocate, copy, return new buffer
    EmitMovRegMem(X86Register.Rax, -0x18, 8); // RAX = length
    EmitBytes(0x49, 0x0F, 0xAF, 0xC1);       // IMUL RAX, R9 (byteLen = length * elemSize)
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = byteLen
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax); // RCX = byteLen (alloc size)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_alloc")); EmitDword(0);
    // Save new buffer
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = new buffer
    // rep movsb: RSI=src, RDI=dst, RCX=count
    EmitMovRegMem(X86Register.Rsi, -0x08, 8);  // RSI = old buffer
    EmitMovRegMem(X86Register.Rdi, -0x30, 8);  // RDI = new buffer
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);  // RCX = byteLen
    EmitBytes(0xF3, 0xA4); // REP MOVSB
    // Return new buffer
    EmitMovRegMem(X86Register.Rax, -0x30, 8);
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
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_alloc")); EmitDword(0);
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
  /// index, converts to UTF-8 via WideCharToMultiByte, allocates via maxon_alloc.
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

    // Step 6: Allocate buffer via maxon_alloc
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_alloc")); EmitDword(0);

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

  /// <summary>maxon_file_open_read(cstring_path) -> handle: returns -1 (stub, indicates failure)</summary>
  private void EmitMaxonFileOpenRead() {
    EmitRuntimeFunctionStart("maxon_file_open_read", 1);
    EmitMovRegImm(X86Register.Rax, -1);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_file_size(handle) -> int: returns 0 (stub)</summary>
  private void EmitMaxonFileSize() {
    EmitRuntimeFunctionStart("maxon_file_size", 1);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_file_read(handle, buffer_ptr, size) -> int: returns 0 (stub)</summary>
  private void EmitMaxonFileRead() {
    EmitRuntimeFunctionStart("maxon_file_read", 3);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_file_close(handle): void (stub)</summary>
  private void EmitMaxonFileClose() {
    EmitRuntimeFunctionStart("maxon_file_close", 1);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_file_delete(cstring_path) -> int: returns 0 (stub)</summary>
  private void EmitMaxonFileDelete() {
    EmitRuntimeFunctionStart("maxon_file_delete", 1);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_write_file(cstring_path, cstring_content) -> int: returns 0 (stub)</summary>
  private void EmitMaxonWriteFile() {
    EmitRuntimeFunctionStart("maxon_write_file", 2);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_write_file_binary(cstring_path, byte_array) -> int: returns 0 (stub)</summary>
  private void EmitMaxonWriteFileBinary() {
    EmitRuntimeFunctionStart("maxon_write_file_binary", 2);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Directory runtime stubs
  // ==========================================================================

  /// <summary>maxon_find_first_file(cstring_pattern) -> handle: returns 0 (stub)</summary>
  private void EmitMaxonFindFirstFile() {
    EmitRuntimeFunctionStart("maxon_find_first_file", 1);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_find_filename(handle) -> cstring: returns 0 (stub)</summary>
  private void EmitMaxonFindFilename() {
    EmitRuntimeFunctionStart("maxon_find_filename", 1);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_find_next_file(handle) -> int: returns 0 (stub)</summary>
  private void EmitMaxonFindNextFile() {
    EmitRuntimeFunctionStart("maxon_find_next_file", 1);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_find_close(handle): void (stub)</summary>
  private void EmitMaxonFindClose() {
    EmitRuntimeFunctionStart("maxon_find_close", 1);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_directory_exists(cstring_path) -> int: returns 0 (stub)</summary>
  private void EmitMaxonDirectoryExists() {
    EmitRuntimeFunctionStart("maxon_directory_exists", 1);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Process runtime stubs
  // ==========================================================================

  /// <summary>maxon_process_create(cstring_cmd, cstring_cwd) -> handle: returns 0 (stub)</summary>
  private void EmitMaxonProcessCreate() {
    EmitRuntimeFunctionStart("maxon_process_create", 2);
    EmitBytes(0x48, 0x31, 0xC0); // XOR rax, rax
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_process_wait(handle, timeout_ms) -> int: returns -1 (stub, error)</summary>
  private void EmitMaxonProcessWait() {
    EmitRuntimeFunctionStart("maxon_process_wait", 2);
    EmitMovRegImm(X86Register.Rax, -1);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_process_get_exit_code(handle) -> int: returns -1 (stub)</summary>
  private void EmitMaxonProcessGetExitCode() {
    EmitRuntimeFunctionStart("maxon_process_get_exit_code", 1);
    EmitMovRegImm(X86Register.Rax, -1);
    EmitRuntimeFunctionEnd();
  }

  /// <summary>maxon_process_close(handle): void (stub)</summary>
  private void EmitMaxonProcessClose() {
    EmitRuntimeFunctionStart("maxon_process_close", 1);
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
}
