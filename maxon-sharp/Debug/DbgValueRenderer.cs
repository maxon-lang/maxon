using System.Buffers.Binary;
using System.Text;

namespace MaxonSharp.Debug;

/// <summary>
/// One node of a rendered value tree (see docs/DEBUGGER_DESIGN.md, P4a). A scalar is a leaf with a
/// <see cref="Display"/> string and no children; a struct/enum is a node whose <see cref="Children"/>
/// are its fields/payload. <see cref="Truncated"/> marks a node whose subtree was cut at the expansion
/// depth (the value IS there in the debuggee — the renderer just stopped descending), so a surface can
/// show a "…" rather than implying the struct is empty.
/// </summary>
public sealed record DbgValue(
  string Name,
  string TypeName,
  MxdbgTypeKind Kind,
  string Display,
  IReadOnlyList<DbgValue> Children,
  bool Truncated);

/// <summary>
/// Turns a stopped debuggee's raw memory into type-aware value trees (P4a). It reads the SAME sidecar
/// tables the compiler wrote — the type table (<see cref="MxdbgReader.Type"/>), its field sub-table
/// (<see cref="MxdbgReader.Field"/>), and the local table (<see cref="MxdbgReader.Local"/>) — and a
/// <c>readMemory</c> delegate (the driver's chunked agent read) to fetch a value's bytes and interpret
/// them by kind. It is the FIRST consumer of the P2a/P2b sidecar type + local tables at runtime.
///
/// Storage model (confirmed against the bootstrap, not guessed):
///   * A scalar local (primitive / ranged int / ranged float / bool / simple enum) lives INLINE in its
///     stack slot — <c>addr = fp + locValue</c> is the value itself.
///   * A managed local (struct, String, Array, associated-value enum/union) is HEAP-allocated; its stack
///     slot holds an 8-byte POINTER to the record (see IrType.IsHeapAllocated and the zero-init store of
///     a managed local in MaxonToStandardConversion). So rendering such a kind first dereferences the
///     slot, then reads the record.
///   * A struct field is one 8-byte slot at <c>record + field.Offset</c>: a scalar field holds its value
///     inline there, a managed field holds a pointer (IrStructType.FieldSlotSize). The renderer treats
///     every <c>addr</c> uniformly as "the slot holding this value", so the same code walks locals and
///     fields.
///
/// The value ABIs (String's fused 48-byte record, the enum tag/payload record) are read off the offsets
/// the lowering writes, named here as constants that cite their source.
/// </summary>
internal sealed class DbgValueRenderer {
  private readonly MxdbgReader _sidecar;

  /// Reads <paramref name="len"/> bytes of the debuggee's memory at an address, or throws
  /// <see cref="DebuggerException"/> if the read cannot be satisfied (target exited / unsupported agent).
  /// The driver supplies the chunked, version-gated agent read behind it.
  private readonly Func<ulong, int, byte[]> _readMemory;

  public DbgValueRenderer(MxdbgReader sidecar, Func<ulong, int, byte[]> readMemory) {
    _sidecar = sidecar;
    _readMemory = readMemory;
  }

  // ---- ABI constants (cite their source; do not guess) ----

  // The fused String/Array record IS a __ManagedMemory: buffer@0, length@8, capacity@16, element_size@24,
  // parent_ptr@32 (MaxonToStandardConversion.Helpers.cs ManagedField* / MlirType.cs InlineManagedMemoryBytes).
  // A String adds isAsciiFlag@40 for a 48-byte record; the renderer needs only the first three fields.
  private const int ManagedBufferOffset = 0;
  private const int ManagedLengthOffset = 8;
  private const int ManagedHeaderReadBytes = 16; // buffer@0 + length@8 — the only two fields we interpret

  // An associated-value enum/union is a heap record: [tag:i64 @ 0, payload_0:i64 @ 8, ...]
  // (MaxonToStandardConversion.Enums.cs "Heap-allocate the enum: [tag @ 0, payload_0 @ 8, ...]").
  private const int EnumTagOffset = 0;
  private const int EnumFirstPayloadOffset = 8;

  /// The name the sidecar gives a void (empty) enum-case payload — an enum whose every case names this
  /// is a SIMPLE enum stored inline as an i64 ordinal, not a heap pointer (DebugInfoBuilder writes
  /// IrType.Void.Name for a payload-less case).
  private const string VoidTypeName = "void";

