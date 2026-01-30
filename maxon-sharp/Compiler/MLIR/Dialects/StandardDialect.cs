using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class StandardOp : IPrintableOp {
	public abstract string Mnemonic { get; }
	public virtual IReadOnlyList<string> PrintableResults => [];
	public virtual IReadOnlyList<string> PrintableOperands => [];
	public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
}

// === Integer Constants ===

public class StdConstI64Op : StandardOp {
	public override string Mnemonic => "arith.constant";
	public long Value { get; }
	public StdI64 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(Value, MlirType.I64) };

	public StdConstI64Op(long value) {
		Value = value;
		Result = new StdI64(MlirContext.Current.NextId());
	}
}

public class StdConstI32Op : StandardOp {
	public override string Mnemonic => "arith.constant";
	public long Value { get; }
	public StdI32 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(Value, MlirType.I32) };

	public StdConstI32Op(long value) {
		Value = value;
		Result = new StdI32(MlirContext.Current.NextId());
	}
}

// === Float Constants ===

public class StdConstF64Op : StandardOp {
	public override string Mnemonic => "arith.float_constant";
	public double Value { get; }
	public StdF64 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["value"] = new FloatAttr(Value, MlirType.F64) };

	public StdConstF64Op(double value) {
		Value = value;
		Result = new StdF64(MlirContext.Current.NextId());
	}
}

// === Integer Arithmetic ===

public class StdAddI64Op : StandardOp {
	public override string Mnemonic => "arith.addi";
	public StdI64 Lhs { get; }
	public StdI64 Rhs { get; }
	public StdI64 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];

	public StdAddI64Op(StdI64 lhs, StdI64 rhs) {
		Lhs = lhs;
		Rhs = rhs;
		Result = new StdI64(MlirContext.Current.NextId());
	}
}

public class StdAddI32Op : StandardOp {
	public override string Mnemonic => "arith.addi";
	public StdI32 Lhs { get; }
	public StdI32 Rhs { get; }
	public StdI32 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];

	public StdAddI32Op(StdI32 lhs, StdI32 rhs) {
		Lhs = lhs;
		Rhs = rhs;
		Result = new StdI32(MlirContext.Current.NextId());
	}
}

public class StdSubI64Op : StandardOp {
	public override string Mnemonic => "arith.subi";
	public StdI64 Lhs { get; }
	public StdI64 Rhs { get; }
	public StdI64 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];

	public StdSubI64Op(StdI64 lhs, StdI64 rhs) {
		Lhs = lhs;
		Rhs = rhs;
		Result = new StdI64(MlirContext.Current.NextId());
	}
}

public class StdSubI32Op : StandardOp {
	public override string Mnemonic => "arith.subi";
	public StdI32 Lhs { get; }
	public StdI32 Rhs { get; }
	public StdI32 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];

	public StdSubI32Op(StdI32 lhs, StdI32 rhs) {
		Lhs = lhs;
		Rhs = rhs;
		Result = new StdI32(MlirContext.Current.NextId());
	}
}

// === Float Comparison ===

public class StdCmpF64Op : StandardOp {
	public override string Mnemonic => $"arith.cmpf {Predicate}";
	public string Predicate { get; }
	public StdF64 Lhs { get; }
	public StdF64 Rhs { get; }
	public StdBool Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];

	public StdCmpF64Op(string predicate, StdF64 lhs, StdF64 rhs) {
		Predicate = predicate;
		Lhs = lhs;
		Rhs = rhs;
		Result = new StdBool(MlirContext.Current.NextId());
	}
}

// === Memory Operations ===

public class StdStoreI64Op(StdI64 value, string varName) : StandardOp {
	public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
	public StdI64 Value { get; } = value;
	public string VarName { get; } = varName;
}

public class StdStoreF64Op(StdF64 value, string varName) : StandardOp {
	public override string Mnemonic => $"memref.store %{Value.Id}, {VarName}";
	public StdF64 Value { get; } = value;
	public string VarName { get; } = varName;
}

public class StdLoadI64Op : StandardOp {
	public override string Mnemonic => $"memref.load {VarName} : i64";
	public string VarName { get; }
	public StdI64 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];

	public StdLoadI64Op(string varName) {
		VarName = varName;
		Result = new StdI64(MlirContext.Current.NextId());
	}
}

public class StdLoadF64Op : StandardOp {
	public override string Mnemonic => $"memref.load {VarName} : f64";
	public string VarName { get; }
	public StdF64 Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];

	public StdLoadF64Op(string varName) {
		VarName = varName;
		Result = new StdF64(MlirContext.Current.NextId());
	}
}

// === Control Flow ===

public class StdCondBrOp(StdBool condition, string thenBlock, string elseBlock) : StandardOp {
	public override string Mnemonic => $"cf.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
	public StdBool Condition { get; } = condition;
	public string ThenBlock { get; } = thenBlock;
	public string ElseBlock { get; } = elseBlock;
}

public class StdBrOp(string target) : StandardOp {
	public override string Mnemonic => $"cf.br {Target}";
	public string Target { get; } = target;
}

// === Function Operations ===

public class StdCallOp : StandardOp {
	public override string Mnemonic => $"func.call @{Callee}";
	public string Callee { get; }
	public List<StdValue> Args { get; }
	public StdValue? Result { get; }
	public override IReadOnlyList<string> PrintableResults =>
		Result != null ? [Result.ToString()] : [];
	public override IReadOnlyList<string> PrintableOperands =>
		Args.Select(a => a.ToString()).ToList();

	public StdCallOp(string callee, List<StdValue> args, StdValue? result = null) {
		Callee = callee;
		Args = args;
		Result = result;
	}
}

public class StdReturnOp : StandardOp {
	public override string Mnemonic => "func.return";
	public StdValue? ReturnValue { get; }
	public override IReadOnlyList<string> PrintableOperands =>
		ReturnValue != null ? [ReturnValue.ToString()] : [];

	public StdReturnOp(StdValue? value = null) {
		ReturnValue = value;
	}
}
