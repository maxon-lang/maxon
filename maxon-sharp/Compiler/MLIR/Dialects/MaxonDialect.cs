using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract record MaxonExpr {
	public sealed record Value(MlirValue MlirValue) : MaxonExpr;
	public sealed record Call(MaxonCallOp CallOp) : MaxonExpr;
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
		if (expr is MaxonExpr.Value v) {
			Operands.Add(v.MlirValue);
		}
	}
}
