using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Executes tests from fragment files.
/// </summary>
public class TestRunner(string specDir, string fragmentDir, string tempDir, bool verbose = false, string? filter = null, int? workers = null) {
	private readonly string _specDir = specDir;
	private readonly string _fragmentDir = fragmentDir;
	private readonly string _tempDir = tempDir;
	private readonly bool _verbose = verbose;
	private readonly string? _filter = filter;
	private readonly int _workerCount = workers ?? Math.Max(1, Environment.ProcessorCount / 2);

	/// <summary>
	/// Run all tests and return summary.
	/// </summary>
	public TestSummary RunAllTests() {
		var sw = Stopwatch.StartNew();

		// Regenerate fragments if specs changed
		Logger.Info(LogCategory.Testing, "Checking for spec changes...");
		var generated = FragmentGenerator.GenerateFragments(_specDir, _fragmentDir);
		if (generated > 0) {
			Logger.Info(LogCategory.Testing, $"Generated {generated} fragment(s)");
		}

		// Load all fragments
		var fragments = LoadAllFragments();
		if (_filter != null) {
			var filterRegex = new Regex(_filter, RegexOptions.IgnoreCase);
			fragments = [.. fragments.Where(f => filterRegex.IsMatch(f.TestName))];
		}

		if (fragments.Count == 0) {
			Logger.Info(LogCategory.Testing, "No tests found");
			return new TestSummary {
				Results = [],
				Passed = 0,
				Failed = 0,
				Total = 0,
				TotalDuration = sw.Elapsed
			};
		}

		Logger.Info(LogCategory.Testing, $"Running {fragments.Count} test(s) with {_workerCount} worker(s)...");

		// Ensure temp directory exists
		Directory.CreateDirectory(_tempDir);

		// Run tests in parallel
		var results = new ConcurrentBag<TestResult>();
		Parallel.ForEach(fragments, new ParallelOptions { MaxDegreeOfParallelism = _workerCount }, fragment => {
			var result = RunTest(fragment);
			results.Add(result);

			if (_verbose || !result.Passed) {
				var status = result.Passed ? "PASS" : "FAIL";
				if (result.Passed) {
					Logger.Info(LogCategory.Testing, $"[{status}] {result.TestName}");
				} else {
					Logger.Error(LogCategory.Testing, $"[{status}] {result.TestName}");
					if (result.ErrorMessage != null) {
						Logger.Error(LogCategory.Testing, $"  {result.ErrorMessage}");
					}
				}
			}
		});

		sw.Stop();

		var resultList = results.ToList();
		var passed = resultList.Count(r => r.Passed);
		var failed = resultList.Count(r => !r.Passed);

		return new TestSummary {
			Results = resultList,
			Passed = passed,
			Failed = failed,
			Total = resultList.Count,
			TotalDuration = sw.Elapsed
		};
	}

	private List<Fragment> LoadAllFragments() {
		var fragments = new List<Fragment>();

		if (!Directory.Exists(_fragmentDir)) {
			return fragments;
		}

		foreach (var file in Directory.GetFiles(_fragmentDir, "*.test", SearchOption.AllDirectories)) {
			var fragment = FragmentGenerator.ParseFragment(file);
			if (fragment != null) {
				fragments.Add(fragment);
			}
		}

		return fragments;
	}

	private TestResult RunTest(Fragment fragment) {
		var sw = Stopwatch.StartNew();

		try {
			// Create a unique temp file for this test
			var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");

			try {
				// Compile the source
				var (Success, Error) = CompileToExecutable(fragment, tempExe);

				if (fragment.Expectation is CompilerErrorExpectation errorExpectation) {
					// Expect compilation to fail
					if (Success) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = "Expected compiler error but compilation succeeded",
							Duration = sw.Elapsed
						};
					}

					if (Error != null && !Error.Contains(errorExpectation.ExpectedError)) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"Expected error containing '{errorExpectation.ExpectedError}', got: {Error}",
							Duration = sw.Elapsed
						};
					}

