using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Executes tests from fragment files.
/// </summary>
public partial class TestRunner(string specDir, string fragmentDir, string tempDir, string projectRoot, string? filter = null, int? workers = null, bool updateRequired = false, Compiler.CompileTarget? target = null, bool verbose = false, bool noBatch = false, bool includeNetwork = false) {
  private readonly string _specDir = specDir;
  private readonly string _fragmentDir = fragmentDir;
  private readonly string _tempDir = tempDir;
  private readonly string _projectRoot = projectRoot;
  private readonly string? _filter = filter;
  // How many workers to use, from the one place that decides it for every harness in this process.
  // The number used to be written here as a bare literal AND copied into `maxon test`'s executor
  // with a comment asking the two to stay in step; two harnesses that carve up one machine must
  // carve it the same way, and a note is not a mechanism. Override with --workers=N.
  private readonly int _workerCount = workers ?? TestExecutor.DefaultWorkers;
  private readonly bool _updateRequired = updateRequired;
  private readonly Compiler.CompileTarget _target = target ?? Compiler.CompileTarget.Default;
  private readonly bool _verbose = verbose;
  private readonly bool _noBatch = noBatch;
  // Include `category: network` specs, which reach the public internet and so cannot be part of a
  // deterministic gate (SpecParser.ParseDirectory explains why). Off by default; --network opts in.
  private readonly bool _includeNetwork = includeNetwork;
  private static long _totalCompileMs;

  /// <summary>
  /// Whether THIS RUN's fragments are the ones the committed goldens pin.
  ///
  /// A fragment's `// CompiledIR` section comes from a compile whose shape both flags change, so under
  /// either of them the goldens are neither compared nor written — the fragments this run produced are
  /// simply not the same artifact:
  /// <list type="bullet">
  /// <item><c>--filter</c>: a spec's batchable tests compile as ONE module with ONE literal pool, so
  ///   which tests were selected decides every survivor's <c>__str_N</c> indices.</item>
  /// <item><c>--no-batch</c>: a batchable test then compiles ALONE, which is a different compile from
  ///   the batched one its golden was minted from.</item>
  /// </list>
  /// Refusing to WRITE matters as much as refusing to compare, and is the older half of the bug: a
  /// filtered or unbatched run used to overwrite goldens with content only that run could reproduce,
  /// so the next full run overwrote them back — flip-flop churn in <c>git status</c> that hid real
  /// codegen diffs in the noise.
  ///
  /// The consequence, stated because it is a real cost and not an oversight: fragment goldens can only
  /// be minted by a FULL <c>--update-required</c> run. Letting a filtered one mint the tests it does
  /// cover would mean writing a golden for a batchable test from a batch it was never in.
  /// </summary>
  private bool FragmentGoldensAreAuthoritative => WhyGoldensAreNotAuthoritative == null;

  /// The flag that disqualified this run, for the line that has to say so — or null when none did.
  ///
  /// ⚠ It is the SAME predicate as <see cref="FragmentGoldensAreAuthoritative"/> and not a second
  /// one, which is the point: the report used to re-derive which half had fired
  /// (<c>_filter != null ? "--filter" : "--no-batch"</c>), so a third disqualifier added above would
  /// have been announced confidently as <c>--no-batch</c>, with nothing to fail. A rule that must
  /// explain itself should return the explanation.
  private string? WhyGoldensAreNotAuthoritative =>
    _filter != null ? FilterFlagName : _noBatch ? NoBatchFlagName : null;

  /// The CLI spellings this runner's report quotes back. Named because the report is the only place
  /// they appear outside `Program`'s argument parsing, and a flag renamed there must not leave this
  /// line naming one that no longer exists.
  private const string FilterFlagName = "--filter";
  private const string NoBatchFlagName = "--no-batch";

  /// <summary>
  /// Which compile a test's committed golden pins. The CALLER decides, because only it knows whether
  /// the fragment in its hand is that compile.
  ///
  /// ⚠ THE RULE HAS TO BE INDEPENDENT OF MACHINE LOAD, and the obvious one is not. "Gate whatever this
  /// run produced" was tried and MEASURED wrong: a mint run that overlapped another agent's suite ran
  /// 4.5x slower, batched binaries hit their timeouts, their specs fell back to per-fragment compiles,
  /// every test still PASSED individually — and a golden was minted from the single compile whose
  /// <c>mm_alloc</c> tag indices are one lower than the batched compile's. A gate whose expected value
  /// depends on how busy the machine was is not a gate.
  ///
  /// The load-independent fact is that a fragment's content comes from a COMPILE, and the batched
  /// compile either succeeded or it did not — which is a property of the source and the compiler
  /// alone. Whether the batched BINARY then ran cleanly is a separate question, and the fallback it
  /// triggers must not reach the goldens.
  /// </summary>
  private enum FragmentSource {
    /// <summary>
    /// This per-fragment compile. The test is in no batched module at all — it is not batchable, the
    /// rewriter rejected it, or its spec's batched source did not compile — so compiling it alone is
    /// the only content there is, on every run.
    /// </summary>
    ThisCompile,

    /// <summary>
    /// The BATCHED compile of its spec, gated separately by <see cref="CheckBatchFragments"/>. This
    /// per-fragment compile is a re-run performed only to attribute a batch-level RUN failure; its IR
    /// is a different artifact and must not touch the golden.
    /// </summary>
    BatchedCompile,
  }

  /// <summary>
  /// What this run did to the committed fragment goldens, plus anything that went wrong producing one.
  ///
  /// One object rather than a <c>ref int</c> and a bag threaded side by side through six frames: they
  /// are the same story, every worker thread touches both, and a <c>ref</c> counter cannot be handed
  /// to a lambda at all.
  /// </summary>
  private sealed class FragmentTally {
    private int _written;
    private int _verified;

    /// <summary>Goldens this run created or regenerated (a new test, or <c>--update-required</c>).</summary>
    public int Written => _written;

    /// <summary>Goldens this run compared against fresh compiler output and found identical.</summary>
    public int Verified => _verified;

    /// <summary>
    /// Goldens that could be neither compared nor written, because the fragment itself could not be
    /// produced. Reported and made a non-zero exit by <c>Program.SpecTest</c> — a gate that could not
    /// run is not a gate that passed.
    /// </summary>
    public ConcurrentBag<string> Errors { get; } = [];

    public void CountWritten() => Interlocked.Increment(ref _written);

    public void CountVerified() => Interlocked.Increment(ref _verified);
  }

  /// <summary>
  /// Run all tests and return summary.
  /// Uses Zig-style worker threads with atomic work-stealing for maximum parallelism.
  /// Each worker handles the full pipeline: regenerate fragment → compile → run → check.
  /// </summary>
  public TestSummary RunAllSpecTests() {
    var sw = Stopwatch.StartNew();

    // Update required blocks in spec files if requested
    if (_updateRequired) {
      UpdateRequiredInSpecFiles();
    }

    // Prepare work items from specs (sequential — parses specs, partitions
    // into batched + per-fragment, ensures directories exist).
    var prepResult = FragmentGenerator.PrepareWorkItems(_specDir, _fragmentDir, _target, _filter, _noBatch, _includeNetwork);

    // Abort on errors (e.g., duplicate test names)
    if (prepResult.Errors.Count > 0) {
      foreach (var error in prepResult.Errors) {
        Logger.Error(LogCategory.Testing, error);
      }
      return new TestSummary {
        Results = [],
        TotalDuration = sw.Elapsed,
        PreparationErrors = prepResult.Errors.Count
      };
    }

    var workItems = prepResult.WorkItems;
    if (workItems.Length == 0) {
      Logger.Info(LogCategory.Testing, "No tests found");
      ReportSuspensionCensus(prepResult.Census);
      return new TestSummary {
        Results = [],
        TotalDuration = sw.Elapsed
      };
    }

    // Ensure temp directory exists
    Directory.CreateDirectory(_tempDir);

    // Count individual tests (batches expand to N tests for progress reporting)
    var totalTestCount = workItems.Sum(WorkItemTestCount);
    Logger.Info(LogCategory.Testing, $"Running {totalTestCount} test(s) with {_workerCount} worker(s)...");

    // Each work item produces an array of TestResults (one per test it contains).
    // For Single, that's one result; for Batch, one per batched test.
    var results = new TestResult[workItems.Length][];
    var nextIndex = 0;
    var tally = new FragmentTally();
    _totalCompileMs = 0;
    var printLock = new object();
    var compilationFailed = 0; // 1 = a compilation error occurred, stop all workers
    string? firstCompilationError = null;

    // Per-spec tracking for real-time progress (counts individual tests, not work items).
    var specTotal = new Dictionary<string, int>();
    var specDone = new Dictionary<string, int>();
    var specFailed = new Dictionary<string, List<string>>();

    // Counted, not listed. A cross-OS run leaves most of a spec unrunnable and the reason is the same
    // one line for every test in the suite (it is said once, by the summary) — listing three thousand
    // identical entries would bury the real failures this report exists to surface.
    var specNotRunHere = new Dictionary<string, int>();

    foreach (var item in workItems) {
      var specName = WorkItemSpecName(item);
      specTotal.TryAdd(specName, 0);
      specTotal[specName] += WorkItemTestCount(item);
    }
    foreach (var name in specTotal.Keys) {
      specDone[name] = 0;
      specFailed[name] = [];
      specNotRunHere[name] = 0;
    }

    // Spawn worker threads (Zig-style: explicit threads + atomic work-stealing)
    var threadCount = Math.Min(_workerCount, workItems.Length);
    var threads = new Thread[threadCount];
    for (int i = 0; i < threadCount; i++) {
      var workerId = i;
      threads[i] = new Thread(() => {
        while (true) {
          if (Volatile.Read(ref compilationFailed) != 0) break;

          var index = Interlocked.Increment(ref nextIndex) - 1;
          if (index >= workItems.Length) break;

          var item = workItems[index];
          var itemSw = _verbose ? System.Diagnostics.Stopwatch.StartNew() : null;
          var itemResults = item switch {
            AnyWorkItem.Single s => [ProcessWorkItem(s.Item, FragmentSource.ThisCompile, tally)],
            AnyWorkItem.Batch b => ProcessSpecBatch(b.Item, tally),
            _ => throw new InvalidOperationException("unknown work item kind"),
          };
          results[index] = itemResults;
          itemSw?.Stop();

          lock (printLock) {
            var specName = WorkItemSpecName(item);
            for (int ri = 0; ri < itemResults.Length; ri++) {
              var result = itemResults[ri];
              var testName = result.TestName;
              // Always use the per-test fragment path: failing batched tests
              // are re-run individually before reporting, so the per-test
              // fragment file exists and points to the actual test source.
              // Batching is an internal optimization that must not surface here.
              var testIdentifier = $"specs/fragments/{specName}/{testName}.test";

              if (_verbose && itemSw != null) {
                Logger.Info(LogCategory.Testing, $"[W{workerId}] [{StatusLabel(result.Outcome)}] {specName}/{testName} ({itemSw.ElapsedMilliseconds}ms)");
              }

              if (result.Outcome == SpecTestOutcome.NotRunHere) {
                specNotRunHere[specName]++;
              } else if (result.Outcome == SpecTestOutcome.Failed) {
                var msg = result.ErrorMessage;
                var isCompilationError = msg != null && msg.StartsWith("Compilation failed:");
                if (isCompilationError && Interlocked.Exchange(ref compilationFailed, 1) == 0) {
                  firstCompilationError = $"{testIdentifier}\n  {msg}";
                }

                specFailed[specName].Add(testIdentifier);
                if (msg != null) specFailed[specName].Add($"  {msg}");
              }
              specDone[specName]++;
            }

            // When all tests in a spec are done, print the spec result
            if (specDone[specName] == specTotal[specName]) {
              var failures = specFailed[specName];
              var total = specTotal[specName];
              if (failures.Count > 0) {
                var failCount = failures.Count(f => !f.StartsWith("  "));
                Logger.Error(LogCategory.Testing, $"[FAIL] {specName} ({total - failCount}/{total})");
                foreach (var f in failures) {
                  Logger.Error(LogCategory.Testing, f);
                }
              } else if (specNotRunHere[specName] > 0) {
                // Its own line and its own marker: this spec was neither green nor red here, and
                // reporting it as either would be the lie. The count is what a reader acts on.
                Logger.Error(LogCategory.Testing,
                  $"[NOT RUN] {specName} ({specNotRunHere[specName]}/{total} compiled but not run on this host)");
              }
            }
          }
        }
      }) { IsBackground = true };
      threads[i].Start();
    }

    foreach (var t in threads) t.Join();
    sw.Stop();

    if (firstCompilationError != null) {
      Logger.Error(LogCategory.Testing, $"Stopped: compilation error encountered:");
      Logger.Error(LogCategory.Testing, firstCompilationError);
    }

    foreach (var error in tally.Errors) {
      Logger.Error(LogCategory.Testing, error);
    }

    if (_totalCompileMs > 0) {
      Logger.Info(LogCategory.Testing, $"Total compile time: {_totalCompileMs}ms (across {_workerCount} workers)");
    }

    // Said on EVERY run, including the skipped case. A run that quietly checked nothing reads exactly
    // like one that checked everything and found nothing wrong, and the whole point of the goldens is
    // that somebody can tell those two apart.
    if (FragmentGoldensAreAuthoritative) {
      Logger.Info(LogCategory.Testing, $"Fragment goldens: {tally.Verified} verified, {tally.Written} written");
    } else {
      Logger.Info(LogCategory.Testing,
        $"Fragment goldens: NOT checked — {WhyGoldensAreNotAuthoritative} changes what a fragment contains, "
        + "so only an unfiltered batched run is authoritative");
    }

    ReportSuspensionCensus(prepResult.Census);

    CleanupExecutables(_tempDir);

    return new TestSummary {
      Results = [.. results.Where(r => r != null).SelectMany(r => r)],
      TotalDuration = sw.Elapsed,
      UngatedGoldens = tally.Errors.Count,
      WhyNotRunHere = TargetRunHost.WhyCannotRun(_target),
    };
  }

