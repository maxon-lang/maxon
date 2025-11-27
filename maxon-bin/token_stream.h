#ifndef TOKEN_STREAM_H
#define TOKEN_STREAM_H

/**
 * Token Stream
 *
 * Provides a cache-efficient Structure-of-Arrays (SoA) layout for tokens,
 * with string interning for deduplication.
 */

#include "lexer.h"
#include "lexer/lexer_platform.h"
#include <cstring>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

/**
 * String Table for string interning
 *
 * All strings (identifiers, keywords, literals) are stored in a contiguous
 * buffer and referenced by offset. This provides:
 * - Memory deduplication (same string stored once)
 * - Cache-friendly sequential access
 * - Zero-copy string references via string_view
 */
class StringTable {
  public:
	// Reserve reasonable initial capacity
	StringTable() {
		buffer_.reserve(64 * 1024); // 64KB initial buffer
		offsets_.reserve(4096);		// 4K strings
	}

	/**
	 * Intern a string and return its offset in the buffer
	 * If the string already exists, returns existing offset (deduplication)
	 */
	uint32_t intern(const char *str, size_t len) {
		std::string_view sv(str, len);

		// Check if already interned
		auto it = intern_map_.find(sv);
		if (it != intern_map_.end()) {
			return it->second;
		}

		// Add to buffer
		uint32_t offset = static_cast<uint32_t>(buffer_.size());
		buffer_.insert(buffer_.end(), str, str + len);
		buffer_.push_back('\0'); // Null-terminate for C compatibility

		// Store in map (use string_view pointing to our buffer)
		std::string_view stored_sv(&buffer_[offset], len);
		intern_map_[stored_sv] = offset;
		offsets_.push_back(offset);

		return offset;
	}

	/**
	 * Intern a std::string
	 */
	uint32_t intern(const std::string &str) {
		return intern(str.data(), str.size());
	}

	/**
	 * Get string by offset
	 */
	std::string_view get(uint32_t offset) const {
		return std::string_view(&buffer_[offset]);
	}

	/**
	 * Get string length by offset (scan for null terminator)
	 */
	size_t get_length(uint32_t offset) const {
		return std::strlen(&buffer_[offset]);
	}

	/**
	 * Get raw C string by offset
	 */
	const char *get_cstr(uint32_t offset) const {
		return &buffer_[offset];
	}

	/**
	 * Get total number of unique strings
	 */
	size_t size() const {
		return offsets_.size();
	}

	/**
	 * Get total buffer size in bytes
	 */
	size_t buffer_size() const {
		return buffer_.size();
	}

	/**
	 * Clear all interned strings
	 */
	void clear() {
		buffer_.clear();
		offsets_.clear();
		intern_map_.clear();
	}

  private:
	std::vector<char> buffer_;									// Contiguous string storage
	std::vector<uint32_t> offsets_;								// Start offset of each string
	std::unordered_map<std::string_view, uint32_t> intern_map_; // Deduplication map
};

/**
 * Compact Token Representation (16 bytes)
 *
 * Original Token was ~100 bytes. This compact version is 6.25x smaller,
 * allowing 4x more tokens per cache line.
 *
 * Layout:
 * - type (8 bits): TokenType enum
 * - keyword_cat (4 bits): KeywordCategory enum (only for KEYWORD tokens)
 * - flags (4 bits): Various flags (has_math_info, etc.)
 * - value_offset (32 bits): Offset into StringTable
 * - line (24 bits): Line number (supports files up to 16M lines)
 * - column (16 bits): Column number (supports lines up to 64K chars)
 * - extra (8 bits): Reserved for future use
 */
struct CompactToken {
	uint8_t type;			  // TokenType
	uint8_t keyword_cat;	  // KeywordCategory (only valid if type == KEYWORD)
	uint8_t flags;			  // Bit flags
	uint8_t reserved;		  // Padding/reserved
	uint32_t value_offset;	  // Offset into StringTable
	uint32_t line : 24;		  // Line number
	uint32_t column_high : 8; // High bits of column
	uint16_t column_low;	  // Low bits of column (together: 24-bit column)

	// Flag bits
	static constexpr uint8_t FLAG_HAS_MATH_INFO = 0x01;
	static constexpr uint8_t FLAG_HAS_KEYWORD_DATA = 0x02;

	// Constructors
	CompactToken() = default;

	CompactToken(TokenType t, uint32_t val_offset, uint32_t ln, uint32_t col)
		: type(static_cast<uint8_t>(t)), keyword_cat(0), flags(0), reserved(0), value_offset(val_offset), line(ln), column_high(static_cast<uint8_t>((col >> 16) & 0xFF)), column_low(static_cast<uint16_t>(col & 0xFFFF)) {}

