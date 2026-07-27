using System.Security.Cryptography;
using System.Text;
using MaxonSharp.Compiler;

namespace MaxonSharp.Testing;

/// <summary>
/// One test file and the tests in it, in discovery order. The unit both the compile and the run are
/// organized around: a file is what a dispatcher is generated for, what a shard runs, and what the
/// report groups by.
/// </summary>
/// <param name="Path">The <c>*.test.maxon</c> file, as the compiler recorded it.</param>
/// <param name="RunnerName">
/// The generated function's own name, as DECLARED inside the test file.
/// </param>
/// <param name="RunnerSymbol">
/// The same function namespace-qualified, as the root entry must CALL it from another namespace.
///
/// Both are stored because both are needed, and minting them together is what keeps them a pair: it
/// was one qualified string that three call sites re-split on the last dot — a third copy of the
/// mangling rule <see cref="DiscoveredTest.ShortSymbol"/> already states.
/// </param>
/// <param name="Tests">
/// The file's tests. Each <see cref="IndexedTest.Index"/> is its position in the whole run, which is
/// what crosses the wire.
/// </param>
internal sealed record TestFileGroup(
  string Path, string RunnerName, string RunnerSymbol, IReadOnlyList<IndexedTest> Tests);

/// <summary>A discovered test paired with its position in the whole run.</summary>
/// <param name="Index">
/// The test's position in discovery order. This — never its prose name — is what the binary reports
/// and what a selection names, so no author-controlled text ever reaches the wire and no test can
/// forge another's report by being called something.
/// </param>
internal sealed record IndexedTest(int Index, DiscoveredTest Test);

/// <summary>
/// Generates the Maxon source that RUNS the tests: one dispatcher per test file, plus the entry
/// point that calls them.
///
/// Nothing here is written to disk. The generated text is folded into the
/// <see cref="SourceFile"/> array the collector already returned and handed straight to the
/// compiler, which is what keeps a `maxon test` run from leaving files in a user's tree and what
/// makes it safe to run two at once.
///
/// It does NOT go through <see cref="SourceCollector.FromDirectory"/>'s injected-source channel —
/// the one the LSP feeds unsaved editor buffers through — for a reason that is not taste: that
/// channel carries a path and text, and this needs to carry a third thing per file, the set of
/// declarations the compiler is allowed to give the reserved <c>__</c> prefix (see
/// <see cref="SourceFile.CompilerOwnedDeclarations"/>). Widening a shared collector's signature for
/// one caller, or smuggling the set alongside by some parallel route, are both worse than building
/// the array here — where the content and the names in it are decided together and cannot disagree.
/// Folding it in also preserves the collector's file ORDER, which the parse depends on in places.
///
/// The per-file dispatcher is APPENDED TO ITS FILE'S OWN CONTENT rather than placed in a file of its
/// own, and that is not a convenience — it is the only thing that compiles. A <c>test</c> declaration
/// is NOT exported: neither <c>PreScanTest</c> nor <c>ParseTest</c> sets <c>IsExported</c>, so
/// <c>__test_&lt;sanitized&gt;</c> is reachable only from its own file. Source anywhere else cannot
/// call it. The same property is what lets a test use the file's PRIVATE helpers and fixtures
/// without anything being <c>export</c>ed to accommodate the harness.
///
/// Only the root entry — which must reach ACROSS namespaces, and so calls the exported per-file
/// runners rather than the tests themselves — is a file of its own.
/// </summary>
internal static class TestDispatcher {
  /// <summary>
  /// The entry point the test binary is compiled with. <c>export</c> because
  /// <see cref="SemanticCheckPass"/> requires any entry function not called <c>main</c> to be
  /// exported; the <c>__</c> prefix marks it compiler-owned, as <c>__TestReport</c> is.
  ///
  /// Naming a non-<c>main</c> entry is also what lets a project's OWN <c>main</c> stay exactly where
  /// it is: it simply stops being reachable, and dead-function elimination drops it.
  /// </summary>
  public const string EntrySymbol = "__maxon_test_main";

  /// <summary>The generated type carrying the run's shared helpers. One copy, called from every file.</summary>
  private const string HelperType = "__MaxonTestRun";

  /// <summary>
  /// File name given to the synthesized root-entry source. It is not a <c>*.test.maxon</c> — it
  /// holds no <c>test</c> declaration — and it sits at the project root so its <c>export</c>s are
  /// visible to every namespace below.
  ///
  /// The name is never opened, but it is what a compile error in generated source is reported
  /// against, so it says what it is.
  /// </summary>
  private const string EntryFileName = "__maxon_test_entry.maxon";

