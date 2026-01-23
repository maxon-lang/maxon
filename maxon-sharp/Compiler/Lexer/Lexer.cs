namespace MaxonSharp.Lexer;

public class Lexer
{
    private readonly string _source;
    private int _pos;
    private int _line = 1;
    private int _column = 1;

    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "function", TokenType.Function },
        { "returns", TokenType.Returns },
        { "return", TokenType.Return },
        { "end", TokenType.End },
        { "int", TokenType.Int }
    };

    public Lexer(string source)
    {
        _source = source;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (!IsAtEnd())
        {
            var token = NextToken();
            if (token != null)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new Token(TokenType.Eof, "", _line, _column));
        return tokens;
    }

    private Token? NextToken()
    {
        // Skip whitespace (but not newlines)
        while (!IsAtEnd() && (Current() == ' ' || Current() == '\t' || Current() == '\r'))
        {
            Advance();
        }

        if (IsAtEnd())
        {
            return null;
        }

        var startLine = _line;
        var startColumn = _column;
        var c = Current();

        // Newline
        if (c == '\n')
        {
            Advance();
            _line++;
            _column = 1;
            return new Token(TokenType.Newline, "\n", startLine, startColumn);
        }

        // Character literal (for end label like 'main')
        if (c == '\'')
        {
            return ScanCharacterLiteral(startLine, startColumn);
        }

        // Delimiters
        if (c == '(')
        {
            Advance();
            return new Token(TokenType.LeftParen, "(", startLine, startColumn);
        }
        if (c == ')')
        {
            Advance();
            return new Token(TokenType.RightParen, ")", startLine, startColumn);
        }

        // Integer literal
        if (char.IsDigit(c))
        {
            return ScanNumber(startLine, startColumn);
        }

        // Identifier or keyword
        if (char.IsLetter(c) || c == '_')
        {
            return ScanIdentifier(startLine, startColumn);
        }

        // Unknown
        Advance();
        return new Token(TokenType.Unknown, c.ToString(), startLine, startColumn);
    }

    private Token ScanNumber(int startLine, int startColumn)
    {
        var start = _pos;
        while (!IsAtEnd() && char.IsDigit(Current()))
        {
            Advance();
        }
        var value = _source[start.._pos];
        return new Token(TokenType.IntegerLiteral, value, startLine, startColumn);
    }

    private Token ScanIdentifier(int startLine, int startColumn)
    {
        var start = _pos;
        while (!IsAtEnd() && (char.IsLetterOrDigit(Current()) || Current() == '_'))
        {
            Advance();
        }
        var value = _source[start.._pos];

        var type = Keywords.TryGetValue(value, out var keyword) ? keyword : TokenType.Identifier;
        return new Token(type, value, startLine, startColumn);
    }

    private Token ScanCharacterLiteral(int startLine, int startColumn)
    {
        Advance(); // consume opening quote
        var start = _pos;
        while (!IsAtEnd() && Current() != '\'')
        {
            Advance();
        }
        var value = _source[start.._pos];
        if (!IsAtEnd())
        {
            Advance(); // consume closing quote
        }
        return new Token(TokenType.CharacterLiteral, value, startLine, startColumn);
    }

    private char Current() => _source[_pos];

    private void Advance()
    {
        _pos++;
        _column++;
    }

    private bool IsAtEnd() => _pos >= _source.Length;
}
