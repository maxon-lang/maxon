namespace MaxonSharp.Compiler.Mlir.Core;

public interface IPrintableOp {
	string Mnemonic { get; }
	IReadOnlyList<string> PrintableResults { get; }
	IReadOnlyList<string> PrintableOperands { get; }
	IReadOnlyDictionary<string, MlirAttribute> PrintableAttributes { get; }
}
