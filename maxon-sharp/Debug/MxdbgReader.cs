namespace MaxonSharp.Debug;

/// <summary>
/// Reads a `.mxdbg` image (see <see cref="MxdbgFormat"/>). Holds the raw bytes and indexes them in
/// place — the intended use is over an mmap, so nothing is copied out of the file beyond the strings
/// a caller actually asks for.
///
/// The driver validates <see cref="BuildId"/> against the binary's embedded `__build_id` before
/// trusting any lookup: a sidecar that does not describe THIS binary is refused, never used to print
/// wrong line numbers.
/// </summary>
public sealed class MxdbgReader {
  private readonly byte[] _b;

  public ulong BuildId { get; }
  public string Triple { get; }

  private readonly uint _stringPoolOff;
  private readonly uint _fileTableOff;
  private readonly uint _fileCount;
  private readonly uint _funcTableOff;
  private readonly uint _funcCount;
  private readonly uint _lineTableOff;
  private readonly uint _lineCount;

  public readonly record struct FuncInfo(
    string Name, uint CodeStart, uint CodeEnd, uint FrameSize, uint ParamCount,
    uint LineFirst, uint LineCount);

  public readonly record struct LineInfo(uint CodeOffset, string File, uint Line, uint Col, uint Flags);

  public MxdbgReader(byte[] bytes) {
    _b = bytes;

    if (_b.Length < MxdbgFormat.HeaderSize || !_b.AsSpan(0, MxdbgFormat.Magic.Length).SequenceEqual(MxdbgFormat.Magic))
      throw new InvalidDataException("Not a .mxdbg file (bad magic).");

    uint version = MxdbgFormat.U32(_b, MxdbgFormat.OffVersion);
    if (version != MxdbgFormat.FormatVersion)
      throw new InvalidDataException(
        $".mxdbg format version {version} is not supported (this build speaks {MxdbgFormat.FormatVersion}).");

    BuildId = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(_b.AsSpan(MxdbgFormat.OffBuildId));
    _stringPoolOff = MxdbgFormat.U32(_b, MxdbgFormat.OffStringPoolOff);
    _fileTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffFileTableOff);
    _fileCount = MxdbgFormat.U32(_b, MxdbgFormat.OffFileCount);
    _funcTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffFuncTableOff);
    _funcCount = MxdbgFormat.U32(_b, MxdbgFormat.OffFuncCount);
    _lineTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffLineTableOff);
    _lineCount = MxdbgFormat.U32(_b, MxdbgFormat.OffLineCount);

    Triple = Str(MxdbgFormat.U32(_b, MxdbgFormat.OffTripleOff), MxdbgFormat.U32(_b, MxdbgFormat.OffTripleLen));
  }

  public uint FileCount => _fileCount;
  public uint FunctionCount => _funcCount;
  public uint LineCount => _lineCount;

  /// Read a string-pool slice as UTF-8. `(0,0)` is the empty string.
  private string Str(uint off, uint len) =>
    len == 0 ? "" : System.Text.Encoding.UTF8.GetString(_b, (int)(_stringPoolOff + off), (int)len);

  public string FileName(uint fileId) {
    if (fileId >= _fileCount) throw new ArgumentOutOfRangeException(nameof(fileId));
    int rec = (int)(_fileTableOff + fileId * MxdbgFormat.FileEntrySize);
    return Str(MxdbgFormat.U32(_b, rec), MxdbgFormat.U32(_b, rec + MxdbgFormat.FieldSize));
  }

  public FuncInfo Function(uint index) {
    if (index >= _funcCount) throw new ArgumentOutOfRangeException(nameof(index));
    int rec = (int)(_funcTableOff + index * MxdbgFormat.FuncEntrySize);
    return new FuncInfo(
      Str(MxdbgFormat.U32(_b, rec), MxdbgFormat.U32(_b, rec + 4)),
      MxdbgFormat.U32(_b, rec + 8),
      MxdbgFormat.U32(_b, rec + 12),
      MxdbgFormat.U32(_b, rec + 16),
      MxdbgFormat.U32(_b, rec + 20),
      MxdbgFormat.U32(_b, rec + 24),
      MxdbgFormat.U32(_b, rec + 28));
  }

  public LineInfo Line(uint index) {
    if (index >= _lineCount) throw new ArgumentOutOfRangeException(nameof(index));
    int rec = (int)(_lineTableOff + index * MxdbgFormat.LineEntrySize);
    return new LineInfo(
      MxdbgFormat.U32(_b, rec),
      FileName(MxdbgFormat.U32(_b, rec + 4)),
      MxdbgFormat.U32(_b, rec + 8),
      MxdbgFormat.U32(_b, rec + 12),
      MxdbgFormat.U32(_b, rec + 16));
  }

  /// The function whose `.text` range contains <paramref name="codeOffset"/>, or null in a gap
  /// (padding, runtime helpers with no source).
  public FuncInfo? FunctionAt(uint codeOffset) {
    for (uint i = 0; i < _funcCount; i++) {
      var f = Function(i);
      if (codeOffset >= f.CodeStart && codeOffset < f.CodeEnd) return f;
    }
    return null;
  }

  /// Map a `.text` code offset to its source position: the greatest line row at or before the offset
  /// within the enclosing function. Null when no function/line covers it.
  public LineInfo? PcToLine(uint codeOffset) {
    var f = FunctionAt(codeOffset);
    if (f is not { } fn || fn.LineCount == 0) return null;

    // Binary search the function's contiguous [LineFirst, LineFirst+LineCount) window (the table is
    // sorted by code offset) for the greatest row whose CodeOffset <= codeOffset.
    uint lo = fn.LineFirst;
    uint hi = fn.LineFirst + fn.LineCount - 1;
    LineInfo? best = null;
    while (lo <= hi) {
      uint mid = lo + (hi - lo) / 2;
      var row = Line(mid);
      if (row.CodeOffset <= codeOffset) {
        best = row;
        if (mid == uint.MaxValue) break;
        lo = mid + 1;
      } else {
        if (mid == 0) break;
        hi = mid - 1;
      }
    }
    return best;
  }
}
