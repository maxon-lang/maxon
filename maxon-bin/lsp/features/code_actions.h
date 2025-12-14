#ifndef MAXON_LSP_CODE_ACTIONS_H
#define MAXON_LSP_CODE_ACTIONS_H

#include "../../compiler_api.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include <vector>

namespace maxon_lsp {

// Aliases for easier access to LSP types
using CodeAction = maxon::lsp::CodeAction;
using CodeActionContext = maxon::lsp::CodeActionContext;
using Diagnostic = maxon::lsp::Diagnostic;
using Range = maxon::lsp::Range;
using Position = maxon::lsp::Position;
using TextEdit = maxon::lsp::TextEdit;
using WorkspaceEdit = maxon::lsp::WorkspaceEdit;

/**
 * Provides code actions (quick fixes and refactorings) for Maxon source code.
 *
 * Supports:
 * - Quick fixes for diagnostics (undefined variables, type mismatches, etc.)
 * - Refactoring actions (extract variable, inline variable, etc.)
 * - Source actions (organize imports, fix all)
 */
class CodeActionsProvider {
  public:
	/**
	 * Get code actions for a range in a document.
	 *
	 * @param document The document to get actions for
	 * @param range The selected range (usually from a diagnostic)
	 * @param context The code action context containing diagnostics and filters
	 * @param cache Analysis cache containing symbols and type info
	 * @return Vector of available code actions
	 */
	std::vector<CodeAction> getCodeActions(
		const Document &document,
		const Range &range,
		const CodeActionContext &context,
		const AnalysisCache *cache);

  private:
	// =========================================================================
	// Category methods - generate actions by type
	// =========================================================================

	/**
	 * Get quick fix actions based on diagnostics.
	 *
	 * @param document The document containing the diagnostics
	 * @param diagnostics The diagnostics to generate fixes for
	 * @param cache Analysis cache for context
	 * @return Vector of quick fix code actions
	 */
	std::vector<CodeAction> getQuickFixes(
		const Document &document,
		const std::vector<Diagnostic> &diagnostics,
		const AnalysisCache *cache);

	/**
	 * Get refactoring actions based on the current selection.
	 *
	 * @param document The document containing the selection
	 * @param range The selected range
	 * @param cache Analysis cache for context
	 * @return Vector of refactoring code actions
	 */
	std::vector<CodeAction> getRefactorings(
		const Document &document,
		const Range &range,
		const AnalysisCache *cache);

	/**
	 * Get source-level actions (fix all, organize, etc.).
	 *
	 * @param document The document to generate actions for
	 * @param cache Analysis cache for context
	 * @return Vector of source code actions
	 */
	std::vector<CodeAction> getSourceActions(
		const Document &document,
		const AnalysisCache *cache);

	// =========================================================================
	// Quick fix generators - create fixes for specific error types
	// =========================================================================

	/**
	 * Create a fix for undefined variable errors.
	 * Suggests declaring the variable with var or let.
	 */
	CodeAction createUndefinedVariableFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &varName);

	/**
	 * Create a fix for type mismatch errors.
	 * Suggests adding an explicit cast with 'as'.
	 */
	CodeAction createTypeMismatchFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &fromType,
		const std::string &toType);

	/**
	 * Create a fix for unused variable warnings.
	 * Offers to remove the declaration or prefix with _.
	 */
	CodeAction createUnusedVariableFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &varName);

	/**
	 * Create a fix to remove unused variable declarations.
	 * Deletes the entire line containing the declaration.
	 */
	CodeAction createRemoveUnusedVariableFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &varName);

	/**
	 * Create a fix for missing end label errors.
	 * Inserts the appropriate end 'label' statement.
	 */
	CodeAction createMissingEndLabelFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &labelName);

	/**
	 * Create a fix for missing return statement errors.
	 * Inserts a return statement with a default value.
	 */
	CodeAction createMissingReturnFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &returnType);

	/**
	 * Create a fix for spelling/typo errors.
	 * Replaces with the suggested correct spelling.
	 */
	CodeAction createSpellingFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &suggested);

	/**
	 * Create a fix for read-only variable assignment errors.
	 * Changes 'let' to 'var' at the declaration site.
	 */
	CodeAction createChangeLetToVarFix(
		const Document &document,
		const Diagnostic &diag,
		const std::string &varName,
		int declarationLine,
		int declarationColumn);

	// =========================================================================
	// Refactoring generators - create refactoring actions
	// =========================================================================

	/**
	 * Create an extract variable refactoring.
	 * Extracts the selected expression into a new variable.
	 */
	CodeAction createExtractVariableRefactoring(
		const Document &document,
		const Range &range);

	/**
	 * Create an inline variable refactoring.
	 * Replaces variable references with the variable's value.
	 */
	CodeAction createInlineVariableRefactoring(
		const Document &document,
		const Range &range,
		const AnalysisCache *cache);

	/**
	 * Create an add type annotation refactoring.
	 * Adds explicit type annotation to a variable declaration.
	 */
	CodeAction createAddTypeAnnotationRefactoring(
		const Document &document,
		const Range &range,
		const AnalysisCache *cache);

	/**
	 * Create a convert var to let refactoring.
	 * Changes a var declaration to let if the variable is never reassigned.
	 */
	CodeAction createConvertVarToLetRefactoring(
		const Document &document,
		const Range &range);

	// =========================================================================
	// Helper methods
	// =========================================================================

	/**
	 * Extract an identifier name from a diagnostic message.
	 * Parses error messages to find the relevant identifier.
	 */
	std::string extractIdentifierFromDiagnostic(const Diagnostic &diag);

	/**
	 * Extract source and target types from a type mismatch diagnostic.
	 *
	 * @param diag The diagnostic to parse
	 * @param fromType Output: the source type
	 * @param toType Output: the target type
	 * @return true if types were successfully extracted
	 */
	bool extractTypesFromDiagnostic(
		const Diagnostic &diag,
		std::string &fromType,
		std::string &toType);

	/**
	 * Create a TextEdit for a given range and replacement text.
	 */
	TextEdit createTextEdit(const Range &range, const std::string &newText);

	/**
	 * Create a WorkspaceEdit with edits for a single document.
	 */
	WorkspaceEdit createWorkspaceEdit(
		const std::string &uri,
		const std::vector<TextEdit> &edits);

	/**
	 * Find identifiers similar to the given name.
	 * Used for did-you-mean suggestions.
	 *
	 * @param name The misspelled name
	 * @param cache Analysis cache containing known symbols
	 * @return Vector of similar identifier names
	 */
	std::vector<std::string> findSimilarIdentifiers(
		const std::string &name,
		const AnalysisCache *cache);

	/**
	 * Calculate the Levenshtein (edit) distance between two strings.
	 * Used for finding similar identifiers.
	 */
	int levenshteinDistance(const std::string &a, const std::string &b);

	/**
	 * Check if a code action kind matches the requested filter.
	 * Handles hierarchical kind matching (e.g., "quickfix" matches "quickfix.foo").
	 */
	bool matchesKindFilter(
		const std::string &kind,
		const std::vector<std::string> &filter);

	/**
	 * Get the text at a specific line in the document.
	 */
	std::string getLineText(const Document &document, int line);

	/**
	 * Get the text within a range in the document.
	 */
	std::string getRangeText(const Document &document, const Range &range);

	/**
	 * Find the indentation at a given line.
	 */
	std::string getIndentation(const Document &document, int line);

	/**
	 * Get a default value for a given type (for return statement fixes).
	 */
	std::string getDefaultValueForType(const std::string &type);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_CODE_ACTIONS_H
