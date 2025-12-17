#ifndef MAXON_LSP_CODE_LENS_H
#define MAXON_LSP_CODE_LENS_H

#include "../document_manager.h"
#include "../lsp_types.h"
#include <vector>

namespace maxon_lsp {

// Aliases for easier access to LSP types
using CodeLens = maxon::lsp::CodeLens;
using Range = maxon::lsp::Range;
using Position = maxon::lsp::Position;
using Command = maxon::lsp::Command;

/**
 * Provides CodeLens for function purity status.
 *
 * Shows "pure" or "mutating: param1, param2" above each function declaration
 * based on whether the function mutates any of its parameters.
 */
class CodeLensProvider {
  public:
	/**
	 * Get CodeLens items for a document.
	 *
	 * Returns a CodeLens for each function showing its purity status.
	 *
	 * @param document The document to get CodeLens for
	 * @param cache Analysis cache containing AST and mutation info
	 * @return Vector of CodeLens items
	 */
	std::vector<CodeLens> getCodeLenses(const Document &document, const AnalysisCache *cache);

  private:
	/**
	 * Build CodeLens for a function.
	 *
	 * @param funcName Function name (for display, not lookup key)
	 * @param line 1-based line number of function declaration
	 * @param mutatedParams Set of mutated parameter names (empty if pure)
	 * @return CodeLens showing purity status
	 */
	CodeLens buildFunctionCodeLens(const std::string &funcName, int line,
								   const std::set<std::string> &mutatedParams);

	/**
	 * Build LSP Range for CodeLens position.
	 * CodeLens appears at the start of the function declaration line.
	 *
	 * @param line 1-based line number
	 * @return 0-based LSP Range at start of line
	 */
	Range buildCodeLensRange(int line);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_CODE_LENS_H
