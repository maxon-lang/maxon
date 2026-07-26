namespace MaxonSharp.Debug;

/// <summary>
/// Running a `--coverage` binary and loading what it wrote — the two steps BOTH faces perform, in
/// one place. The CLI (`maxon coverage`) and the MCP family differ only in where the program's own
/// output goes and how a refusal is delivered; everything about the artifacts is here.
///
/// Launching is NOT here: it is <see cref="TargetLauncher"/>, shared with the profiler, because
/// "start it, forward its output, bound it, kill it, join" is one fact and this command is only one of
/// two that needs it. What stays here is what makes the run a COVERAGE run — clearing the stale data
/// file first, and saying what a killed run means for the counters.
///
/// Every refusal is a sentence naming the defect. There is no partially-trusted path: a report is
/// either built from a binary, a sidecar and a data file that all agree about which build they
/// describe, or it is not built at all.
/// </summary>
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
  /// The run itself is <see cref="TargetLauncher.Run"/>'s, including its promise that
  /// <paramref name="onOutput"/> is called serially. What is added here is the consequence a killed
  /// coverage run has and no other command's does: the counters are written by the program on ITS way
  /// out, so a program that was killed wrote none at all.
  /// </summary>
  public static TargetRun Launch(string exePath, IReadOnlyList<string> targetArgs, string dataPath,
      TimeSpan timeout, Action<string> onOutput) {
    try {
      if (File.Exists(dataPath)) File.Delete(dataPath);
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      return new TargetRun(null, TimedOut: false,
        $"cannot remove the previous {ReportPath.Display(dataPath)}: {ex.Message}");
    }

    var run = TargetLauncher.Run(exePath, targetArgs, timeout, onOutput);
    return run.TimedOut
      ? run with { Error = run.Error + " It wrote no counters." }
      : run;
  }

  /// <summary>
  /// Join the binary's own `.text` hash, its `.mxdbg` coverage-point table, and a run's `.mxcov`
  /// counters into a report — or refuse, saying which of the three is missing or describes a
  /// different build.
  /// </summary>
  public static CoverageReport? Load(string exePath, string dataPath, int? targetExitCode, out string error) {
    var sidecar = MxdbgSidecar.TryLoad(exePath,
      "a coverage report needs the point table it holds", out var binaryBuildId, out error);
    if (sidecar == null) return null;

    // Zero points is not an empty measurement — it is a binary that was never instrumented, and
    // reporting "0/0 lines covered" for one would be an answer to a question it cannot be asked.
    if (sidecar.CoveragePointCount == 0) {
      error = $"{ReportPath.Display(exePath)} was not built with coverage instrumentation"
        + " — it has no coverage points to report on";
      return null;
    }

    if (!File.Exists(dataPath)) {
      error = $"no coverage data at {ReportPath.Display(dataPath)}"
        + " — the program wrote none, so the run did not complete";
      return null;
    }

    MxcovReader? data;
    try {
      data = MxcovReader.TryParse(File.ReadAllBytes(dataPath), out error);
      if (data == null) return null;
    } catch (IOException ex) {
      error = $"cannot read {ReportPath.Display(dataPath)}: {ex.Message}";
      return null;
    }

    return CoverageJoin.TryBuild(exePath, sidecar, binaryBuildId, data, targetExitCode, out error);
  }
}
