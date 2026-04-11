using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

/// <summary>
/// ARM64-specific register manager. Manages GPR and FP register allocation
/// during StandardToARM64 conversion.
/// </summary>
public class ARM64RegisterManager : RegisterManagerBase<ARM64Register, ARM64FloatRegister, ARM64Op> {
  // x0-x15 are available for allocation (x16/x17 = IP0/IP1, x18 = platform, x29 = FP, x30 = LR)
  private static readonly ARM64Register[] _gprPool = [
    ARM64Register.X0, ARM64Register.X1, ARM64Register.X2, ARM64Register.X3,
    ARM64Register.X4, ARM64Register.X5, ARM64Register.X6, ARM64Register.X7,
    ARM64Register.X8, ARM64Register.X9, ARM64Register.X10, ARM64Register.X11,
    ARM64Register.X12, ARM64Register.X13, ARM64Register.X14, ARM64Register.X15
  ];

  // Only D0-D7 are caller-saved and safe to allocate freely.
  // D8-D15 are callee-saved per AAPCS64 and would require prologue/epilogue save/restore.
  private static readonly ARM64FloatRegister[] _fpPool = [
    ARM64FloatRegister.D0, ARM64FloatRegister.D1, ARM64FloatRegister.D2, ARM64FloatRegister.D3,
    ARM64FloatRegister.D4, ARM64FloatRegister.D5, ARM64FloatRegister.D6, ARM64FloatRegister.D7
  ];

  // On ARM64 AAPCS, x0-x15 are caller-saved (x19-x28 are callee-saved)
  private static readonly ARM64Register[] _callerSavedGprs = [
    ARM64Register.X0, ARM64Register.X1, ARM64Register.X2, ARM64Register.X3,
    ARM64Register.X4, ARM64Register.X5, ARM64Register.X6, ARM64Register.X7,
    ARM64Register.X8, ARM64Register.X9, ARM64Register.X10, ARM64Register.X11,
    ARM64Register.X12, ARM64Register.X13, ARM64Register.X14, ARM64Register.X15
  ];

  // d0-d7 are caller-saved (d8-d15 are callee-saved)
  private static readonly ARM64FloatRegister[] _callerSavedFpRegs = [
    ARM64FloatRegister.D0, ARM64FloatRegister.D1, ARM64FloatRegister.D2, ARM64FloatRegister.D3,
    ARM64FloatRegister.D4, ARM64FloatRegister.D5, ARM64FloatRegister.D6, ARM64FloatRegister.D7
  ];

  // Calling convention: x0-x7 for integer args, d0-d7 for float args
  private static readonly ARM64Register[] CallConvRegs = [
    ARM64Register.X0, ARM64Register.X1, ARM64Register.X2, ARM64Register.X3,
    ARM64Register.X4, ARM64Register.X5, ARM64Register.X6, ARM64Register.X7
  ];

  private static readonly ARM64FloatRegister[] CallConvFpRegs = [
    ARM64FloatRegister.D0, ARM64FloatRegister.D1, ARM64FloatRegister.D2, ARM64FloatRegister.D3,
    ARM64FloatRegister.D4, ARM64FloatRegister.D5, ARM64FloatRegister.D6, ARM64FloatRegister.D7
  ];

  public static int RegisterParamCount => CallConvRegs.Length;

  // --- Abstract method implementations ---

  protected override ARM64Register[] GetGprPool() => _gprPool;
  protected override ARM64FloatRegister[] GetFpPool() => _fpPool;
  protected override ARM64Register[] GetCallerSavedGprs() => _callerSavedGprs;
  protected override ARM64FloatRegister[] GetCallerSavedFpRegs() => _callerSavedFpRegs;

  protected override bool SamePhysicalRegister(ARM64Register a, ARM64Register? b) {
    return b != null && a == b.Value;
  }

  /// <summary>Ensure a value is in a GPR and return the physical register.</summary>
  public ARM64Register LoadToRegister(StdValue value, IrBlock<ARM64Op> block) {
    return EnsureInRegister(value, block);
  }

  protected override void EmitImmediateToRegister(ARM64Register reg, long immediate, IrBlock<ARM64Op> block) {
    if (immediate > int.MaxValue && immediate <= uint.MaxValue) {
      // 32-bit Wd encoding can use MOVN to save instructions vs 64-bit MOVZ+MOVK
      block.AddOp(new ARM64MovRegImm32Op(reg, immediate));
    } else {
      block.AddOp(new ARM64MovRegImmOp(reg, immediate));
    }
  }

