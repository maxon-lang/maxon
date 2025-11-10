#include "parser.h"
#include <stdexcept>

Parser::Parser(const std::vector<Token>& toks)
    : tokens(toks), position(0) {}

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

void Parser::advance() {
    if (position < tokens.size()) {
        position++;
    }
}

Token Parser::expect(TokenType type, const std::string& message) {
    if (!check(type)) {
        throw std::runtime_error(message + " at line " + 
                                std::to_string(currentToken().line));
    }
    Token tok = currentToken();
    advance();
    return tok;
}

std::unique_ptr<ExprAST> Parser::parsePrimary() {
    if (check(TokenType::NUMBER)) {
        int value = std::stoi(currentToken().value);
        advance();
        return std::make_unique<NumberExprAST>(value);
    }
    
    if (check(TokenType::IDENTIFIER)) {
        std::string name = currentToken().value;
        advance();
        return std::make_unique<VariableExprAST>(name);
    }
    
    if (match(TokenType::LPAREN)) {
        auto expr = parseExpression();
        expect(TokenType::RPAREN, "Expected ')'");
        return expr;
    }
    
    throw std::runtime_error("Expected expression at line " + 
                            std::to_string(currentToken().line));
}

std::unique_ptr<ExprAST> Parser::parseFactor() {
    return parsePrimary();
}

std::unique_ptr<ExprAST> Parser::parseTerm() {
    auto left = parseFactor();
    
    while (check(TokenType::MULTIPLY) || check(TokenType::DIVIDE)) {
        char op = currentToken().value[0];
        advance();
        auto right = parseFactor();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right));
    }
    
    return left;
}

std::unique_ptr<ExprAST> Parser::parseComparison() {
    auto left = parseTerm();
    
    while (check(TokenType::PLUS) || check(TokenType::MINUS)) {
        char op = currentToken().value[0];
        advance();
        auto right = parseTerm();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right));
    }
    
    return left;
}

std::unique_ptr<ExprAST> Parser::parseExpression() {
    auto left = parseComparison();
    
    // Handle comparison operators
    if (check(TokenType::GT) || check(TokenType::LT) || 
        check(TokenType::GTE) || check(TokenType::LTE) ||
        check(TokenType::ASSIGN)) {
        
        char op;
        TokenType type = currentToken().type;
        
        if (type == TokenType::GT) op = '>';
        else if (type == TokenType::LT) op = '<';
        else if (type == TokenType::GTE) op = 'G'; // >=
        else if (type == TokenType::LTE) op = 'L'; // <=
        else if (type == TokenType::ASSIGN) op = '='; // equality
        
        advance();
        auto right = parseComparison();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right));
    }
    
    return left;
}

std::unique_ptr<VarDeclStmtAST> Parser::parseVarDecl() {
    expect(TokenType::VAR, "Expected 'var'");
    Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
    expect(TokenType::ASSIGN, "Expected '='");
    auto initializer = parseExpression();
    
    return std::make_unique<VarDeclStmtAST>(name.value, std::move(initializer));
}

std::unique_ptr<AssignStmtAST> Parser::parseAssignment(const std::string& name) {
    expect(TokenType::ASSIGN, "Expected '='");
    auto value = parseExpression();
    return std::make_unique<AssignStmtAST>(name, std::move(value));
}

std::unique_ptr<ReturnStmtAST> Parser::parseReturn() {
    expect(TokenType::RETURN, "Expected 'return'");
    auto value = parseExpression();
    return std::make_unique<ReturnStmtAST>(std::move(value));
}

std::unique_ptr<IfStmtAST> Parser::parseIf() {
    expect(TokenType::IF, "Expected 'if'");
    auto condition = parseExpression();
    
    // Skip optional comment string
    if (check(TokenType::STRING)) {
        advance();
    }
    
    std::vector<std::unique_ptr<StmtAST>> thenBody;
    std::vector<std::unique_ptr<StmtAST>> elseBody;
    
    // Parse then body
    while (!check(TokenType::ELSE) && !check(TokenType::END) && 
           !check(TokenType::END_OF_FILE)) {
        thenBody.push_back(parseStatement());
    }
    
    // Parse optional else
    if (match(TokenType::ELSE)) {
        // Skip optional comment string
        if (check(TokenType::STRING)) {
            advance();
        }
        
        while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
            elseBody.push_back(parseStatement());
        }
    }
    
    expect(TokenType::END, "Expected 'end'");
    
    // Skip optional comment string after end
    if (check(TokenType::STRING)) {
        advance();
    }
    
    return std::make_unique<IfStmtAST>(std::move(condition), 
                                       std::move(thenBody),
                                       std::move(elseBody));
}

std::unique_ptr<WhileStmtAST> Parser::parseWhile() {
    expect(TokenType::WHILE, "Expected 'while'");
    auto condition = parseExpression();
    
    // Skip optional comment string
    if (check(TokenType::STRING)) {
        advance();
    }
    
    std::vector<std::unique_ptr<StmtAST>> body;
    
    // Parse body
    while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expect(TokenType::END, "Expected 'end'");
    
    // Skip optional comment string after end
    if (check(TokenType::STRING)) {
        advance();
    }
    
    return std::make_unique<WhileStmtAST>(std::move(condition), std::move(body));
}

std::unique_ptr<StmtAST> Parser::parseStatement() {
    // Skip string literals (comments)
    while (check(TokenType::STRING)) {
        advance();
    }
    
    if (check(TokenType::VAR)) {
        return parseVarDecl();
    }
    
    if (check(TokenType::IF)) {
        return parseIf();
    }
    
    if (check(TokenType::WHILE)) {
        return parseWhile();
    }
    
    if (check(TokenType::RETURN)) {
        return parseReturn();
    }
    
    if (check(TokenType::IDENTIFIER)) {
        std::string name = currentToken().value;
        advance();
        
        if (check(TokenType::ASSIGN)) {
            return parseAssignment(name);
        }
        
        throw std::runtime_error("Expected assignment after identifier at line " +
                                std::to_string(currentToken().line));
    }
    
    throw std::runtime_error("Unexpected token at line " +
                            std::to_string(currentToken().line));
}

std::unique_ptr<FunctionAST> Parser::parseFunction() {
    expect(TokenType::FUNCTION, "Expected 'function'");
    Token name = expect(TokenType::IDENTIFIER, "Expected function name");
    expect(TokenType::LPAREN, "Expected '('");
    expect(TokenType::RPAREN, "Expected ')'");
    
    // Parse return type
    std::string returnType = "void";
    if (check(TokenType::INT)) {
        returnType = currentToken().value;
        advance();
    }
    
    std::vector<std::unique_ptr<StmtAST>> body;
    
    // Parse function body
    while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expect(TokenType::END, "Expected 'end'");
    
    // Skip function name after 'end'
    if (check(TokenType::IDENTIFIER)) {
        advance();
    }
    
    return std::make_unique<FunctionAST>(name.value, returnType, std::move(body));
}

std::unique_ptr<ProgramAST> Parser::parse() {
    std::vector<std::unique_ptr<FunctionAST>> functions;
    
    while (!check(TokenType::END_OF_FILE)) {
        // Skip any stray string literals (comments)
        while (check(TokenType::STRING)) {
            advance();
        }
        
        if (check(TokenType::END_OF_FILE)) {
            break;
        }
        
        functions.push_back(parseFunction());
    }
    
    return std::make_unique<ProgramAST>(std::move(functions));
}
