#ifndef LEXER_H
#define LEXER_H

#include <string>
#include <vector>
#include <optional>
#include <functional>

// Forward declarations for LLVM types
namespace llvm {
    class Function;
    class Module;
    class LLVMContext;
    namespace Intrinsic {
        typedef unsigned ID;
    }
}

enum class TokenType {
    // Keywords (all keywords now use KEYWORD)
    KEYWORD,
    
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

// How a math intrinsic is implemented
enum class MathIntrinsicKind {
    LLVMIntrinsic,   // Use LLVM intrinsic (sqrt, abs, floor, ceil, round)
    RuntimeFunction, // Call runtime library function (sin, cos)
    DirectCast       // Direct IR operation (trunc)
};

// Math intrinsic metadata for code generation
struct MathIntrinsicInfo {
    MathIntrinsicKind kind;
    llvm::Intrinsic::ID intrinsicID;     // For LLVMIntrinsic kind
    std::function<llvm::Function*(llvm::Module*, llvm::LLVMContext&)> getRuntimeFn;  // For RuntimeFunction kind
    std::string returnType;              // "int" or "float"
};

// Forward declarations for LLVM types
namespace llvm {
    class Type;
    class LLVMContext;
}

struct KeywordData {
    KeywordCategory category;
    std::string description;
    std::optional<MathIntrinsicInfo> mathInfo;  // Only set for MathIntrinsic category
    std::optional<std::function<llvm::Type*(llvm::LLVMContext&)>> llvmTypeFactory;  // Only set for Type category
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
    
    // Check if a name is a math intrinsic
    static bool isMathIntrinsic(const std::string& name);
    
    // Get math intrinsic info for code generation (returns nullptr if not a math intrinsic)
    static const MathIntrinsicInfo* getMathIntrinsicInfo(const std::string& name);
    
    // Check if a string is a valid type keyword
    static bool isTypeString(const std::string& name);
    
    // Get LLVM type for a type keyword (returns nullptr if not a type keyword)
    static llvm::Type* getLLVMTypeForKeyword(const std::string& name, llvm::LLVMContext& context);
    
    // Check if a token is a keyword of specific category
    static bool isControlFlowToken(const Token& token);
    static bool isDeclarationToken(const Token& token);
    static bool isTypeToken(const Token& token);
    static bool isLiteralToken(const Token& token);
    static bool isMathIntrinsicToken(const Token& token);
};

#endif // LEXER_H
