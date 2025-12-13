#ifndef MAXON_LSP_FORMATTING_H
#define MAXON_LSP_FORMATTING_H

#include "../../ast.h"
#include "../../compiler_api.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include <vector>

namespace maxon_lsp {

// Namespace alias for easier access to LSP types
using TextEdit = maxon::lsp::TextEdit;
using Position = maxon::lsp::Position;
using Range = maxon::lsp::Range;
using FormattingOptions = maxon::lsp::FormattingOptions;

struct FormattingConfig {
	int tabSize = 4;
	bool insertSpaces = true;
	bool trimTrailingWhitespace = true;
	bool insertFinalNewline = true;
	int maxLineLength = 100; // For wrapping (0 = no limit)
};

class FormattingProvider {
  public:
	// Format entire document
	std::vector<TextEdit> formatDocument(
		const Document &document,
		const FormattingOptions &options,
		const AnalysisCache *cache);

	// Format a range within the document
	std::vector<TextEdit> formatRange(
		const Document &document,
		const Range &range,
		const FormattingOptions &options,
		const AnalysisCache *cache);

	// Format on type (after specific characters)
	std::vector<TextEdit> formatOnType(
		const Document &document,
		const Position &position,
		const std::string &character,
		const FormattingOptions &options);

  private:
	// Convert FormattingOptions to internal config
	FormattingConfig buildConfig(const FormattingOptions &options);

	// Format AST back to string
	std::string formatAST(const ProgramAST *ast, const FormattingConfig &config);

	// Format individual constructs
	std::string formatFunction(const FunctionAST *func, const FormattingConfig &config, int indent);
	std::string formatStruct(const StructDefAST *structDef, const FormattingConfig &config, int indent);
	std::string formatEnum(const EnumDefAST *enumDef, const FormattingConfig &config, int indent);
	std::string formatInterface(const InterfaceDefAST *iface, const FormattingConfig &config, int indent);
	std::string formatStatement(const StmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatExpression(const ExprAST *expr, const FormattingConfig &config);

	// Statement formatters
	std::string formatVarDecl(const VarDeclStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatLetDecl(const LetDeclStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatIf(const IfStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatIfLet(const IfLetStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatIfCase(const IfCaseStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatWhile(const WhileStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatFor(const ForStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatMatch(const MatchStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatReturn(const ReturnStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatAssign(const AssignStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatArrayAssign(const ArrayAssignStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatMemberAssign(const MemberAssignStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatArrayMemberAssign(const ArrayMemberAssignStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatMemberArrayAssign(const MemberArrayAssignStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatElseUnwrap(const ElseUnwrapStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatExprStmt(const ExprStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatBreak(const BreakStmtAST *stmt, const FormattingConfig &config, int indent);
	std::string formatContinue(const ContinueStmtAST *stmt, const FormattingConfig &config, int indent);

	// Expression formatters
	std::string formatBinaryExpr(const BinaryExprAST *expr, const FormattingConfig &config);
	std::string formatUnaryExpr(const UnaryExprAST *expr, const FormattingConfig &config);
	std::string formatCall(const CallExprAST *expr, const FormattingConfig &config);
	std::string formatMemberAccess(const MemberAccessExprAST *expr, const FormattingConfig &config);
	std::string formatArrayIndex(const ArrayIndexExprAST *expr, const FormattingConfig &config);
	std::string formatArrayLiteral(const ArrayLiteralExprAST *expr, const FormattingConfig &config);
	std::string formatStructInit(const StructInitExprAST *expr, const FormattingConfig &config);
	std::string formatMapLiteral(const MapLiteralExprAST *expr, const FormattingConfig &config);
	std::string formatCast(const CastExprAST *expr, const FormattingConfig &config);
	std::string formatMatchExpr(const MatchExprAST *expr, const FormattingConfig &config, int indent);

	// Helper methods
	std::string indent(int level, const FormattingConfig &config);
	std::string formatType(const std::string &type);
	std::string formatParameters(const std::vector<FunctionParameter> &params);

	// Diff-based edit generation
	std::vector<TextEdit> generateEdits(const std::string &original, const std::string &formatted);

	// Auto-indent on newline
	std::vector<TextEdit> autoIndentNewline(const Document &document, const Position &position, const FormattingConfig &config);

	// Complete end label on quote
	std::vector<TextEdit> completeEndLabel(const Document &document, const Position &position);

	// Line-based formatting fallback when AST is not available
	std::string formatLineByLine(const std::string &content, const FormattingConfig &config);

	// Trim trailing whitespace from a line
	std::string trimTrailingWhitespace(const std::string &line);

	// Get operator string from binary operator char
	std::string getOperatorString(char op);

	// Determine the indentation level of a line
	int getLineIndentLevel(const std::string &line, const FormattingConfig &config);

	// Check if a line starts a new block (function, struct, if, while, etc.)
	// insideInterface: true if currently inside an interface definition
	bool startsBlock(const std::string &line, bool insideInterface = false);

	// Check if a line ends a block (end 'label')
	bool endsBlock(const std::string &line);

	// Check if a line starts an inline match expression (e.g., "return match ...")
	bool startsInlineMatchExpr(const std::string &line);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_FORMATTING_H
