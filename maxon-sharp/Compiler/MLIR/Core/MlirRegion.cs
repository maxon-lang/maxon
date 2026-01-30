namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirRegion {
	public List<MlirBlock> Blocks { get; } = [];

	public MlirBlock EntryBlock => Blocks[0];

	public MlirBlock AddBlock(string name) {
		var block = new MlirBlock(name);
		Blocks.Add(block);
		return block;
	}
}
