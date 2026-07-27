using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MaxonSharp.Testing;

/// <summary>
/// How a launched process finished. This is the discriminator callers branch on:
/// <see cref="ProcessRunResult.ExitCode"/> is only a real exit code when the outcome
/// is <see cref="Exited"/>, so reading the code without reading this is how a killed
/// child gets mistaken for one that returned a value.
/// </summary>
internal enum ProcessRunOutcome {
  /// <summary>The child ran to completion; <see cref="ProcessRunResult.ExitCode"/> is its own.</summary>
  Exited,

  /// <summary>
  /// The child outlived its timeout and was killed (with its tree), so it never produced an exit
  /// code of its own.
  ///
  /// Its output is whatever it had written before it died — a PREFIX, not a complete stream, and
  /// possibly cut mid-line. That prefix is reported rather than discarded because how far the child
  /// got is usually the only evidence of what it was doing when the deadline expired.
  /// </summary>
  TimedOut,

  /// <summary>
  /// The child exited — so <see cref="ProcessRunResult.ExitCode"/> is genuinely its own —
  /// but its stdout/stderr never finished draining, so the captured text is incomplete
  /// and is reported as empty rather than as a truncated prefix that reads like real output.
  /// </summary>
  OutputReadTimedOut,
}

/// <summary>
/// The result of one launch: what happened, what the child returned, and what it wrote.
/// </summary>
internal readonly record struct ProcessRunResult(
  ProcessRunOutcome Outcome,
  int ExitCode,
  string Stdout,
  string Stderr) {

  /// <summary>
  /// Whether the child's exit code is the one <c>mm_leak_check</c> substitutes when an
  /// allocation is still live at exit. Named rather than compared inline so a caller
  /// cannot spell the leak check as a bare <c>== 101</c>.
  /// </summary>
  public bool IsMemoryLeak => ExitCode == ProcessLauncher.MemoryLeakExitCode;
}

/// <summary>
/// Arguments for a launch, in exactly one of two forms — which is the point of the type.
/// A launcher that exposed both a command-line string and an argv list as separate settable
/// properties would let a caller populate both, and <see cref="ProcessStartInfo"/> throws
/// when it finds both set.
/// </summary>
internal readonly struct ProcessArguments {
  private readonly string? _commandLine;
  private readonly IReadOnlyList<string>? _argv;

  private ProcessArguments(string? commandLine, IReadOnlyList<string>? argv) {
    _commandLine = commandLine;
    _argv = argv;
  }

  /// <summary>Launch with no arguments.</summary>
  public static ProcessArguments None => default;

  /// <summary>
  /// argv form: one element per argument, quoted by the runtime as needed. Prefer this —
  /// it is the form that cannot be broken by a path containing a space.
  /// </summary>
  public static ProcessArguments Of(params string[] arguments) => new(null, arguments);

  /// <summary>
  /// A pre-formed command line, passed to the child verbatim. This exists for arguments
  /// that arrive as one already-quoted string an author wrote by hand (a spec's `Args:`
  /// line), where re-splitting into argv would silently reinterpret their quoting.
  /// A null or empty line means no arguments.
  /// </summary>
  public static ProcessArguments Verbatim(string? commandLine) =>
    string.IsNullOrEmpty(commandLine) ? None : new(commandLine, null);

  internal void ApplyTo(ProcessStartInfo startInfo) {
    // Assign at most one of the two — see the type's remarks.
    if (_commandLine != null) {
      startInfo.Arguments = _commandLine;
      return;
    }

    if (_argv == null) return;

    foreach (var argument in _argv) {
      startInfo.ArgumentList.Add(argument);
    }
  }
}

/// <summary>
/// One launch to perform. Everything optional is optional because a real caller omits it:
/// the mm-trace monitor inherits the runner's working directory, and a plain test binary
/// needs no environment of its own.
/// </summary>
internal sealed class ProcessLaunchRequest {
  /// <summary>Path to the binary to spawn. Used as given — the caller decides whether to resolve it.</summary>
  public required string ExecutablePath { get; init; }

