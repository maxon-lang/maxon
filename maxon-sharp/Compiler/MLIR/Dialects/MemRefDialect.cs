using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir.Dialects;

public class MemRefAllocaOp(string varName, MlirType varType) : MlirOperation {
	public override string Mnemonic => $"memref.alloca {VarName} : {VarType}";
	public string VarName { get; } = varName;
	public MlirType VarType { get; } = varType;
}

public class MemRefStoreOp(MlirValue value, string varName) : MlirOperation {
	public override string Mnemonic => $"memref.store %{StoreValue.Id}, {VarName}";
	public MlirValue StoreValue { get; } = value;
	public string VarName { get; } = varName;
}

public class MemRefLoadOp : MlirOperation {
	public override string Mnemonic => $"memref.load {VarName} : {VarType}";
	public string VarName { get; }
	public MlirType VarType { get; }
	public MlirValue Result { get; }

	public MemRefLoadOp(string varName, MlirType varType) {
		VarName = varName;
		VarType = varType;
		Result = MlirContext.Current.CreateValue(varType, this);
		Results.Add(Result);
	}
}
