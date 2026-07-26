using System.Text;
using System.Text.Json;

namespace MaxonSharp.Debug;

/// <summary>
/// What a coverage report can say about one source line, and the reason there are FOUR answers
/// rather than three.
///
/// A line with no entry at all carries no coverage point — a comment, a blank line, an `end`. A line
/// with a point that never fired is <see cref="NotCovered"/>: real code the run never reached. And a
/// line whose points ALL lost their code to the optimizer is <see cref="Eliminated"/>: there is
/// nothing there to reach, so calling it "not covered" would blame the test for missing code that
/// does not exist, and calling it "no coverage point" would hide it among the comments.
/// </summary>
public enum CoverageLineState { Covered, NotCovered, Eliminated }

public sealed record CoverageLine(uint Line, CoverageLineState State, ulong Count);

public sealed record CoverageFile(string Path, IReadOnlyList<CoverageLine> Lines);

/// One user `if`, with the count of each arm. <see cref="ElseImplicit"/> marks the arm the source
/// never wrote — the one no line table can describe, because it has no source text to hold a row.
public sealed record CoverageBranch(
  string File, uint Line, uint Col, ulong ThenCount, ulong ElseCount, bool ElseImplicit,
  bool ThenEliminated, bool ElseEliminated);

/// <summary>
/// The joined result of a coverage-point table and a run's counters — the ONE model both faces
/// render. <see cref="RunCompleted"/> false means the program died before its dump: every count in
/// here is real, but the run stopped early, so the report says PARTIAL rather than presenting it as
/// a finished measurement.
/// </summary>
public sealed record CoverageReport(
  string ExePath,
  bool RunCompleted,
  IReadOnlyList<CoverageFile> Files,
  IReadOnlyList<CoverageBranch> Branches) {

  public int LinesCovered => Files.Sum(f => f.Lines.Count(l => l.State == CoverageLineState.Covered));
  public int LinesInstrumented =>
    Files.Sum(f => f.Lines.Count(l => l.State != CoverageLineState.Eliminated));
  public int LinesEliminated =>
    Files.Sum(f => f.Lines.Count(l => l.State == CoverageLineState.Eliminated));
  public int ArmsTaken => Branches.Sum(b => (b.ThenCount > 0 ? 1 : 0) + (b.ElseCount > 0 ? 1 : 0));
  public int ArmsInstrumented =>
    Branches.Sum(b => (b.ThenEliminated ? 0 : 1) + (b.ElseEliminated ? 0 : 1));
}

/// <summary>
/// Joins a binary's `.mxdbg` coverage-point table with a run's `.mxcov` counters.
///
/// Every refusal names its cause. A `.mxcov` from a DIFFERENT build is refused on the build-id
/// exactly as the sidecar is: counters are anonymous indices, so joining last build's numbers to
/// this build's point table would attribute real counts to the wrong source lines — an instrument
/// that lies, which the design doc holds to be worse than no instrument at all.
/// </summary>
public static class CoverageJoin {
  public static CoverageReport? TryBuild(string exePath, MxdbgReader sidecar, ulong binaryBuildId,
      MxcovReader data, out string error) {
    // The refusals deliberately name the FACT and not the two build-ids. The ids are a build-machine
    // detail a reader can do nothing with, and printing them would make every refusal transcript
    // machine-specific — an instrument whose own output cannot be compared across runs.
    if (sidecar.BuildId != binaryBuildId) {
      error = "the .mxdbg sidecar describes a different build of this binary — rebuild it";
      return null;
    }
    if (data.BuildId != binaryBuildId) {
      error = "the .mxcov data was written by a different build of this binary — re-run it";
      return null;
    }
    if (data.Counters.Count != sidecar.CoveragePointCount) {
      error = $"the .mxcov holds {data.Counters.Count} counters but this binary has "
        + $"{sidecar.CoveragePointCount} coverage points";
      return null;
    }

    error = "";
    return new CoverageReport(exePath, data.RunCompleted,
      BuildFiles(sidecar, data), BuildBranches(sidecar, data));
  }

  /// Per-file, per-line states. Several points can land on one line — a statement's own point and
  /// the arm points of an `if` that starts there — so a line takes the HIGHEST count any of them
  /// reached: they all count executions OF THAT LINE, and the arm counts are a subset of the
  /// statement's. A line is Eliminated only when EVERY point on it lost its code.
  private static List<CoverageFile> BuildFiles(MxdbgReader sidecar, MxcovReader data) {
    var byFile = new Dictionary<string, Dictionary<uint, CoverageLine>>(StringComparer.Ordinal);

    for (uint i = 0; i < sidecar.CoveragePointCount; i++) {
      var point = sidecar.CoveragePoint(i);
      var lines = byFile.TryGetValue(point.File, out var existing) ? existing : byFile[point.File] = [];

      ulong count = point.Eliminated ? 0 : data.Counters[(int)i];
      var state = point.Eliminated ? CoverageLineState.Eliminated
        : count > 0 ? CoverageLineState.Covered : CoverageLineState.NotCovered;

      lines[point.Line] = lines.TryGetValue(point.Line, out var prev)
        ? Merge(prev, new CoverageLine(point.Line, state, count))
        : new CoverageLine(point.Line, state, count);
    }

    return [.. byFile
      .OrderBy(f => f.Key, StringComparer.Ordinal)
      .Select(f => new CoverageFile(f.Key, [.. f.Value.Values.OrderBy(l => l.Line)]))];
  }

