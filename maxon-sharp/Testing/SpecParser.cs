using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Parses markdown spec files to extract tests.
/// </summary>
public static partial class SpecParser {
  /// <summary>
  /// Frontmatter `category` marking a spec that requires the public internet. Excluded from the
  /// default gate; `--network` opts back in. See ParseDirectory.
  /// </summary>
  public const string NetworkCategory = "network";

  /// <summary>
  /// Frontmatter `status` values that take a whole spec file out of THIS runner and hand it to
  /// another one. Both roads end in the same place and it is not the place either name suggests:
  /// <c>maxon-selfhosted</c> has not built since 2026-07-13 (accepted, deliberately not repaired),
  /// so a file marked either way is run by NOBODY while its goldens sit in the tree looking exactly
  /// like live ones.
  ///
  /// <para>⭐ THAT IS WHY EACH ONE MUST STATE A REASON (<see cref="StatusReasonKey"/>) AND WHY THE
  /// COUNT IS IN THE RUNNER'S TRAILER. The skip used to be a <c>Logger.Debug</c> line nobody reads,
  /// and 19 files / 173 tests accumulated behind it. Suspending a file is a legitimate move; doing it
  /// SILENTLY is what turned a legitimate move into invisible debt.</para>
  /// </summary>
  public static readonly IReadOnlySet<string> SuspendingStatuses =
    new HashSet<string>(StringComparer.Ordinal) { "draft", "selfhosted" };

  /// <summary>
  /// The frontmatter key carrying WHY a <see cref="SuspendingStatuses"/> file is suspended, and what
  /// would let it run again. Required — a spec that names one of those statuses without it is a
  /// preparation error, so a future author cannot suspend a file by adding two words.
  /// </summary>
  public const string StatusReasonKey = "status-reason";

  /// <summary>
  /// Parse spec TEXT, attributing it to <paramref name="filePath"/> for reporting.
  /// When targetKey is provided (e.g. "x64-windows"), extracts RequiredIR:{targetKey} blocks.
  ///
  /// <para>It takes the TEXT rather than the path so that every classification this file makes —
  /// suspended or live, parseable or not — is a pure function of the bytes, and can therefore be
  /// asserted without a directory of fixture files. <c>SpecRunSelfTest</c> drives exactly this entry
  /// point, which is the only coverage the "a spec that will not parse is an error" rule below has: no
  /// spec in the corpus is unparseable, and one added to give it coverage would break every run.</para>
  /// </summary>
  public static SpecFile ParseText(string content, string filePath, string? targetKey = null) {
    var (feature, status, category, statusReason) = ParseFrontmatter(content);
    var fileName = Path.GetFileName(filePath);
    var (tests, suspendedTests) = ExtractTests(content, fileName, targetKey);

    return new SpecFile {
      FilePath = filePath,
      Feature = feature,
      Status = status,
      Category = category,
      StatusReason = statusReason,
      Tests = tests,
      SuspendedTests = suspendedTests
    };
  }

  /// <summary>
  /// Parse all spec files in a directory, and take the census of the ones nothing runs.
  /// When targetKey is provided (e.g. "x64-windows"), extracts RequiredIR:{targetKey} blocks.
  /// </summary>
  public static SpecScan ParseDirectory(string specDir, string? targetKey = null, bool includeNetwork = false) {
    var files = new List<(string Path, string Content)>();
    var readErrors = new List<string>();

    // Read here rather than inside ScanFiles so that classification stays a pure function of bytes, and
    // so a file this process cannot READ lands in the same errors list as one it cannot PARSE. Both mean
    // the same thing to the suite — a spec that was not run and was not counted.
    foreach (var file in Directory.GetFiles(specDir, "*.md")) {
      try {
        files.Add((file, File.ReadAllText(file)));
      } catch (IOException ex) {
        readErrors.Add($"Could not read {file}: {ex.Message}");
      }
    }

    var scan = ScanFiles(files, targetKey, includeNetwork);
    scan.Errors.InsertRange(0, readErrors);

    return scan;
  }

