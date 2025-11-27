#ifndef SIMD_LEXER_H
#define SIMD_LEXER_H

/**
 * SIMD-Optimized Lexer
 *
 * Drop-in replacement for the original Lexer class with SIMD optimizations.
 * Uses vectorized character classification, keyword matching, and number parsing
 * for significant performance improvements.
 *
 * Performance Targets (vs original lexer):
 * - Character classification: 80x faster (SIMD batch processing)
 * - Keyword recognition: 10-25x faster (perfect hashing)
 * - Whitespace skipping: 16x faster (SIMD scanning)
 * - Overall: 5-8x faster end-to-end
 *
 * Memory Improvements:
 * - Token size: 100 bytes -> 16 bytes (6.25x reduction)
 * - String deduplication via interning
 */

#include "../lexer.h"
#include "simd_char_class.h"
#include "simd_keyword_matcher.h"
#include "simd_number_parser.h"
#include "simd_platform.h"
#include "simd_token_stream.h"
#include <stdexcept>
#include <string>
#include <vector>

namespace simd {

/**
 * SIMD-Optimized Lexer
 *
 * API-compatible with the original Lexer class for easy integration.
 */
class SIMDLexer {
  public:
	/**
	 * Construct lexer with source code
	 */
	explicit SIMDLexer(const std::string &source)
		: source_(source.data()), length_(source.size()), position_(0), line_(1), column_(1) {
		// Initialize keyword matcher on first use
		KeywordMatcher::initialize();
	}

	/**
	 * Construct lexer with source code (C string + length)
	 */
	SIMDLexer(const char *source, size_t length)
		: source_(source), length_(length), position_(0), line_(1), column_(1) {
		KeywordMatcher::initialize();
	}

	/**
	 * Tokenize the source code
	 * Returns a vector of tokens compatible with the existing parser
	 */
	std::vector<Token> tokenize() {
		std::vector<Token> tokens;
		tokens.reserve(length_ / 4); // Rough estimate: 1 token per 4 chars

		while (position_ < length_) {
			// Skip whitespace (SIMD-accelerated)
			skipWhitespace();

			if (position_ >= length_)
				break;

			// Check for comments
			if (position_ + 1 < length_ && source_[position_] == '/') {
				if (source_[position_ + 1] == '/') {
					skipLineComment();
					continue;
				}
				if (source_[position_ + 1] == '*') {
					skipBlockComment();
					continue;
				}
			}

			if (position_ >= length_)
				break;

			int startLine = line_;
			int startColumn = column_;
			char c = source_[position_];

			// Single quote - block identifier or character
			if (c == '\'') {
				tokens.push_back(readString());
			}
			// Double quote - string literal
			else if (c == '"') {
				tokens.push_back(readStringLiteral());
			}
			// Numbers
			else if (CharClassifier::is_digit(c)) {
				tokens.push_back(readNumber());
			}
			// Identifiers and keywords
			else if (CharClassifier::is_ident_start(c)) {
				tokens.push_back(readIdentifier());
			}
			// Operators and delimiters
			else {
				Token tok = readOperatorOrDelimiter(startLine, startColumn);
				if (tok.type != TokenType::UNKNOWN) {
					tokens.push_back(std::move(tok));
				}
			}
		}

		tokens.push_back(Token(TokenType::END_OF_FILE, "", line_, column_));
		return tokens;
	}

	/**
	 * Tokenize to SIMD-optimized token stream
	 * More efficient than vector<Token> for SIMD parser
	 */
	TokenStream tokenize_stream() {
		TokenStream stream;

		while (position_ < length_) {
			skipWhitespace();

			if (position_ >= length_)
				break;

			// Check for comments
			if (position_ + 1 < length_ && source_[position_] == '/') {
				if (source_[position_ + 1] == '/') {
					skipLineComment();
					continue;
				}
				if (source_[position_ + 1] == '*') {
					skipBlockComment();
					continue;
				}
			}

			if (position_ >= length_)
				break;

			Token tok = readNextToken();
			if (tok.type != TokenType::UNKNOWN) {
				stream.add_token(tok);
			}
		}

		stream.add_token(TokenType::END_OF_FILE, "", line_, column_);
		return stream;
	}

	// Static helper methods (same as original Lexer)
	static std::vector<std::string> getKeywords() {
		return Lexer::getKeywords();
	}

