using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

/// <summary>
/// Manages GPR and XMM register allocation during StandardToX86 conversion.
/// Tracks which physical registers hold which values, handles eviction
/// when registers are exhausted, and reloads from stack when needed.
/// </summary>
public class RegisterManager {
  private static readonly X86Register[] GprPool = [
    X86Register.Eax, X86Register.Ecx, X86Register.Edx,
    X86Register.Ebx, X86Register.Esi, X86Register.Edi,
    X86Register.R8, X86Register.R9
  ];

  // Sequential allocation pointer — assigns registers in pool order
  // to match the naive allocator's behavior when registers aren't exhausted
  private int _nextFreshIndex;

  private readonly Dictionary<X86Register, StdValue> _registerContents = [];
  private readonly Dictionary<StdValue, X86Register> _valueToRegister = [];
  private readonly Dictionary<StdValue, int> _valueStackHome = [];
  private readonly Dictionary<X86Register, int> _lastUsed = [];
  private readonly Dictionary<StdValue, long> _constantValues = [];
  private readonly Dictionary<StdValue, X86Register> _registerHints = [];
  private HashSet<StdValue>? _deferredValues;
  private int _currentOpIndex;
  private int _scratchIdCounter = -1000;

  // Spill slot allocation: offsets grow downward (more negative) from the variable area
  private int _spillBaseOffset;
  private int _nextSpillOffset;
  private int _minSpillOffset; // tracks deepest spill across all blocks

  /// <summary>
  /// Total stack frame size including variables and spill slots,
  /// aligned to 16 bytes. Returns 0 if no stack space is needed.
  /// </summary>
  public int TotalStackSize {
    get {
      int raw = -Math.Min(_nextSpillOffset, _minSpillOffset);
      return raw > 0 ? (raw + 15) & ~15 : 0;
    }
  }

  // XMM register pool and state
  private static readonly X86XmmRegister[] XmmPool = [
    X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2, X86XmmRegister.Xmm3,
    X86XmmRegister.Xmm4, X86XmmRegister.Xmm5, X86XmmRegister.Xmm6, X86XmmRegister.Xmm7,
    X86XmmRegister.Xmm8, X86XmmRegister.Xmm9, X86XmmRegister.Xmm10, X86XmmRegister.Xmm11,
    X86XmmRegister.Xmm12, X86XmmRegister.Xmm13, X86XmmRegister.Xmm14, X86XmmRegister.Xmm15
  ];

  private int _nextFreshXmmIndex;
  private readonly Dictionary<X86XmmRegister, StdValue> _xmmContents = [];
  private readonly Dictionary<StdValue, X86XmmRegister> _valueToXmm = [];
  private readonly Dictionary<StdValue, int> _valueXmmStackHome = [];
  private readonly Dictionary<X86XmmRegister, int> _xmmLastUsed = [];
  private readonly Dictionary<StdValue, X86XmmRegister> _valuePreviousXmm = [];

  /// <summary>
  /// Record a preferred register for a value. AllocateRegister will try to
  /// honor this hint when the preferred register is available.
  /// </summary>
  public void SetRegisterHint(StdValue value, X86Register preferredReg) {
    _registerHints[value] = preferredReg;
  }

  /// <summary>
  /// Allocate a physical register for a new value.
  /// May evict an existing value if all registers are occupied.
  /// Protected registers will not be evicted.
  /// </summary>
  private X86Register AllocateRegister(StdValue value, MlirBlock<X86Op> block,
    X86Register? protect1 = null, X86Register? protect2 = null) {
    // 0. Try the hinted register first
    if (_registerHints.TryGetValue(value, out var hinted)) {
      var hinted32 = To32Bit(hinted);
      if (!SamePhysicalRegister(hinted32, protect1) && !SamePhysicalRegister(hinted32, protect2)) {
        if (!_registerContents.ContainsKey(hinted32)) {
          Assign(hinted32, value);
          return hinted32;
        }
        // If the hinted register holds a rematerializable constant, evict it
        if (_registerContents.TryGetValue(hinted32, out var occupant) && _constantValues.ContainsKey(occupant)) {
          _valueToRegister.Remove(occupant);
          _registerContents.Remove(hinted32);
          _lastUsed.Remove(hinted32);
          Assign(hinted32, value);
          return hinted32;
        }
      }
    }

    // 1. Try the next sequential slot (preserves existing test output)
    if (_nextFreshIndex < GprPool.Length) {
      var candidate = GprPool[_nextFreshIndex];
      if (!_registerContents.ContainsKey(candidate)
          && candidate != protect1 && candidate != protect2) {
        _nextFreshIndex++;
        Assign(candidate, value);
        return candidate;
      }
    }

    // 2. Find any free register in pool order
    foreach (var reg in GprPool) {
      if (!_registerContents.ContainsKey(reg)
          && reg != protect1 && reg != protect2) {
        Assign(reg, value);
        return reg;
      }
    }

    // 3. All registers occupied — evict one
    Evict(block, protect1, protect2);

    // Now find the freed register
    foreach (var reg in GprPool) {
      if (!_registerContents.ContainsKey(reg)) {
        Assign(reg, value);
        return reg;
      }
    }

    throw new InvalidOperationException("RegisterManager: failed to allocate register after eviction");
  }

  /// <summary>
  /// Ensure a value is in a physical register, reloading from stack if needed.
  /// Protected registers will not be evicted during allocation.
  /// </summary>
  private X86Register EnsureInRegister(StdValue value, MlirBlock<X86Op> block,
    X86Register? protect1 = null, X86Register? protect2 = null) {
    // Already in a register
    if (_valueToRegister.TryGetValue(value, out var reg)) {
      _lastUsed[reg] = _currentOpIndex;
      return reg;
    }

    // Constant rematerialization: re-emit the immediate instead of loading from stack
    if (_constantValues.TryGetValue(value, out var immediate)) {
      var reloadReg = AllocateRegister(value, block, protect1, protect2);
      EmitImmediateToRegister(reloadReg, immediate, block);
      return reloadReg;
    }

    // Not in a register — must reload from stack
    if (_valueStackHome.TryGetValue(value, out var displacement)) {
      var reloadReg = AllocateRegister(value, block, protect1, protect2);
      block.AddOp(new X86MovRegMemOp(reloadReg, displacement, 8));
      return reloadReg;
    }

    throw new InvalidOperationException($"RegisterManager: value %{value.Id} has no register and no stack home");
  }

  /// <summary>
  /// Values only consumed by sink ops (return/call) skip eager register materialization.
  /// Constants are rematerialized via _constantValues; loads are re-fetched from _valueStackHome.
  /// </summary>
  public HashSet<StdValue>? DeferredValues { set => _deferredValues = value; }

  /// <summary>
  /// Allocate a register and load an immediate value into it.
  /// If the value is in the deferred set, only records it for later rematerialization.
  /// </summary>
  public void EmitLoadImmediate(StdValue result, long immediate, MlirBlock<X86Op> block) {
    _constantValues[result] = immediate;
    if (_deferredValues?.Contains(result) == true) return;
    var gpr = AllocateRegister(result, block);
    EmitImmediateToRegister(gpr, immediate, block);
  }

  /// <summary>
  /// Emit instructions to load an immediate value into a specific register.
  /// Shared by initial constant loading and rematerialization.
  /// </summary>
  private static void EmitImmediateToRegister(X86Register gpr, long immediate, MlirBlock<X86Op> block) {
    if (immediate == 0) {
      block.AddOp(new X86XorRegRegOp(gpr, gpr));
    } else if (immediate < int.MinValue || immediate > int.MaxValue) {
      block.AddOp(new X86MovRegImmOp(To64Bit(gpr), immediate));
    } else {
      block.AddOp(new X86MovRegImmOp(gpr, immediate));
    }
  }

  /// <summary>
  /// Emit a two-operand register-register instruction (e.g. add, sub).
  /// When lhsConsumed is true, reuses the LHS register directly (no extra mov).
  /// When false, allocates a separate result register and copies LHS into it first.
  /// When useLeaForAdd is true and the result needs a separate register, emits a single
  /// LEA instead of mov+add.
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
  /// Result = lhs * rhs.
  /// </summary>
  public void EmitMultiply(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block,
    bool lhsConsumed = false) {
    EmitBinaryRegReg(lhs, rhs, result, block, (l, r) => new X86ImulRegRegOp(l, r), lhsConsumed);
  }

