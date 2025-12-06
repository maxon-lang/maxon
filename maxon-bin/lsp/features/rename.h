#ifndef MAXON_LSP_RENAME_H
#define MAXON_LSP_RENAME_H

#include "../../compiler_api.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include "references.h"
#include <optional>

namespace maxon_lsp {

// Alias for easier access to LSP types
using Location = maxon::lsp::Location;
using Position = maxon::lsp::Position;
using Range = maxon::lsp::Range;
using WorkspaceEdit = maxon::lsp::WorkspaceEdit;
using TextEdit = maxon::lsp::TextEdit;
using PrepareRenameResult = maxon::lsp::PrepareRenameResult;

/**
 * Provides rename functionality for Maxon source code.
 *
 * Supports renaming of:
 * - Variables (local and parameters)
 * - Functions (including end labels)
 * - Types (structs, enums, interfaces)
 * - Struct fields
 */
class RenameProvider {
  public:
	/**
	 * Prepare rename - validate symbol can be renamed.
	 *
	 * This is called before the actual rename to:
	 * 1. Verify the symbol at the position can be renamed
	 * 2. Provide the current symbol name and range for UI
	 *
	 * @param document The document containing the symbol
	 * @param position The cursor position on the symbol
	 * @param cache Analysis cache with AST and semantic info
	 * @return PrepareRenameResult with range and placeholder, or nullopt if not renameable
	 */
	std::optional<PrepareRenameResult> prepareRename(
		const Document &document,
		const Position &position,
		const AnalysisCache *cache);

	/**
	 * Execute rename - generate workspace edits.
	 *
	 * Finds all references to the symbol and creates text edits
	 * to replace them with the new name.
	 *
	 * @param document The document containing the symbol
	 * @param position The cursor position on the symbol
	 * @param newName The new name for the symbol
	 * @param cache Analysis cache with AST and semantic info
	 * @param workspaceRoot Workspace root for multi-file search
	 * @return WorkspaceEdit with all text changes, or nullopt if rename failed
	 */
	std::optional<WorkspaceEdit> rename(
		const Document &document,
		const Position &position,
		const std::string &newName,
		const AnalysisCache *cache,
		const std::string &workspaceRoot);

  private:
	ReferencesProvider referencesProvider_;

	/**
	 * Check if symbol can be renamed.
	 *
	 * Symbols that cannot be renamed:
	 * - Keywords (if, while, function, etc.)
	 * - Built-in types (int, float, bool, etc.)
	 * - Stdlib symbols (read-only)
	 *
	 * @param symbolName Name of the symbol
	 * @param cache Analysis cache with semantic info
	 * @return true if the symbol can be renamed
	 */
	bool canRename(const std::string &symbolName, const AnalysisCache *cache);

	/**
	 * Check if new name is a valid identifier.
	 *
	 * Valid identifiers:
	 * - Start with letter or underscore
	 * - Contain only letters, digits, underscores
	 * - Not a keyword
	 *
	 * @param name The proposed new name
	 * @return true if the name is a valid identifier
	 */
	bool isValidIdentifier(const std::string &name);

	/**
	 * Check for naming conflicts.
	 *
	 * Detects if the new name would conflict with:
	 * - Variables in the same scope
	 * - Functions with the same name
	 * - Types with the same name
	 *
	 * @param newName The proposed new name
	 * @param cache Analysis cache with semantic info
	 * @param document The current document
	 * @param position The position of the symbol being renamed
	 * @return true if there would be a naming conflict
	 */
	bool hasNamingConflict(
		const std::string &newName,
		const AnalysisCache *cache,
		const Document &document,
		const Position &position);

	/**
	 * Get symbol info at position.
	 *
	 * Extracts the identifier at the given position.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The identifier string at position
	 */
	std::string getSymbolAtPosition(const Document &document, const Position &position);

	/**
	 * Get the range of the symbol at position.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The range of the symbol
	 */
	Range getSymbolRange(const Document &document, const Position &position);

	/**
	 * Check if symbol is a keyword or built-in.
	 *
	 * Uses KeywordMatcher to check against all Maxon keywords
	 * and built-in types.
	 *
	 * @param name The symbol name to check
	 * @return true if the name is a keyword or built-in type
	 */
	bool isKeywordOrBuiltin(const std::string &name);

	/**
	 * Check if symbol is in stdlib (read-only).
	 *
	 * Stdlib symbols cannot be renamed because their source
	 * files are not editable.
	 *
	 * @param name The symbol name to check
	 * @param stdlib The loaded stdlib symbols
	 * @return true if the symbol is defined in stdlib
	 */
	bool isStdlibSymbol(const std::string &name, const StdlibSymbols &stdlib);

