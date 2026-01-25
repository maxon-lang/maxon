namespace MaxonSharp.Compiler;

public enum TokenType {
	// Keywords
	Function,
	Returns,
	Return,
	End,
	Let,
	Var,
	Mod,
	Type,
	Enum,
	Array,
	Of,
	If,
	Else,
	While,
	For,
	In,
	Break,
	Continue,
	True,
	False,
	And,
	Or,
	Not,
	// Type system keywords
	Uses,
	Is,
	With,
	Static,
	TypeAlias,
	Export,
	Self,
	SelfType,  // Self (the type)
	Interface,
	Extension,
	Extends,
	From,
	To,
	As,
	Gives,
	// Error handling keywords
	Throws,
	Throw,
	Try,
	Otherwise,
	Ignore,
	// Match statement keywords
	Match,
	Then,
	Fallthrough,
	Default,

	// Types
	Int,
	Float,
	Bool,
	Byte,

	// Literals
	Identifier,
	IntegerLiteral,
	FloatLiteral,
	StringLiteral,
	StringInterp,
	CharacterLiteral,

	// Punctuation
	LeftParen,
	RightParen,
	LeftBrace,
	RightBrace,
	LeftBracket,
	RightBracket,
	Equals,
	EqualsEquals,
	NotEquals,
	LessThan,
	LessEquals,
	GreaterThan,
	GreaterEquals,
	Plus,
	Minus,
	Star,
	Slash,
	Comma,
	Colon,
	Dot,
	Ampersand,
	Pipe,
	Caret,
	LeftShift,
	RightShift,
	DotDot,
	DotDotEquals,
	DotDotLess,

	// Formatting
	Newline,
	DocComment,
	Eof,

	// Special
	Unknown
}

public enum KeywordCategory {
	Control,
	Other,
	Logical,
	Constant,
	TypeKeyword,
}

public enum OperatorCategory {
	Bitwise,
	Comparison,
	Arithmetic,
	Assignment,
}

public record KeywordInfo(TokenType Type, KeywordCategory Category, string HelpText, bool CanHaveBlockLabel);
public record OperatorInfo(TokenType Type, OperatorCategory Category, string HelpText);

public record Token(TokenType Type, string Value, int Line, int Column);

public class Lexer(string source) {
	private readonly string _source = source;
	private int _pos;
	private int _line = 1;
	private int _column = 1;

