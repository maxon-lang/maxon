#include "lexer.h"
#include <cctype>
#include <stdexcept>
#include <unordered_map>

// Unified keyword information
static const std::unordered_map<std::string, Token> keywords = {
    // Types
    {"int",       Token(TokenType::INT,        "int",        0, 0, KeywordCategory::Type,          "Integer type")},
    {"float",     Token(TokenType::FLOAT,      "float",      0, 0, KeywordCategory::Type,          "Floating-point type")},
    {"ptr",       Token(TokenType::PTR,        "ptr",        0, 0, KeywordCategory::Type,          "Pointer type")},
    {"char",      Token(TokenType::CHAR,       "char",       0, 0, KeywordCategory::Type,          "Character type")},
    {"string",    Token(TokenType::STRING_TYPE,"string",     0, 0, KeywordCategory::Type,          "String type")},
    {"bool",      Token(TokenType::BOOL,       "bool",       0, 0, KeywordCategory::Type,          "Boolean type")},
    
    // Control flow
    {"if",        Token(TokenType::IF,         "if",         0, 0, KeywordCategory::ControlFlow,   "Conditional statement")},
    {"else",      Token(TokenType::ELSE,       "else",       0, 0, KeywordCategory::ControlFlow,   "Alternative branch")},
    {"while",     Token(TokenType::WHILE,      "while",      0, 0, KeywordCategory::ControlFlow,   "Loop statement")},
    {"end",       Token(TokenType::END,        "end",        0, 0, KeywordCategory::ControlFlow,   "Block terminator")},
    {"return",    Token(TokenType::RETURN,     "return",     0, 0, KeywordCategory::ControlFlow,   "Return from function")},
    {"break",     Token(TokenType::BREAK,      "break",      0, 0, KeywordCategory::ControlFlow,   "Exit loop")},
    {"continue",  Token(TokenType::CONTINUE,   "continue",   0, 0, KeywordCategory::ControlFlow,   "Skip to next iteration")},
    
    // Declarations
    {"function",  Token(TokenType::FUNCTION,   "function",   0, 0, KeywordCategory::Declaration,   "Function declaration")},
    {"var",       Token(TokenType::VAR,        "var",        0, 0, KeywordCategory::Declaration,   "Mutable variable")},
    {"let",       Token(TokenType::LET,        "let",        0, 0, KeywordCategory::Declaration,   "Immutable variable")},
    {"struct",    Token(TokenType::STRUCT,     "struct",     0, 0, KeywordCategory::Declaration,   "Structure type")},
    {"namespace", Token(TokenType::NAMESPACE,  "namespace",  0, 0, KeywordCategory::Declaration,   "Namespace declaration")},
    {"extern",    Token(TokenType::EXTERN,     "extern",     0, 0, KeywordCategory::Declaration,   "External declaration")},
    
    // Math intrinsics (built into codegen)
    {"sqrt",      Token(TokenType::SQRT,       "sqrt",       0, 0, KeywordCategory::MathIntrinsic, "Square root")},
    {"abs",       Token(TokenType::ABS,        "abs",        0, 0, KeywordCategory::MathIntrinsic, "Absolute value")},
    {"floor",     Token(TokenType::FLOOR,      "floor",      0, 0, KeywordCategory::MathIntrinsic, "Floor function")},
    {"ceil",      Token(TokenType::CEIL,       "ceil",       0, 0, KeywordCategory::MathIntrinsic, "Ceiling function")},
    {"round",     Token(TokenType::ROUND,      "round",      0, 0, KeywordCategory::MathIntrinsic, "Round to nearest")},
    {"trunc",     Token(TokenType::TRUNC,      "trunc",      0, 0, KeywordCategory::MathIntrinsic, "Truncate to integer")},
    {"sin",       Token(TokenType::SIN,        "sin",        0, 0, KeywordCategory::MathIntrinsic, "Sine function")},
    {"cos",       Token(TokenType::COS,        "cos",        0, 0, KeywordCategory::MathIntrinsic, "Cosine function")},
    
    // Literals
    {"true",      Token(TokenType::TRUE,       "true",       0, 0, KeywordCategory::Literal,       "Boolean true")},
    {"false",     Token(TokenType::FALSE,      "false",      0, 0, KeywordCategory::Literal,       "Boolean false")},
    
    // Operators
    {"as",        Token(TokenType::AS,         "as",         0, 0, KeywordCategory::Operator,      "Type cast operator")}
};

