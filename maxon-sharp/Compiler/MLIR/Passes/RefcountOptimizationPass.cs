using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Eliminates redundant incref/decref pairs on Standard dialect IR.
///
/// An incref/decref pair on variable X is redundant when X was assigned from
/// another variable Y that holds its own reference to the same object, and Y's
/// reference is provably alive for the entire window between X's incref and decref.
/// In that case, Y's reference guarantees the object stays alive, making X's
/// incref/decref unnecessary.
///
/// Safe pattern:
///   load %ptr = src; store %ptr → dst    // dst aliases src
///   load %p1 = dst;  mm_incref(%p1)      // incref for dst (redundant if src alive)
///   ... (src not decreffed/zeroed/stored-to) ...
///   load %p2 = dst;  mm_decref(%p2)      // decref for dst (redundant if src alive)
///
/// The pass tracks variable-to-variable aliasing through stores, identifies
/// incref/decref pairs on aliased variables, and verifies the source variable's
/// reference is preserved between the incref and decref.
/// </summary>
public static class RefcountOptimizationPass {
  public static void Run(IrModule<StandardOp> module) {
    foreach (var func in module.Functions) {
      var useCounts = ComputeUseCounts(func);
      foreach (var block in func.Body.Blocks) {
        CancelRedundantRefcounts(block, useCounts);
      }
    }
  }

  private static Dictionary<int, int> ComputeUseCounts(IrFunction<StandardOp> func) {
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

  private static void CancelRedundantRefcounts(IrBlock<StandardOp> block, Dictionary<int, int> useCounts) {
    var ops = block.Operations;

    // Phase 1: Build maps for analysis
    // SSA value ID → variable name it was loaded from
    var loadedFrom = new Dictionary<int, string>();
    // SSA value ID → variable name it was stored to (for tracking store chains)
    var storedTo = new Dictionary<int, string>();
    // Variable name → source variable it was assigned from (alias tracking)
    var aliasSource = new Dictionary<string, string>();
    // Incref index → variable name it operates on
    var increfVar = new Dictionary<int, string>();
    // Incref index → source variable that keeps the object alive
    var increfSource = new Dictionary<int, string>();
    // Decref index → variable name it operates on
    var decrefVar = new Dictionary<int, string>();

    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];

      if (op is ILoadOp load) {
        loadedFrom[load.Result.Id] = load.VarName;
        continue;
      }

      if (op is IStoreOp store) {
        // Track the store destination
        storedTo[store.Value.Id] = store.VarName;
        // If the stored value was loaded from another variable, record the alias
        if (loadedFrom.TryGetValue(store.Value.Id, out var srcVar) && srcVar != store.VarName) {
          aliasSource[store.VarName] = srcVar;
        }
        continue;
      }

      var kind = GetRefcountKind(op, out var heapPtr);
      if (kind == RefcountKind.Incref && heapPtr != null) {
        if (loadedFrom.TryGetValue(heapPtr.Id, out var varName)) {
          increfVar[i] = varName;
          // Check if this variable has a known source alias
          if (aliasSource.TryGetValue(varName, out var src)) {
            increfSource[i] = src;
          }
        }
      } else if (kind == RefcountKind.Decref && heapPtr != null) {
        if (loadedFrom.TryGetValue(heapPtr.Id, out var varName)) {
          decrefVar[i] = varName;
        }
      }
    }

    // Phase 2: Find matching incref/decref pairs where the source is alive throughout
    var toRemove = new HashSet<int>();

