#ifndef MAXON_LSP_DEFINITION_H
#define MAXON_LSP_DEFINITION_H

#include "../../compiler_api.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include <optional>
#include <variant>

namespace maxon_lsp {

// Alias for easier access to LSP types
using Location = maxon::lsp::Location;
using LocationLink = maxon::lsp::LocationLink;
using Position = maxon::lsp::Position;
using Range = maxon::lsp::Range;

/**
 * Provides go-to-definition functionality for symbols in Maxon source code.
 *
 * Supports definition lookup for:
 * - Variables (local and parameters)
 * - Functions (local and stdlib)
 * - Types (structs, enums, interfaces)
 * - Struct fields
 */
class DefinitionProvider {
  public:
	// Result type: either a single Location or a list of LocationLinks
	using DefinitionResult = std::variant<Location, std::vector<LocationLink>>;

	/**
	 * Get definition location for symbol at position.
	 *
	 * @param document The document containing the symbol reference
	 * @param position The cursor position
	 * @param cache Analysis cache containing symbols and type info
	 * @param stdlib Standard library symbols
	 * @param workspaceRoot Root directory of the workspace (for stdlib paths)
	 * @return Definition location if found, nullopt otherwise
	 */
	std::optional<DefinitionResult> getDefinition(
		const Document &document,
		const Position &position,
		const AnalysisCache *cache,
		const StdlibSymbols &stdlib,
		const std::string &workspaceRoot);

  private:
	/**
	 * Find the symbol (identifier) at the given position.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The identifier string at position, or empty string if none
	 */
	std::string getSymbolAtPosition(const Document &document, const Position &position);

	/**
	 * Get the range of the symbol at the given position.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The range of the symbol
	 */
	Range getSymbolRange(const Document &document, const Position &position);

	/**
	 * Check if position is on a member access (after dot).
	 * Extracts object and member names if so.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @param objectName [out] The object part before the dot
	 * @param memberName [out] The member part after the dot
	 * @param cursorOnObject [out] true if cursor is on the object part, false if on member
	 * @return true if this is a member access, false otherwise
	 */
	bool isMemberAccess(const Document &document, const Position &position,
						std::string &objectName, std::string &memberName, bool &cursorOnObject);

	/**
	 * Look up a variable definition.
	 *
	 * @param name Variable name to look up
	 * @param cache Analysis cache containing variable info
	 * @param document The current document (for URI)
	 * @param position The cursor position (for scoped lookup)
	 * @return Location of variable definition if found
	 */
	std::optional<Location> lookupVariable(const std::string &name,
										   const AnalysisCache *cache,
										   const Document &document,
										   const Position &position);

	/**
	 * Look up a function definition.
	 *
	 * @param name Function name to look up
	 * @param cache Analysis cache containing function info
	 * @param stdlib Standard library symbols
	 * @param workspaceRoot Root directory for stdlib paths
	 * @param documentUri URI of the current document (for local functions)
	 * @return Location of function definition if found
	 */
	std::optional<Location> lookupFunction(const std::string &name,
										   const AnalysisCache *cache,
										   const StdlibSymbols &stdlib,
										   const std::string &workspaceRoot,
										   const std::string &documentUri);

	/**
	 * Look up a type definition (struct, enum, interface).
	 *
	 * @param name Type name to look up
	 * @param cache Analysis cache containing type info
	 * @param stdlib Standard library symbols
	 * @param workspaceRoot Root directory for stdlib paths
	 * @param documentUri URI of the current document (for local types)
	 * @return Location of type definition if found
	 */
	std::optional<Location> lookupType(const std::string &name,
									   const AnalysisCache *cache,
									   const StdlibSymbols &stdlib,
									   const std::string &workspaceRoot,
									   const std::string &documentUri);

	/**
	 * Look up a struct field definition.
	 *
	 * @param structName Name of the struct containing the field
	 * @param fieldName Name of the field
	 * @param cache Analysis cache containing struct info
	 * @param stdlib Standard library symbols
	 * @param workspaceRoot Root directory for stdlib paths
	 * @param documentUri URI of the current document (for local structs)
	 * @return Location of field definition if found
	 */
	std::optional<Location> lookupField(const std::string &structName,
										const std::string &fieldName,
										const AnalysisCache *cache,
										const StdlibSymbols &stdlib,
										const std::string &workspaceRoot,
										const std::string &documentUri);

	/**
	 * Look up a function parameter definition.
	 *
	 * @param name Parameter name to look up
	 * @param cache Analysis cache containing function info
	 * @param document The current document
	 * @param position Current cursor position (to find containing function)
	 * @return Location of parameter definition if found
	 */
	std::optional<Location> lookupParameter(const std::string &name,
											const AnalysisCache *cache,
											const Document &document,
											const Position &position);

	/**
	 * Build a Location from URI and position info.
	 *
	 * @param uri The document URI
	 * @param line Start line (1-based from compiler)
	 * @param column Start column (1-based from compiler)
	 * @param endLine End line (1-based), -1 if not available
	 * @param endColumn End column (1-based), -1 if not available
	 * @return Location with proper 0-based LSP positions
	 */
	Location buildLocation(const std::string &uri, int line, int column,
						   int endLine = -1, int endColumn = -1);

	/**
	 * Convert a filesystem path to a file:// URI.
	 *
	 * @param path The filesystem path
	 * @return The file:// URI
	 */
	std::string toUri(const std::string &path);

	// Current symbol range (set by getSymbolAtPosition for highlighting)
	Range currentSymbolRange_;
};

} // namespace maxon_lsp

#endif // MAXON_LSP_DEFINITION_H