  /// <summary>
  /// Classify already-read spec files into the ones this run will execute and the ones it will not,
  /// collecting the reasons for the second group and the errors that make a run impossible.
  ///
  /// <para>⚠ A SPEC THAT WILL NOT PARSE IS AN ERROR, NOT A LOG LINE. It used to be caught here and
  /// written to <c>Logger.Error</c>, which prints and returns — so a spec with an unrecognized code
  /// block, a bad <c>TimeoutMs</c>, or a test with no result checks at all VANISHED from the suite and
  /// the run still exited 0. That is the same lie as a silent suspension, arriving by accident instead
  /// of on purpose, and it is the more dangerous of the two because nobody chose it.</para>
  /// </summary>
  public static SpecScan ScanFiles(
      IEnumerable<(string Path, string Content)> files, string? targetKey = null, bool includeNetwork = false) {
    var specs = new List<SpecFile>();
    var suspendedFiles = new List<SuspendedSpec>();
    var suspendedTests = new List<SuspendedTest>();
    var errors = new List<string>();

    foreach (var (path, content) in files) {
      var fileName = Path.GetFileName(path);
      SpecFile spec;
      try {
        spec = ParseText(content, path, targetKey);
      } catch (Exception ex) {
        errors.Add($"Failed to parse {path}: {ex.Message}");
        continue;
      }

      if (SuspendingStatuses.Contains(spec.Status)) {
        // Every test the file DECLARES, which is not the same as `Tests.Count`: a per-test
        // `SelfhostedOnly` directive inside a whole-file suspension is redundant, but it still moves
        // that test out of `Tests` — so counting only `Tests` would report a file as holding fewer
        // tests than it has, and a census that undercounts is the debt hiding again one level down.
        // They are not double-counted: the per-test list below is built only for LIVE files. Read
        // ONCE, above both readers, so the refusal and the census cannot state different numbers for
        // the same file.
        var declaredTests = spec.Tests.Count + spec.SuspendedTests.Count;

        if (string.IsNullOrWhiteSpace(spec.StatusReason)) {
          errors.Add(
            $"{fileName}: `status: {spec.Status}` takes this file and its {declaredTests} test(s) out "
            + $"of this suite, and no runner in this tree picks them up instead. Add a `{StatusReasonKey}:` "
            + "line to the frontmatter saying why, and what would let them run again.");
          continue;
        }

        suspendedFiles.Add(new SuspendedSpec(fileName, spec.Status, declaredTests, spec.StatusReason));
        continue;
      }

      // A `category: network` spec talks to a real server on the public internet. That makes it
      // a coin toss on somebody else's uptime, not a gate on our compiler: httpbin.org has been
      // observed returning 503, and it rate-limits under the runner's parallelism, so the suite
      // goes red for reasons no change of ours caused. A gate that fails for reasons unrelated to
      // the code under test trains you to ignore it, which is worse than not having it.
      //
      // Not a suspension: these tests are RUNNABLE here and `--network` runs them, so counting them
      // beside files nothing can run would make the census's own number mean two things.
      if (!includeNetwork && spec.Category == NetworkCategory) {
        Logger.Debug(LogCategory.Testing, $"Skipping network spec (pass --network to include): {fileName}");
        continue;
      }

      specs.Add(spec);
      suspendedTests.AddRange(spec.SuspendedTests);
    }

    return new SpecScan(specs, new SpecSuspensionCensus(suspendedFiles, suspendedTests), errors);
  }

  private static (string feature, string status, string category, string? statusReason) ParseFrontmatter(string content) {
    var match = FrontmatterRegex().Match(content);
    if (!match.Success) {
      return ("unknown", "unknown", "unknown", null);
    }

    var yaml = match.Groups[1].Value;
    var feature = ExtractYamlValue(yaml, "feature") ?? "unknown";
    var status = ExtractYamlValue(yaml, "status") ?? "unknown";
    var category = ExtractYamlValue(yaml, "category") ?? "unknown";
    var statusReason = ExtractYamlValue(yaml, StatusReasonKey);

    return (feature, status, category, statusReason);
  }

