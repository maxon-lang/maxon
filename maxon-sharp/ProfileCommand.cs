using MaxonSharp.Debug;

namespace MaxonSharp;

/// <summary>
/// The `maxon profile` command: run a program and say where its CPU time went.
///
/// Everything about the measurement — launching, sampling, and joining samples with debug info — lives
/// in <see cref="ProfileRunner"/>, and all three renderings live in <see cref="ProfileRender"/>. This
/// face only parses a command line, chooses a sink for the program's output, and picks a rendering. The
/// MCP family is the same shape over the same two classes; neither builds a report of its own.
///
/// It profiles a PRISTINE binary: no instrumentation, no rebuild, no flag at build time. The only thing
/// it needs is the `.mxdbg` sidecar that `maxon build` writes by default and that leaves the executable
/// byte-identical — so the program measured here is the program that ships.
/// </summary>
internal static class ProfileCommand {
  private const string JsonFlag = "--json";
  private const string FoldedFlag = "--folded";

  internal const string RunVerb = "run";

  public static int Run(string[] args) {
    if (args.Length == 0) return Usage("expected a verb");

    var verb = args[0];
    if (verb != RunVerb) return Usage($"unknown verb '{verb}'");

    bool json = false;
    bool folded = false;
    string? exePath = null;
    var options = ProfileRunner.Defaults;
    var targetArgs = new List<string>();
    var targetEnv = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var arg in args[1..]) {
      if (arg == JsonFlag) json = true;
      else if (arg == FoldedFlag) folded = true;
      else if (arg.StartsWith(Program.TargetEnvFlag, StringComparison.Ordinal)) {
        // Refused rather than ignored, exactly as `maxon debug` refuses it: a silently dropped variable
        // leaves the program running under an environment the caller believes they configured, and for
        // MAXON_MAX_PROCS that is the difference between a reproducible profile and a machine-dependent one.
        var text = arg[Program.TargetEnvFlag.Length..];
        if (!Program.TryParseTargetEnv(text, out var name, out var value))
          return Usage($"'{Program.TargetEnvFlag}<NAME>=<VALUE>' needs a non-empty variable name, got '{text}'");
        targetEnv[name] = value;
      }
      else if (arg.StartsWith(ProfileRunner.TimeoutFlag, StringComparison.Ordinal)) {
        // Refused rather than clamped, through the one seconds rule: a deadline that rounds away to
        // zero expires before the program could possibly run, and would be reported as a setting.
        var text = arg[ProfileRunner.TimeoutFlag.Length..];
        if (!PositiveSeconds.TryParse(text, out var timeout))
          return Usage($"'{ProfileRunner.TimeoutFlag}' {PositiveSeconds.RequirementText}, got '{text}'");
        options = options with { Timeout = timeout };
      } else if (arg.StartsWith(ProfileRunner.RateFlag, StringComparison.Ordinal)) {
        if (NumericFlagProblem(arg, ProfileRunner.RateFlag, ProfileRunner.RateRefusal, out double rate)
            is { } problem) return Usage(problem);
        options = options with { RateHz = rate };
      } else if (arg.StartsWith(ProfileRunner.MinPercentFlag, StringComparison.Ordinal)) {
        if (NumericFlagProblem(arg, ProfileRunner.MinPercentFlag, ProfileRunner.MinPercentRefusal,
            out double minPercent) is { } problem) return Usage(problem);
        options = options with { MinPercent = minPercent };
      } else if (exePath == null && !arg.StartsWith('-')) exePath = arg;
      else if (exePath != null) targetArgs.Add(arg);
      else return Usage($"unknown option '{arg}'");
    }

    if (exePath == null) return Usage("expected an executable");
    // Named rather than silently preferring one: a caller who asked for both wants two different
    // consumers served, and answering one of them without saying so is a setting that is not in force.
    if (json && folded)
      return Usage($"'{JsonFlag}' and '{FoldedFlag}' are two different machine formats — pick one");

    if (targetEnv.Count > 0) options = options with { TargetEnv = targetEnv };