  /// Combine two points that share a line. Real code outranks eliminated code (the line HAS code),
  /// and among real code the larger count wins.
  private static CoverageLine Merge(CoverageLine a, CoverageLine b) {
    if (a.State == CoverageLineState.Eliminated) return b;
    if (b.State == CoverageLineState.Eliminated) return a;
    return a.Count >= b.Count ? a : b;
  }

  /// <summary>
  /// Pair up each `if`'s arms. Both arms of one branch were minted at the `if` keyword's own
  /// position, so (file, line, col) identifies the branch exactly — two `if`s cannot share one — and
  /// no relationship field has to be carried in the record to say which arms belong together.
  /// </summary>
  private static List<CoverageBranch> BuildBranches(MxdbgReader sidecar, MxcovReader data) {
    var byBranch = new Dictionary<(string File, uint Line, uint Col), CoverageBranch>();

    for (uint i = 0; i < sidecar.CoveragePointCount; i++) {
      var point = sidecar.CoveragePoint(i);
      if (!point.IsThenArm && !point.IsElseArm) continue;

      var key = (point.File, point.Line, point.Col);
      var branch = byBranch.TryGetValue(key, out var existing)
        ? existing
        : new CoverageBranch(point.File, point.Line, point.Col, 0, 0, false, false, false);

      ulong count = point.Eliminated ? 0 : data.Counters[(int)i];
      byBranch[key] = point.IsThenArm
        ? branch with { ThenCount = count, ThenEliminated = point.Eliminated }
        : branch with {
            ElseCount = count, ElseEliminated = point.Eliminated, ElseImplicit = point.IsImplicitArm
          };
    }

    return [.. byBranch.Values
      .OrderBy(b => b.File, StringComparer.Ordinal).ThenBy(b => b.Line).ThenBy(b => b.Col)];
  }
}

/// <summary>
/// The two faces of one report: an annotated source listing for a human, and JSON for a tool. Both
/// read the SAME <see cref="CoverageReport"/> — a second model for the second face is the defect
/// this workstream keeps finding, most recently as three copies of one rule that had diverged into a
/// live wrong answer.
/// </summary>
public static class CoverageRender {
  /// Marker for a line with real code that never ran, in gcov's own spelling.
  private const string NotCoveredMarker = "#####";
  /// Marker for a line whose code the optimizer removed. The fourth state needs its OWN mark: shown
  /// as `#####` it would read as an untested line, and shown blank it would hide among the comments.
  private const string EliminatedMarker = "-----";
  private const int CountColumnWidth = 7;

  /// The rule between a report's sections. Deliberately NOT `---`: the DebugSamples golden format ends
  /// an expected block at the first line starting with `--- `, so a report that ruled its sections
  /// that way is silently TRUNCATED at its first file header when a transcript of it is committed —
  /// as this one was, until the gate caught it.
  private const string SectionRule = "===";

