#ifndef LEXER_H
#define LEXER_H

#include <string>
#include <vector>

enum class TokenType {
    // Keywords
    FUNCTION,
    EXTERN,     // extern keyword for external declarations
    NAMESPACE,  // namespace keyword
    VAR,
    LET,        // let keyword (immutable variables)
    WHILE,
    IF,
    ELSE,
    END,
    RETURN,
    BREAK,      // break keyword
    CONTINUE,   // continue keyword
    INT,
    PTR,        // ptr type keyword
    CHAR,       // char type keyword
    AS,         // as keyword for type casting
    TRUE,       // true keyword
    FALSE,      // false keyword
    
    // Identifiers and literals
    IDENTIFIER,
    NUMBER,
    STRING,       // Double-quoted string literals
    BLOCK_ID,     // Single-quoted block identifiers  
    CHARACTER,    // Single character literal 'A'
    
    // Operators
    EQUALS,     // = (used for both assignment and equality comparison)
    PLUS,       // +
    MINUS,      // -
    MULTIPLY,   // *
    DIVIDE,     // /
    MODULO,     // % (modulo/remainder)
    AMPERSAND,  // & (address-of operator)
    NOT_EQUAL,  // != (not equal)
    GT,         // >
    LT,         // <
    GTE,        // >=
    LTE,        // <=
    
    // Delimiters
    LPAREN,     // (
    RPAREN,     // )
    LBRACKET,   // [
    RBRACKET,   // ]
    COMMA,      // ,
    DOT,        // . (member access / namespace resolution)
    
    // Special
    END_OF_FILE,
    UNKNOWN
};

struct Token {
    TokenType type;
    std::string value;
    int line;
    int column;
    
    Token(TokenType t, const std::string& v, int l, int c)
        : type(t), value(v), line(l), column(c) {}
};

class Lexer {
private:
    std::string source;
    size_t position;
    int line;
    int column;
    
    char currentChar();
    char peek(int offset = 1);
    void advance();
    void skipWhitespace();
    void skipComment();
    Token readNumber();
    Token readIdentifier();
    Token readString();
    Token readStringLiteral();  // For double-quoted strings
    
public:
    Lexer(const std::string& src);
    std::vector<Token> tokenize();
};

#endif // LEXER_H