  private static string? ExtractYamlValue(string yaml, string key) {
    var match = Regex.Match(yaml, $@"^{key}:\s*(.+)$", RegexOptions.Multiline);
    return match.Success ? match.Groups[1].Value.Trim() : null;
  }

  private static (List<TestCase> Tests, List<SuspendedTest> Suspended) ExtractTests(
      string content, string fileName, string? targetKey = null) {
    var tests = new List<TestCase>();
    var suspended = new List<SuspendedTest>();

    // Find all test markers: <!-- test: name -->
    var testMatches = TestMarkerRegex().Matches(content);

    foreach (Match testMatch in testMatches) {
      var testName = testMatch.Groups[1].Value;
      var startIndex = testMatch.Index + testMatch.Length;

      // Find the next test marker (active or disabled) or end of content
      var nextTestMatch = TestBoundaryRegex().Match(content, startIndex);
      var endIndex = nextTestMatch.Success ? nextTestMatch.Index : content.Length;
      var testSection = content[startIndex..endIndex];

      // Parse directives from HTML comments between the test marker and code block
      string? testArgs = null;
      var argsMatch = ArgsDirectiveRegex().Match(testSection);
      if (argsMatch.Success) {
        testArgs = argsMatch.Groups[1].Value.Trim();
      }

      // mm-trace capture mode is enabled by EITHER the `<!-- MmTrace -->`
      // directive OR the presence of an ```mm-trace block (whose body is the
      // expected normalized trace). Extract the block up front so it can both
      // flip the mode flag and count as a valid result check below.
      var mmTraceExpected = ExtractCodeBlock(testSection, "mm-trace");
      bool mmTrace = MmTraceDirectiveRegex().IsMatch(testSection) || mmTraceExpected != null;
      bool asyncTrace = AsyncTraceDirectiveRegex().IsMatch(testSection);

      // `<!-- DebugInfo -->` compiles this test's binary the way `maxon build` does by default:
      // with the `.mxdbg` sidecar's span capture switched on. The suite's other 3200 compiles run
      // with it OFF (the flag is [ThreadStatic] and the workers never set it), so without this
      // directive the whole debug-info lowering path has no coverage at all — which is how it came
      // to crash on a program the repo itself ships as a passing fragment.
      bool debugInfo = DebugInfoDirectiveRegex().IsMatch(testSection);

      // One test — rather than a whole file — handed to the self-hosted runner, by a
      // `<!-- SelfhostedOnly: why -->` directive between the test marker and its first fence.
      //
      // ⚠ THE REASON IS REQUIRED, for the same reason `status-reason` is: the runner this hands the
      // test to CANNOT BE BUILT, so the directive suspends it outright, and a suspension nobody has
      // to justify is one nobody revisits. The bare `<!-- SelfhostedOnly -->` spelling is refused
      // here rather than tolerated — silently accepting it would leave the older, cheaper road open
      // beside the one this check exists to close.
      var selfhostedOnly = SelfhostedOnlyDirectiveRegex().Match(testSection);
      if (selfhostedOnly.Success) {
        var reason = selfhostedOnly.Groups[1].Value.Trim();
        if (reason.Length == 0) {
          throw new Exception(
            $"Test '{testName}' carries a bare `<!-- SelfhostedOnly -->`. That directive takes the test "
            + "out of this suite and hands it to a runner that cannot be built, so it runs nowhere. "
            + "Spell it `<!-- SelfhostedOnly: why, and what would let it run here -->`.");
        }

        suspended.Add(new SuspendedTest(fileName, testName, reason));
        continue;
      }

      // `<!-- targets: a, b -->` restricts a test to the named target keys; on any
      // other target it is skipped. A blank or absent directive means no
      // restriction. The spec format is shared with the self-hosted runner
      // (maxon-selfhosted/Testing/SpecParser.maxon parses the same directive), and
      // this side ignoring it is not a no-op: the x64-only fault tests ran on
      // arm64-macOS and failed there for a year on an assertion their own spec
      // said did not apply.
      //
      // With no targetKey there is no target to test membership against, so the
      // restriction cannot be evaluated and the test runs — matching the
      // behavior of every other target-qualified lookup in this parser.
      var targetsMatch = TargetsDirectiveRegex().Match(testSection);
      if (targetsMatch.Success && targetKey != null) {
        var onlyTargets = targetsMatch.Groups[1].Value
          .Split(',')
          .Select(t => t.Trim())
          .Where(t => t.Length > 0)
          .ToList();

        if (onlyTargets.Count > 0 && !onlyTargets.Contains(targetKey)) {
          Logger.Debug(LogCategory.Testing,
            $"Skipping test '{testName}': restricted to {string.Join(", ", onlyTargets)}, running {targetKey}");
          continue;
        }
      }

      int? timeoutMs = null;
      var timeoutMatch = TimeoutMsDirectiveRegex().Match(testSection);
      if (timeoutMatch.Success) {
        if (!int.TryParse(timeoutMatch.Groups[1].Value.Trim(), out var parsedTimeout) || parsedTimeout <= 0) {
          throw new Exception(
            $"Test '{testName}' has an invalid TimeoutMs directive '{timeoutMatch.Groups[1].Value}'. " +
            "Expected a positive integer (milliseconds).");
        }
        timeoutMs = parsedTimeout;
      }

      var source = ExtractCodeBlock(testSection, "maxon");
      if (source == null) continue;

      ValidateCodeBlockLanguages(testName, testSection);

      var exitCode = ExtractCodeBlock(testSection, "exitcode");
      // Prefer a target-qualified `Stdout:{targetKey}` block over the bare
      // ```stdout block, mirroring the RequiredIR resolution below. This lets a
      // single test pin different expected output per target (e.g. FilePath's
      // `\`-separated Windows output vs `/`-separated posix output). When the
      // current target has no matching block, fall back to the portable bare
      // stdout; if neither exists, stdout stays null and no stdout check runs.
      var stdout = targetKey != null
        ? ExtractCodeBlock(testSection, $"Stdout:{targetKey}") ?? ExtractCodeBlock(testSection, "stdout")
        : ExtractCodeBlock(testSection, "stdout");
      var runtimeStderr = ExtractCodeBlock(testSection, "stderr");
      var compilerStderr = ExtractCodeBlock(testSection, "maxoncstderr");

      // Prefer target-qualified RequiredIR block, fall back to unqualified for backward compat
      var RequiredIR = targetKey != null
        ? ExtractCodeBlock(testSection, $"RequiredIR:{targetKey}") ?? ExtractCodeBlock(testSection, "RequiredIR")
        : ExtractCodeBlock(testSection, "RequiredIR");
      var requiredRdata = ExtractCodeBlock(testSection, "RequiredRdata");
      var requiredData = ExtractCodeBlock(testSection, "RequiredData");

      TestExpectation expectation;
      if (compilerStderr != null) {
        expectation = new CompilerErrorExpectation {
          ExpectedStderr = compilerStderr
        };
      } else {
        // An ```mm-trace block is itself a result check (the trace is the
        // assertion), so a test carrying only that block is valid.
        if (exitCode == null && stdout == null && runtimeStderr == null && RequiredIR == null && requiredRdata == null && requiredData == null && mmTraceExpected == null) {
          throw new Exception(
            $"Test '{testName}' has a maxon block but no result checks. " +
            "Add an exitcode, stdout, stderr, maxoncstderr, RequiredIR, RequiredRdata, RequiredData, or mm-trace block.");
        }
        expectation = new SuccessExpectation {
          ExitCode = exitCode != null ? int.Parse(exitCode.Trim()) : null,
          Stdout = stdout,
          Stderr = runtimeStderr,
          RequiredIR = RequiredIR,
          RequiredRdata = requiredRdata,
          RequiredData = requiredData,
        };
      }

      if (mmTrace && expectation is SuccessExpectation mmSuccess) {
        mmSuccess.MmTrace = true;
        mmSuccess.MmTraceExpected = mmTraceExpected;
      }
      if (asyncTrace && expectation is SuccessExpectation atSuccess) {
        atSuccess.AsyncTrace = true;
      }

      tests.Add(new TestCase {
        Name = testName,
        Source = source,
        Expectation = expectation,
        Args = testArgs,
        SourceFiles = SplitMultiFileSource(source),
        MmTrace = mmTrace,
        MmTraceExpected = mmTraceExpected,
        AsyncTrace = asyncTrace,
        DebugInfo = debugInfo,
        TimeoutMs = timeoutMs,
      });
    }

    // Also extract executable examples from Documentation section
    var docsMatch = DocsSectionRegex().Match(content);
    if (docsMatch.Success) {
      var docsStart = docsMatch.Index + docsMatch.Length;
      var testsSectionMatch = TestsSectionRegex().Match(content, docsStart);
      var docsEnd = testsSectionMatch.Success ? testsSectionMatch.Index : content.Length;
      var docsSection = content[docsStart..docsEnd];

      var exampleIndex = 0;
      var codeMatches = MaxonCodeBlockRegex().Matches(docsSection);
      foreach (Match codeMatch in codeMatches) {
        var code = codeMatch.Groups[1].Value;
        // Only include examples that have a main function (executable)
        if (code.Contains("function main()")) {
          exampleIndex++;

          var afterCode = docsSection[(codeMatch.Index + codeMatch.Length)..];

          // Check for maxoncstderr block (compile error expectation)
          var stderrMatch = MaxoncStderrBlockRegex().Match(afterCode);
          if (stderrMatch.Success && stderrMatch.Index < 20) {
            tests.Add(new TestCase {
              Name = $"docs-example-{exampleIndex}",
              Source = code,
              Expectation = new CompilerErrorExpectation {
                ExpectedStderr = stderrMatch.Groups[1].Value.TrimEnd()
              }
            });
            continue;
          }

          // Look for an exitcode block immediately following this code block
          var exitCodeMatch = ExitCodeBlockRegex().Match(afterCode);
          int? exitCode = 0;
          if (exitCodeMatch.Success && exitCodeMatch.Index < 20) {
            // exitcode block found close to the code block
            if (int.TryParse(exitCodeMatch.Groups[1].Value.Trim(), out var parsedCode)) {
              exitCode = parsedCode;
            }
          }

          tests.Add(new TestCase {
            Name = $"docs-example-{exampleIndex}",
            Source = code,
            Expectation = new SuccessExpectation {
              ExitCode = exitCode
            }
          });
        }
      }
    }

    return (tests, suspended);
  }

