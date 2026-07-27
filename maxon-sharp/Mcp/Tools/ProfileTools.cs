using System.Text;
using System.Text.Json;
using MaxonSharp.Debug;

namespace MaxonSharp.Mcp.Tools;

/// <summary>
/// The PROFILE family — run a program and say where its CPU time actually went.
///
/// Like the debug and coverage families it is thin on purpose: it builds through
/// <see cref="DebugBuild.Compile"/>, runs and samples through <see cref="ProfileRunner"/>, and renders
/// through the SAME <see cref="ProfileRender.Json"/> that `maxon profile --json` prints. There is no
/// second report model here and no second vocabulary; an agent and a human are looking at one
/// instrument through two windows.
///
/// It holds no live state, so unlike a debug session nothing here outlives the request.
/// </summary>
internal static class ProfileTools {

  /// <summary>
  /// This face's bound on a profiled run, deliberately far shorter than the CLI's ten minutes and for
  /// the reason `debug_start` gives for its own: a human at a terminal who launched a runaway sees it
  /// and interrupts, while an agent's tool call has no such observer — it simply blocks the
  /// conversation. `timeoutSeconds` raises it for a program that legitimately runs long, and unlike
  /// coverage reaching it is not fatal: the samples already taken are real, and the answer says the
  /// profile is PARTIAL rather than discarding them.
  /// </summary>
  private static readonly TimeSpan DefaultRunTimeout = TimeSpan.FromMinutes(2);

  public static IReadOnlyList<McpTool> Tools { get; } = [
    new("profile_run",
      "Run a Maxon program and report where its CPU TIME went, by sampling it — no instrumentation, "
      + "no special build, the same binary that ships. Answers with a per-function self-time table, a "
      + "call tree, and one row per stack that ran. Green threads are visible as themselves: a worker "
      + "executing a green thread has that thread's own stack, so the frames are the green thread's, "
      + "and a green thread parked with no work consumes no CPU and correctly contributes nothing. "
      + "READ THE HONESTY FIELDS BEFORE THE PERCENTAGES: `samples` with `achievedRateHz` and "
      + "`durationSeconds` is what tells 3 readings from 30,000, which look identical once divided. "
      + "⚠ Use `achievedRateHz`, not `requestedRateHz`: the sampler ticks on an OS timer whose "
      + "granularity caps it near 1,750 Hz on a typical host, so a high `rateHz` argument is an "
      + "over-ask that is reported rather than met, and the achieved rate is the denominator every "
      + "share below was computed against. `ticks` and `durationSeconds` are the raw pair it comes "
      + "from. `sparse:true` means "
      + "there were too few to divide at all; `samplesUnsymbolized` is how much landed in code this "
      + "binary's debug info cannot name, and it is the number that says how far to trust the rest; "
      + "`stacksTruncated` counts walks that lost their OUTERMOST frames, so those samples are "
      + "attributed under whatever root they did reach. `runCompleted:false` means the deadline "
      + "stopped the program, so the profile describes a prefix of it. `targetExitCode` is the "
      + "measured program's own status, reported as DATA — this tool succeeding says nothing about "
      + "what the program returned.",
      [
        McpSource.Arg("profile"),
        new("args", McpSchema.ArrayOf("Command-line arguments passed to the program.",
          McpSchema.String("One argument.")), Required: false),
        new("rateHz", McpSchema.Number(
          $"Samples per second per running thread (default {ProfileRunner.DefaultRateHz}, max "
          + $"{ProfileRunner.MaxRateHz}). Raise it for a program too short to accumulate samples; "
          + "lower it for one whose behaviour the suspend/resume pair disturbs."), Required: false),
        new("timeoutSeconds", McpSchema.Number(
          "How long to let the program run before stopping it and reporting what was sampled "
          + $"(default {DefaultRunTimeout.TotalSeconds}). Unlike coverage this still yields a report, "
          + "marked `runCompleted:false`."), Required: false),
        // The same knob, the same spelling and the same reason as `debug_start`'s. It is not optional
        // decoration for THIS tool: the answer it advertises above is a green-thread breakdown, and the
        // scheduler fixes its worker count at startup, so an unpinned green-thread profile measures the
        // machine as much as the program. `maxon profile --target-env=` has had it since the rung
        // landed; a control present on one face and absent on the other is how two faces of one
        // instrument come to give different answers to the same question.
        new("env", McpSchema.StringMap(
          "Environment variables set in the PROFILED program. MAXON_MAX_PROCS=N pins its green-thread "
          + "scheduler to N processors, which is what makes a concurrent program's profile reproducible "
          + "rather than machine-dependent.",
          "The variable's value."), Required: false),
      ],
      RunAndReport),
  ];

  private static McpToolResult RunAndReport(McpSession session, JsonElement args) {
    // Stated rather than inherited, the same rule `debug_start` follows for `--debugstream`: this
    // rebuilds whatever it is pointed at, so a build that quietly changed a flag would replace a user's
    // binary and then truthfully report on the replacement. The profiler needs neither instrumentation
    // nor trace hooks, so both are stated OFF — a profile of an instrumented build would be measuring
    // the instrumentation.
    var exePath = DebugBuild.Compile(McpSource.Resolve(session, args), debugStream: false, coverage: false);

    var rateHz = McpArgs.OptionalNumber(args, "rateHz") ?? ProfileRunner.DefaultRateHz;
    if (ProfileRunner.RateRefusal(rateHz) is { } refusal)
      throw new McpInvalidParamsException($"`rateHz` {refusal}, got {rateHz}");

    var options = ProfileRunner.Defaults with {
      Timeout = McpArgs.OptionalTimeoutSeconds(args, "timeoutSeconds", DefaultRunTimeout),
      RateHz = rateHz,
      TargetEnv = McpArgs.OptionalStringMap(args, "env"),
    };

    var output = new StringBuilder();
    var report = ProfileRunner.Run(exePath, McpArgs.OptionalStringArray(args, "args"), options,
      line => output.AppendLine(line), out var error);
    if (report == null) return McpToolResult.Refusal(error);

    // The report is SPLICED as raw JSON rather than re-encoded: it is already the one renderer's
    // output, and wrapping it in a string would hand an agent JSON to parse twice.
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      w.WritePropertyName("profile");
      w.WriteRawValue(ProfileRender.Json(report));
      if (output.Length > 0) w.WriteString("output", output.ToString());
      w.WriteEndObject();
    }
    return McpToolResult.Json(Encoding.UTF8.GetString(buffer.ToArray()));
  }
}