  /// <summary>
  /// Emit a shift instruction (SHL/SAR). x86 shifts require the shift count in CL.
  /// Ensures the shift count is in ECX, the value to shift is in a different register.
  /// </summary>
  public void EmitShift(StdValue lhs, StdValue rhs, StdValue result,
    MlirBlock<X86Op> block, Func<X86Register, X86Op> makeShiftOp) {
    var lhsReg = EnsureInRegister(lhs, block);
    var rhsReg = EnsureInRegister(rhs, block, protect1: lhsReg);

    // If LHS is in ECX and RHS is not, we need to move LHS out of ECX first
    // because ECX will be used for the shift count.
    if (lhsReg == X86Register.Ecx && rhsReg != X86Register.Ecx) {
      // Allocate result in a register that isn't ECX or rhsReg
      var resultReg = AllocateRegister(result, block, protect1: X86Register.Ecx, protect2: rhsReg);
      block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
      // Now put shift count in ECX
      block.AddOp(new X86MovRegRegOp(X86Register.Ecx, rhsReg));
      block.AddOp(makeShiftOp(resultReg));
    } else {
      // Move shift count to ECX if needed
      if (rhsReg != X86Register.Ecx) {
        SpillRegisterIfOccupied(X86Register.Ecx, block);
        block.AddOp(new X86MovRegRegOp(X86Register.Ecx, rhsReg));
      }
      // Allocate result register, protecting LHS and ECX
      var resultReg = AllocateRegister(result, block, protect1: lhsReg, protect2: X86Register.Ecx);
      if (resultReg != lhsReg) {
        block.AddOp(new X86MovRegRegOp(resultReg, lhsReg));
      }
      block.AddOp(makeShiftOp(resultReg));
    }
  }

  /// <summary>
  /// Emit IDIV and capture the quotient in EAX.
  /// Handles register constraints: divisor must not be in EAX/EDX.
  /// </summary>
  public void EmitDivision(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation(lhs, rhs, result, X86Register.Eax, block);
  }

  /// <summary>
  /// Emit IDIV and capture the remainder in EDX.
  /// Handles register constraints: divisor must not be in EAX/EDX.
  /// </summary>
  public void EmitRemainder(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation(lhs, rhs, result, X86Register.Edx, block);
  }

  /// <summary>
  /// Emit unsigned DIV and capture the quotient in EAX.
  /// </summary>
  public void EmitUnsignedDivision(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation(lhs, rhs, result, X86Register.Eax, block);
  }

  /// <summary>
  /// Emit unsigned DIV and capture the remainder in EDX.
  /// </summary>
  public void EmitUnsignedRemainder(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation(lhs, rhs, result, X86Register.Edx, block);
  }

  /// <summary>
  /// Emit IDIV instruction with proper register allocation.
  /// IDIV clobbers both EAX (quotient) and EDX (remainder), so caller specifies which to capture.
  /// </summary>
  /// Shared preamble for all DIV/IDIV variants: ensure operands in registers,
  /// relocate divisor out of EAX/EDX, spill EAX/EDX, move dividend into EAX.
  private X86Register PrepareDivisionRegisters(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);

    if (rhsReg == X86Register.Eax || rhsReg == X86Register.Edx) {
      var safeReg = FindSafeRegisterForIdiv(lhsReg, rhsReg);
      SpillRegisterIfOccupied(safeReg, block);
      block.AddOp(new X86MovRegRegOp(safeReg, rhsReg));
      rhsReg = safeReg;
    }

    SpillRegisterIfOccupied(X86Register.Eax, block);
    SpillRegisterIfOccupied(X86Register.Edx, block);

    if (lhsReg != X86Register.Eax) {
      block.AddOp(new X86MovRegRegOp(X86Register.Eax, lhsReg));
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
    block.AddOp(new X86MovsxdOp(To64Bit(destReg), srcReg));
  }

  public void EmitZeroExtendI32ToI64(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    // x86-64 clears bits 63:32 on any 32-bit register write, so a 32-bit mov suffices
    block.AddOp(new X86MovRegRegOp(To32Bit(destReg), srcReg));
  }

  public void EmitTruncI64ToI32(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var destReg = AllocateRegister(result, block);
    block.AddOp(new X86MovRegRegOp(To32Bit(destReg), To32Bit(srcReg)));
  }

  // --- 32-bit division variants ---

