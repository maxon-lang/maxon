using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Compiler.Ir.Dialects;

public enum StdBinaryOperator {
  Add, Sub, Mul,
  DivSigned, DivUnsigned,
  RemSigned, RemUnsigned,
  And, Or, Xor,
  Shl, ShrSigned, ShrUnsigned,
  Min, Max,
}

public abstract class StandardOp : IPrintableOp {
  public abstract string Mnemonic { get; }
  public virtual IReadOnlyList<string> PrintableResults => [];
  public virtual IReadOnlyList<string> PrintableOperands => [];
  public virtual IReadOnlyDictionary<string, IrAttribute> PrintableAttributes => new Dictionary<string, IrAttribute>();
  public abstract List<StdValue> ReadValues { get; }

  /// Returns the result ID if this op is pure (side-effect-free and safe to remove
  /// when its result is unused), or -1 if it has side effects.
  public abstract int PureResultId { get; }

  /// Returns the result ID of any value defined by this op, or -1 if it defines no value.
  /// Unlike PureResultId, this includes non-pure ops that produce results (e.g., calls, params).
  public virtual int AnyResultId => PureResultId;
}

public abstract class StdUnaryF64Op(StdF64 input) : StandardOp {
  public StdF64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public abstract class StdBinaryF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public abstract class StdUnaryF32Op(StdF32 input) : StandardOp {
  public StdF32 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public abstract class StdBinaryF32Op(StdF32 lhs, StdF32 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdF32 Lhs { get; } = lhs;
  public StdF32 Rhs { get; } = rhs;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public interface IStoreOp {
  string VarName { get; }
  StdValue Value { get; }
  IrType StoredType { get; }
}

public interface ILoadOp {
  string VarName { get; }
  StdValue Result { get; }
}

// === Integer Constants ===

public class StdConstI64Op(long value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(Value, IrType.I64) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdConstI32Op(long value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(Value, IrType.I32) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Float Constants ===

public class StdConstF64Op(double value) : StandardOp {
  public override string Mnemonic => "arith.float_constant";
  public double Value { get; } = value;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new FloatAttr(Value, IrType.F64) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdConstF32Op(float value) : StandardOp {
  public override string Mnemonic => "arith.float_constant";
  public float Value { get; } = value;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new FloatAttr(Value, IrType.F32) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Bool Constants ===

public class StdConstI1Op(bool value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public bool Value { get; } = value;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(Value ? 1 : 0, IrType.I1) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Integer Arithmetic ===

public abstract class StdBinaryI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdAddI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.addi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public class StdSubI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.subi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public abstract class StdBinaryI32Op(StdI32 lhs, StdI32 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdAddI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.addi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public class StdSubI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.subi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public class StdMulI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.muli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public class StdDivI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.divsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

public class StdDivU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.divui";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivUnsigned;
}

public class StdRemI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.remsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemSigned;
}

public class StdRemU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.remui";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemUnsigned;
}

// === I32 Bitwise Operations ===

public class StdAndI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.andi";
  public override StdBinaryOperator Operator => StdBinaryOperator.And;
}

public class StdOrI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.ori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Or;
}

public class StdXorI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.xori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Xor;
}

public class StdShlI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.shli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Shl;
}

public class StdShrI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrSigned;
}

public class StdShrU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrui";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrUnsigned;
}

// === I32 Comparison ===

