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
public partial class TestRunner(string specDir, string fragmentDir, string tempDir, string projectRoot, string? filter = null, int? workers = null, bool updateRequired = false, Compiler.CompileTarget? target = null, bool verbose = false, bool noBatch = false) {
  private readonly string _specDir = specDir;
  private readonly string _fragmentDir = fragmentDir;
  private readonly string _tempDir = tempDir;
  private readonly string _projectRoot = projectRoot;
  private readonly string? _filter = filter;
  // Default to ProcessorCount-2 (leave two cores for OS / IDE / background work).
  // Tests are I/O + compile bound, not CPU-saturating per worker, so under-using
  // the box at 50% wastes wall time. Override with --workers=N.
  private readonly int _workerCount = workers ?? Math.Max(1, Environment.ProcessorCount - 2);
  private readonly bool _updateRequired = updateRequired;
  private readonly Compiler.CompileTarget _target = target ?? Compiler.CompileTarget.Default;
  private readonly bool _verbose = verbose;
  private readonly bool _noBatch = noBatch;
  private static long _totalCompileMs;

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
    var prepResult = FragmentGenerator.PrepareWorkItems(_specDir, _fragmentDir, _filter, _target, _noBatch);

    // Abort on errors (e.g., duplicate test names)
    if (prepResult.Errors.Count > 0) {
      foreach (var error in prepResult.Errors) {
        Logger.Error(LogCategory.Testing, error);
      }
      return new TestSummary {
        Results = [],
        Passed = 0,
        Failed = 0,
        Total = 0,
        TotalDuration = sw.Elapsed,
        FragmentGenerationErrors = prepResult.Errors.Count
      };
    }