Lexer::Lexer(const std::string& src)
    : source(src), position(0), line(1), column(1) {}

std::vector<std::string> Lexer::getKeywords() {
    std::vector<std::string> keywordList;
    keywordList.reserve(keywords.size());
    for (const auto& pair : keywords) {
        keywordList.push_back(pair.first);
    }
    return keywordList;
}

std::vector<Lexer::KeywordInfo> Lexer::getKeywordInfo() {
    std::vector<KeywordInfo> info;
    
    for (const auto& pair : keywords) {
        const Token& token = pair.second;
        if (token.keywordCategory.has_value() && token.description.has_value()) {
            info.push_back({
                pair.first,                  // name
                token.keywordCategory.value(), // category
                token.description.value()    // description
            });
        }
    }
    
    return info;
}

std::vector<std::string> Lexer::getKeywordsByCategory(KeywordCategory category) {
    std::vector<std::string> result;
    for (const auto& pair : keywords) {
        if (pair.second.keywordCategory == category) {
            result.push_back(pair.first);
        }
    }
    return result;
}

char Lexer::currentChar() {
    if (position >= source.length()) {
        return '\0';
    }
    return source[position];
}

char Lexer::peek(int offset) {
    size_t pos = position + offset;
    if (pos >= source.length()) {
        return '\0';
    }
    return source[pos];
}

void Lexer::advance() {
    if (position < source.length()) {
        if (source[position] == '\n') {
            line++;
            column = 1;
        } else {
            column++;
        }
        position++;
    }
}

void Lexer::skipWhitespace() {
    while (std::isspace(currentChar())) {
        advance();
    }
}

void Lexer::skipComment() {
    // Handle // single-line comments
    if (currentChar() == '/' && peek(1) == '/') {
        advance(); // skip first /
        advance(); // skip second /
        while (currentChar() != '\0' && currentChar() != '\n') {
            advance();
        }
        return;
    }
    
    // Handle /* multi-line comments */
    if (currentChar() == '/' && peek(1) == '*') {
        advance(); // skip /
        advance(); // skip *
        while (currentChar() != '\0') {
            if (currentChar() == '*' && peek(1) == '/') {
                advance(); // skip *
                advance(); // skip /
                break;
            }
            advance();
        }
        return;
    }
}

Token Lexer::readNumber() {
    int startLine = line;
    int startColumn = column;
    std::string num;
    bool isFloat = false;
    
    // Read integer part
    while (std::isdigit(currentChar())) {
        num += currentChar();
        advance();
    }
    
    // Check for decimal point
    if (currentChar() == '.' && std::isdigit(peek(1))) {
        isFloat = true;
        num += currentChar();
        advance();
        
        // Read fractional part
        while (std::isdigit(currentChar())) {
            num += currentChar();
            advance();
        }
    }
    
    // Check for scientific notation (e or E)
    if (currentChar() == 'e' || currentChar() == 'E') {
        isFloat = true;
        num += currentChar();
        advance();
        
        // Optional sign
        if (currentChar() == '+' || currentChar() == '-') {
            num += currentChar();
            advance();
        }
        
        // Read exponent
        if (!std::isdigit(currentChar())) {
            throw std::runtime_error("Invalid scientific notation at line " + 
                                   std::to_string(startLine) + ", column " + 
                                   std::to_string(startColumn));
        }
        
        while (std::isdigit(currentChar())) {
            num += currentChar();
            advance();
        }
    }
    
    if (isFloat) {
        return Token(TokenType::FLOAT_LITERAL, num, startLine, startColumn);
    }
    
    return Token(TokenType::NUMBER, num, startLine, startColumn);
}

Token Lexer::readIdentifier() {
    int startLine = line;
    int startColumn = column;
    std::string id;
    
    while (std::isalnum(currentChar()) || currentChar() == '_') {
        id += currentChar();
        advance();
    }
    
    // Check if it's a keyword
    auto it = keywords.find(id);
    if (it != keywords.end()) {
        return Token(it->second.type, id, startLine, startColumn, it->second.keywordCategory, it->second.description);
    }
    
    return Token(TokenType::IDENTIFIER, id, startLine, startColumn);
}

