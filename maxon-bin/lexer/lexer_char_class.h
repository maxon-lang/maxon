#ifndef LEXER_CHAR_CLASS_H
#define LEXER_CHAR_CLASS_H

/**
 * SIMD Character Classification
 *
 * Provides vectorized character classification for lexing operations.
 * Processes 16-32 characters in parallel using SSE4.2/AVX2 instructions.
 */

#include "lexer_platform.h"
#include <cstdint>

/**
 * Character class bitmasks for classification results
 */
enum class CharClass : uint8_t {
	None = 0,
	Whitespace = 1 << 0, // space, tab, newline, carriage return
	Digit = 1 << 1,		 // 0-9
	Alpha = 1 << 2,		 // A-Z, a-z
	Upper = 1 << 3,		 // A-Z
	Lower = 1 << 4,		 // a-z
	AlphaNum = 1 << 5,	 // A-Z, a-z, 0-9
	Underscore = 1 << 6, // _
	Identifier = 1 << 7	 // A-Z, a-z, 0-9, _
};

/**
 * SIMD Character Classifier
 *
 * Classifies 16-32 characters at once using SIMD comparisons.
 * Returns a bitmask indicating which characters match the query.
 */
class CharClassifier {
  public:
	// ========================================================================
	// Single Character Classification (Scalar)
	// ========================================================================

	static bool is_digit(char c) {
		return c >= '0' && c <= '9';
	}

	static bool is_alpha(char c) {
		return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
	}

	static bool is_alnum(char c) {
		return is_digit(c) || is_alpha(c);
	}

	static bool is_ident_start(char c) {
		return is_alpha(c) || c == '_';
	}

	static bool is_ident_char(char c) {
		return is_alnum(c) || c == '_';
	}

	static bool is_whitespace(char c) {
		return c == ' ' || c == '\t' || c == '\n' || c == '\r';
	}

	static bool is_hex_digit(char c) {
		return is_digit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
	}

	static bool is_binary_digit(char c) {
		return c == '0' || c == '1';
	}

	static bool is_octal_digit(char c) {
		return c >= '0' && c <= '7';
	}

	static int hex_digit_value(char c) {
		if (c >= '0' && c <= '9')
			return c - '0';
		if (c >= 'a' && c <= 'f')
			return c - 'a' + 10;
		if (c >= 'A' && c <= 'F')
			return c - 'A' + 10;
		return -1;
	}

	// ========================================================================
	// SSE4.2 Classification (16 characters at a time)
	// ========================================================================

	/**
	 * Check 16 characters for digits (0-9)
	 * Returns a 16-bit mask: bit N is 1 if char N is a digit
	 */
	static uint16_t classify_digits_sse(simd128_t chars) {
		// Range check: '0' <= char <= '9'
		simd128_t is_digit = simd_in_range_128(chars, '0', '9');
		return simd_movemask_epi8_128(is_digit);
	}

	/**
	 * Check 16 characters for alphabetic (A-Z, a-z)
	 * Returns a 16-bit mask: bit N is 1 if char N is alphabetic
	 */
	static uint16_t classify_alpha_sse(simd128_t chars) {
		// Check A-Z and a-z ranges
		simd128_t is_upper = simd_in_range_128(chars, 'A', 'Z');
		simd128_t is_lower = simd_in_range_128(chars, 'a', 'z');
		simd128_t is_alpha = simd_or_128(is_upper, is_lower);
		return simd_movemask_epi8_128(is_alpha);
	}

	/**
	 * Check 16 characters for alphanumeric (A-Z, a-z, 0-9)
	 * Returns a 16-bit mask: bit N is 1 if char N is alphanumeric
	 */
	static uint16_t classify_alnum_sse(simd128_t chars) {
		simd128_t is_upper = simd_in_range_128(chars, 'A', 'Z');
		simd128_t is_lower = simd_in_range_128(chars, 'a', 'z');
		simd128_t is_digit = simd_in_range_128(chars, '0', '9');
		simd128_t is_alnum = simd_or_128(simd_or_128(is_upper, is_lower), is_digit);
		return simd_movemask_epi8_128(is_alnum);
	}