  public ProcessArguments Arguments { get; init; } = ProcessArguments.None;

  /// <summary>Working directory for the child; null inherits this process's.</summary>
  public string? WorkingDirectory { get; init; }

  /// <summary>
  /// Environment variables to set on the child, on top of the inherited environment.
  /// </summary>
  public IReadOnlyDictionary<string, string>? EnvironmentOverrides { get; init; }

  /// <summary>
  /// How long the child may run before it is killed. Required: there is no default here
  /// on purpose, because "how long is too long" is the calling harness's policy and a
  /// launcher that guessed it would be silently overriding one of them.
  /// </summary>
  public required int TimeoutMs { get; init; }
}

/// <summary>
/// Spawns child processes with their output captured and their lifetime bounded, for the
/// test harnesses that run compiled Maxon binaries. Two things here are not conveniences:
///
/// - The <see cref="RunnerJob"/> is what stops an interrupted run from orphaning children.
///   A zombie test binary keeps a file lock on its own .exe, which fails the *next* compile
///   with `E9001: The process cannot access the file ... because it is being used by
///   another process` — a failure that reads as a compiler bug and is not one.
/// - Both streams are read asynchronously. A child that fills the stdout pipe buffer while
///   the parent is blocked reading stderr deadlocks, and does so only for outputs past the
///   buffer size, which is to say rarely and unreproducibly.
/// </summary>
internal static class ProcessLauncher {
  /// <summary>
  /// Exit code mm_leak_check substitutes for the program's own when any managed or raw
  /// allocation is still live at process exit (see RuntimeEmitter.EmitMmLeakCheck).
  /// </summary>
  public const int MemoryLeakExitCode = 101;

  /// <summary>
  /// Stand-in exit code reported when the child was killed for exceeding its timeout and
  /// so never returned one. <see cref="ProcessRunResult.Outcome"/> is what says the code
  /// is synthetic; this value exists so the field has something in it, not as a signal.
  /// </summary>
  private const int NoExitCodeFromKilledProcess = -1;

  /// <summary>
  /// How long to let the abandoned stream reads finish after a timeout kill. The child is
  /// already dead, so this only lets the reader tasks observe EOF and retire rather than
  /// being left dangling.
  /// </summary>
  private const int PostKillDrainMs = 1000;

  /// <summary>
  /// How long to wait for both streams to drain after a clean exit. Generous, because
  /// exceeding it means the captured output is incomplete and the run is reported as
  /// <see cref="ProcessRunOutcome.OutputReadTimedOut"/>.
  /// </summary>
  private const int OutputDrainMs = 3000;

  /// <summary>Message reported as the child's stderr when it was killed for running too long.</summary>
  private const string TimedOutMessage = "Process timed out";

  /// <summary>Message reported as the child's stderr when its output never finished draining.</summary>
  private const string OutputReadTimedOutMessage = "Stream read timed out";

  /// <summary>
  /// Process-lifetime job object. Every binary spawned through this launcher is enrolled
  /// here so that if the launching process dies for any reason (Ctrl-C, crash, debugger
  /// detach, OS forcibly terminating it), the OS closes our handle to the job, which —
  /// because of `KILL_ON_JOB_CLOSE` — terminates every child still in it.
  ///
  /// Lazy-initialized so non-Windows hosts (where the job is a no-op) and commands that
  /// never spawn anything don't allocate it. Process-wide because every harness in the
  /// process should share the one safety net, and because static lifetime matches
  /// "launcher dies → handle freed" most cleanly: the field is reachable until exit.
  /// </summary>
  private static readonly Lazy<WindowsJobObject> _runnerJobLazy = new(() => new WindowsJobObject());
  private static WindowsJobObject RunnerJob => _runnerJobLazy.Value;