  protected override void EmitMovGpr(ARM64Register dest, ARM64Register src, IrBlock<ARM64Op> block) {
    block.AddOp(new ARM64MovRegRegOp(dest, src));
  }

  protected override void EmitSpillGprToStack(int offset, ARM64Register reg, IrBlock<ARM64Op> block) {
    block.AddOp(new ARM64StoreToStackOp(offset, reg, 8));
  }

  protected override void EmitReloadGprFromStack(ARM64Register reg, int offset, IrBlock<ARM64Op> block) {
    block.AddOp(new ARM64LoadFromStackOp(reg, offset, 8));
  }

  protected override void EmitMovFp(ARM64FloatRegister dest, ARM64FloatRegister src, IrBlock<ARM64Op> block) {
    block.AddOp(new ARM64FmovRegRegOp(dest, src, FloatPrecision.F64));
  }

  protected override void EmitSpillFpToStack(int offset, ARM64FloatRegister reg, IrBlock<ARM64Op> block) {
    block.AddOp(new ARM64FloatStoreToStackOp(offset, reg, FloatPrecision.F64));
  }

  protected override void EmitReloadFpFromStack(ARM64FloatRegister reg, int offset, IrBlock<ARM64Op> block) {
    block.AddOp(new ARM64FloatLoadFromStackOp(reg, offset, FloatPrecision.F64));
  }

  protected override ARM64Op MakeSpillGprOp(int offset, ARM64Register reg) {
    return new ARM64StoreToStackOp(offset, reg, 8);
  }

  protected override ARM64Op MakeSpillFpOp(int offset, ARM64FloatRegister reg) {
    return new ARM64FloatStoreToStackOp(offset, reg, FloatPrecision.F64);
  }

  protected override bool IsTerminator(ARM64Op op) {
    return op is ARM64BranchOp or ARM64BranchCondOp or ARM64RetOp or ARM64EpilogueOp;
  }

  // --- ARM64-specific Emit methods ---

