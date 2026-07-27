using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MaxonSharp.Debug;

/// One function's share. <see cref="SelfSamples"/> is samples caught EXECUTING it (it was the leaf);
/// <see cref="TotalSamples"/> is samples anywhere beneath it, counted once per stack so recursion does
/// not multiply a function by its own depth.
public sealed record ProfileFunction(string Name, long SelfSamples, long TotalSamples);

/// <summary>
/// One node of the call tree. <see cref="Samples"/> is every sample whose stack passed THROUGH this
/// node, so a node's own time is what its children do not account for — see <see cref="SelfSamples"/>.
/// </summary>
public sealed record ProfileTreeNode(string Function, long Samples, IReadOnlyList<ProfileTreeNode> Children) {
  public long SelfSamples => Samples - Children.Sum(c => c.Samples);
}

/// <summary>
/// One STACK's share — an OS thread's or a green thread's, which out of process are the same kind of
/// thing (see <see cref="ProfileSampler"/>). Named by its outermost frame, because that is what says
/// what ran there; the allocation base that identifies it is deliberately NOT reported, being a machine
/// address that would make every transcript host-specific and mean nothing to a reader.
///
/// Two rows can share a name, and that is information rather than a defect: it means two separate
/// stacks were running the same entry function.
/// </summary>
public sealed record ProfileStack(string RootFunction, long Samples);

/// <summary>
/// What a sampling run measured — the ONE model both faces render.
///
/// The honesty fields are not decoration; each answers a question a percentage cannot.
/// <see cref="Samples"/> with <see cref="AchievedRateHz"/> and <see cref="DurationSeconds"/> is the
/// difference between 3 samples and 30,000, which look identical once divided.
/// <see cref="SamplesUnsymbolized"/> is how much of the profile landed in code this binary's sidecar
/// cannot name, so a reader knows how much of the rest to trust.
/// <see cref="StacksTruncated"/> matters more than its size suggests: a walk runs leaf-to-ROOT, so a
/// truncated stack has lost its outermost frames and is attributed under whatever it reached.
///
/// ⚠ <see cref="StacksUnreadable"/> and <see cref="SamplesUnsymbolized"/> are INDEPENDENT, not nested,
/// and it is worth knowing which way round: the first is about the stack BEYOND the leaf and the second
/// is about the leaf itself. A sample can be perfectly symbolized and still unreadable — the registers
/// were captured, the leaf named, and only the walk failed, so the sample counts toward `self` time for
/// a real function while contributing no call path. (Verified by forcing the walk to fail: 912 of 912
/// stacks unreadable, 0 samples unsymbolized.)
/// </summary>
/// <param name="TargetExitCode">
/// The status the measured program returned. DATA, never this tool's own exit code — the same policy
/// `maxon debug --batch` settles by emitting `{"event":"exit","code":7}` and itself exiting 0.
/// </param>
/// <param name="RunCompleted">
/// False when the program was still running when measurement stopped (it hit the deadline), so the
/// profile describes a prefix of it.
/// </param>
/// <param name="MinPercent">
/// The share below which a function or a node is summarized rather than listed. It exists because a
/// sampling profiler's LONG TAIL is noise — a function that caught one sample is not a finding — and a
/// report whose contents changed run to run could not be read as a measurement. Everything hidden is
/// counted in the "not shown" lines, so the report never silently drops what it measured.
/// </param>
public sealed record ProfileReport(
  string ExePath,
  int? TargetExitCode,
  bool RunCompleted,
  double RequestedRateHz,
  long Ticks,
  double DurationSeconds,
  long Samples,
  long SamplesUnsymbolized,
  long StacksUnreadable,
  long StacksTruncated,
  double MinPercent,
  IReadOnlyList<ProfileFunction> Functions,
  IReadOnlyList<ProfileTreeNode> Roots,
  IReadOnlyList<ProfileStack> Stacks) {

  /// <summary>
  /// Below this, a breakdown is arithmetic rather than measurement: at 40 samples a function shown as
  /// "5.0%" is two samples, and one more would have made it 7.5%. The report says so rather than
  /// presenting a confident-looking table built on nothing, which is the same refusal
  /// `CoverageReport.RunCompleted` makes about a run that died early.
  /// </summary>
  public const long MinimumTrustworthySamples = 100;

  public bool Sparse => Samples < MinimumTrustworthySamples;

  /// <summary>
  /// The rate the sampler ACTUALLY ticked at — <see cref="Ticks"/> over <see cref="DurationSeconds"/>.
  ///
  /// ⭐ THIS, NOT <see cref="RequestedRateHz"/>, IS WHAT THE REPORT MEANS. A sampling profiler's rate is
  /// not decoration beside the numbers, it is the DENOMINATOR every percentage in the report is computed
  /// against, so printing a rate the run did not achieve states a wrong answer about every row at once.
  /// It is reachable: the loop ticks on an OS waitable timer whose granularity this host puts at ~0.5 ms,
  /// so a caller asking <c>--rate=5000</c> gets about 1,750 (see <see cref="ProfileRunner.MaxRateHz"/>)
  /// — and that ceiling is deliberately NOT enforced as a lower <c>MaxRateHz</c>, because ~0.5 ms is one
  /// machine's granularity rather than a portable fact. Reporting what was achieved is what makes an
  /// over-ask self-documenting instead of a silent lie.
  ///
  /// ⚠ Derived from TICKS rather than samples on purpose. One tick samples every thread that has RUN
  /// since the last one, so `Samples / DurationSeconds` is samples-per-second and drifts from the tick
  /// rate in BOTH directions by a program-dependent amount — up when threads run concurrently, down when
  /// none ran. Both are real numbers; only this one answers the question `--rate` asks.
  /// </summary>
  public double AchievedRateHz => DurationSeconds > 0 ? Ticks / DurationSeconds : 0;
}

