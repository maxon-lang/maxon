#ifndef PARSER_H
#define PARSER_H

#include "ast.h"
#include "lexer.h"
#include "logger.h"
#include "parser_support.h"
#include "token_stream.h"
#include <memory>
#include <vector>

class Parser {
  private:
	// Optimized token storage
	TokenStream stream_;
	LookaheadCache cache_;
	BlockBoundaryAnalyzer boundary_;

	size_t position;
	std::string defaultNamespace; // Namespace derived from file path
	Logger *logger_ = nullptr;	  // Optional logger for detailed tracing

	// Token access methods (use SIMD-optimized TokenStream)
	TokenType currentType() const;
	std::string_view currentValue() const;
	int currentLine() const;
	int currentColumn() const;
	std::optional<KeywordData> currentKeywordData() const;

	// Legacy compatibility - constructs Token on demand
	Token currentToken();
	Token peekToken(int offset = 1);

	bool match(TokenType type);
	bool check(TokenType type) const;
	bool check(TokenType type, int offset) const; // Check token at offset
	bool checkKeyword(const std::string &keyword) const;
	bool checkKeyword(const std::string &keyword, int offset) const;
	void advance();
	Token expect(TokenType type, const std::string &message);
	Token expectKeyword(const std::string &keyword, const std::string &message);

	// Zero-allocation variants - use when Token result is discarded
	void expectAdvance(TokenType type, const std::string &message);
	void expectKeywordAdvance(const std::string &keyword, const std::string &message);
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
	// Primary constructor: accepts TokenStream directly
	explicit Parser(TokenStream &&stream);

	// Legacy constructor for compatibility (converts to TokenStream internally)
	explicit Parser(const std::vector<Token> &toks);

	void setDefaultNamespace(const std::string &ns);
	void setLogger(Logger *logger) { logger_ = logger; }
	std::unique_ptr<ProgramAST> parse();
};

#endif // PARSER_H
