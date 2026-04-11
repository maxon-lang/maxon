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
            } else if (hasSelf && selfDerivedVars.Contains(assign.VarName)) {
              // Assignment to self-derived field → mutates self
              reassigned ??= [];
              reassigned.Add("self");
              mutated ??= [];
              mutated.Add("self");
            }
          }

          // Mutating builtin ops on self-derived SSA values → mutates self (no ABI change needed)
          if (hasSelf) {
            var builtinSelfId = op switch {
              MaxonManagedMemSetOp o => o.ManagedStruct.Id,
              MaxonManagedMemGrowOp o => o.ManagedStruct.Id,
              MaxonManagedMemSetLengthOp o => o.ManagedStruct.Id,
              MaxonManagedMemClearOp o => o.ManagedStruct.Id,
              MaxonManagedMemRemoveOp o => o.ManagedStruct.Id,
              MaxonManagedMemShiftOp o => o.ManagedStruct.Id,
              MaxonManagedMemByteSetOp o => o.ManagedStruct.Id,
              MaxonManagedMemAppendOp o => o.ManagedStruct.Id,
              MaxonManagedListInsertValueOp o => o.ManagedList.Id,
              MaxonManagedListInsertRelativeValueOp o => o.ManagedList.Id,
              MaxonManagedListReinsertOp o => o.ManagedList.Id,
              MaxonManagedListReinsertRelativeOp o => o.ManagedList.Id,
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
            if (argName != null && selfDerivedVars.Contains(argName)) {
              var methodName = call.Callee;
              var lastDot = methodName.LastIndexOf('.');
              if (lastDot >= 0) methodName = methodName[(lastDot + 1)..];
              if (IsMutatingMethodName(methodName)) {
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

    // Second pass: transitive propagation through call chains.
    // If F passes param x to G which reassigns/mutates the corresponding param,
    // then F also reassigns/mutates x.
    bool changed = true;
    while (changed) {
      changed = false;
      foreach (var f in module.Functions) {
        var paramNames = new HashSet<string>(f.ParamNames);
        if (paramNames.Count == 0) continue;
        foreach (var b in f.Body.Blocks) {
          foreach (var op in b.Operations) {
            if (op is not MaxonCallOp call) continue;
            if (!funcLookup.TryGetValue(call.Callee, out var callee)) continue;
            if (call.ArgVarNames == null) continue;
            for (int ci = 0; ci < call.ArgVarNames.Count && ci < callee.ParamNames.Count; ci++) {
              var argVar = call.ArgVarNames[ci];
              if (argVar == null || !paramNames.Contains(argVar)) continue;
              var calleeParamName = callee.ParamNames[ci];
              // Propagate ReassignedParams
              if (callee.ReassignedParams != null && callee.ReassignedParams.Contains(calleeParamName)) {
                f.ReassignedParams ??= [];
                if (f.ReassignedParams.Add(argVar)) changed = true;
              }
              // Propagate MutatedParams
              if (callee.MutatedParams != null && callee.MutatedParams.Contains(calleeParamName)) {
                f.MutatedParams ??= [];
                if (f.MutatedParams.Add(argVar)) changed = true;
              }
            }
          }
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
}
