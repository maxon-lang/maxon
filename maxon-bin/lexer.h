#ifndef LEXER_H
#define LEXER_H

#include <string>
#include <vector>
#include <optional>

enum class TokenType {
    // Keywords
    FUNCTION,
    EXTERN,     // extern keyword for external declarations
    NAMESPACE,  // namespace keyword
    STRUCT,     // struct keyword
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
    FLOAT,      // float type keyword
    PTR,        // ptr type keyword
    CHAR,       // char type keyword
    STRING_TYPE,  // string type keyword (alias for ptr, used for strings)
    BOOL,       // bool type keyword
    AS,         // as keyword for type casting
    TRUE,       // true keyword
    FALSE,      // false keyword
    
    // Math intrinsic functions (keywords, built into codegen)
    SQRT,       // sqrt keyword
    ABS,        // abs keyword
    FLOOR,      // floor keyword
    CEIL,       // ceil keyword
    ROUND,      // round keyword
    TRUNC,      // trunc keyword
    SIN,        // sin keyword
    COS,        // cos keyword
    
    // Identifiers and literals
    IDENTIFIER,
    NUMBER,
    FLOAT_LITERAL, // Floating-point literal
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
    LBRACE,     // {
    RBRACE,     // }
    COMMA,      // ,
    COLON,      // :
    DOT,        // . (member access / namespace resolution)
    
    // Special
    END_OF_FILE,
    UNKNOWN
};

// Keyword category for metadata
enum class KeywordCategory {
    Type,           // int, float, ptr, char, string
    ControlFlow,    // if, else, while, end, return, break, continue
    Declaration,    // function, var, let, struct, namespace, extern
    MathIntrinsic,  // sqrt, abs, floor, ceil, round, trunc, sin, cos
    Literal,        // true, false
    Operator        // as
};

struct KeywordData {
    TokenType type;
    KeywordCategory category;
    std::string description;
};

struct Token {
    TokenType type;
    std::string value;
    int line;
    int column;
    std::optional<KeywordData> keywordData;  // Contains category and description for keyword tokens
    
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
    
    struct KeywordInfo {
        std::string name;
        KeywordCategory category;
        std::string description;
    };
    
    // Get all keyword strings (for IDE/LSP features)
    static std::vector<std::string> getKeywords();
    
    // Get keywords with metadata
    static std::vector<KeywordInfo> getKeywordInfo();
    
    // Get keywords by category
    static std::vector<std::string> getKeywordsByCategory(KeywordCategory category);
};

#endif // LEXER_H
