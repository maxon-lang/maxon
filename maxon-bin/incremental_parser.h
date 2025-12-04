#ifndef INCREMENTAL_PARSER_H
#define INCREMENTAL_PARSER_H

/**
 * Incremental Parser
 *
 * Provides infrastructure for incremental parsing when only part of a document
 * changes. This is used by the LSP server to efficiently update the AST without
 * re-parsing the entire file.
 *
 * Key features:
 * - EditRegion struct to describe text changes
 * - Finding affected declarations based on source ranges
 * - Incremental token stream updates
 * - Partial AST re-parsing
 */

#include "ast.h"
#include "lexer.h"
#include "parser.h"
#include "parser_support.h"
#include "token_stream.h"
#include <memory>
#include <string>
#include <vector>

/**
 * EditRegion describes a text change in the source document.
 * Positions are 1-based line numbers and 0-based column offsets within the line.
 */
struct EditRegion {
	int startLine;    // Start line (1-based)
	int startCol;     // Start column (0-based offset within line)
	int endLine;      // End line (1-based)
	int endCol;       // End column (0-based offset within line)
	std::string newText;  // The new text that replaces the range

	EditRegion() : startLine(0), startCol(0), endLine(0), endCol(0) {}

	EditRegion(int sLine, int sCol, int eLine, int eCol, const std::string &text)
		: startLine(sLine), startCol(sCol), endLine(eLine), endCol(eCol), newText(text) {}

	// Convert to SourceRange for overlap checking
	SourceRange toSourceRange() const {
		return SourceRange(startLine, startCol, endLine, endCol);
	}

	// Check if this edit is a simple insertion (zero-width range)
	bool isInsertion() const {
		return startLine == endLine && startCol == endCol;
	}

	// Check if this edit is a pure deletion (empty newText)
	bool isDeletion() const {
		return newText.empty() && !isInsertion();
	}

	// Calculate how many lines this edit adds or removes
	int lineDelta() const {
		int removedLines = endLine - startLine;
		int addedLines = 0;
		for (char c : newText) {
			if (c == '\n') {
				addedLines++;
			}
		}
		return addedLines - removedLines;
	}
};

/**
 * Information about an affected declaration that needs re-parsing
 */
struct AffectedDeclaration {
	enum class Kind {
		Function,
		Struct,
		Enum,
		Interface
	};

	Kind kind;
	std::string name;
	size_t index;         // Index in the respective vector in ProgramAST
	SourceRange range;    // Original source range
};

/**
 * Result of incremental parsing
 */
struct IncrementalParseResult {
	std::unique_ptr<ProgramAST> ast;  // The updated AST
	bool wasFullReparse;              // True if a full reparse was required
	std::vector<std::string> errors;  // Any errors encountered during parsing

	// Statistics for performance monitoring
	size_t tokensRetokenized;
	size_t declarationsReparsed;
	size_t declarationsPreserved;
};

/**
 * IncrementalParser manages incremental updates to the AST when the source changes.
 */
class IncrementalParser {
public:
	IncrementalParser() = default;

	/**
	 * Find which top-level declarations in the AST are affected by an edit.
	 * Uses SourceRange::overlaps() to find overlapping declarations.
	 *
	 * @param ast The current AST
	 * @param edit The edit region describing the change
	 * @return Vector of affected declarations with their indices
	 */
	std::vector<AffectedDeclaration> findAffectedDeclarations(
		const ProgramAST *ast, const EditRegion &edit) const;

	/**
	 * Find which statements within a function are affected by an edit.
	 * Used for more fine-grained incremental updates within functions.
	 *
	 * @param func The function to check
	 * @param edit The edit region describing the change
	 * @return Vector of statement indices (0-based) that need re-parsing
	 */
	std::vector<size_t> findAffectedStatements(
		const FunctionAST *func, const EditRegion &edit) const;

