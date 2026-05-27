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

public enum StdOpKind {
  ConstI64,
  ConstI32,
  ConstF64,
  ConstF32,
  ConstI1,
  AddI64,
  SubI64,
  AddI32,
  SubI32,
  MulI32,
  DivI32,
  DivU32,
  RemI32,
  RemU32,
  AndI32,
  OrI32,
  XorI32,
  ShlI32,
  ShrI32,
  ShrU32,
  CmpI32,
  CmpU32,
  ExtI32ToI64,
  TruncI64ToI32,
  SiToFpI32,
  UiToFpI32,
  StoreI32,
  LoadI32,
  RemI64,
  MulI64,
  DivI64,
  DivU64,
  RemU64,
  AndI64,
  OrI64,
  XorI64,
  ShlI64,
  ShrI64,
  ShrU64,
  AddF64,
  SubF64,
  MulF64,
  DivF64,
  AddF32,
  SubF32,
  MulF32,
  DivF32,
  AbsF64,
  SqrtF64,
  FloorF64,
  CeilF64,
  RoundF64,
  MinF64,
  MaxF64,
  AbsF32,
  SqrtF32,
  FloorF32,
  CeilF32,
  RoundF32,
  MinF32,
  MaxF32,
  FpToSi,
  FpToUi,
  BitcastF64ToI64,
  SiToFp,
  UiToFp,
  FpToSiF32,
  FpToUiF32,
  SiToFpF32,
  UiToFpF32,
  F64ToF32,
  F32ToF64,
  CmpI64,
  CmpU64,
  CmpF64,
  CmpF32,
  CmpI1,
  SelectI64,
  AndI1,
  OrI1,
  XorI1,
  StoreI64,
  StoreF64,
  LoadI64,
  StoreI1,
  LoadI1,
  LoadF64,
  StoreF32,
  LoadF32,
  StorePtr,
  LoadPtr,
  CondBr,
  Br,
  Switch,
  Param,
  Call,
  FuncRef,
  IndirectCall,
  Return,
  ErrorReturn,
  TryCall,
  Lea,
  BulkZero,
  LeaRdata,
  LeaSymdata,
  LeaUcddata,
  StoreIndirect,
  LoadIndirect,
  NullSafeLoadI64,
  GlobalLoadI64,
  GlobalLoadF64,
  GlobalLoadF32,
  GlobalLoadI1,
  GlobalStoreI64,
  GlobalStoreF64,
  GlobalStoreF32,
  GlobalStoreI1,
  GlobalLoadI8,
  GlobalStoreI8,
  GlobalLoadI16,
  GlobalStoreI16,
  CallRuntime,
  TryCallRuntime,
  CallRuntimeIfNonnull,
  PtrToI64,
  MemCopy,
  MemCopyReverse,
}

public abstract class StandardOp : IPrintableOp {
  public abstract StdOpKind Kind { get; }
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
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public abstract class StdBinaryF64Op(StdF64 lhs, StdF64 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public abstract class StdUnaryF32Op(StdF32 input) : StandardOp {
  public StdF32 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public abstract class StdBinaryF32Op(StdF32 lhs, StdF32 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdF32 Lhs { get; } = lhs;
  public StdF32 Rhs { get; } = rhs;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
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

public sealed class StdConstI64Op(long value) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ConstI64;
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(Value, IrType.I64) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdConstI32Op(long value) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ConstI32;
  public override string Mnemonic => "arith.constant";
  public long Value { get; } = value;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new IntegerAttr(Value, IrType.I32) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Float Constants ===

public sealed class StdConstF64Op(double value) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ConstF64;
  public override string Mnemonic => "arith.float_constant";
  public double Value { get; } = value;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new FloatAttr(Value, IrType.F64) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdConstF32Op(float value) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ConstF32;
  public override string Mnemonic => "arith.float_constant";
  public float Value { get; } = value;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyDictionary<string, IrAttribute> PrintableAttributes =>
    new Dictionary<string, IrAttribute> { ["value"] = new FloatAttr(Value, IrType.F32) };
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Bool Constants ===

public sealed class StdConstI1Op(bool value) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ConstI1;
  public override string Mnemonic => "arith.constant";
  public bool Value { get; } = value;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
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
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdAddI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AddI64;
  public override string Mnemonic => "arith.addi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public sealed class StdSubI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.SubI64;
  public override string Mnemonic => "arith.subi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public abstract class StdBinaryI32Op(StdI32 lhs, StdI32 rhs) : StandardOp {
  public abstract StdBinaryOperator Operator { get; }
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdAddI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AddI32;
  public override string Mnemonic => "arith.addi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public sealed class StdSubI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.SubI32;
  public override string Mnemonic => "arith.subi";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public sealed class StdMulI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MulI32;
  public override string Mnemonic => "arith.muli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public sealed class StdDivI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.DivI32;
  public override string Mnemonic => "arith.divsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

public sealed class StdDivU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.DivU32;
  public override string Mnemonic => "arith.divui";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivUnsigned;
}

public sealed class StdRemI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.RemI32;
  public override string Mnemonic => "arith.remsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemSigned;
}

public sealed class StdRemU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.RemU32;
  public override string Mnemonic => "arith.remui";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemUnsigned;
}

// === I32 Bitwise Operations ===

public sealed class StdAndI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AndI32;
  public override string Mnemonic => "arith.andi";
  public override StdBinaryOperator Operator => StdBinaryOperator.And;
}