	static std::vector<Lexer::KeywordInfo> getKeywordInfo() {
		return Lexer::getKeywordInfo();
	}

	static bool isMathIntrinsic(const std::string &name) {
		return Lexer::isMathIntrinsic(name);
	}

	static const MathIntrinsicInfo *getMathIntrinsicInfo(const std::string &name) {
		return Lexer::getMathIntrinsicInfo(name);
	}

	static bool isTypeString(const std::string &name) {
		return Lexer::isTypeString(name);
	}

  private:
	const char *source_;
	size_t length_;
	size_t position_;
	int line_;
	int column_;

	/**
	 * Get current character (safe bounds check)
	 */
	char currentChar() const {
		return position_ < length_ ? source_[position_] : '\0';
	}

	/**
	 * Peek at character at offset
	 */
	char peek(int offset = 1) const {
		size_t pos = position_ + offset;
		return pos < length_ ? source_[pos] : '\0';
	}

	/**
	 * Advance position by one character
	 */
	void advance() {
		if (position_ < length_) {
			if (source_[position_] == '\n') {
				line_++;
				column_ = 1;
			} else {
				column_++;
			}
			position_++;
		}
	}

	/**
	 * Advance position by N characters, updating line/column
	 */
	void advanceBy(size_t n) {
		for (size_t i = 0; i < n && position_ < length_; ++i) {
			advance();
		}
	}

	/**
	 * Skip whitespace (SIMD-accelerated)
	 */
	void skipWhitespace() {
		size_t new_pos = CharClassifier::skip_whitespace(source_, position_, length_);

		// Update line/column tracking
		if (new_pos > position_) {
			updateLineColumn(position_, new_pos);
			position_ = new_pos;
		}
	}

	/**
	 * Update line and column after skipping a range
	 */
	void updateLineColumn(size_t start, size_t end) {
		for (size_t i = start; i < end; ++i) {
			if (source_[i] == '\n') {
				line_++;
				column_ = 1;
			} else {
				column_++;
			}
		}
	}

	/**
	 * Skip single-line comment (SIMD-accelerated)
	 */
	void skipLineComment() {
		// Skip //
		advance();
		advance();

		// Find end of line using SIMD
		size_t newline_pos = CharClassifier::find_newline(source_, position_, length_);

		// Update line/column
		column_ += static_cast<int>(newline_pos - position_);
		position_ = newline_pos;

		// Skip the newline itself
		if (position_ < length_ && source_[position_] == '\n') {
			advance();
		}
	}

	/**
	 * Skip block comment (SIMD-accelerated)
	 */
	void skipBlockComment() {
		int startLine = line_;
		int startColumn = column_;

		// Skip /*
		advance();
		advance();

		while (position_ < length_) {
			// Look for */ using SIMD
			size_t star_pos = CharClassifier::find_char(source_, position_, length_, '*');

			// Update line tracking for skipped content
			updateLineColumn(position_, star_pos);
			position_ = star_pos;

			if (position_ >= length_) {
				throw std::runtime_error("Unterminated block comment starting at line " +
										 std::to_string(startLine) + ", column " + std::to_string(startColumn));
			}

			// Check for */
			if (source_[position_] == '*' && position_ + 1 < length_ && source_[position_ + 1] == '/') {
				advance(); // skip *
				advance(); // skip /
				return;
			}

			advance(); // skip * and continue searching
		}

		throw std::runtime_error("Unterminated block comment starting at line " +
								 std::to_string(startLine) + ", column " + std::to_string(startColumn));
	}

	/**
	 * Read next token (generic)
	 */
	Token readNextToken() {
		int startLine = line_;
		int startColumn = column_;
		char c = currentChar();

		if (c == '\'')
			return readString();
		if (c == '"')
			return readStringLiteral();
		if (CharClassifier::is_digit(c))
			return readNumber();
		if (CharClassifier::is_ident_start(c))
			return readIdentifier();
		return readOperatorOrDelimiter(startLine, startColumn);
	}