  /// <summary>Prefix of the per-file dispatcher function.</summary>
  private const string FileRunnerPrefix = "__maxon_test_file_";

  /// <summary>
  /// Range of the generated index typealias. A run's tests are numbered from 0, and the upper bound
  /// is stated rather than left at <c>u64.max</c> because the narrowest correct range is the rule —
  /// a project with more tests than this has other problems.
  /// </summary>
  private const int MaxTestsPerRun = 1_000_000;

  /// <summary>
  /// Everything the compile needs: the sources to feed it and the key that distinguishes this
  /// generated source from any other.
  /// </summary>
  /// <param name="Sources">
  /// The full source set: every real file, with each test file's text carrying its appended
  /// dispatcher, plus the synthesized root entry.
  /// </param>
  /// <param name="Groups">The files and their tests, in discovery order.</param>
  /// <param name="ExtraKey">
  /// A hash of every byte of generated source. It is the build cache's <c>extraKey</c>, because that
  /// cache keys on-disk sources by last-write time and generated source has none — two different
  /// dispatchers would otherwise compare equal and the second build would be handed the first's
  /// binary.
  /// </param>
  internal sealed record Generated(
    SourceFile[] Sources,
    IReadOnlyList<TestFileGroup> Groups,
    string ExtraKey);

  /// <summary>
  /// Build the dispatchers for <paramref name="groups"/>.
  ///
  /// EVERY discovered test is generated in, always. Filtering happens at RUN time, through
  /// <see cref="MaxonTestProtocol.SelectFlag"/> — which is precisely what lets `-t` change between
  /// runs without invalidating the build cache, and what keeps the thing being tested identical to
  /// the thing that was compiled.
  /// </summary>
  /// <param name="groups">The files and their tests, from <see cref="GroupTests"/>.</param>
  /// <param name="projectDir">The compile root; the root entry is placed here.</param>
  /// <param name="collected">
  /// Every source the project already has, as collected from disk. Test files are returned with the
  /// dispatcher appended; everything else passes through untouched, in the same order — the parse is
  /// order-sensitive in places, so the collector's order is preserved rather than rebuilt.
  /// </param>
  public static Generated Generate(
      IReadOnlyList<TestFileGroup> groups, string projectDir, SourceFile[] collected) {
    var byPath = groups.ToDictionary(g => g.Path, g => g, StringComparer.Ordinal);
    var keyMaterial = new StringBuilder();
    var sources = new List<SourceFile>(collected.Length + 1);

    foreach (var source in collected) {
      if (!byPath.TryGetValue(source.Path, out var group)) {
        sources.Add(source);
        continue;
      }

      var dispatcher = FileDispatcherSource(group);
      sources.Add(source with {
        Content = source.Content + dispatcher,
        CompilerOwnedDeclarations = FileDispatcherOwnedNames(group),
      });
      keyMaterial.Append(source.Path).Append('\n').Append(dispatcher).Append('\n');
    }

    // Every discovered test file must have been among the collected sources; discovery read them.
    if (sources.Count(s => s.CompilerOwnedDeclarations != null) != groups.Count) {
      throw new InvalidOperationException(
        "discovery found tests in a file the source collector did not return; the two disagree "
        + "about which files exist.");
    }

    var entrySource = EntrySource(groups);
    var entryPath = SourceCollector.NormalizePath(System.IO.Path.Combine(projectDir, EntryFileName));
    sources.Add(new SourceFile(entryPath, entrySource, projectDir, EntryOwnedNames()));
    keyMaterial.Append(entryPath).Append('\n').Append(entrySource);

    return new Generated([.. sources], groups, HashOf(keyMaterial.ToString()));
  }

  /// <summary>
  /// The reserved-prefix names the appended dispatcher declares in one test file — exactly one, its
  /// runner. Derived from the same value the source is generated from, so the exemption cannot name
  /// something other than what was written.
  /// </summary>
  private static IReadOnlySet<string> FileDispatcherOwnedNames(TestFileGroup group) =>
    new HashSet<string>(StringComparer.Ordinal) { group.RunnerName };

  /// <summary>The reserved-prefix names the root entry file declares.</summary>
  private static IReadOnlySet<string> EntryOwnedNames() =>
    new HashSet<string>(StringComparer.Ordinal) { HelperType, EntrySymbol };

