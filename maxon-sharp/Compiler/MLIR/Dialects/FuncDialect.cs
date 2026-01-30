using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public class FuncCallOp : MlirOperation {
	public override string Mnemonic => $"func.call @{Callee}";
	public string Callee { get; }
	public MlirValue? Result { get; }

	public FuncCallOp(string callee, List<MlirValue> args, MlirType? resultType) {
		Callee = callee;
		Operands.AddRange(args);
		if (resultType != null) {
			Result = MlirContext.Current.CreateValue(resultType, this);
			Results.Add(Result);
		}
	}
}

public class FuncReturnOp : MlirOperation {
	public override string Mnemonic => "func.return";
	public MlirValue? ReturnValue { get; }

	public FuncReturnOp(MlirValue? value = null) {
		ReturnValue = value;
		if (value != null) {
			Operands.Add(value);
		}
	}
}
