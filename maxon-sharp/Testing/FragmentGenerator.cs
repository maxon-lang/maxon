using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MaxonSharp.Compiler.Ir.Core;

namespace MaxonSharp.Testing;

/// <summary>
/// Generates .test fragment files from spec files.
/// </summary>
public static partial class FragmentGenerator {

  /// <summary>
  /// Directory under fragmentDir where compiled per-test executables and
  /// per-spec batch executables live. Kept around between runs so the
  /// run-the-binary step has a stable path to invoke; we do NOT skip
  /// recompilation based on what's there (no caching).
  /// </summary>
  public static string GetSpecCacheDir(string fragmentDir) => Path.Combine(fragmentDir, ".spec-cache");

  /// <summary>
  /// Reserved suffix for the generated batch fragment filename. The full base
  /// name is "<spec>_batch", so a spec named "arithmetic" produces
  /// "arithmetic/arithmetic_batch.test".
  /// </summary>
  public const string BatchFragmentSuffix = "_batch";

  /// <summary>
  /// Compute the per-spec batch fragment base name (without `.test` extension).
  /// </summary>
  public static string BatchFragmentBaseName(string specName) => $"{specName}{BatchFragmentSuffix}";

  /// <summary>
  /// Returns true if the test is eligible for batched compilation. Tests that
  /// fail eligibility stay on the per-fragment path (compile + run individually).
  /// </summary>
  public static bool IsBatchable(string specName, TestCase test) {
    // Reserved name guard: an authored test literally named "<spec>_batch"
    // would collide with the generated batch artifact.
    if (test.Name == BatchFragmentBaseName(specName)) return false;

    // CompilerError tests need their own compilation unit to capture stderr.
    if (test.Expectation is not SuccessExpectation success) return false;

    // RequiredIR/RequiredRdata/RequiredData are per-binary properties that
    // can't be sliced from a batched binary.
    if (success.RequiredIR != null) return false;
    if (success.RequiredRdata != null) return false;
    if (success.RequiredData != null) return false;

    // Trace tests count allocations / async events; batched runs share global
    // counters whose ordering would drift across tests in the same process.
    if (test.MmTrace || success.MmTrace) return false;
    if (test.AsyncTrace || success.AsyncTrace) return false;

    // Tests that assert runtime stderr (panics, stack traces, runtime errors)
    // are sensitive to: the source file's virtual name, the test's `main`
    // symbol name, and the dispatcher frame appearing in the stack trace.
    // All three change under batching, so panic-message comparisons become
    // brittle. Defer to per-fragment compilation for these.
    if (success.Stderr != null) return false;

    // Tests with their own argv have no way to receive them: the dispatcher
    // runs every test sequentially in a single process and doesn't forward
    // per-test args.
    if (test.Args != null) return false;

    // Tests that call CommandLine.args() observe whatever argv the batched
    // binary was invoked with (just the exe path) — fine in principle, but
    // count- or content-sensitive argv assertions are written against the
    // per-test single-binary shape, so keep them on the per-fragment path.
    if (test.Source.Contains("CommandLine.args(")) return false;

    // Multi-file tests already use multi-file compilation; mixing them with
    // batching is combinatorially complex — defer.
    if (test.SourceFiles != null) return false;

    return true;
  }