  /// <summary>
  /// Emit a 3-address binary register-register instruction (ARM64 is 3-address: dest, src1, src2).
  /// No need to copy LHS first like X86's 2-address form.
  /// </summary>
  public void EmitBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
    IrBlock<ARM64Op> block, Func<ARM64Register, ARM64Register, ARM64Register, ARM64Op> makeOp) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: rhsReg);
    block.AddOp(makeOp(resultReg, lhsReg, rhsReg));
  }

  /// <summary>
  /// Emit a shift instruction. ARM64 has no CL constraint — any register works.
  /// </summary>
  public void EmitShift(StdValue lhs, StdValue rhs, StdValue result,
    IrBlock<ARM64Op> block, Func<ARM64Register, ARM64Register, ARM64Register, ARM64Op> makeOp) {
    EmitBinaryRegReg(lhs, rhs, result, block, makeOp);
  }

  /// <summary>
  /// Emit signed division. ARM64 SDIV is a 3-address instruction, no RAX/RDX constraint.
  /// </summary>
  public void EmitDivision(StdValue lhs, StdValue rhs, StdValue result, IrBlock<ARM64Op> block) {
    EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64SdivRegRegOp(d, l, r));
  }

  public void EmitUnsignedDivision(StdValue lhs, StdValue rhs, StdValue result, IrBlock<ARM64Op> block) {
    EmitBinaryRegReg(lhs, rhs, result, block, (d, l, r) => new ARM64UdivRegRegOp(d, l, r));
  }

  /// <summary>
  /// Emit signed remainder: result = lhs - (lhs / rhs) * rhs via SDIV + MSUB.
  /// </summary>
  public void EmitRemainder(StdValue lhs, StdValue rhs, StdValue result, IrBlock<ARM64Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    // Allocate a temp for the quotient
    var scratch = new StdI64(_scratchIdCounter--);
    var quotReg = AllocateRegister(scratch, block, protect1: lhsReg, protect2: rhsReg);
    block.AddOp(new ARM64SdivRegRegOp(quotReg, lhsReg, rhsReg));
    var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: rhsReg);
    block.AddOp(new ARM64MsubRegRegOp(resultReg, quotReg, rhsReg, lhsReg));
    // Clean up scratch
    _valueToRegister.Remove(scratch);
    _registerContents.Remove(quotReg);
    _lastUsed.Remove(quotReg);
  }

  public void EmitUnsignedRemainder(StdValue lhs, StdValue rhs, StdValue result, IrBlock<ARM64Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    var scratch = new StdI64(_scratchIdCounter--);
    var quotReg = AllocateRegister(scratch, block, protect1: lhsReg, protect2: rhsReg);
    block.AddOp(new ARM64UdivRegRegOp(quotReg, lhsReg, rhsReg));
    var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: rhsReg);
    block.AddOp(new ARM64MsubRegRegOp(resultReg, quotReg, rhsReg, lhsReg));
    _valueToRegister.Remove(scratch);
    _registerContents.Remove(quotReg);
    _lastUsed.Remove(quotReg);
  }

  // --- Store/Load to/from stack ---

  public void EmitStoreToStack(StdValue value, int offset, int sizeInBytes, IrBlock<ARM64Op> block) {
    var srcReg = EnsureInRegister(value, block);
    block.AddOp(new ARM64StoreToStackOp(offset, srcReg, sizeInBytes));
    NoteStoreToStack(value, offset);
  }

  public void EmitLoadFromStack(StdValue result, int offset, int sizeInBytes, IrBlock<ARM64Op> block) {
    if (sizeInBytes == 8)
      _valueStackHome[result] = offset;
    var gpr = AllocateRegister(result, block);
    block.AddOp(new ARM64LoadFromStackOp(gpr, offset, sizeInBytes));
  }

  public void EmitFpStoreToStack(StdValue value, int offset, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var srcFp = EnsureInFpRegister(value, block);
    block.AddOp(new ARM64FloatStoreToStackOp(offset, srcFp, precision));
    NoteFpStoreToStack(value, offset);
  }

  public void EmitFpLoadFromStack(StdValue result, int offset, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var fpReg = AllocateFpRegister(result);
    block.AddOp(new ARM64FloatLoadFromStackOp(fpReg, offset, precision));
  }

  public void EmitFpLoadFromRdata(StdValue result, string rdataLabel, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var fpReg = AllocateFpRegister(result);
    block.AddOp(new ARM64FloatLoadRdataOp(fpReg, rdataLabel, precision));
  }

  // --- Comparisons ---

  public void EmitIntegerCompare(StdValue lhs, StdValue rhs, IrBlock<ARM64Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    block.AddOp(new ARM64CmpRegRegOp(lhsReg, rhsReg));
  }

  public void EmitSetcc(StdValue result, ARM64ConditionCode condition, IrBlock<ARM64Op> block) {
    var reg = AllocateRegister(result, block);
    block.AddOp(new ARM64CsetOp(reg, condition));
  }

  public void EmitBoolTest(StdValue value, IrBlock<ARM64Op> block) {
    var reg = EnsureInRegister(value, block);
    block.AddOp(new ARM64CmpRegImmOp(reg, 0));
  }

  public void EmitSelectI64(StdValue condition, StdValue trueValue, StdValue falseValue, StdValue result,
    IrBlock<ARM64Op> block) {
    var trueReg = EnsureInRegister(trueValue, block);
    var falseReg = EnsureInRegister(falseValue, block, protect1: trueReg);
    var condReg = EnsureInRegister(condition, block, protect1: trueReg, protect2: falseReg);
    block.AddOp(new ARM64CmpRegImmOp(condReg, 0));
    var resultReg = AllocateRegister(result, block, protect1: trueReg);
    block.AddOp(new ARM64CselOp(resultReg, trueReg, falseReg, ARM64ConditionCode.Ne));
  }

  // --- Float operations ---

  public void EmitFpBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
    IrBlock<ARM64Op> block, Func<ARM64FloatRegister, ARM64FloatRegister, ARM64FloatRegister, ARM64Op> makeOp) {
    var rhsFp = EnsureInFpRegister(rhs, block);
    var lhsFp = EnsureInFpRegister(lhs, block);
    var resultFp = AllocateFpRegister(result);
    block.AddOp(makeOp(resultFp, lhsFp, rhsFp));
  }

  public void EmitFpUnary(StdValue input, StdValue result,
    IrBlock<ARM64Op> block, Func<ARM64FloatRegister, ARM64FloatRegister, ARM64Op> makeOp) {
    var srcFp = EnsureInFpRegister(input, block);
    var resultFp = AllocateFpRegister(result);
    block.AddOp(makeOp(resultFp, srcFp));
  }

  public void EmitFpCompare(StdValue lhs, StdValue rhs, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var rhsFp = EnsureInFpRegister(rhs, block);
    var lhsFp = EnsureInFpRegister(lhs, block);
    block.AddOp(new ARM64FcmpOp(lhsFp, rhsFp, precision));
  }

  // --- Conversions ---

  public void EmitFpToInt(StdValue input, StdValue result, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var srcFp = EnsureInFpRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new ARM64FcvtzsOp(destGpr, srcFp, precision));
  }

  public void EmitIntToFp(StdValue input, StdValue result, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var srcGpr = EnsureInRegister(input, block);
    var destFp = AllocateFpRegister(result);
    block.AddOp(new ARM64ScvtfOp(destFp, srcGpr, precision));
  }

  public void EmitFcvt(StdValue input, StdValue result, FloatPrecision destPrecision, IrBlock<ARM64Op> block) {
    var srcFp = EnsureInFpRegister(input, block);
    var destFp = AllocateFpRegister(result);
    block.AddOp(new ARM64FcvtOp(destFp, srcFp, destPrecision));
  }

  // --- Truncation ---

  public void EmitTruncI64ToI32(StdValue input, StdValue result, IrBlock<ARM64Op> block) {
    // On ARM64, 64-bit values truncate to 32-bit by simply using the lower 32 bits.
    // Just transfer the value — the consumer (store/cmp) handles the width.
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    if (destReg != srcReg)
      block.AddOp(new ARM64MovRegRegOp(destReg, srcReg));
  }

  // --- Unsigned conversions ---

  public void EmitUnsignedIntToFp(StdValue input, StdValue result, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var srcGpr = EnsureInRegister(input, block);
    var destFp = AllocateFpRegister(result);
    block.AddOp(new ARM64UcvtfOp(destFp, srcGpr, precision));
  }

  public void EmitFpToUnsignedInt(StdValue input, StdValue result, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var srcFp = EnsureInFpRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new ARM64FcvtzuOp(destGpr, srcFp, precision));
  }

  // --- Bitcast ---

  public void EmitBitcastF64ToI64(StdValue input, StdValue result, IrBlock<ARM64Op> block) {
    var srcFp = EnsureInFpRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new ARM64FmovToGprOp(destGpr, srcFp, FloatPrecision.F64));
  }

  // --- Sign extension ---

  public void EmitSignExtendI32ToI64(StdValue input, StdValue result, IrBlock<ARM64Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    block.AddOp(new ARM64SxtwOp(destReg, srcReg));
  }

  public void EmitZeroExtendI32ToI64(StdValue input, StdValue result, IrBlock<ARM64Op> block) {
    // On ARM64, 32-bit ops already zero-extend to 64-bit, so just mov
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    if (destReg != srcReg)
      block.AddOp(new ARM64MovRegRegOp(destReg, srcReg));
  }

  // --- Address computation ---

  public void EmitLeaFromStack(StdValue result, int offset, IrBlock<ARM64Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new ARM64LeaStackOp(gpr, offset));
  }

  public void EmitLeaRdata(StdValue result, string rdataLabel, IrBlock<ARM64Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new ARM64AdrpAddRdataOp(gpr, rdataLabel));
  }

  public void EmitLeaSymdata(StdValue result, string symdataLabel, IrBlock<ARM64Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new ARM64AdrpAddSymdataOp(gpr, symdataLabel));
  }

  public void EmitLeaUcddata(StdValue result, string ucddataLabel, IrBlock<ARM64Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new ARM64AdrpAddUcddataOp(gpr, ucddataLabel));
  }

  public void EmitFuncRef(StdValue result, string functionName, IrBlock<ARM64Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new ARM64AdrpAddFuncOp(gpr, functionName));
  }

  // --- Specific register placement ---

  public void EnsureInSpecificRegister(StdValue value, ARM64Register target, IrBlock<ARM64Op> block) {
    _registerHints[value] = target;
    var reg = EnsureInRegister(value, block);
    if (reg != target) {
      SpillRegisterIfOccupied(target, block);
      block.AddOp(new ARM64MovRegRegOp(target, reg));
      // Update tracking: value is now in target, not reg
      _registerContents.Remove(reg);
      _lastUsed.Remove(reg);
      _valueToRegister[value] = target;
      _registerContents[target] = value;
      _lastUsed[target] = _currentOpIndex;
    }
  }

  public void EnsureInSpecificFpRegister(StdValue value, ARM64FloatRegister target, IrBlock<ARM64Op> block) {
    var fpReg = EnsureInFpRegister(value, block);
    if (fpReg != target) {
      SpillFpIfOccupied(target, block);
      block.AddOp(new ARM64FmovRegRegOp(target, fpReg, FloatPrecision.F64));
      // Update tracking: value is now in target, not fpReg
      _fpContents.Remove(fpReg);
      _fpLastUsed.Remove(fpReg);
      _valueToFp[value] = target;
      _fpContents[target] = value;
      _fpLastUsed[target] = _currentOpIndex;
    }
  }

  // --- Indirect load/store ---

  public void EmitStoreIndirect(StdValue basePtr, int offset, StdValue value, int sizeInBytes, IrBlock<ARM64Op> block) {
    var valReg = EnsureInRegister(value, block);
    var baseReg = EnsureInRegister(basePtr, block, protect1: valReg);
    switch (sizeInBytes) {
      case 1: block.AddOp(new ARM64StoreByteIndirectOp(baseReg, offset, valReg)); break;
      case 2: block.AddOp(new ARM64StoreHalfIndirectOp(baseReg, offset, valReg)); break;
      case 4: block.AddOp(new ARM64Store32IndirectOp(baseReg, offset, valReg)); break;
      default: block.AddOp(new ARM64StoreIndirectOp(baseReg, offset, valReg)); break;
    }
  }

  public void EmitLoadIndirect(StdValue basePtr, int offset, StdValue result, int sizeInBytes, IrBlock<ARM64Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    var destReg = AllocateRegister(result, block, protect1: baseReg);
    switch (sizeInBytes) {
      case 1: block.AddOp(new ARM64LoadByteIndirectOp(destReg, baseReg, offset)); break;
      case 2: block.AddOp(new ARM64LoadHalfIndirectOp(destReg, baseReg, offset)); break;
      case 4: block.AddOp(new ARM64Load32IndirectOp(destReg, baseReg, offset)); break;
      default: block.AddOp(new ARM64LoadIndirectOp(destReg, baseReg, offset)); break;
    }
  }

  public void EmitFloatStoreIndirect(StdValue basePtr, int offset, StdValue value, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var fpReg = EnsureInFpRegister(value, block);
    var baseReg = EnsureInRegister(basePtr, block);
    block.AddOp(new ARM64FloatStoreIndirectOp(baseReg, offset, fpReg, precision));
  }

  public void EmitFloatLoadIndirect(StdValue basePtr, int offset, StdValue result, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    var fpReg = AllocateFpRegister(result);
    block.AddOp(new ARM64FloatLoadIndirectOp(fpReg, baseReg, offset, precision));
  }

  // --- Global load/store ---

  public void EmitGlobalStore(string globalName, StdValue value, int size, IrBlock<ARM64Op> block) {
    var srcReg = EnsureInRegister(value, block);
    block.AddOp(new ARM64GlobalStoreOp(globalName, srcReg, size));
  }

  public void EmitGlobalLoad(string globalName, StdValue result, int size, IrBlock<ARM64Op> block) {
    var destReg = AllocateRegister(result, block);
    block.AddOp(new ARM64GlobalLoadOp(globalName, destReg, size));
  }

  public void EmitGlobalStoreFloat(string globalName, StdValue value, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var srcFp = EnsureInFpRegister(value, block);
    block.AddOp(new ARM64GlobalStoreFloatOp(globalName, srcFp, precision));
  }

  public void EmitGlobalLoadFloat(string globalName, StdValue result, FloatPrecision precision, IrBlock<ARM64Op> block) {
    var fpReg = AllocateFpRegister(result);
    block.AddOp(new ARM64GlobalLoadFloatOp(globalName, fpReg, precision));
  }

  // --- Null-safe load ---

  public void EmitNullSafeLoadI64(StdValue basePtr, int offset, StdValue result, IrBlock<ARM64Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    var destReg = AllocateRegister(result, block, protect1: baseReg);
    // CMP base, #0; LDR temp, [base, #offset]; CSEL dest, temp, XZR, NE
    block.AddOp(new ARM64CmpRegImmOp(baseReg, 0));
    var scratch = new StdI64(_scratchIdCounter--);
    var tempReg = AllocateRegister(scratch, block, protect1: baseReg, protect2: destReg);
    block.AddOp(new ARM64LoadIndirectOp(tempReg, baseReg, offset));
    block.AddOp(new ARM64CselOp(destReg, tempReg, ARM64Register.Xzr, ARM64ConditionCode.Ne));
    _valueToRegister.Remove(scratch);
    _registerContents.Remove(tempReg);
    _lastUsed.Remove(tempReg);
  }

  // --- Mov value to value ---

  public void EmitMovValueToValue(StdValue input, StdValue result, IrBlock<ARM64Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var dstReg = AllocateRegister(result, block, protect1: srcReg);
    if (srcReg != dstReg)
      block.AddOp(new ARM64MovRegRegOp(dstReg, srcReg));
  }

  // --- Call emission ---

  public void EmitCall(string callee, List<StdValue> args, StdValue? result, IrBlock<ARM64Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    EmitCallShared(args, result, block,
      preGprPlacement: null,
      emitCallOp: () => new ARM64BranchLinkOp(callee),
      consumedByCall: consumedByCall);
  }

  public void EmitTryCall(string callee, List<StdValue> args, StdValue? result, StdValue errorFlag, IrBlock<ARM64Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    EmitCall(callee, args, result, block, consumedByCall);
    // Error flag comes back in X1
    Assign(ARM64Register.X1, errorFlag);
  }

  public void EmitIndirectCall(StdValue callee, List<StdValue> args, StdValue? result, IrBlock<ARM64Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    ARM64Register calleeReg = default;
    EmitCallShared(args, result, block,
      preGprPlacement: () => {
        calleeReg = EnsureInRegister(callee, block);
        // If callee is in a call convention register, move to X9 (scratch)
        if (Array.Exists(CallConvRegs, r => SamePhysicalRegister(r, calleeReg))) {
          SpillRegisterIfOccupied(ARM64Register.X9, block);
          block.AddOp(new ARM64MovRegRegOp(ARM64Register.X9, calleeReg));
          calleeReg = ARM64Register.X9;
        }
      },
      emitCallOp: () => new ARM64BranchLinkRegOp(calleeReg),
      consumedByCall: consumedByCall);
  }

  private void EmitCallShared(
      List<StdValue> args,
      StdValue? result,
      IrBlock<ARM64Op> block,
      Action? preGprPlacement,
      Func<ARM64Op> emitCallOp,
      HashSet<StdValue>? consumedByCall = null) {
    int regArgCount = Math.Min(args.Count, CallConvRegs.Length);
    int stackArgCount = args.Count - regArgCount;

    SpillCallerSavedRegisters(block, consumedByCall);

    // Stack args beyond register count
    int stackArgBytes = stackArgCount > 0 ? ((stackArgCount * 8 + 15) & ~15) : 0;
    if (stackArgBytes > 0)
      block.AddOp(new ARM64SubRegImmOp(ARM64Register.Sp, ARM64Register.Sp, stackArgBytes));

    for (int i = CallConvRegs.Length; i < args.Count; i++) {
      var argReg = EnsureInRegister(args[i], block);
      int offset = (i - CallConvRegs.Length) * 8;
      block.AddOp(new ARM64StoreToSpOp(offset, argReg));
    }

    // Separate GPR and FP args, tracking their type-specific register indices.
    // ARM64 calling convention: GPR args go in X0,X1,X2,... and FP args in D0,D1,D2,...
    // independently. So for args (float, int, float, int), the ints go in X0,X1 and floats in D0,D1.
    var gprArgs = new List<int>();
    var fpArgs = new List<int>();
    var gprIndex = new int[regArgCount]; // maps overall index -> GPR-specific index
    var fpIndex = new int[regArgCount];  // maps overall index -> FP-specific index
    int gprCount = 0, fpCount = 0;
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32) {
        fpIndex[i] = fpCount++;
        fpArgs.Add(i);
      } else {
        gprIndex[i] = gprCount++;
        gprArgs.Add(i);
      }
    }

    // Place FP args using their FP-specific indices (D0, D1, ...)
    var fpSources = new ARM64FloatRegister?[regArgCount];
    foreach (int i in fpArgs)
      fpSources[i] = EnsureInFpRegister(args[i], block);
    PlaceFpArgs(fpArgs, fpSources, fpIndex, regArgCount, block);

    // Gather GPR arg sources using their GPR-specific indices (X0, X1, ...)
    var argSources = new ARM64Register?[regArgCount];
    int?[] argStackHomes = new int?[regArgCount];
    long?[] argConstants = new long?[regArgCount];
    foreach (int i in gprArgs) {
      var target = CallConvRegs[gprIndex[i]];
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
        throw new InvalidOperationException($"ARM64RegisterManager: call arg %{args[i].Id} has no register, no constant, and no stack home");
      }
    }

    preGprPlacement?.Invoke();

    // Place GPR args using cycle-detection with 3-mov swap (ARM64 has no XCHG)
    var placed = new bool[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32) placed[i] = true;
    }

    var targetRegs = new ARM64Register[regArgCount];
    for (int i = 0; i < regArgCount; i++)
      targetRegs[i] = CallConvRegs[args[i] is StdF64 or StdF32 ? 0 : gprIndex[i]];

    // Mark already-in-place
    foreach (int i in gprArgs) {
      if (argConstants[i] != null) continue;
      if (SamePhysicalRegister(argSources[i], targetRegs[i]))
        placed[i] = true;
    }

    // Place non-conflicting args
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
            block.AddOp(new ARM64MovRegRegOp(targetRegs[i], srcReg));
          else
            block.AddOp(new ARM64LoadFromStackOp(targetRegs[i], argStackHomes[i]!.Value, 8));
          placed[i] = true;
          progress = true;
        }
      }
    }

    // Break cycles with 3-mov swap via X16 (IP0, scratch register)
    foreach (int i in gprArgs) {
      if (placed[i] || argConstants[i] != null) continue;
      if (SamePhysicalRegister(argSources[i], targetRegs[i])) {
        placed[i] = true;
        continue;
      }
      // 3-mov swap: tmp = target; target = source; source = tmp
      block.AddOp(new ARM64MovRegRegOp(ARM64Register.X16, targetRegs[i]));
      block.AddOp(new ARM64MovRegRegOp(targetRegs[i], argSources[i]!.Value));
      block.AddOp(new ARM64MovRegRegOp(argSources[i]!.Value, ARM64Register.X16));
      foreach (int j in gprArgs) {
        if (j != i && !placed[j] && argConstants[j] == null
            && SamePhysicalRegister(argSources[j], targetRegs[i])) {
          argSources[j] = argSources[i];
          break;
        }
      }
      placed[i] = true;
    }

    // Place constant args last
    foreach (int i in gprArgs) {
      if (placed[i]) continue;
      if (argConstants[i] is { } constVal) {
        EmitImmediateToRegister(targetRegs[i], constVal, block);
        placed[i] = true;
      }
    }

    block.AddOp(emitCallOp());

    if (stackArgBytes > 0)
      block.AddOp(new ARM64AddRegImmOp(ARM64Register.Sp, ARM64Register.Sp, stackArgBytes));

    InvalidateCallerSavedRegisters();

    if (result != null) {
      if (result is StdF64 or StdF32) {
        AssignFp(ARM64FloatRegister.D0, result);
      } else {
        Assign(ARM64Register.X0, result);
      }
    }
  }

  private static void PlaceFpArgs(List<int> fpArgs, ARM64FloatRegister?[] fpSources, int[] fpIndex, int regArgCount, IrBlock<ARM64Op> block) {
    if (fpArgs.Count == 0) return;

    var placed = new bool[regArgCount];
    foreach (int i in fpArgs) {
      if (fpSources[i] == CallConvFpRegs[fpIndex[i]])
        placed[i] = true;
    }

    bool progress = true;
    while (progress) {
      progress = false;
      foreach (int i in fpArgs) {
        if (placed[i]) continue;
        bool targetBlocked = false;
        foreach (int j in fpArgs) {
          if (j != i && !placed[j] && fpSources[j] == CallConvFpRegs[fpIndex[i]]) {
            targetBlocked = true;
            break;
          }
        }
        if (!targetBlocked) {
          block.AddOp(new ARM64FmovRegRegOp(CallConvFpRegs[fpIndex[i]], fpSources[i]!.Value, FloatPrecision.F64));
          placed[i] = true;
          progress = true;
        }
      }
    }

    // Break cycles with 3-mov swap via D31 (scratch FP register)
    foreach (int i in fpArgs) {
      if (placed[i]) continue;
      if (fpSources[i] == CallConvFpRegs[fpIndex[i]]) {
        placed[i] = true;
        continue;
      }
      block.AddOp(new ARM64FmovRegRegOp(ARM64FloatRegister.D31, fpSources[i]!.Value, FloatPrecision.F64));
      block.AddOp(new ARM64FmovRegRegOp(fpSources[i]!.Value, CallConvFpRegs[fpIndex[i]], FloatPrecision.F64));
      block.AddOp(new ARM64FmovRegRegOp(CallConvFpRegs[fpIndex[i]], ARM64FloatRegister.D31, FloatPrecision.F64));
      foreach (int j in fpArgs) {
        if (j != i && !placed[j] && fpSources[j] == CallConvFpRegs[i]) {
          fpSources[j] = fpSources[i]!.Value;
          break;
        }
      }
      placed[i] = true;
    }
  }

  private void SpillCallerSavedRegisters(IrBlock<ARM64Op> block, HashSet<StdValue>? consumedByCall = null) {
    foreach (var reg in _callerSavedGprs) {
      if (_registerContents.TryGetValue(reg, out var value)
        && !_valueStackHome.ContainsKey(value)
        && !_constantValues.ContainsKey(value)
        && !(consumedByCall != null && consumedByCall.Contains(value))
        && !_allSyntheticValues.Contains(value)) {
        _nextSpillOffset -= 8;
        block.AddOp(new ARM64StoreToStackOp(_nextSpillOffset, reg, 8));
        _valueStackHome[value] = _nextSpillOffset;
      }
    }
    foreach (var fp in _callerSavedFpRegs) {
      if (_fpContents.TryGetValue(fp, out var value)
        && !_valueFpStackHome.ContainsKey(value)
        && !(consumedByCall != null && consumedByCall.Contains(value))) {
        _nextSpillOffset -= 8;
        block.AddOp(new ARM64FloatStoreToStackOp(_nextSpillOffset, fp, FloatPrecision.F64));
        _valueFpStackHome[value] = _nextSpillOffset;
      }
    }
  }

  // --- Parameters ---

  public void NoteParam(StdValue paramValue, int paramIndex, IrBlock<ARM64Op> block) {
    NoteParam(paramValue, paramIndex, paramIndex, block);
  }

  public void NoteParam(StdValue paramValue, int paramIndex, int typeSpecificIndex, IrBlock<ARM64Op> block) {
    if (typeSpecificIndex < CallConvRegs.Length) {
      if (paramValue is StdF64 or StdF32) {
        AssignFp(CallConvFpRegs[typeSpecificIndex], paramValue);
      } else {
        Assign(CallConvRegs[typeSpecificIndex], paramValue);
      }
    } else {
      // Stack args: on ARM64, caller's frame puts them above FP
      int stackOffset = 16 + (paramIndex - CallConvRegs.Length) * 8;
      if (paramValue is StdF64 or StdF32) {
        var fp = AllocateFpRegister(paramValue);
        block.AddOp(new ARM64FloatLoadFromStackOp(fp, stackOffset, FloatPrecision.F64));
      } else {
        var gpr = AllocateRegister(paramValue, block);
        block.AddOp(new ARM64LoadFromStackOp(gpr, stackOffset, 8));
      }
    }
  }

  // --- Memcpy / BulkZero ---

  public void EmitMemCopy(StdValue srcPtr, StdValue dstPtr, StdValue byteCount, IrBlock<ARM64Op> block) {
    // ARM64 memcpy inline loop clobbers X0-X3 (post-increment), so spill first
    SpillRegisterIfOccupied(ARM64Register.X0, block);
    SpillRegisterIfOccupied(ARM64Register.X1, block);
    SpillRegisterIfOccupied(ARM64Register.X2, block);
    SpillRegisterIfOccupied(ARM64Register.X3, block);
    // ARM64 memcpy uses X0=dst, X1=src, X2=count
    EnsureInSpecificRegister(dstPtr, ARM64Register.X0, block);
    EnsureInSpecificRegister(srcPtr, ARM64Register.X1, block);
    EnsureInSpecificRegister(byteCount, ARM64Register.X2, block);
    block.AddOp(new ARM64MemcpyOp());
    InvalidateGpr(ARM64Register.X0);
    InvalidateGpr(ARM64Register.X1);
    InvalidateGpr(ARM64Register.X2);
    InvalidateGpr(ARM64Register.X3);
  }

  public void EmitMemCopyReverse(StdValue srcPtr, StdValue dstPtr, StdValue byteCount, IrBlock<ARM64Op> block) {
    // Backward memcpy for overlapping shift-right: X0=dst, X1=src, X2=count
    SpillRegisterIfOccupied(ARM64Register.X0, block);
    SpillRegisterIfOccupied(ARM64Register.X1, block);
    SpillRegisterIfOccupied(ARM64Register.X2, block);
    SpillRegisterIfOccupied(ARM64Register.X3, block);
    EnsureInSpecificRegister(dstPtr, ARM64Register.X0, block);
    EnsureInSpecificRegister(srcPtr, ARM64Register.X1, block);
    EnsureInSpecificRegister(byteCount, ARM64Register.X2, block);
    block.AddOp(new ARM64MemcpyReverseOp());
    InvalidateGpr(ARM64Register.X0);
    InvalidateGpr(ARM64Register.X1);
    InvalidateGpr(ARM64Register.X2);
    InvalidateGpr(ARM64Register.X3);
  }

  public void EmitBulkZero(StdValue dstPtr, StdValue qwordCount, IrBlock<ARM64Op> block) {
    SpillRegisterIfOccupied(ARM64Register.X0, block);
    SpillRegisterIfOccupied(ARM64Register.X1, block);
    EnsureInSpecificRegister(dstPtr, ARM64Register.X0, block);
    EnsureInSpecificRegister(qwordCount, ARM64Register.X1, block);
    block.AddOp(new ARM64BulkZeroOp());
    InvalidateGpr(ARM64Register.X0);
    InvalidateGpr(ARM64Register.X1);
  }

  // --- Helpers ---

  private static bool SamePhysicalRegister(ARM64Register? a, ARM64Register? b) {
    return a != null && b != null && a.Value == b.Value;
  }
}
