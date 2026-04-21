using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Analyzes each function to determine which parameters are mutated and how.
/// Sets three properties on each IrFunction:
///   ReassignedParams:     params directly reassigned (controls pass-by-reference ABI)
///   MutatedParams:        params whose reachable data is mutated (superset of ReassignedParams, for E3063)
///   MutatedParamIndices:  same as MutatedParams but as parameter indices (for borrow checking)
/// Includes transitive propagation through call chains.
/// </summary>
public static class ParameterMutationAnalysisPass {
  public static void Run(IrModule<MaxonOp> module) {
    var funcLookup = module.Functions.ToDictionary(f => f.Name);

    // First pass: local analysis per function
    foreach (var f in module.Functions) {
      var paramNames = new HashSet<string>(f.ParamNames);
      if (paramNames.Count == 0) continue;
      bool hasSelf = f.ParamNames[0] == "self";
      HashSet<string>? reassigned = null;
      HashSet<string>? mutated = null;

      // For methods: discover all variable names and SSA values derived from self
      var selfDerivedVars = new HashSet<string>();   // variable names derived from self
      var selfDerivedIds = new HashSet<int>();        // SSA value IDs derived from self

      // Self struct field names — kept separate from selfDerivedVars so that
      // seeding these field names doesn't retroactively taint locals bound
      // via `let x = self.y` (those locals are value copies; reassigning
      // them does not mutate self). The assign check below treats a bare
      // name matching a self field as a direct field store.
      HashSet<string>? selfFieldNames = null;
      if (hasSelf && f.ParamTypes.Count > 0 && f.ParamTypes[0] is IrStructType selfStructType) {
        var resolved = selfStructType.Fields.Count > 0 ? selfStructType
          : (module.TypeDefs.TryGetValue(selfStructType.Name, out var td) && td is IrStructType tds ? tds : selfStructType);
        if (resolved.Fields.Count > 0) {
          selfFieldNames = [];
          foreach (var field in resolved.Fields) {
            selfFieldNames.Add(field.Name);
          }
        }
      }

      if (hasSelf) {
        // Scan to build self-derived variable/SSA sets
        foreach (var b in f.Body.Blocks) {
          foreach (var op in b.Operations) {
            // self parameter itself
            if (op is MaxonStructParamOp sp && sp.Index == 0) {
              selfDerivedIds.Add(sp.Result.Id);
              selfDerivedVars.Add("self");
            }
            // struct_var_ref loading self or a self-derived variable
            if (op is MaxonStructVarRefOp svr && selfDerivedVars.Contains(svr.VarName))
              selfDerivedIds.Add(svr.Result.Id);
            // field access on a self-derived SSA value → result is also self-derived
            if (op is MaxonFieldAccessOp fa && fa.Result != null && selfDerivedIds.Contains(fa.StructValue.Id)) {
              selfDerivedIds.Add(fa.Result.Id);
            }
            // declaration assigning from a self-derived SSA → variable is self-derived
            if (op is MaxonAssignOp assign && assign.IsDeclaration && selfDerivedIds.Contains(assign.Value.Id)) {
              selfDerivedVars.Add(assign.VarName);
            }
          }
        }
      }

      foreach (var b in f.Body.Blocks) {
        foreach (var op in b.Operations) {
          // Track additional SSA refs for self-derived variables (needed for builtin op matching
          // since struct_var_ref can appear after the initial scan)
          if (hasSelf && op is MaxonStructVarRefOp svr2 && selfDerivedVars.Contains(svr2.VarName))
            selfDerivedIds.Add(svr2.Result.Id);

          // Direct assignment to parameter → needs pass-by-ref ABI AND counts as mutation
          if (op is MaxonAssignOp assign && !assign.IsDeclaration) {
            if (paramNames.Contains(assign.VarName)) {
              reassigned ??= [];
              reassigned.Add(assign.VarName);
              mutated ??= [];
              mutated.Add(assign.VarName);
            } else if (hasSelf && (selfDerivedVars.Contains(assign.VarName) || (selfFieldNames?.Contains(assign.VarName) ?? false))) {
              // Assignment to a self-derived alias or a bare self-field name
              // → mutates self. The selfFieldNames set catches the bare-field
              // case (e.g. `pos = x` inside a method, where the parser resolves
              // `pos` to self.pos but doesn't emit it as a declaration in the
              // Maxon-dialect IR).
              reassigned ??= [];
              reassigned.Add("self");
              mutated ??= [];
              mutated.Add("self");
            }
          }

          // Mutating builtin ops on self-derived SSA values → mutates self (no ABI change needed).
          // Throwing __ManagedMemory builtins (set, grow, setLength, remove, shift, byteSet)
          // are now emitted as MaxonTryCallOp and matched by the call-op branch below; only
          // the non-throwing ops (clear, append) and __ManagedList mutations remain as
          // dedicated MaxonOp classes here.
          if (hasSelf) {
            var builtinSelfId = op switch {
              MaxonManagedMemClearOp o => o.ManagedStruct.Id,
              MaxonManagedMemAppendOp o => o.ManagedStruct.Id,
              MaxonManagedListInsertValueOp o => o.ManagedList.Id,
              MaxonManagedListInsertRelativeValueOp o => o.ManagedList.Id,
              MaxonManagedListDetachOp o => o.ManagedList.Id,
              MaxonManagedListRemoveOp o => o.ManagedList.Id,
              MaxonManagedListClearOp o => o.ManagedList.Id,
              MaxonManagedListCursorResetOp o => o.ManagedList.Id,
              MaxonManagedListNodeSetValueOp o => o.Node.Id,
              _ => -1
            };
            if (builtinSelfId >= 0 && selfDerivedIds.Contains(builtinSelfId)) {
              mutated ??= [];
              mutated.Add("self");
            }
          }

          // Mutating method calls on self-derived variables → mutates self (no ABI change needed)
          if (hasSelf && op is MaxonCallOp call && call.ArgVarNames is { Count: > 0 }) {
            var argName = call.ArgVarNames[0];
            // Match either a self-derived local (via `let x = self`) or a self-field name
            // (e.g. `managed` referenced as `self.managed`). The synthetic `__managed_mem_*`
            // throwing-call replacements emit ArgVarNames[0] = the field name, not a local.
            bool firstArgIsSelfTainted = argName != null
              && (selfDerivedVars.Contains(argName) || (selfFieldNames?.Contains(argName) ?? false));
            if (firstArgIsSelfTainted) {
              var methodName = call.Callee;
              var lastDot = methodName.LastIndexOf('.');
              if (lastDot >= 0) methodName = methodName[(lastDot + 1)..];
              if (IsMutatingMethodName(methodName) || IsMutatingBuiltinCallee(call.Callee)) {
                mutated ??= [];
                mutated.Add("self");
              }
            }
          }
        }
      }
      f.ReassignedParams = reassigned;
      f.MutatedParams = mutated;
    }

    // Second pass: transitive propagation through call chains using a
    // reverse call graph + worklist instead of a round-robin fixpoint.
    //
    // Edge(caller, argVar, calleeParam): "caller passes its own param `argVar`
    // as argument for callee's parameter `calleeParam`". When `calleeParam`
    // becomes reassigned or mutated in the callee, that status propagates to
    // `caller`'s `argVar`. We index edges by callee so that toggling a status
    // on a callee only scans edges that could reach it.
    var calleeToEdges = new Dictionary<string, List<(IrFunction<MaxonOp> caller, string argVar, string calleeParam)>>();
    foreach (var f in module.Functions) {
      var paramNames = new HashSet<string>(f.ParamNames);
      if (paramNames.Count == 0) continue;
      foreach (var b in f.Body.Blocks) {
        foreach (var op in b.Operations) {
          if (op is not MaxonCallOp call) continue;
          if (!funcLookup.TryGetValue(call.Callee, out var callee)) continue;
          if (call.ArgVarNames == null) continue;
          if (!calleeToEdges.TryGetValue(call.Callee, out var edgeList)) {
            edgeList = [];
            calleeToEdges[call.Callee] = edgeList;
          }
          for (int ci = 0; ci < call.ArgVarNames.Count && ci < callee.ParamNames.Count; ci++) {
            var argVar = call.ArgVarNames[ci];
            if (argVar == null || !paramNames.Contains(argVar)) continue;
            edgeList.Add((f, argVar, callee.ParamNames[ci]));
          }
        }
      }
    }

    // Seed worklist with every function that already has any mutated/reassigned
    // param. Each dequeue fires propagation across its incoming call edges.
    var worklist = new Queue<IrFunction<MaxonOp>>();
    var inWorklist = new HashSet<string>();
    foreach (var f in module.Functions) {
      if (f.ReassignedParams != null || f.MutatedParams != null) {
        worklist.Enqueue(f);
        inWorklist.Add(f.Name);
      }
    }

    while (worklist.Count > 0) {
      var callee = worklist.Dequeue();
      inWorklist.Remove(callee.Name);
      if (!calleeToEdges.TryGetValue(callee.Name, out var edges)) continue;
      foreach (var (caller, argVar, calleeParam) in edges) {
        bool callerChanged = false;
        if (callee.ReassignedParams != null && callee.ReassignedParams.Contains(calleeParam)) {
          caller.ReassignedParams ??= [];
          if (caller.ReassignedParams.Add(argVar)) callerChanged = true;
        }
        if (callee.MutatedParams != null && callee.MutatedParams.Contains(calleeParam)) {
          caller.MutatedParams ??= [];
          if (caller.MutatedParams.Add(argVar)) callerChanged = true;
        }
        if (callerChanged && inWorklist.Add(caller.Name)) {
          worklist.Enqueue(caller);
        }
      }
    }

    // Third pass: derive MutatedParamIndices from MutatedParams (for borrow checking)
    foreach (var f in module.Functions) {
      if (f.MutatedParams == null) continue;
      var indices = new HashSet<int>();
      for (int i = 0; i < f.ParamNames.Count; i++) {
        if (f.MutatedParams.Contains(f.ParamNames[i]))
          indices.Add(i);
      }
      if (indices.Count > 0)
        f.MutatedParamIndices = indices;
    }
  }

  private static bool IsMutatingMethodName(string methodName) =>
    methodName is "push" or "pop" or "insert" or "remove" or "set" or "clear"
      or "resize" or "reserve" or "append" or "ensureCapacity";

  // Synthetic callee names emitted by the parser for throwing __ManagedMemory builtins
  // (post-migration replacements for the dedicated MaxonManagedMem*Op classes that the
  // mutation analysis still recognises by type above). All of these mutate the receiver.
  private static bool IsMutatingBuiltinCallee(string callee) =>
    callee is "__managed_mem_set" or "__managed_mem_set_byte" or "__managed_mem_set_length"
      or "__managed_mem_grow" or "__managed_mem_shift_right" or "__managed_mem_shift_left"
      or "__managed_mem_remove"
      or "__managed_list_reinsert_first" or "__managed_list_reinsert_last"
      or "__managed_list_reinsert_after" or "__managed_list_reinsert_before"
      // close() zeroes _handle on the __ManagedFile struct.
      or "__managed_file_close";
}
