#ifndef PARSER_SUPPORT_H
#define PARSER_SUPPORT_H

/**
 * Parser Support
 *
 * Provides optimizations for the parser including:
 * - Block boundary precomputation (O(1) lookup for matching 'end' keywords)
 * - Lookahead caching for faster token matching
 */

#include "lexer.h"
#include "lexer/lexer_platform.h"
#include "token_stream.h"
#include <algorithm>
#include <stack>
#include <unordered_map>
#include <vector>

/**
 * Block Boundary Information
 *
 * Precomputed mapping from block-starting keywords (if, while, for, function)
 * to their matching 'end' keywords. Enables O(1) lookup for block boundaries.
 */
struct BlockBoundary {
	size_t start_index;	  // Token index of block start (if, while, for, function)
	size_t end_index;	  // Token index of matching 'end'
	std::string block_id; // Block identifier (for labeled blocks)

	enum class Type {
		If,
		While,
		For,
		Function
	};
	Type type;
};

/**
 * Block Boundary Analyzer
 *
 * Pre-processes the token stream to build a map of block boundaries.
 * This eliminates the need for linear scanning during parsing.
 */
class BlockBoundaryAnalyzer {
  public:
	/**
	 * Analyze token stream and build block boundary map
	 */
	void analyze(const TokenStream &tokens) {
		boundaries_.clear();
		start_to_boundary_.clear();

		std::stack<BlockBoundary> nesting_stack;

		for (size_t i = 0; i < tokens.size(); ++i) {
			if (tokens.get_type(i) != TokenType::KEYWORD) {
				continue;
			}

			std::string_view kw = tokens.get_value(i);

			// Check for block-starting keywords
			if (kw == "if" || kw == "while" || kw == "for" || kw == "function") {
				BlockBoundary boundary;
				boundary.start_index = i;
				boundary.end_index = 0; // Will be filled when we find 'end'

				if (kw == "if")
					boundary.type = BlockBoundary::Type::If;
				else if (kw == "while")
					boundary.type = BlockBoundary::Type::While;
				else if (kw == "for")
					boundary.type = BlockBoundary::Type::For;
				else
					boundary.type = BlockBoundary::Type::Function;

				// Look for block ID (next token after condition/parameters)
				// For simplicity, we'll extract it during parsing
				boundary.block_id = "";

				nesting_stack.push(boundary);
			} else if (kw == "end" && !nesting_stack.empty()) {
				BlockBoundary completed = nesting_stack.top();
				nesting_stack.pop();
				completed.end_index = i;

				// Store the completed boundary
				boundaries_.push_back(completed);
				start_to_boundary_[completed.start_index] = boundaries_.size() - 1;
			}
		}
	}

	/**
	 * Analyze original token vector
	 */
	void analyze(const std::vector<Token> &tokens) {
		boundaries_.clear();
		start_to_boundary_.clear();

		std::stack<BlockBoundary> nesting_stack;

		for (size_t i = 0; i < tokens.size(); ++i) {
			if (tokens[i].type != TokenType::KEYWORD) {
				continue;
			}

			const std::string &kw = tokens[i].value;

			if (kw == "if" || kw == "while" || kw == "for" || kw == "function") {
				BlockBoundary boundary;
				boundary.start_index = i;
				boundary.end_index = 0;

				if (kw == "if")
					boundary.type = BlockBoundary::Type::If;
				else if (kw == "while")
					boundary.type = BlockBoundary::Type::While;
				else if (kw == "for")
					boundary.type = BlockBoundary::Type::For;
				else
					boundary.type = BlockBoundary::Type::Function;

				boundary.block_id = "";
				nesting_stack.push(boundary);
			} else if (kw == "end" && !nesting_stack.empty()) {
				BlockBoundary completed = nesting_stack.top();
				nesting_stack.pop();
				completed.end_index = i;

				boundaries_.push_back(completed);
				start_to_boundary_[completed.start_index] = boundaries_.size() - 1;
			}
		}
	}

