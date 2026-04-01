using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class DeadFunctionElimination {
  public static void Run(MlirModule<MaxonOp> module) {
    var reachable = new HashSet<string>();
    var funcByName = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcByName[func.Name] = func;

    // BFS from entry function and runtime entry points
    var queue = new Queue<string>();
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == module.EntryFunctionName);
    if (mainFunc != null) {
      queue.Enqueue(mainFunc.Name);
      reachable.Add(mainFunc.Name);
    }
    // __module_init and __maxon_global_cleanup are called from _start, not from Maxon code
    foreach (var func in module.Functions) {
      if (func.Name == "__module_init" || func.Name == "__maxon_global_cleanup") {
        if (reachable.Add(func.Name))
          queue.Enqueue(func.Name);
      }
    }

    // Resolve a callee name to its actual function name (handles namespace-qualified names)
    void EnqueueCallee(string callee) {
      if (!reachable.Add(callee)) return;
      queue.Enqueue(callee);
      // Also resolve suffix-matched names so the actual function is marked reachable
      if (!funcByName.ContainsKey(callee)) {
        var suffix = $".{callee}";
        foreach (var candidate in funcByName.Keys) {
          if (candidate.EndsWith(suffix) && reachable.Add(candidate))
            queue.Enqueue(candidate);
        }
      }
    }

    while (queue.Count > 0) {
      var name = queue.Dequeue();
      if (!funcByName.TryGetValue(name, out var func)) continue;

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonCallOp call)
            EnqueueCallee(call.Callee);
          if (op is MaxonAsyncCallOp asyncCall)
            EnqueueCallee(asyncCall.Callee);
          if (op is MaxonFunctionRefOp fnRef)
            EnqueueCallee(fnRef.FunctionName);
          if (op is MaxonClosureCreateOp closureCreate)
            EnqueueCallee(closureCreate.FunctionName);
          if (op is MaxonGlobalLoadOp { LazyInitFuncName: string lazyInit })
            EnqueueCallee(lazyInit);
          // Array.get for struct elements needs the element type's clone() method
          if (op is MaxonManagedMemGetOp { IsStructElement: true, StructElementTypeName: string elemTypeName }) {
            EnqueueCallee($"{elemTypeName}.clone");
            // Also enqueue the concrete alias clone when the element type is a
            // generic alias (e.g. Entry → ____Tuple_Key_Value_String_i64)
            var resolved = module.ResolveConcreteAlias(elemTypeName);
            if (resolved != elemTypeName)
              EnqueueCallee($"{resolved}.clone");
          }
        }
      }
    }

    module.Functions.RemoveAll(f => !reachable.Contains(f.Name));

    // Collect globals that are loaded (read) by reachable functions
    var loadedGlobals = new HashSet<string>();
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonGlobalLoadOp load) {
            loadedGlobals.Add(load.GlobalName);
            if (load.LazyGuardName != null)
              loadedGlobals.Add(load.LazyGuardName);
          }
        }
      }
    }

    // Find globals that are never loaded (only stored to in __module_init)
    var deadGlobals = new HashSet<string>();
    foreach (var g in module.Globals) {
      if (!loadedGlobals.Contains(g.Name))
        deadGlobals.Add(g.Name);
    }

    if (deadGlobals.Count > 0) {
      module.Globals.RemoveAll(g => deadGlobals.Contains(g.Name));
      foreach (var name in deadGlobals)
        module.GlobalVarInfos.Remove(name);

      // Remove dead init code from __module_init and cleanup functions
      foreach (var func in module.Functions) {
        if (func.Name != "__module_init" && func.Name != "__maxon_global_cleanup")
          continue;
        foreach (var block in func.Body.Blocks)
          EliminateDeadOps(block, deadGlobals);
      }
    }

    // Remove __module_init / __maxon_global_cleanup functions that are now empty
    // (only contain scope_end + return with no real work)
    module.Functions.RemoveAll(f => {
      if (f.Name != "__module_init" && f.Name != "__maxon_global_cleanup")
        return false;
      var ops = f.Body.Blocks[0].Operations;
      return ops.All(op => op is MaxonScopeEndOp or MaxonReturnOp);
    });
  }

  /// Remove dead stores and their producing ops from a block.
  /// First removes global_store ops for dead globals, then iteratively
  /// removes ops whose results are unused by any remaining op.
  private static void EliminateDeadOps(MlirBlock<MaxonOp> block, HashSet<string> deadGlobals) {
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
      }

      // Remove ops that define a value nobody uses
      int before = block.Operations.Count;
      block.Operations.RemoveAll(op => {
        // Never remove side-effecting ops
        if (op is MaxonScopeEndOp or MaxonReturnOp or MaxonGlobalStoreOp or MaxonCallOp)
          return false;
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
