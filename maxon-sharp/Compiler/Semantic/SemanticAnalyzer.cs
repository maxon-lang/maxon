using MaxonSharp.Parser;

namespace MaxonSharp.Semantic;

public class SemanticAnalyzer {
	public static bool Analyze(ProgramAst program) {
		Logger.Info(LogCategory.Semantic, "Starting semantic analysis");

		// Check that main function exists
		Logger.Debug(LogCategory.Semantic, "Checking for main function");
		var mainFunc = program.Functions.Find(f => f.Name == "main");
		if (mainFunc == null) {
			Console.Error.WriteLine("Error: No 'main' function found");
			return false;
		}

		// Check that main returns int
		Logger.Debug(LogCategory.Semantic, "Validating main return type");
		if (mainFunc.ReturnType is not SimpleTypeRef { Name: "int" }) {
			Console.Error.WriteLine("Error: 'main' function must return int");
			return false;
		}

		Logger.Info(LogCategory.Semantic, "Semantic analysis complete");
		return true;
	}
}
