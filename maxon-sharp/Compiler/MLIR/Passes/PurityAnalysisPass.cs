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

  // Throwing managed-mem builtins that mutate state — calling any of these
  // makes the calling function impure. Read-only builtins (get, byte_at,
  // slice, create) are excluded.
  private static readonly HashSet<string> ImpureManagedMemBuiltins = [
    "__managed_mem_set", "__managed_mem_remove",
    "__managed_mem_set_byte",
    "__managed_mem_grow", "__managed_mem_set_length",
    "__managed_mem_shift_right", "__managed_mem_shift_left",
    "__managed_list_reinsert_first", "__managed_list_reinsert_last",
    "__managed_list_reinsert_after", "__managed_list_reinsert_before",
    // file I/O is inherently impure
    "__managed_file_size", "__managed_file_read", "__managed_file_write",
    "__managed_file_close", "__managed_file_exists",
    "__managed_file_open_read", "__managed_file_open_write",
    "__managed_file_open_write_executable",
    "__managed_file_delete", "__managed_file_stat",
    "__managed_file_stat_field", "__managed_file_stat_free",
    // directory I/O is inherently impure
    "__managed_directory_open_search", "__managed_directory_create",
    "__managed_directory_current_path", "__managed_directory_next",
    "__managed_directory_filename", "__managed_directory_close",
    "__managed_directory_exists",
  ];

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
          case MaxonManagedMemAppendOp:
          case MaxonManagedMemClearOp:
            return true;
          case MaxonManagedListInsertValueOp:
          case MaxonManagedListInsertRelativeValueOp:
          case MaxonManagedListDetachOp:
          case MaxonManagedListRemoveOp:
          case MaxonManagedListClearOp:
          case MaxonManagedListNodeSetValueOp:
            return true;
          // try-calls to throwing managed-mem builtins that mutate state are
          // also impure (these are synthetic builtins lowered via TryLowerManagedMemBuiltin,
          // not real functions, so transitive propagation won't catch them).
          case MaxonTryCallOp tryCall when ImpureManagedMemBuiltins.Contains(tryCall.Callee):
            return true;
          // Non-throwing synthetic builtins that still touch I/O or mutable state
          // (e.g. __managed_file_exists / __managed_file_close / __managed_file_stat_field).
          case MaxonCallOp callOp when ImpureManagedMemBuiltins.Contains(callOp.Callee):
            return true;
        }
      }
    }
    return false;
  }
}
