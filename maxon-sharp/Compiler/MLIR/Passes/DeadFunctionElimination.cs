using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

public static class DeadFunctionElimination {
  public static void Run(IrModule<MaxonOp> module) {
    // Fixed-point iteration. Each round:
    //   1. Compute the set of globals that any non-init function reads or
    //      writes (bidirectional liveness — stores pin the var alive).
    //   2. Identify globals NOT in that set as dead. Remove their store ops
    //      and producer chains from every init body (and drop the dead
    //      globals from `module.Globals` / `module.GlobalVarInfos`).
    //   3. Walk reachability from `main` + every still-non-empty init.
    //      Drop unreachable functions.
    //   4. If any init body became trivially empty, drop it too.
    // Iterating is the key: when an init has BOTH live and dead writes
    // (e.g. user `let xs = [...]` + stdlib's dead `var captured = ...`
    // share the same `__module_init`), dropping the dead store's producer
    // chain first prevents the reachability walk from pinning callees
    // like `TraceKeyArray.create` that exist solely to support the dead
    // initializer. The fixpoint converges because each round can only
    // shrink the live set.
    while (true) {
      bool changed = false;

      // Step 1+2: prune dead stores + producer chains from every init.
      var referencedGlobals = CollectGlobalsReferencedByLive(module);
      var deadGlobals = new HashSet<string>();
      foreach (var g in module.Globals) {
        if (!referencedGlobals.Contains(g.Name))
          deadGlobals.Add(g.Name);
      }
      if (deadGlobals.Count > 0) {
        module.Globals.RemoveAll(g => deadGlobals.Contains(g.Name));
        foreach (var name in deadGlobals)
          module.GlobalVarInfos.Remove(name);
        foreach (var func in module.Functions) {
          if (func.Name != "__module_init" && func.Name != "__maxon_global_cleanup")
            continue;
          foreach (var block in func.Body.Blocks)
            EliminateDeadOps(block, deadGlobals);
        }
        changed = true;
      }

      // Step 3: walk reachability. Inits with no remaining store ops are
      // also skipped as seeds (their bodies are gut-empty after the
      // EliminateDeadOps cascade and would only pin orphan callees).
      var emptyInits = new HashSet<string>();
      foreach (var func in module.Functions) {
        if (func.Name != "__module_init" && func.Name != "__maxon_global_cleanup")
          continue;
        if (InitBodyHasNoLiveWork(func))
          emptyInits.Add(func.Name);
      }
      int beforeCount = module.Functions.Count;
      WalkReachableAndPrune(module, emptyInits);
      if (module.Functions.Count != beforeCount)
        changed = true;

      // Step 4: drop now-empty init functions outright.
      int beforeFuncs = module.Functions.Count;
      module.RemoveFunctionsWhere(f => {
        if (f.Name != "__module_init" && f.Name != "__maxon_global_cleanup")
          return false;
        var ops = f.Body.Blocks[0].Operations;
        return ops.All(op => op is MaxonScopeEndOp or MaxonReturnOp);
      });
      if (module.Functions.Count != beforeFuncs)
        changed = true;

      if (!changed) break;
    }

    // EliminateDeadOps may have removed call-producing ops from __module_init /
    // cleanup bodies even if no functions were removed in the second sweep.
    module.InvalidateCallGraph();
  }

