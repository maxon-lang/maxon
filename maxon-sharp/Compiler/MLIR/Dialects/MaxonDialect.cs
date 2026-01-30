using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum MaxonValueKind { Integer, Float, Bool }

public abstract class MaxonOp : IPrintableOp {
	public abstract string Mnemonic { get; }
	public virtual IReadOnlyList<string> PrintableResults => [];
	public virtual IReadOnlyList<string> PrintableOperands => [];
	public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
}

public abstract record MaxonExpr {
	public sealed record Value(MaxonValue MaxonValue) : MaxonExpr;
	public sealed record Call(MaxonCallOp CallOp) : MaxonExpr;
	public sealed record VarLoad(MaxonVarLoadOp LoadOp) : MaxonExpr;
}

public class MaxonConstantOp : MaxonOp {
	public override string Mnemonic => "maxon.constant";
	public MaxonValueKind ValueKind { get; }
	public long IntValue { get; }
	public double FloatValue { get; }
	public MaxonValue Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		ValueKind == MaxonValueKind.Float
			? new Dictionary<string, MlirAttribute> { ["value"] = new FloatAttr(FloatValue, MlirType.F64) }
			: new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(IntValue, MlirType.I64) };

	public MaxonConstantOp(long value) {
		ValueKind = MaxonValueKind.Integer;
		IntValue = value;
		Result = new MaxonInteger(MlirContext.Current.NextId());
	}

	public MaxonConstantOp(double value) {
		ValueKind = MaxonValueKind.Float;
		FloatValue = value;
		Result = new MaxonFloat(MlirContext.Current.NextId());
	}
}

public class MaxonVarDeclOp(string varName, MaxonValue initValue) : MaxonOp {
	public override string Mnemonic => $"maxon.var_decl {VarName}";
	public string VarName { get; } = varName;
	public MaxonValue InitValue { get; } = initValue;
	public override IReadOnlyList<string> PrintableOperands => [InitValue.ToString()];
}

public class MaxonVarLoadOp(string varName, MaxonValueKind kind) : MaxonOp {
	public override string Mnemonic => $"maxon.var_load {VarName}";
	public string VarName { get; } = varName;
	public MaxonValueKind ValueKind { get; } = kind;
	public MaxonValue Result { get; } = kind switch {
		MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
		MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
		MaxonValueKind.Bool => new MaxonBool(MlirContext.Current.NextId()),
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["type"] = new TypeAttr(KindToMlirType(ValueKind)) };

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		MaxonValueKind.Bool => MlirType.I1,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonAddOp(MaxonValue lhs, MaxonValue rhs, MaxonValueKind kind) : MaxonOp {
	public override string Mnemonic => "maxon.add";
	public MaxonValue Lhs { get; } = lhs;
	public MaxonValue Rhs { get; } = rhs;
	public MaxonValueKind ValueKind { get; } = kind;
	public MaxonValue Result { get; } = CreateResult(kind);
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["type"] = new TypeAttr(KindToMlirType(ValueKind)) };

	private static MaxonValue CreateResult(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
		MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonSubOp(MaxonValue lhs, MaxonValue rhs, MaxonValueKind kind) : MaxonOp {
	public override string Mnemonic => "maxon.sub";
	public MaxonValue Lhs { get; } = lhs;
	public MaxonValue Rhs { get; } = rhs;
	public MaxonValueKind ValueKind { get; } = kind;
	public MaxonValue Result { get; } = CreateResult(kind);
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["type"] = new TypeAttr(KindToMlirType(ValueKind)) };

	private static MaxonValue CreateResult(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
		MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonCmpOp(string predicate, MaxonValue lhs, MaxonValue rhs, MaxonValueKind kind) : MaxonOp {
	public override string Mnemonic => $"maxon.cmp {Predicate}";
	public string Predicate { get; } = predicate;
	public MaxonValue Lhs { get; } = lhs;
	public MaxonValue Rhs { get; } = rhs;
	public MaxonValueKind ValueKind { get; } = kind;
	public MaxonBool Result { get; } = new MaxonBool(MlirContext.Current.NextId());
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> { ["type"] = new TypeAttr(KindToMlirType(ValueKind)) };

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		MaxonValueKind.Bool => MlirType.I1,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonCondBrOp(MaxonBool condition, string thenBlock, string elseBlock) : MaxonOp {
	public override string Mnemonic => $"maxon.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
	public MaxonBool Condition { get; } = condition;
	public string ThenBlock { get; } = thenBlock;
	public string ElseBlock { get; } = elseBlock;
}

public class MaxonCallOp(string callee, List<MaxonValue> args) : MaxonOp {
	public override string Mnemonic => $"maxon.call @{Callee}";
	public string Callee { get; } = callee;
	public List<MaxonValue> Args { get; } = args;
	public override IReadOnlyList<string> PrintableOperands => Args.Select(a => a.ToString()).ToList();
}

public class MaxonReturnOp(MaxonExpr? expr = null) : MaxonOp {
	public override string Mnemonic => "maxon.return";
	public MaxonExpr? ReturnExpr { get; } = expr;
	public MaxonValue? Value { get; } = expr switch {
		MaxonExpr.Value v => v.MaxonValue,
		MaxonExpr.VarLoad vl => vl.LoadOp.Result,
		_ => null
	};
	public override IReadOnlyList<string> PrintableOperands =>
		Value != null ? [Value.ToString()] : [];
}
