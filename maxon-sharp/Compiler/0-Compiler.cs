using MaxonSharp.Compiler.Mlir.Conversion;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.X86;
using MaxonSharp.Compiler.Mlir.Emit;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler;

/// <summary>
/// Result of compiling source code to MLIR.
/// </summary>
public record CompileToMlirResult(
	string? MaxonIr,
	string? StandardIr,
	string? X86Ir,
	bool Success,
	string? Error
);

public class Compiler {
	/// <summary>
	/// Compile source code and return MLIR at various dialect levels.
	/// </summary>
	public static CompileToMlirResult CompileToMlir(string source) {
		try {
			// Stage 1: Lexing
			var lexer = new Lexer(source);
			var tokens = lexer.Tokenize();

			// Stage 2: Parsing
			var parser = new Parser(tokens);
			var ast = parser.Parse();

			// Stage 3: Semantic analysis
			var semanticAnalyzer = new SemanticAnalyzer();
			if (!semanticAnalyzer.Analyze(ast)) {
				return new CompileToMlirResult(null, null, null, false, "Semantic analysis failed");
			}

			// Stage 4: AST to Maxon Dialect
			var context = new MlirContext();
			var converter = new AstToMaxonConverter(context);
			var module = converter.ConvertProgram(ast);

			// Run Maxon passes (borrow checker, dead function elimination)
			var maxonPasses = new PassManager(context);
			maxonPasses.AddPass(new MaxonBorrowChecker());
			maxonPasses.AddPass(new DeadFunctionEliminationPass());
			maxonPasses.Run(module);

			// Write Maxon IR to string
			var printer = new MlirPrinter();
			module.Print(printer);
			var maxonIr = printer.ToString();

			// Lower Maxon → Standard dialects
			var maxonToStandardPatterns = new ConversionPatternSet();
			MaxonToStandardPatterns.PopulatePatterns(maxonToStandardPatterns);
			var maxonToStandard = new DialectConversionPass(maxonToStandardPatterns);
			maxonToStandard.AddLegalDialect("arith");
			maxonToStandard.AddLegalDialect("memref");
			maxonToStandard.AddLegalDialect("func");
			maxonToStandard.AddLegalDialect("cf");
			maxonToStandard.Run(module);

			// Run Standard passes (mem2reg, constant folding, DCE, dead store elimination)
			var standardPasses = new PassManager(context);
			standardPasses.AddPass(new Mem2RegPass());
			standardPasses.AddPass(new ConstantFoldingPass());
			standardPasses.AddPass(new DeadCodeEliminationPass());
			standardPasses.Run(module);

			// Dead store elimination (runs after DCE removes unused loads)
			var deadStorePass = new DeadStoreEliminationPass();
			deadStorePass.Run(module);

			// Print Standard IR
			var standardPrinter = new MlirPrinter();
			module.Print(standardPrinter);
			var standardIr = standardPrinter.ToString();

			// Lower Standard → X86 dialect
			var standardToX86Patterns = new ConversionPatternSet();
			StandardToX86Patterns.PopulatePatterns(standardToX86Patterns);
			var standardToX86 = new DialectConversionPass(standardToX86Patterns);
			standardToX86.AddLegalDialect("x86");
			standardToX86.Run(module);

			// Insert function frames (prologue/epilogue)
			var framePass = new FunctionFramePass();
			framePass.Run(module);

			// Register allocation
			var regAllocPass = new RegisterAllocationPass();
			regAllocPass.Run(module);

			// Peephole optimization (clean up redundant instructions)
			var peepholePass = new PeepholeOptimizationPass();
			peepholePass.Run(module);

			// Print X86 IR
			var x86Printer = new MlirPrinter();
			module.Print(x86Printer);
			var x86Ir = x86Printer.ToString();

			return new CompileToMlirResult(maxonIr, standardIr, x86Ir, true, null);
		} catch (CompileError ex) {
			return new CompileToMlirResult(null, null, null, false, ex.Format());
		} catch (Exception ex) {
			return new CompileToMlirResult(null, null, null, false, ex.Message);
		}
	}

