using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public class ArithConstantOp : MlirOperation {
	public override string Mnemonic => "arith.constant";
	public long IntValue { get; }
	public MlirType ResultType { get; }
	public MlirValue Result { get; }

	public ArithConstantOp(long value, MlirType type) {
		IntValue = value;
		ResultType = type;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["value"] = new IntegerAttr(value, type);
	}
}

public class ArithFloatConstantOp : MlirOperation {
	public override string Mnemonic => "arith.float_constant";
	public double FloatValue { get; }
	public MlirType ResultType { get; }
	public MlirValue Result { get; }

	public ArithFloatConstantOp(double value, MlirType type) {
		FloatValue = value;
		ResultType = type;
		Result = MlirContext.Current.CreateValue(type, this);
		Results.Add(Result);
		Attributes["value"] = new FloatAttr(value, type);
	}
}

public class ArithAddIOp : MlirOperation {
	public override string Mnemonic => "arith.addi";
	public MlirValue Result { get; }

	public ArithAddIOp(MlirValue lhs, MlirValue rhs) {
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I64, this);
		Results.Add(Result);
	}
}

public class ArithSubIOp : MlirOperation {
	public override string Mnemonic => "arith.subi";
	public MlirValue Result { get; }

	public ArithSubIOp(MlirValue lhs, MlirValue rhs) {
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I64, this);
		Results.Add(Result);
	}
}

public class ArithCmpFOp : MlirOperation {
	public override string Mnemonic => $"arith.cmpf {Predicate}";
	public string Predicate { get; }
	public MlirValue Result { get; }

	public ArithCmpFOp(string predicate, MlirValue lhs, MlirValue rhs) {
		Predicate = predicate;
		Operands.Add(lhs);
		Operands.Add(rhs);
		Result = MlirContext.Current.CreateValue(MlirType.I1, this);
		Results.Add(Result);
	}
}
