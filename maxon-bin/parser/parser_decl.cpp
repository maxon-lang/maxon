#include "../parser.h"
#include <stdexcept>

std::unique_ptr<FunctionAST> Parser::parseFunction() {
	// Check for export keyword
	bool isExported = false;
	if (check(TokenType::KEYWORD) && currentToken().value == "export") {
		isExported = true;
		advance(); // consume 'export'
	}

	// Check for extern keyword
	bool isExtern = false;
	if (check(TokenType::KEYWORD) && currentToken().value == "extern") {
		isExtern = true;
		advance(); // consume 'extern'
	}

	Token funcToken = expectKeyword("function", "Expected 'function'");
	Token name = expect(TokenType::IDENTIFIER, "Expected function name");

	logTrace("Parsing function '" + name.value + "'" +
			 (isExtern ? " (extern)" : "") +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(funcToken.line));

	expect(TokenType::LPAREN, "Expected '('");

	// Parse function parameters
	std::vector<FunctionParameter> parameters;
	if (!check(TokenType::RPAREN)) {
		do {
			Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");

			// Check for array type: []type only (sized arrays not allowed in parameters)
			std::string paramType;
			if (check(TokenType::LBRACKET)) {
				advance(); // consume '['

				// Array parameters must be unsized - reject [N]type syntax
				if (check(TokenType::NUMBER)) {
					throw std::runtime_error("Array parameters must be unsized: use []type, not [" + currentToken().value + "]type\n  Location: line " +
											 std::to_string(currentToken().line) + ", column " +
											 std::to_string(currentToken().column));
				}

				expect(TokenType::RBRACKET, "Expected ']' after '['");

				// Get element type
				std::string elementType;
				if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) {
					elementType = currentToken().value;
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					elementType = parseQualifiedName("array element type");
				} else {
					throw std::runtime_error("Expected array element type (int, float, ptr, char, string, bool, or struct name)\n  Location: line " +
											 std::to_string(currentToken().line) + ", column " +
											 std::to_string(currentToken().column));
				}

				// All array parameters are unsized
				paramType = "[]" + elementType;
			} else {
				// Regular scalar type (or struct)
				if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) {
					paramType = currentToken().value;
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					paramType = parseQualifiedName("parameter type");
				} else {
					throw std::runtime_error("Expected parameter type (int, float, ptr, char, string, bool, struct name, or [size]type)\n  Location: line " +
											 std::to_string(currentToken().line) + ", column " +
											 std::to_string(currentToken().column));
				}
			}

			parameters.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
		} while (match(TokenType::COMMA));
	}

	expect(TokenType::RPAREN, "Expected ')'");

	// Parse return type (optional - defaults to void)
	std::string returnType = "void";
	if ((currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
		returnType = currentToken().value;
		advance();
	}

	std::vector<std::unique_ptr<StmtAST>> body;

	// External functions don't have bodies
	if (isExtern) {
		// No body for extern functions - they're just declarations
		logTrace("Extern function '" + name.value + "' -> " + returnType + " (" + std::to_string(parameters.size()) + " params)");
		return std::make_unique<FunctionAST>(name.value, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported);
	}

	// Parse function body
	while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatement());
	}

	expectKeyword("end", "Expected 'end' to close function body");

	// Function has implicit block identifier which is the function name
	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected function name as block identifier after 'end'");
	if (endBlockIdToken.value != name.value) {
		throw std::runtime_error("Block identifier mismatch in function definition" +
								 std::string("\n  Expected: '") + name.value + "'" +
								 "\n  Found: '" + endBlockIdToken.value + "'" +
								 "\n  Location: line " + std::to_string(endBlockIdToken.line) +
								 ", column " + std::to_string(endBlockIdToken.column) +
								 "\n  Note: The 'end' block identifier must match the function name");
	}

	logTrace("Function '" + name.value + "' -> " + returnType + " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	return std::make_unique<FunctionAST>(name.value, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported);
}

std::unique_ptr<StructDefAST> Parser::parseStruct() {
	// Check for export keyword
	bool isExported = false;
	if (check(TokenType::KEYWORD) && currentToken().value == "export") {
		isExported = true;
		advance(); // consume 'export'
	}

	Token structToken = expectKeyword("struct", "Expected 'struct'");
	int line = structToken.line;
	int column = structToken.column;

	Token nameToken = expect(TokenType::IDENTIFIER, "Expected struct name after 'struct'");
	std::string structName = nameToken.value;

	std::vector<StructField> fields;

	// Parse fields until we hit 'end'
	while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
		Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name");
		std::string fieldName = fieldNameToken.value;

		std::string fieldType;
		if (Lexer::isTypeToken(currentToken())) {
			fieldType = currentToken().value;
			advance();
		} else if (check(TokenType::IDENTIFIER)) {
			fieldType = parseQualifiedName("struct field type");
		} else {
			throw std::runtime_error("Expected type after field name in struct field at line " +
									 std::to_string(currentToken().line) + ", column " +
									 std::to_string(currentToken().column));
		}

		fields.push_back(StructField(fieldName, fieldType, fieldNameToken.line, fieldNameToken.column));
	}

	expectKeyword("end", "Expected 'end' to close struct");

	// Require matching block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match struct name)");
	if (blockIdToken.value != structName) {
		throw std::runtime_error("Block identifier mismatch in struct definition" +
								 std::string("\n  Expected: '") + structName + "'" +
								 std::string("\n  Got: '") + blockIdToken.value + "'" +
								 std::string("\n  at line ") + std::to_string(blockIdToken.line) +
								 std::string(", column ") + std::to_string(blockIdToken.column));
	}

	return std::make_unique<StructDefAST>(structName, std::move(fields), line, column, defaultNamespace, isExported);
}

std::unique_ptr<StructInitExprAST> Parser::parseStructInit(const std::string &structName) {
	int line = currentToken().line;
	int column = currentToken().column;

	expect(TokenType::LBRACE, "Expected '{' for struct initialization");

	std::vector<StructInitField> fields;

	// Parse field initializers: fieldName: value
	while (!check(TokenType::RBRACE) && !check(TokenType::END_OF_FILE)) {
		Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name");
		std::string fieldName = fieldNameToken.value;

		expect(TokenType::COLON, "Expected ':' after field name");

		auto value = parseExpression();

		fields.push_back(StructInitField(fieldName, std::move(value),
										 fieldNameToken.line, fieldNameToken.column));

		// Check for comma (more fields) or closing brace
		if (check(TokenType::COMMA)) {
			advance();
		} else if (!check(TokenType::RBRACE)) {
			throw std::runtime_error("Expected ',' or '}' in struct initialization at line " +
									 std::to_string(currentToken().line) + ", column " +
									 std::to_string(currentToken().column));
		}
	}

	expect(TokenType::RBRACE, "Expected '}' to close struct initialization");

	return std::make_unique<StructInitExprAST>(structName, std::move(fields), line, column);
}
