namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// A source position captured for debug info: the 1-based line and column of the token that
/// originated an op. See docs/DEBUGGER_DESIGN.md.
///
/// The design speaks of a `(fileId, line, col)` span; here the span carries only `(line, col)`
/// and the FILE is resolved from the enclosing <see cref="IrFunction{TOp}.SourceFilePath"/> at emit
/// time. This is deliberate: a function is single-file, so its file is a per-FUNCTION fact that
/// already has one home on the function. Stamping a fileId onto every op's span would write that
/// same fact once per op — the "one fact written twice" pattern this codebase treats as the root
/// of its bugs. If inlining ever splices ops from a foreign file into a function (there is no
/// inliner in the bootstrap pipeline today), the span widens to carry a fileId at that point,
/// when there is finally a second file to distinguish.
/// </summary>
public readonly record struct SourceSpan(int Line, int Col);

/// <summary>
/// Propagates <see cref="SourceSpan"/>s across a lowering pass. A pass records, per destination
/// block, the index at which each source op's output begins (a "mark"), then calls
/// <see cref="AssignRange"/> to stamp the destination function's side-table so every emitted op can
/// be traced back to its originating source position.
///
/// This is METADATA ONLY. It never decides which ops are produced, their order, or their operands,
/// so a build that runs it emits byte-identical code to one that does not — the headline invariant
/// of the debugger (docs/DEBUGGER_DESIGN.md).
/// </summary>
public static class DebugSpanFlow {
  /// <summary>
  /// Record, into <paramref name="marks"/>, that the source op about to be lowered carries a span —
  /// paired with the destination block's CURRENT size, i.e. the index at which this op's output will
  /// begin. Call it at the top of a lowering pass's per-op loop, before the op is lowered. A null
  /// <paramref name="marks"/> (debug info off) or a span-less op records nothing.
  ///
  /// One home for "which source ops seed a line row", shared by all three lowering passes so a change
  /// to that rule cannot land in one pass and silently not the others.
  /// </summary>
  public static void Mark<TSrc, TDst>(List<(int Start, SourceSpan Span)>? marks,
      IrFunction<TSrc> srcFunc, TSrc srcOp, IrBlock<TDst> destBlock)
      where TSrc : IPrintableOp where TDst : IPrintableOp {
    if (marks != null && srcFunc.TryGetDebugSpan(srcOp, out var span)) {
      marks.Add((destBlock.Operations.Count, span));
    }
  }

  /// <summary>
  /// Stamp every op in <paramref name="destBlock"/> with the span of the source op that produced it.
  /// <paramref name="marks"/> holds `(destOpIndex, span)` pairs in ascending destOpIndex order — one
  /// per source op that carried a span. Each mark's span covers the destination ops in
  /// `[destOpIndex, nextMark.destOpIndex)` (or to the end of the block for the last mark). Ops before
  /// the first mark (prologue/parameter spills with no source line) are left untagged; the reader
  /// resolves them to the nearest preceding line, which is the correct line-table semantics.
  /// </summary>
  public static void AssignRange<TOp>(IrFunction<TOp> destFunc, IrBlock<TOp> destBlock,
      List<(int Start, SourceSpan Span)> marks) where TOp : IPrintableOp {
    for (int m = 0; m < marks.Count; m++) {
      int end = m + 1 < marks.Count ? marks[m + 1].Start : destBlock.Operations.Count;
      for (int i = marks[m].Start; i < end; i++) {
        destFunc.SetDebugSpan(destBlock.Operations[i], marks[m].Span);
      }
    }
  }
}
