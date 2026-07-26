using System.Diagnostics;

namespace MaxonSharp.Debug;

/// <summary>
/// Running a `--coverage` binary and loading what it wrote — the two steps BOTH faces perform, in
/// one place. The CLI (`maxon coverage`) and the MCP family differ only in where the program's own
/// output goes and how a refusal is delivered; everything about the artifacts is here.
///
/// Every refusal is a sentence naming the defect. There is no partially-trusted path: a report is
/// either built from a binary, a sidecar and a data file that all agree about which build they
/// describe, or it is not built at all.
/// </summary>
/// <summary>
/// What became of a launched target. <see cref="ExitCode"/> is null when it did not finish, and
/// <see cref="TimedOut"/> says whether the DEADLINE is what stopped it — which matters because that
/// is the one failure here a caller can act on, and only then should either face offer to raise it.
/// Telling someone whose executable does not exist to lengthen a timeout is a refusal that cannot be
/// acted on.
/// </summary>
public readonly record struct TargetRun(int? ExitCode, bool TimedOut, string Error);

public static class CoverageRunner {
  /// The data file a binary writes on exit, and therefore where a report reads it from. Stated once,
  /// because the emitted runtime derives the same name from the same extension constant.
  public static string DataPathFor(string exePath) => exePath + MxcovFormat.DataExtension;

  /// <summary>
  /// The default bound on a coverage run. Ten minutes, the same shape and the same reason as the
  /// debugger's stop deadline: it exists only so a target that never finishes cannot hang the tool
  /// that launched it — CI, or an MCP host with no observer — and <see cref="TimeoutFlag"/> raises it
  /// for a program that legitimately runs long. Instrumentation itself costs a measured 1.18x on a
  /// realistic workload, so it is not what makes a run approach this.
  /// </summary>
  public static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(10);

  /// The CLI flag that overrides <see cref="DefaultRunTimeout"/>, spelled HERE beside the deadline it
  /// sets so the parser that reads it and the usage text that names it cannot drift apart.
  public const string TimeoutFlag = "--timeout=";

  /// The recourse offered to a human whose deadline was too short, worded ONCE beside the flag it
  /// names. It belongs to the CLI face and not to <see cref="Launch"/>, because the other face has a
  /// different one: telling an MCP caller to pass a command-line flag names a control it does not
  /// have, which is a refusal that cannot be acted on.
  public const string RaiseTimeoutText = "Raise the deadline with " + TimeoutFlag + "<seconds> and run again.";

  /// <see cref="DefaultRunTimeout"/> as the usage banner prints it, through the one seconds rule.
  public static string DefaultRunTimeoutText => PositiveSeconds.Text(DefaultRunTimeout);

  /// <summary>
  /// Run the instrumented program to completion, first removing any stale data file so a run whose
  /// dump never happened cannot be reported from the PREVIOUS run's numbers.
  ///
  /// The target's own stdout and stderr both go to <paramref name="onOutput"/> — the sink each face
  /// supplies — so neither face has to guess where a chatty program's output belongs.
  ///
  /// ⚠ <paramref name="onOutput"/> is called SERIALLY, and that is this method's promise rather than
  /// each caller's problem. `BeginOutputReadLine` and `BeginErrorReadLine` start two INDEPENDENT
  /// async readers, so the two handlers below run on different thread-pool threads at the same time
  /// whenever a program writes to both streams. Without the lock, the MCP face's `StringBuilder`
  /// would be appended to concurrently — a data structure with no thread safety at all, whose failure
  /// is a garbled or truncated `output` field, or an exception on a thread-pool thread, which in .NET
  /// takes the whole server down. The CLI face was safe only by accident, because `Console.Error`
  /// happens to be a synchronized writer; safety that depends on which sink a caller picked is not
  /// safety.
  /// </summary>
  public static TargetRun Launch(string exePath, IReadOnlyList<string> targetArgs, string dataPath,
      TimeSpan timeout, Action<string> onOutput) {
    if (!File.Exists(exePath))
      return Failed($"no such executable: {CoverageRender.DisplayPath(exePath)}");

    try {
      if (File.Exists(dataPath)) File.Delete(dataPath);
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      return Failed($"cannot remove the previous {CoverageRender.DisplayPath(dataPath)}: {ex.Message}");
    }

    // Rooted: Process.Start resolves a relative program name against PATH rather than the working
    // directory, so `maxon coverage run dir/prog.exe` would report the program as missing.
    var info = new ProcessStartInfo(Path.GetFullPath(exePath)) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var arg in targetArgs) info.ArgumentList.Add(arg);

