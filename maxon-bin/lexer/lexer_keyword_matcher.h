#ifndef LEXER_KEYWORD_MATCHER_H
#define LEXER_KEYWORD_MATCHER_H

/**
 * Keyword Matcher
 *
 * Provides fast keyword recognition using a perfect hash function.
 * Also serves as the single source of truth for LSP metadata.
 */

#include "../lexer.h"
#include "lexer_platform.h"
#include <array>
#include <cstring>
#include <string>
#include <string_view>
#include <vector>

/**
 * LSP CompletionItemKind values
 * These values match the LSP specification for CompletionItemKind
 */
enum class KeywordCompletionKind : uint8_t {
	Text = 1,
	Method = 2,
	Function = 3,
	Constructor = 4,
	Field = 5,
	Variable = 6,
	Class = 7,
	Interface = 8,
	Module = 9,
	Property = 10,
	Unit = 11,
	Value = 12,
	Enum = 13,
	Keyword = 14,
	Snippet = 15,
	Color = 16,
	File = 17,
	Reference = 18,
	Folder = 19,
	EnumMember = 20,
	Constant = 21,
	Struct = 22,
	Event = 23,
	Operator = 24,
	TypeParameter = 25
};

/**
 * Math intrinsic info stored in keyword entry (uses const char* for static init safety)
 */
struct KeywordMathInfo {
	MathIntrinsicKind kind;
	const char *runtimeFunctionName; // For RuntimeFunction kind
	const char *returnType;			 // "int" or "float"
};

/**
 * Keyword entry in the hash table
 */
struct KeywordEntry {
	const char *keyword;				  // The keyword string (null-terminated)
	const char *description;			  // Human-readable description
	const char *insertText;				  // Text to insert (can include snippet placeholders)
	uint8_t length;						  // Length of the keyword
	KeywordCategory category;			  // Keyword category
	KeywordCompletionKind completionKind; // LSP completion item kind
	bool has_math_info;					  // Whether this keyword has math intrinsic info
	KeywordMathInfo math_info;			  // Math intrinsic info (only valid if has_math_info)
	bool isBlockKeyword;				  // Whether this keyword starts a block (has end 'label')
	bool isNamedBlock;					  // Whether the block uses identifier as label (function, struct, enum, interface)
};

/**
 * LSP keyword information for completion and hover
 */
struct KeywordLSPInfo {
	std::string name;
	std::string documentation;
	std::string insertText;
	KeywordCompletionKind completionKind;
	KeywordCategory category;
	std::string returnType; // For math intrinsics: the return type (e.g., "int", "float")
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
		// Types
		// Note: 'char' and 'string' are NOT keywords - they are stdlib struct types
		add_keyword("int", KeywordCategory::Type,
					"Signed 64-bit integer type",
					"int",
					KeywordCompletionKind::Keyword);
		add_keyword("float", KeywordCategory::Type,
					"64-bit floating-point type (IEEE 754 double)",
					"float",
					KeywordCompletionKind::Keyword);
		add_keyword("byte", KeywordCategory::Type,
					"8-bit unsigned integer type (0-255)",
					"byte",
					KeywordCompletionKind::Keyword);
		add_keyword("bool", KeywordCategory::Type,
					"Boolean type (true or false)",
					"bool",
					KeywordCompletionKind::Keyword);
		add_keyword("nothing", KeywordCategory::Type,
					"Nothing type - indicates function returns no value",
					"nothing",
					KeywordCompletionKind::Keyword);