/// <summary>
/// The three faces of one profile: a summary a human reads, JSON for an agent, and collapsed-stack
/// `folded` for a flamegraph tool. All three read the SAME <see cref="ProfileReport"/>.
///
/// `folded` is the machine format rather than any flamegraph renderer's own, because it is the one every
/// such renderer already reads — flamegraph.pl, speedscope, inferno — so the profile lands in existing
/// tooling without this project shipping a viewer. It is also the format with the least in it: one line
/// per distinct stack, `root;child;leaf <count>`, which is exactly what the call tree already holds.
/// </summary>
public static class ProfileRender {

  /// The rule between sections — <see cref="ReportFormat.SectionRule"/>, which states once why it may
  /// not be `---`. It was a second copy of that constant and of its warning, written because this
  /// report arrived after the coverage one had already paid for the lesson.
  private const string SectionRule = ReportFormat.SectionRule;

  /// <summary>
  /// ⭐ THE ONE CONVENTION FOR A FRAME THE PROFILE COULD NOT NAME: it is written in BRACKETS.
  ///
  /// Every such frame below is bracketed, and no function a programmer can write is — a Maxon
  /// identifier cannot begin with `[` — so <see cref="IsSynthetic"/> tells "we could not name this" from
  /// "this is what it is called" with a single test that cannot fall out of date as placeholders are
  /// added. It was a closed list of three constants for exactly as long as it took a FOURTH kind to
  /// exist (a named foreign module), which the list would have silently classified as a real function:
  /// counted as symbolized, and eligible to become a stack's name.
  /// </summary>
  public static bool IsSynthetic(string frame) => frame.StartsWith('[');

  /// The frame a sample gets when its leaf PC is not in this binary's code and no loaded module claims
  /// it. Named rather than dropped, and never renamed to the nearest known function.
  public const string OutsideTextFrame = "[outside .text]";

  /// The frame for an address that IS in this binary's `.text` but that no function record covers — a
  /// runtime helper the sidecar does not name. Distinct from <see cref="OutsideTextFrame"/> because the
  /// two send a reader somewhere different: this one is our code, that one is not.
  public const string UnnamedTextFrame = "[unnamed .text]";

