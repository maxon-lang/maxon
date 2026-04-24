using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Identifies parameters that are borrow-only: the callee does not extend the
/// parameter reference's lifetime past the call. This is the retention side of
/// Maxon's borrow convention — callees receive borrowed pointers and must not
/// decref them, so the only way for a call to affect the caller's refcount
/// balance on an argument is to <i>retain</i> (bump) the reference.
///
/// A parameter is retained (not borrow-only) when any value transitively
/// derived from its SSA id via local <c>memref.store</c> / <c>memref.load</c>
/// chains reaches one of:
///  - <c>std.call_runtime @mm_incref</c> on the tainted value;
///  - <c>memref.store_indirect</c> of the tainted value into heap memory;
///  - <c>func.return</c> of the tainted value (the callee transfers an owning
///    reference to its caller — except for <see cref="IrFunction{TOp}.ReturnsSelf"/>
///    which is a borrow return by convention);
///  - any argument slot of an outgoing <c>func.call</c>/<c>func.try_call</c>
///    where the callee's corresponding parameter is itself retained;
///  - any argument slot of an <c>func.indirect_call</c> (closure invocation),
///    since the callee is unknown.
///
/// Taint propagates only through direct stores/loads of slot variables. A
/// <c>memref.load_indirect</c> reads a sub-field of an object whose reference
/// is the parameter — the sub-field is a distinct allocation and retention of
/// it does not retain the parameter itself, so taint is <i>not</i> propagated
/// through <c>load_indirect</c>. Likewise, passing the parameter's pointer as
/// a callee-receiver of a borrow-only call does not retain it — only passing
/// to a retaining parameter position counts.
///
/// Results are stored on <see cref="IrFunction{TOp}.BorrowOnlyParamIndices"/>;
/// indices omitted from the set (including missing annotations) mean "retained
/// or unknown — conservatively assume retention".
/// </summary>
public static class ParameterRetentionAnalysisPass {
  public static void Run(IrModule<StandardOp> module) {
    var funcLookup = new Dictionary<string, IrFunction<StandardOp>>(module.Functions.Count);
    foreach (var f in module.Functions) funcLookup[f.Name] = f;

    // Retention is computed to a fixpoint. Each pass over a function may flip
    // parameters from borrow-only to retained when a downstream callee's
    // retention status changes. Worklist is keyed on callee→caller edges so we
    // only re-analyse functions whose callees became more retaining.
    var graph = module.CallGraph;

    // Seed: every function analysed once.
    var worklist = new Queue<IrFunction<StandardOp>>();
    var inWorklist = new HashSet<string>();
    foreach (var f in module.Functions) {
      worklist.Enqueue(f);
      inWorklist.Add(f.Name);
    }

    while (worklist.Count > 0) {
      var f = worklist.Dequeue();
      inWorklist.Remove(f.Name);

      var previous = f.BorrowOnlyParamIndices;
      var updated = Analyze(f, funcLookup);
      f.BorrowOnlyParamIndices = updated;

      if (!SameSet(previous, updated)) {
        // This function's retention summary changed. Re-analyse callers; their
        // retention for arguments passed at positions that lost borrow-only
        // status may now flip to retained.
        foreach (var caller in graph.GetCallers(f.Name)) {
          if (inWorklist.Add(caller.Name)) worklist.Enqueue(caller);
        }
      }
    }
  }

  private static bool SameSet(HashSet<int>? a, HashSet<int>? b) {
    if (ReferenceEquals(a, b)) return true;
    if (a == null || b == null) return (a?.Count ?? 0) == (b?.Count ?? 0);
    if (a.Count != b.Count) return false;
    foreach (var v in a) if (!b.Contains(v)) return false;
    return true;
  }