	/**
	 * Check 16 characters for identifier characters (A-Z, a-z, 0-9, _)
	 * Returns a 16-bit mask: bit N is 1 if char N is valid in an identifier
	 */
	static uint16_t classify_ident_sse(simd128_t chars) {
		simd128_t is_upper = simd_in_range_128(chars, 'A', 'Z');
		simd128_t is_lower = simd_in_range_128(chars, 'a', 'z');
		simd128_t is_digit = simd_in_range_128(chars, '0', '9');
		simd128_t is_underscore = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128('_'));

		simd128_t is_ident = simd_or_128(
			simd_or_128(is_upper, is_lower),
			simd_or_128(is_digit, is_underscore));
		return simd_movemask_epi8_128(is_ident);
	}

	/**
	 * Check 16 characters for whitespace (space, tab, newline, CR)
	 * Returns a 16-bit mask: bit N is 1 if char N is whitespace
	 */
	static uint16_t classify_whitespace_sse(simd128_t chars) {
		simd128_t is_space = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128(' '));
		simd128_t is_tab = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128('\t'));
		simd128_t is_newline = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128('\n'));
		simd128_t is_cr = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128('\r'));

		simd128_t is_ws = simd_or_128(
			simd_or_128(is_space, is_tab),
			simd_or_128(is_newline, is_cr));
		return simd_movemask_epi8_128(is_ws);
	}

	/**
	 * Check 16 characters for newline characters (\n or \r)
	 * Returns a 16-bit mask
	 */
	static uint16_t classify_newline_sse(simd128_t chars) {
		simd128_t is_newline = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128('\n'));
		simd128_t is_cr = simd_cmpeq_epi8_128(chars, simd_set1_epi8_128('\r'));
		simd128_t is_nl = simd_or_128(is_newline, is_cr);
		return simd_movemask_epi8_128(is_nl);
	}

	// ========================================================================
	// AVX2 Classification (32 characters at a time)
	// ========================================================================

	/**
	 * Check 32 characters for digits (0-9)
	 * Returns a 32-bit mask
	 */
	static uint32_t classify_digits_avx2(simd256_t chars) {
		simd256_t is_digit = simd_in_range_256(chars, '0', '9');
		return simd_movemask_epi8_256(is_digit);
	}

	/**
	 * Check 32 characters for alphabetic (A-Z, a-z)
	 * Returns a 32-bit mask
	 */
	static uint32_t classify_alpha_avx2(simd256_t chars) {
		simd256_t is_upper = simd_in_range_256(chars, 'A', 'Z');
		simd256_t is_lower = simd_in_range_256(chars, 'a', 'z');
		simd256_t is_alpha = simd_or_256(is_upper, is_lower);
		return simd_movemask_epi8_256(is_alpha);
	}

	/**
	 * Check 32 characters for alphanumeric (A-Z, a-z, 0-9)
	 * Returns a 32-bit mask
	 */
	static uint32_t classify_alnum_avx2(simd256_t chars) {
		simd256_t is_upper = simd_in_range_256(chars, 'A', 'Z');
		simd256_t is_lower = simd_in_range_256(chars, 'a', 'z');
		simd256_t is_digit = simd_in_range_256(chars, '0', '9');
		simd256_t is_alnum = simd_or_256(simd_or_256(is_upper, is_lower), is_digit);
		return simd_movemask_epi8_256(is_alnum);
	}

	/**
	 * Check 32 characters for identifier characters (A-Z, a-z, 0-9, _)
	 * Returns a 32-bit mask
	 */
	static uint32_t classify_ident_avx2(simd256_t chars) {
		simd256_t is_upper = simd_in_range_256(chars, 'A', 'Z');
		simd256_t is_lower = simd_in_range_256(chars, 'a', 'z');
		simd256_t is_digit = simd_in_range_256(chars, '0', '9');
		simd256_t is_underscore = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256('_'));

		simd256_t is_ident = simd_or_256(
			simd_or_256(is_upper, is_lower),
			simd_or_256(is_digit, is_underscore));
		return simd_movemask_epi8_256(is_ident);
	}

	/**
	 * Check 32 characters for whitespace
	 * Returns a 32-bit mask
	 */
	static uint32_t classify_whitespace_avx2(simd256_t chars) {
		simd256_t is_space = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256(' '));
		simd256_t is_tab = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256('\t'));
		simd256_t is_newline = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256('\n'));
		simd256_t is_cr = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256('\r'));

		simd256_t is_ws = simd_or_256(
			simd_or_256(is_space, is_tab),
			simd_or_256(is_newline, is_cr));
		return simd_movemask_epi8_256(is_ws);
	}

	/**
	 * Check 32 characters for newline characters
	 * Returns a 32-bit mask
	 */
	static uint32_t classify_newline_avx2(simd256_t chars) {
		simd256_t is_newline = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256('\n'));
		simd256_t is_cr = simd_cmpeq_epi8_256(chars, simd_set1_epi8_256('\r'));
		simd256_t is_nl = simd_or_256(is_newline, is_cr);
		return simd_movemask_epi8_256(is_nl);
	}

	// ========================================================================
	// Batch Classification from Memory
	// ========================================================================

	/**
	 * Find the length of contiguous identifier characters starting at position
	 * Uses SIMD to scan 16-32 chars at a time
	 *
	 * @param src Source string
	 * @param pos Starting position
	 * @param len Length of source string
	 * @return Number of identifier characters found
	 */
	static size_t find_identifier_length(const char *src, size_t pos, size_t len) {
		size_t start = pos;

		// Use AVX2 if available and we have enough data
		if (has_avx2()) {
			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				uint32_t mask = classify_ident_avx2(chars);

				if (mask != 0xFFFFFFFF) {
					// Found non-identifier character
					uint32_t first_non_ident = count_trailing_zeros(~mask);
					return (pos - start) + first_non_ident;
				}

				pos += 32;
			}
		}

		// Use SSE for remaining data
		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			uint16_t mask = classify_ident_sse(chars);

			if (mask != 0xFFFF) {
				uint32_t first_non_ident = count_trailing_zeros(static_cast<uint32_t>(~mask & 0xFFFF));
				return (pos - start) + first_non_ident;
			}

			pos += 16;
		}

		// Handle remaining characters (< 16)
		while (pos < len && is_ident_char(src[pos])) {
			++pos;
		}

		return pos - start;
	}

	/**
	 * Find the length of contiguous digit characters starting at position
	 * Uses SIMD to scan 16-32 chars at a time
	 */
	static size_t find_digit_length(const char *src, size_t pos, size_t len) {
		size_t start = pos;

		if (has_avx2()) {
			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				uint32_t mask = classify_digits_avx2(chars);

				if (mask != 0xFFFFFFFF) {
					uint32_t first_non_digit = count_trailing_zeros(~mask);
					return (pos - start) + first_non_digit;
				}

				pos += 32;
			}
		}

		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			uint16_t mask = classify_digits_sse(chars);

			if (mask != 0xFFFF) {
				uint32_t first_non_digit = count_trailing_zeros(static_cast<uint32_t>(~mask & 0xFFFF));
				return (pos - start) + first_non_digit;
			}

			pos += 16;
		}

		while (pos < len && is_digit(src[pos])) {
			++pos;
		}

		return pos - start;
	}

	/**
	 * Skip whitespace starting at position
	 * Returns the position of the first non-whitespace character
	 */
	static size_t skip_whitespace(const char *src, size_t pos, size_t len) {
		if (has_avx2()) {
			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				uint32_t mask = classify_whitespace_avx2(chars);

				if (mask != 0xFFFFFFFF) {
					// Found non-whitespace
					uint32_t first_non_ws = count_trailing_zeros(~mask);
					return pos + first_non_ws;
				}

				pos += 32;
			}
		}

		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			uint16_t mask = classify_whitespace_sse(chars);

			if (mask != 0xFFFF) {
				uint32_t first_non_ws = count_trailing_zeros(static_cast<uint32_t>(~mask & 0xFFFF));
				return pos + first_non_ws;
			}

			pos += 16;
		}

		// Handle remaining characters
		while (pos < len && is_whitespace(src[pos])) {
			++pos;
		}

		return pos;
	}

	/**
	 * Find next newline character starting at position
	 * Returns len if no newline found
	 */
	static size_t find_newline(const char *src, size_t pos, size_t len) {
		if (has_avx2()) {
			simd256_t newline = simd_set1_epi8_256('\n');

			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				simd256_t cmp = simd_cmpeq_epi8_256(chars, newline);
				uint32_t mask = simd_movemask_epi8_256(cmp);

				if (mask != 0) {
					return pos + count_trailing_zeros(mask);
				}

				pos += 32;
			}
		}

		simd128_t newline = simd_set1_epi8_128('\n');

		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			simd128_t cmp = simd_cmpeq_epi8_128(chars, newline);
			uint16_t mask = simd_movemask_epi8_128(cmp);

			if (mask != 0) {
				return pos + count_trailing_zeros(static_cast<uint32_t>(mask));
			}

			pos += 16;
		}

		// Handle remaining characters
		while (pos < len && src[pos] != '\n') {
			++pos;
		}

		return pos;
	}

	/**
	 * Find specific character in string
	 * Returns len if not found
	 */
	static size_t find_char(const char *src, size_t pos, size_t len, char target) {
		if (has_avx2()) {
			simd256_t target_vec = simd_set1_epi8_256(target);

			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				simd256_t cmp = simd_cmpeq_epi8_256(chars, target_vec);
				uint32_t mask = simd_movemask_epi8_256(cmp);

				if (mask != 0) {
					return pos + count_trailing_zeros(mask);
				}

				pos += 32;
			}
		}

		simd128_t target_vec = simd_set1_epi8_128(target);

		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			simd128_t cmp = simd_cmpeq_epi8_128(chars, target_vec);
			uint16_t mask = simd_movemask_epi8_128(cmp);

			if (mask != 0) {
				return pos + count_trailing_zeros(static_cast<uint32_t>(mask));
			}

			pos += 16;
		}

		while (pos < len && src[pos] != target) {
			++pos;
		}

		return pos;
	}

	/**
	 * Find either of two characters (e.g., '*' and '/')
	 * Useful for finding block comment terminators
	 */
	static size_t find_either_char(const char *src, size_t pos, size_t len, char c1, char c2) {
		if (has_avx2()) {
			simd256_t target1 = simd_set1_epi8_256(c1);
			simd256_t target2 = simd_set1_epi8_256(c2);

			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				simd256_t cmp1 = simd_cmpeq_epi8_256(chars, target1);
				simd256_t cmp2 = simd_cmpeq_epi8_256(chars, target2);
				simd256_t cmp = simd_or_256(cmp1, cmp2);
				uint32_t mask = simd_movemask_epi8_256(cmp);

				if (mask != 0) {
					return pos + count_trailing_zeros(mask);
				}

				pos += 32;
			}
		}

		simd128_t target1 = simd_set1_epi8_128(c1);
		simd128_t target2 = simd_set1_epi8_128(c2);

		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			simd128_t cmp1 = simd_cmpeq_epi8_128(chars, target1);
			simd128_t cmp2 = simd_cmpeq_epi8_128(chars, target2);
			simd128_t cmp = simd_or_128(cmp1, cmp2);
			uint16_t mask = simd_movemask_epi8_128(cmp);

			if (mask != 0) {
				return pos + count_trailing_zeros(static_cast<uint32_t>(mask));
			}

			pos += 16;
		}

		while (pos < len && src[pos] != c1 && src[pos] != c2) {
			++pos;
		}

		return pos;
	}

	/**
	 * Count newlines in a range (for updating line numbers)
	 * Returns number of newline characters found
	 */
	static size_t count_newlines(const char *src, size_t start, size_t end) {
		size_t count = 0;
		size_t pos = start;

		if (has_avx2()) {
			simd256_t newline = simd_set1_epi8_256('\n');

			while (pos + 32 <= end) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				simd256_t cmp = simd_cmpeq_epi8_256(chars, newline);
				uint32_t mask = simd_movemask_epi8_256(cmp);
				count += popcount32(mask);
				pos += 32;
			}
		}

		simd128_t newline = simd_set1_epi8_128('\n');

		while (pos + 16 <= end) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			simd128_t cmp = simd_cmpeq_epi8_128(chars, newline);
			uint16_t mask = simd_movemask_epi8_128(cmp);
			count += popcount32(mask);
			pos += 16;
		}

		// Handle remaining characters
		while (pos < end) {
			if (src[pos] == '\n')
				++count;
			++pos;
		}

		return count;
	}
};

#endif // LEXER_CHAR_CLASS_H
