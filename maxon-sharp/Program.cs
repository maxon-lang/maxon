namespace MaxonSharp;

class Program {
	static int Main(string[] args) {
		if (args.Length < 1) {
			Console.Error.WriteLine("Usage: MaxonSharp <source-file>");
			return 1;
		}

		var sourceFile = args[0];
		if (!File.Exists(sourceFile)) {
			Console.Error.WriteLine($"Error: File not found: {sourceFile}");
			return 1;
		}

		var source = File.ReadAllText(sourceFile);
		var outputPath = Path.ChangeExtension(sourceFile, ".exe");

		var success = Compiler.Compile(source, outputPath);

		return success ? 0 : 1;
	}
}
