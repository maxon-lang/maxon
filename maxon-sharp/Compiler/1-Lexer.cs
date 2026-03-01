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
  Union,
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
  Skip,
  True,
  False,
  And,
  Or,
  Not,
  Xor,
  Shl,
  Shr,
  // Type system keywords
  Uses,
  Implements,
  With,
  Static,
  TypeAlias,
  Export,
  Self,
  SelfType,  // Self (the type)
  Interface,
  Extension,
  Extends,
  Where,
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
  Upto,
  Is,
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
  ByteStringLiteral,
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

  // Formatting
  Newline,
  DocComment,
  Eof,

  // Special
  Unknown
}

public enum OperatorCategory {
  Comparison,
  Arithmetic,
  Assignment,
}

public record KeywordInfo(TokenType Type, string HelpText, bool CanHaveBlockLabel);
public record OperatorInfo(TokenType Type, OperatorCategory Category, string HelpText);

public record Token(TokenType Type, string Value, int Line, int Column) {
  private static readonly Dictionary<TokenType, string> DisplayNames = BuildDisplayNames();

  private static Dictionary<TokenType, string> BuildDisplayNames() {
    var names = new Dictionary<TokenType, string>();
    foreach (var (text, info) in Lexer.KeywordMap)
      names[info.Type] = $"'{text}'";
    foreach (var (text, info) in Lexer.OperatorMap)
      names[info.Type] = $"'{text}'";
    names[TokenType.LeftParen] = "'('";
    names[TokenType.RightParen] = "')'";
    names[TokenType.LeftBrace] = "'{'";
    names[TokenType.RightBrace] = "'}'";
    names[TokenType.LeftBracket] = "'['";
    names[TokenType.RightBracket] = "']'";
    names[TokenType.Comma] = "','";
    names[TokenType.Colon] = "':'";
    names[TokenType.Dot] = "'.'";
    names[TokenType.Newline] = "newline";
    names[TokenType.Eof] = "end of file";
    return names;
  }

  public static string DisplayName(TokenType type) =>
    DisplayNames.TryGetValue(type, out var name) ? name : type.ToString().ToLowerInvariant();
}

public class Lexer(string source) {
  private readonly string _source = source;
  private int _pos;
  private int _line = 1;
  private int _column = 1;

  // Keyword map: { keyword_text, TokenType, help_text, can_have_block_label }
  public static readonly Dictionary<string, KeywordInfo> KeywordMap = new() {
    { "function", new(TokenType.Function, "Declares a function. Functions contain executable code and can return values.\n\nExample:\n```maxon\nfunction add(a int, b int) returns int\n    return a + b\nend 'add'\n```", false) },
    { "returns", new(TokenType.Returns, "Specifies the return type of a function.", false) },
    { "return", new(TokenType.Return, "Returns a value from a function and exits the function.", false) },
    { "end", new(TokenType.End, "Ends a block (function, type, if, for, while, etc.). Must be followed by the block's label in quotes.", true) },
    { "let", new(TokenType.Let, "Declares an immutable variable. The value cannot be changed after initialization.", false) },
    { "var", new(TokenType.Var, "Declares a mutable variable. The value can be changed after initialization.", false) },
    { "type", new(TokenType.Type, "Declares a struct type with fields and methods.\n\nExample:\n```maxon\ntype Point\n    var x int\n    var y int\nend 'Point'\n```", false) },
    { "union", new(TokenType.Union, "Declares a union type with a fixed set of named cases, optional associated values, and methods.\n\nExample:\n```maxon\nunion Color\n    red\n    green\n    blue\nend 'Color'\n```", false) },
    { "enum", new(TokenType.Enum, "Declares a named group of typed constant values with no methods.\n\nExample:\n```maxon\nenum HttpStatus\n    ok = 200\n    notFound = 404\nend 'HttpStatus'\n```", false) },
    { "of", new(TokenType.Of, "Used in array type declarations (array of int).", false) },
    { "if", new(TokenType.If, "Conditional statement. Executes code if the condition is true.", true) },
    { "else", new(TokenType.Else, "Alternative branch in an if statement. Executed when the condition is false.", true) },
    { "while", new(TokenType.While, "Loop that continues while the condition is true.", true) },
    { "for", new(TokenType.For, "Loop that iterates over a range or collection.", true) },
    { "in", new(TokenType.In, "Used in for loops to specify the range or collection to iterate over.", false) },
    { "break", new(TokenType.Break, "Exits the current loop or match statement.", false) },
    { "continue", new(TokenType.Continue, "Skips the rest of the current loop iteration and continues with the next iteration.", false) },
    { "skip", new(TokenType.Skip, "Skips the current iteration and the next n elements in a for loop.", false) },
    { "true", new(TokenType.True, "Boolean literal representing true.", false) },
    { "false", new(TokenType.False, "Boolean literal representing false.", false) },
    { "and", new(TokenType.And, "AND operator. Logical AND on bools, bitwise AND on integers.", false) },
    { "or", new(TokenType.Or, "OR operator. Logical OR on bools, bitwise OR on integers.", true) },
    { "not", new(TokenType.Not, "NOT operator. Logical NOT on bools, bitwise NOT on integers.", false) },
    { "xor", new(TokenType.Xor, "XOR operator. Logical XOR on bools, bitwise XOR on integers.", false) },
    { "shl", new(TokenType.Shl, "Shift left operator. Shifts integer bits to the left.", false) },
    { "shr", new(TokenType.Shr, "Shift right operator. Shifts integer bits to the right.", false) },
    { "int", new(TokenType.Int, "Primitive integer type (64-bit signed).", false) },
    { "float", new(TokenType.Float, "Primitive floating-point type (64-bit double precision).", false) },
    { "bool", new(TokenType.Bool, "Primitive boolean type (true or false).", false) },
    { "byte", new(TokenType.Byte, "Primitive byte type (8-bit unsigned integer).", false) },
    { "uses", new(TokenType.Uses, "Declares associated types in an interface.", false) },
    { "typealias", new(TokenType.TypeAlias, "Declares a type alias for an existing type or associated type in an interface.", false) },
    { "implements", new(TokenType.Implements, "Specifies that a type conforms to an interface.", false) },
    { "with", new(TokenType.With, "Specifies interface conformance requirements.", false) },
    { "static", new(TokenType.Static, "Declares a static method that doesn't require an instance.", false) },
    { "export", new(TokenType.Export, "Makes a function or type visible to other modules.", false) },
    { "self", new(TokenType.Self, "Refers to the current instance in a method.", false) },
    { "Self", new(TokenType.SelfType, "Refers to the current type in a method signature.", false) },
    { "interface", new(TokenType.Interface, "Declares an interface that types can conform to.\n\nExample:\n```maxon\ninterface Printable\n    function print() returns int\nend 'Printable'\n```", false) },
    { "extension", new(TokenType.Extension, "Defines extension methods for an interface.\n\nExample:\n```maxon\nextension Iterable\n    function map(transform (Element) returns Element) returns Array of Element\n        ...\n    end 'map'\nend 'Iterable'\n```", false) },
    { "extends", new(TokenType.Extends, "Indicates interface inheritance.", false) },
    { "where", new(TokenType.Where, "Constrains type parameters to require interface conformance.", false) },
    { "from", new(TokenType.From, "Used in range expressions (from X to Y).", false) },
    { "to", new(TokenType.To, "Used in range expressions (from X to Y).", false) },
    { "as", new(TokenType.As, "Type cast operator. Converts a value to a different type.", false) },
    { "gives", new(TokenType.Gives, "Used in iterator expressions.", false) },
    { "throws", new(TokenType.Throws, "Indicates that a function may throw an error.", false) },
    { "throw", new(TokenType.Throw, "Throws an error that can be caught by a try-catch block.", false) },
    { "try", new(TokenType.Try, "Attempts an operation that may throw an error.", false) },
    { "otherwise", new(TokenType.Otherwise, "Provides a fallback for try expressions when an error occurs.", true) },
    { "ignore", new(TokenType.Ignore, "Used with otherwise to silently ignore errors.", false) },
    { "match", new(TokenType.Match, "Pattern matching statement for unions and values.", false) },
    { "then", new(TokenType.Then, "Used in match expressions to separate pattern from result.", true) },
    { "fallthrough", new(TokenType.Fallthrough, "Falls through to the next case in a match statement.", false) },
    { "default", new(TokenType.Default, "Default case in a match statement.", false) },
    { "upto", new(TokenType.Upto, "Exclusive upper bound in range patterns.", false) },
    { "is", new(TokenType.Is, "Reference identity operator. Returns true if two variables refer to the same object. Use 'is not' for inequality.", false) },
  };