	/**
	 * Get the matching 'end' index for a block starting at the given index
	 * Returns SIZE_MAX if not found
	 */
	size_t get_matching_end(size_t start_index) const {
		auto it = start_to_boundary_.find(start_index);
		if (it != start_to_boundary_.end()) {
			return boundaries_[it->second].end_index;
		}
		return SIZE_MAX;
	}

	/**
	 * Get the block boundary for a starting index
	 */
	const BlockBoundary *get_boundary(size_t start_index) const {
		auto it = start_to_boundary_.find(start_index);
		if (it != start_to_boundary_.end()) {
			return &boundaries_[it->second];
		}
		return nullptr;
	}

	/**
	 * Get all boundaries
	 */
	const std::vector<BlockBoundary> &boundaries() const {
		return boundaries_;
	}

	/**
	 * Get nesting depth at a given token index
	 */
	size_t get_nesting_depth(size_t token_index) const {
		size_t depth = 0;
		for (const auto &boundary : boundaries_) {
			if (token_index > boundary.start_index && token_index < boundary.end_index) {
				depth++;
			}
		}
		return depth;
	}

	// ========================================================================
	// Incremental Analysis Support
	// ========================================================================

	/**
	 * Invalidate all boundaries that start at or after the given token index.
	 * After invalidation, reanalyzeFrom() should be called with the updated tokens.
	 *
	 * @param tokenIndex The token index from which to invalidate
	 */
	void invalidateFrom(size_t tokenIndex) {
		// Remove boundaries that start at or after tokenIndex
		boundaries_.erase(
			std::remove_if(boundaries_.begin(), boundaries_.end(),
				[tokenIndex](const BlockBoundary &b) {
					return b.start_index >= tokenIndex;
				}),
			boundaries_.end());

		// Also remove boundaries that span across tokenIndex (their end is invalid)
		boundaries_.erase(
			std::remove_if(boundaries_.begin(), boundaries_.end(),
				[tokenIndex](const BlockBoundary &b) {
					return b.end_index >= tokenIndex;
				}),
			boundaries_.end());

		// Rebuild the start_to_boundary_ map
		start_to_boundary_.clear();
		for (size_t i = 0; i < boundaries_.size(); ++i) {
			start_to_boundary_[boundaries_[i].start_index] = i;
		}
	}

	/**
	 * Reanalyze boundaries from the given token index onwards.
	 * Call this after invalidateFrom() and after splicing new tokens.
	 *
	 * @param tokens The updated token stream
	 * @param fromIndex The token index from which to start reanalysis
	 */
	void reanalyzeFrom(const TokenStream &tokens, size_t fromIndex) {
		// First, find any boundaries that were started before fromIndex but not yet closed
		// These need to be completed with the new token stream
		std::stack<BlockBoundary> nesting_stack;

		// Push any incomplete boundaries (those that started before fromIndex)
		for (const auto &boundary : boundaries_) {
			if (boundary.start_index < fromIndex && boundary.end_index == 0) {
				nesting_stack.push(boundary);
			}
		}

		// Continue analysis from fromIndex
		for (size_t i = fromIndex; i < tokens.size(); ++i) {
			if (tokens.get_type(i) != TokenType::KEYWORD) {
				continue;
			}

			std::string_view kw = tokens.get_value(i);

			if (kw == "if" || kw == "while" || kw == "for" || kw == "function" ||
				kw == "struct" || kw == "enum" || kw == "interface" || kw == "match") {
				BlockBoundary boundary;
				boundary.start_index = i;
				boundary.end_index = 0;

				if (kw == "if")
					boundary.type = BlockBoundary::Type::If;
				else if (kw == "while")
					boundary.type = BlockBoundary::Type::While;
				else if (kw == "for")
					boundary.type = BlockBoundary::Type::For;
				else
					boundary.type = BlockBoundary::Type::Function;

				boundary.block_id = "";
				nesting_stack.push(boundary);
			} else if (kw == "end" && !nesting_stack.empty()) {
				BlockBoundary completed = nesting_stack.top();
				nesting_stack.pop();
				completed.end_index = i;

				boundaries_.push_back(completed);
				start_to_boundary_[completed.start_index] = boundaries_.size() - 1;
			}
		}
	}

