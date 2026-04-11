using MaxonSharp.Compiler.Ir.Core;
using MaxonSharp.Compiler.Ir.Dialects;
using MaxonSharp.Compiler.Ir.Passes;

namespace MaxonSharp.Compiler;

public record CompileTarget(string Arch, string Os) {
  public static CompileTarget Default => Native;

  public static CompileTarget Native {
    get {
      var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
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
  /// Parses a target triple string like "arm64-macos" into a CompileTarget.
  /// </summary>
  public static CompileTarget Parse(string triple) {
    var parts = triple.Split('-', 2);
    if (parts.Length != 2)
      throw new ArgumentException($"Invalid target format '{triple}'. Expected 'arch-os' (e.g., 'arm64-macos').");
    return new CompileTarget(parts[0], parts[1]);
  }
}

public record CompileResult(
  bool Success,
  List<CompileError> Errors,
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
  private readonly IrContext _context = new();

  [ThreadStatic] private static bool _mmTrace;
  public static bool MmTrace { get => _mmTrace; set => _mmTrace = value; }

  [ThreadStatic] private static bool _mmDebug;
  public static bool MmDebug { get => _mmDebug; set => _mmDebug = value; }

  [ThreadStatic] private static bool _asyncTrace;
  public static bool AsyncTrace { get => _asyncTrace; set => _asyncTrace = value; }

  [ThreadStatic] private static bool _debugStream;
  public static bool DebugStream { get => _debugStream; set => _debugStream = value; }

  [ThreadStatic] private static bool _testing;
  public static bool Testing { get => _testing; set => _testing = value; }

  public CompileResult Compile(SourceFile[] sources, string outputPath, string? irOutputPath = null, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null, string entryFunction = "main") {
    target ??= CompileTarget.Default;
    var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

    using var _ = _context.PushScope();

    try {
      var stageSw = System.Diagnostics.Stopwatch.StartNew();
      Logger.Debug(LogCategory.Compiler, "Starting compilation");

      // Stage 1-2: Lex and parse all source files into IR modules
      // Use cached stdlib module, then parse user code into a clone
      var module = StdlibLoader.GetStdlibModule();
      module.EntryFunctionName = entryFunction;

      // Reset IDs so user code starts at %0
      _context.ResetIds();
      MaxonPanicOp.ResetPanicLabels();
      Parser.ResetClosureCounter();

      var parseErrors = CompileSources(module, sources, false, target);
      var parseMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

      if (parseErrors.Count > 0)
        return new CompileResult(false, parseErrors);

      // Stage 3-4: IR pipeline (semantic checks + dialect lowering)
      var pipeline = new IrPipeline();
      var irResult = IrPipeline.Run(module, returnIr, dumpStagesBasePath, target);
      var pipelineMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

      // Write IR if requested
      if (irOutputPath != null) {
        if (irResult.X86Module != null)
          IrPipeline.WriteIrOutput(irResult.X86Module, irOutputPath);
        else if (irResult.ARM64Module != null)
          IrPipeline.WriteIrOutput(irResult.ARM64Module, irOutputPath);
      }

      if (target.Arch == "arm64") {
        // Stage 5: Code emission (ARM64 dialect -> machine code)
        var codeResult = ARM64CodeEmitterStage.Emit(irResult.ARM64Module!);
        var emitMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

        // Stage 6: Write Mach-O executable
        MachOWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, symdata: codeResult.Symdata, got: codeResult.Got, importNames: codeResult.ImportNames);
        var writeMs = stageSw.ElapsedMilliseconds;
        Logger.Trace(LogCategory.Compiler, $"Stages: parse={parseMs}ms pipeline={pipelineMs}ms emit={emitMs}ms write={writeMs}ms");
        Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata to {outputPath}");
      } else if (target.Arch == "x64") {
        // Stage 5: Code emission (X86 dialect -> machine code)
        var codeResult = X86CodeEmitter.Emit(irResult.X86Module!);
        var emitMs = stageSw.ElapsedMilliseconds; stageSw.Restart();

        // Stage 6: Write PE executable
        PeWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, codeResult.Imports, codeResult.Symdata, codeResult.CoffSymbols);
        var writeMs = stageSw.ElapsedMilliseconds;
        Logger.Trace(LogCategory.Compiler, $"Stages: parse={parseMs}ms pipeline={pipelineMs}ms emit={emitMs}ms write={writeMs}ms");
        Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata, {codeResult.Imports.Count} imports to {outputPath}");
      } else {
        throw new InvalidOperationException($"Unsupported target architecture: {target.Arch}");
      }

      return new CompileResult(true, [], irResult.AllStagesIr);
    } catch (CompileError ex) {
      if (ex.FilePath == null && userSourceFile != null) {
        ex.FilePath = userSourceFile;
      }
      return new CompileResult(false, [ex]);
    } catch (Exception ex) {
      return new CompileResult(false, [new CompileError(ErrorCode.InternalError, $"{ex.Message}\n{ex.StackTrace}")]);
    }
  }

