namespace MaxonSharp.Compiler.Ir.Core;

public interface IPrintableOp {
  string Mnemonic { get; }
  IReadOnlyList<string> PrintableResults { get; }
  IReadOnlyList<string> PrintableOperands { get; }
  IReadOnlyDictionary<string, IrAttribute> PrintableAttributes { get; }
}
