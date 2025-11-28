#ifndef LEXER_KEYWORD_MATCHER_H
#define LEXER_KEYWORD_MATCHER_H

/**
 * Keyword Matcher
 *
 * Provides fast keyword recognition using a perfect hash function.
 */

#include "../lexer.h"
#include "lexer_platform.h"
#include <array>
#include <cstring>
#include <string_view>

/**
 * Keyword entry in the hash table
 */
struct KeywordEntry {
	const char *keyword;		 // The keyword string (null-terminated)
	uint8_t length;				 // Length of the keyword
	KeywordCategory category;	 // Keyword category
	bool has_math_info;			 // Whether this keyword has math intrinsic info
	MathIntrinsicInfo math_info; // Math intrinsic info (only valid if has_math_info)
};

/**
 * Keyword Matcher using perfect hashing
 *
 * Uses a simple multiply-shift hash on the first 4 bytes of the keyword.
 * The hash table is sized to minimize collisions for Maxon's 47 keywords.
 */
class KeywordMatcher {
  public:
	// Hash table size (must be power of 2)
	static constexpr size_t TABLE_SIZE = 128;

	// Special value indicating empty slot
	static constexpr uint32_t EMPTY_SLOT = 0xFFFFFFFF;

	/**
	 * Initialize the keyword matcher
	 * Builds the hash table from all Maxon keywords
	 */
	static void initialize() {
		if (initialized_)
			return;

		// Initialize all slots as empty
		for (size_t i = 0; i < TABLE_SIZE; ++i) {
			hash_table_[i] = EMPTY_SLOT;
		}

		// Add all keywords to the hash table
		add_keyword("int", KeywordCategory::Type);
		add_keyword("float", KeywordCategory::Type);
		add_keyword("char", KeywordCategory::Type);
		add_keyword("byte", KeywordCategory::Type);
		add_keyword("string", KeywordCategory::Type);
		add_keyword("bool", KeywordCategory::Type);

		add_keyword("if", KeywordCategory::ControlFlow);
		add_keyword("then", KeywordCategory::ControlFlow);
		add_keyword("else", KeywordCategory::ControlFlow);
		add_keyword("while", KeywordCategory::ControlFlow);
		add_keyword("for", KeywordCategory::ControlFlow);
		add_keyword("in", KeywordCategory::ControlFlow);
		add_keyword("end", KeywordCategory::ControlFlow);
		add_keyword("return", KeywordCategory::ControlFlow);
		add_keyword("break", KeywordCategory::ControlFlow);
		add_keyword("continue", KeywordCategory::ControlFlow);

		add_keyword("function", KeywordCategory::Declaration);
		add_keyword("var", KeywordCategory::Declaration);
		add_keyword("let", KeywordCategory::Declaration);
		add_keyword("struct", KeywordCategory::Declaration);
		add_keyword("interface", KeywordCategory::Declaration);
		add_keyword("export", KeywordCategory::Declaration);
		add_keyword("extern", KeywordCategory::Declaration);

		// Math intrinsics
		add_math_keyword("sqrt", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::Intrinsic, "", "float"});
		add_math_keyword("abs", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::Intrinsic, "", "float"});
		add_math_keyword("floor", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::Intrinsic, "", "int"});
		add_math_keyword("ceil", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::Intrinsic, "", "int"});
		add_math_keyword("round", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::Intrinsic, "", "int"});
		add_math_keyword("trunc", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::DirectCast, "", "int"});
		add_math_keyword("sin", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::RuntimeFunction, "sin", "float"});
		add_math_keyword("cos", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::RuntimeFunction, "cos", "float"});
		add_math_keyword("tan", KeywordCategory::MathIntrinsic,
						 {MathIntrinsicKind::RuntimeFunction, "tan", "float"});

		add_keyword("true", KeywordCategory::Literal);
		add_keyword("false", KeywordCategory::Literal);

		add_keyword("as", KeywordCategory::Operator);
		add_keyword("and", KeywordCategory::Operator);
		add_keyword("or", KeywordCategory::Operator);
		add_keyword("not", KeywordCategory::Operator);
		add_keyword("mod", KeywordCategory::Operator);
		add_keyword("is", KeywordCategory::Operator);

		initialized_ = true;
	}

	/**
	 * Try to match a keyword
	 *
	 * @param str Pointer to the start of the identifier
	 * @param len Length of the identifier
	 * @param out_entry Output: keyword entry if matched
	 * @return true if matched a keyword, false if identifier
	 */
	static bool match(const char *str, size_t len, KeywordEntry &out_entry) {
		if (!initialized_)
			initialize();

		// Compute hash
		uint32_t hash = compute_hash(str, len);

		// Linear probing to find the keyword
		// Maximum probes needed is the number of keywords but in practice
		// we should find it quickly or hit an empty slot
		for (size_t probe = 0; probe < 64; ++probe) { // 64 probes max to handle collisions
			uint32_t current_hash = (hash + probe) & (TABLE_SIZE - 1);
			uint32_t slot = hash_table_[current_hash];

			if (slot == EMPTY_SLOT) {
				// Empty slot - keyword not in table
				return false;
			}

			// Check if this slot matches
			const KeywordEntry &entry = keywords_[slot];
			if (entry.length == len && memcmp(entry.keyword, str, len) == 0) {
				out_entry = entry;
				return true;
			}
			// Collision - continue probing
		}

		return false;
	}

	/**
	 * Check if a string is a keyword (simpler interface)
	 */
	static bool is_keyword(const char *str, size_t len) {
		KeywordEntry entry;
		return match(str, len, entry);
	}

	/**
	 * Check if a std::string is a keyword
	 */
	static bool is_keyword(const std::string &str) {
		return is_keyword(str.data(), str.size());
	}

	/**
	 * Get keyword category (returns Literal as default if not found)
	 */
	static KeywordCategory get_category(const char *str, size_t len) {
		KeywordEntry entry;
		if (match(str, len, entry)) {
			return entry.category;
		}
		return KeywordCategory::Literal; // Invalid/not found
	}

  private:
	// Storage for keyword entries
	static inline std::array<KeywordEntry, 64> keywords_;
	static inline size_t keyword_count_ = 0;

	// Hash table: maps hash -> index in keywords_
	static inline std::array<uint32_t, TABLE_SIZE> hash_table_;

	// Initialization flag
	static inline bool initialized_ = false;

	/**
	 * Compute hash for a keyword
	 * Uses Fibonacci hashing on the first 4 bytes
	 */
	static uint32_t compute_hash(const char *str, size_t len) {
		// Load up to 4 bytes as a uint32
		uint32_t prefix = 0;
		size_t copy_len = len < 4 ? len : 4;
		memcpy(&prefix, str, copy_len);

		// Include length in hash to distinguish keywords with same prefix
		prefix ^= static_cast<uint32_t>(len) << 24;

		// Fibonacci hashing (multiply by golden ratio constant)
		uint32_t hash = (prefix * 0x9E3779B1u) >> (32 - 7); // 7 bits for 128-entry table

		return hash & (TABLE_SIZE - 1);
	}

	/**
	 * Add a keyword to the hash table
	 */
	static void add_keyword(const char *keyword, KeywordCategory category) {
		size_t len = strlen(keyword);
		uint32_t hash = compute_hash(keyword, len);

		// Store in keywords array
		size_t idx = keyword_count_++;
		keywords_[idx] = {keyword, static_cast<uint8_t>(len), category, false, {}};

		// Insert into hash table (with linear probing for collisions)
		while (hash_table_[hash] != EMPTY_SLOT) {
			hash = (hash + 1) & (TABLE_SIZE - 1);
		}
		hash_table_[hash] = static_cast<uint32_t>(idx);
	}

	/**
	 * Add a math keyword with intrinsic info
	 */
	static void add_math_keyword(const char *keyword, KeywordCategory category,
								 const MathIntrinsicInfo &math_info) {
		size_t len = strlen(keyword);
		uint32_t hash = compute_hash(keyword, len);

		size_t idx = keyword_count_++;
		keywords_[idx] = {keyword, static_cast<uint8_t>(len), category, true, math_info};

		while (hash_table_[hash] != EMPTY_SLOT) {
			hash = (hash + 1) & (TABLE_SIZE - 1);
		}
		hash_table_[hash] = static_cast<uint32_t>(idx);
	}
};