	/// <summary>
	/// Compile using the new MLIR-based pipeline.
	/// </summary>
	public static bool CompileWithMlir(SourceFile[] sources, string outputPath, string? mlirOutputPath = null) {
		try {
			Logger.Debug(LogCategory.Compiler, "Starting MLIR-based compilation");

			// Phase 1: Parse all source files
			var asts = new List<ProgramAst>();
			int mainFileIndex = -1;

			for (int i = 0; i < sources.Length; i++) {
				var source = sources[i];
				var lexer = new Lexer(source.Content);
				var tokens = lexer.Tokenize();
				var parser = new Parser(tokens);
				var ast = parser.Parse();
				asts.Add(ast);

				if (ast.Functions.Any(f => f.Name == "main")) {
					mainFileIndex = i;
				}
			}

			if (mainFileIndex < 0) {
				Logger.Error(LogCategory.Compiler, "No 'main' function found");
				return false;
			}

			// Phase 2: Merge ASTs
			var program = ProgramAst.Merge(asts, mainFileIndex);

			// Phase 3: Semantic analysis
			var semanticAnalyzer = new SemanticAnalyzer();
			if (!semanticAnalyzer.Analyze(program)) {
				return false;
			}

			// Phase 4: AST → Maxon Dialect
			Logger.Debug(LogCategory.Compiler, "Phase 4: AST to Maxon dialect");
			var context = new MlirContext();
			var converter = new AstToMaxonConverter(context);
			var module = converter.ConvertProgram(program);

			// Phase 5: Run Maxon passes (borrow checker, dead function elimination)
			Logger.Debug(LogCategory.Compiler, "Phase 5: Maxon dialect passes");
			var passManager = new PassManager(context);
			passManager.AddPass(new MaxonBorrowChecker());
			passManager.AddPass(new DeadFunctionEliminationPass());
			passManager.Run(module);

			// Phase 6: Lower Maxon → Standard dialects
			Logger.Debug(LogCategory.Compiler, "Phase 6: Lower Maxon to Standard");
			var maxonToStandardPatterns = new ConversionPatternSet();
			MaxonToStandardPatterns.PopulatePatterns(maxonToStandardPatterns);
			var maxonToStandard = new DialectConversionPass(maxonToStandardPatterns);
			maxonToStandard.AddLegalDialect("arith");
			maxonToStandard.AddLegalDialect("memref");
			maxonToStandard.AddLegalDialect("func");
			maxonToStandard.AddLegalDialect("cf");
			maxonToStandard.Run(module);

			// Phase 7: Run Standard passes (mem2reg, constant folding, CSE, DCE)
			Logger.Debug(LogCategory.Compiler, "Phase 7: Standard dialect passes");
			var standardPasses = new PassManager(context);
			standardPasses.AddPass(new Mem2RegPass());
			standardPasses.AddPass(new ConstantFoldingPass());
			standardPasses.AddPass(new DeadCodeEliminationPass());
			standardPasses.Run(module);

			// Phase 7.5: Dead store elimination (after DCE removes unused loads)
			Logger.Debug(LogCategory.Compiler, "Phase 7.5: Dead store elimination");
			var deadStorePass = new DeadStoreEliminationPass();
			deadStorePass.Run(module);

			// Phase 8: Lower Standard → X86 dialect
			Logger.Debug(LogCategory.Compiler, "Phase 8: Lower Standard to X86");
			var standardToX86Patterns = new ConversionPatternSet();
			StandardToX86Patterns.PopulatePatterns(standardToX86Patterns);
			var standardToX86 = new DialectConversionPass(standardToX86Patterns);
			standardToX86.AddLegalDialect("x86");
			standardToX86.Run(module);

			// Phase 8.5: Insert function frames (prologue/epilogue)
			Logger.Debug(LogCategory.Compiler, "Phase 8.5: Insert function frames");
			var framePass = new FunctionFramePass();
			framePass.Run(module);

			// Phase 8.6: Register allocation
			Logger.Debug(LogCategory.Compiler, "Phase 8.6: Register allocation");
			var regAllocPass = new RegisterAllocationPass();
			regAllocPass.Run(module);

			// Phase 8.7: Peephole optimization (clean up redundant instructions)
			Logger.Debug(LogCategory.Compiler, "Phase 8.7: Peephole optimization");
			var peepholePass = new PeepholeOptimizationPass();
			peepholePass.Run(module);

			// Write MLIR if requested
			if (mlirOutputPath != null) {
				using var writer = new StreamWriter(mlirOutputPath);
				var printer = new MlirPrinter();
				module.Print(printer);
				writer.Write(printer.ToString());
			}

			// Phase 9: Emit machine code
			Logger.Debug(LogCategory.Compiler, "Phase 9: Emit X86 machine code");
			var emitter = new X86CodeEmitter();

			// Emit globals (define them in the data section)
			foreach (var global in module.Globals) {
				var size = global.Type.SizeInBytes;
				long initValue = 0;
				if (global.InitValue is IntegerAttr intAttr) {
					initValue = intAttr.Value;
				}
				emitter.DefineGlobal(global.Name, size, initValue);
			}

			// Emit main first (entry point must be at start of code section)
			var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main");
			if (mainFunc is null) {
				Logger.Error(LogCategory.Compiler, "No 'main' function found");
				return false;
			}

			EmitFunction(emitter, mainFunc);

			// Emit other functions
			foreach (var func in module.Functions.Where(f => f.Name != "main")) {
				EmitFunction(emitter, func);
			}

			emitter.ResolveLabels();

			// Resolve global references - data section follows code at next section alignment
			// For x86-64 PE, the data section RVA offset from code end is the difference between
			// their virtual addresses (both at section alignment)
			if (emitter.HasGlobals) {
				// Code is aligned to section boundary, data follows at next section
				// In RIP-relative addressing within code, we need the offset from instruction to data
				// The PE layout is: headers | code (padded) | data (padded)
				// Code section virtual address is 0x1000, data section virtual address is code_virt_end
				// For simplicity, data starts right after code in virtual memory terms
				var codeSize = (uint)emitter.GetCode().Length;
				var codeSizeVirtual = AlignUp(codeSize, 0x1000); // Section alignment
				var dataRvaOffset = (int)(codeSizeVirtual - codeSize);
				emitter.ResolveGlobals(dataRvaOffset);
			}

			var code = emitter.GetCode();
			var data = emitter.GetData();

			// Phase 10: Write PE
			Logger.Debug(LogCategory.Compiler, "Phase 10: Write PE executable");
			PeWriter.Write(outputPath, code, data);
			Logger.Info(LogCategory.Compiler, $"Wrote {code.Length} bytes code, {data.Length} bytes data to {outputPath}");

			return true;
		} catch (CompileError ex) {
			Logger.Error(LogCategory.Compiler, ex.Format());
			return false;
		} catch (Exception ex) {
			Logger.Error(LogCategory.Compiler, $"Compilation error: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Emits machine code for a single function.
	/// </summary>
	private static void EmitFunction(X86CodeEmitter emitter, MlirFunction func) {
		emitter.DefineLabel(func.Name);

		foreach (var block in func.Body.Blocks) {
			if (block.Name != "entry") {
				emitter.DefineLabel(block.Name);
			}

			foreach (var op in block.Operations) {
				if (op is X86Op x86Op) {
					emitter.Emit(x86Op);
				}
			}
		}
	}

	/// <summary>
	/// Compile multiple source files into a single executable.
	/// Uses the MLIR-based compilation pipeline.
	/// </summary>
	public static bool Compile(SourceFile[] sources, string outputPath, string? mlirOutputPath = null) {
		return CompileWithMlir(sources, outputPath, mlirOutputPath);
	}

	private static uint AlignUp(uint value, uint alignment) {
		return (value + alignment - 1) & ~(alignment - 1);
	}
}
