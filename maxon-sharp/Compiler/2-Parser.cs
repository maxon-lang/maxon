using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public class Parser(List<Token> tokens, MlirModule<MaxonOp>? seedModule = null) {
  private readonly List<Token> _tokens = tokens;
  private int _pos;

  private MlirModule<MaxonOp>? _currentModule;
  private MlirFunction<MaxonOp>? _currentFunction;
  private MlirBlock<MaxonOp>? _currentBlock;
  private readonly Dictionary<string, VarInfo> _variables = [];
  private readonly HashSet<string> _referencedVars = [];
  private int _blockCounter;
  private readonly Stack<LoopContext> _loopStack = new();
  private readonly Dictionary<string, MlirStructType> _typeRegistry = seedModule != null
    ? new(seedModule.TypeDefs) : [];
  private string? _currentTypeName;

  // Top-level compile-time constants (name -> evaluated value: long, double, or bool)
  private Dictionary<string, object> _topLevelConstants = [];

  // Global mutable variables (name -> type info)
  private record GlobalVarInfo(MaxonValueKind Kind, bool Mutable);
  private readonly Dictionary<string, GlobalVarInfo> _globalVars = [];

  // Default parameter values (funcName -> index -> value)
  private readonly Dictionary<string, Dictionary<int, MlirAttribute>> _functionDefaults = [];

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

    // Seed module with functions and defaults from previously parsed modules
    if (seedModule != null) {
      foreach (var func in seedModule.Functions) {
        if (!module.Functions.Any(f => f.Name == func.Name))
          module.AddFunction(func);
      }
      foreach (var (name, defaults) in seedModule.FunctionDefaults) {
        _functionDefaults.TryAdd(name, defaults);
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
        constDecls.Add(new ConstantDecl(nameToken.Value, exprStart, _pos, nameToken.Line, nameToken.Column));
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
      } else if (Check(TokenType.TypeAlias)) {
        PreScanTypeAlias();
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

    // Only register if not already known (avoid duplicates)
    if (!module.Functions.Any(f => f.Name == funcName)) {
      var func = new MlirFunction<MaxonOp>(funcName, paramNames, paramTypes, returnType);
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
    while (Check(TokenType.Comma)) {
      Advance();
      names.Add(Expect(TokenType.Identifier).Value);
    }
    return names;
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

    var conformingInterfaces = ParseConformanceClause();
    var associatedTypeNames = ParseUsesClause();

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

      SkipToEndOfLine();
      SkipNewlines();
    }

    // Replace temporary entry with complete struct type
    _typeRegistry[typeName] = new MlirStructType(typeName, fields, associatedTypeNames, conformingInterfaces);
    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);

    // consume 'end'
    if (Check(TokenType.End)) Advance();
    // Skip end label
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  /// <summary>
  /// Pre-scan a typealias declaration: typealias Name is SourceType with (Type1, Type2, ...)
  /// Creates a concrete struct type by substituting associated type parameters.
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
    Expect(TokenType.LeftParen);

    var concreteTypes = new List<MlirType>();
    concreteTypes.Add(ParseTypeRef());
    while (Check(TokenType.Comma)) {
      Advance();
      concreteTypes.Add(ParseTypeRef());
    }
    Expect(TokenType.RightParen);

    if (concreteTypes.Count != sourceStruct.AssociatedTypeNames.Count)
      throw new CompileError(ErrorCode.ParserExpectedType,
        $"Type '{sourceName}' expects {sourceStruct.AssociatedTypeNames.Count} type argument(s), got {concreteTypes.Count}",
        aliasNameToken.Line, aliasNameToken.Column);

    // Build substitution map: associated type name -> concrete type
    var substitution = new Dictionary<string, MlirType>();
    for (int i = 0; i < sourceStruct.AssociatedTypeNames.Count; i++) {
      substitution[sourceStruct.AssociatedTypeNames[i]] = concreteTypes[i];
    }

    // Create concrete fields with substituted types
    var concreteFields = new List<MlirStructField>();
    foreach (var field in sourceStruct.Fields) {
      var fieldType = substitution.TryGetValue(field.Type.Name, out var concreteType)
        ? concreteType
        : field.Type;
      concreteFields.Add(new MlirStructField(field.Name, fieldType, field.IsExported, field.IsMutable, field.DefaultValue));
    }

    _typeRegistry[aliasName] = new MlirStructType(aliasName, concreteFields);
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

    // Instance method gets 'self' as first parameter
    var structType = _typeRegistry[typeName];
    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { (MlirType)structType };
    allParamTypes.AddRange(paramTypes);

    // Only register if not already known
    if (!module.Functions.Any(f => f.Name == methodName)) {
      var func = new MlirFunction<MaxonOp>(methodName, allParamNames, allParamTypes, returnType);
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
      if (Check(TokenType.Function) || Check(TokenType.If) || Check(TokenType.While)) {
        depth++;
      } else if (Check(TokenType.End)) {
        depth--;
      }
      Advance();
    }
    // Skip end label if present
    if (Check(TokenType.CharacterLiteral)) Advance();
  }

  private void SkipTopLevelBlock() {
    // Skip to matching 'end' by tracking nesting depth
    // For typealias, there's no end - just skip to end of line
    if (Check(TokenType.TypeAlias)) {
      SkipToEndOfLine();
      return;
    }

    Advance(); // consume 'function' or 'type'
    int depth = 1;
    while (!IsAtEnd() && depth > 0) {
      if (Check(TokenType.Function) || Check(TokenType.Type) || Check(TokenType.If) || Check(TokenType.While)) {
        depth++;
      } else if (Check(TokenType.End)) {
        depth--;
      }
      Advance();
    }
    // Skip the end label if present
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
    } else if (Check(TokenType.Let) || Check(TokenType.Var)) {
      // Already handled in pre-scan; skip over
      SkipTopLevelDecl();
    } else if (Check(TokenType.TypeAlias)) {
      SkipTypeAlias();
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

  private void ParseTypeDecl(MlirModule<MaxonOp> module) {
    Advance(); // consume 'type'
    var nameToken = Expect(TokenType.Identifier);
    var typeName = nameToken.Value;
    _currentTypeName = typeName;
    Logger.Debug(LogCategory.Parser, $"Parsing type: {typeName}");

    ParseConformanceClause();
    var associatedTypeNames = ParseUsesClause();
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

      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected 'var' or 'let' in field declaration, got '{Current().Value}'", Current().Line, Current().Column);
    }

    _currentTypeName = null;
    RemoveAssociatedTypePlaceholders(associatedTypeNames);
    ExpectEndLabel(typeName);
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

    SkipNewlines();
    SetupFunctionParsing(module, methodName, paramNames, paramTypes, paramDefaults, returnType);
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

    SkipNewlines();

    // Instance method gets 'self' as first parameter (the struct type)
    var structType = _typeRegistry[typeName];
    var allParamNames = new List<string> { "self" };
    allParamNames.AddRange(paramNames);
    var allParamTypes = new List<MlirType> { structType };
    allParamTypes.AddRange(paramTypes);

    // Store defaults (offset by 1 for 'self' parameter)
    var offsetDefaults = new Dictionary<int, MlirAttribute>();
    foreach (var (idx, attr) in paramDefaults) {
      offsetDefaults[idx + 1] = attr;
    }

    SetupFunctionParsing(module, methodName, allParamNames, allParamTypes, offsetDefaults, returnType);

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
      Dictionary<int, MlirAttribute> paramDefaults, MlirType? returnType) {
    // Replace pre-registered stub with full function (or create new one)
    var existing = module.Functions.FirstOrDefault(f => f.Name == funcName);
    if (existing != null) {
      module.Functions.Remove(existing);
    }
    var func = new MlirFunction<MaxonOp>(funcName, paramNames, paramTypes, returnType);
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

    SkipNewlines();
    SetupFunctionParsing(module, name, paramNames, paramTypes, paramDefaults, returnType);
    EmitParameters(paramNames, paramTypes, paramTokens);

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
          if (_currentModule!.Functions.Any(f => f.Name == instanceMethodName)) {
            ParseInstanceMethodCallStatement(varInfo, instanceMethodName);
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

    if (returnType is MlirStructType expectedStruct) {
      if (value is not MaxonStruct actualStruct || actualStruct.TypeName != expectedStruct.Name) {
        var actualName = value is MaxonStruct s ? s.TypeName : value.GetType().Name.Replace("Maxon", "").ToLower();
        throw new CompileError(ErrorCode.SemanticTypeMismatch,
          $"Cannot return '{actualName}' from function declared to return '{expectedStruct.Name}'",
          returnToken.Line, returnToken.Column);
      }
      return;
    }

    var valueKind = value switch {
      MaxonInteger => MaxonValueKind.Integer,
      MaxonFloat => MaxonValueKind.Float,
      MaxonBool => MaxonValueKind.Bool,
      MaxonByte => MaxonValueKind.Byte,
      MaxonStruct s => throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Cannot return '{s.TypeName}' from function declared to return '{KindDisplayName(returnType.ToValueKind())}'",
        returnToken.Line, returnToken.Column),
      _ => throw new InvalidOperationException($"Compiler bug: unknown MaxonValue type '{value.GetType().Name}'")
    };

    var expectedKind = returnType.ToValueKind();
    if (valueKind != expectedKind) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Cannot return '{KindDisplayName(valueKind)}' from function declared to return '{KindDisplayName(expectedKind)}'",
        returnToken.Line, returnToken.Column);
    }
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
    } else {
      var kind = initValue switch {
        MaxonInteger => MaxonValueKind.Integer,
        MaxonFloat => MaxonValueKind.Float,
        MaxonBool => MaxonValueKind.Bool,
        MaxonByte => MaxonValueKind.Byte,
        _ => throw new CompileError(ErrorCode.ParserExpectedExpression, $"Cannot determine type of value: {initValue.GetType().Name}", Current().Line, Current().Column)
      };
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
      throw new CompileError(ErrorCode.ImmutableVariable,
        $"cannot assign to immutable variable: '{name}'",
        nameToken.Line, nameToken.Column);
    }

    EmitFieldAssignment(name, varInfo, fieldToken, nameToken);
  }

  private void EmitFieldAssignment(string varName, VarInfo varInfo, Token fieldToken, Token errorToken) {
    var structType = _typeRegistry[varInfo.StructTypeName!];
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
      throw new CompileError(ErrorCode.ImmutableVariable,
        $"cannot assign to field '{structType.Name}.{fieldToken.Value}' because it is immutable (declare with 'var' to make it mutable)",
        errorToken.Line, errorToken.Column);
    }

    var newValue = ResolveExprValue(ParseExpression());
    var structVal = ResolveExprValue(new ExprResult.VarRef(varName, varInfo));
    _currentBlock!.AddOp(new MaxonFieldAssignOp(structVal, varInfo.StructTypeName!, fieldToken.Value, newValue));
  }

  private void ParseCallStatement() {
    var token = Advance(); // consume identifier

    if (TrySiblingMethodCall(token) != null)
      return;

    Advance(); // consume '('
    var args = ParseCallArgs(token);
    CreateFunctionCall(token, args);
  }

  private MaxonCallOp? TrySiblingMethodCall(Token token) {
    if (_currentTypeName == null || !_variables.TryGetValue("self", out var selfInfo))
      return null;
    var siblingMethodName = $"{_currentTypeName}.{token.Value}";
    if (!_currentModule!.Functions.Any(f => f.Name == siblingMethodName))
      return null;
    Advance(); // consume '('
    var structVal = ResolveExprValue(new ExprResult.VarRef("self", selfInfo));
    var qualifiedFuncToken = new Token(TokenType.Identifier, siblingMethodName, token.Line, token.Column);
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
    return lastOp is MaxonReturnOp or MaxonBrOp or MaxonCondBrOp;
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

      string structTypeName;
      if (result is ExprResult.VarRef vr) {
        structTypeName = vr.Info.StructTypeName
          ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Variable '{vr.VarName}' is not a struct type", originToken.Line, originToken.Column);
      } else if (result is ExprResult.Direct d && d.Value is MaxonStruct ms) {
        structTypeName = ms.TypeName;
      } else {
        throw new CompileError(ErrorCode.MlirInvalidFieldAccess, "Cannot access field on non-struct value", fieldToken.Line, fieldToken.Column);
      }

      var structType = _typeRegistry[structTypeName];

      // Check for method call: expr.method(...)
      if (Check(TokenType.LeftParen)) {
        var qualifiedMethodName = $"{structTypeName}.{fieldName}";
        var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == qualifiedMethodName);
        if (callee != null) {
          Advance(); // consume '('
          var structVal = ResolveExprValue(result);
          var qualifiedFuncToken = new Token(TokenType.Identifier, qualifiedMethodName, fieldToken.Line, fieldToken.Column);
          var allArgs = ParseInstanceMethodCallArgs(qualifiedFuncToken, structVal);
          var callOp = CreateFunctionCall(qualifiedFuncToken, allArgs);
          if (callOp.Result != null)
            return new ExprResult.Direct(callOp.Result);
          return new ExprResult.Direct(structVal);
        }
      }

      var field = structType.GetField(fieldName)
        ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type '{structTypeName}' has no field named '{fieldName}'", fieldToken.Line, fieldToken.Column);

      if (!field.IsExported && _currentTypeName != structTypeName) {
        throw new CompileError(ErrorCode.SemanticUnexportedFieldAccess, $"cannot access unexported field: '{fieldName}' outside of type '{structTypeName}'", fieldToken.Line, fieldToken.Column);
      }

      var fieldKind = field.Type.ToValueKind();
      var fieldStructName = field.Type is MlirStructType fst ? fst.Name : null;
      var structVal2 = ResolveExprValue(result);
      var accessOp = new MaxonFieldAccessOp(structVal2, structTypeName, fieldName, fieldKind, fieldStructName);
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
  private ExprResult.Direct ParseStructLiteral(string typeName) {
    Advance(); // consume '{'
    var structType = _typeRegistry[typeName];
    var fieldValues = new List<(string, MaxonValue)>();
    var providedFields = new HashSet<string>();

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
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Field '{field.Name}' of type '{typeName}' requires a value (no default defined)",
            Current().Line, Current().Column);
        }
        var errorToken = new Token(TokenType.Identifier, field.Name, Current().Line, Current().Column);
        var defaultValue = EmitDefaultLiteral(field.DefaultValue, field.Type, errorToken,
          $"Unsupported default value type for field '{field.Name}'");
        fieldValues.Add((field.Name, defaultValue));
      }
    }

    Expect(TokenType.RightBrace);

    var structLiteral = new MaxonStructLiteralOp(typeName, fieldValues);
    _currentBlock!.AddOp(structLiteral);
    return new ExprResult.Direct(structLiteral.Result);
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
        // Cross-block reference: emit a var_ref or struct_var_ref op
        if (v.Info.Kind == MaxonValueKind.Struct) {
          var structRefOp = new MaxonStructVarRefOp(v.VarName, v.Info.StructTypeName!);
          _currentBlock!.AddOp(structRefOp);
          return structRefOp.Result;
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
    _ => throw new InvalidOperationException($"Unhandled MaxonValueKind: {kind}")
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

    // Implicit type conversion for function arguments
    for (int i = 0; i < args.Length; i++) {
      var argKind = DetermineValueKind(args[i]!);
      var paramType = callee.ParamTypes[i];
      if (paramType is MlirStructType) continue;

      var paramKind = MlirTypeToValueKind(paramType);
      if (argKind == paramKind) continue;

      args[i] = ConvertArgToParamType(args[i]!, argKind, paramKind, callee.ParamNames[i], functionNameToken);
    }

    return args.ToList()!;
  }

  private static MaxonValueKind MlirTypeToValueKind(MlirType type) {
    if (type == MlirType.I64) return MaxonValueKind.Integer;
    if (type == MlirType.F64) return MaxonValueKind.Float;
    if (type == MlirType.I1) return MaxonValueKind.Bool;
    if (type == MlirType.I8) return MaxonValueKind.Byte;
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

    MaxonValueKind? resultKind = null;
    string? resultStructTypeName = null;

    if (callee.ReturnType is MlirStructType retStructType) {
      resultKind = MaxonValueKind.Struct;
      resultStructTypeName = retStructType.Name;
    } else if (callee.ReturnType != null) {
      resultKind = callee.ReturnType switch {
        { } t when t == MlirType.I64 => MaxonValueKind.Integer,
        { } t when t == MlirType.F64 => MaxonValueKind.Float,
        { } t when t == MlirType.I1 => MaxonValueKind.Bool,
        _ => throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unsupported return type '{callee.ReturnType}' for function '{functionName}'", functionNameToken.Line, functionNameToken.Column)
      };
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
