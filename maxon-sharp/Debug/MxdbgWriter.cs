using System.Text;

namespace MaxonSharp.Debug;

/// <summary>
/// Builds a `.mxdbg` byte image. The compiler feeds it interned strings, files, functions, types,
/// fields, locals, and line records at emit time (all read-only observations of the already-emitted
/// `.text`), then calls <see cref="Build"/>. Strictly additive — nothing here influences a single
/// emitted code byte.
///
/// Line records may be added in any order; <see cref="Build"/> sorts them by code offset so the
/// reader can binary-search. Types are added by the compiler in a deterministic (name-sorted) order,
/// so a type's index — the `typeId` a field or local points at — is its add order. Strings are
/// interned, so a name repeated across records costs its bytes once.
/// </summary>
public sealed class MxdbgWriter {
  private readonly List<byte> _stringPool = [];
  private readonly Dictionary<string, (uint off, uint len)> _interned = [];

  private readonly List<(uint pathOff, uint pathLen)> _files = [];
  private readonly List<FuncRec> _funcs = [];
  private readonly List<List<LocalRec>> _funcLocals = [];
  private readonly List<LineRec> _lines = [];
  private readonly List<TypeRec> _types = [];
  private readonly List<FieldRec> _fields = [];
  private readonly List<CovRec> _covPoints = [];

  private readonly record struct FuncRec(
    uint NameOff, uint NameLen, uint CodeStart, uint CodeEnd, uint FrameSize, uint ParamCount);

  private readonly record struct LineRec(uint CodeOffset, uint FileId, uint Line, uint Col, uint Flags);

  private readonly record struct TypeRec(
    uint NameOff, uint NameLen, uint Kind, uint Size, uint Align, uint FieldFirst, uint FieldCount);

  private readonly record struct FieldRec(uint NameOff, uint NameLen, uint Offset, uint TypeId);

  private readonly record struct LocalRec(
    uint NameOff, uint NameLen, uint LocKind, uint LocValue, uint TypeId, uint ScopeStart, uint ScopeEnd);

  private readonly record struct CovRec(
    uint CodeOffset, uint FileId, uint Line, uint Col, uint BranchLine, uint BranchCol,
    uint FuncNameOff, uint FuncNameLen, uint Flags);

  /// Intern a string into the shared pool, returning its `(offset,len)`. The empty string interns to
  /// `(0,0)` and writes nothing.
  public (uint off, uint len) Intern(string? s) {
    if (string.IsNullOrEmpty(s)) return (0, 0);
    if (_interned.TryGetValue(s, out var hit)) return hit;

    var bytes = Encoding.UTF8.GetBytes(s);
    var entry = ((uint)_stringPool.Count, (uint)bytes.Length);
    _stringPool.AddRange(bytes);
    _interned[s] = entry;
    return entry;
  }

  /// Register a source file; returns its file id (index into the file table), used by line records.
  public uint AddFile(string path) {
    var (off, len) = Intern(path);
    _files.Add((off, len));
    return (uint)(_files.Count - 1);
  }

  /// Register a function with its `.text` code-offset range and real emitted frame size. Returns the
  /// function id (index into the function table), used by <see cref="AddLocal"/>.
  public uint AddFunction(string name, uint codeStart, uint codeEnd, uint frameSize, uint paramCount) {
    var (off, len) = Intern(name);
    _funcs.Add(new FuncRec(off, len, codeStart, codeEnd, frameSize, paramCount));
    _funcLocals.Add([]);
    return (uint)(_funcs.Count - 1);
  }

  /// Register one line-table row: at <paramref name="codeOffset"/> in `.text`, execution is at
  /// <paramref name="fileId"/>:<paramref name="line"/>:<paramref name="col"/>.
  public void AddLine(uint codeOffset, uint fileId, uint line, uint col, uint flags) {
    _lines.Add(new LineRec(codeOffset, fileId, line, col, flags));
  }