  public static List<CompileError> Check(string filePath, string content) {
    var context = new IrContext();
    using var _ = context.PushScope();

    try {
      var stdlibSources = StdlibLoader.LoadStdlibModules();

      // If checking a stdlib file, replace its content in the stdlib sources
      var normalizedPath = Path.GetFullPath(filePath);
      var stdlibIndex = Array.FindIndex(stdlibSources,
        s => Path.GetFullPath(s.Path) == normalizedPath);

      if (stdlibIndex >= 0) {
        // Stdlib file changed - must re-parse stdlib from scratch
        var module = new IrModule<MaxonOp>();
        var modifiedSources = (SourceFile[])stdlibSources.Clone();
        modifiedSources[stdlibIndex] = new SourceFile(filePath, content);
        return CompileSources(module, modifiedSources, true);
      } else {
        var module = StdlibLoader.GetStdlibModule();
        context.ResetIds();
        MaxonPanicOp.ResetPanicLabels();
        Parser.ResetClosureCounter();
        return CompileSources(module, [new SourceFile(filePath, content)], false);
      }
    } catch (CompileError ex) {
      ex.FilePath ??= filePath;
      return [ex];
    }
  }

  internal static List<CompileError> CompileSources(IrModule<MaxonOp> module, SourceFile[] sources, bool isStdLib, CompileTarget? target = null) {
    target ??= CompileTarget.Default;
    var parserOs = target.ParserOs;
    var parserArch = target.Arch;
    var errors = new List<CompileError>();
    var failedFiles = new HashSet<string>();

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
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }

