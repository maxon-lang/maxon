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
			throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected function declaration, got {Current().Type}", Current().Line, Current().Column);
		}
	}

	private void ParseFunction(MlirModule<MaxonOp> module) {
		Expect(TokenType.Function);
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Logger.Debug(LogCategory.Parser, $"Parsing function: {name}");

		Expect(TokenType.LeftParen);
		var paramTypes = ParseParamList();

		MlirType? returnType = null;
		if (Check(TokenType.Returns)) {
			Advance();
			returnType = ParseTypeRef();
		}

		SkipNewlines();

		var func = new MlirFunction<MaxonOp>(name, paramTypes, returnType);
		module.AddFunction(func);
		_currentFunction = func;
		_currentBlock = func.Body.AddBlock("entry");
		_variables.Clear();

		ParseBodyUntilEnd();
		ExpectEndLabel(name);

		_currentFunction = null;
		_currentBlock = null;
	}

	// ============================================================================
	// Parameter and type parsing
	// ============================================================================

	private List<MlirType> ParseParamList() {
		var paramTypes = new List<MlirType>();
		// For now, no parameters supported
		Expect(TokenType.RightParen);
		return paramTypes;
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
		} else if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.Equals) {
			ParseAssignment();
		} else {
			throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected statement, got {Current().Type}", Current().Line, Current().Column);
		}
	}

	private void ParseReturn() {
		Advance(); // consume 'return'

		if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
			var value = ParseReturnExpression();
			_currentBlock!.AddOp(new MaxonReturnOp(value));
		} else {
			_currentBlock!.AddOp(new MaxonReturnOp());
		}
	}

	private MaxonValue ParseReturnExpression() {
		// Check for function call: identifier followed by '('
		if (Check(TokenType.Identifier) && PeekNext().Type == TokenType.LeftParen) {
			var token = Advance();
			Advance(); // consume '('
			Expect(TokenType.RightParen);

			// Determine return type of callee
			var callee = _currentModule!.Functions.FirstOrDefault(f => f.Name == token.Value);
			MaxonValueKind? resultKind = callee?.ReturnType switch {
				{ } t when t == MlirType.I64 => MaxonValueKind.Integer,
				{ } t when t == MlirType.F64 => MaxonValueKind.Float,
				{ } t when t == MlirType.I1 => MaxonValueKind.Bool,
				_ => null
			};

			var callOp = new MaxonCallOp(token.Value, [], resultKind);
			_currentBlock!.AddOp(callOp);
			return callOp.Result!;
		}

		return ResolveExprValue(ParseExpression());
	}

	private void ParseVarDecl() {
		Advance(); // consume 'var'
		var nameToken = Expect(TokenType.Identifier);
		var name = nameToken.Value;
		Expect(TokenType.Equals);
		var initValue = ResolveExprValue(ParseExpression());

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
		var thenLabel = Expect(TokenType.CharacterLiteral).Value;
		SkipNewlines();

		// Save entry block to append cond_br later
		var entryBlock = _currentBlock!;

		// Create and parse the then block
		var thenBlock = _currentFunction!.Body.AddBlock(thenLabel);
		_currentBlock = thenBlock;
		ParseBodyUntilEnd();

		// Parse: end 'thenLabel'
		var endToken = Expect(TokenType.End);
		if (Check(TokenType.CharacterLiteral)) {
			var label = Advance().Value;
			if (label != thenLabel) {
				throw new CompileError(ErrorCode.ParserMismatchedEndLabel, $"Mismatched end label: expected '{thenLabel}', got '{label}'", endToken.Line, endToken.Column);
			}
		}

		// Check for else
		string? elseLabel = null;
		if (Check(TokenType.Else)) {
			Advance(); // consume 'else'
			elseLabel = Expect(TokenType.CharacterLiteral).Value;
			SkipNewlines();

			var elseBlock = _currentFunction!.Body.AddBlock(elseLabel);
			_currentBlock = elseBlock;
			ParseBodyUntilEnd();
			ExpectEndLabel(elseLabel);
		}

		// Emit cond_br into the entry block
		if (elseLabel != null) {
			entryBlock.AddOp(new MaxonCondBrOp(condition, thenLabel, elseLabel));
		}

		_currentBlock = entryBlock;
	}

	// ============================================================================
	// Expression parsing
	// ============================================================================

	private ExprResult ParseComparisonExpression() {
		var lhs = ParseExpression();

		if (Check(TokenType.EqualsEquals)) {
			Advance(); // consume '=='
			var rhs = ParseExpression();

			MaxonValue lhsVal = ResolveExprValue(lhs);
			MaxonValue rhsVal = ResolveExprValue(rhs);

			var kind = DetermineValueKind(lhsVal, rhsVal);
			var binOp = new MaxonBinOp(MaxonBinOperator.Eq, lhsVal, rhsVal, kind);
			_currentBlock!.AddOp(binOp);
			return new ExprResult.Direct(binOp.Result);
		}

		return lhs;
	}

	private ExprResult ParseExpression() {
		var lhs = ParsePrimary();

		while (Check(TokenType.Plus) || Check(TokenType.Minus)) {
			var opType = Current().Type;
			Advance(); // consume '+' or '-'
			var rhs = ParsePrimary();

			MaxonValue lhsVal = ResolveExprValue(lhs);
			MaxonValue rhsVal = ResolveExprValue(rhs);
			var kind = DetermineValueKind(lhsVal, rhsVal);

			var binOperator = opType == TokenType.Plus ? MaxonBinOperator.Add : MaxonBinOperator.Sub;
			var binOp = new MaxonBinOp(binOperator, lhsVal, rhsVal, kind);
			_currentBlock!.AddOp(binOp);
			lhs = new ExprResult.Direct(binOp.Result);
		}

		return lhs;
	}

	private ExprResult ParsePrimary() {
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
				Advance(); // consume '('
				Expect(TokenType.RightParen);
				var callOp = new MaxonCallOp(token.Value, []);
				_currentBlock!.AddOp(callOp);
				return new ExprResult.Direct(callOp.Result!);
			}
			// Variable reference
			if (_variables.TryGetValue(token.Value, out var varInfo)) {
				return new ExprResult.VarRef(token.Value, varInfo);
			}
			throw new CompileError(ErrorCode.ParserExpectedExpression, $"Undefined variable '{token.Value}'", token.Line, token.Column);
		}

		throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got {Current().Type}", Current().Line, Current().Column);
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

	// ============================================================================
	// Helpers
	// ============================================================================

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
			throw new CompileError(ErrorCode.ParserExpectedToken, $"Expected {type} but got {Current().Type}", Current().Line, Current().Column);
		}
		return Advance();
	}

	private bool IsAtEnd() => _pos >= _tokens.Count;
}