  /// The opaque marker a multi-value enum payload points at (DebugInfoBuilder.MultiPayloadTypeName): no
  /// single type to render, so the case shows its name but not an expanded payload.
  private const string MultiPayloadTypeName = "<multi-payload>";

  private const int PointerSize = 8;

  /// The display for a non-null aggregate (struct): a compact "see the fields" marker. The fields are
  /// the children; the record's heap address is deliberately not shown (it varies per run).
  private const string AggregateDisplay = "{…}";

  /// How deep a struct subtree auto-expands before a node is marked <see cref="DbgValue.Truncated"/>.
  /// Path navigation (<see cref="Evaluate"/>) renders its FINAL node with a fresh budget, so a requested
  /// path always resolves to full depth regardless of this bound.
  private const int MaxExpandDepth = 4;

  /// Cap on bytes fetched to display a String's text (and on a cstring scan), so a huge or corrupt
  /// length never drags the whole buffer across the agent one chunk at a time.
  private const int TextDisplayCap = 256;

  // ---- Public surface ----

  /// The stopped function's named stack-slot locals, each rendered as a value tree. Register-only and
  /// optimized-out locals (no stable stack home) carry no sidecar record and so do not appear — honest,
  /// per the sidecar's "capture what you can" rule.
  public IReadOnlyList<DbgValue> Locals(MaxonDebugger.StopInfo stop) {
    var results = new List<DbgValue>();
    if (_sidecar.FunctionAt((uint)stop.PcOffset) is not { } fn) return results;

    foreach (var loc in StackLocals(fn))
      results.Add(Render(loc.Name, SlotAddress(stop, loc.LocValue), loc.TypeId, 0));
    return results;
  }

  /// <summary>
  /// Resolve a dotted path (<c>person.home.name</c>) against the stopped frame and render the value it
  /// names. The head is a local; each further segment descends a struct field. An unknown local, a
  /// segment into a non-struct, a null intermediate, or a missing field yields an honest error node
  /// (never a guessed value).
  /// </summary>
  public DbgValue Evaluate(MaxonDebugger.StopInfo stop, string path) {
    var segments = path.Split('.', StringSplitOptions.TrimEntries);
    if (segments.Length == 0 || segments[0].Length == 0)
      return ErrorNode(path, "empty expression");

    if (_sidecar.FunctionAt((uint)stop.PcOffset) is not { } fn)
      return ErrorNode(path, "not stopped in a known function");

    if (!TryFindLocal(fn, segments[0], out var loc))
      return ErrorNode(path, $"no local named '{segments[0]}' here");

    ulong addr = SlotAddress(stop, loc.LocValue);
    uint typeId = loc.TypeId;

    for (int s = 1; s < segments.Length; s++) {
      if (!TryDescendField(ref addr, ref typeId, segments[s], out var err))
        return ErrorNode(path, err);
    }

    // Render the final node with a fresh depth budget so a navigated struct expands fully from here.
    return Render(segments[^1], addr, typeId, 0);
  }

  // ---- Rendering by kind ----

  private DbgValue Render(string name, ulong addr, uint typeId, int depth) {
    if (typeId >= _sidecar.TypeCount)
      return Leaf(name, "<unknown>", MxdbgTypeKind.Primitive, $"<unresolved type #{typeId}>");

    var t = _sidecar.Type(typeId);
    return t.Kind switch {
      MxdbgTypeKind.Primitive => RenderPrimitive(name, addr, t),
      MxdbgTypeKind.IntRanged => RenderScalar(name, addr, t),
      MxdbgTypeKind.FloatRanged => Leaf(name, t.Name, t.Kind, FormatFloat(addr, (int)t.Size)),
      MxdbgTypeKind.Struct => RenderStruct(name, addr, t, depth),
      MxdbgTypeKind.Enum or MxdbgTypeKind.Union => RenderEnum(name, addr, t, depth),
      MxdbgTypeKind.String => RenderString(name, addr, t),
      MxdbgTypeKind.Array => RenderArray(name, addr, t),
      MxdbgTypeKind.ManagedRecord => RenderManaged(name, addr, t),
      _ => throw new InvalidOperationException($"Unhandled type kind {t.Kind}"),
    };
  }

  /// The sidecar's spelling of `bool`: a 0/1 byte. It is the one integer Primitive the `i`/`u`
  /// signedness convention below does not describe, so it is named rather than spelled at each reader.
  public const string BoolPrimitiveName = "i1";