					return new TestResult {
						TestName = fragment.TestName,
						Passed = true,
						Duration = sw.Elapsed
					};
				}

				// Expect compilation to succeed
				if (!Success) {
					return new TestResult {
						TestName = fragment.TestName,
						Passed = false,
						ErrorMessage = $"Compilation failed: {Error}",
						Duration = sw.Elapsed
					};
				}

				var successExpectation = (SuccessExpectation)fragment.Expectation;

				// Check HIR if expected
				if (successExpectation.ExpectedHir != null) {
					var hirResult = Compiler.Compiler.CompileToIr(fragment.Source);
					if (!hirResult.Success) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"Failed to generate HIR: {hirResult.Error}",
							Duration = sw.Elapsed
						};
					}

					var (Passed, Message) = CompareIr(successExpectation.ExpectedHir, hirResult.Hir!);
					if (!Passed) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"HIR mismatch: {Message}",
							Duration = sw.Elapsed
						};
					}
				}

				// Check LIR if expected
				if (successExpectation.ExpectedLir != null) {
					var lirResult = Compiler.Compiler.CompileToIr(fragment.Source);
					if (!lirResult.Success) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"Failed to generate LIR: {lirResult.Error}",
							Duration = sw.Elapsed
						};
					}

					var (Passed, Message) = CompareIr(successExpectation.ExpectedLir, lirResult.Lir!);
					if (!Passed) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"LIR mismatch: {Message}",
							Duration = sw.Elapsed
						};
					}
				}

				// Run the executable if we have runtime expectations
				if (successExpectation.ExitCode.HasValue || successExpectation.Stdout != null) {
					var (ExitCode, Stdout, Stderr) = RunExecutable(tempExe);

					if (successExpectation.ExitCode.HasValue && ExitCode != successExpectation.ExitCode.Value) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"Expected exit code {successExpectation.ExitCode.Value}, got {ExitCode}",
							Duration = sw.Elapsed
						};
					}

					if (successExpectation.Stdout != null) {
						var expectedStdout = successExpectation.Stdout.Trim();
						var actualStdout = Stdout.Trim();
						if (expectedStdout != actualStdout) {
							return new TestResult {
								TestName = fragment.TestName,
								Passed = false,
								ErrorMessage = $"Stdout mismatch:\nExpected: {expectedStdout}\nActual: {actualStdout}",
								Duration = sw.Elapsed
							};
						}
					}
				}

				return new TestResult {
					TestName = fragment.TestName,
					Passed = true,
					Duration = sw.Elapsed
				};
			} finally {
				// Cleanup temp file
				if (File.Exists(tempExe)) {
					try { File.Delete(tempExe); } catch { /* ignore */ }
				}
			}
		} catch (Exception ex) {
			return new TestResult {
				TestName = fragment.TestName,
				Passed = false,
				ErrorMessage = $"Exception: {ex.Message}",
				Duration = sw.Elapsed
			};
		}
	}

	private static (bool Success, string? Error) CompileToExecutable(Fragment fragment, string outputPath) {
		try {
			var success = Compiler.Compiler.Compile([new Compiler.SourceFile(fragment.FilePath, fragment.Source)], outputPath);
			return (success, success ? null : "Compilation failed");
		} catch (Exception ex) {
			return (false, ex.Message);
		}
	}

	private static (int ExitCode, string Stdout, string Stderr) RunExecutable(string exePath) {
		var psi = new ProcessStartInfo {
			FileName = exePath,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		using var process = Process.Start(psi)!;
		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		process.WaitForExit(TimeSpan.FromMilliseconds(30000));

		return (process.ExitCode, stdout, stderr);
	}

	private static (bool Passed, string? Message) CompareIr(string expected, string actual) {
		// Normalize whitespace for comparison
		var expectedNorm = NormalizeIr(expected);
		var actualNorm = NormalizeIr(actual);

		if (expectedNorm == actualNorm) {
			return (true, null);
		}

		// Find first difference
		var expectedLines = expectedNorm.Split('\n');
		var actualLines = actualNorm.Split('\n');

		for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++) {
			var expLine = i < expectedLines.Length ? expectedLines[i] : "<missing>";
			var actLine = i < actualLines.Length ? actualLines[i] : "<missing>";

			if (expLine != actLine) {
				return (false, $"Line {i + 1}: expected '{expLine}', got '{actLine}'");
			}
		}

		return (false, "Unknown difference");
	}

	private static string NormalizeIr(string ir) {
		// Trim each line, remove empty lines, normalize line endings
		var lines = ir.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(l => l.Trim())
			.Where(l => l.Length > 0);
		return string.Join("\n", lines);
	}
}
