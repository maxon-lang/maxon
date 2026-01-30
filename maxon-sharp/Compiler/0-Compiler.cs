using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public record CompileResult(
	bool Success,
	string? Error,
	string? AllStagesIr = null
) {
	/// <summary>
	/// Extracts the x86 stage IR from AllStagesIr for unit test compatibility.
	/// </summary>
	public string? X86Ir {
		get {
			if (AllStagesIr == null) return null;
			var marker = $"=== {PipelineStages.X86}";
			var idx = AllStagesIr.IndexOf(marker);
			if (idx < 0) return null;
			var start = idx + marker.Length;
			// Skip the newline after the marker
			if (start < AllStagesIr.Length && AllStagesIr[start] == '\n') start++;
			// Find the next stage marker or end of string
			var nextMarker = AllStagesIr.IndexOf("\n=== ", start);
			var end = nextMarker >= 0 ? nextMarker : AllStagesIr.Length;
			return AllStagesIr[start..end].TrimEnd();
		}
	}
};

public class Compiler {
	private readonly MlirContext _context = new();

	public CompileResult Compile(SourceFile[] sources, string outputPath, string? mlirOutputPath = null, bool returnIr = false, string? dumpStagesBasePath = null) {
		var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

		using var _ = _context.PushScope();

		try {
			Logger.Debug(LogCategory.Compiler, "Starting MLIR-based compilation");

			// Stage 1-2: Lex and parse all source files into MLIR modules
			var module = new MlirModule<MaxonOp>();

			for (int i = 0; i < sources.Length; i++) {
				var source = sources[i];
				var lexer = new Lexer(source.Content);
				var tokens = lexer.Tokenize();
				var parser = new Parser(tokens);
				var parsed = parser.Parse();
				module.Merge(parsed);
			}

			// Stage 3-4: MLIR pipeline (semantic checks + dialect lowering)
			var pipeline = new MlirPipeline();
			var mlirResult = MlirPipeline.Run(module, returnIr, dumpStagesBasePath);

			// Write MLIR if requested
			if (mlirOutputPath != null) {
				MlirPipeline.WriteMlirOutput(mlirResult.Module, mlirOutputPath);
			}

			// Stage 5: Code emission (X86 dialect -> machine code)
			var codeResult = CodeEmitter.Emit(mlirResult.Module);

			// Stage 6: Write PE executable
			PeWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Imports);
			Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Imports.Count} imports to {outputPath}");

			return new CompileResult(true, null, mlirResult.AllStagesIr);
		} catch (CompileError ex) {
			if (ex.FilePath == null && userSourceFile != null) {
				ex.FilePath = userSourceFile;
			}
			return new CompileResult(false, ex.Format());
		} catch (Exception ex) {
			return new CompileResult(false, $"{ex.Message}\n{ex.StackTrace}");
		}
	}
}

public static class StdlibLoader {
	private static readonly string[] WhitelistedModules = [
		"Math.maxon",
		"Pair.maxon",
		"Interfaces.maxon",
		"Array.maxon",
		"String.maxon",
		"Character.maxon",
		"helpers/string/_grapheme.maxon",
		"helpers/string/_hash.maxon",
		"helpers/string/_search.maxon",
		"helpers/string/_utf16.maxon",
		"helpers/string/_utf8.maxon",
		"helpers/string/_views.maxon"
	];

	public static string? FindStdlibPath() {
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

public record SourceFile(string Path, string Content);
