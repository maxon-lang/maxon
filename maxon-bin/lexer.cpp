/**
 * Maxon Lexer Implementation
 *
 * SIMD-optimized lexer for fast tokenization of Maxon source code.
 */

#include "lexer.h"
#include "lexer/lexer_char_class.h"
#include "lexer/lexer_keyword_matcher.h"
#include "lexer/lexer_number_parser.h"
#include "lexer/lexer_platform.h"
#include "parser.h"
#include "token_stream.h"

#include <cctype>
#include <chrono>
#include <cstdio>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <unordered_map>

// ============================================================================
// Keyword Data
// ============================================================================

static const std::unordered_map<std::string, KeywordData> keywords = {
	// Types
	{"int", {KeywordCategory::Type, "Integer type"}},
	{"float", {KeywordCategory::Type, "Floating-point type"}},
	{"char", {KeywordCategory::Type, "Extended Grapheme Cluster (user-perceived character)"}},
	{"byte", {KeywordCategory::Type, "8-bit unsigned integer"}},
	{"bool", {KeywordCategory::Type, "Boolean type"}},
	{"string", {KeywordCategory::Type, "UTF-8 encoded string type"}},

	// Control flow
	{"if", {KeywordCategory::ControlFlow, "Conditional statement"}},
	{"then", {KeywordCategory::ControlFlow, "Single-line if body"}},
	{"else", {KeywordCategory::ControlFlow, "Alternative branch"}},
	{"while", {KeywordCategory::ControlFlow, "Loop statement"}},
	{"for", {KeywordCategory::ControlFlow, "For loop statement"}},
	{"in", {KeywordCategory::ControlFlow, "Iterator in for loop"}},
	{"end", {KeywordCategory::ControlFlow, "Block terminator"}},
	{"return", {KeywordCategory::ControlFlow, "Return from function"}},
	{"break", {KeywordCategory::ControlFlow, "Exit loop"}},
	{"continue", {KeywordCategory::ControlFlow, "Skip to next iteration"}},

	// Declarations
	{"function", {KeywordCategory::Declaration, "Function declaration"}},
	{"var", {KeywordCategory::Declaration, "Mutable variable"}},
	{"let", {KeywordCategory::Declaration, "Immutable variable"}},
	{"struct", {KeywordCategory::Declaration, "Structure type"}},
	{"interface", {KeywordCategory::Declaration, "Interface declaration"}},
	{"export", {KeywordCategory::Declaration, "Export declaration"}},
	{"extern", {KeywordCategory::Declaration, "External declaration"}},

	// Math intrinsics (built into codegen)
	{"sqrt", {KeywordCategory::MathIntrinsic, "Square root", {{MathIntrinsicKind::Intrinsic, "", "float"}}}},
	{"abs", {KeywordCategory::MathIntrinsic, "Absolute value", {{MathIntrinsicKind::Intrinsic, "", "float"}}}},
	{"floor", {KeywordCategory::MathIntrinsic, "Floor function", {{MathIntrinsicKind::Intrinsic, "", "int"}}}},
	{"ceil", {KeywordCategory::MathIntrinsic, "Ceiling function", {{MathIntrinsicKind::Intrinsic, "", "int"}}}},
	{"round", {KeywordCategory::MathIntrinsic, "Round to nearest", {{MathIntrinsicKind::Intrinsic, "", "int"}}}},
	{"trunc", {KeywordCategory::MathIntrinsic, "Truncate to integer", {{MathIntrinsicKind::DirectCast, "", "int"}}}},
	{"sin", {KeywordCategory::MathIntrinsic, "Sine function", {{MathIntrinsicKind::RuntimeFunction, "sin", "float"}}}},
	{"cos", {KeywordCategory::MathIntrinsic, "Cosine function", {{MathIntrinsicKind::RuntimeFunction, "cos", "float"}}}},
	{"tan", {KeywordCategory::MathIntrinsic, "Tangent function", {{MathIntrinsicKind::RuntimeFunction, "tan", "float"}}}},

	// Literals
	{"true", {KeywordCategory::Literal, "Boolean true"}},
	{"false", {KeywordCategory::Literal, "Boolean false"}},

	// Operators
	{"as", {KeywordCategory::Operator, "Type cast operator"}},
	{"and", {KeywordCategory::Operator, "Logical and"}},
	{"or", {KeywordCategory::Operator, "Logical or"}},
	{"not", {KeywordCategory::Operator, "Logical not"}},
	{"mod", {KeywordCategory::Operator, "Modulo operator"}},
	{"is", {KeywordCategory::Operator, "Interface conformance"}}};

// ============================================================================
// Static Initialization
// ============================================================================

