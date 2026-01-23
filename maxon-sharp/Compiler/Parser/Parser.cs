using MaxonSharp.Lexer;

namespace MaxonSharp.Parser;

public class Parser(List<Token> tokens) {
	private readonly List<Token> _tokens = tokens;
	private int _pos;

	public ProgramAst Parse() {
		var functions = new List<FunctionDecl>();

		SkipNewlines();
		while (!IsAtEnd() && Current().Type != TokenType.Eof) {
			functions.Add(ParseFunction());
			SkipNewlines();
		}

		return new ProgramAst(functions);
	}

	private FunctionDecl ParseFunction() {
		Expect(TokenType.Function);
		var name = Expect(TokenType.Identifier).Value;
		Expect(TokenType.LeftParen);
		Expect(TokenType.RightParen);

		TypeRef? returnType = null;
		if (Check(TokenType.Returns)) {
			Advance();
			returnType = ParseTypeRef();
		}

		SkipNewlines();

		var body = new List<Stmt>();
		while (!Check(TokenType.End) && !IsAtEnd()) {
			body.Add(ParseStatement());
			SkipNewlines();
		}

		Expect(TokenType.End);

		// Consume optional end label like 'main'
		if (Check(TokenType.CharacterLiteral)) {
			Advance();
		}

		return new FunctionDecl(name, returnType, body);
	}

	private TypeRef ParseTypeRef() {
		if (Check(TokenType.Int)) {
			Advance();
			return new IntTypeRef();
		}
		throw new Exception($"Expected type at {Current().Line}:{Current().Column}");
	}

	private Stmt ParseStatement() {
		if (Check(TokenType.Return)) {
			Advance();
			Expr? value = null;
			if (!Check(TokenType.Newline) && !Check(TokenType.End) && !Check(TokenType.Eof)) {
				value = ParseExpression();
			}
			return new ReturnStmt(value);
		}

		throw new Exception($"Unexpected token {Current().Type} at {Current().Line}:{Current().Column}");
	}

	private Expr ParseExpression() {
		if (Check(TokenType.IntegerLiteral)) {
			var value = long.Parse(Advance().Value);
			return new IntLiteralExpr(value);
		}
		if (Check(TokenType.Identifier)) {
			var name = Advance().Value;
			return new IdentifierExpr(name);
		}

		throw new Exception($"Expected expression at {Current().Line}:{Current().Column}");
	}

	private void SkipNewlines() {
		while (Check(TokenType.Newline)) {
			Advance();
		}
	}

	private Token Current() => _tokens[_pos];

	private bool Check(TokenType type) => !IsAtEnd() && Current().Type == type;

	private Token Advance() {
		if (!IsAtEnd()) _pos++;
		return _tokens[_pos - 1];
	}

	private Token Expect(TokenType type) {
		if (!Check(type)) {
			throw new Exception($"Expected {type} but got {Current().Type} at {Current().Line}:{Current().Column}");
		}
		return Advance();
	}

	private bool IsAtEnd() => _pos >= _tokens.Count;
}
