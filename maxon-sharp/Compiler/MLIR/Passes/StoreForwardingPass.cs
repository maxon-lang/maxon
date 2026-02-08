using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Store-to-load forwarding on Standard dialect IR.
/// When a load from variable X is immediately followed by a store to variable Y,
/// and the load's result has no other consumers, the pass replaces the (load, store)
/// pair with a direct store of the value previously stored to X.
/// This collapses struct field copy chains (e.g., __struct → arr → __selfbuf)
/// into direct stores, letting DSE clean up the now-dead intermediate stores.
/// </summary>
public static class StoreForwardingPass {
  public static void Run(MlirModule<StandardOp> module) {
    foreach (var func in module.Functions) {
      var useCounts = ComputeUseCounts(func);
      foreach (var block in func.Body.Blocks) {
        ForwardStores(block, useCounts);
      }
    }
  }

  private static Dictionary<int, int> ComputeUseCounts(MlirFunction<StandardOp> func) {
    var counts = new Dictionary<int, int>();
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        foreach (var val in op.ReadValues) {
          counts.TryGetValue(val.Id, out var count);
          counts[val.Id] = count + 1;
        }
      }
    }
    return counts;
  }

  private static void ForwardStores(MlirBlock<StandardOp> block, Dictionary<int, int> useCounts) {
    var ops = block.Operations;
    var lastStored = new Dictionary<string, StdValue>();
    var toRemove = new HashSet<int>();
    int forwarded = 0;

    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];

      if (op is IStoreOp store) {
        lastStored[store.VarName] = GetStoredValue(store);
      } else if (TryGetLoadInfo(op, out var loadVarName, out var loadResult)) {
        if (lastStored.TryGetValue(loadVarName, out var forwardedValue)
            && useCounts.GetValueOrDefault(loadResult.Id) == 1
            && i + 1 < ops.Count
            && ops[i + 1] is IStoreOp nextStore
            && StoreUsesValue(nextStore, loadResult)) {
          // Replace the store with one using the forwarded value
          ops[i + 1] = CreateStore(forwardedValue, nextStore.VarName);
          lastStored[nextStore.VarName] = forwardedValue;
          toRemove.Add(i);
          forwarded++;
        }
        // Non-forwarded loads don't invalidate lastStored — the stored value is unchanged
      } else if (op is StdLeaOp lea) {
        FlushVariable(lastStored, lea.VarName);
      }
    }

    if (toRemove.Count == 0) return;

    foreach (var idx in toRemove.OrderByDescending(i => i)) {
      ops.RemoveAt(idx);
    }

    Logger.Debug(LogCategory.Mlir, $"StoreForwarding: forwarded {forwarded} load-store pair(s) in {block.Name}");
  }

  private static bool TryGetLoadInfo(StandardOp op, out string varName, out StdValue result) {
    switch (op) {
      case StdLoadI64Op load: varName = load.VarName; result = load.Result; return true;
      case StdLoadF64Op load: varName = load.VarName; result = load.Result; return true;
      case StdLoadI1Op load: varName = load.VarName; result = load.Result; return true;
      case StdLoadPtrOp load: varName = load.VarName; result = load.Result; return true;
      default: varName = ""; result = null!; return false;
    }
  }

  private static StdValue GetStoredValue(IStoreOp store) {
    return store switch {
      StdStoreI64Op s => s.Value,
      StdStoreF64Op s => s.Value,
      StdStoreI1Op s => s.Value,
      StdStorePtrOp s => s.Value,
      _ => throw new InvalidOperationException($"Unknown store type: {store.GetType().Name}"),
    };
  }

  private static StandardOp CreateStore(StdValue value, string varName) {
    return value switch {
      StdI64 v => new StdStoreI64Op(v, varName),
      StdF64 v => new StdStoreF64Op(v, varName),
      StdBool v => new StdStoreI1Op(v, varName),
      StdPtr v => new StdStorePtrOp(v, varName),
      _ => throw new InvalidOperationException($"Unknown value type: {value.GetType().Name}"),
    };
  }

  private static bool StoreUsesValue(IStoreOp store, StdValue value) {
    var storedValue = GetStoredValue(store);
    return storedValue.Id == value.Id;
  }

  private static void FlushVariable(Dictionary<string, StdValue> lastStored, string varName) {
    var toRemove = new List<string>();
    foreach (var key in lastStored.Keys) {
      if (key == varName || key.StartsWith(varName + ".")) {
        toRemove.Add(key);
      }
    }
    foreach (var key in toRemove) {
      lastStored.Remove(key);
    }
  }
}