public sealed class StdOrI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.OrI32;
  public override string Mnemonic => "arith.ori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Or;
}

public sealed class StdXorI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.XorI32;
  public override string Mnemonic => "arith.xori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Xor;
}

public sealed class StdShlI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.ShlI32;
  public override string Mnemonic => "arith.shli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Shl;
}

public sealed class StdShrI32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.ShrI32;
  public override string Mnemonic => "arith.shrsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrSigned;
}

public sealed class StdShrU32Op(StdI32 lhs, StdI32 rhs) : StdBinaryI32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.ShrU32;
  public override string Mnemonic => "arith.shrui";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrUnsigned;
}

// === I32 Comparison ===

public sealed class StdCmpI32Op(string predicate, StdI32 lhs, StdI32 rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpI32;
  public override string Mnemonic => $"arith.cmpi {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdCmpU32Op(string predicate, StdI32 lhs, StdI32 rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpU32;
  public override string Mnemonic => $"arith.cmpui {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI32 Lhs { get; } = lhs;
  public StdI32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

// === I32 Width Conversion ===

public sealed class StdExtI32ToI64Op(StdI32 input, bool signExtend = true) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ExtI32ToI64;
  public override string Mnemonic => SignExtend ? "arith.extsi" : "arith.extui";
  public StdI32 Input { get; } = input;
  public bool SignExtend { get; } = signExtend;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdTruncI64ToI32Op(StdI64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.TruncI64ToI32;
  public override string Mnemonic => "arith.trunci";
  public StdI64 Input { get; } = input;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === I32 Float Conversion ===

public sealed class StdSiToFpI32Op(StdI32 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.SiToFpI32;
  public override string Mnemonic => "arith.sitofp";
  public StdI32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdUiToFpI32Op(StdI32 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.UiToFpI32;
  public override string Mnemonic => "arith.uitofp";
  public StdI32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === I32 Memory Operations ===

public sealed class StdStoreI32Op(StdI32 value, string varName) : StandardOp, IStoreOp {
  public override StdOpKind Kind => StdOpKind.StoreI32;
  public override string Mnemonic => $"memref.store {Value}, {VarName}";
  public StdI32 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I32;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdLoadI32Op(string varName) : StandardOp, ILoadOp {
  public override StdOpKind Kind => StdOpKind.LoadI32;
  public override string Mnemonic => $"memref.load {VarName} : i32";
  public string VarName { get; } = varName;
  public StdI32 Result { get; } = new StdI32(IrContext.Current.NextStdId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdRemI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.RemI64;
  public override string Mnemonic => "arith.remsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemSigned;
}

public sealed class StdMulI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MulI64;
  public override string Mnemonic => "arith.muli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public sealed class StdDivI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.DivI64;
  public override string Mnemonic => "arith.divsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

public sealed class StdDivU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.DivU64;
  public override string Mnemonic => "arith.divui";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivUnsigned;
}

public sealed class StdRemU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.RemU64;
  public override string Mnemonic => "arith.remui";
  public override StdBinaryOperator Operator => StdBinaryOperator.RemUnsigned;
}

// === Bitwise Integer Operations ===

public sealed class StdAndI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AndI64;
  public override string Mnemonic => "arith.andi";
  public override StdBinaryOperator Operator => StdBinaryOperator.And;
}

public sealed class StdOrI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.OrI64;
  public override string Mnemonic => "arith.ori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Or;
}

public sealed class StdXorI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.XorI64;
  public override string Mnemonic => "arith.xori";
  public override StdBinaryOperator Operator => StdBinaryOperator.Xor;
}

public sealed class StdShlI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.ShlI64;
  public override string Mnemonic => "arith.shli";
  public override StdBinaryOperator Operator => StdBinaryOperator.Shl;
}

