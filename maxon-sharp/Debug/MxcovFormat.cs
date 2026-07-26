using System.Buffers.Binary;

namespace MaxonSharp.Debug;

/// <summary>
/// The `.mxcov` coverage-counter file a `--coverage` binary writes when it exits: a fixed 32-byte
/// header followed by one little-endian u64 per coverage point, in counter order.
///
/// THE FORMAT IS STATED ONCE, HERE. The bytes are produced by hand-emitted runtime code
/// (<c>RuntimeEmitter.EmitCoverageDump</c>) and consumed by <see cref="MxcovReader"/>; a second
/// spelling of any offset would be a number that can drift between the program that writes it and
/// the tool that reads it, with nothing in between to notice.
///
/// It carries the BUILD-ID of the `.text` it came from. That is what makes a stale data file a
/// refusal instead of a wrong report: counters are anonymous indices, so a `.mxcov` from a previous
/// build joined against today's point table would name real source lines with numbers belonging to
/// other lines — the "instrument that lies" failure the sidecar's own build-id binding exists to
/// prevent (docs/DEBUGGER_DESIGN.md).
/// </summary>
public static class MxcovFormat {
  /// 8 bytes so the header stays 8-byte aligned; a reader that does not see this refuses the file.
  public static readonly byte[] Magic = "MXCOV\0\0\0"u8.ToArray();

  public const uint FormatVersion = 1;

  /// The extension appended to the binary's own path: `foo.exe` -> `foo.exe.mxcov`.
  public const string DataExtension = ".mxcov";

  // Header layout (little-endian).
  //
  // ⚠ STATUS IS A WHOLE WORD, AND ALONE IN IT. It is the ONLY field the running program writes, and
  // the emitted runtime's store primitive is 64-bit — so a status packed beside a neighbour is a
  // store that silently overwrites the neighbour. It did: as a u32 at offset 12 it took out the low
  // half of the build-id below it, and every report then refused its own data file as "a different
  // build". Giving the one written field its own word makes that unrepresentable rather than
  // commented against.
  public const int OffMagic = 0;        // [8]
  public const int OffVersion = 8;      // u32
  public const int OffReserved = 12;    // u32 — 0
  public const int OffBuildId = 16;     // u64 — FNV-1a of the binary's .text
  public const int OffPointCount = 24;  // u32
  public const int OffReserved2 = 28;   // u32 — 0
  public const int OffStatus = 32;      // u64 — the only field the PROGRAM writes
  public const int HeaderSize = 40;

  /// One counter. u64 because a hot line in a long run must not wrap into a smaller, believable number.
  public const int CounterSize = 8;

  /// <summary>
  /// Where counter <paramref name="index"/> sits in the image — the ONE place a coverage point's
  /// index becomes a byte address.
  ///
  /// Three parties compute this and they must agree exactly or the report names the wrong lines: the
  /// x64 emitter's `lock inc` displacement, the arm64 emitter's `adrp/add` addend, and
  /// <see cref="MxcovReader"/> reading the file back — one WRITER per target and one READER, across a
  /// build boundary, with nothing between them that could notice a disagreement. Each spelling the
  /// arithmetic itself is the project's signature bug: a header that grew, or a counter array that
  /// gained alignment padding, would have to be found in three places, and the two that were found
  /// would go on agreeing with each other while the third quietly attributed every count to its
  /// neighbour — a report full of real numbers on the wrong lines.
  /// </summary>
  public static int CounterOffset(int index) => HeaderSize + index * CounterSize;

  /// The whole image's byte length: the block the compiler RESERVES and the block the program WRITES.
  /// Stated once so a reservation and a write cannot come to disagree about how long the file is —
  /// as the offset one past the last counter, so it cannot drift from <see cref="CounterOffset"/>
  /// either. The count is the compiler's own, so it is small and an `int` cannot overflow.
  public static int ImageSize(int pointCount) => CounterOffset(pointCount);

  /// The length a FILE CLAIMS to be, from the count in its own header — what a truncation check
  /// compares against. Widened deliberately: a hostile or corrupt `pointCount` near u32.max overflows
  /// an `int` length into a small positive number, and a check against that would pass a file holding
  /// almost no counters. It lives here, beside <see cref="ImageSize"/>, so the layout is still stated
  /// exactly once.
  public static long DeclaredImageSize(uint pointCount) => HeaderSize + (long)pointCount * CounterSize;

  /// Whether the run that produced the file reached its own exit. An ABORTED file holds every count
  /// up to the panic and is reported as PARTIAL — never presented as a finished measurement, because
  /// the lines after the fault never got their chance to run.
  public const long StatusCompleted = 1;
  public const long StatusAborted = 0;

  /// <summary>
  /// The header bytes the COMPILER stamps into the binary's counter image. Everything except the
  /// status word is known at build time, so the running program computes none of it — it stores one
  /// word and writes the block out. A header the program cannot compute is a header it cannot get
  /// wrong, and it is what binds the data file to the exact `.text` that produced it.
  /// </summary>
  public static byte[] BuildHeader(ulong buildId, int pointCount) {
    var header = new byte[HeaderSize];
    Magic.CopyTo(header, OffMagic);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OffVersion), FormatVersion);
    // A binary that is killed before it dumps leaves no file at all; a binary that dumps overwrites
    // this. Aborted is the honest starting value either way.
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(OffStatus), (ulong)StatusAborted);
    BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(OffBuildId), buildId);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(OffPointCount), (uint)pointCount);
    return header;
  }
}

/// <summary>
/// Reads a `.mxcov` image. Every rejection names what was wrong: a report built from a file this
/// could not fully validate would be worse than no report.
/// </summary>
public sealed class MxcovReader {
  public ulong BuildId { get; }
  public ulong Status { get; }
  public IReadOnlyList<ulong> Counters { get; }

  public bool RunCompleted => Status == (ulong)MxcovFormat.StatusCompleted;

  private MxcovReader(ulong buildId, ulong status, IReadOnlyList<ulong> counters) {
    BuildId = buildId;
    Status = status;
    Counters = counters;
  }

  /// Parse <paramref name="bytes"/>, or return null with <paramref name="error"/> naming the defect.
  public static MxcovReader? TryParse(ReadOnlySpan<byte> bytes, out string error) {
    if (bytes.Length < MxcovFormat.HeaderSize
        || !bytes[..MxcovFormat.Magic.Length].SequenceEqual(MxcovFormat.Magic)) {
      error = "not a .mxcov file (bad magic)";
      return null;
    }

    uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[MxcovFormat.OffVersion..]);
    if (version != MxcovFormat.FormatVersion) {
      error = $".mxcov format version {version} is not supported (this build speaks {MxcovFormat.FormatVersion})";
      return null;
    }

    ulong buildId = BinaryPrimitives.ReadUInt64LittleEndian(bytes[MxcovFormat.OffBuildId..]);
    ulong status = BinaryPrimitives.ReadUInt64LittleEndian(bytes[MxcovFormat.OffStatus..]);
    uint pointCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes[MxcovFormat.OffPointCount..]);

    long need = MxcovFormat.DeclaredImageSize(pointCount);
    if (bytes.Length < need) {
      error = $".mxcov is truncated: {pointCount} counters need {need} bytes, file has {bytes.Length}";
      return null;
    }

    var counters = new ulong[pointCount];
    for (int i = 0; i < counters.Length; i++) {
      counters[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[MxcovFormat.CounterOffset(i)..]);
    }

    error = "";
    return new MxcovReader(buildId, status, counters);
  }
}