	/**
	 * Perform an incremental update of the AST based on an edit.
	 *
	 * This method:
	 * 1. Applies the edit to get new source text
	 * 2. Re-tokenizes only the affected region
	 * 3. Splices new tokens into the token stream
	 * 4. Re-parses only affected declarations
	 * 5. Returns a new AST with unaffected nodes preserved
	 *
	 * @param oldAST The current AST (ownership transferred on success)
	 * @param oldTokens The current token stream (will be modified in place)
	 * @param oldSource The current source text
	 * @param edit The edit to apply
	 * @param defaultNamespace The namespace to use for parsing
	 * @return The updated AST and parsing statistics
	 */
	IncrementalParseResult update(
		std::unique_ptr<ProgramAST> oldAST,
		TokenStream &oldTokens,
		const std::string &oldSource,
		const EditRegion &edit,
		const std::string &defaultNamespace = "");

	/**
	 * Check if an edit is "simple" enough to handle incrementally.
	 * Complex edits (spanning many lines or declarations) may require full reparse.
	 *
	 * @param ast The current AST
	 * @param edit The edit to check
	 * @return True if the edit can be handled incrementally
	 */
	bool canHandleIncrementally(const ProgramAST *ast, const EditRegion &edit) const;

	/**
	 * Apply an edit to source text to get the new source.
	 *
	 * @param source The original source text
	 * @param edit The edit to apply
	 * @return The new source text after applying the edit
	 */
	static std::string applyEdit(const std::string &source, const EditRegion &edit);

	/**
	 * Convert a line/column position to a byte offset in the source.
	 *
	 * @param source The source text
	 * @param line Line number (1-based)
	 * @param col Column offset (0-based)
	 * @return Byte offset into the source string
	 */
	static size_t positionToOffset(const std::string &source, int line, int col);

	/**
	 * Get the lines of source that need to be retokenized for an edit.
	 * This expands the edit region to token boundaries to ensure valid tokenization.
	 *
	 * @param tokens The current token stream
	 * @param edit The edit being applied
	 * @return Pair of (startLine, endLine) for retokenization
	 */
	static std::pair<int, int> getRetokenizationRange(
		const TokenStream &tokens, const EditRegion &edit);

private:
	// Maximum number of affected declarations before falling back to full reparse
	static constexpr size_t MAX_AFFECTED_DECLARATIONS = 5;

	// Maximum edit size (in lines) before falling back to full reparse
	static constexpr int MAX_INCREMENTAL_EDIT_LINES = 50;

	/**
	 * Extract the portion of source text for a given line range.
	 *
	 * @param source The full source text
	 * @param startLine First line to extract (1-based)
	 * @param endLine Last line to extract (1-based, inclusive)
	 * @return The extracted source text including line endings
	 */
	static std::string extractLines(const std::string &source, int startLine, int endLine);

	/**
	 * Find the line number at which a given byte offset falls.
	 *
	 * @param source The source text
	 * @param offset Byte offset into the source
	 * @return Line number (1-based)
	 */
	static int offsetToLine(const std::string &source, size_t offset);

	/**
	 * Adjust line numbers in tokens after an edit.
	 * Tokens after the edit region have their line numbers shifted by lineDelta.
	 *
	 * @param tokens The token stream to adjust (modified in place)
	 * @param fromLine The line from which to start adjusting (1-based)
	 * @param lineDelta The number of lines to add (can be negative)
	 */
	void adjustTokenLines(TokenStream &tokens, int fromLine, int lineDelta);

	/**
	 * Clone a FunctionAST node (deep copy).
	 */
	static std::unique_ptr<FunctionAST> cloneFunction(const FunctionAST *func);

	/**
	 * Clone a StructDefAST node (deep copy).
	 */
	static std::unique_ptr<StructDefAST> cloneStruct(const StructDefAST *structDef);

	/**
	 * Clone an EnumDefAST node (deep copy).
	 */
	static std::unique_ptr<EnumDefAST> cloneEnum(const EnumDefAST *enumDef);

	/**
	 * Clone an InterfaceDefAST node (deep copy).
	 */
	static std::unique_ptr<InterfaceDefAST> cloneInterface(const InterfaceDefAST *iface);
};

#endif // INCREMENTAL_PARSER_H