public sealed class StdShrI64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.ShrI64;
  public override string Mnemonic => "arith.shrsi";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrSigned;
}

public sealed class StdShrU64Op(StdI64 lhs, StdI64 rhs) : StdBinaryI64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.ShrU64;
  public override string Mnemonic => "arith.shrui";
  public override StdBinaryOperator Operator => StdBinaryOperator.ShrUnsigned;
}

// === Float Arithmetic ===

public sealed class StdAddF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AddF64;
  public override string Mnemonic => "arith.addf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public sealed class StdSubF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.SubF64;
  public override string Mnemonic => "arith.subf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public sealed class StdMulF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MulF64;
  public override string Mnemonic => "arith.mulf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public sealed class StdDivF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.DivF64;
  public override string Mnemonic => "arith.divf";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

// === F32 Float Arithmetic ===

public sealed class StdAddF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AddF32;
  public override string Mnemonic => "arith.addf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Add;
}

public sealed class StdSubF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.SubF32;
  public override string Mnemonic => "arith.subf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Sub;
}

public sealed class StdMulF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MulF32;
  public override string Mnemonic => "arith.mulf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Mul;
}

public sealed class StdDivF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.DivF32;
  public override string Mnemonic => "arith.divf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.DivSigned;
}

// === Float Absolute Value ===

public sealed class StdAbsF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override StdOpKind Kind => StdOpKind.AbsF64;
  public override string Mnemonic => "math.absf";
}

// === Float Math Operations ===

public sealed class StdSqrtF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override StdOpKind Kind => StdOpKind.SqrtF64;
  public override string Mnemonic => "math.sqrt";
}

public sealed class StdFloorF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override StdOpKind Kind => StdOpKind.FloorF64;
  public override string Mnemonic => "math.floor";
}

public sealed class StdCeilF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override StdOpKind Kind => StdOpKind.CeilF64;
  public override string Mnemonic => "math.ceil";
}

public sealed class StdRoundF64Op(StdF64 input) : StdUnaryF64Op(input) {
  public override StdOpKind Kind => StdOpKind.RoundF64;
  public override string Mnemonic => "math.round";
}

public sealed class StdMinF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MinF64;
  public override string Mnemonic => "math.minf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Min;
}

public sealed class StdMaxF64Op(StdF64 lhs, StdF64 rhs) : StdBinaryF64Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MaxF64;
  public override string Mnemonic => "math.maxf";
  public override StdBinaryOperator Operator => StdBinaryOperator.Max;
}

// === F32 Float Math Operations ===

public sealed class StdAbsF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override StdOpKind Kind => StdOpKind.AbsF32;
  public override string Mnemonic => "math.absf32";
}

public sealed class StdSqrtF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override StdOpKind Kind => StdOpKind.SqrtF32;
  public override string Mnemonic => "math.sqrtf32";
}

public sealed class StdFloorF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override StdOpKind Kind => StdOpKind.FloorF32;
  public override string Mnemonic => "math.floorf32";
}

public sealed class StdCeilF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override StdOpKind Kind => StdOpKind.CeilF32;
  public override string Mnemonic => "math.ceilf32";
}

public sealed class StdRoundF32Op(StdF32 input) : StdUnaryF32Op(input) {
  public override StdOpKind Kind => StdOpKind.RoundF32;
  public override string Mnemonic => "math.roundf32";
}

