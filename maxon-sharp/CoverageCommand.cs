using MaxonSharp.Debug;

namespace MaxonSharp;

/// <summary>
/// The `maxon coverage` command: run a `--coverage` binary and report what it executed, or report
/// from a run that already happened.
///
/// Everything about the artifacts — launching, and joining binary + sidecar + data — lives in
/// <see cref="CoverageRunner"/>, and both renderings live in <see cref="CoverageRender"/>. This face
/// only parses a command line, chooses a sink for the program's output, and picks text or JSON. The
/// MCP family is the same shape over the same two classes; neither builds a report of its own.
/// </summary>
internal static class CoverageCommand {
  private const string JsonFlag = "--json";
  private const string DataFlag = "--data=";

  internal const string RunVerb = "run";
  internal const string ReportVerb = "report";

  public static int Run(string[] args) {
    if (args.Length == 0) return Usage("expected a verb");

    var verb = args[0];
    if (verb != RunVerb && verb != ReportVerb) return Usage($"unknown verb '{verb}'");

    bool json = false;
    string? dataPath = null;
    string? exePath = null;
    var targetArgs = new List<string>();

    foreach (var arg in args[1..]) {
      if (arg == JsonFlag) json = true;
      else if (arg.StartsWith(DataFlag, StringComparison.Ordinal)) dataPath = arg[DataFlag.Length..];
      else if (exePath == null && !arg.StartsWith('-')) exePath = arg;
      else if (exePath != null) targetArgs.Add(arg);
      else return Usage($"unknown option '{arg}'");
    }

    if (exePath == null) return Usage("expected an executable");
    if (verb == ReportVerb && targetArgs.Count > 0)
      return Usage($"'{ReportVerb}' takes no target arguments — use '{RunVerb}' to launch the program");
    // A program writes to the path baked into it at build time, so telling `run` to read somewhere
    // else would delete that file, run, and then report on a file nothing wrote. Naming the
    // combination is the honest answer; silently ignoring the flag is not.
    if (verb == RunVerb && dataPath != null)
      return Usage($"'{DataFlag}' names where to READ counters, so it belongs to '{ReportVerb}'"
        + $" — '{RunVerb}' reports on the file the program itself just wrote");

    dataPath ??= CoverageRunner.DataPathFor(exePath);

    // The program's own output goes to STDERR, keeping stdout as exactly the report and nothing
    // else, so a coverage run of a chatty program stays machine-readable.
    if (verb == RunVerb
        && !CoverageRunner.Launch(exePath, targetArgs, dataPath, Console.Error.WriteLine, out var runError)) {
      Console.Error.WriteLine($"coverage: {runError}");
      return 1;
    }

    var report = CoverageRunner.Load(exePath, dataPath, out var loadError);
    if (report == null) {
      Console.Error.WriteLine($"coverage: {loadError}");
      return 1;
    }

    Console.Out.Write(json ? CoverageRender.Json(report) + "\n" : CoverageRender.Text(report));

    // A run that did not complete exits NONZERO. CI must not read a program that died halfway as a
    // measured pass — the same rule the debugger's batch surface follows for a crashed debuggee.
    return report.RunCompleted ? 0 : 1;
  }

  private static int Usage(string problem) {
    Console.Error.WriteLine($"coverage: {problem}");
    Console.Error.WriteLine($"Usage: maxon coverage <{RunVerb}|{ReportVerb}> <exe> [{JsonFlag}] [{DataFlag}<file>] [args...]");
    Console.Error.WriteLine($"  {RunVerb}     launch a {Program.CoverageFlag} binary, then report what it executed.");
    Console.Error.WriteLine("           The program's own output goes to stderr so stdout is only the report.");
    Console.Error.WriteLine($"  {ReportVerb}  report from the data an earlier run already wrote.");
    Console.Error.WriteLine($"  {JsonFlag}   emit the report as JSON instead of an annotated source listing.");
    Console.Error.WriteLine($"  {DataFlag}F  read counters from F instead of <exe>{MxcovFormat.DataExtension}.");
    return 1;
  }
}
