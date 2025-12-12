#ifndef LEXER_NUMBER_PARSER_H
#define LEXER_NUMBER_PARSER_H

/**
 * Number Parser
 *
 * Provides SIMD-accelerated parsing of integer and floating-point literals.
 */

#include "lexer_char_class.h"
#include <cmath>
#include <cstdint>
#include <stdexcept>
#include <string>

/**
 * Result of number parsing
 */
struct ParsedNumber {
	enum class Type {
		Integer,
		Float
	};

	Type type;
	union {
		int64_t int_value;
		double float_value;
	};
	size_t chars_consumed;		// Number of characters consumed from input
	std::string literal_string; // Original literal string (for float repr)

	ParsedNumber() : type(Type::Integer), int_value(0), chars_consumed(0) {}
};

/**
 * SIMD Number Parser
 */
class NumberParser {
  private:
	// Error reporting helper - throws runtime_error with location info
	[[noreturn]] static void reportError(const std::string &message, int line, int column) {
		throw std::runtime_error(message + "\n  Location: line " +
								 std::to_string(line) + ", column " +
								 std::to_string(column));
	}

  public:
	/**
	 * Parse a number starting at position
	 * Handles integers (decimal, hex, binary, octal), floats, scientific notation, and byte literals
	 *
	 * @param src Source string
	 * @param pos Starting position (must point to a digit)
	 * @param len Length of source string
	 * @param line Current line number (for error messages)
	 * @param column Current column number (for error messages)
	 * @return ParsedNumber containing type, value, and chars consumed
	 */
	static ParsedNumber parse(const char *src, size_t pos, size_t len,
							  int line, int column) {
		ParsedNumber result;
		size_t start = pos;

		// Check for base prefix (0x, 0b, 0o)
		if (pos + 1 < len && src[pos] == '0') {
			char prefix = src[pos + 1];
			if (prefix == 'x') {
				// Hexadecimal
				pos += 2; // Skip "0x"
				uint64_t value = 0;
				size_t digits = 0;
				pos = parse_hex_integer(src, pos, len, value, digits, line, column);
				if (digits == 0) {
					reportError("Hexadecimal literal requires at least one digit after '0x'", line, column);
				}
				result.type = ParsedNumber::Type::Integer;
				result.int_value = static_cast<int64_t>(value);
				result.chars_consumed = pos - start;
				result.literal_string = std::string(src + start, result.chars_consumed);
				return result;
			} else if (prefix == 'b') {
				// Binary - but check it's not a byte suffix (0b alone)
				// If next char after 'b' is a binary digit, it's a binary literal
				if (pos + 2 < len && CharClassifier::is_binary_digit(src[pos + 2])) {
					pos += 2; // Skip "0b"
					uint64_t value = 0;
					size_t digits = 0;
					pos = parse_binary_integer(src, pos, len, value, digits, line, column);
					result.type = ParsedNumber::Type::Integer;
					result.int_value = static_cast<int64_t>(value);
					result.chars_consumed = pos - start;
					result.literal_string = std::string(src + start, result.chars_consumed);
					return result;
				}
				// Otherwise fall through to parse '0' as decimal integer
			} else if (prefix == 'o') {
				// Octal
				pos += 2; // Skip "0o"
				uint64_t value = 0;
				size_t digits = 0;
				pos = parse_octal_integer(src, pos, len, value, digits, line, column);
				if (digits == 0) {
					reportError("Octal literal requires at least one digit after '0o'", line, column);
				}
				result.type = ParsedNumber::Type::Integer;
				result.int_value = static_cast<int64_t>(value);
				result.chars_consumed = pos - start;
				result.literal_string = std::string(src + start, result.chars_consumed);
				return result;
			}
		}

		// Parse decimal integer part (with underscore support)
		uint64_t int_part = 0;
		size_t int_digits = 0;
		pos = parse_decimal_integer(src, pos, len, int_part, int_digits);

		// Check for decimal point
		bool is_float = false;
		double frac_part = 0.0;

		if (pos < len && src[pos] == '.') {
			// Peek ahead to verify next char is a digit (not .. range operator)
			if (pos + 1 < len && CharClassifier::is_digit(src[pos + 1])) {
				is_float = true;
				pos++; // Consume '.'

				// Parse fractional part - must consume ALL digits even if they overflow
				// because we need to skip them all in the source
				double divisor = 1.0;
				frac_part = 0.0;

				// Parse up to ~15 significant digits for double precision
				// but continue skipping all remaining digits
				while (pos < len && CharClassifier::is_digit(src[pos])) {
					if (divisor < 1e16) { // Only accumulate ~15 digits of precision
						frac_part = frac_part * 10.0 + static_cast<double>(src[pos] - '0');
						divisor *= 10.0;
					}
					pos++;
				}

				// Convert accumulated digits to fraction
				if (divisor > 1.0) {
					frac_part /= divisor;
				}
			}
		}

		// Check for scientific notation (e or E)
		int exponent = 0;
		if (pos < len && (src[pos] == 'e' || src[pos] == 'E')) {
			is_float = true;
			pos++; // Consume 'e' or 'E'

			// Optional sign
			bool neg_exp = false;
			if (pos < len && (src[pos] == '+' || src[pos] == '-')) {
				neg_exp = (src[pos] == '-');
				pos++;
			}

			// Parse exponent
			if (pos >= len || !CharClassifier::is_digit(src[pos])) {
				reportError("Invalid scientific notation", line, column);
			}

			uint64_t exp_value = 0;
			size_t exp_digits = 0;
			pos = parse_integer_simd(src, pos, len, exp_value, exp_digits);

			exponent = neg_exp ? -static_cast<int>(exp_value) : static_cast<int>(exp_value);
		}

		// Build result
		result.chars_consumed = pos - start;
		result.literal_string = std::string(src + start, result.chars_consumed);

		if (is_float) {
			result.type = ParsedNumber::Type::Float;
			double value = static_cast<double>(int_part) + frac_part;
			if (exponent != 0) {
				value *= pow(10.0, static_cast<double>(exponent));
			}
			result.float_value = value;
		} else {
			result.type = ParsedNumber::Type::Integer;
			result.int_value = static_cast<int64_t>(int_part);
		}

		return result;
	}

