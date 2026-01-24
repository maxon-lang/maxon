using MaxonSharp.Compiler;
using MaxonSharp.Testing;

namespace MaxonSharp;

class Program {
	static int Main(string[] args) {
		// Handle test subcommand
		if (args.Length > 0 && args[0] == "test") {
			return RunTests(args[1..]);
		}

		var emitIr = false;
		string? sourceFile = null;

		foreach (var arg in args) {
			if (arg == "--emit-ir") {
				emitIr = true;
			} else if (arg.StartsWith("--log=")) {
				var logValue = arg["--log=".Length..];
				if (!Logger.ParseOption(logValue)) {
					Console.Error.WriteLine($"Error: Invalid log option: {logValue}");
					Console.Error.WriteLine("  Valid levels: none, error, info, debug, trace");
					Console.Error.WriteLine("  Valid categories: compiler, lexer, parser, semantic, hir, lir, codegen, pe");
					return 1;
				}
			} else if (!arg.StartsWith('-')) {
				sourceFile = arg;
			}
		}

		if (sourceFile == null) {
			Console.Error.WriteLine("Usage: MaxonSharp [options] <source-file-or-directory>");
			Console.Error.WriteLine("       MaxonSharp test [test-options]");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Options:");
			Console.Error.WriteLine("  --emit-ir              Write .hir and .lir files");
			Console.Error.WriteLine("  --log=LEVEL            Set all log categories to LEVEL");
			Console.Error.WriteLine("  --log=CATEGORY:LEVEL   Set specific category to LEVEL");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Test options:");
			Console.Error.WriteLine("  --verbose              Show all test results");
			Console.Error.WriteLine("  --filter=PATTERN       Run only tests matching regex pattern");
			Console.Error.WriteLine("  --workers=N            Number of parallel workers (default: CPU/2)");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Log levels: none, error, info, debug, trace");
			Console.Error.WriteLine("Log categories: compiler, lexer, parser, semantic, hir, lir, codegen, pe");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Examples:");
			Console.Error.WriteLine("  MaxonSharp test.maxon --log=debug");
			Console.Error.WriteLine("  MaxonSharp examples/multifile/main.maxon  (multi-file project)");
			Console.Error.WriteLine("  MaxonSharp test --verbose --filter=addition");
			return 1;
		}

		// Determine if this is a multi-file project
		var sourceFiles = CollectProjectFiles(sourceFile);
		if (sourceFiles == null) {
			Console.Error.WriteLine($"Error: File or directory not found: {sourceFile}");
			return 1;
		}

		// Find the main file to determine output path
		var mainFile = FindMainFile(sourceFiles, sourceFile);
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

	/// <summary>
	/// Collects all .maxon files for a project.
	/// If the path is a directory, recursively collects all .maxon files.
	/// If the path is a file, checks if it's in a project directory (has sibling or child .maxon files).
	/// </summary>
	static SourceFile[]? CollectProjectFiles(string path) {
		// Check if it's a directory
		if (Directory.Exists(path)) {
			return CollectFilesFromDirectory(path);
		}

		// Check if it's a file
		if (!File.Exists(path)) {
			return null;
		}

		// It's a file - check if it's part of a multi-file project
		var directory = Path.GetDirectoryName(path);
		if (string.IsNullOrEmpty(directory)) {
			directory = ".";
		}

		// Collect all .maxon files from the directory and subdirectories
		var allFiles = CollectFilesFromDirectory(directory);

		// If there's only one file, return it as a single-file project
		if (allFiles.Length <= 1) {
			var content = File.ReadAllText(path);
			return [new SourceFile(path, content)];
		}

		// Multi-file project
		return allFiles;
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

	static int RunTests(string[] args) {
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
			Console.Error.WriteLine("Error: Could not find project root (looking for specs/ directory)");
			return 1;
		}

		var specDir = Path.Combine(projectDir, "specs");
		var fragmentDir = Path.Combine(specDir, "fragments");
		var tempDir = Path.Combine(projectDir, "temp");

		Console.WriteLine("Running maxon-sharp spec tests...");

		var runner = new TestRunner(specDir, fragmentDir, tempDir, verbose, filter, workers);
		var summary = runner.RunAllTests();

		Console.WriteLine();
		if (summary.Failed == 0) {
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine($"Tests: {summary.Passed} passed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			Console.ResetColor();
			return 0;
		} else {
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"Tests: {summary.Passed} passed, {summary.Failed} failed (total: {summary.Total}) in {summary.TotalDuration.TotalMilliseconds:F0}ms");
			Console.ResetColor();
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