	/**
	 * Find the end label for a function.
	 *
	 * Maxon functions have an end label like: end 'functionName'
	 * When renaming a function, this label must also be updated.
	 *
	 * @param document The document containing the function
	 * @param functionName The current function name
	 * @param cache Analysis cache with AST info
	 * @return Location of the function name in the end label, if found
	 */
	std::optional<Location> findFunctionEndLabel(
		const Document &document,
		const std::string &functionName,
		const AnalysisCache *cache);

	/**
	 * Find the end label for a struct.
	 *
	 * Maxon structs have an end label like: end 'StructName'
	 * When renaming a struct, this label must also be updated.
	 *
	 * @param document The document containing the struct
	 * @param structName The current struct name
	 * @param cache Analysis cache with AST info
	 * @return Location of the struct name in the end label, if found
	 */
	std::optional<Location> findStructEndLabel(
		const Document &document,
		const std::string &structName,
		const AnalysisCache *cache);

	/**
	 * Find the end label for an enum.
	 *
	 * Maxon enums have an end label like: end 'EnumName'
	 * When renaming an enum, this label must also be updated.
	 *
	 * @param document The document containing the enum
	 * @param enumName The current enum name
	 * @param cache Analysis cache with AST info
	 * @return Location of the enum name in the end label, if found
	 */
	std::optional<Location> findEnumEndLabel(
		const Document &document,
		const std::string &enumName,
		const AnalysisCache *cache);

	/**
	 * Find the end label for an interface.
	 *
	 * Maxon interfaces have an end label like: end 'InterfaceName'
	 * When renaming an interface, this label must also be updated.
	 *
	 * @param document The document containing the interface
	 * @param interfaceName The current interface name
	 * @param cache Analysis cache with AST info
	 * @return Location of the interface name in the end label, if found
	 */
	std::optional<Location> findInterfaceEndLabel(
		const Document &document,
		const std::string &interfaceName,
		const AnalysisCache *cache);

	/**
	 * Build workspace edit from locations.
	 *
	 * Groups text edits by document URI and creates a WorkspaceEdit
	 * that can be applied by the editor.
	 *
	 * @param locations All locations where the symbol appears
	 * @param newName The new name to use
	 * @return WorkspaceEdit with all changes
	 */
	WorkspaceEdit buildWorkspaceEdit(
		const std::vector<Location> &locations,
		const std::string &newName,
		const Document *document = nullptr);

	/**
	 * Determine the symbol kind at a position.
	 *
	 * Used to determine if we need to find end labels.
	 *
	 * @param symbolName The symbol name
	 * @param cache Analysis cache with semantic info
	 * @return Symbol kind as string ("function", "struct", "enum", "interface", "variable", "unknown")
	 */
	std::string getSymbolKind(const std::string &symbolName, const AnalysisCache *cache);

	/**
	 * Check if cursor is on a block identifier (quoted label).
	 *
	 * Block identifiers appear in:
	 * - while ... 'label' / end 'label'
	 * - if ... 'label' / else 'label' / end 'label'
	 * - for ... 'label' / end 'label'
	 * - namespace name 'label' / end 'label'
	 * - continue 'label' / break 'label'
	 * - Function end labels: end 'functionName'
	 *
	 * @param document The document to check
	 * @param position The cursor position
	 * @return true if cursor is inside a block identifier
	 */
	bool isOnBlockIdentifier(const Document &document, const Position &position);

	/**
	 * Get the block identifier at position (without quotes).
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The block identifier name, or empty if not on block identifier
	 */
	std::string getBlockIdentifierAtPosition(const Document &document, const Position &position);

	/**
	 * Find all occurrences of a block label in the document.
	 *
	 * Searches for all uses of the block label:
	 * - Opening label (if 'label', while 'label', etc.)
	 * - Closing label (end 'label')
	 * - Intermediate labels (else 'label')
	 * - Jump targets (continue 'label', break 'label')
	 *
	 * @param document The document to search
	 * @param blockName The block label name (without quotes)
	 * @param startPosition Position where rename was requested (for scoping)
	 * @return All locations where the block label appears
	 */
	std::vector<Location> findBlockLabelOccurrences(
		const Document &document,
		const std::string &blockName,
		const Position &startPosition);

	/**
	 * Get the full range of the block identifier including quotes.
	 *
	 * @param document The document
	 * @param position The cursor position
	 * @return Range covering 'name' including the quotes
	 */
	Range getBlockIdentifierRange(const Document &document, const Position &position);

	// Cached symbol range from last getSymbolAtPosition call
	Range currentSymbolRange_;

	// Cached block identifier range
	Range currentBlockIdentifierRange_;
};

} // namespace maxon_lsp

#endif // MAXON_LSP_RENAME_H
