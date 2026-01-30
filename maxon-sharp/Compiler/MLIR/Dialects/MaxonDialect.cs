using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public enum MaxonValueKind { Integer, Float, Bool }

public enum MaxonBinOperator {
	Add, Sub, Mul, Div, Mod,
	Eq, Ne, Lt, Gt, Le, Ge,
	And, Or
}

public abstract class MaxonOp : IPrintableOp {
	public abstract string Mnemonic { get; }
	public virtual IReadOnlyList<string> PrintableResults => [];
	public virtual IReadOnlyList<string> PrintableOperands => [];
	public virtual IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes => new Dictionary<string, MlirAttribute>();
}

public class MaxonLiteralOp : MaxonOp {
	public override string Mnemonic => "maxon.literal";
	public MaxonValueKind ValueKind { get; }
	public long IntValue { get; }
	public double FloatValue { get; }
	public bool BoolValue { get; }
	public MaxonValue Result { get; }
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		ValueKind switch {
			MaxonValueKind.Float => new Dictionary<string, MlirAttribute> { ["value"] = new FloatAttr(FloatValue, MlirType.F64) },
			MaxonValueKind.Bool => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(BoolValue ? 1 : 0, MlirType.I1) },
			_ => new Dictionary<string, MlirAttribute> { ["value"] = new IntegerAttr(IntValue, MlirType.I64) }
		};

	public MaxonLiteralOp(long value) {
		ValueKind = MaxonValueKind.Integer;
		IntValue = value;
		Result = new MaxonInteger(MlirContext.Current.NextId());
	}

	public MaxonLiteralOp(double value) {
		ValueKind = MaxonValueKind.Float;
		FloatValue = value;
		Result = new MaxonFloat(MlirContext.Current.NextId());
	}

	public MaxonLiteralOp(bool value) {
		ValueKind = MaxonValueKind.Bool;
		BoolValue = value;
		Result = new MaxonBool(MlirContext.Current.NextId());
	}
}

public class MaxonAssignOp(string varName, MaxonValue value, bool isDeclaration, bool isMutable, MaxonValueKind valueKind) : MaxonOp {
	public override string Mnemonic => "maxon.assign";
	public string VarName { get; } = varName;
	public MaxonValue Value { get; } = value;
	public bool IsDeclaration { get; } = isDeclaration;
	public bool IsMutable { get; } = isMutable;
	public MaxonValueKind ValueKind { get; } = valueKind;
	public override IReadOnlyList<string> PrintableOperands => [Value.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes {
		get {
			var attrs = new Dictionary<string, MlirAttribute> {
				["var"] = new StringAttr(VarName),
				["kind"] = new TypeAttr(KindToMlirType(ValueKind))
			};
			if (IsDeclaration) attrs["decl"] = new IntegerAttr(1, MlirType.I1);
			if (IsMutable) attrs["mut"] = new IntegerAttr(1, MlirType.I1);
			return attrs;
		}
	}

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		MaxonValueKind.Bool => MlirType.I1,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonVarRefOp(string varName, MaxonValueKind kind) : MaxonOp {
	public override string Mnemonic => "maxon.var_ref";
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
		new Dictionary<string, MlirAttribute> {
			["var"] = new StringAttr(VarName),
			["type"] = new TypeAttr(KindToMlirType(ValueKind))
		};

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		MaxonValueKind.Bool => MlirType.I1,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonBinOp(MaxonBinOperator op, MaxonValue lhs, MaxonValue rhs, MaxonValueKind operandKind) : MaxonOp {
	public override string Mnemonic => "maxon.binop";
	public MaxonBinOperator Operator { get; } = op;
	public MaxonValue Lhs { get; } = lhs;
	public MaxonValue Rhs { get; } = rhs;
	public MaxonValueKind OperandKind { get; } = operandKind;
	public MaxonValue Result { get; } = IsComparison(op)
			? new MaxonBool(MlirContext.Current.NextId())
			: CreateArithResult(operandKind);
	public override IReadOnlyList<string> PrintableResults => [Result.ToString()];
	public override IReadOnlyList<string> PrintableOperands => [Lhs.ToString(), Rhs.ToString()];
	public override IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes =>
		new Dictionary<string, MlirAttribute> {
			["op"] = new StringAttr(Operator.ToString().ToLowerInvariant()),
			["kind"] = new TypeAttr(KindToMlirType(OperandKind))
		};

	private static bool IsComparison(MaxonBinOperator op) =>
		op is MaxonBinOperator.Eq or MaxonBinOperator.Ne or MaxonBinOperator.Lt
			or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge;

	private static MaxonValue CreateArithResult(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
		MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};

	private static MlirType KindToMlirType(MaxonValueKind kind) => kind switch {
		MaxonValueKind.Integer => MlirType.I64,
		MaxonValueKind.Float => MlirType.F64,
		MaxonValueKind.Bool => MlirType.I1,
		_ => throw new ArgumentOutOfRangeException(nameof(kind))
	};
}

public class MaxonCallOp(string callee, List<MaxonValue> args, MaxonValueKind? resultKind = null) : MaxonOp {
	public override string Mnemonic => $"maxon.call @{Callee}";
	public string Callee { get; } = callee;
	public List<MaxonValue> Args { get; } = args;
	public MaxonValue? Result { get; } = resultKind switch {
		MaxonValueKind.Integer => new MaxonInteger(MlirContext.Current.NextId()),
		MaxonValueKind.Float => new MaxonFloat(MlirContext.Current.NextId()),
		MaxonValueKind.Bool => new MaxonBool(MlirContext.Current.NextId()),
		null => null,
		_ => throw new ArgumentOutOfRangeException(nameof(resultKind))
	};
	public MaxonValueKind? ResultKind { get; } = resultKind;
	public override IReadOnlyList<string> PrintableResults => Result != null ? [Result.ToString()] : [];
	public override IReadOnlyList<string> PrintableOperands => [.. Args.Select(a => a.ToString())];
}

public class MaxonCondBrOp(MaxonValue condition, string thenBlock, string elseBlock) : MaxonOp {
	public override string Mnemonic => $"maxon.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
	public MaxonValue Condition { get; } = condition;
	public string ThenBlock { get; } = thenBlock;
	public string ElseBlock { get; } = elseBlock;
}

public class MaxonReturnOp(MaxonValue? value = null) : MaxonOp {
	public override string Mnemonic => "maxon.return";
	public MaxonValue? Value { get; } = value;
	public override IReadOnlyList<string> PrintableOperands =>
		Value != null ? [Value.ToString()] : [];
}
