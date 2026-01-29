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
	private const string SpecCountFileName = ".spec_count";

	/// <summary>
	/// Get the modification time of the compiler executable.
	/// </summary>
	private static DateTime GetCompilerMtime() {
		var exePath = Environment.ProcessPath;
		if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath)) {
			return new FileInfo(exePath).LastWriteTimeUtc;
		}
		// If we can't determine compiler mtime, return max value to force regeneration
		return DateTime.MaxValue;
	}

	/// <summary>
	/// Check if fragments need regeneration based on the .spec_count flag file.
	/// Returns true if regeneration is needed.
	/// </summary>
	private static bool NeedsRegeneration(string fragmentDir, int specCount, int testCount) {
		var flagPath = Path.Combine(fragmentDir, SpecCountFileName);
		if (!File.Exists(flagPath)) {
			return true;
		}

		try {
			var content = File.ReadAllText(flagPath).Trim();
			var parts = content.Split(':');
			if (parts.Length != 2) return true;

			if (!int.TryParse(parts[0], out var savedSpecCount)) return true;
			if (!int.TryParse(parts[1], out var savedTestCount)) return true;

			return savedSpecCount != specCount || savedTestCount != testCount;
		} catch {
			return true;
		}
	}

	/// <summary>
	/// Write the .spec_count flag file to indicate successful generation.
	/// </summary>
	private static void WriteSpecCountFlag(string fragmentDir, int specCount, int testCount) {
		var flagPath = Path.Combine(fragmentDir, SpecCountFileName);
		File.WriteAllText(flagPath, $"{specCount}:{testCount}");
	}

	/// <summary>
	/// Delete the .spec_count flag file (on generation failure).
	/// </summary>
	private static void DeleteSpecCountFlag(string fragmentDir) {
		var flagPath = Path.Combine(fragmentDir, SpecCountFileName);
		if (File.Exists(flagPath)) {
			try { File.Delete(flagPath); } catch { /* ignore */ }
		}
	}

	/// <summary>
	/// Clean the fragments directory by deleting and recreating it.
	/// </summary>
	private static void CleanFragmentsDirectory(string fragmentDir) {
		if (Directory.Exists(fragmentDir)) {
			try { Directory.Delete(fragmentDir, recursive: true); } catch { /* ignore */ }
		}
		Directory.CreateDirectory(fragmentDir);
	}

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
		var totalTests = specs.Sum(s => s.Tests.Count);

		// Check if we need to regenerate based on spec/test count changes
		if (NeedsRegeneration(fragmentDir, specs.Count, totalTests)) {
			// Clean the fragments directory to remove stale fragments
			CleanFragmentsDirectory(fragmentDir);
			force = true;
		}

		var generated = 0;
		var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
		var workerCount = Math.Max(1, Environment.ProcessorCount / 2);
		var compilerMtime = GetCompilerMtime();

		Parallel.ForEach(specs, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, spec => {
			var specFile = new FileInfo(spec.FilePath);
			var specName = Path.GetFileNameWithoutExtension(spec.FilePath);
			var specFragmentDir = Path.Combine(fragmentDir, specName);
			Directory.CreateDirectory(specFragmentDir);

			foreach (var test in spec.Tests) {
				var fragmentPath = Path.Combine(specFragmentDir, $"{test.Name}.test");
				var exePath = Path.Combine(specFragmentDir, $"{test.Name}.exe");
				var fragmentFile = new FileInfo(fragmentPath);

				// Skip if fragment is newer than both spec and compiler (unless force)
				if (!force && fragmentFile.Exists &&
					fragmentFile.LastWriteTimeUtc > specFile.LastWriteTimeUtc &&
					fragmentFile.LastWriteTimeUtc > compilerMtime) {
					continue;
				}

				// Pass absolute path - CompileError.Format() will make it relative to ProjectRoot
				var absolutePath = Path.GetFullPath(fragmentPath);
				var (content, error) = GenerateFragmentContent(test, exePath, absolutePath);
				if (error != null) {
					errors.Add(error);
				}
				File.WriteAllText(fragmentPath, content.Replace("\r\n", "\n").Replace("\r", "\n"));
				Interlocked.Increment(ref generated);
			}
		});

		// Report any compilation errors encountered during fragment generation
		foreach (var error in errors) {
			Logger.Error(LogCategory.Testing, error);
		}

		// Update or delete the flag file based on success/failure
		if (errors.IsEmpty) {
			WriteSpecCountFlag(fragmentDir, specs.Count, totalTests);
		} else {
			DeleteSpecCountFlag(fragmentDir);
		}

		return new FragmentGenerationResult(generated, errors.Count);
	}

	private static (string Content, string? Error) GenerateFragmentContent(TestCase test, string exePath, string fragmentPath) {
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
			sb.AppendLine("MaxoncStderr: ```");
			sb.AppendLine(compilerError.ExpectedStderr);
			sb.AppendLine("```");
		}

		sb.AppendLine("---");

		// Compile to executable and capture IR (for success expectations only)
		if (test.Expectation is SuccessExpectation) {
			var sources = new[] { new Compiler.SourceFile(fragmentPath, test.Source) };
			var result = Compiler.Compiler.Compile(sources, exePath, returnIr: true);
			if (result.Success) {
				sb.Append(result.X86Ir?.TrimEnd());
				sb.AppendLine();
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
		foreach (var line in lines) {
			if (line.TrimStart().StartsWith("// Compilation failed:")) {
				return null;
			}
		}

		return string.Join('\n', lines).TrimEnd();
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
			} else if (line.StartsWith("MaxoncStderr: ```")) {
				expectedError = ExtractMultilineValue(lines, ref i);
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
				ExpectedStderr = expectedError
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
