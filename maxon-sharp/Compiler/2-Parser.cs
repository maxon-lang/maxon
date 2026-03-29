using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public partial class Parser(List<Token> tokens, MlirModule<MaxonOp>? seedModule = null, bool isStdlib = false, string? sourceFilePath = null, string targetOs = "Windows", string targetArch = "x86_64", bool testing = false) {
  private List<Token> _tokens = tokens;
  private readonly bool _isStdlib = isStdlib;
  private readonly string? _sourceFilePath = sourceFilePath;
  private readonly string _targetOs = targetOs;
  private readonly string _targetArch = targetArch;
  private readonly bool _testing = testing;
  private int _pos;

  private MlirModule<MaxonOp>? _currentModule;
  private MlirFunction<MaxonOp>? _currentFunction;
  private MlirBlock<MaxonOp>? _currentBlock;

  // Tracks the ranged primitive type name from the most recent construction expression (e.g., Age{42})
  private string? _lastRangedTypeName;
  // Set by ResolveExprValue: true when the resolved expression was a mutable variable reference
  private bool _lastExprWasMutableVar;
  // Set by ResolveExprValue: the variable name of the last resolved expression (null for non-variable expressions)
  private string? _lastExprVarName;
  // Set by ParseCallArgs/ParseInstanceMethodCallArgs: per-argument mutability for the most recent call
  private List<bool>? _lastArgMutabilities;
  // Set by ParseCallArgs/ParseInstanceMethodCallArgs: per-argument variable names for the most recent call
  private List<string?>? _lastArgVarNames;
  private MlirRangedPrimitiveType? _lastCastRangedType;
  private readonly VarRegistry _variables = new();
  private readonly HashSet<string> _referencedVars = [];
  private int _blockCounter;
  [ThreadStatic] private static int _closureCounter;
  public static void ResetClosureCounter() => _closureCounter = 0;
  private int _discardCounter;
  private readonly Stack<LoopContext> _loopStack = new();
  private readonly Stack<MatchContext> _matchStack = new();
  private bool _inTryContext;
  private MaxonCallOp? _lastExprCallOp;
  private bool _parsingTypeAliasRhs;
  private bool _parsingExtension;
  private bool _skipWhereValidation;
  private readonly Dictionary<string, MlirType> _typeRegistry = seedModule != null
    ? new(seedModule!.TypeDefs.Where(kv =>
        !seedModule!.NonExportedTypeNames.Contains(kv.Key)
        || !seedModule!.TypeDefSourceFiles.TryGetValue(kv.Key, out var src)
        || src == sourceFilePath)) : [];
  // Types registered during this parser's PreScan — only these get auto-conformance synthesis
  private readonly HashSet<string> _locallyDefinedTypes = [];
  private string? _currentTypeName;

  // Top-level compile-time constants (name -> evaluated value: long, double, or bool)
  private Dictionary<string, object> _topLevelConstants = [];

  // Top-level declarations deferred for evaluation at a later phase
  private record EnumConstantValue(string EnumTypeName, string CaseName, int Ordinal);
  /// Shared info for both try-call and try-await operations, used by the otherwise-clause helpers.
  private record TryResultInfo(MaxonInteger ErrorFlag, MaxonValue? Result, MaxonValueKind? ResultKind, string? ResultStructTypeName);
  private record DeferredDecl(string Name, int TokenStart, int TokenEnd, int Line, int Column, bool IsExported = false);
  private readonly List<DeferredDecl> _deferredExprLets = [];
  private readonly List<DeferredDecl> _deferredGlobalVars = [];
  private readonly List<DeferredDecl> _deferredExprVars = [];

  // Lazy static fields (initialized on first access)
  private record LazyStaticField(string QualifiedName, string GuardName, List<Token> Tokens, int TokenStart, int TokenEnd, bool IsMutable, int Line, int Column);
  private readonly List<LazyStaticField> _lazyStaticFields = [];

  // Global mutable variables (name -> type info)
  private readonly Dictionary<string, GlobalVarMetadata> _globalVars = [];

  // Unified variable resolution result
  private abstract record ResolvedVar {
    public record Local(VarInfo Info) : ResolvedVar {
      public override bool IsMutable => Info.Mutable;
    }
    public record Global(GlobalVarMetadata Info) : ResolvedVar {
      public override bool IsMutable => Info.Mutable;
    }
    public abstract bool IsMutable { get; }
  }

  // Default parameter values (funcName -> index -> value)
  private readonly Dictionary<string, Dictionary<int, MlirAttribute>> _functionDefaults = [];

  // Type alias source tracking (aliasName -> sourceTypeName)
  private readonly Dictionary<string, string> _typeAliasSources = [];

  // Export tracking for types, enums, and typealiases defined in this file
  private readonly HashSet<string> _exportedTypes = [];
  private readonly HashSet<string> _exportedTypeAliases = [];
  private readonly HashSet<string> _localTypeAliases = [];
  private readonly HashSet<string> _seededTypeAliases = [];
  private readonly HashSet<string> _usedTypeAliases = [];
  private readonly Dictionary<string, (int Line, int Column)> _typeAliasLocations = [];
  private readonly HashSet<string> _resolvingTypeAliases = [];

  // Interface associated type names (interfaceName -> list of associated type names from 'uses' clause)
  private readonly Dictionary<string, List<string>> _interfaceAssociatedTypes = [];

  // Primitive type interface conformances from extension blocks (e.g., "int" -> ["Hashable", "Equatable"])
  private readonly Dictionary<string, List<string>> _primitiveConformances = seedModule != null
    ? new(seedModule.PrimitiveConformances.Select(kv => new KeyValuePair<string, List<string>>(kv.Key, [.. kv.Value]))) : [];

  // Conditional conformances from extension blocks on generic types
  // e.g., "extension Array implements Hashable where Element is Hashable" -> ("Array", ["Hashable"], {"Element": ["Hashable"]})
  private readonly List<(string SourceTypeName, List<string> Interfaces, Dictionary<string, List<string>> WhereConstraints)> _conditionalConformances = seedModule != null
    ? [.. seedModule.ConditionalConformances] : [];

  // Tracks conditional extension methods skipped due to unsatisfied where-constraints
  // Key: "TypeName.methodName", Value: (paramName, requiredInterface, concreteTypeName)
  private readonly Dictionary<string, (string ParamName, string RequiredInterface, string ConcreteTypeName)> _skippedConditionalExtensions = [];

  /// <summary>
  /// Creates a unique mangled name for an overloaded function by appending
  /// distinguishing parameter names. For instance methods, 'self' is excluded.
  /// The first non-self parameter name is also excluded (since it's positional and
  /// the same across overloads). Only subsequent named params form the suffix.
  /// Example: slice(self, start, endIndex) -> baseName$endIndex
  /// </summary>
  private static string MangleOverloadName(string baseName, List<string> paramNames) {
    var nonSelf = paramNames.Where(n => n != "self").ToList();
    if (nonSelf.Count == 0) return baseName;
    // For 1-param overloads, use param name directly; for 2+, skip the first positional
    var distinguishing = nonSelf.Count == 1 ? nonSelf : nonSelf.Skip(1);
    return $"{baseName}${string.Join("_", distinguishing)}";
  }

  /// <summary>
  /// Creates a mangled name that includes both parameter names and types,
  /// used when name-only mangling produces a collision (same param names, different types).
  /// Example: process(value int) -> baseName$value_i64, process(value String) -> baseName$value_String
  /// </summary>
  private static string MangleOverloadNameWithTypes(string baseName, List<string> paramNames, List<MlirType> paramTypes) {
    var parts = new List<string>();
    for (int i = 0; i < paramNames.Count; i++) {
      if (paramNames[i] == "self") continue;
      parts.Add($"{paramNames[i]}_{TypeMangledSuffix(paramTypes[i])}");
    }
    if (parts.Count == 0) return baseName;
    return $"{baseName}${string.Join("_", parts)}";
  }

  private static string TypeMangledSuffix(MlirType type) => type switch {
    MlirStructType st => st.Name,
    MlirUnionType et => et.Name,
    MlirFunctionType => "fn",
    MlirTypeParameterType tp => tp.ParameterName,
    MlirRangedPrimitiveType rpt => rpt.BaseType.Name,
    _ => type.Name
  };

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
  /// Supports both name-based and type-based disambiguation.
  /// </summary>
  private string ResolveOverloadRegistrationName(MlirModule<MaxonOp> module, string baseName, List<string> paramNames, List<MlirType> paramTypes) {
    var existingOverloads = module.Functions.Where(f => f.Name == baseName || UnmangleName(f.Name) == baseName).ToList();

    if (existingOverloads.Count == 0) {
      return baseName;
    }

    // If the only existing match has the exact same signature, it's the same function
    // (e.g., re-registration during full parse after pre-scan)
    if (existingOverloads.Count == 1 && existingOverloads[0].Name == baseName
        && existingOverloads[0].ParamNames.SequenceEqual(paramNames)
        && ParamTypesMatch(existingOverloads[0].ParamTypes, paramTypes)) {
      return baseName;
    }

    var registrationName = MangleOverloadName(baseName, paramNames);

    // Check if this exact function was already registered under a type-augmented name
    var typeMangledForNew = MangleOverloadNameWithTypes(baseName, paramNames, paramTypes);
    var existingTypeMangled = existingOverloads.FirstOrDefault(f => f.Name == typeMangledForNew);
    if (existingTypeMangled != null) {
      // Same function re-registered (e.g., during full parse after pre-scan)
      if (existingTypeMangled.ParamNames.SequenceEqual(paramNames)
          && ParamTypesMatch(existingTypeMangled.ParamTypes, paramTypes))
        return typeMangledForNew;
    }

    // Retroactively mangle any existing function that still has the unmangled name
    foreach (var existing in existingOverloads) {
      if (!existing.Name.Contains('$')) {
        var mangledExisting = MangleOverloadName(baseName, existing.ParamNames);
        if (mangledExisting == registrationName) {
          // Same function re-registered (e.g., from pre-scan)
          if (existing.ParamNames.SequenceEqual(paramNames)
              && ParamTypesMatch(existing.ParamTypes, paramTypes))
            return registrationName;

          // Name-based collision but different types — use type-augmented mangling
          var typeMangledExisting = MangleOverloadNameWithTypes(baseName, existing.ParamNames, existing.ParamTypes);
          if (typeMangledExisting == typeMangledForNew)
            throw new InvalidOperationException(
              $"Duplicate overload: '{baseName}' already has an overload with the same signature.");

          var oldName = existing.Name;
          existing.Name = typeMangledExisting;
          if (_functionDefaults.Remove(oldName, out var existingDefaults)) {
            _functionDefaults[typeMangledExisting] = existingDefaults;
          }
          return typeMangledForNew;
        }
        var oldExistingName = existing.Name;
        existing.Name = mangledExisting;
        if (_functionDefaults.Remove(oldExistingName, out var defaults)) {
          _functionDefaults[mangledExisting] = defaults;
        }
      } else if (existing.Name == registrationName) {
        // Already mangled with same name — check if it's a re-registration or type collision
        if (existing.ParamNames.SequenceEqual(paramNames)
            && ParamTypesMatch(existing.ParamTypes, paramTypes))
          return registrationName;

        // Name-based collision on already-mangled function — re-mangle both with types
        var typeMangledExisting = MangleOverloadNameWithTypes(baseName, existing.ParamNames, existing.ParamTypes);
        if (typeMangledExisting == typeMangledForNew)
          throw new InvalidOperationException(
            $"Duplicate overload: '{baseName}' already has an overload with the same signature.");

        var oldName = existing.Name;
        existing.Name = typeMangledExisting;
        if (_functionDefaults.Remove(oldName, out var existingDefaults)) {
          _functionDefaults[typeMangledExisting] = existingDefaults;
        }
        return typeMangledForNew;
      }
    }

    return registrationName;
  }

  private static bool ParamTypesMatch(List<MlirType> a, List<MlirType> b) {
    if (a.Count != b.Count) return false;
    for (int i = 0; i < a.Count; i++) {
      if (TypeMangledSuffix(a[i]) != TypeMangledSuffix(b[i])) return false;
    }
    return true;
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

    var dotIdx = qualifiedName.IndexOf('.');
    if (dotIdx > 0) {
      var typePart = qualifiedName[..dotIdx];
      var methodPart = qualifiedName[(dotIdx + 1)..];

      // Where-constraint fallback: if typePart is a type parameter with constraints,
      // resolve via the constrained interface methods
      if (_currentTypeName != null
          && _typeRegistry.TryGetValue(_currentTypeName, out var currentType)
          && currentType is MlirStructType currentStruct
          && currentStruct.WhereConstraints.TryGetValue(typePart, out var constrainedInterfaces)) {
        foreach (var ifaceName in constrainedInterfaces) {
          var ifaceMethodName = $"{ifaceName}.{methodPart}";
          var resolved = ResolveMethodName(ifaceMethodName);
          if (resolved != null) return resolved;
        }
      }

      // Try alias fallback: ByteArray.push → Array.push
      if (_typeAliasSources.TryGetValue(typePart, out var sourceType)) {
        _usedTypeAliases.Add(typePart);
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

  /// Records method names from a conditional extension block that was skipped for a type
  /// because its where-constraints weren't satisfied.
  private void RecordSkippedConditionalExtensions(
      string typeName, MlirStructType structType,
      List<int> functionPositions, Dictionary<string, List<string>> whereConstraints) {
    // Find the first unsatisfied constraint to use in the error message
    foreach (var (paramName, requiredInterfaces) in whereConstraints) {
      if (!structType.TypeParams.TryGetValue(paramName, out var concreteType)) continue;
      if (concreteType is MlirTypeParameterType) continue;
      var concreteTypeName = MlirType.FormatAsSourceName(concreteType);
      foreach (var iface in requiredInterfaces) {
        if (TypeConformsToInterface(concreteTypeName, iface)) continue;
        // Record each method name from this extension block
        foreach (var pos in functionPositions) {
          if (pos + 1 < _tokens.Count) {
            var methodName = _tokens[pos + 1].Value;
            _skippedConditionalExtensions[$"{typeName}.{methodName}"] =
              (paramName, iface, concreteTypeName);
          }
        }
        return;
      }
    }
  }

  /// Checks if a method was skipped as a conditional extension whose where-constraints aren't satisfied.
  /// Returns a descriptive error message suffix, or null if no such extension exists.
  private string? FindUnsatisfiedConditionalExtension(string typeName, string methodName) {
    if (_skippedConditionalExtensions.TryGetValue(
        $"{typeName}.{methodName}", out var info))
      return $" ('{methodName}' is available as a conditional extension where {info.ParamName} is {info.RequiredInterface}, but '{info.ConcreteTypeName}' does not implement '{info.RequiredInterface}')";
    return null;
  }

  /// Check if a type name is an associated type of the current struct being parsed.
  private bool IsAssociatedTypeName(string typeName) {
    return _typeRegistry.TryGetValue(typeName, out var type) && type is MlirTypeParameterType;
  }

  /// Find an interface method by name across all registered interfaces.
  /// Searches for an interface method, restricted to interfaces the type parameter is constrained to.
  private MlirInterfaceMethodSignature? FindInterfaceMethod(string methodName, string typeParamName) {
    // Only search interfaces that the type parameter is constrained to via where clauses
    if (_currentTypeName != null
        && _typeRegistry.TryGetValue(_currentTypeName, out var currentType)
        && currentType is MlirStructType currentStruct
        && currentStruct.WhereConstraints.TryGetValue(typeParamName, out var constrainedInterfaces)) {
      foreach (var ifaceName in constrainedInterfaces) {
        if (_typeRegistry.TryGetValue(ifaceName, out var ifaceType) && ifaceType is MlirInterfaceType iface) {
          var method = iface.Methods.FirstOrDefault(m => m.Name == methodName);
          if (method != null) return method;
        }
      }
    }
    return null;
  }

  private record LoopContext(string SourceLabel, string HeaderLabel, string ExitLabel, HashSet<string> ScopeVars,
    string? IterVarName = null, string? NextMethodName = null, MaxonValueKind? ElementKind = null, string? ElementStructTypeName = null, string? IterableTypeName = null,
    string? RangeCounterVarName = null, MaxonValueKind? RangeElementKind = null, string? RangeStructTypeName = null,
    string? ForInResultVarName = null);
  private record MatchContext(string SourceLabel, string MergeLabel, HashSet<string> ScopeVars);

  private static readonly Dictionary<TokenType, (MaxonBinOperator Op, int Precedence)> BinaryOperators = new() {
    { TokenType.Or, (MaxonBinOperator.Or, 0) },
    { TokenType.Xor, (MaxonBinOperator.BitXor, 1) },
    { TokenType.And, (MaxonBinOperator.And, 2) },
    { TokenType.EqualsEquals, (MaxonBinOperator.Eq, 3) },
    { TokenType.NotEquals, (MaxonBinOperator.Ne, 3) },
    { TokenType.LessThan, (MaxonBinOperator.Lt, 3) },
    { TokenType.GreaterThan, (MaxonBinOperator.Gt, 3) },
    { TokenType.LessEquals, (MaxonBinOperator.Le, 3) },
    { TokenType.GreaterEquals, (MaxonBinOperator.Ge, 3) },
    { TokenType.Shl, (MaxonBinOperator.Shl, 4) },
    { TokenType.Shr, (MaxonBinOperator.Shr, 4) },
    { TokenType.Plus, (MaxonBinOperator.Add, 5) },
    { TokenType.Minus, (MaxonBinOperator.Sub, 5) },
    { TokenType.Star, (MaxonBinOperator.Mul, 6) },
    { TokenType.Slash, (MaxonBinOperator.Div, 6) },
    { TokenType.Mod, (MaxonBinOperator.Mod, 6) },
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

  private MlirType? GetOptimalType(MaxonValue value) {
    // Look up the variable's ranged type to get the optimal type for codegen
    foreach (var info in _variables.Values) {
      if (info.Value.Id == value.Id && info.StructTypeName != null
          && _typeRegistry.TryGetValue(info.StructTypeName, out var rt)
          && rt is MlirRangedPrimitiveType rpt) {
        return rpt.OptimalType;
      }
    }
    return null;
  }

  // Tracks captured variables during closure parsing
  private record CaptureInfo(string Name, MaxonValueKind Kind, MaxonValue OuterValue, string? StructTypeName);
  private List<CaptureInfo>? _closureCaptures;

  // Tracks parameter locations for unused parameter error reporting
  private readonly List<(string Name, int Line, int Column)> _paramLocations = [];
  private readonly List<(string Name, int Line, int Column)> _localVarLocations = [];

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
        if (SpecFragmentsRegex().IsMatch(normalizedPath)) {
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

    EnsureManagedMemoryType();

    SeedFromModule(seedModule, module);

    // Pre-scan to collect and evaluate top-level constants and global variables
    CollectAndEvaluateTopLevelDecls(module);

    SkipNewlines();
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      ParseTopLevel(module);
      SkipNewlines();
    }

    CheckUnusedTypeAliases();

    EmitLazyStaticInitFunctions(module);

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
    EnsureManagedMemoryType();
    SeedFromModule(seedModule, targetModule);

    CollectAndEvaluateTopLevelDecls(targetModule);

    CopyStateToModule(targetModule);
  }

  /// <summary>
  /// Pre-scan only top-level typealiases from this file. Called across all files before
  /// the full PreScan so cross-file typealias references resolve regardless of file order.
  /// Skips typealiases nested inside type/enum/interface/extension blocks.
  /// </summary>
  public void PreScanTypeAliasesOnly(MlirModule<MaxonOp> targetModule) {
    _currentModule = targetModule;
    _skipWhereValidation = true;
    EnsureManagedMemoryType();
    SeedFromModule(seedModule, targetModule);
    PreRegisterTopLevelTypeAliasNames();

    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      SkipNewlines();
      if (IsAtEnd() || Current().Type == TokenType.Eof) break;

      bool isExported = false;
      if (Check(TokenType.Export)) {
        isExported = true;
        Advance();
      }

      if (Check(TokenType.HashIf)) {
        HandleConditionalCompilation();
        continue;
      }
      if (Check(TokenType.HashElse)) {
        HandleConditionalElse();
        continue;
      }
      if (Check(TokenType.HashEndif)) {
        HandleConditionalEndif();
        continue;
      }

      if (Check(TokenType.Type) || Check(TokenType.Union) || Check(TokenType.Enum) || Check(TokenType.Interface)
          || Check(TokenType.Extension) || Check(TokenType.Function)) {
        Advance();
        SkipToMatchingEnd();
        SkipNewlines();
        continue;
      }

      if (Check(TokenType.TypeAlias)) {
        PreScanTypeAlias(isExported);
      } else {
        SkipToEndOfLine();
      }
      SkipNewlines();
    }

    CopyTypeAliasesToModule(targetModule);
  }

  private void EnsureManagedMemoryType() {
    if (!_typeRegistry.ContainsKey("__ManagedMemory")) {
      var mmType = new MlirStructType("__ManagedMemory", [
        new MlirStructField("buffer", MlirType.I64, false, true),
        new MlirStructField("length", MlirType.I64, false, true),
        new MlirStructField("capacity", MlirType.I64, false, true),
        new MlirStructField("element_size", MlirType.I64, false, true),
      ]);
      mmType.AssociatedTypeNames.Add("Element");
      mmType.DocString = "Compiler builtin managed memory buffer. Stores a heap-allocated data pointer, element count, capacity, and element size.";
      _typeRegistry["__ManagedMemory"] = mmType;
    }
    if (!_typeRegistry.ContainsKey("__ManagedList")) {
      var chainType = new MlirStructType("__ManagedList", [
        new MlirStructField("head", MlirType.I64, false, true),
        new MlirStructField("tail", MlirType.I64, false, true),
        new MlirStructField("count", MlirType.I64, false, true),
        new MlirStructField("cursor", MlirType.I64, false, true),
      ]);
      chainType.AssociatedTypeNames.Add("Element");
      chainType.DocString = "Compiler builtin doubly-linked managed list. Stores a head pointer, tail pointer, element count, and iteration cursor.\n\nSee the `managed_list` stdlib module for operations.";
      _typeRegistry["__ManagedList"] = chainType;
    }
    if (!_typeRegistry.ContainsKey("__ManagedListNode")) {
      var nodeType = new MlirStructType("__ManagedListNode", [
        new MlirStructField("next", MlirType.I64, false, true),
        new MlirStructField("prev", MlirType.I64, false, true),
        new MlirStructField("list", MlirType.I64, false, true),
        new MlirStructField("value", MlirType.I64, false, true),
      ]);
      nodeType.AssociatedTypeNames.Add("Element");
      nodeType.DocString = "Compiler builtin node for a `ManagedList` doubly-linked list. Stores next/prev node pointers, a back-pointer to the owning managed list, and the element value.";
      _typeRegistry["__ManagedListNode"] = nodeType;
    }
    if (!_typeRegistry.ContainsKey("__ManagedSocket")) {
      var socketType = new MlirStructType("__ManagedSocket", [
        new MlirStructField("_handle", MlirType.I64, false, true),
      ]) {
        DocString = "Compiler builtin managed socket. Wraps an OS socket handle with automatic cleanup via destructor on last decref."
      };
      _typeRegistry["__ManagedSocket"] = socketType;
    }
    if (!_typeRegistry.ContainsKey("__ManagedFile")) {
      var fileType = new MlirStructType("__ManagedFile", [
        new MlirStructField("_handle", MlirType.I64, false, true),
      ]) {
        DocString = "Compiler builtin managed file. Wraps a Windows file HANDLE with automatic cleanup via destructor on last decref."
      };
      _typeRegistry["__ManagedFile"] = fileType;
    }
    if (!_typeRegistry.ContainsKey("__ManagedDirectory")) {
      var dirType = new MlirStructType("__ManagedDirectory", [
        new MlirStructField("_block", MlirType.I64, false, true),
      ]) {
        DocString = "Compiler builtin managed directory search. Wraps a FindFirstFile block (HANDLE + WIN32_FIND_DATAA) with automatic cleanup."
      };
      _typeRegistry["__ManagedDirectory"] = dirType;
    }
  }

  /// <summary>
  /// Token-level pre-pass to register top-level typealias names as placeholders
  /// so forward and mutual references resolve during the actual typealias scan.
  /// </summary>
  private void PreRegisterTopLevelTypeAliasNames() {
    int prePos = _pos;
    int depth = 0;
    while (prePos < _tokens.Count && _tokens[prePos].Type != TokenType.Eof) {
      var t = _tokens[prePos];
      if (t.Type == TokenType.End) { if (depth > 0) depth--; prePos++; } else if (t.Type == TokenType.Type || t.Type == TokenType.Union || t.Type == TokenType.Enum || t.Type == TokenType.Interface) { depth++; prePos++; } else if (depth == 0 && t.Type == TokenType.Export && prePos + 1 < _tokens.Count && _tokens[prePos + 1].Type == TokenType.TypeAlias) { prePos++; continue; } else if (depth == 0 && t.Type == TokenType.TypeAlias && prePos + 1 < _tokens.Count && _tokens[prePos + 1].Type == TokenType.Identifier) {
        var aliasName = _tokens[prePos + 1].Value;
        if (!_typeRegistry.ContainsKey(aliasName))
          _typeRegistry[aliasName] = new MlirStructType(aliasName, []);
        prePos += 2;
      } else { prePos++; }
    }
  }

  /// <summary>
  /// Copies only typealias-related state to the module. Unlike CopyStateToModule,
  /// this avoids re-evaluating export status of non-typealias types.
  /// </summary>
  private void CopyTypeAliasesToModule(MlirModule<MaxonOp> module) {
    foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
      if (_seededTypeAliases.Contains(aliasName)) continue;
      if (_typeRegistry.TryGetValue(aliasName, out var aliasType))
        module.TypeDefs[aliasName] = aliasType;
      var typeParams = aliasType is MlirStructType st && st.TypeParams.Count > 0
        ? new Dictionary<string, MlirType>(st.TypeParams)
        : aliasType is MlirUnionType ut && ut.TypeParams != null && ut.TypeParams.Count > 0
          ? new Dictionary<string, MlirType>(ut.TypeParams) : null;
      bool isExported = _exportedTypeAliases.Contains(aliasName);
      module.TypeAliasSources[aliasName] = new TypeAliasInfo(sourceTypeName, typeParams,
          isExported, _isStdlib, _sourceFilePath);
      if (_sourceFilePath != null)
        module.TypeDefSourceFiles[aliasName] = _sourceFilePath;
      if (!isExported && !_isStdlib)
        module.NonExportedTypeNames.Add(aliasName);
    }
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
    foreach (var (aliasName, aliasInfo) in source.TypeAliasSources) {
      if (aliasInfo.IsExported || aliasInfo.IsStdlib) {
        if (_typeAliasSources.TryAdd(aliasName, aliasInfo.SourceTypeName))
          _seededTypeAliases.Add(aliasName);
      }
    }
    // Carry forward deferred global inits so the main() parser can emit cross-file inits
    foreach (var init in source.DeferredGlobalInits) {
      if (!target.DeferredGlobalInits.Any(d => d.Name == init.Name))
        target.DeferredGlobalInits.Add(init);
    }
    // Seed exported global variables so cross-file references resolve
    foreach (var (name, meta) in source.GlobalVarInfos) {
      if (!source.NonExportedGlobalVarNames.Contains(name))
        _globalVars.TryAdd(name, meta);
    }
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
    CopyTypeAliasesToModule(module);
    // Track non-exported types/enums (only for types defined in this file)
    foreach (var (name, type) in _typeRegistry) {
      if ((type is MlirStructType || type is MlirUnionType || type is MlirRangedPrimitiveType)
          && !_exportedTypes.Contains(name)
          && !_exportedTypeAliases.Contains(name)
          && !_isStdlib
          && module.TypeDefSourceFiles.TryGetValue(name, out var src) && src == _sourceFilePath)
        module.NonExportedTypeNames.Add(name);
    }
    foreach (var (name, assocTypes) in _interfaceAssociatedTypes)
      module.InterfaceAssociatedTypes.TryAdd(name, assocTypes);
    foreach (var (name, interfaces) in _primitiveConformances)
      module.PrimitiveConformances[name] = [.. interfaces];
    foreach (var cc in _conditionalConformances)
      if (!module.ConditionalConformances.Any(e => e.SourceTypeName == cc.SourceTypeName
          && e.Interfaces.SequenceEqual(cc.Interfaces)))
        module.ConditionalConformances.Add(cc);
  }

  // ============================================================================
  // Pre-scanning for top-level let and var declarations
  // ===========================================================================

  // Raw constant declaration: stores the token range for the initializer expression
  private record ConstantDecl(string Name, int TokenStart, int TokenEnd, int Line, int Column);

  private void CollectAndEvaluateTopLevelDecls(MlirModule<MaxonOp> module) {
    var constDecls = new List<ConstantDecl>();
    int savedPos = _pos;

    PreRegisterTopLevelTypeAliasNames();

    // First pass: scan for top-level declarations (constants, vars, function signatures, type declarations)
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      SkipNewlines();
      if (IsAtEnd() || Current().Type == TokenType.Eof) break;

      bool isExported = false;
      if (Check(TokenType.Export)) {
        isExported = true;
        Advance();
      }

      if (Check(TokenType.Let)) {
        Advance(); // consume 'let'
        var nameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Equals);
        int exprStart = _pos;
        SkipToEndOfLine();
        if (IsComplexInitializer(exprStart)) {
          _deferredExprLets.Add(new DeferredDecl(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column, isExported));
          module.DeferredGlobalInits.Add(new DeferredGlobalInit(
            nameToken.Value, _tokens, exprStart, _pos, IsMutable: false, nameToken.Line, nameToken.Column, _sourceFilePath));
        } else {
          constDecls.Add(new ConstantDecl(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column));
        }
      } else if (Check(TokenType.Var)) {
        Advance(); // consume 'var'
        var nameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Equals);
        int exprStart = _pos;
        SkipToEndOfLine();
        if (IsComplexInitializer(exprStart)) {
          _deferredExprVars.Add(new DeferredDecl(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column, isExported));
          module.DeferredGlobalInits.Add(new DeferredGlobalInit(
            nameToken.Value, _tokens, exprStart, _pos, IsMutable: true, nameToken.Line, nameToken.Column, _sourceFilePath));
        } else {
          _deferredGlobalVars.Add(new DeferredDecl(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column, isExported));
        }
      } else if (Check(TokenType.Function)) {
        // Pre-register function signature for forward references
        PreScanFunction(module, null, isExported);
      } else if (Check(TokenType.Type)) {
        PreScanType(module, isExported);
      } else if (Check(TokenType.Union)) {
        PreScanUnion(module, isExported);
      } else if (Check(TokenType.Enum)) {
        PreScanEnum(module, isExported);
      } else if (Check(TokenType.TypeAlias)) {
        PreScanTypeAlias(isExported);
      } else if (Check(TokenType.Interface)) {
        PreScanInterface();
      } else if (Check(TokenType.Extension)) {
        PreScanExtensionBlock(module);
      } else if (Check(TokenType.HashIf)) {
        HandleConditionalCompilation();
        continue;
      } else if (Check(TokenType.HashElse)) {
        HandleConditionalElse();
        continue;
      } else if (Check(TokenType.HashEndif)) {
        HandleConditionalEndif();
        continue;
      } else {
        // Unknown top-level token, skip to next line
        SkipToEndOfLine();
      }
      SkipNewlines();
    }

    _pos = savedPos;

    DetectCircularTypeAliases();

    // Auto-add Cloneable/Equatable conformance for structs whose fields all conform
    ResolveAutoCloneableConformance(module);
    ResolveAutoEquatableConformance(module);

    // Evaluate constants (handling forward references)
    var evaluated = new Dictionary<string, object>();
    var evaluating = new HashSet<string>();

    foreach (var decl in constDecls) {
      EvaluateConstant(decl.Name, constDecls, evaluated, evaluating, decl.Line, decl.Column);
    }

    _topLevelConstants = evaluated;

    // Evaluate deferred global vars using the same constant expression evaluator
    foreach (var deferred in _deferredGlobalVars) {
      int savedPos2 = _pos;
      _pos = deferred.TokenStart;
      var value = EvalConstExpr(constDecls, evaluated, evaluating);
      _pos = savedPos2;

      var (fieldType, defaultValue) = ConstValueToAttribute(value, deferred.Line, deferred.Column);
      // Use ranged type's optimal storage size for the global (e.g., U16{42} → i16)
      if (_tokens[deferred.TokenStart].Type == TokenType.Identifier
          && _typeRegistry.TryGetValue(_tokens[deferred.TokenStart].Value, out var rangedGlobalType)
          && rangedGlobalType is MlirRangedPrimitiveType rpt) {
        fieldType = rpt.OptimalType;
        defaultValue = new IntegerAttr(((IntegerAttr)defaultValue).Value, fieldType);
      }
      var kind = fieldType.ToValueKind();
      string? enumTypeName = value is EnumConstantValue ecv ? ecv.EnumTypeName : null;
      module.Globals.Add(new MlirGlobal(deferred.Name, fieldType, defaultValue));
      var gvarInfo = new GlobalVarMetadata(kind, true, enumTypeName);
      _globalVars[deferred.Name] = gvarInfo;
      module.GlobalVarInfos[deferred.Name] = gvarInfo;
      if (!deferred.IsExported && !_isStdlib) {
        module.NonExportedGlobalVarNames.Add(deferred.Name);
      }
    }

    // Register deferred expression vars/lets as globals (initialized at runtime in main)
    foreach (var deferred in _deferredExprVars) {
      var typeName = InferDeferredTypeName(deferred);
      module.Globals.Add(new MlirGlobal(deferred.Name, MlirType.I64, new IntegerAttr(0, MlirType.I64)));
      var gvarInfo = new GlobalVarMetadata(MaxonValueKind.Struct, true, TypeName: typeName);
      _globalVars[deferred.Name] = gvarInfo;
      module.GlobalVarInfos[deferred.Name] = gvarInfo;
      if (!deferred.IsExported && !_isStdlib) {
        module.NonExportedGlobalVarNames.Add(deferred.Name);
      }
    }
    foreach (var deferred in _deferredExprLets) {
      var typeName = InferDeferredTypeName(deferred);
      module.Globals.Add(new MlirGlobal(deferred.Name, MlirType.I64, new IntegerAttr(0, MlirType.I64)));
      var gvarInfo = new GlobalVarMetadata(MaxonValueKind.Struct, false, TypeName: typeName);
      _globalVars[deferred.Name] = gvarInfo;
      module.GlobalVarInfos[deferred.Name] = gvarInfo;
      if (!deferred.IsExported && !_isStdlib) {
        module.NonExportedGlobalVarNames.Add(deferred.Name);
      }
    }
  }

  private (MlirType type, MlirAttribute attr) ConstValueToAttribute(object value, int line, int column) {
    if (value is EnumConstantValue ec && _typeRegistry.TryGetValue(ec.EnumTypeName, out var enumType)) {
      return (enumType, new IntegerAttr(ec.Ordinal, MlirType.I64));
    }
    return value switch {
      long l => (MlirType.I64, new IntegerAttr(l, MlirType.I64)),
      double d => (MlirType.F64, new FloatAttr(d, MlirType.F64)),
      bool b => (MlirType.I1, new IntegerAttr(b ? 1 : 0, MlirType.I1)),
      _ => throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Unsupported constant expression type for var initializer: {value.GetType().Name}", line, column)
    };
  }

  /// <summary>
  /// Check if a global var/let initializer needs deferred parsing via ParseExpression.
  /// Array literals, map literals, and struct constructors can't be evaluated as constants.
  /// </summary>
  private bool IsComplexInitializer(int exprStart) {
    if (_tokens[exprStart].Type == TokenType.LeftBracket) return true;
    if (_tokens[exprStart].Type == TokenType.Identifier
        && exprStart + 1 < _tokens.Count
        && _tokens[exprStart + 1].Type == TokenType.LeftBrace) {
      // Ranged type constructions (e.g., U16{42}) are simple constants, not complex initializers
      if (_typeRegistry.TryGetValue(_tokens[exprStart].Value, out var t) && t is MlirRangedPrimitiveType)
        return false;
      return true;
    }
    return false;
  }

  /// <summary>
  /// Check if a static field initializer needs lazy initialization.
  /// Includes all complex initializer patterns plus function calls.
  /// </summary>
  private bool IsComplexStaticInitializer(int exprStart) {
    if (IsComplexInitializer(exprStart)) return true;
    if (_tokens[exprStart].Type == TokenType.Identifier) {
      var next = exprStart + 1 < _tokens.Count ? _tokens[exprStart + 1].Type : TokenType.Eof;
      // Function call: Identifier( or Identifier.Identifier(
      if (next == TokenType.LeftParen) return true;
      if (next == TokenType.Dot
          && exprStart + 2 < _tokens.Count && _tokens[exprStart + 2].Type == TokenType.Identifier
          && exprStart + 3 < _tokens.Count && _tokens[exprStart + 3].Type == TokenType.LeftParen)
        return true;
      // Collection initializer: Identifier from [
      if (next == TokenType.From
          && exprStart + 2 < _tokens.Count && _tokens[exprStart + 2].Type == TokenType.LeftBracket)
        return true;
    }
    return false;
  }

  /// <summary>
  /// Infer a preliminary TypeName for a deferred global from its tokens.
  /// For struct constructors (Identifier{}) use the identifier; for [k:v] creates
  /// a concrete Map type alias; for [...] use "Array".
  /// </summary>
  private string InferDeferredTypeName(DeferredDecl deferred) {
    if (_tokens[deferred.TokenStart].Type == TokenType.Identifier) {
      var name = _tokens[deferred.TokenStart].Value;
      var next = deferred.TokenStart + 1 < _tokens.Count ? _tokens[deferred.TokenStart + 1].Type : TokenType.Eof;

      // Function call: name() — resolve return type from pre-scanned function
      if (next == TokenType.LeftParen) {
        var resolved = InferReturnTypeFromCall(name);
        if (resolved != null) return resolved;
      }

      // Static method call: Type.method() — resolve return type from pre-scanned method
      if (next == TokenType.Dot
          && deferred.TokenStart + 2 < _tokens.Count && _tokens[deferred.TokenStart + 2].Type == TokenType.Identifier
          && deferred.TokenStart + 3 < _tokens.Count && _tokens[deferred.TokenStart + 3].Type == TokenType.LeftParen) {
        var methodName = _tokens[deferred.TokenStart + 2].Value;
        var resolved = InferReturnTypeFromCall($"{name}.{methodName}");
        if (resolved != null) return resolved;
      }

      return name;
    }
    if (_tokens[deferred.TokenStart].Type == TokenType.LeftBracket && IsMapLiteralAt(deferred.TokenStart))
      return InferMapTypeAlias(deferred.TokenStart);
    if (_tokens[deferred.TokenStart].Type == TokenType.LeftBracket)
      return InferArrayTypeAlias(deferred.TokenStart);
    var startToken = _tokens[deferred.TokenStart];
    throw new CompileError(ErrorCode.ParserExpectedExpression,
      $"Cannot infer type of global initializer from '{startToken.Type}' token",
      startToken.Line, startToken.Column);
  }

  /// <summary>
  /// Look up a pre-scanned function's return type name.
  /// Tries exact match, then suffix match for namespace-qualified names.
  /// </summary>
  private string? InferReturnTypeFromCall(string calleeName) {
    if (_currentModule == null) return null;
    var func = _currentModule.Functions.FirstOrDefault(f => f.Name == calleeName)
            ?? _currentModule.Functions.FirstOrDefault(f => f.Name.EndsWith($".{calleeName}"));
    if (func?.ReturnType != null)
      return MlirType.Resolve(func.ReturnType).Name;
    return null;
  }

  /// <summary>
  /// Infer the concrete Array type alias for an array literal by peeking at the first element.
  /// Returns the appropriate alias (e.g., "StringArray", "__Array_String") so that
  /// for-in loops can resolve the correct element type.
  /// </summary>
  private string InferArrayTypeAlias(int bracketPos) {
    int pos = bracketPos + 1; // skip '['
    while (pos < _tokens.Count && _tokens[pos].Type == TokenType.Newline) pos++;

    var elementType = InferTypeFromTokens(pos, out _);
    var elementKind = elementType.ToValueKind();
    string? elementStructTypeName = elementType is MlirStructType st ? st.Name
      : elementType is MlirUnionType en ? en.Name : null;

    var alias = FindArrayTypeAliasForElement(elementKind, elementStructTypeName);
    Logger.Debug(LogCategory.Parser, $"Inferred array type alias '{alias}' from element type '{elementType.Name}'");
    return alias;
  }

  /// <summary>
  /// Peek at tokens starting from a '[' to determine if it's a map literal ([k: v, ...])
  /// vs an array literal ([v, v, ...]). Tracks nesting to avoid false matches on ':' inside
  /// nested expressions like [foo(a: 1)].
  /// </summary>
  private bool IsMapLiteralAt(int bracketPos) {
    int depth = 0;
    for (int i = bracketPos; i < _tokens.Count; i++) {
      var type = _tokens[i].Type;
      if (type == TokenType.LeftParen || type == TokenType.LeftBracket || type == TokenType.LeftBrace)
        depth++;
      else if (type == TokenType.RightParen || type == TokenType.RightBracket || type == TokenType.RightBrace) {
        depth--;
        if (depth <= 0) return false;
      } else if (type == TokenType.Colon && depth == 1)
        return true;
      else if (type == TokenType.Comma && depth == 1)
        return false;
    }
    return false;
  }

  /// <summary>
  /// Create the concrete Map type alias for a map literal by peeking at the first
  /// key:value pair tokens. This enables monomorphization to find the right specialization
  /// before function bodies that reference the global are parsed.
  /// </summary>
  private string InferMapTypeAlias(int bracketPos) {
    int pos = bracketPos + 1; // skip '['
    while (pos < _tokens.Count && _tokens[pos].Type == TokenType.Newline) pos++;

    var keyType = InferTypeFromTokens(pos, out var keyEnd);
    pos = keyEnd;
    while (pos < _tokens.Count && _tokens[pos].Type == TokenType.Newline) pos++;
    if (pos < _tokens.Count && _tokens[pos].Type == TokenType.Colon) pos++;
    while (pos < _tokens.Count && _tokens[pos].Type == TokenType.Newline) pos++;

    var valueType = InferTypeFromTokens(pos, out _);

    var mapSourceTypeName = FindTypeImplementingInterface("BuiltinDictionaryLiteral") ?? "Map";
    return FindOrCreateMapTypeAlias(mapSourceTypeName, keyType, valueType);
  }

  /// <summary>
  /// Infer the MlirType of a simple expression from tokens without parsing.
  /// Used during pre-scanning to determine element types of top-level collection literals.
  /// </summary>
  private MlirType InferTypeFromTokens(int pos, out int endPos) {
    endPos = pos + 1;
    if (pos >= _tokens.Count)
      throw new CompileError(ErrorCode.ParserExpectedExpression,
        "Expected expression in global initializer but reached end of tokens", null, null);

    var token = _tokens[pos];
    switch (token.Type) {
      case TokenType.Minus:
        // Negative literal: -NUMBER
        endPos = pos + 2;
        if (pos + 1 < _tokens.Count && _tokens[pos + 1].Type == TokenType.FloatLiteral)
          return MlirType.F64;
        return MlirType.I64;
      case TokenType.IntegerLiteral:
        return MlirType.I64;
      case TokenType.FloatLiteral:
        return MlirType.F64;
      case TokenType.True:
      case TokenType.False:
        return MlirType.I1;
      case TokenType.StringLiteral:
      case TokenType.StringInterp:
        if (_typeRegistry.TryGetValue("String", out var strType)) return strType;
        throw new CompileError(ErrorCode.ParserExpectedType,
          "String type not found in type registry; is the standard library loaded?", token.Line, token.Column);
      case TokenType.CharacterLiteral:
        if (_typeRegistry.TryGetValue("Character", out var charType)) return charType;
        throw new CompileError(ErrorCode.ParserExpectedType,
          "Character type not found in type registry; is the standard library loaded?", token.Line, token.Column);
      case TokenType.ByteStringLiteral:
        var alias = FindArrayTypeAliasForElement(MaxonValueKind.Byte);
        if (_typeRegistry.TryGetValue(alias, out var bstrType)) return bstrType;
        throw new CompileError(ErrorCode.ParserExpectedType,
          $"ByteArray type alias '{alias}' not found in type registry; is the standard library loaded?", token.Line, token.Column);
      case TokenType.Identifier:
        // Enum/struct reference: Type.case
        if (pos + 2 < _tokens.Count && _tokens[pos + 1].Type == TokenType.Dot) {
          endPos = pos + 3;
          if (_typeRegistry.TryGetValue(token.Value, out var enumType)) {
            // Return the enum type so array literals infer as __Array_EnumName
            if (enumType is MlirEnumType met) return met;
            return enumType;
          }
        }
        // Struct constructor: TypeName{...}
        if (pos + 1 < _tokens.Count && _tokens[pos + 1].Type == TokenType.LeftBrace
            && _typeRegistry.TryGetValue(token.Value, out var ctorType)
            && ctorType is MlirStructType) {
          endPos = pos + 1;
          return ctorType;
        }
        // Bare variable reference — can't resolve type without full expression parsing
        return MlirType.I64;
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression,
      $"Cannot infer type of global initializer from '{token.Type}' token", token.Line, token.Column);
  }

  /// <summary>
  /// Emit deferred top-level expression declarations at the start of the main function body.
  /// Re-parses each saved token range via ParseExpression, then stores the result in globals.
  /// TypeName is determined from the expression result (Array, Map typealias, struct name, etc).
  /// </summary>
  private void EmitDeferredExprDecls(List<DeferredDecl> deferred, bool isMutable) {
    foreach (var decl in deferred)
      EmitSingleDeferredGlobalInit(decl.Name, _tokens, decl.TokenStart, isMutable);
  }

  /// <summary>
  /// Emit deferred global inits from other files in a multi-file build.
  /// Skips names already handled by the current parser's local deferred lists.
  /// </summary>
  private void EmitCrossFileDeferredGlobalInits() {
    var localNames = new HashSet<string>(
      _deferredExprLets.Select(d => d.Name).Concat(_deferredExprVars.Select(d => d.Name)));

    foreach (var init in _currentModule!.DeferredGlobalInits) {
      if (localNames.Contains(init.Name)) continue;

      // Deferred inits reference types from their original file, which may not be exported
      var tempTypes = new List<string>();
      if (init.SourceFilePath != null && seedModule != null) {
        foreach (var (name, type) in seedModule.TypeDefs) {
          if (!_typeRegistry.ContainsKey(name)
              && seedModule.NonExportedTypeNames.Contains(name)
              && seedModule.TypeDefSourceFiles.TryGetValue(name, out var src)
              && src == init.SourceFilePath) {
            _typeRegistry[name] = type;
            tempTypes.Add(name);
          }
        }
      }

      EmitSingleDeferredGlobalInit(init.Name, init.Tokens, init.TokenStart, init.IsMutable);

      foreach (var name in tempTypes)
        _typeRegistry.Remove(name);
    }
  }

  /// <summary>
  /// Emit a separate __module_init function containing all deferred global inits.
  /// This function has NO function scope — it runs in the root scope so globals survive.
  /// Called from _start before main.
  /// </summary>
  private void EmitModuleInitFunction(MlirModule<MaxonOp> module) {
    bool hasDeferred = _deferredExprLets.Count > 0 || _deferredExprVars.Count > 0
      || _currentModule!.DeferredGlobalInits.Any(init =>
        !_deferredExprLets.Any(d => d.Name == init.Name) && !_deferredExprVars.Any(d => d.Name == init.Name));
    if (!hasDeferred) return;

    // Save current parsing state
    var savedFunction = _currentFunction;
    var savedBlock = _currentBlock;

    // Create __module_init function (unqualified — compiler-internal)
    var initFuncName = "__module_init";
    var initFunc = new MlirFunction<MaxonOp>(initFuncName, [], [], returnType: null, throwsType: null);
    module.AddFunction(initFunc);
    _currentFunction = initFunc;
    _currentBlock = initFunc.Body.AddBlock("entry");

    // Emit deferred global inits
    EmitDeferredExprDecls(_deferredExprLets, isMutable: false);
    EmitDeferredExprDecls(_deferredExprVars, isMutable: true);
    EmitCrossFileDeferredGlobalInits();

    _currentBlock.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock.AddOp(new MaxonReturnOp());

    // Restore parsing state
    _currentFunction = savedFunction;
    _currentBlock = savedBlock;
  }

  /// <summary>
  /// Generate lazy init functions for static fields with complex initializers.
  /// Each lazy field gets an init function that sets the guard and evaluates the initializer.
  /// The guard check happens at each load site (in MaxonToStandardConversion).
  /// </summary>
  private void EmitLazyStaticInitFunctions(MlirModule<MaxonOp> module) {
    if (_lazyStaticFields.Count == 0) return;

    var savedFunction = _currentFunction;
    var savedBlock = _currentBlock;
    var savedVariables = _variables.SaveAll();

    try {
      foreach (var field in _lazyStaticFields) {
        // Each lazy init gets a clean variable scope — prevents stray variables
        // from prior functions or prior lazy inits leaking into this scope_end.
        _variables.Clear();

        var initFuncName = $"{field.QualifiedName}.__lazy_init";

        var initFunc = new MlirFunction<MaxonOp>(initFuncName, [], [], returnType: null, throwsType: null);
        module.AddFunction(initFunc);
        _currentFunction = initFunc;
        _currentBlock = initFunc.Body.AddBlock("entry");

        // Set guard to true before evaluating (prevents infinite recursion)
        var trueConst = new MaxonLiteralOp(true);
        _currentBlock.AddOp(trueConst);
        _currentBlock.AddOp(new MaxonGlobalStoreOp(field.GuardName, trueConst.Result, MaxonValueKind.Bool));

        // Evaluate the initializer expression and store result
        EmitSingleDeferredGlobalInit(field.QualifiedName, field.Tokens, field.TokenStart, field.IsMutable);

        _currentBlock.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
        _currentBlock.AddOp(new MaxonReturnOp());
      }
    } finally {
      _variables.RestoreAll(savedVariables);
      _currentFunction = savedFunction;
      _currentBlock = savedBlock;
    }
  }

  /// <summary>
  /// Parse and emit a single deferred global variable initialization.
  /// Temporarily swaps the token list and position if needed.
  /// </summary>
  private void EmitSingleDeferredGlobalInit(string name, List<Token> tokens, int tokenStart, bool isMutable) {
    var savedTokens = _tokens;
    int savedPos = _pos;
    _tokens = tokens;
    _pos = tokenStart;

    var exprResult = ParseExpression();
    var value = ResolveExprValue(exprResult);
    _currentBlock!.AddOp(new MaxonGlobalStoreOp(name, value, MaxonValueKind.Struct));

    if (value is MaxonStruct ms)
      _globalVars[name] = new GlobalVarMetadata(MaxonValueKind.Struct, isMutable, TypeName: ms.TypeName);
    else if (value is MaxonEnum me)
      _globalVars[name] = new GlobalVarMetadata(MaxonValueKind.Enum, isMutable, EnumTypeName: me.TypeName);

    _tokens = savedTokens;
    _pos = savedPos;
  }

  /// <summary>
  /// Pre-scan a function declaration to register its signature.
  /// Only parses name, params, and return type; skips the body.
  /// </summary>
  private void PreScanFunction(MlirModule<MaxonOp> module, string? owningType, bool isExported = false) {
    Advance(); // consume 'function'
    var nameToken = ExpectIdentifierLike();
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
      // Top-level function: prepend file-based namespace (except main)
      var namespace_ = DeriveNamespace();
      funcName = (baseName == "main" || string.IsNullOrEmpty(namespace_)) ? baseName : $"{namespace_}.{baseName}";
    }
    Logger.Trace(LogCategory.Parser, $"PreScanFunction: {funcName} (base: {baseName}, file: {_sourceFilePath})");

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    var registrationName = ResolveOverloadRegistrationName(module, funcName, paramNames, paramTypes);

    if (!module.Functions.Any(f => f.Name == registrationName)) {
      var func = new MlirFunction<MaxonOp>(registrationName, paramNames, paramTypes, returnType, throwsType) {
        IsExported = isExported,
        SourceFilePath = _sourceFilePath,
        SourceLine = nameToken.Line,
        SourceColumn = nameToken.Column
      };
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
  /// Parse an 'implements' clause (e.g., 'implements Equatable') for interface conformance.
  /// Returns the list of interface names the type conforms to.
  /// </summary>
  private (List<string> Interfaces, Dictionary<string, MlirType> TypeParams) ParseConformanceClause() {
    var names = new List<string>();
    var typeParams = new Dictionary<string, MlirType>();
    if (!Check(TokenType.Implements)) return (names, typeParams);

    Advance(); // consume 'implements'
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
  /// Parse a where clause: where TypeParam is Interface [and Interface2] [, TypeParam2 is Interface3]
  /// Returns a mapping from type parameter names to their required interface names.
  /// </summary>
  private Dictionary<string, List<string>> ParseWhereClause(List<string> associatedTypeNames) {
    var constraints = new Dictionary<string, List<string>>();
    if (!Check(TokenType.Where)) return constraints;

    Advance(); // consume 'where'

    while (true) {
      var paramToken = Expect(TokenType.Identifier);
      var paramName = paramToken.Value;

      if (!associatedTypeNames.Contains(paramName))
        throw new CompileError(ErrorCode.ParserUnexpectedToken,
          $"'{paramName}' is not a type parameter of this type",
          paramToken.Line, paramToken.Column);

      Expect(TokenType.Is);

      var interfaces = new List<string> {
        Expect(TokenType.Identifier).Value
      };

      // 'and' for multiple interfaces on the same param
      while (Check(TokenType.And)) {
        Advance(); // consume 'and'
        interfaces.Add(Expect(TokenType.Identifier).Value);
      }

      constraints[paramName] = interfaces;

      // Comma separates constraints for different type parameters
      if (!Check(TokenType.Comma)) break;
      Advance(); // consume ','
    }

    return constraints;
  }

  /// <summary>
  /// Parse type arguments after 'with': either a single type or (Type1, Type2, ...).
  /// When expectedCount > 1 and no parentheses, consumes comma-separated types.
  /// </summary>
  private List<MlirType> ParseWithTypeArgs(int expectedCount) {
    if (Check(TokenType.LeftParen)) {
      // When expecting a single type arg, let ParseTypeRef handle it —
      // (A, B) is a tuple type, not two separate type arguments
      if (expectedCount == 1) {
        return [ParseTypeRef()];
      }
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
                 || t.Type == TokenType.Union || t.Type == TokenType.Enum || t.Type == TokenType.Interface) {
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

  private void PreScanType(MlirModule<MaxonOp> module, bool isExported = false) {
    Advance(); // consume 'type'
    var typeNameToken = Expect(TokenType.Identifier);
    var typeName = typeNameToken.Value;
    _currentTypeName = typeName;
    Logger.Trace(LogCategory.Parser, $"PreScanType: {typeName} (file: {_sourceFilePath})");

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
    var whereConstraints = ParseWhereClause(associatedTypeNames);

    SkipNewlines();

    var fields = new List<MlirStructField>();

    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      // Conditional compilation inside type body (pre-scan)
      if (Check(TokenType.HashIf)) {
        HandleConditionalCompilation();
        continue;
      }
      if (Check(TokenType.HashElse)) {
        HandleConditionalElse();
        continue;
      }
      if (Check(TokenType.HashEndif)) {
        HandleConditionalEndif();
        continue;
      }

      bool isFieldExported = false;
      if (Check(TokenType.Export)) {
        Advance();
        isFieldExported = true;
      }

      if (Check(TokenType.Static)) {
        Advance(); // consume 'static'

        if (Check(TokenType.Function)) {
          PreScanFunction(module, typeName, isFieldExported);
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
        PreScanInstanceMethod(module, typeName, isFieldExported);
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
        }

        fields.Add(new MlirStructField(fieldName, fieldType, isFieldExported, isMutable, defaultValue));
        SkipNewlines();
        continue;
      }

      // If we consumed 'export' but didn't match any known member, it's likely a missing var/let
      if (isFieldExported && !Check(TokenType.End) && !IsAtEnd()) {
        var badToken = Current();
        throw new CompileError(ErrorCode.ParserExpectedToken,
          $"Expected 'var', 'let', 'function', or 'static' after 'export' in type '{typeName}', got '{badToken.Value}'",
          badToken.Line, badToken.Column);
      }

      SkipToEndOfLine();
      SkipNewlines();
    }

    // Replace temporary entry with complete struct type
    var completedStruct = new MlirStructType(typeName, fields, associatedTypeNames, conformingInterfaces,
      typeParams: conformanceTypeParams.Count > 0 ? conformanceTypeParams : null,
      whereConstraints: whereConstraints.Count > 0 ? whereConstraints : null) {
      SourceFilePath = _sourceFilePath,
      SourceLine = typeNameToken.Line,
      SourceColumn = typeNameToken.Column
    };
    _typeRegistry[typeName] = completedStruct;
    _locallyDefinedTypes.Add(typeName);
    if (isExported) _exportedTypes.Add(typeName);
    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);

    ValidateInterfaceConformance(module, typeName, conformingInterfaces, conformanceTypeParams, typeNameToken);

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  /// <summary>
  /// Check whether a concrete type name conforms to a given interface.
  /// </summary>
  private bool TypeConformsToInterface(string concreteTypeName, string requiredInterface) {
    if (_typeRegistry.TryGetValue(concreteTypeName, out var typeEntry)) {
      if (typeEntry is MlirStructType st && st.ConformingInterfaces.Contains(requiredInterface))
        return true;
      if (typeEntry is MlirUnionType et && et.ConformingInterfaces.Contains(requiredInterface))
        return true;
      // Ranged primitive types inherit conformance from their base type
      if (typeEntry is MlirRangedPrimitiveType rpt)
        return TypeConformsToInterface(MlirType.FormatAsSourceName(rpt.BaseType), requiredInterface);
    }
    if (_primitiveConformances.TryGetValue(concreteTypeName, out var extInterfaces)
        && extInterfaces.Contains(requiredInterface))
      return true;
    return false;
  }

  /// <summary>
  /// After all types are pre-scanned, auto-add Cloneable conformance to structs
  /// whose fields are all Cloneable. Uses topological resolution to handle structs
  /// that contain other structs.
  /// </summary>
  private void ResolveAutoCloneableConformance(MlirModule<MaxonOp> module) {
    ResolveAutoConformance(module, "Cloneable", PreRegisterSyntheticStructClone);
  }

  private void ResolveAutoEquatableConformance(MlirModule<MaxonOp> module) {
    ResolveAutoConformance(module, "Equatable", PreRegisterSyntheticStructEquals);
  }

  /// <summary>
  /// Auto-add interface conformance to locally-defined struct types whose fields all conform.
  /// Uses iterative resolution to handle structs containing other structs (topological order).
  /// Only processes types defined in this parse session — stdlib types declare conformance explicitly.
  /// </summary>
  private void ResolveAutoConformance(
      MlirModule<MaxonOp> module,
      string interfaceName,
      Action<MlirModule<MaxonOp>, string, MlirStructType> preRegisterStub) {
    var candidates = new Dictionary<string, MlirStructType>();
    foreach (var (name, type) in _typeRegistry) {
      if (type is MlirStructType st && !st.ConformingInterfaces.Contains(interfaceName)
          && (_locallyDefinedTypes.Contains(name) || st.IsTuple)) {
        candidates[name] = st;
      }
    }

    bool changed = true;
    while (changed) {
      changed = false;
      foreach (var (name, st) in candidates) {
        if (st.ConformingInterfaces.Contains(interfaceName)) continue;

        bool allFieldsConform = true;
        foreach (var field in st.Fields) {
          // Primitives, ranged primitives, and enums/unions without associated values
          // inherently conform to Cloneable/Equatable (they are value types)
          if (field.Type is MlirRangedPrimitiveType or MlirEnumType
              || (field.Type is MlirUnionType ut && !ut.HasAssociatedValues)
              || (field.Type is not MlirStructType and not MlirUnionType
                  and not MlirInterfaceType and not MlirFunctionType and not MlirTypeParameterType))
            continue;
          var fieldTypeName = MlirType.FormatAsSourceName(field.Type);
          if (field.Type is MlirStructType fieldStruct) {
            fieldTypeName = fieldStruct.Name;
          }
          if (!TypeConformsToInterface(fieldTypeName, interfaceName)) {
            allFieldsConform = false;
            break;
          }
        }

        if (allFieldsConform) {
          st.ConformingInterfaces.Add(interfaceName);
          preRegisterStub(module, name, st);
          changed = true;
        }
      }
    }
  }

  /// <summary>
  /// Pre-register a stub clone() method for auto-Cloneable struct types.
  /// </summary>
  private void PreRegisterSyntheticStructClone(MlirModule<MaxonOp> module, string typeName, MlirStructType structType) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";

    var cloneName = $"{qualifiedTypeName}.clone";
    if (!module.Functions.Any(f => f.Name == cloneName)) {
      var cloneFunc = new MlirFunction<MaxonOp>(
        cloneName, ["self"], [(MlirType)structType], (MlirType)structType, null) {
        SourceFilePath = _sourceFilePath
      };
      module.AddFunction(cloneFunc);
    }
  }

  /// <summary>
  /// Pre-register a stub equals() method for auto-Equatable struct types.
  /// </summary>
  private void PreRegisterSyntheticStructEquals(MlirModule<MaxonOp> module, string typeName, MlirStructType structType) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";

    var equalsName = $"{qualifiedTypeName}.equals";
    if (!module.Functions.Any(f => f.Name == equalsName)) {
      var equalsFunc = new MlirFunction<MaxonOp>(
        equalsName, ["self", "other"], [(MlirType)structType, (MlirType)structType], MlirType.I1, null) {
        SourceFilePath = _sourceFilePath
      };
      module.AddFunction(equalsFunc);
    }
  }

  private void ValidateWhereConstraints(
      MlirStructType sourceStruct, Dictionary<string, MlirType> substitution,
      string sourceName, Token errorToken) {
    if (_skipWhereValidation) return;
    foreach (var (paramName, requiredInterfaces) in sourceStruct.WhereConstraints) {
      if (!substitution.TryGetValue(paramName, out var concreteType)) continue;
      // Skip validation when the concrete type is still an unresolved type parameter
      if (concreteType is MlirTypeParameterType) continue;

      var concreteTypeName = MlirType.FormatAsSourceName(concreteType);
      foreach (var requiredInterface in requiredInterfaces) {
        if (!TypeConformsToInterface(concreteTypeName, requiredInterface))
          throw new CompileError(ErrorCode.SemanticWhereConstraintViolation,
            $"Type '{concreteTypeName}' does not satisfy constraint '{requiredInterface}' required by type parameter '{paramName}' of '{sourceName}'",
            errorToken.Line, errorToken.Column);
      }
    }
  }

  /// <summary>
  /// Check whether a conforming type's associated type bindings satisfy all where constraints.
  /// Used by conditional extensions to filter which types receive the extension methods.
  /// </summary>
  private bool TypeSatisfiesWhereConstraints(
      MlirStructType structType, Dictionary<string, List<string>> whereConstraints) {
    foreach (var (paramName, requiredInterfaces) in whereConstraints) {
      if (!structType.TypeParams.TryGetValue(paramName, out var concreteType)) return false;
      // Unresolved type parameters are allowed (generic synthesis will handle them)
      if (concreteType is MlirTypeParameterType) continue;

      var concreteTypeName = MlirType.FormatAsSourceName(concreteType);
      foreach (var requiredInterface in requiredInterfaces) {
        if (!TypeConformsToInterface(concreteTypeName, requiredInterface))
          return false;
      }
    }
    return true;
  }

  /// <summary>
  /// Validates that a type implements all methods required by its declared conforming interfaces.
  /// </summary>
  private void ValidateInterfaceConformance(
      MlirModule<MaxonOp> module, string typeName, List<string> conformingInterfaces,
      Dictionary<string, MlirType> typeParams, Token nameToken) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";

    foreach (var ifaceName in conformingInterfaces) {
      if (!_typeRegistry.TryGetValue(ifaceName, out var ifaceType) || ifaceType is not MlirInterfaceType iface)
        continue;

      if (_interfaceAssociatedTypes.TryGetValue(ifaceName, out var assocTypeNames) && assocTypeNames.Count > 0) {
        var missingBindings = assocTypeNames.Where(n => !typeParams.ContainsKey(n)).ToList();
        if (missingBindings.Count > 0) {
          throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
            $"Type '{typeName}' does not define required associated type '{missingBindings[0]}' from interface '{ifaceName}'",
            nameToken.Line, nameToken.Column);
        }
      }

      var allMethods = new List<(MlirInterfaceMethodSignature Method, string SourceInterface)>();
      CollectInterfaceMethods(iface, ifaceName, allMethods, []);

      var missingMethods = new List<string>();
      var wrongSignatureMethods = new List<string>();
      foreach (var (method, sourceIfaceName) in allMethods) {
        var qualifiedName = $"{qualifiedTypeName}.{method.Name}";
        var func = module.Functions.FirstOrDefault(f => f.Name == qualifiedName);

        // Try mangled name for overloaded methods (e.g. toString$format)
        if (method.ParamNames.Count > 0) {
          var mangledName = MangleOverloadName($"{qualifiedTypeName}.{method.Name}", method.ParamNames);
          if (mangledName != qualifiedName) {
            var mangledFunc = module.Functions.FirstOrDefault(f => f.Name == mangledName);
            if (mangledFunc != null) func = mangledFunc;
          }
        }

        // Static method signatures are handled by the compiler directly
        if (method.IsStatic) {
          if (func != null && method.ThrowsTypeName != null)
            ValidateThrowsConformance(func, typeName, method.Name, sourceIfaceName, method.ThrowsTypeName, nameToken);
          continue;
        }

        if (func == null) {
          var sig = method.FormatResolved(typeParams);
          if (sourceIfaceName != ifaceName) sig += $" (from {sourceIfaceName})";
          missingMethods.Add(sig);
        } else if (!SignatureMatches(method, func, sourceIfaceName, typeName, typeParams)) {
          var actualSig = FormatFunctionSignature(method.Name, func);
          wrongSignatureMethods.Add($"{actualSig} (expected {method.FormatResolved(typeParams)})");
        } else if (method.ThrowsTypeName != null) {
          ValidateThrowsConformance(func, typeName, method.Name, sourceIfaceName, method.ThrowsTypeName, nameToken);
        }
      }

      if (missingMethods.Count > 0) {
        var details = string.Join("\n", missingMethods.Select(m => $"  - {m}"));
        throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
          $"Partial interface implementation: type '{typeName}' is missing {missingMethods.Count} method(s):\n{details}",
          nameToken.Line, nameToken.Column);
      }

      if (wrongSignatureMethods.Count > 0) {
        var details = string.Join("\n", wrongSignatureMethods.Select(m => $"  - {m}"));
        throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
          $"Partial interface implementation: type '{typeName}' has {wrongSignatureMethods.Count} method(s) with wrong signature:\n{details}",
          nameToken.Line, nameToken.Column);
      }
    }
  }

  /// <summary>
  /// Recursively collects all method signatures from an interface and its extended (parent) interfaces.
  /// </summary>
  private void CollectInterfaceMethods(MlirInterfaceType iface, string ifaceName,
      List<(MlirInterfaceMethodSignature Method, string SourceInterface)> result, HashSet<string> visited) {
    if (!visited.Add(ifaceName)) return;

    foreach (var method in iface.Methods) {
      result.Add((method, ifaceName));
    }

    foreach (var parentName in iface.ExtendedInterfaces) {
      if (_typeRegistry.TryGetValue(parentName, out var parentType) && parentType is MlirInterfaceType parentIface) {
        CollectInterfaceMethods(parentIface, parentName, result, visited);
      }
    }
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
        && throwsTypeEntry is MlirUnionType throwsEnum
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
      var expectedResolved = expectedTypeName != null ? ResolveTypeName(expectedTypeName) : null;
      if (expectedResolved == null || (expectedResolved != funcParamTypes[i].Name
          && !IsRangedTypeCompatibleWithBase(funcParamTypes[i], expectedResolved)))
        return false;
    }

    var expectedReturnName = ResolveInterfaceTypeName(method.ReturnTypeName, ifaceName, typeName, typeParams);
    var expectedReturn = expectedReturnName != null ? ResolveTypeName(expectedReturnName) : MlirType.Void.Name;
    var actualReturn = func.ReturnType?.Name ?? MlirType.Void.Name;
    if (expectedReturn != actualReturn && !IsRangedTypeCompatibleWithBase(func.ReturnType, expectedReturn))
      return false;

    return true;
  }

  // Ranged types are compatible with their base types in either direction for interface matching
  private bool IsRangedTypeCompatibleWithBase(MlirType? actualType, string expectedBaseName) {
    // Actual is ranged, expected is base (e.g., function returns Age, interface expects i64)
    if (actualType is MlirRangedPrimitiveType rpt)
      return rpt.BaseType.Name == expectedBaseName;
    // Expected is ranged, actual is base (e.g., interface expects Integer, function returns i64)
    if (actualType != null && _typeRegistry.TryGetValue(expectedBaseName, out var expType) && expType is MlirRangedPrimitiveType expRpt)
      return expRpt.BaseType.Name == actualType.Name;
    return false;
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
      // Use internal type name for matching, source name for display
      return resolvedType.Name;
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


  /// <summary>
  /// Check if the current position has 'function IDENTIFIER (' pattern, indicating
  /// an enum instance method declaration (not a keyword used as an enum case name).
  /// </summary>
  private bool IsEnumMethodStart() {
    if (!Check(TokenType.Function)) return false;
    if (_pos + 2 >= _tokens.Count) return false;
    return _tokens[_pos + 1].Type == TokenType.Identifier && _tokens[_pos + 2].Type == TokenType.LeftParen;
  }

  /// <summary>
  /// Check if current 'end' token is the block terminator (end 'label') vs an enum case name.
  /// Block-ending 'end' is followed by a character literal label; case-name 'end' is followed by newline/EOF.
  /// </summary>
  private bool IsEndOfBlock() {
    if (!Check(TokenType.End)) return false;
    if (_pos + 1 >= _tokens.Count) return true;
    var next = _tokens[_pos + 1].Type;
    return next == TokenType.CharacterLiteral;
  }

  /// <summary>
  /// Consume the current token as an enum case name. Accepts any non-EOF token.
  /// </summary>
  private Token ExpectEnumCaseName() {
    var token = Current();
    Advance();
    return token;
  }

  private void PreScanUnion(MlirModule<MaxonOp> module, bool isExported = false) {
    Advance(); // consume 'union'
    var nameToken = Expect(TokenType.Identifier);
    var enumName = nameToken.Value;
    _currentTypeName = enumName;
    Logger.Trace(LogCategory.Parser, $"PreScanUnion: {enumName} (file: {_sourceFilePath})");

    var associatedTypeNames = ParseUsesClause();
    var (conformingInterfaces, conformanceTypeParams) = ParseConformanceClause();
    var whereConstraints = ParseWhereClause(associatedTypeNames);

    // Temporary entry so ParseTypeRef can resolve forward references
    if (!_typeRegistry.TryGetValue(enumName, out MlirType? value)) {
      value = new MlirUnionType(enumName, [], null, conformingInterfaces,
        associatedTypeNames: associatedTypeNames);
      _typeRegistry[enumName] = value;
    } else if (value is MlirUnionType existingPre && existingPre.ConformingInterfaces.Count == 0 && conformingInterfaces.Count > 0) {
      // Pre-registered entry had no conforming interfaces; update with parsed ones
      value = new MlirUnionType(enumName, [.. existingPre.Cases], existingPre.BackingType, conformingInterfaces,
        associatedTypeNames: associatedTypeNames);
      _typeRegistry[enumName] = value;
    }

    SkipNewlines();

    var cases = new List<MlirEnumCase>();
    var caseNames = new HashSet<string>();
    MlirType? backingType = null;
    int ordinal = 0;

    while (!IsEndOfBlock() && !IsEnumMethodStart() && !IsAtEnd()) {
      SkipNewlines();
      if (IsEndOfBlock() || IsEnumMethodStart()) break;

      // Implicit string-backed ("North") or char-backed ('N'): literal as case name
      if (Check(TokenType.StringLiteral) || Check(TokenType.CharacterLiteral)) {
        bool isString = Check(TokenType.StringLiteral);
        var litToken = Advance();
        var litName = litToken.Value;

        if (!caseNames.Add(litName)) {
          throw new CompileError(ErrorCode.SemanticUnionDuplicateCase,
            $"duplicate union case: '{litName}'", litToken.Line, litToken.Column);
        }

        MlirType expectedBacking = isString ? new MlirStringBackingType() : new MlirCharBackingType();
        if (backingType == null) {
          backingType = expectedBacking;
        } else if (backingType.GetType() != expectedBacking.GetType()) {
          throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
            $"raw value type mismatch: expected {(isString ? "string" : "char")}",
            litToken.Line, litToken.Column);
        }

        foreach (var existing in cases) {
          if (existing.RawValue is string existingVal && existingVal == litName) {
            throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
              $"duplicate raw value: '{litName}'", litToken.Line, litToken.Column);
          }
        }

        cases.Add(new MlirEnumCase(litName, ordinal, litName));
        ordinal++;
        SkipNewlines();
        continue;
      }

      var caseToken = ExpectEnumCaseName();
      var caseName = caseToken.Value;

      if (!caseNames.Add(caseName)) {
        throw new CompileError(ErrorCode.SemanticUnionDuplicateCase,
          $"duplicate union case: '{caseName}'", caseToken.Line, caseToken.Column);
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
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.F64 ? "float" : "int")}, got int'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is long existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
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
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected int, got float'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is double existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
        } else if (Check(TokenType.StringLiteral)) {
          if (isNegative) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              "cannot negate a string raw value", caseToken.Line, caseToken.Column);
          }

          var rawVal = Advance().Value;

          if (backingType == null) {
            backingType = new MlirStringBackingType();
          } else if (backingType is not MlirStringBackingType) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.I64 ? "int" : backingType == MlirType.F64 ? "float" : "String")}, got String'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is string existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
        } else if (Check(TokenType.CharacterLiteral)) {
          if (isNegative) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              "cannot negate a character raw value", caseToken.Line, caseToken.Column);
          }

          var rawVal = StringUtils.ResolveEscapes(Advance().Value);

          if (backingType == null) {
            backingType = new MlirCharBackingType();
          } else if (backingType is not MlirCharBackingType) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.I64 ? "int" : backingType == MlirType.F64 ? "float" : "char")}, got char'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is string existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
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

    var existingEnum = (MlirUnionType)value;
    var finalInterfaces = new List<string>(existingEnum.ConformingInterfaces);

    // Auto-add Hashable and Equatable for enums without associated values
    bool hasAssocValues = cases.Any(c => c.AssociatedValues is { Count: > 0 });
    if (!hasAssocValues) {
      if (!finalInterfaces.Contains("Hashable")) finalInterfaces.Add("Hashable");
      if (!finalInterfaces.Contains("Equatable")) finalInterfaces.Add("Equatable");
    }

    var finalEnumType = new MlirUnionType(enumName, cases, backingType, finalInterfaces,
      associatedTypeNames: associatedTypeNames,
      typeParams: conformanceTypeParams.Count > 0 ? conformanceTypeParams : null,
      whereConstraints: whereConstraints.Count > 0 ? whereConstraints : null) {
      SourceFilePath = _sourceFilePath,
      SourceLine = nameToken.Line,
      SourceColumn = nameToken.Column
    };
    _typeRegistry[enumName] = finalEnumType;

    // Pre-register synthetic hash() and equals() methods for enums without associated values
    if (!hasAssocValues) {
      PreRegisterSyntheticUnionMethods(module, enumName, finalEnumType);
    }

    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    _currentTypeName = null;

    if (isExported) _exportedTypes.Add(enumName);

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  private void PreScanEnum(MlirModule<MaxonOp> module, bool isExported = false) {
    Advance(); // consume 'enum'
    var nameToken = Expect(TokenType.Identifier);
    var name = nameToken.Value;
    Logger.Trace(LogCategory.Parser, $"PreScanEnum: {name} (file: {_sourceFilePath})");

    // Temporary entry so ParseTypeRef can resolve forward references
    if (!_typeRegistry.ContainsKey(name))
      _typeRegistry[name] = new MlirEnumType(name, []);

    SkipNewlines();

    var cases = new List<MlirEnumCase>();
    var caseNames = new HashSet<string>();
    MlirType? backingType = null;
    int ordinal = 0;

    while (!IsEndOfBlock() && !IsAtEnd()) {
      SkipNewlines();
      if (IsEndOfBlock()) break;

      // Implicit string-backed ("North") or char-backed ('N'): literal as case name
      if (Check(TokenType.StringLiteral) || Check(TokenType.CharacterLiteral)) {
        bool isString = Check(TokenType.StringLiteral);
        var litToken = Advance();
        var litName = litToken.Value;

        if (!caseNames.Add(litName)) {
          throw new CompileError(ErrorCode.SemanticUnionDuplicateCase,
            $"duplicate enum case: '{litName}'", litToken.Line, litToken.Column);
        }

        MlirType expectedBacking = isString ? new MlirStringBackingType() : new MlirCharBackingType();
        if (backingType == null) {
          backingType = expectedBacking;
        } else if (backingType.GetType() != expectedBacking.GetType()) {
          throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
            $"raw value type mismatch: expected {(isString ? "string" : "char")}",
            litToken.Line, litToken.Column);
        }

        foreach (var existing in cases) {
          if (existing.RawValue is string existingVal && existingVal == litName) {
            throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
              $"duplicate raw value: '{litName}'", litToken.Line, litToken.Column);
          }
        }

        cases.Add(new MlirEnumCase(litName, ordinal, litName));
        ordinal++;
        SkipNewlines();
        continue;
      }

      var caseToken = ExpectEnumCaseName();
      var caseName = caseToken.Value;

      if (!caseNames.Add(caseName)) {
        throw new CompileError(ErrorCode.SemanticUnionDuplicateCase,
          $"duplicate enum case: '{caseName}'", caseToken.Line, caseToken.Column);
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
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.F64 ? "float" : "int")}, got int'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is long existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
          // Next auto-increment starts from this value + 1
          ordinal = (int)(rawVal + 1);
        } else if (Check(TokenType.FloatLiteral)) {
          var rawVal = ParseFloatLiteral(Advance());
          if (isNegative) rawVal = -rawVal;

          if (backingType == null) {
            backingType = MlirType.F64;
          } else if (backingType != MlirType.F64) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected int, got float'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is double existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
          ordinal++;
        } else if (Check(TokenType.StringLiteral)) {
          if (isNegative) throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
            "cannot negate a string raw value", caseToken.Line, caseToken.Column);

          var rawVal = Advance().Value;

          if (backingType == null) {
            backingType = new MlirStringBackingType();
          } else if (backingType is not MlirStringBackingType) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.I64 ? "int" : backingType == MlirType.F64 ? "float" : "String")}, got String'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is string existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
          ordinal++;
        } else if (Check(TokenType.CharacterLiteral)) {
          if (isNegative) throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
            "cannot negate a character raw value", caseToken.Line, caseToken.Column);

          var rawVal = StringUtils.ResolveEscapes(Advance().Value);

          if (backingType == null) {
            backingType = new MlirCharBackingType();
          } else if (backingType is not MlirCharBackingType) {
            throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.I64 ? "int" : backingType == MlirType.F64 ? "float" : "char")}, got char'",
              caseToken.Line, caseToken.Column);
          }

          foreach (var existing in cases) {
            if (existing.RawValue is string existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticUnionDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
          ordinal++;
        } else {
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            "expected integer, float, string, or character literal for constants value",
            caseToken.Line, caseToken.Column);
        }
      } else {
        // Bare name: auto-increment (only valid for integer-backed)
        if (backingType == null) backingType = MlirType.I64;
        else if (backingType != MlirType.I64) {
          throw new CompileError(ErrorCode.SemanticUnionRawValueTypeMismatch,
            $"bare case name requires integer backing; cannot mix with {(backingType == MlirType.F64 ? "float" : backingType is MlirStringBackingType ? "String" : "char")} values",
            caseToken.Line, caseToken.Column);
        }
        cases.Add(new MlirEnumCase(caseName, ordinal, (long)ordinal));
        ordinal++;
      }

      SkipNewlines();
    }

    // Enums support Hashable and Equatable via rawValue comparison
    var finalConstantsType = new MlirEnumType(name, cases, backingType, ["Hashable", "Equatable"]);
    _typeRegistry[name] = finalConstantsType;

    PreRegisterSyntheticUnionMethods(module, name, finalConstantsType);

    if (isExported) _exportedTypes.Add(name);

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  private void ParseEnumDecl(MlirModule<MaxonOp> module) {
    Advance(); // consume 'enum'
    var nameToken = Expect(TokenType.Identifier);
    var enumName = nameToken.Value;
    _currentTypeName = enumName;

    SkipNewlines();

    // Cases already captured in pre-scan; skip all case lines
    while (!IsEndOfBlock() && !IsAtEnd()) {
      SkipNewlines();
      if (IsEndOfBlock()) break;
      SkipToEndOfLine();
      SkipNewlines();
    }

    // Synthesize hash() and equals() for enum types
    var enumType = (MlirEnumType)_typeRegistry[enumName];
    SynthesizeUnionHashAndEquals(module, enumName, enumType);

    _currentTypeName = null;
    ExpectEndLabel(enumName);
  }

  /// <summary>
  /// Pre-register synthetic hash() and equals() method signatures for enums.
  /// These are registered during pre-scan so monomorphization can find them.
  /// </summary>
  private void PreRegisterSyntheticUnionMethods(MlirModule<MaxonOp> module, string enumName, MlirUnionType enumType) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? enumName : $"{namespace_}.{enumName}";

    // hash() -> int
    var hashName = $"{qualifiedTypeName}.hash";
    if (!module.Functions.Any(f => f.Name == hashName)) {
      var hashFunc = new MlirFunction<MaxonOp>(
        hashName, ["self"], [(MlirType)enumType], MlirType.I64, null) {
        SourceFilePath = _sourceFilePath
      };
      module.AddFunction(hashFunc);
    }

    // equals(other Self) -> bool
    var equalsName = $"{qualifiedTypeName}.equals";
    if (!module.Functions.Any(f => f.Name == equalsName)) {
      var equalsFunc = new MlirFunction<MaxonOp>(
        equalsName, ["self", "other"], [(MlirType)enumType, (MlirType)enumType], MlirType.I1, null) {
        SourceFilePath = _sourceFilePath
      };
      module.AddFunction(equalsFunc);
    }
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
    Logger.Trace(LogCategory.Parser, $"PreScanInterface: {interfaceName} (file: {_sourceFilePath})");
    // Allow Self to resolve inside interface method signatures
    _currentTypeName = interfaceName;

    // Parse 'extends' clause for interface inheritance (e.g., interface Derived extends Base)
    var extendedInterfaces = new List<string>();
    if (Check(TokenType.Extends)) {
      Advance();
      do {
        extendedInterfaces.Add(Expect(TokenType.Identifier).Value);
      } while (Check(TokenType.Comma) && Advance() != null);
    }

    // Temporary entry so ParseTypeRef can resolve Self references
    if (!_typeRegistry.ContainsKey(interfaceName)) {
      _typeRegistry[interfaceName] = new MlirInterfaceType(interfaceName, [], extendedInterfaces);
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
      var methodName = ExpectIdentifierLike().Value;

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
    _typeRegistry[interfaceName] = new MlirInterfaceType(interfaceName, methods, extendedInterfaces);
    if (associatedTypeNames.Count > 0) {
      _interfaceAssociatedTypes[interfaceName] = associatedTypeNames;
    }
  }

  /// <summary>
  /// Pre-scan an extension block: register method signatures for each concrete type conforming to the interface.
  /// </summary>
  private void PreScanExtensionBlock(MlirModule<MaxonOp> module) {
    Logger.Trace(LogCategory.Parser, $"PreScanExtensionBlock (file: {_sourceFilePath})");
    ProcessExtensionBlock(module, (m, positions, typeName) => {
      foreach (var pos in positions) {
        _pos = pos;
        PreScanInstanceMethod(m, typeName);
      }
    }, (m, typeName, conformances, nameToken) => {
      ValidateInterfaceConformance(m, typeName, conformances, [], nameToken);
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
      Action<MlirModule<MaxonOp>, List<int>, string> processFunction,
      Action<MlirModule<MaxonOp>, string, List<string>, Token>? validateConformance = null) {
    Advance(); // consume 'extension'
    _parsingExtension = true;
    Token interfaceNameToken;
    if (Check(TokenType.Int) || Check(TokenType.Float) || Check(TokenType.Bool) || Check(TokenType.Byte)) {
      interfaceNameToken = Advance();
    } else {
      interfaceNameToken = Expect(TokenType.Identifier);
    }
    _parsingExtension = false;
    var interfaceName = interfaceNameToken.Value;

    // Parse optional conformance clause for extension blocks (primitives and generic struct types)
    List<string> extensionConformances = [];
    if (Check(TokenType.Implements)) {
      (extensionConformances, _) = ParseConformanceClause();
    }

    // Parse optional where clause for conditional extensions
    _interfaceAssociatedTypes.TryGetValue(interfaceName, out var extAssocTypeNames);
    // For type extensions (e.g., extension Array where ...), use the struct's own associated type names
    if (extAssocTypeNames == null && _typeRegistry.TryGetValue(interfaceName, out var extTypeVal)
        && extTypeVal is MlirStructType extStruct && extStruct.AssociatedTypeNames.Count > 0) {
      extAssocTypeNames = extStruct.AssociatedTypeNames;
    }
    var extensionWhereConstraints = ParseWhereClause(extAssocTypeNames ?? []);

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

    // Primitive type extensions: process methods directly for the named type
    if (interfaceName is "int" or "float" or "bool" or "byte") {
      _currentTypeName = interfaceName;
      _parsingExtension = true;
      processFunction(module, functionPositions, interfaceName);
      _parsingExtension = false;
      _currentTypeName = null;
      if (extensionConformances.Count > 0) {
        validateConformance?.Invoke(module, interfaceName, extensionConformances, interfaceNameToken);
        if (!_primitiveConformances.TryGetValue(interfaceName, out var existing))
          _primitiveConformances[interfaceName] = [.. extensionConformances];
        else
          existing.AddRange(extensionConformances.Where(c => !existing.Contains(c)));
      }
      _pos = endPos;
      return;
    }

    // Type extensions: extension on a generic struct (e.g., extension Array where Element is Equatable)
    if (_typeRegistry.TryGetValue(interfaceName, out var targetTypeEntry)
        && targetTypeEntry is MlirStructType targetStruct
        && targetStruct.AssociatedTypeNames.Count > 0) {
      _currentTypeName = interfaceName;

      // Bind associated type names as unresolved type parameters (generic synthesis)
      var registeredParams = new List<string>();
      foreach (var assocName in targetStruct.AssociatedTypeNames) {
        _typeRegistry[assocName] = new MlirTypeParameterType(assocName);
        registeredParams.Add(assocName);
      }

      foreach (var pos in typealiasPositions) {
        _pos = pos;
        PreScanTypeAlias();
      }

      var addedConstraints = InjectWhereConstraints(targetStruct, extensionWhereConstraints);
      var filteredFuncPositions = FilterConflictingExtensionMethods(
        functionPositions, interfaceName, extensionWhereConstraints, module);

      int funcCountBefore = module.Functions.Count;
      processFunction(module, filteredFuncPositions, interfaceName);

      TagAndCleanupConditionalExtension(
        module, funcCountBefore, extensionWhereConstraints, targetStruct, addedConstraints);

      // Record conditional conformances for later application to concrete type aliases
      if (extensionConformances.Count > 0 && extensionWhereConstraints.Count > 0) {
        _conditionalConformances.Add((interfaceName, [.. extensionConformances], new(extensionWhereConstraints)));
      }

      RemoveAssociatedTypePlaceholders(registeredParams);
      _currentTypeName = null;
      _pos = endPos;
      return;
    }

    var assocTypeNames = extAssocTypeNames;

    // Snapshot before iterating — PreScanTypeAlias modifies _typeRegistry
    var conformingTypes = new List<(string Name, MlirStructType Type)>();
    foreach (var (typeName, type) in _typeRegistry) {
      if (type is not MlirStructType structType) continue;
      if (!structType.ConformingInterfaces.Contains(interfaceName)) continue;
      if (_typeAliasSources.ContainsKey(typeName)) continue;
      // For conditional extensions, skip concrete types whose bindings don't satisfy the constraints
      // (generic types with unresolved params are allowed through for generic synthesis)
      if (extensionWhereConstraints.Count > 0
          && !TypeSatisfiesWhereConstraints(structType, extensionWhereConstraints)) {
        RecordSkippedConditionalExtensions(
          typeName, structType, functionPositions, extensionWhereConstraints);
        continue;
      }
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

      var addedConstraints = InjectWhereConstraints(structType, extensionWhereConstraints);
      var filteredFuncPositions = FilterConflictingExtensionMethods(
        functionPositions, typeName, extensionWhereConstraints, module);

      int funcCountBefore = module.Functions.Count;
      processFunction(module, filteredFuncPositions, typeName);

      TagAndCleanupConditionalExtension(
        module, funcCountBefore, extensionWhereConstraints, structType, addedConstraints);

      RemoveAssociatedTypePlaceholders(registeredParams);
    }

    _currentTypeName = null;
    _pos = endPos;
  }

  /// Temporarily inject where constraints into a struct type so method body parsing
  /// can resolve interface method calls on constrained type parameters.
  /// Returns the constraints that were added (for cleanup), or null if none were added.
  private static Dictionary<string, List<string>>? InjectWhereConstraints(
      MlirStructType structType, Dictionary<string, List<string>> whereConstraints) {
    if (whereConstraints.Count == 0) return null;
    Dictionary<string, List<string>>? added = null;
    foreach (var (paramName, interfaces) in whereConstraints) {
      if (!structType.WhereConstraints.ContainsKey(paramName)) {
        structType.WhereConstraints[paramName] = [.. interfaces];
        added ??= [];
        added[paramName] = structType.WhereConstraints[paramName];
      }
    }
    return added;
  }

  /// Remove temporarily injected where constraints from a struct type.
  private static void RemoveInjectedWhereConstraints(
      MlirStructType structType, Dictionary<string, List<string>>? addedConstraints) {
    if (addedConstraints == null) return;
    foreach (var paramName in addedConstraints.Keys)
      structType.WhereConstraints.Remove(paramName);
  }

  /// Filter out extension methods that conflict with existing methods on the target type.
  private List<int> FilterConflictingExtensionMethods(
      List<int> functionPositions, string typeName,
      Dictionary<string, List<string>> whereConstraints, MlirModule<MaxonOp> module) {
    if (whereConstraints.Count == 0) return functionPositions;
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";
    return [.. functionPositions.Where(pos => {
      if (pos + 1 >= _tokens.Count) return true;
      var methodName = _tokens[pos + 1].Value;
      var fullName = $"{qualifiedTypeName}.{methodName}";
      var peekedParamNames = PeekExtensionMethodParamNames(pos);
      var mangledName = MangleOverloadName(fullName, peekedParamNames);
      // Skip this extension method if the same specific overload already has a body
      // (prevents a different extension's overload from blocking this one)
      var hasExistingBody = module.Functions.Any(f => (f.Name == fullName || f.Name == mangledName)
          && f.Body.Blocks.Count > 0);
      if (hasExistingBody) {
        Logger.Debug(LogCategory.Parser, $"Filtering extension method {mangledName}: already has a body on {typeName}");
      }
      return !hasExistingBody;
    })];
  }

  /// Peek at parameter names from a function token position without advancing the parser.
  /// Saves and restores parser position. Returns names including "self" for instance methods.
  private List<string> PeekExtensionMethodParamNames(int pos) {
    int savedPos = _pos;
    _pos = pos;
    Advance(); // consume 'function'
    Advance(); // consume method name
    Expect(TokenType.LeftParen);
    var names = new List<string> { "self" };
    if (!Check(TokenType.RightParen)) {
      do {
        var paramName = ExpectIdentifierLike();
        names.Add(paramName.Value);
        // Skip the type reference (consume until comma or closing paren at depth 0)
        int depth = 0;
        while (!IsAtEnd()) {
          if (Check(TokenType.LeftParen)) depth++;
          else if (Check(TokenType.RightParen)) {
            if (depth == 0) break;
            depth--;
          } else if (Check(TokenType.Comma) && depth == 0) break;
          else if (Check(TokenType.Equals) && depth == 0) {
            // Skip default value
            Advance();
            while (!IsAtEnd() && !Check(TokenType.Comma) && !Check(TokenType.RightParen)) Advance();
            break;
          }
          Advance();
        }
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    _pos = savedPos;
    return names;
  }

  /// Tag newly-added functions with where constraints and clean up injected constraints.
  private static void TagAndCleanupConditionalExtension(
      MlirModule<MaxonOp> module, int funcCountBefore,
      Dictionary<string, List<string>> whereConstraints,
      MlirStructType structType, Dictionary<string, List<string>>? addedConstraints) {
    if (whereConstraints.Count == 0) return;
    for (int i = funcCountBefore; i < module.Functions.Count; i++) {
      module.Functions[i].ExtensionWhereConstraints = whereConstraints;
    }
    RemoveInjectedWhereConstraints(structType, addedConstraints);
  }

  /// <summary>
  /// Pre-scan a typealias declaration: typealias Name = SourceType with (Type1, Type2, ...)
  /// Creates a concrete struct type by substituting associated type parameters.
  /// Also supports "with N Type" form where N is an integer capacity hint (e.g., Vector with 3 int).
  /// </summary>

  private void DetectCircularTypeAliases() {
    foreach (var aliasName in _localTypeAliases) {
      var visited = new HashSet<string>();
      if (HasCircularTypeAlias(aliasName, visited)) {
        var (line, col) = _typeAliasLocations.TryGetValue(aliasName, out var loc) ? loc : (0, 0);
        throw new CompileError(ErrorCode.ParserCircularDependency,
          $"Circular typealias dependency: {string.Join(" -> ", visited)} -> {aliasName}",
          line, col);
      }
    }
  }

  private bool HasCircularTypeAlias(string name, HashSet<string> visited) {
    if (!visited.Add(name)) return true;
    if (!_typeRegistry.TryGetValue(name, out var type)) { visited.Remove(name); return false; }
    if (type is not MlirStructType st) { visited.Remove(name); return false; }
    foreach (var (_, paramType) in st.TypeParams) {
      if (IsTypeAlias(paramType.Name) && HasCircularTypeAlias(paramType.Name, visited))
        return true;
    }
    visited.Remove(name);
    return false;
  }

  private bool IsTypeAlias(string name) =>
    _localTypeAliases.Contains(name) || _seededTypeAliases.Contains(name);

  private void PreScanTypeAlias(bool isExported = false) {
    Advance(); // consume 'typealias'
    var aliasNameToken = Expect(TokenType.Identifier);
    var aliasName = aliasNameToken.Value;
    Logger.Trace(LogCategory.Parser, $"PreScanTypeAlias: {aliasName} (file: {_sourceFilePath})");

    // Duplicate detection only for top-level typealiases (not type-scoped ones)
    if (_currentTypeName == null && !_localTypeAliases.Add(aliasName))
      throw new CompileError(ErrorCode.SemanticDuplicateTypeAlias,
        $"Duplicate typealias '{aliasName}'", aliasNameToken.Line, aliasNameToken.Column);

    if (_currentTypeName == null)
      _typeAliasLocations[aliasName] = (aliasNameToken.Line, aliasNameToken.Column);

    if (isExported) _exportedTypeAliases.Add(aliasName);

    _resolvingTypeAliases.Add(aliasName);

    Expect(TokenType.Equals);
    _parsingTypeAliasRhs = true;
    try {

      // Tuple type alias: typealias Entry = (Key, Value)
      if (Check(TokenType.LeftParen)) {
        var tupleType = (MlirStructType)ParseTypeRef();
        // Build typeParams from any type parameter fields (e.g., Key, Value)
        var typeParams = new Dictionary<string, MlirType>();
        foreach (var field in tupleType.Fields) {
          if (field.Type is MlirTypeParameterType tp)
            typeParams[tp.ParameterName] = tp;
        }
        // Create an alias type with the alias name but tuple fields and behavior
        var aliasType = new MlirStructType(aliasName, [.. tupleType.Fields], isTuple: true,
          typeParams: typeParams.Count > 0 ? typeParams : null);
        _typeRegistry[aliasName] = aliasType;
        _typeAliasSources[aliasName] = tupleType.Name;
        return;
      }

      // Reject bare sized type names — require explicit range syntax
      if (Check(TokenType.Identifier) && IsSizedTypeName(Current().Value)) {
        var shortToken = Current();
        throw new CompileError(ErrorCode.ParserExpectedType,
          $"Bare sized type '{shortToken.Value}' is not allowed. Use explicit range syntax, e.g. 'int({shortToken.Value}.min to {shortToken.Value}.max)'",
          shortToken.Line, shortToken.Column);
      }

      // Ranged primitive alias: typealias Age = int(0 to 150)
      if (Check(TokenType.Int) || Check(TokenType.Float) || Check(TokenType.Byte)) {
        var primitiveToken = Advance();
        var baseType = primitiveToken.Type switch {
          TokenType.Int => MlirType.I64,
          TokenType.Float => MlirType.F64,
          TokenType.Byte => MlirType.I8,
          _ => throw new InvalidOperationException()
        };
        Expect(TokenType.LeftParen);
        var (lower, lowerQualifier) = ParseRangeBound();
        bool upperInclusive;
        if (Check(TokenType.To)) { Advance(); upperInclusive = true; } else if (Check(TokenType.Upto)) { Advance(); upperInclusive = false; } else throw new CompileError(ErrorCode.ParserExpectedToken, "Expected 'to' or 'upto' in range", Current().Line, Current().Column);
        var (upper, upperQualifier) = ParseRangeBound();
        Expect(TokenType.RightParen);
        if (lower > upper || (lower == upper && !upperInclusive))
          throw new CompileError(ErrorCode.SemanticTypeMismatch, $"Invalid range: lower bound {lower} must be less than upper bound {upper}", primitiveToken.Line, primitiveToken.Column);
        // Ranges that span negative and above i64.max are unrepresentable in any single 64-bit type
        if (baseType != MlirType.F64 && baseType != MlirType.F32 && lower < 0 && upper > (double)long.MaxValue)
          throw new CompileError(ErrorCode.SemanticTypeMismatch, $"Invalid range: range {lower} to {upper} exceeds any representable integer type", primitiveToken.Line, primitiveToken.Column);
        // byte ranges must fit within 0..255
        if (baseType == MlirType.I8 && (lower < 0 || upper > 255))
          throw new CompileError(ErrorCode.SemanticTypeMismatch, $"Invalid byte range: bounds must be within 0 to u8.max", primitiveToken.Line, primitiveToken.Column);
        // When both bounds use type qualifiers, they must reference the same type (e.g. i64.min to i64.max, not i32.min to u64.max)
        if (lowerQualifier != null && upperQualifier != null && lowerQualifier != upperQualifier)
          throw new CompileError(ErrorCode.SemanticTypeMismatch, $"Mismatched type bounds: '{lowerQualifier}.min' and '{upperQualifier}.max' must reference the same type", primitiveToken.Line, primitiveToken.Column);
        var rangedType = new MlirRangedPrimitiveType(aliasName, baseType, lower, upper, upperInclusive);
        _typeRegistry[aliasName] = rangedType;
        _typeAliasSources[aliasName] = primitiveToken.Value;
        return;
      }

      var sourceNameToken = Expect(TokenType.Identifier);
      var sourceName = sourceNameToken.Value;

      if (!_typeRegistry.TryGetValue(sourceName, out var sourceType))
        throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown type: {sourceName}", sourceNameToken.Line, sourceNameToken.Column);

      // Interface alias: typealias ElementIterable = Iterable with Element
      if (sourceType is MlirInterfaceType) {
        var assocTypeNames = _interfaceAssociatedTypes.TryGetValue(sourceName, out var names) ? names : [];
        if (assocTypeNames.Count == 0)
          throw new CompileError(ErrorCode.ParserExpectedType, $"Interface '{sourceName}' has no associated types", sourceNameToken.Line, sourceNameToken.Column);

        Expect(TokenType.With);
        var ifaceConcreteTypes = new List<MlirType>();
        if (Check(TokenType.LeftParen)) {
          Advance();
          ifaceConcreteTypes.Add(ParseTypeRef());
          while (Check(TokenType.Comma)) { Advance(); ifaceConcreteTypes.Add(ParseTypeRef()); }
          Expect(TokenType.RightParen);
        } else {
          ifaceConcreteTypes.Add(ParseTypeRef());
        }

        RejectBarePrimitiveTypeArgs(ifaceConcreteTypes, aliasNameToken);

        if (ifaceConcreteTypes.Count != assocTypeNames.Count)
          throw new CompileError(ErrorCode.ParserExpectedType,
            $"Interface '{sourceName}' expects {assocTypeNames.Count} type argument(s), got {ifaceConcreteTypes.Count}",
            aliasNameToken.Line, aliasNameToken.Column);

        var ifaceSubstitution = new Dictionary<string, MlirType>();
        for (int i = 0; i < assocTypeNames.Count; i++)
          ifaceSubstitution[assocTypeNames[i]] = ifaceConcreteTypes[i];

        _typeRegistry[aliasName] = new MlirStructType(aliasName, [],
          conformingInterfaces: [sourceName],
          typeParams: ifaceSubstitution,
          isInterfaceAlias: true);
        _typeAliasSources[aliasName] = sourceName;
        return;
      }

      // Generic union alias: typealias IntNode = ListNode with Integer
      if (sourceType is MlirUnionType sourceUnion && sourceUnion.AssociatedTypeNames.Count > 0) {
        Expect(TokenType.With);

        var concreteTypes = ParseWithTypeArgs(sourceUnion.AssociatedTypeNames.Count);
        RejectBarePrimitiveTypeArgs(concreteTypes, aliasNameToken);

        if (concreteTypes.Count != sourceUnion.AssociatedTypeNames.Count)
          throw new CompileError(ErrorCode.ParserExpectedType,
            $"Type '{sourceName}' expects {sourceUnion.AssociatedTypeNames.Count} type argument(s), got {concreteTypes.Count}",
            aliasNameToken.Line, aliasNameToken.Column);

        var substitution = new Dictionary<string, MlirType>();
        for (int i = 0; i < sourceUnion.AssociatedTypeNames.Count; i++) {
          substitution[sourceUnion.AssociatedTypeNames[i]] = concreteTypes[i];
        }

        ValidateUnionWhereConstraints(sourceUnion, substitution, sourceName, aliasNameToken);
        RegisterConcreteUnionAlias(aliasName, sourceName, sourceUnion, substitution);
        return;
      }

      if (sourceType is not MlirStructType sourceStruct)
        throw new CompileError(ErrorCode.ParserExpectedType, $"Type '{sourceName}' is not a struct or union type", sourceNameToken.Line, sourceNameToken.Column);

      if (sourceStruct.AssociatedTypeNames.Count == 0)
        throw new CompileError(ErrorCode.ParserExpectedType, $"Type '{sourceName}' has no associated types", sourceNameToken.Line, sourceNameToken.Column);

      Expect(TokenType.With);

      var concreteTypes2 = new List<MlirType>();
      var constParams = new Dictionary<string, long>();

      if (Check(TokenType.LeftParen)) {
        // When expecting a single type arg, let ParseTypeRef handle it —
        // (A, B) is a tuple type, not two separate type arguments
        if (sourceStruct.AssociatedTypeNames.Count == 1) {
          concreteTypes2.Add(ParseTypeRef());
        } else {
          Advance(); // consume '('
          concreteTypes2.Add(ParseTypeRef());
          while (Check(TokenType.Comma)) {
            Advance();
            concreteTypes2.Add(ParseTypeRef());
          }
          Expect(TokenType.RightParen);
        }
      } else {
        // Check for "with N Type" form: integer followed by type
        if (Check(TokenType.IntegerLiteral)) {
          var intToken = Advance();
          constParams["__capacity"] = ParseIntegerLiteral(intToken);
        }
        concreteTypes2.Add(ParseTypeRef());
        // Parse remaining comma-separated type args for multi-parameter generics
        while (concreteTypes2.Count < sourceStruct.AssociatedTypeNames.Count && Check(TokenType.Comma)) {
          Advance();
          concreteTypes2.Add(ParseTypeRef());
        }
      }

      RejectBarePrimitiveTypeArgs(concreteTypes2, aliasNameToken);

      if (concreteTypes2.Count != sourceStruct.AssociatedTypeNames.Count)
        throw new CompileError(ErrorCode.ParserExpectedType,
          $"Type '{sourceName}' expects {sourceStruct.AssociatedTypeNames.Count} type argument(s), got {concreteTypes2.Count}",
          aliasNameToken.Line, aliasNameToken.Column);

      // Build substitution map: associated type name -> concrete type
      var substitution2 = new Dictionary<string, MlirType>();
      for (int i = 0; i < sourceStruct.AssociatedTypeNames.Count; i++) {
        substitution2[sourceStruct.AssociatedTypeNames[i]] = concreteTypes2[i];
      }

      ValidateWhereConstraints(sourceStruct, substitution2, sourceName, aliasNameToken);

      RegisterConcreteTypeAlias(aliasName, sourceName, sourceStruct, substitution2,
        constParams.Count > 0 ? constParams : null);

    } finally { _parsingTypeAliasRhs = false; _resolvingTypeAliases.Remove(aliasName); }
  }

  private void RegisterConcreteTypeAlias(
      string aliasName,
      string sourceName,
      MlirStructType sourceStruct,
      Dictionary<string, MlirType> substitution,
      Dictionary<string, long>? constParams = null) {
    // Resolve local typealiases: if a field's type is a typealias whose source type
    // has type params referencing our substitution keys, create a concrete alias.
    // E.g., Array's "ElementMemory = __ManagedMemory with Element" becomes
    // a concrete __ManagedMemory alias when Element is resolved to a concrete type.
    var expandedSub = new Dictionary<string, MlirType>(substitution);
    foreach (var field in sourceStruct.Fields) {
      if (expandedSub.ContainsKey(field.Type.Name)) continue;
      if (!_typeAliasSources.TryGetValue(field.Type.Name, out var fieldAliasSource)) continue;
      if (!_typeRegistry.TryGetValue(field.Type.Name, out var fieldAliasType)) continue;
      if (fieldAliasType is not MlirStructType fieldAliasStruct) continue;
      if (fieldAliasStruct.TypeParams.Count == 0) continue;

      // Build concrete substitution for this local alias by resolving its type params
      // through the parent's substitution (e.g., Element -> Pair)
      var localSub = new Dictionary<string, MlirType>();
      bool allResolved = true;
      foreach (var (paramName, paramType) in fieldAliasStruct.TypeParams) {
        if (paramType is MlirTypeParameterType tp
            && substitution.TryGetValue(tp.ParameterName, out var concrete)
            && concrete is not MlirTypeParameterType)
          localSub[paramName] = concrete;
        else if (paramType is not MlirTypeParameterType)
          localSub[paramName] = paramType; // already concrete
        else {
          allResolved = false;
          break;
        }
      }

      if (!allResolved || localSub.Count == 0) continue;

      // Look up the source struct for the local alias
      if (!_typeRegistry.TryGetValue(fieldAliasSource, out var fieldSourceType)) continue;
      if (fieldSourceType is not MlirStructType fieldSourceStruct) continue;

      // Create concrete alias, e.g., __ManagedMemory_Pair
      var concreteAliasName = $"{fieldAliasSource}_{string.Join("_", localSub.Values.Select(t => t.Name))}";
      if (!_typeRegistry.ContainsKey(concreteAliasName))
        RegisterConcreteTypeAlias(concreteAliasName, fieldAliasSource, fieldSourceStruct, localSub);
      else
        _typeAliasSources.TryAdd(concreteAliasName, fieldAliasSource);
      expandedSub[field.Type.Name] = _typeRegistry[concreteAliasName];
    }

    var concreteFields = new List<MlirStructField>();
    foreach (var field in sourceStruct.Fields) {
      var fieldType = expandedSub.TryGetValue(field.Type.Name, out var concreteType)
        ? concreteType
        : field.Type;
      concreteFields.Add(new MlirStructField(field.Name, fieldType, field.IsExported, field.IsMutable, field.DefaultValue));
    }
    _typeRegistry[aliasName] = new MlirStructType(aliasName, concreteFields,
      conformingInterfaces: [.. sourceStruct.ConformingInterfaces],
      constParams: constParams,
      typeParams: substitution.Count > 0 ? substitution : null);

    // Apply conditional conformances from extension blocks (e.g., Array implements Hashable where Element is Hashable)
    var newStruct = (MlirStructType)_typeRegistry[aliasName];
    foreach (var (sourceType, interfaces, whereConstraints) in _conditionalConformances) {
      if (sourceType != sourceName) continue;
      if (TypeSatisfiesWhereConstraints(newStruct, whereConstraints)) {
        foreach (var iface in interfaces)
          if (!newStruct.ConformingInterfaces.Contains(iface))
            newStruct.ConformingInterfaces.Add(iface);
      }
    }

    Logger.Debug(LogCategory.Parser, $"Registering type alias: {aliasName} -> {sourceName}");
    _typeAliasSources[aliasName] = sourceName;
  }

  private void RegisterConcreteUnionAlias(
      string aliasName,
      string sourceName,
      MlirUnionType sourceUnion,
      Dictionary<string, MlirType> substitution) {
    // Create concrete alias type first so self-referential cases resolve correctly
    var concreteAliasType = new MlirUnionType(aliasName, [],
      sourceUnion.BackingType,
      [.. sourceUnion.ConformingInterfaces],
      typeParams: substitution.Count > 0 ? substitution : null);
    _typeRegistry[aliasName] = concreteAliasType;

    // Build full substitution including self-reference: ListNode → IntNode
    var fullSubstitution = new Dictionary<string, MlirType>(substitution) {
      [sourceName] = concreteAliasType,
      ["Self"] = concreteAliasType
    };

    var concreteCases = new List<MlirEnumCase>();
    foreach (var c in sourceUnion.Cases) {
      if (c.AssociatedValues is { Count: > 0 }) {
        var concreteValues = new List<(string Name, MlirType Type)>();
        foreach (var (name, type) in c.AssociatedValues) {
          var newType = fullSubstitution.TryGetValue(type.Name, out var concreteType)
            ? concreteType
            : type;
          concreteValues.Add((name, newType));
        }
        concreteCases.Add(new MlirEnumCase(c.Name, c.Ordinal, associatedValues: concreteValues));
      } else {
        concreteCases.Add(c);
      }
    }

    // Update the already-registered type with concrete cases
    _typeRegistry[aliasName] = new MlirUnionType(aliasName, concreteCases,
      sourceUnion.BackingType,
      [.. sourceUnion.ConformingInterfaces],
      typeParams: substitution.Count > 0 ? substitution : null);

    _typeAliasSources[aliasName] = sourceName;
  }

  private void ValidateUnionWhereConstraints(
      MlirUnionType sourceUnion, Dictionary<string, MlirType> substitution,
      string sourceName, Token errorToken) {
    if (_skipWhereValidation) return;
    foreach (var (paramName, requiredInterfaces) in sourceUnion.WhereConstraints) {
      if (!substitution.TryGetValue(paramName, out var concreteType)) continue;
      if (concreteType is MlirTypeParameterType) continue;

      var concreteTypeName = MlirType.FormatAsSourceName(concreteType);
      foreach (var requiredInterface in requiredInterfaces) {
        if (!TypeConformsToInterface(concreteTypeName, requiredInterface))
          throw new CompileError(ErrorCode.SemanticWhereConstraintViolation,
            $"Type '{concreteTypeName}' does not satisfy constraint '{requiredInterface}' required by type parameter '{paramName}' of '{sourceName}'",
            errorToken.Line, errorToken.Column);
      }
    }
  }

  /// Parses a range bound in a ranged typealias: a numeric literal, negative literal, or type.min/type.max.
  /// Returns the value and the type qualifier name (e.g. "i64") if a type-qualified bound was used, null otherwise.
  private (double value, string? qualifier) ParseRangeBound() {
    // type.min / type.max (e.g., u32.max, i8.min)
    if (Check(TokenType.Identifier) && _pos + 2 < _tokens.Count
        && _tokens[_pos + 1].Type == TokenType.Dot
        && (_tokens[_pos + 2].Value == "min" || _tokens[_pos + 2].Value == "max")
        && IsSizedTypeName(Current().Value)) {
      var typeName = Advance().Value;
      Advance(); // consume dot
      var keyword = Advance().Value;
      return (ResolveTypeBound(typeName, keyword), typeName);
    }
    // Negative literal
    bool negative = false;
    if (Check(TokenType.Minus)) {
      Advance();
      negative = true;
    }
    if (Check(TokenType.IntegerLiteral)) {
      var val = (double)ParseIntegerLiteral(Advance());
      return (negative ? -val : val, null);
    }
    if (Check(TokenType.FloatLiteral)) {
      var val = ParseFloatLiteral(Advance());
      return (negative ? -val : val, null);
    }
    throw new CompileError(ErrorCode.ParserExpectedToken, "Expected numeric literal, 'min', 'max', or 'type.min'/'type.max' in range bound", Current().Line, Current().Column);
  }

  private static bool IsSizedTypeName(string name) => name is
    "u8" or "u16" or "u32" or "u64" or
    "i8" or "i16" or "i32" or "i64" or
    "f32" or "f64";

  private static double ResolveTypeBound(string typeName, string keyword) => (typeName, keyword) switch {
    ("i64", "min") => (double)long.MinValue,
    ("i64", "max") => (double)long.MaxValue,
    ("f64", "min") => double.MinValue,
    ("f64", "max") => double.MaxValue,
    ("f32", "min") => (double)-float.MaxValue,
    ("f32", "max") => (double)float.MaxValue,
    ("u8", "max") => 255,
    ("u16", "max") => 65535,
    ("u32", "max") => 4294967295,
    ("u64", "max") => (double)ulong.MaxValue,
    ("i8", "min") => -128,
    ("i8", "max") => 127,
    ("i16", "min") => -32768,
    ("i16", "max") => 32767,
    ("i32", "min") => -2147483648,
    ("i32", "max") => 2147483647,
    _ => throw new InvalidOperationException($"Unknown type bound: {typeName}.{keyword}")
  };

  private void PreScanInstanceMethod(MlirModule<MaxonOp> module, string typeName, bool isExported = false) {
    Advance(); // consume 'function'
    var nameToken = ExpectIdentifierLike();

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
    MlirType selfType = TryGetPrimitiveMlirType(typeName) ?? (MlirType)_typeRegistry[typeName];

    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { selfType };
    allParamTypes.AddRange(paramTypes);

    var registrationName = ResolveOverloadRegistrationName(module, methodName, allParamNames, allParamTypes);

    // Register if not already present (by mangled name)
    if (!module.Functions.Any(f => f.Name == registrationName)) {
      var func = new MlirFunction<MaxonOp>(registrationName, allParamNames, allParamTypes, returnType, throwsType) {
        IsExported = isExported,
        SourceFilePath = _sourceFilePath
      };
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
    int bracketDepth = 0;
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      if (Check(TokenType.LeftBracket)) bracketDepth++;
      else if (Check(TokenType.RightBracket)) bracketDepth--;
      else if (Check(TokenType.Newline) && bracketDepth <= 0) break;
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
    bool prevWasDot = false;
    while (!IsAtEnd() && depth > 0) {
      if (prevWasDot) {
        // Keywords after '.' are member names, not block openers/closers
      } else if (Check(TokenType.Function) || Check(TokenType.If) || Check(TokenType.While) || Check(TokenType.For) || Check(TokenType.Match)) {
        // Keywords used as match case labels (e.g., `function then ...`, `if to newline then ...`)
        // are not block openers. Detect by checking if the next token indicates a case label context:
        // `then`, `gives` (value arms), `to`/`upto` (range patterns).
        var next = _pos + 1 < _tokens.Count ? _tokens[_pos + 1].Type : TokenType.Eof;
        if (next is not TokenType.Then and not TokenType.Gives and not TokenType.To and not TokenType.Upto)
          depth++;
      } else if (Check(TokenType.Else)) {
        var next = _pos + 1 < _tokens.Count ? _tokens[_pos + 1].Type : TokenType.Eof;
        if (next is TokenType.Then or TokenType.Gives or TokenType.To or TokenType.Upto) {
          // Match case label context — not a block opener
        } else if (next == TokenType.If) {
          // else if — don't increment (let 'if' handle it)
        } else {
          // Standalone else 'label' opens a block needing its own 'end'
          depth++;
        }
      } else if (Check(TokenType.Otherwise)) {
        var next = _pos + 1 < _tokens.Count ? _tokens[_pos + 1].Type : TokenType.Eof;
        if (next is TokenType.Then or TokenType.Gives or TokenType.To or TokenType.Upto) {
          // Match case label context — not a block opener
        } else if (next == TokenType.CharacterLiteral) {
          // otherwise 'label' introduces a block ending with end (but not inline otherwise <value>)
          depth++;
        }
      } else if (Check(TokenType.End)) {
        depth--;
      }
      prevWasDot = Check(TokenType.Dot);
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
    return EvalConstOr(decls, evaluated, evaluating);
  }

  private object EvalConstOr(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstXor(decls, evaluated, evaluating);
    while (Check(TokenType.Or)) {
      Advance();
      var rhs = EvalConstXor(decls, evaluated, evaluating);
      if (lhs is bool lb && rhs is bool rb) lhs = lb || rb;
      else if (lhs is long ll && rhs is long rl) lhs = ll | rl;
      else throw new InvalidOperationException($"Cannot apply 'or' to {lhs?.GetType().Name} and {rhs?.GetType().Name}");
    }
    return lhs;
  }

  private object EvalConstXor(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstAnd(decls, evaluated, evaluating);
    while (Check(TokenType.Xor)) {
      Advance();
      var rhs = EvalConstAnd(decls, evaluated, evaluating);
      if (lhs is bool lb && rhs is bool rb) lhs = lb ^ rb;
      else if (lhs is long ll && rhs is long rl) lhs = ll ^ rl;
      else throw new InvalidOperationException($"Cannot apply 'xor' to {lhs?.GetType().Name} and {rhs?.GetType().Name}");
    }
    return lhs;
  }

  private object EvalConstAnd(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstComparison(decls, evaluated, evaluating);
    while (Check(TokenType.And)) {
      Advance();
      var rhs = EvalConstComparison(decls, evaluated, evaluating);
      if (lhs is bool lb && rhs is bool rb) lhs = lb && rb;
      else if (lhs is long ll && rhs is long rl) lhs = ll & rl;
      else throw new InvalidOperationException($"Cannot apply 'and' to {lhs?.GetType().Name} and {rhs?.GetType().Name}");
    }
    return lhs;
  }

  private object EvalConstComparison(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstShift(decls, evaluated, evaluating);

    if (Check(TokenType.EqualsEquals) || Check(TokenType.NotEquals) ||
        Check(TokenType.LessThan) || Check(TokenType.GreaterThan) ||
        Check(TokenType.LessEquals) || Check(TokenType.GreaterEquals)) {
      var opType = Advance().Type;
      var rhs = EvalConstShift(decls, evaluated, evaluating);
      return EvalConstComparisonOp(lhs, rhs, opType);
    }

    return lhs;
  }

  private object EvalConstShift(List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    var lhs = EvalConstAddSub(decls, evaluated, evaluating);
    while (Check(TokenType.Shl) || Check(TokenType.Shr)) {
      var op = Advance().Type;
      var rhs = EvalConstAddSub(decls, evaluated, evaluating);
      if (lhs is long ll && rhs is long rl) {
        lhs = op == TokenType.Shl ? ll << (int)rl : ll >> (int)rl;
      } else {
        throw new InvalidOperationException($"Cannot apply shift to {lhs?.GetType().Name} and {rhs?.GetType().Name}");
      }
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
      if (val is long l) return ~l;
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
    if (Check(TokenType.StringLiteral)) {
      var token = Advance();
      return StringUtils.ResolveEscapes(token.Value);
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
      // Handle ranged type construction: TypeName{expr}
      if (Check(TokenType.LeftBrace) && _typeRegistry.TryGetValue(token.Value, out var rangedCheck) && rangedCheck is MlirRangedPrimitiveType rangedConst) {
        _usedTypeAliases.Add(token.Value);
        Advance(); // consume '{'
        var innerVal = EvalConstExpr(decls, evaluated, evaluating);
        Expect(TokenType.RightBrace);
        // Validate range at compile time
        double numericVal = innerVal is long lv ? (double)lv : innerVal is double dv ? dv : throw new CompileError(
          ErrorCode.SemanticTypeMismatch, $"Cannot construct '{rangedConst.Name}' from non-numeric value", token.Line, token.Column);
        var upperLimit = rangedConst.UpperInclusive ? rangedConst.UpperBound : rangedConst.UpperBound - 1;
        if (numericVal < rangedConst.LowerBound || numericVal > upperLimit) {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"Value {innerVal} is outside the range of '{rangedConst.Name}' ({rangedConst.FormatRange()})", token.Line, token.Column);
        }
        return innerVal;
      }
      // Handle enum constant: EnumType.caseName
      if (Check(TokenType.Dot) && _typeRegistry.TryGetValue(token.Value, out var constType) && constType is MlirUnionType constEnumType) {
        Advance(); // consume '.'
        var caseToken = Expect(TokenType.Identifier);
        var enumCase = constEnumType.GetCase(caseToken.Value)
          ?? throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
            $"unknown union case: '{caseToken.Value}'", caseToken.Line, caseToken.Column);
        return new EnumConstantValue(token.Value, caseToken.Value, enumCase.Ordinal);
      }
      if (Check(TokenType.LeftParen)) {
        throw new CompileError(ErrorCode.ParserNonConstantInitializer,
          $"Function calls are not allowed in global variable initializers; '{token.Value}()' is not a constant expression",
          token.Line, token.Column);
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
    if (Check(TokenType.HashIf)) {
      HandleConditionalCompilation();
      return;
    }
    if (Check(TokenType.HashElse)) {
      HandleConditionalElse();
      return;
    }
    if (Check(TokenType.HashEndif)) {
      HandleConditionalEndif();
      return;
    }

    if (Check(TokenType.Export)) {
      Advance();
    }

    if (Check(TokenType.Function)) {
      ParseFunction(module);
    } else if (Check(TokenType.Type)) {
      ParseTypeDecl(module);
    } else if (Check(TokenType.Union)) {
      ParseUnionDecl(module);
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
    ParseWhereClause(associatedTypeNames);
    SkipNewlines();

    // Type is already fully constructed in _typeRegistry from pre-scan
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      // Conditional compilation inside type body
      if (Check(TokenType.HashIf)) {
        HandleConditionalCompilation();
        continue;
      }
      if (Check(TokenType.HashElse)) {
        HandleConditionalElse();
        continue;
      }
      if (Check(TokenType.HashEndif)) {
        HandleConditionalEndif();
        continue;
      }

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

    // Synthesize clone() body for auto-Cloneable structs
    if (_typeRegistry.TryGetValue(typeName, out var regType) && regType is MlirStructType st
        && st.ConformingInterfaces.Contains("Cloneable")) {
      SynthesizeStructClone(module, typeName, st);
    }

    // Synthesize equals() body for auto-Equatable structs
    if (_typeRegistry.TryGetValue(typeName, out var regTypeEq) && regTypeEq is MlirStructType stEq
        && stEq.ConformingInterfaces.Contains("Equatable")) {
      SynthesizeStructEquals(module, typeName, stEq);
    }

    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    ExpectEndLabel(typeName);
  }

  private void ParseUnionDecl(MlirModule<MaxonOp> module) {
    Advance(); // consume 'union'
    var nameToken = Expect(TokenType.Identifier);
    var enumName = nameToken.Value;
    _currentTypeName = enumName;
    Logger.Debug(LogCategory.Parser, $"Parsing enum: {enumName}");

    // Skip uses/conformance/where clauses (already captured during pre-scan)
    var associatedTypeNames = ParseUsesClause();
    ParseConformanceClause();
    ParseWhereClause(associatedTypeNames);

    SkipNewlines();

    var enumType = (MlirUnionType)_typeRegistry[enumName];

    // Skip cases (already pre-scanned)
    while (!IsEndOfBlock() && !IsEnumMethodStart() && !IsAtEnd()) {
      SkipNewlines();
      if (IsEndOfBlock() || IsEnumMethodStart()) break;
      SkipToEndOfLine();
      SkipNewlines();
    }

    // Parse instance methods
    while (IsEnumMethodStart() && !IsAtEnd()) {
      ParseUnionInstanceMethod(module, enumName, enumType);
      SkipNewlines();
    }

    // Synthesize hash() and equals() for enums without associated values
    if (!enumType.HasAssociatedValues) {
      SynthesizeUnionHashAndEquals(module, enumName, enumType);
    }

    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    ExpectEndLabel(enumName);
  }

  private void ParseUnionInstanceMethod(MlirModule<MaxonOp> module, string enumName, MlirUnionType enumType) {
    Expect(TokenType.Function);
    var nameToken = ExpectIdentifierLike();
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
    var backingKind = GetUnionBackingKind(enumType);
    var selfParamOp = new MaxonEnumParamOp(0, "self", enumName, backingKind);
    _currentBlock!.AddOp(selfParamOp);
    _variables.Declare("self", MaxonValueKind.Enum, false, selfParamOp.Result, _currentBlock!, OwnershipFlags.IsParam, structTypeName: enumName);

    // Emit remaining params (offset by 1 for 'self')
    EmitParameters(paramNames, paramTypes, paramTokens, paramOffset: 1);

    ParseBodyUntilEnd();
    ExpectEndLabel(nameToken.Value);
    FinishFunctionBody(nameToken.Value, nameToken, returnType);
  }

  /// <summary>
  /// Synthesize clone() method body for an auto-Cloneable struct type.
  /// Creates a new struct literal with each field cloned from self.
  /// </summary>
  private void SynthesizeStructClone(MlirModule<MaxonOp> module, string typeName, MlirStructType structType) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";

    var cloneName = $"{qualifiedTypeName}.clone";
    var cloneFunc = module.Functions.FirstOrDefault(f => f.Name == cloneName);
    if (cloneFunc == null) return;

    // Only synthesize if the stub has no body (user didn't provide their own)
    if (cloneFunc.Body.Blocks.Count > 0) return;

    module.Functions.Remove(cloneFunc);
    cloneFunc = new MlirFunction<MaxonOp>(
      cloneName, ["self"], [(MlirType)structType], (MlirType)structType, null) {
      SourceFilePath = _sourceFilePath
    };
    module.AddFunction(cloneFunc);
    var block = cloneFunc.Body.AddBlock("entry");

    // self param
    var selfParam = new MaxonStructParamOp(0, "self", typeName);
    block.AddOp(selfParam);

    // Access each field and clone if needed
    var fieldValues = new List<(string FieldName, MaxonValue Value)>();
    var cloneTempNames = new List<string>();
    foreach (var field in structType.Fields) {
      var fieldKind = field.Type.ToValueKind();
      string? fieldStructTypeName = null;
      if (field.Type is MlirStructType fst) fieldStructTypeName = fst.Name;
      else if (field.Type is MlirUnionType fut) fieldStructTypeName = fut.Name;

      var accessOp = new MaxonFieldAccessOp(selfParam.Result, typeName, field.Name, fieldKind, fieldStructTypeName);
      block.AddOp(accessOp);

      MaxonValue fieldValue = accessOp.Result;
      if (field.Type is MlirStructType nestedStruct) {
        // Recursively clone nested struct fields using qualified name
        var nestedQualified = string.IsNullOrEmpty(namespace_) ? nestedStruct.Name : $"{namespace_}.{nestedStruct.Name}";
        var nestedCloneName = $"{nestedQualified}.clone";
        var nestedCloneCall = new MaxonCallOp(nestedCloneName, [accessOp.Result], MaxonValueKind.Struct, nestedStruct.Name);
        block.AddOp(nestedCloneCall);
        fieldValue = nestedCloneCall.Result!;
        // Track clone result so it can be cleaned up at scope end
        var cloneTempName = $"__call_tmp_{nestedCloneCall.Result!.Id}";
        block.AddOp(new MaxonAssignOp(cloneTempName, fieldValue, true, false, MaxonValueKind.Struct));
        cloneTempNames.Add(cloneTempName);
      }
      // Primitives have value semantics — assignment is already a copy
      fieldValues.Add((field.Name, fieldValue));
    }

    var structLit = new MaxonStructLiteralOp(typeName, fieldValues);
    block.AddOp(structLit);

    var retvalName = $"__retval_{MlirContext.Current.NextId()}";
    block.AddOp(new MaxonAssignOp(retvalName, structLit.Result, true, false, MaxonValueKind.Struct));
    var scopeVars = new List<string>(cloneTempNames) { retvalName };
    block.AddOp(new MaxonScopeEndOp(scopeVars, [retvalName]));
    block.AddOp(new MaxonReturnOp(structLit.Result));
  }

  /// <summary>
  /// Synthesize equals() method body for an auto-Equatable struct type.
  /// Compares each field using == (for primitives) or .equals() (for structs).
  /// </summary>
  private void SynthesizeStructEquals(MlirModule<MaxonOp> module, string typeName, MlirStructType structType) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? typeName : $"{namespace_}.{typeName}";

    var equalsName = $"{qualifiedTypeName}.equals";
    var equalsFunc = module.Functions.FirstOrDefault(f => f.Name == equalsName);
    if (equalsFunc == null) return;

    // Only synthesize if the stub has no body (user didn't provide their own)
    if (equalsFunc.Body.Blocks.Count > 0) return;

    module.Functions.Remove(equalsFunc);
    equalsFunc = new MlirFunction<MaxonOp>(
      equalsName, ["self", "other"], [(MlirType)structType, (MlirType)structType], MlirType.I1, null) {
      SourceFilePath = _sourceFilePath
    };
    module.AddFunction(equalsFunc);
    var block = equalsFunc.Body.AddBlock("entry");

    var selfParam = new MaxonStructParamOp(0, "self", typeName);
    block.AddOp(selfParam);
    var otherParam = new MaxonStructParamOp(1, "other", typeName);
    block.AddOp(otherParam);

    // Start with true, AND each field comparison
    MaxonValue? accumulator = null;

    foreach (var field in structType.Fields) {
      var fieldKind = field.Type.ToValueKind();
      string? fieldStructTypeName = null;
      if (field.Type is MlirStructType fst) fieldStructTypeName = fst.Name;
      else if (field.Type is MlirUnionType fut) fieldStructTypeName = fut.Name;

      var selfAccess = new MaxonFieldAccessOp(selfParam.Result, typeName, field.Name, fieldKind, fieldStructTypeName);
      block.AddOp(selfAccess);
      var otherAccess = new MaxonFieldAccessOp(otherParam.Result, typeName, field.Name, fieldKind, fieldStructTypeName);
      block.AddOp(otherAccess);

      MaxonValue fieldEqual;
      if (field.Type is MlirStructType nestedStruct) {
        // Nested struct: call .equals()
        var nestedQualified = string.IsNullOrEmpty(namespace_) ? nestedStruct.Name : $"{namespace_}.{nestedStruct.Name}";
        var nestedEqualsName = $"{nestedQualified}.equals";
        var nestedEqualsCall = new MaxonCallOp(nestedEqualsName, [selfAccess.Result, otherAccess.Result], MaxonValueKind.Bool);
        block.AddOp(nestedEqualsCall);
        fieldEqual = nestedEqualsCall.Result!;
      } else {
        // Primitives have value semantics — bitwise equality is sufficient
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, selfAccess.Result, otherAccess.Result, fieldKind);
        block.AddOp(cmpOp);
        fieldEqual = cmpOp.Result;
      }

      if (accumulator == null) {
        accumulator = fieldEqual;
      } else {
        var andOp = new MaxonBinOp(MaxonBinOperator.And, accumulator, fieldEqual, MaxonValueKind.Bool);
        block.AddOp(andOp);
        accumulator = andOp.Result;
      }
    }

    // Handle empty structs (no fields) - always equal
    if (accumulator == null) {
      var trueOp = new MaxonLiteralOp(true);
      block.AddOp(trueOp);
      accumulator = trueOp.Result;
    }

    block.AddOp(new MaxonScopeEndOp([]));
    block.AddOp(new MaxonReturnOp(accumulator));
  }

  /// <summary>
  /// Synthesize hash() and equals() method bodies for an enum type.
  /// hash() delegates to self.rawValue.hash(); equals() delegates to rawValue comparison.
  /// </summary>
  private void SynthesizeUnionHashAndEquals(MlirModule<MaxonOp> module, string enumName, MlirUnionType enumType) {
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? enumName : $"{namespace_}.{enumName}";
    var backingKind = GetUnionBackingKind(enumType);

    // Determine backing type name for method dispatch
    string backingTypeName;
    if (enumType.BackingType is MlirStringBackingType) backingTypeName = "String";
    else if (enumType.BackingType is MlirCharBackingType) backingTypeName = "Character";
    else if (enumType.BackingType == MlirType.F64) backingTypeName = "float";
    else if (enumType.BackingType == MlirType.I64 || enumType.BackingType == null) backingTypeName = "int";
    else throw new InvalidOperationException($"Unsupported enum backing type for Hashable: {enumType.BackingType}");

    // --- hash() ---
    var hashName = $"{qualifiedTypeName}.hash";
    var hashFunc = module.Functions.First(f => f.Name == hashName);
    module.Functions.Remove(hashFunc);
    hashFunc = new MlirFunction<MaxonOp>(hashName, ["self"], [(MlirType)enumType], MlirType.I64, null) {
      SourceFilePath = _sourceFilePath
    };
    module.AddFunction(hashFunc);
    var hashBlock = hashFunc.Body.AddBlock("entry");

    // self param
    var selfParam = new MaxonEnumParamOp(0, "self", enumName, backingKind);
    hashBlock.AddOp(selfParam);

    var rawValue = EmitUnionRawValueExtraction(hashBlock, selfParam.Result, enumType, enumName, backingKind);

    // For string/char backing, the raw value is a heap-allocated struct that must be cleaned up
    var scopeEndVars = new List<string>();
    if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
      var rawVarName = "__hash_raw";
      hashBlock.AddOp(new MaxonAssignOp(rawVarName, rawValue, isDeclaration: true, isMutable: true, MaxonValueKind.Struct));
      scopeEndVars.Add(rawVarName);
    }

    // Call backingType.hash(rawValue)
    var hashCall = new MaxonCallOp($"{backingTypeName}.hash", [rawValue], MaxonValueKind.Integer);
    hashBlock.AddOp(hashCall);
    hashBlock.AddOp(new MaxonScopeEndOp(scopeEndVars));
    hashBlock.AddOp(new MaxonReturnOp(hashCall.Result));

    // --- equals() ---
    var equalsName = $"{qualifiedTypeName}.equals";
    var equalsFunc = module.Functions.First(f => f.Name == equalsName);
    module.Functions.Remove(equalsFunc);
    equalsFunc = new MlirFunction<MaxonOp>(equalsName, ["self", "other"], [(MlirType)enumType, (MlirType)enumType], MlirType.I1, null) {
      SourceFilePath = _sourceFilePath
    };
    module.AddFunction(equalsFunc);
    var equalsBlock = equalsFunc.Body.AddBlock("entry");

    var selfParam2 = new MaxonEnumParamOp(0, "self", enumName, backingKind);
    equalsBlock.AddOp(selfParam2);
    var otherParam = new MaxonEnumParamOp(1, "other", enumName, backingKind);
    equalsBlock.AddOp(otherParam);

    var selfRaw = EmitUnionRawValueExtraction(equalsBlock, selfParam2.Result, enumType, enumName, backingKind);
    var otherRaw = EmitUnionRawValueExtraction(equalsBlock, otherParam.Result, enumType, enumName, backingKind);

    // For string/char backing, raw values are heap-allocated structs that must be cleaned up
    var eqScopeEndVars = new List<string>();
    if (enumType.BackingType is MlirStringBackingType or MlirCharBackingType) {
      equalsBlock.AddOp(new MaxonAssignOp("__eq_self_raw", selfRaw, isDeclaration: true, isMutable: true, MaxonValueKind.Struct));
      equalsBlock.AddOp(new MaxonAssignOp("__eq_other_raw", otherRaw, isDeclaration: true, isMutable: true, MaxonValueKind.Struct));
      eqScopeEndVars.AddRange(["__eq_self_raw", "__eq_other_raw"]);
    }

    // Call backingType.equals(selfRaw, otherRaw)
    var equalsCall = new MaxonCallOp($"{backingTypeName}.equals", [selfRaw, otherRaw], MaxonValueKind.Bool);
    equalsBlock.AddOp(equalsCall);
    equalsBlock.AddOp(new MaxonScopeEndOp(eqScopeEndVars));
    equalsBlock.AddOp(new MaxonReturnOp(equalsCall.Result));
  }

  /// <summary>
  /// Emit ops to extract the raw value from an enum value, dispatching to the
  /// correct op type based on backing type (String, Character, or numeric).
  /// </summary>
  private static MaxonValue EmitUnionRawValueExtraction(
      MlirBlock<MaxonOp> block, MaxonValue enumValue, MlirUnionType enumType,
      string enumName, MaxonValueKind backingKind) {
    if (enumType.BackingType is MlirStringBackingType) {
      var rawOp = new MaxonEnumStringRawValueOp(enumValue, enumName, isChar: false);
      block.AddOp(rawOp);
      return rawOp.Result;
    }
    if (enumType.BackingType is MlirCharBackingType) {
      var rawOp = new MaxonEnumStringRawValueOp(enumValue, enumName, isChar: true);
      block.AddOp(rawOp);
      return rawOp.Result;
    }
    var numericRawOp = new MaxonEnumRawValueOp(enumValue, enumName, backingKind);
    block.AddOp(numericRawOp);
    return numericRawOp.Result;
  }

  private static MaxonValueKind GetUnionBackingKind(MlirUnionType enumType) {
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
  private ExprResult.Direct ParseUnionStaticMethod(MlirUnionType enumType, string methodName) {
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Expect(TokenType.LeftParen);

    if (methodName == "fromRawValue") {
      return ParseUnionFromRawValue(enumType, methodToken);
    } else if (methodName == "fromName") {
      return ParseUnionFromName(enumType, methodToken);
    }
    throw new InvalidOperationException($"Unknown enum static method: '{methodName}'");
  }

  /// Source-level keyword ("enum" or "union") for error messages.
  private static string DeclKeyword(MlirUnionType enumType) =>
    enumType is MlirEnumType ? "enum" : "union";

  /// <summary>
  /// Emits an array literal containing all enum cases in declaration order.
  /// Reuses the existing array literal infrastructure (EmitManagedMemoryFromElements).
  /// </summary>
  private ExprResult EmitEnumAllCases(MlirEnumType enumType, Token token) {
    var elements = new List<MaxonValue>();
    foreach (var enumCase in enumType.Cases) {
      MaxonEnumLiteralOp enumLitOp;
      if (enumType.BackingType == MlirType.F64) {
        enumLitOp = new MaxonEnumLiteralOp(enumType.Name, enumCase.Name, (double)enumCase.RawValue!);
      } else if (enumType.BackingType == MlirType.I64) {
        enumLitOp = new MaxonEnumLiteralOp(enumType.Name, enumCase.Name, (long)enumCase.RawValue!);
      } else if (enumType.BackingType is null or MlirStringBackingType or MlirCharBackingType) {
        enumLitOp = new MaxonEnumLiteralOp(enumType.Name, enumCase.Name, (long)enumCase.Ordinal);
      } else {
        throw new InvalidOperationException($"Unsupported enum backing type: {enumType.BackingType}");
      }
      _currentBlock!.AddOp(enumLitOp);
      elements.Add(enumLitOp.Result);
    }

    var (managedStruct, arrayTag, elementCount, elementKind, elementStructTypeName) =
      EmitManagedMemoryFromElements(elements);

    var arrayTypeName = FindArrayTypeAliasForElement(elementKind, elementStructTypeName);

    var iterIndexLit = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(iterIndexLit);

    var arrayFields = new List<(string Name, MaxonValue Value)> {
      ("iterIndex", iterIndexLit.Result),
      ("managed", managedStruct.Result)
    };
    var arrayStruct = new MaxonStructLiteralOp(arrayTypeName, arrayFields);
    _currentBlock!.AddOp(arrayStruct);

    arrayStruct.ArrayLiteralTag = arrayTag;
    arrayStruct.ArrayLiteralCount = elementCount;

    return ParseFieldAccessChain(new ExprResult.Direct(arrayStruct.Result), token);
  }

  private ExprResult.Direct ParseUnionFromRawValue(MlirUnionType enumType, Token methodToken) {
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

  private void ValidateFromRawValueLiteral(MlirUnionType enumType, Token methodToken) {
    // Check if the argument is a compile-time literal
    var lastOps = _currentBlock!.Operations;
    if (lastOps.Count < 1) return;

    // The call op hasn't been emitted yet, so the literal is the last op
    var prevOp = lastOps[^1];
    if (prevOp is not MaxonLiteralOp litOp) return;

    // For simple/int-backed enums, check if literal matches any case's raw value
    var caseKind = DeclKeyword(enumType);
    if (enumType.BackingType == null) {
      // Simple enum: raw value is ordinal
      var ordinal = litOp.IntValue;
      if (ordinal < 0 || ordinal >= enumType.Cases.Count) {
        throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
          $"no {caseKind} case with raw value '{ordinal}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);
      }
    } else if (enumType.BackingType == MlirType.I64) {
      var intVal = litOp.IntValue;
      if (!enumType.Cases.Any(c => c.RawValue is long rv && rv == intVal)) {
        throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
          $"no {caseKind} case with raw value '{intVal}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);
      }
    } else if (enumType.BackingType == MlirType.F64) {
      var floatVal = litOp.FloatValue;
      if (!enumType.Cases.Any(c => c.RawValue is double rv && rv == floatVal)) {
        throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
          $"no {caseKind} case with raw value '{floatVal}': '{enumType.Name}'",
          methodToken.Line, methodToken.Column);
      }
    }
    // String/char-backed enums use MaxonStringLiteralOp/MaxonCharLiteralOp, not MaxonLiteralOp,
    // so they never reach this validation path — runtime fromRawValue handles them.
  }

  private ExprResult.Direct ParseUnionFromName(MlirUnionType enumType, Token methodToken) {
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

    // When the name argument is a compile-time literal, validate it now for a better error
    // message than a runtime panic. Walk backwards to match the result id to its origin op.
    bool isLiteral = false;
    string? literalName = null;
    var lastOps = _currentBlock!.Operations;
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
      var matchedCase = enumType.GetCase(literalName) ?? throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
          $"no {DeclKeyword(enumType)} case named '{literalName}': '{enumType.Name}'",
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
    var fieldToken = _tokens[_pos - 1];
    Expect(TokenType.Equals);

    var qualifiedName = $"{typeName}.{fieldName}";

    // Save position to try constant evaluation first, fall back to lazy init
    int exprStart = _pos;
    SkipToEndOfLine();
    int exprEnd = _pos;

    // Check if this is a complex initializer (function call, struct literal, array literal)
    if (IsComplexStaticInitializer(exprStart)) {
      // Lazy static field: defer initialization to first access
      var guardName = $"{qualifiedName}.__initialized";
      module.Globals.Add(new MlirGlobal(qualifiedName, MlirType.I64, new IntegerAttr(0, MlirType.I64)));
      module.Globals.Add(new MlirGlobal(guardName, MlirType.I1, new IntegerAttr(0, MlirType.I1)));

      var typeName2 = InferDeferredTypeName(new DeferredDecl(qualifiedName, exprStart, exprEnd, fieldToken.Line, fieldToken.Column));
      var gvarInfo = new GlobalVarMetadata(MaxonValueKind.Struct, isMutable, TypeName: typeName2, IsLazy: true);
      _globalVars[qualifiedName] = gvarInfo;
      module.GlobalVarInfos[qualifiedName] = gvarInfo;
      _globalVars[guardName] = new GlobalVarMetadata(MaxonValueKind.Bool, true);

      _lazyStaticFields.Add(new LazyStaticField(qualifiedName, guardName, _tokens, exprStart, exprEnd, isMutable, fieldToken.Line, fieldToken.Column));
    } else {
      // Simple constant initializer — evaluate at compile time
      _pos = exprStart;
      var value = EvalConstExpr([], _topLevelConstants, []);
      var (fieldType, defaultValue) = ConstValueToAttribute(value, fieldToken.Line, fieldToken.Column);

      if (isMutable) {
        module.Globals.Add(new MlirGlobal(qualifiedName, fieldType, defaultValue));
        _globalVars[qualifiedName] = new GlobalVarMetadata(fieldType.ToValueKind(), true);
      } else {
        RegisterStaticLetConstant(qualifiedName, fieldType, defaultValue);
      }
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
    var nameToken = ExpectIdentifierLike();

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
    var nameToken = ExpectIdentifierLike();

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

    // Instance method gets 'self' as first parameter
    MlirType selfType = TryGetPrimitiveMlirType(typeName) ?? (MlirType)_typeRegistry[typeName];

    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { selfType };
    allParamTypes.AddRange(paramTypes);

    // Store defaults (offset by 1 for 'self' parameter)
    var offsetDefaults = new Dictionary<int, MlirAttribute>();
    foreach (var (idx, attr) in paramDefaults) {
      offsetDefaults[idx + 1] = attr;
    }

    SetupFunctionParsing(module, methodName, allParamNames, allParamTypes, offsetDefaults, returnType, throwsType);

    if (PrimitiveTypes.TryGetValue(typeName, out var primInfo)) {
      // Primitive type: emit simple param op for 'self'
      var selfParamOp = new MaxonParamOp(0, "self", primInfo.Kind);
      _currentBlock!.AddOp(selfParamOp);
      _variables.Declare("self", primInfo.Kind, false, selfParamOp.Result, _currentBlock!, OwnershipFlags.IsParam);
    } else {
      var structType = (MlirStructType)_typeRegistry[typeName];

      // Emit self param (struct) and register fields as accessible variables
      var selfParamOp = new MaxonStructParamOp(0, "self", typeName);
      _currentBlock!.AddOp(selfParamOp);
      _variables.Declare("self", MaxonValueKind.Struct, false, selfParamOp.Result, _currentBlock!, OwnershipFlags.IsParam, structTypeName: typeName);

      // Register all fields of 'self' as directly accessible variables
      foreach (var field in structType.Fields) {
        var fieldKind = field.Type.ToValueKind();
        string? fieldStructName = field.Type is MlirStructType fst ? fst.Name
          : field.Type is MlirUnionType fut ? fut.Name
          : null;
        // Type parameter fields with where constraints: treat as struct with the param name
        // so method calls can be resolved through the interface during monomorphization
        if (fieldStructName == null && field.Type is MlirTypeParameterType tp
            && structType.WhereConstraints.ContainsKey(tp.ParameterName)) {
          fieldStructName = tp.ParameterName;
          fieldKind = MaxonValueKind.Struct;
        }
        var fieldAccessOp = new MaxonFieldAccessOp(selfParamOp.Result, typeName, field.Name, fieldKind, fieldStructName);
        _currentBlock!.AddOp(fieldAccessOp);
        _variables.Declare(field.Name, fieldKind, field.IsMutable, fieldAccessOp.Result, _currentBlock!, structTypeName: fieldStructName, isSelfField: true);
      }
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
      Dictionary<int, MlirAttribute> paramDefaults, MlirType? returnType, MlirType? throwsType = null,
      int? nameLine = null, int? nameColumn = null) {
    // Determine the registration name (may be mangled for overloads)
    var registrationName = funcName;
    // Check if this function has been mangled (overload exists)
    var mangledName = MangleOverloadName(funcName, paramNames);
    if (module.Functions.Any(f => f.Name == mangledName)) {
      registrationName = mangledName;
    }
    // Also check type-augmented mangled name for same-name-different-type overloads
    var typeMangledName = MangleOverloadNameWithTypes(funcName, paramNames, paramTypes);
    if (module.Functions.Any(f => f.Name == typeMangledName)) {
      registrationName = typeMangledName;
    }

    // Replace pre-registered stub with full function (or create new one)
    var existing = module.Functions.FirstOrDefault(f => f.Name == registrationName);
    if (existing != null && existing.Body.Blocks.Count > 0) {
      throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
        $"Duplicate function '{funcName}'", nameLine, nameColumn);
    }
    if (existing != null) {
      module.Functions.Remove(existing);
    }
    var func = new MlirFunction<MaxonOp>(registrationName, paramNames, paramTypes, returnType, throwsType);
    if (existing != null) {
      func.IsExported = existing.IsExported;
      func.SourceFilePath = existing.SourceFilePath;
    }
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
    _localVarLocations.Clear();
    _blockCounter = 0;

    return func;
  }

  /// <summary>
  /// Emits parameter ops and registers them as variables.
  /// paramOffset is the index offset for the MLIR param op (e.g., 1 for instance methods with 'self').
  /// </summary>
  private void EmitParameters(List<string> paramNames, List<MlirType> paramTypes, List<Token> paramTokens, int paramOffset = 0) {
    for (int i = 0; i < paramNames.Count; i++) {
      if (paramNames[i] != "_") {
        _paramLocations.Add((paramNames[i], paramTokens[i].Line, paramTokens[i].Column));
      }
      var paramType = paramTypes[i];
      if (paramType is MlirStructType structType) {
        var structParamOp = new MaxonStructParamOp(i + paramOffset, paramNames[i], structType.Name);
        _currentBlock!.AddOp(structParamOp);
        _variables.Declare(paramNames[i], MaxonValueKind.Struct, true, structParamOp.Result, _currentBlock!, OwnershipFlags.IsParam, structTypeName: structType.Name);
      } else if (paramType is MlirUnionType enumType) {
        var backingKind = GetUnionBackingKind(enumType);
        var enumParamOp = new MaxonEnumParamOp(i + paramOffset, paramNames[i], enumType.Name, backingKind);
        _currentBlock!.AddOp(enumParamOp);
        _variables.Declare(paramNames[i], MaxonValueKind.Enum, true, enumParamOp.Result, _currentBlock!, OwnershipFlags.IsParam, structTypeName: enumType.Name);
      } else if (paramType is MlirFunctionType fnType) {
        var fnParamOp = new MaxonFunctionParamOp(i + paramOffset, paramNames[i], fnType);
        _currentBlock!.AddOp(fnParamOp);
        _variables.Declare(paramNames[i], MaxonValueKind.Function, true, fnParamOp.Result, _currentBlock!, OwnershipFlags.IsParam, fnTypeName: fnType);
      } else if (paramType is MlirRangedPrimitiveType rangedParam) {
        var kind = rangedParam.BaseType.ToValueKind();
        var paramOp = new MaxonParamOp(i + paramOffset, paramNames[i], kind);
        _currentBlock!.AddOp(paramOp);
        _variables.Declare(paramNames[i], kind, true, paramOp.Result, _currentBlock!, OwnershipFlags.IsParam, structTypeName: rangedParam.Name);
      } else if (paramType is MlirTypeParameterType tp) {
        var paramOp = new MaxonParamOp(i + paramOffset, paramNames[i], MaxonValueKind.TypeParameter);
        _currentBlock!.AddOp(paramOp);
        _variables.Declare(paramNames[i], MaxonValueKind.TypeParameter, true, paramOp.Result, _currentBlock!, OwnershipFlags.IsParam, structTypeName: tp.ParameterName);
      } else {
        var kind = paramType.ToValueKind();
        var paramOp = new MaxonParamOp(i + paramOffset, paramNames[i], kind);
        _currentBlock!.AddOp(paramOp);
        _variables.Declare(paramNames[i], kind, true, paramOp.Result, _currentBlock!, OwnershipFlags.IsParam);
      }
    }
  }

  private void CheckUnusedVariables() {
    foreach (var (paramName, paramLine, paramCol) in _paramLocations) {
      if (!_referencedVars.Contains(paramName)) {
        throw new CompileError(ErrorCode.SemanticUnusedVariable, $"unused variable: '{paramName}'", paramLine, paramCol);
      }
    }
    foreach (var (varName, varLine, varCol) in _localVarLocations) {
      if (!_referencedVars.Contains(varName)) {
        throw new CompileError(ErrorCode.SemanticUnusedVariable, $"unused variable: '{varName}'", varLine, varCol);
      }
    }
  }

  private void FinishFunctionBody(string name, Token nameToken, MlirType? returnType) {
    CheckUnusedVariables();

    // E037: Check for missing return statements
    if (returnType != null && _currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      throw new CompileError(ErrorCode.SemanticMissingReturn, $"missing return statement: '{name}'", nameToken.Line, nameToken.Column);
    }

    if (returnType == null && _currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
      _currentBlock.AddOp(new MaxonReturnOp());
    }

    _currentFunction = null;
    _currentBlock = null;
  }

  private void CheckUnusedTypeAliases() {
    foreach (var aliasName in _localTypeAliases) {
      if (_exportedTypeAliases.Contains(aliasName)) continue;
      if (_usedTypeAliases.Contains(aliasName)) continue;
      if (!_typeAliasLocations.TryGetValue(aliasName, out var loc)) continue;
      throw new CompileError(ErrorCode.SemanticUnusedTypeAlias,
        $"unused typealias: '{aliasName}'", loc.Line, loc.Column);
    }
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
    // Union/enum case: TypeName.caseName
    if (CheckIdentifierLike() && PeekNext().Type == TokenType.Dot) {
      var typeToken = Advance(); // consume type name
      Advance(); // consume '.'
      var caseToken = ExpectIdentifierLike();
      var typeName = typeToken.Value;
      // Resolve the type to validate it exists and is a union
      var resolvedName = ResolveBaseTypeName(typeName);
      if (!_typeRegistry.TryGetValue(resolvedName, out var type) || type is not MlirUnionType unionType) {
        throw new CompileError(ErrorCode.ParserExpectedExpression,
          $"'{typeName}' is not a known union type for default value",
          typeToken.Line, typeToken.Column);
      }
      var _ = unionType.GetCase(caseToken.Value) ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
          $"'{caseToken.Value}' is not a case of union '{typeName}'",
          caseToken.Line, caseToken.Column);
      return (type, new EnumAttr(resolvedName, caseToken.Value));
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression, "Expected default value", Current().Line, Current().Column);
  }

  /// Captures the tokens of a default value expression without evaluating it.
  /// The tokens are stored and re-parsed via ParseExpression() at each call site.
  /// This supports any literal expression as a default value.
  private TokenRangeAttr CaptureDefaultValueTokens() {
    var startPos = _pos;
    // Skip over the expression by tracking bracket/brace/paren nesting.
    // The expression ends at an unmatched ',' or ')' at depth 0.
    var depth = 0;
    while (_pos < _tokens.Count) {
      var type = Current().Type;
      if (type == TokenType.LeftParen || type == TokenType.LeftBracket || type == TokenType.LeftBrace) {
        depth++;
      } else if (type == TokenType.RightParen || type == TokenType.RightBracket || type == TokenType.RightBrace) {
        if (depth == 0) break;
        depth--;
      } else if (type == TokenType.Comma && depth == 0) {
        break;
      } else if (type == TokenType.Newline && depth == 0) {
        break;
      }
      _pos++;
    }
    if (_pos == startPos) {
      throw new CompileError(ErrorCode.ParserExpectedExpression, "Expected default value", Current().Line, Current().Column);
    }
    return new TokenRangeAttr(_tokens.GetRange(startPos, _pos - startPos));
  }

  private void ParseFunction(MlirModule<MaxonOp> module) {
    Expect(TokenType.Function);
    var nameToken = ExpectIdentifierLike();
    var baseName = nameToken.Value;

    // Top-level functions get qualified with file-based namespace (except main)
    var namespace_ = DeriveNamespace();
    var name = (baseName == "main" || string.IsNullOrEmpty(namespace_)) ? baseName : $"{namespace_}.{baseName}";

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
    var func = SetupFunctionParsing(module, name, paramNames, paramTypes, paramDefaults, returnType, throwsType,
      nameLine: nameToken.Line, nameColumn: nameToken.Column);
    func.SourceLine = nameToken.Line;
    func.SourceColumn = nameToken.Column;
    EmitParameters(paramNames, paramTypes, paramTokens);

    ParseBodyUntilEnd();
    ExpectEndLabel(baseName);
    FinishFunctionBody(baseName, nameToken, returnType);

    // Emit deferred top-level expression lets/vars into a separate __module_init function
    // that runs in the root scope (before main), so globals survive main's scope exit.
    // Emitted after main so __module_init's IDs don't shift main's labels.
    if (baseName == "main") {
      EmitModuleInitFunction(module);
    }
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
        var paramToken = ExpectIdentifierLike();
        var paramType = ParseTypeRef();
        if (Check(TokenType.Equals)) {
          Advance(); // consume '='
          defaults[paramIndex] = CaptureDefaultValueTokens();
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

  private static void RejectBarePrimitiveTypeArgs(List<MlirType> typeArgs, Token errorToken) {
    foreach (var ct in typeArgs) {
      if (ct.IsBarePrimitive)
        throw new CompileError(ErrorCode.ParserExpectedType,
          $"Cannot use bare type '{MlirType.FormatAsSourceName(ct)}' as a type argument; use a ranged typealias instead (e.g. typealias MyType = {MlirType.FormatAsSourceName(ct)}(...))",
          errorToken.Line, errorToken.Column);
    }
  }

  private MlirType ParseTypeRef() {
    // Parenthesized types: function type or tuple type
    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      var paramTypes = new List<MlirType>();
      while (!Check(TokenType.RightParen) && !IsAtEnd()) {
        // Check if this is a named parameter (name Type) or just a type
        if (CheckIdentifierLike() && PeekNext().Type != TokenType.Comma && PeekNext().Type != TokenType.RightParen) {
          // This looks like "name Type" - skip the name
          Advance(); // consume name
        }
        paramTypes.Add(ParseTypeRef());
        if (Check(TokenType.Comma)) Advance();
      }
      Expect(TokenType.RightParen);
      if (Check(TokenType.Returns)) {
        // Function type: (ParamType, ...) returns ReturnType
        Advance();
        var returnType = ParseTypeRef();
        return new MlirFunctionType(paramTypes, returnType);
      }
      // 0 or 1 params without 'returns' → function type (void return)
      if (paramTypes.Count < 2)
        return new MlirFunctionType(paramTypes, null);
      // 2+ params without 'returns' → tuple type
      return GetOrCreateTupleType(paramTypes);
    }

    var typeNamePos = _pos;
    var typeName = ExpectTypeName();
    return typeName switch {
      "int" => _parsingTypeAliasRhs || _parsingExtension
        ? MlirType.I64
        : throw new CompileError(ErrorCode.SemanticTypeMismatch,
            "Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)",
            _tokens[typeNamePos].Line, _tokens[typeNamePos].Column),

      "float" => _parsingTypeAliasRhs || _parsingExtension
        ? MlirType.F64
        : throw new CompileError(ErrorCode.SemanticTypeMismatch,
            "Cannot use bare 'float' as a type. Define a typealias with range constraints, e.g., typealias MyFloat = float(0.0 to 1.0)",
            _tokens[typeNamePos].Line, _tokens[typeNamePos].Column),
      "byte" => _parsingTypeAliasRhs || _parsingExtension
        ? MlirType.I8
        : throw new CompileError(ErrorCode.SemanticTypeMismatch,
            "Cannot use bare 'byte' as a type. Define a typealias with range constraints, e.g., typealias MyByte = byte(0 to u8.max)",
            _tokens[typeNamePos].Line, _tokens[typeNamePos].Column),
      "bool" => MlirType.I1,
      _ => _resolvingTypeAliases.Contains(typeName)
        ? throw new CompileError(ErrorCode.ParserCircularDependency,
            $"Circular typealias dependency: {typeName}",
            _tokens[typeNamePos].Line, _tokens[typeNamePos].Column)
        : seedModule?.AmbiguousTypeNames.Contains(typeName) == true
        ? throw new CompileError(ErrorCode.SemanticAmbiguousTypeReference,
            $"Ambiguous type '{typeName}': multiple definitions exist across files",
            _tokens[typeNamePos].Line, _tokens[typeNamePos].Column)
        : IsNonExportedCrossFileType(typeName)
        ? throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown type: {typeName}",
            _tokens[typeNamePos].Line, _tokens[typeNamePos].Column)
        : _typeRegistry.TryGetValue(typeName, out var structType)
        ? MarkTypeAliasUsed(typeName, structType)
        : throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown type: {typeName}", Current().Line, Current().Column)
    };
  }

  private MlirType MarkTypeAliasUsed(string name, MlirType type) {
    _usedTypeAliases.Add(name);
    return type;
  }

  private bool IsNonExportedCrossFileType(string typeName) {
    if (seedModule == null || _sourceFilePath == null) return false;
    if (!seedModule.NonExportedTypeNames.Contains(typeName)) return false;
    return seedModule.TypeDefSourceFiles.TryGetValue(typeName, out var sourceFile)
        && sourceFile != _sourceFilePath;
  }

  private MlirStructType GetOrCreateTupleType(List<MlirType> elementTypes) {
    var name = MlirStructType.TupleMangledName(elementTypes);
    if (_typeRegistry.TryGetValue(name, out var existing) && existing is MlirStructType st)
      return st;
    var tupleType = MlirStructType.CreateTupleType(elementTypes);
    _typeRegistry[name] = tupleType;
    return tupleType;
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
    // Accept ranged typealias names (e.g., "value as Age")
    if (Check(TokenType.Identifier)) {
      var name = Current().Value;
      if (_typeRegistry.TryGetValue(name, out var type) && type is MlirRangedPrimitiveType rpt) {
        Advance();
        _usedTypeAliases.Add(name);
        _lastCastRangedType = rpt;
        return rpt.BaseType.ToValueKind();
      }
    }
    throw new CompileError(ErrorCode.ParserExpectedType, "Expected type name after 'as'", Current().Line, Current().Column);
  }

  // ============================================================================
  // Statement parsing
  // ============================================================================

  private void PushScope() {
    _variables.PushScope();
  }

  private void PopScope() {
    _variables.PopScope();
  }

  private void ParseBodyUntilEnd() {
    bool wasDeadCode = false;
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;
      // After exhaustive match/if-else where all branches terminate, _currentBlock
      // becomes null. Create a dead block so subsequent unreachable code can still
      // be parsed for syntax validation without crashing.
      if (_currentBlock == null) {
        if (!wasDeadCode) {
          var unreachable = Current();
          throw new CompileError(ErrorCode.SemanticUnreachableCode,
            "unreachable code after exhaustive match or branching where all paths return/throw",
            unreachable.Line, unreachable.Column);
        }
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
    if (Check(TokenType.HashIf)) {
      HandleConditionalCompilation();
      return;
    }
    if (Check(TokenType.HashElse)) {
      HandleConditionalElse();
      return;
    }
    if (Check(TokenType.HashEndif)) {
      HandleConditionalEndif();
      return;
    }
    if (Check(TokenType.Return)) {
      ParseReturn();
    } else if (Check(TokenType.At)) {
      ParseAnnotatedDecl();
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
    } else if (Check(TokenType.Skip)) {
      ParseSkip();
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
    } else if (Check(TokenType.Panic)) {
      ParsePanic();
    } else if (Check(TokenType.Try)) {
      ParseTryStatement();
    } else if (Check(TokenType.LeftParen)) {
      ParseTupleAssignment();
    } else if (Check(TokenType.Await)) {
      // Statement-form await: `await p` (result discarded)
      var awaitToken = Advance();
      ParseAwaitExpression(awaitToken);
    } else {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, $"unexpected token: '{Current().Value}'", Current().Line, Current().Column);
    }
  }

  private void ParseDotStatement() {
    var nameToken = _tokens[_pos];

    // Tuple positional field assignment: t.0 = value
    if (_pos + 2 < _tokens.Count && _tokens[_pos + 2].Type == TokenType.IntegerLiteral) {
      ParseAssignment();
      return;
    }

    // Look ahead further to determine what follows: identifier.identifier = ... or identifier.identifier(...)
    if (_pos + 2 < _tokens.Count && IsIdentifierLikeToken(_tokens[_pos + 2])) {
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
        ParseAssignment();
        return;
      }

      if (afterIdent.Type == TokenType.Dot) {
        // Nested field access chain: o.inner.x = ... or o.inner.method(...)
        // Scan ahead to find what terminates the chain
        if (IsNestedFieldChainEndingWith(TokenType.Equals)) {
          ParseAssignment();
          return;
        }
        if (IsNestedFieldChainEndingWith(TokenType.LeftParen)) {
          ParseNestedFieldMethodCall();
          return;
        }
      }

      if (afterIdent.Type == TokenType.LeftParen) {
        // Check for __Builtins static method call
        if (nameToken.Value == "__Builtins") {
          Advance(); // consume '__Builtins'
          Advance(); // consume '.'
          var methodToken = Advance(); // consume method name
          Advance(); // consume '('
          EmitBuiltinsStaticMethod(methodToken);
          return;
        }

        // Check for builtin managed type static methods as statements (void calls)
        {
          var resolvedBase = ResolveBaseTypeName(nameToken.Value);
          var nextMethod = _tokens[_pos + 2].Value;
          Func<string, (bool, MaxonValue?)>? dispatcher = resolvedBase switch {
            "__ManagedFile" when nextMethod is "statFree"
              => TryEmitBuiltinManagedFileStaticMethod,
            _ => null,
          };
          if (dispatcher != null) {
            _usedTypeAliases.Add(nameToken.Value);
            Advance(); // consume type name
            Advance(); // consume '.'
            var methodToken = Advance(); // consume method name
            Advance(); // consume '('
            var (handled, _) = dispatcher(methodToken.Value);
            if (handled) return;
            throw new CompileError(ErrorCode.ParserExpectedExpression,
              $"Unknown static method '{methodToken.Value}' on {resolvedBase}",
              methodToken.Line, methodToken.Column);
          }
        }

        // Check for static/qualified function call: Type.method(...)
        if (ResolveMethodName(qualifiedName) != null) {
          ParseQualifiedCallStatement();
          return;
        }

        // Check for instance method call: var.method(...)
        var resolved = ResolveVariable(nameToken.Value);
        var structTypeName = resolved switch {
          ResolvedVar.Local(var info) => info.StructTypeName,
          ResolvedVar.Global(var info) => info.TypeName,
          null => null,
          _ => throw new InvalidOperationException()
        };
        if (structTypeName != null) {
          var methodFieldName = _tokens[_pos + 2].Value;

          // Try builtin type method interception before normal method resolution
          var baseTypeName = ResolveBaseTypeName(structTypeName);
          if (IsBuiltinMethodType(baseTypeName)) {
            Advance(); // consume variable name
            Advance(); // consume '.'
            var methodToken = Advance(); // consume method name
            Advance(); // consume '('
            MaxonValue structVal = resolved switch {
              ResolvedVar.Local(var info) => info.Value,
              ResolvedVar.Global(var info) => EmitGlobalLoad(nameToken.Value, info).Value,
              _ => throw new InvalidOperationException()
            };
            var (handled, _) = TryEmitBuiltinTypeMethod(structTypeName, methodFieldName, structVal);
            if (handled) {
              return;
            }
            throw new CompileError(ErrorCode.ParserExpectedExpression,
              $"Unknown method '{methodFieldName}' on builtin type '{baseTypeName}'",
              methodToken.Line, methodToken.Column);
          }

          // Optimized String.append: builds directly into target buffer
          if (structTypeName == "String" && methodFieldName == "append") {
            Advance(); // consume variable name
            Advance(); // consume '.'
            Advance(); // consume 'append'
            Advance(); // consume '('
            MaxonValue structVal = resolved switch {
              ResolvedVar.Local(var info) => info.Value,
              ResolvedVar.Global(var info) => EmitGlobalLoad(nameToken.Value, info).Value,
              _ => throw new InvalidOperationException()
            };
            TrySkipArgLabel();
            EmitStringAppendBuiltin(structVal, nameToken.Value);
            Expect(TokenType.RightParen);
            return;
          }

          var instanceMethodName = $"{structTypeName}.{methodFieldName}";
          var resolvedName = ResolveMethodName(instanceMethodName);
          if (resolvedName != null) {
            ParseInstanceMethodCallStatement(nameToken.Value, resolved!, resolvedName);
            return;
          }

          // Type parameter: resolve method through where-constrained interfaces
          if (_typeRegistry.TryGetValue(structTypeName, out var regType2) && regType2 is MlirTypeParameterType) {
            var ifaceMethod = FindInterfaceMethod(methodFieldName, structTypeName);
            if (ifaceMethod != null) {
              ParseTypeParamMethodCallStatement(nameToken.Value, resolved!, structTypeName, methodFieldName, ifaceMethod);
              return;
            }
          }
        }

        // Check for promise method call: p.cancel()
        if (resolved is ResolvedVar.Local(var localInfo) && localInfo.Value is MaxonPromise) {
          var methodFieldName = _tokens[_pos + 2].Value;
          if (methodFieldName == "cancel") {
            Advance(); // consume variable name
            Advance(); // consume '.'
            Advance(); // consume 'cancel'
            Expect(TokenType.LeftParen);
            Expect(TokenType.RightParen);
            var cancelOp = new MaxonCancelPromiseOp(localInfo.Value);
            _currentBlock!.AddOp(cancelOp);
            return;
          }
          throw new CompileError(ErrorCode.MlirInvalidMethodCall,
            $"Promise has no method named '{methodFieldName}'",
            _tokens[_pos + 2].Line, _tokens[_pos + 2].Column);
        }
      }
    }

    // Fall through to assignment (handles field chains)
    ParseAssignment();
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
    // Also accepts integer literals for tuple positional access (e.g. t.0.something)
    // Returns true if the chain ends with the specified terminator token
    var i = _pos;
    while (i < _tokens.Count) {
      if (_tokens[i].Type != TokenType.Identifier && _tokens[i].Type != TokenType.IntegerLiteral) return false;
      i++;
      if (i >= _tokens.Count) return false;
      if (_tokens[i].Type == terminator) return true;
      if (_tokens[i].Type != TokenType.Dot) return false;
      i++;
    }
    return false;
  }

  /// Resolves a variable name by checking local scope then global scope.
  private ResolvedVar? ResolveVariable(string name) {
    if (_variables.TryGetValue(name, out var varInfo)) {
      _referencedVars.Add(name);
      return new ResolvedVar.Local(varInfo);
    }
    if (_globalVars.TryGetValue(name, out var globalVarInfo))
      return new ResolvedVar.Global(globalVarInfo);
    return null;
  }

  /// Resolves a variable and emits a load for globals, returning an ExprResult.
  private ExprResult LoadVariable(string name, Token token) {
    return ResolveVariable(name) switch {
      ResolvedVar.Local(var info) => new ExprResult.VarRef(name, info),
      ResolvedVar.Global(var info) => EmitGlobalLoad(name, info),
      null => throw CreateUndefinedVariableError(name, token),
      _ => throw new InvalidOperationException()
    };
  }

  private CompileError CreateUndefinedVariableError(string name, Token token,
      ErrorCode fallbackCode = ErrorCode.SemanticUndefinedVariable) {
    if (_typeRegistry.TryGetValue(name, out var type)) {
      var isGeneric = type is MlirStructType st && st.AssociatedTypeNames.Count > 0
        || type is MlirUnionType ut && ut.AssociatedTypeNames.Count > 0;
      if (isGeneric)
        return new CompileError(ErrorCode.SemanticUndefinedVariable,
          $"'{name}' requires a typealias before use, e.g.: typealias My{name} = {name} with <type>",
          token.Line, token.Column);
      return new CompileError(ErrorCode.SemanticUndefinedVariable,
        $"'{name}' is a type and cannot be used directly as a value",
        token.Line, token.Column);
    }
    return new CompileError(fallbackCode,
      $"Undefined variable '{name}'", token.Line, token.Column);
  }

  private ExprResult.Direct EmitGlobalLoad(string name, GlobalVarMetadata info) {
    var loadOp = new MaxonGlobalLoadOp(name, info.Kind, info.EnumTypeName,
      structTypeName: info.Kind == MaxonValueKind.Struct ? info.TypeName : null);

    // Check if this is a lazy static field — add guard info for lowering
    var lazyField = _lazyStaticFields.Find(f => f.QualifiedName == name);
    if (lazyField != null) {
      loadOp.LazyGuardName = lazyField.GuardName;
      loadOp.LazyInitFuncName = $"{lazyField.QualifiedName}.__lazy_init";
    }

    _currentBlock!.AddOp(loadOp);
    return new ExprResult.Direct(loadOp.Result);
  }

  /// Resolves a variable name to its struct value and type name, checking both local and global scope.
  private (MaxonValue value, string structTypeName) ResolveStructVariable(string name, Token nameToken, bool requireMutable = false) {
    switch (ResolveVariable(name)) {
      case ResolvedVar.Local(var info) when info.StructTypeName != null:
        if (requireMutable && !info.Mutable) {
          throw new CompileError(ErrorCode.ParserImmutableVariable,
            $"cannot assign to immutable variable: '{name}'",
            nameToken.Line, nameToken.Column);
        }
        return (ResolveExprValue(new ExprResult.VarRef(name, info)), info.StructTypeName);
      case ResolvedVar.Global(var info) when info.Kind == MaxonValueKind.Struct && info.TypeName != null:
        if (requireMutable && !info.Mutable) {
          throw new CompileError(ErrorCode.ParserImmutableVariable,
            $"cannot assign to immutable variable: '{name}'",
            nameToken.Line, nameToken.Column);
        }
        return (EmitGlobalLoad(name, info).Value, info.TypeName);
    }
    throw CreateUndefinedVariableError(name, nameToken);
  }

  private void ParseNestedFieldMethodCall() {
    // Handles: o.inner.field.method(args)
    // Walk the chain of field accesses until we reach the final identifier followed by '('
    var nameToken = Advance(); // consume root variable name
    var (currentValue, currentStructTypeName) = ResolveStructVariable(nameToken.Value, nameToken);

    // Walk intermediate field accesses: consume '.fieldName' pairs
    // Stop when the next segment is followed by '(' (method call)
    while (Check(TokenType.Dot)) {
      Advance(); // consume '.'
      var fieldToken = ExpectFieldName();

      if (Check(TokenType.LeftParen)) {
        // Try builtin type method
        var baseNestedType = ResolveBaseTypeName(currentStructTypeName);
        if (IsBuiltinMethodType(baseNestedType)) {
          Advance(); // consume '('
          var (handled, _) = TryEmitBuiltinTypeMethod(currentStructTypeName, fieldToken.Value, currentValue);
          if (handled) {
            // Builtin type methods don't need discarded-result tracking
            return;
          }
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Unknown method '{fieldToken.Value}' on builtin type '{baseNestedType}'",
            fieldToken.Line, fieldToken.Column);
        }

        // Optimized String.append: builds directly into target buffer
        if (currentStructTypeName == "String" && fieldToken.Value == "append") {
          Advance(); // consume '('
          TrySkipArgLabel();
          EmitStringAppendBuiltin(currentValue, nameToken.Value);
          Expect(TokenType.RightParen);
          return;
        }

        // This is the method call at the end of the chain
        var methodName = $"{currentStructTypeName}.{fieldToken.Value}";
        var resolvedName = ResolveMethodName(methodName)
          ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Undefined method '{fieldToken.Value}' on type '{currentStructTypeName}'",
            fieldToken.Line, fieldToken.Column);
        Advance(); // consume '('
        var qualifiedToken = new Token(TokenType.Identifier, resolvedName, fieldToken.Line, fieldToken.Column);
        var (args, callee) = ParseInstanceMethodCallArgs(qualifiedToken, currentValue);
        var callOp = CreateFunctionCall(qualifiedToken, args, callee);
        MarkDiscardedResult(callOp, fieldToken);
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

    var selfInfo = (ResolveVariable("self") as ResolvedVar.Local)?.Info
      ?? throw new CompileError(ErrorCode.SemanticUnexpectedToken, "'self' can only be used inside instance methods", selfToken.Line, selfToken.Column);

    if (Check(TokenType.Equals)) {
      // self.field = value
      Advance(); // consume '='
      var selfVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      EmitFieldAssignment(selfInfo.StructTypeName!, selfVal, fieldToken, selfToken);
    } else if (Check(TokenType.LeftParen)) {
      // self.method(...)
      // Try builtin type method
      var baseSelfType = ResolveBaseTypeName(selfInfo.StructTypeName!);
      if (IsBuiltinMethodType(baseSelfType)) {
        Advance(); // consume '('
        var selfVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
        var (handled, _) = TryEmitBuiltinTypeMethod(selfInfo.StructTypeName!, fieldToken.Value, selfVal);
        if (handled) {
          // Builtin type methods don't need discarded-result tracking
          return;
        }
        throw new CompileError(ErrorCode.ParserExpectedExpression,
          $"Unknown method '{fieldToken.Value}' on builtin type '{baseSelfType}'",
          fieldToken.Line, fieldToken.Column);
      }
      var methodName = $"{selfInfo.StructTypeName}.{fieldToken.Value}";
      var resolvedName = ResolveMethodName(methodName)
        ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined method '{fieldToken.Value}' on type '{selfInfo.StructTypeName}'", fieldToken.Line, fieldToken.Column);
      Advance(); // consume '('
      var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      var qualifiedToken = new Token(TokenType.Identifier, resolvedName, selfToken.Line, selfToken.Column);
      var (allArgs, selfCallee) = ParseInstanceMethodCallArgs(qualifiedToken, structVal);
      var callOp = CreateFunctionCall(qualifiedToken, allArgs, selfCallee);
      MarkDiscardedResult(callOp, fieldToken);
    } else if (Check(TokenType.Dot)) {
      // self.field.method(...) or self.field.subfield... chain
      var selfVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      var (currentValue, currentStructTypeName) = EmitIntermediateFieldAccess(selfVal, selfInfo.StructTypeName!, fieldToken);

      while (Check(TokenType.Dot)) {
        Advance(); // consume '.'
        var nextFieldToken = ExpectFieldName();

        if (Check(TokenType.LeftParen)) {
          // Try builtin type method
          var baseChainType = ResolveBaseTypeName(currentStructTypeName);
          if (IsBuiltinMethodType(baseChainType)) {
            Advance(); // consume '('
            var (handled, _) = TryEmitBuiltinTypeMethod(currentStructTypeName, nextFieldToken.Value, currentValue);
            if (handled) {
              // Builtin type methods don't need discarded-result tracking
              return;
            }
            throw new CompileError(ErrorCode.ParserExpectedExpression,
              $"Unknown method '{nextFieldToken.Value}' on builtin type '{baseChainType}'",
              nextFieldToken.Line, nextFieldToken.Column);
          }

          // Terminal method call
          var methodName = $"{currentStructTypeName}.{nextFieldToken.Value}";
          var resolvedName = ResolveMethodName(methodName)
            ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
              $"Undefined method '{nextFieldToken.Value}' on type '{currentStructTypeName}'",
              nextFieldToken.Line, nextFieldToken.Column);
          Advance(); // consume '('
          var qualifiedToken = new Token(TokenType.Identifier, resolvedName, nextFieldToken.Line, nextFieldToken.Column);
          var (args, callee) = ParseInstanceMethodCallArgs(qualifiedToken, currentValue);
          var callOp = CreateFunctionCall(qualifiedToken, args, callee);
          MarkDiscardedResult(callOp, nextFieldToken);
          return;
        }

        if (Check(TokenType.Equals)) {
          // Terminal field assignment
          Advance(); // consume '='
          EmitFieldAssignment(currentStructTypeName, currentValue, nextFieldToken, selfToken);
          return;
        }

        // Intermediate field access
        (currentValue, currentStructTypeName) = EmitIntermediateFieldAccess(currentValue, currentStructTypeName, nextFieldToken);
      }

      throw new CompileError(ErrorCode.ParserExpectedExpression,
        "Expected method call or assignment at end of field access chain",
        Current().Line, Current().Column);
    } else {
      throw new CompileError(ErrorCode.SemanticUnexpectedToken, $"Expected '=' or '(' after 'self.{fieldToken.Value}'", fieldToken.Line, fieldToken.Column);
    }
  }

  private void ParseReturn() {
    var returnToken = Current();
    Advance(); // consume 'return'

    if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
      var expr = ParseExpression();
      string? returnVarName = expr is ExprResult.VarRef rv ? rv.VarName : null;
      var value = ResolveExprValue(expr);
      CheckReturnType(value, returnToken);
      value = CheckReturnRange(value, returnToken);
      // For heap-allocated returns (structs and associated-value enums),
      // incref the return value so it survives the caller.
      // Self-fields are children of the struct, not independently owned — don't incref them.
      bool isAssocEnum = value is MaxonEnum me
        && _typeRegistry.TryGetValue(me.TypeName, out var retEnumTd)
        && retEnumTd is MlirUnionType { HasAssociatedValues: true };
      if (value is MaxonStruct || isAssocEnum) {
        // Self and self-fields are borrowed references — mark ReturnsSelf for downstream
        bool isSelfOrSelfField = returnVarName == "self"
          || (returnVarName != null
            && _variables.TryGetValue(returnVarName, out var retVarInfo)
            && retVarInfo.IsSelfField);
        if (isSelfOrSelfField) {
          if (expr is ExprResult.VarRef { VarName: "self" })
            _currentFunction!.ReturnsSelf = true;
        }
      }
      // Emit scope end — cleans all tracked vars except the returned one.
      // When returning a managed value that was not directly named (e.g. `return foo()` or
      // `return try foo() otherwise bar`), find its backing variable so scope cleanup skips it —
      // the caller takes ownership of the returned reference.
      HashSet<string>? keepVars = null;
      if (returnVarName != null) {
        keepVars = [returnVarName];
      } else if (expr is ExprResult.Direct) {
        // For `return foo()`: last op is a MaxonAssignOp for __call_tmp_X.
        // For `return try foo() otherwise bar`: last op is a MaxonStructVarRefOp/__MaxonEnumVarRefOp for __try_result_X.
        var lastOp = _currentBlock!.Operations.Count > 0 ? _currentBlock.Operations[^1] : null;
        string? backedByVar = lastOp switch {
          MaxonStructVarRefOp sv => sv.VarName,
          MaxonEnumVarRefOp ev => ev.VarName,
          MaxonAssignOp { IsDeclaration: true } av when av.VarName.StartsWith("__call_tmp_") => av.VarName,
          MaxonAssignOp { IsDeclaration: true } av when av.VarName.StartsWith("__lit_tmp_") => av.VarName,
          _ => null
        };
        if (backedByVar != null && _variables.ContainsKey(backedByVar))
          keepVars = [backedByVar];
      }
      _currentBlock!.AddOp(new MaxonScopeEndOp(GetScopeEndVars(), keepVars) { VarMetadata = _variables.GetScopeEndVarMetadata() });
      _currentBlock!.AddOp(new MaxonReturnOp(value));
    } else {
      _currentBlock!.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
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
        if (value is MaxonStruct aliasActual && IsStructTypeCompatible(aliasActual.TypeName, expectedStruct.Name)) {
          return;
        }
        // Allow returning concrete tuples when expected type has unresolved type parameters
        if (value is MaxonStruct && expectedStruct.IsTuple
            && expectedStruct.Fields.Any(f => f.Type is MlirTypeParameterType)) {
          return;
        }
        var actualName = value is MaxonStruct s ? s.TypeName : value.GetType().Name.Replace("Maxon", "").ToLower();
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot return '{actualName}' from function declared to return '{expectedStruct.Name}'",
          returnToken.Line, returnToken.Column);
      }
      return;
    }

    if (returnType is MlirUnionType expectedEnum) {
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
    // Float literals are F64; allow narrowing to F32 ranged types (range check validates)
    if (valueKind == MaxonValueKind.Float && expectedKind == MaxonValueKind.Float32)
      return;

    throw new CompileError(ErrorCode.SemanticTypeMismatch,
      $"Cannot return '{KindToTypeName(valueKind)}' from function declared to return '{KindToTypeName(expectedKind)}'",
      returnToken.Line, returnToken.Column);
  }

  private MaxonValue CheckReturnRange(MaxonValue value, Token returnToken) {
    if (_currentFunction?.ReturnType is not MlirRangedPrimitiveType rangedType)
      return value;

    if (rangedType.IsFullBaseRange)
      return value;

    var expectedKind = rangedType.BaseType.ToValueKind();
    return ValidateAndEmitRangeCheck(value, rangedType, expectedKind, returnToken);
  }

  private void ParsePanic() {
    var panicToken = Advance(); // consume 'panic'
    Expect(TokenType.LeftParen);
    var msgToken = Current();

    var sourceFileName = _sourceFilePath != null ? Path.GetFileName(_sourceFilePath) : "unknown";
    var prefix = $"panic at {sourceFileName}:{panicToken.Line}: ";

    if (msgToken.Type == TokenType.StringInterp) {
      Advance(); // consume interpolated string
      Expect(TokenType.RightParen);
      // Prepend location prefix and append newline to the interpolated string content
      var prefixedToken = new Token(TokenType.StringInterp, prefix + msgToken.Value + "\\n", msgToken.Line, msgToken.Column);
      var interpResult = EmitStringLiteralWithInterpolation(prefixedToken);
      _currentBlock!.AddOp(new MaxonPanicDynamicOp(interpResult));
    } else if (msgToken.Type == TokenType.StringLiteral) {
      Advance(); // consume string literal
      Expect(TokenType.RightParen);
      var message = prefix + msgToken.Value;
      _currentBlock!.AddOp(new MaxonPanicOp(message));
    } else {
      throw new CompileError(ErrorCode.SemanticTypeMismatch, "panic requires a string argument", msgToken.Line, msgToken.Column);
    }
  }

  private void ParseThrow() {
    var throwToken = Advance(); // consume 'throw'
    var expr = ParseExpression();
    var errorValue = ResolveExprValue(expr);
    if (errorValue is not MaxonEnum enumVal) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch, "throw requires an error enum value", throwToken.Line, throwToken.Column);
    }
    _currentBlock!.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock!.AddOp(new MaxonThrowOp(errorValue, enumVal.TypeName));
  }

  /// <summary>
  /// Parse a try statement (statement context, not expression).
  /// Forms: try call() otherwise ignore
  ///        try call() otherwise 'label' ... end 'label'
  /// </summary>
  private void ParseTryStatement() {
    var tryToken = Advance(); // consume 'try'
    _lastExprCallOp = null;
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

    TryResultInfo tryInfo;
    MlirType? calleeThrowsType = null;

    // Check if this is a try-await expression (the inner parsed an await op)
    var lastOp = _currentBlock!.Operations[^1];
    if (lastOp is MaxonAwaitOp awaitOp) {
      // try await <promise>
      var promise = awaitOp.Promise;
      if (promise is not MaxonPromise promiseVal) {
        throw new CompileError(ErrorCode.SemanticUnexpectedToken, "try await requires a promise value", tryToken.Line, tryToken.Column);
      }
      if (!promiseVal.Throws) {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          "'try await' requires a promise from an async throwing function",
          tryToken.Line, tryToken.Column);
      }

      // Replace the MaxonAwaitOp with a MaxonTryAwaitOp
      _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
      var tryAwaitOp = new MaxonTryAwaitOp(promise, promiseVal.InnerKind, promiseVal.InnerStructTypeName);
      _currentBlock!.AddOp(tryAwaitOp);
      _lastExprCallOp = null;

      tryInfo = new TryResultInfo(tryAwaitOp.ErrorFlag, tryAwaitOp.Result, tryAwaitOp.ResultKind, tryAwaitOp.ResultStructTypeName);

      // Void-returning async functions can't be used as values in assignments
      if (!isStatementContext && tryAwaitOp.Result == null) {
        throw new CompileError(ErrorCode.SemanticErrorTypeMismatch,
          "type mismatch: 'async function does not return a value'",
          tryToken.Line, tryToken.Column);
      }
    } else {
      // Standard try-call path
      // If the call returned a struct, EmitCallReturnTempAssign added a __call_tmp_ assign after it — remove that too.
      if (lastOp is MaxonAssignOp { IsDeclaration: true } tmpAssign && tmpAssign.VarName.StartsWith("__call_tmp_")) {
        _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
        _variables.Remove(tmpAssign.VarName);
        lastOp = _currentBlock!.Operations[^1];
      }
      if (lastOp is not MaxonCallOp foundCallOp) {
        throw new CompileError(ErrorCode.SemanticUnexpectedToken, "try requires a function call or await expression", tryToken.Line, tryToken.Column);
      }
      var callOp = foundCallOp;

      // Look up the callee to check it actually throws
      var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == callOp.Callee);
      if (callee != null && callee.ThrowsType == null) {
        throw new CompileError(ErrorCode.SemanticTryRequiresThrowingFunction,
          $"try requires a throwing function: ''{callOp.Callee}' does not throw'",
          tryToken.Line, tryToken.Column);
      }
      calleeThrowsType = callee?.ThrowsType;

      // Replace the MaxonCallOp with a MaxonTryCallOp
      _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
      var tryCallOp = new MaxonTryCallOp(callOp.Callee, callOp.Args, callOp.ResultKind, callOp.ResultStructTypeName) {
        ArgMutabilities = callOp.ArgMutabilities,
        ArgVarNames = callOp.ArgVarNames,
        CallLine = callOp.CallLine,
        CallColumn = callOp.CallColumn
      };
      _currentBlock!.AddOp(tryCallOp);
      _lastExprCallOp = tryCallOp;

      tryInfo = new TryResultInfo(tryCallOp.ErrorFlag, tryCallOp.Result, tryCallOp.ResultKind, tryCallOp.ResultStructTypeName);

      // Void-returning functions can't be used as values in assignments
      if (!isStatementContext && tryCallOp.Result == null) {
        throw new CompileError(ErrorCode.SemanticErrorTypeMismatch,
          $"type mismatch: ''{callOp.Callee}' does not return a value'",
          tryToken.Line, tryToken.Column);
      }
    }

    // Check for 'otherwise' clause
    if (!Check(TokenType.Otherwise)) {
      // Propagation form: try func() - propagates error to caller
      if (_currentFunction?.ThrowsType == null) {
        throw new CompileError(ErrorCode.SemanticUnexpectedToken,
          "try without otherwise requires the enclosing function to have 'throws'",
          tryToken.Line, tryToken.Column);
      }
      return EmitTryPropagate(tryInfo);
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
      return new ExprResult.Direct(tryInfo.ErrorFlag);
    }

    // Panic form: otherwise panic("message") — unconditionally panics on error
    if (Check(TokenType.Panic)) {
      return EmitTryOtherwisePanic(tryInfo);
    }

    // Check for block handler form: otherwise 'label' ... end 'label'
    if (Check(TokenType.CharacterLiteral)) {
      return EmitTryOtherwiseBlock(tryInfo, null, calleeThrowsType);
    }

    // Check for '(e)' error binding before block label
    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      var errorBindingToken = Expect(TokenType.Identifier);
      Expect(TokenType.RightParen);
      return EmitTryOtherwiseBlock(tryInfo, errorBindingToken, calleeThrowsType);
    }

    // Default value form: otherwise <expression>
    return EmitTryOtherwiseDefault(tryInfo, tryToken);
  }

  /// <summary>
  /// Stores try call error flag and result to mutable variables for cross-block access.
  /// Returns (errorFlagVar, resultVar) names.
  /// </summary>
  private (string errorFlagVar, string? resultVar) StoreTryValuesForCrossBlockAccess(TryResultInfo tryInfo) {
    var errorFlagVar = $"__try_error_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(errorFlagVar, tryInfo.ErrorFlag, true, true, MaxonValueKind.Integer));
    _variables.Declare(errorFlagVar, MaxonValueKind.Integer, true, tryInfo.ErrorFlag, _currentBlock!);

    string? resultVar = null;
    if (tryInfo.Result != null) {
      resultVar = $"__try_result_{_blockCounter++}";
      var resultKind = tryInfo.ResultKind ?? MaxonValueKind.Integer;
      _currentBlock!.AddOp(new MaxonAssignOp(resultVar, tryInfo.Result, true, true, resultKind));
      _variables.Declare(resultVar, resultKind, true, tryInfo.Result, _currentBlock!, OwnershipFlags.IsTemp);
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
      var enumType = (MlirUnionType)_typeRegistry[structTypeName!];
      var backingKind = GetUnionBackingKind(enumType);
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
  private ExprResult.Direct EmitTryContinueBlock(string continueBlockLabel, string? resultVar, TryResultInfo tryInfo) {
    var contBlock = _currentFunction!.Body.AddBlock(continueBlockLabel);
    _currentBlock = contBlock;

    if (resultVar != null) {
      var resultKind = tryInfo.ResultKind ?? MaxonValueKind.Integer;
      var structTypeName = tryInfo.ResultStructTypeName;


      var loadedValue = EmitVarRefOp(resultVar, resultKind, structTypeName);
      return new ExprResult.Direct(loadedValue);
    }
    return new ExprResult.Direct(tryInfo.ErrorFlag);
  }

  private ExprResult.Direct EmitTryPropagate(TryResultInfo tryInfo) {
    var propagateErrorBlock = UniqueLabel("propagate_error");
    var continueBlock = UniqueLabel("try_continue");

    var (errorFlagVar, resultVar) = StoreTryValuesForCrossBlockAccess(tryInfo);
    EmitErrorFlagCheck(tryInfo.ErrorFlag, propagateErrorBlock, continueBlock);

    // Propagation block: re-throw the error
    var propBlock = _currentFunction!.Body.AddBlock(propagateErrorBlock);
    _currentBlock = propBlock;
    var loadErrorOp = new MaxonVarRefOp(errorFlagVar, MaxonValueKind.Integer);
    _currentBlock!.AddOp(loadErrorOp);
    _currentBlock!.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock!.AddOp(new MaxonReturnOp(loadErrorOp.Result, isErrorPropagation: true));

    return EmitTryContinueBlock(continueBlock, resultVar, tryInfo);
  }

  private ExprResult.Direct EmitTryOtherwiseBlock(TryResultInfo tryInfo, Token? errorBindingToken, MlirType? errorType = null) {
    var labelToken = Expect(TokenType.CharacterLiteral);
    var blockLabel = labelToken.Value;

    var errorBlock = UniqueLabel("otherwise_error");
    var continueBlock = UniqueLabel("otherwise_continue");

    var (errorFlagVar, resultVar) = StoreTryValuesForCrossBlockAccess(tryInfo);
    EmitErrorFlagCheck(tryInfo.ErrorFlag, errorBlock, continueBlock);

    // Error handling block
    var errBlock = _currentFunction!.Body.AddBlock(errorBlock);
    _currentBlock = errBlock;

    if (errorBindingToken != null) {
      EmitErrorBinding(errorBindingToken.Value, errorFlagVar, errorType);
    }

    ExpectNewline();
    ParseBodyUntilEnd();
    if (!BlockEndsWithTerminator(errBlock)) {
      _currentBlock!.AddOp(new MaxonBrOp(continueBlock));
    }

    ExpectEndLabel(blockLabel);

    return EmitTryContinueBlock(continueBlock, resultVar, tryInfo);
  }

  /// <summary>
  /// Emits an otherwise panic form: try func() otherwise panic("message").
  /// On error, panics immediately. On success, returns the result.
  /// </summary>
  private ExprResult.Direct EmitTryOtherwisePanic(TryResultInfo tryInfo) {
    var errorBlock = UniqueLabel("otherwise_panic");
    var continueBlock = UniqueLabel("otherwise_continue");

    var (_, resultVar) = StoreTryValuesForCrossBlockAccess(tryInfo);
    EmitErrorFlagCheck(tryInfo.ErrorFlag, errorBlock, continueBlock);

    // Error block: emit panic (which is a terminator — never returns)
    var errBlock = _currentFunction!.Body.AddBlock(errorBlock);
    _currentBlock = errBlock;
    ParsePanic();

    return EmitTryContinueBlock(continueBlock, resultVar, tryInfo);
  }

  /// <summary>
  /// Emits a typed error binding variable in an error handler block.
  /// When the error type is a known enum, produces a typed enum value for match support.
  /// </summary>
  private void EmitErrorBinding(string bindingName, string errorFlagVar, MlirType? errorType) {
    var loadErrorOp = new MaxonVarRefOp(errorFlagVar, MaxonValueKind.Integer);
    _currentBlock!.AddOp(loadErrorOp);

    if (errorType is MlirUnionType enumType) {
      var backingKind = GetUnionBackingKind(enumType);
      var toEnumOp = new MaxonErrorFlagToEnumOp(loadErrorOp.Result, enumType.Name, backingKind, enumType.HasAssociatedValues);
      _currentBlock!.AddOp(toEnumOp);
      _currentBlock!.AddOp(new MaxonAssignOp(bindingName, toEnumOp.Result, isDeclaration: true, isMutable: false, MaxonValueKind.Enum));
      _variables.Declare(bindingName, MaxonValueKind.Enum, false, toEnumOp.Result, _currentBlock!, structTypeName: enumType.Name);
    } else {
      _variables.Declare(bindingName, MaxonValueKind.Integer, false, loadErrorOp.Result, _currentBlock!);
    }
  }

  private ExprResult.Direct EmitTryOtherwiseDefault(TryResultInfo tryInfo, Token tryToken) {
    var defaultExpr = ParseExpression();
    var defaultValue = ResolveExprValue(defaultExpr);

    var resultVarName = $"__try_result_{_blockCounter++}";
    var defaultKind = DetermineValueKind(defaultValue);
    var expectedKind = tryInfo.ResultKind ?? MaxonValueKind.Integer;

    // Coerce integer-backed constants to their backing type when the expected type is numeric.
    // Don't coerce when the try call itself returns an enum (e.g. fromRawValue), so the otherwise
    // default stays as an enum value and the types remain compatible.
    if (defaultKind == MaxonValueKind.Enum && expectedKind != MaxonValueKind.Enum
        && TryCoerceConstantsToBackingType(defaultValue, out var defaultCoerced, out var defaultBackingKind)) {
      defaultValue = defaultCoerced!;
      defaultKind = defaultBackingKind;
    }

    // Type-check: otherwise expression must match the success type
    // Allow numeric coercions (int literal to byte/short) and generic type parameters
    if (defaultKind != expectedKind
        && expectedKind != MaxonValueKind.TypeParameter
        && !(IsNumericKind(defaultKind) && IsNumericKind(expectedKind))) {
      var defaultTypeName = defaultValue is MaxonStruct ds ? ds.TypeName : KindToTypeName(defaultKind);
      var expectedTypeName = tryInfo.ResultStructTypeName ?? KindToTypeName(expectedKind);
      throw new CompileError(ErrorCode.SemanticErrorTypeMismatch,
        $"type mismatch: 'otherwise type '{defaultTypeName}' does not match expected type '{expectedTypeName}''",
        tryToken.Line, tryToken.Column);
    }

    var resultKind = defaultKind;
    var structTypeName = tryInfo.ResultStructTypeName
      ?? (defaultValue is MaxonStruct s ? s.TypeName : null)
      ?? (defaultValue is MaxonEnum e ? e.TypeName : null);

    bool isStructResult = resultKind == MaxonValueKind.Struct && structTypeName != null;

    if (isStructResult) {
      // For struct results: lazy evaluation — default is only created on error path,
      // try result is only stored on success path. This avoids incref'ing the invalid
      // error code on the error path and avoids allocating the default on the success path.
      return EmitTryOtherwiseDefaultStruct(tryInfo, resultVarName, defaultValue, resultKind, structTypeName!);
    }

    // For non-struct results: the original eager pattern is fine (no refcounting involved)
    var defaultVarName = $"__try_default_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(defaultVarName, defaultValue, true, true, resultKind));
    _variables.Declare(defaultVarName, resultKind, true, defaultValue, _currentBlock!, structTypeName: structTypeName);

    if (tryInfo.Result != null) {
      _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, tryInfo.Result, true, true, resultKind));
      _variables.Declare(resultVarName, resultKind, true, tryInfo.Result, _currentBlock!, OwnershipFlags.IsTemp, structTypeName: structTypeName);
    }

    var errorBlock = UniqueLabel("otherwise_default_error");
    var continueBlock = UniqueLabel("otherwise_default_continue");

    EmitErrorFlagCheck(tryInfo.ErrorFlag, errorBlock, continueBlock);

    // Error block: adopt default value as the result
    var errBlock = _currentFunction!.Body.AddBlock(errorBlock);
    _currentBlock = errBlock;
    var loadedDefault = EmitVarRefOp(defaultVarName, resultKind, structTypeName);
    _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, loadedDefault, false, true, resultKind));
    _currentBlock!.AddOp(new MaxonBrOp(continueBlock));

    // Continue block: load result
    var contBlock = _currentFunction!.Body.AddBlock(continueBlock);
    _currentBlock = contBlock;

    var loadedResult = EmitVarRefOp(resultVarName, resultKind, structTypeName);
    return new ExprResult.Direct(loadedResult);
  }

  /// <summary>
  /// Emit try/otherwise default for struct-typed results. Uses lazy evaluation:
  /// the default is only materialized on the error path, and the try result is only
  /// stored on the success path. This prevents incref'ing the invalid error code and
  /// avoids allocating unused defaults.
  /// </summary>
  private ExprResult.Direct EmitTryOtherwiseDefaultStruct(
    TryResultInfo tryInfo, string resultVarName, MaxonValue defaultValue,
    MaxonValueKind resultKind, string structTypeName) {

    // Move the default expression ops from the current block to the error block.
    // The default expression was parsed into _currentBlock — we need to extract those ops.
    // Strategy: save the ops added by ParseExpression, remove them from entry block,
    // and re-add them to the error block.

    // Find which ops were added by the default expression parsing. We know the try call/await op
    // was the last op before ParseExpression started. Look for ops after the try call/await.
    var entryBlock = _currentBlock!;
    var tryOpIndex = -1;
    for (int i = entryBlock.Operations.Count - 1; i >= 0; i--) {
      if (entryBlock.Operations[i] is MaxonTryCallOp or MaxonTryAwaitOp) {
        tryOpIndex = i;
        break;
      }
    }

    // Extract default expression ops (everything after the try call/await)
    var defaultOps = new List<MaxonOp>();
    if (tryOpIndex >= 0) {
      for (int i = tryOpIndex + 1; i < entryBlock.Operations.Count; i++) {
        defaultOps.Add((MaxonOp)entryBlock.Operations[i]);
      }
      entryBlock.Operations.RemoveRange(tryOpIndex + 1, defaultOps.Count);
    }

    var errorBlockLabel = UniqueLabel("otherwise_default_error");
    var successBlockLabel = UniqueLabel("otherwise_default_success");
    var continueBlock = UniqueLabel("otherwise_default_continue");

    // Branch: error → error block, success → success block
    EmitErrorFlagCheck(tryInfo.ErrorFlag, errorBlockLabel, successBlockLabel);

    // Error block: replay default expression ops, then assign to result var
    var errBlock = _currentFunction!.Body.AddBlock(errorBlockLabel);
    _currentBlock = errBlock;
    foreach (var op in defaultOps) {
      _currentBlock!.AddOp(op);
    }
    _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, defaultValue, true, true, resultKind));
    _currentBlock!.AddOp(new MaxonBrOp(continueBlock));

    // Success block: assign try result to result var
    var sucBlock = _currentFunction!.Body.AddBlock(successBlockLabel);
    _currentBlock = sucBlock;
    if (tryInfo.Result != null) {
      _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, tryInfo.Result, true, true, resultKind));
    }
    _currentBlock!.AddOp(new MaxonBrOp(continueBlock));

    // Register variable from entry scope for later lookups
    _variables.Declare(resultVarName, resultKind, true, tryInfo.Result ?? defaultValue, entryBlock, OwnershipFlags.IsTemp, structTypeName: structTypeName);

    // Continue block: load result
    var contBlock = _currentFunction!.Body.AddBlock(continueBlock);
    _currentBlock = contBlock;

    var loadedResult = EmitVarRefOp(resultVarName, resultKind, structTypeName);
    return new ExprResult.Direct(loadedResult);
  }

  private void ParseAnnotatedDecl() {
    var atToken = Advance(); // consume '@'
    var annotation = Expect(TokenType.Identifier);
    if (annotation.Value != "heap")
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Unknown annotation '@{annotation.Value}' (expected '@heap')", atToken.Line, atToken.Column);
    if (Check(TokenType.Var))
      ParseVarOrLetDecl(isMutable: true, forceHeap: true);
    else if (Check(TokenType.Let))
      ParseVarOrLetDecl(isMutable: false, forceHeap: true);
    else
      throw new CompileError(ErrorCode.ParserUnexpectedToken, "@heap must be followed by 'var' or 'let'", atToken.Line, atToken.Column);
  }

  private void ParseVarDecl() {
    ParseVarOrLetDecl(isMutable: true);
  }

  private void ParseLetDecl() {
    ParseVarOrLetDecl(isMutable: false);
  }

  private void ParseVarOrLetDecl(bool isMutable, bool forceHeap = false) {
    Advance(); // consume 'var' or 'let'
    _lastRangedTypeName = null;

    // Tuple destructuring: var (x, y) = expr
    if (Check(TokenType.LeftParen)) {
      ParseTupleDestructuring(isMutable);
      return;
    }

    var nameToken = Expect(TokenType.Identifier);
    var name = nameToken.Value;
    var isDiscard = name == "_";
    if (isDiscard) {
      name = $"__discard_{_discardCounter++}";
    }
    if (!isDiscard) {
      _localVarLocations.Add((name, nameToken.Line, nameToken.Column));
    }
    Expect(TokenType.Equals);

    _lastExprCallOp = null;
    var initExpr = ParseExpression();
    var initValue = ResolveExprValue(initExpr) ?? throw new InvalidOperationException($"Compiler bug: Variable '{name}' initialization expression did not produce a value (this should not happen - please report this bug)");

    // Mark the underlying call as let-discarded for purity checking
    if (isDiscard) {
      var lastOp = _currentBlock!.Operations.Count > 0 ? _currentBlock!.Operations[^1] : null;
      var hasCall = lastOp is MaxonCallOp || _lastExprCallOp is MaxonTryCallOp;
      // EmitCallReturnTempAssign adds a __call_tmp_ assign after the call op for struct returns
      var hasCallTmp = lastOp is MaxonAssignOp { VarName: var tmpName } && tmpName.StartsWith("__call_tmp_")
        && _currentBlock!.Operations.Count > 1 && _currentBlock!.Operations[^2] is MaxonCallOp;
      if (!hasCall && !hasCallTmp) {
        throw new CompileError(ErrorCode.SemanticSelfAssignment,
          "expected a function call",
          nameToken.Line, nameToken.Column);
      }
      MarkLetDiscardResult(nameToken);
    }

    if (forceHeap && initValue is not MaxonStruct)
      throw new CompileError(ErrorCode.ParserUnexpectedToken, "@heap can only be used with struct literals", nameToken.Line, nameToken.Column);

    if (initValue is MaxonStruct structVal) {
      var kind = MaxonValueKind.Struct;
      var assignOp = new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind);
      if (forceHeap) assignOp.ForceHeap = true;
      _currentBlock!.AddOp(assignOp);
      _variables.Declare(name, kind, isMutable, initValue, _currentBlock!, structTypeName: structVal.TypeName);
      // Fix temp ownership AFTER adding named var so __try_result_ ends up after it in the dict.
      FixupTempOwnership();
    } else if (initValue is MaxonEnum enumVal) {
      var kind = MaxonValueKind.Enum;
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables.Declare(name, kind, isMutable, initValue, _currentBlock!, structTypeName: enumVal.TypeName);
      FixupTempOwnership();
    } else if (initValue is MaxonFunctionPtr) {
      var kind = MaxonValueKind.Function;
      var fnType = GetFunctionTypeFromLastOp();
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables.Declare(name, kind, isMutable, initValue, _currentBlock!, fnTypeName: fnType);
    } else {
      var kind = DetermineValueKind(initValue);
      var rangedTypeName = _lastRangedTypeName;
      _lastRangedTypeName = null;
      // Preserve TypeParameter kind so monomorphization can resolve the concrete type
      if (kind == MaxonValueKind.Integer) {
        var lastOp = _currentBlock!.Operations.LastOrDefault();
        if (lastOp is MaxonVarRefOp { ValueKind: MaxonValueKind.TypeParameter } varRefOp
            && varRefOp.Result == initValue) {
          kind = MaxonValueKind.TypeParameter;
        } else if (lastOp is MaxonCallOp { ResultKind: MaxonValueKind.TypeParameter } callOp
            && callOp.Result == initValue) {
          kind = MaxonValueKind.TypeParameter;
        } else if (lastOp is MaxonManagedMemGetOp { ResultKind: MaxonValueKind.TypeParameter } memGetOp
            && memGetOp.Result == initValue) {
          kind = MaxonValueKind.TypeParameter;
        }
      }
      // Use Float32 kind when assigning to an F32-backed ranged type
      if (kind == MaxonValueKind.Float && rangedTypeName != null
          && _typeRegistry.TryGetValue(rangedTypeName, out var rt)
          && rt is MlirRangedPrimitiveType rpt && rpt.OptimalType == MlirType.F32) {
        kind = MaxonValueKind.Float32;
      }
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables.Declare(name, kind, isMutable, initValue, _currentBlock!, structTypeName: rangedTypeName);
    }
  }

  private void ParseTupleDestructuring(bool isMutable) {
    var parenToken = Advance(); // consume '('
    var names = new List<string>();
    do {
      var nameToken = Expect(TokenType.Identifier);
      names.Add(nameToken.Value);
      if (nameToken.Value != "_") {
        _localVarLocations.Add((nameToken.Value, nameToken.Line, nameToken.Column));
      }
      if (!Check(TokenType.Comma)) break;
      Advance();
    } while (true);
    Expect(TokenType.RightParen);
    Expect(TokenType.Equals);

    var (tempName, tupleType) = ParseTupleRhs(parenToken, names, "Tuple destructuring");
    EmitTupleFieldBindings(tempName, tupleType, names, isMutable);
  }

  // Each slot in a tuple assignment can be:
  //   - a plain identifier: assign to existing var
  //   - `var name`: declare a new mutable var
  //   - `let name`: declare a new immutable var
  //   - `_`: discard
  private record TupleSlot(Token NameToken, bool IsDeclaration, bool IsMutable);

  private void ParseTupleAssignment() {
    var parenToken = Advance(); // consume '('
    var slots = new List<TupleSlot>();
    do {
      bool isDecl = false;
      bool isMutable = false;
      if (Check(TokenType.Var)) { Advance(); isDecl = true; isMutable = true; } else if (Check(TokenType.Let)) { Advance(); isDecl = true; isMutable = false; }
      var nameToken = Expect(TokenType.Identifier);
      slots.Add(new TupleSlot(nameToken, isDecl, isMutable));
      if (!Check(TokenType.Comma)) break;
      Advance();
    } while (true);
    Expect(TokenType.RightParen);
    Expect(TokenType.Equals);

    var names = slots.Select(s => s.NameToken.Value).ToList();
    var (tempName, tupleType) = ParseTupleRhs(parenToken, names, "Tuple assignment");
    EmitTupleFieldAssignments(tempName, tupleType, slots);
  }

  // Parses and validates the RHS of a tuple binding (destructuring or assignment).
  // Evaluates the expression, confirms it's a tuple of the right arity, stores it
  // in a temp slot, and transfers ownership. Returns (tempName, tupleType).
  private (string tempName, MlirStructType tupleType) ParseTupleRhs(Token parenToken, List<string> names, string context) {
    _lastExprCallOp = null;
    var initExpr = ParseExpression();
    var initValue = ResolveExprValue(initExpr)
      ?? throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"{context} requires a value", parenToken.Line, parenToken.Column);

    if (initValue is not MaxonStruct structVal)
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"{context} requires a tuple value", parenToken.Line, parenToken.Column);

    if (!_typeRegistry.TryGetValue(structVal.TypeName, out var regType) || regType is not MlirStructType tupleType || !tupleType.IsTuple)
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"{context} requires a tuple value", parenToken.Line, parenToken.Column);

    if (names.Count != tupleType.Fields.Count)
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Tuple has {tupleType.Fields.Count} elements but destructuring has {names.Count} bindings",
        parenToken.Line, parenToken.Column);

    // If all tuple elements are discarded, mark the underlying call for purity checking
    if (names.All(n => n == "_")) {
      MarkLetDiscardResult(parenToken);
    }

    // Store the tuple in a temp variable and transfer ownership from the call temp
    var tempName = $"__destruct_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(tempName, initValue, true, false, MaxonValueKind.Struct));
    _variables.Declare(tempName, MaxonValueKind.Struct, false, initValue, _currentBlock!, OwnershipFlags.IsTemp | OwnershipFlags.CallReturn, structTypeName: tupleType.Name);
    FixupTempOwnership();

    return (tempName, tupleType);
  }

  // Emits new variable declarations for each tuple field (used by destructuring).
  private void EmitTupleFieldBindings(string sourceName, MlirStructType tupleType, List<string> names, bool isMutable) {
    var slots = names.Select(n => new TupleSlot(new Token(TokenType.Identifier, n, 0, 0), IsDeclaration: true, IsMutable: isMutable)).ToList();
    EmitTupleFieldAssignments(sourceName, tupleType, slots);
  }

  // Emits a declaration or assignment for each tuple field.
  // Slots with IsDeclaration=true declare a new variable; others assign to an existing one.
  private void EmitTupleFieldAssignments(string sourceName, MlirStructType tupleType, List<TupleSlot> slots) {
    for (int i = 0; i < slots.Count; i++) {
      var slot = slots[i];
      var nameToken = slot.NameToken;
      var name = nameToken.Value;
      if (name == "_") continue;

      var field = tupleType.Fields[i];
      var fieldKind = field.Type.ToValueKind();
      string? fieldStructName = field.Type is MlirStructType fst ? fst.Name
        : field.Type is MlirUnionType fet ? fet.Name : null;

      var refOp = new MaxonStructVarRefOp(sourceName, tupleType.Name);
      _currentBlock!.AddOp(refOp);
      var accessOp = new MaxonFieldAccessOp(refOp.Result, tupleType.Name, field.Name, fieldKind, fieldStructName);
      _currentBlock!.AddOp(accessOp);

      if (slot.IsDeclaration) {
        _currentBlock!.AddOp(new MaxonAssignOp(name, accessOp.Result, isDeclaration: true, isMutable: slot.IsMutable, fieldKind));
        _variables.Declare(name, fieldKind, slot.IsMutable, accessOp.Result, _currentBlock!, structTypeName: fieldStructName);
        if (nameToken.Line > 0) _localVarLocations.Add((name, nameToken.Line, nameToken.Column));
      } else {
        var resolved = ResolveVariable(name)
          ?? throw CreateUndefinedVariableError(name, nameToken, ErrorCode.ParserExpectedExpression);

        if (resolved is not ResolvedVar.Local(var varInfo))
          throw new CompileError(ErrorCode.ParserImmutableVariable,
            $"cannot assign to global variable '{name}' via tuple assignment",
            nameToken.Line, nameToken.Column);

        if (!varInfo.Mutable)
          throw new CompileError(ErrorCode.ParserImmutableVariable,
            $"cannot assign to immutable variable: '{name}'",
            nameToken.Line, nameToken.Column);

        _currentBlock!.AddOp(new MaxonAssignOp(name, accessOp.Result, isDeclaration: false, isMutable: true, fieldKind));
        _variables[name] = new VarInfo(name, fieldKind, true, accessOp.Result, _currentBlock!, varInfo.Flags, fieldStructName, PayloadBinding: varInfo.PayloadBinding);
      }
    }
  }

  private MlirFunctionType GetFunctionTypeFromLastOp() {
    var lastOp = _currentBlock!.Operations.LastOrDefault();
    return lastOp switch {
      MaxonFunctionRefOp fnRefOp => fnRefOp.FunctionType,
      MaxonClosureCreateOp closureOp => closureOp.FunctionType,
      MaxonFunctionParamOp fnParamOp => fnParamOp.FunctionType,
      MaxonFunctionVarRefOp fnVarRefOp => fnVarRefOp.FunctionType,
      _ => throw new InvalidOperationException($"Cannot determine function type from {lastOp?.GetType().Name}")
    };
  }

  private void ParseAssignment() {
    var nameToken = Advance(); // consume identifier
    var name = nameToken.Value;

    // Field assignment: name.field[.field...] = expr
    if (Check(TokenType.Dot)) {
      Expect(TokenType.Dot);
      var fieldToken = ExpectFieldName();
      var (currentValue, currentStructTypeName) = ResolveStructVariable(name, nameToken, requireMutable: true);

      while (Check(TokenType.Dot)) {
        (currentValue, currentStructTypeName) = EmitIntermediateFieldAccess(currentValue, currentStructTypeName, fieldToken);
        Advance(); // consume '.'
        fieldToken = ExpectFieldName();
      }

      Expect(TokenType.Equals);
      EmitFieldAssignment(currentStructTypeName, currentValue, fieldToken, nameToken, name);
      return;
    }

    // Simple assignment: name = expr
    Expect(TokenType.Equals);

    var resolved = ResolveVariable(name)
      ?? throw CreateUndefinedVariableError(name, nameToken, ErrorCode.ParserExpectedExpression);

    if (!resolved.IsMutable) {
      throw new CompileError(ErrorCode.ParserImmutableVariable,
        $"cannot assign to immutable variable: '{name}'",
        nameToken.Line, nameToken.Column);
    }

    var newVal = ResolveExprValue(ParseExpression());
    if (_lastExprVarName == name) {
      throw new CompileError(ErrorCode.SemanticSelfAssignment,
        $"self-assignment has no effect: '{name} = {name}'",
        nameToken.Line, nameToken.Column);
    }

    if (resolved is ResolvedVar.Global(var globalInfo)) {
      _currentBlock!.AddOp(new MaxonGlobalStoreOp(name, newVal, globalInfo.Kind));
    } else {
      var varInfo = ((ResolvedVar.Local)resolved).Info;
      var fnType = varInfo.Kind == MaxonValueKind.Function ? GetFunctionTypeFromLastOp() : null;
      _currentBlock!.AddOp(new MaxonAssignOp(name, newVal, isDeclaration: false, isMutable: true, varInfo.Kind));
      FixupTempOwnership();
      // Write back to union heap block when assigning to a mutable payload binding
      if (varInfo.PayloadBinding is { } pb) {
        _currentBlock!.AddOp(new MaxonEnumPayloadAssignOp(pb.EnumVarName, pb.EnumTypeName, pb.PayloadIndex, newVal));
      }
      _variables[name] = fnType != null
        ? new VarInfo(name, varInfo.Kind, true, newVal, _currentBlock!, varInfo.Flags, FnTypeName: fnType)
        : new VarInfo(name, varInfo.Kind, true, newVal, _currentBlock!, varInfo.Flags, varInfo.StructTypeName, PayloadBinding: varInfo.PayloadBinding);
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

  private Token ExpectFieldName() {
    if (Check(TokenType.IntegerLiteral)) {
      var tok = Advance();
      var index = ParseIntegerLiteral(tok);
      return new Token(TokenType.Identifier, $"_{index}", tok.Line, tok.Column);
    }
    return Expect(TokenType.Identifier);
  }

  private void EmitFieldAssignment(string structTypeName, MaxonValue structVal, Token fieldToken, Token errorToken, string? lhsVarName = null) {
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

    if (lhsVarName != null && IsFieldSelfAssignment(lhsVarName, fieldToken.Value)) {
      throw new CompileError(ErrorCode.SemanticSelfAssignment,
        $"self-assignment has no effect: '{lhsVarName}.{fieldToken.Value} = {lhsVarName}.{fieldToken.Value}'",
        errorToken.Line, errorToken.Column);
    }

    _currentBlock!.AddOp(new MaxonFieldAssignOp(structVal, structTypeName, fieldToken.Value, newValue));
  }

  // Checks if the last op emitted is a field access on the same variable and field as the LHS
  private bool IsFieldSelfAssignment(string varName, string fieldName) {
    var ops = _currentBlock!.Operations;
    if (ops.Count < 2) return false;
    if (ops[^1] is not MaxonFieldAccessOp fieldAccess) return false;
    if (fieldAccess.FieldName != fieldName) return false;

    // Struct VarRefs always emit a MaxonStructVarRefOp before the field access
    return ops[^2] is MaxonStructVarRefOp structVarRef && structVarRef.VarName == varName;
  }

  private void ParseCallStatement() {
    var token = Advance(); // consume identifier

    var siblingCall = TrySiblingMethodCall(token);
    if (siblingCall != null) {
      MarkDiscardedResult(siblingCall, token);
      return;
    }

    Advance(); // consume '('
    var (args, callee) = ParseCallArgs(token);
    var callOp = CreateFunctionCall(token, args, callee);
    MarkDiscardedResult(callOp, token);
  }

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
    ["writeStdout"] = new(
      "Writes a __ManagedMemory buffer to stdout.\n\n`__Builtins.writeStdout(managed) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedWriteStdoutOp(managed);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["writeStderr"] = new(
      "Writes a __ManagedMemory buffer to stderr.\n\n`__Builtins.writeStderr(managed) returns int`",
      p => {
        var managed = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonManagedWriteStderrOp(managed);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    // === Command Line intrinsics ===
    ["commandLineCount"] = RuntimeCallIntrinsic(
      "Returns the number of command line arguments.\n\n`__Builtins.commandLineCount() returns int`",
      "maxon_command_line_count", 0, true),
    ["commandLineArg"] = RuntimeCallToManaged(
      "Returns a __ManagedMemory for the command line argument at the given index.\n\n`__Builtins.commandLineArg(index) returns __ManagedMemory`",
      "maxon_command_line_arg", 1, freeCString: true),
    // === Process intrinsics ===
    ["processCreate"] = TwoManagedToCStringRuntimeCall(
      "Creates a process. Returns handle.\n\n`__Builtins.processCreate(cmd_managed, cwd_managed) returns int`",
      "maxon_process_create"),
    ["processWait"] = RuntimeCallIntrinsic(
      "Waits for process. Returns 0=completed, 1=timeout, -1=error.\n\n`__Builtins.processWait(handle, timeoutMs) returns int`",
      "maxon_process_wait", 2, true),
    ["processGetExitCode"] = RuntimeCallIntrinsic(
      "Gets exit code of completed process.\n\n`__Builtins.processGetExitCode(handle) returns int`",
      "maxon_process_get_exit_code", 1, true),
    ["processClose"] = RuntimeCallIntrinsic(
      "Closes a process handle.\n\n`__Builtins.processClose(handle)`",
      "maxon_process_close", 1, false),
    ["processCreateWithCapture"] = TwoManagedToCStringRuntimeCall(
      "Creates a process with stdout/stderr capture. Returns capture struct pointer.\n\n`__Builtins.processCreateWithCapture(cmd_managed, cwd_managed) returns int`",
      "maxon_process_create_with_capture"),
    ["processGetHandle"] = RuntimeCallIntrinsic(
      "Gets hProcess from capture struct.\n\n`__Builtins.processGetHandle(capture_ptr) returns int`",
      "maxon_process_get_handle", 1, true),
    ["processCloseCapture"] = RuntimeCallIntrinsic(
      "Closes the process handle and frees the capture struct.\n\n`__Builtins.processCloseCapture(capture_ptr)`",
      "maxon_process_close_capture", 1, false),
    ["processReadStdout"] = RuntimeCallToManaged(
      "Reads stdout from capture struct. Returns __ManagedMemory.\n\n`__Builtins.processReadStdout(capture_ptr) returns __ManagedMemory`",
      "maxon_process_read_stdout", 1, freeCString: true),
    ["processReadStderr"] = RuntimeCallToManaged(
      "Reads stderr from capture struct. Returns __ManagedMemory.\n\n`__Builtins.processReadStderr(capture_ptr) returns __ManagedMemory`",
      "maxon_process_read_stderr", 1, freeCString: true),
    // === Sleep intrinsic ===
    ["sleep"] = RuntimeCallIntrinsic(
      "Suspends the current green thread for the given milliseconds.\n\n`__Builtins.sleep(ms)`",
      "maxon_sleep", 1, false),
    // === Time intrinsics ===
    ["currentTimeMs"] = RuntimeCallIntrinsic(
      "Returns monotonic time in milliseconds.\n\n`__Builtins.currentTimeMs() returns int`",
      "maxon_current_time_ms", 0, true),
    // === Primitive type intrinsics ===
    ["floatToBits"] = new(
      "Reinterprets a float's IEEE 754 bit pattern as an integer (bitcast).\n\n`__Builtins.floatToBits(value) returns int`",
      p => {
        var value = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonBitcastF64ToI64Op(value);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    // === UCD (Unicode Character Database) intrinsics ===
    ["ucdByteAt"] = new(
      "Loads a byte from a .ucd section blob.\n\n`__Builtins.ucdByteAt(\"label\", offset) returns int`",
      p => {
        var labelToken = p.Expect(TokenType.StringLiteral);
        var label = labelToken.Value;
        p.Expect(TokenType.Comma);
        var offset = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonUcdByteLoadOp(label, offset);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
    ["ucdI64At"] = new(
      "Loads a 64-bit int from a .ucd section blob.\n\n`__Builtins.ucdI64At(\"label\", index) returns int`",
      p => {
        var labelToken = p.Expect(TokenType.StringLiteral);
        var label = labelToken.Value;
        p.Expect(TokenType.Comma);
        var index = p.ResolveExprValue(p.ParseExpression());
        p.Expect(TokenType.RightParen);
        var op = new MaxonUcdI64LoadOp(label, index);
        p._currentBlock!.AddOp(op);
        return op.Result;
      }),
  };

  private static readonly Dictionary<string, (MlirType Type, MaxonValueKind Kind)> PrimitiveTypes = new() {
    ["int"] = (MlirType.I64, MaxonValueKind.Integer),

    ["float"] = (MlirType.F64, MaxonValueKind.Float),
    ["bool"] = (MlirType.I1, MaxonValueKind.Bool),
    ["byte"] = (MlirType.I8, MaxonValueKind.Byte),
  };

  private static MlirType? TryGetPrimitiveMlirType(string typeName) =>
    PrimitiveTypes.TryGetValue(typeName, out var info) ? info.Type : null;


  /// <summary>
  /// Get the element type from the current struct's type parameters (e.g., Element for Array/Vector)
  /// </summary>
  private MaxonValueKind GetElementKind() {
    return GetTypeParamKind("Element");
  }

  /// <summary>
  /// Get the kind for a named type parameter from the current struct (e.g., "Key", "Value", "Element").
  /// Returns TypeParameter if the param exists but is unresolved; Integer if the param doesn't exist.
  /// </summary>
  private MaxonValueKind GetTypeParamKind(string paramName) {
    if (_currentTypeName != null
        && _typeRegistry.TryGetValue(_currentTypeName, out var typeInfo)
        && typeInfo is MlirStructType structType) {
      if (structType.TypeParams.TryGetValue(paramName, out var paramType)) {
        return paramType.ToValueKind();
      }
      // AssociatedTypeNames (from 'uses' clause) are type parameters too,
      // but aren't in TypeParams when conformance bindings occupy that slot
      if (structType.AssociatedTypeNames.Contains(paramName)) {
        return MaxonValueKind.TypeParameter;
      }
    }
    // No matching type parameter - default to Integer for non-generic types
    return MaxonValueKind.Integer;
  }

  /// <summary>
  /// Resolves the element kind for a __ManagedMemory alias by looking up the concrete
  /// type's "Element" type parameter. For example, a typealias KeyMemory = __ManagedMemory with Key
  /// will resolve to (TypeParameter, "Key").
  /// </summary>
  private (MaxonValueKind kind, string? typeParamName) GetManagedMemElementKind(string structTypeName) {
    if (_typeRegistry.TryGetValue(structTypeName, out var typeInfo)
        && typeInfo is MlirStructType structType
        && structType.TypeParams.TryGetValue("Element", out var elemType)) {
      if (elemType is MlirTypeParameterType tpt)
        return (MaxonValueKind.TypeParameter, tpt.ParameterName);
      return (elemType.ToValueKind(), null);
    }
    // For bare __ManagedMemory (no Element type param), don't fall back to enclosing type —
    // the enclosing type's Element (e.g., String's Element=Character from Iterable) is unrelated.
    if (ResolveBaseTypeName(structTypeName) == "__ManagedMemory")
      return (MaxonValueKind.Integer, null);
    // Fallback: use enclosing type's "Element" param (existing behavior)
    var kind = GetElementKind();
    if (kind == MaxonValueKind.TypeParameter)
      return (kind, "Element");
    return (kind, null);
  }

  /// Converts two managed args to cstrings, calls a runtime function, returns its result.
  private static BuiltinInfo TwoManagedToCStringRuntimeCall(string doc, string runtimeName) {
    return new(doc, p => {
      var arg1 = p.ResolveExprValue(p.ParseExpression());
      p.Expect(TokenType.Comma);
      var arg2 = p.ResolveExprValue(p.ParseExpression());
      p.Expect(TokenType.RightParen);
      var cstr1 = new MaxonManagedToCStringOp(arg1);
      p._currentBlock!.AddOp(cstr1);
      var cstr2 = new MaxonManagedToCStringOp(arg2);
      p._currentBlock!.AddOp(cstr2);
      var op = new MaxonCallRuntimeOp(runtimeName, [cstr1.Result, cstr2.Result], true);
      p._currentBlock!.AddOp(op);
      return op.Result;
    });
  }

  /// Calls a runtime function returning a cstring, converts to managed, optionally frees the cstring.
  private static BuiltinInfo RuntimeCallToManaged(string doc, string runtimeName, int argCount, bool freeCString) {
    return new(doc, p => {
      var args = new List<MaxonValue>();
      for (int i = 0; i < argCount; i++) {
        if (i > 0) p.Expect(TokenType.Comma);
        args.Add(p.ResolveExprValue(p.ParseExpression()));
      }
      p.Expect(TokenType.RightParen);
      var rtOp = new MaxonCallRuntimeOp(runtimeName, args, true);
      p._currentBlock!.AddOp(rtOp);
      var toManagedOp = new MaxonCStringToManagedOp(rtOp.Result!);
      p._currentBlock!.AddOp(toManagedOp);
      if (freeCString) {
        var freeOp = new MaxonCallRuntimeOp("mm_free", [rtOp.Result!], false);
        p._currentBlock!.AddOp(freeOp);
      }
      return toManagedOp.Result;
    });
  }

  /// Parses N arguments and emits a MaxonCallRuntimeOp.
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

  /// Returns the base source type name for a type (e.g., "IntManagedList" -> "__ManagedList").
  /// Returns the type name itself if it's not an alias.
  private string ResolveBaseTypeName(string typeName) {
    return _typeAliasSources.TryGetValue(typeName, out var source) ? source : typeName;
  }

  /// Checks if a struct type is a __ManagedMemory type, either directly by name,
  /// by prefix convention (__ManagedMemory_*), through type alias resolution,
  /// or by field layout (buffer, length, capacity, element_size).
  private bool IsManagedMemoryStruct(MlirStructType structType) {
    if (structType.Name == "__ManagedMemory" || structType.Name.StartsWith("__ManagedMemory_"))
      return true;
    if (ResolveBaseTypeName(structType.Name) == "__ManagedMemory")
      return true;
    // Fallback: check field layout for aliases not in _typeAliasSources (e.g., ByteMemory)
    if (structType.Fields.Count == 4
        && structType.Fields[0].Name == "buffer"
        && structType.Fields[1].Name == "length"
        && structType.Fields[2].Name == "capacity"
        && structType.Fields[3].Name == "element_size")
      return true;
    return false;
  }

  /// Resolves a type alias to its monomorphized concrete name by combining the base source
  /// type with the alias's type parameter names (e.g., "ENode" -> "__ManagedListNode_Element").
  /// Returns the type name itself if it's not an alias or has no type parameters.
  private string ResolveConcreteTypeName(string typeName) {
    if (!_typeAliasSources.TryGetValue(typeName, out var source)) return typeName;
    if (!_typeRegistry.TryGetValue(typeName, out var regType)
        || regType is not MlirStructType regStruct
        || regStruct.TypeParams.Count == 0) return source;
    // Normalize ranged primitives to their base type name so that e.g.
    // ByteArray (Element=Byte) and __Array_i8 (Element=i8) resolve identically.
    return $"{source}_{string.Join("_", regStruct.TypeParams.Values.Select(t => t is MlirRangedPrimitiveType rpt ? rpt.BaseType.Name : t.Name))}";
  }

  /// Gets the Element type parameter name for a managed-list-family type (ManagedList or ManagedListNode alias).
  /// Returns the concrete element type name (e.g., "Integer", "String").
  private string GetManagedListElementType(string structTypeName) {
    if (_typeRegistry.TryGetValue(structTypeName, out var typeInfo) && typeInfo is MlirStructType st) {
      if (st.TypeParams.TryGetValue("Element", out var elemType))
        return MlirType.FormatAsSourceName(elemType);
    }
    return "Element";
  }

  /// Gets the MaxonValueKind for the Element type of a managed-list-family type.
  private MaxonValueKind GetManagedListElementKind(string structTypeName) {
    if (_typeRegistry.TryGetValue(structTypeName, out var typeInfo) && typeInfo is MlirStructType st) {
      if (st.TypeParams.TryGetValue("Element", out var elemType))
        return elemType.ToValueKind();
    }
    return MaxonValueKind.Integer;
  }

  /// Ensures a concrete ManagedListNode alias exists with the same Element type as the given ManagedList type.
  /// For example, if listTypeName is "__ManagedList_Integer" (alias for ManagedList with Element=Integer),
  /// this creates/finds "__ManagedListNode_Integer" with TypeParams["Element"]=Integer.
  /// Returns the concrete ManagedListNode alias name, or "ManagedListNode" if no Element type can be resolved.
  private string EnsureConcreteManagedListNodeAlias(string listTypeName) {
    if (!_typeRegistry.TryGetValue(listTypeName, out var typeInfo) || typeInfo is not MlirStructType listStruct)
      return "__ManagedListNode";
    if (!listStruct.TypeParams.TryGetValue("Element", out var elemType))
      return "__ManagedListNode";

    var elemName = MlirType.FormatAsSourceName(elemType);
    var aliasName = $"__ManagedListNode_{elemName}";

    if (!_typeRegistry.ContainsKey(aliasName)) {
      var nodeBase = (MlirStructType)_typeRegistry["__ManagedListNode"];
      RegisterConcreteTypeAlias(aliasName, "__ManagedListNode", nodeBase,
        new Dictionary<string, MlirType> { ["Element"] = elemType });
    }
    _typeAliasSources.TryAdd(aliasName, "__ManagedListNode");
    return aliasName;
  }

  /// Skips an optional argument label (e.g., "value:" in `insertFirst(value: 42)`).
  /// Maxon supports labeled arguments but managed list intrinsics parse them directly.
  private void TrySkipArgLabel() {
    if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
      Advance(); // skip label
      Advance(); // skip ':'
    }
  }

  /// Tries to emit a builtin ManagedList or ManagedListNode method call directly as MaxonOps.
  /// Returns (true, result) if handled, (false, null) if not a builtin managed list method.
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedListMethod(string structTypeName, string methodName, MaxonValue selfValue) {
    var baseType = ResolveBaseTypeName(structTypeName);

    if (baseType == "__ManagedList") {
      var valueKind = GetManagedListElementType(structTypeName);
      var elementKind = GetManagedListElementKind(structTypeName);
      var concreteNodeAlias = EnsureConcreteManagedListNodeAlias(structTypeName);
      switch (methodName) {
        case "insertFirst": {
          // insertFirst(value Element) returns ManagedListNode
          TrySkipArgLabel();
          var value = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListInsertValueOp(selfValue, value, atHead: true, valueKind);
          _currentBlock!.AddOp(op);
          op.Result.TypeName = concreteNodeAlias;
          return (true, op.Result);
        }
        case "insertLast": {
          // insertLast(value Element) returns ManagedListNode
          TrySkipArgLabel();
          var value = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListInsertValueOp(selfValue, value, atHead: false, valueKind);
          _currentBlock!.AddOp(op);
          op.Result.TypeName = concreteNodeAlias;
          return (true, op.Result);
        }
        case "insertAfter": {
          // insertAfter(target ManagedListNode, value Element) returns ManagedListNode
          TrySkipArgLabel();
          var target = ResolveExprValue(ParseExpression());
          Expect(TokenType.Comma);
          TrySkipArgLabel();
          var value = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListInsertRelativeValueOp(selfValue, target, value, after: true, valueKind);
          _currentBlock!.AddOp(op);
          op.Result.TypeName = concreteNodeAlias;
          return (true, op.Result);
        }
        case "insertBefore": {
          // insertBefore(target ManagedListNode, value Element) returns ManagedListNode
          TrySkipArgLabel();
          var target = ResolveExprValue(ParseExpression());
          Expect(TokenType.Comma);
          TrySkipArgLabel();
          var value = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListInsertRelativeValueOp(selfValue, target, value, after: false, valueKind);
          _currentBlock!.AddOp(op);
          op.Result.TypeName = concreteNodeAlias;
          return (true, op.Result);
        }
        case "reinsertFirst": {
          // reinsertFirst(node ManagedListNode)
          TrySkipArgLabel();
          var node = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListReinsertOp(selfValue, node, atHead: true);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "reinsertLast": {
          // reinsertLast(node ManagedListNode)
          TrySkipArgLabel();
          var node = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListReinsertOp(selfValue, node, atHead: false);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "reinsertAfter": {
          // reinsertAfter(target ManagedListNode, node ManagedListNode)
          TrySkipArgLabel();
          var target = ResolveExprValue(ParseExpression());
          Expect(TokenType.Comma);
          TrySkipArgLabel();
          var node = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListReinsertRelativeOp(selfValue, target, node, after: true);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "reinsertBefore": {
          // reinsertBefore(target ManagedListNode, node ManagedListNode)
          TrySkipArgLabel();
          var target = ResolveExprValue(ParseExpression());
          Expect(TokenType.Comma);
          TrySkipArgLabel();
          var node = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListReinsertRelativeOp(selfValue, target, node, after: false);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "detach": {
          // detach(node ManagedListNode)
          TrySkipArgLabel();
          var node = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListDetachOp(selfValue, node);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "remove": {
          // remove(node ManagedListNode) returns Element
          TrySkipArgLabel();
          var node = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListRemoveOp(selfValue, node, valueKind, elementKind);
          _currentBlock!.AddOp(op);
          return (true, op.Result);
        }
        case "head": {
          // head() returns ManagedListNode throws ManagedListError
          Expect(TokenType.RightParen);
          var callOp = new MaxonCallOp("__managed_list_head", [selfValue], MaxonValueKind.Struct, concreteNodeAlias);
          _currentBlock!.AddOp(callOp);
          return (true, callOp.Result);
        }
        case "tail": {
          // tail() returns ManagedListNode throws ManagedListError
          Expect(TokenType.RightParen);
          var callOp = new MaxonCallOp("__managed_list_tail", [selfValue], MaxonValueKind.Struct, concreteNodeAlias);
          _currentBlock!.AddOp(callOp);
          return (true, callOp.Result);
        }
        case "count": {
          // count() returns int
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListCountOp(selfValue);
          _currentBlock!.AddOp(op);
          return (true, op.Result);
        }
        case "isEmpty": {
          // isEmpty() returns bool — implemented as count() == 0
          Expect(TokenType.RightParen);
          var countOp = new MaxonManagedListCountOp(selfValue);
          _currentBlock!.AddOp(countOp);
          var zeroLit = new MaxonLiteralOp(0L);
          _currentBlock!.AddOp(zeroLit);
          var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, countOp.Result, zeroLit.Result, MaxonValueKind.Integer);
          _currentBlock!.AddOp(cmpOp);
          return (true, cmpOp.Result);
        }
        case "clear": {
          // clear()
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListClearOp(selfValue, valueKind);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "cursorReset": {
          // cursorReset() — reset iteration cursor to null (before head)
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListCursorResetOp(selfValue);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
        case "cursorValue": {
          // cursorValue() returns Element — read value at current cursor position
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListCursorValueOp(selfValue, valueKind, elementKind);
          _currentBlock!.AddOp(op);
          return (true, op.Result);
        }
        case "cursorAdvance": {
          // cursorAdvance() — advance cursor to next node, throws if at end
          Expect(TokenType.RightParen);
          var callOp = new MaxonCallOp("__managed_list_cursor_advance", [selfValue], (MaxonValueKind?)null, null);
          _currentBlock!.AddOp(callOp);
          return (true, null);
        }
        case "cursorStart": {
          // cursorStart() — set cursor to head node, throws if empty
          Expect(TokenType.RightParen);
          var callOp = new MaxonCallOp("__managed_list_cursor_start", [selfValue], (MaxonValueKind?)null, null);
          _currentBlock!.AddOp(callOp);
          return (true, null);
        }
      }
    }

    if (baseType == "__ManagedListNode") {
      var valueKind = GetManagedListElementType(structTypeName);
      var elementKind = GetManagedListElementKind(structTypeName);
      switch (methodName) {
        case "next": {
          // next() returns ManagedListNode throws ManagedListError — preserve concrete alias
          Expect(TokenType.RightParen);
          var callOp = new MaxonCallOp("__managed_list_node_next", [selfValue], MaxonValueKind.Struct, structTypeName);
          _currentBlock!.AddOp(callOp);
          return (true, callOp.Result);
        }
        case "prev": {
          // prev() returns ManagedListNode throws ManagedListError — preserve concrete alias
          Expect(TokenType.RightParen);
          var callOp = new MaxonCallOp("__managed_list_node_prev", [selfValue], MaxonValueKind.Struct, structTypeName);
          _currentBlock!.AddOp(callOp);
          return (true, callOp.Result);
        }
        case "value": {
          // value() returns Element
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListNodeValueOp(selfValue, valueKind, elementKind);
          _currentBlock!.AddOp(op);
          return (true, op.Result);
        }
        case "setValue": {
          // setValue(v Element)
          TrySkipArgLabel();
          var value = ResolveExprValue(ParseExpression());
          Expect(TokenType.RightParen);
          var op = new MaxonManagedListNodeSetValueOp(selfValue, value, valueKind);
          _currentBlock!.AddOp(op);
          return (true, null);
        }
      }
    }

    return (false, null); // not a builtin managed list method
  }

  private static bool IsBuiltinMethodType(string baseTypeName) =>
    baseTypeName is "__ManagedList" or "__ManagedListNode" or "__ManagedMemory" or "__ManagedSocket" or "__ManagedFile" or "__ManagedDirectory";

  /// Unified dispatch for builtin type instance methods.
  /// Routes to ManagedList/ManagedListNode or ManagedMemory handlers based on the resolved base type.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinTypeMethod(
    string structTypeName, string methodName, MaxonValue selfValue) {
    var baseType = ResolveBaseTypeName(structTypeName);
    if (baseType is "__ManagedList" or "__ManagedListNode")
      return TryEmitBuiltinManagedListMethod(structTypeName, methodName, selfValue);
    if (baseType == "__ManagedMemory")
      return TryEmitBuiltinManagedMemoryMethod(structTypeName, methodName, selfValue);
    if (baseType == "__ManagedSocket")
      return TryEmitBuiltinManagedSocketMethod(structTypeName, methodName, selfValue);
    if (baseType == "__ManagedFile")
      return TryEmitBuiltinManagedFileMethod(structTypeName, methodName, selfValue);
    if (baseType == "__ManagedDirectory")
      return TryEmitBuiltinManagedDirectoryMethod(structTypeName, methodName, selfValue);
    throw new InvalidOperationException($"TryEmitBuiltinTypeMethod called for non-builtin type '{baseType}'");
  }

  /// Emits builtin __ManagedMemory instance method calls as MaxonOps.
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedMemoryMethod(
    string structTypeName, string methodName, MaxonValue selfValue) {
    switch (methodName) {
      case "length": {
        Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(selfValue, "__ManagedMemory", "length", MaxonValueKind.Integer);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "capacity": {
        Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(selfValue, "__ManagedMemory", "capacity", MaxonValueKind.Integer);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "elementSize": {
        Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(selfValue, "__ManagedMemory", "element_size", MaxonValueKind.Integer);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "clear": {
        Expect(TokenType.RightParen);
        var (_, typeParamName) = GetManagedMemElementKind(structTypeName);
        var op = new MaxonManagedMemClearOp(selfValue) {
          TypeParamName = typeParamName
        };
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "setLength": {
        TrySkipArgLabel();
        var newLen = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemSetLengthOp(selfValue, newLen);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "get": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var (elementKind, typeParamName) = GetManagedMemElementKind(structTypeName);
        var op = new MaxonManagedMemGetOp(selfValue, index, elementKind) {
          TypeParamName = typeParamName
        };
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "remove": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var (elementKind, typeParamName) = GetManagedMemElementKind(structTypeName);
        var op = new MaxonManagedMemRemoveOp(selfValue, index, elementKind) {
          TypeParamName = typeParamName
        };
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "set": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var value = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var (elementKind, typeParamName) = GetManagedMemElementKind(structTypeName);
        var op = new MaxonManagedMemSetOp(selfValue, index, value, elementKind) {
          TypeParamName = typeParamName
        };
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "grow": {
        TrySkipArgLabel();
        var newCap = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGrowOp(selfValue, newCap);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "shiftRight": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var count = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemShiftOp(selfValue, index, count, shiftRight: true);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "shiftLeft": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var count = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemShiftOp(selfValue, index, count, shiftRight: false);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "byteAt": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemByteGetOp(selfValue, index);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "setByte": {
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var value = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemByteSetOp(selfValue, index, value);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
      case "concat": {
        TrySkipArgLabel();
        var other = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var (elementKind, typeParamName) = GetManagedMemElementKind(structTypeName);
        var isStructElem = elementKind == MaxonValueKind.Struct || elementKind == MaxonValueKind.Enum;
        var op = new MaxonManagedMemConcatOp(selfValue, other) {
          IsStructElement = isStructElem,
          TypeParamName = typeParamName
        };
        _currentBlock!.AddOp(op);
        EmitLiteralTempAssign(op.Result);
        return (true, op.Result);
      }
      case "slice": {
        TrySkipArgLabel();
        var start = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var end = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var (elementKind, typeParamName) = GetManagedMemElementKind(structTypeName);
        var isStructElem = elementKind == MaxonValueKind.Struct || elementKind == MaxonValueKind.Enum;
        var op = new MaxonManagedMemSliceOp(selfValue, start, end) {
          IsStructElement = isStructElem,
          TypeParamName = typeParamName
        };
        _currentBlock!.AddOp(op);
        EmitLiteralTempAssign(op.Result);
        return (true, op.Result);
      }
      case "toCString": {
        Expect(TokenType.RightParen);
        var op = new MaxonManagedToCStringOp(selfValue);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "makeCharFromBytes": {
        TrySkipArgLabel();
        var pos = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var len = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonMakeCharFromBytesOp(selfValue, pos, len);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedMemory static method calls (create, fromCString).
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedMemoryStaticMethod(string methodName) {
    switch (methodName) {
      case "create": {
        TrySkipArgLabel();
        var count = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var elementSize = ParseElementSizeConstant();
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemCreateOp(count, elementSize);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "fromCString": {
        TrySkipArgLabel();
        var cstrPtr = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonCStringToManagedOp(cstrPtr);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedSocket instance method calls as MaxonOps.
  /// The opening '(' has already been consumed.
#pragma warning disable IDE0060 // structTypeName kept for interface consistency with other TryEmitBuiltin*Method dispatchers
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedSocketMethod(
    string structTypeName, string methodName, MaxonValue selfValue) {
#pragma warning restore IDE0060
    switch (methodName) {
      case "sendFrom": {
        // sendFrom(managed, offset, length) → maxon_net_send(handle, buf+offset, length)
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var offset = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var length = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedSocket", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var bufferRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "buffer", MaxonValueKind.Integer);
        _currentBlock!.AddOp(bufferRef);
        var addOp = new MaxonBinOp(MaxonBinOperator.Add, bufferRef.Result, offset, MaxonValueKind.Integer);
        _currentBlock!.AddOp(addOp);
        var op = new MaxonCallRuntimeOp("maxon_net_send", [handleRef.Result, addOp.Result, length], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "recv": {
        // recv(managed) → maxon_net_recv(handle, buf, capacity)
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedSocket", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var bufferRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "buffer", MaxonValueKind.Integer);
        _currentBlock!.AddOp(bufferRef);
        var capacityRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "capacity", MaxonValueKind.Integer);
        _currentBlock!.AddOp(capacityRef);
        var op = new MaxonCallRuntimeOp("maxon_net_recv", [handleRef.Result, bufferRef.Result, capacityRef.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "close": {
        // close() → maxon_net_close(handle), then zero the handle
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedSocket", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var op = new MaxonCallRuntimeOp("maxon_net_close", [handleRef.Result], false);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedSocket static method calls (tcpConnect).
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedSocketStaticMethod(string methodName) {
    switch (methodName) {
      case "tcpConnect": {
        // tcpConnect(managed_host, port) → maxon_net_tcp_connect(cstring, port) → managed socket ptr
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var port = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var bufferRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "buffer", MaxonValueKind.Integer);
        _currentBlock!.AddOp(bufferRef);
        var lengthRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "length", MaxonValueKind.Integer);
        _currentBlock!.AddOp(lengthRef);
        var toCStr = new MaxonCallRuntimeOp("maxon_to_cstring", [bufferRef.Result, lengthRef.Result], true);
        _currentBlock!.AddOp(toCStr);
        var op = new MaxonCallRuntimeOp("maxon_net_tcp_connect", [toCStr.Result!, port], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedFile instance method calls as MaxonOps.
  /// The opening '(' has already been consumed.
#pragma warning disable IDE0060 // structTypeName kept for interface consistency with other TryEmitBuiltin*Method dispatchers
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedFileMethod(
    string structTypeName, string methodName, MaxonValue selfValue) {
#pragma warning restore IDE0060
    switch (methodName) {
      case "size": {
        // size() → maxon_file_size(handle)
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedFile", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var op = new MaxonCallRuntimeOp("maxon_file_size", [handleRef.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "read": {
        // read(managed, size) → maxon_file_read(handle, buffer, size, capacity)
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var size = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedFile", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var bufferRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "buffer", MaxonValueKind.Integer);
        _currentBlock!.AddOp(bufferRef);
        var capacityRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "capacity", MaxonValueKind.Integer);
        _currentBlock!.AddOp(capacityRef);
        var op = new MaxonCallRuntimeOp("maxon_file_read", [handleRef.Result, bufferRef.Result, size, capacityRef.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "write": {
        // write(managed) → maxon_managed_file_write(handle, buffer, length)
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedFile", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var bufferRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "buffer", MaxonValueKind.Integer);
        _currentBlock!.AddOp(bufferRef);
        var lengthRef = new MaxonFieldAccessOp(managed, "__ManagedMemory", "length", MaxonValueKind.Integer);
        _currentBlock!.AddOp(lengthRef);
        var op = new MaxonCallRuntimeOp("maxon_managed_file_write", [handleRef.Result, bufferRef.Result, lengthRef.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "close": {
        // close() → maxon_file_close(handle)
        Expect(TokenType.RightParen);
        var handleRef = new MaxonFieldAccessOp(selfValue, "__ManagedFile", "_handle", MaxonValueKind.Integer);
        _currentBlock!.AddOp(handleRef);
        var op = new MaxonCallRuntimeOp("maxon_file_close", [handleRef.Result], false);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedFile static method calls.
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedFileStaticMethod(string methodName) {
    switch (methodName) {
      case "openRead": {
        // openRead(managed) → maxon_managed_file_open_read(cstring) → managed file ptr or -1
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_managed_file_open_read", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "openWrite": {
        // openWrite(managed) → maxon_managed_file_open_write(cstring) → managed file ptr or -1
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_managed_file_open_write", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "openWriteExecutable": {
        // openWriteExecutable(managed) → maxon_managed_file_open_write_executable(cstring) → managed file ptr or -1
        // Same as openWrite but creates file with executable permissions (0755) on Unix.
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_managed_file_open_write_executable", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "exists": {
        // exists(managed) → maxon_file_exists(cstring) → 1/0
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_file_exists", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "delete": {
        // delete(managed) → maxon_file_delete(cstring) → 0=success
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_file_delete", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "stat": {
        // stat(managed) → maxon_file_stat(cstring) → raw buffer ptr or -1
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_file_stat", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "statField": {
        // statField(buffer, index) → i64 value at index*8 from buffer
        TrySkipArgLabel();
        var buffer = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        TrySkipArgLabel();
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonCallRuntimeOp("maxon_file_stat_field", [buffer, index], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "statFree": {
        // statFree(buffer) → frees the stat buffer
        TrySkipArgLabel();
        var buffer = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonCallRuntimeOp("mm_raw_free", [buffer], false);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedDirectory instance method calls as MaxonOps.
  /// The opening '(' has already been consumed.
#pragma warning disable IDE0060 // structTypeName kept for interface consistency with other TryEmitBuiltin*Method dispatchers
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedDirectoryMethod(
    string structTypeName, string methodName, MaxonValue selfValue) {
#pragma warning restore IDE0060
    switch (methodName) {
      case "filename": {
        // filename() → maxon_find_filename(block) → cstring → managed
        Expect(TokenType.RightParen);
        var blockRef = new MaxonFieldAccessOp(selfValue, "__ManagedDirectory", "_block", MaxonValueKind.Integer);
        _currentBlock!.AddOp(blockRef);
        var op = new MaxonCallRuntimeOp("maxon_find_filename", [blockRef.Result], true);
        _currentBlock!.AddOp(op);
        var toManagedOp = new MaxonCStringToManagedOp(op.Result!);
        _currentBlock!.AddOp(toManagedOp);
        return (true, toManagedOp.Result);
      }
      case "next": {
        // next() → maxon_find_next_file(block)
        Expect(TokenType.RightParen);
        var blockRef = new MaxonFieldAccessOp(selfValue, "__ManagedDirectory", "_block", MaxonValueKind.Integer);
        _currentBlock!.AddOp(blockRef);
        var op = new MaxonCallRuntimeOp("maxon_find_next_file", [blockRef.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "close": {
        // close() → maxon_managed_dir_close(block)
        Expect(TokenType.RightParen);
        var blockRef = new MaxonFieldAccessOp(selfValue, "__ManagedDirectory", "_block", MaxonValueKind.Integer);
        _currentBlock!.AddOp(blockRef);
        var op = new MaxonCallRuntimeOp("maxon_managed_dir_close", [blockRef.Result], false);
        _currentBlock!.AddOp(op);
        return (true, null);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedDirectory static method calls.
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedDirectoryStaticMethod(string methodName) {
    switch (methodName) {
      case "openSearch": {
        // openSearch(managed) → maxon_managed_dir_open_search(cstring) → managed dir ptr or 0
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_managed_dir_open_search", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
      case "exists": {
        // exists(managed) → maxon_directory_exists(cstring) → bool as int
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_directory_exists", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        var zeroOp = new MaxonLiteralOp(0L);
        _currentBlock!.AddOp(zeroOp);
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Ne, op.Result!, zeroOp.Result, MaxonValueKind.Integer);
        _currentBlock!.AddOp(cmpOp);
        return (true, cmpOp.Result);
      }
      case "create": {
        // create(managed) → maxon_create_directory(cstring) → bool as int
        TrySkipArgLabel();
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var toCstrOp = new MaxonManagedToCStringOp(managed);
        _currentBlock!.AddOp(toCstrOp);
        var op = new MaxonCallRuntimeOp("maxon_create_directory", [toCstrOp.Result], true);
        _currentBlock!.AddOp(op);
        var zeroOp = new MaxonLiteralOp(0L);
        _currentBlock!.AddOp(zeroOp);
        var cmpOp = new MaxonBinOp(MaxonBinOperator.Ne, op.Result!, zeroOp.Result, MaxonValueKind.Integer);
        _currentBlock!.AddOp(cmpOp);
        return (true, cmpOp.Result);
      }
      case "currentPath": {
        // currentPath() → maxon_get_current_directory() → cstring → managed
        Expect(TokenType.RightParen);
        var rtOp = new MaxonCallRuntimeOp("maxon_get_current_directory", [], true);
        _currentBlock!.AddOp(rtOp);
        var toManagedOp = new MaxonCStringToManagedOp(rtOp.Result!);
        _currentBlock!.AddOp(toManagedOp);
        var freeOp = new MaxonCallRuntimeOp("mm_free", [rtOp.Result!], false);
        _currentBlock!.AddOp(freeOp);
        return (true, toManagedOp.Result);
      }
    }
    return (false, null);
  }

  /// Emits builtin __ManagedList static method calls.
  /// The opening '(' has already been consumed.
  private (bool Handled, MaxonValue? Result) TryEmitBuiltinManagedListStaticMethod(string methodName) {
    switch (methodName) {
      case "create": {
        Expect(TokenType.RightParen);
        var op = new MaxonManagedListCreateOp();
        _currentBlock!.AddOp(op);
        return (true, op.Result);
      }
    }
    return (false, null);
  }

  /// <summary>
  /// Dispatches a __Builtins static method call.
  /// Called after consuming '('. Returns the result value (or null for void builtins).
  /// </summary>
  private MaxonValue? EmitBuiltinsStaticMethod(Token token) {
    if (CompilerBuiltins.TryGetValue(token.Value, out var info))
      return info.Handler(this);
    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unknown builtin '{token.Value}'", token.Line, token.Column);
  }

  private MaxonCallOp? TrySiblingMethodCall(Token token) {
    if (_currentTypeName == null || ResolveVariable("self") is not ResolvedVar.Local(var selfInfo))
      return null;

    // Construct namespace-qualified type name for sibling method lookup
    var namespace_ = DeriveNamespace(includeFilename: false);
    var qualifiedTypeName = string.IsNullOrEmpty(namespace_) ? _currentTypeName : $"{namespace_}.{_currentTypeName}";
    var siblingMethodName = $"{qualifiedTypeName}.{token.Value}";

    var resolvedSiblingName = ResolveMethodName(siblingMethodName);
    if (resolvedSiblingName == null)
      return null;
    Advance(); // consume '('
    var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedSiblingName, token.Line, token.Column);

    // Check if the resolved function is actually an instance method (first param is "self").
    // Module-level functions in the same file should be called without prepending self.
    var resolvedFunc = _currentModule!.Functions.FirstOrDefault(f => f.Name == resolvedSiblingName || UnmangleName(f.Name) == resolvedSiblingName);
    if (resolvedFunc != null && resolvedFunc.ParamNames.Count > 0 && resolvedFunc.ParamNames[0] == "self") {
      var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      var (siblingArgs, siblingCallee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, structVal);
      return CreateFunctionCall(qualifiedFuncToken, siblingArgs, siblingCallee);
    }

    var (args, callee) = ParseCallArgs(qualifiedFuncToken);
    return CreateFunctionCall(qualifiedFuncToken, args, callee);
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
    var callOp = CreateFunctionCall(qualifiedToken, args, callee);
    OverrideCalleeForTypeAlias(callOp, typeToken.Value, qualifiedName);
    MarkDiscardedResult(callOp, methodToken);
  }

  private void ParseInstanceMethodCallStatement(string name, ResolvedVar resolved, string methodName) {
    Advance(); // consume variable name
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Advance(); // consume '('

    MaxonValue structVal = resolved switch {
      ResolvedVar.Local(var info) => info.Value,
      ResolvedVar.Global(var info) => EmitGlobalLoad(name, info).Value,
      _ => throw new InvalidOperationException()
    };
    // Set self mutability and variable name for pass-by-reference tracking
    _lastExprWasMutableVar = resolved switch {
      ResolvedVar.Local(var info) => info.Mutable,
      ResolvedVar.Global(var info) => info.Mutable,
      _ => false
    };
    _lastExprVarName = name;

    var qualifiedToken = new Token(TokenType.Identifier, methodName, methodToken.Line, methodToken.Column);
    var (args, callee) = ParseInstanceMethodCallArgs(qualifiedToken, structVal);
    var callOp = CreateFunctionCall(qualifiedToken, args, callee);
    MarkDiscardedResult(callOp, methodToken);
  }

  /// Resolve the return type of an interface method signature to a ValueKind and optional struct type name.
  /// Self (which resolves to the interface name during parsing) maps to the concrete userTypeName.
  private (MaxonValueKind? Kind, string? StructTypeName) ResolveInterfaceMethodReturn(MlirInterfaceMethodSignature ifaceMethod, string userTypeName, string fieldName, Token errorToken) {
    var resultKind = ifaceMethod.ReturnTypeName switch {
      null => (MaxonValueKind?)null,
      "int" => MaxonValueKind.Integer,
      "float" => MaxonValueKind.Float,
      "f32" => MaxonValueKind.Float32,
      "bool" => MaxonValueKind.Bool,
      "byte" => MaxonValueKind.Byte,
      "Self" => MaxonValueKind.Struct,
      { } n when _typeRegistry.TryGetValue(n, out var rType) && rType is MlirInterfaceType => MaxonValueKind.Struct,
      { } n when IsAssociatedTypeName(n) => MaxonValueKind.TypeParameter,
      { } n when _typeRegistry.TryGetValue(n, out var rType) && rType is MlirRangedPrimitiveType rpt => rpt.BaseType.ToValueKind(),
      { } n => throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Unsupported return type '{n}' in interface method '{fieldName}'", errorToken.Line, errorToken.Column)
    };
    bool isSelfReturn = ifaceMethod.ReturnTypeName == "Self"
      || (_typeRegistry.TryGetValue(ifaceMethod.ReturnTypeName ?? "", out var retType) && retType is MlirInterfaceType);
    string? resultStructTypeName = isSelfReturn ? userTypeName : null;
    return (resultKind, resultStructTypeName);
  }

  private void ParseTypeParamMethodCallStatement(string name, ResolvedVar resolved, string userTypeName, string fieldName, MlirInterfaceMethodSignature ifaceMethod) {
    Advance(); // consume variable name
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Advance(); // consume '('

    MaxonValue structVal = resolved switch {
      ResolvedVar.Local(var info) => info.Value,
      ResolvedVar.Global(var info) => EmitGlobalLoad(name, info).Value,
      _ => throw new InvalidOperationException()
    };

    var qualifiedMethodName = $"{userTypeName}.{fieldName}";
    var args = new List<MaxonValue> { structVal };
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

    var (resultKind, resultStructTypeName) = ResolveInterfaceMethodReturn(ifaceMethod, userTypeName, fieldName, methodToken);
    var callOp = new MaxonCallOp(qualifiedMethodName, args, resultKind, resultStructTypeName);
    _currentBlock!.AddOp(callOp);
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
    ExpectNewline();

    // Save entry block to append cond_br later
    var entryBlock = _currentBlock!;

    // Create and parse the then block
    var thenBlock = _currentFunction!.Body.AddBlock(thenLabel);
    _currentBlock = thenBlock;
    var thenOuterScope = _variables.SnapshotKeys();
    PushScope();
    ParseBodyUntilEnd();
    var thenInnerScope = _variables.KeysSince(thenOuterScope);
    PopScope();
    var thenEndBlock = _currentBlock; // may differ from thenBlock after nested if/else

    // Parse: end 'thenLabel'
    ExpectEndLabel(thenSourceLabel);

    // Check for else or else-if
    MlirBlock<MaxonOp>? elseBlock = null;
    MlirBlock<MaxonOp>? elseEndBlock = null;
    string? elseLabel = null;
    List<string>? elseInnerScope = null;
    if (Check(TokenType.Else)) {
      Advance(); // consume 'else'
      if (Check(TokenType.If)) {
        // else-if chain: create a synthetic block and parse the nested if into it
        elseLabel = $"{thenLabel}.elseif";
        elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
        _currentBlock = elseBlock;
        var elseIfOuterScope = _variables.SnapshotKeys();
        PushScope();
        ParseIf();
        elseInnerScope = _variables.KeysSince(elseIfOuterScope);
        PopScope();
        elseEndBlock = _currentBlock;
      } else {
        var elseSourceLabel = Expect(TokenType.CharacterLiteral).Value;
        elseLabel = UniqueLabel(elseSourceLabel);
        ExpectNewline();

        elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
        _currentBlock = elseBlock;
        var elseOuterScope = _variables.SnapshotKeys();
        PushScope();
        ParseBodyUntilEnd();
        elseInnerScope = _variables.KeysSince(elseOuterScope);
        PopScope();
        elseEndBlock = _currentBlock;
        ExpectEndLabel(elseSourceLabel);
      }
    }

    EmitConditionalBranch(entryBlock, condition, thenLabel, thenBlock, thenEndBlock, thenInnerScope, elseLabel, elseBlock, elseEndBlock, elseInnerScope);
  }

  /// <summary>
  /// Creates merge blocks and emits a conditional branch for if/if-try statements.
  /// Handles the common logic of determining if branches need merge blocks and setting up control flow.
  /// thenEndBlock/elseEndBlock are the final blocks after parsing each branch body
  /// (may differ from initial blocks due to nested if/else creating merge blocks).
  /// </summary>
  private void EmitConditionalBranch(MlirBlock<MaxonOp> entryBlock, MaxonValue condition, string thenLabel, MlirBlock<MaxonOp> thenBlock, MlirBlock<MaxonOp>? thenEndBlock, List<string>? thenInnerScope, string? elseLabel, MlirBlock<MaxonOp>? elseBlock, MlirBlock<MaxonOp>? elseEndBlock, List<string>? elseInnerScope) {
    // Use the end blocks for merge decisions — the end block is where control flow
    // actually is after parsing each branch (may be a merge block from nested if/else)
    bool thenTerminated = thenEndBlock == null || BlockEndsWithTerminator(thenEndBlock);
    bool elseTerminated = elseEndBlock != null ? BlockEndsWithTerminator(elseEndBlock) : (elseBlock != null);

    bool thenNeedsMerge = !thenTerminated;
    bool elseNeedsMerge = elseBlock != null && !elseTerminated;

    if (thenNeedsMerge || elseNeedsMerge) {
      var mergeLabel = $"{thenLabel}.merge";
      var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);

      if (thenNeedsMerge) {
        var tb = thenEndBlock ?? thenBlock;
        if (thenInnerScope != null) tb.AddOp(new MaxonScopeEndOp(thenInnerScope) { VarMetadata = _variables.GetScopeEndVarMetadata() });
        tb.AddOp(new MaxonBrOp(mergeLabel));
      }
      if (elseNeedsMerge) {
        var eb = elseEndBlock ?? elseBlock!;
        if (elseInnerScope != null) eb.AddOp(new MaxonScopeEndOp(elseInnerScope) { VarMetadata = _variables.GetScopeEndVarMetadata() });
        eb.AddOp(new MaxonBrOp(mergeLabel));
      }

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

    // If the call returned a struct, EmitCallReturnTempAssign added a __call_tmp_ assign — remove it
    var lastOp = _currentBlock!.Operations[^1];
    if (lastOp is MaxonAssignOp { IsDeclaration: true } tmpAssign2 && tmpAssign2.VarName.StartsWith("__call_tmp_")) {
      _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
      _variables.Remove(tmpAssign2.VarName);
      lastOp = _currentBlock!.Operations[^1];
    }
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
    var tryCallOp = new MaxonTryCallOp(callOp.Callee, callOp.Args, callOp.ResultKind, callOp.ResultStructTypeName) {
      ArgMutabilities = callOp.ArgMutabilities,
      ArgVarNames = callOp.ArgVarNames,
      CallLine = callOp.CallLine,
      CallColumn = callOp.CallColumn
    };
    _currentBlock!.AddOp(tryCallOp);

    // Store error flag and result to mutable variables for cross-block access
    var tryInfo = new TryResultInfo(tryCallOp.ErrorFlag, tryCallOp.Result, tryCallOp.ResultKind, tryCallOp.ResultStructTypeName);
    var (errorFlagVar, resultVar) = StoreTryValuesForCrossBlockAccess(tryInfo);

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
    var ifTryThenOuterScope = _variables.SnapshotKeys();
    PushScope();

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
      _variables.Declare(bindingName, resultKind, false, loadedValue, _currentBlock!, structTypeName: bindingStructTypeName);
    }

    ParseBodyUntilEnd();
    var ifTryThenInnerScope = _variables.KeysSince(ifTryThenOuterScope);
    PopScope();
    var thenEndBlock = _currentBlock;
    ExpectEndLabel(thenSourceLabel);

    // Check for else clause
    MlirBlock<MaxonOp>? elseBlock = null;
    MlirBlock<MaxonOp>? elseEndBlock = null;
    string? elseLabel = null;
    List<string>? ifTryElseInnerScope = null;
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
      ExpectNewline();

      elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
      _currentBlock = elseBlock;
      var ifTryElseOuterScope = _variables.SnapshotKeys();
      PushScope();

      // If error binding requested, emit a typed error binding in the else block
      if (errorBindingToken != null) {
        EmitErrorBinding(errorBindingToken.Value, errorFlagVar, callee?.ThrowsType);
      }

      ParseBodyUntilEnd();
      ifTryElseInnerScope = _variables.KeysSince(ifTryElseOuterScope);
      PopScope();
      elseEndBlock = _currentBlock;
      ExpectEndLabel(elseSourceLabel);
    }

    EmitConditionalBranch(entryBlock, condition, thenLabel, thenBlock, thenEndBlock, ifTryThenInnerScope, elseLabel, elseBlock, elseEndBlock, ifTryElseInnerScope);
  }

  private void ParseWhile() {
    Advance(); // consume 'while'

    // Save the entry block - we'll add the branch to the header from here
    var entryBlock = _currentBlock!;

    // Scan forward to find the loop label - the last character literal before the newline.
    // Character literals in the condition (e.g. '\n') must not be confused with the label.
    int savedPos = _pos;
    int lastCharLitPos = -1;
    while (!Check(TokenType.Newline) && !IsAtEnd()) {
      if (Check(TokenType.CharacterLiteral)) lastCharLitPos = _pos;
      Advance();
    }
    if (lastCharLitPos < 0) {
      throw new CompileError(ErrorCode.ParserExpectedToken, "Expected loop label after while condition", Current().Line, Current().Column);
    }
    var loopSourceLabel = _tokens[lastCharLitPos].Value;
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
    ExpectNewline();

    // Emit cond_br: if condition is true, go to body; else go to exit
    headerBlock.AddOp(new MaxonCondBrOp(condition, bodyLabel, exitLabel));

    // Create and parse the body block
    var bodyBlock = _currentFunction!.Body.AddBlock(bodyLabel);
    _currentBlock = bodyBlock;
    var loopOuterScope = _variables.SnapshotKeys();
    PushScope();
    _loopStack.Push(new LoopContext(loopSourceLabel, headerLabel, exitLabel, loopOuterScope));
    ParseBodyUntilEnd();
    var loopInnerScope = _variables.KeysSince(loopOuterScope);
    PopScope();
    _loopStack.Pop();
    ExpectEndLabel(loopSourceLabel);

    // At end of body, branch back to header
    // _currentBlock may differ from bodyBlock (e.g. if/else merge block)
    // _currentBlock can be null if all paths in the body terminated
    if (_currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonScopeEndOp(loopInnerScope) { VarMetadata = _variables.GetScopeEndVarMetadata() });
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

    // Support tuple destructuring: for (x, y) in collection 'label'
    List<string>? destructureNames = null;
    string itemName;
    if (Check(TokenType.LeftParen)) {
      Advance(); // consume '('
      destructureNames = [];
      do {
        if (Check(TokenType.Comma)) Advance();
        var nameToken = Expect(TokenType.Identifier);
        if (nameToken.Value == "_") {
          destructureNames.Add($"__discard_{_discardCounter++}");
        } else {
          destructureNames.Add(nameToken.Value);
          _localVarLocations.Add((nameToken.Value, nameToken.Line, nameToken.Column));
        }
      } while (Check(TokenType.Comma));
      Expect(TokenType.RightParen);
      itemName = $"__for_tuple_{_blockCounter}";
    } else {
      var itemToken = Expect(TokenType.Identifier);
      if (itemToken.Value == "_") {
        itemName = $"__discard_{_discardCounter++}";
      } else {
        itemName = itemToken.Value;
        _localVarLocations.Add((itemName, itemToken.Line, itemToken.Column));
      }
    }
    Expect(TokenType.In);
    var iterableExpr = ParseExpression();
    var iterableValue = ResolveExprValue(iterableExpr);
    var iterableSourceVarName = _lastExprVarName; // capture before further parsing overwrites it

    // Range expression: `for i in start to end` or `for i in start upto end`
    if (Check(TokenType.To) || Check(TokenType.Upto)) {
      var inclusive = Check(TokenType.To);
      Advance(); // consume 'to' or 'upto'
      var endExpr = ParseExpression();
      var endValue = ResolveExprValue(endExpr);
      var loopSourceLabel2 = Expect(TokenType.CharacterLiteral).Value;
      ExpectNewline();

      // Validate that the start value's type implements Strideable
      var startKind = DetermineValueKind(iterableValue);
      var endKind = DetermineValueKind(endValue);
      if (startKind != endKind)
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          "Range start and end must be the same type", forToken.Line, forToken.Column);

      ValidateStrideableConformance(startKind, iterableValue, forToken);

      ParseRangeForLoop(itemName, iterableValue, endValue, startKind, inclusive, loopSourceLabel2, forToken,
        iterableValue is MaxonStruct sms ? sms.TypeName : null);
      return;
    }

    var loopSourceLabel = Expect(TokenType.CharacterLiteral).Value;
    ExpectNewline();

    var iterableTypeName = ((iterableValue is MaxonStruct ms) ? ms.TypeName : _currentTypeName) ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
        "Cannot determine type for 'for-in' iterable expression",
        forToken.Line, forToken.Column);

    var iterableType = _typeRegistry.TryGetValue(iterableTypeName, out var regType)
      ? regType as MlirStructType : null;

    string nextMethodName;
    string createIteratorMethodName;
    MlirType? elementMlirType;

    if (iterableType is { IsInterfaceAlias: true }) {
      // Interface alias: resolve next() from the interface definition
      var ifaceName = iterableType.ConformingInterfaces[0];
      nextMethodName = $"{iterableTypeName}.next";

      // Get element type from interface method signature + type params
      var ifaceType = _typeRegistry.TryGetValue(ifaceName, out var ifType) ? ifType as MlirInterfaceType : null;
      var nextSig = ifaceType?.Methods.FirstOrDefault(m => m.Name == "next")
        ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Interface '{ifaceName}' has no next() method", forToken.Line, forToken.Column);

      // Resolve the return type through the alias's type params
      if (nextSig.ReturnTypeName != null && iterableType.TypeParams.TryGetValue(nextSig.ReturnTypeName, out var resolvedElemType)) {
        elementMlirType = resolvedElemType;
      } else if (nextSig.ReturnTypeName != null && _typeRegistry.TryGetValue(nextSig.ReturnTypeName, out var regElemType)) {
        elementMlirType = regElemType;
      } else {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot resolve element type for interface '{ifaceName}' iterator", forToken.Line, forToken.Column);
      }

      // Only emit createIterator if the interface defines it
      var hasCreateIterator = ifaceType?.Methods.Any(m => m.Name == "createIterator") == true;
      createIteratorMethodName = hasCreateIterator ? $"{iterableTypeName}.createIterator" : "";

      // Create stub functions so calls can reference them (monomorphization rewrites later)
      if (!_currentModule!.Functions.Any(f => f.Name == nextMethodName)) {
        var stubFunc = new MlirFunction<MaxonOp>(nextMethodName,
          ["self"], [iterableType], elementMlirType,
          _typeRegistry.TryGetValue("IterationError", out var iterErrType) ? iterErrType : null);
        _currentModule.Functions.Add(stubFunc);
      }
      if (hasCreateIterator && !_currentModule!.Functions.Any(f => f.Name == $"{iterableTypeName}.createIterator")) {
        var stubFunc = new MlirFunction<MaxonOp>($"{iterableTypeName}.createIterator",
          ["self"], [iterableType], null, null);
        _currentModule.Functions.Add(stubFunc);
      }
    } else {
      // Concrete type: resolve next() and createIterator() from the module's functions
      nextMethodName = ResolveMethodName($"{iterableTypeName}.next") ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Type '{iterableTypeName}' does not implement Iterable (missing next() method)",
          forToken.Line, forToken.Column);
      createIteratorMethodName = ResolveMethodName($"{iterableTypeName}.createIterator")
          ?? "";

      var nextFunc = _currentModule!.Functions.FirstOrDefault(f => f.Name == nextMethodName)
        ?? _currentModule!.Functions.First(f => UnmangleName(f.Name) == nextMethodName);
      elementMlirType = nextFunc.ReturnType;
      // Resolve type parameter to concrete type using the iterable's type params
      if (elementMlirType is MlirTypeParameterType tp
          && iterableType?.TypeParams.TryGetValue(tp.ParameterName, out var concreteElemType) == true) {
        elementMlirType = concreteElemType;
      }
    }

    // Resolve to canonical type (pre-scanned stubs may have 0 fields)
    if (elementMlirType is MlirStructType retStruct
        && _typeRegistry.TryGetValue(retStruct.Name, out var canonical)
        && canonical is MlirStructType canonicalStruct) {
      elementMlirType = canonicalStruct;
    }

    // Resolve tuple type parameter fields to concrete types using the iterable's type params.
    // Entry is (Key, Value) with MlirTypeParameterType fields — substitute with concrete types.
    if (elementMlirType is MlirStructType tupleElem && tupleElem.IsTuple
        && tupleElem.Fields.Any(f => f.Type is MlirTypeParameterType)
        && iterableType?.TypeParams is { Count: > 0 }) {
      var resolvedFields = tupleElem.Fields.Select(f => {
        if (f.Type is MlirTypeParameterType tp
            && iterableType.TypeParams.TryGetValue(tp.ParameterName, out var concreteType))
          return new MlirStructField(f.Name, concreteType, f.IsExported, f.IsMutable, f.DefaultValue);
        return f;
      }).ToList();
      elementMlirType = new MlirStructType(tupleElem.Name, resolvedFields, isTuple: true);
    }
    var elementKind = elementMlirType!.ToValueKind();
    var elementStructTypeName = elementMlirType switch {
      MlirStructType s => s.Name,
      MlirUnionType e => e.Name,
      _ => (string?)null
    };

    var loopLabel = UniqueLabel(loopSourceLabel);
    var headerLabel = $"{loopLabel}.header";
    var bodyLabel = loopLabel;
    var exitLabel = $"{loopLabel}.exit";

    // Copy the iterable into a mutable iterator variable (heap-pointer semantics: shares backing data)
    var iterVarName = $"__for_iter_{_blockCounter}";
    _currentBlock!.AddOp(new MaxonAssignOp(iterVarName, iterableValue, isDeclaration: true, isMutable: true, MaxonValueKind.Struct));
    _variables.Declare(iterVarName, MaxonValueKind.Struct, true, iterableValue, _currentBlock!, OwnershipFlags.IsTemp, structTypeName: iterableTypeName);

    // Call createIterator() on the copy to zero iteration state for a fresh traversal.
    // createIterator() is void — it mutates the iterator in place via heap-pointer semantics.
    if (createIteratorMethodName != "") {
      var iterVar = _variables[iterVarName];
      var createIterCallOp = new MaxonCallOp(createIteratorMethodName, [iterVar.Value], null, null);
      _currentBlock!.AddOp(createIterCallOp);
    }

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
    _variables.Declare(errorFlagVar, MaxonValueKind.Integer, true, tryCallOp.ErrorFlag, headerBlock);

    string? resultVar = null;
    if (tryCallOp.Result != null) {
      resultVar = $"__forin_result_{_blockCounter++}";
      headerBlock.AddOp(new MaxonAssignOp(resultVar, tryCallOp.Result, true, true, elementKind));
      _variables.Declare(resultVar, elementKind, true, tryCallOp.Result, headerBlock, OwnershipFlags.IsTemp | OwnershipFlags.CallReturn, structTypeName: elementStructTypeName);
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
    var forInOuterScope = _variables.SnapshotKeys();
    PushScope();
    _loopStack.Push(new LoopContext(loopSourceLabel, headerLabel, exitLabel, forInOuterScope,
      iterVarName, nextMethodName, elementKind, elementStructTypeName, iterableTypeName,
      ForInResultVarName: resultVar));

    if (resultVar != null) {
      var loadedValue = EmitVarRefOp(resultVar, elementKind, elementStructTypeName);
      _currentBlock!.AddOp(new MaxonAssignOp(itemName, loadedValue, isDeclaration: true, isMutable: false, elementKind));
      _variables.Declare(itemName, elementKind, false, loadedValue, bodyBlock, structTypeName: elementStructTypeName);

      // Destructure tuple into individual variables
      if (destructureNames != null) {
        if (elementMlirType is not MlirStructType tupleType || !tupleType.IsTuple)
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            "For-loop tuple destructuring requires an iterator that returns a tuple", forToken.Line, forToken.Column);
        if (destructureNames.Count != tupleType.Fields.Count)
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"Tuple has {tupleType.Fields.Count} elements but destructuring has {destructureNames.Count} bindings",
            forToken.Line, forToken.Column);

        if (iterableTypeName.Contains("EnumeratedIterator")
            && destructureNames[0].StartsWith("__discard_"))
          throw new CompileError(ErrorCode.SemanticDiscardedEnumeratedIndex,
            "discarding the index of enumerated() is unnecessary; use 'for value in collection' instead",
            forToken.Line, forToken.Column);

        EmitTupleFieldBindings(itemName, tupleType, destructureNames, isMutable: false);
      }
    }

    // Make iterator source immutable for the loop body to prevent mutation during iteration
    VarInfo? originalIterableInfo = null;
    if (iterableSourceVarName != null && _variables.TryGetValue(iterableSourceVarName, out var srcInfo) && srcInfo.Mutable) {
      originalIterableInfo = srcInfo;
      _variables[iterableSourceVarName] = srcInfo with { Mutable = false };
    }

    // Parse the body
    ParseBodyUntilEnd();

    // Restore original mutability after loop body
    if (originalIterableInfo != null) {
      _variables[iterableSourceVarName!] = originalIterableInfo;
    }

    var forInInnerScope = _variables.KeysSince(forInOuterScope);
    var loopCtx = _loopStack.Peek();
    PopScope();
    _loopStack.Pop();
    ExpectEndLabel(loopSourceLabel);

    // Branch back to header (skip if all paths in the body terminated)
    if (_currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      var loopScopeVars = WithForInResultVar(loopCtx, forInInnerScope);
      _currentBlock.AddOp(new MaxonScopeEndOp(loopScopeVars) { VarMetadata = _variables.GetScopeEndVarMetadata() });
      _currentBlock.AddOp(new MaxonBrOp(headerLabel));
    }

    // Create exit block
    var exitBlock = _currentFunction!.Body.AddBlock(exitLabel);
    _currentBlock = exitBlock;
  }

  private void ValidateStrideableConformance(MaxonValueKind kind, MaxonValue value, Token forToken) {
    if (kind is MaxonValueKind.Integer) return; // integers always implement Strideable

    if (kind == MaxonValueKind.Struct && value is MaxonStruct ms) {
      if (_typeRegistry.TryGetValue(ms.TypeName, out var regType) && regType is MlirStructType st
          && st.ConformingInterfaces.Contains("Strideable")) {
        return;
      }
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Type '{ms.TypeName}' does not implement Strideable (required for range expressions)",
        forToken.Line, forToken.Column);
    }

    throw new CompileError(ErrorCode.SemanticTypeMismatch,
      "Range expressions require a type that implements Strideable",
      forToken.Line, forToken.Column);
  }

  // Desugars `for i in start to/upto end 'label'` into a while loop:
  //   var __range_current = start
  //   while __range_current <= end 'label'  (or < for upto)
  //     let i = __range_current
  //     __range_current = __range_current + 1  (or advancedBy(1) for structs)
  //     <body>
  //   end 'label'
  private void ParseRangeForLoop(string itemName, MaxonValue startValue, MaxonValue endValue,
      MaxonValueKind elementKind, bool inclusive, string loopSourceLabel, Token forToken,
      string? structTypeName) {
    var loopLabel = UniqueLabel(loopSourceLabel);
    var headerLabel = $"{loopLabel}.header";
    var bodyLabel = loopLabel;
    var exitLabel = $"{loopLabel}.exit";

    // Store end bound for comparison in header
    var endVarName = $"__range_end_{_blockCounter}";
    _currentBlock!.AddOp(new MaxonAssignOp(endVarName, endValue, isDeclaration: true, isMutable: false, elementKind));
    _variables.Declare(endVarName, elementKind, false, endValue, _currentBlock!, structTypeName: structTypeName);

    // Create mutable counter initialized to start
    var counterVarName = $"__range_current_{_blockCounter}";
    _currentBlock!.AddOp(new MaxonAssignOp(counterVarName, startValue, isDeclaration: true, isMutable: true, elementKind));
    _variables.Declare(counterVarName, elementKind, true, startValue, _currentBlock!, structTypeName: structTypeName);

    // Branch to header
    _currentBlock!.AddOp(new MaxonBrOp(headerLabel));

    // Header block: compare counter with end bound
    var headerBlock = _currentFunction!.Body.AddBlock(headerLabel);
    _currentBlock = headerBlock;

    var currentVal = EmitVarRefOp(counterVarName, elementKind, structTypeName);
    var endVal = EmitVarRefOp(endVarName, elementKind, structTypeName);

    MaxonValue condResult;
    var cmpOp2 = inclusive ? MaxonBinOperator.Le : MaxonBinOperator.Lt;

    if (elementKind == MaxonValueKind.Struct && structTypeName != null) {
      // Use Comparable.compare() for struct types
      var compareMethodName = $"{structTypeName}.compare";
      var cmpToken = new Token(TokenType.Identifier, compareMethodName, forToken.Line, forToken.Column);
      var callOp = CreateFunctionCall(cmpToken, [currentVal, endVal]);
      condResult = EmitOrderingCheck(callOp.Result!, cmpOp2);
    } else {
      var cmpBinOp = new MaxonBinOp(cmpOp2, currentVal, endVal, elementKind);
      _currentBlock!.AddOp(cmpBinOp);
      condResult = cmpBinOp.Result;
    }

    headerBlock.AddOp(new MaxonCondBrOp(condResult, bodyLabel, exitLabel));

    // Body block
    var bodyBlock = _currentFunction!.Body.AddBlock(bodyLabel);
    _currentBlock = bodyBlock;
    var rangeOuterScope = _variables.SnapshotKeys();
    PushScope();

    // Increment happens in a separate block after the body so that the loop
    // variable's stack slot is not overwritten before the body uses it.
    // continue jumps to the increment block (not the header) so the counter
    // still advances.
    var incrLabel = $"{loopLabel}.incr";
    _loopStack.Push(new LoopContext(loopSourceLabel, incrLabel, exitLabel, rangeOuterScope,
        RangeCounterVarName: counterVarName, RangeElementKind: elementKind, RangeStructTypeName: structTypeName));

    // Bind loop variable: let i = __range_current
    var loadedCurrent = EmitVarRefOp(counterVarName, elementKind, structTypeName);
    _currentBlock!.AddOp(new MaxonAssignOp(itemName, loadedCurrent, isDeclaration: true, isMutable: false, elementKind));
    _variables.Declare(itemName, elementKind, false, loadedCurrent, bodyBlock, structTypeName: structTypeName);

    // Parse the body statements
    ParseBodyUntilEnd();
    var rangeInnerScope = _variables.KeysSince(rangeOuterScope);
    PopScope();
    _loopStack.Pop();
    ExpectEndLabel(loopSourceLabel);

    // Branch to increment block (unless body ended with a terminator)
    if (_currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonScopeEndOp(rangeInnerScope) { VarMetadata = _variables.GetScopeEndVarMetadata() });
      _currentBlock.AddOp(new MaxonBrOp(incrLabel));
    }

    // Increment block: advance counter and jump back to header
    var incrBlock = _currentFunction!.Body.AddBlock(incrLabel);
    _currentBlock = incrBlock;

    var oneLit = new MaxonLiteralOp(1L);
    _currentBlock!.AddOp(oneLit);
    EmitRangeCounterAdvance(counterVarName, elementKind, structTypeName, oneLit.Result, forToken);

    _currentBlock!.AddOp(new MaxonBrOp(headerLabel));

    // Exit block
    var exitBlock = _currentFunction!.Body.AddBlock(exitLabel);
    _currentBlock = exitBlock;
  }

  private void ParseBreak() {
    var token = Advance(); // consume 'break'
    var (exitLabel, scopeVars, _) = ResolveBreakTarget(token);
    var breakInnerScope = _variables.KeysSince(scopeVars);
    _currentBlock!.AddOp(new MaxonScopeEndOp(breakInnerScope) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock!.AddOp(new MaxonBrOp(exitLabel));
  }

  private void ParseContinue() {
    var token = Advance(); // consume 'continue'
    var loop = ResolveLoopTarget(token);
    var continueInnerScope = _variables.KeysSince(loop.ScopeVars);
    var continueScopeVars = WithForInResultVar(loop, continueInnerScope);
    _currentBlock!.AddOp(new MaxonScopeEndOp(continueScopeVars) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock!.AddOp(new MaxonBrOp(loop.HeaderLabel));
  }

  private void ParseSkip() {
    var token = Advance(); // consume 'skip'

    var skipCountExpr = ParseExpression();
    var skipCountValue = ResolveExprValue(skipCountExpr);

    if (_loopStack.Count == 0) {
      throw new CompileError(ErrorCode.ParserUnexpectedToken,
        "'skip' can only be used inside a loop", token.Line, token.Column);
    }
    var loop = _loopStack.Peek();

    if (loop.RangeCounterVarName != null) {
      EmitRangeSkip(token, skipCountValue, loop);
      return;
    }

    if (loop.IterVarName == null) {
      throw new CompileError(ErrorCode.ParserSkipOutsideIteratorLoop,
        "'skip' can only be used inside a for loop", token.Line, token.Column);
    }

    // Generate the skip loop: call next() n times, exiting the for loop if exhausted
    var skipLabel = UniqueLabel("skip");
    var skipHeaderLabel = $"{skipLabel}.header";
    var skipBodyLabel = $"{skipLabel}.body";
    var skipDoneLabel = $"{skipLabel}.done";

    // Store skip count in a mutable counter variable
    var skipCounterVar = $"__skip_counter_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(skipCounterVar, skipCountValue, isDeclaration: true, isMutable: true, MaxonValueKind.Integer));
    _variables.Declare(skipCounterVar, MaxonValueKind.Integer, true, skipCountValue, _currentBlock!);

    _currentBlock!.AddOp(new MaxonBrOp(skipHeaderLabel));

    // Skip header: check if counter > 0
    var skipHeaderBlock = _currentFunction!.Body.AddBlock(skipHeaderLabel);
    _currentBlock = skipHeaderBlock;

    var counterVal = EmitVarRefOp(skipCounterVar, MaxonValueKind.Integer, null);
    var zeroLit = new MaxonLiteralOp(0L);
    skipHeaderBlock.AddOp(zeroLit);
    var cmpOp = new MaxonBinOp(MaxonBinOperator.Gt, counterVal, zeroLit.Result, MaxonValueKind.Integer);
    skipHeaderBlock.AddOp(cmpOp);
    skipHeaderBlock.AddOp(new MaxonCondBrOp(cmpOp.Result, skipBodyLabel, skipDoneLabel));

    // Skip body: call try next() on the iterator, decrement counter
    var skipBodyBlock = _currentFunction!.Body.AddBlock(skipBodyLabel);
    _currentBlock = skipBodyBlock;

    // Load the iterator struct ref
    var iterRef = new MaxonStructVarRefOp(loop.IterVarName, loop.IterableTypeName!);
    skipBodyBlock.AddOp(iterRef);

    // Call try next(self) to advance the iterator
    var tryCallOp = new MaxonTryCallOp(loop.NextMethodName!, [iterRef.Result], loop.ElementKind!.Value, loop.ElementStructTypeName);
    skipBodyBlock.AddOp(tryCallOp);

    // Store discarded result so scope_end can release it
    string? discardVar = null;
    if (tryCallOp.Result != null) {
      discardVar = $"__skip_discard_{_blockCounter++}";
      skipBodyBlock.AddOp(new MaxonAssignOp(discardVar, tryCallOp.Result, true, true, loop.ElementKind!.Value));
      _variables.Declare(discardVar, loop.ElementKind!.Value, true, tryCallOp.Result, skipBodyBlock, structTypeName: loop.ElementStructTypeName);
    }

    // Check if iterator is exhausted
    var zeroLit2 = new MaxonLiteralOp(0L);
    skipBodyBlock.AddOp(zeroLit2);
    var errCmp = new MaxonBinOp(MaxonBinOperator.Eq, tryCallOp.ErrorFlag, zeroLit2.Result, MaxonValueKind.Integer);
    skipBodyBlock.AddOp(errCmp);

    // Create a block for the "still has elements" path
    var skipDecrLabel = $"{skipLabel}.decr";
    skipBodyBlock.AddOp(new MaxonCondBrOp(errCmp.Result, skipDecrLabel, loop.ExitLabel));

    // Decrement counter, release discarded value, and loop back
    var skipDecrBlock = _currentFunction!.Body.AddBlock(skipDecrLabel);
    _currentBlock = skipDecrBlock;

    var currentCount = EmitVarRefOp(skipCounterVar, MaxonValueKind.Integer, null);
    var oneLit = new MaxonLiteralOp(1L);
    skipDecrBlock.AddOp(oneLit);
    var subOp = new MaxonBinOp(MaxonBinOperator.Sub, currentCount, oneLit.Result, MaxonValueKind.Integer);
    skipDecrBlock.AddOp(subOp);
    skipDecrBlock.AddOp(new MaxonAssignOp(skipCounterVar, subOp.Result, isDeclaration: false, isMutable: true, MaxonValueKind.Integer));
    // Release the discarded next() result before looping back — the next
    // iteration will overwrite the slot, so we must decref+zero it now.
    if (discardVar != null) {
      skipDecrBlock.AddOp(new MaxonScopeEndOp([discardVar]) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    }
    skipDecrBlock.AddOp(new MaxonBrOp(skipHeaderLabel));

    // Skip done: branch to the loop header (like continue)
    var skipDoneBlock = _currentFunction!.Body.AddBlock(skipDoneLabel);
    _currentBlock = skipDoneBlock;
    var skipInnerScope = _variables.KeysSince(loop.ScopeVars);
    var skipScopeVars = WithForInResultVar(loop, skipInnerScope);
    skipDoneBlock.AddOp(new MaxonScopeEndOp(skipScopeVars) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    skipDoneBlock.AddOp(new MaxonBrOp(loop.HeaderLabel));
  }

  private void EmitRangeSkip(Token token, MaxonValue skipCountValue, LoopContext loop) {
    EmitRangeCounterAdvance(loop.RangeCounterVarName!, loop.RangeElementKind!.Value, loop.RangeStructTypeName, skipCountValue, token);

    var rangeSkipInnerScope = _variables.KeysSince(loop.ScopeVars);
    _currentBlock!.AddOp(new MaxonScopeEndOp(rangeSkipInnerScope) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock!.AddOp(new MaxonBrOp(loop.HeaderLabel));
  }

  private void EmitRangeCounterAdvance(string counterVarName, MaxonValueKind elementKind, string? structTypeName, MaxonValue stepValue, Token token) {
    var currentVal = EmitVarRefOp(counterVarName, elementKind, structTypeName);

    if (elementKind == MaxonValueKind.Struct && structTypeName != null) {
      var advancedByName = $"{structTypeName}.advancedBy";
      var advToken = new Token(TokenType.Identifier, advancedByName, token.Line, token.Column);
      var advCall = CreateFunctionCall(advToken, [currentVal, stepValue]);
      // Remove the __call_tmp_ added by EmitCallReturnTempAssign — ownership
      // transfers directly to the counter variable, preventing a leak when
      // the temp is overwritten on each loop iteration.
      var lastOp = _currentBlock!.Operations[^1];
      if (lastOp is MaxonAssignOp { IsDeclaration: true } tmpAssign && tmpAssign.VarName.StartsWith("__call_tmp_")) {
        _currentBlock!.Operations.RemoveAt(_currentBlock!.Operations.Count - 1);
        _variables.Remove(tmpAssign.VarName);
      }
      _currentBlock!.AddOp(new MaxonAssignOp(counterVarName, advCall.Result!, isDeclaration: false, isMutable: true, elementKind));
    } else {
      var addOp = new MaxonBinOp(MaxonBinOperator.Add, currentVal, stepValue, elementKind);
      _currentBlock!.AddOp(addOp);
      _currentBlock!.AddOp(new MaxonAssignOp(counterVarName, addOp.Result, isDeclaration: false, isMutable: true, elementKind));
    }
  }

  /// Prepends the for-in result variable to the scope var list when the loop
  /// iterates over heap-allocated elements (structs, type parameters, associated-value enums).
  /// The result var is declared before the loop's scope snapshot so KeysSince doesn't
  /// capture it, but it must be released at every loop-exit path.
  private static List<string> WithForInResultVar(LoopContext loop, List<string> innerScope) {
    var resultVar = loop.ForInResultVarName;
    if (resultVar != null
        && loop.ElementKind is MaxonValueKind.Struct or MaxonValueKind.TypeParameter or MaxonValueKind.Enum) {
      return [resultVar, .. innerScope];
    }
    return innerScope;
  }

  private (string ExitLabel, HashSet<string> ScopeVars, bool IsLoop) ResolveBreakTarget(Token keyword) {
    if (_matchStack.Count == 0 && _loopStack.Count == 0) {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"'{keyword.Value}' can only be used inside a loop or match", keyword.Line, keyword.Column);
    }
    if (Check(TokenType.CharacterLiteral)) {
      var labelToken = Advance();
      foreach (var ctx in _matchStack) {
        if (ctx.SourceLabel == labelToken.Value) return (ctx.MergeLabel, ctx.ScopeVars, false);
      }
      foreach (var ctx in _loopStack) {
        if (ctx.SourceLabel == labelToken.Value) {
          // Only redundant if it's the innermost loop AND there's no intervening match
          // (with a match on the stack, unlabeled break would target the match, not the loop)
          if (_loopStack.Peek() == ctx && _matchStack.Count == 0)
            throw new CompileError(ErrorCode.ParserRedundantLoopLabel,
              $"'break' with label '{labelToken.Value}' targets its own loop; use 'break' without a label, or 'break' with the label of an outer loop",
              labelToken.Line, labelToken.Column);
          return (ctx.ExitLabel, ctx.ScopeVars, true);
        }
      }
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"No enclosing loop or match with label '{labelToken.Value}'", labelToken.Line, labelToken.Column);
    }
    // Unlabeled break: prefer match if we're inside one, otherwise loop
    if (_matchStack.Count > 0) { var m = _matchStack.Peek(); return (m.MergeLabel, m.ScopeVars, false); }
    var l = _loopStack.Peek();
    return (l.ExitLabel, l.ScopeVars, true);
  }

  private LoopContext ResolveLoopTarget(Token keyword) {
    if (_loopStack.Count == 0) {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"'{keyword.Value}' can only be used inside a loop", keyword.Line, keyword.Column);
    }
    if (Check(TokenType.CharacterLiteral)) {
      var labelToken = Advance();
      foreach (var ctx in _loopStack) {
        if (ctx.SourceLabel == labelToken.Value) {
          if (_loopStack.Peek() == ctx)
            throw new CompileError(ErrorCode.ParserRedundantLoopLabel,
              $"'continue' with label '{labelToken.Value}' targets its own loop; use 'continue' without a label, or 'continue' with the label of an outer loop",
              labelToken.Line, labelToken.Column);
          return ctx;
        }
      }
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"No enclosing loop with label '{labelToken.Value}'", labelToken.Line, labelToken.Column);
    }
    return _loopStack.Peek();
  }

  private static bool BlockEndsWithTerminator(MlirBlock<MaxonOp> block) {
    if (block.Operations.Count == 0) return false;
    var lastOp = block.Operations[^1];
    return lastOp is MaxonReturnOp or MaxonBrOp or MaxonCondBrOp or MaxonThrowOp or MaxonPanicOp or MaxonPanicDynamicOp;
  }

  /// <summary>
  /// Returns true if the block ends with an unconditional branch to the given label.
  /// Used to detect break-to-merge in match cases.
  /// </summary>
  private static bool BlockEndsBranchingToLabel(MlirBlock<MaxonOp> block, string label) {
    if (block.Operations.Count == 0) return false;
    return block.Operations[^1] is MaxonBrOp br && br.Target == label;
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
  // Pattern for enum/union case: matches ordinal/raw value and optionally binds payload values
  private sealed record EnumCasePattern(int Ordinal, string CaseName, object? RawValue, List<(string Name, int Line, int Column)>? Bindings,
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
      string? enumTypeName, MlirUnionType? enumType,
      HashSet<string> seenPatternKeys, HashSet<string> seenEnumCases,
      MaxonValueKind compareKind, string? structTypeName) {
    var patterns = new List<MatchPattern>();
    while (true) {
      var patternLine = Current().Line;
      var patternCol = Current().Column;

      if (CheckIdentifierLike() && enumType != null
          && PeekNext().Type != TokenType.Dot
          && enumType.GetCase(Current().Value) != null) {

        // Bare case name pattern for enums/unions: red, empty, value(n), etc.
        var caseNameToken = Advance();
        var enumCase = enumType.GetCase(caseNameToken.Value)!;

        // Check for range pattern: caseName to/upto caseName
        if (Check(TokenType.To) || Check(TokenType.Upto)) {
          bool upperInclusive = Check(TokenType.To);
          Advance(); // consume 'to' or 'upto'

          var upperCaseToken = ExpectIdentifierLike();
          var upperCase = enumType.GetCase(upperCaseToken.Value)
            ?? throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
              $"unknown union case: '{upperCaseToken.Value}'",
              upperCaseToken.Line, upperCaseToken.Column);

          var rangeDisplayName = $"{caseNameToken.Value} {(upperInclusive ? "to" : "upto")} {upperCaseToken.Value}";
          if (!seenPatternKeys.Add(rangeDisplayName)) {
            throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
              $"duplicate pattern in match: '{rangeDisplayName}'",
              patternLine, patternCol);
          }

          // Mark all covered cases for exhaustiveness checking, detecting overlaps
          var lowerOrdinal = enumCase.Ordinal;
          var upperOrdinal = upperCase.Ordinal;
          foreach (var c in enumType.Cases) {
            if (c.Ordinal >= lowerOrdinal && (upperInclusive ? c.Ordinal <= upperOrdinal : c.Ordinal < upperOrdinal)) {
              if (!seenEnumCases.Add(c.Name)) {
                throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
                  $"overlapping pattern in match: '{c.Name}' is already covered",
                  patternLine, patternCol);
              }
            }
          }

          if (enumType.BackingType == MlirType.F64) {
            patterns.Add(new RangePattern(
              new FloatRangeBound((double)enumCase.RawValue!),
              new FloatRangeBound((double)upperCase.RawValue!),
              upperInclusive, rangeDisplayName, patternLine, patternCol));
          } else {
            patterns.Add(new RangePattern(
              new IntRangeBound(enumCase.Ordinal),
              new IntRangeBound(upperCase.Ordinal),
              upperInclusive, rangeDisplayName, patternLine, patternCol));
          }
        } else {
          var displayName = caseNameToken.Value;
          if (!seenPatternKeys.Add(displayName)) {
            throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
              $"duplicate pattern in match: '{displayName}'",
              patternLine, patternCol);
          }
          if (!seenEnumCases.Add(caseNameToken.Value)) {
            throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
              $"overlapping pattern in match: '{displayName}' is already covered",
              patternLine, patternCol);
          }

          List<(string Name, int Line, int Column)>? bindings = null;
          if (Check(TokenType.LeftParen)) {
            Advance(); // consume '('
            bindings = [];
            while (!Check(TokenType.RightParen) && !IsAtEnd()) {
              var bindingToken = Expect(TokenType.Identifier);
              if (bindingToken.Value == "_") {
                bindings.Add(($"__discard_{_discardCounter++}", bindingToken.Line, bindingToken.Column));
              } else {
                bindings.Add((bindingToken.Value, bindingToken.Line, bindingToken.Column));
              }
              if (Check(TokenType.Comma)) Advance();
            }
            Expect(TokenType.RightParen);

            // Validate binding count matches associated value count
            var expectedCount = enumCase.AssociatedValues?.Count ?? 0;
            if (bindings.Count != expectedCount) {
              throw new CompileError(ErrorCode.SemanticUnionWrongBindingCount,
                $"wrong binding count: '{caseNameToken.Value}'",
                caseNameToken.Line, caseNameToken.Column);
            }
          }

          patterns.Add(new EnumCasePattern(enumCase.Ordinal, caseNameToken.Value, enumCase.RawValue, bindings,
            enumCase.AssociatedValues, displayName, patternLine, patternCol));
        }
      } else if (CheckIdentifierLike() && enumType != null
          && PeekNext().Type != TokenType.Dot
          && enumType.GetCase(Current().Value) == null
          && (PeekNext().Type == TokenType.LeftParen || PeekNext().Type == TokenType.Then || PeekNext().Type == TokenType.Gives)) {
        // Bare identifier in enum/union match that is NOT a valid case name
        throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
          $"unknown union case: '{Current().Value}'",
          patternLine, patternCol);
      } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Dot) {
        // Enum pattern: Type.case
        var enumTypeToken = Advance();
        Advance(); // consume '.'
        var caseNameToken = ExpectIdentifierLike();
        var patternEnumName = enumTypeToken.Value;

        if (enumTypeName == null || patternEnumName != enumTypeName) {
          throw new CompileError(ErrorCode.ParserUnexpectedToken,
            $"pattern type '{patternEnumName}' does not match scrutinee type",
            enumTypeToken.Line, enumTypeToken.Column);
        }

        // Qualified case names (Type.case) are not allowed in match — use bare case names
        throw new CompileError(ErrorCode.SemanticMatchQualifiedCaseName,
          $"use '{caseNameToken.Value}' instead of '{patternEnumName}.{caseNameToken.Value}' in match",
          enumTypeToken.Line, enumTypeToken.Column);
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
        var resolvedCharValue = StringUtils.ResolveEscapes(charToken.Value);
        patterns.Add(ParseCharPatternOrRange(resolvedCharValue, seenPatternKeys, patternLine, patternCol));
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
      var lower = compareKind is MaxonValueKind.Float or MaxonValueKind.Float32
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
    if (compareKind is MaxonValueKind.Float or MaxonValueKind.Float32) {
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
      var resolvedUpperValue = StringUtils.ResolveEscapes(upperToken.Value);
      var lower = new CharRangeBound(charValue);
      var upper = new CharRangeBound(resolvedUpperValue);
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
      if (compareKind is MaxonValueKind.Float or MaxonValueKind.Float32) {
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

  private static bool IsNumericKind(MaxonValueKind kind) =>
    kind is MaxonValueKind.Integer or MaxonValueKind.Float or MaxonValueKind.Float32
        or MaxonValueKind.Byte or MaxonValueKind.Short;

  private static string KindToTypeName(MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => "int",
    MaxonValueKind.Float => "float",
    MaxonValueKind.Float32 => "f32",
    MaxonValueKind.Bool => "bool",
    MaxonValueKind.Byte => "byte",
    MaxonValueKind.Short => "int",
    MaxonValueKind.Struct => "struct",
    MaxonValueKind.Enum => "union",
    MaxonValueKind.Function => "function",
    _ => throw new InvalidOperationException($"Unknown value kind: {kind}")
  };

  /// Maps primitive value kinds to their MlirType. Returns null for non-primitive kinds (Struct, Enum, Function).
  private static MlirType? KindToMlirType(MaxonValueKind kind) => kind switch {
    MaxonValueKind.Integer => MlirType.I64,
    MaxonValueKind.Float => MlirType.F64,
    MaxonValueKind.Float32 => MlirType.F32,
    MaxonValueKind.Bool => MlirType.I1,
    MaxonValueKind.Byte => MlirType.I8,
    MaxonValueKind.Short => MlirType.I16,
    MaxonValueKind.Struct => null,
    MaxonValueKind.Enum => null,
    MaxonValueKind.Function => null,
    _ => throw new InvalidOperationException($"Unknown value kind: {kind}")
  };

  private static string ArgTypeName(MaxonValue value, MaxonValueKind kind) {
    return value switch {
      MaxonStruct ms => ms.TypeName,
      MaxonEnum me => me.TypeName,
      _ => KindToTypeName(kind)
    };
  }

  /// <summary>
  /// Checks if argTypeName is compatible with paramTypeName, considering typealiases.
  /// A typealias like StringArray (source: Array) is compatible with Array.
  /// </summary>
  private bool IsStructTypeCompatible(string argTypeName, string paramTypeName) {
    if (argTypeName == paramTypeName) return true;
    // Resolve both sides to their monomorphized concrete names so aliases match
    var argResolved = ResolveConcreteTypeName(argTypeName);
    var paramResolved = ResolveConcreteTypeName(paramTypeName);
    if (argResolved == paramResolved) return true;
    if (argResolved == paramTypeName || argTypeName == paramResolved) return true;
    _typeAliasSources.TryGetValue(argTypeName, out var argSource);
    _typeAliasSources.TryGetValue(paramTypeName, out var paramSource);
    // Check if arg type is a typealias whose source matches the param type
    if (argSource == paramTypeName) return true;
    // Check if param type is a typealias whose source matches the arg type
    if (paramSource == argTypeName) return true;
    // Both are aliases of the same source: compatible only if type params match
    if (argSource != null && paramSource != null && argSource == paramSource) {
      return HaveMatchingTypeParams(argTypeName, paramTypeName);
    }
    // Interface alias: accept any arg type that conforms to the required interface
    if (_typeRegistry.TryGetValue(paramTypeName, out var paramTypeEntry)
        && paramTypeEntry is MlirStructType paramStruct
        && paramStruct.IsInterfaceAlias) {
      return ArgConformsToInterfaceAlias(argTypeName, paramStruct);
    }
    return false;
  }

  private bool ArgConformsToInterfaceAlias(string argTypeName, MlirStructType interfaceAlias) {
    var requiredInterface = interfaceAlias.ConformingInterfaces[0];
    if (!TypeConformsToInterface(argTypeName, requiredInterface))
      return false;
    // If the interface alias has concrete type params, verify they match
    if (interfaceAlias.TypeParams.Count > 0
        && _typeRegistry.TryGetValue(argTypeName, out var argTypeEntry)
        && argTypeEntry is MlirStructType argStruct) {
      foreach (var (paramName, requiredType) in interfaceAlias.TypeParams) {
        if (requiredType is MlirTypeParameterType) continue;
        if (!argStruct.TypeParams.TryGetValue(paramName, out var argParamType))
          return false;
        if (argParamType is MlirTypeParameterType) continue;
        if (argParamType.Name != requiredType.Name)
          return false;
      }
    }
    return true;
  }

  private bool HaveMatchingTypeParams(string typeA, string typeB) {
    if (!_typeRegistry.TryGetValue(typeA, out var typeAEntry) || typeAEntry is not MlirStructType structA) return false;
    if (!_typeRegistry.TryGetValue(typeB, out var typeBEntry) || typeBEntry is not MlirStructType structB) return false;
    if (structA.TypeParams.Count != structB.TypeParams.Count) return false;
    foreach (var (key, valueA) in structA.TypeParams) {
      if (!structB.TypeParams.TryGetValue(key, out var valueB)) return false;
      if (valueA is MlirTypeParameterType || valueB is MlirTypeParameterType) continue;
      // Normalize ranged primitives to base type for comparison (e.g., Byte → i8)
      var nameA = valueA is MlirRangedPrimitiveType rptA ? rptA.BaseType.Name : valueA.Name;
      var nameB = valueB is MlirRangedPrimitiveType rptB ? rptB.BaseType.Name : valueB.Name;
      if (nameA != nameB) return false;
    }
    return true;
  }

  /// <summary>
  /// Emits payload binding variables for associated-value enum match patterns.
  /// For each EnumCasePattern with bindings, extracts payload values from the
  /// original enum scrutinee and declares them as local variables.
  /// </summary>
  private void EmitUnionCaseBindings(List<MatchPattern> patterns, string origEnumTempName, string enumTypeName, bool scrutineeMutable, string? scrutineeVarName = null) {
    foreach (var pattern in patterns) {
      if (pattern is not EnumCasePattern { Bindings: { } bindings, AssociatedValues: { } assocValues }) continue;

      // Load the original enum value from the cross-block temp variable
      var enumVarRef = new MaxonEnumVarRefOp(origEnumTempName, enumTypeName, MaxonValueKind.Integer);
      _currentBlock!.AddOp(enumVarRef);

      for (int i = 0; i < bindings.Count; i++) {
        var (bindingName, bindingLine, bindingCol) = bindings[i];
        var assocType = assocValues[i].Type;
        var bindingKind = assocType.ToValueKind();
        string? structTypeName = assocType is MlirStructType st ? st.Name
          : assocType is MlirUnionType et ? et.Name : null;

        var payloadOp = new MaxonEnumPayloadOp(enumVarRef.Result, enumTypeName, i, bindingKind, structTypeName);
        _currentBlock!.AddOp(payloadOp);

        // Write-back targets the original variable so mutations are visible after the match
        var payloadBinding = scrutineeMutable && scrutineeVarName != null
          ? new EnumPayloadBinding(scrutineeVarName, enumTypeName, i)
          : null;

        _currentBlock!.AddOp(new MaxonAssignOp(bindingName, payloadOp.Result,
          isDeclaration: true, isMutable: scrutineeMutable, bindingKind));
        _variables.Declare(bindingName, bindingKind, scrutineeMutable, payloadOp.Result, _currentBlock!, structTypeName: structTypeName, payloadBinding: payloadBinding);
        if (!bindingName.StartsWith("__discard_")) {
          _localVarLocations.Add((bindingName, bindingLine, bindingCol));
        }
      }
    }
  }

  /// <summary>
  /// Emits comparison ops for a set of patterns against the scrutinee value.
  /// Multiple patterns are combined with logical OR.
  /// Returns the combined boolean comparison result.
  /// </summary>
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
        EmitLiteralTempAssign((MaxonStruct)strLit.Result);
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
        EmitLiteralTempAssign((MaxonStruct)charLit.Result);
        var equalsMethodName = $"{structTypeName}.equals";
        var equalsToken = new Token(TokenType.Identifier, equalsMethodName, pattern.Line, pattern.Column);
        var callOp = CreateFunctionCall(equalsToken, [refOp.Result, charLit.Result]);
        return callOp.Result!;
      }
      case EnumCasePattern enumCasePat: {
        // Compare tag/raw value against the case's value
        var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
        _currentBlock!.AddOp(refOp);
        MaxonLiteralOp patLit;
        if (enumCasePat.RawValue is double dv) {
          patLit = new MaxonLiteralOp(dv);
        } else if (enumCasePat.RawValue is long lv) {
          patLit = new MaxonLiteralOp(lv);
        } else {
          patLit = new MaxonLiteralOp((long)enumCasePat.Ordinal);
        }
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
      EmitLiteralTempAssign(charLit.Result);
      var compareMethodName = $"{structTypeName}.compare";
      var compareToken = new Token(TokenType.Identifier, compareMethodName, rangePat.Line, rangePat.Column);
      var callOp = CreateFunctionCall(compareToken, [refOp.Result, charLit.Result]);
      lowerCmp = EmitOrderingCheck(callOp.Result!, MaxonBinOperator.Ge);
    }

    if (rangePat.Upper is CharRangeBound upperBound) {
      var refOp = new MaxonStructVarRefOp(scrutTempName, structTypeName);
      _currentBlock!.AddOp(refOp);
      var charLit = new MaxonCharLiteralOp(upperBound.Value, structTypeName);
      _currentBlock!.AddOp(charLit);
      EmitLiteralTempAssign(charLit.Result);
      var compareMethodName = $"{structTypeName}.compare";
      var compareToken = new Token(TokenType.Identifier, compareMethodName, rangePat.Line, rangePat.Column);
      var callOp = CreateFunctionCall(compareToken, [refOp.Result, charLit.Result]);
      upperCmp = EmitOrderingCheck(callOp.Result!, rangePat.UpperInclusive ? MaxonBinOperator.Le : MaxonBinOperator.Lt);
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
  private (MaxonValue CompareVal, MaxonValueKind CompareKind, string? EnumTypeName, MlirUnionType? EnumType, string? StructTypeName)
      ResolveScrutinee(MaxonValue scrutineeVal) {
    if (scrutineeVal is MaxonEnum enumVal) {
      var enumTypeName = enumVal.TypeName;
      var enumType = (MlirUnionType)_typeRegistry[enumTypeName];
      if (enumType.HasAssociatedValues) {
        // For associated-value enums, extract the tag for comparison
        var tagOp = new MaxonEnumTagOp(scrutineeVal, enumTypeName);
        _currentBlock!.AddOp(tagOp);
        return (tagOp.Result, MaxonValueKind.Integer, enumTypeName, enumType, null);
      }
      var backingKind = GetUnionBackingKind(enumType);
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
      string? enumTypeName, MlirUnionType? enumType, string? structTypeName)
      SetupMatchScrutinee(MaxonValue scrutineeVal, string matchLabel) {
    string? origEnumTempName = null;
    if (scrutineeVal is MaxonEnum origEnum) {
      var origEnumType = (MlirUnionType)_typeRegistry[origEnum.TypeName];
      if (origEnumType.HasAssociatedValues) {
        origEnumTempName = $"__match_enum_{matchLabel}";
      }
    }
    var (compareVal, compareKind, enumTypeName, enumType, structTypeName) = ResolveScrutinee(scrutineeVal);
    if (origEnumTempName != null) {
      _currentBlock!.AddOp(new MaxonAssignOp(origEnumTempName, scrutineeVal, isDeclaration: true, isMutable: false, MaxonValueKind.Enum));
      _variables.Declare(origEnumTempName, MaxonValueKind.Enum, false, scrutineeVal, _currentBlock!, OwnershipFlags.IsTemp);
    }
    return (origEnumTempName, compareVal, compareKind, enumTypeName, enumType, structTypeName);
  }

  /// <summary>
  /// Validates enum exhaustiveness — all cases must be covered, or a 'default throws' clause must be present.
  /// 'default' without 'throws' is not allowed on enums.
  /// </summary>
  private static void ValidateEnumExhaustiveness(
      MlirUnionType? enumType, string? enumTypeName,
      bool hasDefaultThrows, HashSet<string> seenEnumCases, Token errorToken) {
    if (enumType == null) return;
    // 'default throws' covers all unmatched cases — no exhaustiveness check needed
    if (hasDefaultThrows) return;
    var missingCases = enumType.Cases
      .Where(c => !seenEnumCases.Contains(c.Name))
      .Select(c => c.Name)
      .ToList();
    if (missingCases.Count > 0) {
      throw new CompileError(ErrorCode.ParserMatchNotExhaustive,
        $"match on {DeclKeyword(enumType)} '{enumTypeName}' is not exhaustive, missing: {string.Join(", ", missingCases)}",
        errorToken.Line, errorToken.Column);
    }
  }

  /// <summary>
  /// Parses and emits a 'default throws' case for an enum match.
  /// The 'throws' token must not yet be consumed. On return, the case is fully emitted
  /// and the caller should increment caseIndex and continue to the next iteration.
  /// </summary>
  private void ParseDefaultThrowsCase(
      Token defaultToken, string matchLabel, int caseIndex,
      List<MlirBlock<MaxonOp>?> cmpBlocks, List<MlirBlock<MaxonOp>> caseBlocks,
      List<bool> caseIsDefault, List<bool>? caseFallthrough, List<List<string>>? caseOuterScopes) {
    Advance(); // consume 'throws'
    var throwLabel = $"{matchLabel}.case{caseIndex}";
    cmpBlocks.Add(null);
    var throwBlock = _currentFunction!.Body.AddBlock(throwLabel);
    _currentBlock = throwBlock;
    var throwExpr = ParseExpression();
    var errorValue = ResolveExprValue(throwExpr);
    if (errorValue is not MaxonEnum enumVal) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch, "throws requires an error enum value", defaultToken.Line, defaultToken.Column);
    }
    _currentBlock.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    _currentBlock.AddOp(new MaxonThrowOp(errorValue, enumVal.TypeName));
    caseBlocks.Add(throwBlock);
    caseIsDefault.Add(true);
    caseFallthrough?.Add(false);
    caseOuterScopes?.Add([]);
  }

  /// <summary>
  /// Parses and emits a 'default panic("message")' case for a match.
  /// The 'panic' token must not yet be consumed.
  /// </summary>
  private void ParseDefaultPanicCase(
      Token defaultToken, string matchLabel, int caseIndex,
      List<MlirBlock<MaxonOp>?> cmpBlocks, List<MlirBlock<MaxonOp>> caseBlocks,
      List<bool> caseIsDefault, List<bool>? caseFallthrough, List<List<string>>? caseOuterScopes) {
    Advance(); // consume 'panic'
    Expect(TokenType.LeftParen);
    var msgToken = Current();

    var sourceFileName = _sourceFilePath != null ? Path.GetFileName(_sourceFilePath) : "unknown";
    var prefix = $"panic at {sourceFileName}:{defaultToken.Line}: ";

    var panicLabel = $"{matchLabel}.case{caseIndex}";
    cmpBlocks.Add(null);
    var panicBlock = _currentFunction!.Body.AddBlock(panicLabel);
    _currentBlock = panicBlock;
    _currentBlock.AddOp(new MaxonScopeEndOp(GetScopeEndVars()) { VarMetadata = _variables.GetScopeEndVarMetadata() });

    if (msgToken.Type == TokenType.StringInterp) {
      Advance(); // consume interpolated string
      Expect(TokenType.RightParen);
      var prefixedToken = new Token(TokenType.StringInterp, prefix + msgToken.Value + "\\n", msgToken.Line, msgToken.Column);
      var interpResult = EmitStringLiteralWithInterpolation(prefixedToken);
      _currentBlock.AddOp(new MaxonPanicDynamicOp(interpResult));
    } else if (msgToken.Type == TokenType.StringLiteral) {
      Advance(); // consume string literal
      Expect(TokenType.RightParen);
      var message = prefix + msgToken.Value;
      _currentBlock.AddOp(new MaxonPanicOp(message));
    } else {
      throw new CompileError(ErrorCode.SemanticTypeMismatch, "panic requires a string argument", msgToken.Line, msgToken.Column);
    }

    caseBlocks.Add(panicBlock);
    caseIsDefault.Add(true);
    caseFallthrough?.Add(false);
    caseOuterScopes?.Add([]);
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
    var scrutineeMutable = _lastExprWasMutableVar;
    var scrutineeVarName = _lastExprVarName;
    var (origEnumTempName, compareVal, compareKind, enumTypeName, enumType, structTypeName) =
      SetupMatchScrutinee(scrutineeVal, matchLabel);

    var entryBlock = _currentBlock!;

    // Store scrutinee in a temp var so comparison blocks can reference it across blocks
    var scrutTempName = $"__match_{matchLabel}";
    entryBlock.AddOp(new MaxonAssignOp(scrutTempName, compareVal, isDeclaration: true, isMutable: false, compareKind));
    _variables.Declare(scrutTempName, compareKind, false, compareVal, entryBlock, OwnershipFlags.IsTemp, structTypeName: structTypeName);

    var mergeLabel = $"{matchLabel}.merge";
    var caseBlocks = new List<MlirBlock<MaxonOp>>();
    var caseEndBlocks = new List<MlirBlock<MaxonOp>?>();
    var caseIsDefault = new List<bool>();
    var caseFallthrough = new List<bool>();
    var caseOuterScopes = new List<List<string>>();
    // One entry per case: non-default cases have their comparison block, default has null
    var cmpBlocks = new List<MlirBlock<MaxonOp>?>();
    var seenPatternKeys = new HashSet<string>();
    var seenEnumCases = new HashSet<string>();
    bool hasDefault = false;
    bool defaultSeen = false;

    _matchStack.Push(new MatchContext(sourceLabel, mergeLabel, _variables.SnapshotKeys()));
    int caseIndex = 0;
    while (!IsMatchBlockEnd(enumType) && !IsAtEnd()) {
      SkipNewlines();
      if (IsMatchBlockEnd(enumType)) break;

      var caseLine = Current().Line;
      var caseCol = Current().Column;
      bool isDefault = false;
      var patterns = new List<MatchPattern>();

      if (Check(TokenType.Default)) {
        var defaultToken = Advance(); // consume 'default'
        isDefault = true;
        hasDefault = true;
        defaultSeen = true;

        // For enum/union matches, 'default' must be followed by 'throws <error>' or 'panic("message")'.
        if (enumType != null) {
          if (Check(TokenType.Throws)) {
            ParseDefaultThrowsCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough, caseOuterScopes);
          } else if (Check(TokenType.Panic)) {
            ParseDefaultPanicCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough, caseOuterScopes);
          } else {
            throw new CompileError(ErrorCode.ParserMatchDefaultUnionMustThrow,
              $"'default' in a match on {DeclKeyword(enumType)} '{enumTypeName}' must be followed by 'throws <error>' or 'panic(\"message\")'",
              defaultToken.Line, defaultToken.Column);
          }
          caseEndBlocks.Add(null); // throws/panic always terminate — no merge block
          caseIndex++;
          SkipNewlines();
          continue;
        }

        // For non-enum matches, 'default throws' and 'default panic' are allowed
        if (Check(TokenType.Throws)) {
          ParseDefaultThrowsCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough, caseOuterScopes);
          caseEndBlocks.Add(null);
          caseIndex++;
          SkipNewlines();
          continue;
        }
        if (Check(TokenType.Panic)) {
          ParseDefaultPanicCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough, caseOuterScopes);
          caseEndBlocks.Add(null);
          caseIndex++;
          SkipNewlines();
          continue;
        }
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
        var varsBeforeCmp = _variables.SnapshotKeys();
        var combinedCmp = EmitPatternComparison(patterns, scrutTempName, compareKind, structTypeName);
        // Clean up any managed temporaries created during pattern comparison
        // (e.g. string/char literals) before the conditional branch
        var cmpTemps = _variables.KeysSince(varsBeforeCmp);
        if (cmpTemps.Count > 0) {
          cmpBlock.AddOp(new MaxonScopeEndOp(cmpTemps) { VarMetadata = _variables.GetScopeEndVarMetadata() });
          foreach (var t in cmpTemps) _variables.Remove(t);
        }
        cmpBlock.AddOp(new MaxonCondBrOp(combinedCmp, caseBodyLabel, ""));
        cmpBlocks.Add(cmpBlock);
      } else {
        cmpBlocks.Add(null);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
      }

      _currentBlock = caseBodyBlock;
      var caseOuterScope = _variables.SnapshotKeys();
      PushScope();

      // Emit payload bindings for associated-value enum patterns
      if (origEnumTempName != null) {
        EmitUnionCaseBindings(patterns, origEnumTempName, enumTypeName!, scrutineeMutable, scrutineeVarName);
      }

      bool caseHasReturn = Check(TokenType.Return);
      ParseStatement();
      var caseInnerScope = _variables.KeysSince(caseOuterScope);
      PopScope();

      // After ParseStatement, _currentBlock may differ from caseBodyBlock if the
      // statement was a nested match (or other branching construct). The nested
      // construct's merge block becomes the effective exit point for this case arm.
      var caseEndBlock = _currentBlock;

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
      caseEndBlocks.Add(caseEndBlock);
      caseIsDefault.Add(isDefault);
      caseFallthrough.Add(hasFallthrough);
      caseOuterScopes.Add(caseInnerScope);

      caseIndex++;
      SkipNewlines();
    }
    _matchStack.Pop();

    var endToken = ExpectMatchEndLabel(sourceLabel);
    ValidateEnumExhaustiveness(enumType, enumTypeName, hasDefault, seenEnumCases, endToken);
    if (enumType == null && !hasDefault) {
      throw new CompileError(ErrorCode.ParserMatchNotExhaustive,
        "match is not exhaustive: add a 'default' arm",
        endToken.Line, endToken.Column);
    }

    // Always create merge block - case bodies that don't terminate branch here
    var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);

    PatchComparisonChain(entryBlock, matchLabel, cmpBlocks, caseIsDefault, mergeLabel);

    // Wire case body blocks: branch to merge or fallthrough to next case body
    bool allTerminate = true;
    bool anyBranchesToMerge = false;
    for (int i = 0; i < caseBlocks.Count; i++) {
      var caseBlock = caseBlocks[i];
      var endBlock = caseEndBlocks[i];
      if (!BlockEndsWithTerminator(caseBlock)) {
        // Simple case: case body block doesn't branch away — emit scope_end + branch here
        allTerminate = false;
        anyBranchesToMerge = true;
        caseBlock.AddOp(new MaxonScopeEndOp(caseOuterScopes[i]) { VarMetadata = _variables.GetScopeEndVarMetadata() });
        if (caseFallthrough[i] && i + 1 < caseBlocks.Count) {
          caseBlock.AddOp(new MaxonBrOp($"{matchLabel}.case{i + 1}"));
        } else {
          caseBlock.AddOp(new MaxonBrOp(mergeLabel));
        }
      } else if (endBlock != null && endBlock != caseBlock && !BlockEndsWithTerminator(endBlock)) {
        // Nested construct (e.g. inner match): the case body block branches into the
        // nested construct's comparison chain, and flow exits through the nested
        // construct's merge block (endBlock). Emit scope_end for the outer arm's
        // bindings here so they are properly decreffed.
        allTerminate = false;
        anyBranchesToMerge = true;
        if (caseOuterScopes[i].Count > 0) {
          endBlock.AddOp(new MaxonScopeEndOp(caseOuterScopes[i]) { VarMetadata = _variables.GetScopeEndVarMetadata() });
        }
        if (caseFallthrough[i] && i + 1 < caseBlocks.Count) {
          endBlock.AddOp(new MaxonBrOp($"{matchLabel}.case{i + 1}"));
        } else {
          endBlock.AddOp(new MaxonBrOp(mergeLabel));
        }
      } else if (BlockEndsBranchingToLabel(caseBlock, mergeLabel)) {
        // Case ends with a branch to merge (e.g. break) — flow continues after match
        anyBranchesToMerge = true;
      }
    }

    // Don't remove scrutTempName or origEnumTempName — let the function
    // scope-end clean them up. Removing them here prevents cleanup when
    // flow continues past the match (non-terminating case bodies).

    // Code after match is unreachable only if all cases terminate AND none branch to merge
    _currentBlock = (allTerminate && !anyBranchesToMerge) ? null : mergeBlock;
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
    _variables.Declare(resultVarName, resultKind, true, zeroLit.Result, entryBlock);

    // Store scrutinee for cross-block access
    var scrutTempName = $"__match_{matchLabel}";
    entryBlock.AddOp(new MaxonAssignOp(scrutTempName, compareVal, isDeclaration: true, isMutable: false, compareKind));
    _variables.Declare(scrutTempName, compareKind, false, compareVal, entryBlock, OwnershipFlags.IsTemp, structTypeName: structTypeName);

    var mergeLabel = $"{matchLabel}.merge";
    var caseBlocks = new List<MlirBlock<MaxonOp>>();
    var caseIsDefault = new List<bool>();
    var cmpBlocks = new List<MlirBlock<MaxonOp>?>();
    var seenPatternKeys = new HashSet<string>();
    var seenEnumCases = new HashSet<string>();
    bool hasDefault = false;
    bool defaultSeen = false;
    string? resultStructTypeName = null;

    int caseIndex = 0;
    while (!IsMatchBlockEnd(enumType) && !IsAtEnd()) {
      SkipNewlines();
      if (IsMatchBlockEnd(enumType)) break;

      var caseLine = Current().Line;
      var caseCol = Current().Column;
      bool isDefault = false;
      var patterns = new List<MatchPattern>();

      if (Check(TokenType.Default)) {
        var defaultToken = Advance(); // consume 'default'
        isDefault = true;
        hasDefault = true;
        defaultSeen = true;

        // For enum/union matches, 'default' must be followed by 'throws <error>' or 'panic("message")'.
        if (enumType != null) {
          if (Check(TokenType.Throws)) {
            ParseDefaultThrowsCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough: null, caseOuterScopes: null);
          } else if (Check(TokenType.Panic)) {
            ParseDefaultPanicCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough: null, caseOuterScopes: null);
          } else {
            throw new CompileError(ErrorCode.ParserMatchDefaultUnionMustThrow,
              $"'default' in a match on {DeclKeyword(enumType)} '{enumTypeName}' must be followed by 'throws <error>' or 'panic(\"message\")'",
              defaultToken.Line, defaultToken.Column);
          }
          caseIndex++;
          SkipNewlines();
          continue;
        }

        // For non-enum matches, 'default throws' and 'default panic' are allowed
        if (Check(TokenType.Throws)) {
          ParseDefaultThrowsCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough: null, caseOuterScopes: null);
          caseIndex++;
          SkipNewlines();
          continue;
        }
        if (Check(TokenType.Panic)) {
          ParseDefaultPanicCase(defaultToken, matchLabel, caseIndex, cmpBlocks, caseBlocks, caseIsDefault, caseFallthrough: null, caseOuterScopes: null);
          caseIndex++;
          SkipNewlines();
          continue;
        }
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
        var varsBeforeCmp = _variables.SnapshotKeys();
        var combinedCmp = EmitPatternComparison(patterns, scrutTempName, compareKind, structTypeName);
        // Clean up any managed temporaries created during pattern comparison
        var cmpTemps = _variables.KeysSince(varsBeforeCmp);
        if (cmpTemps.Count > 0) {
          cmpBlock.AddOp(new MaxonScopeEndOp(cmpTemps) { VarMetadata = _variables.GetScopeEndVarMetadata() });
          foreach (var t in cmpTemps) _variables.Remove(t);
        }
        cmpBlock.AddOp(new MaxonCondBrOp(combinedCmp, caseBodyLabel, ""));
        cmpBlocks.Add(cmpBlock);
      } else {
        cmpBlocks.Add(null);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
      }

      _currentBlock = caseBodyBlock;

      // Emit payload bindings for associated-value enum patterns (read-only in match expressions)
      if (origEnumTempName != null) {
        EmitUnionCaseBindings(patterns, origEnumTempName, enumTypeName!, scrutineeMutable: false);
      }

      var caseValue = ResolveExprValue(ParseExpression());
      resultKind = DetermineValueKind(caseValue);

      // Update variable info to reflect the actual result type (may differ from initial Integer placeholder)
      resultStructTypeName = caseValue is MaxonEnum me ? me.TypeName
        : caseValue is MaxonStruct ms ? ms.TypeName : null;
      _variables[resultVarName] = new VarInfo(resultVarName, resultKind, true, caseValue, entryBlock, StructTypeName: resultStructTypeName);

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
    if (enumType == null && !hasDefault) {
      throw new CompileError(ErrorCode.ParserMatchNotExhaustive,
        "match expression is not exhaustive: add a 'default' arm",
        endToken.Line, endToken.Column);
    }

    var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);
    PatchComparisonChain(entryBlock, matchLabel, cmpBlocks, caseIsDefault, mergeLabel);

    _currentBlock = mergeBlock;

    // Don't remove scrutTempName, origEnumTempName, or resultVarName — let
    // the function scope-end clean them up.  Removing resultVarName here
    // would skip the decref for the match-expression temporary, leaking it.

    var resultValue = EmitVarRefOp(resultVarName, resultKind, resultStructTypeName);

    return new ExprResult.Direct(resultValue);
  }

  // ============================================================================
  // Expression parsing
  // ============================================================================

  private ExprResult ParseExpression(int minPrecedence = 0) {
    var lhs = ParsePrimary();

    // Handle 'as' cast expressions (postfix, binds tighter than binary ops)
    while (Check(TokenType.As)) {
      var asToken = Advance(); // consume 'as'
      _lastCastRangedType = null;
      var targetKind = ParseTypeKeyword();
      var inputVal = ResolveExprValue(lhs);
      var sourceKind = DetermineValueKind(inputVal);
      ValidateCast(sourceKind, targetKind, asToken);
      var castOp = new MaxonCastOp(inputVal, targetKind);
      _currentBlock!.AddOp(castOp);
      // When casting to a ranged type, propagate the type name
      if (_lastCastRangedType != null) {
        _lastRangedTypeName = _lastCastRangedType.Name;
        _lastCastRangedType = null;
      }
      lhs = new ExprResult.Direct(castOp.Result);
    }

    while ((BinaryOperators.TryGetValue(Current().Type, out var entry) && entry.Precedence >= minPrecedence)
        || (Current().Type == TokenType.Is && 3 >= minPrecedence)) {
      // 'and fallthrough' is match syntax, not a binary expression
      if (Current().Type == TokenType.And && PeekNext().Type == TokenType.Fallthrough) break;

      // Reference identity: 'is' / 'is not'
      if (Current().Type == TokenType.Is) {
        lhs = ParseRefIdentity(lhs);
        continue;
      }

      var opToken = Advance(); // consume operator
      var rhs = ParseExpression(entry.Precedence + 1);

      // Type parameter operands require where-clause constraints for comparison operators
      ValidateTypeParameterConstraints(lhs, rhs, entry.Op, opToken);

      MaxonValue lhsVal = ResolveExprValue(lhs);
      MaxonValue rhsVal = ResolveExprValue(rhs);

      // Coerce string/char-backed constants to their backing struct type so struct
      // equality/ordering checks below can match them against plain String/Character values.
      if (lhsVal is MaxonEnum && TryCoerceConstantsToBackingType(lhsVal, out var lhsCoerced, out _) && lhsCoerced is MaxonStruct)
        lhsVal = lhsCoerced!;
      if (rhsVal is MaxonEnum && TryCoerceConstantsToBackingType(rhsVal, out var rhsCoerced, out _) && rhsCoerced is MaxonStruct)
        rhsVal = rhsCoerced!;

      // Coerce character literals to codepoint integers when the other operand is an integer
      {
        if (lhsVal is MaxonStruct { TypeName: "Character" } && IsIntegerLikeKind(DetermineValueKind(rhsVal)))
          TryCoerceCharLiteralToCodepoint(ref lhsVal);
        if (rhsVal is MaxonStruct { TypeName: "Character" } && IsIntegerLikeKind(DetermineValueKind(lhsVal)))
          TryCoerceCharLiteralToCodepoint(ref rhsVal);
      }

      // Constrained type parameter comparison: field (Struct kind from where-clause
      // promotion) vs parameter (TypeParameter/Integer kind). Both reference the same
      // type parameter and are i64 at runtime — emit integer comparison.
      // This applies to all comparison operators including ==/!= because at the generic
      // level, type parameters are always i64 — Equatable dispatch only applies to
      // concrete struct types resolved at instantiation time.
      {
        bool isTypeParamStruct(MaxonValue v) =>
          v is MaxonStruct ms && _typeRegistry.TryGetValue(ms.TypeName, out var reg) && reg is MlirTypeParameterType;
        if (isTypeParamStruct(lhsVal) || isTypeParamStruct(rhsVal)) {
          var tpBinOp = new MaxonBinOp(entry.Op, lhsVal, rhsVal, MaxonValueKind.Integer);
          _currentBlock!.AddOp(tpBinOp);
          lhs = new ExprResult.Direct(tpBinOp.Result);
          continue;
        }
      }

      // Struct equality/inequality via Equatable interface
      if (entry.Op is MaxonBinOperator.Eq or MaxonBinOperator.Ne
          && lhsVal is MaxonStruct lhsStruct && rhsVal is MaxonStruct rhsStruct
          && IsStructTypeCompatible(lhsStruct.TypeName, rhsStruct.TypeName)) {
        if (_typeRegistry[lhsStruct.TypeName] is MlirStructType structType && structType.ConformingInterfaces.Contains("Equatable")) {
          if (!_isStdlib && _typeRegistry[lhsStruct.TypeName] is MlirUnionType enumT && enumT is not MlirEnumType) {
            var opStr = entry.Op == MaxonBinOperator.Eq ? "==" : "!=";
            throw new CompileError(ErrorCode.SemanticUnionNotComparable,
              $"cannot compare union values with '{opStr}', use 'match' instead",
              opToken.Line, opToken.Column);
          }
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
        } else {
          var opStr = entry.Op == MaxonBinOperator.Eq ? "==" : "!=";
          throw new CompileError(ErrorCode.SemanticEqRequiresEquatable,
            $"'{opStr}' requires type '{lhsStruct.TypeName}' to implement 'Equatable'",
            opToken.Line, opToken.Column);
        }
      }

      // Struct ordering via Comparable interface
      if (entry.Op is MaxonBinOperator.Lt or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge
          && lhsVal is MaxonStruct lhsCmpStruct && rhsVal is MaxonStruct rhsCmpStruct
          && IsStructTypeCompatible(lhsCmpStruct.TypeName, rhsCmpStruct.TypeName)) {
        if (_typeRegistry[lhsCmpStruct.TypeName] is MlirStructType cmpStructType && cmpStructType.ConformingInterfaces.Contains("Comparable")) {
          var compareMethodName = $"{lhsCmpStruct.TypeName}.compare";
          var compareToken = new Token(TokenType.Identifier, compareMethodName, opToken.Line, opToken.Column);
          var callOp = CreateFunctionCall(compareToken, [lhsVal, rhsVal]);
          lhs = new ExprResult.Direct(EmitOrderingCheck(callOp.Result!, entry.Op));
          continue;
        }
      }

      MaxonValueKind kind;
      MaxonValue promotedLhs, promotedRhs;
      var resolvedOp = entry.Op;

      if (entry.Op is MaxonBinOperator.And or MaxonBinOperator.Or or MaxonBinOperator.BitXor) {
        var lhsKind = DetermineValueKind(lhsVal);
        var rhsKind = DetermineValueKind(rhsVal);
        if (lhsKind == MaxonValueKind.Bool && rhsKind == MaxonValueKind.Bool) {
          kind = MaxonValueKind.Bool;
        } else if (lhsKind == MaxonValueKind.Integer && rhsKind == MaxonValueKind.Integer) {
          kind = MaxonValueKind.Integer;
          resolvedOp = entry.Op switch {
            MaxonBinOperator.And => MaxonBinOperator.BitAnd,
            MaxonBinOperator.Or => MaxonBinOperator.BitOr,
            MaxonBinOperator.BitXor => MaxonBinOperator.BitXor,
            _ => entry.Op
          };
        } else {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"Operator '{opToken.Value}' requires both operands to be the same type (both bool or both int)",
            opToken.Line, opToken.Column);
        }
        promotedLhs = lhsVal;
        promotedRhs = rhsVal;
      } else {
        (kind, promotedLhs, promotedRhs) = DetermineValueKind(lhsVal, rhsVal, entry.Op, opToken);

        // Enum values cannot be compared with == or != - must use match
        // (Constants are excluded: they allow direct comparison)
        if (!_isStdlib && kind == MaxonValueKind.Enum && (entry.Op is MaxonBinOperator.Eq or MaxonBinOperator.Ne)) {
          var enumTypeName2 = lhsVal is MaxonEnum le2 ? le2.TypeName : ((MaxonEnum)rhsVal).TypeName;
          if (!(_typeRegistry.TryGetValue(enumTypeName2, out var enumTypeEntry2) && enumTypeEntry2 is MlirEnumType)) {
            var opStr = entry.Op == MaxonBinOperator.Eq ? "==" : "!=";
            throw new CompileError(ErrorCode.SemanticUnionNotComparable,
              $"cannot compare union values with '{opStr}', use 'match' instead",
              opToken.Line, opToken.Column);
          }
        }

        // Enum comparisons use the backing kind (Integer or Float)
        if (kind == MaxonValueKind.Enum) {
          var cmpEnumTypeName = lhsVal is MaxonEnum le ? le.TypeName : ((MaxonEnum)rhsVal).TypeName;
          var cmpEnumType = (MlirUnionType)_typeRegistry[cmpEnumTypeName];
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
            kind = GetUnionBackingKind(cmpEnumType);
          }
        }

        // int / int now produces int (truncating division)
        // int / float or float / int still produces float
      }

      MlirType? optimalType = null;
      if (kind == MaxonValueKind.Integer) {
        optimalType = GetOptimalType(promotedLhs) ?? GetOptimalType(promotedRhs);
      }
      var binOp = new MaxonBinOp(resolvedOp, promotedLhs, promotedRhs, kind, optimalType);
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

    if (Check(TokenType.Async)) {
      var asyncToken = Advance(); // consume 'async'
      return ParseAsyncCall(asyncToken);
    }

    if (Check(TokenType.Await)) {
      var awaitToken = Advance(); // consume 'await'
      return ParseAwaitExpression(awaitToken);
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
      var zeroOp = kind is MaxonValueKind.Float or MaxonValueKind.Float32
        ? new MaxonLiteralOp(0.0, kind)
        : new MaxonLiteralOp(0L);
      _currentBlock!.AddOp(zeroOp);
      var subOp = new MaxonBinOp(MaxonBinOperator.Sub, zeroOp.Result, innerVal, kind);
      _currentBlock!.AddOp(subOp);
      return new ExprResult.Direct(subOp.Result);
    }

    if (Check(TokenType.LeftBracket)) {
      var bracketToken = Current();
      Advance(); // consume '['
      SkipNewlines();

      if (Check(TokenType.RightBracket)) {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          "Empty array literals are not supported; use a typed empty array like 'IntArray{}' instead",
          Current().Line, Current().Column);
      }

      // Parse first element to determine if this is a map or array literal
      var firstExpr = ResolveExprValue(ParseExpression());
      SkipNewlines();

      ExprResult.Direct result;
      if (Check(TokenType.Colon)) {
        // Map literal: [key: value, ...]
        result = ParseMapLiteral(firstExpr, bracketToken);
      } else {
        // Array literal: [expr, expr, ...]
        result = ParseArrayLiteralWithFirstElement(firstExpr);
      }

      // Handle chained member access: [1,2,3].get(0) or ["a": 1].get("a")
      if (Check(TokenType.Dot)) {
        return ParseFieldAccessChain(result, bracketToken);
      }
      return result;
    }

    if (Check(TokenType.LeftParen)) {
      // Need to distinguish between:
      // 1. Closure: (param type, ...) gives expr
      // 2. Tuple literal: (expr, expr, ...)
      // 3. Parenthesized expression: (expr)
      if (IsClosure()) {
        return ParseClosure();
      }
      var parenToken = Advance(); // consume '('
      var first = ParseExpression();
      if (Check(TokenType.Comma)) {
        // Tuple literal: (expr, expr, ...)
        var elements = new List<MaxonValue> { ResolveExprValue(first) };
        while (Check(TokenType.Comma)) {
          Advance(); // consume ','
          elements.Add(ResolveExprValue(ParseExpression()));
        }
        Expect(TokenType.RightParen);
        var elementTypes = elements.Select(InferType).ToList();
        var tupleType = GetOrCreateTupleType(elementTypes);
        var fieldValues = elements.Select((val, i) => ($"_{i}", val)).ToList();
        var structLiteral = new MaxonStructLiteralOp(tupleType.Name, fieldValues);
        _currentBlock!.AddOp(structLiteral);
        var result = new ExprResult.Direct(structLiteral.Result);
        if (Check(TokenType.Dot))
          return ParseFieldAccessChain(result, parenToken);
        return result;
      }
      // Parenthesized expression: (expr)
      Expect(TokenType.RightParen);
      return first;
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
      EmitLiteralTempAssign((MaxonStruct)result);
      if (Check(TokenType.Dot)) {
        return ParseFieldAccessChain(new ExprResult.Direct(result), token);
      }
      return new ExprResult.Direct(result);
    }

    if (Check(TokenType.ByteStringLiteral)) {
      var token = Advance();
      var result = EmitByteStringLiteral(token);
      EmitLiteralTempAssign((MaxonStruct)result);
      if (Check(TokenType.Dot)) {
        return ParseFieldAccessChain(new ExprResult.Direct(result), token);
      }
      return new ExprResult.Direct(result);
    }

    if (Check(TokenType.CharacterLiteral)) {
      var token = Advance();
      var result = EmitCharLiteral(token);
      EmitLiteralTempAssign((MaxonStruct)result);
      if (Check(TokenType.Dot)) {
        return ParseFieldAccessChain(new ExprResult.Direct(result), token);
      }
      return new ExprResult.Direct(result);
    }

    if (Check(TokenType.Not)) {
      var token = Advance(); // consume 'not'
      var inner = ParsePrimary();
      var innerVal = ResolveExprValue(inner);
      var kind = DetermineValueKind(innerVal);
      if (kind == MaxonValueKind.Bool) {
        var trueOp = new MaxonLiteralOp(true);
        _currentBlock!.AddOp(trueOp);
        var xorOp = new MaxonBinOp(MaxonBinOperator.BitXor, innerVal, trueOp.Result, MaxonValueKind.Bool);
        _currentBlock!.AddOp(xorOp);
        return new ExprResult.Direct(xorOp.Result);
      }
      if (kind is MaxonValueKind.Integer) {
        var allOnesOp = new MaxonLiteralOp(-1L);
        _currentBlock!.AddOp(allOnesOp);
        var xorOp = new MaxonBinOp(MaxonBinOperator.BitXor, innerVal, allOnesOp.Result, kind);
        _currentBlock!.AddOp(xorOp);
        return new ExprResult.Direct(xorOp.Result);
      }
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        "'not' requires a bool or int operand", token.Line, token.Column);
    }

    if (Check(TokenType.Self)) {
      var selfToken = Advance(); // consume 'self'
      var selfVarInfo = (ResolveVariable("self") as ResolvedVar.Local)?.Info
        ?? throw new CompileError(ErrorCode.SemanticUnexpectedToken, "'self' can only be used inside instance methods", selfToken.Line, selfToken.Column);
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

      // Check for ranged primitive construction: TypeName{value}
      if (_typeRegistry.TryGetValue(token.Value, out var regType) && regType is MlirRangedPrimitiveType rangedType && Check(TokenType.LeftBrace)) {
        _usedTypeAliases.Add(token.Value);
        return ParseRangedPrimitiveConstruction(token, rangedType);
      }

      // Check for struct literal: TypeName{...} or TypeName { ... }
      if (_typeRegistry.ContainsKey(token.Value) && Check(TokenType.LeftBrace)) {
        _usedTypeAliases.Add(token.Value);
        return ParseStructLiteral(token.Value);
      }

      // Check for "TypeName from [...]" syntax (BuiltinArrayLiteral or InitableFromArrayLiteral)
      if (_typeRegistry.ContainsKey(token.Value) && Check(TokenType.From)) {
        _usedTypeAliases.Add(token.Value);
        return ParseFromExpression(token);
      }

      // Check for enum case with keyword name: EnumType.keywordCase
      if (Check(TokenType.Dot) && PeekNext().Type != TokenType.Identifier
          && _typeRegistry.TryGetValue(token.Value, out var kwEnumEntry) && kwEnumEntry is MlirUnionType kwEnumType) {
        var kwMemberName = PeekNext().Value;
        var kwEnumCase = kwEnumType.GetCase(kwMemberName);
        if (kwEnumCase != null) {
          _usedTypeAliases.Add(token.Value);
          Advance(); // consume '.'
          Advance(); // consume keyword case name
          if (kwEnumType.HasAssociatedValues) {
            var args = new List<MaxonValue>();
            if (kwEnumCase.AssociatedValues is { Count: > 0 }) {
              Expect(TokenType.LeftParen);
              for (int ai = 0; ai < kwEnumCase.AssociatedValues.Count; ai++) {
                if (ai > 0) Expect(TokenType.Comma);
                var argExpr = ParseExpression();
                args.Add(ResolveExprValue(argExpr));
              }
              Expect(TokenType.RightParen);
            }
            var constructOp = new MaxonEnumConstructOp(token.Value, kwMemberName, kwEnumCase.Ordinal, args);
            if (_sourceFilePath != null)
              constructOp.SourceLocation = $"{Path.GetFileName(_sourceFilePath)}:{token.Line}";
            _currentBlock!.AddOp(constructOp);
            return ParseFieldAccessChain(new ExprResult.Direct(constructOp.Result), token);
          }
          MaxonEnumLiteralOp kwEnumLitOp;
          if (kwEnumType.BackingType == MlirType.F64)
            kwEnumLitOp = new MaxonEnumLiteralOp(token.Value, kwMemberName, (double)kwEnumCase.RawValue!);
          else if (kwEnumType.BackingType == MlirType.I64)
            kwEnumLitOp = new MaxonEnumLiteralOp(token.Value, kwMemberName, (long)kwEnumCase.RawValue!);
          else
            kwEnumLitOp = new MaxonEnumLiteralOp(token.Value, kwMemberName, (long)kwEnumCase.Ordinal);
          _currentBlock!.AddOp(kwEnumLitOp);
          return ParseFieldAccessChain(new ExprResult.Direct(kwEnumLitOp.Result), token);
        }
      }

      // Check for qualified name: TypeName.member
      if (Check(TokenType.Dot) && IsIdentifierLikeToken(PeekNext())) {
        var qualifiedName = $"{token.Value}.{PeekNext().Value}";

        // Check for builtin managed type static methods
        {
          var resolvedBase = ResolveBaseTypeName(token.Value);
          var nextMethod = PeekNext().Value;
          Func<string, (bool, MaxonValue?)>? dispatcher = resolvedBase switch {
            "__ManagedMemory" when nextMethod is "create" or "fromCString"
              => TryEmitBuiltinManagedMemoryStaticMethod,
            "__ManagedSocket" when nextMethod is "tcpConnect"
              => TryEmitBuiltinManagedSocketStaticMethod,
            "__ManagedFile" when nextMethod is "openRead" or "openWrite" or "openWriteExecutable" or "exists" or "delete" or "stat" or "statField" or "statFree"
              => TryEmitBuiltinManagedFileStaticMethod,
            "__ManagedDirectory" when nextMethod is "openSearch" or "exists" or "create" or "currentPath"
              => TryEmitBuiltinManagedDirectoryStaticMethod,
            "__ManagedList" when nextMethod is "create"
              => TryEmitBuiltinManagedListStaticMethod,
            _ => null,
          };
          if (dispatcher != null) {
            _usedTypeAliases.Add(token.Value);
            Advance(); // consume '.'
            var staticMethodToken = Advance(); // consume method name
            Expect(TokenType.LeftParen);
            var (handled, staticResult) = dispatcher(staticMethodToken.Value);
            if (handled && staticResult != null) {
              // ManagedList.create() needs the alias name for type parameter resolution
              if (resolvedBase == "__ManagedList" && staticResult is MaxonStruct listResult)
                listResult.TypeName = token.Value;
              return ParseFieldAccessChain(new ExprResult.Direct(staticResult), token);
            }
            throw new CompileError(ErrorCode.ParserExpectedExpression,
              $"Unknown static method '{staticMethodToken.Value}' on {resolvedBase}",
              staticMethodToken.Line, staticMethodToken.Column);
          }
        }

        // Check for __Builtins static methods
        if (token.Value == "__Builtins") {
          Advance(); // consume '.'
          var staticMethodToken = Advance(); // consume method name
          Expect(TokenType.LeftParen);
          var builtinResult = EmitBuiltinsStaticMethod(staticMethodToken);
          if (builtinResult != null)
            return ParseFieldAccessChain(new ExprResult.Direct(builtinResult), token);
          if (_inTryContext)
            return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"__Builtins.{staticMethodToken.Value} does not return a value",
            staticMethodToken.Line, staticMethodToken.Column);
        }

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
          return ParseFieldAccessChain(EmitGlobalLoad(qualifiedName, globalInfo), token);
        }

        // Check for enum case: EnumType.caseName
        if (_typeRegistry.TryGetValue(token.Value, out var typeEntry) && typeEntry is MlirUnionType enumType) {
          var memberName = PeekNext().Value;
          var enumCase = enumType.GetCase(memberName);
          if (enumCase != null) {
            _usedTypeAliases.Add(token.Value);
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
                  // Type-check the argument (skip for type parameters — checked after monomorphization)
                  var expectedType = enumCase.AssociatedValues[ai].Type;
                  if (expectedType is not MlirTypeParameterType) {
                    var actualKind = DetermineValueKind(argVal);
                    var expectedKind = expectedType.ToValueKind();
                    if (actualKind != expectedKind) {
                      var actualTypeName = argVal is MaxonStruct ms
                        ? ms.TypeName
                        : MlirType.FormatAsSourceName(actualKind.ToMlirType());
                      throw new CompileError(ErrorCode.SemanticTypeMismatch,
                        $"type mismatch: 'expected {MlirType.FormatAsSourceName(expectedType)}, got {actualTypeName}'",
                        Current().Line, Current().Column);
                    }
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
              if (_sourceFilePath != null)
                constructOp.SourceLocation = $"{Path.GetFileName(_sourceFilePath)}:{token.Line}";
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
          // allCases: static property returning Array of all enum cases
          if (memberName == "allCases") {
            if (enumType is not MlirEnumType)
              throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
                $"allCases is not available on union types", token.Line, token.Column);
            Advance(); // consume '.'
            Advance(); // consume 'allCases'
            return EmitEnumAllCases((MlirEnumType)enumType, token);
          }

          // If not followed by '(', it can't be a method call, so it's an unknown case name.
          if (_pos + 2 >= _tokens.Count || _tokens[_pos + 2].Type != TokenType.LeftParen) {
            Advance(); // consume '.'
            Advance(); // consume member name
            throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
              $"unknown {DeclKeyword(enumType)} case: '{memberName}'", token.Line, token.Column);
          }

          // fromRawValue requires raw values, which only enum has; fromName works on union too since
          // it constructs cases by name (including associated-value cases which have no rawValue).
          if (memberName == "fromRawValue" && enumType is not MlirEnumType)
            throw new CompileError(ErrorCode.SemanticUnionUnknownCase,
              $"unknown union case: 'fromRawValue'", token.Line, token.Column);
          if (memberName is "fromRawValue" or "fromName")
            return ParseUnionStaticMethod(enumType, memberName);
        }

        // Check for qualified function call: TypeName.method(...)
        // _pos is at '.', _pos+1 is 'method', _pos+2 should be '('
        if (_pos + 2 < _tokens.Count && _tokens[_pos + 2].Type == TokenType.LeftParen) {
          var resolvedQualified = ResolveMethodName(qualifiedName);

          if (resolvedQualified != null) {
            Advance(); // consume '.'
            var methodToken = Advance(); // consume method name
            Advance(); // consume '('
            var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedQualified, methodToken.Line, methodToken.Column);
            var (args, callee) = ParseCallArgs(qualifiedFuncToken);
            var callOp = CreateFunctionCall(qualifiedFuncToken, args, callee);
            OverrideCalleeForTypeAlias(callOp, token.Value, qualifiedName);
            if (callOp.Result != null)
              return ParseFieldAccessChain(new ExprResult.Direct(callOp.Result), methodToken);
            if (_inTryContext)
              return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
            throw new CompileError(ErrorCode.ParserExpectedExpression, $"Function '{resolvedQualified}' does not return a value", methodToken.Line, methodToken.Column);
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
        // Check for sibling method call: bare methodName() inside an instance method resolves to self.methodName()
        var siblingCallOp = TrySiblingMethodCall(token);
        if (siblingCallOp != null) {
          if (siblingCallOp.Result != null)
            return ParseFieldAccessChain(new ExprResult.Direct(siblingCallOp.Result), token);
          if (_inTryContext)
            return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
          throw new CompileError(ErrorCode.ParserExpectedExpression, $"Method '{token.Value}' does not return a value", token.Line, token.Column);
        }

        // Check for indirect call through function-typed variable
        if (ResolveVariable(token.Value) is ResolvedVar.Local(var fnVarInfo) && fnVarInfo.Kind == MaxonValueKind.Function) {
          Logger.Debug(LogCategory.Parser, $"  Indirect call through function variable: {token.Value}");
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
            } else if (fnType.ReturnType is MlirUnionType retEnumType) {
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
          return ParseFieldAccessChain(new ExprResult.Direct(callOp.Result), token);
        if (_inTryContext)
          return new ExprResult.Direct(new MaxonInteger(MlirContext.Current.NextId()));
        throw new CompileError(ErrorCode.ParserExpectedExpression, $"Function '{token.Value}' does not return a value", token.Line, token.Column);
      }

      // Check for top-level constant reference
      if (_topLevelConstants.TryGetValue(token.Value, out var constValue)) {
        return EmitConstantLiteral(constValue);
      }

      // Variable reference (local or global)
      switch (ResolveVariable(token.Value)) {
        case ResolvedVar.Local(var info):
          return ParseFieldAccessChain(new ExprResult.VarRef(token.Value, info), token);
        case ResolvedVar.Global(var info):
          return ParseFieldAccessChain(EmitGlobalLoad(token.Value, info), token);
        case null:
          break;
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

      throw CreateUndefinedVariableError(token.Value, token, ErrorCode.ParserExpectedExpression);
    }

    // Keywords used as variable names (e.g., parameter named 'with')
    if (CheckIdentifierLike()) {
      var token = Advance();
      return ParseFieldAccessChain(LoadVariable(token.Value, token), token);
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
      Token fieldToken;
      string fieldName;
      if (Check(TokenType.IntegerLiteral)) {
        // Tuple positional access: .0, .1, .2
        fieldToken = Advance();
        var index = ParseIntegerLiteral(fieldToken);
        fieldName = $"_{index}";
      } else {
        fieldToken = Expect(TokenType.Identifier);
        fieldName = fieldToken.Value;
      }

      string userTypeName;
      if (result is ExprResult.VarRef vr) {
        userTypeName = vr.Info.StructTypeName
          ?? (vr.Info.Kind is MaxonValueKind.Integer or MaxonValueKind.Float or MaxonValueKind.Float32 or MaxonValueKind.Bool or MaxonValueKind.Byte or MaxonValueKind.Short ? KindToTypeName(vr.Info.Kind) : null)
          ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Variable '{vr.VarName}' is not a struct or enum type", originToken.Line, originToken.Column);
      } else if (result is ExprResult.Direct d && d.Value is MaxonStruct ms) {
        userTypeName = ms.TypeName;
      } else if (result is ExprResult.Direct d2 && d2.Value is MaxonEnum me) {
        userTypeName = me.TypeName;
      } else {
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, "Cannot access field on non-struct value", fieldToken.Line, fieldToken.Column);
      }

      // Primitive types: only method calls are allowed, no field access
      if (PrimitiveTypes.ContainsKey(userTypeName)) {
        if (Check(TokenType.LeftParen)) {
          var qualifiedMethodName = $"{userTypeName}.{fieldName}";
          var resolvedMethodName = ResolveMethodName(qualifiedMethodName);
          if (resolvedMethodName != null) {
            Advance(); // consume '('
            var selfVal = ResolveExprValue(result);
            var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedMethodName, fieldToken.Line, fieldToken.Column);
            var (allArgs, callee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, selfVal);
            var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs, callee);
            result = new ExprResult.Direct(callOp.Result ?? selfVal);
            continue;
          }
        }
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Primitive type '{userTypeName}' has no method named '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      var registeredType = _typeRegistry[userTypeName];

      // Handle enum-specific access
      if (registeredType is MlirUnionType enumType) {
        // Unions (non-enum) don't have .name, .rawValue, or .ordinal — those belong to enum
        if (enumType is not MlirEnumType && fieldName is "name" or "rawValue" or "ordinal") {
          throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
            $"union type '{userTypeName}' has no property '{fieldName}'",
            fieldToken.Line, fieldToken.Column);
        }

        // .name access - returns case name as String
        if (fieldName == "name") {
          var enumVal = ResolveExprValue(result);
          var nameOp = new MaxonEnumNameOp(enumVal, userTypeName);
          _currentBlock!.AddOp(nameOp);
          EmitLiteralTempAssign(nameOp.Result);
          result = new ExprResult.Direct(nameOp.Result);
          continue;
        }

        // .rawValue access
        if (fieldName == "rawValue") {
          var enumVal = ResolveExprValue(result);
          if (enumType.BackingType is MlirStringBackingType) {
            var stringRawOp = new MaxonEnumStringRawValueOp(enumVal, userTypeName, isChar: false);
            _currentBlock!.AddOp(stringRawOp);
            EmitLiteralTempAssign(stringRawOp.Result);
            result = new ExprResult.Direct(stringRawOp.Result);
            continue;
          }
          if (enumType.BackingType is MlirCharBackingType) {
            var charRawOp = new MaxonEnumStringRawValueOp(enumVal, userTypeName, isChar: true);
            _currentBlock!.AddOp(charRawOp);
            EmitLiteralTempAssign(charRawOp.Result);
            result = new ExprResult.Direct(charRawOp.Result);
            continue;
          }
          var resultKind = GetUnionBackingKind(enumType);
          var rawValueOp = new MaxonEnumRawValueOp(enumVal, userTypeName, resultKind);
          _currentBlock!.AddOp(rawValueOp);
          result = new ExprResult.Direct(rawValueOp.Result);
          continue;
        }

        // .ordinal access - returns zero-based declaration position as int
        if (fieldName == "ordinal") {
          var enumVal = ResolveExprValue(result);
          var ordinalOp = new MaxonEnumOrdinalOp(enumVal, userTypeName);
          _currentBlock!.AddOp(ordinalOp);
          result = new ExprResult.Direct(ordinalOp.Result);
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

        var keyword = DeclKeyword(enumType);
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"{char.ToUpper(keyword[0])}{keyword[1..]} type '{userTypeName}' has no property or method named '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      // Type parameter: only method calls are allowed, resolved through where-constrained interfaces
      if (registeredType is MlirTypeParameterType) {
        if (Check(TokenType.LeftParen)) {
          var qualifiedMethodName = $"{userTypeName}.{fieldName}";
          var ifaceMethod = FindInterfaceMethod(fieldName, userTypeName);
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
            var (resultKind, resultStructTypeName) = ResolveInterfaceMethodReturn(ifaceMethod, userTypeName, fieldName, fieldToken);
            var callOp = new MaxonCallOp(qualifiedMethodName, args, resultKind, resultStructTypeName);
            _currentBlock!.AddOp(callOp);
            result = new ExprResult.Direct(callOp.Result ?? selfVal);
            continue;
          }
        }
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type parameter '{userTypeName}' has no method '{fieldName}'; add a where clause to constrain '{userTypeName}' to an interface that provides '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      // Ranged primitive type: delegate method calls to the base primitive type
      if (registeredType is MlirRangedPrimitiveType rangedPrimType) {
        var basePrimName = MlirType.FormatAsSourceName(rangedPrimType.BaseType);
        if (Check(TokenType.LeftParen)) {
          var qualifiedMethodName = $"{basePrimName}.{fieldName}";
          var resolvedMethodName = ResolveMethodName(qualifiedMethodName);
          if (resolvedMethodName != null) {
            Advance(); // consume '('
            var primVal = ResolveExprValue(result);
            var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedMethodName, fieldToken.Line, fieldToken.Column);
            var (allArgs, primCallee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, primVal);
            var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs, primCallee);
            result = new ExprResult.Direct(callOp.Result ?? primVal);
            continue;
          }
        }
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type '{userTypeName}' has no method '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      var structType = (MlirStructType)registeredType;

      // Check for method call: expr.method(...)
      if (Check(TokenType.LeftParen)) {
        // Try builtin type method
        var baseExprType = ResolveBaseTypeName(userTypeName);
        if (IsBuiltinMethodType(baseExprType)) {
          Advance(); // consume '('
          var structVal = ResolveExprValue(result);
          var (handled, chainResult) = TryEmitBuiltinTypeMethod(userTypeName, fieldName, structVal);
          if (handled) {
            result = new ExprResult.Direct(chainResult ?? structVal);
            continue;
          }
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Unknown method '{fieldName}' on builtin type '{baseExprType}'",
            fieldToken.Line, fieldToken.Column);
        }

        var qualifiedMethodName = $"{userTypeName}.{fieldName}";
        Logger.Debug(LogCategory.Parser, $"Resolving method: {qualifiedMethodName}");
        var resolvedMethodName = ResolveMethodName(qualifiedMethodName);
        Logger.Debug(LogCategory.Parser, $"Resolved to: {resolvedMethodName}");
        if (resolvedMethodName != null) {
          Advance(); // consume '('
          var methodStructVal = ResolveExprValue(result);
          var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedMethodName, fieldToken.Line, fieldToken.Column);
          var (allArgs, structCallee) = ParseInstanceMethodCallArgs(qualifiedFuncToken, methodStructVal);
          var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs, structCallee);
          result = new ExprResult.Direct(callOp.Result ?? methodStructVal);
          continue;
        }

      }

      var field = structType.GetField(fieldName);
      if (field == null) {
        var hint = FindUnsatisfiedConditionalExtension(userTypeName, fieldName);
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
          $"Type '{userTypeName}' has no field named '{fieldName}'{hint ?? ""}", fieldToken.Line, fieldToken.Column);
      }

      if (!field.IsExported && _currentTypeName != userTypeName) {
        throw new CompileError(ErrorCode.SemanticUnexportedFieldAccess, $"cannot access unexported field: '{fieldName}' outside of type '{userTypeName}'", fieldToken.Line, fieldToken.Column);
      }

      var fieldKind = field.Type.ToValueKind();
      var fieldStructName = field.Type is MlirStructType fst ? fst.Name
        : field.Type is MlirUnionType fet ? fet.Name
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

    var parts = ParseStringInterpParts(token, stringTypeName);

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

  /// <summary>
  /// Emits an optimized String.append operation. If the argument is an interpolated string,
  /// extracts the parts and emits MaxonStringAppendInterpOp to write them directly into the
  /// target buffer. Otherwise emits MaxonStringAppendOp for a plain string argument.
  /// </summary>
  private void EmitStringAppendBuiltin(MaxonValue selfValue, string selfVarName) {
    var stringTypeName = FindTypeImplementingInterface("BuiltinStringLiteral") ?? "String";

    if (Check(TokenType.StringInterp)) {
      // Interpolated string argument: extract parts and emit append-interp op
      var token = Advance();
      var parts = ParseStringInterpParts(token, stringTypeName);
      var appendOp = new MaxonStringAppendInterpOp(selfValue, parts) { SelfVarName = selfVarName };
      _currentBlock!.AddOp(appendOp);
    } else if (Check(TokenType.StringLiteral)) {
      // String literal: emit as a single-part append-interp
      var token = Advance();
      var literalText = StringUtils.ResolveEscapes(token.Value);
      var parts = new List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> {
        (true, literalText, null, null, null)
      };
      var appendOp = new MaxonStringAppendInterpOp(selfValue, parts) { SelfVarName = selfVarName };
      _currentBlock!.AddOp(appendOp);
    } else {
      // Expression argument (variable, function call, etc.): emit append op
      var argExpr = ParseExpression();
      var argValue = ResolveExprValue(argExpr);
      var appendOp = new MaxonStringAppendOp(selfValue, argValue) { SelfVarName = selfVarName };
      _currentBlock!.AddOp(appendOp);
    }
  }

  /// <summary>
  /// Parses string interpolation parts from a StringInterp token. Shared between
  /// EmitStringLiteralWithInterpolation and EmitStringAppendBuiltin.
  /// </summary>
  private List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)> ParseStringInterpParts(Token token, string stringTypeName) {
    var parts = new List<(bool IsLiteral, string? LiteralValue, MaxonValue? ExprValue, string? FormatSpec, MlirType? OptimalType)>();
    var text = token.Value;
    var pos = 0;
    var literalBuf = new System.Text.StringBuilder();

    while (pos < text.Length) {
      if (text[pos] == '\\' && pos + 1 < text.Length) {
        var nextChar = text[pos + 1];
        if (nextChar is '{' or '}') {
          literalBuf.Append(nextChar);
          pos += 2;
        } else if (nextChar == 'x') {
          var escLen = Math.Min(4, text.Length - pos);
          var escSeq = text.Substring(pos, escLen);
          try { literalBuf.Append(StringUtils.ResolveEscapes(escSeq)); } catch (InvalidEscapeException ex) {
            throw new CompileError(ErrorCode.LexerInvalidEscape,
                $"{ex.Message} in string interpolation", token.Line, token.Column + pos);
          }
          pos += 4;
        } else if (nextChar == 'u') {
          var escLen = Math.Min(6, text.Length - pos);
          var escSeq = text.Substring(pos, escLen);
          try { literalBuf.Append(StringUtils.ResolveEscapes(escSeq)); } catch (InvalidEscapeException ex) {
            throw new CompileError(ErrorCode.LexerInvalidEscape,
                $"{ex.Message} in string interpolation", token.Line, token.Column + pos);
          }
          pos += 6;
        } else {
          try { literalBuf.Append(StringUtils.ResolveEscapes($"\\{nextChar}")); } catch (InvalidEscapeException ex) {
            throw new CompileError(ErrorCode.LexerInvalidEscape,
                $"{ex.Message} in string interpolation", token.Line, token.Column + pos);
          }
          pos += 2;
        }
      } else if (text[pos] == '{') {
        if (literalBuf.Length > 0) {
          parts.Add((true, literalBuf.ToString(), null, null, (MlirType?)null));
          literalBuf.Clear();
        }
        pos++;
        var exprStart = pos;
        var braceDepth = 1;
        while (pos < text.Length && braceDepth > 0) {
          if (text[pos] == '{') braceDepth++;
          else if (text[pos] == '}') braceDepth--;
          if (braceDepth > 0) pos++;
        }
        var exprText = text[exprStart..pos];
        if (pos < text.Length) pos++;

        string? formatSpec = null;
        int parenDepth = 0;
        int colonIdx = -1;
        for (int ci = 0; ci < exprText.Length; ci++) {
          if (exprText[ci] == '(') parenDepth++;
          else if (exprText[ci] == ')') parenDepth--;
          else if (exprText[ci] == ':' && parenDepth == 0) { colonIdx = ci; break; }
        }
        if (colonIdx >= 0) {
          formatSpec = exprText[(colonIdx + 1)..];
          exprText = exprText[..colonIdx];
        }

        var (exprValue, _exprKind) = ParseInterpolationExpressionWithKind(exprText, token.Line, token.Column + 1 + exprStart);
        var exprOptimalType = GetOptimalType(exprValue);

        // Non-String structs need a toString call
        if (exprValue is MaxonStruct structVal && structVal.TypeName != stringTypeName) {
          var overloads = ResolveFunctionOverloads($"{structVal.TypeName}.toString");
          if (overloads.Count == 0)
            throw new CompileError(ErrorCode.SemanticPartialInterfaceImpl,
                $"Type '{structVal.TypeName}' used in string interpolation must have a toString method",
                token.Line, token.Column);

          var toStringFunc = formatSpec != null
            ? overloads.FirstOrDefault(f => f.ParamTypes.Count == 2) ?? overloads.First(f => f.ParamTypes.Count == 1)
            : overloads.First(f => f.ParamTypes.Count == 1);

          var callArgs = new List<MaxonValue> { structVal };
          if (formatSpec != null && toStringFunc.ParamTypes.Count == 2) {
            var fmtLiteral = new MaxonStringLiteralOp(formatSpec, stringTypeName);
            _currentBlock!.AddOp(fmtLiteral);
            callArgs.Add(fmtLiteral.Result);
            var fmtTempName = $"__interp_fmt_{fmtLiteral.Result.Id}";
            _currentBlock!.AddOp(new MaxonAssignOp(fmtTempName, fmtLiteral.Result, true, false, MaxonValueKind.Struct));
            _variables.Declare(fmtTempName, MaxonValueKind.Struct, false, fmtLiteral.Result, _currentBlock!, OwnershipFlags.IsTemp, structTypeName: stringTypeName);
            formatSpec = null;
          }

          var (resultKind, resultStructTypeName) = ResolveCallResultType(toStringFunc.ReturnType, callArgs);
          var callOp = new MaxonCallOp(toStringFunc.Name, callArgs, resultKind, resultStructTypeName);
          _currentBlock!.AddOp(callOp);

          var tempName = $"__interp_tostr_{callOp.Result!.Id}";
          var assignOp = new MaxonAssignOp(tempName, callOp.Result, true, false, resultKind ?? MaxonValueKind.Struct);
          _currentBlock!.AddOp(assignOp);
          _variables.Declare(tempName, resultKind ?? MaxonValueKind.Struct, false, callOp.Result, _currentBlock!, OwnershipFlags.IsTemp | OwnershipFlags.CallReturn, structTypeName: resultStructTypeName);
          exprValue = callOp.Result;
        }

        parts.Add((false, null, exprValue, formatSpec, exprOptimalType));
      } else {
        literalBuf.Append(text[pos]);
        pos++;
      }
    }

    if (literalBuf.Length > 0) {
      parts.Add((true, literalBuf.ToString(), null, null, (MlirType?)null));
    }

    return parts;
  }

  /// Returns the resolved value and its value kind (needed to distinguish signed vs unsigned in interpolation).
  private (MaxonValue Value, MaxonValueKind Kind) ParseInterpolationExpressionWithKind(string exprText, int sourceLine, int sourceColumn) {
    var lexer = new Lexer(exprText);
    var exprTokens = lexer.Tokenize();

    // Remap token positions from the sub-lexer (which starts at line 1, col 1)
    // back to the original source position within the enclosing string literal.
    for (int i = 0; i < exprTokens.Count; i++) {
      var t = exprTokens[i];
      exprTokens[i] = t with { Line = sourceLine, Column = sourceColumn + t.Column - 1 };
    }

    // Save and swap parser token state
    var savedTokens = new List<Token>(_tokens);
    var savedPos = _pos;

    _tokens.Clear();
    _tokens.AddRange(exprTokens);
    _pos = 0;

    try {
      var exprResult = ParseExpression();
      var kind = exprResult switch {
        ExprResult.VarRef v => v.Info.Kind,
        ExprResult.Direct d => GetValueKind(d.Value),
        ExprResult _ => throw new InvalidOperationException($"Unexpected ExprResult type: {exprResult.GetType().Name}"),
      };
      return (ResolveExprValue(exprResult), kind);
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

  private MaxonStruct EmitByteStringLiteral(Token token) {
    string value;
    try { value = StringUtils.ResolveEscapes(token.Value); } catch (InvalidEscapeException ex) {
      throw new CompileError(ErrorCode.LexerInvalidEscape,
          $"{ex.Message} in byte string literal", token.Line, token.Column);
    }
    var arrayTypeName = FindArrayTypeAliasForElement(MaxonValueKind.Byte);
    _usedTypeAliases.Add(arrayTypeName);
    var op = new MaxonByteStringLiteralOp(value, arrayTypeName);
    _currentBlock!.AddOp(op);
    return op.Result;
  }


  private string? FindTypeImplementingInterface(string interfaceName) {
    // Prefer non-alias types (e.g., Map over HeaderMap) to avoid chained alias
    // resolution failures when creating concrete type aliases like __Map_i64_i64
    string? fallback = null;
    foreach (var (name, type) in _typeRegistry) {
      if (type is MlirStructType structType && structType.ConformingInterfaces.Contains(interfaceName)) {
        if (!_typeAliasSources.ContainsKey(name))
          return name;
        fallback ??= name;
      }
    }
    return fallback;
  }

  private ExprResult.Direct EmitConstantLiteral(object constValue) {
    if (constValue is string strVal) {
      var stringTypeName = FindTypeImplementingInterface("BuiltinStringLiteral") ?? "String";
      var strOp = new MaxonStringLiteralOp(strVal, stringTypeName);
      _currentBlock!.AddOp(strOp);
      EmitLiteralTempAssign((MaxonStruct)strOp.Result);
      return new ExprResult.Direct(strOp.Result);
    }
    if (constValue is EnumConstantValue ec) {
      var enumType = (MlirUnionType)_typeRegistry[ec.EnumTypeName];
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
  /// Continues parsing an array literal when '[' has already been consumed and the first element
  /// has already been parsed. Used when detecting map vs array literal syntax.
  /// </summary>
  private ExprResult.Direct ParseArrayLiteralWithFirstElement(MaxonValue firstElement) {
    var elements = new List<MaxonValue> { firstElement };
    while (Check(TokenType.Comma)) {
      Advance();
      SkipNewlines();
      if (Check(TokenType.RightBracket)) break; // trailing comma
      elements.Add(ResolveExprValue(ParseExpression()));
      SkipNewlines();
    }
    Expect(TokenType.RightBracket);

    var (managedStruct, arrayTag, elementCount, elementKind, elementStructTypeName) =
      EmitManagedMemoryFromElements(elements);

    var arrayTypeName = FindArrayTypeAliasForElement(elementKind, elementStructTypeName);

    var iterIndexLit = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(iterIndexLit);

    var arrayFields = new List<(string Name, MaxonValue Value)> {
      ("iterIndex", iterIndexLit.Result),
      ("managed", managedStruct.Result)
    };
    var arrayStruct = new MaxonStructLiteralOp(arrayTypeName, arrayFields);
    _currentBlock!.AddOp(arrayStruct);

    arrayStruct.ArrayLiteralTag = arrayTag;
    arrayStruct.ArrayLiteralCount = elementCount;

    return new ExprResult.Direct(arrayStruct.Result);
  }

  /// <summary>
  /// Parses a map literal: [key: value, key: value, ...].
  /// Called after '[' consumed and first key expression already parsed. Colon detected but not consumed.
  /// Creates two __ManagedMemory structs (keys, values) and calls Map.init(keys, values).
  /// </summary>
  private ExprResult.Direct ParseMapLiteral(MaxonValue firstKey, Token bracketToken) {
    Advance(); // consume ':'
    var firstValue = ResolveExprValue(ParseExpression());

    var keys = new List<MaxonValue> { firstKey };
    var values = new List<MaxonValue> { firstValue };

    while (Check(TokenType.Comma)) {
      Advance(); // consume ','
      SkipNewlines();
      if (Check(TokenType.RightBracket)) break; // trailing comma
      var key = ResolveExprValue(ParseExpression());
      SkipNewlines();
      Expect(TokenType.Colon);
      var value = ResolveExprValue(ParseExpression());
      SkipNewlines();
      keys.Add(key);
      values.Add(value);
    }
    Expect(TokenType.RightBracket);

    // Create __ManagedMemory for keys
    var (keysManagedStruct, keysTag, keyCount, keyKind, keyStructTypeName) =
      EmitManagedMemoryFromElements(keys);
    keysManagedStruct.ArrayLiteralTag = keysTag;
    keysManagedStruct.ArrayLiteralCount = keyCount;

    // Create __ManagedMemory for values
    var (valuesManagedStruct, valuesTag, _, valueKind, valueStructTypeName) =
      EmitManagedMemoryFromElements(values);
    valuesManagedStruct.ArrayLiteralTag = valuesTag;
    valuesManagedStruct.ArrayLiteralCount = keyCount;

    // Find the type implementing BuiltinDictionaryLiteral (Map)
    var mapSourceTypeName = FindTypeImplementingInterface("BuiltinDictionaryLiteral")
      ?? throw new CompileError(ErrorCode.SemanticUndefinedFunction,
          "No type implements BuiltinDictionaryLiteral (Map type not found in stdlib)",
          bracketToken.Line, bracketToken.Column);

    // Determine concrete Key and Value types
    var keyType = InferMlirTypeFromElements(keyKind, keyStructTypeName, bracketToken);
    var valueType = InferMlirTypeFromElements(valueKind, valueStructTypeName, bracketToken);

    // Create or find concrete Map type alias
    var concreteMapTypeName = FindOrCreateMapTypeAlias(mapSourceTypeName, keyType, valueType);

    // Resolve Map.init method
    var sourceTypeName = _typeAliasSources.TryGetValue(concreteMapTypeName, out var src) ? src : concreteMapTypeName;
    var initMethodName = $"{sourceTypeName}.init";
    var resolvedInitName = ResolveMethodName(initMethodName);
    var initFunc = resolvedInitName != null
      ? _currentModule!.Functions.FirstOrDefault(f => f.Name == resolvedInitName)
          ?? _currentModule!.Functions.FirstOrDefault(f => UnmangleName(f.Name) == resolvedInitName)
          ?? throw new CompileError(ErrorCode.SemanticUndefinedFunction,
              $"Map type '{mapSourceTypeName}' does not have a valid init method (no '{initMethodName}' found)",
              bracketToken.Line, bracketToken.Column)
      : throw new CompileError(ErrorCode.SemanticUndefinedFunction,
            $"Map type '{mapSourceTypeName}' does not have a valid init method (no '{initMethodName}' found)",
            bracketToken.Line, bracketToken.Column);

    // Call Map.init(keys, values)
    var callOp = new MaxonCallOp(initFunc.Name,
      [keysManagedStruct.Result, valuesManagedStruct.Result],
      MaxonValueKind.Struct, concreteMapTypeName);
    _currentBlock!.AddOp(callOp);

    return new ExprResult.Direct(callOp.Result!);
  }

  /// <summary>
  /// Converts a MaxonValueKind + optional struct type name into an MlirType.
  /// Used to determine concrete Key/Value types for map literal type alias creation.
  /// </summary>
  private MlirType InferMlirTypeFromElements(MaxonValueKind kind, string? structTypeName, Token errorToken) {
    if ((kind == MaxonValueKind.Struct || kind == MaxonValueKind.Enum) && structTypeName != null) {
      if (_typeRegistry.TryGetValue(structTypeName, out var type))
        return type;
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Unknown type '{structTypeName}' in map literal",
        errorToken.Line, errorToken.Column);
    }
    return KindToMlirType(kind) ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
      $"Unsupported value kind '{kind}' in map literal", errorToken.Line, errorToken.Column);
  }

  /// <summary>
  /// Finds an existing Map type alias with matching Key/Value types, or creates one.
  /// Pattern follows FindArrayTypeAliasForElement.
  /// </summary>
  private string FindOrCreateMapTypeAlias(string mapSourceTypeName, MlirType keyType, MlirType valueType) {
    // Search for existing Map alias with matching Key and Value types
    foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
      if (sourceTypeName != mapSourceTypeName) continue;
      if (_typeRegistry.TryGetValue(aliasName, out var aliasType)
          && aliasType is MlirStructType st
          && st.TypeParams.TryGetValue("Key", out var kType) && kType.Name == keyType.Name
          && st.TypeParams.TryGetValue("Value", out var vType) && vType.Name == valueType.Name) {
        return aliasName;
      }
    }

    // No existing alias — auto-create one
    var autoAliasName = $"__Map_{keyType.Name}_{valueType.Name}";
    if (!_typeRegistry.ContainsKey(autoAliasName)
        && _typeRegistry.TryGetValue(mapSourceTypeName, out var mapType)
        && mapType is MlirStructType mapStruct) {
      var substitution = new Dictionary<string, MlirType> {
        ["Key"] = keyType,
        ["Value"] = valueType
      };
      RegisterConcreteTypeAlias(autoAliasName, mapSourceTypeName, mapStruct, substitution);

      // Ensure inner array type aliases exist for the Key and Value types
      // (e.g., __Array_TokenKind for Map<int, TokenKind>)
      EnsureArrayTypeAliasForType(keyType);
      EnsureArrayTypeAliasForType(valueType);
    }
    // Ensure alias source is tracked even if the type was already registered
    // (e.g., from a prior parser pass via the shared type registry)
    _typeAliasSources.TryAdd(autoAliasName, mapSourceTypeName);
    return autoAliasName;
  }

  /// <summary>
  /// Ensures a concrete Array type alias exists for the given element type.
  /// For enum/struct types, calls FindArrayTypeAliasForElement to auto-create if needed.
  /// Primitive types (int, float, etc.) already have predefined Array aliases.
  /// </summary>
  private void EnsureArrayTypeAliasForType(MlirType type) {
    if (type is MlirUnionType enumType) {
      FindArrayTypeAliasForElement(MaxonValueKind.Enum, enumType.Name);
    } else if (type is MlirStructType structType) {
      FindArrayTypeAliasForElement(MaxonValueKind.Struct, structType.Name);
    }
    // Primitive types (i64, f64, etc.) already have predefined aliases (IntArray, FloatArray, etc.)
  }

  /// <summary>
  /// Finds the typealias name for Array with the given element type.
  /// Returns the concrete alias (e.g., "ByteArray") if one exists, otherwise "Array".
  /// For struct element types, auto-creates a type alias if none exists.
  /// </summary>
  private string FindArrayTypeAliasForElement(MaxonValueKind elementKind, string? elementStructTypeName = null) {
    // Resolve the element type name: struct/enum use their type name, primitives use MlirType name
    string? elementTypeName;
    if ((elementKind == MaxonValueKind.Struct || elementKind == MaxonValueKind.Enum) && elementStructTypeName != null)
      elementTypeName = elementStructTypeName;
    else
      elementTypeName = KindToMlirType(elementKind)?.Name;

    if (elementTypeName == null)
      throw new InvalidOperationException(
        $"Cannot resolve element type name for kind '{elementKind}' (structTypeName={elementStructTypeName})");

    // Search for an existing typealias of Array with this element type
    var existing = FindArrayAliasByElementName(elementTypeName);
    if (existing != null) return existing;

    // For struct/enum elements, auto-create an alias (e.g., __Array_Pair)
    if ((elementKind == MaxonValueKind.Struct || elementKind == MaxonValueKind.Enum) && elementStructTypeName != null) {
      var autoAliasName = $"__Array_{elementStructTypeName}";
      if (!_typeRegistry.ContainsKey(autoAliasName)
          && _typeRegistry.TryGetValue("Array", out var arrayType)
          && arrayType is MlirStructType arrayStruct
          && _typeRegistry.TryGetValue(elementStructTypeName, out var elemRegisteredType)) {
        var substitution = new Dictionary<string, MlirType> { ["Element"] = elemRegisteredType };
        RegisterConcreteTypeAlias(autoAliasName, "Array", arrayStruct, substitution);
      }
      // Auto-created aliases must survive across parser passes (PreScan → Parse)
      _exportedTypeAliases.Add(autoAliasName);
      return autoAliasName;
    }

    // Auto-create alias for all primitive element types so monomorphization
    // can produce concrete specializations (e.g., for .enumerated() on int arrays)
    var primMlirType = KindToMlirType(elementKind);
    if (primMlirType != null) {
      var autoAliasName = $"__Array_{primMlirType.Name}";
      if (!_typeRegistry.ContainsKey(autoAliasName)
          && _typeRegistry.TryGetValue("Array", out var arrayType)
          && arrayType is MlirStructType arrayStruct) {
        var substitution = new Dictionary<string, MlirType> { ["Element"] = primMlirType };
        RegisterConcreteTypeAlias(autoAliasName, "Array", arrayStruct, substitution);
      }
      // Ensure alias source is registered even when the type was seeded from a
      // previous parser pass — RegisterConcreteTypeAlias is skipped in that case.
      _typeAliasSources.TryAdd(autoAliasName, "Array");
      return autoAliasName;
    }

    return "Array";
  }

  private string? FindArrayAliasByElementName(string elementTypeName) {
    foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
      if (sourceTypeName != "Array") continue;
      // Only consider user-defined (local) aliases, not stdlib aliases.
      // Stdlib array aliases (e.g. Sha256Words) should not be inferred for
      // unrelated array literals — let the caller auto-create an __Array_ alias instead.
      if (!_localTypeAliases.Contains(aliasName)) continue;
      if (_typeRegistry.TryGetValue(aliasName, out var aliasType)
          && aliasType is MlirStructType st
          && st.TypeParams.TryGetValue("Element", out var elemType)) {
        // Direct match (e.g., Element is "String" and we're looking for "String")
        if (elemType.Name == elementTypeName) return aliasName;
        // Ranged type match (e.g., Element is "Int" which is int(min..max), we're looking for "i64")
        if (elemType is MlirRangedPrimitiveType rpt && rpt.BaseType.Name == elementTypeName)
          return aliasName;
      }
    }
    return null;
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

    return EmitManagedMemoryFromElements(elements);
  }

  /// <summary>
  /// Takes a list of parsed element values and emits stack variable assignments + __ManagedMemory struct.
  /// Shared by array literals, map literal keys, and map literal values.
  /// </summary>
  private (MaxonStructLiteralOp ManagedStruct, string ArrayTag, int ElementCount, MaxonValueKind ElementKind, string? ElementStructTypeName) EmitManagedMemoryFromElements(List<MaxonValue> elements) {
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
    } else if (elements[0] is MaxonEnum enumElem) {
      elementStructTypeName = enumElem.TypeName;
      elementSize = elementKind.ElementSize();
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
      // Register heap-allocated struct element slots for scope-end cleanup.
      // The lowering increfs these slots, so they need a matching decref at scope exit.
      if (elementStructTypeName != null
          && _typeRegistry.TryGetValue(elementStructTypeName, out var elemType2)
          && elemType2.IsHeapAllocated) {
        _variables.Declare(elemVarName, elementKind, false, elements[i], _currentBlock!,
          flags: OwnershipFlags.IsTemp, structTypeName: elementStructTypeName);
      }
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

    // Use a concrete __ManagedMemory type when elements are heap-allocated structs,
    // so the destructor knows to decref each element when the buffer is freed.
    string managedTypeName = "__ManagedMemory";
    if (elementStructTypeName != null
        && _typeRegistry.TryGetValue(elementStructTypeName, out var elemRegType)
        && elemRegType.IsHeapAllocated
        && _typeRegistry.TryGetValue("__ManagedMemory", out var mmBase)
        && mmBase is MlirStructType mmStruct) {
      var concreteName = $"__ManagedMemory_{elementStructTypeName}";
      if (!_typeRegistry.ContainsKey(concreteName)) {
        var sub = new Dictionary<string, MlirType> { ["Element"] = elemRegType };
        RegisterConcreteTypeAlias(concreteName, "__ManagedMemory", mmStruct, sub);
      } else {
        _typeAliasSources.TryAdd(concreteName, "__ManagedMemory");
      }
      managedTypeName = concreteName;
    }

    var managedStruct = new MaxonStructLiteralOp(managedTypeName, managedFields);
    _currentBlock!.AddOp(managedStruct);

    return (managedStruct, arrayTag, count, elementKind, elementStructTypeName);
  }

  /// Parses ranged primitive construction: Age{42}, Count{someExpr}
  private ExprResult.Direct ParseRangedPrimitiveConstruction(Token typeToken, MlirRangedPrimitiveType rangedType) {
    Advance(); // consume '{'
    var innerExpr = ParseExpression();
    var innerValue = ResolveExprValue(innerExpr);
    Expect(TokenType.RightBrace);

    var innerKind = DetermineValueKind(innerValue);
    var expectedKind = rangedType.BaseType.ToValueKind();

    if (innerKind != expectedKind) {
      bool compatible =
        (expectedKind == MaxonValueKind.Byte && innerKind == MaxonValueKind.Integer) ||
        (expectedKind == MaxonValueKind.Float32 && innerKind == MaxonValueKind.Float);
      if (!compatible) {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot construct '{rangedType.Name}' from {KindToTypeName(innerKind)}, expected {MlirType.FormatAsSourceName(rangedType.BaseType)}",
          typeToken.Line, typeToken.Column);
      }
      // Re-emit float literal as Float32 so lowering produces StdF32 ops
      if (expectedKind == MaxonValueKind.Float32 && innerKind == MaxonValueKind.Float) {
        var lastOp = _currentBlock!.Operations.LastOrDefault();
        if (lastOp is MaxonLiteralOp lit && lit.Result == innerValue) {
          _currentBlock.Operations.Remove(lastOp);
          var f32Lit = new MaxonLiteralOp(lit.FloatValue, MaxonValueKind.Float32);
          _currentBlock.AddOp(f32Lit);
          innerValue = f32Lit.Result;
        }
      }
    }

    innerValue = ValidateAndEmitRangeCheck(innerValue, rangedType, expectedKind, typeToken);

    // Re-tag as Short when the optimal storage type is i16/u16
    var optimalKind = rangedType.OptimalType.ToValueKind();
    if (optimalKind == MaxonValueKind.Short) {
      var castOp = new MaxonCastOp(innerValue, MaxonValueKind.Short);
      _currentBlock!.AddOp(castOp);
      innerValue = castOp.Result;
    }

    _lastRangedTypeName = rangedType.Name;
    return new ExprResult.Direct(innerValue);
  }

  /// <summary>
  /// Validates a value against a ranged type's bounds. For literal constants, performs
  /// a compile-time check. For non-constant values, emits a runtime range check.
  /// </summary>
  private MaxonValue ValidateAndEmitRangeCheck(MaxonValue value, MlirRangedPrimitiveType rangedType, MaxonValueKind expectedKind, Token errorToken) {
    bool isLiteral = false;
    if (_currentBlock!.Operations.LastOrDefault() is MaxonLiteralOp litOp && litOp.Result == value) {
      double numericValue = litOp.ValueKind switch {
        MaxonValueKind.Integer => (double)litOp.IntValue,
        MaxonValueKind.Float or MaxonValueKind.Float32 => litOp.FloatValue,
        MaxonValueKind.Byte => (double)litOp.IntValue,
        _ => double.NaN
      };
      if (!double.IsNaN(numericValue)) {
        isLiteral = true;
        var upperLimit = rangedType.UpperInclusive ? rangedType.UpperBound : rangedType.UpperBound - 1;
        if (numericValue < rangedType.LowerBound || numericValue > upperLimit) {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"Value {litOp.IntValue} is outside the range of '{rangedType.Name}' ({rangedType.FormatRange()})",
            errorToken.Line, errorToken.Column);
        }
      }
    }

    if (!isLiteral) {
      value = EmitRuntimeRangeCheck(value, rangedType, expectedKind, errorToken.Line, _sourceFilePath);
    }

    return value;
  }

  private MaxonValue EmitRuntimeRangeCheck(MaxonValue value, MlirRangedPrimitiveType rangedType, MaxonValueKind kind, int sourceLine, string? sourceFilePath) {
    // Skip runtime check for full-range types (no values can be out of range)
    if (rangedType.IsFullBaseRange) return value;

    var checkId = _blockCounter++;
    var panicLabel = $"__range_panic_{checkId}";
    var continueLabel = $"__range_ok_{checkId}";
    // For comparisons, use the appropriate float kind or Integer for int/byte
    var cmpKind = kind is MaxonValueKind.Float or MaxonValueKind.Float32 ? kind : MaxonValueKind.Integer;

    bool isFloatBase = rangedType.BaseType == MlirType.F64 || rangedType.BaseType == MlirType.F32;
    bool needsLowerCheck = !isFloatBase
      ? rangedType.LowerBound > (double)long.MinValue
      : rangedType.LowerBound > double.MinValue;
    bool needsUpperCheck = !isFloatBase
      ? rangedType.UpperBound < (double)long.MaxValue
      : rangedType.UpperBound < double.MaxValue;

    MaxonValue? outOfRange = null;

    // Emit lower bound check: value < lowerBound
    if (needsLowerCheck) {
      MaxonLiteralOp lowerLit = rangedType.BaseType == MlirType.F64
        ? new MaxonLiteralOp(rangedType.LowerBound)
        : new MaxonLiteralOp((long)rangedType.LowerBound);
      _currentBlock!.AddOp(lowerLit);
      var cmpLower = new MaxonBinOp(MaxonBinOperator.Lt, value, lowerLit.Result, cmpKind);
      _currentBlock!.AddOp(cmpLower);
      outOfRange = cmpLower.Result;
    }

    // Emit upper bound check: value > upperBound (or value >= for upto)
    if (needsUpperCheck) {
      var upperOp = rangedType.UpperInclusive ? MaxonBinOperator.Gt : MaxonBinOperator.Ge;
      MaxonLiteralOp upperLit = rangedType.BaseType == MlirType.F64
        ? new MaxonLiteralOp(rangedType.UpperBound)
        : new MaxonLiteralOp((long)rangedType.UpperBound);
      _currentBlock!.AddOp(upperLit);
      var cmpUpper = new MaxonBinOp(upperOp, value, upperLit.Result, cmpKind);
      _currentBlock!.AddOp(cmpUpper);

      if (outOfRange != null) {
        var orOp = new MaxonBinOp(MaxonBinOperator.Or, outOfRange, cmpUpper.Result, MaxonValueKind.Bool);
        _currentBlock!.AddOp(orOp);
        outOfRange = orOp.Result;
      } else {
        outOfRange = cmpUpper.Result;
      }
    }

    if (outOfRange == null) return value;

    // Branch: if outOfRange → panic, else → continue
    _currentBlock!.AddOp(new MaxonCondBrOp(outOfRange, panicLabel, continueLabel));

    // Panic block
    var panicBlock = _currentFunction!.Body.AddBlock(panicLabel);
    var sourceFileName = sourceFilePath != null ? Path.GetFileName(sourceFilePath) : "unknown";
    panicBlock.AddOp(new MaxonPanicOp(
      $"panic at {sourceFileName}:{sourceLine}: Range check failed: value outside typealias '{rangedType.Name}'"));

    // Continue block — no reload needed because the panic block never returns,
    // so the register holding the original value is never clobbered on this path.
    _currentBlock = _currentFunction!.Body.AddBlock(continueLabel);
    return value;
  }

  private ExprResult.Direct ParseStructLiteral(string typeName) {
    var literalLine = Current().Line;
    Advance(); // consume '{'

    var baseTypeName = ResolveBaseTypeName(typeName);

    // Block direct construction of compiler-internal types
    if (baseTypeName is "__ManagedMemory" or "__ManagedFile" or "__ManagedDirectory"
                      or "__ManagedSocket" or "__ManagedList" or "__ManagedListNode") {
      throw new CompileError(ErrorCode.SemanticBuiltinTypeConstruction,
        $"'{baseTypeName}' is a compiler builtin type and cannot be constructed directly",
        literalLine, Current().Column);
    }

    var structType = (MlirStructType)_typeRegistry[typeName];
    var fieldValues = new List<(string, MaxonValue)>();
    var providedFields = new HashSet<string>();

    // Track array literal tag for types with __capacity and __ManagedMemory
    string? arrayLiteralTag = null;
    int arrayLiteralCount = 0;
    bool skipZeroInit = false;

    SkipNewlines();
    if (!Check(TokenType.RightBrace)) {
      do {
        SkipNewlines();
        var fieldNameToken = Expect(TokenType.Identifier);

        // skipZeroInit is a compiler directive for BuiltinArrayLiteral types, not a real field
        if (fieldNameToken.Value == "skipZeroInit") {
          if (!structType.ConformingInterfaces.Contains("BuiltinArrayLiteral")) {
            throw new CompileError(ErrorCode.SemanticUnknownField,
              $"'skipZeroInit' is only valid on types conforming to BuiltinArrayLiteral",
              fieldNameToken.Line, fieldNameToken.Column);
          }
          Expect(TokenType.Colon);
          Expect(TokenType.True);
          skipZeroInit = true;
          // Not a real field — don't add to fieldValues or providedFields
          continue;
        }

        var field = structType.GetField(fieldNameToken.Value) ?? throw new CompileError(ErrorCode.SemanticUnknownField,
            $"Type '{typeName}' has no field '{fieldNameToken.Value}'",
            fieldNameToken.Line, fieldNameToken.Column);
        if (!providedFields.Add(fieldNameToken.Value)) {
          throw new CompileError(ErrorCode.SemanticDuplicateDefinition,
            $"Duplicate field '{fieldNameToken.Value}' in '{typeName}' literal",
            fieldNameToken.Line, fieldNameToken.Column);
        }
        Expect(TokenType.Colon);
        var value = ResolveExprValue(ParseExpression());

        // Type-check: struct field must match value's struct type
        if (field.Type is MlirStructType fieldStructType && value is MaxonStruct valueStruct) {
          if (ResolveBaseTypeName(valueStruct.TypeName) != ResolveBaseTypeName(fieldStructType.Name)) {
            throw new CompileError(ErrorCode.SemanticTypeMismatch,
              $"Type mismatch: field '{fieldNameToken.Value}' expects '{fieldStructType.Name}' but got '{valueStruct.TypeName}'",
              fieldNameToken.Line, fieldNameToken.Column);
          }
        }

        fieldValues.Add((fieldNameToken.Value, value));
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    SkipNewlines();

    // Validate all exported fields are provided when user supplies any fields
    // (empty {} is allowed as zero-initialization)
    if (providedFields.Count > 0) {
      var missingFields = new List<string>();
      foreach (var field in structType.Fields) {
        if (!field.IsExported) continue;
        if (providedFields.Contains(field.Name)) continue;
        if (field.DefaultValue != null) continue;
        missingFields.Add(field.Name);
      }
      if (missingFields.Count > 0) {
        throw new CompileError(ErrorCode.SemanticUnknownField,
          $"Missing required field{(missingFields.Count > 1 ? "s" : "")} for type '{typeName}': {string.Join(", ", missingFields)}",
          Current().Line, Current().Column);
      }
    }

    // Fill in defaults for unspecified fields
    foreach (var field in structType.Fields) {
      if (!providedFields.Contains(field.Name)) {
        if (field.DefaultValue == null) {
          // Fixed-capacity type with managed __ManagedMemory field
          if (field.Name == "managed" && ResolveBaseTypeName(field.Type.Name) == "__ManagedMemory" &&
              structType.ConstParams.TryGetValue("__capacity", out var capacity)) {
            // Determine element type from struct's type parameters
            if (!structType.TypeParams.TryGetValue("Element", out var elemType)) {
              throw new CompileError(ErrorCode.SemanticTypeMismatch,
                $"Cannot determine element size for '{structType.Name}': no Element type parameter",
                Current().Line, Current().Column);
            }
            var elemKind = elemType.ToValueKind();
            int elemSize = elemType.ElementSize;

            arrayLiteralTag = $"__arr_{_blockCounter++}";
            arrayLiteralCount = (int)capacity;
            if (!skipZeroInit) {
              // Create N zero-valued elements on the stack (in reverse order for proper memory layout)
              for (int i = arrayLiteralCount - 1; i >= 0; i--) {
                var zeroVal = new MaxonLiteralOp(0L);
                _currentBlock!.AddOp(zeroVal);
                var elemVarName = $"{arrayLiteralTag}.{i}";
                var assignOp = new MaxonAssignOp(elemVarName, zeroVal.Result, isDeclaration: true, isMutable: false, elemKind);
                _currentBlock!.AddOp(assignOp);
              }
            }

            // Create __ManagedMemory struct with stack-allocated buffer
            var bufLit = new MaxonLiteralOp(0L); // buffer pointer placeholder
            _currentBlock!.AddOp(bufLit);
            var lenLit = new MaxonLiteralOp(capacity);
            _currentBlock!.AddOp(lenLit);
            var capLit = new MaxonLiteralOp(0L); // capacity=0 means read-only (rdata/stack); conversion patches when writable
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
          } else if (field.Type is MlirUnionType fieldUnionType && fieldUnionType.HasAssociatedValues) {
            // Associated-value union fields need a heap-allocated default (first case with zero args)
            var defaultCase = fieldUnionType.Cases[0];
            var zeroArgs = new List<MaxonValue>();
            if (defaultCase.AssociatedValues != null) {
              foreach (var _ in defaultCase.AssociatedValues) {
                var zeroVal = new MaxonLiteralOp(0L);
                _currentBlock!.AddOp(zeroVal);
                zeroArgs.Add(zeroVal.Result);
              }
            }
            var enumConstruct = new MaxonEnumConstructOp(fieldUnionType.Name, defaultCase.Name, 0, zeroArgs);
            _currentBlock!.AddOp(enumConstruct);
            fieldValues.Add((field.Name, enumConstruct.Result));
          } else {
            // Zero-initialize primitive fields not provided
            var zeroVal = new MaxonLiteralOp(0L);
            _currentBlock!.AddOp(zeroVal);
            fieldValues.Add((field.Name, zeroVal.Result));
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
    // Set the array literal tag for lowering if this type has a __ManagedMemory buffer
    if (arrayLiteralTag != null) {
      structLiteral.ArrayLiteralTag = arrayLiteralTag;
      structLiteral.ArrayLiteralCount = arrayLiteralCount;
      structLiteral.SkipZeroInit = skipZeroInit;
    }
    if (_sourceFilePath != null)
      structLiteral.SourceLocation = $"{Path.GetFileName(_sourceFilePath)}:{literalLine}";
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
      } else if (subField.Type is MlirUnionType nestedUnionType && nestedUnionType.HasAssociatedValues) {
        var defaultCase = nestedUnionType.Cases[0];
        var zeroArgs = new List<MaxonValue>();
        if (defaultCase.AssociatedValues != null) {
          foreach (var _ in defaultCase.AssociatedValues) {
            var zeroVal = new MaxonLiteralOp(0L);
            _currentBlock!.AddOp(zeroVal);
            zeroArgs.Add(zeroVal.Result);
          }
        }
        var enumConstruct = new MaxonEnumConstructOp(nestedUnionType.Name, defaultCase.Name, 0, zeroArgs);
        _currentBlock!.AddOp(enumConstruct);
        zeroFields.Add((subField.Name, enumConstruct.Result));
      } else {
        long value = 0L;
        // For __ManagedMemory types, set element_size from the Element type parameter.
        // Check both directly and through _typeAliasSources, since aliases like
        // ByteMemory or __ManagedMemory_QueryKey may refer to __ManagedMemory.
        if (subField.Name == "element_size" && IsManagedMemoryStruct(structType)) {
          if (typeParams.TryGetValue("Element", out var elemType)) {
            value = elemType.ElementSize;
          } else if (structType.TypeParams.TryGetValue("Element", out var elemType2)
                     && elemType2 is not MlirTypeParameterType) {
            value = elemType2.ElementSize;
          } else if (structType.Name.StartsWith("__ManagedMemory_")) {
            var elemTypeName = structType.Name["__ManagedMemory_".Length..];
            if (_typeRegistry.TryGetValue(elemTypeName, out var regType)) {
              value = regType.ElementSize;
            }
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
    EmitLiteralTempAssign(stringStruct);

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
    EmitLiteralTempAssign(charStruct);

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

    return KindToMlirType(elemAssign.ValueKind) ?? throw new CompileError(ErrorCode.SemanticTypeMismatch,
      $"Cannot infer element type from value kind '{elemAssign.ValueKind}'",
      Current().Line, Current().Column);
  }

  // Resolves an expression result to a MaxonValue, emitting a var_ref op if needed for cross-block references
  private MaxonValue ResolveExprValue(ExprResult expr) {
    _lastExprWasMutableVar = expr is ExprResult.VarRef { Info.Mutable: true };
    _lastExprVarName = expr is ExprResult.VarRef vr ? vr.VarName : null;
    switch (expr) {
      case ExprResult.Direct d:
        return d.Value;
      case ExprResult.VarRef v:
        // Captured variable inside a closure: emit env load instead of normal var ref
        if (v.Info.IsCaptured && _closureCaptures != null) {
          return EmitClosureCapture(v.VarName, v.Info);
        }
        // Struct/enum variables always need a fresh var_ref op so each reference
        // gets a unique SSA value ID (prevents aliasing in structVarNames when
        // multiple variables share the same underlying value).
        if (v.Info.Kind == MaxonValueKind.Struct) {
          var structRefOp = new MaxonStructVarRefOp(v.VarName, v.Info.StructTypeName!);
          _currentBlock!.AddOp(structRefOp);
          return structRefOp.Result;
        }
        if (v.Info.Kind == MaxonValueKind.Enum) {
          var enumType = (MlirUnionType)_typeRegistry[v.Info.StructTypeName!];
          var backingKind = GetUnionBackingKind(enumType);
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
        // Promise variables need to preserve their type metadata across blocks
        // so await can verify the value is a promise and access InnerKind/Throws.
        if (v.Info.Value is MaxonPromise origPromise) {
          return new MaxonPromise(refOp.Result.Id, origPromise.InnerKind, origPromise.InnerStructTypeName, origPromise.Throws);
        }
        return refOp.Result;
    }
    throw new InvalidOperationException($"Unknown expression result type: {expr.GetType().Name}");
  }

  /// <summary>
  /// Records a captured variable and emits a MaxonClosureEnvLoadOp to load it from the environment.
  /// </summary>
  private MaxonValue EmitClosureCapture(string varName, VarInfo info) {
    // Check if already captured; reuse the same index
    int captureIndex = _closureCaptures!.FindIndex(c => c.Name == varName);
    if (captureIndex < 0) {
      captureIndex = _closureCaptures.Count;
      _closureCaptures.Add(new CaptureInfo(varName, info.Kind, info.Value, info.StructTypeName));
    }
    var envLoadOp = new MaxonClosureEnvLoadOp(captureIndex, varName, info.Kind, info.StructTypeName);
    _currentBlock!.AddOp(envLoadOp);
    return envLoadOp.Result;
  }

  private abstract record ExprResult {
    public sealed record Direct(MaxonValue Value) : ExprResult;
    public sealed record VarRef(string VarName, VarInfo Info) : ExprResult;
  }

  private MlirType InferType(MaxonValue value) {
    return value switch {
      MaxonInteger => MlirType.I64,
      MaxonFloat => MlirType.F64,
      MaxonBool => MlirType.I1,
      MaxonByte => MlirType.I8,
      MaxonShort => MlirType.I16,
      MaxonStruct ms => _typeRegistry.TryGetValue(ms.TypeName, out var st) ? st
        : throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown struct type: {ms.TypeName}", Current().Line, Current().Column),
      MaxonEnum me => _typeRegistry.TryGetValue(me.TypeName, out var et) ? et
        : throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown enum type: {me.TypeName}", Current().Line, Current().Column),
      _ => throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Cannot infer type of {value.GetType().Name}", Current().Line, Current().Column)
    };
  }

  private MaxonValueKind DetermineValueKind(MaxonValue value) {
    return value switch {
      MaxonInteger => MaxonValueKind.Integer,
      MaxonFloat => MaxonValueKind.Float,
      MaxonBool => MaxonValueKind.Bool,
      MaxonByte => MaxonValueKind.Byte,
      MaxonShort => MaxonValueKind.Short,
      MaxonStruct => MaxonValueKind.Struct,
      MaxonEnum => MaxonValueKind.Enum,
      MaxonFunctionPtr => MaxonValueKind.Function,
      MaxonPromise => MaxonValueKind.Integer, // Promises are opaque pointers, stored as i64
      _ => throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Cannot determine kind of {value.GetType().Name}",
        Current().Line, Current().Column)
    };
  }

  private static bool IsWideningCast(MaxonValueKind source, MaxonValueKind target) {
    return (source, target) switch {
      (MaxonValueKind.Byte, MaxonValueKind.Integer) => true,
      (MaxonValueKind.Byte, MaxonValueKind.Float) => true,
      (MaxonValueKind.Byte, MaxonValueKind.Float32) => true,
      (MaxonValueKind.Short, MaxonValueKind.Integer) => true,
      (MaxonValueKind.Short, MaxonValueKind.Float) => true,
      (MaxonValueKind.Short, MaxonValueKind.Float32) => true,
      (MaxonValueKind.Integer, MaxonValueKind.Float) => true,
      (MaxonValueKind.Integer, MaxonValueKind.Float32) => true,
      (MaxonValueKind.Integer, MaxonValueKind.Short) => false,
      (MaxonValueKind.Integer, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Short, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Short, MaxonValueKind.Bool) => false,
      (MaxonValueKind.Byte, MaxonValueKind.Short) => true,
      (MaxonValueKind.Float, MaxonValueKind.Integer) => false,
      (MaxonValueKind.Float, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Float, MaxonValueKind.Short) => false,
      (MaxonValueKind.Float32, MaxonValueKind.Integer) => false,
      (MaxonValueKind.Float32, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Float32, MaxonValueKind.Short) => false,
      (MaxonValueKind.Float32, MaxonValueKind.Float) => true,
      (MaxonValueKind.Float, MaxonValueKind.Float32) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Integer) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Float) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Float32) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Byte) => false,
      (MaxonValueKind.Bool, MaxonValueKind.Short) => false,
      (MaxonValueKind.Integer, MaxonValueKind.Bool) => false,
      (MaxonValueKind.Float, MaxonValueKind.Bool) => false,
      (MaxonValueKind.Float32, MaxonValueKind.Bool) => false,
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
        $"Cannot cast from {KindToTypeName(sourceKind)} to {KindToTypeName(targetKind)}",
        asToken.Line, asToken.Column);
    }
  }


  private void ValidateTypeParameterConstraints(ExprResult lhs, ExprResult rhs, MaxonBinOperator op, Token opToken) {
    if (!IsComparisonOp(op)) return;
    var requiredInterface = op is MaxonBinOperator.Eq or MaxonBinOperator.Ne ? "Equatable" : "Comparable";

    foreach (var expr in new[] { lhs, rhs }) {
      if (expr is not ExprResult.VarRef v || v.Info.Kind != MaxonValueKind.TypeParameter) continue;
      var typeParamName = v.Info.StructTypeName;
      if (typeParamName == null) continue;

      if (_currentTypeName == null
          || !_typeRegistry.TryGetValue(_currentTypeName, out var currentType)
          || currentType is not MlirStructType currentStruct
          || !currentStruct.WhereConstraints.TryGetValue(typeParamName, out var constraints)
          || !constraints.Contains(requiredInterface)) {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Operator '{opToken.Value}' requires type parameter '{typeParamName}' to be constrained with 'where {typeParamName} is {requiredInterface}'",
          opToken.Line, opToken.Column);
      }
    }
  }

  private static bool IsComparisonOp(MaxonBinOperator op) =>
    op is MaxonBinOperator.Eq or MaxonBinOperator.Ne or MaxonBinOperator.Lt
      or MaxonBinOperator.Gt or MaxonBinOperator.Le or MaxonBinOperator.Ge;

  private (MaxonValueKind Kind, MaxonValue Lhs, MaxonValue Rhs) DetermineValueKind(
      MaxonValue lhs, MaxonValue rhs, MaxonBinOperator op, Token opToken) {
    var lhsKind = DetermineValueKind(lhs);
    var rhsKind = DetermineValueKind(rhs);

    if (lhsKind == rhsKind) {
      return (lhsKind, lhs, rhs);
    }

    // Integer-backed constants used with their backing type: coerce to raw value.
    if (lhsKind == MaxonValueKind.Enum && TryCoerceConstantsToBackingType(lhs, out var lhsRaw, out var lhsBackingKind)) {
      lhs = lhsRaw!; lhsKind = lhsBackingKind;
    }
    if (rhsKind == MaxonValueKind.Enum && TryCoerceConstantsToBackingType(rhs, out var rhsRaw, out var rhsBackingKind)) {
      rhs = rhsRaw!; rhsKind = rhsBackingKind;
    }

    if (lhsKind == rhsKind) {
      return (lhsKind, lhs, rhs);
    }

    // MatchKinds is called from arithmetic paths that bypass EmitBinaryOp's early coercion
    if (lhsKind == MaxonValueKind.Struct && IsIntegerLikeKind(rhsKind)) {
      if (TryCoerceCharLiteralToCodepoint(ref lhs)) { lhsKind = MaxonValueKind.Integer; }
    }
    if (rhsKind == MaxonValueKind.Struct && IsIntegerLikeKind(lhsKind)) {
      if (TryCoerceCharLiteralToCodepoint(ref rhs)) { rhsKind = MaxonValueKind.Integer; }
    }

    if (lhsKind == rhsKind) {
      return (lhsKind, lhs, rhs);
    }

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
        $"type mismatch: 'cannot compare {KindToTypeName(lhsKind)} with {KindToTypeName(rhsKind)}'",
        opToken.Line, opToken.Column);
    }

    if (IsWideningCast(lhsKind, rhsKind)) return (rhsKind, PromoteValue(lhs, rhsKind), rhs);
    if (IsWideningCast(rhsKind, lhsKind)) return (lhsKind, lhs, PromoteValue(rhs, lhsKind));

    throw new CompileError(ErrorCode.ParserExpectedExpression,
      $"Cannot operate on {KindToTypeName(lhsKind)} and {KindToTypeName(rhsKind)}",
      opToken.Line, opToken.Column);
  }

  /// <summary>
  /// If <paramref name="value"/> is a constants-type enum value, emits a raw value extraction
  /// op and returns the backing kind. Returns false for regular enums.
  /// String/char-backed constants return MaxonValueKind.Struct (the actual String/Character value).
  /// </summary>
  private bool TryCoerceConstantsToBackingType(MaxonValue value, out MaxonValue? raw, out MaxonValueKind backingKind) {
    raw = null;
    backingKind = MaxonValueKind.Integer;
    if (value is not MaxonEnum me) return false;
    if (!_typeRegistry.TryGetValue(me.TypeName, out var type)) return false;
    if (type is not MlirEnumType constantsType) return false;
    if (constantsType.BackingType is MlirStringBackingType or MlirCharBackingType) {
      backingKind = MaxonValueKind.Struct;
      raw = EmitUnionRawValueExtraction(_currentBlock!, value, constantsType, me.TypeName, MaxonValueKind.Integer);
      EmitLiteralTempAssign((MaxonStruct)raw);
    } else {
      backingKind = GetUnionBackingKind(constantsType);
      raw = EmitUnionRawValueExtraction(_currentBlock!, value, constantsType, me.TypeName, backingKind);
    }
    return true;
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

  private static bool IsIntegerLikeKind(MaxonValueKind kind) =>
    kind is MaxonValueKind.Integer or MaxonValueKind.Byte or MaxonValueKind.Short;

  /// <summary>
  /// If the given value is a Character struct produced by a char literal op,
  /// replaces it with an integer literal containing the Unicode codepoint value.
  /// Only coerces actual literals, not Character variables.
  /// </summary>
  private bool TryCoerceCharLiteralToCodepoint(ref MaxonValue value) {
    if (value is not MaxonStruct ms || ms.TypeName != "Character") return false;

    // Find the producing MaxonCharLiteralOp in the current block
    MaxonCharLiteralOp? charOp = null;
    int charOpIdx = -1;
    var ops = _currentBlock!.Operations;
    for (int i = ops.Count - 1; i >= 0; i--) {
      if (ops[i] is MaxonCharLiteralOp cop && cop.Result == value) {
        charOp = cop;
        charOpIdx = i;
        break;
      }
    }
    if (charOp == null) return false; // Not a literal — don't coerce

    // Extract Unicode codepoint from the escape-resolved string
    string resolved = charOp.Value;
    int codepoint;
    if (resolved.Length == 1) {
      codepoint = resolved[0];
    } else if (resolved.Length == 2 && char.IsSurrogatePair(resolved, 0)) {
      codepoint = char.ConvertToUtf32(resolved, 0);
    } else {
      return false; // Multi-codepoint grapheme cluster — cannot coerce to single int
    }

    // Remove the MaxonCharLiteralOp and its temp assign from the block
    for (int i = ops.Count - 1; i >= charOpIdx; i--) {
      if (ops[i] == charOp) {
        ops.RemoveAt(i);
      } else if (ops[i] is MaxonAssignOp assign && assign.Value == value
                 && assign.VarName.StartsWith("__lit_tmp_")) {
        _variables.Remove(assign.VarName);
        ops.RemoveAt(i);
      }
    }

    // Emit an integer literal with the codepoint value
    var litOp = new MaxonLiteralOp((long)codepoint);
    _currentBlock!.AddOp(litOp);
    value = litOp.Result;
    return true;
  }

  private MaxonValue PromoteValue(MaxonValue value, MaxonValueKind targetKind) {
    var sourceKind = DetermineValueKind(value);
    return (sourceKind, targetKind) switch {
      (MaxonValueKind.Integer, MaxonValueKind.Float) => PromoteIntToFloat(value),
      (MaxonValueKind.Integer, MaxonValueKind.Float32) => PromoteIntToFloat(value),
      (MaxonValueKind.Byte, MaxonValueKind.Integer) => value,
      (MaxonValueKind.Byte, MaxonValueKind.Float) => PromoteIntToFloat(value),
      (MaxonValueKind.Byte, MaxonValueKind.Float32) => PromoteIntToFloat(value),
      (MaxonValueKind.Float32, MaxonValueKind.Float) => EmitCast(value, MaxonValueKind.Float),
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
  private bool IsFunctionVisible(MlirFunction<MaxonOp> func) {
    if (func.IsExported) return true;
    if (func.SourceFilePath == null || _sourceFilePath == null) return true;
    return func.SourceFilePath == _sourceFilePath;
  }

  private MlirFunction<MaxonOp> ResolveFunctionName(string functionName, Token functionNameToken) {
    // First, try to find a function in the current file's namespace
    var currentNamespace = DeriveNamespace();
    var qualifiedName = string.IsNullOrEmpty(currentNamespace) ? functionName : $"{currentNamespace}.{functionName}";

    // Check for exact match with current namespace (local function)
    var localMatch = _currentModule!.Functions.FirstOrDefault(f => f.Name == qualifiedName);
    if (localMatch != null) {
      return localMatch;
    }

    // Find all other matches: exact match OR suffix match, filtered by visibility
    var exactMatches = _currentModule!.Functions.Where(f => f.Name == functionName && IsFunctionVisible(f)).ToList();
    var suffixPattern = $".{functionName}";
    var suffixMatches = _currentModule!.Functions.Where(f => f.Name.EndsWith(suffixPattern) && IsFunctionVisible(f)).ToList();

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
      // Try alias fallback: ByteArray.equals → Array.equals
      var dotIdx = functionName.IndexOf('.');
      if (dotIdx > 0) {
        var typePart = functionName[..dotIdx];
        var methodPart = functionName[(dotIdx + 1)..];
        if (_typeAliasSources.TryGetValue(typePart, out var sourceType)) {
          var aliasedName = $"{sourceType}.{methodPart}";
          var aliased = ResolveMethodName(aliasedName);
          if (aliased != null) {
            var match = _currentModule!.Functions.FirstOrDefault(f => f.Name == aliased);
            if (match != null) return match;
          }
        }
      }
      // Check if there's a non-visible match to give a better error message
      var hiddenExact = _currentModule!.Functions.FirstOrDefault(f => f.Name == functionName && !IsFunctionVisible(f));
      var hiddenSuffix = _currentModule!.Functions.FirstOrDefault(f => f.Name.EndsWith(suffixPattern) && !IsFunctionVisible(f));
      var hidden = hiddenExact ?? hiddenSuffix;
      if (hidden != null) {
        throw new CompileError(ErrorCode.SemanticSymbolNotExported,
          $"function '{functionName}' is not exported",
          functionNameToken.Line, functionNameToken.Column);
      }
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

    // Check local namespace (always visible — same file)
    var localMatches = _currentModule!.Functions.Where(f => f.Name == qualifiedName || f.Name.StartsWith(qualifiedName + "$")).ToList();
    if (localMatches.Count > 0) return localMatches;

    // Exact matches (including overload variants), filtered by visibility
    var exactMatches = _currentModule!.Functions.Where(f => (f.Name == functionName || f.Name.StartsWith(functionName + "$")) && IsFunctionVisible(f)).ToList();
    if (exactMatches.Count > 0) return exactMatches;

    // Suffix matches, filtered by visibility
    var suffixPattern = $".{functionName}";
    var suffixDollar = $".{functionName}$";
    var suffixMatches = _currentModule!.Functions.Where(f => (f.Name.EndsWith(suffixPattern) || f.Name.Contains(suffixDollar)) && IsFunctionVisible(f)).ToList();
    return suffixMatches;
  }

  /// <summary>
  /// Given multiple overload candidates, peeks at the named arguments at the current
  /// token position to select the matching overload. If only one candidate, returns it.
  /// </summary>
  private MlirFunction<MaxonOp> SelectOverloadByNamedArgs(List<MlirFunction<MaxonOp>> candidates, Token callToken) {
    if (candidates.Count == 1) return candidates[0];
    if (candidates.Count == 0) {
      // Check if there's a non-visible match to give a better error message
      var functionName = callToken.Value;
      var suffixPattern = $".{functionName}";
      var hidden = _currentModule!.Functions.FirstOrDefault(f =>
        (f.Name == functionName || f.Name.EndsWith(suffixPattern)) && !IsFunctionVisible(f));
      if (hidden != null) {
        throw new CompileError(ErrorCode.SemanticSymbolNotExported,
          $"function '{functionName}' is not exported",
          callToken.Line, callToken.Column);
      }
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

    // Disambiguate by argument count: exclude candidates whose required parameter
    // count (excluding 'self') doesn't match the number of provided arguments
    if (matching.Count > 1) {
      var argCount = PeekArgCount();
      var countFiltered = matching.Where(c => {
        int requiredParams = c.ParamNames.Count(n => n != "self");
        return requiredParams == argCount;
      }).ToList();
      if (countFiltered.Count >= 1 && countFiltered.Count < matching.Count) {
        Logger.Debug(LogCategory.Parser, $"  Overload disambiguation: narrowed {matching.Count} candidates to {countFiltered.Count} by argument count ({argCount})");
        matching = countFiltered;
      }
    }

    if (matching.Count == 1) return matching[0];
    if (matching.Count == 0) {
      var candidateInfo = string.Join(", ", candidates.Select(c =>
        FormatOverloadSignature(c)));
      throw new CompileError(ErrorCode.SemanticAmbiguousFunctionCall,
        $"No overload of '{UnmangleName(callToken.Value)}' matches the named arguments. Candidates: {candidateInfo}",
        callToken.Line, callToken.Column);
    }

    // Disambiguate by peeked argument types against each candidate's parameter types
    if (matching.Count > 1 && _pos < _tokens.Count) {
      var argTypes = PeekArgTypes();
      if (argTypes.Count > 0 && argTypes.Any(t => t != null)) {
        var scored = matching.Select(c => {
          int firstParamIdx = c.ParamNames.Contains("self") ? 1 : 0;
          int score = 0;
          bool compatible = true;
          for (int i = 0; i < argTypes.Count && (firstParamIdx + i) < c.ParamTypes.Count; i++) {
            var argType = argTypes[i];
            var paramType = c.ParamTypes[firstParamIdx + i];
            if (argType == null) continue; // unknown arg type — compatible with anything
            if (!IsOverloadArgTypeCompatible(argType, paramType)) { compatible = false; break; }
            if (paramType is MlirTypeParameterType) score += 0; // generic match
            else if (paramType is MlirStructType paramSt && paramSt.TypeParams.Values.Any(v => v is MlirTypeParameterType))
              score += 0; // generic struct with unresolved type params (e.g., ElementArray = Array with Element)
            else if (TypeMangledSuffix(argType) == TypeMangledSuffix(paramType)) score += 2; // exact match
            else score += 1; // widening/compatible match
          }
          return (Candidate: c, Score: score, Compatible: compatible);
        }).ToList();

        var compatibleCandidates = scored.Where(s => s.Compatible).ToList();
        if (compatibleCandidates.Count == 1) {
          Logger.Debug(LogCategory.Parser, $"  Overload disambiguation: selected by argument types");
          return compatibleCandidates[0].Candidate;
        } else if (compatibleCandidates.Count > 1) {
          // Pick highest-scoring candidate if there's a clear winner
          var maxScore = compatibleCandidates.Max(s => s.Score);
          var best = compatibleCandidates.Where(s => s.Score == maxScore).ToList();
          if (best.Count == 1) {
            Logger.Debug(LogCategory.Parser, $"  Overload disambiguation: selected by best type score ({maxScore})");
            return best[0].Candidate;
          }
          matching = [.. best.Select(s => s.Candidate)];
        } else {
          // No overload matches arg types — pick first and let call-site type checking report the real error
          Logger.Debug(LogCategory.Parser, $"  Overload disambiguation: no compatible candidate, picking first");
          return scored[0].Candidate;
        }
      }
    }

    // Legacy fallback: disambiguate by first arg's token structure (collection, closure, char)
    if (matching.Count > 1 && _pos < _tokens.Count) {
      bool argIsCollection = Current().Type == TokenType.LeftBracket;
      bool argIsClosure = Current().Type == TokenType.LeftParen;
      bool argIsCharLiteral = Current().Type == TokenType.CharacterLiteral;

      var narrowed = matching.Where(c => {
        int firstParamIdx = c.ParamNames.Contains("self") ? 1 : 0;
        if (firstParamIdx >= c.ParamTypes.Count) return true;
        var paramType = c.ParamTypes[firstParamIdx];
        bool paramIsCollection = paramType is MlirStructType st
          && st.TypeParams.Count > 0
          && st.TypeParams.Values.Any(v => v is MlirTypeParameterType);
        bool paramIsFunction = paramType is MlirFunctionType;
        bool paramIsCharacter = paramType is MlirStructType cs && cs.Name == "Character";

        if (paramIsFunction != argIsClosure) return false;
        if (argIsCharLiteral && !paramIsCharacter) return false;
        if (!argIsCharLiteral && paramIsCharacter) return false;

        return paramIsCollection == argIsCollection;
      }).ToList();
      if (narrowed.Count >= 1 && narrowed.Count < matching.Count) {
        Logger.Debug(LogCategory.Parser, $"  Overload disambiguation: narrowed {matching.Count} candidates to {narrowed.Count} by first arg token type (collection={argIsCollection}, closure={argIsClosure}, charLiteral={argIsCharLiteral})");
        matching = narrowed;
      }
    }

    if (matching.Count == 1) return matching[0];

    var matchInfo = string.Join(", ", matching.Select(c =>
      FormatOverloadSignature(c)));
    throw new CompileError(ErrorCode.SemanticAmbiguousFunctionCall,
      $"Ambiguous overload for '{UnmangleName(callToken.Value)}': multiple overloads match. Candidates: {matchInfo}",
      callToken.Line, callToken.Column);
  }

  private static string FormatOverloadSignature(MlirFunction<MaxonOp> func) {
    var paramParts = func.ParamNames.Zip(func.ParamTypes)
      .Where(p => p.First != "self")
      .Select(p => $"{p.First} {TypeMangledSuffix(p.Second)}");
    return $"({string.Join(", ", paramParts)})";
  }

  /// <summary>
  /// Checks if an inferred argument type is compatible with a parameter type for overload selection.
  /// </summary>
  private bool IsOverloadArgTypeCompatible(MlirType argType, MlirType paramType) {
    if (paramType is MlirTypeParameterType) return true;
    if (TypeMangledSuffix(argType) == TypeMangledSuffix(paramType)) return true;
    // Struct compatibility: check if arg type conforms to param type's interface or is a subtype
    if (argType is MlirStructType argStruct && paramType is MlirStructType paramStruct) {
      return IsStructTypeCompatible(argStruct.Name, paramStruct.Name);
    }
    // Widening: i8/i16 -> i64
    if (paramType == MlirType.I64 && (argType == MlirType.I8 || argType == MlirType.I16)) return true;
    // Ranged primitives match their base type
    if (argType is MlirRangedPrimitiveType argRanged)
      return IsOverloadArgTypeCompatible(argRanged.BaseType, paramType);
    if (paramType is MlirRangedPrimitiveType paramRanged)
      return IsOverloadArgTypeCompatible(argType, paramRanged.BaseType);
    return false;
  }

  /// <summary>
  /// Peeks at argument expressions in the token stream to infer their types without consuming tokens.
  /// Returns a list of inferred types (null for unknown/uninferable).
  /// </summary>
  private List<MlirType?> PeekArgTypes() {
    var types = new List<MlirType?>();
    var savedPos = _pos;
    int parenDepth = 1;

    while (_pos < _tokens.Count && parenDepth > 0) {
      // Skip named arg labels (name: value)
      if ((Current().Type == TokenType.Identifier || Lexer.KeywordMap.ContainsKey(Current().Value))
          && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.Colon) {
        _pos += 2; // skip name and colon
        continue;
      }

      var argType = InferArgTypeFromToken();
      types.Add(argType);

      // Skip to next comma at depth 1 or closing paren
      SkipToNextArgBoundary(ref parenDepth);
    }

    _pos = savedPos;
    return types;
  }

  private MlirType? InferArgTypeFromToken() {
    if (_pos >= _tokens.Count) return null;
    var token = Current();
    return token.Type switch {
      TokenType.IntegerLiteral => MlirType.I64,
      TokenType.FloatLiteral => MlirType.F64,
      TokenType.StringLiteral or TokenType.StringInterp =>
        _typeRegistry.TryGetValue("String", out var strType) ? strType : null,
      TokenType.ByteStringLiteral =>
        _typeRegistry.TryGetValue(FindArrayTypeAliasForElement(MaxonValueKind.Byte), out var baType) ? baType : null,
      TokenType.CharacterLiteral =>
        _typeRegistry.TryGetValue("Character", out var charType) ? charType : null,
      TokenType.True or TokenType.False => MlirType.I1,
      TokenType.LeftBracket => null, // collection — handled by legacy fallback
      TokenType.LeftParen => null, // closure — handled by legacy fallback
      TokenType.Minus when _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.IntegerLiteral => MlirType.I64,
      TokenType.Minus when _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.FloatLiteral => MlirType.F64,
      TokenType.Identifier => InferIdentifierArgType(token.Value),
      _ => null
    };
  }

  /// Infer the type of an identifier-starting argument, including dotted field access (e.g., nameToken.value).
  private MlirType? InferIdentifierArgType(string name) {
    // Check for dotted field access: identifier.field
    if (_pos + 2 < _tokens.Count
        && _tokens[_pos + 1].Type == TokenType.Dot
        && _tokens[_pos + 2].Type == TokenType.Identifier) {
      var baseType = InferIdentifierType(name);
      if (baseType is MlirStructType st) {
        var fieldName = _tokens[_pos + 2].Value;
        var field = st.Fields.FirstOrDefault(f => f.Name == fieldName);
        if (field != null) return field.Type;
      }
    }
    return InferIdentifierType(name);
  }

  private MlirType? InferIdentifierType(string name) {
    // Check if it's a known variable
    if (_variables.TryGetValue(name, out var varInfo)) {
      return varInfo.Kind switch {
        MaxonValueKind.Integer => MlirType.I64,
        MaxonValueKind.Float => MlirType.F64,
        MaxonValueKind.Bool => MlirType.I1,
        MaxonValueKind.Byte => MlirType.I8,
        MaxonValueKind.Short => MlirType.I16,
        MaxonValueKind.Struct => varInfo.StructTypeName != null && _typeRegistry.TryGetValue(varInfo.StructTypeName, out var st) ? st : null,
        MaxonValueKind.Enum => varInfo.StructTypeName != null && _typeRegistry.TryGetValue(varInfo.StructTypeName, out var et) ? et : null,
        MaxonValueKind.Function => varInfo.FnTypeName,
        _ => null
      };
    }
    return null;
  }

  private void SkipToNextArgBoundary(ref int parenDepth) {
    while (_pos < _tokens.Count && parenDepth > 0) {
      var t = Current().Type;
      if (t == TokenType.LeftParen || t == TokenType.LeftBracket || t == TokenType.LeftBrace) parenDepth++;
      if (t == TokenType.RightParen || t == TokenType.RightBracket || t == TokenType.RightBrace) {
        parenDepth--;
        if (parenDepth == 0) break;
      }
      if (t == TokenType.Comma && parenDepth == 1) {
        _pos++; // skip the comma
        return;
      }
      _pos++;
    }
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

      if ((Current().Type == TokenType.Identifier || Lexer.KeywordMap.ContainsKey(Current().Value))
          && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.Colon) {
        names.Add(Current().Value);
      }
      _pos++;
    }

    _pos = savedPos;
    return names;
  }

  /// <summary>
  /// Peeks ahead to count the number of arguments at the current call site.
  /// Assumes _pos is right after the opening paren. Returns 0 for empty arg list.
  /// </summary>
  private int PeekArgCount() {
    var savedPos = _pos;

    // Empty arg list: immediate closing paren
    if (_pos < _tokens.Count && Current().Type == TokenType.RightParen) {
      _pos = savedPos;
      return 0;
    }

    int count = 1; // At least one arg if we didn't hit ')'
    int parenDepth = 1;
    while (_pos < _tokens.Count && parenDepth > 0) {
      if (Current().Type == TokenType.LeftParen) parenDepth++;
      if (Current().Type == TokenType.RightParen) {
        parenDepth--;
        if (parenDepth == 0) break;
      }
      if (Current().Type == TokenType.Comma && parenDepth == 1) count++;
      _pos++;
    }

    _pos = savedPos;
    return count;
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
      _lastArgMutabilities = callee.ParamTypes.Count > 0
        ? [.. new bool[callee.ParamTypes.Count]]
        : null;
      _lastArgVarNames = callee.ParamTypes.Count > 0
        ? [.. new string?[callee.ParamTypes.Count]]
        : null;
      if (callee.ParamTypes.Count > 0) {
        return (FillDefaultArgs(functionNameToken, callee, new MaxonValue?[callee.ParamTypes.Count]), callee);
      }
      return ([], callee);
    }

    var args = new MaxonValue?[callee.ParamTypes.Count];
    var argMuts = new bool[callee.ParamTypes.Count];
    var argNames = new string?[callee.ParamTypes.Count];
    ParseArgList(functionNameToken, callee, args, firstPositionalIndex: 0, argMutabilities: argMuts, argVarNames: argNames);
    Expect(TokenType.RightParen);

    _lastArgMutabilities = [.. argMuts];
    _lastArgVarNames = [.. argNames];
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

    // Resolve type parameters from the self arg's concrete struct type
    Dictionary<string, MlirType>? selfTypeParams = null;
    if (args.Length > 0 && args[0] is MaxonStruct selfStruct
        && _typeRegistry.TryGetValue(selfStruct.TypeName, out var selfType)
        && selfType is MlirStructType selfStructType
        && selfStructType.TypeParams.Count > 0) {
      selfTypeParams = selfStructType.TypeParams;
    }

    // Implicit type conversion and type checking for function arguments
    for (int i = 0; i < args.Length; i++) {
      var argKind = DetermineValueKind(args[i]!);
      var paramType = callee.ParamTypes[i];

      // Resolve type-parameter params against the self arg's concrete type params
      if (paramType is MlirTypeParameterType tp) {
        if (selfTypeParams != null && selfTypeParams.TryGetValue(tp.ParameterName, out var resolvedType)
            && resolvedType is not MlirTypeParameterType) {
          paramType = resolvedType;
        } else {
          continue;
        }
      }

      if (paramType is MlirStructType paramStructType) {
        // Struct parameter: arg must be a struct with matching type name
        if (args[i] is not MaxonStruct argStruct) {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"argument type mismatch for '{callee.ParamNames[i]}': expected '{paramStructType.Name}', got '{KindToTypeName(argKind)}'",
            functionNameToken.Line, functionNameToken.Column);
        }
        // For Self-typed params (param type matches self's base type), the arg must match
        // the self's concrete type, not just the base type
        var expectedTypeName = paramStructType.Name;
        if (i > 0 && args[0] is MaxonStruct selfArg
            && _typeAliasSources.TryGetValue(selfArg.TypeName, out var selfSource)
            && selfSource == paramStructType.Name) {
          expectedTypeName = selfArg.TypeName;
        }
        if (!IsStructTypeCompatible(argStruct.TypeName, expectedTypeName)) {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"argument type mismatch for '{callee.ParamNames[i]}': expected '{expectedTypeName}', got '{argStruct.TypeName}'",
            functionNameToken.Line, functionNameToken.Column);
        }
        continue;
      }

      if (paramType is MlirUnionType paramEnumType) {
        // Enum parameter: arg must be an enum with matching type name
        if (args[i] is not MaxonEnum argEnum) {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"argument type mismatch for '{callee.ParamNames[i]}': expected '{paramEnumType.Name}', got '{ArgTypeName(args[i]!, argKind)}'",
            functionNameToken.Line, functionNameToken.Column);
        }
        if (argEnum.TypeName != paramEnumType.Name) {
          throw new CompileError(ErrorCode.SemanticTypeMismatch,
            $"argument type mismatch for '{callee.ParamNames[i]}': expected '{paramEnumType.Name}', got '{argEnum.TypeName}'",
            functionNameToken.Line, functionNameToken.Column);
        }
        continue;
      }

      // Primitive parameter: arg must not be a struct/enum
      if (argKind is MaxonValueKind.Struct or MaxonValueKind.Enum) {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"argument type mismatch for '{callee.ParamNames[i]}': expected '{MlirType.FormatAsSourceName(paramType)}', got '{ArgTypeName(args[i]!, argKind)}'",
          functionNameToken.Line, functionNameToken.Column);
      }

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
      (MaxonValueKind.Integer, MaxonValueKind.Float32) => EmitCast(arg, MaxonValueKind.Float32),
      (MaxonValueKind.Float, MaxonValueKind.Integer) => EmitCast(arg, MaxonValueKind.Integer),
      (MaxonValueKind.Float32, MaxonValueKind.Integer) => EmitCast(arg, MaxonValueKind.Integer),
      (MaxonValueKind.Integer, MaxonValueKind.Byte) => EmitCast(arg, MaxonValueKind.Byte),
      (MaxonValueKind.Float, MaxonValueKind.Byte) => EmitCast(arg, MaxonValueKind.Byte),
      (MaxonValueKind.Float32, MaxonValueKind.Byte) => EmitCast(arg, MaxonValueKind.Byte),
      (MaxonValueKind.Float32, MaxonValueKind.Float) => EmitCast(arg, MaxonValueKind.Float),
      (MaxonValueKind.Float, MaxonValueKind.Float32) => EmitCast(arg, MaxonValueKind.Float32),
      (MaxonValueKind.Byte, MaxonValueKind.Integer) => arg, // byte is stored as i64 internally
      _ => throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"argument type mismatch for '{paramName}': expected '{KindToTypeName(paramKind)}', got '{KindToTypeName(argKind)}'",
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
    // Resolve function type parameters for closure type inference
    if (resolvedType is MlirFunctionType ft && typeParams != null) {
      resolvedType = ResolveFunctionType(ft, typeParams);
    }
    // Untyped closure: when expecting a function type and tokens look like a closure
    if (resolvedType is MlirFunctionType expectedFnType && IsClosure()) {
      return ResolveExprValue(ParseClosure(expectedFnType));
    }
    return ResolveExprValue(ParseExpression());
  }

  /// <summary>
  /// Shared logic for parsing a first-positional + named-remaining argument list.
  /// Fills entries into the pre-allocated args array. firstPositionalIndex is the
  /// slot used when the first argument is positional (0 for normal calls, 1 for
  /// instance methods where slot 0 is self).
  /// </summary>
  private void ParseArgList(Token callToken, MlirFunction<MaxonOp> callee, MaxonValue?[] args, int firstPositionalIndex, Dictionary<string, MlirType>? typeParams = null, bool[]? argMutabilities = null, string?[]? argVarNames = null) {
    // Track where each arg was evaluated for cross-block pinning.
    var argLocations = new (MlirBlock<MaxonOp>? block, int opIndex)[args.Length];

    if (CheckIdentifierLike() && PeekNext().Type == TokenType.Colon) {
      ParseNamedArg(callee, args, typeParams, argMutabilities, argVarNames);
      for (int i = 0; i < args.Length; i++)
        if (args[i] != null && argLocations[i].block == null)
          argLocations[i] = (_currentBlock!, _currentBlock!.Operations.Count);
    } else {
      args[firstPositionalIndex] = ParseCallArgValue(callee.ParamTypes[firstPositionalIndex], typeParams);
      if (argMutabilities != null) argMutabilities[firstPositionalIndex] = _lastExprWasMutableVar;
      if (argVarNames != null) argVarNames[firstPositionalIndex] = _lastExprVarName;
      argLocations[firstPositionalIndex] = (_currentBlock!, _currentBlock!.Operations.Count);
    }

    while (Check(TokenType.Comma) && Advance() != null) {
      if (CheckIdentifierLike() && PeekNext().Type == TokenType.Colon) {
        ParseNamedArg(callee, args, typeParams, argMutabilities, argVarNames);
        for (int i = 0; i < args.Length; i++)
          if (args[i] != null && argLocations[i].block == null)
            argLocations[i] = (_currentBlock!, _currentBlock!.Operations.Count);
      } else {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Second and subsequent arguments must be named. Use 'name: value' syntax",
          callToken.Line, callToken.Column);
      }
    }

    // If any arg was evaluated in a different block than the final one,
    // retroactively insert a store in the arg's original block and reload here.
    var finalBlock = _currentBlock!;
    for (int i = 0; i < args.Length; i++) {
      if (args[i] == null || argLocations[i].block == null) continue;
      if (argLocations[i].block == finalBlock) continue;
      if (args[i] is MaxonStruct || args[i] is MaxonEnum) continue;
      var kind = DetermineValueKind(args[i]!);
      var pinName = $"__arg_pin_{_blockCounter++}";
      // Insert store in the original block, right after the arg was evaluated
      var storeOp = new MaxonAssignOp(pinName, args[i]!, true, true, kind);
      argLocations[i].block!.Operations.Insert(argLocations[i].opIndex, storeOp);
      _variables.Declare(pinName, kind, true, args[i]!, argLocations[i].block!);
      // Reload in the current (final) block
      var reloadOp = new MaxonVarRefOp(pinName, kind);
      finalBlock.AddOp(reloadOp);
      args[i] = reloadOp.Result;
    }
  }

  private void ParseNamedArg(MlirFunction<MaxonOp> callee, MaxonValue?[] args, Dictionary<string, MlirType>? typeParams = null, bool[]? argMutabilities = null, string?[]? argVarNames = null) {
    var nameToken = Advance();
    Advance(); // consume ':'
    var idx = callee.ParamNames.IndexOf(nameToken.Value);
    if (idx < 0)
      throw new CompileError(ErrorCode.SemanticUndefinedVariable, $"unknown parameter name: '{nameToken.Value}'", nameToken.Line, nameToken.Column);
    args[idx] = ParseCallArgValue(callee.ParamTypes[idx], typeParams);
    if (argMutabilities != null) argMutabilities[idx] = _lastExprWasMutableVar;
    if (argVarNames != null) argVarNames[idx] = _lastExprVarName;
  }

  /// <summary>
  /// Parses call arguments for an instance method call, pre-filling the self argument at index 0.
  /// The first explicit argument is positional (maps to index 1), subsequent args must be named.
  /// </summary>
  private (List<MaxonValue> args, MlirFunction<MaxonOp> callee) ParseInstanceMethodCallArgs(Token methodNameToken, MaxonValue selfValue) {
    // Capture self mutability and var name before argument parsing overwrites them
    var selfMutable = _lastExprWasMutableVar;
    var selfVarName = _lastExprVarName;

    var candidates = ResolveFunctionOverloads(methodNameToken.Value);
    var callee = SelectOverloadByNamedArgs(candidates, methodNameToken);

    var args = new MaxonValue?[callee.ParamTypes.Count];
    args[0] = selfValue;

    var argMuts = new bool[callee.ParamTypes.Count];
    argMuts[0] = selfMutable;
    var argNames = new string?[callee.ParamTypes.Count];
    argNames[0] = selfVarName;

    // Resolve type parameters from the self value's concrete struct type
    Dictionary<string, MlirType>? typeParams = null;
    if (selfValue is MaxonStruct ms && _typeRegistry.TryGetValue(ms.TypeName, out var selfType) && selfType is MlirStructType selfStructType && selfStructType.TypeParams.Count > 0) {
      typeParams = BuildFullTypeParams(ms.TypeName, selfStructType);
    }

    if (!Check(TokenType.RightParen)) {
      ParseArgList(methodNameToken, callee, args, firstPositionalIndex: 1, typeParams, argMutabilities: argMuts, argVarNames: argNames);
    }

    Expect(TokenType.RightParen);
    _lastArgMutabilities = [.. argMuts];
    _lastArgVarNames = [.. argNames];
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

    // When return type is a struct with unresolved type params (e.g., ElementArray from
    // Iterable extension with Element still abstract), resolve through the self arg's
    // concrete element type to find/create the right concrete alias.
    if (returnType is MlirStructType returnStruct
        && args.Count > 0 && args[0] is MaxonStruct selfStruct) {
      // Check if any type param is unresolved: either a direct type parameter, or
      // a struct type that is itself an inner type alias with unresolved params
      // Check for unresolved type params. Use the registry to get current type info
      // since conformance clause references may be stale (pointing to pre-registered placeholders).
      bool hasUnresolved = returnStruct.TypeParams.Values.Any(t => {
        if (t is MlirTypeParameterType) return true;
        if (t is MlirStructType st && _typeAliasSources.ContainsKey(st.Name)) {
          var current = _typeRegistry.TryGetValue(st.Name, out var reg) && reg is MlirStructType regSt ? regSt : st;
          return current.TypeParams.Values.Any(inner => inner is MlirTypeParameterType);
        }
        return false;
      });
      if (hasUnresolved) {
        var resolved = ResolveStructReturnTypeThroughSelf(returnStruct, selfStruct.TypeName);
        if (resolved != null) return (MaxonValueKind.Struct, resolved);
      }

      // When return type is a type alias with already-resolved params (e.g., EnumSelf with
      // Source=CodepointView, Element=Codepoint from extension on a non-alias Iterable type),
      // find or create the concrete specialization alias for monomorphization.
      // Only applies when self is a non-alias concrete type — alias types are handled by
      // the hasUnresolved path above.
      // Skip when the return type itself is already a registered concrete alias whose
      // type params match what's in the registry (e.g., StringArray with Element=String) —
      // no need to create a mangled duplicate like __Array_String.
      if (!hasUnresolved && returnStruct.TypeParams.Count > 0
          && !_typeAliasSources.ContainsKey(selfStruct.TypeName)
          && _typeAliasSources.TryGetValue(returnStruct.Name, out var returnSourceName)
          && _typeRegistry.TryGetValue(returnSourceName, out var returnSourceReg)
          && returnSourceReg is MlirStructType returnSourceStruct
          && !returnStruct.TypeParams.Values.Any(t => t is MlirTypeParameterType)) {
        // If the return type is already registered with the same concrete type params,
        // use it directly instead of creating a mangled duplicate.
        if (_typeRegistry.TryGetValue(returnStruct.Name, out var existingReg)
            && existingReg is MlirStructType existingStruct
            && existingStruct.TypeParams.Count == returnStruct.TypeParams.Count
            && existingStruct.TypeParams.All(kv =>
                returnStruct.TypeParams.TryGetValue(kv.Key, out var rv) && rv.Name == kv.Value.Name)) {
          return (MaxonValueKind.Struct, returnStruct.Name);
        }
        var mangledName = $"__{returnSourceName}_{string.Join("_", returnStruct.TypeParams.Values.Select(t => t.Name))}";
        if (!_typeRegistry.ContainsKey(mangledName)) {
          RegisterConcreteTypeAlias(mangledName, returnSourceName, returnSourceStruct, new(returnStruct.TypeParams));
        }
        return (MaxonValueKind.Struct, mangledName);
      }
    }

    var kind = returnType.ToValueKind();
    var typeName = returnType switch {
      MlirStructType s => s.Name,
      MlirUnionType e => e.Name,
      _ => (string?)null
    };
    return (kind, typeName);
  }

  /// <summary>
  /// Resolves a struct return type with unresolved type params by tracing through the self
  /// type's type param chain. For example, when Iterable.map() returns ElementArray (Array
  /// with unresolved Element), and self is __Map_String_i64, resolves Element through the
  /// Map source type's bindings to find the concrete array alias.
  /// </summary>
  private string? ResolveStructReturnTypeThroughSelf(MlirStructType returnStruct, string selfTypeName) {
    // Get the source type for the self (e.g., __Map_String_i64 -> Map)
    MlirStructType sourceStruct;
    MlirStructType selfStruct;
    if (_typeAliasSources.TryGetValue(selfTypeName, out var sourceTypeName)) {
      if (!_typeRegistry.TryGetValue(sourceTypeName, out var sourceRegType)) return null;
      if (sourceRegType is not MlirStructType ss) return null;
      sourceStruct = ss;
      if (!_typeRegistry.TryGetValue(selfTypeName, out var selfRegType)) return null;
      if (selfRegType is not MlirStructType self) return null;
      selfStruct = self;
    } else {
      // Non-alias types (e.g., CodepointView implementing Iterable with Codepoint):
      // the type is its own source, and its TypeParams come from the conformance clause
      if (!_typeRegistry.TryGetValue(selfTypeName, out var selfRegType)) return null;
      if (selfRegType is not MlirStructType self) return null;
      sourceStruct = self;
      selfStruct = self;
    }

    // Build the resolution map from return type's unresolved params to concrete types.
    var resolvedReturnParams = new Dictionary<string, MlirType>();
    foreach (var (paramName, paramType) in returnStruct.TypeParams) {
      if (paramType is MlirTypeParameterType tp) {
        // Self type parameter resolves to the concrete self type
        if (tp.ParameterName == "Self") {
          resolvedReturnParams[paramName] = selfStruct;
          continue;
        }
        // Direct type parameter (e.g., Element -> Element(tp)): look up in source type
        if (sourceStruct.TypeParams.TryGetValue(tp.ParameterName, out var sourceBinding)) {
          if (sourceBinding is MlirStructType innerAlias) {
            var resolved = ResolveInnerAliasToConcreteType(innerAlias.Name, selfStruct.TypeParams);
            if (resolved != null) { resolvedReturnParams[paramName] = resolved; continue; }
          }
          // Source binding is still unresolved — resolve through the concrete alias's TypeParams
          if (sourceBinding is MlirTypeParameterType sourceTp
              && selfStruct.TypeParams.TryGetValue(sourceTp.ParameterName, out var concreteSelfBinding)) {
            resolvedReturnParams[paramName] = concreteSelfBinding;
          } else {
            resolvedReturnParams[paramName] = sourceBinding;
          }
        }
        // Generic base types with no TypeParams — fall back to the
        // concrete alias's TypeParams (e.g., StringArray.TypeParams["Element"] = String)
        else if (selfStruct.TypeParams.TryGetValue(tp.ParameterName, out var selfBinding)) {
          resolvedReturnParams[paramName] = selfBinding;
        }
      } else if (paramType is MlirStructType innerStruct
                 && _typeAliasSources.ContainsKey(innerStruct.Name)) {
        // Look up current type info from registry (conformance refs may be stale placeholders)
        var currentInner = _typeRegistry.TryGetValue(innerStruct.Name, out var innerReg) && innerReg is MlirStructType regInner ? regInner : innerStruct;
        if (currentInner.TypeParams.Values.Any(inner => inner is MlirTypeParameterType)) {
          // Struct is an inner alias with unresolved type params (e.g., Element -> Entry
          // where Entry = Pair with (Key, Value)). Resolve through self's substitutions.
          var resolved = ResolveInnerAliasToConcreteType(innerStruct.Name, selfStruct.TypeParams);
          if (resolved != null) { resolvedReturnParams[paramName] = resolved; continue; }
        }
        resolvedReturnParams[paramName] = paramType;
      } else {
        resolvedReturnParams[paramName] = paramType;
      }
    }

    // All params resolved? Find or create concrete alias.
    if (resolvedReturnParams.Values.Any(t => t is MlirTypeParameterType)) return null;

    // Find source for return type (e.g., ElementArray -> Array)
    if (!_typeAliasSources.TryGetValue(returnStruct.Name, out var returnSourceName)) return null;

    // Search for existing alias matching the resolved params
    foreach (var (aliasName, aliasSource) in _typeAliasSources) {
      if (aliasSource != returnSourceName) continue;
      if (!_typeRegistry.TryGetValue(aliasName, out var aliasRegType)) continue;
      if (aliasRegType is not MlirStructType aliasSt) continue;
      if (aliasSt.TypeParams.Count != resolvedReturnParams.Count) continue;
      if (aliasSt.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;
      bool match = true;
      foreach (var (pn, pt) in resolvedReturnParams) {
        if (!aliasSt.TypeParams.TryGetValue(pn, out var ct) || ct.Name != pt.Name) { match = false; break; }
      }
      if (match) return aliasName;
    }

    // No existing alias — auto-create one
    if (resolvedReturnParams.Count == 1 && resolvedReturnParams.TryGetValue("Element", out var elemType)) {
      if (elemType is MlirStructType elemStruct) {
        return FindArrayTypeAliasForElement(MaxonValueKind.Struct, elemStruct.Name);
      }
      if (elemType is MlirUnionType elemEnum) {
        return FindArrayTypeAliasForElement(MaxonValueKind.Enum, elemEnum.Name);
      }
      var elemKind = elemType.ToValueKind();
      return FindArrayTypeAliasForElement(elemKind);
    }

    // Auto-create concrete alias for multi-param generic types (e.g., EnumeratedIterator with Source, Element)
    if (_typeRegistry.TryGetValue(returnSourceName, out var returnSourceReg)
        && returnSourceReg is MlirStructType returnSourceStruct) {
      var mangledName = $"__{returnSourceName}_{string.Join("_", resolvedReturnParams.Values.Select(t => t.Name))}";
      if (!_typeRegistry.ContainsKey(mangledName)) {
        RegisterConcreteTypeAlias(mangledName, returnSourceName, returnSourceStruct, new(resolvedReturnParams));
      }
      return mangledName;
    }

    return null;
  }

  /// <summary>
  /// Resolves an inner type alias (e.g., Entry = Pair with (Key, Value)) to a concrete type
  /// using the outer type's resolved type params (e.g., Key=String, Value=i64).
  /// </summary>
  private MlirStructType? ResolveInnerAliasToConcreteType(string innerAliasName, Dictionary<string, MlirType> outerTypeParams) {
    if (!_typeRegistry.TryGetValue(innerAliasName, out var innerRegType)) return null;
    if (innerRegType is not MlirStructType innerStruct) return null;
    if (!_typeAliasSources.TryGetValue(innerAliasName, out var innerSourceName)) return null;

    // Resolve inner alias's type params through outer substitution
    var resolvedInnerParams = new Dictionary<string, MlirType>();
    foreach (var (pn, pt) in innerStruct.TypeParams) {
      if (pt is MlirTypeParameterType tp && outerTypeParams.TryGetValue(tp.ParameterName, out var resolved))
        resolvedInnerParams[pn] = resolved;
      else
        resolvedInnerParams[pn] = pt;
    }

    // If still unresolved, give up
    if (resolvedInnerParams.Values.Any(t => t is MlirTypeParameterType)) return null;

    // Find concrete alias matching these resolved params
    foreach (var (aliasName, aliasSource) in _typeAliasSources) {
      if (aliasSource != innerSourceName) continue;
      if (!_typeRegistry.TryGetValue(aliasName, out var aliasRegType)) continue;
      if (aliasRegType is not MlirStructType aliasSt) continue;
      if (aliasSt.TypeParams.Count != resolvedInnerParams.Count) continue;
      if (aliasSt.TypeParams.Values.Any(t => t is MlirTypeParameterType)) continue;
      bool match = true;
      foreach (var (pn, pt) in resolvedInnerParams) {
        if (!aliasSt.TypeParams.TryGetValue(pn, out var ct) || ct.Name != pt.Name) { match = false; break; }
      }
      if (match) return aliasSt;
    }

    return null;
  }

  /// <summary>
  /// Builds a full type param map for a concrete alias, including conformance-derived params.
  /// For __Map_String_i64, the direct TypeParams are {Key: String, Value: i64}.
  /// The source type Map has conformance 'Iterable with Entry', binding Element → Entry.
  /// This method resolves Entry → StringIntPair and adds Element → StringIntPair to the map.
  /// </summary>
  private Dictionary<string, MlirType> BuildFullTypeParams(string aliasName, MlirStructType aliasStruct) {
    var result = new Dictionary<string, MlirType>(aliasStruct.TypeParams);

    // Look up source type to get conformance-bound type params
    if (!_typeAliasSources.TryGetValue(aliasName, out var sourceName)) return result;
    if (!_typeRegistry.TryGetValue(sourceName, out var sourceTypeReg)) return result;
    if (sourceTypeReg is not MlirStructType sourceStruct) return result;

    foreach (var (paramName, paramValue) in sourceStruct.TypeParams) {
      if (result.ContainsKey(paramName)) continue;
      // paramValue is a conformance-bound type like Entry (an inner alias)
      if (paramValue is MlirStructType innerStruct && _typeAliasSources.ContainsKey(innerStruct.Name)) {
        var resolved = ResolveInnerAliasToConcreteType(innerStruct.Name, result);
        if (resolved != null) {
          result[paramName] = resolved;
          // Also map the inner alias name itself (e.g., Entry → StringIntPair)
          // so function types using the inner alias name can be resolved
          if (!result.ContainsKey(innerStruct.Name))
            result[innerStruct.Name] = resolved;
        }
      }
    }

    return result;
  }

  /// <summary>
  /// Resolves type parameter types within a function type using the given type params map.
  /// For example, fn(Element) returns Element with {Element: StringIntPair} becomes
  /// fn(StringIntPair) returns StringIntPair.
  /// </summary>
  private static MlirFunctionType ResolveFunctionType(MlirFunctionType ft, Dictionary<string, MlirType> typeParams) {
    var newParams = ft.ParameterTypes.Select(p => ResolveTypeParam(p, typeParams)).ToList();
    var newReturn = ft.ReturnType != null ? ResolveTypeParam(ft.ReturnType, typeParams) : null;
    if (!newParams.SequenceEqual(ft.ParameterTypes) || newReturn != ft.ReturnType)
      return new MlirFunctionType(newParams, newReturn);
    return ft;
  }

  /// <summary>
  /// Resolves a single type through the type params map, including inner alias resolution.
  /// </summary>
  private static MlirType ResolveTypeParam(MlirType type, Dictionary<string, MlirType> typeParams) {
    if (type is MlirTypeParameterType tp && typeParams.TryGetValue(tp.ParameterName, out var resolved))
      return resolved;
    if (type is MlirStructType st && typeParams.TryGetValue(st.Name, out var resolvedStruct))
      return resolvedStruct;
    return type;
  }

  /// Validates that a throwing function is called within a try context.
  private void ValidateThrowingCallContext(MlirFunction<MaxonOp> callee, Token functionNameToken, string displayName) {
    if (callee.ThrowsType == null || _inTryContext) return;

    if (Check(TokenType.Otherwise)) {
      throw new CompileError(ErrorCode.SemanticOtherwiseRequiresTry,
        "otherwise requires try expression",
        Current().Line, Current().Column);
    }
    throw new CompileError(ErrorCode.SemanticThrowingFunctionRequiresTry,
      $"throwing function requires try: '{displayName}'",
      functionNameToken.Line, functionNameToken.Column);
  }

  /// <summary>
  /// Creates a function call operation, validating the function exists and determining the result type.
  /// Handles struct return types by setting ResultStructTypeName.
  /// </summary>
  private MaxonCallOp CreateFunctionCall(Token functionNameToken, List<MaxonValue> args) {
    var functionName = functionNameToken.Value;
    var callee = ResolveFunctionName(functionName, functionNameToken);

    ValidateThrowingCallContext(callee, functionNameToken, functionName);

    var (resultKind, resultStructTypeName) = ResolveCallResultType(callee.ReturnType, args);
    var callOp = new MaxonCallOp(callee.Name, args, resultKind, resultStructTypeName) {
      ArgMutabilities = _lastArgMutabilities,
      ArgVarNames = _lastArgVarNames,
      CallLine = functionNameToken.Line,
      CallColumn = functionNameToken.Column
    };
    _lastArgMutabilities = null;
    _lastArgVarNames = null;
    _currentBlock!.AddOp(callOp);
    _lastRangedTypeName = null;
    // Struct call returns need a temp variable so refcounting tracks the intermediate
    if (callOp.Result != null && resultKind == MaxonValueKind.Struct) {
      EmitCallReturnTempAssign(callOp, resultKind.Value, resultStructTypeName);
    }
    return callOp;
  }

  /// <summary>
  /// Creates a function call operation using a pre-resolved callee (from overload resolution).
  /// Skips function lookup since the callee was already selected by ParseCallArgs/ParseInstanceMethodCallArgs.
  /// </summary>
  private MaxonCallOp CreateFunctionCall(Token functionNameToken, List<MaxonValue> args, MlirFunction<MaxonOp> callee) {
    var functionName = functionNameToken.Value;

    ValidateThrowingCallContext(callee, functionNameToken, UnmangleName(functionName));

    var (resultKind, resultStructTypeName) = ResolveCallResultType(callee.ReturnType, args);
    var callOp = new MaxonCallOp(callee.Name, args, resultKind, resultStructTypeName) {
      ArgMutabilities = _lastArgMutabilities,
      ArgVarNames = _lastArgVarNames,
      CallLine = functionNameToken.Line,
      CallColumn = functionNameToken.Column
    };
    _lastArgMutabilities = null;
    _lastArgVarNames = null;
    _currentBlock!.AddOp(callOp);
    _lastRangedTypeName = null;
    // Struct call returns need a temp variable so refcounting tracks the intermediate
    if (callOp.Result != null && resultKind == MaxonValueKind.Struct) {
      EmitCallReturnTempAssign(callOp, resultKind.Value, resultStructTypeName);
    }
    return callOp;
  }

  /// <summary>
  /// Parses an async function call: `async functionName(args...)`.
  /// Spawns a green thread and returns a promise.
  /// </summary>
  private ExprResult.Direct ParseAsyncCall(Token asyncToken) {
    var nameToken = Expect(TokenType.Identifier);
    Token funcToken;
    // Support qualified method calls: async TypeName.method(args)
    if (Check(TokenType.Dot) && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.Identifier) {
      var qualifiedName = $"{nameToken.Value}.{_tokens[_pos + 1].Value}";
      var resolved = ResolveMethodName(qualifiedName);
      if (resolved != null) {
        Advance(); // consume '.'
        Advance(); // consume method name
        funcToken = new Token(TokenType.Identifier, resolved, nameToken.Line, nameToken.Column);
      } else {
        funcToken = nameToken;
      }
    } else {
      funcToken = nameToken;
    }
    Expect(TokenType.LeftParen);
    var argsStartPos = _pos; // position right after '('
    var (args, callee) = ParseCallArgs(funcToken);
    var argsEndPos = _pos; // position right after ')'

    var (resultKind, resultStructTypeName) = ResolveCallResultType(callee.ReturnType, args);
    // Build source text for error messages: "async funcName(arg1, arg2, ...)"
    // Reconstruct the argument text from tokens between the parens
    var argParts = new System.Text.StringBuilder();
    var needSep = false;
    for (var i = argsStartPos; i < argsEndPos && i < _tokens.Count; i++) {
      if (_tokens[i].Type is TokenType.LeftParen or TokenType.RightParen) continue;
      if (_tokens[i].Type == TokenType.Comma) {
        argParts.Append(", ");
        needSep = false;
        continue;
      }
      if (_tokens[i].Type == TokenType.Colon) {
        argParts.Append(": ");
        needSep = false;
        continue;
      }
      if (needSep) argParts.Append(' ');
      argParts.Append(_tokens[i].Value);
      needSep = true;
    }
    var sourceText = $"async {nameToken.Value}({argParts})";
    var asyncOp = new MaxonAsyncCallOp(callee.Name, args, resultKind, resultStructTypeName, throws: callee.ThrowsType != null) {
      ArgMutabilities = _lastArgMutabilities,
      ArgVarNames = _lastArgVarNames,
      CallLine = asyncToken.Line,
      CallColumn = asyncToken.Column,
      CallSourceText = sourceText,
    };
    _lastArgMutabilities = null;
    _lastArgVarNames = null;
    _currentBlock!.AddOp(asyncOp);
    return new ExprResult.Direct(asyncOp.Result);
  }

  /// <summary>
  /// Parses an await expression: `await promise`.
  /// Waits for the green thread to complete and returns its result.
  /// </summary>
  private ExprResult.Direct ParseAwaitExpression(Token awaitToken) {
    var inner = ParsePrimary();
    var innerVal = ResolveExprValue(inner);

    if (innerVal is not MaxonPromise promise) {
      var kindName = DetermineValueKind(innerVal).ToString().ToLowerInvariant();
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"'await' requires a promise value from 'async', got {kindName}",
        awaitToken.Line, awaitToken.Column);
    }

    var awaitOp = new MaxonAwaitOp(promise, promise.InnerKind, promise.InnerStructTypeName);
    _currentBlock!.AddOp(awaitOp);
    if (awaitOp.Result != null)
      return new ExprResult.Direct(awaitOp.Result);
    // Void promise: the await has no result value. Statement-form callers discard this.
    // Return the (already-defined) promise value as a sentinel; any attempt to use it
    // in a value context would be a semantic error caught by type checking.
    return new ExprResult.Direct(promise);
  }

  /// <summary>
  /// Returns the variables to clean at scope end, with user-declared variables listed before
  /// internal temp variables (__call_tmp_, __try_result_). This ensures user vars are decreffed
  /// first, which matches the expected cleanup order in specs.
  /// </summary>
  private List<string> GetScopeEndVars() {
    return _variables.GetScopeEndVars();
  }

  /// <summary>
  /// When a named variable takes ownership of a value previously held by a temp variable,
  /// fix up _variables to prevent double-decref or incorrect scope_end ordering.
  ///
  /// Called AFTER adding the named variable to _variables and its assign op to the block.
  ///
  /// For CallReturn temps: remove entirely — the named var is the sole owner.
  /// For non-CallReturn temps (like __try_result_): re-insert at end for ordering.
  /// </summary>
  private void FixupTempOwnership() {
    // The last op is the named var's assign — look one op back for the backing temp.
    var ops = _currentBlock!.Operations;
    var prevOp = ops.Count >= 2 ? ops[^2] : null;
    string? backedByVar = prevOp switch {
      MaxonStructVarRefOp sv => sv.VarName,
      MaxonEnumVarRefOp ev => ev.VarName,
      MaxonAssignOp { IsDeclaration: true } av when _variables.HasFlag(av.VarName, OwnershipFlags.CallReturn) => av.VarName,
      _ => null
    };
    if (backedByVar != null) {
      // Propagate CallReturn to the user var's assign so the lowering skips incref
      if (_variables.HasFlag(backedByVar, OwnershipFlags.CallReturn) && ops[^1] is MaxonAssignOp userAssign) {
        userAssign.OwnerFlags = (userAssign.OwnerFlags ?? OwnershipFlags.None) | OwnershipFlags.CallReturn;
      }
      _variables.TransferTempOwnership(backedByVar);
    }
  }

  /// <summary>
  /// Assigns a struct call return to a temp variable so refcounting can track the intermediate.
  /// Without this, chained method calls like a.foo().bar() create untracked struct references.
  /// </summary>
  private void EmitCallReturnTempAssign(MaxonCallOp callOp, MaxonValueKind resultKind, string? resultStructTypeName) {
    var tempName = $"__call_tmp_{callOp.Result!.Id}";
    var assignOp = new MaxonAssignOp(tempName, callOp.Result, true, false, resultKind) {
      OwnerFlags = OwnershipFlags.IsTemp | OwnershipFlags.CallReturn
    };
    _currentBlock!.AddOp(assignOp);
    _variables.Declare(tempName, resultKind, false, callOp.Result, _currentBlock!,
      OwnershipFlags.IsTemp | OwnershipFlags.CallReturn, structTypeName: resultStructTypeName);
  }

  /// <summary>
  /// Assigns a struct literal (string/char/byte-string) to a temp variable so the scope
  /// cleanup can decref it at block exit. Without this, literals used as arguments or in
  /// expressions without being assigned to a named var are leaked.
  /// </summary>
  private void EmitLiteralTempAssign(MaxonStruct litResult) {
    var tempName = $"__lit_tmp_{litResult.Id}";
    var assignOp = new MaxonAssignOp(tempName, litResult, true, false, MaxonValueKind.Struct) {
      OwnerFlags = OwnershipFlags.IsTemp
    };
    _currentBlock!.AddOp(assignOp);
    _variables.Declare(tempName, MaxonValueKind.Struct, false, litResult, _currentBlock!,
      OwnershipFlags.IsTemp, structTypeName: litResult.TypeName);
  }

  private static void MarkDiscardedResult(MaxonCallOp callOp, Token callToken) {
    if (callOp.Result != null) {
      callOp.IsDiscardedResult = true;
      callOp.CallLine = callToken.Line;
      callOp.CallColumn = callToken.Column;
    }
  }

  // Marks the last call op as explicitly discarded via `let _ = func()`
  private void MarkLetDiscardResult(Token discardToken) {
    // Check the last op in the current block first (simple case: `let _ = func()`)
    if (_currentBlock!.Operations.Count > 0) {
      var lastOp = _currentBlock!.Operations[^1];
      if (lastOp is MaxonCallOp callOp && callOp.Result != null) {
        callOp.IsLetDiscardResult = true;
        callOp.CallLine = discardToken.Line;
        callOp.CallColumn = discardToken.Column;
        return;
      }
      // EmitCallReturnTempAssign may have added a __call_tmp_ assign after the call op
      if (lastOp is MaxonAssignOp { VarName: var name } && name.StartsWith("__call_tmp_")
          && _currentBlock!.Operations.Count > 1
          && _currentBlock!.Operations[^2] is MaxonCallOp callOp2 && callOp2.Result != null) {
        callOp2.IsLetDiscardResult = true;
        callOp2.CallLine = discardToken.Line;
        callOp2.CallColumn = discardToken.Column;
        return;
      }
    }
    // Fall back to the last try call op (handles try...otherwise which spans blocks)
    if (_lastExprCallOp is MaxonTryCallOp && _lastExprCallOp.Result != null) {
      _lastExprCallOp.IsLetDiscardResult = true;
      _lastExprCallOp.CallLine = discardToken.Line;
      _lastExprCallOp.CallColumn = discardToken.Column;
    }
  }

  // ============================================================================
  // Helpers
  // ============================================================================

  /// <summary>
  /// Translates a comparison operator into an Ordering enum ordinal check.
  /// Ordering ordinals: lessThan=0, equalTo=1, greaterThan=2.
  /// </summary>
  private static (MaxonBinOperator Operator, long OrdinalValue) OrderingCheckForOperator(MaxonBinOperator op) => op switch {
    MaxonBinOperator.Lt => (MaxonBinOperator.Eq, 0L),
    MaxonBinOperator.Gt => (MaxonBinOperator.Eq, 2L),
    MaxonBinOperator.Le => (MaxonBinOperator.Ne, 2L),
    MaxonBinOperator.Ge => (MaxonBinOperator.Ne, 0L),
    _ => throw new InvalidOperationException($"Unexpected comparison operator for Ordering check: {op}")
  };

  /// <summary>
  /// Emits a comparison of a compare() result (Ordering enum) against an ordinal value.
  /// Returns a bool-typed MaxonValue representing the comparison result.
  /// </summary>
  private MaxonValue EmitOrderingCheck(MaxonValue compareResult, MaxonBinOperator op) {
    var (cmpOperator, ordinalValue) = OrderingCheckForOperator(op);
    var ordinalLit = new MaxonLiteralOp(ordinalValue);
    _currentBlock!.AddOp(ordinalLit);
    var cmpOp = new MaxonBinOp(cmpOperator, compareResult, ordinalLit.Result, MaxonValueKind.Integer);
    _currentBlock!.AddOp(cmpOp);
    return cmpOp.Result;
  }

  /// <summary>
  /// For generic methods returning a TypeParameter placeholder, checks if the caller's
  /// struct type has a concrete Element type that should override the result kind.
  /// </summary>
  private (MaxonValueKind Kind, string? StructTypeName) OverrideResultKindForElementType(MaxonValueKind resultKind, string? resultStructTypeName, List<MaxonValue> args) {
    if (resultKind != MaxonValueKind.TypeParameter || args.Count == 0)
      return (resultKind, resultStructTypeName);

    // Resolve the self arg's struct type name — either directly from MaxonStruct,
    // or by looking up the variable in the registry (needed for module-level arrays
    // in multi-file builds where args[0] is a plain MaxonValue, not MaxonStruct)
    string? selfTypeName = null;
    if (args[0] is MaxonStruct selfStruct) {
      selfTypeName = selfStruct.TypeName;
    } else {
      foreach (var varInfo in _variables.Values) {
        if (varInfo.Value == args[0] && varInfo.StructTypeName != null) {
          selfTypeName = varInfo.StructTypeName;
          break;
        }
      }
    }

    if (selfTypeName != null
        && _typeRegistry.TryGetValue(selfTypeName, out var selfType)
        && selfType is MlirStructType selfStructType) {
      // Check common type parameter names: "Element" (Array/Iterable), "Value" (Map), "Key" (Map)
      MlirType? resolvedType = null;
      if (selfStructType.TypeParams.TryGetValue("Element", out var elementType))
        resolvedType = elementType;
      else if (selfStructType.TypeParams.TryGetValue("Value", out var valueType))
        resolvedType = valueType;

      if (resolvedType != null) {
        if (resolvedType is MlirTypeParameterType) return (resultKind, resultStructTypeName);
        var kind = resolvedType.ToValueKind();
        var typeName = resolvedType switch {
          MlirStructType s => s.Name,
          MlirUnionType e => e.Name,
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
    if (attr is IntegerAttr intAttr && type == MlirType.I1) {
      var defaultOp = new MaxonLiteralOp(intAttr.Value != 0);
      _currentBlock!.AddOp(defaultOp);
      return defaultOp.Result;
    }
    if (attr is IntegerAttr intAttr2) {
      var defaultOp = new MaxonLiteralOp(intAttr2.Value);
      _currentBlock!.AddOp(defaultOp);
      return defaultOp.Result;
    }
    if (attr is FloatAttr floatAttr) {
      var defaultOp = new MaxonLiteralOp(floatAttr.Value);
      _currentBlock!.AddOp(defaultOp);
      return defaultOp.Result;
    }
    if (attr is EnumAttr enumAttr) {
      var enumType = (MlirUnionType)_typeRegistry[enumAttr.EnumTypeName];
      var enumCase = enumType.GetCase(enumAttr.CaseName)!;
      MaxonEnumLiteralOp enumOp;
      if (enumType.BackingType == MlirType.F64) {
        enumOp = new MaxonEnumLiteralOp(enumAttr.EnumTypeName, enumAttr.CaseName, (double)enumCase.RawValue!);
      } else if (enumType.BackingType == MlirType.I64) {
        enumOp = new MaxonEnumLiteralOp(enumAttr.EnumTypeName, enumAttr.CaseName, (long)enumCase.RawValue!);
      } else {
        enumOp = new MaxonEnumLiteralOp(enumAttr.EnumTypeName, enumAttr.CaseName, (long)enumCase.Ordinal);
      }
      _currentBlock!.AddOp(enumOp);
      return enumOp.Result;
    }
    if (attr is TokenRangeAttr tokenRange) {
      return EmitDefaultFromTokens(tokenRange);
    }
    throw new CompileError(ErrorCode.ParserExpectedExpression, context, errorToken.Line, errorToken.Column);
  }

  /// Re-parses stored default value tokens via ParseExpression(), so any literal
  /// expression (strings, arrays, structs, characters, etc.) works as a default.
  private MaxonValue EmitDefaultFromTokens(TokenRangeAttr tokenRange) {
    var savedPos = _pos;
    var savedTokens = _tokens;
    // Inject the stored tokens plus an EOF sentinel so ParseExpression stops cleanly
    _tokens = [.. tokenRange.Tokens, new Token(TokenType.Eof, "", 0, 0)];
    _pos = 0;
    var result = ResolveExprValue(ParseExpression());
    _tokens = savedTokens;
    _pos = savedPos;
    return result;
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
      MaxonShort => MaxonValueKind.Short,
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

    // After param name, must see a type token (typed closure)
    if (_pos + lookahead >= _tokens.Count)
      return false;
    var afterName = _tokens[_pos + lookahead].Type;
    if (afterName == TokenType.Identifier || afterName == TokenType.Int || afterName == TokenType.Float ||
           afterName == TokenType.Bool || afterName == TokenType.Byte || afterName == TokenType.LeftParen)
      return true;

    // Untyped closure: (name) gives or (name, name, ...) gives
    // Skip comma-separated names until we hit ')'
    while (_pos + lookahead < _tokens.Count) {
      if (_tokens[_pos + lookahead].Type == TokenType.RightParen) {
        lookahead++;
        return _pos + lookahead < _tokens.Count && _tokens[_pos + lookahead].Type == TokenType.Gives;
      }
      if (_tokens[_pos + lookahead].Type == TokenType.Comma) {
        lookahead++;
        if (_pos + lookahead >= _tokens.Count || _tokens[_pos + lookahead].Type != TokenType.Identifier)
          return false;
        lookahead++;
      } else {
        return false;
      }
    }
    return false;
  }

  /// <summary>
  /// Parses a closure expression: (param type, ...) gives expr
  /// Creates an anonymous function and returns a reference to it.
  /// When inferredFnType is provided, parameters without type annotations
  /// use the inferred types from the calling context, and the return type
  /// is set to enable struct literal parsing in the body.
  /// </summary>
  private ExprResult.Direct ParseClosure(MlirFunctionType? inferredFnType = null) {
    Expect(TokenType.LeftParen);

    // Parse closure parameters
    var paramNames = new List<string>();
    var paramTypes = new List<MlirType>();
    var paramTokens = new List<Token>();

    if (!Check(TokenType.RightParen)) {
      int paramIdx = 0;
      do {
        var paramToken = Expect(TokenType.Identifier);
        var paramName = paramToken.Value;
        paramNames.Add(paramName);
        paramTokens.Add(paramToken);
        // Check if next token is ')' or ',' — untyped parameter, use inferred type
        if (Check(TokenType.RightParen) || Check(TokenType.Comma)) {
          if (inferredFnType != null && paramIdx < inferredFnType.ParameterTypes.Count) {
            paramTypes.Add(inferredFnType.ParameterTypes[paramIdx]);
          } else {
            throw new CompileError(ErrorCode.ParserExpectedType,
              $"Cannot infer type for closure parameter '{paramName}'. Add an explicit type annotation.",
              Current().Line, Current().Column);
          }
        } else {
          var paramType = ParseTypeRef();
          paramTypes.Add(paramType);
        }
        paramIdx++;
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    Expect(TokenType.RightParen);
    Expect(TokenType.Gives);

    // Determine return type from expression (we'll infer it after parsing)
    // Create a unique name for the closure
    var closureName = $"__closure_{_closureCounter++}";

    // Save current parsing state
    var savedVars = _variables.SaveAll();
    var savedBlock = _currentBlock;
    var savedFunction = _currentFunction;
    var savedReferencedVars = new HashSet<string>(_referencedVars);
    var savedClosureCaptures = _closureCaptures;
    var savedParamLocations = new List<(string, int, int)>(_paramLocations);
    var savedLocalVarLocations = new List<(string, int, int)>(_localVarLocations);
    _paramLocations.Clear();
    _localVarLocations.Clear();

    // Use inferred return type if available, so struct literal syntax works in body
    MlirType? inferredReturnType = inferredFnType?.ReturnType;
    var closureFunc = new MlirFunction<MaxonOp>(closureName, paramNames, paramTypes, inferredReturnType, null) {
      IsStdlib = false,
    };

    _currentFunction = closureFunc;
    _currentBlock = closureFunc.Body.AddBlock("entry");
    _closureCaptures = [];
    _referencedVars.Clear();

    // Build new variable scope: closure params + outer vars marked as captured
    var closureParamNames = new HashSet<string>(paramNames);
    _variables.Clear();

    // Add closure parameters to scope
    for (int i = 0; i < paramNames.Count; i++) {
      if (paramNames[i] != "_" && i < paramTokens.Count) {
        _paramLocations.Add((paramNames[i], paramTokens[i].Line, paramTokens[i].Column));
      }
      if (paramTypes[i] is MlirStructType structType) {
        var structParamOp = new MaxonStructParamOp(i, paramNames[i], structType.Name);
        _currentBlock.AddOp(structParamOp);
        _variables.Declare(paramNames[i], MaxonValueKind.Struct, false, structParamOp.Result, _currentBlock, OwnershipFlags.IsParam, structTypeName: structType.Name);
      } else if (paramTypes[i] is MlirUnionType enumType) {
        var backingKind = GetUnionBackingKind(enumType);
        var paramOp = new MaxonParamOp(i, paramNames[i], backingKind);
        _currentBlock.AddOp(paramOp);
        _variables.Declare(paramNames[i], MaxonValueKind.Enum, false, paramOp.Result, _currentBlock, OwnershipFlags.IsParam, structTypeName: enumType.Name);
      } else {
        var pKind = paramTypes[i].ToValueKind();
        var paramOp = new MaxonParamOp(i, paramNames[i], pKind);
        _currentBlock.AddOp(paramOp);
        _variables.Declare(paramNames[i], pKind, false, paramOp.Result, _currentBlock, OwnershipFlags.IsParam);
      }
    }

    // Add outer variables as captured entries (excluding closure params and 'self')
    foreach (var kv in savedVars) {
      if (!closureParamNames.Contains(kv.Key) && kv.Key != "self") {
        _variables[kv.Key] = kv.Value with { IsCaptured = true };
      }
    }

    // Parse the closure body expression
    var bodyExpr = ParseExpression();
    var bodyValue = ResolveExprValue(bodyExpr);

    // Emit return — compute keepVars to protect returned managed values from scope cleanup
    HashSet<string>? keepVars = null;
    var returnVarName = _lastExprVarName;
    if (returnVarName != null && _variables.ContainsKey(returnVarName)
        && _variables[returnVarName].Kind is MaxonValueKind.Struct or MaxonValueKind.Enum) {
      keepVars = [returnVarName];
    } else if (bodyExpr is ExprResult.Direct) {
      var lastOp = _currentBlock!.Operations.Count > 0 ? _currentBlock.Operations[^1] : null;
      string? backedByVar = lastOp switch {
        MaxonStructVarRefOp sv => sv.VarName,
        MaxonEnumVarRefOp ev => ev.VarName,
        MaxonAssignOp { IsDeclaration: true } av when av.VarName.StartsWith("__call_tmp_") => av.VarName,
        MaxonAssignOp { IsDeclaration: true } av when av.VarName.StartsWith("__lit_tmp_") => av.VarName,
        _ => null
      };
      if (backedByVar != null && _variables.ContainsKey(backedByVar))
        keepVars = [backedByVar];
    }
    _currentBlock.AddOp(new MaxonScopeEndOp(GetScopeEndVars(), keepVars) { VarMetadata = _variables.GetScopeEndVarMetadata() });
    var returnOp = new MaxonReturnOp(bodyValue);
    _currentBlock.AddOp(returnOp);

    // Determine return type: use inferred type if available, otherwise derive from body
    MlirType returnType;
    if (inferredReturnType != null) {
      returnType = inferredReturnType;
    } else {
      var returnKind = DetermineValueKind(bodyValue);
      returnType = returnKind.ToMlirType();
    }

    // Collect captures discovered during body parsing
    var captures = _closureCaptures;
    var hasCaptures = captures.Count > 0;

    // Build the final closure function signature, adding hidden __env param if captures exist
    var finalParamNames = new List<string>(paramNames);
    var finalParamTypes = new List<MlirType>(paramTypes);
    if (hasCaptures) {
      finalParamNames.Add("__env");
      finalParamTypes.Add(MlirType.I64);
      // Prepend __env param op to entry block so it gets stored as a variable
      var envParamOp = new MaxonParamOp(paramNames.Count, "__env", MaxonValueKind.Integer);
      var entryBlock = closureFunc.Body.Blocks[0];
      entryBlock.Operations.Insert(0, envParamOp);
    }

    // Create the final function with proper return type
    var finalClosureFunc = new MlirFunction<MaxonOp>(closureName, finalParamNames, finalParamTypes, returnType, null) {
      IsStdlib = false,
    };
    // Copy the body from the temporary function
    foreach (var block in closureFunc.Body.Blocks) {
      finalClosureFunc.Body.Blocks.Add(block);
    }

    // Add closure function to module
    _currentModule!.Functions.Add(finalClosureFunc);

    CheckUnusedVariables();

    // Restore parsing state
    _variables.RestoreAll(savedVars);
    _currentBlock = savedBlock;
    _currentFunction = savedFunction;
    _referencedVars.Clear();
    foreach (var v in savedReferencedVars) _referencedVars.Add(v);
    // Captured variables are used by the outer function (passed into the closure environment)
    foreach (var c in captures) _referencedVars.Add(c.Name);
    _closureCaptures = savedClosureCaptures;
    _paramLocations.Clear();
    _paramLocations.AddRange(savedParamLocations);
    _localVarLocations.Clear();
    _localVarLocations.AddRange(savedLocalVarLocations);

    // Create a function reference or closure create op
    var fnType = new MlirFunctionType(paramTypes, returnType);
    if (hasCaptures) {
      var capturedValues = captures.Select(c => c.OuterValue).ToList();
      var capturedNames = captures.Select(c => c.Name).ToList();
      var capturedKinds = captures.Select(c => c.Kind).ToList();
      var capturedStructTypes = captures.Select(c => c.StructTypeName).ToList();
      var closureCreateOp = new MaxonClosureCreateOp(closureName, fnType,
        capturedValues, capturedNames, capturedKinds, capturedStructTypes);
      _currentBlock!.AddOp(closureCreateOp);
      return new ExprResult.Direct(closureCreateOp.Result);
    } else {
      var fnRefOp = new MaxonFunctionRefOp(closureName, fnType);
      _currentBlock!.AddOp(fnRefOp);
      return new ExprResult.Direct(fnRefOp.Result);
    }
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

  /// <summary>
  /// Handle #if os(...) / #if arch(...) conditional compilation directive.
  /// Evaluates the condition and skips the inactive branch.
  /// Called from any context where #if may appear (top-level, inside functions, inside types).
  /// </summary>
  private void HandleConditionalCompilation() {
    Advance(); // consume #if
    SkipNewlines(); // skip any newlines between #if and condition

    // Parse condition: os(Windows), os(Linux), arch(x86_64), arch(aarch64)
    var conditionActive = EvaluateConditionalCondition();
    SkipToEndOfLine();
    SkipNewlines();

    if (conditionActive) {
      // Parse the active branch normally — tokens will be consumed by the caller's parsing loop.
      // We just need to watch for #else (skip it) or #endif (done).
      // Do nothing here — the caller will parse tokens until it hits #else or #endif.
    } else {
      // Skip the inactive #if branch
      SkipConditionalBranch();
      SkipNewlines();
      // Now at #else or #endif
      if (Check(TokenType.HashElse)) {
        Advance(); // consume #else
        SkipToEndOfLine();
        SkipNewlines();
        // The #else branch is active — caller will parse normally until #endif
      } else if (Check(TokenType.HashEndif)) {
        Advance(); // consume #endif
        SkipToEndOfLine();
        SkipNewlines();
      }
    }
  }

  /// <summary>
  /// Handle #else when we're in the active #if branch — skip the #else branch.
  /// </summary>
  private void HandleConditionalElse() {
    Advance(); // consume #else
    SkipToEndOfLine();
    SkipNewlines();
    // Skip the #else branch (we already parsed the #if branch)
    SkipConditionalBranch();
    SkipNewlines();
    // Now at #endif
    if (Check(TokenType.HashEndif)) {
      Advance(); // consume #endif
      SkipToEndOfLine();
      SkipNewlines();
    }
  }

  /// <summary>
  /// Handle #endif — just consume it.
  /// </summary>
  private void HandleConditionalEndif() {
    Advance(); // consume #endif
    SkipToEndOfLine();
    SkipNewlines();
  }

  /// <summary>
  /// Evaluate a conditional compilation condition with boolean operators.
  /// Supports: os(X), arch(X), testing(X), `not`, `and`, `or`.
  /// Precedence (lowest→highest): or → and → not → atom.
  /// </summary>
  private bool EvaluateConditionalCondition() {
    var result = EvaluateConditionalOr();
    return result;
  }

  private bool EvaluateConditionalOr() {
    var result = EvaluateConditionalAnd();
    while (Check(TokenType.Or)) {
      Advance();
      result = EvaluateConditionalAnd() | result; // no short-circuit — must consume tokens
    }
    return result;
  }

  private bool EvaluateConditionalAnd() {
    var result = EvaluateConditionalUnary();
    while (Check(TokenType.And)) {
      Advance();
      result = EvaluateConditionalUnary() & result; // no short-circuit — must consume tokens
    }
    return result;
  }

  private bool EvaluateConditionalUnary() {
    if (Check(TokenType.Not)) {
      Advance();
      return !EvaluateConditionalUnary();
    }
    return EvaluateConditionalAtom();
  }

  private bool EvaluateConditionalAtom() {
    var funcToken = Expect(TokenType.Identifier);
    var funcName = funcToken.Value;
    Expect(TokenType.LeftParen);
    // Accept identifier, true, or false as the condition value
    var valueToken = Current().Type is TokenType.True or TokenType.False
      ? Advance()
      : Expect(TokenType.Identifier);
    var value = valueToken.Value;
    Expect(TokenType.RightParen);

    return funcName switch {
      "os" => string.Equals(value, _targetOs, StringComparison.OrdinalIgnoreCase),
      "arch" => string.Equals(value, _targetArch, StringComparison.OrdinalIgnoreCase),
      "testing" => string.Equals(value, _testing.ToString(), StringComparison.OrdinalIgnoreCase),
      _ => throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Unknown conditional compilation function '{funcName}'. Expected 'os', 'arch', or 'testing'.",
        funcToken.Line, funcToken.Column),
    };
  }

  /// <summary>
  /// Skip tokens until a matching #else or #endif is found at the current nesting depth.
  /// Nested #if/#endif pairs are tracked via a depth counter.
  /// </summary>
  private void SkipConditionalBranch() {
    int depth = 1;
    while (!IsAtEnd()) {
      if (Check(TokenType.HashIf)) {
        depth++;
        Advance();
      } else if (Check(TokenType.HashElse) && depth == 1) {
        return; // Don't consume — caller handles it
      } else if (Check(TokenType.HashEndif)) {
        depth--;
        if (depth == 0) return; // Don't consume — caller handles it
        Advance();
      } else {
        Advance();
      }
    }
  }

  private void SkipNewlines() {
    while (Check(TokenType.Newline) || Check(TokenType.DocComment)) {
      Advance();
    }
  }

  /// Require at least one newline after a block label, then skip remaining newlines.
  private void ExpectNewline() {
    if (!Check(TokenType.Newline)) {
      var token = Current();
      throw new CompileError(ErrorCode.ParserUnexpectedToken,
        $"Expected newline after block label, got '{token.Value}'",
        token.Line, token.Column);
    }
    SkipNewlines();
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

  /// If the callee was resolved through a type alias, override it with the alias-qualified name
  /// so Stage 1 monomorphization can match and specialize it correctly.
  private void OverrideCalleeForTypeAlias(MaxonCallOp callOp, string aliasTypeName, string aliasQualifiedName) {
    if (callOp.Callee != aliasQualifiedName && _typeAliasSources.ContainsKey(aliasTypeName)) {
      callOp.Callee = aliasQualifiedName;
      if (callOp.Result is MaxonStruct resultStruct)
        resultStruct.TypeName = aliasTypeName;
    }
  }

  /// Parses 'is' or 'is not' reference identity expression.
  private ExprResult.Direct ParseRefIdentity(ExprResult lhs) {
    var isToken = Advance(); // consume 'is'
    var negate = Check(TokenType.Not);
    if (negate) Advance(); // consume 'not'
    var rhsExpr = ParseExpression(4); // precedence above comparison level

    MaxonValue lhsVal = ResolveExprValue(lhs);
    MaxonValue rhsVal = ResolveExprValue(rhsExpr);

    // Only struct-typed values (references) support identity comparison
    if (lhsVal is not MaxonStruct lhsStruct || rhsVal is not MaxonStruct rhsStruct) {
      var opStr = negate ? "is not" : "is";
      throw new CompileError(ErrorCode.SemanticRefIdentityOnPrimitive,
        $"'{opStr}' requires reference types (structs), not primitive values",
        isToken.Line, isToken.Column);
    }
    var lhsBase = ResolveBaseTypeName(lhsStruct.TypeName);
    var rhsBase = ResolveBaseTypeName(rhsStruct.TypeName);
    if (lhsBase != rhsBase) {
      var opStr = negate ? "is not" : "is";
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"'{opStr}' requires both operands to be the same type, got '{lhsStruct.TypeName}' and '{rhsStruct.TypeName}'",
        isToken.Line, isToken.Column);
    }

    var refEqOp = new MaxonRefEqOp(lhsVal, rhsVal, negate);
    _currentBlock!.AddOp(refEqOp);
    return new ExprResult.Direct(refEqOp.Result);
  }

  /// Returns true if the given token can be used as a name (identifier or keyword used as name).
  private static bool IsIdentifierLikeToken(Token token) =>
    token.Type == TokenType.Identifier || Lexer.KeywordMap.ContainsKey(token.Value)
    || token.Type == TokenType.Mod;

  /// Returns true if the current token can be used as a name (identifier or keyword used as name).
  private bool CheckIdentifierLike() =>
    !IsAtEnd() && IsIdentifierLikeToken(Current());

  /// Returns true if the current position is a match block end (`end 'label'`),
  /// as opposed to a keyword used as a bare case name in an enum/union match.
  private bool IsMatchBlockEnd(MlirUnionType? enumType) {
    if (!Check(TokenType.End)) return false;
    // If there's no enum type, any `end` terminates the block
    if (enumType == null) return true;
    // If `end` is a valid case name for this enum, check what follows:
    // `end 'label'` = block end, `end then/gives/to/upto/or` = case pattern
    if (enumType.GetCase("end") == null) return true;
    var next = PeekNext();
    return next.Type == TokenType.CharacterLiteral;
  }

  /// Consumes and returns the current token if it can be used as a name; throws otherwise.
  private Token ExpectIdentifierLike() {
    if (!CheckIdentifierLike())
      throw new CompileError(ErrorCode.ParserExpectedToken, $"Expected identifier but got '{Current().Value}'", Current().Line, Current().Column);
    return Advance();
  }

  private bool IsAtEnd() => _pos >= _tokens.Count;

  [System.Text.RegularExpressions.GeneratedRegex(@"(?:^|/)specs/fragments[^/]*/")]
  private static partial System.Text.RegularExpressions.Regex SpecFragmentsRegex();
}
