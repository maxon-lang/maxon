using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Infers function purity by analyzing side effects. A function is impure if it
/// writes to stdout/stderr, mutates global state, mutates any parameter
/// (including self-field writes — detected via MutatedParams populated by
/// ParameterMutationAnalysisPass), calls runtime functions, or transitively
/// calls any impure function.
///
/// Depends on ParameterMutationAnalysisPass having run beforehand: that pass
/// resolves bare-name assignments like `pos = x` inside a method back to
/// "mutates self", which the Maxon-dialect IR doesn't make syntactically
/// obvious (such assigns lower to MaxonAssignOp("pos", ...) rather than
/// MaxonFieldAssignOp, so the assigned name alone isn't enough to classify).
/// </summary>
public static class PurityAnalysisPass {
  public static void Run(IrModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      if (func.ReturnType == null) {
        func.IsPure = false;
        continue;
      }
      if (func.Body.Blocks.Count == 0) {
        func.IsPure = false;
        continue;
      }
      // Any parameter mutation (including self-field writes) is impure.
      if (func.MutatedParams is { Count: > 0 }) {
        func.IsPure = false;
        continue;
      }
      if (IsDirectlyImpure(func)) {
        func.IsPure = false;
      }
    }

    // Propagate impurity transitively via a reverse call graph + worklist.
    // Build callers[callee] = list of functions that call callee once, then
    // whenever we mark a callee impure, push its callers onto a worklist.
    var funcLookup = new Dictionary<string, IrFunction<MaxonOp>>();
    foreach (var func in module.Functions) {
      funcLookup[func.Name] = func;
    }

    // callers[name] = functions that directly call `name`
    var callers = new Dictionary<string, List<IrFunction<MaxonOp>>>();
    // Mark any function containing an indirect call as impure up-front —
    // indirect calls are conservatively impure, matching the old behavior.
    foreach (var func in module.Functions) {
      if (!func.IsPure) continue;
      bool sawIndirect = false;
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is MaxonIndirectCallOp) { sawIndirect = true; break; }
          if (op is MaxonCallOp call) {
            if (!callers.TryGetValue(call.Callee, out var list)) {
              list = [];
              callers[call.Callee] = list;
            }
            list.Add(func);
          }
        }
        if (sawIndirect) break;
      }
      if (sawIndirect) func.IsPure = false;
    }

    // Seed worklist with all currently-impure functions; propagate backward.
    var worklist = new Queue<IrFunction<MaxonOp>>();
    var enqueued = new HashSet<string>();
    foreach (var func in module.Functions) {
      if (!func.IsPure) {
        worklist.Enqueue(func);
        enqueued.Add(func.Name);
      }
    }

    while (worklist.Count > 0) {
      var impure = worklist.Dequeue();
      if (!callers.TryGetValue(impure.Name, out var callerList)) continue;
      foreach (var caller in callerList) {
        if (!caller.IsPure) continue;
        caller.IsPure = false;
        if (enqueued.Add(caller.Name)) {
          worklist.Enqueue(caller);
        }
      }
    }
  }

  // I/O, globals, runtime calls, and mutations through managed memory or
  // managed lists. The managed-* mutations are conservative: the analyzer
  // doesn't distinguish "mutating a caller-provided array" from "mutating
  // a locally-constructed array", so any such op treats the function as
  // impure. This matches the existing analyzer's over-approximation.
  //
  // Parameter mutations (direct reassignment, self-field writes, and
  // builtin ops on self-derived values) are captured by MutatedParams
  // in the caller and are not re-checked here.
  private static bool IsDirectlyImpure(IrFunction<MaxonOp> func) {
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          case MaxonManagedWriteStdoutOp:
          case MaxonManagedWriteStderrOp:
          case MaxonGlobalStoreOp:
          case MaxonCallRuntimeOp:
            return true;
          case MaxonManagedMemSetOp:
          case MaxonManagedMemGrowOp:
          case MaxonManagedMemShiftOp:
          case MaxonManagedMemByteSetOp:
          case MaxonManagedMemAppendOp:
          case MaxonManagedMemSetLengthOp:
          case MaxonManagedMemRemoveOp:
          case MaxonManagedMemClearOp:
            return true;
          case MaxonManagedListInsertValueOp:
          case MaxonManagedListInsertRelativeValueOp:
          case MaxonManagedListReinsertOp:
          case MaxonManagedListReinsertRelativeOp:
          case MaxonManagedListDetachOp:
          case MaxonManagedListRemoveOp:
          case MaxonManagedListClearOp:
          case MaxonManagedListNodeSetValueOp:
            return true;
        }
      }
    }
    return false;
  }
}
