using System.Globalization;
using System.Text;
using System.Text.Json;
using MaxonSharp.Debug;

namespace MaxonSharp.Testing;

/// <summary>
/// Both faces of a test run, over the one <see cref="TestRunReport"/>.
///
/// Text is emitted in TWO pieces on purpose. <see cref="FileBlock"/> is printed as each file
/// finishes, so an interactive run shows progress; <see cref="Summary"/> is printed once at the end.
/// The executor hands files over in discovery order regardless of which finished first, so the
/// concatenation of the pieces is the same bytes every time — which is the only reason a golden can
/// gate this at all.
///
/// JSON is written by hand with <see cref="Utf8JsonWriter"/> and no DTOs, matching
/// <c>CoverageRender.Json</c>: the shape is decided here rather than by whatever the model's field
/// names happen to be, so renaming a C# property cannot silently change a consumer's contract.
/// </summary>
internal static class TestRender {
  private const string PassMark = "✓";
  private const string FailMark = "✗";

  /// <summary>
  /// Width the test name is padded to before the status column. Fixed rather than measured, because
  /// a width computed from the widest name in the run could not be known until the run ENDED, and
  /// results are printed while it is still going. A name longer than this pushes its status right
  /// rather than being truncated — a clipped test name is worse than a ragged column.
  /// </summary>
  private const int NameColumnWidth = 44;

  /// <summary>Width the status/duration is right-aligned in.</summary>
  private const int StatusColumnWidth = 11;

  private const string Indent = "  ";

  /// <summary>Shown for a failure that reported neither a thrown error nor any output of its own.</summary>
  private const string NoAssertionMessageText =
    "an assertion failed, and the test wrote nothing to stderr saying which";

  /// <summary>
  /// How a non-passing outcome is labelled, in both faces. One map, so the word a human reads and
  /// the token a machine reads can never describe different states.
  /// </summary>
  private static string Label(TestOutcome outcome) => outcome switch {
    TestOutcome.Passed => "PASS",
    TestOutcome.Failed => "FAIL",
    TestOutcome.Crashed => "CRASHED",
    TestOutcome.DidNotRun => "DID NOT RUN",
    TestOutcome.TimedOut => "TIMED OUT",
    TestOutcome.Leaked => "LEAKED",
    _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unhandled test outcome"),
  };

  /// <summary>The machine-readable spelling of an outcome, for JSON.</summary>
  private static string JsonState(TestOutcome outcome) => outcome switch {
    TestOutcome.Passed => "passed",
    TestOutcome.Failed => "failed",
    TestOutcome.Crashed => "crashed",
    TestOutcome.DidNotRun => "didNotRun",
    TestOutcome.TimedOut => "timedOut",
    TestOutcome.Leaked => "leaked",
    _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unhandled test outcome"),
  };

  /// <summary>
  /// One file's results: a header naming the file, then a line per test.
  /// </summary>
  /// <param name="showTiming">
  /// False under <c>--no-timing</c>, which is what makes stdout byte-reproducible: a duration is the
  /// one thing in this report that differs between two runs of identical code.
  /// </param>
  public static string FileBlock(TestFileResults file, bool color, bool showTiming) {
    var sb = new StringBuilder();
    sb.Append(Ansi.Bold(ReportPath.Display(file.Path), color)).Append(":\n");

    foreach (var result in file.Results) {
      var passed = result.Outcome == TestOutcome.Passed;
      var mark = passed ? Ansi.Green(PassMark, color) : Ansi.Red(FailMark, color);
      var status = StatusText(result, color, showTiming);

      // Trimmed because the name is padded to a fixed column and the status may be empty (under
      // --no-timing, every passing line would otherwise end in a run of spaces). Colour codes end
      // with the SGR reset, which is not whitespace, so a coloured line trims identically.
      var line = $"{Indent}{mark} {PadName(result.Test.DisplayName)}{status}".TrimEnd();
      sb.Append(line).Append('\n');
    }

    return sb.ToString();
  }

  /// <summary>
  /// Pad a name to the status column. Padding is computed on the RAW name — colour codes are never
  /// applied to it — so a coloured run and an uncoloured one align identically.
  /// </summary>
  private static string PadName(string name) =>
    name.Length >= NameColumnWidth ? name + " " : name.PadRight(NameColumnWidth);

