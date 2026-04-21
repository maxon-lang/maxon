namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// A single natural loop identified by a back-edge <c>latch → header</c> where
/// the header dominates the latch.
///
/// Body blocks are all CFG nodes that can reach the latch without passing
/// through the header, plus the header itself. Exit blocks are successors of
/// body blocks that are outside the loop — control leaves the loop through
/// these edges.
///
/// When multiple back-edges target the same header (e.g. two <c>continue</c>
/// sites), their bodies are merged into one loop. This matches the standard
/// "natural loop" definition and is more convenient for transformations that
/// want one structure per loop header.
/// </summary>
public sealed class NaturalLoop {
  public required string Header { get; init; }
  /// <summary>Blocks inside the loop, including the header.</summary>
  public required HashSet<string> Body { get; init; }
  /// <summary>Blocks outside the loop that are successors of a body block.</summary>
  public required HashSet<string> Exits { get; init; }
}

/// <summary>
/// Detects all natural loops in a function via back-edges in the CFG.
/// Requires the dominator tree to test "header dominates latch".
/// </summary>
public static class NaturalLoops {
  /// <summary>
  /// Returns one <see cref="NaturalLoop"/> per loop header. Loops are not
  /// nested in the return value — if A contains B, both are present and must
  /// be distinguished by the caller (typically by comparing <see cref="NaturalLoop.Body"/>
  /// set sizes).
  /// </summary>
  public static List<NaturalLoop> Find(CfgData cfg, DominatorTree domTree) {
    // header → merged body set across all back-edges targeting that header.
    var bodyByHeader = new Dictionary<string, HashSet<string>>();

    foreach (var (fromBlock, succs) in cfg.Successors) {
      foreach (var succ in succs) {
        // A back-edge is an edge whose target dominates its source.
        if (!domTree.Dominates(succ, fromBlock)) continue;

        // Natural-loop body = header ∪ {all blocks that can reach the latch
        // without going through the header}. Walk backward from the latch
        // (fromBlock) along predecessors, treating the header as a barrier.
        if (!bodyByHeader.TryGetValue(succ, out var body)) {
          body = [succ];
          bodyByHeader[succ] = body;
        }

        if (body.Add(fromBlock)) {
          var worklist = new Queue<string>();
          worklist.Enqueue(fromBlock);
          while (worklist.Count > 0) {
            var cur = worklist.Dequeue();
            if (cur == succ) continue;
            if (cfg.Predecessors.TryGetValue(cur, out var preds)) {
              foreach (var pred in preds) {
                if (body.Add(pred) && pred != succ) worklist.Enqueue(pred);
              }
            }
          }
        }
      }
    }

    var loops = new List<NaturalLoop>();
    foreach (var (header, body) in bodyByHeader) {
      // Exits: any successor of a body block that is not itself in the body.
      var exits = new HashSet<string>();
      foreach (var b in body) {
        if (cfg.Successors.TryGetValue(b, out var succs)) {
          foreach (var s in succs) if (!body.Contains(s)) exits.Add(s);
        }
      }
      loops.Add(new NaturalLoop { Header = header, Body = body, Exits = exits });
    }
    return loops;
  }
}
