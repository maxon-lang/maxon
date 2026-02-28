using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Infers function purity by analyzing side effects. A function is impure if it
/// writes to stdout/stderr, mutates global state, mutates parameters, calls
/// runtime functions, or transitively calls any impure function.
/// </summary>
public static class PurityAnalysisPass {
  public static void Run(MlirModule<MaxonOp> module) {
    foreach (var func in module.Functions) {
      if (func.ReturnType == null) {
        func.IsPure = false;
        continue;
      }
      if (func.Body.Blocks.Count == 0) {
        func.IsPure = false;
        continue;
      }
      if (IsDirectlyImpure(func)) {
        func.IsPure = false;
      }
    }

    // Propagate impurity transitively: calling an impure function makes you impure
    var funcLookup = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions) {
      funcLookup[func.Name] = func;
    }

    bool changed = true;
    while (changed) {
      changed = false;
      foreach (var func in module.Functions) {
        if (!func.IsPure) continue;
        foreach (var block in func.Body.Blocks) {
          foreach (var op in block.Operations) {
            if (op is MaxonCallOp call
                && funcLookup.TryGetValue(call.Callee, out var callee)
                && !callee.IsPure) {
              func.IsPure = false;
              changed = true;
              goto nextFunc;
            }
            // Indirect calls (closures) are conservatively impure
            if (op is MaxonIndirectCallOp) {
              func.IsPure = false;
              changed = true;
              goto nextFunc;
            }
          }
        }
        nextFunc:;
      }
    }
  }

  private static bool IsDirectlyImpure(MlirFunction<MaxonOp> func) {
    var paramNames = new HashSet<string>(func.ParamNames);
    foreach (var block in func.Body.Blocks) {
      foreach (var op in block.Operations) {
        switch (op) {
          case MaxonManagedWriteStdoutOp:
          case MaxonManagedWriteStderrOp:
          case MaxonGlobalStoreOp:
          case MaxonCallRuntimeOp:
            return true;
          // Direct assignment to a parameter (mutating param)
          case MaxonAssignOp assign when !assign.IsDeclaration && paramNames.Contains(assign.VarName):
            return true;
          // Mutating managed memory through self or parameters
          case MaxonManagedMemSetOp:
          case MaxonManagedMemGrowOp:
          case MaxonManagedMemShiftOp:
          case MaxonManagedMemByteSetOp:
          case MaxonManagedMemConcatOp:
          case MaxonManagedMemSetLengthOp:
            return true;
          // Swapping heap-pointer fields mutates the parent struct
          case MaxonSwapFieldOp:
          case MaxonSwapPayloadOp:
            return true;
          // Chain mutation operations modify the chain data structure in-place
          case MaxonChainInsertValueOp:
          case MaxonChainInsertRelativeValueOp:
          case MaxonChainReinsertOp:
          case MaxonChainReinsertRelativeOp:
          case MaxonChainDetachOp:
          case MaxonChainRemoveOp:
          case MaxonChainClearOp:
          case MaxonChainNodeSetValueOp:
            return true;
        }
      }
    }
    return false;
  }
}
