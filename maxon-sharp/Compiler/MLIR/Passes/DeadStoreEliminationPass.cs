using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Dead store elimination and dead value elimination on Standard dialect IR.
/// 1. Intra-block DSE: a store to variable X is dead if X is stored to again
///    in the same block before any load or LEA references it.
/// 2. Cross-block liveness DSE: backward dataflow analysis identifies stores to
///    variables that are never loaded on any reachable path to function exit.
/// 3. Dead value elimination: pure ops (constants, arithmetic) whose results
///    are never referenced by any other op are removed.
/// </summary>
public static class DeadStoreEliminationPass {
  public static void Run(MlirModule<StandardOp> module) {
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        EliminateDeadStores(block);
      }
      EliminateDeadStoresLiveness(func);
      EliminateDeadValues(func);
    }
  }

  private static void EliminateDeadStores(MlirBlock<StandardOp> block) {
    var ops = block.Operations;
    var pendingStore = new Dictionary<string, int>();
    var deadIndices = new HashSet<int>();

    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];

      if (op is IStoreOp store) {
        if (pendingStore.TryGetValue(store.VarName, out var prevIdx)) {
          deadIndices.Add(prevIdx);
        }
        pendingStore[store.VarName] = i;
      } else if (TryGetLoadVarName(op, out var loadVarName)) {
        pendingStore.Remove(loadVarName);
      } else if (op is StdLeaOp lea) {
        FlushVariable(pendingStore, lea.VarName);
      }
    }

    if (deadIndices.Count == 0) return;

    foreach (var idx in deadIndices.OrderByDescending(i => i)) {
      ops.RemoveAt(idx);
    }

    Logger.Debug(LogCategory.Mlir, $"DSE: eliminated {deadIndices.Count} dead store(s) in {block.Name}");
  }

  /// <summary>
  /// Cross-block liveness-based dead store elimination.
  /// Uses backward dataflow analysis to find stores to variables that are
  /// never loaded on any path from the store to function exit.
  /// </summary>
  private static void EliminateDeadStoresLiveness(MlirFunction<StandardOp> func) {
    var blocks = func.Body.Blocks;

    // Collect all variable names and build field-variable map for LEA expansion
    var allVarNames = CollectAllVarNames(func);
    if (allVarNames.Count == 0) return;
    var fieldVarMap = BuildFieldVarMap(allVarNames);

    // Build CFG: block name → successor block names
    var successors = new Dictionary<string, List<string>>();
    for (int bi = 0; bi < blocks.Count; bi++) {
      var succs = GetSuccessors(blocks[bi]);
      // Blocks with no terminator (empty or non-branch last op) fall through
      // to the next physical block
      if (succs.Count == 0 && bi + 1 < blocks.Count && !EndsWithTerminator(blocks[bi])) {
        succs = [blocks[bi + 1].Name];
      }
      successors[blocks[bi].Name] = succs;
    }

    // Compute GEN and KILL for each block
    var gen = new Dictionary<string, HashSet<string>>();
    var kill = new Dictionary<string, HashSet<string>>();
    foreach (var block in blocks) {
      var (g, k) = ComputeGenKill(block, fieldVarMap);
      gen[block.Name] = g;
      kill[block.Name] = k;
    }

    // Backward dataflow: compute LIVE_IN/LIVE_OUT for each block
    var liveIn = new Dictionary<string, HashSet<string>>();
    foreach (var block in blocks) {
      liveIn[block.Name] = [];
    }

    bool changed = true;
    while (changed) {
      changed = false;
      for (int i = blocks.Count - 1; i >= 0; i--) {
        var name = blocks[i].Name;

        // LIVE_OUT = union of LIVE_IN of all successors
        var liveOut = new HashSet<string>();
        foreach (var succ in successors[name]) {
          if (liveIn.TryGetValue(succ, out var succLiveIn)) {
            liveOut.UnionWith(succLiveIn);
          }
        }

        // LIVE_IN = GEN ∪ (LIVE_OUT \ KILL)
        var newLiveIn = new HashSet<string>(gen[name]);
        var passThrough = new HashSet<string>(liveOut);
        passThrough.ExceptWith(kill[name]);
        newLiveIn.UnionWith(passThrough);

        if (!newLiveIn.SetEquals(liveIn[name])) {
          liveIn[name] = newLiveIn;
          changed = true;
        }
      }
    }

    // Compute LIVE_OUT for the elimination pass
    var liveOutMap = new Dictionary<string, HashSet<string>>();
    foreach (var block in blocks) {
      var liveOut = new HashSet<string>();
      foreach (var succ in successors[block.Name]) {
        if (liveIn.TryGetValue(succ, out var succLiveIn)) {
          liveOut.UnionWith(succLiveIn);
        }
      }
      liveOutMap[block.Name] = liveOut;
    }

    // Per-block backward scan to eliminate dead stores
    int totalEliminated = 0;
    foreach (var block in blocks) {
      totalEliminated += EliminateWithLiveness(block, liveOutMap[block.Name], fieldVarMap);
    }

    if (totalEliminated > 0) {
      Logger.Debug(LogCategory.Mlir, $"LiveDSE: eliminated {totalEliminated} dead store(s) in {func.Name}");
    }
  }

  private static List<string> GetSuccessors(MlirBlock<StandardOp> block) {
    if (block.Operations.Count == 0) return [];
    var lastOp = block.Operations[^1];
    return lastOp switch {
      StdBrOp br => [br.Target],
      StdCondBrOp condBr => [condBr.ThenBlock, condBr.ElseBlock],
      StdReturnOp => [],
      StdErrorReturnOp => [],
      _ => [],
    };
  }

  private static bool EndsWithTerminator(MlirBlock<StandardOp> block) {
    if (block.Operations.Count == 0) return false;
    var lastOp = block.Operations[^1];
    return lastOp is StdBrOp or StdCondBrOp or StdReturnOp or StdErrorReturnOp;
  }

  private static HashSet<string> CollectAllVarNames(MlirFunction<StandardOp> func) {
    var names = new HashSet<string>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is IStoreOp store) names.Add(store.VarName);
        else if (TryGetLoadVarName(op, out var loadVar)) names.Add(loadVar);
      }
    }
    return names;
  }

  /// <summary>
  /// Builds a map from base variable name → all field variable names.
  /// e.g., "arr" → {"arr.buffer", "arr.count", "arr.managed.capacity", ...}
  /// Handles nested fields: "arr.managed" → {"arr.managed.capacity", ...}
  /// </summary>
  private static Dictionary<string, HashSet<string>> BuildFieldVarMap(HashSet<string> allVarNames) {
    var map = new Dictionary<string, HashSet<string>>();
    foreach (var name in allVarNames) {
      var dotIdx = name.IndexOf('.');
      while (dotIdx >= 0) {
        var prefix = name[..dotIdx];
        if (!map.TryGetValue(prefix, out var fields)) {
          fields = [];
          map[prefix] = fields;
        }
        fields.Add(name);
        dotIdx = name.IndexOf('.', dotIdx + 1);
      }
    }
    return map;
  }

  /// <summary>
  /// Computes GEN and KILL sets for a block.
  /// GEN = variables loaded or LEA'd before any store to them (upward-exposed uses).
  /// KILL = variables stored in the block (definitions).
  /// </summary>
  private static (HashSet<string> gen, HashSet<string> kill) ComputeGenKill(
      MlirBlock<StandardOp> block, Dictionary<string, HashSet<string>> fieldVarMap) {
    var gen = new HashSet<string>();
    var kill = new HashSet<string>();

    foreach (var op in block.Operations) {
      // Uses: loads and LEAs (check before adding to kill)
      if (TryGetLoadVarName(op, out var loadVarName)) {
        if (!kill.Contains(loadVarName)) {
          gen.Add(loadVarName);
        }
      } else if (op is StdLeaOp lea) {
        if (!kill.Contains(lea.VarName)) gen.Add(lea.VarName);
        if (fieldVarMap.TryGetValue(lea.VarName, out var fields)) {
          foreach (var field in fields) {
            if (!kill.Contains(field)) gen.Add(field);
          }
        }
      }

      // Definitions: stores
      if (op is IStoreOp store) {
        kill.Add(store.VarName);
      }
    }

    return (gen, kill);
  }

  /// <summary>
  /// Backward scan of a block using LIVE_OUT to identify dead stores.
  /// A store to X is dead if X is not live at the point of the store.
  /// </summary>
  private static int EliminateWithLiveness(MlirBlock<StandardOp> block,
      HashSet<string> liveOutSet, Dictionary<string, HashSet<string>> fieldVarMap) {
    var live = new HashSet<string>(liveOutSet);
    var ops = block.Operations;
    var deadIndices = new HashSet<int>();

    for (int i = ops.Count - 1; i >= 0; i--) {
      var op = ops[i];

      if (op is IStoreOp store) {
        if (live.Contains(store.VarName)) {
          live.Remove(store.VarName);
        } else {
          Logger.Debug(LogCategory.Mlir, $"  LiveDSE: dead store to '{store.VarName}' at index {i} in {block.Name}");
          deadIndices.Add(i);
        }
      } else if (TryGetLoadVarName(op, out var loadVarName)) {
        live.Add(loadVarName);
      } else if (op is StdLeaOp lea) {
        live.Add(lea.VarName);
        if (fieldVarMap.TryGetValue(lea.VarName, out var fields)) {
          foreach (var field in fields) {
            live.Add(field);
          }
        }
      }
    }

    if (deadIndices.Count == 0) return 0;

    foreach (var idx in deadIndices.OrderByDescending(i => i)) {
      ops.RemoveAt(idx);
    }

    return deadIndices.Count;
  }

  private static bool TryGetLoadVarName(StandardOp op, out string varName) {
    switch (op) {
      case StdLoadI64Op load: varName = load.VarName; return true;
      case StdLoadF64Op load: varName = load.VarName; return true;
      case StdLoadI1Op load: varName = load.VarName; return true;
      case StdLoadPtrOp load: varName = load.VarName; return true;
      default: varName = ""; return false;
    }
  }

  /// <summary>
  /// Removes pure operations whose results are never consumed.
  /// Collects used value IDs across all blocks (values can be referenced cross-block),
  /// then removes side-effect-free ops with no consumers.
  /// Runs iteratively since removing an op may orphan its inputs.
  /// </summary>
  private static void EliminateDeadValues(MlirFunction<StandardOp> func) {
    int totalRemoved = 0;
    bool changed = true;
    while (changed) {
      var usedIds = new HashSet<int>();
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          foreach (var val in op.ReadValues) {
            usedIds.Add(val.Id);
          }
        }
      }

      int removed = 0;
      foreach (var block in func.Body.Blocks) {
        removed += block.Operations.RemoveAll(op => IsDeadPureOp(op, usedIds));
      }
      totalRemoved += removed;
      changed = removed > 0;
    }

    if (totalRemoved > 0) {
      Logger.Debug(LogCategory.Mlir, $"DCE: eliminated {totalRemoved} dead value(s) in {func.Name}");
    }
  }

  private static bool IsDeadPureOp(StandardOp op, HashSet<int> usedIds) {
    int resultId = op.PureResultId;
    return resultId >= 0 && !usedIds.Contains(resultId);
  }

  private static void FlushVariable(Dictionary<string, int> pendingStore, string varName) {
    var toRemove = new List<string>();
    foreach (var key in pendingStore.Keys) {
      if (key == varName || key.StartsWith(varName + ".")) {
        toRemove.Add(key);
      }
    }
    foreach (var key in toRemove) {
      pendingStore.Remove(key);
    }
  }
}