  public void EmitDivision32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation32(lhs, rhs, result, X86Register.Eax, block);
  }

  public void EmitRemainder32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitIdivOperation32(lhs, rhs, result, X86Register.Edx, block);
  }

  public void EmitUnsignedDivision32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation32(lhs, rhs, result, X86Register.Eax, block);
  }

  public void EmitUnsignedRemainder32(StdValue lhs, StdValue rhs, StdValue result, MlirBlock<X86Op> block) {
    EmitDivOperation32(lhs, rhs, result, X86Register.Edx, block);
  }

  private void EmitIdivOperation32(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    // 32-bit IDIV requires EDX:EAX dividend; CDQ sets up the sign extension
    block.AddOp(new X86CdqOp());
    block.AddOp(new X86IdivReg32Op(rhsReg));
    Assign(resultRegister, result);
  }

  private void EmitDivOperation32(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    block.AddOp(new X86XorRegRegOp(X86Register.Edx, X86Register.Edx));
    block.AddOp(new X86DivReg32Op(rhsReg));
    Assign(resultRegister, result);
  }

  private void EmitDivOperation(StdValue lhs, StdValue rhs, StdValue result, X86Register resultRegister, MlirBlock<X86Op> block) {
    var rhsReg = PrepareDivisionRegisters(lhs, rhs, block);
    // Zero RDX for unsigned division (vs CQO for signed)
    block.AddOp(new X86XorRegRegOp(X86Register.Edx, X86Register.Edx));
    block.AddOp(new X86DivRegOp(rhsReg));
    Assign(resultRegister, result);
  }

  /// <summary>
  /// Find a register that is safe to use as a temporary for IDIV divisor relocation.
  /// Must not be EAX, EDX, the LHS register, or the current divisor register.
  /// Prefers a free register, but will return an occupied one (caller must spill it).
  /// </summary>
  private X86Register FindSafeRegisterForIdiv(X86Register lhsReg, X86Register divisorReg) {
    X86Register? fallback = null;
    foreach (var reg in GprPool) {
      if (reg == X86Register.Eax || reg == X86Register.Edx) continue;
      if (reg == lhsReg || reg == divisorReg) continue;
      if (!_registerContents.ContainsKey(reg))
        return reg;
      fallback ??= reg;
    }
    if (fallback != null)
      return fallback.Value;
    throw new InvalidOperationException("RegisterManager: no safe register for IDIV divisor relocation");
  }

  /// <summary>
  /// If a register contains a live value without a stack home, spill it.
  /// Then remove the register mapping so the register can be reused.
  /// </summary>
  private void SpillRegisterIfOccupied(X86Register reg, MlirBlock<X86Op> block) {
    if (!_registerContents.TryGetValue(reg, out var existing)) return;
    // Constants can be rematerialized — no need to store to stack
    if (!_valueStackHome.ContainsKey(existing) && !_constantValues.ContainsKey(existing)) {
      _nextSpillOffset -= 8;
      block.AddOp(new X86MovMemRegOp(_nextSpillOffset, reg, 8));
      _valueStackHome[existing] = _nextSpillOffset;
    }
    _valueToRegister.Remove(existing);
    _registerContents.Remove(reg);
    _lastUsed.Remove(reg);
  }

  /// <summary>
  /// Store a GPR value to a stack offset and record the stack home.
  /// </summary>
  public void EmitStoreToStack(StdValue value, int offset, int sizeInBytes, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(value, block);
    block.AddOp(new X86MovMemRegOp(offset, srcReg, sizeInBytes));
    NoteStoreToStack(value, offset);
  }

  /// <summary>
  /// Load a GPR value from a stack offset.
  /// For 8-byte loads, records the stack home so call arg placement can load
  /// directly into the target register instead of going through an intermediate.
  /// Sub-8-byte loads don't record a stack home because EnsureInRegister
  /// reloads always use 8-byte movs.
  /// </summary>
  public void EmitLoadFromStack(StdValue result, int offset, int sizeInBytes, MlirBlock<X86Op> block) {
    if (sizeInBytes == 8)
      _valueStackHome[result] = offset;
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86MovRegMemOp(gpr, offset, sizeInBytes));
  }

  /// <summary>
  /// Store an XMM value to a stack offset and record the stack home.
  /// </summary>
  public void EmitXmmStoreToStack(StdValue value, int offset, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(value, block);
    block.AddOp(new X86MovMemXmmOp(offset, srcXmm, FloatPrecision.F64));
    NoteXmmStoreToStack(value, offset);
  }

  /// <summary>
  /// Load an XMM value from a stack offset.
  /// </summary>
  public void EmitXmmLoadFromStack(StdValue result, int offset, MlirBlock<X86Op> block) {
    var xmmReg = AllocateXmmRegister(result);
    block.AddOp(new X86MovXmmMemOp(xmmReg, offset, FloatPrecision.F64));
  }

  /// <summary>
  /// Allocate an XMM register and load a float constant from rdata.
  /// </summary>
  public void EmitXmmLoadFromRipRelative(StdValue result, string rdataLabel, MlirBlock<X86Op> block) {
    var xmmReg = AllocateXmmRegister(result);
    block.AddOp(new X86MovXmmRipRelOp(xmmReg, rdataLabel, FloatPrecision.F64));
  }

  // --- F32 (single-precision) XMM stack/rip-relative operations ---

  /// <summary>
  /// Store an XMM value to a stack offset using MOVSS (single-precision).
  /// </summary>
  public void EmitXmmStoreToStackF32(StdValue value, int offset, MlirBlock<X86Op> block) {
    var xmm = EnsureInXmmRegister(value, block);
    block.AddOp(new X86MovMemXmmOp(offset, xmm, FloatPrecision.F32));
  }

  /// <summary>
  /// Load an XMM value from a stack offset using MOVSS (single-precision).
  /// </summary>
  public void EmitXmmLoadFromStackF32(StdValue result, int offset, MlirBlock<X86Op> block) {
    var xmm = AllocateXmmRegister(result);
    block.AddOp(new X86MovXmmMemOp(xmm, offset, FloatPrecision.F32));
  }

  /// <summary>
  /// Allocate an XMM register and load a float32 constant from rdata using MOVSS.
  /// </summary>
  public void EmitXmmLoadFromRipRelativeF32(StdValue result, string rdataLabel, MlirBlock<X86Op> block) {
    var xmm = AllocateXmmRegister(result);
    block.AddOp(new X86MovXmmRipRelOp(xmm, rdataLabel, FloatPrecision.F32));
  }

  /// <summary>
  /// Ensure both GPR operands are in registers and emit cmp.
  /// </summary>
  public void EmitIntegerCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var rhsReg = EnsureInRegister(rhs, block);
    var lhsReg = EnsureInRegister(lhs, block, protect1: rhsReg);
    // Use 64-bit registers when either constant exceeds signed 32-bit range
    bool needsWide = (_constantValues.TryGetValue(rhs, out var rhsImm) && (rhsImm < int.MinValue || rhsImm > int.MaxValue))
                  || (_constantValues.TryGetValue(lhs, out var lhsImm) && (lhsImm < int.MinValue || lhsImm > int.MaxValue));
    if (needsWide) {
      lhsReg = To64Bit(lhsReg);
      rhsReg = To64Bit(rhsReg);
    }
    block.AddOp(new X86CmpRegRegOp(lhsReg, rhsReg));
  }

  /// <summary>
  /// Ensure a bool value is in a register and emit test reg, reg.
  /// Sets ZF=1 when value is 0 (false).
  /// </summary>
  public void EmitBoolTest(StdValue value, MlirBlock<X86Op> block) {
    var reg = EnsureInRegister(value, block);
    block.AddOp(new X86TestRegRegOp(reg, reg));
  }

  /// <summary>
  /// Emit a conditional select: result = condition ? trueValue : falseValue.
  /// Uses TEST + CMOVNE to select between two i64 values.
  /// </summary>
  public void EmitSelectI64(StdValue condition, StdValue trueValue, StdValue falseValue, StdValue result, MlirBlock<X86Op> block) {
    var trueReg = EnsureInRegister(trueValue, block);
    var falseReg = EnsureInRegister(falseValue, block, protect1: trueReg);
    var condReg = EnsureInRegister(condition, block, protect1: trueReg, protect2: falseReg);

    // TEST condition, condition (sets ZF=1 if condition is 0/false)
    block.AddOp(new X86TestRegRegOp(condReg, condReg));

    // Start with falseValue in result, then CMOVNE to trueValue if condition != 0
    var resultReg = AllocateRegister(result, block, protect1: To64Bit(trueReg), protect2: To64Bit(condReg));
    block.AddOp(new X86MovRegRegOp(resultReg, falseReg));
    block.AddOp(new X86CmovneRegRegOp(resultReg, trueReg));
  }

  /// <summary>
  /// Materialize a comparison result (from CPU flags) into a GPR via setcc + movzx.
  /// </summary>
  public void EmitSetcc(StdValue result, string condition, MlirBlock<X86Op> block) {
    var reg = AllocateRegister(result, block);
    block.AddOp(new X86SetccOp(condition, reg));
    block.AddOp(new X86MovzxRegOp(reg));
  }

  /// <summary>
  /// Materialize a float comparison result with NaN-correct IEEE 754 semantics.
  /// For eq/ne, a single setcc is insufficient because ucomisd sets ZF=1 for NaN.
  /// </summary>
  public void EmitFloatSetcc(StdValue result, string predicate, MlirBlock<X86Op> block) {
    var reg = AllocateRegister(result, block);
    switch (predicate) {
      case "eq":
        // ordered equal: (ZF=1) AND (PF=0)
        EmitCompoundSetcc(reg, "e", "np", (a, b) => new X86AndRegRegOp(a, b), block);
        break;
      case "ne":
        // unordered not-equal: (ZF=0) OR (PF=1)
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

  /// <summary>
  /// Emit two setcc instructions and combine them, using a scratch register for the second.
  /// </summary>
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

  /// <summary>
  /// Emit an XMM binary operation (e.g. addsd).
  /// </summary>
  public void EmitXmmBinaryRegReg(StdValue lhs, StdValue rhs, StdValue result,
    MlirBlock<X86Op> block, Func<X86XmmRegister, X86XmmRegister, X86Op> makeOp) {
    var rhsXmm = EnsureInXmmRegister(rhs, block);
    var lhsXmm = EnsureInXmmRegister(lhs, block);
    var resultXmm = AllocateXmmRegister(result);
    if (resultXmm != lhsXmm) {
      block.AddOp(new X86MovXmmXmmOp(resultXmm, lhsXmm, FloatPrecision.F64));
    }
    block.AddOp(makeOp(resultXmm, rhsXmm));
  }

  /// <summary>
  /// Emit cvttsd2si: convert XMM float to GPR integer with truncation.
  /// </summary>
  public void EmitCvttSd2Si(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new X86CvttFloat2SiOp(destGpr, srcXmm, FloatPrecision.F64));
  }

  /// <summary>
  /// Emit movq: reinterpret XMM float bits as GPR integer (bitcast).
  /// </summary>
  public void EmitMovqXmmToGpr(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new X86MovqXmmToGprOp(destGpr, srcXmm));
  }

  /// <summary>
  /// Emit cvtsi2sd: convert GPR integer to XMM float.
  /// </summary>
  public void EmitCvtSi2Sd(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcGpr = EnsureInRegister(input, block);
    var destXmm = AllocateXmmRegister(result);
    block.AddOp(new X86CvtSi2FloatOp(destXmm, srcGpr, FloatPrecision.F64));
  }

  // --- F32 conversion operations ---

  /// <summary>
  /// Emit cvttss2si: convert XMM float32 to GPR integer with truncation.
  /// </summary>
  public void EmitCvttSs2Si(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var destGpr = AllocateRegister(result, block);
    block.AddOp(new X86CvttFloat2SiOp(destGpr, srcXmm, FloatPrecision.F32));
  }

  /// <summary>
  /// Emit cvtsi2ss: convert GPR integer to XMM float32.
  /// </summary>
  public void EmitCvtSi2Ss(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcGpr = EnsureInRegister(input, block);
    var destXmm = AllocateXmmRegister(result);
    block.AddOp(new X86CvtSi2FloatOp(destXmm, srcGpr, FloatPrecision.F32));
  }

  /// <summary>
  /// Emit cvtsd2ss: convert double-precision to single-precision.
  /// </summary>
  public void EmitCvtSd2Ss(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var destXmm = AllocateXmmRegister(result);
    block.AddOp(new X86CvtSd2SsOp(destXmm, srcXmm));
  }

  /// <summary>
  /// Emit cvtss2sd: convert single-precision to double-precision.
  /// </summary>
  public void EmitCvtSs2Sd(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var destXmm = AllocateXmmRegister(result);
    block.AddOp(new X86CvtSs2SdOp(destXmm, srcXmm));
  }

  /// <summary>
  /// Emit a unary XMM operation where the instruction reads src and writes dest
  /// independently (e.g. sqrtsd, roundsd). No preliminary copy needed.
  /// </summary>
  public void EmitXmmUnaryRegReg(StdValue input, StdValue result, MlirBlock<X86Op> block,
    Func<X86XmmRegister, X86XmmRegister, X86Op> makeOp) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var resultXmm = AllocateXmmRegister(result);
    block.AddOp(makeOp(resultXmm, srcXmm));
  }

  /// <summary>
  /// Emit absf64: clear sign bit via ANDPD with rdata mask.
  /// </summary>
  public void EmitAbsF64(StdValue input, StdValue result, string maskLabel, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var resultXmm = AllocateXmmRegister(result);
    if (resultXmm != srcXmm) {
      block.AddOp(new X86MovXmmXmmOp(resultXmm, srcXmm, FloatPrecision.F64));
    }
    block.AddOp(new X86AndMaskRipRelOp(resultXmm, maskLabel, FloatPrecision.F64));
  }

  /// <summary>
  /// Emit absf32: clear sign bit via ANDPS with rdata mask.
  /// </summary>
  public void EmitAbsF32(StdValue input, StdValue result, string maskLabel, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(input, block);
    var resultXmm = AllocateXmmRegister(result);
    if (resultXmm != srcXmm) {
      block.AddOp(new X86MovXmmXmmOp(resultXmm, srcXmm, FloatPrecision.F32));
    }
    block.AddOp(new X86AndMaskRipRelOp(resultXmm, maskLabel, FloatPrecision.F32));
  }

  /// <summary>
  /// Ensure both XMM operands are in registers and emit ucomisd.
  /// </summary>
  public void EmitXmmCompare(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var rhsReg = EnsureInXmmRegister(rhs, block);
    var lhsReg = EnsureInXmmRegister(lhs, block);
    block.AddOp(new X86UcomisXmmOp(lhsReg, rhsReg, FloatPrecision.F64));
  }

  /// <summary>
  /// Ensure both XMM operands are in registers and emit ucomiss (single-precision).
  /// </summary>
  public void EmitXmmCompareF32(StdValue lhs, StdValue rhs, MlirBlock<X86Op> block) {
    var lhsXmm = EnsureInXmmRegister(lhs, block);
    var rhsXmm = EnsureInXmmRegister(rhs, block);
    block.AddOp(new X86UcomisXmmOp(lhsXmm, rhsXmm, FloatPrecision.F32));
  }

  /// <summary>
  /// Ensure a value is in a specific physical register, emitting a mov if needed.
  /// Hints the allocator to place the value directly in the target register
  /// when reloading from stack or rematerializing, avoiding a redundant reg-reg mov.
  /// </summary>
  public void EnsureInSpecificRegister(StdValue value, X86Register target, MlirBlock<X86Op> block) {
    _registerHints[value] = target;
    var reg = EnsureInRegister(value, block);
    if (reg != target && reg != To32Bit(target)) {
      block.AddOp(new X86MovRegRegOp(target, reg));
    }
  }

  // Internal calling convention: pass up to 8 integer parameters in registers
  private static readonly X86Register[] CallConvRegs = [
    X86Register.Ecx, X86Register.Edx, X86Register.R8, X86Register.R9,
    X86Register.Esi, X86Register.Edi, X86Register.Eax, X86Register.Ebx
  ];
  public static int RegisterParamCount => CallConvRegs.Length;

  /// <summary>
  /// Shared implementation for direct and indirect calls.
  /// Handles argument placement, caller-saved spilling, GPR arg shuffling,
  /// stack cleanup, register invalidation, and result assignment.
  /// </summary>
  private void EmitCallShared(
      List<StdValue> args,
      StdValue? result,
      MlirBlock<X86Op> block,
      Action? preGprPlacement,
      Func<X86Op> emitCallOp,
      HashSet<StdValue>? consumedByCall = null) {
    int regArgCount = Math.Min(args.Count, CallConvRegs.Length);
    int stackArgCount = args.Count - regArgCount;

    // Spill caller-saved registers that hold live values without a stack home.
    // This must happen before argument placement overwrites those registers.
    SpillCallerSavedRegisters(block, consumedByCall);

    // Allocate stack space for overflow args (aligned to 16 bytes)
    int stackArgBytes = stackArgCount > 0 ? ((stackArgCount * 8 + 15) & ~15) : 0;
    if (stackArgBytes > 0)
      block.AddOp(new X86SubRegImmOp(X86Register.Rsp, stackArgBytes));

    // Place stack args first (5th+ parameters) using rsp-relative stores
    for (int i = CallConvRegs.Length; i < args.Count; i++) {
      var argReg = EnsureInRegister(args[i], block);
      int offset = (i - CallConvRegs.Length) * 8;
      block.AddOp(new X86MovMemRspRegOp(offset, argReg));
    }

    // Place float args into their target XMM registers using multi-pass
    // placement to avoid overwriting an arg that hasn't been placed yet.
    var gprArgs = new List<int>();
    var xmmArgs = new List<int>();
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32)
        xmmArgs.Add(i);
      else
        gprArgs.Add(i);
    }

    // Snapshot where each XMM arg currently lives
    var xmmSources = new X86XmmRegister?[regArgCount];
    foreach (int i in xmmArgs)
      xmmSources[i] = EnsureInXmmRegister(args[i], block);

    PlaceXmmArgs(xmmArgs, xmmSources, regArgCount, block);

    // Snapshot where each GPR register arg currently lives (register or stack).
    // This avoids calling EnsureInRegister in a loop where each call
    // can evict the previous arg under high register pressure.
    var argSources = new X86Register?[regArgCount];
    int?[] argStackHomes = new int?[regArgCount];
    long?[] argConstants = new long?[regArgCount];
    foreach (int i in gprArgs) {
      var target64 = To64Bit(CallConvRegs[i]);
      if (_valueToRegister.TryGetValue(args[i], out var reg)) {
        var reg64 = To64Bit(reg);
        // Prefer loading from stack directly into the target register
        // instead of emitting a reg-reg mov from a non-target register
        if (reg64 != target64 && _valueStackHome.TryGetValue(args[i], out var disp))
          argStackHomes[i] = disp;
        else
          argSources[i] = reg64;
      } else if (_constantValues.TryGetValue(args[i], out var imm)) {
        argConstants[i] = imm;
      } else if (_valueStackHome.TryGetValue(args[i], out var disp)) {
        argStackHomes[i] = disp;
      } else {
        throw new InvalidOperationException($"RegisterManager: call arg %{args[i].Id} has no register, no constant, and no stack home");
      }
    }

    // For indirect calls, secure the callee in a non-arg register before
    // GPR placement overwrites arg registers.
    preGprPlacement?.Invoke();

    // Move GPR args to their target calling convention registers.
    // We track the actual current physical state of registers throughout
    // the placement process.
    var placed = new bool[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32) placed[i] = true;
    }

    // x86-64 calling convention always uses 64-bit registers for arguments.
    // Using 64-bit ensures pointer-sized values (like heap pointers stored
    // as i64) are not truncated by 32-bit mov instructions.
    var targetRegs = new X86Register[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      targetRegs[i] = To64Bit(CallConvRegs[i]);
    }

    // Pass 1: mark GPR args already in their target register,
    // and mark constants as placed (they'll be emitted in Pass 4)
    foreach (int i in gprArgs) {
      if (argConstants[i] != null)
        continue; // handled in Pass 4
      if (SamePhysicalRegister(argSources[i], targetRegs[i]))
        placed[i] = true;
    }

    // Pass 2: repeatedly place register/stack args whose target is free
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

    // Pass 3: resolve remaining conflicts with xchg (only register-register cycles)
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

    // Pass 4: place constant args last — they have no source register so can't
    // conflict, and placing them last avoids being clobbered by earlier moves
    foreach (int i in gprArgs) {
      if (placed[i]) continue;
      if (argConstants[i] is { } constVal) {
        EmitImmediateToRegister(targetRegs[i], constVal, block);
        placed[i] = true;
      }
    }

    block.AddOp(emitCallOp());

    // Clean up stack args
    if (stackArgBytes > 0)
      block.AddOp(new X86AddRegImmOp(X86Register.Rsp, stackArgBytes));

    // Caller-saved registers are clobbered by the call.
    InvalidateCallerSavedRegisters();

    if (result != null) {
      if (result is StdF64 or StdF32) {
        AssignXmm(X86XmmRegister.Xmm0, result);
      } else if (result is StdPtr) {
        // Pointers use 64-bit RAX
        Assign(X86Register.Rax, result);
      } else if (result is StdI64 or StdI32 or StdBool) {
        // All integer types use 32-bit EAX (even i64, for compatibility with existing codegen)
        Assign(X86Register.Eax, result);
      } else {
        throw new InvalidOperationException($"RegisterManager: unsupported result type {result.GetType().Name}");
      }
    }
  }

  /// <summary>
  /// Emit a function call, placing arguments in calling convention registers
  /// (and on the stack for 5th+ args), invalidating caller-saved registers,
  /// and recording the result (if any) in Eax.
  /// </summary>
  public void EmitCall(string callee, List<StdValue> args, StdValue? result, MlirBlock<X86Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    EmitCallShared(
      args, result, block,
      preGprPlacement: null,
      emitCallOp: () => new X86CallDirectOp(callee),
      consumedByCall: consumedByCall
    );
  }

  /// <summary>
  /// Emit a tail call: place arguments in registers, emit epilogue, then jmp.
  /// No caller-saved spilling or stack cleanup — the callee's ret will return
  /// directly to our caller.
  /// </summary>
  public void EmitTailCall(string callee, List<StdValue> args, MlirBlock<X86Op> block) {
    int regArgCount = Math.Min(args.Count, CallConvRegs.Length);

    // Ensure all args are materialized in registers before placement
    foreach (var arg in args) {
      if (arg is StdF64 or StdF32)
        EnsureInXmmRegister(arg, block);
      else
        EnsureInRegister(arg, block);
    }

    // Snapshot where each GPR arg currently lives
    var gprArgs = new List<int>();
    var xmmArgs = new List<int>();
    var argSources = new X86Register?[regArgCount];
    for (int i = 0; i < regArgCount; i++) {
      if (args[i] is StdF64 or StdF32) {
        xmmArgs.Add(i);
      } else {
        gprArgs.Add(i);
        argSources[i] = To64Bit(_valueToRegister[args[i]]);
      }
    }

    // Place float args into their target XMM registers using multi-pass
    var xmmSources = new X86XmmRegister?[regArgCount];
    foreach (int i in xmmArgs)
      xmmSources[i] = _valueToXmm[args[i]];

    PlaceXmmArgs(xmmArgs, xmmSources, regArgCount, block);

    var placed = new bool[regArgCount];
    foreach (int i in xmmArgs)
      placed[i] = true;

    var targetRegs = new X86Register[regArgCount];
    for (int i = 0; i < regArgCount; i++)
      targetRegs[i] = To64Bit(CallConvRegs[i]);

    // Pass 1: mark GPR args already in their target register
    foreach (int i in gprArgs) {
      if (SamePhysicalRegister(argSources[i], targetRegs[i]))
        placed[i] = true;
    }

    // Pass 2: place register args whose target is free (all args guaranteed in registers)
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

    // Pass 3: resolve remaining conflicts with xchg
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

    // Epilogue tears down the stack frame, then jmp reuses our caller's return address
    block.AddOp(new X86EpilogueOp());
    block.AddOp(new X86JmpOp(callee));
  }

  /// <summary>
  /// Emit a try-call: same as EmitCall but also captures RDX as the error flag.
  /// RDX must be captured before InvalidateCallerSavedRegisters clobbers it.
  /// </summary>
  public void EmitTryCall(string callee, List<StdValue> args, StdValue? result, StdValue errorFlag, MlirBlock<X86Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    EmitCall(callee, args, result, block, consumedByCall);
    // After EmitCall, the result is in EAX and RDX holds the error flag.
    // But InvalidateCallerSavedRegisters already ran inside EmitCall.
    // We need RDX to have been preserved. Since the call just happened and
    // Assign(Eax, result) doesn't touch RDX, RDX still holds the callee's value.
    // Re-assign it now.
    Assign(X86Register.Edx, errorFlag);
  }

  /// <summary>
  /// Emit a function reference (get address of a function).
  /// Uses LEA with RIP-relative addressing to get the function's address.
  /// </summary>
  public void EmitFuncRef(string functionName, StdValue result, MlirBlock<X86Op> block) {
    var reg = AllocateRegister(result, block);
    block.AddOp(new X86LeaFuncAddrOp(reg, functionName));
  }

  /// <summary>
  /// Emit an indirect call through a function pointer.
  /// </summary>
  public void EmitIndirectCall(StdValue callee, List<StdValue> args, StdValue? result, MlirBlock<X86Op> block,
      HashSet<StdValue>? consumedByCall = null) {
    X86Register calleeReg = default;
    EmitCallShared(
      args, result, block,
      preGprPlacement: () => {
        // Ensure callee is in a register not used for arg passing
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

  /// <summary>
  /// Record that a function parameter has arrived in a calling convention register,
  /// or load it from the stack for 5th+ parameters.
  /// After push rbp / mov rbp, rsp: [rbp+16] = 5th param, [rbp+24] = 6th, etc.
  /// </summary>
  // Internal calling convention: pass up to 8 float parameters in XMM registers
  private static readonly X86XmmRegister[] CallConvXmmRegs = [
    X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2, X86XmmRegister.Xmm3,
    X86XmmRegister.Xmm4, X86XmmRegister.Xmm5, X86XmmRegister.Xmm6, X86XmmRegister.Xmm7
  ];

  public void NoteParam(StdValue paramValue, int paramIndex, MlirBlock<X86Op> block) {
    if (paramIndex < CallConvRegs.Length) {
      if (paramValue is StdF64 or StdF32) {
        AssignXmm(CallConvXmmRegs[paramIndex], paramValue);
      } else if (paramValue is StdPtr or StdI64) {
        // Pointers and 64-bit integers use full 64-bit register
        Assign(CallConvRegs[paramIndex], paramValue);
      } else if (paramValue is StdI32) {
        // 32-bit integers use 32-bit register
        Assign(CallConvRegs[paramIndex], paramValue);
      } else if (paramValue is StdBool) {
        // Bool uses 8-bit but is passed in full register, just assign the register
        Assign(CallConvRegs[paramIndex], paramValue);
      } else {
        throw new InvalidOperationException($"RegisterManager: unsupported parameter type {paramValue.GetType().Name}");
      }
    } else {
      int stackOffset = 16 + (paramIndex - CallConvRegs.Length) * 8;
      if (paramValue is StdF64 or StdF32) {
        var xmm = AllocateXmmRegister(paramValue);
        // Both F64 and F32 are passed as 8-byte slots on the stack in our calling convention
        block.AddOp(new X86MovXmmMemOp(xmm, stackOffset, FloatPrecision.F64));
      } else if (paramValue is StdPtr or StdI64) {
        var gpr = AllocateRegister(paramValue, block);
        block.AddOp(new X86MovRegMemOp(gpr, stackOffset, sizeInBytes: 8));
      } else if (paramValue is StdI32) {
        var gpr = AllocateRegister(paramValue, block);
        block.AddOp(new X86MovRegMemOp(gpr, stackOffset, sizeInBytes: 4));
      } else if (paramValue is StdBool) {
        var gpr = AllocateRegister(paramValue, block);
        // Bool parameters from stack need 1-byte load (zero-extended)
        block.AddOp(new X86MovRegMemOp(gpr, stackOffset, sizeInBytes: 1));
      } else {
        throw new InvalidOperationException($"RegisterManager: unsupported parameter type {paramValue.GetType().Name}");
      }
    }
  }

  private static readonly X86Register[] CallerSavedRegisters = [
    X86Register.Eax, X86Register.Ecx, X86Register.Edx,
    X86Register.Ebx, X86Register.Esi, X86Register.Edi,
    X86Register.R8, X86Register.R9, X86Register.R10, X86Register.R11
  ];

  /// <summary>
  /// Before a call, spill any caller-saved register values that don't have
  /// a stack home yet. Values in consumedByCall are args whose last use is
  /// this call — they don't need spilling since they won't be read after.
  /// </summary>
  private void SpillCallerSavedRegisters(MlirBlock<X86Op> block, HashSet<StdValue>? consumedByCall = null) {
    foreach (var reg in CallerSavedRegisters) {
      if (_registerContents.TryGetValue(reg, out var value)
        && !_valueStackHome.ContainsKey(value)
        && !_constantValues.ContainsKey(value)
        && !(consumedByCall != null && consumedByCall.Contains(value))) {
        _nextSpillOffset -= 8;
        block.AddOp(new X86MovMemRegOp(_nextSpillOffset, reg, 8));
        _valueStackHome[value] = _nextSpillOffset;
      }
    }
    foreach (var xmm in CallerSavedXmmRegisters) {
      if (_xmmContents.TryGetValue(xmm, out var value)
        && !_valueXmmStackHome.ContainsKey(value)
        && !(consumedByCall != null && consumedByCall.Contains(value))) {
        _nextSpillOffset -= 8;
        block.AddOp(new X86MovMemXmmOp(_nextSpillOffset, xmm, FloatPrecision.F64));
        _valueXmmStackHome[value] = _nextSpillOffset;
      }
    }
  }

  // All XMM registers are caller-saved in our internal convention
  private static readonly X86XmmRegister[] CallerSavedXmmRegisters = [
    X86XmmRegister.Xmm0, X86XmmRegister.Xmm1, X86XmmRegister.Xmm2,
    X86XmmRegister.Xmm3, X86XmmRegister.Xmm4, X86XmmRegister.Xmm5,
    X86XmmRegister.Xmm6, X86XmmRegister.Xmm7
  ];

  /// <summary>
  /// After a call, caller-saved registers are clobbered. Remove any value mappings
  /// for those registers so stale values are never used.
  /// Windows x64 caller-saved: rax, rcx, rdx, r8, r9, r10, r11, xmm0-xmm5.
  /// </summary>
  private void Invalidate(X86Register reg) {
    if (_registerContents.TryGetValue(reg, out var value)) {
      _valueToRegister.Remove(value);
      _registerContents.Remove(reg);
      _lastUsed.Remove(reg);
    }
  }

  private void InvalidateCallerSavedRegisters() {
    foreach (var reg in CallerSavedRegisters) {
      if (_registerContents.TryGetValue(reg, out var value)) {
        _valueToRegister.Remove(value);
        _registerContents.Remove(reg);
        _lastUsed.Remove(reg);
      }
    }
    foreach (var xmm in CallerSavedXmmRegisters) {
      if (_xmmContents.TryGetValue(xmm, out var value)) {
        _valuePreviousXmm[value] = xmm;  // Remember which register it was in
        _valueToXmm.Remove(value);
        _xmmContents.Remove(xmm);
        _xmmLastUsed.Remove(xmm);
      }
    }
  }

  /// <summary>
  /// Record that a value has been stored to stack at the given displacement.
  /// </summary>
  private void NoteStoreToStack(StdValue value, int displacement) {
    _valueStackHome[value] = displacement;
  }

  /// <summary>
  /// Allocate an XMM register for a new float value.
  /// Prefers reusing the previous register if it's now free (optimization for post-call reloads).
  /// </summary>
  private X86XmmRegister AllocateXmmRegister(StdValue value) {
    // If this value had a previous register and it's now free, reuse it
    if (_valuePreviousXmm.TryGetValue(value, out var previousReg) && !_xmmContents.ContainsKey(previousReg)) {
      _valuePreviousXmm.Remove(value);  // Clear the hint
      AssignXmm(previousReg, value);
      return previousReg;
    }

    if (_nextFreshXmmIndex < XmmPool.Length) {
      var candidate = XmmPool[_nextFreshXmmIndex];
      if (!_xmmContents.ContainsKey(candidate)) {
        _nextFreshXmmIndex++;
        AssignXmm(candidate, value);
        return candidate;
      }
    }

    foreach (var reg in XmmPool) {
      if (!_xmmContents.ContainsKey(reg)) {
        AssignXmm(reg, value);
        return reg;
      }
    }

    throw new InvalidOperationException("RegisterManager: all XMM registers occupied");
  }

  /// <summary>
  /// Ensure a float value is in an XMM register, reloading from stack if needed.
  /// </summary>
  private X86XmmRegister EnsureInXmmRegister(StdValue value, MlirBlock<X86Op> block) {
    if (_valueToXmm.TryGetValue(value, out var reg)) {
      _xmmLastUsed[reg] = _currentOpIndex;
      return reg;
    }

    if (_valueXmmStackHome.TryGetValue(value, out var displacement)) {
      var reloadReg = AllocateXmmRegister(value);
      block.AddOp(new X86MovXmmMemOp(reloadReg, displacement, FloatPrecision.F64));
      return reloadReg;
    }

    throw new InvalidOperationException($"RegisterManager: float value %{value.Id} has no XMM register and no stack home");
  }

  /// <summary>
  /// Record that a float value has been stored to stack at the given displacement.
  /// </summary>
  private void NoteXmmStoreToStack(StdValue value, int displacement) {
    _valueXmmStackHome[value] = displacement;
  }

  // --- Struct support: LEA and indirect memory operations ---

  /// <summary>
  /// Emit LEA reg, [rbp + offset] to get the address of a variable on the stack.
  /// Used for sret pointer passing to callees.
  /// </summary>
  public void EmitLeaFromStack(StdPtr result, int offset, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaRegMemOp(To64Bit(gpr), offset));
  }

  /// <summary>
  /// Emit LEA reg, [rip + disp] to get the address of an rdata label.
  /// Used for constant array literals stored in .rdata.
  /// </summary>
  public void EmitLeaRipRelative(StdPtr result, string rdataLabel, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaRipRelOp(To64Bit(gpr), rdataLabel));
  }

  public void EmitLeaSymdataRelative(StdPtr result, string symdataLabel, MlirBlock<X86Op> block) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86LeaSymdataRelOp(To64Bit(gpr), symdataLabel));
  }

  /// <summary>
  /// Move one value to another (same register, type reinterpretation).
  /// Used for StdPtrToI64Op where the value is the same register.
  /// </summary>
  public void EmitMovValueToValue(StdValue input, StdValue result, MlirBlock<X86Op> block) {
    var srcReg = EnsureInRegister(input, block);
    var dstReg = AllocateRegister(result, block, protect1: srcReg);
    if (srcReg != dstReg)
      block.AddOp(new X86MovRegRegOp(To64Bit(dstReg), To64Bit(srcReg)));
  }

  /// <summary>
  /// Copy byteCount bytes from src to dst using rep movsb.
  /// Uses RSI (src), RDI (dst), RCX (count).
  /// </summary>
  public void EmitMemCopy(StdValue srcPtr, StdValue dstPtr, StdValue byteCount, MlirBlock<X86Op> block) {
    // Spill any live values occupying RSI/RDI/RCX that aren't memcopy
    // arguments, so they aren't silently clobbered during argument placement.
    var memCopyArgs = new HashSet<StdValue> { srcPtr, dstPtr, byteCount };
    SpillRegisterIfNotArg(X86Register.Esi, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Edi, memCopyArgs, block);
    SpillRegisterIfNotArg(X86Register.Ecx, memCopyArgs, block);

    // Load all three values into registers, avoiding the target fixed registers
    // to prevent conflicts during the subsequent parallel assignment.
    var srcReg = EnsureInRegister(srcPtr, block);
    var dstReg = EnsureInRegister(dstPtr, block, protect1: srcReg);
    var cntReg = EnsureInRegister(byteCount, block, protect1: srcReg, protect2: dstReg);

    // Perform a conflict-free parallel assignment to RSI, RDI, RCX.
    // The moves must be ordered so no target clobbers a source that hasn't
    // been read yet. We detect conflicts and use a safe ordering.
    var moves = new List<(X86Register target, X86Register source)>();
    if (!SamePhysReg(srcReg, X86Register.Esi))
      moves.Add((X86Register.Rsi, srcReg));
    if (!SamePhysReg(dstReg, X86Register.Edi))
      moves.Add((X86Register.Rdi, dstReg));
    if (!SamePhysReg(cntReg, X86Register.Ecx))
      moves.Add((X86Register.Rcx, cntReg));

    // Emit moves in an order that avoids clobbering: if a target overlaps a
    // later source, emit the later move first.
    var emitted = new HashSet<int>();
    for (int pass = 0; pass < moves.Count + 1 && emitted.Count < moves.Count; pass++) {
      for (int i = 0; i < moves.Count; i++) {
        if (emitted.Contains(i)) continue;
        // Check if this move's target clobbers any un-emitted source
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
    // If there's a cycle (e.g., A→B, B→A), break it with a temp via xchg or stack
    if (emitted.Count < moves.Count) {
      // Remaining moves form a cycle. Just spill to stack and reload.
      foreach (var i in Enumerable.Range(0, moves.Count).Except(emitted)) {
        var (target, source) = moves[i];
        _nextSpillOffset -= 8;
        block.AddOp(new X86MovMemRegOp(_nextSpillOffset, source, 8));
        block.AddOp(new X86MovRegMemOp(target, _nextSpillOffset, 8));
        emitted.Add(i);
      }
    }

    block.AddOp(new X86RepMovsbOp());
    // rep movsb clobbers RSI, RDI, RCX — invalidate them
    Invalidate(X86Register.Esi);
    Invalidate(X86Register.Edi);
    Invalidate(X86Register.Ecx);
  }

  public void EmitBulkZero(int baseOffset, int qwordCount, MlirBlock<X86Op> block) {
    SpillRegisterIfOccupied(X86Register.Eax, block);
    SpillRegisterIfOccupied(X86Register.Edi, block);
    SpillRegisterIfOccupied(X86Register.Ecx, block);
    block.AddOp(new X86LeaRegMemOp(X86Register.Rdi, baseOffset));
    block.AddOp(new X86XorRegRegOp(X86Register.Eax, X86Register.Eax));
    block.AddOp(new X86MovRegImmOp(X86Register.Ecx, qwordCount));
    block.AddOp(new X86RepStosqOp());
    Invalidate(X86Register.Eax);
    Invalidate(X86Register.Edi);
    Invalidate(X86Register.Ecx);
  }

  // Check if two register names refer to the same physical register
  // (e.g., Edi and Rdi, or Eax and Rax).
  private static bool SamePhysReg(X86Register a, X86Register b) {
    return a == b || To64Bit(a) == To64Bit(b);
  }

  private void SpillRegisterIfNotArg(X86Register reg, HashSet<StdValue> args, MlirBlock<X86Op> block) {
    if (_registerContents.TryGetValue(reg, out var value) && !args.Contains(value)) {
      SpillRegisterIfOccupied(reg, block);
    }
  }

  /// <summary>
  /// Emit store through a register pointer with an offset: MOV [baseReg + offset], srcReg
  /// Used for sret return writes.
  /// </summary>
  public void EmitStoreIndirect(StdValue value, StdValue basePtr, int fieldOffset, MlirType fieldType, MlirBlock<X86Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    if (fieldType == MlirType.F64) {
      var srcXmm = EnsureInXmmRegister(value, block);
      block.AddOp(new X86MovIndirectMemXmmOp(baseReg, fieldOffset, srcXmm, FloatPrecision.F64));
    } else if (fieldType == MlirType.I1 || fieldType == MlirType.I8) {
      var srcReg = EnsureInRegister(value, block, protect1: baseReg);
      block.AddOp(new X86MovByteIndirectRegOp(baseReg, fieldOffset, srcReg));
    } else if (fieldType == MlirType.I16 || fieldType == MlirType.U16) {
      var srcReg = EnsureInRegister(value, block, protect1: baseReg);
      block.AddOp(new X86MovWordIndirectRegOp(baseReg, fieldOffset, srcReg));
    } else if (fieldType == MlirType.I64 || fieldType == MlirType.Fn || fieldType is MlirUnionType || fieldType is MlirStructType) {
      // Struct fields are heap pointers (i64)
      var srcReg = EnsureInRegister(value, block, protect1: baseReg);
      block.AddOp(new X86MovIndirectMemRegOp(baseReg, fieldOffset, srcReg));
    } else {
      throw new InvalidOperationException($"EmitStoreIndirect: unhandled field type: {fieldType}");
    }
  }

  /// <summary>
  /// Emit load through a register pointer with an offset: MOV destReg, [baseReg + offset]
  /// </summary>
  public void EmitLoadIndirect(StdValue result, StdValue basePtr, int fieldOffset, MlirType fieldType, MlirBlock<X86Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    if (fieldType == MlirType.F64) {
      var destXmm = AllocateXmmRegister(result);
      block.AddOp(new X86MovXmmIndirectMemOp(destXmm, baseReg, fieldOffset, FloatPrecision.F64));
    } else if (fieldType == MlirType.I1 || fieldType == MlirType.I8) {
      var destGpr = AllocateRegister(result, block, protect1: baseReg);
      block.AddOp(new X86MovzxRegByteIndirectOp(destGpr, baseReg, fieldOffset));
    } else if (fieldType == MlirType.I16 || fieldType == MlirType.U16) {
      var destGpr = AllocateRegister(result, block, protect1: baseReg);
      block.AddOp(new X86MovzxRegWordIndirectOp(destGpr, baseReg, fieldOffset));
    } else if (fieldType == MlirType.I64 || fieldType == MlirType.Fn || fieldType is MlirUnionType || fieldType is MlirStructType) {
      // Struct fields are heap pointers (i64) — load the 64-bit pointer value
      var destGpr = AllocateRegister(result, block, protect1: baseReg);
      block.AddOp(new X86MovRegIndirectMemOp(destGpr, baseReg, fieldOffset));
    } else {
      throw new InvalidOperationException($"EmitLoadIndirect: unhandled field type: {fieldType}");
    }
  }


  /// Null-safe load: returns [basePtr + offset] if basePtr is non-null, else 0.
  public void EmitNullSafeLoadI64(StdI64 result, StdI64 basePtr, int fieldOffset, MlirBlock<X86Op> block) {
    var baseReg = EnsureInRegister(basePtr, block);
    var destReg = AllocateRegister(result, block, protect1: baseReg);
    block.AddOp(new X86XorRegRegOp(destReg, destReg));
    block.AddOp(new X86TestRegRegOp(baseReg, baseReg));
    var skipLabel = $"__nsload_{result.Id}";
    block.AddOp(new X86JccOp("z", skipLabel));
    block.AddOp(new X86MovRegIndirectMemOp(destReg, baseReg, fieldOffset));
    block.AddOp(new X86LabelDefOp(skipLabel));
  }

  // --- Global variable support: RIP-relative addressing ---

  /// <summary>
  /// Load a global integer/bool variable into a GPR register via RIP-relative addressing.
  /// </summary>
  public void EmitGlobalLoad(StdValue result, string globalName, MlirBlock<X86Op> block, int size = 8) {
    var gpr = AllocateRegister(result, block);
    block.AddOp(new X86GlobalLoadOp(globalName, gpr, size));
  }

  /// <summary>
  /// Store a GPR value to a global variable via RIP-relative addressing.
  /// </summary>
  public void EmitGlobalStore(StdValue value, string globalName, MlirBlock<X86Op> block, int size = 8) {
    var reg = EnsureInRegister(value, block);
    block.AddOp(new X86GlobalStoreOp(globalName, reg, size));
  }

  /// <summary>
  /// Load a global float variable into an XMM register via RIP-relative addressing.
  /// </summary>
  public void EmitXmmGlobalLoad(StdValue result, string globalName, MlirBlock<X86Op> block) {
    var xmmReg = AllocateXmmRegister(result);
    block.AddOp(new X86GlobalLoadXmmOp(globalName, xmmReg, FloatPrecision.F64));
  }

  /// <summary>
  /// Store an XMM value to a global float variable via RIP-relative addressing.
  /// </summary>
  public void EmitXmmGlobalStore(StdValue value, string globalName, MlirBlock<X86Op> block) {
    var srcXmm = EnsureInXmmRegister(value, block);
    block.AddOp(new X86GlobalStoreXmmOp(globalName, srcXmm, FloatPrecision.F64));
  }

  /// <summary>
  /// Load a global float32 variable into an XMM register via MOVSS RIP-relative addressing.
  /// </summary>
  public void EmitXmmGlobalLoadF32(StdValue result, string globalName, MlirBlock<X86Op> block) {
    var xmm = AllocateXmmRegister(result);
    block.AddOp(new X86GlobalLoadXmmOp(globalName, xmm, FloatPrecision.F32));
  }

  /// <summary>
  /// Store an XMM value to a global float32 variable via MOVSS RIP-relative addressing.
  /// </summary>
  public void EmitXmmGlobalStoreF32(StdValue value, string globalName, MlirBlock<X86Op> block) {
    var xmm = EnsureInXmmRegister(value, block);
    block.AddOp(new X86GlobalStoreXmmOp(globalName, xmm, FloatPrecision.F32));
  }

  /// <summary>
  /// Ensure a float value is in XMM0 for returning from a function.
  /// </summary>
  public void EnsureInXmm0ForReturn(StdValue value, MlirBlock<X86Op> block) {
    var xmmReg = EnsureInXmmRegister(value, block);
    if (xmmReg != X86XmmRegister.Xmm0) {
      block.AddOp(new X86MovXmmXmmOp(X86XmmRegister.Xmm0, xmmReg, FloatPrecision.F64));
    }
  }

  /// <summary>
  /// Mark a value as dead and free its register.
  /// </summary>
  public void NoteValueDead(StdValue value) {
    if (_valueToRegister.TryGetValue(value, out var reg)) {
      _valueToRegister.Remove(value);
      _registerContents.Remove(reg);
      _lastUsed.Remove(reg);
    }

    if (_valueToXmm.TryGetValue(value, out var xmmReg)) {
      _valueToXmm.Remove(value);
      _xmmContents.Remove(xmmReg);
      _xmmLastUsed.Remove(xmmReg);
    }
  }

  /// <summary>
  /// After an add/sub, the destination register now holds a new result value.
  /// The old value is replaced.
  /// </summary>
  private void TransferValue(StdValue oldValue, X86Register reg, StdValue newValue) {
    // Remove old value mapping if it points to this register
    if (_valueToRegister.TryGetValue(oldValue, out var oldReg) && oldReg == reg) {
      _valueToRegister.Remove(oldValue);
    }

    _registerContents[reg] = newValue;
    _valueToRegister[newValue] = reg;
    _lastUsed[reg] = _currentOpIndex;
  }

  public void AdvanceOp() {
    _currentOpIndex++;
  }

  /// <summary>
  /// Set the base offset for spill slot allocation.
  /// This should be the negative offset just past the last variable slot
  /// (e.g. -16 if variables occupy offsets -8 and -16).
  /// </summary>
  public void SetSpillBaseOffset(int offset) {
    _spillBaseOffset = offset;
    _nextSpillOffset = offset;
    _minSpillOffset = offset;
  }

  public void Reset() {
    _nextFreshIndex = 0;
    _registerContents.Clear();
    _valueToRegister.Clear();
    _valueStackHome.Clear();
    _lastUsed.Clear();
    _currentOpIndex = 0;

    _nextFreshXmmIndex = 0;
    _xmmContents.Clear();
    _valueToXmm.Clear();
    _valueXmmStackHome.Clear();
    _xmmLastUsed.Clear();
    _registerHints.Clear();

    if (_nextSpillOffset < _minSpillOffset)
      _minSpillOffset = _nextSpillOffset;
    _nextSpillOffset = _spillBaseOffset;
  }

  private void Assign(X86Register reg, StdValue value) {
    _registerContents[reg] = value;
    _valueToRegister[value] = reg;
    _lastUsed[reg] = _currentOpIndex;
  }

  /// <summary>
  /// Place XMM args into their calling convention target registers using a
  /// multi-pass algorithm that avoids overwriting args not yet placed.
  /// </summary>
  private static void PlaceXmmArgs(List<int> xmmArgs, X86XmmRegister?[] xmmSources, int regArgCount, MlirBlock<X86Op> block) {
    if (xmmArgs.Count == 0) return;

    var placed = new bool[regArgCount];

    // Pass 1: mark args already in their target register
    foreach (int i in xmmArgs) {
      if (xmmSources[i] == CallConvXmmRegs[i])
        placed[i] = true;
    }

    // Pass 2: place args whose target is not occupied by another unplaced arg
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

    // Pass 3: resolve remaining cycles using a temp XMM register.
    // Analogous to the GPR xchg pass but using a scratch register since
    // XMM registers don't have an xchg instruction.
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
      // Update the other arg's source to reflect the swap
      foreach (int j in xmmArgs) {
        if (j != i && !placed[j] && xmmSources[j] == CallConvXmmRegs[i]) {
          xmmSources[j] = xmmSources[i]!.Value;
          break;
        }
      }
      placed[i] = true;
    }
  }

  private static bool SamePhysicalRegister(X86Register? a, X86Register? b) {
    if (a == null || b == null) return false;
    return To64Bit(a.Value) == To64Bit(b.Value);
  }

  private static X86Register To64Bit(X86Register reg) => reg switch {
    X86Register.Eax => X86Register.Rax,
    X86Register.Ecx => X86Register.Rcx,
    X86Register.Edx => X86Register.Rdx,
    X86Register.Ebx => X86Register.Rbx,
    X86Register.Esp => X86Register.Rsp,
    X86Register.Ebp => X86Register.Rbp,
    X86Register.Esi => X86Register.Rsi,
    X86Register.Edi => X86Register.Rdi,
    _ => reg // R8-R15 and Rax-Rdi are already 64-bit
  };

  // Normalize a 64-bit register name to its 32-bit form used by the GprPool.
  private static X86Register To32Bit(X86Register reg) => reg switch {
    X86Register.Rax => X86Register.Eax,
    X86Register.Rcx => X86Register.Ecx,
    X86Register.Rdx => X86Register.Edx,
    X86Register.Rbx => X86Register.Ebx,
    X86Register.Rsp => X86Register.Esp,
    X86Register.Rbp => X86Register.Ebp,
    X86Register.Rsi => X86Register.Esi,
    X86Register.Rdi => X86Register.Edi,
    _ => reg // R8-R15 and 32-bit forms are already canonical
  };

  private void AssignXmm(X86XmmRegister reg, StdValue value) {
    _xmmContents[reg] = value;
    _valueToXmm[value] = reg;
    _xmmLastUsed[reg] = _currentOpIndex;
  }

  /// <summary>
  /// Find the least-recently-used register, optionally filtering to values with a stack home.
  /// Returns null if no eligible register is found.
  /// </summary>
  private X86Register? FindLruRegister(X86Register? protect1, X86Register? protect2,
      bool requireStackHome = false, bool requireConstant = false) {
    X86Register? bestReg = null;
    int bestLastUsed = int.MaxValue;

    foreach (var (reg, value) in _registerContents) {
      if (reg == protect1 || reg == protect2) continue;
      if (requireConstant && !_constantValues.ContainsKey(value)) continue;
      if (requireStackHome && !_valueStackHome.ContainsKey(value)) continue;
      var lastUsed = _lastUsed.GetValueOrDefault(reg, 0);
      if (lastUsed < bestLastUsed) {
        bestLastUsed = lastUsed;
        bestReg = reg;
      }
    }

    return bestReg;
  }

  /// <summary>
  /// Evict the least-recently-used register value.
  /// Prefers values that already have a stack home (no spill store needed).
  /// If no value has a stack home, allocates a spill slot and stores the value.
  /// Protected registers will not be evicted.
  /// </summary>
  private void Evict(MlirBlock<X86Op> block, X86Register? protect1 = null, X86Register? protect2 = null) {
    // Prefer evicting constants (rematerializable, no store needed),
    // then values with a stack home (no store needed), then anything else
    var bestReg = FindLruRegister(protect1, protect2, requireConstant: true)
      ?? FindLruRegister(protect1, protect2, requireStackHome: true)
      ?? FindLruRegister(protect1, protect2, requireStackHome: false);

    if (bestReg != null) {
      SpillRegisterIfOccupied(bestReg.Value, block);
      return;
    }

    throw new InvalidOperationException("RegisterManager: no registers to evict");
  }
}
