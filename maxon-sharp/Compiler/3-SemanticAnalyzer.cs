namespace MaxonSharp.Compiler;

public class SemanticAnalyzer {
	private MutationAnalyzer? _mutationAnalyzer;

	/// <summary>
	/// Get the mutation analyzer after analysis is complete.
	/// </summary>
	public MutationAnalyzer? MutationAnalyzer => _mutationAnalyzer;

	/// <summary>
	/// Analyze a program AST. Requires a 'main' function to be present.
	/// </summary>
	public bool Analyze(ProgramAst program) {
		return Analyze(program, requireMain: true);
	}

	/// <summary>
	/// Analyze a program AST.
	/// </summary>
	/// <param name="program">The program AST to analyze.</param>
	/// <param name="requireMain">If true, requires a 'main' function to be present.</param>
	public bool Analyze(ProgramAst program, bool requireMain) {
		Logger.Debug(LogCategory.Semantic, "Starting semantic analysis");

		if (requireMain) {
			// Check that main function exists
			Logger.Debug(LogCategory.Semantic, "Checking for main function");
			var mainFunc = program.Functions.Find(f => f.Name == "main");
			if (mainFunc == null) {
				Logger.Error(LogCategory.Semantic, "No 'main' function found");
				return false;
			}

			// Check that main returns int
			Logger.Debug(LogCategory.Semantic, "Validating main return type");
			if (mainFunc.ReturnType is not SimpleTypeRef { Name: "int" }) {
				Logger.Error(LogCategory.Semantic, "'main' function must return int");
				return false;
			}
		}

		// Run mutation analysis for ownership tracking
		Logger.Debug(LogCategory.Semantic, "Running mutation analysis");
		_mutationAnalyzer = new MutationAnalyzer();
		_mutationAnalyzer.Analyze(program);

		Logger.Debug(LogCategory.Semantic, "Semantic analysis complete");
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
