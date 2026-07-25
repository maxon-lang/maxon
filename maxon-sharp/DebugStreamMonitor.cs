using System.IO.MemoryMappedFiles;
using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp;

/// <summary>
/// Shared-memory debug stream monitor. Creates named shared memory,
/// spawns the target process with --debugstream=&lt;name&gt; in its environment,
/// reads binary events from the ring buffer, and formats them as text.
///
/// THE READER SIDE OF THE COMMIT PROTOCOL (see the ring protocol note on
/// <see cref="RuntimeEmitter"/>'s DebugStream partial). An entry appears below `write_cursor`
/// the moment it is RESERVED; its payload is written afterwards, outside the ring lock. So
/// "below write_cursor" does not mean "readable", and this monitor must not treat it as if it
/// did — it decodes only entries carrying <see cref="RuntimeEmitter.DsEntryFlagCommitted"/>,
/// and stops at the first one that does not.
/// </summary>
public class DebugStreamMonitor {

  /// How long to idle when the ring has nothing decodable — whether it is empty, or its head
  /// entry is reserved and its producer is still writing the payload.
  private const int PollIntervalMs = 1;

  // The event families `--filter` can select. Validated at PARSE time (below) rather than in the
  // decode loop, so that PassesFilter has no unhandled case to fall through — an unrecognised
  // filter used to silently show EVERY event, which is the opposite of what the user asked for.
  private const string FilterMm = "mm";
  private const string FilterSched = "sched";
  private const string FilterLog = "log";
  private const string UsageLine = "Usage: maxon monitor [--filter=mm|sched|log] <exe> [args...]";

  /// <summary>
  /// Report a failure and exit nonzero rather than dumping a raw .NET stack trace at whoever
  /// ran the command. This is NOT the blanket catch that hid the Mach-O parse failure: that one
  /// swallowed the error and returned an empty name table, so the monitor carried on printing
  /// `tag=1` for every event as though nothing had happened. This one prints the reason and
  /// FAILS, which is the whole difference between reporting and swallowing.
  /// </summary>
  public static int Run(string[] args) {
    try {
      return RunMonitor(args);
    } catch (Exception ex) {
      Console.Error.WriteLine($"maxon monitor: {ex.Message}");
      return 1;
    }
  }

