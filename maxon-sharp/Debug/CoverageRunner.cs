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
public static class CoverageRunner {
  /// The data file a binary writes on exit, and therefore where a report reads it from. Stated once,
  /// because the emitted runtime derives the same name from the same extension constant.
  public static string DataPathFor(string exePath) => exePath + MxcovFormat.DataExtension;

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
  public static bool Launch(string exePath, IReadOnlyList<string> targetArgs, string dataPath,
      Action<string> onOutput, out string error) {
    if (!File.Exists(exePath)) {
      error = $"no such executable: {CoverageRender.DisplayPath(exePath)}";
      return false;
    }

    try {
      if (File.Exists(dataPath)) File.Delete(dataPath);
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      error = $"cannot remove the previous {CoverageRender.DisplayPath(dataPath)}: {ex.Message}";
      return false;
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
      void Deliver(string? line) {
        if (line == null) return;
        lock (outputGate) onOutput(line);
      }

      process.OutputDataReceived += (_, e) => Deliver(e.Data);
      process.ErrorDataReceived += (_, e) => Deliver(e.Data);
      process.BeginOutputReadLine();
      process.BeginErrorReadLine();
      process.WaitForExit();
      error = "";
      return true;
    } catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) {
      error = $"could not run {CoverageRender.DisplayPath(exePath)}: {ex.Message}";
      return false;
    }
  }

  /// <summary>
  /// Join the binary's own `.text` hash, its `.mxdbg` coverage-point table, and a run's `.mxcov`
  /// counters into a report — or refuse, saying which of the three is missing or describes a
  /// different build.
  /// </summary>
  public static CoverageReport? Load(string exePath, string dataPath, out string error) {
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

    return CoverageJoin.TryBuild(exePath, sidecar, binaryBuildId, data, out error);
  }
}
