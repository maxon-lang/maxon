using System.Globalization;
using MaxonSharp.Compiler;

namespace MaxonSharp;

/// <summary>
/// ONE ADVISORY LOCK PER CHECKOUT, held by the commands that write the checkout's SHARED build
/// products, so two of them can never run in one tree at once.
///
/// <para>⭐ <b>THE RULE THIS REPLACES WAS PROSE, AND PROSE IS NOT A GATE.</b> Both `.claude/CLAUDE.md`
/// and the rung skill carried a sentence — <i>"NEVER run two suites in one tree at once"</i> — that a
/// second agent cannot see and a timed-out MCP call cannot honour. The race that filed this one is the
/// bootstrap's own: <c>maxon build maxon-shv2</c> twice into <c>maxon-shv2/.maxon/</c>, which produced
/// a 12-minute build printing nothing but its <c>error-codes: OK</c> line and then a silent exit 1 —
/// and made a reviewer report the change under review as a compile-time regression. <b>A race that
/// manifests as SLOW is worse than one that manifests as WRONG, because slow gets attributed to the
/// change under test.</b></para>
///
/// <para>⚠ <b>THIS FILE AND <c>maxon-shv2/Compiler/TreeLock.maxon</c> ARE ONE MECHANISM IN TWO
/// LANGUAGES, and they must stay agreeing.</b> They have to be: <c>build maxon-shv2</c> is a BOOTSTRAP
/// command and <c>spec-test</c> is an shv2 one, so the two processes that contend are not the same
/// program. Same file name, same field names, same intervals. What keeps the pair from drifting is
/// that only ONE field is ever parsed — <c>token</c>, and only to answer "is this still mine" — while
/// every refusal QUOTES the record it found rather than reformatting it. A field added on either side
/// is therefore displayed by the other with no edit here at all.</para>
///
/// <para>That Maxon file carries the full argument: what the lock covers, why the checkout rather than
/// the output directory, why a heartbeat rather than a pid probe, and what the read-back in
/// <see cref="Acquire"/> can and cannot exclude. It is not restated here, because a rationale written
/// down twice is this project's signature bug.</para>
/// </summary>
internal static class TreeLock {
  /// <summary>Where the record lives, relative to the checkout root. Gitignored.</summary>
  private const string LockFileName = ".maxon-tree.lock";

  private const string PidField = "pid";
  private const string TokenField = "token";
  private const string SinceField = "sinceUnix";
  private const string HeldField = "heldSeconds";
  private const string ArgvField = "argv";
  private const char FieldSeparator = '=';
  private const string RecordLineSeparator = "\n";

  /// <summary>
  /// How often the holder rewrites the record, and how long without one before it is treated as
  /// abandoned. The gap between them is the whole margin — see the Maxon side for the reasoning; both
  /// numbers must match it, because a holder written by one and read by the other has to agree about
  /// what "still moving" means.
  /// </summary>
  private const int TouchIntervalMs = 5000;
  private const int AbandonedAfterSeconds = 60;

  /// <summary>The pause between writing the record and reading it back.</summary>
  private const int AcquireVerifyDelayMs = 20;

  /// <summary>
  /// The exit code that means NOTHING RAN — distinct from 1 (ran and failed) so a caller reading only
  /// the number can tell them apart. Same value and same meaning as the shv2 driver's
  /// <c>NothingRanExitCode</c> and as <c>cross-target-gate.sh</c>'s staleness refusal.
  /// </summary>
  public const int NothingRanExitCode = 2;

  private static readonly Lock _gate = new();

  /// <summary>The record this process WROTE, or null when it wrote none.</summary>
  private static string? _ownedPath;
  private static string? _ownedToken;
  private static long _ownedSinceUnix;
  private static long _lastTouchTicks;

