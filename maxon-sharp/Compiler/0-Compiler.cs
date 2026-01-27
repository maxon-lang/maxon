using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

/// <summary>
/// Result of compilation.
/// </summary>
public record CompileResult(
	bool Success,
	string? Error,
	string? X86Ir = null
);

public class Compiler {
	/// <summary>
	/// Compile source files using the MLIR-based pipeline.
	/// </summary>
	/// <param name="sources">Source files to compile</param>
	/// <param name="outputPath">Path for the output executable</param>
	/// <param name="mlirOutputPath">Optional path to write MLIR output</param>
	/// <param name="returnIr">If true, include X86 IR in the result</param>
	public static CompileResult Compile(SourceFile[] sources, string outputPath, string? mlirOutputPath = null, bool returnIr = false) {
		// Track the original user source file for error reporting (before prepending stdlib)
		var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

		try {
			// Reset global ID counters for each compilation (important for parallel test runs)
			MlirValue.ResetIdCounter();
			MlirBlock.ResetIdCounter();
			VRegOperand.ResetTempIdCounter();

			Logger.Debug(LogCategory.Compiler, "Starting MLIR-based compilation");

			// Load stdlib and prepend to sources
			var stdlibSources = StdlibLoader.LoadStdlibModules();
			sources = StdlibLoader.PrependStdlib(stdlibSources, sources);

			// Stage 1: Parse all source files
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
				return new CompileResult(false, "No 'main' function found");
			}

			// Stage 2: Merge ASTs
			var program = ProgramAst.Merge(asts, mainFileIndex);

			// Stage 3: Semantic analysis
			var semanticAnalyzer = new SemanticAnalyzer();
			if (!semanticAnalyzer.Analyze(program)) {
				return new CompileResult(false, "Semantic analysis failed");
			}

			// MLIR pipeline (AST → Maxon → Standard → X86 dialect)
			var pipeline = new MlirPipeline();
			var mlirResult = pipeline.Run(program, semanticAnalyzer.MutationAnalyzer!, returnIr);

			// Write MLIR if requested
			if (mlirOutputPath != null) {
				MlirPipeline.WriteMlirOutput(mlirResult.Module, mlirOutputPath);
			}

			// Code emission (X86 dialect → machine code)
			var codeResult = CodeEmitter.Emit(mlirResult.Module);

			// Write PE executable
			PeWriter.Write(outputPath, codeResult.Code, codeResult.Data);
			Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Data.Length} bytes data to {outputPath}");

			return new CompileResult(true, null, mlirResult.X86Ir);
		} catch (CompileError ex) {
			// For single-file compilation, add file path to error if not already set
			if (ex.FilePath == null && userSourceFile != null) {
				ex.FilePath = userSourceFile;
			}
			return new CompileResult(false, ex.Format());
		} catch (Exception ex) {
			return new CompileResult(false, ex.Message);
		}
	}
}

public static class StdlibLoader {
	private static readonly string[] WhitelistedModules = ["Math.maxon"];

	public static string? FindStdlibPath() {
		// Search exe_dir/stdlib, exe_dir/../stdlib, exe_dir/../../stdlib, etc.
		// Check closer paths first to avoid finding wrong stdlib at root
		// Use AppContext.BaseDirectory for single-file app compatibility
		var exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
		Logger.Debug(LogCategory.Compiler, $"StdlibLoader: exeDir={exeDir}");
		if (string.IsNullOrEmpty(exeDir)) return null;

		foreach (var levels in new[] { 0, 1, 2, 3, 4, 5 }) {
			var path = exeDir;
			for (int i = 0; i < levels && path != null; i++)
				path = Path.GetDirectoryName(path);

			if (path != null) {
				var stdlibPath = Path.Combine(path, "stdlib");
				Logger.Debug(LogCategory.Compiler, $"StdlibLoader: checking {stdlibPath}");
				if (Directory.Exists(stdlibPath)) {
					Logger.Debug(LogCategory.Compiler, $"StdlibLoader: found stdlib at {stdlibPath}");
					return stdlibPath;
				}
			}
		}
		Logger.Debug(LogCategory.Compiler, "StdlibLoader: stdlib not found");
		return null;
	}

	public static SourceFile[] LoadStdlibModules() {
		var stdlibPath = FindStdlibPath();
		if (stdlibPath == null) return [];

		var sources = new List<SourceFile>();
		foreach (var module in WhitelistedModules) {
			var filePath = Path.Combine(stdlibPath, module);
			if (File.Exists(filePath))
				sources.Add(new SourceFile(filePath, File.ReadAllText(filePath)));
		}
		return [.. sources];
	}

	public static SourceFile[] PrependStdlib(SourceFile[] stdlibSources, SourceFile[] userSources) {
		var combined = new SourceFile[stdlibSources.Length + userSources.Length];
		stdlibSources.CopyTo(combined, 0);
		userSources.CopyTo(combined, stdlibSources.Length);
		return combined;
	}
}
