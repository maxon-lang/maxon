using MaxonSharp.Compiler;
using MaxonSharp.Testing;

namespace MaxonSharp;

/// <summary>
/// The `maxon test` command: discover every <c>test</c> declaration in a project, compile them into
/// one binary with a generated entry point, run them, and report.
///
/// This face parses a command line and sequences the four things that do the work — discovery
/// (<see cref="Compiler.Compiler.DiscoverTests"/>), generation (<see cref="TestDispatcher"/>),
/// execution (<see cref="TestExecutor"/>) and rendering (<see cref="TestRender"/>). It builds no
/// report of its own, exactly as <see cref="CoverageCommand"/> builds none.
/// </summary>
internal static class TestCommand {
  private const string FilterFlagLong = "--filter=";
  private const string FilterFlagShort = "-t";
  private const string FilterFlagShortEquals = "-t=";
  private const string WorkersFlag = "--workers=";
  private const string IsolateFlag = "--isolate";
  private const string BailFlag = "--bail";
  private const string BailFlagEquals = "--bail=";
  private const string TimeoutFlag = "--timeout=";
  private const string ListFlag = "--list";
  private const string JsonFlag = "--json";
  private const string NoTimingFlag = "--no-timing";
  private const string ColorFlag = "--color=";
  private const string TargetFlag = "--target=";
  private const string LogFlag = "--log=";

  /// <summary>
  /// How long one test PROCESS may run. Stated in milliseconds because a unit test that needs whole
  /// seconds is already a problem the number should make visible.
  ///
  /// It bounds the process and therefore the whole SHARD, which by default is a whole file — so it
  /// is a budget the file's tests share, and adding a slow test to a file can push its neighbours
  /// past the deadline. That is why the usage line says "a test process" rather than "a test", and
  /// why <c>--isolate</c> gives every test the full budget to itself.
  /// </summary>
  private const int DefaultTimeoutMs = 5000;

  /// <summary>Failures allowed before stopping, when <c>--bail</c> is given with no count.</summary>
  private const int DefaultBailAfter = 1;

  /// <summary>Cache slot for the test binary, beside the project's own and its build-runner's.</summary>
  private const string CacheName = "test";

  /// <summary>The test binary's name inside the cache directory. It is not the user's artifact.</summary>
  private const string BinaryStem = ".maxon-test";

  /// <summary>Separates the parts of a comma-separated filter, each of which is matched on its own.</summary>
  private const char FilterSeparator = ',';

  /// <summary>Everything passed. Exit code 0 and nothing to act on.</summary>
  private const int ExitAllPassed = 0;

  /// <summary>
  /// Something failed, crashed, timed out, leaked, did not run — or NO TESTS WERE FOUND. That last
  /// one shares an exit code with a failure deliberately: a zero-test run that exits 0 is the
  /// classic CI lie, where a renamed directory or a broken glob reads for months as a green suite.
  /// </summary>
  private const int ExitTestsFailed = 1;

  /// <summary>
  /// The run could not happen: a bad flag, a compile error, a project that is not there. Distinct
  /// from 1 because CI must be able to tell "the code is broken" from "the harness never ran".
  /// </summary>
  private const int ExitCouldNotRun = 2;

