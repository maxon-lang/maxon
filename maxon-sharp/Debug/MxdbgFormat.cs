using System.Buffers.Binary;

namespace MaxonSharp.Debug;

/// <summary>
/// The `.mxdbg` sidecar format — Maxon's own debug-info container (see docs/DEBUGGER_DESIGN.md).
///
/// Position-independent, little-endian, mmap-friendly: a fixed header carrying absolute
/// `(offset,count)` for each section, then arrays of fixed-width records, then one shared string
/// pool addressed by `(offset,len)`. No pointers — a reader maps the bytes and indexes them without
/// parsing or allocating.
///
/// THE FIELD WIDTH IS STATED ONCE. Every count, offset, and length in the file is a little-endian
/// <see cref="FieldSize"/>-byte word. The writer emits them and the reader consumes them through the
/// same two primitives (<see cref="Put"/> / <see cref="U32"/>), so the two ends cannot disagree about
/// stride — the failure the DebugStream name-blob format calls out (a `2` spelled at each end drifts
/// and desynchronises the whole parse). Everything past the string pool being variable-length, a
/// stride bug would not fail to load; it would slide the parse one word and turn every later record
/// into garbage.
///
/// P2 scope adds the type table, its field sub-table, the local-variable location table, and each
/// function's real frame size. The coverage-point table (P6) still has reserved header slots
/// (written as 0). The P2 additions change the header layout and grow the function record, so this is
/// an INCOMPATIBLE change and <see cref="FormatVersion"/> is bumped — a reader that speaks only the
/// P1 layout refuses the file rather than misreading the moved fields.
///
/// The compiler populates the type/field tables and each function's frame size. It does not yet
/// populate the local table (that binding of a local NAME to its source-level TYPE is erased by the
/// Standard dialect and needs a per-function side-table carried from the Maxon→Standard pass — the
/// P2b slice); every function therefore reports `localCount == 0` for now. The format, writer, reader
/// and self-test fully support the local records, so P2b lands the capture without another version
/// bump.
/// </summary>
public static class MxdbgFormat {
  /// 8 bytes so the header stays 4/8-byte aligned. A reader that does not see this refuses the file.
  public static readonly byte[] Magic = "MXDBG\0\0\0"u8.ToArray();

  /// Bumped only on an INCOMPATIBLE layout change. The driver refuses a version it does not speak
  /// rather than misread it — the "an instrument that lies is worse than none" rule the DebugStream
  /// handshake established. v2: type table + field sub-table + local table + per-function frame size.
  public const uint FormatVersion = 2;

  /// The one true width of every count/offset/length word in the file.
  public const int FieldSize = 4;

  /// The sidecar's file extension, appended to the binary's path: `foo.exe` -> `foo.exe.mxdbg`.
  public const string SidecarExtension = ".mxdbg";

  // Header layout (little-endian). HeaderSize is the offset of the first section.
  public const int OffMagic = 0;        // [8]
  public const int OffVersion = 8;      // u32
  public const int OffReserved = 12;    // u32 — 0 for now
  public const int OffBuildId = 16;     // u64 — FNV-1a of the binary's .text
  public const int OffTripleOff = 24;   // u32 } target triple, into the string pool
  public const int OffTripleLen = 28;   // u32 }
  public const int OffStringPoolOff = 32;
  public const int OffStringPoolSize = 36;
  public const int OffFileTableOff = 40;
  public const int OffFileCount = 44;
  public const int OffFuncTableOff = 48;
  public const int OffFuncCount = 52;
  public const int OffLineTableOff = 56;
  public const int OffLineCount = 60;
  public const int OffLocalTableOff = 64; // local-variable location table
  public const int OffLocalCount = 68;
  public const int OffTypeTableOff = 72;  // type table
  public const int OffTypeCount = 76;
  public const int OffCovTableOff = 80;   // P6 (0 until then)
  public const int OffCovCount = 84;
  public const int OffFieldTableOff = 88; // type-field sub-table (indexed by the type table)
  public const int OffFieldCount = 92;
  public const int HeaderSize = 96;       // padded to an 8-byte multiple

  // Fixed record strides.
  public const int FileEntrySize = 8;   // pathOff(4) pathLen(4)
  // nameOff nameLen codeStart codeEnd frameSize paramCount lineFirst lineCount localFirst localCount
  public const int FuncEntrySize = 40;
  public const int LineEntrySize = 20;  // codeOffset fileId line col flags
  public const int TypeEntrySize = 28;  // nameOff nameLen kind size align fieldFirst fieldCount
  public const int FieldEntrySize = 16; // nameOff nameLen offset typeId
  // nameOff nameLen locKind locValue typeId scopeStart scopeEnd. locValue is a SIGNED rbp-relative
  // offset for a stack slot (stored as its two's-complement u32); read it back through a cast.
  public const int LocalEntrySize = 28;

