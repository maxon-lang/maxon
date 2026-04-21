namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// Dominator tree for a function's CFG, built by the Cooper/Harvey/Kennedy
/// iterative algorithm over reverse-post-order.
///
/// Block A dominates block B when every path from the function entry to B
/// passes through A. The immediate dominator of B is the closest such A.
/// </summary>
public class DominatorTree {
  // Maps block name → immediate dominator block name.
  private readonly Dictionary<string, string> _idom;
  // Reverse-post-order index per block. Lower index = earlier in RPO.
  // Used by Intersect() to walk chains efficiently without unbounded loops.
  private readonly Dictionary<string, int> _rpoIndex;

  private DominatorTree(Dictionary<string, string> idom, Dictionary<string, int> rpoIndex) {
    _idom = idom;
    _rpoIndex = rpoIndex;
  }

  /// <summary>
  /// Builds the dominator tree using the Cooper/Harvey/Kennedy iterative
  /// algorithm.  Runs in O(N log N) on most practical CFGs.
  /// </summary>
  public static DominatorTree Build(string entryBlock, CfgData cfg) {
    // Step 1: Compute reverse-post-order via DFS from the entry block.
    var rpo = ComputeRpo(entryBlock, cfg.Successors);
    var rpoIndex = new Dictionary<string, int>(rpo.Count);
    for (int i = 0; i < rpo.Count; i++) rpoIndex[rpo[i]] = i;

    // Step 2: Initialise idom only for the entry; all others are undefined.
    var idom = new Dictionary<string, string>(rpo.Count) { [entryBlock] = entryBlock };

    // Step 3: Iterate until stable.
    bool changed = true;
    while (changed) {
      changed = false;
      // Process in RPO (entry first, so predecessors are processed before
      // their successors on the first pass).
      foreach (var b in rpo) {
        if (b == entryBlock) continue;

        if (!cfg.Predecessors.TryGetValue(b, out var preds) || preds.Count == 0) {
          // Unreachable block — skip.
          continue;
        }

        // Pick the first predecessor that already has a dominator assigned.
        string? newIdom = null;
        foreach (var p in preds) {
          if (!idom.ContainsKey(p)) continue;
          newIdom = p;
          break;
        }
        if (newIdom == null) continue;

        // Intersect with remaining processed predecessors.
        foreach (var p in preds) {
          if (p == newIdom) continue;
          if (!idom.ContainsKey(p)) continue;
          newIdom = Intersect(p, newIdom, idom, rpoIndex);
        }

        if (!idom.TryGetValue(b, out var existing) || existing != newIdom) {
          idom[b] = newIdom;
          changed = true;
        }
      }
    }

    return new DominatorTree(idom, rpoIndex);
  }

  /// <summary>
  /// Returns true when block <paramref name="a"/> dominates block
  /// <paramref name="b"/> (i.e., every path from entry to b passes through a).
  /// </summary>
  public bool Dominates(string a, string b) {
    // Walk the idom chain upward from b until we reach a or the entry.
    var cur = b;
    while (true) {
      if (cur == a) return true;
      if (!_idom.TryGetValue(cur, out var parent) || parent == cur) break;
      cur = parent;
    }
    return false;
  }

  /// <summary>
  /// Returns true when <paramref name="a"/> strictly dominates
  /// <paramref name="b"/> (dominates and a ≠ b).
  /// </summary>
  public bool StrictlyDominates(string a, string b) => a != b && Dominates(a, b);

  /// <summary>
  /// Computes reverse post-order of blocks reachable from the entry.
  /// Unreachable blocks are omitted; the entry appears first.
  /// </summary>
  private static List<string> ComputeRpo(string entry, Dictionary<string, List<string>> successors) {
    var postOrder = new List<string>();
    var visited = new HashSet<string>();
    DfsPostOrder(entry, successors, visited, postOrder);
    postOrder.Reverse(); // post-order → reverse post-order
    return postOrder;
  }

  private static void DfsPostOrder(
      string block,
      Dictionary<string, List<string>> successors,
      HashSet<string> visited,
      List<string> postOrder) {
    if (!visited.Add(block)) return;
    if (successors.TryGetValue(block, out var succs)) {
      foreach (var s in succs) DfsPostOrder(s, successors, visited, postOrder);
    }
    postOrder.Add(block);
  }

  /// <summary>
  /// Finds the common dominator of two blocks using RPO index for fast
  /// upward chain traversal (the "intersect" inner loop from Cooper et al.).
  /// </summary>
  private static string Intersect(
      string b1, string b2,
      Dictionary<string, string> idom,
      Dictionary<string, int> rpoIndex) {
    while (b1 != b2) {
      while (rpoIndex.GetValueOrDefault(b1, int.MaxValue) > rpoIndex.GetValueOrDefault(b2, int.MaxValue))
        b1 = idom[b1];
      while (rpoIndex.GetValueOrDefault(b2, int.MaxValue) > rpoIndex.GetValueOrDefault(b1, int.MaxValue))
        b2 = idom[b2];
    }
    return b1;
  }
}