		// Control flow
		add_keyword("if", KeywordCategory::ControlFlow,
					"Conditional statement - executes code block if condition is true",
					"if $1\n\t$2\nend 'if'",
					KeywordCompletionKind::Keyword,
					true, false); // isBlockKeyword=true, isNamedBlock=false (uses 'if' as label)
		add_keyword("then", KeywordCategory::ControlFlow,
					"Single-line if body - use after condition for one-line statements",
					"then",
					KeywordCompletionKind::Keyword);
		add_keyword("else", KeywordCategory::ControlFlow,
					"Alternative branch - executed when if condition is false",
					"else\n\t$1",
					KeywordCompletionKind::Keyword);
		add_keyword("while", KeywordCategory::ControlFlow,
					"Loop statement - repeats while condition is true",
					"while $1\n\t$2\nend 'while'",
					KeywordCompletionKind::Keyword,
					true, false); // isBlockKeyword=true, isNamedBlock=false
		add_keyword("for", KeywordCategory::ControlFlow,
					"For loop - iterates over a range or collection",
					"for $1 in $2\n\t$3\nend 'for'",
					KeywordCompletionKind::Keyword,
					true, false); // isBlockKeyword=true, isNamedBlock=false
		add_keyword("in", KeywordCategory::ControlFlow,
					"Iterator keyword in for loop",
					"in",
					KeywordCompletionKind::Keyword);
		add_keyword("end", KeywordCategory::ControlFlow,
					"Block terminator - ends function, loop, or control flow block",
					"end '$1'",
					KeywordCompletionKind::Keyword);
		add_keyword("return", KeywordCategory::ControlFlow,
					"Return from function with optional value",
					"return $1",
					KeywordCompletionKind::Keyword);
		add_keyword("break", KeywordCategory::ControlFlow,
					"Exit loop immediately",
					"break",
					KeywordCompletionKind::Keyword);
		add_keyword("continue", KeywordCategory::ControlFlow,
					"Skip to next iteration of loop",
					"continue",
					KeywordCompletionKind::Keyword);
		add_keyword("match", KeywordCategory::ControlFlow,
					"Pattern matching statement - matches value against cases",
					"match $1\n\t$2:\n\t\t$3\n\tdefault:\n\t\t$4\nend 'match'",
					KeywordCompletionKind::Keyword,
					true, false); // isBlockKeyword=true, isNamedBlock=false
		add_keyword("default", KeywordCategory::ControlFlow,
					"Default case in match statement - executed when no other case matches",
					"default:",
					KeywordCompletionKind::Keyword);
		add_keyword("fallthrough", KeywordCategory::ControlFlow,
					"Continue to next case in match statement",
					"fallthrough",
					KeywordCompletionKind::Keyword);
		add_keyword("gives", KeywordCategory::ControlFlow,
					"Yield value from match expression",
					"gives $1",
					KeywordCompletionKind::Keyword);

		// Declarations
		add_keyword("function", KeywordCategory::Declaration,
					"Function declaration - defines a named function",
					"function $1($2) returns $3\n\t$4\nend '$1'",
					KeywordCompletionKind::Keyword,
					true, true); // isBlockKeyword=true, isNamedBlock=true (uses function name as label)
		add_keyword("returns", KeywordCategory::Declaration,
					"Returns keyword - specifies function return type",
					"returns",
					KeywordCompletionKind::Keyword);
		add_keyword("var", KeywordCategory::Declaration,
					"Mutable variable declaration",
					"var $1 = $2",
					KeywordCompletionKind::Keyword);
		add_keyword("let", KeywordCategory::Declaration,
					"Immutable variable declaration",
					"let $1 = $2",
					KeywordCompletionKind::Keyword);
		add_keyword("type", KeywordCategory::Declaration,
					"Type declaration - defines a composite type",
					"type $1\n\t$2\nend '$1'",
					KeywordCompletionKind::Keyword,
					true, true); // isBlockKeyword=true, isNamedBlock=true
		add_keyword("enum", KeywordCategory::Declaration,
					"Enumeration type declaration - defines a type with named values",
					"enum $1\n\t$2\nend '$1'",
					KeywordCompletionKind::Keyword,
					true, true); // isBlockKeyword=true, isNamedBlock=true
		add_keyword("interface", KeywordCategory::Declaration,
					"Interface declaration - defines a contract for types",
					"interface $1\n\t$2\nend '$1'",
					KeywordCompletionKind::Keyword,
					true, true); // isBlockKeyword=true, isNamedBlock=true
		add_keyword("associatedtype", KeywordCategory::Declaration,
					"Associated type declaration in interface",
					"associatedtype $1",
					KeywordCompletionKind::Keyword);
		add_keyword("uses", KeywordCategory::Declaration,
					"Interface associated type constraint",
					"uses $1",
					KeywordCompletionKind::Keyword);
		add_keyword("with", KeywordCategory::Declaration,
					"Struct associated type binding",
					"with $1 = $2",
					KeywordCompletionKind::Keyword);
		add_keyword("extends", KeywordCategory::Declaration,
					"Interface inheritance - extends another interface",
					"extends $1",
					KeywordCompletionKind::Keyword);
		add_keyword("export", KeywordCategory::Declaration,
					"Export declaration - makes item visible to other modules",
					"export",
					KeywordCompletionKind::Keyword);
		add_keyword("extern", KeywordCategory::Declaration,
					"External declaration - declares item implemented elsewhere",
					"extern",
					KeywordCompletionKind::Keyword);
		add_keyword("static", KeywordCategory::Declaration,
					"Static function - belongs to type but has no implicit self parameter",
					"static function $1($2) returns $3\n\t$4\nend '$1'",
					KeywordCompletionKind::Keyword);
		add_keyword("from", KeywordCategory::Declaration,
					"Map key type specifier",
					"from",
					KeywordCompletionKind::Keyword);
		add_keyword("to", KeywordCategory::Declaration,
					"Map value type specifier",
					"to",
					KeywordCompletionKind::Keyword);
		add_keyword("array", KeywordCategory::Type,
					"Array type - use 'array of T' for collections",
					"array of $1",
					KeywordCompletionKind::Keyword);
		add_keyword("of", KeywordCategory::Type,
					"Type constructor keyword - used with 'array of T'",
					"of",
					KeywordCompletionKind::Keyword);

