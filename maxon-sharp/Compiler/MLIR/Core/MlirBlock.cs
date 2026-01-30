namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirBlock(string name) {
	public string Name { get; } = name;
	public List<MlirOperation> Operations { get; } = [];

	public void AddOp(MlirOperation op) {
		Operations.Add(op);
	}
}
