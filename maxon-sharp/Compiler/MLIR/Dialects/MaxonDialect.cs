using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract record MaxonExpr {
	public sealed record Value(MlirValue MlirValue) : MaxonExpr;
	public sealed record Call(MaxonCallOp CallOp) : MaxonExpr;
	public sealed record VarLoad(MaxonVarLoadOp LoadOp) : MaxonExpr;
}

public class MaxonConstantOp : MlirOperation {
	public override string Mnemonic => "maxon.constant";
	public long Value { get; }
	public MlirType ResultType { get; }
	public MlirValue Result { get; }

	public MaxonConstantOp(long value, MlirType type) {
		Value = value;
		ResultType = type;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["value"] = new IntegerAttr(value, type);
	}
}

public class MaxonFloatConstantOp : MlirOperation {
	public override string Mnemonic => "maxon.float_constant";
	public double Value { get; }
	public MlirType ResultType { get; }
	public MlirValue Result { get; }

	public MaxonFloatConstantOp(double value, MlirType type) {
		Value = value;
		ResultType = type;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["value"] = new FloatAttr(value, type);
	}
}

public class MaxonVarDeclOp : MlirOperation {
	public override string Mnemonic => $"maxon.var_decl {VarName}";
	public string VarName { get; }
	public MlirValue InitValue { get; }

	public MaxonVarDeclOp(string varName, MlirValue initValue) {
		VarName = varName;
		InitValue = initValue;
		Operands.Add(initValue);
	}
}

public class MaxonVarLoadOp : MlirOperation {
	public override string Mnemonic => $"maxon.var_load {VarName}";
	public string VarName { get; }
	public MlirValue Result { get; }

	public MaxonVarLoadOp(string varName, MlirType type) {
		VarName = varName;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["type"] = new TypeAttr(type);
	}
}

public class MaxonCmpFOp : MlirOperation {
	public override string Mnemonic => $"maxon.cmpf {Predicate}";
	public string Predicate { get; }
	public MlirValue Result { get; }

	public MaxonCmpFOp(string predicate, MlirValue lhs, MlirValue rhs) {
		Predicate = predicate;
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I1, this);
		Results.Add(Result);
	}
}

public class MaxonCondBrOp(MlirValue condition, string thenBlock, string elseBlock) : MlirOperation {
	public override string Mnemonic => $"maxon.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
	public MlirValue Condition { get; } = condition;
	public string ThenBlock { get; } = thenBlock;
	public string ElseBlock { get; } = elseBlock;
}

public class MaxonCallOp : MlirOperation {
	public override string Mnemonic => $"maxon.call @{Callee}";
	public string Callee { get; }

	public MaxonCallOp(string callee, List<MlirValue> args) {
		Callee = callee;
		Operands.AddRange(args);
	}
}

public class MaxonReturnOp : MlirOperation {
	public override string Mnemonic => "maxon.return";
	public MaxonExpr? ReturnExpr { get; }

	public MaxonReturnOp(MaxonExpr? expr = null) {
		ReturnExpr = expr;
		switch (expr) {
			case MaxonExpr.Value v:
				Operands.Add(v.MlirValue);
				break;
			case MaxonExpr.VarLoad vl:
				Operands.Add(vl.LoadOp.Result);
				break;
		}
	}
}
