using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public class Parser(List<Token> tokens, MlirModule<MaxonOp>? seedModule = null, bool isStdlib = false, string? sourceFilePath = null) {
  private readonly List<Token> _tokens = tokens;
  private readonly bool _isStdlib = isStdlib;
  private readonly string? _sourceFilePath = sourceFilePath;
  private int _pos;

  private MlirModule<MaxonOp>? _currentModule;
  private MlirFunction<MaxonOp>? _currentFunction;
  private MlirBlock<MaxonOp>? _currentBlock;
  private readonly Dictionary<string, VarInfo> _variables = [];
  private readonly HashSet<string> _referencedVars = [];
  private int _blockCounter;
  private int _closureCounter;
  private readonly Stack<LoopContext> _loopStack = new();
  private bool _inTryContext;
  private readonly Dictionary<string, MlirType> _typeRegistry = seedModule != null
    ? new(seedModule.TypeDefs) : [];
  private string? _currentTypeName;

  // Top-level compile-time constants (name -> evaluated value: long, double, or bool)
  private Dictionary<string, object> _topLevelConstants = [];

  // Top-level array let declarations deferred to main function body
  private record EnumConstantValue(string EnumTypeName, string CaseName, int Ordinal);
  private record DeferredArrayLet(string Name, int TokenStart, int TokenEnd, int Line, int Column);
  private readonly List<DeferredArrayLet> _deferredArrayLets = [];

  // Global mutable variables (name -> type info)
  private record GlobalVarInfo(MaxonValueKind Kind, bool Mutable);
  private readonly Dictionary<string, GlobalVarInfo> _globalVars = [];

  // Default parameter values (funcName -> index -> value)
  private readonly Dictionary<string, Dictionary<int, MlirAttribute>> _functionDefaults = [];

  // Type alias source tracking (aliasName -> sourceTypeName)
  private readonly Dictionary<string, string> _typeAliasSources = [];

  // Interface associated type names (interfaceName -> list of associated type names from 'uses' clause)
  private readonly Dictionary<string, List<string>> _interfaceAssociatedTypes = [];

  /// <summary>
  /// Creates a unique mangled name for an overloaded function by appending
  /// distinguishing parameter names. For instance methods, 'self' is excluded.
  /// The first non-self parameter name is also excluded (since it's positional and
  /// the same across overloads). Only subsequent named params form the suffix.
  /// Example: slice(self, start, endIndex) -> baseName$endIndex
  /// </summary>
  private static string MangleOverloadName(string baseName, List<string> paramNames) {
    var nonSelf = paramNames.Where(n => n != "self").ToList();
    if (nonSelf.Count <= 1) return baseName; // 0 or 1 param, no disambiguation possible
    var distinguishing = nonSelf.Skip(1); // skip first positional param
    return $"{baseName}${string.Join("_", distinguishing)}";
  }

  /// <summary>
  /// Extracts the base function name from a potentially mangled overload name.
  /// "stdlib.String.slice$endIndex" -> "stdlib.String.slice"
  /// </summary>
  private static string UnmangleName(string mangledName) {
    var dollarIdx = mangledName.IndexOf('$');
    return dollarIdx >= 0 ? mangledName[..dollarIdx] : mangledName;
  }

  /// <summary>
  /// Detects overloads for a function being registered and returns the mangled registration name.
  /// If existing functions with the same base name exist, retroactively mangles them
  /// and returns a mangled name for the new function. Updates _functionDefaults
  /// dictionary when renaming existing functions.
  /// </summary>
  private string ResolveOverloadRegistrationName(MlirModule<MaxonOp> module, string baseName, List<string> paramNames) {
    var existingOverloads = module.Functions.Where(f => f.Name == baseName || UnmangleName(f.Name) == baseName).ToList();

    if (existingOverloads.Count == 0) {
      return baseName;
    }

    // If the only existing match has the exact same param names, it's the same function
    // (e.g., re-registration during full parse after pre-scan)
    if (existingOverloads.Count == 1 && existingOverloads[0].Name == baseName
        && existingOverloads[0].ParamNames.SequenceEqual(paramNames)) {
      return baseName;
    }

    var registrationName = MangleOverloadName(baseName, paramNames);

    // Retroactively mangle any existing function that still has the unmangled name
    foreach (var existing in existingOverloads) {
      if (!existing.Name.Contains('$')) {
        var mangledExisting = MangleOverloadName(baseName, existing.ParamNames);
        if (mangledExisting == registrationName) {
          // Same function re-registered (e.g., from pre-scan)
          if (existing.ParamNames.SequenceEqual(paramNames))
            return registrationName;
          throw new InvalidOperationException(
            $"Overload name collision: '{baseName}' has two overloads that mangle to the same name '{registrationName}'. " +
            $"Overloads must differ in their named parameter names (not just types).");
        }
        var oldName = existing.Name;
        existing.Name = mangledExisting;
        if (_functionDefaults.Remove(oldName, out var existingDefaults)) {
          _functionDefaults[mangledExisting] = existingDefaults;
        }
      }
    }

    return registrationName;
  }

  /// <summary>
  /// Resolves a qualified method name, falling back to the source type for type aliases.
  /// E.g., "ByteArray.push" → looks for "ByteArray.push" first, then "Array.push".
  /// Also supports suffix matching for namespace-qualified names.
  /// Returns the resolved function name if found, null otherwise.
  /// </summary>
  private string? ResolveMethodName(string qualifiedName) {
    // Check for exact match
    if (_currentModule!.Functions.Any(f => f.Name == qualifiedName))
      return qualifiedName;

    // Check for mangled overload variants (exact prefix with '$')
    var overloadMatches = _currentModule!.Functions.Where(f => f.Name.StartsWith(qualifiedName + "$")).ToList();
    if (overloadMatches.Count > 0)
      return qualifiedName; // return base name; overload resolution happens downstream

    // Try suffix match (for namespace-qualified names)
    var suffixPattern = $".{qualifiedName}";
    var suffixDollar = $".{qualifiedName}$";
    var suffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith(suffixPattern) || f.Name.Contains(suffixDollar)).ToList();
    if (suffixMatches.Count == 1)
      return suffixMatches[0].Name;
    if (suffixMatches.Count > 1) {
      // Multiple matches - check if they are all overloads of the same base name
      var baseNames = suffixMatches.Select(f => UnmangleName(f.Name)).Distinct().ToList();
      if (baseNames.Count == 1)
        return baseNames[0]; // all overloads of one function; return base name for downstream resolution
      // Truly ambiguous - caller will need to handle this
      return null;
    }

    // Try alias fallback: ByteArray.push → Array.push
    var dotIdx = qualifiedName.IndexOf('.');
    if (dotIdx > 0) {
      var typePart = qualifiedName[..dotIdx];
      var methodPart = qualifiedName[(dotIdx + 1)..];
      if (_typeAliasSources.TryGetValue(typePart, out var sourceType)) {
        Logger.Debug(LogCategory.Parser, $"  Type alias: {typePart} -> {sourceType}");
        var aliasedName = $"{sourceType}.{methodPart}";
        Logger.Debug(LogCategory.Parser, $"  Trying aliased name: {aliasedName}");
        if (_currentModule!.Functions.Any(f => f.Name == aliasedName))
          return aliasedName;
        // Check for mangled overload variants of aliased name
        var aliasOverloads = _currentModule!.Functions.Where(f => f.Name.StartsWith(aliasedName + "$")).ToList();
        if (aliasOverloads.Count > 0)
          return aliasedName;
        // Try suffix match on aliased name too
        var aliasSuffixPattern = $".{aliasedName}";
        var aliasSuffixDollar = $".{aliasedName}$";
        var aliasSuffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith(aliasSuffixPattern) || f.Name.Contains(aliasSuffixDollar)).ToList();
        Logger.Debug(LogCategory.Parser, $"  Alias suffix matches: {aliasSuffixMatches.Count} - {string.Join(", ", aliasSuffixMatches.Select(f => f.Name))}");
        if (aliasSuffixMatches.Count == 1)
          return aliasSuffixMatches[0].Name;
        if (aliasSuffixMatches.Count > 1) {
          var aliasBaseNames = aliasSuffixMatches.Select(f => UnmangleName(f.Name)).Distinct().ToList();
          if (aliasBaseNames.Count == 1)
            return aliasBaseNames[0];
        }
      }
    }
    return null;
  }

  /// Check if a type name is an associated type of the current struct being parsed.
  private bool IsAssociatedTypeName(string typeName) {
    return _typeRegistry.TryGetValue(typeName, out var type) && type is MlirTypeParameterType;
  }

  /// Find an interface method by name across all registered interfaces.
  private MlirInterfaceMethodSignature? FindInterfaceMethod(string methodName) {
    foreach (var (_, registeredType) in _typeRegistry) {
      if (registeredType is MlirInterfaceType iface) {
        var method = iface.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method != null) return method;
      }
    }
    return null;
  }

  private record LoopContext(string SourceLabel, string HeaderLabel, string ExitLabel);

  private static readonly Dictionary<TokenType, (MaxonBinOperator Op, int Precedence)> BinaryOperators = new() {
    { TokenType.Or, (MaxonBinOperator.Or, 0) },
    { TokenType.And, (MaxonBinOperator.And, 1) },
    { TokenType.EqualsEquals, (MaxonBinOperator.Eq, 2) },
    { TokenType.NotEquals, (MaxonBinOperator.Ne, 2) },
    { TokenType.LessThan, (MaxonBinOperator.Lt, 2) },
    { TokenType.GreaterThan, (MaxonBinOperator.Gt, 2) },
    { TokenType.LessEquals, (MaxonBinOperator.Le, 2) },
    { TokenType.GreaterEquals, (MaxonBinOperator.Ge, 2) },
    { TokenType.Pipe, (MaxonBinOperator.BitOr, 3) },
    { TokenType.Caret, (MaxonBinOperator.BitXor, 4) },
    { TokenType.Ampersand, (MaxonBinOperator.BitAnd, 5) },
    { TokenType.LeftShift, (MaxonBinOperator.Shl, 6) },
    { TokenType.RightShift, (MaxonBinOperator.Shr, 6) },
    { TokenType.Plus, (MaxonBinOperator.Add, 7) },
    { TokenType.Minus, (MaxonBinOperator.Sub, 7) },
    { TokenType.Star, (MaxonBinOperator.Mul, 8) },
    { TokenType.Slash, (MaxonBinOperator.Div, 8) },
    { TokenType.Mod, (MaxonBinOperator.Mod, 8) },
  };

  private static readonly Dictionary<string, Func<MaxonValue, (MaxonOp Op, MaxonValue Result)>> BuiltinOps1 = new() {
    { "trunc", arg => { var op = new MaxonTruncOp(arg); return (op, op.Result); } },
    { "abs", arg => { var op = new MaxonAbsOp(arg); return (op, op.Result); } },
    { "sqrt", arg => { var op = new MaxonSqrtOp(arg); return (op, op.Result); } },
    { "floor", arg => { var op = new MaxonFloorOp(arg); return (op, op.Result); } },
    { "ceil", arg => { var op = new MaxonCeilOp(arg); return (op, op.Result); } },
    { "round", arg => { var op = new MaxonRoundOp(arg); return (op, op.Result); } },
  };

  private static readonly Dictionary<string, Func<MaxonValue, MaxonValue, (MaxonOp Op, MaxonValue Result)>> BuiltinOps2 = new() {
    { "min", (a, b) => { var op = new MaxonMinOp(a, b); return (op, op.Result); } },
    { "max", (a, b) => { var op = new MaxonMaxOp(a, b); return (op, op.Result); } },
  };

  // VarInfo now tracks struct type name for struct variables, or function type for function variables
  private record VarInfo(MaxonValueKind Kind, bool Mutable, MaxonValue Value, MlirBlock<MaxonOp> DefinedInBlock, string? StructTypeName = null, MlirFunctionType? FnTypeName = null);

  // Tracks parameter locations for unused parameter error reporting
  private readonly List<(string Name, int Line, int Column)> _paramLocations = [];

  /// <summary>
  /// Derives the namespace from the source file path.
  /// For stdlib files, prefixes with "stdlib." and uses path after "stdlib/".
  /// For user files, uses just the filename (since they're all in the same directory for build).
  /// Examples (includeFilename=true):
  ///   - stdlib/Math.maxon -> "stdlib.Math"
  ///   - stdlib/helpers/string/_utf8.maxon -> "stdlib.helpers.string._utf8"
  ///   - /path/to/project/main.maxon -> "main"
  ///   - /path/to/project/utils.maxon -> "utils"
  ///   - specs/fragments/register-allocator/int-nine-params-function.test -> "register-allocator"
  /// Examples (includeFilename=false):
  ///   - stdlib/Math.maxon -> "stdlib"
  ///   - stdlib/helpers/string/_utf8.maxon -> "stdlib.helpers.string"
  ///   - /path/to/project/main.maxon -> ""
  ///   - /path/to/project/utils.maxon -> ""
  /// </summary>
  private string DeriveNamespace(bool includeFilename = true) {
    if (_sourceFilePath == null) return "";

    var fileName = Path.GetFileNameWithoutExtension(_sourceFilePath);

    if (!_isStdlib) {
      // For spec test fragments, use the parent directory name as the namespace
      // but ONLY when includeFilename is true (for top-level functions)
      if (includeFilename) {
        var normalizedPath = _sourceFilePath.Replace('\\', '/');
        if (normalizedPath.Contains("/specs/fragments/")) {
          var parentDirName = Path.GetFileName(Path.GetDirectoryName(_sourceFilePath));
          return parentDirName ?? fileName;
        }
      }

      // For user code, just use the filename as the namespace (if includeFilename is true)
      return includeFilename ? fileName : "";
    }

    // For stdlib, derive from path after "stdlib/" and prefix with "stdlib."
    var dirName = Path.GetDirectoryName(_sourceFilePath) ?? "";
    dirName = dirName.Replace('\\', '/');

    var parts = new List<string> { "stdlib" };
    var pathSegments = dirName.Split('/', StringSplitOptions.RemoveEmptyEntries);

    // Collect path segments after "stdlib"
    bool foundStdlib = false;
    for (int i = 0; i < pathSegments.Length; i++) {
      if (pathSegments[i] == "stdlib") {
        foundStdlib = true;
        continue;
      }
      if (foundStdlib) {
        parts.Add(pathSegments[i]);
      }
    }

    // Add the filename if requested
    if (includeFilename) {
      parts.Add(fileName);
    }

    return string.Join(".", parts);
  }

  public MlirModule<MaxonOp> Parse() {
    Logger.Debug(LogCategory.Parser, "Starting parser");
    var module = new MlirModule<MaxonOp>();
    _currentModule = module;

    // Register __ManagedMemory opaque struct type (buffer pointer, length, capacity, element_size)
    if (!_typeRegistry.ContainsKey("__ManagedMemory")) {
      _typeRegistry["__ManagedMemory"] = new MlirStructType("__ManagedMemory", [
        new MlirStructField("buffer", MlirType.I64, false, true),
        new MlirStructField("length", MlirType.I64, false, true),
        new MlirStructField("capacity", MlirType.I64, false, true),
        new MlirStructField("element_size", MlirType.I64, false, true),
      ]);
    }

    SeedFromModule(seedModule, module);

    // Pre-scan to collect and evaluate top-level constants and global variables
    CollectAndEvaluateTopLevelDecls(module);

    SkipNewlines();
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      ParseTopLevel(module);
      SkipNewlines();
    }

    CopyStateToModule(module);

    Logger.Debug(LogCategory.Parser, $"Parser complete: {module.Functions.Count} functions");
    return module;
  }

  /// <summary>
  /// Pre-scan only: registers function signatures, type definitions, enums, interfaces,
  /// and constants without parsing function bodies. Used for cross-file forward references.
  /// </summary>
  public void PreScan(MlirModule<MaxonOp> targetModule) {
    _currentModule = targetModule;

    if (!_typeRegistry.ContainsKey("__ManagedMemory")) {
      _typeRegistry["__ManagedMemory"] = new MlirStructType("__ManagedMemory", [
        new MlirStructField("buffer", MlirType.I64, false, true),
        new MlirStructField("length", MlirType.I64, false, true),
        new MlirStructField("capacity", MlirType.I64, false, true),
        new MlirStructField("element_size", MlirType.I64, false, true),
      ]);
    }

    SeedFromModule(seedModule, targetModule);

    CollectAndEvaluateTopLevelDecls(targetModule);

    CopyStateToModule(targetModule);
  }

  /// <summary>
  /// Seeds the parser's internal dictionaries from a previously-parsed module so that
  /// cross-file forward references resolve correctly.
  /// </summary>
  private void SeedFromModule(MlirModule<MaxonOp>? source, MlirModule<MaxonOp> target) {
    if (source == null) return;
    foreach (var func in source.Functions) {
      if (!target.Functions.Any(f => f.Name == func.Name))
        target.AddFunction(func);
    }
    foreach (var (name, defaults) in source.FunctionDefaults)
      _functionDefaults.TryAdd(name, defaults);
    foreach (var (name, assocTypes) in source.InterfaceAssociatedTypes)
      _interfaceAssociatedTypes.TryAdd(name, assocTypes);
    foreach (var (aliasName, aliasInfo) in source.TypeAliasSources)
      _typeAliasSources.TryAdd(aliasName, aliasInfo.SourceTypeName);
  }

  /// <summary>
  /// Copies parser-local state (type registry, function defaults, etc.) back to
  /// the module so subsequent parsers or downstream passes can access it.
  /// </summary>
  private void CopyStateToModule(MlirModule<MaxonOp> module) {
    foreach (var (name, type) in _typeRegistry)
      module.TypeDefs[name] = type;
    foreach (var (name, defaults) in _functionDefaults)
      module.FunctionDefaults.TryAdd(name, defaults);
    foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
      Dictionary<string, MlirType>? typeParams = null;
      if (_typeRegistry.TryGetValue(aliasName, out var aliasType) && aliasType is MlirStructType st)
        typeParams = st.TypeParams.Count > 0 ? new Dictionary<string, MlirType>(st.TypeParams) : null;
      module.TypeAliasSources[aliasName] = new TypeAliasInfo(sourceTypeName, typeParams);
    }
    foreach (var (name, assocTypes) in _interfaceAssociatedTypes)
      module.InterfaceAssociatedTypes.TryAdd(name, assocTypes);
  }

  // ============================================================================
  // Pre-scanning for top-level let and var declarations
  // ============================================================================

  // Raw constant declaration: stores the token range for the initializer expression
  private record ConstantDecl(string Name, int TokenStart, int TokenEnd, int Line, int Column);

  private void CollectAndEvaluateTopLevelDecls(MlirModule<MaxonOp> module) {
    var constDecls = new List<ConstantDecl>();
    int savedPos = _pos;

    // First pass: scan for top-level declarations (constants, vars, function signatures, type declarations)
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      SkipNewlines();
      if (IsAtEnd() || Current().Type == TokenType.Eof) break;

      // Skip 'export' prefix
      if (Check(TokenType.Export)) {
        Advance();
      }

      if (Check(TokenType.Let)) {
        Advance(); // consume 'let'
        var nameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Equals);
        int exprStart = _pos;
        SkipToEndOfLine();
        if (_tokens[exprStart].Type == TokenType.LeftBracket) {
          _deferredArrayLets.Add(new DeferredArrayLet(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column));
        } else {
          constDecls.Add(new ConstantDecl(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column));
        }
      } else if (Check(TokenType.Var)) {
        Advance(); // consume 'var'
        var nameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Equals);
        var (fieldType, defaultValue) = ParseFieldDefault();
        var globalName = nameToken.Value;
        var kind = fieldType.ToValueKind();
        module.Globals.Add(new MlirGlobal(globalName, fieldType, defaultValue));
        _globalVars[globalName] = new GlobalVarInfo(kind, true);
      } else if (Check(TokenType.Function)) {
        // Pre-register function signature for forward references
        PreScanFunction(module, null);
      } else if (Check(TokenType.Type)) {
        PreScanType(module);
      } else if (Check(TokenType.Enum)) {
        PreScanEnum(module);
      } else if (Check(TokenType.TypeAlias)) {
        PreScanTypeAlias();
      } else if (Check(TokenType.Interface)) {
        PreScanInterface();
      } else if (Check(TokenType.Extension)) {
        PreScanExtensionBlock(module);
      } else {
        // Unknown top-level token, skip to next line
        SkipToEndOfLine();
      }
      SkipNewlines();
    }

    _pos = savedPos;

    // Evaluate constants (handling forward references)
    var evaluated = new Dictionary<string, object>();
    var evaluating = new HashSet<string>();

    foreach (var decl in constDecls) {
      EvaluateConstant(decl.Name, constDecls, evaluated, evaluating, decl.Line, decl.Column);
    }

    _topLevelConstants = evaluated;
  }

  /// <summary>
  /// Emit deferred top-level array let declarations at the start of the main function body.
  /// Re-parses each saved token range to produce array literal ops in the current block.
  /// </summary>
  private void EmitDeferredArrayLets() {
    foreach (var deferred in _deferredArrayLets) {
      int savedPos = _pos;
      _pos = deferred.TokenStart;

      var arrayResult = ParseArrayLiteral();
      var arrayValue = arrayResult.Value;

      var assignOp = new MaxonAssignOp(deferred.Name, arrayValue, isDeclaration: true, isMutable: false, MaxonValueKind.Struct);
      _currentBlock!.AddOp(assignOp);
      _variables[deferred.Name] = new VarInfo(MaxonValueKind.Struct, false, arrayValue, _currentBlock!, "Array");

      _pos = savedPos;
    }
  }

  /// <summary>
  /// Pre-scan a function declaration to register its signature.
  /// Only parses name, params, and return type; skips the body.
  /// </summary>
  private void PreScanFunction(MlirModule<MaxonOp> module, string? owningType) {
    Advance(); // consume 'function'
    var nameToken = Expect(TokenType.Identifier);
    var baseName = nameToken.Value;

    // Construct function name with namespace
    string funcName;
    if (owningType != null) {
      // Static/instance method: prepend namespace to type name
      // Don't include filename in namespace since owningType is already the type name
      var namespace_ = DeriveNamespace(includeFilename: false);
      var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? owningType : $"{namespace_}.{owningType}";
      funcName = $"{qualifiedTypeName}.{baseName}";
    } else {
      // Top-level function: prepend file-based namespace
      var namespace_ = DeriveNamespace();
      funcName = string.IsNullOrEmpty(namespace_) ? baseName : $"{namespace_}.{baseName}";
    }

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    var registrationName = ResolveOverloadRegistrationName(module, funcName, paramNames);

    if (!module.Functions.Any(f => f.Name == registrationName)) {
      Logger.Debug(LogCategory.Parser, $"PreScanFunction: registering {registrationName} (owningType={owningType ?? "null"})");
      var func = new MlirFunction<MaxonOp>(registrationName, paramNames, paramTypes, returnType, throwsType);
      module.AddFunction(func);

      if (paramDefaults.Count > 0) {
        _functionDefaults[registrationName] = paramDefaults;
      }
    }

    SkipToMatchingEnd();
  }

  /// <summary>
  /// Pre-scan a type declaration to register the type and its static methods.
  /// </summary>
  /// <summary>
  /// Parse a 'uses' clause (e.g., 'uses Key, Value') and register placeholder types
  /// in the type registry so that ParseTypeRef can resolve associated type names.
  /// Returns the list of associated type names parsed.
  /// </summary>
  private List<string> ParseUsesClause() {
    var names = new List<string>();
    if (!Check(TokenType.Uses)) return names;

    Advance(); // consume 'uses'
    names.Add(Expect(TokenType.Identifier).Value);
    while (Check(TokenType.Comma)) {
      Advance();
      names.Add(Expect(TokenType.Identifier).Value);
    }
    foreach (var name in names) {
      _typeRegistry[name] = new MlirTypeParameterType(name);
    }
    return names;
  }

  /// <summary>
  /// Parse an 'is' clause (e.g., 'is Equatable') for interface conformance.
  /// Returns the list of interface names the type conforms to.
  /// </summary>
  private (List<string> Interfaces, Dictionary<string, MlirType> TypeParams) ParseConformanceClause() {
    var names = new List<string>();
    var typeParams = new Dictionary<string, MlirType>();
    if (!Check(TokenType.Is)) return (names, typeParams);

    Advance(); // consume 'is'
    var ifaceName = Expect(TokenType.Identifier).Value;
    names.Add(ifaceName);
    if (Check(TokenType.With)) {
      Advance();
      int expectedCount = GetAssociatedTypeCount(ifaceName);
      var withTypes = ParseWithTypeArgs(expectedCount);
      ResolveWithTypeParams(ifaceName, withTypes, typeParams);
    }
    while (Check(TokenType.Comma)) {
      Advance();
      ifaceName = Expect(TokenType.Identifier).Value;
      names.Add(ifaceName);
      if (Check(TokenType.With)) {
        Advance();
        int expectedCount = GetAssociatedTypeCount(ifaceName);
        var withTypes = ParseWithTypeArgs(expectedCount);
        ResolveWithTypeParams(ifaceName, withTypes, typeParams);
      }
    }
    return (names, typeParams);
  }

  /// <summary>
  /// Parse type arguments after 'with': either a single type or (Type1, Type2, ...).
  /// When expectedCount > 1 and no parentheses, consumes comma-separated types.
  /// </summary>
  private List<MlirType> ParseWithTypeArgs(int expectedCount) {
    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      var types = new List<MlirType> { ParseTypeRef() };
      while (Check(TokenType.Comma)) {
        Advance();
        types.Add(ParseTypeRef());
      }
      Expect(TokenType.RightParen);
      return types;
    }
    var result = new List<MlirType> { ParseTypeRef() };
    while (result.Count < expectedCount && Check(TokenType.Comma)) {
      Advance();
      result.Add(ParseTypeRef());
    }
    return result;
  }

  private int GetAssociatedTypeCount(string ifaceName) {
    if (_interfaceAssociatedTypes.TryGetValue(ifaceName, out var assocNames))
      return assocNames.Count;
    return 1;
  }

  /// <summary>
  /// Maps interface associated type names to the concrete types specified in 'with' clauses.
  /// For example, 'Iterable with byte' maps Element -> byte.
  /// 'Dictionary with (Key, Value)' maps Key -> Key, Value -> Value.
  /// </summary>
  private void ResolveWithTypeParams(string ifaceName, List<MlirType> withTypes, Dictionary<string, MlirType> typeParams) {
    if (_interfaceAssociatedTypes.TryGetValue(ifaceName, out var assocNames)) {
      for (int i = 0; i < assocNames.Count && i < withTypes.Count; i++) {
        typeParams[assocNames[i]] = withTypes[i];
      }
    }
  }

  /// <summary>
  /// Parse an optional 'throws ErrorType' clause after a return type.
  /// Returns the error type if present, null otherwise.
  /// </summary>
  private MlirType? ParseThrowsClause() {
    if (!Check(TokenType.Throws)) return null;
    Advance(); // consume 'throws'
    return ParseTypeRef();
  }

  private void RemoveAssociatedTypePlaceholders(List<string> associatedTypeNames) {
    foreach (var name in associatedTypeNames) {
      _typeRegistry.Remove(name);
    }
  }

  /// <summary>
  /// Token-level forward scan to pre-register typealias names inside a type body
  /// as placeholder struct types, so the conformance clause can reference them.
  /// Does not advance the main position.
  /// </summary>
  private void PreRegisterTypeAliasNames() {
    int savedPos = _pos;
    int depth = 0;
    while (_pos < _tokens.Count && _tokens[_pos].Type != TokenType.Eof) {
      var t = _tokens[_pos];
      if (t.Type == TokenType.End) {
        if (depth == 0) break;
        depth--;
        _pos++;
      } else if (t.Type == TokenType.Function || t.Type == TokenType.Type
                 || t.Type == TokenType.Enum || t.Type == TokenType.Interface) {
        depth++;
        _pos++;
      } else if (t.Type == TokenType.TypeAlias && _pos + 1 < _tokens.Count
                 && _tokens[_pos + 1].Type == TokenType.Identifier) {
        var aliasName = _tokens[_pos + 1].Value;
        if (!_typeRegistry.ContainsKey(aliasName))
          _typeRegistry[aliasName] = new MlirStructType(aliasName, []);
        _pos += 2;
      } else {
        _pos++;
      }
    }
    _pos = savedPos;
  }

  private void PreScanType(MlirModule<MaxonOp> module) {
    Advance(); // consume 'type'
    var typeNameToken = Expect(TokenType.Identifier);
    var typeName = typeNameToken.Value;
    _currentTypeName = typeName;

    // Temporary entry so ParseTypeRef/PreScanInstanceMethod can resolve Self references
    if (!_typeRegistry.ContainsKey(typeName)) {
      _typeRegistry[typeName] = new MlirStructType(typeName, []);
    }

    // Pre-register typealias names inside the type body so the conformance
    // clause can reference them (e.g. `type Map is Iterable with Entry`
    // where `Entry` is a typealias defined inside Map)
    PreRegisterTypeAliasNames();

    var associatedTypeNames = ParseUsesClause();
    var (conformingInterfaces, conformanceTypeParams) = ParseConformanceClause();

    // Builtin* interfaces are reserved for stdlib types
    if (!_isStdlib) {
      var builtinInterface = conformingInterfaces.FirstOrDefault(i => i.StartsWith("Builtin"));
      if (builtinInterface != null)
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Interface '{builtinInterface}' can only be implemented by stdlib types",
          typeNameToken.Line, typeNameToken.Column);
    }

    SkipNewlines();

    var fields = new List<MlirStructField>();

    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      bool isFieldExported = false;
      if (Check(TokenType.Export)) {
        Advance();
        isFieldExported = true;
      }

      if (Check(TokenType.Static)) {
        Advance(); // consume 'static'

        if (Check(TokenType.Function)) {
          PreScanFunction(module, typeName);
          SkipNewlines();
          continue;
        }

        // Static var/let: skip
        if (Check(TokenType.Var) || Check(TokenType.Let)) {
          Advance();
          Advance(); // consume name
          Expect(TokenType.Equals);
          SkipToEndOfLine();
          SkipNewlines();
          continue;
        }
      }

      if (Check(TokenType.Function)) {
        PreScanInstanceMethod(module, typeName);
        SkipNewlines();
        continue;
      }

      // Internal typealias declaration (e.g., typealias ElementArray is Array with Element)
      if (Check(TokenType.TypeAlias)) {
        PreScanTypeAlias();
        SkipNewlines();
        continue;
      }

      // Instance field declaration
      if (Check(TokenType.Var) || Check(TokenType.Let)) {
        bool isMutable = Check(TokenType.Var);
        Advance(); // consume var/let
        var fieldName = Expect(TokenType.Identifier).Value;

        MlirType fieldType;
        MlirAttribute? defaultValue = null;
        if (Check(TokenType.Equals)) {
          Advance();
          (fieldType, defaultValue) = ParseFieldDefault();
        } else {
          fieldType = ParseTypeRef();
          // Provide implicit defaults for primitive types
          if (fieldType == MlirType.I64) defaultValue = new IntegerAttr(0, MlirType.I64);
          else if (fieldType == MlirType.F64) defaultValue = new FloatAttr(0.0, MlirType.F64);
          else if (fieldType == MlirType.I1) defaultValue = new IntegerAttr(0, MlirType.I1);
          else if (fieldType == MlirType.I8) defaultValue = new IntegerAttr(0, MlirType.I8);
        }

        fields.Add(new MlirStructField(fieldName, fieldType, isFieldExported, isMutable, defaultValue));
        SkipNewlines();
        continue;
      }

      SkipToEndOfLine();
      SkipNewlines();
    }

    // Replace temporary entry with complete struct type
    _typeRegistry[typeName] = new MlirStructType(typeName, fields, associatedTypeNames, conformingInterfaces,
      typeParams: conformanceTypeParams.Count > 0 ? conformanceTypeParams : null);
    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);

    // Validate interface conformance: each required method must exist with matching signature
    var structTypeParams = conformanceTypeParams;

    // Construct namespace-qualified type name for method lookup
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";

    foreach (var ifaceName in conformingInterfaces) {
      if (!_typeRegistry.TryGetValue(ifaceName, out var ifaceType) || ifaceType is not MlirInterfaceType iface)
        continue;

      // Check for missing type binding: interface has associated types but type doesn't provide 'with'
      if (_interfaceAssociatedTypes.TryGetValue(ifaceName, out var assocTypeNames) && assocTypeNames.Count > 0) {
        var missingBindings = assocTypeNames.Where(n => !structTypeParams.ContainsKey(n)).ToList();
        if (missingBindings.Count > 0) {
          throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
            $"Type '{typeName}' does not define required associated type '{missingBindings[0]}' from interface '{ifaceName}'",
            typeNameToken.Line, typeNameToken.Column);
        }
      }

      var missingMethods = new List<string>();
      var wrongSignatureMethods = new List<string>();
      foreach (var method in iface.Methods) {
        var qualifiedName = $"{qualifiedTypeName}.{method.Name}";
        var func = module.Functions.FirstOrDefault(f => f.Name == qualifiedName);

        // Static method signatures are handled by the compiler directly —
        // only validate throws conformance
        if (method.IsStatic) {
          if (func != null && method.ThrowsTypeName != null)
            ValidateThrowsConformance(func, typeName, method.Name, ifaceName, method.ThrowsTypeName, typeNameToken);
          continue;
        }

        if (func == null) {
          missingMethods.Add(method.FormatResolved(structTypeParams));
        } else if (!SignatureMatches(method, func, ifaceName, typeName, structTypeParams)) {
          var actualSig = FormatFunctionSignature(method.Name, func);
          wrongSignatureMethods.Add($"{actualSig} (expected {method.FormatResolved(structTypeParams)})");
        } else if (method.ThrowsTypeName != null) {
          ValidateThrowsConformance(func, typeName, method.Name, ifaceName, method.ThrowsTypeName, typeNameToken);
        }
      }

      if (missingMethods.Count > 0) {
        var details = string.Join("\n", missingMethods.Select(m => $"  - {m}"));
        throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
          $"Partial interface implementation: type '{typeName}' is missing {missingMethods.Count} method(s):\n{details}",
          typeNameToken.Line, typeNameToken.Column);
      }

      if (wrongSignatureMethods.Count > 0) {
        var details = string.Join("\n", wrongSignatureMethods.Select(m => $"  - {m}"));
        throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
          $"Partial interface implementation: type '{typeName}' has {wrongSignatureMethods.Count} method(s) with wrong signature:\n{details}",
          typeNameToken.Line, typeNameToken.Column);
      }
    }

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  /// <summary>
  /// Validates that a function's throws clause conforms to the interface requirement:
  /// must throw, and the thrown type must conform to Error.
  /// </summary>
  private void ValidateThrowsConformance(MlirFunction<MaxonOp> func, string typeName, string methodName, string ifaceName, string requiredThrowsType, Token typeNameToken) {
    if (func.ThrowsType == null) {
      throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
        $"Method '{typeName}.{methodName}' must throw '{requiredThrowsType}' as required by interface '{ifaceName}'",
        typeNameToken.Line, typeNameToken.Column);
    }
    var throwsName = func.ThrowsType.Name;
    if (_typeRegistry.TryGetValue(throwsName, out var throwsTypeEntry)
        && throwsTypeEntry is MlirEnumType throwsEnum
        && !throwsEnum.ConformingInterfaces.Contains("Error")) {
      throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
        $"Method '{typeName}.{methodName}' throws '{throwsName}' which does not conform to Error",
        typeNameToken.Line, typeNameToken.Column);
    }
  }

  /// <summary>
  /// Maps a source-level type name (e.g., "int") to the corresponding MlirType.Name (e.g., "i64").
  /// </summary>
  private static string ResolveTypeName(string sourceTypeName) {
    return sourceTypeName switch {
      "int" => MlirType.I64.Name,
      "float" => MlirType.F64.Name,
      "bool" => MlirType.I1.Name,
      "byte" => MlirType.I8.Name,
      "cstring" => MlirType.I64.Name,
      _ => sourceTypeName
    };
  }

  /// <summary>
  /// Checks whether a function's signature matches an interface method's expected signature.
  /// Instance methods have 'self' as the first parameter which is skipped during comparison.
  /// Self-typed parameters in the interface (resolved to ifaceName) match the implementing typeName.
  /// Associated types of the implementing type are erased to i64, so the check accounts for that.
  /// </summary>
  private bool SignatureMatches(MlirInterfaceMethodSignature method, MlirFunction<MaxonOp> func, string ifaceName, string typeName, Dictionary<string, MlirType> typeParams) {
    // Instance methods have 'self' as first param; skip it
    var funcParamTypes = func.ParamTypes.Skip(1).ToList();

    if (method.ParamTypeNames.Count != funcParamTypes.Count)
      return false;

    // Get the implementing type's associated type names for erasure detection
    _ = _typeRegistry.TryGetValue(typeName, out var implType) && implType is MlirStructType st
      ? st.AssociatedTypeNames : [];

    for (int i = 0; i < method.ParamTypeNames.Count; i++) {
      var expectedTypeName = ResolveInterfaceTypeName(method.ParamTypeNames[i], ifaceName, typeName, typeParams);
      if (expectedTypeName == null || ResolveTypeName(expectedTypeName) != funcParamTypes[i].Name)
        return false;
    }

    var expectedReturnName = ResolveInterfaceTypeName(method.ReturnTypeName, ifaceName, typeName, typeParams);
    var expectedReturn = expectedReturnName != null ? ResolveTypeName(expectedReturnName) : MlirType.Void.Name;
    var actualReturn = func.ReturnType?.Name ?? MlirType.Void.Name;
    if (expectedReturn != actualReturn)
      return false;

    return true;
  }

  /// <summary>
  /// Resolves a type name in an interface method signature, handling Self references and associated types.
  /// Associated types that belong to the implementing type are kept as type parameter names.
  /// </summary>
  private static string? ResolveInterfaceTypeName(string? name, string ifaceName, string typeName, Dictionary<string, MlirType> typeParams) {
    if (name == null) return null;
    if (name == ifaceName) return typeName;
    // Resolve associated types (e.g., Element -> byte when struct declares 'with byte')
    if (typeParams.TryGetValue(name, out var resolvedType)) {
      return MlirType.FormatAsSourceName(resolvedType);
    }
    return name;
  }

  /// <summary>
  /// Formats a function's actual signature in the same style as MlirInterfaceMethodSignature.Format(),
  /// using source-level type names for readability.
  /// </summary>
  private static string FormatFunctionSignature(string methodName, MlirFunction<MaxonOp> func) {
    // Skip 'self' parameter
    var paramNames = func.ParamNames.Skip(1).ToList();
    var paramTypes = func.ParamTypes.Skip(1).ToList();
    var paramsStr = string.Join(", ", paramNames.Zip(paramTypes, (n, t) => $"{n} {MlirType.FormatAsSourceName(t)}"));
    var returnStr = func.ReturnType != null ? $" returns {MlirType.FormatAsSourceName(func.ReturnType)}" : " returns void";
    return $"{methodName}({paramsStr}){returnStr}";
  }


  private void PreScanEnum(MlirModule<MaxonOp> module) {
    Advance(); // consume 'enum'
    var nameToken = Expect(TokenType.Identifier);
    var enumName = nameToken.Value;
    _currentTypeName = enumName;

    var (conformingInterfaces, _) = ParseConformanceClause();

    // Temporary entry so ParseTypeRef can resolve forward references
    if (!_typeRegistry.TryGetValue(enumName, out MlirType? value)) {
      value = new MlirEnumType(enumName, [], null, conformingInterfaces);
      _typeRegistry[enumName] = value;
    } else if (value is MlirEnumType existingPre && existingPre.ConformingInterfaces.Count == 0 && conformingInterfaces.Count > 0) {
      // Pre-registered entry had no conforming interfaces; update with parsed ones
      value = new MlirEnumType(enumName, [.. existingPre.Cases], existingPre.BackingType, conformingInterfaces);
      _typeRegistry[enumName] = value;
    }

    SkipNewlines();

    var cases = new List<MlirEnumCase>();
    var caseNames = new HashSet<string>();
    MlirType? backingType = null;
    int ordinal = 0;

    while (!Check(TokenType.End) && !Check(TokenType.Function) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End) || Check(TokenType.Function)) break;

      // Implicit string-backed ("North") or char-backed ('N'): literal as case name
      if (Check(TokenType.StringLiteral) || Check(TokenType.CharacterLiteral)) {
        bool isString = Check(TokenType.StringLiteral);
        var litToken = Advance();
        var litName = litToken.Value;

        if (!caseNames.Add(litName)) {
          throw new CompileError(ErrorCode.SemanticEnumDuplicateCase,
            $"duplicate enum case: '{litName}'", litToken.Line, litToken.Column);
        }

        MlirType expectedBacking = isString ? new MlirStringBackingType() : new MlirCharBackingType();
        if (backingType == null) {
          backingType = expectedBacking;
        } else if (backingType.GetType() != expectedBacking.GetType()) {
          throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
            $"raw value type mismatch: expected {(isString ? "string" : "char")}",
            litToken.Line, litToken.Column);
        }

        foreach (var existing in cases) {
          if (existing.RawValue is string existingVal && existingVal == litName) {
            throw new CompileError(ErrorCode.SemanticEnumDuplicateRawValue,
              $"duplicate raw value: '{litName}'", litToken.Line, litToken.Column);
          }
        }

        cases.Add(new MlirEnumCase(litName, ordinal, litName));
        ordinal++;
        SkipNewlines();
        continue;
      }

      var caseToken = Expect(TokenType.Identifier);
      var caseName = caseToken.Value;

      if (!caseNames.Add(caseName)) {
        throw new CompileError(ErrorCode.SemanticEnumDuplicateCase,
          $"duplicate enum case: '{caseName}'", caseToken.Line, caseToken.Column);
      }

      // Associated value syntax: caseName(paramName type, ...)
      if (Check(TokenType.LeftParen)) {
        Advance(); // consume '('
        var assocValues = new List<(string Name, MlirType Type)>();
        while (!Check(TokenType.RightParen) && !IsAtEnd()) {
          var paramName = Expect(TokenType.Identifier).Value;
          var paramType = ParseTypeRef();
          assocValues.Add((paramName, paramType));
          if (Check(TokenType.Comma)) Advance();
        }
        Expect(TokenType.RightParen);
        cases.Add(new MlirEnumCase(caseName, ordinal, associatedValues: assocValues));
        ordinal++;
        SkipNewlines();
        continue;
      }

      if (Check(TokenType.Equals)) {
        Advance(); // consume '='

        bool isNegative = false;
        if (Check(TokenType.Minus)) {
          isNegative = true;
          Advance();
        }

        if (Check(TokenType.IntegerLiteral)) {
          var rawVal = ParseIntegerLiteral(Advance());
          if (isNegative) rawVal = -rawVal;

          if (backingType == null) {
            backingType = MlirType.I64;
          } else if (backingType != MlirType.I64) {
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.F64 ? "float" : "int")}, got int'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is long existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticEnumDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
        } else if (Check(TokenType.FloatLiteral)) {
          var rawVal = ParseFloatLiteral(Advance());
          if (isNegative) rawVal = -rawVal;

          if (backingType == null) {
            backingType = MlirType.F64;
          } else if (backingType != MlirType.F64) {
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              $"raw value type mismatch: 'expected int, got float'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is double existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticEnumDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
        } else if (Check(TokenType.StringLiteral)) {
          if (isNegative) {
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              "cannot negate a string raw value", caseToken.Line, caseToken.Column);
          }

          var rawVal = Advance().Value;

          if (backingType == null) {
            backingType = new MlirStringBackingType();
          } else if (backingType is not MlirStringBackingType) {
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.I64 ? "int" : backingType == MlirType.F64 ? "float" : "String")}, got String'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is string existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticEnumDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
        } else if (Check(TokenType.CharacterLiteral)) {
          if (isNegative) {
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              "cannot negate a character raw value", caseToken.Line, caseToken.Column);
          }

          var rawVal = Advance().Value;

          if (backingType == null) {
            backingType = new MlirCharBackingType();
          } else if (backingType is not MlirCharBackingType) {
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.I64 ? "int" : backingType == MlirType.F64 ? "float" : "char")}, got char'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is string existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticEnumDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
        } else {
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"expected integer, float, string, or character literal for enum raw value",
            caseToken.Line, caseToken.Column);
        }
      } else {
        cases.Add(new MlirEnumCase(caseName, ordinal));
      }

      ordinal++;
      SkipNewlines();
    }

    // Pre-scan methods inside the enum
    while (Check(TokenType.Function) && !IsAtEnd()) {
      PreScanInstanceMethod(module, enumName);
      SkipNewlines();
    }

    var existingEnum = (MlirEnumType)value;
    _typeRegistry[enumName] = new MlirEnumType(enumName, cases, backingType, existingEnum.ConformingInterfaces);
    _currentTypeName = null;

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  /// <summary>
  /// Pre-scan an interface declaration to register its method signatures.
  /// Interface methods are signature-only (no body, no end keyword per method).
  /// Handles uses clause, static functions, typealias declarations, and throws clauses.
  /// </summary>
  private void PreScanInterface() {
    Advance(); // consume 'interface'
    var nameToken = Expect(TokenType.Identifier);
    var interfaceName = nameToken.Value;
    // Allow Self to resolve inside interface method signatures
    _currentTypeName = interfaceName;

    // Temporary entry so ParseTypeRef can resolve Self references
    if (!_typeRegistry.ContainsKey(interfaceName)) {
      _typeRegistry[interfaceName] = new MlirInterfaceType(interfaceName, []);
    }

    // Handle 'uses' clause for associated types (e.g., interface Iterable uses Element)
    var associatedTypeNames = ParseUsesClause();

    SkipNewlines();

    var methods = new List<MlirInterfaceMethodSignature>();

    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      // Skip 'export' on members
      if (Check(TokenType.Export)) Advance();

      // Track static methods (factory methods, static requirements)
      bool isStatic = false;
      if (Check(TokenType.Static)) {
        Advance();
        isStatic = true;
      }

      // Skip typealias declarations inside interfaces
      if (Check(TokenType.TypeAlias)) {
        SkipToEndOfLine();
        SkipNewlines();
        continue;
      }

      if (!Check(TokenType.Function)) {
        SkipToEndOfLine();
        SkipNewlines();
        continue;
      }

      Advance(); // consume 'function'
      var methodName = Expect(TokenType.Identifier).Value;

      Expect(TokenType.LeftParen);
      var paramNames = new List<string>();
      var paramTypeNames = new List<string>();
      if (!Check(TokenType.RightParen)) {
        do {
          var paramToken = Expect(TokenType.Identifier);
          var paramTypeName = ExpectTypeName();
          paramNames.Add(paramToken.Value);
          paramTypeNames.Add(paramTypeName);
        } while (Check(TokenType.Comma) && Advance() != null);
      }
      Expect(TokenType.RightParen);

      string? returnTypeName = null;
      if (Check(TokenType.Returns)) {
        Advance();
        returnTypeName = ExpectTypeName();
      }

      string? throwsTypeName = null;
      if (Check(TokenType.Throws)) {
        Advance();
        throwsTypeName = ExpectTypeName();
      }

      // Skip rest of line
      SkipToEndOfLine();

      methods.Add(new MlirInterfaceMethodSignature(methodName, paramTypeNames, paramNames, returnTypeName, isStatic, throwsTypeName));
      SkipNewlines();
    }

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();

    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    _typeRegistry[interfaceName] = new MlirInterfaceType(interfaceName, methods);
    if (associatedTypeNames.Count > 0) {
      _interfaceAssociatedTypes[interfaceName] = associatedTypeNames;
    }
  }

  /// <summary>
  /// Pre-scan an extension block: register method signatures for each concrete type conforming to the interface.
  /// </summary>
  private void PreScanExtensionBlock(MlirModule<MaxonOp> module) {
    ProcessExtensionBlock(module, (m, positions, typeName) => {
      foreach (var pos in positions) {
        _pos = pos;
        PreScanInstanceMethod(m, typeName);
      }
    });
  }

  /// <summary>
  /// Parse an extension block: parse method bodies for each concrete type conforming to the interface.
  /// </summary>
  private void ParseExtensionBlock(MlirModule<MaxonOp> module) {
    ProcessExtensionBlock(module, (m, positions, typeName) => {
      foreach (var pos in positions) {
        _pos = pos;
        ParseInstanceMethod(m, typeName);
      }
    });
  }

  /// <summary>
  /// Shared logic for processing extension blocks. Scans the block structure,
  /// finds conforming types, sets up type parameters, and delegates method
  /// processing to the provided callback (pre-scan vs full parse).
  /// </summary>
  private void ProcessExtensionBlock(
      MlirModule<MaxonOp> module,
      Action<MlirModule<MaxonOp>, List<int>, string> processFunction) {
    Advance(); // consume 'extension'
    var interfaceNameToken = Expect(TokenType.Identifier);
    var interfaceName = interfaceNameToken.Value;

    SkipNewlines();
    var typealiasPositions = new List<int>();
    var functionPositions = new List<int>();

    // Scan to find top-level typealias and function positions within the block
    int scanPos = _pos;
    int depth = 1;
    while (scanPos < _tokens.Count && depth > 0) {
      var tokenType = _tokens[scanPos].Type;
      if (depth == 1) {
        if (tokenType == TokenType.TypeAlias) {
          typealiasPositions.Add(scanPos);
        } else if (tokenType == TokenType.Function) {
          functionPositions.Add(scanPos);
        }
      }
      if (tokenType == TokenType.Function || tokenType == TokenType.If
          || tokenType == TokenType.While || tokenType == TokenType.For
          || tokenType == TokenType.Match) {
        depth++;
      } else if (tokenType == TokenType.Else) {
        depth++;
      } else if (tokenType == TokenType.Otherwise && scanPos + 1 < _tokens.Count
                 && _tokens[scanPos + 1].Type == TokenType.CharacterLiteral) {
        depth++;
      } else if (tokenType == TokenType.End) {
        depth--;
      }
      scanPos++;
    }
    int endPos = scanPos;
    if (endPos < _tokens.Count && _tokens[endPos].Type == TokenType.CharacterLiteral) endPos++;

    _interfaceAssociatedTypes.TryGetValue(interfaceName, out var assocTypeNames);

    // Snapshot before iterating — PreScanTypeAlias modifies _typeRegistry
    var conformingTypes = new List<(string Name, MlirStructType Type)>();
    foreach (var (typeName, type) in _typeRegistry) {
      if (type is not MlirStructType structType) continue;
      if (!structType.ConformingInterfaces.Contains(interfaceName)) continue;
      if (_typeAliasSources.ContainsKey(typeName)) continue;
      conformingTypes.Add((typeName, structType));
    }

    foreach (var (typeName, structType) in conformingTypes) {
      _currentTypeName = typeName;

      var registeredParams = new List<string>();
      if (assocTypeNames != null) {
        foreach (var assocName in assocTypeNames) {
          if (structType.TypeParams.TryGetValue(assocName, out var concreteType)) {
            _typeRegistry[assocName] = concreteType;
          } else {
            _typeRegistry[assocName] = new MlirTypeParameterType(assocName);
          }
          registeredParams.Add(assocName);
        }
      }

      foreach (var pos in typealiasPositions) {
        _pos = pos;
        PreScanTypeAlias();
      }

      processFunction(module, functionPositions, typeName);

      RemoveAssociatedTypePlaceholders(registeredParams);
    }

    _currentTypeName = null;
    _pos = endPos;
  }

  /// <summary>
  /// Pre-scan a typealias declaration: typealias Name = SourceType with (Type1, Type2, ...)
  /// Creates a concrete struct type by substituting associated type parameters.
  /// Also supports "with N Type" form where N is an integer capacity hint (e.g., Vector with 3 int).
  /// </summary>
  private void PreScanTypeAlias() {
    Advance(); // consume 'typealias'
    var aliasNameToken = Expect(TokenType.Identifier);
    var aliasName = aliasNameToken.Value;
    Expect(TokenType.Equals);
    var sourceNameToken = Expect(TokenType.Identifier);
    var sourceName = sourceNameToken.Value;

    if (!_typeRegistry.TryGetValue(sourceName, out var sourceType))
      throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown type: {sourceName}", sourceNameToken.Line, sourceNameToken.Column);

    if (sourceType is not MlirStructType sourceStruct)
      throw new CompileError(ErrorCode.ParserExpectedType, $"Type '{sourceName}' is not a struct type", sourceNameToken.Line, sourceNameToken.Column);

    if (sourceStruct.AssociatedTypeNames.Count == 0)
      throw new CompileError(ErrorCode.ParserExpectedType, $"Type '{sourceName}' has no associated types", sourceNameToken.Line, sourceNameToken.Column);

    Expect(TokenType.With);

    var concreteTypes = new List<MlirType>();
    var constParams = new Dictionary<string, long>();

    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      concreteTypes.Add(ParseTypeRef());
      while (Check(TokenType.Comma)) {
        Advance();
        concreteTypes.Add(ParseTypeRef());
      }
      Expect(TokenType.RightParen);
    } else {
      // Check for "with N Type" form: integer followed by type
      if (Check(TokenType.IntegerLiteral)) {
        var intToken = Advance();
        constParams["__capacity"] = ParseIntegerLiteral(intToken);
      }
      concreteTypes.Add(ParseTypeRef());
    }

    if (concreteTypes.Count != sourceStruct.AssociatedTypeNames.Count)
      throw new CompileError(ErrorCode.ParserExpectedType,
        $"Type '{sourceName}' expects {sourceStruct.AssociatedTypeNames.Count} type argument(s), got {concreteTypes.Count}",
        aliasNameToken.Line, aliasNameToken.Column);

    // Build substitution map: associated type name -> concrete type
    var substitution = new Dictionary<string, MlirType>();
    for (int i = 0; i < sourceStruct.AssociatedTypeNames.Count; i++) {
      substitution[sourceStruct.AssociatedTypeNames[i]] = concreteTypes[i];
    }

    RegisterConcreteTypeAlias(aliasName, sourceName, sourceStruct, substitution,
      constParams.Count > 0 ? constParams : null);
  }

  private void RegisterConcreteTypeAlias(
      string aliasName,
      string sourceName,
      MlirStructType sourceStruct,
      Dictionary<string, MlirType> substitution,
      Dictionary<string, long>? constParams = null) {
    var concreteFields = new List<MlirStructField>();
    foreach (var field in sourceStruct.Fields) {
      var fieldType = substitution.TryGetValue(field.Type.Name, out var concreteType)
        ? concreteType
        : field.Type;
      concreteFields.Add(new MlirStructField(field.Name, fieldType, field.IsExported, field.IsMutable, field.DefaultValue));
    }
    _typeRegistry[aliasName] = new MlirStructType(aliasName, concreteFields,
      conformingInterfaces: [.. sourceStruct.ConformingInterfaces],
      constParams: constParams,
      typeParams: substitution.Count > 0 ? substitution : null);
    Logger.Debug(LogCategory.Parser, $"Registering type alias: {aliasName} -> {sourceName}");
    _typeAliasSources[aliasName] = sourceName;
  }

  private void PreScanInstanceMethod(MlirModule<MaxonOp> module, string typeName) {
    Advance(); // consume 'function'
    var nameToken = Expect(TokenType.Identifier);

    // Construct qualified method name with namespace
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";
    var methodName = $"{qualifiedTypeName}.{nameToken.Value}";

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    // Instance method gets 'self' as first parameter
    var structType = _typeRegistry[typeName];

    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { (MlirType)structType };
    allParamTypes.AddRange(paramTypes);

    var registrationName = ResolveOverloadRegistrationName(module, methodName, allParamNames);

    // Register if not already present (by mangled name)
    if (!module.Functions.Any(f => f.Name == registrationName)) {
      var func = new MlirFunction<MaxonOp>(registrationName, allParamNames, allParamTypes, returnType, throwsType);
      module.AddFunction(func);

      if (paramDefaults.Count > 0) {
        var offsetDefaults = new Dictionary<int, MlirAttribute>();
        foreach (var (idx, attr) in paramDefaults) {
          offsetDefaults[idx + 1] = attr;
        }
        _functionDefaults[registrationName] = offsetDefaults;
      }
    }

    SkipToMatchingEnd();
  }

  private void SkipToEndOfLine() {
    while (!IsAtEnd() && !Check(TokenType.Newline) && Current().Type != TokenType.Eof) {
      Advance();
    }
  }

  /// <summary>
  /// Skip to matching 'end' keyword by tracking nesting depth for if/while/function blocks.
  /// Also skips any trailing end label.
  /// </summary>
  private void SkipToMatchingEnd() {
    SkipNewlines();
    int depth = 1;
    while (!IsAtEnd() && depth > 0) {
      if (Check(TokenType.Function) || Check(TokenType.If) || Check(TokenType.While) || Check(TokenType.For) || Check(TokenType.Match)) {
        depth++;
      } else if (Check(TokenType.Else)) {
        // else 'label' introduces a new block ending with end
        depth++;
      } else if (Check(TokenType.Otherwise) && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.CharacterLiteral) {
        // otherwise 'label' introduces a block ending with end (but not inline otherwise <value>)
        depth++;
      } else if (Check(TokenType.End)) {
        depth--;
      }
      Advance();
    }
    // Skip end label if present
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  private object EvaluateConstant(string name, List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating, int refLine = 0, int refCol = 0) {
    if (evaluated.TryGetValue(name, out var val)) return val;

    var decl = decls.FirstOrDefault(d => d.Name == name) ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined constant '{name}'", refLine, refCol);
    if (!evaluating.Add(name)) {
      // Circular dependency: collect all names in the cycle
      var cycleNames = string.Join(", ", evaluating);
      var lastDecl = decls.Last(d => evaluating.Contains(d.Name));
      throw new CompileError(ErrorCode.ParserCircularDependency,
        $"Circular dependency detected among global constants: {cycleNames}",
        lastDecl.Line, lastDecl.Column);
    }

    int savedPos = _pos;
    _pos = decl.TokenStart;
    var result = EvalConstExpr(decls, evaluated, evaluating);
    _pos = savedPos;

    evaluated[name] = result;
    evaluating.Remove(name);
    return result;
  }

  // ============================================================================
  // Compile-time constant expression evaluator
  // ============================================================================

  private object EvalConstExpr(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    return EvalConstLogicalOr(decls, evaluated, evaluating);
  }

  private object EvalConstLogicalOr(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstLogicalAnd(decls, evaluated, evaluating);
    while (Check(TokenType.Or)) {
      Advance();
      var rhs = EvalConstLogicalAnd(decls, evaluated, evaluating);
      lhs = (bool)lhs || (bool)rhs;
    }
    return lhs;
  }

  private object EvalConstLogicalAnd(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstComparison(decls, evaluated, evaluating);
    while (Check(TokenType.And)) {
      Advance();
      var rhs = EvalConstComparison(decls, evaluated, evaluating);
      lhs = (bool)lhs && (bool)rhs;
    }
    return lhs;
  }

  private object EvalConstComparison(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstAddSub(decls, evaluated, evaluating);

    if (Check(TokenType.EqualsEquals) || Check(TokenType.NotEquals) ||
        Check(TokenType.LessThan) || Check(TokenType.GreaterThan) ||
        Check(TokenType.LessEquals) || Check(TokenType.GreaterEquals)) {
      var opType = Advance().Type;
      var rhs = EvalConstAddSub(decls, evaluated, evaluating);
      return EvalConstComparisonOp(lhs, rhs, opType);
    }

    return lhs;
  }

  private static bool EvalConstComparisonOp(object lhs, object rhs, TokenType op) {
    (var lVal, var rVal) = PromoteConstPair(lhs, rhs);

    if (lVal is not IComparable lCmp)
      throw new InvalidOperationException($"Cannot compare {lVal?.GetType().Name} and {rVal?.GetType().Name}");

    return op switch {
      TokenType.EqualsEquals => lVal.Equals(rVal),
      TokenType.NotEquals => !lVal.Equals(rVal),
      TokenType.LessThan => lCmp.CompareTo(rVal) < 0,
      TokenType.GreaterThan => lCmp.CompareTo(rVal) > 0,
      TokenType.LessEquals => lCmp.CompareTo(rVal) <= 0,
      TokenType.GreaterEquals => lCmp.CompareTo(rVal) >= 0,
      _ => throw new InvalidOperationException($"Unsupported comparison: {op}")
    };
  }

  private object EvalConstAddSub(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstMulDiv(decls, evaluated, evaluating);

    while (Check(TokenType.Plus) || Check(TokenType.Minus)) {
      var op = Advance().Type;
      var rhs = EvalConstMulDiv(decls, evaluated, evaluating);
      (var lVal, var rVal) = PromoteConstPair(lhs, rhs);

      if (lVal is long li && rVal is long ri) {
        lhs = op == TokenType.Plus ? li + ri : li - ri;
      } else if (lVal is double ld && rVal is double rd) {
        lhs = op == TokenType.Plus ? ld + rd : ld - rd;
      } else {
        throw new InvalidOperationException($"Cannot add/subtract {lVal?.GetType().Name} and {rVal?.GetType().Name}");
      }
    }

    return lhs;
  }

  private object EvalConstMulDiv(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstUnary(decls, evaluated, evaluating);

    while (Check(TokenType.Star) || Check(TokenType.Slash) || Check(TokenType.Mod)) {
      var op = Advance().Type;
      var rhs = EvalConstUnary(decls, evaluated, evaluating);
      (var lVal, var rVal) = PromoteConstPair(lhs, rhs);

      if (lVal is long li && rVal is long ri) {
        if (op == TokenType.Star) lhs = li * ri;
        else if (op == TokenType.Slash) lhs = (double)li / ri;
        else if (op == TokenType.Mod) lhs = li % ri;
        else throw new InvalidOperationException();
      } else if (lVal is double ld && rVal is double rd) {
        if (op == TokenType.Star) lhs = ld * rd;
        else if (op == TokenType.Slash) lhs = ld / rd;
        else if (op == TokenType.Mod) lhs = (long)ld % (long)rd;
        else throw new InvalidOperationException();
      } else {
        throw new InvalidOperationException($"Cannot multiply/divide {lVal?.GetType().Name} and {rVal?.GetType().Name}");
      }
    }

    return lhs;
  }

  private object EvalConstUnary(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    if (Check(TokenType.Minus)) {
      Advance();
      var val = EvalConstPrimary(decls, evaluated, evaluating);
      if (val is long l) return -l;
      if (val is double d) return -d;
      throw new InvalidOperationException($"Cannot negate {val?.GetType().Name}");
    }
    if (Check(TokenType.Not)) {
      Advance();
      var val = EvalConstPrimary(decls, evaluated, evaluating);
      if (val is bool b) return !b;
      throw new InvalidOperationException($"Cannot apply 'not' to {val?.GetType().Name}");
    }
    return EvalConstPrimary(decls, evaluated, evaluating);
  }

  private object EvalConstPrimary(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    if (Check(TokenType.IntegerLiteral)) {
      var token = Advance();
      return ParseIntegerLiteral(token);
    }
    if (Check(TokenType.FloatLiteral)) {
      var token = Advance();
      return ParseFloatLiteral(token);
    }
    if (Check(TokenType.True)) {
      Advance();
      return true;
    }
    if (Check(TokenType.False)) {
      Advance();
      return false;
    }
    if (Check(TokenType.LeftParen)) {
      Advance();
      var val = EvalConstExpr(decls, evaluated, evaluating);
      Expect(TokenType.RightParen);
      return val;
    }
    if (Check(TokenType.Identifier)) {
      var token = Advance();
      // Handle enum constant: EnumType.caseName
      if (Check(TokenType.Dot) && _typeRegistry.TryGetValue(token.Value, out var constType) && constType is MlirEnumType constEnumType) {
        Advance(); // consume '.'
        var caseToken = Expect(TokenType.Identifier);
        var enumCase = constEnumType.GetCase(caseToken.Value)
          ?? throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
            $"unknown enum case: '{caseToken.Value}'", caseToken.Line, caseToken.Column);
        return new EnumConstantValue(token.Value, caseToken.Value, enumCase.Ordinal);
      }
      return EvaluateConstant(token.Value, decls, evaluated, evaluating, token.Line, token.Column);
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected constant expression, got '{Current().Value}'", Current().Line, Current().Column);
  }

  private static (object, object) PromoteConstPair(object lhs, object rhs) {
    if (lhs is long ll && rhs is double rd) return ((double)ll, rd);
    if (lhs is double ld && rhs is long rl) return (ld, (double)rl);
    return (lhs, rhs);
  }

  // ============================================================================
  // Top-level parsing
  // ============================================================================

  private void ParseTopLevel(MlirModule<MaxonOp> module) {
    if (Check(TokenType.Export)) {
      Advance();
    }

    if (Check(TokenType.Function)) {
      ParseFunction(module);
    } else if (Check(TokenType.Type)) {
      ParseTypeDecl(module);
    } else if (Check(TokenType.Enum)) {
      ParseEnumDecl(module);
    } else if (Check(TokenType.Let) || Check(TokenType.Var)) {
      // Already handled in pre-scan; skip over
      SkipTopLevelDecl();
    } else if (Check(TokenType.TypeAlias)) {
      SkipTypeAlias();
    } else if (Check(TokenType.Interface)) {
      SkipInterfaceDecl();
    } else if (Check(TokenType.Extension)) {
      ParseExtensionBlock(module);
    } else {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected function declaration, got '{Current().Value}'", Current().Line, Current().Column);
    }
  }

  private void SkipTopLevelDecl() {
    Advance(); // consume 'let' or 'var'
    Advance(); // consume name
    Expect(TokenType.Equals);
    SkipToEndOfLine();
  }

  private void SkipTypeAlias() {
    Advance(); // consume 'typealias'
    SkipToEndOfLine();
  }

  /// <summary>
  /// Skip an interface declaration during the main parse phase.
  /// Does not use SkipToMatchingEnd because interface method signatures
  /// have no body/end, so the depth counter would be wrong.
  /// </summary>
  private void SkipInterfaceDecl() {
    Advance(); // consume 'interface'
    SkipToEndOfLine(); // skip name and optional uses clause
    SkipNewlines();
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipToEndOfLine();
      SkipNewlines();
    }
    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  private void ParseTypeDecl(MlirModule<MaxonOp> module) {
    Advance(); // consume 'type'
    var nameToken = Expect(TokenType.Identifier);
    var typeName = nameToken.Value;
    _currentTypeName = typeName;
    Logger.Debug(LogCategory.Parser, $"Parsing type: {typeName}");

    var associatedTypeNames = ParseUsesClause();
    ParseConformanceClause();
    SkipNewlines();

    // Type is already fully constructed in _typeRegistry from pre-scan
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      // Handle 'static' keyword for static fields and static methods
      if (Check(TokenType.Static)) {
        Advance(); // consume 'static'

        if (Check(TokenType.Function)) {
          // Static method: parse as a function with qualified name
          ParseStaticMethod(module, typeName);
          SkipNewlines();
          continue;
        }

        ParseStaticField(module, typeName);
        SkipNewlines();
        continue;
      }

      // Handle 'export' keyword
      if (Check(TokenType.Export)) {
        Advance();

        // export function (instance method)
        if (Check(TokenType.Function)) {
          ParseInstanceMethod(module, typeName);
          SkipNewlines();
          continue;
        }

        // export static function/var/let
        if (Check(TokenType.Static)) {
          Advance(); // consume 'static'

          if (Check(TokenType.Function)) {
            ParseStaticMethod(module, typeName);
            SkipNewlines();
            continue;
          }

          ParseStaticField(module, typeName);
          SkipNewlines();
          continue;
        }
      }

      if (Check(TokenType.Var) || Check(TokenType.Let)) {
        Advance(); // consume var/let
        Expect(TokenType.Identifier); // consume field name

        // Advance past type or default value
        if (Check(TokenType.Equals)) {
          Advance();
          ParseFieldDefault();
        } else {
          ParseTypeRef();
        }

        SkipNewlines();
        continue;
      }

      if (Check(TokenType.Function)) {
        ParseInstanceMethod(module, typeName);
        SkipNewlines();
        continue;
      }

      // Internal typealias declaration - already handled in prescan, skip
      if (Check(TokenType.TypeAlias)) {
        SkipTypeAlias();
        SkipNewlines();
        continue;
      }

      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected 'var' or 'let' in field declaration, got '{Current().Value}'", Current().Line, Current().Column);
    }

    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    ExpectEndLabel(typeName);
  }

  private void ParseEnumDecl(MlirModule<MaxonOp> module) {
    Advance(); // consume 'enum'
    var nameToken = Expect(TokenType.Identifier);
    var enumName = nameToken.Value;
    _currentTypeName = enumName;
    Logger.Debug(LogCategory.Parser, $"Parsing enum: {enumName}");

    // Skip conformance clause (already captured during pre-scan)
    ParseConformanceClause();

    SkipNewlines();

    var enumType = (MlirEnumType)_typeRegistry[enumName];

    // Skip cases (already pre-scanned)
    while (!Check(TokenType.End) && !Check(TokenType.Function) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End) || Check(TokenType.Function)) break;
      SkipToEndOfLine();
      SkipNewlines();
    }

    // Parse instance methods
    while (Check(TokenType.Function) && !IsAtEnd()) {
      ParseEnumInstanceMethod(module, enumName, enumType);
      SkipNewlines();
    }

    _currentTypeName = null;
    ExpectEndLabel(enumName);
  }

  private void ParseEnumInstanceMethod(MlirModule<MaxonOp> module, string enumName, MlirEnumType enumType) {
    Expect(TokenType.Function);
    var nameToken = Expect(TokenType.Identifier);
    var methodName = $"{enumName}.{nameToken.Value}";
    Logger.Debug(LogCategory.Parser, $"Parsing enum instance method: {methodName}");

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    SkipNewlines();

    // Instance method gets 'self' as first parameter (the enum type)
    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { (MlirType)enumType };
    allParamTypes.AddRange(paramTypes);

    // Store defaults (offset by 1 for 'self' parameter)
    var offsetDefaults = new Dictionary<int, MlirAttribute>();
    foreach (var (idx, attr) in paramDefaults) {
      offsetDefaults[idx + 1] = attr;
    }

    SetupFunctionParsing(module, methodName, allParamNames, allParamTypes, offsetDefaults, returnType, throwsType);

    // Emit self param as enum and register it
    var backingKind = GetEnumBackingKind(enumType);
    var selfParamOp = new MaxonEnumParamOp(0, "self", enumName, backingKind);
    _currentBlock!.AddOp(selfParamOp);
    _variables["self"] = new VarInfo(MaxonValueKind.Enum, false, selfParamOp.Result, _currentBlock!, enumName);

    // Emit remaining params (offset by 1 for 'self')
    EmitParameters(paramNames, paramTypes, paramTokens, paramOffset: 1);

    ParseBodyUntilEnd();
    ExpectEndLabel(nameToken.Value);
    FinishFunctionBody(nameToken.Value, nameToken, returnType);
  }

  private static MaxonValueKind GetEnumBackingKind(MlirEnumType enumType) {
    if (enumType.BackingType == MlirType.F64) return MaxonValueKind.Float;
    if (enumType.BackingType is MlirStringBackingType) return MaxonValueKind.Integer;
    if (enumType.BackingType is MlirCharBackingType) return MaxonValueKind.Integer;
    if (enumType.BackingType == MlirType.I64 || enumType.BackingType == null) return MaxonValueKind.Integer;
    throw new InvalidOperationException($"Unsupported enum backing type: {enumType.BackingType}");
  }

  /// <summary>
  /// Parse EnumType.fromRawValue(arg) or EnumType.fromName(nameArg, ...associatedArgs).
  /// Emits a MaxonCallOp with a synthetic callee name for lowering.
  /// </summary>
  private ExprResult.Direct ParseEnumStaticMethod(MlirEnumType enumType, string methodName) {
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Expect(TokenType.LeftParen);

    if (methodName == "fromRawValue") {
      return ParseEnumFromRawValue(enumType, methodToken);
    } else if (methodName == "fromName") {
      return ParseEnumFromName(enumType, methodToken);
    }
    throw new InvalidOperationException($"Unknown enum static method: '{methodName}'");
  }

  private ExprResult.Direct ParseEnumFromRawValue(MlirEnumType enumType, Token methodToken) {
    // fromRawValue is not available for enums with associated values
    if (enumType.HasAssociatedValues) {
      throw new CompileError(ErrorCode.SemanticUndefinedVariable,
        $"fromRawValue is not available for enums with associated values: '{enumType.Name}'",
        methodToken.Line, methodToken.Column);
    }

    var argExpr = ParseExpression();
    var argVal = ResolveExprValue(argExpr);
    Expect(TokenType.RightParen);

    // Determine expected argument type based on enum backing type
    var argKind = DetermineValueKind(argVal);
    MaxonValueKind expectedKind;
    if (enumType.BackingType == MlirType.F64) {
      expectedKind = MaxonValueKind.Float;
    } else if (enumType.BackingType is MlirStringBackingType) {
      expectedKind = MaxonValueKind.Struct; // String is a struct
    } else if (enumType.BackingType is MlirCharBackingType) {
      expectedKind = MaxonValueKind.Struct; // Character is a struct
    } else {
      expectedKind = MaxonValueKind.Integer; // simple or int-backed
    }

    // Type check
    if (argKind != expectedKind) {
      var actualTypeName = argVal is MaxonStruct ms
        ? ms.TypeName
        : MlirType.FormatAsSourceName(argKind.ToMlirType());
      string expectedTypeName;
      if (enumType.BackingType == MlirType.F64) expectedTypeName = "float";
      else if (enumType.BackingType is MlirStringBackingType) expectedTypeName = "String";
      else if (enumType.BackingType is MlirCharBackingType) expectedTypeName = "Character";
      else expectedTypeName = "int";
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"type mismatch: 'expected {expectedTypeName}, got {actualTypeName}'",
        methodToken.Line, methodToken.Column);
    }

    // Compile-time validation for literal values
    ValidateFromRawValueLiteral(enumType, methodToken);

    // Emit a MaxonCallOp with synthetic callee for lowering
    var callOp = new MaxonCallOp(
      $"__enum_fromRawValue:{enumType.Name}",
      [argVal],
      MaxonValueKind.Enum,
      enumType.Name);
    _currentBlock!.AddOp(callOp);
    return new ExprResult.Direct(callOp.Result!);
  }

  private void ValidateFromRawValueLiteral(MlirEnumType enumType, Token methodToken) {
    // Check if the argument is a compile-time literal
    var lastOps = _currentBlock!.Operations;
    if (lastOps.Count < 1) return;

    // The call op hasn't been emitted yet, so the literal is the last op
    var prevOp = lastOps[^1];
    if (prevOp is not MaxonLiteralOp litOp) return;

    // For simple/int-backed enums, check if literal matches any case's raw value
    if (enumType.BackingType == null) {
      // Simple enum: raw value is ordinal
      var ordinal = litOp.IntValue;
      if (ordinal < 0 || ordinal >= enumType.Cases.Count) {
        throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
          $"no enum case with raw value '{ordinal}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);
      }
    } else if (enumType.BackingType == MlirType.I64) {
      var intVal = litOp.IntValue;
      if (!enumType.Cases.Any(c => c.RawValue is long rv && rv == intVal)) {
        throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
          $"no enum case with raw value '{intVal}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);
      }
    } else if (enumType.BackingType == MlirType.F64) {
      var floatVal = litOp.FloatValue;
      if (!enumType.Cases.Any(c => c.RawValue is double rv && rv == floatVal)) {
        throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
          $"no enum case with raw value '{floatVal}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);
      }
    }
    // String/char-backed enums: literal arg would be a string/char literal, not MaxonLiteralOp
    // Those are handled by MaxonStringLiteralOp/MaxonCharLiteralOp
  }

  private ExprResult.Direct ParseEnumFromName(MlirEnumType enumType, Token methodToken) {
    var nameExpr = ParseExpression();
    var nameVal = ResolveExprValue(nameExpr);

    // First arg must be a String
    var nameKind = DetermineValueKind(nameVal);
    if (nameKind != MaxonValueKind.Struct || nameVal is not MaxonStruct nameStruct || nameStruct.TypeName != "String") {
      var actualTypeName = nameVal is MaxonStruct ms
        ? ms.TypeName
        : MlirType.FormatAsSourceName(nameKind.ToMlirType());
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"type mismatch: 'expected String, got {actualTypeName}'",
        methodToken.Line, methodToken.Column);
    }

    // Check if name is a compile-time string literal
    bool isLiteral = false;
    string? literalName = null;
    var lastOps = _currentBlock!.Operations;
    // Walk backwards to find the string literal that produced this value
    for (int i = lastOps.Count - 1; i >= 0; i--) {
      if (lastOps[i] is MaxonStringLiteralOp strLitOp && strLitOp.Result.Id == nameVal.Id) {
        isLiteral = true;
        literalName = strLitOp.Value;
        break;
      }
    }

    var args = new List<MaxonValue> { nameVal };

    if (isLiteral && literalName != null) {
      // Validate the literal name against known cases
      var matchedCase = enumType.GetCase(literalName) ?? throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
          $"no enum case named '{literalName}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);

      // For associated-value enums with a literal name, parse associated value args
      if (enumType.HasAssociatedValues && matchedCase.AssociatedValues is { Count: > 0 }) {
        // Expect additional args after the name
        if (!Check(TokenType.Comma)) {
          throw new CompileError(ErrorCode.SemanticWrongArgCount,
            $"wrong argument count: 'case '{literalName}' requires {matchedCase.AssociatedValues.Count} associated value(s)'",
            methodToken.Line, methodToken.Column);
        }
        for (int ai = 0; ai < matchedCase.AssociatedValues.Count; ai++) {
          Expect(TokenType.Comma);
          var avExpr = ParseExpression();
          var avVal = ResolveExprValue(avExpr);
          // Type-check
          var expectedType = matchedCase.AssociatedValues[ai].Type;
          var actualKind = DetermineValueKind(avVal);
          var expectedAVKind = expectedType.ToValueKind();
          if (actualKind != expectedAVKind) {
            var actualTypeName = avVal is MaxonStruct ms2
              ? ms2.TypeName
              : MlirType.FormatAsSourceName(actualKind.ToMlirType());
            throw new CompileError(ErrorCode.SemanticTypeMismatch,
              $"type mismatch: 'expected {MlirType.FormatAsSourceName(expectedType)}, got {actualTypeName}'",
              methodToken.Line, methodToken.Column);
          }
          args.Add(avVal);
        }
      }
    }

    Expect(TokenType.RightParen);

    // Emit a MaxonCallOp with synthetic callee for lowering
    var callOp = new MaxonCallOp(
      $"__enum_fromName:{enumType.Name}",
      args,
      MaxonValueKind.Enum,
      enumType.Name);
    _currentBlock!.AddOp(callOp);
    return new ExprResult.Direct(callOp.Result!);
  }

  private void ParseStaticField(MlirModule<MaxonOp> module, string typeName) {
    bool isMutable;
    if (Check(TokenType.Var)) { Advance(); isMutable = true; } else if (Check(TokenType.Let)) { Advance(); isMutable = false; } else {
      throw new CompileError(ErrorCode.ParserUnexpectedToken,
        $"Expected 'var', 'let', or 'function' after 'static', got '{Current().Value}'",
        Current().Line, Current().Column);
    }

    var fieldName = Expect(TokenType.Identifier).Value;
    Expect(TokenType.Equals);
    var (fieldType, defaultValue) = ParseFieldDefault();
    var qualifiedName = $"{typeName}.{fieldName}";

    if (isMutable) {
      module.Globals.Add(new MlirGlobal(qualifiedName, fieldType, defaultValue));
      _globalVars[qualifiedName] = new GlobalVarInfo(fieldType.ToValueKind(), true);
    } else {
      RegisterStaticLetConstant(qualifiedName, fieldType, defaultValue);
    }
  }

  private void RegisterStaticLetConstant(string name, MlirType type, MlirAttribute? value) {
    if (value is IntegerAttr intAttr && type == MlirType.I1) {
      _topLevelConstants[name] = intAttr.Value != 0;
    } else if (value is IntegerAttr intAttr2) {
      _topLevelConstants[name] = intAttr2.Value;
    } else if (value is FloatAttr floatAttr) {
      _topLevelConstants[name] = floatAttr.Value;
    }
  }

  private void ParseStaticMethod(MlirModule<MaxonOp> module, string typeName) {
    Expect(TokenType.Function);
    var nameToken = Expect(TokenType.Identifier);

    // Construct qualified method name: namespace.TypeName.methodName
    // Don't include filename in namespace since typeName is already the type
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";
    var methodName = $"{qualifiedTypeName}.{nameToken.Value}";
    Logger.Debug(LogCategory.Parser, $"Parsing static method: {methodName}");

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    SkipNewlines();
    SetupFunctionParsing(module, methodName, paramNames, paramTypes, paramDefaults, returnType, throwsType);
    EmitParameters(paramNames, paramTypes, paramTokens);

    ParseBodyUntilEnd();
    ExpectEndLabel(nameToken.Value);
    FinishFunctionBody(nameToken.Value, nameToken, returnType);
  }

  private void ParseInstanceMethod(MlirModule<MaxonOp> module, string typeName) {
    Expect(TokenType.Function);
    var nameToken = Expect(TokenType.Identifier);

    // Construct qualified method name: namespace.TypeName.methodName
    // Don't include filename in namespace since typeName is already the type
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";
    var methodName = $"{qualifiedTypeName}.{nameToken.Value}";
    Logger.Debug(LogCategory.Parser, $"Parsing instance method: {methodName}");

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    SkipNewlines();

    // Instance method gets 'self' as first parameter (the struct type)
    var structType = (MlirStructType)_typeRegistry[typeName];

    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { structType };
    allParamTypes.AddRange(paramTypes);

    // Store defaults (offset by 1 for 'self' parameter)
    var offsetDefaults = new Dictionary<int, MlirAttribute>();
    foreach (var (idx, attr) in paramDefaults) {
      offsetDefaults[idx + 1] = attr;
    }

    SetupFunctionParsing(module, methodName, allParamNames, allParamTypes, offsetDefaults, returnType, throwsType);

    // Emit self param (struct) and register fields as accessible variables
    var selfParamOp = new MaxonStructParamOp(0, "self", typeName);
    _currentBlock!.AddOp(selfParamOp);
    _variables["self"] = new VarInfo(MaxonValueKind.Struct, false, selfParamOp.Result, _currentBlock!, typeName);

    // Register all fields of 'self' as directly accessible variables
    foreach (var field in structType.Fields) {
      var fieldKind = field.Type.ToValueKind();
      var fieldStructName = field.Type is MlirStructType fst ? fst.Name : null;
      var fieldAccessOp = new MaxonFieldAccessOp(selfParamOp.Result, typeName, field.Name, fieldKind, fieldStructName);
      _currentBlock!.AddOp(fieldAccessOp);
      _variables[field.Name] = new VarInfo(fieldKind, field.IsMutable, fieldAccessOp.Result, _currentBlock!, fieldStructName);
    }

    // Emit remaining params (offset by 1 for 'self')
    EmitParameters(paramNames, paramTypes, paramTokens, paramOffset: 1);

    ParseBodyUntilEnd();
    ExpectEndLabel(nameToken.Value);
    FinishFunctionBody(nameToken.Value, nameToken, returnType);
  }

  /// <summary>
  /// Common setup for parsing a function body: replaces pre-registered stub, clears state,
  /// creates entry block, and stores defaults. Returns the created function.
  /// </summary>
  private MlirFunction<MaxonOp> SetupFunctionParsing(
      MlirModule<MaxonOp> module, string funcName,
      List<string> paramNames, List<MlirType> paramTypes,
      Dictionary<int, MlirAttribute> paramDefaults, MlirType? returnType, MlirType? throwsType = null) {
    // Determine the registration name (may be mangled for overloads)
    var registrationName = funcName;
    // Check if this function has been mangled (overload exists)
    var mangledName = MangleOverloadName(funcName, paramNames);
    if (module.Functions.Any(f => f.Name == mangledName)) {
      registrationName = mangledName;
    }

    // Replace pre-registered stub with full function (or create new one)
    var existing = module.Functions.FirstOrDefault(f => f.Name == registrationName);
    if (existing != null) {
      module.Functions.Remove(existing);
    }
    var func = new MlirFunction<MaxonOp>(registrationName, paramNames, paramTypes, returnType, throwsType);
    module.AddFunction(func);

    // Store defaults
    if (paramDefaults.Count > 0) {
      _functionDefaults[registrationName] = paramDefaults;
    }

    _currentFunction = func;
    _currentBlock = func.Body.AddBlock("entry");
    _variables.Clear();
    _referencedVars.Clear();
    _paramLocations.Clear();
    _blockCounter = 0;

    return func;
  }

  /// <summary>
  /// Emits parameter ops and registers them as variables.
  /// paramOffset is the index offset for the MLIR param op (e.g., 1 for instance methods with 'self').
  /// </summary>
  private void EmitParameters(List<string> paramNames, List<MlirType> paramTypes, List<Token> paramTokens, int paramOffset = 0) {
    for (int i = 0; i < paramNames.Count; i++) {
      if (!paramNames[i].StartsWith('_')) {
        _paramLocations.Add((paramNames[i], paramTokens[i].Line, paramTokens[i].Column));
      }
      var paramType = paramTypes[i];
      if (paramType is MlirStructType structType) {
        var structParamOp = new MaxonStructParamOp(i + paramOffset, paramNames[i], structType.Name);
        _currentBlock!.AddOp(structParamOp);
        _variables[paramNames[i]] = new VarInfo(MaxonValueKind.Struct, false, structParamOp.Result, _currentBlock!, structType.Name);
      } else if (paramType is MlirEnumType enumType) {
        var backingKind = GetEnumBackingKind(enumType);
        var enumParamOp = new MaxonEnumParamOp(i + paramOffset, paramNames[i], enumType.Name, backingKind);
        _currentBlock!.AddOp(enumParamOp);
        _variables[paramNames[i]] = new VarInfo(MaxonValueKind.Enum, false, enumParamOp.Result, _currentBlock!, enumType.Name);
      } else if (paramType is MlirFunctionType fnType) {
        var fnParamOp = new MaxonFunctionParamOp(i + paramOffset, paramNames[i], fnType);
        _currentBlock!.AddOp(fnParamOp);
        _variables[paramNames[i]] = new VarInfo(MaxonValueKind.Function, false, fnParamOp.Result, _currentBlock!, FnTypeName: fnType);
      } else if (paramType is MlirTypeParameterType tp) {
        var paramOp = new MaxonParamOp(i + paramOffset, paramNames[i], MaxonValueKind.TypeParameter);
        _currentBlock!.AddOp(paramOp);
        _variables[paramNames[i]] = new VarInfo(MaxonValueKind.TypeParameter, false, paramOp.Result, _currentBlock!, tp.ParameterName);
      } else {
        var kind = paramType.ToValueKind();
        var paramOp = new MaxonParamOp(i + paramOffset, paramNames[i], kind);
        _currentBlock!.AddOp(paramOp);
        _variables[paramNames[i]] = new VarInfo(kind, false, paramOp.Result, _currentBlock!);
      }
    }
  }

  private void FinishFunctionBody(string name, Token nameToken, MlirType? returnType) {
    // E014: Check for unused parameters
    foreach (var (paramName, paramLine, paramCol) in _paramLocations) {
      if (!_referencedVars.Contains(paramName)) {
        throw new CompileError(ErrorCode.SemanticUnusedVariable, $"unused variable: '{paramName}'", paramLine, paramCol);
      }
    }

    // E037: Check for missing return statements
    if (returnType != null && _currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      throw new CompileError(ErrorCode.SemanticMissingReturn, $"missing return statement: '{name}'", nameToken.Line, nameToken.Column);
    }

    if (returnType == null && _currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonReturnOp());
    }

    _currentFunction = null;
    _currentBlock = null;
  }

  private (MlirType type, MlirAttribute defaultValue) ParseFieldDefault() {
    if (Check(TokenType.Minus)) {
      Advance(); // consume '-'
      if (Check(TokenType.IntegerLiteral)) {
        var val = -ParseIntegerLiteral(Advance());
        return (MlirType.I64, new IntegerAttr(val, MlirType.I64));
      }
      if (Check(TokenType.FloatLiteral)) {
        var val = -ParseFloatLiteral(Advance());
        return (MlirType.F64, new FloatAttr(val, MlirType.F64));
      }
      throw new CompileError(ErrorCode.ParserExpectedExpression, "Expected number after '-' in default value", Current().Line, Current().Column);
    }
    if (Check(TokenType.IntegerLiteral)) {
      var val = ParseIntegerLiteral(Advance());
      return (MlirType.I64, new IntegerAttr(val, MlirType.I64));
    }
    if (Check(TokenType.FloatLiteral)) {
      var val = ParseFloatLiteral(Advance());
      return (MlirType.F64, new FloatAttr(val, MlirType.F64));
    }
    if (Check(TokenType.True)) {
      Advance();
      return (MlirType.I1, new IntegerAttr(1, MlirType.I1));
    }
    if (Check(TokenType.False)) {
      Advance();
      return (MlirType.I1, new IntegerAttr(0, MlirType.I1));
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression, "Expected default value", Current().Line, Current().Column);
  }

  private void ParseFunction(MlirModule<MaxonOp> module) {
    Expect(TokenType.Function);
    var nameToken = Expect(TokenType.Identifier);
    var baseName = nameToken.Value;

    // Top-level functions get qualified with file-based namespace
    var namespace_ = DeriveNamespace();
    var name = string.IsNullOrEmpty(namespace_) ? baseName : $"{namespace_}.{baseName}";

    Logger.Debug(LogCategory.Parser, $"Parsing function: {name} (base: {baseName}, namespace: {namespace_})");

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    SkipNewlines();
    var func = SetupFunctionParsing(module, name, paramNames, paramTypes, paramDefaults, returnType, throwsType);
    func.SourceLine = nameToken.Line;
    func.SourceColumn = nameToken.Column;
    EmitParameters(paramNames, paramTypes, paramTokens);

    // Emit deferred top-level array lets at start of main
    if (baseName == "main") {
      EmitDeferredArrayLets();
    }

    ParseBodyUntilEnd();
    ExpectEndLabel(baseName);
    FinishFunctionBody(baseName, nameToken, returnType);
  }

  // ============================================================================
  // Parameter and type parsing
  // ============================================================================

  private (List<string> Names, List<MlirType> Types, Dictionary<int, MlirAttribute> Defaults, List<Token> ParamTokens) ParseParamListWithDefaults() {
    var names = new List<string>();
    var types = new List<MlirType>();
    var defaults = new Dictionary<int, MlirAttribute>();
    var paramTokens = new List<Token>();
    if (!Check(TokenType.RightParen)) {
      int paramIndex = 0;
      do {
        var paramToken = Expect(TokenType.Identifier);
        var paramType = ParseTypeRef();
        if (Check(TokenType.Equals)) {
          Advance(); // consume '='
          var (_, defaultValue) = ParseFieldDefault();
          defaults[paramIndex] = defaultValue;
        }
        names.Add(paramToken.Value);
        types.Add(paramType);
        paramTokens.Add(paramToken);
        paramIndex++;
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    Expect(TokenType.RightParen);
    return (names, types, defaults, paramTokens);
  }

  private MlirType ParseTypeRef() {
    // Function type: (ParamType, ...) returns ReturnType
    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      var paramTypes = new List<MlirType>();
      while (!Check(TokenType.RightParen) && !IsAtEnd()) {
        // Check if this is a named parameter (name Type) or just a type
        if (Check(TokenType.Identifier) && PeekNext().Type != TokenType.Comma && PeekNext().Type != TokenType.RightParen) {
          // This looks like "name Type" - skip the name
          Advance(); // consume name
        }
        paramTypes.Add(ParseTypeRef());
        if (Check(TokenType.Comma)) Advance();
      }
      Expect(TokenType.RightParen);
      MlirType? returnType = null;
      if (Check(TokenType.Returns)) {
        Advance();
        returnType = ParseTypeRef();
      }
      return new MlirFunctionType(paramTypes, returnType);
    }

    var typeName = ExpectTypeName();
    return typeName switch {
      "int" => MlirType.I64,
      "float" => MlirType.F64,
      "bool" => MlirType.I1,
      "byte" => MlirType.I8,
      "cstring" => MlirType.I64, // raw pointer type for FFI
      _ => _typeRegistry.TryGetValue(typeName, out var structType)
        ? structType
        : throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown type: {typeName}", Current().Line, Current().Column)
    };
  }

  private string ExpectTypeName() {
    if (Check(TokenType.Int)) return Advance().Value;
    if (Check(TokenType.Float)) return Advance().Value;
    if (Check(TokenType.Bool)) return Advance().Value;
    if (Check(TokenType.Byte)) return Advance().Value;
    if (Check(TokenType.SelfType)) {
      Advance();
      if (_currentTypeName == null) throw new CompileError(ErrorCode.ParserExpectedType, "'Self' can only be used inside a type declaration", Current().Line, Current().Column);
      return _currentTypeName;
    }
    if (Check(TokenType.Identifier)) return Advance().Value;
    throw new CompileError(ErrorCode.ParserExpectedType, "Expected type name", Current().Line, Current().Column);
  }

  private MaxonValueKind ParseTypeKeyword() {
    if (Check(TokenType.Int)) { Advance(); return MaxonValueKind.Integer; }
    if (Check(TokenType.Float)) { Advance(); return MaxonValueKind.Float; }
    if (Check(TokenType.Bool)) { Advance(); return MaxonValueKind.Bool; }
    if (Check(TokenType.Byte)) { Advance(); return MaxonValueKind.Byte; }
    throw new CompileError(ErrorCode.ParserExpectedType, "Expected type name after 'as'", Current().Line, Current().Column);
  }

  // ============================================================================
  // Statement parsing
  // ============================================================================

  private void ParseBodyUntilEnd() {
    bool wasDeadCode = false;
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;
      // After exhaustive match/if-else where all branches terminate, _currentBlock
      // becomes null. Create a dead block so subsequent unreachable code can still
      // be parsed for syntax validation without crashing.
      if (_currentBlock == null) {
        wasDeadCode = true;
        var deadLabel = $"__dead_{_blockCounter++}";
        _currentBlock = _currentFunction!.Body.AddBlock(deadLabel);
      }
      ParseStatement();
      SkipNewlines();
    }
    if (wasDeadCode) {
      _currentBlock = null;
    }
  }

  private void ParseStatement() {
    if (Check(TokenType.Return)) {
      ParseReturn();
    } else if (Check(TokenType.Var)) {
      ParseVarDecl();
    } else if (Check(TokenType.Let)) {
      ParseLetDecl();
    } else if (Check(TokenType.If)) {
      ParseIf();
    } else if (Check(TokenType.While)) {
      ParseWhile();
    } else if (Check(TokenType.For)) {
      ParseForIn();
    } else if (Check(TokenType.Break)) {
      ParseBreak();
    } else if (Check(TokenType.Continue)) {
      ParseContinue();
    } else if (TryRewritePrimitiveStaticMethod()) {
      ParseCallStatement();
    } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Dot) {
      // Could be: field assignment (p.x = 30), qualified name assignment (Counter.count = 42),
      // or qualified name call (Counter.increment())
      ParseDotStatement();
    } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Equals) {
      // Simple assignment or global assignment
      ParseAssignment();
    } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.LeftParen) {
      ParseCallStatement();
    } else if (Check(TokenType.Self) && PeekNext().Type == TokenType.Dot) {
      ParseSelfDotStatement();
    } else if (Check(TokenType.Match)) {
      ParseMatch();
    } else if (Check(TokenType.Throw)) {
      ParseThrow();
    } else if (Check(TokenType.Try)) {
      ParseTryStatement();
    } else {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, $"unexpected token: '{Current().Value}'", Current().Line, Current().Column);
    }
  }

  private void ParseDotStatement() {
    var nameToken = _tokens[_pos];
    var dotToken = _tokens[_pos + 1];
    // Look ahead further to determine what follows: identifier.identifier = ... or identifier.identifier(...)
    if (_pos + 2 < _tokens.Count && _tokens[_pos + 2].Type == TokenType.Identifier) {
      var afterIdent = _pos + 3 < _tokens.Count ? _tokens[_pos + 3] : new Token(TokenType.Eof, "", 0, 0);

      // Check if it's a qualified name (Type.member)
      var qualifiedName = $"{nameToken.Value}.{_tokens[_pos + 2].Value}";

      if (afterIdent.Type == TokenType.Equals) {
        // Could be either Type.field = value or instance.field = value
        if (_globalVars.ContainsKey(qualifiedName)) {
          // Global/static variable assignment: Type.field = value
          ParseQualifiedAssignment();
          return;
        }
        // Otherwise it's an instance field assignment: p.x = 30
        ParseFieldAssignment();
        return;
      }

      if (afterIdent.Type == TokenType.Dot) {
        // Nested field access chain: o.inner.x = ... or o.inner.method(...)
        // Scan ahead to find what terminates the chain
        if (IsNestedFieldChainEndingWith(TokenType.Equals)) {
          ParseFieldAssignment();
          return;
        }
        if (IsNestedFieldChainEndingWith(TokenType.LeftParen)) {
          ParseNestedFieldMethodCall();
          return;
        }
      }

      if (afterIdent.Type == TokenType.LeftParen) {
        // Check for static/qualified function call: Type.method(...)
        var suffixPattern = $".{qualifiedName}";
        if (_currentModule!.Functions.Any(f => f.Name == qualifiedName
            || f.Name.EndsWith(suffixPattern)
            || f.Name.StartsWith(qualifiedName + "$")
            || f.Name.Contains(suffixPattern + "$"))) {
          ParseQualifiedCallStatement();
          return;
        }

        // Check for instance method call: var.method(...)
        if (_variables.TryGetValue(nameToken.Value, out var varInfo) && varInfo.StructTypeName != null) {
          var instanceMethodName = $"{varInfo.StructTypeName}.{_tokens[_pos + 2].Value}";
          var resolvedName = ResolveMethodName(instanceMethodName);
          if (resolvedName != null) {
            ParseInstanceMethodCallStatement(varInfo, resolvedName);
            return;
          }
        }
      }
    }

    // Fall through to field assignment
    ParseFieldAssignment();
  }

  /// Validates field exists and is accessible, emits MaxonFieldAccessOp, returns updated current value and struct type name.
  private (MaxonValue value, string structTypeName) EmitIntermediateFieldAccess(
      MaxonValue currentValue, string currentStructTypeName, Token fieldToken) {
    var structType = (MlirStructType)_typeRegistry[currentStructTypeName];
    var field = structType.GetField(fieldToken.Value)
      ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
        $"Type '{structType.Name}' has no field named '{fieldToken.Value}'",
        fieldToken.Line, fieldToken.Column);

    if (!field.IsExported && _currentTypeName != structType.Name) {
      throw new CompileError(ErrorCode.SemanticUnexportedFieldAccess,
        $"cannot access unexported field: '{fieldToken.Value}' outside of type '{structType.Name}'",
        fieldToken.Line, fieldToken.Column);
    }

    var fieldKind = field.Type.ToValueKind();
    var fieldStructName = field.Type is MlirStructType fst ? fst.Name : null;
    var accessOp = new MaxonFieldAccessOp(currentValue, currentStructTypeName, fieldToken.Value, fieldKind, fieldStructName);
    _currentBlock!.AddOp(accessOp);
    var newStructTypeName = fieldStructName
      ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
        $"Cannot access nested field on non-struct field '{fieldToken.Value}'",
        fieldToken.Line, fieldToken.Column);
    return (accessOp.Result, newStructTypeName);
  }

  private bool IsNestedFieldChainEndingWith(TokenType terminator) {
    // Scan from _pos forward through identifier.identifier.identifier... pattern
    // Returns true if the chain ends with the specified terminator token
    var i = _pos;
    while (i < _tokens.Count) {
      if (_tokens[i].Type != TokenType.Identifier) return false;
      i++;
      if (i >= _tokens.Count) return false;
      if (_tokens[i].Type == terminator) return true;
      if (_tokens[i].Type != TokenType.Dot) return false;
      i++;
    }
    return false;
  }

  private void ParseNestedFieldMethodCall() {
    // Handles: o.inner.field.method(args)
    // Walk the chain of field accesses until we reach the final identifier followed by '('
    var nameToken = Advance(); // consume root variable name
    if (!_variables.TryGetValue(nameToken.Value, out var varInfo) || varInfo.StructTypeName == null) {
      throw new CompileError(ErrorCode.SemanticUndefinedVariable,
        $"Undefined variable '{nameToken.Value}'",
        nameToken.Line, nameToken.Column);
    }

    var currentStructTypeName = varInfo.StructTypeName;
    var currentValue = ResolveExprValue(new ExprResult.VarRef(nameToken.Value, varInfo));

    // Walk intermediate field accesses: consume '.fieldName' pairs
    // Stop when the next segment is followed by '(' (method call)
    while (Check(TokenType.Dot)) {
      Advance(); // consume '.'
      var fieldToken = Expect(TokenType.Identifier);

      if (Check(TokenType.LeftParen)) {
        // This is the method call at the end of the chain
        var methodName = $"{currentStructTypeName}.{fieldToken.Value}";
        var resolvedName = ResolveMethodName(methodName)
          ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Undefined method '{fieldToken.Value}' on type '{currentStructTypeName}'",
            fieldToken.Line, fieldToken.Column);
        Advance(); // consume '('
        var qualifiedToken = new Token(TokenType.Identifier, resolvedName, fieldToken.Line, fieldToken.Column);
        var (args, callee) = ParseInstanceMethodCallArgs(qualifiedToken, currentValue);
        CreateFunctionCall(qualifiedToken, args, callee);
        return;
      }

      // Intermediate field access
      (currentValue, currentStructTypeName) = EmitIntermediateFieldAccess(currentValue, currentStructTypeName, fieldToken);
    }

    throw new CompileError(ErrorCode.ParserExpectedExpression,
      "Expected method call at end of field access chain",
      Current().Line, Current().Column);
  }

  private void ParseSelfDotStatement() {
    var selfToken = Advance(); // consume 'self'
    Advance(); // consume '.'
    var fieldToken = Expect(TokenType.Identifier);

    if (!_variables.TryGetValue("self", out var selfInfo)) {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, "'self' can only be used inside instance methods", selfToken.Line, selfToken.Column);
    }

    if (Check(TokenType.Equals)) {
      // self.field = value
      Advance(); // consume '='
      var selfVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      EmitFieldAssignment(selfInfo.StructTypeName!, selfVal, fieldToken, selfToken);
    } else if (Check(TokenType.LeftParen)) {
      // self.method(...)
      var methodName = $"{selfInfo.StructTypeName}.{fieldToken.Value}";
      var resolvedName = ResolveMethodName(methodName)
        ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined method '{fieldToken.Value}' on type '{selfInfo.StructTypeName}'", fieldToken.Line, fieldToken.Column);
      Advance(); // consume '('
      var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      var qualifiedToken = new Token(TokenType.Identifier, resolvedName, selfToken.Line, selfToken.Column);
      var (allArgs, selfCallee) = ParseInstanceMethodCallArgs(qualifiedToken, structVal);
      CreateFunctionCall(qualifiedToken, allArgs, selfCallee);
    } else {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, $"Expected '=' or '(' after 'self.{fieldToken.Value}'", fieldToken.Line, fieldToken.Column);
    }
  }

  private void ParseReturn() {
    var returnToken = Current();
    Advance(); // consume 'return'

    if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
      // Check for anonymous struct literal: return { field: value, ... }
      if (Check(TokenType.LeftBrace) && _currentFunction?.ReturnType is MlirStructType retStructType) {
        var structLiteral = ParseStructLiteral(retStructType.Name);
        var value = ResolveExprValue(structLiteral);
        _currentBlock!.AddOp(new MaxonReturnOp(value));
      } else {
        var value = ResolveExprValue(ParseExpression());
        CheckReturnType(value, returnToken);
        _currentBlock!.AddOp(new MaxonReturnOp(value));
      }
    } else {
      _currentBlock!.AddOp(new MaxonReturnOp());
    }
  }

  private void CheckReturnType(MaxonValue value, Token returnToken) {
    var returnType = _currentFunction?.ReturnType;
    if (returnType == null) return;

    // Skip type checking for type parameter return types (e.g., Element in generic types)
    if (returnType is MlirTypeParameterType) return;

    if (returnType is MlirStructType expectedStruct) {
      if (value is not MaxonStruct actualStruct || actualStruct.TypeName != expectedStruct.Name) {
        var actualName = value is MaxonStruct s ? s.TypeName : value.GetType().Name.Replace("Maxon", "").ToLower();
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot return '{actualName}' from function declared to return '{expectedStruct.Name}'",
          returnToken.Line, returnToken.Column);
      }
      return;
    }

    if (returnType is MlirEnumType expectedEnum) {
      if (value is not MaxonEnum actualEnum || actualEnum.TypeName != expectedEnum.Name) {
        var actualName = value is MaxonEnum e ? e.TypeName : value.GetType().Name.Replace("Maxon", "").ToLower();
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot return '{actualName}' from function declared to return '{expectedEnum.Name}'",
          returnToken.Line, returnToken.Column);
      }
      return;
    }

    var valueKind = DetermineValueKind(value);

    var expectedKind = returnType.ToValueKind();
    if (valueKind == expectedKind || IsWideningCast(valueKind, expectedKind))
      return;

    throw new CompileError(ErrorCode.SemanticTypeMismatch,
      $"Cannot return '{KindDisplayName(valueKind)}' from function declared to return '{KindDisplayName(expectedKind)}'",
      returnToken.Line, returnToken.Column);
  }

  private void ParseThrow() {
    var throwToken = Advance(); // consume 'throw'
    var expr = ParseExpression();
    var errorValue = ResolveExprValue(expr);
    if (errorValue is not MaxonEnum enumVal) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch, "throw requires an error enum value", throwToken.Line, throwToken.Column);
    }
    _currentBlock!.AddOp(new MaxonThrowOp(errorValue, enumVal.TypeName));
  }

  /// <summary>
  /// Parse a try statement (statement context, not expression).
  /// Forms: try call() otherwise ignore
  ///        try call() otherwise 'label' ... end 'label'
  /// </summary>
  private void ParseTryStatement() {
    var tryToken = Advance(); // consume 'try'
    ParseTryExpression(tryToken, isStatementContext: true);
  }

  /// <summary>
  /// Core try/otherwise parsing shared between expression and statement contexts.
  /// Returns the result value (for expression context) or null (for ignore/block in statement context).
  /// </summary>
  private ExprResult.Direct ParseTryExpression(Token tryToken, bool isStatementContext = false) {
    // Parse the function call expression inside try
    var savedTryContext = _inTryContext;
    _inTryContext = true;
    var callExpr = ParsePrimary();
    _inTryContext = savedTryContext;

    // The callExpr should have generated a MaxonCallOp - we need to replace it with a MaxonTryCallOp
    var lastOp = _currentBlock!.Operations[^1];
    if (lastOp is not MaxonCallOp callOp) {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, "try requires a function call", tryToken.Line, tryToken.Column);
    }

    // Look up the callee to check it actually throws
    var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == callOp.Callee);
    if (callee != null && callee.ThrowsType == null) {
      throw new CompileError(ErrorCode.SemanticTryRequiresThrowingFunction,
        $"try requires a throwing function: ''{callOp.Callee}' does not throw'",
        tryToken.Line, tryToken.Column);
    }

    // Replace the MaxonCallOp with a MaxonTryCallOp
    _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
    var tryCallOp = new MaxonTryCallOp(callOp.Callee, callOp.Args, callOp.ResultKind, callOp.ResultStructTypeName);
    _currentBlock!.AddOp(tryCallOp);

    // '!' is syntactic-only (clone marker for struct results from arrays).
    // The actual struct copy is handled by get()'s return path.
    if (Check(TokenType.Bang)) {
      Advance();
    }

    // Check for 'otherwise' clause
    if (!Check(TokenType.Otherwise)) {
      // Propagation form: try func() - propagates error to caller
      if (_currentFunction?.ThrowsType == null) {
        throw new CompileError(ErrorCode.SemanticUnexpectedToken,
          "try without otherwise requires the enclosing function to have 'throws'",
          tryToken.Line, tryToken.Column);
      }
      return EmitTryPropagate(tryCallOp);
    }

    // Parse 'otherwise' clause
    Advance(); // consume 'otherwise'

    // Check for 'ignore' form
    if (Check(TokenType.Ignore)) {
      if (!isStatementContext) {
        throw new CompileError(ErrorCode.SemanticErrorTypeMismatch,
          "type mismatch: ''otherwise ignore' cannot be used in assignment'",
          tryToken.Line, tryToken.Column);
      }
      Advance(); // consume 'ignore'
      return new ExprResult.Direct(tryCallOp.ErrorFlag);
    }

    // Check for block handler form: otherwise 'label' ... end 'label'
    if (Check(TokenType.CharacterLiteral)) {
      return EmitTryOtherwiseBlock(tryCallOp, null);
    }

    // Check for '(e)' error binding before block label
    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      var errorBindingToken = Expect(TokenType.Identifier);
      Expect(TokenType.RightParen);
      return EmitTryOtherwiseBlock(tryCallOp, errorBindingToken);
    }

    // Default value form: otherwise <expression>
    return EmitTryOtherwiseDefault(tryCallOp);
  }

  /// <summary>
  /// Stores try call error flag and result to mutable variables for cross-block access.
  /// Returns (errorFlagVar, resultVar) names.
  /// </summary>
  private (string errorFlagVar, string? resultVar) StoreTryValuesForCrossBlockAccess(MaxonTryCallOp tryCallOp) {
    var errorFlagVar = $"__try_error_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(errorFlagVar, tryCallOp.ErrorFlag, true, true, MaxonValueKind.Integer));
    _variables[errorFlagVar] = new VarInfo(MaxonValueKind.Integer, true, tryCallOp.ErrorFlag, _currentBlock!, null);

    string? resultVar = null;
    if (tryCallOp.Result != null) {
      resultVar = $"__try_result_{_blockCounter++}";
      var resultKind = tryCallOp.ResultKind ?? MaxonValueKind.Integer;
      _currentBlock!.AddOp(new MaxonAssignOp(resultVar, tryCallOp.Result, true, true, resultKind));
      _variables[resultVar] = new VarInfo(resultKind, true, tryCallOp.Result, _currentBlock!, null);
    }
    return (errorFlagVar, resultVar);
  }

  /// <summary>
  /// Emits error flag != 0 check and conditional branch.
  /// </summary>
  private void EmitErrorFlagCheck(MaxonValue errorFlag, string errorBlockLabel, string continueBlockLabel) {
    var zeroOp = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(zeroOp);
    var cmpOp = new MaxonBinOp(MaxonBinOperator.Ne, errorFlag, zeroOp.Result, MaxonValueKind.Integer);
    _currentBlock!.AddOp(cmpOp);
    _currentBlock!.AddOp(new MaxonCondBrOp(cmpOp.Result, errorBlockLabel, continueBlockLabel));
  }

  /// <summary>
  /// Emits the correct var ref op for loading a variable, handling struct/enum/primitive types.
  /// </summary>
  private MaxonValue EmitVarRefOp(string varName, MaxonValueKind kind, string? structTypeName) {
    if (kind == MaxonValueKind.Struct) {
      var structRefOp = new MaxonStructVarRefOp(varName, structTypeName!);
      _currentBlock!.AddOp(structRefOp);
      return structRefOp.Result;
    }
    if (kind == MaxonValueKind.Enum) {
      var enumType = (MlirEnumType)_typeRegistry[structTypeName!];
      var backingKind = GetEnumBackingKind(enumType);
      var enumRefOp = new MaxonEnumVarRefOp(varName, structTypeName!, backingKind);
      _currentBlock!.AddOp(enumRefOp);
      return enumRefOp.Result;
    }
    var loadOp = new MaxonVarRefOp(varName, kind);
    _currentBlock!.AddOp(loadOp);
    return loadOp.Result;
  }

  /// <summary>
  /// Creates the continue block after a try/otherwise and loads the result from a variable.
  /// </summary>
  private ExprResult.Direct EmitTryContinueBlock(string continueBlockLabel, string? resultVar, MaxonTryCallOp tryCallOp) {
    var contBlock = _currentFunction!.Body.AddBlock(continueBlockLabel);
    _currentBlock = contBlock;

    if (resultVar != null) {
      var resultKind = tryCallOp.ResultKind ?? MaxonValueKind.Integer;
      var loadedValue = EmitVarRefOp(resultVar, resultKind, tryCallOp.ResultStructTypeName);
      return new ExprResult.Direct(loadedValue);
    }
    return new ExprResult.Direct(tryCallOp.ErrorFlag);
  }

  private ExprResult.Direct EmitTryPropagate(MaxonTryCallOp tryCallOp) {
    var propagateErrorBlock = UniqueLabel("propagate_error");
    var continueBlock = UniqueLabel("try_continue");

    var (errorFlagVar, resultVar) = StoreTryValuesForCrossBlockAccess(tryCallOp);
    EmitErrorFlagCheck(tryCallOp.ErrorFlag, propagateErrorBlock, continueBlock);

    // Propagation block: load error flag from variable and re-throw
    var propBlock = _currentFunction!.Body.AddBlock(propagateErrorBlock);
    _currentBlock = propBlock;
    var loadErrorOp = new MaxonVarRefOp(errorFlagVar, MaxonValueKind.Integer);
    _currentBlock!.AddOp(loadErrorOp);
    _currentBlock!.AddOp(new MaxonReturnOp(loadErrorOp.Result, isErrorPropagation: true));

    return EmitTryContinueBlock(continueBlock, resultVar, tryCallOp);
  }

  private ExprResult.Direct EmitTryOtherwiseBlock(MaxonTryCallOp tryCallOp, Token? errorBindingToken) {
    var labelToken = Expect(TokenType.CharacterLiteral);
    var blockLabel = labelToken.Value;

    var errorBlock = UniqueLabel("otherwise_error");
    var continueBlock = UniqueLabel("otherwise_continue");

    var (errorFlagVar, resultVar) = StoreTryValuesForCrossBlockAccess(tryCallOp);
    EmitErrorFlagCheck(tryCallOp.ErrorFlag, errorBlock, continueBlock);

    // Error handling block
    var errBlock = _currentFunction!.Body.AddBlock(errorBlock);
    _currentBlock = errBlock;

    if (errorBindingToken != null) {
      var loadErrorOp = new MaxonVarRefOp(errorFlagVar, MaxonValueKind.Integer);
      _currentBlock!.AddOp(loadErrorOp);
      _variables[errorBindingToken.Value] = new VarInfo(MaxonValueKind.Integer, false, loadErrorOp.Result, _currentBlock!, null);
    }

    SkipNewlines();
    ParseBodyUntilEnd();
    if (!BlockEndsWithTerminator(errBlock)) {
      _currentBlock!.AddOp(new MaxonBrOp(continueBlock));
    }

    ExpectEndLabel(blockLabel);

    return EmitTryContinueBlock(continueBlock, resultVar, tryCallOp);
  }

  private ExprResult.Direct EmitTryOtherwiseDefault(MaxonTryCallOp tryCallOp) {
    var defaultExpr = ParseExpression();
    var defaultValue = ResolveExprValue(defaultExpr);

    var resultVarName = $"__try_result_{_blockCounter++}";
    var resultKind = DetermineValueKind(defaultValue);
    var structTypeName = tryCallOp.ResultStructTypeName
      ?? (defaultValue is MaxonStruct s ? s.TypeName : null)
      ?? (defaultValue is MaxonEnum e ? e.TypeName : null);

    // Store the default value to a temp variable so it can be used across blocks
    var defaultVarName = $"__try_default_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(defaultVarName, defaultValue, true, true, resultKind));
    _variables[defaultVarName] = new VarInfo(resultKind, true, defaultValue, _currentBlock!, structTypeName);

    // Store the call result (success path value)
    if (tryCallOp.Result != null) {
      _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, tryCallOp.Result, true, true, resultKind));
      _variables[resultVarName] = new VarInfo(resultKind, true, tryCallOp.Result, _currentBlock!, structTypeName);
    }

    var errorBlock = UniqueLabel("otherwise_default_error");
    var continueBlock = UniqueLabel("otherwise_default_continue");

    EmitErrorFlagCheck(tryCallOp.ErrorFlag, errorBlock, continueBlock);

    // Error block: load default from temp variable and overwrite result
    var errBlock = _currentFunction!.Body.AddBlock(errorBlock);
    _currentBlock = errBlock;
    var loadedDefault = EmitVarRefOp(defaultVarName, resultKind, structTypeName);
    _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, loadedDefault, false, true, resultKind));
    _currentBlock!.AddOp(new MaxonBrOp(continueBlock));

    // Continue block: load from result variable
    var contBlock = _currentFunction!.Body.AddBlock(continueBlock);
    _currentBlock = contBlock;

    var loadedResult = EmitVarRefOp(resultVarName, resultKind, structTypeName);
    return new ExprResult.Direct(loadedResult);
  }

  private void ParseVarDecl() {
    ParseVarOrLetDecl(isMutable: true);
  }

  private void ParseLetDecl() {
    ParseVarOrLetDecl(isMutable: false);
  }

  private void ParseVarOrLetDecl(bool isMutable) {
    Advance(); // consume 'var' or 'let'
    var nameToken = Expect(TokenType.Identifier);
    var name = nameToken.Value;
    Expect(TokenType.Equals);
    var initExpr = ParseExpression();
    var initValue = ResolveExprValue(initExpr) ?? throw new InvalidOperationException($"Compiler bug: Variable '{name}' initialization expression did not produce a value (this should not happen - please report this bug)");

    if (initValue is MaxonStruct structVal) {
      var kind = MaxonValueKind.Struct;
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables[name] = new VarInfo(kind, isMutable, initValue, _currentBlock!, structVal.TypeName);
    } else if (initValue is MaxonEnum enumVal) {
      var kind = MaxonValueKind.Enum;
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables[name] = new VarInfo(kind, isMutable, initValue, _currentBlock!, enumVal.TypeName);
    } else if (initValue is MaxonFunctionPtr) {
      var kind = MaxonValueKind.Function;
      var fnType = GetFunctionTypeFromLastOp();
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables[name] = new VarInfo(kind, isMutable, initValue, _currentBlock!, FnTypeName: fnType);
    } else {
      var kind = DetermineValueKind(initValue);
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables[name] = new VarInfo(kind, isMutable, initValue, _currentBlock!);
    }
  }

  private MlirFunctionType GetFunctionTypeFromLastOp() {
    var lastOp = _currentBlock!.Operations.LastOrDefault();
    return lastOp switch {
      MaxonFunctionRefOp fnRefOp => fnRefOp.FunctionType,
      MaxonFunctionParamOp fnParamOp => fnParamOp.FunctionType,
      MaxonFunctionVarRefOp fnVarRefOp => fnVarRefOp.FunctionType,
      _ => throw new InvalidOperationException($"Cannot determine function type from {lastOp?.GetType().Name}")
    };
  }

  private void ParseAssignment() {
    var nameToken = Advance(); // consume identifier
    var name = nameToken.Value;
    Expect(TokenType.Equals);

    // Check if it's a global variable assignment
    if (_globalVars.TryGetValue(name, out var globalInfo)) {
      var newValue = ResolveExprValue(ParseExpression());
      _currentBlock!.AddOp(new MaxonGlobalStoreOp(name, newValue, globalInfo.Kind));
      return;
    }

    if (!_variables.TryGetValue(name, out var varInfo)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{name}'", nameToken.Line, nameToken.Column);
    }
    if (!varInfo.Mutable) {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Variable '{name}' is not mutable", nameToken.Line, nameToken.Column);
    }

    var newVal = ResolveExprValue(ParseExpression());
    // Capture function type before adding the assign op (which would change the last op)
    MlirFunctionType? fnType = null;
    if (varInfo.Kind == MaxonValueKind.Function) {
      fnType = GetFunctionTypeFromLastOp();
    }
    _currentBlock!.AddOp(new MaxonAssignOp(name, newVal, isDeclaration: false, isMutable: true, varInfo.Kind));
    // Preserve function type if reassigning a function variable
    if (varInfo.Kind == MaxonValueKind.Function) {
      _variables[name] = new VarInfo(varInfo.Kind, true, newVal, _currentBlock!, FnTypeName: fnType);
    } else {
      _variables[name] = new VarInfo(varInfo.Kind, true, newVal, _currentBlock!, varInfo.StructTypeName);
    }
  }

  private void ParseQualifiedAssignment() {
    var typeToken = Advance(); // consume type name
    Advance(); // consume '.'
    var fieldToken = Advance(); // consume field name
    Expect(TokenType.Equals);

    var qualifiedName = $"{typeToken.Value}.{fieldToken.Value}";

    if (_globalVars.TryGetValue(qualifiedName, out var globalInfo)) {
      var newValue = ResolveExprValue(ParseExpression());
      _currentBlock!.AddOp(new MaxonGlobalStoreOp(qualifiedName, newValue, globalInfo.Kind));
      return;
    }

    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined static variable '{qualifiedName}'", typeToken.Line, typeToken.Column);
  }

  private void ParseFieldAssignment() {
    var nameToken = Advance(); // consume struct var name
    var name = nameToken.Value;
    Expect(TokenType.Dot);
    var fieldToken = Expect(TokenType.Identifier);

    if (!_variables.TryGetValue(name, out var varInfo)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{name}'", nameToken.Line, nameToken.Column);
    }

    if (!varInfo.Mutable) {
      throw new CompileError(ErrorCode.ParserImmutableVariable,
        $"cannot assign to immutable variable: '{name}'",
        nameToken.Line, nameToken.Column);
    }

    // Parse chain of intermediate field accesses: o.inner.inner2...fieldN = value
    // Emit MaxonFieldAccessOp for each intermediate struct-typed field,
    // then MaxonFieldAssignOp for the final field.
    var currentStructTypeName = varInfo.StructTypeName!;
    var currentValue = ResolveExprValue(new ExprResult.VarRef(name, varInfo));

    while (Check(TokenType.Dot)) {
      // This is an intermediate field - it must be struct-typed so we can continue the chain
      (currentValue, currentStructTypeName) = EmitIntermediateFieldAccess(currentValue, currentStructTypeName, fieldToken);

      Advance(); // consume '.'
      fieldToken = Expect(TokenType.Identifier);
    }

    Expect(TokenType.Equals);

    // Now emit the final field assignment
    EmitFieldAssignment(currentStructTypeName, currentValue, fieldToken, nameToken);
  }

  private void EmitFieldAssignment(string structTypeName, MaxonValue structVal, Token fieldToken, Token errorToken) {
    var structType = (MlirStructType)_typeRegistry[structTypeName];
    var field = structType.GetField(fieldToken.Value)
      ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
        $"Type '{structType.Name}' has no field named '{fieldToken.Value}'",
        fieldToken.Line, fieldToken.Column);

    if (!field.IsExported && _currentTypeName != structType.Name) {
      throw new CompileError(ErrorCode.SemanticUnexportedFieldAccess,
        $"cannot access unexported field: '{fieldToken.Value}' outside of type '{structType.Name}'",
        fieldToken.Line, fieldToken.Column);
    }

    if (!field.IsMutable) {
      throw new CompileError(ErrorCode.ParserImmutableVariable,
        $"cannot assign to field '{structType.Name}.{fieldToken.Value}' because it is immutable (declare with 'var' to make it mutable)",
        errorToken.Line, errorToken.Column);
    }

    var newValue = ResolveExprValue(ParseExpression());
    _currentBlock!.AddOp(new MaxonFieldAssignOp(structVal, structTypeName, fieldToken.Value, newValue));
  }

  private void ParseCallStatement() {
    var token = Advance(); // consume identifier

    // Handle compiler builtins (__managed_memory_*, __cstring_*, __make_char_*)
    if (IsCompilerBuiltin(token.Value)) {
      Advance(); // consume '('
      TryEmitManagedMemoryBuiltin(token);
      return;
    }

    if (TrySiblingMethodCall(token) != null)
      return;

    Advance(); // consume '('
    var (args, callee) = ParseCallArgs(token);
    CreateFunctionCall(token, args, callee);
  }

  private static bool IsCompilerBuiltin(string name) => CompilerBuiltins.ContainsKey(name);

  /// <summary>
  /// Parses element_size argument which must be a compile-time integer literal (e.g., 1, 8).
  /// </summary>
  private int ParseElementSizeConstant() {
    if (Check(TokenType.IntegerLiteral)) {
      return (int)ParseIntegerLiteral(Advance());
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression,
      "element_size must be a compile-time constant (integer literal)",
      Current().Line, Current().Column);
  }

  public record BuiltinInfo(string HelpText, Func<Parser, MaxonValue?> Handler);

  public static readonly Dictionary<string, BuiltinInfo> CompilerBuiltins = new() {
    ["__managed_memory_len"] = new(
      "Returns the length (number of elements) of a managed memory buffer.\n\n`__managed_memory_len(memory) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(managed, "__ManagedMemory", "length", MaxonValueKind.Integer);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_capacity"] = new(
      "Returns the capacity of a managed memory buffer.\n\n`__managed_memory_capacity(memory) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(managed, "__ManagedMemory", "capacity", MaxonValueKind.Integer);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_set_length"] = new(
      "Sets the length of a managed memory buffer.\n\n`__managed_memory_set_length(memory, length)`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var newLen = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonFieldAssignOp(managed, "__ManagedMemory", "length", newLen);
        p._currentBlock!.AddOp(op);
        return null;
      }),
    ["__managed_memory_get_unchecked"] = new(
      "Gets the element at the given index without bounds checking.\n\n`__managed_memory_get_unchecked(memory, index) returns Element`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGetOp(managed, index, p.GetElementKind());
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_set_at"] = new(
      "Sets the element at the given index in a managed memory buffer.\n\n`__managed_memory_set_at(memory, index, value)`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var value = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var elementKind = p.GetElementKind();
        var op = new MaxonManagedMemSetOp(managed, index, value, elementKind);
        p._currentBlock!.AddOp(op);
        return null;
      }),
    ["__managed_memory_create"] = new(
      "Allocates a new heap-backed managed memory buffer with the given initial capacity.\n\n`__managed_memory_create(capacity, element_size) returns __ManagedMemory`",
      p => {
        var count = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        int elementSize = p.ParseElementSizeConstant();
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemCreateOp(count, elementSize);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_grow"] = new(
      "Grows a managed memory buffer to a new capacity using realloc.\n\n`__managed_memory_grow(memory, new_capacity)`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var newCap = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGrowOp(managed, newCap);
        p._currentBlock!.AddOp(op);
        return null;
      }),
    ["__managed_memory_shift_right"] = new(
      "Shifts elements right in the buffer starting at the given index, creating a gap.\n\n`__managed_memory_shift_right(memory, index, count)`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var count = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemShiftOp(managed, index, count, shiftRight: true);
        p._currentBlock!.AddOp(op);
        return null;
      }),
    ["__managed_memory_shift_left"] = new(
      "Shifts elements left in the buffer starting at the given index, closing a gap.\n\n`__managed_memory_shift_left(memory, index, count)`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var count = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemShiftOp(managed, index, count, shiftRight: false);
        p._currentBlock!.AddOp(op);
        return null;
      }),
    ["__managed_memory_byte_at"] = new(
      "Gets a single byte at the given index, zero-extended to int.\n\n`__managed_memory_byte_at(memory, index) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemByteGetOp(managed, index);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_set_byte"] = new(
      "Sets a single byte at the given index in a managed memory buffer.\n\n`__managed_memory_set_byte(memory, index, value)`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var value = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemByteSetOp(managed, index, value);
        p._currentBlock!.AddOp(op);
        return null;
      }),
    ["__managed_memory_concat"] = new(
      "Concatenates two managed memory buffers into a new buffer.\n\n`__managed_memory_concat(lhs, rhs) returns __ManagedMemory`",
      p => {
        var lhs = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var rhs = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemConcatOp(lhs, rhs);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_slice"] = new(
      "Creates a new buffer from a slice of the source buffer [start, end).\n\n`__managed_memory_slice(memory, start, end) returns __ManagedMemory`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var start = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var end = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemSliceOp(managed, start, end);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__managed_memory_to_cstring"] = new(
      "Returns the raw buffer pointer from a managed memory struct as a C string.\n\n`__managed_memory_to_cstring(memory) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedToCStringOp(managed);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__cstring_to_managed"] = new(
      "Converts a null-terminated C string pointer to a managed memory buffer.\n\n`__cstring_to_managed(cstring_ptr) returns __ManagedMemory`",
      p => {
        var cstrPtr = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonCStringToManagedOp(cstrPtr);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__cstring_write_stdout"] = new(
      "Writes a null-terminated C string to stdout.\n\n`__cstring_write_stdout(cstring_ptr) returns int`",
      p => {
        var cstrPtr = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonCStringWriteStdoutOp(cstrPtr);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__make_char_from_bytes"] = new(
      "Creates a Character value from bytes in a managed memory buffer at the given position.\n\n`__make_char_from_bytes(memory, position, length) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var pos = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var len = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonMakeCharFromBytesOp(managed, pos, len);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    // === Command Line intrinsics ===
    ["__command_line_count"] = RuntimeCallIntrinsic(
      "Returns the number of command line arguments.\n\n`__command_line_count() returns int`",
      "maxon_command_line_count", 0, true),
    ["__command_line_arg"] = RuntimeCallIntrinsic(
      "Returns a C string pointer for the command line argument at the given index.\n\n`__command_line_arg(index) returns int`",
      "maxon_command_line_arg", 1, true),
    // === File I/O intrinsics ===
    ["__file_open_read"] = RuntimeCallIntrinsic(
      "Opens a file for reading, returns handle or -1.\n\n`__file_open_read(cstring_path) returns int`",
      "maxon_file_open_read", 1, true),
    ["__file_size"] = RuntimeCallIntrinsic(
      "Returns the size of an open file handle.\n\n`__file_size(handle) returns int`",
      "maxon_file_size", 1, true),
    ["__file_read"] = RuntimeCallIntrinsic(
      "Reads bytes from file into managed memory.\n\n`__file_read(handle, managed, size) returns int`",
      "maxon_file_read", 3, true),
    ["__file_close"] = RuntimeCallIntrinsic(
      "Closes a file handle.\n\n`__file_close(handle)`",
      "maxon_file_close", 1, false),
    ["__file_delete"] = RuntimeCallIntrinsic(
      "Deletes a file. Returns 0 on success, non-zero on failure.\n\n`__file_delete(cstring_path) returns int`",
      "maxon_file_delete", 1, true),
    ["__write_file"] = RuntimeCallIntrinsic(
      "Writes text content to a file. Returns 0 on success.\n\n`__write_file(cstring_path, cstring_content) returns int`",
      "maxon_write_file", 2, true),
    ["__write_file_binary"] = new(
      "Writes binary content to a file. Returns 0 on success.\n\n`__write_file_binary(cstring_path, array) returns int`",
      p => {
        var path = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var arrayVal = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        // Extract managed.buffer and managed.length from the Array struct
        var managedRef = new MaxonFieldAccessOp(arrayVal, "Array", "managed", MaxonValueKind.Struct, "__ManagedMemory");
        p._currentBlock!.AddOp(managedRef);
        var bufferRef = new MaxonFieldAccessOp(managedRef.Result, "__ManagedMemory", "buffer", MaxonValueKind.Integer);
        p._currentBlock!.AddOp(bufferRef);
        var lengthRef = new MaxonFieldAccessOp(managedRef.Result, "__ManagedMemory", "length", MaxonValueKind.Integer);
        p._currentBlock!.AddOp(lengthRef);
        var op = new MaxonCallRuntimeOp("maxon_write_file_binary", [path, bufferRef.Result, lengthRef.Result], true);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    // === Directory intrinsics ===
    ["__find_first_file"] = RuntimeCallIntrinsic(
      "Opens a file search. Returns handle or 0.\n\n`__find_first_file(cstring_pattern) returns int`",
      "maxon_find_first_file", 1, true),
    ["__find_filename"] = RuntimeCallIntrinsic(
      "Gets the current filename from a search handle as a C string.\n\n`__find_filename(handle) returns int`",
      "maxon_find_filename", 1, true),
    ["__find_next_file"] = RuntimeCallIntrinsic(
      "Advances to next file. Returns non-zero if found.\n\n`__find_next_file(handle) returns int`",
      "maxon_find_next_file", 1, true),
    ["__find_close"] = RuntimeCallIntrinsic(
      "Closes a file search handle.\n\n`__find_close(handle)`",
      "maxon_find_close", 1, false),
    ["__directory_exists"] = new(
      "Checks if a directory exists. Returns true/false.\n\n`__directory_exists(cstring_path) returns bool`",
      p => {
        var path = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonCallRuntimeOp("maxon_directory_exists", [path], true);
        p._currentBlock!.AddOp(op);
        var zeroOp = new MaxonLiteralOp(0L);
        p._currentBlock!.AddOp(zeroOp);
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Ne, op.Result!, zeroOp.Result, MaxonValueKind.Integer);
        p._currentBlock!.AddOp(cmpOp);
        return cmpOp.Result;
      }),
    // === Process intrinsics ===
    ["__process_create"] = RuntimeCallIntrinsic(
      "Creates a process. Returns handle.\n\n`__process_create(cstring_cmd, cstring_cwd) returns int`",
      "maxon_process_create", 2, true),
    ["__process_wait"] = RuntimeCallIntrinsic(
      "Waits for process. Returns 0=completed, 1=timeout, -1=error.\n\n`__process_wait(handle, timeoutMs) returns int`",
      "maxon_process_wait", 2, true),
    ["__process_get_exit_code"] = RuntimeCallIntrinsic(
      "Gets exit code of completed process.\n\n`__process_get_exit_code(handle) returns int`",
      "maxon_process_get_exit_code", 1, true),
    ["__process_close"] = RuntimeCallIntrinsic(
      "Closes a process handle.\n\n`__process_close(handle)`",
      "maxon_process_close", 1, false),
    // === Map intrinsics ===
    ["__map_get_init_key"] = new(
      "Gets a key from initialization managed memory at given index.\n\n`__map_get_init_key(managed, index) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGetOp(managed, index, p.GetElementKind());
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["__map_get_init_value"] = new(
      "Gets a value from initialization managed memory at given index.\n\n`__map_get_init_value(managed, index) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGetOp(managed, index, p.GetElementKind());
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
  };

  /// <summary>
  /// Get the element type from the current struct's type parameters (e.g., Element for Array/Vector)
  /// </summary>
  private MaxonValueKind GetElementKind() {
    if (_currentTypeName != null
        && _typeRegistry.TryGetValue(_currentTypeName, out var typeInfo)
        && typeInfo is MlirStructType structType
        && structType.TypeParams.TryGetValue("Element", out var elementType)) {
      return elementType.ToValueKind();
    }
    // No Element type parameter - default to Integer for non-generic types
    return MaxonValueKind.Integer;
  }

  /// <summary>
  /// Creates a CompilerBuiltinInfo that parses N arguments and emits a MaxonCallRuntimeOp.
  /// </summary>
  private static BuiltinInfo RuntimeCallIntrinsic(string doc, string runtimeName, int argCount, bool hasResult) {
    return new(doc, p => {
      var args = new List<MaxonValue>();
      for (int i = 0; i < argCount; i++) {
        if (i > 0) p.Expect(TokenType.Comma);
        args.Add(p.ResolveExprValue(p.ParseExpression()));
      }
      p.Expect(TokenType.RightParen);
      var op = new MaxonCallRuntimeOp(runtimeName, args, hasResult);
      p._currentBlock!.AddOp(op);
      return op.Result;
    });
  }

  /// <summary>
  /// Handles compiler builtin intrinsics.
  /// Called after consuming '('. Returns the result value (or null for void builtins).
  /// </summary>
  private MaxonValue? TryEmitManagedMemoryBuiltin(Token token) {
    if (CompilerBuiltins.TryGetValue(token.Value, out var info))
      return info.Handler(this);
    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unknown builtin '{token.Value}'", token.Line, token.Column);
  }

  private MaxonCallOp? TrySiblingMethodCall(Token token) {
    if (_currentTypeName == null || !_variables.TryGetValue("self", out var selfInfo))
      return null;

    // Construct namespace-qualified type name for sibling method lookup
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? _currentTypeName : $"{namespace_}.{_currentTypeName}";
    var siblingMethodName = $"{qualifiedTypeName}.{token.Value}";

    var resolvedSiblingName = ResolveMethodName(siblingMethodName);
    if (resolvedSiblingName == null)
      return null;
    Advance(); // consume '('
    var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
    var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedSiblingName, token.Line, token.Column);
    var (siblingArgs, siblingCallee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, structVal);
    return CreateFunctionCall(qualifiedFuncToken, siblingArgs, siblingCallee);
  }

  private void ParseQualifiedCallStatement() {
    var typeToken = Advance(); // consume type name
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Advance(); // consume '('

    // Create a synthetic token for the qualified name, positioned at the method name
    var qualifiedName = $"{typeToken.Value}.{methodToken.Value}";
    var qualifiedToken = new Token(TokenType.Identifier, qualifiedName, methodToken.Line, methodToken.Column);

    var (args, callee) = ParseCallArgs(qualifiedToken);
    CreateFunctionCall(qualifiedToken, args, callee);
  }

  private void ParseInstanceMethodCallStatement(VarInfo varInfo, string methodName) {
    Advance(); // consume variable name
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Advance(); // consume '('

    var structVal = varInfo.Value;
    var qualifiedToken = new Token(TokenType.Identifier, methodName, methodToken.Line, methodToken.Column);
    var (args, callee) = ParseInstanceMethodCallArgs(qualifiedToken, structVal);
    CreateFunctionCall(qualifiedToken, args, callee);
  }

  private void ParseIf() {
    Advance(); // consume 'if'

    // Dispatch to if-try forms: `if try expr` or `if let name = try expr`
    if (Check(TokenType.Try) || Check(TokenType.Let)) {
      ParseIfTry();
      return;
    }

    var condition = ResolveExprValue(ParseExpression());

    // Parse then-block label
    var thenSourceLabel = Expect(TokenType.CharacterLiteral).Value;
    var thenLabel = UniqueLabel(thenSourceLabel);
    SkipNewlines();

    // Save entry block to append cond_br later
    var entryBlock = _currentBlock!;

    // Create and parse the then block
    var thenBlock = _currentFunction!.Body.AddBlock(thenLabel);
    _currentBlock = thenBlock;
    ParseBodyUntilEnd();
    var thenEndBlock = _currentBlock; // may differ from thenBlock after nested if/else

    // Parse: end 'thenLabel'
    ExpectEndLabel(thenSourceLabel);

    // Check for else or else-if
    MlirBlock<MaxonOp>? elseBlock = null;
    MlirBlock<MaxonOp>? elseEndBlock = null;
    string? elseLabel = null;
    if (Check(TokenType.Else)) {
      Advance(); // consume 'else'
      if (Check(TokenType.If)) {
        // else-if chain: create a synthetic block and parse the nested if into it
        elseLabel = $"{thenLabel}.elseif";
        elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
        _currentBlock = elseBlock;
        ParseIf();
        elseEndBlock = _currentBlock;
      } else {
        var elseSourceLabel = Expect(TokenType.CharacterLiteral).Value;
        elseLabel = UniqueLabel(elseSourceLabel);
        SkipNewlines();

        elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
        _currentBlock = elseBlock;
        ParseBodyUntilEnd();
        elseEndBlock = _currentBlock;
        ExpectEndLabel(elseSourceLabel);
      }
    }

    EmitConditionalBranch(entryBlock, condition, thenLabel, thenBlock, thenEndBlock, elseLabel, elseBlock, elseEndBlock);
  }

  /// <summary>
  /// Creates merge blocks and emits a conditional branch for if/if-try statements.
  /// Handles the common logic of determining if branches need merge blocks and setting up control flow.
  /// thenEndBlock/elseEndBlock are the final blocks after parsing each branch body
  /// (may differ from initial blocks due to nested if/else creating merge blocks).
  /// </summary>
  private void EmitConditionalBranch(MlirBlock<MaxonOp> entryBlock, MaxonValue condition, string thenLabel, MlirBlock<MaxonOp> thenBlock, MlirBlock<MaxonOp>? thenEndBlock, string? elseLabel, MlirBlock<MaxonOp>? elseBlock, MlirBlock<MaxonOp>? elseEndBlock) {
    // Use the end blocks for merge decisions — the end block is where control flow
    // actually is after parsing each branch (may be a merge block from nested if/else)
    bool thenTerminated = thenEndBlock == null || BlockEndsWithTerminator(thenEndBlock);
    bool elseTerminated = elseEndBlock != null ? BlockEndsWithTerminator(elseEndBlock) : (elseBlock != null);

    bool thenNeedsMerge = !thenTerminated;
    bool elseNeedsMerge = elseBlock != null && !elseTerminated;

    if (thenNeedsMerge || elseNeedsMerge) {
      var mergeLabel = $"{thenLabel}.merge";
      var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);

      if (thenNeedsMerge)
        (thenEndBlock ?? thenBlock).AddOp(new MaxonBrOp(mergeLabel));
      if (elseNeedsMerge)
        (elseEndBlock ?? elseBlock!).AddOp(new MaxonBrOp(mergeLabel));

      entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, elseLabel ?? mergeLabel));
      _currentBlock = mergeBlock;
    } else if (elseLabel != null) {
      entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, elseLabel));
      _currentBlock = null;
    } else {
      var afterLabel = $"{thenLabel}.after";
      var afterBlock = _currentFunction!.Body.AddBlock(afterLabel);
      entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, afterLabel));
      _currentBlock = afterBlock;
    }
  }

  /// <summary>
  /// Parses `if try expr 'label'` (boolean form) and `if let name = try expr 'label'` (binding form).
  /// Called after 'if' has been consumed. Current token is 'try' or 'let'.
  /// </summary>
  private void ParseIfTry() {
    // Determine form: binding (`if let name = try ...`) or boolean (`if try ...`)
    string? bindingName = null;
    if (Check(TokenType.Let)) {
      Advance(); // consume 'let'
      bindingName = Expect(TokenType.Identifier).Value;
      Expect(TokenType.Equals);
    }

    // Parse the try call expression (shared logic with ParseTryExpression lines 1990-2011)
    var tryToken = Expect(TokenType.Try);
    _inTryContext = true;
    var callExpr = ParsePrimary();
    _inTryContext = false;

    var lastOp = _currentBlock!.Operations[^1];
    if (lastOp is not MaxonCallOp callOp) {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, "try requires a function call", tryToken.Line, tryToken.Column);
    }

    // Validate the callee actually throws
    var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == callOp.Callee);
    if (callee != null && callee.ThrowsType == null) {
      throw new CompileError(ErrorCode.SemanticTryRequiresThrowingFunction,
        $"try requires a throwing function: ''{callOp.Callee}' does not throw'",
        tryToken.Line, tryToken.Column);
    }

    // Replace MaxonCallOp with MaxonTryCallOp
    _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
    var tryCallOp = new MaxonTryCallOp(callOp.Callee, callOp.Args, callOp.ResultKind, callOp.ResultStructTypeName);
    _currentBlock!.AddOp(tryCallOp);

    // Store error flag and result to mutable variables for cross-block access
    var (errorFlagVar, resultVar) = StoreTryValuesForCrossBlockAccess(tryCallOp);

    // Build condition: errorFlag == 0 means success (true = enter then-block)
    var zeroOp = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(zeroOp);
    var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, tryCallOp.ErrorFlag, zeroOp.Result, MaxonValueKind.Integer);
    _currentBlock!.AddOp(cmpOp);
    var condition = cmpOp.Result;

    // Parse block label
    var thenSourceLabel = Expect(TokenType.CharacterLiteral).Value;
    var thenLabel = UniqueLabel(thenSourceLabel);
    SkipNewlines();

    // Save entry block for the conditional branch
    var entryBlock = _currentBlock!;

    // Create the then-block
    var thenBlock = _currentFunction!.Body.AddBlock(thenLabel);
    _currentBlock = thenBlock;

    // For binding form, load the result and create a let-binding inside the then-block
    if (bindingName != null && resultVar != null) {
      var resultKind = tryCallOp.ResultKind ?? MaxonValueKind.Integer;
      var loadedValue = EmitVarRefOp(resultVar, resultKind, tryCallOp.ResultStructTypeName);

      // Determine StructTypeName for the binding: use the try call's ResultStructTypeName,
      // or resolve the associated type name for type-parameter return types
      string? bindingStructTypeName = tryCallOp.ResultStructTypeName;
      if (bindingStructTypeName == null
          && callee?.ReturnType is MlirTypeParameterType
          && _currentTypeName != null
          && _typeRegistry.TryGetValue(_currentTypeName, out var currentTypeReg)
          && currentTypeReg is MlirStructType currentSt) {
        bindingStructTypeName = currentSt.AssociatedTypeNames
          .FirstOrDefault(n => _typeRegistry.ContainsKey(n));
      }

      _currentBlock!.AddOp(new MaxonAssignOp(bindingName, loadedValue, isDeclaration: true, isMutable: false, resultKind));
      _variables[bindingName] = new VarInfo(resultKind, false, loadedValue, _currentBlock!, bindingStructTypeName);
    }

    ParseBodyUntilEnd();
    var thenEndBlock = _currentBlock;
    ExpectEndLabel(thenSourceLabel);

    // Check for else clause
    MlirBlock<MaxonOp>? elseBlock = null;
    MlirBlock<MaxonOp>? elseEndBlock = null;
    string? elseLabel = null;
    if (Check(TokenType.Else)) {
      Advance(); // consume 'else'

      // Check for error binding: else (e) 'label'
      Token? errorBindingToken = null;
      if (Check(TokenType.LeftParen)) {
        Advance(); // consume '('
        errorBindingToken = Expect(TokenType.Identifier);
        Expect(TokenType.RightParen);
      }

      var elseSourceLabel = Expect(TokenType.CharacterLiteral).Value;
      elseLabel = UniqueLabel(elseSourceLabel);
      SkipNewlines();

      elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
      _currentBlock = elseBlock;

      // If error binding requested, load the error flag as a variable in the else block
      if (errorBindingToken != null) {
        var loadErrorOp = new MaxonVarRefOp(errorFlagVar, MaxonValueKind.Integer);
        _currentBlock!.AddOp(loadErrorOp);
        _variables[errorBindingToken.Value] = new VarInfo(MaxonValueKind.Integer, false, loadErrorOp.Result, _currentBlock!, null);
      }

      ParseBodyUntilEnd();
      elseEndBlock = _currentBlock;
      ExpectEndLabel(elseSourceLabel);
    }

    EmitConditionalBranch(entryBlock, condition, thenLabel, thenBlock, thenEndBlock, elseLabel, elseBlock, elseEndBlock);
  }

  private void ParseWhile() {
    Advance(); // consume 'while'

    // Save the entry block - we'll add the branch to the header from here
    var entryBlock = _currentBlock!;

    // Scan forward to find the loop label (character literal) at end of while line
    int savedPos = _pos;
    while (!Check(TokenType.CharacterLiteral) && !Check(TokenType.Newline) && !IsAtEnd()) {
      Advance();
    }
    if (!Check(TokenType.CharacterLiteral)) {
      throw new CompileError(ErrorCode.ParserExpectedToken, "Expected loop label after while condition", Current().Line, Current().Column);
    }
    var loopSourceLabel = Advance().Value;
    _pos = savedPos; // rewind to parse condition properly

    var loopLabel = UniqueLabel(loopSourceLabel);
    var headerLabel = $"{loopLabel}.header";
    var bodyLabel = loopLabel;
    var exitLabel = $"{loopLabel}.exit";

    // Branch from entry to header
    entryBlock.AddOp(new MaxonBrOp(headerLabel));

    // Create header block with condition
    var headerBlock = _currentFunction!.Body.AddBlock(headerLabel);
    _currentBlock = headerBlock;
    var condition = ResolveExprValue(ParseExpression());

    // Consume the label (already parsed above, but we need to advance past it)
    Expect(TokenType.CharacterLiteral);
    SkipNewlines();

    // Emit cond_br: if condition is true, go to body; else go to exit
    headerBlock.AddOp(new MaxonCondBrOp(condition, bodyLabel, exitLabel));

    // Create and parse the body block
    var bodyBlock = _currentFunction!.Body.AddBlock(bodyLabel);
    _currentBlock = bodyBlock;
    _loopStack.Push(new LoopContext(loopSourceLabel, headerLabel, exitLabel));
    ParseBodyUntilEnd();
    _loopStack.Pop();
    ExpectEndLabel(loopSourceLabel);

    // At end of body, branch back to header
    // _currentBlock may differ from bodyBlock (e.g. if/else merge block)
    // _currentBlock can be null if all paths in the body terminated
    if (_currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonBrOp(headerLabel));
    }

    // Create exit block - this is where execution continues after the loop
    var exitBlock = _currentFunction!.Body.AddBlock(exitLabel);
    _currentBlock = exitBlock;
  }

  /// <summary>
  /// Parse for-in loop: for varName in expr 'label' ... end 'label'
  /// Calls try next() each iteration and exits on IterationError.exhausted.
  /// </summary>
  private void ParseForIn() {
    var forToken = Advance(); // consume 'for'
    var itemName = Expect(TokenType.Identifier).Value;
    Expect(TokenType.In);
    var iterableExpr = ParseExpression();
    var iterableValue = ResolveExprValue(iterableExpr);
    var loopSourceLabel = Expect(TokenType.CharacterLiteral).Value;
    SkipNewlines();

    var iterableTypeName = ((iterableValue is MaxonStruct ms) ? ms.TypeName : _currentTypeName) ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
        "Cannot determine type for 'for-in' iterable expression",
        forToken.Line, forToken.Column);

    var iterableType = _typeRegistry.TryGetValue(iterableTypeName, out var regType)
      ? regType as MlirStructType : null;

    // Resolve the next() method name
    var nextMethodName = ResolveMethodName($"{iterableTypeName}.next") ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Type '{iterableTypeName}' does not implement Iterable (missing next() method)",
        forToken.Line, forToken.Column);

    // Determine the element type from next()'s return type
    var nextFunc = _currentModule!.Functions.FirstOrDefault(f => f.Name == nextMethodName)
      ?? _currentModule!.Functions.First(f => UnmangleName(f.Name) == nextMethodName);
    var elementMlirType = nextFunc.ReturnType;
    // Resolve type parameter to concrete type using the iterable's type params
    if (elementMlirType is MlirTypeParameterType tp
        && iterableType?.TypeParams.TryGetValue(tp.ParameterName, out var concreteElemType) == true) {
      elementMlirType = concreteElemType;
    }
    // Resolve to canonical type (pre-scanned stubs may have 0 fields)
    if (elementMlirType is MlirStructType retStruct
        && _typeRegistry.TryGetValue(retStruct.Name, out var canonical)
        && canonical is MlirStructType canonicalStruct) {
      elementMlirType = canonicalStruct;
    }
    var elementKind = elementMlirType!.ToValueKind();
    var elementStructTypeName = elementMlirType switch {
      MlirStructType s => s.Name,
      MlirEnumType e => e.Name,
      _ => (string?)null
    };

    var loopLabel = UniqueLabel(loopSourceLabel);
    var headerLabel = $"{loopLabel}.header";
    var bodyLabel = loopLabel;
    var exitLabel = $"{loopLabel}.exit";

    // Create a mutable copy of the iterable so next() can update iteration state
    var iterVarName = $"__for_iter_{_blockCounter}";
    _currentBlock!.AddOp(new MaxonAssignOp(iterVarName, iterableValue, isDeclaration: true, isMutable: true, MaxonValueKind.Struct));
    _variables[iterVarName] = new VarInfo(MaxonValueKind.Struct, true, iterableValue, _currentBlock!, iterableTypeName);

    // Branch from entry to header
    _currentBlock!.AddOp(new MaxonBrOp(headerLabel));

    // Header block: call try next() and check for exhaustion
    var headerBlock = _currentFunction!.Body.AddBlock(headerLabel);
    _currentBlock = headerBlock;

    // Load the iterator struct
    var iterRef = new MaxonStructVarRefOp(iterVarName, iterableTypeName);
    headerBlock.AddOp(iterRef);

    // Call try next(self) — next() throws IterationError.exhausted when done
    var tryCallOp = new MaxonTryCallOp(nextMethodName, [iterRef.Result], elementKind, elementStructTypeName);
    headerBlock.AddOp(tryCallOp);

    // Store error flag and result for cross-block access
    var errorFlagVar = $"__try_error_{_blockCounter++}";
    headerBlock.AddOp(new MaxonAssignOp(errorFlagVar, tryCallOp.ErrorFlag, true, true, MaxonValueKind.Integer));
    _variables[errorFlagVar] = new VarInfo(MaxonValueKind.Integer, true, tryCallOp.ErrorFlag, headerBlock, null);

    string? resultVar = null;
    if (tryCallOp.Result != null) {
      resultVar = $"__try_result_{_blockCounter++}";
      headerBlock.AddOp(new MaxonAssignOp(resultVar, tryCallOp.Result, true, true, elementKind));
      _variables[resultVar] = new VarInfo(elementKind, true, tryCallOp.Result, headerBlock, elementStructTypeName);
    }

    // Check error flag: zero means success → continue to body, non-zero → exit
    // Block ordering requires then=fallthrough=body, else=jump=exit
    var zeroOp = new MaxonLiteralOp(0L);
    headerBlock.AddOp(zeroOp);
    var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, tryCallOp.ErrorFlag, zeroOp.Result, MaxonValueKind.Integer);
    headerBlock.AddOp(cmpOp);
    headerBlock.AddOp(new MaxonCondBrOp(cmpOp.Result, bodyLabel, exitLabel));

    // Body block: load the result as the loop variable
    var bodyBlock = _currentFunction!.Body.AddBlock(bodyLabel);
    _currentBlock = bodyBlock;
    _loopStack.Push(new LoopContext(loopSourceLabel, headerLabel, exitLabel));

    if (resultVar != null) {
      var loadedValue = EmitVarRefOp(resultVar, elementKind, elementStructTypeName);
      _currentBlock!.AddOp(new MaxonAssignOp(itemName, loadedValue, isDeclaration: true, isMutable: false, elementKind));
      _variables[itemName] = new VarInfo(elementKind, false, loadedValue, bodyBlock, elementStructTypeName);
    }

    // Parse the body
    ParseBodyUntilEnd();
    _loopStack.Pop();
    ExpectEndLabel(loopSourceLabel);

    // Branch back to header (skip if all paths in the body terminated)
    if (_currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonBrOp(headerLabel));
    }

    // Create exit block
    var exitBlock = _currentFunction!.Body.AddBlock(exitLabel);
    _currentBlock = exitBlock;
  }

  private void ParseBreak() {
    var token = Advance(); // consume 'break'
    var loop = ResolveLoopTarget(token);
    _currentBlock!.AddOp(new MaxonBrOp(loop.ExitLabel));
  }

  private void ParseContinue() {
    var token = Advance(); // consume 'continue'
    var loop = ResolveLoopTarget(token);
    _currentBlock!.AddOp(new MaxonBrOp(loop.HeaderLabel));
  }

  private LoopContext ResolveLoopTarget(Token keyword) {
    if (_loopStack.Count == 0) {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"'{keyword.Value}' can only be used inside a loop", keyword.Line, keyword.Column);
    }
    if (Check(TokenType.CharacterLiteral)) {
      var labelToken = Advance();
      foreach (var ctx in _loopStack) {
        if (ctx.SourceLabel == labelToken.Value) return ctx;
      }
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"No enclosing loop with label '{labelToken.Value}'", labelToken.Line, labelToken.Column);
    }
    return _loopStack.Peek();
  }

  private static bool BlockEndsWithTerminator(MlirBlock<MaxonOp> block) {
    if (block.Operations.Count == 0) return false;
    var lastOp = block.Operations[^1];
    return lastOp is MaxonReturnOp or MaxonBrOp or MaxonCondBrOp or MaxonThrowOp;
  }

  // ============================================================================
  // Match statement / expression
  // ============================================================================

  private abstract record MatchPattern(string DisplayName, int Line, int Column);
  private sealed record ExactIntPattern(long Value, string DisplayName, int Line, int Column) : MatchPattern(DisplayName, Line, Column);
  private sealed record ExactFloatPattern(double Value, string DisplayName, int Line, int Column) : MatchPattern(DisplayName, Line, Column);
  private sealed record ExactStringPattern(string Value, string DisplayName, int Line, int Column) : MatchPattern(DisplayName, Line, Column);
  private sealed record ExactCharPattern(string Value, string DisplayName, int Line, int Column) : MatchPattern(DisplayName, Line, Column);
  private sealed record RangePattern(RangeBound? Lower, RangeBound? Upper, bool UpperInclusive, string DisplayName, int Line, int Column) : MatchPattern(DisplayName, Line, Column);
  // Pattern for associated-value enum case: matches tag and optionally binds payload values
  private sealed record EnumCasePattern(int Ordinal, string CaseName, List<string>? Bindings,
      List<(string Name, MlirType Type)>? AssociatedValues, string DisplayName, int Line, int Column)
      : MatchPattern(DisplayName, Line, Column);

  private abstract record RangeBound;
  private sealed record IntRangeBound(long Value) : RangeBound;
  private sealed record FloatRangeBound(double Value) : RangeBound;
  private sealed record CharRangeBound(string Value) : RangeBound;

  /// <summary>
  /// Parses match case patterns. Supports integer, float, string, character literals,
  /// enum member access, and range patterns (to/upto/min/max).
  /// Multiple patterns can be separated by 'or'. Returns the list of parsed patterns.
  /// Also tracks seen patterns for duplicate detection and enum cases for exhaustiveness.
  /// </summary>
  private List<MatchPattern> ParseMatchPatterns(
      string? enumTypeName, MlirEnumType? enumType,
      HashSet<string> seenPatternKeys, HashSet<string> seenEnumCases,
      MaxonValueKind compareKind, string? structTypeName) {
    var patterns = new List<MatchPattern>();
    while (true) {
      var patternLine = Current().Line;
      var patternCol = Current().Column;

      if (Check(TokenType.Identifier) && enumType is { HasAssociatedValues: true }
          && PeekNext().Type != TokenType.Dot
          && enumType.GetCase(Current().Value) != null) {
        // Bare case name pattern for associated-value enums: empty, value(n), etc.
        var caseNameToken = Advance();
        var enumCase = enumType.GetCase(caseNameToken.Value)!;

        var displayName = caseNameToken.Value;
        if (!seenPatternKeys.Add(displayName)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{displayName}'",
            patternLine, patternCol);
        }
        seenEnumCases.Add(caseNameToken.Value);

        List<string>? bindings = null;
        if (Check(TokenType.LeftParen)) {
          Advance(); // consume '('
          bindings = [];
          while (!Check(TokenType.RightParen) && !IsAtEnd()) {
            var bindingName = Expect(TokenType.Identifier).Value;
            bindings.Add(bindingName);
            if (Check(TokenType.Comma)) Advance();
          }
          Expect(TokenType.RightParen);

          // Validate binding count matches associated value count
          var expectedCount = enumCase.AssociatedValues?.Count ?? 0;
          if (bindings.Count != expectedCount) {
            throw new CompileError(ErrorCode.SemanticEnumWrongBindingCount,
              $"wrong binding count: '{caseNameToken.Value}'",
              caseNameToken.Line, caseNameToken.Column);
          }
        }

        patterns.Add(new EnumCasePattern(enumCase.Ordinal, caseNameToken.Value, bindings,
          enumCase.AssociatedValues, displayName, patternLine, patternCol));
      } else if (Check(TokenType.Identifier) && enumType is { HasAssociatedValues: true }
          && PeekNext().Type != TokenType.Dot
          && enumType.GetCase(Current().Value) == null
          && (PeekNext().Type == TokenType.LeftParen || PeekNext().Type == TokenType.Then || PeekNext().Type == TokenType.Gives)) {
        // Bare identifier in associated-value match that is NOT a valid case name
        throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
          $"unknown enum case: '{Current().Value}'",
          patternLine, patternCol);
      } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Dot) {
        // Enum pattern: Type.case
        var enumTypeToken = Advance();
        Advance(); // consume '.'
        var caseNameToken = Expect(TokenType.Identifier);
        var patternEnumName = enumTypeToken.Value;

        if (enumTypeName == null || patternEnumName != enumTypeName) {
          throw new CompileError(ErrorCode.ParserUnexpectedToken,
            $"pattern type '{patternEnumName}' does not match scrutinee type",
            enumTypeToken.Line, enumTypeToken.Column);
        }

        var enumCase = enumType!.GetCase(caseNameToken.Value)
          ?? throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
            $"unknown enum case: '{caseNameToken.Value}'",
            caseNameToken.Line, caseNameToken.Column);

        var displayName = $"{patternEnumName}.{caseNameToken.Value}";
        if (!seenPatternKeys.Add(displayName)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{displayName}'",
            patternLine, patternCol);
        }
        seenEnumCases.Add(caseNameToken.Value);

        if (enumType.BackingType == MlirType.F64) {
          var rawValue = (double)enumCase.RawValue!;
          patterns.Add(new ExactFloatPattern(rawValue, displayName, patternLine, patternCol));
        } else {
          long rawValue;
          if (enumType.BackingType == MlirType.I64) {
            rawValue = (long)enumCase.RawValue!;
          } else if (enumType.BackingType == null || enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
            rawValue = enumCase.Ordinal;
          } else {
            throw new InvalidOperationException($"Unsupported enum backing type in match pattern: {enumType.BackingType}");
          }
          patterns.Add(new ExactIntPattern(rawValue, displayName, patternLine, patternCol));
        }
      } else if (Check(TokenType.Identifier) && Current().Value == "min") {
        // Open-ended lower bound: min to/upto <upper>
        Advance(); // consume 'min'
        if (compareKind == MaxonValueKind.Struct) {
          throw new CompileError(ErrorCode.ParserMatchTypeMismatch,
            $"'min' is only valid for numeric range patterns",
            patternLine, patternCol);
        }
        bool upperInclusive = true;
        if (Check(TokenType.To)) {
          Advance();
        } else if (Check(TokenType.Upto)) {
          Advance();
          upperInclusive = false;
        } else {
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            "'min' must be followed by 'to' or 'upto'",
            Current().Line, Current().Column);
        }
        var upper = ParseRangeEndpoint(compareKind);
        var displayName = $"min {(upperInclusive ? "to" : "upto")} {RangeBoundDisplay(upper)}";
        if (!seenPatternKeys.Add(displayName)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{displayName}'",
            patternLine, patternCol);
        }
        patterns.Add(new RangePattern(null, upper, upperInclusive, displayName, patternLine, patternCol));
      } else if (Check(TokenType.IntegerLiteral) || (Check(TokenType.Minus) && _pos + 1 < _tokens.Count
          && (_tokens[_pos + 1].Type == TokenType.IntegerLiteral || _tokens[_pos + 1].Type == TokenType.FloatLiteral))) {
        // Integer or negative integer/float, possibly followed by range
        if (compareKind == MaxonValueKind.Struct) {
          throw new CompileError(ErrorCode.ParserMatchTypeMismatch,
            $"pattern type 'int' does not match scrutinee type '{structTypeName}'",
            patternLine, patternCol);
        }
        bool negative = false;
        if (Check(TokenType.Minus)) {
          Advance(); // consume '-'
          negative = true;
        }
        if (Check(TokenType.FloatLiteral)) {
          // Negative float
          var litToken = Advance();
          var litValue = double.Parse(litToken.Value, System.Globalization.CultureInfo.InvariantCulture);
          if (negative) litValue = -litValue;
          patterns.Add(ParseFloatPatternOrRange(litValue, compareKind, seenPatternKeys, patternLine, patternCol));
        } else {
          var litToken = Advance();
          var litValue = ParseIntegerLiteral(litToken);
          if (negative) litValue = -litValue;
          patterns.Add(ParseIntPatternOrRange(litValue, compareKind, seenPatternKeys, patternLine, patternCol));
        }
      } else if (Check(TokenType.FloatLiteral)) {
        if (compareKind == MaxonValueKind.Struct) {
          throw new CompileError(ErrorCode.ParserMatchTypeMismatch,
            $"pattern type 'float' does not match scrutinee type '{structTypeName}'",
            patternLine, patternCol);
        }
        var litToken = Advance();
        var litValue = double.Parse(litToken.Value, System.Globalization.CultureInfo.InvariantCulture);
        patterns.Add(ParseFloatPatternOrRange(litValue, compareKind, seenPatternKeys, patternLine, patternCol));
      } else if (Check(TokenType.StringLiteral)) {
        if (compareKind != MaxonValueKind.Struct || structTypeName == null ||
            !(_typeRegistry[structTypeName] is MlirStructType st && st.ConformingInterfaces.Contains("BuiltinStringLiteral"))) {
          var scrutineeTypeName = enumTypeName ?? structTypeName ?? KindToTypeName(compareKind);
          throw new CompileError(ErrorCode.ParserMatchTypeMismatch,
            $"pattern type 'String' does not match scrutinee type '{scrutineeTypeName}'",
            Current().Line, Current().Column);
        }
        var strToken = Advance();
        var displayName = $"\"{strToken.Value}\"";
        if (!seenPatternKeys.Add(displayName)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{displayName}'",
            patternLine, patternCol);
        }
        patterns.Add(new ExactStringPattern(strToken.Value, displayName, patternLine, patternCol));
      } else if (Check(TokenType.CharacterLiteral) && structTypeName != null &&
          _typeRegistry[structTypeName] is MlirStructType charSt && charSt.ConformingInterfaces.Contains("BuiltinCharLiteral")) {
        var charToken = Advance();
        patterns.Add(ParseCharPatternOrRange(charToken.Value, seenPatternKeys, patternLine, patternCol));
      } else {
        throw new CompileError(ErrorCode.ParserExpectedExpression,
          $"Expected pattern value, got '{Current().Value}'",
          Current().Line, Current().Column);
      }

      // 'or' separates patterns; 'then'/'gives' ends the pattern list
      if (Check(TokenType.Or)) {
        Advance(); // consume 'or'
      } else {
        break;
      }
    }
    return patterns;
  }

  private MatchPattern ParseIntPatternOrRange(long value, MaxonValueKind compareKind,
      HashSet<string> seenPatternKeys, int patternLine, int patternCol) {
    if (Check(TokenType.To) || Check(TokenType.Upto)) {
      bool upperInclusive = Check(TokenType.To);
      Advance();
      RangeBound? upper = null;
      if (Check(TokenType.Identifier) && Current().Value == "max") {
        Advance();
      } else {
        upper = ParseRangeEndpoint(compareKind);
      }
      var lower = compareKind == MaxonValueKind.Float
        ? (RangeBound)new FloatRangeBound(value)
        : new IntRangeBound(value);
      var displayName = $"{value} {(upperInclusive ? "to" : "upto")} {(upper == null ? "max" : RangeBoundDisplay(upper))}";
      if (!seenPatternKeys.Add(displayName)) {
        throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
          $"duplicate pattern in match: '{displayName}'",
          patternLine, patternCol);
      }
      return new RangePattern(lower, upper, upperInclusive, displayName, patternLine, patternCol);
    }
    var display = value.ToString();
    if (!seenPatternKeys.Add(display)) {
      throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
        $"duplicate pattern in match: '{display}'",
        patternLine, patternCol);
    }
    if (compareKind == MaxonValueKind.Float) {
      return new ExactFloatPattern(value, display, patternLine, patternCol);
    }
    return new ExactIntPattern(value, display, patternLine, patternCol);
  }

  private MatchPattern ParseFloatPatternOrRange(double value, MaxonValueKind compareKind,
      HashSet<string> seenPatternKeys, int patternLine, int patternCol) {
    if (Check(TokenType.To) || Check(TokenType.Upto)) {
      bool upperInclusive = Check(TokenType.To);
      Advance();
      RangeBound? upper = null;
      if (Check(TokenType.Identifier) && Current().Value == "max") {
        Advance();
      } else {
        upper = ParseRangeEndpoint(compareKind);
      }
      var lower = new FloatRangeBound(value);
      var displayName = $"{value} {(upperInclusive ? "to" : "upto")} {(upper == null ? "max" : RangeBoundDisplay(upper))}";
      if (!seenPatternKeys.Add(displayName)) {
        throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
          $"duplicate pattern in match: '{displayName}'",
          patternLine, patternCol);
      }
      return new RangePattern(lower, upper, upperInclusive, displayName, patternLine, patternCol);
    }
    var display = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (!seenPatternKeys.Add(display)) {
      throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
        $"duplicate pattern in match: '{display}'",
        patternLine, patternCol);
    }
    return new ExactFloatPattern(value, display, patternLine, patternCol);
  }

  private MatchPattern ParseCharPatternOrRange(string charValue, HashSet<string> seenPatternKeys,
      int patternLine, int patternCol) {
    if (Check(TokenType.To) || Check(TokenType.Upto)) {
      bool upperInclusive = Check(TokenType.To);
      Advance();
      var upperToken = Expect(TokenType.CharacterLiteral);
      var lower = new CharRangeBound(charValue);
      var upper = new CharRangeBound(upperToken.Value);
      var displayName = $"'{charValue}' {(upperInclusive ? "to" : "upto")} '{upperToken.Value}'";
      if (!seenPatternKeys.Add(displayName)) {
        throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
          $"duplicate pattern in match: '{displayName}'",
          patternLine, patternCol);
      }
      return new RangePattern(lower, upper, upperInclusive, displayName, patternLine, patternCol);
    }
    var display = $"'{charValue}'";
    if (!seenPatternKeys.Add(display)) {
      throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
        $"duplicate pattern in match: '{display}'",
        patternLine, patternCol);
    }
    return new ExactCharPattern(charValue, display, patternLine, patternCol);
  }

  private RangeBound ParseRangeEndpoint(MaxonValueKind compareKind) {
    bool negative = false;
    if (Check(TokenType.Minus)) {
      Advance();
      negative = true;
    }
    if (Check(TokenType.FloatLiteral)) {
      var tok = Advance();
      var val = double.Parse(tok.Value, System.Globalization.CultureInfo.InvariantCulture);
      if (negative) val = -val;
      return new FloatRangeBound(val);
    }
    if (Check(TokenType.IntegerLiteral)) {
      var tok = Advance();
      var val = ParseIntegerLiteral(tok);
      if (negative) val = -val;
      if (compareKind == MaxonValueKind.Float) {
        return new FloatRangeBound(val);
      }
      return new IntRangeBound(val);
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression,
      $"Expected range endpoint, got '{Current().Value}'",
      Current().Line, Current().Column);
  }

  private static string RangeBoundDisplay(RangeBound bound) {
    return bound switch {
      IntRangeBound i => i.Value.ToString(),
      FloatRangeBound f => f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
      CharRangeBound c => $"'{c.Value}'",
      _ => throw new InvalidOperationException($"Unknown range bound type: {bound.GetType().Name}")
    };
  }

  private static string KindToTypeName(MaxonValueKind kind) {
    return kind switch {
      MaxonValueKind.Integer => "int",
      MaxonValueKind.Float => "float",
      MaxonValueKind.Bool => "bool",
      MaxonValueKind.Byte => "byte",
      MaxonValueKind.Struct => "struct",
      MaxonValueKind.Enum => "enum",
      MaxonValueKind.Function => "function",
      _ => throw new InvalidOperationException($"Unknown value kind: {kind}")
    };
  }

  /// <summary>
  /// Emits comparison ops for a set of patterns against the scrutinee value.
  /// Multiple patterns are combined with logical OR.
  /// Returns the combined boolean comparison result.
  /// </summary>
  /// <summary>
  /// Emits payload binding variables for associated-value enum match patterns.
  /// For each EnumCasePattern with bindings, extracts payload values from the
  /// original enum scrutinee and declares them as local variables.
  /// </summary>
  private void EmitEnumCaseBindings(List<MatchPattern> patterns, string origEnumTempName, string enumTypeName) {
    foreach (var pattern in patterns) {
      if (pattern is not EnumCasePattern { Bindings: { } bindings, AssociatedValues: { } assocValues }) continue;

      // Load the original enum value from the cross-block temp variable
      var enumVarRef = new MaxonEnumVarRefOp(origEnumTempName, enumTypeName, MaxonValueKind.Integer);
      _currentBlock!.AddOp(enumVarRef);

      for (int i = 0; i < bindings.Count; i++) {
        var bindingName = bindings[i];
        var assocType = assocValues[i].Type;
        var bindingKind = assocType.ToValueKind();

        var payloadOp = new MaxonEnumPayloadOp(enumVarRef.Result, enumTypeName, i, bindingKind);
        _currentBlock!.AddOp(payloadOp);

        _currentBlock!.AddOp(new MaxonAssignOp(bindingName, payloadOp.Result,
          isDeclaration: true, isMutable: false, bindingKind));
        _variables[bindingName] = new VarInfo(bindingKind, false, payloadOp.Result, _currentBlock!);
      }
    }
  }

  private MaxonValue EmitPatternComparison(
      List<MatchPattern> patterns, string scrutTempName,
      MaxonValueKind compareKind, string? structTypeName) {
    MaxonValue? combinedCmp = null;
    foreach (var pattern in patterns) {
      var patternCmp = EmitSinglePatternComparison(pattern, scrutTempName, compareKind, structTypeName);
      if (combinedCmp == null) {
        combinedCmp = patternCmp;
      } else {
        var orOp = new MaxonBinOp(MaxonBinOperator.Or, combinedCmp, patternCmp, MaxonValueKind.Bool);
        _currentBlock!.AddOp(orOp);
        combinedCmp = orOp.Result;
      }
    }
    return combinedCmp!;
  }

  private MaxonValue EmitSinglePatternComparison(MatchPattern pattern, string scrutTempName,
      MaxonValueKind compareKind, string? structTypeName) {
    switch (pattern) {
      case ExactIntPattern intPat: {
        var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
        _currentBlock!.AddOp(refOp);
        var patLit = new MaxonLiteralOp(intPat.Value);
        _currentBlock!.AddOp(patLit);
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, refOp.Result, patLit.Result, compareKind);
        _currentBlock!.AddOp(cmpOp);
        return cmpOp.Result;
      }
      case ExactFloatPattern floatPat: {
        var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
        _currentBlock!.AddOp(refOp);
        var patLit = new MaxonLiteralOp(floatPat.Value);
        _currentBlock!.AddOp(patLit);
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, refOp.Result, patLit.Result, compareKind);
        _currentBlock!.AddOp(cmpOp);
        return cmpOp.Result;
      }
      case ExactStringPattern stringPat: {
        var refOp = new MaxonStructVarRefOp(scrutTempName, structTypeName!);
        _currentBlock!.AddOp(refOp);
        var strLit = new MaxonStringLiteralOp(stringPat.Value, structTypeName!);
        _currentBlock!.AddOp(strLit);
        var equalsMethodName = $"{structTypeName}.equals";
        var equalsToken = new Token(TokenType.Identifier, equalsMethodName, pattern.Line, pattern.Column);
        var callOp = CreateFunctionCall(equalsToken, [refOp.Result, strLit.Result]);
        return callOp.Result!;
      }
      case ExactCharPattern charPat: {
        var refOp = new MaxonStructVarRefOp(scrutTempName, structTypeName!);
        _currentBlock!.AddOp(refOp);
        var charLit = new MaxonCharLiteralOp(charPat.Value, structTypeName!);
        _currentBlock!.AddOp(charLit);
        var equalsMethodName = $"{structTypeName}.equals";
        var equalsToken = new Token(TokenType.Identifier, equalsMethodName, pattern.Line, pattern.Column);
        var callOp = CreateFunctionCall(equalsToken, [refOp.Result, charLit.Result]);
        return callOp.Result!;
      }
      case EnumCasePattern enumCasePat: {
        // Compare tag (ordinal) against the case's ordinal
        var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
        _currentBlock!.AddOp(refOp);
        var patLit = new MaxonLiteralOp((long)enumCasePat.Ordinal);
        _currentBlock!.AddOp(patLit);
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, refOp.Result, patLit.Result, compareKind);
        _currentBlock!.AddOp(cmpOp);
        return cmpOp.Result;
      }
      case RangePattern rangePat: {
        return EmitRangeComparison(rangePat, scrutTempName, compareKind, structTypeName);
      }
      default:
        throw new InvalidOperationException($"Unknown pattern type: {pattern.GetType().Name}");
    }
  }

  private MaxonValue EmitRangeComparison(RangePattern rangePat, string scrutTempName,
      MaxonValueKind compareKind, string? structTypeName) {
    if (rangePat.Lower is CharRangeBound || rangePat.Upper is CharRangeBound) {
      return EmitCharRangeComparison(rangePat, scrutTempName, structTypeName!);
    }

    MaxonValue? lowerCmp = null;
    MaxonValue? upperCmp = null;

    if (rangePat.Lower != null) {
      var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
      _currentBlock!.AddOp(refOp);
      MaxonLiteralOp lowerLit;
      if (rangePat.Lower is FloatRangeBound fb) {
        lowerLit = new MaxonLiteralOp(fb.Value);
      } else {
        lowerLit = new MaxonLiteralOp(((IntRangeBound)rangePat.Lower).Value);
      }
      _currentBlock!.AddOp(lowerLit);
      var geOp = new MaxonBinOp(MaxonBinOperator.Ge, refOp.Result, lowerLit.Result, compareKind);
      _currentBlock!.AddOp(geOp);
      lowerCmp = geOp.Result;
    }

    if (rangePat.Upper != null) {
      var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
      _currentBlock!.AddOp(refOp);
      MaxonLiteralOp upperLit;
      if (rangePat.Upper is FloatRangeBound fb) {
        upperLit = new MaxonLiteralOp(fb.Value);
      } else {
        upperLit = new MaxonLiteralOp(((IntRangeBound)rangePat.Upper).Value);
      }
      _currentBlock!.AddOp(upperLit);
      var upperOp = rangePat.UpperInclusive
        ? new MaxonBinOp(MaxonBinOperator.Le, refOp.Result, upperLit.Result, compareKind)
        : new MaxonBinOp(MaxonBinOperator.Lt, refOp.Result, upperLit.Result, compareKind);
      _currentBlock!.AddOp(upperOp);
      upperCmp = upperOp.Result;
    }

    if (lowerCmp != null && upperCmp != null) {
      var andOp = new MaxonBinOp(MaxonBinOperator.And, lowerCmp, upperCmp, MaxonValueKind.Bool);
      _currentBlock!.AddOp(andOp);
      return andOp.Result;
    }
    return (lowerCmp ?? upperCmp)!;
  }

  private MaxonValue EmitCharRangeComparison(RangePattern rangePat, string scrutTempName, string structTypeName) {
    MaxonValue? lowerCmp = null;
    MaxonValue? upperCmp = null;

    if (rangePat.Lower is CharRangeBound lowerBound) {
      var refOp = new MaxonStructVarRefOp(scrutTempName, structTypeName);
      _currentBlock!.AddOp(refOp);
      var charLit = new MaxonCharLiteralOp(lowerBound.Value, structTypeName);
      _currentBlock!.AddOp(charLit);
      var compareMethodName = $"{structTypeName}.compare";
      var compareToken = new Token(TokenType.Identifier, compareMethodName, rangePat.Line, rangePat.Column);
      var callOp = CreateFunctionCall(compareToken, [refOp.Result, charLit.Result]);
      var zeroLit = new MaxonLiteralOp(0L);
      _currentBlock!.AddOp(zeroLit);
      var geOp = new MaxonBinOp(MaxonBinOperator.Ge, callOp.Result!, zeroLit.Result, MaxonValueKind.Integer);
      _currentBlock!.AddOp(geOp);
      lowerCmp = geOp.Result;
    }

    if (rangePat.Upper is CharRangeBound upperBound) {
      var refOp = new MaxonStructVarRefOp(scrutTempName, structTypeName);
      _currentBlock!.AddOp(refOp);
      var charLit = new MaxonCharLiteralOp(upperBound.Value, structTypeName);
      _currentBlock!.AddOp(charLit);
      var compareMethodName = $"{structTypeName}.compare";
      var compareToken = new Token(TokenType.Identifier, compareMethodName, rangePat.Line, rangePat.Column);
      var callOp = CreateFunctionCall(compareToken, [refOp.Result, charLit.Result]);
      var zeroLit = new MaxonLiteralOp(0L);
      _currentBlock!.AddOp(zeroLit);
      var upperOp = rangePat.UpperInclusive
        ? new MaxonBinOp(MaxonBinOperator.Le, callOp.Result!, zeroLit.Result, MaxonValueKind.Integer)
        : new MaxonBinOp(MaxonBinOperator.Lt, callOp.Result!, zeroLit.Result, MaxonValueKind.Integer);
      _currentBlock!.AddOp(upperOp);
      upperCmp = upperOp.Result;
    }

    if (lowerCmp != null && upperCmp != null) {
      var andOp = new MaxonBinOp(MaxonBinOperator.And, lowerCmp, upperCmp, MaxonValueKind.Bool);
      _currentBlock!.AddOp(andOp);
      return andOp.Result;
    }
    return (lowerCmp ?? upperCmp)!;
  }

  /// <summary>
  /// Resolves scrutinee to a comparable value. For enums, extracts the raw backing value.
  /// Returns the compare value, its kind, and enum metadata if applicable.
  /// </summary>
  private (MaxonValue CompareVal, MaxonValueKind CompareKind, string? EnumTypeName, MlirEnumType? EnumType, string? StructTypeName)
      ResolveScrutinee(MaxonValue scrutineeVal) {
    if (scrutineeVal is MaxonEnum enumVal) {
      var enumTypeName = enumVal.TypeName;
      var enumType = (MlirEnumType)_typeRegistry[enumTypeName];
      if (enumType.HasAssociatedValues) {
        // For associated-value enums, extract the tag for comparison
        var tagOp = new MaxonEnumTagOp(scrutineeVal, enumTypeName);
        _currentBlock!.AddOp(tagOp);
        return (tagOp.Result, MaxonValueKind.Integer, enumTypeName, enumType, null);
      }
      var backingKind = GetEnumBackingKind(enumType);
      var rawOp = new MaxonEnumRawValueOp(scrutineeVal, enumTypeName, backingKind);
      _currentBlock!.AddOp(rawOp);
      return (rawOp.Result, backingKind, enumTypeName, enumType, null);
    }
    if (scrutineeVal is MaxonStruct structVal) {
      return (scrutineeVal, MaxonValueKind.Struct, null, null, structVal.TypeName);
    }
    return (scrutineeVal, DetermineValueKind(scrutineeVal), null, null, null);
  }

  /// Resolves the scrutinee and, for associated-value enums, stores the original
  /// enum value in a temp variable so payload can be extracted in case bodies.
  private (string? origEnumTempName, MaxonValue compareVal, MaxonValueKind compareKind,
      string? enumTypeName, MlirEnumType? enumType, string? structTypeName)
      SetupMatchScrutinee(MaxonValue scrutineeVal, string matchLabel) {
    string? origEnumTempName = null;
    if (scrutineeVal is MaxonEnum origEnum) {
      var origEnumType = (MlirEnumType)_typeRegistry[origEnum.TypeName];
      if (origEnumType.HasAssociatedValues) {
        origEnumTempName = $"__match_enum_{matchLabel}";
      }
    }
    var (compareVal, compareKind, enumTypeName, enumType, structTypeName) = ResolveScrutinee(scrutineeVal);
    if (origEnumTempName != null) {
      _currentBlock!.AddOp(new MaxonAssignOp(origEnumTempName, scrutineeVal, isDeclaration: true, isMutable: false, MaxonValueKind.Enum));
      _variables[origEnumTempName] = new VarInfo(MaxonValueKind.Enum, false, scrutineeVal, _currentBlock!);
    }
    return (origEnumTempName, compareVal, compareKind, enumTypeName, enumType, structTypeName);
  }

  /// <summary>
  /// Validates enum exhaustiveness when no default case is present.
  /// </summary>
  private static void ValidateEnumExhaustiveness(
      MlirEnumType? enumType, string? enumTypeName,
      bool hasDefault, HashSet<string> seenEnumCases, Token errorToken) {
    if (enumType == null || hasDefault) return;
    var missingCases = enumType.Cases
      .Where(c => !seenEnumCases.Contains(c.Name))
      .Select(c => c.Name)
      .ToList();
    if (missingCases.Count > 0) {
      throw new CompileError(ErrorCode.ParserMatchNotExhaustive,
        $"match on enum '{enumTypeName}' is not exhaustive, missing: {string.Join(", ", missingCases)}",
        errorToken.Line, errorToken.Column);
    }
  }

  /// <summary>
  /// Expects 'end' followed by a matching block identifier for match statements.
  /// </summary>
  private Token ExpectMatchEndLabel(string expectedLabel) {
    var endToken = Expect(TokenType.End);
    if (Check(TokenType.CharacterLiteral)) {
      var label = Advance().Value;
      if (label != expectedLabel) {
        throw new CompileError(ErrorCode.ParserMatchMismatchedBlockId,
          $"block identifier mismatch: expected '{expectedLabel}', got '{label}'",
          endToken.Line, endToken.Column);
      }
    }
    return endToken;
  }

  /// <summary>
  /// Patches the comparison chain after all cases are parsed.
  /// Each comparison block's false branch goes to the next comparison, or default, or merge.
  /// </summary>
  private static void PatchComparisonChain(
      MlirBlock<MaxonOp> entryBlock, string matchLabel,
      List<MlirBlock<MaxonOp>?> cmpBlocks, List<bool> caseIsDefault,
      string mergeLabel) {
    int firstCmpIndex = -1;
    int defaultIndex = -1;
    for (int i = 0; i < caseIsDefault.Count; i++) {
      if (!caseIsDefault[i] && firstCmpIndex < 0) firstCmpIndex = i;
      if (caseIsDefault[i] && defaultIndex < 0) defaultIndex = i;
    }

    // Entry block branches to first comparison (or default body if only default)
    if (firstCmpIndex >= 0) {
      entryBlock.AddOp(new MaxonBrOp($"{matchLabel}.cmp{firstCmpIndex}"));
    } else if (defaultIndex >= 0) {
      entryBlock.AddOp(new MaxonBrOp($"{matchLabel}.case{defaultIndex}"));
    }

    // Patch each comparison block's false target
    for (int i = 0; i < cmpBlocks.Count; i++) {
      if (caseIsDefault[i]) continue;

      var cmpBlock = cmpBlocks[i]!;
      var condBr = (MaxonCondBrOp)cmpBlock.Operations[^1];

      // Find next comparison or fall to default/merge
      string falseTarget;
      int nextCmpIndex = -1;
      for (int j = i + 1; j < cmpBlocks.Count; j++) {
        if (!caseIsDefault[j]) { nextCmpIndex = j; break; }
      }

      if (nextCmpIndex >= 0) {
        falseTarget = $"{matchLabel}.cmp{nextCmpIndex}";
      } else if (defaultIndex >= 0) {
        falseTarget = $"{matchLabel}.case{defaultIndex}";
      } else {
        falseTarget = mergeLabel;
      }

      cmpBlock.Operations.RemoveAt(cmpBlock.Operations.Count - 1);
      cmpBlock.AddOp(new MaxonCondBrOp(condBr.Condition, condBr.ThenBlock, falseTarget));
    }
  }

  private void ParseMatch() {
    Advance(); // consume 'match'

    var scrutineeExpr = ParseExpression();

    if (!Check(TokenType.CharacterLiteral)) {
      throw new CompileError(ErrorCode.ParserMatchMissingBlockId, "missing block identifier", Current().Line, Current().Column);
    }
    var blockIdToken = Advance();
    var sourceLabel = blockIdToken.Value;
    var matchLabel = UniqueLabel(sourceLabel);
    SkipNewlines();

    var scrutineeVal = ResolveExprValue(scrutineeExpr);
    var (origEnumTempName, compareVal, compareKind, enumTypeName, enumType, structTypeName) =
      SetupMatchScrutinee(scrutineeVal, matchLabel);

    var entryBlock = _currentBlock!;

    // Store scrutinee in a temp var so comparison blocks can reference it across blocks
    var scrutTempName = $"__match_{matchLabel}";
    entryBlock.AddOp(new MaxonAssignOp(scrutTempName, compareVal, isDeclaration: true, isMutable: false, compareKind));
    _variables[scrutTempName] = new VarInfo(compareKind, false, compareVal, entryBlock, StructTypeName: structTypeName);

    var mergeLabel = $"{matchLabel}.merge";
    var caseBlocks = new List<MlirBlock<MaxonOp>>();
    var caseIsDefault = new List<bool>();
    var caseFallthrough = new List<bool>();
    // One entry per case: non-default cases have their comparison block, default has null
    var cmpBlocks = new List<MlirBlock<MaxonOp>?>();
    var seenPatternKeys = new HashSet<string>();
    var seenEnumCases = new HashSet<string>();
    bool hasDefault = false;
    bool defaultSeen = false;

    int caseIndex = 0;
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      var caseLine = Current().Line;
      var caseCol = Current().Column;
      bool isDefault = false;
      var patterns = new List<MatchPattern>();

      if (Check(TokenType.Default)) {
        Advance(); // consume 'default'
        isDefault = true;
        hasDefault = true;
        defaultSeen = true;
      } else {
        if (defaultSeen) {
          throw new CompileError(ErrorCode.ParserMatchDefaultNotLast,
            "'default' case must be the last case in match",
            caseLine, caseCol);
        }
        patterns = ParseMatchPatterns(enumTypeName, enumType, seenPatternKeys, seenEnumCases, compareKind, structTypeName);
      }

      Expect(TokenType.Then);

      var caseBodyLabel = $"{matchLabel}.case{caseIndex}";
      MlirBlock<MaxonOp> caseBodyBlock;

      if (!isDefault) {
        // Comparison block must be added before the case body block so that
        // the cond_br's true target (case body) is the physical fall-through
        var cmpLabel = $"{matchLabel}.cmp{caseIndex}";
        var cmpBlock = _currentFunction!.Body.AddBlock(cmpLabel);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
        _currentBlock = cmpBlock;
        var combinedCmp = EmitPatternComparison(patterns, scrutTempName, compareKind, structTypeName);
        cmpBlock.AddOp(new MaxonCondBrOp(combinedCmp, caseBodyLabel, ""));
        cmpBlocks.Add(cmpBlock);
      } else {
        cmpBlocks.Add(null);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
      }

      _currentBlock = caseBodyBlock;

      // Emit payload bindings for associated-value enum patterns
      if (origEnumTempName != null) {
        EmitEnumCaseBindings(patterns, origEnumTempName, enumTypeName!);
      }

      bool caseHasReturn = Check(TokenType.Return);
      ParseStatement();

      bool hasFallthrough = false;
      if (Check(TokenType.And) && PeekNext().Type == TokenType.Fallthrough) {
        var andToken = Advance(); // consume 'and'
        Advance(); // consume 'fallthrough'
        if (caseHasReturn) {
          throw new CompileError(ErrorCode.ParserMatchFallthroughWithReturn,
            "match fallthrough with return: 'cannot combine 'fallthrough' with 'return''",
            andToken.Line, andToken.Column);
        }
        hasFallthrough = true;
      }

      caseBlocks.Add(caseBodyBlock);
      caseIsDefault.Add(isDefault);
      caseFallthrough.Add(hasFallthrough);

      caseIndex++;
      SkipNewlines();
    }

    var endToken = ExpectMatchEndLabel(sourceLabel);
    ValidateEnumExhaustiveness(enumType, enumTypeName, hasDefault, seenEnumCases, endToken);

    // Always create merge block - comparison chain needs a valid fallback target
    var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);

    PatchComparisonChain(entryBlock, matchLabel, cmpBlocks, caseIsDefault, mergeLabel);

    // Wire case body blocks: branch to merge or fallthrough to next case body
    bool allTerminate = true;
    for (int i = 0; i < caseBlocks.Count; i++) {
      var caseBlock = caseBlocks[i];
      if (!BlockEndsWithTerminator(caseBlock)) {
        allTerminate = false;
        if (caseFallthrough[i] && i + 1 < caseBlocks.Count) {
          caseBlock.AddOp(new MaxonBrOp($"{matchLabel}.case{i + 1}"));
        } else {
          caseBlock.AddOp(new MaxonBrOp(mergeLabel));
        }
      }
    }

    _variables.Remove(scrutTempName);
    if (origEnumTempName != null) _variables.Remove(origEnumTempName);

    // If all cases terminate, there's no reachable code after the match
    _currentBlock = allTerminate ? null : mergeBlock;
  }

  private ExprResult.Direct ParseMatchExpression() {
    Advance(); // consume 'match'

    var scrutineeExpr = ParseExpression();

    if (!Check(TokenType.CharacterLiteral)) {
      throw new CompileError(ErrorCode.ParserMatchMissingBlockId, "missing block identifier", Current().Line, Current().Column);
    }
    var blockIdToken = Advance();
    var sourceLabel = blockIdToken.Value;
    var matchLabel = UniqueLabel(sourceLabel);
    SkipNewlines();

    var scrutineeVal = ResolveExprValue(scrutineeExpr);
    var (origEnumTempName, compareVal, compareKind, enumTypeName, enumType, structTypeName) =
      SetupMatchScrutinee(scrutineeVal, matchLabel);

    var entryBlock = _currentBlock!;

    // Create a mutable result variable to hold the match expression value.
    // Initialized as Integer; the kind is updated when the first case value is parsed.
    var resultVarName = $"__matchexpr_{matchLabel}";
    var resultKind = MaxonValueKind.Integer;
    var zeroLit = new MaxonLiteralOp(0L);
    entryBlock.AddOp(zeroLit);
    entryBlock.AddOp(new MaxonAssignOp(resultVarName, zeroLit.Result, isDeclaration: true, isMutable: true, resultKind));
    _variables[resultVarName] = new VarInfo(resultKind, true, zeroLit.Result, entryBlock);

    // Store scrutinee for cross-block access
    var scrutTempName = $"__match_{matchLabel}";
    entryBlock.AddOp(new MaxonAssignOp(scrutTempName, compareVal, isDeclaration: true, isMutable: false, compareKind));
    _variables[scrutTempName] = new VarInfo(compareKind, false, compareVal, entryBlock, StructTypeName: structTypeName);

    var mergeLabel = $"{matchLabel}.merge";
    var caseBlocks = new List<MlirBlock<MaxonOp>>();
    var caseIsDefault = new List<bool>();
    var cmpBlocks = new List<MlirBlock<MaxonOp>?>();
    var seenPatternKeys = new HashSet<string>();
    var seenEnumCases = new HashSet<string>();
    bool hasDefault = false;
    bool defaultSeen = false;

    int caseIndex = 0;
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      var caseLine = Current().Line;
      var caseCol = Current().Column;
      bool isDefault = false;
      var patterns = new List<MatchPattern>();

      if (Check(TokenType.Default)) {
        Advance(); // consume 'default'
        isDefault = true;
        hasDefault = true;
        defaultSeen = true;
      } else {
        if (defaultSeen) {
          throw new CompileError(ErrorCode.ParserMatchDefaultNotLast,
            "'default' case must be the last case in match",
            caseLine, caseCol);
        }
        patterns = ParseMatchPatterns(enumTypeName, enumType, seenPatternKeys, seenEnumCases, compareKind, structTypeName);
      }

      Expect(TokenType.Gives);

      var caseBodyLabel = $"{matchLabel}.case{caseIndex}";

      MlirBlock<MaxonOp> caseBodyBlock;
      if (!isDefault) {
        // Comparison block added first for correct fall-through ordering
        var cmpLabel = $"{matchLabel}.cmp{caseIndex}";
        var cmpBlock = _currentFunction!.Body.AddBlock(cmpLabel);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
        _currentBlock = cmpBlock;
        var combinedCmp = EmitPatternComparison(patterns, scrutTempName, compareKind, structTypeName);
        cmpBlock.AddOp(new MaxonCondBrOp(combinedCmp, caseBodyLabel, ""));
        cmpBlocks.Add(cmpBlock);
      } else {
        cmpBlocks.Add(null);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
      }

      _currentBlock = caseBodyBlock;

      // Emit payload bindings for associated-value enum patterns
      if (origEnumTempName != null) {
        EmitEnumCaseBindings(patterns, origEnumTempName, enumTypeName!);
      }

      var caseValue = ResolveExprValue(ParseExpression());
      resultKind = DetermineValueKind(caseValue);

      if (Check(TokenType.And)) {
        throw new CompileError(ErrorCode.ParserUnexpectedToken,
          $"unexpected token: '{Current().Value}'",
          Current().Line, Current().Column);
      }

      _currentBlock.AddOp(new MaxonAssignOp(resultVarName, caseValue, isDeclaration: false, isMutable: true, resultKind));
      _currentBlock.AddOp(new MaxonBrOp(mergeLabel));

      caseBlocks.Add(caseBodyBlock);
      caseIsDefault.Add(isDefault);

      caseIndex++;
      SkipNewlines();
    }

    var endToken = ExpectMatchEndLabel(sourceLabel);
    ValidateEnumExhaustiveness(enumType, enumTypeName, hasDefault, seenEnumCases, endToken);

    var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);
    PatchComparisonChain(entryBlock, matchLabel, cmpBlocks, caseIsDefault, mergeLabel);

    _variables.Remove(scrutTempName);
    _variables.Remove(resultVarName);
    if (origEnumTempName != null) _variables.Remove(origEnumTempName);

    _currentBlock = mergeBlock;
    var resultRef = new MaxonVarRefOp(resultVarName, resultKind);
    _currentBlock.AddOp(resultRef);

    return new ExprResult.Direct(resultRef.Result);
  }

  // ============================================================================
  // Expression parsing
  // ============================================================================

  private ExprResult ParseExpression(int minPrecedence = 0) {
    var lhs = ParsePrimary();

    // Handle 'as' cast expressions (postfix, binds tighter than binary ops)
    while (Check(TokenType.As)) {
      var asToken = Advance(); // consume 'as'
      var targetKind = ParseTypeKeyword();
      var inputVal = ResolveExprValue(lhs);
      var sourceKind = DetermineValueKind(inputVal);
      ValidateCast(sourceKind, targetKind, asToken);
      var castOp = new MaxonCastOp(inputVal, targetKind);
      _currentBlock!.AddOp(castOp);
      lhs = new ExprResult.Direct(castOp.Result);
    }

    while (BinaryOperators.TryGetValue(Current().Type, out var entry) && entry.Precedence >= minPrecedence) {
      // 'and fallthrough' is match syntax, not a binary expression
      if (Current().Type == TokenType.And && PeekNext().Type == TokenType.Fallthrough) break;

      var opToken = Advance(); // consume operator
      var rhs = ParseExpression(entry.Precedence + 1);

      MaxonValue lhsVal = ResolveExprValue(lhs);
      MaxonValue rhsVal = ResolveExprValue(rhs);

      // Struct equality/inequality via Equatable interface
      if (entry.Op is MaxonBinOperator.Eq or MaxonBinOperator.Ne
          && lhsVal is MaxonStruct lhsStruct && rhsVal is MaxonStruct rhsStruct
          && lhsStruct.TypeName == rhsStruct.TypeName) {
        if (_typeRegistry[lhsStruct.TypeName] is MlirStructType structType && structType.ConformingInterfaces.Contains("Equatable")) {
          var equalsMethodName = $"{lhsStruct.TypeName}.equals";
          var equalsToken = new Token(TokenType.Identifier, equalsMethodName, opToken.Line, opToken.Column);
          var callOp = CreateFunctionCall(equalsToken, [lhsVal, rhsVal]);
          if (entry.Op == MaxonBinOperator.Ne) {
            var trueOp = new MaxonLiteralOp(true);
            _currentBlock!.AddOp(trueOp);
            var xorOp = new MaxonBinOp(MaxonBinOperator.BitXor, callOp.Result!, trueOp.Result, MaxonValueKind.Bool);
            _currentBlock!.AddOp(xorOp);
            lhs = new ExprResult.Direct(xorOp.Result);
          } else {
            lhs = new ExprResult.Direct(callOp.Result!);
          }
          continue;
        }
      }

      // Struct ordering via Comparable interface
      if (entry.Op is MaxonBinOperator.Lt or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge
          && lhsVal is MaxonStruct lhsCmpStruct && rhsVal is MaxonStruct rhsCmpStruct
          && lhsCmpStruct.TypeName == rhsCmpStruct.TypeName) {
        if (_typeRegistry[lhsCmpStruct.TypeName] is MlirStructType cmpStructType && cmpStructType.ConformingInterfaces.Contains("Comparable")) {
          var compareMethodName = $"{lhsCmpStruct.TypeName}.compare";
          var compareToken = new Token(TokenType.Identifier, compareMethodName, opToken.Line, opToken.Column);
          var callOp = CreateFunctionCall(compareToken, [lhsVal, rhsVal]);
          var zeroLit = new MaxonLiteralOp(0L);
          _currentBlock!.AddOp(zeroLit);
          var cmpOp = new MaxonBinOp(entry.Op, callOp.Result!, zeroLit.Result, MaxonValueKind.Integer);
          _currentBlock!.AddOp(cmpOp);
          lhs = new ExprResult.Direct(cmpOp.Result);
          continue;
        }
      }

      MaxonValueKind kind;
      MaxonValue promotedLhs, promotedRhs;

      if (entry.Op is MaxonBinOperator.And or MaxonBinOperator.Or) {
        kind = MaxonValueKind.Bool;
        promotedLhs = lhsVal;
        promotedRhs = rhsVal;
      } else {
        (kind, promotedLhs, promotedRhs) = DetermineValueKind(lhsVal, rhsVal, entry.Op, opToken);

        // Enum comparisons use the backing kind (Integer or Float)
        if (kind == MaxonValueKind.Enum) {
          var cmpEnumTypeName = lhsVal is MaxonEnum le ? le.TypeName : ((MaxonEnum)rhsVal).TypeName;
          var cmpEnumType = (MlirEnumType)_typeRegistry[cmpEnumTypeName];
          if (cmpEnumType.HasAssociatedValues) {
            // For associated-value enums, compare tags only
            kind = MaxonValueKind.Integer;
            if (promotedLhs is MaxonEnum) {
              var lhsTag = new MaxonEnumTagOp(promotedLhs, cmpEnumTypeName);
              _currentBlock!.AddOp(lhsTag);
              promotedLhs = lhsTag.Result;
            }
            if (promotedRhs is MaxonEnum) {
              var rhsTag = new MaxonEnumTagOp(promotedRhs, cmpEnumTypeName);
              _currentBlock!.AddOp(rhsTag);
              promotedRhs = rhsTag.Result;
            }
          } else {
            kind = GetEnumBackingKind(cmpEnumType);
          }
        }

        // Division always produces a float result
        if (entry.Op == MaxonBinOperator.Div && kind == MaxonValueKind.Integer) {
          promotedLhs = PromoteIntToFloat(promotedLhs);
          promotedRhs = PromoteIntToFloat(promotedRhs);
          kind = MaxonValueKind.Float;
        }
      }

      var binOp = new MaxonBinOp(entry.Op, promotedLhs, promotedRhs, kind);
      _currentBlock!.AddOp(binOp);
      lhs = new ExprResult.Direct(binOp.Result);
    }

    return lhs;
  }

  private ExprResult ParsePrimary() {
    if (Check(TokenType.Match)) {
      return ParseMatchExpression();
    }

    if (Check(TokenType.Try)) {
      var tryToken = Advance(); // consume 'try'
      return ParseTryExpression(tryToken);
    }

    if (Check(TokenType.Minus)) {
      Advance(); // consume '-'
      if (Check(TokenType.IntegerLiteral))
        return EmitConstantLiteral(-ParseIntegerLiteral(Advance()));
      if (Check(TokenType.FloatLiteral))
        return EmitConstantLiteral(-ParseFloatLiteral(Advance()));

      var inner = ParsePrimary();
      var innerVal = ResolveExprValue(inner);
      var kind = DetermineValueKind(innerVal);
      var zeroOp = kind == MaxonValueKind.Float
        ? new MaxonLiteralOp(0.0)
        : new MaxonLiteralOp(0L);
      _currentBlock!.AddOp(zeroOp);
      var subOp = new MaxonBinOp(MaxonBinOperator.Sub, zeroOp.Result, innerVal, kind);
      _currentBlock!.AddOp(subOp);
      return new ExprResult.Direct(subOp.Result);
    }

    if (Check(TokenType.LeftBracket)) {
      var arrayResult = ParseArrayLiteral();
      // Handle chained member access: [1,2,3].get(0)
      if (Check(TokenType.Dot)) {
        var bracketToken = new Token(TokenType.LeftBracket, "[", Current().Line, Current().Column);
        return ParseFieldAccessChain(arrayResult, bracketToken);
      }
      return arrayResult;
    }

    if (Check(TokenType.LeftParen)) {
      // Need to distinguish between:
      // 1. Parenthesized expression: (expr)
      // 2. Closure: (param type, ...) gives expr
      if (IsClosure()) {
        return ParseClosure();
      }
      Advance(); // consume '('
      var inner = ParseExpression();
      Expect(TokenType.RightParen);
      return inner;
    }

    if (Check(TokenType.IntegerLiteral))
      return EmitConstantLiteral(ParseIntegerLiteral(Advance()));

    if (Check(TokenType.FloatLiteral))
      return EmitConstantLiteral(ParseFloatLiteral(Advance()));

    if (Check(TokenType.True) || Check(TokenType.False))
      return EmitConstantLiteral(Advance().Type == TokenType.True);

    if (Check(TokenType.StringLiteral) || Check(TokenType.StringInterp)) {
      var token = Advance();
      var result = EmitStringLiteralWithInterpolation(token);
      if (Check(TokenType.Dot)) {
        return ParseFieldAccessChain(new ExprResult.Direct(result), token);
      }
      return new ExprResult.Direct(result);
    }

    if (Check(TokenType.CharacterLiteral)) {
      var token = Advance();
      var result = EmitCharLiteral(token);
      if (Check(TokenType.Dot)) {
        return ParseFieldAccessChain(new ExprResult.Direct(result), token);
      }
      return new ExprResult.Direct(result);
    }

    if (Check(TokenType.Not)) {
      var token = Advance(); // consume 'not'
      var inner = ParsePrimary();
      var innerVal = ResolveExprValue(inner);
      // Implement 'not' as XOR with true (1)
      var trueOp = new MaxonLiteralOp(true);
      _currentBlock!.AddOp(trueOp);
      var xorOp = new MaxonBinOp(MaxonBinOperator.BitXor, innerVal, trueOp.Result, MaxonValueKind.Bool);
      _currentBlock!.AddOp(xorOp);
      return new ExprResult.Direct(xorOp.Result);
    }

    if (Check(TokenType.Self)) {
      var selfToken = Advance(); // consume 'self'
      if (!_variables.TryGetValue("self", out var selfVarInfo)) {
        throw new CompileError(ErrorCode.SemanticUnexpectedToken, "'self' can only be used inside instance methods", selfToken.Line, selfToken.Column);
      }
      _referencedVars.Add("self");
      return ParseFieldAccessChain(new ExprResult.VarRef("self", selfVarInfo), selfToken);
    }

    if (Check(TokenType.SelfType)) {
      var selfTypeToken = Advance(); // consume 'Self'
      if (_currentTypeName == null) {
        throw new CompileError(ErrorCode.ParserExpectedExpression, "'Self' can only be used inside a type declaration", selfTypeToken.Line, selfTypeToken.Column);
      }
      // Replace the SelfType token in the stream so the Identifier handler below processes it
      _tokens[_pos - 1] = new Token(TokenType.Identifier, _currentTypeName, selfTypeToken.Line, selfTypeToken.Column);
      _pos--; // back up so the Identifier check below picks it up
    }

    TryRewritePrimitiveStaticMethod();

    if (Check(TokenType.Identifier)) {
      var token = Advance();

      // Check for struct literal: TypeName{...} or TypeName { ... }
      if (_typeRegistry.ContainsKey(token.Value) && Check(TokenType.LeftBrace)) {
        return ParseStructLiteral(token.Value);
      }

      // Check for "TypeName from [...]" syntax (BuiltinArrayLiteral or InitableFromArrayLiteral)
      if (_typeRegistry.ContainsKey(token.Value) && Check(TokenType.From)) {
        return ParseFromExpression(token);
      }

      // Check for qualified name: TypeName.member
      if (Check(TokenType.Dot) && PeekNext().Type == TokenType.Identifier) {
        var qualifiedName = $"{token.Value}.{PeekNext().Value}";

        // Check for compile-time constant
        if (_topLevelConstants.TryGetValue(qualifiedName, out var constVal)) {
          Advance(); // consume '.'
          Advance(); // consume member name
          return EmitConstantLiteral(constVal);
        }

        // Check for global/static variable
        if (_globalVars.TryGetValue(qualifiedName, out var globalInfo)) {
          Advance(); // consume '.'
          Advance(); // consume member name
          var loadOp = new MaxonGlobalLoadOp(qualifiedName, globalInfo.Kind);
          _currentBlock!.AddOp(loadOp);
          return new ExprResult.Direct(loadOp.Result);
        }

        // Check for enum case: EnumType.caseName
        if (_typeRegistry.TryGetValue(token.Value, out var typeEntry) && typeEntry is MlirEnumType enumType) {
          var memberName = PeekNext().Value;
          var enumCase = enumType.GetCase(memberName);
          if (enumCase != null) {
            Advance(); // consume '.'
            Advance(); // consume case name

            // Associated value enum: use MaxonEnumConstructOp
            if (enumType.HasAssociatedValues) {
              var args = new List<MaxonValue>();
              if (enumCase.AssociatedValues is { Count: > 0 }) {
                // Case with associated values: parse arguments
                Expect(TokenType.LeftParen);
                for (int ai = 0; ai < enumCase.AssociatedValues.Count; ai++) {
                  if (ai > 0) Expect(TokenType.Comma);
                  var argExpr = ParseExpression();
                  var argVal = ResolveExprValue(argExpr);
                  // Type-check the argument
                  var expectedType = enumCase.AssociatedValues[ai].Type;
                  var actualKind = DetermineValueKind(argVal);
                  var expectedKind = expectedType.ToValueKind();
                  if (actualKind != expectedKind) {
                    // Determine source-level name for the actual type
                    var actualTypeName = argVal is MaxonStruct ms
                      ? ms.TypeName
                      : MlirType.FormatAsSourceName(actualKind.ToMlirType());
                    throw new CompileError(ErrorCode.SemanticTypeMismatch,
                      $"type mismatch: 'expected {MlirType.FormatAsSourceName(expectedType)}, got {actualTypeName}'",
                      Current().Line, Current().Column);
                  }
                  args.Add(argVal);
                }
                // Check for too many arguments
                if (Check(TokenType.Comma)) {
                  Advance();
                  // Count remaining extra args after the comma
                  int extraArgs = 0;
                  while (!Check(TokenType.RightParen) && !IsAtEnd()) {
                    ParseExpression();
                    extraArgs++;
                    if (Check(TokenType.Comma)) Advance();
                  }
                  throw new CompileError(ErrorCode.SemanticWrongArgCount,
                    $"wrong argument count: 'expected {enumCase.AssociatedValues.Count}, got {enumCase.AssociatedValues.Count + extraArgs}'",
                    token.Line, token.Column);
                }
                Expect(TokenType.RightParen);
              } else if (Check(TokenType.LeftParen)) {
                // Case without associated values but caller used parens - error
                throw new CompileError(ErrorCode.SemanticWrongArgCount,
                  $"wrong argument count: 'expected 0, got ...'",
                  token.Line, token.Column);
              }
              // Cases with no associated values and no parens: just tag, no payload
              var constructOp = new MaxonEnumConstructOp(token.Value, memberName, enumCase.Ordinal, args);
              _currentBlock!.AddOp(constructOp);
              return ParseFieldAccessChain(new ExprResult.Direct(constructOp.Result), token);
            }

            // Simple/raw-value enum: use MaxonEnumLiteralOp
            MaxonEnumLiteralOp enumLitOp;
            if (enumType.BackingType == MlirType.F64) {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (double)enumCase.RawValue!);
            } else if (enumType.BackingType == MlirType.I64) {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (long)enumCase.RawValue!);
            } else if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (long)enumCase.Ordinal);
            } else if (enumType.BackingType == null) {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (long)enumCase.Ordinal);
            } else {
              throw new InvalidOperationException($"Unsupported enum backing type: {enumType.BackingType}");
            }
            _currentBlock!.AddOp(enumLitOp);
            return ParseFieldAccessChain(new ExprResult.Direct(enumLitOp.Result), token);
          }
          // Not a valid case - but could be a static method call, so fall through
          // If it's not a method either, report unknown case
          if (_pos + 2 >= _tokens.Count || _tokens[_pos + 2].Type != TokenType.LeftParen) {
            Advance(); // consume '.'
            Advance(); // consume member name
            throw new CompileError(ErrorCode.SemanticEnumUnknownCase,
              $"unknown enum case: '{memberName}'", token.Line, token.Column);
          }

          // Check for fromRawValue / fromName static methods on enum types
          if (memberName is "fromRawValue" or "fromName") {
            return ParseEnumStaticMethod(enumType, memberName);
          }
        }

        // Check for qualified function call: TypeName.method(...)
        // _pos is at '.', _pos+1 is 'method', _pos+2 should be '('
        if (_pos + 2 < _tokens.Count && _tokens[_pos + 2].Type == TokenType.LeftParen) {
          // Find all matches (exact, suffix, or mangled overloads)
          var exactMatches = _currentModule!.Functions.Where(f => f.Name == qualifiedName || f.Name.StartsWith(qualifiedName + "$")).ToList();
          var suffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith($".{qualifiedName}") || f.Name.Contains($".{qualifiedName}$")).ToList();
          var totalMatches = exactMatches.Count + suffixMatches.Count;

          if (totalMatches > 0) {
            Advance(); // consume '.'
            var methodToken = Advance(); // consume method name
            Advance(); // consume '('
            var qualifiedFuncToken = new Token(TokenType.Identifier, qualifiedName, methodToken.Line, methodToken.Column);
            var (args, callee) = ParseCallArgs(qualifiedFuncToken);
            var callOp = CreateFunctionCall(qualifiedFuncToken, args, callee);
            if (callOp.Result != null)
              return new ExprResult.Direct(callOp.Result);
            if (_inTryContext)
              return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
            throw new CompileError(ErrorCode.ParserExpectedExpression, $"Function '{qualifiedName}' does not return a value", methodToken.Line, methodToken.Column);
          }
        }
      }

      // Check for function call: name(...)
      if (Check(TokenType.LeftParen)) {
        if (BuiltinOps1.TryGetValue(token.Value, out var makeOp1)) {
          Advance(); // consume '('
          var arg = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var (op, result) = makeOp1(arg);
          _currentBlock!.AddOp(op);
          return new ExprResult.Direct(result);
        }
        if (BuiltinOps2.TryGetValue(token.Value, out var makeOp2)) {
          Advance(); // consume '('
          var arg1 = ResolveExprValue(ParseExpression());
          Expect(TokenType.Comma);
          var arg2 = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var (op, result) = makeOp2(arg1, arg2);
          _currentBlock!.AddOp(op);
          return new ExprResult.Direct(result);
        }
        // Handle compiler builtins in expression context
        if (IsCompilerBuiltin(token.Value)) {
          Advance(); // consume '('
          var builtinResult = TryEmitManagedMemoryBuiltin(token);
          if (builtinResult != null)
            return new ExprResult.Direct(builtinResult);
          if (_inTryContext)
            return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
          throw new CompileError(ErrorCode.ParserExpectedExpression, $"Builtin '{token.Value}' does not return a value", token.Line, token.Column);
        }
        // Check for sibling method call: bare methodName() inside an instance method resolves to self.methodName()
        var siblingCallOp = TrySiblingMethodCall(token);
        if (siblingCallOp != null) {
          if (siblingCallOp.Result != null)
            return new ExprResult.Direct(siblingCallOp.Result);
          if (_inTryContext)
            return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
          throw new CompileError(ErrorCode.ParserExpectedExpression, $"Method '{token.Value}' does not return a value", token.Line, token.Column);
        }

        // Check for indirect call through function-typed variable
        if (_variables.TryGetValue(token.Value, out var fnVarInfo) && fnVarInfo.Kind == MaxonValueKind.Function) {
          Logger.Debug(LogCategory.Parser, $"  Indirect call through function variable: {token.Value}");
          _referencedVars.Add(token.Value);
          var fnType = fnVarInfo.FnTypeName!;
          Advance(); // consume '('
          var indirectArgs = ParseIndirectCallArgs(token, fnType);

          // Get the function pointer value
          MaxonValue calleeValue;
          if (fnVarInfo.DefinedInBlock == _currentBlock) {
            calleeValue = fnVarInfo.Value;
          } else {
            var fnVarRefOp = new MaxonFunctionVarRefOp(token.Value, fnType);
            _currentBlock!.AddOp(fnVarRefOp);
            calleeValue = fnVarRefOp.Result;
          }

          // Determine result kind from function type
          MaxonValueKind? resultKind = null;
          string? resultStructTypeName = null;
          if (fnType.ReturnType != null) {
            if (fnType.ReturnType is MlirStructType retStructType) {
              resultKind = MaxonValueKind.Struct;
              resultStructTypeName = retStructType.Name;
            } else if (fnType.ReturnType is MlirEnumType retEnumType) {
              resultKind = MaxonValueKind.Enum;
              resultStructTypeName = retEnumType.Name;
            } else {
              resultKind = fnType.ReturnType.ToValueKind();
            }
          }

          var indirectCallOp = new MaxonIndirectCallOp(calleeValue, fnType, indirectArgs, resultKind, resultStructTypeName);
          _currentBlock!.AddOp(indirectCallOp);
          if (indirectCallOp.Result != null)
            return new ExprResult.Direct(indirectCallOp.Result);
          if (_inTryContext)
            return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
          throw new CompileError(ErrorCode.ParserExpectedExpression, $"Function variable '{token.Value}' does not return a value", token.Line, token.Column);
        }

        Advance(); // consume '('
        var (args, callee) = ParseCallArgs(token);
        var callOp = CreateFunctionCall(token, args, callee);
        if (callOp.Result != null)
          return new ExprResult.Direct(callOp.Result);
        if (_inTryContext)
          return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
        throw new CompileError(ErrorCode.ParserExpectedExpression, $"Function '{token.Value}' does not return a value", token.Line, token.Column);
      }

      // Check for top-level constant reference
      if (_topLevelConstants.TryGetValue(token.Value, out var constValue)) {
        return EmitConstantLiteral(constValue);
      }

      // Check for global variable reference
      if (_globalVars.TryGetValue(token.Value, out var globalVarInfo)) {
        var loadOp = new MaxonGlobalLoadOp(token.Value, globalVarInfo.Kind);
        _currentBlock!.AddOp(loadOp);
        return new ExprResult.Direct(loadOp.Result);
      }

      // Variable reference
      if (_variables.TryGetValue(token.Value, out var varInfo)) {
        _referencedVars.Add(token.Value);
        return ParseFieldAccessChain(new ExprResult.VarRef(token.Value, varInfo), token);
      }

      // Function reference (bare function name without parentheses = function pointer)
      // Try to resolve as a function using the same logic as function calls
      // First check local namespace, then exact match, then suffix match
      var currentNamespace = DeriveNamespace();
      var qualifiedFuncName = string.IsNullOrEmpty(currentNamespace) ? token.Value : $"{currentNamespace}.{token.Value}";

      var referencedFunc = _currentModule!.Functions.FirstOrDefault(f => f.Name == qualifiedFuncName);
      referencedFunc ??= _currentModule!.Functions.FirstOrDefault(f => f.Name == token.Value);
      if (referencedFunc == null) {
        var suffixPattern = $".{token.Value}";
        var suffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith(suffixPattern)).ToList();
        if (suffixMatches.Count == 1) {
          referencedFunc = suffixMatches[0];
        } else if (suffixMatches.Count > 1) {
          var candidates = string.Join(", ", suffixMatches.Select(f => f.Name));
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Ambiguous function reference '{token.Value}': multiple candidates found: {candidates}. Use a qualified name to disambiguate.",
            token.Line, token.Column);
        }
      }

      if (referencedFunc != null) {
        var fnType = GetFunctionType(referencedFunc);
        var fnRefOp = new MaxonFunctionRefOp(referencedFunc.Name, fnType);
        _currentBlock!.AddOp(fnRefOp);
        return new ExprResult.Direct(fnRefOp.Result);
      }

      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{token.Value}'", token.Line, token.Column);
    }

    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got '{Current().Value}'", Current().Line, Current().Column);
  }

  private static MlirFunctionType GetFunctionType(MlirFunction<MaxonOp> func) {
    var paramTypes = func.ParamTypes.ToList();
    return new MlirFunctionType(paramTypes, func.ReturnType);
  }

  private ExprResult ParseFieldAccessChain(ExprResult result, Token originToken) {
    while (Check(TokenType.Dot)) {
      Advance(); // consume '.'
      var fieldToken = Expect(TokenType.Identifier);
      var fieldName = fieldToken.Value;

      string userTypeName;
      if (result is ExprResult.VarRef vr) {
        userTypeName = vr.Info.StructTypeName
          ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Variable '{vr.VarName}' is not a struct or enum type", originToken.Line, originToken.Column);
      } else if (result is ExprResult.Direct d && d.Value is MaxonStruct ms) {
        userTypeName = ms.TypeName;
      } else if (result is ExprResult.Direct d2 && d2.Value is MaxonEnum me) {
        userTypeName = me.TypeName;
      } else {
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, "Cannot access field on non-struct value", fieldToken.Line, fieldToken.Column);
      }

      var registeredType = _typeRegistry[userTypeName];

      // Handle enum-specific access
      if (registeredType is MlirEnumType enumType) {
        // .name access - returns case name as String
        if (fieldName == "name") {
          var enumVal = ResolveExprValue(result);
          var nameOp = new MaxonEnumNameOp(enumVal, userTypeName);
          _currentBlock!.AddOp(nameOp);
          result = new ExprResult.Direct(nameOp.Result);
          continue;
        }

        // .rawValue access
        if (fieldName == "rawValue") {
          var enumVal = ResolveExprValue(result);
          if (enumType.BackingType is MlirStringBackingType) {
            var stringRawOp = new MaxonEnumStringRawValueOp(enumVal, userTypeName, isChar: false);
            _currentBlock!.AddOp(stringRawOp);
            result = new ExprResult.Direct(stringRawOp.Result);
            continue;
          }
          if (enumType.BackingType is MlirCharBackingType) {
            var charRawOp = new MaxonEnumStringRawValueOp(enumVal, userTypeName, isChar: true);
            _currentBlock!.AddOp(charRawOp);
            result = new ExprResult.Direct(charRawOp.Result);
            continue;
          }
          var resultKind = GetEnumBackingKind(enumType);
          var rawValueOp = new MaxonEnumRawValueOp(enumVal, userTypeName, resultKind);
          _currentBlock!.AddOp(rawValueOp);
          result = new ExprResult.Direct(rawValueOp.Result);
          continue;
        }

        // Method call on enum
        if (Check(TokenType.LeftParen)) {
          var qualifiedMethodName = $"{userTypeName}.{fieldName}";
          var hasMethod = _currentModule!.Functions.Any(f => f.Name == qualifiedMethodName || f.Name.StartsWith(qualifiedMethodName + "$"));
          if (hasMethod) {
            Advance(); // consume '('
            var enumVal = ResolveExprValue(result);
            var qualifiedFuncToken = new Token(TokenType.Identifier, qualifiedMethodName, fieldToken.Line, fieldToken.Column);
            var (allArgs, enumCallee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, enumVal);
            var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs, enumCallee);
            result = new ExprResult.Direct(callOp.Result ?? enumVal);
            continue;
          }
        }

        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Enum type '{userTypeName}' has no property or method named '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      // Type parameter: only method calls are allowed, resolved through interface definitions
      if (registeredType is MlirTypeParameterType) {
        if (Check(TokenType.LeftParen)) {
          var qualifiedMethodName = $"{userTypeName}.{fieldName}";
          var ifaceMethod = FindInterfaceMethod(fieldName);
          if (ifaceMethod != null) {
            Advance(); // consume '('
            var selfVal = ResolveExprValue(result);
            var args = new List<MaxonValue> { selfVal };
            if (!Check(TokenType.RightParen)) {
              while (true) {
                if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
                  Advance(); // consume label
                  Advance(); // consume ':'
                }
                args.Add(ResolveExprValue(ParseExpression()));
                if (!Check(TokenType.Comma)) break;
                Advance(); // consume ','
              }
            }
            Expect(TokenType.RightParen);
            var resultKind = ifaceMethod.ReturnTypeName switch {
              "int" => MaxonValueKind.Integer,
              "float" => MaxonValueKind.Float,
              "bool" => MaxonValueKind.Bool,
              "byte" => MaxonValueKind.Byte,
              null => (MaxonValueKind?)null,
              { } name when IsAssociatedTypeName(name) => MaxonValueKind.TypeParameter,
              { } name => throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Unsupported return type '{name}' in interface method '{fieldName}'", fieldToken.Line, fieldToken.Column)
            };
            var callOp = new MaxonCallOp(qualifiedMethodName, args, resultKind, null);
            _currentBlock!.AddOp(callOp);
            result = new ExprResult.Direct(callOp.Result ?? selfVal);
            continue;
          }
        }
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type parameter '{userTypeName}' has no method named '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      var structType = (MlirStructType)registeredType;

      // Check for method call: expr.method(...)
      if (Check(TokenType.LeftParen)) {
        var qualifiedMethodName = $"{userTypeName}.{fieldName}";
        Logger.Debug(LogCategory.Parser, $"Resolving method: {qualifiedMethodName}");
        var resolvedMethodName = ResolveMethodName(qualifiedMethodName);
        Logger.Debug(LogCategory.Parser, $"Resolved to: {resolvedMethodName}");
        if (resolvedMethodName != null) {
          Advance(); // consume '('
          var structVal = ResolveExprValue(result);
          var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedMethodName, fieldToken.Line, fieldToken.Column);
          var (allArgs, structCallee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, structVal);
          var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs, structCallee);
          result = new ExprResult.Direct(callOp.Result ?? structVal);
          continue;
        }

      }

      var field = structType.GetField(fieldName)
        ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type '{userTypeName}' has no field named '{fieldName}'", fieldToken.Line, fieldToken.Column);

      if (!field.IsExported && _currentTypeName != userTypeName) {
        throw new CompileError(ErrorCode.SemanticUnexportedFieldAccess, $"cannot access unexported field: '{fieldName}' outside of type '{userTypeName}'", fieldToken.Line, fieldToken.Column);
      }

      var fieldKind = field.Type.ToValueKind();
      var fieldStructName = field.Type is MlirStructType fst ? fst.Name
        : field.Type is MlirEnumType fet ? fet.Name
        : null;
      var structVal2 = ResolveExprValue(result);
      var accessOp = new MaxonFieldAccessOp(structVal2, userTypeName, fieldName, fieldKind, fieldStructName);
      _currentBlock!.AddOp(accessOp);
      result = new ExprResult.Direct(accessOp.Result);
    }

    return result;
  }

  private MaxonStruct EmitStringLiteralWithInterpolation(Token token) {
    var stringTypeName = FindTypeImplementingInterface("BuiltinStringLiteral") ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
        "No type implements BuiltinStringLiteral (String type not found in stdlib)",
        token.Line, token.Column);

    var parts = new List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec)>();
    var text = token.Value;
    var pos = 0;
    var literalBuf = new System.Text.StringBuilder();

    while (pos < text.Length) {
      if (text[pos] == '\\' && pos + 1 < text.Length) {
        var nextChar = text[pos + 1];
        if (nextChar is '{' or '}') {
          literalBuf.Append(nextChar);
          pos += 2;
        } else {
          try { literalBuf.Append(StringUtils.ResolveEscapes($"\\{nextChar}")); } catch (InvalidEscapeException ex) {
            throw new CompileError(ErrorCode.LexerInvalidEscape,
                $"{ex.Message} in string interpolation", token.Line, token.Column + pos);
          }
          pos += 2;
        }
      } else if (text[pos] == '{') {
        if (literalBuf.Length > 0) {
          parts.Add((true, literalBuf.ToString(), null, null));
          literalBuf.Clear();
        }
        pos++; // skip '{'
        var exprStart = pos;
        var braceDepth = 1;
        while (pos < text.Length && braceDepth > 0) {
          if (text[pos] == '{') braceDepth++;
          else if (text[pos] == '}') braceDepth--;
          if (braceDepth > 0) pos++;
        }
        var exprText = text[exprStart..pos];
        if (pos < text.Length) pos++; // skip closing '}'

        // Split on ':' for format specifier (e.g., {value:verbose})
        string? formatSpec = null;
        var colonIdx = exprText.IndexOf(':');
        if (colonIdx >= 0) {
          formatSpec = exprText[(colonIdx + 1)..];
          exprText = exprText[..colonIdx];
        }

        var exprValue = ParseInterpolationExpression(exprText);
        parts.Add((false, null, exprValue, formatSpec));
      } else {
        literalBuf.Append(text[pos]);
        pos++;
      }
    }

    if (literalBuf.Length > 0) {
      parts.Add((true, literalBuf.ToString(), null, null));
    }

    // If no expression parts, emit a regular string literal with escaped content
    if (parts.All(p => p.IsLiteral)) {
      var fullText = string.Concat(parts.Where(p => p.IsLiteral).Select(p => p.LiteralValue));
      var op = new MaxonStringLiteralOp(fullText, stringTypeName);
      _currentBlock!.AddOp(op);
      return op.Result;
    }

    var interpOp = new MaxonStringInterpOp(parts, stringTypeName);
    _currentBlock!.AddOp(interpOp);
    return interpOp.Result;
  }

  private MaxonValue ParseInterpolationExpression(string exprText) {
    var lexer = new Lexer(exprText);
    var exprTokens = lexer.Tokenize();

    // Save and swap parser token state
    var savedTokens = new List<Token>(_tokens);
    var savedPos = _pos;

    _tokens.Clear();
    _tokens.AddRange(exprTokens);
    _pos = 0;

    try {
      var exprResult = ParseExpression();
      return ResolveExprValue(exprResult);
    } finally {
      _tokens.Clear();
      _tokens.AddRange(savedTokens);
      _pos = savedPos;
    }
  }

  private MaxonStruct EmitCharLiteral(Token token) {
    var charTypeName = FindTypeImplementingInterface("BuiltinCharLiteral") ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
        "No type implements BuiltinCharLiteral (Character type not found in stdlib)",
        token.Line, token.Column);
    string value;
    try { value = StringUtils.ResolveEscapes(token.Value); } catch (InvalidEscapeException ex) {
      throw new CompileError(ErrorCode.LexerInvalidEscape,
          $"{ex.Message} in character literal", token.Line, token.Column);
    }
    var op = new MaxonCharLiteralOp(value, charTypeName);
    _currentBlock!.AddOp(op);
    return op.Result;
  }


  private string? FindTypeImplementingInterface(string interfaceName) {
    foreach (var (name, type) in _typeRegistry) {
      if (type is MlirStructType structType && structType.ConformingInterfaces.Contains(interfaceName))
        return name;
    }
    return null;
  }

  private ExprResult.Direct EmitConstantLiteral(object constValue) {
    if (constValue is EnumConstantValue ec) {
      var enumType = (MlirEnumType)_typeRegistry[ec.EnumTypeName];
      var enumCase = enumType.GetCase(ec.CaseName)!;
      MaxonEnumLiteralOp enumLitOp;
      if (enumType.BackingType == MlirType.F64) {
        enumLitOp = new MaxonEnumLiteralOp(ec.EnumTypeName, ec.CaseName, (double)enumCase.RawValue!);
      } else if (enumType.BackingType == MlirType.I64) {
        enumLitOp = new MaxonEnumLiteralOp(ec.EnumTypeName, ec.CaseName, (long)enumCase.RawValue!);
      } else if (enumType.BackingType == null || enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
        enumLitOp = new MaxonEnumLiteralOp(ec.EnumTypeName, ec.CaseName, (long)enumCase.Ordinal);
      } else {
        throw new InvalidOperationException($"Unsupported enum backing type for constant: {enumType.BackingType}");
      }
      _currentBlock!.AddOp(enumLitOp);
      return new ExprResult.Direct(enumLitOp.Result);
    }
    var op = constValue switch {
      long v => new MaxonLiteralOp(v),
      double v => new MaxonLiteralOp(v),
      bool v => new MaxonLiteralOp(v),
      _ => throw new InvalidOperationException($"Unsupported constant type: {constValue.GetType().Name}")
    };
    _currentBlock!.AddOp(op);
    return new ExprResult.Direct(op.Result);
  }

  /// <summary>
  /// Parse a struct literal: {field: value, ...} or TypeName{field: value, ...}
  /// The opening '{' has NOT been consumed yet.
  /// </summary>
  /// <summary>
  /// Parses an array literal: [expr, expr, ...].
  /// Creates a stack-allocated __ManagedMemory struct and wraps it in an Array struct.
  /// </summary>
  private ExprResult.Direct ParseArrayLiteral() {
    var (managedStruct, arrayTag, elementCount, elementKind, elementStructTypeName) = EmitArrayLiteralElements();

    // Find the appropriate typealias for this element type
    // e.g., byte -> ByteArray, int -> IntArray, Pair -> __Array_Pair
    var arrayTypeName = FindArrayTypeAliasForElement(elementKind, elementStructTypeName);

    // Create Array struct: {iterIndex: 0, managed: <managed>}
    var iterIndexLit = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(iterIndexLit);

    var arrayFields = new List<(string Name, MaxonValue Value)> {
      ("iterIndex", iterIndexLit.Result),
      ("managed", managedStruct.Result)
    };
    var arrayStruct = new MaxonStructLiteralOp(arrayTypeName, arrayFields);
    _currentBlock!.AddOp(arrayStruct);

    // Record the element variable names for lowering to resolve the buffer
    arrayStruct.ArrayLiteralTag = arrayTag;
    arrayStruct.ArrayLiteralCount = elementCount;

    return new ExprResult.Direct(arrayStruct.Result);
  }

  /// <summary>
  /// Finds the typealias name for Array with the given element type.
  /// Returns the concrete alias (e.g., "ByteArray") if one exists, otherwise "Array".
  /// For struct element types, auto-creates a type alias if none exists.
  /// </summary>
  private string FindArrayTypeAliasForElement(MaxonValueKind elementKind, string? elementStructTypeName = null) {
    // For struct elements, search for existing alias or auto-create one
    if (elementKind == MaxonValueKind.Struct && elementStructTypeName != null) {
      // Search for an existing typealias of Array with this struct Element type
      foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
        if (sourceTypeName != "Array") continue;
        if (_typeRegistry.TryGetValue(aliasName, out var aliasType)
            && aliasType is MlirStructType st
            && st.TypeParams.TryGetValue("Element", out var elemType)
            && elemType.Name == elementStructTypeName) {
          return aliasName;
        }
      }

      // No existing alias - auto-create one (e.g., __Array_Pair)
      var autoAliasName = $"__Array_{elementStructTypeName}";
      if (!_typeRegistry.ContainsKey(autoAliasName)
          && _typeRegistry.TryGetValue("Array", out var arrayType)
          && arrayType is MlirStructType arrayStruct
          && _typeRegistry.TryGetValue(elementStructTypeName, out var elemStructType)) {
        var substitution = new Dictionary<string, MlirType> { ["Element"] = elemStructType };
        RegisterConcreteTypeAlias(autoAliasName, "Array", arrayStruct, substitution);
      }
      return autoAliasName;
    }

    // Map element kind to the expected Element type name
    var elementTypeName = elementKind switch {
      MaxonValueKind.Integer => "i64",
      MaxonValueKind.Float => "f64",
      MaxonValueKind.Byte => "i8",
      MaxonValueKind.Bool => "i1",
      MaxonValueKind.Enum => null,
      MaxonValueKind.Function => null,
      MaxonValueKind.Struct => throw new InvalidOperationException("Struct element kind should have been handled above"),
      _ => throw new InvalidOperationException($"Unhandled element kind: {elementKind}")
    };

    if (elementTypeName == null) return "Array";

    // Search for a typealias of Array that has this Element type
    foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
      if (sourceTypeName != "Array") continue;
      // Check if this alias has the matching Element type parameter
      if (_typeRegistry.TryGetValue(aliasName, out var aliasType)
          && aliasType is MlirStructType st
          && st.TypeParams.TryGetValue("Element", out var elemType)
          && elemType.Name == elementTypeName) {
        return aliasName;
      }
    }

    return "Array";
  }

  /// <summary>
  /// Parses [expr, expr, ...] and emits element assigns + __ManagedMemory struct.
  /// Returns the managed memory struct op, the element count, and the element kind.
  /// </summary>
  private (MaxonStructLiteralOp ManagedStruct, string ArrayTag, int ElementCount, MaxonValueKind ElementKind, string? ElementStructTypeName) EmitArrayLiteralElements() {
    Advance(); // consume '['

    // Parse element expressions
    var elements = new List<MaxonValue>();
    if (!Check(TokenType.RightBracket)) {
      elements.Add(ResolveExprValue(ParseExpression()));
      while (Check(TokenType.Comma)) {
        Advance();
        if (Check(TokenType.RightBracket)) break; // trailing comma
        elements.Add(ResolveExprValue(ParseExpression()));
      }
    }
    Expect(TokenType.RightBracket);

    int count = elements.Count;

    if (count == 0) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        "Empty array literals are not supported; use a typed empty array like 'IntArray{}' instead",
        Current().Line, Current().Column);
    }

    // Determine element type from first element (all elements must have same type)
    var elementKind = GetValueKind(elements[0]);

    // Determine element size - for structs, look up the actual type size
    int elementSize;
    string? elementStructTypeName = null;
    if (elements[0] is MaxonStruct structElem) {
      elementStructTypeName = structElem.TypeName;
      if (_typeRegistry.TryGetValue(structElem.TypeName, out var structType)) {
        elementSize = structType.ElementSize;
      } else {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot determine element size: unknown struct type '{structElem.TypeName}'",
          Current().Line, Current().Column);
      }
    } else {
      elementSize = elementKind.ElementSize();
    }

    // Store elements in reverse order so element 0 ends up at the lowest stack address.
    // Stack grows downward: first stored → highest address, last stored → lowest address.
    // LEA finds the lowest address, so element 0 must be stored last.
    var arrayTag = $"__arr_{_blockCounter++}";
    for (int i = count - 1; i >= 0; i--) {
      var elemVarName = $"{arrayTag}.{i}";
      var assignOp = new MaxonAssignOp(elemVarName, elements[i], isDeclaration: true, isMutable: false, elementKind);
      _currentBlock!.AddOp(assignOp);
    }

    // Create __ManagedMemory struct: {buffer: <tag_id>, length: count, capacity: 0, element_size}
    // buffer is represented as the array tag identifier (resolved during lowering)
    var bufLit = new MaxonLiteralOp(0L); // buffer pointer placeholder (0 = will be set up during lowering)
    _currentBlock!.AddOp(bufLit);
    var lenLit = new MaxonLiteralOp((long)count);
    _currentBlock!.AddOp(lenLit);
    var capLit = new MaxonLiteralOp(0L); // capacity = 0 means stack-allocated
    _currentBlock!.AddOp(capLit);
    var elemSizeLit = new MaxonLiteralOp((long)elementSize);
    _currentBlock!.AddOp(elemSizeLit);

    var managedFields = new List<(string Name, MaxonValue Value)> {
      ("buffer", bufLit.Result),
      ("length", lenLit.Result),
      ("capacity", capLit.Result),
      ("element_size", elemSizeLit.Result)
    };
    var managedStruct = new MaxonStructLiteralOp("__ManagedMemory", managedFields);
    _currentBlock!.AddOp(managedStruct);

    return (managedStruct, arrayTag, count, elementKind, elementStructTypeName);
  }

  private ExprResult.Direct ParseStructLiteral(string typeName) {
    Advance(); // consume '{'
    var structType = (MlirStructType)_typeRegistry[typeName];
    var fieldValues = new List<(string, MaxonValue)>();
    var providedFields = new HashSet<string>();

    // Track array literal tag if this is a Vector-like type with __capacity
    string? arrayLiteralTag = null;
    int arrayLiteralCount = 0;

    SkipNewlines();
    if (!Check(TokenType.RightBrace)) {
      do {
        SkipNewlines();
        var fieldNameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Colon);
        var value = ResolveExprValue(ParseExpression());
        fieldValues.Add((fieldNameToken.Value, value));
        providedFields.Add(fieldNameToken.Value);
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    SkipNewlines();

    // Fill in defaults for unspecified fields
    foreach (var field in structType.Fields) {
      if (!providedFields.Contains(field.Name)) {
        if (field.DefaultValue == null) {
          // Special case: Vector-like type with __capacity and managed __ManagedMemory field
          if (field.Name == "managed" && field.Type.Name == "__ManagedMemory" &&
              structType.ConstParams.TryGetValue("__capacity", out var capacity)) {
            // Determine element type from struct's type parameters
            if (!structType.TypeParams.TryGetValue("Element", out var elemType)) {
              throw new CompileError(ErrorCode.SemanticTypeMismatch,
                $"Cannot determine element size for '{structType.Name}': no Element type parameter",
                Current().Line, Current().Column);
            }
            var elemKind = elemType.ToValueKind();
            int elemSize = elemType.ElementSize;

            // Create N zero-valued elements on the stack (in reverse order for proper memory layout)
            arrayLiteralTag = $"__arr_{_blockCounter++}";
            arrayLiteralCount = (int)capacity;
            for (int i = arrayLiteralCount - 1; i >= 0; i--) {
              var zeroVal = new MaxonLiteralOp(0L);
              _currentBlock!.AddOp(zeroVal);
              var elemVarName = $"{arrayLiteralTag}.{i}";
              var assignOp = new MaxonAssignOp(elemVarName, zeroVal.Result, isDeclaration: true, isMutable: false, elemKind);
              _currentBlock!.AddOp(assignOp);
            }

            // Create __ManagedMemory struct with stack-allocated buffer
            var bufLit = new MaxonLiteralOp(0L); // buffer pointer placeholder
            _currentBlock!.AddOp(bufLit);
            var lenLit = new MaxonLiteralOp(capacity);
            _currentBlock!.AddOp(lenLit);
            var capLit = new MaxonLiteralOp(0L); // capacity = 0 means stack-allocated
            _currentBlock!.AddOp(capLit);
            var elemSizeLit = new MaxonLiteralOp((long)elemSize);
            _currentBlock!.AddOp(elemSizeLit);

            var managedFields = new List<(string Name, MaxonValue Value)> {
              ("buffer", bufLit.Result),
              ("length", lenLit.Result),
              ("capacity", capLit.Result),
              ("element_size", elemSizeLit.Result)
            };
            var managedStruct = new MaxonStructLiteralOp("__ManagedMemory", managedFields);
            _currentBlock!.AddOp(managedStruct);
            fieldValues.Add((field.Name, managedStruct.Result));
          } else if (field.Type is MlirStructType fieldStructType) {
            // For struct-typed fields, emit a zero-initialized struct literal (recursively for nested structs)
            var zeroResult = EmitZeroStructLiteral(fieldStructType, structType.TypeParams);
            fieldValues.Add((field.Name, zeroResult));
          } else {
            throw new CompileError(ErrorCode.ParserExpectedExpression,
              $"Field '{field.Name}' of type '{typeName}' requires a value (no default defined)",
              Current().Line, Current().Column);
          }
        } else {
          var errorToken = new Token(TokenType.Identifier, field.Name, Current().Line, Current().Column);
          var defaultValue = EmitDefaultLiteral(field.DefaultValue, field.Type, errorToken,
            $"Unsupported default value type for field '{field.Name}'");
          fieldValues.Add((field.Name, defaultValue));
        }
      }
    }

    Expect(TokenType.RightBrace);

    var structLiteral = new MaxonStructLiteralOp(typeName, fieldValues);
    // If this is a Vector-like type, set the array literal tag for lowering
    if (arrayLiteralTag != null) {
      structLiteral.ArrayLiteralTag = arrayLiteralTag;
      structLiteral.ArrayLiteralCount = arrayLiteralCount;
    }
    _currentBlock!.AddOp(structLiteral);
    return new ExprResult.Direct(structLiteral.Result);
  }

  /// Recursively creates a zero-initialized struct literal, handling nested struct fields.
  /// For __ManagedMemory, element_size is determined from the parent's Element type parameter.
  private MaxonStruct EmitZeroStructLiteral(MlirStructType structType, Dictionary<string, MlirType>? parentTypeParams = null) {
    // Merge type params: use struct's own, resolving type parameters through parent's.
    // Unresolved type parameters (no parent to resolve through) are dropped.
    var typeParams = new Dictionary<string, MlirType>();
    var source = structType.TypeParams.Count > 0 ? structType.TypeParams : parentTypeParams ?? [];
    foreach (var (key, value) in source) {
      if (value is MlirTypeParameterType tp) {
        if (parentTypeParams != null && parentTypeParams.TryGetValue(tp.ParameterName, out var resolved)
            && resolved is not MlirTypeParameterType) {
          typeParams[key] = resolved;
        }
      } else {
        typeParams[key] = value;
      }
    }

    var zeroFields = new List<(string Name, MaxonValue Value)>();
    foreach (var subField in structType.Fields) {
      if (subField.Type is MlirStructType nestedType) {
        var nestedResult = EmitZeroStructLiteral(nestedType, typeParams);
        zeroFields.Add((subField.Name, nestedResult));
      } else {
        long value = 0L;
        // For __ManagedMemory.element_size, determine from Element type parameter
        if (structType.Name == "__ManagedMemory" && subField.Name == "element_size") {
          if (typeParams.TryGetValue("Element", out var elemType)) {
            value = elemType.ElementSize;
          }
        }
        var lit = new MaxonLiteralOp(value);
        _currentBlock!.AddOp(lit);
        zeroFields.Add((subField.Name, lit.Result));
      }
    }
    var zeroStruct = new MaxonStructLiteralOp(structType.Name, zeroFields);
    _currentBlock!.AddOp(zeroStruct);
    return zeroStruct.Result;
  }

  /// <summary>
  /// Parse "TypeName from literal" syntax.
  /// Supports array literals ([...]), string literals ("..."), and char literals ('...').
  /// For arrays: checks BuiltinArrayLiteral first, falls back to InitableFromArrayLiteral.
  /// For strings: checks InitableFromStringLiteral, creates String and passes to init().
  /// For chars: checks InitableFromCharLiteral, creates Character and passes to init().
  /// For types with associated types and __capacity, auto-creates a concrete alias type.
  /// </summary>
  private ExprResult.Direct ParseFromExpression(Token typeToken) {
    Advance(); // consume 'from'
    var typeName = typeToken.Value;

    // Look up the type
    if (!_typeRegistry.TryGetValue(typeName, out var registeredType))
      throw new CompileError(ErrorCode.ParserExpectedType,
        $"Unknown type '{typeName}'", typeToken.Line, typeToken.Column);
    if (registeredType is not MlirStructType sourceStruct)
      throw new CompileError(ErrorCode.ParserExpectedType,
        $"Type '{typeName}' is not a struct type", typeToken.Line, typeToken.Column);

    // Handle string literal: TypeName from "..."
    if (Check(TokenType.StringLiteral)) {
      return ParseFromStringLiteral(typeToken, typeName, sourceStruct);
    }

    // Handle char literal: TypeName from '...'
    if (Check(TokenType.CharacterLiteral)) {
      return ParseFromCharLiteral(typeToken, typeName, sourceStruct);
    }

    // Handle array literal: TypeName from [...]
    if (!Check(TokenType.LeftBracket)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression,
        "Expected literal after 'from' (string, character, or array)", Current().Line, Current().Column);
    }

    bool isBuiltinArrayLiteral = sourceStruct.ConformingInterfaces.Contains("BuiltinArrayLiteral");
    bool isInitableFromArrayLiteral = sourceStruct.ConformingInterfaces.Contains("InitableFromArrayLiteral");

    if (!isBuiltinArrayLiteral && !isInitableFromArrayLiteral)
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Type '{typeName}' does not conform to BuiltinArrayLiteral or InitableFromArrayLiteral",
        typeToken.Line, typeToken.Column);

    // Parse elements — both paths start the same way
    var (managedStruct, arrayTag, elementCount, _, _) = EmitArrayLiteralElements();

    // The init argument differs: BuiltinArrayLiteral gets __ManagedMemory, InitableFromArrayLiteral gets Array
    MaxonValue initArg;
    if (isBuiltinArrayLiteral) {
      // Pass __ManagedMemory directly
      managedStruct.ArrayLiteralTag = arrayTag;
      managedStruct.ArrayLiteralCount = elementCount;
      initArg = managedStruct.Result;
    } else {
      // Wrap in Array struct for InitableFromArrayLiteral
      var iterIndexLit = new MaxonLiteralOp(0L);
      _currentBlock!.AddOp(iterIndexLit);

      var arrayFields = new List<(string Name, MaxonValue Value)> {
        ("iterIndex", iterIndexLit.Result),
        ("managed", managedStruct.Result)
      };
      var arrayStruct = new MaxonStructLiteralOp("Array", arrayFields);
      _currentBlock!.AddOp(arrayStruct);

      arrayStruct.ArrayLiteralTag = arrayTag;
      arrayStruct.ArrayLiteralCount = elementCount;
      initArg = arrayStruct.Result;
    }

    // For types with associated types, auto-create a concrete alias with __capacity
    string concreteTypeName = typeName;
    if (sourceStruct.AssociatedTypeNames.Count > 0 && elementCount > 0) {
      var elementType = InferArrayLiteralElementType(arrayTag);

      concreteTypeName = $"__{typeName}_{elementCount}_{elementType.Name}";
      if (!_typeRegistry.ContainsKey(concreteTypeName)) {
        var substitution = new Dictionary<string, MlirType>();
        foreach (var assocName in sourceStruct.AssociatedTypeNames) {
          substitution[assocName] = elementType;
        }
        RegisterConcreteTypeAlias(concreteTypeName, typeName, sourceStruct, substitution,
          new Dictionary<string, long> { ["__capacity"] = elementCount });
      }
    }

    // Look up the init method: TypeName.init or the source type's init
    var sourceTypeName = _typeAliasSources.TryGetValue(concreteTypeName, out var src) ? src : concreteTypeName;
    var initMethodName = $"{sourceTypeName}.init";

    var resolvedInitName = ResolveMethodName(initMethodName);
    var initFunc = resolvedInitName != null
      ? _currentModule!.Functions.FirstOrDefault(f => f.Name == resolvedInitName)
        ?? _currentModule!.Functions.FirstOrDefault(f => UnmangleName(f.Name) == resolvedInitName)
        ?? throw new CompileError(ErrorCode.SemanticUndefinedFunction,
            $"Type '{typeName}' does not have a valid init method (no '{initMethodName}' found)",
            typeToken.Line, typeToken.Column)
      : throw new CompileError(ErrorCode.SemanticUndefinedFunction,
          $"Type '{typeName}' does not have a valid init method (no '{initMethodName}' found)",
          typeToken.Line, typeToken.Column);

    // Determine return type - init returns Self which is the struct type
    MaxonValueKind? resultKind = MaxonValueKind.Struct;
    string resultStructTypeName = concreteTypeName;

    // Create the call to init using the resolved function name (may be mangled)
    var callOp = new MaxonCallOp(initFunc.Name, [initArg], resultKind, resultStructTypeName);
    _currentBlock!.AddOp(callOp);

    if (callOp.Result == null) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"'{initMethodName}' must return a value", typeToken.Line, typeToken.Column);
    }

    return new ExprResult.Direct(callOp.Result);
  }

  /// <summary>
  /// Parse "TypeName from "..."" for types conforming to InitableFromStringLiteral.
  /// Creates a String from the literal and passes it to TypeName.init(value String).
  /// </summary>
  private ExprResult.Direct ParseFromStringLiteral(Token typeToken, string typeName, MlirStructType sourceStruct) {
    if (!sourceStruct.ConformingInterfaces.Contains("InitableFromStringLiteral"))
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Type '{typeName}' does not conform to InitableFromStringLiteral",
        typeToken.Line, typeToken.Column);

    var stringToken = Advance(); // consume the string literal
    var stringStruct = EmitStringLiteralWithInterpolation(stringToken);

    return EmitFromLiteralInitCall(typeToken, typeName, stringStruct);
  }

  /// <summary>
  /// Parse "TypeName from '...'" for types conforming to InitableFromCharLiteral.
  /// Creates a Character from the literal and passes it to TypeName.init(value Character).
  /// </summary>
  private ExprResult.Direct ParseFromCharLiteral(Token typeToken, string typeName, MlirStructType sourceStruct) {
    if (!sourceStruct.ConformingInterfaces.Contains("InitableFromCharLiteral"))
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Type '{typeName}' does not conform to InitableFromCharLiteral",
        typeToken.Line, typeToken.Column);

    var charToken = Advance(); // consume the char literal
    var charStruct = EmitCharLiteral(charToken);

    return EmitFromLiteralInitCall(typeToken, typeName, charStruct);
  }

  /// <summary>
  /// Emit the init() call for "TypeName from literal" expressions.
  /// Looks up TypeName.init and calls it with the provided literal value.
  /// </summary>
  private ExprResult.Direct EmitFromLiteralInitCall(Token typeToken, string typeName, MaxonStruct literalValue) {
    var initMethodName = $"{typeName}.init";
    var resolvedInitName = ResolveMethodName(initMethodName);
    var initFunc = resolvedInitName != null
      ? _currentModule!.Functions.FirstOrDefault(f => f.Name == resolvedInitName)
        ?? _currentModule!.Functions.FirstOrDefault(f => UnmangleName(f.Name) == resolvedInitName)
        ?? throw new CompileError(ErrorCode.SemanticUndefinedFunction,
            $"Type '{typeName}' does not have a valid init method",
            typeToken.Line, typeToken.Column)
      : throw new CompileError(ErrorCode.SemanticUndefinedFunction,
          $"Type '{typeName}' does not have a valid init method",
          typeToken.Line, typeToken.Column);

    var callOp = new MaxonCallOp(initFunc.Name, [literalValue], MaxonValueKind.Struct, typeName);
    _currentBlock!.AddOp(callOp);

    if (callOp.Result == null) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"'{initMethodName}' must return a value", typeToken.Line, typeToken.Column);
    }

    return new ExprResult.Direct(callOp.Result);
  }

  /// <summary>
  /// Infer the element type of an array literal from its element assignments.
  /// </summary>
  private MlirType InferArrayLiteralElementType(string arrayTag) {
    var elemAssign = _currentBlock!.Operations.OfType<MaxonAssignOp>()
      .FirstOrDefault(op => op.VarName.StartsWith($"{arrayTag}.")) ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
        "Cannot infer element type: no element assignments found in array literal",
        Current().Line, Current().Column);

    return elemAssign.ValueKind switch {
      MaxonValueKind.Integer => MlirType.I64,
      MaxonValueKind.Float => MlirType.F64,
      MaxonValueKind.Bool => MlirType.I1,
      MaxonValueKind.Byte => MlirType.I8,
      _ => throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Cannot infer element type from value kind '{elemAssign.ValueKind}'",
        Current().Line, Current().Column)
    };
  }

  // Resolves an expression result to a MaxonValue, emitting a var_ref op if needed for cross-block references
  private MaxonValue ResolveExprValue(ExprResult expr) {
    switch (expr) {
      case ExprResult.Direct d:
        return d.Value;
      case ExprResult.VarRef v:
        // Struct/enum variables always need a fresh var_ref op so each reference
        // gets a unique SSA value ID (prevents aliasing in structVarNames when
        // multiple variables share the same underlying value).
        if (v.Info.Kind == MaxonValueKind.Struct) {
          var structRefOp = new MaxonStructVarRefOp(v.VarName, v.Info.StructTypeName!);
          _currentBlock!.AddOp(structRefOp);
          return structRefOp.Result;
        }
        if (v.Info.Kind == MaxonValueKind.Enum) {
          var enumType = (MlirEnumType)_typeRegistry[v.Info.StructTypeName!];
          var backingKind = GetEnumBackingKind(enumType);
          var enumRefOp = new MaxonEnumVarRefOp(v.VarName, v.Info.StructTypeName!, backingKind);
          _currentBlock!.AddOp(enumRefOp);
          return enumRefOp.Result;
        }
        if (v.Info.DefinedInBlock == _currentBlock) {
          return v.Info.Value;
        }
        // Cross-block reference for non-struct/enum types
        var refOp = new MaxonVarRefOp(v.VarName, v.Info.Kind);
        _currentBlock!.AddOp(refOp);
        return refOp.Result;
    }
    throw new InvalidOperationException($"Unknown expression result type: {expr.GetType().Name}");
  }

  private abstract record ExprResult {
    public sealed record Direct(MaxonValue Value) : ExprResult;
    public sealed record VarRef(string VarName, VarInfo Info) : ExprResult;
  }

  private MaxonValueKind DetermineValueKind(MaxonValue value) {
    return value switch {
      MaxonInteger => MaxonValueKind.Integer,
      MaxonFloat => MaxonValueKind.Float,
      MaxonBool => MaxonValueKind.Bool,
      MaxonByte => MaxonValueKind.Byte,
      MaxonStruct => MaxonValueKind.Struct,
      MaxonEnum => MaxonValueKind.Enum,
      MaxonFunctionPtr => MaxonValueKind.Function,
      _ => throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Cannot determine kind of {value.GetType().Name}",
        Current().Line, Current().Column)
    };
  }

  private static bool IsWideningCast(MaxonValueKind source, MaxonValueKind target) {
    return (source, target) switch {
      (MaxonValueKind.Byte, MaxonValueKind.Integer) => true,
      (MaxonValueKind.Byte, MaxonValueKind.Float) => true,
      (MaxonValueKind.Integer, MaxonValueKind.Float) => true,
      (MaxonValueKind.Integer, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Float, MaxonValueKind.Integer) => false,
      (MaxonValueKind.Float, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Integer) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Float) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Integer, MaxonValueKind.Bool) => false,
      (MaxonValueKind.Float, MaxonValueKind.Bool) => false,
      (MaxonValueKind.Byte, MaxonValueKind.Bool) => false,
      _ => throw new InvalidOperationException($"Unhandled cast combination: {source} -> {target}")
    };
  }

  private void ValidateCast(MaxonValueKind sourceKind, MaxonValueKind targetKind, Token asToken) {
    if (sourceKind == targetKind) return;

    // Allow int literal in 0-255 range to cast to byte (check lastOp rather
    // than searching by value ID, since MaxonValue.Equals uses ID comparison
    // and IDs can collide between literal results and var-ref results).
    if (targetKind == MaxonValueKind.Byte && sourceKind == MaxonValueKind.Integer) {
      var lastOp = _currentBlock!.Operations.LastOrDefault();
      if (lastOp is MaxonLiteralOp lit && lit.ValueKind == MaxonValueKind.Integer
          && lit.IntValue >= 0 && lit.IntValue <= 255) {
        return;
      }
    }

    if (!IsWideningCast(sourceKind, targetKind)) {
      throw new CompileError(
        ErrorCode.SemanticUnsafeCast,
        $"Cannot cast from {KindDisplayName(sourceKind)} to {KindDisplayName(targetKind)}",
        asToken.Line, asToken.Column);
    }
  }

  private static string KindDisplayName(MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => "int",
    MaxonValueKind.Float => "float",
    MaxonValueKind.Bool => "bool",
    MaxonValueKind.Byte => "byte",
    MaxonValueKind.Struct => "struct",
    MaxonValueKind.Enum => "enum",
    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
  };

  private static bool IsComparisonOp(MaxonBinOperator op) =>
    op is MaxonBinOperator.Eq or MaxonBinOperator.Ne or MaxonBinOperator.Lt
      or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge;

  private (MaxonValueKind Kind, MaxonValue Lhs, MaxonValue Rhs) DetermineValueKind(
      MaxonValue lhs, MaxonValue rhs, MaxonBinOperator op, Token opToken) {
    var lhsKind = DetermineValueKind(lhs);
    var rhsKind = DetermineValueKind(rhs);

    if (lhsKind == rhsKind) return (lhsKind, lhs, rhs);

    // Comparisons do not allow implicit int/float promotion
    if (IsComparisonOp(op)) {
      // Allow byte vs int-literal-in-range comparisons
      if (lhsKind == MaxonValueKind.Byte && rhsKind == MaxonValueKind.Integer) {
        if (IsSmallIntLiteral(rhs))
          return (MaxonValueKind.Byte, lhs, rhs);
      }
      if (lhsKind == MaxonValueKind.Integer && rhsKind == MaxonValueKind.Byte) {
        if (IsSmallIntLiteral(lhs))
          return (MaxonValueKind.Byte, lhs, rhs);
      }

      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"type mismatch: 'cannot compare {KindDisplayName(lhsKind)} with {KindDisplayName(rhsKind)}'",
        opToken.Line, opToken.Column);
    }

    if (IsWideningCast(lhsKind, rhsKind)) return (rhsKind, PromoteValue(lhs, rhsKind), rhs);
    if (IsWideningCast(rhsKind, lhsKind)) return (lhsKind, lhs, PromoteValue(rhs, lhsKind));

    throw new CompileError(ErrorCode.ParserExpectedExpression,
      $"Cannot operate on {KindDisplayName(lhsKind)} and {KindDisplayName(rhsKind)}",
      opToken.Line, opToken.Column);
  }

  private bool IsSmallIntLiteral(MaxonValue value) {
    // Check if this value is an int literal in 0-255 range
    var lastOps = _currentBlock!.Operations;
    for (int i = lastOps.Count - 1; i >= 0; i--) {
      if (lastOps[i] is MaxonLiteralOp lit && lit.Result == value)
        return lit.ValueKind == MaxonValueKind.Integer && lit.IntValue >= 0 && lit.IntValue <= 255;
    }
    return false;
  }

  private MaxonValue PromoteValue(MaxonValue value, MaxonValueKind targetKind) {
    var sourceKind = DetermineValueKind(value);
    return (sourceKind, targetKind) switch {
      (MaxonValueKind.Integer, MaxonValueKind.Float) => PromoteIntToFloat(value),
      (MaxonValueKind.Byte, MaxonValueKind.Integer) => value,
      (MaxonValueKind.Byte, MaxonValueKind.Float) => PromoteIntToFloat(value),
      _ => throw new InvalidOperationException($"Unhandled promotion: {sourceKind} -> {targetKind}")
    };
  }

  private MaxonFloat PromoteIntToFloat(MaxonValue intValue) {
    var op = new MaxonIntToFloatOp(intValue);
    _currentBlock!.AddOp(op);
    return op.Result;
  }

  /// <summary>
  /// Resolves a function name by finding all matches (exact or suffix).
  /// Errors if no matches or multiple matches (ambiguous).
  /// Examples:
  ///   - "helpers.greet" finds exact match "helpers.greet"
  ///   - "greet" finds suffix matches like "helpers.greet", "utils.greet"
  ///   - If multiple matches exist, it's ambiguous and errors
  /// </summary>
  private MlirFunction<MaxonOp> ResolveFunctionName(string functionName, Token functionNameToken) {
    // First, try to find a function in the current file's namespace
    var currentNamespace = DeriveNamespace();
    var qualifiedName = string.IsNullOrEmpty(currentNamespace) ? functionName : $"{currentNamespace}.{functionName}";

    // Check for exact match with current namespace (local function)
    var localMatch = _currentModule!.Functions.FirstOrDefault(f => f.Name == qualifiedName);
    if (localMatch != null) {
      return localMatch;
    }

    // Find all other matches: exact match OR suffix match
    var exactMatches = _currentModule!.Functions.Where(f => f.Name == functionName).ToList();
    var suffixPattern = $".{functionName}";
    var suffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith(suffixPattern)).ToList();

    Logger.Debug(LogCategory.Parser, $"ResolveFunctionName: '{functionName}'");
    Logger.Debug(LogCategory.Parser, $"  Local match ('{qualifiedName}'): {(localMatch != null ? "found" : "not found")}");
    Logger.Debug(LogCategory.Parser, $"  Exact matches: {exactMatches.Count} - {string.Join(", ", exactMatches.Select(f => f.Name))}");
    Logger.Debug(LogCategory.Parser, $"  Suffix matches: {suffixMatches.Count} - {string.Join(", ", suffixMatches.Select(f => f.Name))}");

    // Prefer exact matches - they take precedence over suffix matches
    if (exactMatches.Count == 1) {
      return exactMatches[0];
    } else if (exactMatches.Count > 1) {
      var candidates = string.Join(", ", exactMatches.Select(f => f.Name));
      throw new CompileError(ErrorCode.SemanticAmbiguousFunctionCall,
        $"Ambiguous function call '{functionName}': multiple exact candidates found: {candidates}. Use a qualified name to disambiguate.",
        functionNameToken.Line, functionNameToken.Column);
    }

    // No exact matches - try suffix matches
    if (suffixMatches.Count == 0) {
      throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Undefined function '{functionName}'",
        functionNameToken.Line, functionNameToken.Column);
    } else if (suffixMatches.Count > 1) {
      var candidates = string.Join(", ", suffixMatches.Select(f => f.Name));
      throw new CompileError(ErrorCode.SemanticAmbiguousFunctionCall,
        $"Ambiguous function call '{functionName}': multiple candidates found: {candidates}. Use a qualified name to disambiguate.",
        functionNameToken.Line, functionNameToken.Column);
    }

    return suffixMatches[0];
  }

  /// <summary>
  /// Returns all function candidates matching the given base name, including
  /// mangled overload variants (names containing '$').
  /// </summary>
  private List<MlirFunction<MaxonOp>> ResolveFunctionOverloads(string functionName) {
    var currentNamespace = DeriveNamespace();
    var qualifiedName = string.IsNullOrEmpty(currentNamespace) ? functionName : $"{currentNamespace}.{functionName}";

    // Check local namespace
    var localMatches = _currentModule!.Functions.Where(f => f.Name == qualifiedName || f.Name.StartsWith(qualifiedName + "$")).ToList();
    if (localMatches.Count > 0) return localMatches;

    // Exact matches (including overload variants)
    var exactMatches = _currentModule!.Functions.Where(f => f.Name == functionName || f.Name.StartsWith(functionName + "$")).ToList();
    if (exactMatches.Count > 0) return exactMatches;

    // Suffix matches
    var suffixPattern = $".{functionName}";
    var suffixDollar = $".{functionName}$";
    var suffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith(suffixPattern) || f.Name.Contains(suffixDollar)).ToList();
    return suffixMatches;
  }

  /// <summary>
  /// Given multiple overload candidates, peeks at the named arguments at the current
  /// token position to select the matching overload. If only one candidate, returns it.
  /// </summary>
  private MlirFunction<MaxonOp> SelectOverloadByNamedArgs(List<MlirFunction<MaxonOp>> candidates, Token callToken) {
    if (candidates.Count == 1) return candidates[0];
    if (candidates.Count == 0) {
      throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Undefined function '{callToken.Value}'",
        callToken.Line, callToken.Column);
    }

    var namedArgNames = PeekNamedArgNames();

    // Filter candidates: a candidate matches if all named args from the call site
    // correspond to parameter names in the candidate
    var matching = candidates.Where(c => {
      foreach (var name in namedArgNames) {
        if (!c.ParamNames.Contains(name)) return false;
      }
      return true;
    }).ToList();

    if (matching.Count == 1) return matching[0];
    if (matching.Count == 0) {
      var candidateInfo = string.Join(", ", candidates.Select(c =>
        $"({string.Join(", ", c.ParamNames.Where(n => n != "self"))})"));
      throw new CompileError(ErrorCode.SemanticAmbiguousFunctionCall,
        $"No overload of '{UnmangleName(callToken.Value)}' matches the named arguments. Candidates: {candidateInfo}",
        callToken.Line, callToken.Column);
    }

    var matchInfo = string.Join(", ", matching.Select(c =>
      $"({string.Join(", ", c.ParamNames.Where(n => n != "self"))})"));
    throw new CompileError(ErrorCode.SemanticAmbiguousFunctionCall,
      $"Ambiguous overload for '{UnmangleName(callToken.Value)}': multiple overloads match. Candidates: {matchInfo}",
      callToken.Line, callToken.Column);
  }

  /// <summary>
  /// Peeks ahead in the token stream to find named argument names (identifier followed
  /// by ':') without consuming tokens.
  /// </summary>
  private HashSet<string> PeekNamedArgNames() {
    var names = new HashSet<string>();
    var savedPos = _pos;

    int parenDepth = 1;
    while (_pos < _tokens.Count && parenDepth > 0) {
      if (Current().Type == TokenType.LeftParen) parenDepth++;
      if (Current().Type == TokenType.RightParen) {
        parenDepth--;
        if (parenDepth == 0) break;
      }

      if (Current().Type == TokenType.Identifier && _pos + 1 < _tokens.Count
          && _tokens[_pos + 1].Type == TokenType.Colon) {
        names.Add(Current().Value);
      }
      _pos++;
    }

    _pos = savedPos;
    return names;
  }

  /// <summary>
  /// Parses call arguments. The first argument can be positional or named.
  /// Second and subsequent arguments MUST be named (name: value syntax),
  /// unless a function has 2+ params and a second positional arg is given (error).
  /// Handles default parameter values for omitted arguments.
  /// </summary>
  private (List<MaxonValue> args, MlirFunction<MaxonOp> callee) ParseCallArgs(Token functionNameToken) {
    var candidates = ResolveFunctionOverloads(functionNameToken.Value);
    var callee = SelectOverloadByNamedArgs(candidates, functionNameToken);

    if (Check(TokenType.RightParen)) {
      Advance();
      if (callee.ParamTypes.Count > 0) {
        return (FillDefaultArgs(functionNameToken, callee, new MaxonValue?[callee.ParamTypes.Count]), callee);
      }
      return ([], callee);
    }

    var args = new MaxonValue?[callee.ParamTypes.Count];
    ParseArgList(functionNameToken, callee, args, firstPositionalIndex: 0);
    Expect(TokenType.RightParen);

    return (FillDefaultArgs(functionNameToken, callee, args), callee);
  }

  /// <summary>
  /// Parses arguments for an indirect call (call through function pointer).
  /// Uses the function type to determine expected number of arguments.
  /// </summary>
  private List<MaxonValue> ParseIndirectCallArgs(Token varNameToken, MlirFunctionType fnType) {
    var args = new List<MaxonValue>();
    if (!Check(TokenType.RightParen)) {
      do {
        // For indirect calls, we parse positional arguments only for now
        // (named arguments would require knowing parameter names from the function type)
        var argExpr = ParseExpression();
        var argValue = ResolveExprValue(argExpr);
        args.Add(argValue);
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    Expect(TokenType.RightParen);

    // Validate argument count
    if (args.Count != fnType.ParameterTypes.Count) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Expected {fnType.ParameterTypes.Count} argument(s), got {args.Count}",
        varNameToken.Line, varNameToken.Column);
    }

    return args;
  }

  private List<MaxonValue> FillDefaultArgs(Token functionNameToken, MlirFunction<MaxonOp> callee, MaxonValue?[] args) {
    // Fill in defaults for missing arguments
    _functionDefaults.TryGetValue(callee.Name, out var defaults);

    for (int i = 0; i < args.Length; i++) {
      if (args[i] == null) {
        if (defaults != null && defaults.TryGetValue(i, out var defaultAttr)) {
          args[i] = EmitDefaultLiteral(defaultAttr, callee.ParamTypes[i], functionNameToken,
            $"Missing argument for parameter '{callee.ParamNames[i]}' in call to '{callee.Name}'");
        } else {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"missing argument for parameter '{callee.ParamNames[i]}'",
            functionNameToken.Line, functionNameToken.Column);
        }
      }
    }

    if (args.Length > 0 && args[0] is MaxonStruct selfStruct
        && _typeRegistry.TryGetValue(selfStruct.TypeName, out var selfType)
        && selfType is MlirStructType selfStructType
        && selfStructType.TypeParams.TryGetValue("Element", out var elemType)) {
      // Check if we're calling an Element-polymorphic method and get caller's Element type
      Logger.Debug(LogCategory.Parser, $"FillDefaultArgs: {callee.Name} - callerElementType={elemType}");
    }

    // Implicit type conversion for function arguments
    for (int i = 0; i < args.Length; i++) {
      var argKind = DetermineValueKind(args[i]!);
      var paramType = callee.ParamTypes[i];
      if (paramType is MlirStructType) continue;
      if (paramType is MlirEnumType) continue;

      // Skip type conversion for type-parameter params
      if (paramType is MlirTypeParameterType) continue;

      var paramKind = paramType.ToValueKind();
      if (argKind == paramKind) continue;

      args[i] = ConvertArgToParamType(args[i]!, argKind, paramKind, callee.ParamNames[i], functionNameToken);
    }

    return args.ToList()!;
  }


  private MaxonValue ConvertArgToParamType(MaxonValue arg, MaxonValueKind argKind, MaxonValueKind paramKind,
      string paramName, Token callToken) {
    return (argKind, paramKind) switch {
      (MaxonValueKind.Integer, MaxonValueKind.Float) => EmitCast(arg, MaxonValueKind.Float),
      (MaxonValueKind.Float, MaxonValueKind.Integer) => EmitCast(arg, MaxonValueKind.Integer),
      (MaxonValueKind.Integer, MaxonValueKind.Byte) => EmitCast(arg, MaxonValueKind.Byte),
      (MaxonValueKind.Float, MaxonValueKind.Byte) => EmitCast(arg, MaxonValueKind.Byte),
      (MaxonValueKind.Byte, MaxonValueKind.Integer) => arg, // byte is stored as i64 internally
      _ => throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"argument type mismatch for '{paramName}': expected '{KindDisplayName(paramKind)}', got '{KindDisplayName(argKind)}'",
        callToken.Line, callToken.Column)
    };
  }

  private MaxonValue EmitCast(MaxonValue value, MaxonValueKind targetKind) {
    var castOp = new MaxonCastOp(value, targetKind);
    _currentBlock!.AddOp(castOp);
    return castOp.Result;
  }

  /// <summary>
  /// Parse a single call argument value. If the argument starts with '{' and the expected
  /// parameter type is a struct, parse it as an anonymous struct literal.
  /// </summary>
  private MaxonValue ParseCallArgValue(MlirType expectedType, Dictionary<string, MlirType>? typeParams = null) {
    var resolvedType = expectedType;
    if (resolvedType is MlirTypeParameterType tp && typeParams != null && typeParams.TryGetValue(tp.ParameterName, out var concrete)) {
      resolvedType = concrete;
    }
    if (Check(TokenType.LeftBrace) && resolvedType is MlirStructType structType) {
      var structLiteral = ParseStructLiteral(structType.Name);
      return ResolveExprValue(structLiteral);
    }
    return ResolveExprValue(ParseExpression());
  }

  /// <summary>
  /// Shared logic for parsing a first-positional + named-remaining argument list.
  /// Fills entries into the pre-allocated args array. firstPositionalIndex is the
  /// slot used when the first argument is positional (0 for normal calls, 1 for
  /// instance methods where slot 0 is self).
  /// </summary>
  private void ParseArgList(Token callToken, MlirFunction<MaxonOp> callee, MaxonValue?[] args, int firstPositionalIndex, Dictionary<string, MlirType>? typeParams = null) {
    if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
      ParseNamedArg(callee, args, typeParams);
    } else {
      args[firstPositionalIndex] = ParseCallArgValue(callee.ParamTypes[firstPositionalIndex], typeParams);
    }

    while (Check(TokenType.Comma) && Advance() != null) {
      if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
        ParseNamedArg(callee, args, typeParams);
      } else {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Second and subsequent arguments must be named. Use 'name: value' syntax",
          callToken.Line, callToken.Column);
      }
    }
  }

  private void ParseNamedArg(MlirFunction<MaxonOp> callee, MaxonValue?[] args, Dictionary<string, MlirType>? typeParams = null) {
    var nameToken = Advance();
    Advance(); // consume ':'
    var idx = callee.ParamNames.IndexOf(nameToken.Value);
    if (idx < 0)
      throw new CompileError(ErrorCode.SemanticUndefinedVariable, $"unknown parameter name: '{nameToken.Value}'", nameToken.Line, nameToken.Column);
    args[idx] = ParseCallArgValue(callee.ParamTypes[idx], typeParams);
  }

  /// <summary>
  /// Parses call arguments for an instance method call, pre-filling the self argument at index 0.
  /// The first explicit argument is positional (maps to index 1), subsequent args must be named.
  /// </summary>
  private (List<MaxonValue> args, MlirFunction<MaxonOp> callee) ParseInstanceMethodCallArgs(Token methodNameToken, MaxonValue selfValue) {
    var candidates = ResolveFunctionOverloads(methodNameToken.Value);
    var callee = SelectOverloadByNamedArgs(candidates, methodNameToken);

    var args = new MaxonValue?[callee.ParamTypes.Count];
    args[0] = selfValue;

    // Resolve type parameters from the self value's concrete struct type
    Dictionary<string, MlirType>? typeParams = null;
    if (selfValue is MaxonStruct ms && _typeRegistry.TryGetValue(ms.TypeName, out var selfType) && selfType is MlirStructType selfStructType && selfStructType.TypeParams.Count > 0) {
      typeParams = selfStructType.TypeParams;
    }

    if (!Check(TokenType.RightParen)) {
      ParseArgList(methodNameToken, callee, args, firstPositionalIndex: 1, typeParams);
    }

    Expect(TokenType.RightParen);
    return (FillDefaultArgs(methodNameToken, callee, args), callee);
  }

  /// <summary>
  /// Resolves the result kind and struct type name for a function call based on the callee's return type.
  /// For type parameter returns, resolves against the self arg's element type.
  /// </summary>
  private (MaxonValueKind?, string?) ResolveCallResultType(MlirType? returnType, List<MaxonValue> args) {
    if (returnType == null) return (null, null);
    if (returnType is MlirTypeParameterType)
      return OverrideResultKindForElementType(MaxonValueKind.TypeParameter, null, args);
    var kind = returnType.ToValueKind();
    var typeName = returnType switch {
      MlirStructType s => s.Name,
      MlirEnumType e => e.Name,
      _ => (string?)null
    };
    return (kind, typeName);
  }

  /// <summary>
  /// Creates a function call operation, validating the function exists and determining the result type.
  /// Handles struct return types by setting ResultStructTypeName.
  /// </summary>
  private MaxonCallOp CreateFunctionCall(Token functionNameToken, List<MaxonValue> args) {
    var functionName = functionNameToken.Value;
    var callee = ResolveFunctionName(functionName, functionNameToken);

    // E057: calling a throwing function without try
    if (callee.ThrowsType != null && !_inTryContext) {
      throw new CompileError(ErrorCode.SemanticThrowingFunctionRequiresTry,
        $"throwing function requires try: '{functionName}'",
        functionNameToken.Line, functionNameToken.Column);
    }

    var (resultKind, resultStructTypeName) = ResolveCallResultType(callee.ReturnType, args);
    var callOp = new MaxonCallOp(callee.Name, args, resultKind, resultStructTypeName);
    _currentBlock!.AddOp(callOp);
    return callOp;
  }

  /// <summary>
  /// Creates a function call operation using a pre-resolved callee (from overload resolution).
  /// Skips function lookup since the callee was already selected by ParseCallArgs/ParseInstanceMethodCallArgs.
  /// </summary>
  private MaxonCallOp CreateFunctionCall(Token functionNameToken, List<MaxonValue> args, MlirFunction<MaxonOp> callee) {
    var functionName = functionNameToken.Value;

    // E057: calling a throwing function without try
    if (callee.ThrowsType != null && !_inTryContext) {
      throw new CompileError(ErrorCode.SemanticThrowingFunctionRequiresTry,
        $"throwing function requires try: '{UnmangleName(functionName)}'",
        functionNameToken.Line, functionNameToken.Column);
    }

    var (resultKind, resultStructTypeName) = ResolveCallResultType(callee.ReturnType, args);
    var callOp = new MaxonCallOp(callee.Name, args, resultKind, resultStructTypeName);
    _currentBlock!.AddOp(callOp);
    return callOp;
  }

  // ============================================================================
  // Helpers
  // ============================================================================

  /// <summary>
  /// For generic methods returning a TypeParameter placeholder, checks if the caller's
  /// struct type has a concrete Element type that should override the result kind.
  /// </summary>
  private (MaxonValueKind Kind, string? StructTypeName) OverrideResultKindForElementType(MaxonValueKind resultKind, string? resultStructTypeName, List<MaxonValue> args) {
    if (resultKind == MaxonValueKind.TypeParameter && args.Count > 0 && args[0] is MaxonStruct selfStruct) {
      if (_typeRegistry.TryGetValue(selfStruct.TypeName, out var selfType)
          && selfType is MlirStructType selfStructType
          && selfStructType.TypeParams.TryGetValue("Element", out var elementType)) {
        if (elementType is MlirTypeParameterType) return (resultKind, resultStructTypeName);
        var kind = elementType.ToValueKind();
        var typeName = elementType switch {
          MlirStructType s => s.Name,
          MlirEnumType e => e.Name,
          _ => (string?)null
        };
        return (kind, typeName);
      }
    }
    return (resultKind, resultStructTypeName);
  }

  private string UniqueLabel(string label) => $"{label}_{_blockCounter++}";

  /// <summary>
  /// Emits a literal op for a default attribute value and returns the resulting MaxonValue.
  /// </summary>
  private MaxonValue EmitDefaultLiteral(MlirAttribute attr, MlirType type, Token errorToken, string context) {
    MaxonLiteralOp defaultOp;
    if (attr is IntegerAttr intAttr && type == MlirType.I1) {
      defaultOp = new MaxonLiteralOp(intAttr.Value != 0);
    } else if (attr is IntegerAttr intAttr2) {
      defaultOp = new MaxonLiteralOp(intAttr2.Value);
    } else if (attr is FloatAttr floatAttr) {
      defaultOp = new MaxonLiteralOp(floatAttr.Value);
    } else {
      throw new CompileError(ErrorCode.ParserExpectedExpression, context, errorToken.Line, errorToken.Column);
    }
    _currentBlock!.AddOp(defaultOp);
    return defaultOp.Result;
  }

  private static long ParseIntegerLiteral(Token token) {
    var text = token.Value.Replace("_", "");
    try {
      if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        return Convert.ToInt64(text[2..], 16);
      if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        return Convert.ToInt64(text[2..], 2);
      if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        return Convert.ToInt64(text[2..], 8);
      return long.Parse(text);
    } catch (OverflowException) {
      throw new CompileError(ErrorCode.ParserLiteralOverflow,
        $"Integer literal '{token.Value}' is outside the range of int ({long.MinValue} to {long.MaxValue})",
        token.Line, token.Column);
    }
  }

  private static MaxonValueKind GetValueKind(MaxonValue value) {
    return value switch {
      MaxonInteger => MaxonValueKind.Integer,
      MaxonFloat => MaxonValueKind.Float,
      MaxonBool => MaxonValueKind.Bool,
      MaxonByte => MaxonValueKind.Byte,
      MaxonStruct => MaxonValueKind.Struct,
      MaxonEnum => MaxonValueKind.Enum,
      _ => throw new InvalidOperationException($"Unknown MaxonValue type: {value.GetType()}")
    };
  }

  private static double ParseFloatLiteral(Token token) {
    var text = token.Value.Replace("_", "");
    try {
      var value = double.Parse(text, CultureInfo.InvariantCulture);
      if (double.IsInfinity(value)) {
        throw new CompileError(ErrorCode.ParserLiteralOverflow,
          $"Float literal '{token.Value}' is outside the range of float",
          token.Line, token.Column);
      }
      return value;
    } catch (OverflowException) {
      throw new CompileError(ErrorCode.ParserLiteralOverflow,
        $"Float literal '{token.Value}' is outside the range of float",
        token.Line, token.Column);
    }
  }

  /// <summary>
  /// Checks if the current position is the start of a closure expression.
  /// Closures look like: (param type, ...) gives expr
  /// vs parenthesized expression: (expr)
  /// </summary>
  private bool IsClosure() {
    // Look ahead without consuming tokens
    int lookahead = 0;

    // Must start with '('
    if (_pos + lookahead >= _tokens.Count || _tokens[_pos + lookahead].Type != TokenType.LeftParen)
      return false;
    lookahead++;

    // Empty params: () gives expr
    if (_pos + lookahead < _tokens.Count && _tokens[_pos + lookahead].Type == TokenType.RightParen) {
      lookahead++;
      return _pos + lookahead < _tokens.Count && _tokens[_pos + lookahead].Type == TokenType.Gives;
    }

    // Non-empty params: (name type, ...) gives expr
    // First param: identifier followed by type (identifier, int, float, bool, byte, or function type)
    if (_pos + lookahead >= _tokens.Count || _tokens[_pos + lookahead].Type != TokenType.Identifier)
      return false;
    lookahead++;

    // After param name, must see a type token
    if (_pos + lookahead >= _tokens.Count)
      return false;
    var afterName = _tokens[_pos + lookahead].Type;
    return afterName == TokenType.Identifier || afterName == TokenType.Int || afterName == TokenType.Float ||
           afterName == TokenType.Bool || afterName == TokenType.Byte || afterName == TokenType.LeftParen;
  }

  /// <summary>
  /// Parses a closure expression: (param type, ...) gives expr
  /// Creates an anonymous function and returns a reference to it.
  /// </summary>
  private ExprResult.Direct ParseClosure() {
    Expect(TokenType.LeftParen);

    // Parse closure parameters
    var paramNames = new List<string>();
    var paramTypes = new List<MlirType>();

    if (!Check(TokenType.RightParen)) {
      do {
        var paramName = Expect(TokenType.Identifier).Value;
        var paramType = ParseTypeRef();
        paramNames.Add(paramName);
        paramTypes.Add(paramType);
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    Expect(TokenType.RightParen);
    Expect(TokenType.Gives);

    // Determine return type from expression (we'll infer it after parsing)
    // Create a unique name for the closure
    var closureName = $"__closure_{_closureCounter++}";

    // Save current parsing state
    var savedVars = new Dictionary<string, VarInfo>(_variables);
    var savedBlock = _currentBlock;
    var savedFunction = _currentFunction;
    var savedReferencedVars = new HashSet<string>(_referencedVars);

    // We'll determine the return type after parsing, use a placeholder for now
    var closureFunc = new MlirFunction<MaxonOp>(closureName, paramNames, paramTypes, null, null) {
      IsStdlib = false,
    };

    _currentFunction = closureFunc;
    _currentBlock = closureFunc.Body.AddBlock("entry");
    _variables.Clear();
    _referencedVars.Clear();

    // Add closure parameters to scope
    for (int i = 0; i < paramNames.Count; i++) {
      var pKind = paramTypes[i].ToValueKind();
      var paramOp = new MaxonParamOp(i, paramNames[i], pKind);
      _currentBlock.AddOp(paramOp);
      _variables[paramNames[i]] = new VarInfo(pKind, false, paramOp.Result, _currentBlock);
    }

    // Parse the closure body expression
    var bodyExpr = ParseExpression();
    var bodyValue = ResolveExprValue(bodyExpr);

    // Emit return
    var returnOp = new MaxonReturnOp(bodyValue);
    _currentBlock.AddOp(returnOp);

    // Determine return type from the body value
    var returnKind = DetermineValueKind(bodyValue);
    var returnType = returnKind.ToMlirType();

    // Create the final function with proper return type
    var finalClosureFunc = new MlirFunction<MaxonOp>(closureName, paramNames, paramTypes, returnType, null) {
      IsStdlib = false,
    };
    // Copy the body from the temporary function
    foreach (var block in closureFunc.Body.Blocks) {
      finalClosureFunc.Body.Blocks.Add(block);
    }

    // Add closure function to module
    _currentModule!.Functions.Add(finalClosureFunc);

    // Restore parsing state
    _variables.Clear();
    foreach (var kv in savedVars) _variables[kv.Key] = kv.Value;
    _currentBlock = savedBlock;
    _currentFunction = savedFunction;
    _referencedVars.Clear();
    foreach (var v in savedReferencedVars) _referencedVars.Add(v);

    // Create a function reference to the closure
    var fnType = new MlirFunctionType(paramTypes, returnType);
    var fnRefOp = new MaxonFunctionRefOp(closureName, fnType);
    _currentBlock!.AddOp(fnRefOp);

    return new ExprResult.Direct(fnRefOp.Result);
  }

  private int ExpectEndLabel(string expectedLabel) {
    var endToken = Expect(TokenType.End);
    if (Check(TokenType.CharacterLiteral)) {
      var label = Advance().Value;
      if (label != expectedLabel) {
        throw new CompileError(ErrorCode.ParserMismatchedEndLabel, $"Mismatched end label: expected '{expectedLabel}', got '{label}'", endToken.Line, endToken.Column);
      }
    }
    return endToken.Line;
  }

  private Token PeekNext() => _pos + 1 < _tokens.Count ? _tokens[_pos + 1] : new Token(TokenType.Eof, "", 0, 0);

  /// <summary>
  /// Detects `int.fromString(...)` and similar primitive type static method calls,
  /// and rewrites the tokens so the identifier handler sees `__int_fromString(...)`.
  /// Returns true if a rewrite was performed.
  /// </summary>
  private bool TryRewritePrimitiveStaticMethod() {
    if (!(Check(TokenType.Int) || Check(TokenType.Float) || Check(TokenType.Bool) || Check(TokenType.Byte)))
      return false;
    if (PeekNext().Type != TokenType.Dot
        || _pos + 2 >= _tokens.Count || _tokens[_pos + 2].Type != TokenType.Identifier
        || _pos + 3 >= _tokens.Count || _tokens[_pos + 3].Type != TokenType.LeftParen)
      return false;

    var typeToken = Advance();
    Advance(); // consume '.'
    var methodToken = _tokens[_pos];
    _tokens[_pos] = new Token(TokenType.Identifier, $"__{typeToken.Value}_{methodToken.Value}", methodToken.Line, methodToken.Column);
    return true;
  }

  private void SkipNewlines() {
    while (Check(TokenType.Newline) || Check(TokenType.DocComment)) {
      Advance();
    }
  }

  private Token Current() => _pos < _tokens.Count ? _tokens[_pos] : new Token(TokenType.Eof, "", 0, 0);
  private bool Check(TokenType type) => !IsAtEnd() && Current().Type == type;

  private Token Advance() {
    if (!IsAtEnd()) _pos++;
    return _tokens[_pos - 1];
  }

  private Token Expect(TokenType type) {
    if (!Check(type)) {
      throw new CompileError(ErrorCode.ParserExpectedToken, $"Expected {Token.DisplayName(type)} but got '{Current().Value}'", Current().Line, Current().Column);
    }
    return Advance();
  }

  private bool IsAtEnd() => _pos >= _tokens.Count;
}
