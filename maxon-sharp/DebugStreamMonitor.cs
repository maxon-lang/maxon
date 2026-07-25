using MaxonSharp.Compiler.Ir.Runtime;

namespace MaxonSharp;

/// <summary>
/// `maxon monitor` — the LIVE TIMELINE face of the DebugStream ring. It creates the shared segment,
/// spawns the target with <c>MAXON_DEBUGSTREAM</c> naming it, and prints every decoded event as it
/// arrives, prefixed with the producer's own `+SSSS.mmm` delta and indented by trace depth.
///
/// The wire format is not its business: <see cref="DebugStreamDecoder"/> owns the segment header, the
/// schema handshake, the commit protocol, the interned name tables and every event formatter, and
/// `maxon debug` decodes the same bytes through the same code. What lives here is only what a live
/// timeline needs and a stop-correlated slice does not — the timestamp prefix, the depth indent, the
/// forwarding of the target's own stdout into the same stream, and the closing summary.
/// </summary>
public class DebugStreamMonitor {

  /// How long to idle when the ring has nothing decodable — whether it is empty, or its head entry is
  /// reserved and its producer is still writing the payload.
  private const int PollIntervalMs = 1;

  private static readonly string UsageLine =
    $"Usage: maxon monitor [--filter={string.Join('|', DebugStreamDecoder.Families)}] <exe> [args...]";

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

    // Validated HERE, at the command line, rather than in the decode loop: an unrecognised filter used
    // to silently show EVERY event, which is the opposite of what the user asked for.
    if (filter != null && !DebugStreamDecoder.Families.Contains(filter)) {
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

    using var decoder = DebugStreamDecoder.Create(exePath, filter);

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
    psi.EnvironmentVariables[RuntimeEmitter.DsActivationEnvVar] = decoder.SegmentName;

    var process = new System.Diagnostics.Process { StartInfo = psi };
    process.Start();

    // Buffered output for event lines (avoids per-line Console.WriteLine overhead)
    using var stdout = new StreamWriter(Console.OpenStandardOutput(), bufferSize: 65536);
    stdout.AutoFlush = false;

    // Cached indent strings by depth. Both the cached and the uncached indent are built by
    // Indent(), so the two cannot disagree about how wide a level is.
    var indentCache = new string[MaxCachedIndentDepth];
    for (int i = 0; i < indentCache.Length; i++)
      indentCache[i] = Indent(i);

    // Synchronize writes to stdout between event loop and forwarding task
    var stdoutLock = new object();

    var stdio = TargetStdio.Forward(process,
      line => { lock (stdoutLock) { stdout.WriteLine(line); } },
      Console.Error.WriteLine);

    void Print(DsEvent e) {
      string indent = e.Depth < indentCache.Length ? indentCache[e.Depth] : Indent(e.Depth);
      lock (stdoutLock) {
        stdout.Write('[');
        stdout.Write('+');
        uint seconds = e.TimestampMs / MillisecondsPerSecond;
        uint ms = e.TimestampMs % MillisecondsPerSecond;
        stdout.Write(seconds.ToString("D4"));
        stdout.Write('.');
        stdout.Write(ms.ToString("D3"));
        stdout.Write(']');
        stdout.Write(' ');
        stdout.Write(indent);
        stdout.WriteLine(e.Text);
      }
    }

    while (true) {
      // Snapshot liveness BEFORE the drain reads the cursors. If the producer is already gone by this
      // read, then every store it will ever make is in the ring, so the cursors read next are FINAL.
      bool producerExited = process.HasExited;

      var status = decoder.Drain(producerExited, Print);
      if (status == DebugStreamDecoder.DrainStatus.Decoded) continue;

      lock (stdoutLock) { stdout.Flush(); }

      if (status == DebugStreamDecoder.DrainStatus.SchemaMismatch) {
        Console.Error.WriteLine(decoder.SchemaMismatchMessage);
        // Kill the target rather than leave it running blind into a ring nobody is draining — and drain
        // its stdio on the way out, the same obligation the normal exit below has, because this is the
        // one path where the user is diagnosing a version mismatch and needs every line it managed to
        // produce.
        stdio.EndAndJoin(graceMilliseconds: 0);
        return SchemaMismatchExit;
      }

      if (status == DebugStreamDecoder.DrainStatus.Finished) break;

      if (status != DebugStreamDecoder.DrainStatus.Idle)
        throw new InvalidOperationException($"Unhandled DebugStream drain status {status}");

      // Either the ring is empty, or its head entry is reserved-but-not-yet-committed and its producer
      // is mid-payload. Wait: decoding it now would decode whatever bytes the ring last held there.
      Thread.Sleep(PollIntervalMs);
    }

    // The producer is gone and the ring is drained; wait as long as the target's own exit takes.
    stdio.EndAndJoin(Timeout.Infinite);

    // Final summary
    long totalEvents = decoder.TotalEvents;
    long droppedEvents = decoder.DroppedEvents;
    long abandonedEntries = decoder.AbandonedEntries;
    if (totalEvents > 0 || droppedEvents > 0 || abandonedEntries > 0) {
      long bufferSize = decoder.BufferSize;
      double peakMB = decoder.PeakUsed / (1024.0 * 1024.0);
      double bufMB = bufferSize / (1024.0 * 1024.0);
      int peakPct = bufferSize > 0 ? (int)(decoder.PeakUsed * 100 / bufferSize) : 0;
      // `abandoned` only ever appears when it is non-zero, but it appears LOUDLY when it is: it
      // means the producer was killed mid-entry and that entry's payload is gone for good.
      string abandoned = abandonedEntries > 0
        ? $", {abandonedEntries} abandoned (producer died mid-entry)"
        : "";
      Console.Error.WriteLine($"[debugstream] {totalEvents} events, {droppedEvents} dropped{abandoned}, peak buffer: {peakMB:F1} MB / {bufMB:F1} MB ({peakPct}%)");
    }

    return process.ExitCode;
  }

  /// The timestamp on the wire is a millisecond delta; the trace prints it as `+SSSS.mmm`.
  private const uint MillisecondsPerSecond = 1000;

  // DEPTH_INC / DEPTH_DEC nest the trace. Indents are precomputed up to this depth and built on
  // demand past it — the cache is an optimisation, never a different answer.
  private const int SpacesPerDepthLevel = 2;
  private const int MaxCachedIndentDepth = 64;

  private static string Indent(int depth) => new(' ', depth * SpacesPerDepthLevel);

  /// The monitor's own exit code when it refuses to decode a ring it does not speak the schema of.
  /// Distinct from the target's exit code, which is what a successful run returns.
  private const int SchemaMismatchExit = 3;
}
