#include "code_actions.h"
#include <algorithm>
#include <regex>
#include <sstream>

namespace maxon_lsp {

// Use LSP type namespace
namespace CodeActionKind = maxon::lsp::CodeActionKind;

std::vector<CodeAction> CodeActionsProvider::getCodeActions(
	const Document &document,
	const Range &range,
	const CodeActionContext &context,
	const AnalysisCache *cache) {
	std::vector<CodeAction> actions;

	// Determine which kinds of actions to include based on context.only filter
	bool includeQuickFix = true;
	bool includeRefactor = true;
	bool includeSource = true;

	if (context.only.has_value() && !context.only->empty()) {
		includeQuickFix = false;
		includeRefactor = false;
		includeSource = false;

		for (const auto &kind : *context.only) {
			if (kind == CodeActionKind::QuickFix || kind.find(CodeActionKind::QuickFix) == 0) {
				includeQuickFix = true;
			}
			if (kind == CodeActionKind::Refactor || kind.find(CodeActionKind::Refactor) == 0) {
				includeRefactor = true;
			}
			if (kind == CodeActionKind::Source || kind.find(CodeActionKind::Source) == 0) {
				includeSource = true;
			}
		}
	}

	// Get quick fixes from diagnostics
	if (includeQuickFix && !context.diagnostics.empty()) {
		auto fixes = getQuickFixes(document, context.diagnostics, cache);
		actions.insert(actions.end(), fixes.begin(), fixes.end());
	}

	// Get refactoring actions based on selection
	if (includeRefactor && !range.isEmpty()) {
		auto refactorings = getRefactorings(document, range, cache);
		actions.insert(actions.end(), refactorings.begin(), refactorings.end());
	}

	// Get source actions
	if (includeSource) {
		auto sourceActions = getSourceActions(document, cache);
		actions.insert(actions.end(), sourceActions.begin(), sourceActions.end());
	}

	return actions;
}

std::vector<CodeAction> CodeActionsProvider::getQuickFixes(
	const Document &document,
	const std::vector<Diagnostic> &diagnostics,
	const AnalysisCache *cache) {
	std::vector<CodeAction> fixes;

	for (const auto &diag : diagnostics) {
		const std::string &msg = diag.message;

		// Check for undefined variable errors
		// Pattern: "Undefined variable 'name'" or "Undefined variable: name"
		if (msg.find("Undefined variable") != std::string::npos ||
			msg.find("undefined variable") != std::string::npos) {
			std::string varName = extractIdentifierFromDiagnostic(diag);
			if (!varName.empty()) {
				fixes.push_back(createUndefinedVariableFix(document, diag, varName));

				// Also check for similar identifiers (did-you-mean)
				if (cache) {
					auto similar = findSimilarIdentifiers(varName, cache);
					for (const auto &suggestion : similar) {
						fixes.push_back(createSpellingFix(document, diag, suggestion));
					}
				}
			}
		}

		// Check for undefined function errors
		if (msg.find("Undefined function") != std::string::npos ||
			msg.find("undefined function") != std::string::npos) {
			std::string funcName = extractIdentifierFromDiagnostic(diag);
			if (!funcName.empty() && cache) {
				auto similar = findSimilarIdentifiers(funcName, cache);
				for (const auto &suggestion : similar) {
					fixes.push_back(createSpellingFix(document, diag, suggestion));
				}
			}
		}

		// Check for type mismatch errors
		// Pattern: "Type mismatch: expected 'X', got 'Y'" or similar
		if (msg.find("Type mismatch") != std::string::npos ||
			msg.find("type mismatch") != std::string::npos ||
			msg.find("Cannot assign") != std::string::npos) {
			std::string fromType, toType;
			if (extractTypesFromDiagnostic(diag, fromType, toType)) {
				fixes.push_back(createTypeMismatchFix(document, diag, fromType, toType));
			}
		}

		// Check for unused variable warnings
		// Pattern: "Variable 'name' is declared but never used"
		if (msg.find("never used") != std::string::npos ||
			msg.find("unused variable") != std::string::npos ||
			msg.find("Unused variable") != std::string::npos) {
			std::string varName = extractIdentifierFromDiagnostic(diag);
			if (!varName.empty()) {
				fixes.push_back(createUnusedVariableFix(document, diag, varName));
				fixes.push_back(createRemoveUnusedVariableFix(document, diag, varName));
			}
		}

		// Check for missing end label errors
		// Pattern: "Expected 'end 'label''" or "Missing end label"
		if (msg.find("Expected end") != std::string::npos ||
			msg.find("expected end") != std::string::npos ||
			msg.find("Missing end") != std::string::npos ||
			msg.find("missing end") != std::string::npos) {
			// Try to extract the expected label
			std::regex labelRegex("'([^']+)'");
			std::smatch match;
			if (std::regex_search(msg, match, labelRegex) && match.size() > 1) {
				std::string label = match[1].str();
				fixes.push_back(createMissingEndLabelFix(document, diag, label));
			}
		}

		// Check for missing return statement errors
		// Pattern: "Function 'name' must return a value of type 'X'"
		if (msg.find("must return") != std::string::npos ||
			msg.find("Missing return") != std::string::npos ||
			msg.find("missing return") != std::string::npos) {
			// Extract the return type
			std::regex typeRegex("type '([^']+)'");
			std::smatch match;
			if (std::regex_search(msg, match, typeRegex) && match.size() > 1) {
				std::string returnType = match[1].str();
				fixes.push_back(createMissingReturnFix(document, diag, returnType));
			}
		}
	}

	return fixes;
}

std::vector<CodeAction> CodeActionsProvider::getRefactorings(
	const Document &document,
	const Range &range,
	const AnalysisCache *cache) {
	std::vector<CodeAction> refactorings;

	// Only offer refactorings if there's a non-empty selection
	if (range.isEmpty()) {
		return refactorings;
	}

	std::string selectedText = getRangeText(document, range);
	if (selectedText.empty()) {
		return refactorings;
	}

	// Extract variable: offer if selection looks like an expression
	// Avoid offering for single keywords, whitespace, etc.
	bool looksLikeExpression = false;
	if (!selectedText.empty()) {
		// Simple heuristic: contains alphanumeric chars and isn't just a keyword
		bool hasAlpha = false;
		for (char c : selectedText) {
			if (std::isalnum(c)) {
				hasAlpha = true;
				break;
			}
		}
		// Check it's not just whitespace or a simple keyword
		std::string trimmed = selectedText;
		trimmed.erase(0, trimmed.find_first_not_of(" \t\n\r"));
		trimmed.erase(trimmed.find_last_not_of(" \t\n\r") + 1);

		if (hasAlpha && !trimmed.empty() &&
			trimmed != "if" && trimmed != "else" && trimmed != "while" &&
			trimmed != "for" && trimmed != "function" && trimmed != "struct" &&
			trimmed != "end" && trimmed != "return" && trimmed != "var" && trimmed != "let") {
			looksLikeExpression = true;
		}
	}

	if (looksLikeExpression) {
		refactorings.push_back(createExtractVariableRefactoring(document, range));
	}

	// Check if selection is on a variable name for inline/convert refactorings
	if (cache) {
		std::string lineText = getLineText(document, range.start.line);

		// Check for var/let declarations on this line
		if (lineText.find("var ") != std::string::npos) {
			refactorings.push_back(createConvertVarToLetRefactoring(document, range));
		}

		// Check if we're on a variable that could be inlined
		std::string potentialVar = selectedText;
		potentialVar.erase(0, potentialVar.find_first_not_of(" \t\n\r"));
		potentialVar.erase(potentialVar.find_last_not_of(" \t\n\r") + 1);

		auto varIt = cache->variables.find(potentialVar);
		if (varIt != cache->variables.end()) {
			refactorings.push_back(createInlineVariableRefactoring(document, range, cache));
		}

		// Check for type annotation refactoring
		// Look for declarations without explicit types
		std::regex declRegex(R"((var|let)\s+\w+\s*=)");
		if (std::regex_search(lineText, declRegex)) {
			refactorings.push_back(createAddTypeAnnotationRefactoring(document, range, cache));
		}
	}

	return refactorings;
}

std::vector<CodeAction> CodeActionsProvider::getSourceActions(
	const Document &document,
	const AnalysisCache *cache) {
	std::vector<CodeAction> actions;

	// Source actions are typically document-wide operations
	// For now, we don't have any Maxon-specific source actions
	// This could include things like:
	// - Organize imports (when Maxon supports them)
	// - Fix all diagnostics
	// - Format document

	return actions;
}

CodeAction CodeActionsProvider::createUndefinedVariableFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &varName) {
	CodeAction action;
	action.title = "Declare variable '" + varName + "' with var";
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};

	// Find the line where the error occurred
	int line = diag.range.start.line;
	std::string indent = getIndentation(document, line);

	// Insert a variable declaration before the current line
	std::string insertText = indent + "var " + varName + " = \n";

	TextEdit edit;
	edit.range = Range(line, 0, line, 0);
	edit.newText = insertText;

	action.edit = createWorkspaceEdit(document.uri, {edit});

	return action;
}

