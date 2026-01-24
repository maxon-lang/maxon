using MaxonSharp.Parser;

namespace MaxonSharp.Semantic;

public class SemanticAnalyzer {
	private MutationAnalyzer? _mutationAnalyzer;

	/// <summary>
	/// Get the mutation analyzer after analysis is complete.
	/// </summary>
	public MutationAnalyzer? MutationAnalyzer => _mutationAnalyzer;

	public bool Analyze(ProgramAst program) {
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

		// Run mutation analysis for ownership tracking
		Logger.Debug(LogCategory.Semantic, "Running mutation analysis");
		_mutationAnalyzer = new MutationAnalyzer();
		_mutationAnalyzer.Analyze(program);

		Logger.Info(LogCategory.Semantic, "Semantic analysis complete");
		return true;
	}

	/// <summary>
	/// Static helper for simple analysis without mutation tracking.
	/// </summary>
	public static bool AnalyzeSimple(ProgramAst program) {
		var analyzer = new SemanticAnalyzer();
		return analyzer.Analyze(program);
	}
}