  /// <summary>
  /// State the suspended population out loud, on EVERY run and whatever the number — including zero,
  /// exactly as the fragment-goldens line above states the skipped case. A report that only speaks up
  /// when there is something to say is one a reader stops looking for, and that is how this got here:
  /// a file marked `status: selfhosted` was skipped with a <c>Logger.Debug</c> line nobody reads, its
  /// goldens sat in the live fragment tree looking exactly like live ones, and 19 files / 173 tests
  /// accumulated behind it while the suite reported green.
  ///
  /// <para>⚠ IT IS NOT A GATE and must not become one. A suspension with a stated reason is a
  /// legitimate move — <see cref="SpecParser.StatusReasonKey"/> is what makes it cost something to
  /// make — and reddening the run over a number somebody already justified would train exactly the
  /// habit this line exists to break.</para>
  /// </summary>
  private static void ReportSuspensionCensus(SpecSuspensionCensus census) {
    if (census.NothingSuspended) {
      Logger.Info(LogCategory.Testing, "Suspended: none — every spec file in this directory ran here.");
      return;
    }

    Logger.Info(LogCategory.Testing,
      $"Suspended: {census.SuspendedFileCount} spec file(s) holding {census.TestsInSuspendedFiles} test(s) "
      + $"(`status:`), plus {census.SuspendedTestCount} test(s) marked SelfhostedOnly inside live files. "
      + "NOTHING RUNS THEM — this runner skips them and no other runner in this tree reads them. Each "
      + $"states why: `{SpecParser.StatusReasonKey}:` in the frontmatter, or the directive's own text.");
  }

  /// The `--verbose` per-test marker. A `match` rather than a ternary so a fourth outcome cannot be
  /// silently printed as FAIL.
  private static string StatusLabel(SpecTestOutcome outcome) => outcome switch {
    SpecTestOutcome.Passed => "PASS",
    SpecTestOutcome.Failed => "FAIL",
    SpecTestOutcome.NotRunHere => "NOT RUN",
    var unhandled => throw new ArgumentOutOfRangeException(nameof(outcome), unhandled,
      "Unhandled spec-test outcome; every outcome needs a marker of its own."),
  };

  // ----- helpers for the unified work-item list -----

  private static string WorkItemSpecName(AnyWorkItem item) => item switch {
    AnyWorkItem.Single s => s.Item.SpecName,
    AnyWorkItem.Batch b => b.Item.SpecName,
    _ => throw new InvalidOperationException(),
  };

  private static int WorkItemTestCount(AnyWorkItem item) => item switch {
    AnyWorkItem.Single => 1,
    AnyWorkItem.Batch b => b.Item.Tests.Length,
    _ => throw new InvalidOperationException(),
  };

  // ----- the committed fragment goldens -----

  /// <summary>
  /// Fold the committed golden's verdict into a test's result.
  ///
  /// A PASSING test whose golden moved becomes a FAILING one, because that is the only report anybody
  /// acts on. The whole defect this closes was that the fragment was written unconditionally on every
  /// run, pass or fail, and compared nowhere: 141 committed goldens under
  /// <c>specs/fragments-x64-windows/</c> were silently rewritten by every full suite run while the
  /// summary said "3198 passed". A golden the suite regenerates cannot gate anything — a codegen
  /// regression would have been absorbed into the working tree, not reported.
  ///
  /// A test that ALREADY FAILED is left alone in both directions: its golden is neither compared (the
  /// real failure is the report, and a derived second one buries it) nor written (a fragment from a
  /// broken compile is not a golden anybody should keep).
  ///
  /// ⭐ A TEST THAT COULD NOT BE RUN HERE IS STILL GATED, and that is most of what a cross-OS run is
  /// FOR. What the golden pins is the COMPILE, which happened and succeeded; whether this machine can
  /// then execute the result is a different question and not one the fragment records. So a
  /// `--target=x64-windows` suite on a Mac still compares all three thousand committed x64 goldens
  /// byte for byte, and a codegen regression that only shows on that target is caught on a host that
  /// cannot run a single one of its binaries. (Writing stays refused — see
  /// <see cref="CheckFragmentGolden"/>.)
  ///
  /// EVERY OTHER PASSING TEST IS GATED — there is no ungated path. Which COMPILE each one is gated
  /// against is <see cref="FragmentSource"/>'s question, and the answer never depends on how the run
  /// went, only on what compiled.
  /// </summary>
  private TestResult ApplyFragmentGolden(TestResult result, string fragmentPath, string content, FragmentTally tally) {
    if (result.Outcome == SpecTestOutcome.Failed) return result;
    if (!FragmentGoldensAreAuthoritative) return result;

    var mismatch = CheckFragmentGolden(fragmentPath, content, tally);
    if (mismatch == null) return result;

    return TestResult.Fail(result.TestName, result.FilePath, result.Duration, mismatch);
  }

  /// <summary>
  /// Compare one test's committed <c>.test</c> golden with the fragment this run produced, or write it
  /// through one of the two doors that are allowed to. Returns null when the golden is satisfied, and
  /// the failure message otherwise.
  ///
  /// WHY THE GOLDENS ARE COMMITTED AT ALL. Running the test proves the generated code is CORRECT — a
  /// value in the wrong register computes the wrong answer and the exit-code assertion fails. It
  /// cannot prove the code is still GOOD: an extra spill, a lost coalesce, a released reference the
  /// slot never held all still compute the right answer, and a suite that only runs the program
  /// reports green while the emitted code silently gets worse. Pinning the IR turns every such change
  /// into a failing test that has to be looked at and justified or fixed. It also reaches where the
  /// run cannot: a test executes ONE path, so a bad allocation in a block it never enters is invisible
  /// to the exit code, while the fragment pins the whole function.
  ///
  /// THE TWO WRITING DOORS, and why they are the only two:
  /// <list type="bullet">
  /// <item>NO GOLDEN ON DISK — a brand-new test. There is nothing to compare against, and failing a
  ///   first run for want of a file it cannot yet have would be a rite, not a gate.</item>
  /// <item><c>--update-required</c> — the one door a change comes through, and deliberately one you
  ///   have to open, because the diff IS the review.</item>
  /// </list>
  ///
  /// ⚖ BOTH DOORS ARE SHUT ON A CROSS-OS RUN (see <see cref="TargetRunHost.MintRefusalFor"/>), and the
  /// FIRST-RUN one is why the rule is asked here as well as at the flag: it needs no flag at all. A
  /// test with no committed golden yet mints one on any run that passes, and under a foreign OS a
  /// compiler-error case passes without ever launching a binary — the same unvalidated golden
  /// `--update-required` is refused for, arriving without it. (`--update-required` itself cannot reach
  /// this method cross-OS: <c>Program.RunSpecTests</c> refuses it before the runner is constructed.)
  ///
  /// ⚠ CROSS-OS RUNS NOW REACH THIS METHOD IN NUMBERS, which they did not when the guard was written:
  /// `spec-test --target=x64-windows` on an arm64-macOS host used to ABORT at the first test binary it
  /// tried to launch (`Win32Exception (13)` on a worker thread, exit 134, a stack trace), and since
  /// PLAN row G12 it runs the whole suite and reports the unrunnable tests as
  /// <see cref="SpecTestOutcome.NotRunHere"/>. Every one of those still arrives here to have its
  /// golden COMPARED — which is the point of the exercise — and the refusal below is what keeps the
  /// two doors that WRITE one shut.
  /// </summary>
  private string? CheckFragmentGolden(string fragmentPath, string content, FragmentTally tally) {
    if (!File.Exists(fragmentPath) || _updateRequired) {
      if (TargetRunHost.MintRefusalFor(_target) is { } refusal) {
        // Into the errors bag rather than the test's own verdict: the test itself did what it was
        // asked, and what failed is that this run could neither compare nor write its golden — which
        // is exactly what that bag reports, and why it makes the run's exit non-zero.
        tally.Errors.Add($"{fragmentPath}: refusing to mint a golden — {refusal}");
        return null;
      }

      File.WriteAllText(fragmentPath, content);
      tally.CountWritten();
      return null;
    }

    // Both sides normalized, so the comparison is about CONTENT. The goldens are committed LF
    // (`.gitattributes`), but a checkout with `core.autocrlf=true` would otherwise fail every fragment
    // in the suite for a reason that is not codegen and that no diff would show.
    var committed = FragmentGenerator.NormalizeToLf(File.ReadAllText(fragmentPath));
    if (committed == content) {
      tally.CountVerified();
      return null;
    }

    return "codegen changed — golden fragment mismatch\n"
      + FirstDifference(committed, content) + "\n"
      + "  If the new output is INTENDED, re-run the FULL suite with --update-required and REVIEW the diff:\n"
      + $"  {fragmentPath}";
  }

  /// <summary>
  /// The first line at which the committed golden and the fresh fragment diverge. A whole-fragment
  /// dump would bury the one line that matters under a hundred that did not move.
  /// </summary>
  private static string FirstDifference(string committed, string actual) {
    var oldLines = committed.Split('\n');
    var newLines = actual.Split('\n');
    var shared = Math.Min(oldLines.Length, newLines.Length);

    for (int i = 0; i < shared; i++) {
      if (oldLines[i] != newLines[i]) {
        return $"  line {i + 1}:\n    golden: {oldLines[i]}\n    actual: {newLines[i]}";
      }
    }

    return $"  golden has {oldLines.Length} lines, actual has {newLines.Length} — one is a prefix of the other";
  }

  /// <summary>
  /// Process a single work item: generate fragment → compile → run → check the test → check its
  /// committed golden.
  /// </summary>
  private TestResult ProcessWorkItem(TestWorkItem item, FragmentSource source, FragmentTally tally) {
    var testSw = Stopwatch.StartNew();

    try {
      // Step 1: Generate the fragment content. Always regenerated — no cache.
      // Fragment content (IR snapshot) is captured untraced; only the real
      // test-run compile enables tracing.
      SetCompileFlags();
      var absolutePath = Path.GetFullPath(item.FragmentPath);
      var (content, genError) = FragmentGenerator.GenerateFragmentContent(item.Test, item.ExePath, absolutePath, _target);
      if (genError != null) {
        tally.Errors.Add($"Error compiling 'specs/fragments/{item.SpecName}/{item.TestName}.test':\n{genError}");
        return TestResult.Fail(item.TestName, item.FragmentPath, testSw.Elapsed, $"Fragment generation failed: {genError}");
      }

      // Step 2: Parse what we just generated — NOT the file on disk, which is now the committed
      // golden and may legitimately differ (that difference is what step 4 reports).
      var fragment = FragmentGenerator.ParseFragmentContent(content, item.FragmentPath);
      if (fragment == null) {
        return TestResult.Fail(item.TestName, item.FragmentPath, testSw.Elapsed, "Failed to parse generated fragment content");
      }

      // Step 3: Run the test (compile + execute + check expectations)
      var result = RunTest(fragment, item);

      // Step 4: Gate the committed golden — unless this test's golden pins its spec's BATCHED
      // compile, in which case CheckBatchFragments does it and this fragment is only a re-run.
      return source == FragmentSource.ThisCompile
        ? ApplyFragmentGolden(result, item.FragmentPath, content, tally)
        : result;
    } catch (Exception ex) {
      return TestResult.Fail(item.TestName, item.FragmentPath, testSw.Elapsed, $"Exception: {ex.Message}");
    }
  }