CodeAction CodeActionsProvider::createTypeMismatchFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &fromType,
	const std::string &toType) {
	CodeAction action;
	action.title = "Add cast to '" + toType + "'";
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};

	// Get the text at the diagnostic range
	std::string exprText = getRangeText(document, diag.range);

	// Wrap the expression with 'as Type'
	std::string newText = "(" + exprText + " as " + toType + ")";

	TextEdit edit = createTextEdit(diag.range, newText);
	action.edit = createWorkspaceEdit(document.uri, {edit});

	return action;
}

CodeAction CodeActionsProvider::createUnusedVariableFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &varName) {
	CodeAction action;
	action.title = "Prefix with underscore to ignore: _" + varName;
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};

	// Find the variable name in the line and replace it
	int line = diag.range.start.line;
	std::string lineText = getLineText(document, line);

	// Find where the variable is declared
	size_t varPos = lineText.find(varName);
	if (varPos != std::string::npos) {
		// Make sure it's the declaration (after var/let)
		size_t declPos = lineText.find("var ");
		if (declPos == std::string::npos) {
			declPos = lineText.find("let ");
		}

		if (declPos != std::string::npos && varPos > declPos) {
			Range replaceRange(line, static_cast<int>(varPos),
							   line, static_cast<int>(varPos + varName.length()));
			TextEdit edit = createTextEdit(replaceRange, "_" + varName);
			action.edit = createWorkspaceEdit(document.uri, {edit});
		}
	}

	return action;
}

