#include "lexer.h"
#include <llvm/IR/Intrinsics.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Module.h>
#include <llvm/IR/LLVMContext.h>
#include <cctype>
#include <stdexcept>
#include <unordered_map>

// Forward declare runtime function helpers
namespace {
    llvm::Function* getOrDeclareSin(llvm::Module* module, llvm::LLVMContext& context);
    llvm::Function* getOrDeclareCos(llvm::Module* module, llvm::LLVMContext& context);
}

// Unified keyword information
static const std::unordered_map<std::string, KeywordData> keywords = {
    // Types
    {"int",       {KeywordCategory::Type,          "Integer type",        std::nullopt, [](llvm::LLVMContext& ctx) { return llvm::Type::getInt32Ty(ctx); }}},
    {"float",     {KeywordCategory::Type,          "Floating-point type", std::nullopt, [](llvm::LLVMContext& ctx) { return llvm::Type::getDoubleTy(ctx); }}},
    {"ptr",       {KeywordCategory::Type,          "Pointer type",        std::nullopt, [](llvm::LLVMContext& ctx) { return llvm::PointerType::get(ctx, 0); }}},
    {"char",      {KeywordCategory::Type,          "Character type",      std::nullopt, [](llvm::LLVMContext& ctx) { return llvm::Type::getInt8Ty(ctx); }}},
    {"string",    {KeywordCategory::Type,          "String type",         std::nullopt, [](llvm::LLVMContext& ctx) { return llvm::PointerType::get(ctx, 0); }}},
    {"bool",      {KeywordCategory::Type,          "Boolean type",        std::nullopt, [](llvm::LLVMContext& ctx) { return llvm::Type::getInt1Ty(ctx); }}},
    
    // Control flow
    {"if",        {KeywordCategory::ControlFlow,   "Conditional statement"}},
    {"else",      {KeywordCategory::ControlFlow,   "Alternative branch"}},
    {"while",     {KeywordCategory::ControlFlow,   "Loop statement"}},
    {"end",       {KeywordCategory::ControlFlow,   "Block terminator"}},
    {"return",    {KeywordCategory::ControlFlow,   "Return from function"}},
    {"break",     {KeywordCategory::ControlFlow,   "Exit loop"}},
    {"continue",  {KeywordCategory::ControlFlow,   "Skip to next iteration"}},
    
    // Declarations
    {"function",  {KeywordCategory::Declaration,   "Function declaration"}},
    {"var",       {KeywordCategory::Declaration,   "Mutable variable"}},
    {"let",       {KeywordCategory::Declaration,   "Immutable variable"}},
    {"struct",    {KeywordCategory::Declaration,   "Structure type"}},
    {"namespace", {KeywordCategory::Declaration,   "Namespace declaration"}},
    {"extern",    {KeywordCategory::Declaration,   "External declaration"}},
    
    // Math intrinsics (built into codegen)
    {"sqrt",      {KeywordCategory::MathIntrinsic, "Square root",         {{MathIntrinsicKind::LLVMIntrinsic,   llvm::Intrinsic::sqrt,  nullptr, "float"}}}},
    {"abs",       {KeywordCategory::MathIntrinsic, "Absolute value",      {{MathIntrinsicKind::LLVMIntrinsic,   llvm::Intrinsic::fabs,  nullptr, "float"}}}},
    {"floor",     {KeywordCategory::MathIntrinsic, "Floor function",      {{MathIntrinsicKind::LLVMIntrinsic,   llvm::Intrinsic::floor, nullptr, "int"}}}},
    {"ceil",      {KeywordCategory::MathIntrinsic, "Ceiling function",    {{MathIntrinsicKind::LLVMIntrinsic,   llvm::Intrinsic::ceil,  nullptr, "int"}}}},
    {"round",     {KeywordCategory::MathIntrinsic, "Round to nearest",    {{MathIntrinsicKind::LLVMIntrinsic,   llvm::Intrinsic::round, nullptr, "int"}}}},
    {"trunc",     {KeywordCategory::MathIntrinsic, "Truncate to integer", {{MathIntrinsicKind::DirectCast,      0,                      nullptr, "int"}}}},
    {"sin",       {KeywordCategory::MathIntrinsic, "Sine function",       {{MathIntrinsicKind::RuntimeFunction, 0,                      getOrDeclareSin, "float"}}}},
    {"cos",       {KeywordCategory::MathIntrinsic, "Cosine function",     {{MathIntrinsicKind::RuntimeFunction, 0,                      getOrDeclareCos, "float"}}}},
    
    // Literals
    {"true",      {KeywordCategory::Literal,       "Boolean true"}},
    {"false",     {KeywordCategory::Literal,       "Boolean false"}},
    
    // Operators
    {"as",        {KeywordCategory::Operator,      "Type cast operator"}}
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
        const KeywordData& data = pair.second;
        info.push_back({
            pair.first,                  // name
            data.category, // category
            data.description    // description
        });
    }
    
    return info;
}