public class StdCmpI32Op(string predicate, StdI32 lhs, StdI32 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpi {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdCmpU32Op(string predicate, StdI32 lhs, StdI32 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpui {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

// === I32 Width Conversion ===

public class StdExtI32ToI64Op(StdI32 input, bool signExtend = true) : StandardOp {
  public override string Mnemonic => SignExtend ? "arith.extsi" : "arith.extui";
  public StdI32 Input { get; } = input;
  public bool SignExtend { get; } = signExtend;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdTruncI64ToI32Op(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.trunci";
  public StdI64 Input { get; } = input;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === I32 Float Conversion ===

public class StdSiToFpI32Op(StdI32 input) : StandardOp {
  public override string Mnemonic => "arith.sitofp";
  public StdI32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdUiToFpI32Op(StdI32 input) : StandardOp {
  public override string Mnemonic => "arith.uitofp";
  public StdI32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === I32 Memory Operations ===

public class StdStoreI32Op(StdI32 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdI32 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I32;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadI32Op(string varName) : StandardOp, ILoadOp {
  public override string Mnemonic => $"memref.load {VarName} : i32";
  public string VarName { get; } = varName;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdRemI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.remsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemSigned;
}

public class StdMulI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.muli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public class StdDivI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.divsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

public class StdDivU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.divui";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivUnsigned;
}

public class StdRemU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.remui";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemUnsigned;
}

// === Bitwise Integer Operations ===

public class StdAndI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.andi";
  public override StdBinaryOperator Operator => StdBinaryOperator.And;
}

public class StdOrI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.ori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Or;
}

public class StdXorI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.xori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Xor;
}

public class StdShlI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.shli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Shl;
}

public class StdShrI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrSigned;
}

public class StdShrU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrui";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrUnsigned;
}

// === Float Arithmetic ===

public class StdAddF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "arith.addf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public class StdSubF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "arith.subf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public class StdMulF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "arith.mulf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public class StdDivF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "arith.divf";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

// === F32 Float Arithmetic ===

public class StdAddF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override string Mnemonic => "arith.addf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public class StdSubF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override string Mnemonic => "arith.subf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public class StdMulF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override string Mnemonic => "arith.mulf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public class StdDivF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override string Mnemonic => "arith.divf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

// === Float Absolute Value ===

public class StdAbsF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override string Mnemonic => "math.absf";
}

// === Float Math Operations ===

public class StdSqrtF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override string Mnemonic => "math.sqrt";
}

public class StdFloorF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override string Mnemonic => "math.floor";
}

public class StdCeilF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override string Mnemonic => "math.ceil";
}

public class StdRoundF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override string Mnemonic => "math.round";
}

public class StdMinF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "math.minf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Min;
}

public class StdMaxF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "math.maxf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Max;
}

// === F32 Float Math Operations ===

public class StdAbsF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override string Mnemonic => "math.absf32";
}

public class StdSqrtF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override string Mnemonic => "math.sqrtf32";
}

public class StdFloorF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override string Mnemonic => "math.floorf32";
}

public class StdCeilF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override string Mnemonic => "math.ceilf32";
}

public class StdRoundF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override string Mnemonic => "math.roundf32";
}

public class StdMinF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override string Mnemonic => "math.minf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Min;
}

public class StdMaxF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override string Mnemonic => "math.maxf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Max;
}

// === Float-to-Int Conversion ===

