using System.Text;

namespace MaxonSharp.Testing;

/// <summary>
/// Generates .test fragment files from spec files.
/// </summary>
public static class FragmentGenerator {
	/// <summary>
	/// Generate fragment files from all specs in the given directory.
	/// </summary>
	/// <param name="specDir">Directory containing spec files</param>
	/// <param name="fragmentDir">Directory to output fragment files</param>
	/// <param name="force">Force regeneration even if up to date</param>
	/// <returns>Number of fragments generated</returns>
	public static int GenerateFragments(string specDir, string fragmentDir, bool force = false) {
		if (!Directory.Exists(specDir)) {
			Console.Error.WriteLine($"Spec directory not found: {specDir}");
			return 0;
		}

		Directory.CreateDirectory(fragmentDir);

		var specs = SpecParser.ParseDirectory(specDir);
		var generated = 0;
		var workerCount = Math.Max(1, Environment.ProcessorCount / 2);

		Parallel.ForEach(specs, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, spec => {
			var specFile = new FileInfo(spec.FilePath);
			var featureFragmentDir = Path.Combine(fragmentDir, spec.Feature);
			Directory.CreateDirectory(featureFragmentDir);

			foreach (var test in spec.Tests) {
				var fragmentPath = Path.Combine(featureFragmentDir, $"{test.Name}.test");
				var fragmentFile = new FileInfo(fragmentPath);

				// Skip if fragment is newer than spec (unless force)
				if (!force && fragmentFile.Exists && fragmentFile.LastWriteTimeUtc > specFile.LastWriteTimeUtc) {
					continue;
				}

				var content = GenerateFragmentContent(test);
				File.WriteAllText(fragmentPath, content);
				Interlocked.Increment(ref generated);
			}
		});

		return generated;
	}

	private static string GenerateFragmentContent(TestCase test) {
		var sb = new StringBuilder();
		sb.AppendLine($"// Test: {test.Name}");
		sb.AppendLine(test.Source);
		sb.AppendLine("---");

		if (test.Expectation is SuccessExpectation success) {
			if (success.ExitCode.HasValue) {
				sb.AppendLine($"ExitCode: {success.ExitCode.Value}");
			}
			if (success.Stdout != null) {
				sb.AppendLine("Stdout: ```");
				sb.AppendLine(success.Stdout);
				sb.AppendLine("```");
			}
			if (success.ExpectedHir != null) {
				sb.AppendLine("ExpectedHIR: ```");
				sb.AppendLine(success.ExpectedHir);
				sb.AppendLine("```");
			}
			if (success.ExpectedLir != null) {
				sb.AppendLine("ExpectedLIR: ```");
				sb.AppendLine(success.ExpectedLir);
				sb.AppendLine("```");
			}
		} else if (test.Expectation is CompilerErrorExpectation error) {
			sb.AppendLine($"ExpectedError: {error.ExpectedError}");
		}

		sb.AppendLine("---");

		return sb.ToString();
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

		// Extract source code (between first line and first ---)
		var sourceLines = lines[1..separatorIndex];
		var source = string.Join("\n", sourceLines).Trim();

		// Parse expectations (between first --- and second ---)
		var secondSeparatorIndex = Array.FindIndex(lines, separatorIndex + 1, l => l.Trim() == "---");
		if (secondSeparatorIndex < 0) {
			secondSeparatorIndex = lines.Length;
		}

		var expectationSection = string.Join("\n", lines[(separatorIndex + 1)..secondSeparatorIndex]);
		var expectation = ParseExpectation(expectationSection);

		return new Fragment {
			FilePath = fragmentPath,
			TestName = testName,
			Source = source,
			Expectation = expectation
		};
	}

	private static TestExpectation ParseExpectation(string section) {
		var lines = section.Split('\n');
		int? exitCode = null;
		string? stdout = null;
		string? expectedHir = null;
		string? expectedLir = null;
		string? expectedError = null;

		var i = 0;
		while (i < lines.Length) {
			var line = lines[i].Trim();

			if (line.StartsWith("ExitCode:")) {
				var value = line["ExitCode:".Length..].Trim();
				if (int.TryParse(value, out var code)) {
					exitCode = code;
				}
			} else if (line.StartsWith("ExpectedError:")) {
				expectedError = line["ExpectedError:".Length..].Trim();
			} else if (line.StartsWith("Stdout: ```")) {
				stdout = ExtractMultilineValue(lines, ref i);
			} else if (line.StartsWith("ExpectedHIR: ```")) {
				expectedHir = ExtractMultilineValue(lines, ref i);
			} else if (line.StartsWith("ExpectedLIR: ```")) {
				expectedLir = ExtractMultilineValue(lines, ref i);
			}

			i++;
		}

		if (expectedError != null) {
			return new CompilerErrorExpectation {
				ExpectedError = expectedError
			};
		}

		return new SuccessExpectation {
			ExitCode = exitCode,
			Stdout = stdout,
			ExpectedHir = expectedHir,
			ExpectedLir = expectedLir
		};
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
}