CodeAction CodeActionsProvider::createRemoveUnusedVariableFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &varName) {
	CodeAction action;
	action.title = "Remove unused variable '" + varName + "'";
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};

	// Delete the entire line containing the unused variable
	int line = diag.range.start.line;
	std::string lineText = getLineText(document, line);

	// Make sure it's a variable declaration line
	if (lineText.find("var ") != std::string::npos ||
		lineText.find("let ") != std::string::npos) {
		// Delete from start of line to start of next line
		Range deleteRange(line, 0, line + 1, 0);
		TextEdit edit = createTextEdit(deleteRange, "");
		action.edit = createWorkspaceEdit(document.uri, {edit});
	}

	return action;
}

CodeAction CodeActionsProvider::createMissingEndLabelFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &labelName) {
	CodeAction action;
	action.title = "Add end '" + labelName + "'";
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};
	action.isPreferred = true;

	// Insert the end label at the error position
	int line = diag.range.end.line;
	std::string indent = getIndentation(document, line);

	// Insert on a new line after the current position
	std::string insertText = "\n" + indent + "end '" + labelName + "'";

	TextEdit edit;
	edit.range = Range(line, static_cast<int>(getLineText(document, line).length()),
					   line, static_cast<int>(getLineText(document, line).length()));
	edit.newText = insertText;

	action.edit = createWorkspaceEdit(document.uri, {edit});

	return action;
}

