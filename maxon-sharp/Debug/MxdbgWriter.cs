using System.Text;

namespace MaxonSharp.Debug;

/// <summary>
/// Builds a `.mxdbg` byte image. The compiler feeds it interned strings, files, functions, and line
/// records at emit time (all read-only observations of the already-emitted `.text`), then calls
/// <see cref="Build"/>. Strictly additive — nothing here influences a single emitted code byte.
///
/// Line records may be added in any order; <see cref="Build"/> sorts them by code offset so the
/// reader can binary-search. Strings are interned, so a name repeated across records costs its bytes
/// once.
/// </summary>
public sealed class MxdbgWriter {
  private readonly List<byte> _stringPool = [];
  private readonly Dictionary<string, (uint off, uint len)> _interned = [];

  private readonly List<(uint pathOff, uint pathLen)> _files = [];
  private readonly List<FuncRec> _funcs = [];
  private readonly List<LineRec> _lines = [];

  private readonly record struct FuncRec(
    uint NameOff, uint NameLen, uint CodeStart, uint CodeEnd, uint FrameSize, uint ParamCount);

  private readonly record struct LineRec(uint CodeOffset, uint FileId, uint Line, uint Col, uint Flags);

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

  /// Register a function with its `.text` code-offset range.
  public void AddFunction(string name, uint codeStart, uint codeEnd, uint frameSize, uint paramCount) {
    var (off, len) = Intern(name);
    _funcs.Add(new FuncRec(off, len, codeStart, codeEnd, frameSize, paramCount));
  }

  /// Register one line-table row: at <paramref name="codeOffset"/> in `.text`, execution is at
  /// <paramref name="fileId"/>:<paramref name="line"/>:<paramref name="col"/>.
  public void AddLine(uint codeOffset, uint fileId, uint line, uint col, uint flags) {
    _lines.Add(new LineRec(codeOffset, fileId, line, col, flags));
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

    // Sections are laid out header → files → funcs → lines → string pool. Offsets are absolute.
    uint fileTableOff = HeaderEnd();
    uint funcTableOff = fileTableOff + (uint)(_files.Count * MxdbgFormat.FileEntrySize);
    uint lineTableOff = funcTableOff + (uint)(_funcs.Count * MxdbgFormat.FuncEntrySize);
    uint stringPoolOff = lineTableOff + (uint)(lines.Count * MxdbgFormat.LineEntrySize);

    var buf = new List<byte>((int)stringPoolOff + _stringPool.Count);

    WriteHeader(buf, buildId, tripleOff, tripleLen,
      stringPoolOff, fileTableOff, funcTableOff, lineTableOff, (uint)lines.Count);

    foreach (var (pathOff, pathLen) in _files) {
      MxdbgFormat.Put(buf, pathOff);
      MxdbgFormat.Put(buf, pathLen);
    }

    foreach (var f in _funcs) {
      var (lineFirst, lineCount) = FunctionLineSpan(lines, f);
      MxdbgFormat.Put(buf, f.NameOff);
      MxdbgFormat.Put(buf, f.NameLen);
      MxdbgFormat.Put(buf, f.CodeStart);
      MxdbgFormat.Put(buf, f.CodeEnd);
      MxdbgFormat.Put(buf, f.FrameSize);
      MxdbgFormat.Put(buf, f.ParamCount);
      MxdbgFormat.Put(buf, lineFirst);
      MxdbgFormat.Put(buf, lineCount);
    }

    foreach (var l in lines) {
      MxdbgFormat.Put(buf, l.CodeOffset);
      MxdbgFormat.Put(buf, l.FileId);
      MxdbgFormat.Put(buf, l.Line);
      MxdbgFormat.Put(buf, l.Col);
      MxdbgFormat.Put(buf, l.Flags);
    }

    buf.AddRange(_stringPool);
    return [.. buf];
  }

  private static uint HeaderEnd() => MxdbgFormat.HeaderSize;

  /// The [first,count) window of sorted line rows whose code offset lies in this function's range.
  /// The line table is sorted, so a function's rows are contiguous — one linear pass finds the window.
  private static (uint first, uint count) FunctionLineSpan(List<LineRec> sortedLines, FuncRec f) {
    uint first = 0;
    uint count = 0;
    bool started = false;
    for (int i = 0; i < sortedLines.Count; i++) {
      var off = sortedLines[i].CodeOffset;
      if (off >= f.CodeStart && off < f.CodeEnd) {
        if (!started) {
          first = (uint)i;
          started = true;
        }
        count++;
      }
    }
    return (first, count);
  }

  private void WriteHeader(List<byte> buf, ulong buildId, uint tripleOff, uint tripleLen,
      uint stringPoolOff, uint fileTableOff, uint funcTableOff, uint lineTableOff, uint lineCount) {
    // The header is fixed-size and written positionally; grow the buffer, then patch fields by offset.
    buf.AddRange(new byte[MxdbgFormat.HeaderSize]);
    var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buf);

    MxdbgFormat.Magic.CopyTo(span[MxdbgFormat.OffMagic..]);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffVersion..], MxdbgFormat.FormatVersion);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(span[MxdbgFormat.OffBuildId..], buildId);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffTripleOff..], tripleOff);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffTripleLen..], tripleLen);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffStringPoolOff..], stringPoolOff);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffStringPoolSize..], (uint)_stringPool.Count);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffFileTableOff..], fileTableOff);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffFileCount..], (uint)_files.Count);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffFuncTableOff..], funcTableOff);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffFuncCount..], (uint)_funcs.Count);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffLineTableOff..], lineTableOff);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[MxdbgFormat.OffLineCount..], lineCount);
    // Local/type/coverage section slots stay 0 until P2/P6.
  }
}
