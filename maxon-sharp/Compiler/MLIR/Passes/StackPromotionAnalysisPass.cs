using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Escape analysis pass that identifies struct literals safe for stack allocation.
/// Uses a whitelist approach: a struct literal is stack-eligible only if every use of
/// the variable is one of: field access, field assign (as target), or struct var ref
/// within the same basic block. Any other use disqualifies it.
/// Phase 1: only structs with all-primitive fields (no heap-allocated field types).
/// </summary>
public static class StackPromotionAnalysisPass {
  public static void Run(MlirModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      if (func.IsStdlib) continue;
      AnalyzeFunction(func, module);
    }
  }

  private static void AnalyzeFunction(MlirFunction<MaxonOp> func, MlirModule<MaxonOp> module) {
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
            && !assign.VarName.StartsWith("__") // Skip compiler-generated temps (array elements, etc.)
            && IsTypeEligible(lit, module)) {
          candidates[assign.VarName] = (lit, blockIdx);
        }
      }
    }

    if (candidates.Count == 0) return;

    // Step 2: Build SSA ID -> variable name map by tracking StructVarRef chains
    // and any other op that loads a candidate variable producing a struct-typed SSA value
    var ssaToVar = new Dictionary<int, string>();
    foreach (var (varName, (lit, _)) in candidates) {
      ssaToVar[lit.Result.Id] = varName;
    }
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonStructVarRefOp svr && candidates.ContainsKey(svr.VarName))
          ssaToVar[svr.Result.Id] = svr.VarName;
        // VarRefOp can also load struct variables when used as struct-typed expressions
        if (op is MaxonVarRefOp vr && vr.ValueKind == MaxonValueKind.Struct && candidates.ContainsKey(vr.VarName))
          ssaToVar[vr.Result.Id] = vr.VarName;
      }
    }

    // Step 3: Whitelist check — walk all ops and disqualify candidates used in non-whitelisted ways
    var disqualified = new HashSet<string>();
    for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
      var block = func.Body.Blocks[blockIdx];
      foreach (var op in block.Operations) {
        switch (op) {
          // WHITELISTED: initial declaration assign of the struct literal
          case MaxonAssignOp assign when assign.IsDeclaration
              && ssaToVar.TryGetValue(assign.Value.Id, out var declVar) && assign.VarName == declVar:
            break; // OK

          // DISQUALIFIED: any other assign referencing a candidate's SSA value
          case MaxonAssignOp assign when ssaToVar.TryGetValue(assign.Value.Id, out var assignVar):
            disqualified.Add(assignVar);
            break;

          // DISQUALIFIED: reassignment of the candidate variable
          case MaxonAssignOp assign when !assign.IsDeclaration && candidates.ContainsKey(assign.VarName):
            disqualified.Add(assign.VarName);
            break;

          // WHITELISTED: struct var ref in the same block (needed for field access)
          case MaxonStructVarRefOp svr when candidates.TryGetValue(svr.VarName, out var svrInfo) && blockIdx == svrInfo.BlockIndex:
            break; // OK

          // DISQUALIFIED: struct var ref in a different block
          case MaxonStructVarRefOp svr when candidates.ContainsKey(svr.VarName):
            disqualified.Add(svr.VarName);
            break;

          // WHITELISTED: field access on a candidate's SSA value (reading a field)
          case MaxonFieldAccessOp fa when ssaToVar.ContainsKey(fa.StructValue.Id):
            break; // OK

          // WHITELISTED: field assign where the candidate is the TARGET (not the value being stored)
          case MaxonFieldAssignOp fa when ssaToVar.ContainsKey(fa.StructValue.Id)
              && !ssaToVar.ContainsKey(fa.NewValue.Id):
            break; // OK

          // DISQUALIFIED: field assign where a candidate is the value being stored INTO another struct
          case MaxonFieldAssignOp fa when ssaToVar.TryGetValue(fa.NewValue.Id, out var faNewVar):
            disqualified.Add(faNewVar);
            break;

          // DISQUALIFIED: passed to a function call (by variable name or SSA value)
          case MaxonCallOp call: {
            if (call.ArgVarNames != null) {
              foreach (var argName in call.ArgVarNames) {
                if (argName != null && candidates.ContainsKey(argName))
                  disqualified.Add(argName);
              }
            }
            foreach (var arg in call.Args) {
              if (ssaToVar.TryGetValue(arg.Id, out var argVar))
                disqualified.Add(argVar);
            }
            break;
          }

          // DISQUALIFIED: captured by a closure
          case MaxonClosureCreateOp closure: {
            foreach (var capName in closure.CapturedNames) {
              if (candidates.ContainsKey(capName))
                disqualified.Add(capName);
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
            // Also check variable names used by any op
            foreach (var varName in GetReferencedVarNames(op)) {
              if (candidates.ContainsKey(varName))
                disqualified.Add(varName);
            }
            break;
          }
        }
      }
    }

    // Step 4: Add surviving candidates
    foreach (var (varName, (lit, _)) in candidates) {
      if (!disqualified.Contains(varName)) {
        module.StackEligibleStructs.Add(lit.Result.Id);
      }
    }
  }

  /// Get all SSA value IDs referenced by an operation (catch-all for non-whitelisted ops).
  private static IEnumerable<int> GetReferencedIds(MaxonOp op) {
    // Extract all MaxonValue references from any operation via its PrintableOperands
    // isn't possible since they're formatted strings. Instead, enumerate known op types
    // that carry struct-typed SSA values.
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
      case MaxonBinOp bin:
        yield return bin.Lhs.Id;
        yield return bin.Rhs.Id;
        break;
      case MaxonManagedListInsertValueOp insert:
        yield return insert.Value.Id;
        break;
      case MaxonManagedListInsertRelativeValueOp insert:
        yield return insert.Value.Id;
        break;
      case MaxonStringInterpOp interp:
        foreach (var part in interp.Parts) {
          if (part.ExprValue != null) yield return part.ExprValue.Id;
        }
        break;
      case MaxonThrowOp throwOp:
        yield return throwOp.ErrorValue.Id;
        break;
    }
  }

  /// Get variable names referenced by an operation (beyond ArgVarNames which is checked separately).
  private static IEnumerable<string> GetReferencedVarNames(MaxonOp op) {
    switch (op) {
      case MaxonVarRefOp varRef:
        yield return varRef.VarName;
        break;
    }
  }

  private static bool IsTypeEligible(MaxonStructLiteralOp lit, MlirModule<MaxonOp> module) {
    if (lit.ArrayLiteralTag != null) return false;
    if (!module.TypeDefs.TryGetValue(lit.TypeName, out var typeDef)) return false;
    if (typeDef is not MlirStructType structType) return false;
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
