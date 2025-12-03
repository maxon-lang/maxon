#include "../parser.h"
#include <stdexcept>

// ============================================================================
// SIMD-optimized token accessors
// ============================================================================

TokenType Parser::currentType() const {
	return cache_.peek_type(0);
}

std::string_view Parser::currentValue() const {
	if (position >= stream_.size()) {
		return "";
	}
	return stream_.get_value(position);
}

int Parser::currentLine() const {
	if (position >= stream_.size()) {
		return stream_.size() > 0 ? stream_[stream_.size() - 1].get_line() : 0;
	}
	return static_cast<int>(stream_[position].get_line());
}

int Parser::currentColumn() const {
	if (position >= stream_.size()) {
		return stream_.size() > 0 ? stream_[stream_.size() - 1].get_column() : 0;
	}
	return static_cast<int>(stream_[position].get_column());
}

std::optional<KeywordData> Parser::currentKeywordData() const {
	if (position >= stream_.size()) {
		return std::nullopt;
	}
	const auto &ct = stream_[position];
	if (!ct.has_keyword_data()) {
		return std::nullopt;
	}

	KeywordData kd;
	kd.category = ct.get_keyword_category();
	// Note: math info lookup would need to be implemented if needed
	return kd;
}

// Legacy compatibility - constructs Token on demand (used for error messages, etc.)
Token Parser::currentToken() {
	if (position >= stream_.size()) {
		// Return last token (should be EOF)
		if (stream_.size() > 0) {
			const auto &ct = stream_[stream_.size() - 1];
			return Token(ct.get_type(), std::string(stream_.get_value(stream_.size() - 1)),
						 static_cast<int>(ct.get_line()), static_cast<int>(ct.get_column()));
		}
		return Token(TokenType::END_OF_FILE, "", 0, 0);
	}
	const auto &ct = stream_[position];
	Token tok(ct.get_type(), std::string(stream_.get_value(position)),
			  static_cast<int>(ct.get_line()), static_cast<int>(ct.get_column()));

	// Restore keyword data if present
	if (ct.has_keyword_data()) {
		KeywordData kd;
		kd.category = ct.get_keyword_category();
		tok.keywordData = kd;
	}
	return tok;
}

Token Parser::peekToken(int offset) {
	size_t pos = position + offset;
	if (pos >= stream_.size()) {
		// Return last token (should be EOF)
		if (stream_.size() > 0) {
			const auto &ct = stream_[stream_.size() - 1];
			return Token(ct.get_type(), std::string(stream_.get_value(stream_.size() - 1)),
						 static_cast<int>(ct.get_line()), static_cast<int>(ct.get_column()));
		}
		return Token(TokenType::END_OF_FILE, "", 0, 0);
	}
	const auto &ct = stream_[pos];
	Token tok(ct.get_type(), std::string(stream_.get_value(pos)),
			  static_cast<int>(ct.get_line()), static_cast<int>(ct.get_column()));

	if (ct.has_keyword_data()) {
		KeywordData kd;
		kd.category = ct.get_keyword_category();
		tok.keywordData = kd;
	}
	return tok;
}

// ============================================================================
// Token matching and navigation (using SIMD LookaheadCache)
// ============================================================================

bool Parser::match(TokenType type) {
	if (check(type)) {
		advance();
		return true;
	}
	return false;
}

bool Parser::check(TokenType type) const {
	return cache_.check(type, 0);
}

bool Parser::check(TokenType type, int offset) const {
	return cache_.check(type, static_cast<size_t>(offset));
}

bool Parser::checkKeyword(const std::string &keyword) const {
	if (!check(TokenType::KEYWORD)) {
		return false;
	}
	return currentValue() == keyword;
}

bool Parser::checkKeyword(const std::string &keyword, int offset) const {
	if (!check(TokenType::KEYWORD, offset)) {
		return false;
	}
	size_t pos = position + offset;
	if (pos >= stream_.size()) {
		return false;
	}
	return stream_.get_value(pos) == keyword;
}

void Parser::advance() {
	if (position < stream_.size()) {
		position++;
		cache_.set_position(position);
	}
}

// ============================================================================
// Expect methods (with error handling)
// ============================================================================

