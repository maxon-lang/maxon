using System.Text;
using System.Text.Json;
using MaxonSharp.Debug;

namespace MaxonSharp.Mcp.Tools;

/// <summary>
/// The COVERAGE family — build a program with instrumentation, run it, and say which of its lines
/// and which arm of each `if` actually executed.
///
/// Like the debug family it is thin on purpose: it builds through <see cref="DebugBuild.Compile"/>,
/// runs and joins through <see cref="CoverageRunner"/>, and renders through
/// <see cref="CoverageRender.Json"/> — the SAME renderer `maxon coverage --json` prints. There is no
/// second report model here and no second vocabulary; an agent and a human are looking at one
/// instrument through two windows.
///
/// It holds no live state, so unlike a debug session nothing here outlives the request.
/// </summary>
internal static class CoverageTools {
  /// <summary>
  /// This face's bound on a coverage run, deliberately far shorter than the CLI's ten minutes and for
  /// the reason `debug_start` gives for its own: a human at a terminal who launched a runaway sees it
  /// and interrupts, while an agent's tool call has no such observer — it simply blocks the
  /// conversation. Longer than `debug_start`'s twenty seconds because this waits for a whole program
  /// to FINISH rather than for its first breakpoint; `timeoutSeconds` raises it for a program that
  /// legitimately runs long.
  /// </summary>
  private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(2);

  public static IReadOnlyList<McpTool> Tools { get; } = [
    new("coverage_run",
      "Build a Maxon program WITH coverage instrumentation, run it to completion, and report which "
      + "lines executed how many times and which arm of each `if` was taken. Four line states are "
      + "distinguished and they are not interchangeable: a count (executed), notCovered (real code "
      + "the run never reached), eliminated (the optimizer emitted no code for it, so there is "
      + "nothing to reach), and lines absent from the report entirely (no coverage point — a "
      + "comment, a blank, an `end`). `runCompleted:false` means the program died before finishing, "
      + "so the counts stop where it died. Branch arms include the `else` a program never WROTE, "
      + "which no line table can describe. `targetExitCode` is the measured program's own status, "
      + "reported as DATA — this tool succeeding says nothing about what the program returned.",
      [
        McpSource.Arg("measure"),
        new("args", McpSchema.ArrayOf("Command-line arguments passed to the program.",
          McpSchema.String("One argument.")), Required: false),
        new("timeoutSeconds", McpSchema.Number(
          "How long to let the program run before killing it and reporting an honest timeout "
          + $"(default {DefaultRunTimeout.TotalSeconds}). A killed program writes no counters, so this "
          + "refuses rather than returning a partial measurement. Raise it for a program that "
          + "legitimately runs long."), Required: false),
      ],
      RunAndReport),

    new("coverage_report",
      "Report from coverage data an earlier run already wrote, without running the program again. "
      + "Refuses — rather than reporting wrong lines — if the data was written by a different build "
      + "of the binary, because a counter is an anonymous index and last build's numbers would name "
      + "this build's source lines.",
      [McpSource.Arg("report on")],
      ReportOnly),
  ];

  private static McpToolResult RunAndReport(McpSession session, JsonElement args) {
    var exePath = Build(session, args);
    var dataPath = CoverageRunner.DataPathFor(exePath);
    var output = new StringBuilder();
    var timeout = McpArgs.OptionalTimeoutSeconds(args, "timeoutSeconds", DefaultRunTimeout);

    var run = CoverageRunner.Launch(exePath, McpArgs.OptionalStringArray(args, "args"), dataPath,
      timeout, line => output.AppendLine(line));
    // The recourse an MCP caller can act on — its own argument, never the CLI's flag — and offered
    // only for the failure raising it would actually cure.
    if (run.ExitCode is null)
      return McpToolResult.Refusal(run.Error + (run.TimedOut ? " Raise `timeoutSeconds` and call again." : ""));

    return Result(exePath, dataPath, run.ExitCode, output.ToString());
  }

  /// The target's exit code is null here on purpose: this tool did not run the program, so it has no
  /// status of its own to report and says nothing rather than reporting a zero it did not observe.
  private static McpToolResult ReportOnly(McpSession session, JsonElement args) {
    var exePath = Build(session, args);
    return Result(exePath, CoverageRunner.DataPathFor(exePath), targetExitCode: null, output: "");
  }

  /// <summary>
  /// Build the program with instrumentation ON, stated rather than inherited — the same rule
  /// `debug_start` follows for `--debugstream`. A build that quietly dropped it would produce a
  /// binary this family then truthfully reports as uninstrumented, about a build it just made.
  /// </summary>
  private static string Build(McpSession session, JsonElement args) =>
    DebugBuild.Compile(McpSource.Resolve(session, args), debugStream: false, coverage: true);

  /// <summary>
  /// The report as JSON, plus whatever the program printed. The report is SPLICED as raw JSON rather
  /// than re-encoded: it is already the one renderer's output, and wrapping it in a string would
  /// hand an agent JSON to parse twice.
  /// </summary>
  private static McpToolResult Result(string exePath, string dataPath, int? targetExitCode, string output) {
    var report = CoverageRunner.Load(exePath, dataPath, targetExitCode, out var error);
    if (report == null) return McpToolResult.Refusal(error);

    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      w.WritePropertyName("coverage");
      w.WriteRawValue(CoverageRender.Json(report));
      if (output.Length > 0) w.WriteString("output", output);
      w.WriteEndObject();
    }
    return McpToolResult.Json(Encoding.UTF8.GetString(buffer.ToArray()));
  }
}
