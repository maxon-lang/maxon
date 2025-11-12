namespace Test;

using NUnit.Framework;
using System.Diagnostics;

// trying to embed the test code and expected IR in this file would be a huge PITA
// due to the way C# handles multiline strings (escaping, indentation, etc.)
// so we put the test code in separate files and read them here
// these "fragments" include the test code, the expected IR, and expected outputs
// if we are testing for parser or semantic errors then the code will not compile
// therefore there is no expected IR. In that case we put "N/A" in the fragment file

public class FragmentTests {
	private static readonly string fragmentsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "language-tests", "fragments");
	private static string[] Fragments() {
		return [.. Directory.EnumerateFiles(fragmentsPath, "*.test").Select(f => Path.GetFileName(f))];
	}
	private static readonly string debugFragmentsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "language-tests", "debug-fragments");
	private static string[] DebugFragments() {
		return [.. Directory.EnumerateFiles(debugFragmentsPath, "*.test").Select(f => Path.GetFileName(f))];
	}
	private static readonly string docFragmentsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "language-tests", "doc-fragments");
	private static string[] DocFragments() {
		return [.. Directory.EnumerateFiles(docFragmentsPath, "*.test").Select(f => Path.GetFileName(f))];
	}

	// Check for UPDATE_FRAGMENTS environment variable
	private static bool ShouldUpdateFragments() {
		var envVar = Environment.GetEnvironmentVariable("UPDATE_FRAGMENTS");
		return envVar == "1" || envVar?.ToLower() == "true";
	}

	[Test, TestCaseSource(nameof(DocFragments))]
	public void DocFragmentTest(string fragmentFilename) {
		TestFragment(fragmentFilename, docFragmentsPath, false, true);
	}

	[Test, TestCaseSource(nameof(Fragments))]
	public void FragmentTest(string fragmentFilename) {
		TestFragment(fragmentFilename, fragmentsPath, false, true);
	}

	[Test, TestCaseSource(nameof(DebugFragments))]
	public void DebugFragmentTest(string fragmentFilename) {
		TestFragment(fragmentFilename, debugFragmentsPath, true, false);
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
			return string.Join("\n", contentLines).TrimEnd();
		} else {
			// Keyword-delimited or single-line format
			var contentLines = new List<string> { content };
			for (var j = i + 1; j < lines.Length; j++) {
				var nextLine = lines[j].TrimEnd('\r');
				if (nextLine.StartsWith("ExitCode: ") || 
				    nextLine.StartsWith("ParserError: ") || 
				    nextLine.StartsWith("MaxoncStdout: ") || 
				    nextLine.StartsWith("MaxoncStderr: ") ||
				    nextLine.StartsWith("Stdout: ") ||
				    nextLine.StartsWith("Stderr: ")) {
					break;
				}
				contentLines.Add(nextLine);
				i = j; // Advance the outer loop counter
			}
			return string.Join("\n", contentLines).TrimEnd();
		}
	}

	private static void TestFragment(string fragmentFilename, string path, bool debug, bool optimize) {
		// Check command line for update flag, otherwise auto-update when IR is empty
		var updateInsteadOfTest = ShouldUpdateFragments();

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

		var expectedIR = "";
		while (true) {
			var line = fragmentFile.ReadLine();
			if (line == null || line == "---") {
				break;
			}
			expectedIR += line + "\n";
		}
		expectedIR = expectedIR.Trim();

		var expectedExitCode = -1;
		var expectedParserErrors = new List<string>();
		var expectedMaxoncStdout = "";
		var expectedMaxoncStderr = "";
		var expectedStdout = "";
		var expectedStderr = "";
		if (expectedIR == "" && !updateInsteadOfTest) {
			// If there's no IR yet and we're not manually updating, enable auto-update
			updateInsteadOfTest = true;
		} else if (!updateInsteadOfTest) {
			// Read the rest of the file after the IR section
			var remainingContent = fragmentFile.ReadToEnd();
			var lines = remainingContent.Split('\n');
			
			for (var i = 0; i < lines.Length; i++) {
				var line = lines[i].TrimEnd('\r');
				if (line.StartsWith("ExitCode: ")) {
					expectedExitCode = int.Parse(line[10..]);
				} else if (line.StartsWith("ParserError: ")) {
					expectedParserErrors.Add(ReadMultilineContent(lines, ref i, line[13..]));
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
		}

		fragmentFile.Close();

		// Create a temporary directory for test files
		var tempDir = Path.Combine(Path.GetTempPath(), "maxon-tests-" + Guid.NewGuid().ToString());
		Directory.CreateDirectory(tempDir);

		try {
			// Write the source code to a temporary file
			var sourceFilename = Path.Combine(tempDir, "test.maxon");
			File.WriteAllText(sourceFilename, source);
			// The compiler writes IR to this file
			var llFilename = Path.Combine(tempDir, "test.ll");

		// Call maxon.exe to compile the source
		var maxonPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "build", "bin", "maxon.exe"));
			var maxon = new Process();
			maxon.StartInfo.FileName = maxonPath;
			var optimizeFlag = optimize ? " -O" : "";
			var debugFlag = debug ? " --debug" : "";
			maxon.StartInfo.Arguments = $"compile test.maxon --emit-llvm -o test.ll{optimizeFlag}{debugFlag}";
			maxon.StartInfo.WorkingDirectory = tempDir;
			maxon.StartInfo.RedirectStandardOutput = true;
			maxon.StartInfo.RedirectStandardError = true;
			maxon.StartInfo.UseShellExecute = false;
			maxon.Start();
			var maxonStdout = maxon.StandardOutput.ReadToEnd();
			var maxonStderr = maxon.StandardError.ReadToEnd();
			maxon.WaitForExit();

		var llSource = "N/A";
		var processExitCode = -1;
		var parserErrors = new List<string>();
		var stdout = "";
		var stderr = "";

		// Check if compilation succeeded and .ll file was created
		if (maxon.ExitCode == 0 && File.Exists(llFilename)) {
			llSource = File.ReadAllText(llFilename).Trim();
			
			// Check the IR if not updating
			if (!updateInsteadOfTest && expectedIR != "N/A") {
				Assert.That(llSource, Is.EqualTo(expectedIR), "Generated LLVM IR does not match expected IR");
			}				// maxon also generates an executable when --emit-llvm is used with -o
				var exeFilename = Path.Combine(tempDir, "test.exe");
			
			// Run the executable if it was generated
			if (File.Exists(exeFilename)) {
				var process = new Process();
				process.StartInfo.FileName = exeFilename;
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
				
				// Wait up to 100ms for the process to complete
				if (process.WaitForExit(100)) {
					processExitCode = process.ExitCode;
					// Wait for stream reading to complete
					stdoutThread.Join(100);
					stderrThread.Join(100);
					// Read captured output
					stdout = capturedStdout.Replace("\r\n", "\n").TrimEnd();
					stderr = capturedStderr.Replace("\r\n", "\n").TrimEnd();
				} else {
					// Process timed out
					process.Kill();
					processExitCode = -1;
					parserErrors.Add("Test executable timed out (100ms)");
				}
			} else {
				parserErrors.Add("Executable not created by maxon");
			}
		} else {
			// Capture the complete stderr output as the error message
			// But only if MaxoncStderr is not explicitly specified in the test
			if (!string.IsNullOrEmpty(maxonStderr) && expectedMaxoncStderr == "") {
				// Normalize line endings to \n and trim
				parserErrors.Add(maxonStderr.Replace("\r\n", "\n").Trim());
			} else if (maxon.ExitCode != 0 && expectedMaxoncStderr == "") {
				parserErrors.Add($"maxon failed with exit code {maxon.ExitCode}");
			}
		}

		if (updateInsteadOfTest) {
			UpdateFragment(fragmentPath, source, llSource, processExitCode, parserErrors, maxonStdout, maxonStderr, stdout, stderr);
			Assert.Fail("Updated fragment file, please inspect the changes and make sure they are correct.");
		} else {
			if (expectedExitCode != -1) {
				Assert.That(processExitCode, Is.EqualTo(expectedExitCode));
			}
			// Check parser errors count first
			Assert.That(parserErrors, Has.Count.EqualTo(expectedParserErrors.Count));
			// Then check individual error messages
			for (var i = 0; i < expectedParserErrors.Count; i++) {
				Assert.That(parserErrors[i], Is.EqualTo(expectedParserErrors[i]));
			}
			// Only check stdout/stderr if they were explicitly specified in the test
			if (expectedMaxoncStdout != "") {
				Assert.That(maxonStdout, Is.EqualTo(expectedMaxoncStdout));
			}
			if (expectedMaxoncStderr != "") {
				// Normalize line endings for comparison
				var normalizedMaxonStderr = maxonStderr.Replace("\r\n", "\n").TrimEnd();
				var normalizedExpectedStderr = expectedMaxoncStderr.Replace("\r\n", "\n").TrimEnd();
				Assert.That(normalizedMaxonStderr, Is.EqualTo(normalizedExpectedStderr));
			}
			if (expectedStdout != "") {
				Assert.That(stdout, Is.EqualTo(expectedStdout), "Test executable stdout does not match expected");
			}
			if (expectedStderr != "") {
				Assert.That(stderr, Is.EqualTo(expectedStderr), "Test executable stderr does not match expected");
			}
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

	private static void UpdateFragment(string fragmentPath, string source, string llSource, int expectedExitCode, List<string> parserErrors, string maxoncStdout, string maxoncStderr, string stdout, string stderr) {
		File.WriteAllText(fragmentPath, source + "---\n" + llSource + "\n---");
		if (expectedExitCode != -1) {
			File.AppendAllText(fragmentPath, "\nExitCode: " + expectedExitCode);
		}
		foreach (var error in parserErrors) {
			// Use triple backticks for multi-line ParserError
			if (error.Contains('\n')) {
				File.AppendAllText(fragmentPath, "\nParserError: ```\n" + error + "\n```");
			} else {
				File.AppendAllText(fragmentPath, "\nParserError: " + error);
			}
		}
		if (maxoncStdout != "") {
			// Use triple backticks for multi-line MaxoncStdout
			if (maxoncStdout.Contains('\n')) {
				File.AppendAllText(fragmentPath, "\nMaxoncStdout: ```\n" + maxoncStdout + "\n```");
			} else {
				File.AppendAllText(fragmentPath, "\nMaxoncStdout: " + maxoncStdout);
			}
		}
		if (maxoncStderr != "") {
			// Use triple backticks for multi-line MaxoncStderr
			var trimmedStderr = maxoncStderr.Trim();
			if (trimmedStderr.Contains('\n')) {
				File.AppendAllText(fragmentPath, "\nMaxoncStderr: ```\n" + trimmedStderr + "\n```");
			} else {
				File.AppendAllText(fragmentPath, "\nMaxoncStderr: " + trimmedStderr);
			}
		}
		if (stdout != "") {
			// Use triple backticks for multi-line stdout
			if (stdout.Contains('\n')) {
				File.AppendAllText(fragmentPath, "\nStdout: ```\n" + stdout + "\n```");
			} else {
				File.AppendAllText(fragmentPath, "\nStdout: " + stdout);
			}
		}
		if (stderr != "") {
			// Use triple backticks for multi-line stderr
			if (stderr.Contains('\n')) {
				File.AppendAllText(fragmentPath, "\nStderr: ```\n" + stderr + "\n```");
			} else {
				File.AppendAllText(fragmentPath, "\nStderr: " + stderr);
			}
		}
	}
}
