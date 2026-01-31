using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public class Parser(List<Token> tokens) {
  private readonly List<Token> _tokens = tokens;
  private int _pos;

  private MlirModule<MaxonOp>? _currentModule;
  private MlirFunction<MaxonOp>? _currentFunction;
  private MlirBlock<MaxonOp>? _currentBlock;
  private readonly Dictionary<string, VarInfo> _variables = [];
  private int _blockCounter;
  private readonly Stack<LoopContext> _loopStack = new();
  private readonly Dictionary<string, MlirStructType> _typeRegistry = [];

  private record LoopContext(string SourceLabel, string HeaderLabel, string ExitLabel);

  private static readonly Dictionary<TokenType, (MaxonBinOperator Op, int Precedence)> BinaryOperators = new() {
    { TokenType.Pipe, (MaxonBinOperator.BitOr, 0) },
    { TokenType.Caret, (MaxonBinOperator.BitXor, 1) },
    { TokenType.Ampersand, (MaxonBinOperator.BitAnd, 2) },
    { TokenType.LeftShift, (MaxonBinOperator.Shl, 3) },
    { TokenType.RightShift, (MaxonBinOperator.Shr, 3) },
    { TokenType.Plus, (MaxonBinOperator.Add, 4) },
    { TokenType.Minus, (MaxonBinOperator.Sub, 4) },
    { TokenType.Star, (MaxonBinOperator.Mul, 5) },
    { TokenType.Slash, (MaxonBinOperator.Div, 5) },
    { TokenType.Mod, (MaxonBinOperator.Mod, 5) },
  };

  private static readonly Dictionary<TokenType, MaxonBinOperator> ComparisonOperators = new() {
    { TokenType.EqualsEquals, MaxonBinOperator.Eq },
    { TokenType.NotEquals, MaxonBinOperator.Ne },
    { TokenType.LessThan, MaxonBinOperator.Lt },
    { TokenType.GreaterThan, MaxonBinOperator.Gt },
    { TokenType.LessEquals, MaxonBinOperator.Le },
    { TokenType.GreaterEquals, MaxonBinOperator.Ge },
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

  public MlirModule<MaxonOp> Parse() {
    Logger.Debug(LogCategory.Parser, "Starting parser");
    var module = new MlirModule<MaxonOp>();
    _currentModule = module;

    SkipNewlines();
    while (!IsAtEnd() && Current().Type != TokenType.Eof) {
      ParseTopLevel(module);
      SkipNewlines();
    }

    // Copy type registry to module for downstream passes
    foreach (var (name, type) in _typeRegistry) {
      module.TypeDefs[name] = type;
    }

    Logger.Debug(LogCategory.Parser, $"Parser complete: {module.Functions.Count} functions");
    return module;
  }

  // ============================================================================
  // Top-level parsing
  // ============================================================================

  private void ParseTopLevel(MlirModule<MaxonOp> module) {
    if (Check(TokenType.Function)) {
      ParseFunction(module);
    } else if (Check(TokenType.Type)) {
      ParseTypeDecl();
    } else {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected function declaration, got '{Current().Value}'", Current().Line, Current().Column);
    }
  }

  private void ParseTypeDecl() {
    Advance(); // consume 'type'
    var nameToken = Expect(TokenType.Identifier);
    var typeName = nameToken.Value;
    Logger.Debug(LogCategory.Parser, $"Parsing type: {typeName}");
    SkipNewlines();

    var fields = new List<MlirStructField>();
    while (!Check(TokenType.End) && !IsAtEnd()) {
      SkipNewlines();
      if (Check(TokenType.End)) break;

      bool isExported = false;
      if (Check(TokenType.Export)) {
        Advance();
        isExported = true;
      }

      bool isMutable;
      if (Check(TokenType.Var)) {
        Advance();
        isMutable = true;
      } else if (Check(TokenType.Let)) {
        Advance();
        isMutable = false;
      } else {
        throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected 'var' or 'let' in field declaration, got '{Current().Value}'", Current().Line, Current().Column);
      }

      var fieldName = Expect(TokenType.Identifier).Value;

      MlirType fieldType;
      MlirAttribute? defaultValue = null;

      if (Check(TokenType.Equals)) {
        // Default value with inferred type: export var value = 0
        Advance();
        (fieldType, defaultValue) = ParseFieldDefault();
      } else {
        fieldType = ParseTypeRef();
      }

      fields.Add(new MlirStructField(fieldName, fieldType, isExported, isMutable, defaultValue));
      SkipNewlines();
    }

    var structType = new MlirStructType(typeName, fields);
    _typeRegistry[typeName] = structType;
    ExpectEndLabel(typeName);
  }

  private (MlirType type, MlirAttribute defaultValue) ParseFieldDefault() {
    if (Check(TokenType.Minus)) {
      Advance(); // consume '-'
      if (Check(TokenType.IntegerLiteral)) {
        var val = -ParseIntegerLiteral(Advance().Value);
        return (MlirType.I64, new IntegerAttr(val, MlirType.I64));
      }
      if (Check(TokenType.FloatLiteral)) {
        var val = -double.Parse(Advance().Value, CultureInfo.InvariantCulture);
        return (MlirType.F64, new FloatAttr(val, MlirType.F64));
      }
      throw new CompileError(ErrorCode.ParserExpectedExpression, "Expected number after '-' in default value", Current().Line, Current().Column);
    }
    if (Check(TokenType.IntegerLiteral)) {
      var val = ParseIntegerLiteral(Advance().Value);
      return (MlirType.I64, new IntegerAttr(val, MlirType.I64));
    }
    if (Check(TokenType.FloatLiteral)) {
      var val = double.Parse(Advance().Value, CultureInfo.InvariantCulture);
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
    var (paramNames, paramTypes) = ParseParamList();

    MlirType? returnType = null;
    if (Check(TokenType.Returns)) {
      Advance();
      returnType = ParseTypeRef();
    }

    SkipNewlines();

    var func = new MlirFunction<MaxonOp>(name, paramNames, paramTypes, returnType);
    module.AddFunction(func);
    _currentFunction = func;
    _currentBlock = func.Body.AddBlock("entry");
    _variables.Clear();
    _blockCounter = 0;

    // Emit param ops and register parameters as variables
    for (int i = 0; i < paramNames.Count; i++) {
      var paramType = paramTypes[i];
      if (paramType is MlirStructType structType) {
        // Struct param: emit MaxonStructParamOp, register as struct var
        var structParamOp = new MaxonStructParamOp(i, paramNames[i], structType.Name);
        _currentBlock.AddOp(structParamOp);
        _variables[paramNames[i]] = new VarInfo(MaxonValueKind.Struct, false, structParamOp.Result, _currentBlock, structType.Name);
      } else {
        var kind = paramType.ToValueKind();
        var paramOp = new MaxonParamOp(i, paramNames[i], kind);
        _currentBlock.AddOp(paramOp);
        _variables[paramNames[i]] = new VarInfo(kind, false, paramOp.Result, _currentBlock);
      }
    }

    ParseBodyUntilEnd();
    ExpectEndLabel(name);

    // Add implicit return for void functions if the last block doesn't have a terminator
    if (returnType == null && _currentBlock != null && !BlockEndsWithTerminator(_currentBlock)) {
      _currentBlock.AddOp(new MaxonReturnOp());
    }

    _currentFunction = null;
    _currentBlock = null;
  }

  // ============================================================================
  // Parameter and type parsing
  // ============================================================================

  private (List<string> Names, List<MlirType> Types) ParseParamList() {
    var names = new List<string>();
    var types = new List<MlirType>();
    if (!Check(TokenType.RightParen)) {
      do {
        var paramName = Expect(TokenType.Identifier).Value;
        var paramType = ParseTypeRef();
        names.Add(paramName);
        types.Add(paramType);
      } while (Check(TokenType.Comma) && Advance() != null);
    }
    Expect(TokenType.RightParen);
    return (names, types);
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
      // Field assignment: p.x = 30
      ParseFieldAssignment();
    } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Equals) {
      ParseAssignment();
    } else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.LeftParen) {
      ParseCallStatement();
    } else {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected statement, got '{Current().Value}'", Current().Line, Current().Column);
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
        var value = ResolveExprValue(ParseComparisonExpression());
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
        $"Cannot return '{s.TypeName}' from function declared to return '{returnType.ToValueKind().ToString().ToLower()}'",
        returnToken.Line, returnToken.Column),
      _ => throw new InvalidOperationException($"Compiler bug: unknown MaxonValue type '{value.GetType().Name}'")
    };

    var expectedKind = returnType.ToValueKind();
    if (valueKind != expectedKind) {
      throw new CompileError(ErrorCode.SemanticTypeMismatch,
        $"Cannot return '{valueKind.ToString().ToLower()}' from function declared to return '{expectedKind.ToString().ToLower()}'",
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
    var initExpr = ParseComparisonExpression();
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

    if (!_variables.TryGetValue(name, out var varInfo)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{name}'", nameToken.Line, nameToken.Column);
    }
    if (!varInfo.Mutable) {
      throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Variable '{name}' is not mutable", nameToken.Line, nameToken.Column);
    }

    var newValue = ResolveExprValue(ParseComparisonExpression());
    _currentBlock!.AddOp(new MaxonAssignOp(name, newValue, isDeclaration: false, isMutable: true, varInfo.Kind));
    _variables[name] = new VarInfo(varInfo.Kind, true, newValue, _currentBlock!, varInfo.StructTypeName);
  }

  private void ParseFieldAssignment() {
    var nameToken = Advance(); // consume struct var name
    var name = nameToken.Value;
    Expect(TokenType.Dot);
    var fieldToken = Expect(TokenType.Identifier);
    var fieldName = fieldToken.Value;
    Expect(TokenType.Equals);

    if (!_variables.TryGetValue(name, out var varInfo)) {
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{name}'", nameToken.Line, nameToken.Column);
    }

    // Check instance mutability first: let structs cannot have any fields assigned
    if (!varInfo.Mutable) {
      throw new CompileError(ErrorCode.ImmutableVariable,
        $"cannot assign to immutable variable: '{name}'",
        nameToken.Line, nameToken.Column);
    }

    // Check field mutability: even on a var struct, let fields cannot be modified
    var structType = _typeRegistry[varInfo.StructTypeName!];
    var field = structType.GetField(fieldName) ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess,
      $"Type '{structType.Name}' has no field named '{fieldName}'",
      fieldToken.Line, fieldToken.Column);

    if (!field.IsMutable) {
      throw new CompileError(ErrorCode.ImmutableVariable,
        $"cannot assign to field '{structType.Name}.{fieldName}' because it is immutable (declare with 'var' to make it mutable)",
        nameToken.Line, nameToken.Column);
    }

    var newValue = ResolveExprValue(ParseComparisonExpression());
    var structVal = ResolveExprValue(new ExprResult.VarRef(name, varInfo));
    _currentBlock!.AddOp(new MaxonFieldAssignOp(structVal, varInfo.StructTypeName!, fieldName, newValue));
  }

  private void ParseCallStatement() {
    var token = Advance(); // consume identifier
    Advance(); // consume '('
    var args = ParseCallArgs(token);
    CreateFunctionCall(token, args);
  }

  private void ParseIf() {
    Advance(); // consume 'if'

    var condition = ResolveExprValue(ParseComparisonExpression());

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
    var condition = ResolveExprValue(ParseComparisonExpression());

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

  private ExprResult ParseComparisonExpression() {
    var lhs = ParseExpression();

    if (ComparisonOperators.TryGetValue(Current().Type, out var cmpOperator)) {
      Advance(); // consume comparison operator
      var rhs = ParseExpression();

      MaxonValue lhsVal = ResolveExprValue(lhs);
      MaxonValue rhsVal = ResolveExprValue(rhs);

      var (kind, promotedLhs, promotedRhs) = DetermineValueKind(lhsVal, rhsVal);
      var binOp = new MaxonBinOp(cmpOperator, promotedLhs, promotedRhs, kind);
      _currentBlock!.AddOp(binOp);
      return new ExprResult.Direct(binOp.Result);
    }

    return lhs;
  }

  private ExprResult ParseExpression(int minPrecedence = 0) {
    var lhs = ParsePrimary();

    // Handle 'as' cast expressions (postfix, binds tighter than binary ops)
    while (Check(TokenType.As)) {
      Advance(); // consume 'as'
      var targetKind = ParseTypeKeyword();
      var inputVal = ResolveExprValue(lhs);
      var castOp = new MaxonCastOp(inputVal, targetKind);
      _currentBlock!.AddOp(castOp);
      lhs = new ExprResult.Direct(castOp.Result);
    }

    while (BinaryOperators.TryGetValue(Current().Type, out var entry) && entry.Precedence >= minPrecedence) {
      Advance(); // consume operator
      var rhs = ParseExpression(entry.Precedence + 1);

      MaxonValue lhsVal = ResolveExprValue(lhs);
      MaxonValue rhsVal = ResolveExprValue(rhs);
      var (kind, promotedLhs, promotedRhs) = DetermineValueKind(lhsVal, rhsVal);

      // Division always produces a float result
      if (entry.Op == MaxonBinOperator.Div && kind == MaxonValueKind.Integer) {
        promotedLhs = PromoteIntToFloat(promotedLhs);
        promotedRhs = PromoteIntToFloat(promotedRhs);
        kind = MaxonValueKind.Float;
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
      if (Check(TokenType.IntegerLiteral)) {
        var token = Advance();
        var value = -ParseIntegerLiteral(token.Value);
        var op = new MaxonLiteralOp(value);
        _currentBlock!.AddOp(op);
        return new ExprResult.Direct(op.Result);
      }

      if (Check(TokenType.FloatLiteral)) {
        var token = Advance();
        var value = -double.Parse(token.Value, CultureInfo.InvariantCulture);
        var op = new MaxonLiteralOp(value);
        _currentBlock!.AddOp(op);
        return new ExprResult.Direct(op.Result);
      }

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

    if (Check(TokenType.IntegerLiteral)) {
      var token = Advance();
      var value = ParseIntegerLiteral(token.Value);
      var op = new MaxonLiteralOp(value);
      _currentBlock!.AddOp(op);
      return new ExprResult.Direct(op.Result);
    }

    if (Check(TokenType.FloatLiteral)) {
      var token = Advance();
      var value = double.Parse(token.Value, CultureInfo.InvariantCulture);
      var op = new MaxonLiteralOp(value);
      _currentBlock!.AddOp(op);
      return new ExprResult.Direct(op.Result);
    }

    if (Check(TokenType.True) || Check(TokenType.False)) {
      var token = Advance();
      var op = new MaxonLiteralOp(token.Type == TokenType.True);
      _currentBlock!.AddOp(op);
      return new ExprResult.Direct(op.Result);
    }

    if (Check(TokenType.Identifier)) {
      var token = Advance();

      // Check for struct literal: TypeName{...} or TypeName { ... }
      if (_typeRegistry.ContainsKey(token.Value) && Check(TokenType.LeftBrace)) {
        return ParseStructLiteral(token.Value);
      }

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
        Advance(); // consume '('
        var args = ParseCallArgs(token);
        var callOp = CreateFunctionCall(token, args);
        if (callOp.Result != null)
          return new ExprResult.Direct(callOp.Result);
        throw new CompileError(ErrorCode.ParserExpectedExpression, $"Function '{token.Value}' does not return a value", token.Line, token.Column);
      }

      // Variable reference
      if (_variables.TryGetValue(token.Value, out var varInfo)) {
        var result = new ExprResult.VarRef(token.Value, varInfo) as ExprResult;

        // Check for field access chain: varName.fieldName
        while (Check(TokenType.Dot)) {
          Advance(); // consume '.'
          var fieldToken = Expect(TokenType.Identifier);
          var fieldName = fieldToken.Value;

          // Determine the struct type name
          string structTypeName;
          if (result is ExprResult.VarRef vr) {
            structTypeName = vr.Info.StructTypeName
              ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Variable '{vr.VarName}' is not a struct type", token.Line, token.Column);
          } else if (result is ExprResult.Direct d && d.Value is MaxonStruct ms) {
            structTypeName = ms.TypeName;
          } else {
            throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Cannot access field on non-struct value", fieldToken.Line, fieldToken.Column);
          }

          var structType = _typeRegistry[structTypeName];
          var field = structType.GetField(fieldName)
            ?? throw new CompileError(ErrorCode.MlirInvalidFieldAccess, $"Type '{structTypeName}' has no field named '{fieldName}'", fieldToken.Line, fieldToken.Column);

          var fieldKind = field.Type.ToValueKind();
          var structVal = ResolveExprValue(result);
          var accessOp = new MaxonFieldAccessOp(structVal, structTypeName, fieldName, fieldKind);
          _currentBlock!.AddOp(accessOp);
          result = new ExprResult.Direct(accessOp.Result);
        }

        return result;
      }
      throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{token.Value}'", token.Line, token.Column);
    }

    throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got '{Current().Value}'", Current().Line, Current().Column);
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
        var value = ResolveExprValue(ParseComparisonExpression());
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
        MaxonLiteralOp defaultOp;
        if (field.DefaultValue is IntegerAttr intAttr && field.Type == MlirType.I1) {
          defaultOp = new MaxonLiteralOp(intAttr.Value != 0);
        } else if (field.DefaultValue is IntegerAttr intAttr2) {
          defaultOp = new MaxonLiteralOp(intAttr2.Value);
        } else if (field.DefaultValue is FloatAttr floatAttr) {
          defaultOp = new MaxonLiteralOp(floatAttr.Value);
        } else {
          throw new CompileError(ErrorCode.ParserExpectedExpression,
            $"Unsupported default value type for field '{field.Name}'",
            Current().Line, Current().Column);
        }
        _currentBlock!.AddOp(defaultOp);
        fieldValues.Add((field.Name, defaultOp.Result));
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

  private (MaxonValueKind Kind, MaxonValue Lhs, MaxonValue Rhs) DetermineValueKind(MaxonValue lhs, MaxonValue rhs) {
    return (lhs, rhs) switch {
      (MaxonInteger, MaxonInteger) => (MaxonValueKind.Integer, lhs, rhs),
      (MaxonFloat, MaxonFloat) => (MaxonValueKind.Float, lhs, rhs),
      (MaxonBool, MaxonBool) => (MaxonValueKind.Bool, lhs, rhs),
      (MaxonByte, MaxonByte) => (MaxonValueKind.Byte, lhs, rhs),
      (MaxonInteger, MaxonFloat) => (MaxonValueKind.Float, PromoteIntToFloat(lhs), rhs),
      (MaxonFloat, MaxonInteger) => (MaxonValueKind.Float, lhs, PromoteIntToFloat(rhs)),
      _ => throw new CompileError(ErrorCode.ParserExpectedExpression,
        $"Cannot operate on {lhs.GetType().Name} and {rhs.GetType().Name}",
        Current().Line, Current().Column)
    };
  }

  private MaxonFloat PromoteIntToFloat(MaxonValue intValue) {
    var op = new MaxonIntToFloatOp(intValue);
    _currentBlock!.AddOp(op);
    return op.Result;
  }

  /// <summary>
  /// Parses call arguments using first-positional, rest-named rule.
  /// Resolves named arguments to positional order based on the callee's parameter names.
  /// Handles struct arguments (including anonymous struct literals inferred from parameter type).
  /// </summary>
  private List<MaxonValue> ParseCallArgs(Token functionNameToken) {
    if (Check(TokenType.RightParen)) {
      Advance();
      return [];
    }

    var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == functionNameToken.Value)
      ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined function '{functionNameToken.Value}'", functionNameToken.Line, functionNameToken.Column);

    var args = new MaxonValue?[callee.ParamTypes.Count];

    // First argument is always positional
    args[0] = ParseCallArgValue(callee.ParamTypes[0]);
    int nextPositional = 1;

    while (Check(TokenType.Comma) && Advance() != null) {
      // Check for named argument: identifier followed by ':'
      if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
        var nameToken = Advance();
        Advance(); // consume ':'
        var idx = callee.ParamNames.IndexOf(nameToken.Value);
        if (idx < 0)
          throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Unknown parameter '{nameToken.Value}' in call to '{callee.Name}'", nameToken.Line, nameToken.Column);
        args[idx] = ParseCallArgValue(callee.ParamTypes[idx]);
      } else {
        args[nextPositional] = ParseCallArgValue(callee.ParamTypes[nextPositional]);
        nextPositional++;
      }
    }

    Expect(TokenType.RightParen);
    return args.ToList()!;
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

  private static long ParseIntegerLiteral(string text) {
    text = text.Replace("_", "");
    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
      return Convert.ToInt64(text[2..], 16);
    if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
      return Convert.ToInt64(text[2..], 2);
    if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
      return Convert.ToInt64(text[2..], 8);
    return long.Parse(text);
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
