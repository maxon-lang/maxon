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
	/// <param name="dumpStagesBasePath">If set, write IR at each pipeline stage</param>
	public MlirPipelineResult Run(ProgramAst program, MutationAnalyzer mutationAnalyzer, bool returnIr = false, string? dumpStagesBasePath = null) {
		// Helper to dump stage if requested
		void DumpStage(MlirModule mod, string stageName) {
			if (dumpStagesBasePath != null) {
				var path = $"{dumpStagesBasePath}.{stageName}.mlir";
				WriteMlirOutput(mod, path);
				Logger.Info(LogCategory.Compiler, $"Wrote {path}");
			}
		}

		// AST → Maxon Dialect
		Logger.Debug(LogCategory.Compiler, "Converting AST to Maxon dialect");
		var converter = new AstToMaxonConverter(mutationAnalyzer);
		var module = converter.ConvertProgram(program);

		// Maxon passes (borrow checker, inlining, dead function elimination)
		Logger.Debug(LogCategory.Compiler, "Running Maxon dialect passes");
		var passManager = new PassManager(_context);
		passManager.AddPass(new MaxonBorrowChecker());
		passManager.AddPass(new InliningPass());
		passManager.AddPass(new DeadFunctionEliminationPass());
		passManager.Run(module);

		// Renumber all SSA values after dead function elimination to get clean sequential IDs
		module.RenumberValues();

		DumpStage(module, "1-maxon");

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

		DumpStage(module, "2-standard");

		// Debug: print IR after Maxon->Standard lowering
		if (Logger.GetLevel(LogCategory.Compiler) <= LogLevel.Trace) {
			var postMaxonPrinter = new MlirPrinter();
			module.Print(postMaxonPrinter);
			Logger.Trace(LogCategory.Compiler, $"After MaxonToStandard (before standard passes):\n{postMaxonPrinter}");
		}

		// Standard passes (mem2reg, LICM, constant folding, jump threading, GVN, DCE)
		Logger.Debug(LogCategory.Compiler, "Running Standard dialect passes");
		var standardPasses = new PassManager(_context);
		standardPasses.AddPass(new Mem2RegPass());
		standardPasses.Run(module);

		DumpStage(module, "3-mem2reg");

		standardPasses = new PassManager(_context);
		standardPasses.AddPass(new LICMPass());
		standardPasses.AddPass(new ConstantFoldingPass());
		standardPasses.AddPass(new JumpThreadingPass()); // Re-enabled for testing
		standardPasses.AddPass(new GVNPass());
		standardPasses.AddPass(new DeadCodeEliminationPass());
		standardPasses.Run(module);

		// Dead store elimination (after DCE removes unused loads)
		Logger.Debug(LogCategory.Compiler, "Running dead store elimination");
		var deadStorePass = new DeadStoreEliminationPass();
		deadStorePass.Run(module);

		// Tail call optimization (mark tail calls before lowering)
		Logger.Debug(LogCategory.Compiler, "Running tail call optimization");
		var tailCallPass = new TailCallOptimizationPass();
		tailCallPass.Run(module);

		// Analyze allocas and compute frame layout BEFORE lowering
		// This must happen before StandardToX86 so LowerAllocaOp can use the computed offsets
		Logger.Debug(LogCategory.Compiler, "Computing stack frame layout");
		var frameLayoutPass = new FrameLayoutAnalysisPass();
		frameLayoutPass.Run(module);

		DumpStage(module, "4-optimized");

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

		DumpStage(module, "5-x86");

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

		DumpStage(module, "6-frame");

		// Register allocation
		Logger.Debug(LogCategory.Compiler, "Allocating registers");
		var regAllocPass = new RegisterAllocationPass();
		regAllocPass.Run(module);

		DumpStage(module, "7-regalloc");

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

		DumpStage(module, "8-final");

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