    // The program's own output goes to STDERR, keeping stdout as exactly the report and nothing else,
    // so a profile of a chatty program stays machine-readable.
    var report = ProfileRunner.Run(exePath, targetArgs, options, Console.Error.WriteLine, out var error);
    if (report == null) {
      Console.Error.WriteLine($"profile: {error}");
      return 1;
    }

    Console.Out.Write(json ? ProfileRender.Json(report) + "\n"
      : folded ? ProfileRender.Folded(report)
      : ProfileRender.Text(report));

    // This process's own exit answers ONE question: did the measurement complete. The measured
    // program's status is reported as data above, exactly as `maxon debug --batch` emits an `exit`
    // event and still exits 0 — a program that returns 7 is not this tool failing. What DOES make it
    // fail is a run that was killed at its deadline, which CI must not read as a finished measurement.
    return report.RunCompleted ? 0 : 1;
  }

  /// <summary>
  /// The problem with a numeric flag, or null when it is usable.
  ///
  /// ONE statement of "parse it, then ask the rule that OWNS its meaning", so the two numeric flags
  /// cannot come to refuse differently — and so each refusal is worded by the rule (which knows why the
  /// bound exists) rather than by the parser (which does not).
  /// </summary>
  private static string? NumericFlagProblem(string arg, string flag, Func<double, string?> refusal,
      out double value) {
    var text = arg[flag.Length..];
    if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture, out value))
      return $"'{flag}' needs a number, got '{text}'";

    return refusal(value) is { } reason ? $"'{flag}' {reason}, got '{text}'" : null;
  }

  private static int Usage(string problem) {
    Console.Error.WriteLine($"profile: {problem}");
    Console.Error.WriteLine($"Usage: maxon profile {RunVerb} <exe> [{JsonFlag}|{FoldedFlag}]"
      + $" [{ProfileRunner.RateFlag}<hz>] [{ProfileRunner.MinPercentFlag}<share>]"
      + $" [{ProfileRunner.TimeoutFlag}<seconds>] [args...]");
    Console.Error.WriteLine($"  {RunVerb}      launch the program and sample where its CPU time goes.");
    Console.Error.WriteLine("            The binary needs no instrumentation and is not rebuilt; only the");
    Console.Error.WriteLine("            .mxdbg sidecar is read, so the program measured is the one that ships.");
    Console.Error.WriteLine("            The program's own output goes to stderr so stdout is only the report.");
    Console.Error.WriteLine($"  {JsonFlag}    emit the full report as JSON instead of a summary.");
    Console.Error.WriteLine($"  {FoldedFlag}  emit collapsed stacks (`root;child;leaf <count>`), the format");
    Console.Error.WriteLine("            flamegraph.pl, inferno and speedscope all read.");
    Console.Error.WriteLine($"  {ProfileRunner.RateFlag}N   samples per second per running thread"
      + $" (default {ProfileRunner.DefaultRateHz}, max {ProfileRunner.MaxRateHz}).");
    Console.Error.WriteLine($"  {ProfileRunner.MinPercentFlag}N");
    Console.Error.WriteLine("            hide rows below this share of the run from the printed tables");
    Console.Error.WriteLine($"            (default {ProfileRunner.DefaultMinPercent}); how many were hidden is"
      + " always reported.");
    Console.Error.WriteLine($"  {ProfileRunner.TimeoutFlag}S  stop the program and report a PARTIAL profile after S"
      + $" seconds (default {ProfileRunner.DefaultRunTimeoutText}).");
    Console.Error.WriteLine($"  {Program.TargetEnvFlag}N=V");
    Console.Error.WriteLine("            set a variable in the PROFILED program's environment (repeatable).");
    Console.Error.WriteLine("            MAXON_MAX_PROCS=N pins the scheduler's worker count, which is what");
    Console.Error.WriteLine("            makes a green-thread profile reproducible rather than machine-dependent.");
    Console.Error.WriteLine("            This tool's own exit code says whether the MEASUREMENT completed;");
    Console.Error.WriteLine("            the program's own status is reported as `target exit code`.");
    return 1;
  }
}