    // Pre-scan all sources to register function signatures, type details, etc.
    // so that cross-file forward references resolve regardless of parse order
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var lexer = new Lexer(source.Content);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch);
        parser.PreScan(module);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }

    // Typealias type params may reference placeholder types from PreScan (e.g.,
    // `FooArray = Array with FooEnum` prescanned before FooEnum gets its cases).
    // Now that all types are fully defined, update the references.
    try {
      RefreshTypeAliasTypeParams(module);
      ResolveStructRawValueEnumRefs(module);
    } catch (CompileError ex) {
      errors.Add(ex);
    }

    // Full parse with all signatures known
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var lexer = new Lexer(source.Content);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch);
        var parsed = parser.Parse();
        module.Merge(parsed);
        // Collect declaration-level errors from parser recovery
        foreach (var err in parser.Errors) errors.Add(err);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
      }
    }

    return errors;
  }

  private static void RefreshTypeAliasTypeParams(IrModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not IrStructType structType || structType.TypeParams.Count == 0)
        continue;
      foreach (var key in structType.TypeParams.Keys.ToList()) {
        var paramType = structType.TypeParams[key];
        if (paramType is IrTypeParameterType)
          continue;
        if (module.TypeDefs.TryGetValue(paramType.Name, out var currentType) && currentType != paramType)
          structType.TypeParams[key] = currentType;
      }
    }
  }

  /// <summary>
  /// Resolves deferred enum member references in struct-backed enum raw values.
  /// Called after all files are pre-scanned so cross-file enum types are available.
  /// </summary>
  private static void ResolveStructRawValueEnumRefs(IrModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not IrEnumType enumType) continue;
      foreach (var enumCase in enumType.Cases) {
        if (enumCase.RawValue is not StructRawValue srv) continue;
        if (srv.UnresolvedEnumRefs.Count == 0 && srv.UnresolvedConstRefs.Count == 0) continue;

        foreach (var (fieldName, enumTypeName, caseName, line, column) in srv.UnresolvedEnumRefs) {
          if (!module.TypeDefs.TryGetValue(enumTypeName, out var refType) || refType is not IrEnumType refEnum) {
            throw new CompileError(ErrorCode.SemanticUnknownType,
              $"unknown enum type: '{enumTypeName}'", line, column);
          }
          var refCase = refEnum.GetCase(caseName)
            ?? throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
              $"unknown enum case: '{caseName}' in '{enumTypeName}'", line, column);
          srv.Fields.Add((fieldName, refCase.Ordinal));
        }
        srv.UnresolvedEnumRefs.Clear();

        foreach (var (fieldName, constName, line, column) in srv.UnresolvedConstRefs) {
          if (!module.ExportedConstants.TryGetValue(constName, out var constVal)) {
            throw new CompileError(ErrorCode.SemanticUnknownField,
              $"unknown constant: '{constName}'", line, column);
          }
          long value = constVal switch {
            long l => l,
            double d => BitConverter.DoubleToInt64Bits(d),
            bool b => b ? 1 : 0,
            _ => throw new CompileError(ErrorCode.SemanticUnknownField,
              $"constant '{constName}' is not a numeric or boolean value", line, column)
          };
          srv.Fields.Add((fieldName, value));
        }
        srv.UnresolvedConstRefs.Clear();
      }
    }
  }

  private static void PreRegisterTypeNames(IrModule<MaxonOp> module, SourceFile source, bool isStdlib = false) {
    var lexer = new Lexer(source.Content);
    var tokens = lexer.Tokenize();
    int parenDepth = 0;
    for (int i = 0; i < tokens.Count - 1; i++) {
      var t = tokens[i];
      // Track parenthesis nesting so we only recognize type declarations at
      // top level. Without this, a parameter pair like `type StdType` inside
      // a function signature gets misread as a top-level `type StdType`
      // declaration and shadows the real type across files.
      if (t.Type == TokenType.LeftParen) { parenDepth++; continue; }
      if (t.Type == TokenType.RightParen) { parenDepth--; continue; }
      if (parenDepth != 0) continue;

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
        var structType = new IrStructType(name, [], assocNames);
        SetSourceLocation(structType, source, nameToken);
        module.TypeDefs.TryAdd(name, structType);
        if (!isExported && !isStdlib)
          module.NonExportedTypeNames.Add(name);
        if (source.Path != null) module.TypeDefSourceFiles[name] = source.Path;
        i += 1;
      } else if ((t.Type == TokenType.Enum || t.Type == TokenType.Union) && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var typeName = nameToken.Value;
        var namedType = new IrEnumType(typeName, [], null, []) { IsUnion = t.Type == TokenType.Union };
        SetSourceLocation(namedType, source, nameToken);
        module.TypeDefs.TryAdd(typeName, namedType);
        if (!isExported && !isStdlib) module.NonExportedTypeNames.Add(typeName);
        if (source.Path != null) module.TypeDefSourceFiles[typeName] = source.Path;
        i += 1;
      } else if (t.Type == TokenType.Interface && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var ifaceName = nameToken.Value;
        var ifaceType = new IrInterfaceType(ifaceName, []);
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
          var placeholder = new IrStructType(aliasName, []);
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

  private static void SetSourceLocation(IrType type, SourceFile source, Token nameToken) {
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
  private static IrModule<MaxonOp>? _cachedStdlibModule;
  private static readonly object _stdlibLock = new();

  /// Returns a cached parsed stdlib module clone ready for user code compilation.
  /// The clone has all functions marked IsStdlib=true.
  public static IrModule<MaxonOp> GetStdlibModule() {
    if (_cachedStdlibModule != null)
      return _cachedStdlibModule.Clone();

    lock (_stdlibLock) {
      if (_cachedStdlibModule != null)
        return _cachedStdlibModule.Clone();

      var context = new IrContext();
      using var _ = context.PushScope();
      var module = new IrModule<MaxonOp>();
      var sources = LoadStdlibModules();
      var stdlibErrors = Compiler.CompileSources(module, sources, true);
      if (stdlibErrors.Count > 0) throw stdlibErrors[0];
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
