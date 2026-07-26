using System.Diagnostics;

namespace MaxonSharp.Debug;

/// <summary>
/// What became of a launched target. <see cref="ExitCode"/> is null when it did not finish, and
/// <see cref="TimedOut"/> says whether the DEADLINE is what stopped it — which matters because that
/// is the one failure here a caller can act on, and only then should either face offer to raise it.
/// Telling someone whose executable does not exist to lengthen a timeout is a refusal that cannot be
/// acted on.
/// </summary>
public readonly record struct TargetRun(int? ExitCode, bool TimedOut, string Error);

/// <summary>
/// Running a program to completion under a deadline, with its output forwarded — the step every
/// measuring command shares.
///
/// It is stated here once because TWO commands now launch a target purely in order to measure it
/// (`maxon coverage run` and `maxon profile run`) and they differ in exactly one thing: what watches the
/// program WHILE it runs. Coverage watches nothing (its counters are written by the program itself);
/// the profiler runs a sampler. Everything else — rooting the path, the serial output gate, the
/// kill/wait/join order, the wording of each refusal — is one fact, and a second copy of it would drift
/// first in the part that is invisible until it fails.
/// </summary>
public static class TargetLauncher {

  /// <summary>
  /// Run <paramref name="exePath"/> to completion, or kill it at <paramref name="timeout"/>.
  ///
  /// The target's own stdout and stderr both go to <paramref name="onOutput"/> — the sink each face
  /// supplies — so neither face has to guess where a chatty program's output belongs.
  ///
  /// ⚠ <paramref name="onOutput"/> is called SERIALLY, and that is this method's promise rather than
  /// each caller's problem. The two forwarding readers run on different thread-pool threads whenever a
  /// program writes to both streams. Without the gate, the MCP face's <c>StringBuilder</c> would be
  /// appended to concurrently — a data structure with no thread safety at all, whose failure is garbled
  /// or truncated output, or an exception on a thread-pool thread, which in .NET takes the whole server
  /// down. The CLI face was safe only by accident, because <c>Console.Error</c> happens to be a
  /// synchronized writer; safety that depends on which sink a caller picked is not safety.
  ///
  /// <paramref name="observe"/> is handed the live process and returns something that is DISPOSED once
  /// the program has ended — however it ended, including the timeout kill. That shape is what stops a
  /// sampler from outliving its target: there is no path out of here that skips the dispose.
  /// </summary>
  public static TargetRun Run(string exePath, IReadOnlyList<string> targetArgs, TimeSpan timeout,
      Action<string> onOutput, Func<Process, IDisposable?>? observe = null,
      IReadOnlyDictionary<string, string>? targetEnv = null) {
    if (!File.Exists(exePath))
      return Failed($"no such executable: {ReportPath.Display(exePath)}");

    // Rooted: Process.Start resolves a relative program name against PATH rather than the working
    // directory, so `dir/prog.exe` would be reported as missing.
    var info = new ProcessStartInfo(Path.GetFullPath(exePath)) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var arg in targetArgs) info.ArgumentList.Add(arg);

    // Applied on top of the inherited environment, the same meaning `maxon debug --target-env=` has:
    // a knob the target reads before `main`, of which MAXON_MAX_PROCS is the one that decides whether a
    // green-thread run is reproducible at all.
    if (targetEnv != null)
      foreach (var (name, value) in targetEnv) info.EnvironmentVariables[name] = value;

    try {
      using var process = Process.Start(info) ?? throw new IOException("the process did not start");
      var outputGate = new object();
      void Deliver(string line) {
        lock (outputGate) onOutput(line);
      }

      using var observer = observe?.Invoke(process);

      // TargetStdio owns the ONE statement of how a session with a spawned target ends: wait out the
      // grace period, kill what is left — the whole process TREE, since a target's children are as
      // much this run's orphans as the target — wait for the kill to settle so the exit status is
      // readable, and only THEN join the forwarding tasks. Joining before the kill blocks forever on
      // a pipe that never closes, which is the other half of the same defect.
      var stdio = TargetStdio.Forward(process, Deliver, Deliver);
      if (stdio.EndAndJoin(PositiveSeconds.ToWaitMilliseconds(timeout))) {
        // The FACT only — each face appends the recourse its own caller can act on.
        return new TargetRun(null, TimedOut: true,
          $"{ReportPath.Display(exePath)} did not finish within {PositiveSeconds.Text(timeout)}s"
          + " and was killed.");
      }

      return new TargetRun(process.ExitCode, TimedOut: false, "");
    } catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) {
      return Failed($"could not run {ReportPath.Display(exePath)}: {ex.Message}");
    }
  }

  private static TargetRun Failed(string error) => new(null, TimedOut: false, error);
}
