using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Decides, once and for the whole module, which functions hand their result back in two
/// registers instead of as a pointer to a heap record (see
/// <see cref="IrStructType.IsTwoRegisterValueTuple"/>), and records them on
/// <see cref="IrModule{TOp}.ValueTupleReturnFunctions"/>.
///
/// The ABI must be decided in ONE place because a callee and its call sites have to agree
/// without consulting each other: if they disagree, the caller reads a scalar as a pointer.
/// That is why this is a module-wide verdict rather than a predicate each site re-derives.
///
/// Two exclusions keep the gate narrow. Both leave the function on today's heap lowering,
/// which costs an allocation and never costs correctness:
///
///   - THROWING functions. A try-call's second return register already carries the error flag,
///     and on the error path there is no tuple to hand back at all.
///
///   - ADDRESS-TAKEN functions. A function reached through a function VALUE is called by its
///     TYPE, and a function type cannot express whether the target throws: a throwing and a
///     non-throwing `(Num, Num) -> (Num, Num)` are the same `IrFunctionType`, so an indirect
///     call cannot tell which ABI its target uses. Excluding address-taken functions removes
///     the question — every indirect call keeps the pointer convention it has today.
/// </summary>
public static class ValueTupleAbiPass {
  public static void Run(IrModule<MaxonOp> module) {
    var addressTaken = CollectAddressTakenFunctions(module);

    module.ValueTupleReturnFunctions.Clear();
    foreach (var func in module.Functions) {
      if (func.ThrowsType != null) continue;
      if (addressTaken.Contains(func.Name)) continue;
      if (IrStructType.AsTwoRegisterValueTuple(func.ReturnType, module.TypeDefs) == null) continue;

      module.ValueTupleReturnFunctions.Add(func.Name);
    }
  }

  /// <summary>
  /// Names of every function whose ADDRESS is taken — the two ops that can produce a callable
  /// pointer to a named function. Both are emitted by the parser and merely copied by
  /// FunctionCloner / MonomorphizationPass, so scanning the post-monomorphisation module sees
  /// every one of them.
  /// </summary>
  private static HashSet<string> CollectAddressTakenFunctions(IrModule<MaxonOp> module) {
    var addressTaken = new HashSet<string>();
    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          switch (op) {
            case MaxonFunctionRefOp fnRef:
              addressTaken.Add(fnRef.FunctionName);
              break;

            case MaxonClosureCreateOp closure:
              addressTaken.Add(closure.FunctionName);
              break;
          }
        }
      }
    }
    return addressTaken;
  }
}
