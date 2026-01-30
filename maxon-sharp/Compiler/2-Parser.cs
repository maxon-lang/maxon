using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public class Parser(List<Token> tokens, int sourceFileIndex) {
	private readonly List<Token> _tokens = tokens;
	private readonly int _sourceFileIndex = sourceFileIndex;
	private int _pos;

	private MlirFunction? _currentFunction;
	private MlirBlock? _currentBlock;

	public MlirModule Parse() {
		Logger.Debug(LogCategory.Parser, "Starting parser");
		var module = new MlirModule();

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

	private void ParseTopLevel(MlirModule module) {
		if (Check(TokenType.Function)) {
			ParseFunction(module);
		} else {
			throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected function declaration, got {Current().Type}", Current().Line, Current().Column);
		}
	}

	private void ParseFunction(MlirModule module) {
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

		var func = new MlirFunction(name, paramTypes, returnType);
		module.AddFunction(func);
		_currentFunction = func;
		_currentBlock = func.Body.AddBlock("entry");

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
		} else {
			throw new CompileError(ErrorCode.ParserUnexpectedToken, $"Expected statement, got {Current().Type}", Current().Line, Current().Column);
		}
	}

	private void ParseReturn() {
		Advance(); // consume 'return'

		if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
			_currentBlock!.AddOp(new MaxonReturnOp(ParseExpression()));
		} else {
			_currentBlock!.AddOp(new MaxonReturnOp());
		}
	}

	// ============================================================================
	// Expression parsing
	// ============================================================================

	private MaxonExpr ParseExpression() {
		if (Check(TokenType.IntegerLiteral)) {
			var token = Advance();
			var value = ParseIntegerLiteral(token.Value);
			var op = new MaxonConstantOp(value, MlirType.I64);
			_currentBlock!.AddOp(op);
			return new MaxonExpr.Value(op.Result);
		}

		if (Check(TokenType.Identifier)) {
			var token = Advance();
			if (Check(TokenType.LeftParen)) {
				Advance(); // consume '('
				Expect(TokenType.RightParen);
				var callOp = new MaxonCallOp(token.Value, []);
				_currentBlock!.AddOp(callOp);
				return new MaxonExpr.Call(callOp);
			}
			throw new CompileError(ErrorCode.ParserExpectedExpression, $"Unexpected identifier '{token.Value}'", token.Line, token.Column);
		}

		throw new CompileError(ErrorCode.ParserExpectedExpression, $"Expected expression, got {Current().Type}", Current().Line, Current().Column);
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