	/**
	 * Parse just an integer (no byte/float handling)
	 * Returns position after last digit
	 */
	static size_t parse_integer_simd(const char *src, size_t pos, size_t len,
									 uint64_t &value, size_t &digit_count) {
		value = 0;
		size_t start = pos;

		// Fast path for short numbers (most common case)
		// Use scalar parsing for numbers up to 16 digits
		while (pos < len && CharClassifier::is_digit(src[pos])) {
			// Check for overflow (max uint64 is ~19 digits)
			if (value > 1844674407370955161ULL) { // UINT64_MAX / 10
				// Could overflow, proceed carefully
				uint64_t digit = static_cast<uint64_t>(src[pos] - '0');
				if (value > (UINT64_MAX - digit) / 10) {
					// Would overflow, stop here
					break;
				}
			}
			value = value * 10 + static_cast<uint64_t>(src[pos] - '0');
			pos++;
		}

		digit_count = pos - start;
		return pos;
	}

	/**
	 * Parse a decimal integer with underscore separator support
	 */
	static size_t parse_decimal_integer(const char *src, size_t pos, size_t len,
										uint64_t &value, size_t &digit_count) {
		value = 0;
		digit_count = 0;

		while (pos < len) {
			char c = src[pos];
			if (CharClassifier::is_digit(c)) {
				// Check for overflow
				if (value > 1844674407370955161ULL) {
					uint64_t digit = static_cast<uint64_t>(c - '0');
					if (value > (UINT64_MAX - digit) / 10) {
						break;
					}
				}
				value = value * 10 + static_cast<uint64_t>(c - '0');
				digit_count++;
				pos++;
			} else if (c == '_') {
				// Skip underscore separators (but don't count as digit)
				pos++;
			} else {
				break;
			}
		}

		return pos;
	}

	/**
	 * Parse a hexadecimal integer (after 0x prefix)
	 */
	static size_t parse_hex_integer(const char *src, size_t pos, size_t len,
									uint64_t &value, size_t &digit_count,
									int line, int column) {
		value = 0;
		digit_count = 0;

		while (pos < len) {
			char c = src[pos];
			if (CharClassifier::is_hex_digit(c)) {
				// Check for overflow (max uint64 fits in 16 hex digits)
				if (value > 0x0FFFFFFFFFFFFFFFULL) {
					reportError("Hexadecimal literal overflow", line, column);
				}
				value = value * 16 + static_cast<uint64_t>(CharClassifier::hex_digit_value(c));
				digit_count++;
				pos++;
			} else if (c == '_') {
				pos++;
			} else {
				break;
			}
		}

		return pos;
	}

