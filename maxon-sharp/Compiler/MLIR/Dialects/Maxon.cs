using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

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

public class MaxonReturnOp : MlirOperation {
	public override string Mnemonic => "maxon.return";
	public MlirValue? ReturnValue { get; }

	public MaxonReturnOp(MlirValue? value = null) {
		ReturnValue = value;
		if (value != null) {
			Operands.Add(value);
		}
	}
}
