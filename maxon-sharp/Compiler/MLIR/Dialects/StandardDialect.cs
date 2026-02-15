using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class StandardOp : IPrintableOp {
  public abstract string Mnemonic { get; }
  public virtual IReadOnlyList<string> PrintableResults => [];
  public virtual IReadOnlyList<string> PrintableOperands => [];
  public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
  public abstract List<StdValue> ReadValues { get; }

  /// Returns the result ID if this op is pure (side-effect-free and safe to remove
  /// when its result is unused), or -1 if it has side effects.
  public abstract int PureResultId { get; }
}

public abstract class StdUnaryF64Op(StdF64 input) : StandardOp {
  public StdF64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public abstract class StdBinaryF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public interface IStoreOp {
  string VarName { get; }
  MlirType StoredType { get; }
}

// === Integer Constants ===

public class StdConstI64Op(long value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(Value, MlirType.I64) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdConstI32Op(long value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(Value, MlirType.I32) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Float Constants ===

public class StdConstF64Op(double value) : StandardOp {
  public override string Mnemonic => "arith.float_constant";
  public double Value { get; } = value;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> { ["value"] = new FloatAttr(Value, MlirType.F64) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Bool Constants ===

public class StdConstI1Op(bool value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public bool Value { get; } = value;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(Value ? 1 : 0, MlirType.I1) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Integer Arithmetic ===

public abstract class StdBinaryI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdAddI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.addi";
}

public class StdSubI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.subi";
}

public abstract class StdBinaryI32Op(StdI32 lhs, StdI32 rhs) : StandardOp {
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdAddI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.addi";
}

public class StdSubI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.subi";
}

public class StdMulI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.muli";
}

public class StdDivI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.divsi";
}

public class StdDivU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.divui";
}

public class StdRemI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.remsi";
}

public class StdRemU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.remui";
}

// === I32 Bitwise Operations ===

public class StdAndI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.andi";
}

public class StdOrI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.ori";
}

public class StdXorI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.xori";
}

public class StdShlI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.shli";
}

public class StdShrI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrsi";
}

public class StdShrU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrui";
}

// === I32 Comparison ===

public class StdCmpI32Op(string predicate, StdI32 lhs, StdI32 rhs) : StandardOp {
  public override string Mnemonic => $"arith.cmpi {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
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
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
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
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdTruncI64ToI32Op(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.trunci";
  public StdI64 Input { get; } = input;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === I32 Float Conversion ===

public class StdSiToFpI32Op(StdI32 input) : StandardOp {
  public override string Mnemonic => "arith.sitofp";
  public StdI32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdUiToFpI32Op(StdI32 input) : StandardOp {
  public override string Mnemonic => "arith.uitofp";
  public StdI32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === I32 Memory Operations ===

public class StdStoreI32Op(StdI32 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdI32 Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.I32;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadI32Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : i32";
  public string VarName { get; } = varName;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdRemI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.remsi";
}

public class StdMulI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.muli";
}

public class StdDivI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.divsi";
}

public class StdDivU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.divui";
}

public class StdRemU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.remui";
}

// === Bitwise Integer Operations ===

public class StdAndI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.andi";
}

public class StdOrI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.ori";
}

public class StdXorI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.xori";
}

public class StdShlI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.shli";
}

public class StdShrI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrsi";
}

public class StdShrU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.shrui";
}

// === Float Arithmetic ===

public class StdAddF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.addf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdSubF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.subf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdMulF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.mulf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdDivF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.divf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
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
}

public class StdMaxF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override string Mnemonic => "math.maxf";
}

// === Float-to-Int Conversion ===

