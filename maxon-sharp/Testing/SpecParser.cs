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
	/// </summary>
	public static List<SpecFile> ParseDirectory(string specDir) {
		var specs = new List<SpecFile>();

		foreach (var file in Directory.GetFiles(specDir, "*.md")) {
			try {
				var spec = Parse(file);
				specs.Add(spec);
			} catch (Exception ex) {
				Logger.Error(LogCategory.Testing, $"Failed to parse {file}: {ex.Message}");
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

			// Find the next test marker or end of content
			var nextTestMatch = TestMarkerRegex().Match(content, startIndex);
			var endIndex = nextTestMatch.Success ? nextTestMatch.Index : content.Length;
			var testSection = content[startIndex..endIndex];

			var source = ExtractCodeBlock(testSection, "maxon");
			if (source == null) continue;

			var exitCode = ExtractCodeBlock(testSection, "exitcode");
			var stdout = ExtractCodeBlock(testSection, "stdout");
			var stderr = ExtractCodeBlock(testSection, "maxoncstderr");

			// MLIR blocks (new format)
			var expectedMlir = ExtractCodeBlock(testSection, "expectedmlir");
			var requiredMlir = ExtractCodeBlock(testSection, "requiredmlir");

			TestExpectation expectation;
			if (stderr != null) {
				expectation = new CompilerErrorExpectation {
					ExpectedError = stderr
				};
			} else {
				expectation = new SuccessExpectation {
					ExitCode = exitCode != null ? int.Parse(exitCode.Trim()) : null,
					Stdout = stdout,
					ExpectedMlir = expectedMlir,
					RequiredMlir = requiredMlir
				};
			}

			tests.Add(new TestCase {
				Name = testName,
				Source = source,
				Expectation = expectation
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
			foreach (Match codeMatch in MaxonCodeBlockRegex().Matches(docsSection)) {
				var code = codeMatch.Groups[1].Value;
				// Only include examples that have a main function (executable)
				if (code.Contains("function main()")) {
					exampleIndex++;
					tests.Add(new TestCase {
						Name = $"docs-example-{exampleIndex}",
						Source = code,
						Expectation = new SuccessExpectation {
							ExitCode = 0
						}
					});
				}
			}
		}

		return tests;
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

	[GeneratedRegex(@"^## Documentation", RegexOptions.Multiline)]
	private static partial Regex DocsSectionRegex();

	[GeneratedRegex(@"^## Tests", RegexOptions.Multiline)]
	private static partial Regex TestsSectionRegex();

	[GeneratedRegex(@"```maxon\r?\n(.*?)```", RegexOptions.Singleline)]
	private static partial Regex MaxonCodeBlockRegex();
}