	/**
	 * Adjust all token indices by a delta.
	 * Used after splicing to update existing boundary indices.
	 *
	 * @param fromIndex Token index from which to apply the delta
	 * @param delta Amount to add to indices (can be negative for removals)
	 */
	void adjustIndices(size_t fromIndex, int64_t delta) {
		for (auto &boundary : boundaries_) {
			if (boundary.start_index >= fromIndex) {
				boundary.start_index = static_cast<size_t>(
					static_cast<int64_t>(boundary.start_index) + delta);
			}
			if (boundary.end_index >= fromIndex) {
				boundary.end_index = static_cast<size_t>(
					static_cast<int64_t>(boundary.end_index) + delta);
			}
		}

		// Rebuild the start_to_boundary_ map after adjusting indices
		start_to_boundary_.clear();
		for (size_t i = 0; i < boundaries_.size(); ++i) {
			start_to_boundary_[boundaries_[i].start_index] = i;
		}
	}

	/**
	 * Clear all boundaries
	 */
	void clear() {
		boundaries_.clear();
		start_to_boundary_.clear();
	}

  private:
	std::vector<BlockBoundary> boundaries_;
	std::unordered_map<size_t, size_t> start_to_boundary_; // start_index -> index in boundaries_
};

/**
 * Lookahead Cache
 *
 * Caches the next N token types for fast lookahead operations.
 * Uses SIMD to batch-load token types for comparison.
 */
class LookaheadCache {
  public:
	static constexpr size_t CACHE_SIZE = 32; // Cache 32 token types

	LookaheadCache() : tokens_(nullptr), position_(0), cache_start_(SIZE_MAX) {}

	/**
	 * Initialize with token stream
	 */
	void initialize(const TokenStream *tokens) {
		tokens_ = tokens;
		position_ = 0;
		refresh_cache();
	}

	/**
	 * Initialize with token vector
	 */
	void initialize(const std::vector<Token> *tokens) {
		vector_tokens_ = tokens;
		tokens_ = nullptr;
		position_ = 0;
		refresh_cache_from_vector();
	}

	/**
	 * Set current position
	 */
	void set_position(size_t pos) {
		position_ = pos;
		if (pos < cache_start_ || pos >= cache_start_ + CACHE_SIZE) {
			refresh_cache();
		}
	}

	/**
	 * Get token type at offset from current position
	 */
	TokenType peek_type(size_t offset = 0) const {
		size_t abs_pos = position_ + offset;

		if (abs_pos >= cache_start_ && abs_pos < cache_start_ + CACHE_SIZE) {
			return static_cast<TokenType>(cache_[abs_pos - cache_start_]);
		}

		// Cache miss - access directly
		if (tokens_) {
			if (abs_pos < tokens_->size()) {
				return tokens_->get_type(abs_pos);
			}
		} else if (vector_tokens_) {
			if (abs_pos < vector_tokens_->size()) {
				return (*vector_tokens_)[abs_pos].type;
			}
		}

		return TokenType::END_OF_FILE;
	}

	/**
	 * Check if token at offset matches type
	 */
	bool check(TokenType type, size_t offset = 0) const {
		return peek_type(offset) == type;
	}

	/**
	 * Check if token at offset matches any of the given types (SIMD-accelerated)
	 */
	bool check_any(std::initializer_list<TokenType> types, size_t offset = 0) const {
		uint8_t target = static_cast<uint8_t>(peek_type(offset));

		for (TokenType t : types) {
			if (static_cast<uint8_t>(t) == target) {
				return true;
			}
		}
		return false;
	}