  private static int RunMonitor(string[] args) {
    // Parse args: [--filter=mm|sched|log] <exe> [exe-args...]
    const string FilterOption = "--filter=";
    string? filter = null;
    int exeIndex = 0;

    for (int i = 0; i < args.Length; i++) {
      if (args[i].StartsWith(FilterOption)) {
        filter = args[i][FilterOption.Length..];
      } else {
        exeIndex = i;
        break;
      }
    }

    if (exeIndex >= args.Length) {
      Console.Error.WriteLine(UsageLine);
      return 1;
    }

    if (filter is not (null or FilterMm or FilterSched or FilterLog)) {
      Console.Error.WriteLine($"Unknown --filter value '{filter}'.");
      Console.Error.WriteLine(UsageLine);
      return 1;
    }

    var exePath = args[exeIndex];
    var exeArgs = args[(exeIndex + 1)..];

    if (!File.Exists(exePath)) {
      Console.Error.WriteLine($"Executable not found: {exePath}");
      return 1;
    }

    // The producer MAPS exactly this many bytes (EmitDebugStreamInit), so it is not the monitor's
    // number to choose — it is the shared layout contract, and it has one definition.
    long totalSize = RuntimeEmitter.DsSharedMemorySize;

    using var mapping = SharedMapping.Create(totalSize, SharedSegmentPrefix);
    using var accessor = mapping.Map.CreateViewAccessor(0, totalSize);

    // Write header
    accessor.Write(RuntimeEmitter.DsOffMagic, RuntimeEmitter.DsMagic);
    accessor.Write(RuntimeEmitter.DsOffVersion, RuntimeEmitter.DsVersion);
    accessor.Write(RuntimeEmitter.DsOffBufferSize, (long)RuntimeEmitter.DsDefaultBufferSize);
    accessor.Write(RuntimeEmitter.DsOffWriteCursor, 0L);
    accessor.Write(RuntimeEmitter.DsOffReadCursor, 0L);
    accessor.Write(RuntimeEmitter.DsOffFlags, 0L);
    accessor.Write(RuntimeEmitter.DsOffProcessId, 0L);
    accessor.Write(RuntimeEmitter.DsOffStartTimestamp, Environment.TickCount64);
    accessor.Write(RuntimeEmitter.DsOffTotalEvents, 0L);
    accessor.Write(RuntimeEmitter.DsOffDroppedEvents, 0L);
    accessor.Write(RuntimeEmitter.DsOffTagTableOffset,
      RuntimeEmitter.DsSharedMemorySize - RuntimeEmitter.DsTagTableReserveSize);
    accessor.Write(RuntimeEmitter.DsOffTagTableCount, 0L);
    accessor.Write(RuntimeEmitter.DsOffPeakUsed, 0L);
    // Seed the producer's announcement slot to "nobody has spoken". Only the producer writes it,
    // and a producer too old to know it exists leaves it exactly here — which is what makes one
    // detectable rather than merely broken. See CheckProducerSchema.
    accessor.Write(RuntimeEmitter.DsOffProducerVersion, RuntimeEmitter.DsProducerVersionUnset);

    // Spawn target process with MAXON_DEBUGSTREAM env var
    var psi = new System.Diagnostics.ProcessStartInfo {
      FileName = Path.GetFullPath(exePath),
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      CreateNoWindow = true,
    };
    // Pass remaining args
    foreach (var arg in exeArgs) {
      psi.ArgumentList.Add(arg);
    }
    psi.EnvironmentVariables[RuntimeEmitter.DsActivationEnvVar] = mapping.SegmentName;

    var process = new System.Diagnostics.Process { StartInfo = psi };
    process.Start();

    // Read loop
    int depth = 0;
    long readCursor = 0;
    long bufferSize = RuntimeEmitter.DsDefaultBufferSize;
    long bufferMask = bufferSize - 1;
    // Read the two interned-name tables out of the executable's .symtab section: MXDS_TAGS
    // (mm allocation type names) and MXDS_STRS (the names `__DebugStream` interned at compile
    // time). Both exist so an event can carry a u16 and still print as a real name.
    string[] tagNames = ReadNameTableFromExecutable(Path.GetFullPath(exePath), RuntimeEmitter.DsTagTableMagic);
    string[] logNames = ReadNameTableFromExecutable(Path.GetFullPath(exePath), RuntimeEmitter.DsStrTableMagic);

    // Buffered output for event lines (avoids per-line Console.WriteLine overhead)
    using var stdout = new StreamWriter(Console.OpenStandardOutput(), bufferSize: 65536);
    stdout.AutoFlush = false;

    // Pre-allocate private buffer for copy-then-process
    var localBuf = new byte[bufferSize];

    // Cached indent strings by depth. Both the cached and the uncached indent are built by
    // Indent(), so the two cannot disagree about how wide a level is.
    var indentCache = new string[MaxCachedIndentDepth];
    for (int i = 0; i < indentCache.Length; i++)
      indentCache[i] = Indent(i);

    // Synchronize writes to stdout between event loop and forwarding task
    var stdoutLock = new object();

    // Forward stdout/stderr in background
    var stdoutTask = Task.Run(() => {
      var sr = process.StandardOutput;
      while (sr.ReadLine() is { } line) {
        lock (stdoutLock) {
          stdout.WriteLine(line);
        }
      }
    });
    var stderrTask = Task.Run(() => {
      var sr = process.StandardError;
      while (sr.ReadLine() is { } line)
        Console.Error.WriteLine(line);
    });

    // Entries whose producer DIED between __ds_reserve and __ds_commit. Their payload will never
    // be written, so they are unreadable — but they are counted and reported, never silently
    // dropped from the trace, because a missing event is exactly the lie this protocol prevents.
    long abandonedEntries = 0;

    while (true) {
      // Snapshot liveness BEFORE the cursors. If the producer is already gone by this read, then
      // every store it will ever make is in the ring, so the cursors read next are FINAL. The
      // other order would let a last event land after the check and be lost.
      bool producerExited = process.HasExited;

      long writeCursor = accessor.ReadInt64(RuntimeEmitter.DsOffWriteCursor);

      // Pairs with the producer's RELEASE store of write_cursor in __ds_reserve. Everything it
      // wrote below this cursor — every entry HEADER, and the schema version it announced before
      // the first of them — is therefore visible to the checks below. Without this, the scan could
      // read a previous generation's bytes at a ring offset the cursor claims is live, and those
      // bytes carry a stale commit bit and a stale entry_size.
      Thread.MemoryBarrier();

      // Before a single entry is decoded: does the target even speak our schema? A mismatch here
      // is not a trace with a gap in it, it is a trace that is quietly and entirely fictional.
      if (CheckProducerSchema(accessor, writeCursor) is { } mismatch) {
        Console.Error.WriteLine(mismatch);
        lock (stdoutLock) { stdout.Flush(); }

        // Kill the target rather than leave it running blind into a ring nobody is draining.
        if (!process.HasExited) {
          try {
            process.Kill(entireProcessTree: true);
          } catch (InvalidOperationException) {
            // It exited between the check and the kill — which is the state we were asking for.
          }
        }
        process.WaitForExit();

        // Drain the forwarded stdio before returning, the same obligation the normal exit below has: the
        // target is gone so its pipes are closed and these complete promptly, but skipping them threw
        // away whatever it had already written — on the one path where the user is diagnosing a version
        // mismatch and needs every line the target managed to produce.
#pragma warning disable VSTHRD002 // synchronous entry point, no SyncContext to deadlock against
        stdoutTask.Wait();
        stderrTask.Wait();
#pragma warning restore VSTHRD002

        return SchemaMismatchExit;
      }

      long committedEnd = ScanCommittedPrefix(accessor, readCursor, writeCursor, bufferMask);

      if (committedEnd == readCursor) {
        lock (stdoutLock) { stdout.Flush(); }

        if (!producerExited) {
          // Either the ring is empty, or its head entry is reserved-but-not-yet-committed and its
          // producer is mid-payload. Wait: decoding it now would decode whatever bytes the ring
          // last held at that offset, and advancing past it would lose it.
          Thread.Sleep(PollIntervalMs);
          continue;
        }

        if (readCursor >= writeCursor) break; // fully drained, and the producer is gone

        // The producer died between reserving the head entry and committing it — a crash, a
        // panic, a watchdog kill. Nothing will ever fill that payload, so waiting for it is a
        // HANG, and the loop condition alone would spin here forever. Its HEADER is intact
        // (reserve wrote it before releasing write_cursor), so step over it and keep draining
        // the entries behind it, which may well be complete.
        abandonedEntries++;
        readCursor += EntrySizeOf(ReadEntryHeader(accessor, readCursor, bufferMask), readCursor);
        PublishReadCursor(accessor, readCursor);
        continue;
      }

      // Orders every header load in the scan above ahead of every payload load in the copy below.
      // That is what makes the commit bit mean anything: __ds_commit released the bit AFTER the
      // payload stores, so a reader that has seen the bit and does not reorder past it is
      // guaranteed the payload.
      Thread.MemoryBarrier();

      // Copy the committed prefix out of the ring into the private buffer, then immediately
      // release that span back to the producer.
      long pending = committedEnd - readCursor;
      long startPos = readCursor & bufferMask;
      long firstChunk = Math.Min(pending, bufferSize - startPos);
      accessor.ReadArray(RuntimeEmitter.DsHeaderSize + startPos, localBuf, 0, (int)firstChunk);
      if (firstChunk < pending) {
        // Wrap-around: copy the second chunk from the start of the ring buffer
        accessor.ReadArray(RuntimeEmitter.DsHeaderSize, localBuf, (int)firstChunk, (int)(pending - firstChunk));
      }

      readCursor = committedEnd;
      PublishReadCursor(accessor, readCursor);

      // Process events from private copy. Every entry in it is committed, so every payload is
      // whole — the walk below can trust what it reads.
      long localOffset = 0;
      while (localOffset < pending) {
        long header = BitConverter.ToInt64(localBuf, (int)localOffset);
        byte eventType = (byte)(header & RuntimeEmitter.DsEntryTypeMask);
        int entrySize = EntrySizeOf(header, readCursor - pending + localOffset);
        uint timestampDelta =
          (uint)((header >> RuntimeEmitter.DsEntryTimestampShift) & RuntimeEmitter.DsEntryTimestampMask);

        // ONE advance for the whole walk. Padding, the depth markers and every filtered-out family
        // all step over the entry by exactly the same amount, and spelling that out per branch is
        // six chances to step by the wrong one.
        if (eventType == RuntimeEmitter.DsEvDepthInc) {
          depth++;
        } else if (eventType == RuntimeEmitter.DsEvDepthDec) {
          if (depth > 0) depth--;
        } else if (eventType != RuntimeEmitter.DsEvPadding && PassesFilter(eventType, filter)) {
          string? line = FormatEventFromBuffer(eventType, localBuf, (int)localOffset, tagNames, logNames);

          if (line != null) {
            string indent = depth < indentCache.Length ? indentCache[depth] : Indent(depth);
            lock (stdoutLock) {
              stdout.Write('[');
              stdout.Write('+');
              uint seconds = timestampDelta / MillisecondsPerSecond;
              uint ms = timestampDelta % MillisecondsPerSecond;
              stdout.Write(seconds.ToString("D4"));
              stdout.Write('.');
              stdout.Write(ms.ToString("D3"));
              stdout.Write(']');
              stdout.Write(' ');
              stdout.Write(indent);
              stdout.WriteLine(line);
            }
          }
        }

        localOffset += entrySize;
      }
    }

    lock (stdoutLock) { stdout.Flush(); }

    // Wait for process exit
    process.WaitForExit();
#pragma warning disable VSTHRD002 // No deadlock risk — synchronous entry point, no SyncContext
    stdoutTask.Wait();
    stderrTask.Wait();
#pragma warning restore VSTHRD002

    // Final summary
    long totalEvents = accessor.ReadInt64(RuntimeEmitter.DsOffTotalEvents);
    long droppedEvents = accessor.ReadInt64(RuntimeEmitter.DsOffDroppedEvents);
    long peakUsed = accessor.ReadInt64(RuntimeEmitter.DsOffPeakUsed);
    if (totalEvents > 0 || droppedEvents > 0 || abandonedEntries > 0) {
      double peakMB = peakUsed / (1024.0 * 1024.0);
      double bufMB = bufferSize / (1024.0 * 1024.0);
      int peakPct = bufferSize > 0 ? (int)(peakUsed * 100 / bufferSize) : 0;
      // `abandoned` only ever appears when it is non-zero, but it appears LOUDLY when it is: it
      // means the producer was killed mid-entry and that entry's payload is gone for good.
      string abandoned = abandonedEntries > 0
        ? $", {abandonedEntries} abandoned (producer died mid-entry)"
        : "";
      Console.Error.WriteLine($"[debugstream] {totalEvents} events, {droppedEvents} dropped{abandoned}, peak buffer: {peakMB:F1} MB / {bufMB:F1} MB ({peakPct}%)");
    }

    return process.ExitCode;
  }