	// Keyword map: { keyword_text, TokenType, KeywordCategory, help_text, can_have_block_label }
	public static readonly Dictionary<string, KeywordInfo> KeywordMap = new() {
		{ "function", new(TokenType.Function, KeywordCategory.Other, "Declares a function. Functions contain executable code and can return values.\n\nExample:\n```maxon\nfunction add(a int, b int) returns int\n    return a + b\nend 'add'\n```", false) },
		{ "returns", new(TokenType.Returns, KeywordCategory.Other, "Specifies the return type of a function.", false) },
		{ "return", new(TokenType.Return, KeywordCategory.Control, "Returns a value from a function and exits the function.", false) },
		{ "end", new(TokenType.End, KeywordCategory.Control, "Ends a block (function, type, if, for, while, etc.). Must be followed by the block's label in quotes.", true) },
		{ "let", new(TokenType.Let, KeywordCategory.Other, "Declares an immutable variable. The value cannot be changed after initialization.", false) },
		{ "var", new(TokenType.Var, KeywordCategory.Other, "Declares a mutable variable. The value can be changed after initialization.", false) },
		{ "mod", new(TokenType.Mod, KeywordCategory.Logical, "Modulo operator. Returns the remainder of division.", false) },
		{ "type", new(TokenType.Type, KeywordCategory.Other, "Declares a struct type with fields and methods.\n\nExample:\n```maxon\ntype Point\n    var x int\n    var y int\nend 'Point'\n```", false) },
		{ "enum", new(TokenType.Enum, KeywordCategory.Other, "Declares an enumeration type with a fixed set of cases.\n\nExample:\n```maxon\nenum Color\n    red\n    green\n    blue\nend 'Color'\n```", false) },
		{ "array", new(TokenType.Array, KeywordCategory.TypeKeyword, "Array type declaration.", false) },
		{ "of", new(TokenType.Of, KeywordCategory.TypeKeyword, "Used in array type declarations (array of int).", false) },
		{ "if", new(TokenType.If, KeywordCategory.Control, "Conditional statement. Executes code if the condition is true.", true) },
		{ "else", new(TokenType.Else, KeywordCategory.Control, "Alternative branch in an if statement. Executed when the condition is false.", true) },
		{ "while", new(TokenType.While, KeywordCategory.Control, "Loop that continues while the condition is true.", true) },
		{ "for", new(TokenType.For, KeywordCategory.Control, "Loop that iterates over a range or collection.", true) },
		{ "in", new(TokenType.In, KeywordCategory.Control, "Used in for loops to specify the range or collection to iterate over.", false) },
		{ "break", new(TokenType.Break, KeywordCategory.Control, "Exits the current loop immediately.", false) },
		{ "continue", new(TokenType.Continue, KeywordCategory.Control, "Skips the rest of the current loop iteration and continues with the next iteration.", false) },
		{ "true", new(TokenType.True, KeywordCategory.Constant, "Boolean literal representing true.", false) },
		{ "false", new(TokenType.False, KeywordCategory.Constant, "Boolean literal representing false.", false) },
		{ "and", new(TokenType.And, KeywordCategory.Logical, "Logical AND operator. Returns true if both operands are true.", false) },
		{ "or", new(TokenType.Or, KeywordCategory.Logical, "Logical OR operator. Returns true if either operand is true.", true) },
		{ "not", new(TokenType.Not, KeywordCategory.Logical, "Logical NOT operator. Negates a boolean value.", false) },
		{ "int", new(TokenType.Int, KeywordCategory.TypeKeyword, "Primitive integer type (64-bit signed).", false) },
		{ "float", new(TokenType.Float, KeywordCategory.TypeKeyword, "Primitive floating-point type (64-bit double precision).", false) },
		{ "bool", new(TokenType.Bool, KeywordCategory.TypeKeyword, "Primitive boolean type (true or false).", false) },
		{ "byte", new(TokenType.Byte, KeywordCategory.TypeKeyword, "Primitive byte type (8-bit unsigned integer).", false) },
		{ "uses", new(TokenType.Uses, KeywordCategory.Other, "Declares associated types in an interface.", false) },
		{ "typealias", new(TokenType.TypeAlias, KeywordCategory.Other, "Declares a type alias for an existing type or associated type in an interface.", false) },
		{ "is", new(TokenType.Is, KeywordCategory.Logical, "Specifies that a type conforms to an interface.", false) },
		{ "with", new(TokenType.With, KeywordCategory.Other, "Specifies interface conformance requirements.", false) },
		{ "static", new(TokenType.Static, KeywordCategory.Other, "Declares a static method that doesn't require an instance.", false) },
		{ "export", new(TokenType.Export, KeywordCategory.Other, "Makes a function or type visible to other modules.", false) },
		{ "self", new(TokenType.Self, KeywordCategory.Other, "Refers to the current instance in a method.", false) },
		{ "Self", new(TokenType.SelfType, KeywordCategory.Other, "Refers to the current type in a method signature.", false) },
		{ "interface", new(TokenType.Interface, KeywordCategory.Other, "Declares an interface that types can conform to.\n\nExample:\n```maxon\ninterface Printable\n    function print() returns int\nend 'Printable'\n```", false) },
		{ "extension", new(TokenType.Extension, KeywordCategory.Other, "Defines extension methods for an interface.\n\nExample:\n```maxon\nextension Iterable\n    function map(transform (Element) returns Element) returns Array of Element\n        ...\n    end 'map'\nend 'Iterable'\n```", false) },
		{ "extends", new(TokenType.Extends, KeywordCategory.Other, "Indicates interface inheritance.", false) },
		{ "from", new(TokenType.From, KeywordCategory.Other, "Used in range expressions (from X to Y).", false) },
		{ "to", new(TokenType.To, KeywordCategory.Other, "Used in range expressions (from X to Y).", false) },
		{ "as", new(TokenType.As, KeywordCategory.Logical, "Type cast operator. Converts a value to a different type.", false) },
		{ "gives", new(TokenType.Gives, KeywordCategory.Control, "Used in iterator expressions.", false) },
		{ "throws", new(TokenType.Throws, KeywordCategory.Other, "Indicates that a function may throw an error.", false) },
		{ "throw", new(TokenType.Throw, KeywordCategory.Control, "Throws an error that can be caught by a try-catch block.", false) },
		{ "try", new(TokenType.Try, KeywordCategory.Control, "Attempts an operation that may throw an error.", false) },
		{ "otherwise", new(TokenType.Otherwise, KeywordCategory.Control, "Provides a fallback for try expressions when an error occurs.", true) },
		{ "ignore", new(TokenType.Ignore, KeywordCategory.Control, "Used with otherwise to silently ignore errors.", false) },
		{ "match", new(TokenType.Match, KeywordCategory.Control, "Pattern matching statement for enums and values.", false) },
		{ "then", new(TokenType.Then, KeywordCategory.Control, "Used in match expressions to separate pattern from result.", true) },
		{ "fallthrough", new(TokenType.Fallthrough, KeywordCategory.Control, "Falls through to the next case in a match statement.", false) },
		{ "default", new(TokenType.Default, KeywordCategory.Control, "Default case in a match statement.", false) },
	};

