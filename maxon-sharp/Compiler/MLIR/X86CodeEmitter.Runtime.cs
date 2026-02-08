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
    EmitMaxonF64ToString();
    EmitMaxonBoolToString();
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
    EmitMaxonMemcmp();
    EmitMaxonCleanupUntracked();

    if (TrackAllocs) {
      EmitTrackingRuntimeFunctions();
    }
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
    EmitMovSdMemXmm(-0x08, X86XmmRegister.Xmm0);

    // ---- Special case: value == 0.0 ----
    // XORPD xmm1, xmm1 to get 0.0
    EmitBytes(0x66, 0x0F, 0x57, 0xC9); // XORPD xmm1, xmm1
    EmitUcomisd(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1);
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
    EmitMovSdXmmMem(X86XmmRegister.Xmm0, -0x08);
    // XORPD xmm1, xmm1 for zero
    EmitBytes(0x66, 0x0F, 0x57, 0xC9); // XORPD xmm1, xmm1
    EmitUcomisd(X86XmmRegister.Xmm1, X86XmmRegister.Xmm0);
    // If 0.0 > value (i.e., value is negative), UCOMISD sets CF
    EmitJcc("be", "rt_f64str_positive");

    // Negative: negate by subtracting from zero (0.0 - value)
    // XMM1 is already 0.0
    EmitSubSd(X86XmmRegister.Xmm1, X86XmmRegister.Xmm0); // XMM1 = 0.0 - value = |value|
    // Store |value| back
    EmitMovSdMemXmm(-0x08, X86XmmRegister.Xmm1);
    // is_negative = 1
    EmitMovRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x18, X86Register.Rax, 8);

    // ---- Extract integer part ----
    DefineLabel("rt_f64str_positive");
    EmitMovSdXmmMem(X86XmmRegister.Xmm0, -0x08); // XMM0 = |value|
    EmitCvttSd2Si(X86Register.Rax, X86XmmRegister.Xmm0); // RAX = truncate(|value|)
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
    EmitMovSdXmmMem(X86XmmRegister.Xmm0, -0x08); // XMM0 = |value|
    EmitMovRegMem(X86Register.Rax, -0x30, 8);     // RAX = integer part
    EmitCvtSi2Sd(X86XmmRegister.Xmm1, X86Register.Rax); // XMM1 = (double)integer_part
    EmitSubSd(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1); // XMM0 = fractional part

    // Multiply by 1000000 for 6 decimal places
    // Load 1000000.0 into XMM1 via GPR to avoid rdata dependency
    EmitMovRegImm(X86Register.Rax, BitConverter.DoubleToInt64Bits(1000000.0));
    EmitBytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // MOVQ XMM1, RAX
    EmitMulSd(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1); // XMM0 = frac * 1000000

    // Add 0.5 for rounding
    // Load 0.5 into XMM1 via GPR to avoid rdata dependency
    EmitMovRegImm(X86Register.Rax, BitConverter.DoubleToInt64Bits(0.5));
    EmitBytes(0x66, 0x48, 0x0F, 0x6E, 0xC8); // MOVQ XMM1, RAX
    EmitAddSd(X86XmmRegister.Xmm0, X86XmmRegister.Xmm1); // XMM0 = frac * 1000000 + 0.5

    EmitCvttSd2Si(X86Register.Rax, X86XmmRegister.Xmm0); // RAX = rounded 6-digit fractional integer

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

  /// <summary>
  /// maxon_cleanup_untracked(capacity_in_rcx, buffer_in_rdx)
  /// Frees heap-allocated buffer if capacity > 0 (skips rdata/constant buffers).
  /// Stack: [rbp-8]=capacity, [rbp-16]=buffer
  /// </summary>
  private void EmitMaxonCleanupUntracked() {
    EmitRuntimeFunctionStart("maxon_cleanup_untracked", 2);
    // Only free if capacity > 0 (heap-allocated)
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // capacity
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_cleanup_untracked_done");
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // buffer
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);
    DefineLabel("rt_cleanup_untracked_done");
    EmitRuntimeFunctionEnd();
  }

  // ==========================================================================
  // Allocation tracking runtime functions
  // ==========================================================================

  /// <summary>
  /// Emit all allocation tracking runtime functions. Called when TrackAllocs is enabled.
  /// </summary>
  private void EmitTrackingRuntimeFunctions() {
    // Define rdata string constants used by tracking functions
    DefineRdata("rdata_track_alloc", System.Text.Encoding.UTF8.GetBytes("ALLOC #"));
    DefineRdata("rdata_track_free", System.Text.Encoding.UTF8.GetBytes("FREE #"));
    DefineRdata("rdata_track_incref", System.Text.Encoding.UTF8.GetBytes("INCREF: "));
    DefineRdata("rdata_track_decref", System.Text.Encoding.UTF8.GetBytes("DECREF: "));
    DefineRdata("rdata_track_move", System.Text.Encoding.UTF8.GetBytes("MOVE: "));
    DefineRdata("rdata_track_copy", System.Text.Encoding.UTF8.GetBytes("COPY: "));
    DefineRdata("rdata_track_cleanup", System.Text.Encoding.UTF8.GetBytes("CLEANUP: "));
    DefineRdata("rdata_track_colon", System.Text.Encoding.UTF8.GetBytes(": "));
    DefineRdata("rdata_track_bytes", System.Text.Encoding.UTF8.GetBytes(" bytes ("));
    DefineRdata("rdata_track_rparen", System.Text.Encoding.UTF8.GetBytes(")\n"));
    DefineRdata("rdata_track_arrow_rc", System.Text.Encoding.UTF8.GetBytes(" -> rc="));
    DefineRdata("rdata_track_newline", System.Text.Encoding.UTF8.GetBytes("\n"));
    DefineRdata("rdata_track_summary_header", System.Text.Encoding.UTF8.GetBytes("\n=== MEMORY STATS ===\n"));
    DefineRdata("rdata_track_summary_allocated", System.Text.Encoding.UTF8.GetBytes("Allocated: "));
    DefineRdata("rdata_track_summary_freed", System.Text.Encoding.UTF8.GetBytes("Freed:     "));
    DefineRdata("rdata_track_summary_leaked", System.Text.Encoding.UTF8.GetBytes("Leaked:    "));
    DefineRdata("rdata_track_summary_moves", System.Text.Encoding.UTF8.GetBytes("Moves:     "));
    DefineRdata("rdata_track_summary_increfs", System.Text.Encoding.UTF8.GetBytes("Increfs:   "));
    DefineRdata("rdata_track_summary_decrefs", System.Text.Encoding.UTF8.GetBytes("Decrefs:   "));
    DefineRdata("rdata_track_summary_copies", System.Text.Encoding.UTF8.GetBytes("Copies:    "));
    DefineRdata("rdata_track_summary_cleanups", System.Text.Encoding.UTF8.GetBytes("Cleanups:  "));
    DefineRdata("rdata_track_array_cleanup", System.Text.Encoding.UTF8.GetBytes("array cleanup"));

    DefineRdata("rdata_track_array_element", System.Text.Encoding.UTF8.GetBytes("<array element>"));

    // Emit all tracking functions
    EmitMaxonTrackPrintStr();
    EmitMaxonTrackAlloc();
    EmitMaxonTrackFree();
    EmitMaxonTrackIncref();
    EmitMaxonTrackDecref();
    EmitMaxonTrackMove();
    EmitMaxonTrackCopy();
    EmitMaxonTrackCleanup();
    EmitMaxonCleanupManaged();
    EmitMaxonCleanupManagedFree();
    EmitMaxonCleanupArrayElements();
    EmitMaxonPrintAllocSummary();
  }

  /// <summary>
  /// maxon_track_print_str(ptr_in_rcx, len_in_rdx)
  /// Writes a non-null-terminated string to stdout using WriteFile.
  /// Stack: [rbp-8]=ptr, [rbp-16]=len, [rbp-24]=handle, [rbp-32]=bytesWritten
  /// </summary>
  private void EmitMaxonTrackPrintStr() {
    EmitRuntimeFunctionStart("maxon_track_print_str", 2, 0x40);

    // GetStdHandle(STD_OUTPUT_HANDLE = -11)
    EmitMovRegImm(X86Register.Rcx, -11);
    EmitCallImport("kernel32.dll", "GetStdHandle");
    EmitMovMemReg(-0x18, X86Register.Rax, 8); // [rbp-24] = handle

    // WriteFile(handle, buffer, nNumberOfBytesToWrite, &lpNumberOfBytesWritten, lpOverlapped)
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // arg1: handle
    EmitMovRegMem(X86Register.Rdx, -0x08, 8);  // arg2: buffer (ptr)
    EmitMovRegMem(X86Register.R8, -0x10, 8);   // arg3: length
    // arg4: LEA R9, [rbp-0x20] (&bytesWritten)
    EmitBytes(0x4C, 0x8D, 0x4D, 0xE0);
    // arg5: lpOverlapped = NULL at [rsp+0x20]
    EmitBytes(0x48, 0xC7, 0x44, 0x24, 0x20, 0x00, 0x00, 0x00, 0x00);
    EmitCallImport("kernel32.dll", "WriteFile");

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_alloc(ptr_in_rcx, size_in_rdx, tag_ptr_in_r8, tag_len_in_r9)
  /// Tracks an allocation by incrementing next_id, updating alloc_bytes, storing in table,
  /// and printing: ALLOC #id: size bytes (tag)
  /// Stack: [rbp-8]=ptr, [rbp-16]=size, [rbp-24]=tag_ptr, [rbp-32]=tag_len,
  ///        [rbp-40]=id, [rbp-48]=loop counter, [rbp-64]=number buffer (24 bytes)
  /// </summary>
  private void EmitMaxonTrackAlloc() {
    EmitRuntimeFunctionStart("maxon_track_alloc", 4, 0xC0);

    // Increment __track_next_id: load, add 1, store, use new value
    EmitGlobalLoadReg(X86Register.Rax, "__track_next_id");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__track_next_id");
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // [rbp-40] = id

    // Add size to __track_alloc_bytes
    EmitGlobalLoadReg(X86Register.Rax, "__track_alloc_bytes");
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = size
    EmitBytes(0x48, 0x01, 0xC8); // ADD rax, rcx
    EmitGlobalStoreReg(X86Register.Rax, "__track_alloc_bytes");

    // Find empty slot in __track_table and store {ptr, size, id}
    // Load table base address into R10
    EmitGlobalLeaReg(X86Register.R10, "__track_table");
    // R11 = loop counter (0 to 255)
    EmitBytes(0x4D, 0x31, 0xDB); // XOR r11, r11

    DefineLabel("rt_track_alloc_find_slot");
    // Check if R11 >= 256
    EmitBytes(0x49, 0x81, 0xFB, 0x00, 0x01, 0x00, 0x00); // CMP r11, 256
    EmitJcc("ge", "rt_track_alloc_slot_done"); // No slot found, skip

    // Calculate entry address: R10 + R11*24
    // RAX = R11 * 24
    EmitBytes(0x4C, 0x89, 0xD8); // MOV rax, r11
    EmitMovRegImm(X86Register.Rcx, 24);
    EmitBytes(0x48, 0x0F, 0xAF, 0xC1); // IMUL rax, rcx
    EmitBytes(0x4C, 0x01, 0xD0); // ADD rax, r10 => RAX = entry address

    // Check if ptr (first 8 bytes) == 0
    EmitBytes(0x48, 0x83, 0x38, 0x00); // CMP qword [rax], 0
    EmitJcc("nz", "rt_track_alloc_next_slot"); // Slot occupied, try next

    // Found empty slot: store {ptr, size, id}
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = ptr
    EmitBytes(0x48, 0x89, 0x08); // MOV [rax], rcx (ptr at offset 0)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = size
    EmitBytes(0x48, 0x89, 0x48, 0x08); // MOV [rax+8], rcx (size at offset 8)
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = id
    EmitBytes(0x48, 0x89, 0x48, 0x10); // MOV [rax+16], rcx (id at offset 16)
    EmitJmp("rt_track_alloc_slot_done");

    DefineLabel("rt_track_alloc_next_slot");
    EmitBytes(0x49, 0xFF, 0xC3); // INC r11
    EmitJmp("rt_track_alloc_find_slot");

    DefineLabel("rt_track_alloc_slot_done");

    // Print: "ALLOC #"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_alloc");
    EmitMovRegImm(X86Register.Rdx, 7); // length of "ALLOC #"
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print id
    EmitMovRegMem(X86Register.Rcx, -0x28, 8); // RCX = id
    EmitLeaRegMem(X86Register.Rdx, -0x40); // RDX = buffer address
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    // RAX = length, print it
    EmitLeaRegMem(X86Register.Rcx, -0x40); // RCX = buffer
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax); // RDX = length
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print ": "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_colon");
    EmitMovRegImm(X86Register.Rdx, 2);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print size
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // RCX = size
    EmitLeaRegMem(X86Register.Rdx, -0x40); // RDX = buffer address
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x40);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print " bytes ("
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_bytes");
    EmitMovRegImm(X86Register.Rdx, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // tag_ptr
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // tag_len
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print ")\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_rparen");
    EmitMovRegImm(X86Register.Rdx, 2);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_free(ptr_in_rcx, tag_ptr_in_rdx, tag_len_in_r8)
  /// Looks up ptr in table, updates freed_bytes, clears entry, prints: FREE #id: size bytes (tag)
  /// Stack: [rbp-8]=ptr, [rbp-16]=tag_ptr, [rbp-24]=tag_len,
  ///        [rbp-32]=id, [rbp-40]=size, [rbp-64]=number buffer (24 bytes)
  /// </summary>
  private void EmitMaxonTrackFree() {
    EmitRuntimeFunctionStart("maxon_track_free", 3, 0xC0);

    // Look up ptr in __track_table
    EmitGlobalLeaReg(X86Register.R10, "__track_table");
    EmitBytes(0x4D, 0x31, 0xDB); // XOR r11, r11 (loop counter)

    DefineLabel("rt_track_free_find_slot");
    // Check if R11 >= 256
    EmitBytes(0x49, 0x81, 0xFB, 0x00, 0x01, 0x00, 0x00); // CMP r11, 256
    EmitJcc("ge", "rt_track_free_not_found");

    // Calculate entry address: R10 + R11*24
    EmitBytes(0x4C, 0x89, 0xD8); // MOV rax, r11
    EmitMovRegImm(X86Register.Rcx, 24);
    EmitBytes(0x48, 0x0F, 0xAF, 0xC1); // IMUL rax, rcx
    EmitBytes(0x4C, 0x01, 0xD0); // ADD rax, r10

    // Check if ptr at [rax] matches our ptr
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // RCX = ptr to find
    EmitBytes(0x48, 0x39, 0x08); // CMP [rax], rcx
    EmitJcc("nz", "rt_track_free_next_slot");

    // Found: load size and id
    EmitBytes(0x48, 0x8B, 0x48, 0x08); // MOV rcx, [rax+8] (size)
    EmitMovMemReg(-0x28, X86Register.Rcx, 8); // [rbp-40] = size
    EmitBytes(0x48, 0x8B, 0x48, 0x10); // MOV rcx, [rax+16] (id)
    EmitMovMemReg(-0x20, X86Register.Rcx, 8); // [rbp-32] = id

    // Add size to __track_freed_bytes
    EmitGlobalLoadReg(X86Register.Rcx, "__track_freed_bytes");
    EmitMovRegMem(X86Register.Rdx, -0x28, 8); // RDX = size
    EmitBytes(0x48, 0x01, 0xD1); // ADD rcx, rdx
    EmitGlobalStoreReg(X86Register.Rcx, "__track_freed_bytes");

    // Clear the entry: set ptr to 0
    EmitBytes(0x48, 0xC7, 0x00, 0x00, 0x00, 0x00, 0x00); // MOV qword [rax], 0

    EmitJmp("rt_track_free_found");

    DefineLabel("rt_track_free_next_slot");
    EmitBytes(0x49, 0xFF, 0xC3); // INC r11
    EmitJmp("rt_track_free_find_slot");

    DefineLabel("rt_track_free_not_found");
    // Not found: set id and size to 0 for printing
    EmitMovRegImm(X86Register.Rax, 0);
    EmitMovMemReg(-0x20, X86Register.Rax, 8); // id = 0
    EmitMovMemReg(-0x28, X86Register.Rax, 8); // size = 0

    DefineLabel("rt_track_free_found");

    // Print: "FREE #"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_free");
    EmitMovRegImm(X86Register.Rdx, 6);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print id
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);
    EmitLeaRegMem(X86Register.Rdx, -0x40);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x40);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print ": "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_colon");
    EmitMovRegImm(X86Register.Rdx, 2);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print size
    EmitMovRegMem(X86Register.Rcx, -0x28, 8);
    EmitLeaRegMem(X86Register.Rdx, -0x40);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x40);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print " bytes ("
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_bytes");
    EmitMovRegImm(X86Register.Rdx, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // tag_ptr
    EmitMovRegMem(X86Register.Rdx, -0x18, 8); // tag_len
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print ")\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_rparen");
    EmitMovRegImm(X86Register.Rdx, 2);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_incref(tag_ptr_in_rcx, tag_len_in_rdx, new_rc_in_r8)
  /// Increments incref counter and prints: INCREF: tag -> rc=new_rc
  /// Stack: [rbp-8]=tag_ptr, [rbp-16]=tag_len, [rbp-24]=new_rc, [rbp-48]=number buffer
  /// </summary>
  private void EmitMaxonTrackIncref() {
    EmitRuntimeFunctionStart("maxon_track_incref", 3, 0x80);

    // Increment __track_incref_count
    EmitGlobalLoadReg(X86Register.Rax, "__track_incref_count");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__track_incref_count");

    // Print "INCREF: "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_incref");
    EmitMovRegImm(X86Register.Rdx, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print " -> rc="
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_arrow_rc");
    EmitMovRegImm(X86Register.Rdx, 7);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print new_rc
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // new_rc
    EmitLeaRegMem(X86Register.Rdx, -0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x30);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_decref(tag_ptr_in_rcx, tag_len_in_rdx, new_rc_in_r8)
  /// Increments decref counter and prints: DECREF: tag -> rc=new_rc
  /// Stack: [rbp-8]=tag_ptr, [rbp-16]=tag_len, [rbp-24]=new_rc, [rbp-48]=number buffer
  /// </summary>
  private void EmitMaxonTrackDecref() {
    EmitRuntimeFunctionStart("maxon_track_decref", 3, 0x80);

    // Increment __track_decref_count
    EmitGlobalLoadReg(X86Register.Rax, "__track_decref_count");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__track_decref_count");

    // Print "DECREF: "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_decref");
    EmitMovRegImm(X86Register.Rdx, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print " -> rc="
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_arrow_rc");
    EmitMovRegImm(X86Register.Rdx, 7);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print new_rc
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // new_rc
    EmitLeaRegMem(X86Register.Rdx, -0x30);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x30);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_move(tag_ptr_in_rcx, tag_len_in_rdx)
  /// Increments move counter and prints: MOVE: tag
  /// Stack: [rbp-8]=tag_ptr, [rbp-16]=tag_len
  /// </summary>
  private void EmitMaxonTrackMove() {
    EmitRuntimeFunctionStart("maxon_track_move", 2, 0x80);

    // Increment __track_move_count
    EmitGlobalLoadReg(X86Register.Rax, "__track_move_count");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__track_move_count");

    // Print "MOVE: "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_move");
    EmitMovRegImm(X86Register.Rdx, 6);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_copy(tag_ptr_in_rcx, tag_len_in_rdx)
  /// Increments copy counter and prints: COPY: tag
  /// Stack: [rbp-8]=tag_ptr, [rbp-16]=tag_len
  /// </summary>
  private void EmitMaxonTrackCopy() {
    EmitRuntimeFunctionStart("maxon_track_copy", 2, 0x80);

    // Increment __track_copy_count
    EmitGlobalLoadReg(X86Register.Rax, "__track_copy_count");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__track_copy_count");

    // Print "COPY: "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_copy");
    EmitMovRegImm(X86Register.Rdx, 6);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_track_cleanup(tag_ptr_in_rcx, tag_len_in_rdx)
  /// Increments cleanup counter and prints: CLEANUP: tag
  /// Stack: [rbp-8]=tag_ptr, [rbp-16]=tag_len
  /// </summary>
  private void EmitMaxonTrackCleanup() {
    EmitRuntimeFunctionStart("maxon_track_cleanup", 2, 0x80);

    // Increment __track_cleanup_count
    EmitGlobalLoadReg(X86Register.Rax, "__track_cleanup_count");
    EmitAddRegImm(X86Register.Rax, 1);
    EmitGlobalStoreReg(X86Register.Rax, "__track_cleanup_count");

    // Print "CLEANUP: "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_cleanup");
    EmitMovRegImm(X86Register.Rdx, 9);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print tag
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitMovRegMem(X86Register.Rdx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cleanup_managed(buffer_in_rcx, var_tag_ptr_in_rdx, var_tag_len_in_r8)
  /// Combined cleanup function: prints CLEANUP, then if buffer != 0, prints DECREF + FREE and frees.
  /// Stack: [rbp-8]=capacity, [rbp-16]=buffer, [rbp-24]=tag_ptr, [rbp-32]=tag_len
  /// </summary>
  private void EmitMaxonCleanupManaged() {
    EmitRuntimeFunctionStart("maxon_cleanup_managed", 4, 0x60);

    // Call maxon_track_cleanup(tag_ptr, tag_len)
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // tag_ptr
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // tag_len
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_cleanup")); EmitDword(0);

    // Only free heap-allocated buffers (capacity > 0); rdata/constant buffers have capacity=0
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // capacity
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_cleanup_managed_done");

    // Buffer is heap-allocated: DECREF with rc=0
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // tag_ptr
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // tag_len
    EmitBytes(0x4D, 0x31, 0xC0); // XOR r8, r8 (rc=0)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_decref")); EmitDword(0);

    // FREE: call maxon_track_free(buffer, "array cleanup" ptr, len)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // buffer
    EmitLeaRegRipRel(X86Register.Rdx, "rdata_track_array_cleanup");
    EmitMovRegImm(X86Register.R8, 13); // len("array cleanup")
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_free")); EmitDword(0);

    // Actually free the memory
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // buffer
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel("rt_cleanup_managed_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cleanup_managed_free(capacity_rcx, buffer_rdx, tag_ptr_r8, tag_len_r9)
  /// Does the DECREF/FREE part of managed cleanup without printing the CLEANUP tag.
  /// Used when the CLEANUP tag has already been printed (e.g. for arrays with managed elements).
  /// Stack: [rbp-8]=capacity, [rbp-16]=buffer, [rbp-24]=tag_ptr, [rbp-32]=tag_len
  /// </summary>
  private void EmitMaxonCleanupManagedFree() {
    EmitRuntimeFunctionStart("maxon_cleanup_managed_free", 4, 0x60);

    // Only free heap-allocated buffers (capacity > 0); rdata/constant buffers have capacity=0
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // capacity
    EmitBytes(0x48, 0x85, 0xC0); // TEST rax, rax
    EmitJcc("z", "rt_cleanup_managed_free_done");

    // Buffer is heap-allocated: DECREF with rc=0
    EmitMovRegMem(X86Register.Rcx, -0x18, 8); // tag_ptr
    EmitMovRegMem(X86Register.Rdx, -0x20, 8); // tag_len
    EmitBytes(0x4D, 0x31, 0xC0); // XOR r8, r8 (rc=0)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_decref")); EmitDword(0);

    // FREE: call maxon_track_free(buffer, "array cleanup" ptr, len)
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // buffer
    EmitLeaRegRipRel(X86Register.Rdx, "rdata_track_array_cleanup");
    EmitMovRegImm(X86Register.R8, 13); // len("array cleanup")
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_free")); EmitDword(0);

    // Actually free the memory
    EmitMovRegMem(X86Register.Rcx, -0x10, 8); // buffer
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel("rt_cleanup_managed_free_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_cleanup_array_elements(buffer_rcx, length_rdx, elem_size_r8, offsets_ptr_r9, num_offsets_rsi)
  /// Iterates over array elements and cleans up managed fields within each element.
  /// For each managed field offset in each element,
  /// prints CLEANUP: &lt;array element&gt; and frees the buffer if capacity > 0.
  /// Stack: [rbp-8]=buffer, [rbp-16]=length, [rbp-24]=elem_size, [rbp-32]=offsets_ptr,
  ///        [rbp-40]=num_offsets, [rbp-48]=loop_i, [rbp-56]=loop_j, [rbp-64]=elem_base
  /// </summary>
  private void EmitMaxonCleanupArrayElements() {
    EmitRuntimeFunctionStart("maxon_cleanup_array_elements", 5, 0x80);

    // Initialize outer loop counter i = 0
    EmitBytes(0x48, 0xC7, 0x45, 0xD0, 0x00, 0x00, 0x00, 0x00); // MOV qword [rbp-48], 0

    DefineLabel("rt_cleanup_elems_outer_loop");
    // Check if i >= length
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = i
    EmitBytes(0x48, 0x3B, 0x45, 0xF0);       // CMP RAX, [rbp-16] (length)
    EmitJcc("ge", "rt_cleanup_elems_done");

    // Compute elem_base = buffer + i * elem_size
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = i
    EmitBytes(0x48, 0x0F, 0xAF, 0x45, 0xE8); // IMUL RAX, [rbp-24] (elem_size)
    EmitBytes(0x48, 0x03, 0x45, 0xF8);        // ADD RAX, [rbp-8] (buffer)
    EmitMovMemReg(-0x40, X86Register.Rax, 8); // [rbp-64] = elem_base

    // Initialize inner loop counter j = 0
    EmitBytes(0x48, 0xC7, 0x45, 0xC8, 0x00, 0x00, 0x00, 0x00); // MOV qword [rbp-56], 0

    DefineLabel("rt_cleanup_elems_inner_loop");
    // Check if j >= num_offsets
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = j
    EmitBytes(0x48, 0x3B, 0x45, 0xD8);       // CMP RAX, [rbp-40] (num_offsets)
    EmitJcc("ge", "rt_cleanup_elems_inner_done");

    // CLEANUP: <array element> for the managed field
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_array_element");
    EmitMovRegImm(X86Register.Rdx, 15); // len("<array element>")
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_cleanup")); EmitDword(0);

    // Load managed field offset: offset = offsets_ptr[j]
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = j
    EmitBytes(0x48, 0xC1, 0xE0, 0x03);        // SHL RAX, 3 (j * 8)
    EmitBytes(0x48, 0x03, 0x45, 0xE0);        // ADD RAX, [rbp-32] (offsets_ptr)
    EmitBytes(0x48, 0x8B, 0x00);               // MOV RAX, [RAX] (offset value)

    // Compute mm_base = elem_base + offset
    EmitBytes(0x48, 0x03, 0x45, 0xC0);        // ADD RAX, [rbp-64] (elem_base)
    // RAX now points to the __ManagedMemory within the managed field

    // Check capacity: [mm_base + 16] (__ManagedMemory.capacity is at offset 16)
    EmitBytes(0x48, 0x8B, 0x48, 0x10);        // MOV RCX, [RAX+16] (capacity)
    EmitBytes(0x48, 0x85, 0xC9);               // TEST RCX, RCX
    EmitJcc("z", "rt_cleanup_elems_field_skip");

    // Capacity > 0: free the buffer at [mm_base + 0]
    EmitBytes(0x48, 0x8B, 0x08);               // MOV RCX, [RAX] (buffer pointer)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel("rt_cleanup_elems_field_skip");
    // j++
    EmitMovRegMem(X86Register.Rax, -0x38, 8); // RAX = j
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x38, X86Register.Rax, 8); // [rbp-56] = j + 1
    EmitJmp("rt_cleanup_elems_inner_loop");

    DefineLabel("rt_cleanup_elems_inner_done");
    // i++
    EmitMovRegMem(X86Register.Rax, -0x30, 8); // RAX = i
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x30, X86Register.Rax, 8); // [rbp-48] = i + 1
    EmitJmp("rt_cleanup_elems_outer_loop");

    DefineLabel("rt_cleanup_elems_done");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// maxon_print_alloc_summary()
  /// Prints a summary of all memory tracking stats:
  /// === MEMORY STATS ===
  /// Allocated: X bytes
  /// Freed:     X bytes
  /// Leaked:    X bytes
  /// Moves:     X
  /// Increfs:   X
  /// Decrefs:   X
  /// Copies:    X
  /// Cleanups:  X
  /// Stack: [rbp-8]=leaked_bytes (computed), [rbp-32]=number buffer
  /// </summary>
  private void EmitMaxonPrintAllocSummary() {
    EmitRuntimeFunctionStart("maxon_print_alloc_summary", 0, 0x80);

    // Print header: "\n=== MEMORY STATS ===\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_header");
    EmitMovRegImm(X86Register.Rdx, 22); // length
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Allocated: "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_allocated");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print allocated bytes
    EmitGlobalLoadReg(X86Register.Rcx, "__track_alloc_bytes");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print " bytes\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_bytes");
    EmitMovRegImm(X86Register.Rdx, 7); // " bytes\n" (skip the '(' from " bytes (")
    // Actually we need a proper " bytes\n" string
    // Let's print " bytes" part then newline
    EmitMovRegImm(X86Register.Rdx, 6); // " bytes"
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Freed:     "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_freed");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print freed bytes
    EmitGlobalLoadReg(X86Register.Rcx, "__track_freed_bytes");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print " bytes\n"
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_bytes");
    EmitMovRegImm(X86Register.Rdx, 6);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Compute and print leaked bytes (alloc - freed)
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_leaked");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitGlobalLoadReg(X86Register.Rax, "__track_alloc_bytes");
    EmitGlobalLoadReg(X86Register.Rcx, "__track_freed_bytes");
    EmitBytes(0x48, 0x29, 0xC8); // SUB rax, rcx => leaked = alloc - freed
    EmitMovMemReg(-0x08, X86Register.Rax, 8); // save for reference

    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_bytes");
    EmitMovRegImm(X86Register.Rdx, 6);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Moves:     "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_moves");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitGlobalLoadReg(X86Register.Rcx, "__track_move_count");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Increfs:   "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_increfs");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitGlobalLoadReg(X86Register.Rcx, "__track_incref_count");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Decrefs:   "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_decrefs");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitGlobalLoadReg(X86Register.Rcx, "__track_decref_count");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Copies:    "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_copies");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitGlobalLoadReg(X86Register.Rcx, "__track_copy_count");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    // Print "Cleanups:  "
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_summary_cleanups");
    EmitMovRegImm(X86Register.Rdx, 11);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitGlobalLoadReg(X86Register.Rcx, "__track_cleanup_count");
    EmitLeaRegMem(X86Register.Rdx, -0x20);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_i64_to_string")); EmitDword(0);
    EmitLeaRegMem(X86Register.Rcx, -0x20);
    EmitMovRegReg(X86Register.Rdx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);
    EmitLeaRegRipRel(X86Register.Rcx, "rdata_track_newline");
    EmitMovRegImm(X86Register.Rdx, 1);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_track_print_str")); EmitDword(0);

    EmitRuntimeFunctionEnd();
  }
}