  /// The stem of the segment name / backing-file name. Carries the monitor's pid and a random
  /// suffix so that concurrent spec-test workers cannot collide on it.
  private const string SharedSegmentPrefix = "maxon_ds_";

  /// The timestamp on the wire is a millisecond delta; the trace prints it as `+SSSS.mmm`.
  private const uint MillisecondsPerSecond = 1000;

  // DEPTH_INC / DEPTH_DEC nest the trace. Indents are precomputed up to this depth and built on
  // demand past it — the cache is an optimisation, never a different answer.
  private const int SpacesPerDepthLevel = 2;
  private const int MaxCachedIndentDepth = 64;

  private static string Indent(int depth) => new(' ', depth * SpacesPerDepthLevel);

  /// <summary>
  /// Does this event belong to the family the user asked for? A null filter means "all of them".
  ///
  /// The families are CONTIGUOUS code ranges (see the event table in RuntimeEmitter): the mm codes
  /// sit below DsEvSchedSpawn, sched and dbg between that and the Log range, and the Log events —
  /// the ones USER MAXON SOURCE emitted — at the top. So membership is a range test, not a list
  /// that has to be kept in step with the event table.
  ///
  /// The filter string is validated when the command line is parsed, so an unrecognised one cannot
  /// reach here; if one does, it would silently show every event, which is the bug this throw
  /// exists to make impossible.
  /// </summary>
  private static bool PassesFilter(byte eventType, string? filter) {
    if (filter == null) return true;

    bool isLogEvent = eventType >= RuntimeEmitter.DsEvLogPhaseBegin
                   && eventType <= RuntimeEmitter.DsEvLogText;

    return filter switch {
      FilterMm => eventType < RuntimeEmitter.DsEvSchedSpawn,
      FilterSched => eventType >= RuntimeEmitter.DsEvSchedSpawn && !isLogEvent,
      FilterLog => isLogEvent,
      _ => throw new InvalidOperationException(
        $"--filter '{filter}' reached the decode loop; it should have been rejected at parse time")
    };
  }

  /// The monitor's own exit code when it refuses to decode a ring it does not speak the schema of.
  /// Distinct from the target's exit code, which is what a successful run returns.
  private const int SchemaMismatchExit = 3;