  public static int Run(string[] args) {
    var filters = new List<string>();
    int? workers = null;
    var isolate = false;
    int? bailAfter = null;
    var timeoutMs = DefaultTimeoutMs;
    var list = false;
    var json = false;
    var showTiming = true;
    var color = ColorSetting.Auto;
    CompileTarget? target = null;
    string? path = null;

    for (var i = 0; i < args.Length; i++) {
      var arg = args[i];

      if (arg.StartsWith(FilterFlagLong, StringComparison.Ordinal)) {
        filters.Add(arg[FilterFlagLong.Length..]);
      } else if (arg.StartsWith(FilterFlagShortEquals, StringComparison.Ordinal)) {
        filters.Add(arg[FilterFlagShortEquals.Length..]);
      } else if (arg == FilterFlagShort) {
        // `-t PATTERN` — the separated form, which is what a shell user types when the pattern has
        // spaces. Refused rather than ignored when nothing follows.
        if (i + 1 >= args.Length) return Usage($"'{FilterFlagShort}' needs a pattern");
        filters.Add(args[++i]);
      } else if (arg.StartsWith(WorkersFlag, StringComparison.Ordinal)) {
        var value = arg[WorkersFlag.Length..];
        if (!int.TryParse(value, out var parsed) || parsed < 1)
          return Usage($"'{WorkersFlag}' needs a positive whole number, got '{value}'");
        workers = parsed;
      } else if (arg == IsolateFlag) {
        isolate = true;
      } else if (arg == BailFlag) {
        bailAfter = DefaultBailAfter;
      } else if (arg.StartsWith(BailFlagEquals, StringComparison.Ordinal)) {
        var value = arg[BailFlagEquals.Length..];
        if (!int.TryParse(value, out var parsed) || parsed < 1)
          return Usage($"'{BailFlagEquals}' needs a positive whole number, got '{value}'");
        bailAfter = parsed;
      } else if (arg.StartsWith(TimeoutFlag, StringComparison.Ordinal)) {
        var value = arg[TimeoutFlag.Length..];
        if (!int.TryParse(value, out var parsed) || parsed < 1)
          return Usage($"'{TimeoutFlag}' needs a positive whole number of milliseconds, got '{value}'");
        timeoutMs = parsed;
      } else if (arg == ListFlag) {
        list = true;
      } else if (arg == JsonFlag) {
        json = true;
      } else if (arg == NoTimingFlag) {
        showTiming = false;
      } else if (arg.StartsWith(ColorFlag, StringComparison.Ordinal)) {
        var value = arg[ColorFlag.Length..];
        if (!Ansi.TryParse(value, out color))
          return Usage($"'{ColorFlag}' takes {Ansi.Choices}, got '{value}'");
      } else if (arg.StartsWith(TargetFlag, StringComparison.Ordinal)) {
        try {
          target = CompileTarget.Parse(arg[TargetFlag.Length..]);
        } catch (ArgumentException ex) {
          return Usage(ex.Message);
        }
      } else if (arg.StartsWith(LogFlag, StringComparison.Ordinal)) {
        if (!Logger.ParseOption(arg[LogFlag.Length..])) return Usage($"unrecognized log spec '{arg}'");
      } else if (arg.StartsWith('-')) {
        return Usage($"unknown option '{arg}'");
      } else if (path == null) {
        path = arg;
      } else {
        return Usage($"expected at most one project path, also got '{arg}'");
      }
    }

    // The DEFAULT is put through the same refusal an explicit `--target=` already gets from
    // CompileTarget.Parse above, and that half is not redundant: the default is the HOST, which on a
    // Linux box is `x64-linux`. Refused HERE for Program.ParseTarget's two stated reasons, both of
    // which this command reaches before any compile — Program.GetOutputExtension chooses the binary's
    // extension from the OS and died on an unknown one with an unhandled ArgumentException and a
    // stack trace, and BuildCache.IsCacheValid is consulted, so a cached binary could be handed back
    // for a request that should have been refused.
    var resolvedTarget = target ?? CompileTarget.Default;
    if (resolvedTarget.Unsupported is { } unsupported) return Usage(unsupported.Format());

    return Execute(new Settings(
      path ?? Directory.GetCurrentDirectory(), filters, workers ?? TestExecutor.DefaultWorkers,
      isolate, bailAfter, timeoutMs, list, json, showTiming, color,
      resolvedTarget));
  }

  /// <summary>Everything the command line settled, so the run itself takes no strings apart.</summary>
  private sealed record Settings(
    string ProjectPath, IReadOnlyList<string> Filters, int Workers, bool Isolate, int? BailAfter,
    int TimeoutMs, bool List, bool Json, bool ShowTiming, ColorSetting Color, CompileTarget Target);