  /// <summary>
  /// Process a spec batch work item: regenerate the batch fragment file if
  /// stale, compile the batched source once if needed, then run the cached
  /// batch executable once per test that requires execution. Returns one
  /// TestResult per test in the batch (in the same order as item.Tests).
  ///
  /// Batching is an internal optimization. A RUN failure that betrays batching (a test missing
  /// from the batched binary's output, a binary that produced no output at all) transparently
  /// falls back to the per-fragment path so callers see the same individual pass/fail they would
  /// see with --no-batch. A COMPILE failure of the shared source also falls back, so every test
  /// still gets a verdict — but it is reported, not absorbed: see
  /// <see cref="FallbackAfterBatchCompileFailure"/>. Successful batches still produce per-test
  /// slice mismatches (Stdout / exit code) that are indistinguishable from single-test failures.
  /// </summary>
  private TestResult[] ProcessSpecBatch(SpecBatchWorkItem item, FragmentTally tally) {
    // Step 1: build the batched source. All rewritten fragments + the
    // dispatcher's `main` go into one file. The rewriter mangles every
    // top-level decl (functions, types, typealiases, enums, lets, vars,
    // and per-test `main`) so concatenating fragment bodies never collides.
    var (source, skipped, notInBatchedModule) = FragmentGenerator.BuildBatchSource(item.SpecName, item.Tests);
    foreach (var s in skipped) {
      Logger.Debug(LogCategory.Testing, $"[BATCH SKIP] {s}");
    }
    if (source == null) {
      return FallbackBatchToSingles(item, "rewriter rejected all tests", tally);
    }

    SetCompileFlags();

    var compileSw = Stopwatch.StartNew();
    Directory.CreateDirectory(Path.GetDirectoryName(item.BatchExePath)!);
    string? batchedArchIr = null;
    try {
      // Use the spec name (without `_batch` suffix) as the virtualPath stem
      // so the parser-derived namespace matches what a non-batched compile of
      // the same source would produce. Otherwise the per-test IR slices end
      // up with `<spec>_batch.<name>` qualifiers that leak the batching
      // implementation detail.
      var virtualPath = Path.Combine(_fragmentDir, item.SpecName, $"{item.SpecName}.maxon");
      // Spec-fragment single-file: RootPath = the fragment directory itself
      // (decision #2 in the directory-as-module redesign plan).
      var virtualRoot = Path.GetDirectoryName(virtualPath);
      var compilerSources = new[] { new Compiler.SourceFile(virtualPath, source, virtualRoot) };
      var result = new Compiler.Compiler().Compile(compilerSources, item.BatchExePath, returnIr: true, target: _target);
      compileSw.Stop();
      Interlocked.Add(ref _totalCompileMs, compileSw.ElapsedMilliseconds);

      if (!result.Success) {
        var compileError = string.Join("\n", result.Errors.Select(e => e.Format()));
        return FallbackAfterBatchCompileFailure(item, notInBatchedModule.Count, $"batch compile failed: {compileError}", tally);
      }
      batchedArchIr = result.ArchIr;
    } catch (Exception ex) {
      compileSw.Stop();
      Interlocked.Add(ref _totalCompileMs, compileSw.ElapsedMilliseconds);
      return FallbackAfterBatchCompileFailure(item, notInBatchedModule.Count, $"batch compile threw: {ex.Message}", tally);
    }

    // Step 2: Run the batched binary ONCE. The dispatcher runs every
    // included test sequentially and emits framing markers around each;
    // we slice the output to recover per-test stdout and exit code. Tests
    // the rewriter rejected (skipped at build time) are NOT in the binary
    // and run via the per-fragment path instead.
    //
    // Sum the per-test timeouts (or default for tests without an explicit
    // value) so a batch containing a long-timeout test doesn't get killed
    // by the per-test default. This is the same "additive on serial work"
    // semantics callers would expect from running the tests one at a time.
    var batchTimeoutMs = item.Tests.Sum(t => t.TimeoutMs ?? DefaultTestTimeoutMs);
    var batchSw = Stopwatch.StartNew();
    var batchRun = RunExecutable(item.BatchExePath, _tempDir, args: null, timeoutMs: batchTimeoutMs);
    batchSw.Stop();

    // Parse the markers out of stdout. If ANY batched test fails its slice
    // check (or is missing entirely), every batchable test in the batch is
    // re-run via the per-fragment path so the user sees real individual
    // pass/fail with the per-test fragment file path — batching is an
    // implementation detail that must not leak into reports.
    var perTest = ParseBatchOutput(batchRun.Stdout);

    // First pass: gather batched results without committing them. If any
    // batched test fails, we discard the whole set and re-run via the
    // per-fragment path so the user sees real individual pass/fail.
    var results = new TestResult[item.Tests.Length];
    var batchableIdx = new List<int>();
    var batchedResults = new TestResult?[item.Tests.Length];

    // A BATCH THIS HOST COULD NOT LAUNCH DOES NOT FALL BACK, and the reason is that falling back
    // would answer a question nobody asked: re-running each test in its own binary produces the
    // same "cannot be launched here" for every one of them, having paid a separate compile for
    // each. The batched compile ALREADY SUCCEEDED, which is the whole of what these tests' goldens
    // pin, so the per-test verdict is settled without another process.
    var batchUnrunnable = WhyUncomparable(batchRun, _target) is { HostCannotRun: true } why ? why : null;

    // The dispatcher returns 0 on a clean run, so a non-zero PROCESS exit means
    // something happened that the per-test markers cannot express: most importantly
    // a memory leak, which mm_leak_check reports by overriding the exit code with 101
    // as the process leaves. A test's marker carries only the value its own `main`
    // returned, so a leaking batch still prints a full set of passing markers — which
    // is how a leaked reference per routed try-block call sat in the compiler unnoticed
    // behind a green suite. The leak counter is process-global, so the batch cannot say
    // WHICH test leaked; invalidating the batch re-runs each test in its own binary,
    // where its own leak check attributes it.
    var allBatchablePassed = batchUnrunnable != null || batchRun.ExitCode == 0;
    if (batchUnrunnable == null && batchRun.ExitCode != 0) {
      Logger.Debug(LogCategory.Testing,
        $"[BATCH EXIT {batchRun.ExitCode}] {item.SpecName}: batched binary exited non-zero "
        + $"({(batchRun.IsMemoryLeak ? "memory leak" : "crash or runtime error")}) — "
        + "re-running individually to attribute it");
    }

    for (int i = 0; i < item.Tests.Length; i++) {
      var test = item.Tests[i];

      // ASK THE FUNCTION THAT BUILT THE MODULE. This used to re-run `BatchRewriter.Rewrite` and test
      // only its `Batchable` flag, while `BuildBatchSource` also drops a test whose `RewrittenSource`
      // or `MangledMainName` came back null — one membership question decided twice, by two
      // predicates that are not the same predicate. The golden gate is precisely what a divergence
      // would bite: a test believed batched but absent from the module has no IR slice, so its
      // fragment would be pinned WITHOUT its `// CompiledIR` section and go on passing forever. It
      // also drops a second full rewrite of every batchable test's source.
      if (notInBatchedModule.Contains(test.Name)) {
        // Not in the batched module, so compiling it alone is the only content there is.
        results[i] = RunOneAsSingle(item.SpecName, test, item.SpecFile, FragmentSource.ThisCompile, tally);
        continue;
      }
      batchableIdx.Add(i);

      if (batchUnrunnable != null) {
        batchedResults[i] = TestResult.NotRunHere(
          test.Name, PerTestFragmentPath(item.SpecName, test.Name), batchSw.Elapsed, batchUnrunnable.Message);
        continue;
      }

      var batched = CheckBatchedTestResult(item, test, perTest, batchSw.Elapsed);
      if (batched == null || !batched.Passed) {
        allBatchablePassed = false;
      }
      batchedResults[i] = batched;
    }

    if (allBatchablePassed) {
      foreach (var i in batchableIdx) {
        results[i] = batchedResults[i]!;
      }
    } else {
      Logger.Debug(LogCategory.Testing, $"[BATCH FALLBACK] {item.SpecName}: re-running batchable tests individually after batch failure");
      foreach (var i in batchableIdx) {
        results[i] = RunOneAsSingle(item.SpecName, item.Tests[i], item.SpecFile, FragmentSource.BatchedCompile, tally);
      }
    }

    // OUTSIDE the branch, and that is the whole point. These tests' goldens pin the batched COMPILE,
    // which happened above and succeeded — whether the batched BINARY then ran cleanly is a different
    // question, and one that a loaded machine can answer differently from run to run (a batched binary
    // that misses its timeout falls back, and every test then passes individually). Gating here makes
    // the expected content a function of the source and the compiler alone.
    CheckBatchFragments(item, batchableIdx, batchedArchIr, results, tally);

    return results;
  }

  /// <summary>
  /// After a successful batched compile, slice the batched IR text into per-test snippets and gate
  /// each batchable test's committed golden against its slice, downgrading <paramref name="results"/>
  /// where one moved.
  /// </summary>
  private void CheckBatchFragments(SpecBatchWorkItem item, List<int> batchableIdx, string? batchedArchIr, TestResult[] results, FragmentTally tally) {
    // `returnIr: true` is passed unconditionally above, so a batched compile that SUCCEEDED and
    // returned nothing is the runner and the compiler disagreeing — not a condition to skip a gate
    // over silently, which is how the goldens stopped gating anything in the first place.
    if (batchedArchIr == null) {
      tally.Errors.Add($"[BATCH IR] {item.SpecName}: batched compile succeeded but returned no IR, "
        + "so no fragment golden could be checked");
      return;
    }

    var batchableTests = batchableIdx.Select(i => item.Tests[i]).ToList();
    Dictionary<string, string> perTestIr;
    try {
      perTestIr = FragmentGenerator.SplitBatchedIr(batchedArchIr, batchableTests);
    } catch (Exception ex) {
      tally.Errors.Add($"[BATCH IR SPLIT] {item.SpecName}: {ex.Message}");
      return;
    }

    foreach (var i in batchableIdx) {
      var test = item.Tests[i];
      perTestIr.TryGetValue(test.Name, out var ir);
      var content = FragmentGenerator.GenerateFragmentContentWithIr(test, ir);
      var fragmentPath = PerTestFragmentPath(item.SpecName, test.Name);
      try {
        results[i] = ApplyFragmentGolden(results[i], fragmentPath, content, tally);
      } catch (Exception ex) {
        tally.Errors.Add($"[BATCH FRAGMENT] {fragmentPath}: {ex.Message}");
      }
    }
  }

  /// <summary>
  /// Per-test slice of a batched binary's output: the test's stdout and the
  /// exit code its renamed-main returned.
  /// </summary>
  private record BatchedTestResult(string Stdout, int ExitCode);

  /// <summary>
  /// Walk the batched binary's stdout for the BEGIN/END markers and extract
  /// each test's stdout and exit code. Tests with no END marker are absent
  /// from the dictionary — the caller treats them as "did not run".
  /// </summary>
  private static Dictionary<string, BatchedTestResult> ParseBatchOutput(string stdout) {
    var results = new Dictionary<string, BatchedTestResult>();
    var beginMarker = FragmentGenerator.BatchTestBeginMarker;
    var endMarker = FragmentGenerator.BatchTestEndMarker;
    var suffix = FragmentGenerator.BatchMarkerSuffix;

    int pos = 0;
    while (pos < stdout.Length) {
      var beginIdx = stdout.IndexOf(beginMarker, pos, StringComparison.Ordinal);
      if (beginIdx < 0) break;
      var nameStart = beginIdx + beginMarker.Length;
      var nameEnd = stdout.IndexOf(suffix, nameStart, StringComparison.Ordinal);
      if (nameEnd < 0) break;
      var testName = stdout[nameStart..nameEnd];

      // Find the corresponding END marker for THIS test.
      var endTag = endMarker + testName + ":";
      var endIdx = stdout.IndexOf(endTag, nameEnd, StringComparison.Ordinal);
      if (endIdx < 0) {
        // No END marker — test crashed mid-run. Skip; caller reports this.
        pos = nameEnd + suffix.Length;
        continue;
      }
      var exitStart = endIdx + endTag.Length;
      var exitEnd = stdout.IndexOf(suffix, exitStart, StringComparison.Ordinal);
      if (exitEnd < 0) break;

      // The exit code is emitted verbatim by the dispatcher's captured
      // `let ec_<name> = renamedMain()`, so the format is fully under our
      // control — a parse failure here means the dispatcher template has
      // drifted from this parser, not a runtime condition.
      var exitStr = stdout[exitStart..exitEnd];
      if (!int.TryParse(exitStr, out var ec)) {
        throw new InvalidOperationException(
          $"batch dispatcher emitted non-integer exit code '{exitStr}' for test '{testName}'");
      }

      // The test's actual stdout is everything between the BEGIN-marker line
      // and the END-marker line. We also include the leading and trailing
      // newlines the dispatcher emits around the markers; trimming happens
      // at comparison time.
      var stdoutStart = nameEnd + suffix.Length;
      // Skip the trailing \n of the BEGIN marker line.
      if (stdoutStart < stdout.Length && stdout[stdoutStart] == '\n') stdoutStart++;
      var testStdout = stdout[stdoutStart..endIdx];
      // Trim the trailing \n that came right before the END marker.
      if (testStdout.EndsWith('\n')) testStdout = testStdout[..^1];

      results[testName] = new BatchedTestResult(testStdout, ec);
      pos = exitEnd + suffix.Length;
    }

    return results;
  }

  /// <summary>
  /// Compare one batched test's parsed result against its expectation. Returns
  /// null if the test is missing from the parsed output (an earlier test in
  /// the batch crashed, or the binary failed before any tests ran); the caller
  /// re-runs those tests individually so the user sees a real per-test result.
  /// </summary>
  private TestResult? CheckBatchedTestResult(SpecBatchWorkItem item, TestCase test, Dictionary<string, BatchedTestResult> parsed, TimeSpan elapsed) {
    if (!parsed.TryGetValue(test.Name, out var slice)) {
      return null;
    }

    var success = (SuccessExpectation)test.Expectation;
    // Surface the per-test fragment path so failure reports point at the same
    // file the per-fragment path would — even though we didn't write a fragment
    // for the batched compile itself.
    var perTestFragmentPath = PerTestFragmentPath(item.SpecName, test.Name);

    // ASKED, not restated. A batched test's exit code and stdout are judged by exactly the routines
    // the per-fragment path uses — the POSIX 8-bit exit mask and the CRLF/path normalization are one
    // rule each, and a batched run that folded either differently would pass or fail tests the
    // unbatched run of the same program does not.
    var exitCodeError = success.ExitCode.HasValue
      ? CheckExitCode(success.ExitCode.Value, slice.ExitCode)
      : null;
    if (exitCodeError != null) {
      return TestResult.Fail(test.Name, perTestFragmentPath, elapsed, exitCodeError);
    }

    var stdoutError = success.Stdout != null ? CheckStdout(success.Stdout, slice.Stdout) : null;
    if (stdoutError != null) {
      return TestResult.Fail(test.Name, perTestFragmentPath, elapsed, stdoutError);
    }

    // Note: stderr is shared across the whole batch (it's the parent process's
    // stderr stream), so we can't attribute stderr to individual tests here.
    // Tests with `Stderr:` expectations are excluded from batching by the
    // eligibility filter.

    return TestResult.Pass(test.Name, perTestFragmentPath, elapsed);
  }