  /// <summary>
  /// Does the target speak our wire schema? Returns the message to fail with, or null to proceed.
  ///
  /// The producer announces its version in `__debugstream_init`, before it can emit anything, and
  /// `write_cursor` is RELEASE-stored after that announcement — so a monitor that can see any entry
  /// at all can see the version that produced it. There is no window in which a current producer
  /// looks like an old one.
  ///
  /// Which makes the three cases exhaustive:
  ///   * the announced version is ours — decode.
  ///   * NOTHING announced and the ring is empty — nothing ever attached. A binary built without
  ///     `--debugstream` never opens the segment, and monitoring one is not an error; it simply has
  ///     no events. Proceed, and print nothing.
  ///   * anything else — a foreign schema. REFUSE, and say so.
  ///
  /// The last case is the one this exists for, because its silent form is so convincing: a v1
  /// producer never sets the commit bit, so this monitor waits for a commit that will never come,
  /// lets the ring fill until the producer is dropping ~98% of its events, and then steps over
  /// every entry as "abandoned (producer died mid-entry)". Measured: 0 events decoded, 283221
  /// dropped, 5290 abandoned — and the producer had exited cleanly. Every number in that summary is
  /// a fiction, and it is the trace an old binary produces TODAY. An instrument that lies is worse
  /// than no instrument; this is the check that makes it merely refuse.
  /// </summary>
  private static string? CheckProducerSchema(MemoryMappedViewAccessor accessor, long writeCursor) {
    long announced = accessor.ReadInt64(RuntimeEmitter.DsOffProducerVersion);

    if (announced == RuntimeEmitter.DsVersion) return null;

    bool nothingAttached =
      announced == RuntimeEmitter.DsProducerVersionUnset && writeCursor == 0;
    if (nothingAttached) return null;

    string speaks = announced == RuntimeEmitter.DsProducerVersionUnset
      ? $"a schema older than v{RuntimeEmitter.DsVersion} (it wrote {writeCursor} bytes of entries "
        + "without ever announcing a version)"
      : $"schema v{announced}";

    return $"[debugstream] SCHEMA MISMATCH — refusing to decode.\n"
      + $"[debugstream]   this monitor speaks v{RuntimeEmitter.DsVersion}; the target speaks {speaks}.\n"
      + "[debugstream]   The two disagree about the entry COMMIT BIT, so every event in this trace\n"
      + "[debugstream]   would be wrong: payloads decoded before they were written, or — more\n"
      + "[debugstream]   likely — nothing decoded at all and the loss blamed on a producer crash\n"
      + "[debugstream]   that never happened.\n"
      + "[debugstream]   Rebuild the target with this compiler and run it again.";
  }

  /// <summary>
  /// How far below <paramref name="writeCursor"/> the ring is actually READABLE: the end of the
  /// run of entries, starting at <paramref name="readCursor"/>, that carry the COMMIT BIT.
  ///
  /// An entry appears below write_cursor as soon as it is RESERVED, and its payload is written
  /// after the ring lock is released — so "present" and "readable" are two different states, and
  /// the commit bit is the difference. The scan stops at the first entry without it and never
  /// looks past: entries are consumed in ring order, so an uncommitted head briefly blocks the
  /// committed entries behind it rather than being skipped over and lost.
  /// </summary>
  private static long ScanCommittedPrefix(MemoryMappedViewAccessor accessor, long readCursor,
      long writeCursor, long bufferMask) {
    long cursor = readCursor;
    while (cursor < writeCursor) {
      long header = ReadEntryHeader(accessor, cursor, bufferMask);
      if ((header & RuntimeEmitter.DsEntryHeaderCommittedBit) == 0) break;
      cursor += EntrySizeOf(header, cursor);
    }
    return cursor;
  }

  /// <summary>
  /// The packed 8-byte header of the entry at <paramref name="cursor"/>. An entry never straddles
  /// the end of the ring — __ds_reserve emits a padding entry rather than let one wrap — so this
  /// single read is always contiguous.
  /// </summary>
  private static long ReadEntryHeader(MemoryMappedViewAccessor accessor, long cursor, long bufferMask) =>
    accessor.ReadInt64(RuntimeEmitter.DsHeaderSize + (cursor & bufferMask));

  /// <summary>
  /// Total bytes of an entry, header included. Zero is not a legal entry size — __ds_reserve
  /// never reserves one, and every event family has at least a header — so a zero here means the
  /// ring is corrupt AND that a walk keyed off it would spin on this offset forever. Say so.
  /// </summary>
  private static int EntrySizeOf(long header, long cursor) {
    int size = (int)((header >> RuntimeEmitter.DsEntrySizeShift) & RuntimeEmitter.DsEntrySizeMask);
    if (size == 0)
      throw new InvalidOperationException(
        $"DebugStream ring corrupt: entry at cursor {cursor} declares size 0 (header 0x{header:x16})");
    return size;
  }

  /// <summary>
  /// Publish `read_cursor`, handing that span of the ring back to the producer.
  ///
  /// The fence is not decoration. The instant this store lands, the producer may overwrite the
  /// bytes it frees — so every load of the copy we just took must be complete before it.
  /// </summary>
  private static void PublishReadCursor(MemoryMappedViewAccessor accessor, long readCursor) {
    Thread.MemoryBarrier();
    accessor.Write(RuntimeEmitter.DsOffReadCursor, readCursor);
  }

  // The slice of the PE/COFF format this parser walks: DOS stub -> PE signature -> COFF header ->
  // optional header -> section table. Named, because a bare `Seek(14)` in the middle of a chain of
  // relative skips is unverifiable by reading it — which is exactly how the field below it came to
  // be read two bytes early.
  private const int DosOffPeSignaturePointer = 0x3C;  // e_lfanew
  private const int PeSignatureSize = 4;              // "PE\0\0"
  private const int CoffOffNumberOfSections = 2;      // COFF: Machine(2) then NumberOfSections(2)
  private const int CoffOffSizeOfOptionalHeader = 16;
  private const int CoffHeaderSize = 20;
  private const int SectionHeaderSize = 40;
  private const int SectionNameSize = 8;
  /// Section header: Name(8) VirtualSize(4) VirtualAddress(4), then SizeOfRawData and
  /// PointerToRawData back to back — which is why one seek here reads both.
  private const int SectionOffRawDataSize = 16;
  /// Where the compiler puts its symdata in a PE image, and so where both name blobs live.
  private const string SymtabSectionName = ".symtab";
  /// The DOS stub's "MZ", the first two bytes of every PE image.
  private const ushort DosMzMagic = 0x5A4D;