  private static int Execute(Settings settings) {
    if (!Directory.Exists(settings.ProjectPath))
      return Fail($"not a directory: {settings.ProjectPath}");

    var projectDir = Path.GetFullPath(settings.ProjectPath);

    // Every path this command prints is spelled by ReportPath — the same rule the coverage, profile
    // and launcher reports use — so a transcript of a run is the same bytes on every machine.
    // `projectDir` in particular is ABSOLUTE by construction, and printing it raw is exactly the
    // machine-specific output that rule exists to prevent.
    var displayDir = Debug.ReportPath.Display(projectDir);
    var color = Ansi.Enabled(settings.Color);

    // Two process-globals, set and deliberately NOT restored — unlike the compiler flags below,
    // which are. They differ in what they govern: the flags change the BYTES a compile emits, so a
    // later compile in this process must not inherit them, whereas these two only decide how
    // diagnostics are printed and how paths are spelled, for the remainder of a command that owns
    // the process and then exits. `maxon test` is reachable only from Program.Main and runs nothing
    // after itself; if that ever stops being true, they need a scope.
    Logger.SetLevel(LogCategory.Compiler, LogLevel.Error);
    CompileError.ProjectRoot = projectDir;

    // Tests are source, so they must be collected — the default selection leaves them out precisely
    // so a `maxon build` does not ship them.
    var sources = SourceCollector.FromDirectory(projectDir, editorOverrides: null,
      SourceSelection.ProductionAndTests);
    if (sources.Length == 0) return Fail($"no .maxon files found in: {displayDir}");

    // Discovery is also where a name collision surfaces. Within one file the parser refuses it
    // (E3107, naming both prose names); ACROSS files in one directory — which share a namespace, so
    // `a.test.maxon` and `b.test.maxon` both declaring `test 'works'` mint one symbol twice — the
    // module refuses the second as E3006. Both arrive here as a discovery error and exit 2, so
    // nothing downstream has to re-check for a duplicate that cannot reach it.
    if (!TryDiscover(projectDir, sources, settings.Target, out var tests, out var discoveryFailure))
      return Fail(discoveryFailure);

    var groups = TestDispatcher.GroupTests(tests);
    var selected = Select(groups, settings.Filters);

    // Before any source is synthesized: `--list` compiles nothing, and generating a dispatcher it
    // would then throw away is the same claim made quietly.
    if (settings.List) {
      Console.Out.Write(settings.Json
        ? TestRender.ListJson(groups, selected) + "\n"
        : TestRender.ListText(groups, selected, color));
      // A list that found nothing is the same lie a run that found nothing is.
      return selected.Count == 0 ? ExitTestsFailed : ExitAllPassed;
    }

    if (tests.Count == 0)
      return FailNoTests($"no `test` declarations found under {displayDir}", settings, color);

    if (selected.Count == 0) {
      return FailNoTests(
        $"no test matched {string.Join(", ", settings.Filters.Select(f => $"'{f}'"))}", settings, color);
    }

    var generated = TestDispatcher.Generate(groups, projectDir, sources);
    var binaryPath = Path.Combine(BuildCache.GetCacheDir(projectDir),
      BinaryStem + Program.GetOutputExtension(settings.Target));

    var compileSw = System.Diagnostics.Stopwatch.StartNew();
    if (!TryCompile(projectDir, sources, generated, binaryPath, settings.Target, out var compiled,
        out var compileFailure)) {
      return Fail(compileFailure);
    }
    compileSw.Stop();

    var execution = TestExecutor.Run(groups, selected,
      new TestRunOptions(binaryPath, settings.Workers, settings.Isolate, settings.BailAfter,
        settings.TimeoutMs),
      // Streamed for a text run so results appear as they land, buffered for JSON, which is one
      // document and cannot be emitted in pieces.
      settings.Json ? null : file => Console.Out.Write(TestRender.FileBlock(file, color, settings.ShowTiming)));

    var report = new TestRunReport(execution.Files, compileSw.ElapsedMilliseconds, execution.RunMs,
      compiled, execution.Bailed);

    Console.Out.Write(settings.Json
      ? TestRender.Json(report, settings.ShowTiming) + "\n"
      : TestRender.Summary(report, color, settings.ShowTiming));

    return report.Failed > 0 ? ExitTestsFailed : ExitAllPassed;
  }

  /// <summary>
  /// Discover, preferring the manifest. On a miss the parse is done and the manifest rewritten, so
  /// the cost of a cold discovery is paid once per source change rather than once per invocation.
  /// </summary>
  private static bool TryDiscover(string projectDir, SourceFile[] sources, CompileTarget target,
      out List<DiscoveredTest> tests, out string failure) {
    failure = "";

    if (TestManifest.Read(projectDir, sources, target) is { } cached) {
      tests = cached;
      return true;
    }

    // The SAME target the binary is compiled for, so what is discovered and what is generated
    // cannot describe different programs.
    var errors = Compiler.Compiler.DiscoverTests(sources, target, out tests);
    if (errors.Count > 0) {
      failure = string.Join("\n", errors.Select(e => e.Format()));
      return false;
    }

    TestManifest.Write(projectDir, sources, target, tests);
    return true;
  }