  /// <summary>
  /// How a sidecar type is read as a MACHINE INTEGER: how many bytes at the address, and whether those
  /// bytes SIGN-extend. False for anything that is not an integer or a bool.
  ///
  /// The ONE reading of a scalar's shape, and it has two consumers on opposite sides of the debugger: the
  /// renderer below, and the breakpoint-condition resolver that tells the in-process agent how to load
  /// the same local (<c>MaxonDebugger.TryScalarOperandShape</c>). Two copies would diverge into a wrong
  /// answer nothing reports — `print x` showing one number while `break … if x == that number` never
  /// fires — so both ask here.
  ///
  /// Signedness comes from the Primitive NAMING CONVENTION (`i&lt;bits&gt;` signed, `u&lt;bits&gt;`
  /// unsigned — DebugInfoBuilder writes the IrType names straight through), except the bool, which is
  /// read UNSIGNED; a ranged alias is a signed machine integer. The WIDTH is always the type record's own
  /// Size, so no per-name width table is restated to drift from the layout codegen actually emitted.
  /// </summary>
  public static bool TryScalarShape(MxdbgReader.TypeInfo t, out int width, out bool signed) {
    width = (int)t.Size;
    signed = false;

    if (t.Kind == MxdbgTypeKind.IntRanged) {
      signed = true;
      return true;
    }
    if (t.Kind != MxdbgTypeKind.Primitive) {
      width = 0;
      return false;
    }
    if (t.Name == BoolPrimitiveName) return true;
    if (t.Name.StartsWith('u')) return true;
    if (t.Name.StartsWith('i')) {
      signed = true;
      return true;
    }

    width = 0;
    return false;
  }

  /// A scalar integer or bool, read through <see cref="TryScalarShape"/> so the value shown and the value
  /// a breakpoint condition compares are read the same way. Bool prints true/false; every other integer
  /// prints decimal, signed or not per its shape.
  private DbgValue RenderScalar(string name, ulong addr, MxdbgReader.TypeInfo t) {
    if (!TryScalarShape(t, out int width, out bool signed))
      throw new InvalidOperationException($"'{t.Name}' is not a scalar integer or bool");

    string display =
      t.Name == BoolPrimitiveName ? (ReadUnsigned(addr, width) != 0 ? "true" : "false")
      : signed ? ReadSigned(addr, width).ToString()
      : ReadUnsigned(addr, width).ToString();
    return Leaf(name, t.Name, t.Kind, display);
  }

  /// A machine scalar, interpreted by its type NAME (the sidecar's Primitive kind covers i*/u*/f*/bool/
  /// void/cstring/fn). The integers and the bool go through the shared shape rule; the rest of the kind —
  /// void, the floats, cstring, fn — carries no width/signedness question to share.
  private DbgValue RenderPrimitive(string name, ulong addr, MxdbgReader.TypeInfo t) {
    if (TryScalarShape(t, out _, out _)) return RenderScalar(name, addr, t);

    int size = (int)t.Size;
    string display = t.Name switch {
      "void" => "void",
      "f32" or "f64" => FormatFloat(addr, size),
      "cstring" => FormatCString(addr),
      "fn" => $"fn@0x{ReadPointer(addr):x}",
      _ => FormatHex(addr, size),
    };
    return Leaf(name, t.Name, MxdbgTypeKind.Primitive, display);
  }

  private DbgValue RenderStruct(string name, ulong addr, MxdbgReader.TypeInfo t, int depth) {
    ulong record = ReadPointer(addr);
    if (record == 0) return Leaf(name, t.Name, MxdbgTypeKind.Struct, "null");

    // A struct's value IS its fields (the children); the heap address is left OUT of the display on
    // purpose — it varies per run (ASLR), which would make a committed transcript non-reproducible, and
    // it adds nothing the field subtree does not already show.
    if (depth >= MaxExpandDepth)
      return new DbgValue(name, t.Name, MxdbgTypeKind.Struct, AggregateDisplay, [], true);

    var children = new List<DbgValue>();
    foreach (var f in Fields(t))
      children.Add(Render(f.Name, record + f.Offset, f.TypeId, depth + 1));
    return new DbgValue(name, t.Name, MxdbgTypeKind.Struct, AggregateDisplay, children, false);
  }