  /// The frame for a stack that could not be read at all, so not even a leaf survives.
  public const string UnreadableStackFrame = "[unreadable stack]";

  /// A leaf outside this binary's `.text`, named by the module that owns it where one does. Still
  /// UNSYMBOLIZED — a module name says which library, not which function — so it is bracketed and
  /// counts against the profile's own honesty figure exactly as the anonymous form does.
  public static string ForeignFrame(string? moduleName) =>
    moduleName is { Length: > 0 } name ? $"[{name}]" : OutsideTextFrame;

  private const int PercentColumnWidth = 6;
  private const int CountColumnWidth = 7;

  public static string Text(ProfileReport report) {
    var sb = new StringBuilder();
    sb.Append("profile: ").Append(ReportPath.Display(report.ExePath)).Append('\n');
    if (report.TargetExitCode is { } exitCode)
      sb.Append("target exit code: ").Append(exitCode).Append('\n');

    // ⭐ THE ACHIEVED RATE IS THE HEADLINE and the requested one is the parenthetical, because the
    // achieved rate is what every percentage below was computed against while the requested rate is
    // only what somebody typed. Both are always printed, rather than collapsing to one line when they
    // agree: whether they agree is itself a MEASURED quantity that lands wherever the host's timer and
    // load put it, so a report whose shape depended on it would change shape run to run — and this
    // report's whole discipline is that its structure is stable while its numbers are not.
    sb.Append("samples: ").Append(report.Samples)
      .Append(" at ").Append(Rate(report.AchievedRateHz)).Append(" Hz")
      .Append(" over ").Append(Number(report.DurationSeconds)).Append('s')
      .Append(" (requested ").Append(Number(report.RequestedRateHz)).Append(" Hz)\n");

    if (!report.RunCompleted)
      sb.Append("PARTIAL: the program was still running when sampling stopped —"
        + " this profile describes the part that ran\n");

    if (report.Sparse)
      sb.Append($"TOO FEW SAMPLES: under {ProfileReport.MinimumTrustworthySamples}, so the shares below are"
        + " arithmetic on a handful of readings rather than a measurement."
        + " Profile a longer run, or raise the rate\n");

    // Always stated, including as zero. "How much of this could not be named" is the number that says
    // whether to trust the rest, and a line that appears only when it is bad teaches a reader to assume
    // its absence means something else.
    sb.Append("unsymbolized: ").Append(report.SamplesUnsymbolized).Append(" samples (")
      .Append(Percent(report.SamplesUnsymbolized, report.Samples)).Append(")\n");
    sb.Append("stacks: ").Append(report.StacksTruncated).Append(" truncated, ")
      .Append(report.StacksUnreadable).Append(" unreadable\n");

    AppendFunctions(sb, report);
    AppendTree(sb, report);
    AppendStacks(sb, report);
    return sb.ToString();
  }

  private static void AppendFunctions(StringBuilder sb, ProfileReport report) {
    sb.Append('\n').Append(SectionRule).Append(" hot functions (self time) ").Append(SectionRule).Append('\n');

    long shown = 0;
    foreach (var fn in report.Functions) {
      if (!AtLeast(fn.SelfSamples, report)) continue;
      AppendRow(sb, fn.SelfSamples, report.Samples, 0, fn.Name);
      shown++;
    }
    AppendHidden(sb, report.Functions.Count - shown, "functions", report.MinPercent);
  }

  private static void AppendTree(StringBuilder sb, ProfileReport report) {
    sb.Append('\n').Append(SectionRule).Append(" call tree ").Append(SectionRule).Append('\n');

    long hidden = 0;
    foreach (var root in report.Roots) AppendNode(sb, root, report, depth: 0, ref hidden);
    AppendHidden(sb, hidden, "nodes", report.MinPercent);
  }

