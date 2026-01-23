namespace MaxonSharp.Lexer;

public enum TokenType
{
    // Keywords
    Function,
    Returns,
    Return,
    End,
    Int,

    // Literals
    Identifier,
    IntegerLiteral,
    CharacterLiteral,

    // Delimiters
    LeftParen,
    RightParen,
    Newline,
    Eof,

    // Special
    Unknown
}
