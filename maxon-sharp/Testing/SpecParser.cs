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
  /// Parse a spec file and extract all tests.
  /// When targetKey is provided (e.g. "x64-windows"), extracts RequiredIR:{targetKey} blocks.
  /// </summary>
  public static SpecFile Parse(string filePath, string? targetKey = null) {
    var content = File.ReadAllText(filePath);
    var (feature, status, category) = ParseFrontmatter(content);
    var tests = ExtractTests(content, targetKey);

    return new SpecFile {
      FilePath = filePath,
      Feature = feature,
      Status = status,
      Category = category,
      Tests = tests
    };
  }

  /// <summary>
  /// Parse all spec files in a directory.
  /// Skips specs with status: draft (work-in-progress) or status: selfhosted
  /// (written against self-hosted-only intrinsics like __mm_raw_alloc, which
  /// the C# bootstrap doesn't expose, or pinning RequiredIR where the two
  /// compilers' lowering diverges — one shared block can't satisfy both
  /// runners, so the spec is owned by the self-hosted suite).
  /// When targetKey is provided (e.g. "x64-windows"), extracts RequiredIR:{targetKey} blocks.
  /// </summary>
  public static List<SpecFile> ParseDirectory(string specDir, string? targetKey = null, bool includeNetwork = false) {
    var specs = new List<SpecFile>();

    foreach (var file in Directory.GetFiles(specDir, "*.md")) {
      try {
        var spec = Parse(file, targetKey);
        if (spec.Status == "draft") {
          Logger.Debug(LogCategory.Testing, $"Skipping draft spec: {Path.GetFileName(file)}");
          continue;
        }
        if (spec.Status == "selfhosted") {
          Logger.Debug(LogCategory.Testing, $"Skipping selfhosted-only spec: {Path.GetFileName(file)}");
          continue;
        }
        // A `category: network` spec talks to a real server on the public internet. That makes it
        // a coin toss on somebody else's uptime, not a gate on our compiler: httpbin.org has been
        // observed returning 503, and it rate-limits under the runner's parallelism, so the suite
        // goes red for reasons no change of ours caused. A gate that fails for reasons unrelated to
        // the code under test trains you to ignore it, which is worse than not having it.
        //
        // They are still real tests and they still run — `--network` opts in. They are just not part
        // of the default gate.
        if (!includeNetwork && spec.Category == NetworkCategory) {
          Logger.Debug(LogCategory.Testing, $"Skipping network spec (pass --network to include): {Path.GetFileName(file)}");
          continue;
        }
        specs.Add(spec);
      } catch (Exception ex) {
        Logger.Error(LogCategory.Testing, $"Failed to parse {file}: {ex.Message}\n{ex.StackTrace}");
      }
    }

    return specs;
  }

  private static (string feature, string status, string category) ParseFrontmatter(string content) {
    var match = FrontmatterRegex().Match(content);
    if (!match.Success) {
      return ("unknown", "unknown", "unknown");
    }

    var yaml = match.Groups[1].Value;
    var feature = ExtractYamlValue(yaml, "feature") ?? "unknown";
    var status = ExtractYamlValue(yaml, "status") ?? "unknown";
    var category = ExtractYamlValue(yaml, "category") ?? "unknown";

    return (feature, status, category);
  }

  private static string? ExtractYamlValue(string yaml, string key) {
    var match = Regex.Match(yaml, $@"^{key}:\s*(.+)$", RegexOptions.Multiline);
    return match.Success ? match.Groups[1].Value.Trim() : null;
  }

  private static List<TestCase> ExtractTests(string content, string? targetKey = null) {
    var tests = new List<TestCase>();

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

      // Tests that exercise a self-hosted-only diagnostic (e.g. E3095) can
      // opt out of the C# runner by emitting a `<!-- SelfhostedOnly -->`
      // directive between the test marker and its first fence.
      if (SelfhostedOnlyDirectiveRegex().IsMatch(testSection)) {
        Logger.Debug(LogCategory.Testing, $"Skipping selfhosted-only test: {testName}");
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

    return tests;
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

  [GeneratedRegex(@"<!--\s*SelfhostedOnly\s*-->")]
  private static partial Regex SelfhostedOnlyDirectiveRegex();

  [GeneratedRegex(@"<!--\s*targets:\s*(.*?)\s*-->")]
  private static partial Regex TargetsDirectiveRegex();

  [GeneratedRegex(@"<!--\s*AsyncTrace\s*-->")]
  private static partial Regex AsyncTraceDirectiveRegex();

  [GeneratedRegex(@"<!--\s*TimeoutMs:\s*(\d+)\s*-->")]
  private static partial Regex TimeoutMsDirectiveRegex();

  [GeneratedRegex(@"^// --- file:\s*(.+)$", RegexOptions.Multiline)]
  private static partial Regex FileMarkerRegex();

  [GeneratedRegex(@"```([a-zA-Z][\w:\-]*)\r?\n", RegexOptions.Multiline)]
  private static partial Regex CodeBlockLanguageRegex();
}