  private static void AppendNode(StringBuilder sb, ProfileTreeNode node, ProfileReport report, int depth,
      ref long hidden) {
    if (!AtLeast(node.Samples, report)) {
      hidden += 1 + CountDescendants(node);
      return;
    }

    AppendRow(sb, node.Samples, report.Samples, depth, node.Function);
    foreach (var child in node.Children) AppendNode(sb, child, report, depth + 1, ref hidden);
  }

  private static long CountDescendants(ProfileTreeNode node) =>
    node.Children.Sum(c => 1 + CountDescendants(c));

  private static void AppendStacks(StringBuilder sb, ProfileReport report) {
    sb.Append('\n').Append(SectionRule).Append(" stacks ").Append(SectionRule).Append('\n');
    sb.Append("  one row per stack that ran — an OS thread's or a green thread's — named by its"
      + " outermost frame\n");

    long shown = 0;
    foreach (var stack in report.Stacks) {
      if (!AtLeast(stack.Samples, report)) continue;
      AppendRow(sb, stack.Samples, report.Samples, 0, stack.RootFunction);
      shown++;
    }
    AppendHidden(sb, report.Stacks.Count - shown, "stacks", report.MinPercent);
  }

  /// Percent, count, then the name — in that order so a transcript's numeric columns sit at a fixed
  /// position and the indentation that carries the tree's SHAPE is never disturbed by a wider count.
  private static void AppendRow(StringBuilder sb, long samples, long total, int depth, string name) =>
    sb.Append(Percent(samples, total).PadLeft(PercentColumnWidth))
      .Append("  ").Append(samples.ToString(CultureInfo.InvariantCulture).PadLeft(CountColumnWidth))
      .Append("  ").Append(' ', depth * 2).Append(name).Append('\n');

  /// <summary>
  /// How much of a section was summarized away — ALWAYS stated, including as zero, for the reason the
  /// unsymbolized line is: a line that appears only when it has something to report teaches a reader
  /// that its absence means something else, when in fact absence and zero are the same fact.
  ///
  /// It also makes a profile's shape stable, which for a nondeterministic instrument is worth as much:
  /// a single stray sample in a foreign module adds one hidden row, and with a conditional line that
  /// turned a report that was otherwise identical run to run into one that grew two lines at random —
  /// measured, as a golden that passed three times and failed the fourth.
  /// </summary>
  private static void AppendHidden(StringBuilder sb, long hidden, string noun, double minPercent) =>
    sb.Append("  (").Append(hidden).Append(' ').Append(noun).Append(" below ")
      .Append(Number(minPercent)).Append("% not shown)\n");

  /// Whether a row clears the reporting floor. Compared as a PRODUCT rather than by dividing, so a
  /// zero-sample profile cannot divide by zero on its way to hiding everything anyway.
  private static bool AtLeast(long samples, ProfileReport report) =>
    samples * 100.0 >= report.MinPercent * report.Samples;

  private static string Percent(long samples, long total) =>
    total == 0 ? "0.0%" : (100.0 * samples / total).ToString("0.0", CultureInfo.InvariantCulture) + "%";

  /// The shortest round-trip decimal form — the same spelling `Utf8JsonWriter` gives the same double,
  /// so the prose face and the JSON face cannot print different numbers for one measurement.
  private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

  /// A MEASURED rate, to whole hertz. Unlike every other number here it is a ratio of two measurements,
  /// so its full binary expansion (`963.9614325876122`) is a dozen digits of noise around a figure whose
  /// last honest digit is the first — and the JSON face carries the exact ticks and duration for anyone
  /// who wants to divide them differently.
  private static string Rate(double hz) => hz.ToString("0", CultureInfo.InvariantCulture);

