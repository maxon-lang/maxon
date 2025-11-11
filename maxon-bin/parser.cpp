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
    
    if (check(TokenType::TRUE)) {
        advance();
        return std::make_unique<BooleanExprAST>(true);
    }
    
    if (check(TokenType::FALSE)) {
        advance();
        return std::make_unique<BooleanExprAST>(false);
    }
    
    if (check(TokenType::IDENTIFIER)) {
        std::string name = currentToken().value;
        advance();
        
        // Check for function call
        if (check(TokenType::LPAREN)) {
            advance(); // consume '('
            std::vector<std::unique_ptr<ExprAST>> args;
            
            // Parse arguments
            if (!check(TokenType::RPAREN)) {
                args.push_back(parseExpression());
                
                while (match(TokenType::COMMA)) {
                    args.push_back(parseExpression());
                }
            }
            
            expect(TokenType::RPAREN, "Expected ')' after function arguments");
            return std::make_unique<CallExprAST>(name, std::move(args));
        }
        
        // Just a variable reference
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
    if (check(TokenType::EQUALS) || check(TokenType::NOT_EQUAL) ||
        check(TokenType::GT) || check(TokenType::LT) || 
        check(TokenType::GTE) || check(TokenType::LTE)) {
        
        char op;
        TokenType type = currentToken().type;
        
        if (type == TokenType::EQUALS) op = 'E'; // = (equality)
        else if (type == TokenType::NOT_EQUAL) op = 'N'; // !=
        else if (type == TokenType::GT) op = '>';
        else if (type == TokenType::LT) op = '<';
        else if (type == TokenType::GTE) op = 'G'; // >=
        else if (type == TokenType::LTE) op = 'L'; // <=
        
        advance();
        auto right = parseComparison();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right));
    }
    
    return left;
}

std::unique_ptr<VarDeclStmtAST> Parser::parseVarDecl() {
    Token varToken = expect(TokenType::VAR, "Expected 'var'");
    Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
    expect(TokenType::EQUALS, "Expected '='");
    auto initializer = parseExpression();
    
    return std::make_unique<VarDeclStmtAST>(name.value, std::move(initializer), varToken.line, varToken.column);
}

std::unique_ptr<LetDeclStmtAST> Parser::parseLetDecl() {
    Token letToken = expect(TokenType::LET, "Expected 'let'");
    Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
    expect(TokenType::EQUALS, "Expected '='");
    auto initializer = parseExpression();
    
    return std::make_unique<LetDeclStmtAST>(name.value, std::move(initializer), letToken.line, letToken.column);
}

std::unique_ptr<AssignStmtAST> Parser::parseAssignment(const std::string& name) {
    Token assignToken = expect(TokenType::EQUALS, "Expected '='");
    auto value = parseExpression();
    return std::make_unique<AssignStmtAST>(name, std::move(value), assignToken.line, assignToken.column);
}

std::unique_ptr<ReturnStmtAST> Parser::parseReturn() {
    Token returnToken = expect(TokenType::RETURN, "Expected 'return'");
    auto value = parseExpression();
    return std::make_unique<ReturnStmtAST>(std::move(value), returnToken.line, returnToken.column);
}

std::unique_ptr<BreakStmtAST> Parser::parseBreak() {
    Token breakToken = expect(TokenType::BREAK, "Expected 'break'");
    return std::make_unique<BreakStmtAST>(breakToken.line, breakToken.column);
}

std::unique_ptr<ContinueStmtAST> Parser::parseContinue() {
    Token continueToken = expect(TokenType::CONTINUE, "Expected 'continue'");
    return std::make_unique<ContinueStmtAST>(continueToken.line, continueToken.column);
}