    try {
      using var process = Process.Start(info) ?? throw new IOException("the process did not start");
      var outputGate = new object();
      void Deliver(string line) {
        lock (outputGate) onOutput(line);
      }

      // TargetStdio owns the ONE statement of how a session with a spawned target ends: wait out the
      // grace period, kill what is left — the whole process TREE, since a target's children are as
      // much this run's orphans as the target — wait for the kill to settle so the exit status is
      // readable, and only THEN join the forwarding tasks. Joining before the kill blocks forever on
      // a pipe that never closes, which is the other half of the same defect.
      var stdio = TargetStdio.Forward(process, Deliver, Deliver);
      if (stdio.EndAndJoin(PositiveSeconds.ToWaitMilliseconds(timeout))) {
        // The FACT only — each face appends the recourse its own caller can act on.
        return new TargetRun(null, TimedOut: true,
          $"{CoverageRender.DisplayPath(exePath)} did not finish within {PositiveSeconds.Text(timeout)}s"
          + " and was killed, so it wrote no counters.");
      }

      return new TargetRun(process.ExitCode, TimedOut: false, "");
    } catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) {
      return Failed($"could not run {CoverageRender.DisplayPath(exePath)}: {ex.Message}");
    }
  }

  private static TargetRun Failed(string error) => new(null, TimedOut: false, error);

  /// <summary>
  /// Join the binary's own `.text` hash, its `.mxdbg` coverage-point table, and a run's `.mxcov`
  /// counters into a report — or refuse, saying which of the three is missing or describes a
  /// different build.
  /// </summary>
  public static CoverageReport? Load(string exePath, string dataPath, int? targetExitCode, out string error) {
    if (!BinaryBuildId.TryCompute(exePath, out var binaryBuildId, out error)) return null;

    var sidecarPath = exePath + MxdbgFormat.SidecarExtension;
    if (!File.Exists(sidecarPath)) {
      error = $"no debug-info sidecar at {CoverageRender.DisplayPath(sidecarPath)}"
        + " — a coverage report needs the point table it holds";
      return null;
    }

    MxdbgReader sidecar;
    try {
      sidecar = new MxdbgReader(File.ReadAllBytes(sidecarPath));
    } catch (Exception ex) when (ex is InvalidDataException or IOException) {
      error = $"cannot read {CoverageRender.DisplayPath(sidecarPath)}: {ex.Message}";
      return null;
    }

    // Zero points is not an empty measurement — it is a binary that was never instrumented, and
    // reporting "0/0 lines covered" for one would be an answer to a question it cannot be asked.
    if (sidecar.CoveragePointCount == 0) {
      error = $"{CoverageRender.DisplayPath(exePath)} was not built with coverage instrumentation"
        + " — it has no coverage points to report on";
      return null;
    }

    if (!File.Exists(dataPath)) {
      error = $"no coverage data at {CoverageRender.DisplayPath(dataPath)}"
        + " — the program wrote none, so the run did not complete";
      return null;
    }

    MxcovReader? data;
    try {
      data = MxcovReader.TryParse(File.ReadAllBytes(dataPath), out error);
      if (data == null) return null;
    } catch (IOException ex) {
      error = $"cannot read {CoverageRender.DisplayPath(dataPath)}: {ex.Message}";
      return null;
    }

    return CoverageJoin.TryBuild(exePath, sidecar, binaryBuildId, data, targetExitCode, out error);
  }
}
