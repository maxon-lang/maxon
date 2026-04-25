using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

public static class BorrowCheckPass {
  record BorrowRecord(string BorrowingVar, string SourceVar, int? Line, int? Column);

  public static void Run(IrModule<MaxonOp> module) {
    var funcLookup = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var func in module.Functions)
      funcLookup[func.Name] = func;

    var hot = StageTimer.HotFunctions > 0 ? new List<(string Name, long Ms)>() : null;

    // MutatedParamIndices is already set by ParameterMutationAnalysisPass.
    // CheckFunction reads funcLookup (read-only after build above) and may
    // throw CompileError on violation; ParallelFunctions captures errors and
    // re-throws the lexicographically-first one.
    ParallelFunctions.Run(module, func => {
      if (func.IsStdlib) return;
      CheckFunction(func, funcLookup);
    }, hot);

    if (hot != null) StageTimer.PrintHotFunctions("borrow", hot);
  }

  private static void CheckFunction(
      IrFunction<MaxonOp> func,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    // Borrows often span blocks (try_call result flows through branching), so
    // analyses use a single global op index across all blocks. We previously
    // materialized a flat List<MaxonOp> to that end; that allocation isn't
    // needed — we can walk blocks in order with a running counter and feed the
    // same indices into all the per-op tables in one pass.
    if (func.Body.Blocks.Count == 0) return;

    // assignsByValueId[valueId] = ordered list of (opIndex, MaxonAssignOp).
    // FindUltimateAssignmentTarget needs "first assign after a given index",
    // which is a binary search on this list.
    var assignsByValueId = new Dictionary<int, List<(int idx, MaxonAssignOp op)>>();
    // structVarRefByVar[varName] = ordered list of (opIndex, MaxonStructVarRefOp).
    var structVarRefByVar = new Dictionary<string, List<(int idx, MaxonStructVarRefOp op)>>();
    // Last-use index per variable (NLL).
    var lastUse = new Dictionary<string, int>();

    int totalOps = 0;
    for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
      var ops = func.Body.Blocks[bi].Operations;
      for (int oi = 0; oi < ops.Count; oi++) {
        var op = ops[oi];
        int gi = totalOps++;
        if (op is MaxonAssignOp a) {
          if (!assignsByValueId.TryGetValue(a.Value.Id, out var list)) {
            list = [];
            assignsByValueId[a.Value.Id] = list;
          }
          list.Add((gi, a));
        } else if (op is MaxonStructVarRefOp svr) {
          if (!structVarRefByVar.TryGetValue(svr.VarName, out var list)) {
            list = [];
            structVarRefByVar[svr.VarName] = list;
          }
          list.Add((gi, svr));
        }
        foreach (var varName in GetReferencedVars(op))
          lastUse[varName] = gi;
      }
    }
    if (totalOps == 0) return;

    // Pass 2: Find pending borrows (call → activation index, where activation
    // is the op that binds the user-visible borrowing variable).
    //
    // Activating at the assignment rather than at the call lets the linear
    // walk respect CFG branching: try-otherwise emits the otherwise-error
    // block between the call and the assign-in-the-continue-block, so any
    // mutation inside the otherwise block sits BEFORE the activation index
    // and does not see the borrow. The borrow is also semantically absent
    // there — when the call threw, the borrowing variable was never bound.
    var pendingBorrows = new Dictionary<int, List<(string sourceVar, BorrowRecord record)>>();
    {
      int gi = 0;
      for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
        var ops = func.Body.Blocks[bi].Operations;
        for (int oi = 0; oi < ops.Count; oi++) {
          int i = gi++;
          if (ops[oi] is not MaxonCallOp call) continue;
          var pending = DetectBorrow(call, i, funcLookup, assignsByValueId, structVarRefByVar);
          if (pending == null) continue;
          var (activationIdx, sourceVar, record) = pending.Value;
          if (!pendingBorrows.TryGetValue(activationIdx, out var list)) {
            list = [];
            pendingBorrows[activationIdx] = list;
          }
          list.Add((sourceVar, record));
        }
      }
    }

    // Pass 3: Walk ops linearly; activate pending borrows at their assignment
    // index, expire dead borrows after their borrowing variable's last use,
    // and check each call for mutation conflicts.
    var activeBorrows = new Dictionary<string, List<BorrowRecord>>();
    int gi2 = 0;
    for (int bi = 0; bi < func.Body.Blocks.Count; bi++) {
      var ops = func.Body.Blocks[bi].Operations;
      for (int oi = 0; oi < ops.Count; oi++) {
        var op = ops[oi];
        int i = gi2++;

        ExpireBorrows(activeBorrows, lastUse, i);

        if (pendingBorrows.TryGetValue(i, out var nowActive)) {
          foreach (var (sourceVar, record) in nowActive) {
            if (!activeBorrows.TryGetValue(sourceVar, out var list)) {
              list = [];
              activeBorrows[sourceVar] = list;
            }
            list.Add(record);
          }
        }

        if (op is MaxonCallOp call) {
          DetectMutationConflict(call, func, funcLookup, activeBorrows);
        }

        // Reassignment of a borrowed-from source kills the borrow (safe due to incref)
        if (op is MaxonAssignOp assign && !assign.IsDeclaration
            && activeBorrows.ContainsKey(assign.VarName)) {
          activeBorrows.Remove(assign.VarName);
        }
      }
    }
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

  /// Detect if a call creates a borrow from its receiver. Returns the op
  /// index where the borrow should activate (the assignment that binds the
  /// user-visible borrowing variable), the source var, and the borrow record.
  /// Returns null if the call doesn't create a tracked borrow.
  private static (int activationIdx, string sourceVar, BorrowRecord record)? DetectBorrow(
      MaxonCallOp call, int opIndex,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup,
      Dictionary<int, List<(int idx, MaxonAssignOp op)>> assignsByValueId,
      Dictionary<string, List<(int idx, MaxonStructVarRefOp op)>> structVarRefByVar) {
    // Must have a result (returns something)
    if (call.Result == null) return null;
    // Must be an instance method with a self argument
    if (call.ArgVarNames is not { Count: > 0 }) return null;
    var sourceVar = call.ArgVarNames[0];
    if (sourceVar == null) return null;
    // Skip internal/compiler-generated variables
    if (sourceVar.StartsWith("__")) return null;

    // Check: is this a method call (callee has 'self' as first param)?
    if (!funcLookup.TryGetValue(call.Callee, out var callee)) return null;
    if (callee.ParamNames.Count == 0 || callee.ParamNames[0] != "self") return null;

    // Skip chainable methods (return Self type — builder pattern, not a borrow)
    if (callee.ReturnType != null && callee.ParamTypes.Count > 0
        && callee.ReturnType.Name == callee.ParamTypes[0].Name) return null;

    // Only track borrows from generic/parameterized receiver types (i.e.,
    // collection-like types such as Array, ArrayIterator). Non-generic types
    // return value copies from method calls, not pointers into a managed
    // backing buffer, so they cannot create dangling-reference borrows.
    if (callee.ParamTypes.Count == 0
        || callee.ParamTypes[0] is not IrStructType { TypeParams.Count: > 0 }) return null;

    // Skip if callee returns void
    if (callee.ReturnType == null || callee.ReturnType == IrType.Void) return null;

    // Value-type returns (primitives, simple enums) are copies, not borrows
    if (!callee.ReturnType.IsHeapAllocated) return null;

    // Skip mutating methods that return extracted elements (pop, remove).
    // These return values that are no longer part of the collection's internal state.
    if (IsMutatingCall(call, funcLookup)) return null;

    // Find what user variable the result ultimately gets assigned to, plus
    // the op index of the assignment so we can defer borrow activation.
    // For try_call, the result flows through intermediate __try_result_N variables.
    var target = FindUltimateAssignmentTarget(call.Result.Id, opIndex, assignsByValueId, structVarRefByVar);
    if (target == null) return null;
    var (targetVar, activationIdx) = target.Value;
    // Skip compiler-generated variables that didn't resolve to a user variable
    if (targetVar.StartsWith("__")) return null;

    return (activationIdx, sourceVar, new BorrowRecord(targetVar, sourceVar, call.CallLine, call.CallColumn));
  }

  /// Find the ultimate user variable that a call result gets assigned to,
  /// returning (varName, assignOpIndex). Follows assignment chains through
  /// intermediate compiler-generated variables (e.g., try_call result ->
  /// __try_result_N -> user variable). Uses presorted per-valueId assign /
  /// per-varName struct_var_ref indices so each step of the walk is O(log k),
  /// avoiding the O(ops) inner scans the old implementation did per borrow
  /// (F8 in nested-foraging-hummingbird).
  private static (string varName, int assignIdx)? FindUltimateAssignmentTarget(
      int resultId, int opIndex,
      Dictionary<int, List<(int idx, MaxonAssignOp op)>> assignsByValueId,
      Dictionary<string, List<(int idx, MaxonStructVarRefOp op)>> structVarRefByVar) {
    var currentId = resultId;
    string? currentVar = null;
    int currentVarAssignIdx = -1;

    for (int depth = 0; depth < 5; depth++) {
      // First assignment after opIndex whose Value.Id == currentId
      if (!assignsByValueId.TryGetValue(currentId, out var assignList)) break;
      var assignHit = FirstAfter(assignList, opIndex);
      if (assignHit == null) break;
      var nextVar = assignHit.Value.op.VarName;

      if (!nextVar.StartsWith("__")) {
        // Found a user variable — this is the ultimate target
        return (nextVar, assignHit.Value.idx);
      }

      // It's an intermediate variable — find the struct_var_ref that reads it,
      // then continue following that SSA value
      currentVar = nextVar;
      currentVarAssignIdx = assignHit.Value.idx;
      if (!structVarRefByVar.TryGetValue(currentVar, out var svrList)) break;
      var svrHit = FirstAfter(svrList, opIndex);
      if (svrHit == null) break;
      currentId = svrHit.Value.op.Result.Id;
    }

    // If we only found compiler-generated variables, return the first one
    // (better to track it than miss the borrow entirely)
    if (currentVar == null) return null;
    return (currentVar, currentVarAssignIdx);
  }

  /// Binary search: first entry whose idx > exclusiveLowerBound.
  private static (int idx, T op)? FirstAfter<T>(List<(int idx, T op)> sorted, int exclusiveLowerBound) {
    int lo = 0, hi = sorted.Count;
    while (lo < hi) {
      int mid = (lo + hi) >>> 1;
      if (sorted[mid].idx > exclusiveLowerBound) hi = mid;
      else lo = mid + 1;
    }
    return lo < sorted.Count ? sorted[lo] : null;
  }

  /// Check if a call mutates a variable that has active borrows.
  private static void DetectMutationConflict(
      MaxonCallOp call,
      IrFunction<MaxonOp> func,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup,
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
      Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    if (!funcLookup.TryGetValue(call.Callee, out var callee)) return false;
    return callee.MutatedParamIndices != null && callee.MutatedParamIndices.Contains(argIdx);
  }

  /// Check if a call mutates its receiver (self argument at index 0).
  private static bool IsMutatingCall(
      MaxonCallOp call,
      Dictionary<string, IrFunction<MaxonOp>> funcLookup) {
    return DoesMutateArg(call, 0, funcLookup);
  }

  /// Format a callee name for display: "ns.StringArray.push" -> "StringArray.push"
  private static string FormatMethodName(string callee) {
    var firstDot = callee.IndexOf('.');
    return firstDot >= 0 ? callee[(firstDot + 1)..] : callee;
  }
}
