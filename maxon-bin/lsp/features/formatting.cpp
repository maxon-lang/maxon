#include "formatting.h"
#include <algorithm>
#include <regex>
#include <sstream>

namespace maxon_lsp {

FormattingConfig FormattingProvider::buildConfig(const FormattingOptions &options) {
	FormattingConfig config;
	config.tabSize = options.tabSize;
	config.insertSpaces = options.insertSpaces;
	config.trimTrailingWhitespace = options.trimTrailingWhitespace.value_or(true);
	config.insertFinalNewline = options.insertFinalNewline.value_or(true);
	return config;
}

std::vector<TextEdit> FormattingProvider::formatDocument(
	const Document &document,
	const FormattingOptions &options,
	const AnalysisCache *cache) {
	FormattingConfig config = buildConfig(options);

	// Always use line-by-line formatting to preserve comments, blank lines,
	// and implicit type declarations. AST-based formatting loses this information.
	std::string formatted = formatLineByLine(document.content, config);

	return generateEdits(document.content, formatted);
}

std::vector<TextEdit> FormattingProvider::formatRange(
	const Document &document,
	const Range &range,
	const FormattingOptions &options,
	const AnalysisCache *cache) {
	FormattingConfig config = buildConfig(options);

	// Always use line-by-line formatting to preserve comments, blank lines,
	// and implicit type declarations.
	std::string formatted = formatLineByLine(document.content, config);

	// Generate all edits
	std::vector<TextEdit> allEdits = generateEdits(document.content, formatted);

	// Filter to only include edits that overlap with the range
	std::vector<TextEdit> rangeEdits;
	for (const auto &edit : allEdits) {
		// Check if edit overlaps with range
		if (edit.range.end.line >= range.start.line &&
			edit.range.start.line <= range.end.line) {
			rangeEdits.push_back(edit);
		}
	}

	return rangeEdits;
}

std::vector<TextEdit> FormattingProvider::formatOnType(
	const Document &document,
	const Position &position,
	const std::string &character,
	const FormattingOptions &options) {
	FormattingConfig config = buildConfig(options);

	if (character == "\n") {
		return autoIndentNewline(document, position, config);
	} else if (character == "'") {
		return completeEndLabel(document, position);
	}

	return {};
}

std::vector<TextEdit> FormattingProvider::autoIndentNewline(
	const Document &document,
	const Position &position,
	const FormattingConfig &config) {
	// Position is right after the newline was inserted
	// We need to look at the previous line to determine indentation
	if (position.line == 0) {
		return {};
	}

	int prevLineIdx = position.line - 1;
	if (prevLineIdx < 0 || prevLineIdx >= document.getLineCount()) {
		return {};
	}

	std::string prevLine = document.getLine(prevLineIdx);

	// Get current indentation level of previous line
	int currentIndent = getLineIndentLevel(prevLine, config);

	// Check if previous line starts a block
	if (startsBlock(prevLine)) {
		currentIndent++;
	}

	// Generate the indentation string
	std::string indentStr = indent(currentIndent, config);

	// Create edit to insert indentation at the cursor position
	TextEdit edit;
	edit.range = Range(position.line, 0, position.line, position.character);
	edit.newText = indentStr;

	return {edit};
}

std::vector<TextEdit> FormattingProvider::completeEndLabel(
	const Document &document,
	const Position &position) {
	// This is called after typing a quote character
	// Check if we're on a line that starts with "end" and complete the label

	if (position.line < 0 || position.line >= document.getLineCount()) {
		return {};
	}

	std::string currentLine = document.getLine(position.line);

	// Check if line matches "end '" pattern (with optional whitespace)
	std::regex endPattern(R"(^\s*end\s+'$)");
	if (!std::regex_match(currentLine, endPattern)) {
		return {};
	}

	// Search backwards for the matching block start to find the label
	for (int lineIdx = position.line - 1; lineIdx >= 0; lineIdx--) {
		std::string line = document.getLine(lineIdx);

		// Look for block start patterns with labels
		// Pattern: keyword ... 'label' or ... then 'label' or ... do 'label'
		std::regex labelPattern(R"('([^']+)'\s*$)");
		std::smatch match;
		if (std::regex_search(line, match, labelPattern)) {
			std::string label = match[1].str();

			// Insert the label and closing quote
			TextEdit edit;
			edit.range = Range(position.line, position.character, position.line, position.character);
			edit.newText = label + "'";

			return {edit};
		}
	}

	return {};
}

std::string FormattingProvider::formatAST(const ProgramAST *ast, const FormattingConfig &config) {
	std::ostringstream out;

	// Format interfaces
	for (size_t i = 0; i < ast->interfaces.size(); i++) {
		if (i > 0 || !ast->functions.empty() || !ast->structs.empty() || !ast->enums.empty()) {
			out << "\n";
		}
		out << formatInterface(ast->interfaces[i].get(), config, 0);
	}

	// Format enums
	for (size_t i = 0; i < ast->enums.size(); i++) {
		if (!ast->interfaces.empty() || i > 0) {
			out << "\n";
		}
		out << formatEnum(ast->enums[i].get(), config, 0);
	}

	// Format structs
	for (size_t i = 0; i < ast->structs.size(); i++) {
		if (!ast->interfaces.empty() || !ast->enums.empty() || i > 0) {
			out << "\n";
		}
		out << formatStruct(ast->structs[i].get(), config, 0);
	}

	// Format functions
	for (size_t i = 0; i < ast->functions.size(); i++) {
		if (!ast->interfaces.empty() || !ast->enums.empty() || !ast->structs.empty() || i > 0) {
			out << "\n";
		}
		out << formatFunction(ast->functions[i].get(), config, 0);
	}

	std::string result = out.str();

	// Ensure final newline if configured
	if (config.insertFinalNewline && !result.empty() && result.back() != '\n') {
		result += '\n';
	}

	return result;
}

std::string FormattingProvider::formatFunction(const FunctionAST *func, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	// Export keyword if applicable
	if (func->isExported) {
		out << indent(indentLevel, config) << "export ";
	} else {
		out << indent(indentLevel, config);
	}

	// Extern functions have no body
	if (func->isExtern) {
		out << "extern function " << func->name << "(";
		out << formatParameters(func->parameters);
		out << ")";
		if (!func->returnType.empty()) {
			out << " " << formatType(func->returnType);
		}
		out << "\n";
		return out.str();
	}

	out << "function " << func->name << "(";
	out << formatParameters(func->parameters);
	out << ")";
	if (!func->returnType.empty()) {
		out << " " << formatType(func->returnType);
	}
	out << "\n";

	// Format body
	for (const auto &stmt : func->body) {
		out << formatStatement(stmt.get(), config, indentLevel + 1);
	}

	// End statement
	out << indent(indentLevel, config) << "end '" << func->name << "'\n";

	return out.str();
}

std::string FormattingProvider::formatStruct(const StructDefAST *structDef, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	// Export keyword if applicable
	if (structDef->isExported) {
		out << indent(indentLevel, config) << "export ";
	} else {
		out << indent(indentLevel, config);
	}

	out << "type " << structDef->name;

	// Interface conformance
	if (!structDef->conformsTo.empty()) {
		out << " is ";
		for (size_t i = 0; i < structDef->conformsTo.size(); i++) {
			if (i > 0)
				out << ", ";
			out << structDef->conformsTo[i];
		}
	}

	out << "\n";

	// Format fields
	for (const auto &field : structDef->fields) {
		out << indent(indentLevel + 1, config);
		out << (field.isImmutable ? "let " : "var ");
		out << field.name << " " << formatType(field.type);
		if (field.defaultValue) {
			out << " = " << formatExpression(field.defaultValue.get(), config);
		}
		out << "\n";
	}

	// Format methods
	for (const auto &method : structDef->methods) {
		out << "\n";
		out << formatFunction(method.get(), config, indentLevel + 1);
	}

	out << indent(indentLevel, config) << "end '" << structDef->name << "'\n";

	return out.str();
}

std::string FormattingProvider::formatEnum(const EnumDefAST *enumDef, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	// Export keyword if applicable
	if (enumDef->isExported) {
		out << indent(indentLevel, config) << "export ";
	} else {
		out << indent(indentLevel, config);
	}

	out << "enum " << enumDef->name;

	// Raw value type
	if (!enumDef->rawValueType.empty()) {
		out << " " << enumDef->rawValueType;
	}

	out << "\n";

	// Format cases
	for (const auto &enumCase : enumDef->cases) {
		out << indent(indentLevel + 1, config) << "case " << enumCase.name;

		// Associated values
		if (!enumCase.associatedValues.empty()) {
			out << "(";
			for (size_t i = 0; i < enumCase.associatedValues.size(); i++) {
				if (i > 0)
					out << ", ";
				out << enumCase.associatedValues[i].name << " " << enumCase.associatedValues[i].type;
			}
			out << ")";
		}

		// Raw value
		if (enumCase.rawValue) {
			out << " = " << formatExpression(enumCase.rawValue.get(), config);
		}

		out << "\n";
	}

	// Format methods
	for (const auto &method : enumDef->methods) {
		out << "\n";
		out << formatFunction(method.get(), config, indentLevel + 1);
	}

	out << indent(indentLevel, config) << "end '" << enumDef->name << "'\n";

	return out.str();
}

std::string FormattingProvider::formatInterface(const InterfaceDefAST *iface, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	// Export keyword if applicable
	if (iface->isExported) {
		out << indent(indentLevel, config) << "export ";
	} else {
		out << indent(indentLevel, config);
	}

	out << "interface " << iface->name;

	// Associated types
	if (!iface->associatedTypes.empty()) {
		out << " uses ";
		for (size_t i = 0; i < iface->associatedTypes.size(); i++) {
			if (i > 0)
				out << ", ";
			out << iface->associatedTypes[i];
		}
	}

	out << "\n";

	// Format method signatures
	for (const auto &method : iface->methods) {
		out << indent(indentLevel + 1, config) << "function " << method.name << "(";
		for (size_t i = 0; i < method.parameters.size(); i++) {
			if (i > 0)
				out << ", ";
			out << method.parameters[i].name << " " << formatType(method.parameters[i].type);
		}
		out << ")";
		if (!method.returnType.empty()) {
			out << " " << formatType(method.returnType);
		}
		out << "\n";
	}

	out << indent(indentLevel, config) << "end '" << iface->name << "'\n";

	return out.str();
}

std::string FormattingProvider::formatStatement(const StmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	if (!stmt)
		return "";

	// Try each statement type
	if (auto *varDecl = dynamic_cast<const VarDeclStmtAST *>(stmt)) {
		return formatVarDecl(varDecl, config, indentLevel);
	}
	if (auto *letDecl = dynamic_cast<const LetDeclStmtAST *>(stmt)) {
		return formatLetDecl(letDecl, config, indentLevel);
	}
	if (auto *ifStmt = dynamic_cast<const IfStmtAST *>(stmt)) {
		return formatIf(ifStmt, config, indentLevel);
	}
	if (auto *ifLetStmt = dynamic_cast<const IfLetStmtAST *>(stmt)) {
		return formatIfLet(ifLetStmt, config, indentLevel);
	}
	if (auto *ifCaseStmt = dynamic_cast<const IfCaseStmtAST *>(stmt)) {
		return formatIfCase(ifCaseStmt, config, indentLevel);
	}
	if (auto *whileStmt = dynamic_cast<const WhileStmtAST *>(stmt)) {
		return formatWhile(whileStmt, config, indentLevel);
	}
	if (auto *forStmt = dynamic_cast<const ForStmtAST *>(stmt)) {
		return formatFor(forStmt, config, indentLevel);
	}
	if (auto *matchStmt = dynamic_cast<const MatchStmtAST *>(stmt)) {
		return formatMatch(matchStmt, config, indentLevel);
	}
	if (auto *returnStmt = dynamic_cast<const ReturnStmtAST *>(stmt)) {
		return formatReturn(returnStmt, config, indentLevel);
	}
	if (auto *assignStmt = dynamic_cast<const AssignStmtAST *>(stmt)) {
		return formatAssign(assignStmt, config, indentLevel);
	}
	if (auto *arrAssign = dynamic_cast<const ArrayAssignStmtAST *>(stmt)) {
		return formatArrayAssign(arrAssign, config, indentLevel);
	}
	if (auto *memAssign = dynamic_cast<const MemberAssignStmtAST *>(stmt)) {
		return formatMemberAssign(memAssign, config, indentLevel);
	}
	if (auto *arrMemAssign = dynamic_cast<const ArrayMemberAssignStmtAST *>(stmt)) {
		return formatArrayMemberAssign(arrMemAssign, config, indentLevel);
	}
	if (auto *memArrAssign = dynamic_cast<const MemberArrayAssignStmtAST *>(stmt)) {
		return formatMemberArrayAssign(memArrAssign, config, indentLevel);
	}
	if (auto *elseUnwrap = dynamic_cast<const ElseUnwrapStmtAST *>(stmt)) {
		return formatElseUnwrap(elseUnwrap, config, indentLevel);
	}
	if (auto *exprStmt = dynamic_cast<const ExprStmtAST *>(stmt)) {
		return formatExprStmt(exprStmt, config, indentLevel);
	}
	if (auto *breakStmt = dynamic_cast<const BreakStmtAST *>(stmt)) {
		return formatBreak(breakStmt, config, indentLevel);
	}
	if (auto *continueStmt = dynamic_cast<const ContinueStmtAST *>(stmt)) {
		return formatContinue(continueStmt, config, indentLevel);
	}

	// Fallback for unknown statement types
	return indent(indentLevel, config) + "// unknown statement\n";
}

std::string FormattingProvider::formatVarDecl(const VarDeclStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << "var " << stmt->name;
	if (!stmt->type.empty()) {
		out << " " << formatType(stmt->type);
	}
	if (stmt->initializer) {
		out << " = " << formatExpression(stmt->initializer.get(), config);
	}
	out << "\n";
	return out.str();
}

std::string FormattingProvider::formatLetDecl(const LetDeclStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << "let " << stmt->name;
	if (!stmt->type.empty()) {
		out << " " << formatType(stmt->type);
	}
	if (stmt->initializer) {
		out << " = " << formatExpression(stmt->initializer.get(), config);
	}
	out << "\n";
	return out.str();
}

std::string FormattingProvider::formatIf(const IfStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << indent(indentLevel, config) << "if " << formatExpression(stmt->condition.get(), config);
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	// Then body
	for (const auto &s : stmt->thenBody) {
		out << formatStatement(s.get(), config, indentLevel + 1);
	}

	// Else body
	if (!stmt->elseBody.empty()) {
		out << indent(indentLevel, config) << "else";
		if (!stmt->blockId.empty()) {
			out << " '" << stmt->blockId << "'";
		}
		out << "\n";
		for (const auto &s : stmt->elseBody) {
			out << formatStatement(s.get(), config, indentLevel + 1);
		}
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatIfLet(const IfLetStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << indent(indentLevel, config) << "if let " << stmt->bindingName << " = ";
	out << formatExpression(stmt->optionalExpr.get(), config);
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	// Then body
	for (const auto &s : stmt->thenBody) {
		out << formatStatement(s.get(), config, indentLevel + 1);
	}

	// Else body
	if (!stmt->elseBody.empty()) {
		out << indent(indentLevel, config) << "else";
		if (!stmt->blockId.empty()) {
			out << " '" << stmt->blockId << "'";
		}
		out << "\n";
		for (const auto &s : stmt->elseBody) {
			out << formatStatement(s.get(), config, indentLevel + 1);
		}
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatIfCase(const IfCaseStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << indent(indentLevel, config) << "if case " << stmt->caseName << "(";
	for (size_t i = 0; i < stmt->bindings.size(); i++) {
		if (i > 0)
			out << ", ";
		out << stmt->bindings[i];
	}
	out << ") = " << formatExpression(stmt->enumExpr.get(), config);
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	// Then body
	for (const auto &s : stmt->thenBody) {
		out << formatStatement(s.get(), config, indentLevel + 1);
	}

	// Else body
	if (!stmt->elseBody.empty()) {
		out << indent(indentLevel, config) << "else";
		if (!stmt->blockId.empty()) {
			out << " '" << stmt->blockId << "'";
		}
		out << "\n";
		for (const auto &s : stmt->elseBody) {
			out << formatStatement(s.get(), config, indentLevel + 1);
		}
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatWhile(const WhileStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << indent(indentLevel, config) << "while " << formatExpression(stmt->condition.get(), config);
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	for (const auto &s : stmt->body) {
		out << formatStatement(s.get(), config, indentLevel + 1);
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatFor(const ForStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << indent(indentLevel, config) << "for " << stmt->loopVar << " in ";
	out << formatExpression(stmt->iterable.get(), config);
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	for (const auto &s : stmt->body) {
		out << formatStatement(s.get(), config, indentLevel + 1);
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatMatch(const MatchStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << indent(indentLevel, config) << "match " << formatExpression(stmt->scrutinee.get(), config);
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	for (const auto &caseItem : stmt->cases) {
		out << indent(indentLevel + 1, config);

		if (caseItem.isDefault) {
			out << "default";
		} else {
			for (size_t i = 0; i < caseItem.patterns.size(); i++) {
				if (i > 0)
					out << " or ";
				out << formatExpression(caseItem.patterns[i].get(), config);
			}
		}

		out << " then ";
		if (caseItem.statement) {
			// Single statement on same line (without indentation)
			std::string stmtStr = formatStatement(caseItem.statement.get(), config, 0);
			// Remove leading whitespace and trailing newline for inline display
			size_t start = stmtStr.find_first_not_of(" \t");
			if (start != std::string::npos) {
				stmtStr = stmtStr.substr(start);
			}
			if (!stmtStr.empty() && stmtStr.back() == '\n') {
				stmtStr.pop_back();
			}
			out << stmtStr;
		}

		if (caseItem.hasFallthrough) {
			out << " and fallthrough";
		}

		out << "\n";
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatReturn(const ReturnStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << "return";
	if (stmt->value) {
		out << " " << formatExpression(stmt->value.get(), config);
	}
	out << "\n";
	return out.str();
}

std::string FormattingProvider::formatAssign(const AssignStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << stmt->name << " = ";
	out << formatExpression(stmt->value.get(), config) << "\n";
	return out.str();
}

std::string FormattingProvider::formatArrayAssign(const ArrayAssignStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << stmt->arrayName << "[";
	out << formatExpression(stmt->index.get(), config) << "] = ";
	out << formatExpression(stmt->value.get(), config) << "\n";
	return out.str();
}

std::string FormattingProvider::formatMemberAssign(const MemberAssignStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << stmt->objectName << "." << stmt->memberName << " = ";
	out << formatExpression(stmt->value.get(), config) << "\n";
	return out.str();
}

std::string FormattingProvider::formatArrayMemberAssign(const ArrayMemberAssignStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << stmt->arrayName << "[";
	out << formatExpression(stmt->index.get(), config) << "]." << stmt->memberName << " = ";
	out << formatExpression(stmt->value.get(), config) << "\n";
	return out.str();
}

std::string FormattingProvider::formatMemberArrayAssign(const MemberArrayAssignStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << stmt->objectName << "." << stmt->memberName << "[";
	out << formatExpression(stmt->index.get(), config) << "] = ";
	out << formatExpression(stmt->value.get(), config) << "\n";
	return out.str();
}

std::string FormattingProvider::formatElseUnwrap(const ElseUnwrapStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << "var " << stmt->name;
	if (!stmt->declaredType.empty()) {
		out << " " << formatType(stmt->declaredType);
	}
	out << " = " << formatExpression(stmt->optionalExpr.get(), config);
	out << " else";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	// Else body
	for (const auto &s : stmt->elseBody) {
		out << formatStatement(s.get(), config, indentLevel + 1);
	}

	out << indent(indentLevel, config) << "end";
	if (!stmt->blockId.empty()) {
		out << " '" << stmt->blockId << "'";
	}
	out << "\n";

	return out.str();
}

std::string FormattingProvider::formatExprStmt(const ExprStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config);
	out << formatExpression(stmt->expression.get(), config) << "\n";
	return out.str();
}

std::string FormattingProvider::formatBreak(const BreakStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << "break";
	if (!stmt->targetLabel.empty()) {
		out << " '" << stmt->targetLabel << "'";
	}
	out << "\n";
	return out.str();
}

std::string FormattingProvider::formatContinue(const ContinueStmtAST *stmt, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;
	out << indent(indentLevel, config) << "continue";
	if (!stmt->targetLabel.empty()) {
		out << " '" << stmt->targetLabel << "'";
	}
	out << "\n";
	return out.str();
}

std::string FormattingProvider::formatExpression(const ExprAST *expr, const FormattingConfig &config) {
	if (!expr)
		return "";

	// Binary expression
	if (auto *binary = dynamic_cast<const BinaryExprAST *>(expr)) {
		return formatBinaryExpr(binary, config);
	}

	// Unary expression
	if (auto *unary = dynamic_cast<const UnaryExprAST *>(expr)) {
		return formatUnaryExpr(unary, config);
	}

	// Call expression
	if (auto *call = dynamic_cast<const CallExprAST *>(expr)) {
		return formatCall(call, config);
	}

	// Member access
	if (auto *member = dynamic_cast<const MemberAccessExprAST *>(expr)) {
		return formatMemberAccess(member, config);
	}

	// Array index
	if (auto *arrIdx = dynamic_cast<const ArrayIndexExprAST *>(expr)) {
		return formatArrayIndex(arrIdx, config);
	}

	// Array literal
	if (auto *arrLit = dynamic_cast<const ArrayLiteralExprAST *>(expr)) {
		return formatArrayLiteral(arrLit, config);
	}

	// Struct init
	if (auto *structInit = dynamic_cast<const StructInitExprAST *>(expr)) {
		return formatStructInit(structInit, config);
	}

	// Map literal
	if (auto *mapLit = dynamic_cast<const MapLiteralExprAST *>(expr)) {
		return formatMapLiteral(mapLit, config);
	}

	// Cast expression
	if (auto *cast = dynamic_cast<const CastExprAST *>(expr)) {
		return formatCast(cast, config);
	}

	// Match expression
	if (auto *matchExpr = dynamic_cast<const MatchExprAST *>(expr)) {
		return formatMatchExpr(matchExpr, config, 0);
	}

	// Number literal
	if (auto *num = dynamic_cast<const NumberExprAST *>(expr)) {
		return std::to_string(num->value);
	}

	// Byte literal
	if (auto *byteExpr = dynamic_cast<const ByteExprAST *>(expr)) {
		return std::to_string(byteExpr->value) + "b";
	}

	// Float literal
	if (auto *floatExpr = dynamic_cast<const FloatExprAST *>(expr)) {
		if (!floatExpr->literalString.empty()) {
			return floatExpr->literalString;
		}
		std::ostringstream ss;
		ss << floatExpr->value;
		std::string result = ss.str();
		// Ensure there's a decimal point
		if (result.find('.') == std::string::npos) {
			result += ".0";
		}
		return result;
	}

	// Variable
	if (auto *var = dynamic_cast<const VariableExprAST *>(expr)) {
		return var->name;
	}

	// Boolean
	if (auto *boolExpr = dynamic_cast<const BooleanExprAST *>(expr)) {
		return boolExpr->value ? "true" : "false";
	}

	// Character
	if (auto *charExpr = dynamic_cast<const CharacterExprAST *>(expr)) {
		return "'" + charExpr->value + "'";
	}

	// String literal
	if (auto *strExpr = dynamic_cast<const StringLiteralExprAST *>(expr)) {
		return "\"" + strExpr->value + "\"";
	}

	// Nil
	if (dynamic_cast<const NilExprAST *>(expr)) {
		return "nil";
	}

	// Enum case expression
	if (auto *enumCase = dynamic_cast<const EnumCaseExprAST *>(expr)) {
		std::ostringstream out;
		out << enumCase->enumName << "." << enumCase->caseName;
		if (!enumCase->arguments.empty()) {
			out << "(";
			for (size_t i = 0; i < enumCase->arguments.size(); i++) {
				if (i > 0)
					out << ", ";
				out << formatExpression(enumCase->arguments[i].get(), config);
			}
			out << ")";
		}
		return out.str();
	}

	// Slice expression
	if (auto *slice = dynamic_cast<const SliceExprAST *>(expr)) {
		std::ostringstream out;
		out << slice->objectName << "[";
		if (slice->start) {
			out << formatExpression(slice->start.get(), config);
		}
		out << "..";
		if (slice->end) {
			out << formatExpression(slice->end.get(), config);
		}
		out << "]";
		return out.str();
	}

	return "/* unknown expr */";
}

std::string FormattingProvider::formatBinaryExpr(const BinaryExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	std::string left = formatExpression(expr->left.get(), config);
	std::string right = formatExpression(expr->right.get(), config);
	std::string op = getOperatorString(expr->op);

	// Add spaces around binary operators
	out << left << " " << op << " " << right;

	return out.str();
}

std::string FormattingProvider::formatUnaryExpr(const UnaryExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	// No space after unary operators
	if (expr->op == 'n') { // 'not' operator
		out << "not " << formatExpression(expr->operand.get(), config);
	} else {
		out << expr->op << formatExpression(expr->operand.get(), config);
	}

	return out.str();
}

std::string FormattingProvider::formatCall(const CallExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	out << expr->callee << "(";
	for (size_t i = 0; i < expr->args.size(); i++) {
		if (i > 0)
			out << ", ";
		out << formatExpression(expr->args[i].value.get(), config);
	}
	out << ")";

	return out.str();
}

std::string FormattingProvider::formatMemberAccess(const MemberAccessExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	if (expr->object) {
		out << formatExpression(expr->object.get(), config);
	} else {
		out << expr->objectName;
	}
	out << "." << expr->memberName;

	return out.str();
}

std::string FormattingProvider::formatArrayIndex(const ArrayIndexExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	if (expr->hasArrayExpr()) {
		out << formatExpression(expr->arrayExpr.get(), config);
	} else {
		out << expr->arrayName;
	}
	out << "[" << formatExpression(expr->index.get(), config) << "]";

	return out.str();
}

std::string FormattingProvider::formatArrayLiteral(const ArrayLiteralExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	// Value-initialized array: [1, 2, 3]
	out << "[";
	for (size_t i = 0; i < expr->values.size(); i++) {
		if (i > 0)
			out << ", ";
		out << formatExpression(expr->values[i].get(), config);
	}
	out << "]";

	return out.str();
}

std::string FormattingProvider::formatStructInit(const StructInitExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	out << expr->structName << "{";
	for (size_t i = 0; i < expr->fields.size(); i++) {
		if (i > 0)
			out << ", ";
		out << expr->fields[i].name << ": ";
		out << formatExpression(expr->fields[i].value.get(), config);
	}
	out << "}";

	return out.str();
}

std::string FormattingProvider::formatMapLiteral(const MapLiteralExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	out << expr->dictType << " from " << formatType(expr->keyType);
	out << " to " << formatType(expr->valueType);

	return out.str();
}

std::string FormattingProvider::formatCast(const CastExprAST *expr, const FormattingConfig &config) {
	std::ostringstream out;

	out << formatExpression(expr->expr.get(), config);
	out << " as " << formatType(expr->targetType);

	return out.str();
}

std::string FormattingProvider::formatMatchExpr(const MatchExprAST *expr, const FormattingConfig &config, int indentLevel) {
	std::ostringstream out;

	out << "match " << formatExpression(expr->scrutinee.get(), config);
	if (!expr->blockId.empty()) {
		out << " '" << expr->blockId << "'";
	}
	out << "\n";

	for (const auto &caseItem : expr->cases) {
		out << indent(indentLevel + 1, config);

		if (caseItem.isDefault) {
			out << "default";
		} else {
			for (size_t i = 0; i < caseItem.patterns.size(); i++) {
				if (i > 0)
					out << " or ";
				out << formatExpression(caseItem.patterns[i].get(), config);
			}
		}

		out << " gives ";
		if (caseItem.resultExpr) {
			out << formatExpression(caseItem.resultExpr.get(), config);
		}
		out << "\n";
	}

	out << indent(indentLevel, config) << "end";
	if (!expr->blockId.empty()) {
		out << " '" << expr->blockId << "'";
	}

	return out.str();
}

std::string FormattingProvider::indent(int level, const FormattingConfig &config) {
	if (level <= 0)
		return "";

	std::string result;
	if (config.insertSpaces) {
		result = std::string(level * config.tabSize, ' ');
	} else {
		result = std::string(level, '\t');
	}
	return result;
}

std::string FormattingProvider::formatType(const std::string &type) {
	// Types are returned as-is, but this function exists for future enhancements
	return type;
}

std::string FormattingProvider::formatParameters(const std::vector<FunctionParameter> &params) {
	std::ostringstream out;
	for (size_t i = 0; i < params.size(); i++) {
		if (i > 0)
			out << ", ";
		out << params[i].name << " " << formatType(params[i].type);
	}
	return out.str();
}

std::string FormattingProvider::getOperatorString(char op) {
	switch (op) {
	case '+':
		return "+";
	case '-':
		return "-";
	case '*':
		return "*";
	case '/':
		return "/";
	case '%':
		return "mod";
	case '<':
		return "<";
	case '>':
		return ">";
	case '=':
		return "==";
	case '!':
		return "!=";
	case 'l':
		return "<="; // Less than or equal
	case 'g':
		return ">="; // Greater than or equal
	case '&':
		return "and";
	case '|':
		return "or";
	default:
		return std::string(1, op);
	}
}

std::vector<TextEdit> FormattingProvider::generateEdits(const std::string &original, const std::string &formatted) {
	std::vector<TextEdit> edits;

	// Split into lines
	std::vector<std::string> originalLines;
	std::vector<std::string> formattedLines;

	std::istringstream origStream(original);
	std::istringstream fmtStream(formatted);
	std::string line;

	while (std::getline(origStream, line)) {
		originalLines.push_back(line);
	}
	while (std::getline(fmtStream, line)) {
		formattedLines.push_back(line);
	}

	// Handle empty original
	if (originalLines.empty() && !formattedLines.empty()) {
		TextEdit edit;
		edit.range = Range(0, 0, 0, 0);
		edit.newText = formatted;
		return {edit};
	}

	// Simple line-by-line comparison
	// For a production implementation, we'd use a proper diff algorithm (Myers, patience diff, etc.)
	// This implementation generates a single edit that replaces the entire document if there are differences

	bool hasDifferences = false;
	if (originalLines.size() != formattedLines.size()) {
		hasDifferences = true;
	} else {
		for (size_t i = 0; i < originalLines.size(); i++) {
			if (originalLines[i] != formattedLines[i]) {
				hasDifferences = true;
				break;
			}
		}
	}

	if (hasDifferences) {
		// Generate a single edit that replaces the entire document
		// This is simpler and works well for full document formatting
		TextEdit edit;
		int lastLine = static_cast<int>(originalLines.size()) - 1;
		int lastChar = originalLines.empty() ? 0 : static_cast<int>(originalLines.back().size());
		if (lastLine < 0)
			lastLine = 0;
		edit.range = Range(0, 0, lastLine, lastChar);
		edit.newText = formatted;
		edits.push_back(edit);
	}

	return edits;
}

std::string FormattingProvider::formatLineByLine(const std::string &content, const FormattingConfig &config) {
	std::ostringstream out;
	std::istringstream in(content);
	std::string line;
	int indentLevel = 0;
	bool insideInterface = false;
	// Track match expressions that started inline (e.g., "return match ...")
	// so their end statement stays at the same indent level as the starting line
	std::vector<int> inlineMatchStartLevels;

	while (std::getline(in, line)) {
		// Trim leading/trailing whitespace
		size_t start = line.find_first_not_of(" \t");
		size_t end = line.find_last_not_of(" \t\r");

		if (start == std::string::npos) {
			// Empty line
			out << "\n";
			continue;
		}

		std::string trimmed = line.substr(start, end - start + 1);

		// Check if this line ends an interface (before adjusting indent)
		bool endsInterface = insideInterface && endsBlock(trimmed);

		// Adjust indent level for end statements
		if (endsBlock(trimmed)) {
			// Check if this end corresponds to an inline match expression
			if (!inlineMatchStartLevels.empty()) {
				// Pop the inline match and restore to its starting level
				int matchStartLevel = inlineMatchStartLevels.back();
				inlineMatchStartLevels.pop_back();
				indentLevel = matchStartLevel;
			} else {
				indentLevel = std::max(0, indentLevel - 1);
			}
			if (endsInterface) {
				insideInterface = false;
			}
		}

		// Output the line with proper indentation
		out << indent(indentLevel, config) << trimmed;

		// Trim trailing whitespace if configured
		if (config.trimTrailingWhitespace) {
			// Already trimmed above
		}

		out << "\n";

		// Check if this line starts an interface
		bool startsInterface = (trimmed.rfind("interface ", 0) == 0 || trimmed.rfind("export interface ", 0) == 0);

		// Check if this line starts an inline match expression (e.g., "return match ..." or "var x = match ...")
		bool startsInlineMatch = startsInlineMatchExpr(trimmed);

		// Adjust indent level for block starts
		// Inside an interface, function lines are just signatures, not block starts
		if (startsBlock(trimmed, insideInterface)) {
			if (startsInlineMatch) {
				// Track this as an inline match so the end stays at the same level
				inlineMatchStartLevels.push_back(indentLevel);
			}
			indentLevel++;
			if (startsInterface) {
				insideInterface = true;
			}
		}
	}

	std::string result = out.str();

	// Ensure final newline
	if (config.insertFinalNewline && !result.empty() && result.back() != '\n') {
		result += '\n';
	}

	return result;
}

std::string FormattingProvider::trimTrailingWhitespace(const std::string &line) {
	size_t end = line.find_last_not_of(" \t\r");
	if (end == std::string::npos) {
		return "";
	}
	return line.substr(0, end + 1);
}

int FormattingProvider::getLineIndentLevel(const std::string &line, const FormattingConfig &config) {
	int spaces = 0;
	for (char c : line) {
		if (c == ' ') {
			spaces++;
		} else if (c == '\t') {
			spaces += config.tabSize;
		} else {
			break;
		}
	}
	return spaces / config.tabSize;
}

bool FormattingProvider::startsBlock(const std::string &line, bool insideInterface) {
	// Remove leading/trailing whitespace
	size_t start = line.find_first_not_of(" \t");
	if (start == std::string::npos)
		return false;
	std::string trimmed = line.substr(start);

	// Check for block-starting keywords
	// These patterns indicate a new block that requires increased indentation

	// Function declaration (not extern)
	// Inside an interface, function lines are just method signatures without bodies
	if (trimmed.rfind("function ", 0) == 0 && trimmed.find("extern") == std::string::npos) {
		if (insideInterface) {
			return false; // Interface method signatures don't start blocks
		}
		return true;
	}

	// Export function
	if (trimmed.rfind("export function ", 0) == 0) {
		return true;
	}

	// Type declaration
	if (trimmed.rfind("type ", 0) == 0 || trimmed.rfind("export type ", 0) == 0) {
		return true;
	}

	// Enum declaration
	if (trimmed.rfind("enum ", 0) == 0 || trimmed.rfind("export enum ", 0) == 0) {
		return true;
	}

	// Interface declaration
	if (trimmed.rfind("interface ", 0) == 0 || trimmed.rfind("export interface ", 0) == 0) {
		return true;
	}

	// Control flow with block labels
	// if ... 'label'
	if (trimmed.rfind("if ", 0) == 0 && trimmed.find("'") != std::string::npos) {
		return true;
	}

	// while ... 'label'
	if (trimmed.rfind("while ", 0) == 0 && trimmed.find("'") != std::string::npos) {
		return true;
	}

	// for ... 'label'
	if (trimmed.rfind("for ", 0) == 0 && trimmed.find("'") != std::string::npos) {
		return true;
	}

	// match ... 'label' (standalone or inline after return/var/let/etc)
	if (trimmed.find("match ") != std::string::npos && trimmed.find("'") != std::string::npos) {
		return true;
	}

	// else 'label' - starts a new block after the if's then-block
	// Check for: else 'label' or end 'label' else 'label'
	if (trimmed.rfind("else ", 0) == 0 && trimmed.find("'") != std::string::npos) {
		return true;
	}

	// end 'label' else 'label' pattern - ends one block and starts another
	// The end part decrements, so we need to increment for the else part
	if (trimmed.find("end ") == 0 && trimmed.find(" else ") != std::string::npos) {
		return true;
	}

	return false;
}

bool FormattingProvider::endsBlock(const std::string &line) {
	// Remove leading/trailing whitespace
	size_t start = line.find_first_not_of(" \t");
	if (start == std::string::npos)
		return false;
	std::string trimmed = line.substr(start);

	// Check for end statement
	if (trimmed.rfind("end ", 0) == 0 || trimmed == "end") {
		return true;
	}

	return false;
}

bool FormattingProvider::startsInlineMatchExpr(const std::string &line) {
	// Check if this line contains a match expression that starts inline
	// (i.e., after "return", "var x =", "let x =", etc.)
	// These match expressions need special handling because their end statement
	// should stay at the same indentation level as the starting line

	size_t start = line.find_first_not_of(" \t");
	if (start == std::string::npos)
		return false;
	std::string trimmed = line.substr(start);

	// Look for "match " that's not at the start of the line
	// "return match ...", "var x = match ...", "let x = match ..."
	size_t matchPos = trimmed.find("match ");
	if (matchPos == std::string::npos)
		return false;

	// If match is at position 0, it's a standalone match statement, not inline
	if (matchPos == 0)
		return false;

	// Verify there's a label (required for match expressions)
	if (trimmed.find("'") == std::string::npos)
		return false;

	return true;
}

} // namespace maxon_lsp
