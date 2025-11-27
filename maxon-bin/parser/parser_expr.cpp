#include "../parser.h"
#include <stdexcept>

std::unique_ptr<ExprAST> Parser::parsePrimary() {
	if (check(TokenType::NUMBER)) {
		int value = std::stoi(currentToken().value);
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		return std::make_unique<NumberExprAST>(value, line, column);
	}

	if (check(TokenType::FLOAT_LITERAL)) {
		double value = std::stod(currentToken().value);
		int line = currentToken().line;
		int column = currentToken().column;
		std::string literalString = currentToken().value;
		advance();
		return std::make_unique<FloatExprAST>(value, line, column, literalString);
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "true") {
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		return std::make_unique<BooleanExprAST>(true, line, column);
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "false") {
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		return std::make_unique<BooleanExprAST>(false, line, column);
	}

	if (check(TokenType::CHARACTER)) {
		char value = currentToken().value[0]; // Get first character
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		return std::make_unique<CharacterExprAST>(value, line, column);
	}

	if (check(TokenType::STRING)) {
		std::string value = currentToken().value;
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		return std::make_unique<StringLiteralExprAST>(value, line, column);
	}

	// Array literal: [5]int or [1,2,3]
	if (check(TokenType::LBRACKET)) {
		int line = currentToken().line;
		int column = currentToken().column;
		advance(); // consume '['

		// Look ahead to determine which form:
		// - If first element is a number followed by ']' then type: [size]type
		// - Otherwise: [val1, val2, ...]

		if (check(TokenType::NUMBER) && peek(1).type == TokenType::RBRACKET) {
			// [size]type form
			Token sizeToken = expect(TokenType::NUMBER, "Expected array size");
			int size = std::stoi(sizeToken.value);
			expect(TokenType::RBRACKET, "Expected ']' after array size");

			// Now expect the element type (primitive or struct)
			std::string elementType;
			if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) {
				elementType = currentToken().value;
				advance();
			} else if (check(TokenType::IDENTIFIER)) {
				elementType = parseQualifiedName("array element type");
			} else {
				throw std::runtime_error("Expected array element type (int, float, ptr, char, string, or struct name) at line " +
										 std::to_string(currentToken().line));
			}

			return std::make_unique<ArrayLiteralExprAST>(size, elementType, line, column);
		} else {
			// [val1, val2, ...] form
			std::vector<std::unique_ptr<ExprAST>> values;

			if (!check(TokenType::RBRACKET)) {
				values.push_back(parseLogicalOr());

				while (match(TokenType::COMMA)) {
					values.push_back(parseLogicalOr());
				}
			}

			expect(TokenType::RBRACKET, "Expected ']' after array values");
			return std::make_unique<ArrayLiteralExprAST>(std::move(values), line, column);
		}
	}

	// Math intrinsic function keywords (built-in functions)
	if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::MathIntrinsic) {
		std::string funcName = currentToken().value;
		int line = currentToken().line;
		int column = currentToken().column;
		advance();

		expect(TokenType::LPAREN, "Expected '(' after '" + funcName + "'");
		auto arg = parseLogicalOr();
		expect(TokenType::RPAREN, "Expected ')' after argument");

		std::vector<std::unique_ptr<ExprAST>> args;
		args.push_back(std::move(arg));
		return std::make_unique<CallExprAST>(funcName, std::move(args), line, column);
	}

	if (check(TokenType::IDENTIFIER)) {
		std::string name = currentToken().value;
		int line = currentToken().line;
		int column = currentToken().column;
		advance();

		// Check for struct initialization (e.g., Planet{x: 1.0, y: 2.0})
		if (check(TokenType::LBRACE)) {
			return parseStructInit(name);
		}

		// Check for member access (e.g., array.length) or method call (e.g., arr.push(5))
		// This must come before namespace qualification check
		if (check(TokenType::DOT) && !check(TokenType::LPAREN, 1)) {
			advance(); // consume '.'
			Token member = expect(TokenType::IDENTIFIER, "Expected member name after '.'");

			// If followed by '(', could be namespace call or method call
			if (check(TokenType::LPAREN)) {
				// Check if this is a method call: simple variable name followed by method(args)
				// vs namespace call: namespace.namespace.function(args)
				// Method calls have no dots in the object name
				if (name.find('.') == std::string::npos) {
					// This is a method call: arr.push(5) -> push(arr, 5)
					std::string methodName = member.value;
					advance(); // consume '('
					std::vector<std::unique_ptr<ExprAST>> args;

					// First argument is the object itself
					args.push_back(std::make_unique<VariableExprAST>(name, line, column));

					// Parse remaining arguments
					if (!check(TokenType::RPAREN)) {
						args.push_back(parseLogicalOr());

						while (match(TokenType::COMMA)) {
							args.push_back(parseLogicalOr());
						}
					}

					expect(TokenType::RPAREN, "Expected ')' after method arguments");
					return std::make_unique<CallExprAST>(methodName, std::move(args), line, column);
				}

				// This is namespace.function() - restore as qualified name
				std::string qualifiedName = name + "." + member.value;

				// Continue building qualified name for multiple namespaces
				while (check(TokenType::DOT) && peek(1).type == TokenType::IDENTIFIER) {
					advance(); // consume '.'
					Token nextMember = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
					qualifiedName = qualifiedName + "." + nextMember.value;
				}

				advance(); // consume '('
				std::vector<std::unique_ptr<ExprAST>> args;

				// Parse arguments
				if (!check(TokenType::RPAREN)) {
					args.push_back(parseLogicalOr());

					while (match(TokenType::COMMA)) {
						args.push_back(parseLogicalOr());
					}
				}

				expect(TokenType::RPAREN, "Expected ')' after function arguments");
				return std::make_unique<CallExprAST>(qualifiedName, std::move(args), line, column);
			} else {
				// This is a member access (e.g., array.length)
				return std::make_unique<MemberAccessExprAST>(name, member.value, line, column);
			}
		}

		// Check for namespace qualification (namespace.namespace.function)
		// Support multiple levels: stdlib.fmt.function
		while (check(TokenType::DOT)) {
			advance(); // consume '.'
			Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
			name = name + "." + memberName.value;
		}

		// Check for function call
		if (check(TokenType::LPAREN)) {
			advance(); // consume '('
			std::vector<std::unique_ptr<ExprAST>> args;

			// Parse arguments
			if (!check(TokenType::RPAREN)) {
				args.push_back(parseLogicalOr());

				while (match(TokenType::COMMA)) {
					args.push_back(parseLogicalOr());
				}
			}

			expect(TokenType::RPAREN, "Expected ')' after function arguments");
			return std::make_unique<CallExprAST>(name, std::move(args), line, column);
		}

		// Check for array indexing
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['
			auto index = parseLogicalOr();
			expect(TokenType::RBRACKET, "Expected ']' after array index");
			auto arrayExpr = std::make_unique<ArrayIndexExprAST>(name, std::move(index), line, column);

			// Check for member access on array element (e.g., arr[0].field)
			if (check(TokenType::DOT)) {
				advance(); // consume '.'
				Token member = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
				// Create a member access expression with the array index as the object
				return std::make_unique<MemberAccessExprAST>(std::move(arrayExpr), member.value, line, column);
			}

			return arrayExpr;
		}

		// Just a variable reference
		return std::make_unique<VariableExprAST>(name, line, column);
	}

	if (match(TokenType::LPAREN)) {
		auto expr = parseLogicalOr();
		expect(TokenType::RPAREN, "Expected ')' to close parenthesized expression");
		return expr;
	}

	std::string foundStr;
	if (currentToken().type == TokenType::END_OF_FILE) {
		foundStr = "end of file";
	} else {
		foundStr = "'" + currentToken().value + "'";
	}

	throw std::runtime_error("Expected expression\n  Found: " + foundStr +
							 "\n  Location: line " + std::to_string(currentToken().line) +
							 ", column " + std::to_string(currentToken().column) +
							 "\n  Note: An expression can be a number, variable, function call, or arithmetic/comparison operation");
}

