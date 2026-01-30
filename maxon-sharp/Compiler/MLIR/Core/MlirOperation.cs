namespace MaxonSharp.Compiler.Mlir.Core;

public interface IMlirOp {
	string Mnemonic { get; }
	List<MlirValue> Operands { get; }
	List<MlirValue> Results { get; }
	Dictionary<string, MlirAttribute> Attributes { get; }
}
