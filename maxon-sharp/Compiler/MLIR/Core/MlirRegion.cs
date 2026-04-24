namespace MaxonSharp.Compiler.Ir.Core;

public class IrRegion<TOp> where TOp : IPrintableOp {
  public List<IrBlock<TOp>> Blocks { get; } = [];

  public IrBlock<TOp> EntryBlock => Blocks[0];

  public IrBlock<TOp> AddBlock(string name) {
    var block = new IrBlock<TOp>(name);
    Blocks.Add(block);
    return block;
  }

  /// Same as AddBlock(name) but pre-sizes the block's operation list to
  /// <paramref name="opCapacity"/>. Used by lowering passes that know the
  /// source block's op count — new block is typically 1:1 or slightly larger.
  public IrBlock<TOp> AddBlock(string name, int opCapacity) {
    var block = new IrBlock<TOp>(name, opCapacity);
    Blocks.Add(block);
    return block;
  }
}