	/**
	 * Find next token of specific type within cache
	 * Returns relative offset from current position, or SIZE_MAX if not found
	 */
	size_t find_next(TokenType type) const {
		if (tokens_ && has_avx2()) {
			return find_next_simd(type);
		}
		return find_next_scalar(type);
	}

  private:
	const TokenStream *tokens_ = nullptr;
	const std::vector<Token> *vector_tokens_ = nullptr;
	size_t position_;
	size_t cache_start_;
	alignas(32) uint8_t cache_[CACHE_SIZE];

	void refresh_cache() {
		if (!tokens_) {
			if (vector_tokens_) {
				refresh_cache_from_vector();
			}
			return;
		}

		cache_start_ = position_;
		size_t count = std::min(CACHE_SIZE, tokens_->size() - position_);

		// Copy token types to cache
		for (size_t i = 0; i < count; ++i) {
			cache_[i] = static_cast<uint8_t>(tokens_->get_type(position_ + i));
		}

		// Fill remaining with END_OF_FILE
		for (size_t i = count; i < CACHE_SIZE; ++i) {
			cache_[i] = static_cast<uint8_t>(TokenType::END_OF_FILE);
		}
	}

	void refresh_cache_from_vector() {
		if (!vector_tokens_)
			return;

		cache_start_ = position_;
		size_t count = std::min(CACHE_SIZE, vector_tokens_->size() - position_);

		for (size_t i = 0; i < count; ++i) {
			cache_[i] = static_cast<uint8_t>((*vector_tokens_)[position_ + i].type);
		}

		for (size_t i = count; i < CACHE_SIZE; ++i) {
			cache_[i] = static_cast<uint8_t>(TokenType::END_OF_FILE);
		}
	}

	size_t find_next_simd(TokenType type) const {
		simd256_t target = simd_set1_epi8_256(static_cast<int8_t>(type));
		simd256_t chunk = simd_loadu_256(cache_);
		simd256_t cmp = simd_cmpeq_epi8_256(chunk, target);
		uint32_t mask = simd_movemask_epi8_256(cmp);

		if (mask != 0) {
			return count_trailing_zeros(mask);
		}
		return SIZE_MAX;
	}

	size_t find_next_scalar(TokenType type) const {
		uint8_t target = static_cast<uint8_t>(type);
		for (size_t i = 0; i < CACHE_SIZE; ++i) {
			if (cache_[i] == target) {
				return i;
			}
		}
		return SIZE_MAX;
	}
};

/**
 * Statement Classification
 *
 * Quickly classify the next statement type based on current token.
 * Eliminates long if-else chains in parseStatement().
 */
enum class StatementKind {
	VarDecl,
	LetDecl,
	If,
	While,
	For,
	Return,
	Break,
	Continue,
	Assignment,
	FunctionCall,
	MethodCall,
	Unknown
};

/**
 * Classify statement based on current token
 */
inline StatementKind classify_statement(TokenType type, const std::string &value) {
	if (type == TokenType::KEYWORD) {
		// Use prefix matching for fast classification
		if (value.size() >= 2) {
			char c1 = value[0];

			switch (c1) {
			case 'v':
				if (value == "var")
					return StatementKind::VarDecl;
				break;
			case 'l':
				if (value == "let")
					return StatementKind::LetDecl;
				break;
			case 'i':
				if (value == "if")
					return StatementKind::If;
				break;
			case 'w':
				if (value == "while")
					return StatementKind::While;
				break;
			case 'f':
				if (value == "for")
					return StatementKind::For;
				break;
			case 'r':
				if (value == "return")
					return StatementKind::Return;
				break;
			case 'b':
				if (value == "break")
					return StatementKind::Break;
				break;
			case 'c':
				if (value == "continue")
					return StatementKind::Continue;
				break;
			}
		}
	} else if (type == TokenType::IDENTIFIER) {
		// Could be assignment, function call, or method call
		// Need to look at next token to determine
		return StatementKind::Unknown; // Parser will determine
	}

	return StatementKind::Unknown;
}

