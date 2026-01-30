using System.Diagnostics;
using MaxonSharp.Compiler;
using MaxonSharp.Testing;

namespace MaxonSharp;

class Program {
	static int Main(string[] args) {
		if (args.Length == 0) {
			PrintUsage();
			return 1;
		}

		var command = args[0];

		return command switch {
			"compile" => RunCompile(args[1..]),
			"build" => RunBuild(args[1..]),
			"run" => RunRun(args[1..]),
			"spec-test" => RunSpecTests(args[1..]),
			_ => Fail()
		};
	}

	static void PrintUsage() {
		Console.WriteLine("Usage: maxonsharp <command> [options]");
		Console.WriteLine();
		Console.WriteLine("Commands:");
		Console.WriteLine("  compile <file>           Compile a single .maxon file");
		Console.WriteLine("  build [<directory>]      Build a project (default: current directory)");
		Console.WriteLine("  run <file|directory>     Compile and run");
		Console.WriteLine("  spec-test [options]      Run spec tests");
		Console.WriteLine();
		Console.WriteLine("Build options (compile, build, run):");
		Console.WriteLine("  --emit-ir                Write .mlir file");
		Console.WriteLine("  --dump-stages            Write IR at each pipeline stage (.1-maxon.mlir, etc.)");
		Console.WriteLine();
		Console.WriteLine("Spec test options:");
		Console.WriteLine("  --filter=PATTERN         Run only tests matching pattern");
		Console.WriteLine();
		Console.WriteLine("Logging (all commands):");
		Console.WriteLine("  --log=LEVEL              Set all log categories to LEVEL");
		Console.WriteLine("  --log=CATEGORY:LEVEL     Set specific category to LEVEL");
		Console.WriteLine();
		Console.WriteLine("Log levels: none, error, info, debug, trace");
		Console.WriteLine("Log categories: compiler, lexer, parser, semantic, hir, lir, optimizer, codegen, pe, testing");
		Console.WriteLine();
		Console.WriteLine("Testing log levels:");
		Console.WriteLine("  info   - Show failures and summary only");
		Console.WriteLine("  debug  - Also show each passing test");
	}

	static int Fail() {
		PrintUsage();
		return 1;
	}

	static (bool emitIr, bool dumpStages, bool valid) ParseOptions(string[] args, HashSet<string>? additionalOptions = null) {
		var emitIr = false;
		var dumpStages = false;

		foreach (var arg in args) {
			if (arg == "--emit-ir") {
				emitIr = true;
			} else if (arg == "--dump-stages") {
				dumpStages = true;
			} else if (arg.StartsWith("--log=")) {
				if (!Logger.ParseOption(arg["--log=".Length..])) {
					return (false, false, false);
				}
			} else if (arg.StartsWith('-')) {
				var recognized = false;
				if (additionalOptions != null) {
					foreach (var opt in additionalOptions) {
						if (opt.EndsWith('=') ? arg.StartsWith(opt) : arg == opt) {
							recognized = true;
							break;
						}
					}
				}
				if (!recognized) {
					return (false, false, false);
				}
			}
		}

		return (emitIr, dumpStages, true);
	}

	static int RunCompile(string[] args) {
		var (emitIr, dumpStages, valid) = ParseOptions(args);
		if (!valid) return Fail();

		var sourceFile = GetNonOptionArg(args);
		if (sourceFile == null) return Fail();

		if (!File.Exists(sourceFile)) {
			Console.WriteLine($"File not found: {sourceFile}");
			return 1;
		}

		var content = ReadFileContentUntilSeparator(sourceFile);
		var sources = new SourceFile[] { new(sourceFile, content) };
		var outputPath = Path.ChangeExtension(sourceFile, ".exe");

		var (mlirOutputPath, dumpStagesBasePath) = GetOutputPaths(sourceFile, emitIr, dumpStages);

		return CompileAndReportResult(sources, outputPath, mlirOutputPath, dumpStagesBasePath);
	}

