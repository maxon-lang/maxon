using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Dead store elimination and dead value elimination on Standard dialect IR.
/// 1. Intra-block DSE: a store to variable X is dead if X is stored to again
///    in the same block before any load or LEA references it.
/// 2. Dead value elimination: pure ops (constants, arithmetic) whose results
///    are never referenced by any other op are removed.
/// </summary>
public static class DeadStoreEliminationPass {
  public static void Run(MlirModule<StandardOp> module) {
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        EliminateDeadStores(block);
      }
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
      } else if (op is StdLoadI64Op load64) {
        pendingStore.Remove(load64.VarName);
      } else if (op is StdLoadI1Op loadI1) {
        pendingStore.Remove(loadI1.VarName);
      } else if (op is StdLoadF64Op loadF64) {
        pendingStore.Remove(loadF64.VarName);
      } else if (op is StdLoadPtrOp loadPtr) {
        pendingStore.Remove(loadPtr.VarName);
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
  /// Removes pure operations whose results are never consumed.
  /// Collects used value IDs across all blocks (values can be referenced cross-block),
  /// then removes constants and arithmetic ops with no consumers.
  /// </summary>
  private static void EliminateDeadValues(MlirFunction<StandardOp> func) {
    var usedIds = new HashSet<int>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        foreach (var val in op.ReadValues) {
          usedIds.Add(val.Id);
        }
      }
    }

    int totalRemoved = 0;
    foreach (var block in func.Body.Blocks) {
      totalRemoved += block.Operations.RemoveAll(op => IsDeadPureOp(op, usedIds));
    }

    if (totalRemoved > 0) {
      Logger.Debug(LogCategory.Mlir, $"DCE: eliminated {totalRemoved} dead value(s) in {func.Name}");
    }
  }

  private static bool IsDeadPureOp(StandardOp op, HashSet<int> usedIds) {
    return op switch {
      StdConstI64Op c => !usedIds.Contains(c.Result.Id),
      StdConstI32Op c => !usedIds.Contains(c.Result.Id),
      StdConstF64Op c => !usedIds.Contains(c.Result.Id),
      StdConstI1Op c => !usedIds.Contains(c.Result.Id),
      _ => false,
    };
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