std::unique_ptr<ExprAST> Parser::parseUnary() {
	// Handle unary operators: -, +, not
	if (check(TokenType::MINUS) || check(TokenType::PLUS)) {
		char op = currentToken().value[0];
		int line = currentToken().line;
		int column = currentToken().column;
		advance();

		auto operand = parseUnary(); // Allow chaining: --x, +-x, etc.
		return std::make_unique<UnaryExprAST>(op, std::move(operand), line, column);
	}

	// Handle 'not' keyword
	if (check(TokenType::KEYWORD) && currentToken().value == "not") {
		int line = currentToken().line;
		int column = currentToken().column;
		advance();

		auto operand = parseUnary(); // Allow chaining: not not x
		return std::make_unique<UnaryExprAST>('!', std::move(operand), line, column);
	}

	return parsePrimary();
}

std::unique_ptr<ExprAST> Parser::parseFactor() {
	auto expr = parseUnary();

	// Handle type cast: expr as type
	if (check(TokenType::KEYWORD) && currentToken().value == "as") {
		int line = currentToken().line;
		int column = currentToken().column;
		advance(); // consume 'as'

		// Expect a type keyword
		if (currentToken().keywordData && currentToken().keywordData->category == KeywordCategory::Type) {
			std::string targetType = currentToken().value;
			advance();
			expr = std::make_unique<CastExprAST>(std::move(expr), targetType, line, column);
		} else {
			throw std::runtime_error("Expected type after 'as' keyword (int, float, ptr, char, string, or bool)\n  Location: line " +
									 std::to_string(currentToken().line) + ", column " +
									 std::to_string(currentToken().column));
		}
	}

	return expr;
}