  /// <summary>
  /// An enum/union rendered by discriminant. A SIMPLE enum (every case payload is void) is an inline
  /// i64 ordinal; an associated-value enum/union is a heap record whose tag is at offset 0 and whose
  /// single-value payload is at offset 8. The case is matched by ordinal (the sidecar records a case's
  /// ordinal as its field.Offset); an unmatched discriminant is shown honestly by its raw value rather
  /// than as a wrong case.
  /// </summary>
  private DbgValue RenderEnum(string name, ulong addr, MxdbgReader.TypeInfo t, int depth) {
    bool heap = EnumIsHeapAllocated(t);

    ulong record = 0;
    long tag;
    if (heap) {
      record = ReadPointer(addr);
      if (record == 0) return Leaf(name, t.Name, t.Kind, "null");
      tag = ReadSigned(record + EnumTagOffset, PointerSize);
    } else {
      tag = ReadSigned(addr, PointerSize);
    }

    if (!TryFindCase(t, tag, out var caseField))
      return Leaf(name, t.Name, t.Kind, $"{t.Name}(#{tag})");

    string display = $"{t.Name}.{caseField.Name}";
    var payloadTypeName = _sidecar.TypeName(caseField.TypeId);

    // Only a heap enum carries a payload record, and only a single-value payload has one type to render;
    // a multi-value payload points at the opaque marker (rendering it is a documented P4a residual).
    if (heap && payloadTypeName != VoidTypeName && payloadTypeName != MultiPayloadTypeName) {
      var payload = Render(caseField.Name, record + EnumFirstPayloadOffset, caseField.TypeId, depth + 1);
      return new DbgValue(name, t.Name, t.Kind, display, [payload], false);
    }
    return Leaf(name, t.Name, t.Kind, display);
  }