Token Lexer::readString() {
    int startLine = line;
    int startColumn = column;
    std::string str;
    
    advance(); // Skip opening '
    
    while (currentChar() != '\0' && currentChar() != '\'') {
        if (currentChar() == '\n') {
            throw std::runtime_error("Unterminated string literal at line " + 
                                   std::to_string(startLine) + ", column " + 
                                   std::to_string(startColumn) + 
                                   ": string started with ' but missing closing '");
        }
        str += currentChar();
        advance();
    }
    
    if (currentChar() == '\0') {
        throw std::runtime_error("Unterminated string literal at line " + 
                               std::to_string(startLine) + ", column " + 
                               std::to_string(startColumn) + 
                               ": reached end of file without finding closing '");
    }
    
    if (currentChar() == '\'') {
        advance(); // Skip closing '
    }
    
    // Check if this is a character literal (single character between quotes)
    if (str.length() == 1) {
        // Return as CHARACTER token
        return Token(TokenType::CHARACTER, str, startLine, startColumn);
    }
    
    // Multi-character single-quoted string is a block identifier
    return Token(TokenType::BLOCK_ID, str, startLine, startColumn);
}

Token Lexer::readStringLiteral() {
    int startLine = line;
    int startColumn = column;
    std::string str;
    
    advance(); // Skip opening "
    
    while (currentChar() != '\0' && currentChar() != '"') {
        if (currentChar() == '\\') {
            // Handle escape sequences
            advance();
            if (currentChar() == '\0') {
                throw std::runtime_error("Unterminated string literal at line " + 
                                       std::to_string(startLine) + ", column " + 
                                       std::to_string(startColumn) + 
                                       ": reached end of file in escape sequence");
            }
            switch (currentChar()) {
                case 'n':  str += '\n'; break;
                case 't':  str += '\t'; break;
                case 'r':  str += '\r'; break;
                case '\\': str += '\\'; break;
                case '"':  str += '"'; break;
                case '0':  str += '\0'; break;
                default:
                    throw std::runtime_error("Unknown escape sequence '\\" + 
                                           std::string(1, currentChar()) + "' at line " + 
                                           std::to_string(line) + ", column " + 
                                           std::to_string(column));
            }
            advance();
        } else {
            str += currentChar();
            advance();
        }
    }
    
    if (currentChar() == '\0') {
        throw std::runtime_error("Unterminated string literal at line " + 
                               std::to_string(startLine) + ", column " + 
                               std::to_string(startColumn) + 
                               ": reached end of file without finding closing \"");
    }
    
    advance(); // Skip closing "
    
    return Token(TokenType::STRING, str, startLine, startColumn);
}