  /// <summary>
  /// Collapsed stacks: one line per distinct call path, `root;child;leaf &lt;samples&gt;`.
  ///
  /// Every node with self time produces a line, which is exactly the set of distinct stacks the run
  /// caught — a tree node's self count IS the number of samples whose leaf was that path. Nothing is
  /// filtered by <see cref="ProfileReport.MinPercent"/> here, deliberately: that floor exists to keep a
  /// HUMAN's table stable, while a flamegraph tool does its own thresholding and would rather be given
  /// everything. Sorted by path so two runs of one program produce diffable output.
  /// </summary>
  public static string Folded(ProfileReport report) {
    var lines = new List<string>();
    foreach (var root in report.Roots) CollectFolded(root, "", lines);
    lines.Sort(StringComparer.Ordinal);

    var sb = new StringBuilder();
    foreach (var line in lines) sb.Append(line).Append('\n');
    return sb.ToString();
  }

  private static void CollectFolded(ProfileTreeNode node, string prefix, List<string> lines) {
    var path = prefix.Length == 0 ? node.Function : prefix + ";" + node.Function;
    if (node.SelfSamples > 0)
      lines.Add($"{path} {node.SelfSamples.ToString(CultureInfo.InvariantCulture)}");

    foreach (var child in node.Children) CollectFolded(child, path, lines);
  }

  public static string Json(ProfileReport report) {
    using var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false })) {
      w.WriteStartObject();
      w.WriteString("exe", ReportPath.Display(report.ExePath));
      if (report.TargetExitCode is { } exitCode) w.WriteNumber("targetExitCode", exitCode);
      w.WriteBoolean("runCompleted", report.RunCompleted);
      // Both rates, named for which is which, plus the two raw measurements they come from — so a
      // consumer can divide them itself rather than trusting a rounded figure, and cannot mistake the
      // rate that was asked for for the one every share below was computed against.
      w.WriteNumber("achievedRateHz", report.AchievedRateHz);
      w.WriteNumber("requestedRateHz", report.RequestedRateHz);
      w.WriteNumber("ticks", report.Ticks);
      w.WriteNumber("durationSeconds", report.DurationSeconds);
      w.WriteNumber("samples", report.Samples);
      w.WriteNumber("samplesUnsymbolized", report.SamplesUnsymbolized);
      w.WriteNumber("stacksUnreadable", report.StacksUnreadable);
      w.WriteNumber("stacksTruncated", report.StacksTruncated);
      // Written only where it is TRUE, so it states a fact rather than being a field that reads false
      // on every healthy profile.
      if (report.Sparse) w.WriteBoolean("sparse", true);

      // The FULL tables travel to an agent, unfiltered by MinPercent: the floor exists to keep a
      // printed table stable for a human, and silently truncating a machine-readable answer to a
      // display setting would make the JSON face a worse instrument than the text one.
      w.WriteStartArray("functions");
      foreach (var fn in report.Functions) {
        w.WriteStartObject();
        w.WriteString("name", fn.Name);
        w.WriteNumber("selfSamples", fn.SelfSamples);
        w.WriteNumber("totalSamples", fn.TotalSamples);
        w.WriteEndObject();
      }
      w.WriteEndArray();

      w.WriteStartArray("tree");
      foreach (var root in report.Roots) WriteNode(w, root);
      w.WriteEndArray();

      w.WriteStartArray("stacks");
      foreach (var stack in report.Stacks) {
        w.WriteStartObject();
        w.WriteString("root", stack.RootFunction);
        w.WriteNumber("samples", stack.Samples);
        w.WriteEndObject();
      }
      w.WriteEndArray();
      w.WriteEndObject();
    }
    return Encoding.UTF8.GetString(buffer.ToArray());
  }

  private static void WriteNode(Utf8JsonWriter w, ProfileTreeNode node) {
    w.WriteStartObject();
    w.WriteString("function", node.Function);
    w.WriteNumber("samples", node.Samples);
    w.WriteNumber("selfSamples", node.SelfSamples);
    if (node.Children.Count > 0) {
      w.WriteStartArray("children");
      foreach (var child in node.Children) WriteNode(w, child);
      w.WriteEndArray();
    }
    w.WriteEndObject();
  }
}
