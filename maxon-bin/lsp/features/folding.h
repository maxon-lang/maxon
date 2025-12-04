#ifndef MAXON_LSP_FOLDING_H
#define MAXON_LSP_FOLDING_H

#include "../../ast.h"
#include "../../compiler_api.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include <vector>

namespace maxon_lsp {

// Aliases for easier access to LSP types
using FoldingRange = maxon::lsp::FoldingRange;

/**
 * Provides folding range information for code folding in editors.
 *
 * Supports folding for:
 * - Functions (from declaration to end 'name')
 * - Structs, Enums, Interfaces
 * - Control flow blocks (if, while, for, match)
 * - Comment blocks (consecutive // or /// lines)
 */
class FoldingRangeProvider {
  public:
	/**
	 * Get folding ranges for a document.
	 *
	 * @param document The document to extract folding ranges from
	 * @param cache Analysis cache containing the AST
	 * @return Vector of FoldingRange objects
	 */
	std::vector<FoldingRange> getFoldingRanges(
		const Document &document,
		const AnalysisCache *cache);

  private:
	/**
	 * Extract folding ranges from AST nodes.
	 *
	 * @param ast Root AST node
	 * @param ranges Output vector to add ranges to
	 */
	void extractFromAST(const ASTNode *ast, std::vector<FoldingRange> &ranges);

	/**
	 * Extract folding ranges for comment blocks from raw text.
	 *
	 * @param content Document content
	 * @param ranges Output vector to add ranges to
	 */
	void extractCommentBlocks(const std::string &content, std::vector<FoldingRange> &ranges);

	/**
	 * Create a folding range from line numbers.
	 *
	 * @param startLine Start line (1-based from AST)
	 * @param endLine End line (1-based from AST)
	 * @param kind Optional folding kind (comment, imports, region)
	 * @return FoldingRange with 0-based lines
	 */
	FoldingRange createRange(int startLine, int endLine, const std::string &kind = "");
};

} // namespace maxon_lsp

#endif // MAXON_LSP_FOLDING_H
