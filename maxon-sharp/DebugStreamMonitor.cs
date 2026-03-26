using System.IO.MemoryMappedFiles;
using MaxonSharp.Compiler.Mlir.Runtime;

namespace MaxonSharp;

/// <summary>
/// Shared-memory debug stream monitor. Creates named shared memory,
/// spawns the target process with --debugstream=&lt;name&gt; in its environment,
/// reads binary events from the ring buffer, and formats them as text.
/// </summary>
public class DebugStreamMonitor {

  public int Run(string[] args) {
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
    var startTs = accessor.ReadInt64(RuntimeEmitter.DsOffStartTimestamp);

    // Read tag names from the executable's .symtab section
    string[] tagNames = ReadTagsFromExecutable(Path.GetFullPath(exePath));

    // Forward stdout/stderr in background
    var stdoutTask = Task.Run(() => {
      var sr = process.StandardOutput;
      while (sr.ReadLine() is { } line)
        Console.WriteLine(line);
    });
    var stderrTask = Task.Run(() => {
      var sr = process.StandardError;
      while (sr.ReadLine() is { } line)
        Console.Error.WriteLine(line);
    });

    while (!process.HasExited || readCursor < accessor.ReadInt64(RuntimeEmitter.DsOffWriteCursor)) {
      long writeCursor = accessor.ReadInt64(RuntimeEmitter.DsOffWriteCursor);

      if (readCursor >= writeCursor) {
        Thread.Sleep(1);
        continue;
      }

      while (readCursor < writeCursor) {
        long pos = readCursor & bufferMask;
        long dataOffset = RuntimeEmitter.DsHeaderSize + pos;

        // Read entry header (8 bytes)
        long header = accessor.ReadInt64(dataOffset);
        byte eventType = (byte)(header & 0xFF);
        ushort entrySize = (ushort)((header >> 16) & 0xFFFF);
        uint timestampDelta = (uint)((header >> 32) & 0xFFFFFFFF);

        if (entrySize == 0) break; // safety

        if (eventType == RuntimeEmitter.DsEvPadding) {
          readCursor += entrySize;
          continue;
        }

        string timestamp = FormatTimestamp(timestampDelta);

        if (eventType == RuntimeEmitter.DsEvDepthInc) {
          depth++;
          readCursor += entrySize;
          continue;
        }
        if (eventType == RuntimeEmitter.DsEvDepthDec) {
          if (depth > 0) depth--;
          readCursor += entrySize;
          continue;
        }

        string indent = new string(' ', depth * 2);
        string? line = FormatEvent(eventType, dataOffset, accessor, filter, tagNames);

        if (line != null) {
          Console.WriteLine($"{timestamp} {indent}{line}");
        }

        readCursor += entrySize;
      }

      // Update read cursor so producer knows we've consumed
      accessor.Write(RuntimeEmitter.DsOffReadCursor, readCursor);
    }

    // Wait for process exit
    process.WaitForExit();
    stdoutTask.Wait();
    stderrTask.Wait();

    // Final summary
    long totalEvents = accessor.ReadInt64(RuntimeEmitter.DsOffTotalEvents);
    long droppedEvents = accessor.ReadInt64(RuntimeEmitter.DsOffDroppedEvents);
    if (totalEvents > 0 || droppedEvents > 0) {
      Console.Error.WriteLine($"[debugstream] {totalEvents} events, {droppedEvents} dropped");
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

  private static string FormatTimestamp(uint deltaMs) {
    uint seconds = deltaMs / 1000;
    uint ms = deltaMs % 1000;
    return $"[+{seconds:D4}.{ms:D3}]";
  }

  private static string? FormatEvent(byte eventType, long dataOffset, MemoryMappedViewAccessor accessor, string? filter, string[] tagNames) {
    switch (eventType) {
      case RuntimeEmitter.DsEvMmAlloc: {
        if (filter == "sched") return null;
        long allocId = accessor.ReadInt64(dataOffset + 8);
        long tagAndSize = accessor.ReadInt64(dataOffset + 16);
        int tagIndex = (int)(tagAndSize & 0xFFFF);
        int size = (int)((tagAndSize >> 32) & 0xFFFFFFFF);
        return $"mm_alloc {ResolveTag(tagIndex, tagNames)} #{allocId} size={size}";
      }
      case RuntimeEmitter.DsEvMmFree: {
        if (filter == "sched") return null;
        long allocId = accessor.ReadInt64(dataOffset + 8);
        long tagField = accessor.ReadInt64(dataOffset + 16);
        int tagIndex = (int)(tagField & 0xFFFF);
        return $"mm_free {ResolveTag(tagIndex, tagNames)} #{allocId}";
      }
      case RuntimeEmitter.DsEvMmIncref:
      case RuntimeEmitter.DsEvMmDecref:
      case RuntimeEmitter.DsEvMmTransfer: {
        if (filter == "sched") return null;
        string name = eventType switch {
          RuntimeEmitter.DsEvMmIncref => "mm_incref",
          RuntimeEmitter.DsEvMmDecref => "mm_decref",
          RuntimeEmitter.DsEvMmTransfer => "mm_transfer",
          _ => throw new InvalidOperationException($"Unexpected refcount event type: 0x{eventType:X2}")
        };
        long allocId = accessor.ReadInt64(dataOffset + 8);
        long tagAndRc = accessor.ReadInt64(dataOffset + 16);
        int tagIndex = (int)(tagAndRc & 0xFFFF);
        int rc = (int)((tagAndRc >> 32) & 0xFFFFFFFF);
        return $"{name} {ResolveTag(tagIndex, tagNames)} #{allocId} rc={rc}";
      }
      case RuntimeEmitter.DsEvMmRawAlloc: {
        if (filter == "sched") return null;
        long rawId = accessor.ReadInt64(dataOffset + 8);
        long size = accessor.ReadInt64(dataOffset + 16);
        return $"mm_raw_alloc #{rawId} size={size}";
      }
      case RuntimeEmitter.DsEvMmRawFree: {
        if (filter == "sched") return null;
        long rawId = accessor.ReadInt64(dataOffset + 8);
        return $"mm_raw_free #{rawId}";
      }
      case RuntimeEmitter.DsEvSchedSpawn:
      case RuntimeEmitter.DsEvSchedAwait:
      case RuntimeEmitter.DsEvSchedYield:
      case RuntimeEmitter.DsEvSchedResume:
      case RuntimeEmitter.DsEvIoYield:
      case RuntimeEmitter.DsEvIoResume: {
        if (filter == "mm") return null;
        string name = eventType switch {
          RuntimeEmitter.DsEvSchedSpawn => "sched_spawn",
          RuntimeEmitter.DsEvSchedAwait => "sched_await",
          RuntimeEmitter.DsEvSchedYield => "sched_yield",
          RuntimeEmitter.DsEvSchedResume => "sched_resume",
          RuntimeEmitter.DsEvIoYield => "io_yield",
          RuntimeEmitter.DsEvIoResume => "io_resume",
          _ => throw new InvalidOperationException($"Unexpected sched event type: 0x{eventType:X2}")
        };
        long traceId = accessor.ReadInt64(dataOffset + 8);
        return $"{name} #{traceId}";
      }
      case RuntimeEmitter.DsEvHeartbeat:
        return null;
      default:
        return $"unknown_event(0x{eventType:X2})";
    }
  }
}