public sealed class StdMinF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MinF32;
  public override string Mnemonic => "math.minf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Min;
}

public sealed class StdMaxF32Op(StdF32 lhs, StdF32 rhs) : StdBinaryF32Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.MaxF32;
  public override string Mnemonic => "math.maxf32";
  public override StdBinaryOperator Operator => StdBinaryOperator.Max;
}

// === Float-to-Int Conversion ===

public sealed class StdFpToSiOp(StdF64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.FpToSi;
  public override string Mnemonic => "arith.fptosi";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdFpToUiOp(StdF64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.FpToUi;
  public override string Mnemonic => "arith.fptoui";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Float Bitcast (reinterpret bits) ===

public sealed class StdBitcastF64ToI64Op(StdF64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.BitcastF64ToI64;
  public override string Mnemonic => "arith.bitcast_f64_to_i64";
  public StdF64 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Int-to-Float Conversion ===

public sealed class StdSiToFpOp(StdI64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.SiToFp;
  public override string Mnemonic => "arith.sitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdUiToFpOp(StdI64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.UiToFp;
  public override string Mnemonic => "arith.uitofp";
  public StdI64 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === F32 Float-to-Int Conversion ===

public sealed class StdFpToSiF32Op(StdF32 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.FpToSiF32;
  public override string Mnemonic => "arith.fptosi_f32";
  public StdF32 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdFpToUiF32Op(StdF32 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.FpToUiF32;
  public override string Mnemonic => "arith.fptoui_f32";
  public StdF32 Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === F32 Int-to-Float Conversion ===

public sealed class StdSiToFpF32Op(StdI64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.SiToFpF32;
  public override string Mnemonic => "arith.sitofp_f32";
  public StdI64 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdUiToFpF32Op(StdI64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.UiToFpF32;
  public override string Mnemonic => "arith.uitofp_f32";
  public StdI64 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === F64/F32 Precision Conversion ===

public sealed class StdF64ToF32Op(StdF64 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.F64ToF32;
  public override string Mnemonic => "arith.truncf_f64_to_f32";
  public StdF64 Input { get; } = input;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdF32ToF64Op(StdF32 input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.F32ToF64;
  public override string Mnemonic => "arith.extf_f32_to_f64";
  public StdF32 Input { get; } = input;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

// === Comparison ===

public sealed class StdCmpI64Op(string predicate, StdI64 lhs, StdI64 rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpI64;
  public override string Mnemonic => $"arith.cmpi {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdCmpU64Op(string predicate, StdI64 lhs, StdI64 rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpU64;
  public override string Mnemonic => $"arith.cmpui {Predicate}";
  public string Predicate { get; } = predicate;
  public StdI64 Lhs { get; } = lhs;
  public StdI64 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdCmpF64Op(string predicate, StdF64 lhs, StdF64 rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpF64;
  public override string Mnemonic => $"arith.cmpf {Predicate}";
  public string Predicate { get; } = predicate;
  public StdF64 Lhs { get; } = lhs;
  public StdF64 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdCmpF32Op(string predicate, StdF32 lhs, StdF32 rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpF32;
  public override string Mnemonic => $"arith.cmpf32 {Predicate}";
  public string Predicate { get; } = predicate;
  public StdF32 Lhs { get; } = lhs;
  public StdF32 Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdCmpI1Op(string predicate, StdBool lhs, StdBool rhs) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CmpI1;
  public override string Mnemonic => $"arith.cmpi1 {Predicate}";
  public string Predicate { get; } = predicate;
  public StdBool Lhs { get; } = lhs;
  public StdBool Rhs { get; } = rhs;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

// === Conditional Select ===

/// Selects between two i64 values based on a boolean condition.
/// If condition is true, result = trueValue; otherwise result = falseValue.
public sealed class StdSelectI64Op(StdBool condition, StdI64 trueValue, StdI64 falseValue) : StandardOp {
  public override StdOpKind Kind => StdOpKind.SelectI64;
  public override string Mnemonic => "arith.select";
  public StdBool Condition { get; } = condition;
  public StdI64 TrueValue { get; } = trueValue;
  public StdI64 FalseValue { get; } = falseValue;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
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
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
  public override List<StdValue> ReadValues => [Lhs, Rhs];
  public override int PureResultId => Result.Id;
}

public sealed class StdAndI1Op(StdBool lhs, StdBool rhs) : StdBinaryI1Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.AndI1;
  public override string Mnemonic => "arith.andi1";
  public override StdBinaryOperator Operator => StdBinaryOperator.And;
}

public sealed class StdOrI1Op(StdBool lhs, StdBool rhs) : StdBinaryI1Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.OrI1;
  public override string Mnemonic => "arith.ori1";
  public override StdBinaryOperator Operator => StdBinaryOperator.Or;
}

public sealed class StdXorI1Op(StdBool lhs, StdBool rhs) : StdBinaryI1Op(lhs, rhs) {
  public override StdOpKind Kind => StdOpKind.XorI1;
  public override string Mnemonic => "arith.xori1";
  public override StdBinaryOperator Operator => StdBinaryOperator.Xor;
}

// === Memory Operations ===

public sealed class StdStoreI64Op(StdI64 value, string varName) : StandardOp, IStoreOp {
  public override StdOpKind Kind => StdOpKind.StoreI64;
  public override string Mnemonic => $"memref.store {Value}, {VarName}";
  public StdI64 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I64;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdStoreF64Op(StdF64 value, string varName) : StandardOp, IStoreOp {
  public override StdOpKind Kind => StdOpKind.StoreF64;
  public override string Mnemonic => $"memref.store {Value}, {VarName}";
  public StdF64 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.F64;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdLoadI64Op(string varName) : StandardOp, ILoadOp {
  public override StdOpKind Kind => StdOpKind.LoadI64;
  public override string Mnemonic => $"memref.load {VarName} : i64";
  public string VarName { get; } = varName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdStoreI1Op(StdBool value, string varName) : StandardOp, IStoreOp {
  public override StdOpKind Kind => StdOpKind.StoreI1;
  public override string Mnemonic => $"memref.store {Value}, {VarName}";
  public StdBool Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I1;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdLoadI1Op(string varName) : StandardOp, ILoadOp {
  public override StdOpKind Kind => StdOpKind.LoadI1;
  public override string Mnemonic => $"memref.load {VarName} : i1";
  public string VarName { get; } = varName;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdLoadF64Op(string varName) : StandardOp, ILoadOp {
  public override StdOpKind Kind => StdOpKind.LoadF64;
  public override string Mnemonic => $"memref.load {VarName} : f64";
  public string VarName { get; } = varName;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdStoreF32Op(StdF32 value, string varName) : StandardOp, IStoreOp {
  public override StdOpKind Kind => StdOpKind.StoreF32;
  public override string Mnemonic => $"memref.store {Value}, {VarName}";
  public StdF32 Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.F32;
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdLoadF32Op(string varName) : StandardOp, ILoadOp {
  public override StdOpKind Kind => StdOpKind.LoadF32;
  public override string Mnemonic => $"memref.load {VarName} : f32";
  public string VarName { get; } = varName;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

public sealed class StdStorePtrOp(StdPtr value, string varName) : StandardOp, IStoreOp {
  public override StdOpKind Kind => StdOpKind.StorePtr;
  public override string Mnemonic => $"memref.store {Value}, {VarName}";
  public StdPtr Value { get; } = value;
  StdValue IStoreOp.Value => Value;
  public string VarName { get; } = varName;
  public IrType StoredType => IrType.I64; // Function pointers are 64-bit
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdLoadPtrOp(string varName) : StandardOp, ILoadOp {
  public override StdOpKind Kind => StdOpKind.LoadPtr;
  public override string Mnemonic => $"memref.load {VarName} : ptr";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextStdId());
  StdValue ILoadOp.Result => Result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// === Control Flow ===

public sealed class StdCondBrOp(StdBool condition, string thenBlock, string elseBlock) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CondBr;
  public override string Mnemonic => $"cf.cond_br {Condition} [then: {ThenBlock}, else: {ElseBlock}]";
  public StdBool Condition { get; } = condition;
  public string ThenBlock { get; } = thenBlock;
  public string ElseBlock { get; } = elseBlock;
  public override List<StdValue> ReadValues => [Condition];
  public override int PureResultId => -1;
}

public sealed class StdBrOp(string target) : StandardOp {
  public override StdOpKind Kind => StdOpKind.Br;
  public override string Mnemonic => $"cf.br {Target}";
  public string Target { get; } = target;
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
}

public sealed class StdSwitchOp(StdI64 scrutinee, string[] caseTargets, string defaultTarget) : StandardOp {
  public override StdOpKind Kind => StdOpKind.Switch;
  public override string Mnemonic => $"cf.switch {Scrutinee} [{CaseTargets.Length} cases] default={DefaultTarget}";
  public StdI64 Scrutinee { get; } = scrutinee;
  public string[] CaseTargets { get; } = caseTargets;
  public string DefaultTarget { get; } = defaultTarget;
  public override List<StdValue> ReadValues => [Scrutinee];
  public override int PureResultId => -1;
}

// === Function Operations ===

public sealed class StdParamOp(int index, string name, StdValue result) : StandardOp {
  public override StdOpKind Kind => StdOpKind.Param;
  public override string Mnemonic => $"func.param {Name} : {Result.GetType().Name}";
  public int Index { get; } = index;
  public string Name { get; } = name;
  public StdValue Result { get; } = result;
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public override int AnyResultId => Result.Id;
}

public sealed class StdCallOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.Call;
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
public sealed class StdFuncRefOp(string functionName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.FuncRef;
  public override string Mnemonic => $"func.ref @{FunctionName}";
  public string FunctionName { get; } = functionName;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Calls a function indirectly through a function pointer
public sealed class StdIndirectCallOp(StdValue callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.IndirectCall;
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

public sealed class StdReturnOp(StdValue? value = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.Return;
  public override string Mnemonic => "func.return";
  public StdValue? ReturnValue { get; } = value;
  public override IReadOnlyList<string> PrintableOperands =>
    ReturnValue != null ? [ReturnValue.ToString()] : [];
  public override List<StdValue> ReadValues => ReturnValue != null ? [ReturnValue] : [];
  public override int PureResultId => -1;
}

// === Error handling operations ===

// Returns from a function with an error flag set (non-zero error ordinal in RDX, dummy value in RAX)
public sealed class StdErrorReturnOp(StdValue errorFlag) : StandardOp {
  public override StdOpKind Kind => StdOpKind.ErrorReturn;
  public override string Mnemonic => "func.error_return";
  public StdValue ErrorFlag { get; } = errorFlag;
  public override IReadOnlyList<string> PrintableOperands => [ErrorFlag.ToString()];
  public override List<StdValue> ReadValues => [ErrorFlag];
  public override int PureResultId => -1;
}

// Calls a throwing function and captures both the result (RAX) and error flag (RDX)
public sealed class StdTryCallOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.TryCall;
  public override string Mnemonic => $"func.try_call @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public StdI64 ErrorFlag { get; } = new StdI64(IrContext.Current.NextStdId());
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
public sealed class StdLeaOp(string varName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.Lea;
  public override string Mnemonic => $"memref.lea {VarName}";
  public string VarName { get; } = varName;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Address escape — removing would change semantics
}

// Bulk zero-initialize N contiguous qwords at a tagged stack region.
// Fields are named {Tag}.0 through {Tag}.{QwordCount-1}.
public sealed class StdBulkZeroOp(string tag, int qwordCount, bool zeroInit = true) : StandardOp {
  public override StdOpKind Kind => StdOpKind.BulkZero;
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
public sealed class StdLeaRdataOp(string rdataLabel) : StandardOp {
  public override StdOpKind Kind => StdOpKind.LeaRdata;
  public override string Mnemonic => $"memref.lea_rdata {RdataLabel}";
  public string RdataLabel { get; } = rdataLabel;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Gets the address of a symdata label via RIP-relative addressing (for panic messages in .symtab)
public sealed class StdLeaSymdataOp(string symdataLabel) : StandardOp {
  public override StdOpKind Kind => StdOpKind.LeaSymdata;
  public override string Mnemonic => $"memref.lea_symdata {SymdataLabel}";
  public string SymdataLabel { get; } = symdataLabel;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Gets the address of a ucddata label via RIP-relative addressing (for Unicode Character Database tables in .ucd)
public sealed class StdLeaUcddataOp(string ucddataLabel) : StandardOp {
  public override StdOpKind Kind => StdOpKind.LeaUcddata;
  public override string Mnemonic => $"memref.lea_ucddata {UcddataLabel}";
  public string UcddataLabel { get; } = ucddataLabel;
  public StdPtr Result { get; } = new StdPtr(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => Result.Id;
}

// Store a value through a pointer at a given offset (for sret writes)
public sealed class StdStoreIndirectOp(StdValue value, StdValue basePtr, int fieldOffset, IrType fieldType) : StandardOp {
  public override StdOpKind Kind => StdOpKind.StoreIndirect;
  public override string Mnemonic => $"memref.store_indirect {Value}, {BasePtr}+{FieldOffset}";
  public StdValue Value { get; } = value;
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public IrType FieldType { get; } = fieldType;
  public override List<StdValue> ReadValues => [Value, BasePtr];
  public override int PureResultId => -1;
}

// Load a value through a pointer at a given offset (for reading sret results)
public sealed class StdLoadIndirectOp(StdValue basePtr, int fieldOffset, IrType fieldType) : StandardOp {
  public override StdOpKind Kind => StdOpKind.LoadIndirect;
  public override string Mnemonic => $"memref.load_indirect {BasePtr}+{FieldOffset}";
  public StdValue BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public IrType FieldType { get; } = fieldType;
  public StdValue Result { get; } = StdValueFactory.CreateStdValueForType(fieldType);

  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [BasePtr];
  public override int PureResultId => Result.Id;
}

/// Null-safe load: returns [basePtr + fieldOffset] if basePtr != null, else returns 0.
public sealed class StdNullSafeLoadI64Op(StdI64 basePtr, int fieldOffset) : StandardOp {
  public override StdOpKind Kind => StdOpKind.NullSafeLoadI64;
  public override string Mnemonic => $"memref.null_safe_load {BasePtr}+{FieldOffset}";
  public StdI64 BasePtr { get; } = basePtr;
  public int FieldOffset { get; } = fieldOffset;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [BasePtr];
  public override int PureResultId => Result.Id;
}

public static class StdValueFactory {
  public static StdValue CreateStdValueForType(IrType type) {
    if (type == IrType.F32) return new StdF32(IrContext.Current.NextStdId());
    if (type == IrType.F64) return new StdF64(IrContext.Current.NextStdId());
    if (type == IrType.I1) return new StdBool(IrContext.Current.NextStdId());
    if (type == IrType.I8) return new StdI64(IrContext.Current.NextStdId());
    if (type == IrType.U8) return new StdI64(IrContext.Current.NextStdId());
    if (type == IrType.I16) return new StdI64(IrContext.Current.NextStdId());
    if (type == IrType.U16) return new StdI64(IrContext.Current.NextStdId());
    if (type == IrType.I32) return new StdI32(IrContext.Current.NextStdId());
    if (type == IrType.U32) return new StdU32(IrContext.Current.NextStdId());
    if (type == IrType.I64) return new StdI64(IrContext.Current.NextStdId());
    if (type == IrType.U64) return new StdI64(IrContext.Current.NextStdId());
    if (type == IrType.CString) return new StdI64(IrContext.Current.NextStdId());
    if (type is IrEnumType) return new StdI64(IrContext.Current.NextStdId());
    if (type is IrStructType) return new StdI64(IrContext.Current.NextStdId());
    if (type is IrRangedPrimitiveType rpt) return CreateStdValueForType(rpt.OptimalType);
    // Function values are represented as 64-bit code-pointers in storage.
    if (type is IrFunctionType) return new StdI64(IrContext.Current.NextStdId());
    throw new InvalidOperationException($"Cannot create StdValue for type: {type}");
  }
}


// ============================================================================
// Global variable operations
// ============================================================================

public sealed class StdGlobalLoadI64Op(string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalLoadI64;
  public override string Mnemonic => $"std.global_load_i64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public sealed class StdGlobalLoadF64Op(string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalLoadF64;
  public override string Mnemonic => $"std.global_load_f64 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdF64 Result { get; } = new StdF64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public sealed class StdGlobalLoadF32Op(string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalLoadF32;
  public override string Mnemonic => $"std.global_load_f32 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdF32 Result { get; } = new StdF32(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public sealed class StdGlobalLoadI1Op(string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalLoadI1;
  public override string Mnemonic => $"std.global_load_i1 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdBool Result { get; } = new StdBool(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1; // Reads mutable global state
  public override int AnyResultId => Result.Id;
}

public sealed class StdGlobalStoreI64Op(StdI64 value, string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalStoreI64;
  public override string Mnemonic => $"std.global_store_i64 @{GlobalName}";
  public StdI64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdGlobalStoreF64Op(StdF64 value, string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalStoreF64;
  public override string Mnemonic => $"std.global_store_f64 @{GlobalName}";
  public StdF64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdGlobalStoreF32Op(StdF32 value, string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalStoreF32;
  public override string Mnemonic => $"std.global_store_f32 @{GlobalName}";
  public StdF32 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdGlobalStoreI1Op(StdBool value, string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalStoreI1;
  public override string Mnemonic => $"std.global_store_i1 @{GlobalName}";
  public StdBool Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdGlobalLoadI8Op(string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalLoadI8;
  public override string Mnemonic => $"std.global_load_i8 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public override int AnyResultId => Result.Id;
}

public sealed class StdGlobalStoreI8Op(StdI64 value, string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalStoreI8;
  public override string Mnemonic => $"std.global_store_i8 @{GlobalName}";
  public StdI64 Value { get; } = value;
  public string GlobalName { get; } = globalName;
  public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
  public override List<StdValue> ReadValues => [Value];
  public override int PureResultId => -1;
}

public sealed class StdGlobalLoadI16Op(string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalLoadI16;
  public override string Mnemonic => $"std.global_load_i16 @{GlobalName}";
  public string GlobalName { get; } = globalName;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override List<StdValue> ReadValues => [];
  public override int PureResultId => -1;
  public override int AnyResultId => Result.Id;
}

public sealed class StdGlobalStoreI16Op(StdI64 value, string globalName) : StandardOp {
  public override StdOpKind Kind => StdOpKind.GlobalStoreI16;
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

public sealed class StdCallRuntimeOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CallRuntime;
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
public sealed class StdTryCallRuntimeOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.TryCallRuntime;
  public override string Mnemonic => $"std.try_call_runtime @{Callee}";
  public string Callee { get; } = callee;
  public List<StdValue> Args { get; } = args;
  public StdValue? Result { get; } = result;
  public StdI64 ErrorFlag { get; } = new StdI64(IrContext.Current.NextStdId());
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
public sealed class StdCallRuntimeIfNonnullOp(string callee, List<StdValue> args, StdValue? result = null) : StandardOp {
  public override StdOpKind Kind => StdOpKind.CallRuntimeIfNonnull;
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
public sealed class StdPtrToI64Op(StdPtr input) : StandardOp {
  public override StdOpKind Kind => StdOpKind.PtrToI64;
  public override string Mnemonic => "std.ptr_to_i64";
  public StdPtr Input { get; } = input;
  public StdI64 Result { get; } = new StdI64(IrContext.Current.NextStdId());
  public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
  public override IReadOnlyList<string> PrintableOperands => [Input.ToString()];
  public override List<StdValue> ReadValues => [Input];
  public override int PureResultId => Result.Id;
}

public sealed class StdMemCopyOp(StdValue srcPtr, StdValue dstPtr, StdValue byteCount) : StandardOp {
  public override StdOpKind Kind => StdOpKind.MemCopy;
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
public sealed class StdMemCopyReverseOp(StdValue srcPtr, StdValue dstPtr, StdValue byteCount) : StandardOp {
  public override StdOpKind Kind => StdOpKind.MemCopyReverse;
  public override string Mnemonic => "std.memcopy_reverse";
  public StdValue SrcPtr { get; } = srcPtr;
  public StdValue DstPtr { get; } = dstPtr;
  public StdValue ByteCount { get; } = byteCount;
  public override IReadOnlyList<string> PrintableOperands => [SrcPtr.ToString(), DstPtr.ToString(), ByteCount.ToString()];
  public override List<StdValue> ReadValues => [SrcPtr, DstPtr, ByteCount];
  public override int PureResultId => -1;
}
