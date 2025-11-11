#ifndef LEXER_H
#define LEXER_H

#include <string>
#include <vector>

enum class TokenType {
    // Keywords
    FUNCTION,
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
    TRUE,       // true keyword
    FALSE,      // false keyword
    
    // Identifiers and literals
    IDENTIFIER,
    NUMBER,
    STRING,
    
    // Operators
    ASSIGN,     // =
    PLUS,       // +
    MINUS,      // -
    MULTIPLY,   // *
    DIVIDE,     // /
    EQUAL_EQUAL,// == (equality comparison)
    NOT_EQUAL,  // != (not equal)
    GT,         // >
    LT,         // <
    GTE,        // >=
    LTE,        // <=
    
    // Delimiters
    LPAREN,     // (
    RPAREN,     // )
    COMMA,      // ,
    
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
    
public:
    Lexer(const std::string& src);
    std::vector<Token> tokenize();
};

#endif // LEXER_H
