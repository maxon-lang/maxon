namespace MaxonSharp;

class Program {
	static int Main(string[] args) {
		var emitIr = false;
		string? sourceFile = null;

		foreach (var arg in args) {
			if (arg == "--emit-ir") {
				emitIr = true;
			} else if (!arg.StartsWith('-')) {
				sourceFile = arg;
			}
		}

		if (sourceFile == null) {
			Console.Error.WriteLine("Usage: MaxonSharp [--emit-ir] <source-file>");
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
