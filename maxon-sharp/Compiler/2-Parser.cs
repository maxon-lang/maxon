using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public class Parser(List<Token> tokens, MlirModule<MaxonOp>? seedModule = null, bool isStdlib = false) {
  private readonly List<Token> _tokens = tokens;
  private readonly bool _isStdlib = isStdlib;
  private int _pos;

  private MlirModule<MaxonOp>? _currentModule;
  private MlirFunction<MaxonOp>? _currentFunction;
  private MlirBlock<MaxonOp>? _currentBlock;
  private readonly Dictionary<string, VarInfo> _variables = [];
  private readonly HashSet<string> _referencedVars = [];
  private int _blockCounter;
  private readonly Stack<LoopContext> _loopStack = new();
  private bool _inTryContext;
  private readonly Dictionary<string, MlirType> _typeRegistry = seedModule != null
    ? new(seedModule.TypeDefs) : [];
  private string? _currentTypeName;

  // Top-level compile-time constants (name -> evaluated value: long, double, or bool)
  private Dictionary<string, object> _topLevelConstants = [];

  // Top-level array let declarations deferred to main function body
  private record DeferredArrayLet(string Name, int TokenStart, int TokenEnd, int Line, int Column);
  private readonly List<DeferredArrayLet> _deferredArrayLets = [];

  // Global mutable variables (name -> type info)
  private record GlobalVarInfo(MaxonValueKind Kind, bool Mutable);
  private readonly Dictionary<string, GlobalVarInfo> _globalVars = [];

  // Default parameter values (funcName -> index -> value)
  private readonly Dictionary<string, Dictionary<int, MlirAttribute>> _functionDefaults = [];

  // Type alias source tracking (aliasName -> sourceTypeName)
  private readonly Dictionary<string, string> _typeAliasSources = [];

  // Element-polymorphic parameter tracking (funcName -> set of param indices that are Element type)
  // Also tracks return type: index -1 means return type is Element-polymorphic
  private readonly Dictionary<string, HashSet<int>> _elementPolymorphicParams = [];


  /// <summary>
  /// Resolves a qualified method name, falling back to the source type for type aliases.
  /// E.g., "ByteArray.push" → looks for "ByteArray.push" first, then "Array.push".
  /// Returns the resolved function name if found, null otherwise.
  /// </summary>
  private string? ResolveMethodName(string qualifiedName) {
    if (_currentModule!.Functions.Any(f => f.Name == qualifiedName))
      return qualifiedName;
    // Try alias fallback: ByteArray.push → Array.push
    var dotIdx = qualifiedName.IndexOf('.');
    if (dotIdx > 0) {
      var typePart = qualifiedName[..dotIdx];
      var methodPart = qualifiedName[(dotIdx + 1)..];
      if (_typeAliasSources.TryGetValue(typePart, out var sourceType)) {
        var aliasedName = $"{sourceType}.{methodPart}";
        if (_currentModule!.Functions.Any(f => f.Name == aliasedName))
          return aliasedName;
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

  // VarInfo now tracks struct type name for struct variables
  private record VarInfo(MaxonValueKind Kind, bool Mutable, MaxonValue Value, MlirBlock<MaxonOp> DefinedInBlock, string? StructTypeName = null);

  // Tracks parameter locations for unused parameter error reporting
  private readonly List<(string Name, int Line, int Column)> _paramLocations = [];

  public MlirModule<MaxonOp> Parse() {
    Logger.Debug(LogCategory.Parser, "Starting parser");
    var module = new MlirModule<MaxonOp>();
    _currentModule = module;

    // Register __ManagedMemory opaque struct type (buffer pointer, length, capacity)
    if (!_typeRegistry.ContainsKey("__ManagedMemory")) {
      _typeRegistry["__ManagedMemory"] = new MlirStructType("__ManagedMemory", [
        new MlirStructField("buffer", MlirType.I64, false, true),
        new MlirStructField("length", MlirType.I64, false, true),
        new MlirStructField("capacity", MlirType.I64, false, true),
      ]);
    }

    // Seed module with functions and defaults from previously parsed modules
    if (seedModule != null) {
      foreach (var func in seedModule.Functions) {
        if (!module.Functions.Any(f => f.Name == func.Name))
          module.AddFunction(func);
      }
      foreach (var (name, defaults) in seedModule.FunctionDefaults) {
        _functionDefaults.TryAdd(name, defaults);
      }
      foreach (var (name, params_) in seedModule.ElementPolymorphicParams) {
        _elementPolymorphicParams.TryAdd(name, params_);
      }
    }

    // Pre-scan to collect and evaluate top-level constants and global variables
    CollectAndEvaluateTopLevelDecls(module);

    SkipNewlines();
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      ParseTopLevel(module);
      SkipNewlines();
    }

    // Copy type registry and function defaults to module for downstream passes
    foreach (var (name, type) in _typeRegistry) {
      module.TypeDefs[name] = type;
    }
    foreach (var (name, defaults) in _functionDefaults) {
      module.FunctionDefaults.TryAdd(name, defaults);
    }
    foreach (var (name, params_) in _elementPolymorphicParams) {
      module.ElementPolymorphicParams.TryAdd(name, params_);
    }
    // Propagate type alias sources to module for monomorphization
    foreach (var (aliasName, sourceTypeName) in _typeAliasSources) {
      // Get TypeParams from the registered struct type if it exists
      Dictionary<string, MlirType>? typeParams = null;
      if (_typeRegistry.TryGetValue(aliasName, out var aliasType) && aliasType is MlirStructType st) {
        typeParams = st.TypeParams.Count > 0 ? new Dictionary<string, MlirType>(st.TypeParams) : null;
      }
      module.TypeAliasSources[aliasName] = new TypeAliasInfo(sourceTypeName, typeParams);
    }

    Logger.Debug(LogCategory.Parser, $"Parser complete: {module.Functions.Count} functions");
    return module;
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
        Advance();
        SkipToMatchingEnd();
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
      EvaluateConstant(decl.Name, constDecls, evaluated, evaluating);
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
    var funcName = owningType != null ? $"{owningType}.{nameToken.Value}" : nameToken.Value;

    Expect(TokenType.LeftParen);
    var (paramNames, paramTypes, paramDefaults, paramTokens) = ParseParamListWithDefaults();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    var throwsType = ParseThrowsClause();

    // Only register if not already known (avoid duplicates)
    if (!module.Functions.Any(f => f.Name == funcName)) {
      var func = new MlirFunction<MaxonOp>(funcName, paramNames, paramTypes, returnType, throwsType);
      module.AddFunction(func);

      if (paramDefaults.Count > 0) {
        _functionDefaults[funcName] = paramDefaults;
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
      _typeRegistry[name] = new MlirStructType(name, []);
    }
    return names;
  }

  /// <summary>
  /// Parse an 'is' clause (e.g., 'is Equatable') for interface conformance.
  /// Returns the list of interface names the type conforms to.
  /// </summary>
  private List<string> ParseConformanceClause() {
    var names = new List<string>();
    if (!Check(TokenType.Is)) return names;

    Advance(); // consume 'is'
    names.Add(Expect(TokenType.Identifier).Value);
    // Skip optional 'with TypeArg' suffix (e.g., Iterable with Element)
    if (Check(TokenType.With)) {
      Advance();
      ParseTypeRef();
    }
    while (Check(TokenType.Comma)) {
      Advance();
      names.Add(Expect(TokenType.Identifier).Value);
      if (Check(TokenType.With)) {
        Advance();
        ParseTypeRef();
      }
    }
    return names;
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

  private void PreScanType(MlirModule<MaxonOp> module) {
    Advance(); // consume 'type'
    var typeNameToken = Expect(TokenType.Identifier);
    var typeName = typeNameToken.Value;
    _currentTypeName = typeName;

    // Temporary entry so ParseTypeRef/PreScanInstanceMethod can resolve Self references
    if (!_typeRegistry.ContainsKey(typeName)) {
      _typeRegistry[typeName] = new MlirStructType(typeName, []);
    }

    var associatedTypeNames = ParseUsesClause();
    var conformingInterfaces = ParseConformanceClause();

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
    _typeRegistry[typeName] = new MlirStructType(typeName, fields, associatedTypeNames, conformingInterfaces);
    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);

    // Validate interface conformance: each required method must exist with matching signature
    foreach (var ifaceName in conformingInterfaces) {
      if (!_typeRegistry.TryGetValue(ifaceName, out var ifaceType) || ifaceType is not MlirInterfaceType iface)
        continue;

      var missingMethods = new List<string>();
      var wrongSignatureMethods = new List<string>();
      foreach (var method in iface.Methods) {
        var qualifiedName = $"{typeName}.{method.Name}";
        var func = module.Functions.FirstOrDefault(f => f.Name == qualifiedName);
        if (func == null) {
          missingMethods.Add(method.Format());
        } else if (!SignatureMatches(method, func, ifaceName, typeName)) {
          var actualSig = FormatFunctionSignature(method.Name, func);
          wrongSignatureMethods.Add($"{actualSig} (expected {method.Format()})");
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
  /// </summary>
  private static bool SignatureMatches(MlirInterfaceMethodSignature method, MlirFunction<MaxonOp> func, string ifaceName, string typeName) {
    // Instance methods have 'self' as first param; skip it
    var funcParamTypes = func.ParamTypes.Skip(1).ToList();

    if (method.ParamTypeNames.Count != funcParamTypes.Count)
      return false;

    for (int i = 0; i < method.ParamTypeNames.Count; i++) {
      var expectedTypeName = method.ParamTypeNames[i];
      // Self-typed parameters: interface stores the interface name, implementation uses the concrete type
      if (expectedTypeName == ifaceName)
        expectedTypeName = typeName;
      if (ResolveTypeName(expectedTypeName) != funcParamTypes[i].Name)
        return false;
    }

    var expectedReturnName = method.ReturnTypeName;
    if (expectedReturnName == ifaceName)
      expectedReturnName = typeName;
    var expectedReturn = expectedReturnName != null ? ResolveTypeName(expectedReturnName) : MlirType.Void.Name;
    var actualReturn = func.ReturnType?.Name ?? MlirType.Void.Name;
    if (expectedReturn != actualReturn)
      return false;

    return true;
  }

  /// <summary>
  /// Formats a function's actual signature in the same style as MlirInterfaceMethodSignature.Format(),
  /// using source-level type names for readability.
  /// </summary>
  private static string FormatFunctionSignature(string methodName, MlirFunction<MaxonOp> func) {
    // Skip 'self' parameter
    var paramNames = func.ParamNames.Skip(1).ToList();
    var paramTypes = func.ParamTypes.Skip(1).ToList();
    var paramsStr = string.Join(", ", paramNames.Zip(paramTypes, (n, t) => $"{n} {FormatTypeName(t)}"));
    var returnStr = func.ReturnType != null ? $" returns {FormatTypeName(func.ReturnType)}" : " returns void";
    return $"{methodName}({paramsStr}){returnStr}";
  }

  /// <summary>
  /// Maps an MlirType back to its source-level name for error messages.
  /// </summary>
  private static string FormatTypeName(MlirType type) {
    if (type == MlirType.I64) return "int";
    if (type == MlirType.F64) return "float";
    if (type == MlirType.I1) return "bool";
    if (type == MlirType.I8) return "byte";
    if (type == MlirType.Void) return "void";
    return type.Name;
  }

  private void PreScanEnum(MlirModule<MaxonOp> module) {
    Advance(); // consume 'enum'
    var nameToken = Expect(TokenType.Identifier);
    var enumName = nameToken.Value;
    _currentTypeName = enumName;

    var conformingInterfaces = ParseConformanceClause();

    // Temporary entry so ParseTypeRef can resolve forward references
    if (!_typeRegistry.TryGetValue(enumName, out MlirType? value)) {
      value = new MlirEnumType(enumName, [], null, conformingInterfaces);
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

      var caseToken = Expect(TokenType.Identifier);
      var caseName = caseToken.Value;

      if (!caseNames.Add(caseName)) {
        throw new CompileError(ErrorCode.SemanticEnumDuplicateCase,
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
            throw new CompileError(ErrorCode.SemanticEnumRawValueTypeMismatch,
              $"raw value type mismatch: 'expected {(backingType == MlirType.F64 ? "float" : "int")}, got int'",
              caseToken.Line, caseToken.Column);
          }

          // Check for duplicate raw values
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

          // Check for duplicate raw values
          foreach (var existing in cases) {
            if (existing.RawValue is double existingVal && existingVal == rawVal) {
              throw new CompileError(ErrorCode.SemanticEnumDuplicateRawValue,
                $"duplicate raw value: '{rawVal}'", caseToken.Line, caseToken.Column);
            }
          }

          cases.Add(new MlirEnumCase(caseName, ordinal, rawVal));
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

      // Skip static function declarations (factory methods, not instance requirements)
      if (Check(TokenType.Static)) {
        SkipToEndOfLine();
        SkipNewlines();
        continue;
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

      // Skip rest of line (handles throws clause and any other trailing tokens)
      SkipToEndOfLine();

      methods.Add(new MlirInterfaceMethodSignature(methodName, paramTypeNames, paramNames, returnTypeName));
      SkipNewlines();
    }

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();

    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    _typeRegistry[interfaceName] = new MlirInterfaceType(interfaceName, methods);
  }

  /// <summary>
  /// Pre-scan a typealias declaration: typealias Name is SourceType with (Type1, Type2, ...)
  /// Creates a concrete struct type by substituting associated type parameters.
  /// Also supports "with N Type" form where N is an integer capacity hint (e.g., Vector with 3 int).
  /// </summary>
  private void PreScanTypeAlias() {
    Advance(); // consume 'typealias'
    var aliasNameToken = Expect(TokenType.Identifier);
    var aliasName = aliasNameToken.Value;
    Expect(TokenType.Is);
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
    _typeAliasSources[aliasName] = sourceName;
  }

  private void PreScanInstanceMethod(MlirModule<MaxonOp> module, string typeName) {
    Advance(); // consume 'function'
    var nameToken = Expect(TokenType.Identifier);
    var methodName = $"{typeName}.{nameToken.Value}";

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

    // Track element-polymorphic parameters BEFORE replacing with I64
    var elementParams = new HashSet<int>();
    if (structType is MlirStructType st && st.AssociatedTypeNames.Count > 0) {
      // Check return type (index -1)
      if (returnType is MlirStructType retSt && st.AssociatedTypeNames.Contains(retSt.Name)) {
        elementParams.Add(-1);
        returnType = MlirType.I64;
      }
      // Check parameters (offset by 1 for 'self')
      for (int i = 0; i < paramTypes.Count; i++) {
        if (paramTypes[i] is MlirStructType paramSt && st.AssociatedTypeNames.Contains(paramSt.Name)) {
          elementParams.Add(i + 1); // +1 for 'self' at index 0
          paramTypes[i] = MlirType.I64;
        }
      }
      if (elementParams.Count > 0) {
        _elementPolymorphicParams[methodName] = elementParams;
      }
    }

    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { (MlirType)structType };
    allParamTypes.AddRange(paramTypes);

    // Skip methods with function-typed parameters (higher-order functions not yet supported)
    if (paramTypes.Any(t => t == MlirType.Fn)) {
      SkipToMatchingEnd();
      return;
    }

    // Only register if not already known
    if (!module.Functions.Any(f => f.Name == methodName)) {
      var func = new MlirFunction<MaxonOp>(methodName, allParamNames, allParamTypes, returnType, throwsType);
      module.AddFunction(func);

      if (paramDefaults.Count > 0) {
        var offsetDefaults = new Dictionary<int, MlirAttribute>();
        foreach (var (idx, attr) in paramDefaults) {
          offsetDefaults[idx + 1] = attr;
        }
        _functionDefaults[methodName] = offsetDefaults;
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
      if (Check(TokenType.Function) || Check(TokenType.If) || Check(TokenType.While) || Check(TokenType.For)) {
        depth++;
      } else if (Check(TokenType.End)) {
        depth--;
      }
      Advance();
    }
    // Skip end label if present
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  private object EvaluateConstant(string name, List<ConstantDecl> decls, Dictionary<string, object> evaluated, HashSet<string> evaluating) {
    if (evaluated.TryGetValue(name, out var val)) return val;

    var decl = decls.FirstOrDefault(d => d.Name == name) ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined constant '{name}'", 0, 0);
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
      return EvaluateConstant(token.Value, decls, evaluated, evaluating);
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
      Advance();
      SkipToMatchingEnd();
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
    return MaxonValueKind.Integer;
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
    var methodName = $"{typeName}.{nameToken.Value}";
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
    var methodName = $"{typeName}.{nameToken.Value}";
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

    // Skip methods with function-typed parameters (higher-order functions not yet supported)
    if (paramTypes.Any(t => t == MlirType.Fn)) {
      SkipToMatchingEnd();
      return;
    }

    // Instance method gets 'self' as first parameter (the struct type)
    var structType = (MlirStructType)_typeRegistry[typeName];

    // Track element-polymorphic parameters BEFORE replacing with I64
    var elementParams = new HashSet<int>();
    if (structType.AssociatedTypeNames.Count > 0) {
      // Check return type (index -1)
      if (returnType is MlirStructType retSt && structType.AssociatedTypeNames.Contains(retSt.Name)) {
        elementParams.Add(-1);
        returnType = MlirType.I64;
      }
      // Check parameters (offset by 1 for 'self')
      for (int i = 0; i < paramTypes.Count; i++) {
        if (paramTypes[i] is MlirStructType paramSt && structType.AssociatedTypeNames.Contains(paramSt.Name)) {
          elementParams.Add(i + 1); // +1 for 'self' at index 0
          paramTypes[i] = MlirType.I64;
        }
      }
      if (elementParams.Count > 0) {
        _elementPolymorphicParams[methodName] = elementParams;
      }
    }

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
    // Replace pre-registered stub with full function (or create new one)
    var existing = module.Functions.FirstOrDefault(f => f.Name == funcName);
    if (existing != null) {
      module.Functions.Remove(existing);
    }
    var func = new MlirFunction<MaxonOp>(funcName, paramNames, paramTypes, returnType, throwsType);
    module.AddFunction(func);

    // Store defaults
    if (paramDefaults.Count > 0) {
      _functionDefaults[funcName] = paramDefaults;
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
    var name = nameToken.Value;
    Logger.Debug(LogCategory.Parser, $"Parsing function: {name}");

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
    if (name == "main") {
      EmitDeferredArrayLets();
    }

    ParseBodyUntilEnd();
    ExpectEndLabel(name);
    FinishFunctionBody(name, nameToken, returnType);
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
      while (!Check(TokenType.RightParen) && !IsAtEnd()) {
        ParseTypeRef();
        if (Check(TokenType.Comma)) Advance();
      }
      Expect(TokenType.RightParen);
      if (Check(TokenType.Returns)) {
        Advance();
        ParseTypeRef();
      }
      return MlirType.Fn; // sentinel for function-typed parameters
    }

    var typeName = ExpectTypeName();
    return typeName switch {
      "int" => MlirType.I64,
      "float" => MlirType.F64,
      "bool" => MlirType.I1,
      "byte" => MlirType.I8,
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
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;
      ParseStatement();
      SkipNewlines();
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

      if (afterIdent.Type == TokenType.LeftParen) {
        // Check for static/qualified function call: Type.method(...)
        if (_currentModule!.Functions.Any(f => f.Name == qualifiedName)) {
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
      EmitFieldAssignment("self", selfInfo, fieldToken, selfToken);
    } else if (Check(TokenType.LeftParen)) {
      // self.method(...)
      var methodName = $"{selfInfo.StructTypeName}.{fieldToken.Value}";
      var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == methodName)
        ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined method '{fieldToken.Value}' on type '{selfInfo.StructTypeName}'", fieldToken.Line, fieldToken.Column);
      Advance(); // consume '('
      var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
      var explicitArgs = new List<MaxonValue>();
      if (!Check(TokenType.RightParen)) {
        explicitArgs.Add(ResolveExprValue(ParseExpression()));
        while (Check(TokenType.Comma)) {
          Advance();
          explicitArgs.Add(ResolveExprValue(ParseExpression()));
        }
      }
      Expect(TokenType.RightParen);
      var allArgs = new List<MaxonValue> { structVal };
      allArgs.AddRange(explicitArgs);
      var qualifiedToken = new Token(TokenType.Identifier, methodName, selfToken.Line, selfToken.Column);
      CreateFunctionCall(qualifiedToken, allArgs);
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

    // Skip type checking for associated type placeholders (e.g., Element in generic types)
    if (_currentTypeName != null && returnType is MlirStructType retSt
        && _typeRegistry.TryGetValue(_currentTypeName, out var currentTypeInfo)
        && currentTypeInfo is MlirStructType currentStruct
        && currentStruct.AssociatedTypeNames.Contains(retSt.Name)) {
      return;
    }

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
    if (valueKind != expectedKind) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Cannot return '{KindDisplayName(valueKind)}' from function declared to return '{KindDisplayName(expectedKind)}'",
        returnToken.Line, returnToken.Column);
    }
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
    _inTryContext = true;
    var callExpr = ParsePrimary();
    _inTryContext = false;

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
  /// Creates the continue block after a try/otherwise and loads the result from a variable.
  /// </summary>
  private ExprResult.Direct EmitTryContinueBlock(string continueBlockLabel, string? resultVar, MaxonTryCallOp tryCallOp) {
    var contBlock = _currentFunction!.Body.AddBlock(continueBlockLabel);
    _currentBlock = contBlock;

    if (resultVar != null) {
      var resultKind = tryCallOp.ResultKind ?? MaxonValueKind.Integer;
      var loadResultOp = new MaxonVarRefOp(resultVar, resultKind);
      _currentBlock!.AddOp(loadResultOp);
      return new ExprResult.Direct(loadResultOp.Result);
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

    // Store the default value to a temp variable so it can be used across blocks
    var defaultVarName = $"__try_default_{_blockCounter++}";
    _currentBlock!.AddOp(new MaxonAssignOp(defaultVarName, defaultValue, true, true, resultKind));
    _variables[defaultVarName] = new VarInfo(resultKind, true, defaultValue, _currentBlock!, null);

    // Store the call result (success path value)
    if (tryCallOp.Result != null) {
      _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, tryCallOp.Result, true, true, resultKind));
      _variables[resultVarName] = new VarInfo(resultKind, true, tryCallOp.Result, _currentBlock!, null);
    }

    var errorBlock = UniqueLabel("otherwise_default_error");
    var continueBlock = UniqueLabel("otherwise_default_continue");

    EmitErrorFlagCheck(tryCallOp.ErrorFlag, errorBlock, continueBlock);

    // Error block: load default from temp variable and overwrite result
    var errBlock = _currentFunction!.Body.AddBlock(errorBlock);
    _currentBlock = errBlock;
    var loadDefaultOp = new MaxonVarRefOp(defaultVarName, resultKind);
    _currentBlock!.AddOp(loadDefaultOp);
    _currentBlock!.AddOp(new MaxonAssignOp(resultVarName, loadDefaultOp.Result, false, true, resultKind));
    _currentBlock!.AddOp(new MaxonBrOp(continueBlock));

    // Continue block: load from result variable
    var contBlock = _currentFunction!.Body.AddBlock(continueBlock);
    _currentBlock = contBlock;

    var refOp = new MaxonVarRefOp(resultVarName, resultKind);
    _currentBlock!.AddOp(refOp);
    return new ExprResult.Direct(refOp.Result);
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
    } else {
      var kind = DetermineValueKind(initValue);
      _currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: isMutable, kind));
      _variables[name] = new VarInfo(kind, isMutable, initValue, _currentBlock!);
    }
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
    _currentBlock!.AddOp(new MaxonAssignOp(name, newVal, isDeclaration: false, isMutable: true, varInfo.Kind));
    _variables[name] = new VarInfo(varInfo.Kind, true, newVal, _currentBlock!, varInfo.StructTypeName);
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
    Expect(TokenType.Equals);

    if (!_variables.TryGetValue(name, out var varInfo)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{name}'", nameToken.Line, nameToken.Column);
    }

    if (!varInfo.Mutable) {
      throw new CompileError(ErrorCode.ParserImmutableVariable,
        $"cannot assign to immutable variable: '{name}'",
        nameToken.Line, nameToken.Column);
    }

    EmitFieldAssignment(name, varInfo, fieldToken, nameToken);
  }

  private void EmitFieldAssignment(string varName, VarInfo varInfo, Token fieldToken, Token errorToken) {
    var structType = (MlirStructType)_typeRegistry[varInfo.StructTypeName!];
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
    var structVal = ResolveExprValue(new ExprResult.VarRef(varName, varInfo));
    _currentBlock!.AddOp(new MaxonFieldAssignOp(structVal, varInfo.StructTypeName!, fieldToken.Value, newValue));
  }

  private void ParseCallStatement() {
    var token = Advance(); // consume identifier

    // Handle __managed_memory_* builtins
    if (token.Value.StartsWith("__managed_memory_") || token.Value == "__element_size") {
      Advance(); // consume '('
      TryEmitManagedMemoryBuiltin(token);
      return;
    }

    if (TrySiblingMethodCall(token) != null)
      return;

    Advance(); // consume '('
    var args = ParseCallArgs(token);
    CreateFunctionCall(token, args);
  }

  /// <summary>
  /// Handles __managed_memory_* and __element_size builtin intrinsics.
  /// Called after consuming '('. Returns the result value (or null for void builtins).
  /// </summary>
  private MaxonValue? TryEmitManagedMemoryBuiltin(Token token) {
    const int ELEMENT_SIZE = 8; // all values are 8 bytes in current implementation

    // Get the element type from the current struct's type parameters (e.g., Element for Array/Vector)
    MaxonValueKind GetElementKind() {
      if (_currentTypeName != null
          && _typeRegistry.TryGetValue(_currentTypeName, out var typeInfo)
          && typeInfo is MlirStructType structType
          && structType.TypeParams.TryGetValue("Element", out var elementType)) {
        return elementType == MlirType.F64 ? MaxonValueKind.Float : MaxonValueKind.Integer;
      }
      return MaxonValueKind.Integer;
    }

    switch (token.Value) {
      case "__element_size": {
        Expect(TokenType.RightParen);
        var lit = new MaxonLiteralOp((long)ELEMENT_SIZE);
        _currentBlock!.AddOp(lit);
        return lit.Result;
      }

      case "__managed_memory_len": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(managed, "__ManagedMemory", "length", MaxonValueKind.Integer);
        _currentBlock!.AddOp(op);
        return op.Result;
      }

      case "__managed_memory_capacity": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonFieldAccessOp(managed, "__ManagedMemory", "capacity", MaxonValueKind.Integer);
        _currentBlock!.AddOp(op);
        return op.Result;
      }

      case "__managed_memory_set_length": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var newLen = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonFieldAssignOp(managed, "__ManagedMemory", "length", newLen);
        _currentBlock!.AddOp(op);
        return null;
      }

      case "__managed_memory_get_unchecked": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGetOp(managed, index, ELEMENT_SIZE, GetElementKind());
        _currentBlock!.AddOp(op);
        return op.Result;
      }

      case "__managed_memory_set_at": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var value = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        // Determine element kind from the value being stored (for Element-polymorphic types)
        var elementKind = DetermineValueKind(value);
        var op = new MaxonManagedMemSetOp(managed, index, value, ELEMENT_SIZE, elementKind);
        _currentBlock!.AddOp(op);
        return null;
      }

      case "__managed_memory_create": {
        var count = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        // second arg is element size, skip it (we use compile-time constant)
        ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemCreateOp(count, ELEMENT_SIZE);
        _currentBlock!.AddOp(op);
        return op.Result;
      }

      case "__managed_memory_grow": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var newCap = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemGrowOp(managed, newCap, ELEMENT_SIZE);
        _currentBlock!.AddOp(op);
        return null;
      }

      case "__managed_memory_shift_right": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var count = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemShiftOp(managed, index, count, ELEMENT_SIZE, shiftRight: true);
        _currentBlock!.AddOp(op);
        return null;
      }

      case "__managed_memory_shift_left": {
        var managed = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var index = ResolveExprValue(ParseExpression());
        Expect(TokenType.Comma);
        var count = ResolveExprValue(ParseExpression());
        Expect(TokenType.RightParen);
        var op = new MaxonManagedMemShiftOp(managed, index, count, ELEMENT_SIZE, shiftRight: false);
        _currentBlock!.AddOp(op);
        return null;
      }

      default:
        throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unknown builtin '{token.Value}'", token.Line, token.Column);
    }
  }

  private MaxonCallOp? TrySiblingMethodCall(Token token) {
    if (_currentTypeName == null || !_variables.TryGetValue("self", out var selfInfo))
      return null;
    var siblingMethodName = $"{_currentTypeName}.{token.Value}";
    var resolvedSiblingName = ResolveMethodName(siblingMethodName);
    if (resolvedSiblingName == null)
      return null;
    Advance(); // consume '('
    var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
    var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedSiblingName, token.Line, token.Column);
    var siblingArgs = ParseInstanceMethodCallArgs(qualifiedFuncToken, structVal);
    return CreateFunctionCall(qualifiedFuncToken, siblingArgs);
  }

  private void ParseQualifiedCallStatement() {
    var typeToken = Advance(); // consume type name
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Advance(); // consume '('

    // Create a synthetic token for the qualified name, positioned at the method name
    var qualifiedName = $"{typeToken.Value}.{methodToken.Value}";
    var qualifiedToken = new Token(TokenType.Identifier, qualifiedName, methodToken.Line, methodToken.Column);

    var args = ParseCallArgs(qualifiedToken);
    CreateFunctionCall(qualifiedToken, args);
  }

  private void ParseInstanceMethodCallStatement(VarInfo varInfo, string methodName) {
    Advance(); // consume variable name
    Advance(); // consume '.'
    var methodToken = Advance(); // consume method name
    Advance(); // consume '('

    var structVal = varInfo.Value;
    var qualifiedToken = new Token(TokenType.Identifier, methodName, methodToken.Line, methodToken.Column);
    var args = ParseInstanceMethodCallArgs(qualifiedToken, structVal);
    CreateFunctionCall(qualifiedToken, args);
  }

  private void ParseIf() {
    Advance(); // consume 'if'

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

    // Parse: end 'thenLabel'
    ExpectEndLabel(thenSourceLabel);

    // Check for else or else-if
    MlirBlock<MaxonOp>? elseBlock = null;
    string? elseLabel = null;
    if (Check(TokenType.Else)) {
      Advance(); // consume 'else'
      if (Check(TokenType.If)) {
        // else-if chain: create a synthetic block and parse the nested if into it
        elseLabel = $"{thenLabel}.elseif";
        elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
        _currentBlock = elseBlock;
        ParseIf();
      } else {
        var elseSourceLabel = Expect(TokenType.CharacterLiteral).Value;
        elseLabel = UniqueLabel(elseSourceLabel);
        SkipNewlines();

        elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
        _currentBlock = elseBlock;
        ParseBodyUntilEnd();
        ExpectEndLabel(elseSourceLabel);
      }
    }

    // If either branch doesn't end with a return, create a merge block
    bool thenNeedsMerge = !BlockEndsWithTerminator(thenBlock);
    bool elseNeedsMerge = elseBlock != null && !BlockEndsWithTerminator(elseBlock);

    if (thenNeedsMerge || elseNeedsMerge) {
      var mergeLabel = $"{thenLabel}.merge";
      var mergeBlock = _currentFunction!.Body.AddBlock(mergeLabel);

      if (thenNeedsMerge)
        thenBlock.AddOp(new MaxonBrOp(mergeLabel));
      if (elseNeedsMerge)
        elseBlock!.AddOp(new MaxonBrOp(mergeLabel));

      // Emit cond_br: if true go to then-block, if false go to else or merge
      entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, elseLabel ?? mergeLabel));
      _currentBlock = mergeBlock;
    } else if (elseLabel != null) {
      // Both branches terminate — cond_br dispatches directly, no after block needed.
      entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, elseLabel));
      _currentBlock = null;
    } else {
      // If-without-else where the then-block terminates — need an after block
      // as the false target for the cond_br.
      var afterLabel = $"{thenLabel}.after";
      var afterBlock = _currentFunction!.Body.AddBlock(afterLabel);
      entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, afterLabel));
      _currentBlock = afterBlock;
    }
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
    if (!BlockEndsWithTerminator(_currentBlock!)) {
      _currentBlock!.AddOp(new MaxonBrOp(headerLabel));
    }

    // Create exit block - this is where execution continues after the loop
    var exitBlock = _currentFunction!.Body.AddBlock(exitLabel);
    _currentBlock = exitBlock;
  }

  /// <summary>
  /// Parse for-in loop: for varName in expr 'label' ... end 'label'
  /// Desugars to a while loop using an index variable and managed memory operations.
  /// </summary>
  private void ParseForIn() {
    var forToken = Advance(); // consume 'for'
    var itemName = Expect(TokenType.Identifier).Value;
    Expect(TokenType.In);
    var iterableExpr = ParseExpression();
    var iterableValue = ResolveExprValue(iterableExpr);
    var loopSourceLabel = Expect(TokenType.CharacterLiteral).Value;
    SkipNewlines();

    var loopLabel = UniqueLabel(loopSourceLabel);
    var headerLabel = $"{loopLabel}.header";
    var bodyLabel = loopLabel;
    var exitLabel = $"{loopLabel}.exit";

    // Get the iterable's type info to access its managed field
    var iterableTypeName = (iterableValue is MaxonStruct ms) ? ms.TypeName : _currentTypeName;
    var iterableType = iterableTypeName != null && _typeRegistry.TryGetValue(iterableTypeName, out var regType)
      ? regType as MlirStructType : null;

    // Create an index variable: var __for_idx = 0
    var idxVarName = $"__for_idx_{_blockCounter}";
    var zeroLit = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(zeroLit);
    var idxAssign = new MaxonAssignOp(idxVarName, zeroLit.Result, isDeclaration: true, isMutable: true, MaxonValueKind.Integer);
    _currentBlock!.AddOp(idxAssign);
    _variables[idxVarName] = new VarInfo(MaxonValueKind.Integer, true, zeroLit.Result, _currentBlock!, null);

    // Get the length of the iterable's managed memory
    // Access the managed field of the iterable struct
    MaxonValue managedValue;
    if (iterableType != null && iterableType.Fields.Any(f => f.Name == "managed")) {
      var managedAccess = new MaxonFieldAccessOp(iterableValue, iterableTypeName!, "managed", MaxonValueKind.Struct, "__ManagedMemory");
      _currentBlock!.AddOp(managedAccess);
      managedValue = managedAccess.Result;
    } else {
      managedValue = iterableValue;
    }

    // Get length: __managed_memory_len(managed)
    var lenAccess = new MaxonFieldAccessOp(managedValue, "__ManagedMemory", "length", MaxonValueKind.Integer);
    _currentBlock!.AddOp(lenAccess);
    var lenVarName = $"__for_len_{_blockCounter}";
    var lenAssign = new MaxonAssignOp(lenVarName, lenAccess.Result, isDeclaration: true, isMutable: false, MaxonValueKind.Integer);
    _currentBlock!.AddOp(lenAssign);
    _variables[lenVarName] = new VarInfo(MaxonValueKind.Integer, false, lenAccess.Result, _currentBlock!, null);

    // Branch from entry to header
    _currentBlock!.AddOp(new MaxonBrOp(headerLabel));

    // Create header block: check idx < len
    var headerBlock = _currentFunction!.Body.AddBlock(headerLabel);
    _currentBlock = headerBlock;

    // Load current index
    var idxLoad = new MaxonVarRefOp(idxVarName, MaxonValueKind.Integer);
    headerBlock.AddOp(idxLoad);
    // Load length
    var lenLoad = new MaxonVarRefOp(lenVarName, MaxonValueKind.Integer);
    headerBlock.AddOp(lenLoad);
    // Compare: idx < len
    var cmpOp = new MaxonBinOp(MaxonBinOperator.Lt, idxLoad.Result, lenLoad.Result, MaxonValueKind.Integer);
    headerBlock.AddOp(cmpOp);
    headerBlock.AddOp(new MaxonCondBrOp(cmpOp.Result, bodyLabel, exitLabel));

    // Create body block
    var bodyBlock = _currentFunction!.Body.AddBlock(bodyLabel);
    _currentBlock = bodyBlock;
    _loopStack.Push(new LoopContext(loopSourceLabel, headerLabel, exitLabel));

    // Load current index again in body
    var bodyIdxLoad = new MaxonVarRefOp(idxVarName, MaxonValueKind.Integer);
    bodyBlock.AddOp(bodyIdxLoad);

    // Get element: __managed_memory_get_unchecked(managed, idx)
    // Re-access managed field in body block
    MaxonValue bodyManagedValue;
    if (iterableType != null && iterableType.Fields.Any(f => f.Name == "managed")) {
      var bodyManagedAccess = new MaxonFieldAccessOp(iterableValue, iterableTypeName!, "managed", MaxonValueKind.Struct, "__ManagedMemory");
      bodyBlock.AddOp(bodyManagedAccess);
      bodyManagedValue = bodyManagedAccess.Result;
    } else {
      bodyManagedValue = managedValue;
    }

    var getOp = new MaxonManagedMemGetOp(bodyManagedValue, bodyIdxLoad.Result, 8, MaxonValueKind.Integer);
    bodyBlock.AddOp(getOp);

    // Declare the loop variable
    var itemAssign = new MaxonAssignOp(itemName, getOp.Result, isDeclaration: true, isMutable: false, MaxonValueKind.Integer);
    bodyBlock.AddOp(itemAssign);
    _variables[itemName] = new VarInfo(MaxonValueKind.Integer, false, getOp.Result, bodyBlock, null);

    // Increment index: idx = idx + 1
    var idxLoad2 = new MaxonVarRefOp(idxVarName, MaxonValueKind.Integer);
    bodyBlock.AddOp(idxLoad2);
    var oneLit = new MaxonLiteralOp(1L);
    bodyBlock.AddOp(oneLit);
    var addOp = new MaxonBinOp(MaxonBinOperator.Add, idxLoad2.Result, oneLit.Result, MaxonValueKind.Integer);
    bodyBlock.AddOp(addOp);
    var idxUpdate = new MaxonAssignOp(idxVarName, addOp.Result, isDeclaration: false, isMutable: true, MaxonValueKind.Integer);
    bodyBlock.AddOp(idxUpdate);

    // Parse the body
    ParseBodyUntilEnd();
    _loopStack.Pop();
    ExpectEndLabel(loopSourceLabel);

    // Branch back to header
    if (!BlockEndsWithTerminator(_currentBlock!)) {
      _currentBlock!.AddOp(new MaxonBrOp(headerLabel));
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

  private record MatchPattern(long RawValue, string DisplayName, int Line, int Column);

  /// <summary>
  /// Parses match case patterns (integer literals or enum member access like Color.red).
  /// Multiple patterns can be separated by 'or'. Returns the list of parsed patterns.
  /// Also tracks seen patterns for duplicate detection and enum cases for exhaustiveness.
  /// </summary>
  private List<MatchPattern> ParseMatchPatterns(
      string? enumTypeName, MlirEnumType? enumType,
      HashSet<long> seenPatterns, HashSet<string> seenEnumCases) {
    var patterns = new List<MatchPattern>();
    while (true) {
      var patternLine = Current().Line;
      var patternCol = Current().Column;

      if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Dot) {
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

        long rawValue;
        if (enumType.BackingType == MlirType.F64) {
          rawValue = BitConverter.DoubleToInt64Bits((double)enumCase.RawValue!);
        } else if (enumType.BackingType == MlirType.I64) {
          rawValue = (long)enumCase.RawValue!;
        } else {
          rawValue = enumCase.Ordinal;
        }

        var displayName = $"{patternEnumName}.{caseNameToken.Value}";
        if (!seenPatterns.Add(rawValue)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{displayName}'",
            patternLine, patternCol);
        }
        seenEnumCases.Add(caseNameToken.Value);
        patterns.Add(new MatchPattern(rawValue, displayName, patternLine, patternCol));
      } else if (Check(TokenType.IntegerLiteral)) {
        var litToken = Advance();
        var litValue = ParseIntegerLiteral(litToken);
        if (!seenPatterns.Add(litValue)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{litValue}'",
            patternLine, patternCol);
        }
        patterns.Add(new MatchPattern(litValue, litValue.ToString(), patternLine, patternCol));
      } else if (Check(TokenType.Minus) && _pos + 1 < _tokens.Count && _tokens[_pos + 1].Type == TokenType.IntegerLiteral) {
        Advance(); // consume '-'
        var litToken = Advance();
        var litValue = -ParseIntegerLiteral(litToken);
        if (!seenPatterns.Add(litValue)) {
          throw new CompileError(ErrorCode.ParserMatchDuplicatePattern,
            $"duplicate pattern in match: '{litValue}'",
            patternLine, patternCol);
        }
        patterns.Add(new MatchPattern(litValue, litValue.ToString(), patternLine, patternCol));
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

  /// <summary>
  /// Emits comparison ops for a set of patterns against the scrutinee value.
  /// Multiple patterns are combined with logical OR.
  /// Returns the combined boolean comparison result.
  /// </summary>
  private MaxonValue EmitPatternComparison(
      List<MatchPattern> patterns, string scrutTempName,
      MaxonValueKind compareKind) {
    var refOp = new MaxonVarRefOp(scrutTempName, compareKind);
    _currentBlock!.AddOp(refOp);
    var localCompareVal = refOp.Result;

    MaxonValue? combinedCmp = null;
    foreach (var pattern in patterns) {
      MaxonLiteralOp patLit;
      if (compareKind == MaxonValueKind.Float) {
        patLit = new MaxonLiteralOp(BitConverter.Int64BitsToDouble(pattern.RawValue));
      } else {
        patLit = new MaxonLiteralOp(pattern.RawValue);
      }
      _currentBlock!.AddOp(patLit);

      var cmpOp = new MaxonBinOp(MaxonBinOperator.Eq, localCompareVal, patLit.Result, compareKind);
      _currentBlock!.AddOp(cmpOp);

      if (combinedCmp == null) {
        combinedCmp = cmpOp.Result;
      } else {
        var orOp = new MaxonBinOp(MaxonBinOperator.Or, combinedCmp, cmpOp.Result, MaxonValueKind.Bool);
        _currentBlock!.AddOp(orOp);
        combinedCmp = orOp.Result;
      }
    }
    return combinedCmp!;
  }

  /// <summary>
  /// Resolves scrutinee to a comparable value. For enums, extracts the raw backing value.
  /// Returns the compare value, its kind, and enum metadata if applicable.
  /// </summary>
  private (MaxonValue CompareVal, MaxonValueKind CompareKind, string? EnumTypeName, MlirEnumType? EnumType)
      ResolveScrutinee(MaxonValue scrutineeVal) {
    if (scrutineeVal is MaxonEnum enumVal) {
      var enumTypeName = enumVal.TypeName;
      var enumType = (MlirEnumType)_typeRegistry[enumTypeName];
      var backingKind = GetEnumBackingKind(enumType);
      var rawOp = new MaxonEnumRawValueOp(scrutineeVal, enumTypeName, backingKind);
      _currentBlock!.AddOp(rawOp);
      return (rawOp.Result, backingKind, enumTypeName, enumType);
    }
    return (scrutineeVal, DetermineValueKind(scrutineeVal), null, null);
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
    var (compareVal, compareKind, enumTypeName, enumType) = ResolveScrutinee(scrutineeVal);

    var entryBlock = _currentBlock!;

    // Store scrutinee in a temp var so comparison blocks can reference it across blocks
    var scrutTempName = $"__match_{matchLabel}";
    entryBlock.AddOp(new MaxonAssignOp(scrutTempName, compareVal, isDeclaration: true, isMutable: false, compareKind));
    _variables[scrutTempName] = new VarInfo(compareKind, false, compareVal, entryBlock);

    var mergeLabel = $"{matchLabel}.merge";
    var caseBlocks = new List<MlirBlock<MaxonOp>>();
    var caseIsDefault = new List<bool>();
    var caseFallthrough = new List<bool>();
    // One entry per case: non-default cases have their comparison block, default has null
    var cmpBlocks = new List<MlirBlock<MaxonOp>?>();
    var seenPatterns = new HashSet<long>();
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
        patterns = ParseMatchPatterns(enumTypeName, enumType, seenPatterns, seenEnumCases);
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
        var combinedCmp = EmitPatternComparison(patterns, scrutTempName, compareKind);
        cmpBlock.AddOp(new MaxonCondBrOp(combinedCmp, caseBodyLabel, ""));
        cmpBlocks.Add(cmpBlock);
      } else {
        cmpBlocks.Add(null);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
      }

      _currentBlock = caseBodyBlock;
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
    var (compareVal, compareKind, enumTypeName, enumType) = ResolveScrutinee(scrutineeVal);

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
    _variables[scrutTempName] = new VarInfo(compareKind, false, compareVal, entryBlock);

    var mergeLabel = $"{matchLabel}.merge";
    var caseBlocks = new List<MlirBlock<MaxonOp>>();
    var caseIsDefault = new List<bool>();
    var cmpBlocks = new List<MlirBlock<MaxonOp>?>();
    var seenPatterns = new HashSet<long>();
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
        patterns = ParseMatchPatterns(enumTypeName, enumType, seenPatterns, seenEnumCases);
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
        var combinedCmp = EmitPatternComparison(patterns, scrutTempName, compareKind);
        cmpBlock.AddOp(new MaxonCondBrOp(combinedCmp, caseBodyLabel, ""));
        cmpBlocks.Add(cmpBlock);
      } else {
        cmpBlocks.Add(null);
        caseBodyBlock = _currentFunction!.Body.AddBlock(caseBodyLabel);
      }

      _currentBlock = caseBodyBlock;
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
          var enumTypeName = lhsVal is MaxonEnum le ? le.TypeName : ((MaxonEnum)rhsVal).TypeName;
          var enumType = (MlirEnumType)_typeRegistry[enumTypeName];
          kind = GetEnumBackingKind(enumType);
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
            MaxonEnumLiteralOp enumLitOp;
            if (enumType.BackingType == MlirType.F64) {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (double)enumCase.RawValue!);
            } else if (enumType.BackingType == MlirType.I64) {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (long)enumCase.RawValue!);
            } else {
              enumLitOp = new MaxonEnumLiteralOp(token.Value, memberName, (long)enumCase.Ordinal);
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
        }

        // Check for qualified function call: TypeName.method(...)
        // _pos is at '.', _pos+1 is 'method', _pos+2 should be '('
        if (_pos + 2 < _tokens.Count && _tokens[_pos + 2].Type == TokenType.LeftParen) {
          if (_currentModule!.Functions.Any(f => f.Name == qualifiedName)) {
            Advance(); // consume '.'
            var methodToken = Advance(); // consume method name
            Advance(); // consume '('
            var qualifiedFuncToken = new Token(TokenType.Identifier, qualifiedName, methodToken.Line, methodToken.Column);
            var args = ParseCallArgs(qualifiedFuncToken);
            var callOp = CreateFunctionCall(qualifiedFuncToken, args);
            if (callOp.Result != null)
              return new ExprResult.Direct(callOp.Result);
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
        // Handle __managed_memory_* and __element_size builtins in expression context
        if (token.Value.StartsWith("__managed_memory_") || token.Value == "__element_size") {
          Advance(); // consume '('
          var builtinResult = TryEmitManagedMemoryBuiltin(token);
          if (builtinResult != null)
            return new ExprResult.Direct(builtinResult);
          throw new CompileError(ErrorCode.ParserExpectedExpression, $"Builtin '{token.Value}' does not return a value", token.Line, token.Column);
        }
        // Check for sibling method call: bare methodName() inside an instance method resolves to self.methodName()
        var siblingCallOp = TrySiblingMethodCall(token);
        if (siblingCallOp != null) {
          if (siblingCallOp.Result != null)
            return new ExprResult.Direct(siblingCallOp.Result);
          throw new CompileError(ErrorCode.ParserExpectedExpression, $"Method '{token.Value}' does not return a value", token.Line, token.Column);
        }

        Advance(); // consume '('
        var args = ParseCallArgs(token);
        var callOp = CreateFunctionCall(token, args);
        if (callOp.Result != null)
          return new ExprResult.Direct(callOp.Result);
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
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{token.Value}'", token.Line, token.Column);
    }

    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got '{Current().Value}'", Current().Line, Current().Column);
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
        // .rawValue access
        if (fieldName == "rawValue") {
          var enumVal = ResolveExprValue(result);
          var resultKind = GetEnumBackingKind(enumType);
          var rawValueOp = new MaxonEnumRawValueOp(enumVal, userTypeName, resultKind);
          _currentBlock!.AddOp(rawValueOp);
          result = new ExprResult.Direct(rawValueOp.Result);
          continue;
        }

        // Method call on enum
        if (Check(TokenType.LeftParen)) {
          var qualifiedMethodName = $"{userTypeName}.{fieldName}";
          var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == qualifiedMethodName);
          if (callee != null) {
            Advance(); // consume '('
            var enumVal = ResolveExprValue(result);
            var qualifiedFuncToken = new Token(TokenType.Identifier, qualifiedMethodName, fieldToken.Line, fieldToken.Column);
            var allArgs = ParseInstanceMethodCallArgs(qualifiedFuncToken, enumVal);
            var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs);
            if (callOp.Result != null)
              return new ExprResult.Direct(callOp.Result);
            return new ExprResult.Direct(enumVal);
          }
        }

        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Enum type '{userTypeName}' has no property or method named '{fieldName}'", fieldToken.Line, fieldToken.Column);
      }

      var structType = (MlirStructType)registeredType;

      // Check for method call: expr.method(...)
      if (Check(TokenType.LeftParen)) {
        var qualifiedMethodName = $"{userTypeName}.{fieldName}";
        var resolvedMethodName = ResolveMethodName(qualifiedMethodName);
        if (resolvedMethodName != null) {
          Advance(); // consume '('
          var structVal = ResolveExprValue(result);
          var qualifiedFuncToken = new Token(TokenType.Identifier, resolvedMethodName, fieldToken.Line, fieldToken.Column);
          var allArgs = ParseInstanceMethodCallArgs(qualifiedFuncToken, structVal);
          var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs);
          if (callOp.Result != null)
            return new ExprResult.Direct(callOp.Result);
          return new ExprResult.Direct(structVal);
        }
      }

      var field = structType.GetField(fieldName)
        ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type '{userTypeName}' has no field named '{fieldName}'", fieldToken.Line, fieldToken.Column);

      if (!field.IsExported && _currentTypeName != userTypeName) {
        throw new CompileError(ErrorCode.SemanticUnexportedFieldAccess, $"cannot access unexported field: '{fieldName}' outside of type '{userTypeName}'", fieldToken.Line, fieldToken.Column);
      }

      var fieldKind = field.Type.ToValueKind();
      var fieldStructName = field.Type is MlirStructType fst ? fst.Name : null;
      var structVal2 = ResolveExprValue(result);
      var accessOp = new MaxonFieldAccessOp(structVal2, userTypeName, fieldName, fieldKind, fieldStructName);
      _currentBlock!.AddOp(accessOp);
      result = new ExprResult.Direct(accessOp.Result);
    }

    return result;
  }

  private ExprResult.Direct EmitConstantLiteral(object constValue) {
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
    var (managedStruct, arrayTag, elementCount) = EmitArrayLiteralElements();

    // Create Array struct: {iterIndex: 0, managed: <managed>}
    var iterIndexLit = new MaxonLiteralOp(0L);
    _currentBlock!.AddOp(iterIndexLit);

    var arrayFields = new List<(string Name, MaxonValue Value)> {
      ("iterIndex", iterIndexLit.Result),
      ("managed", managedStruct.Result)
    };
    var arrayStruct = new MaxonStructLiteralOp("Array", arrayFields);
    _currentBlock!.AddOp(arrayStruct);

    // Record the element variable names for lowering to resolve the buffer
    arrayStruct.ArrayLiteralTag = arrayTag;
    arrayStruct.ArrayLiteralCount = elementCount;

    return new ExprResult.Direct(arrayStruct.Result);
  }

  /// <summary>
  /// Parses [expr, expr, ...] and emits element assigns + __ManagedMemory struct.
  /// Returns the managed memory struct op and the element count.
  /// </summary>
  private (MaxonStructLiteralOp ManagedStruct, string ArrayTag, int ElementCount) EmitArrayLiteralElements() {
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

    // Determine element type from first element (all elements must have same type)
    var elementKind = count > 0 ? GetValueKind(elements[0]) : MaxonValueKind.Integer;

    // Store elements in reverse order so element 0 ends up at the lowest stack address.
    // Stack grows downward: first stored → highest address, last stored → lowest address.
    // LEA finds the lowest address, so element 0 must be stored last.
    var arrayTag = $"__arr_{_blockCounter++}";
    for (int i = count - 1; i >= 0; i--) {
      var elemVarName = $"{arrayTag}.{i}";
      var assignOp = new MaxonAssignOp(elemVarName, elements[i], isDeclaration: true, isMutable: false, elementKind);
      _currentBlock!.AddOp(assignOp);
    }

    // Create __ManagedMemory struct: {buffer: <tag_id>, length: count, capacity: 0}
    // buffer is represented as the array tag identifier (resolved during lowering)
    var bufLit = new MaxonLiteralOp(0L); // buffer pointer placeholder (0 = will be set up during lowering)
    _currentBlock!.AddOp(bufLit);
    var lenLit = new MaxonLiteralOp((long)count);
    _currentBlock!.AddOp(lenLit);
    var capLit = new MaxonLiteralOp(0L); // capacity = 0 means stack-allocated
    _currentBlock!.AddOp(capLit);

    var managedFields = new List<(string Name, MaxonValue Value)> {
      ("buffer", bufLit.Result),
      ("length", lenLit.Result),
      ("capacity", capLit.Result)
    };
    var managedStruct = new MaxonStructLiteralOp("__ManagedMemory", managedFields);
    _currentBlock!.AddOp(managedStruct);

    return (managedStruct, arrayTag, count);
  }

  private ExprResult.Direct ParseStructLiteral(string typeName) {
    Advance(); // consume '{'
    var structType = (MlirStructType)_typeRegistry[typeName];
    var fieldValues = new List<(string, MaxonValue)>();
    var providedFields = new HashSet<string>();

    // Track array literal tag if this is a Vector-like type with __capacity
    string? arrayLiteralTag = null;
    int arrayLiteralCount = 0;

    if (!Check(TokenType.RightBrace)) {
      do {
        var fieldNameToken = Expect(TokenType.Identifier);
        Expect(TokenType.Colon);
        var value = ResolveExprValue(ParseExpression());
        fieldValues.Add((fieldNameToken.Value, value));
        providedFields.Add(fieldNameToken.Value);
      } while (Check(TokenType.Comma) && Advance() != null);
    }

    // Fill in defaults for unspecified fields
    foreach (var field in structType.Fields) {
      if (!providedFields.Contains(field.Name)) {
        if (field.DefaultValue == null) {
          // Special case: Vector-like type with __capacity and managed __ManagedMemory field
          if (field.Name == "managed" && field.Type.Name == "__ManagedMemory" &&
              structType.ConstParams.TryGetValue("__capacity", out var capacity)) {
            // Create N zero-valued elements on the stack (in reverse order for proper memory layout)
            arrayLiteralTag = $"__arr_{_blockCounter++}";
            arrayLiteralCount = (int)capacity;
            for (int i = arrayLiteralCount - 1; i >= 0; i--) {
              var zeroVal = new MaxonLiteralOp(0L);
              _currentBlock!.AddOp(zeroVal);
              var elemVarName = $"{arrayLiteralTag}.{i}";
              var assignOp = new MaxonAssignOp(elemVarName, zeroVal.Result, isDeclaration: true, isMutable: false, MaxonValueKind.Integer);
              _currentBlock!.AddOp(assignOp);
            }

            // Create __ManagedMemory struct with stack-allocated buffer
            var bufLit = new MaxonLiteralOp(0L); // buffer pointer placeholder
            _currentBlock!.AddOp(bufLit);
            var lenLit = new MaxonLiteralOp(capacity);
            _currentBlock!.AddOp(lenLit);
            var capLit = new MaxonLiteralOp(0L); // capacity = 0 means stack-allocated
            _currentBlock!.AddOp(capLit);

            var managedFields = new List<(string Name, MaxonValue Value)> {
              ("buffer", bufLit.Result),
              ("length", lenLit.Result),
              ("capacity", capLit.Result)
            };
            var managedStruct = new MaxonStructLiteralOp("__ManagedMemory", managedFields);
            _currentBlock!.AddOp(managedStruct);
            fieldValues.Add((field.Name, managedStruct.Result));
          } else if (field.Type is MlirStructType fieldStructType) {
            // For struct-typed fields, emit a zero-initialized struct literal
            var zeroFields = new List<(string Name, MaxonValue Value)>();
            foreach (var subField in fieldStructType.Fields) {
              var zeroLit = new MaxonLiteralOp(0L);
              _currentBlock!.AddOp(zeroLit);
              zeroFields.Add((subField.Name, zeroLit.Result));
            }
            var zeroStruct = new MaxonStructLiteralOp(fieldStructType.Name, zeroFields);
            _currentBlock!.AddOp(zeroStruct);
            fieldValues.Add((field.Name, zeroStruct.Result));
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

  /// <summary>
  /// Parse "TypeName from [...]" syntax.
  /// Checks BuiltinArrayLiteral first (passes __ManagedMemory directly, stdlib types).
  /// Falls back to InitableFromArrayLiteral (passes Array wrapper, user types).
  /// For types with associated types and __capacity, auto-creates a concrete alias type.
  /// </summary>
  private ExprResult.Direct ParseFromExpression(Token typeToken) {
    Advance(); // consume 'from'
    var typeName = typeToken.Value;

    // Expect array literal
    if (!Check(TokenType.LeftBracket)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression,
        "Expected array literal after 'from'", Current().Line, Current().Column);
    }

    // Look up the type
    if (!_typeRegistry.TryGetValue(typeName, out var registeredType))
      throw new CompileError(ErrorCode.ParserExpectedType,
        $"Unknown type '{typeName}'", typeToken.Line, typeToken.Column);
    if (registeredType is not MlirStructType sourceStruct)
      throw new CompileError(ErrorCode.ParserExpectedType,
        $"Type '{typeName}' is not a struct type", typeToken.Line, typeToken.Column);

    bool isBuiltinArrayLiteral = sourceStruct.ConformingInterfaces.Contains("BuiltinArrayLiteral");
    bool isInitableFromArrayLiteral = sourceStruct.ConformingInterfaces.Contains("InitableFromArrayLiteral");

    if (!isBuiltinArrayLiteral && !isInitableFromArrayLiteral)
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Type '{typeName}' does not conform to BuiltinArrayLiteral or InitableFromArrayLiteral",
        typeToken.Line, typeToken.Column);

    // Parse elements — both paths start the same way
    var (managedStruct, arrayTag, elementCount) = EmitArrayLiteralElements();

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

    var initFunc = _currentModule!.Functions.FirstOrDefault(f => f.Name == initMethodName) ?? throw new CompileError(ErrorCode.SemanticUndefinedFunction,
        $"Type '{typeName}' does not have a valid init method (no '{initMethodName}' found)",
        typeToken.Line, typeToken.Column);

    // Determine return type - init returns Self which is the struct type
    MaxonValueKind? resultKind = MaxonValueKind.Struct;
    string resultStructTypeName = concreteTypeName;

    // Create the call to init — pass either __ManagedMemory or Array depending on interface
    var callOp = new MaxonCallOp(initMethodName, [initArg], resultKind, resultStructTypeName);
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
        if (v.Info.DefinedInBlock == _currentBlock) {
          return v.Info.Value;
        }
        // Cross-block reference: emit a var_ref, struct_var_ref, or enum_var_ref op
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
  /// Parses call arguments. The first argument can be positional or named.
  /// Second and subsequent arguments MUST be named (name: value syntax),
  /// unless a function has 2+ params and a second positional arg is given (error).
  /// Handles default parameter values for omitted arguments.
  /// </summary>
  private List<MaxonValue> ParseCallArgs(Token functionNameToken) {
    if (Check(TokenType.RightParen)) {
      Advance();
      var callee0 = _currentModule!.Functions.FirstOrDefault(f => f.Name == functionNameToken.Value);
      if (callee0 != null && callee0.ParamTypes.Count > 0) {
        return FillDefaultArgs(functionNameToken, callee0, new MaxonValue?[callee0.ParamTypes.Count]);
      }
      return [];
    }

    var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == functionNameToken.Value)
      ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined function '{functionNameToken.Value}'", functionNameToken.Line, functionNameToken.Column);

    var args = new MaxonValue?[callee.ParamTypes.Count];
    ParseArgList(functionNameToken, callee, args, firstPositionalIndex: 0);
    Expect(TokenType.RightParen);

    return FillDefaultArgs(functionNameToken, callee, args);
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

    // Check if we're calling an Element-polymorphic method and get caller's Element type
    MlirType? callerElementType = null;
    if (args.Length > 0 && args[0] is MaxonStruct selfStruct
        && _typeRegistry.TryGetValue(selfStruct.TypeName, out var selfType)
        && selfType is MlirStructType selfStructType
        && selfStructType.TypeParams.TryGetValue("Element", out var elemType)) {
      callerElementType = elemType;
      Logger.Debug(LogCategory.Parser, $"FillDefaultArgs: {callee.Name} - callerElementType={callerElementType}");
    }

    // Get Element-polymorphic param indices for this function (or its source via alias)
    if (_elementPolymorphicParams.TryGetValue(callee.Name, out HashSet<int>? elementParams)) {
      Logger.Debug(LogCategory.Parser, $"FillDefaultArgs: {callee.Name} - found elementParams directly: [{string.Join(",", elementParams)}]");
    } else {
      // Check if callee is from an aliased type (Vec2F.set → Vector.set)
      var dotIdx = callee.Name.IndexOf('.');
      if (dotIdx > 0) {
        var typePart = callee.Name[..dotIdx];
        var methodPart = callee.Name[(dotIdx + 1)..];
        if (_typeAliasSources.TryGetValue(typePart, out var sourceType)) {
          var sourceMethodName = $"{sourceType}.{methodPart}";
          if (_elementPolymorphicParams.TryGetValue(sourceMethodName, out elementParams)) {
            Logger.Debug(LogCategory.Parser, $"FillDefaultArgs: {callee.Name} - found elementParams via alias {sourceMethodName}: [{string.Join(",", elementParams)}]");
          }
        }
      }
    }

    // Implicit type conversion for function arguments
    for (int i = 0; i < args.Length; i++) {
      var argKind = DetermineValueKind(args[i]!);
      var paramType = callee.ParamTypes[i];
      if (paramType is MlirStructType) continue;
      if (paramType is MlirEnumType) continue;

      var paramKind = MlirTypeToValueKind(paramType);
      if (argKind == paramKind) continue;

      // Skip float<->int conversion for Element-polymorphic parameters when caller has float Element type
      if (elementParams != null && elementParams.Contains(i)
          && callerElementType == MlirType.F64
          && ((argKind == MaxonValueKind.Float && paramKind == MaxonValueKind.Integer)
           || (argKind == MaxonValueKind.Integer && paramKind == MaxonValueKind.Float))) {
        continue;
      }

      args[i] = ConvertArgToParamType(args[i]!, argKind, paramKind, callee.ParamNames[i], functionNameToken);
    }

    return args.ToList()!;
  }

  private static MaxonValueKind MlirTypeToValueKind(MlirType type) {
    if (type == MlirType.I64) return MaxonValueKind.Integer;
    if (type == MlirType.F64) return MaxonValueKind.Float;
    if (type == MlirType.I1) return MaxonValueKind.Bool;
    if (type == MlirType.I8) return MaxonValueKind.Byte;
    if (type is MlirEnumType) return MaxonValueKind.Enum;
    throw new InvalidOperationException($"Cannot map MlirType '{type}' to MaxonValueKind");
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
  private MaxonValue ParseCallArgValue(MlirType expectedType) {
    if (Check(TokenType.LeftBrace) && expectedType is MlirStructType structType) {
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
  private void ParseArgList(Token callToken, MlirFunction<MaxonOp> callee, MaxonValue?[] args, int firstPositionalIndex) {
    if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
      ParseNamedArg(callee, args);
    } else {
      args[firstPositionalIndex] = ParseCallArgValue(callee.ParamTypes[firstPositionalIndex]);
    }

    while (Check(TokenType.Comma) && Advance() != null) {
      if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
        ParseNamedArg(callee, args);
      } else {
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Second and subsequent arguments must be named. Use 'name: value' syntax",
          callToken.Line, callToken.Column);
      }
    }
  }

  private void ParseNamedArg(MlirFunction<MaxonOp> callee, MaxonValue?[] args) {
    var nameToken = Advance();
    Advance(); // consume ':'
    var idx = callee.ParamNames.IndexOf(nameToken.Value);
    if (idx < 0)
      throw new CompileError(ErrorCode.SemanticUndefinedVariable, $"unknown parameter name: '{nameToken.Value}'", nameToken.Line, nameToken.Column);
    args[idx] = ParseCallArgValue(callee.ParamTypes[idx]);
  }

  /// <summary>
  /// Parses call arguments for an instance method call, pre-filling the self argument at index 0.
  /// The first explicit argument is positional (maps to index 1), subsequent args must be named.
  /// </summary>
  private List<MaxonValue> ParseInstanceMethodCallArgs(Token methodNameToken, MaxonValue selfValue) {
    var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == methodNameToken.Value)
      ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined function '{methodNameToken.Value}'", methodNameToken.Line, methodNameToken.Column);

    var args = new MaxonValue?[callee.ParamTypes.Count];
    args[0] = selfValue;

    if (!Check(TokenType.RightParen)) {
      ParseArgList(methodNameToken, callee, args, firstPositionalIndex: 1);
    }

    Expect(TokenType.RightParen);
    return FillDefaultArgs(methodNameToken, callee, args);
  }

  /// <summary>
  /// Creates a function call operation, validating the function exists and determining the result type.
  /// Handles struct return types by setting ResultStructTypeName.
  /// </summary>
  private MaxonCallOp CreateFunctionCall(Token functionNameToken, List<MaxonValue> args) {
    var functionName = functionNameToken.Value;

    var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == functionName) ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined function '{functionName}'", functionNameToken.Line, functionNameToken.Column);

    // E057: calling a throwing function without try
    if (callee.ThrowsType != null && !_inTryContext) {
      throw new CompileError(ErrorCode.SemanticThrowingFunctionRequiresTry,
        $"throwing function requires try: '{functionName}'",
        functionNameToken.Line, functionNameToken.Column);
    }

    MaxonValueKind? resultKind = null;
    string? resultStructTypeName = null;

    if (callee.ReturnType is MlirStructType retStructType) {
      resultKind = MaxonValueKind.Struct;
      resultStructTypeName = retStructType.Name;
    } else if (callee.ReturnType is MlirEnumType retEnumType) {
      resultKind = MaxonValueKind.Enum;
      resultStructTypeName = retEnumType.Name;
    } else if (callee.ReturnType != null) {
      resultKind = callee.ReturnType switch {
        { } t when t == MlirType.I64 => MaxonValueKind.Integer,
        { } t when t == MlirType.F64 => MaxonValueKind.Float,
        { } t when t == MlirType.I1 => MaxonValueKind.Bool,
        _ => throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unsupported return type '{callee.ReturnType}' for function '{functionName}'", functionNameToken.Line, functionNameToken.Column)
      };

      // For generic methods (returning I64 as Element placeholder), check if the caller's
      // struct type has a concrete Element type that should override the result kind
      if (resultKind == MaxonValueKind.Integer && args.Count > 0 && args[0] is MaxonStruct selfStruct) {
        Logger.Debug(LogCategory.Parser, $"CreateFunctionCall: checking Element type for {selfStruct.TypeName}");
        if (_typeRegistry.TryGetValue(selfStruct.TypeName, out var selfType)
            && selfType is MlirStructType selfStructType) {
          Logger.Debug(LogCategory.Parser, $"  TypeParams: {string.Join(", ", selfStructType.TypeParams.Select(kv => $"{kv.Key}={kv.Value}"))}");
          if (selfStructType.TypeParams.TryGetValue("Element", out var elementType)) {
            Logger.Debug(LogCategory.Parser, $"  elementType={elementType}, MlirType.F64={MlirType.F64}, equal={elementType == MlirType.F64}, name={elementType.Name}");
            if (elementType == MlirType.F64) {
              Logger.Debug(LogCategory.Parser, $"  Overriding resultKind to Float");
              resultKind = MaxonValueKind.Float;
            }
          }
        }
      }
    }

    var callOp = new MaxonCallOp(functionName, args, resultKind, resultStructTypeName);
    _currentBlock!.AddOp(callOp);
    return callOp;
  }

  // ============================================================================
  // Helpers
  // ============================================================================

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
