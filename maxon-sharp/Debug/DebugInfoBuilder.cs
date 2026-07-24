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

  /// Register a function's `.text` range and its real emitted frame size (bytes the prologue reserves
  /// below the frame pointer). frame-relative locals are placed against this frame.
  public void AddFunction(string name, int codeStart, int codeEnd, uint frameSize, int paramCount) =>
    _writer.AddFunction(name, (uint)codeStart, (uint)codeEnd, frameSize, (uint)paramCount);

  /// <summary>
  /// Register every real function's `.text` range and frame size. A function ends where the NEXT
  /// symbol (another function or a runtime helper) begins, so ranges stay tight even against the
  /// runtime helpers laid out between user functions. Shared by both code emitters — the per-target
  /// inputs are the label-offset lookup, the emitted symbol list, and the frame-size reader (each
  /// target names its own prologue op).
  /// </summary>
  public void RegisterFunctions<TOp>(IrModule<TOp> module, Func<string, int> labelOffset,
      IReadOnlyList<(string Name, int CodeOffset)> symbols, int codeLen,
      Func<IrFunction<TOp>, uint> frameSizeOf) where TOp : IPrintableOp {
    var sortedOffsets = symbols.Select(e => e.CodeOffset).OrderBy(o => o).ToList();
    foreach (var func in module.Functions) {
      var start = labelOffset(func.Name);
      if (start < 0) continue;

      // A function ends where the NEXT symbol begins: the first offset strictly greater than `start`.
      // The offsets are sorted, so that boundary is a binary search, not a scan — the difference
      // between O(functions x symbols) and O(functions x log symbols).
      int idx = MxdbgFormat.PartitionPoint(sortedOffsets.Count, i => sortedOffsets[i] <= start);
      int end = idx < sortedOffsets.Count ? sortedOffsets[idx] : codeLen;
      AddFunction(func.Name, start, end, frameSizeOf(func), func.ParamTypes.Count);
    }
  }

  // ---- Type table ----

  /// One renderable type collected during the closure, keyed in <see cref="RegisterTypes"/> by its
  /// canonical name. Field type references are held by NAME and resolved to a typeId only after the
  /// whole type set is sorted, so the id a field points at is stable regardless of walk order.
  private readonly record struct TypeDesc(
    MxdbgTypeKind Kind, uint Size, uint Align, List<(string Name, uint Offset, string TypeName)> Fields);

  /// <summary>
  /// Build the type table from the module's resolved type definitions. Structs render as named
  /// offset-typed fields, enums/unions as cases keyed by discriminant, ranged aliases carry their
  /// backing width, and any primitive or opaque type a field references is added so its `typeId` is
  /// valid. Pure observation — reads the same layout codegen used, writes no code byte.
  ///
  /// Deterministic: the type set is sorted by name before ids are assigned, so the file is identical
  /// across builds no matter what order the type dictionary enumerates in.
  /// </summary>
  public void RegisterTypes(IReadOnlyDictionary<string, IrType> typeDefs) {
    var descs = new Dictionary<string, TypeDesc>();

    // Seed the worklist with every renderable user type, then close over the field/case types they
    // reference (primitives, opaque heap types, and other user types).
    var work = new Queue<IrType>();
    foreach (var t in typeDefs.Values) {
      if (RenderKind(t) != null) work.Enqueue(t);
    }

    while (work.Count > 0) {
      var t = work.Dequeue();
      var name = CanonicalName(t);
      if (descs.ContainsKey(name)) continue;

      var desc = Describe(t);
      descs[name] = desc;
      foreach (var f in desc.Fields) {
        // A field's type is resolved to its canonical definition so a nominal reference lands on the
        // one entry for that type rather than a partially-specialised use-site copy.
        var ft = ResolveFieldType(f.TypeName, typeDefs);
        if (ft != null && !descs.ContainsKey(CanonicalName(ft))) work.Enqueue(ft);
      }
    }

    // Sort by name and assign each type its id (its index), then emit. Fields resolve their type name
    // to that id. A field naming a type that never made it into the set (should not happen — the
    // closure enqueues every referenced type) falls back to id 0 rather than crashing the sidecar.
    var names = descs.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    var nameToId = new Dictionary<string, uint>(names.Count);
    for (int i = 0; i < names.Count; i++) nameToId[names[i]] = (uint)i;

    foreach (var name in names) {
      var d = descs[name];
      _writer.AddType(name, d.Kind, d.Size, d.Align);
      foreach (var (fieldName, offset, fieldTypeName) in d.Fields)
        _writer.AddField(fieldName, offset, nameToId.GetValueOrDefault(fieldTypeName, 0u));
    }
  }

  /// The name a type is interned under. A type's name is its identity here; use-site copies of a
  /// struct/enum collapse onto one entry because a field reference is first resolved to the canonical
  /// definition (see <see cref="ResolveFieldType"/>) and then named through this.
  private static string CanonicalName(IrType t) => t.Name;

  /// The renderable kind of a type, or null for an opaque heap/pointer type (interface, function,
  /// type parameter, placeholder, backing marker, error union) that is described as a
  /// <see cref="MxdbgTypeKind.ManagedRecord"/> only when a field points at it.
  private static MxdbgTypeKind? RenderKind(IrType t) => t switch {
    IrRangedPrimitiveType r => r.IsFloatBased ? MxdbgTypeKind.FloatRanged : MxdbgTypeKind.IntRanged,
    IrStructType s when s.ConformingInterfaces.Contains("BuiltinStringLiteral")
      || s.ConformingInterfaces.Contains("BuiltinCharLiteral") => MxdbgTypeKind.String,
    IrStructType s when s.ConformingInterfaces.Contains("BuiltinArrayLiteral") => MxdbgTypeKind.Array,
    IrStructType => MxdbgTypeKind.Struct,
    IrEnumType e => e.IsUnion ? MxdbgTypeKind.Union : MxdbgTypeKind.Enum,
    _ when IsPrimitive(t) => MxdbgTypeKind.Primitive,
    _ => null,
  };

  private static bool IsPrimitive(IrType t) =>
    t == IrType.I8 || t == IrType.I16 || t == IrType.I32 || t == IrType.I64
    || t == IrType.U8 || t == IrType.U16 || t == IrType.U32 || t == IrType.U64
    || t == IrType.F32 || t == IrType.F64 || t == IrType.I1 || t == IrType.Void
    || t == IrType.CString || t == IrType.Fn;

  /// Build the serialized descriptor for a type. Sizes and field offsets are the very layout codegen
  /// emitted (<see cref="IrType.SizeInBytes"/>, <see cref="IrStructField.Offset"/>). A type parameter
  /// is never sized (its <c>SizeInBytes</c> throws), so an opaque type is a pointer width.
  private static TypeDesc Describe(IrType t) {
    var kind = RenderKind(t) ?? MxdbgTypeKind.ManagedRecord;
    var fields = new List<(string, uint, string)>();

    switch (t) {
      case IrStructType s:
        foreach (var f in s.Fields)
          fields.Add((f.Name, (uint)f.Offset, f.Type.Name));
        return new TypeDesc(kind, (uint)s.SizeInBytes, 8, fields);

      case IrEnumType e:
        // Cases render by discriminant: offset = ordinal, type = the case's single payload (or void).
        foreach (var c in e.Cases) {
          var payload = c.AssociatedValues is { Count: 1 } av ? av[0].Type : IrType.Void;
          fields.Add((c.Name, (uint)c.Ordinal, payload.Name));
        }
        return new TypeDesc(kind, (uint)e.SizeInBytes, 8, fields);

      case IrRangedPrimitiveType r:
        return new TypeDesc(kind, (uint)r.SizeInBytes, (uint)r.SizeInBytes, fields);

      case IrTypeParameterType:
        // Unsized — carry a pointer width rather than call SizeInBytes (which throws).
        return new TypeDesc(MxdbgTypeKind.ManagedRecord, 8, 8, fields);
    }

    if (IsPrimitive(t)) {
      uint size = (uint)t.SizeInBytes;
      uint align = size == 0 ? 1 : size;
      return new TypeDesc(MxdbgTypeKind.Primitive, size, align, fields);
    }

    // Opaque heap/pointer type (interface, function, placeholder, backing marker, error union):
    // an 8-byte reference with a layout the sidecar does not describe further here.
    return new TypeDesc(MxdbgTypeKind.ManagedRecord, 8, 8, fields);
  }

  /// Resolve a field's type NAME to the IrType to describe next: the canonical definition when the
  /// name is a user type, otherwise the primitive singleton of that name. Null for an unresolvable
  /// name (its field falls back to type id 0).
  private static IrType? ResolveFieldType(string name, IReadOnlyDictionary<string, IrType> typeDefs) {
    if (typeDefs.TryGetValue(name, out var def)) return def;
    return name switch {
      "i8" => IrType.I8, "i16" => IrType.I16, "i32" => IrType.I32, "i64" => IrType.I64,
      "u8" => IrType.U8, "u16" => IrType.U16, "u32" => IrType.U32, "u64" => IrType.U64,
      "f32" => IrType.F32, "f64" => IrType.F64, "i1" => IrType.I1, "void" => IrType.Void,
      "cstring" => IrType.CString, "fn" => IrType.Fn,
      _ => null,
    };
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
