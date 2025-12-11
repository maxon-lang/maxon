#ifndef PARSER_H
#define PARSER_H

#include "ast.h"
#include "lexer.h"
#include "logger.h"
#include "parser_support.h"
#include "token_stream.h"
#include <memory>
#include <vector>

// Parse error information for error recovery and LSP diagnostics
struct ParseError {
	std::string message;
	int line;
	int column;
	int endLine;   // End position line (defaults to line if not set)
	int endColumn; // End position column (defaults to column if not set)
	int severity;  // 1 = Error, 2 = Warning, 3 = Info, 4 = Hint

	// Full constructor with all fields
	ParseError(const std::string &msg, int l, int c, int el = 0, int ec = 0, int sev = 1)
		: message(msg), line(l), column(c),
		  endLine(el == 0 ? l : el), endColumn(ec == 0 ? c : ec), severity(sev) {}
};

class Parser {
  private:
	// Optimized token storage
	TokenStream stream_;
	LookaheadCache cache_;
	BlockBoundaryAnalyzer boundary_;

	size_t position;
	std::string defaultNamespace; // Namespace derived from file path
	Logger *logger_ = nullptr;	  // Optional logger for detailed tracing

	// Error recovery state
	std::vector<ParseError> parseErrors_; // Collected parse errors
	bool inErrorRecovery_ = false;		  // True when recovering from an error

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
	std::string parseTypeString(const std::string &context);			 // Parse type including 'array of T'
	std::string parseTypeStringWithOptional(const std::string &context); // Parse type with optional 'or nil'
	std::string parseOptionalReturnType(int rparenLine, bool allowSelfType = false); // Parse return type if on same line

	// Declaration parsing helpers
	bool parseOptionalExport();													 // Parse optional 'export' keyword
	Token expectMatchingBlockId(const std::string &name, const std::string &ctx); // Validate end block identifier
	std::vector<FunctionParameter> parseParameterList(const std::string *selfType, int selfLine, int selfColumn);
	std::vector<std::unique_ptr<StmtAST>> parseStatementBody(); // Parse statements until 'end'
	std::unique_ptr<FunctionAST> parseMethodImpl(const std::string &receiverType, bool allowInterfacePrefix);

	std::unique_ptr<ExprAST> parseExpression();
	std::unique_ptr<ExprAST> parseBitwiseAnd();
	std::unique_ptr<ExprAST> parseBitwiseXor();
	std::unique_ptr<ExprAST> parseBitwiseOr();
	std::unique_ptr<ExprAST> parseLogicalAnd();
	std::unique_ptr<ExprAST> parseLogicalOr();
	std::unique_ptr<ExprAST> parseComparison();
	std::unique_ptr<ExprAST> parseShift();
	std::unique_ptr<ExprAST> parseAdditive();
	std::unique_ptr<ExprAST> parseTerm();
	std::unique_ptr<ExprAST> parseFactor();
	std::unique_ptr<ExprAST> parseUnary();
	std::unique_ptr<ExprAST> parsePostfix();
	std::unique_ptr<ExprAST> parsePrimary();

	std::unique_ptr<StmtAST> parseStatement();
	std::unique_ptr<StmtAST> parseVarDecl();
	std::unique_ptr<LetDeclStmtAST> parseLetDecl();
	std::tuple<Token, std::string, std::unique_ptr<ExprAST>> parseVariableDeclarationComponents();
	std::unique_ptr<AssignStmtAST> parseAssignment(const std::string &name);
	std::unique_ptr<StmtAST> parseIf();
	std::unique_ptr<IfLetStmtAST> parseIfLet(Token ifToken);
	std::unique_ptr<ElseUnwrapStmtAST> parseElseUnwrap(Token varToken, Token nameToken,
													   const std::string &explicitType,
													   std::unique_ptr<ExprAST> optionalExpr);
	std::unique_ptr<WhileStmtAST> parseWhile();
	std::unique_ptr<ForStmtAST> parseFor();
	std::unique_ptr<ReturnStmtAST> parseReturn();
	std::unique_ptr<BreakStmtAST> parseBreak();
	std::unique_ptr<ContinueStmtAST> parseContinue();
	std::unique_ptr<MatchStmtAST> parseMatch();
	std::unique_ptr<MatchExprAST> parseMatchExpr();
	std::unique_ptr<StmtAST> parseMatchCaseStatement(); // Parse statement in match case (stops before 'and fallthrough')

	std::unique_ptr<FunctionAST> parseFunction();
	std::unique_ptr<FunctionAST> parseMethod(const std::string &structName);   // Parse method inside struct
	std::unique_ptr<FunctionAST> parseEnumMethod(const std::string &enumName); // Parse method inside enum
	std::unique_ptr<StructDefAST> parseStruct();
	std::unique_ptr<EnumDefAST> parseEnum();
	std::unique_ptr<InterfaceDefAST> parseInterface();
	std::unique_ptr<StructInitExprAST> parseStructInit(const std::string &structName);
	std::unique_ptr<IfCaseStmtAST> parseIfCase(Token ifToken); // Parse if case statement

	// Logging helpers
	void logTrace(const std::string &msg);
	void logDetail(const std::string &msg);

	// Error reporting helper - throws runtime_error with location info
	[[noreturn]] void reportError(const std::string &message, int line, int column);

	// Error recovery methods
	void synchronize();									   // Advance to next synchronization point
	bool isSyncToken() const;							   // Check if current token is a sync point
	std::unique_ptr<StmtAST> parseStatementWithRecovery(); // Statement parsing with error recovery

  public:
	// Primary constructor: accepts TokenStream directly
	explicit Parser(TokenStream &&stream);

	// Legacy constructor for compatibility (converts to TokenStream internally)
	explicit Parser(const std::vector<Token> &toks);

	void setDefaultNamespace(const std::string &ns);
	void setLogger(Logger *logger) { logger_ = logger; }
	std::unique_ptr<ProgramAST> parse();

	// Error recovery accessors
	bool isInErrorRecoveryMode() const { return inErrorRecovery_; }
	const std::vector<ParseError> &getParseErrors() const { return parseErrors_; }
	bool hasErrors() const { return !parseErrors_.empty(); }
};

#endif // PARSER_H
