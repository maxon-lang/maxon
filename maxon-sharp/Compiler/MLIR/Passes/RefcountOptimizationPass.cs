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

    // Phase 2: Find matching incref/decref pairs where the source is alive throughout.
    //
    // The previous implementation linearly searched forward through all remaining
    // ops for each incref's matching decref and then re-scanned the in-between
    // range for liveness (F7 in nested-foraging-hummingbird — O(increfs × ops)).
    // This rewrite pre-tabulates:
    //   - decref indices grouped by variable (sorted ascending)
    //   - a sorted list of "source-liveness-breaking" op indices (aliasing ops +
    //     every store to *any* variable, which we filter by name per lookup)
    // and then each incref becomes two binary searches + an O(k) store-per-var
    // window check where k is the number of stores to a specific source var.
    var decrefIdxByVar = new Dictionary<string, List<int>>();
    foreach (var (idx, varName) in decrefVar) {
      if (!decrefIdxByVar.TryGetValue(varName, out var list)) {
        list = [];
        decrefIdxByVar[varName] = list;
      }
      list.Add(idx);
    }
    foreach (var list in decrefIdxByVar.Values) list.Sort();

    // "Aliasing event" indices: any op that could release an arbitrary heap
    // object, plus any decref of any variable (which we'll filter per source).
    // Sorted ascending.
    var aliasingEvents = new List<int>();
    // Stores grouped by destination variable (sorted ascending).
    var storeIdxByVar = new Dictionary<string, List<int>>();
    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];
      if (IsAliasingOp(op)) aliasingEvents.Add(i);
      if (op is IStoreOp st) {
        if (!storeIdxByVar.TryGetValue(st.VarName, out var list)) {
          list = [];
          storeIdxByVar[st.VarName] = list;
        }
        list.Add(i);
      }
    }

    var toRemove = new HashSet<int>();

    foreach (var (incIdx, varName) in increfVar) {
      // Only optimize if we know the source variable
      if (!increfSource.TryGetValue(incIdx, out var srcVar)) continue;

      // Find the first decref of varName after incIdx via binary search.
      if (!decrefIdxByVar.TryGetValue(varName, out var decrefList)) continue;
      int decIdx = FirstGreaterThan(decrefList, incIdx);
      if (decIdx < 0) continue;

      // Verify the source variable is alive between incref and decref:
      //  - no store to srcVar (would change what it points to)
      //  - no decref of srcVar (would release the source reference)
      //  - no aliasing ops that could release srcVar transitively
      // Each is a range query over a presorted index list.
      if (HasIndexInRange(aliasingEvents, incIdx + 1, decIdx - 1)) continue;
      if (storeIdxByVar.TryGetValue(srcVar, out var srcStores)
          && HasIndexInRange(srcStores, incIdx + 1, decIdx - 1)) continue;
      if (decrefIdxByVar.TryGetValue(srcVar, out var srcDecrefs)
          && HasIndexInRange(srcDecrefs, incIdx + 1, decIdx - 1)) continue;

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

  /// Returns the smallest index in the ascending-sorted list that is strictly
  /// greater than `target`, or -1 if none.
  private static int FirstGreaterThan(List<int> sorted, int target) {
    int lo = 0, hi = sorted.Count;
    while (lo < hi) {
      int mid = (lo + hi) >>> 1;
      if (sorted[mid] > target) hi = mid;
      else lo = mid + 1;
    }
    return lo < sorted.Count ? sorted[lo] : -1;
  }

  /// Returns true if the ascending-sorted list contains any value in [from, to].
  private static bool HasIndexInRange(List<int> sorted, int from, int to) {
    if (from > to || sorted.Count == 0) return false;
    int lo = 0, hi = sorted.Count;
    while (lo < hi) {
      int mid = (lo + hi) >>> 1;
      if (sorted[mid] >= from) hi = mid;
      else lo = mid + 1;
    }
    return lo < sorted.Count && sorted[lo] <= to;
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