Token Parser::expect(TokenType type, const std::string &message) {
	if (!check(type)) {
		std::string typeStr;
		switch (type) {
		case TokenType::IDENTIFIER:
			typeStr = "identifier";
			break;
		case TokenType::NUMBER:
			typeStr = "number";
			break;
		case TokenType::STRING:
			typeStr = "string literal";
			break;
		case TokenType::BLOCK_ID:
			typeStr = "block identifier";
			break;
		case TokenType::CHARACTER:
			typeStr = "character literal";
			break;
		case TokenType::LPAREN:
			typeStr = "'('";
			break;
		case TokenType::RPAREN:
			typeStr = "')'";
			break;
		case TokenType::LBRACKET:
			typeStr = "'['";
			break;
		case TokenType::RBRACKET:
			typeStr = "']'";
			break;
		case TokenType::ASSIGN:
			typeStr = "'='";
			break;
		case TokenType::EQUAL_EQUAL:
			typeStr = "'=='";
			break;
		case TokenType::COMMA:
			typeStr = "','";
			break;
		case TokenType::KEYWORD:
			typeStr = "'" + std::string(currentValue()) + "'";
			break;
		default:
			typeStr = "token";
		}

		std::string foundStr;
		if (currentType() == TokenType::END_OF_FILE) {
			foundStr = "end of file";
		} else {
			foundStr = "'" + std::string(currentValue()) + "'";
		}

		reportError(message + "\n  Expected: " + typeStr + "\n  Found: " + foundStr,
					currentLine(), currentColumn());
	}
	Token tok = currentToken();
	advance();
	return tok;
}

Token Parser::expectKeyword(const std::string &keyword, const std::string &message) {
	if (!checkKeyword(keyword)) {
		reportError(message, currentLine(), currentColumn());
	}
	Token tok = currentToken();
	advance();
	return tok;
}

// ============================================================================
// Zero-allocation expect variants (use when Token result is discarded)
// ============================================================================

void Parser::expectAdvance(TokenType type, const std::string &message) {
	if (!check(type)) {
		std::string typeStr;
		switch (type) {
		case TokenType::IDENTIFIER:
			typeStr = "identifier";
			break;
		case TokenType::NUMBER:
			typeStr = "number";
			break;
		case TokenType::STRING:
			typeStr = "string literal";
			break;
		case TokenType::BLOCK_ID:
			typeStr = "block identifier";
			break;
		case TokenType::CHARACTER:
			typeStr = "character literal";
			break;
		case TokenType::LPAREN:
			typeStr = "'('";
			break;
		case TokenType::RPAREN:
			typeStr = "')'";
			break;
		case TokenType::LBRACKET:
			typeStr = "'['";
			break;
		case TokenType::RBRACKET:
			typeStr = "']'";
			break;
		case TokenType::ASSIGN:
			typeStr = "'='";
			break;
		case TokenType::EQUAL_EQUAL:
			typeStr = "'=='";
			break;
		case TokenType::COMMA:
			typeStr = "','";
			break;
		case TokenType::KEYWORD:
			typeStr = "'" + std::string(currentValue()) + "'";
			break;
		default:
			typeStr = "token";
		}

		std::string foundStr;
		if (currentType() == TokenType::END_OF_FILE) {
			foundStr = "end of file";
		} else {
			foundStr = "'" + std::string(currentValue()) + "'";
		}

		reportError(message + "\n  Expected: " + typeStr + "\n  Found: " + foundStr,
					currentLine(), currentColumn());
	}
	advance();
}

void Parser::expectKeywordAdvance(const std::string &keyword, const std::string &message) {
	if (!checkKeyword(keyword)) {
		reportError(message, currentLine(), currentColumn());
	}
	advance();
}

// Parse a qualified name (dotted identifier like 'iter.Iterator')
std::string Parser::parseQualifiedName(const std::string &context) {
	Token firstToken = expect(TokenType::IDENTIFIER, "Expected " + context);
	std::string qualifiedName = firstToken.value;

	// Parse additional dotted components
	while (check(TokenType::DOT)) {
		advance(); // consume '.'
		Token nextToken = expect(TokenType::IDENTIFIER, "Expected identifier after '.' in " + context);
		qualifiedName += "." + nextToken.value;
	}

	return qualifiedName;
}
