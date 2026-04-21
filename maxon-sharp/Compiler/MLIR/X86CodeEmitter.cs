using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir;

public record ImportEntry(string DllName, string FunctionName);

public partial class X86CodeEmitter() {
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

  // Symdata section: symbol table for stack traces (separate from rdata)
  private readonly List<byte> _symdata = [];
  private readonly Dictionary<string, int> _symdataLabels = [];
  private readonly List<(int offset, string label)> _symdataFixups = [];

  // Ucddata section: Unicode Character Database tables (read-only, separate from rdata)
  private readonly List<byte> _ucddata = [];
  private readonly Dictionary<string, int> _ucddataLabels = [];
  private readonly List<(int offset, string label)> _ucddataFixups = [];

  // Jump table fixups: entries stored in rdata that point to code labels.
  // Each entry is (slotIndex within table, code label, table base rdata label).
  private readonly List<(int slotIndex, string codeLabel, string tableLabel)> _jumpTableFixups = [];

  // Chkstk call sites for patching
  private readonly List<int> _chkstkCallSites = [];

  // Labels of runtime helper functions that should appear in the COFF symbol table.
  private readonly List<string> _runtimeFunctionLabels = [];
  public IReadOnlyList<string> RuntimeFunctionLabels => _runtimeFunctionLabels;

  // Counter for generating unique __gt_past_morestack labels
  private int _morestackLabelCounter;

  private string _currentFunction = "";

  public IReadOnlyList<ImportEntry> Imports => _imports;
  public bool HasRdata => _rdata.Count > 0;
  public bool HasGlobals => _data.Count > 0;
  public bool HasImports => _imports.Count > 0;
  public bool HasSymdata => _symdata.Count > 0;
  public bool HasUcddata => _ucddata.Count > 0;

  // --- Label management ---

  public void DefineLabel(string name) {
    _labels[name] = _code.Count;
    if (name.Contains("runAllSpecTests"))
      Logger.Debug(LogCategory.Codegen, $"LABEL: {name} at code offset {_code.Count} (0x{_code.Count:X})");
  }

  public void SetCurrentFunction(string name) {
    _currentFunction = name;
  }

  public string ScopedLabel(string blockName) {
    return $"{_currentFunction}.{blockName}";
  }

  // --- Data section management ---