std::vector<Token> Lexer::tokenize() {
    std::vector<Token> tokens;
    
    while (currentChar() != '\0') {
        skipWhitespace();
        
        // Skip comments
        if (currentChar() == '/' && (peek(1) == '/' || peek(1) == '*')) {
            skipComment();
            continue;
        }
        
        if (currentChar() == '\0') {
            break;
        }
        
        int startLine = line;
        int startColumn = column;
        char c = currentChar();
        
        // Single quote - block identifier
        if (c == '\'') {
            tokens.push_back(readString());
        }
        // Numbers
        else if (std::isdigit(c)) {
            tokens.push_back(readNumber());
        }
        // Identifiers and keywords
        else if (std::isalpha(c) || c == '_') {
            tokens.push_back(readIdentifier());
        }
        // Operators and delimiters
        else if (c == '+') {
            tokens.push_back(Token(TokenType::PLUS, "+", startLine, startColumn));
            advance();
        }
        else if (c == '-') {
            tokens.push_back(Token(TokenType::MINUS, "-", startLine, startColumn));
            advance();
        }
        else if (c == '*') {
            tokens.push_back(Token(TokenType::MULTIPLY, "*", startLine, startColumn));
            advance();
        }
        else if (c == '/') {
            tokens.push_back(Token(TokenType::DIVIDE, "/", startLine, startColumn));
            advance();
        }
        else if (c == '%') {
            tokens.push_back(Token(TokenType::MODULO, "%", startLine, startColumn));
            advance();
        }
        else if (c == '&') {
            tokens.push_back(Token(TokenType::AMPERSAND, "&", startLine, startColumn));
            advance();
        }
        else if (c == '=') {
            tokens.push_back(Token(TokenType::EQUALS, "=", startLine, startColumn));
            advance();
        }
        else if (c == '!') {
            advance();
            if (currentChar() == '=') {
                tokens.push_back(Token(TokenType::NOT_EQUAL, "!=", startLine, startColumn));
                advance();
            } else {
                throw std::runtime_error("Unexpected character '!' at line " + 
                                       std::to_string(startLine) + ", column " + 
                                       std::to_string(startColumn) + 
                                       ": did you mean '!=' (not equal)?");
            }
        }
        else if (c == '>') {
            advance();
            if (currentChar() == '=') {
                tokens.push_back(Token(TokenType::GTE, ">=", startLine, startColumn));
                advance();
            } else {
                tokens.push_back(Token(TokenType::GT, ">", startLine, startColumn));
            }
        }
        else if (c == '<') {
            advance();
            if (currentChar() == '=') {
                tokens.push_back(Token(TokenType::LTE, "<=", startLine, startColumn));
                advance();
            } else {
                tokens.push_back(Token(TokenType::LT, "<", startLine, startColumn));
            }
        }
        else if (c == '(') {
            tokens.push_back(Token(TokenType::LPAREN, "(", startLine, startColumn));
            advance();
        }
        else if (c == ')') {
            tokens.push_back(Token(TokenType::RPAREN, ")", startLine, startColumn));
            advance();
        }
        else if (c == '[') {
            tokens.push_back(Token(TokenType::LBRACKET, "[", startLine, startColumn));
            advance();
        }
        else if (c == ']') {
            tokens.push_back(Token(TokenType::RBRACKET, "]", startLine, startColumn));
            advance();
        }
        else if (c == '{') {
            tokens.push_back(Token(TokenType::LBRACE, "{", startLine, startColumn));
            advance();
        }
        else if (c == '}') {
            tokens.push_back(Token(TokenType::RBRACE, "}", startLine, startColumn));
            advance();
        }
        else if (c == ',') {
            tokens.push_back(Token(TokenType::COMMA, ",", startLine, startColumn));
            advance();
        }
        else if (c == ':') {
            tokens.push_back(Token(TokenType::COLON, ":", startLine, startColumn));
            advance();
        }
        else if (c == '.') {
            // Check if this is attempting to be a float literal without leading zero (e.g., .5)
            if (std::isdigit(peek(1))) {
                throw std::runtime_error("Invalid float literal at line " + 
                                       std::to_string(startLine) + ", column " + 
                                       std::to_string(startColumn) + 
                                       ": float literals must have a leading zero (use 0" + 
                                       std::string(1, c) + std::string(1, peek(1)) + " instead of " + 
                                       std::string(1, c) + std::string(1, peek(1)) + ")");
            }
            tokens.push_back(Token(TokenType::DOT, ".", startLine, startColumn));
            advance();
        }
        else {
            // Unknown character - provide helpful error
            std::string charDesc;
            if (std::isprint(c)) {
                charDesc = "'" + std::string(1, c) + "'";
            } else {
                charDesc = "(ASCII " + std::to_string((int)c) + ")";
            }
            
            std::string suggestion;
            if (c == ';') {
                suggestion = "\n  Note: Maxon doesn't use semicolons at the end of statements";
            } else if (c == '[' || c == ']') {
                suggestion = "\n  Note: Arrays are not yet supported in Maxon";
            } else if (c == '"') {
                // Double quotes are for string literals - handle them
                tokens.push_back(readStringLiteral());
                continue;
            }
            
            throw std::runtime_error("Unexpected character " + charDesc + " at line " + 
                                   std::to_string(startLine) + ", column " + 
                                   std::to_string(startColumn) + suggestion);
        }
    }
    
    tokens.push_back(Token(TokenType::END_OF_FILE, "", line, column));
    return tokens;
}