  /// <summary>
  /// Computes the borrow-only parameter indices for one function under the
  /// current view of other functions' annotations. Conservative: any param
  /// whose retention cannot be proven false is treated as retained.
  /// </summary>
  private static HashSet<int>? Analyze(
      IrFunction<StandardOp> f,
      Dictionary<string, IrFunction<StandardOp>> funcLookup) {
    if (f.ParamNames.Count == 0) return null;

    // Per-parameter taint state.
    //  - taintedSsa[i]: SSA ids whose value is (transitively) the parameter's pointer.
    //  - taintedSlot[i]: slot variable names whose last stored value was tainted.
    //
    // A slot re-tainted after a store of a non-tainted value loses its taint
    // for subsequent loads — tracked via a per-slot taint bit.
    int n = f.ParamNames.Count;
    var taintedSsa = new HashSet<int>[n];
    var taintedSlot = new Dictionary<string, bool>[n];
    for (int i = 0; i < n; i++) {
      taintedSsa[i] = [];
      taintedSlot[i] = [];
    }

    // Retention verdicts start optimistic (all borrow-only) and flip on first
    // evidence of retention.
    var retained = new bool[n];

    foreach (var block in f.Body.Blocks) {
      foreach (var op in block.Operations) {
        // --- Taint seeds: StdParamOp on each parameter's SSA result. ---
        if (op is StdParamOp p && p.Index >= 0 && p.Index < n) {
          taintedSsa[p.Index].Add(p.Result.Id);
          continue;
        }

        // --- Taint propagation through local slot stores. ---
        if (op is IStoreOp store) {
          for (int i = 0; i < n; i++) {
            taintedSlot[i][store.VarName] = taintedSsa[i].Contains(store.Value.Id);
          }
          continue;
        }

        // --- Taint propagation through local slot loads. ---
        if (op is ILoadOp load) {
          for (int i = 0; i < n; i++) {
            if (taintedSlot[i].TryGetValue(load.VarName, out var isTainted) && isTainted) {
              taintedSsa[i].Add(load.Result.Id);
            }
          }
          continue;
        }

        // --- Retention events. ---
        //
        // Each branch below identifies an op that escapes its operand into a
        // location outlasting the call. If any of our tracked parameters has
        // its taint on that operand, the parameter is retained.

        // mm_incref on the param's own pointer → retention.
        if (op is StdCallRuntimeOp rt && rt.Callee == "mm_incref" && rt.Args.Count > 0) {
          MarkRetainedByValue(taintedSsa, retained, rt.Args[0].Id);
          continue;
        }
        if (op is StdCallRuntimeIfNonnullOp rti && rti.Callee == "mm_incref" && rti.Args.Count > 0) {
          MarkRetainedByValue(taintedSsa, retained, rti.Args[0].Id);
          continue;
        }

        // store_indirect of the param into a heap field → retention.
        if (op is StdStoreIndirectOp si) {
          MarkRetainedByValue(taintedSsa, retained, si.Value.Id);
          continue;
        }

        // memcopy with tainted source → conservatively retention (the param's
        // pointer is being copied into heap memory).
        if (op is StdMemCopyOp mc) {
          MarkRetainedByValue(taintedSsa, retained, mc.SrcPtr.Id);
          continue;
        }
        if (op is StdMemCopyReverseOp mcr) {
          MarkRetainedByValue(taintedSsa, retained, mcr.SrcPtr.Id);
          continue;
        }

        // func.return of a tainted value → retention, unless the callee is a
        // self-returning method (borrow return by convention).
        if (op is StdReturnOp ret && ret.ReturnValue != null && !f.ReturnsSelf) {
          MarkRetainedByValue(taintedSsa, retained, ret.ReturnValue.Id);
          continue;
        }

        // Global stores of tainted values → retention.
        if (op is StdGlobalStoreI64Op gs) {
          MarkRetainedByValue(taintedSsa, retained, gs.Value.Id);
          continue;
        }

        // Direct calls: retention depends on the callee's per-param verdicts.
        if (op is StdCallOp call) {
          ApplyCallRetention(call.Callee, call.Args, funcLookup, taintedSsa, retained);
          continue;
        }
        if (op is StdTryCallOp tcall) {
          ApplyCallRetention(tcall.Callee, tcall.Args, funcLookup, taintedSsa, retained);
          continue;
        }

        // Indirect calls (closure invocations): callee unknown — any tainted
        // arg is conservatively retained.
        if (op is StdIndirectCallOp icall) {
          foreach (var arg in icall.Args) {
            MarkRetainedByValue(taintedSsa, retained, arg.Id);
          }
          continue;
        }
      }
    }

    // Convert the retained bitmap into the borrow-only index set.
    HashSet<int>? borrowOnly = null;
    for (int i = 0; i < n; i++) {
      if (!retained[i]) {
        borrowOnly ??= [];
        borrowOnly.Add(i);
      }
    }
    return borrowOnly;
  }

  /// <summary>
  /// For a direct (or try-) call, mark each caller parameter retained when its
  /// tainted SSA id appears at an argument position whose callee parameter is
  /// NOT in the callee's <see cref="IrFunction{TOp}.BorrowOnlyParamIndices"/>.
  /// Unknown callees (e.g. stdlib functions not in the module) and callees
  /// with no annotation yet are conservatively treated as retaining all args.
  /// </summary>
  private static void ApplyCallRetention(
      string calleeName,
      List<StdValue> args,
      Dictionary<string, IrFunction<StandardOp>> funcLookup,
      HashSet<int>[] taintedSsa,
      bool[] retained) {
    funcLookup.TryGetValue(calleeName, out var callee);
    var borrowOnly = callee?.BorrowOnlyParamIndices;

    for (int argIdx = 0; argIdx < args.Count; argIdx++) {
      bool argRetainedByCallee = borrowOnly == null || !borrowOnly.Contains(argIdx);
      if (!argRetainedByCallee) continue;
      MarkRetainedByValue(taintedSsa, retained, args[argIdx].Id);
    }
  }

  /// <summary>
  /// Mark every parameter whose taint set contains <paramref name="ssaId"/> as
  /// retained. Called when an op is identified as letting that SSA value escape.
  /// </summary>
  private static void MarkRetainedByValue(HashSet<int>[] taintedSsa, bool[] retained, int ssaId) {
    for (int i = 0; i < taintedSsa.Length; i++) {
      if (taintedSsa[i].Contains(ssaId)) retained[i] = true;
    }
  }
}
