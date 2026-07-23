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
/// This is P1 scope: header + string pool + file/function/line tables + build-id. Local-variable
/// location lists (P2), the type table (P2), and the coverage-point table (P6) have reserved header
/// slots (written as 0) and are added without a format-version bump only if their records append.
/// </summary>
public static class MxdbgFormat {
  /// 8 bytes so the header stays 4/8-byte aligned. A reader that does not see this refuses the file.
  public static readonly byte[] Magic = "MXDBG\0\0\0"u8.ToArray();

  /// Bumped only on an INCOMPATIBLE layout change. The driver refuses a version it does not speak
  /// rather than misread it — the "an instrument that lies is worse than none" rule the DebugStream
  /// handshake established.
  public const uint FormatVersion = 1;

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
  public const int OffLocalTableOff = 64; // P2 (0 until then)
  public const int OffLocalCount = 68;
  public const int OffTypeTableOff = 72;  // P2
  public const int OffTypeCount = 76;
  public const int OffCovTableOff = 80;   // P6
  public const int OffCovCount = 84;
  public const int HeaderSize = 96;       // padded to an 8-byte multiple

  // Fixed record strides.
  public const int FileEntrySize = 8;   // pathOff(4) pathLen(4)
  public const int FuncEntrySize = 32;  // nameOff nameLen codeStart codeEnd frameSize paramCount lineFirst lineCount
  public const int LineEntrySize = 20;  // codeOffset fileId line col flags

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