  /// <summary>
  /// Take the lock on the checkout containing <paramref name="operandPath"/>, or REFUSE. Returns the
  /// exit code the command should return: 0 to go ahead, <see cref="NothingRanExitCode"/> to stop.
  /// A refusal is IMMEDIATE and never a wait.
  /// </summary>
  public static int Acquire(string operandPath) {
    var lockPath = LockPathFor(operandPath);
    if (lockPath == null) return 0;  // no checkout above it: nothing shared to guard

    var existing = ReadRecord(lockPath);
    if (existing != null) {
      if (existing.UntouchedSeconds <= AbandonedAfterSeconds) return ReportBusy(lockPath, existing);

      // ⭐ BREAKING A LOCK IS EXPLICIT AND LOGGED, NEVER SILENT — if this ever breaks a lock whose
      // holder was merely slow, this line is the only evidence that would explain the two runs after it.
      Console.Error.WriteLine($"warning: breaking an ABANDONED tree lock at {lockPath} — nothing has "
        + $"touched it for {existing.UntouchedSeconds} s (a live holder rewrites it every {TouchIntervalMs} ms), "
        + "so the process that took it is gone or has stopped making progress. It said:");
      Console.Error.WriteLine(Indented(existing.Text));
    }

    return Take(lockPath);
  }

  /// <summary>
  /// Say the holder is still working. Throttled to one write every <see cref="TouchIntervalMs"/>, so
  /// it is cheap enough for the compile pipeline to call at every stage boundary — which is what makes
  /// it cover a long <c>build</c> and every in-process spec-test compile from ONE site. Thread-safe:
  /// the suite compiles on worker threads.
  /// </summary>
  public static void Touch() {
    lock (_gate) {
      if (_ownedPath == null) return;
      if (Environment.TickCount64 - _lastTouchTicks < TouchIntervalMs) return;
      WriteRecord(_ownedPath, _ownedToken!, _ownedSinceUnix, NowUnixSeconds() - _ownedSinceUnix);
      _lastTouchTicks = Environment.TickCount64;
    }
  }

  /// <summary>
  /// Give the lock up. A no-op for a process that never took one.
  ///
  /// <para>⚠ It removes the file only if the file is still MINE: a holder that stalled long enough to
  /// be declared abandoned has had its lock broken and RETAKEN, and deleting the path blindly would
  /// then hand the tree to a third process.</para>
  /// </summary>
  public static void Release() {
    lock (_gate) {
      if (_ownedPath == null) return;
      if (RecordCarriesToken(_ownedPath, _ownedToken!)) {
        try { File.Delete(_ownedPath); } catch (IOException) { /* already gone: nothing to release */ }
                                        catch (UnauthorizedAccessException) { /* ditto */ }
      }
      _ownedPath = null;
      _ownedToken = null;
    }
  }

  /// <summary>
  /// The lock file for the checkout containing <paramref name="operandPath"/>, or null when there is
  /// no <c>stdlib/</c> above it. The path is made absolute first: <c>maxon build maxon-shv2</c> names
  /// its project relatively, and a relative name has no directory chain to walk up.
  /// </summary>
  private static string? LockPathFor(string operandPath) {
    var stdlibDir = StdlibLoader.FindStdlibPath(Path.GetFullPath(operandPath));
    if (stdlibDir == null) return null;
    var checkoutRoot = Path.GetDirectoryName(stdlibDir);
    return checkoutRoot == null ? null : Path.Combine(checkoutRoot, LockFileName);
  }

  /// <summary>A record found on disk: its own bytes, and how long ago it was last touched.</summary>
  private sealed record FoundRecord(string Text, long UntouchedSeconds);

