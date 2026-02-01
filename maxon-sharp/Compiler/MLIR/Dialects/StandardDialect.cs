using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class StandardOp : IPrintableOp {
  public abstract string Mnemonic { get; }
  public virtual IReadOnlyList<string> PrintableResults => [];
  public virtual IReadOnlyList<string> PrintableOperands => [];
  public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
  public abstract List<StdValue> ReadValues { get; }
}

public abstract class StdUnaryF64Op(StdF64 input) : StandardOp {
  public StdF64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
}

public abstract class StdBinaryF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
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
}

public class StdConstI32Op(long value) : StandardOp {
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
    new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(Value, MlirType.I32) };
  public override List<StdValue> ReadValues => [];
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
}

// === Integer Arithmetic ===

public abstract class StdBinaryI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdAddI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.addi";
}

public class StdAddI32Op(StdI32 lhs, StdI32 rhs) : StandardOp {
  public override string Mnemonic => "arith.addi";
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdSubI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override string Mnemonic => "arith.subi";
}

public class StdSubI32Op(StdI32 lhs, StdI32 rhs) : StandardOp {
  public override string Mnemonic => "arith.subi";
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdI32 Result { get; } = new StdI32(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
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

// === Float Arithmetic ===

public class StdAddF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.addf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdSubF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.subf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdMulF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.mulf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdDivF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public override string Mnemonic => "arith.divf";
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
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
}

// === Int-to-Float Conversion ===

public class StdSiToFpOp(StdI64 input) : StandardOp {
  public override string Mnemonic => "arith.sitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
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
}

public class StdOrI1Op(StdBool lhs, StdBool rhs) : StandardOp {
  public override string Mnemonic => "arith.ori1";
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdXorI1Op(StdBool lhs, StdBool rhs) : StandardOp {
  public override string Mnemonic => "arith.xori1";
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
}

// === Memory Operations ===

public class StdStoreI64Op(StdI64 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdI64 Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.I64;
  public override List<StdValue> ReadValues => [Value];
}

public class StdStoreF64Op(StdF64 value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdF64 Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.F64;
  public override List<StdValue> ReadValues => [Value];
}

public class StdLoadI64Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : i64";
  public string VarName { get; } = varName;
  public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
}

public class StdStoreI1Op(StdBool value, string varName) : StandardOp, IStoreOp {
  public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
  public StdBool Value { get; } = value;
  public string VarName { get; } = varName;
  public MlirType StoredType => MlirType.I1;
  public override List<StdValue> ReadValues => [Value];
}

public class StdLoadI1Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : i1";
  public string VarName { get; } = varName;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
}

public class StdLoadF64Op(string varName) : StandardOp {
  public override string Mnemonic => $"memref.load {VarName} : f64";
  public string VarName { get; } = varName;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
}

// === Control Flow ===

public class StdCondBrOp(StdBool condition, string thenBlock, string elseBlock) : StandardOp {
  public override string Mnemonic => $"cf.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
  public StdBool Condition { get; } = condition;
  public string ThenBlock { get; } = thenBlock;
  public string ElseBlock { get; } = elseBlock;
  public override List<StdValue> ReadValues => [Condition];
}

public class StdBrOp(string target) : StandardOp {
  public override string Mnemonic => $"cf.br {Target}";
  public string Target { get; } = target;
  public override List<StdValue> ReadValues => [];
}

// === Function Operations ===

public class StdParamOp(int index, string name, StdValue result) : StandardOp {
  public override string Mnemonic => $"func.param {Name} : {Result.GetType().Name}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public StdValue Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
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
}

public class StdReturnOp(StdValue? value = null) : StandardOp {
  public override string Mnemonic => "func.return";
  public StdValue? ReturnValue { get; } = value;
  public override IReadOnlyList<string> PrintableOperands =>
    ReturnValue != null ? [ReturnValue.ToString()] : [];
  public override List<StdValue> ReadValues => ReturnValue != null ? [ReturnValue] : [];
}

// === Struct pointer operations (for sret convention) ===

// Gets the stack address of a named variable (for passing struct by pointer)
public class StdLeaOp(string varName) : StandardOp {
  public override string Mnemonic => $"memref.lea {VarName}";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
}

// Store a value through a pointer at a given offset (for sret writes)
public class StdStoreIndirectOp(StdValue value, StdValue basePtr, int fieldOffset, MlirType fieldType) : StandardOp {
  public override string Mnemonic => $"memref.store_indirect %{Value.Id}, %{BasePtr.Id}+{FieldOffset}";
  public StdValue Value { get; } = value;
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public MlirType FieldType { get; } = fieldType;
  public override List<StdValue> ReadValues => [Value, BasePtr];
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
}

public static class StdValueFactory {
  public static StdValue CreateStdValueForType(MlirType type) {
    if (type == MlirType.F64) return new StdF64(MlirContext.Current.NextId());
    if (type == MlirType.I1) return new StdBool(MlirContext.Current.NextId());
    if (type == MlirType.I64) return new StdI64(MlirContext.Current.NextId());
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
}

public class StdGlobalLoadF64Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_f64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdF64 Result { get; } = new StdF64(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
}

public class StdGlobalLoadI1Op(string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_load_i1 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdBool Result { get; } = new StdBool(MlirContext.Current.NextId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
}

public class StdGlobalStoreI64Op(StdI64 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i64 @{GlobalName}";
  public StdI64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
}

public class StdGlobalStoreF64Op(StdF64 value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_f64 @{GlobalName}";
  public StdF64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
}

public class StdGlobalStoreI1Op(StdBool value, string globalName) : StandardOp {
  public override string Mnemonic => $"std.global_store_i1 @{GlobalName}";
  public StdBool Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
}