	/**
	 * Parse a binary integer (after 0b prefix)
	 */
	static size_t parse_binary_integer(const char *src, size_t pos, size_t len,
									   uint64_t &value, size_t &digit_count,
									   int line, int column) {
		value = 0;
		digit_count = 0;

		while (pos < len) {
			char c = src[pos];
			if (CharClassifier::is_binary_digit(c)) {
				// Check for overflow (max uint64 fits in 64 binary digits)
				if (value > 0x7FFFFFFFFFFFFFFFULL) {
					reportError("Binary literal overflow", line, column);
				}
				value = value * 2 + static_cast<uint64_t>(c - '0');
				digit_count++;
				pos++;
			} else if (c == '_') {
				pos++;
			} else {
				break;
			}
		}

		return pos;
	}

	/**
	 * Parse an octal integer (after 0o prefix)
	 */
	static size_t parse_octal_integer(const char *src, size_t pos, size_t len,
									  uint64_t &value, size_t &digit_count,
									  int line, int column) {
		value = 0;
		digit_count = 0;

		while (pos < len) {
			char c = src[pos];
			if (CharClassifier::is_octal_digit(c)) {
				// Check for overflow
				if (value > 0x1FFFFFFFFFFFFFFFULL) {
					reportError("Octal literal overflow", line, column);
				}
				value = value * 8 + static_cast<uint64_t>(c - '0');
				digit_count++;
				pos++;
			} else if (c == '_') {
				pos++;
			} else {
				break;
			}
		}

		return pos;
	}

	/**
	 * SIMD-accelerated digit scanning
	 * Finds the extent of digit characters starting at position
	 */
	static size_t find_digit_extent(const char *src, size_t pos, size_t len) {
		size_t start = pos;

		// Use AVX2 if available
		if (has_avx2()) {
			while (pos + 32 <= len) {
				simd256_t chars = simd_loadu_256(&src[pos]);
				uint32_t mask = CharClassifier::classify_digits_avx2(chars);

				if (mask != 0xFFFFFFFF) {
					// Found non-digit
					return pos - start + count_trailing_zeros(~mask);
				}

				pos += 32;
			}
		}

		// Use SSE
		while (pos + 16 <= len) {
			simd128_t chars = simd_loadu_128(&src[pos]);
			uint16_t mask = CharClassifier::classify_digits_sse(chars);

			if (mask != 0xFFFF) {
				return pos - start + count_trailing_zeros(static_cast<uint32_t>(~mask & 0xFFFF));
			}

			pos += 16;
		}

		// Handle remaining bytes
		while (pos < len && CharClassifier::is_digit(src[pos])) {
			pos++;
		}

		return pos - start;
	}

	/**
	 * Check if a character starts a number
	 */
	static bool is_number_start(char c) {
		return CharClassifier::is_digit(c);
	}

	/**
	 * Validate a parsed integer is in byte range
	 */
	static bool is_valid_byte(int64_t value) {
		return value >= 0 && value <= 255;
	}
};

/**
 * SIMD-accelerated integer to string conversion
 * Useful for generating string representations efficiently
 */
class IntToString {
  public:
	/**
	 * Convert integer to string
	 * Returns number of characters written
	 */
	static size_t convert(int64_t value, char *buffer, size_t buffer_size) {
		if (buffer_size < 21) { // Max int64 is 19 digits + sign + null
			return 0;
		}

		char *ptr = buffer;
		bool negative = value < 0;

		if (negative) {
			*ptr++ = '-';
			// Handle INT64_MIN specially to avoid overflow
			if (value == INT64_MIN) {
				const char *min_str = "9223372036854775808";
				size_t min_len = 19;
				memcpy(ptr, min_str, min_len);
				ptr[min_len] = '\0';
				return min_len + 1;
			}
			value = -value;
		}

		// Convert to string (backwards)
		char temp[20];
		char *temp_ptr = temp;

		do {
			*temp_ptr++ = '0' + static_cast<char>(value % 10);
			value /= 10;
		} while (value > 0);

		// Reverse into buffer
		while (temp_ptr > temp) {
			*ptr++ = *--temp_ptr;
		}

		*ptr = '\0';
		return static_cast<size_t>(ptr - buffer);
	}

	/**
	 * Get digit count for an integer
	 */
	static size_t digit_count(uint64_t value) {
		if (value == 0)
			return 1;

		size_t count = 0;
		while (value > 0) {
			value /= 10;
			count++;
		}
		return count;
	}
};

#endif // LEXER_NUMBER_PARSER_H
