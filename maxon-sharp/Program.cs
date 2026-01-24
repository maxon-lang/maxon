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
			Console.Error.WriteLine("Usage: MaxonSharp [options] <source-file>");
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
			Console.Error.WriteLine("  MaxonSharp test.maxon --log=lexer:trace");
			Console.Error.WriteLine("  MaxonSharp test --verbose --filter=addition");
			return 1;
		}

		if (!File.Exists(sourceFile)) {
			Console.Error.WriteLine($"Error: File not found: {sourceFile}");
			return 1;
		}

		var source = File.ReadAllText(sourceFile);
		var outputPath = Path.ChangeExtension(sourceFile, ".exe");
		string? hirOutputPath = null;
		string? lirOutputPath = null;
		if (emitIr) {
			hirOutputPath = Path.ChangeExtension(sourceFile, ".hir");
			lirOutputPath = Path.ChangeExtension(sourceFile, ".lir");
		}

		var success = Compiler.Compiler.Compile(source, outputPath, hirOutputPath, lirOutputPath);

		return success ? 0 : 1;
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
