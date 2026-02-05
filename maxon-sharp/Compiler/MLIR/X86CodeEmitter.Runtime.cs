using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {
  private static readonly X86Register[] _abiArgRegs =
    [X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9];

  public void EmitRuntimeFunctions() {
    EmitMaxonAlloc();
    EmitMaxonRealloc();
    EmitMaxonFree();
    EmitMaxonCowCheck();
    EmitMaxonWriteStdout();
    EmitMaxonI64ToString();
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
    EmitByte(0x75); // JNZ realloc_path
    int jnzPatchPos = _code.Count;
    EmitByte(0x00); // placeholder
    // Alloc path
    EmitHeapCall("HeapAlloc", 0x08, rbpSlotR8: -0x10); // HEAP_ZERO_MEMORY
    EmitByte(0xEB); // JMP epilogue
    int jmpEpiloguePatchPos = _code.Count;
    EmitByte(0x00); // placeholder
    // Realloc path
    _code[jnzPatchPos] = (byte)(_code.Count - (jnzPatchPos + 1));
    EmitHeapCall("HeapReAlloc", 0x08, rbpSlotR8: -0x08, rbpSlotR9: -0x10); // HEAP_ZERO_MEMORY
    EmitRuntimeFunctionEnd(jmpEpiloguePatchPos);
  }

  /// <summary>maxon_free(ptr_in_rcx) — null-safe, skips free if ptr is 0</summary>
  private void EmitMaxonFree() {
    EmitRuntimeFunctionStart("maxon_free", 1);
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitByte(0x74); // JZ skip
    int jzPatchPos = _code.Count;
    EmitByte(0x00); // placeholder
    EmitHeapCall("HeapFree", 0, rbpSlotR8: -0x08);
    _code[jzPatchPos] = (byte)(_code.Count - (jzPatchPos + 1));
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
    EmitByte(0x75); // JNZ already_writable
    int jnzWritable = _code.Count;
    EmitByte(0x00); // placeholder
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
    EmitByte(0xEB); // JMP epilogue
    int jmpCowEpilogue = _code.Count;
    EmitByte(0x00); // placeholder
    // already_writable: return old buffer
    _code[jnzWritable] = (byte)(_code.Count - (jnzWritable + 1));
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitRuntimeFunctionEnd(jmpCowEpilogue);
  }

  /// <summary>maxon_write_stdout(cstr_ptr_in_rcx) -> bytes_written_in_rax</summary>
  private void EmitMaxonWriteStdout() {
    // Stack layout: [rbp-8]=cstr_ptr, [rbp-16]=length, [rbp-24]=handle, [rbp-32]=bytesWritten
    EmitRuntimeFunctionStart("maxon_write_stdout", 1, 0x40);
    // strlen: RDX = ptr copy, RAX = counter
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx); // RDX = ptr
    EmitMovRegImm(X86Register.Rax, 0); // RAX = 0 (counter)
    int strlenLoopPos = _code.Count;
    // movzx ecx, byte ptr [rdx+rax]: 0F B6 0C 02
    EmitBytes(0x0F, 0xB6, 0x0C, 0x02);
    // TEST CL, CL
    EmitBytes(0x84, 0xC9);
    // JZ done_strlen
    EmitByte(0x74);
    int jzStrlenDone = _code.Count;
    EmitByte(0x00); // placeholder
    // INC RAX
    EmitBytes(0x48, 0xFF, 0xC0);
    // JMP strlen_loop
    EmitByte(0xEB);
    EmitByte((byte)((strlenLoopPos - _code.Count) - 1));
    // done_strlen:
    _code[jzStrlenDone] = (byte)(_code.Count - (jzStrlenDone + 1));
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
    EmitByte(0x75); // JNZ not_zero
    int jnzNotZero = _code.Count;
    EmitByte(0x00); // placeholder
    // Write '0' to buffer[0], null to buffer[1]
    EmitBytes(0xC6, 0x02, 0x30); // MOV byte [rdx], '0'
    EmitBytes(0xC6, 0x42, 0x01, 0x00); // MOV byte [rdx+1], 0
    EmitMovRegImm(X86Register.Rax, 1);
    EmitByte(0xEB); // JMP epilogue
    int jmpZeroEpilogue = _code.Count;
    EmitByte(0x00); // placeholder

    // not_zero: check for negative
    _code[jnzNotZero] = (byte)(_code.Count - (jnzNotZero + 1));
    // R8 = 0 (is_negative flag)
    EmitBytes(0x4D, 0x31, 0xC0); // XOR r8, r8
    // TEST rcx, rcx / JS negative
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitByte(0x79); // JNS positive
    int jnsPositive = _code.Count;
    EmitByte(0x00); // placeholder
    // Negate: rcx = -rcx
    EmitBytes(0x48, 0xF7, 0xD9); // NEG rcx
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // update stored value
    // R8 = 1 (is_negative)
    EmitMovRegImm(X86Register.R8, 1);

    // positive: R9 = buffer + 20 (write position, work backwards from end)
    _code[jnsPositive] = (byte)(_code.Count - (jnsPositive + 1));
    EmitMovRegReg(X86Register.R9, X86Register.Rdx); // R9 = buffer
    EmitAddRegImm(X86Register.R9, 20); // R9 = buffer + 20
    EmitBytes(0x41, 0xC6, 0x01, 0x00); // MOV byte [r9], 0 (null terminator)

    // Save is_negative flag to stack
    EmitMovMemReg(-0x18, X86Register.R8, 8); // [rbp-24] = is_negative

    // digit_loop: divide rcx by 10, write remainder as digit
    int digitLoopPos = _code.Count;
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
    EmitByte(0x75); // JNZ digit_loop
    EmitByte((byte)((digitLoopPos - _code.Count) - 1));

    // If negative, prepend '-'
    EmitMovRegMem(X86Register.R8, -0x18, 8); // R8 = is_negative
    EmitBytes(0x4D, 0x85, 0xC0); // TEST r8, r8
    EmitByte(0x74); // JZ no_sign
    int jzNoSign = _code.Count;
    EmitByte(0x00); // placeholder
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitBytes(0x41, 0xC6, 0x01, 0x2D); // MOV byte [r9], '-'

    // no_sign: copy from R9 to buffer start, compute length
    _code[jzNoSign] = (byte)(_code.Count - (jzNoSign + 1));
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

    EmitRuntimeFunctionEnd(jmpZeroEpilogue);
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

  /// <summary>
  /// Patch any short jumps targeting the epilogue, then emit mov rsp,rbp / pop rbp / ret.
  /// </summary>
  private void EmitRuntimeFunctionEnd(params int[] patchPositions) {
    foreach (var pos in patchPositions)
      _code[pos] = (byte)(_code.Count - (pos + 1));
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
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
