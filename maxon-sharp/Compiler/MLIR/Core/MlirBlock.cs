namespace MaxonSharp.Compiler.Ir.Core;

public class IrBlock<TOp>(string name) where TOp : IPrintableOp {
  public string Name { get; } = name;
  public List<TOp> Operations { get; } = [];

  public void AddOp(TOp op) {
    Operations.Add(op);
  }
}
