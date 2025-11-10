#include "lexer.h"
#include <cctype>
#include <stdexcept>
#include <unordered_map>

static const std::unordered_map<std::string, TokenType> keywords = {
    {"function", TokenType::FUNCTION},
    {"var", TokenType::VAR},
    {"while", TokenType::WHILE},
    {"if", TokenType::IF},
    {"else", TokenType::ELSE},
    {"end", TokenType::END},
    {"return", TokenType::RETURN},
    {"int", TokenType::INT}
};

Lexer::Lexer(const std::string& src)
    : source(src), position(0), line(1), column(1) {}

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
    // Comments in Maxon are strings like 'comment text'
    // They're just string literals that we'll ignore at the statement level
}

Token Lexer::readNumber() {
    int startLine = line;
    int startColumn = column;
    std::string num;
    
    while (std::isdigit(currentChar())) {
        num += currentChar();
        advance();
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
        return Token(it->second, id, startLine, startColumn);
    }
    
    return Token(TokenType::IDENTIFIER, id, startLine, startColumn);
}

Token Lexer::readString() {
    int startLine = line;
    int startColumn = column;
    std::string str;
    
    advance(); // Skip opening '
    
    while (currentChar() != '\0' && currentChar() != '\'') {
        str += currentChar();
        advance();
    }
    
    if (currentChar() == '\'') {
        advance(); // Skip closing '
    }
    
    return Token(TokenType::STRING, str, startLine, startColumn);
}

std::vector<Token> Lexer::tokenize() {
    std::vector<Token> tokens;
    
    while (currentChar() != '\0') {
        skipWhitespace();
        
        if (currentChar() == '\0') {
            break;
        }
        
        int startLine = line;
        int startColumn = column;
        char c = currentChar();
        
        // Single quote - string/comment
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
        else if (c == '=') {
            tokens.push_back(Token(TokenType::ASSIGN, "=", startLine, startColumn));
            advance();
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
        else {
            // Unknown character, skip it
            advance();
        }
    }
    
    tokens.push_back(Token(TokenType::END_OF_FILE, "", line, column));
    return tokens;
}