  /// <summary>
  /// Prepare work items from spec files. Parses specs, creates directories, checks
  /// staleness against the .spec-cache, and returns work items for the unified test pipeline.
  /// Does NOT compile anything — compilation happens in worker threads.
  /// </summary>
  public static PrepareResult PrepareWorkItems(string specDir, string fragmentDir, string? filter = null, Compiler.CompileTarget? target = null, bool noBatch = false) {
    var errors = new List<string>();

    if (!Directory.Exists(specDir)) {
      errors.Add($"Spec directory not found: {specDir}");
      return new PrepareResult([], 0, errors);
    }

    Directory.CreateDirectory(fragmentDir);

    var targetKey = target != null ? $"{target.Arch}-{target.Os}" : null;
    var specs = SpecParser.ParseDirectory(specDir, targetKey);
    var totalTests = specs.Sum(s => s.Tests.Count);

    // Directory under fragmentDir where compiled exes go. Persisted on disk
    // because the run-the-binary step needs a stable path to invoke; not used
    // for skipping recompilation.
    var specCacheDir = GetSpecCacheDir(fragmentDir);

    // Check for duplicate test names within specs and reject the reserved
    // batch name (`<spec>_batch`) if any spec accidentally uses it.
    foreach (var spec in specs) {
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      var reservedName = BatchFragmentBaseName(specName);
      var dupes = spec.Tests.GroupBy(t => t.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
      foreach (var dupe in dupes) {
        errors.Add($"Duplicate test name '{dupe}' in {spec.FilePath}");
      }
      foreach (var t in spec.Tests) {
        if (t.Name == reservedName) {
          errors.Add($"Test name '{reservedName}' is reserved (used by the batched-compilation system) in {spec.FilePath}");
        }
      }
    }
    if (errors.Count > 0) {
      return new PrepareResult([], totalTests, errors);
    }

    // Pre-create all spec fragment directories (and cache subdirectories)
    foreach (var spec in specs) {
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      Directory.CreateDirectory(Path.Combine(fragmentDir, specName));
      Directory.CreateDirectory(Path.Combine(specCacheDir, specName));
    }

    // Determine exe extension for cached executables
    var exeExt = target?.Os == "windows" || (target == null && RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? ".exe" : "";

    // Build work items with staleness info. Batched tests collapse into one
    // SpecBatchWorkItem per spec; everything else stays on the per-fragment
    // path (TestWorkItem inside an AnyWorkItem.Single).
    var workItems = new List<AnyWorkItem>();
    var allFragmentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var spec in specs) {
      var specFile = new FileInfo(spec.FilePath);
      var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
      var specFragmentDir = Path.Combine(fragmentDir, specName);

      // Apply --filter at the test level. We then partition the surviving tests
      // into batchable / non-batchable.
      var filteredTests = spec.Tests
        .Where(t => filter == null || $"{specName}/{t.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
        .ToList();
      if (filteredTests.Count == 0) continue;

      var batchable = noBatch ? [] : filteredTests.Where(t => IsBatchable(specName, t)).ToList();
      var nonBatchable = noBatch ? filteredTests : [.. filteredTests.Where(t => !IsBatchable(specName, t))];

      // Build per-fragment work items for the non-batchable tests.
      foreach (var test in nonBatchable) {
        var fragmentPath = Path.Combine(specFragmentDir, $"{test.Name}.test");
        var irExePath = Path.Combine(specFragmentDir, $"{test.Name}.ir_exe");
        allFragmentPaths.Add(Path.GetFullPath(fragmentPath));

        workItems.Add(new AnyWorkItem.Single(new TestWorkItem(fragmentPath, irExePath, specName, test.Name, test, specFile)));
      }

      // Build a single SpecBatchWorkItem for the batchable tests, if any.
      // The shared exe goes under .spec-cache/<spec>/. Each batchable test
      // gets its own per-test fragment written after the batched compile
      // (with IR sliced from the batched output for inspection); register
      // those paths so orphan cleanup doesn't sweep them.
      if (batchable.Count > 0) {
        var batchBaseName = BatchFragmentBaseName(specName);
        var batchExePath = Path.Combine(specCacheDir, specName, $"{batchBaseName}{exeExt}");

        foreach (var test in batchable) {
          var fragmentPath = Path.Combine(specFragmentDir, $"{test.Name}.test");
          allFragmentPaths.Add(Path.GetFullPath(fragmentPath));
        }

        workItems.Add(new AnyWorkItem.Batch(new SpecBatchWorkItem(
          specName, batchExePath, [.. batchable], specFile)));
      }
    }

    // On unfiltered runs, clean up orphaned fragment files and exe-staging
    // directories for tests/specs that no longer exist. (No global cache to
    // invalidate — orphan cleanup is the only persistent-state hygiene step.)
    if (filter == null) {
      foreach (var specDir2 in Directory.GetDirectories(fragmentDir)) {
        if (Path.GetFileName(specDir2) == ".spec-cache") continue;
        foreach (var file in Directory.GetFiles(specDir2, "*.test")) {
          if (!allFragmentPaths.Contains(Path.GetFullPath(file))) {
            try { File.Delete(file); } catch { }
          }
        }
      }
      var expectedSpecDirs = new HashSet<string>(specs.Select(s => Path.GetFileNameWithoutExtension(s.FilePath)), StringComparer.OrdinalIgnoreCase);
      foreach (var dir in Directory.GetDirectories(fragmentDir)) {
        if (Path.GetFileName(dir) == ".spec-cache") continue;
        if (!expectedSpecDirs.Contains(Path.GetFileName(dir))) {
          try { Directory.Delete(dir, recursive: true); } catch { }
        }
      }
      if (Directory.Exists(specCacheDir)) {
        foreach (var dir in Directory.GetDirectories(specCacheDir)) {
          if (!expectedSpecDirs.Contains(Path.GetFileName(dir))) {
            try { Directory.Delete(dir, recursive: true); } catch { }
          }
        }
      }
    }

    // Summary log: how many tests took the batched path vs the per-fragment
    // path. Useful for understanding where the speedup is coming from.
    int batchedTestCount = 0, batchCount = 0, singleTestCount = 0;
    foreach (var w in workItems) {
      switch (w) {
        case AnyWorkItem.Batch b:
          batchCount++;
          batchedTestCount += b.Item.Tests.Length;
          break;
        case AnyWorkItem.Single:
          singleTestCount++;
          break;
      }
    }
    Logger.Info(LogCategory.Testing,
      $"Batching: {batchedTestCount} test(s) in {batchCount} batch(es), {singleTestCount} test(s) per-fragment");

    return new PrepareResult([.. workItems], totalTests, errors);
  }

  /// <summary>
  /// Build the header + expectation sections of a fragment file (everything up
  /// through the second `---` separator). The IR section that follows is
  /// produced by either compiling the test in isolation
  /// (`GenerateFragmentContent`) or by extracting from a batched compile
  /// (`GenerateFragmentContentWithIr`).
  /// </summary>
  private static StringBuilder BuildFragmentPrelude(TestCase test) {
    var sb = new StringBuilder();
    sb.AppendLine($"// Test: {test.Name}");
    sb.AppendLine(test.Source);
    sb.AppendLine("---");

    if (test.Args != null) sb.AppendLine($"Args: {test.Args}");
    if (test.MmTrace) sb.AppendLine("MmTrace: true");
    if (test.AsyncTrace) sb.AppendLine("AsyncTrace: true");
    if (test.TimeoutMs.HasValue) sb.AppendLine($"TimeoutMs: {test.TimeoutMs.Value}");

    if (test.Expectation is SuccessExpectation success) {
      if (success.ExitCode.HasValue) sb.AppendLine($"ExitCode: {success.ExitCode.Value}");
      if (success.Stdout != null) {
        sb.AppendLine("Stdout: ```");
        sb.AppendLine(success.Stdout);
        sb.AppendLine("```");
      }
      if (success.Stderr != null) {
        sb.AppendLine("Stderr: ```");
        sb.AppendLine(success.Stderr);
        sb.AppendLine("```");
      }
      if (success.RequiredIR != null) {
        sb.AppendLine("RequiredIR: ```");
        sb.AppendLine(success.RequiredIR);
        sb.AppendLine("```");
      }
      if (success.RequiredRdata != null) {
        sb.AppendLine("RequiredRdata: ```");
        sb.AppendLine(success.RequiredRdata);
        sb.AppendLine("```");
      }
      if (success.RequiredData != null) {
        sb.AppendLine("RequiredData: ```");
        sb.AppendLine(success.RequiredData);
        sb.AppendLine("```");
      }
    } else if (test.Expectation is CompilerErrorExpectation compilerError) {
      sb.AppendLine("MaxoncStderr: ```");
      sb.AppendLine(compilerError.ExpectedStderr);
      sb.AppendLine("```");
    }

    sb.AppendLine("---");
    return sb;
  }

  /// <summary>
  /// Build a complete fragment file using a pre-extracted IR snippet (e.g. one
  /// produced by `SplitBatchedIr`). Used by the batched-compile path so we
  /// don't recompile each test individually just to fill in the
  /// `// CompiledIR` section. The IR is for inspection only — it may differ
  /// from a per-fragment compile because batched optimization decisions are
  /// not identical.
  /// </summary>
  public static string GenerateFragmentContentWithIr(TestCase test, string? compiledIr) {
    var sb = BuildFragmentPrelude(test);
    if (test.Expectation is SuccessExpectation s && s.RequiredIR == null && compiledIr != null) {
      sb.AppendLine("// CompiledIR");
      sb.Append(compiledIr.Trim());
      sb.AppendLine();
    }
    sb.AppendLine("---");
    return sb.ToString();
  }

  public static (string Content, string? Error) GenerateFragmentContent(TestCase test, string exePath, string fragmentPath, Compiler.CompileTarget? target = null) {
    var sb = BuildFragmentPrelude(test);
    string? error = null;
    var commentLine = $"// Test: {test.Name}";
    var sourceWithComment = $"{commentLine}\n{test.Source}";

    // Compile to executable and capture IR (for success expectations only, skip if RequiredIR covers it)
    if (test.Expectation is SuccessExpectation s2 && s2.RequiredIR == null) {
      Compiler.SourceFile[] sources;
      if (test.SourceFiles != null) {
        var tempDir = Path.Combine(Path.GetTempPath(), $"maxon-frag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
          // Spec-fragment multi-file: RootPath = tempDir (decision #2).
          var multiFileRoot = tempDir;
          sources = [.. test.SourceFiles.Select(f => {
            var path = Path.Combine(tempDir, f.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, f.Source);
            return new Compiler.SourceFile(path, f.Source, multiFileRoot);
          })];
          var result = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, target: target);
          if (result.Success) {
            if (result.ArchIr != null) {
              sb.AppendLine("// CompiledIR");
              sb.Append(result.ArchIr.Trim());
              sb.AppendLine();
            }
          } else {
            var errorStr = string.Join("\n", result.Errors.Select(e => e.Format()));
            sb.AppendLine("// CompiledIR");
            sb.AppendLine($"// Compilation failed: {errorStr}");
            error ??= errorStr;
          }
          try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
        } finally {
          try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
      } else {
        // Spec-fragment single-file: RootPath = the fragment directory (decision #2).
        sources = [new Compiler.SourceFile(fragmentPath, sourceWithComment, Path.GetDirectoryName(fragmentPath))];
        var result = new Compiler.Compiler().Compile(sources, exePath, returnIr: true, target: target);
        if (result.Success) {
          if (result.ArchIr != null) {
            sb.AppendLine("// CompiledIR");
            sb.Append(result.ArchIr.Trim());
            sb.AppendLine();
          }
        } else {
          var errorStr = string.Join("\n", result.Errors.Select(e => e.Format()));
          sb.AppendLine("// CompiledIR");
          sb.AppendLine($"// Compilation failed: {errorStr}");
          error ??= errorStr;
        }
        try { if (File.Exists(exePath)) File.Delete(exePath); } catch { }
      }
    }

    sb.AppendLine("---");

    return (sb.ToString(), error);
  }

  /// <summary>
  /// Parse a fragment file.
  /// </summary>
  public static Fragment? ParseFragment(string fragmentPath) {
    if (!File.Exists(fragmentPath)) {
      return null;
    }

    var content = File.ReadAllText(fragmentPath);
    var lines = content.Split('\n');

    // Extract test name from first line
    var testName = "unknown";
    if (lines.Length > 0 && lines[0].StartsWith("// Test: ")) {
      testName = lines[0]["// Test: ".Length..].Trim();
    }

    // Find the separator "---"
    var separatorIndex = Array.FindIndex(lines, 1, l => l.Trim() == "---");
    if (separatorIndex < 0) {
      return null;
    }

    // Extract source code (from start to first ---), including the comment line
    // so that line numbers in error messages match the actual file
    var sourceLines = lines[0..separatorIndex];
    var source = string.Join("\n", sourceLines).Trim();

    // Parse expectations (between first --- and second ---)
    var secondSeparatorIndex = Array.FindIndex(lines, separatorIndex + 1, l => l.Trim() == "---");
    if (secondSeparatorIndex < 0) {
      secondSeparatorIndex = lines.Length;
    }

    var expectationSection = string.Join("\n", lines[(separatorIndex + 1)..secondSeparatorIndex]);
    var (expectation, fragmentArgs, mmTrace, asyncTrace, timeoutMs) = ParseExpectation(expectationSection);

    // Parse compiled IR (between second --- and third ---).
    // Non-empty section 3 must start with the "// CompiledIR" header so it
    // cannot be confused with the RequiredIR expectation block in section 2.
    string? generatedIR = null;
    if (secondSeparatorIndex < lines.Length) {
      var thirdSeparatorIndex = Array.FindIndex(lines, secondSeparatorIndex + 1, l => l.Trim() == "---");
      if (thirdSeparatorIndex < 0) {
        thirdSeparatorIndex = lines.Length;
      }
      var irSection = lines[(secondSeparatorIndex + 1)..thirdSeparatorIndex];
      generatedIR = ExtractGeneratedIr(irSection, fragmentPath);
    }

    // Detect multi-file markers in source
    var sourceFiles = SplitMultiFileSource(source);

    return new Fragment {
      FilePath = fragmentPath,
      TestName = testName,
      Source = source,
      Expectation = expectation,
      GeneratedIR = generatedIR,
      Args = fragmentArgs,
      SourceFiles = sourceFiles,
      MmTrace = mmTrace,
      AsyncTrace = asyncTrace,
      TimeoutMs = timeoutMs,
    };
  }

  private static string? ExtractGeneratedIr(string[] lines, string fragmentPath) {
    // Find the first non-blank line. An entirely blank/empty section is legal
    // (e.g. the fragment was generated with RequiredIR set, or for a
    // compiler_error test), and corresponds to "no compiled IR captured".
    int firstNonBlank = -1;
    for (int i = 0; i < lines.Length; i++) {
      if (lines[i].Trim().Length > 0) {
        firstNonBlank = i;
        break;
      }
    }
    if (firstNonBlank < 0) {
      return null;
    }

    // Non-empty section 3 must start with the "// CompiledIR" header. If it
    // does not, the fragment was written by an older format and needs to be
    // regenerated before the parser can trust its contents.
    if (lines[firstNonBlank].Trim() != "// CompiledIR") {
      throw new InvalidDataException(
        $"Fragment '{fragmentPath}' is in a stale format: section 3 is missing the '// CompiledIR' header. " +
        "Regenerate the fragment (run the spec test runner with regeneration enabled) and retry.");
    }

    // Drop the header and then apply the existing "// Compilation failed:"
    // filter on the remainder — a compile-failure marker means we captured no
    // usable IR, so return null.
    var remainder = lines[(firstNonBlank + 1)..];
    foreach (var line in remainder) {
      if (line.TrimStart().StartsWith("// Compilation failed:")) {
        return null;
      }
    }

    return string.Join('\n', remainder).TrimEnd();
  }

  private static (TestExpectation Expectation, string? Args, bool MmTrace, bool AsyncTrace, int? TimeoutMs) ParseExpectation(string section) {
    var lines = section.Split('\n');
    int? exitCode = null;
    string? stdout = null;
    string? stderr = null;
    string? RequiredIR = null;
    string? requiredRdata = null;
    string? requiredData = null;
    string? expectedError = null;
    string? args = null;
    bool mmTrace = false;
    bool asyncTrace = false;
    int? timeoutMs = null;

    var i = 0;
    while (i < lines.Length) {
      var line = lines[i].Trim();

      if (line.StartsWith("ExitCode:")) {
        var value = line["ExitCode:".Length..].Trim();
        if (int.TryParse(value, out var code)) {
          exitCode = code;
        }
      } else if (line.StartsWith("Args:")) {
        args = line["Args:".Length..].Trim();
      } else if (line.StartsWith("MmTrace:")) {
        mmTrace = line["MmTrace:".Length..].Trim() == "true";
      } else if (line.StartsWith("AsyncTrace:")) {
        asyncTrace = line["AsyncTrace:".Length..].Trim() == "true";
      } else if (line.StartsWith("TimeoutMs:")) {
        var value = line["TimeoutMs:".Length..].Trim();
        if (int.TryParse(value, out var ms) && ms > 0) {
          timeoutMs = ms;
        }
      } else if (line.StartsWith("MaxoncStderr: ```")) {
        expectedError = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("Stdout: ```")) {
        stdout = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("Stderr: ```")) {
        stderr = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("RequiredIR: ```")) {
        RequiredIR = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("RequiredRdata: ```")) {
        requiredRdata = ExtractMultilineValue(lines, ref i);
      } else if (line.StartsWith("RequiredData: ```")) {
        requiredData = ExtractMultilineValue(lines, ref i);
      }

      i++;
    }

    if (expectedError != null) {
      return (new CompilerErrorExpectation {
        ExpectedStderr = expectedError
      }, args, mmTrace, asyncTrace, timeoutMs);
    }

    return (new SuccessExpectation {
      ExitCode = exitCode,
      Stdout = stdout,
      Stderr = stderr,
      RequiredIR = RequiredIR,
      RequiredRdata = requiredRdata,
      RequiredData = requiredData,
      MmTrace = mmTrace,
      AsyncTrace = asyncTrace,
    }, args, mmTrace, asyncTrace, timeoutMs);
  }

  private static readonly Regex FileMarkerPattern = FileMarkerRegex();

  private static List<(string FileName, string Source)>? SplitMultiFileSource(string source) {
    var matches = FileMarkerPattern.Matches(source);
    if (matches.Count == 0) return null;

    var files = new List<(string FileName, string Source)>();
    for (int i = 0; i < matches.Count; i++) {
      var fileName = matches[i].Groups[1].Value.Trim();
      // Reject `..` segments to prevent temp-dir escape when the files are
      // written to disk. Forward slashes are allowed for subdirectories.
      var segments = fileName.Replace('\\', '/').Split('/');
      if (segments.Any(s => s == ".." || s == "."))
        throw new InvalidOperationException(
          $"Invalid '// --- file:' marker '{fileName}': '.' and '..' segments are not allowed");
      var start = matches[i].Index + matches[i].Length;
      var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
      var fileSource = source[start..end].Trim();
      files.Add((fileName, fileSource));
    }

    return files.Count > 0 ? files : null;
  }

  private static string ExtractMultilineValue(string[] lines, ref int i) {
    var sb = new StringBuilder();
    i++; // Move past the opening line with ```
    while (i < lines.Length && !lines[i].Trim().StartsWith("```")) {
      sb.AppendLine(lines[i]);
      i++;
    }
    return sb.ToString().TrimEnd();
  }

  [GeneratedRegex(@"^// --- file:\s*(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
  private static partial Regex FileMarkerRegex();

  // ===== Batched-fragment support =====

  /// <summary>
  /// Markers the dispatcher emits around each test's run. The runner slices
  /// the binary's stdout on these to recover per-test stdout and exit code.
  /// Sentinel sequences chosen to be unlikely to appear in any test's output.
  /// </summary>
  public const string BatchTestBeginMarker = "<<<MAXON_BATCH_TEST_BEGIN:";
  public const string BatchTestEndMarker = "<<<MAXON_BATCH_TEST_END:";
  public const string BatchMarkerSuffix = ">>>";

  /// <summary>
  /// Build a single batched source file containing the rewritten bodies of
  /// every batchable test in the spec, plus a dispatcher `main` that runs
  /// each test's renamed-main in sequence. The dispatcher prints framing
  /// markers around each test so the runner can slice the binary's stdout
  /// into per-test stdout and per-test exit code.
  ///
  /// Single-execution model: the runner invokes the binary ONCE; the
  /// dispatcher runs every batched test in order, prints
  /// `&lt;&lt;&lt;MAXON_BATCH_TEST_BEGIN:&lt;name&gt;&gt;&gt;&gt;\n` before each, then the
  /// test's own stdout, then `&lt;&lt;&lt;MAXON_BATCH_TEST_END:&lt;name&gt;:&lt;exitCode&gt;&gt;&gt;&gt;\n`
  /// after. On a clean run the binary returns 0; per-test pass/fail comes
  /// from parsing the markers. A test that panics terminates the process,
  /// so tests after the panic have no END marker and are reported as
  /// "did not run due to earlier crash".
  ///
  /// Returns Source=null if every test in the batch was rejected by the
  /// rewriter (caller falls back to per-fragment compilation).
  ///
  /// SkippedTestNames contains the names of tests the rewriter rejected. The
  /// caller is responsible for running those tests via the per-fragment path
  /// (the dispatcher won't run them).
  /// </summary>
  public static (string? Source, List<string> SkippedReasons, HashSet<string> SkippedTestNames) BuildBatchSource(string specName, IReadOnlyList<TestCase> tests) {
    var skipped = new List<string>();
    var skippedNames = new HashSet<string>();
    var sb = new StringBuilder();
    var includedTests = new List<(string TestName, string MangledMain)>();

    foreach (var test in tests) {
      var rr = BatchRewriter.Rewrite(test.Name, test.Source);
      if (!rr.Batchable || rr.RewrittenSource == null || rr.MangledMainName == null) {
        skipped.Add($"{specName}/{test.Name}: {rr.Reason}");
        skippedNames.Add(test.Name);
        continue;
      }
      sb.AppendLine(rr.RewrittenSource);
      sb.AppendLine();
      includedTests.Add((test.Name, rr.MangledMainName));
    }

    if (includedTests.Count == 0) {
      return (null, skipped, skippedNames);
    }

    sb.AppendLine($"// --- batch dispatcher for {specName} ---");
    sb.AppendLine("function main() returns ExitCode");
    foreach (var (testName, mangled) in includedTests) {
      // Print the BEGIN marker, run the test, print the END marker with
      // the test's exit code. Each marker on its own line so the parser can
      // anchor on line boundaries even if a test's stdout doesn't end with
      // a newline.
      sb.AppendLine($"\tprint(\"\\n{BatchTestBeginMarker}{testName}{BatchMarkerSuffix}\\n\")");
      sb.AppendLine($"\tlet ec_{SanitizeTestName(testName)} = {mangled}()");
      sb.AppendLine($"\tprint(\"\\n{BatchTestEndMarker}{testName}:{{ec_{SanitizeTestName(testName)}}}{BatchMarkerSuffix}\\n\")");
    }
    sb.AppendLine("\treturn 0 as ExitCode");
    sb.AppendLine("end 'main'");

    return (sb.ToString(), skipped, skippedNames);
  }

  /// <summary>
  /// Mirror BatchRewriter.MangleSymbol's sanitization for use in local
  /// variable names within the dispatcher (which can't contain `-` etc.).
  /// </summary>
  private static string SanitizeTestName(string testName) {
    var sb = new StringBuilder(testName.Length);
    foreach (var c in testName) {
      if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c); else sb.Append('_');
    }
    return sb.ToString();
  }

  /// <summary>
  /// Split the IR text returned by a batched compile into per-test IR snippets,
  /// suitable for embedding in each test's `// CompiledIR` section as a human
  /// inspection aid. NOT semantically identical to a per-fragment compile —
  /// inlining/optimization decisions can differ when more code is in the unit.
  ///
  /// Strategy: top-level `func @<name>` blocks whose name starts with the
  /// rewriter's `_b_&lt;sanitized&gt;_` prefix are attributed to that test. The
  /// prefix is stripped from every occurrence within the function body (call
  /// targets, label-qualified jumps, lea_func operands, etc.) so the snippet
  /// reads as if compiled in isolation. Functions with no `_b_` prefix
  /// (dispatcher `main`, stdlib, runtime helpers) are not attributed to any
  /// test and are dropped.
  /// </summary>
  public static Dictionary<string, string> SplitBatchedIr(string archIr, IReadOnlyList<TestCase> tests) {
    var result = new Dictionary<string, string>();

    // Map sanitized test name → original test name. Match the longest sanitized
    // prefix first so "foo-bar" → `_b_foo_bar_*` doesn't get mis-attributed to
    // a sibling test "foo".
    var sanitizedToTest = tests
      .Select(t => (Sanitized: SanitizeTestName(t.Name), Test: t))
      .OrderByDescending(p => p.Sanitized.Length)
      .ToList();

    var funcs = SplitTopLevelFunctions(archIr);
    var perTest = new Dictionary<string, List<string>>();

    foreach (var (header, body) in funcs) {
      // header looks like `  func @<name>(...) -> ... {` (indent 2). Function
      // names are emitted module-qualified, e.g. `<spec>._b_<sanitized>_main`
      // — so look for the rewriter prefix anywhere in the name, not only at
      // the start.
      var name = ExtractFuncName(header);
      if (name == null) continue;
      var bIdx = name.IndexOf("_b_");
      if (bIdx < 0) continue;
      var afterB = name[(bIdx + "_b_".Length)..];

      foreach (var (sanitized, test) in sanitizedToTest) {
        var suffix = $"{sanitized}_";
        if (!afterB.StartsWith(suffix)) continue;

        // Strip the rewriter prefix everywhere in this function's text. The
        // `_b_<sanitized>_` sentinel is distinctive enough that a plain
        // string-replace is safe (no false positives in opcodes, register
        // names, or string literals — those don't contain that sequence).
        // The leftover module prefix (e.g. `abs.main` instead of `main`)
        // stays — it's a real qualifying namespace and reads fine.
        var prefix = $"_b_{sanitized}_";
        var stripped = (header + body).Replace(prefix, "");
        if (!perTest.TryGetValue(test.Name, out var list)) {
          list = [];
          perTest[test.Name] = list;
        }
        list.Add(stripped);
        break;
      }
    }

    foreach (var (testName, parts) in perTest) {
      var sb = new StringBuilder();
      sb.AppendLine("module {");
      foreach (var fn in parts) sb.Append(fn);
      sb.AppendLine("}");
      // Re-stabilize counter-based label numbering against this test's slice
      // of functions only. The batched IR uses module-wide counters (e.g.
      // `__nonnull_skip_47`) that depend on every other test in the batch;
      // restabilizing here makes the numbers depend only on this test's IR.
      result[testName] = IrPrinter.StabilizeLabels(sb.ToString()).TrimEnd();
    }

    return result;
  }

  /// <summary>
  /// Split an IR module's text into top-level function blocks. Each entry is
  /// (headerLine, bodyAndClosingBrace). Lines outside any function (the outer
  /// `module {` and the trailing `}`) are dropped; the caller wraps the
  /// extracted functions in a fresh module. Line endings are normalized to LF
  /// in the output regardless of the input's mix of LF/CRLF.
  /// </summary>
  private static List<(string Header, string Body)> SplitTopLevelFunctions(string ir) {
    var results = new List<(string, string)>();
    var lines = ir.Replace("\r\n", "\n").Split('\n');
    int i = 0;
    while (i < lines.Length) {
      var line = lines[i];
      // Top-level functions are indented exactly 2 spaces.
      if (line.StartsWith("  func @")) {
        var header = line + "\n";
        var bodySb = new StringBuilder();
        i++;
        // Collect lines until the matching closing `}` at indent 2.
        while (i < lines.Length) {
          bodySb.Append(lines[i]).Append('\n');
          if (lines[i] == "  }") {
            i++;
            break;
          }
          i++;
        }
        results.Add((header, bodySb.ToString()));
      } else {
        i++;
      }
    }
    return results;
  }

  /// <summary>
  /// Pull the function name out of a header line like `  func @foo(args) -> ret {`.
  /// Returns null if the line doesn't match the expected shape. Spec module
  /// names can contain `-`, so they're allowed here even though the rewriter's
  /// sanitizer converts them to `_`.
  /// </summary>
  private static string? ExtractFuncName(string headerLine) {
    var marker = "func @";
    var start = headerLine.IndexOf(marker);
    if (start < 0) return null;
    start += marker.Length;
    var end = start;
    while (end < headerLine.Length) {
      var c = headerLine[end];
      if (char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-') end++;
      else break;
    }
    return end > start ? headerLine[start..end] : null;
  }

}