public class StdFpToSiOp(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.fptosi";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdFpToUiOp(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.fptoui";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Float Bitcast (reinterpret bits) ===

public class StdBitcastF64ToI64Op(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.bitcast_f64_to_i64";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Int-to-Float Conversion ===

public class StdSiToFpOp(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.sitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdUiToFpOp(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.uitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === F32 Float-to-Int Conversion ===

public class StdFpToSiF32Op(StdF32 input) : StandardOp {
  public override string Mnemonic => "arith.fptosi_f32";
  public StdF32 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdFpToUiF32Op(StdF32 input) : StandardOp {
  public override string Mnemonic => "arith.fptoui_f32";
  public StdF32 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === F32 Int-to-Float Conversion ===

public class StdSiToFpF32Op(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.sitofp_f32";
  public StdI64 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdUiToFpF32Op(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.uitofp_f32";
  public StdI64 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === F64/F32 Precision Conversion ===

public class StdF64ToF32Op(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.truncf_f64_to_f32";
  public StdF64 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdF32ToF64Op(StdF32 input) : StandardOp {
  public override string Mnemonic => "arith.extf_f32_to_f64";
  public StdF32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Comparison ===

public class StdCmpI64Op(string predicate, StdI64 lhs, StdI64 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpi {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdCmpU64Op(string predicate, StdI64 lhs, StdI64 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpui {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdCmpF64Op(string predicate, StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpf {Predicate}";
  public string Predicate { get; } = predicate;
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdCmpF32Op(string predicate, StdF32 lhs, StdF32 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpf32 {Predicate}";
  public string Predicate { get; } = predicate;
  public StdF32 Lhs { get; } = lhs;
  public StdF32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdCmpI1Op(string predicate, StdBool lhs, StdBool rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpi1 {Predicate}";
  public string Predicate { get; } = predicate;
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

// === Conditional Select ===

/// Selects between two i64 values based on a boolean condition.
/// If condition is true, result = trueValue; otherwise result = falseValue.
public class StdSelectI64Op(StdBool condition, StdI64 trueValue, StdI64 falseValue) : StandardOp {
  public override string Mnemonic => "arith.select";
  public StdBool Condition { get; } = condition;
  public StdI64 TrueValue { get; } = trueValue;
  public StdI64 FalseValue { get; } = falseValue;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Condition.ToString(), TrueValue.ToString(), FalseValue.ToString()];
  public override List<StdValue> ReadValues => [Condition, TrueValue, FalseValue];
  public override int PureResultId => Result.Id;
}

// === Bool Logical Operations ===

public abstract class StdBinaryI1Op(StdBool lhs, StdBool rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdAndI1Op(StdBool lhs, StdBool rhs) : StdBinaryI1Op(lhs, rhs) {
  public override string Mnemonic => "arith.andi1";
  public override StdBinaryOperator Operator => StdBinaryOperator.And;
}

public class StdOrI1Op(StdBool lhs, StdBool rhs) : StdBinaryI1Op(lhs, rhs) {
  public override string Mnemonic => "arith.ori1";
  public override StdBinaryOperator Operator => StdBinaryOperator.Or;
}

public class StdXorI1Op(StdBool lhs, StdBool rhs) : StdBinaryI1Op(lhs, rhs) {
  public override string Mnemonic => "arith.xori1";
  public override StdBinaryOperator Operator => StdBinaryOperator.Xor;
}

// === Memory Operations ===

public class StdStoreI64Op(StdI64 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdI64 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I64;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdStoreF64Op(StdF64 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdF64 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.F64;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadI64Op(string varName) : StandardOp, ILoadOp {
  public override string Mnemonic => $"memref.load {VarName} : i64";
  public string VarName { get; } = varName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdStoreI1Op(StdBool value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdBool Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I1;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadI1Op(string varName) : StandardOp, ILoadOp {
  public override string Mnemonic => $"memref.load {VarName} : i1";
  public string VarName { get; } = varName;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdLoadF64Op(string varName) : StandardOp, ILoadOp {
  public override string Mnemonic => $"memref.load {VarName} : f64";
  public string VarName { get; } = varName;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdStoreF32Op(StdF32 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdF32 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.F32;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadF32Op(string varName) : StandardOp, ILoadOp {
  public override string Mnemonic => $"memref.load {VarName} : f32";
  public string VarName { get; } = varName;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdStorePtrOp(StdPtr value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdPtr Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I64; // Function pointers are 64-bit
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadPtrOp(string varName) : StandardOp, ILoadOp {
  public override string Mnemonic => $"memref.load {VarName} : ptr";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Control Flow ===

public class StdCondBrOp(StdBool condition, string thenBlock, string elseBlock) : StandardOp {
  public override string Mnemonic => $"cf.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
  public StdBool Condition { get; } = condition;
  public string ThenBlock { get; } = thenBlock;
  public string ElseBlock { get; } = elseBlock;
  public override List<StdValue> ReadValues => [Condition];
  public override int PureResultId => -1;
}

public class StdBrOp(string target) : StandardOp {
  public override string Mnemonic => $"cf.br {Target}";
  public string Target { get; } = target;
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
}

public class StdSwitchOp(StdI64 scrutinee, string[] caseTargets, string defaultTarget) : StandardOp {
  public override string Mnemonic => $"cf.switch %{Scrutinee.Id} [{CaseTargets.Length} cases] default={DefaultTarget}";
  public StdI64 Scrutinee { get; } = scrutinee;
  public string[] CaseTargets { get; } = caseTargets;
  public string DefaultTarget { get; } = defaultTarget;
  public override List<StdValue> ReadValues => [Scrutinee];
  public override int PureResultId => -1;
}

// === Function Operations ===

public class StdParamOp(int index, string name, StdValue result) : StandardOp {
  public override string Mnemonic => $"func.param {Name} : {Result.GetType().Name}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public StdValue Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public override int AnyResultId => Result.Id;
}

public class StdCallOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override string Mnemonic => $"func.call @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => Args;
  public override int PureResultId => -1;
  public override int AnyResultId => Result?.Id ?? -1;
}

// Gets the address of a function (function reference/pointer)
public class StdFuncRefOp(string functionName) : StandardOp {
  public override string Mnemonic => $"func.ref @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Calls a function indirectly through a function pointer
public class StdIndirectCallOp(StdValue callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override string Mnemonic => "func.indirect_call";
  public StdValue Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands =>
    [Callee.ToString(), .. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => [Callee, .. Args];
  public override int PureResultId => -1;
  public override int AnyResultId => Result?.Id ?? -1;
}

public class StdReturnOp(StdValue? value = null) : StandardOp {
  public override string Mnemonic => "func.return";
  public StdValue? ReturnValue { get; } = value;
  public override IReadOnlyList<string> PrintableOperands =>
    ReturnValue != null ? [ReturnValue.ToString()] : [];
  public override List<StdValue> ReadValues => ReturnValue != null ? [ReturnValue] : [];
  public override int PureResultId => -1;
}

// === Error handling operations ===

// Returns from a function with an error flag set (non-zero error ordinal in RDX, dummy value in RAX)
public class StdErrorReturnOp(StdValue errorFlag) : StandardOp {
  public override string Mnemonic => "func.error_return";
  public StdValue ErrorFlag { get; } = errorFlag;
  public override IReadOnlyList<string> PrintableOperands => [ErrorFlag.ToString()];
  public override List<StdValue> ReadValues => [ErrorFlag];
  public override int PureResultId => -1;
}

// Calls a throwing function and captures both the result (RAX) and error flag (RDX)
public class StdTryCallOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override string Mnemonic => $"func.try_call @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public StdI64 ErrorFlag { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString(), ErrorFlag.ToString()] : [ErrorFlag.ToString()];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => Args;
  public override int PureResultId => -1;
  public override int AnyResultId => Result?.Id ?? ErrorFlag.Id;
}

// === Struct pointer operations (for sret convention) ===

// Gets the stack address of a named variable (for passing struct by pointer)
public class StdLeaOp(string varName) : StandardOp {
  public override string Mnemonic => $"memref.lea {VarName}";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Address escape — removing would change semantics
}

// Bulk zero-initialize N contiguous qwords at a tagged stack region.
// Fields are named {Tag}.0 through {Tag}.{QwordCount-1}.
public class StdBulkZeroOp(string tag, int qwordCount, bool zeroInit = true) : StandardOp {
  public override string Mnemonic => $"memref.bulk_zero {Tag}, {QwordCount}";
  public string Tag { get; } = tag;
  public int QwordCount { get; } = qwordCount;
  public bool ZeroInit { get; } = zeroInit;
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public IEnumerable<string> FieldNames() {
    for (int i = 0; i < QwordCount; i++)
      yield return $"{Tag}.{i}";
  }
}

// Gets the address of an rdata label via RIP-relative addressing (for constant data in .rdata)
public class StdLeaRdataOp(string rdataLabel) : StandardOp {
  public override string Mnemonic => $"memref.lea_rdata {RdataLabel}";
  public string RdataLabel { get; } = rdataLabel;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Gets the address of a symdata label via RIP-relative addressing (for panic messages in .symtab)
public class StdLeaSymdataOp(string symdataLabel) : StandardOp {
  public override string Mnemonic => $"memref.lea_symdata {SymdataLabel}";
  public string SymdataLabel { get; } = symdataLabel;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Gets the address of a ucddata label via RIP-relative addressing (for Unicode Character Database tables in .ucd)
public class StdLeaUcddataOp(string ucddataLabel) : StandardOp {
  public override string Mnemonic => $"memref.lea_ucddata {UcddataLabel}";
  public string UcddataLabel { get; } = ucddataLabel;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Store a value through a pointer at a given offset (for sret writes)
public class StdStoreIndirectOp(StdValue value, StdValue basePtr, int fieldOffset, IrType fieldType) : StandardOp {
  public override string Mnemonic => $"memref.store_indirect %{Value.Id}, %{BasePtr.Id}+{FieldOffset}";
  public StdValue Value { get; } = value;
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public IrType FieldType { get; } = fieldType;
  public override List<StdValue> ReadValues => [Value, BasePtr];
  public override int PureResultId => -1;
}

// Load a value through a pointer at a given offset (for reading sret results)
public class StdLoadIndirectOp(StdValue basePtr, int fieldOffset, IrType fieldType) : StandardOp {
  public override string Mnemonic => $"memref.load_indirect %{BasePtr.Id}+{FieldOffset}";
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public IrType FieldType { get; } = fieldType;
  public StdValue Result { get; } = StdValueFactory.CreateStdValueForType(fieldType);

  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [BasePtr];
  public override int PureResultId => Result.Id;
}

/// Null-safe load: returns [basePtr + fieldOffset] if basePtr != null, else returns 0.
public class StdNullSafeLoadI64Op(StdI64 basePtr, int fieldOffset) : StandardOp {
  public override string Mnemonic => $"memref.null_safe_load %{BasePtr.Id}+{FieldOffset}";
  public StdI64 BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [BasePtr];
  public override int PureResultId => Result.Id;
}

public static class StdValueFactory {
  public static StdValue CreateStdValueForType(IrType type) {
    if (type == IrType.F32) return new StdF32(IrContext.Current.NextId());
    if (type == IrType.F64) return new StdF64(IrContext.Current.NextId());
    if (type == IrType.I1) return new StdBool(IrContext.Current.NextId());
    if (type == IrType.I8) return new StdI64(IrContext.Current.NextId());
    if (type == IrType.U8) return new StdI64(IrContext.Current.NextId());
    if (type == IrType.I16) return new StdI64(IrContext.Current.NextId());
    if (type == IrType.U16) return new StdI64(IrContext.Current.NextId());
    if (type == IrType.I32) return new StdI32(IrContext.Current.NextId());
    if (type == IrType.U32) return new StdU32(IrContext.Current.NextId());
    if (type == IrType.I64) return new StdI64(IrContext.Current.NextId());
    if (type == IrType.U64) return new StdI64(IrContext.Current.NextId());
    if (type is IrEnumType) return new StdI64(IrContext.Current.NextId());
    if (type is IrStructType) return new StdI64(IrContext.Current.NextId());
    if (type is IrRangedPrimitiveType rpt) return CreateStdValueForType(rpt.OptimalType);
    throw new InvalidOperationException($"Cannot create StdValue for type: {type}");
  }
}


// ============================================================================
// Global variable operations
// ============================================================================

public class StdGlobalLoadI64Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public class StdGlobalLoadF64Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_f64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public class StdGlobalLoadF32Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_f32 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public class StdGlobalLoadI1Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i1 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public class StdGlobalStoreI64Op(StdI64 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i64 @{GlobalName}";
  public StdI64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdGlobalStoreF64Op(StdF64 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_f64 @{GlobalName}";
  public StdF64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdGlobalStoreF32Op(StdF32 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_f32 @{GlobalName}";
  public StdF32 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdGlobalStoreI1Op(StdBool value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i1 @{GlobalName}";
  public StdBool Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdGlobalLoadI8Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i8 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public override int AnyResultId => Result.Id;
}

public class StdGlobalStoreI8Op(StdI64 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i8 @{GlobalName}";
  public StdI64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdGlobalLoadI16Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i16 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public override int AnyResultId => Result.Id;
}

public class StdGlobalStoreI16Op(StdI64 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i16 @{GlobalName}";
  public StdI64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

// ============================================================================
// Runtime call operations (for heap allocation via maxon_alloc/maxon_realloc/maxon_free)
// ============================================================================

public class StdCallRuntimeOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override string Mnemonic => $"std.call_runtime @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => Args;
  public override int PureResultId => -1;
  public override int AnyResultId => Result?.Id ?? -1;
}

/// Calls a runtime function that returns both a result (RAX) and an error flag (RDX).
/// Used for __gt_try_await which returns the async result and the threw flag.
public class StdTryCallRuntimeOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override string Mnemonic => $"std.try_call_runtime @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public StdI64 ErrorFlag { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString(), ErrorFlag.ToString()] : [ErrorFlag.ToString()];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => Args;
  public override int PureResultId => -1;
  public override int AnyResultId => Result?.Id ?? ErrorFlag.Id;
}

/// <summary>
/// Like StdCallRuntimeOp but guards the first argument against null.
/// If the first arg is null, the call is skipped entirely.
/// Used for decref/incref on values that may legitimately be null
/// (e.g., zero-initialized scope variables, uninitialized globals).
/// </summary>
public class StdCallRuntimeIfNonnullOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override string Mnemonic => $"std.call_runtime_if_nonnull @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString()] : [];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => Args;
  public override int PureResultId => -1;
  public override int AnyResultId => Result?.Id ?? -1;
}

// ============================================================================
// Memory copy operation (for buffer grow/shift)
// ============================================================================

// Reinterpret a pointer value as i64 (same register, different type wrapper)
public class StdPtrToI64Op(StdPtr input) : StandardOp {
  public override string Mnemonic => "std.ptr_to_i64";
  public StdPtr Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdMemCopyOp(StdValue srcPtr, StdValue dstPtr, StdValue byteCount) : StandardOp {
  public override string Mnemonic => "std.memcopy";
  public StdValue SrcPtr { get; } = srcPtr;
  public StdValue DstPtr { get; } = dstPtr;
  public StdValue ByteCount { get; } = byteCount;
  public override IReadOnlyList<string> PrintableOperands => [SrcPtr.ToString(), DstPtr.ToString(), ByteCount.ToString()];
  public override List<StdValue> ReadValues => [SrcPtr, DstPtr, ByteCount];
  public override int PureResultId => -1;
}

/// Backward memcopy for overlapping shift-right: copies from end to start.
/// srcPtr and dstPtr point to the START of the regions (same as StdMemCopyOp).
public class StdMemCopyReverseOp(StdValue srcPtr, StdValue dstPtr, StdValue byteCount) : StandardOp {
  public override string Mnemonic => "std.memcopy_reverse";
  public StdValue SrcPtr { get; } = srcPtr;
  public StdValue DstPtr { get; } = dstPtr;
  public StdValue ByteCount { get; } = byteCount;
  public override IReadOnlyList<string> PrintableOperands => [SrcPtr.ToString(), DstPtr.ToString(), ByteCount.ToString()];
  public override List<StdValue> ReadValues => [SrcPtr, DstPtr, ByteCount];
  public override int PureResultId => -1;
}