std::unique_ptr<ExprAST> Parser::parseTerm() {
	auto left = parseFactor();

	while (check(TokenType::MULTIPLY) || check(TokenType::DIVIDE) ||
		   (check(TokenType::KEYWORD) && currentToken().value == "mod")) {
		char op;
		if (check(TokenType::KEYWORD) && currentToken().value == "mod") {
			op = '%';
		} else {
			op = currentToken().value[0];
		}
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		auto right = parseFactor();
		left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseComparison() {
	auto left = parseTerm();

	while (check(TokenType::PLUS) || check(TokenType::MINUS)) {
		char op = currentToken().value[0];
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		auto right = parseTerm();
		left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseExpression() {
	auto left = parseComparison();

	// Handle comparison operators
	if (check(TokenType::EQUAL_EQUAL) || check(TokenType::NOT_EQUAL) ||
		check(TokenType::GT) || check(TokenType::LT) ||
		check(TokenType::GTE) || check(TokenType::LTE)) {

		char op;
		TokenType type = currentToken().type;
		int line = currentToken().line;
		int column = currentToken().column;

		if (type == TokenType::EQUAL_EQUAL)
			op = 'E'; // == (equality)
		else if (type == TokenType::NOT_EQUAL)
			op = 'N'; // !=
		else if (type == TokenType::GT)
			op = '>';
		else if (type == TokenType::LT)
			op = '<';
		else if (type == TokenType::GTE)
			op = 'G'; // >=
		else if (type == TokenType::LTE)
			op = 'L'; // <=

		advance();
		auto right = parseComparison();
		left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseLogicalAnd() {
	auto left = parseExpression();

	// Handle 'and' operator
	while (check(TokenType::KEYWORD) && currentToken().value == "and") {
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		auto right = parseExpression();
		left = std::make_unique<BinaryExprAST>('&', std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseLogicalOr() {
	auto left = parseLogicalAnd();

	// Handle 'or' operator
	while (check(TokenType::KEYWORD) && currentToken().value == "or") {
		int line = currentToken().line;
		int column = currentToken().column;
		advance();
		auto right = parseLogicalAnd();
		left = std::make_unique<BinaryExprAST>('|', std::move(left), std::move(right), line, column);
	}

	return left;
}