  /// Register a type-table entry; returns its typeId (index into the type table), which fields and
  /// locals reference. <see cref="AddField"/> calls that follow attach to THIS type until the next
  /// <see cref="AddType"/>, so a caller adds a type and then its fields.
  public uint AddType(string name, MxdbgTypeKind kind, uint size, uint align) {
    var (off, len) = Intern(name);
    _types.Add(new TypeRec(off, len, (uint)kind, size, align, (uint)_fields.Count, 0));
    return (uint)(_types.Count - 1);
  }

  /// Append a field to the most recently added type. <paramref name="typeId"/> indexes the type table
  /// (the field's own type). For an enum/union the "field" is a case: <paramref name="offset"/> is the
  /// case ordinal and <paramref name="typeId"/> is the case's payload type (or void).
  public void AddField(string name, uint offset, uint typeId) {
    if (_types.Count == 0)
      throw new InvalidOperationException("AddField called before any AddType.");
    var (off, len) = Intern(name);
    _fields.Add(new FieldRec(off, len, offset, typeId));
    var t = _types[^1];
    _types[^1] = t with { FieldCount = t.FieldCount + 1 };
  }

  /// Record where a named local lives across `[scopeStart, scopeEnd)` code offsets.
  /// <paramref name="locValue"/> is a signed rbp-relative offset for a stack slot; it is stored as its
  /// two's-complement bit pattern. <paramref name="typeId"/> indexes the type table.
  public void AddLocal(uint funcIndex, string name, MxdbgLocKind locKind, int locValue, uint typeId,
      uint scopeStart, uint scopeEnd) {
    if (funcIndex >= _funcLocals.Count)
      throw new ArgumentOutOfRangeException(nameof(funcIndex));
    var (off, len) = Intern(name);
    _funcLocals[(int)funcIndex].Add(
      new LocalRec(off, len, (uint)locKind, unchecked((uint)locValue), typeId, scopeStart, scopeEnd));
  }

  /// <summary>
  /// Append one coverage point. Its COUNTER INDEX is its position in this table, so points must be
  /// added in counter order and every point must be added — including one the optimizer left with no
  /// code (<see cref="MxdbgFormat.CovFlagEliminated"/>, <paramref name="codeOffset"/> 0). Skipping
  /// one would slide every later record against the counter array it describes.
  /// </summary>
  public void AddCoveragePoint(uint codeOffset, uint fileId, uint line, uint col,
      uint branchLine, uint branchCol, string funcName, uint flags) {
    var (nameOff, nameLen) = Intern(funcName);
    _covPoints.Add(new CovRec(codeOffset, fileId, line, col, branchLine, branchCol, nameOff, nameLen, flags));
  }

