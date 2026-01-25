using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MaxonSharp.Testing;

/// <summary>
/// Executes tests from fragment files.
/// </summary>
public class TestRunner(string specDir, string fragmentDir, string tempDir, string? filter = null, int? workers = null) {
	private readonly string _specDir = specDir;
	private readonly string _fragmentDir = fragmentDir;
	private readonly string _tempDir = tempDir;
	private readonly string? _filter = filter;
	private readonly int _workerCount = workers ?? Math.Max(1, Environment.ProcessorCount / 2);

	/// <summary>
	/// Run all tests and return summary.
	/// </summary>
	public TestSummary RunAllSpecTests() {
		var sw = Stopwatch.StartNew();

		// Regenerate fragments if specs changed
		var genResult = FragmentGenerator.GenerateFragments(_specDir, _fragmentDir);
		if (genResult.Generated > 0) {
			Logger.Info(LogCategory.Testing, $"Generated {genResult.Generated} fragment(s)");
		}

		// Abort if fragment generation had errors
		if (genResult.Errors > 0) {
			Logger.Error(LogCategory.Testing, $"Fragment generation failed with {genResult.Errors} error(s). Fix spec files before running tests.");
			return new TestSummary {
				Results = [],
				Passed = 0,
				Failed = genResult.Errors,
				Total = genResult.Errors,
				TotalDuration = sw.Elapsed
			};
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

			if (result.Passed) {
				// Passing tests only show at debug level
				Logger.Debug(LogCategory.Testing, $"[PASS] {result.TestName}");
			} else {
				// Failing tests show at error level (always visible)
				Logger.Error(LogCategory.Testing, $"[FAIL] {result.TestName}");
				if (result.ErrorMessage != null) {
					Logger.Error(LogCategory.Testing, $"  {result.ErrorMessage}");
				}
			}
		});

		sw.Stop();

		// Clean up generated .exe files in fragment directories
		CleanupExecutables(_fragmentDir);

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

	/// <summary>
	/// Recursively clean up .exe files from the fragment directory and its subdirectories.
	/// </summary>
	private static void CleanupExecutables(string directory) {
		if (!Directory.Exists(directory)) return;

		try {
			// Delete .exe files in this directory
			foreach (var exeFile in Directory.GetFiles(directory, "*.exe")) {
				try {
					File.Delete(exeFile);
				} catch {
					// Ignore deletion errors (file may be locked)
				}
			}

			// Recurse into subdirectories
			foreach (var subDir in Directory.GetDirectories(directory)) {
				CleanupExecutables(subDir);
			}
		} catch {
			// Ignore directory access errors
		}
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
			// Check for pre-compiled executable alongside the fragment
			var fragmentDir = Path.GetDirectoryName(fragment.FilePath)!;
			var precompiledExe = Path.Combine(fragmentDir, $"{fragment.TestName}.exe");
			var fragmentFile = new FileInfo(fragment.FilePath);

			// Determine which executable to use
			string exePath;
			bool needsCleanup;
			string? compileError = null;

			if (File.Exists(precompiledExe)) {
				var exeFile = new FileInfo(precompiledExe);
				if (exeFile.LastWriteTimeUtc >= fragmentFile.LastWriteTimeUtc) {
					// Use pre-compiled executable
					exePath = precompiledExe;
					needsCleanup = false;
				} else {
					// Exe is stale, compile fresh
					var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");
					var result = CompileToExecutable(fragment, tempExe);
					compileError = result.Error;
					exePath = tempExe;
					needsCleanup = true;
				}
			} else {
				// No pre-compiled exe, compile fresh
				var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");
				var result = CompileToExecutable(fragment, tempExe);
				compileError = result.Error;
				exePath = tempExe;
				needsCleanup = true;
			}

			try {
				if (fragment.Expectation is CompilerErrorExpectation errorExpectation) {
					// Expect compilation to fail - need to compile to check for error
					if (compileError == null && !needsCleanup) {
						// Pre-compiled exe exists, but we expect an error - compile to get the error
						var tempExe = Path.Combine(_tempDir, $"{fragment.TestName}_{Environment.CurrentManagedThreadId}.exe");
						var result = CompileToExecutable(fragment, tempExe);
						compileError = result.Error;
						if (File.Exists(tempExe)) {
							try { File.Delete(tempExe); } catch { /* ignore */ }
						}
					}

					var compiledSuccessfully = compileError == null;
					if (compiledSuccessfully) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = "Expected compiler error but compilation succeeded",
							Duration = sw.Elapsed
						};
					}

					if (compileError != null && !compileError.Contains(errorExpectation.ExpectedError)) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"Expected error containing '{errorExpectation.ExpectedError}', got: {compileError}",
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
				if (compileError != null) {
					return new TestResult {
						TestName = fragment.TestName,
						Passed = false,
						ErrorMessage = $"Compilation failed: {compileError}",
						Duration = sw.Elapsed
					};
				}

				var successExpectation = (SuccessExpectation)fragment.Expectation;

				// Check Required MLIR using GeneratedIr from fragment
				if (successExpectation.RequiredMlir != null) {
					if (fragment.GeneratedIr == null) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = "RequiredMlir specified but no generated IR found in fragment",
							Duration = sw.Elapsed
						};
					}

					var (Passed, Message) = CheckRequiredIr(successExpectation.RequiredMlir, fragment.GeneratedIr);
					if (!Passed) {
						return new TestResult {
							TestName = fragment.TestName,
							Passed = false,
							ErrorMessage = $"Required MLIR not found: {Message}",
							Duration = sw.Elapsed
						};
					}
				}

				// Run the executable if we have runtime expectations
				if (successExpectation.ExitCode.HasValue || successExpectation.Stdout != null) {
					var (ExitCode, Stdout, Stderr) = RunExecutable(exePath);

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
				// Cleanup temp file if we created one
				if (needsCleanup && File.Exists(exePath)) {
					try { File.Delete(exePath); } catch { /* ignore */ }
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
			var result = Compiler.Compiler.CompileWithMlir(
				[new Compiler.SourceFile(fragment.FilePath, fragment.Source)],
				outputPath);
			return (result.Success, result.Error);
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

	private static string NormalizeIr(string ir) {
		// Trim each line, remove empty lines, normalize line endings
		var lines = ir.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(l => l.Trim())
			.Where(l => l.Length > 0);
		return string.Join("\n", lines);
	}

	private static (bool Passed, string? Message) CheckRequiredIr(string required, string actual) {
		// Normalize both for exact match
		var requiredNorm = NormalizeIr(required);
		var actualNorm = NormalizeIr(actual);

		if (actualNorm == requiredNorm) {
			return (true, null);
		}

		// Show what we expected vs what we got
		return (false, $"IR mismatch.\nExpected:\n{requiredNorm}\n\nActual:\n{actualNorm}");
	}
}
