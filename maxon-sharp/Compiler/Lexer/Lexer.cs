namespace MaxonSharp.Lexer;

public class Lexer(string source) {
	private readonly string _source = source;
	private int _pos;
	private int _line = 1;
	private int _column = 1;

	private static readonly Dictionary<string, TokenType> Keywords = new() {
		// Control flow
		{ "function", TokenType.Function },
		{ "returns", TokenType.Returns },
		{ "return", TokenType.Return },
		{ "end", TokenType.End },
		{ "let", TokenType.Let },
		{ "var", TokenType.Var },
		{ "mod", TokenType.Mod },
		{ "type", TokenType.Type },
		{ "enum", TokenType.Enum },
		{ "array", TokenType.Array },
		{ "of", TokenType.Of },
		{ "if", TokenType.If },
		{ "else", TokenType.Else },
		{ "while", TokenType.While },
		{ "for", TokenType.For },
		{ "in", TokenType.In },
		{ "break", TokenType.Break },
		{ "continue", TokenType.Continue },
		{ "true", TokenType.True },
		{ "false", TokenType.False },
		{ "and", TokenType.And },
		{ "or", TokenType.Or },
		{ "not", TokenType.Not },
		// Type system
		{ "uses", TokenType.Uses },
		{ "is", TokenType.Is },
		{ "with", TokenType.With },
		{ "static", TokenType.Static },
		{ "typealias", TokenType.TypeAlias },
		{ "export", TokenType.Export },
		{ "self", TokenType.Self },
		{ "Self", TokenType.SelfType },
		{ "interface", TokenType.Interface },
		{ "extension", TokenType.Extension },
		{ "extends", TokenType.Extends },
		{ "from", TokenType.From },
		{ "to", TokenType.To },
		{ "as", TokenType.As },
		{ "gives", TokenType.Gives },
		// Error handling
		{ "throws", TokenType.Throws },
		{ "throw", TokenType.Throw },
		{ "try", TokenType.Try },
		{ "otherwise", TokenType.Otherwise },
		{ "ignore", TokenType.Ignore },
		// Match
		{ "match", TokenType.Match },
		{ "then", TokenType.Then },
		{ "fallthrough", TokenType.Fallthrough },
		{ "default", TokenType.Default },
		// Built-in functions
		{ "print", TokenType.Print },
		// Types
		{ "int", TokenType.Int },
		{ "float", TokenType.Float },
		{ "bool", TokenType.Bool },
		{ "byte", TokenType.Byte },
		{ "string", TokenType.String },
	};

	public List<Token> Tokenize() {
		Logger.Info(LogCategory.Lexer, "Starting lexer");
		var tokens = new List<Token>();

		while (!IsAtEnd()) {
			var token = NextToken();
			if (token != null) {
				tokens.Add(token);
				Logger.Trace(LogCategory.Lexer, $"Token: {token.Type} '{token.Value}' at {token.Line}:{token.Column}");
			}
		}

		tokens.Add(new Token(TokenType.Eof, "", _line, _column));
		Logger.Info(LogCategory.Lexer, $"Lexer complete: {tokens.Count} tokens");
		return tokens;
	}

