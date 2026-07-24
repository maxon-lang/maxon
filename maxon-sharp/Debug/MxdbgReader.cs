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
  private readonly uint _typeTableOff;
  private readonly uint _typeCount;
  private readonly uint _fieldTableOff;
  private readonly uint _fieldCount;
  private readonly uint _localTableOff;
  private readonly uint _localCount;

  public readonly record struct FuncInfo(
    string Name, uint CodeStart, uint CodeEnd, uint FrameSize, uint ParamCount,
    uint LineFirst, uint LineCount, uint LocalFirst, uint LocalCount);

  public readonly record struct LineInfo(uint CodeOffset, string File, uint Line, uint Col, uint Flags);

  public readonly record struct TypeInfo(
    string Name, MxdbgTypeKind Kind, uint Size, uint Align, uint FieldFirst, uint FieldCount);

  public readonly record struct FieldInfo(string Name, uint Offset, uint TypeId);

  public readonly record struct LocalInfo(
    string Name, MxdbgLocKind LocKind, int LocValue, uint TypeId, uint ScopeStart, uint ScopeEnd);

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
    _typeTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffTypeTableOff);
    _typeCount = MxdbgFormat.U32(_b, MxdbgFormat.OffTypeCount);
    _fieldTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffFieldTableOff);
    _fieldCount = MxdbgFormat.U32(_b, MxdbgFormat.OffFieldCount);
    _localTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffLocalTableOff);
    _localCount = MxdbgFormat.U32(_b, MxdbgFormat.OffLocalCount);

    Triple = Str(MxdbgFormat.U32(_b, MxdbgFormat.OffTripleOff), MxdbgFormat.U32(_b, MxdbgFormat.OffTripleLen));
  }

  public uint FileCount => _fileCount;
  public uint FunctionCount => _funcCount;
  public uint LineCount => _lineCount;
  public uint TypeCount => _typeCount;
  public uint FieldCount => _fieldCount;
  public uint LocalCount => _localCount;

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
      MxdbgFormat.U32(_b, rec + 28),
      MxdbgFormat.U32(_b, rec + 32),
      MxdbgFormat.U32(_b, rec + 36));
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

  public TypeInfo Type(uint index) {
    if (index >= _typeCount) throw new ArgumentOutOfRangeException(nameof(index));
    int rec = (int)(_typeTableOff + index * MxdbgFormat.TypeEntrySize);
    return new TypeInfo(
      Str(MxdbgFormat.U32(_b, rec), MxdbgFormat.U32(_b, rec + 4)),
      (MxdbgTypeKind)MxdbgFormat.U32(_b, rec + 8),
      MxdbgFormat.U32(_b, rec + 12),
      MxdbgFormat.U32(_b, rec + 16),
      MxdbgFormat.U32(_b, rec + 20),
      MxdbgFormat.U32(_b, rec + 24));
  }

  public FieldInfo Field(uint index) {
    if (index >= _fieldCount) throw new ArgumentOutOfRangeException(nameof(index));
    int rec = (int)(_fieldTableOff + index * MxdbgFormat.FieldEntrySize);
    return new FieldInfo(
      Str(MxdbgFormat.U32(_b, rec), MxdbgFormat.U32(_b, rec + 4)),
      MxdbgFormat.U32(_b, rec + 8),
      MxdbgFormat.U32(_b, rec + 12));
  }

  public LocalInfo Local(uint index) {
    if (index >= _localCount) throw new ArgumentOutOfRangeException(nameof(index));
    int rec = (int)(_localTableOff + index * MxdbgFormat.LocalEntrySize);
    return new LocalInfo(
      Str(MxdbgFormat.U32(_b, rec), MxdbgFormat.U32(_b, rec + 4)),
      (MxdbgLocKind)MxdbgFormat.U32(_b, rec + 8),
      unchecked((int)MxdbgFormat.U32(_b, rec + 12)),
      MxdbgFormat.U32(_b, rec + 16),
      MxdbgFormat.U32(_b, rec + 20),
      MxdbgFormat.U32(_b, rec + 24));
  }

  /// The name of the type at <paramref name="typeId"/>, or "" when the id is out of range. Used to
  /// render a field's or local's type without the caller re-indexing the type table.
  public string TypeName(uint typeId) => typeId < _typeCount ? Type(typeId).Name : "";

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
