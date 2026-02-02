using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

public static class SemanticCheckPass {
  public static void Run(MlirModule<MaxonOp> module) {
    // E3001: main function must exist
    var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main") ?? throw new CompileError(ErrorCode.SemanticNoMain, "No 'main' function found");

    // E3002: main must return int
    if (mainFunc.ReturnType == null || mainFunc.ReturnType != MlirType.I64) {
      throw new CompileError(ErrorCode.SemanticMainWrongReturnType, "Function 'main' must return int");
    }

    // E054: main cannot throw
    if (mainFunc.ThrowsType != null) {
      throw new CompileError(ErrorCode.SemanticMainCannotThrow, "main cannot throw: 'main'", mainFunc.SourceLine, mainFunc.SourceColumn);
    }
  }
}
