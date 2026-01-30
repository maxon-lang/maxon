namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirRegion<TOp> where TOp : IPrintableOp {
	public List<MlirBlock<TOp>> Blocks { get; } = [];

	public MlirBlock<TOp> EntryBlock => Blocks[0];

	public MlirBlock<TOp> AddBlock(string name) {
		var block = new MlirBlock<TOp>(name);
		Blocks.Add(block);
		return block;
	}
}
