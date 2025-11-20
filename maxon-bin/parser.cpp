#include "parser.h"
#include <stdexcept>
#include <tuple>

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
            case TokenType::EQUALS: typeStr = "'='"; break;
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

std::unique_ptr<ExprAST> Parser::parsePrimary() {
    if (check(TokenType::NUMBER)) {
        int value = std::stoi(currentToken().value);
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<NumberExprAST>(value, line, column);
    }
    
    if (check(TokenType::FLOAT_LITERAL)) {
        double value = std::stod(currentToken().value);
        int line = currentToken().line;
        int column = currentToken().column;
        std::string literalString = currentToken().value;
        advance();
        return std::make_unique<FloatExprAST>(value, line, column, literalString);
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "true") {
        int line = currentToken().line;
        int column = currentToken().column;
        advance();
        return std::make_unique<BooleanExprAST>(true, line, column);
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "false") {
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
    
    // Array literal: [5]int or [1,2,3]
    if (check(TokenType::LBRACKET)) {
        int line = currentToken().line;
        int column = currentToken().column;
        advance(); // consume '['
        
        // Look ahead to determine which form:
        // - If first element is a number followed by ']' then type: [size]type
        // - Otherwise: [val1, val2, ...]
        
        if (check(TokenType::NUMBER) && peek(1).type == TokenType::RBRACKET) {
            // [size]type form
            Token sizeToken = expect(TokenType::NUMBER, "Expected array size");
            int size = std::stoi(sizeToken.value);
            expect(TokenType::RBRACKET, "Expected ']' after array size");
            
            // Now expect the element type (primitive or struct)
            if (!(currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) && !check(TokenType::IDENTIFIER)) {
                throw std::runtime_error("Expected array element type (int, float, ptr, char, string, or struct name) at line " + 
                                       std::to_string(currentToken().line));
            }
            std::string elementType = currentToken().value;
            advance();
            
            return std::make_unique<ArrayLiteralExprAST>(size, elementType, line, column);
        } else {
            // [val1, val2, ...] form
            std::vector<std::unique_ptr<ExprAST>> values;
            
            if (!check(TokenType::RBRACKET)) {
                values.push_back(parseExpression());
                
                while (match(TokenType::COMMA)) {
                    values.push_back(parseExpression());
                }
            }
            
            expect(TokenType::RBRACKET, "Expected ']' after array values");
            return std::make_unique<ArrayLiteralExprAST>(std::move(values), line, column);
        }
    }
    
    // Math intrinsic function keywords (built-in functions)
    if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::MathIntrinsic) {
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
        
        // Check for struct initialization (e.g., Planet{x: 1.0, y: 2.0})
        if (check(TokenType::LBRACE)) {
            return parseStructInit(name);
        }
        
        // Check for member access (e.g., array.length)
        // This must come before namespace qualification check
        if (check(TokenType::DOT) && !check(TokenType::LPAREN, 1)) {
            advance(); // consume '.'
            Token member = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
            
            // If followed by '(', treat as namespace-qualified function call
            if (check(TokenType::LPAREN)) {
                // This is namespace.function() - restore as qualified name
                std::string qualifiedName = name + "." + member.value;
                
                // Continue building qualified name for multiple namespaces
                while (check(TokenType::DOT) && peek(1).type == TokenType::IDENTIFIER) {
                    advance(); // consume '.'
                    Token nextMember = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
                    qualifiedName = qualifiedName + "." + nextMember.value;
                }
                
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
                return std::make_unique<CallExprAST>(qualifiedName, std::move(args), line, column);
            } else {
                // This is a member access (e.g., array.length)
                return std::make_unique<MemberAccessExprAST>(name, member.value, line, column);
            }
        }
        
        // Check for namespace qualification (namespace.namespace.function)
        // Support multiple levels: stdlib.fmt.function
        while (check(TokenType::DOT)) {
            advance(); // consume '.'
            Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
            name = name + "." + memberName.value;
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
            auto arrayExpr = std::make_unique<ArrayIndexExprAST>(name, std::move(index), line, column);
            
            // Check for member access on array element (e.g., arr[0].field)
            if (check(TokenType::DOT)) {
                advance(); // consume '.'
                Token member = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
                // Create a member access expression with the array index as the object
                return std::make_unique<MemberAccessExprAST>(std::move(arrayExpr), member.value, line, column);
            }
            
            return arrayExpr;
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
    if (check(TokenType::KEYWORD) && currentToken().value == "as") {
        int line = currentToken().line;
        int column = currentToken().column;
        advance(); // consume 'as'
        
        // Expect a type keyword
        if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) {
            std::string targetType = currentToken().value;
            advance();
            expr = std::make_unique<CastExprAST>(std::move(expr), targetType, line, column);
        } else {
            throw std::runtime_error("Expected type after 'as' keyword (int, float, ptr, char, string, or bool)\n  Location: line " +
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
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
    Token varToken = expectKeyword("var", "Expected 'var'");
    auto [name, type, initializer] = parseVariableDeclarationComponents();
    return std::make_unique<VarDeclStmtAST>(name.value, std::move(initializer), type, varToken.line, varToken.column);
}

std::unique_ptr<LetDeclStmtAST> Parser::parseLetDecl() {
    Token letToken = expectKeyword("let", "Expected 'let'");
    auto [name, type, initializer] = parseVariableDeclarationComponents();
    return std::make_unique<LetDeclStmtAST>(name.value, std::move(initializer), type, letToken.line, letToken.column);
}

std::tuple<Token, std::string, std::unique_ptr<ExprAST>> Parser::parseVariableDeclarationComponents() {
    Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
    
    // Type is always inferred from initializer
    std::string type = "";
    
    expect(TokenType::EQUALS, "Expected '='");
    auto initializer = parseExpression();
    
    return {name, type, std::move(initializer)};
}

std::unique_ptr<AssignStmtAST> Parser::parseAssignment(const std::string& name) {
    Token assignToken = expect(TokenType::EQUALS, "Expected '='");
    auto value = parseExpression();
    return std::make_unique<AssignStmtAST>(name, std::move(value), assignToken.line, assignToken.column);
}

std::unique_ptr<ReturnStmtAST> Parser::parseReturn() {
    Token returnToken = expectKeyword("return", "Expected 'return'");
    auto value = parseExpression();
    return std::make_unique<ReturnStmtAST>(std::move(value), returnToken.line, returnToken.column);
}

std::unique_ptr<BreakStmtAST> Parser::parseBreak() {
    Token breakToken = expectKeyword("break", "Expected 'break'");
    return std::make_unique<BreakStmtAST>(breakToken.line, breakToken.column);
}

std::unique_ptr<ContinueStmtAST> Parser::parseContinue() {
    Token continueToken = expectKeyword("continue", "Expected 'continue'");
    return std::make_unique<ContinueStmtAST>(continueToken.line, continueToken.column);
}

std::unique_ptr<IfStmtAST> Parser::parseIf() {
    Token ifToken = expectKeyword("if", "Expected 'if'");
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
    while (!(check(TokenType::KEYWORD) && currentToken().value == "else") && 
           !(check(TokenType::KEYWORD) && currentToken().value == "end") && 
           !check(TokenType::END_OF_FILE)) {
        thenBody.push_back(parseStatement());
    }
    
    // Parse optional else
    if (check(TokenType::KEYWORD) && currentToken().value == "else") {
        match(TokenType::KEYWORD); // consume "else"
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
        
        while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
            elseBody.push_back(parseStatement());
        }
    }
    
    expectKeyword("end", "Expected 'end' to close if block");
    
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
    Token whileToken = expectKeyword("while", "Expected 'while'");
    auto condition = parseExpression();
    
    // Require block identifier
    Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'while' condition (use 'id' where id is any string)");
    std::string blockId = blockIdToken.value;
    
    std::vector<std::unique_ptr<StmtAST>> body;
    
    // Parse body
    while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expectKeyword("end", "Expected 'end' to close while loop");
    
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
    if (check(TokenType::KEYWORD) && currentToken().value == "var") {
        return parseVarDecl();
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "let") {
        return parseLetDecl();
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "if") {
        return parseIf();
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "while") {
        return parseWhile();
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "return") {
        return parseReturn();
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "break") {
        return parseBreak();
    }
    
    if (check(TokenType::KEYWORD) && currentToken().value == "continue") {
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
        
        // Check for array indexing assignment: array[index] = value or array[index].member = value
        if (check(TokenType::LBRACKET)) {
            advance(); // consume '['
            auto index = parseExpression();
            expect(TokenType::RBRACKET, "Expected ']' after array index");
            
            // Check for member access on array element
            if (check(TokenType::DOT)) {
                advance(); // consume '.'
                Token memberName = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
                expect(TokenType::EQUALS, "Expected '=' in member assignment");
                auto value = parseExpression();
                // Create ArrayMemberAssignStmtAST for arr[i].field = value
                return std::make_unique<ArrayMemberAssignStmtAST>(name, std::move(index), memberName.value, std::move(value), idLine, idColumn);
            }
            
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
    if (check(TokenType::KEYWORD) && currentToken().value == "extern") {
        isExtern = true;
        advance(); // consume 'extern'
    }
    
    Token funcToken = expectKeyword("function", "Expected 'function'");
    Token name = expect(TokenType::IDENTIFIER, "Expected function name");
    expect(TokenType::LPAREN, "Expected '('");
    
    // Parse function parameters
    std::vector<FunctionParameter> parameters;
    if (!check(TokenType::RPAREN)) {
        do {
            Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");
            
            // Check for array type: []type only (sized arrays not allowed in parameters)
            std::string paramType;
            if (check(TokenType::LBRACKET)) {
                advance(); // consume '['
                
                // Array parameters must be unsized - reject [N]type syntax
                if (check(TokenType::NUMBER)) {
                    throw std::runtime_error("Array parameters must be unsized: use []type, not [" + currentToken().value + "]type\n  Location: line " + 
                                           std::to_string(currentToken().line) + ", column " + 
                                           std::to_string(currentToken().column));
                }
                
                expect(TokenType::RBRACKET, "Expected ']' after '['");
                
                // Get element type
                std::string elementType;
                if ((currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
                    elementType = currentToken().value;
                    advance();
                } else {
                    throw std::runtime_error("Expected array element type (int, float, ptr, char, string, bool, or struct name)\n  Location: line " + 
                                           std::to_string(currentToken().line) + ", column " + 
                                           std::to_string(currentToken().column));
                }
                
                // All array parameters are unsized
                paramType = "[]" + elementType;
            } else {
                // Regular scalar type (or struct)
                if ((currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
                    paramType = currentToken().value;
                    advance();
                } else {
                    throw std::runtime_error("Expected parameter type (int, float, ptr, char, string, bool, struct name, or [size]type)\n  Location: line " + 
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
    if ((currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
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
    while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
        body.push_back(parseStatement());
    }
    
    expectKeyword("end", "Expected 'end' to close function body");
    
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
    Token namespaceToken = expectKeyword("namespace", "Expected 'namespace'");
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
    while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
        if ((check(TokenType::KEYWORD) && currentToken().value == "extern") || 
            (check(TokenType::KEYWORD) && currentToken().value == "function")) {
            functions.push_back(parseFunction());
        } else {
            throw std::runtime_error("Expected 'function' or 'extern function' in namespace body\n  Location: line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
    }
    
    expectKeyword("end", "Expected 'end' to close namespace");
    
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

std::unique_ptr<StructDefAST> Parser::parseStruct() {
    Token structToken = expectKeyword("struct", "Expected 'struct'");
    int line = structToken.line;
    int column = structToken.column;
    
    Token nameToken = expect(TokenType::IDENTIFIER, "Expected struct name after 'struct'");
    std::string structName = nameToken.value;
    
    std::vector<StructField> fields;
    
    // Parse fields until we hit 'end'
    while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
        Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name");
        std::string fieldName = fieldNameToken.value;
        
        std::string fieldType;
        if (Lexer::isTypeToken(currentToken()) || check(TokenType::IDENTIFIER)) {
            fieldType = currentToken().value;
            advance();
        } else {
            throw std::runtime_error("Expected type after field name in struct field at line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
        
        fields.push_back(StructField(fieldName, fieldType, fieldNameToken.line, fieldNameToken.column));
    }
    
    expectKeyword("end", "Expected 'end' to close struct");
    
    // Require matching block identifier
    Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match struct name)");
    if (blockIdToken.value != structName) {
        throw std::runtime_error("Block identifier mismatch in struct definition" +
                               std::string("\n  Expected: '") + structName + "'" +
                               std::string("\n  Got: '") + blockIdToken.value + "'" +
                               std::string("\n  at line ") + std::to_string(blockIdToken.line) +
                               std::string(", column ") + std::to_string(blockIdToken.column));
    }
    
    return std::make_unique<StructDefAST>(structName, std::move(fields), line, column);
}

std::unique_ptr<StructInitExprAST> Parser::parseStructInit(const std::string& structName) {
    int line = currentToken().line;
    int column = currentToken().column;
    
    expect(TokenType::LBRACE, "Expected '{' for struct initialization");
    
    std::vector<StructInitField> fields;
    
    // Parse field initializers: fieldName: value
    while (!check(TokenType::RBRACE) && !check(TokenType::END_OF_FILE)) {
        Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name");
        std::string fieldName = fieldNameToken.value;
        
        expect(TokenType::COLON, "Expected ':' after field name");
        
        auto value = parseExpression();
        
        fields.push_back(StructInitField(fieldName, std::move(value), 
                                        fieldNameToken.line, fieldNameToken.column));
        
        // Check for comma (more fields) or closing brace
        if (check(TokenType::COMMA)) {
            advance();
        } else if (!check(TokenType::RBRACE)) {
            throw std::runtime_error("Expected ',' or '}' in struct initialization at line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
    }
    
    expect(TokenType::RBRACE, "Expected '}' to close struct initialization");
    
    return std::make_unique<StructInitExprAST>(structName, std::move(fields), line, column);
}

std::unique_ptr<ProgramAST> Parser::parse() {
    std::vector<std::unique_ptr<FunctionAST>> functions;
    std::vector<std::unique_ptr<NamespaceAST>> namespaces;
    std::vector<std::unique_ptr<StructDefAST>> structs;
    
    while (!check(TokenType::END_OF_FILE)) {
        if (check(TokenType::END_OF_FILE)) {
            break;
        }
        
        // Check for namespace, struct, extern, or function keyword
        if (check(TokenType::KEYWORD) && currentToken().value == "namespace") {
            namespaces.push_back(parseNamespace());
        } else if (check(TokenType::KEYWORD) && currentToken().value == "struct") {
            structs.push_back(parseStruct());
        } else if ((check(TokenType::KEYWORD) && currentToken().value == "extern") || 
                   (check(TokenType::KEYWORD) && currentToken().value == "function")) {
            functions.push_back(parseFunction());
        } else {
            throw std::runtime_error("Expected 'namespace', 'struct', 'function', or 'extern function' at top level\n  Location: line " + 
                                   std::to_string(currentToken().line) + ", column " + 
                                   std::to_string(currentToken().column));
        }
    }
    
    return std::make_unique<ProgramAST>(std::move(functions), std::move(namespaces), std::move(structs));
}
