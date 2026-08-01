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
///
/// ⚠ A MARK IS AN INDEX INTO ONE DESTINATION BLOCK, AND ONLY THAT BLOCK CAN INTERPRET IT. Every
/// mark in a list handed to <see cref="AssignRange"/> must have been measured against the block
/// handed alongside it. A pass whose destination block is REPLACED while a source block is still
/// being lowered — <c>MaxonToStandardConversion</c> is one; its bounds checks, divide-by-zero guards
/// and `try` error edges each hand back a fresh merge block through a `ref` parameter — must
/// therefore stamp and reset at every such switch. It is the pass that changes blocks, so it is the
/// pass that owns the reset; the two target lowerings hold one block per source block by
/// construction and have nothing to do.
///
/// This was assumed rather than maintained, and cost the whole feature: marks measured against a
/// long, since-abandoned block were replayed against the short merge block that replaced it, walking
/// off its end and failing the compile with `E9001 ... Index was out of range` — on programs the spec
/// suite compiles successfully every run, because it compiles them with debug info off.
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
  ///
  /// <paramref name="destBlock"/> must be the same block every mark in <paramref name="marks"/> was
  /// measured against — see the type's remarks.
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
  ///
  /// The same untagged-prefix rule is what covers a block a source op created part-way through its
  /// own lowering (an error or merge block): it holds no mark, and the block it was appended after
  /// ends with that op's own line, so the nearest preceding row is the right one.
  ///
  /// Every mark must be an index into <paramref name="destBlock"/> — the caller's obligation, stated
  /// on the type. Violating it is a compiler bug, not bad input, so it is a hard failure here rather
  /// than a clamp: a clamp would keep compiling and write a line table pointing at the wrong lines,
  /// which is a debugger that lies.
  /// </summary>
  public static void AssignRange<TOp>(IrFunction<TOp> destFunc, IrBlock<TOp> destBlock,
      List<(int Start, SourceSpan Span)> marks) where TOp : IPrintableOp {
    // Validated before any stamping, not as each range is walked: mark m's range ENDS at mark m+1's
    // start, so a mark past the end of the block is read one iteration before its own.
    foreach (var (start, _) in marks) {
      if (start > destBlock.Operations.Count) {
        throw new InvalidOperationException(
          $"Debug-span mark {start} was measured against a different block than '{destBlock.Name}' " +
          $"({destBlock.Operations.Count} ops) in function '{destFunc.Name}'. A pass that replaces its " +
          "destination block mid-lowering must stamp and reset its marks at the switch.");
      }
    }

    for (int m = 0; m < marks.Count; m++) {
      int end = m + 1 < marks.Count ? marks[m + 1].Start : destBlock.Operations.Count;
      for (int i = marks[m].Start; i < end; i++) {
        destFunc.SetDebugSpan(destBlock.Operations[i], marks[m].Span);
      }
    }
  }
}
