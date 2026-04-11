namespace MaxonSharp.Compiler.Ir.Core;

public class IrRegion<TOp> where TOp : IPrintableOp {
  public List<IrBlock<TOp>> Blocks { get; } = [];

  public IrBlock<TOp> EntryBlock => Blocks[0];

  public IrBlock<TOp> AddBlock(string name) {
    var block = new IrBlock<TOp>(name);
    Blocks.Add(block);
    return block;
  }
}
