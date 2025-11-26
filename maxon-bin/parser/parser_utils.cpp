#include "../parser.h"
#include <stdexcept>

Token& Parser::currentToken() {
    if (position >= tokens.size()) {
        return tokens.back();
    }
    return tokens[position];
}

Token& Parser::peek(int offset) {
    size_t pos = position + offset;
    if (pos >= tokens.size()) {
        return tokens.back();
    }
    return tokens[pos];
}

bool Parser::match(TokenType type) {
    if (check(type)) {
        advance();
        return true;
    }
    return false;
}

bool Parser::check(TokenType type) {
    return currentToken().type == type;
}

bool Parser::check(TokenType type, int offset) {
    if (position + offset < tokens.size()) {
        return tokens[position + offset].type == type;
    }
    return false;
}

void Parser::advance() {
    if (position < tokens.size()) {
        position++;
    }
}

Token Parser::expect(TokenType type, const std::string& message) {
    if (!check(type)) {
        std::string typeStr;
        switch (type) {
            case TokenType::IDENTIFIER: typeStr = "identifier"; break;
            case TokenType::NUMBER: typeStr = "number"; break;
            case TokenType::STRING: typeStr = "string literal"; break;
            case TokenType::BLOCK_ID: typeStr = "block identifier"; break;
            case TokenType::CHARACTER: typeStr = "character literal"; break;
            case TokenType::LPAREN: typeStr = "'('"; break;
            case TokenType::RPAREN: typeStr = "')'"; break;
            case TokenType::LBRACKET: typeStr = "'['"; break;
            case TokenType::RBRACKET: typeStr = "']'"; break;
            case TokenType::ASSIGN: typeStr = "'='"; break;
            case TokenType::EQUAL_EQUAL: typeStr = "'=='"; break;
            case TokenType::COMMA: typeStr = "','"; break;
            case TokenType::KEYWORD: typeStr = "'" + currentToken().value + "'"; break;
            default: typeStr = "token";
        }
        
        std::string foundStr;
        if (currentToken().type == TokenType::END_OF_FILE) {
            foundStr = "end of file";
        } else {
            foundStr = "'" + currentToken().value + "'";
        }
        
        throw std::runtime_error(message + "\n  Expected: " + typeStr + 
                               "\n  Found: " + foundStr +
                               "\n  Location: line " + std::to_string(currentToken().line) + 
                               ", column " + std::to_string(currentToken().column));
    }
    Token tok = currentToken();
    advance();
    return tok;
}

Token Parser::expectKeyword(const std::string& keyword, const std::string& message) {
    if (!check(TokenType::KEYWORD) || currentToken().value != keyword) {
        throw std::runtime_error(message + " at line " + std::to_string(currentToken().line) + 
                               ", column " + std::to_string(currentToken().column));
    }
    Token tok = currentToken();
    advance();
    return tok;
}

// Parse a qualified name (dotted identifier like 'iter.Iterator')
std::string Parser::parseQualifiedName(const std::string& context) {
    Token firstToken = expect(TokenType::IDENTIFIER, "Expected " + context);
    std::string qualifiedName = firstToken.value;
    
    // Parse additional dotted components
    while (check(TokenType::DOT)) {
        advance(); // consume '.'
        Token nextToken = expect(TokenType::IDENTIFIER, "Expected identifier after '.' in " + context);
        qualifiedName += "." + nextToken.value;
    }
    
    return qualifiedName;
}
