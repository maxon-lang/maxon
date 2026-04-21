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
    foreach (var func in module.Functions) {
      var useCounts = ComputeUseCounts(func);
      foreach (var block in func.Body.Blocks) {
        CancelRedundantRefcounts(block, useCounts);
      }
      CancelCrossBlockRedundantRefcounts(func, useCounts);
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
    var aliasingEvents = new List<int>();
    // Like aliasingEvents but excludes direct (non-try) function calls.
    // Used for aliasFromStore candidates: in Maxon's calling convention,
    // callees borrow parameters and cannot release the srcVar object, so a
    // StdCallOp in the window is safe to ignore for those candidates.
    var aliasingEventsNoDirectCalls = new List<int>();
    // Stores grouped by destination variable (sorted ascending).
    var storeIdxByVar = new Dictionary<string, List<int>>();
    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];
      if (IsAliasingOp(op)) aliasingEvents.Add(i);
      if (IsAliasingOp(op, includeDirectCalls: false)) aliasingEventsNoDirectCalls.Add(i);
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
      // For aliasFromStore candidates, direct (non-try) function calls are
      // allowed in the window: Maxon's calling convention requires callees to
      // borrow parameters, so a call cannot release the srcVar object.
      var windowEvents = increfAliasFromStore.Contains(incIdx)
          ? aliasingEventsNoDirectCalls
          : aliasingEvents;
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

  // ─── Cross-block sub-pass ────────────────────────────────────────────────

  /// <summary>
  /// Incref candidate found during alias analysis of one block that may have
  /// its matching decref in a different, dominated block.
  /// </summary>
  private sealed class CrossBlockIncref {
    public required IrBlock<StandardOp> Block { get; init; }
    public required int OpIndex { get; init; }
    public required string VarName { get; init; }
    public required string SrcVar { get; init; }
    /// True when the alias was established via firstStoreOf (same SSA value
    /// written to two slots) rather than a load-based alias.
    public required bool IsFromStore { get; init; }
  }

  /// <summary>
  /// Decref found during analysis of one block that is a cross-block
  /// elimination candidate for some incref in a dominating block.
  /// </summary>
  private sealed class CrossBlockDecref {
    public required IrBlock<StandardOp> Block { get; init; }
    public required int OpIndex { get; init; }
    public required string VarName { get; init; }
  }

  /// <summary>
  /// Per-block summary of ops that would kill srcVar liveness: any aliasing
  /// event, plus any store or decref of a specific variable.
  /// </summary>
  private sealed class BlockKillInfo {
    /// Variables that are stored-to or decreffed anywhere in this block.
    public required HashSet<string> KilledVars { get; init; }
    /// Indices of aliasing events in this block (ascending), for prefix/suffix
    /// range checks on the entry and exit blocks of a candidate window.
    public required List<int> AliasingEventIndices { get; init; }
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
      Dictionary<int, int> useCounts) {
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
    foreach (var b in blocks) killInfo[b.Name] = BuildBlockKillInfo(b);

    // Parameters are owned by the caller; the callee need not have a local
    // decref of the parameter slot, so the aliasFromStore safety check is
    // skipped when srcVar is one of these slots.
    var paramVarNames = CollectParameterSlotNames(blocks[0]);

    // Collect cross-block incref candidates and all decrefs per variable.
    var increfCandidates = new List<CrossBlockIncref>();
    var decrefsByVar = new Dictionary<string, List<CrossBlockDecref>>();

    foreach (var block in blocks) {
      CollectCrossBlockCandidates(block, increfCandidates, decrefsByVar);
    }

    // For each incref candidate, find dominated decrefs and check safety.
    var toRemovePerBlock = new Dictionary<string, HashSet<int>>();

    foreach (var inc in increfCandidates) {
      if (!decrefsByVar.TryGetValue(inc.VarName, out var decrefList)) continue;

      // srcVar must own a reference. Ownership is established either by being
      // a function parameter (caller owns it) or by having an explicit decref
      // somewhere in this function (meaning something increffed it earlier).
      // If srcVar has no decref at all, it never took ownership — the
      // incref/decref pair on varName IS the sole owner, and eliminating it
      // would leave the object permanently at rc=0 (a leak).
      if (!decrefsByVar.ContainsKey(inc.SrcVar) && !paramVarNames.Contains(inc.SrcVar)) continue;

      // Safety: count distinct blocks (reachable from the incref block, not inc's own block)
      // that contain a decref for this variable.
      //
      // If there is MORE than one such block, the variable is decreffed on multiple
      // independent execution paths (e.g. a normal return path AND an exception/otherwise
      // path). In that case we cannot eliminate just one incref and one decref — doing so
      // would leave a decref on the other path with no matching incref, causing an underflow.
      //
      // When there is exactly ONE decref block reachable from the incref, correct compiler
      // output guarantees that block is on every exit path from the incref (i.e., it
      // post-dominates the incref), so eliminating the pair is safe.
      var reachableFromInc = BfsReachable(inc.Block.Name, cfg.Successors);
      var reachableDecrefBlocks = decrefList
          .Where(dec => dec.Block.Name != inc.Block.Name && reachableFromInc.Contains(dec.Block.Name))
          .Select(dec => dec.Block.Name)
          .Distinct()
          .Count();

      if (reachableDecrefBlocks != 1) continue;

      // Find the one dominated decref to pair with this incref.
      CrossBlockDecref? matchedDec = null;
      foreach (var dec in decrefList) {
        if (dec.Block.Name == inc.Block.Name) continue; // intra-block already handled
        if (!domTree.StrictlyDominates(inc.Block.Name, dec.Block.Name)) continue;

        if (!IsCrossBlockWindowSafe(inc, dec, cfg, killInfo)) continue;

        // aliasFromStore safety: srcVar must have a decref reachable at or after
        // dec.Block / dec.OpIndex, so the shared allocation is freed.
        // Exception: if srcVar is a function parameter, the caller owns the
        // object and handles cleanup — no local decref is needed in the callee.
        if (inc.IsFromStore && !paramVarNames.Contains(inc.SrcVar)
            && !HasReachableDecrefAfter(inc.SrcVar, dec, cfg, killInfo)) continue;

        matchedDec = dec;
        break;
      }

      if (matchedDec == null) continue;

      Logger.Debug(LogCategory.Ir,
          $"  RefcountOpt(cross-block): cancel incref@{inc.OpIndex} in {inc.Block.Name} / " +
          $"decref@{matchedDec.OpIndex} in {matchedDec.Block.Name} for var '{inc.VarName}' (source '{inc.SrcVar}')");

      var incSet = GetOrCreateRemoveSet(toRemovePerBlock, inc.Block.Name);
      incSet.Add(inc.OpIndex);
      var decSet = GetOrCreateRemoveSet(toRemovePerBlock, matchedDec.Block.Name);
      decSet.Add(matchedDec.OpIndex);

      TryRemoveFeedingLoad(inc.Block.Operations, inc.OpIndex, useCounts, incSet);
      TryRemoveFeedingLoad(matchedDec.Block.Operations, matchedDec.OpIndex, useCounts, decSet);
    }

    // Apply removals per block.
    int totalEliminated = 0;
    foreach (var (blockName, indices) in toRemovePerBlock) {
      if (indices.Count == 0) continue;
      var ops = blockByName[blockName].Operations;
      foreach (var idx in indices.OrderByDescending(i => i)) ops.RemoveAt(idx);
      totalEliminated += indices.Count;
    }

    if (totalEliminated > 0) {
      Logger.Debug(LogCategory.Ir, $"RefcountOpt(cross-block): eliminated {totalEliminated} op(s) in {func.Name}");
    }
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
  private static bool IsCrossBlockWindowSafe(
      CrossBlockIncref inc,
      CrossBlockDecref dec,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo) {
    var incKill = killInfo[inc.Block.Name];
    var decKill = killInfo[dec.Block.Name];

    // Suffix of incref block (from incIdx+1 to end).
    if (HasKillInRange(incKill, inc.SrcVar, inc.OpIndex + 1, int.MaxValue)) return false;

    // Prefix of decref block (from 0 to decIdx-1).
    if (HasKillInRange(decKill, inc.SrcVar, 0, dec.OpIndex - 1)) return false;

    // Intermediate blocks: all blocks on any path from inc.Block to dec.Block
    // that are not inc.Block or dec.Block themselves.
    var intermediates = ComputeIntermediates(inc.Block.Name, dec.Block.Name, cfg);

    foreach (var midName in intermediates) {
      // Loop guard: if any intermediate has inc.Block as a successor, there
      // is a back-edge that could re-execute the incref — skip conservatively.
      if (cfg.Successors.TryGetValue(midName, out var midSuccs) && midSuccs.Contains(inc.Block.Name))
        return false;

      var midKill = killInfo[midName];
      // Function calls alone do not endanger srcVar: in Maxon's calling
      // convention, callees borrow their parameters and the caller handles
      // scope-end decrefs. Only a direct store or decref of srcVar in an
      // intermediate block can break the alias or release the object early.
      if (midKill.KilledVars.Contains(inc.SrcVar)) return false;
    }

    // Check that srcVar is not decreffed on any path reachable from the incref
    // block that bypasses the matched decref block.
    //
    // If there is such a path (e.g. a sibling branch that decrefs srcVar and
    // then uses varName as a return value), eliminating the incref leaves
    // varName without a reference on that path — the srcVar decref would drop
    // the refcount to zero while varName still needs it.
    //
    // "Bypasses dec.Block" = reachable from inc.Block without going through
    // dec.Block. We compute this by BFS with dec.Block treated as a barrier.
    if (HasSrcVarDecrefBypassingDecBlock(inc, dec, cfg, killInfo)) return false;

    return true;
  }

  /// <summary>
  /// Returns true if srcVar has any decref in a block that is reachable from
  /// the incref block WITHOUT passing through the matched decref block.
  ///
  /// Rationale: if such a path exists (e.g. a sibling branch that decrefs
  /// srcVar before using varName as a return value), eliminating the incref
  /// leaves varName without a reference on that path.
  /// </summary>
  private static bool HasSrcVarDecrefBypassingDecBlock(
      CrossBlockIncref inc,
      CrossBlockDecref dec,
      CfgData cfg,
      Dictionary<string, BlockKillInfo> killInfo) {
    return HasDecrefInSuccessors(inc.Block.Name, inc.SrcVar, cfg, killInfo,
        barriers: [inc.Block.Name, dec.Block.Name]);
  }

  /// <summary>
  /// Returns true if <paramref name="srcVar"/> has any decref reachable at or
  /// after (dec.Block, dec.OpIndex) via BFS through the CFG. Used for the
  /// aliasFromStore safety check.
  /// </summary>
  private static bool HasReachableDecrefAfter(
      string srcVar,
      CrossBlockDecref dec,
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
  /// We compute it as: ForwardReachable(fromBlock) ∩ BackwardReachable(toBlock)
  /// minus the two endpoints.
  /// </summary>
  private static HashSet<string> ComputeIntermediates(
      string fromBlock, string toBlock, CfgData cfg) {
    var forward = BfsReachable(fromBlock, cfg.Successors);
    forward.Remove(fromBlock);

    var backward = BfsReachable(toBlock, cfg.Predecessors);
    backward.Remove(toBlock);

    forward.IntersectWith(backward);
    return forward; // now holds only intermediate blocks
  }

  private static HashSet<string> BfsReachable(
      string start, Dictionary<string, List<string>> edges) {
    var visited = new HashSet<string> { start };
    var queue = new Queue<string>();
    queue.Enqueue(start);
    while (queue.Count > 0) {
      var cur = queue.Dequeue();
      if (edges.TryGetValue(cur, out var nexts)) {
        foreach (var n in nexts) { if (visited.Add(n)) queue.Enqueue(n); }
      }
    }
    return visited;
  }

  /// <summary>
  /// Uses the shared AnalyzeAliases scan to emit CrossBlockIncref /
  /// CrossBlockDecref entries for one block.
  ///
  /// Only increfs whose matching decref is in a *different* block contribute to
  /// cross-block elimination; decrefs are collected globally (callers filter by
  /// block).
  /// </summary>
  private static void CollectCrossBlockCandidates(
      IrBlock<StandardOp> block,
      List<CrossBlockIncref> increfCandidates,
      Dictionary<string, List<CrossBlockDecref>> decrefsByVar) {
    AnalyzeAliases(block.Operations, (i, varName, srcVar, isFromStore) => {
      if (srcVar == null) return;
      increfCandidates.Add(new CrossBlockIncref {
        Block = block,
        OpIndex = i,
        VarName = varName,
        SrcVar = srcVar,
        IsFromStore = isFromStore,
      });
    }, (i, varName) => {
      if (!decrefsByVar.TryGetValue(varName, out var list)) {
        list = [];
        decrefsByVar[varName] = list;
      }
      list.Add(new CrossBlockDecref { Block = block, OpIndex = i, VarName = varName });
    });
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
  /// </summary>
  private static void AnalyzeAliases(
      List<StandardOp> ops,
      Action<int, string, string?, bool> onIncref,
      Action<int, string> onDecref) {
    // SSA value ID → variable name it was loaded from.
    var loadedFrom = new Dictionary<int, string>();
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

    for (int i = 0; i < ops.Count; i++) {
      var op = ops[i];

      if (op is ILoadOp load) {
        loadedFrom[load.Result.Id] = load.VarName;
        continue;
      }

      if (op is IStoreOp store) {
        if (loadedFrom.TryGetValue(store.Value.Id, out var srcVar) && srcVar != store.VarName) {
          aliasSource[store.VarName] = srcVar;
          aliasFromStore.Remove(store.VarName);
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
  }

  /// <summary>
  /// Builds the kill-info summary for a single block: aliasing events,
  /// store indices per variable, and decref indices per variable.
  /// </summary>
  private static BlockKillInfo BuildBlockKillInfo(IrBlock<StandardOp> block) {
    var ops = block.Operations;
    var killedVars = new HashSet<string>();
    var aliasingIdxs = new List<int>();
    var storeIdxs = new Dictionary<string, List<int>>();
    var decrefIdxs = new Dictionary<string, List<int>>();

    // loadedFrom is needed to map heap-pointer SSA IDs to variable names for
    // decref index tracking. We do a local one-pass scan here.
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

      if (IsAliasingOp(op)) {
        aliasingIdxs.Add(i);
      }

      var kind = GetRefcountKind(op, out var heapPtr);
      if (kind == RefcountKind.Decref && heapPtr != null
          && loadedFrom.TryGetValue(heapPtr.Id, out var varName)) {
        killedVars.Add(varName);
        AppendIndex(decrefIdxs, varName, i);
      }
    }

    return new BlockKillInfo {
      KilledVars = killedVars,
      AliasingEventIndices = aliasingIdxs,
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
  /// Returns true if any op in the inclusive index range [<paramref name="fromIdx"/>,
  /// <paramref name="toIdx"/>] kills <paramref name="srcVar"/> liveness:
  /// aliasing event, store to srcVar, or decref of srcVar.
  /// </summary>
  private static bool HasKillInRange(BlockKillInfo kill, string srcVar, int fromIdx, int toIdx) {
    if (HasIndexInRange(kill.AliasingEventIndices, fromIdx, toIdx)) return true;
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
  /// Returns true if an operation could release arbitrary heap objects via
  /// side effects.
  ///
  /// When <paramref name="includeDirectCalls"/> is false, direct
  /// (non-try) function calls (<see cref="StdCallOp"/>) are treated as
  /// non-aliasing. This is valid for aliasFromStore candidates: Maxon's
  /// borrowing convention guarantees a callee cannot release the srcVar object
  /// without holding its own independent reference. Try-calls and runtime
  /// calls are still aliasing because error paths can run scope cleanup that
  /// decrefs srcVar in a side branch.
  /// </summary>
  private static bool IsAliasingOp(StandardOp op, bool includeDirectCalls = true) {
    if (includeDirectCalls && op is StdCallOp) return true;
    if (op is StdTryCallOp or StdTryCallRuntimeOp) return true;
    if (op is StdStoreIndirectOp or StdMemCopyOp or StdMemCopyReverseOp) return true;
    // mm_decref can trigger destructors with arbitrary side effects.
    if (op is StdCallRuntimeOp rt && rt.Callee != "mm_incref" && rt.Callee != "mm_trace_transfer") return true;
    if (op is StdCallRuntimeIfNonnullOp grt && grt.Callee != "mm_incref") return true;
    return false;
  }
}
