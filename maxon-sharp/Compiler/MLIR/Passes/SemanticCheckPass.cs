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

    // E3002: main must return int (or int-based ranged type like Integer)
    var mainRetBase = mainFunc.ReturnType is MlirRangedPrimitiveType rpt ? rpt.BaseType : mainFunc.ReturnType;
    if (mainRetBase == null || mainRetBase != MlirType.I64) {
      throw new CompileError(ErrorCode.SemanticMainWrongReturnType, "Function 'main' must return int");
    }

    // E054: main cannot throw
    if (mainFunc.ThrowsType != null) {
      throw new CompileError(ErrorCode.SemanticMainCannotThrow, "main cannot throw: 'main'", mainFunc.SourceLine, mainFunc.SourceColumn);
    }
  }
}