  private static FoundRecord? ReadRecord(string lockPath) {
    try {
      var text = File.ReadAllText(lockPath);
      var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(lockPath), TimeSpan.Zero).ToUnixTimeSeconds();
      return new FoundRecord(text, Math.Max(0, NowUnixSeconds() - modified));
    } catch (IOException) {
      return null;      // it went away between the two calls, which is a release or a break
    } catch (UnauthorizedAccessException) {
      return null;
    }
  }

  /// <summary>
  /// Write this process's record, then read it back to make sure it is the one that stuck — see the
  /// Maxon side for why the pause between the two is what makes that mutual exclusion rather than a
  /// formality, and for the residual it leaves.
  /// </summary>
  private static int Take(string lockPath) {
    var sinceUnix = NowUnixSeconds();
    var token = $"{Environment.ProcessId}-{sinceUnix}-{Environment.TickCount64}";

    if (!WriteRecord(lockPath, token, sinceUnix, heldSeconds: 0)) {
      Console.Error.WriteLine($"error: could not write this checkout's tree lock at {lockPath}. It guards the "
        + "shared output directories two commands would otherwise corrupt, so nothing was run.");
      return NothingRanExitCode;
    }

    Thread.Sleep(AcquireVerifyDelayMs);

    if (!RecordCarriesToken(lockPath, token)) {
      Console.Error.WriteLine("error: another maxon command claimed this checkout's tree lock at the same "
        + "instant, and it won. Nothing was run — re-run once it has finished.");
      return NothingRanExitCode;
    }

    lock (_gate) {
      _ownedPath = lockPath;
      _ownedToken = token;
      _ownedSinceUnix = sinceUnix;
      _lastTouchTicks = Environment.TickCount64;
    }
    return 0;
  }

  /// <summary>
  /// The record's bytes. <c>argv</c> is the whole command line, because "which of my four terminals is
  /// this?" is the question a reader actually has. A newline inside an argument would forge a field,
  /// so it is flattened.
  /// </summary>
  private static bool WriteRecord(string lockPath, string token, long sinceUnix, long heldSeconds) {
    var argv = string.Join(' ', Environment.GetCommandLineArgs()).Replace("\r", " ").Replace("\n", " ");
    var text = string.Concat(
      Field(PidField, Environment.ProcessId.ToString(CultureInfo.InvariantCulture)),
      Field(TokenField, token),
      Field(SinceField, sinceUnix.ToString(CultureInfo.InvariantCulture)),
      Field(HeldField, heldSeconds.ToString(CultureInfo.InvariantCulture)),
      Field(ArgvField, argv));

    try {
      File.WriteAllText(lockPath, text);
      return true;
    } catch (IOException) {
      return false;
    } catch (UnauthorizedAccessException) {
      return false;
    }
  }

  private static string Field(string key, string value) => $"{key}{FieldSeparator}{value}{RecordLineSeparator}";

  /// <summary>
  /// Does the record still name <paramref name="token"/> as its holder? The ONE field either compiler
  /// parses out of the other's record — everything else is echoed.
  /// </summary>
  private static bool RecordCarriesToken(string lockPath, string token) {
    var found = ReadRecord(lockPath);
    if (found == null) return false;
    var prefix = $"{TokenField}{FieldSeparator}";
    foreach (var line in found.Text.Split('\n')) {
      // The value may itself contain '=', so the split is at the FIRST separator only.
      if (line.StartsWith(prefix, StringComparison.Ordinal)) return line[prefix.Length..] == token;
    }
    return false;
  }

  /// <summary>
  /// ⭐ The refusal. It names the holder, says when it last moved, quotes the record whole, and says
  /// what to do — the four things a reader needs and the four a bare "locked" sends them off to
  /// re-derive.
  /// </summary>
  private static int ReportBusy(string lockPath, FoundRecord existing) {
    Console.Error.WriteLine("error: this checkout is BUSY — another maxon command holds its tree lock, and two "
      + "of them in one tree corrupt each other's output directories. Nothing was run.");
    Console.Error.WriteLine($"  lock:  {lockPath}");
    Console.Error.WriteLine("  held by:");
    Console.Error.WriteLine(Indented(existing.Text));
    Console.Error.WriteLine($"  last progress: {existing.UntouchedSeconds} s ago (a live holder rewrites the "
      + $"record every {TouchIntervalMs} ms)");
    Console.Error.WriteLine($"  Wait for it to finish, or end that process. A holder that stops making progress "
      + $"for {AbandonedAfterSeconds} s is treated as abandoned and its lock is broken automatically, with a "
      + "line saying so.");
    return NothingRanExitCode;
  }

  private static string Indented(string record) =>
    string.Join('\n', record.Split('\n')
      .Where(line => line.Length > 0)
      .Select(line => $"    {line}"));

  private static long NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
