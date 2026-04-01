using System.IO.MemoryMappedFiles;
using MaxonSharp.Compiler.Mlir.Runtime;

namespace MaxonSharp;

/// <summary>
/// Shared-memory debug stream monitor. Creates named shared memory,
/// spawns the target process with --debugstream=&lt;name&gt; in its environment,
/// reads binary events from the ring buffer, and formats them as text.
/// </summary>
public class DebugStreamMonitor {

  public static int Run(string[] args) {
    // Parse args: [--filter=mm|sched] <exe> [exe-args...]
    string? filter = null;
    int exeIndex = 0;

    for (int i = 0; i < args.Length; i++) {
      if (args[i].StartsWith("--filter=")) {
        filter = args[i]["--filter=".Length..];
      } else {
        exeIndex = i;
        break;
      }
    }

    if (exeIndex >= args.Length) {
      Console.Error.WriteLine("Usage: maxon monitor [--filter=mm|sched] <exe> [args...]");
      return 1;
    }

    var exePath = args[exeIndex];
    var exeArgs = args[(exeIndex + 1)..];

    if (!File.Exists(exePath)) {
      Console.Error.WriteLine($"Executable not found: {exePath}");
      return 1;
    }

    // Generate unique named shared memory segment
    var shmName = $"maxon_ds_{Environment.ProcessId}_{Random.Shared.Next():x8}";

    // Total shared memory size: header + buffer + tag table space
    long totalSize = RuntimeEmitter.DsHeaderSize + RuntimeEmitter.DsDefaultBufferSize + 65536;

    using var mmf = MemoryMappedFile.CreateNew(shmName, totalSize);
    using var accessor = mmf.CreateViewAccessor(0, totalSize);

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
    accessor.Write(RuntimeEmitter.DsOffTagTableOffset, (long)(RuntimeEmitter.DsHeaderSize + RuntimeEmitter.DsDefaultBufferSize));
    accessor.Write(RuntimeEmitter.DsOffTagTableCount, 0L);
    accessor.Write(RuntimeEmitter.DsOffPeakUsed, 0L);

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
    psi.EnvironmentVariables["MAXON_DEBUGSTREAM"] = shmName;

    var process = new System.Diagnostics.Process { StartInfo = psi };
    process.Start();

    // Read loop
    int depth = 0;
    long readCursor = 0;
    long bufferSize = RuntimeEmitter.DsDefaultBufferSize;
    long bufferMask = bufferSize - 1;
    // Read tag names from the executable's .symtab section
    string[] tagNames = ReadTagsFromExecutable(Path.GetFullPath(exePath));

    // Buffered output for event lines (avoids per-line Console.WriteLine overhead)
    using var stdout = new StreamWriter(Console.OpenStandardOutput(), bufferSize: 65536);
    stdout.AutoFlush = false;

    // Pre-allocate private buffer for copy-then-process
    var localBuf = new byte[bufferSize];

    // Cached indent strings by depth
    var indentCache = new string[64];
    for (int i = 0; i < indentCache.Length; i++)
      indentCache[i] = new string(' ', i * 2);

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

    while (!process.HasExited || readCursor < accessor.ReadInt64(RuntimeEmitter.DsOffWriteCursor)) {
      long writeCursor = accessor.ReadInt64(RuntimeEmitter.DsOffWriteCursor);

      if (readCursor >= writeCursor) {
        lock (stdoutLock) { stdout.Flush(); }
        Thread.Sleep(1);
        continue;
      }

      // Copy pending data from ring buffer into private buffer, then immediately
      // advance read_cursor to free ring buffer space for the producer.
      long pending = writeCursor - readCursor;
      long startPos = readCursor & bufferMask;
      long firstChunk = Math.Min(pending, bufferSize - startPos);
      accessor.ReadArray(RuntimeEmitter.DsHeaderSize + startPos, localBuf, 0, (int)firstChunk);
      if (firstChunk < pending) {
        // Wrap-around: copy the second chunk from the start of the ring buffer
        accessor.ReadArray(RuntimeEmitter.DsHeaderSize, localBuf, (int)firstChunk, (int)(pending - firstChunk));
      }

      // Release ring buffer immediately
      readCursor = writeCursor;
      accessor.Write(RuntimeEmitter.DsOffReadCursor, readCursor);

      // Process events from private copy
      long localOffset = 0;
      while (localOffset < pending) {
        // Read entry header (8 bytes)
        long header = BitConverter.ToInt64(localBuf, (int)localOffset);
        byte eventType = (byte)(header & 0xFF);
        ushort entrySize = (ushort)((header >> 16) & 0xFFFF);
        uint timestampDelta = (uint)((header >> 32) & 0xFFFFFFFF);

        if (entrySize == 0) break; // safety

        if (eventType == RuntimeEmitter.DsEvPadding) {
          localOffset += entrySize;
          continue;
        }

        if (eventType == RuntimeEmitter.DsEvDepthInc) {
          depth++;
          localOffset += entrySize;
          continue;
        }
        if (eventType == RuntimeEmitter.DsEvDepthDec) {
          if (depth > 0) depth--;
          localOffset += entrySize;
          continue;
        }

        // Pre-filter before formatting
        if (filter == "sched" && eventType <= RuntimeEmitter.DsEvMmRawFree) {
          localOffset += entrySize;
          continue;
        }
        if (filter == "mm" && eventType >= RuntimeEmitter.DsEvSchedSpawn) {
          localOffset += entrySize;
          continue;
        }

        string? line = FormatEventFromBuffer(eventType, localBuf, (int)localOffset, tagNames);

        if (line != null) {
          string indent = depth < indentCache.Length ? indentCache[depth] : new string(' ', depth * 2);
          lock (stdoutLock) {
            stdout.Write('[');
            stdout.Write('+');
            uint seconds = timestampDelta / 1000;
            uint ms = timestampDelta % 1000;
            stdout.Write(seconds.ToString("D4"));
            stdout.Write('.');
            stdout.Write(ms.ToString("D3"));
            stdout.Write(']');
            stdout.Write(' ');
            stdout.Write(indent);
            stdout.WriteLine(line);
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
    if (totalEvents > 0 || droppedEvents > 0) {
      double peakMB = peakUsed / (1024.0 * 1024.0);
      double bufMB = bufferSize / (1024.0 * 1024.0);
      int peakPct = bufferSize > 0 ? (int)(peakUsed * 100 / bufferSize) : 0;
      Console.Error.WriteLine($"[debugstream] {totalEvents} events, {droppedEvents} dropped, peak buffer: {peakMB:F1} MB / {bufMB:F1} MB ({peakPct}%)");
    }

    return process.ExitCode;
  }

  /// <summary>
  /// Parse the PE executable to find the .symtab section, then scan for the
  /// MXDS_TAGS magic to locate the packed tag table blob.
  /// </summary>
  private static string[] ReadTagsFromExecutable(string exePath) {
    try {
      using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
      using var reader = new BinaryReader(fs);

      // DOS header: e_lfanew at offset 0x3C gives PE signature offset
      fs.Seek(0x3C, SeekOrigin.Begin);
      var peOffset = reader.ReadUInt32();

      // PE signature (4 bytes) + COFF header (20 bytes)
      fs.Seek(peOffset + 4, SeekOrigin.Begin);
      var numberOfSections = reader.ReadUInt16();  // offset +2 in COFF header: NumberOfSections
      fs.Seek(14, SeekOrigin.Current);  // skip rest of COFF header (Machine already read as part of seek)

      // Optional header size is at COFF offset +16, but we already read past it.
      // Back up: COFF header is 20 bytes starting at peOffset+4.
      // We read 2 bytes (NumberOfSections), skipped 14 = 16 bytes into COFF.
      // SizeOfOptionalHeader is at COFF+16, which is current position.
      var sizeOfOptionalHeader = reader.ReadUInt16();
      fs.Seek(2, SeekOrigin.Current); // skip Characteristics

      // Skip optional header to reach section headers
      fs.Seek(sizeOfOptionalHeader, SeekOrigin.Current);

      // Read section headers (40 bytes each)
      for (int i = 0; i < numberOfSections; i++) {
        var nameBytes = reader.ReadBytes(8);
        var name = System.Text.Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
        var virtualSize = reader.ReadUInt32();
        var virtualAddress = reader.ReadUInt32();
        var rawDataSize = reader.ReadUInt32();
        var rawDataPointer = reader.ReadUInt32();
        fs.Seek(16, SeekOrigin.Current); // skip remaining section header fields

        if (name == ".symtab") {
          return ScanForTagBlob(fs, reader, rawDataPointer, rawDataSize);
        }
      }
    } catch {
      // PE parsing failed — return empty tag table
    }
    return [];
  }

  /// <summary>
  /// Scan the .symtab section bytes for the MXDS_TAGS magic prefix and decode the tag table.
  /// </summary>
  private static string[] ScanForTagBlob(FileStream fs, BinaryReader reader, uint sectionOffset, uint sectionSize) {
    var magic = RuntimeEmitter.DsTagTableMagic;
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
      int offset = pos + magic.Length;
      if (offset + 2 > sectionData.Length) return [];

      ushort count = BitConverter.ToUInt16(sectionData, offset);
      offset += 2;

      var names = new string[count];
      for (int i = 0; i < count && offset + 2 <= sectionData.Length; i++) {
        ushort len = BitConverter.ToUInt16(sectionData, offset);
        offset += 2;
        if (offset + len > sectionData.Length) break;
        names[i] = System.Text.Encoding.UTF8.GetString(sectionData, offset, len);
        offset += len;
      }
      return names;
    }

    return [];
  }

  private static string ResolveTag(int tagIndex, string[] tagNames) {
    if (tagIndex > 0 && tagIndex < tagNames.Length && !string.IsNullOrEmpty(tagNames[tagIndex]))
      return tagNames[tagIndex];
    return $"tag={tagIndex}";
  }

  private static string? FormatEventFromBuffer(byte eventType, byte[] buf, int offset, string[] tagNames) {
    switch (eventType) {
      case RuntimeEmitter.DsEvMmAlloc: {
        long allocId = BitConverter.ToInt64(buf, offset + 8);
        long tagAndSize = BitConverter.ToInt64(buf, offset + 16);
        int tagIndex = (int)(tagAndSize & 0xFFFF);
        int size = (int)((tagAndSize >> 32) & 0xFFFFFFFF);
        return $"mm_alloc {ResolveTag(tagIndex, tagNames)} #{allocId} size={size}";
      }
      case RuntimeEmitter.DsEvMmFree: {
        long allocId = BitConverter.ToInt64(buf, offset + 8);
        long tagField = BitConverter.ToInt64(buf, offset + 16);
        int tagIndex = (int)(tagField & 0xFFFF);
        return $"mm_free {ResolveTag(tagIndex, tagNames)} #{allocId}";
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
        long allocId = BitConverter.ToInt64(buf, offset + 8);
        long tagAndRc = BitConverter.ToInt64(buf, offset + 16);
        int tagIndex = (int)(tagAndRc & 0xFFFF);
        int rc = (int)((tagAndRc >> 32) & 0xFFFFFFFF);
        return $"{name} {ResolveTag(tagIndex, tagNames)} #{allocId} rc={rc}";
      }
      case RuntimeEmitter.DsEvMmRawAlloc: {
        long rawId = BitConverter.ToInt64(buf, offset + 8);
        long size = BitConverter.ToInt64(buf, offset + 16);
        return $"mm_raw_alloc #{rawId} size={size}";
      }
      case RuntimeEmitter.DsEvMmRawFree: {
        long rawId = BitConverter.ToInt64(buf, offset + 8);
        return $"mm_raw_free #{rawId}";
      }
      case RuntimeEmitter.DsEvMmRealloc: {
        long allocId = BitConverter.ToInt64(buf, offset + 8);
        long tagAndSize = BitConverter.ToInt64(buf, offset + 16);
        int tagIndex = (int)(tagAndSize & 0xFFFF);
        int size = (int)((tagAndSize >> 32) & 0xFFFFFFFF);
        return $"mm_realloc {ResolveTag(tagIndex, tagNames)} #{allocId} size={size}";
      }
      case RuntimeEmitter.DsEvMmCow: {
        long allocId = BitConverter.ToInt64(buf, offset + 8);
        long tagAndSize = BitConverter.ToInt64(buf, offset + 16);
        int tagIndex = (int)(tagAndSize & 0xFFFF);
        int size = (int)((tagAndSize >> 32) & 0xFFFFFFFF);
        return $"mm_cow {ResolveTag(tagIndex, tagNames)} #{allocId} size={size}";
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
        long traceId = BitConverter.ToInt64(buf, offset + 8);
        return $"{name} #{traceId}";
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
}
