using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

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
