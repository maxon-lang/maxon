#include "../parser.h"
#include "../types/type_conversion.h"
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

// Parse a type string, handling:
// - Primitive types: int, float, bool, byte
// - Struct types: MyStruct, pkg.MyStruct
// - Array types: array of T, array of N T, array of array of T
// - Function types: (T1, T2) -> R
std::string Parser::parseTypeString(const std::string &context) {
	// Check for function type: (T1, T2, ...) -> R
	if (check(TokenType::LPAREN)) {
		advance(); // consume '('

		std::vector<std::string> paramTypes;
		if (!check(TokenType::RPAREN)) {
			do {
				paramTypes.push_back(parseTypeString("function parameter type"));
			} while (match(TokenType::COMMA));
		}
		expectAdvance(TokenType::RPAREN, "Expected ')' in function type");

		expectAdvance(TokenType::ARROW, "Expected '->' in function type");

		std::string returnType = parseTypeString("function return type");

		// Build function type string: fn(T1,T2)->R
		std::string funcType = "fn(";
		for (size_t i = 0; i < paramTypes.size(); i++) {
			if (i > 0) funcType += ",";
			funcType += paramTypes[i];
		}
		funcType += ")->" + returnType;
		return funcType;
	}

	// Check for 'array' keyword - new array syntax
	if (checkKeyword("array")) {
		advance(); // consume 'array'

		if (!checkKeyword("of")) {
			reportError("Expected 'of' after 'array' in type declaration\n"
						"  Use: array of int, array of 5 byte, etc.",
						currentLine(), currentColumn());
		}
		advance(); // consume 'of'

		// Check for sized array: array of N T
		if (check(TokenType::NUMBER)) {
			Token sizeToken = currentToken();
			int size = std::stoi(sizeToken.value);
			advance(); // consume the number

			// Now parse the element type (recursive for nested arrays)
			std::string elementType = parseTypeString(context);

			// Return internal representation: _StaticArray<N, T>
			return maxon::TypeConversion::makeStaticArrayType(size, elementType);
		}

		// Unsized array: array of T (recursive for nested arrays)
		std::string elementType = parseTypeString(context);
		return maxon::TypeConversion::makeManagedArrayType(elementType);
	}

	// Check for type keywords (int, float, bool, byte, array, of)
	auto kd = currentKeywordData();
	if (kd && kd->category == KeywordCategory::Type) {
		std::string typeName = std::string(currentValue());
		// Skip 'of' keyword as it's only valid after 'array'
		if (typeName == "of") {
			reportError("Unexpected 'of' keyword - use 'array of T' syntax",
						currentLine(), currentColumn());
		}
		advance();
		return typeName;
	}

	// Must be a struct/identifier type
	if (check(TokenType::IDENTIFIER)) {
		return parseQualifiedName(context);
	}

	reportError("Expected type for " + context, currentLine(), currentColumn());
}

// Parse a type string with optional "or nil" suffix
std::string Parser::parseTypeStringWithOptional(const std::string &context) {
	std::string baseType = parseTypeString(context);

	// Check for "or nil" suffix for optional types
	if (checkKeyword("or")) {
		advance(); // consume 'or'
		if (!checkKeyword("nil")) {
			reportError("Expected 'nil' after 'or' in type",
						currentLine(), currentColumn());
		}
		advance(); // consume 'nil'

		// Reject nested optionals
		if (baseType.find(" or nil") != std::string::npos) {
			reportError("Cannot make optional type '" + baseType + "' optional again",
						currentLine(), currentColumn());
		}

		return baseType + " or nil";
	}

	return baseType;
}

// ============================================================================
// Error Recovery Methods
// ============================================================================

// Check if the current token is a synchronization point
// Sync tokens are tokens that typically start new statements or declarations
bool Parser::isSyncToken() const {
	if (check(TokenType::END_OF_FILE)) {
		return true;
	}

	// Check for keywords that start statements or declarations
	if (check(TokenType::KEYWORD)) {
		std::string_view kw = currentValue();
		return kw == "function" || kw == "struct" || kw == "enum" ||
			   kw == "interface" || kw == "end" || kw == "var" ||
			   kw == "let" || kw == "if" || kw == "while" ||
			   kw == "for" || kw == "return" || kw == "break" ||
			   kw == "continue" || kw == "match" || kw == "export" ||
			   kw == "extern";
	}

	return false;
}

// Advance tokens until we reach a synchronization point
// This is called after catching an error to find a safe place to resume parsing
void Parser::synchronize() {
	inErrorRecovery_ = true;

	// Skip tokens until we find a sync point
	while (!check(TokenType::END_OF_FILE)) {
		// Check if current token is a sync point
		if (isSyncToken()) {
			inErrorRecovery_ = false;
			return;
		}
		advance();
	}

	inErrorRecovery_ = false;
}

// Parse a statement with error recovery
// On error, creates an ErrorStmtAST and synchronizes to next statement
std::unique_ptr<StmtAST> Parser::parseStatementWithRecovery() {
	int startLine = currentLine();
	int startCol = currentColumn();

	try {
		return parseStatement();
	} catch (const std::runtime_error &e) {
		// Record the error
		std::string errorMsg = e.what();
		parseErrors_.push_back(ParseError(errorMsg, startLine, startCol));

		// Track position before synchronization for error range
		int endLine = currentLine();
		int endCol = currentColumn();

		// Synchronize to next safe point
		synchronize();

		// Update end position to where we synchronized
		if (endLine != currentLine() || endCol != currentColumn()) {
			endLine = currentLine();
			endCol = currentColumn();
		}

		// Return an ErrorStmtAST as placeholder
		return std::make_unique<ErrorStmtAST>(errorMsg, startLine, startCol, endLine, endCol);
	}
}
