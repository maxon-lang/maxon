namespace FragmentTests;

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

	private static void TestFragment(string fragmentFilename, string path, bool debug, bool optimize) {
		// for convenience, set this to true to update all the fragments
		// this will compile the code and update the expected IR and results in the fragment file
		// after doing this carefully inspect the changes and make sure they are correct
		var updateInsteadOfTest = false;

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
		if (expectedIR == "") {
			updateInsteadOfTest = true;
		} else {
			while (true) {
				var line = fragmentFile.ReadLine();
				if (line == null) {
					break;
				}
				if (line.StartsWith("ExitCode: ")) {
					expectedExitCode = int.Parse(line[10..]);
				} else if (line.StartsWith("ParserError: ")) {
					expectedParserErrors.Add(line[13..]);
				} else if (line.StartsWith("MaxoncStdout: ")) {
					expectedMaxoncStdout = line[14..];
				} else if (line.StartsWith("MaxoncStderr: ")) {
					expectedMaxoncStderr = line[14..];
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

			// Call maxonc.exe to compile the source
			var maxoncPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "maxon-bin", "build", "bin", "Release", "maxonc.exe"));
			var maxonc = new Process();
			maxonc.StartInfo.FileName = maxoncPath;
			maxonc.StartInfo.Arguments = $"test.maxon --emit-llvm -o test.ll";
			maxonc.StartInfo.WorkingDirectory = tempDir;
			maxonc.StartInfo.RedirectStandardOutput = true;
			maxonc.StartInfo.RedirectStandardError = true;
			maxonc.StartInfo.UseShellExecute = false;
			maxonc.Start();
			var maxoncStdout = maxonc.StandardOutput.ReadToEnd();
			var maxoncStderr = maxonc.StandardError.ReadToEnd();
			maxonc.WaitForExit();

			var llSource = "N/A";
			var processExitCode = -1;
			var parserErrors = new List<string>();

		// Check if compilation succeeded and .ll file was created
		if (maxonc.ExitCode == 0 && File.Exists(llFilename)) {
			llSource = File.ReadAllText(llFilename).Trim();
			
			// Check the IR if not updating
			if (!updateInsteadOfTest && expectedIR != "N/A") {
				Assert.That(llSource, Is.EqualTo(expectedIR), "Generated LLVM IR does not match expected IR");
			}				// maxonc also generates an executable when --emit-llvm is used with -o
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
				
				// Wait up to 100ms for the process to complete
				if (process.WaitForExit(100)) {
					processExitCode = process.ExitCode;
				} else {
					// Process timed out
					process.Kill();
					processExitCode = -1;
					parserErrors.Add("Test executable timed out (100ms)");
				}
			} else {
				parserErrors.Add("Executable not created by maxonc");
			}
		} else {
			// Parse errors from maxonc stderr
			if (!string.IsNullOrEmpty(maxoncStderr)) {
				var errorLines = maxoncStderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
				foreach (var errorLine in errorLines) {
					if (errorLine.Contains("Error:") || errorLine.Contains("error:")) {
						parserErrors.Add(errorLine.Trim());
					}
				}
			}
			if (maxonc.ExitCode != 0 && parserErrors.Count == 0) {
				parserErrors.Add($"maxonc failed with exit code {maxonc.ExitCode}");
			}
		}

		if (updateInsteadOfTest) {
			UpdateFragment(fragmentPath, source, llSource, processExitCode, parserErrors, maxoncStdout, maxoncStderr);
			Assert.Fail("Updated fragment file, please inspect the changes and make sure they are correct.");
		} else {
			if (expectedExitCode != -1) {
				Assert.That(processExitCode, Is.EqualTo(expectedExitCode));
			}
			for (var i = 0; i < expectedParserErrors.Count; i++) {
				Assert.That(parserErrors[i], Is.EqualTo(expectedParserErrors[i]));
			}
			Assert.That(parserErrors, Has.Count.EqualTo(expectedParserErrors.Count));
			// Only check stdout/stderr if they were explicitly specified in the test
			if (expectedMaxoncStdout != "") {
				Assert.That(maxoncStdout, Is.EqualTo(expectedMaxoncStdout));
			}
			if (expectedMaxoncStderr != "") {
				Assert.That(maxoncStderr, Is.EqualTo(expectedMaxoncStderr));
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

	private static void UpdateFragment(string fragmentPath, string source, string llSource, int expectedExitCode, List<string> parserErrors, string maxoncStdout, string maxoncStderr) {
		File.WriteAllText(fragmentPath, source + "---\n" + llSource + "\n---");
		if (expectedExitCode != -1) {
			File.AppendAllText(fragmentPath, "\nExitCode: " + expectedExitCode);
		}
		foreach (var error in parserErrors) {
			File.AppendAllText(fragmentPath, "\nParserError: " + error);
		}
		if (maxoncStdout != "") {
			File.AppendAllText(fragmentPath, "\nMaxoncStdout: " + maxoncStdout);
		}
		if (maxoncStderr != "") {
			File.AppendAllText(fragmentPath, "\nMaxoncStderr: " + maxoncStderr);
		}
	}
}