	static int RunBuild(string[] args) {
		var (emitIr, dumpStages, valid) = ParseOptions(args);
		if (!valid) return Fail();

		var directory = GetNonOptionArg(args) ?? Directory.GetCurrentDirectory();

		if (!Directory.Exists(directory)) {
			Console.WriteLine($"Directory not found: {directory}");
			return 1;
		}

		var sourceFiles = CollectFilesFromDirectory(directory);
		if (sourceFiles.Length == 0) {
			Console.WriteLine($"No .maxon files found in: {directory}");
			return 1;
		}

		var mainFile = FindMainFile(sourceFiles, directory);
		var outputPath = Path.ChangeExtension(mainFile, ".exe");

		var (mlirOutputPath, dumpStagesBasePath) = GetOutputPaths(mainFile, emitIr, dumpStages);

		return CompileAndReportResult(sourceFiles, outputPath, mlirOutputPath, dumpStagesBasePath);
	}

	static int RunRun(string[] args) {
		var (emitIr, dumpStages, valid) = ParseOptions(args);
		if (!valid) return Fail();

		var path = GetNonOptionArg(args);
		if (path == null) return Fail();

		var (sourceFiles, mainFile) = ResolveSourceFilesAndMain(path);
		if (sourceFiles == null || mainFile == null) return 1;

		var outputPath = Path.ChangeExtension(mainFile, ".exe");
		var (mlirOutputPath, dumpStagesBasePath) = GetOutputPaths(mainFile, emitIr, dumpStages);

		var compileResult = CompileAndReportResult(sourceFiles, outputPath, mlirOutputPath, dumpStagesBasePath);
		if (compileResult != 0) return compileResult;

		return RunExecutable(outputPath);
	}

	/// <summary>
	/// Reads file content up to the first "---" separator line.
	/// </summary>
	static string ReadFileContentUntilSeparator(string filePath) {
		var content = File.ReadAllText(filePath);
		var lines = content.Split('\n');
		var sourceLines = new List<string>();
		foreach (var line in lines) {
			if (line.Trim() == "---") {
				break;
			}
			sourceLines.Add(line);
		}
		return string.Join('\n', sourceLines);
	}

	/// <summary>
	/// Recursively collects all .maxon files from a directory.
	/// </summary>
	static SourceFile[] CollectFilesFromDirectory(string directory) {
		var files = new List<SourceFile>();

		foreach (var file in Directory.GetFiles(directory, "*.maxon", SearchOption.AllDirectories)) {
			var content = ReadFileContentUntilSeparator(file);
			files.Add(new SourceFile(file, content));
		}

		return [.. files];
	}

	/// <summary>
	/// Finds the main file (containing main function) or uses the originally specified file.
	/// </summary>
	static string FindMainFile(SourceFile[] files, string originalPath) {
		// If original path was a file, prefer it
		if (File.Exists(originalPath)) {
			return originalPath;
		}

		// Look for a file containing 'function main'
		foreach (var file in files) {
			if (file.Content.Contains("function main")) {
				return file.Path;
			}
		}

		// Look for main.maxon
		foreach (var file in files) {
			if (Path.GetFileName(file.Path).Equals("main.maxon", StringComparison.OrdinalIgnoreCase)) {
				return file.Path;
			}
		}

		// Fall back to first file
		return files.Length > 0 ? files[0].Path : originalPath;
	}

	/// <summary>
	/// Gets the first non-option argument from the args array.
	/// </summary>
	static string? GetNonOptionArg(string[] args) {
		foreach (var arg in args) {
			if (!arg.StartsWith('-')) {
				return arg;
			}
		}
		return null;
	}

	/// <summary>
	/// Gets output paths for MLIR and dump stages based on flags.
	/// </summary>
	static (string? mlirOutputPath, string? dumpStagesBasePath) GetOutputPaths(string mainFile, bool emitIr, bool dumpStages) {
		string? mlirOutputPath = null;
		if (emitIr) {
			mlirOutputPath = Path.ChangeExtension(mainFile, ".mlir");
		}

		string? dumpStagesBasePath = null;
		if (dumpStages) {
			dumpStagesBasePath = Path.ChangeExtension(mainFile, null);
		}

		return (mlirOutputPath, dumpStagesBasePath);
	}