CodeAction CodeActionsProvider::createMissingReturnFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &returnType) {
	CodeAction action;
	action.title = "Add return statement";
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};

	int line = diag.range.start.line;
	std::string indent = getIndentation(document, line);

	// Get a default value for the return type
	std::string defaultValue = getDefaultValueForType(returnType);

	// Insert a return statement
	std::string insertText = indent + "    return " + defaultValue + "\n";

	TextEdit edit;
	edit.range = Range(line, 0, line, 0);
	edit.newText = insertText;

	action.edit = createWorkspaceEdit(document.uri, {edit});

	return action;
}

CodeAction CodeActionsProvider::createSpellingFix(
	const Document &document,
	const Diagnostic &diag,
	const std::string &suggested) {
	CodeAction action;
	action.title = "Did you mean '" + suggested + "'?";
	action.kind = CodeActionKind::QuickFix;
	action.diagnostics = std::vector<Diagnostic>{diag};

	TextEdit edit = createTextEdit(diag.range, suggested);
	action.edit = createWorkspaceEdit(document.uri, {edit});

	return action;
}

CodeAction CodeActionsProvider::createExtractVariableRefactoring(
	const Document &document,
	const Range &range) {
	CodeAction action;
	action.title = "Extract to variable";
	action.kind = CodeActionKind::RefactorExtract;

	std::string selectedText = getRangeText(document, range);
	std::string indent = getIndentation(document, range.start.line);

	// Create edits: replace selection with variable name, insert declaration before
	std::vector<TextEdit> edits;

	// Insert the variable declaration before the current line
	TextEdit declEdit;
	declEdit.range = Range(range.start.line, 0, range.start.line, 0);
	declEdit.newText = indent + "let extracted = " + selectedText + "\n";
	edits.push_back(declEdit);

	// Replace the selected expression with the variable name
	TextEdit replaceEdit = createTextEdit(range, "extracted");
	edits.push_back(replaceEdit);

	action.edit = createWorkspaceEdit(document.uri, edits);

	return action;
}

CodeAction CodeActionsProvider::createInlineVariableRefactoring(
	const Document &document,
	const Range &range,
	const AnalysisCache *cache) {
	CodeAction action;
	action.title = "Inline variable";
	action.kind = CodeActionKind::RefactorInline;

	// This is a placeholder - full implementation would need to:
	// 1. Find the variable's initial value
	// 2. Find all usages of the variable
	// 3. Replace usages with the value
	// 4. Remove the declaration

	// For now, just mark the action as disabled
	action.disabled = maxon::lsp::CodeActionDisabled{"Variable inlining not yet fully implemented"};

	return action;
}

CodeAction CodeActionsProvider::createAddTypeAnnotationRefactoring(
	const Document &document,
	const Range &range,
	const AnalysisCache *cache) {
	CodeAction action;
	action.title = "Add explicit type annotation";
	action.kind = CodeActionKind::RefactorRewrite;

	// This would require knowing the inferred type
	// For now, mark as disabled
	action.disabled = maxon::lsp::CodeActionDisabled{"Type inference needed for annotation"};

	return action;
}

CodeAction CodeActionsProvider::createConvertVarToLetRefactoring(
	const Document &document,
	const Range &range) {
	CodeAction action;
	action.title = "Convert to immutable (let)";
	action.kind = CodeActionKind::RefactorRewrite;

	int line = range.start.line;
	std::string lineText = getLineText(document, line);

	// Find 'var' and replace with 'let'
	size_t varPos = lineText.find("var ");
	if (varPos != std::string::npos) {
		Range replaceRange(line, static_cast<int>(varPos),
						   line, static_cast<int>(varPos + 3));
		TextEdit edit = createTextEdit(replaceRange, "let");
		action.edit = createWorkspaceEdit(document.uri, {edit});
	} else {
		action.disabled = maxon::lsp::CodeActionDisabled{"No var declaration found"};
	}

	return action;
}

