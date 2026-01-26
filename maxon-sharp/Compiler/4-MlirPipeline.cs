using MaxonSharp.Compiler.Mlir.Conversion;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler;

/// <summary>
/// Result of MLIR pipeline execution.
/// </summary>
public record MlirPipelineResult(
	MlirModule Module,
	string? X86Ir = null
);

/// <summary>
/// Stage 4: MLIR-based compilation pipeline.
/// Converts AST to X86 dialect through a series of dialect lowerings and optimizations.
/// </summary>
public class MlirPipeline {
	private readonly MlirContext _context;

	public MlirPipeline() {
		_context = new MlirContext();
	}

	/// <summary>
	/// Runs the full MLIR pipeline from AST to X86 dialect.
	/// </summary>
	/// <param name="program">The program AST to compile</param>
	/// <param name="mutationAnalyzer">Mutation analysis results for ownership tracking</param>
	/// <param name="returnIr">If true, include X86 IR in the result</param>
	public MlirPipelineResult Run(ProgramAst program, MutationAnalyzer mutationAnalyzer, bool returnIr = false) {
		// AST → Maxon Dialect
		Logger.Debug(LogCategory.Compiler, "Converting AST to Maxon dialect");
		var converter = new AstToMaxonConverter(mutationAnalyzer);
		var module = converter.ConvertProgram(program);

		// Maxon passes (borrow checker, dead function elimination)
		Logger.Debug(LogCategory.Compiler, "Running Maxon dialect passes");
		var passManager = new PassManager(_context);
		passManager.AddPass(new MaxonBorrowChecker());
		passManager.AddPass(new DeadFunctionEliminationPass());
		passManager.Run(module);

		// Lower Maxon → Standard dialects
		Logger.Debug(LogCategory.Compiler, "Lowering Maxon to Standard dialects");

		// Debug: print IR before Maxon->Standard lowering
		if (Logger.GetLevel(LogCategory.Compiler) <= LogLevel.Trace) {
			var preMaxonPrinter = new MlirPrinter();
			module.Print(preMaxonPrinter);
			Logger.Trace(LogCategory.Compiler, $"Before MaxonToStandard:\n{preMaxonPrinter}");
		}

		var maxonToStandardPatterns = new ConversionPatternSet();
		MaxonToStandardPatterns.PopulatePatterns(maxonToStandardPatterns);
		var maxonToStandard = new DialectConversionPass(maxonToStandardPatterns);
		maxonToStandard.AddLegalDialect("arith");
		maxonToStandard.AddLegalDialect("memref");
		maxonToStandard.AddLegalDialect("func");
		maxonToStandard.AddLegalDialect("cf");
		maxonToStandard.Run(module);

		// Debug: print IR after Maxon->Standard lowering
		if (Logger.GetLevel(LogCategory.Compiler) <= LogLevel.Trace) {
			var postMaxonPrinter = new MlirPrinter();
			module.Print(postMaxonPrinter);
			Logger.Trace(LogCategory.Compiler, $"After MaxonToStandard (before standard passes):\n{postMaxonPrinter}");
		}

		// Standard passes (mem2reg, constant folding, CSE, DCE)
		Logger.Debug(LogCategory.Compiler, "Running Standard dialect passes");
		var standardPasses = new PassManager(_context);
		standardPasses.AddPass(new Mem2RegPass());
		standardPasses.AddPass(new ConstantFoldingPass());
		standardPasses.AddPass(new DeadCodeEliminationPass());
		standardPasses.Run(module);

		// Dead store elimination (after DCE removes unused loads)
		Logger.Debug(LogCategory.Compiler, "Running dead store elimination");
		var deadStorePass = new DeadStoreEliminationPass();
		deadStorePass.Run(module);

		// Analyze allocas and compute frame layout BEFORE lowering
		// This must happen before StandardToX86 so LowerAllocaOp can use the computed offsets
		Logger.Debug(LogCategory.Compiler, "Computing stack frame layout");
		var frameLayoutPass = new FrameLayoutAnalysisPass();
		frameLayoutPass.Run(module);

		// Lower Standard → X86 dialect
		Logger.Debug(LogCategory.Compiler, "Lowering Standard to X86 dialect");

		// Debug: print IR before lowering
		if (Logger.GetLevel(LogCategory.Compiler) <= LogLevel.Trace) {
			var prePrinter = new MlirPrinter();
			module.Print(prePrinter);
			Logger.Trace(LogCategory.Compiler, $"Before StandardToX86:\n{prePrinter}");
		}

		var standardToX86Patterns = new ConversionPatternSet();
		StandardToX86Patterns.PopulatePatterns(standardToX86Patterns);
		var standardToX86 = new DialectConversionPass(standardToX86Patterns);
		standardToX86.AddLegalDialect("x86");
		standardToX86.Run(module);

		// Debug: print IR after lowering, before frame insertion
		if (Logger.GetLevel(LogCategory.Compiler) <= LogLevel.Trace) {
			var postPrinter = new MlirPrinter();
			module.Print(postPrinter);
			Logger.Trace(LogCategory.Compiler, $"After StandardToX86 (before frame):\n{postPrinter}");
		}

		// Insert function frames (prologue/epilogue)
		Logger.Debug(LogCategory.Compiler, "Inserting function frames");
		var framePass = new FunctionFramePass();
		framePass.Run(module);

		// Register allocation
		Logger.Debug(LogCategory.Compiler, "Allocating registers");
		var regAllocPass = new RegisterAllocationPass();
		regAllocPass.Run(module);

		// Debug: print IR after register allocation
		if (Logger.GetLevel(LogCategory.Compiler) <= LogLevel.Trace) {
			var postRegPrinter = new MlirPrinter();
			module.Print(postRegPrinter);
			Logger.Trace(LogCategory.Compiler, $"After RegisterAllocation:\n{postRegPrinter}");
		}

		// Peephole optimization (clean up redundant instructions)
		Logger.Debug(LogCategory.Compiler, "Running peephole optimization");
		var peepholePass = new PeepholeOptimizationPass();
		peepholePass.Run(module);

		// Capture X86 IR if requested
		string? x86Ir = null;
		if (returnIr) {
			var irPrinter = new MlirPrinter();
			module.Print(irPrinter);
			x86Ir = irPrinter.ToString();
		}

		return new MlirPipelineResult(module, x86Ir);
	}

	/// <summary>
	/// Writes the MLIR module to a file.
	/// </summary>
	public static void WriteMlirOutput(MlirModule module, string path) {
		using var writer = new StreamWriter(path);
		var printer = new MlirPrinter();
		module.Print(printer);
		writer.Write(printer.ToString());
	}
}
