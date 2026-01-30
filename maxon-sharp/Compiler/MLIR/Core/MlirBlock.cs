namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirBlock<TOp>(string name) where TOp : IPrintableOp {
	public string Name { get; } = name;
	public List<TOp> Operations { get; } = [];

	public void AddOp(TOp op) {
		Operations.Add(op);
	}
}
