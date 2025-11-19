namespace Test;

using System.Diagnostics;
using System.Text.RegularExpressions;
using NUnit.Framework;

// trying to embed the test code and expected IR in this file would be a huge PITA
// due to the way C# handles multiline strings (escaping, indentation, etc.)
// so we put the test code in separate files and read them here
// these "fragments" include the test code, the expected IR, and expected outputs
// if we are testing for parser or semantic errors then the code will not compile
// therefore there is no expected IR. In that case we put "N/A" in the fragment file

public partial class FragmentTests {
	private static readonly string fragmentsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "language-tests", "fragments");
	private static string[] Fragments() {
		return [.. Directory.EnumerateFiles(fragmentsPath, "*.test").Select(f => Path.GetFileName(f))];
	}
	private static readonly string docFragmentsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "language-tests", "doc-fragments");
	private static string[] DocFragments() {
		return [.. Directory.EnumerateFiles(docFragmentsPath, "*.test").Select(f => Path.GetFileName(f))];
	}

	[Test, TestCaseSource(nameof(DocFragments))]
	public void DocFragmentTest(string fragmentFilename) {
		TestFragment(fragmentFilename, docFragmentsPath, false, true);
	}

	[Test, TestCaseSource(nameof(Fragments))]
	public void FragmentTest(string fragmentFilename) {
		TestFragment(fragmentFilename, fragmentsPath, false, true);
	}

	// Helper method to read multiline content (supports both ``` delimited and keyword-delimited formats)
	private static string ReadMultilineContent(string[] lines, ref int i, string initialContent) {
		var content = initialContent.Trim();

		if (content == "```") {
			// Triple backtick delimited format
			var contentLines = new List<string>();
			for (var j = i + 1; j < lines.Length; j++) {
				var nextLine = lines[j].TrimEnd('\r');
				if (nextLine == "```") {
					i = j; // Advance to the closing delimiter
					break;
				}
				contentLines.Add(nextLine);
			}
			return string.Join(Environment.NewLine, contentLines).TrimEnd();
		} else {
			// Keyword-delimited or single-line format
			var contentLines = new List<string> { content };
			for (var j = i + 1; j < lines.Length; j++) {
				var nextLine = lines[j].TrimEnd('\r');
				if (nextLine.StartsWith("ExitCode: ") ||
					nextLine.StartsWith("MaxoncStdout: ") ||
					nextLine.StartsWith("MaxoncStderr: ") ||
					nextLine.StartsWith("Stdout: ") ||
					nextLine.StartsWith("Stderr: ")) {
					break;
				}
				contentLines.Add(nextLine);
				i = j; // Advance the outer loop counter
			}
			return string.Join(Environment.NewLine, contentLines).TrimEnd();
		}
	}

	private static void TestFragment(string fragmentFilename, string path, bool debug, bool optimize) {
		var fragmentPath = Path.Combine(path, fragmentFilename);
		var fragmentFile = File.OpenText(fragmentPath);

	var source = "";
	while (true) {
		var line = fragmentFile.ReadLine();
		if (line == null || line == "---") {
			break;
		}
		source += line + "\n";
	}

	// Dual IR format (no labels): OptimizedIR first, then UnoptimizedIR
	var expectedOptimizedIR = "";
	var expectedUnoptimizedIR = "";
	
	// Read optimized IR section (first section after source)
	while (true) {
		var line = fragmentFile.ReadLine();
		if (line == null || line == "---") {
			break;
		}
		expectedOptimizedIR += line + "\n";
	}
	expectedOptimizedIR = expectedOptimizedIR.Trim();
	
	// Read unoptimized IR section (second section)
	while (true) {
		var line = fragmentFile.ReadLine();
		if (line == null || line == "---") {
			break;
		}
		expectedUnoptimizedIR += line + "\n";
	}
	expectedUnoptimizedIR = expectedUnoptimizedIR.Trim();
	
	var expectedExitCode = -1;
	var expectedMaxoncStdout = "";
	var expectedMaxoncStderr = "";
	var expectedStdout = "";
	var expectedStderr = "";
	var expectedArgs = "";
	var expectedOptimizedInstructionCount = -1;
	var expectedUnoptimizedInstructionCount = -1;
		// Read existing metadata (Args, ExitCode, etc.) regardless of update mode
		// so we can preserve Args when updating IR
		var remainingContent = fragmentFile.ReadToEnd();
		var lines = remainingContent.Split('\n');

		for (var i = 0; i < lines.Length; i++) {
		var line = lines[i].TrimEnd('\r');
		if (line.StartsWith("ExitCode: ")) {
			expectedExitCode = int.Parse(line[10..]);
		} else if (line.StartsWith("Args: ")) {
			expectedArgs = line[6..].Trim();
		} else if (line.StartsWith("OptimizedInstructionCount: ")) {
			expectedOptimizedInstructionCount = int.Parse(line[27..]);
		} else if (line.StartsWith("UnoptimizedInstructionCount: ")) {
			expectedUnoptimizedInstructionCount = int.Parse(line[29..]);
		} else if (line.StartsWith("MaxoncStdout: ")) {
			expectedMaxoncStdout = ReadMultilineContent(lines, ref i, line[14..]);
		} else if (line.StartsWith("MaxoncStderr: ")) {
			expectedMaxoncStderr = ReadMultilineContent(lines, ref i, line[14..]);
		} else if (line.StartsWith("Stdout: ")) {
			expectedStdout = ReadMultilineContent(lines, ref i, line[8..]);
		} else if (line.StartsWith("Stderr: ")) {
			expectedStderr = ReadMultilineContent(lines, ref i, line[8..]);
		}
		}

		fragmentFile.Close();

		// Create a temporary directory for test files
		var tempDir = Path.Combine(Path.GetTempPath(), "maxon-tests-" + Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try {
		// Write the source code to a temporary file
		var sourceFilename = Path.Combine(tempDir, "test.maxon");
		File.WriteAllText(sourceFilename, source);

		// Compile both optimized and unoptimized versions
	var optimizedIR = "";
	var unoptimizedIR = "";
	var optimizedInstructionCount = -1;
	var unoptimizedInstructionCount = -1;
	
	var maxonPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "build", "bin", "maxon.exe"));
	
	Process? maxonOpt = null;
	string maxonOptStdout = "";
	string maxonOptStderr = "";
	
	// Always compile with optimization if we have expected optimized IR (regardless of optimize flag)
	// or if optimize flag is explicitly set
	if (!string.IsNullOrEmpty(expectedOptimizedIR) || optimize) {
		var llFilename = Path.Combine(tempDir, "test-opt.ll");
		maxonOpt = new Process();
		maxonOpt.StartInfo.FileName = maxonPath;
		maxonOpt.StartInfo.Arguments = $"compile test.maxon --emit-llvm -o test-opt.ll -O";
		maxonOpt.StartInfo.WorkingDirectory = tempDir;
		maxonOpt.StartInfo.RedirectStandardOutput = true;
		maxonOpt.StartInfo.RedirectStandardError = true;
		maxonOpt.StartInfo.UseShellExecute = false;
		maxonOpt.Start();
		maxonOptStdout = maxonOpt.StandardOutput.ReadToEnd();
		maxonOptStderr = maxonOpt.StandardError.ReadToEnd();
		maxonOpt.WaitForExit();
		
		// Normalize temp file paths in error messages for consistent comparisons
		maxonOptStderr = maxonOptStderr
			.Replace("test-opt.exe.tmp.obj", "test.exe.tmp.obj")
			.Replace("test-debug.exe.tmp.obj", "test.exe.tmp.obj")
			.Replace("output.exe.tmp.obj", "test.exe.tmp.obj");
		
		if (maxonOpt.ExitCode == 0 && File.Exists(llFilename)) {
				optimizedIR = File.ReadAllText(llFilename).Trim();
			}
		}
		
		// Always compile without optimization if we have expected unoptimized IR (regardless of debug flag)
		// or if debug flag is explicitly set
		if (!string.IsNullOrEmpty(expectedUnoptimizedIR) || debug) {
			var llFilename = Path.Combine(tempDir, "test-debug.ll");
			var maxonDebug = new Process();
			maxonDebug.StartInfo.FileName = maxonPath;
			maxonDebug.StartInfo.Arguments = $"compile test.maxon --emit-llvm -o test-debug.ll --debug";
			maxonDebug.StartInfo.WorkingDirectory = tempDir;
			maxonDebug.StartInfo.RedirectStandardOutput = true;
			maxonDebug.StartInfo.RedirectStandardError = true;
			maxonDebug.StartInfo.UseShellExecute = false;
			maxonDebug.Start();
			var maxonDebugStdout = maxonDebug.StandardOutput.ReadToEnd();
			var maxonDebugStderr = maxonDebug.StandardError.ReadToEnd();
			maxonDebug.WaitForExit();
			
			if (maxonDebug.ExitCode == 0 && File.Exists(llFilename)) {
				unoptimizedIR = File.ReadAllText(llFilename).Trim();
			}
		}
		
		var processExitCode = -1;
		var stdout = "";
		var stderr = "";

		// Verify optimized IR if expected
		if (!string.IsNullOrEmpty(expectedOptimizedIR) && !string.IsNullOrEmpty(optimizedIR)) {
			if (expectedOptimizedIR == "N/A") {
				Assert.Fail("Test fragment has N/A for expected optimized IR, but the code compiles successfully. Use create-test-fragment.ps1 to regenerate the expected IR.");
			}
			Assert.That(optimizedIR, Is.EqualTo(expectedOptimizedIR), "Generated optimized LLVM IR does not match expected IR");
		}
		
		// Verify unoptimized IR if expected
		if (!string.IsNullOrEmpty(expectedUnoptimizedIR) && !string.IsNullOrEmpty(unoptimizedIR)) {
			if (expectedUnoptimizedIR == "N/A") {
				Assert.Fail("Test fragment has N/A for expected unoptimized IR, but the code compiles successfully.");
			}
			Assert.That(unoptimizedIR, Is.EqualTo(expectedUnoptimizedIR), "Generated unoptimized LLVM IR does not match expected IR");
		}
		
		// Compile and run instrumented versions to count instructions
		if (expectedOptimizedInstructionCount > 0) {
			optimizedInstructionCount = CountInstructionsViaInstrumentation(tempDir, maxonPath, true, expectedArgs);
		}
		if (expectedUnoptimizedInstructionCount > 0) {
			unoptimizedInstructionCount = CountInstructionsViaInstrumentation(tempDir, maxonPath, false, expectedArgs);
		}
		
		// Check instruction counts and detect performance regressions
		const double REGRESSION_THRESHOLD = 0.05; // 5%
		
		if (expectedOptimizedInstructionCount > 0 && optimizedInstructionCount > 0) {
			var optDiff = (double)(optimizedInstructionCount - expectedOptimizedInstructionCount) / expectedOptimizedInstructionCount;
			if (optDiff > REGRESSION_THRESHOLD) {
				Assert.Fail($"Performance regression in optimized code: {optimizedInstructionCount} instructions vs baseline {expectedOptimizedInstructionCount} (+{optDiff * 100:F1}%)");
			}
			// Log improvement for visibility
			if (optDiff < -REGRESSION_THRESHOLD) {
				Console.WriteLine($"Performance improvement in optimized code: {optimizedInstructionCount} instructions vs baseline {expectedOptimizedInstructionCount} ({optDiff * 100:F1}%)");
			}
		}
		
		if (expectedUnoptimizedInstructionCount > 0 && unoptimizedInstructionCount > 0) {
			var unoptDiff = (double)(unoptimizedInstructionCount - expectedUnoptimizedInstructionCount) / expectedUnoptimizedInstructionCount;
			if (unoptDiff > REGRESSION_THRESHOLD) {
				Assert.Fail($"Performance regression in unoptimized code: {unoptimizedInstructionCount} instructions vs baseline {expectedUnoptimizedInstructionCount} (+{unoptDiff * 100:F1}%)");
			}
			// Log improvement for visibility
			if (unoptDiff < -REGRESSION_THRESHOLD) {
				Console.WriteLine($"Performance improvement in unoptimized code: {unoptimizedInstructionCount} instructions vs baseline {expectedUnoptimizedInstructionCount} ({unoptDiff * 100:F1}%)");
			}
		}
		
		// Helper function to run an executable and capture output
		var runExecutable = (string exePath) => {
			var process = new Process();
			process.StartInfo.FileName = exePath;
			process.StartInfo.Arguments = expectedArgs;
			process.StartInfo.RedirectStandardInput = true;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.RedirectStandardError = true;
			process.StartInfo.UseShellExecute = false;
			process.Start();

			// Read streams using background threads to avoid deadlock
			string capturedStdout = "";
			string capturedStderr = "";
			var stdoutThread = new System.Threading.Thread(() => {
				capturedStdout = process.StandardOutput.ReadToEnd();
			});
			var stderrThread = new System.Threading.Thread(() => {
				capturedStderr = process.StandardError.ReadToEnd();
			});
			stdoutThread.Start();
			stderrThread.Start();

			int exitCode = -1;
			// Wait up to 100ms for the process to complete
			if (process.WaitForExit(100)) {
				exitCode = process.ExitCode;
				// Wait for stream reading to complete
				stdoutThread.Join(100);
				stderrThread.Join(100);
			} else {
				// Process timed out
				process.Kill();
			}
			
			return (exitCode, capturedStdout.Replace("\r\n", "\n").TrimEnd(), capturedStderr.Replace("\r\n", "\n").TrimEnd());
		};
		
		// Compile and run executables for runtime testing (only if we expect output)
		// For dual-IR tests, we compile and run BOTH optimized and unoptimized versions
		bool shouldRunExecutables = expectedExitCode != -1 || expectedStdout != "" || expectedStderr != "";
		
		if (shouldRunExecutables) {
			// Compile and run optimized version if we have optimized IR
			if (!string.IsNullOrEmpty(expectedOptimizedIR) && expectedOptimizedIR != "N/A") {
				var maxonOptExe = new Process();
				maxonOptExe.StartInfo.FileName = maxonPath;
				maxonOptExe.StartInfo.Arguments = "compile test.maxon -o test-opt.exe -O";
				maxonOptExe.StartInfo.WorkingDirectory = tempDir;
				maxonOptExe.StartInfo.RedirectStandardOutput = true;
				maxonOptExe.StartInfo.RedirectStandardError = true;
				maxonOptExe.StartInfo.UseShellExecute = false;
				maxonOptExe.Start();
				maxonOptExe.StandardOutput.ReadToEnd();
				maxonOptExe.StandardError.ReadToEnd();
				maxonOptExe.WaitForExit();
				
				var optExePath = Path.Combine(tempDir, "test-opt.exe");
				if (File.Exists(optExePath)) {
					var (optExitCode, optStdout, optStderr) = runExecutable(optExePath);
					processExitCode = optExitCode;
					stdout = optStdout;
					stderr = optStderr;
				} else {
					Assert.Fail($"Expected optimized executable at {optExePath} but it was not created");
				}
			}
			
			// Compile and run unoptimized version if we have unoptimized IR
			if (!string.IsNullOrEmpty(expectedUnoptimizedIR) && expectedUnoptimizedIR != "N/A") {
				var maxonDebugExe = new Process();
				maxonDebugExe.StartInfo.FileName = maxonPath;
				maxonDebugExe.StartInfo.Arguments = "compile test.maxon -o test-debug.exe --debug";
				maxonDebugExe.StartInfo.WorkingDirectory = tempDir;
				maxonDebugExe.StartInfo.RedirectStandardOutput = true;
				maxonDebugExe.StartInfo.RedirectStandardError = true;
				maxonDebugExe.StartInfo.UseShellExecute = false;
				maxonDebugExe.Start();
				maxonDebugExe.StandardOutput.ReadToEnd();
				maxonDebugExe.StandardError.ReadToEnd();
				maxonDebugExe.WaitForExit();
				
				var debugExePath = Path.Combine(tempDir, "test-debug.exe");
				if (File.Exists(debugExePath)) {
					var (debugExitCode, debugStdout, debugStderr) = runExecutable(debugExePath);
					
					// For dual-IR tests, verify both versions produce the same output
					if (!string.IsNullOrEmpty(expectedOptimizedIR) && expectedOptimizedIR != "N/A") {
						Assert.That(debugExitCode, Is.EqualTo(processExitCode), 
							$"Optimized and unoptimized executables have different exit codes: {processExitCode} vs {debugExitCode}");
						Assert.That(debugStdout, Is.EqualTo(stdout), 
							"Optimized and unoptimized executables produce different stdout");
						Assert.That(debugStderr, Is.EqualTo(stderr), 
							"Optimized and unoptimized executables produce different stderr");
					} else {
						// If only unoptimized, use its output
						processExitCode = debugExitCode;
						stdout = debugStdout;
						stderr = debugStderr;
					}
				} else {
					Assert.Fail($"Expected unoptimized executable at {debugExePath} but it was not created");
				}
			}
		} else {
			// No executable expected (compilation error test) - use compiler exit code
			processExitCode = maxonOpt?.ExitCode ?? 1;
		}

	if (expectedExitCode != -1) {
			Assert.That(processExitCode, Is.EqualTo(expectedExitCode));
		}
		// Only check stdout/stderr if they were explicitly specified in the test
		if (expectedMaxoncStdout != "") {
			Assert.That(maxonOptStdout, Is.EqualTo(expectedMaxoncStdout));
		}
		if (expectedMaxoncStderr != "") {
			// Normalize line endings and temp file paths for comparison
			var normalizedMaxonStderr = maxonOptStderr
				.Replace("\r\n", "\n")
				.Replace("temp-opt.exe.tmp.obj", "test.exe.tmp.obj")
				.Replace("temp-debug.exe.tmp.obj", "test.exe.tmp.obj")
				.Replace("output.exe.tmp.obj", "test.exe.tmp.obj")
				.TrimEnd();
			var normalizedExpectedStderr = expectedMaxoncStderr.Replace("\r\n", "\n").TrimEnd();
			Assert.That(normalizedMaxonStderr, Is.EqualTo(normalizedExpectedStderr));
		}
		if (expectedStdout != "") {
				// Normalize line endings for comparison
				var normalizedStdout = stdout.Replace("\r\n", "\n");
				var normalizedExpectedStdout = expectedStdout.Replace("\r\n", "\n");
				Assert.That(normalizedStdout, Is.EqualTo(normalizedExpectedStdout), "Test executable stdout does not match expected");
			}
			if (expectedStderr != "") {
				// Normalize line endings for comparison
				var normalizedStderr = stderr.Replace("\r\n", "\n");
				var normalizedExpectedStderr = expectedStderr.Replace("\r\n", "\n");
				Assert.That(normalizedStderr, Is.EqualTo(normalizedExpectedStderr), "Test executable stderr does not match expected");
			}
		} finally {
			// Clean up temporary directory
			try {
				if (Directory.Exists(tempDir)) {
					Directory.Delete(tempDir, true);
				}
			} catch {
			// Ignore cleanup errors
		}
	}
}