  /// <summary>
  /// The right-hand column: a duration for a test that ran, and the outcome's name for one that did
  /// not finish under its own power. An outcome with no duration always shows its label, even under
  /// <c>--no-timing</c> — the label is a verdict, not a measurement.
  /// </summary>
  private static string StatusText(UnitTestResult result, bool color, bool showTiming) {
    if (result.Outcome is TestOutcome.Passed or TestOutcome.Failed) {
      if (!showTiming) return "";
      return Ansi.Dim(Duration(result.Nanos).PadLeft(StatusColumnWidth), color);
    }

    return Paint(result.Outcome, Label(result.Outcome).PadLeft(StatusColumnWidth), color);
  }

  /// <summary>
  /// Colour an outcome's name. ONE rule, used by the per-test line and by the failure block's
  /// header, so a state cannot be yellow in the list and red in the detail.
  ///
  /// <see cref="TestOutcome.DidNotRun"/> is yellow rather than red because it is not a verdict ON
  /// the test — nothing observed it, usually because something ELSE killed its process — and
  /// colouring it like a failure points a reader at the wrong test.
  /// </summary>
  private static string Paint(TestOutcome outcome, string text, bool color) =>
    outcome == TestOutcome.DidNotRun ? Ansi.Yellow(text, color) : Ansi.Red(text, color);

  private static string Duration(long nanos) =>
    (nanos / 1_000_000.0).ToString("0.00", CultureInfo.InvariantCulture) + "ms";

  /// <summary>
  /// The tail of a run: one detail block per failure, then the counts, then the footer.
  /// </summary>
  public static string Summary(TestRunReport report, bool color, bool showTiming) {
    var sb = new StringBuilder();

    foreach (var file in report.Files) {
      foreach (var result in file.Results) {
        if (!result.IsFailure) continue;
        AppendFailureDetail(sb, file.Path, result, color);
      }
    }

    sb.Append('\n');
    sb.Append(' ').Append(report.Passed).Append(" pass\n");
    sb.Append(' ').Append(Colorize(report.Failed, $"{report.Failed} fail", color)).Append('\n');

    if (report.Bailed) {
      sb.Append(' ').Append(Ansi.Yellow(
        "bailed out early — the counts above describe what RAN, not what was selected", color)).Append('\n');
    }

    sb.Append('\n');
    sb.Append(CountLine(report.Total, report.FileCount));

    if (showTiming) {
      var compile = report.Compiled ? $"compile {report.CompileMs}ms" : "compile cached";
      sb.Append("   ").Append(compile).Append(", run ").Append(report.RunMs).Append("ms");
    }

    sb.Append('\n');
    return sb.ToString();
  }

  private static string Colorize(int failed, string text, bool color) =>
    failed > 0 ? Ansi.Red(text, color) : text;

  /// <summary>
  /// One failure, in the shape a reader acts on: what failed, then why, then where.
  ///
  /// A reported <c>threw</c> is preferred over captured output because it is STRUCTURED — the
  /// compiler's substituted handler names the error type, the live case, and the line of the `try`
  /// that threw, which is the line to open. Captured output is what remains when the failure was the
  /// test's own assertion or a panic, neither of which reports through that channel.
  /// </summary>
  private static void AppendFailureDetail(StringBuilder sb, string path, UnitTestResult result, bool color) {
    sb.Append('\n');
    sb.Append(Ansi.Red(Label(result.Outcome), color)).Append("  ")
      .Append(ReportPath.Display(path)).Append(" > ").Append(result.Test.DisplayName).Append('\n');

    if (result.Thrown is { } thrown) {
      sb.Append(Indent).Append("threw ").Append(thrown.ErrorType).Append('.')
        .Append(thrown.ErrorCase).Append('\n');
      sb.Append(Indent).Append("at ").Append(thrown.File).Append(':').Append(thrown.Line).Append('\n');
    }

    if (result.Output.Length > 0) {
      foreach (var line in result.Output.Split('\n')) {
        sb.Append(Indent).Append(line).Append('\n');
      }
    } else if (result.Thrown == null && result.Outcome == TestOutcome.Failed) {
      // A test throws exactly TestFailure, so a failure with no `threw` report is the test's OWN
      // assertion. With nothing on stderr either, the report would otherwise be a bare headline —
      // say which of the two it is, rather than leaving a reader to wonder if output was lost.
      sb.Append(Indent).Append(NoAssertionMessageText).Append('\n');
    }

    if (result.RecoveredInIsolation) {
      sb.Append(Indent).Append(Ansi.Yellow(
        "this test passed when re-run alone, so the failure depends on what ran before it", color))
        .Append('\n');
    }
  }

