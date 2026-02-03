using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

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
      // Parse stdlib first as a seed module, then reset IDs for user code
      var module = new MlirModule<MaxonOp>();

      var stdlibSources = StdlibLoader.LoadStdlibModules();
      CompileSources(module, stdlibSources, true);

      foreach (var func in module.Functions)
        func.IsStdlib = true;

      // Reset IDs so user code starts at %0
      _context.ResetIds();

      CompileSources(module, sources, false);

      // Remove unreachable functions (e.g. unused stdlib functions)
      DeadFunctionElimination.Run(module);

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

  private static void CompileSources(MlirModule<MaxonOp> module, SourceFile[] sources, bool isStdLib) {
    // Pre-register type names from all sources so cross-file references resolve
    // (e.g., Character.maxon references String before String.maxon is parsed)
    foreach (var source in sources)
      PreRegisterTypeNames(module, source);

    foreach (var source in sources) {
      try {
        var lexer = new Lexer(source.Content);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, module, isStdlib: isStdLib);
        var parsed = parser.Parse();
        module.Merge(parsed);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        throw;
      }
    }
  }

  private static void PreRegisterTypeNames(MlirModule<MaxonOp> module, SourceFile source) {
    var lexer = new Lexer(source.Content);
    var tokens = lexer.Tokenize();
    for (int i = 0; i < tokens.Count - 1; i++) {
      var t = tokens[i];
      if (t.Type == TokenType.Export && i + 2 < tokens.Count
          && tokens[i + 1].Type == TokenType.Type
          && tokens[i + 2].Type == TokenType.Identifier) {
        module.TypeDefs.TryAdd(tokens[i + 2].Value, new MlirStructType(tokens[i + 2].Value, []));
        i += 2;
      } else if (t.Type == TokenType.Type && tokens[i + 1].Type == TokenType.Identifier) {
        module.TypeDefs.TryAdd(tokens[i + 1].Value, new MlirStructType(tokens[i + 1].Value, []));
        i += 1;
      }
    }
  }
}

public static class StdlibLoader {
  private static readonly string[] WhitelistedModules = [
    "Interfaces.maxon",
    "Array.maxon",
    "Vector.maxon",
    "Math.maxon",
    "Pair.maxon",
    "Set.maxon",
    "helpers/string/_utf8.maxon",
    "helpers/string/_hash.maxon",
    "helpers/string/_grapheme.maxon",
    "helpers/string/_search.maxon",
    "helpers/string/_utf16.maxon",
    "String.maxon",
    "helpers/string/_views.maxon",
    "Character.maxon",
    "Print.maxon"
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
