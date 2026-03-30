using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// X86-specific register manager. Manages GPR and XMM register allocation
/// during StandardToX86 conversion.
/// </summary>
public class RegisterManager : RegisterManagerBase<X86Register, X86XmmRegister, X86Op> {
  private static readonly X86Register[] _gprPool = [
    X86Register.Rax, X86Register.Rcx, X86Register.Rdx,
    X86Register.Rbx, X86Register.Rsi, X86Register.Rdi,
    X86Register.R8, X86Register.R9
  ];

  private static readonly X86XmmRegister[] _xmmPool = [
    X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2, X86XmmRegister.Xmm3,
    X86XmmRegister.Xmm4, X86XmmRegister.Xmm5, X86XmmRegister.Xmm6, X86XmmRegister.Xmm7,
    X86XmmRegister.Xmm8, X86XmmRegister.Xmm9, X86XmmRegister.Xmm10, X86XmmRegister.Xmm11,
    X86XmmRegister.Xmm12, X86XmmRegister.Xmm13, X86XmmRegister.Xmm14, X86XmmRegister.Xmm15
  ];

  private static readonly X86Register[] _callerSavedRegisters = [
    X86Register.Rax, X86Register.Rcx, X86Register.Rdx,
    X86Register.Rbx, X86Register.Rsi, X86Register.Rdi,
    X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11
  ];

  private static readonly X86XmmRegister[] _callerSavedXmmRegisters = [
    X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2,
    X86XmmRegister.Xmm3, X86XmmRegister.Xmm4, X86XmmRegister.Xmm5,
    X86XmmRegister.Xmm6, X86XmmRegister.Xmm7
  ];

  // Internal calling convention: pass up to 8 integer parameters in registers
  private static readonly X86Register[] CallConvRegs = [
    X86Register.Rcx, X86Register.Rdx, X86Register.R8, X86Register.R9,
    X86Register.Rsi, X86Register.Rdi, X86Register.Rax, X86Register.Rbx
  ];

  // Internal calling convention: pass up to 8 float parameters in XMM registers
  private static readonly X86XmmRegister[] CallConvXmmRegs = [
    X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2, X86XmmRegister.Xmm3,
    X86XmmRegister.Xmm4, X86XmmRegister.Xmm5, X86XmmRegister.Xmm6, X86XmmRegister.Xmm7
  ];

  public static int RegisterParamCount => CallConvRegs.Length;

  // --- Abstract method implementations ---

  protected override X86Register[] GetGprPool() => _gprPool;
  protected override X86XmmRegister[] GetFpPool() => _xmmPool;
  protected override X86Register[] GetCallerSavedGprs() => _callerSavedRegisters;
  protected override X86XmmRegister[] GetCallerSavedFpRegs() => _callerSavedXmmRegisters;

  protected override bool SamePhysicalRegister(X86Register a, X86Register? b) {
    return b != null && a == b.Value;
  }

  protected override void EmitImmediateToRegister(X86Register gpr, long immediate, MlirBlock<X86Op> block) {
    if (immediate == 0) {
      block.AddOp(new X86XorRegRegOp(gpr, gpr));
    } else {
      block.AddOp(new X86MovRegImmOp(gpr, immediate));
    }
  }

  protected override void EmitMovGpr(X86Register dest, X86Register src, MlirBlock<X86Op> block) {
    block.AddOp(new X86MovRegRegOp(dest, src));
  }

  protected override void EmitSpillGprToStack(int offset, X86Register reg, MlirBlock<X86Op> block) {
    block.AddOp(new X86MovMemRegOp(offset, reg, 8));
  }

  protected override void EmitReloadGprFromStack(X86Register reg, int offset, MlirBlock<X86Op> block) {
    block.AddOp(new X86MovRegMemOp(reg, offset, 8));
  }

  protected override void EmitMovFp(X86XmmRegister dest, X86XmmRegister src, MlirBlock<X86Op> block) {
    block.AddOp(new X86MovXmmXmmOp(dest, src, FloatPrecision.F64));
  }

  protected override void EmitSpillFpToStack(int offset, X86XmmRegister reg, MlirBlock<X86Op> block) {
    block.AddOp(new X86MovMemXmmOp(offset, reg, FloatPrecision.F64));
  }

  protected override void EmitReloadFpFromStack(X86XmmRegister reg, int offset, MlirBlock<X86Op> block) {
    block.AddOp(new X86MovXmmMemOp(reg, offset, FloatPrecision.F64));
  }

  protected override X86Op MakeSpillGprOp(int offset, X86Register reg) {
    return new X86MovMemRegOp(offset, reg, 8);
  }

  protected override X86Op MakeSpillFpOp(int offset, X86XmmRegister reg) {
    return new X86MovMemXmmOp(offset, reg, FloatPrecision.F64);
  }

  protected override bool IsTerminator(X86Op op) {
    return op is X86JccOp or X86JmpOp or X86RetOp or X86EpilogueOp;
  }

  // --- X86-specific Emit methods ---

  /// <summary>
  /// Ensure a value is in a GPR and return the physical register.
  /// </summary>
  public X86Register LoadToRegister(StdValue value, MlirBlock<X86Op> block) {
    return EnsureInRegister(value, block);
  }