std::unique_ptr<IfStmtAST> Parser::parseIf() {
    Token ifToken = expect(TokenType::IF, "Expected 'if'");
    auto condition = parseExpression();
    
    int conditionLine = ifToken.line;
    
    // Check if this is a single-line if statement (no block identifier)
    // Single-line if: if <condition> <statement>
    // Multi-line if: if <condition> 'blockId' ... end 'blockId'
    bool isSingleLine = false;
    if (!check(TokenType::STRING)) {
        // No block identifier means single-line if
        isSingleLine = true;
    }
    
    std::vector<std::unique_ptr<StmtAST>> thenBody;
    std::vector<std::unique_ptr<StmtAST>> elseBody;
    
    if (isSingleLine) {
        // Single-line if: parse one statement that must be on the same line
        if (currentToken().line != conditionLine) {
            throw std::runtime_error("Single-line if statement must have statement on same line at line " + 
                                   std::to_string(conditionLine));
        }
        thenBody.push_back(parseStatement());
        
        // Single-line if doesn't support else
        return std::make_unique<IfStmtAST>(std::move(condition), 
                                           std::move(thenBody),
                                           std::move(elseBody),
                                           ifToken.line, ifToken.column);
    }
    
    // Multi-line if with block identifier
    Token blockIdToken = expect(TokenType::STRING, "Expected block identifier after 'if'");
    std::string blockId = blockIdToken.value;
    
    // Parse then body
    while (!check(TokenType::ELSE) && !check(TokenType::END) && 
           !check(TokenType::END_OF_FILE)) {
        thenBody.push_back(parseStatement());
    }
    
    // Parse optional else
    if (match(TokenType::ELSE)) {
        // Require same block identifier after else
        Token elseBlockIdToken = expect(TokenType::STRING, "Expected block identifier after 'else'");
        if (elseBlockIdToken.value != blockId) {
            throw std::runtime_error("Block identifier mismatch: 'else' expects '" + blockId + 
                                   "' but got '" + elseBlockIdToken.value + "' at line " + 
                                   std::to_string(elseBlockIdToken.line));
        }
        
        while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
            elseBody.push_back(parseStatement());
        }
    }
    
    expect(TokenType::END, "Expected 'end'");
    
    // Require matching block identifier after end
    Token endBlockIdToken = expect(TokenType::STRING, "Expected block identifier after 'end'");
    if (endBlockIdToken.value != blockId) {
        throw std::runtime_error("Block identifier mismatch: 'end' expects '" + blockId + 
                               "' but got '" + endBlockIdToken.value + "' at line " + 
                               std::to_string(endBlockIdToken.line));
    }
    
    return std::make_unique<IfStmtAST>(std::move(condition), 
                                       std::move(thenBody),
                                       std::move(elseBody),
                                       ifToken.line, ifToken.column);
}

std::unique_ptr<WhileStmtAST> Parser::parseWhile() {
    Token whileToken = expect(TokenType::WHILE, "Expected 'while'");
    auto condition = parseExpression();
    
    // Require block identifier
    Token blockIdToken = expect(TokenType::STRING, "Expected block identifier after 'while'");
    std::string blockId = blockIdToken.value;
    
    std::vector<std::unique_ptr<StmtAST>> body;
    
    // Parse body
    while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expect(TokenType::END, "Expected 'end'");
    
    // Require matching block identifier after end
    Token endBlockIdToken = expect(TokenType::STRING, "Expected block identifier after 'end'");
    if (endBlockIdToken.value != blockId) {
        throw std::runtime_error("Block identifier mismatch: 'end' expects '" + blockId + 
                               "' but got '" + endBlockIdToken.value + "' at line " + 
                               std::to_string(endBlockIdToken.line));
    }
    
    return std::make_unique<WhileStmtAST>(std::move(condition), std::move(body), whileToken.line, whileToken.column);
}

std::unique_ptr<StmtAST> Parser::parseStatement() {
    if (check(TokenType::VAR)) {
        return parseVarDecl();
    }
    
    if (check(TokenType::LET)) {
        return parseLetDecl();
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
    
    if (check(TokenType::BREAK)) {
        return parseBreak();
    }
    
    if (check(TokenType::CONTINUE)) {
        return parseContinue();
    }
    
    if (check(TokenType::IDENTIFIER)) {
        std::string name = currentToken().value;
        int idLine = currentToken().line;
        int idColumn = currentToken().column;
        advance();
        
        if (check(TokenType::EQUALS)) {
            return parseAssignment(name);
        }
        
        throw std::runtime_error("Unknown identifier '" + name + "' at line " +
                                std::to_string(idLine) + " character " + std::to_string(idColumn));
    }
    
    throw std::runtime_error("Unexpected token at line " +
                            std::to_string(currentToken().line));
}

std::unique_ptr<FunctionAST> Parser::parseFunction() {
    Token funcToken = expect(TokenType::FUNCTION, "Expected 'function'");
    Token name = expect(TokenType::IDENTIFIER, "Expected function name");
    expect(TokenType::LPAREN, "Expected '('");
    
    // Parse function parameters
    std::vector<FunctionParameter> parameters;
    if (!check(TokenType::RPAREN)) {
        do {
            Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");
            Token paramType = expect(TokenType::INT, "Expected parameter type");
            parameters.push_back(FunctionParameter(paramName.value, paramType.value, paramName.line, paramName.column));
        } while (match(TokenType::COMMA));
    }
    
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
    
    // Function has implicit block identifier which is the function name
    // Require matching block identifier after end
    Token endBlockIdToken = expect(TokenType::STRING, "Expected function name as block identifier after 'end'");
    if (endBlockIdToken.value != name.value) {
        throw std::runtime_error("Block identifier mismatch: 'end' expects '" + name.value + 
                               "' but got '" + endBlockIdToken.value + "' at line " + 
                               std::to_string(endBlockIdToken.line));
    }
    
    return std::make_unique<FunctionAST>(name.value, std::move(parameters), returnType, std::move(body), funcToken.line, funcToken.column);
}

std::unique_ptr<ProgramAST> Parser::parse() {
    std::vector<std::unique_ptr<FunctionAST>> functions;
    
    while (!check(TokenType::END_OF_FILE)) {
        if (check(TokenType::END_OF_FILE)) {
            break;
        }
        
        functions.push_back(parseFunction());
    }
    
    return std::make_unique<ProgramAST>(std::move(functions));
}