std::string CodeActionsProvider::extractIdentifierFromDiagnostic(const Diagnostic &diag) {
	const std::string &msg = diag.message;

	// Try to extract identifier from quotes: 'identifier' or "identifier"
	std::regex singleQuoteRegex("'([a-zA-Z_][a-zA-Z0-9_]*)'");
	std::regex doubleQuoteRegex("\"([a-zA-Z_][a-zA-Z0-9_]*)\"");

	std::smatch match;
	if (std::regex_search(msg, match, singleQuoteRegex) && match.size() > 1) {
		return match[1].str();
	}
	if (std::regex_search(msg, match, doubleQuoteRegex) && match.size() > 1) {
		return match[1].str();
	}

	// Try pattern: "identifier: message" or "Undefined identifier"
	std::regex colonRegex("([a-zA-Z_][a-zA-Z0-9_]*):");
	if (std::regex_search(msg, match, colonRegex) && match.size() > 1) {
		std::string candidate = match[1].str();
		// Skip common error message prefixes
		if (candidate != "Error" && candidate != "Warning" && candidate != "Type") {
			return candidate;
		}
	}

	return "";
}

bool CodeActionsProvider::extractTypesFromDiagnostic(
	const Diagnostic &diag,
	std::string &fromType,
	std::string &toType) {
	const std::string &msg = diag.message;

	// Pattern: "expected 'X', got 'Y'" or "expected X, got Y"
	std::regex expectedGotRegex(R"(expected\s+'?([^',]+)'?,\s*got\s+'?([^',]+)'?)");
	std::smatch match;
	if (std::regex_search(msg, match, expectedGotRegex) && match.size() > 2) {
		toType = match[1].str();
		fromType = match[2].str();
		return true;
	}

	// Pattern: "Cannot assign 'X' to 'Y'"
	std::regex assignRegex(R"(Cannot assign\s+'([^']+)'\s+to\s+'([^']+)')");
	if (std::regex_search(msg, match, assignRegex) && match.size() > 2) {
		fromType = match[1].str();
		toType = match[2].str();
		return true;
	}

	// Pattern: "Type mismatch: X vs Y"
	std::regex vsRegex(R"(Type mismatch:\s*'?([^']+?)'?\s+vs\s+'?([^']+?)'?)");
	if (std::regex_search(msg, match, vsRegex) && match.size() > 2) {
		fromType = match[1].str();
		toType = match[2].str();
		return true;
	}

	return false;
}

TextEdit CodeActionsProvider::createTextEdit(const Range &range, const std::string &newText) {
	TextEdit edit;
	edit.range = range;
	edit.newText = newText;
	return edit;
}

WorkspaceEdit CodeActionsProvider::createWorkspaceEdit(
	const std::string &uri,
	const std::vector<TextEdit> &edits) {
	WorkspaceEdit wsEdit;
	wsEdit.changes = std::map<std::string, std::vector<TextEdit>>();
	(*wsEdit.changes)[uri] = edits;
	return wsEdit;
}

std::vector<std::string> CodeActionsProvider::findSimilarIdentifiers(
	const std::string &name,
	const AnalysisCache *cache) {
	std::vector<std::string> similar;
	if (!cache) {
		return similar;
	}

	const int maxDistance = 2; // Maximum edit distance for suggestions

	// Check variables
	for (const auto &[varName, varInfo] : cache->variables) {
		int dist = levenshteinDistance(name, varName);
		if (dist > 0 && dist <= maxDistance) {
			similar.push_back(varName);
		}
	}

	// Check functions
	for (const auto &[funcName, funcInfo] : cache->functions) {
		int dist = levenshteinDistance(name, funcName);
		if (dist > 0 && dist <= maxDistance) {
			similar.push_back(funcName);
		}
	}

	// Check structs
	for (const auto &[structName, structInfo] : cache->structs) {
		int dist = levenshteinDistance(name, structName);
		if (dist > 0 && dist <= maxDistance) {
			similar.push_back(structName);
		}
	}

	// Limit to top 3 suggestions
	if (similar.size() > 3) {
		similar.resize(3);
	}

	return similar;
}

