using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public record ImportEntry(string DllName, string FunctionName);

public class X86CodeEmitter {
  private readonly List<byte> _code = [];
  private readonly List<byte> _rdata = [];
  private readonly List<byte> _data = [];
  private readonly List<ImportEntry> _imports = [];

  // Label name -> code offset
  private readonly Dictionary<string, int> _labels = [];

  // Fixups: code offset -> target label name (for relative call/jmp)
  private readonly List<(int offset, string target)> _relCallFixups = [];

  // Jump fixups: code offset -> target label name (for Jcc and Jmp)
  private readonly List<(int offset, string target)> _jumpFixups = [];

  // Import fixups: code offset -> import index (for indirect calls through IAT)
  private readonly List<(int offset, int importIndex)> _importCallFixups = [];

  // Rdata fixups: code offset -> rdata label
  private readonly List<(int offset, string label)> _rdataFixups = [];

  // Rdata labels: label -> offset within rdata section
  private readonly Dictionary<string, int> _rdataLabels = [];

  // Global fixups: code offset -> global name
  private readonly List<(int offset, string name)> _globalFixups = [];

  // Global names: name -> offset within data section
  private readonly Dictionary<string, int> _globalLabels = [];

  // Function address fixups: code offset -> function name (for LEA of function addresses)
  private readonly List<(int offset, string funcName)> _funcAddrFixups = [];

  // Chkstk call sites for patching
  private readonly List<int> _chkstkCallSites = [];

  private string _currentFunction = "";

  public IReadOnlyList<ImportEntry> Imports => _imports;
  public bool HasRdata => _rdata.Count > 0;
  public bool HasGlobals => _data.Count > 0;
  public bool HasImports => _imports.Count > 0;

  // --- Label management ---

  public void DefineLabel(string name) {
    _labels[name] = _code.Count;
  }

  public void SetCurrentFunction(string name) {
    _currentFunction = name;
  }

  public string ScopedLabel(string blockName) {
    return $"{_currentFunction}.{blockName}";
  }

  // --- Data section management ---

  public void DefineRdata(string label, byte[] bytes, int alignment = 1) {
    if (alignment > 1) {
      var padding = (alignment - (_rdata.Count % alignment)) % alignment;
      for (var i = 0; i < padding; i++) _rdata.Add(0);
    }
    _rdataLabels[label] = _rdata.Count;
    _rdata.AddRange(bytes);
  }

  public void DefineGlobal(string name, int size, long initValue) {
    _globalLabels[name] = _data.Count;
    var bytes = new byte[size];
    if (initValue != 0) {
      var valueBytes = BitConverter.GetBytes(initValue);
      Array.Copy(valueBytes, bytes, Math.Min(valueBytes.Length, size));
    }
    _data.AddRange(bytes);
  }

  // --- Emit X86 operations ---

  public void Emit(X86Op op) {
    switch (op) {
      case X86PrologueOp prologue:
        EmitPushReg(X86Register.Rbp);
        EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
        if (prologue.StackSize > 0)
          EmitSubRegImm(X86Register.Rsp, prologue.StackSize);
        break;
      case X86EpilogueOp:
        EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
        EmitPopReg(X86Register.Rbp);
        break;
      case X86PushRegOp push:
        EmitPushReg(push.Register);
        break;
      case X86PopRegOp pop:
        EmitPopReg(pop.Register);
        break;
      case X86MovRegRegOp mov:
        EmitMovRegReg(mov.Dest, mov.Src);
        break;
      case X86XchgRegRegOp xchg:
        EmitXchgRegReg(xchg.A, xchg.B);
        break;
      case X86MovRegImmOp mov:
        EmitMovRegImm(mov.Dest, mov.Immediate);
        break;
      case X86RetOp:
        EmitByte(0xC3); // ret
        break;
      case X86SubRegImmOp sub:
        EmitSubRegImm(sub.Dest, sub.Immediate);
        break;
      case X86AddRegImmOp add:
        EmitAddRegImm(add.Dest, add.Immediate);
        break;
      case X86AddRegRegOp addReg:
        EmitAddRegReg(addReg.Dest, addReg.Src);
        break;
      case X86SubRegRegOp subReg:
        EmitSubRegReg(subReg.Dest, subReg.Src);
        break;
      case X86AndRegRegOp andReg:
        EmitAndRegReg(andReg.Dest, andReg.Src);
        break;
      case X86OrRegRegOp orReg:
        EmitOrRegReg(orReg.Dest, orReg.Src);
        break;
      case X86XorRegRegOp xor:
        EmitXorRegReg(xor.Dest, xor.Src);
        break;
      case X86ShlRegClOp shl:
        EmitShlRegCl(shl.Dest);
        break;
      case X86SarRegClOp sar:
        EmitSarRegCl(sar.Dest);
        break;
      case X86ImulRegRegOp imul:
        EmitImulRegReg(imul.Dest, imul.Src);
        break;
      case X86CallDirectOp call:
        EmitByte(0xE8); // call rel32
        _relCallFixups.Add((_code.Count, call.Target));
        EmitDword(0); // placeholder, patched by ResolveLabels
        break;
      case X86CallIndirectOp callIndirect:
        EmitCallIndirect(callIndirect.Target);
        break;
      case X86MovMemRegOp movMem:
        EmitMovMemReg(movMem.Displacement, movMem.Src, movMem.SizeInBytes);
        break;
      case X86MovMemRspRegOp movMemRsp:
        EmitMovMemRspReg(movMemRsp.Offset, movMemRsp.Src);
        break;
      case X86MovRegMemOp movReg:
        EmitMovRegMem(movReg.Dest, movReg.Displacement, movReg.SizeInBytes);
        break;
      case X86MovSdXmmRipRelOp movsd:
        EmitMovSdXmmRipRel(movsd.Dest, movsd.RdataLabel);
        break;
      case X86MovSdMemXmmOp movsd:
        EmitMovSdMemXmm(movsd.Displacement, movsd.Src);
        break;
      case X86MovSdXmmMemOp movsd:
        EmitMovSdXmmMem(movsd.Dest, movsd.Displacement);
        break;
      case X86CmpRegRegOp cmp:
        EmitCmpRegReg(cmp.Lhs, cmp.Rhs);
        break;
      case X86TestRegRegOp test:
        EmitTestRegReg(test.Lhs, test.Rhs);
        break;
      case X86AddSdOp addsd:
        EmitAddSd(addsd.Dest, addsd.Src);
        break;
      case X86SubSdOp subsd:
        EmitSubSd(subsd.Dest, subsd.Src);
        break;
      case X86MulSdOp mulsd:
        EmitMulSd(mulsd.Dest, mulsd.Src);
        break;
      case X86DivSdOp divsd:
        EmitDivSd(divsd.Dest, divsd.Src);
        break;
      case X86CvttSd2SiOp cvttsd2si:
        EmitCvttSd2Si(cvttsd2si.Dest, cvttsd2si.Src);
        break;
      case X86CvtSi2SdOp cvtsi2sd:
        EmitCvtSi2Sd(cvtsi2sd.Dest, cvtsi2sd.Src);
        break;
      case X86AndpdRipRelOp andpd:
        EmitAndpdRipRel(andpd.Dest, andpd.RdataLabel);
        break;
      case X86SqrtSdOp sqrtsd:
        EmitSqrtSd(sqrtsd.Dest, sqrtsd.Src);
        break;
      case X86RoundSdOp roundsd:
        EmitRoundSd(roundsd.Dest, roundsd.Src, roundsd.Mode);
        break;
      case X86MinSdOp minsd:
        EmitMinSd(minsd.Dest, minsd.Src);
        break;
      case X86MaxSdOp maxsd:
        EmitMaxSd(maxsd.Dest, maxsd.Src);
        break;
      case X86MovSdXmmXmmOp movsdReg:
        EmitMovSdXmmXmm(movsdReg.Dest, movsdReg.Src);
        break;
      case X86UcomisdOp ucomisd:
        EmitUcomisd(ucomisd.Src1, ucomisd.Src2);
        break;
      case X86SetccOp setcc:
        EmitSetcc(setcc.Condition, setcc.Dest);
        break;
      case X86MovzxRegOp movzx:
        EmitMovzxReg8To64(movzx.Dest);
        break;
      case X86JccOp jcc:
        EmitJcc(jcc.Condition, jcc.Target);
        break;
      case X86JmpOp jmp:
        EmitJmp(jmp.Target);
        break;
      case X86CqoOp:
        EmitBytes(0x48, 0x99); // CQO: REX.W + 99
        break;
      case X86IdivRegOp idiv:
        EmitIdivReg(idiv.Divisor);
        break;
      case X86LeaRegMemOp lea:
        EmitLeaRegMem(lea.Dest, lea.Displacement);
        break;
      case X86LeaRipRelOp leaRip:
        EmitLeaRegRipRel(leaRip.Dest, leaRip.RdataLabel);
        break;
      case X86LeaFuncAddrOp leaFunc:
        EmitLeaFuncAddr(leaFunc.Dest, leaFunc.FunctionName);
        break;
      case X86MovIndirectMemRegOp movInd:
        EmitMovIndirectMemReg(movInd.BaseReg, movInd.Displacement, movInd.Src);
        break;
      case X86MovRegIndirectMemOp movInd:
        EmitMovRegIndirectMem(movInd.Dest, movInd.BaseReg, movInd.Displacement);
        break;
      case X86MovSdIndirectMemXmmOp movsdInd:
        EmitMovSdIndirectMemXmm(movsdInd.BaseReg, movsdInd.Displacement, movsdInd.Src);
        break;
      case X86MovSdXmmIndirectMemOp movsdInd:
        EmitMovSdXmmIndirectMem(movsdInd.Dest, movsdInd.BaseReg, movsdInd.Displacement);
        break;
      case X86MovzxRegByteIndirectOp movzxByte:
        EmitMovzxRegByteIndirect(movzxByte.Dest, movzxByte.BaseReg, movzxByte.Displacement);
        break;
      case X86MovByteIndirectRegOp movByte:
        EmitMovByteIndirectReg(movByte.BaseReg, movByte.Displacement, movByte.Src);
        break;
      case X86GlobalLoadOp globalLoad:
        EmitGlobalLoadReg(globalLoad.Dest, globalLoad.GlobalName);
        break;
      case X86GlobalStoreOp globalStore:
        EmitGlobalStoreReg(globalStore.Src, globalStore.GlobalName);
        break;
      case X86GlobalLoadXmmOp globalLoadXmm:
        EmitGlobalLoadXmm(globalLoadXmm.Dest, globalLoadXmm.GlobalName);
        break;
      case X86GlobalStoreXmmOp globalStoreXmm:
        EmitGlobalStoreXmm(globalStoreXmm.Src, globalStoreXmm.GlobalName);
        break;
      case X86RepMovsbOp:
        EmitBytes(0xF3, 0xA4); // REP MOVSB
        break;
      case X86CallImportOp callImport:
        EmitCallImport(callImport.DllName, callImport.FunctionName);
        break;
      default:
        throw new InvalidOperationException($"No X86 emission for: {op.GetType().Name} ({op.Mnemonic})");
    }
  }

