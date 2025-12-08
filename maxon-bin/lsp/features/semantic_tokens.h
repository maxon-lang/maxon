#ifndef MAXON_LSP_FEATURES_SEMANTIC_TOKENS_H
#define MAXON_LSP_FEATURES_SEMANTIC_TOKENS_H

#include "../../compiler_api.h"
#include "../document_manager.h"
#include "../lsp_types.h"
#include <optional>
#include <set>
#include <string>
#include <vector>

namespace maxon_lsp {

// Use LSP namespace types
using SemanticTokens = maxon::lsp::SemanticTokens;
using SemanticTokensLegend = maxon::lsp::SemanticTokensLegend;

/**
 * Provides semantic tokens for block identifiers with nesting level colorization.
 *
 * Block identifiers in Maxon are labels that appear after keywords and at 'end' statements:
 * - Function names: function myFunc() ... end 'myFunc'
 * - Block labels: while ... 'loop' ... end 'loop'
 * - Struct names: struct Point ... end 'Point'
 *
 * The semantic tokens provider assigns different modifier bits based on nesting level,
 * allowing the editor to color each level differently (cycling through 6 colors).
 */
class SemanticTokensProvider {
  public:
	SemanticTokensProvider() = default;

	/**
	 * Get the semantic tokens legend defining supported token types and modifiers.
	 * Must match what VS Code extension expects.
	 */
	static SemanticTokensLegend getLegend();

	/**
	 * Compute semantic tokens for a document.
	 * Returns tokens for block identifiers (with nesting level) and type references.
	 */
	std::optional<SemanticTokens> getSemanticTokens(
		const Document &document,
		const AnalysisCache *cache,
		const StdlibSymbols &stdlib);

  private:
	// Token type indices in the legend
	static constexpr int TYPE_TOKEN_TYPE = 1;	// 'type' token
	static constexpr int LABEL_TOKEN_TYPE = 22; // 'label' for block identifiers

	// Number of nesting levels before cycling
	static constexpr int NUM_LEVELS = 6;

	// Modifier bit offset for level0 (levels are level0, level1, ..., level5)
	static constexpr int LEVEL_MODIFIER_OFFSET = 10;

	/**
	 * A semantic token to be emitted.
	 */
	struct SemanticToken {
		int line;	   // 0-indexed line number
		int startChar; // 0-indexed start character (column)
		int length;	   // Length of the token
		int tokenType; // Token type index
		int modifiers; // Token modifier bitmask
	};

	/**
	 * Find all block identifiers in the document.
	 * Tracks nesting level as we encounter block-starting keywords.
	 */
	std::vector<SemanticToken> findBlockIdentifiers(const Document &document);

	/**
	 * Parse a single line for block identifiers.
	 * Updates nesting level based on keywords found.
	 */
	void parseLineForBlocks(
		const std::string &line,
		int lineNumber,
		int &currentLevel,
		std::vector<SemanticToken> &tokens);

	/**
	 * Find type references in the document using analysis cache.
	 * Identifies struct names, interface names, and internal types.
	 */
	std::vector<SemanticToken> findTypeReferences(
		const Document &document,
		const AnalysisCache *cache,
		const StdlibSymbols &stdlib);

	/**
	 * Find type references in a single line.
	 */
	void findTypesInLine(
		const std::string &line,
		int lineNumber,
		const std::set<std::string> &knownTypes,
		std::vector<SemanticToken> &tokens);

	/**
	 * Encode tokens in LSP delta format.
	 * Each token is: [deltaLine, deltaStartChar, length, tokenType, tokenModifiers]
	 */
	std::vector<int> encodeTokens(const std::vector<SemanticToken> &tokens);

	/**
	 * Get the modifier bitmask for a nesting level.
	 * Levels cycle through 0-5, each with a different bit set.
	 */
	int getLevelModifier(int level);
};

} // namespace maxon_lsp

#endif // MAXON_LSP_FEATURES_SEMANTIC_TOKENS_H