int CodeActionsProvider::levenshteinDistance(const std::string &a, const std::string &b) {
	const size_t m = a.size();
	const size_t n = b.size();

	if (m == 0)
		return static_cast<int>(n);
	if (n == 0)
		return static_cast<int>(m);

	// Use two rows instead of full matrix for memory efficiency
	std::vector<int> prev(n + 1);
	std::vector<int> curr(n + 1);

	// Initialize first row
	for (size_t j = 0; j <= n; ++j) {
		prev[j] = static_cast<int>(j);
	}

	for (size_t i = 1; i <= m; ++i) {
		curr[0] = static_cast<int>(i);

		for (size_t j = 1; j <= n; ++j) {
			int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
			curr[j] = std::min({
				prev[j] + 1,	   // deletion
				curr[j - 1] + 1,   // insertion
				prev[j - 1] + cost // substitution
			});
		}

		std::swap(prev, curr);
	}

	return prev[n];
}

bool CodeActionsProvider::matchesKindFilter(
	const std::string &kind,
	const std::vector<std::string> &filter) {
	if (filter.empty()) {
		return true;
	}

	for (const auto &f : filter) {
		// Exact match
		if (kind == f) {
			return true;
		}
		// Hierarchical match: "quickfix" matches "quickfix.foo"
		if (kind.find(f + ".") == 0 || f.find(kind + ".") == 0) {
			return true;
		}
		// Parent match: "quickfix" matches when filter is "quickfix.extract"
		if (f.find(kind) == 0) {
			return true;
		}
	}

	return false;
}

std::string CodeActionsProvider::getLineText(const Document &document, int line) {
	if (line < 0 || line >= document.getLineCount()) {
		return "";
	}
	return document.getLine(line);
}

std::string CodeActionsProvider::getRangeText(const Document &document, const Range &range) {
	if (range.start.line < 0 || range.end.line >= document.getLineCount()) {
		return "";
	}

	if (range.start.line == range.end.line) {
		// Single line selection
		std::string line = document.getLine(range.start.line);
		int start = std::max(0, range.start.character);
		int end = std::min(static_cast<int>(line.length()), range.end.character);
		if (start < end) {
			return line.substr(start, end - start);
		}
		return "";
	}

	// Multi-line selection
	std::stringstream ss;

	// First line (from start character to end of line)
	std::string firstLine = document.getLine(range.start.line);
	int start = std::max(0, range.start.character);
	if (start < static_cast<int>(firstLine.length())) {
		ss << firstLine.substr(start);
	}
	ss << "\n";

	// Middle lines (complete lines)
	for (int i = range.start.line + 1; i < range.end.line; ++i) {
		ss << document.getLine(i) << "\n";
	}

	// Last line (from beginning to end character)
	if (range.end.line > range.start.line) {
		std::string lastLine = document.getLine(range.end.line);
		int end = std::min(static_cast<int>(lastLine.length()), range.end.character);
		ss << lastLine.substr(0, end);
	}

	return ss.str();
}

std::string CodeActionsProvider::getIndentation(const Document &document, int line) {
	std::string lineText = getLineText(document, line);
	std::string indent;

	for (char c : lineText) {
		if (c == ' ' || c == '\t') {
			indent += c;
		} else {
			break;
		}
	}

	return indent;
}

std::string CodeActionsProvider::getDefaultValueForType(const std::string &type) {
	if (type == "int") {
		return "0";
	} else if (type == "float") {
		return "0.0";
	} else if (type == "bool") {
		return "false";
	} else if (type == "string") {
		return "\"\"";
	} else if (type == "byte") {
		return "0";
	} else if (type == "char") {
		return "'\\0'";
	} else if (type.front() == '[') {
		// Array type
		return "[]";
	} else if (type.find("?") != std::string::npos) {
		// Optional type
		return "nil";
	} else {
		// Struct or unknown type - return a placeholder
		return "/* TODO: provide value */";
	}
}

} // namespace maxon_lsp