  public void DefineRdata(string label, byte[] bytes, int alignment = 1) {
    if (_rdataLabels.TryGetValue(label, out int oldOffset)) {
      var oldBytes = System.Text.Encoding.UTF8.GetString([.. _rdata], oldOffset, Math.Min(64, _rdata.Count - oldOffset)).TrimEnd('\0');
      var newBytes = System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(64, bytes.Length)).TrimEnd('\0');
      Logger.Debug(LogCategory.Codegen, $"WARNING: Duplicate rdata label '{label}' - old[{oldOffset}]='{oldBytes}' new[{_rdata.Count}]='{newBytes}'");
    }
    if (alignment > 1) {
      var padding = (alignment - (_rdata.Count % alignment)) % alignment;
      for (var i = 0; i < padding; i++) _rdata.Add(0);
    }
    _rdataLabels[label] = _rdata.Count;
    _rdata.AddRange(bytes);
  }

  public void DefineSymdata(string label, byte[] bytes, int alignment = 1) {
    if (alignment > 1) {
      var padding = (alignment - (_symdata.Count % alignment)) % alignment;
      for (var i = 0; i < padding; i++) _symdata.Add(0);
    }
    _symdataLabels[label] = _symdata.Count;
    _symdata.AddRange(bytes);
  }

  public void DefineUcddata(string label, byte[] bytes, int alignment = 1) {
    if (alignment > 1) {
      var padding = (alignment - (_ucddata.Count % alignment)) % alignment;
      for (var i = 0; i < padding; i++) _ucddata.Add(0);
    }
    _ucddataLabels[label] = _ucddata.Count;
    _ucddata.AddRange(bytes);
  }

  public void DefineGlobal(string name, int size, long initValue) {
    // Align to natural alignment (1->1, 4->4, 8->8)
    var alignment = size >= 8 ? 8 : size >= 4 ? 4 : size >= 2 ? 2 : 1;
    var padding = (alignment - (_data.Count % alignment)) % alignment;
    for (var i = 0; i < padding; i++) _data.Add(0);
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
        // Green thread stack growth check: before pushing rbp, verify we have enough
        // stack space. If not, call __gt_morestack which relocates the stack.
        // On the main thread, stackguard=0 so the check always passes (RSP > 0).
        if (prologue.StackSize > 0) {
          var pastLabel = $"__gt_past_morestack_{_morestackLabelCounter++}";
          // LEA R10, [RSP - frameSize] — compute what RSP would be after allocation
          if (prologue.StackSize <= 127) {
            Rex.W().Reg(X86Register.R10).Emit(this);
            EmitByte(0x8D); EmitByte(0x54); EmitByte(0x24); // LEA R10, [RSP + disp8]
            EmitByte((byte)(-(int)prologue.StackSize & 0xFF));
          } else {
            Rex.W().Reg(X86Register.R10).Emit(this);
            EmitByte(0x8D); EmitByte(0x94); EmitByte(0x24); // LEA R10, [RSP + disp32]
            EmitDword(-(int)prologue.StackSize);
          }
          // Load current green thread via inline TLS (no function call, preserves arg regs)
          EmitLoadCurrentGtInline(X86Register.R11);
          // CMP R10, [R11 + 0x50] (stackguard)
          Rex.W().Reg(X86Register.R10).Rm(X86Register.R11).Emit(this);
          EmitByte(0x3B); // CMP r64, r/m64
          EmitByte((byte)(0x40 | (RegCode(X86Register.R10) << 3) | RegCode(X86Register.R11))); // mod=01 (disp8)
          EmitByte(0x50); // disp8 = 0x50
          // JAE past_morestack (skip if R10 >= stackguard)
          EmitJcc("ae", pastLabel);
          // CALL __gt_morestack
          EmitByte(0xE8);
          _relCallFixups.Add((_code.Count, "__gt_morestack"));
          EmitDword(0);
          DefineLabel(pastLabel);
        }
        EmitPushReg(X86Register.Rbp);
        EmitMovRegReg(X86Register.Rbp, X86Register.Rsp);
        if (prologue.StackSize > 4096) {
          // Large frames need __chkstk to probe guard pages
          EmitMovRegImm(X86Register.Rax, prologue.StackSize);
          EmitByte(0xE8); // call rel32
          _chkstkCallSites.Add(_code.Count);
          EmitDword(0); // placeholder, patched by PatchChkstkCalls
          EmitSubRegReg(X86Register.Rsp, X86Register.Rax);
        } else if (prologue.StackSize > 0) {
          EmitSubRegImm(X86Register.Rsp, prologue.StackSize);
        }
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
      case X86MovsxdOp movsxd:
        EmitMovsxd(movsxd.Dest, movsxd.Src);
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
      case X86ShrRegClOp shr:
        EmitShrRegCl(shr.Dest);
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
      case X86MovXmmRipRelOp mov:
        EmitMovXmmRipRel(mov.Dest, mov.RdataLabel, mov.Precision);
        break;
      case X86MovMemXmmOp mov:
        EmitMovMemXmm(mov.Displacement, mov.Src, mov.Precision);
        break;
      case X86MovXmmMemOp mov:
        EmitMovXmmMem(mov.Dest, mov.Displacement, mov.Precision);
        break;
      case X86CmpRegRegOp cmp:
        EmitCmpRegReg(cmp.Lhs, cmp.Rhs);
        break;
      case X86TestRegRegOp test:
        EmitTestRegReg(test.Lhs, test.Rhs);
        break;
      case X86AddXmmOp add:
        EmitAddXmm(add.Dest, add.Src, add.Precision);
        break;
      case X86SubXmmOp sub:
        EmitSubXmm(sub.Dest, sub.Src, sub.Precision);
        break;
      case X86MulXmmOp mul:
        EmitMulXmm(mul.Dest, mul.Src, mul.Precision);
        break;
      case X86DivXmmOp div:
        EmitDivXmm(div.Dest, div.Src, div.Precision);
        break;
      case X86CvttFloat2SiOp cvtt:
        EmitCvttFloat2Si(cvtt.Dest, cvtt.Src, cvtt.Precision);
        break;
      case X86MovqXmmToGprOp movq:
        EmitMovqXmmToGpr(movq.Dest, movq.Src);
        break;
      case X86CvtSi2FloatOp cvt:
        EmitCvtSi2Float(cvt.Dest, cvt.Src, cvt.Precision);
        break;
      case X86AndMaskRipRelOp andMask:
        EmitAndMaskRipRel(andMask.Dest, andMask.RdataLabel, andMask.Precision);
        break;
      case X86SqrtXmmOp sqrt:
        EmitSqrtXmm(sqrt.Dest, sqrt.Src, sqrt.Precision);
        break;
      case X86RoundXmmOp round:
        EmitRoundXmm(round.Dest, round.Src, round.Mode, round.Precision);
        break;
      case X86MinXmmOp min:
        EmitMinXmm(min.Dest, min.Src, min.Precision);
        break;
      case X86MaxXmmOp max:
        EmitMaxXmm(max.Dest, max.Src, max.Precision);
        break;
      case X86MovXmmXmmOp movXmm:
        EmitMovXmmXmm(movXmm.Dest, movXmm.Src, movXmm.Precision);
        break;
      case X86UcomisXmmOp ucomis:
        EmitUcomisXmm(ucomis.Src1, ucomis.Src2, ucomis.Precision);
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
      case X86JumpTableOp jumpTable:
        EmitJumpTableDispatch(jumpTable);
        break;
      case X86LabelDefOp labelDef:
        DefineLabel(labelDef.Name);
        break;
      case X86CqoOp:
        EmitBytes(0x48, 0x99); // CQO: REX.W + 99
        break;
      case X86CdqOp:
        EmitByte(0x99); // CDQ: 32-bit IDIV needs EDX:EAX; no REX.W keeps it 32-bit (vs CQO)
        break;
      case X86IdivRegOp idiv:
        EmitIdivReg(idiv.Divisor);
        break;
      case X86IdivReg32Op idiv32:
        EmitIdivReg32(idiv32.Divisor);
        break;
      case X86DivRegOp div:
        EmitDivReg(div.Divisor);
        break;
      case X86DivReg32Op div32:
        EmitDivReg32(div32.Divisor);
        break;
      case X86LeaRegMemOp lea:
        EmitLeaRegMem(lea.Dest, lea.Displacement);
        break;
      case X86LeaRipRelOp leaRip:
        EmitLeaRegRipRel(leaRip.Dest, leaRip.RdataLabel);
        break;
      case X86LeaSymdataRelOp leaSymdata:
        EmitLeaRegSymdataRel(leaSymdata.Dest, leaSymdata.SymdataLabel);
        break;
      case X86LeaUcddataRelOp leaUcdOp:
        EmitLeaRegUcddataRel(leaUcdOp.Dest, leaUcdOp.UcddataLabel);
        break;
      case X86LeaFuncAddrOp leaFunc:
        EmitLeaFuncAddr(leaFunc.Dest, leaFunc.FunctionName);
        break;
      case X86LeaRegRegRegOp leaRRR:
        EmitLeaRegRegReg(leaRRR.Dest, leaRRR.BaseReg, leaRRR.Index);
        break;
      case X86MovIndirectMemRegOp movInd:
        EmitMovIndirectMemReg(movInd.BaseReg, movInd.Displacement, movInd.Src);
        break;
      case X86MovRegIndirectMemOp movInd:
        EmitMovRegIndirectMem(movInd.Dest, movInd.BaseReg, movInd.Displacement);
        break;
      case X86MovIndirectMemXmmOp movInd:
        EmitMovIndirectMemXmm(movInd.BaseReg, movInd.Displacement, movInd.Src, movInd.Precision);
        break;
      case X86MovXmmIndirectMemOp movInd:
        EmitMovXmmIndirectMem(movInd.Dest, movInd.BaseReg, movInd.Displacement, movInd.Precision);
        break;
      case X86MovzxRegByteIndirectOp movzxByte:
        EmitMovzxRegByteIndirect(movzxByte.Dest, movzxByte.BaseReg, movzxByte.Displacement);
        break;
      case X86MovByteIndirectRegOp movByte:
        EmitMovByteIndirectReg(movByte.BaseReg, movByte.Displacement, movByte.Src);
        break;
      case X86MovzxRegWordIndirectOp movzxWord:
        EmitMovzxRegWordIndirect(movzxWord.Dest, movzxWord.BaseReg, movzxWord.Displacement);
        break;
      case X86MovWordIndirectRegOp movWord:
        EmitMovWordIndirectReg(movWord.BaseReg, movWord.Displacement, movWord.Src);
        break;
      case X86GlobalLoadOp globalLoad:
        EmitGlobalLoadReg(globalLoad.Dest, globalLoad.GlobalName, globalLoad.Size);
        break;
      case X86GlobalStoreOp globalStore:
        EmitGlobalStoreReg(globalStore.Src, globalStore.GlobalName, globalStore.Size);
        break;
      case X86GlobalLoadXmmOp globalLoadXmm:
        EmitGlobalLoadXmm(globalLoadXmm.Dest, globalLoadXmm.GlobalName, globalLoadXmm.Precision);
        break;
      case X86GlobalStoreXmmOp globalStoreXmm:
        EmitGlobalStoreXmm(globalStoreXmm.Src, globalStoreXmm.GlobalName, globalStoreXmm.Precision);
        break;
      case X86RepMovsbOp:
        EmitBytes(0xF3, 0xA4); // REP MOVSB
        break;
      case X86StdOp:
        EmitByte(0xFD); // STD
        break;
      case X86CldOp:
        EmitByte(0xFC); // CLD
        break;
      case X86RepStosqOp:
        EmitBytes(0xF3, 0x48, 0xAB); // REP STOSQ
        break;
      case X86CallImportOp callImport:
        EmitCallImport(callImport.DllName, callImport.FunctionName);
        break;
      case X86CmovneRegRegOp cmovne:
        EmitCmovneRegReg(cmovne.Dest, cmovne.Src);
        break;
      case X86CvtSd2SsOp cvtsd2ss:
        EmitCvtSd2Ss(cvtsd2ss.Dest, cvtsd2ss.Src);
        break;
      case X86CvtSs2SdOp cvtss2sd:
        EmitCvtSs2Sd(cvtss2sd.Dest, cvtss2sd.Src);
        break;
      default:
        throw new InvalidOperationException($"No X86 emission for: {op.GetType().Name} ({op.Mnemonic})");
    }
  }

  // --- Start wrapper (_start entry point) ---

  public void EmitStartWrapper(string mainFunctionName, string? globalCleanupFunctionName = null, string? moduleInitFunctionName = null) {
    DefineLabel("mrt_start");

    // sub rsp, 0x28 (0x20 shadow space + 0x8 local storage, keeps 16-byte alignment)
    // [rsp+0x20] = main return value
    EmitBytes(0x48, 0x83, 0xEC, 0x28);

    // Initialize green thread scheduler first — the stack growth check in every
    // function prologue dereferences __gt_current, which must be non-NULL before
    // any user code runs (including module initialization).
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "__gt_init"));
    EmitDword(0);

    // Allocate a dedicated 8KB system stack for P[0] (same as P[1..N])
    EmitXorRegReg(X86Register.Rcx, X86Register.Rcx);     // lpAddress = NULL
    EmitMovRegImm(X86Register.Rdx, 0x2000);               // dwSize = 8KB
    EmitMovRegImm(X86Register.R8, 0x3000);                 // MEM_COMMIT|MEM_RESERVE
    EmitMovRegImm(X86Register.R9, 0x04);                   // PAGE_READWRITE
    EmitCallImport("kernel32.dll", "VirtualAlloc");
    EmitAddRegImm(X86Register.Rax, 0x2000);                // RAX = top of system stack
    EmitGlobalLoadReg(X86Register.R10, "__sched_tls_teb_offset");
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R10, X86Register.R10, 0); // R10 = P[0]*
    EmitMovIndirectMemReg(X86Register.R10, POffSystemStackSP, X86Register.Rax);

    // Initialize I/O subsystem (IOCP + worker threads) after green thread scheduler
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "__io_init"));
    EmitDword(0);

    // Initialize debugstream (checks MAXON_DEBUGSTREAM env var, opens shared memory)
    if (Compiler.DebugStream) {
      EmitByte(0xE8);
      _relCallFixups.Add((_code.Count, "__debugstream_init"));
      EmitDword(0);
    }

    // call __module_init (initializes globals)
    if (!string.IsNullOrEmpty(moduleInitFunctionName)) {
      EmitByte(0xE8);
      _relCallFixups.Add((_code.Count, moduleInitFunctionName));
      EmitDword(0);
    }

    // call main
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, mainFunctionName));
    EmitDword(0);
    // Save main's return value at [rsp+0x20]
    EmitBytes(0x48, 0x89, 0x44, 0x24, 0x20); // MOV [rsp+0x20], rax

    // Drain any remaining green threads
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "__gt_cleanup"));
    EmitDword(0);

    // Shut down I/O subsystem after all green threads have finished
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "__io_shutdown"));
    EmitDword(0);

    // Optionally clean up globals
    if (!string.IsNullOrEmpty(globalCleanupFunctionName)) {
      EmitByte(0xE8);
      _relCallFixups.Add((_code.Count, globalCleanupFunctionName));
      EmitDword(0);
    }

    // Shut down debugstream before leak check (so monitor sees the shutdown)
    if (Compiler.DebugStream) {
      EmitByte(0xE8);
      _relCallFixups.Add((_code.Count, "__debugstream_shutdown"));
      EmitDword(0);
    }

    // mm_leak_check(exit_code) — report any leaked allocations, returns 101 if leaked or original exit_code
    EmitBytes(0x48, 0x8B, 0x4C, 0x24, 0x20); // MOV rcx, [rsp+0x20] (main's return value as arg0)
    EmitByte(0xE8);
    _relCallFixups.Add((_code.Count, "mm_leak_check"));
    EmitDword(0);

    // RAX = exit code from mm_leak_check (101 if leaked, original exit code otherwise)
    EmitBytes(0x48, 0x89, 0xC1); // MOV rcx, rax

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
    // Probes each 4K page between current rsp and rsp-rax to trigger
    // guard page expansion on Windows. rax = allocation size (preserved).
    // Uses r10/r11 as scratch (caller-saved, safe in prologue context).

    // r10 = rsp (save original rsp, accounting for return address on stack)
    EmitMovRegReg(X86Register.R10, X86Register.Rsp);

    // r11 = rsp - rax (target address)
    EmitMovRegReg(X86Register.R11, X86Register.Rsp);
    EmitSubRegReg(X86Register.R11, X86Register.Rax);

    // Probe loop: touch each 4K page
    DefineLabel("__chkstk_loop");
    EmitSubRegImm(X86Register.Rsp, 4096);
    // test dword ptr [rsp], 0 — read-probe to trigger guard page expansion
    EmitByte(0xF7); EmitByte(0x04); EmitByte(0x24); EmitDword(0);
    EmitCmpRegReg(X86Register.Rsp, X86Register.R11);
    EmitJcc("a", "__chkstk_loop"); // loop while rsp > target

    // Restore original rsp (caller will do sub rsp, rax)
    EmitMovRegReg(X86Register.Rsp, X86Register.R10);
    EmitByte(0xC3); // ret
  }

  private void EmitCallImport(string dllName, string functionName) {
    // call [rip+disp32] (indirect call through IAT)
    EmitBytes(0xFF, 0x15);
    var importIndex = AddImport(dllName, functionName);
    _importCallFixups.Add((_code.Count, importIndex));
    EmitDword(0); // placeholder
  }

  /// <summary>
  /// Emit a Windows API call that runs on the OS thread's system stack.
  /// If on a GT stack (currentGt->stackBase != 0), switches RSP to
  /// P->systemStackSP for the call and restores afterwards. If on the
  /// main thread or during early init, calls directly without switching.
  /// R10 is preserved (saved/restored around the switch). R11 is clobbered.
  /// Assumes register args (RCX, RDX, R8, R9) are already set by the caller.
  /// </summary>
  private int _sysStackLabelCounter;
  private void EmitCallImportOnSystemStack(string dllName, string functionName) {
    var id = _sysStackLabelCounter++;
    var skipLabel = $"__sysstk_skip_{id}";
    var doneLabel = $"__sysstk_done_{id}";

    // R10 must be preserved: the register allocator uses R10 as a scratch
    // register for indirect calls (holding the callee pointer), and some
    // call sequences rely on R10 surviving across nested runtime calls.
    // Use R11 for all TLS/P* lookups in the guard code.

    // Guard: skip if TLS not set up yet
    EmitGlobalLoadReg(X86Register.R11, "__sched_tls_teb_offset");
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", skipLabel);
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R11, X86Register.R11, 0); // R11 = P*
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", skipLabel);

    // Check if on GT stack (stackBase != 0).
    // Save P* on the stack so we can reuse R11 for the check.
    EmitPushReg(X86Register.R11);      // save P* on current stack
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, POffCurrentGt);
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, GtOffStackBase);
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitPopReg(X86Register.R11);       // restore R11 = P* (POP preserves flags)
    EmitJcc("z", skipLabel);

    // --- GT stack path: switch RSP to the system stack ---
    // R11 = P*. Save R10 on GT stack, use R10 to shuttle GT RSP across.
    EmitPushReg(X86Register.R10);      // save original R10 on GT stack
    EmitMovRegReg(X86Register.R10, X86Register.Rsp); // R10 = GT RSP (post-push)
    EmitMovRegIndirectMem(X86Register.Rsp, X86Register.R11, POffSystemStackSP);
    EmitPushReg(X86Register.R10);      // save GT RSP on system stack

    // Alignment: systemStackSP is 16-aligned, 1 push → 8-misaligned.
    // SUB 0x28 (shadow 0x20 + pad 0x08) → 16-aligned before CALL.
    EmitSubRegImm(X86Register.Rsp, 0x28);
    EmitCallImport(dllName, functionName);
    EmitAddRegImm(X86Register.Rsp, 0x28);

    EmitPopReg(X86Register.R10);       // R10 = GT RSP (pointing at saved R10)
    EmitMovRegReg(X86Register.Rsp, X86Register.R10); // restore GT RSP
    EmitPopReg(X86Register.R10);       // restore original R10
    EmitJmp(doneLabel);

    // --- Main thread / early init path: call directly ---
    DefineLabel(skipLabel);
    EmitCallImport(dllName, functionName);
    DefineLabel(doneLabel);
  }

  /// <summary>
  /// Switch RSP to system stack for 5+ arg calls. Same stackBase check as above.
  /// After this, [RSP+0x20..] is on the system stack for stack arg writes.
  /// Must be paired with EmitSystemStackLeave(frameSize).
  /// </summary>
  private void EmitSystemStackEnter(int frameSize) {
    var id = _sysStackLabelCounter++;
    _systemStackEnterIds.Push(id);
    var skipLabel = $"__sysstk_enter_skip_{id}";
    var doneLabel = $"__sysstk_enter_done_{id}";

    // Guard: skip if TLS not set up yet (use R11 exclusively for guard)
    EmitGlobalLoadReg(X86Register.R11, "__sched_tls_teb_offset");
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", skipLabel);
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R11, X86Register.R11, 0); // R11 = P*
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", skipLabel);

    // Check if on GT stack (stackBase != 0)
    EmitPushReg(X86Register.R11);      // save P* on current stack
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, POffCurrentGt);
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, GtOffStackBase);
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitPopReg(X86Register.R11);       // restore R11 = P* (POP preserves flags)
    EmitJcc("z", skipLabel);

    // --- GT stack path: switch RSP to system stack ---
    EmitPushReg(X86Register.R10);      // save original R10 on GT stack
    EmitMovRegReg(X86Register.R10, X86Register.Rsp); // R10 = GT RSP (post-push)
    EmitMovRegIndirectMem(X86Register.Rsp, X86Register.R11, POffSystemStackSP);
    EmitPushReg(X86Register.R10);      // save GT RSP on system stack
    EmitSubRegImm(X86Register.Rsp, frameSize);
    EmitJmp(doneLabel);

    // --- Main thread / early init path: just SUB frameSize ---
    DefineLabel(skipLabel);
    EmitSubRegImm(X86Register.Rsp, frameSize);
    DefineLabel(doneLabel);
  }

  private readonly Stack<int> _systemStackEnterIds = new();

  private void EmitSystemStackLeave(int frameSize) {
    var id = _systemStackEnterIds.Pop();
    var skipLabel = $"__sysstk_leave_skip_{id}";
    var doneLabel = $"__sysstk_leave_done_{id}";

    // Guard: same check to determine if we switched
    EmitGlobalLoadReg(X86Register.R11, "__sched_tls_teb_offset");
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", skipLabel);
    EmitByte(0x65); // GS prefix
    EmitMovRegIndirectMemRaw(X86Register.R11, X86Register.R11, 0); // R11 = P*
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitJcc("z", skipLabel);

    EmitPushReg(X86Register.R11);      // save P*
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, POffCurrentGt);
    EmitMovRegIndirectMem(X86Register.R11, X86Register.R11, GtOffStackBase);
    EmitBytes(0x4D, 0x85, 0xDB); // TEST R11, R11
    EmitPopReg(X86Register.R11);       // restore R11 = P*
    EmitJcc("z", skipLabel);

    // --- GT stack path: undo SUB, restore GT RSP, restore R10 ---
    EmitAddRegImm(X86Register.Rsp, frameSize);
    EmitPopReg(X86Register.R10);       // R10 = GT RSP (pointing at saved R10)
    EmitMovRegReg(X86Register.Rsp, X86Register.R10); // restore GT RSP
    EmitPopReg(X86Register.R10);       // restore original R10
    EmitJmp(doneLabel);

    // --- Main thread / early init path: just ADD frameSize ---
    DefineLabel(skipLabel);
    EmitAddRegImm(X86Register.Rsp, frameSize);
    DefineLabel(doneLabel);
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

  public void ResolveJumpTableFixups(int rvaOffset) {
    // Each table entry is a 4-byte signed offset from the table base to a code label.
    // At runtime: target = tableBase + entry.
    var codeSize = _code.Count;
    foreach (var (slotIndex, codeLabel, tableLabel) in _jumpTableFixups) {
      if (!_labels.TryGetValue(codeLabel, out var codeLabelOffset)) {
        throw new InvalidOperationException($"Unresolved jump table target: {codeLabel}");
      }
      if (!_rdataLabels.TryGetValue(tableLabel, out var tableBaseOffset)) {
        throw new InvalidOperationException($"Unresolved jump table label: {tableLabel}");
      }

      // entry = (textRVA + codeLabelOffset) - (rdataRVA + tableBaseOffset)
      //       = codeLabelOffset - (codeSize + rvaOffset + tableBaseOffset)
      var entry = codeLabelOffset - (codeSize + rvaOffset + tableBaseOffset);
      PatchRdataDword(tableBaseOffset + slotIndex * 4, entry);
    }
  }

  private void PatchRdataDword(int offset, int value) {
    _rdata[offset] = (byte)(value & 0xFF);
    _rdata[offset + 1] = (byte)((value >> 8) & 0xFF);
    _rdata[offset + 2] = (byte)((value >> 16) & 0xFF);
    _rdata[offset + 3] = (byte)((value >> 24) & 0xFF);
  }

  public void ResolveSymdata(int rvaOffset) {
    var codeSize = _code.Count;
    foreach (var (offset, label) in _symdataFixups) {
      if (!_symdataLabels.TryGetValue(label, out var symdataOffset)) {
        throw new InvalidOperationException($"Unresolved symdata label: {label}");
      }
      var target = codeSize + rvaOffset + symdataOffset;
      var rel = target - (offset + 4);
      PatchDword(offset, rel);
    }
  }

  public void ResolveUcddata(int rvaOffset) {
    var codeSize = _code.Count;
    foreach (var (offset, label) in _ucddataFixups) {
      if (!_ucddataLabels.TryGetValue(label, out var ucddataOffset)) {
        throw new InvalidOperationException($"Unresolved ucddata label: {label}");
      }
      var target = codeSize + rvaOffset + ucddataOffset;
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
    // The PE writer groups imports by DLL (using GroupBy which preserves first-occurrence
    // order of DLL names) and writes a null qword terminator after each DLL's entries in
    // the IAT. We must replicate this grouping to compute the correct IAT byte offset
    // for each import index.
    var iatByteOffsets = new int[_imports.Count];
    var dllGroups = _imports.Select((imp, idx) => (imp, idx))
                            .GroupBy(x => x.imp.DllName)
                            .ToList();
    var currentOffset = 0;
    foreach (var group in dllGroups) {
      foreach (var (_, idx) in group) {
        iatByteOffsets[idx] = currentOffset;
        currentOffset += 8;
      }
      currentOffset += 8; // null terminator after each DLL group
    }

    for (int i = 0; i < _imports.Count; i++) {
      Logger.Debug(LogCategory.Codegen, $"Import[{i}] = {_imports[i].DllName}::{_imports[i].FunctionName} -> IAT offset 0x{iatByteOffsets[i]:X}");
    }

    var codeSize = _code.Count;
    foreach (var (offset, importIndex) in _importCallFixups) {
      var iatEntryOffset = iatByteOffsets[importIndex];
      var target = codeSize + rvaOffset + iatEntryOffset;
      var rel = target - (offset + 4);
      PatchDword(offset, rel);
    }
  }

  // --- Symbol table ---

  public int GetLabelOffset(string name) {
    return _labels.TryGetValue(name, out var offset) ? offset : -1;
  }

  /// <summary>
  /// Emits a sorted function symbol table into symdata for stack trace lookups.
  /// Format: count(4) | entries[count] of {codeOffset(4), nameOffset(4)} | name strings
  /// </summary>
  public void EmitSymbolTable(List<(string name, int codeOffset)> functions) {
    functions.Sort((a, b) => a.codeOffset.CompareTo(b.codeOffset));

    // Build the name strings block and record their offsets within it
    var nameStrings = new List<byte>();
    var nameOffsets = new List<int>();
    foreach (var (name, _) in functions) {
      nameOffsets.Add(nameStrings.Count);
      nameStrings.AddRange(System.Text.Encoding.UTF8.GetBytes(name));
      nameStrings.Add(0); // null terminator
    }

    // Header size: 4 (count) + 8 * count (entries)
    var headerSize = 4 + 8 * functions.Count;

    // Build full table bytes
    var table = new byte[headerSize + nameStrings.Count];
    BitConverter.GetBytes(functions.Count).CopyTo(table, 0);
    for (int i = 0; i < functions.Count; i++) {
      var entryOffset = 4 + i * 8;
      BitConverter.GetBytes(functions[i].codeOffset).CopyTo(table, entryOffset);
      BitConverter.GetBytes(headerSize + nameOffsets[i]).CopyTo(table, entryOffset + 4);
    }
    // Copy name strings after the header+entries
    nameStrings.CopyTo(table, headerSize);

    DefineSymdata("__symtable", table, 4);
  }

  // --- Output ---

  public byte[] GetCode() => [.. _code];
  public byte[] GetRdata() => [.. _rdata];
  public byte[] GetData() => [.. _data];
  public byte[] GetSymdata() => [.. _symdata];
  public byte[] GetUcddata() => [.. _ucddata];

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

  // MFENCE: full memory barrier. Orders all prior memory accesses before
  // all subsequent memory accesses on this core.
  private void EmitMfence() => EmitBytes(0x0F, 0xAE, 0xF0);

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

    // SIB index field (REX.X bit)
    public Rex Index(X86Register reg) {
      if (IsExtended(reg)) { _rex |= 0x02; _needed = true; }
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

  private void EmitMovsxd(X86Register dest, X86Register src) {
    // MOVSXD r64, r/m32: REX.W + 63 /r (sign-extend i32 to i64)
    Rex.W().Reg(dest).Rm(src).Emit(this);
    EmitByte(0x63);
    EmitByte((byte)(0xC0 | (RegCode(dest) << 3) | RegCode(src)));
  }

  private void EmitMovRegImm(X86Register dest, long immediate) {
    if (!Is64BitReg(dest) && !Is32BitReg(dest))
      throw new ArgumentException($"EmitMovRegImm: unsupported register size: {dest}");
    if (immediate == 0) {
      EmitXorRegReg(dest, dest);
      return;
    }
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
      } else if (immediate > int.MaxValue && immediate <= uint.MaxValue) {
        // MOV r32, imm32: zero-extends to r64, shorter than mov r64, imm64.
        // Handles extended registers (R8-R15) that lack 32-bit enum names.
        Rex.NoW().Rm(dest).EmitIf(this);
        EmitByte((byte)(0xB8 + RegCode(dest)));
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
    if (dest == src) {
      // Zeroing idiom: XOR r/m32, r32 (32-bit write zero-extends to 64 bits, saves a byte)
      Rex.NoW().Reg(src).Rm(dest).Emit(this);
    } else {
      // General XOR: XOR r/m64, r64 (must preserve full 64-bit operands)
      Rex.W().Reg(src).Rm(dest).Emit(this);
    }
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

  private void EmitShrRegCl(X86Register dest) {
    RequireGpr(dest, nameof(EmitShrRegCl));
    // SHR r/m64, CL: REX.W + D3 /5 (logical right shift, fills with zeros)
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xD3);
    EmitByte((byte)(0xC0 | (5 << 3) | RegCode(dest)));
  }

  private void EmitShlRegImm(X86Register dest, byte imm) {
    RequireGpr(dest, nameof(EmitShlRegImm));
    // SHL r/m64, imm8: REX.W + C1 /4 ib
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xC1);
    EmitByte((byte)(0xC0 | (4 << 3) | RegCode(dest)));
    EmitByte(imm);
  }

  private void EmitShrRegImm(X86Register dest, byte imm) {
    RequireGpr(dest, nameof(EmitShrRegImm));
    // SHR r/m64, imm8: REX.W + C1 /5 ib
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xC1);
    EmitByte((byte)(0xC0 | (5 << 3) | RegCode(dest)));
    EmitByte(imm);
  }

  private void EmitBsfRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitBsfRegReg));
    RequireGpr(src, nameof(EmitBsfRegReg));
    // BSF r64, r/m64: REX.W + 0F BC /r
    Rex.W().Reg(dest).Rm(src).Emit(this);
    EmitByte(0x0F);
    EmitByte(0xBC);
    EmitByte((byte)(0xC0 | (RegCode(dest) << 3) | RegCode(src)));
  }

  private void EmitBtrMemReg(X86Register baseReg, int displacement, X86Register bitIndex) {
    RequireGpr(baseReg, nameof(EmitBtrMemReg));
    RequireGpr(bitIndex, nameof(EmitBtrMemReg));
    // BTR r/m64, r64: REX.W + 0F B3 /r with [base+disp] addressing
    Rex.W().Reg(bitIndex).Rm(baseReg).Emit(this);
    EmitByte(0x0F);
    EmitByte(0xB3);
    EmitModRmWithBase(baseReg, bitIndex, displacement);
  }

  private void EmitBtsMemReg(X86Register baseReg, int displacement, X86Register bitIndex) {
    RequireGpr(baseReg, nameof(EmitBtsMemReg));
    RequireGpr(bitIndex, nameof(EmitBtsMemReg));
    // BTS r/m64, r64: REX.W + 0F AB /r with [base+disp] addressing
    Rex.W().Reg(bitIndex).Rm(baseReg).Emit(this);
    EmitByte(0x0F);
    EmitByte(0xAB);
    EmitModRmWithBase(baseReg, bitIndex, displacement);
  }

  private void EmitIncReg(X86Register dest) {
    RequireGpr(dest, nameof(EmitIncReg));
    // INC r/m64: REX.W + FF /0
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xFF);
    EmitByte((byte)(0xC0 | RegCode(dest)));
  }

  private void EmitDecReg(X86Register dest) {
    RequireGpr(dest, nameof(EmitDecReg));
    // DEC r/m64: REX.W + FF /1
    Rex.W().Rm(dest).Emit(this);
    EmitByte(0xFF);
    EmitByte((byte)(0xC0 | (1 << 3) | RegCode(dest)));
  }

  /// CMP r64, [rbp+disp32]: REX.W + 3B /r [rbp+disp32]
  private void EmitCmpRegMem(X86Register lhs, int rbpDisp) {
    Require64BitGpr(lhs, nameof(EmitCmpRegMem));
    Rex.W().Reg(lhs).Emit(this);
    EmitByte(0x3B);
    EmitByte((byte)(0x85 | (RegCode(lhs) << 3))); // mod=10, rm=101(rbp)
    EmitDword(rbpDisp);
  }

  private void EmitCmpRegReg(X86Register lhs, X86Register rhs) {
    RequireGpr(lhs, nameof(EmitCmpRegReg));
    RequireGpr(rhs, nameof(EmitCmpRegReg));
    // CMP r/m64, r64: REX.W + 39 /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(rhs).Rm(lhs).Emit(this);
    EmitByte(0x39);
    EmitByte((byte)(0xC0 | (RegCode(rhs) << 3) | RegCode(lhs)));
  }

  private void EmitCmpRegImm(X86Register lhs, long immediate) {
    Require64BitGpr(lhs, nameof(EmitCmpRegImm));
    if (immediate >= -128 && immediate <= 127) {
      // CMP r64, imm8: REX.W + 83 /7 ib
      Rex.W().Rm(lhs).Emit(this);
      EmitByte(0x83);
      EmitByte((byte)(0xF8 | RegCode(lhs)));
      EmitByte((byte)immediate);
    } else {
      // CMP r64, imm32: REX.W + 81 /7 id
      Rex.W().Rm(lhs).Emit(this);
      EmitByte(0x81);
      EmitByte((byte)(0xF8 | RegCode(lhs)));
      EmitDword((int)immediate);
    }
  }

  private void EmitTestRegReg(X86Register lhs, X86Register rhs) {
    RequireGpr(lhs, nameof(EmitTestRegReg));
    RequireGpr(rhs, nameof(EmitTestRegReg));
    // TEST r/m64, r64: REX.W + 85 /r (REX.W promotes 32-bit register names)
    Rex.W().Reg(rhs).Rm(lhs).Emit(this);
    EmitByte(0x85);
    EmitByte((byte)(0xC0 | (RegCode(rhs) << 3) | RegCode(lhs)));
  }

  private void EmitCmovneRegReg(X86Register dest, X86Register src) {
    RequireGpr(dest, nameof(EmitCmovneRegReg));
    RequireGpr(src, nameof(EmitCmovneRegReg));
    // CMOVNE r64, r/m64: REX.W + 0F 45 /r
    Rex.W().Reg(dest).Rm(src).Emit(this);
    EmitByte(0x0F);
    EmitByte(0x45);
    EmitByte((byte)(0xC0 | (RegCode(dest) << 3) | RegCode(src)));
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

  private void EmitIdivReg32(X86Register divisor) {
    RequireGpr(divisor, nameof(EmitIdivReg32));
    // IDIV r/m32: F7 /7 (no REX.W = 32-bit)
    Rex.NoW().Rm(divisor).EmitIf(this);
    EmitByte(0xF7);
    EmitByte((byte)(0xF8 | RegCode(divisor)));
  }

  private void EmitDivReg32(X86Register divisor) {
    RequireGpr(divisor, nameof(EmitDivReg32));
    // DIV r/m32: F7 /6 (no REX.W = 32-bit)
    Rex.NoW().Rm(divisor).EmitIf(this);
    EmitByte(0xF7);
    EmitByte((byte)(0xF0 | RegCode(divisor)));
  }

  private void EmitDivReg(X86Register divisor) {
    RequireGpr(divisor, nameof(EmitDivReg));
    // DIV r/m64: REX.W + F7 /6
    Rex.W().Rm(divisor).Emit(this);
    EmitByte(0xF7);
    EmitByte((byte)(0xF0 | RegCode(divisor))); // /6 = 110 in reg field
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

  private static byte PrecPrefix(FloatPrecision p) => p == FloatPrecision.F64 ? (byte)0xF2 : (byte)0xF3;

  private void EmitMovXmmRipRel(X86XmmRegister dest, string rdataLabel, FloatPrecision precision) {
    // MOV[SD/SS] xmm, [rip+disp32]: prefix 0F 10 /r (ModRM: mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(dest);
    EmitByte(PrecPrefix(precision));
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x10);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _rdataFixups.Add((_code.Count, rdataLabel));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitMovMemXmm(int displacement, X86XmmRegister src, FloatPrecision precision) {
    // MOV[SD/SS] [rbp+disp], xmm: prefix 0F 11 /r
    var reg = XmmRegCode(src);
    EmitByte(PrecPrefix(precision));
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

  private void EmitMovXmmMem(X86XmmRegister dest, int displacement, FloatPrecision precision) {
    // MOV[SD/SS] xmm, [rbp+disp]: prefix 0F 10 /r
    var reg = XmmRegCode(dest);
    EmitByte(PrecPrefix(precision));
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

  private void EmitAddXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x58, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitSubXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x5C, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitMulXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x59, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitDivXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x5E, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitCvttFloat2Si(X86Register dest, X86XmmRegister src, FloatPrecision precision) {
    RequireGpr(dest, nameof(EmitCvttFloat2Si));
    // CVTT[SD/SS]2SI r64, xmm: prefix REX.W 0F 2C /r
    var d = RegCode(dest);
    var s = XmmRegCode(src);
    EmitByte(PrecPrefix(precision));
    Rex.W().Reg(dest).Rm(s).Emit(this);
    EmitBytes(0x0F, 0x2C);
    EmitByte((byte)(0xC0 | (d << 3) | (s & 7)));
  }

  private void EmitMovqXmmToGpr(X86Register dest, X86XmmRegister src) {
    RequireGpr(dest, nameof(EmitMovqXmmToGpr));
    // MOVQ r64, xmm: 66 REX.W 0F 7E /r
    var d = RegCode(dest);
    var s = XmmRegCode(src);
    EmitByte(0x66);
    Rex.W().Reg(s).Rm(dest).Emit(this);
    EmitBytes(0x0F, 0x7E);
    EmitByte((byte)(0xC0 | ((s & 7) << 3) | (d & 7)));
  }

  private void EmitCvtSi2Float(X86XmmRegister dest, X86Register src, FloatPrecision precision) {
    RequireGpr(src, nameof(EmitCvtSi2Float));
    // CVTSI2[SD/SS] xmm, r64: prefix REX.W 0F 2A /r
    var d = XmmRegCode(dest);
    var s = RegCode(src);
    EmitByte(PrecPrefix(precision));
    Rex.W().Reg(d).Rm(src).Emit(this);
    EmitBytes(0x0F, 0x2A);
    EmitByte((byte)(0xC0 | ((d & 7) << 3) | (s & 7)));
  }

  private void EmitCvtSd2Ss(X86XmmRegister dest, X86XmmRegister src) {
    // CVTSD2SS xmm, xmm: F2 0F 5A /r
    EmitXmmRegRegOp(0xF2, 0x0F, 0x5A, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitCvtSs2Sd(X86XmmRegister dest, X86XmmRegister src) {
    // CVTSS2SD xmm, xmm: F3 0F 5A /r
    EmitXmmRegRegOp(0xF3, 0x0F, 0x5A, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitAndMaskRipRel(X86XmmRegister dest, string rdataLabel, FloatPrecision precision) {
    // ANDPD: 66 0F 54 /r, ANDPS: 0F 54 /r (no prefix)
    var reg = XmmRegCode(dest);
    if (precision == FloatPrecision.F64) EmitByte(0x66);
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x54);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _rdataFixups.Add((_code.Count, rdataLabel));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitSqrtXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x51, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitRoundXmm(X86XmmRegister dest, X86XmmRegister src, byte mode, FloatPrecision precision) {
    // ROUND[SD/SS] xmm, xmm, imm8: 66 0F 3A [0B/0A] /r ib
    var d = XmmRegCode(dest);
    var s = XmmRegCode(src);
    EmitByte(0x66);
    Rex.NoW().Reg(d).Rm(s).EmitIf(this);
    EmitBytes(0x0F, 0x3A, precision == FloatPrecision.F64 ? (byte)0x0B : (byte)0x0A);
    EmitByte((byte)(0xC0 | ((d & 7) << 3) | (s & 7)));
    EmitByte(mode);
  }

  private void EmitMinXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x5D, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitMaxXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x5F, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitMovXmmXmm(X86XmmRegister dest, X86XmmRegister src, FloatPrecision precision) {
    EmitXmmRegRegOp(PrecPrefix(precision), 0x0F, 0x10, XmmRegCode(dest), XmmRegCode(src));
  }

  private void EmitUcomisXmm(X86XmmRegister src1, X86XmmRegister src2, FloatPrecision precision) {
    if (precision == FloatPrecision.F64) {
      // UCOMISD xmm, xmm: 66 0F 2E /r
      EmitXmmRegRegOp(0x66, 0x0F, 0x2E, XmmRegCode(src1), XmmRegCode(src2));
    } else {
      // UCOMISS xmm, xmm: 0F 2E /r (no prefix byte)
      var s1 = XmmRegCode(src1);
      var s2 = XmmRegCode(src2);
      Rex.NoW().Reg(s1).Rm(s2).EmitIf(this);
      EmitBytes(0x0F, 0x2E);
      EmitByte((byte)(0xC0 | ((s1 & 7) << 3) | (s2 & 7)));
    }
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

  private void EmitJumpTableDispatch(X86JumpTableOp jt) {
    var indexReg = jt.IndexReg;

    // Unsigned compare catches negative values too (they wrap to large positives)
    EmitCmpRegImm(indexReg, jt.CaseCount);
    EmitJcc("ae", jt.DefaultTarget);

    EmitLeaRegRipRel(X86Register.R11, jt.RdataLabel);
    EmitMovsxdRegBaseIndexScale4(indexReg, X86Register.R11, indexReg);
    EmitAddRegReg(X86Register.R11, indexReg);
    EmitJmpIndirect(X86Register.R11);

    for (int i = 0; i < jt.CaseTargets.Length; i++) {
      _jumpTableFixups.Add((i, jt.CaseTargets[i], jt.RdataLabel));
    }
  }

  private void EmitMovsxdRegBaseIndexScale4(X86Register dest, X86Register baseReg, X86Register index) {
    RequireGpr(dest, nameof(EmitMovsxdRegBaseIndexScale4));
    RequireGpr(baseReg, nameof(EmitMovsxdRegBaseIndexScale4));
    RequireGpr(index, nameof(EmitMovsxdRegBaseIndexScale4));
    // MOVSXD r64, dword [base + index*4]: REX.W + 63 /r with SIB
    // ModRM: mod=00, reg=dest, r/m=100 (SIB follows)
    // SIB: scale=10 (*4), index=index, base=base
    Rex.W().Reg(dest).Index(index).Rm(baseReg).Emit(this);
    EmitByte(0x63);
    var destCode = RegCode(dest);
    var baseCode = RegCode(baseReg);
    var indexCode = RegCode(index);
    if (baseCode == 5) {
      // RBP/R13 as base with mod=00 means [RIP+disp32], so use mod=01 with disp8=0
      EmitByte((byte)(0x44 | (destCode << 3))); // mod=01, r/m=100 (SIB)
      EmitByte((byte)(0x80 | (indexCode << 3) | baseCode)); // scale=10 (*4), index, base
      EmitByte(0x00); // disp8 = 0
    } else {
      EmitByte((byte)(0x04 | (destCode << 3))); // mod=00, r/m=100 (SIB)
      EmitByte((byte)(0x80 | (indexCode << 3) | baseCode)); // scale=10 (*4), index, base
    }
  }

  private void EmitJmpIndirect(X86Register target) {
    RequireGpr(target, nameof(EmitJmpIndirect));
    // JMP r/m64: FF /4 (mod=11, reg=100, r/m=target)
    Rex.NoW().Rm(target).EmitIf(this);
    EmitByte(0xFF);
    EmitByte((byte)(0xE0 | RegCode(target))); // 11 100 rrr = 0xE0 | reg
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

  private void EmitLeaRegSymdataRel(X86Register dest, string symdataLabel) {
    RequireGpr(dest, nameof(EmitLeaRegSymdataRel));
    // LEA r64, [rip+disp32]: same encoding as rdata, but fixup resolves against symdata section
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8D);
    EmitByte((byte)(0x05 | (RegCode(dest) << 3)));
    _symdataFixups.Add((_code.Count, symdataLabel));
    EmitDword(0);
  }

  private void EmitLeaRegUcddataRel(X86Register dest, string ucddataLabel) {
    RequireGpr(dest, nameof(EmitLeaRegUcddataRel));
    // LEA r64, [rip+disp32]: same encoding as rdata, but fixup resolves against ucddata section
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8D);
    EmitByte((byte)(0x05 | (RegCode(dest) << 3)));
    _ucddataFixups.Add((_code.Count, ucddataLabel));
    EmitDword(0);
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

  private void EmitLeaRegRegReg(X86Register dest, X86Register baseReg, X86Register index) {
    RequireGpr(dest, nameof(EmitLeaRegRegReg));
    RequireGpr(baseReg, nameof(EmitLeaRegRegReg));
    RequireGpr(index, nameof(EmitLeaRegRegReg));
    // LEA r64, [base + index*1]: REX.W + 8D /r with SIB
    Rex.W().Reg(dest).Index(index).Rm(baseReg).Emit(this);
    EmitByte(0x8D);
    var baseCode = RegCode(baseReg);
    var indexCode = RegCode(index);
    var destCode = RegCode(dest);
    if (baseCode == 5) {
      // RBP/R13 as base with mod=00 means [RIP+disp32], so use mod=01 with disp8=0
      EmitByte((byte)(0x44 | (destCode << 3))); // mod=01, r/m=100 (SIB)
      EmitByte((byte)(indexCode << 3 | baseCode)); // scale=00, index, base
      EmitByte(0x00); // disp8 = 0
    } else {
      EmitByte((byte)(0x04 | (destCode << 3))); // mod=00, r/m=100 (SIB)
      EmitByte((byte)(indexCode << 3 | baseCode)); // scale=00, index, base
    }
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

  private void EmitMovzxRegWordIndirect(X86Register dest, X86Register baseReg, int displacement) {
    RequireGpr(dest, nameof(EmitMovzxRegWordIndirect));
    RequireGpr(baseReg, nameof(EmitMovzxRegWordIndirect));
    // MOVZX r32, word ptr [baseReg+disp]: [REX] 0F B7 /r (32-bit dest zero-extends to 64-bit)
    Rex.NoW().Reg(dest).Rm(baseReg).EmitIf(this);
    EmitBytes(0x0F, 0xB7);
    EmitModRmWithBase(baseReg, dest, displacement);
  }

  private void EmitMovWordIndirectReg(X86Register baseReg, int displacement, X86Register src) {
    RequireGpr(baseReg, nameof(EmitMovWordIndirectReg));
    RequireGpr(src, nameof(EmitMovWordIndirectReg));
    // MOV word ptr [baseReg+disp], src16: 66 [REX] 89 /r
    EmitByte(0x66);
    Rex.NoW().Reg(src).Rm(baseReg).EmitIf(this);
    EmitByte(0x89);
    EmitModRmWithBase(baseReg, src, displacement);
  }

  private void EmitMovIndirectMemXmm(X86Register baseReg, int displacement, X86XmmRegister src, FloatPrecision precision) {
    RequireGpr(baseReg, nameof(EmitMovIndirectMemXmm));
    // MOV[SD/SS] [baseReg+disp], xmm: prefix [REX] 0F 11 /r
    var reg = XmmRegCode(src);
    EmitByte(PrecPrefix(precision));
    Rex.NoW().Reg(reg).Rm(baseReg).EmitIf(this);
    EmitBytes(0x0F, 0x11);
    EmitModRmWithBaseXmm(baseReg, reg, displacement);
  }

  private void EmitMovXmmIndirectMem(X86XmmRegister dest, X86Register baseReg, int displacement, FloatPrecision precision) {
    RequireGpr(baseReg, nameof(EmitMovXmmIndirectMem));
    // MOV[SD/SS] xmm, [baseReg+disp]: prefix [REX] 0F 10 /r
    var reg = XmmRegCode(dest);
    EmitByte(PrecPrefix(precision));
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

  private void EmitGlobalLoadReg(X86Register dest, string globalName, int size = 8) {
    RequireGpr(dest, nameof(EmitGlobalLoadReg));
    if (size == 1) {
      // MOVZX r32, byte [rip+disp32]: 0F B6 /r (mod=00, r/m=101 for RIP-relative)
      // Using r32 destination zero-extends to r64 automatically
      Rex.NoW().Reg(dest).EmitIf(this);
      EmitBytes(0x0F, 0xB6);
    } else if (size == 2) {
      // MOVZX r32, word [rip+disp32]: 0F B7 /r (mod=00, r/m=101 for RIP-relative)
      Rex.NoW().Reg(dest).EmitIf(this);
      EmitBytes(0x0F, 0xB7);
    } else if (size == 4) {
      // MOV r32, [rip+disp32]: 8B /r (no REX.W, so 32-bit operand, zero-extends to r64)
      Rex.NoW().Reg(dest).EmitIf(this);
      EmitByte(0x8B);
    } else {
      // MOV r64, [rip+disp32]: REX.W + 8B /r
      Rex.W().Reg(dest).Emit(this);
      EmitByte(0x8B);
    }
    EmitByte((byte)(0x05 | (RegCode(dest) << 3))); // mod=00, r/m=101 (RIP-relative)
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalLeaReg(X86Register dest, string globalName) {
    RequireGpr(dest, nameof(EmitGlobalLeaReg));
    // LEA r64, [rip+disp32]: REX.W + 8D /r (mod=00, r/m=101 for RIP-relative)
    Rex.W().Reg(dest).Emit(this);
    EmitByte(0x8D);
    EmitByte((byte)(0x05 | (RegCode(dest) << 3))); // mod=00, r/m=101 (RIP-relative)
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalStoreReg(X86Register src, string globalName, int size = 8) {
    RequireGpr(src, nameof(EmitGlobalStoreReg));
    if (size == 1) {
      // MOV byte [rip+disp32], r8: REX + 88 /r (mod=00, r/m=101 for RIP-relative)
      Rex.NoW().Reg(src).ByteReg(src).EmitIf(this);
      EmitByte(0x88);
    } else if (size == 2) {
      // MOV word [rip+disp32], r16: 66 [REX] 89 /r
      EmitByte(0x66);
      Rex.NoW().Reg(src).EmitIf(this);
      EmitByte(0x89);
    } else if (size == 4) {
      // MOV dword [rip+disp32], r32: 89 /r (no REX.W)
      Rex.NoW().Reg(src).EmitIf(this);
      EmitByte(0x89);
    } else {
      // MOV [rip+disp32], r64: REX.W + 89 /r
      Rex.W().Reg(src).Emit(this);
      EmitByte(0x89);
    }
    EmitByte((byte)(0x05 | (RegCode(src) << 3))); // mod=00, r/m=101 (RIP-relative)
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalLoadXmm(X86XmmRegister dest, string globalName, FloatPrecision precision) {
    // MOV[SD/SS] xmm, [rip+disp32]: prefix [REX] 0F 10 /r (mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(dest);
    EmitByte(PrecPrefix(precision));
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x10);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }

  private void EmitGlobalStoreXmm(X86XmmRegister src, string globalName, FloatPrecision precision) {
    // MOV[SD/SS] [rip+disp32], xmm: prefix [REX] 0F 11 /r (mod=00, r/m=101 for RIP-relative)
    var reg = XmmRegCode(src);
    EmitByte(PrecPrefix(precision));
    Rex.NoW().Reg(reg).EmitIf(this);
    EmitBytes(0x0F, 0x11);
    EmitByte((byte)(0x05 | ((reg & 7) << 3)));
    _globalFixups.Add((_code.Count, globalName));
    EmitDword(0); // placeholder for RIP-relative displacement
  }
}
