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
  /// The parser interposes a binding of this prefix between a call and the user's variable
  /// (2-Parser.cs EmitCallReturnTempAssign) purely to track the intermediate for refcounting,
  /// then drops it from scope tracking the moment a named variable takes ownership
  /// (FixupTempOwnership). The dead assign it leaves behind sits between a call and its real
  /// binding, so the candidate scan must look through it.
  private const string CallReturnTempPrefix = "__call_tmp_";

  /// <summary>
  /// Compiler-generated bindings this analysis models exactly as it models a user variable:
  /// one name, bound once, holding one struct pointer. They may be a candidate or an alias.
  ///
  /// This is a WHITELIST and must stay one. A `__` name that is NOT listed here falls through
  /// to the disqualifying arms in step 4, and that fallthrough is load-bearing: an array
  /// literal's element slots (`__arr_&lt;tag&gt;.&lt;i&gt;`) are not bindings at all but slots of one
  /// contiguous buffer that is memcpy'd onto the heap. Reading such a store as a harmless
  /// alias would put a genuinely escaping record on the stack and leave the array pointing at
  /// a dead frame. "Any temp I do not model is an escape" is the safe default; each entry
  /// below is a specific, checked departure from it.
  /// </summary>
  private static readonly string[] ModelledTempPrefixes = [
    // The iterator's `current()` result in a `for` loop. The parser declares it with exactly
    // the flags EmitCallReturnTempAssign gives `__call_tmp_N` (IsTemp | CallReturn) — a single
    // binding of a single call result, which is the shape this analysis already reasons about.
    "__forin_result_",
    // A `for (a, b) in ...` loop's item variable. It IS the loop variable, and carries a
    // generated name only because the user wrote a destructuring pattern where an identifier
    // would otherwise go: `for x in ...` binds the very same value to the user's own name and
    // has always been modelled. The name is the only difference.
    "__for_tuple_",
  ];

  /// Whether a binding is one the escape analysis can reason about — a user variable, or one
  /// of the compiler temps that behaves exactly like one (see <see cref="ModelledTempPrefixes"/>).
  private static bool IsModelledBinding(string varName) =>
    !varName.StartsWith("__") || Array.Exists(ModelledTempPrefixes, varName.StartsWith);

  public static void Run(IrModule<MaxonOp> module) {
    var funcLookup = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcLookup[func.Name] = func;

    // Build interprocedural escape map: sets func.EscapingParams on each function
    BuildEscapingParams(module, funcLookup);

    foreach (var func in module.Functions) {
      // Runs for stdlib too: the value-tuple ABI follows the SIGNATURE, so a directly-returned
      // tuple literal must lose its heap record wherever it was compiled.
      MarkDirectlyReturnedValueTuples(func, module);

      if (func.IsStdlib) continue;
      AnalyzeFunction(func, module, funcLookup);
    }
  }

  /// <summary>
  /// A tuple record produced and returned DIRECTLY — `return (a, b)` or `return makePair(..)` —
  /// from a function that hands the pair back in two registers needs no escape analysis and no
  /// heap record: it is never bound to a user name, so nothing can alias it, and the return
  /// copies its two fields into registers.
  ///
  /// It must lose the record, not merely be allowed to. `scope_end` runs BEFORE the return and
  /// the parser lists such a value in its KeepVars — "ownership transferred to caller", which
  /// is true of a returned POINTER and false of a returned pair of scalars. So a heap record
  /// here would be released by nobody (the caller was handed values, not a reference) and leak;
  /// decreffing it at scope_end instead would free it before the return reads its halves. A
  /// stack record has neither problem: there is nothing to release.
  ///
  /// This is separate from AnalyzeFunction's candidates, which all require a binding to give
  /// them a variable name to reason about. A directly-returned value has no name precisely
  /// BECAUSE it cannot escape, so it is the one case that needs no analysis.
  /// </summary>
  private static void MarkDirectlyReturnedValueTuples(IrFunction<MaxonOp> func, IrModule<MaxonOp> module) {
    if (!module.ValueTupleReturnFunctions.Contains(func.Name)) return;

    foreach (var block in func.Body.Blocks) {
      var ops = block.Operations;
      for (int i = 0; i < ops.Count - 1; i++) {
        int producerResultId;
        switch (ops[i]) {
          case MaxonStructLiteralOp lit when IsTypeEligible(lit, module):
            producerResultId = lit.Result.Id;
            break;

          case MaxonCallOp call and not MaxonTryCallOp
              when call.Result != null && ReturnsTwoRegisterValueTuple(call.Callee, module):
            producerResultId = call.Result.Id;
            break;

          default:
            continue;
        }

        if (IsDirectlyReturned(ops, i + 1, producerResultId))
          module.StackEligibleStructs.Add(producerResultId);
      }
    }
  }

  /// <summary>
  /// Whether a producer's result flows straight into this function's return, looking through
  /// the two things the parser interposes: the <see cref="CallReturnTempPrefix"/> bookkeeping
  /// binding, and the enclosing scope's `scope_end` (which names only variables, and so cannot
  /// be a use of an unbound value).
  ///
  /// Adjacency modulo those two IS the proof that the return is the value's only use: nothing
  /// earlier can reference a value that does not exist yet, and the return terminates the
  /// block. Anything else in between means "not this shape" — and an unmodelled shape must
  /// mean "do not promote".
  /// </summary>
  private static bool IsDirectlyReturned(List<MaxonOp> ops, int start, int producerResultId) {
    for (int i = start; i < ops.Count; i++) {
      switch (ops[i]) {
        case MaxonReturnOp ret:
          return ret.Value?.Id == producerResultId;

        case MaxonScopeEndOp:
          continue;

        case MaxonAssignOp assign when assign.IsDeclaration
            && assign.Value.Id == producerResultId
            && assign.VarName.StartsWith(CallReturnTempPrefix):
          continue;

        default:
          return false;
      }
    }
    return false;
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
    // Step 1: Collect candidates — a record this frame produces, immediately bound to a
    // declaration. Two producers qualify, on identical escape conditions:
    //   - a struct literal, which this frame builds itself;
    //   - a call whose callee returns a two-register value tuple, whose halves arrive in
    //     registers and so belong to this frame the moment they land.
    // The value-tuple call is what makes the callee's record unnecessary: nothing is handed
    // back by pointer, so there is no allocation for the caller to keep alive.
    var candidates = new Dictionary<string, int>();
    foreach (var block in func.Body.Blocks) {
      var ops = block.Operations;
      for (int i = 0; i < ops.Count - 1; i++) {
        int producerResultId;
        switch (ops[i]) {
          case MaxonStructLiteralOp lit when IsTypeEligible(lit, module):
            producerResultId = lit.Result.Id;
            break;

          // A throwing call is excluded: MaxonTryCallOp's result is only valid on the
          // success edge, and the error edge would leave the slots unwritten.
          case MaxonCallOp call and not MaxonTryCallOp
              when call.Result != null && ReturnsTwoRegisterValueTuple(call.Callee, module):
            producerResultId = call.Result.Id;
            break;

          default:
            continue;
        }

        if (FindBoundVariable(ops, i + 1, producerResultId) is string varName)
          candidates[varName] = producerResultId;
      }
    }

    if (candidates.Count == 0) return;

    // Step 2: Build SSA ID -> variable name map
    var ssaToVar = new Dictionary<int, string>();
    foreach (var (varName, resultId) in candidates) {
      ssaToVar[resultId] = varName;
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
    // Only bindings this analysis models count (see IsModelledBinding): an unmodelled compiler
    // temp — `__arr_0.0` and friends — is NOT a true alias, and leaving it untracked here is
    // what makes step 4 disqualify the candidate rather than wave the store through.
    var aliases = new Dictionary<string, HashSet<string>>(); // candidate -> set of alias var names
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonAssignOp assign && assign.IsDeclaration
            && IsModelledBinding(assign.VarName)
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

          // WHITELISTED: the parser's refcount-bookkeeping binding (see FindBoundVariable).
          // It is dropped from scope tracking the moment the named variable takes ownership,
          // and even were it live it would be harmless: the assign lowering propagates
          // stack-ness and the stack tag to an alias, so it resolves to the very slots the
          // candidate already owns rather than to a second record.
          case MaxonAssignOp assign when assign.IsDeclaration
              && assign.VarName.StartsWith(CallReturnTempPrefix)
              && ssaToVar.ContainsKey(assign.Value.Id):
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

          // WHITELISTED: returning a two-register value tuple does NOT escape it. The halves
          // are copied into registers at the return, so nothing of this frame's record
          // outlives the frame — which is precisely the constraint that forces every other
          // returned struct onto the heap (see GetReferencedIds, where MaxonReturnOp falls
          // through to the disqualifying default).
          case MaxonReturnOp ret when ret.Value != null
              && ssaToVar.ContainsKey(ret.Value.Id)
              && module.ValueTupleReturnFunctions.Contains(func.Name):
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
    foreach (var (varName, resultId) in candidates) {
      if (!disqualified.Contains(varName)) {
        module.StackEligibleStructs.Add(resultId);
      }
    }
  }

  /// <summary>
  /// The variable a producer's result is bound to, looking through any
  /// <see cref="CallReturnTempPrefix"/> bookkeeping binding in between.
  ///
  /// Null unless the result flows straight into a plain user declaration. Any other shape —
  /// no binding at all, a `@heap` binding, some other compiler temp — is one this analysis
  /// does not model, and not modelling a shape must mean "do not promote", never "promote
  /// anyway".
  /// </summary>
  private static string? FindBoundVariable(List<MaxonOp> ops, int start, int producerResultId) {
    for (int i = start; i < ops.Count; i++) {
      if (ops[i] is not MaxonAssignOp assign
          || !assign.IsDeclaration
          || assign.Value.Id != producerResultId
          || assign.ForceHeap) // @heap directive forces heap allocation
        return null;

      if (!assign.VarName.StartsWith(CallReturnTempPrefix))
        return IsModelledBinding(assign.VarName) ? assign.VarName : null;
    }
    return null;
  }

  /// Whether a direct call to <paramref name="callee"/> hands its result back in two
  /// registers rather than as a heap record — ValueTupleAbiPass's module-wide verdict, which
  /// is the same one the lowering reads. An unknown callee is absent from the set and so
  /// answers false, the conservative direction.
  private static bool ReturnsTwoRegisterValueTuple(string callee, IrModule<MaxonOp> module) =>
    module.ValueTupleReturnFunctions.Contains(callee);

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
      case MaxonManagedMemGetOp memGet:
        yield return memGet.ManagedStruct.Id;
        yield return memGet.Index.Id;
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
