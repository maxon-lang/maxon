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
/// function's real frame size. The P2 additions change the header layout and grow the function
/// record, so this is an INCOMPATIBLE change and <see cref="FormatVersion"/> is bumped — a reader
/// that speaks only the P1 layout refuses the file rather than misreading the moved fields.
///
/// P6 fills the reserved coverage-point slots. That section is written only by a `--coverage` build:
/// a coverage point is a COUNTER, and a counter that no instrumented code increments would be a table
/// describing a binary that cannot produce data for it.
///
/// The compiler populates the type/field tables, each function's frame size, AND (as of P2b) each
/// function's named locals. A local's source-level TYPE is erased by the Standard dialect (every store
/// carries only i64/f64/ptr), so it is captured in the Maxon→Standard pass, where the type is still
/// known, into a per-function side-table that the machine conversion carries forward and the emitter
/// joins with the register-allocator's name→slot map. Locals with a stable stack slot bind to that
/// slot; a value the optimizer kept only in registers (no stack home) has no record — honest, since
/// the sidecar would otherwise name a location it does not live at.
/// </summary>
public static class MxdbgFormat {
  /// 8 bytes so the header stays 4/8-byte aligned. A reader that does not see this refuses the file.
  public static readonly byte[] Magic = "MXDBG\0\0\0"u8.ToArray();

  /// Bumped on any change to what the file MEANS, not merely to where its bytes sit. The driver
  /// refuses a version it does not speak rather than misread it — the "an instrument that lies is
  /// worse than none" rule the DebugStream handshake established. v2: type table + field sub-table +
  /// local table + per-function frame size. v3: the coverage-point table.
  ///
  /// v3 is layout-compatible with v2 (every section is found through its own header slot, so a v2
  /// reader would still read a v3 file correctly) and is bumped anyway, because the CONTRACT changed:
  /// in v2 a zero <see cref="OffCovCount"/> meant "this format cannot say", and in v3 it means "this
  /// binary has no coverage points". A reader that cannot tell those apart would report an
  /// instrumented binary's coverage as empty, which is the one answer worse than refusing.
  public const uint FormatVersion = 3;

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
  public const int OffCovTableOff = 80;   // coverage-point table
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
  // codeOffset fileId line col branchLine branchCol funcNameOff funcNameLen flags. A point's COUNTER
  // INDEX is its record index, which is what binds this table to the `.mxcov` counter array
  // position-for-position.
  //
  // TWO positions, because a branch arm needs both and they are genuinely different. `line`/`col` is
  // the ARM's own source position — what LINE coverage counts, so `green gives 2` shows its own count
  // rather than reporting blank. `branchLine`/`branchCol` is the CONSTRUCT's, which is what groups
  // the arms of one `if` or one `match` into a single branch. Collapsing them (arms recorded at the
  // construct's position) is what made every `match` arm invisible to the line listing.
  // Zero for a point that is not an arm.
  //
  // The owning function is named by STRING, not by a funcId into the function table, because an
  // ELIMINATED point's function is not in that table at all (dead-function elimination removed it),
  // and a funcId would then have to be a sentinel that points at some unrelated function. Naming it
  // costs one interned string per function and can never mislabel.
  public const int CovEntrySize = 36;

  // Line-entry flag bits.
  public const uint LineFlagStatement = 1 << 0; // a statement boundary (a valid step/breakpoint stop)
  public const uint LineFlagCoverage = 1 << 1;  // a coverage point's counter increment starts here

  // Coverage-point flag bits. Kind and state are one word because they are one fact about a point:
  // WHAT the source construct is, and WHETHER the compiler emitted code for it.
  public const uint CovFlagStatement = 1 << 0;   // the head of a user statement
  public const uint CovFlagArmThen = 1 << 1;     // the TRUE arm of a user `if`
  public const uint CovFlagArmElse = 1 << 2;     // the FALSE arm of a user `if`
  // Set with CovFlagArmElse for the arm the source does not write (`if c then … end`). Instrumenting
  // it is the whole reason a coverage report can say "the false arm ran 4 times": the line table has
  // no row for an arm that has no source text, and never can.
  public const uint CovFlagArmImplicit = 1 << 3;
  // One arm of a user `match`. There is deliberately NO implicit counterpart: an `if` with no `else`
  // still takes a false edge, so that arm exists and is instrumented, whereas Maxon's `match` is
  // exhaustive and the fall-through past its last arm is unreachable. Instrumenting that would report
  // a permanently-untaken arm — a different lie. A flag that would always read the same value is not
  // a fact, it is the same fact written twice.
  public const uint CovFlagArmCase = 1 << 5;
  // No code was emitted for this point — the optimizer removed the code it anchored (today: a whole
  // function dead-function elimination dropped). Distinct from a zero counter, which means real code
  // that never ran. Set at emit time by observing which points reached `.text`.
  public const uint CovFlagEliminated = 1 << 4;

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