  /// <summary>
  /// The path a report shows: relative to the working directory when the file is under it, so a
  /// transcript is the same on every machine, and absolute when it is not (which is honest — a file
  /// outside the tree has no shorter true name).
  /// </summary>
  public static string DisplayPath(string path) {
    try {
      var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), path);
      return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
        ? path.Replace('\\', '/')
        : relative.Replace('\\', '/');
    } catch (ArgumentException) {
      return path.Replace('\\', '/');
    }
  }

  public static string Text(CoverageReport report) {
    var sb = new StringBuilder();
    sb.Append("coverage: ").Append(DisplayPath(report.ExePath)).Append('\n');
    if (!report.RunCompleted) {
      sb.Append("PARTIAL: the run did not complete — these counts stop where the program died\n");
    }
    sb.Append($"lines: {report.LinesCovered}/{report.LinesInstrumented} covered")
      .Append($"  branches: {report.ArmsTaken}/{report.ArmsInstrumented} arms taken")
      .Append($"  eliminated: {report.LinesEliminated} lines\n");
    sb.Append($"legend: a count = executed, {NotCoveredMarker} = never executed, ")
      .Append($"{EliminatedMarker} = no code emitted (optimized away), blank = no coverage point\n");

    foreach (var file in report.Files) {
      sb.Append('\n').Append(SectionRule).Append(' ').Append(DisplayPath(file.Path))
        .Append(' ').Append(SectionRule).Append('\n');
      AppendListing(sb, file);
    }

    if (report.Branches.Count > 0) {
      sb.Append('\n').Append(SectionRule).Append(" branches ").Append(SectionRule).Append('\n');
      foreach (var b in report.Branches) {
        sb.Append("  ").Append(DisplayPath(b.File)).Append(':').Append(b.Line).Append(':').Append(b.Col)
          .Append("  then=").Append(ArmText(b.ThenCount, b.ThenEliminated))
          .Append("  else=").Append(ArmText(b.ElseCount, b.ElseEliminated))
          .Append(b.ElseImplicit ? " (else is implicit)" : "")
          .Append('\n');
      }
    }
    return sb.ToString();
  }

  private static string ArmText(ulong count, bool eliminated) => eliminated ? EliminatedMarker : count.ToString();

  /// <summary>
  /// The gcov-style annotated listing. Reading the source is best-effort: a file that has moved
  /// since the build still gets its per-line states, with the text replaced by a stated placeholder
  /// rather than the report silently dropping the lines it cannot show.
  /// </summary>
  private static void AppendListing(StringBuilder sb, CoverageFile file) {
    var states = file.Lines.ToDictionary(l => l.Line);
    string[]? source = TryReadLines(file.Path);

    if (source == null) {
      sb.Append("  (source not available: ").Append(DisplayPath(file.Path)).Append(")\n");
      foreach (var line in file.Lines)
        sb.Append(Marker(line).PadLeft(CountColumnWidth)).Append(" |").Append(line.Line).Append('\n');
      return;
    }

    for (int i = 0; i < source.Length; i++) {
      uint lineNumber = (uint)(i + 1);
      var marker = states.TryGetValue(lineNumber, out var state) ? Marker(state) : "";
      sb.Append(marker.PadLeft(CountColumnWidth)).Append(" |")
        .Append(lineNumber.ToString().PadLeft(4)).Append("| ").Append(source[i]).Append('\n');
    }
  }

  private static string Marker(CoverageLine line) => line.State switch {
    CoverageLineState.Covered => line.Count.ToString(),
    CoverageLineState.NotCovered => NotCoveredMarker,
    CoverageLineState.Eliminated => EliminatedMarker,
    _ => throw new InvalidOperationException($"Unhandled coverage line state: {line.State}")
  };

  private static string[]? TryReadLines(string path) {
    try {
      // Split on '\n' and trim a trailing '\r' so a CRLF checkout renders the same as an LF one.
      return [.. File.ReadAllText(path).Split('\n').Select(l => l.TrimEnd('\r'))];
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) {
      return null;
    }
  }

  public static string Json(CoverageReport report) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      w.WriteString("exe", DisplayPath(report.ExePath));
      w.WriteBoolean("runCompleted", report.RunCompleted);
      w.WriteNumber("linesCovered", report.LinesCovered);
      w.WriteNumber("linesInstrumented", report.LinesInstrumented);
      w.WriteNumber("linesEliminated", report.LinesEliminated);
      w.WriteNumber("armsTaken", report.ArmsTaken);
      w.WriteNumber("armsInstrumented", report.ArmsInstrumented);

      w.WriteStartArray("files");
      foreach (var file in report.Files) {
        w.WriteStartObject();
        w.WriteString("path", DisplayPath(file.Path));
        w.WriteStartArray("lines");
        foreach (var line in file.Lines) {
          w.WriteStartObject();
          w.WriteNumber("line", line.Line);
          w.WriteString("state", StateName(line.State));
          if (line.State != CoverageLineState.Eliminated) w.WriteNumber("count", line.Count);
          w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
      }
      w.WriteEndArray();

      w.WriteStartArray("branches");
      foreach (var b in report.Branches) {
        w.WriteStartObject();
        w.WriteString("file", DisplayPath(b.File));
        w.WriteNumber("line", b.Line);
        w.WriteNumber("col", b.Col);
        WriteArm(w, "then", b.ThenCount, b.ThenEliminated);
        WriteArm(w, "else", b.ElseCount, b.ElseEliminated);
        w.WriteBoolean("elseImplicit", b.ElseImplicit);
        w.WriteEndObject();
      }
      w.WriteEndArray();
      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  /// An eliminated arm reports no count at all rather than a zero a consumer would read as "never
  /// taken" — the same distinction the text listing draws with its own marker.
  private static void WriteArm(Utf8JsonWriter w, string name, ulong count, bool eliminated) {
    if (eliminated) w.WriteString(name, "eliminated");
    else w.WriteNumber(name, count);
  }

  private static string StateName(CoverageLineState state) => state switch {
    CoverageLineState.Covered => "covered",
    CoverageLineState.NotCovered => "notCovered",
    CoverageLineState.Eliminated => "eliminated",
    _ => throw new InvalidOperationException($"Unhandled coverage line state: {state}")
  };
}