	private Token? NextToken() {
		// Skip whitespace (but not newlines)
		while (!IsAtEnd() && (Current() == ' ' || Current() == '\t' || Current() == '\r')) {
			Advance();
		}

		if (IsAtEnd()) {
			return null;
		}

		var startLine = _line;
		var startColumn = _column;
		var c = Current();

		// Newline
		if (c == '\n') {
			Advance();
			_line++;
			_column = 1;
			return new Token(TokenType.Newline, "\n", startLine, startColumn);
		}

		// Single-character tokens
		switch (c) {
			case '(':
				Advance();
				return new Token(TokenType.LeftParen, "(", startLine, startColumn);
			case ')':
				Advance();
				return new Token(TokenType.RightParen, ")", startLine, startColumn);
			case '{':
				Advance();
				return new Token(TokenType.LeftBrace, "{", startLine, startColumn);
			case '}':
				Advance();
				return new Token(TokenType.RightBrace, "}", startLine, startColumn);
			case '[':
				Advance();
				return new Token(TokenType.LeftBracket, "[", startLine, startColumn);
			case ']':
				Advance();
				return new Token(TokenType.RightBracket, "]", startLine, startColumn);
			case '+':
				Advance();
				return new Token(TokenType.Plus, "+", startLine, startColumn);
			case '-':
				Advance();
				return new Token(TokenType.Minus, "-", startLine, startColumn);
			case '*':
				Advance();
				return new Token(TokenType.Star, "*", startLine, startColumn);
			case ',':
				Advance();
				return new Token(TokenType.Comma, ",", startLine, startColumn);
			case ':':
				Advance();
				return new Token(TokenType.Colon, ":", startLine, startColumn);
			case '&':
				Advance();
				return new Token(TokenType.Ampersand, "&", startLine, startColumn);
			case '|':
				Advance();
				return new Token(TokenType.Pipe, "|", startLine, startColumn);
			case '^':
				Advance();
				return new Token(TokenType.Caret, "^", startLine, startColumn);
		}

		// Multi-character operators
		if (c == '.') {
			if (Peek(1) == '.') {
				if (Peek(2) == '=') {
					Advance(); Advance(); Advance();
					return new Token(TokenType.DotDotEquals, "..=", startLine, startColumn);
				}
				if (Peek(2) == '<') {
					Advance(); Advance(); Advance();
					return new Token(TokenType.DotDotLess, "..<", startLine, startColumn);
				}
				Advance(); Advance();
				return new Token(TokenType.DotDot, "..", startLine, startColumn);
			}
			Advance();
			return new Token(TokenType.Dot, ".", startLine, startColumn);
		}

		if (c == '=') {
			if (Peek(1) == '=') {
				Advance(); Advance();
				return new Token(TokenType.EqualsEquals, "==", startLine, startColumn);
			}
			Advance();
			return new Token(TokenType.Equals, "=", startLine, startColumn);
		}

		if (c == '!') {
			if (Peek(1) == '=') {
				Advance(); Advance();
				return new Token(TokenType.NotEquals, "!=", startLine, startColumn);
			}
			// Single ! not supported
			Advance();
			return new Token(TokenType.Unknown, "!", startLine, startColumn);
		}

		if (c == '<') {
			if (Peek(1) == '<') {
				Advance(); Advance();
				return new Token(TokenType.LeftShift, "<<", startLine, startColumn);
			}
			if (Peek(1) == '=') {
				Advance(); Advance();
				return new Token(TokenType.LessEquals, "<=", startLine, startColumn);
			}
			Advance();
			return new Token(TokenType.LessThan, "<", startLine, startColumn);
		}

		if (c == '>') {
			if (Peek(1) == '>') {
				Advance(); Advance();
				return new Token(TokenType.RightShift, ">>", startLine, startColumn);
			}
			if (Peek(1) == '=') {
				Advance(); Advance();
				return new Token(TokenType.GreaterEquals, ">=", startLine, startColumn);
			}
			Advance();
			return new Token(TokenType.GreaterThan, ">", startLine, startColumn);
		}

		// Comments and slash
		if (c == '/') {
			if (Peek(1) == '/') {
				// Check for doc comment ///
				if (Peek(2) == '/') {
					Advance(); Advance(); Advance(); // Skip ///
																					 // Skip leading space
					if (!IsAtEnd() && Current() == ' ') Advance();
					var start = _pos;
					while (!IsAtEnd() && Current() != '\n') Advance();
					var text = _source[start.._pos];
					return new Token(TokenType.DocComment, text, startLine, startColumn);
				}
				// Regular comment - skip to end of line
				while (!IsAtEnd() && Current() != '\n') Advance();
				return NextToken(); // Return next actual token
			}
			Advance();
			return new Token(TokenType.Slash, "/", startLine, startColumn);
		}

		// Character literal (also used for end labels like 'main')
		if (c == '\'') {
			return ScanCharacterLiteral(startLine, startColumn);
		}

		// String literal
		if (c == '"') {
			return ScanStringLiteral(startLine, startColumn);
		}

		// Integer or float literal
		if (char.IsDigit(c)) {
			return ScanNumber(startLine, startColumn);
		}

		// Identifier or keyword
		if (char.IsLetter(c) || c == '_') {
			return ScanIdentifier(startLine, startColumn);
		}

		// Unknown
		Advance();
		return new Token(TokenType.Unknown, c.ToString(), startLine, startColumn);
	}

