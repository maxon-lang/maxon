using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Passes;

/// <summary>
/// Shared CFG helpers for Standard dialect passes that need to inspect block
/// terminators without performing full dialect-specific analysis.
/// </summary>
internal static class StandardCfgHelpers {
  public static List<string> GetSuccessors(IrBlock<StandardOp> block) {
    if (block.Operations.Count == 0) return [];
    var lastOp = block.Operations[^1];
    return lastOp switch {
      StdBrOp br => [br.Target],
      StdCondBrOp condBr => [condBr.ThenBlock, condBr.ElseBlock],
      StdReturnOp => [],
      StdErrorReturnOp => [],
      // Block without a terminator: CfgBuilder treats an empty successor list
      // as "fall through to the next block in layout order".
      _ => [],
    };
  }

  public static bool EndsWithTerminator(IrBlock<StandardOp> block) {
    if (block.Operations.Count == 0) return false;
    var lastOp = block.Operations[^1];
    return lastOp is StdBrOp or StdCondBrOp or StdReturnOp or StdErrorReturnOp;
  }
}
