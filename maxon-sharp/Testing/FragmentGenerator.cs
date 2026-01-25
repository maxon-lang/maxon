using System.Text;

namespace MaxonSharp.Testing;

/// <summary>
/// Result of fragment generation.
/// </summary>
public record FragmentGenerationResult(int Generated, int Errors);

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
	/// <returns>Result with number of fragments generated and error count</returns>
	public static FragmentGenerationResult GenerateFragments(string specDir, string fragmentDir, bool force = false) {
		if (!Directory.Exists(specDir)) {
			Logger.Error(LogCategory.Testing, $"Spec directory not found: {specDir}");
			return new FragmentGenerationResult(0, 1);
		}

		Directory.CreateDirectory(fragmentDir);

		var specs = SpecParser.ParseDirectory(specDir);
		var generated = 0;
		var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
		var workerCount = Math.Max(1, Environment.ProcessorCount / 2);

		Parallel.ForEach(specs, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, spec => {
			var specFile = new FileInfo(spec.FilePath);
			var featureFragmentDir = Path.Combine(fragmentDir, spec.Feature);
			Directory.CreateDirectory(featureFragmentDir);

			foreach (var test in spec.Tests) {
				var fragmentPath = Path.Combine(featureFragmentDir, $"{test.Name}.test");
				var exePath = Path.Combine(featureFragmentDir, $"{test.Name}.exe");
				var fragmentFile = new FileInfo(fragmentPath);

				// Skip if fragment is newer than spec (unless force)
				if (!force && fragmentFile.Exists && fragmentFile.LastWriteTimeUtc > specFile.LastWriteTimeUtc) {
					continue;
				}

				var (content, error) = GenerateFragmentContent(test, exePath);
				if (error != null) {
					errors.Add($"{test.Name}: {error}");
				}
				File.WriteAllText(fragmentPath, content);
				Interlocked.Increment(ref generated);
			}
		});

		// Report any compilation errors encountered during fragment generation
		foreach (var error in errors) {
			Logger.Error(LogCategory.Testing, $"Fragment generation error: {error}");
		}

		return new FragmentGenerationResult(generated, errors.Count);
	}

	private static (string Content, string? Error) GenerateFragmentContent(TestCase test, string exePath) {
		var sb = new StringBuilder();
		string? error = null;

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

			// Write required MLIR if specified
			if (success.RequiredMlir != null) {
				sb.AppendLine("RequiredMlir: ```");
				sb.AppendLine(success.RequiredMlir);
				sb.AppendLine("```");
			}
		} else if (test.Expectation is CompilerErrorExpectation compilerError) {
			sb.AppendLine($"ExpectedError: {compilerError.ExpectedError}");
		}

		sb.AppendLine("---");

		// Compile to executable and capture IR (for success expectations only)
		if (test.Expectation is SuccessExpectation) {
			var sources = new[] { new Compiler.SourceFile("test.mx", test.Source) };
			var result = Compiler.Compiler.CompileWithMlir(sources, exePath, returnIr: true);
			if (result.Success) {
				sb.AppendLine("// Generated X86 IR:");
				sb.AppendLine(result.X86Ir);
			} else {
				sb.AppendLine($"// Compilation failed: {result.Error}");
				error ??= result.Error;
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

		// Parse generated IR (between second --- and third ---)
		string? generatedIr = null;
		if (secondSeparatorIndex < lines.Length) {
			var thirdSeparatorIndex = Array.FindIndex(lines, secondSeparatorIndex + 1, l => l.Trim() == "---");
			if (thirdSeparatorIndex < 0) {
				thirdSeparatorIndex = lines.Length;
			}
			var irSection = lines[(secondSeparatorIndex + 1)..thirdSeparatorIndex];
			generatedIr = ExtractGeneratedIr(irSection);
		}

		return new Fragment {
			FilePath = fragmentPath,
			TestName = testName,
			Source = source,
			Expectation = expectation,
			GeneratedIr = generatedIr
		};
	}

	private static string? ExtractGeneratedIr(string[] lines) {
		var sb = new StringBuilder();
		var foundIr = false;

		foreach (var line in lines) {
			if (line.TrimStart().StartsWith("// Generated X86 IR:")) {
				foundIr = true;
				continue;
			}
			if (line.TrimStart().StartsWith("// Compilation failed:")) {
				return null;
			}
			if (foundIr) {
				sb.AppendLine(line);
			}
		}

		if (!foundIr) {
			return null;
		}

		return sb.ToString().TrimEnd();
	}

	private static TestExpectation ParseExpectation(string section) {
		var lines = section.Split('\n');
		int? exitCode = null;
		string? stdout = null;
		string? expectedMlir = null;
		string? requiredMlir = null;
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
			} else if (line.StartsWith("ExpectedMlir: ```")) {
				expectedMlir = ExtractMultilineValue(lines, ref i);
			} else if (line.StartsWith("RequiredMlir: ```")) {
				requiredMlir = ExtractMultilineValue(lines, ref i);
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
			ExpectedMlir = expectedMlir,
			RequiredMlir = requiredMlir
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