	/**
	 * Read number (SIMD-accelerated)
	 */
	Token readNumber() {
		int startLine = line_;
		int startColumn = column_;

		// Use SIMD number parser
		ParsedNumber num = NumberParser::parse(source_, position_, length_, startLine, startColumn);

		// Advance position and update line/column
		advanceBy(num.chars_consumed);

		// Create token based on type
		switch (num.type) {
		case ParsedNumber::Type::Byte:
			return Token(TokenType::BYTE_LITERAL, std::to_string(num.int_value), startLine, startColumn);
		case ParsedNumber::Type::Float:
			return Token(TokenType::FLOAT_LITERAL, num.literal_string, startLine, startColumn);
		case ParsedNumber::Type::Integer:
		default:
			return Token(TokenType::NUMBER, std::to_string(num.int_value), startLine, startColumn);
		}
	}

	/**
	 * Read identifier or keyword (SIMD-accelerated)
	 */
	Token readIdentifier() {
		int startLine = line_;
		int startColumn = column_;
		size_t start = position_;

		// Find identifier length using SIMD
		size_t ident_len = CharClassifier::find_identifier_length(source_, position_, length_);

		// Update position
		advanceBy(ident_len);

		// Check if it's a keyword using perfect hash
		KeywordEntry entry;
		if (KeywordMatcher::match(source_ + start, ident_len, entry)) {
			Token tok(TokenType::KEYWORD, std::string(source_ + start, ident_len), startLine, startColumn);

			KeywordData kd;
			kd.category = entry.category;
			kd.description = ""; // Description not stored in fast path

			if (entry.has_math_info) {
				kd.mathInfo = entry.math_info;
			}

			tok.keywordData = kd;
			return tok;
		}

		// It's an identifier
		return Token(TokenType::IDENTIFIER, std::string(source_ + start, ident_len), startLine, startColumn);
	}

	/**
	 * Read single-quoted string or character
	 */
	Token readString() {
		int startLine = line_;
		int startColumn = column_;
		std::string str;

		advance(); // Skip opening '

		while (position_ < length_ && source_[position_] != '\'') {
			if (source_[position_] == '\n') {
				throw std::runtime_error("Unterminated string literal at line " +
										 std::to_string(startLine) + ", column " + std::to_string(startColumn) +
										 ": string started with ' but missing closing '");
			}
			str += source_[position_];
			advance();
		}

		if (position_ >= length_) {
			throw std::runtime_error("Unterminated string literal at line " +
									 std::to_string(startLine) + ", column " + std::to_string(startColumn) +
									 ": reached end of file without finding closing '");
		}

		advance(); // Skip closing '

		// Single character is CHARACTER token
		if (str.length() == 1) {
			return Token(TokenType::CHARACTER, str, startLine, startColumn);
		}

		// Multi-character is BLOCK_ID
		return Token(TokenType::BLOCK_ID, str, startLine, startColumn);
	}

	/**
	 * Read double-quoted string literal
	 */
	Token readStringLiteral() {
		int startLine = line_;
		int startColumn = column_;
		std::string str;

		advance(); // Skip opening "

		while (position_ < length_ && source_[position_] != '"') {
			if (source_[position_] == '\\') {
				advance();
				if (position_ >= length_) {
					throw std::runtime_error("Unterminated string literal at line " +
											 std::to_string(startLine) + ", column " + std::to_string(startColumn) +
											 ": reached end of file in escape sequence");
				}

				switch (source_[position_]) {
				case 'n':
					str += '\n';
					break;
				case 't':
					str += '\t';
					break;
				case 'r':
					str += '\r';
					break;
				case '\\':
					str += '\\';
					break;
				case '"':
					str += '"';
					break;
				case '0':
					str += '\0';
					break;
				default:
					throw std::runtime_error("Unknown escape sequence '\\" +
											 std::string(1, source_[position_]) + "' at line " +
											 std::to_string(line_) + ", column " + std::to_string(column_));
				}
				advance();
			} else {
				str += source_[position_];
				advance();
			}
		}

		if (position_ >= length_) {
			throw std::runtime_error("Unterminated string literal at line " +
									 std::to_string(startLine) + ", column " + std::to_string(startColumn) +
									 ": reached end of file without finding closing \"");
		}

		advance(); // Skip closing "

		return Token(TokenType::STRING, str, startLine, startColumn);
	}

