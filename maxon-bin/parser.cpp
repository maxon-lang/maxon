#include "parser.h"
#include <stdexcept>

Parser::Parser(const std::vector<Token>& toks)
    : tokens(toks), position(0), defaultNamespace("") {}

void Parser::setDefaultNamespace(const std::string& ns) {
    defaultNamespace = ns;
}

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
            case TokenType::EQUALS: typeStr = "'='"; break;
            case TokenType::END: typeStr = "'end'"; break;
            case TokenType::FUNCTION: typeStr = "'function'"; break;
            case TokenType::VAR: typeStr = "'var'"; break;
            case TokenType::LET: typeStr = "'let'"; break;
            case TokenType::RETURN: typeStr = "'return'"; break;
            case TokenType::IF: typeStr = "'if'"; break;
            case TokenType::WHILE: typeStr = "'while'"; break;
            case TokenType::COMMA: typeStr = "','"; break;
            case TokenType::INT:
            case TokenType::FLOAT: typeStr = "type specifier"; break;
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

std::unique_ptr<ExprAST> Parser::parsePrimary() {
    if (check(TokenType::NUMBER)) {
        int value = std::stoi(currentToken().value);
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<NumberExprAST>(value, line, column);
    }
    
    if (check(TokenType::FLOAT_LITERAL)) {
        double value = currentToken().floatValue;
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<FloatExprAST>(value, line, column);
    }
    
    if (check(TokenType::TRUE)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<BooleanExprAST>(true, line, column);
    }
    
    if (check(TokenType::FALSE)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<BooleanExprAST>(false, line, column);
    }
    
    if (check(TokenType::CHARACTER)) {
        char value = currentToken().value[0];  // Get first character
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<CharacterExprAST>(value, line, column);
    }
    
    if (check(TokenType::STRING)) {
        std::string value = currentToken().value;
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<StringLiteralExprAST>(value, line, column);
    }
    
    // Math function keywords (built-in functions)
    // Single-argument functions: sqrt, abs, sin, cos, tan, log, exp, floor, ceil, round, trunc
    if (check(TokenType::SQRT) || check(TokenType::ABS) || check(TokenType::SIN) || 
        check(TokenType::COS) || check(TokenType::TAN) || check(TokenType::LOG) || 
        check(TokenType::EXP) || check(TokenType::FLOOR) || check(TokenType::CEIL) ||
        check(TokenType::ROUND) || check(TokenType::TRUNC)) {
        std::string funcName = currentToken().value;
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        
        expect(TokenType::LPAREN, "Expected '(' after '" + funcName + "'");
        auto arg = parseExpression();
        expect(TokenType::RPAREN, "Expected ')' after argument");
        
        std::vector<std::unique_ptr<ExprAST>> args;
        args.push_back(std::move(arg));
        return std::make_unique<CallExprAST>(funcName, std::move(args), line, column);
    }
    
    // Two-argument function: pow
    if (check(TokenType::POW)) {
        std::string funcName = currentToken().value;
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        
        expect(TokenType::LPAREN, "Expected '(' after 'pow'");
        auto base = parseExpression();
        expect(TokenType::COMMA, "Expected ',' after first argument to 'pow'");
        auto exponent = parseExpression();
        expect(TokenType::RPAREN, "Expected ')' after arguments");
        
        std::vector<std::unique_ptr<ExprAST>> args;
        args.push_back(std::move(base));
        args.push_back(std::move(exponent));
        return std::make_unique<CallExprAST>(funcName, std::move(args), line, column);
    }
    
    // Address-of operator: &variable
    if (check(TokenType::AMPERSAND)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance(); // consume '&'
        
        // Expect an identifier (variable name)
        if (!check(TokenType::IDENTIFIER)) {
            throw std::runtime_error("Expected variable name after '&' operator\n  Location: line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
        
        std::string varName = currentToken().value;
        advance();
        return std::make_unique<AddressOfExprAST>(varName, line, column);
    }
    
    // Dereference operator: *expr
    if (check(TokenType::MULTIPLY)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance(); // consume '*'
        
        // Parse the expression to dereference
        auto expr = parsePrimary();
        return std::make_unique<DerefExprAST>(std::move(expr), line, column);
    }
    
    if (check(TokenType::IDENTIFIER)) {
        std::string name = currentToken().value;
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        
        // Check for namespace qualification (namespace.namespace.function)
        // Support multiple levels: stdlib.fmt.function
        while (check(TokenType::DOT)) {
            advance(); // consume '.'
            Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
            name = name + "::" + memberName.value;
        }
        
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
            return std::make_unique<CallExprAST>(name, std::move(args), line, column);
        }
        
        // Check for array indexing
        if (check(TokenType::LBRACKET)) {
            advance(); // consume '['
            auto index = parseExpression();
            expect(TokenType::RBRACKET, "Expected ']' after array index");
            return std::make_unique<ArrayIndexExprAST>(name, std::move(index), line, column);
        }
        
        // Just a variable reference
        return std::make_unique<VariableExprAST>(name, line, column);
    }
    
    if (match(TokenType::LPAREN)) {
        auto expr = parseExpression();
        expect(TokenType::RPAREN, "Expected ')' to close parenthesized expression");
        return expr;
    }
    
    std::string foundStr;
    if (currentToken().type == TokenType::END_OF_FILE) {
        foundStr = "end of file";
    } else {
        foundStr = "'" + currentToken().value + "'";
    }
    
    throw std::runtime_error("Expected expression\n  Found: " + foundStr +
                            "\n  Location: line " + std::to_string(currentToken().line) + 
                            ", column " + std::to_string(currentToken().column) +
                            "\n  Note: An expression can be a number, variable, function call, or arithmetic/comparison operation");
}

std::unique_ptr<ExprAST> Parser::parseUnary() {
    // Handle unary operators: - and +
    if (check(TokenType::MINUS) || check(TokenType::PLUS)) {
        char op = currentToken().value[0];
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        
        auto operand = parseUnary();  // Allow chaining: --x, +-x, etc.
        return std::make_unique<UnaryExprAST>(op, std::move(operand), line, column);
    }
    
    return parsePrimary();
}

std::unique_ptr<ExprAST> Parser::parseFactor() {
    auto expr = parseUnary();
    
    // Handle type cast: expr as type
    if (check(TokenType::AS)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance(); // consume 'as'
        
        // Expect a type keyword (int, float, ptr, char)
        std::string targetType;
        if (check(TokenType::INT)) {
            targetType = "int";
            advance();
        } else if (check(TokenType::FLOAT)) {
            targetType = "float";
            advance();
        } else if (check(TokenType::PTR)) {
            targetType = "ptr";
            advance();
        } else if (check(TokenType::CHAR)) {
            targetType = "char";
            advance();
        } else {
            throw std::runtime_error("Expected type after 'as' keyword (int, float, ptr, or char)\n  Location: line " +
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
        
        expr = std::make_unique<CastExprAST>(std::move(expr), targetType, line, column);
    }
    
    return expr;
}

std::unique_ptr<ExprAST> Parser::parseTerm() {
    auto left = parseFactor();
    
    while (check(TokenType::MULTIPLY) || check(TokenType::DIVIDE) || check(TokenType::MODULO)) {
        char op = currentToken().value[0];
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        auto right = parseFactor();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
    }
    
    return left;
}

std::unique_ptr<ExprAST> Parser::parseComparison() {
    auto left = parseTerm();
    
    while (check(TokenType::PLUS) || check(TokenType::MINUS)) {
        char op = currentToken().value[0];
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        auto right = parseTerm();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
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
        int line = currentToken().line;
        int column = currentToken().column;
        
        if (type == TokenType::EQUALS) op = 'E'; // = (equality)
        else if (type == TokenType::NOT_EQUAL) op = 'N'; // !=
        else if (type == TokenType::GT) op = '>';
        else if (type == TokenType::LT) op = '<';
        else if (type == TokenType::GTE) op = 'G'; // >=
        else if (type == TokenType::LTE) op = 'L'; // <=
        
        advance();
        auto right = parseComparison();
        left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
    }
    
    return left;
}

std::unique_ptr<VarDeclStmtAST> Parser::parseVarDecl() {
    Token varToken = expect(TokenType::VAR, "Expected 'var'");
    Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
    
    // Optional type annotation
    std::string type;
    int arraySize = 0;
    
    // Check for array type: [size]type
    if (check(TokenType::LBRACKET)) {
        advance(); // consume '['
        Token sizeToken = expect(TokenType::NUMBER, "Expected array size");
        arraySize = std::stoi(sizeToken.value);
        expect(TokenType::RBRACKET, "Expected ']' after array size");
        
        // Now expect the element type
        if (check(TokenType::INT) || check(TokenType::FLOAT) || check(TokenType::PTR) || check(TokenType::CHAR)) {
            type = currentToken().value;
            advance();
        } else {
            throw std::runtime_error("Expected array element type (int, float, ptr, or char) at line " + 
                                   std::to_string(currentToken().line));
        }
    } else if (check(TokenType::INT) || check(TokenType::FLOAT) || check(TokenType::PTR) || check(TokenType::CHAR)) {
        // Regular type annotation
        type = currentToken().value;
        advance();
    }
    
    expect(TokenType::EQUALS, "Expected '='");
    auto initializer = parseExpression();
    
    return std::make_unique<VarDeclStmtAST>(name.value, std::move(initializer), type, arraySize, varToken.line, varToken.column);
}

std::unique_ptr<LetDeclStmtAST> Parser::parseLetDecl() {
    Token letToken = expect(TokenType::LET, "Expected 'let'");
    Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
    
    // Optional type annotation
    std::string type;
    int arraySize = 0;
    
    // Check for array type: [size]type
    if (check(TokenType::LBRACKET)) {
        advance(); // consume '['
        Token sizeToken = expect(TokenType::NUMBER, "Expected array size");
        arraySize = std::stoi(sizeToken.value);
        expect(TokenType::RBRACKET, "Expected ']' after array size");
        
        // Now expect the element type
        if (check(TokenType::INT) || check(TokenType::FLOAT) || check(TokenType::PTR) || check(TokenType::CHAR)) {
            type = currentToken().value;
            advance();
        } else {
            throw std::runtime_error("Expected array element type (int, float, ptr, or char) at line " + 
                                   std::to_string(currentToken().line));
        }
    } else if (check(TokenType::INT) || check(TokenType::FLOAT) || check(TokenType::PTR) || check(TokenType::CHAR)) {
        // Regular type annotation
        type = currentToken().value;
        advance();
    }
    
    expect(TokenType::EQUALS, "Expected '='");
    auto initializer = parseExpression();
    
    return std::make_unique<LetDeclStmtAST>(name.value, std::move(initializer), type, arraySize, letToken.line, letToken.column);
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
    if (!check(TokenType::BLOCK_ID)) {
        // No block identifier means single-line if
        isSingleLine = true;
    }
    
    std::vector<std::unique_ptr<StmtAST>> thenBody;
    std::vector<std::unique_ptr<StmtAST>> elseBody;
    
    if (isSingleLine) {
        // Single-line if: parse one statement that must be on the same line
        if (currentToken().line != conditionLine) {
            throw std::runtime_error("Single-line if statement must have statement on same line" + 
                                   std::string("\n  Location: line ") + std::to_string(conditionLine) + 
                                   ", column " + std::to_string(ifToken.column) +
                                   "\n  Note: For multi-line if blocks, use: if <condition> 'id' ... end 'id'");
        }
        thenBody.push_back(parseStatement());
        
        // Single-line if doesn't support else
        return std::make_unique<IfStmtAST>(std::move(condition), 
                                           std::move(thenBody),
                                           std::move(elseBody),
                                           ifToken.line, ifToken.column);
    }
    
    // Multi-line if with block identifier
    Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'if' condition (use 'id' where id is any string)");
    std::string blockId = blockIdToken.value;
    
    // Parse then body
    while (!check(TokenType::ELSE) && !check(TokenType::END) && 
           !check(TokenType::END_OF_FILE)) {
        thenBody.push_back(parseStatement());
    }
    
    // Parse optional else
    if (match(TokenType::ELSE)) {
        // Require same block identifier after else
        Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else' (must match the 'if' block identifier)");
        if (elseBlockIdToken.value != blockId) {
            throw std::runtime_error("Block identifier mismatch in if-else statement" +
                                   std::string("\n  Expected: '") + blockId + "'" +
                                   "\n  Found: '" + elseBlockIdToken.value + "'" +
                                   "\n  Location: line " + std::to_string(elseBlockIdToken.line) + 
                                   ", column " + std::to_string(elseBlockIdToken.column) +
                                   "\n  Note: The 'else' block identifier must match the 'if' block identifier");
        }
        
        while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
            elseBody.push_back(parseStatement());
        }
    }
    
    expect(TokenType::END, "Expected 'end' to close if block");
    
    // Require matching block identifier after end
    Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the opening block identifier)");
    if (endBlockIdToken.value != blockId) {
        throw std::runtime_error("Block identifier mismatch in if statement" +
                               std::string("\n  Expected: '") + blockId + "'" +
                               "\n  Found: '" + endBlockIdToken.value + "'" +
                               "\n  Location: line " + std::to_string(endBlockIdToken.line) + 
                               ", column " + std::to_string(endBlockIdToken.column) +
                               "\n  Note: The 'end' block identifier must match the opening 'if' block identifier");
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
    Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'while' condition (use 'id' where id is any string)");
    std::string blockId = blockIdToken.value;
    
    std::vector<std::unique_ptr<StmtAST>> body;
    
    // Parse body
    while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expect(TokenType::END, "Expected 'end' to close while loop");
    
    // Require matching block identifier after end
    Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the opening block identifier)");
    if (endBlockIdToken.value != blockId) {
        throw std::runtime_error("Block identifier mismatch in while loop" +
                               std::string("\n  Expected: '") + blockId + "'" +
                               "\n  Found: '" + endBlockIdToken.value + "'" +
                               "\n  Location: line " + std::to_string(endBlockIdToken.line) + 
                               ", column " + std::to_string(endBlockIdToken.column) +
                               "\n  Note: The 'end' block identifier must match the 'while' block identifier");
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
    
    // Check for pointer dereference assignment: *ptr = value
    if (check(TokenType::MULTIPLY)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance(); // consume '*'
        
        // Parse just the pointer identifier/expression (not a full expression that includes =)
        auto ptrExpr = parsePrimary();
        
        // Expect '='
        if (!check(TokenType::EQUALS)) {
            throw std::runtime_error("Expected '=' after pointer dereference in assignment at line " + 
                                   std::to_string(line) + ", column " + std::to_string(column));
        }
        advance(); // consume '='
        
        // Parse the value to assign
        auto value = parseExpression();
        
        return std::make_unique<DerefAssignStmtAST>(std::move(ptrExpr), std::move(value), line, column);
    }
    
    if (check(TokenType::IDENTIFIER)) {
        std::string name = currentToken().value;
        int idLine = currentToken().line;
        int idColumn = currentToken().column;
        advance();
        
        // Check for namespace qualification
        if (check(TokenType::DOT)) {
            advance(); // consume '.'
            Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
            name = name + "::" + memberName.value;
        }
        
        // Check for array indexing assignment: array[index] = value
        if (check(TokenType::LBRACKET)) {
            advance(); // consume '['
            auto index = parseExpression();
            expect(TokenType::RBRACKET, "Expected ']' after array index");
            expect(TokenType::EQUALS, "Expected '=' in array assignment");
            auto value = parseExpression();
            return std::make_unique<ArrayAssignStmtAST>(name, std::move(index), std::move(value), idLine, idColumn);
        }
        
        if (check(TokenType::EQUALS)) {
            return parseAssignment(name);
        }
        
        if (check(TokenType::LPAREN)) {
            // Parse function call as statement
            advance(); // consume '('
            
            std::vector<std::unique_ptr<ExprAST>> args;
            if (!check(TokenType::RPAREN)) {
                do {
                    args.push_back(parseExpression());
                } while (match(TokenType::COMMA));
            }
            
            expect(TokenType::RPAREN, "Expected ')' after function arguments");
            
            auto callExpr = std::make_unique<CallExprAST>(name, std::move(args), idLine, idColumn);
            return std::make_unique<ExprStmtAST>(std::move(callExpr), idLine, idColumn);
        }
        
        throw std::runtime_error("Unexpected identifier '" + name + "'" +
                                std::string("\n  Location: line ") + std::to_string(idLine) + 
                                ", column " + std::to_string(idColumn) +
                                "\n  Note: Did you forget an assignment (=), function call (), or keyword?");
    }
    
    std::string foundStr;
    if (currentToken().type == TokenType::END_OF_FILE) {
        foundStr = "end of file";
    } else {
        foundStr = "'" + currentToken().value + "'";
    }
    
    throw std::runtime_error("Unexpected token: " + foundStr +
                            "\n  Location: line " + std::to_string(currentToken().line) + 
                            ", column " + std::to_string(currentToken().column) +
                            "\n  Note: Expected a statement (var, let, if, while, return, break, continue, or assignment)");
}

std::unique_ptr<FunctionAST> Parser::parseFunction() {
    // Check for extern keyword
    bool isExtern = false;
    if (check(TokenType::EXTERN)) {
        isExtern = true;
        advance(); // consume 'extern'
    }
    
    Token funcToken = expect(TokenType::FUNCTION, "Expected 'function'");
    Token name = expect(TokenType::IDENTIFIER, "Expected function name");
    expect(TokenType::LPAREN, "Expected '('");
    
    // Parse function parameters
    std::vector<FunctionParameter> parameters;
    if (!check(TokenType::RPAREN)) {
        do {
            Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");
            
            // Check for array type: []type or [size]type
            std::string paramType;
            if (check(TokenType::LBRACKET)) {
                advance(); // consume '['
                
                // Check if size is provided or if it's an unsized array parameter
                std::string sizeStr = "";
                if (check(TokenType::NUMBER)) {
                    Token sizeToken = currentToken();
                    sizeStr = sizeToken.value;
                    advance();
                }
                
                expect(TokenType::RBRACKET, "Expected ']' after array size");
                
                // Get element type
                std::string elementType;
                if (check(TokenType::INT)) {
                    elementType = "int";
                    advance();
                } else if (check(TokenType::PTR)) {
                    elementType = "ptr";
                    advance();
                } else if (check(TokenType::CHAR)) {
                    elementType = "char";
                    advance();
                } else if (check(TokenType::FLOAT)) {
                    elementType = "float";
                    advance();
                } else {
                    throw std::runtime_error("Expected array element type (int, float, ptr, or char)\n  Location: line " + 
                                           std::to_string(currentToken().line) + ", column " + 
                                           std::to_string(currentToken().column));
                }
                
                // Encode as "[]type" for unsized or "[size]type" for sized
                if (sizeStr.empty()) {
                    paramType = "[]" + elementType;
                } else {
                    paramType = "[" + sizeStr + "]" + elementType;
                }
            } else {
                // Regular scalar type
                if (check(TokenType::INT)) {
                    paramType = "int";
                    advance();
                } else if (check(TokenType::PTR)) {
                    paramType = "ptr";
                    advance();
                } else if (check(TokenType::CHAR)) {
                    paramType = "char";
                    advance();
                } else if (check(TokenType::FLOAT)) {
                    paramType = "float";
                    advance();
                } else {
                    throw std::runtime_error("Expected parameter type (int, float, ptr, char, or [size]type)\n  Location: line " + 
                                           std::to_string(currentToken().line) + ", column " + 
                                           std::to_string(currentToken().column));
                }
            }
            
            parameters.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
        } while (match(TokenType::COMMA));
    }
    
    expect(TokenType::RPAREN, "Expected ')'");
    
    // Parse return type (optional - defaults to void)
    std::string returnType = "void";
    if (check(TokenType::INT) || check(TokenType::FLOAT) || check(TokenType::PTR) || check(TokenType::CHAR)) {
        returnType = currentToken().value;
        advance();
    }
    
    std::vector<std::unique_ptr<StmtAST>> body;
    
    // External functions don't have bodies
    if (isExtern) {
        // No body for extern functions - they're just declarations
        return std::make_unique<FunctionAST>(name.value, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace);
    }
    
    // Parse function body
    while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expect(TokenType::END, "Expected 'end' to close function body");
    
    // Function has implicit block identifier which is the function name
    // Require matching block identifier after end
    Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected function name as block identifier after 'end'");
    if (endBlockIdToken.value != name.value) {
        throw std::runtime_error("Block identifier mismatch in function definition" +
                               std::string("\n  Expected: '") + name.value + "'" +
                               "\n  Found: '" + endBlockIdToken.value + "'" +
                               "\n  Location: line " + std::to_string(endBlockIdToken.line) + 
                               ", column " + std::to_string(endBlockIdToken.column) +
                               "\n  Note: The 'end' block identifier must match the function name");
    }
    
    return std::make_unique<FunctionAST>(name.value, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace);
}

std::unique_ptr<NamespaceAST> Parser::parseNamespace() {
    Token namespaceToken = expect(TokenType::NAMESPACE, "Expected 'namespace'");
    Token name = expect(TokenType::IDENTIFIER, "Expected namespace name");
    
    // Expect block identifier (namespace name in quotes)
    Token blockId = expect(TokenType::BLOCK_ID, "Expected namespace name as block identifier");
    if (blockId.value != name.value) {
        throw std::runtime_error("Block identifier mismatch in namespace definition" +
                               std::string("\n  Expected: '") + name.value + "'" +
                               "\n  Found: '" + blockId.value + "'" +
                               "\n  Location: line " + std::to_string(blockId.line) + 
                               ", column " + std::to_string(blockId.column));
    }
    
    // Parse functions within the namespace
    std::vector<std::unique_ptr<FunctionAST>> functions;
    while (!check(TokenType::END) && !check(TokenType::END_OF_FILE)) {
        if (check(TokenType::EXTERN) || check(TokenType::FUNCTION)) {
            functions.push_back(parseFunction());
        } else {
            throw std::runtime_error("Expected 'function' or 'extern function' in namespace body\n  Location: line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
    }
    
    expect(TokenType::END, "Expected 'end' to close namespace");
    
    // Expect matching block identifier
    Token endBlockId = expect(TokenType::BLOCK_ID, "Expected namespace name as block identifier after 'end'");
    if (endBlockId.value != name.value) {
        throw std::runtime_error("Block identifier mismatch after namespace" +
                               std::string("\n  Expected: '") + name.value + "'" +
                               "\n  Found: '" + endBlockId.value + "'" +
                               "\n  Location: line " + std::to_string(endBlockId.line) + 
                               ", column " + std::to_string(endBlockId.column));
    }
    
    return std::make_unique<NamespaceAST>(name.value, std::move(functions), namespaceToken.line, namespaceToken.column);
}

std::unique_ptr<ProgramAST> Parser::parse() {
    std::vector<std::unique_ptr<FunctionAST>> functions;
    std::vector<std::unique_ptr<NamespaceAST>> namespaces;
    
    while (!check(TokenType::END_OF_FILE)) {
        if (check(TokenType::END_OF_FILE)) {
            break;
        }
        
        // Check for namespace, extern, or function keyword
        if (check(TokenType::NAMESPACE)) {
            namespaces.push_back(parseNamespace());
        } else if (check(TokenType::EXTERN) || check(TokenType::FUNCTION)) {
            functions.push_back(parseFunction());
        } else {
            throw std::runtime_error("Expected 'namespace', 'function', or 'extern function' at top level\n  Location: line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
    }
    
    return std::make_unique<ProgramAST>(std::move(functions), std::move(namespaces));
}