  /// <summary>
  /// Which test indices the filter keeps. No filter keeps everything.
  ///
  /// A pattern matches a test if it is a substring of the test's NAME or of its FILE's displayed
  /// path, so `-t math` finds both a file called math and a test about maths. Comma-separated
  /// patterns UNION, which is what makes `-t a,b` mean "run a and b" rather than "run nothing".
  /// Matching is case-insensitive: these are prose names a human is retyping from a report.
  /// </summary>
  private static HashSet<int> Select(IReadOnlyList<TestFileGroup> groups, IReadOnlyList<string> filters) {
    var patterns = filters
      .SelectMany(f => f.Split(FilterSeparator))
      .Select(p => p.Trim())
      .Where(p => p.Length > 0)
      .ToList();

    var selected = new HashSet<int>();
    foreach (var group in groups) {
      var displayPath = Debug.ReportPath.Display(group.Path);
      foreach (var test in group.Tests) {
        if (patterns.Count == 0 || patterns.Any(p => Matches(p, displayPath, test.Test.DisplayName)))
          selected.Add(test.Index);
      }
    }
    return selected;
  }

  private static bool Matches(string pattern, string displayPath, string name) =>
    name.Contains(pattern, StringComparison.OrdinalIgnoreCase)
    || displayPath.Contains(pattern, StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// Compile every discovered test into one binary, through the build cache.
  ///
  /// Two things here are load-bearing:
  ///
  /// - The generated source is held in memory, never written to disk, and its content hash is the
  ///   cache's <c>extraKey</c>. The cache keys real sources by last-write time, which synthesized
  ///   source does not have; without the key two different dispatchers would compare equal and the
  ///   second run would be handed the first's binary.
  /// - Only REAL files are handed to the cache. It refuses anything else by name, precisely because
  ///   a filesystem-shaped path that does not exist used to key as a constant.
  /// </summary>
  private static bool TryCompile(string projectDir, SourceFile[] onDiskSources,
      TestDispatcher.Generated generated, string binaryPath, CompileTarget target,
      out bool compiled, out string failure) {
    compiled = false;
    failure = "";

    BuildCache.EnsureCacheDir(projectDir);

    // The flag scope wraps the CACHE PROBE as well as the compile: the cache keys on these flags, so
    // probing outside the scope would ask about a build nobody is making.
    using var scope = InternalCompileScope.Enter();

    if (BuildCache.IsCacheValid(projectDir, onDiskSources, binaryPath, target, CacheName,
        generated.ExtraKey)) {
      return true;
    }

    // Captured BEFORE the compile — see BuildCache.CaptureInputs.
    var testInputs = BuildCache.CaptureInputs(onDiskSources, target);
    var result = new Compiler.Compiler().Compile(generated.Sources, binaryPath, irOutputPath: null,
      dumpStagesBasePath: null, target: target, entryFunction: TestDispatcher.EntrySymbol);

    if (!result.Success) {
      failure = string.Join("\n", result.Errors.Select(e => e.Format()));
      return false;
    }

    BuildCache.WriteCache(projectDir, testInputs, binaryPath, CacheName, generated.ExtraKey);
    compiled = true;
    return true;
  }

  /// <summary>
  /// Report a run that found nothing to do. It is a FAILURE, and says why in the report's own voice
  /// rather than as an error, because nothing went wrong with the harness — there is simply nothing
  /// green to claim.
  /// </summary>
  private static int FailNoTests(string reason, Settings settings, bool color) {
    if (settings.Json) {
      var empty = new TestRunReport([], CompileMs: 0, RunMs: 0, Compiled: false, Bailed: false);
      Console.Out.Write(TestRender.Json(empty, settings.ShowTiming, reason) + "\n");
    } else {
      Console.Out.Write(Ansi.Yellow(reason, color) + "\n");
    }
    return ExitTestsFailed;
  }

  private static int Fail(string problem) {
    Console.Error.WriteLine($"test: {problem}");
    return ExitCouldNotRun;
  }

  /// <summary>
  /// How wide the flag column in the usage text is. One number, so the listing cannot go ragged the
  /// way a hand-padded one does the moment a flag's name changes length.
  ///
  /// 27 because this block is printed BOTH by <see cref="Usage"/> and, verbatim, inside
  /// <c>maxon</c>'s top-level usage, whose other sections align their descriptions there.
  /// </summary>
  private const int UsageFlagColumnWidth = 27;

  /// <summary>One usage line. An empty <paramref name="flag"/> continues the previous one.</summary>
  private static void Option(TextWriter output, string flag, string description) {
    var left = flag.Length == 0 ? new string(' ', UsageFlagColumnWidth) : ("  " + flag).PadRight(UsageFlagColumnWidth);
    output.WriteLine((left + description).TrimEnd());
  }

  /// <summary>
  /// The flags this command accepts and the codes it exits with.
  /// </summary>
  /// <remarks>
  /// ONE copy, printed by <see cref="Usage"/> on a bad flag and by <c>maxon</c>'s top-level usage,
  /// rather than written out again in the second place. The EXIT CODES are the part that must not
  /// be allowed to drift: CI branches on them, so a stale second listing would route a pipeline by
  /// a number this command had stopped using — and both texts would look equally authoritative.
  ///
  /// Every default it quotes is read from the constant that supplies it, for the same reason.
  /// </remarks>
  public static void WriteOptions(TextWriter output) {
    Option(output, $"{FilterFlagShort} P, {FilterFlagShortEquals}P, {FilterFlagLong}P",
      "run only tests whose NAME or FILE contains P");
    Option(output, "", "(case-insensitive; comma-separated patterns are a union)");
    Option(output, ListFlag, "print the tests that would run and compile nothing");
    Option(output, JsonFlag, "emit the report as JSON instead of text");
    Option(output, IsolateFlag, "run every test in its own process");
    // "stop CLAIMING", not "stop": TestExecutor checks the limit before a worker takes its NEXT
    // shard, so shards already in flight finish and report. A run can therefore end with more than
    // N failures, and a usage line promising exactly N would be describing a harness that killed
    // work it had already paid for.
    Option(output, $"{BailFlag}[=N]", $"stop claiming new work after N failures (default {DefaultBailAfter});");
    Option(output, "", "shards already running finish, so the total may exceed N");
    Option(output, $"{WorkersFlag}N", $"run N test processes at once (default {TestExecutor.DefaultWorkers} here)");
    Option(output, $"{TimeoutFlag}MS", $"kill a test process after MS milliseconds (default {DefaultTimeoutMs});");
    Option(output, "", $"a whole file shares one process, so {IsolateFlag} gives each test its own budget");
    Option(output, NoTimingFlag, "omit durations, making stdout byte-reproducible");
    Option(output, $"{ColorFlag}{Ansi.Choices}", "");
    Option(output, "", "colour; auto means only when stdout is a terminal");
    Option(output, $"{TargetFlag}ARCH-OS", "compile the test binary for a specific target");
    // Accepted by the parser above, so it is listed. A flag this block leaves out is a flag no
    // reader can discover and no drift check can notice — the listing is only THE listing if it is
    // complete.
    Option(output, $"{LogFlag}CATEGORY:LEVEL", "enable compiler logging (e.g. codegen:trace)");
    output.WriteLine();
    output.WriteLine("Exit codes:");
    output.WriteLine($"  {ExitAllPassed}  every test passed.");
    output.WriteLine($"  {ExitTestsFailed}  a test failed, crashed, timed out, leaked, or did not run —");
    output.WriteLine("     or NO TESTS WERE FOUND, which is never reported as success.");
    output.WriteLine($"  {ExitCouldNotRun}  the run could not happen: a bad flag, a compile error, no such project.");
  }

  private static int Usage(string problem) {
    Console.Error.WriteLine($"test: {problem}");
    Console.Error.WriteLine($"Usage: maxon test [<directory>] [{FilterFlagShort}|{FilterFlagLong}PATTERN]"
      + $" [{ListFlag}] [{JsonFlag}] [options]");
    WriteOptions(Console.Error);
    return ExitCouldNotRun;
  }
}
