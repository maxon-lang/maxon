namespace MaxonSharp.Compiler.Ir.Core;

public class IrBlock<TOp> where TOp : IPrintableOp {
  public string Name { get; }
  public List<TOp> Operations { get; }

  public IrBlock(string name) {
    Name = name;
    Operations = [];
  }

  /// <summary>
  /// Pre-sizes the operations list. Used by conversion passes that know or
  /// can estimate the output block's op count up front (typically the source
  /// block's size, since lowering is roughly 1:1 per op). Avoids the default
  /// 4/8/16/32/... doubling chain for hot blocks.
  /// </summary>
  public IrBlock(string name, int opCapacity) {
    Name = name;
    Operations = new List<TOp>(opCapacity);
  }

  public void AddOp(TOp op) {
    Operations.Add(op);
  }
}