	// Getters
	TokenType get_type() const {
		return static_cast<TokenType>(type);
	}

	KeywordCategory get_keyword_category() const {
		return static_cast<KeywordCategory>(keyword_cat);
	}

	uint32_t get_line() const {
		return line;
	}

	uint32_t get_column() const {
		return (static_cast<uint32_t>(column_high) << 16) | column_low;
	}

	bool has_math_info() const {
		return (flags & FLAG_HAS_MATH_INFO) != 0;
	}

	bool has_keyword_data() const {
		return (flags & FLAG_HAS_KEYWORD_DATA) != 0;
	}

	// Setters
	void set_keyword_category(KeywordCategory cat) {
		keyword_cat = static_cast<uint8_t>(cat);
		flags |= FLAG_HAS_KEYWORD_DATA;
	}

	void set_math_info_flag() {
		flags |= FLAG_HAS_MATH_INFO;
	}
};

static_assert(sizeof(CompactToken) == 16, "CompactToken must be exactly 16 bytes");

/**
 * Math Intrinsic Info Storage
 *
 * Separate storage for MathIntrinsicInfo to keep CompactToken small.
 * Only tokens with FLAG_HAS_MATH_INFO set have entries here.
 */
struct MathInfoEntry {
	uint32_t token_index;	// Index of the token this info belongs to
	MathIntrinsicInfo info; // The actual math info
};

/**
 * SIMD-Optimized Token Stream
 *
 * Uses Structure-of-Arrays (SoA) layout for vectorized operations:
 * - types_: Dense array of token types (1 byte each)
 * - tokens_: Full token data for detailed access
 * - strings_: String table for all token values
 */
class TokenStream {
  public:
	TokenStream() {
		// Reserve reasonable capacity
		types_.reserve(8192);
		tokens_.reserve(8192);
	}

	/**
	 * Add a token to the stream
	 */
	void add_token(TokenType type, const std::string &value,
				   int line, int column,
				   const std::optional<KeywordData> &keyword_data = std::nullopt) {
		uint32_t offset = strings_.intern(value);

		CompactToken token(type, offset, static_cast<uint32_t>(line), static_cast<uint32_t>(column));

		if (keyword_data.has_value()) {
			token.set_keyword_category(keyword_data->category);

			if (keyword_data->mathInfo.has_value()) {
				token.set_math_info_flag();
				math_infos_.push_back({static_cast<uint32_t>(tokens_.size()),
									   keyword_data->mathInfo.value()});
			}
		}

		types_.push_back(static_cast<uint8_t>(type));
		tokens_.push_back(token);
	}

	/**
	 * Add a token from the original Token struct
	 */
	void add_token(const Token &tok) {
		add_token(tok.type, tok.value, tok.line, tok.column, tok.keywordData);
	}

	/**
	 * Convert from original token vector
	 */
	void import_tokens(const std::vector<Token> &tokens) {
		clear();
		types_.reserve(tokens.size());
		tokens_.reserve(tokens.size());

		for (const auto &tok : tokens) {
			add_token(tok);
		}
	}

	/**
	 * Convert to original token vector (for compatibility with existing parser)
	 */
	std::vector<Token> export_tokens() const {
		std::vector<Token> result;
		result.reserve(tokens_.size());

		for (size_t i = 0; i < tokens_.size(); ++i) {
			const CompactToken &ct = tokens_[i];

			Token tok(ct.get_type(),
					  std::string(strings_.get(ct.value_offset)),
					  static_cast<int>(ct.get_line()),
					  static_cast<int>(ct.get_column()));

			// Restore keyword data if present
			if (ct.has_keyword_data()) {
				KeywordData kd;
				kd.category = ct.get_keyword_category();

				// Find math info if present
				if (ct.has_math_info()) {
					for (const auto &mi : math_infos_) {
						if (mi.token_index == i) {
							kd.mathInfo = mi.info;
							break;
						}
					}
				}

				tok.keywordData = kd;
			}

			result.push_back(std::move(tok));
		}

		return result;
	}

	/**
	 * Find next token of specific type starting from position (SIMD-accelerated)
	 * Returns tokens_.size() if not found
	 */
	size_t find_next_type(TokenType type, size_t start) const {
		if (start >= types_.size()) {
			return types_.size();
		}

		const uint8_t target = static_cast<uint8_t>(type);

		// Use AVX2 if available and we have enough data
		if (has_avx2() && types_.size() - start >= 32) {
			return find_next_type_avx2(target, start);
		}

		// Fallback to SSE or scalar
		return find_next_type_scalar(target, start);
	}