// Helper method to count instructions via runtime instrumentation
private static int CountInstructionsViaInstrumentation(string tempDir, string maxonPath, bool optimize, string args) {
	try {
		// Compile with profiling enabled
		var profileExe = Path.Combine(tempDir, optimize ? "test-profile-opt.exe" : "test-profile-unopt.exe");
		var compileProcess = new Process();
		compileProcess.StartInfo.FileName = maxonPath;
		var compileArgs = optimize ? "compile test.maxon -o \"" + Path.GetFileName(profileExe) + "\" -O --profile" : "compile test.maxon -o \"" + Path.GetFileName(profileExe) + "\" --profile";
		compileProcess.StartInfo.Arguments = compileArgs;
		compileProcess.StartInfo.WorkingDirectory = tempDir;
		compileProcess.StartInfo.RedirectStandardOutput = true;
		compileProcess.StartInfo.RedirectStandardError = true;
		compileProcess.StartInfo.UseShellExecute = false;
		compileProcess.Start();
		compileProcess.StandardOutput.ReadToEnd();
		var compileStderr = compileProcess.StandardError.ReadToEnd();
		compileProcess.WaitForExit();
		
		if (compileProcess.ExitCode != 0 || !File.Exists(profileExe)) {
			return -1;
		}
		
		// Run the profiled executable
		var runProcess = new Process();
		runProcess.StartInfo.FileName = profileExe;
		runProcess.StartInfo.Arguments = args;
		runProcess.StartInfo.WorkingDirectory = tempDir;
		runProcess.StartInfo.RedirectStandardOutput = true;
		runProcess.StartInfo.RedirectStandardError = true;
		runProcess.StartInfo.UseShellExecute = false;
		runProcess.Start();
		var profileStdout = runProcess.StandardOutput.ReadToEnd();
		var profileStderr = runProcess.StandardError.ReadToEnd();
		runProcess.WaitForExit(1000);
		
		// Parse instruction count from stderr (instrumentation outputs to stderr)
		// Look for lines like: "Total instructions executed: 123"
		var match = MyRegex().Match(profileStderr);
		
		if (match.Success) {
			return int.Parse(match.Groups[1].Value);
		}
		
		return -1;
	} catch {
		return -1;
	}
}

	[GeneratedRegexAttribute(@"Total instructions executed:\s*(\d+)", RegexOptions.IgnoreCase, "en-US")]
	private static partial System.Text.RegularExpressions.Regex MyRegex();
}