		// Math intrinsics
		add_math_keyword("sqrt", KeywordCategory::MathIntrinsic,
						 "Square root - returns the square root of a float value",
						 "sqrt($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::Intrinsic, "", "float"});
		add_math_keyword("abs", KeywordCategory::MathIntrinsic,
						 "Absolute value - returns the non-negative value",
						 "abs($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::Intrinsic, "", "float"});
		add_math_keyword("floor", KeywordCategory::MathIntrinsic,
						 "Floor function - rounds down to nearest integer",
						 "floor($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::Intrinsic, "", "int"});
		add_math_keyword("ceil", KeywordCategory::MathIntrinsic,
						 "Ceiling function - rounds up to nearest integer",
						 "ceil($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::Intrinsic, "", "int"});
		add_math_keyword("round", KeywordCategory::MathIntrinsic,
						 "Round to nearest integer",
						 "round($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::Intrinsic, "", "int"});
		add_math_keyword("trunc", KeywordCategory::MathIntrinsic,
						 "Truncate to integer - removes fractional part",
						 "trunc($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::DirectCast, "", "int"});
		add_math_keyword("sin", KeywordCategory::MathIntrinsic,
						 "Sine function - returns sine of angle in radians",
						 "sin($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::RuntimeFunction, "sin", "float"});
		add_math_keyword("cos", KeywordCategory::MathIntrinsic,
						 "Cosine function - returns cosine of angle in radians",
						 "cos($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::RuntimeFunction, "cos", "float"});
		add_math_keyword("tan", KeywordCategory::MathIntrinsic,
						 "Tangent function - returns tangent of angle in radians",
						 "tan($1)",
						 KeywordCompletionKind::Function,
						 {MathIntrinsicKind::RuntimeFunction, "tan", "float"});

		// Literals
		add_keyword("true", KeywordCategory::Literal,
					"Boolean true literal",
					"true",
					KeywordCompletionKind::Constant);
		add_keyword("false", KeywordCategory::Literal,
					"Boolean false literal",
					"false",
					KeywordCompletionKind::Constant);
		add_keyword("nil", KeywordCategory::Literal,
					"Null/absent value literal",
					"nil",
					KeywordCompletionKind::Constant);

		// Operators
		add_keyword("as", KeywordCategory::Operator,
					"Type cast operator - converts value to specified type",
					"as $1",
					KeywordCompletionKind::Operator);
		add_keyword("and", KeywordCategory::Operator,
					"Logical AND operator - returns true if both operands are true",
					"and",
					KeywordCompletionKind::Operator);
		add_keyword("or", KeywordCategory::Operator,
					"Logical OR operator - returns true if either operand is true",
					"or",
					KeywordCompletionKind::Operator);
		add_keyword("not", KeywordCategory::Operator,
					"Logical NOT operator - negates boolean value",
					"not",
					KeywordCompletionKind::Operator);
		add_keyword("mod", KeywordCategory::Operator,
					"Modulo operator - returns remainder of integer division",
					"mod",
					KeywordCompletionKind::Operator);
		add_keyword("is", KeywordCategory::Operator,
					"Interface conformance check - tests if type implements interface",
					"is $1",
					KeywordCompletionKind::Operator);

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

	/**
	 * Get the number of registered keywords
	 */
	static size_t keyword_count() {
		if (!initialized_)
			initialize();
		return keyword_count_;
	}

	/**
	 * Get a keyword entry by index (for iteration)
	 */
	static const KeywordEntry &get_keyword(size_t index) {
		if (!initialized_)
			initialize();
		return keywords_[index];
	}

	/**
	 * Iterate over all keywords with a callback
	 */
	template <typename Func>
	static void for_each_keyword(Func &&func) {
		if (!initialized_)
			initialize();
		for (size_t i = 0; i < keyword_count_; ++i) {
			func(keywords_[i]);
		}
	}

	/**
	 * Get all keywords with LSP metadata
	 * Returns a vector of KeywordLSPInfo for use by LSP server
	 */
	static std::vector<KeywordLSPInfo> getLSPKeywordInfo() {
		if (!initialized_)
			initialize();

		std::vector<KeywordLSPInfo> result;
		result.reserve(keyword_count_);

		for (size_t i = 0; i < keyword_count_; ++i) {
			const KeywordEntry &entry = keywords_[i];
			result.push_back({entry.keyword,
							  entry.description,
							  entry.insertText,
							  entry.completionKind,
							  entry.category,
							  entry.has_math_info ? entry.math_info.returnType : ""});
		}

		return result;
	}

	/**
	 * Get keywords that start named blocks (function, struct, enum, interface)
	 * These keywords are followed by a name and have an end 'name' terminator
	 */
	static std::vector<std::string> getNamedBlockKeywords() {
		if (!initialized_)
			initialize();

		std::vector<std::string> result;
		for (size_t i = 0; i < keyword_count_; ++i) {
			if (keywords_[i].isNamedBlock) {
				result.push_back(keywords_[i].keyword);
			}
		}
		return result;
	}

	/**
	 * Get all keywords that start blocks with labels
	 * Includes both named blocks (function, struct) and control flow blocks (if, while, for, match)
	 */
	static std::vector<std::string> getAllBlockKeywords() {
		if (!initialized_)
			initialize();

		std::vector<std::string> result;
		for (size_t i = 0; i < keyword_count_; ++i) {
			if (keywords_[i].isBlockKeyword) {
				result.push_back(keywords_[i].keyword);
			}
		}
		return result;
	}

	/**
	 * Get keywords matching a prefix for completion
	 * Returns keywords whose names start with the given prefix
	 */
	static std::vector<KeywordLSPInfo> getKeywordsForCompletion(const std::string &prefix) {
		if (!initialized_)
			initialize();

		std::vector<KeywordLSPInfo> result;

		for (size_t i = 0; i < keyword_count_; ++i) {
			const KeywordEntry &entry = keywords_[i];
			std::string_view keyword(entry.keyword, entry.length);

			// Check if keyword starts with prefix (case-sensitive)
			if (prefix.empty() || (keyword.length() >= prefix.length() &&
								   keyword.substr(0, prefix.length()) == prefix)) {
				result.push_back({entry.keyword,
								  entry.description,
								  entry.insertText,
								  entry.completionKind,
								  entry.category,
								  entry.has_math_info ? entry.math_info.returnType : ""});
			}
		}

		return result;
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
	static void add_keyword(const char *keyword, KeywordCategory category,
							const char *description, const char *insertText,
							KeywordCompletionKind completionKind,
							bool isBlockKeyword = false, bool isNamedBlock = false) {
		size_t len = strlen(keyword);
		uint32_t hash = compute_hash(keyword, len);

		// Store in keywords array
		size_t idx = keyword_count_++;
		keywords_[idx] = {keyword, description, insertText, static_cast<uint8_t>(len), category, completionKind, false, {}, isBlockKeyword, isNamedBlock};

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
								 const char *description, const char *insertText,
								 KeywordCompletionKind completionKind,
								 const KeywordMathInfo &math_info) {
		size_t len = strlen(keyword);
		uint32_t hash = compute_hash(keyword, len);

		size_t idx = keyword_count_++;
		keywords_[idx] = {keyword, description, insertText,
						  static_cast<uint8_t>(len), category, completionKind,
						  true, math_info, false, false};

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
		// Maxon keywords start with: a,b,c,d,e,f,g,i,l,m,n,o,r,s,t,u,v,w
		// Characters that CANNOT start a keyword:
		// h, j, k, p, q, x, y, z
		// Also uppercase letters and digits

		// Quick lookup table
		static constexpr uint32_t not_keyword_chars[] = {
			// Lowercase h,j,k,p,q,x,y,z
			(1u << ('h' - 'a')) |
			(1u << ('j' - 'a')) | (1u << ('k' - 'a')) | (1u << ('p' - 'a')) |
			(1u << ('q' - 'a')) | (1u << ('x' - 'a')) |
			(1u << ('y' - 'a')) | (1u << ('z' - 'a'))};

		if (first >= 'a' && first <= 'z') {
			return (not_keyword_chars[0] & (1u << (first - 'a'))) != 0;
		}

		// Uppercase and other chars can't start keywords
		return first < 'a' || first > 'z';
	}
};

#endif // LEXER_KEYWORD_MATCHER_H