  // The slice of the Mach-O format this parser walks: mach_header_64 -> load commands ->
  // LC_SEGMENT_64 -> its section_64 table. Named for the same reason the PE offsets above are:
  // a bare `Seek(64)` in the middle of a walk is unverifiable by reading it.
  private const uint MachO64Magic = 0xFEEDFACF;
  private const int MachOHeaderSize = 32;
  private const int MachOOffNumberOfCommands = 16;  // mach_header_64: ncmds
  private const uint MachOLcSegment64 = 0x19;
  private const int MachOSegmentOffName = 8;        // segment_command_64: cmd(4) cmdsize(4) segname(16)
  private const int MachOSegmentOffNumberOfSections = 64;
  /// sizeof(segment_command_64). Its section_64 table begins immediately after it.
  private const int MachOSegmentHeaderSize = 72;
  private const int MachOSectionHeaderSize = 80;
  /// section_64's sectname and segname, and segment_command_64's segname, are all char[16].
  private const int MachONameSize = 16;
  /// section_64: sectname(16) segname(16) addr(8), then size and offset.
  private const int MachOSectionOffSize = 40;
  private const int MachOSectionOffFileOffset = 48;
  /// Mach-O has no `.symtab`: 6-MachOWriter merges rdata, ucddata and symdata into ONE
  /// __TEXT,__const section, so that is where both name blobs live.
  private const string MachOTextSegmentName = "__TEXT";
  private const string MachOConstSectionName = "__const";

  /// <summary>
  /// Find the section carrying the compiler's symdata, then scan it for a packed name-table blob
  /// carrying <paramref name="magic"/>. Two blobs use this: MXDS_TAGS (mm allocation type names)
  /// and MXDS_STRS (the names `__DebugStream` interned at compile time). ONE entry point and ONE
  /// <see cref="ScanForNameBlob"/> under both container formats, so neither the two blobs nor the
  /// two containers can drift apart on the format.
  ///
  /// The container is chosen by the file's MAGIC rather than by the monitor's own OS: the
  /// compiler cross-compiles, so the executable in front of us need not match the machine reading
  /// it. This dispatch did not exist — the parser was PE-only, and on a Mach-O binary it read
  /// `e_lfanew`@0x3C as 0, believed the file had 256 sections and a 920-byte optional header,
  /// found no `.symtab` in the megabyte of unrelated bytes it then walked, and a blanket catch
  /// turned all of it into an empty table. Every name in the trace quietly became `tag=1`.
  /// </summary>
  private static string[] ReadNameTableFromExecutable(string exePath, byte[] magic) {
    using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    using var reader = new BinaryReader(fs);

    uint fileMagic = reader.ReadUInt32();

    // A failure here is LOUD. An executable whose name tables cannot be located is not an
    // executable with no names in it: it is a monitor that will print `tag=1` for every event and
    // a golden that will fail without ever saying why. That silence is what hid this bug.
    var (sectionOffset, sectionSize) =
      fileMagic == MachO64Magic ? FindMachOSymdataSection(fs, reader, exePath)
      : (fileMagic & ushort.MaxValue) == DosMzMagic ? FindPeSymdataSection(fs, reader, exePath)
      : throw new InvalidOperationException(
          $"DebugStream: cannot read the interned name tables from '{exePath}': expected a PE "
          + $"image (MZ) or a 64-bit Mach-O executable (magic 0x{MachO64Magic:X8}), but the file "
          + $"begins 0x{fileMagic:X8}.");

    return ScanForNameBlob(fs, reader, sectionOffset, sectionSize, magic);
  }