  /// Serialize the accumulated tables into a `.mxdbg` image bound to <paramref name="buildId"/>.
  public byte[] Build(ulong buildId, string targetTriple) {
    var (tripleOff, tripleLen) = Intern(targetTriple);

    // The function table indexes into the SORTED line table, so sort first, then compute each
    // function's [lineFirst, lineFirst+lineCount) span. Ties on code offset keep insertion order
    // (a stable sort) so a statement and its coverage twin at the same PC do not reorder.
    var lines = _lines
      .Select((rec, i) => (rec, i))
      .OrderBy(t => t.rec.CodeOffset)
      .ThenBy(t => t.i)
      .Select(t => t.rec)
      .ToList();

    // Locals are laid out grouped by function, in function order, so each function's rows form a
    // contiguous [localFirst, localFirst+localCount) window addressable from its function record.
    var localFirst = new uint[_funcs.Count];
    var localCount = new uint[_funcs.Count];
    var locals = new List<LocalRec>();
    for (int i = 0; i < _funcs.Count; i++) {
      localFirst[i] = (uint)locals.Count;
      localCount[i] = (uint)_funcLocals[i].Count;
      locals.AddRange(_funcLocals[i]);
    }

    // Sections are laid out header → files → funcs → lines → types → fields → locals → string pool.
    // Offsets are absolute.
    uint fileTableOff = MxdbgFormat.HeaderSize;
    uint funcTableOff = fileTableOff + (uint)(_files.Count * MxdbgFormat.FileEntrySize);
    uint lineTableOff = funcTableOff + (uint)(_funcs.Count * MxdbgFormat.FuncEntrySize);
    uint typeTableOff = lineTableOff + (uint)(lines.Count * MxdbgFormat.LineEntrySize);
    uint fieldTableOff = typeTableOff + (uint)(_types.Count * MxdbgFormat.TypeEntrySize);
    uint localTableOff = fieldTableOff + (uint)(_fields.Count * MxdbgFormat.FieldEntrySize);
    uint covTableOff = localTableOff + (uint)(locals.Count * MxdbgFormat.LocalEntrySize);
    uint stringPoolOff = covTableOff + (uint)(_covPoints.Count * MxdbgFormat.CovEntrySize);

    var buf = new List<byte>((int)stringPoolOff + _stringPool.Count);

    WriteHeader(buf, buildId, tripleOff, tripleLen, stringPoolOff,
      fileTableOff, funcTableOff, lineTableOff, (uint)lines.Count,
      typeTableOff, fieldTableOff, localTableOff, (uint)locals.Count, covTableOff);

    foreach (var (pathOff, pathLen) in _files) {
      MxdbgFormat.Put(buf, pathOff);
      MxdbgFormat.Put(buf, pathLen);
    }

    for (int i = 0; i < _funcs.Count; i++) {
      var f = _funcs[i];
      var (lineFirst, lineCount) = FunctionLineSpan(lines, f);
      MxdbgFormat.Put(buf, f.NameOff);
      MxdbgFormat.Put(buf, f.NameLen);
      MxdbgFormat.Put(buf, f.CodeStart);
      MxdbgFormat.Put(buf, f.CodeEnd);
      MxdbgFormat.Put(buf, f.FrameSize);
      MxdbgFormat.Put(buf, f.ParamCount);
      MxdbgFormat.Put(buf, lineFirst);
      MxdbgFormat.Put(buf, lineCount);
      MxdbgFormat.Put(buf, localFirst[i]);
      MxdbgFormat.Put(buf, localCount[i]);
    }

    foreach (var l in lines) {
      MxdbgFormat.Put(buf, l.CodeOffset);
      MxdbgFormat.Put(buf, l.FileId);
      MxdbgFormat.Put(buf, l.Line);
      MxdbgFormat.Put(buf, l.Col);
      MxdbgFormat.Put(buf, l.Flags);
    }

    foreach (var t in _types) {
      MxdbgFormat.Put(buf, t.NameOff);
      MxdbgFormat.Put(buf, t.NameLen);
      MxdbgFormat.Put(buf, t.Kind);
      MxdbgFormat.Put(buf, t.Size);
      MxdbgFormat.Put(buf, t.Align);
      MxdbgFormat.Put(buf, t.FieldFirst);
      MxdbgFormat.Put(buf, t.FieldCount);
    }

    foreach (var fld in _fields) {
      MxdbgFormat.Put(buf, fld.NameOff);
      MxdbgFormat.Put(buf, fld.NameLen);
      MxdbgFormat.Put(buf, fld.Offset);
      MxdbgFormat.Put(buf, fld.TypeId);
    }

    foreach (var lc in locals) {
      MxdbgFormat.Put(buf, lc.NameOff);
      MxdbgFormat.Put(buf, lc.NameLen);
      MxdbgFormat.Put(buf, lc.LocKind);
      MxdbgFormat.Put(buf, lc.LocValue);
      MxdbgFormat.Put(buf, lc.TypeId);
      MxdbgFormat.Put(buf, lc.ScopeStart);
      MxdbgFormat.Put(buf, lc.ScopeEnd);
    }

    foreach (var cp in _covPoints) {
      MxdbgFormat.Put(buf, cp.CodeOffset);
      MxdbgFormat.Put(buf, cp.FileId);
      MxdbgFormat.Put(buf, cp.Line);
      MxdbgFormat.Put(buf, cp.Col);
      MxdbgFormat.Put(buf, cp.BranchLine);
      MxdbgFormat.Put(buf, cp.BranchCol);
      MxdbgFormat.Put(buf, cp.FuncNameOff);
      MxdbgFormat.Put(buf, cp.FuncNameLen);
      MxdbgFormat.Put(buf, cp.Flags);
    }

    buf.AddRange(_stringPool);
    return [.. buf];
  }