std::vector<std::string> Lexer::getKeywordsByCategory(KeywordCategory category) {
    std::vector<std::string> result;
    for (const auto& pair : keywords) {
        if (pair.second.category == category) {
            result.push_back(pair.first);
        }
    }
    return result;
}

bool Lexer::isMathIntrinsic(const std::string& name) {
    auto it = keywords.find(name);
    return it != keywords.end() && it->second.category == KeywordCategory::MathIntrinsic;
}

const MathIntrinsicInfo* Lexer::getMathIntrinsicInfo(const std::string& name) {
    auto it = keywords.find(name);
    if (it != keywords.end() && it->second.category == KeywordCategory::MathIntrinsic) {
        return it->second.mathInfo.has_value() ? &it->second.mathInfo.value() : nullptr;
    }
    return nullptr;
}

bool Lexer::isTypeString(const std::string& name) {
    auto it = keywords.find(name);
    return it != keywords.end() && it->second.category == KeywordCategory::Type;
}

llvm::Type* Lexer::getLLVMTypeForKeyword(const std::string& name, llvm::LLVMContext& context) {
    auto it = keywords.find(name);
    if (it != keywords.end() && it->second.llvmTypeFactory.has_value()) {
        return it->second.llvmTypeFactory.value()(context);
    }
    return nullptr;
}

bool Lexer::isControlFlowToken(const Token& token) {
    return token.type == TokenType::KEYWORD && token.keywordData.has_value() && 
           token.keywordData.value().category == KeywordCategory::ControlFlow;
}

bool Lexer::isDeclarationToken(const Token& token) {
    return token.type == TokenType::KEYWORD && token.keywordData.has_value() && 
           token.keywordData.value().category == KeywordCategory::Declaration;
}

bool Lexer::isTypeToken(const Token& token) {
    return token.type == TokenType::KEYWORD && token.keywordData.has_value() && 
           token.keywordData.value().category == KeywordCategory::Type;
}

bool Lexer::isLiteralToken(const Token& token) {
    return token.type == TokenType::KEYWORD && token.keywordData.has_value() && 
           token.keywordData.value().category == KeywordCategory::Literal;
}

bool Lexer::isMathIntrinsicToken(const Token& token) {
    return token.type == TokenType::KEYWORD && token.keywordData.has_value() && 
           token.keywordData.value().category == KeywordCategory::MathIntrinsic;
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
        Token token(TokenType::KEYWORD, id, startLine, startColumn);
        token.keywordData = it->second;
        return token;
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

// Runtime function helpers (defined here to avoid circular dependency with codegen)
namespace {
    llvm::Function* getOrDeclareSin(llvm::Module* module, llvm::LLVMContext& context) {
        llvm::Function* sinFunc = module->getFunction("sin");
        if (!sinFunc) {
            llvm::FunctionType* sinFuncType = llvm::FunctionType::get(
                llvm::Type::getDoubleTy(context),
                {llvm::Type::getDoubleTy(context)},
                false
            );
            sinFunc = llvm::Function::Create(
                sinFuncType,
                llvm::Function::ExternalLinkage,
                "sin",
                module
            );
        }
        return sinFunc;
    }

    llvm::Function* getOrDeclareCos(llvm::Module* module, llvm::LLVMContext& context) {
        llvm::Function* cosFunc = module->getFunction("cos");
        if (!cosFunc) {
            llvm::FunctionType* cosFuncType = llvm::FunctionType::get(
                llvm::Type::getDoubleTy(context),
                {llvm::Type::getDoubleTy(context)},
                false
            );
            cosFunc = llvm::Function::Create(
                cosFuncType,
                llvm::Function::ExternalLinkage,
                "cos",
                module
            );
        }
        return cosFunc;
    }
}
