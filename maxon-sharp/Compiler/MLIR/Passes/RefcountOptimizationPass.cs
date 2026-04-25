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
/// Two sub-passes run in sequence per function:
///
/// 1. Intra-block: the incref and its matching decref are in the same block.
///    The original approach — linear scan within a block.
///
/// 2. Cross-block: the incref lands in one block (e.g. the function entry) and
///    the matching decref is in a dominated successor (e.g. a scope-exit block).
///    Safety is checked via a dominator tree and per-block kill-sets so that
///    every path through intermediate blocks is verified free of srcVar mutations.
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
    var funcLookup = new Dictionary<string, IrFunction<StandardOp>>(module.Functions.Count);
    foreach (var f in module.Functions) funcLookup[f.Name] = f;

    var hot = StageTimer.HotFunctions > 0 ? new List<(string Name, long Ms)>() : null;

    // Per-function work is local: it reads funcLookup (read-only after the
    // build above) and BorrowOnlyParamIndices (set by ParameterRetentionAnalysis
    // earlier in the pipeline, never mutated here). It only mutates
    // block.Operations within the current func, so concurrent execution across
    // distinct funcs is safe.
    ParallelFunctions.Run(module, func => {
      var useCounts = ComputeUseCounts(func);
      foreach (var block in func.Body.Blocks) {
        CancelRedundantRefcounts(block, useCounts, funcLookup);
      }
      CancelCrossBlockRedundantRefcounts(func, useCounts, funcLookup);
      CancelLoopInvariantRedundantRefcounts(func, useCounts, funcLookup);
      CancelGlobalLoadOrphanBrackets(func, funcLookup);
    }, hot);

    if (hot != null) StageTimer.PrintHotFunctions("refcount", hot);
  }

  /// <summary>
  /// Returns true if a direct (or try-) function call is borrow-only: every
  /// argument position is annotated borrow-only on the callee's
  /// <see cref="IrFunction{TOp}.BorrowOnlyParamIndices"/>. Calls to unknown
  /// callees, or to callees without a populated annotation, return false.
  ///
  /// Borrow-only calls cannot retain any of their arguments, so they cannot
  /// extend the caller's refcount obligation on an aliased slot past the call.
  /// Combined with Maxon's borrow convention (callees may not decref a
  /// borrowed parameter), this means a borrow-only call cannot affect the
  /// refcount of any allocation held by a surrounding slot — it's safe to
  /// treat as non-aliasing inside an incref/decref window. Try-calls additionally
  /// rely on the caller's scope-end cleanup running symmetrically on the error
  /// path; the comment at <see cref="ClassifyAliasingOp"/> spells out the
  /// bracket-balance argument.
  /// </summary>
  private static bool IsBorrowOnlyCall(
      string calleeName,
      List<StdValue> args,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    if (!funcLookup.TryGetValue(calleeName, out var callee)) return false;
    var borrowOnly = callee.BorrowOnlyParamIndices;
    if (borrowOnly == null) return false;
    // A callee with fewer borrow-only params than arg positions retains some;
    // require every position to be annotated borrow-only.
    for (int i = 0; i < args.Count; i++) {
      if (!borrowOnly.Contains(i)) return false;
    }
    return true;
  }

  private static bool IsBorrowOnlyCall(
      StdCallOp call,
      Dictionary<string, IrFunction<StandardOp>> funcLookup)
    => IsBorrowOnlyCall(call.Callee, call.Args, funcLookup);

  private static bool IsBorrowOnlyCall(
      StdTryCallOp call,
      Dictionary<string, IrFunction<StandardOp>> funcLookup)
    => IsBorrowOnlyCall(call.Callee, call.Args, funcLookup);

  private static Dictionary<int, int> ComputeUseCounts(IrFunction<StandardOp> func) {
    // Pre-size to roughly the number of ops; one ReadValue per op is typical
    // and dictionary growth+rehash dominates this method's allocation cost
    // when functions are even moderately large.
    int approxOps = 0;
    foreach (var block in func.Body.Blocks) approxOps += block.Operations.Count;
    var counts = new Dictionary<int, int>(approxOps);
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

  private static void CancelRedundantRefcounts(
      IrBlock<StandardOp> block,
      Dictionary<int, int> useCounts,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    var ops = block.Operations;

    // Incref index → variable name it operates on
    var increfVar = new Dictionary<int, string>();
    // Incref index → source variable that keeps the object alive
    var increfSource = new Dictionary<int, string>();
    // Incref indices whose alias came from firstStoreOf (needs extra safety check).
    var increfAliasFromStore = new HashSet<int>();
    // Decref index → variable name it operates on
    var decrefVar = new Dictionary<int, string>();

    AnalyzeAliases(ops, (i, varName, srcVar, isFromStore) => {
      increfVar[i] = varName;
      if (srcVar != null) {
        increfSource[i] = srcVar;
        if (isFromStore) increfAliasFromStore.Add(i);
      }
    }, (i, varName) => {
      decrefVar[i] = varName;
    });

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
    foreach (var (idx, varName) in decrefVar) AppendIndex(decrefIdxByVar, varName, idx);
    foreach (var list in decrefIdxByVar.Values) list.Sort();

    // "Aliasing event" indices: any op that could release an arbitrary heap
    // object, plus any decref of any variable (which we'll filter per source).
    // Sorted ascending.
    //
    // Two strictness tiers are populated; the one used depends on the alias
    // kind of each incref candidate (see window selection below):
    //   - aliasingEventsNoDirectCalls drops every StdCallOp. Used for
    //     aliasFromStore candidates — Maxon's borrow convention forbids the
    //     callee from decref'ing a borrowed parameter, so direct calls cannot
    //     release the srcVar object.
    //   - borrowAwareAliasingEvents drops only StdCallOps whose every
    //     parameter is annotated borrow-only by ParameterRetentionAnalysisPass.
    //     Used for load-based aliases — a borrow-only callee cannot retain
    //     any argument, so it cannot extend lifetimes in a way that would
    //     later release srcVar, and combined with the borrow convention it
    //     cannot release srcVar at the call site either.
    var aliasingEventsNoDirectCalls = new List<int>();
    var borrowAwareAliasingEvents = new List<int>();
    // Stores grouped by destination variable (sorted ascending).
    var storeIdxByVar = new Dictionary<string, List<int>>();
    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];
      ClassifyAliasingOp(op, funcLookup, out bool isStrict, out bool isBorrowAware);
      if (isStrict) aliasingEventsNoDirectCalls.Add(i);
      if (isBorrowAware) borrowAwareAliasingEvents.Add(i);
      if (op is IStoreOp st) AppendIndex(storeIdxByVar, st.VarName, i);
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
      //
      // For aliasFromStore candidates, all direct (non-try) function calls are
      // allowed in the window: Maxon's calling convention requires callees to
      // borrow parameters, so a call cannot release the srcVar object.
      //
      // For load-based aliases, only direct calls known to be borrow-only on
      // every argument position are allowed — those cannot retain, so they
      // cannot extend the caller's refcount obligation on the aliased slot
      // past the call. A call to an unknown or partially-retaining function
      // still counts as an aliasing event. This relaxation is what lands
      // Part 5 of the refcount-optimization roadmap (short-lived argument
      // temporaries around borrowed calls).
      var windowEvents = increfAliasFromStore.Contains(incIdx)
          ? aliasingEventsNoDirectCalls
          : borrowAwareAliasingEvents;
      if (HasIndexInRange(windowEvents, incIdx + 1, decIdx - 1)) continue;
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
      if (increfAliasFromStore.Contains(incIdx)
          && (srcDecrefs == null || FirstGreaterThan(srcDecrefs, decIdx - 1) < 0)) {
        continue;
      }

      // Safe to cancel this incref/decref pair
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

  // ─── Shared sub-pass data ────────────────────────────────────────────────

  /// <summary>
  /// An incref site found during alias analysis of a block.
  /// <c>SrcVar</c> is the aliased source slot when known; <c>null</c> when the
  /// slot has no recorded alias source. Cross-block elimination filters out
  /// null-source candidates early; loop-invariant elimination keeps them for
  /// diagnostics but also rejects them at the safety check.
  /// </summary>
  private sealed class IncrefSite {
    public required IrBlock<StandardOp> Block { get; init; }
    public required int OpIndex { get; init; }
    public required string VarName { get; init; }
    public required string? SrcVar { get; init; }
    /// True when the alias was established via firstStoreOf (same SSA value
    /// written to two slots) rather than a load-based alias.
    public required bool IsFromStore { get; init; }
  }

  /// <summary>Decref on a value loaded from <c>VarName</c>.</summary>
  private sealed class DecrefSite {
    public required IrBlock<StandardOp> Block { get; init; }
    public required int OpIndex { get; init; }
    public required string VarName { get; init; }
  }

  /// <summary>
  /// Per-block summary of ops that would kill a source variable's liveness.
  ///
  /// Two aliasing-event lists are populated for different safety regimes:
  ///  - <see cref="StrictAliasingEventIndices"/> excludes direct calls
  ///    entirely. Used where Maxon's borrow convention already rules out
  ///    direct-call interference regardless of callee identity (e.g. the
  ///    loop-invariant sub-pass's strict mode for aliasFromStore candidates).
  ///  - <see cref="BorrowAwareAliasingEventIndices"/> excludes direct calls
  ///    whose every argument position is annotated borrow-only on the
  ///    callee's <see cref="IrFunction{TOp}.BorrowOnlyParamIndices"/>. Used
  ///    by the intra-block and cross-block sub-passes for load-based
  ///    aliases: a borrow-only callee cannot retain, and under the borrow
  ///    convention cannot decref, any passed pointer — so it cannot affect
  ///    the refcount of the aliased source in any way.
  /// </summary>
  private sealed class BlockKillInfo {
    /// Variables that are stored-to or decreffed anywhere in this block.
    public required HashSet<string> KilledVars { get; init; }
    /// Aliasing event indices excluding direct calls (ascending).
    public required List<int> StrictAliasingEventIndices { get; init; }
    /// Aliasing event indices excluding direct calls that are known to be
    /// borrow-only on all argument positions (ascending).
    public required List<int> BorrowAwareAliasingEventIndices { get; init; }
    /// Store indices per variable (ascending) in this block.
    public required Dictionary<string, List<int>> StoreIndices { get; init; }
    /// Decref indices per variable (ascending) in this block.
    public required Dictionary<string, List<int>> DecrefIndices { get; init; }
  }

  /// <summary>
  /// Eliminates incref/decref pairs whose incref and matching decref are in
  /// different blocks but the incref block strictly dominates the decref block.
  /// </summary>
  private static void CancelCrossBlockRedundantRefcounts(
      IrFunction<StandardOp> func,
      Dictionary<int, int> useCounts,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    var blocks = func.Body.Blocks;
    if (blocks.Count < 2) return;

    // Build CFG and dominator tree.
    var cfg = CfgBuilder<StandardOp>.Build(
        blocks, StandardCfgHelpers.GetSuccessors, StandardCfgHelpers.EndsWithTerminator);
    var domTree = DominatorTree.Build(blocks[0].Name, cfg);

    // Index blocks by name for fast lookup.
    var blockByName = new Dictionary<string, IrBlock<StandardOp>>(blocks.Count);
    foreach (var b in blocks) blockByName[b.Name] = b;

    // Build per-block kill info.
    var killInfo = new Dictionary<string, BlockKillInfo>(blocks.Count);
    foreach (var b in blocks) killInfo[b.Name] = BuildBlockKillInfo(b, funcLookup);

    // Parameters are owned by the caller; the callee need not have a local
    // decref of the parameter slot, so the aliasFromStore safety check is
    // skipped when srcVar is one of these slots.
    var paramVarNames = CollectParameterSlotNames(blocks[0]);

    // Pass 1: build function-level alias maps from all blocks. These capture
    // alias relationships established in any block — e.g. the storeAlias for
    // __try_result_3 → __managed_list_nav_s* is established in `entry` but
    // the decref fires in `otherwise_stmt_*`. Without a cross-block map, the
    // per-block dual-indexing in CollectRefcountSites would miss it.
    var aliasSourceGlobal = new Dictionary<string, string>();
    var aliasFromStoreGlobal = new HashSet<string>();
    var storeAliasGlobal = new Dictionary<string, string>();
    foreach (var block in blocks) {
      AnalyzeAliases(block.Operations, (_, _, _, _) => { }, (_, _) => { },
          aliasSourceGlobal, aliasFromStoreGlobal, storeAliasGlobal);
    }

    // Pass 2: collect incref candidates and decrefs, dual-indexing decrefs
    // under sibling slot names using the function-level alias maps.
    var increfCandidates = new List<IncrefSite>();
    var decrefsByVar = new Dictionary<string, List<DecrefSite>>();

    foreach (var block in blocks) {
      CollectRefcountSites(block, increfCandidates, decrefsByVar, requireSrcVar: true,
          aliasSourceOverride: aliasSourceGlobal,
          aliasFromStoreOverride: aliasFromStoreGlobal,
          storeAliasOverride: storeAliasGlobal);
    }

    // For each incref candidate, find dominated decrefs and check safety.
    var toRemovePerBlock = new Dictionary<string, HashSet<int>>();

    foreach (var inc in increfCandidates) {
      if (!decrefsByVar.TryGetValue(inc.VarName, out var decrefList)) {
        continue;
      }

      var effectiveSrcVar = inc.SrcVar!;

      // srcVar must own a reference. Ownership is established by any of:
      //  1. Being a function parameter (caller owns it).
      //  2. Having an explicit decref somewhere in this function (including
      //     via a dual-indexed aliasFromStore alias — see CollectRefcountSites).
      //  3. Its own aliasSource being a parameter or having a decref.
      //     This handles load_indirect chains where srcVar itself is a
      //     borrowed sub-slot (e.g. __selfref_*) whose ancestor is `self`.
      // If srcVar has no decref at all and no owning ancestor, the
      // incref/decref pair on varName IS the sole owner, and eliminating it
      // would leave the object permanently at rc=0 (a leak).
      if (!decrefsByVar.ContainsKey(effectiveSrcVar) && !paramVarNames.Contains(effectiveSrcVar)) {
        // Walk one alias level up: if srcVar's own source is a parameter or
        // has a decref, ownership is satisfied transitively.
        bool ownedTransitively = aliasSourceGlobal.TryGetValue(effectiveSrcVar, out var grandSrc)
            && (paramVarNames.Contains(grandSrc) || decrefsByVar.ContainsKey(grandSrc));
        if (!ownedTransitively) continue;
      }

      // Collect every decref of varName reachable from the incref block.
      // Paired with the incref, they cover all the normal-exit paths on which
      // the emitter generated a scope-end cleanup. Multi-exit buckets (e.g. a
      // for-in match arm and a no-match continue path both decrefing the
      // same slot at their respective scope ends) would previously bail here
      // because the old rule required exactly one reachable decref block.
      var reachableFromInc = BfsReachable(inc.Block.Name, cfg.Successors);
      var reachableDecs = decrefList
          .Where(dec => dec.Block.Name != inc.Block.Name
                     && reachableFromInc.Contains(dec.Block.Name))
          .ToList();

      if (reachableDecs.Count == 0) continue;

      // Every matched decref must be strictly dominated by the incref.
      // Otherwise there is a path into the decref that does not execute the
      // incref, and removing the pair would leave that path with an
      // unmatched decref.
      bool allDominated = true;
      foreach (var dec in reachableDecs) {
        if (!domTree.StrictlyDominates(inc.Block.Name, dec.Block.Name)) {
          allDominated = false;
          break;
        }
      }
      if (!allDominated) continue;

      // Per-pair window safety: for each (inc, dec) pair, the range from the
      // incref to the decref must not contain a store or decref of srcVar.
      // Also verify srcVar is not decreffed on any path that bypasses the
      // entire matched-dec set — if a path from inc reaches a side decref of
      // srcVar without going through one of our matched decs, eliminating
      // the bracket would leave varName dangling on that path.
      var matchedDecBlocks = new HashSet<string>(reachableDecs.Select(d => d.Block.Name));
      var srcVarBypassBarriers = new HashSet<string>(matchedDecBlocks) { inc.Block.Name };
      bool bypassFail = HasDecrefInSuccessors(inc.Block.Name, effectiveSrcVar, cfg, killInfo, srcVarBypassBarriers);
      if (bypassFail) continue;

      bool allPairsSafe = true;
      foreach (var dec in reachableDecs) {
        bool pairSafe = IsCrossBlockPairSafe(inc, effectiveSrcVar, dec, cfg, killInfo, aliasSourceGlobal);
        if (!pairSafe) { allPairsSafe = false; break; }

        // aliasFromStore safety: srcVar must have a decref reachable at or
        // after dec.Block / dec.OpIndex, so the shared allocation is freed.
        // Exception: parameters — the caller owns cleanup.
        // Exception: when the prefix-kill relaxation applies, srcVar's own
        // decref already fired in the prefix of dec.Block and freed the
        // allocation there; no later decref is required.
        if (inc.IsFromStore && !paramVarNames.Contains(effectiveSrcVar)
            && !HasReachableDecrefAfter(effectiveSrcVar, dec, cfg, killInfo)
            && !PrefixDecrefOfSrcVarExists(killInfo[dec.Block.Name], effectiveSrcVar, dec)) {
          allPairsSafe = false;
          break;
        }
      }
      if (!allPairsSafe) continue;

      // Non-overlap: on a single forward execution from the incref, at most
      // one of the matched decrefs may fire. From each matched dec, BFS
      // forward with the incref block as a barrier (loop back-edges that
      // re-enter the incref start a new iteration, not relevant here) and
      // check no *other* matched dec block is reached.
      bool overlap = false;
      foreach (var dec in reachableDecs) {
        var visited = new HashSet<string> { inc.Block.Name };
        var worklist = new Queue<string>();
        if (cfg.Successors.TryGetValue(dec.Block.Name, out var succs0)) {
          foreach (var s in succs0) if (visited.Add(s)) worklist.Enqueue(s);
        }
        while (worklist.Count > 0) {
          var name = worklist.Dequeue();
          if (matchedDecBlocks.Contains(name)) { overlap = true; break; }
          if (cfg.Successors.TryGetValue(name, out var succs)) {
            foreach (var s in succs) if (visited.Add(s)) worklist.Enqueue(s);
          }
        }
        if (overlap) break;
      }
      if (overlap) continue;

      // Post-dominance: every forward path from the incref to a function exit
      // must pass through at least one matched decref block. Otherwise there's
      // an execution path (e.g. an early `return varName` from inside a loop
      // where the decref of varName lives in the loop-continue branch) that
      // runs the incref but never the decref. Removing the bracket on that
      // path would leave the pointee under-referenced — the caller receives a
      // pointer whose only surviving refcount slot is still owned by the
      // source variable (e.g. the array the loop was iterating), and when
      // the caller decrefs, the source later double-frees.
      //
      // BFS from inc.Block's successors with matchedDecBlocks as barriers.
      // A path "escapes" if it reaches a block with no successors (function
      // exit: return/throw) without going through any matched dec block.
      {
        var visited = new HashSet<string>();
        var worklist = new Queue<string>();
        if (cfg.Successors.TryGetValue(inc.Block.Name, out var incSuccs)) {
          foreach (var s in incSuccs) {
            if (matchedDecBlocks.Contains(s)) continue;
            if (visited.Add(s)) worklist.Enqueue(s);
          }
        }
        bool escapes = false;
        // Block has no successors iff it ends with a function-exit terminator
        // (return/throw). An unterminated block is a compiler bug, but treat
        // it conservatively as an escape.
        while (worklist.Count > 0) {
          var name = worklist.Dequeue();
          if (!cfg.Successors.TryGetValue(name, out var succs) || succs.Count == 0) {
            escapes = true; break;
          }
          foreach (var s in succs) {
            if (matchedDecBlocks.Contains(s)) continue;
            if (visited.Add(s)) worklist.Enqueue(s);
          }
        }
        if (escapes) continue;
      }
      var incSet = GetOrCreateRemoveSet(toRemovePerBlock, inc.Block.Name);
      incSet.Add(inc.OpIndex);
      TryRemoveFeedingLoad(inc.Block.Operations, inc.OpIndex, useCounts, incSet);
      foreach (var dec in reachableDecs) {
        var decSet = GetOrCreateRemoveSet(toRemovePerBlock, dec.Block.Name);
        decSet.Add(dec.OpIndex);
        TryRemoveFeedingLoad(dec.Block.Operations, dec.OpIndex, useCounts, decSet);
      }
    }

    ApplyRemovals(toRemovePerBlock, blockByName);
  }

  // ─── Loop-invariant sub-pass ─────────────────────────────────────────────

  /// <summary>
  /// Eliminates incref/decref pairs that live inside a natural loop body and
  /// operate on a slot whose pointer value does not change within the
  /// window between the incref and the decref.
  ///
  /// This covers two related shapes:
  ///  1. The slot is truly loop-invariant (never stored in the loop body).
  ///     The scope-level reference that populated the slot is what anchors
  ///     the pointee's liveness; the per-iteration bump+release is overhead.
  ///  2. A fresh value is stored into the slot early in the iteration, then
  ///     an <c>incref slot; borrowed_call; decref slot</c> bracket wraps a
  ///     subsequent use. The bracket adds +1/-1 to the refcount around a
  ///     call that neither retains via its argument contract nor decrefs
  ///     the passed value — Maxon's direct-call borrow convention. The net
  ///     effect is zero.
  ///
  /// Safety across the window (incref → decref):
  ///  - No store to <c>VarName</c> (the cached pointer would become stale).
  ///  - No decref of <c>VarName</c> (another path already released the ref).
  ///  - No "aliasing event" that could decref the pointee: <c>store_indirect</c>,
  ///    <c>mem_copy</c>, runtime calls other than <c>mm_incref</c>, try-calls
  ///    (their error paths run scope cleanup).
  ///    Direct <c>func.call</c> ops are *allowed* in the window under
  ///    Maxon's borrow convention: the callee receives a borrowed pointer
  ///    and cannot decref it without an independent reference of its own.
  ///    Re-matching of an already-claimed decref is prevented by removing
  ///    it from the candidate list after a successful pairing.
  /// </summary>
  private static void CancelLoopInvariantRedundantRefcounts(
      IrFunction<StandardOp> func,
      Dictionary<int, int> useCounts,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    var blocks = func.Body.Blocks;
    if (blocks.Count < 2) return;

    var cfg = CfgBuilder<StandardOp>.Build(
        blocks, StandardCfgHelpers.GetSuccessors, StandardCfgHelpers.EndsWithTerminator);
    var domTree = DominatorTree.Build(blocks[0].Name, cfg);
    var loops = NaturalLoops.Find(cfg, domTree);
    if (loops.Count == 0) return;

    var blockByName = new Dictionary<string, IrBlock<StandardOp>>(blocks.Count);
    foreach (var b in blocks) blockByName[b.Name] = b;

    var killInfo = new Dictionary<string, BlockKillInfo>(blocks.Count);
    foreach (var b in blocks) killInfo[b.Name] = BuildBlockKillInfo(b, funcLookup);

    // Function-wide decref map. Decrefs outside any loop body are still
    // relevant for the "exactly one reachable decref block" safety check:
    // scope cleanup on break paths lives outside the loop body but is
    // reachable from the incref. aliasFromStore decrefs are dual-indexed
    // under the source key for the same reason as in CollectRefcountSites.
    var decrefsByVarAllBlocks = new Dictionary<string, List<DecrefSite>>();
    foreach (var block in blocks) {
      CollectRefcountSites(block, [], decrefsByVarAllBlocks, requireSrcVar: false);
    }

    // Function parameters own a reference supplied by the caller; aliasFromStore
    // candidates sourced from a parameter slot don't need a local decref of
    // the source to satisfy the allocation-freeing invariant.
    var paramVarNames = CollectParameterSlotNames(blocks[0]);

    var toRemovePerBlock = new Dictionary<string, HashSet<int>>();

    foreach (var loop in loops) {
      EliminateLoopInvariantPairs(loop, blockByName, killInfo, cfg, useCounts,
          toRemovePerBlock, decrefsByVarAllBlocks, paramVarNames);
    }

    ApplyRemovals(toRemovePerBlock, blockByName);
  }

  private static void EliminateLoopInvariantPairs(
      NaturalLoop loop,
      Dictionary<string, IrBlock<StandardOp>> blockByName,
      Dictionary<string, BlockKillInfo> killInfo,
      CfgData cfg,
      Dictionary<int, int> useCounts,
      Dictionary<string, HashSet<int>> toRemovePerBlock,
      Dictionary<string, List<DecrefSite>> decrefsByVarAllBlocks,
      HashSet<string> paramVarNames) {
    // Increfs are restricted to the loop body (we only optimize pairs fully
    // inside the loop). Decrefs for pairing come from the loop body too. The
    // function-wide decref map is used below for the "exactly one reachable
    // decref block" safety check.
    var increfs = new List<IncrefSite>();
    var decrefsByVar = new Dictionary<string, List<DecrefSite>>();

    foreach (var blockName in loop.Body) {
      CollectRefcountSites(blockByName[blockName], increfs, decrefsByVar, requireSrcVar: false);
    }

    if (increfs.Count == 0) return;

    foreach (var inc in increfs) {
      // An incref without a known alias source has no external anchor for the
      // pointee's liveness. Eliminating such a pair leaves a fresh mm_alloc
      // result (rc=0) to leak: the in-loop incref is what bumped rc to 1, and
      // without it the scope-end decref_if_nonnull skips (slot was zeroed) and
      // the allocation is never freed. Require an alias source.
      if (inc.SrcVar == null) continue;

      // srcVar must own a reference. Ownership is established either by being
      // a function parameter (caller owns it) or by having an explicit decref
      // somewhere in this function. Mirrors the cross-block sub-pass check.
      if (!decrefsByVarAllBlocks.ContainsKey(inc.SrcVar) && !paramVarNames.Contains(inc.SrcVar)) continue;

      if (!decrefsByVar.TryGetValue(inc.VarName, out var decrefList)) continue;
      if (!decrefsByVarAllBlocks.TryGetValue(inc.VarName, out var allDecrefs)) continue;

      // Safety: find all decrefs of VarName reachable from the incref in the
      // full CFG (not restricted to the loop body — a break path that leaves
      // the body still runs scope cleanup on VarName). Then require them to
      // all be in the *same* block: eliminating a single (incref, decref)
      // pair leaves any sibling decref on another exit path unmatched, causing
      // an underflow. The cross-block sub-pass uses the same rule; we widen
      // reachability here because scope cleanup on loop-exit paths lives
      // outside the loop body. A same-block decref after the incref is handled
      // by the pairing loop below, but combined with any other reachable
      // decref block the count is ambiguous and we still bail.
      var reachableFromInc = BfsReachable(inc.Block.Name, cfg.Successors);
      int reachableDecrefBlocks = allDecrefs
          .Where(dec => dec.Block.Name != inc.Block.Name && reachableFromInc.Contains(dec.Block.Name))
          .Select(dec => dec.Block.Name)
          .Distinct()
          .Count();

      bool sameBlockDecrefAvailable = allDecrefs.Any(d => d.Block.Name == inc.Block.Name && d.OpIndex > inc.OpIndex);
      int allowedReachable = sameBlockDecrefAvailable ? 0 : 1;
      if (reachableDecrefBlocks != allowedReachable) continue;

      DecrefSite? matchedDec = null;
      foreach (var dec in decrefList) {
        if (!IsLoopPairWindowSafe(inc, dec, loop, cfg, killInfo)) continue;

        // srcVar must also stay alive across the window: no stores, no decrefs
        // in the loop body. Same proof the cross-block sub-pass performs —
        // folded into the window check by re-running against inc.SrcVar.
        if (LoopWindowKillsVar(inc, dec, loop, cfg, killInfo, inc.SrcVar)) continue;

        // aliasFromStore: same-SSA-in-two-slots alias. The only refcount
        // lifecycle on the shared allocation is VarName's pair — eliding it
        // leaks unless srcVar has its own decref reachable at-or-after the
        // candidate decref. Params are exempt (caller handles cleanup).
        if (inc.IsFromStore && !paramVarNames.Contains(inc.SrcVar)
            && !AnyDecrefReachableAtOrAfter(inc.SrcVar, dec, cfg, decrefsByVarAllBlocks)) continue;

        matchedDec = dec;
        break;
      }

      if (matchedDec == null) continue;

      // Post-dominance: every forward path from the incref to a function
      // exit must pass through the matched decref block. See the matching
      // check in CancelCrossBlockRedundantRefcounts for the rationale.
      if (matchedDec.Block.Name != inc.Block.Name) {
        var visited = new HashSet<string>();
        var worklist = new Queue<string>();
        var barrier = matchedDec.Block.Name;
        if (cfg.Successors.TryGetValue(inc.Block.Name, out var incSuccs)) {
          foreach (var s in incSuccs) {
            if (s == barrier) continue;
            if (visited.Add(s)) worklist.Enqueue(s);
          }
        }
        bool escapes = false;
        while (worklist.Count > 0) {
          var name = worklist.Dequeue();
          if (!cfg.Successors.TryGetValue(name, out var succs) || succs.Count == 0) {
            escapes = true; break;
          }
          foreach (var s in succs) {
            if (s == barrier) continue;
            if (visited.Add(s)) worklist.Enqueue(s);
          }
        }
        if (escapes) continue;
      }
      var incSet = GetOrCreateRemoveSet(toRemovePerBlock, inc.Block.Name);
      incSet.Add(inc.OpIndex);
      var decSet = GetOrCreateRemoveSet(toRemovePerBlock, matchedDec.Block.Name);
      decSet.Add(matchedDec.OpIndex);

      TryRemoveFeedingLoad(inc.Block.Operations, inc.OpIndex, useCounts, incSet);
      TryRemoveFeedingLoad(matchedDec.Block.Operations, matchedDec.OpIndex, useCounts, decSet);

      // Prevent re-matching of the same decref.
      decrefList.Remove(matchedDec);
    }
  }

  /// <summary>
  /// Safety check for the loop-invariant window. Tests that every block on
  /// every body-internal path from <paramref name="inc"/> to
  /// <paramref name="dec"/> is free of stores to <c>inc.VarName</c>, decrefs
  /// of <c>inc.VarName</c>, and strict aliasing events (indirect stores,
  /// mem-copies, try-calls, runtime calls other than mm_incref). Direct
  /// <c>func.call</c> ops are allowed — Maxon's calling convention borrows
  /// arguments, so a plain call cannot decref the passed value.
  /// </summary>
  private static bool IsLoopPairWindowSafe(
      IncrefSite inc,
      DecrefSite dec,
      NaturalLoop loop,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo) =>
      !AnyBlockOnWindowHasKill(inc, dec, loop, cfg, killInfo,
          (kill, from, to) => LoopWindowHasKill(kill, inc.VarName, from, to));

  /// <summary>
  /// Returns true when <paramref name="varName"/> is stored or decreffed
  /// anywhere in the loop-pair window. Used to verify that
  /// <c>inc.SrcVar</c>, the alias anchor, stays live across the entire window.
  /// </summary>
  private static bool LoopWindowKillsVar(
      IncrefSite inc,
      DecrefSite dec,
      NaturalLoop loop,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo,
      string varName) =>
      AnyBlockOnWindowHasKill(inc, dec, loop, cfg, killInfo,
          (kill, from, to) => VarHasKillInRange(kill, varName, from, to));

  /// <summary>
  /// Runs <paramref name="predicate"/> across the loop-body window from
  /// <paramref name="inc"/> to <paramref name="dec"/>: the incref-block suffix,
  /// the decref-block prefix, and every intermediate block in full. Returns
  /// true as soon as any segment satisfies the predicate.
  /// </summary>
  private static bool AnyBlockOnWindowHasKill(
      IncrefSite inc,
      DecrefSite dec,
      NaturalLoop loop,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo,
      Func<BlockKillInfo, int, int, bool> predicate) {
    if (inc.Block.Name == dec.Block.Name) {
      if (dec.OpIndex <= inc.OpIndex) return true;
      return predicate(killInfo[inc.Block.Name], inc.OpIndex + 1, dec.OpIndex - 1);
    }

    if (!BfsReachable(inc.Block.Name, cfg.Successors, loop.Body).Contains(dec.Block.Name))
      return true;

    if (predicate(killInfo[inc.Block.Name], inc.OpIndex + 1, int.MaxValue)) return true;
    if (predicate(killInfo[dec.Block.Name], 0, dec.OpIndex - 1)) return true;

    foreach (var midName in ComputeIntermediates(inc.Block.Name, dec.Block.Name, cfg, loop.Body)) {
      if (predicate(killInfo[midName], 0, int.MaxValue)) return true;
    }
    return false;
  }

  /// <summary>Loop-window kill predicate: strict aliasing + store/decref of varName.</summary>
  private static bool LoopWindowHasKill(BlockKillInfo kill, string varName, int from, int to) {
    if (HasIndexInRange(kill.StrictAliasingEventIndices, from, to)) return true;
    return VarHasKillInRange(kill, varName, from, to);
  }

  /// <summary>Per-var kill predicate: store or decref of varName in range.</summary>
  private static bool VarHasKillInRange(BlockKillInfo kill, string varName, int from, int to) {
    if (kill.StoreIndices.TryGetValue(varName, out var si)
        && HasIndexInRange(si, from, to)) return true;
    if (kill.DecrefIndices.TryGetValue(varName, out var di)
        && HasIndexInRange(di, from, to)) return true;
    return false;
  }

  /// <summary>
  /// Returns true when <paramref name="srcVar"/> has any decref at or after
  /// the given candidate decref site, considering both the decref-block tail
  /// and successor blocks. Used for the aliasFromStore safety check.
  /// </summary>
  private static bool AnyDecrefReachableAtOrAfter(
      string srcVar,
      DecrefSite dec,
      CfgData cfg,
      Dictionary<string, List<DecrefSite>> decrefsByVarAllBlocks) {
    if (!decrefsByVarAllBlocks.TryGetValue(srcVar, out var srcDecrefs)) return false;

    if (srcDecrefs.Any(d => d.Block.Name == dec.Block.Name && d.OpIndex >= dec.OpIndex)) return true;

    var reachable = BfsReachable(dec.Block.Name, cfg.Successors);
    reachable.Remove(dec.Block.Name);
    return srcDecrefs.Any(d => reachable.Contains(d.Block.Name));
  }

  /// <summary>
  /// Returns true when the full window from (incref block suffix) through all
  /// intermediate blocks to (decref block prefix) contains no op that kills
  /// srcVar liveness.
  ///
  /// In the incref-block suffix and the decref-block prefix: kills include any
  /// aliasing event (call, indirect-store, …), any store to srcVar, or any
  /// decref of srcVar.  In intermediate blocks: only direct stores and decrefs
  /// of srcVar are kills — function calls are not, because in Maxon's calling
  /// convention callees borrow parameters and do not decref them.
  ///
  /// Additionally: srcVar must not be decreffed on any path reachable from the
  /// incref block that bypasses the matched decref block. If srcVar is decreffed
  /// on a side path (e.g. a success branch sibling to the error branch), then
  /// eliminating the incref would leave varName without a reference on that path.
  /// </summary>
  private static bool IsCrossBlockPairSafe(
      IncrefSite inc,
      string srcVar,
      DecrefSite dec,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo,
      Dictionary<string, string>? aliasSourceGlobal = null) {
    var incKill = killInfo[inc.Block.Name];
    var decKill = killInfo[dec.Block.Name];

    // Suffix of incref block (from incIdx+1 to end).
    bool suffixKill = HasKillInRange(incKill, srcVar, inc.OpIndex + 1, int.MaxValue,
            SelectWindowEvents(incKill, inc.IsFromStore));
    if (suffixKill) return false;

    // Prefix of decref block (from 0 to decIdx-1).
    //
    // For aliasFromStore (IsFromStore=true) candidates: use the full aliasing-event
    // check with TryPrefixIsBenignSiblingCleanup relaxation (roadmap item #13).
    //
    // For borrowedFrom (IsFromStore=false) candidates: use a sibling-aware check
    // that blocks only when srcVar itself is stored/decreffed, OR when a sibling
    // variable (same aliasSource parent as srcVar) is decreffed. A sibling decref
    // kills srcVar because both hold the same runtime allocation; a non-sibling
    // mm_decref's destructor cannot reach srcVar since srcVar was never incref'd
    // into any non-sibling struct.
    bool prefixKill;
    bool prefixRelax = false;
    int prefixEnd = dec.OpIndex - 1;
    if (inc.IsFromStore) {
      prefixRelax = TryPrefixIsBenignSiblingCleanup(decKill, inc.VarName, srcVar, dec);
      prefixKill = HasKillInRange(decKill, srcVar, 0, prefixEnd, SelectWindowEvents(decKill, true));
    } else {
      // Sibling-aware prefix kill for borrowedFrom aliases.
      // Direct store or decref of srcVar kills it.
      prefixKill = VarHasKillInRange(decKill, srcVar, 0, prefixEnd);
      // Also block if any sibling (same aliasSource parent) is decreffed —
      // siblings hold the same allocation and a sibling decref is effectively
      // a decref of srcVar.
      if (!prefixKill && aliasSourceGlobal != null
          && aliasSourceGlobal.TryGetValue(srcVar, out var srcVarParent)) {
        foreach (var kv in decKill.DecrefIndices) {
          if (kv.Key == srcVar) continue;
          if (aliasSourceGlobal.TryGetValue(kv.Key, out var kParent)
              && kParent == srcVarParent
              && HasIndexInRange(kv.Value, 0, prefixEnd)) {
            prefixKill = true;
            break;
          }
        }
      }
    }
    if (!prefixRelax && prefixKill) {
      return false;
    }

    // Intermediate blocks: all blocks on any path from inc.Block to dec.Block
    // that are not inc.Block or dec.Block themselves. The backward walk from
    // dec uses inc.Block as a barrier so that blocks which only back-reach
    // dec by looping through inc (restarting the iteration) don't count as
    // intermediates — their decref/store activity is part of a *different*
    // iteration's window, not the one rooted at this incref.
    var intermediates = ComputeIntermediatesBarrierBackward(
        inc.Block.Name, dec.Block.Name, cfg);

    foreach (var midName in intermediates) {
      // Loop guard: if any intermediate has inc.Block as a successor, there
      // is a back-edge that could re-execute the incref — skip conservatively.
      if (cfg.Successors.TryGetValue(midName, out var midSuccs) && midSuccs.Contains(inc.Block.Name)) {
        return false;
      }

      var midKill = killInfo[midName];
      // Function calls alone do not endanger srcVar: in Maxon's calling
      // convention, callees borrow their parameters and the caller handles
      // scope-end decrefs. Only a direct store or decref of srcVar in an
      // intermediate block can break the alias or release the object early.
      if (midKill.KilledVars.Contains(srcVar)) {
        return false;
      }
    }

    // NOTE: the bypass check (srcVar decreffed on a path not covered by the
    // matched dec blocks) is performed once at the multi-exit level — it is
    // not per-pair, it needs the full set of matched decs as barriers.
    return true;
  }

  /// <summary>
  /// Returns true when <paramref name="srcVar"/> has at least one decref in
  /// the prefix of <paramref name="dec"/>'s block (before dec.OpIndex).
  /// Used alongside the prefix-kill relaxation: when the relaxation fires,
  /// the allocation is freed at srcVar's prefix decref, so the aliasFromStore
  /// "srcVar decref reachable at-or-after matched dec" invariant is
  /// already satisfied — just not *after* the matched dec.
  /// </summary>
  private static bool PrefixDecrefOfSrcVarExists(
      BlockKillInfo kill, string srcVar, DecrefSite dec) {
    if (!kill.DecrefIndices.TryGetValue(srcVar, out var srcDecrefs)) return false;
    return HasIndexInRange(srcDecrefs, 0, dec.OpIndex - 1);
  }

  /// <summary>
  /// aliasFromStore prefix-kill relaxation: returns true when the only kill
  /// of <paramref name="srcVar"/> in the prefix of <paramref name="dec"/>'s
  /// block is a single <c>mm_decref</c> of srcVar at some position X &lt;
  /// dec.OpIndex, AND the range (X, dec.OpIndex) contains only ops that
  /// cannot observe the freed allocation — specifically, no load of
  /// <paramref name="srcVar"/> or <paramref name="varName"/>, no
  /// <c>load_indirect</c>, and no call. Scope-end cleanup of sibling slots
  /// (load-different-slot → decref-if-nonnull → const 0 → store 0 → …)
  /// satisfies this shape.
  ///
  /// When this holds, the bracket elimination is safe even though the old
  /// rule would have flagged srcVar's prefix decref as a kill: after the
  /// elimination, the allocation is freed by srcVar's decref (just as it
  /// would be before elimination, because rc reached 0 there already —
  /// the eliminated incref/decref pair on varName was a pure +1/−1 bump
  /// on top). No op between srcVar's decref and the (now-removed) varName
  /// decref dereferences the allocation.
  /// </summary>
  private static bool TryPrefixIsBenignSiblingCleanup(
      BlockKillInfo kill,
      string varName,
      string srcVar,
      DecrefSite dec) {
    // At most one srcVar decref in the prefix. Zero is fine (the real
    // srcVar decref is later in the block or elsewhere in the CFG); 1 is
    // the classic "sibling scope-end decref before ours" shape. 2+ is
    // suspicious — bail.
    int prefixEnd = dec.OpIndex - 1;
    if (kill.DecrefIndices.TryGetValue(srcVar, out var srcDecrefs)) {
      int xCount = 0;
      foreach (var i in srcDecrefs) {
        if (i < 0 || i > prefixEnd) continue;
        if (++xCount > 1) return false;
      }
    }

    // No store to srcVar in the prefix — a store would change what srcVar
    // points to, breaking the alias invariant entirely.
    if (kill.StoreIndices.TryGetValue(srcVar, out var srcStores)
        && HasIndexInRange(srcStores, 0, prefixEnd)) {
      return false;
    }

    // No store to varName in the prefix either. (A varName store would
    // replace the alias mid-scope.)
    if (kill.StoreIndices.TryGetValue(varName, out var varStores)
        && HasIndexInRange(varStores, 0, prefixEnd)) {
      return false;
    }

    var ops = dec.Block.Operations;

    // A helper: find the load op at position < toIdx whose result feeds the
    // op at toIdx (the op must take one StdValue argument which came from a
    // load of the given slot). Returns -1 if no such load exists.
    int FindFeedingLoadOf(string slot, int toIdx) {
      if (toIdx < 0 || toIdx >= ops.Count) return -1;
      var target = ops[toIdx];
      int argId = target switch {
        StdCallRuntimeOp rt when rt.Args.Count > 0 => rt.Args[0].Id,
        StdCallRuntimeIfNonnullOp rti when rti.Args.Count > 0 => rti.Args[0].Id,
        _ => -1,
      };
      if (argId < 0) return -1;
      for (int i = toIdx - 1; i >= 0; i--) {
        if (ops[i] is ILoadOp feed && feed.VarName == slot
            && feed.Result is StdValue fv && fv.Id == argId) {
          return i;
        }
      }
      return -1;
    }

    // Identify the load that feeds the matched decref — that load will be
    // eliminated together with the decref via TryRemoveFeedingLoad, so we
    // allow it in the scan. Also identify the load that feeds srcVar's own
    // prefix decref (if any) so we don't flag it as "load of srcVar" in
    // the scan — it's part of srcVar's own scope-end cleanup.
    int feedingLoadIdx = FindFeedingLoadOf(varName, dec.OpIndex);
    int srcVarFeedingLoadIdx = -1;
    if (kill.DecrefIndices.TryGetValue(srcVar, out var srcDecrefsForFeed)) {
      foreach (var xIdx in srcDecrefsForFeed) {
        if (xIdx >= 0 && xIdx <= prefixEnd) {
          srcVarFeedingLoadIdx = FindFeedingLoadOf(srcVar, xIdx);
          break; // only one allowed per the earlier xCount check.
        }
      }
    }

    // Walk every op in the prefix [0, dec.OpIndex). The prefix is safe when
    // every op is one of:
    //   - a load of a slot other than srcVar or varName (unrelated cleanup
    //     or constant marshalling);
    //   - a store to a slot other than srcVar or varName (e.g. store-zero
    //     at scope-end of an unrelated slot);
    //   - a bare constant;
    //   - a null-guarded mm_decref / mm_incref runtime call. The arg SSA
    //     is bound by the pair-safety check to be a load of a slot that
    //     cannot alias the shared allocation (scope-end cleanup of a
    //     sibling slot), so the destructor side-effect can't release
    //     srcVar's allocation;
    //   - a plain mm_incref / mm_decref / mm_trace_transfer runtime call
    //     (same reasoning).
    //   - the single load that feeds the matched varName decref, which
    //     will be removed together with the decref.
    //
    // Any other op (load_indirect, store_indirect, direct/try call, load
    // of srcVar or varName, store to srcVar or varName) could observe or
    // mutate the shared allocation and is rejected.
    // Reject-what's-dangerous instead of whitelist-what's-benign, so that
    // mm-trace's extra scope-tag construction ops (lea_symdata, ptr_to_i64)
    // and other non-effectful shape-carriers don't cause the scan to bail.
    // Dangerous = anything that could observe or mutate the shared allocation.
    for (int i = 0; i < dec.OpIndex; i++) {
      if (i == feedingLoadIdx) continue;       // feeds the matched varName decref (will be removed together).
      if (i == srcVarFeedingLoadIdx) continue; // feeds srcVar's own scope-end decref (not our concern).
      var op = ops[i];
      // Loads of srcVar or varName: observe the allocation directly. The
      // feeding-load cases are handled by the skips above.
      if (op is ILoadOp load && (load.VarName == srcVar || load.VarName == varName)) return false;
      // Stores to srcVar or varName: replace the alias mid-scope.
      if (op is IStoreOp store && (store.VarName == srcVar || store.VarName == varName)) return false;
      // Indirect memory ops: can dereference / mutate heap through any pointer.
      if (op is StdLoadIndirectOp or StdStoreIndirectOp or StdMemCopyOp or StdMemCopyReverseOp) return false;
      // Direct or try-calls: could release or retain.
      if (op is StdCallOp or StdTryCallOp or StdTryCallRuntimeOp) return false;
      // Runtime calls other than mm_incref / mm_decref / mm_trace_transfer
      // (which have no observable effect on the shared allocation beyond
      // the refcount itself, which we've already accounted for).
      if (op is StdCallRuntimeOp rt
          && rt.Callee != "mm_incref" && rt.Callee != "mm_decref"
          && rt.Callee != "mm_trace_transfer") return false;
      if (op is StdCallRuntimeIfNonnullOp rtg
          && rtg.Callee != "mm_incref" && rtg.Callee != "mm_decref"
          && rtg.Callee != "mm_trace_transfer") return false;
    }

    return true;
  }

  /// <summary>
  /// Returns true if <paramref name="srcVar"/> has any decref reachable at or
  /// after (dec.Block, dec.OpIndex) via BFS through the CFG. Used for the
  /// aliasFromStore safety check.
  /// </summary>
  private static bool HasReachableDecrefAfter(
      string srcVar,
      DecrefSite dec,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo) {
    // Check the tail of dec.Block first (at or after dec.OpIndex).
    if (killInfo[dec.Block.Name].DecrefIndices.TryGetValue(srcVar, out var localDecrefs)
        && HasIndexInRange(localDecrefs, dec.OpIndex, int.MaxValue)) {
      return true;
    }

    return HasDecrefInSuccessors(dec.Block.Name, srcVar, cfg, killInfo,
        barriers: [dec.Block.Name]);
  }

  /// <summary>
  /// BFS forward from <paramref name="startBlock"/>'s successors looking for any
  /// block that contains a decref of <paramref name="varName"/>. Blocks in
  /// <paramref name="barriers"/> are excluded from the traversal.
  /// </summary>
  private static bool HasDecrefInSuccessors(
      string startBlock,
      string varName,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo,
      IEnumerable<string> barriers) {
    var visited = new HashSet<string>(barriers);
    var worklist = new Queue<string>();
    if (cfg.Successors.TryGetValue(startBlock, out var startSuccs)) {
      foreach (var s in startSuccs) { if (visited.Add(s)) worklist.Enqueue(s); }
    }

    while (worklist.Count > 0) {
      var name = worklist.Dequeue();
      if (killInfo.TryGetValue(name, out var info) && info.DecrefIndices.ContainsKey(varName))
        return true;
      if (cfg.Successors.TryGetValue(name, out var succs)) {
        foreach (var s in succs) { if (visited.Add(s)) worklist.Enqueue(s); }
      }
    }

    return false;
  }

  /// <summary>
  /// Computes the set of intermediate block names on any path from
  /// <paramref name="fromBlock"/> to <paramref name="toBlock"/>, exclusive of
  /// both endpoints.
  ///
  /// Computed as: ForwardReachable(fromBlock) ∩ BackwardReachable(toBlock)
  /// minus the two endpoints. When <paramref name="allowed"/> is non-null,
  /// traversal is restricted to blocks in that set — used by the loop-invariant
  /// sub-pass to compute intermediates inside a loop body.
  /// </summary>
  private static HashSet<string> ComputeIntermediates(
      string fromBlock, string toBlock, CfgData cfg, HashSet<string>? allowed = null) {
    var forward = BfsReachable(fromBlock, cfg.Successors, allowed);
    forward.Remove(fromBlock);

    var backward = BfsReachable(toBlock, cfg.Predecessors, allowed);
    backward.Remove(toBlock);

    forward.IntersectWith(backward);
    return forward; // now holds only intermediate blocks
  }

  /// <summary>
  /// Same as <see cref="ComputeIntermediates"/> except the backward walk
  /// from <paramref name="toBlock"/> treats <paramref name="fromBlock"/> as
  /// a barrier. Use this when loop back-edges can reach <paramref name="toBlock"/>
  /// by routing through <paramref name="fromBlock"/> again — the routed path
  /// represents a *different* iteration's window than the one we're
  /// analysing, and blocks only backward-reachable via that route
  /// shouldn't be treated as intermediates.
  /// </summary>
  private static HashSet<string> ComputeIntermediatesBarrierBackward(
      string fromBlock, string toBlock, CfgData cfg) {
    var forward = BfsReachable(fromBlock, cfg.Successors);
    forward.Remove(fromBlock);

    // Build an "allowed" set for the backward walk that excludes fromBlock
    // so BFS stops there — any block reached is on a path that doesn't loop
    // back through fromBlock.
    var allowedForBackward = new HashSet<string>(cfg.Predecessors.Keys);
    allowedForBackward.Remove(fromBlock);
    // toBlock must be in the allowed set for the walk to start there.
    allowedForBackward.Add(toBlock);
    var backward = BfsReachable(toBlock, cfg.Predecessors, allowedForBackward);
    backward.Remove(toBlock);

    forward.IntersectWith(backward);
    return forward;
  }

  /// <summary>
  /// Breadth-first reachability from <paramref name="start"/> over
  /// <paramref name="edges"/>. When <paramref name="allowed"/> is non-null,
  /// both the seed and every successor are rejected if they fall outside
  /// that set.
  /// </summary>
  private static HashSet<string> BfsReachable(
      string start, Dictionary<string, List<string>> edges, HashSet<string>? allowed = null) {
    var visited = new HashSet<string>();
    if (allowed == null || allowed.Contains(start)) visited.Add(start);
    var queue = new Queue<string>();
    queue.Enqueue(start);
    while (queue.Count > 0) {
      var cur = queue.Dequeue();
      if (!edges.TryGetValue(cur, out var nexts)) continue;
      foreach (var n in nexts) {
        if (allowed != null && !allowed.Contains(n)) continue;
        if (visited.Add(n)) queue.Enqueue(n);
      }
    }
    return visited;
  }

  /// <summary>
  /// Uses the shared <see cref="AnalyzeAliases"/> scan to emit
  /// <see cref="IncrefSite"/> / <see cref="DecrefSite"/> entries for one block.
  /// When <paramref name="requireSrcVar"/> is true, increfs whose alias source
  /// is unknown are dropped (the cross-block sub-pass cannot use them); when
  /// false, they are preserved and the caller filters as needed.
  ///
  /// Decrefs are dual-indexed under sibling variable names when the same SSA
  /// value was stored to multiple slots. Two kinds of siblings are covered:
  ///   • <c>aliasFromStore</c>: secondStoreOf alias (firstStoreOf path) —
  ///     decref under the alias is also indexed under the canonical first slot.
  ///   • <c>storeAlias</c>: borrowedFrom alias that has a firstStoreOf
  ///     predecessor — decref under the alias is also indexed under that
  ///     predecessor. Covers the advance/__ListIterator pattern where both
  ///     slots carry the same pointer via borrowedFrom but the incref fires on
  ///     the first-store slot and the decref fires on the later-store slot.
  ///
  /// Dual-indexing consults <paramref name="aliasSourceOverride"/> /
  /// <paramref name="aliasFromStoreOverride"/> / <paramref name="storeAliasOverride"/>
  /// when they are non-null — pass these to use function-level alias maps so
  /// cross-block alias relationships (store in block A, decref in block B) are
  /// handled correctly. Otherwise the lookups use the local per-block maps
  /// built during this scan.
  /// </summary>
  private static void CollectRefcountSites(
      IrBlock<StandardOp> block,
      List<IncrefSite> increfs,
      Dictionary<string, List<DecrefSite>> decrefsByVar,
      bool requireSrcVar,
      Dictionary<string, string>? aliasSourceOverride = null,
      HashSet<string>? aliasFromStoreOverride = null,
      Dictionary<string, string>? storeAliasOverride = null) {
    var localAliasSource = new Dictionary<string, string>();
    var localAliasFromStore = new HashSet<string>();
    var localStoreAlias = new Dictionary<string, string>();

    var dualAliasSource = aliasSourceOverride ?? localAliasSource;
    var dualAliasFromStore = aliasFromStoreOverride ?? localAliasFromStore;
    var dualStoreAlias = storeAliasOverride ?? localStoreAlias;

    AnalyzeAliases(block.Operations, (i, varName, srcVar, isFromStore) => {
      if (requireSrcVar && srcVar == null) return;
      increfs.Add(new IncrefSite {
        Block = block,
        OpIndex = i,
        VarName = varName,
        SrcVar = srcVar,
        IsFromStore = isFromStore,
      });
    }, (i, varName) => {
      AppendDecref(decrefsByVar, block, i, varName);
      // Dual-index: if this variable is a firstStoreOf alias (aliasFromStore),
      // also index the decref under its source (the canonical first slot).
      if (dualAliasFromStore.Contains(varName)
          && dualAliasSource.TryGetValue(varName, out var fsSource)) {
        AppendDecref(decrefsByVar, block, i, fsSource);
      }
      // Dual-index: if this variable is a borrowedFrom alias that also has a
      // firstStoreOf predecessor (storeAlias), index the decref under that
      // predecessor too. Handles sibling slots (e.g. __try_result_3 and
      // __managed_list_nav_s*) that hold the same SSA value via different stores.
      if (dualStoreAlias.TryGetValue(varName, out var storeSource)) {
        AppendDecref(decrefsByVar, block, i, storeSource);
      }
    }, localAliasSource, localAliasFromStore, localStoreAlias);
  }

  /// <summary>Appends a decref site to the per-variable list, creating it if needed.</summary>
  private static void AppendDecref(
      Dictionary<string, List<DecrefSite>> decrefsByVar,
      IrBlock<StandardOp> block, int opIndex, string varName) {
    if (!decrefsByVar.TryGetValue(varName, out var list)) {
      list = [];
      decrefsByVar[varName] = list;
    }
    list.Add(new DecrefSite { Block = block, OpIndex = opIndex, VarName = varName });
  }

  /// <summary>
  /// Shared alias analysis scan for one block.
  ///
  /// For each incref whose heap pointer traces back to a known variable, invokes
  /// <paramref name="onIncref"/> with (opIndex, varName, srcVar, isFromStore).
  /// srcVar is null when the incref's variable has no known alias source.
  /// For each decref on a known variable, invokes <paramref name="onDecref"/>
  /// with (opIndex, varName).
  ///
  /// Alias resolution: prefer a load-based source (classic `var b = a` lowering:
  /// load a; store b). If no load-based source exists but the same SSA value has
  /// already been stored to another slot (firstStoreOf fallback), that earlier
  /// slot becomes the alias anchor — with isFromStore=true to flag that an extra
  /// safety check is needed at elimination time (see CancelRedundantRefcounts).
  ///
  /// Optional out parameters receive the full aliasSource, aliasFromStore, and
  /// storeAlias maps built during the scan. Entries are merged (not replaced)
  /// into any pre-existing dictionary/set the caller passes in.
  ///
  /// storeAlias: variable → firstStoreOf-predecessor when a store has *both* a
  /// borrowedFrom source (which sets aliasSource) and a firstStoreOf predecessor
  /// (meaning another slot received the same SSA value earlier). Used to
  /// dual-index decrefs so the incref/decref matching can find sibling variables
  /// that hold the same physical pointer but were assigned via different paths.
  /// </summary>
  private static void AnalyzeAliases(
      List<StandardOp> ops,
      Action<int, string, string?, bool> onIncref,
      Action<int, string> onDecref,
      Dictionary<string, string>? aliasSourceOut = null,
      HashSet<string>? aliasFromStoreOut = null,
      Dictionary<string, string>? storeAliasOut = null) {
    // SSA value ID → variable name it was loaded from via a *direct* ILoadOp.
    // Used to attribute refcount ops to a specific variable: an incref/decref
    // on a value produced by `load var` *is* a refcount op on `var`, while
    // `load_indirect %parent+off` produces a sub-object whose refcount op
    // must not be attributed to `parent`.
    var loadedFrom = new Dictionary<int, string>();
    // SSA value ID → a variable whose live reference keeps this pointer alive.
    // This superset of loadedFrom also propagates through load_indirect: the
    // result of `load_indirect %p+off` borrows from whatever keeps %p alive.
    // Used only to establish aliasSource on stores (the receiving slot can
    // be kept alive by the borrowed-from variable), not to attribute refcount
    // ops to that variable.
    var borrowedFrom = new Dictionary<int, string>();
    // SSA value ID → first variable it was stored to. When the same SSA heap
    // pointer is stored into multiple slots, the first slot is the canonical
    // holder; subsequent slots are aliases of it. This catches `var b = a`
    // patterns lowered as two stores of the same call result with no intervening
    // reload, and the for-in lowering that stores the iterator-current result
    // into both `__forin_result` and the user's loop variable.
    var firstStoreOf = new Dictionary<int, string>();
    // Variable name → source variable it was assigned from (alias tracking).
    var aliasSource = new Dictionary<string, string>();
    // Variable names whose aliasSource came from the firstStoreOf fallback
    // rather than a load-based alias. These need an extra safety check at
    // elimination time (documented in CancelRedundantRefcounts).
    var aliasFromStore = new HashSet<string>();
    // Variable name → firstStoreOf-predecessor: set when a store uses the
    // borrowedFrom path (so aliasSource = borrow source) but the same SSA
    // value was stored to an earlier slot. Captures sibling-slot relationships
    // where both variables hold the same physical pointer via borrowedFrom but
    // the decref fires on one while the incref was attributed to the other.
    var storeAlias = new Dictionary<string, string>();

    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];

      if (op is ILoadOp load) {
        loadedFrom[load.Result.Id] = load.VarName;
        borrowedFrom[load.Result.Id] = load.VarName;
        continue;
      }

      // A load_indirect of a heap pointer that was itself borrowed from a known
      // variable produces a borrowed sub-object. Track the result as borrowing
      // from the same source so that a slot populated with this sub-object can
      // be deemed safely alive while the parent variable is alive. Crucially,
      // we do *not* add the result to `loadedFrom` — a refcount op on the
      // sub-object is not a refcount op on the parent variable (it operates on
      // the field contents, which can differ from the variable itself).
      if (op is StdLoadIndirectOp indirect
          && indirect.Result is StdI64 indResult
          && borrowedFrom.TryGetValue(indirect.BasePtr.Id, out var indSrc)) {
        borrowedFrom[indResult.Id] = indSrc;
        continue;
      }

      if (op is IStoreOp store) {
        if (borrowedFrom.TryGetValue(store.Value.Id, out var srcVar) && srcVar != store.VarName) {
          aliasSource[store.VarName] = srcVar;
          aliasFromStore.Remove(store.VarName);
          // If the same SSA value was already stored to an earlier slot, record
          // that predecessor as a storeAlias. This lets the decref dual-indexing
          // find the incref even when borrowedFrom dominates aliasSource.
          if (firstStoreOf.TryGetValue(store.Value.Id, out var firstSlotBf) && firstSlotBf != store.VarName) {
            storeAlias[store.VarName] = firstSlotBf;
          }
        } else if (firstStoreOf.TryGetValue(store.Value.Id, out var firstSlot) && firstSlot != store.VarName) {
          aliasSource[store.VarName] = firstSlot;
          aliasFromStore.Add(store.VarName);
        }
        if (!firstStoreOf.ContainsKey(store.Value.Id)) {
          firstStoreOf[store.Value.Id] = store.VarName;
        }
        continue;
      }

      var kind = GetRefcountKind(op, out var heapPtr);
      if (kind == RefcountKind.Incref && heapPtr != null
          && loadedFrom.TryGetValue(heapPtr.Id, out var incrVar)) {
        aliasSource.TryGetValue(incrVar, out var src);
        onIncref(i, incrVar, src, src != null && aliasFromStore.Contains(incrVar));
      } else if (kind == RefcountKind.Decref && heapPtr != null
          && loadedFrom.TryGetValue(heapPtr.Id, out var decrVar)) {
        onDecref(i, decrVar);
      }
    }

    if (aliasSourceOut != null)
      foreach (var kv in aliasSource) aliasSourceOut[kv.Key] = kv.Value;
    aliasFromStoreOut?.UnionWith(aliasFromStore);
    if (storeAliasOut != null)
      foreach (var kv in storeAlias) storeAliasOut[kv.Key] = kv.Value;
  }

  /// <summary>
  /// Builds the kill-info summary for a single block: aliasing events (three
  /// variants — see <see cref="BlockKillInfo"/>), store indices per variable,
  /// and decref indices per variable.
  /// </summary>
  private static BlockKillInfo BuildBlockKillInfo(
      IrBlock<StandardOp> block,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    var ops = block.Operations;
    var killedVars = new HashSet<string>();
    var strictAliasingIdxs = new List<int>();
    var borrowAwareAliasingIdxs = new List<int>();
    var storeIdxs = new Dictionary<string, List<int>>();
    var decrefIdxs = new Dictionary<string, List<int>>();

    // loadedFrom is needed to map heap-pointer SSA IDs to variable names for
    // decref index tracking. We do a local one-pass scan here. Only direct
    // `load var` produces an entry — a decref on the result of `load_indirect`
    // targets the field contents, not the parent variable, so we must not
    // treat it as a kill/decref of the parent (see AnalyzeAliases).
    var loadedFrom = new Dictionary<int, string>();

    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];

      if (op is ILoadOp load) {
        loadedFrom[load.Result.Id] = load.VarName;
        continue;
      }

      if (op is IStoreOp store) {
        killedVars.Add(store.VarName);
        AppendIndex(storeIdxs, store.VarName, i);
        continue;
      }

      ClassifyAliasingOp(op, funcLookup, out bool isStrict, out bool isBorrowAware);
      if (isStrict) strictAliasingIdxs.Add(i);
      if (isBorrowAware) borrowAwareAliasingIdxs.Add(i);

      var kind = GetRefcountKind(op, out var heapPtr);
      if (kind == RefcountKind.Decref && heapPtr != null
          && loadedFrom.TryGetValue(heapPtr.Id, out var varName)) {
        killedVars.Add(varName);
        AppendIndex(decrefIdxs, varName, i);
      }
    }

    return new BlockKillInfo {
      KilledVars = killedVars,
      StrictAliasingEventIndices = strictAliasingIdxs,
      BorrowAwareAliasingEventIndices = borrowAwareAliasingIdxs,
      StoreIndices = storeIdxs,
      DecrefIndices = decrefIdxs,
    };
  }

  /// Append <paramref name="idx"/> to the per-key index list, creating it if needed.
  private static void AppendIndex(Dictionary<string, List<int>> map, string key, int idx) {
    if (!map.TryGetValue(key, out var list)) {
      list = [];
      map[key] = list;
    }
    list.Add(idx);
  }

  /// <summary>
  /// Picks the aliasing-event list to use for a window rooted at an incref
  /// whose <see cref="IncrefSite.IsFromStore"/> flag is
  /// <paramref name="isFromStore"/>. aliasFromStore candidates use the
  /// <see cref="BlockKillInfo.StrictAliasingEventIndices"/> view — Maxon's
  /// borrow convention guarantees a direct-call callee cannot decref the
  /// srcVar allocation without holding its own independent reference, so
  /// direct calls in the window are safe. Load-based aliases use the
  /// borrow-aware view
  /// (<see cref="BlockKillInfo.BorrowAwareAliasingEventIndices"/>), which
  /// additionally drops direct calls to borrow-only callees.
  ///
  /// This matches the intra-block sub-pass's behaviour. Keeping the two
  /// sub-passes aligned on window tier avoids the inconsistency where
  /// intra-block would eliminate a pair but cross-block on the same shape
  /// would bail.
  /// </summary>
  private static List<int> SelectWindowEvents(BlockKillInfo kill, bool isFromStore) =>
      isFromStore ? kill.StrictAliasingEventIndices : kill.BorrowAwareAliasingEventIndices;

  /// <summary>
  /// Returns true if any op in the inclusive index range [<paramref name="fromIdx"/>,
  /// <paramref name="toIdx"/>] kills <paramref name="srcVar"/> liveness:
  /// aliasing event, store to srcVar, or decref of srcVar.
  ///
  /// <paramref name="aliasingEvents"/> selects which aliasing-event view the
  /// caller wants — typically <see cref="BlockKillInfo.BorrowAwareAliasingEventIndices"/>
  /// for load-based aliases (drops borrow-only direct calls) or
  /// <see cref="BlockKillInfo.StrictAliasingEventIndices"/> for aliasFromStore candidates.
  /// </summary>
  private static bool HasKillInRange(BlockKillInfo kill, string srcVar, int fromIdx, int toIdx, List<int> aliasingEvents) {
    if (HasIndexInRange(aliasingEvents, fromIdx, toIdx)) return true;
    if (kill.StoreIndices.TryGetValue(srcVar, out var si)
        && HasIndexInRange(si, fromIdx, toIdx)) return true;
    if (kill.DecrefIndices.TryGetValue(srcVar, out var di)
        && HasIndexInRange(di, fromIdx, toIdx)) return true;
    return false;
  }

  private static HashSet<int> GetOrCreateRemoveSet(Dictionary<string, HashSet<int>> map, string blockName) {
    if (!map.TryGetValue(blockName, out var set)) {
      set = [];
      map[blockName] = set;
    }
    return set;
  }

  /// Applies per-block removal sets in descending index order.
  private static void ApplyRemovals(
      Dictionary<string, HashSet<int>> toRemovePerBlock,
      Dictionary<string, IrBlock<StandardOp>> blockByName) {
    foreach (var (blockName, indices) in toRemovePerBlock) {
      if (indices.Count == 0) continue;
      var ops = blockByName[blockName].Operations;
      foreach (var idx in indices.OrderByDescending(i => i)) ops.RemoveAt(idx);
    }
  }

  /// <summary>
  /// Returns the variable slot names that receive function parameters in the
  /// entry block (the store destinations whose source SSA id comes from a
  /// StdParamOp).
  /// </summary>
  private static HashSet<string> CollectParameterSlotNames(IrBlock<StandardOp> entryBlock) {
    var paramSsaIds = new HashSet<int>();
    foreach (var op in entryBlock.Operations) {
      if (op is StdParamOp p) paramSsaIds.Add(p.Result.Id);
    }

    var paramSlots = new HashSet<string>();
    foreach (var op in entryBlock.Operations) {
      if (op is IStoreOp st && paramSsaIds.Contains(st.Value.Id))
        paramSlots.Add(st.VarName);
    }
    return paramSlots;
  }

  // ─── Shared helpers ───────────────────────────────────────────────────────

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
  /// Returns true if an operation could release arbitrary heap objects via side
  /// effects, excluding direct calls (<see cref="StdCallOp"/>). Direct calls are
  /// excluded because Maxon's borrow convention guarantees a callee cannot release
  /// the srcVar object without holding its own independent reference.
  ///
  /// Call sites needing the full borrow-aware relaxation (which additionally
  /// drops proven borrow-only try-calls) go through <see cref="ClassifyAliasingOp"/>.
  ///
  /// Note: <see cref="StdStoreIndirectOp"/>, <see cref="StdMemCopyOp"/>, and
  /// <see cref="StdMemCopyReverseOp"/> are intentionally NOT classified as
  /// aliasing here. Maxon does not use smart-pointer semantics — field overwrites
  /// do not trigger destructors or call mm_decref on the old value. A raw heap
  /// write cannot release srcVar's backing allocation. Only explicit mm_decref
  /// calls (tracked separately) can release objects.
  /// </summary>
  private static bool IsAliasingOp(StandardOp op) {
    if (op is StdTryCallOp or StdTryCallRuntimeOp) return true;
    // mm_decref can trigger destructors with arbitrary side effects.
    if (op is StdCallRuntimeOp rt && rt.Callee != "mm_incref" && rt.Callee != "mm_trace_transfer") return true;
    // Guarded runtime calls are C functions — no BorrowOnlyParamIndices annotation exists for them,
    // so no borrow-aware relaxation is possible here. mm_incref is the only safe exception.
    if (op is StdCallRuntimeIfNonnullOp grt && grt.Callee != "mm_incref") return true;
    return false;
  }

  /// <summary>
  /// Classifies <paramref name="op"/> against the two call-sensitive aliasing
  /// views that the pass tracks alongside the fully-conservative one:
  /// <paramref name="isStrict"/> is true when the op is aliasing and it is not
  /// a direct call (<see cref="StdCallOp"/>); <paramref name="isBorrowAware"/>
  /// is true when the op is aliasing and, if it is a direct or try-call, the
  /// callee is not known to be borrow-only on every argument position. A
  /// direct call to a proven borrow-only callee yields false for both flags —
  /// it cannot decref (borrow convention) and cannot retain (analysis verdict)
  /// any passed pointer.
  ///
  /// Try-calls receive the same relaxation as direct calls when borrow-only.
  /// Safety: on the error path, control either (a) terminates the function via
  /// <c>func.error_return</c> after <c>MaxonScopeEndOp</c> cleanup that already
  /// decrefs <c>srcVar</c> — symmetric with the success path's window-end
  /// decref, so the bracket balances on both paths; or (b) rejoins the success
  /// block without touching <c>srcVar</c> (the error handler only installs a
  /// fallback into the try-call's result slot) — <c>srcVar</c> is alive
  /// throughout and the window-end decref runs normally. The existing
  /// kill-in-range check for plain stores/decrefs of <c>srcVar</c> continues
  /// to guard against mutations anywhere in the window.
  ///
  /// <see cref="StdTryCallRuntimeOp"/> is intentionally excluded: its callees
  /// are C runtime functions never analysed by
  /// <see cref="ParameterRetentionAnalysisPass"/>, so no borrow-only verdict
  /// exists to consult.
  /// </summary>
  private static void ClassifyAliasingOp(
      StandardOp op,
      Dictionary<string, IrFunction<StandardOp>> funcLookup,
      out bool isStrict,
      out bool isBorrowAware) {
    if (op is StdCallOp callOp) {
      isStrict = false;
      isBorrowAware = !IsBorrowOnlyCall(callOp, funcLookup);
      return;
    }
    if (op is StdTryCallOp tryOp) {
      bool borrowOnly = IsBorrowOnlyCall(tryOp, funcLookup);
      isStrict = !borrowOnly;
      isBorrowAware = !borrowOnly;
      return;
    }
    isStrict = IsAliasingOp(op);
    isBorrowAware = isStrict;
  }

  // ─── Global-load orphan-bracket elimination (item #11) ────────────────────

  /// <summary>
  /// Describes a matched open triple for a global-load orphan bracket:
  ///   StdGlobalLoadI64Op @GlobalName → opaque i64 ptr
  ///   IStoreOp (ptr → TempName)
  ///   ILoadOp (TempName) → load result
  ///   StdCallRuntimeOp mm_incref (load result)        ← IncrefOpIndex
  /// All four ops live in the same block (<see cref="Block"/>).
  /// </summary>
  private sealed class GlobalBracketOpen {
    public required IrBlock<StandardOp> Block { get; init; }
    public required string GlobalName { get; init; }
    public required string TempName { get; init; }
    /// Index of the StdGlobalLoadI64Op in Block.
    public required int GlobalLoadOpIndex { get; init; }
    /// Index of the store of the global-load result into TempName.
    public required int StoreOpIndex { get; init; }
    /// Index of the load of TempName that feeds the mm_incref.
    public required int FeedingLoadOpIndex { get; init; }
    /// Index of the mm_incref op.
    public required int IncrefOpIndex { get; init; }
  }

  /// <summary>
  /// Describes a matched close triple for a global-load orphan bracket in a
  /// scope-end block:
  ///   ILoadOp (TempName) → ptr
  ///   StdCallRuntimeIfNonnullOp mm_decref (ptr)       ← DecrefOpIndex
  ///   StdConstI64Op 0                                 ← ZeroConstOpIndex  (-1 if absent)
  ///   IStoreOp (0 → TempName)                        ← ZeroStoreOpIndex  (-1 if absent)
  ///
  /// The zero-out tail (const0 + store-zero) may be absent when
  /// DeadStoreEliminationPass removes it before RefcountOptimizationPass runs.
  /// </summary>
  private sealed class GlobalBracketClose {
    public required IrBlock<StandardOp> Block { get; init; }
    public required string TempName { get; init; }
    /// Index of the ILoadOp that feeds the mm_decref.
    public required int FeedingLoadOpIndex { get; init; }
    /// Index of the mm_decref_if_nonnull op.
    public required int DecrefOpIndex { get; init; }
    /// Index of the StdConstI64Op 0, or -1 if the zero-out tail was already removed.
    public required int ZeroConstOpIndex { get; init; }
    /// Index of the IStoreOp(0 → TempName), or -1 if the zero-out tail was already removed.
    public required int ZeroStoreOpIndex { get; init; }
  }

  /// <summary>
  /// Eliminates incref/decref pairs whose subject is an orphan temp produced
  /// by the global-load lowering at MaxonToStandardConversion.cs:1801-1811.
  ///
  /// Emit pattern (open — in whichever block the global load occurs):
  ///   %p  = std.global_load_i64 @G
  ///   store %p → __global_G_N           (orphan temp, OwnershipFlags.Orphan)
  ///   %q  = load __global_G_N
  ///   mm_incref %q                       ← bracket open
  ///
  /// Emit pattern (close — scope-end cleanup per temps.OrphanTemps loop):
  ///   %r  = load __global_G_N
  ///   mm_decref_if_nonnull %r            ← bracket close
  ///   %0  = const_i64 0
  ///   store %0 → __global_G_N
  ///
  /// The global's module-level slot owns the reference from __module_init
  /// through program end; the function-local bracket is pure churn whenever
  /// the function doesn't retain the loaded value.
  ///
  /// Safety: the elimination is sound when:
  ///  1. The global @G is never reassigned inside this function (no
  ///     StdGlobalStoreI64Op @G) — the global's value stays stable across
  ///     all paths so the module-level reference remains valid.
  ///  2. No op in the function (outside the matched open/close) retains a
  ///     value loaded from the temp: no store to the temp (other than the
  ///     zero-out in the close triple), no load of the temp flowing into
  ///     store_indirect / a retaining call arg / a func.return.
  ///
  /// Both open and close ops must be eliminated together — removing only the
  /// incref would leave the scope-end decref_if_nonnull reading an uninitialised
  /// slot; removing only the decref would leak the module reference on error
  /// paths that do not execute the scope-end block.
  /// </summary>
  private static void CancelGlobalLoadOrphanBrackets(
      IrFunction<StandardOp> func,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    var blocks = func.Body.Blocks;
    if (blocks.Count == 0) return;

    var blockByName = new Dictionary<string, IrBlock<StandardOp>>(blocks.Count);
    foreach (var b in blocks) blockByName[b.Name] = b;

    // Step 1: collect opens — one per global-load triple.
    var opensByTemp = new Dictionary<string, GlobalBracketOpen>();
    foreach (var block in blocks) {
      var ops = block.Operations;
      // SSA id → index of the op that produced it in this block.
      var defIdx = new Dictionary<int, int>();
      for (int i = 0; i < ops.Count; i++) {
        var op = ops[i];
        int anyId = op.AnyResultId;
        if (anyId >= 0) defIdx[anyId] = i;

        // Match: StdGlobalLoadI64Op immediately followed (within the block) by
        // a store of its result into a __global_* temp, then a load + mm_incref.
        if (op is not StdGlobalLoadI64Op gload) continue;
        string globalName = gload.GlobalName;
        int loadResultId = gload.Result.Id;

        // Find the store of the global-load result into a __global_ temp.
        int storeIdx = -1;
        string? tempName = null;
        for (int j = i + 1; j < ops.Count; j++) {
          if (ops[j] is IStoreOp st && st.Value.Id == loadResultId
              && st.VarName.StartsWith("__global_")) {
            storeIdx = j;
            tempName = st.VarName;
            break;
          }
          // Another op consumed the global_load result before the store — no match.
          if (IsConsumerOf(ops[j], loadResultId)) break;
        }
        if (storeIdx < 0 || tempName == null) continue;

        // Find the load of tempName followed immediately by mm_incref.
        int feedingLoadIdx = -1, increfIdx = -1;
        for (int j = storeIdx + 1; j < ops.Count; j++) {
          if (ops[j] is ILoadOp ld && ld.VarName == tempName) {
            // Look for mm_incref consuming this load's result.
            for (int k = j + 1; k < ops.Count; k++) {
              if (ops[k] is StdCallRuntimeOp rt && rt.Callee == "mm_incref"
                  && rt.Args.Count >= 1 && rt.Args[0].Id == ld.Result.Id) {
                feedingLoadIdx = j;
                increfIdx = k;
                break;
              }
              // Another consumer of ld.Result before the incref — no match.
              if (IsConsumerOf(ops[k], ld.Result.Id)) break;
            }
            if (increfIdx >= 0) break;
          }
          // A store to tempName before we found the incref — this open triple is
          // non-standard; bail.
          if (ops[j] is IStoreOp stCheck && stCheck.VarName == tempName) break;
        }

        if (feedingLoadIdx < 0) continue;

        if (opensByTemp.Remove(tempName)) {
          // Multiple opens for the same temp in different blocks — too complex; skip both.
          continue;
        }

        opensByTemp[tempName] = new GlobalBracketOpen {
          Block = block,
          GlobalName = globalName,
          TempName = tempName,
          GlobalLoadOpIndex = i,
          StoreOpIndex = storeIdx,
          FeedingLoadOpIndex = feedingLoadIdx,
          IncrefOpIndex = increfIdx,
        };
      }
    }

    if (opensByTemp.Count == 0) return;

    // Step 2: collect closes — one per scope-end cleanup of the orphan temp.
    //
    // The conversion always emits a 4-op close:
    //   load tempName → ptr
    //   mm_decref_if_nonnull ptr
    //   const_i64 0
    //   store 0 → tempName
    //
    // However, the DeadStoreEliminationPass runs before RefcountOptimizationPass
    // and may remove the zero-out (const0 + store-zero) when tempName is not
    // live on exit from the scope-end block. In that case the close appears as
    // just 2 ops (load + decref_if_nonnull). We match both shapes.
    //
    // CRITICAL: after collecting closes, we verify that EVERY decref of tempName
    // across ALL blocks is covered by a matched close. If any decref is outside
    // the matched close set, we bail — otherwise eliminating the incref would leave
    // that stray decref unpaired (refcount underflow).
    var closesByTemp = new Dictionary<string, List<GlobalBracketClose>>();
    foreach (var block in blocks) {
      var ops = block.Operations;
      for (int i = 0; i < ops.Count - 1; i++) {
        if (ops[i] is not ILoadOp ld || !opensByTemp.ContainsKey(ld.VarName)) continue;
        string tempName = ld.VarName;
        int loadedId = ld.Result.Id;

        // Find the mm_decref_if_nonnull consuming loadedId, skipping over any
        // mm-trace tag-construction ops (lea_symdata + ptr_to_i64 pairs) that
        // the emitter inserts in --mm-trace mode between the load and the decref.
        // Reject if any other op intervenes before the decref.
        int decrefIdx = -1;
        for (int k = i + 1; k < ops.Count && k <= i + 5; k++) {
          var candidate = ops[k];
          if (candidate is StdCallRuntimeIfNonnullOp decrefOp
              && decrefOp.Callee == "mm_decref"
              && decrefOp.Args.Count >= 1 && decrefOp.Args[0].Id == loadedId) {
            decrefIdx = k;
            break;
          }
          // Allow mm-trace shaping ops (lea_symdata + ptr_to_i64) to intervene.
          if (candidate is StdLeaSymdataOp or StdPtrToI64Op) continue;
          // Any other op before the decref means this isn't the pattern we target.
          break;
        }
        if (decrefIdx < 0) continue;

        // Try to match the optional zero-out tail (const 0 + store 0 → tempName).
        // The tail must follow the decref without any intervening ops.
        int zeroConstIdx = -1, zeroStoreIdx = -1;
        if (decrefIdx + 2 < ops.Count
            && ops[decrefIdx + 1] is StdConstI64Op zeroOp && zeroOp.Value == 0
            && ops[decrefIdx + 2] is IStoreOp stZero
               && stZero.Value.Id == zeroOp.Result.Id
               && stZero.VarName == tempName) {
          zeroConstIdx = decrefIdx + 1;
          zeroStoreIdx = decrefIdx + 2;
        }

        if (!closesByTemp.TryGetValue(tempName, out var list)) {
          list = [];
          closesByTemp[tempName] = list;
        }
        list.Add(new GlobalBracketClose {
          Block = block,
          TempName = tempName,
          FeedingLoadOpIndex = i,
          DecrefOpIndex = decrefIdx,
          ZeroConstOpIndex = zeroConstIdx,
          ZeroStoreOpIndex = zeroStoreIdx,
        });
      }
    }

    // Step 3: for each global temp that has both an open and at least one close,
    // perform the body-safety check and eliminate if safe.
    var toRemovePerBlock = new Dictionary<string, HashSet<int>>();

    foreach (var (tempName, open) in opensByTemp) {
      if (!closesByTemp.TryGetValue(tempName, out var closes)) continue;
      if (closes.Count == 0) continue;

      // Coverage check: every decref of tempName in every block must be
      // accounted for by a matched close. If any decref falls outside the
      // matched close set (e.g. from a stray scope-end cleanup that survived
      // DSE), eliminating the incref would leave that decref unpaired →
      // refcount underflow. Walk all blocks and compare each decref-of-tempName
      // op against the set of (block, decrefOpIndex) pairs from matched closes.
      var matchedDecrefPositions = new HashSet<(string, int)>(
          closes.Select(c => (c.Block.Name, c.DecrefOpIndex)));
      bool allDecrefsMatched = true;
      foreach (var block in blocks) {
        var bOps = block.Operations;
        var blockLoadedFrom = new Dictionary<int, string>();
        for (int i = 0; i < bOps.Count; i++) {
          if (bOps[i] is ILoadOp loadOp) { blockLoadedFrom[loadOp.Result.Id] = loadOp.VarName; continue; }
          var kind = GetRefcountKind(bOps[i], out var hp);
          if (kind == RefcountKind.Decref && hp != null
              && blockLoadedFrom.TryGetValue(hp.Id, out var decrVar)
              && decrVar == tempName) {
            if (!matchedDecrefPositions.Contains((block.Name, i))) {
              allDecrefsMatched = false;
              break;
            }
          }
        }
        if (!allDecrefsMatched) break;
      }
      if (!allDecrefsMatched) continue;

      // Safety check 1: global @G is never reassigned in this function.
      // If any block stores to the global, the module-level reference may change
      // mid-function and the borrowed value becomes stale.
      bool globalReassigned = false;
      foreach (var block in blocks) {
        foreach (var op in block.Operations) {
          if (op is StdGlobalStoreI64Op gStore && gStore.GlobalName == open.GlobalName) {
            globalReassigned = true;
            break;
          }
        }
        if (globalReassigned) break;
      }
      if (globalReassigned) continue;

      // Collect the set of (block, opIndex) pairs that are in open/close triples
      // so we can skip them in the retention scan.
      var skipPairs = new HashSet<(string, int)> {
        (open.Block.Name, open.GlobalLoadOpIndex),
        (open.Block.Name, open.StoreOpIndex),
        (open.Block.Name, open.FeedingLoadOpIndex),
        (open.Block.Name, open.IncrefOpIndex),
      };
      foreach (var close in closes) {
        skipPairs.Add((close.Block.Name, close.FeedingLoadOpIndex));
        skipPairs.Add((close.Block.Name, close.DecrefOpIndex));
        if (close.ZeroConstOpIndex >= 0) skipPairs.Add((close.Block.Name, close.ZeroConstOpIndex));
        if (close.ZeroStoreOpIndex >= 0) skipPairs.Add((close.Block.Name, close.ZeroStoreOpIndex));
      }

      // Safety check 2: no op outside the open/close set retains a value derived
      // from tempName. Walk all blocks; track which SSA ids are "tainted" (derived
      // from a load of tempName), and reject if any tainted value reaches:
      //   - a store_indirect (field write of heap memory)
      //   - an IStoreOp to a var other than tempName itself  (alias propagation)
      //   - a StdCallOp or StdTryCallOp at a retaining parameter position
      //   - a func.return / func.error_return
      //   - a mm_incref runtime call (manual incref)
      // Also reject if tempName is stored-to by any op outside the matched triples
      // (re-assignment of the temp mid-function would change the close triple's meaning).
      bool retentionDetected = false;
      foreach (var block in blocks) {
        if (retentionDetected) break;
        var ops = block.Operations;
        // Tainted SSA ids within this block (values loaded from tempName or
        // derived from such values through loads or load_indirect).
        var tainted = new HashSet<int>();
        // SSA ids known to be constant zero in this block (used to recognise
        // null-initialisation stores to tempName, which are safe).
        var constZero = new HashSet<int>();

        for (int i = 0; i < ops.Count; i++) {
          if (retentionDetected) break;
          var op = ops[i];
          bool isSkipped = skipPairs.Contains((block.Name, i));

          // Track constant-zero SSA ids so we can distinguish null-init stores.
          if (op is StdConstI64Op cst && cst.Value == 0 && cst.AnyResultId >= 0) {
            constZero.Add(cst.AnyResultId);
            continue;
          }

          if (op is ILoadOp loadOp && loadOp.VarName == tempName) {
            if (!isSkipped) tainted.Add(loadOp.Result.Id);
            continue;
          }

          if (op is StdLoadIndirectOp indirectLoad) {
            // Propagate taint through load_indirect (field reads are fine as long
            // as we don't retain the result — we propagate so that if the result
            // is later incref'd or stored_indirect we catch it).
            if (tainted.Contains(indirectLoad.BasePtr.Id)
                && indirectLoad.Result is StdI64 indRes)
              tainted.Add(indRes.Id);
            continue;
          }

          // A store to tempName outside the open/close set means the temp is
          // reused or overwritten — too complex to reason about; bail.
          // Exception: storing a constant zero is a null-initialisation or
          // null-out that the emitter always generates for orphan-temp slots;
          // these are safe because the temp is still null (not a live pointer)
          // at the point of the store, so no reference is lost.
          if (!isSkipped && op is IStoreOp st && st.VarName == tempName) {
            if (!constZero.Contains(st.Value.Id)) {
              retentionDetected = true;
              break;
            }
            continue; // null-init store — safe, skip
          }

          // A store of a tainted value into any other slot creates an alias we
          // can't track easily — conservative rejection (unless this is a known
          // non-heap store, but we have no such distinction at this level).
          if (!isSkipped && op is IStoreOp stTaint && tainted.Contains(stTaint.Value.Id)
              && stTaint.VarName != tempName) {
            retentionDetected = true;
            break;
          }

          // store_indirect of a tainted value writes the pointer into heap memory.
          if (!isSkipped && op is StdStoreIndirectOp stInd && tainted.Contains(stInd.Value.Id)) {
            retentionDetected = true;
            break;
          }

          // mm_incref of a tainted value.
          if (!isSkipped && op is StdCallRuntimeOp rt && rt.Callee == "mm_incref"
              && rt.Args.Count > 0 && tainted.Contains(rt.Args[0].Id)) {
            retentionDetected = true;
            break;
          }

          // Any return of a tainted value — transfers ownership to the caller.
          if (!isSkipped && op is StdReturnOp ret && ret.ReturnValue != null
              && tainted.Contains(ret.ReturnValue.Id)) {
            retentionDetected = true;
            break;
          }

          // A direct or try-call passing a tainted value at a retaining position.
          if (!isSkipped && op is StdCallOp directCall) {
            if (!IsBorrowOnlyCall(directCall, funcLookup)) {
              foreach (var arg in directCall.Args) {
                if (tainted.Contains(arg.Id)) { retentionDetected = true; break; }
              }
            }
            // Even borrow-only: taint doesn't flow through calls — borrow-only
            // callees cannot retain, and their return is a new independently-owned value.
          } else if (!isSkipped && op is StdTryCallOp tryCall) {
            if (!IsBorrowOnlyCall(tryCall, funcLookup)) {
              foreach (var arg in tryCall.Args) {
                if (tainted.Contains(arg.Id)) { retentionDetected = true; break; }
              }
            }
          }
        }
      }
      if (retentionDetected) continue;

      // Elimination: remove the incref (and its feeding load) from the open
      // block, and all close triples as a unit. The global_load and store ops
      // that initialize the temp stay — the function body still reads through
      // the temp slot for struct field accesses.
      var openSet = GetOrCreateRemoveSet(toRemovePerBlock, open.Block.Name);
      openSet.Add(open.FeedingLoadOpIndex);
      openSet.Add(open.IncrefOpIndex);

      foreach (var close in closes) {
        var closeSet = GetOrCreateRemoveSet(toRemovePerBlock, close.Block.Name);
        closeSet.Add(close.FeedingLoadOpIndex);
        closeSet.Add(close.DecrefOpIndex);
        if (close.ZeroConstOpIndex >= 0) closeSet.Add(close.ZeroConstOpIndex);
        if (close.ZeroStoreOpIndex >= 0) closeSet.Add(close.ZeroStoreOpIndex);
      }
    }

    ApplyRemovals(toRemovePerBlock, blockByName);
  }

  /// <summary>
  /// Returns true if <paramref name="op"/> reads the SSA value with id
  /// <paramref name="ssaId"/> as one of its operands.
  /// </summary>
  private static bool IsConsumerOf(StandardOp op, int ssaId) {
    foreach (var v in op.ReadValues) {
      if (v.Id == ssaId) return true;
    }
    return false;
  }
}