public class StdFpToSiOp(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.fptosi";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdFpToUiOp(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.fptoui";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Float Bitcast (reinterpret bits) ===

public class StdBitcastF64ToI64Op(StdF64 input) : StandardOp {
  public override string Mnemonic => "arith.bitcast_f64_to_i64";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Int-to-Float Conversion ===

public class StdSiToFpOp(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.sitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public class StdUiToFpOp(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.uitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
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
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
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
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
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
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
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
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
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
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Condition.ToString(), TrueValue.ToString(), FalseValue.ToString()];
  public override List<StdValue> ReadValues => [Condition, TrueValue, FalseValue];
  public override int PureResultId => Result.Id;
}

// === Bool Logical Operations ===

public class StdAndI1Op(StdBool lhs, StdBool rhs) : StandardOp {
  public override string Mnemonic => "arith.andi1";
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdOrI1Op(StdBool lhs, StdBool rhs) : StandardOp {
  public override string Mnemonic => "arith.ori1";
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public class StdXorI1Op(StdBool lhs, StdBool rhs) : StandardOp {
  public override string Mnemonic => "arith.xori1";
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

// === Memory Operations ===

public class StdStoreI64Op(StdI64 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdI64 Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.I64;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdStoreF64Op(StdF64 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdF64 Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.F64;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadI64Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : i64";
  public string VarName { get; } = varName;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdStoreI1Op(StdBool value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdBool Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.I1;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadI1Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : i1";
  public string VarName { get; } = varName;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdLoadF64Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : f64";
  public string VarName { get; } = varName;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public class StdStorePtrOp(StdPtr value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdPtr Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.I64; // Function pointers are 64-bit
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public class StdLoadPtrOp(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : ptr";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(MlirContext.Current.NextId());
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

// === Function Operations ===

public class StdParamOp(int index, string name, StdValue result) : StandardOp {
  public override string Mnemonic => $"func.param {Name} : {Result.GetType().Name}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public StdValue Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
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
}

// Gets the address of a function (function reference/pointer)
public class StdFuncRefOp(string functionName) : StandardOp {
  public override string Mnemonic => $"func.ref @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public StdPtr Result { get; } = new StdPtr(MlirContext.Current.NextId());
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
  public StdI64 ErrorFlag { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults =>
    Result != null ? [Result.ToString(), ErrorFlag.ToString()] : [ErrorFlag.ToString()];
  public override IReadOnlyList<string> PrintableOperands =>
    [.. Args.Select(a => a.ToString())];
  public override List<StdValue> ReadValues => Args;
  public override int PureResultId => -1;
}

// === Struct pointer operations (for sret convention) ===

// Gets the stack address of a named variable (for passing struct by pointer)
public class StdLeaOp(string varName) : StandardOp {
  public override string Mnemonic => $"memref.lea {VarName}";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Address escape — removing would change semantics
}

// Gets the address of an rdata label via RIP-relative addressing (for constant data in .rdata)
public class StdLeaRdataOp(string rdataLabel) : StandardOp {
  public override string Mnemonic => $"memref.lea_rdata {RdataLabel}";
  public string RdataLabel { get; } = rdataLabel;
  public StdPtr Result { get; } = new StdPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Store a value through a pointer at a given offset (for sret writes)
public class StdStoreIndirectOp(StdValue value, StdValue basePtr, int fieldOffset, MlirType fieldType) : StandardOp {
  public override string Mnemonic => $"memref.store_indirect %{Value.Id}, %{BasePtr.Id}+{FieldOffset}";
  public StdValue Value { get; } = value;
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public MlirType FieldType { get; } = fieldType;
  public override List<StdValue> ReadValues => [Value, BasePtr];
  public override int PureResultId => -1;
}

// Load a value through a pointer at a given offset (for reading sret results)
public class StdLoadIndirectOp(StdValue basePtr, int fieldOffset, MlirType fieldType) : StandardOp {
  public override string Mnemonic => $"memref.load_indirect %{BasePtr.Id}+{FieldOffset}";
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public MlirType FieldType { get; } = fieldType;
  public StdValue Result { get; } = StdValueFactory.CreateStdValueForType(fieldType);

  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [BasePtr];
  public override int PureResultId => Result.Id;
}

public static class StdValueFactory {
  public static StdValue CreateStdValueForType(MlirType type) {
    if (type == MlirType.F64) return new StdF64(MlirContext.Current.NextId());
    if (type == MlirType.I1) return new StdBool(MlirContext.Current.NextId());
    if (type == MlirType.I8) return new StdI64(MlirContext.Current.NextId());
    if (type == MlirType.U8) return new StdI64(MlirContext.Current.NextId());
    if (type == MlirType.I32) return new StdI32(MlirContext.Current.NextId());
    if (type == MlirType.U32) return new StdU32(MlirContext.Current.NextId());
    if (type == MlirType.I64) return new StdI64(MlirContext.Current.NextId());
    if (type == MlirType.U64) return new StdI64(MlirContext.Current.NextId());
    if (type is MlirEnumType) return new StdI64(MlirContext.Current.NextId());
    if (type is MlirStructType) return new StdI64(MlirContext.Current.NextId());
    if (type is MlirRangedPrimitiveType rpt) return CreateStdValueForType(rpt.OptimalType);
    throw new InvalidOperationException($"Cannot create StdValue for type: {type}");
  }
}


// ============================================================================
// Global variable operations
// ============================================================================

public class StdGlobalLoadI64Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
}

public class StdGlobalLoadF64Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_f64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
}

public class StdGlobalLoadI1Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i1 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
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

public class StdGlobalStoreI1Op(StdBool value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i1 @{GlobalName}";
  public StdBool Value { get; } = value;
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
}

// ============================================================================
// Memory copy operation (for buffer grow/shift)
// ============================================================================

// Reinterpret a pointer value as i64 (same register, different type wrapper)
public class StdPtrToI64Op(StdPtr input) : StandardOp {
  public override string Mnemonic => "std.ptr_to_i64";
  public StdPtr Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
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
