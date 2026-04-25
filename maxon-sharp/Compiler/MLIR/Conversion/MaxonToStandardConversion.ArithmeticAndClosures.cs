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
  private static StdI64 EnsureI64(StdValue value, IrBlock<StandardOp> block, bool signExtend = true) {
    if (value is StdI64 i64) return i64;
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
      case MaxonBinOperator.Shl: { var o = new StdShlI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Shr: { var o = new StdShrI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
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
      case MaxonBinOperator.Shl: { var o = new StdShlI32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Shr: { var o = new StdShrU32Op(lhs, rhs); stdOp = o; result = o.Result; break; }
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
      case MaxonBinOperator.Shl: { var o = new StdShlI64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
      case MaxonBinOperator.Shr: { var o = new StdShrU64Op(lhs, rhs); stdOp = o; result = o.Result; break; }
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
  { (MaxonBinOperator.Shl, MaxonValueKind.Integer), (l, r) => { var op = new StdShlI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Shr, MaxonValueKind.Integer), (l, r) => { var op = new StdShrU64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
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
  { (MaxonBinOperator.Shl, MaxonValueKind.Short), (l, r) => { var op = new StdShlI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
  { (MaxonBinOperator.Shr, MaxonValueKind.Short), (l, r) => { var op = new StdShrU64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
    // Logical operations (bool)
    { (MaxonBinOperator.And, MaxonValueKind.Bool), (l, r) => { var op = new StdAndI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.Or, MaxonValueKind.Bool), (l, r) => { var op = new StdOrI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.BitXor, MaxonValueKind.Bool), (l, r) => { var op = new StdXorI1Op((StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.Eq, MaxonValueKind.Bool), (l, r) => { var op = new StdCmpI1Op("eq", (StdBool)l, (StdBool)r); return (op, op.Result); } },
  { (MaxonBinOperator.Ne, MaxonValueKind.Bool), (l, r) => { var op = new StdCmpI1Op("ne", (StdBool)l, (StdBool)r); return (op, op.Result); } },
  };

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
        case MaxonBinOperator.Shl:
        case MaxonBinOperator.Shr:
          if (rVal == 0) { result = lhsStd; return true; }
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

    // Allocate environment to hold captured values (each 8 bytes)
    int envSize = closureOp.CapturedValues.Count * 8;
    var envPtr = EmitAlloc(block, envSize, "ClosureEnv", scopeName: _currentFuncName);

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

    // Track the env_ptr for this closure and register for scope-end cleanup
    var envVarName = temps.CreateTemp("env", refOp.Result.Id, "ClosureEnv", OwnershipFlags.Orphan);
    EmitStore(block, envPtr, envVarName, varTypes);
    EmitIncrefValue(block, envPtr, scopeName: _currentFuncName);
    varNameToStructType[envVarName] = "ClosureEnv";
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

  private static void LowerFunctionParam(
    MaxonFunctionParamOp fnParamOp,
    IrBlock<StandardOp> block,
    Dictionary<MaxonValue, StdValue> valueMap,
    Dictionary<string, string> varTypes,
    Dictionary<int, string> fnEnvVarNames,
    Dictionary<int, StdValue> fnEnvDirectValues,
    Dictionary<int, int> paramFlatIndex) {
    int flatIdx = paramFlatIndex.GetValueOrDefault(fnParamOp.Index, fnParamOp.Index);
    var paramOp = new StdParamOp(flatIdx, fnParamOp.Name, new StdPtr(IrContext.Current.NextStdId()));
    block.AddOp(paramOp);
    valueMap[fnParamOp.Result] = paramOp.Result;
    // Store function pointer to variable so it can be loaded later via StdLoadI64Op
    block.AddOp(new StdStorePtrOp((StdPtr)paramOp.Result, fnParamOp.Name));
    varTypes[fnParamOp.Name] = "ptr";
    // Receive the hidden env_ptr (next parameter slot)
    var envVarName = $"__env_{fnParamOp.Name}";
    var envParamOp = new StdParamOp(flatIdx + 1, envVarName, new StdI64(IrContext.Current.NextStdId()));
    block.AddOp(envParamOp);
    EmitStore(block, envParamOp.Result, envVarName, varTypes);
    fnEnvVarNames[paramOp.Result.Id] = envVarName;
    fnEnvDirectValues[paramOp.Result.Id] = envParamOp.Result;
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
    var srcEnvVarName = $"__env_{fnVarRefOp.VarName}";
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
    Dictionary<int, StdValue> fnEnvDirectValues,
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
    if (fnEnvDirectValues.TryGetValue(calleeValue.Id, out var directEnvPtr)) {
      newArgs.Add(directEnvPtr);
    } else if (fnEnvVarNames.TryGetValue(calleeValue.Id, out var envVarName)) {
      var envPtr = EmitLoad(block, envVarName, varTypes);
      newArgs.Add(envPtr);
    } else {
      // No env tracked — pass 0 (no captures)
      var zeroConst = new StdConstI64Op(0);
      block.AddOp(zeroConst);
      newArgs.Add(zeroConst.Result);
    }

    StdValue? resultValue = null;
    string? sretVarName = null;
    if (indirectCallOp.ResultKind == MaxonValueKind.Struct && indirectCallOp.ResultStructTypeName != null
      && typeDefs.TryGetValue(indirectCallOp.ResultStructTypeName, out var retTypeDef) && retTypeDef is IrStructType) {
      // Struct return: result is a heap pointer (i64)
      resultValue = new StdI64(IrContext.Current.NextStdId());
      var icallretId = IrContext.Current.NextId();
      sretVarName = temps.CreateTemp("icallret", icallretId, indirectCallOp.ResultStructTypeName!, OwnershipFlags.Orphan);
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
      // Struct return: store heap pointer in named variable
      EmitStore(block, callOp.Result, sretVarName, varTypes);
      valueMap[indirectCallOp.Result] = new StdHeapPtr(callOp.Result!.Id, indirectCallOp.ResultStructTypeName ?? "unknown", sretVarName);
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
      MaxonValueKind.TypeParameter => IrType.I64, // unresolved type parameter, stored as i64
      _ => throw new InvalidOperationException($"{context}: unsupported element kind '{kind}'")
    };
  }
}
