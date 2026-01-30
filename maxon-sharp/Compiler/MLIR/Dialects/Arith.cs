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