	private Token ScanNumber(int startLine, int startColumn) {
		var start = _pos;
		var c = Current();

		// Check for hex literal (0x or 0X)
		if (c == '0' && (Peek(1) == 'x' || Peek(1) == 'X')) {
			Advance(); Advance(); // Skip 0x
			while (!IsAtEnd() && (IsHexDigit(Current()) || Current() == '_')) {
				Advance();
			}
			return new Token(TokenType.IntegerLiteral, _source[start.._pos], startLine, startColumn);
		}

		// Check for binary literal (0b or 0B)
		if (c == '0' && (Peek(1) == 'b' || Peek(1) == 'B')) {
			Advance(); Advance(); // Skip 0b
			while (!IsAtEnd() && (Current() == '0' || Current() == '1' || Current() == '_')) {
				Advance();
			}
			return new Token(TokenType.IntegerLiteral, _source[start.._pos], startLine, startColumn);
		}

		// Check for octal literal (0o or 0O)
		if (c == '0' && (Peek(1) == 'o' || Peek(1) == 'O')) {
			Advance(); Advance(); // Skip 0o
			while (!IsAtEnd() && ((Current() >= '0' && Current() <= '7') || Current() == '_')) {
				Advance();
			}
			return new Token(TokenType.IntegerLiteral, _source[start.._pos], startLine, startColumn);
		}

		// Decimal integer or float
		while (!IsAtEnd() && (char.IsDigit(Current()) || Current() == '_')) {
			Advance();
		}

		// Check for float
		if (!IsAtEnd() && Current() == '.' && Peek(1) != '.') {
			Advance(); // consume .
			while (!IsAtEnd() && (char.IsDigit(Current()) || Current() == '_')) {
				Advance();
			}
			// Check for exponent
			if (!IsAtEnd() && (Current() == 'e' || Current() == 'E')) {
				Advance();
				if (!IsAtEnd() && (Current() == '+' || Current() == '-')) {
					Advance();
				}
				while (!IsAtEnd() && char.IsDigit(Current())) {
					Advance();
				}
			}
			return new Token(TokenType.FloatLiteral, _source[start.._pos], startLine, startColumn);
		}

		return new Token(TokenType.IntegerLiteral, _source[start.._pos], startLine, startColumn);
	}

	private Token ScanIdentifier(int startLine, int startColumn) {
		var start = _pos;
		while (!IsAtEnd() && (char.IsLetterOrDigit(Current()) || Current() == '_')) {
			Advance();
		}
		var value = _source[start.._pos];

		var type = Keywords.TryGetValue(value, out var keyword) ? keyword : TokenType.Identifier;
		if (type != TokenType.Identifier) {
			Logger.Debug(LogCategory.Lexer, $"Recognized keyword: {value}");
		}
		return new Token(type, value, startLine, startColumn);
	}

	private Token ScanCharacterLiteral(int startLine, int startColumn) {
		Advance(); // consume opening quote
		var start = _pos;
		while (!IsAtEnd() && Current() != '\'') {
			if (Current() == '\\' && !IsAtEnd(1)) {
				Advance(); Advance(); // Skip escape sequence
			} else {
				Advance();
			}
		}
		var value = _source[start.._pos];
		if (!IsAtEnd()) {
			Advance(); // consume closing quote
		}
		return new Token(TokenType.CharacterLiteral, value, startLine, startColumn);
	}

	private Token ScanStringLiteral(int startLine, int startColumn) {
		Advance(); // consume opening quote
		var start = _pos;
		var hasInterpolation = false;

		while (!IsAtEnd() && Current() != '"') {
			if (Current() == '\\' && !IsAtEnd(1)) {
				Advance(); Advance(); // Skip escape sequence
			} else if (Current() == '{') {
				hasInterpolation = true;
				Advance();
			} else {
				Advance();
			}
		}
		var value = _source[start.._pos];
		if (!IsAtEnd()) {
			Advance(); // consume closing quote
		}

		var type = hasInterpolation ? TokenType.StringInterp : TokenType.StringLiteral;
		return new Token(type, value, startLine, startColumn);
	}

	private char Current() => _source[_pos];

	private char Peek(int offset) {
		var pos = _pos + offset;
		return pos < _source.Length ? _source[pos] : '\0';
	}

	private void Advance() {
		_pos++;
		_column++;
	}

	private bool IsAtEnd(int offset = 0) => _pos + offset >= _source.Length;

	private static bool IsHexDigit(char c) =>
		(c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