namespace {
struct KeywordMatcherInit {
	KeywordMatcherInit() {
		KeywordMatcher::initialize();
	}
};
static KeywordMatcherInit keyword_matcher_init;
} // namespace

// ============================================================================
// Lexer Constructor
// ============================================================================

Lexer::Lexer(const std::string &source)
	: source_(source.data()), length_(source.size()), position_(0), line_(1), column_(1) {
}

Lexer::Lexer(const char *source, size_t length)
	: source_(source), length_(length), position_(0), line_(1), column_(1) {
}

// ============================================================================
// Tokenization
// ============================================================================

std::vector<Token> Lexer::tokenize() {
	std::vector<Token> tokens;
	tokens.reserve(length_ / 4); // Rough estimate: 1 token per 4 chars

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

TokenStream Lexer::tokenize_stream() {
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

// ============================================================================
// Private Helper Methods
// ============================================================================

char Lexer::currentChar() const {
	return position_ < length_ ? source_[position_] : '\0';
}

char Lexer::peek(int offset) const {
	size_t pos = position_ + offset;
	return pos < length_ ? source_[pos] : '\0';
}

void Lexer::advance() {
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

void Lexer::advanceBy(size_t n) {
	for (size_t i = 0; i < n && position_ < length_; ++i) {
		advance();
	}
}

void Lexer::skipWhitespace() {
	size_t new_pos = CharClassifier::skip_whitespace(source_, position_, length_);
	if (new_pos > position_) {
		updateLineColumn(position_, new_pos);
		position_ = new_pos;
	}
}

void Lexer::updateLineColumn(size_t start, size_t end) {
	for (size_t i = start; i < end; ++i) {
		if (source_[i] == '\n') {
			line_++;
			column_ = 1;
		} else {
			column_++;
		}
	}
}

void Lexer::skipLineComment() {
	advance();
	advance();
	size_t newline_pos = CharClassifier::find_newline(source_, position_, length_);
	column_ += static_cast<int>(newline_pos - position_);
	position_ = newline_pos;
	if (position_ < length_ && source_[position_] == '\n') {
		advance();
	}
}

void Lexer::skipBlockComment() {
	int startLine = line_;
	int startColumn = column_;

	advance();
	advance();

	while (position_ < length_) {
		size_t star_pos = CharClassifier::find_char(source_, position_, length_, '*');
		updateLineColumn(position_, star_pos);
		position_ = star_pos;

		if (position_ >= length_) {
			throw std::runtime_error("Unterminated block comment starting at line " +
									 std::to_string(startLine) + ", column " + std::to_string(startColumn));
		}

		if (source_[position_] == '*' && position_ + 1 < length_ && source_[position_ + 1] == '/') {
			advance();
			advance();
			return;
		}

		advance();
	}

	throw std::runtime_error("Unterminated block comment starting at line " +
							 std::to_string(startLine) + ", column " + std::to_string(startColumn));
}

Token Lexer::readNextToken() {
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

Token Lexer::readNumber() {
	int startLine = line_;
	int startColumn = column_;

	ParsedNumber num = NumberParser::parse(source_, position_, length_, startLine, startColumn);
	advanceBy(num.chars_consumed);

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

Token Lexer::readIdentifier() {
	int startLine = line_;
	int startColumn = column_;
	size_t start = position_;

	size_t ident_len = CharClassifier::find_identifier_length(source_, position_, length_);
	advanceBy(ident_len);

	KeywordEntry entry;
	if (KeywordMatcher::match(source_ + start, ident_len, entry)) {
		Token tok(TokenType::KEYWORD, std::string(source_ + start, ident_len), startLine, startColumn);

		KeywordData kd;
		kd.category = entry.category;
		kd.description = "";

		if (entry.has_math_info) {
			kd.mathInfo = entry.math_info;
		}

		tok.keywordData = kd;
		return tok;
	}

	return Token(TokenType::IDENTIFIER, std::string(source_ + start, ident_len), startLine, startColumn);
}

Token Lexer::readString() {
	int startLine = line_;
	int startColumn = column_;
	std::string str;

	advance();

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

	advance();

	if (str.length() == 1) {
		return Token(TokenType::CHARACTER, str, startLine, startColumn);
	}

	return Token(TokenType::BLOCK_ID, str, startLine, startColumn);
}

Token Lexer::readStringLiteral() {
	int startLine = line_;
	int startColumn = column_;
	std::string str;

	advance();

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

	advance();

	return Token(TokenType::STRING, str, startLine, startColumn);
}

Token Lexer::readOperatorOrDelimiter(int startLine, int startColumn) {
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

	case '&':
		advance();
		return Token(TokenType::AMPERSAND, "&", startLine, startColumn);

	case '|':
		advance();
		return Token(TokenType::PIPE, "|", startLine, startColumn);

	case '^':
		advance();
		return Token(TokenType::CARET, "^", startLine, startColumn);

	case '>':
		advance();
		if (currentChar() == '>') {
			advance();
			return Token(TokenType::RSHIFT, ">>", startLine, startColumn);
		}
		if (currentChar() == '=') {
			advance();
			return Token(TokenType::GTE, ">=", startLine, startColumn);
		}
		return Token(TokenType::GT, ">", startLine, startColumn);

	case '<':
		advance();
		if (currentChar() == '<') {
			advance();
			return Token(TokenType::LSHIFT, "<<", startLine, startColumn);
		}
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

// ============================================================================
// Static Keyword Query Methods
// ============================================================================

std::vector<std::string> Lexer::getKeywords() {
	std::vector<std::string> keywordList;
	keywordList.reserve(keywords.size());
	for (const auto &pair : keywords) {
		keywordList.push_back(pair.first);
	}
	return keywordList;
}

std::vector<Lexer::KeywordInfo> Lexer::getKeywordInfo() {
	std::vector<KeywordInfo> info;
	for (const auto &pair : keywords) {
		const KeywordData &data = pair.second;
		info.push_back({pair.first, data.category, data.description});
	}
	return info;
}

std::vector<std::string> Lexer::getKeywordsByCategory(KeywordCategory category) {
	std::vector<std::string> result;
	for (const auto &pair : keywords) {
		if (pair.second.category == category) {
			result.push_back(pair.first);
		}
	}
	return result;
}

bool Lexer::isMathIntrinsic(const std::string &name) {
	auto it = keywords.find(name);
	return it != keywords.end() && it->second.category == KeywordCategory::MathIntrinsic;
}

const MathIntrinsicInfo *Lexer::getMathIntrinsicInfo(const std::string &name) {
	auto it = keywords.find(name);
	if (it != keywords.end() && it->second.category == KeywordCategory::MathIntrinsic) {
		return it->second.mathInfo.has_value() ? &it->second.mathInfo.value() : nullptr;
	}
	return nullptr;
}

bool Lexer::isTypeString(const std::string &name) {
	auto it = keywords.find(name);
	return it != keywords.end() && it->second.category == KeywordCategory::Type;
}

bool Lexer::isControlFlowToken(const Token &token) {
	return token.type == TokenType::KEYWORD && token.keywordData.has_value() &&
		   token.keywordData.value().category == KeywordCategory::ControlFlow;
}

bool Lexer::isDeclarationToken(const Token &token) {
	return token.type == TokenType::KEYWORD && token.keywordData.has_value() &&
		   token.keywordData.value().category == KeywordCategory::Declaration;
}

bool Lexer::isTypeToken(const Token &token) {
	return token.type == TokenType::KEYWORD && token.keywordData.has_value() &&
		   token.keywordData.value().category == KeywordCategory::Type;
}

bool Lexer::isLiteralToken(const Token &token) {
	return token.type == TokenType::KEYWORD && token.keywordData.has_value() &&
		   token.keywordData.value().category == KeywordCategory::Literal;
}

bool Lexer::isMathIntrinsicToken(const Token &token) {
	return token.type == TokenType::KEYWORD && token.keywordData.has_value() &&
		   token.keywordData.value().category == KeywordCategory::MathIntrinsic;
}

// ============================================================================
// Utility Functions
// ============================================================================

static CPUFeature cached_features = CPUFeature::None;
static bool features_detected = false;

static CPUFeature get_cached_cpu_features() {
	if (!features_detected) {
		cached_features = detect_cpu_features();
		features_detected = true;
	}
	return cached_features;
}

const char *get_lexer_capability() {
	if (has_avx2()) {
		return "AVX2 (256-bit)";
	} else if (has_sse42()) {
		return "SSE4.2 (128-bit)";
	} else {
		return "Scalar (no SIMD)";
	}
}

void print_cpu_features() {
	CPUFeature features = get_cached_cpu_features();

	printf("CPU Features:\n");
	printf("  SSE2:     %s\n", has_feature(features, CPUFeature::SSE2) ? "YES" : "no");
	printf("  SSE3:     %s\n", has_feature(features, CPUFeature::SSE3) ? "YES" : "no");
	printf("  SSSE3:    %s\n", has_feature(features, CPUFeature::SSSE3) ? "YES" : "no");
	printf("  SSE4.1:   %s\n", has_feature(features, CPUFeature::SSE41) ? "YES" : "no");
	printf("  SSE4.2:   %s\n", has_feature(features, CPUFeature::SSE42) ? "YES" : "no");
	printf("  AVX:      %s\n", has_feature(features, CPUFeature::AVX) ? "YES" : "no");
	printf("  AVX2:     %s\n", has_feature(features, CPUFeature::AVX2) ? "YES" : "no");
	printf("  AVX-512F: %s\n", has_feature(features, CPUFeature::AVX512F) ? "YES" : "no");
	printf("  POPCNT:   %s\n", has_feature(features, CPUFeature::POPCNT) ? "YES" : "no");
	printf("  BMI1:     %s\n", has_feature(features, CPUFeature::BMI1) ? "YES" : "no");
	printf("  BMI2:     %s\n", has_feature(features, CPUFeature::BMI2) ? "YES" : "no");
}

struct BenchmarkResult {
	double total_ms;
	size_t token_count;
	size_t source_bytes;
	double tokens_per_ms;
	double mb_per_sec;
	double avg_time_per_iteration_us;
};

static BenchmarkResult benchmark_lexer(const std::string &source, int iterations) {
	BenchmarkResult result = {};
	result.source_bytes = source.size();

	// Warm up
	{
		Lexer lexer(source);
		auto tokens = lexer.tokenize();
		result.token_count = tokens.size();
	}

	// Benchmark
	auto start = std::chrono::high_resolution_clock::now();

	for (int i = 0; i < iterations; ++i) {
		Lexer lexer(source);
		auto tokens = lexer.tokenize();
		(void)tokens;
	}

	auto end = std::chrono::high_resolution_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);

	result.total_ms = duration.count() / 1000.0;
	result.tokens_per_ms = (result.token_count * iterations) / result.total_ms;
	result.mb_per_sec = (result.source_bytes * iterations) / (result.total_ms * 1000.0);
	result.avg_time_per_iteration_us = static_cast<double>(duration.count()) / iterations;

	return result;
}

void run_lexer_benchmark(const std::string &source, const std::string &filename, int iterations) {
	std::cout << "\n";
	std::cout << "========================================\n";
	std::cout << "Lexer Benchmark: " << filename << "\n";
	std::cout << "========================================\n";
	std::cout << "Source size: " << source.size() << " bytes\n";
	std::cout << "Iterations:  " << iterations << "\n";
	std::cout << "\n";

	print_cpu_features();
	std::cout << "\nSIMD Mode: " << get_lexer_capability() << "\n\n";

	std::cout << "Running lexer benchmark...\n";
	BenchmarkResult result = benchmark_lexer(source, iterations);

	std::cout << "\n";
	std::cout << "Results:\n";
	std::cout << "----------------------------------------\n";
	std::cout << std::fixed << std::setprecision(2);

	std::cout << "Total time (ms):    " << std::setw(8) << result.total_ms << "\n";
	std::cout << "Avg time (µs):      " << std::setw(8) << result.avg_time_per_iteration_us << "\n";
	std::cout << "Tokens/ms:          " << std::setw(8) << result.tokens_per_ms << "\n";
	std::cout << "MB/sec:             " << std::setw(8) << result.mb_per_sec << "\n";
	std::cout << "----------------------------------------\n";
	std::cout << "Token count:        " << result.token_count << "\n";
	std::cout << "========================================\n";
}

void run_pipeline_benchmark(const std::string &source, const std::string &filename, int iterations) {
	std::cout << "\n";
	std::cout << "========================================\n";
	std::cout << "Pipeline Benchmark: " << filename << "\n";
	std::cout << "========================================\n";
	std::cout << "Source size: " << source.size() << " bytes\n";
	std::cout << "Iterations:  " << iterations << "\n";
	std::cout << "\n";

	// Run lexer + parser pipeline
	auto start = std::chrono::high_resolution_clock::now();

	for (int i = 0; i < iterations; ++i) {
		Lexer lexer(source);
		auto stream = lexer.tokenize_stream();
		Parser parser(std::move(stream));
		auto program = parser.parse();
		(void)program;
	}

	auto end = std::chrono::high_resolution_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);

	double total_ms = duration.count() / 1000.0;
	double avg_time_us = static_cast<double>(duration.count()) / iterations;
	double mb_per_sec = (source.size() * iterations) / (total_ms * 1000.0);

	std::cout << "\n";
	std::cout << "Results (Lexer + Parser):\n";
	std::cout << "----------------------------------------\n";
	std::cout << std::fixed << std::setprecision(2);

	std::cout << "Total time (ms):    " << std::setw(8) << total_ms << "\n";
	std::cout << "Avg time (µs):      " << std::setw(8) << avg_time_us << "\n";
	std::cout << "MB/sec:             " << std::setw(8) << mb_per_sec << "\n";
	std::cout << "----------------------------------------\n";
	std::cout << "========================================\n";
}