  /// <summary>
  /// The whole run as JSON, emitted once at the end rather than streamed.
  /// </summary>
  /// <param name="showTiming">
  /// False under <c>--no-timing</c>, which drops every duration here too. The flag is about making
  /// stdout REPRODUCIBLE, and a JSON consumer diffing two runs needs that as much as a golden does —
  /// honouring it in one face only would make the flag mean different things by output format.
  /// </param>
  /// <param name="emptyReason">
  /// Why the run has no results, when it has none. Carried into JSON because the text face says it
  /// and this must not say less: "no tests exist" and "no test matched -t" are different problems
  /// with the same exit code, and a machine reader that cannot tell them apart cannot act.
  /// </param>
  public static string Json(TestRunReport report, bool showTiming, string? emptyReason = null) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      if (emptyReason != null) w.WriteString("reason", emptyReason);
      w.WriteNumber("total", report.Total);
      w.WriteNumber("passed", report.Passed);
      w.WriteNumber("failed", report.Failed);
      w.WriteNumber("files", report.FileCount);
      w.WriteBoolean("compiled", report.Compiled);
      if (showTiming) {
        w.WriteNumber("compileMs", report.CompileMs);
        w.WriteNumber("runMs", report.RunMs);
      }
      // Only when true: a flag that is false in almost every run is a column of one value.
      if (report.Bailed) w.WriteBoolean("bailed", true);

      w.WriteStartArray("results");
      foreach (var file in report.Files) {
        foreach (var result in file.Results) {
          WriteResult(w, file.Path, result, showTiming);
        }
      }
      w.WriteEndArray();

      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  private static void WriteResult(Utf8JsonWriter w, string path, UnitTestResult result, bool showTiming) {
    w.WriteStartObject();
    w.WriteString("file", ReportPath.Display(path));
    w.WriteString("name", result.Test.DisplayName);
    w.WriteString("symbol", result.Test.Symbol);
    w.WriteNumber("line", result.Test.Line);
    w.WriteString("state", JsonState(result.Outcome));

    // Absent rather than zero for an outcome that never produced one: a test that crashed did not
    // take 0.00ms, and a machine reader must be able to tell "fast" from "never measured".
    if (showTiming && result.Outcome is TestOutcome.Passed or TestOutcome.Failed)
      w.WriteNumber("nanos", result.Nanos);

    if (result.Thrown is { } thrown) {
      w.WriteStartObject("threw");
      w.WriteString("errorType", thrown.ErrorType);
      w.WriteString("errorCase", thrown.ErrorCase);
      w.WriteString("file", thrown.File);
      w.WriteNumber("line", thrown.Line);
      w.WriteEndObject();
    }

    if (result.Output.Length > 0) w.WriteString("output", result.Output);
    if (result.RecoveredInIsolation) w.WriteBoolean("recoveredInIsolation", true);

    w.WriteEndObject();
  }

  /// <summary>
  /// <c>--list</c>: what WOULD run, compiling nothing.
  ///
  /// It reads the same discovery the run does, so a listed test and a run test cannot come from
  /// different answers to "what is a test".
  /// </summary>
  public static string ListText(IReadOnlyList<TestFileGroup> groups, IReadOnlySet<int> selected, bool color) {
    var sb = new StringBuilder();
    var count = 0;
    var files = 0;

    foreach (var group in groups) {
      var members = group.Tests.Where(t => selected.Contains(t.Index)).ToList();
      if (members.Count == 0) continue;

      files++;
      sb.Append(Ansi.Bold(ReportPath.Display(group.Path), color)).Append(":\n");
      foreach (var member in members) {
        sb.Append(Indent).Append(member.Test.DisplayName).Append('\n');
        count++;
      }
    }

    sb.Append('\n').Append(CountLine(count, files)).Append('\n');
    return sb.ToString();
  }

  /// <summary>
  /// The "N tests across M files." line, without its newline. Both faces of a run end with it, and
  /// spelling the pluralisation twice is how the two would come to disagree about the singular.
  /// </summary>
  private static string CountLine(int tests, int files) =>
    $" {tests} {(tests == 1 ? "test" : "tests")} across {files} {(files == 1 ? "file." : "files.")}";

  /// <summary><c>--list --json</c>.</summary>
  public static string ListJson(IReadOnlyList<TestFileGroup> groups, IReadOnlySet<int> selected) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      w.WriteStartArray("tests");
      foreach (var group in groups) {
        foreach (var member in group.Tests) {
          if (!selected.Contains(member.Index)) continue;

          w.WriteStartObject();
          w.WriteString("file", ReportPath.Display(group.Path));
          w.WriteString("name", member.Test.DisplayName);
          w.WriteString("symbol", member.Test.Symbol);
          w.WriteNumber("line", member.Test.Line);
          w.WriteEndObject();
        }
      }
      w.WriteEndArray();
      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }
}