  /// <summary>
  /// Splits source containing "// --- file: name.maxon" markers into multiple files.
  /// Returns null if no file markers are found (single-file test).
  /// </summary>
  private static List<(string FileName, string Source)>? SplitMultiFileSource(string source) {
    var matches = FileMarkerRegex().Matches(source);
    if (matches.Count == 0) return null;

    var files = new List<(string FileName, string Source)>();
    for (int i = 0; i < matches.Count; i++) {
      var fileName = matches[i].Groups[1].Value.Trim();
      // Reject `..` segments to prevent temp-dir escape when files are written
      // to disk by the test framework. Forward slashes for subdirectories are
      // allowed (e.g. `// --- file: feature/sub/foo.maxon`).
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

  private static readonly HashSet<string> KnownCodeBlockLanguages = [
    "maxon", "exitcode", "stdout", "stderr", "maxoncstderr", "RequiredIR", "RequiredRdata", "RequiredData", "mm-trace"
  ];

  private static readonly HashSet<string> KnownCodeBlockPrefixes = [
    "RequiredIR", "RequiredLowering", "Stdout"
  ];

  private static void ValidateCodeBlockLanguages(string testName, string testSection) {
    foreach (Match match in CodeBlockLanguageRegex().Matches(testSection)) {
      var language = match.Groups[1].Value;
      if (KnownCodeBlockLanguages.Contains(language)) continue;
      // Allow target-qualified blocks like RequiredIR:x64-windows
      var colonIdx = language.IndexOf(':');
      if (colonIdx > 0 && KnownCodeBlockPrefixes.Contains(language[..colonIdx])) continue;
      throw new Exception(
        $"Test '{testName}' has unrecognized code block language '{language}'. " +
        $"Valid languages: {string.Join(", ", KnownCodeBlockLanguages)}");
    }
  }

  private static string? ExtractCodeBlock(string content, string language) {
    var pattern = $@"```{Regex.Escape(language)}\r?\n(.*?)```";
    var match = Regex.Match(content, pattern, RegexOptions.Singleline);
    return match.Success ? match.Groups[1].Value.TrimEnd() : null;
  }

  [GeneratedRegex(@"^---\r?\n(.*?)\r?\n---", RegexOptions.Singleline)]
  private static partial Regex FrontmatterRegex();

  [GeneratedRegex(@"<!--\s*test:\s*(\S+)\s*-->")]
  private static partial Regex TestMarkerRegex();

  [GeneratedRegex(@"<!--\s*(?:disabled-)?test:\s*\S+\s*-->")]
  private static partial Regex TestBoundaryRegex();

  [GeneratedRegex(@"^## Documentation", RegexOptions.Multiline)]
  private static partial Regex DocsSectionRegex();

  [GeneratedRegex(@"^## Tests", RegexOptions.Multiline)]
  private static partial Regex TestsSectionRegex();

  [GeneratedRegex(@"```maxon\r?\n(.*?)```", RegexOptions.Singleline)]
  private static partial Regex MaxonCodeBlockRegex();

  [GeneratedRegex(@"```exitcode\r?\n(\d+)\r?\n```", RegexOptions.Singleline)]
  private static partial Regex ExitCodeBlockRegex();

  [GeneratedRegex(@"```maxoncstderr\r?\n(.*?)```", RegexOptions.Singleline)]
  private static partial Regex MaxoncStderrBlockRegex();

  [GeneratedRegex(@"<!--\s*Args:\s*(.+?)\s*-->")]
  private static partial Regex ArgsDirectiveRegex();

  [GeneratedRegex(@"<!--\s*MmTrace\s*-->")]
  private static partial Regex MmTraceDirectiveRegex();

  /// Group 1 is the reason, empty for the bare spelling — which is refused, not tolerated.
  [GeneratedRegex(@"<!--\s*SelfhostedOnly\s*:?(.*?)\s*-->")]
  private static partial Regex SelfhostedOnlyDirectiveRegex();

  [GeneratedRegex(@"<!--\s*targets:\s*(.*?)\s*-->")]
  private static partial Regex TargetsDirectiveRegex();

  [GeneratedRegex(@"<!--\s*AsyncTrace\s*-->")]
  private static partial Regex AsyncTraceDirectiveRegex();

  [GeneratedRegex(@"<!--\s*DebugInfo\s*-->")]
  private static partial Regex DebugInfoDirectiveRegex();

  [GeneratedRegex(@"<!--\s*TimeoutMs:\s*(\d+)\s*-->")]
  private static partial Regex TimeoutMsDirectiveRegex();

  [GeneratedRegex(@"^// --- file:\s*(.+)$", RegexOptions.Multiline)]
  private static partial Regex FileMarkerRegex();

  [GeneratedRegex(@"```([a-zA-Z][\w:\-]*)\r?\n", RegexOptions.Multiline)]
  private static partial Regex CodeBlockLanguageRegex();
}
