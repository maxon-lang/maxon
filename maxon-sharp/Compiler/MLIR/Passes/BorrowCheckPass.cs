using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class BorrowCheckPass {
  record BorrowRecord(string BorrowingVar, string SourceVar, int? Line, int? Column);

  public static void Run(MlirModule<MaxonOp> module) {
    var funcLookup = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcLookup[func.Name] = func;

    // MutatedParamIndices is already set by ParameterMutationAnalysisPass (runs earlier in pipeline)
    foreach (var func in module.Functions) {
      if (func.IsStdlib) continue;
      CheckFunction(func, funcLookup);
    }
  }

  private static void CheckFunction(
      MlirFunction<MaxonOp> func,
      Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    // Flatten all ops across all blocks for cross-block analysis.
    // Borrows often span blocks (e.g., try_call result flows through branching).
    var allOps = new List<MaxonOp>();
    foreach (var block in func.Body.Blocks)
      allOps.AddRange(block.Operations);
    if (allOps.Count == 0) return;

    // Pass 1: Compute last-use index for each variable (NLL)
    var lastUse = ComputeLastUse(allOps);

    // Pass 2: Track borrows and check for mutation conflicts
    var activeBorrows = new Dictionary<string, List<BorrowRecord>>();

    for (int i = 0; i < allOps.Count; i++) {
      var op = allOps[i];

      // Expire dead borrows (NLL: borrow dies after last use of borrowing var)
      ExpireBorrows(activeBorrows, lastUse, i);

      // Detect new borrows from method calls
      if (op is MaxonCallOp call) {
        DetectBorrow(call, allOps, i, funcLookup, activeBorrows);
        DetectMutationConflict(call, func, funcLookup, activeBorrows);
      }

      // Reassignment of a borrowed-from source kills the borrow (safe due to incref)
      if (op is MaxonAssignOp assign && !assign.IsDeclaration
          && activeBorrows.ContainsKey(assign.VarName)) {
        activeBorrows.Remove(assign.VarName);
      }
    }
  }

  /// Compute the last operation index where each variable is referenced.
  private static Dictionary<string, int> ComputeLastUse(List<MaxonOp> ops) {
    var lastUse = new Dictionary<string, int>();
    for (int i = 0; i < ops.Count; i++) {
      foreach (var varName in GetReferencedVars(ops[i]))
        lastUse[varName] = i;
    }
    return lastUse;
  }

  /// Get all variable names referenced (read) by an operation.
  private static IEnumerable<string> GetReferencedVars(MaxonOp op) {
    switch (op) {
      case MaxonVarRefOp varRef:
        yield return varRef.VarName;
        break;
      case MaxonStructVarRefOp structRef:
        yield return structRef.VarName;
        break;
      case MaxonCallOp call:
        if (call.ArgVarNames != null) {
          foreach (var name in call.ArgVarNames) {
            if (name != null) yield return name;
          }
        }
        break;
      case MaxonAssignOp:
        // We don't count assignment as a "use" of VarName for NLL purposes
        break;
    }
  }

  /// Remove borrows whose borrowing variable's last use is before the current index.
  private static void ExpireBorrows(
      Dictionary<string, List<BorrowRecord>> activeBorrows,
      Dictionary<string, int> lastUse, int currentIndex) {
    // Collect keys to avoid modification during iteration
    var sources = activeBorrows.Keys.ToList();
    foreach (var source in sources) {
      var borrows = activeBorrows[source];
      borrows.RemoveAll(b =>
        !lastUse.TryGetValue(b.BorrowingVar, out var last) || last < currentIndex);
      if (borrows.Count == 0)
        activeBorrows.Remove(source);
    }
  }

  /// Detect if a call creates a borrow from its receiver.
  private static void DetectBorrow(
      MaxonCallOp call, IReadOnlyList<MaxonOp> ops, int opIndex,
      Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
      Dictionary<string, List<BorrowRecord>> activeBorrows) {
    // Must have a result (returns something)
    if (call.Result == null) return;
    // Must be an instance method with a self argument
    if (call.ArgVarNames is not { Count: > 0 }) return;
    var sourceVar = call.ArgVarNames[0];
    if (sourceVar == null) return;
    // Skip internal/compiler-generated variables
    if (sourceVar.StartsWith("__")) return;

    // Check: is this a method call (callee has 'self' as first param)?
    if (!funcLookup.TryGetValue(call.Callee, out var callee)) return;
    if (callee.ParamNames.Count == 0 || callee.ParamNames[0] != "self") return;

    // Skip chainable methods (return Self type — builder pattern, not a borrow)
    if (callee.ReturnType != null && callee.ParamTypes.Count > 0
        && callee.ReturnType.Name == callee.ParamTypes[0].Name) return;

    // Skip if callee returns void
    if (callee.ReturnType == null || callee.ReturnType == MlirType.Void) return;

    // Value-type returns (primitives, simple enums) are copies, not borrows
    if (!callee.ReturnType.IsHeapAllocated) return;

    // Skip mutating methods that return extracted elements (pop, remove).
    // These return values that are no longer part of the collection's internal state.
    if (IsMutatingCall(call, funcLookup)) return;

    // Find what user variable the result ultimately gets assigned to.
    // For try_call, the result flows through intermediate __try_result_N variables.
    var targetVar = FindUltimateAssignmentTarget(call.Result.Id, ops, opIndex);
    if (targetVar == null) return;
    // Skip compiler-generated variables that didn't resolve to a user variable
    if (targetVar.StartsWith("__")) return;

    // Record the borrow
    if (!activeBorrows.ContainsKey(sourceVar))
      activeBorrows[sourceVar] = [];
    activeBorrows[sourceVar].Add(new BorrowRecord(targetVar, sourceVar, call.CallLine, call.CallColumn));
  }

  /// Find the ultimate user variable that a call result gets assigned to.
  /// Follows assignment chains through intermediate compiler-generated variables
  /// (e.g., try_call result -> __try_result_N -> user variable).
  private static string? FindUltimateAssignmentTarget(int resultId, IReadOnlyList<MaxonOp> ops, int opIndex) {
    var currentId = resultId;
    string? currentVar = null;

    // Follow assignment chains up to a reasonable depth
    for (int depth = 0; depth < 5; depth++) {
      string? nextVar = null;
      // Search forward from the current position for an assignment of currentId
      for (int j = opIndex + 1; j < ops.Count; j++) {
        if (ops[j] is MaxonAssignOp assign && assign.Value.Id == currentId) {
          nextVar = assign.VarName;
          break;
        }
      }
      if (nextVar == null) break;

      if (!nextVar.StartsWith("__")) {
        // Found a user variable — this is the ultimate target
        return nextVar;
      }

      // It's an intermediate variable — find the struct_var_ref that reads it,
      // then continue following that SSA value
      currentVar = nextVar;
      bool found = false;
      for (int j = opIndex + 1; j < ops.Count; j++) {
        if (ops[j] is MaxonStructVarRefOp svr && svr.VarName == currentVar) {
          currentId = svr.Result.Id;
          found = true;
          break;
        }
      }
      if (!found) break;
    }

    // If we only found compiler-generated variables, return the first one
    // (better to track it than miss the borrow entirely)
    return currentVar;
  }

  /// Check if a call mutates a variable that has active borrows.
  private static void DetectMutationConflict(
      MaxonCallOp call,
      MlirFunction<MaxonOp> func,
      Dictionary<string, MlirFunction<MaxonOp>> funcLookup,
      Dictionary<string, List<BorrowRecord>> activeBorrows) {
    if (activeBorrows.Count == 0) return;
    if (call.ArgVarNames is not { Count: > 0 }) return;

    // Check each argument: if it's a borrowed-from variable and this call mutates it
    for (int argIdx = 0; argIdx < call.ArgVarNames.Count; argIdx++) {
      var argVar = call.ArgVarNames[argIdx];
      if (argVar == null) continue;

      if (!activeBorrows.TryGetValue(argVar, out var borrows)) continue;
      if (borrows.Count == 0) continue;

      // Does this call mutate this argument?
      if (!DoesMutateArg(call, argIdx, funcLookup)) continue;

      // Conflict! Report the first active borrow.
      var borrow = borrows[0];
      var methodDisplay = FormatMethodName(call.Callee);
      var msg = borrow.Line != null
        ? $"cannot mutate '{argVar}' via '{methodDisplay}' while it is borrowed by '{borrow.BorrowingVar}' (borrowed at line {borrow.Line})"
        : $"cannot mutate '{argVar}' via '{methodDisplay}' while it is borrowed by '{borrow.BorrowingVar}'";

      throw new CompileError(ErrorCode.SemanticBorrowConflict, msg, call.CallLine, call.CallColumn) {
        FilePath = func.SourceFilePath
      };
    }
  }

  /// Check if a call mutates the argument at the given index.
  private static bool DoesMutateArg(
      MaxonCallOp call, int argIdx,
      Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(call.Callee, out var callee)) return false;
    return callee.MutatedParamIndices != null && callee.MutatedParamIndices.Contains(argIdx);
  }

  /// Check if a call mutates its receiver (self argument at index 0).
  private static bool IsMutatingCall(
      MaxonCallOp call,
      Dictionary<string, MlirFunction<MaxonOp>> funcLookup) {
    return DoesMutateArg(call, 0, funcLookup);
  }

  /// Format a callee name for display: "ns.StringArray.push" -> "StringArray.push"
  private static string FormatMethodName(string callee) {
    var firstDot = callee.IndexOf('.');
    return firstDot >= 0 ? callee[(firstDot + 1)..] : callee;
  }
}