	/// <summary>
	/// Compiles source files and reports the result.
	/// </summary>
	static int CompileAndReportResult(SourceFile[] sources, string outputPath, string? mlirOutputPath, string? dumpStagesBasePath) {
		var result = new Compiler.Compiler().Compile(sources, outputPath, mlirOutputPath, dumpStagesBasePath: dumpStagesBasePath);
		if (!result.Success && result.Error != null) {
			Logger.Error(LogCategory.Compiler, result.Error);
		}
		return result.Success ? 0 : 1;
	}

	/// <summary>
	/// Resolves source files and main file from either a file or directory path.
	/// </summary>
	static (SourceFile[]? sourceFiles, string? mainFile) ResolveSourceFilesAndMain(string path) {
		if (Directory.Exists(path)) {
			var sourceFiles = CollectFilesFromDirectory(path);
			if (sourceFiles.Length == 0) {
				Console.WriteLine($"No .maxon files found in: {path}");
				return (null, null);
			}
			var mainFile = FindMainFile(sourceFiles, path);
			return (sourceFiles, mainFile);
		} else if (File.Exists(path)) {
			var content = ReadFileContentUntilSeparator(path);
			var sourceFiles = new SourceFile[] { new(path, content) };
			return (sourceFiles, path);
		} else {
			Console.WriteLine($"File or directory not found: {path}");
			return (null, null);
		}
	}

	/// <summary>
	/// Runs a compiled executable and returns its exit code.
	/// </summary>
	static int RunExecutable(string executablePath) {
		var process = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = executablePath,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			}
		};

		process.Start();

		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();

		process.WaitForExit();

		if (!string.IsNullOrEmpty(stdout)) {
			Console.Write(stdout);
		}
		if (!string.IsNullOrEmpty(stderr)) {
			Console.Error.Write(stderr);
		}

		return process.ExitCode;
	}

	static int RunSpecTests(string[] args) {
		SetupTestLogging();

		var specTestOptions = new HashSet<string> { "--filter=", "--workers=" };
		var (_, _, valid) = ParseOptions(args, specTestOptions);
		if (!valid) return Fail();

		string? filter = null;
		int? workers = null;

		foreach (var arg in args) {
			if (arg.StartsWith("--filter=")) {
				filter = arg["--filter=".Length..];
			} else if (arg.StartsWith("--workers=")) {
				if (int.TryParse(arg["--workers=".Length..], out var w)) {
					workers = w;
				} else {
					return Fail();
				}
			}
		}

		var projectDir = FindProjectRoot();
		if (projectDir == null) {
			Console.WriteLine("Could not find project root (looking for specs/ directory)");
			return 1;
		}

		var specDir = Path.Combine(projectDir, "specs");
		var fragmentDir = Path.Combine(specDir, "fragments");
		var tempDir = Path.Combine(projectDir, "temp");

		Compiler.CompileError.ProjectRoot = projectDir;

		Logger.Info(LogCategory.Testing, "Running maxon-sharp spec tests...");

		var runner = new TestRunner(specDir, fragmentDir, tempDir, filter, workers);
		var summary = runner.RunAllSpecTests();

		Logger.Info(LogCategory.Testing, "");
		if (summary.FragmentGenerationErrors > 0) {
			Logger.Error(LogCategory.Testing, $"Fragment generation failed: {summary.FragmentGenerationErrors} error(s) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			return 1;
		}

		return ReportTestResults(summary);
	}

	static string? FindProjectRoot() {
		// Look for specs/ directory to find project root
		var dir = Directory.GetCurrentDirectory();
		while (dir != null) {
			if (Directory.Exists(Path.Combine(dir, "specs"))) {
				return dir;
			}
			dir = Path.GetDirectoryName(dir);
		}
		return null;
	}

	/// <summary>
	/// Sets up logging for test commands (suppresses compiler Info messages).
	/// </summary>
	static void SetupTestLogging() {
		Logger.SetLevel(LogCategory.Compiler, LogLevel.Error);
	}

	/// <summary>
	/// Reports test results in a consistent format.
	/// </summary>
	static int ReportTestResults(TestSummary summary) {
		if (summary.Failed == 0) {
			Logger.Info(LogCategory.Testing, $"Tests: {summary.Passed} passed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			return 0;
		} else {
			Logger.Error(LogCategory.Testing, $"Tests: {summary.Passed} passed, {summary.Failed} failed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			return 1;
		}
	}

}