    var workItems = prepResult.WorkItems;
    if (workItems.Length == 0) {
      Logger.Info(LogCategory.Testing, "No tests found");
      return new TestSummary {
        Results = [],
        Passed = 0,
        Failed = 0,
        Total = 0,
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
    var generatedCount = 0;
    _totalCompileMs = 0;
    var generationErrors = new ConcurrentBag<string>();
    var printLock = new object();
    var compilationFailed = 0; // 1 = a compilation error occurred, stop all workers
    string? firstCompilationError = null;

    // Per-spec tracking for real-time progress (counts individual tests, not work items).
    var specTotal = new Dictionary<string, int>();
    var specDone = new Dictionary<string, int>();
    var specFailed = new Dictionary<string, List<string>>();
    foreach (var item in workItems) {
      var specName = WorkItemSpecName(item);
      specTotal.TryAdd(specName, 0);
      specTotal[specName] += WorkItemTestCount(item);
    }
    foreach (var name in specTotal.Keys) {
      specDone[name] = 0;
      specFailed[name] = [];
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
            AnyWorkItem.Single s => [ProcessWorkItem(s.Item, ref generatedCount, generationErrors)],
            AnyWorkItem.Batch b => ProcessSpecBatch(b.Item, ref generatedCount, generationErrors),
            _ => throw new InvalidOperationException("unknown work item kind"),
          };
          results[index] = itemResults;
          itemSw?.Stop();

          lock (printLock) {
            var specName = WorkItemSpecName(item);
            for (int ri = 0; ri < itemResults.Length; ri++) {
              var result = itemResults[ri];
              var testName = result.TestName;
              var testIdentifier = item is AnyWorkItem.Batch
                ? $"specs/fragments/{specName}/{FragmentGenerator.BatchFragmentBaseName(specName)}.test:{testName}"
                : $"specs/fragments/{specName}/{testName}.test";

              if (_verbose && itemSw != null) {
                var status = result.Passed ? "PASS" : "FAIL";
                Logger.Info(LogCategory.Testing, $"[W{workerId}] [{status}] {specName}/{testName} ({itemSw.ElapsedMilliseconds}ms)");
              }

              if (!result.Passed) {
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
                  if (f.StartsWith("  ")) {
                    if (_verbose) Logger.Error(LogCategory.Testing, f);
                  } else {
                    Logger.Error(LogCategory.Testing, f);
                  }
                }
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

    foreach (var error in generationErrors) {
      Logger.Error(LogCategory.Testing, error);
    }

    if (_totalCompileMs > 0) {
      Logger.Info(LogCategory.Testing, $"Total compile time: {_totalCompileMs}ms (across {_workerCount} workers)");
    }

    if (generatedCount > 0) {
      Logger.Info(LogCategory.Testing, $"Generated {generatedCount} fragment(s)");
    }

    CleanupExecutables(_tempDir);

    var resultList = results.Where(r => r != null).SelectMany(r => r).ToList();
    var passed = resultList.Count(r => r.Passed);
    var failed = resultList.Count(r => !r.Passed);

    return new TestSummary {
      Results = resultList,
      Passed = passed,
      Failed = failed,
      Total = resultList.Count,
      TotalDuration = sw.Elapsed,
      FragmentGenerationErrors = generationErrors.Count,
    };
  }

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

  /// <summary>
  /// Process a single work item: regenerate fragment → compile → run → check.
  /// </summary>
  private TestResult ProcessWorkItem(TestWorkItem item, ref int generatedCount, ConcurrentBag<string> generationErrors) {
    var testSw = Stopwatch.StartNew();

    try {
      // Step 1: Regenerate the fragment file. Always regenerated — no cache.
      Compiler.Compiler.MmTrace = false;
      Compiler.Compiler.AsyncTrace = false;
      Compiler.Compiler.Testing = true;
      var absolutePath = Path.GetFullPath(item.FragmentPath);
      var (content, genError) = FragmentGenerator.GenerateFragmentContent(item.Test, item.ExePath, absolutePath, _target);
      if (genError != null) {
        generationErrors.Add($"Error compiling 'specs/fragments/{item.SpecName}/{item.TestName}.test':\n{genError}");
        return new TestResult {
          TestName = item.TestName,
          Passed = false,
          ErrorMessage = $"Fragment generation failed: {genError}",
          Duration = testSw.Elapsed,
          FilePath = item.FragmentPath
        };
      }
      File.WriteAllText(item.FragmentPath, content.Replace("\r\n", "\n").Replace("\r", "\n"));
      Interlocked.Increment(ref generatedCount);

      // Step 2: Parse the fragment we just wrote.
      var fragment = FragmentGenerator.ParseFragment(item.FragmentPath);
      if (fragment == null) {
        return new TestResult {
          TestName = item.TestName,
          Passed = false,
          ErrorMessage = "Failed to parse fragment file",
          Duration = testSw.Elapsed,
          FilePath = item.FragmentPath
        };
      }

      // Step 3: Run the test (compile + execute + check expectations)
      return RunTest(fragment, item);
    } catch (Exception ex) {
      return new TestResult {
        TestName = item.TestName,
        Passed = false,
        ErrorMessage = $"Exception: {ex.Message}",
        Duration = testSw.Elapsed,
        FilePath = item.FragmentPath
      };
    }
  }

  /// <summary>
  /// Process a spec batch work item: regenerate the batch fragment file if
  /// stale, compile the batched source once if needed, then run the cached
  /// batch executable once per test that requires execution. Returns one
  /// TestResult per test in the batch (in the same order as item.Tests).
  ///
  /// On any unrecoverable failure (rewriter rejects all tests, batched compile
  /// fails), every test in the batch is marked failed via the batch's shared
  /// error path AND the runner records a fallback warning so a developer
  /// running with --no-batch can confirm it's a batching artifact.
  /// </summary>
  private TestResult[] ProcessSpecBatch(SpecBatchWorkItem item, ref int generatedCount, ConcurrentBag<string> generationErrors) {
    var results = new TestResult[item.Tests.Length];

    // Step 1: build the batched source. All rewritten fragments + the
    // dispatcher's `main` go into one file. The rewriter mangles every
    // top-level decl (functions, types, typealiases, enums, lets, vars,
    // and per-test `main`) so concatenating fragment bodies never collides.
    var (source, skipped, _) = FragmentGenerator.BuildBatchSource(item.SpecName, item.Tests);
    foreach (var s in skipped) {
      Logger.Debug(LogCategory.Testing, $"[BATCH SKIP] {s}");
    }
    if (source == null) {
      return FailBatch(item, "rewriter rejected all tests");
    }

    Compiler.Compiler.MmTrace = false;
    Compiler.Compiler.AsyncTrace = false;
    Compiler.Compiler.Testing = true;

    string? batchedIr;
    var compileSw = Stopwatch.StartNew();
    Directory.CreateDirectory(Path.GetDirectoryName(item.BatchExePath)!);
    try {
      var virtualPath = Path.Combine(_fragmentDir, item.SpecName, $"{FragmentGenerator.BatchFragmentBaseName(item.SpecName)}.maxon");
      var compilerSources = new[] { new Compiler.SourceFile(virtualPath, source) };
      var result = new Compiler.Compiler().Compile(compilerSources, item.BatchExePath, returnIr: true, target: _target);
      compileSw.Stop();
      Interlocked.Add(ref _totalCompileMs, compileSw.ElapsedMilliseconds);

      if (!result.Success) {
        var compileError = string.Join("\n", result.Errors.Select(e => e.Format()));
        return FailBatch(item, $"batch compile failed: {compileError}");
      }
      batchedIr = result.ArchIr;
    } catch (Exception ex) {
      compileSw.Stop();
      Interlocked.Add(ref _totalCompileMs, compileSw.ElapsedMilliseconds);
      return FailBatch(item, $"batch compile threw: {ex.Message}");
    }

    // Persist the batch fragment file with the compiled IR snapshot.
    var content = FragmentGenerator.GenerateBatchContent(item.SpecName, item.Tests, batchedIr);
    File.WriteAllText(item.BatchFragmentPath, content.Replace("\r\n", "\n").Replace("\r", "\n"));
    Interlocked.Increment(ref generatedCount);

    // Step 2: Run the batched binary ONCE. The dispatcher runs every
    // included test sequentially and emits framing markers around each;
    // we slice the output to recover per-test stdout and exit code. Tests
    // the rewriter rejected (skipped at build time) are NOT in the binary
    // and run via the per-fragment path instead.
    var batchSw = Stopwatch.StartNew();
    var (batchExitCode, batchStdout, batchStderr) = RunExecutable(item.BatchExePath, _tempDir, args: null);
    batchSw.Stop();

    // Parse the markers out of stdout. Any test missing its END marker is
    // reported as "did not run" (likely a panic in an earlier test killed
    // the process).
    var perTest = ParseBatchOutput(batchStdout);

    for (int i = 0; i < item.Tests.Length; i++) {
      var test = item.Tests[i];
      var rewrite = BatchRewriter.Rewrite(test.Name, test.Source);
      if (!rewrite.Batchable) {
        results[i] = RunOneAsSingle(item.SpecName, test, item.SpecFile, ref generatedCount, generationErrors);
        continue;
      }
      results[i] = CheckBatchedTestResult(item, test, perTest, batchStderr, batchExitCode, batchSw.Elapsed);
    }

    return results;
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
      var testName = stdout.Substring(nameStart, nameEnd - nameStart);

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
      var exitStr = stdout.Substring(exitStart, exitEnd - exitStart);
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
      var testStdout = stdout.Substring(stdoutStart, endIdx - stdoutStart);
      // Trim the trailing \n that came right before the END marker.
      if (testStdout.EndsWith('\n')) testStdout = testStdout[..^1];

      results[testName] = new BatchedTestResult(testStdout, ec);
      pos = exitEnd + suffix.Length;
    }

    return results;
  }

  /// <summary>
  /// Compare one batched test's parsed result against its expectation.
  /// </summary>
  private TestResult CheckBatchedTestResult(SpecBatchWorkItem item, TestCase test, Dictionary<string, BatchedTestResult> parsed, string batchStderr, int batchExitCode, TimeSpan elapsed) {
    if (!parsed.TryGetValue(test.Name, out var slice)) {
      // No END marker for this test. Either the dispatcher didn't reach it
      // (an earlier test crashed), or the binary itself failed before any
      // tests ran. Distinguish the two by whether ANY test produced output.
      var reason = parsed.Count == 0
        ? $"batched binary produced no test output (exit code {batchExitCode}, stderr: {batchStderr.Trim()})"
        : "did not run — likely an earlier test in the batch crashed or panicked";
      return new TestResult {
        TestName = test.Name,
        Passed = false,
        ErrorMessage = reason,
        Duration = elapsed,
        FilePath = item.BatchFragmentPath,
      };
    }

    var success = (SuccessExpectation)test.Expectation;

    if (success.ExitCode.HasValue) {
      var expectedCode = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? success.ExitCode.Value
        : success.ExitCode.Value & 0xFF;
      if (slice.ExitCode != expectedCode) {
        return new TestResult {
          TestName = test.Name,
          Passed = false,
          ErrorMessage = $"Expected exit code {expectedCode}, got {slice.ExitCode}",
          Duration = elapsed,
          FilePath = item.BatchFragmentPath,
        };
      }
    }

    if (success.Stdout != null) {
      var expectedStdout = NormalizePathsForComparison(success.Stdout.Replace("\r\n", "\n").Trim());
      var actualStdout = NormalizePathsForComparison(slice.Stdout.Replace("\r\n", "\n").Trim());
      if (expectedStdout != actualStdout) {
        return new TestResult {
          TestName = test.Name,
          Passed = false,
          ErrorMessage = $"Stdout mismatch:\nExpected: {expectedStdout}\nActual: {actualStdout}",
          Duration = elapsed,
          FilePath = item.BatchFragmentPath,
        };
      }
    }

    // Note: stderr is shared across the whole batch (it's the parent process's
    // stderr stream), so we can't attribute stderr to individual tests here.
    // Tests with `Stderr:` expectations are excluded from batching by the
    // eligibility filter.

    return new TestResult {
      TestName = test.Name,
      Passed = true,
      Duration = elapsed,
      FilePath = item.BatchFragmentPath,
    };
  }

  /// <summary>
  /// Run a single test (originally part of a spec batch) through the
  /// per-fragment compilation path. Used for tests the rewriter rejects.
  /// </summary>
  private TestResult RunOneAsSingle(string specName, TestCase test, FileInfo specFile, ref int generatedCount, ConcurrentBag<string> generationErrors) {
    var fragmentPath = Path.Combine(_fragmentDir, specName, $"{test.Name}.test");
    var irExePath = Path.Combine(_fragmentDir, specName, $"{test.Name}.ir_exe");
    var single = new TestWorkItem(fragmentPath, irExePath, specName, test.Name, test, specFile);
    return ProcessWorkItem(single, ref generatedCount, generationErrors);
  }

  /// <summary>
  /// Fail every test in the batch with the batch's compile error. We deliberately
  /// do NOT silently re-run as singles — a batched-compile failure means either
  /// the rewriter mishandled some construct (bug to fix in BatchRewriter) or a
  /// spec test relies on a pattern that doesn't survive batching (spec to
  /// adjust). Either way, the developer needs to see the failure, not have it
  /// papered over by an automatic fallback.
  /// </summary>
  private static TestResult[] FailBatch(SpecBatchWorkItem item, string reason) {
    Logger.Error(LogCategory.Testing, $"[BATCH FAIL] {item.SpecName}: {reason}");
    var results = new TestResult[item.Tests.Length];
    var msg = $"batch compile failed for spec '{item.SpecName}': {reason}. "
        + "Either fix BatchRewriter to handle this construct, or adjust the spec test "
        + "to avoid the pattern that triggered the failure. Run with --no-batch to "
        + "confirm the test itself is correct.";
    for (int i = 0; i < item.Tests.Length; i++) {
      var test = item.Tests[i];
      results[i] = new TestResult {
        TestName = test.Name,
        Passed = false,
        ErrorMessage = msg,
        Duration = TimeSpan.Zero,
        FilePath = item.BatchFragmentPath,
      };
    }
    return results;
  }

  /// <summary>
  /// Recursively clean up compiled executables from the fragment directory and its subdirectories.
  /// On Windows, these are .exe files. On macOS/Linux, these are extensionless files
  /// whose name matches a .test file in the same directory.
  /// </summary>
  private static void CleanupExecutables(string directory) {
    if (!Directory.Exists(directory)) return;

    try {
      // Delete .exe and .ir_exe files in this directory
      foreach (var exeFile in Directory.GetFiles(directory, "*.exe")) {
        try {
          File.Delete(exeFile);
        } catch {
          // Ignore deletion errors (file may be locked)
        }
      }
      foreach (var irExeFile in Directory.GetFiles(directory, "*.ir_exe")) {
        try {
          File.Delete(irExeFile);
        } catch {
          // Ignore deletion errors (file may be locked)
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

  private string ExeExtension => _target.Os == "windows" ? ".exe" : "";

  private TestResult RunTest(Fragment fragment, TestWorkItem item) {
    var sw = Stopwatch.StartNew();

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
        if (File.Exists(tempExe)) {
          try { File.Delete(tempExe); } catch { /* ignore */ }
        }

        var compiledSuccessfully = compileError == null;
        if (compiledSuccessfully) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = "Expected compiler error but compilation succeeded",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }

        // Normalize and compare stderr exactly
        var expectedNorm = NormalizeStderr(errorExpectation.ExpectedStderr);
        var actualNorm = NormalizeStderr(compileError!);
        if (expectedNorm != actualNorm) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = $"Stderr mismatch.\nExpected:\n  {expectedNorm}\nActual:\n  {actualNorm}",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }

        return new TestResult {
          TestName = fragment.TestName,
          Passed = true,
          Duration = sw.Elapsed,
          FilePath = fragment.FilePath
        };
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
        return new TestResult {
          TestName = fragment.TestName,
          Passed = false,
          ErrorMessage = $"Compilation failed: {compileError}",
          Duration = sw.Elapsed,
          FilePath = fragment.FilePath
        };
      }

      var successExpectation = (SuccessExpectation)fragment.Expectation;

      // Check Required IR by compiling fresh with all pipeline stages.
      // Use a dedicated temp exe so we never overwrite the cached exe.
      if (successExpectation.RequiredIR != null) {
        Compiler.SourceFile[] irSources;
        string? irTempDir = null;
        if (fragment.SourceFiles != null) {
          irTempDir = Path.Combine(Path.GetTempPath(), $"maxon-ir-{Guid.NewGuid():N}");
          Directory.CreateDirectory(irTempDir);
          irSources = [.. fragment.SourceFiles.Select(f => {
            var path = Path.Combine(irTempDir, f.FileName);
            File.WriteAllText(path, f.Source);
            return new Compiler.SourceFile(path, f.Source);
          })];
        } else {
          irSources = [new Compiler.SourceFile(fragment.FilePath, fragment.Source)];
        }
        var irExePath = Path.Combine(_tempDir, $"{fragment.TestName}_{Guid.NewGuid():N}_ir{ExeExtension}");
        Compiler.Compiler.MmTrace = false;
        Compiler.Compiler.MmDebug = false;
        Compiler.Compiler.AsyncTrace = false;
        Compiler.Compiler.Testing = true;
        var irResult = new Compiler.Compiler().Compile(irSources, irExePath, returnIr: true, target: _target);
        if (irTempDir != null) {
          try { Directory.Delete(irTempDir, recursive: true); } catch { }
        }
        try { if (File.Exists(irExePath)) File.Delete(irExePath); } catch { }
        if (!irResult.Success || irResult.AllStagesIr == null) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = "RequiredIR specified but compilation failed or produced no IR",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }

        var (Passed, Message) = CheckRequiredIr(successExpectation.RequiredIR, irResult.AllStagesIr, _target);
        if (!Passed) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = $"Required IR mismatch: {Message}",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }
      }

      // Check Required Rdata (PE-only)
      if (successExpectation.RequiredRdata != null && _target.Os == "windows") {
        var (rdataPassed, rdataMessage) = CheckRequiredRdata(successExpectation.RequiredRdata, exePath);
        if (!rdataPassed) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = $"Required Rdata mismatch: {rdataMessage}",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }
      }

      // Check Required Data (.data section, PE-only)
      if (successExpectation.RequiredData != null && _target.Os == "windows") {
        var (dataPassed, dataMessage) = CheckRequiredData(successExpectation.RequiredData, exePath);
        if (!dataPassed) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = $"Required Data mismatch: {dataMessage}",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }
      }

