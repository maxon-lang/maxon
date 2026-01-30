using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class StandardOp : IPrintableOp {
	public abstract string Mnemonic { get; }
	public virtual IReadOnlyList<string> PrintableResults => [];
	public virtual IReadOnlyList<string> PrintableOperands => [];
	public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
	public abstract List<StdValue> ReadValues { get; }
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

// === Integer Arithmetic ===

public class StdAddI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
	public override string Mnemonic => "arith.addi";
	public StdI64 Lhs { get; } = lhs;
	public StdI64 Rhs { get; } = rhs;
	public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override List<StdValue> ReadValues => [Lhs, Rhs];
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

public class StdSubI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
	public override string Mnemonic => "arith.subi";
	public StdI64 Lhs { get; } = lhs;
	public StdI64 Rhs { get; } = rhs;
	public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override List<StdValue> ReadValues => [Lhs, Rhs];
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

public class StdRemI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
	public override string Mnemonic => "arith.remsi";
	public StdI64 Lhs { get; } = lhs;
	public StdI64 Rhs { get; } = rhs;
	public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdMulI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
	public override string Mnemonic => "arith.muli";
	public StdI64 Lhs { get; } = lhs;
	public StdI64 Rhs { get; } = rhs;
	public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override List<StdValue> ReadValues => [Lhs, Rhs];
}

public class StdDivI64Op(StdI64 lhs, StdI64 rhs) : StandardOp {
	public override string Mnemonic => "arith.divsi";
	public StdI64 Lhs { get; } = lhs;
	public StdI64 Rhs { get; } = rhs;
	public StdI64 Result { get; } = new StdI64(MlirContext.Current.NextId());
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override List<StdValue> ReadValues => [Lhs, Rhs];
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
	public override List<StdValue> ReadValues => [];
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
