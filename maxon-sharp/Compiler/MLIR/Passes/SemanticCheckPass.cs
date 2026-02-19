using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class SemanticCheckPass {
  public static void Run(MlirModule<MaxonOp> module) {
    // E3001: main function must exist (check for exact match or suffix match)
    Logger.Debug(LogCategory.Semantic, $"SemanticCheckPass: Checking for main function among {module.Functions.Count} functions");
    var mainMatches = module.Functions.Where(f => f.Name == "main" || f.Name.EndsWith(".main")).ToList();
    Logger.Debug(LogCategory.Semantic, $"  Found {mainMatches.Count} main candidates: {string.Join(", ", mainMatches.Select(f => f.Name))}");
    var mainFunc = mainMatches.FirstOrDefault()
      ?? throw new CompileError(ErrorCode.SemanticNoMain, "No 'main' function found");

    if (mainMatches.Count > 1) {
      var second = mainMatches[1];
      throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
        $"Multiple 'main' functions found", second.SourceLine, second.SourceColumn) {
        FilePath = second.SourceFilePath
      };
    }

    // E3002: main must return ExitCode
    if (mainFunc.ReturnType is not MlirRangedPrimitiveType { Name: "ExitCode" }) {
      throw new CompileError(ErrorCode.SemanticMainWrongReturnType, "Function 'main' must return ExitCode");
    }

    // E054: main cannot throw
    if (mainFunc.ThrowsType != null) {
      throw new CompileError(ErrorCode.SemanticMainCannotThrow, "main cannot throw: 'main'", mainFunc.SourceLine, mainFunc.SourceColumn);
    }

    // Check discarded function results
    CheckDiscardedResults(module);
  }

  private static void CheckDiscardedResults(MlirModule<MaxonOp> module) {
    var funcLookup = new Dictionary<string, MlirFunction<MaxonOp>>();
    foreach (var func in module.Functions) {
      funcLookup[func.Name] = func;
    }

    foreach (var func in module.Functions) {
      foreach (var block in func.Body.Blocks) {
        foreach (var op in block.Operations) {
          if (op is not MaxonCallOp call) continue;
          if (call.Result == null) continue;
          if (!call.IsDiscardedResult && !call.IsLetDiscardResult) continue;
          if (!funcLookup.TryGetValue(call.Callee, out var callee)) continue;

          // Chainable methods (returning own type) can always be discarded
          if (IsChainable(callee)) continue;

          if (callee.IsPure) {
            throw new CompileError(ErrorCode.SemanticDiscardedPureResult,
              $"result of pure function '{FormatCalleeName(call.Callee)}' must be used",
              call.CallLine, call.CallColumn) { FilePath = func.SourceFilePath };
          }

          // Impure: explicit `let _ =` is allowed, bare discard is not
          if (call.IsDiscardedResult && !call.IsLetDiscardResult) {
            throw new CompileError(ErrorCode.SemanticDiscardedImpureResult,
              $"result of '{FormatCalleeName(call.Callee)}' is not used (assign to '_' to discard)",
              call.CallLine, call.CallColumn) { FilePath = func.SourceFilePath };
          }
        }
      }
    }
  }

  private static bool IsChainable(MlirFunction<MaxonOp> func) {
    if (func.ParamNames.Count == 0 || func.ParamNames[0] != "self") return false;
    if (func.ReturnType == null) return false;
    var selfType = func.ParamTypes[0];
    return func.ReturnType.Name == selfType.Name;
  }

  private static string FormatCalleeName(string callee) {
    // Strip first namespace segment: "ns.Type.method" -> "Type.method", "ns.func" -> "func"
    var dot = callee.IndexOf('.');
    return dot >= 0 ? callee[(dot + 1)..] : callee;
  }
}