  /// <summary>
  /// A NUL-PADDED fixed-width name field: PE's 8-byte section name, Mach-O's 16-byte segname and
  /// sectname. All three pad rather than terminate, so the trailing NULs are trimmed off the full
  /// width rather than scanned for.
  /// </summary>
  private static string ReadFixedName(FileStream fs, BinaryReader reader, long offset, int size) {
    fs.Seek(offset, SeekOrigin.Begin);
    return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(size)).TrimEnd('\0');
  }

  /// <summary>
  /// The file offset and size of the PE image's <see cref="SymtabSectionName"/> section.
  /// </summary>
  private static (uint Offset, uint Size) FindPeSymdataSection(FileStream fs, BinaryReader reader,
      string exePath) {
    fs.Seek(DosOffPeSignaturePointer, SeekOrigin.Begin);
    long coff = reader.ReadUInt32() + PeSignatureSize;

    // Each field is seeked to ABSOLUTELY, off the COFF base and its own named offset. This used
    // to be a chain of relative reads and skips, and the chain was one field out of step:
    // `numberOfSections` was seeked to COFF+0 and so actually read MACHINE — 0x8664 on x64. The
    // section walk below was bounded by 34404 instead of 6, and only ever terminated because
    // `.symtab` happens to appear early and returns.
    fs.Seek(coff + CoffOffNumberOfSections, SeekOrigin.Begin);
    int numberOfSections = reader.ReadUInt16();

    fs.Seek(coff + CoffOffSizeOfOptionalHeader, SeekOrigin.Begin);
    int sizeOfOptionalHeader = reader.ReadUInt16();

    // The section table follows the optional header, whose size is the field just read.
    long sectionTable = coff + CoffHeaderSize + sizeOfOptionalHeader;

    for (int i = 0; i < numberOfSections; i++) {
      long header = sectionTable + (long)i * SectionHeaderSize;
      if (ReadFixedName(fs, reader, header, SectionNameSize) != SymtabSectionName) continue;

      fs.Seek(header + SectionOffRawDataSize, SeekOrigin.Begin);
      uint rawDataSize = reader.ReadUInt32();
      uint rawDataPointer = reader.ReadUInt32();
      return (rawDataPointer, rawDataSize);
    }

    throw new InvalidOperationException(
      $"DebugStream: PE image '{exePath}' has no '{SymtabSectionName}' section among its "
      + $"{numberOfSections} sections; the interned name tables live there.");
  }

  /// <summary>
  /// The file offset and size of <see cref="MachOTextSegmentName"/>,<see cref="MachOConstSectionName"/>
  /// in a 64-bit Mach-O executable — the section 6-MachOWriter merges the compiler's symdata into.
  /// </summary>
  private static (uint Offset, uint Size) FindMachOSymdataSection(FileStream fs, BinaryReader reader,
      string exePath) {
    fs.Seek(MachOOffNumberOfCommands, SeekOrigin.Begin);
    uint commandCount = reader.ReadUInt32();

    long command = MachOHeaderSize;
    for (uint i = 0; i < commandCount; i++) {
      fs.Seek(command, SeekOrigin.Begin);
      uint cmd = reader.ReadUInt32();
      uint cmdSize = reader.ReadUInt32();

      // Every load command advances the walk by its OWN size, so a zero-sized one does not merely
      // mean a corrupt file — it means this loop never terminates. Say so instead of hanging.
      if (cmdSize == 0)
        throw new InvalidOperationException(
          $"DebugStream: Mach-O '{exePath}' is corrupt: load command {i} (cmd 0x{cmd:X}) at file "
          + $"offset {command} declares size 0.");

      if (cmd == MachOLcSegment64
          && ReadFixedName(fs, reader, command + MachOSegmentOffName, MachONameSize) == MachOTextSegmentName) {
        fs.Seek(command + MachOSegmentOffNumberOfSections, SeekOrigin.Begin);
        uint sectionCount = reader.ReadUInt32();

        for (uint s = 0; s < sectionCount; s++) {
          long section = command + MachOSegmentHeaderSize + (long)s * MachOSectionHeaderSize;
          if (ReadFixedName(fs, reader, section, MachONameSize) != MachOConstSectionName) continue;

          fs.Seek(section + MachOSectionOffSize, SeekOrigin.Begin);
          ulong size = reader.ReadUInt64();

          fs.Seek(section + MachOSectionOffFileOffset, SeekOrigin.Begin);
          uint offset = reader.ReadUInt32();
          return (offset, (uint)size);
        }
      }

      command += cmdSize;
    }

    throw new InvalidOperationException(
      $"DebugStream: Mach-O '{exePath}' has no '{MachOConstSectionName}' section in its "
      + $"'{MachOTextSegmentName}' segment among its {commandCount} load commands; the interned "
      + "name tables live there.");
  }

  /// <summary>
  /// Scan the .symtab section bytes for the magic prefix and decode the packed name table.
  /// </summary>
  private static string[] ScanForNameBlob(FileStream fs, BinaryReader reader, uint sectionOffset,
      uint sectionSize, byte[] magic) {
    fs.Seek(sectionOffset, SeekOrigin.Begin);
    var sectionData = reader.ReadBytes((int)sectionSize);

    // Scan for magic
    for (int pos = 0; pos <= sectionData.Length - magic.Length; pos++) {
      bool match = true;
      for (int j = 0; j < magic.Length; j++) {
        if (sectionData[pos + j] != magic[j]) { match = false; break; }
      }
      if (!match) continue;

      // Found magic at pos. Decode: [magic(10)][count:u16][len0:u16][name0]...
      // Every advance is DsNameBlobFieldSize — the emitter's own constant, so the two ends of this
      // format cannot disagree about how wide a field is and slide the parse.
      int offset = pos + magic.Length;
      if (offset + RuntimeEmitter.DsNameBlobFieldSize > sectionData.Length) return [];

      ushort count = BitConverter.ToUInt16(sectionData, offset);
      offset += RuntimeEmitter.DsNameBlobFieldSize;

      var names = new string[count];
      for (int i = 0; i < count && offset + RuntimeEmitter.DsNameBlobFieldSize <= sectionData.Length; i++) {
        ushort len = BitConverter.ToUInt16(sectionData, offset);
        offset += RuntimeEmitter.DsNameBlobFieldSize;
        if (offset + len > sectionData.Length) break;
        names[i] = System.Text.Encoding.UTF8.GetString(sectionData, offset, len);
        offset += len;
      }
      return names;
    }

    return [];
  }

  /// <summary>
  /// Resolve an interned index back to the name the compiler embedded in the executable. Index 0
  /// means "no name" in both tables, and a name may legitimately be missing (an executable built
  /// by an older compiler), so an unresolvable index prints as `<field>=<n>` rather than throwing
  /// — a trace with one raw number in it is still worth reading.
  /// </summary>
  private static string ResolveInternedName(int index, string[] names, string fieldName) {
    if (index > 0 && index < names.Length && !string.IsNullOrEmpty(names[index]))
      return names[index];
    return $"{fieldName}={index}";
  }

  // The two interned-name fields the events carry. MXDS_TAGS holds mm allocation TYPE names;
  // MXDS_STRS holds the names `__DebugStream` interned at compile time.
  private const string TagFieldName = "tag";
  private const string LogNameFieldName = "name";

  /// <summary>
  /// Decode the payload every TAGGED mm event shares: the alloc id, the interned tag NAME, and
  /// the packed word's 32-bit value slot — an allocation size for the alloc family, a new
  /// refcount for the refcount family, and unused for mm_free.
  ///
  /// One decoder against the emitter's one packer (<c>EmitDsStoreMmPayload</c>), keyed off the
  /// same offsets. The six mm cases used to inline the same four lines each, which is six chances
  /// to disagree with the producer about a shift.
  /// </summary>
  private static (long AllocId, string Tag, long Value) ReadMmPayload(byte[] buf, int offset,
      string[] tagNames) {
    long allocId = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsMmOffAllocId);
    long packed = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsMmOffPacked);
    int tagIndex = (int)(packed & RuntimeEmitter.DsMmTagIndexMask);
    long value = (packed >> RuntimeEmitter.DsMmValueShift) & RuntimeEmitter.DsMmValueMask;
    return (allocId, ResolveInternedName(tagIndex, tagNames, TagFieldName), value);
  }

  private static string? FormatEventFromBuffer(byte eventType, byte[] buf, int offset, string[] tagNames,
      string[] logNames) {
    switch (eventType) {
      case RuntimeEmitter.DsEvLogPhaseBegin:
      case RuntimeEmitter.DsEvLogPhaseEnd:
      case RuntimeEmitter.DsEvLogEvent:
      case RuntimeEmitter.DsEvLogText:
        return FormatLogEvent(eventType, buf, offset, logNames);
      case RuntimeEmitter.DsEvMmAlloc:
      case RuntimeEmitter.DsEvMmRealloc:
      case RuntimeEmitter.DsEvMmCow: {
        string name = eventType switch {
          RuntimeEmitter.DsEvMmAlloc => "mm_alloc",
          RuntimeEmitter.DsEvMmRealloc => "mm_realloc",
          RuntimeEmitter.DsEvMmCow => "mm_cow",
          _ => throw new InvalidOperationException($"Unexpected alloc event type: 0x{eventType:X2}")
        };
        var (allocId, tag, size) = ReadMmPayload(buf, offset, tagNames);
        return $"{name} {tag} #{allocId} size={size}";
      }
      case RuntimeEmitter.DsEvMmFree: {
        // The value slot is unused by a free — it has no size and no refcount to report.
        var (allocId, tag, _) = ReadMmPayload(buf, offset, tagNames);
        return $"mm_free {tag} #{allocId}";
      }
      case RuntimeEmitter.DsEvMmIncref:
      case RuntimeEmitter.DsEvMmDecref:
      case RuntimeEmitter.DsEvMmTransfer: {
        string name = eventType switch {
          RuntimeEmitter.DsEvMmIncref => "mm_incref",
          RuntimeEmitter.DsEvMmDecref => "mm_decref",
          RuntimeEmitter.DsEvMmTransfer => "mm_transfer",
          _ => throw new InvalidOperationException($"Unexpected refcount event type: 0x{eventType:X2}")
        };
        var (allocId, tag, rc) = ReadMmPayload(buf, offset, tagNames);
        return $"{name} {tag} #{allocId} rc={rc}";
      }
      case RuntimeEmitter.DsEvMmRawAlloc: {
        long rawId = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsMmOffAllocId);
        long size = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsMmOffRawSize);
        return $"mm_raw_alloc #{rawId} size={size}";
      }
      case RuntimeEmitter.DsEvMmRawFree: {
        long rawId = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsMmOffAllocId);
        return $"mm_raw_free #{rawId}";
      }
      case RuntimeEmitter.DsEvSchedSpawn:
      case RuntimeEmitter.DsEvSchedAwait:
      case RuntimeEmitter.DsEvSchedYield:
      case RuntimeEmitter.DsEvSchedResume:
      case RuntimeEmitter.DsEvIoYield:
      case RuntimeEmitter.DsEvIoResume: {
        string name = eventType switch {
          RuntimeEmitter.DsEvSchedSpawn => "sched_spawn",
          RuntimeEmitter.DsEvSchedAwait => "sched_await",
          RuntimeEmitter.DsEvSchedYield => "sched_yield",
          RuntimeEmitter.DsEvSchedResume => "sched_resume",
          RuntimeEmitter.DsEvIoYield => "io_yield",
          RuntimeEmitter.DsEvIoResume => "io_resume",
          _ => throw new InvalidOperationException($"Unexpected sched event type: 0x{eventType:X2}")
        };
        long traceId = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsSchedOffTraceId);
        return $"{name} #{traceId}";
      }
      case RuntimeEmitter.DsEvDbgEnqueue:
      case RuntimeEmitter.DsEvDbgDequeue:
      case RuntimeEmitter.DsEvDbgRunnextSet:
      case RuntimeEmitter.DsEvDbgRunnextTake:
      case RuntimeEmitter.DsEvDbgRunnextDisplace:
      case RuntimeEmitter.DsEvDbgStatusStore:
      case RuntimeEmitter.DsEvDbgIoComplete:
      case RuntimeEmitter.DsEvDbgFreeListPush:
      case RuntimeEmitter.DsEvDbgFreeListPop:
      case RuntimeEmitter.DsEvDbgWloopRunGt:
      case RuntimeEmitter.DsEvDbgAwaitDeqRun:
      case RuntimeEmitter.DsEvDbgTrampolineCompleted:
      case RuntimeEmitter.DsEvDbgTimerFire:
      case RuntimeEmitter.DsEvDbgCsxEntry:
      case RuntimeEmitter.DsEvDbgCsxExit: {
        long gt = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsDbgOffGt);
        long pId = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsDbgOffPid);
        long arg2 = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsDbgOffArg2);
        long arg3 = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsDbgOffArg3);
        long arg4 = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsDbgOffArg4);
        return FormatDbgEvent(eventType, gt, pId, arg2, arg3, arg4);
      }
      case RuntimeEmitter.DsEvHeartbeat:
        return null;
      case RuntimeEmitter.DsEvDepthInc:
      case RuntimeEmitter.DsEvDepthDec:
      case RuntimeEmitter.DsEvPadding:
        throw new InvalidOperationException($"Event type 0x{eventType:X2} should be handled before FormatEventFromBuffer");
    }
    throw new InvalidOperationException($"Unknown debug stream event type: 0x{eventType:X2}");
  }

  /// <summary>
  /// Decode the Log events — the ones USER MAXON SOURCE emitted through the `__DebugStream`
  /// builtin. Every one of them carries gt + p_id + unit_id, which is what lets N interleaved
  /// workers be demuxed back into per-worker, per-unit timelines.
  ///
  /// `cat` and `lvl` print numerically: the wire schema fixes them as raw bytes, and their
  /// meaning belongs to the emitting program's own category/level enums, which the monitor
  /// deliberately does not know. Anything the monitor DOES name — a phase, an event — is an
  /// interned index into MXDS_STRS, so it costs the emitting program nothing to be readable.
  /// </summary>
  private static string FormatLogEvent(byte eventType, byte[] buf, int offset, string[] logNames) {
    long gt = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsLogOffGt);
    long pId = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsLogOffPid);
    long fields = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsLogOffFields);
    uint unitId = (uint)((fields >> RuntimeEmitter.DsLogUnitIdShift) & uint.MaxValue);
    string who = $"gt=0x{gt:x} P{pId} unit={unitId}";

    switch (eventType) {
      case RuntimeEmitter.DsEvLogPhaseBegin:
      case RuntimeEmitter.DsEvLogPhaseEnd: {
        int phaseId = (int)(fields & RuntimeEmitter.DsLogU16FieldMask);
        string verb = eventType == RuntimeEmitter.DsEvLogPhaseBegin ? "log_phase_begin" : "log_phase_end";
        return $"{verb} {ResolveInternedName(phaseId, logNames, LogNameFieldName)} {who}";
      }
      case RuntimeEmitter.DsEvLogEvent: {
        int category = (int)(fields & RuntimeEmitter.DsLogCatMask);
        int level = (int)((fields >> RuntimeEmitter.DsLogLvlShift) & RuntimeEmitter.DsLogLvlMask);
        int eventId = (int)((fields >> RuntimeEmitter.DsLogU16FieldShift) & RuntimeEmitter.DsLogU16FieldMask);
        long arg0 = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsLogOffArg0);
        long arg1 = BitConverter.ToInt64(buf, offset + RuntimeEmitter.DsLogOffArg1);
        return $"log_event {ResolveInternedName(eventId, logNames, LogNameFieldName)} cat={category} lvl={level} {who} a0={arg0} a1={arg1}";
      }
      case RuntimeEmitter.DsEvLogText: {
        int category = (int)(fields & RuntimeEmitter.DsLogCatMask);
        int level = (int)((fields >> RuntimeEmitter.DsLogLvlShift) & RuntimeEmitter.DsLogLvlMask);
        int len = (int)((fields >> RuntimeEmitter.DsLogU16FieldShift) & RuntimeEmitter.DsLogU16FieldMask);
        var text = System.Text.Encoding.UTF8.GetString(buf, offset + RuntimeEmitter.DsLogOffText, len)
          .TrimEnd('\r', '\n');
        return $"log_text cat={category} lvl={level} {who} {text}";
      }
      default:
        throw new InvalidOperationException($"Unexpected log event type: 0x{eventType:X2}");
    }
  }

  /// <summary>
  /// Name the queue a green thread went onto or came off. The codes are the emitter's own
  /// <c>DsDbgQueue*</c> constants — the enqueue and dequeue sides share the first two and diverge
  /// on the third (a steal is a CHAIN going in and a FIRST coming out), which is exactly why the
  /// direction is a parameter here instead of two near-identical switches.
  ///
  /// A code the emitter never writes cannot appear, so it is a corrupt trace, not an unknown kind.
  /// </summary>
  private static string FormatQueueKind(long kind, bool isEnqueue) => kind switch {
    RuntimeEmitter.DsDbgQueueLocal   => "local",
    RuntimeEmitter.DsDbgQueueGlobal  => "global",
    RuntimeEmitter.DsDbgQueueRunnext => "runnext",
    RuntimeEmitter.DsDbgQueueStealChain => isEnqueue ? "steal_chain" : "steal_first",
    _ => throw new InvalidOperationException($"DebugStream: unknown dbg queue kind {kind}")
  };

  private static string FormatDbgEvent(byte eventType, long gt, long pId, long arg2, long arg3, long arg4) {
    string gtHex = $"gt=0x{gt:x}";
    string pIdStr = $"P{pId}";
    switch (eventType) {
      case RuntimeEmitter.DsEvDbgEnqueue:
        return $"dbg_enqueue {gtHex} {pIdStr} kind={FormatQueueKind(arg2, isEnqueue: true)} owner=P{arg3}";
      case RuntimeEmitter.DsEvDbgDequeue:
        return $"dbg_dequeue {gtHex} {pIdStr} kind={FormatQueueKind(arg2, isEnqueue: false)} from=P{arg3}";
      case RuntimeEmitter.DsEvDbgRunnextSet:
        return $"dbg_runnext_set {gtHex} {pIdStr}";
      case RuntimeEmitter.DsEvDbgRunnextTake:
        return $"dbg_runnext_take {gtHex} {pIdStr}";
      case RuntimeEmitter.DsEvDbgRunnextDisplace:
        return $"dbg_runnext_displace displaced={gtHex} new_gt=0x{arg2:x} {pIdStr}";
      case RuntimeEmitter.DsEvDbgStatusStore:
        return $"dbg_status {gtHex} {pIdStr} {arg2}->{arg3} site={arg4}";
      case RuntimeEmitter.DsEvDbgIoComplete: {
        // The emitter's DsDbgIoPhase* constants. A code it never writes is a corrupt trace.
        string phase = arg2 switch {
          RuntimeEmitter.DsDbgIoPhaseStatusSet  => "status_set",
          RuntimeEmitter.DsDbgIoPhaseSpinDone   => "spin_done",
          RuntimeEmitter.DsDbgIoPhaseEnqueueing => "enqueueing",
          _ => throw new InvalidOperationException($"DebugStream: unknown dbg io phase {arg2}")
        };
        return $"dbg_io_complete {gtHex} phase={phase}";
      }
      case RuntimeEmitter.DsEvDbgFreeListPush:
        return $"dbg_free_push {gtHex} {pIdStr} new_len={arg2}";
      case RuntimeEmitter.DsEvDbgFreeListPop:
        return $"dbg_free_pop  {gtHex} {pIdStr} new_len={arg2}";
      case RuntimeEmitter.DsEvDbgWloopRunGt:
        return $"dbg_wloop_run {gtHex} {pIdStr}";
      case RuntimeEmitter.DsEvDbgAwaitDeqRun:
        return $"dbg_await_run {gtHex} {pIdStr}";
      case RuntimeEmitter.DsEvDbgTrampolineCompleted:
        return $"dbg_tramp_completed {gtHex}";
      case RuntimeEmitter.DsEvDbgTimerFire:
        return $"dbg_timer_fire {gtHex}";
      case RuntimeEmitter.DsEvDbgCsxEntry:
        return $"dbg_csx_entry from={gtHex} to=0x{arg2:x} from_rsp=0x{arg3:x} from_rbp=0x{arg4:x}";
      case RuntimeEmitter.DsEvDbgCsxExit:
        return $"dbg_csx_exit  from={gtHex} to=0x{arg2:x} to_rsp=0x{arg3:x} to_rbp=0x{arg4:x}";
      default:
        // Unreachable: FormatEventFromBuffer routes only the DsEvDbg* codes enumerated above here,
        // so a miss means a new code was added to that switch and not to this one.
        throw new InvalidOperationException($"Unexpected dbg event type: 0x{eventType:X2}");
    }
  }
}
