using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Parses markdown spec files to extract tests.
/// </summary>
public static partial class SpecParser {
  /// <summary>
  /// Parse a spec file and extract all tests.
  /// </summary>
  public static SpecFile Parse(string filePath) {
    var content = File.ReadAllText(filePath);
    var (feature, status, category) = ParseFrontmatter(content);
    var tests = ExtractTests(content);

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
  /// Skips specs with status: draft.
  /// </summary>
  public static List<SpecFile> ParseDirectory(string specDir) {
    var specs = new List<SpecFile>();

    foreach (var file in Directory.GetFiles(specDir, "*.md")) {
      try {
        var spec = Parse(file);
        if (spec.Status == "draft") {
          Logger.Debug(LogCategory.Testing, $"Skipping draft spec: {Path.GetFileName(file)}");
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

  private static List<TestCase> ExtractTests(string content) {
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

      bool trackMemory = false;
      var trackMemMatch = TrackMemoryDirectiveRegex().Match(testSection);
      if (trackMemMatch.Success) {
        trackMemory = trackMemMatch.Groups[1].Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
      }

      var source = ExtractCodeBlock(testSection, "maxon");
      if (source == null) continue;

      ValidateCodeBlockLanguages(testName, testSection);

      var exitCode = ExtractCodeBlock(testSection, "exitcode");
      var stdout = ExtractCodeBlock(testSection, "stdout");
      var stderr = ExtractCodeBlock(testSection, "maxoncstderr");

      var requiredMLIR = ExtractCodeBlock(testSection, "RequiredMLIR");
      var requiredRdata = ExtractCodeBlock(testSection, "RequiredRdata");
      var requiredData = ExtractCodeBlock(testSection, "RequiredData");

      TestExpectation expectation;
      if (stderr != null) {
        expectation = new CompilerErrorExpectation {
          ExpectedStderr = stderr
        };
      } else {
        if (exitCode == null && stdout == null && requiredMLIR == null && requiredRdata == null) {
          throw new Exception(
            $"Test '{testName}' has a maxon block but no result checks. " +
            "Add an exitcode, stdout, maxoncstderr, RequiredMLIR, or RequiredRdata block.");
        }
        expectation = new SuccessExpectation {
          ExitCode = exitCode != null ? int.Parse(exitCode.Trim()) : null,
          Stdout = stdout,
          RequiredMLIR = requiredMLIR,
          RequiredRdata = requiredRdata,
          RequiredData = requiredData,
          TrackMemory = trackMemory
        };
      }

      tests.Add(new TestCase {
        Name = testName,
        Source = source,
        Expectation = expectation,
        Args = testArgs,
        TrackMemory = trackMemory,
        SourceFiles = SplitMultiFileSource(source)
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
      var start = matches[i].Index + matches[i].Length;
      var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
      var fileSource = source[start..end].Trim();
      files.Add((fileName, fileSource));
    }

    return files.Count > 0 ? files : null;
  }

  private static readonly HashSet<string> KnownCodeBlockLanguages = [
    "maxon", "exitcode", "stdout", "maxoncstderr", "RequiredMLIR", "RequiredRdata"
  ];

  private static void ValidateCodeBlockLanguages(string testName, string testSection) {
    foreach (Match match in CodeBlockLanguageRegex().Matches(testSection)) {
      var language = match.Groups[1].Value;
      if (!KnownCodeBlockLanguages.Contains(language)) {
        throw new Exception(
          $"Test '{testName}' has unrecognized code block language '{language}'. " +
          $"Valid languages: {string.Join(", ", KnownCodeBlockLanguages)}");
      }
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

  [GeneratedRegex(@"<!--\s*TrackMemory:\s*(.+?)\s*-->")]
  private static partial Regex TrackMemoryDirectiveRegex();

  [GeneratedRegex(@"^// --- file:\s*(.+)$", RegexOptions.Multiline)]
  private static partial Regex FileMarkerRegex();

  [GeneratedRegex(@"```([a-zA-Z]\w*)\r?\n", RegexOptions.Multiline)]
  private static partial Regex CodeBlockLanguageRegex();
}
