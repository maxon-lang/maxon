namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// One instrumented coverage point: a place in the SOURCE whose executions a `--coverage` build
/// counts. Minted by the parser, where the user's own control-flow structure is still visible, and
/// carried on the module through both lowerings to the emitter (see docs/DEBUGGER_DESIGN.md).
///
/// A point's identity is its INDEX in the owning <see cref="CoveragePointTable"/>. That index is the
/// counter slot the emitted code increments, the record index in the `.mxdbg` coverage table, and the
/// counter index in the `.mxcov` data file — one number, stated once, so the three cannot drift.
///
/// The FILE is stored here rather than resolved from the enclosing function, which is where every
/// other source position in this compiler gets it (see <see cref="SourceSpan"/>'s reasoning that a
/// function is single-file). The reason is lifetime: a point OUTLIVES its function. Dead-function
/// elimination removes a function nobody calls, and the report must still be able to say "these lines
/// have no code" — which it cannot do if the only route to their file is through a function that is
/// no longer in the module.
/// </summary>
public readonly record struct CoveragePoint(string FunctionName, string FilePath, int Line, int Col, uint Flags);

/// <summary>
/// An emitted machine op that IS a coverage point's counter increment. Both targets implement it, so
/// the debug-info builder — which sees ops only as <see cref="IPrintableOp"/> — can record where each
/// point's code landed without a per-target type test, and a new target cannot forget to be visible
/// to it without also failing to have a coverage op at all.
/// </summary>
public interface ICoveragePointOp {
  int PointId { get; }
}

/// <summary>
/// The module's coverage points, in mint order. Empty unless the compile was asked for `--coverage`;
/// nothing consults it otherwise, so a normal build carries an empty list and emits no instrumentation.
/// </summary>
public sealed class CoveragePointTable {
  private readonly List<CoveragePoint> _points = [];

  public int Count => _points.Count;

  public CoveragePoint this[int index] => _points[index];

  /// Allocate the next counter slot for a source position, returning its index — which the caller
  /// puts into the op that increments it.
  public int Mint(string functionName, string filePath, int line, int col, uint flags) {
    _points.Add(new CoveragePoint(functionName, filePath, line, col, flags));
    return _points.Count - 1;
  }
}
