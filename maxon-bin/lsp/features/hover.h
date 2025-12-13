#ifndef MAXON_LSP_HOVER_H
#define MAXON_LSP_HOVER_H

#include "../../compiler_api.h"
#include "../../intrinsics_defs.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include <optional>

namespace maxon_lsp {

// Alias for easier access to LSP types
using Hover = maxon::lsp::Hover;
using Position = maxon::lsp::Position;
using Range = maxon::lsp::Range;
using MarkupContent = maxon::lsp::MarkupContent;
using MarkupKind = maxon::lsp::MarkupKind;

/**
 * Provides hover information for symbols in Maxon source code.
 *
 * Supports hover for:
 * - Keywords with documentation
 * - Variables with type and mutability info
 * - Functions with signatures and documentation
 * - Types (built-in, structs, enums, interfaces)
 * - Struct fields
 */
class HoverProvider {
  public:
	/**
	 * Get hover information at a position in a document.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @param cache Analysis cache containing symbols and type info
	 * @param stdlib Standard library symbols
	 * @return Hover information if available, nullopt otherwise
	 */
	std::optional<Hover> getHover(
		const Document &document,
		const Position &position,
		const AnalysisCache *cache,
		const StdlibSymbols &stdlib);

  private:
	/**
	 * Find the token/identifier at the given position.
	 * Handles dot-separated names for member access.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The token string at position, or empty string if none
	 */
	std::string getTokenAtPosition(const Document &document, const Position &position);

	/**
	 * Get the range of the token at the given position.
	 *
	 * @param document The document to search in
	 * @param position The cursor position
	 * @return The range of the token
	 */
	Range getTokenRange(const Document &document, const Position &position);

	/**
	 * Look up a keyword and return hover info.
	 *
	 * @param token The keyword to look up
	 * @return Hover info if token is a keyword, nullopt otherwise
	 */
	std::optional<Hover> lookupKeyword(const std::string &token);

	/**
	 * Look up a struct field declaration when cursor is on a field in a type definition.
	 *
	 * @param name Field name to look up
	 * @param cache Analysis cache containing struct info
	 * @param position Position to check if we're in a struct definition
	 * @return Hover info if on a field declaration, nullopt otherwise
	 */
	std::optional<Hover> lookupFieldDeclaration(const std::string &name, const AnalysisCache *cache, const Position &position);

	/**
	 * Look up a variable in the current scope and return hover info.
	 *
	 * @param name Variable name to look up
	 * @param cache Analysis cache containing variable info
	 * @param position Position for scope determination
	 * @return Hover info if variable found, nullopt otherwise
	 */
	std::optional<Hover> lookupVariable(const std::string &name, const AnalysisCache *cache, const Position &position);

	/**
	 * Look up a function and return hover info.
	 *
	 * @param name Function name to look up
	 * @param cache Analysis cache containing function info
	 * @param stdlib Standard library symbols
	 * @return Hover info if function found, nullopt otherwise
	 */
	std::optional<Hover> lookupFunction(const std::string &name, const AnalysisCache *cache, const StdlibSymbols &stdlib);

	/**
	 * Look up a type (built-in, struct, enum, interface) and return hover info.
	 *
	 * @param name Type name to look up
	 * @param cache Analysis cache containing type info
	 * @param stdlib Standard library symbols
	 * @return Hover info if type found, nullopt otherwise
	 */
	std::optional<Hover> lookupType(const std::string &name, const AnalysisCache *cache, const StdlibSymbols &stdlib);

	/**
	 * Look up a struct field and return hover info.
	 *
	 * @param structName Name of the struct containing the field
	 * @param fieldName Name of the field
	 * @param cache Analysis cache containing struct info
	 * @param stdlib Standard library symbols
	 * @return Hover info if field found, nullopt otherwise
	 */
	std::optional<Hover> lookupField(const std::string &structName, const std::string &fieldName,
									 const AnalysisCache *cache, const StdlibSymbols &stdlib);

	/**
	 * Look up a compiler intrinsic (e.g., __write_file_binary) and return hover info.
	 *
	 * @param name Intrinsic name (must start with __)
	 * @return Hover info if intrinsic found, nullopt otherwise
	 */
	std::optional<Hover> lookupIntrinsic(const std::string &name);

	/**
	 * Format intrinsic hover content.
	 *
	 * @param name Intrinsic name
	 * @param returnType Return type
	 * @param params Parameter definitions
	 * @return Markdown formatted string
	 */
	std::string formatIntrinsicHover(const std::string &name, const std::string &returnType,
									 const std::vector<IntrinsicParamDef> &params);

	// Format hover content as markdown

	/**
	 * Format keyword hover content.
	 *
	 * @param keyword The keyword info to format
	 * @return Markdown formatted string
	 */
	std::string formatKeywordHover(const KeywordLSPInfo &keyword);

	/**
	 * Format variable hover content.
	 *
	 * @param name Variable name
	 * @param type Variable type
	 * @param isMutable Whether the variable is mutable (var vs let)
	 * @param value The variable's initial value (shown for immutable variables)
	 * @param isParameter Whether this is a function parameter
	 * @param doc Optional documentation
	 * @return Markdown formatted string
	 */
	std::string formatVariableHover(const std::string &name, const std::string &type,
									bool isMutable, const std::string &value = "", bool isParameter = false, const std::string &doc = "");

	/**
	 * Format function hover content from FunctionInfo.
	 *
	 * @param func Function info from semantic analysis
	 * @param doc Optional documentation
	 * @return Markdown formatted string
	 */
	std::string formatFunctionHover(const FunctionInfo &func, const std::string &doc = "");

	/**
	 * Format function hover content from LSPSymbolInfo.
	 *
	 * @param symbol Symbol info from analysis
	 * @return Markdown formatted string
	 */
	std::string formatFunctionHover(const LSPSymbolInfo &symbol);

	/**
	 * Format struct hover content.
	 *
	 * @param structInfo Struct info from semantic analysis
	 * @param doc Optional documentation
	 * @return Markdown formatted string
	 */
	std::string formatStructHover(const StructInfo &structInfo, const std::string &doc = "");

	/**
	 * Format enum hover content.
	 *
	 * @param enumInfo Enum info from semantic analysis
	 * @param doc Optional documentation
	 * @return Markdown formatted string
	 */
	std::string formatEnumHover(const EnumInfo &enumInfo, const std::string &doc = "");

	/**
	 * Format interface hover content.
	 *
	 * @param iface Interface info from semantic analysis
	 * @param doc Optional documentation
	 * @return Markdown formatted string
	 */
	std::string formatInterfaceHover(const InterfaceInfo &iface, const std::string &doc = "");

	/**
	 * Format struct field hover content.
	 *
	 * @param structName Name of the containing struct
	 * @param fieldName Name of the field
	 * @param type Field type
	 * @return Markdown formatted string
	 */
	std::string formatFieldHover(const std::string &structName, const std::string &fieldName,
								 const std::string &type);

	/**
	 * Build a Hover object with markdown content.
	 *
	 * @param markdown The markdown content
	 * @param range The range to highlight
	 * @return Hover object ready for LSP response
	 */
	Hover buildHover(const std::string &markdown, const Range &range);

	/**
	 * Check if a position is inside a comment.
	 * Handles both line comments and block comments.
	 *
	 * @param document The document to check
	 * @param position The position to check
	 * @return true if position is inside a comment
	 */
	bool isPositionInComment(const Document &document, const Position &position);

	// Current token range (set by getTokenAtPosition)
	Range currentTokenRange_;
};

} // namespace maxon_lsp

#endif // MAXON_LSP_HOVER_H
