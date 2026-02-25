using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

// Per-allocation tracking within a scope
public record AllocInfo(string VarName, string? StructTypeName, bool IsFromCall, int StructLiteralResultId = -1);

// Per-scope analysis results
public class ScopeInfo(string scopeVar) {
  public string ScopeVar { get; } = scopeVar; public List<AllocInfo> Allocations { get; } = [];
  public HashSet<string> EscapingVars { get; } = [];
  public HashSet<string> PassedToFunctionVars { get; } = [];
  public bool ReceivesMovedAllocs { get; set; }
  public bool ReceivesNonReturnMoves { get; set; }
  public bool NeedsRuntimeFrame { get; set; }
  public bool CanStaticFree { get; set; }
  public bool CanStaticReturn { get; set; }
  // The var name being return-moved (if any) — used to skip mm_move and mm_free_simple
  public string? ReturnMoveVar { get; set; }
  // Struct literal result ID of the return allocation — needs mm_alloc (not mm_alloc_simple)
  // so callers can safely mm_move it
  public int ReturnAllocResultId { get; set; } = -1;
  // Struct literal result IDs eligible for stack allocation (per-allocation, not per-scope)
  public HashSet<int> StackAllocIds { get; } = [];
}

/// <summary>
/// Analyzes Maxon dialect IR to determine per-scope memory management properties.
/// Runs before MaxonToStandardConversion and provides information used to optimize
/// scope frame allocation (eliding empty scope frames, static free, etc.).
/// Results are stored in module.ScopeAnalysis for consumption by the conversion.
/// </summary>
public static class ScopeAnalysisPass {
  public static void Run(MlirModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      if (func.IsStdlib) continue;
      if (func.Body.Blocks.Count == 0) continue;
      AnalyzeFunction(func, module);
    }
  }

  private static void AnalyzeFunction(MlirFunction<MaxonOp> func, MlirModule<MaxonOp> module) {
    // Map from MaxonValue ID to the op that produced it, used to trace
    // whether an assigned value comes from a struct literal vs a call
    var producingOps = new Dictionary<int, MaxonOp>();
    // All ScopeInfos for this function, keyed by scope var name
    var scopeInfos = new Dictionary<string, ScopeInfo>();
    // Stack tracking the currently active scope (innermost last)
    var scopeStack = new List<string>();
    // Function lookup for purity checks — pure functions can't reparent/free allocations
    var funcByName = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var f in module.Functions) funcByName[f.Name] = f;
    // Track var names whose struct values are used as fields in other struct literals —
    // these get EmitReparent (mm_move mode=1) during lowering, which reads backpointers
    var nestedInStructField = new HashSet<string>();
    // Map value ID → var name for struct var refs
    var structVarRefNames = new Dictionary<int, string>();

    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        // Track producing ops for value tracing
        switch (op) {
          case MaxonStructLiteralOp structLit:
            producingOps[structLit.Result.Id] = op;
            // Track field values that reference struct vars — these get EmitReparent during lowering
            foreach (var (_, fieldVal) in structLit.FieldValues) {
              if (structVarRefNames.TryGetValue(fieldVal.Id, out var refVarName))
                nestedInStructField.Add(refVarName);
            }
            break;
          case MaxonCallOp callOp when callOp.Result != null:
            producingOps[callOp.Result.Id] = op;
            break;
          case MaxonStructVarRefOp varRefOp:
            structVarRefNames[varRefOp.Result.Id] = varRefOp.VarName;
            break;
        }

        switch (op) {
          case MaxonScopeEnterOp enterOp: {
            var info = new ScopeInfo(enterOp.ResultVar);
            scopeInfos[enterOp.ResultVar] = info;
            scopeStack.Add(enterOp.ResultVar);
            break;
          }

          case MaxonScopeExitOp exitOp when exitOp.Tag == "block_exit": {
            // Only pop on normal block exits — cleanup exits (return_cleanup,
            // break_cleanup, continue_cleanup) appear in alternate control flow
            // blocks and must not disturb the scope stack for subsequent blocks
            for (int i = scopeStack.Count - 1; i >= 0; i--) {
              if (scopeStack[i] == exitOp.ScopeVar) {
                scopeStack.RemoveAt(i);
                break;
              }
            }
            break;
          }

          case MaxonAssignOp { IsDeclaration: true } assignOp
            when assignOp.ValueKind is MaxonValueKind.Struct or MaxonValueKind.Enum: {
            // If the assigned value comes from a struct_var_ref, the source var will be
            // reparented (mm_move mode=1) during lowering — mark it as nested
            if (structVarRefNames.TryGetValue(assignOp.Value.Id, out var srcVarName))
              nestedInStructField.Add(srcVarName);

            if (scopeStack.Count == 0) break;
            var currentScope = scopeStack[^1];
            if (!scopeInfos.TryGetValue(currentScope, out var scopeInfo)) break;

            bool isFromCall = false;
            string? structTypeName = null;
            int structLitResultId = -1;

            if (producingOps.TryGetValue(assignOp.Value.Id, out var producer)) {
              switch (producer) {
                case MaxonStructLiteralOp structLit:
                  structTypeName = structLit.TypeName;
                  structLitResultId = structLit.Result.Id;
                  isFromCall = false;
                  break;
                case MaxonCallOp:
                  isFromCall = true;
                  break;
                default:
                  throw new InvalidOperationException(
                    $"ScopeAnalysisPass: unhandled producing op type {producer.GetType().Name} for var '{assignOp.VarName}'");
              }
            } else {
              // Value comes from something we don't track (e.g. parameter, phi) — treat as call
              isFromCall = true;
            }

            scopeInfo.Allocations.Add(new AllocInfo(assignOp.VarName, structTypeName, isFromCall, structLitResultId));
            break;
          }

          case MaxonMoveOp moveOp: {
            if (scopeStack.Count > 0) {
              var currentScope = scopeStack[^1];
              if (scopeInfos.TryGetValue(currentScope, out var srcInfo)) {
                srcInfo.EscapingVars.Add(moveOp.VarName);
                if (moveOp.Tag == "return_move")
                  srcInfo.ReturnMoveVar = moveOp.VarName;
              }
            }
            // Mark the destination scope as receiving moved allocs
            if (scopeInfos.TryGetValue(moveOp.DestScopeVar, out var destInfo)) {
              destInfo.ReceivesMovedAllocs = true;
              if (moveOp.Tag != "return_move")
                destInfo.ReceivesNonReturnMoves = true;
            }
            break;
          }

          case MaxonCallOp callOp: {
            // Only track vars passed to functions that might call mm_move on their
            // parameters — such functions are unsafe for mm_alloc_simple allocations
            // because mm_move reads the backpointer at [ptr-8]
            if (callOp.ArgVarNames != null && scopeStack.Count > 0) {
              bool calleeSafe = funcByName.TryGetValue(callOp.Callee, out var callee)
                && callee.IsPure && !HasMoveOnParams(callee);
              if (!calleeSafe) {
                var currentScope = scopeStack[^1];
                if (scopeInfos.TryGetValue(currentScope, out var scopeInfo)) {
                  foreach (var argVarName in callOp.ArgVarNames) {
                    if (argVarName != null)
                      scopeInfo.PassedToFunctionVars.Add(argVarName);
                  }
                }
              }
            }
            break;
          }
        }
      }

    }

    // Compute derived properties for each scope
    foreach (var (scopeVar, info) in scopeInfos) {
      // Per-allocation stack eligibility: flat primitive struct with nonzero size,
      // non-escaping, not nested in another struct, not passed to unsafe functions,
      // not an array element temp (these get EmitReparent during array literal lowering)
      foreach (var a in info.Allocations) {
        if (a.StructLiteralResultId >= 0
            && !a.IsFromCall
            && IsFlatPrimitiveStruct(a.StructTypeName, module)
            && GetStructSize(a.StructTypeName, module) > 0
            && !info.EscapingVars.Contains(a.VarName)
            && !info.PassedToFunctionVars.Contains(a.VarName)
            && !nestedInStructField.Contains(a.VarName)
            && !a.VarName.StartsWith("__arr_")) {
          info.StackAllocIds.Add(a.StructLiteralResultId);
        }
      }

      // Count heap allocations (non-stack-allocated)
      int heapAllocCount = info.Allocations.Count(a => !info.StackAllocIds.Contains(a.StructLiteralResultId));

      // A scope needs a runtime frame if it has heap allocations or receives moved allocs
      info.NeedsRuntimeFrame = heapAllocCount > 0 || info.ReceivesMovedAllocs;

      // A scope can use static free when all heap allocations qualify
      // (stack-allocated ones are excluded since they don't need freeing)
      info.CanStaticFree = heapAllocCount > 0
        && info.EscapingVars.Count == 0
        && !info.ReceivesMovedAllocs
        && info.Allocations.All(a =>
          info.StackAllocIds.Contains(a.StructLiteralResultId)
          || (!a.IsFromCall
            && IsFlatPrimitiveStruct(a.StructTypeName, module)
            && !info.PassedToFunctionVars.Contains(a.VarName)
            && !nestedInStructField.Contains(a.VarName)));

      // CanStaticReturn: the only escaping var is the return value, it's a flat primitive
      // struct literal, and all other allocations qualify for static free.
      // The return value uses mm_alloc (registered in caller's scope via __mm_current_scope),
      // other allocs use mm_alloc_simple/mm_free_simple, and mm_move + mm_scope_exit are skipped.
      if (info.ReturnMoveVar != null) {
        var retAlloc = info.Allocations.FirstOrDefault(a => a.VarName == info.ReturnMoveVar);
        if (info.EscapingVars.Count == 1
            && info.EscapingVars.Contains(info.ReturnMoveVar)
            && !info.ReceivesNonReturnMoves
            && retAlloc != null
            && !retAlloc.IsFromCall
            && IsFlatPrimitiveStruct(retAlloc.StructTypeName, module)
            && info.Allocations.All(a =>
              a.VarName == info.ReturnMoveVar
              || info.StackAllocIds.Contains(a.StructLiteralResultId)
              || (!a.IsFromCall
                && IsFlatPrimitiveStruct(a.StructTypeName, module)
                && !info.PassedToFunctionVars.Contains(a.VarName)
                && !nestedInStructField.Contains(a.VarName)))) {
          info.CanStaticReturn = true;
          info.ReturnAllocResultId = retAlloc.StructLiteralResultId;
        }
      }
    }

    // Skip storing results for __module_init's root scope — not safe to optimize
    if (scopeInfos.Count > 0)
      module.ScopeAnalysis[func.Name] = scopeInfos;
  }

  /// <summary>
  /// Returns true if the function has any MaxonMoveOp — meaning it calls mm_move
  /// at runtime, which reads backpointers and is unsafe for mm_alloc_simple allocations.
  /// </summary>
  private static bool HasMoveOnParams(MlirFunction<MaxonOp> func) {
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        if (op is MaxonMoveOp) return true;
      }
    }
    return false;
  }

  private static int GetStructSize(string? structTypeName, MlirModule<MaxonOp> module) {
    if (structTypeName == null) return 0;
    if (!module.TypeDefs.TryGetValue(structTypeName, out var typeDef)) return 0;
    return typeDef.SizeInBytes;
  }

  /// <summary>
  /// Returns true if the struct type is a simple flat struct with only primitive fields.
  /// Only flat structs can safely use mm_alloc_simple (no child allocations, no backpointer needed).
  /// </summary>
  private static bool IsFlatPrimitiveStruct(string? structTypeName, MlirModule<MaxonOp> module) {
    if (structTypeName == null) return false;
    if (!module.TypeDefs.TryGetValue(structTypeName, out var typeDef)) return false;
    if (typeDef is not MlirStructType structType) return false;
    foreach (var field in structType.Fields) {
      var resolved = MlirType.Resolve(field.Type);
      // Only allow primitive types (i64, f64, i32, f32, i8, i16, i1, function pointers)
      if (resolved is MlirStructType) return false;
      if (resolved is MlirUnionType) return false;
      if (resolved is MlirRangedPrimitiveType) continue; // ranged ints resolve to primitives
      if (resolved == MlirType.I64 || resolved == MlirType.F64 ||
          resolved == MlirType.I32 || resolved == MlirType.F32 ||
          resolved == MlirType.I8 || resolved == MlirType.I16 ||
          resolved == MlirType.I1) continue;
      return false; // Unknown field type — conservative
    }
    return true;
  }
}