    foreach (var (incIdx, varName) in increfVar) {
      // Only optimize if we know the source variable
      if (!increfSource.TryGetValue(incIdx, out var srcVar)) continue;

      // Find the matching decref for this variable (first decref of varName after incIdx)
      int decIdx = -1;
      for (int i = incIdx + 1; i < ops.Count; i++) {
        if (decrefVar.TryGetValue(i, out var dv) && dv == varName) {
          decIdx = i;
          break;
        }
      }
      if (decIdx < 0) continue;

      // Verify the source variable is alive between incref and decref:
      // - No store to srcVar (would change what it points to)
      // - No decref of srcVar (would release the source reference)
      // - No aliasing ops that could release srcVar transitively
      bool sourceAlive = true;
      for (int i = incIdx + 1; i < decIdx; i++) {
        var op = ops[i];

        // Store to the source variable invalidates it
        if (op is IStoreOp st && st.VarName == srcVar) {
          sourceAlive = false;
          break;
        }

        // Decref of the source variable releases the reference we depend on
        if (decrefVar.TryGetValue(i, out var dv) && dv == srcVar) {
          sourceAlive = false;
          break;
        }

        // Any aliasing op could transitively release the source
        if (IsAliasingOp(op)) {
          sourceAlive = false;
          break;
        }
      }

      if (!sourceAlive) continue;

      // Safe to cancel this incref/decref pair
      Logger.Debug(LogCategory.Ir, $"  RefcountOpt: cancel incref@{incIdx}/decref@{decIdx} for var '{varName}' (source '{srcVar}' alive) in {block.Name}");
      toRemove.Add(incIdx);
      toRemove.Add(decIdx);

      // Also remove single-use loads that fed the incref/decref
      int increfLoadIdx = FindPrecedingLoad(ops, incIdx, increfVar.ContainsKey(incIdx) ? GetRefcountHeapPtr(ops[incIdx])?.Id ?? -1 : -1);
      if (increfLoadIdx >= 0 && ops[increfLoadIdx] is ILoadOp il && useCounts.GetValueOrDefault(il.Result.Id) == 1) {
        toRemove.Add(increfLoadIdx);
      }
      int decrefLoadIdx = FindPrecedingLoad(ops, decIdx, decrefVar.ContainsKey(decIdx) ? GetRefcountHeapPtr(ops[decIdx])?.Id ?? -1 : -1);
      if (decrefLoadIdx >= 0 && ops[decrefLoadIdx] is ILoadOp dl && useCounts.GetValueOrDefault(dl.Result.Id) == 1) {
        toRemove.Add(decrefLoadIdx);
      }
    }

    if (toRemove.Count == 0) return;

    foreach (var idx in toRemove.OrderByDescending(i => i)) {
      ops.RemoveAt(idx);
    }

    Logger.Debug(LogCategory.Ir, $"RefcountOpt: eliminated {toRemove.Count} op(s) in {block.Name}");
  }

  private static int FindPrecedingLoad(List<StandardOp> ops, int fromIndex, int valueId) {
    if (valueId < 0) return -1;
    for (int j = fromIndex - 1; j >= 0; j--) {
      if (ops[j] is ILoadOp load && load.Result.Id == valueId) {
        return j;
      }
    }
    return -1;
  }

  private static StdValue? GetRefcountHeapPtr(StandardOp op) {
    if (op is StdCallRuntimeOp rtOp && (rtOp.Callee == "mm_incref" || rtOp.Callee == "mm_decref"))
      return rtOp.Args[0];
    if (op is StdCallRuntimeIfNonnullOp guardOp && (guardOp.Callee == "mm_incref" || guardOp.Callee == "mm_decref"))
      return guardOp.Args[0];
    return null;
  }

  private enum RefcountKind { Incref, Decref, None }

  private static RefcountKind GetRefcountKind(StandardOp op, out StdValue? heapPtr) {
    heapPtr = null;
    if (op is StdCallRuntimeOp rtOp) {
      if (rtOp.Callee == "mm_incref") { heapPtr = rtOp.Args[0]; return RefcountKind.Incref; }
      if (rtOp.Callee == "mm_decref") { heapPtr = rtOp.Args[0]; return RefcountKind.Decref; }
    } else if (op is StdCallRuntimeIfNonnullOp guardOp) {
      if (guardOp.Callee == "mm_incref") { heapPtr = guardOp.Args[0]; return RefcountKind.Incref; }
      if (guardOp.Callee == "mm_decref") { heapPtr = guardOp.Args[0]; return RefcountKind.Decref; }
    }
    return RefcountKind.None;
  }

  /// <summary>
  /// Returns true if an operation could release arbitrary heap objects via
  /// side effects (function calls, indirect stores, runtime calls with side effects).
  /// </summary>
  private static bool IsAliasingOp(StandardOp op) {
    if (op is StdCallOp or StdTryCallOp or StdTryCallRuntimeOp) return true;
    if (op is StdStoreIndirectOp or StdMemCopyOp or StdMemCopyReverseOp) return true;
    // mm_decref can trigger destructors with arbitrary side effects
    if (op is StdCallRuntimeOp rt && rt.Callee != "mm_incref" && rt.Callee != "mm_trace_transfer") return true;
    if (op is StdCallRuntimeIfNonnullOp grt && grt.Callee != "mm_incref") return true;
    return false;
  }
}
