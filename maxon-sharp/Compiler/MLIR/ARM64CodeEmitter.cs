using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class ARM64CodeEmitter() {
  private readonly List<byte> _code = [];
  private readonly List<byte> _rdata = [];
  private readonly List<byte> _data = [];
  private readonly List<byte> _symdata = [];
  private readonly List<byte> _ucddata = [];

  // Label name -> code offset
  private readonly Dictionary<string, int> _labels = [];

  // Runtime function labels for symbol table inclusion
  private readonly List<string> _runtimeFunctionLabels = [];
  public IReadOnlyList<string> RuntimeFunctionLabels => _runtimeFunctionLabels;

  // Branch fixups: code offset -> target label name (for B and BL instructions)
  private readonly List<(int offset, string target)> _branchFixups = [];

  // Conditional branch fixups: code offset -> target label
  private readonly List<(int offset, string target)> _condBranchFixups = [];

  // ADRP+ADD fixups for rdata: (adrpOffset, addOffset, label)
  private readonly List<(int adrpOffset, int addOffset, string label)> _rdataAdrpFixups = [];
  private readonly Dictionary<string, int> _rdataLabels = [];

  // ADRP+ADD fixups for globals
  private readonly List<(int adrpOffset, int addOffset, string name)> _globalAdrpFixups = [];
  private readonly Dictionary<string, int> _globalLabels = [];

  // ADRP+ADD fixups for symdata
  private readonly List<(int adrpOffset, int addOffset, string label)> _symdataAdrpFixups = [];
  private readonly Dictionary<string, int> _symdataLabels = [];

  // ADRP+ADD fixups for ucddata
  private readonly List<(int adrpOffset, int addOffset, string label)> _ucddataAdrpFixups = [];
  private readonly Dictionary<string, int> _ucddataLabels = [];

  // ADRP+ADD fixups for function addresses
  private readonly List<(int adrpOffset, int addOffset, string funcName)> _funcAddrAdrpFixups = [];

  // Float load from rdata via ADRP+LDR: (adrpOffset, ldrOffset, label, is64bit)
  private readonly List<(int adrpOffset, int ldrOffset, string label, bool is64bit)> _floatRdataFixups = [];

  // Global load/store fixups: (adrpOffset, accessOffset, name, isStore, size, isFloat, is64bit)
  private readonly List<(int adrpOffset, int accessOffset, string name, bool isStore, int size, bool isFloat, bool is64bit)> _globalAccessFixups = [];

  // Imported dylib functions: GOT-based indirect calls
  private readonly List<byte> _got = [];  // GOT data (zeros, filled by dyld)
  private readonly Dictionary<string, int> _importSlots = [];  // function name -> GOT slot index
  private readonly List<string> _importNames = [];  // ordered list of imported function names
  // GOT ADRP+LDR fixups: (adrpOffset, ldrOffset, functionName)
  private readonly List<(int adrpOffset, int ldrOffset, string functionName)> _gotFixups = [];

  // Jump table fixups: (slotIndex, codeLabel, tableLabel)
  private readonly List<(int slotIndex, string codeLabel, string tableLabel)> _jumpTableFixups = [];

  private string _currentFunction = "";
  private int _uniqueLabelCounter;
  private int _currentRuntimeStackSize;

  public bool HasRdata => _rdata.Count > 0;
  public bool HasGlobals => _data.Count > 0;
  public bool HasSymdata => _symdata.Count > 0;
  public bool HasUcddata => _ucddata.Count > 0;

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

  public int GetLabelOffset(string name) {
    return _labels.TryGetValue(name, out var offset) ? offset : -1;
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

  // --- Byte emission helpers ---

  private void EmitWord(uint word) {
    _code.Add((byte)(word & 0xFF));
    _code.Add((byte)((word >> 8) & 0xFF));
    _code.Add((byte)((word >> 16) & 0xFF));
    _code.Add((byte)((word >> 24) & 0xFF));
  }

  private void PatchWord(int offset, uint word) {
    _code[offset] = (byte)(word & 0xFF);
    _code[offset + 1] = (byte)((word >> 8) & 0xFF);
    _code[offset + 2] = (byte)((word >> 16) & 0xFF);
    _code[offset + 3] = (byte)((word >> 24) & 0xFF);
  }

  private uint ReadWord(int offset) {
    return (uint)(_code[offset] | (_code[offset + 1] << 8) | (_code[offset + 2] << 16) | (_code[offset + 3] << 24));
  }

  // --- Register encoding ---

  private static uint Reg(ARM64Register r) {
    return r switch {
      >= ARM64Register.X0 and <= ARM64Register.X30 => (uint)(r - ARM64Register.X0),
      ARM64Register.Sp => 31,
      ARM64Register.Xzr => 31,
      >= ARM64Register.W0 and <= ARM64Register.W30 => (uint)(r - ARM64Register.W0),
      ARM64Register.Wzr => 31,
      _ => throw new ArgumentException($"Invalid register: {r}")
    };
  }

  private static uint FloatReg(ARM64FloatRegister r) {
    return r switch {
      >= ARM64FloatRegister.D0 and <= ARM64FloatRegister.D31 => (uint)(r - ARM64FloatRegister.D0),
      >= ARM64FloatRegister.S0 and <= ARM64FloatRegister.S31 => (uint)(r - ARM64FloatRegister.S0),
      _ => throw new ArgumentException($"Invalid float register: {r}")
    };
  }

  // --- Condition code encoding ---

  private static uint CondCode(ARM64ConditionCode cc) {
    return cc switch {
      ARM64ConditionCode.Eq => 0b0000,
      ARM64ConditionCode.Ne => 0b0001,
      ARM64ConditionCode.Hs => 0b0010,
      ARM64ConditionCode.Lo => 0b0011,
      ARM64ConditionCode.Gt => 0b1100,
      ARM64ConditionCode.Ge => 0b1010,
      ARM64ConditionCode.Lt => 0b1011,
      ARM64ConditionCode.Le => 0b1101,
      ARM64ConditionCode.Hi => 0b1000,
      ARM64ConditionCode.Ls => 0b1001,
      _ => throw new ArgumentException($"Unknown condition code: {cc}")
    };
  }

  // Invert a condition code (for CSET which uses inverted condition)
  private static uint InvertedCondCode(ARM64ConditionCode cc) {
    return CondCode(cc) ^ 1u;
  }

  // --- Emit ARM64 operations ---

  public void Emit(ARM64Op op) {
    switch (op) {
      case ARM64PrologueOp prologue:
        EmitPrologue(prologue.StackSize);
        break;
      case ARM64EpilogueOp:
        EmitEpilogue();
        break;
      case ARM64MovRegRegOp mov:
        EmitMovRegReg(mov.Dest, mov.Src);
        break;
      case ARM64MovRegImmOp mov:
        EmitMovRegImm(mov.Dest, mov.Immediate);
        break;
      case ARM64StoreToStackOp store:
        EmitStoreToStack(store.Displacement, store.Src, store.SizeInBytes);
        break;
      case ARM64LoadFromStackOp load:
        EmitLoadFromStack(load.Dest, load.Displacement, load.SizeInBytes);
        break;
      case ARM64StoreIndirectOp store:
        EmitStoreIndirect(store.BaseReg, store.Displacement, store.Src, 8);
        break;
      case ARM64LoadIndirectOp load:
        EmitLoadIndirect(load.Dest, load.BaseReg, load.Displacement, 8);
        break;
      case ARM64StoreByteIndirectOp store:
        EmitStoreIndirect(store.BaseReg, store.Displacement, store.Src, 1);
        break;
      case ARM64LoadByteIndirectOp load:
        EmitLoadIndirect(load.Dest, load.BaseReg, load.Displacement, 1);
        break;
      case ARM64StoreHalfIndirectOp store:
        EmitStoreIndirect(store.BaseReg, store.Displacement, store.Src, 2);
        break;
      case ARM64LoadHalfIndirectOp load:
        EmitLoadIndirect(load.Dest, load.BaseReg, load.Displacement, 2);
        break;
      case ARM64Store32IndirectOp store:
        EmitStoreIndirect(store.BaseReg, store.Displacement, store.Src, 4);
        break;
      case ARM64Load32IndirectOp load:
        EmitLoadIndirect(load.Dest, load.BaseReg, load.Displacement, 4);
        break;
      case ARM64AddRegRegOp add:
        EmitAluRegReg(0x8B000000, add.Dest, add.Src1, add.Src2);
        break;
      case ARM64SubRegRegOp sub:
        EmitAluRegReg(0xCB000000, sub.Dest, sub.Src1, sub.Src2);
        break;
      case ARM64MulRegRegOp mul:
        // MUL Xd, Xn, Xm = MADD Xd, Xn, Xm, XZR
        EmitWord(0x9B007C00 | (Reg(mul.Src2) << 16) | (Reg(mul.Src1) << 5) | Reg(mul.Dest));
        break;
      case ARM64SdivRegRegOp sdiv:
        EmitWord(0x9AC00C00 | (Reg(sdiv.Src2) << 16) | (Reg(sdiv.Src1) << 5) | Reg(sdiv.Dest));
        break;
      case ARM64UdivRegRegOp udiv:
        EmitWord(0x9AC00800 | (Reg(udiv.Src2) << 16) | (Reg(udiv.Src1) << 5) | Reg(udiv.Dest));
        break;
      case ARM64MsubRegRegOp msub:
        // MSUB Xd, Xn, Xm, Xa: Xd = Xa - Xn * Xm
        EmitWord(0x9B008000 | (Reg(msub.Src2) << 16) | (Reg(msub.Accumulator) << 10) | (Reg(msub.Src1) << 5) | Reg(msub.Dest));
        break;
      case ARM64NegRegOp neg:
        // NEG Xd, Xn = SUB Xd, XZR, Xn
        EmitWord(0xCB000000 | (Reg(neg.Src) << 16) | (31u << 5) | Reg(neg.Dest));
        break;
      case ARM64AddRegImmOp add:
        EmitAddSubImm(add.Dest, add.Src, add.Immediate, isAdd: true);
        break;
      case ARM64SubRegImmOp sub:
        EmitAddSubImm(sub.Dest, sub.Src, sub.Immediate, isAdd: false);
        break;
      case ARM64AndRegRegOp and:
        EmitAluRegReg(0x8A000000, and.Dest, and.Src1, and.Src2);
        break;
      case ARM64OrrRegRegOp orr:
        EmitAluRegReg(0xAA000000, orr.Dest, orr.Src1, orr.Src2);
        break;
      case ARM64EorRegRegOp eor:
        EmitAluRegReg(0xCA000000, eor.Dest, eor.Src1, eor.Src2);
        break;
      case ARM64MvnRegOp mvn:
        // MVN Xd, Xn = ORN Xd, XZR, Xn
        EmitWord(0xAA2003E0 | (Reg(mvn.Src) << 16) | Reg(mvn.Dest));
        break;
      case ARM64LslRegRegOp lsl:
        EmitWord(0x9AC02000 | (Reg(lsl.Src2) << 16) | (Reg(lsl.Src1) << 5) | Reg(lsl.Dest));
        break;
      case ARM64AsrRegRegOp asr:
        EmitWord(0x9AC02800 | (Reg(asr.Src2) << 16) | (Reg(asr.Src1) << 5) | Reg(asr.Dest));
        break;
      case ARM64LsrRegRegOp lsr:
        EmitWord(0x9AC02400 | (Reg(lsr.Src2) << 16) | (Reg(lsr.Src1) << 5) | Reg(lsr.Dest));
        break;
      case ARM64CmpRegRegOp cmp:
        // CMP Xn, Xm = SUBS XZR, Xn, Xm
        EmitWord(0xEB00001F | (Reg(cmp.Rhs) << 16) | (Reg(cmp.Lhs) << 5));
        break;
      case ARM64CmpRegImmOp cmp:
        EmitCmpImm(cmp.Lhs, cmp.Immediate);
        break;
      case ARM64TestRegRegOp tst:
        // TST Xn, Xm = ANDS XZR, Xn, Xm
        EmitWord(0xEA00001F | (Reg(tst.Rhs) << 16) | (Reg(tst.Lhs) << 5));
        break;
      case ARM64CsetOp cset:
        // CSET Xd, cond = CSINC Xd, XZR, XZR, invert(cond)
        EmitWord(0x9A9F07E0 | (InvertedCondCode(cset.Condition) << 12) | Reg(cset.Dest));
        break;
      case ARM64CselOp csel:
        EmitWord(0x9A800000 | (Reg(csel.Src2) << 16) | (CondCode(csel.Condition) << 12) | (Reg(csel.Src1) << 5) | Reg(csel.Dest));
        break;
      case ARM64SxtwOp sxtw:
        // SXTW Xd, Wn = SBFM Xd, Xn, #0, #31
        EmitWord(0x93407C00 | (Reg(sxtw.Src) << 5) | Reg(sxtw.Dest));
        break;
      case ARM64BranchOp branch:
        EmitBranch(branch.Target);
        break;
      case ARM64BranchCondOp bcond:
        EmitBranchCond(bcond.Condition, bcond.Target);
        break;
      case ARM64BranchLinkOp bl:
        EmitBranchLink(bl.Target);
        break;
      case ARM64BranchLinkRegOp blr:
        EmitWord(0xD63F0000 | (Reg(blr.Target) << 5));
        break;
      case ARM64RetOp:
        EmitWord(0xD65F03C0); // RET (X30)
        break;
      case ARM64LabelDefOp labelDef:
        DefineLabel(labelDef.Name);
        break;
      case ARM64LeaStackOp lea:
        EmitAddSubImm(lea.Dest, ARM64Register.X29, lea.Displacement, isAdd: true);
        break;
      case ARM64AdrpAddRdataOp adrp:
        EmitAdrpAddFixup(adrp.Dest, _rdataAdrpFixups, adrp.RdataLabel);
        break;
      case ARM64AdrpAddGlobalOp adrp:
        EmitAdrpAddFixup(adrp.Dest, _globalAdrpFixups, adrp.GlobalName);
        break;
      case ARM64AdrpAddSymdataOp adrp:
        EmitAdrpAddFixup(adrp.Dest, _symdataAdrpFixups, adrp.SymdataLabel);
        break;
      case ARM64AdrpAddUcddataOp adrp:
        EmitAdrpAddFixup(adrp.Dest, _ucddataAdrpFixups, adrp.UcddataLabel);
        break;
      case ARM64AdrpAddFuncOp adrp:
        EmitAdrpAddFixup(adrp.Dest, _funcAddrAdrpFixups, adrp.FunctionName);
        break;
      case ARM64LeaRegRegOp lea:
        EmitAluRegReg(0x8B000000, lea.Dest, lea.BaseReg, lea.Index);
        break;
      case ARM64GlobalLoadOp globalLoad:
        EmitGlobalAccess(globalLoad.Dest, globalLoad.GlobalName, globalLoad.Size, isStore: false, isFloat: false, is64bit: false);
        break;
      case ARM64GlobalStoreOp globalStore:
        EmitGlobalAccess(globalStore.Src, globalStore.GlobalName, globalStore.Size, isStore: true, isFloat: false, is64bit: false);
        break;
      case ARM64GlobalLoadFloatOp globalLoadF:
        EmitGlobalAccessFloat(globalLoadF.Dest, globalLoadF.GlobalName, globalLoadF.Precision, isStore: false);
        break;
      case ARM64GlobalStoreFloatOp globalStoreF:
        EmitGlobalAccessFloat(globalStoreF.Src, globalStoreF.GlobalName, globalStoreF.Precision, isStore: true);
        break;
case ARM64MemcpyOp:
        EmitMemcpyLoop();
        break;
      case ARM64BulkZeroOp:
        EmitBulkZeroLoop();
        break;
      case ARM64StoreToSpOp storeSp:
        EmitStoreToSp(storeSp.Offset, storeSp.Src);
        break;
      case ARM64JumpTableOp jt:
        EmitJumpTableDispatch(jt);
        break;
      // Float ops
      case ARM64FmovToFloatOp fmov:
        EmitFmovToFloat(fmov.Dest, fmov.Src, fmov.Precision);
        break;
      case ARM64FmovToGprOp fmov:
        EmitFmovToGpr(fmov.Dest, fmov.Src, fmov.Precision);
        break;
      case ARM64FmovRegRegOp fmov:
        EmitFmovRegReg(fmov.Dest, fmov.Src, fmov.Precision);
        break;
      case ARM64FloatLoadFromStackOp fload:
        EmitFloatLoadFromStack(fload.Dest, fload.Displacement, fload.Precision);
        break;
      case ARM64FloatStoreToStackOp fstore:
        EmitFloatStoreToStack(fstore.Displacement, fstore.Src, fstore.Precision);
        break;
      case ARM64FloatLoadIndirectOp fload:
        EmitFloatLoadIndirect(fload.Dest, fload.BaseReg, fload.Displacement, fload.Precision);
        break;
      case ARM64FloatStoreIndirectOp fstore:
        EmitFloatStoreIndirect(fstore.BaseReg, fstore.Displacement, fstore.Src, fstore.Precision);
        break;
      case ARM64FloatLoadRdataOp fload:
        EmitFloatLoadRdata(fload.Dest, fload.RdataLabel, fload.Precision);
        break;
      case ARM64FaddOp fadd:
        EmitFloatArith(0x1E602800, fadd.Dest, fadd.Src1, fadd.Src2, fadd.Precision);
        break;
      case ARM64FsubOp fsub:
        EmitFloatArith(0x1E603800, fsub.Dest, fsub.Src1, fsub.Src2, fsub.Precision);
        break;
      case ARM64FmulOp fmul:
        EmitFloatArith(0x1E600800, fmul.Dest, fmul.Src1, fmul.Src2, fmul.Precision);
        break;
      case ARM64FdivOp fdiv:
        EmitFloatArith(0x1E601800, fdiv.Dest, fdiv.Src1, fdiv.Src2, fdiv.Precision);
        break;
      case ARM64FsqrtOp fsqrt:
        EmitFloatUnary(0x1E61C000, fsqrt.Dest, fsqrt.Src, fsqrt.Precision);
        break;
      case ARM64FnegOp fneg:
        EmitFloatUnary(0x1E614000, fneg.Dest, fneg.Src, fneg.Precision);
        break;
      case ARM64FabsOp fabs:
        EmitFloatUnary(0x1E60C000, fabs.Dest, fabs.Src, fabs.Precision);
        break;
      case ARM64FminOp fmin:
        EmitFloatArith(0x1E605800, fmin.Dest, fmin.Src1, fmin.Src2, fmin.Precision);
        break;
      case ARM64FmaxOp fmax:
        EmitFloatArith(0x1E604800, fmax.Dest, fmax.Src1, fmax.Src2, fmax.Precision);
        break;
      case ARM64FrintzOp frintz:
        EmitFloatUnary(0x1E65C000, frintz.Dest, frintz.Src, frintz.Precision);
        break;
      case ARM64FrintpOp frintp:
        EmitFloatUnary(0x1E64C000, frintp.Dest, frintp.Src, frintp.Precision);
        break;
      case ARM64FrintmOp frintm:
        EmitFloatUnary(0x1E654000, frintm.Dest, frintm.Src, frintm.Precision);
        break;
      case ARM64FrintnOp frintn:
        EmitFloatUnary(0x1E644000, frintn.Dest, frintn.Src, frintn.Precision);
        break;
      case ARM64FcmpOp fcmp:
        EmitFcmp(fcmp.Lhs, fcmp.Rhs, fcmp.Precision);
        break;
      case ARM64ScvtfOp scvtf:
        EmitScvtf(scvtf.Dest, scvtf.Src, scvtf.Precision);
        break;
      case ARM64FcvtzsOp fcvtzs:
        EmitFcvtzs(fcvtzs.Dest, fcvtzs.Src, fcvtzs.Precision);
        break;
      case ARM64UcvtfOp ucvtf:
        EmitUcvtf(ucvtf.Dest, ucvtf.Src, ucvtf.Precision);
        break;
      case ARM64FcvtzuOp fcvtzu:
        EmitFcvtzu(fcvtzu.Dest, fcvtzu.Src, fcvtzu.Precision);
        break;
      case ARM64FcvtOp fcvt:
        EmitFcvt(fcvt.Dest, fcvt.Src, fcvt.DestPrecision);
        break;
      default:
        throw new InvalidOperationException($"No ARM64 emission for: {op.GetType().Name} ({op.Mnemonic})");
    }
  }

  // --- Instruction encoding helpers ---

  private void EmitPrologue(int stackSize) {
    if (stackSize > 0) {
      // Stack guard check for green thread small stacks.
      // X28 = P* (0 if no green thread context, e.g. I/O worker).
      // Skip check if X28 == 0 (no P context) — uses CBZ.
      // Check: if (SP - frameSize) < gt->stackGuard → call __gt_morestack
      var guardLabel = $"__stackguard_ok_{_uniqueLabelCounter}";
      _uniqueLabelCounter++;

      EmitCbz(ARM64Register.X28, guardLabel); // no P* → skip guard check
      // SUB X16, SP, #stackSize (projected SP after allocation)
      EmitAddSubImm(ARM64Register.X16, ARM64Register.Sp, stackSize > 4095 ? 4095 : stackSize, isAdd: false);
      // LDR X17, [X28, #POffCurrentGt]
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X17, ARM64Register.X28, 0x18, 8); // POffCurrentGt = 0x18
      // LDR X17, [X17, #GtOffStackGuard]
      EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X17, ARM64Register.X17, 0x50, 8); // GtOffStackGuard = 0x50
      // CMP X16, X17
      EmitWord(0xEB11021F);
      EmitBranchCond(ARM64ConditionCode.Hs, guardLabel); // SP - frameSize >= guard → OK
      // Stack needs growth. Save original LR in X16 before BL clobbers it.
      EmitMovRegReg(ARM64Register.X16, ARM64Register.X30);
      EmitBranchLink("__gt_morestack");
      // morestack returns here after growing. Restore original LR.
      EmitMovRegReg(ARM64Register.X30, ARM64Register.X16);
      DefineLabel(guardLabel);

      // Pattern: x29 near top of frame, locals below.
      // STP X29, X30, [SP, #-16]!  (save regs, SP -= 16)
      var imm7_16 = (uint)((-16 / 8) & 0x7F);
      EmitWord(0xA9800000 | (imm7_16 << 15) | (30u << 10) | (31u << 5) | 29u);
      // MOV X29, SP  (x29 = old_sp - 16)
      EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);
      // Allocate space for locals below x29
      int localsSize = stackSize - 16;
      if (localsSize > 0) {
        if (localsSize <= 4096) {
          // Small frame: single subtract, no probing needed
          EmitAddSubImm(ARM64Register.Sp, ARM64Register.Sp, localsSize, isAdd: false);
        } else {
          // Large frame: probe each page to avoid skipping the guard page.
          // Use X16 as scratch (caller-saved, not used for args at prologue time).
          // Loop: subtract 4096, store zero (probe), repeat until remainder < 4096.
          EmitMovRegImm(ARM64Register.X16, localsSize);
          var probeLoop = $"__stackprobe_{_uniqueLabelCounter}";
          var probeDone = $"__stackprobe_done_{_uniqueLabelCounter}";
          _uniqueLabelCounter++;

          // X17 = 4096 (page size, constant across loop)
          EmitMovRegImm(ARM64Register.X17, 4096);

          DefineLabel(probeLoop);
          // CMP X16, X17 (remaining vs 4096)
          EmitWord(0xEB00001F | (Reg(ARM64Register.X17) << 16) | (Reg(ARM64Register.X16) << 5));
          _condBranchFixups.Add((_code.Count, probeDone));
          EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Lt)); // B.LT done
          // SUB SP, SP, X17, UXTX (extended register form: Rd=31 means SP, not XZR)
          EmitWord(0xCB206000 | (Reg(ARM64Register.X17) << 16) | (Reg(ARM64Register.Sp) << 5) | Reg(ARM64Register.Sp));
          // Probe: STR XZR, [SP] (touch the page)
          EmitWord(0xF90003FF); // STR XZR, [SP]
          // SUB X16, X16, X17
          EmitWord(0xCB000000 | (Reg(ARM64Register.X17) << 16) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X16));
          EmitBranch(probeLoop);

          DefineLabel(probeDone);
          // Subtract remainder
          // SUB SP, SP, X16, UXTX (extended register form: Rd=31 means SP, not XZR)
          EmitWord(0xCB206000 | (Reg(ARM64Register.X16) << 16) | (Reg(ARM64Register.Sp) << 5) | Reg(ARM64Register.Sp));
        }
      }
    }
  }

  private void EmitEpilogue() {
    // MOV SP, X29 (restore SP to frame pointer, discards locals)
    EmitWord(0x91000000 | (29u << 5) | 31u); // ADD SP, X29, #0 (MOV SP, X29)
    // LDP X29, X30, [SP], #16 (restore saved regs, SP += 16 = caller's SP)
    EmitWord(0xA8C10000 | (30u << 10) | (31u << 5) | 29u);
  }

  private void EmitMovRegReg(ARM64Register dest, ARM64Register src) {
    if (src == ARM64Register.Sp || dest == ARM64Register.Sp) {
      // MOV involving SP uses ADD Xd, Xn, #0
      EmitWord(0x91000000 | (Reg(src) << 5) | Reg(dest));
    } else {
      // MOV Xd, Xm = ORR Xd, XZR, Xm
      EmitWord(0xAA0003E0 | (Reg(src) << 16) | Reg(dest));
    }
  }

  private void EmitMovRegImm(ARM64Register dest, long value) {
    var uval = (ulong)value;

    // Check if zero
    if (uval == 0) {
      // MOV Xd, XZR
      EmitWord(0xAA1F03E0 | Reg(dest));
      return;
    }

    // Check if simple MOVZ (fits in 16 bits at any shift)
    for (int shift = 0; shift < 4; shift++) {
      var shifted = uval >> (shift * 16);
      if (shifted <= 0xFFFF && (shifted << (shift * 16)) == uval) {
        // MOVZ Xd, #imm16, LSL #shift*16
        EmitWord(0xD2800000 | ((uint)shift << 21) | ((uint)(shifted & 0xFFFF) << 5) | Reg(dest));
        return;
      }
    }

    // Check if MOVN is better (for negative values close to -1)
    var notVal = ~uval;
    for (int shift = 0; shift < 4; shift++) {
      var shifted = notVal >> (shift * 16);
      if (shifted <= 0xFFFF && (shifted << (shift * 16)) == notVal) {
        // MOVN Xd, #imm16, LSL #shift*16
        EmitWord(0x92800000 | ((uint)shift << 21) | ((uint)(shifted & 0xFFFF) << 5) | Reg(dest));
        return;
      }
    }

    // General case: MOVZ + up to 3 MOVK
    var hw0 = (uint)(uval & 0xFFFF);
    var hw1 = (uint)((uval >> 16) & 0xFFFF);
    var hw2 = (uint)((uval >> 32) & 0xFFFF);
    var hw3 = (uint)((uval >> 48) & 0xFFFF);

    // Find first non-zero halfword for MOVZ
    bool first = true;
    if (hw0 != 0 || (hw1 == 0 && hw2 == 0 && hw3 == 0)) {
      EmitWord(0xD2800000 | (hw0 << 5) | Reg(dest)); // MOVZ Xd, #hw0
      first = false;
    }
    if (hw1 != 0) {
      if (first) {
        EmitWord(0xD2800000 | (1u << 21) | (hw1 << 5) | Reg(dest)); // MOVZ Xd, #hw1, LSL#16
        first = false;
      } else {
        EmitWord(0xF2A00000 | (hw1 << 5) | Reg(dest)); // MOVK Xd, #hw1, LSL#16
      }
    }
    if (hw2 != 0) {
      if (first) {
        EmitWord(0xD2800000 | (2u << 21) | (hw2 << 5) | Reg(dest)); // MOVZ Xd, #hw2, LSL#32
        first = false;
      } else {
        EmitWord(0xF2C00000 | (hw2 << 5) | Reg(dest)); // MOVK Xd, #hw2, LSL#32
      }
    }
    if (hw3 != 0) {
      if (first) {
        EmitWord(0xD2800000 | (3u << 21) | (hw3 << 5) | Reg(dest));
      } else {
        EmitWord(0xF2E00000 | (hw3 << 5) | Reg(dest)); // MOVK Xd, #hw3, LSL#48
      }
    }
  }

  private void EmitStoreToStack(int displacement, ARM64Register src, int sizeInBytes) {
    // STR to [X29, #displacement]
    switch (sizeInBytes) {
      case 8:
        EmitLoadStoreUnsignedImm(0xF9000000, src, ARM64Register.X29, displacement, 8);
        break;
      case 4:
        EmitLoadStoreUnsignedImm(0xB9000000, src, ARM64Register.X29, displacement, 4);
        break;
      case 2:
        EmitLoadStoreUnsignedImm(0x79000000, src, ARM64Register.X29, displacement, 2);
        break;
      case 1:
        EmitLoadStoreUnsignedImm(0x39000000, src, ARM64Register.X29, displacement, 1);
        break;
    }
  }

  private void EmitLoadFromStack(ARM64Register dest, int displacement, int sizeInBytes) {
    switch (sizeInBytes) {
      case 8:
        EmitLoadStoreUnsignedImm(0xF9400000, dest, ARM64Register.X29, displacement, 8);
        break;
      case 4:
        EmitLoadStoreUnsignedImm(0xB9400000, dest, ARM64Register.X29, displacement, 4);
        break;
      case 2:
        EmitLoadStoreUnsignedImm(0x79400000, dest, ARM64Register.X29, displacement, 2);
        break;
      case 1:
        EmitLoadStoreUnsignedImm(0x39400000, dest, ARM64Register.X29, displacement, 1);
        break;
    }
  }

  private void EmitStoreIndirect(ARM64Register baseReg, int displacement, ARM64Register src, int sizeInBytes) {
    uint opcode = sizeInBytes switch {
      8 => 0xF9000000,
      4 => 0xB9000000,
      2 => 0x79000000,
      1 => 0x39000000,
      _ => throw new ArgumentException($"Invalid store size: {sizeInBytes}")
    };
    EmitLoadStoreUnsignedImm(opcode, src, baseReg, displacement, sizeInBytes);
  }

  private void EmitLoadIndirect(ARM64Register dest, ARM64Register baseReg, int displacement, int sizeInBytes) {
    uint opcode = sizeInBytes switch {
      8 => 0xF9400000,
      4 => 0xB9400000,
      2 => 0x79400000,
      1 => 0x39400000,
      _ => throw new ArgumentException($"Invalid load size: {sizeInBytes}")
    };
    EmitLoadStoreUnsignedImm(opcode, dest, baseReg, displacement, sizeInBytes);
  }

  private void EmitLoadStoreUnsignedImm(uint opcode, ARM64Register rt, ARM64Register rn, int offset, int scale) {
    if (offset >= 0 && offset % scale == 0 && offset / scale < 4096) {
      var scaledImm = (uint)(offset / scale);
      EmitWord(opcode | (scaledImm << 10) | (Reg(rn) << 5) | Reg(rt));
    } else if (offset >= -256 && offset <= 255) {
      // Use unscaled LDUR/STUR
      // Convert base opcode to unscaled variant
      // LDUR/STUR: size_opc 11 1 00 0 0 0 0 imm9 00 Rn Rt
      var baseOp = opcode & 0xFFC00000; // keep size and V and opc bits
      // Actually unscaled offset: replace bit 24 (switch from unsigned offset to unscaled)
      baseOp = (baseOp & ~0x01000000u) | 0x00000000u; // clear bit 24
      // Bit [11:10] = 00 for unscaled
      var imm9 = (uint)(offset & 0x1FF);
      EmitWord(baseOp | (imm9 << 12) | (Reg(rn) << 5) | Reg(rt));
    } else {
      // Large offset: load offset into X16, then use register offset
      EmitMovRegImm(ARM64Register.X16, offset);
      // ADD X16, Rn, X16
      EmitWord(0x8B000000 | (Reg(ARM64Register.X16) << 16) | (Reg(rn) << 5) | Reg(ARM64Register.X16));
      // Now use zero offset with X16 as base
      EmitWord(opcode | (Reg(ARM64Register.X16) << 5) | Reg(rt));
    }
  }

  private void EmitAluRegReg(uint baseOpcode, ARM64Register dest, ARM64Register src1, ARM64Register src2) {
    EmitWord(baseOpcode | (Reg(src2) << 16) | (Reg(src1) << 5) | Reg(dest));
  }

  /// <summary>
  /// Set or clear a bit in a bit string at [baseReg + offset].
  /// bitIndex is a register containing the bit index (0-based from the base).
  /// Uses X14, X15, X16, X17 as scratch (all caller-saved, not in VReg map).
  /// </summary>
  internal void EmitBitTestAndModify(ARM64Register baseReg, int offset, ARM64Register bitIndex, bool clear) {
    // Compute qword address: X16 = baseReg + offset + (bitIndex >> 6) * 8
    // LSR X16, bitIndex, #6
    EmitWord(0xD340FC00 | (6u << 16) | (Reg(bitIndex) << 5) | Reg(ARM64Register.X16));
    // LSL X16, X16, #3
    var immr_lsl3 = (uint)((64 - 3) & 63); // 61
    var imms_lsl3 = (uint)((63 - 3) & 63); // 60
    EmitWord(0xD3400000 | (immr_lsl3 << 16) | (imms_lsl3 << 10) | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X16));
    // ADD X16, baseReg, X16
    EmitAluRegReg(0x8B000000, ARM64Register.X16, baseReg, ARM64Register.X16);
    // ADD X16, X16, #offset (offset is ArenaMetaOffBitmap = 0x10, fits in imm12)
    EmitAddSubImm(ARM64Register.X16, ARM64Register.X16, offset, true);

    // Load qword: X17 = [X16]
    EmitLoadIndirect(ARM64Register.X17, ARM64Register.X16, 0, 8);

    // Compute bit position within qword: X15 = bitIndex & 63
    // AND X15, bitIndex, #0x3F (logical immediate: N=1, immr=0, imms=5)
    EmitWord(0x92401400u | (Reg(bitIndex) << 5) | Reg(ARM64Register.X15));

    // Compute mask: X14 = 1 << X15
    EmitMovRegImm(ARM64Register.X14, 1);
    // LSLV X14, X14, X15
    EmitWord(0x9AC02000 | (Reg(ARM64Register.X15) << 16) | (Reg(ARM64Register.X14) << 5) | Reg(ARM64Register.X14));

    if (clear) {
      // BIC X17, X17, X14 (clear bit)
      EmitWord(0x8A200000 | (Reg(ARM64Register.X14) << 16) | (Reg(ARM64Register.X17) << 5) | Reg(ARM64Register.X17));
    } else {
      // ORR X17, X17, X14 (set bit)
      EmitAluRegReg(0xAA000000, ARM64Register.X17, ARM64Register.X17, ARM64Register.X14);
    }

    // Store back: [X16] = X17
    EmitStoreIndirect(ARM64Register.X16, 0, ARM64Register.X17, 8);
  }

  private void EmitAddSubImm(ARM64Register dest, ARM64Register src, long immediate, bool isAdd) {
    if (immediate >= 0 && immediate < 4096) {
      var opcode = isAdd ? 0x91000000u : 0xD1000000u;
      EmitWord(opcode | ((uint)immediate << 10) | (Reg(src) << 5) | Reg(dest));
    } else if (immediate < 0 && immediate > -4096) {
      // Flip add/sub for negative immediate
      var opcode = isAdd ? 0xD1000000u : 0x91000000u;
      EmitWord(opcode | ((uint)(-immediate) << 10) | (Reg(src) << 5) | Reg(dest));
    } else {
      // Large immediate: load into X16, then use register variant
      EmitMovRegImm(ARM64Register.X16, immediate);
      var opcode = isAdd ? 0x8B000000u : 0xCB000000u;
      EmitWord(opcode | (Reg(ARM64Register.X16) << 16) | (Reg(src) << 5) | Reg(dest));
    }
  }

  private void EmitCmpImm(ARM64Register lhs, long immediate) {
    if (immediate >= 0 && immediate < 4096) {
      // CMP Xn, #imm = SUBS XZR, Xn, #imm
      EmitWord(0xF100001F | ((uint)immediate << 10) | (Reg(lhs) << 5));
    } else {
      EmitMovRegImm(ARM64Register.X16, immediate);
      EmitWord(0xEB00001F | (Reg(ARM64Register.X16) << 16) | (Reg(lhs) << 5));
    }
  }

  private void EmitBranch(string target) {
    _branchFixups.Add((_code.Count, target));
    EmitWord(0x14000000); // B placeholder
  }

  private void EmitBranchCond(ARM64ConditionCode condition, string target) {
    _condBranchFixups.Add((_code.Count, target));
    EmitWord(0x54000000 | CondCode(condition)); // B.cond placeholder
  }

  private void EmitBranchLink(string target, bool zeroSecondArg = false) {
    if (zeroSecondArg) EmitMovRegImm(ARM64Register.X1, 0);
    _branchFixups.Add((_code.Count, target));
    EmitWord(0x94000000); // BL placeholder
  }

  private void EmitStoreToSp(int offset, ARM64Register src) {
    EmitLoadStoreUnsignedImm(0xF9000000, src, ARM64Register.Sp, offset, 8);
  }

  private void EmitAdrpAddFixup(ARM64Register dest, List<(int, int, string)> fixupList, string label) {
    var adrpOffset = _code.Count;
    EmitWord(0x90000000 | Reg(dest)); // ADRP Xd, #0 (placeholder)
    var addOffset = _code.Count;
    EmitWord(0x91000000 | (Reg(dest) << 5) | Reg(dest)); // ADD Xd, Xd, #0 (placeholder)
    fixupList.Add((adrpOffset, addOffset, label));
  }

  private void EmitGlobalAccess(ARM64Register reg, string globalName, int size, bool isStore, bool isFloat, bool is64bit) {
    var adrpOffset = _code.Count;
    EmitWord(0x90000000 | Reg(ARM64Register.X17)); // ADRP X17, #0 (placeholder)
    var accessOffset = _code.Count;
    // Emit LDR/STR [X17, #0] placeholder
    uint opcode;
    if (isStore) {
      opcode = size switch {
        8 => 0xF9000000,
        4 => 0xB9000000,
        2 => 0x79000000,
        1 => 0x39000000,
        _ => 0xF9000000
      };
    } else {
      opcode = size switch {
        8 => 0xF9400000,
        4 => 0xB9400000,
        2 => 0x79400000,
        1 => 0x39400000,
        _ => 0xF9400000
      };
    }
    EmitWord(opcode | (Reg(ARM64Register.X17) << 5) | Reg(reg));
    _globalAccessFixups.Add((adrpOffset, accessOffset, globalName, isStore, size, isFloat, is64bit));
  }

  private void EmitGlobalAccessFloat(ARM64FloatRegister reg, string globalName, FloatPrecision precision, bool isStore) {
    var adrpOffset = _code.Count;
    EmitWord(0x90000000 | Reg(ARM64Register.X17)); // ADRP X17, #0
    var accessOffset = _code.Count;
    uint opcode;
    var is64 = precision == FloatPrecision.F64;
    if (isStore) {
      opcode = is64 ? 0xFD000000u : 0xBD000000u; // STR Dt/St
    } else {
      opcode = is64 ? 0xFD400000u : 0xBD400000u; // LDR Dt/St
    }
    EmitWord(opcode | (Reg(ARM64Register.X17) << 5) | FloatReg(reg));
    _globalAccessFixups.Add((adrpOffset, accessOffset, globalName, isStore, is64 ? 8 : 4, true, is64));
  }

  // --- Float instruction encoding ---

  private void EmitFloatArith(uint baseOp, ARM64FloatRegister dest, ARM64FloatRegister src1, ARM64FloatRegister src2, FloatPrecision precision) {
    var op = precision == FloatPrecision.F32 ? (baseOp & ~0x00400000u) : baseOp; // clear type bit for F32
    EmitWord(op | (FloatReg(src2) << 16) | (FloatReg(src1) << 5) | FloatReg(dest));
  }

  private void EmitFloatUnary(uint baseOp, ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) {
    var op = precision == FloatPrecision.F32 ? (baseOp & ~0x00400000u) : baseOp;
    EmitWord(op | (FloatReg(src) << 5) | FloatReg(dest));
  }

  private void EmitFcmp(ARM64FloatRegister lhs, ARM64FloatRegister rhs, FloatPrecision precision) {
    // FCMP Dn, Dm: 0001 1110 011 Rm 00 1000 Rn 00 000
    var typebit = precision == FloatPrecision.F64 ? 0x00400000u : 0x00000000u;
    EmitWord(0x1E202000 | typebit | (FloatReg(rhs) << 16) | (FloatReg(lhs) << 5));
  }

  private void EmitScvtf(ARM64FloatRegister dest, ARM64Register src, FloatPrecision precision) {
    // SCVTF Dd, Xn: sf=1, type, rmode=00, opcode=010
    var typebit = precision == FloatPrecision.F64 ? 0x00400000u : 0x00000000u;
    EmitWord(0x9E220000 | typebit | (Reg(src) << 5) | FloatReg(dest));
  }

  private void EmitFcvtzs(ARM64Register dest, ARM64FloatRegister src, FloatPrecision precision) {
    // FCVTZS Xd, Dn: sf=1, type, rmode=11, opcode=000
    var typebit = precision == FloatPrecision.F64 ? 0x00400000u : 0x00000000u;
    EmitWord(0x9E780000 | typebit | (FloatReg(src) << 5) | Reg(dest));
  }

  private void EmitUcvtf(ARM64FloatRegister dest, ARM64Register src, FloatPrecision precision) {
    // UCVTF Dd, Xn: sf=1, type, rmode=00, opcode=011
    var typebit = precision == FloatPrecision.F64 ? 0x00400000u : 0x00000000u;
    EmitWord(0x9E230000 | typebit | (Reg(src) << 5) | FloatReg(dest));
  }

  private void EmitFcvtzu(ARM64Register dest, ARM64FloatRegister src, FloatPrecision precision) {
    // FCVTZU Xd, Dn: sf=1, type, rmode=11, opcode=001
    var typebit = precision == FloatPrecision.F64 ? 0x00400000u : 0x00000000u;
    EmitWord(0x9E790000 | typebit | (FloatReg(src) << 5) | Reg(dest));
  }

  private void EmitFcvt(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision destPrecision) {
    if (destPrecision == FloatPrecision.F32) {
      // FCVT Sd, Dn (double to single): 0001 1110 0110 0001 0100 00 Rn Rd
      EmitWord(0x1E624000 | (FloatReg(src) << 5) | FloatReg(dest));
    } else {
      // FCVT Dd, Sn (single to double): 0001 1110 0010 0010 1100 00 Rn Rd
      EmitWord(0x1E22C000 | (FloatReg(src) << 5) | FloatReg(dest));
    }
  }

  private void EmitFmovToFloat(ARM64FloatRegister dest, ARM64Register src, FloatPrecision precision) {
    if (precision == FloatPrecision.F64) {
      // FMOV Dd, Xn: 1001 1110 0110 0111 0000 00 Rn Rd
      EmitWord(0x9E670000 | (Reg(src) << 5) | FloatReg(dest));
    } else {
      // FMOV Sd, Wn: 0001 1110 0010 0111 0000 00 Rn Rd
      EmitWord(0x1E270000 | (Reg(src) << 5) | FloatReg(dest));
    }
  }

  private void EmitFmovToGpr(ARM64Register dest, ARM64FloatRegister src, FloatPrecision precision) {
    if (precision == FloatPrecision.F64) {
      // FMOV Xd, Dn: 1001 1110 0110 0110 0000 00 Rn Rd
      EmitWord(0x9E660000 | (FloatReg(src) << 5) | Reg(dest));
    } else {
      // FMOV Wd, Sn: 0001 1110 0010 0110 0000 00 Rn Rd
      EmitWord(0x1E260000 | (FloatReg(src) << 5) | Reg(dest));
    }
  }

  private void EmitFmovRegReg(ARM64FloatRegister dest, ARM64FloatRegister src, FloatPrecision precision) {
    var typebit = precision == FloatPrecision.F64 ? 0x00400000u : 0x00000000u;
    // FMOV Dd, Dn: 0001 1110 0110 0000 0100 00 Rn Rd
    EmitWord(0x1E204000 | typebit | (FloatReg(src) << 5) | FloatReg(dest));
  }

  private void EmitFloatLoadFromStack(ARM64FloatRegister dest, int displacement, FloatPrecision precision) {
    var opcode = precision == FloatPrecision.F64 ? 0xFD400000u : 0xBD400000u;
    var scale = precision == FloatPrecision.F64 ? 8 : 4;
    EmitFloatLoadStoreUnsignedImm(opcode, dest, ARM64Register.X29, displacement, scale);
  }

  private void EmitFloatStoreToStack(int displacement, ARM64FloatRegister src, FloatPrecision precision) {
    var opcode = precision == FloatPrecision.F64 ? 0xFD000000u : 0xBD000000u;
    var scale = precision == FloatPrecision.F64 ? 8 : 4;
    EmitFloatLoadStoreUnsignedImm(opcode, src, ARM64Register.X29, displacement, scale);
  }

  private void EmitFloatLoadIndirect(ARM64FloatRegister dest, ARM64Register baseReg, int displacement, FloatPrecision precision) {
    var opcode = precision == FloatPrecision.F64 ? 0xFD400000u : 0xBD400000u;
    var scale = precision == FloatPrecision.F64 ? 8 : 4;
    EmitFloatLoadStoreUnsignedImm(opcode, dest, baseReg, displacement, scale);
  }

  private void EmitFloatStoreIndirect(ARM64Register baseReg, int displacement, ARM64FloatRegister src, FloatPrecision precision) {
    var opcode = precision == FloatPrecision.F64 ? 0xFD000000u : 0xBD000000u;
    var scale = precision == FloatPrecision.F64 ? 8 : 4;
    EmitFloatLoadStoreUnsignedImm(opcode, src, baseReg, displacement, scale);
  }

  private void EmitFloatLoadStoreUnsignedImm(uint opcode, ARM64FloatRegister rt, ARM64Register rn, int offset, int scale) {
    if (offset >= 0 && offset % scale == 0 && offset / scale < 4096) {
      var scaledImm = (uint)(offset / scale);
      EmitWord(opcode | (scaledImm << 10) | (Reg(rn) << 5) | FloatReg(rt));
    } else if (offset >= -256 && offset <= 255) {
      // Unscaled offset variant
      var baseOp = (opcode & ~0x01000000u); // clear bit 24 for unscaled
      var imm9 = (uint)(offset & 0x1FF);
      EmitWord(baseOp | (imm9 << 12) | (Reg(rn) << 5) | FloatReg(rt));
    } else {
      // Large offset
      EmitMovRegImm(ARM64Register.X16, offset);
      EmitWord(0x8B000000 | (Reg(ARM64Register.X16) << 16) | (Reg(rn) << 5) | Reg(ARM64Register.X16));
      EmitWord(opcode | (Reg(ARM64Register.X16) << 5) | FloatReg(rt));
    }
  }

  private void EmitFloatLoadRdata(ARM64FloatRegister dest, string rdataLabel, FloatPrecision precision) {
    // ADRP X17, #page
    var adrpOffset = _code.Count;
    EmitWord(0x90000000 | Reg(ARM64Register.X17));
    // LDR Dt, [X17, #pageoff]
    var ldrOffset = _code.Count;
    var opcode = precision == FloatPrecision.F64 ? 0xFD400000u : 0xBD400000u;
    EmitWord(opcode | (Reg(ARM64Register.X17) << 5) | FloatReg(dest));
    _floatRdataFixups.Add((adrpOffset, ldrOffset, rdataLabel, precision == FloatPrecision.F64));
  }

  // --- Memcpy and bulk zero loops ---

  private void EmitMemcpyLoop() {
    // X0 = dst, X1 = src, X2 = count (bytes)
    // CBZ X2, done
    var loopLabel = $"__memcpy_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__memcpy_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // CBZ X2, done
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X2)); // CBZ X2 (placeholder offset)

    DefineLabel(loopLabel);
    // LDRB W3, [X1], #1
    EmitWord(0x38401420 | (1u << 5) | 3u); // post-index +1, X1, W3
    // Actually: LDRB W3, [X1], #1 = 0x38401423
    // Let me use explicit encoding:
    // LDRB (post-index): 00 111 000 010 imm9 01 Rn Rt
    // imm9 = 1 = 0x001
    PatchWord(_code.Count - 4, 0x38401423); // LDRB W3, [X1], #1

    // STRB W3, [X0], #1
    EmitWord(0x38001403); // STRB W3, [X0], #1

    // SUB X2, X2, #1
    EmitWord(0xD1000442); // SUB X2, X2, #1

    // CBNZ X2, loop
    _condBranchFixups.Add((_code.Count, loopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X2)); // CBNZ X2 (placeholder)

    DefineLabel(doneLabel);
  }

  private void EmitBulkZeroLoop() {
    // X0 = dst, X1 = count (in qwords)
    var loopLabel = $"__bulkzero_loop_{_uniqueLabelCounter}";
    var doneLabel = $"__bulkzero_done_{_uniqueLabelCounter}";
    _uniqueLabelCounter++;

    // CBZ X1, done
    _condBranchFixups.Add((_code.Count, doneLabel));
    EmitWord(0xB4000000 | Reg(ARM64Register.X1));

    DefineLabel(loopLabel);
    // STR XZR, [X0], #8
    EmitWord(0xF800841F); // STR XZR, [X0], #8

    // SUB X1, X1, #1
    EmitWord(0xD1000421); // SUB X1, X1, #1

    // CBNZ X1, loop
    _condBranchFixups.Add((_code.Count, loopLabel));
    EmitWord(0xB5000000 | Reg(ARM64Register.X1));

    DefineLabel(doneLabel);
  }

  // --- Green thread / runtime helpers ---

  /// Load a global's address into a register via ADRP+ADD
  private void EmitGlobalLeaReg(ARM64Register dest, string globalName) {
    EmitAdrpAddFixup(dest, _globalAdrpFixups, globalName);
  }

  /// Load a 64-bit value from a global into a register via ADRP+ADD+LDR
  private void EmitGlobalLoadReg(ARM64Register dest, string globalName) {
    EmitAdrpAddFixup(ARM64Register.X17, _globalAdrpFixups, globalName);
    EmitLoadStoreUnsignedImm(0xF9400000, dest, ARM64Register.X17, 0, 8); // LDR dest, [X17]
  }

  /// Store a 64-bit register to a global via ADRP+ADD+STR
  private void EmitGlobalStoreReg(ARM64Register src, string globalName) {
    EmitAdrpAddFixup(ARM64Register.X17, _globalAdrpFixups, globalName);
    EmitLoadStoreUnsignedImm(0xF9000000, src, ARM64Register.X17, 0, 8); // STR src, [X17]
  }

  /// Emit CBZ Xn, label (compare and branch if zero)
  private void EmitCbz(ARM64Register reg, string label) {
    _condBranchFixups.Add((_code.Count, label));
    EmitWord(0xB4000000 | Reg(reg));
  }

  /// Emit CBNZ Xn, label (compare and branch if non-zero)
  private void EmitCbnz(ARM64Register reg, string label) {
    _condBranchFixups.Add((_code.Count, label));
    EmitWord(0xB5000000 | Reg(reg));
  }

  /// Emit STP Xt1, Xt2, [SP, #-16]! (pre-index decrement)
  private void EmitStpPreIndex(ARM64Register rt1, ARM64Register rt2) {
    // STP <Xt1>, <Xt2>, [SP, #-16]!
    // opc=10, V=0, type=101 (pre-index), imm7=-16/8=-2 => 0x7E (7-bit signed)
    uint imm7 = unchecked((uint)(-16 / 8)) & 0x7Fu;
    EmitWord(0xA9800000 | (imm7 << 15) | (Reg(rt2) << 10) | (31u << 5) | Reg(rt1));
  }

  /// Emit LDP Xt1, Xt2, [SP], #16 (post-index increment)
  private void EmitLdpPostIndex(ARM64Register rt1, ARM64Register rt2) {
    uint imm7 = (16u / 8) & 0x7Fu;
    EmitWord(0xA8C00000 | (imm7 << 15) | (Reg(rt2) << 10) | (31u << 5) | Reg(rt1));
  }

  /// Emit STP Dt1, Dt2, [SP, #-16]! (SIMD pre-index)
  private void EmitStpFpPreIndex(ARM64FloatRegister rt1, ARM64FloatRegister rt2) {
    uint imm7 = unchecked((uint)(-16 / 8)) & 0x7Fu;
    EmitWord(0x6D800000 | (imm7 << 15) | (FloatReg(rt2) << 10) | (31u << 5) | FloatReg(rt1));
  }

  /// Emit LDP Dt1, Dt2, [SP], #16 (SIMD post-index)
  private void EmitLdpFpPostIndex(ARM64FloatRegister rt1, ARM64FloatRegister rt2) {
    uint imm7 = (16u / 8) & 0x7Fu;
    EmitWord(0x6CC00000 | (imm7 << 15) | (FloatReg(rt2) << 10) | (31u << 5) | FloatReg(rt1));
  }

  /// Emit CMP Xn, Xm (register compare)
  private void EmitCmpRegReg(ARM64Register lhs, ARM64Register rhs) {
    // SUBS XZR, Xn, Xm
    EmitWord(0xEB00001F | (Reg(rhs) << 16) | (Reg(lhs) << 5));
  }

  // --- Jump table dispatch ---

  private void EmitJumpTableDispatch(ARM64JumpTableOp jt) {
    var indexReg = jt.IndexReg;

    // 1. Bounds check: CMP Xn, #caseCount (unsigned — catches negative values too)
    EmitWord(0xF100001F | ((uint)jt.CaseCount << 10) | (Reg(indexReg) << 5));
    // 2. B.HS defaultTarget (out of bounds → default)
    _condBranchFixups.Add((_code.Count, jt.DefaultTarget));
    EmitWord(0x54000000 | CondCode(ARM64ConditionCode.Hs));

    // 3. ADRP X17, tableLabel + ADD X17, X17, #pageOff — load table base address
    EmitAdrpAddFixup(ARM64Register.X17, _rdataAdrpFixups, jt.RdataLabel);

    // 4. LDRSW X16, [X17, Xn, LSL #2] — load signed 32-bit entry at index*4
    EmitWord(0xB8A07800 | (Reg(indexReg) << 16) | (Reg(ARM64Register.X17) << 5) | Reg(ARM64Register.X16));

    // 5. ADD X17, X17, X16 — compute target = tableBase + offset
    EmitAluRegReg(0x8B000000, ARM64Register.X17, ARM64Register.X17, ARM64Register.X16);

    // 6. BR X17 — indirect branch to target
    EmitWord(0xD61F0000 | (Reg(ARM64Register.X17) << 5));

    // Record fixups for rdata patching
    for (int i = 0; i < jt.CaseTargets.Length; i++) {
      _jumpTableFixups.Add((i, jt.CaseTargets[i], jt.RdataLabel));
    }
  }

  public void ResolveJumpTableFixups(uint textSectionFileOffset, ulong textSegmentVMAddr,
      uint rdataSectionFileOffset) {
    // Each rdata table entry is a signed 32-bit offset from the table base to the target code label.
    // At runtime: target = tableBase + entry
    foreach (var (slotIndex, codeLabel, tableLabel) in _jumpTableFixups) {
      if (!_labels.TryGetValue(codeLabel, out var codeLabelOffset))
        throw new InvalidOperationException($"Unresolved jump table target: {codeLabel}");
      if (!_rdataLabels.TryGetValue(tableLabel, out var tableBaseOffset))
        throw new InvalidOperationException($"Unresolved jump table label: {tableLabel}");

      // Code label VM address
      var codeLabelVMAddr = textSegmentVMAddr + textSectionFileOffset + (ulong)codeLabelOffset;
      // Table base VM address
      var tableBaseVMAddr = textSegmentVMAddr + rdataSectionFileOffset + (ulong)tableBaseOffset;
      // Entry = signed offset from table base to code label
      var entry = (int)((long)codeLabelVMAddr - (long)tableBaseVMAddr);

      // Patch the rdata entry (4 bytes, little-endian)
      var off = tableBaseOffset + slotIndex * 4;
      _rdata[off] = (byte)(entry & 0xFF);
      _rdata[off + 1] = (byte)((entry >> 8) & 0xFF);
      _rdata[off + 2] = (byte)((entry >> 16) & 0xFF);
      _rdata[off + 3] = (byte)((entry >> 24) & 0xFF);
    }
  }

  // --- Start wrapper ---

  public void EmitStartWrapper(string mainFunctionName, string? globalCleanupFunctionName = null, string? moduleInitFunctionName = null) {
    // Define writable global storage for argc/argv (must be in __DATA, not __TEXT)
    DefineGlobal("__argc_global", 8, 0);
    DefineGlobal("__argv_global", 8, 0);

    DefineLabel("_start");

    // LC_MAIN entry: X0 = argc, X1 = argv — save before anything clobbers them
    // Use X9/X10 as scratch to preserve across the STP
    EmitMovRegReg(ARM64Register.X9, ARM64Register.X0);
    EmitMovRegReg(ARM64Register.X10, ARM64Register.X1);

    // Zero X29 and X30 so the frame chain terminates here for stack walkers
    EmitMovRegImm(ARM64Register.X29, 0);
    EmitMovRegImm(ARM64Register.X30, 0);
    // Set up a minimal frame: STP x29, x30, [sp, #-32]!
    EmitWord(0xA9BE7BFD); // STP x29, x30, [sp, #-32]!
    EmitMovRegReg(ARM64Register.X29, ARM64Register.Sp);

    // Store argc and argv to writable globals in __DATA segment
    EmitAdrpAddFixup(ARM64Register.X11, _globalAdrpFixups, "__argc_global");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X9, ARM64Register.X11, 0, 8); // STR X9, [X11]
    EmitAdrpAddFixup(ARM64Register.X11, _globalAdrpFixups, "__argv_global");
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X10, ARM64Register.X11, 0, 8); // STR X10, [X11]

    // Call module init if present
    if (!string.IsNullOrEmpty(moduleInitFunctionName)) {
      EmitBranchLink(moduleInitFunctionName);
    }

    // Initialize green thread runtime
    EmitBranchLink("__gt_init");

    // Initialize kqueue I/O subsystem
    EmitBranchLink("__io_init");

    // Call main
    EmitBranchLink(mainFunctionName);

    // Save return value to stack [x29, #16]
    EmitLoadStoreUnsignedImm(0xF9000000, ARM64Register.X0, ARM64Register.X29, 16, 8);

    // Drain any outstanding green threads
    EmitBranchLink("__gt_cleanup");

    // Shut down kqueue I/O subsystem
    EmitBranchLink("__io_shutdown");

    // Call global cleanup if present
    if (!string.IsNullOrEmpty(globalCleanupFunctionName)) {
      EmitBranchLink(globalCleanupFunctionName);
    }

    // mm_leak_check(exit_code) — returns 101 if leaked, original exit_code otherwise
    EmitLoadStoreUnsignedImm(0xF9400000, ARM64Register.X0, ARM64Register.X29, 16, 8); // X0 = main's return value
    EmitBranchLink("mm_leak_check");
    // X0 = exit code from mm_leak_check

    // Exit via libsystem _exit
    EmitCallImport("_exit");

    // BRK in case we somehow get past exit
    EmitWord(0xD4200000); // BRK #0
  }

  // --- Symbol table ---

  public void EmitSymbolTable(List<(string name, int codeOffset)> entries) {
    // Same format as X86: pairs of (name_offset_in_symdata, code_offset)
    // Write name strings to symdata, then write the table entries to symdata
    var nameOffsets = new List<int>();
    foreach (var (name, _) in entries) {
      var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
      var align = (8 - (_symdata.Count % 8)) % 8;
      for (var i = 0; i < align; i++) _symdata.Add(0);
      nameOffsets.Add(_symdata.Count);
      _symdata.AddRange(nameBytes);
      _symdata.Add(0); // null terminator
    }

    // Align to 8
    var pad = (8 - (_symdata.Count % 8)) % 8;
    for (var i = 0; i < pad; i++) _symdata.Add(0);

    // Define the symbol table itself
    _symdataLabels["__symtab"] = _symdata.Count;

    // Entry count
    var countBytes = BitConverter.GetBytes((long)entries.Count);
    _symdata.AddRange(countBytes);

    // Entries: (name_offset_i64, code_offset_i64) pairs
    for (int i = 0; i < entries.Count; i++) {
      _symdata.AddRange(BitConverter.GetBytes((long)nameOffsets[i]));
      _symdata.AddRange(BitConverter.GetBytes((long)entries[i].codeOffset));
    }
  }

  // --- Label resolution ---

  public void ResolveLabels() {
    // Resolve B and BL fixups (26-bit signed offset in instructions)
    foreach (var (offset, target) in _branchFixups) {
      if (!_labels.TryGetValue(target, out var targetOffset)) {
        throw new InvalidOperationException($"Undefined label: {target}");
      }
      var delta = targetOffset - offset; // byte offset
      var instrOffset = delta / 4; // instruction offset (26-bit signed)
      var existing = ReadWord(offset);
      var opcode = existing & 0xFC000000; // preserve opcode bits (B=0x14, BL=0x94)
      PatchWord(offset, opcode | ((uint)instrOffset & 0x03FFFFFF));
    }

    // Resolve B.cond / CBZ / CBNZ fixups (19-bit signed offset)
    foreach (var (offset, target) in _condBranchFixups) {
      if (!_labels.TryGetValue(target, out var targetOffset)) {
        throw new InvalidOperationException($"Undefined label: {target}");
      }
      var delta = targetOffset - offset;
      var instrOffset = delta / 4;
      var existing = ReadWord(offset);
      // B.cond: imm19 is bits [23:5], CBZ/CBNZ: imm19 is bits [23:5]
      PatchWord(offset, (existing & 0xFF00001F) | (((uint)instrOffset & 0x7FFFF) << 5));
    }
  }

  /// <summary>
  /// Resolves ADRP+ADD fixups for a given section at the specified file offset.
  /// The Mach-O layout places: __text at textFileOffset, then sections at known offsets.
  /// For ADRP+ADD: ADRP gives page-relative address, ADD gives page offset.
  /// In Mach-O with PIE, we compute addresses relative to the text segment VA base.
  /// </summary>
  public void ResolveAdrpFixups(
    uint textSectionFileOffset,
    ulong textSegmentVMAddr,
    uint rdataSectionFileOffset,
    uint symdataSectionFileOffset,
    uint ucddataSectionFileOffset,
    ulong dataSegmentVMAddr = 0) {

    // Resolve rdata ADRP+ADD
    foreach (var (adrpOffset, addOffset, label) in _rdataAdrpFixups) {
      if (!_rdataLabels.TryGetValue(label, out var rdataOffset)) {
        throw new InvalidOperationException($"Undefined rdata label: {label}");
      }
      var targetFileOffset = rdataSectionFileOffset + (uint)rdataOffset;
      PatchAdrpAdd(adrpOffset, addOffset, textSectionFileOffset, textSegmentVMAddr, targetFileOffset);
    }

    // Resolve global ADRP+ADD (data is in separate segment with different VM addr)
    foreach (var (adrpOffset, addOffset, name) in _globalAdrpFixups) {
      if (!_globalLabels.TryGetValue(name, out var globalOffset)) {
        throw new InvalidOperationException($"Undefined global: {name}");
      }
      var targetVA = dataSegmentVMAddr + (ulong)globalOffset;
      PatchAdrpAddWithVA(adrpOffset, addOffset, textSectionFileOffset, textSegmentVMAddr, targetVA);
    }

    // Resolve symdata ADRP+ADD
    foreach (var (adrpOffset, addOffset, label) in _symdataAdrpFixups) {
      if (!_symdataLabels.TryGetValue(label, out var symdataOffset)) {
        throw new InvalidOperationException($"Undefined symdata label: {label}");
      }
      var targetFileOffset = symdataSectionFileOffset + (uint)symdataOffset;
      PatchAdrpAdd(adrpOffset, addOffset, textSectionFileOffset, textSegmentVMAddr, targetFileOffset);
    }

    // Resolve ucddata ADRP+ADD
    foreach (var (adrpOffset, addOffset, label) in _ucddataAdrpFixups) {
      if (!_ucddataLabels.TryGetValue(label, out var ucddataOffset)) {
        throw new InvalidOperationException($"Undefined ucddata label: {label}");
      }
      var targetFileOffset = ucddataSectionFileOffset + (uint)ucddataOffset;
      PatchAdrpAdd(adrpOffset, addOffset, textSectionFileOffset, textSegmentVMAddr, targetFileOffset);
    }

    // Resolve function address ADRP+ADD (target is in code section)
    foreach (var (adrpOffset, addOffset, funcName) in _funcAddrAdrpFixups) {
      if (!_labels.TryGetValue(funcName, out var funcOffset)) {
        throw new InvalidOperationException($"Undefined function: {funcName}");
      }
      var targetFileOffset = textSectionFileOffset + (uint)funcOffset;
      PatchAdrpAdd(adrpOffset, addOffset, textSectionFileOffset, textSegmentVMAddr, targetFileOffset);
    }

    // Resolve float rdata fixups (ADRP + LDR)
    foreach (var (adrpOffset, ldrOffset, label, is64bit) in _floatRdataFixups) {
      if (!_rdataLabels.TryGetValue(label, out var rdataOffset)) {
        throw new InvalidOperationException($"Undefined rdata label for float: {label}");
      }
      var targetFileOffset = rdataSectionFileOffset + (uint)rdataOffset;
      PatchAdrpLdr(adrpOffset, ldrOffset, textSectionFileOffset, textSegmentVMAddr, targetFileOffset, is64bit ? 8 : 4);
    }

    // Resolve global access fixups (ADRP + LDR/STR) — data is in separate segment
    foreach (var (adrpOffset, accessOffset, name, isStore, size, isFloat, is64bit) in _globalAccessFixups) {
      if (!_globalLabels.TryGetValue(name, out var globalOffset)) {
        throw new InvalidOperationException($"Undefined global for access: {name}");
      }
      var targetVA = dataSegmentVMAddr + (ulong)globalOffset;
      PatchAdrpLdrWithVA(adrpOffset, accessOffset, textSectionFileOffset, textSegmentVMAddr, targetVA, size);
    }
  }

  private void PatchAdrpAddWithVA(int adrpOffset, int addOffset, uint textSectionFileOffset, ulong textSegmentVMAddr, ulong targetVA) {
    var instrVA = textSegmentVMAddr + textSectionFileOffset + (ulong)adrpOffset;
    var instrPage = instrVA & ~0xFFFUL;
    var targetPage = targetVA & ~0xFFFUL;
    var pageOffset = (long)(targetPage - instrPage);
    var pageOffsetInPages = pageOffset >> 12;
    var immlo = (uint)(pageOffsetInPages & 0x3);
    var immhi = (uint)((pageOffsetInPages >> 2) & 0x7FFFF);
    var existing = ReadWord(adrpOffset);
    PatchWord(adrpOffset, (existing & 0x9F00001F) | (immlo << 29) | (immhi << 5));
    var withinPage = (uint)(targetVA & 0xFFF);
    var existingAdd = ReadWord(addOffset);
    PatchWord(addOffset, (existingAdd & 0xFFC003FF) | (withinPage << 10));
  }

  private void PatchAdrpLdrWithVA(int adrpOffset, int ldrOffset, uint textSectionFileOffset, ulong textSegmentVMAddr, ulong targetVA, int scale) {
    var instrVA = textSegmentVMAddr + textSectionFileOffset + (ulong)adrpOffset;
    var instrPage = instrVA & ~0xFFFUL;
    var targetPage = targetVA & ~0xFFFUL;
    var pageOffset = (long)(targetPage - instrPage);
    var pageOffsetInPages = pageOffset >> 12;
    var immlo = (uint)(pageOffsetInPages & 0x3);
    var immhi = (uint)((pageOffsetInPages >> 2) & 0x7FFFF);
    var existing = ReadWord(adrpOffset);
    PatchWord(adrpOffset, (existing & 0x9F00001F) | (immlo << 29) | (immhi << 5));
    var withinPage = (uint)(targetVA & 0xFFF);
    if (withinPage % (uint)scale != 0)
      throw new InvalidOperationException($"ADRP+LDR target VA 0x{targetVA:X} is not aligned to scale {scale}");
    var scaledOffset = withinPage / (uint)scale;
    var existingLdr = ReadWord(ldrOffset);
    PatchWord(ldrOffset, (existingLdr & 0xFFC003FF) | (scaledOffset << 10));
  }

  private void PatchAdrpAdd(int adrpOffset, int addOffset, uint textSectionFileOffset, ulong textSegmentVMAddr, uint targetFileOffset) {
    // Compute VAs. instrVA is relative to the code buffer (starts at textSectionFileOffset).
    // targetVA is absolute file offset mapped via __TEXT segment (fileoff=0, vmaddr=textSegmentVMAddr).
    var instrVA = textSegmentVMAddr + textSectionFileOffset + (ulong)adrpOffset;
    var targetVA = textSegmentVMAddr + targetFileOffset;

    // ADRP: page-relative offset
    var instrPage = instrVA & ~0xFFFUL;
    var targetPage = targetVA & ~0xFFFUL;
    var pageOffset = (long)(targetPage - instrPage);
    var pageOffsetInPages = pageOffset >> 12;

    var immlo = (uint)(pageOffsetInPages & 0x3);
    var immhi = (uint)((pageOffsetInPages >> 2) & 0x7FFFF);
    var existing = ReadWord(adrpOffset);
    PatchWord(adrpOffset, (existing & 0x9F00001F) | (immlo << 29) | (immhi << 5));

    // ADD: page offset (12-bit)
    var withinPage = (uint)(targetVA & 0xFFF);
    var existingAdd = ReadWord(addOffset);
    PatchWord(addOffset, (existingAdd & 0xFFC003FF) | (withinPage << 10));
  }

  private void PatchAdrpLdr(int adrpOffset, int ldrOffset, uint textSectionFileOffset, ulong textSegmentVMAddr, uint targetFileOffset, int scale) {
    var instrVA = textSegmentVMAddr + textSectionFileOffset + (ulong)adrpOffset;
    var targetVA = textSegmentVMAddr + targetFileOffset;

    var instrPage = instrVA & ~0xFFFUL;
    var targetPage = targetVA & ~0xFFFUL;
    var pageOffset = (long)(targetPage - instrPage);
    var pageOffsetInPages = pageOffset >> 12;

    var immlo = (uint)(pageOffsetInPages & 0x3);
    var immhi = (uint)((pageOffsetInPages >> 2) & 0x7FFFF);
    var existing = ReadWord(adrpOffset);
    PatchWord(adrpOffset, (existing & 0x9F00001F) | (immlo << 29) | (immhi << 5));

    // LDR/STR: scaled offset within page
    var withinPage = (uint)(targetVA & 0xFFF);
    if (withinPage % (uint)scale != 0)
      throw new InvalidOperationException($"ADRP+LDR target VA 0x{targetVA:X} is not aligned to scale {scale}");
    var scaledOffset = withinPage / (uint)scale;
    var existingLdr = ReadWord(ldrOffset);
    PatchWord(ldrOffset, (existingLdr & 0xFFC003FF) | (scaledOffset << 10));
  }

  // --- Getters ---

  public byte[] GetCode() => [.. _code];
  public byte[] GetRdata() => [.. _rdata];
  public byte[] GetData() => [.. _data];
  public byte[] GetSymdata() => [.. _symdata];
  public byte[] GetUcddata() => [.. _ucddata];
  public byte[] GetGot() => [.. _got];
  public IReadOnlyList<string> GetImportNames() => _importNames;
  public bool HasImports => _importNames.Count > 0;

  // --- Dylib import support ---

  /// <summary>
  /// Register an imported function. Allocates a GOT slot (8 bytes, zero-initialized).
  /// Returns the GOT slot offset. Deduplicates by name.
  /// </summary>
  private int RegisterImport(string functionName) {
    if (_importSlots.TryGetValue(functionName, out var slot)) return slot;
    slot = _got.Count;
    _importSlots[functionName] = slot;
    _importNames.Add(functionName);
    // 8-byte GOT entry (zero, filled by dyld at load time)
    for (int i = 0; i < 8; i++) _got.Add(0);
    return slot;
  }

  /// <summary>
  /// Emit an indirect call to an imported dylib function via the GOT.
  /// Uses X16 (intra-procedure-call scratch register) as the trampoline register.
  /// Sequence: ADRP X16, got_page; LDR X16, [X16, got_offset]; BLR X16
  /// </summary>
  public void EmitCallImport(string functionName) {
    RegisterImport(functionName);
    var adrpOffset = _code.Count;
    EmitWord(0x90000000 | Reg(ARM64Register.X16)); // ADRP X16, #0 (placeholder)
    var ldrOffset = _code.Count;
    EmitWord(0xF9400000 | (Reg(ARM64Register.X16) << 5) | Reg(ARM64Register.X16)); // LDR X16, [X16, #0] (placeholder)
    EmitWord(0xD63F0000 | (Reg(ARM64Register.X16) << 5)); // BLR X16
    _gotFixups.Add((adrpOffset, ldrOffset, functionName));
  }

  /// <summary>
  /// Resolve GOT ADRP+LDR fixups. Called after layout is computed.
  /// gotSectionVMAddr is the VM address of the __got section.
  /// </summary>
  public void ResolveGotFixups(uint textSectionFileOffset, ulong textSegmentVMAddr, ulong gotSectionVMAddr) {
    foreach (var (adrpOffset, ldrOffset, functionName) in _gotFixups) {
      if (!_importSlots.TryGetValue(functionName, out var gotSlotOffset)) {
        throw new InvalidOperationException($"Undefined import: {functionName}");
      }
      var targetVA = gotSectionVMAddr + (ulong)gotSlotOffset;
      PatchAdrpLdrWithVA(adrpOffset, ldrOffset, textSectionFileOffset, textSegmentVMAddr, targetVA, 8);
    }
  }
}