/**
 * Parse Decision Table
 *
 * Pre-computed masks for fast token type checking.
 */
class ParseDecisionTable {
  public:
	// Token types that can start an expression
	static constexpr uint64_t EXPR_START_MASK =
		(1ULL << static_cast<uint8_t>(TokenType::IDENTIFIER)) |
		(1ULL << static_cast<uint8_t>(TokenType::NUMBER)) |
		(1ULL << static_cast<uint8_t>(TokenType::FLOAT_LITERAL)) |
		(1ULL << static_cast<uint8_t>(TokenType::BYTE_LITERAL)) |
		(1ULL << static_cast<uint8_t>(TokenType::STRING)) |
		(1ULL << static_cast<uint8_t>(TokenType::CHARACTER)) |
		(1ULL << static_cast<uint8_t>(TokenType::LPAREN)) |
		(1ULL << static_cast<uint8_t>(TokenType::LBRACKET)) |
		(1ULL << static_cast<uint8_t>(TokenType::KEYWORD)) | // true, false, math intrinsics
		(1ULL << static_cast<uint8_t>(TokenType::MINUS)) |	 // unary minus
		(1ULL << static_cast<uint8_t>(TokenType::PLUS));	 // unary plus

	// Binary operators
	static constexpr uint64_t BINARY_OP_MASK =
		(1ULL << static_cast<uint8_t>(TokenType::PLUS)) |
		(1ULL << static_cast<uint8_t>(TokenType::MINUS)) |
		(1ULL << static_cast<uint8_t>(TokenType::MULTIPLY)) |
		(1ULL << static_cast<uint8_t>(TokenType::DIVIDE)) |
		(1ULL << static_cast<uint8_t>(TokenType::EQUAL_EQUAL)) |
		(1ULL << static_cast<uint8_t>(TokenType::NOT_EQUAL)) |
		(1ULL << static_cast<uint8_t>(TokenType::GT)) |
		(1ULL << static_cast<uint8_t>(TokenType::LT)) |
		(1ULL << static_cast<uint8_t>(TokenType::GTE)) |
		(1ULL << static_cast<uint8_t>(TokenType::LTE));

	// Comparison operators
	static constexpr uint64_t COMPARISON_OP_MASK =
		(1ULL << static_cast<uint8_t>(TokenType::EQUAL_EQUAL)) |
		(1ULL << static_cast<uint8_t>(TokenType::NOT_EQUAL)) |
		(1ULL << static_cast<uint8_t>(TokenType::GT)) |
		(1ULL << static_cast<uint8_t>(TokenType::LT)) |
		(1ULL << static_cast<uint8_t>(TokenType::GTE)) |
		(1ULL << static_cast<uint8_t>(TokenType::LTE));

	static bool is_expr_start(TokenType type) {
		return (EXPR_START_MASK & (1ULL << static_cast<uint8_t>(type))) != 0;
	}

	static bool is_binary_op(TokenType type) {
		return (BINARY_OP_MASK & (1ULL << static_cast<uint8_t>(type))) != 0;
	}

	static bool is_comparison_op(TokenType type) {
		return (COMPARISON_OP_MASK & (1ULL << static_cast<uint8_t>(type))) != 0;
	}
};

/**
 * Parser Statistics
 *
 * Collects performance metrics for the parser.
 */
struct ParserStats {
	size_t tokens_processed = 0;
	size_t cache_hits = 0;
	size_t cache_misses = 0;
	size_t block_lookups = 0;
	size_t block_lookup_saves = 0; // Tokens saved by O(1) lookup
};

#endif // PARSER_SUPPORT_H
