#ifndef PARSER_H
#define PARSER_H

#include "ast.h"
#include "lexer.h"
#include "logger.h"
#include <memory>
#include <vector>

class Parser {
  private:
	std::vector<Token> tokens;
	size_t position;
	std::string defaultNamespace; // Namespace derived from file path
	Logger *logger_ = nullptr;	  // Optional logger for detailed tracing

	Token &currentToken();
	Token &peek(int offset = 1);
	bool match(TokenType type);
	bool check(TokenType type);
	bool check(TokenType type, int offset); // Check token at offset
	void advance();
	Token expect(TokenType type, const std::string &message);
	Token expectKeyword(const std::string &keyword, const std::string &message);
	std::string parseQualifiedName(const std::string &context);

	std::unique_ptr<ExprAST> parseExpression();
	std::unique_ptr<ExprAST> parseLogicalAnd();
	std::unique_ptr<ExprAST> parseLogicalOr();
	std::unique_ptr<ExprAST> parseComparison();
	std::unique_ptr<ExprAST> parseTerm();
	std::unique_ptr<ExprAST> parseFactor();
	std::unique_ptr<ExprAST> parseUnary();
	std::unique_ptr<ExprAST> parsePrimary();

	std::unique_ptr<StmtAST> parseStatement();
	std::unique_ptr<VarDeclStmtAST> parseVarDecl();
	std::unique_ptr<LetDeclStmtAST> parseLetDecl();
	std::tuple<Token, std::string, std::unique_ptr<ExprAST>> parseVariableDeclarationComponents();
	std::unique_ptr<AssignStmtAST> parseAssignment(const std::string &name);
	std::unique_ptr<IfStmtAST> parseIf();
	std::unique_ptr<WhileStmtAST> parseWhile();
	std::unique_ptr<ForStmtAST> parseFor();
	std::unique_ptr<ReturnStmtAST> parseReturn();
	std::unique_ptr<BreakStmtAST> parseBreak();
	std::unique_ptr<ContinueStmtAST> parseContinue();

	std::unique_ptr<FunctionAST> parseFunction();
	std::unique_ptr<StructDefAST> parseStruct();
	std::unique_ptr<StructInitExprAST> parseStructInit(const std::string &structName);

	// Logging helpers
	void logTrace(const std::string &msg);
	void logDetail(const std::string &msg);

  public:
	Parser(const std::vector<Token> &toks);
	void setDefaultNamespace(const std::string &ns);
	void setLogger(Logger *logger) { logger_ = logger; }
	std::unique_ptr<ProgramAST> parse();
};

#endif // PARSER_H
