using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class StandardOp : IMlirOp {
	public abstract string Mnemonic { get; }
	public List<MlirValue> Operands { get; } = [];
	public List<MlirValue> Results { get; } = [];
	public Dictionary<string, MlirAttribute> Attributes { get; } = [];
}

public class StandardMemRefAllocaOp(string varName, MlirType varType) : StandardOp {
	public override string Mnemonic => $"memref.alloca {VarName} : {VarType}";
	public string VarName { get; } = varName;
	public MlirType VarType { get; } = varType;
}

public class StandardMemRefStoreOp(MlirValue value, string varName) : StandardOp {
	public override string Mnemonic => $"memref.store %{StoreValue.Id}, {VarName}";
	public MlirValue StoreValue { get; } = value;
	public string VarName { get; } = varName;
}

public class StandardMemRefLoadOp : StandardOp {
	public override string Mnemonic => $"memref.load {VarName} : {VarType}";
	public string VarName { get; }
	public MlirType VarType { get; }
	public MlirValue Result { get; }

	public StandardMemRefLoadOp(string varName, MlirType varType) {
		VarName = varName;
		VarType = varType;
		Result = MlirContext.Current.CreateValue(varType, this);
		Results.Add(Result);
	}
}

public class StandardFuncCallOp : StandardOp {
	public override string Mnemonic => $"func.call @{Callee}";
	public string Callee { get; }
	public MlirValue? Result { get; }

	public StandardFuncCallOp(string callee, List<MlirValue> args, MlirType? resultType) {
		Callee = callee;
		Operands.AddRange(args);
		if (resultType != null) {
			Result = MlirContext.Current.CreateValue(resultType, this);
			Results.Add(Result);
		}
	}
}

public class StandardFuncReturnOp : StandardOp {
	public override string Mnemonic => "func.return";
	public MlirValue? ReturnValue { get; }

	public StandardFuncReturnOp(MlirValue? value = null) {
		ReturnValue = value;
		if (value != null) {
			Operands.Add(value);
		}
	}
}

public class StandardCfCondBrOp(MlirValue condition, string thenBlock, string elseBlock) : StandardOp {
	public override string Mnemonic => $"cf.cond_br %{Condition.Id} [then: {ThenBlock}, else: {ElseBlock}]";
	public MlirValue Condition { get; } = condition;
	public string ThenBlock { get; } = thenBlock;
	public string ElseBlock { get; } = elseBlock;
}

public class StandardCfBrOp(string target) : StandardOp {
	public override string Mnemonic => $"cf.br {Target}";
	public string Target { get; } = target;
}

public class StandardArithConstantOp : StandardOp {
	public override string Mnemonic => "arith.constant";
	public long IntValue { get; }
	public MlirType ResultType { get; }
	public MlirValue Result { get; }

	public StandardArithConstantOp(long value, MlirType type) {
		IntValue = value;
		ResultType = type;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["value"] = new IntegerAttr(value, type);
	}
}

public class StandardArithFloatConstantOp : StandardOp {
	public override string Mnemonic => "arith.float_constant";
	public double FloatValue { get; }
	public MlirType ResultType { get; }
	public MlirValue Result { get; }

	public StandardArithFloatConstantOp(double value, MlirType type) {
		FloatValue = value;
		ResultType = type;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["value"] = new FloatAttr(value, type);
	}
}

public class StandardArithAddIOp : StandardOp {
	public override string Mnemonic => "arith.addi";
	public MlirValue Result { get; }

	public StandardArithAddIOp(MlirValue lhs, MlirValue rhs) {
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I64, this);
		Results.Add(Result);
	}
}

public class StandardArithSubIOp : StandardOp {
	public override string Mnemonic => "arith.subi";
	public MlirValue Result { get; }

	public StandardArithSubIOp(MlirValue lhs, MlirValue rhs) {
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I64, this);
		Results.Add(Result);
	}
}

public class StandardArithCmpFOp : StandardOp {
	public override string Mnemonic => $"arith.cmpf {Predicate}";
	public string Predicate { get; }
	public MlirValue Result { get; }

	public StandardArithCmpFOp(string predicate, MlirValue lhs, MlirValue rhs) {
		Predicate = predicate;
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I1, this);
		Results.Add(Result);
	}
}