      // Run the executable if we have runtime expectations
      if (successExpectation.ExitCode.HasValue || successExpectation.Stdout != null || successExpectation.Stderr != null) {
        var (ExitCode, Stdout, Stderr) = RunExecutable(exePath, _tempDir, fragment.Args);

        if (successExpectation.ExitCode.HasValue) {
          // On macOS/Linux, process exit codes are masked to 8 bits (0-255)
          var expectedCode = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? successExpectation.ExitCode.Value
            : successExpectation.ExitCode.Value & 0xFF;
          if (ExitCode != expectedCode) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Expected exit code {expectedCode}, got {ExitCode}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }
        }

        if (successExpectation.Stdout != null) {
          var expectedStdout = successExpectation.Stdout.Replace("\r\n", "\n").Trim();
          var actualStdout = Stdout.Replace("\r\n", "\n").Trim();
          // Normalize machine-specific paths so tests are portable across OSes
          actualStdout = NormalizePathsForComparison(actualStdout);
          expectedStdout = NormalizePathsForComparison(expectedStdout);
          if (expectedStdout != actualStdout) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Stdout mismatch:\nExpected: {expectedStdout}\nActual: {actualStdout}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }
        }

        if (successExpectation.Stderr != null) {
          var normalize = fragment.AsyncTrace ? NormalizeAsyncTraceStderr : (Func<string, string>)(s => s.Replace("\r\n", "\n").Trim());
          var expectedStderr = normalize(successExpectation.Stderr);
          var actualStderr = normalize(Stderr);
          if (expectedStderr != actualStderr) {
            return new TestResult {
              TestName = fragment.TestName,
              Passed = false,
              ErrorMessage = $"Stderr mismatch:\nExpected: {expectedStderr}\nActual: {actualStderr}",
              Duration = sw.Elapsed,
              FilePath = fragment.FilePath
            };
          }
        } else if (!string.IsNullOrWhiteSpace(Stderr)) {
          return new TestResult {
            TestName = fragment.TestName,
            Passed = false,
            ErrorMessage = $"Unexpected stderr output:\n{Stderr.Trim()}",
            Duration = sw.Elapsed,
            FilePath = fragment.FilePath
          };
        }
      }

      return new TestResult {
        TestName = fragment.TestName,
        Passed = true,
        Duration = sw.Elapsed,
        FilePath = fragment.FilePath
      };
    } catch (Exception ex) {
      return new TestResult {
        TestName = fragment.TestName,
        Passed = false,
        ErrorMessage = $"Exception: {ex.Message}",
        Duration = sw.Elapsed,
        FilePath = fragment.FilePath
      };
    }
  }

  private static (bool Success, string? Error) CompileToExecutable(Fragment fragment, string outputPath, Compiler.CompileTarget? target = null) {
    try {
      Compiler.SourceFile[] sources;
      string? tempDir = null;

      if (fragment.SourceFiles != null) {
        tempDir = Path.Combine(Path.GetTempPath(), $"maxon-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        sources = [.. fragment.SourceFiles.Select(f => {
          var path = Path.Combine(tempDir, f.FileName);
          File.WriteAllText(path, f.Source);
          return new Compiler.SourceFile(path, f.Source);
        })];
      } else {
        sources = [new Compiler.SourceFile(fragment.FilePath, fragment.Source)];
      }

      try {
        Compiler.Compiler.MmTrace = fragment.MmTrace;
        Compiler.Compiler.AsyncTrace = fragment.AsyncTrace;
        Compiler.Compiler.Testing = true;
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

  private const int TestTimeoutMs = 5000;

  private static (int ExitCode, string Stdout, string Stderr) RunExecutable(string exePath, string workingDirectory, string? args = null) {
    // Code signing and executable permissions are now handled by MachOWriter at compile time

    var psi = new ProcessStartInfo {
      FileName = exePath,
      Arguments = args ?? "",
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true,
      StandardOutputEncoding = Encoding.UTF8,
      StandardErrorEncoding = Encoding.UTF8
    };

    // Use a job object to ensure child processes are killed on timeout (Windows only, no-op elsewhere)
    using var job = new WindowsJobObject();
    using var process = Process.Start(psi)!;

    // Assign process to job object for guaranteed cleanup
    job.AssignProcess(process.Handle);

    // Read stdout/stderr asynchronously to avoid deadlocks
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();

    bool exited = process.WaitForExit(TestTimeoutMs);
    if (!exited) {
      // Process timed out - kill it and drain streams
      try { process.Kill(entireProcessTree: true); } catch { }
      try { process.Kill(); } catch { }
      // Wait briefly for async reads to complete after kill, then abandon
#pragma warning disable VSTHRD002
      Task.WaitAll([stdoutTask, stderrTask], 1000);
#pragma warning restore VSTHRD002
      return (-1, "", "Process timed out");
    }

    // Process exited normally - wait for async reads to complete with a timeout
    // to guard against edge cases where streams aren't fully drained
#pragma warning disable VSTHRD002
    if (!Task.WaitAll([stdoutTask, stderrTask], 3000))
      return (process.ExitCode, "", "Stream read timed out");
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    return (process.ExitCode, stdout, stderr);
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

    var otherBackend = target.Arch == "arm64" ? "x86" : "arm64";

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
    var sections = new Dictionary<string, string>();
    var lines = ir.Split(['\r', '\n']);
    string? currentSection = null;
    var currentLines = new List<string>();

    foreach (var line in lines) {
      var trimmed = line.Trim();
      if (trimmed.StartsWith("=== ")) {
        if (currentSection != null) {
          sections[currentSection] = string.Join("\n", currentLines);
        }
        currentSection = trimmed[4..].Trim();
        currentLines.Clear();
      } else {
        currentLines.Add(line);
      }
    }

    if (currentSection != null) {
      sections[currentSection] = string.Join("\n", currentLines);
    } else {
      // No sections found — treat entire text as one block
      sections[""] = ir;
    }

    return sections;
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
    if (_target.Os == "windows") {
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

  // ============================================================================
  // PE section content verification
  // ============================================================================

  private static (bool Passed, string? Message) CheckRequiredRdata(string requiredRdata, string exePath) =>
    CheckRequiredSection(requiredRdata, exePath, ".rdata");

  private static (bool Passed, string? Message) CheckRequiredData(string requiredData, string exePath) =>
    CheckRequiredSection(requiredData, exePath, ".data");

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
    var specs = SpecParser.ParseDirectory(_specDir, targetKey);
    var updatedSpecs = 0;

    Directory.CreateDirectory(_tempDir);

    foreach (var spec in specs) {
      var specContent = File.ReadAllText(spec.FilePath);
      var updated = false;
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);

      foreach (var test in spec.Tests) {
        if (test.Expectation is not SuccessExpectation success) continue;

        // Honor --filter: skip tests that don't match
        if (_filter != null) {
          var testPath = $"{specName}/{test.Name}";
          if (!testPath.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
        }

        var fragmentPath = Path.GetFullPath(Path.Combine(_fragmentDir, specName, $"{test.Name}.test"));
        var sourceWithComment = $"// Test: {test.Name}\n{test.Source}";
        var sources = new[] { new Compiler.SourceFile(fragmentPath, sourceWithComment) };

        // Find the test marker once (shared by both RequiredIR and stderr updates)
        var markerPattern = $@"<!--\s*test:\s*{Regex.Escape(test.Name)}\s*-->";
        var markerMatch = Regex.Match(specContent, markerPattern);
        if (!markerMatch.Success) continue;

        // Update RequiredIR for current target
        if (success.RequiredIR != null || HasAnyRequiredIRBlock(specContent, markerMatch)) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_ir.exe");
          try {
            Compiler.Compiler.MmTrace = false;
            Compiler.Compiler.MmDebug = false;
            Compiler.Compiler.AsyncTrace = false;
            Compiler.Compiler.Testing = true;
            var irResult = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, target: _target);

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
            try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
          }
        }

        // Update stderr (for MmTrace, AsyncTrace, or plain stderr blocks)
        if (success.Stderr != null) {
          var exePath = Path.Combine(_tempDir, $"{specName}_{test.Name}_stderr.exe");
          try {
            Compiler.Compiler.MmTrace = test.MmTrace;
            Compiler.Compiler.AsyncTrace = test.AsyncTrace;
            Compiler.Compiler.MmDebug = false;
            Compiler.Compiler.Testing = true;
            var result = new Compiler.Compiler().Compile(sources, exePath, target: _target);

            if (result.Success) {
              var (_, _, actualStderr) = RunExecutable(exePath, _tempDir, test.Args);
              var normalize = test.AsyncTrace ? NormalizeAsyncTraceStderr : (Func<string, string>)(s => s.Replace("\r\n", "\n").Trim());
              var oldStderr = normalize(success.Stderr);
              var newStderr = normalize(actualStderr);
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
            try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
          }
        }
      }

      if (updated) {
        File.WriteAllText(spec.FilePath, specContent.Replace("\r\n", "\n"));
        updatedSpecs++;
      }
    }

    if (updatedSpecs > 0) {
      Logger.Info(LogCategory.Testing, $"Updated required blocks in {updatedSpecs} spec file(s)");
    }
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
}
