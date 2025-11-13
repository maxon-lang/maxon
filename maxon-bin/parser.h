#ifndef PARSER_H
#define PARSER_H

#include "lexer.h"
#include "ast.h"
#include <vector>
#include <memory>

class Parser {
private:
    std::vector<Token> tokens;
    size_t position;
    std::string defaultNamespace;  // Namespace derived from file path
    
    Token& currentToken();
    Token& peek(int offset = 1);
    bool match(TokenType type);
    bool check(TokenType type);
    bool check(TokenType type, int offset);  // Check token at offset
    void advance();
    Token expect(TokenType type, const std::string& message);
    
    std::unique_ptr<ExprAST> parseExpression();
    std::unique_ptr<ExprAST> parseComparison();
    std::unique_ptr<ExprAST> parseTerm();
    std::unique_ptr<ExprAST> parseFactor();
    std::unique_ptr<ExprAST> parseUnary();
    std::unique_ptr<ExprAST> parsePrimary();
    
    std::unique_ptr<StmtAST> parseStatement();
    std::unique_ptr<VarDeclStmtAST> parseVarDecl();
    std::unique_ptr<LetDeclStmtAST> parseLetDecl();
    std::unique_ptr<AssignStmtAST> parseAssignment(const std::string& name);
    std::unique_ptr<IfStmtAST> parseIf();
    std::unique_ptr<WhileStmtAST> parseWhile();
    std::unique_ptr<ReturnStmtAST> parseReturn();
    std::unique_ptr<BreakStmtAST> parseBreak();
    std::unique_ptr<ContinueStmtAST> parseContinue();
    
    std::unique_ptr<FunctionAST> parseFunction();
    std::unique_ptr<NamespaceAST> parseNamespace();
    
public:
    Parser(const std::vector<Token>& toks);
    void setDefaultNamespace(const std::string& ns);
    std::unique_ptr<ProgramAST> parse();
};

#endif // PARSER_H
