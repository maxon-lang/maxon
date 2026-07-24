using System.IO.MemoryMappedFiles;

namespace MaxonSharp;

/// <summary>
/// A named shared-memory segment the CONSUMER creates and a Maxon process maps — the substrate
/// under both `maxon monitor` (the DebugStream ring) and the P3a debug-agent attach probe. The
/// consumer creates it, seeds its header, and hands the target its <see cref="SegmentName"/> via an
/// env var; the target opens it by that name (OpenFileMappingA / open) and maps it.
///
/// The two platforms name a shared mapping differently, and .NET implements only one of them.
/// <c>MemoryMappedFile.CreateNew(name, ...)</c> creates a Win32 SECTION OBJECT — a Windows concept —
/// and everywhere else it throws `PlatformNotSupportedException: Named maps are not supported`. The
/// monitor died on that line before it ever spawned the child, so every mm-trace test on macOS saw
/// an EMPTY TRACE and read it as "the program allocated nothing".
///
/// So off Windows the segment is a plain temp FILE mapped MAP_SHARED, and the env var carries its
/// PATH instead of a name. That costs the producer exactly one token — `open(path, O_RDWR)` where
/// Windows opens a section by name — because file-backed MAP_SHARED pages are shared between
/// processes in precisely the way a named segment's are. It deliberately does NOT use POSIX
/// `shm_open`: that is variadic, and on Apple arm64 a variadic call made through the fixed-register
/// path silently passes garbage for `mode`, creating the object with mode 0 and failing every
/// subsequent open with EACCES.
/// </summary>
internal sealed class SharedMapping : IDisposable {
  public required MemoryMappedFile Map { get; init; }

  /// The value the target reads from its activation env var: a segment NAME on Windows, a file PATH
  /// everywhere else.
  public required string SegmentName { get; init; }

  /// The temp file backing the mapping off Windows, to be unlinked when the consumer is done.
  /// Null on Windows, where a section object has no filesystem presence to clean up.
  private string? BackingFilePath { get; init; }

  /// <summary>
  /// Create a segment of <paramref name="totalSize"/> bytes. <paramref name="namePrefix"/> stems the
  /// segment/backing-file name and carries the consumer's pid plus a random suffix, so concurrent
  /// consumers (e.g. spec-test workers) cannot collide on it.
  /// </summary>
  public static SharedMapping Create(long totalSize, string namePrefix) {
    var id = $"{namePrefix}{Environment.ProcessId}_{Random.Shared.Next():x8}";

    if (OperatingSystem.IsWindows()) {
      return new SharedMapping {
        Map = MemoryMappedFile.CreateNew(id, totalSize),
        SegmentName = id,
        BackingFilePath = null
      };
    }

    var path = Path.Combine(Path.GetTempPath(), id);
    return new SharedMapping {
      Map = MemoryMappedFile.CreateFromFile(path, FileMode.CreateNew, mapName: null, totalSize,
        MemoryMappedFileAccess.ReadWrite),
      SegmentName = path,
      BackingFilePath = path
    };
  }

  public void Dispose() {
    Map.Dispose();

    // Unlink the backing file so a run does not leave the segment bytes behind in the temp
    // directory. Unix keeps the pages alive until the last munmap regardless of the directory
    // entry, so this is safe even if the child is somehow still mapped.
    if (BackingFilePath != null) File.Delete(BackingFilePath);
  }
}
