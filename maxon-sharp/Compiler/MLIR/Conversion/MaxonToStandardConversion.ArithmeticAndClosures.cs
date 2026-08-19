using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Conversion;

public static partial class MaxonToStandardConversion {
  private static StdI32 EnsureI32(StdValue value, IrBlock<StandardOp> block) {
    if (value is StdI32 i32) return i32;
    var truncOp = new StdTruncI64ToI32Op((StdI64)value);
    block.AddOp(truncOp);
    return truncOp.Result;
  }

  /// Extends an StdI32 to StdI64, or passes through if already StdI64.
  ///
  /// A `StdU32` is an i32 whose bits are UNSIGNED, so it decides its own extension — it zero-extends,
  /// and `signExtend` does not apply to it. That is folded in HERE rather than left to the caller:
  /// every call site used to spell the same `is StdU32 ? new StdI32(id) : value` dance beside its own
  /// `signExtend: value is not StdU32`, which was one fact written down four times — and a fifth
  /// caller that forgot it would not get a wrong answer but an `InvalidCastException` on the
  /// `(StdI32)` below.
  private static StdI64 EnsureI64(StdValue value, IrBlock<StandardOp> block, bool signExtend = true) {
    if (value is StdI64 i64) return i64;

    if (value is StdU32 u32) {
      var zeroExtOp = new StdExtI32ToI64Op(new StdI32(u32.Id), signExtend: false);
      block.AddOp(zeroExtOp);
      return zeroExtOp.Result;
    }

    var extOp = new StdExtI32ToI64Op((StdI32)value, signExtend);
    block.AddOp(extOp);
    return extOp.Result;
  }