	/**
	 * Find all tokens of specific type (SIMD-accelerated)
	 */
	std::vector<size_t> find_all_type(TokenType type) const {
		std::vector<size_t> result;
		size_t pos = 0;

		while (pos < types_.size()) {
			pos = find_next_type(type, pos);
			if (pos < types_.size()) {
				result.push_back(pos);
				++pos;
			}
		}

		return result;
	}

	/**
	 * Count tokens of specific type (SIMD-accelerated)
	 */
	size_t count_type(TokenType type) const {
		const uint8_t target = static_cast<uint8_t>(type);
		size_t count = 0;

		if (has_avx2() && types_.size() >= 32) {
			count = count_type_avx2(target);
		} else {
			for (uint8_t t : types_) {
				if (t == target)
					++count;
			}
		}

		return count;
	}

	// Accessors
	size_t size() const { return tokens_.size(); }
	bool empty() const { return tokens_.empty(); }

	const CompactToken &operator[](size_t i) const { return tokens_[i]; }
	CompactToken &operator[](size_t i) { return tokens_[i]; }

	TokenType get_type(size_t i) const { return static_cast<TokenType>(types_[i]); }
	std::string_view get_value(size_t i) const { return strings_.get(tokens_[i].value_offset); }

	const StringTable &strings() const { return strings_; }
	const std::vector<uint8_t> &types() const { return types_; }
	const std::vector<CompactToken> &tokens() const { return tokens_; }

	void clear() {
		types_.clear();
		tokens_.clear();
		math_infos_.clear();
		strings_.clear();
	}

	/**
	 * Get memory usage statistics
	 */
	struct MemoryStats {
		size_t types_bytes;
		size_t tokens_bytes;
		size_t strings_bytes;
		size_t math_infos_bytes;
		size_t total_bytes;
	};

	MemoryStats get_memory_stats() const {
		MemoryStats stats;
		stats.types_bytes = types_.capacity() * sizeof(uint8_t);
		stats.tokens_bytes = tokens_.capacity() * sizeof(CompactToken);
		stats.strings_bytes = strings_.buffer_size();
		stats.math_infos_bytes = math_infos_.capacity() * sizeof(MathInfoEntry);
		stats.total_bytes = stats.types_bytes + stats.tokens_bytes +
							stats.strings_bytes + stats.math_infos_bytes;
		return stats;
	}

  private:
	std::vector<uint8_t> types_;			// Dense type array for SIMD scanning
	std::vector<CompactToken> tokens_;		// Full token data
	std::vector<MathInfoEntry> math_infos_; // Math intrinsic info (sparse)
	StringTable strings_;					// String interning

	/**
	 * AVX2-accelerated type search
	 */
	size_t find_next_type_avx2(uint8_t target, size_t start) const {
		simd256_t target_vec = simd_set1_epi8_256(static_cast<int8_t>(target));

		// Process 32 bytes at a time
		size_t i = start;
		for (; i + 32 <= types_.size(); i += 32) {
			simd256_t chunk = simd_loadu_256(&types_[i]);
			simd256_t cmp = simd_cmpeq_epi8_256(chunk, target_vec);
			uint32_t mask = simd_movemask_epi8_256(cmp);

			if (mask != 0) {
				return i + count_trailing_zeros(mask);
			}
		}

		// Handle remaining bytes
		return find_next_type_scalar(target, i);
	}

	/**
	 * Scalar fallback for type search
	 */
	size_t find_next_type_scalar(uint8_t target, size_t start) const {
		for (size_t i = start; i < types_.size(); ++i) {
			if (types_[i] == target) {
				return i;
			}
		}
		return types_.size();
	}

	/**
	 * AVX2-accelerated type counting
	 */
	size_t count_type_avx2(uint8_t target) const {
		simd256_t target_vec = simd_set1_epi8_256(static_cast<int8_t>(target));
		size_t count = 0;

		size_t i = 0;
		for (; i + 32 <= types_.size(); i += 32) {
			simd256_t chunk = simd_loadu_256(&types_[i]);
			simd256_t cmp = simd_cmpeq_epi8_256(chunk, target_vec);
			uint32_t mask = simd_movemask_epi8_256(cmp);
			count += popcount32(mask);
		}

		// Handle remaining bytes
		for (; i < types_.size(); ++i) {
			if (types_[i] == target)
				++count;
		}

		return count;
	}
};

#endif // TOKEN_STREAM_H
