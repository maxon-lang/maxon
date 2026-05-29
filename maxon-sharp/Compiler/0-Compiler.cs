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

  /// <summary>
  /// Resets process-wide compile state that would otherwise drift across
  /// independent compiles. The CLI calls this once per invocation; the LSP
  /// calls it before every recompile. Without these resets closure/panic
  /// labels collide and the IR id counter fails to start at %0.
  ///
  /// Also seeds the stdlib-namespace counters past the cached stdlib's max id
  /// so lowering-time stdlib MaxonValues (e.g. MaxonManagedMemSliceOp.Result)
  /// don't alias parser-time stdlib MaxonValues in per-function valueMaps.
  /// Safe to call before the cached stdlib has been built — seeds with 0 in
  /// that case (the stdlib parse runs in its own context and won't collide).
  /// </summary>
  public static void ResetStaticCompileState(IrContext context) {
    context.ResetIds();
    context.SeedStdlibCounters(StdlibLoader.CachedStdlibMaxValueId, StdlibLoader.CachedStdlibMaxStdValueId);
    MaxonPanicOp.ResetPanicLabels();
    Parser.ResetClosureCounter();
  }

  public CompileResult Compile(SourceFile[] sources, string outputPath, string? irOutputPath = null, bool returnIr = false, string? dumpStagesBasePath = null, CompileTarget? target = null, string entryFunction = "main") {
    target ??= CompileTarget.Default;
    var userSourceFile = sources.Length == 1 ? sources[0].Path : null;

    using var _ = _context.PushScope();

    try {
      var totalSw = System.Diagnostics.Stopwatch.StartNew();
      var stageSw = StageTimer.Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;
      Logger.Debug(LogCategory.Compiler, "Starting compilation");

      // Stage 1-2: Lex and parse all source files into IR modules
      // Use cached stdlib module, then parse user code into a clone
      var module = StdlibLoader.GetStdlibModule();
      module.EntryFunctionName = entryFunction;

      ResetStaticCompileState(_context);

      Dictionary<string, long>? parseTimings = StageTimer.Enabled ? [] : null;
      var parseErrors = CompileSources(module, sources, false, target, parseTimings);
      long parseMs = 0;
      if (stageSw != null) { parseMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }
      if (parseTimings != null)
        Console.Error.WriteLine("Parse:" + StageTimer.Format(parseTimings) + $" tokens={StageTimer.TokensLexed}");

      if (parseErrors.Count > 0)
        return new CompileResult(false, parseErrors);

      // Stage 3-4: IR pipeline (semantic checks + dialect lowering)
      var pipeline = new IrPipeline();
      var irResult = IrPipeline.Run(module, returnIr, dumpStagesBasePath, target);
      long pipelineMs = 0;
      if (stageSw != null) { pipelineMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }

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
        long emitMs = 0;
        if (stageSw != null) { emitMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }

        // Stage 6: Write Mach-O executable
        MachOWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, symdata: codeResult.Symdata, got: codeResult.Got, importNames: codeResult.ImportNames);
        if (stageSw != null)
          Console.Error.WriteLine($"Stages: parse={parseMs}ms pipeline={pipelineMs}ms emit={emitMs}ms write={stageSw.ElapsedMilliseconds}ms");
        Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata to {outputPath} in {totalSw.ElapsedMilliseconds}ms");
      } else if (target.Arch == "x64") {
        // Stage 5: Code emission (X86 dialect -> machine code)
        var codeResult = X86CodeEmitter.Emit(irResult.X86Module!);
        long emitMs = 0;
        if (stageSw != null) { emitMs = stageSw.ElapsedMilliseconds; stageSw.Restart(); }

        // Stage 6: Write PE executable
        PeWriter.Write(outputPath, codeResult.Code, codeResult.Rdata, codeResult.Data, codeResult.Ucddata, codeResult.Imports, codeResult.Symdata, codeResult.CoffSymbols);
        if (stageSw != null)
          Console.Error.WriteLine($"Stages: parse={parseMs}ms pipeline={pipelineMs}ms emit={emitMs}ms write={stageSw.ElapsedMilliseconds}ms");
        Logger.Info(LogCategory.Compiler, $"Wrote {codeResult.Code.Length} bytes code, {codeResult.Rdata.Length} bytes rdata, {codeResult.Data.Length} bytes data, {codeResult.Ucddata.Length} bytes ucddata, {codeResult.Symdata.Length} bytes symdata, {codeResult.Imports.Count} imports to {outputPath} in {totalSw.ElapsedMilliseconds}ms");
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
        // Stdlib file changed - must re-parse stdlib from scratch.
        // Preserve the original SourceFile's RootPath so the replaced entry
        // retains the stdlib anchor (parent of stdlib dir).
        var module = new IrModule<MaxonOp>();
        var modifiedSources = (SourceFile[])stdlibSources.Clone();
        modifiedSources[stdlibIndex] = new SourceFile(filePath, content, modifiedSources[stdlibIndex].RootPath);
        return CompileSources(module, modifiedSources, true);
      } else {
        var module = StdlibLoader.GetStdlibModule();
        ResetStaticCompileState(context);
        // Single-file Check: anchor at the file's parent dir (decision #3).
        var rootPath = Path.GetDirectoryName(Path.GetFullPath(filePath));
        return CompileSources(module, [new SourceFile(filePath, content, rootPath)], false);
      }
    } catch (CompileError ex) {
      ex.FilePath ??= filePath;
      return [ex];
    }
  }

  /// <summary>
  /// Run lightweight analysis passes (parameter mutation + borrow check) on
  /// an already-parsed module. Returns any <see cref="CompileError"/>s found.
  /// Used by the LSP to surface E3070 and similar errors that the parse phase
  /// alone cannot detect, without paying the cost of the full IR pipeline.
  /// </summary>
  internal static List<CompileError> RunAnalysisPasses(IrModule<MaxonOp> module) {
    try {
      ParameterMutationAnalysisPass.Run(module);
      BorrowCheckPass.Run(module);
      return [];
    } catch (CompileError ex) {
      return [ex];
    } catch {
      return [];
    }
  }

  internal static List<CompileError> CompileSources(IrModule<MaxonOp> module, SourceFile[] sources, bool isStdLib, CompileTarget? target = null, Dictionary<string, long>? timings = null) {
    target ??= CompileTarget.Default;
    var parserOs = target.ParserOs;
    var parserArch = target.Arch;
    var errors = new List<CompileError>();
    var failedFiles = new HashSet<string>();
    var sw = timings != null ? new System.Diagnostics.Stopwatch() : null;

    // Per-source token cache. The same file is walked by up to 5 passes
    // (PreRegisterTypeNames, PreScanTypeAliasesOnly, PreScan, RescanExtensions,
    // PreScanTypeAliasesOnly again, Parse). Each pass previously re-lexed from
    // scratch; caching cuts that to one lex per file. Parsers mutate tokens
    // only during full parse (Self-type rewrite, primitive-static method
    // rewrite), which is the last pass, so the shared list is safe across
    // pre-scans. ReportLexerErrors is idempotent on an already-sanitised list.
    var tokensBySource = new Dictionary<string, List<Token>>(sources.Length);

    // When timing is on, route every lex through this Stopwatch so the "lex"
    // bucket isolates tokenization cost from whichever pre-scan happens to
    // trigger the cache miss first.
    var lexSw = timings != null ? new System.Diagnostics.Stopwatch() : null;

    List<Token> TokensFor(SourceFile source) {
      if (!tokensBySource.TryGetValue(source.Path, out var cached)) {
        if (lexSw != null) {
          lexSw.Restart();
          cached = new Lexer(source.Content).Tokenize();
          StageTimer.Record(timings!, "lex", lexSw.ElapsedMilliseconds);
          StageTimer.TokensLexed += cached.Count;
        } else {
          cached = new Lexer(source.Content).Tokenize();
        }
        tokensBySource[source.Path] = cached;
      }
      return cached;
    }

    // When timing, pre-warm the token cache so the "lex" bucket isolates
    // tokenization cost. Without this, lex time falls into whichever pre-scan
    // happens to trigger the cache miss first (typically preRegTypes).
    if (lexSw != null) {
      foreach (var source in sources) TokensFor(source);
    }

    // Pre-register type names from all sources so cross-file references resolve
    // (e.g., Character.maxon references String before String.maxon is parsed)
    sw?.Restart();
    foreach (var source in sources)
      PreRegisterTypeNames(module, source, TokensFor(source), isStdLib);
    if (sw != null) StageTimer.Record(timings!, "preRegTypes", sw.ElapsedMilliseconds);

    // Pre-scan top-level typealiases from all sources so cross-file typealias
    // references resolve regardless of file processing order
    sw?.Restart();
    foreach (var source in sources) {
      try {
        var tokens = TokensFor(source);
        ReportLexerErrors(tokens, source.Path, errors);
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch, rootPath: source.RootPath);
        parser.PreScanTypeAliasesOnly(module);
        // PreScanTypeAliasesOnly recovers from per-block errors (e.g. duplicate
        // enum raw value) so the rest of the file's typealiases still register.
        // Surface the recovered errors and mark the file as failed so later
        // passes don't run on a partially-parsed module.
        if (parser.Errors.Count > 0) {
          foreach (var err in parser.Errors) errors.Add(err);
          failedFiles.Add(source.Path);
        }
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "preScanAliases", sw.ElapsedMilliseconds);

    // Pre-scan all sources to register function signatures, type details, etc.
    // so that cross-file forward references resolve regardless of parse order
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch, rootPath: source.RootPath);
        parser.PreScan(module);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "preScan", sw.ElapsedMilliseconds);

    // Re-scan extension blocks for files that had unresolved interface extensions
    // due to file ordering (conforming types in files not yet pre-scanned).
    // Only for non-stdlib: stdlib files are all in one CompileSources call so
    // ordering issues within stdlib are handled by the pre-scan.
    if (!isStdLib && module.DeferredExtensionFiles.Count > 0) {
      sw?.Restart();
      foreach (var source in sources) {
        if (failedFiles.Contains(source.Path)) continue;
        if (!module.DeferredExtensionFiles.Contains(source.Path)) continue;
        try {
          var tokens = TokensFor(source);
          var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch, rootPath: source.RootPath);
          parser.RescanExtensionBlocks(module);
        } catch (CompileError ex) {
          ex.FilePath ??= source.Path;
          errors.Add(ex);
          failedFiles.Add(source.Path);
        }
      }
      module.DeferredExtensionFiles.Clear();
      if (sw != null) StageTimer.Record(timings!, "rescanExt", sw.ElapsedMilliseconds);
    }

    // Any pre-scan failure leaves the module in a partially-registered state.
    // Continuing into the full parse would produce cascading false errors (e.g.
    // "Undefined function" for methods that do exist but were never registered).
    // Return early so only the real pre-scan errors are reported.
    if (errors.Count > 0) {
      return errors;
    }

    // Typealias type params may reference placeholder types from PreScan (e.g.,
    // `FooArray = Array with FooEnum` prescanned before FooEnum gets its cases).
    // Now that all types are fully defined, update the references.
    sw?.Restart();
    try {
      RefreshTypeAliasTypeParams(module);
      ResolveStructRawValueEnumRefs(module);
    } catch (CompileError ex) {
      errors.Add(ex);
    }
    if (sw != null) StageTimer.Record(timings!, "refreshAliases", sw.ElapsedMilliseconds);

    if (errors.Count > 0) {
      errors.Add(HaltedError(errors, "type resolution errors prevent full parse"));
      return errors;
    }

    // Re-scan typealiases now that all source struct bodies are fully parsed.
    // The first typealias pre-scan runs before PreScan, so an alias like
    // `MirModule = IrModule with MirOp` specializes against a source struct that
    // still has no fields, freezing the alias with empty fields and unresolved
    // inner aliases. Re-running PreScanTypeAliasesOnly against the now-populated
    // source struct lets RegisterConcreteTypeAlias produce correct fields and
    // per-instance inner aliases (e.g., Array_MirOp for the `ops` field).
    // Without this, compilation is file-order-dependent — passes when the source
    // type's file is pre-scanned before the alias file, fails otherwise.
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch, rootPath: source.RootPath);
        parser.PreScanTypeAliasesOnly(module, rescan: true);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
        failedFiles.Add(source.Path);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "rescanAliases", sw.ElapsedMilliseconds);

    // Function-backed enums need a final resolution AFTER the typealias rescan,
    // because the rescan re-runs PreScanEnum for every file and re-creates each
    // IrEnumType with the placeholder IrFunctionBackingType (signature filled in
    // later). Resolving here uses the now-complete function registry and the
    // post-rescan IrEnumType instances that subsequent passes will read.
    try {
      ResolveFunctionBackedEnumRefs(module);
    } catch (CompileError ex) {
      errors.Add(ex);
    }

    if (errors.Count > 0) {
      errors.Add(HaltedError(errors, "typealias re-scan errors prevent full parse"));
      return errors;
    }

    // Full parse with all signatures known
    sw?.Restart();
    foreach (var source in sources) {
      if (failedFiles.Contains(source.Path)) continue;
      try {
        var tokens = TokensFor(source);
        var parser = new Parser(tokens, module, isStdlib: isStdLib, sourceFilePath: source.Path, testing: Testing, targetOs: parserOs, targetArch: parserArch, rootPath: source.RootPath);
        var parsed = parser.Parse();
        module.Merge(parsed);
        // Collect declaration-level errors from parser recovery
        foreach (var err in parser.Errors) errors.Add(err);
      } catch (CompileError ex) {
        ex.FilePath ??= source.Path;
        errors.Add(ex);
      }
    }
    if (sw != null) StageTimer.Record(timings!, "fullParse", sw.ElapsedMilliseconds);

    return errors;
  }

  /// <summary>
  /// Replaces Error tokens with harmless StringLiteral tokens so parsing can continue.
  /// Builds a "compilation halted" error that points to the same file and line as
  /// the first error in <paramref name="errors"/>, so the user can see exactly where
  /// the phase failed without hunting through cascading false positives.
  /// </summary>
  private static CompileError HaltedError(List<CompileError> errors, string reason) {
    var first = errors[0];
    return new CompileError(ErrorCode.InternalError, $"compilation halted due to errors above: {reason}", first.Line, first.Column) {
      FilePath = first.FilePath
    };
  }

  // Lexer error tokens encode their kind via a sentinel prefix on the token's
  // Value string. The lexer never sees ErrorCode directly (it lives in a
  // separate file), so we round-trip the kind through the token text. The
  // reporter strips the prefix and emits the matching CompileError.
  private static readonly (string Prefix, ErrorCode Code)[] LexerErrorPrefixes = [
    ("__unterminated_string__:", ErrorCode.LexerUnterminatedString),
    ("__unterminated_block_comment__:", ErrorCode.LexerUnterminatedBlockComment),
  ];

  /// When reportErrors is true, also adds CompileErrors to the error list.
  /// </summary>
  private static void ReportLexerErrors(List<Token> tokens, string filePath, List<CompileError>? errors) {
    for (int i = 0; i < tokens.Count; i++) {
      if (tokens[i].Type == TokenType.Error) {
        var tok = tokens[i];
        var (code, message) = ClassifyLexerError(tok.Value);
        errors?.Add(new CompileError(code, message, tok.Line, tok.Column) { FilePath = filePath });
        tokens[i] = new Token(TokenType.StringLiteral, "", tok.Line, tok.Column);
      }
    }
  }

  private static (ErrorCode Code, string Message) ClassifyLexerError(string tokenValue) {
    foreach (var (prefix, code) in LexerErrorPrefixes) {
      if (tokenValue.StartsWith(prefix)) {
        return (code, tokenValue[prefix.Length..]);
      }
    }
    return (ErrorCode.LexerUnescapedBrace, tokenValue);
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
  /// Resolves deferred function references in function-backed enum raw values.
  /// PreScanEnum records each case's RawValue as the raw identifier the user
  /// wrote (e.g. "doubleFn") with a placeholder IrFunctionBackingType. After
  /// every file's top-level functions have been pre-scanned, this pass looks
  /// each identifier up against the module's function registry, validates that
  /// all cases share a single signature, and rewrites each case's RawValue to
  /// the fully qualified function name so the lowering pass can emit the
  /// correct StdFuncRefOp for the select chain.
  /// </summary>
  private static void ResolveFunctionBackedEnumRefs(IrModule<MaxonOp> module) {
    foreach (var (_, type) in module.TypeDefs) {
      if (type is not IrEnumType enumType) continue;
      if (enumType.BackingType is not IrFunctionBackingType placeholder) continue;
      // Empty signature is the placeholder marker; a non-empty one means the
      // backing has already been resolved (idempotent across repeated calls).
      if (placeholder.Signature.ParameterTypes.Count > 0 || placeholder.Signature.ReturnType != null)
        continue;

      IrFunctionType? sharedSignature = null;
      string? firstCaseName = null;
      foreach (var enumCase in enumType.Cases) {
        if (enumCase.RawValue is not string ident) continue;
        var line = enumCase.SourceLine ?? 0;
        var col = enumCase.SourceColumn ?? 0;

        var fn = ResolveFunctionByIdent(module, ident)
          ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"function '{ident}' (referenced by enum case '{enumType.Name}.{enumCase.Name}') is not declared",
            line, col);
        var sig = new IrFunctionType([.. fn.ParamTypes], fn.ReturnType);
        if (sharedSignature == null) {
          sharedSignature = sig;
          firstCaseName = enumCase.Name;
        } else if (!Parser.FunctionSignaturesEqual(sharedSignature, sig)) {
          throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
            $"raw value type mismatch: function '{ident}' has signature '{sig.Name}', "
              + $"but case '{enumType.Name}.{firstCaseName}' established signature '{sharedSignature.Name}'",
            line, col);
        }
        // Rewrite the case's RawValue to the fully qualified function name so
        // the codegen pass emits the correct StdFuncRefOp label.
        enumCase.RawValue = fn.Name;
      }

      if (sharedSignature != null) {
        enumType.BackingType = new IrFunctionBackingType(sharedSignature);
      }
    }
  }

  /// Mirrors the bare-name function-reference resolution at expression sites:
  /// exact, then short-name suffix match. Used by the function-backed enum
  /// resolver so the identifier the user wrote (typically unqualified) maps to
  /// whichever function was pre-scanned for it across the module.
  private static IrFunction<MaxonOp>? ResolveFunctionByIdent(IrModule<MaxonOp> module, string ident) {
    var fn = module.FindFunctionByExactName(ident);
    if (fn != null) return fn;
    if (ident.IndexOf('.') < 0) {
      var suffixDot = "." + ident;
      var matches = module.FindFunctionsByShortName(ident)
        .Where(f => f.Name.EndsWith(suffixDot)).ToList();
      if (matches.Count == 1) return matches[0];
    }
    return null;
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

  private static void PreRegisterTypeNames(IrModule<MaxonOp> module, SourceFile source, List<Token> tokens, bool isStdlib = false) {
    int parenDepth = 0;
    // Visibility of the `extension` block currently being scanned (if any), so
    // that a typealias declared directly inside `module extension X` / `export
    // extension X` inherits that block's cross-file visibility. Without this an
    // inner alias would be recorded as file-scoped and a consumer file that
    // parses before the extension's full PreScan would reject it as an unknown
    // cross-file type. -1 means "not inside an extension block".
    int extensionBlockEndDepth = -1;
    bool extensionIsExported = false;
    bool extensionIsModuleVisible = false;
    int blockDepth = 0;
    bool prevWasNewline = true; // first scanned token sits at statement start
    var prevTokenType = TokenType.Newline;
    for (int i = 0; i < tokens.Count - 1; i++) {
      var t = tokens[i];

      // Block-structure tracking (mirrors ProcessExtensionBlock's scanner) so we
      // know when the current `extension` block closes. Done before the
      // paren-depth gate because openers/`end` are statement-level tokens.
      {
        var next = i + 1 < tokens.Count ? tokens[i + 1].Type : TokenType.Eof;
        // `extension` is intentionally excluded here: its opener may be preceded
        // by a `module`/`export` modifier that the loop body consumes (advancing
        // past the `extension` token), so the increment is done in the extension
        // branch below where the token is seen regardless of any modifier.
        if (t.Type is TokenType.Function or TokenType.If or TokenType.While
            or TokenType.For or TokenType.Match or TokenType.Type
            or TokenType.Enum or TokenType.Union or TokenType.Interface) {
          bool opensBlock = true;
          // `function(` (no name) is a function-type / lambda literal, not a block.
          if (t.Type == TokenType.Function && next == TokenType.LeftParen) opensBlock = false;
          // Match case labels reuse these keywords (`if ... then`, `for ... to`).
          if (next is TokenType.Then or TokenType.Gives or TokenType.To or TokenType.Upto) opensBlock = false;
          // Postfix ternary `if` is mid-expression; block `if` only appears at
          // statement start (after a newline) or directly after `else`.
          if (t.Type == TokenType.If && !prevWasNewline && prevTokenType != TokenType.Else) opensBlock = false;
          if (opensBlock) blockDepth++;
        } else if (t.Type == TokenType.Else) {
          if (next is TokenType.Then or TokenType.Gives or TokenType.To or TokenType.Upto) {
            // Match case label — not a block opener.
          } else if (next == TokenType.If) {
            // `else if` — the upcoming `if` will bump depth.
          } else if (prevTokenType == TokenType.CharacterLiteral) {
            blockDepth++;
          }
        } else if ((t.Type == TokenType.Otherwise && next is TokenType.CharacterLiteral or TokenType.LeftParen)
                   || (t.Type == TokenType.Try && next == TokenType.CharacterLiteral)) {
          blockDepth++;
        } else if (t.Type == TokenType.End) {
          blockDepth--;
          if (extensionBlockEndDepth >= 0 && blockDepth < extensionBlockEndDepth) {
            extensionBlockEndDepth = -1;
            extensionIsExported = false;
            extensionIsModuleVisible = false;
          }
        }
        prevWasNewline = t.Type == TokenType.Newline;
        prevTokenType = t.Type;
      }

      // Track parenthesis nesting so we only recognize type declarations at
      // top level. Without this, a parameter pair like `type StdType` inside
      // a function signature gets misread as a top-level `type StdType`
      // declaration and shadows the real type across files.
      if (t.Type == TokenType.LeftParen) { parenDepth++; continue; }
      if (t.Type == TokenType.RightParen) { parenDepth--; continue; }
      if (parenDepth != 0) continue;

      bool isExported = false;
      bool isModuleVisible = false;
      if (t.Type == TokenType.Export && i + 1 < tokens.Count) {
        isExported = true;
        i++;
        t = tokens[i];
      } else if (t.Type == TokenType.Identifier && t.Value == "module" && i + 1 < tokens.Count
          && IsModuleModifierFollowedByDecl(tokens[i + 1].Type)) {
        // `module` is a contextual keyword. Recognize it here when followed by
        // a declaration token so module-scoped types are tracked correctly.
        isModuleVisible = true;
        i++;
        t = tokens[i];
      }

      // Entering an `extension` block: count the opener here (block-tracking
      // skips it) and remember its visibility for inner aliases. The matching
      // `end` brings blockDepth back below this level, clearing the context.
      if (t.Type == TokenType.Extension) {
        blockDepth++;
        extensionBlockEndDepth = blockDepth;
        extensionIsExported = isExported;
        extensionIsModuleVisible = isModuleVisible;
      }

      // An inner typealias of a `module`/`export` extension inherits the block's
      // visibility so it propagates across files (matching the parser's
      // PreScanExtensionBlock behavior, but recorded early enough that a
      // consumer file parsing first can still see it).
      if (extensionBlockEndDepth >= 0 && t.Type == TokenType.TypeAlias) {
        isExported = isExported || extensionIsExported;
        isModuleVisible = isModuleVisible || extensionIsModuleVisible;
      }

      if (t.Type == TokenType.Type && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var name = nameToken.Value;
        var assocNames = ParseUsesClauseTokens(tokens, i + 2);
        var structType = new IrStructType(name, [], assocNames);
        SetSourceLocation(structType, source, nameToken);
        module.TypeDefs.TryAdd(name, structType);
        if (!isExported && !isModuleVisible && !isStdlib)
          module.NonExportedTypeNames.Add(name);
        if (isModuleVisible) module.ModuleVisibleTypeNames.Add(name);
        if (source.Path != null) module.TypeDefSourceFiles[name] = source.Path;
        i += 1;
      } else if ((t.Type == TokenType.Enum || t.Type == TokenType.Union) && i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier) {
        var nameToken = tokens[i + 1];
        var typeName = nameToken.Value;
        var namedType = new IrEnumType(typeName, [], null, []) { IsUnion = t.Type == TokenType.Union };
        SetSourceLocation(namedType, source, nameToken);
        module.TypeDefs.TryAdd(typeName, namedType);
        if (!isExported && !isModuleVisible && !isStdlib) module.NonExportedTypeNames.Add(typeName);
        if (isModuleVisible) module.ModuleVisibleTypeNames.Add(typeName);
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
        if (!isExported && !isModuleVisible && !isStdlib)
          module.NonExportedTypeNames.Add(aliasName);
        if (isModuleVisible) module.ModuleVisibleTypeNames.Add(aliasName);
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

  // `module` is a contextual keyword (see Parser.CheckModuleKeyword). At the
  // pre-register pass we recognise it only when followed by a token that starts
  // a declaration; otherwise we leave the identifier alone so user code can
  // still use `module` as a parameter or local variable name.
  private static bool IsModuleModifierFollowedByDecl(TokenType nextType) =>
    nextType is TokenType.Type or TokenType.Enum or TokenType.Union
              or TokenType.Interface or TokenType.TypeAlias
              or TokenType.Function or TokenType.Var or TokenType.Let
              or TokenType.Static or TokenType.Extension;

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
  private static int _cachedStdlibMaxValueId;
  private static int _cachedStdlibMaxStdValueId;
  private static readonly object _stdlibLock = new();

  /// Highest stdlib MaxonValue id (low-bits, without StdlibIdBit) minted during the
  /// cached stdlib parse. User compiles must seed their IrContext past this so
  /// lowering-time stdlib MaxonValues don't alias parser-time ones in valueMap.
  public static int CachedStdlibMaxValueId => _cachedStdlibMaxValueId;
  public static int CachedStdlibMaxStdValueId => _cachedStdlibMaxStdValueId;

  /// Returns a cached parsed stdlib module clone ready for user code compilation.
  /// The clone has all functions marked IsStdlib=true.
  public static IrModule<MaxonOp> GetStdlibModule() {
    if (_cachedStdlibModule != null)
      return _cachedStdlibModule.Clone();

    lock (_stdlibLock) {
      if (_cachedStdlibModule != null)
        return _cachedStdlibModule.Clone();

      var context = new IrContext(isStdlibContext: true);
      using var _ = context.PushScope();
      var module = new IrModule<MaxonOp>();
      var sources = LoadStdlibModules();
      var stdlibErrors = Compiler.CompileSources(module, sources, true);
      if (stdlibErrors.Count > 0) throw stdlibErrors[0];
      foreach (var func in module.Functions) {
        func.IsStdlib = true;
        func.IsExported = true;
        // Stdlib symbols are globally visible; collapse any module-scoped flag.
        func.IsModuleVisible = false;
      }
      // Snapshot the stdlib counters so user compiles can seed their stdlib-namespace
      // counters past these and avoid id collisions during stdlib function lowering.
      _cachedStdlibMaxValueId = context.NextStdlibValueId - 1;
      _cachedStdlibMaxStdValueId = context.NextStdlibStdValueId - 1;
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

    var allFiles = Directory.GetFiles(stdlibPath, "*.maxon", SearchOption.AllDirectories);

    // stdlib/Internals.maxon is a self-hosted-only file: it uses the
    // `__Internals.*` intrinsic namespace (file-path-gated raw Std-op
    // access) which the C# bootstrap doesn't implement. The bootstrap
    // never needs to compile it because the self-hosted compiler builds
    // its own stdlib cache from a curated whitelist (StdlibLoader.maxon)
    // where Internals.maxon contributes the migrated runtime helpers.
    var files = allFiles.Where(f => Path.GetFileName(f) != "Internals.maxon").ToArray();

    // Sort: Interfaces.maxon first (foundational shared protocols), then
    // helper files (in subdirectories), then remaining top-level files,
    // alphabetically within each group.
    Array.Sort(files, (a, b) => {
      var aIsInterfaces = Path.GetFileName(a) == "Interfaces.maxon" && Path.GetDirectoryName(a) == stdlibPath;
      var bIsInterfaces = Path.GetFileName(b) == "Interfaces.maxon" && Path.GetDirectoryName(b) == stdlibPath;
      if (aIsInterfaces != bIsInterfaces) return aIsInterfaces ? -1 : 1;
      var aIsHelper = Path.GetDirectoryName(a) != stdlibPath;
      var bIsHelper = Path.GetDirectoryName(b) != stdlibPath;
      if (aIsHelper != bIsHelper) return aIsHelper ? -1 : 1;
      return string.Compare(a, b, StringComparison.Ordinal);
    });

    // Per Phase 1 of the directory-as-module redesign, stdlib files anchor
    // at the parent of the stdlib directory so that rel(file, root) yields
    // "stdlib/<subdirs>/<name>.maxon" and namespace = "stdlib.<subdirs>".
    var stdlibRoot = Path.GetDirectoryName(stdlibPath);
    var sources = new List<SourceFile>();
    foreach (var filePath in files)
      sources.Add(new SourceFile(filePath, File.ReadAllText(filePath), stdlibRoot));
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

public record SourceFile(string Path, string Content, string? RootPath = null);
