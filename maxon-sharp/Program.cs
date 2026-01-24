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
			_ => HandleUnknownCommand(command)
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
		Console.WriteLine("  --emit-ir                Write .hir and .lir files");
		Console.WriteLine();
		Console.WriteLine("Spec test options:");
		Console.WriteLine("  --verbose                Show all test results");
		Console.WriteLine("  --filter=PATTERN         Run only tests matching regex pattern");
		Console.WriteLine("  --workers=N              Number of parallel workers (default: CPU/2)");
		Console.WriteLine();
		Console.WriteLine("Logging (all commands):");
		Console.WriteLine("  --log=LEVEL              Set all log categories to LEVEL");
		Console.WriteLine("  --log=CATEGORY:LEVEL     Set specific category to LEVEL");
		Console.WriteLine();
		Console.WriteLine("Log levels: none, error, info, debug, trace");
		Console.WriteLine("Log categories: compiler, lexer, parser, semantic, hir, lir, codegen, pe");
	}

	static int HandleUnknownCommand(string command) {
		Console.WriteLine($"Unknown command: {command}");
		Console.WriteLine();
		PrintUsage();
		return 1;
	}

	static (bool emitIr, bool valid) ParseCommonOptions(string[] args) {
		var emitIr = false;

		foreach (var arg in args) {
			if (arg == "--emit-ir") {
				emitIr = true;
			} else if (arg.StartsWith("--log=")) {
				var logValue = arg["--log=".Length..];
				if (!Logger.ParseOption(logValue)) {
					Console.WriteLine($"Invalid log option: {logValue}");
					Console.WriteLine("  Valid levels: none, error, info, debug, trace");
					Console.WriteLine("  Valid categories: compiler, lexer, parser, semantic, hir, lir, codegen, pe");
					return (false, false);
				}
			}
		}

		return (emitIr, true);
	}

	static int RunCompile(string[] args) {
		var (emitIr, valid) = ParseCommonOptions(args);
		if (!valid) return 1;

		string? sourceFile = null;
		foreach (var arg in args) {
			if (!arg.StartsWith('-')) {
				sourceFile = arg;
				break;
			}
		}

		if (sourceFile == null) {
			Console.WriteLine("Usage: maxonsharp compile [options] <file>");
			Console.WriteLine();
			Console.WriteLine("Compile a single .maxon file to an executable.");
			return 1;
		}

		if (!File.Exists(sourceFile)) {
			Console.WriteLine($"File not found: {sourceFile}");
			return 1;
		}

		var content = File.ReadAllText(sourceFile);
		var sources = new SourceFile[] { new(sourceFile, content) };
		var outputPath = Path.ChangeExtension(sourceFile, ".exe");

		string? hirOutputPath = null;
		string? lirOutputPath = null;
		if (emitIr) {
			hirOutputPath = Path.ChangeExtension(sourceFile, ".hir");
			lirOutputPath = Path.ChangeExtension(sourceFile, ".lir");
		}

		var success = Compiler.Compiler.Compile(sources, outputPath, hirOutputPath, lirOutputPath);
		return success ? 0 : 1;
	}

	static int RunBuild(string[] args) {
		var (emitIr, valid) = ParseCommonOptions(args);
		if (!valid) return 1;

		string? directory = null;
		foreach (var arg in args) {
			if (!arg.StartsWith('-')) {
				directory = arg;
				break;
			}
		}

		// Default to current directory if not specified
		directory ??= Directory.GetCurrentDirectory();

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

		string? hirOutputPath = null;
		string? lirOutputPath = null;
		if (emitIr) {
			hirOutputPath = Path.ChangeExtension(mainFile, ".hir");
			lirOutputPath = Path.ChangeExtension(mainFile, ".lir");
		}

		var success = Compiler.Compiler.Compile(sourceFiles, outputPath, hirOutputPath, lirOutputPath);
		return success ? 0 : 1;
	}

	static int RunRun(string[] args) {
		var (emitIr, valid) = ParseCommonOptions(args);
		if (!valid) return 1;

		string? path = null;
		foreach (var arg in args) {
			if (!arg.StartsWith('-')) {
				path = arg;
				break;
			}
		}

		if (path == null) {
			Console.WriteLine("Usage: maxonsharp run [options] <file|directory>");
			Console.WriteLine();
			Console.WriteLine("Compile and run a .maxon file or project.");
			return 1;
		}

		SourceFile[] sourceFiles;
		string mainFile;

		if (Directory.Exists(path)) {
			sourceFiles = CollectFilesFromDirectory(path);
			if (sourceFiles.Length == 0) {
				Console.WriteLine($"No .maxon files found in: {path}");
				return 1;
			}
			mainFile = FindMainFile(sourceFiles, path);
		} else if (File.Exists(path)) {
			var content = File.ReadAllText(path);
			sourceFiles = [new SourceFile(path, content)];
			mainFile = path;
		} else {
			Console.WriteLine($"File or directory not found: {path}");
			return 1;
		}

		var outputPath = Path.ChangeExtension(mainFile, ".exe");

		string? hirOutputPath = null;
		string? lirOutputPath = null;
		if (emitIr) {
			hirOutputPath = Path.ChangeExtension(mainFile, ".hir");
			lirOutputPath = Path.ChangeExtension(mainFile, ".lir");
		}

		var success = Compiler.Compiler.Compile(sourceFiles, outputPath, hirOutputPath, lirOutputPath);
		if (!success) {
			return 1;
		}

		// Run the compiled executable
		var process = new Process {
			StartInfo = new ProcessStartInfo {
				FileName = outputPath,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			}
		};

		process.Start();

		// Read output streams
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

	/// <summary>
	/// Recursively collects all .maxon files from a directory.
	/// </summary>
	static SourceFile[] CollectFilesFromDirectory(string directory) {
		var files = new List<SourceFile>();

		foreach (var file in Directory.GetFiles(directory, "*.maxon", SearchOption.AllDirectories)) {
			var content = File.ReadAllText(file);
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

	static int RunSpecTests(string[] args) {
		var verbose = false;
		string? filter = null;
		int? workers = null;

		foreach (var arg in args) {
			if (arg == "--verbose") {
				verbose = true;
			} else if (arg.StartsWith("--filter=")) {
				filter = arg["--filter=".Length..];
			} else if (arg.StartsWith("--workers=")) {
				if (int.TryParse(arg["--workers=".Length..], out var w)) {
					workers = w;
				}
			}
		}

		// Determine paths relative to the project
		var projectDir = FindProjectRoot();
		if (projectDir == null) {
			Console.WriteLine("Could not find project root (looking for specs/ directory)");
			return 1;
		}

		var specDir = Path.Combine(projectDir, "specs");
		var fragmentDir = Path.Combine(specDir, "fragments");
		var tempDir = Path.Combine(projectDir, "temp");

		Logger.Info(LogCategory.Testing, "Running maxon-sharp spec tests...");

		var runner = new TestRunner(specDir, fragmentDir, tempDir, verbose, filter, workers);
		var summary = runner.RunAllSpecTests();

		Logger.Info(LogCategory.Testing, "");
		if (summary.Failed == 0) {
			Logger.Info(LogCategory.Testing, $"Tests: {summary.Passed} passed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			return 0;
		} else {
			Logger.Error(LogCategory.Testing, $"Tests: {summary.Passed} passed, {summary.Failed} failed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			return 1;
		}
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
}
