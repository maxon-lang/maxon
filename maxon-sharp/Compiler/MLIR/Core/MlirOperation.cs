namespace MaxonSharp.Compiler.Mlir.Core;

public abstract class MlirOperation {
	public abstract string Mnemonic { get; }
	public List<MlirValue> Operands { get; } = [];
	public List<MlirValue> Results { get; } = [];
	public Dictionary<string, MlirAttribute> Attributes { get; } = [];
}