  // --- Start wrapper (_start entry point) ---

  public void EmitStartWrapper() {
    DefineLabel("_start");

    // sub rsp, 0x28 (shadow space + alignment for Windows x64 ABI)
    EmitBytes(0x48, 0x83, 0xEC, 0x28);

    // call main (relative call, patched later)
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "main"));
    EmitDword(0); // placeholder

    // mov ecx, eax (move return value to first arg for ExitProcess)
    EmitBytes(0x89, 0xC1);

    // call [rip+disp32] ExitProcess (indirect through IAT)
    EmitBytes(0xFF, 0x15);
    var exitProcessIndex = AddImport("kernel32.dll", "ExitProcess");
    _importCallFixups.Add((_code.Count, exitProcessIndex));
    EmitDword(0); // placeholder

    // int3 (should never reach here)
    EmitByte(0xCC);
  }

  // --- Runtime stubs ---

  public void EmitChkstk() {
    DefineLabel("__chkstk");
    // Minimal chkstk: just return. For large stack frames Windows requires
    // probing each page, but for basic.maxon this is sufficient.
    EmitByte(0xC3); // ret
  }

  /// <summary>
  /// Emit runtime allocation functions.
  /// Uses Windows HeapAlloc/HeapReAlloc/HeapFree via GetProcessHeap.
  /// </summary>
  public void EmitRuntimeFunctions() {
    // maxon_alloc(size_in_rcx) -> ptr_in_rax
    // Windows x64 ABI: RCX=arg1, RDX=arg2, R8=arg3, R9=arg4
    DefineLabel("maxon_alloc");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x30); // shadow space + alignment
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = size
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    // HeapAlloc(heap, HEAP_ZERO_MEMORY, size)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegImm(X86Register.Rdx, 0x08); // HEAP_ZERO_MEMORY
    EmitMovRegMem(X86Register.R8, -0x08, 8);
    EmitCallImport("kernel32.dll", "HeapAlloc");
    EmitEpilogue();

    // maxon_realloc(ptr_in_rcx, new_size_in_rdx) -> new_ptr_in_rax
    // If ptr is NULL, falls back to HeapAlloc (HeapReAlloc doesn't accept NULL).
    // Returns a new pointer — caller must update its buffer field.
    DefineLabel("maxon_realloc");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x30);
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = ptr
    EmitMovMemReg(-0x10, X86Register.Rdx, 8); // [rbp-16] = new_size
    // If ptr == 0, use HeapAlloc instead
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    var jnzReallocPath = EmitJccForward("nz");
    // Alloc path: HeapAlloc(heap, HEAP_ZERO_MEMORY, new_size)
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegImm(X86Register.Rdx, 0x08); // HEAP_ZERO_MEMORY
    EmitMovRegMem(X86Register.R8, -0x10, 8);
    EmitCallImport("kernel32.dll", "HeapAlloc");
    var jmpEpilogue = EmitJmpForward();
    // Realloc path: HeapReAlloc(heap, HEAP_ZERO_MEMORY, ptr, new_size)
    PatchForwardJump(jnzReallocPath);
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegImm(X86Register.Rdx, 0x08); // HEAP_ZERO_MEMORY
    EmitMovRegMem(X86Register.R8, -0x08, 8); // ptr
    EmitMovRegMem(X86Register.R9, -0x10, 8); // new_size
    EmitCallImport("kernel32.dll", "HeapReAlloc");
    EmitEpilogue(jmpEpilogue);

    // maxon_free(ptr_in_rcx)
    DefineLabel("maxon_free");
    // If ptr is 0, just return (noop)
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    var jzSkip = EmitJccForward("z");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x30);
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = ptr
    EmitCallImport("kernel32.dll", "GetProcessHeap");
    // HeapFree(heap, 0, ptr)
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitMovRegImm(X86Register.Rdx, 0);
    EmitMovRegMem(X86Register.R8, -0x08, 8);
    EmitCallImport("kernel32.dll", "HeapFree");
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    PatchForwardJump(jzSkip);
    EmitByte(0xC3); // ret

    // maxon_cow_check(buffer_in_rcx, capacity_in_rdx, length_in_r8, elemSize_in_r9) -> new_buffer_in_rax
    // If capacity != 0, buffer is already writable — return it as-is.
    // If capacity == 0, allocate length*elemSize bytes, copy from old buffer, return new buffer.
    DefineLabel("maxon_cow_check");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x40);
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = old buffer
    EmitMovMemReg(-0x10, X86Register.Rdx, 8); // [rbp-16] = capacity
    EmitMovMemReg(-0x18, X86Register.R8, 8);  // [rbp-24] = length
    EmitMovMemReg(-0x20, X86Register.R9, 8);  // [rbp-32] = elemSize
    // TEST rdx, rdx (check capacity)
    EmitBytes(0x48, 0x85, 0xD2);
    var jnzWritable = EmitJccForward("nz");
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
    var jmpCowEpilogue = EmitJmpForward();
    // already_writable: return old buffer
    PatchForwardJump(jnzWritable);
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitEpilogue(jmpCowEpilogue);

    // maxon_write_stdout(cstr_ptr_in_rcx) -> bytes_written_in_rax
    // Stack layout: [rbp-8]=cstr_ptr, [rbp-16]=length, [rbp-24]=handle, [rbp-32]=bytesWritten
    DefineLabel("maxon_write_stdout");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x40); // shadow space + locals
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = cstr_ptr
    // strlen: RDX = ptr copy, RAX = counter
    EmitMovRegReg(X86Register.Rdx, X86Register.Rcx); // RDX = ptr
    EmitMovRegImm(X86Register.Rax, 0); // RAX = 0 (counter)
    int strlenLoopPos = _code.Count;
    // movzx ecx, byte ptr [rdx+rax]: 0F B6 0C 02
    EmitBytes(0x0F, 0xB6, 0x0C, 0x02);
    // TEST CL, CL
    EmitBytes(0x84, 0xC9);
    var jzStrlenDone = EmitJccForward("z");
    // INC RAX
    EmitBytes(0x48, 0xFF, 0xC0);
    // JMP strlen_loop (backward)
    EmitByte(0xE9);
    EmitDword(strlenLoopPos - (_code.Count + 4));
    // done_strlen:
    PatchForwardJump(jzStrlenDone);
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
    EmitEpilogue();

    // maxon_i64_to_string(value_in_rcx, buffer_ptr_in_rdx) -> length_in_rax
    // Converts a signed 64-bit integer to its decimal string representation.
    // Writes into caller-provided buffer (must be >= 21 bytes). Returns byte count.
    // Stack: [rbp-8]=value, [rbp-16]=buffer, [rbp-24]=write_pos (end of buffer working backwards)
    DefineLabel("maxon_i64_to_string");
    EmitPushReg(X86Register.Rbp);
    EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
    EmitSubRegImm(X86Register.Rsp, 0x30);
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // [rbp-8] = value
    EmitMovMemReg(-0x10, X86Register.Rdx, 8); // [rbp-16] = buffer

    // Special case: value == 0
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    var jnzNotZero = EmitJccForward("nz");
    // Write '0' to buffer[0], null to buffer[1]
    EmitBytes(0xC6, 0x02, 0x30); // MOV byte [rdx], '0'
    EmitBytes(0xC6, 0x42, 0x01, 0x00); // MOV byte [rdx+1], 0
    EmitMovRegImm(X86Register.Rax, 1);
    var jmpZeroEpilogue = EmitJmpForward();

    // not_zero: check for negative
    PatchForwardJump(jnzNotZero);
    // R8 = 0 (is_negative flag)
    EmitBytes(0x4D, 0x31, 0xC0); // XOR r8, r8
    // TEST rcx, rcx / JS negative
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    var jnsPositive = EmitJccForward("ns");
    // Negate: rcx = -rcx
    EmitBytes(0x48, 0xF7, 0xD9); // NEG rcx
    EmitMovMemReg(-0x08, X86Register.Rcx, 8); // update stored value
    // R8 = 1 (is_negative)
    EmitMovRegImm(X86Register.R8, 1);

    // positive: R9 = buffer + 20 (write position, work backwards from end)
    PatchForwardJump(jnsPositive);
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
    // JNZ digit_loop (backward)
    EmitByte(0x0F);
    EmitByte(0x85);
    EmitDword(digitLoopPos - (_code.Count + 4));

    // If negative, prepend '-'
    EmitMovRegMem(X86Register.R8, -0x18, 8); // R8 = is_negative
    EmitBytes(0x4D, 0x85, 0xC0); // TEST r8, r8
    var jzNoSign = EmitJccForward("z");
    EmitBytes(0x49, 0xFF, 0xC9); // DEC r9
    EmitBytes(0x41, 0xC6, 0x01, 0x2D); // MOV byte [r9], '-'

    // no_sign: copy from R9 to buffer start, compute length
    PatchForwardJump(jzNoSign);
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

    // epilogue (zero and non-zero paths converge here)
    EmitEpilogue(jmpZeroEpilogue);
  }

  private void EmitCallImport(string dllName, string functionName) {
    // call [rip+disp32] (indirect call through IAT)
    EmitBytes(0xFF, 0x15);
    var importIndex = AddImport(dllName, functionName);
    _importCallFixups.Add((_code.Count, importIndex));
    EmitDword(0); // placeholder
  }

  public void PatchChkstkCalls() {
    if (!_labels.TryGetValue("__chkstk", out var chkstkOffset)) return;
    foreach (var site in _chkstkCallSites) {
      var rel = chkstkOffset - (site + 4);
      PatchDword(site, rel);
    }
  }

  // --- Resolution ---

  public void ResolveLabels() {
    foreach (var (offset, target) in _relCallFixups) {
      if (!_labels.TryGetValue(target, out var targetOffset)) {
        throw new InvalidOperationException($"Unresolved label: {target}");
      }
      var rel = targetOffset - (offset + 4);
      PatchDword(offset, rel);
    }
    foreach (var (offset, target) in _jumpFixups) {
      if (!_labels.TryGetValue(target, out var targetOffset)) {
        throw new InvalidOperationException($"Unresolved jump target: {target}");
      }
      var rel = targetOffset - (offset + 4);
      PatchDword(offset, rel);
    }
    foreach (var (offset, funcName) in _funcAddrFixups) {
      if (!_labels.TryGetValue(funcName, out var funcOffset)) {
        throw new InvalidOperationException($"Unresolved function address: {funcName}");
      }
      var rel = funcOffset - (offset + 4);
      PatchDword(offset, rel);
    }
  }

  public void ResolveRdata(int rvaOffset) {
    // rvaOffset = distance from end of code bytes to start of rdata in virtual memory
    // Target position relative to code start = _code.Count + rvaOffset + rdataOffset
    // RIP-relative: target - (fixupOffset + 4)
    var codeSize = _code.Count;
    foreach (var (offset, label) in _rdataFixups) {
      if (!_rdataLabels.TryGetValue(label, out var rdataOffset)) {
        throw new InvalidOperationException($"Unresolved rdata label: {label}");
      }
      var target = codeSize + rvaOffset + rdataOffset;
      var rel = target - (offset + 4);
      PatchDword(offset, rel);
    }
  }

  public void ResolveGlobals(int rvaOffset) {
    var codeSize = _code.Count;
    foreach (var (offset, name) in _globalFixups) {
      if (!_globalLabels.TryGetValue(name, out var globalOffset)) {
        throw new InvalidOperationException($"Unresolved global: {name}");
      }
      var target = codeSize + rvaOffset + globalOffset;
      var rel = target - (offset + 4);
      PatchDword(offset, rel);
    }
  }

  public void ResolveImports(int rvaOffset) {
    // IAT entries are 8 bytes each. Import index i is at IAT offset i*8.
    var codeSize = _code.Count;
    foreach (var (offset, importIndex) in _importCallFixups) {
      var iatEntryOffset = importIndex * 8;
      var target = codeSize + rvaOffset + iatEntryOffset;
      var rel = target - (offset + 4);
      PatchDword(offset, rel);
    }
  }

  // --- Output ---

  public byte[] GetCode() => [.. _code];
  public byte[] GetRdata() => [.. _rdata];
  public byte[] GetData() => [.. _data];

  // --- Private helpers ---

  private int AddImport(string dllName, string functionName) {
    var index = _imports.FindIndex(i => i.DllName == dllName && i.FunctionName == functionName);
    if (index >= 0) return index;
    _imports.Add(new ImportEntry(dllName, functionName));
    return _imports.Count - 1;
  }

  private void EmitByte(byte b) {
    _code.Add(b);
  }

  private void EmitBytes(params byte[] bytes) {
    _code.AddRange(bytes);
  }

  private void EmitDword(int value) {
    _code.AddRange(BitConverter.GetBytes(value));
  }

  private void PatchDword(int offset, int value) {
    var bytes = BitConverter.GetBytes(value);
    _code[offset] = bytes[0];
    _code[offset + 1] = bytes[1];
    _code[offset + 2] = bytes[2];
    _code[offset + 3] = bytes[3];
  }

  // --- X86 encoding helpers ---

  private static bool Is64BitReg(X86Register reg) {
    return reg >= X86Register.Rax && reg <= X86Register.R15;
  }

  private static bool Is32BitReg(X86Register reg) {
    return reg >= X86Register.Eax && reg <= X86Register.Edi;
  }

  private static int RegCode(X86Register reg) {
    return reg switch {
      X86Register.Rax or X86Register.Eax => 0,
      X86Register.Rcx or X86Register.Ecx => 1,
      X86Register.Rdx or X86Register.Edx => 2,
      X86Register.Rbx or X86Register.Ebx => 3,
      X86Register.Rsp or X86Register.Esp => 4,
      X86Register.Rbp or X86Register.Ebp => 5,
      X86Register.Rsi or X86Register.Esi => 6,
      X86Register.Rdi or X86Register.Edi => 7,
      X86Register.R8 => 0,
      X86Register.R9 => 1,
      X86Register.R10 => 2,
      X86Register.R11 => 3,
      X86Register.R12 => 4,
      X86Register.R13 => 5,
      X86Register.R14 => 6,
      X86Register.R15 => 7,
      _ => throw new ArgumentException($"Unknown register: {reg}")
    };
  }

  private struct Rex {
    private byte _rex;
    private bool _needed;

    public static Rex W() => new() { _rex = 0x48, _needed = true };
    public static Rex NoW() => new() { _rex = 0x40, _needed = false };

    // ModRM.reg field (REX.R bit)
    public Rex Reg(X86Register reg) {
      if (IsExtended(reg)) { _rex |= 0x04; _needed = true; }
      return this;
    }

    // ModRM.r/m field (REX.B bit)
    public Rex Rm(X86Register reg) {
      if (IsExtended(reg)) { _rex |= 0x01; _needed = true; }
      return this;
    }

    // ModRM.reg field for XMM register code (REX.R bit)
    public Rex Reg(int xmmCode) {
      if (xmmCode >= 8) { _rex |= 0x04; _needed = true; }
      return this;
    }

    // ModRM.r/m field for XMM register code (REX.B bit)
    public Rex Rm(int xmmCode) {
      if (xmmCode >= 8) { _rex |= 0x01; _needed = true; }
      return this;
    }

    // Force REX for byte operations on RSP/RBP/RSI/RDI (access SPL/BPL/SIL/DIL)
    public Rex ByteReg(X86Register reg) {
      if (NeedsByteRex(reg)) _needed = true;
      return this;
    }

    // Always emit (use when REX.W is set)
    public readonly void Emit(X86CodeEmitter e) => e.EmitByte(_rex);

    // Emit only if needed (use when REX.W is not set)
    public readonly void EmitIf(X86CodeEmitter e) { if (_needed) e.EmitByte(_rex); }

    private static bool IsExtended(X86Register reg) =>
      reg >= X86Register.R8 && reg <= X86Register.R15;

    // Registers with codes 4-7 (SP, BP, SI, DI) need a REX prefix for byte
    // operations to access SPL/BPL/SIL/DIL instead of AH/CH/DH/BH.
    private static bool NeedsByteRex(X86Register reg) =>
      reg is X86Register.Esp or X86Register.Rsp
        or X86Register.Ebp or X86Register.Rbp
        or X86Register.Esi or X86Register.Rsi
        or X86Register.Edi or X86Register.Rdi;
  }

  private static void Require64BitGpr(X86Register reg, string caller) {
    if (!Is64BitReg(reg))
      throw new ArgumentException($"{caller}: expected 64-bit register, got {reg}");
  }

  private static void RequireGpr(X86Register reg, string caller) {
    if (!Is64BitReg(reg) && !Is32BitReg(reg))
      throw new ArgumentException($"{caller}: unsupported register size: {reg}");
  }

  private void EmitPushReg(X86Register reg) {
    Require64BitGpr(reg, nameof(EmitPushReg));
    Rex.NoW().Rm(reg).EmitIf(this);
    EmitByte((byte)(0x50 + RegCode(reg)));
  }

  private void EmitPopReg(X86Register reg) {
    Require64BitGpr(reg, nameof(EmitPopReg));
    Rex.NoW().Rm(reg).EmitIf(this);
    EmitByte((byte)(0x58 + RegCode(reg)));
  }

  private void EmitXchgRegReg(X86Register a, X86Register b) {
    bool a64 = Is64BitReg(a), a32 = Is32BitReg(a);
    bool b64 = Is64BitReg(b), b32 = Is32BitReg(b);

    if (!a64 && !a32)
      throw new ArgumentException($"EmitXchgRegReg: unsupported register size for a: {a}");
    if (!b64 && !b32)
      throw new ArgumentException($"EmitXchgRegReg: unsupported register size for b: {b}");

    if (a64 && b64) {
      // XCHG r64, r64: REX.W 87 /r
      Rex.W().Reg(a).Rm(b).Emit(this);
      EmitByte(0x87);
      EmitByte((byte)(0xC0 | (RegCode(a) << 3) | RegCode(b)));
    } else if (a32 && b32) {
      // XCHG r32, r32: 87 /r
      EmitByte(0x87);
      EmitByte((byte)(0xC0 | (RegCode(a) << 3) | RegCode(b)));
    } else {
      throw new ArgumentException($"EmitXchgRegReg: register sizes must match (got {a} and {b})");
    }
  }

  private void EmitMovRegReg(X86Register dest, X86Register src) {
    bool dest64 = Is64BitReg(dest), dest32 = Is32BitReg(dest);
    bool src64 = Is64BitReg(src), src32 = Is32BitReg(src);

    if (!dest64 && !dest32)
      throw new ArgumentException($"EmitMovRegReg: unsupported dest register size: {dest}");
    if (!src64 && !src32)
      throw new ArgumentException($"EmitMovRegReg: unsupported src register size: {src}");

    // Always use 64-bit MOV to preserve pointer values that flow through
    // i64 registers (the register allocator uses 32-bit names from GprPool
    // but values may be 64-bit pointers).
    {
      Rex.W().Reg(src).Rm(dest).Emit(this);
      EmitByte(0x89);
      EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
    }
  }

  private void EmitMovRegImm(X86Register dest, long immediate) {
    if (!Is64BitReg(dest) && !Is32BitReg(dest))
      throw new ArgumentException($"EmitMovRegImm: unsupported register size: {dest}");
    // For 32-bit register names, negative values require the 64-bit
    // sign-extending form, since mov r32, imm32 zero-extends to 64 bits.
    if (Is32BitReg(dest) && immediate < 0) {
      // MOV r64, imm32 (sign-extended): REX.W + C7 /0 id
      Rex.W().Rm(dest).Emit(this);
      EmitByte(0xC7);
      EmitByte((byte)(0xC0 | RegCode(dest)));
      EmitDword((int)immediate);
    } else if (Is64BitReg(dest)) {
      if (immediate >= int.MinValue && immediate <= int.MaxValue) {
        // MOV r64, imm32 (sign-extended): REX.W + C7 /0 id
        Rex.W().Rm(dest).Emit(this);
        EmitByte(0xC7);
        EmitByte((byte)(0xC0 | RegCode(dest)));
        EmitDword((int)immediate);
      } else {
        // MOV r64, imm64: REX.W + B8+rd io
        Rex.W().Rm(dest).Emit(this);
        EmitByte((byte)(0xB8 + RegCode(dest)));
        _code.AddRange(BitConverter.GetBytes(immediate));
      }
    } else {
      // MOV r32, imm32: B8+rd id (non-negative values only)
      EmitByte((byte)(0xB8 + RegCode(dest)));
      EmitDword((int)immediate);
    }
  }

  private void EmitSubRegImm(X86Register dest, long immediate) {
    Require64BitGpr(dest, nameof(EmitSubRegImm));
    if (immediate >= -128 && immediate <= 127) {
      // SUB r64, imm8: REX.W + 83 /5 ib
      Rex.W().Rm(dest).Emit(this);
      EmitByte(0x83);
      EmitByte((byte)(0xE8 | RegCode(dest)));
      EmitByte((byte)immediate);
    } else {
      // SUB r64, imm32: REX.W + 81 /5 id
      Rex.W().Rm(dest).Emit(this);
      EmitByte(0x81);
      EmitByte((byte)(0xE8 | RegCode(dest)));
      EmitDword((int)immediate);
    }
  }

  private void EmitAddRegImm(X86Register dest, long immediate) {
    Require64BitGpr(dest, nameof(EmitAddRegImm));
    if (immediate >= -128 && immediate <= 127) {
      // ADD r64, imm8: REX.W + 83 /0 ib
      Rex.W().Rm(dest).Emit(this);
      EmitByte(0x83);
      EmitByte((byte)(0xC0 | RegCode(dest)));
      EmitByte((byte)immediate);
    } else {
      // ADD r64, imm32: REX.W + 81 /0 id
      Rex.W().Rm(dest).Emit(this);
      EmitByte(0x81);
      EmitByte((byte)(0xC0 | RegCode(dest)));
      EmitDword((int)immediate);
    }
  }

  private void EmitAddRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitAddRegReg));
    RequireGpr(src, nameof(EmitAddRegReg));
    // ADD r/m64, r64: REX.W + 01 /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(src).Rm(dest).Emit(this);
    EmitByte(0x01);
    EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
  }

  private void EmitSubRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitSubRegReg));
    RequireGpr(src, nameof(EmitSubRegReg));
    // SUB r/m64, r64: REX.W + 29 /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(src).Rm(dest).Emit(this);
    EmitByte(0x29);
    EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
  }

  private void EmitXorRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitXorRegReg));
    RequireGpr(src, nameof(EmitXorRegReg));
    // XOR r32, r32: 31 /r  (implicitly zero-extends to 64 bits)
    Rex.NoW().Reg(src).Rm(dest).EmitIf(this);
    EmitByte(0x31);
    EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
  }

  private void EmitAndRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitAndRegReg));
    RequireGpr(src, nameof(EmitAndRegReg));
    // AND r/m64, r64: REX.W + 21 /r
    Rex.W().Reg(src).Rm(dest).Emit(this);
    EmitByte(0x21);
    EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
  }

  private void EmitOrRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitOrRegReg));
    RequireGpr(src, nameof(EmitOrRegReg));
    // OR r/m64, r64: REX.W + 09 /r
    Rex.W().Reg(src).Rm(dest).Emit(this);
    EmitByte(0x09);
    EmitByte((byte)(0xC0 | (RegCode(src) << 3) | RegCode(dest)));
  }

  private void EmitShlRegCl(X86Register dest) {
    RequireGpr(dest, nameof(EmitShlRegCl));
    // SHL r/m64, CL: REX.W + D3 /4
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xD3);
    EmitByte((byte)(0xC0 | (4 << 3) | RegCode(dest)));
  }

  private void EmitSarRegCl(X86Register dest) {
    RequireGpr(dest, nameof(EmitSarRegCl));
    // SAR r/m64, CL: REX.W + D3 /7 (arithmetic right shift, preserves sign)
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xD3);
    EmitByte((byte)(0xC0 | (7 << 3) | RegCode(dest)));
  }

  private void EmitCmpRegReg(X86Register lhs, X86Register rhs) {
    RequireGpr(lhs, nameof(EmitCmpRegReg));
    RequireGpr(rhs, nameof(EmitCmpRegReg));
    // CMP r/m64, r64: REX.W + 39 /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(rhs).Rm(lhs).Emit(this);
    EmitByte(0x39);
    EmitByte((byte)(0xC0 | (RegCode(rhs) << 3) | RegCode(lhs)));
  }

  private void EmitTestRegReg(X86Register lhs, X86Register rhs) {
    RequireGpr(lhs, nameof(EmitTestRegReg));
    RequireGpr(rhs, nameof(EmitTestRegReg));
    // TEST r/m64, r64: REX.W + 85 /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(rhs).Rm(lhs).Emit(this);
    EmitByte(0x85);
    EmitByte((byte)(0xC0 | (RegCode(rhs) << 3) | RegCode(lhs)));
  }

  private void EmitSetcc(string condition, X86Register dest) {
    RequireGpr(dest, nameof(EmitSetcc));
    byte condOpcode = ConditionToOpcode(condition);
    byte setccOpcode = (byte)(0x90 | (condOpcode & 0x0F));
    Rex.NoW().Rm(dest).ByteReg(dest).EmitIf(this);
    EmitByte(0x0F);
    EmitByte(setccOpcode);
    EmitByte((byte)(0xC0 | RegCode(dest)));
  }

  private void EmitMovzxReg8To64(X86Register dest) {
    RequireGpr(dest, nameof(EmitMovzxReg8To64));
    // MOVZX r64, r8: REX.W + 0F B6 /r (same register for src and dest)
    Rex.W().Reg(dest).Rm(dest).Emit(this);
    EmitByte(0x0F);
    EmitByte(0xB6);
    EmitByte((byte)(0xC0 | (RegCode(dest) << 3) | RegCode(dest)));
  }

  private static byte ConditionToOpcode(string condition) => condition switch {
    "e" or "z" => 0x84,
    "ne" or "nz" => 0x85,
    "b" or "c" => 0x82,
    "ae" or "nc" => 0x83,
    "be" => 0x86,
    "a" => 0x87,
    "s" => 0x88,
    "ns" => 0x89,
    "p" or "pe" => 0x8A,
    "np" or "po" => 0x8B,
    "l" => 0x8C,
    "ge" => 0x8D,
    "le" => 0x8E,
    "g" => 0x8F,
    _ => throw new InvalidOperationException($"Unsupported condition: {condition}")
  };

  private void EmitImulRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitImulRegReg));
    RequireGpr(src, nameof(EmitImulRegReg));
    // IMUL r64, r/m64: REX.W + 0F AF /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(dest).Rm(src).Emit(this);
    EmitByte(0x0F);
    EmitByte(0xAF);
    EmitByte((byte)(0xC0 | (RegCode(dest) << 3) | RegCode(src)));
  }

  private void EmitCallIndirect(X86Register target) {
    RequireGpr(target, nameof(EmitCallIndirect));
    // CALL r/m64: FF /2 (call near, absolute indirect)
    // ModR/M byte: 11 010 rrr (mod=11 for register, /2=010, r/m=register code)
    Rex.NoW().Rm(target).EmitIf(this);
    EmitByte(0xFF);
    EmitByte((byte)(0xD0 | RegCode(target))); // 11 010 rrr = 0xD0 | reg
  }

  private void EmitIdivReg(X86Register divisor) {
    RequireGpr(divisor, nameof(EmitIdivReg));
    // IDIV r/m64: REX.W + F7 /7 (REX.W promotes 32-bit register names)
    Rex.W().Rm(divisor).Emit(this);
    EmitByte(0xF7);
    EmitByte((byte)(0xF8 | RegCode(divisor))); // /7 = 111 in reg field
  }

  private void EmitMovMemReg(int displacement, X86Register src, int sizeInBytes) {
    RequireGpr(src, nameof(EmitMovMemReg));
    switch (sizeInBytes) {
      case 1: {
        // MOV [rbp+disp], r8: 88 /r (uses low byte of register)
        // REX needed for extended regs (R8-R15) and to access sil/dil/bpl/spl (codes 4-7)
        Rex.NoW().Reg(src).ByteReg(src).EmitIf(this);
        EmitByte(0x88);
        break;
      }
      case 4:
        // MOV [rbp+disp], r32: 89 /r (no REX.W = 32-bit operand)
        Rex.NoW().Reg(src).EmitIf(this);
        EmitByte(0x89);
        break;
      case 8:
        // MOV [rbp+disp], r64: REX.W + 89 /r
        Rex.W().Reg(src).Emit(this);
        EmitByte(0x89);
        break;
      default:
        throw new InvalidOperationException($"EmitMovMemReg: unsupported size {sizeInBytes}");
    }
    if (displacement >= -128 && displacement <= 127) {
      EmitByte((byte)(0x45 | (RegCode(src) << 3))); // mod=01, r/m=rbp(5)
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte((byte)(0x85 | (RegCode(src) << 3))); // mod=10, r/m=rbp(5)
      EmitDword(displacement);
    }
  }

  private void EmitMovRegMem(X86Register dest, int displacement, int sizeInBytes) {
    RequireGpr(dest, nameof(EmitMovRegMem));
    switch (sizeInBytes) {
      case 1:
        // MOVZX r64, byte [rbp+disp]: REX.W + 0F B6 /r
        Rex.W().Reg(dest).Emit(this);
        EmitByte(0x0F);
        EmitByte(0xB6);
        break;
      case 4:
        // MOV r32, [rbp+disp]: 8B /r (no REX.W = zero-extends to r64)
        Rex.NoW().Reg(dest).EmitIf(this);
        EmitByte(0x8B);
        break;
      case 8:
        // MOV r64, [rbp+disp]: REX.W + 8B /r
        Rex.W().Reg(dest).Emit(this);
        EmitByte(0x8B);
        break;
      default:
        throw new InvalidOperationException($"EmitMovRegMem: unsupported size {sizeInBytes}");
    }
    if (displacement >= -128 && displacement <= 127) {
      EmitByte((byte)(0x45 | (RegCode(dest) << 3))); // mod=01, r/m=rbp(5)
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte((byte)(0x85 | (RegCode(dest) << 3))); // mod=10, r/m=rbp(5)
      EmitDword(displacement);
    }
  }

  private void EmitMovMemRspReg(int offset, X86Register src) {
    RequireGpr(src, nameof(EmitMovMemRspReg));
    // MOV [rsp+offset], r64: REX.W + 89 /r with SIB byte (rsp addressing requires SIB)
    Rex.W().Reg(src).Emit(this);
    EmitByte(0x89);
    if (offset == 0) {
      EmitByte((byte)(0x04 | (RegCode(src) << 3))); // mod=00, r/m=100 (SIB follows)
      EmitByte(0x24); // SIB: scale=00, index=rsp(100), base=rsp(100)
    } else if (offset >= -128 && offset <= 127) {
      EmitByte((byte)(0x44 | (RegCode(src) << 3))); // mod=01, r/m=100 (SIB follows)
      EmitByte(0x24); // SIB: scale=00, index=rsp(100), base=rsp(100)
      EmitByte((byte)(offset & 0xFF));
    } else {
      EmitByte((byte)(0x84 | (RegCode(src) << 3))); // mod=10, r/m=100 (SIB follows)
      EmitByte(0x24); // SIB: scale=00, index=rsp(100), base=rsp(100)
      EmitDword(offset);
    }
  }

  // --- SSE2 encoding helpers ---

  private static int XmmRegCode(X86XmmRegister reg) {
    return (int)reg;
  }

  /// <summary>
  /// Emit an SSE/SSE2 XMM-to-XMM register instruction: prefix [REX] opcode ModRM
  /// </summary>
  private void EmitXmmRegRegOp(byte prefix, byte opcode1, byte opcode2, int destCode, int srcCode) {
    EmitByte(prefix);
    Rex.NoW().Reg(destCode).Rm(srcCode).EmitIf(this);
    EmitBytes(opcode1, opcode2);
    EmitByte((byte)(0xC0 | ((destCode & 7) << 3) | (srcCode & 7)));
  }

  private void EmitMovSdXmmRipRel(X86XmmRegister dest, string rdataLabel) {
    // MOVSD xmm, [rip+disp32]: F2 0F 10 /r (ModRM: mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(dest);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x10);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _rdataFixups.Add((_code.Count, rdataLabel));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitMovSdMemXmm(int displacement, X86XmmRegister src) {
    // MOVSD [rbp+disp], xmm: F2 0F 11 /r
    var reg = XmmRegCode(src);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x11);
    if (displacement >= -128 && displacement <= 127) {
      EmitByte((byte)(0x45 | ((reg & 7) << 3))); // mod=01, r/m=rbp(5)
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte((byte)(0x85 | ((reg & 7) << 3))); // mod=10, r/m=rbp(5)
      EmitDword(displacement);
    }
  }

  private void EmitMovSdXmmMem(X86XmmRegister dest, int displacement) {
    // MOVSD xmm, [rbp+disp]: F2 0F 10 /r
    var reg = XmmRegCode(dest);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x10);
    if (displacement >= -128 && displacement <= 127) {
      EmitByte((byte)(0x45 | ((reg & 7) << 3)));
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte((byte)(0x85 | ((reg & 7) << 3)));
      EmitDword(displacement);
    }
  }

  private void EmitAddSd(X86XmmRegister dest, X86XmmRegister src) {
    // ADDSD xmm, xmm: F2 0F 58 /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x58, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitSubSd(X86XmmRegister dest, X86XmmRegister src) {
    // SUBSD xmm, xmm: F2 0F 5C /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x5C, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitMulSd(X86XmmRegister dest, X86XmmRegister src) {
    // MULSD xmm, xmm: F2 0F 59 /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x59, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitDivSd(X86XmmRegister dest, X86XmmRegister src) {
    // DIVSD xmm, xmm: F2 0F 5E /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x5E, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitCvttSd2Si(X86Register dest, X86XmmRegister src) {
    RequireGpr(dest, nameof(EmitCvttSd2Si));
    // CVTTSD2SI r64, xmm: F2 REX.W 0F 2C /r
    var d = RegCode(dest);
    var s = XmmRegCode(src);
    EmitByte(0xF2);
    Rex.W().Reg(dest).Rm(s).Emit(this);
    EmitBytes(0x0F, 0x2C);
    EmitByte((byte)(0xC0 | (d << 3) | (s & 7)));
  }

  private void EmitCvtSi2Sd(X86XmmRegister dest, X86Register src) {
    RequireGpr(src, nameof(EmitCvtSi2Sd));
    // CVTSI2SD xmm, r64: F2 REX.W 0F 2A /r
    var d = XmmRegCode(dest);
    var s = RegCode(src);
    EmitByte(0xF2);
    Rex.W().Reg(d).Rm(src).Emit(this);
    EmitBytes(0x0F, 0x2A);
    EmitByte((byte)(0xC0 | ((d & 7) << 3) | (s & 7)));
  }

  private void EmitAndpdRipRel(X86XmmRegister dest, string rdataLabel) {
    // ANDPD xmm, [rip+disp32]: 66 0F 54 /r (ModRM: mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(dest);
    EmitByte(0x66);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x54);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _rdataFixups.Add((_code.Count, rdataLabel));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitSqrtSd(X86XmmRegister dest, X86XmmRegister src) {
    // SQRTSD xmm, xmm: F2 0F 51 /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x51, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitRoundSd(X86XmmRegister dest, X86XmmRegister src, byte mode) {
    // ROUNDSD xmm, xmm, imm8: 66 0F 3A 0B /r ib
    var d = XmmRegCode(dest);
    var s = XmmRegCode(src);
    EmitByte(0x66);
    Rex.NoW().Reg(d).Rm(s).EmitIf(this);
    EmitBytes(0x0F, 0x3A, 0x0B);
    EmitByte((byte)(0xC0 | ((d & 7) << 3) | (s & 7)));
    EmitByte(mode);
  }

  private void EmitMinSd(X86XmmRegister dest, X86XmmRegister src) {
    // MINSD xmm, xmm: F2 0F 5D /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x5D, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitMaxSd(X86XmmRegister dest, X86XmmRegister src) {
    // MAXSD xmm, xmm: F2 0F 5F /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x5F, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitMovSdXmmXmm(X86XmmRegister dest, X86XmmRegister src) {
    // MOVSD xmm, xmm: F2 0F 10 /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x10, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitUcomisd(X86XmmRegister src1, X86XmmRegister src2) {
    // UCOMISD xmm, xmm: 66 0F 2E /r
    EmitXmmRegRegOp(0x66, 0x0F, 0x2E, XmmRegCode(src1), XmmRegCode(src2));
  }

  private void EmitJcc(string condition, string target) {
    // Jcc rel32: 0F 8x rel32
    byte opcode = ConditionToOpcode(condition);
    EmitByte(0x0F);
    EmitByte(opcode);
    _jumpFixups.Add((_code.Count, target));
    EmitDword(0); // placeholder for rel32
  }

  private void EmitJmp(string target) {
    // JMP rel32: E9 rel32
    EmitByte(0xE9);
    _jumpFixups.Add((_code.Count, target));
    EmitDword(0); // placeholder
  }

  // --- Forward jump helpers for runtime stubs ---

  private int EmitJmpForward() {
    EmitByte(0xE9); // JMP rel32
    var patchOffset = _code.Count;
    EmitDword(0);
    return patchOffset;
  }

  private int EmitJccForward(string condition) {
    byte opcode = ConditionToOpcode(condition);
    EmitByte(0x0F);
    EmitByte(opcode);
    var patchOffset = _code.Count;
    EmitDword(0);
    return patchOffset;
  }

  private void PatchForwardJump(int patchOffset) {
    var rel = _code.Count - (patchOffset + 4);
    PatchDword(patchOffset, rel);
  }

  private void EmitEpilogue(params int[] forwardJumps) {
    foreach (var patchOffset in forwardJumps)
      PatchForwardJump(patchOffset);
    EmitMovRegReg(X86Register.Rsp, X86Register.Rbp);
    EmitPopReg(X86Register.Rbp);
    EmitByte(0xC3); // ret
  }

  // --- Struct support: LEA and indirect memory operations ---

  private void EmitLeaRegMem(X86Register dest, int displacement) {
    RequireGpr(dest, nameof(EmitLeaRegMem));
    // LEA r64, [rbp+disp]: REX.W + 8D /r
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8D);
    if (displacement >= -128 && displacement <= 127) {
      EmitByte((byte)(0x45 | (RegCode(dest) << 3))); // mod=01, r/m=rbp(5)
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte((byte)(0x85 | (RegCode(dest) << 3))); // mod=10, r/m=rbp(5)
      EmitDword(displacement);
    }
  }

  private void EmitLeaRegRipRel(X86Register dest, string rdataLabel) {
    RequireGpr(dest, nameof(EmitLeaRegRipRel));
    // LEA r64, [rip+disp32]: REX.W + 8D /r (mod=00, r/m=101 for RIP-relative)
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8D);
    EmitByte((byte)(0x05 | (RegCode(dest) << 3))); // mod=00, r/m=101 (RIP-relative)
    _rdataFixups.Add((_code.Count, rdataLabel));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitLeaFuncAddr(X86Register dest, string functionName) {
    RequireGpr(dest, nameof(EmitLeaFuncAddr));
    // LEA r64, [rip+disp32]: REX.W + 8D /r (mod=00, r/m=101 for RIP-relative)
    // Uses function address fixup instead of rdata fixup
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8D);
    EmitByte((byte)(0x05 | (RegCode(dest) << 3))); // mod=00, r/m=101 (RIP-relative)
    _funcAddrFixups.Add((_code.Count, functionName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitMovIndirectMemReg(X86Register baseReg, int displacement, X86Register src) {
    RequireGpr(baseReg, nameof(EmitMovIndirectMemReg));
    RequireGpr(src, nameof(EmitMovIndirectMemReg));
    // MOV [baseReg+disp], r64: REX.W + 89 /r
    Rex.W().Reg(src).Rm(baseReg).Emit(this);
    EmitByte(0x89);
    EmitModRmWithBase(baseReg, src, displacement);
  }

  private void EmitMovRegIndirectMem(X86Register dest, X86Register baseReg, int displacement) {
    RequireGpr(dest, nameof(EmitMovRegIndirectMem));
    RequireGpr(baseReg, nameof(EmitMovRegIndirectMem));
    // MOV r64, [baseReg+disp]: REX.W + 8B /r
    Rex.W().Reg(dest).Rm(baseReg).Emit(this);
    EmitByte(0x8B);
    EmitModRmWithBase(baseReg, dest, displacement);
  }

  private void EmitMovzxRegByteIndirect(X86Register dest, X86Register baseReg, int displacement) {
    RequireGpr(dest, nameof(EmitMovzxRegByteIndirect));
    RequireGpr(baseReg, nameof(EmitMovzxRegByteIndirect));
    // MOVZX r64, byte ptr [baseReg+disp]: REX.W + 0F B6 /r
    Rex.W().Reg(dest).Rm(baseReg).Emit(this);
    EmitBytes(0x0F, 0xB6);
    EmitModRmWithBase(baseReg, dest, displacement);
  }

  private void EmitMovByteIndirectReg(X86Register baseReg, int displacement, X86Register src) {
    RequireGpr(baseReg, nameof(EmitMovByteIndirectReg));
    RequireGpr(src, nameof(EmitMovByteIndirectReg));
    // MOV byte ptr [baseReg+disp], src8: REX + 88 /r
    Rex.NoW().Reg(src).Rm(baseReg).Emit(this);
    EmitByte(0x88);
    EmitModRmWithBase(baseReg, src, displacement);
  }

  private void EmitMovSdIndirectMemXmm(X86Register baseReg, int displacement, X86XmmRegister src) {
    RequireGpr(baseReg, nameof(EmitMovSdIndirectMemXmm));
    // MOVSD [baseReg+disp], xmm: F2 [REX] 0F 11 /r
    var reg = XmmRegCode(src);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).Rm(baseReg).EmitIf(this);
    EmitBytes(0x0F, 0x11);
    EmitModRmWithBaseXmm(baseReg, reg, displacement);
  }

  private void EmitMovSdXmmIndirectMem(X86XmmRegister dest, X86Register baseReg, int displacement) {
    RequireGpr(baseReg, nameof(EmitMovSdXmmIndirectMem));
    // MOVSD xmm, [baseReg+disp]: F2 [REX] 0F 10 /r
    var reg = XmmRegCode(dest);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).Rm(baseReg).EmitIf(this);
    EmitBytes(0x0F, 0x10);
    EmitModRmWithBaseXmm(baseReg, reg, displacement);
  }

  /// <summary>
  /// Emit ModR/M (and SIB if needed) for [baseReg+displacement] addressing.
  /// The reg field comes from the GPR operand.
  /// RSP-based addressing requires a SIB byte.
  /// </summary>
  private void EmitModRmWithBase(X86Register baseReg, X86Register regOperand, int displacement) {
    int baseCode = RegCode(baseReg);
    int regCode = RegCode(regOperand);
    bool needsSib = baseCode == 4; // RSP/R12 need SIB byte

    if (displacement == 0 && baseCode != 5) {
      // mod=00 (no displacement, except RBP/R13 which always need disp8)
      EmitByte((byte)((regCode << 3) | baseCode));
      if (needsSib) EmitByte(0x24);
    } else if (displacement >= -128 && displacement <= 127) {
      // mod=01 (8-bit displacement)
      EmitByte((byte)(0x40 | (regCode << 3) | baseCode));
      if (needsSib) EmitByte(0x24);
      EmitByte((byte)(displacement & 0xFF));
    } else {
      // mod=10 (32-bit displacement)
      EmitByte((byte)(0x80 | (regCode << 3) | baseCode));
      if (needsSib) EmitByte(0x24);
      EmitDword(displacement);
    }
  }

  /// <summary>
  /// Same as EmitModRmWithBase but for XMM register in the reg field.
  /// </summary>
  private void EmitModRmWithBaseXmm(X86Register baseReg, int xmmCode, int displacement) {
    int baseCode = RegCode(baseReg);
    int regCode = xmmCode & 7;
    bool needsSib = baseCode == 4;

    if (displacement == 0 && baseCode != 5) {
      EmitByte((byte)((regCode << 3) | baseCode));
      if (needsSib) EmitByte(0x24);
    } else if (displacement >= -128 && displacement <= 127) {
      EmitByte((byte)(0x40 | (regCode << 3) | baseCode));
      if (needsSib) EmitByte(0x24);
      EmitByte((byte)(displacement & 0xFF));
    } else {
      EmitByte((byte)(0x80 | (regCode << 3) | baseCode));
      if (needsSib) EmitByte(0x24);
      EmitDword(displacement);
    }
  }

  // --- Global variable load/store (RIP-relative addressing) ---

  private void EmitGlobalLoadReg(X86Register dest, string globalName) {
    RequireGpr(dest, nameof(EmitGlobalLoadReg));
    // MOV r64, [rip+disp32]: REX.W + 8B /r (mod=00, r/m=101 for RIP-relative)
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8B);
    EmitByte((byte)(0x05 | (RegCode(dest) << 3))); // mod=00, r/m=101 (RIP-relative)
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalStoreReg(X86Register src, string globalName) {
    RequireGpr(src, nameof(EmitGlobalStoreReg));
    // MOV [rip+disp32], r64: REX.W + 89 /r (mod=00, r/m=101 for RIP-relative)
    Rex.W().Reg(src).Emit(this);
    EmitByte(0x89);
    EmitByte((byte)(0x05 | (RegCode(src) << 3))); // mod=00, r/m=101 (RIP-relative)
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalLoadXmm(X86XmmRegister dest, string globalName) {
    // MOVSD xmm, [rip+disp32]: F2 [REX] 0F 10 /r (mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(dest);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x10);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalStoreXmm(X86XmmRegister src, string globalName) {
    // MOVSD [rip+disp32], xmm: F2 [REX] 0F 11 /r (mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(src);
    EmitByte(0xF2);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x11);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }
}
