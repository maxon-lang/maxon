using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Escape analysis pass that identifies struct literals safe for stack allocation.
/// A struct is stack-eligible if it (and all its aliases) never escape to a location
/// that outlives the caller's stack frame: heap fields, globals, closures, or callees
/// that alias the parameter.
/// Phase 1: only structs with all-primitive fields (no heap-allocated field types).
/// </summary>
public static class StackPromotionAnalysisPass {
  public static void Run(IrModule<MaxonOp> module) {
    var funcLookup = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcLookup[func.Name] = func;

    // Build interprocedural escape map: sets func.EscapingParams on each function
    BuildEscapingParams(module, funcLookup);

    foreach (var func in module.Functions) {
      if (func.IsStdlib) continue;
      AnalyzeFunction(func, module, funcLookup);
    }
  }

  /// <summary>
  /// For each function, determine which struct-typed parameters escape.
  /// A param escapes if it's aliased, stored into a heap field, stored in a global,
  /// captured by a closure, or passed to another function where that param escapes.
  /// ReturnsSelf does NOT count as escaping (pointer returns to caller).
  /// Results are stored on func.EscapingParams.
  /// </summary>
  private static void BuildEscapingParams(
      IrModule<MaxonOp> module, Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    foreach (var func in module.Functions) {
      if (func.ParamNames.Count == 0) continue;

      var escapingParams = new HashSet<string>();

      // Build SSA ID -> param name map for struct params
      var paramSsa = new Dictionary<int, string>();
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonStructParamOp sp && func.ParamNames.Contains(sp.Name))
            paramSsa[sp.Result.Id] = sp.Name;
          if (op is MaxonStructVarRefOp svr && func.ParamNames.Contains(svr.VarName))
            paramSsa[svr.Result.Id] = svr.VarName;
          if (op is MaxonVarRefOp vr && vr.ValueKind == MaxonValueKind.Struct && func.ParamNames.Contains(vr.VarName))
            paramSsa[vr.Result.Id] = vr.VarName;
        }
      }

      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          switch (op) {
            // Reassignment of param (p = newValue) — callee creates new heap object
            // and stores through ref pointer, corrupts stack memory
            case MaxonAssignOp assign when !assign.IsDeclaration
                && func.ParamNames.Contains(assign.VarName):
              escapingParams.Add(assign.VarName);
              break;

            // Aliasing: var b = paramVar (incref/decref would corrupt stack memory)
            case MaxonAssignOp assign when !assign.IsDeclaration
                && paramSsa.TryGetValue(assign.Value.Id, out var aliasParam):
              escapingParams.Add(aliasParam);
              break;
            case MaxonAssignOp assign when assign.IsDeclaration
                && paramSsa.TryGetValue(assign.Value.Id, out var declAliasParam)
                && assign.VarName != declAliasParam:
              escapingParams.Add(declAliasParam);
              break;

            // Stored into another struct's field
            case MaxonFieldAssignOp fa when paramSsa.TryGetValue(fa.NewValue.Id, out var faParam):
              escapingParams.Add(faParam);
              break;

            // Stored in a global
            case MaxonGlobalStoreOp gs when paramSsa.TryGetValue(gs.Value.Id, out var gsParam):
              escapingParams.Add(gsParam);
              break;

            // Captured by a closure
            case MaxonClosureCreateOp closure: {
              foreach (var capName in closure.CapturedNames) {
                if (func.ParamNames.Contains(capName))
                  escapingParams.Add(capName);
              }
              break;
            }

            // Used as a field value in a struct literal
            case MaxonStructLiteralOp lit: {
              foreach (var (_, fieldVal) in lit.FieldValues) {
                if (paramSsa.TryGetValue(fieldVal.Id, out var litParam))
                  escapingParams.Add(litParam);
              }
              break;
            }

            // Inserted into managed list
            case MaxonManagedListInsertValueOp listInsert:
              if (paramSsa.TryGetValue(listInsert.Value.Id, out var listInsertParam))
                escapingParams.Add(listInsertParam);
              break;

            case MaxonManagedListInsertRelativeValueOp listRelInsert:
              if (paramSsa.TryGetValue(listRelInsert.Value.Id, out var listRelInsertParam))
                escapingParams.Add(listRelInsertParam);
              break;

            // __managed_mem_set is now emitted as a MaxonTryCallOp, not a dedicated op.
            // Args[2] is the value stored into the managed buffer (element writes escape to the heap).
            case MaxonTryCallOp tryCall when tryCall.Callee == "__managed_mem_set" && tryCall.Args.Count >= 3:
              if (paramSsa.TryGetValue(tryCall.Args[2].Id, out var memSetTryParam))
                escapingParams.Add(memSetTryParam);
              break;
          }
        }
      }

      if (escapingParams.Count > 0)
        func.EscapingParams = escapingParams;
    }

    // Second pass: propagate escapes through call chains.
    // If func A passes param X to func B's param Y, and B escapes Y, then A escapes X.
    bool changed = true;
    while (changed) {
      changed = false;
      foreach (var func in module.Functions) {
        if (func.ParamNames.Count == 0) continue;

        foreach (var block in func.Body.Blocks) {
          foreach (var op in block.Operations) {
            if (op is not MaxonCallOp call) continue;

            if (!funcLookup.TryGetValue(call.Callee, out var callee)) continue;
            if (callee.EscapingParams == null) continue;

            for (int i = 0; i < call.Args.Count && i < callee.ParamNames.Count; i++) {
              if (!callee.EscapingParams.Contains(callee.ParamNames[i])) continue;

              // This arg position escapes in the callee. Check if the arg is one of our params.
              string? paramName = null;
              if (call.ArgVarNames != null && i < call.ArgVarNames.Count)
                paramName = call.ArgVarNames[i];

              if (paramName != null && func.ParamNames.Contains(paramName)) {
                func.EscapingParams ??= [];
                if (func.EscapingParams.Add(paramName))
                  changed = true;
              }
            }
          }
        }
      }
    }
  }

  private static void AnalyzeFunction(
      IrFunction<MaxonOp> func, IrModule<MaxonOp> module,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    // Step 1: Collect candidates: struct literal immediately followed by a declaration assign.
    var candidates = new Dictionary<string, (MaxonStructLiteralOp Literal, int BlockIndex)>();
    for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
      var block = func.Body.Blocks[blockIdx];
      var ops = block.Operations;
      for (int i = 0; i < ops.Count - 1; i++) {
        if (ops[i] is MaxonStructLiteralOp lit
            && ops[i + 1] is MaxonAssignOp assign
            && assign.IsDeclaration
            && assign.Value.Id == lit.Result.Id
            && !assign.ForceHeap // @heap directive forces heap allocation
            && !assign.VarName.StartsWith("__") // Skip compiler-generated temps
            && IsTypeEligible(lit, module)) {
          candidates[assign.VarName] = (lit, blockIdx);
        }
      }
    }

    if (candidates.Count == 0) return;

    // Step 2: Build SSA ID -> variable name map
    var ssaToVar = new Dictionary<int, string>();
    foreach (var (varName, (lit, _)) in candidates) {
      ssaToVar[lit.Result.Id] = varName;
    }
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonStructVarRefOp svr && candidates.ContainsKey(svr.VarName))
          ssaToVar[svr.Result.Id] = svr.VarName;
        if (op is MaxonVarRefOp vr && vr.ValueKind == MaxonValueKind.Struct && candidates.ContainsKey(vr.VarName))
          ssaToVar[vr.Result.Id] = vr.VarName;
      }
    }

    // Step 3: Track aliases (var b = a). Both must not escape for the original to be eligible.
    // Skip compiler-generated temps (__arr_0.0 etc.) — they're not true aliases.
    var aliases = new Dictionary<string, HashSet<string>>(); // candidate -> set of alias var names
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonAssignOp assign && assign.IsDeclaration
            && !assign.VarName.StartsWith("__")
            && ssaToVar.TryGetValue(assign.Value.Id, out var srcVar)
            && assign.VarName != srcVar) {
          if (!aliases.ContainsKey(srcVar)) aliases[srcVar] = [];
          aliases[srcVar].Add(assign.VarName);
          // Track alias SSA values too
          ssaToVar[assign.Value.Id] = srcVar; // already there, but ensure
        }
      }
    }
    // Also track StructVarRef for aliases
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonStructVarRefOp svr) {
          foreach (var (candidate, aliasSet) in aliases) {
            if (aliasSet.Contains(svr.VarName))
              ssaToVar[svr.Result.Id] = candidate;
          }
        }
      }
    }

    // Step 4: Check escape conditions. A candidate is disqualified if it or any alias escapes.
    var disqualified = new HashSet<string>();

    // Reverse map: alias name -> candidate name (for O(1) lookup)
    var aliasToCandidate = new Dictionary<string, string>();
    foreach (var (cand, aliasSet) in aliases) {
      foreach (var alias in aliasSet)
        aliasToCandidate[alias] = cand;
    }

    string? ResolveCandidate(string varName) {
      if (candidates.ContainsKey(varName)) return varName;
      return aliasToCandidate.GetValueOrDefault(varName);
    }

    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          // WHITELISTED: initial declaration assign of the struct literal
          case MaxonAssignOp assign when assign.IsDeclaration
              && ssaToVar.TryGetValue(assign.Value.Id, out var declVar) && assign.VarName == declVar:
            break;

          // WHITELISTED: alias declaration (var b = a) — tracked above
          case MaxonAssignOp assign when assign.IsDeclaration
              && ssaToVar.TryGetValue(assign.Value.Id, out var aliasSrc)
              && aliases.TryGetValue(aliasSrc, out var aliasSet2) && aliasSet2.Contains(assign.VarName):
            break;

          // DISQUALIFIED: reassignment of candidate or alias
          case MaxonAssignOp assign when !assign.IsDeclaration && ResolveCandidate(assign.VarName) is string reassignCand:
            disqualified.Add(reassignCand);
            break;

          // DISQUALIFIED: non-alias assign referencing candidate SSA
          case MaxonAssignOp assign when ssaToVar.TryGetValue(assign.Value.Id, out var assignVar)
              && !(aliases.TryGetValue(assignVar, out var aSet) && aSet.Contains(assign.VarName)):
            disqualified.Add(assignVar);
            break;

          // WHITELISTED: struct var ref (needed for field access, calls, etc.)
          case MaxonStructVarRefOp:
            break;

          // WHITELISTED: field access on a candidate's SSA value
          case MaxonFieldAccessOp fa when ssaToVar.ContainsKey(fa.StructValue.Id):
            break;

          // WHITELISTED: field assign where candidate is the TARGET
          case MaxonFieldAssignOp fa when ssaToVar.ContainsKey(fa.StructValue.Id)
              && !ssaToVar.ContainsKey(fa.NewValue.Id):
            break;

          // DISQUALIFIED: field assign where candidate is the value being stored
          case MaxonFieldAssignOp fa when ssaToVar.TryGetValue(fa.NewValue.Id, out var faNewVar):
            disqualified.Add(faNewVar);
            break;

          // CONDITIONAL: passed to a function call — check if callee escapes that param.
          // MaxonTryCallOp inherits from MaxonCallOp so throwing calls are covered here too,
          // including throwing __managed_mem_set where element stores escape to the heap.
          case MaxonCallOp call:
            DisqualifyEscapingCallArgs(call.Callee, call.Args, call.ArgVarNames, ssaToVar, funcLookup, disqualified, ResolveCandidate);
            break;

          // DISQUALIFIED: captured by a closure
          case MaxonClosureCreateOp closure: {
            foreach (var capName in closure.CapturedNames) {
              if (ResolveCandidate(capName) is string capCand)
                disqualified.Add(capCand);
            }
            foreach (var capVal in closure.CapturedValues) {
              if (ssaToVar.TryGetValue(capVal.Id, out var capVar))
                disqualified.Add(capVar);
            }
            break;
          }

          // DISQUALIFIED: used as a field value in another struct literal
          case MaxonStructLiteralOp lit: {
            foreach (var (_, fieldVal) in lit.FieldValues) {
              if (ssaToVar.TryGetValue(fieldVal.Id, out var fvVar))
                disqualified.Add(fvVar);
            }
            break;
          }

          // DISQUALIFIED: any other op that references a candidate's SSA value
          default: {
            foreach (var ssaId in GetReferencedIds(op)) {
              if (ssaToVar.TryGetValue(ssaId, out var refVar))
                disqualified.Add(refVar);
            }
            foreach (var varName in GetReferencedVarNames(op)) {
              if (ResolveCandidate(varName) is string refCand)
                disqualified.Add(refCand);
            }
            break;
          }
        }
      }
    }

    // Step 5: Add surviving candidates
    foreach (var (varName, (lit, _)) in candidates) {
      if (!disqualified.Contains(varName)) {
        module.StackEligibleStructs.Add(lit.Result.Id);
      }
    }
  }

  /// Check if a callee escapes the parameter at the given arg index.
  private static bool CalleeEscapesParam(
      string callee, int argIndex,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(callee, out var calleeFunc)) return true; // unknown callee = conservative
    if (argIndex >= calleeFunc.ParamNames.Count) return true;
    var paramName = calleeFunc.ParamNames[argIndex];
    // Struct params that aren't struct-typed don't need escape checking
    if (calleeFunc.ParamTypes[argIndex] is not IrStructType) return false;
    return calleeFunc.EscapingParams != null && calleeFunc.EscapingParams.Contains(paramName);
  }

  /// Disqualify any stack-promotion candidate passed as an arg to a call whose callee escapes
  /// that argument position. Shared between MaxonCallOp and MaxonTryCallOp (the latter inherits
  /// from the former; a single `case MaxonCallOp` covers both).
  private static void DisqualifyEscapingCallArgs(
      string callee, List<MaxonValue> args, List<string?>? argVarNames,
      Dictionary<int, string> ssaToVar,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup,
      HashSet<string> disqualified,
      Func<string, string?> resolveCandidate) {
    if (argVarNames != null) {
      for (int i = 0; i < argVarNames.Count; i++) {
        var argName = argVarNames[i];
        if (argName == null) continue;
        if (resolveCandidate(argName) is string cand && CalleeEscapesParam(callee, i, funcLookup))
          disqualified.Add(cand);
      }
    }
    for (int i = 0; i < args.Count; i++) {
      if (ssaToVar.TryGetValue(args[i].Id, out var argVar) && CalleeEscapesParam(callee, i, funcLookup))
        disqualified.Add(argVar);
    }
  }

  /// Get all SSA value IDs referenced by an operation (catch-all for non-whitelisted ops).
  private static IEnumerable<int> GetReferencedIds(MaxonOp op) {
    switch (op) {
      case MaxonReturnOp ret when ret.Value != null:
        yield return ret.Value.Id;
        break;
      case MaxonRefEqOp refEq:
        yield return refEq.Lhs.Id;
        yield return refEq.Rhs.Id;
        break;
      case MaxonGlobalStoreOp gs:
        yield return gs.Value.Id;
        break;
      case MaxonManagedListInsertValueOp insert:
        yield return insert.Value.Id;
        break;
      case MaxonManagedListInsertRelativeValueOp insert:
        yield return insert.Value.Id;
        break;
      case MaxonThrowOp throwOp:
        yield return throwOp.ErrorValue.Id;
        break;
    }
  }

  /// Get variable names referenced by an operation.
  private static IEnumerable<string> GetReferencedVarNames(MaxonOp op) {
    switch (op) {
      case MaxonVarRefOp varRef:
        yield return varRef.VarName;
        break;
    }
  }

  private static bool IsTypeEligible(MaxonStructLiteralOp lit, IrModule<MaxonOp> module) {
    if (lit.ArrayLiteralTag != null) return false;
    if (!module.TypeDefs.TryGetValue(lit.TypeName, out var typeDef)) return false;
    if (typeDef is not IrStructType structType) return false;
    if (TypeAliasInfo.IsManagedMemoryType(lit.TypeName, module.TypeAliasSources)) return false;
    if (TypeAliasInfo.IsManagedListType(lit.TypeName, module.TypeAliasSources)) return false;

    foreach (var field in structType.Fields) {
      if (field.Type.IsHeapAllocated) return false;
      if (module.TypeDefs.TryGetValue(field.Type.Name, out var resolvedFieldType)
          && resolvedFieldType.IsHeapAllocated) return false;
    }
    return true;
  }
}
