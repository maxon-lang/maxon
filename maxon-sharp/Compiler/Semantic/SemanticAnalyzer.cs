using MaxonSharp.Parser;

namespace MaxonSharp.Semantic;

public class SemanticAnalyzer {
	public static bool Analyze(ProgramAst program) {
		// Check that main function exists
		var mainFunc = program.Functions.Find(f => f.Name == "main");
		if (mainFunc == null) {
			Console.Error.WriteLine("Error: No 'main' function found");
			return false;
		}

		// Check that main returns int
		if (mainFunc.ReturnType is not SimpleTypeRef { Name: "int" }) {
			Console.Error.WriteLine("Error: 'main' function must return int");
			return false;
		}

		return true;
	}
}
