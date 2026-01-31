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

	private static readonly Dictionary<TokenType, (MaxonBinOperator Op, int Precedence)> BinaryOperators = new() {
		{ TokenType.Plus, (MaxonBinOperator.Add, 1) },
		{ TokenType.Minus, (MaxonBinOperator.Sub, 1) },
		{ TokenType.Star, (MaxonBinOperator.Mul, 2) },
		{ TokenType.Slash, (MaxonBinOperator.Div, 2) },
		{ TokenType.Mod, (MaxonBinOperator.Mod, 2) },
	};

	private static readonly Dictionary<TokenType, MaxonBinOperator> ComparisonOperators = new() {
		{ TokenType.EqualsEquals, MaxonBinOperator.Eq },
		{ TokenType.NotEquals, MaxonBinOperator.Ne },
		{ TokenType.LessThan, MaxonBinOperator.Lt },
		{ TokenType.GreaterThan, MaxonBinOperator.Gt },
		{ TokenType.LessEquals, MaxonBinOperator.Le },
		{ TokenType.GreaterEquals, MaxonBinOperator.Ge },
	};

	private static readonly Dictionary<string, Func<MaxonValue, (MaxonOp Op, MaxonValue Result)>> BuiltinOps = new() {
		{ "trunc", arg => { var op = new MaxonTruncOp(arg); return (op, op.Result); } },
		{ "abs", arg => { var op = new MaxonAbsOp(arg); return (op, op.Result); } },
	};

	private record VarInfo(MaxonValueKind Kind, bool Mutable, MaxonValue Value, MlirBlock<MaxonOp> DefinedInBlock);

	public MlirModule<MaxonOp> Parse() {
		Logger.Debug(LogCategory.Parser, "Starting parser");
		var module = new MlirModule<MaxonOp>();
		_currentModule = module;

		SkipNewlines();
		while (!IsAtEnd() && Current().Type != TokenType.Eof) {
			ParseTopLevel(module);
			SkipNewlines();
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
		} else {
			throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected function declaration, got '{Current().Value}'", Current().Line, Current().Column);
		}
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
			var kind = paramTypes[i].ToValueKind();
			var paramOp = new MaxonParamOp(i, paramNames[i], kind);
			_currentBlock.AddOp(paramOp);
			_variables[paramNames[i]] = new VarInfo(kind, false, paramOp.Result, _currentBlock);
		}

		ParseBodyUntilEnd();
		ExpectEndLabel(name);

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
			_ => throw new CompileError(ErrorCode.ParserExpectedType, $"Unknown type: {typeName}", Current().Line, Current().Column)
		};
	}

	private string ExpectTypeName() {
		if (Check(TokenType.Int)) return Advance().Value;
		if (Check(TokenType.Float)) return Advance().Value;
		if (Check(TokenType.Bool)) return Advance().Value;
		if (Check(TokenType.Identifier)) return Advance().Value;
		throw new CompileError(ErrorCode.ParserExpectedType, "Expected type name", Current().Line, Current().Column);
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
		} else if (Check(TokenType.If)) {
			ParseIf();
		} else if (Check(TokenType.While)) {
			ParseWhile();
		} else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Equals) {
			ParseAssignment();
		} else {
			throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected statement, got '{Current().Value}'", Current().Line, Current().Column);
		}
	}

	private void ParseReturn() {
		Advance(); // consume 'return'

		if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
			var value = ResolveExprValue(ParseExpression());
			_currentBlock!.AddOp(new MaxonReturnOp(value));
		} else {
			_currentBlock!.AddOp(new MaxonReturnOp());
		}
	}


	private void ParseVarDecl() {
		Advance(); // consume 'var'
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Expect(TokenType.Equals);
		var initValue = ResolveExprValue(ParseExpression()) ?? throw new InvalidOperationException($"Compiler bug: Variable '{name}' initialization expression did not produce a value (this should not happen - please report this bug)");
		var kind = initValue switch {
			MaxonInteger => MaxonValueKind.Integer,
			MaxonFloat => MaxonValueKind.Float,
			MaxonBool => MaxonValueKind.Bool,
			_ => throw new CompileError(ErrorCode.ParserExpectedExpression, $"Cannot determine type of value: {initValue.GetType().Name}", Current().Line, Current().Column)
		};
		_currentBlock!.AddOp(new MaxonAssignOp(name, initValue, isDeclaration: true, isMutable: true, kind));
		_variables[name] = new VarInfo(kind, true, initValue, _currentBlock!);
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

		var newValue = ResolveExprValue(ParseExpression());
		_currentBlock!.AddOp(new MaxonAssignOp(name, newValue, isDeclaration: false, isMutable: true, varInfo.Kind));
		_variables[name] = new VarInfo(varInfo.Kind, true, newValue, _currentBlock!);
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

		// Check for else
		MlirBlock<MaxonOp>? elseBlock = null;
		string? elseLabel = null;
		if (Check(TokenType.Else)) {
			Advance(); // consume 'else'
			var elseSourceLabel = Expect(TokenType.CharacterLiteral).Value;
			elseLabel = UniqueLabel(elseSourceLabel);
			SkipNewlines();

			elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
			_currentBlock = elseBlock;
			ParseBodyUntilEnd();
			ExpectEndLabel(elseSourceLabel);
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
		ParseBodyUntilEnd();
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

			var kind = DetermineValueKind(lhsVal, rhsVal);
			var binOp = new MaxonBinOp(cmpOperator, lhsVal, rhsVal, kind);
			_currentBlock!.AddOp(binOp);
			return new ExprResult.Direct(binOp.Result);
		}

		return lhs;
	}

	private ExprResult ParseExpression(int minPrecedence = 0) {
		var lhs = ParsePrimary();

		while (BinaryOperators.TryGetValue(Current().Type, out var entry) && entry.Precedence >= minPrecedence) {
			Advance(); // consume operator
			var rhs = ParseExpression(entry.Precedence + 1);

			MaxonValue lhsVal = ResolveExprValue(lhs);
			MaxonValue rhsVal = ResolveExprValue(rhs);
			var kind = DetermineValueKind(lhsVal, rhsVal);

			var binOp = new MaxonBinOp(entry.Op, lhsVal, rhsVal, kind);
			_currentBlock!.AddOp(binOp);
			lhs = new ExprResult.Direct(binOp.Result);
		}

		return lhs;
	}

	private ExprResult ParsePrimary() {
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

		if (Check(TokenType.Identifier)) {
			var token = Advance();
			if (Check(TokenType.LeftParen)) {
				if (BuiltinOps.TryGetValue(token.Value, out var makeOp)) {
					Advance(); // consume '('
					var arg = ResolveExprValue(ParseExpression());
					Expect(TokenType.RightParen);
					var (op, result) = makeOp(arg);
					_currentBlock!.AddOp(op);
					return new ExprResult.Direct(result);
				}
				Advance(); // consume '('
				var args = ParseCallArgs(token);
				var callOp = CreateFunctionCall(token, args);
				return new ExprResult.Direct(callOp.Result!);
			}
			// Variable reference
			if (_variables.TryGetValue(token.Value, out var varInfo)) {
				return new ExprResult.VarRef(token.Value, varInfo);
			}
			throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{token.Value}'", token.Line, token.Column);
		}

		throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got '{Current().Value}'", Current().Line, Current().Column);
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
				// Cross-block reference: emit a var_ref op
				var refOp = new MaxonVarRefOp(v.VarName, v.Info.Kind);
				_currentBlock!.AddOp(refOp);
				return refOp.Result;
			default:
				throw new InvalidOperationException($"Unknown expression result type: {expr.GetType().Name}");
		}
	}

	private abstract record ExprResult {
		public sealed record Direct(MaxonValue Value) : ExprResult;
		public sealed record VarRef(string VarName, VarInfo Info) : ExprResult;
	}

	private MaxonValueKind DetermineValueKind(MaxonValue lhs, MaxonValue rhs) {
		return (lhs, rhs) switch {
			(MaxonInteger, MaxonInteger) => MaxonValueKind.Integer,
			(MaxonFloat, MaxonFloat) => MaxonValueKind.Float,
			(MaxonBool, MaxonBool) => MaxonValueKind.Bool,
			_ => throw new CompileError(ErrorCode.ParserExpectedExpression,
				$"Cannot operate on {lhs.GetType().Name} and {rhs.GetType().Name}",
				Current().Line, Current().Column)
		};
	}

	/// <summary>
	/// Parses call arguments using first-positional, rest-named rule.
	/// Resolves named arguments to positional order based on the callee's parameter names.
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
		args[0] = ResolveExprValue(ParseExpression());
		int nextPositional = 1;

		while (Check(TokenType.Comma) && Advance() != null) {
			// Check for named argument: identifier followed by ':'
			if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Colon) {
				var nameToken = Advance();
				Advance(); // consume ':'
				var idx = callee.ParamNames.IndexOf(nameToken.Value);
				if (idx < 0)
					throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Unknown parameter '{nameToken.Value}' in call to '{callee.Name}'", nameToken.Line, nameToken.Column);
				args[idx] = ResolveExprValue(ParseExpression());
			} else {
				args[nextPositional] = ResolveExprValue(ParseExpression());
				nextPositional++;
			}
		}

		Expect(TokenType.RightParen);
		return args.ToList()!;
	}

	/// <summary>
	/// Creates a function call operation, validating the function exists and determining the result type.
	/// </summary>
	private MaxonCallOp CreateFunctionCall(Token functionNameToken, List<MaxonValue> args) {
		var functionName = functionNameToken.Value;

		var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == functionName) ?? throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined function '{functionName}'", functionNameToken.Line, functionNameToken.Column);
		MaxonValueKind? resultKind = callee.ReturnType switch {
				{ } t when t == MlirType.I64 => MaxonValueKind.Integer,
				{ } t when t == MlirType.F64 => MaxonValueKind.Float,
				{ } t when t == MlirType.I1 => MaxonValueKind.Bool,
			null => null, // void function
			_ => throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unsupported return type '{callee.ReturnType}' for function '{functionName}'", functionNameToken.Line, functionNameToken.Column)
		};

		var callOp = new MaxonCallOp(functionName, args, resultKind);
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