  private static (StandardOp Op, StdValue Result) CreateSignedI32BinOp(
    MaxonBinOperator op, StdI32 lhs, StdI32 rhs) {
    StandardOp stdOp;
    StdValue result;
    switch (op) {
      case MaxonBinOperator.Add: { var o = new StdAddI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Sub: { var o = new StdSubI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Mul: { var o = new StdMulI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Div: { var o = new StdDivI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Mod: { var o = new StdRemI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Eq: { var o = new StdCmpI32Op("eq", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Ne: { var o = new StdCmpI32Op("ne", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Lt: { var o = new StdCmpI32Op("lt", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Gt: { var o = new StdCmpI32Op("gt", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Le: { var o = new StdCmpI32Op("le", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Ge: { var o = new StdCmpI32Op("ge", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitAnd: { var o = new StdAndI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitOr: { var o = new StdOrI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitXor: { var o = new StdXorI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      default: throw new InvalidOperationException($"Unsupported signed i32 binop: {op}");
    }
    return (stdOp, result);
  }

  private static (StandardOp Op, StdValue Result) CreateUnsignedI32BinOp(
    MaxonBinOperator op, StdI32 lhs, StdI32 rhs) {
    StandardOp stdOp;
    StdValue result;
    switch (op) {
      case MaxonBinOperator.Add: { var o = new StdAddI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Sub: { var o = new StdSubI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Mul: { var o = new StdMulI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Div: { var o = new StdDivU32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Mod: { var o = new StdRemU32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Eq: { var o = new StdCmpU32Op("eq", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Ne: { var o = new StdCmpU32Op("ne", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Lt: { var o = new StdCmpU32Op("ult", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Gt: { var o = new StdCmpU32Op("ugt", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Le: { var o = new StdCmpU32Op("ule", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Ge: { var o = new StdCmpU32Op("uge", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitAnd: { var o = new StdAndI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitOr: { var o = new StdOrI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitXor: { var o = new StdXorI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      default: throw new InvalidOperationException($"Unsupported unsigned i32 binop: {op}");
    }
    return (stdOp, result);
  }

  /// <summary>
  /// Creates an unsigned integer binary op. Add/Sub/Mul/Bitwise are identical to signed;
  /// only Div/Mod/Cmp use unsigned variants.
  /// </summary>
  private static (StandardOp Op, StdValue Result) CreateUnsignedIntBinOp(
    MaxonBinOperator op, StdI64 lhs, StdI64 rhs) {
    StandardOp stdOp;
    StdValue result;
    switch (op) {
      case MaxonBinOperator.Add: { var o = new StdAddI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Sub: { var o = new StdSubI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Mul: { var o = new StdMulI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Div: { var o = new StdDivU64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Mod: { var o = new StdRemU64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Eq: { var o = new StdCmpU64Op("eq", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Ne: { var o = new StdCmpU64Op("ne", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Lt: { var o = new StdCmpU64Op("ult", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Gt: { var o = new StdCmpU64Op("ugt", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Le: { var o = new StdCmpU64Op("ule", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Ge: { var o = new StdCmpU64Op("uge", lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitAnd: { var o = new StdAndI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitOr: { var o = new StdOrI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.BitXor: { var o = new StdXorI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      default: throw new InvalidOperationException($"Unsupported unsigned int binop: {op}");
    }
    return (stdOp, result);
  }

  private static readonly Dictionary<(MaxonBinOperator, MaxonValueKind), Func<StdValue, StdValue, (StandardOp Op, StdValue Result)>> BinOpFactories = new() {
  { (MaxonBinOperator.Add, MaxonValueKind.Integer), (l, r) => { var op = new StdAddI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Sub, MaxonValueKind.Integer), (l, r) => { var op = new StdSubI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Mul, MaxonValueKind.Integer), (l, r) => { var op = new StdMulI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Div, MaxonValueKind.Integer), (l, r) => { var op = new StdDivI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Mod, MaxonValueKind.Integer), (l, r) => { var op = new StdRemI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Add, MaxonValueKind.Float), (l, r) => { var op = new StdAddF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Sub, MaxonValueKind.Float), (l, r) => { var op = new StdSubF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Mul, MaxonValueKind.Float), (l, r) => { var op = new StdMulF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Div, MaxonValueKind.Float), (l, r) => { var op = new StdDivF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Eq, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("eq", (StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("ne", (StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Lt, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("lt", (StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Gt, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("gt", (StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Le, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("le", (StdF64)l, (StdF64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ge, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("ge", (StdF64)l, (StdF64)r); return (op, op.Result); } },
    // Float32 operations
    { (MaxonBinOperator.Add, MaxonValueKind.Float32), (l, r) => { var op = new StdAddF32Op((StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Sub, MaxonValueKind.Float32), (l, r) => { var op = new StdSubF32Op((StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Mul, MaxonValueKind.Float32), (l, r) => { var op = new StdMulF32Op((StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Div, MaxonValueKind.Float32), (l, r) => { var op = new StdDivF32Op((StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Eq, MaxonValueKind.Float32), (l, r) => { var op = new StdCmpF32Op("eq", (StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Float32), (l, r) => { var op = new StdCmpF32Op("ne", (StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Lt, MaxonValueKind.Float32), (l, r) => { var op = new StdCmpF32Op("lt", (StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Gt, MaxonValueKind.Float32), (l, r) => { var op = new StdCmpF32Op("gt", (StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Le, MaxonValueKind.Float32), (l, r) => { var op = new StdCmpF32Op("le", (StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ge, MaxonValueKind.Float32), (l, r) => { var op = new StdCmpF32Op("ge", (StdF32)l, (StdF32)r); return (op, op.Result); } },
  { (MaxonBinOperator.Eq, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("eq", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("ne", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Lt, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("lt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Gt, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("gt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Le, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("le", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ge, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("ge", (StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Bitwise operations (integer only)
    { (MaxonBinOperator.BitAnd, MaxonValueKind.Integer), (l, r) => { var op = new StdAndI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitOr, MaxonValueKind.Integer), (l, r) => { var op = new StdOrI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitXor, MaxonValueKind.Integer), (l, r) => { var op = new StdXorI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Byte operations (bytes are represented as I64 at standard level)
    { (MaxonBinOperator.Eq, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("eq", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("ne", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Lt, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("lt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Gt, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("gt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Le, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("le", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ge, MaxonValueKind.Byte), (l, r) => { var op = new StdCmpI64Op("ge", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Add, MaxonValueKind.Byte), (l, r) => { var op = new StdAddI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Sub, MaxonValueKind.Byte), (l, r) => { var op = new StdSubI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Short operations (shorts are represented as I64 at standard level)
    { (MaxonBinOperator.Eq, MaxonValueKind.Short), (l, r) => { var op = new StdCmpI64Op("eq", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Short), (l, r) => { var op = new StdCmpI64Op("ne", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Lt, MaxonValueKind.Short), (l, r) => { var op = new StdCmpI64Op("lt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Gt, MaxonValueKind.Short), (l, r) => { var op = new StdCmpI64Op("gt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Le, MaxonValueKind.Short), (l, r) => { var op = new StdCmpI64Op("le", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ge, MaxonValueKind.Short), (l, r) => { var op = new StdCmpI64Op("ge", (StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Add, MaxonValueKind.Short), (l, r) => { var op = new StdAddI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Sub, MaxonValueKind.Short), (l, r) => { var op = new StdSubI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Mul, MaxonValueKind.Short), (l, r) => { var op = new StdMulI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Div, MaxonValueKind.Short), (l, r) => { var op = new StdDivI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Mod, MaxonValueKind.Short), (l, r) => { var op = new StdRemI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitAnd, MaxonValueKind.Short), (l, r) => { var op = new StdAndI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitOr, MaxonValueKind.Short), (l, r) => { var op = new StdOrI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitXor, MaxonValueKind.Short), (l, r) => { var op = new StdXorI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Logical operations (bool)
    { (MaxonBinOperator.And, MaxonValueKind.Bool), (l, r) => { var op = new StdAndI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.Or, MaxonValueKind.Bool), (l, r) => { var op = new StdOrI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitXor, MaxonValueKind.Bool), (l, r) => { var op = new StdXorI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.Eq, MaxonValueKind.Bool), (l, r) => { var op = new StdCmpI1Op("eq", (StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Bool), (l, r) => { var op = new StdCmpI1Op("ne", (StdBool)l, (StdBool)r); return (op, op.Result); } },
  };

  // ============================================================================
  // Shifts
  // ============================================================================

  /// The count of `binOp` as a compile-time integer, or null when the compiler cannot see it.
  ///
  /// "A literal op" is this tier's whole constant view, and it is EXACTLY the parser's: the parser
  /// materializes every shift count it folds (see Parser.EmitShift), so a count it knows is a
  /// count that arrives here as a literal, and a count it does not know arrives here as anything
  /// else. That correspondence is what keeps the fold and the codegen from becoming two opinions.
  private static long? ShiftCountOf(MaxonBinOp binOp, Dictionary<MaxonValue, MaxonLiteralOp> literalMap) =>
    literalMap.TryGetValue(binOp.Rhs, out var lit)
      && lit.ValueKind is MaxonValueKind.Integer or MaxonValueKind.Short
      ? lit.IntValue
      : null;

  /// True iff this op is an integer shift — the ops <see cref="EmitShift"/> owns, and the ops that
  /// therefore never reach the width dispatch that would narrow them.
  private static bool IsIntegerShift(MaxonBinOp binOp) =>
    binOp.Operator is MaxonBinOperator.Shl or MaxonBinOperator.Shr
    && binOp.OperandKind is MaxonValueKind.Integer or MaxonValueKind.Short;

  /// ⭐ Build a shift — THE one place a Std-tier shift is emitted, and the runtime half of
  /// <see cref="ShiftSemantics"/>. Every integer shift comes here, whether or not its count is a
  /// constant, and the reason is the whole point of the routine:
  ///
  ///   **ONE WIDTH.** A shift is 64 bits (see ShiftSemantics' width bullet). Its operands are
  ///   widened to i64 and its result IS an i64, whatever ranged type the left operand carries.
  ///   There used to be a second path — a constant count in 0..63 fell through to the ordinary
  ///   width dispatch, which narrows an op to i32 when `OptimalType` says the operands fit — and it
  ///   truncated the shift's VALUE: `(0-8) shl 29` on an `int(-2^31 to 2^31-1)` answered **0**,
  ///   while the identical shift by a count the compiler could not see answered **-4294967296**.
  ///   The fold and the codegen were two opinions. The narrowing is gone, and with it the second
  ///   path: the FILL is the only thing a ranged type decides here.
  ///
  ///   **ONE SATURATION.** x64 masks a 64-bit shift's count to its low 6 bits, and arm64's
  ///   LSLV/ASRV do the same, so a count the compiler cannot see cannot be handed to the
  ///   instruction as written: `x shl 64` would compute `x shl 0` (x, UNCHANGED) and `x shl 100`
  ///   would compute `x shl 36`. Such a count is SATURATED first, by the rule
  ///   <see cref="ShiftSemantics.Eval"/> folds by:
  ///
  ///     • a shift that fills with ZEROS (`shl`, and an UNSIGNED `shr`) shifts every bit out, so
  ///       its out-of-range value is the constant **0** — a select on the RESULT.
  ///     • a shift that fills with the SIGN (a signed `shr`) leaves the sign behind, and `x sar 63`
  ///       already IS the sign — so its out-of-range value is a **clamp of the COUNT** to 63. No
  ///       select of the result is needed, and none is emitted.
  ///
  ///   The range test is UNSIGNED, which is why one compare covers both ends: a negative count is a
  ///   huge unsigned one, so it reads as out-of-range. It is belt-and-braces — the parser has
  ///   already emitted the panic Go requires for a negative count (Parser.EmitNegativeShiftCountCheck)
  ///   — but it means the expression is correct standing alone rather than by an invariant
  ///   established two passes away.
  ///
  /// A count the compiler CAN see and the instruction accepts as written pays none of the
  /// saturation: it gets the bare shift, and that is the only thing constant-count lowering still
  /// buys. Everything else about the two is identical, which is why they are one routine.
  private static StdI64 EmitShift(
    MaxonBinOp binOp, StdValue lhs, StdValue rhs,
    Dictionary<MaxonValue, MaxonLiteralOp> literalMap, IrBlock<StandardOp> block) {

    // ASKED, not restated — the same classifier the parser's fold reads (Parser.EmitShift), over
    // the same `OptimalType`, so the folded and the emitted path cannot come to different
    // conclusions about which way this shift fills.
    var kind = ShiftSemantics.KindOf(binOp.Operator, binOp.IsUnsigned);

    var lhs64 = EnsureI64(lhs, block, signExtend: !binOp.IsUnsigned);
    var count64 = EnsureI64(rhs, block, signExtend: true);

    var foldedCount = ShiftCountOf(binOp, literalMap);

    // `x shl 0` / `x shr 0` is `x`. Recognized HERE rather than among the algebraic identities,
    // because only here is the operand already widened: the identity used to hand back the left
    // operand at ITS width, which for a narrow ranged local was an i32 — a shift result that was
    // not 64 bits wide, which is exactly the invariant above.
    if (foldedCount == ShiftSemantics.MinShiftCount)
      return lhs64;

    if (foldedCount is { } count && ShiftSemantics.IsUnguardedCount(count))
      return EmitRawShift(lhs64, count64, kind, block);

    var widthConst = new StdConstI64Op(ShiftSemantics.ShiftCountBits);
    block.AddOp(widthConst);
    var countFits = new StdCmpU64Op("ult", count64, widthConst.Result);
    block.AddOp(countFits);

    // A sign-filling shift saturates by CLAMPING the count to 63 — `x sar 63` already IS the sign,
    // so no select of the result is needed. A zero-filling one saturates the RESULT to 0.
    if (!ShiftSemantics.ZeroFills(kind)) {
      var maxCount = new StdConstI64Op(ShiftSemantics.MaxUnguardedShiftCount);
      block.AddOp(maxCount);
      var clamped = new StdSelectI64Op(countFits.Result, count64, maxCount.Result);
      block.AddOp(clamped);
      return EmitRawShift(lhs64, clamped.Result, kind, block);
    }

    var rawShift = EmitRawShift(lhs64, count64, kind, block);
    var zero = new StdConstI64Op(0);
    block.AddOp(zero);
    var saturated = new StdSelectI64Op(countFits.Result, rawShift, zero.Result);
    block.AddOp(saturated);
    return saturated.Result;
  }

  /// The shift INSTRUCTION, at 64 bits, one per <see cref="ShiftSemantics.ShiftKind"/>. The count
  /// must already be one the hardware's mask is a no-op on — a folded count in 0..63, or one
  /// <see cref="EmitShift"/> has saturated.
  private static StdI64 EmitRawShift(
    StdI64 lhs, StdI64 count, ShiftSemantics.ShiftKind kind, IrBlock<StandardOp> block) {

    StdBinaryI64Op shift = kind switch {
      ShiftSemantics.ShiftKind.Left => new StdShlI64Op(lhs, count),
      ShiftSemantics.ShiftKind.ArithmeticRight => new StdShrI64Op(lhs, count),
      ShiftSemantics.ShiftKind.LogicalRight => new StdShrU64Op(lhs, count),
      _ => throw new InvalidOperationException($"EmitRawShift: unhandled ShiftKind {kind}"),
    };
    block.AddOp(shift);
    return shift.Result;
  }

  // ============================================================================
  // Algebraic identity optimization
  // ============================================================================

  /// <summary>
  /// Attempts to simplify a binary operation when one or both operands are known constants.
  /// Returns true if the identity was applied, with the result value set accordingly.
  /// When a new constant must be emitted (e.g. x*0=0), it is added to the block.
  /// </summary>
  private static bool TryAlgebraicIdentity(
    MaxonBinOp binOp,
    Dictionary<MaxonValue, MaxonLiteralOp> literalMap,
    Dictionary<MaxonValue, StdValue> valueMap,
    IrBlock<StandardOp> block,
    out StdValue result) {

    literalMap.TryGetValue(binOp.Lhs, out var lhsLit);
    literalMap.TryGetValue(binOp.Rhs, out var rhsLit);

    // No constants — nothing to optimize
    if (lhsLit == null && rhsLit == null) {
      result = null!;
      return false;
    }

    var lhsStd = valueMap[binOp.Lhs];
    var rhsStd = valueMap[binOp.Rhs];

    // Integer / Byte identities
    if (binOp.OperandKind is MaxonValueKind.Integer or MaxonValueKind.Byte or MaxonValueKind.Short) {
      long? lVal = lhsLit?.IntValue;
      long? rVal = rhsLit?.IntValue;

      switch (binOp.Operator) {
        case MaxonBinOperator.Add:
          if (rVal == 0) { result = lhsStd; return true; }
          if (lVal == 0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.Sub:
          if (rVal == 0) { result = lhsStd; return true; }
          break;
        case MaxonBinOperator.Mul:
          if (rVal == 1) { result = lhsStd; return true; }
          if (lVal == 1) { result = rhsStd; return true; }
          if (rVal == 0) { result = EmitConstI64(0, block); return true; }
          if (lVal == 0) { result = EmitConstI64(0, block); return true; }
          break;
        case MaxonBinOperator.Div:
          if (rVal == 1) { result = lhsStd; return true; }
          break;
        case MaxonBinOperator.Mod:
          if (rVal == 1) { result = EmitConstI64(0, block); return true; }
          break;
        case MaxonBinOperator.BitAnd:
          if (rVal == 0) { result = EmitConstI64(0, block); return true; }
          if (lVal == 0) { result = EmitConstI64(0, block); return true; }
          break;
        case MaxonBinOperator.BitOr:
          if (rVal == 0) { result = lhsStd; return true; }
          if (lVal == 0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.BitXor:
          if (rVal == 0) { result = lhsStd; return true; }
          if (lVal == 0) { result = rhsStd; return true; }
          break;
      }
    }

    // Float identities (safe subset — avoids signed-zero and NaN edge cases)
    if (binOp.OperandKind is MaxonValueKind.Float or MaxonValueKind.Float32) {
      double? lVal = lhsLit?.FloatValue;
      double? rVal = rhsLit?.FloatValue;

      switch (binOp.Operator) {
        case MaxonBinOperator.Mul:
          if (rVal == 1.0) { result = lhsStd; return true; }
          if (lVal == 1.0) { result = rhsStd; return true; }
          break;
        case MaxonBinOperator.Div:
          if (rVal == 1.0) { result = lhsStd; return true; }
          break;
      }
    }

    // Bool identities
    if (binOp.OperandKind == MaxonValueKind.Bool) {
      bool? lVal = lhsLit?.BoolValue;
      bool? rVal = rhsLit?.BoolValue;

      switch (binOp.Operator) {
        case MaxonBinOperator.And:
          if (rVal == true) { result = lhsStd; return true; }
          if (lVal == true) { result = rhsStd; return true; }
          if (rVal == false) { result = EmitConstI1(false, block); return true; }
          if (lVal == false) { result = EmitConstI1(false, block); return true; }
          break;
        case MaxonBinOperator.Or:
          if (rVal == false) { result = lhsStd; return true; }
          if (lVal == false) { result = rhsStd; return true; }
          if (rVal == true) { result = EmitConstI1(true, block); return true; }
          if (lVal == true) { result = EmitConstI1(true, block); return true; }
          break;
      }
    }

    result = null!;
    return false;
  }

  private static StdI64 EmitConstI64(long value, IrBlock<StandardOp> block) {
    var op = new StdConstI64Op(value);
    block.AddOp(op);
    return op.Result;
  }

  private static StdBool EmitConstI1(bool value, IrBlock<StandardOp> block) {
    var op = new StdConstI1Op(value);
    block.AddOp(op);
    return op.Result;
  }

  // ============================================================================
  // Function pointer operations
  // ============================================================================

  private static void LowerFunctionRef(
    MaxonFunctionRefOp fnRefOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap) {
    var refOp = new StdFuncRefOp(fnRefOp.FunctionName);
    block.AddOp(refOp);
    valueMap[fnRefOp.Result] = refOp.Result;
    // Non-capturing: no env_ptr stored. LowerIndirectCall will inline 0.
  }

  /// The closure environment's name, in the three places it is spelled: EmitAlloc's mm-trace tag,
  /// the env temp's registry entry, and the env slot's entry in the name -> struct-type map. It is
  /// a name the compiler mints for itself and NOT a declared type — no TypeDefs entry resolves it —
  /// which is why it never reaches EmitAlloc's typeName.
  private const string ClosureEnvName = "ClosureEnv";

  private static void LowerClosureCreate(
    MaxonClosureCreateOp closureOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> fnEnvVarNames,
    Dictionary<string, string> varNameToStructType,
    VarRegistry temps) {
    // Create function reference
    var refOp = new StdFuncRefOp(closureOp.FunctionName);
    block.AddOp(refOp);
    valueMap[closureOp.Result] = refOp.Result;

    // Allocate environment to hold captured values (each 8 bytes).
    //
    // The env is a SYNTHETIC allocation: it has no declared type, so it passes ClosureEnvName as
    // EmitAlloc's mm-trace TAG rather than as its typeName. A null typeName is the honest spelling
    // of "no declared type decides this block's destructor", and the NULL destructor it produces is
    // CORRECT here — the env holds the ADDRESSES of the captured variables (capture by reference),
    // which their own scopes own and free. Passing the name as a typeName would instead ASSERT that
    // TypeDefs resolves it, which is now a refusal rather than a silent null destructor.
    int envSize = closureOp.CapturedValues.Count * 8;
    var envPtr = EmitAlloc(block, envSize, typeName: null, tag: ClosureEnvName, scopeName: _currentFuncName);

    // Store the ADDRESS of each captured variable into the environment
    // so that closures capture by reference (reads see mutations after capture)
    for (int i = 0; i < closureOp.CapturedValues.Count; i++) {
      var capturedName = closureOp.CapturedNames[i];
      StdValue addressVal;

      if (_refParamPtrVars != null && _refParamPtrVars.TryGetValue(capturedName, out var refPtrName)) {
        // Variable is itself a ref param — forward the existing reference pointer
        addressVal = EmitLoad(block, refPtrName, varTypes);
      } else if (closureOp.CapturedKinds[i] == MaxonValueKind.Struct
             && valueMap.TryGetValue(closureOp.CapturedValues[i], out var ccSv) && ccSv is StdHeapPtr ccHp) {
        // Struct variable: take address of the slot holding the heap pointer
        var leaOp = new StdLeaOp(ccHp.VarName!);
        block.AddOp(leaOp);
        var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
        block.AddOp(ptrToI64);
        addressVal = ptrToI64.Result;
      } else {
        // Primitive/enum variable: take address of the variable's stack slot
        var leaOp = new StdLeaOp(capturedName);
        block.AddOp(leaOp);
        var ptrToI64 = new StdPtrToI64Op(leaOp.Result);
        block.AddOp(ptrToI64);
        addressVal = ptrToI64.Result;
      }

      block.AddOp(new StdStoreIndirectOp(addressVal, envPtr, i * 8, IrType.I64));
    }

    // Track the env_ptr for this closure and register for scope-end cleanup. The slot is named
    // BEFORE the store because EmitStore reads the map for its debug-info record, and only the
    // StdHeapPtr arm would have filled it — an untyped env would otherwise record its storage
    // width instead of what the slot actually holds.
    var envVarName = temps.CreateTemp("env", refOp.Result.Id, ClosureEnvName, OwnershipFlags.Orphan);
    varNameToStructType[envVarName] = ClosureEnvName;
    EmitStore(block, envPtr, envVarName, varTypes);
    EmitIncrefValue(block, envPtr, scopeName: _currentFuncName);
    fnEnvVarNames[refOp.Result.Id] = envVarName;
  }

  private static void LowerClosureEnvLoad(
    MaxonClosureEnvLoadOp envLoadOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    VarRegistry temps) {
    // Load the __env parameter (stored as a variable during function lowering)
    var envBasePtr = EmitLoad(block, "__env", varTypes);
    // Environment stores ADDRESSES, not values — load address then dereference
    var addrLoadOp = new StdLoadIndirectOp(envBasePtr, envLoadOp.Index * 8, IrType.I64);
    block.AddOp(addrLoadOp);

    // Dereference type must match the captured variable's original storage type
    var derefType = envLoadOp.ValueKind switch {
      MaxonValueKind.Float => IrType.F64,
      MaxonValueKind.Bool => IrType.I1,
      MaxonValueKind.Integer => IrType.I64,
      MaxonValueKind.Byte => IrType.I64,
      MaxonValueKind.Short => IrType.I64,
      MaxonValueKind.Struct => IrType.I64,
      MaxonValueKind.Enum => IrType.I64,
      MaxonValueKind.Function => IrType.I64,
      MaxonValueKind.Float32 => IrType.F32,
      MaxonValueKind.TypeParameter => throw new InvalidOperationException($"Cannot dereference captured type parameter '{envLoadOp.Name}'"),
      _ => throw new InvalidOperationException($"Unsupported kind for closure env deref: {envLoadOp.ValueKind}"),
    };
    var derefOp = new StdLoadIndirectOp(addrLoadOp.Result, 0, derefType);
    block.AddOp(derefOp);

    if (envLoadOp.ValueKind == MaxonValueKind.Struct) {
      // Struct captures: dereferenced value is the heap pointer — track it
      var structVarName = $"__capture_{envLoadOp.Name}";
      temps.RegisterTemp(structVarName, envLoadOp.StructTypeName ?? "unknown", OwnershipFlags.Borrowed);
      EmitStore(block, derefOp.Result, structVarName, varTypes);
      valueMap[envLoadOp.Result] = new StdHeapPtr(derefOp.Result.Id, envLoadOp.StructTypeName ?? "unknown", structVarName);
    } else {
      valueMap[envLoadOp.Result] = derefOp.Result;
    }
  }

  // Lower a function-typed parameter into its two-slot ABI form: the function
  // pointer in `flatIdx` and the hidden env pointer in `flatIdx + 1`. Callable
  // for both a MaxonFunctionParamOp and a generic MaxonParamOp that
  // monomorphized to a function kind (which shares MaxonFunctionPtr as its
  // result), so both routes allocate the env slot and keep trailing params'
  // flat indices aligned with the signature builder.
  private static void LowerFunctionParam(
    int paramIndex,
    string paramName,
    MaxonValue paramResult,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> fnEnvVarNames,
    Dictionary<int, StdI64> fnEnvDirectValues,
    Dictionary<int, int> paramFlatIndex) {
    int flatIdx = paramFlatIndex.GetValueOrDefault(paramIndex, paramIndex);
    var paramOp = new StdParamOp(flatIdx, paramName, new StdPtr(IrContext.Current.NextStdId()));
    block.AddOp(paramOp);
    valueMap[paramResult] = paramOp.Result;
    // Store function pointer to variable so it can be loaded later via StdLoadI64Op
    block.AddOp(new StdStorePtrOp((StdPtr)paramOp.Result, paramName));
    varTypes[paramName] = "ptr";
    // Receive the hidden env_ptr (next parameter slot)
    var envVarName = ClosureEnvSlotName(paramName);
    var envValue = new StdI64(IrContext.Current.NextStdId());
    var envParamOp = new StdParamOp(flatIdx + 1, envVarName, envValue);
    block.AddOp(envParamOp);
    EmitStore(block, envParamOp.Result, envVarName, varTypes);
    fnEnvVarNames[paramOp.Result.Id] = envVarName;
    fnEnvDirectValues[paramOp.Result.Id] = envValue;
  }

  /// <summary>
  /// The slot that carries the capture ENVIRONMENT of the function value held by
  /// <paramref name="varName"/>. Naming it here, once, is what lets a function value's pointer and
  /// its environment travel together: whoever BINDS the value writes this slot, and whoever READS
  /// the variable in another block finds it. Spelling the prefix at each site instead is how the
  /// two halves came apart — the binder wrote an environment only for a PARAMETER, and a local's
  /// reader dutifully looked up a slot nobody had created and passed 0.
  /// </summary>
  private static string ClosureEnvSlotName(string varName) => $"__env_{varName}";

  /// <summary>
  /// The capture environment belonging to the function value <paramref name="fnValueId"/>, or null
  /// when the value carries none (a plain function reference, which ignores the argument anyway).
  ///
  /// A direct value is preferred over a slot because a PARAMETER's environment arrives as an SSA
  /// value that is already correct in every block; a slot must be re-loaded where it is read.
  /// </summary>
  private static StdI64? ResolveClosureEnvPtr(
    int fnValueId,
    IrBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string>? fnEnvVarNames,
    Dictionary<int, StdI64>? fnEnvDirectValues) {
    if (fnEnvDirectValues != null && fnEnvDirectValues.TryGetValue(fnValueId, out var directEnvPtr))
      return directEnvPtr;

    if (fnEnvVarNames == null || !fnEnvVarNames.TryGetValue(fnValueId, out var envVarName)) return null;

    // Every environment slot is written as a raw pointer, so its load is always in the i64 family
    // (`EmitLoad` answers StdHeapPtr for a slot registered as managed, which derives from StdI64).
    return EmitLoad(block, envVarName, varTypes) as StdI64
      ?? throw new InvalidOperationException($"closure env slot '{envVarName}' is not pointer-typed");
  }

  /// <summary>
  /// The hidden environment ARGUMENT accompanying a function value that is being handed to a callee
  /// — as the callee of an indirect call, or as an argument to a direct one. Zero when the value
  /// carries no environment, which every callee tolerates because a function that captures nothing
  /// never reads the parameter.
  ///
  /// This is the ONLY place that answers "what environment travels with this function value?", and
  /// it is a deliberate consolidation. The answer was written out three times — once per call shape
  /// — and the copies disagreed: the argument path could not see an environment at all unless it was
  /// handed the map, and the TRY-call path was never handed it, so `try apply(f, ...)` passed 0 and
  /// nil-dereffed. A fact spelled once per caller is a fact that will differ per caller.
  /// </summary>
  private static StdI64 ResolveClosureEnvArg(
    int fnValueId,
    IrBlock<StandardOp> block,
    Dictionary<string, string> varTypes,
    Dictionary<int, string>? fnEnvVarNames,
    Dictionary<int, StdI64>? fnEnvDirectValues) {
    var envPtr = ResolveClosureEnvPtr(fnValueId, block, varTypes, fnEnvVarNames, fnEnvDirectValues);
    if (envPtr != null) return envPtr;

    var zeroConst = new StdConstI64Op(0);
    block.AddOp(zeroConst);

    return zeroConst.Result;
  }

  /// <summary>
  /// Binds <paramref name="envPtr"/> as the environment of the function value now held by
  /// <paramref name="varName"/>, so a read of that variable in ANOTHER block still reaches it.
  ///
  /// The binding TAKES A REFERENCE, and <see cref="ReleaseClosureEnvSlot"/> drops it when the
  /// binding's scope ends. Treating the slot as a borrowed alias of the `closure_create` temp
  /// instead — which is what this did first — is a USE-AFTER-FREE: every `maxon.scope_end` sweeps
  /// EVERY orphan temp in the function (`VarRegistry.OrphanTemps` is one flat per-function set with
  /// no scope attached), so the temp's reference is dropped by the FIRST scope_end reached, which
  /// for a closure bound outside a loop and called inside it is the loop body's — while the
  /// variable, and this slot, live on in the enclosing scope. Two iterations then read a freed
  /// block: the first still finds the old bytes intact, the next reads whatever reused them.
  ///
  /// The reference is what makes the binding's lifetime the env's lifetime, independently of
  /// whichever scope_end happens to sweep the temp first.
  ///
  /// <paramref name="ownedEnvSlots"/> records that THIS binding owns its slot's reference, and the
  /// distinction is load-bearing: <see cref="LowerFunctionParam"/> writes a slot of the same NAME
  /// for a function-typed parameter, but that one holds the CALLER's environment, which the callee
  /// borrows for the length of the call and must never incref, decref, or release.
  /// </summary>
  private static void BindClosureEnvSlot(
    IrBlock<StandardOp> block,
    StdI64 envPtr,
    string varName,
    Dictionary<string, string> varTypes,
    HashSet<string> ownedEnvSlots,
    string scopeName) {
    var slotName = ClosureEnvSlotName(varName);

    // Rebinding (`var f = <closure>` then `f = <other>`) drops the environment this binding still
    // holds. Guarded by ownership: on a function PARAMETER's slot the outgoing value belongs to the
    // caller, and releasing it here would free a live environment out from under them.
    if (ownedEnvSlots.Contains(varName)) {
      var outgoingEnv = (StdI64)EmitLoad(block, slotName, varTypes);
      EmitDecrefValueIfNonnull(block, outgoingEnv, scopeName: scopeName);
    }

    block.AddOp(new StdStoreI64Op(envPtr, slotName));
    varTypes[slotName] = "i64";
    EmitIncrefValueIfNonnull(block, envPtr, scopeName: scopeName);
    ownedEnvSlots.Add(varName);
  }

  /// <summary>
  /// Drops the reference <see cref="BindClosureEnvSlot"/> took, at the scope_end that cleans the
  /// BINDING — which is the one scope that knows the environment can no longer be reached, because
  /// the only thing that could reach it was the variable now going out of scope.
  ///
  /// Silent for anything this lowering does not own: a function parameter's identically-named slot
  /// (the caller's environment), and any variable that never held a closure.
  /// </summary>
  private static void ReleaseClosureEnvSlot(
    IrBlock<StandardOp> block,
    string varName,
    Dictionary<string, string> varTypes,
    HashSet<string> ownedEnvSlots,
    string scopeName) {
    if (!ownedEnvSlots.Contains(varName)) return;

    var slotName = ClosureEnvSlotName(varName);
    if (!varTypes.ContainsKey(slotName)) return;

    var envPtr = (StdI64)EmitLoad(block, slotName, varTypes);
    EmitDecrefValueIfNonnull(block, envPtr, scopeName: scopeName);

    // Zero the slot so a second scope_end over the same name cannot release it twice.
    var zeroOp = new StdConstI64Op(0);
    block.AddOp(zeroOp);
    block.AddOp(new StdStoreI64Op(zeroOp.Result, slotName));
  }

  private static void LowerFunctionVarRef(
    MaxonFunctionVarRefOp fnVarRefOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> fnEnvVarNames) {
    // Function pointers are stored as 8-byte integers (pointers)
    var loadOp = new StdLoadI64Op(fnVarRefOp.VarName);
    block.AddOp(loadOp);
    valueMap[fnVarRefOp.Result] = loadOp.Result;
    // Also load and track the associated env_ptr
    var srcEnvVarName = ClosureEnvSlotName(fnVarRefOp.VarName);
    if (varTypes.ContainsKey(srcEnvVarName)) {
      fnEnvVarNames[loadOp.Result.Id] = srcEnvVarName;
    }
  }

  private static void LowerIndirectCall(
    MaxonIndirectCallOp indirectCallOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<string, IrType> typeDefs,
    Dictionary<int, string> fnEnvVarNames,
    Dictionary<int, StdI64> fnEnvDirectValues,
    VarRegistry temps) {
    var calleeValue = valueMap[indirectCallOp.Callee];
    var newArgs = new List<StdValue>();

    for (int i = 0; i < indirectCallOp.Args.Count; i++) {
      var arg = indirectCallOp.Args[i];
      if (valueMap.TryGetValue(arg, out var argSv) && argSv is StdHeapPtr argHp) {
        // Struct args: pass heap pointer directly
        var heapPtr = EmitLoad(block, argHp.VarName!, varTypes);
        newArgs.Add(heapPtr);
      } else {
        newArgs.Add(valueMap[arg]);
      }
    }

    // Append hidden env_ptr argument for closure support
    newArgs.Add(ResolveClosureEnvArg(calleeValue.Id, block, varTypes, fnEnvVarNames, fnEnvDirectValues));

    // Which returns come back as an OWNED heap pointer, and so must land in a registered
    // temp slot — without one, scope-end cleanup has nothing to decref. A direct call
    // gets this right for both shapes (LowerCallCore's `calleeRetStructType` and
    // `calleeRetAssocEnum` branches); this path used to name only the struct one, so a
    // union with a payload, returned through a function value, leaked its box on EVERY
    // call. `MaxonValueKind.Enum` alone is NOT the test: a payload-free enum is a bare
    // ordinal, with nothing to release.
    var retEnumType = indirectCallOp.CalleeType.ReturnType as IrEnumType;
    var managedRetTypeName = indirectCallOp.ResultKind switch {
      MaxonValueKind.Struct when indirectCallOp.ResultStructTypeName != null
        && typeDefs.TryGetValue(indirectCallOp.ResultStructTypeName, out var retTypeDef)
        && retTypeDef is IrStructType => indirectCallOp.ResultStructTypeName,
      MaxonValueKind.Enum when retEnumType is { HasAssociatedValues: true } => retEnumType.Name,
      _ => null
    };

    StdValue? resultValue = null;
    string? sretVarName = null;
    if (managedRetTypeName != null) {
      // Managed return: the result is a heap pointer (i64). CallReturn marks it a MOVE —
      // the callee already handed over its reference, so an assignment from this temp must
      // not incref it, exactly as for a direct call.
      resultValue = new StdI64(IrContext.Current.NextStdId());
      var icallretId = IrContext.Current.NextId();
      sretVarName = temps.CreateTemp("icallret", icallretId, managedRetTypeName, OwnershipFlags.Orphan | OwnershipFlags.CallReturn);
    } else if (indirectCallOp.ResultKind != null) {
      resultValue = indirectCallOp.ResultKind switch {
        MaxonValueKind.Integer => new StdI64(IrContext.Current.NextStdId()),
        MaxonValueKind.Float => new StdF64(IrContext.Current.NextStdId()),
        MaxonValueKind.Float32 => new StdF32(IrContext.Current.NextStdId()),
        MaxonValueKind.Bool => new StdBool(IrContext.Current.NextStdId()),
        MaxonValueKind.Byte => new StdI64(IrContext.Current.NextStdId()),
        MaxonValueKind.Short => new StdI64(IrContext.Current.NextStdId()),
        MaxonValueKind.Enum => new StdI64(IrContext.Current.NextStdId()),
        MaxonValueKind.Function => new StdPtr(IrContext.Current.NextStdId()),
        MaxonValueKind.TypeParameter => new StdI64(IrContext.Current.NextStdId()),
        _ => throw new InvalidOperationException($"Unsupported result kind for indirect call: {indirectCallOp.ResultKind}")
      };
    }

    var callOp = new StdIndirectCallOp(calleeValue, newArgs, resultValue);
    block.AddOp(callOp);

    if (sretVarName != null && indirectCallOp.Result != null && callOp.Result != null) {
      // Managed return: store the heap pointer in the temp slot that owns it.
      EmitStore(block, callOp.Result, sretVarName, varTypes);
      valueMap[indirectCallOp.Result] = new StdHeapPtr(callOp.Result!.Id, managedRetTypeName!, sretVarName);
    } else if (indirectCallOp.Result != null && callOp.Result != null) {
      valueMap[indirectCallOp.Result] = callOp.Result;
    }
  }

  /// <summary>
  /// Maps MaxonValueKind to the IrType used for managed memory element access.
  /// Struct, Enum, and Function kinds are stored as pointers (I64).
  /// </summary>
  private static IrType GetManagedMemElementType(MaxonValueKind kind, string context) {
    return kind switch {
      MaxonValueKind.Integer => IrType.I64,
      MaxonValueKind.Float => IrType.F64,
      MaxonValueKind.Float32 => IrType.F32,
      MaxonValueKind.Byte => IrType.I8,
      MaxonValueKind.Short => IrType.I16,
      MaxonValueKind.Bool => IrType.I8,
      MaxonValueKind.Enum => IrType.I64,
      MaxonValueKind.Struct => IrType.I64, // struct references are pointers
      MaxonValueKind.Function => IrType.I64, // function pointers
      // A TypeParameter must never survive monomorphization. Defaulting it to i64 (as this
      // did) silently reads 8 bytes out of whatever slot the element actually occupies — a
      // Byte/Short/Bool/Float32 element would be miscompiled rather than rejected.
      MaxonValueKind.TypeParameter => throw new InvalidOperationException(
        $"{context}: element type parameter reached lowering unresolved — monomorphization "
        + "failed to bind it (see TypeSubstitution.ResolveManagedElement)"),
      _ => throw new InvalidOperationException($"{context}: unsupported element kind '{kind}'")
    };
  }
}
