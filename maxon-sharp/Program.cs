namespace MaxonSharp;

class Program {
	static int Main(string[] args) {
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
			Console.Error.WriteLine();
			Console.Error.WriteLine("Options:");
			Console.Error.WriteLine("  --emit-ir              Write .hir and .lir files");
			Console.Error.WriteLine("  --log=LEVEL            Set all log categories to LEVEL");
			Console.Error.WriteLine("  --log=CATEGORY:LEVEL   Set specific category to LEVEL");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Log levels: none, error, info, debug, trace");
			Console.Error.WriteLine("Log categories: compiler, lexer, parser, semantic, hir, lir, codegen, pe");
			Console.Error.WriteLine();
			Console.Error.WriteLine("Examples:");
			Console.Error.WriteLine("  maxon-sharp test.maxon --log=debug");
			Console.Error.WriteLine("  maxon-sharp test.maxon --log=lexer:trace");
			Console.Error.WriteLine("  maxon-sharp test.maxon --log=parser:debug --log=codegen:trace");
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

		var success = Compiler.Compile(source, outputPath, hirOutputPath, lirOutputPath);

		return success ? 0 : 1;
	}
}
