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
    // SSA value ID → first variable it was stored to.
    // When the same SSA heap pointer is stored into multiple slots, the first
    // slot is the canonical holder; subsequent slots are aliases of it. This
    // catches `var b = a` patterns that the compiler lowers as two stores of
    // the same call result (`store %v, a; store %v, b`) with no intervening
    // reload, as well as the for-in lowering that stores the iterator-current
    // result into both `__forin_result` and the user's loop variable.
    var firstStoreOf = new Dictionary<int, string>();
    // Variable name → source variable it was assigned from (alias tracking)
    var aliasSource = new Dictionary<string, string>();
    // Variable names whose aliasSource came from the firstStoreOf fallback
    // rather than a load-based alias. These need an extra safety check at
    // elimination time: the source slot must have its own decref in the block,
    // otherwise nothing would free the shared allocation after we elide the
    // second slot's refcount pair. Load-based aliases don't need this check
    // because the load-source slot was populated via some earlier assign that
    // already owns its own reference.
    var aliasFromStore = new HashSet<string>();
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
        // Prefer a load-based source (classic `var b = a` lowering: load a; store b).
        if (loadedFrom.TryGetValue(store.Value.Id, out var srcVar) && srcVar != store.VarName) {
          aliasSource[store.VarName] = srcVar;
          aliasFromStore.Remove(store.VarName);
        } else if (firstStoreOf.TryGetValue(store.Value.Id, out var firstSlot) && firstSlot != store.VarName) {
          // No load-based source, but the same SSA value already lives in
          // another slot. That earlier slot is an alias anchor for this one.
          aliasSource[store.VarName] = firstSlot;
          aliasFromStore.Add(store.VarName);
        }
        // Record the first slot that received this SSA value.
        if (!firstStoreOf.ContainsKey(store.Value.Id)) {
          firstStoreOf[store.Value.Id] = store.VarName;
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
      decrefIdxByVar.TryGetValue(srcVar, out var srcDecrefs);
      if (srcDecrefs != null
          && HasIndexInRange(srcDecrefs, incIdx + 1, decIdx - 1)) continue;

      // For firstStoreOf-sourced aliases (same SSA value stored into two slots,
      // no intervening load): srcVar must have its own decref at-or-after the
      // candidate decref. Otherwise the only refcount lifecycle on the shared
      // allocation was varName's pair — eliding it leaks the allocation.
      // Load-sourced aliases don't need this: the source slot was populated by
      // some earlier assign that already owns its own reference.
      if (aliasFromStore.Contains(varName)
          && (srcDecrefs == null || FirstGreaterThan(srcDecrefs, decIdx - 1) < 0)) {
        continue;
      }

      // Safe to cancel this incref/decref pair
      Logger.Debug(LogCategory.Ir, $"  RefcountOpt: cancel incref@{incIdx}/decref@{decIdx} for var '{varName}' (source '{srcVar}' alive) in {block.Name}");
      toRemove.Add(incIdx);
      toRemove.Add(decIdx);

      // The load that produced each heap-pointer was emitted solely to feed
      // this refcount op. If it has no other users, drop it too — otherwise
      // later passes see an orphaned load that pessimizes their alias view.
      TryRemoveFeedingLoad(ops, incIdx, useCounts, toRemove);
      TryRemoveFeedingLoad(ops, decIdx, useCounts, toRemove);
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

  /// Drop the load that produced the heap-pointer operand of the refcount op at
  /// `rcIdx`, when that load's result is only consumed by the refcount op we're
  /// already removing.
  private static void TryRemoveFeedingLoad(List<StandardOp> ops, int rcIdx, Dictionary<int, int> useCounts, HashSet<int> toRemove) {
    if (GetRefcountKind(ops[rcIdx], out var heapPtr) == RefcountKind.None) return;
    int valueId = heapPtr!.Id;

    for (int j = rcIdx - 1; j >= 0; j--) {
      if (ops[j] is not ILoadOp load || load.Result.Id != valueId) continue;
      if (useCounts.GetValueOrDefault(load.Result.Id) == 1) toRemove.Add(j);
      return;
    }
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
