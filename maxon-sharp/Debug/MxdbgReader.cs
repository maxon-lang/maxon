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
  private readonly uint _covTableOff;
  private readonly uint _covCount;

  public readonly record struct FuncInfo(
    string Name, uint CodeStart, uint CodeEnd, uint FrameSize, uint ParamCount,
    uint LineFirst, uint LineCount, uint LocalFirst, uint LocalCount);

  public readonly record struct LineInfo(uint CodeOffset, string File, uint Line, uint Col, uint Flags);

  public readonly record struct TypeInfo(
    string Name, MxdbgTypeKind Kind, uint Size, uint Align, uint FieldFirst, uint FieldCount);

  public readonly record struct FieldInfo(string Name, uint Offset, uint TypeId);

  public readonly record struct LocalInfo(
    string Name, MxdbgLocKind LocKind, int LocValue, uint TypeId, uint ScopeStart, uint ScopeEnd);

  /// One coverage point. Its COUNTER INDEX is its index in this table — the same number the emitted
  /// increment carries and the same slot of the `.mxcov` counter array.
  public readonly record struct CovPointInfo(
    uint CodeOffset, string File, uint Line, uint Col, string FunctionName, uint Flags) {
    /// No code was emitted for this point: the optimizer removed what it anchored. Distinct from a
    /// zero COUNT, which means real code that never ran.
    public bool Eliminated => (Flags & MxdbgFormat.CovFlagEliminated) != 0;
    public bool IsStatement => (Flags & MxdbgFormat.CovFlagStatement) != 0;
    public bool IsThenArm => (Flags & MxdbgFormat.CovFlagArmThen) != 0;
    public bool IsElseArm => (Flags & MxdbgFormat.CovFlagArmElse) != 0;
    /// The `else` the source never wrote — instrumented anyway, which is the whole point.
    public bool IsImplicitArm => (Flags & MxdbgFormat.CovFlagArmImplicit) != 0;
  }

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
    _covTableOff = MxdbgFormat.U32(_b, MxdbgFormat.OffCovTableOff);
    _covCount = MxdbgFormat.U32(_b, MxdbgFormat.OffCovCount);

    Triple = Str(MxdbgFormat.U32(_b, MxdbgFormat.OffTripleOff), MxdbgFormat.U32(_b, MxdbgFormat.OffTripleLen));
  }

  public uint FileCount => _fileCount;
  public uint FunctionCount => _funcCount;
  public uint LineCount => _lineCount;
  public uint TypeCount => _typeCount;
  public uint FieldCount => _fieldCount;
  public uint LocalCount => _localCount;

  /// How many coverage points this binary was instrumented with. Zero on any build that was not
  /// `--coverage`, which the driver reports as "this binary is not instrumented" rather than as an
  /// empty measurement.
  public uint CoveragePointCount => _covCount;

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

  public CovPointInfo CoveragePoint(uint index) {
    if (index >= _covCount) throw new ArgumentOutOfRangeException(nameof(index));
    int rec = (int)(_covTableOff + index * MxdbgFormat.CovEntrySize);
    return new CovPointInfo(
      MxdbgFormat.U32(_b, rec),
      FileName(MxdbgFormat.U32(_b, rec + 4)),
      MxdbgFormat.U32(_b, rec + 8),
      MxdbgFormat.U32(_b, rec + 12),
      Str(MxdbgFormat.U32(_b, rec + 16), MxdbgFormat.U32(_b, rec + 20)),
      MxdbgFormat.U32(_b, rec + 24));
  }

  /// The name of the type at <paramref name="typeId"/>, or "" when the id is out of range. Used to
  /// render a field's or local's type without the caller re-indexing the type table.
  public string TypeName(uint typeId) => typeId < _typeCount ? Type(typeId).Name : "";

  /// Function indices sorted by CodeStart, built once on first use. The function table is stored in
  /// module-emission order (only the LINE table is sorted at write time), so <see cref="FunctionAt"/>
  /// cannot binary-search it directly — this index gives it an O(log F) lookup instead of the O(F) scan
  /// that, called once per single-stepped instruction by the stepper's <see cref="PcToLine"/>, made a
  /// `step` O(N·F) (and worse: the old scan materialized every probed function, decoding a name string
  /// the range test never needed).
  private int[]? _funcOrderByCodeStart;

  /// A function's CodeStart/CodeEnd read WITHOUT decoding its name — the only fields the range search
  /// needs, so the O(log F) probe path allocates nothing.
  private uint FuncCodeStart(int index) =>
    MxdbgFormat.U32(_b, (int)(_funcTableOff + (uint)index * MxdbgFormat.FuncEntrySize) + 8);

  private uint FuncCodeEnd(int index) =>
    MxdbgFormat.U32(_b, (int)(_funcTableOff + (uint)index * MxdbgFormat.FuncEntrySize) + 12);

  private int[] FuncOrderByCodeStart() {
    if (_funcOrderByCodeStart is null) {
      var order = new int[_funcCount];
      for (int i = 0; i < _funcCount; i++) order[i] = i;
      Array.Sort(order, (a, b) => FuncCodeStart(a).CompareTo(FuncCodeStart(b)));
      _funcOrderByCodeStart = order;
    }
    return _funcOrderByCodeStart;
  }

  /// The function whose `.text` range contains <paramref name="codeOffset"/>, or null in a gap
  /// (padding, runtime helpers with no source). O(log F): the emitter's function ranges are disjoint,
  /// so the greatest CodeStart not past the offset is the ONLY candidate that can contain it — a
  /// containment miss there means the offset lies in a gap. Identical answers to a linear scan for the
  /// disjoint ranges the emitter produces.
  public FuncInfo? FunctionAt(uint codeOffset) {
    var order = FuncOrderByCodeStart();

    // Partition point: the first function that starts AFTER codeOffset; its predecessor holds the
    // greatest CodeStart <= codeOffset — the sole containment candidate for the emitter's disjoint
    // ranges. Routes through the ONE partition-point primitive the debug builder also uses, rather than
    // hand-rolling the loop a third time.
    int after = MxdbgFormat.PartitionPoint(order.Length, i => FuncCodeStart(order[i]) <= codeOffset);
    if (after == 0) return null;

    int cand = order[after - 1];
    return codeOffset >= FuncCodeStart(cand) && codeOffset < FuncCodeEnd(cand) ? Function((uint)cand) : null;
  }

  /// <summary>
  /// The smallest `.text` code offset whose line row is EXACTLY <paramref name="fileName"/>:<paramref
  /// name="line"/> — the address `break file:line` plants a breakpoint at. Null when the line carries
  /// no statement (a blank line, an `end`/brace line, a comment): the honest "no code at that line" the
  /// driver reports rather than a breakpoint at the wrong place. This is the inverse of
  /// <see cref="PcToLine"/> over the same line table, so the two never disagree.
  ///
  /// The file is matched by trailing path component (case-insensitively), so `break foo.maxon:N` finds
  /// a row the sidecar recorded under an absolute or differently-rooted path. The smallest matching
  /// offset is the statement's entry, past the prologue for any non-first statement.
  /// </summary>
  public uint? LineToOffset(string fileName, uint line) =>
    LineStartIndex().TryGetValue(LineKey(LeafPathComponent(fileName), line), out var offset) ? offset : null;

  /// `leaf:line` -> the smallest code offset with a row there, built once on first use.
  ///
  /// The line table is sorted by CODE OFFSET, not by line, so this direction cannot be binary-searched
  /// the way <see cref="PcToLine"/> is — it was a full scan that DECODED a file-name string per row,
  /// which is O(rows) per resolved line and quadratic across a set of them. Bounded in practice (the
  /// agent holds 16 breakpoints), but the same shape <see cref="FunctionAt"/>'s index already replaced
  /// on the stepping path, and built the same lazy way so a session that never resolves a line by
  /// number pays nothing.
  private Dictionary<string, uint>? _lineStartIndex;

  private Dictionary<string, uint> LineStartIndex() {
    if (_lineStartIndex is null) {
      // OrdinalIgnoreCase on the whole key is exactly the comparison the scan applied to the leaf: a
      // path leaf cannot contain ':', so no leaf+line pair can collide with another's.
      var index = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
      for (uint i = 0; i < _lineCount; i++) {
        var row = Line(i);
        var key = LineKey(LeafPathComponent(row.File), row.Line);
        if (!index.TryGetValue(key, out var best) || row.CodeOffset < best) index[key] = row.CodeOffset;
      }
      _lineStartIndex = index;
    }
    return _lineStartIndex;
  }

  private static string LineKey(string leaf, uint line) => $"{leaf}:{line}";

  /// The trailing path component, split on both separators so a Windows-rooted sidecar path and a
  /// forward-slash command-line spelling compare equal. Public and single-sourced here because the file
  /// completion pool (which offers `break <file>` leaves) MUST split a path the same way this reader
  /// resolves `break file:line` by leaf — a second copy could offer a leaf that then fails to resolve.
  public static string LeafPathComponent(string path) {
    int cut = path.LastIndexOfAny(['/', '\\']);
    return cut < 0 ? path : path[(cut + 1)..];
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