  /// The [first,count) window of sorted line rows whose code offset lies in this function's range.
  /// The line table is sorted by code offset, so a function's rows are the contiguous block
  /// `[lower(CodeStart), lower(CodeEnd))` — two binary searches, not a full scan of the table per
  /// function. That turns the whole line-span pass from O(functions x lines) into O(functions x log
  /// lines). An empty window reports first=0 to match the all-zero record the scan wrote.
  private static (uint first, uint count) FunctionLineSpan(List<LineRec> sortedLines, FuncRec f) {
    int n = sortedLines.Count;
    int lo = MxdbgFormat.PartitionPoint(n, i => sortedLines[i].CodeOffset < f.CodeStart);
    int hi = MxdbgFormat.PartitionPoint(n, i => sortedLines[i].CodeOffset < f.CodeEnd);
    return hi > lo ? ((uint)lo, (uint)(hi - lo)) : (0u, 0u);
  }

  private void WriteHeader(List<byte> buf, ulong buildId, uint tripleOff, uint tripleLen,
      uint stringPoolOff, uint fileTableOff, uint funcTableOff, uint lineTableOff, uint lineCount,
      uint typeTableOff, uint fieldTableOff, uint localTableOff, uint localCount, uint covTableOff) {
    // The header is fixed-size and written positionally; grow the buffer, then patch fields by offset.
    // Each field is one FieldSize-byte little-endian word, written through the shared primitive so the
    // stride is stated once (matching how the reader consumes them).
    buf.AddRange(new byte[MxdbgFormat.HeaderSize]);
    var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buf);

    MxdbgFormat.Magic.CopyTo(span[MxdbgFormat.OffMagic..]);
    Hdr(span, MxdbgFormat.OffVersion, MxdbgFormat.FormatVersion);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span[MxdbgFormat.OffBuildId..], buildId);
    Hdr(span, MxdbgFormat.OffTripleOff, tripleOff);
    Hdr(span, MxdbgFormat.OffTripleLen, tripleLen);
    Hdr(span, MxdbgFormat.OffStringPoolOff, stringPoolOff);
    Hdr(span, MxdbgFormat.OffStringPoolSize, (uint)_stringPool.Count);
    Hdr(span, MxdbgFormat.OffFileTableOff, fileTableOff);
    Hdr(span, MxdbgFormat.OffFileCount, (uint)_files.Count);
    Hdr(span, MxdbgFormat.OffFuncTableOff, funcTableOff);
    Hdr(span, MxdbgFormat.OffFuncCount, (uint)_funcs.Count);
    Hdr(span, MxdbgFormat.OffLineTableOff, lineTableOff);
    Hdr(span, MxdbgFormat.OffLineCount, lineCount);
    Hdr(span, MxdbgFormat.OffLocalTableOff, localTableOff);
    Hdr(span, MxdbgFormat.OffLocalCount, localCount);
    Hdr(span, MxdbgFormat.OffTypeTableOff, typeTableOff);
    Hdr(span, MxdbgFormat.OffTypeCount, (uint)_types.Count);
    Hdr(span, MxdbgFormat.OffFieldTableOff, fieldTableOff);
    Hdr(span, MxdbgFormat.OffFieldCount, (uint)_fields.Count);
    Hdr(span, MxdbgFormat.OffCovTableOff, covTableOff);
    Hdr(span, MxdbgFormat.OffCovCount, (uint)_covPoints.Count);
  }

  private static void Hdr(Span<byte> span, int at, uint value) =>
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[at..], value);
}
