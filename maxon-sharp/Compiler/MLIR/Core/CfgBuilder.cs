namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// CFG data for a function: per-block successor and predecessor maps.
/// </summary>
public class CfgData {
  public Dictionary<string, List<string>> Successors { get; } = [];
  public Dictionary<string, List<string>> Predecessors { get; } = [];
}

/// <summary>
/// Builds control flow graph data from a function's blocks.
/// Generic over dialect — takes delegates for successor extraction since
/// terminator ops have no shared interface across dialects.
/// </summary>
public static class CfgBuilder<TOp> where TOp : IPrintableOp {
  /// <summary>
  /// Builds successor and predecessor maps for the given blocks.
  /// Handles fall-through for blocks that don't end with a terminator.
  /// </summary>
  public static CfgData Build(
      List<IrBlock<TOp>> blocks,
      Func<IrBlock<TOp>, List<string>> getSuccessors,
      Func<IrBlock<TOp>, bool> endsWithTerminator) {
    var cfg = new CfgData();
    foreach (var b in blocks) cfg.Predecessors[b.Name] = [];
    for (int bi = 0; bi < blocks.Count; bi++) {
      var b = blocks[bi];
      var succs = getSuccessors(b);
      if (succs.Count == 0 && bi + 1 < blocks.Count && !endsWithTerminator(b)) {
        succs = [blocks[bi + 1].Name];
      }
      cfg.Successors[b.Name] = succs;
      foreach (var succ in succs) {
        if (cfg.Predecessors.TryGetValue(succ, out var preds))
          preds.Add(b.Name);
      }
    }
    return cfg;
  }
}
