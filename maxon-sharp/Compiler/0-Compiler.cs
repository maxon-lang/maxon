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

  public CompileResult Compile(SourceFile[] sources, string outputPath, string? mlirOutputPath = null, bool returnIr = false, string? dumpStagesBasePath = null, bool trackAllocs = false) {
    var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

    using var _ = _context.PushScope();

    try {
      Logger.Debug(LogCategory.Compiler, "Starting MLIR-based compilation");

      // Stage 1-2: Lex and parse all source files into MLIR modules
      // Use cached stdlib module, then parse user code into a clone
      var module = StdlibLoader.GetStdlibModule();

      // Reset IDs so user code starts at %0
      _context.ResetIds();

      CompileSources(module, sources, false);

      // Stage 3-4: MLIR pipeline (semantic checks + dialect lowering)
      var pipeline = new MlirPipeline();
      var mlirResult = MlirPipeline.Run(module, returnIr, dumpStagesBasePath, trackAllocs);

      // Write MLIR if requested
      if (mlirOutputPath != null) {
        MlirPipeline.WriteMlirOutput(mlirResult.Module, mlirOutputPath);
      }

      // Stage 5: Code emission (X86 dialect -> machine code)
      var codeResult = CodeEmitter.Emit(mlirResult.Module, trackAllocs);

      // Stage 6: Write PE executable
      PeWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Imports, codeResult.Symdata);
      Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Symdata.Length} bytes symdata, {codeResult.Imports.Count} imports to {outputPath}");

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

  public static List<CompileError> Check(string filePath, string content) {
    var context = new MlirContext();
    using var _ = context.PushScope();
    var errors = new List<CompileError>();

    try {
      var stdlibSources = StdlibLoader.LoadStdlibModules();

      // If checking a stdlib file, replace its content in the stdlib sources
      var normalizedPath = Path.GetFullPath(filePath);
      var stdlibIndex = Array.FindIndex(stdlibSources,
        s => Path.GetFullPath(s.Path) == normalizedPath);

      if (stdlibIndex >= 0) {
        // Stdlib file changed - must re-parse stdlib from scratch
        var module = new MlirModule<MaxonOp>();
        var modifiedSources = (SourceFile[])stdlibSources.Clone();
        modifiedSources[stdlibIndex] = new SourceFile(filePath, content);
        CompileSources(module, modifiedSources, true);
      } else {
        var module = StdlibLoader.GetStdlibModule();
        context.ResetIds();
        CompileSources(module, [new SourceFile(filePath, content)], false);
      }
    } catch (CompileError ex) {
      ex.FilePath ??= filePath;
      errors.Add(ex);
    }

    return errors;
  }

  internal static void CompileSources(MlirModule<MaxonOp> module, SourceFile[] sources, bool isStdLib) {
    // Pre-register type names from all sources so cross-file references resolve
    // (e.g., Character.maxon references String before String.maxon is parsed)
    foreach (var source in sources)
      PreRegisterTypeNames(module, source, isStdLib);

    // Pre-scan top-level typealiases from all sources so cross-file typealias
    // references resolve regardless of file processing order
    foreach (var source in sources) {
      try {
        var lexer = new Lexer(source.Content);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path);
        parser.PreScanTypeAliasesOnly(module);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        throw;
      }
    }

    // Pre-scan all sources to register function signatures, type details, etc.
    // so that cross-file forward references resolve regardless of parse order
    foreach (var source in sources) {
      try {
        var lexer = new Lexer(source.Content);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path);
        parser.PreScan(module);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        throw;
      }
    }

    // Typealias type params may reference placeholder types from PreScan (e.g.,
    // `FooArray = Array with FooEnum` prescanned before FooEnum gets its cases).
    // Now that all types are fully defined, update the references.
    RefreshTypeAliasTypeParams(module);

    // Full parse with all signatures known
    foreach (var source in sources) {
      try {
        var lexer = new Lexer(source.Content);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path);
        var parsed = parser.Parse();
        module.Merge(parsed);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        throw;
      }
    }
  }

  private static void RefreshTypeAliasTypeParams(MlirModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not MlirStructType structType || structType.TypeParams.Count == 0)
        continue;
      foreach (var key in structType.TypeParams.Keys.ToList()) {
        var paramType = structType.TypeParams[key];
        if (paramType is MlirTypeParameterType)
          continue;
        if (module.TypeDefs.TryGetValue(paramType.Name, out var currentType) && currentType != paramType)
          structType.TypeParams[key] = currentType;
      }
    }
  }

  private static void PreRegisterTypeNames(MlirModule<MaxonOp> module, SourceFile source, bool isStdlib = false) {
    var lexer = new Lexer(source.Content);
    var tokens = lexer.Tokenize();
    for (int i = 0; i < tokens.Count - 1; i++) {
      var t = tokens[i];
      bool isExported = false;
      if (t.Type == TokenType.Export && i + 1 < tokens.Count) {
        isExported = true;
        i++;
        t = tokens[i];
      }

      if (t.Type == TokenType.Type && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var name = nameToken.Value;
        var assocNames = ParseUsesClauseTokens(tokens, i + 2);
        var structType = new MlirStructType(name, [], assocNames);
        SetSourceLocation(structType, source, nameToken);
        module.TypeDefs.TryAdd(name, structType);
        if (!isExported && !isStdlib)
          module.NonExportedTypeNames.Add(name);
        if (source.Path != null) module.TypeDefSourceFiles[name] = source.Path;
        i += 1;
      } else if ((t.Type == TokenType.Union || t.Type == TokenType.Enum) && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var typeName = nameToken.Value;
        MlirType namedType = t.Type == TokenType.Union
          ? new MlirEnumType(typeName, [], null, [])
          : new MlirConstantsType(typeName, []);
        SetSourceLocation(namedType, source, nameToken);
        module.TypeDefs.TryAdd(typeName, namedType);
        if (!isExported && !isStdlib) module.NonExportedTypeNames.Add(typeName);
        if (source.Path != null) module.TypeDefSourceFiles[typeName] = source.Path;
        i += 1;
      } else if (t.Type == TokenType.Interface && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var ifaceName = nameToken.Value;
        var ifaceType = new MlirInterfaceType(ifaceName, []);
        SetSourceLocation(ifaceType, source, nameToken);
        module.TypeDefs.TryAdd(ifaceName, ifaceType);
        var assocNames = ParseUsesClauseTokens(tokens, i + 2);
        if (assocNames.Count > 0)
          module.InterfaceAssociatedTypes.TryAdd(ifaceName, assocNames);
        i += 1;
      }
    }
  }

  private static void SetSourceLocation(MlirType type, SourceFile source, Token nameToken) {
    type.SourceFilePath = source.Path;
    type.SourceLine = nameToken.Line;
    type.SourceColumn = nameToken.Column;
  }

  /// <summary>
  /// Token-level extraction of `uses A, B, C` clause from a type declaration.
  /// </summary>
  private static List<string> ParseUsesClauseTokens(List<Token> tokens, int startPos) {
    var names = new List<string>();
    if (startPos >= tokens.Count || tokens[startPos].Type != TokenType.Uses)
      return names;
    int pos = startPos + 1; // skip 'uses'
    while (pos < tokens.Count && tokens[pos].Type == TokenType.Identifier) {
      names.Add(tokens[pos].Value);
      pos++;
      if (pos < tokens.Count && tokens[pos].Type == TokenType.Comma)
        pos++;
      else
        break;
    }
    return names;
  }
}