/**
 * SIMD-accelerated keyword prefix check
 *
 * For very short identifiers (2-3 chars), directly compare against
 * common keywords without hashing.
 */
class KeywordPrefixMatcher {
  public:
	/**
	 * Quick check for 2-character keywords: if, in, or, as
	 */
	static bool is_2char_keyword(const char *str) {
		uint16_t val;
		memcpy(&val, str, 2);

		// Common 2-char keywords
		return val == 0x6669	 // "if" (little-endian)
			   || val == 0x6E69	 // "in"
			   || val == 0x726F	 // "or"
			   || val == 0x7361; // "as"
	}

	/**
	 * Quick check for 3-character keywords: int, for, var, let, end, mod, not
	 */
	static bool is_3char_keyword(const char *str) {
		// Load 4 bytes, mask off the last byte
		uint32_t val;
		memcpy(&val, str, 4);
		val &= 0x00FFFFFF; // Keep only first 3 bytes

		// Common 3-char keywords (little-endian)
		return val == 0x00746E69	 // "int"
			   || val == 0x00726F66	 // "for"
			   || val == 0x00726176	 // "var"
			   || val == 0x0074656C	 // "let"
			   || val == 0x00646E65	 // "end"
			   || val == 0x00646F6D	 // "mod"
			   || val == 0x00746F6E; // "not"
	}

	/**
	 * Quick prefix rejection
	 * Returns true if the string CANNOT be a keyword based on first char
	 */
	static bool not_keyword_prefix(char first) {
		// Maxon keywords start with: a,b,c,e,f,i,l,m,n,o,r,s,t,v,w
		// Characters that CANNOT start a keyword:
		// d, g, h, j, k, p, q, u, x, y, z
		// Also uppercase letters and digits

		// Quick lookup table
		static constexpr uint32_t not_keyword_chars[] = {
			// Lowercase d,g,h,j,k,p,q,u,x,y,z
			(1u << ('d' - 'a')) | (1u << ('g' - 'a')) | (1u << ('h' - 'a')) |
			(1u << ('j' - 'a')) | (1u << ('k' - 'a')) | (1u << ('p' - 'a')) |
			(1u << ('q' - 'a')) | (1u << ('u' - 'a')) | (1u << ('x' - 'a')) |
			(1u << ('y' - 'a')) | (1u << ('z' - 'a'))};

		if (first >= 'a' && first <= 'z') {
			return (not_keyword_chars[0] & (1u << (first - 'a'))) != 0;
		}

		// Uppercase and other chars can't start keywords
		return first < 'a' || first > 'z';
	}
};

#endif // LEXER_KEYWORD_MATCHER_H