  /// <summary>
  /// Emit a two-operand register-register instruction (e.g. add, sub).
  /// </summary>
  public void EmitBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
    MlirBlock<X86Op> block, Func<X86Register, X86Register, X86Op> makeOp,
    bool lhsConsumed = false, bool useLeaForAdd = false) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    if (lhsConsumed) {
      TransferValue(lhs, lhsReg, result);
      block.AddOp(makeOp(lhsReg, rhsReg));
    } else {
      var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: rhsReg);
      if (resultReg != lhsReg) {
        if (useLeaForAdd) {
          block.AddOp(new X86LeaRegRegRegOp(resultReg, lhsReg, rhsReg));
          return;
        }
        block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
      }
      block.AddOp(makeOp(resultReg, rhsReg));
    }
  }

  /// <summary>
  /// Emit IMUL (integer multiplication).
  /// </summary>
  public void EmitMultiply(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block,
    bool lhsConsumed = false) {
    EmitBinaryRegReg(lhs, rhs, result, block, (l, r) => new X86ImulRegRegOp(l, r), lhsConsumed);
  }

  /// <summary>
  /// Emit a shift instruction (SHL/SHR/SAR). x86 shifts require the shift count in CL.
  /// </summary>
  public void EmitShift(StdValue lhs, StdValue rhs, StdValue result,
    MlirBlock<X86Op> block, Func<X86Register, X86Op> makeShiftOp) {
    var lhsReg = EnsureInRegister(lhs, block);
    var rhsReg = EnsureInRegister(rhs, block, protect1: lhsReg);

    if (lhsReg == X86Register.Rcx && rhsReg != X86Register.Rcx) {
      // lhs is in Rcx but we need Rcx for the shift count.
      // Copy lhs to a result register first, then spill lhs from Rcx
      // so it can be reloaded from the stack if needed again later.
      var resultReg = AllocateRegister(result, block, protect1: X86Register.Rcx, protect2: rhsReg);
      block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
      SpillRegisterIfOccupied(X86Register.Rcx, block);
      block.AddOp(new X86MovRegRegOp(X86Register.Rcx, rhsReg));
      block.AddOp(makeShiftOp(resultReg));
    } else {
      if (rhsReg != X86Register.Rcx) {
        SpillRegisterIfOccupied(X86Register.Rcx, block);
        block.AddOp(new X86MovRegRegOp(X86Register.Rcx, rhsReg));
      }
      var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: X86Register.Rcx);
      if (resultReg != lhsReg) {
        block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
      }
      block.AddOp(makeShiftOp(resultReg));
    }
  }

  // --- Division ---

  public void EmitDivision(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation(lhs, rhs, result, X86Register.Rax, block);
  }

  public void EmitRemainder(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation(lhs, rhs, result, X86Register.Rdx, block);
  }

  public void EmitUnsignedDivision(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation(lhs, rhs, result, X86Register.Rax, block);
  }

  public void EmitUnsignedRemainder(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation(lhs, rhs, result, X86Register.Rdx, block);
  }

  private X86Register PrepareDivisionRegisters(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);

    if (rhsReg == X86Register.Rax || rhsReg == X86Register.Rdx) {
      var safeReg = FindSafeRegisterForIdiv(lhsReg, rhsReg);
      SpillRegisterIfOccupied(safeReg, block);
      block.AddOp(new X86MovRegRegOp(safeReg, rhsReg));
      rhsReg = safeReg;
    }

    SpillRegisterIfOccupied(X86Register.Rax, block);
    SpillRegisterIfOccupied(X86Register.Rdx, block);

    if (lhsReg != X86Register.Rax) {
      block.AddOp(new X86MovRegRegOp(X86Register.Rax, lhsReg));
    }

    return rhsReg;
  }

  private void EmitIdivOperation(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    block.AddOp(new X86CqoOp());
    block.AddOp(new X86IdivRegOp(rhsReg));
    Assign(resultRegister, result);
  }

  // --- I32 ↔ I64 width conversion ---

  public void EmitSignExtendI32ToI64(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    block.AddOp(new X86MovsxdOp(destReg, srcReg));
  }

  public void EmitZeroExtendI32ToI64(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    block.AddOp(new X86MovRegRegOp(To32Bit(destReg), srcReg));
  }

  public void EmitTruncI64ToI32(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    block.AddOp(new X86MovRegRegOp(To32Bit(destReg), To32Bit(srcReg)));
  }

  // --- 32-bit division variants ---

  public void EmitDivision32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation32(lhs, rhs, result, X86Register.Rax, block);
  }

  public void EmitRemainder32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation32(lhs, rhs, result, X86Register.Rdx, block);
  }

  public void EmitUnsignedDivision32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation32(lhs, rhs, result, X86Register.Rax, block);
  }

  public void EmitUnsignedRemainder32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation32(lhs, rhs, result, X86Register.Rdx, block);
  }

  private void EmitIdivOperation32(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    block.AddOp(new X86CdqOp());
    block.AddOp(new X86IdivReg32Op(rhsReg));
    Assign(resultRegister, result);
  }

  private void EmitDivOperation32(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    block.AddOp(new X86XorRegRegOp(X86Register.Rdx, X86Register.Rdx));
    block.AddOp(new X86DivReg32Op(rhsReg));
    Assign(resultRegister, result);
  }

  private void EmitDivOperation(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    block.AddOp(new X86XorRegRegOp(X86Register.Rdx, X86Register.Rdx));
    block.AddOp(new X86DivRegOp(rhsReg));
    Assign(resultRegister, result);
  }

  private X86Register FindSafeRegisterForIdiv(X86Register lhsReg, X86Register divisorReg) {
    X86Register? fallback = null;
    foreach (var reg in GprPool) {
      if (reg == X86Register.Rax || reg == X86Register.Rdx) continue;
      if (reg == lhsReg || reg == divisorReg) continue;
      if (!_registerContents.ContainsKey(reg))
        return reg;
      fallback ??= reg;
    }
    if (fallback != null)
      return fallback.Value;
    throw new InvalidOperationException("RegisterManager: no safe register for IDIV divisor relocation");
  }

  // --- Store/Load to/from stack ---

  public void EmitStoreToStack(StdValue value, int offset, int sizeInBytes, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(value, block);
    block.AddOp(new X86MovMemRegOp(offset, srcReg, sizeInBytes));
    NoteStoreToStack(value, offset);
  }

  public void EmitLoadFromStack(StdValue result, int offset, int sizeInBytes, MlirBlock<X86Op> block) {
    if (sizeInBytes == 8)
      _valueStackHome[result] = offset;
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86MovRegMemOp(gpr, offset, sizeInBytes));
  }

  public void EmitXmmStoreToStack(StdValue value, int offset, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(value, block);
    block.AddOp(new X86MovMemXmmOp(offset, srcXmm, FloatPrecision.F64));
    NoteFpStoreToStack(value, offset);
  }

  public void EmitXmmLoadFromStack(StdValue result, int offset, MlirBlock<X86Op> block) {
    var xmmReg = AllocateFpRegister(result);
    block.AddOp(new X86MovXmmMemOp(xmmReg, offset, FloatPrecision.F64));
  }

  public void EmitXmmLoadFromRipRelative(StdValue result, string rdataLabel, MlirBlock<X86Op> block) {
    var xmmReg = AllocateFpRegister(result);
    block.AddOp(new X86MovXmmRipRelOp(xmmReg, rdataLabel, FloatPrecision.F64));
  }

  // --- F32 (single-precision) XMM stack/rip-relative operations ---

  public void EmitXmmStoreToStackF32(StdValue value, int offset, MlirBlock<X86Op> block) {
    var xmm = EnsureInFpRegister(value, block);
    block.AddOp(new X86MovMemXmmOp(offset, xmm, FloatPrecision.F32));
  }

  public void EmitXmmLoadFromStackF32(StdValue result, int offset, MlirBlock<X86Op> block) {
    var xmm = AllocateFpRegister(result);
    block.AddOp(new X86MovXmmMemOp(xmm, offset, FloatPrecision.F32));
  }

  public void EmitXmmLoadFromRipRelativeF32(StdValue result, string rdataLabel, MlirBlock<X86Op> block) {
    var xmm = AllocateFpRegister(result);
    block.AddOp(new X86MovXmmRipRelOp(xmm, rdataLabel, FloatPrecision.F32));
  }

  // --- Comparisons ---

  public void EmitIntegerCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    block.AddOp(new X86CmpRegRegOp(lhsReg, rhsReg));
  }

  public void EmitBoolTest(StdValue value, MlirBlock<X86Op> block) {
    var reg = EnsureInRegister(value, block);
    block.AddOp(new X86TestRegRegOp(reg, reg));
  }

  public void EmitSelectI64(StdValue condition, StdValue trueValue, StdValue falseValue, StdValue result, MlirBlock<X86Op> block) {
    var trueReg = EnsureInRegister(trueValue, block);
    var falseReg = EnsureInRegister(falseValue, block, protect1: trueReg);
    var condReg = EnsureInRegister(condition, block, protect1: trueReg, protect2: falseReg);

    block.AddOp(new X86TestRegRegOp(condReg, condReg));

    var resultReg = AllocateRegister(result, block, protect1: trueReg, protect2: condReg);
    block.AddOp(new X86MovRegRegOp(resultReg, falseReg));
    block.AddOp(new X86CmovneRegRegOp(resultReg, trueReg));
  }

  public void EmitSetcc(StdValue result, string condition, MlirBlock<X86Op> block) {
    var reg = AllocateRegister(result, block);
    block.AddOp(new X86SetccOp(condition, reg));
    block.AddOp(new X86MovzxRegOp(reg));
  }

  public void EmitFloatSetcc(StdValue result, string predicate, MlirBlock<X86Op> block) {
    var reg = AllocateRegister(result, block);
    switch (predicate) {
      case "eq":
        EmitCompoundSetcc(reg, "e", "np", (a, b) => new X86AndRegRegOp(a, b), block);
        break;
      case "ne":
        EmitCompoundSetcc(reg, "ne", "p", (a, b) => new X86OrRegRegOp(a, b), block);
        break;
      case "gt":
        block.AddOp(new X86SetccOp("a", reg));
        block.AddOp(new X86MovzxRegOp(reg));
        break;
      case "ge":
        block.AddOp(new X86SetccOp("ae", reg));
        block.AddOp(new X86MovzxRegOp(reg));
        break;
      case "lt":
        block.AddOp(new X86SetccOp("b", reg));
        block.AddOp(new X86MovzxRegOp(reg));
        break;
      case "le":
        block.AddOp(new X86SetccOp("be", reg));
        block.AddOp(new X86MovzxRegOp(reg));
        break;
      case var unknown:
        throw new InvalidOperationException($"Unknown float comparison predicate for setcc: {unknown}");
    }
  }

  private void EmitCompoundSetcc(X86Register reg, string cond1, string cond2,
      Func<X86Register, X86Register, X86Op> combine, MlirBlock<X86Op> block) {
    block.AddOp(new X86SetccOp(cond1, reg));
    block.AddOp(new X86MovzxRegOp(reg));
    var scratch = new StdBool(_scratchIdCounter--);
    var tmpReg = AllocateRegister(scratch, block, protect1: reg);
    block.AddOp(new X86SetccOp(cond2, tmpReg));
    block.AddOp(new X86MovzxRegOp(tmpReg));
    block.AddOp(combine(reg, tmpReg));
    _valueToRegister.Remove(scratch);
    _registerContents.Remove(tmpReg);
    _lastUsed.Remove(tmpReg);
  }

  // --- XMM binary/unary operations ---

  public void EmitXmmBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
    MlirBlock<X86Op> block, Func<X86XmmRegister, X86XmmRegister, X86Op> makeOp) {
    var rhsXmm = EnsureInFpRegister(rhs, block);
    var lhsXmm = EnsureInFpRegister(lhs, block);
    var resultXmm = AllocateFpRegister(result);
    if (resultXmm != lhsXmm) {
      block.AddOp(new X86MovXmmXmmOp(resultXmm, lhsXmm, FloatPrecision.F64));
    }
    block.AddOp(makeOp(resultXmm, rhsXmm));
  }

  public void EmitCvttSd2Si(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new X86CvttFloat2SiOp(destGpr, srcXmm, FloatPrecision.F64));
  }

  public void EmitMovqXmmToGpr(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new X86MovqXmmToGprOp(destGpr, srcXmm));
  }

  public void EmitCvtSi2Sd(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcGpr = EnsureInRegister(input, block);
    var destXmm = AllocateFpRegister(result);
    block.AddOp(new X86CvtSi2FloatOp(destXmm, srcGpr, FloatPrecision.F64));
  }

  // --- F32 conversion operations ---

  public void EmitCvttSs2Si(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new X86CvttFloat2SiOp(destGpr, srcXmm, FloatPrecision.F32));
  }

  public void EmitCvtSi2Ss(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcGpr = EnsureInRegister(input, block);
    var destXmm = AllocateFpRegister(result);
    block.AddOp(new X86CvtSi2FloatOp(destXmm, srcGpr, FloatPrecision.F32));
  }

  public void EmitCvtSd2Ss(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var destXmm = AllocateFpRegister(result);
    block.AddOp(new X86CvtSd2SsOp(destXmm, srcXmm));
  }

  public void EmitCvtSs2Sd(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var destXmm = AllocateFpRegister(result);
    block.AddOp(new X86CvtSs2SdOp(destXmm, srcXmm));
  }

  public void EmitXmmUnaryRegReg(StdValue input, StdValue result, MlirBlock<X86Op> block,
    Func<X86XmmRegister, X86XmmRegister, X86Op> makeOp) {
    var srcXmm = EnsureInFpRegister(input, block);
    var resultXmm = AllocateFpRegister(result);
    block.AddOp(makeOp(resultXmm, srcXmm));
  }

  public void EmitAbsF64(StdValue input, StdValue result, string maskLabel, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var resultXmm = AllocateFpRegister(result);
    if (resultXmm != srcXmm) {
      block.AddOp(new X86MovXmmXmmOp(resultXmm, srcXmm, FloatPrecision.F64));
    }
    block.AddOp(new X86AndMaskRipRelOp(resultXmm, maskLabel, FloatPrecision.F64));
  }

  public void EmitAbsF32(StdValue input, StdValue result, string maskLabel, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(input, block);
    var resultXmm = AllocateFpRegister(result);
    if (resultXmm != srcXmm) {
      block.AddOp(new X86MovXmmXmmOp(resultXmm, srcXmm, FloatPrecision.F32));
    }
    block.AddOp(new X86AndMaskRipRelOp(resultXmm, maskLabel, FloatPrecision.F32));
  }

  public void EmitXmmCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var rhsReg = EnsureInFpRegister(rhs, block);
    var lhsReg = EnsureInFpRegister(lhs, block);
    block.AddOp(new X86UcomisXmmOp(lhsReg, rhsReg, FloatPrecision.F64));
  }

  public void EmitXmmCompareF32(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var lhsXmm = EnsureInFpRegister(lhs, block);
    var rhsXmm = EnsureInFpRegister(rhs, block);
    block.AddOp(new X86UcomisXmmOp(lhsXmm, rhsXmm, FloatPrecision.F32));
  }

  // --- Specific register placement ---

  public void EnsureInSpecificRegister(StdValue value, X86Register target, MlirBlock<X86Op> block) {
    _registerHints[value] = target;
    var reg = EnsureInRegister(value, block);
    if (reg != target) {
      block.AddOp(new X86MovRegRegOp(target, reg));
    }
  }

  public void EnsureInXmm0ForReturn(StdValue value, MlirBlock<X86Op> block) {
    var xmmReg = EnsureInFpRegister(value, block);
    if (xmmReg != X86XmmRegister.Xmm0) {
      block.AddOp(new X86MovXmmXmmOp(X86XmmRegister.Xmm0, xmmReg, FloatPrecision.F64));
    }
  }

  // --- Call emission ---

  private void EmitCallShared(
      List<StdValue> args,
      StdValue? result,
      MlirBlock<X86Op> block,
      Action? preGprPlacement,
      Func<X86Op> emitCallOp,
      HashSet<StdValue>? consumedByCall = null) {
    int regArgCount = Math.Min(args.Count, CallConvRegs.Length);
    int stackArgCount = args.Count - regArgCount;

    SpillCallerSavedRegisters(block, consumedByCall);

    int stackArgBytes = stackArgCount > 0 ? ((stackArgCount * 8 + 15) & ~15) : 0;
    if (stackArgBytes > 0)
      block.AddOp(new X86SubRegImmOp(X86Register.Rsp, stackArgBytes));

    for (int i = CallConvRegs.Length; i < args.Count; i++) {
      var argReg = EnsureInRegister(args[i], block);
      int offset = (i - CallConvRegs.Length) * 8;
      block.AddOp(new X86MovMemRspRegOp(offset, argReg));
    }

    var gprArgs = new List<int>();
    var xmmArgs = new List<int>();
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32)
        xmmArgs.Add(i);
      else
        gprArgs.Add(i);
    }

    var xmmSources = new X86XmmRegister?[regArgCount];
    foreach (int i in xmmArgs)
      xmmSources[i] = EnsureInFpRegister(args[i], block);

    PlaceXmmArgs(xmmArgs, xmmSources, regArgCount, block);

    var argSources = new X86Register?[regArgCount];
    int?[] argStackHomes = new int?[regArgCount];
    long?[] argConstants = new long?[regArgCount];
    foreach (int i in gprArgs) {
      var target = CallConvRegs[i];
      if (_valueToRegister.TryGetValue(args[i], out var reg)) {
        if (reg != target && _valueStackHome.TryGetValue(args[i], out var disp))
          argStackHomes[i] = disp;
        else
          argSources[i] = reg;
      } else if (_constantValues.TryGetValue(args[i], out var imm)) {
        argConstants[i] = imm;
      } else if (_valueStackHome.TryGetValue(args[i], out var disp)) {
        argStackHomes[i] = disp;
      } else {
        throw new InvalidOperationException($"RegisterManager: call arg %{args[i].Id} has no register, no constant, and no stack home");
      }
    }

    preGprPlacement?.Invoke();

    var placed = new bool[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32) placed[i] = true;
    }

    var targetRegs = new X86Register[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      targetRegs[i] = CallConvRegs[i];
    }

    foreach (int i in gprArgs) {
      if (argConstants[i] != null)
        continue;
      if (SamePhysicalRegister(argSources[i], targetRegs[i]))
        placed[i] = true;
    }

    bool progress = true;
    while (progress) {
      progress = false;
      foreach (int i in gprArgs) {
        if (placed[i] || argConstants[i] != null) continue;
        bool targetBlocked = false;
        foreach (int j in gprArgs) {
          if (j != i && !placed[j] && argConstants[j] == null
              && SamePhysicalRegister(argSources[j], targetRegs[i])) {
            targetBlocked = true;
            break;
          }
        }
        if (!targetBlocked) {
          if (argSources[i] is { } srcReg)
            block.AddOp(new X86MovRegRegOp(targetRegs[i], srcReg));
          else
            block.AddOp(new X86MovRegMemOp(targetRegs[i], argStackHomes[i]!.Value, 8));
          placed[i] = true;
          progress = true;
        }
      }
    }

    foreach (int i in gprArgs) {
      if (placed[i] || argConstants[i] != null) continue;
      if (SamePhysicalRegister(argSources[i], targetRegs[i])) {
        placed[i] = true;
        continue;
      }
      block.AddOp(new X86XchgRegRegOp(argSources[i]!.Value, targetRegs[i]));
      foreach (int j in gprArgs) {
        if (j != i && !placed[j] && argConstants[j] == null
            && SamePhysicalRegister(argSources[j], targetRegs[i])) {
          argSources[j] = argSources[i];
          break;
        }
      }
      placed[i] = true;
    }

    foreach (int i in gprArgs) {
      if (placed[i]) continue;
      if (argConstants[i] is { } constVal) {
        EmitImmediateToRegister(targetRegs[i], constVal, block);
        placed[i] = true;
      }
    }

    block.AddOp(emitCallOp());

    if (stackArgBytes > 0)
      block.AddOp(new X86AddRegImmOp(X86Register.Rsp, stackArgBytes));

    InvalidateCallerSavedRegisters();

    if (result != null) {
      if (result is StdF64 or StdF32) {
        AssignFp(X86XmmRegister.Xmm0, result);
      } else if (result is StdPtr or StdI64 or StdI32 or StdBool) {
        Assign(X86Register.Rax, result);
      } else {
        throw new InvalidOperationException($"RegisterManager: unsupported result type {result.GetType().Name}");
      }
    }
  }

  public void EmitCall(string callee, List<StdValue> args, StdValue? result, MlirBlock<X86Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    EmitCallShared(
      args, result, block,
      preGprPlacement: null,
      emitCallOp: () => new X86CallDirectOp(callee),
      consumedByCall: consumedByCall
    );
  }

  public void EmitTailCall(string callee, List<StdValue> args, MlirBlock<X86Op> block) {
    int regArgCount = Math.Min(args.Count, CallConvRegs.Length);

    foreach (var arg in args) {
      if (arg is StdF64 or StdF32)
        EnsureInFpRegister(arg, block);
      else
        EnsureInRegister(arg, block);
    }

    var gprArgs = new List<int>();
    var xmmArgs = new List<int>();
    var argSources = new X86Register?[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32) {
        xmmArgs.Add(i);
      } else {
        gprArgs.Add(i);
        argSources[i] = _valueToRegister[args[i]];
      }
    }

    var xmmSources = new X86XmmRegister?[regArgCount];
    foreach (int i in xmmArgs)
      xmmSources[i] = _valueToFp[args[i]];

    PlaceXmmArgs(xmmArgs, xmmSources, regArgCount, block);

    var placed = new bool[regArgCount];
    foreach (int i in xmmArgs)
      placed[i] = true;

    var targetRegs = new X86Register[regArgCount];
    for (int i = 0; i < regArgCount; i++)
      targetRegs[i] = CallConvRegs[i];

    foreach (int i in gprArgs) {
      if (SamePhysicalRegister(argSources[i], targetRegs[i]))
        placed[i] = true;
    }

    bool progress = true;
    while (progress) {
      progress = false;
      foreach (int i in gprArgs) {
        if (placed[i]) continue;
        bool targetBlocked = false;
        foreach (int j in gprArgs) {
          if (j != i && !placed[j] && SamePhysicalRegister(argSources[j], targetRegs[i])) {
            targetBlocked = true;
            break;
          }
        }
        if (!targetBlocked) {
          block.AddOp(new X86MovRegRegOp(targetRegs[i], argSources[i]!.Value));
          placed[i] = true;
          progress = true;
        }
      }
    }

    foreach (int i in gprArgs) {
      if (placed[i]) continue;
      if (SamePhysicalRegister(argSources[i], targetRegs[i])) {
        placed[i] = true;
        continue;
      }
      block.AddOp(new X86XchgRegRegOp(argSources[i]!.Value, targetRegs[i]));
      foreach (int j in gprArgs) {
        if (j != i && !placed[j] && SamePhysicalRegister(argSources[j], targetRegs[i])) {
          argSources[j] = argSources[i];
          break;
        }
      }
      placed[i] = true;
    }

    block.AddOp(new X86EpilogueOp());
    block.AddOp(new X86JmpOp(callee));
  }

  public void EmitTryCall(string callee, List<StdValue> args, StdValue? result, StdValue errorFlag, MlirBlock<X86Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    EmitCall(callee, args, result, block, consumedByCall);
    Assign(X86Register.Rdx, errorFlag);
  }

  public void EmitFuncRef(string functionName, StdValue result, MlirBlock<X86Op> block) {
    var reg = AllocateRegister(result, block);
    block.AddOp(new X86LeaFuncAddrOp(reg, functionName));
  }

  public void EmitIndirectCall(StdValue callee, List<StdValue> args, StdValue? result, MlirBlock<X86Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    X86Register calleeReg = default;
    EmitCallShared(
      args, result, block,
      preGprPlacement: () => {
        calleeReg = EnsureInRegister(callee, block);
        if (Array.Exists(CallConvRegs, r => SamePhysicalRegister(r, calleeReg))) {
          block.AddOp(new X86MovRegRegOp(X86Register.R10, calleeReg));
          calleeReg = X86Register.R10;
        }
      },
      emitCallOp: () => new X86CallIndirectOp(calleeReg),
      consumedByCall: consumedByCall
    );
  }

  // --- Parameters ---

  public void NoteParam(StdValue paramValue, int paramIndex, MlirBlock<X86Op> block) {
    if (paramIndex < CallConvRegs.Length) {
      if (paramValue is StdF64 or StdF32) {
        AssignFp(CallConvXmmRegs[paramIndex], paramValue);
      } else if (paramValue is StdPtr or StdI64) {
        Assign(CallConvRegs[paramIndex], paramValue);
      } else if (paramValue is StdI32) {
        Assign(CallConvRegs[paramIndex], paramValue);
      } else if (paramValue is StdBool) {
        Assign(CallConvRegs[paramIndex], paramValue);
      } else {
        throw new InvalidOperationException($"RegisterManager: unsupported parameter type {paramValue.GetType().Name}");
      }
    } else {
      int stackOffset = 16 + (paramIndex - CallConvRegs.Length) * 8;
      if (paramValue is StdF64 or StdF32) {
        var xmm = AllocateFpRegister(paramValue);
        block.AddOp(new X86MovXmmMemOp(xmm, stackOffset, FloatPrecision.F64));
      } else if (paramValue is StdPtr or StdI64) {
        var gpr = AllocateRegister(paramValue, block);
        block.AddOp(new X86MovRegMemOp(gpr, stackOffset, sizeInBytes: 8));
      } else if (paramValue is StdI32) {
        var gpr = AllocateRegister(paramValue, block);
        block.AddOp(new X86MovRegMemOp(gpr, stackOffset, sizeInBytes: 4));
      } else if (paramValue is StdBool) {
        var gpr = AllocateRegister(paramValue, block);
        block.AddOp(new X86MovRegMemOp(gpr, stackOffset, sizeInBytes: 1));
      } else {
        throw new InvalidOperationException($"RegisterManager: unsupported parameter type {paramValue.GetType().Name}");
      }
    }
  }

  // --- Caller-saved spilling (X86-specific because it emits X86 ops directly) ---

  private void SpillCallerSavedRegisters(MlirBlock<X86Op> block, HashSet<StdValue>? consumedByCall = null) {
    foreach (var reg in _callerSavedRegisters) {
      if (_registerContents.TryGetValue(reg, out var value)
        && !_valueStackHome.ContainsKey(value)
        && !_constantValues.ContainsKey(value)
        && !(consumedByCall != null && consumedByCall.Contains(value))
        && !_allSyntheticValues.Contains(value)) {
        _nextSpillOffset -= 8;
        block.AddOp(new X86MovMemRegOp(_nextSpillOffset, reg, 8));
        _valueStackHome[value] = _nextSpillOffset;
      }
    }
    foreach (var xmm in _callerSavedXmmRegisters) {
      if (_fpContents.TryGetValue(xmm, out var value)
        && !_valueFpStackHome.ContainsKey(value)
        && !(consumedByCall != null && consumedByCall.Contains(value))) {
        _nextSpillOffset -= 8;
        block.AddOp(new X86MovMemXmmOp(_nextSpillOffset, xmm, FloatPrecision.F64));
        _valueFpStackHome[value] = _nextSpillOffset;
      }
    }
  }

  // --- XMM arg placement ---

  private static void PlaceXmmArgs(List<int> xmmArgs, X86XmmRegister?[] xmmSources, int regArgCount, MlirBlock<X86Op> block) {
    if (xmmArgs.Count == 0) return;

    var placed = new bool[regArgCount];

    foreach (int i in xmmArgs) {
      if (xmmSources[i] == CallConvXmmRegs[i])
        placed[i] = true;
    }

    bool progress = true;
    while (progress) {
      progress = false;
      foreach (int i in xmmArgs) {
        if (placed[i]) continue;
        bool targetBlocked = false;
        foreach (int j in xmmArgs) {
          if (j != i && !placed[j] && xmmSources[j] == CallConvXmmRegs[i]) {
            targetBlocked = true;
            break;
          }
        }
        if (!targetBlocked) {
          block.AddOp(new X86MovXmmXmmOp(CallConvXmmRegs[i], xmmSources[i]!.Value, FloatPrecision.F64));
          placed[i] = true;
          progress = true;
        }
      }
    }

    foreach (int i in xmmArgs) {
      if (placed[i]) continue;
      if (xmmSources[i] == CallConvXmmRegs[i]) {
        placed[i] = true;
        continue;
      }
      var temp = X86XmmRegister.Xmm15;
      block.AddOp(new X86MovXmmXmmOp(temp, xmmSources[i]!.Value, FloatPrecision.F64));
      block.AddOp(new X86MovXmmXmmOp(xmmSources[i]!.Value, CallConvXmmRegs[i], FloatPrecision.F64));
      block.AddOp(new X86MovXmmXmmOp(CallConvXmmRegs[i], temp, FloatPrecision.F64));
      foreach (int j in xmmArgs) {
        if (j != i && !placed[j] && xmmSources[j] == CallConvXmmRegs[i]) {
          xmmSources[j] = xmmSources[i]!.Value;
          break;
        }
      }
      placed[i] = true;
    }
  }

  // --- SamePhysicalRegister with nullable (used by call arg placement) ---

  private static bool SamePhysicalRegister(X86Register? a, X86Register? b) {
    return a != null && b != null && a.Value == b.Value;
  }

  // --- Struct support ---

  public void EmitLeaFromStack(StdPtr result, int offset, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaRegMemOp(gpr, offset));
  }

  public void EmitLeaRipRelative(StdPtr result, string rdataLabel, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaRipRelOp(gpr, rdataLabel));
  }

  public void EmitLeaSymdataRelative(StdPtr result, string symdataLabel, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaSymdataRelOp(gpr, symdataLabel));
  }

  public void EmitLeaUcddataRelative(StdPtr result, string ucddataLabel, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaUcddataRelOp(gpr, ucddataLabel));
  }

  public void EmitMovValueToValue(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var dstReg = AllocateRegister(result, block, protect1: srcReg);
    if (srcReg != dstReg)
      block.AddOp(new X86MovRegRegOp(dstReg, srcReg));
  }

  public void EmitMemCopy(StdValue srcPtr, StdValue dstPtr, StdValue byteCount, MlirBlock<X86Op> block) {
    var memCopyArgs = new HashSet<StdValue> { srcPtr, dstPtr, byteCount };
    SpillRegisterIfNotArg(X86Register.Rsi, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Rdi, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Rcx, memCopyArgs, block);

    var srcReg = EnsureInRegister(srcPtr, block);
    var dstReg = EnsureInRegister(dstPtr, block, protect1: srcReg);
    var cntReg = EnsureInRegister(byteCount, block, protect1: srcReg, protect2: dstReg);

    var moves = new List<(X86Register target, X86Register source)>();
    if (!SamePhysReg(srcReg, X86Register.Rsi))
      moves.Add((X86Register.Rsi, srcReg));
    if (!SamePhysReg(dstReg, X86Register.Rdi))
      moves.Add((X86Register.Rdi, dstReg));
    if (!SamePhysReg(cntReg, X86Register.Rcx))
      moves.Add((X86Register.Rcx, cntReg));

    var emitted = new HashSet<int>();
    for (int pass = 0; pass < moves.Count + 1 && emitted.Count < moves.Count; pass++) {
      for (int i = 0; i < moves.Count; i++) {
        if (emitted.Contains(i)) continue;
        bool conflicts = false;
        for (int j = 0; j < moves.Count; j++) {
          if (j == i || emitted.Contains(j)) continue;
          if (SamePhysReg(moves[i].target, moves[j].source)) {
            conflicts = true;
            break;
          }
        }
        if (!conflicts) {
          block.AddOp(new X86MovRegRegOp(moves[i].target, moves[i].source));
          emitted.Add(i);
        }
      }
    }
    if (emitted.Count < moves.Count) {
      var remaining = Enumerable.Range(0, moves.Count).Except(emitted).ToList();
      var spillOffsets = new int[moves.Count];
      foreach (var i in remaining) {
        _nextSpillOffset -= 8;
        spillOffsets[i] = _nextSpillOffset;
        block.AddOp(new X86MovMemRegOp(_nextSpillOffset, moves[i].source, 8));
      }
      foreach (var i in remaining) {
        block.AddOp(new X86MovRegMemOp(moves[i].target, spillOffsets[i], 8));
        emitted.Add(i);
      }
    }

    block.AddOp(new X86RepMovsbOp());
    InvalidateGpr(X86Register.Rsi);
    InvalidateGpr(X86Register.Rdi);
    InvalidateGpr(X86Register.Rcx);
  }

  public void EmitMemCopyReverse(StdValue srcPtr, StdValue dstPtr, StdValue byteCount, MlirBlock<X86Op> block) {
    // Backward memcopy for overlapping shift-right.
    // Setup: RSI=src+count-1, RDI=dst+count-1, RCX=count
    // Then: STD; REP MOVSB; CLD
    //
    // Strategy: spill src/dst/count to stack, then load into RSI/RDI/RCX
    // with adjustments to avoid register conflicts.
    var memCopyArgs = new HashSet<StdValue> { srcPtr, dstPtr, byteCount };
    SpillRegisterIfNotArg(X86Register.Rsi, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Rdi, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Rcx, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Rax, memCopyArgs, block);

    // Get all three values into registers (avoiding conflicts)
    var srcReg = EnsureInRegister(srcPtr, block);
    var dstReg = EnsureInRegister(dstPtr, block, protect1: srcReg);
    var cntReg = EnsureInRegister(byteCount, block, protect1: srcReg, protect2: dstReg);

    // Spill all three to dedicated stack slots so we can freely clobber registers
    _nextSpillOffset -= 8;
    int srcSlot = _nextSpillOffset;
    block.AddOp(new X86MovMemRegOp(srcSlot, srcReg, 8));

    _nextSpillOffset -= 8;
    int dstSlot = _nextSpillOffset;
    block.AddOp(new X86MovMemRegOp(dstSlot, dstReg, 8));

    _nextSpillOffset -= 8;
    int cntSlot = _nextSpillOffset;
    block.AddOp(new X86MovMemRegOp(cntSlot, cntReg, 8));

    // RCX = count
    block.AddOp(new X86MovRegMemOp(X86Register.Rcx, cntSlot, 8));
    // RSI = src + count - 1
    block.AddOp(new X86MovRegMemOp(X86Register.Rsi, srcSlot, 8));
    block.AddOp(new X86AddRegRegOp(X86Register.Rsi, X86Register.Rcx));
    block.AddOp(new X86SubRegImmOp(X86Register.Rsi, 1));
    // RDI = dst + count - 1
    block.AddOp(new X86MovRegMemOp(X86Register.Rdi, dstSlot, 8));
    block.AddOp(new X86AddRegRegOp(X86Register.Rdi, X86Register.Rcx));
    block.AddOp(new X86SubRegImmOp(X86Register.Rdi, 1));

    // STD; REP MOVSB; CLD
    block.AddOp(new X86StdOp());
    block.AddOp(new X86RepMovsbOp());
    block.AddOp(new X86CldOp());
    InvalidateGpr(X86Register.Rsi);
    InvalidateGpr(X86Register.Rdi);
    InvalidateGpr(X86Register.Rcx);
    InvalidateGpr(X86Register.Rax);
  }

  public void EmitBulkZero(int baseOffset, int qwordCount, MlirBlock<X86Op> block) {
    SpillRegisterIfOccupied(X86Register.Rax, block);
    SpillRegisterIfOccupied(X86Register.Rdi, block);
    SpillRegisterIfOccupied(X86Register.Rcx, block);
    block.AddOp(new X86LeaRegMemOp(X86Register.Rdi, baseOffset));
    block.AddOp(new X86XorRegRegOp(X86Register.Rax, X86Register.Rax));
    block.AddOp(new X86MovRegImmOp(X86Register.Rcx, qwordCount));
    block.AddOp(new X86RepStosqOp());
    InvalidateGpr(X86Register.Rax);
    InvalidateGpr(X86Register.Rdi);
    InvalidateGpr(X86Register.Rcx);
  }

  private static bool SamePhysReg(X86Register a, X86Register b) {
    return a == b;
  }

  private void SpillRegisterIfNotArg(X86Register reg, HashSet<StdValue> args, MlirBlock<X86Op> block) {
    if (_registerContents.TryGetValue(reg, out var value) && !args.Contains(value)) {
      SpillRegisterIfOccupied(reg, block);
    }
  }

  // --- Indirect memory operations ---

  public void EmitStoreIndirect(StdValue value, StdValue basePtr, int fieldOffset, MlirType fieldType, MlirBlock<X86Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    if (fieldType == MlirType.F64) {
      var srcXmm = EnsureInFpRegister(value, block);
      block.AddOp(new X86MovIndirectMemXmmOp(baseReg, fieldOffset, srcXmm, FloatPrecision.F64));
    } else if (fieldType == MlirType.I1 || fieldType == MlirType.I8) {
      var srcReg = EnsureInRegister(value, block, protect1: baseReg);
      block.AddOp(new X86MovByteIndirectRegOp(baseReg, fieldOffset, srcReg));
    } else if (fieldType == MlirType.I16 || fieldType == MlirType.U16) {
      var srcReg = EnsureInRegister(value, block, protect1: baseReg);
      block.AddOp(new X86MovWordIndirectRegOp(baseReg, fieldOffset, srcReg));
    } else if (fieldType == MlirType.I64 || fieldType == MlirType.Fn || fieldType is MlirUnionType || fieldType is MlirStructType) {
      var srcReg = EnsureInRegister(value, block, protect1: baseReg);
      block.AddOp(new X86MovIndirectMemRegOp(baseReg, fieldOffset, srcReg));
    } else {
      throw new InvalidOperationException($"EmitStoreIndirect: unhandled field type: {fieldType}");
    }
  }

  public void EmitLoadIndirect(StdValue result, StdValue basePtr, int fieldOffset, MlirType fieldType, MlirBlock<X86Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    if (fieldType == MlirType.F64) {
      var destXmm = AllocateFpRegister(result);
      block.AddOp(new X86MovXmmIndirectMemOp(destXmm, baseReg, fieldOffset, FloatPrecision.F64));
    } else if (fieldType == MlirType.I1 || fieldType == MlirType.I8) {
      var destGpr = AllocateRegister(result, block, protect1: baseReg);
      block.AddOp(new X86MovzxRegByteIndirectOp(destGpr, baseReg, fieldOffset));
    } else if (fieldType == MlirType.I16 || fieldType == MlirType.U16) {
      var destGpr = AllocateRegister(result, block, protect1: baseReg);
      block.AddOp(new X86MovzxRegWordIndirectOp(destGpr, baseReg, fieldOffset));
    } else if (fieldType == MlirType.I64 || fieldType == MlirType.Fn || fieldType is MlirUnionType || fieldType is MlirStructType) {
      var destGpr = AllocateRegister(result, block, protect1: baseReg);
      block.AddOp(new X86MovRegIndirectMemOp(destGpr, baseReg, fieldOffset));
    } else {
      throw new InvalidOperationException($"EmitLoadIndirect: unhandled field type: {fieldType}");
    }
  }

  public void EmitNullSafeLoadI64(StdI64 result, StdI64 basePtr, int fieldOffset, MlirBlock<X86Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    var destReg = AllocateRegister(result, block, protect1: baseReg);
    block.AddOp(new X86XorRegRegOp(destReg, destReg));
    block.AddOp(new X86TestRegRegOp(baseReg, baseReg));
    var skipLabel = $"__nsload_{StandardToX86Conversion.NextLabelId()}";
    block.AddOp(new X86JccOp("z", skipLabel));
    block.AddOp(new X86MovRegIndirectMemOp(destReg, baseReg, fieldOffset));
    block.AddOp(new X86LabelDefOp(skipLabel));
  }

  // --- Global variable support ---

  public void EmitGlobalLoad(StdValue result, string globalName, MlirBlock<X86Op> block, int size = 8) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86GlobalLoadOp(globalName, gpr, size));
  }

  public void EmitGlobalStore(StdValue value, string globalName, MlirBlock<X86Op> block, int size = 8) {
    var reg = EnsureInRegister(value, block);
    block.AddOp(new X86GlobalStoreOp(globalName, reg, size));
  }

  public void EmitXmmGlobalLoad(StdValue result, string globalName, MlirBlock<X86Op> block) {
    var xmmReg = AllocateFpRegister(result);
    block.AddOp(new X86GlobalLoadXmmOp(globalName, xmmReg, FloatPrecision.F64));
  }

  public void EmitXmmGlobalStore(StdValue value, string globalName, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInFpRegister(value, block);
    block.AddOp(new X86GlobalStoreXmmOp(globalName, srcXmm, FloatPrecision.F64));
  }

  public void EmitXmmGlobalLoadF32(StdValue result, string globalName, MlirBlock<X86Op> block) {
    var xmm = AllocateFpRegister(result);
    block.AddOp(new X86GlobalLoadXmmOp(globalName, xmm, FloatPrecision.F32));
  }

  public void EmitXmmGlobalStoreF32(StdValue value, string globalName, MlirBlock<X86Op> block) {
    var xmm = EnsureInFpRegister(value, block);
    block.AddOp(new X86GlobalStoreXmmOp(globalName, xmm, FloatPrecision.F32));
  }

  // --- 32-bit register conversion ---

  private static X86Register To32Bit(X86Register reg) => reg switch {
    X86Register.Rax => X86Register.Eax,
    X86Register.Rcx => X86Register.Ecx,
    X86Register.Rdx => X86Register.Edx,
    X86Register.Rbx => X86Register.Ebx,
    X86Register.Rsp => X86Register.Esp,
    X86Register.Rbp => X86Register.Ebp,
    X86Register.Rsi => X86Register.Esi,
    X86Register.Rdi => X86Register.Edi,
    _ => reg
  };
}