  /// <summary>
  /// Partition tests into files, PRESERVING discovery order both between files and within one.
  ///
  /// Order is not cosmetic here: it is the run's numbering, the report's reading order, and the
  /// thing that makes a redirected run byte-reproducible. Sorting by name instead would make the
  /// report stable and the numbering depend on a sort nobody asked for.
  ///
  /// Separate from <see cref="Generate"/> because <c>--list</c> needs the grouping and nothing else:
  /// it is documented as compiling nothing, and synthesizing a dispatcher it will never hand to the
  /// compiler is the same kind of quiet untruth.
  /// </summary>
  public static IReadOnlyList<TestFileGroup> GroupTests(IReadOnlyList<DiscoveredTest> tests) {
    // Refused here, by count, rather than left to fail as a range violation inside source the user
    // cannot open: every index is typed `MaxonTestIndex`, so the (absurd) overflow would otherwise
    // surface as a type error in generated text naming a typealias nobody wrote.
    // `>` and not `>=`: indices run 0..Count-1, so exactly MaxTestsPerRun tests is the largest run
    // whose every index fits `int(0 to MaxTestsPerRun)`.
    if (tests.Count > MaxTestsPerRun) {
      throw new InvalidOperationException(
        $"{tests.Count} tests exceeds the {MaxTestsPerRun} a single run is numbered for.");
    }

    return GroupByFile(tests);
  }

  private static List<TestFileGroup> GroupByFile(IReadOnlyList<DiscoveredTest> tests) {
    var order = new List<string>();
    var byFile = new Dictionary<string, List<IndexedTest>>(StringComparer.Ordinal);

    for (var i = 0; i < tests.Count; i++) {
      var test = tests[i];
      if (!byFile.TryGetValue(test.File, out var list)) {
        list = [];
        byFile[test.File] = list;
        order.Add(test.File);
      }
      list.Add(new IndexedTest(i, test));
    }

    var groups = new List<TestFileGroup>(order.Count);
    for (var f = 0; f < order.Count; f++) {
      var path = order[f];
      var members = byFile[path];
      // The index makes the name unique by construction. A sanitized path alone would not be: two
      // files in ONE directory whose names differ only outside [A-Za-z0-9_] share a namespace and
      // would sanitize alike.
      var runnerName = FileRunnerPrefix + f;
      groups.Add(new TestFileGroup(path, runnerName,
        Qualify(members[0].Test.Namespace, runnerName), members));
    }
    return groups;
  }

  /// <summary>
  /// Join a namespace to a short name the way the compiler's mangling does. Empty namespace — a file
  /// at the compile root — takes the bare name.
  /// </summary>
  private static string Qualify(string ns, string shortName) =>
    ns.Length == 0 ? shortName : $"{ns}.{shortName}";

  /// <summary>
  /// The dispatcher appended to one test file.
  ///
  /// Each test is wrapped in a selection guard, a begin marker, a timer and a handler. The handler
  /// is the block form of <c>otherwise</c> setting a variable, rather than two exits, because
  /// control CONTINUES past an <c>otherwise</c> block — so one report site serves both outcomes and
  /// there is no second place for pass and fail to be reported differently.
  ///
  /// A test throws exactly <c>TestFailure</c>, so this catches everything a test body can raise; a
  /// foreign error was already converted (and reported) by the handler the compiler substitutes.
  /// What it cannot catch is a <c>panic</c>, which is uncatchable by design — that is why the begin
  /// marker is written BEFORE the body, and why a begin with no end is the crash signal.
  /// </summary>
  private static string FileDispatcherSource(TestFileGroup group) {
    var sb = new StringBuilder();
    sb.Append('\n');
    sb.Append("// Generated by `maxon test` — never written to disk. It lives in this file because a\n");
    sb.Append("// `test` is not exported, so nowhere else can call one. See TestDispatcher.\n");
    sb.Append($"export function {group.RunnerName}(selection String)\n");

    foreach (var (index, test) in group.Tests.Select(t => (t.Index, t.Test))) {
      var guard = $"maxonTestGuard{index}";
      var caught = $"maxonTestCaught{index}";

      sb.Append($"\tif {HelperType}.selected(selection, index: {index}) '{guard}'\n");
      sb.Append($"\t\t{HelperType}.began({index})\n");
      sb.Append($"\t\tvar outcome{index} = \"{MaxonTestProtocol.OutcomePass}\"\n");
      sb.Append($"\t\tlet started{index} = Clock.nowNanos()\n");
      sb.Append($"\t\ttry {test.ShortSymbol}() otherwise '{caught}'\n");
      sb.Append($"\t\t\toutcome{index} = \"{MaxonTestProtocol.OutcomeFail}\"\n");
      sb.Append($"\t\tend '{caught}'\n");
      sb.Append($"\t\t{HelperType}.ended({index}, outcome: outcome{index}, "
        + $"nanos: Clock.elapsedNanos(started{index}))\n");
      sb.Append($"\tend '{guard}'\n");
    }

    sb.Append($"end '{group.RunnerName}'\n");
    return sb.ToString();
  }