	// Operator map: { operator_text, TokenType, OperatorCategory, help_text }
	public static readonly Dictionary<string, OperatorInfo> OperatorMap = new() {
		{ "<<", new(TokenType.LeftShift, OperatorCategory.Bitwise, "Bitwise left shift operator. Shifts bits to the left.") },
		{ ">>", new(TokenType.RightShift, OperatorCategory.Bitwise, "Bitwise right shift operator. Shifts bits to the right.") },
		{ "&", new(TokenType.Ampersand, OperatorCategory.Bitwise, "Bitwise AND operator. Performs bitwise AND operation.") },
		{ "|", new(TokenType.Pipe, OperatorCategory.Bitwise, "Bitwise OR operator. Performs bitwise OR operation.") },
		{ "^", new(TokenType.Caret, OperatorCategory.Bitwise, "Bitwise XOR operator. Performs bitwise exclusive OR operation.") },
		{ "==", new(TokenType.EqualsEquals, OperatorCategory.Comparison, "Equality operator. Returns true if operands are equal.") },
		{ "!=", new(TokenType.NotEquals, OperatorCategory.Comparison, "Inequality operator. Returns true if operands are not equal.") },
		{ ">=", new(TokenType.GreaterEquals, OperatorCategory.Comparison, "Greater than or equal operator. Returns true if left operand is greater than or equal to right operand.") },
		{ ">", new(TokenType.GreaterThan, OperatorCategory.Comparison, "Greater than operator. Returns true if left operand is greater than right operand.") },
		{ "<=", new(TokenType.LessEquals, OperatorCategory.Comparison, "Less than or equal operator. Returns true if left operand is less than or equal to right operand.") },
		{ "<", new(TokenType.LessThan, OperatorCategory.Comparison, "Less than operator. Returns true if left operand is less than right operand.") },
		{ "+", new(TokenType.Plus, OperatorCategory.Arithmetic, "Addition operator. Adds two numbers.") },
		{ "-", new(TokenType.Minus, OperatorCategory.Arithmetic, "Subtraction operator. Subtracts right operand from left operand.") },
		{ "*", new(TokenType.Star, OperatorCategory.Arithmetic, "Multiplication operator. Multiplies two numbers.") },
		{ "/", new(TokenType.Slash, OperatorCategory.Arithmetic, "Division operator. Divides left operand by right operand.") },
		{ "=", new(TokenType.Equals, OperatorCategory.Assignment, "Assignment operator. Assigns the value on the right to the variable on the left.") },
	};

	public List<Token> Tokenize() {
		Logger.Debug(LogCategory.Lexer, "Starting lexer");
		var tokens = new List<Token>();

		while (!IsAtEnd()) {
			var token = NextToken();
			if (token != null) {
				tokens.Add(token);
				Logger.Trace(LogCategory.Lexer, $"Token: {token.Type} '{token.Value}' at {token.Line}:{token.Column}");
			}
		}

		tokens.Add(new Token(TokenType.Eof, "", _line, _column));
		Logger.Debug(LogCategory.Lexer, $"Lexer complete: {tokens.Count} tokens");
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

		var type = KeywordMap.TryGetValue(value, out var keywordInfo) ? keywordInfo.Type : TokenType.Identifier;
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