	/**
	 * Read operator or delimiter
	 */
	Token readOperatorOrDelimiter(int startLine, int startColumn) {
		char c = currentChar();

		switch (c) {
		case '+':
			advance();
			return Token(TokenType::PLUS, "+", startLine, startColumn);

		case '-':
			advance();
			return Token(TokenType::MINUS, "-", startLine, startColumn);

		case '*':
			advance();
			return Token(TokenType::MULTIPLY, "*", startLine, startColumn);

		case '/':
			advance();
			return Token(TokenType::DIVIDE, "/", startLine, startColumn);

		case '=':
			advance();
			if (currentChar() == '=') {
				advance();
				return Token(TokenType::EQUAL_EQUAL, "==", startLine, startColumn);
			}
			return Token(TokenType::ASSIGN, "=", startLine, startColumn);

		case '!':
			advance();
			if (currentChar() == '=') {
				advance();
				return Token(TokenType::NOT_EQUAL, "!=", startLine, startColumn);
			}
			throw std::runtime_error("Unexpected character '!' at line " +
									 std::to_string(startLine) + ", column " + std::to_string(startColumn) +
									 ": did you mean '!=' (not equal)?");

		case '>':
			advance();
			if (currentChar() == '=') {
				advance();
				return Token(TokenType::GTE, ">=", startLine, startColumn);
			}
			return Token(TokenType::GT, ">", startLine, startColumn);

		case '<':
			advance();
			if (currentChar() == '=') {
				advance();
				return Token(TokenType::LTE, "<=", startLine, startColumn);
			}
			return Token(TokenType::LT, "<", startLine, startColumn);

		case '(':
			advance();
			return Token(TokenType::LPAREN, "(", startLine, startColumn);

		case ')':
			advance();
			return Token(TokenType::RPAREN, ")", startLine, startColumn);

		case '[':
			advance();
			return Token(TokenType::LBRACKET, "[", startLine, startColumn);

		case ']':
			advance();
			return Token(TokenType::RBRACKET, "]", startLine, startColumn);

		case '{':
			advance();
			return Token(TokenType::LBRACE, "{", startLine, startColumn);

		case '}':
			advance();
			return Token(TokenType::RBRACE, "}", startLine, startColumn);

		case ',':
			advance();
			return Token(TokenType::COMMA, ",", startLine, startColumn);

		case ':':
			advance();
			return Token(TokenType::COLON, ":", startLine, startColumn);

		case '.':
			if (peek(1) == '.') {
				advance();
				advance();
				return Token(TokenType::DOT_DOT, "..", startLine, startColumn);
			}
			if (CharClassifier::is_digit(peek(1))) {
				throw std::runtime_error("Invalid float literal at line " +
										 std::to_string(startLine) + ", column " + std::to_string(startColumn) +
										 ": float literals must have a leading zero (use 0." +
										 std::string(1, peek(1)) + " instead of ." + std::string(1, peek(1)) + ")");
			}
			advance();
			return Token(TokenType::DOT, ".", startLine, startColumn);

		default: {
			// Unknown character
			std::string charDesc;
			if (std::isprint(c)) {
				charDesc = "'" + std::string(1, c) + "'";
			} else {
				charDesc = "(ASCII " + std::to_string(static_cast<int>(c)) + ")";
			}

			std::string suggestion;
			if (c == ';') {
				suggestion = "\n  Note: Maxon doesn't use semicolons at the end of statements";
			}

			throw std::runtime_error("Unexpected character " + charDesc + " at line " +
									 std::to_string(startLine) + ", column " + std::to_string(startColumn) + suggestion);
		}
		}
	}
};

/**
 * Factory function to choose between SIMD and scalar lexer
 * based on CPU capabilities and source size
 */
inline std::vector<Token> tokenize(const std::string &source, bool force_simd = false) {
	// Use SIMD lexer if:
	// 1. force_simd is true, OR
	// 2. SSE4.2 is available AND source is large enough to benefit
	if (force_simd || (has_sse42() && source.size() > 1024)) {
		SIMDLexer lexer(source);
		return lexer.tokenize();
	}

	// Fall back to original lexer for small files or no SIMD support
	Lexer lexer(source);
	return lexer.tokenize();
}

/**
 * Tokenize to stream (always uses SIMD lexer)
 */
inline TokenStream tokenize_stream(const std::string &source) {
	SIMDLexer lexer(source);
	return lexer.tokenize_stream();
}

} // namespace simd

#endif // SIMD_LEXER_H