  /// <summary>
  /// The root entry: the shared helpers, then a call to every file's dispatcher.
  ///
  /// <c>useWireFormat</c> is called once, first, before anything can report — that is the contract
  /// <c>stdlib/Testing.maxon</c> states, and it is why the format is an argument rather than a
  /// constant the stdlib holds.
  ///
  /// It returns 0 whatever the tests did. The runner attributes outcomes from the MARKER STREAM, and
  /// a non-zero exit here would be indistinguishable from the crash it must never be confused with.
  /// The exit code a test binary owns is therefore only ever "how the process died".
  /// </summary>
  private static string EntrySource(IReadOnlyList<TestFileGroup> groups) {
    var sb = new StringBuilder();
    sb.Append("// Generated by `maxon test` — never written to disk. See TestDispatcher.\n\n");
    sb.Append($"export typealias MaxonTestIndex = int(0 to {MaxTestsPerRun})\n\n");

    sb.Append($"export type {HelperType}\n");

    sb.Append("\t/// The run's selection, read once. Absent means every test — what a human running\n");
    sb.Append("\t/// this binary by hand gets — and is a DIFFERENT setting from an empty selection,\n");
    sb.Append("\t/// which selects nothing.\n");
    sb.Append("\texport static function selection() returns String\n");
    sb.Append($"\t\treturn try CommandLine.getOptionValue(\"{MaxonTestProtocol.SelectOptionName}\")"
      + $" otherwise \"{MaxonTestProtocol.SelectAll}\"\n");
    sb.Append("\tend 'selection'\n\n");

    sb.Append("\texport static function selected(selection String, index MaxonTestIndex) returns bool\n");
    sb.Append($"\t\tif selection == \"{MaxonTestProtocol.SelectAll}\" 'all'\n");
    sb.Append("\t\t\treturn true\n");
    sb.Append("\t\tend 'all'\n\n");
    // The same brackets FormatSelection writes, with the index left as a Maxon interpolation.
    var bracket = MaxonTestProtocol.SelectIndexBracket;
    sb.Append($"\t\treturn selection.contains(\"{bracket}{{index}}{bracket}\")\n");
    sb.Append("\tend 'selected'\n\n");

    sb.Append("\t/// Written BEFORE the body runs, which is what makes a process death attributable:\n");
    sb.Append("\t/// the last test to begin and never end is the one that killed it. `printError`\n");
    sb.Append("\t/// writes straight to the handle with no buffer, so the marker has already left the\n");
    sb.Append("\t/// process by the time the body can panic.\n");
    sb.Append("\texport static function began(index MaxonTestIndex)\n");
    sb.Append($"\t\tprintError(\"{Wrap($"{MaxonTestProtocol.BeganTag}{Sep()}{{index}}")}\\n\")\n");
    sb.Append("\tend 'began'\n\n");

    sb.Append("\texport static function ended(index MaxonTestIndex, outcome String, nanos DurationNanos)\n");
    sb.Append($"\t\tprintError(\"{Wrap($"{MaxonTestProtocol.EndedTag}{Sep()}{{index}}{Sep()}{{outcome}}{Sep()}{{nanos}}")}\\n\")\n");
    sb.Append("\tend 'ended'\n");
    sb.Append($"end '{HelperType}'\n\n");

    sb.Append($"export function {EntrySymbol}() returns ExitCode\n");
    sb.Append($"\t__TestReport.useWireFormat(\"{MaxonTestProtocol.Wrapper}\", "
      + $"separator: \"{Sep()}\")\n");
    sb.Append($"\tlet selection = {HelperType}.selection()\n\n");

    foreach (var group in groups) {
      sb.Append($"\t{group.RunnerSymbol}(selection)\n");
    }

    sb.Append("\n\treturn 0\n");
    sb.Append($"end '{EntrySymbol}'\n");
    return sb.ToString();
  }

  /// <summary>
  /// The field separator as it must be SPELLED in Maxon source. Generated from the one constant, so
  /// the character the parser matches on and the character the binary prints cannot diverge.
  /// </summary>
  private static string Sep() => MaxonTestProtocol.FieldSeparatorInMaxonSource;

  /// <summary>Bracket a report body the way <see cref="MaxonTestProtocol"/> requires.</summary>
  private static string Wrap(string body) =>
    $"{MaxonTestProtocol.Wrapper}{body}{MaxonTestProtocol.Wrapper}";

  /// <summary>
  /// Content hash of every generated byte, for the build cache's <c>extraKey</c>. SHA-256 rather
  /// than a string's own hash code because it must be stable ACROSS PROCESSES — the manifest it
  /// lands in outlives the run that wrote it.
  /// </summary>
  private static string HashOf(string content) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