  /// <summary>
  /// Start a child with stdout/stderr redirected and UTF-8-decoded and no console window,
  /// enroll it in the process-lifetime job, read both streams asynchronously (to avoid
  /// pipe-buffer deadlocks), and wait with a timeout. On timeout the child and its tree
  /// are killed and the streams are abandoned after a short drain.
  /// </summary>
  public static ProcessRunResult Run(ProcessLaunchRequest request) {
    ArgumentNullException.ThrowIfNull(request);

    var startInfo = new ProcessStartInfo {
      FileName = request.ExecutablePath,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8,
    };
    request.Arguments.ApplyTo(startInfo);

    if (request.WorkingDirectory != null) {
      startInfo.WorkingDirectory = request.WorkingDirectory;
    }

    if (request.EnvironmentOverrides != null) {
      foreach (var (name, value) in request.EnvironmentOverrides) {
        startInfo.EnvironmentVariables[name] = value;
      }
    }

    // A failed spawn has no result to report, so it throws rather than returning some
    // stand-in exit code that a caller would have to recognize.
    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException($"Failed to start process: {request.ExecutablePath}");

    if (!RunnerJob.AssignProcess(process.Handle)) {
      Logger.Debug(LogCategory.Testing, $"AssignProcessToJobObject failed for {startInfo.FileName} (errno {Marshal.GetLastWin32Error()})");
    }

    // Read stdout/stderr asynchronously to avoid deadlocks
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();

    bool exited = process.WaitForExit(request.TimeoutMs);
    if (!exited) {
      // Process timed out - kill it, then collect whatever it managed to write.
      try { process.Kill(entireProcessTree: true); } catch { }
      try { process.Kill(); } catch { }

      // Killing the child closes its pipes, so the readers reach EOF and hand back everything it
      // wrote BEFORE it died. That PREFIX is kept rather than discarded: it used to be thrown away
      // as "abandoned mid-read", which is true of the stream and false of the bytes — and it is the
      // only record of how far the child got. `maxon test` reads exactly that to say WHICH test was
      // running when the deadline expired; with it discarded, a killed shard looked identical to one
      // that never started, and the timed-out test was reported as "did not run".
      //
      // A drain that does not finish, or a reader that FAULTED on the pipe the kill just closed,
      // leaves the text incomplete in a way nothing downstream can detect — so those cases report
      // the reason instead of a prefix of unknown length. The fault is caught rather than allowed to
      // escape as an AggregateException: this is called on worker threads, where an unhandled
      // exception ends the process.
      //
      // EVERY caller must branch on Outcome. A killed child's output looks exactly like a complete
      // one otherwise, and comparing it against an expectation is how a hanging program passes.
#pragma warning disable VSTHRD002
      try {
        bool drained = Task.WaitAll([stdoutTask, stderrTask], PostKillDrainMs);
        return drained
          ? new ProcessRunResult(ProcessRunOutcome.TimedOut, NoExitCodeFromKilledProcess,
              stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult())
          : new ProcessRunResult(ProcessRunOutcome.TimedOut, NoExitCodeFromKilledProcess, "", TimedOutMessage);
      } catch (Exception ex) when (ex is AggregateException or IOException or ObjectDisposedException) {
        return new ProcessRunResult(ProcessRunOutcome.TimedOut, NoExitCodeFromKilledProcess, "", TimedOutMessage);
      }
#pragma warning restore VSTHRD002
    }

    // Process exited normally - wait for async reads to complete with a timeout
    // to guard against edge cases where streams aren't fully drained
#pragma warning disable VSTHRD002
    if (!Task.WaitAll([stdoutTask, stderrTask], OutputDrainMs))
      return new ProcessRunResult(ProcessRunOutcome.OutputReadTimedOut, process.ExitCode, "", OutputReadTimedOutMessage);
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    return new ProcessRunResult(ProcessRunOutcome.Exited, process.ExitCode, stdout, stderr);
  }
}