  private DbgValue RenderString(string name, ulong addr, MxdbgReader.TypeInfo t) {
    ulong record = ReadPointer(addr);
    if (record == 0) return Leaf(name, t.Name, MxdbgTypeKind.String, "null");

    var header = _readMemory(record, ManagedHeaderReadBytes);
    ulong buffer = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(ManagedBufferOffset));
    long length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(ManagedLengthOffset));
    if (length < 0) length = 0;

    int show = (int)Math.Min(length, TextDisplayCap);
    string text = show == 0 || buffer == 0 ? "" : Encoding.UTF8.GetString(_readMemory(buffer, show));
    string ellipsis = length > show ? "…" : "";
    return Leaf(name, t.Name, MxdbgTypeKind.String, $"\"{text}{ellipsis}\" (len={length})");
  }

  private DbgValue RenderArray(string name, ulong addr, MxdbgReader.TypeInfo t) {
    ulong record = ReadPointer(addr);
    if (record == 0) return Leaf(name, t.Name, MxdbgTypeKind.Array, "null");

    var header = _readMemory(record, ManagedHeaderReadBytes);
    long length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(ManagedLengthOffset));
    // Typed per-element expansion is a P4a residual: the Array type entry carries no element typeId.
    return Leaf(name, t.Name, MxdbgTypeKind.Array, $"Array(len={length})");
  }

  private DbgValue RenderManaged(string name, ulong addr, MxdbgReader.TypeInfo t) {
    ulong ptr = ReadPointer(addr);
    string display = ptr == 0 ? "null" : $"<{t.Name}>@0x{ptr:x}";
    return Leaf(name, t.Name, MxdbgTypeKind.ManagedRecord, display);
  }

  // ---- Navigation helpers ----

  private bool TryFindLocal(MxdbgReader.FuncInfo fn, string localName, out MxdbgReader.LocalInfo found) {
    foreach (var loc in StackLocals(fn)) {
      if (loc.Name == localName) {
        found = loc;
        return true;
      }
    }
    found = default;
    return false;
  }

  /// Descend one struct-field segment: dereference the current struct record, find the named field, and
  /// advance (addr, typeId) to that field's slot. Only a struct has navigable named fields; a segment
  /// into any other kind is an honest error.
  private bool TryDescendField(ref ulong addr, ref uint typeId, string segment, out string error) {
    if (typeId >= _sidecar.TypeCount) {
      error = $"cannot navigate into '{segment}': unresolved type";
      return false;
    }
    var t = _sidecar.Type(typeId);
    if (t.Kind != MxdbgTypeKind.Struct) {
      error = $"cannot navigate into '{segment}': {t.Name} is not a struct";
      return false;
    }

    ulong record = ReadPointer(addr);
    if (record == 0) {
      error = $"cannot navigate into '{segment}': {t.Name} is null";
      return false;
    }

    foreach (var f in Fields(t)) {
      if (f.Name == segment) {
        addr = record + f.Offset;
        typeId = f.TypeId;
        error = "";
        return true;
      }
    }

    error = $"no field '{segment}' in {t.Name}";
    return false;
  }

  private bool EnumIsHeapAllocated(MxdbgReader.TypeInfo t) {
    foreach (var f in Fields(t))
      if (_sidecar.TypeName(f.TypeId) != VoidTypeName) return true;
    return false;
  }

  private bool TryFindCase(MxdbgReader.TypeInfo t, long discriminant, out MxdbgReader.FieldInfo found) {
    foreach (var f in Fields(t)) {
      if (f.Offset == discriminant) {
        found = f;
        return true;
      }
    }
    found = default;
    return false;
  }

  // ---- Table-window scans (ONE definition of each window; the [First, First+Count) bound is stated
  //      once so a future edit cannot drift one copy's bound past the others') ----

  /// The fields (struct fields, or enum/union cases) of a type, in table order. The single home for the
  /// field sub-table window `[FieldFirst, FieldFirst+FieldCount)`.
  private IEnumerable<MxdbgReader.FieldInfo> Fields(MxdbgReader.TypeInfo t) {
    for (uint fi = t.FieldFirst; fi < t.FieldFirst + t.FieldCount; fi++)
      yield return _sidecar.Field(fi);
  }

  /// The function's named locals that have a frame-pointer-relative stack home — the only ones a value
  /// tree can be read against. The single home for both the local window `[LocalFirst, LocalFirst+
  /// LocalCount)` and the stack-slot filter, so <see cref="Locals"/> and <see cref="TryFindLocal"/>
  /// cannot disagree on which locals are inspectable.
  private IEnumerable<MxdbgReader.LocalInfo> StackLocals(MxdbgReader.FuncInfo fn) {
    for (uint i = fn.LocalFirst; i < fn.LocalFirst + fn.LocalCount; i++) {
      var loc = _sidecar.Local(i);
      if (loc.LocKind == MxdbgLocKind.StackSlotRbpRel) yield return loc;
    }
  }

  // ---- Byte-level readers ----

  private static ulong SlotAddress(MaxonDebugger.StopInfo stop, int rbpRelativeOffset) =>
    (ulong)(stop.Fp + rbpRelativeOffset);

  private ulong ReadPointer(ulong addr) =>
    BinaryPrimitives.ReadUInt64LittleEndian(_readMemory(addr, PointerSize));

  private long ReadSigned(ulong addr, int size) {
    var b = _readMemory(addr, size);
    long v = 0;
    for (int i = 0; i < size; i++) v |= (long)b[i] << (8 * i);
    int bits = size * 8;
    if (bits < 64 && (v & (1L << (bits - 1))) != 0) v |= -(1L << bits); // sign-extend
    return v;
  }

  private ulong ReadUnsigned(ulong addr, int size) {
    var b = _readMemory(addr, size);
    ulong v = 0;
    for (int i = 0; i < size; i++) v |= (ulong)b[i] << (8 * i);
    return v;
  }

  private string FormatFloat(ulong addr, int size) {
    var b = _readMemory(addr, size);
    return size == 4
      ? BinaryPrimitives.ReadSingleLittleEndian(b).ToString("R")
      : BinaryPrimitives.ReadDoubleLittleEndian(b).ToString("R");
  }

  private string FormatHex(ulong addr, int size) {
    if (size <= 0) return "0x0";
    var b = _readMemory(addr, size);
    var sb = new StringBuilder("0x");
    for (int i = size - 1; i >= 0; i--) sb.Append(b[i].ToString("x2"));
    return sb.ToString();
  }

  private string FormatCString(ulong addr) {
    ulong ptr = ReadPointer(addr);
    if (ptr == 0) return "null";
    var bytes = _readMemory(ptr, TextDisplayCap);
    int nul = Array.IndexOf(bytes, (byte)0);
    int len = nul < 0 ? bytes.Length : nul;
    return $"\"{Encoding.UTF8.GetString(bytes, 0, len)}\"";
  }

  // ---- Node builders ----

  private static DbgValue Leaf(string name, string typeName, MxdbgTypeKind kind, string display) =>
    new(name, typeName, kind, display, [], false);

  /// An honest failure node — an unknown local, a bad path, a navigation into a non-struct. It renders
  /// like any value (name + a bracketed reason) so a surface never has to special-case "could not
  /// resolve" and never shows a guessed value in its place.
  private static DbgValue ErrorNode(string name, string reason) =>
    new(name, "", MxdbgTypeKind.Primitive, $"<{reason}>", [], false);
}
