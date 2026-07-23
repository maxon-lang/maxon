using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Debug;

/// <summary>
/// Accumulates debug-info observations while the code emitter runs, then hands the compiler a
/// populated <see cref="MxdbgWriter"/> to finalize with the build-id and write as a `.mxdbg` sidecar.
///
/// It is a PURE OBSERVER of the already-emitted `.text` (see docs/DEBUGGER_DESIGN.md): it reads code
/// offsets and per-op source spans that the pipeline captured on the side, and records file, function,
/// and line rows. Nothing here changes a single emitted byte, so a build that constructs one of these
/// and a build that does not are byte-identical.
/// </summary>
public sealed class DebugInfoBuilder {
  private readonly MxdbgWriter _writer = new();

  // File path -> file-table id, so a path repeated across functions/lines registers once.
  private readonly Dictionary<string, uint> _fileIds = [];

  // Per-function line cursor: the last source position emitted, so a line row is written only when
  // the position CHANGES. That yields one row per statement boundary — a minimal, monotonic table.
  private bool _haveLast;
  private uint _lastFileId;
  private uint _lastLine;
  private uint _lastCol;

  public MxdbgWriter Writer => _writer;

  /// Start a function's line rows. Resets the change-detection cursor so the function's first
  /// span-bearing op always emits a row, even if it repeats the previous function's last position
  /// (otherwise that function's line window would be empty and PC->line would miss it).
  public void BeginFunction() => _haveLast = false;

  /// Note the op about to be emitted at <paramref name="codeOffset"/>: if it carries a span, record a
  /// line row at its source position. One home for the emit-side capture, shared by both code emitters.
  public void NoteOp<TOp>(int codeOffset, IrFunction<TOp> func, TOp op) where TOp : IPrintableOp {
    if (func.TryGetDebugSpan(op, out var span)) {
      NoteLine(codeOffset, func.SourceFilePath, span.Line, span.Col);
    }
  }

  /// Register a function's `.text` range. frameSize is 0 until P2 (frame-relative locals live there).
  public void AddFunction(string name, int codeStart, int codeEnd, int paramCount) =>
    _writer.AddFunction(name, (uint)codeStart, (uint)codeEnd, frameSize: 0, (uint)paramCount);

  /// <summary>
  /// Register every real function's `.text` range. A function ends where the NEXT symbol (another
  /// function or a runtime helper) begins, so ranges stay tight even against the runtime helpers
  /// laid out between user functions. Shared by both code emitters — the only per-target inputs are
  /// the label-offset lookup and the emitted symbol list.
  /// </summary>
  public void RegisterFunctions<TOp>(IrModule<TOp> module, Func<string, int> labelOffset,
      IReadOnlyList<(string Name, int CodeOffset)> symbols, int codeLen) where TOp : IPrintableOp {
    var sortedOffsets = symbols.Select(e => e.CodeOffset).OrderBy(o => o).ToList();
    foreach (var func in module.Functions) {
      var start = labelOffset(func.Name);
      if (start < 0) continue;

      // A function ends where the NEXT symbol begins: the first offset strictly greater than `start`.
      // The offsets are sorted, so that boundary is a binary search, not a scan — the difference
      // between O(functions x symbols) and O(functions x log symbols).
      int idx = MxdbgFormat.PartitionPoint(sortedOffsets.Count, i => sortedOffsets[i] <= start);
      int end = idx < sortedOffsets.Count ? sortedOffsets[idx] : codeLen;
      AddFunction(func.Name, start, end, func.ParamTypes.Count);
    }
  }

  /// Record that execution at <paramref name="codeOffset"/> is at <paramref name="filePath"/>:line:col.
  /// A row is emitted only when the position differs from the previous op's. A null/empty file path
  /// (runtime helpers, synthetic functions with no source) records nothing.
  private void NoteLine(int codeOffset, string? filePath, int line, int col) {
    if (!TryFileId(filePath, out var fileId)) return;

    uint l = (uint)line;
    uint c = (uint)col;
    if (_haveLast && _lastFileId == fileId && _lastLine == l && _lastCol == c) return;

    _writer.AddLine((uint)codeOffset, fileId, l, c, MxdbgFormat.LineFlagStatement);
    _haveLast = true;
    _lastFileId = fileId;
    _lastLine = l;
    _lastCol = c;
  }

  private bool TryFileId(string? path, out uint fileId) {
    if (string.IsNullOrEmpty(path)) {
      fileId = 0;
      return false;
    }
    if (!_fileIds.TryGetValue(path, out fileId)) {
      fileId = _writer.AddFile(path);
      _fileIds[path] = fileId;
    }
    return true;
  }
}
