using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

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

    // Tests with their own argv conflict with the dispatcher's argv usage.
    if (test.Args != null) return false;

    // Tests that call CommandLine.args() see two extra argv entries when run
    // via the batched dispatcher (the exe path + the test name) versus the
    // single argv[0] they'd see in a per-test exe. Exclude such tests so
    // count- or content-sensitive argv assertions stay accurate.
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
      if (batchable.Count > 0) {
        var batchBaseName = BatchFragmentBaseName(specName);
        var batchFragmentPath = Path.Combine(specFragmentDir, $"{batchBaseName}.test");
        var batchExePath = Path.Combine(specCacheDir, specName, $"{batchBaseName}{exeExt}");
        allFragmentPaths.Add(Path.GetFullPath(batchFragmentPath));

        workItems.Add(new AnyWorkItem.Batch(new SpecBatchWorkItem(
          specName, batchFragmentPath, batchExePath, [.. batchable], specFile)));
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

  public static (string Content, string? Error) GenerateFragmentContent(TestCase test, string exePath, string fragmentPath, Compiler.CompileTarget? target = null) {
    var sb = new StringBuilder();
    string? error = null;

    var commentLine = $"// Test: {test.Name}";
    var sourceWithComment = $"{commentLine}\n{test.Source}";

    sb.AppendLine(commentLine);
    sb.AppendLine(test.Source);
    sb.AppendLine("---");

    if (test.Args != null) {
      sb.AppendLine($"Args: {test.Args}");
    }

    if (test.MmTrace) {
      sb.AppendLine("MmTrace: true");
    }

    if (test.AsyncTrace) {
      sb.AppendLine("AsyncTrace: true");
    }

    if (test.Expectation is SuccessExpectation success) {
      if (success.ExitCode.HasValue) {
        sb.AppendLine($"ExitCode: {success.ExitCode.Value}");
      }
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

      // Write required IR if specified
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

    // Compile to executable and capture IR (for success expectations only, skip if RequiredIR covers it)
    if (test.Expectation is SuccessExpectation s2 && s2.RequiredIR == null) {
      Compiler.SourceFile[] sources;
      if (test.SourceFiles != null) {
        var tempDir = Path.Combine(Path.GetTempPath(), $"maxon-frag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try {
          sources = [.. test.SourceFiles.Select(f => {
            var path = Path.Combine(tempDir, f.FileName);
            File.WriteAllText(path, f.Source);
            return new Compiler.SourceFile(path, f.Source);
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
        sources = [new Compiler.SourceFile(fragmentPath, sourceWithComment)];
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
    var (expectation, fragmentArgs, mmTrace, asyncTrace) = ParseExpectation(expectationSection);

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

  private static (TestExpectation Expectation, string? Args, bool MmTrace, bool AsyncTrace) ParseExpectation(string section) {
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
      }, args, mmTrace, asyncTrace);
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
    }, args, mmTrace, asyncTrace);
  }

  private static readonly Regex FileMarkerPattern = FileMarkerRegex();

  private static List<(string FileName, string Source)>? SplitMultiFileSource(string source) {
    var matches = FileMarkerPattern.Matches(source);
    if (matches.Count == 0) return null;

    var files = new List<(string FileName, string Source)>();
    for (int i = 0; i < matches.Count; i++) {
      var fileName = matches[i].Groups[1].Value.Trim();
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
  /// Build a single batched source file containing the rewritten bodies of
  /// every batchable test in the spec, plus a dispatcher `main` that selects
  /// which test's mangled-main to call based on argv[1]. Free functions,
  /// types, typealiases, lets, and the per-test `main` are all renamed by the
  /// rewriter to per-test mangled names, so all fragments coexist in one file
  /// without collisions.
  ///
  /// Returns Source=null if every test in the batch was rejected by the
  /// rewriter (caller falls back to per-fragment compilation).
  ///
  /// SkippedTestNames contains the names of tests the rewriter rejected. The
  /// caller is responsible for running those tests via the per-fragment path
  /// (otherwise the dispatcher would return sentinel 253 for them).
  /// </summary>
  public static (string? Source, List<string> SkippedReasons, HashSet<string> SkippedTestNames) BuildBatchSource(string specName, IReadOnlyList<TestCase> tests) {
    var skipped = new List<string>();
    var skippedNames = new HashSet<string>();
    var sb = new StringBuilder();
    var dispatcherCases = new List<(string TestName, string MangledMain)>();

    foreach (var test in tests) {
      var rr = BatchRewriter.Rewrite(test.Name, test.Source);
      if (!rr.Batchable || rr.RewrittenSource == null || rr.MangledMainName == null) {
        skipped.Add($"{specName}/{test.Name}: {rr.Reason}");
        skippedNames.Add(test.Name);
        continue;
      }
      sb.AppendLine(rr.RewrittenSource);
      sb.AppendLine();
      dispatcherCases.Add((test.Name, rr.MangledMainName));
    }

    if (dispatcherCases.Count == 0) {
      return (null, skipped, skippedNames);
    }

    sb.AppendLine($"// --- batch dispatcher for {specName} ---");
    sb.AppendLine("function main() returns ExitCode");
    sb.AppendLine("\tlet args = CommandLine.args()");
    sb.AppendLine("\tlet testName = try args.get(1) otherwise \"\"");
    sb.AppendLine("\tif testName == \"\" 'noArg'");
    sb.AppendLine("\t\treturn 254 as ExitCode");
    sb.AppendLine("\tend 'noArg'");
    int caseIdx = 0;
    foreach (var (testName, mangled) in dispatcherCases) {
      var label = $"r{caseIdx}";
      caseIdx++;
      sb.AppendLine($"\tif testName == \"{testName}\" '{label}'");
      sb.AppendLine($"\t\treturn {mangled}()");
      sb.AppendLine($"\tend '{label}'");
    }
    sb.AppendLine("\treturn 253 as ExitCode");
    sb.AppendLine("end 'main'");

    return (sb.ToString(), skipped, skippedNames);
  }

  /// <summary>
  /// Generate the on-disk text of a __batch.test file. Includes per-test source
  /// + expectation sections, plus a single shared CompiledIR section at the end.
  /// Note: this method does NOT compile — the IR is supplied by the caller from
  /// a previous compile of the batched source.
  /// </summary>
  public static string GenerateBatchContent(string specName, IReadOnlyList<TestCase> tests, string? compiledIr) {
    var sb = new StringBuilder();
    sb.Append("// Batch: ").AppendLine(specName);
    sb.Append("// Tests: ").AppendLine(string.Join(", ", tests.Select(t => t.Name)));

    foreach (var test in tests) {
      sb.AppendLine("---");
      sb.Append("// Test: ").AppendLine(test.Name);
      sb.AppendLine(test.Source);
      sb.AppendLine("---");
      AppendExpectationLines(sb, test);
    }

    sb.AppendLine("---");
    if (compiledIr != null) {
      sb.AppendLine("// CompiledIR");
      sb.Append(compiledIr.Trim());
      sb.AppendLine();
    }
    sb.AppendLine("---");

    return sb.ToString();
  }

  private static void AppendExpectationLines(StringBuilder sb, TestCase test) {
    if (test.Args != null) sb.Append("Args: ").AppendLine(test.Args);
    if (test.MmTrace) sb.AppendLine("MmTrace: true");
    if (test.AsyncTrace) sb.AppendLine("AsyncTrace: true");
    if (test.Expectation is SuccessExpectation success) {
      if (success.ExitCode.HasValue) sb.Append("ExitCode: ").Append(success.ExitCode.Value).AppendLine();
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
    } else if (test.Expectation is CompilerErrorExpectation ce) {
      sb.AppendLine("MaxoncStderr: ```");
      sb.AppendLine(ce.ExpectedStderr);
      sb.AppendLine("```");
    }
  }

  /// <summary>
  /// A parsed __batch.test file: a list of per-test fragments plus the shared
  /// compiled IR (which applies to the batched binary as a whole, not any one
  /// fragment in isolation).
  /// </summary>
  public class BatchedFragment {
    public required string FilePath { get; init; }
    public required string SpecName { get; init; }
    public required List<Fragment> Fragments { get; init; }
    public string? SharedCompiledIR { get; init; }
  }

  /// <summary>
  /// Parse a __batch.test file. Returns null if the file does not exist or is
  /// not in the batched format (callers should NOT silently fall through to the
  /// per-fragment parser; a missing batch file means the batch hasn't been
  /// generated yet, and a non-batched file with this name is a bug).
  /// </summary>
  public static BatchedFragment? ParseBatchFragment(string batchPath) {
    if (!File.Exists(batchPath)) return null;
    var content = File.ReadAllText(batchPath);
    var lines = content.Split('\n');
    if (lines.Length == 0 || !lines[0].StartsWith("// Batch: ")) return null;

    var specName = lines[0]["// Batch: ".Length..].Trim();

    // Locate all section separators.
    var sepIndices = new List<int>();
    for (int i = 0; i < lines.Length; i++) {
      if (lines[i].Trim() == "---") sepIndices.Add(i);
    }
    // Layout: [header lines]\n---\n<test1 source>\n---\n<test1 expect>\n---\n... \n---\n<IR>\n---
    // So sections come in pairs (source, expect) per test, then a final IR section.
    // We expect (sepIndices.Count - 1) pairs + 1 IR = at least 3 separators total
    // if there's at least one test.
    if (sepIndices.Count < 3) return null;

    var fragments = new List<Fragment>();
    int s = 0;
    while (s + 2 < sepIndices.Count) {
      var sourceStart = sepIndices[s] + 1;
      var sourceEnd = sepIndices[s + 1];
      var expectStart = sepIndices[s + 1] + 1;
      var expectEnd = sepIndices[s + 2];

      var sourceLines = lines[sourceStart..sourceEnd];
      var source = string.Join("\n", sourceLines).Trim();

      // Extract the test name from the first non-blank line of the source
      // section (the "// Test: <name>" comment is the convention used by
      // GenerateBatchContent).
      string testName = "unknown";
      foreach (var line in sourceLines) {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("// Test: ")) {
          testName = trimmed["// Test: ".Length..].Trim();
          break;
        }
        if (trimmed.Length > 0) break;
      }

      var expectationSection = string.Join("\n", lines[expectStart..expectEnd]);
      var (expectation, args, mmTrace, asyncTrace) = ParseExpectation(expectationSection);

      fragments.Add(new Fragment {
        FilePath = batchPath,
        TestName = testName,
        Source = source,
        Expectation = expectation,
        GeneratedIR = null,
        Args = args,
        SourceFiles = null,
        MmTrace = mmTrace,
        AsyncTrace = asyncTrace,
      });

      s += 2;
    }

    // The last separator pair brackets the shared CompiledIR section.
    string? sharedIr = null;
    if (s + 1 < sepIndices.Count) {
      var irStart = sepIndices[s] + 1;
      var irEnd = sepIndices[s + 1];
      var irLines = lines[irStart..irEnd];
      sharedIr = ExtractGeneratedIr(irLines, batchPath);
    }

    return new BatchedFragment {
      FilePath = batchPath,
      SpecName = specName,
      Fragments = fragments,
      SharedCompiledIR = sharedIr,
    };
  }
}
