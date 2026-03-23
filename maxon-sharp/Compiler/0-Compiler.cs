using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Passes;

namespace MaxonSharp.Compiler;

public record CompileTarget(string Arch, string Os) {
  public static CompileTarget Default => Native;

  public static CompileTarget Native {
    get {
      var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch {
        System.Runtime.InteropServices.Architecture.Arm64 => "aarch64",
        System.Runtime.InteropServices.Architecture.X64 => "x86_64",
        var unsupported => throw new PlatformNotSupportedException($"Unsupported architecture: {unsupported}")
      };
      var os = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
        System.Runtime.InteropServices.OSPlatform.OSX) ? "macos" :
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
          System.Runtime.InteropServices.OSPlatform.Linux) ? "linux" : "windows";
      return new CompileTarget(arch, os);
    }
  }

  /// <summary>
  /// Maps CompileTarget.Os to the Parser's targetOs parameter value.
  /// </summary>
  public string ParserOs => Os.ToLowerInvariant() switch {
    "macos" => "Macos",
    "windows" => "Windows",
    "linux" => "Linux",
    var unknown => throw new ArgumentException($"Unknown OS '{unknown}' in CompileTarget. Expected macos, windows, or linux.")
  };

  /// <summary>
  /// Parses a target triple string like "aarch64-macos" into a CompileTarget.
  /// </summary>
  public static CompileTarget Parse(string triple) {
    var parts = triple.Split('-', 2);
    if (parts.Length != 2)
      throw new ArgumentException($"Invalid target format '{triple}'. Expected 'arch-os' (e.g., 'aarch64-macos').");
    return new CompileTarget(parts[0], parts[1]);
  }
}

public record CompileResult(
  bool Success,
  string? Error,
  string? AllStagesIr = null
) {
  /// <summary>
  /// Extracts the architecture-specific stage IR (x86 or arm64) from AllStagesIr.
  /// </summary>
  public string? ArchIr {
    get {
      if (AllStagesIr == null) return null;
      // Find the last === marker (the arch-specific stage)
      var lastMarker = AllStagesIr.LastIndexOf("\n=== ");
      if (lastMarker < 0) return null;
      var start = lastMarker + 1; // skip the leading newline
      // Skip past the marker line itself
      var lineEnd = AllStagesIr.IndexOf('\n', start);
      if (lineEnd < 0) return null;
      return AllStagesIr[(lineEnd + 1)..].TrimEnd();
    }
  }
};

public class Compiler {
  private readonly MlirContext _context = new();

  [ThreadStatic] private static bool _mmTrace;
  public static bool MmTrace { get => _mmTrace; set => _mmTrace = value; }

  [ThreadStatic] private static bool _mmDebug;
  public static bool MmDebug { get => _mmDebug; set => _mmDebug = value; }

  [ThreadStatic] private static bool _asyncTrace;
  public static bool AsyncTrace { get => _asyncTrace; set => _asyncTrace = value; }

  [ThreadStatic] private static bool _testing;
  public static bool Testing { get => _testing; set => _testing = value; }

  public CompileResult Compile(SourceFile[] sources, string outputPath, string? mlirOutputPath = null, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null) {
    target ??= CompileTarget.Default;
    var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

    using var _ = _context.PushScope();

    try {
      var stageSw = System.Diagnostics.Stopwatch.StartNew();
      Logger.Debug(LogCategory.Compiler, "Starting MLIR-based compilation");

      // Stage 1-2: Lex and parse all source files into MLIR modules
      // Use cached stdlib module, then parse user code into a clone
      var module = StdlibLoader.GetStdlibModule();

      // Reset IDs so user code starts at %0
      _context.ResetIds();
      MaxonPanicOp.ResetPanicLabels();
      Parser.ResetClosureCounter();

      CompileSources(module, sources, false, target);
      var parseMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

      // Stage 3-4: MLIR pipeline (semantic checks + dialect lowering)
      var pipeline = new MlirPipeline();
      var mlirResult = MlirPipeline.Run(module, returnIr, dumpStagesBasePath, target);
      var pipelineMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

      // Write MLIR if requested
      if (mlirOutputPath != null) {
        if (mlirResult.X86Module != null)
          MlirPipeline.WriteMlirOutput(mlirResult.X86Module, mlirOutputPath);
        else if (mlirResult.ARM64Module != null)
          MlirPipeline.WriteMlirOutput(mlirResult.ARM64Module, mlirOutputPath);
      }

      if (target.Arch == "aarch64") {
        // Stage 5: Code emission (ARM64 dialect -> machine code)
        var codeResult = ARM64CodeEmitterStage.Emit(mlirResult.ARM64Module!);
        var emitMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

        // Stage 6: Write Mach-O executable
        MachOWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, symdata: codeResult.Symdata, got: codeResult.Got, importNames: codeResult.ImportNames);
        var writeMs = stageSw.ElapsedMilliseconds;
        Logger.Trace(LogCategory.Compiler, $"Stages: parse={parseMs}ms pipeline={pipelineMs}ms emit={emitMs}ms write={writeMs}ms");
        Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata to {outputPath}");
      } else if (target.Arch == "x86_64") {
        // Stage 5: Code emission (X86 dialect -> machine code)
        var codeResult = X86CodeEmitter.Emit(mlirResult.X86Module!);

        // Stage 6: Write PE executable
        PeWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, codeResult.Imports, codeResult.Symdata, codeResult.CoffSymbols);
        Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata, {codeResult.Imports.Count} imports to {outputPath}");
      } else {
        throw new InvalidOperationException($"Unsupported target architecture: {target.Arch}");
      }

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
        MaxonPanicOp.ResetPanicLabels();
        Parser.ResetClosureCounter();
        CompileSources(module, [new SourceFile(filePath, content)], false);
      }
    } catch (CompileError ex) {
      ex.FilePath ??= filePath;
      errors.Add(ex);
    }

    return errors;
  }

  internal static void CompileSources(MlirModule<MaxonOp> module, SourceFile[] sources, bool isStdLib, CompileTarget? target = null) {
    target ??= CompileTarget.Default;
    var parserOs = target.ParserOs;
    var parserArch = target.Arch;

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
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch);
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
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch);
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
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch);
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
          ? new MlirUnionType(typeName, [], null, [])
          : new MlirEnumType(typeName, []);
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
      } else if (t.Type == TokenType.TypeAlias && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        // Pre-register typealias names as placeholders so cross-file references
        // resolve regardless of file processing order during PreScanTypeAliasesOnly
        var nameToken = tokens[i + 1];
        var aliasName = nameToken.Value;
        if (!module.TypeDefs.ContainsKey(aliasName)) {
          var placeholder = new MlirStructType(aliasName, []);
          SetSourceLocation(placeholder, source, nameToken);
          module.TypeDefs[aliasName] = placeholder;
        }
        if (!isExported && !isStdlib)
          module.NonExportedTypeNames.Add(aliasName);
        if (source.Path != null) module.TypeDefSourceFiles[aliasName] = source.Path;
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

    var path = exeDir;
    while (path != null) {
      var stdlibPath = Path.Combine(path, "stdlib");
      Logger.Debug(LogCategory.Compiler, $"StdlibLoader: checking {stdlibPath}");
      if (Directory.Exists(stdlibPath)) {
        Logger.Debug(LogCategory.Compiler, $"StdlibLoader: found stdlib at {stdlibPath}");
        return stdlibPath;
      }
      path = Path.GetDirectoryName(path);
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