  // Operator map: { operator_text, TokenType, OperatorCategory, help_text }
  public static readonly Dictionary<string, OperatorInfo> OperatorMap = new() {
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
    { "mod", new(TokenType.Mod, OperatorCategory.Arithmetic, "Modulo operator. Returns the remainder of division.") },
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
        if (Peek(1) == '-' && Peek(2) == '-' && IsLineSeparator(3)) {
          _pos = _source.Length;
          return null;
        }
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
    }

    // Multi-character operators
    if (c == '.') {
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

    if (c == '!' && Peek(1) == '=') {
      Advance(); Advance();
      return new Token(TokenType.NotEquals, "!=", startLine, startColumn);
    }

    if (c == '<') {
      if (Peek(1) == '=') {
        Advance(); Advance();
        return new Token(TokenType.LessEquals, "<=", startLine, startColumn);
      }
      Advance();
      return new Token(TokenType.LessThan, "<", startLine, startColumn);
    }

    if (c == '>') {
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

    // Byte string literal: b"..."
    if (c == 'b' && Peek(1) == '"') {
      Advance(); // consume 'b'
      return ScanByteStringLiteral(startLine, startColumn);
    }

    // Integer or float literal
    if (char.IsDigit(c)) {
      return ScanNumber(startLine, startColumn);
    }

    // Identifier or keyword: starts with letter or underscore, continues with alphanumeric or underscore
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

    var type = KeywordMap.TryGetValue(value, out var keywordInfo) ? keywordInfo.Type
      : OperatorMap.TryGetValue(value, out var opInfo) ? opInfo.Type
      : TokenType.Identifier;
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

  private Token ScanByteStringLiteral(int startLine, int startColumn) {
    Advance(); // consume opening quote
    var start = _pos;

    while (!IsAtEnd() && Current() != '"') {
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

    return new Token(TokenType.ByteStringLiteral, value, startLine, startColumn);
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

  /// Only whitespace (or nothing) remains until end-of-line or end-of-file.
  private bool IsLineSeparator(int offset) {
    for (var i = _pos + offset; i < _source.Length; i++) {
      var ch = _source[i];
      if (ch == '\n' || ch == '\r') return true;
      if (ch != ' ' && ch != '\t') return false;
    }
    return true;
  }

  private static bool IsHexDigit(char c) =>
    (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