  /// <summary>
  /// Run a single test (originally part of a spec batch) through the
  /// per-fragment compilation path. Used for tests the rewriter rejects and
  /// for the whole batch when batching fails (compile error or any per-test
  /// slice mismatch) — the user always sees real per-test pass/fail.
  /// </summary>
  private TestResult RunOneAsSingle(string specName, TestCase test, FileInfo specFile, FragmentSource source, FragmentTally tally) {
    var fragmentPath = PerTestFragmentPath(specName, test.Name);
    var irExePath = Path.Combine(_fragmentDir, specName, $"{test.Name}.ir_exe");
    var single = new TestWorkItem(fragmentPath, irExePath, specName, test.Name, test, specFile);
    return ProcessWorkItem(single, source, tally);
  }

  /// <summary>
  /// The batched compile/run failed in a way that doesn't identify a specific
  /// test (rewriter rejected everything, the shared compile errored, or the
  /// compiler threw). Re-run every batchable test in `item.Tests` through the
  /// per-fragment path so the user sees real individual pass/fail rather than
  /// a generic batch-flavored failure message. Tests the rewriter rejected
  /// already fall through to the per-fragment path naturally.
  /// </summary>
  private TestResult[] FallbackBatchToSingles(SpecBatchWorkItem item, string reason, FragmentTally tally) {
    Logger.Debug(LogCategory.Testing, $"[BATCH FALLBACK] {item.SpecName}: {reason}");
    var results = new TestResult[item.Tests.Length];
    for (int i = 0; i < item.Tests.Length; i++) {
      // No batched module exists for this spec — the rewriter rejected everything, or the batched
      // source did not compile — so this per-fragment compile IS what mints these goldens.
      results[i] = RunOneAsSingle(item.SpecName, item.Tests[i], item.SpecFile, FragmentSource.ThisCompile, tally);
    }
    return results;
  }

  /// <summary>
  /// A batched module that does not compile is a compiler or rewriter defect, never a property of
  /// the spec: whether a test can be batched at all is decided per test before this point
  /// (<c>IsBatchable</c>, <c>[BATCH SKIP]</c>), so every test that reached the module compiles on
  /// its own. The fallback still runs each one, but the goldens it mints and compares have a
  /// per-fragment shape that no unfiltered batched run produces — they gate nothing — so the
  /// failure goes into the tally, which reaches the normal output and the exit code, rather than a
  /// debug line that only a run nobody makes would show.
  /// </summary>
  private TestResult[] FallbackAfterBatchCompileFailure(SpecBatchWorkItem item, int rejectedByRewriter, string reason, FragmentTally tally) {
    var batched = item.Tests.Length - rejectedByRewriter;
    tally.Errors.Add(
      $"[BATCH COMPILE] {item.SpecName}: the batched module did not compile, so its {batched} batchable "
      + $"golden(s) were compared or written from per-fragment compiles that no authoritative run "
      + $"produces — {reason}");
    return FallbackBatchToSingles(item, reason, tally);
  }

  /// <summary>
  /// Compiled artifacts a run leaves beside the committed <c>.test</c> goldens, by glob.
  ///
  /// <c>*.mxdbg</c> is here because a compile with debug info on writes a sidecar next to every
  /// binary it produces — so the moment anything switches that flag on for a compile aimed at this
  /// tree, every deleted executable leaves one behind, untracked, in a committed directory. The glob
  /// catches all three spellings (<c>x.exe.mxdbg</c>, <c>x.ir_exe.mxdbg</c>, and the extensionless
  /// posix <c>x.mxdbg</c>), which is why it is a pattern rather than a suffix per binary kind.
  /// </summary>
  private static readonly string[] FragmentArtifactPatterns = ["*.exe", "*.ir_exe", "*.mxdbg"];

  /// <summary>
  /// Recursively clean up compiled artifacts from the fragment directory and its subdirectories.
  /// On Windows the binaries are .exe files; on macOS/Linux they are extensionless files
  /// whose name matches a .test file in the same directory.
  /// </summary>
  private static void CleanupExecutables(string directory) {
    if (!Directory.Exists(directory)) return;

    try {
      foreach (var pattern in FragmentArtifactPatterns) {
        foreach (var artifact in Directory.GetFiles(directory, pattern)) {
          try {
            File.Delete(artifact);
          } catch {
            // Ignore deletion errors (file may be locked)
          }
        }
      }

      // Delete extensionless executables (macOS/Linux) that have a matching .test file
      foreach (var file in Directory.GetFiles(directory)) {
        if (Path.GetExtension(file) == "" && File.Exists(file + ".test")) {
          try {
            File.Delete(file);
          } catch {
            // Ignore deletion errors (file may be locked)
          }
        }
      }

      // Recurse into subdirectories
      foreach (var subDir in Directory.GetDirectories(directory)) {
        CleanupExecutables(subDir);
      }
    } catch {
      // Ignore directory access errors
    }
  }

  /// ASKED, not restated — the same answer <see cref="Program.GetOutputExtension"/> gives the compile
  /// that writes the file. Spelled here as its own `Os == "windows"` test, it was a third copy of the
  /// rule (with `Program.GetOutputExtension` and `FragmentGenerator`) that would silently hand back
  /// "" for an OS the compiler has no writer for, where the compiler's own answer throws.
  private string ExeExtension => Program.GetOutputExtension(_target);

  /// Where one test's committed `.test` golden lives. Five call sites spelled this join out, and they
  /// have to agree exactly: the batched IR gate, the batched failure report and the per-fragment path
  /// all name the SAME file for one test, and a report pointing at a path the gate did not read is a
  /// report about the wrong file.
  private string PerTestFragmentPath(string specName, string testName) =>
    Path.Combine(_fragmentDir, specName, $"{testName}.test");

  private TestResult RunTest(Fragment fragment, TestWorkItem item) {
    var sw = Stopwatch.StartNew();

    // BOUND ONCE. Every verdict this method returns is about the same test, names the same fragment
    // file and is measured by the same stopwatch, and it returns from twenty places — spelled out at
    // each, that triple is twenty chances for a report to point at another test's file.
    TestResult Fail(string message) => TestResult.Fail(fragment.TestName, fragment.FilePath, sw.Elapsed, message);
    TestResult Pass() => TestResult.Pass(fragment.TestName, fragment.FilePath, sw.Elapsed);
    TestResult NotComparable(UncomparableRun why) => Uncomparable(fragment.TestName, fragment.FilePath, sw.Elapsed, why);

    try {
      // Cached executable path in .spec-cache/{specName}/{testName}.exe
      var specCacheDir = FragmentGenerator.GetSpecCacheDir(_fragmentDir);
      var cachedExePath = Path.Combine(specCacheDir, item.SpecName, $"{fragment.TestName}{ExeExtension}");

      string? compileError = null;

      // Compile if needed (to cache dir for success tests, to temp for compiler-error tests)
      if (fragment.Expectation is CompilerErrorExpectation errorExpectation) {
        // CompilerError tests: compile to temp to capture the error message
        var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}{ExeExtension}");
        var compileSw = Stopwatch.StartNew();
        var (Success, Error) = CompileToExecutable(fragment, tempExe, _target);
        compileSw.Stop();
        Interlocked.Add(ref _totalCompileMs, compileSw.ElapsedMilliseconds);
        compileError = Error;
        CompiledArtifact.Delete(tempExe);

        var compiledSuccessfully = compileError == null;
        if (compiledSuccessfully) {
          return Fail("Expected compiler error but compilation succeeded");
        }

        // Normalize and compare stderr exactly
        var expectedNorm = NormalizeStderr(errorExpectation.ExpectedStderr);
        var actualNorm = NormalizeStderr(compileError!);
        if (expectedNorm != actualNorm) {
          return Fail($"Stderr mismatch.\nExpected:\n  {expectedNorm}\nActual:\n  {actualNorm}");
        }

        return Pass();
      }

      // Success expectation — compile to the on-disk staging path so the run
      // step has a stable file to invoke.
      var successCompileSw = Stopwatch.StartNew();
      Directory.CreateDirectory(Path.GetDirectoryName(cachedExePath)!);
      var (_, successError) = CompileToExecutable(fragment, cachedExePath, _target);
      successCompileSw.Stop();
      Interlocked.Add(ref _totalCompileMs, successCompileSw.ElapsedMilliseconds);
      compileError = successError;
      var exePath = cachedExePath;

      // Expect compilation to succeed
      if (compileError != null) {
        return Fail($"Compilation failed: {compileError}");
      }

      var successExpectation = (SuccessExpectation)fragment.Expectation;

      // Check Required IR by compiling fresh with all pipeline stages.
      // Use a dedicated temp exe so we never overwrite the cached exe.
      if (successExpectation.RequiredIR != null) {
        var (irSources, irTempDir) = BuildTestSources(fragment.SourceFiles, fragment.FilePath, fragment.Source);
        var irExePath = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}_ir{ExeExtension}");
        SetCompileFlags();

        // `finally` for the same reason the three --update-required probes below use one. `irTempDir`
        // lives under the OS temp directory and NOTHING sweeps it — CleanupExecutables only ever
        // visits `_tempDir` — so a skipped delete here is not litter that a later run collects.
        Compiler.CompileResult irResult;
        try {
          irResult = new Compiler.Compiler().Compile(irSources, irExePath, returnIr: true, target: _target);
        } finally {
          if (irTempDir != null) {
            try { Directory.Delete(irTempDir, recursive: true); } catch { }
          }
          CompiledArtifact.Delete(irExePath);
        }

        if (!irResult.Success || irResult.AllStagesIr == null) {
          return Fail("RequiredIR specified but compilation failed or produced no IR");
        }

        var (Passed, Message) = CheckRequiredIr(successExpectation.RequiredIR, irResult.AllStagesIr, _target);
        if (!Passed) {
          return Fail($"Required IR mismatch: {Message}");
        }
      }

      // The section pins read PE SECTION HEADERS (`ParsePeSections`), so they are asked of a PE and
      // of nothing else. ASKED of the roster — `ObjectFormat` is where "which targets are a PE" is
      // decided — rather than spelled here as `Os == "windows"`, which was a second, independent
      // statement of the same fact: add a writer for another PE-hosted OS and the roster would know
      // while this line would silently stop checking.
      if (_target.ObjectFormat == Compiler.ObjectFormat.Pe) {
        foreach (var (sectionName, required) in RequiredSectionPins(successExpectation)) {
          var (sectionPassed, sectionMessage) = CheckRequiredSection(required, exePath, sectionName);
          if (sectionPassed) continue;

          return Fail($"Required {sectionName} mismatch: {sectionMessage}");
        }
      }

      // mm-trace capture mode: run the binary under `monitor --filter=mm`,
      // decode + normalize the binary event stream, and compare against the
      // authored golden. The monitor's stdout interleaves trace lines with the
      // child's own stdout, so a plain Stdout block (if present) is checked via
      // a separate untraced run rather than against the monitor output.
      if (fragment.MmTrace) {
        var monitorRun = CaptureMmTrace(exePath, fragment.TimeoutMs ?? DefaultTestTimeoutMs);
        var monitorStderr = monitorRun.Stderr;

        if (WhyUncomparable(monitorRun, _target) is { } monitorIncomplete) {
          return NotComparable(monitorIncomplete);
        }

        // THE EXIT CODE COMES FIRST. It is the monitor's, and so the child's —
        // but only if the monitor got as far as running one. A monitor that died
        // on its own produces a trace that is EMPTY rather than wrong, and an
        // empty trace compared first reports "mm-trace mismatch" — the symptom —
        // while the exit code that names it as a CRASH is never reached. That is
        // how a monitor exiting 134 against `ExitCode: 0` was read for as long as
        // it was as a program that allocated nothing.
        if (successExpectation.ExitCode.HasValue) {
          var exitError = CheckExitCode(successExpectation.ExitCode.Value, monitorRun.ExitCode);
          if (exitError != null) {
            return Fail(WithMonitorStderr(exitError, monitorStderr));
          }
        }

        var expectedTrace = NormalizeMmTrace(successExpectation.MmTraceExpected ?? "");
        var actualTrace = NormalizeMmTrace(monitorRun.Stdout);
        if (expectedTrace != actualTrace) {
          return Fail(WithMonitorStderr(
              $"mm-trace mismatch:\nExpected:\n{expectedTrace}\nActual:\n{actualTrace}", monitorStderr));
        }

        if (successExpectation.Stdout != null) {
          var plainRun = RunExecutable(exePath, _tempDir, fragment.Args, fragment.TimeoutMs);
          if (WhyUncomparable(plainRun, _target) is { } plainIncomplete) {
            return NotComparable(plainIncomplete);
          }

          if (CheckStdout(successExpectation.Stdout, plainRun.Stdout) is { } stdoutError) {
            return Fail(stdoutError);
          }
        }

        return Pass();
      }

      // Run the executable if we have runtime expectations
      if (successExpectation.ExitCode.HasValue || successExpectation.Stdout != null || successExpectation.Stderr != null) {
        var run = RunExecutable(exePath, _tempDir, fragment.Args, fragment.TimeoutMs);

        // Ahead of every expectation below: a killed or half-drained child has a PREFIX, and each
        // of those checks would happily match one — and a child that never started has nothing at
        // all, which every one of them would read as an empty stdout and a zero exit code.
        if (WhyUncomparable(run, _target) is { } incomplete) {
          return NotComparable(incomplete);
        }

        if (successExpectation.ExitCode.HasValue) {
          var exitError = CheckExitCode(successExpectation.ExitCode.Value, run.ExitCode);
          if (exitError != null) {
            return Fail(exitError);
          }
        }

        if (successExpectation.Stdout != null) {
          var stdoutError = CheckStdout(successExpectation.Stdout, run.Stdout);
          if (stdoutError != null) {
            return Fail(stdoutError);
          }
        }

        if (successExpectation.Stderr != null) {
          var normalize = fragment.AsyncTrace ? NormalizeAsyncTraceStderr : (Func<string, string>)(s => s.Replace("\r\n", "\n").Trim());
          var expectedStderr = normalize(successExpectation.Stderr);
          var actualStderr = normalize(StripFaultRipSuffix(run.Stderr));
          if (expectedStderr != actualStderr) {
            return Fail($"Stderr mismatch:\nExpected: {expectedStderr}\nActual: {actualStderr}");
          }
        } else if (!string.IsNullOrWhiteSpace(run.Stderr)) {
          return Fail($"Unexpected stderr output:\n{run.Stderr.Trim()}");
        }
      }