  /// True when `func` contains no `MaxonGlobalStoreOp` and no `MaxonCallOp`
  /// — i.e. nothing the reachability walk would chase, only literals and
  /// scope-end housekeeping. Such inits are stand-ins that should be
  /// dropped as reachability roots so we don't pull orphan callees in.
  private static bool InitBodyHasNoLiveWork(IrFunction<MaxonOp> func) {
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonGlobalStoreOp) return false;
        if (op is MaxonCallOp) return false;
      }
    }
    return true;
  }

  /// One round of function-level reachability: seed from `main` and every
  /// init/cleanup not already in `deadInits`, BFS through the call graph,
  /// drop unreachable functions from `module.Functions`.
  private static void WalkReachableAndPrune(IrModule<MaxonOp> module, HashSet<string> deadInits) {
    var reachable = new HashSet<string>();
    var funcByName = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcByName[func.Name] = func;

    var queue = new Queue<string>();
    var mainFunc = module.FindFunctionByExactName(module.EntryFunctionName);
    if (mainFunc != null) {
      queue.Enqueue(mainFunc.Name);
      reachable.Add(mainFunc.Name);
    }
    foreach (var func in module.Functions) {
      if (func.Name == "__module_init" || func.Name == "__maxon_global_cleanup") {
        if (deadInits.Contains(func.Name)) continue;
        if (reachable.Add(func.Name))
          queue.Enqueue(func.Name);
      }
    }

    // Resolve a callee name to its actual function name (handles namespace-qualified names).
    void EnqueueCallee(string callee) {
      if (!reachable.Add(callee)) return;
      queue.Enqueue(callee);
      if (!funcByName.ContainsKey(callee)) {
        if (callee.IndexOf('.') < 0) {
          foreach (var candidate in module.FindFunctionsByShortName(callee)) {
            if (reachable.Add(candidate.Name))
              queue.Enqueue(candidate.Name);
          }
        } else {
          var suffix = $".{callee}";
          foreach (var candidate in funcByName.Keys) {
            if (candidate.EndsWith(suffix) && reachable.Add(candidate))
              queue.Enqueue(candidate);
          }
        }
      }
    }

    var graph = module.CallGraph;
    while (queue.Count > 0) {
      var name = queue.Dequeue();
      if (!funcByName.TryGetValue(name, out var func)) continue;
      foreach (var refName in graph.GetReferencedNames(func))
        EnqueueCallee(refName);
    }

    module.RemoveFunctionsWhere(f => !reachable.Contains(f.Name));
    module.InvalidateCallGraph();
  }

  /// Collect the names of all globals read or written by any non-init
  /// function in the module. Bidirectional liveness — stores count.
  /// Init bodies' own stores are excluded so they don't pin themselves
  /// alive via the var they're trying to populate.
  private static HashSet<string> CollectGlobalsReferencedByLive(IrModule<MaxonOp> module) {
    var refs = new HashSet<string>();
    foreach (var func in module.Functions) {
      bool isInit = func.Name == "__module_init" || func.Name == "__maxon_global_cleanup";
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonGlobalLoadOp load) {
            refs.Add(load.GlobalName);
            if (load.LazyGuardName != null)
              refs.Add(load.LazyGuardName);
          }
          if (!isInit && op is MaxonGlobalStoreOp store) {
            refs.Add(store.GlobalName);
          }
        }
      }
    }
    return refs;
  }


  /// Pure-allocator allowlist for call ops whose only effect is heap
  /// allocation routed into a dropped slot. Hits the shapes the parser
  /// emits for `var x = T.create()` / `let xs = [...]` initializers,
  /// including the synthesized `__ManagedMemory.create` the array-literal
  /// lowering uses internally.
  private static bool IsPureAllocatorCallee(string callee) {
    return callee.EndsWith(".create") || callee.EndsWith(".from");
  }

  /// Remove dead stores and their producing ops from a block.
  /// First removes global_store ops for dead globals, then iteratively
  /// removes ops whose results are unused by any remaining op.
  private static void EliminateDeadOps(IrBlock<MaxonOp> block, HashSet<string> deadGlobals) {
    // Remove dead global stores
    block.Operations.RemoveAll(op =>
      op is MaxonGlobalStoreOp store && deadGlobals.Contains(store.GlobalName));

    // Iteratively remove ops that produce unused values
    bool changed = true;
    while (changed) {
      changed = false;

      // Collect array element tags from surviving struct literals
      var liveArrayTags = new HashSet<string>();
      foreach (var op in block.Operations) {
        if (op is MaxonStructLiteralOp sl && sl.ArrayLiteralTag != null)
          liveArrayTags.Add(sl.ArrayLiteralTag);
      }

      // Keep literals alive if they feed into surviving array element assigns
      var liveAssignValueIds = new HashSet<int>();
      foreach (var op in block.Operations) {
        if (op is MaxonAssignOp { VarName: var name } assignOp && name.StartsWith("__arr_")) {
          var dotIdx = name.LastIndexOf('.');
          if (dotIdx > 0 && liveArrayTags.Contains(name[..dotIdx]))
            liveAssignValueIds.Add(assignOp.Value.Id);
        }
      }

      // Collect var names actually read by `MaxonVarRefOp` ops. An assign
      // whose var is never referenced (other than for scope-end cleanup) is
      // dead and can be dropped — its rhs result becomes unused, which lets
      // the call/literal it produced get DCE'd on the next iteration. This
      // catches the dead-top-level-var pattern: `__call_tmp_N` synthetic
      // temps created by `let/var x = T.create()` initializers whose
      // backing globalStore was eliminated as dead.
      var referencedVarNames = new HashSet<string>();
      foreach (var op in block.Operations) {
        if (op is MaxonVarRefOp vr)
          referencedVarNames.Add(vr.VarName);
      }

      // Collect value IDs used by surviving ops
      var usedIds = new HashSet<int>(liveAssignValueIds);
      foreach (var op in block.Operations) {
        if (op is MaxonGlobalStoreOp gs)
          usedIds.Add(gs.Value.Id);
        if (op is MaxonStructLiteralOp sl) {
          foreach (var (_, val) in sl.FieldValues)
            usedIds.Add(val.Id);
        }
        if (op is MaxonReturnOp ret && ret.Value != null)
          usedIds.Add(ret.Value.Id);
        if (op is MaxonCallOp call) {
          foreach (var arg in call.Args)
            usedIds.Add(arg.Id);
        }
        // Pin the assign's rhs only when the var is actually referenced by
        // a downstream read. Otherwise the assign is dead and its rhs is
        // available for removal — critical for the dead-init cleanup
        // cascade. Array-element assigns are gated separately above.
        if (op is MaxonAssignOp assign && !assign.VarName.StartsWith("__arr_")) {
          if (referencedVarNames.Contains(assign.VarName))
            usedIds.Add(assign.Value.Id);
        }
      }

      // Remove ops that define a value nobody uses
      int before = block.Operations.Count;
      block.Operations.RemoveAll(op => {
        // Never remove side-effecting ops
        if (op is MaxonScopeEndOp or MaxonReturnOp or MaxonGlobalStoreOp)
          return false;
        // Calls are normally side-effecting, but stdlib factory shapes
        // (`Type.create`, `Type.from`, `__ManagedMemory.create`) are pure
        // allocators — their only effect is heap allocation routed into a
        // slot we just dropped. Allow removing those when the result is
        // unused so the cleanup cascade dissolves an entire init body.
        if (op is MaxonCallOp call) {
          if (call.Result != null && !usedIds.Contains(call.Result.Id) && IsPureAllocatorCallee(call.Callee))
            return true;
          return false;
        }
        // Drop assigns whose var has no downstream `var_ref` reader. The
        // remaining scope-end mention of the var is harmless once we clean
        // it up at the bottom of `EliminateDeadOps`.
        if (op is MaxonAssignOp deadAssign && !deadAssign.VarName.StartsWith("__arr_") &&
            !referencedVarNames.Contains(deadAssign.VarName)) {
          return true;
        }
        // Remove value-producing ops whose result is unused
        if (op is MaxonLiteralOp lit && !usedIds.Contains(lit.Result.Id))
          return true;
        if (op is MaxonStructLiteralOp sl && !usedIds.Contains(sl.Result.Id))
          return true;
        // Remove array element assigns whose tag has no surviving struct literal
        if (op is MaxonAssignOp { VarName: var name } && name.StartsWith("__arr_")) {
          var dotIdx = name.LastIndexOf('.');
          if (dotIdx > 0 && !liveArrayTags.Contains(name[..dotIdx]))
            return true;
        }
        return false;
      });
      if (block.Operations.Count < before)
        changed = true;
    }

    // Clean up ScopeEndOp: remove dead variable names that no longer have backing assigns
    var liveVarNames = new HashSet<string>();
    foreach (var op in block.Operations) {
      if (op is MaxonAssignOp a)
        liveVarNames.Add(a.VarName);
    }
    for (int i = 0; i < block.Operations.Count; i++) {
      if (block.Operations[i] is MaxonScopeEndOp scopeEnd) {
        var filtered = scopeEnd.VarsToClean.Where(v => liveVarNames.Contains(v)).ToList();
        if (filtered.Count < scopeEnd.VarsToClean.Count)
          block.Operations[i] = new MaxonScopeEndOp(filtered, scopeEnd.KeepVars) { VarMetadata = scopeEnd.VarMetadata };
      }
    }
  }
}