public static class StdlibLoader {
  private static SourceFile[]? _cachedSources;
  private static MlirModule<MaxonOp>? _cachedStdlibModule;
  private static readonly object _stdlibLock = new();

  /// Returns a cached parsed stdlib module clone ready for user code compilation.
  /// The clone has all functions marked IsStdlib=true.
  public static MlirModule<MaxonOp> GetStdlibModule() {
    if (_cachedStdlibModule != null)
      return _cachedStdlibModule.Clone();

    lock (_stdlibLock) {
      if (_cachedStdlibModule != null)
        return _cachedStdlibModule.Clone();

      var context = new MlirContext();
      using var _ = context.PushScope();
      var module = new MlirModule<MaxonOp>();
      var sources = LoadStdlibModules();
      Compiler.CompileSources(module, sources, true);
      foreach (var func in module.Functions) {
        func.IsStdlib = true;
        func.IsExported = true;
      }
      _cachedStdlibModule = module;
      return module.Clone();
    }
  }

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
    if (_cachedSources != null) return _cachedSources;

    var stdlibPath = FindStdlibPath();
    if (stdlibPath == null) return [];

    var files = Directory.GetFiles(stdlibPath, "*.maxon", SearchOption.AllDirectories);

    // Sort: Interfaces.maxon first (foundational typealiases), then helper files
    // (in subdirectories), then remaining top-level files, alphabetically within each group
    Array.Sort(files, (a, b) => {
      var aIsInterfaces = Path.GetFileName(a) == "Interfaces.maxon" && Path.GetDirectoryName(a) == stdlibPath;
      var bIsInterfaces = Path.GetFileName(b) == "Interfaces.maxon" && Path.GetDirectoryName(b) == stdlibPath;
      if (aIsInterfaces != bIsInterfaces) return aIsInterfaces ? -1 : 1;
      var aIsHelper = Path.GetDirectoryName(a) != stdlibPath;
      var bIsHelper = Path.GetDirectoryName(b) != stdlibPath;
      if (aIsHelper != bIsHelper) return aIsHelper ? -1 : 1;
      return string.Compare(a, b, StringComparison.Ordinal);
    });

    var sources = new List<SourceFile>();
    foreach (var filePath in files)
      sources.Add(new SourceFile(filePath, File.ReadAllText(filePath)));
    _cachedSources = [.. sources];
    return _cachedSources;
  }

  public static SourceFile[] PrependStdlib(SourceFile[] stdlibSources, SourceFile[] userSources) {
    var combined = new SourceFile[stdlibSources.Length + userSources.Length];
    stdlibSources.CopyTo(combined, 0);
    userSources.CopyTo(combined, stdlibSources.Length);
    return combined;
  }
}

public record SourceFile(string Path, string Content);