      return Pass();
    } catch (Exception ex) {
      return Fail($"Exception: {ex.Message}");
    }
  }

  /// <summary>
  /// Compare an actual process exit code against the expected value, applying
  /// the 8-bit mask that macOS/Linux impose on exit codes. Returns an error
  /// message on mismatch, or null on match. Shared by the plain-run and
  /// mm-trace-capture paths.
  /// </summary>
  private static string? CheckExitCode(int expected, int actual) {
    var expectedCode = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? expected : expected & 0xFF;
    return actual == expectedCode ? null : $"Expected exit code {expectedCode}, got {actual}";
  }

  /// <summary>
  /// Compare expected vs actual stdout after CRLF-folding, trimming, and
  /// machine-specific-path normalization (so tests are portable across OSes).
  /// Returns an error message on mismatch, or null on match.
  /// </summary>
  private string? CheckStdout(string expected, string actual) {
    var expectedStdout = NormalizePathsForComparison(expected.Replace("\r\n", "\n").Trim());
    var actualStdout = NormalizePathsForComparison(actual.Replace("\r\n", "\n").Trim());
    return expectedStdout == actualStdout ? null : $"Stdout mismatch:\nExpected: {expectedStdout}\nActual: {actualStdout}";
  }

  /// <summary>
  /// `spec-test --debug-info`: force <c>DebugInfo</c> on for EVERY spec compile, whatever each test's
  /// directive says. It answers "how much of the corpus survives the path `maxon build` takes by
  /// DEFAULT?", which no per-test directive can ask of 3200 programs at once — and it is the run in
  /// which the committed fragment goldens still verifying is the proof that debug info changed not one
  /// emitted byte.
  ///
  /// ⚠ It was an ENVIRONMENT VARIABLE (`MAXON_SPEC_DEBUG_INFO=1`) and no script, gate or CI path ever
  /// set it. A switch nothing turns on cannot fail, so it measured nothing and rotted quietly; a flag
  /// can be written into <c>buildall.sh</c>, which is where it now is.
  ///
  /// Static, and set from the CLI before any runner exists, because <see cref="SetCompileFlags"/> is
  /// static (one of its callers has no instance) and is on the hot path of every one of those compiles.
  /// </summary>
  internal static bool ForceDebugInfo { get; set; }

  /// <summary>
  /// Set the process-wide (ThreadStatic) compile flags every spec-test compile
  /// depends on. All compile sites route through here so the trace producers
  /// (`MmTrace` text-stderr, the mm-trace binary `DebugStream`, `AsyncTrace`)
  /// and `DebugInfo` are explicitly (re)set on each compile — a flag left set
  /// from a prior compile on the same worker thread would silently mis-trace an
  /// unrelated test. `MmDebug` (runtime debug checks) is never enabled by the
  /// harness, and `Testing` is always on.
  ///
  /// ⚠ `DebugInfo` HAD NO CALLER HERE AT ALL, and that is the whole reason the debug-info lowering
  /// path could crash on a program this suite compiles every run: the flag is [ThreadStatic], only
  /// `maxon build` and the MCP's debug build ever wrote it, and the spec workers are their own
  /// threads — so all ~3200 compiles read the CLR default `false`. It defaults to false HERE too,
  /// because the goldens pin a no-debug-info compile; it is a test's `&lt;!-- DebugInfo --&gt;`
  /// directive that turns it on, for the one compile whose binary gets run.
  /// </summary>
  private static void SetCompileFlags(bool mmTrace = false, bool debugStream = false, bool asyncTrace = false,
      bool debugInfo = false) {
    Compiler.Compiler.MmTrace = mmTrace;
    Compiler.Compiler.DebugStream = debugStream;
    Compiler.Compiler.AsyncTrace = asyncTrace;
    Compiler.Compiler.DebugInfo = debugInfo || ForceDebugInfo;
    Compiler.Compiler.MmDebug = false;
    Compiler.Compiler.Testing = true;
  }

  /// <summary>
  /// Build the compiler's source array for one spec test — the ONE place that knows a test may be
  /// several files, and how a multi-file one reaches the compiler (split into a temp directory,
  /// which becomes the module RootPath; decision #2 in the directory-as-module redesign).
  ///
  /// ⚠ WRITTEN ONCE BECAUSE THE THIRD COPY WAS WRONG AND SILENT. Three call sites needed this —
  /// the run's RequiredIR check, <see cref="CompileToExecutable"/>, and
  /// <see cref="UpdateRequiredInSpecFiles"/> — and the third built a SINGLE `SourceFile` holding
  /// the merged multi-file text under the fragment's own `<name>.test` filename. A test whose
  /// sources include a `*.test.maxon` file then failed to compile with E2058 ("a 'test'
  /// declaration is only allowed in a file whose name ends in '.test.maxon'"), which that path
  /// discarded without a word — so `--update-required` could never regenerate those blocks and
  /// never said so. `test-declaration/survives-dead-function-elimination` was unregenerable for
  /// exactly that reason.
  ///
  /// Returns the sources and, for a multi-file test, the temp directory the caller must delete.
  /// </summary>
  private static (Compiler.SourceFile[] Sources, string? TempDir) BuildTestSources(
      List<(string FileName, string Source)>? sourceFiles, string singleFilePath, string singleFileSource) {
    if (sourceFiles == null) {
      // Spec-fragment single-file: RootPath = the fragment directory.
      return ([new Compiler.SourceFile(singleFilePath, singleFileSource, Path.GetDirectoryName(singleFilePath))], null);
    }

    var tempDir = Path.Combine(Path.GetTempPath(), $"maxon-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempDir);
    // Spec-fragment multi-file: RootPath = tempDir (decision #2).
    Compiler.SourceFile[] sources = [.. sourceFiles.Select(f => {
      var path = Path.Combine(tempDir, f.FileName);
      Directory.CreateDirectory(Path.GetDirectoryName(path)!);
      File.WriteAllText(path, f.Source);
      return new Compiler.SourceFile(path, f.Source, tempDir);
    })];
    return (sources, tempDir);
  }

  /// <param name="target">
  /// REQUIRED, with no host default. All three callers already pass <c>_target</c>, so the default
  /// was dead — but dead in the one file where a forgotten target is a silent wrong answer rather
  /// than a compile error: a cross-target run that omitted it would compile the fragment for the
  /// HOST and report the result under the requested triple's name.
  /// </param>
  private static (bool Success, string? Error) CompileToExecutable(Fragment fragment, string outputPath, Compiler.CompileTarget target) {
    try {
      // Map from per-file path to (fragmentPath, lineOffset) so multi-file
      // error messages can be rewritten to point at the merged fragment
      // file with line numbers matching the spec's expected stderr.
      Dictionary<string, (string FragmentPath, int LineOffset)>? splitFileMap = null;

      var (sources, tempDir) = BuildTestSources(fragment.SourceFiles, fragment.FilePath, fragment.Source);
      if (tempDir != null) {
        splitFileMap = ComputeSplitFileMap(fragment, tempDir);
      }

      try {
        // mm-trace capture mode compiles with the binary DebugStream producer
        // (emits the __ds_* funcs + type-name tag blob), NOT the legacy
        // text-stderr MmTrace producer: the harness decodes the ring buffer
        // via `monitor --filter=mm`. Both gate the same MM instrumentation
        // sites, so DebugStream alone is sufficient.
        SetCompileFlags(debugStream: fragment.MmTrace, asyncTrace: fragment.AsyncTrace,
          debugInfo: fragment.DebugInfo);
        var result = new Compiler.Compiler().Compile(sources, outputPath, target: target);
        var error = result.Errors.Count > 0
          ? string.Join("\n", result.Errors.Select(e => e.Format()))
          : null;
        // Normalize temp directory paths to just filenames for multi-file tests
        if (error != null && tempDir != null) {
          var root = Compiler.CompileError.ProjectRoot ?? Environment.CurrentDirectory;
          var relativeTempDir = Path.GetRelativePath(root, tempDir).Replace('\\', '/');
          if (!relativeTempDir.EndsWith('/')) relativeTempDir += '/';
          error = error.Replace(relativeTempDir, "");
        }
        // Rewrite per-file split paths to the merged fragment path, with
        // line numbers offset to match the merged source so errors line up
        // with what the spec asserts.
        if (error != null && splitFileMap != null) {
          error = RewriteMultiFileErrorPaths(error, splitFileMap);
        }
        return (result.Success, error);
      } finally {
        if (tempDir != null) {
          try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
      }
    } catch (Exception ex) {
      return (false, ex.Message);
    }
  }

  private const int DefaultTestTimeoutMs = 2000;

  /// <summary>
  /// Environment variable that pins the runtime scheduler to a single OS
  /// worker so green-thread event ordering is deterministic. Set defensively
  /// for mm-trace captures; a harmless no-op until Foundation 1 wires it into
  /// the runtime.
  /// </summary>
  private const string MaxProcsEnvVar = "MAXON_MAX_PROCS";

  /// <summary>
  /// <see cref="MaxProcsEnvVar"/> value that clamps the scheduler to a single
  /// worker (deterministic mm-trace event ordering).
  /// </summary>
  private const string SingleWorkerProcCount = "1";

  /// <summary>
  /// Host-compiler subcommand and MM-only filter that decode an mm-trace
  /// binary's debug-event stream (`maxon monitor --filter=mm &lt;exe&gt;`).
  /// </summary>
  private const string MonitorSubcommand = "monitor";
  private const string MmOnlyFilterArg = "--filter=mm";

  /// <summary>
  /// Prefix shared by every decoded mm-trace event line (`mm_alloc`,
  /// `mm_incref`, `mm_free`, …); used to keep only genuine trace lines when
  /// normalizing.
  /// </summary>
  private const string MmTraceEventPrefix = "mm_";

  /// <summary>
  /// Why this run's output cannot be compared against an expectation — and WHOSE limit that is.
  /// Null when it can be compared.
  ///
  /// <para>The second field is the whole of what row G12 added, and it is not a nicety: a launch that
  /// failed is a real DEFECT when the host could have run the binary (a locked file, a mode bit the
  /// compiler failed to set, a malformed image) and is the HOST's limit when it never could (a PE on
  /// macOS). Collapsing the two would excuse an unexecutable binary this compiler had just emitted —
  /// the worst possible false green, since a codegen bug that produces an unrunnable program is
  /// exactly what the run is there to catch.</para>
  /// </summary>
  internal sealed record UncomparableRun(string Message, bool HostCannotRun);

  /// <summary>
  /// Classify one launch. A child that was KILLED for outliving its deadline, or whose streams never
  /// finished draining, did not produce a result — it produced a PREFIX. Comparing a prefix against a
  /// golden is how a hanging binary passes: it prints exactly what the spec expects, then hangs, and
  /// every expectation matches. It used to be caught by accident, because the launcher replaced a
  /// killed child's output with the words "Process timed out" and the stderr check tripped on them.
  /// That substitution is gone — the prefix is now reported, because `maxon test` needs it to say
  /// WHICH test was running — so the accident has to become a rule.
  ///
  /// <see cref="ProcessRunOutcome"/> is the launcher's answer to "did this finish", and this is the
  /// one place the spec runner asks it, so no individual expectation check has to remember to.
  ///
  /// Static and internal so <see cref="SpecRunSelfTest"/> can pin the LaunchFailed rows without
  /// standing up a runner or a suite.
  /// </summary>
  internal static UncomparableRun? WhyUncomparable(ProcessRunResult run, Compiler.CompileTarget target) => run.Outcome switch {
    ProcessRunOutcome.Exited => null,
    ProcessRunOutcome.TimedOut => new UncomparableRun(
      "process did not exit before its timeout and was killed. What it wrote before the kill is a "
      + $"prefix, not a result, so no expectation was compared against it:\nstdout:\n{run.Stdout}\nstderr:\n{run.Stderr}",
      HostCannotRun: false),
    ProcessRunOutcome.OutputReadTimedOut => new UncomparableRun(
      $"process exited ({run.ExitCode}) but its output never finished draining, so what it printed "
      + "is incomplete and no expectation was compared against it",
      HostCannotRun: false),
    ProcessRunOutcome.LaunchFailed => new UncomparableRun(
      $"the test binary never started, so no expectation was compared against it: {run.Stderr}",
      HostCannotRun: TargetRunHost.WhyCannotRun(target) != null),
    var unhandled => throw new ArgumentOutOfRangeException(nameof(run), unhandled,
      "Unhandled process outcome; every outcome must state whether its output is comparable."),
  };

  /// <summary>
  /// The verdict a test earns when its run could not be compared. Internal and static for the same
  /// reason as <see cref="WhyUncomparable"/>: this mapping — and NOT the launcher outcome — is what
  /// decides whether a test that did not run is a failure or a limit of this machine, so it is the
  /// thing worth pinning.
  /// </summary>
  internal static TestResult Uncomparable(string testName, string filePath, TimeSpan duration, UncomparableRun why) =>
    why.HostCannotRun
      ? TestResult.NotRunHere(testName, filePath, duration, why.Message)
      : TestResult.Fail(testName, filePath, duration, why.Message);

  /// <summary>
  /// The result every launch of a target binary gets when this host cannot execute one, or null when
  /// it can — in which case the caller launches for real.
  ///
  /// <para>SYNTHESIZED RATHER THAN ATTEMPTED, for two reasons. The message is then the RULE's ("a PE
  /// can only be spawned on Windows") instead of an errno the reader has to interpret — a foreign-OS
  /// binary on macOS reports `Permission denied`, which reads like a mode bit and is not one. And a
  /// cross-OS suite does not spend three thousand doomed <c>fork</c>/<c>exec</c> pairs to be told the
  /// same thing three thousand times.</para>
  /// </summary>
  private ProcessRunResult? UnrunnableTargetBinary() =>
    TargetRunHost.WhyCannotRun(_target) is { } why
      ? new ProcessRunResult(ProcessRunOutcome.LaunchFailed, ProcessLauncher.NoExitCodeFromProcess, "", why)
      : null;

  /// <summary>
  /// Run a compiled test binary and capture it. This is where the spec suite's own
  /// policy lives — a test with no stated timeout gets <see cref="DefaultTestTimeoutMs"/>,
  /// and a spec's `Args:` line is a command line its author already quoted, so it is
  /// passed through verbatim rather than re-split into argv.
  ///
  /// Every caller must pass the result through <see cref="WhyUncomparable"/> before comparing it
  /// against anything.
  /// </summary>
  private ProcessRunResult RunExecutable(string exePath, string workingDirectory, string? args = null, int? timeoutMs = null) {
    if (UnrunnableTargetBinary() is { } unrunnable) return unrunnable;

    // Code signing and executable permissions are now handled by MachOWriter at compile time
    return ProcessLauncher.Run(new ProcessLaunchRequest {
      ExecutablePath = exePath,
      Arguments = ProcessArguments.Verbatim(args),
      WorkingDirectory = workingDirectory,
      TimeoutMs = timeoutMs ?? DefaultTestTimeoutMs,
    });
  }

  /// <summary>
  /// Run the mm-trace test binary under this same maxon.exe's
  /// `monitor --filter=mm` decoder and capture the monitor's stdout — the
  /// formatted trace lines interleaved with the child's own stdout. Returns
  /// the raw captured stdout and the monitor's exit code (which is the
  /// child's exit code). The monitor sets the child's MAXON_DEBUGSTREAM, so
  /// we don't. Normalize the returned stdout with <see cref="NormalizeMmTrace"/>
  /// before comparing against a golden.
  ///
  /// STDERR IS RETURNED, NOT DISCARDED. It carries the `[debugstream]` event
  /// summary, the child's own stderr, and — the reason this matters — the
  /// monitor's unhandled-exception message and stack trace. Dropping it made a
  /// monitor that CRASHED before spawning the child indistinguishable from a
  /// program that simply allocated nothing: the mm-trace goldens failed against
  /// an empty trace while the `PlatformNotSupportedException` explaining it was
  /// thrown away by this very call.
  /// </summary>
  private ProcessRunResult CaptureMmTrace(string exePath, int timeoutMs) {
    // The MONITOR is native and would start happily; the binary it exists to trace is the target's,
    // so this door needs the same preflight as a plain run. Without it a cross-OS mm-trace test
    // reported an empty trace — the monitor's own report of a child it could not spawn — as a
    // codegen mismatch.
    if (UnrunnableTargetBinary() is { } unrunnable) return unrunnable;

    // Self-invoke the running maxon.exe as the monitor: ProcessPath is the
    // host compiler binary, which carries the DebugStreamMonitor CLI.
    var monitorExe = Environment.ProcessPath
      ?? throw new InvalidOperationException("Environment.ProcessPath is null; cannot self-invoke as the mm-trace monitor.");

    return ProcessLauncher.Run(new ProcessLaunchRequest {
      ExecutablePath = monitorExe,
      Arguments = ProcessArguments.Of(MonitorSubcommand, MmOnlyFilterArg, exePath),
      EnvironmentOverrides = new Dictionary<string, string> { [MaxProcsEnvVar] = SingleWorkerProcCount },
      TimeoutMs = timeoutMs,
    });
  }

  /// <summary>
  /// Attach the monitor's stderr to an mm-trace failure message. Every mm-trace
  /// verdict goes through here, so no failure can report a symptom while the
  /// cause sits unread in a discarded stream.
  /// </summary>
  private static string WithMonitorStderr(string message, string monitorStderr) =>
    string.IsNullOrWhiteSpace(monitorStderr)
      ? message
      : $"{message}\nMonitor stderr:\n{monitorStderr.TrimEnd()}";

  /// <summary>
  /// Normalize raw `monitor --filter=mm` stdout into a stable, comparable
  /// trace. Steps: (1) keep only the monitor's `[+SSSS.mmm]`-prefixed trace
  /// lines, dropping the child's forwarded stdout; (2) strip that timestamp
  /// prefix and the depth indent, leaving `mm_&lt;verb&gt; ...`; (3) dense-renumber
  /// `#&lt;id&gt;` alloc ids to 1,2,3,… by first appearance so run-specific
  /// monotonic ids don't leak into goldens. Idempotent: re-normalizing an
  /// already-normalized trace is a no-op, so the same routine normalizes both
  /// captured output and the authored expected block.
  /// </summary>
  private static string NormalizeMmTrace(string rawStdout) {
    var idMap = new Dictionary<string, int>(StringComparer.Ordinal);
    var kept = new List<string>();

    foreach (var rawLine in rawStdout.Replace("\r\n", "\n").Split('\n')) {
      // A timestamped monitor line yields its payload from capture group 1
      // (the `[+SSSS.mmm]` prefix and depth indent stripped); an
      // already-normalized golden line is the bare trimmed text. Either way
      // keep ONLY genuine `mm_<verb>` events — the child's forwarded stdout
      // and any non-MM line are dropped — which also makes this idempotent.
      var match = MmTraceLineRegex().Match(rawLine);
      var payload = (match.Success ? match.Groups[1].Value : rawLine).Trim();
      if (!payload.StartsWith(MmTraceEventPrefix, StringComparison.Ordinal)) continue;

      payload = MmTraceAllocIdRegex().Replace(payload, m => {
        var raw = m.Groups[1].Value;
        if (!idMap.TryGetValue(raw, out var dense)) {
          dense = idMap.Count + 1;
          idMap[raw] = dense;
        }
        return $"#{dense}";
      });

      kept.Add(payload);
    }

    return string.Join("\n", kept).Trim();
  }

  private static string NormalizeIr(string ir) {
    // Trim each line, remove empty lines, normalize line endings
    var lines = ir.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
      .Select(l => l.Trim())
      .Where(l => l.Length > 0);
    return string.Join("\n", lines);
  }

  private static (bool Passed, string? Message) CheckRequiredIr(string required, string actual, Compiler.CompileTarget target) {
    // Parse both into sections (e.g., "=== maxon", "=== standard", "=== x86", "=== arm64")
    var requiredSections = ParseIrSections(required);
    var actualSections = ParseIrSections(actual);

    // The ARCH vocabulary decides, the STAGE vocabulary is produced, and they are two
    // vocabularies that happen to spell `arm64` the same way — as `x64` vs `x86` proves. Spelled
    // as bare literals, renaming a stage name would leave this line comparing the wrong one.
    var otherBackend = target.Arch == Compiler.CompileTarget.Arm64Arch
      ? Compiler.PipelineStages.X86
      : Compiler.PipelineStages.ARM64;

    // Compare only sections that are relevant: skip the other backend's section
    foreach (var (name, requiredContent) in requiredSections) {
      if (name == otherBackend) continue; // skip irrelevant backend section

      if (!actualSections.TryGetValue(name, out var actualContent)) {
        return (false, $"IR mismatch: missing section '=== {name}' in actual output.");
      }

      var requiredNorm = NormalizeIr(requiredContent);
      var actualNorm = NormalizeIr(actualContent);
      if (requiredNorm != actualNorm) {
        return (false, $"IR mismatch in section '=== {name}'.\nExpected:\n{requiredNorm}\n\nActual:\n{actualNorm}");
      }
    }

    // Also check that the actual output doesn't have extra sections we didn't expect
    // (but skip the other backend's section in actual too)
    foreach (var (name, _) in actualSections) {
      if (name == otherBackend) continue;
      if (!requiredSections.ContainsKey(name)) {
        return (false, $"IR mismatch: unexpected section '=== {name}' in actual output.");
      }
    }

    return (true, null);
  }

  /// <summary>
  /// Parse IR text into named sections split by "=== sectionName" headers.
  /// If no headers are found, the entire text is returned as a single unnamed section.
  /// </summary>
  private static Dictionary<string, string> ParseIrSections(string ir) {
    var split = Compiler.PipelineStages.Split(ir);

    // No markers at all — a RequiredIR block written before the dump grew stages, or a single-stage
    // fragment. One unnamed block, so the comparison above still has something to compare.
    if (split.Count == 0) return new Dictionary<string, string> { [""] = ir };

    var sections = new Dictionary<string, string>();
    foreach (var (name, body) in split) sections[name] = body;

    return sections;
  }

  /// <summary>
  /// Build a per-file map { tempPath -> (fragmentPath, lineOffset) } so
  /// multi-file error messages can be rewritten to point at the merged
  /// fragment file. The line offset is the line number in the merged
  /// source where each split file's content starts (1-based), matching
  /// how the merged fragment looks on disk.
  /// </summary>
  private static Dictionary<string, (string FragmentPath, int LineOffset)> ComputeSplitFileMap(Fragment fragment, string tempDir) {
    var map = new Dictionary<string, (string, int)>();
    if (fragment.SourceFiles == null) return map;
    // Re-derive offsets from the merged fragment source rather than from
    // the per-file slices, since the merged source preserves the
    // `// --- file:` marker lines and any blank lines between sections.
    var lines = fragment.Source.Replace("\r\n", "\n").Split('\n');
    var markerRegex = MyRegex();
    var fileFirstLine = new Dictionary<string, int>();
    for (int currentLine = 1; currentLine <= lines.Length; currentLine++) {
      var m = markerRegex.Match(lines[currentLine - 1]);
      if (m.Success) {
        var fileName = m.Groups[1].Value.Trim();
        // First content line of this section is the line AFTER the marker.
        if (!fileFirstLine.ContainsKey(fileName)) {
          fileFirstLine[fileName] = currentLine + 1;
        }
      }
    }
    // Use the fragment path relative to the project root so the
    // rewritten error matches the spec format ("specs/fragments/...").
    var projectRoot = Compiler.CompileError.ProjectRoot ?? Environment.CurrentDirectory;
    var relFragmentPath = Path.GetRelativePath(projectRoot, fragment.FilePath).Replace('\\', '/');
    foreach (var (FileName, _) in fragment.SourceFiles) {
      var path = Path.Combine(tempDir, FileName);
      // Per-file source as written to disk has no marker line, so its
      // line 1 corresponds to merged-source line `currentFileFirstLine`.
      // Subtract 1 because the rewrite adds the offset to the per-file
      // line number (which is 1-based).
      var offset = fileFirstLine.TryGetValue(FileName, out var ln) ? ln - 1 : 0;
      map[path] = (relFragmentPath, offset);
    }
    return map;
  }

  /// <summary>
  /// Rewrite per-file paths in an error string to the merged fragment
  /// path, with line numbers offset to match the merged source. Each
  /// path occurrence in the error is matched as `<path>:<line>:<col>:`
  /// and replaced with `<fragmentPath>:<line+offset>:<col>:`.
  /// </summary>
  private static string RewriteMultiFileErrorPaths(string error, Dictionary<string, (string FragmentPath, int LineOffset)> map) {
    foreach (var (tempPath, (fragmentPath, offset)) in map) {
      var normalizedTempPath = tempPath.Replace('\\', '/');
      var pattern = new Regex($"{Regex.Escape(normalizedTempPath)}:(\\d+):(\\d+)");
      error = pattern.Replace(error.Replace('\\', '/'), m => {
        var line = int.Parse(m.Groups[1].Value) + offset;
        return $"{fragmentPath}:{line}:{m.Groups[2].Value}";
      });
      // Also handle the bare-filename form (no temp prefix) in case
      // CompileError formatted relative-to-cwd or stripped temp path.
      var bareName = Path.GetFileName(tempPath);
      var barePattern = new Regex($"\\b{Regex.Escape(bareName)}:(\\d+):(\\d+)");
      error = barePattern.Replace(error, m => {
        var line = int.Parse(m.Groups[1].Value) + offset;
        return $"{fragmentPath}:{line}:{m.Groups[2].Value}";
      });
    }
    return error;
  }

  /// <summary>
  /// Normalize stderr for comparison: CRLF -> LF, trim, backslash -> forward slash in paths.
  /// </summary>
  private static string NormalizeStderr(string stderr) {
    var normalized = stderr.Replace("\r\n", "\n");
    normalized = normalized.Replace('\\', '/');
    // Normalize target-specific fragment directory to generic path for comparison
    // e.g., "specs/fragments-arm64-macos/" -> "specs/fragments/"
    normalized = FragmentDirRegex().Replace(normalized, "specs/fragments/");
    return normalized.Trim();
  }

  // Matches the worker-suffix appended by --async-trace-workers: " [M=N]" at end of line.
  private static readonly Regex AsyncWorkerSuffix = AsyncWorkerSuffixRegex();

  // Lines emitted by the worker lifecycle tracer that are timing-dependent and excluded from trace comparison.
  private static readonly HashSet<string> AsyncWorkerOnlyPrefixes = new(StringComparer.Ordinal) {
    "worker_start", "worker_park", "worker_wake", "worker_exit"
  };

  /// <summary>
  /// Normalize async trace stderr for comparison: strip non-deterministic worker lifecycle
  /// lines (worker_start/park/wake/exit) and [M=N] worker suffixes so tests are stable
  /// regardless of scheduling timing.
  /// </summary>
  /// Replace CWD with placeholder and unify path separators to native format.
  private string NormalizePathsForComparison(string s) {
    var cwd = Directory.GetCurrentDirectory();
    if (_target.Os == Compiler.CompileTarget.WindowsOs) {
      s = s.Replace(cwd.Replace('/', '\\'), "{CWD}");
      s = s.Replace('/', '\\');
    } else {
      s = s.Replace(cwd, "{CWD}");
      s = s.Replace('\\', '/');
    }
    return s;
  }

  private static string NormalizeAsyncTraceStderr(string stderr) {
    var lines = stderr.Replace("\r\n", "\n").Split('\n');
    var kept = new List<string>();
    foreach (var raw in lines) {
      var line = AsyncWorkerSuffix.Replace(raw.TrimEnd(), "");
      var prefix = line.Contains(' ') ? line[..line.IndexOf(' ')] : line;
      if (!AsyncWorkerOnlyPrefixes.Contains(prefix))
        kept.Add(line);
    }
    return string.Join('\n', kept).Trim();
  }

  // CPU-fault panics from __gt_fault_diagnostic carry a
  //   " at rip=0xHEX diag_base=0xHEX"
  // suffix identifying the faulting instruction and the ASLR slide of the
  // image. Both addresses are build- and run-specific, so strip the entire
  // suffix before comparing against spec expectations.
  private static string StripFaultRipSuffix(string stderr) {
    var lines = stderr.Replace("\r\n", "\n").Split('\n');
    for (int i = 0; i < lines.Length; i++) {
      var idx = lines[i].IndexOf(" at rip=0x", StringComparison.Ordinal);
      if (idx >= 0) lines[i] = lines[i][..idx];
    }
    return string.Join('\n', lines);
  }

  // ============================================================================
  // PE section content verification
  // ============================================================================

  /// The PE sections a spec case can pin, paired with the block that pins each — the ONE place the two
  /// pins are enumerated, so a third one is added by adding a row rather than by copying a twelve-line
  /// check that already existed twice. Absent blocks are dropped here, so the caller has nothing to
  /// skip.
  private static IEnumerable<(string SectionName, string Required)> RequiredSectionPins(
      SuccessExpectation expectation) {
    if (expectation.RequiredRdata is { } rdata) yield return (RdataSectionName, rdata);
    if (expectation.RequiredData is { } data) yield return (DataSectionName, data);
  }

  private const string RdataSectionName = ".rdata";
  private const string DataSectionName = ".data";

  private static (bool Passed, string? Message) CheckRequiredSection(string requiredContent, string exePath, string sectionName) {
    var expectedBytes = ParseTypedSectionValues(requiredContent);
    if (expectedBytes == null) {
      return (false, $"Failed to parse typed values for {sectionName}");
    }

    var sections = ParsePeSections(exePath);
    if (sections == null) {
      return (false, "Failed to parse PE sections");
    }

    var section = sections.FirstOrDefault(s => s.Name == sectionName);
    if (section == null) {
      return (false, $"PE does not contain {sectionName} section");
    }

    var actualBytes = ReadPeSectionData(exePath, section);
    if (actualBytes == null) {
      return (false, $"Failed to read {sectionName} section data");
    }

    // The data section may contain additional runtime globals (e.g., green thread
    // scheduler state) after the user data. Check that the user data forms a prefix.
    if (actualBytes.Length >= expectedBytes.Length &&
        actualBytes.AsSpan(0, expectedBytes.Length).SequenceEqual(expectedBytes)) {
      return (true, null);
    }

    return (false, $"{sectionName} mismatch.\nExpected ({expectedBytes.Length} bytes): {FormatHex(expectedBytes)}\nActual ({actualBytes.Length} bytes): {FormatHex(actualBytes)}");
  }

  private static string FormatHex(byte[] data) {
    var sb = new StringBuilder(data.Length * 3);
    for (int i = 0; i < data.Length; i++) {
      if (i > 0) sb.Append(' ');
      sb.Append(data[i].ToString("x2"));
    }
    return sb.ToString();
  }

  /// <summary>
  /// Parses typed value lines into a concatenated byte array.
  /// </summary>
  private static byte[]? ParseTypedSectionValues(string block) {
    var result = new List<byte>();
    var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    foreach (var rawLine in lines) {
      var line = rawLine.Trim();
      if (line.Length == 0) continue;

      var spaceIdx = line.IndexOf(' ');
      if (spaceIdx < 0) return null;

      var type = line[..spaceIdx];
      var value = line[(spaceIdx + 1)..].Trim();

      switch (type) {
        case "f64": {
          if (!double.TryParse(value, CultureInfo.InvariantCulture, out var d)) return null;
          result.AddRange(BitConverter.GetBytes(d));
          break;
        }
        case "f32": {
          if (!float.TryParse(value, CultureInfo.InvariantCulture, out var f)) return null;
          result.AddRange(BitConverter.GetBytes(f));
          break;
        }
        case "i8": {
          if (!sbyte.TryParse(value, out var sb)) return null;
          result.Add((byte)sb);
          break;
        }
        case "i16": {
          if (!short.TryParse(value, out var s)) return null;
          result.AddRange(BitConverter.GetBytes(s));
          break;
        }
        case "u16": {
          var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
          foreach (var part in parts) {
            if (!ushort.TryParse(part.Trim(), out var u)) return null;
            result.AddRange(BitConverter.GetBytes(u));
          }
          break;
        }
        case "i32": {
          if (!int.TryParse(value, out var n)) return null;
          result.AddRange(BitConverter.GetBytes(n));
          break;
        }
        case "i64": {
          if (!long.TryParse(value, out var l)) return null;
          result.AddRange(BitConverter.GetBytes(l));
          break;
        }
        case "i64[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!long.TryParse(part.Trim(), out var l)) return null;
            result.AddRange(BitConverter.GetBytes(l));
          }
          break;
        }
        case "i8[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!sbyte.TryParse(part.Trim(), out var sb)) return null;
            result.Add((byte)sb);
          }
          break;
        }
        case "u8[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!byte.TryParse(part.Trim(), out var b)) return null;
            result.Add(b);
          }
          break;
        }
        case "i16[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!short.TryParse(part.Trim(), out var s)) return null;
            result.AddRange(BitConverter.GetBytes(s));
          }
          break;
        }
        case "u16[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!ushort.TryParse(part.Trim(), out var u)) return null;
            result.AddRange(BitConverter.GetBytes(u));
          }
          break;
        }
        case "u32": {
          if (!uint.TryParse(value, out var u)) return null;
          result.AddRange(BitConverter.GetBytes(u));
          break;
        }
        case "u32[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!uint.TryParse(part.Trim(), out var u)) return null;
            result.AddRange(BitConverter.GetBytes(u));
          }
          break;
        }
        case "i32[]": {
          var parts = value.Split(',');
          foreach (var part in parts) {
            if (!int.TryParse(part.Trim(), out var n)) return null;
            result.AddRange(BitConverter.GetBytes(n));
          }
          break;
        }
        case "utf8": {
          if (value.Length < 2 || value[0] != '"' || value[^1] != '"') return null;
          try {
            var str = StringUtils.ResolveEscapes(value[1..^1]);
            result.AddRange(Encoding.UTF8.GetBytes(str));
          } catch (InvalidEscapeException) {
            return null;
          }
          break;
        }
        case "pad": {
          if (!int.TryParse(value, out var count) || count < 0) return null;
          for (var j = 0; j < count; j++) result.Add(0);
          break;
        }
        default:
          return null;
      }
    }

    return [.. result];
  }


  // ============================================================================
  // PE parsing helpers
  // ============================================================================

  private sealed record PeSectionInfo(string Name, uint VirtualSize, uint VirtualAddress, uint RawSize, uint RawOffset, uint Characteristics);

  private static List<PeSectionInfo>? ParsePeSections(string exePath) {
    try {
      using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
      using var reader = new BinaryReader(fs);

      var dosMagic = reader.ReadUInt16();
      if (dosMagic != 0x5A4D) return null;

      fs.Position = 0x3C;
      var peOffset = reader.ReadUInt32();

      fs.Position = peOffset;
      var peSignature = reader.ReadUInt32();
      if (peSignature != 0x00004550) return null;

      reader.ReadUInt16(); // Machine
      var numberOfSections = reader.ReadUInt16();
      reader.ReadUInt32(); // TimeDateStamp
      reader.ReadUInt32(); // PointerToSymbolTable
      reader.ReadUInt32(); // NumberOfSymbols
      var sizeOfOptionalHeader = reader.ReadUInt16();
      reader.ReadUInt16(); // Characteristics

      fs.Position += sizeOfOptionalHeader;

      var sections = new List<PeSectionInfo>();
      for (int i = 0; i < numberOfSections; i++) {
        var nameBytes = reader.ReadBytes(8);
        var name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
        var virtualSize = reader.ReadUInt32();
        var virtualAddress = reader.ReadUInt32();
        var rawSize = reader.ReadUInt32();
        var rawOffset = reader.ReadUInt32();
        reader.ReadUInt32(); // PointerToRelocations
        reader.ReadUInt32(); // PointerToLinenumbers
        reader.ReadUInt16(); // NumberOfRelocations
        reader.ReadUInt16(); // NumberOfLinenumbers
        var characteristics = reader.ReadUInt32();
        sections.Add(new PeSectionInfo(name, virtualSize, virtualAddress, rawSize, rawOffset, characteristics));
      }

      return sections;
    } catch {
      return null;
    }
  }

  private static byte[]? ReadPeSectionData(string exePath, PeSectionInfo section) {
    try {
      using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read);
      fs.Position = section.RawOffset;
      // Read VirtualSize bytes (actual data) rather than RawSize (file-aligned, padded with zeros)
      var readSize = Math.Min(section.VirtualSize, section.RawSize);
      var data = new byte[readSize];
      fs.ReadExactly(data, 0, (int)readSize);
      return data;
    } catch {
      return null;
    }
  }

  /// <summary>
  /// Update required blocks (RequiredIR, MmTrace stderr) in spec files with freshly generated output.
  /// Only updates the current target's RequiredIR block without disturbing other targets.
  /// </summary>
  private void UpdateRequiredInSpecFiles() {
    var targetKey = $"{_target.Arch}-{_target.Os}";
    // Parse with targetKey so success.RequiredIR contains the current target's block (or unqualified fallback)
    var scan = SpecParser.ParseDirectory(_specDir, targetKey, _includeNetwork);

    // ⚖ A MINT RUN OVER A CORPUS THAT DID NOT FULLY PARSE IS REFUSED, and it is refused HERE as well as
    // in `RunAllSpecTests` because this door opens FIRST — `--update-required` mints before the suite
    // runs, so a scan error reported only downstream would arrive after the goldens had been rewritten.
    // The spec whose parse failed is exactly the one whose blocks would go un-regenerated while every
    // other file's moved, which is a partial mint nobody asked for and nothing records.
    if (scan.Errors.Count > 0) {
      foreach (var error in scan.Errors) Logger.Error(LogCategory.Testing, error);
      throw new InvalidOperationException(
        $"Refusing to regenerate required blocks: {scan.Errors.Count} spec file(s) could not be read or "
        + "parsed (listed above). A mint over a corpus that did not fully parse writes some files and "
        + "silently skips others.");
    }

    var specs = scan.Specs;
    var updatedSpecs = 0;
    // Split-file directories for multi-file tests, removed once every spec has been rewritten.
    var tempSourceDirs = new List<string>();

    Directory.CreateDirectory(_tempDir);

    foreach (var spec in specs) {
      var specContent = File.ReadAllText(spec.FilePath);
      var updated = false;
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);

      foreach (var test in spec.Tests) {
        // Honor --filter: skip tests that don't match
        if (_filter != null) {
          var testPath = $"{specName}/{test.Name}";
          if (!testPath.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
        }

        var fragmentPath = Path.GetFullPath(PerTestFragmentPath(specName, test.Name));
        var sourceWithComment = $"// Test: {test.Name}\n{test.Source}";

        // Find the test marker once (shared by both RequiredIR and stderr updates)
        var markerPattern = $@"<!--\s*test:\s*{Regex.Escape(test.Name)}\s*-->";
        var markerMatch = Regex.Match(specContent, markerPattern);
        if (!markerMatch.Success) continue;

        // For tests that expect a compiler error, regenerate the
        // `maxoncstderr` block from the current compiler output.
        if (test.Expectation is CompilerErrorExpectation cerr) {
          try {
            SetCompileFlags();
            var (_, compileError) = CompileToExecutable(
              new Fragment {
                FilePath = fragmentPath,
                Source = sourceWithComment,
                TestName = test.Name,
                Args = test.Args,
                MmTrace = test.MmTrace,
                AsyncTrace = test.AsyncTrace,
                DebugInfo = test.DebugInfo,
                Expectation = cerr,
                SourceFiles = test.SourceFiles,
              },
              Path.Combine(_tempDir, $"{specName}_{test.Name}_cerr.exe"),
              _target);
            if (compileError != null) {
              var newStderr = NormalizeStderr(compileError);
              var oldStderr = NormalizeStderr(cerr.ExpectedStderr);
              if (oldStderr != newStderr) {
                var searchStart = markerMatch.Index + markerMatch.Length;
                var blockPattern = @"```maxoncstderr\s*\n(.*?)```";
                var candidate = Regex.Match(specContent[searchStart..], blockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));
                if (candidate.Success) {
                  var absoluteStart = searchStart + candidate.Index;
                  var absoluteEnd = absoluteStart + candidate.Length;
                  var replacement = $"```maxoncstderr\n{newStderr}\n```";
                  specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                  updated = true;
                  Logger.Debug(LogCategory.Testing, $"Updated maxoncstderr for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                }
              }
            }
          } catch (Exception ex) {
            Logger.Debug(LogCategory.Testing, $"Skipping maxoncstderr regeneration for '{test.Name}': {ex.Message}");
          }
          continue;
        }

        if (test.Expectation is not SuccessExpectation success) continue;

        // Built ONCE for this test — the RequiredIR, stderr and mm-trace regenerations below all
        // compile the same sources, and for a multi-file test that means one split-out temp
        // directory rather than three. Collected for deletion at the end of the method: the three
        // blocks run in sequence with no `continue` between them, so per-block cleanup would only
        // be re-deriving that ordering.
        var (sources, sourcesTempDir) = BuildTestSources(test.SourceFiles, fragmentPath, sourceWithComment);
        if (sourcesTempDir != null) tempSourceDirs.Add(sourcesTempDir);

        // Update RequiredIR for current target
        if (success.RequiredIR != null || HasAnyRequiredIRBlock(specContent, markerMatch)) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_ir.exe");
          try {
            SetCompileFlags();
            var irResult = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, target: _target);

            // ⚠ A FAILED REGENERATION IS REPORTED, NEVER SWALLOWED. This used to fall straight
            // through: the block kept its stale contents and the run said nothing, so the only
            // symptom was the same test failing again afterwards for a reason `--update-required`
            // had already discovered and discarded.
            if (!irResult.Success || irResult.AllStagesIr == null) {
              Logger.Error(LogCategory.Testing,
                $"--update-required: could not regenerate RequiredIR for '{specName}/{test.Name}' — " +
                $"it does not compile for {targetKey}, so the pinned block is unchanged and stale.");
            }

            if (irResult.Success && irResult.AllStagesIr != null) {
              var newRequiredIR = irResult.AllStagesIr.Trim();
              var searchStart = markerMatch.Index + markerMatch.Length;

              // Find the next test marker to bound our search
              var nextTestMatch = Regex.Match(specContent[searchStart..], @"<!--\s*(?:disabled-)?test:\s*\S+\s*-->", RegexOptions.None, TimeSpan.FromSeconds(5));
              var searchEnd = nextTestMatch.Success ? searchStart + nextTestMatch.Index : specContent.Length;

              // Try to find and update existing target-qualified block
              var qualifiedBlockPattern = $@"```RequiredIR:{Regex.Escape(targetKey)}\s*\n(.*?)```";
              var qualifiedMatch = Regex.Match(specContent[searchStart..searchEnd], qualifiedBlockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));

              if (qualifiedMatch.Success) {
                // Update existing target-qualified block
                var oldNorm = NormalizeIr(qualifiedMatch.Groups[1].Value.TrimEnd());
                var newNorm = NormalizeIr(newRequiredIR);
                if (oldNorm != newNorm) {
                  var absoluteStart = searchStart + qualifiedMatch.Index;
                  var absoluteEnd = absoluteStart + qualifiedMatch.Length;
                  var replacement = $"```RequiredIR:{targetKey}\n{newRequiredIR}\n```";
                  specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                  updated = true;
                  Logger.Debug(LogCategory.Testing, $"Updated RequiredIR:{targetKey} for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                }
              } else {
                // No target-qualified block exists — check for unqualified block to migrate or find insertion point
                var unqualifiedPattern = @"```RequiredIR\s*\n(.*?)```";
                var unqualifiedMatch = Regex.Match(specContent[searchStart..searchEnd], unqualifiedPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));

                if (unqualifiedMatch.Success) {
                  // Migrate: rename unqualified block to x64-windows (since all existing blocks contain x86 IR)
                  // and insert a new block for the current target if different
                  var absoluteStart = searchStart + unqualifiedMatch.Index;
                  var absoluteEnd = absoluteStart + unqualifiedMatch.Length;
                  var existingContent = unqualifiedMatch.Groups[1].Value.TrimEnd();

                  if (targetKey == "x64-windows") {
                    // Current target is x86 — just rename the block and update content
                    var replacement = $"```RequiredIR:x64-windows\n{newRequiredIR}\n```";
                    specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                  } else {
                    // Current target is different — rename existing to x64-windows and append new target block
                    var replacement = $"```RequiredIR:x64-windows\n{existingContent}\n```\n```RequiredIR:{targetKey}\n{newRequiredIR}\n```";
                    specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                  }
                  updated = true;
                  Logger.Debug(LogCategory.Testing, $"Migrated RequiredIR to target-qualified for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                } else {
                  // Find the last RequiredIR block for any target and insert after it
                  var anyBlockPattern = @"```RequiredIR:[^\s`]+\s*\n(.*?)```";
                  var lastMatch = Regex.Matches(specContent[searchStart..searchEnd], anyBlockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5))
                    .Cast<Match>().LastOrDefault();

                  if (lastMatch != null) {
                    var insertPos = searchStart + lastMatch.Index + lastMatch.Length;
                    var newBlock = $"\n```RequiredIR:{targetKey}\n{newRequiredIR}\n```";
                    specContent = string.Concat(specContent.AsSpan(0, insertPos), newBlock, specContent.AsSpan(insertPos));
                    updated = true;
                    Logger.Debug(LogCategory.Testing, $"Added RequiredIR:{targetKey} for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                  }
                }
              }
            }
          } finally {
            CompiledArtifact.Delete(exePath);
          }
        }

        // Update stderr (for AsyncTrace or plain panic/runtime stderr blocks).
        // mm-trace no longer routes through stderr — it has its own binary
        // capture branch below — so this compile never enables tracing.
        if (success.Stderr != null) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_stderr.exe");
          try {
            SetCompileFlags(asyncTrace: test.AsyncTrace);
            var result = new Compiler.Compiler().Compile(sources, exePath, target: _target);

            // A run that did not finish is not a golden. Regeneration writes what it observed
            // straight into the committed spec, so splicing a killed child's PREFIX in would commit
            // a truncated expectation that then passes forever — the one failure a regenerator must
            // never produce.
            var stderrRun = RunExecutable(exePath, _tempDir, test.Args, test.TimeoutMs);
            if (result.Success && WhyUncomparable(stderrRun, _target) is { } stderrIncomplete) {
              Logger.Error(LogCategory.Testing,
                $"Not updating stderr for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}: {stderrIncomplete.Message}");
            } else if (result.Success) {
              var actualStderr = stderrRun.Stderr;
              var normalize = test.AsyncTrace ? NormalizeAsyncTraceStderr : (Func<string, string>)(s => s.Replace("\r\n", "\n").Trim());
              var oldStderr = normalize(success.Stderr);
              var newStderr = normalize(StripFaultRipSuffix(actualStderr));
              if (oldStderr != newStderr) {
                // Re-find marker since specContent may have shifted from RequiredIR update
                var markerMatch2 = Regex.Match(specContent, markerPattern);
                if (markerMatch2.Success) {
                  var searchStart2 = markerMatch2.Index + markerMatch2.Length;
                  var blockPattern = @"```stderr\s*\n(.*?)```";
                  var candidate = Regex.Match(specContent[searchStart2..], blockPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5));
                  if (candidate.Success) {
                    var absoluteStart = searchStart2 + candidate.Index;
                    var absoluteEnd = absoluteStart + candidate.Length;
                    var stderrContent = test.AsyncTrace ? NormalizeAsyncTraceStderr(actualStderr) : actualStderr.Replace("\r\n", "\n").Trim();
                    var replacement = $"```stderr\n{stderrContent}\n```";
                    specContent = string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd));
                    updated = true;
                    Logger.Debug(LogCategory.Testing, $"Updated stderr for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                  }
                }
              }
            }
          } finally {
            CompiledArtifact.Delete(exePath);
          }
        }

        // Regenerate the ```mm-trace golden for mm-trace capture-mode tests:
        // compile with the binary DebugStream producer, capture + normalize the
        // `monitor --filter=mm` output, then splice the block into the spec.
        if (test.MmTrace) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_mmtrace.exe");
          try {
            SetCompileFlags(debugStream: true);
            var result = new Compiler.Compiler().Compile(sources, exePath, target: _target);

            // Refused for the reason the stderr regeneration above is: a prefix must never become a
            // committed expectation.
            var traceRun = CaptureMmTrace(exePath, test.TimeoutMs ?? DefaultTestTimeoutMs);
            if (result.Success && WhyUncomparable(traceRun, _target) is { } traceIncomplete) {
              Logger.Error(LogCategory.Testing,
                $"Not updating mm-trace for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}: {traceIncomplete.Message}");
            } else if (result.Success) {
              var monitorStdout = traceRun.Stdout;
              var newTrace = NormalizeMmTrace(monitorStdout);
              var oldTrace = NormalizeMmTrace(success.MmTraceExpected ?? "");
              if (oldTrace != newTrace || success.MmTraceExpected == null) {
                // Re-find marker since specContent may have shifted from the
                // RequiredIR/stderr splices above.
                var markerMatch2 = Regex.Match(specContent, markerPattern);
                if (markerMatch2.Success) {
                  var (splicedContent, spliced) = SpliceMmTraceBlock(specContent, markerMatch2, newTrace);
                  if (spliced) {
                    specContent = splicedContent;
                    updated = true;
                    Logger.Debug(LogCategory.Testing, $"Updated mm-trace for test '{test.Name}' in {Path.GetFileName(spec.FilePath)}");
                  }
                }
              }
            }
          } finally {
            CompiledArtifact.Delete(exePath);
          }
        }
      }

      if (updated) {
        File.WriteAllText(spec.FilePath, FragmentGenerator.NormalizeToLf(specContent));
        updatedSpecs++;
      }
    }

    foreach (var dir in tempSourceDirs) {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }

    if (updatedSpecs > 0) {
      Logger.Info(LogCategory.Testing, $"Updated required blocks in {updatedSpecs} spec file(s)");
    }
  }

  /// <summary>
  /// Splice a freshly-normalized ```mm-trace golden into the spec text for one
  /// test. Mirrors the RequiredIR/maxoncstderr splice pattern: bound the search
  /// to the test's slice (up to the next test marker); replace an existing
  /// ```mm-trace block if present, otherwise insert a new one after the last
  /// fenced result block in the slice. Returns the (possibly) rewritten content
  /// and whether a splice occurred.
  /// </summary>
  private static (string Content, bool Spliced) SpliceMmTraceBlock(string specContent, Match markerMatch, string newTrace) {
    var searchStart = markerMatch.Index + markerMatch.Length;
    var nextTestMatch = Regex.Match(specContent[searchStart..], @"<!--\s*(?:disabled-)?test:\s*\S+\s*-->", RegexOptions.None, TimeSpan.FromSeconds(5));
    var searchEnd = nextTestMatch.Success ? searchStart + nextTestMatch.Index : specContent.Length;
    var replacement = $"```mm-trace\n{newTrace}\n```";

    // Replace an existing block if the test already has one.
    var existing = Regex.Match(specContent[searchStart..searchEnd], @"```mm-trace\s*\n(.*?)```", RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    if (existing.Success) {
      var absoluteStart = searchStart + existing.Index;
      var absoluteEnd = absoluteStart + existing.Length;
      return (string.Concat(specContent.AsSpan(0, absoluteStart), replacement, specContent.AsSpan(absoluteEnd)), true);
    }

    // No existing block: insert after the last fenced result block in the slice.
    var lastFence = Regex.Matches(specContent[searchStart..searchEnd], @"```[^\n]*\n.*?```", RegexOptions.Singleline, TimeSpan.FromSeconds(5))
      .Cast<Match>().LastOrDefault();
    if (lastFence != null) {
      var insertPos = searchStart + lastFence.Index + lastFence.Length;
      var newBlock = $"\n\n{replacement}";
      return (string.Concat(specContent.AsSpan(0, insertPos), newBlock, specContent.AsSpan(insertPos)), true);
    }

    return (specContent, false);
  }

  /// <summary>
  /// Check if a test section has any RequiredIR block (qualified or unqualified).
  /// </summary>
  private static bool HasAnyRequiredIRBlock(string specContent, Match markerMatch) {
    var searchStart = markerMatch.Index + markerMatch.Length;
    var nextTestMatch = Regex.Match(specContent[searchStart..], @"<!--\s*(?:disabled-)?test:\s*\S+\s*-->", RegexOptions.None, TimeSpan.FromSeconds(5));
    var searchEnd = nextTestMatch.Success ? searchStart + nextTestMatch.Index : specContent.Length;
    var section = specContent[searchStart..searchEnd];
    return Regex.IsMatch(section, @"```RequiredIR[:\s]", RegexOptions.None, TimeSpan.FromSeconds(5));
  }

  [System.Text.RegularExpressions.GeneratedRegex(@"specs/fragments-[^/]+/")]
  private static partial System.Text.RegularExpressions.Regex FragmentDirRegex();
  [GeneratedRegex(@" \[M=\d+\]$", RegexOptions.Multiline)]
  private static partial Regex AsyncWorkerSuffixRegex();
  // A monitor trace line: `[+SSSS.mmm]` timestamp prefix, optional depth
  // indent (captured by \s*), then the `mm_<verb> ...` payload in group 1.
  [GeneratedRegex(@"^\[\+\d+\.\d+\]\s*(.*)$")]
  private static partial Regex MmTraceLineRegex();
  // An alloc-id token `#<digits>` for dense first-appearance renumbering.
  [GeneratedRegex(@"#(\d+)")]
  private static partial Regex MmTraceAllocIdRegex();
  [GeneratedRegex(@"^// --- file:\s*(.+)$")]
  private static partial Regex MyRegex();
}