  // Line-entry flag bits.
  public const uint LineFlagStatement = 1 << 0; // a statement boundary (a valid step/breakpoint stop)
  public const uint LineFlagCoverage = 1 << 1;  // also a coverage point (P6)

  /// FNV-1a 64-bit over a byte span — the sidecar's build-id, matching the compiler-fingerprint
  /// content-hash convention. Binds a sidecar to exactly the `.text` it describes.
  public static ulong ComputeBuildId(ReadOnlySpan<byte> text) {
    const ulong offset = 14695981039346656037UL;
    const ulong prime = 1099511628211UL;
    ulong h = offset;
    foreach (byte b in text) {
      h ^= b;
      h *= prime;
    }
    return h;
  }

  /// Index of the first position in `[0, count)` for which <paramref name="inLeftPartition"/> is
  /// false — the classic partition point, for a monotone predicate that is true over a prefix and
  /// false thereafter. Returns <paramref name="count"/> when the predicate holds throughout.
  /// O(log count).
  ///
  /// Both boundary searches the debug builder needs over a sorted table — a function's `.text` end
  /// (first symbol offset past its start) and a function's line-row window (first row at/after each
  /// code bound) — reduce to this one primitive, so neither hand-rolls the loop. It replaces the
  /// linear scans that made function registration O(functions x symbols) and the line-span lookup
  /// O(functions x lines).
  internal static int PartitionPoint(int count, Func<int, bool> inLeftPartition) {
    int lo = 0;
    int hi = count;
    while (lo < hi) {
      int mid = (lo + hi) >>> 1;
      if (inLeftPartition(mid)) lo = mid + 1;
      else hi = mid;
    }
    return lo;
  }

  internal static void Put(List<byte> buf, uint value) {
    Span<byte> tmp = stackalloc byte[FieldSize];
    BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);
    buf.AddRange(tmp);
  }

  internal static uint U32(ReadOnlySpan<byte> bytes, int offset) =>
    BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, FieldSize));
}

/// <summary>
/// What a type-table entry describes, so the driver renders bytes as the right shape (a String's
/// fused record as text, an enum by discriminant, a struct as named fields). Serialized as the
/// <see cref="MxdbgFormat.FieldSize"/>-byte `kind` word of a type record.
/// </summary>
public enum MxdbgTypeKind : uint {
  /// A machine scalar with no source-level range (i8..u64, f32/f64, bool, void, cstring, a raw
  /// pointer). No fields.
  Primitive = 0,
  /// A ranged integer alias (`int(0 to 150)` / a named `typealias`). `size` is the optimal backing width.
  IntRanged = 1,
  /// A ranged float alias.
  FloatRanged = 2,
  /// A user struct. `fieldFirst`/`fieldCount` index the field sub-table; each field is a named,
  /// offset value of another type.
  Struct = 3,
  /// A plain or associated-value enum. Its "fields" are the CASES: field offset = the case ordinal,
  /// field type = the case's single payload type (or void).
  Enum = 4,
  /// An enum declared with the `union` keyword. Same field encoding as <see cref="Enum"/>.
  Union = 5,
  /// The fused heap String/Character record (conforms to BuiltinStringLiteral / BuiltinCharLiteral).
  String = 6,
  /// The fused heap Array/Vector record (conforms to BuiltinArrayLiteral).
  Array = 7,
  /// A heap-allocated record reached through an 8-byte pointer whose layout the sidecar does not
  /// further describe here (an interface value, a function value, an unresolved type parameter). It
  /// exists so a struct field or local of such a type has a valid `typeId` to point at.
  ManagedRecord = 8,
}

/// <summary>
/// Where a local lives at a given PC, serialized as the `locKind` word of a local record. The
/// accompanying `locValue` is interpreted per kind: a signed rbp-relative byte offset for
/// <see cref="StackSlotRbpRel"/>, a target register number for <see cref="Register"/>, and unused
/// (0) for <see cref="OptimizedOut"/>.
/// </summary>
public enum MxdbgLocKind : uint {
  StackSlotRbpRel = 0,
  Register = 1,
  OptimizedOut = 2,
}
