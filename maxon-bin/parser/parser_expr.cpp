#include "../parser.h"
#include <stdexcept>

std::unique_ptr<ExprAST> Parser::parsePrimary() {
	if (check(TokenType::NUMBER)) {
		int value = std::stoi(std::string(currentValue()));
		int line = currentLine();
		int column = currentColumn();
		advance();
		return std::make_unique<NumberExprAST>(value, line, column);
	}

	if (check(TokenType::BYTE_LITERAL)) {
		uint8_t value = static_cast<uint8_t>(std::stoi(std::string(currentValue())));
		int line = currentLine();
		int column = currentColumn();
		advance();
		return std::make_unique<ByteExprAST>(value, line, column);
	}

	if (check(TokenType::FLOAT_LITERAL)) {
		double value = std::stod(std::string(currentValue()));
		int line = currentLine();
		int column = currentColumn();
		std::string literalString = std::string(currentValue());
		advance();
		return std::make_unique<FloatExprAST>(value, line, column, literalString);
	}

	if (checkKeyword("true")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		return std::make_unique<BooleanExprAST>(true, line, column);
	}

	if (checkKeyword("false")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		return std::make_unique<BooleanExprAST>(false, line, column);
	}

	if (check(TokenType::CHARACTER)) {
		char value = currentValue()[0]; // Get first character
		int line = currentLine();
		int column = currentColumn();
		advance();
		return std::make_unique<CharacterExprAST>(value, line, column);
	}

	if (check(TokenType::STRING)) {
		std::string value = std::string(currentValue());
		int line = currentLine();
		int column = currentColumn();
		advance();
		return std::make_unique<StringLiteralExprAST>(value, line, column);
	}

	// Array literal: [5]int or [1,2,3]
	if (check(TokenType::LBRACKET)) {
		int line = currentLine();
		int column = currentColumn();
		advance(); // consume '['

		// Look ahead to determine which form:
		// - If first element is a number followed by ']' then type: [size]type
		// - Otherwise: [val1, val2, ...]

		if (check(TokenType::NUMBER) && peekToken(1).type == TokenType::RBRACKET) {
			// [size]type form
			Token sizeToken = expect(TokenType::NUMBER, "Expected array size");
			int size = std::stoi(sizeToken.value);
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array size");

			// Now expect the element type (primitive or struct)
			std::string elementType;
			auto kd = currentKeywordData();
			if (kd && kd->category == KeywordCategory::Type) {
				elementType = std::string(currentValue());
				advance();
			} else if (check(TokenType::IDENTIFIER)) {
				elementType = parseQualifiedName("array element type");
			} else {
				throw std::runtime_error("Expected array element type (int, float, ptr, char, string, or struct name) at line " +
										 std::to_string(currentLine()));
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

			expectAdvance(TokenType::RBRACKET, "Expected ']' after array values");
			return std::make_unique<ArrayLiteralExprAST>(std::move(values), line, column);
		}
	}

	// Math intrinsic function keywords (built-in functions)
	auto mathKd = currentKeywordData();
	if (mathKd && mathKd->category == KeywordCategory::MathIntrinsic) {
		std::string funcName = std::string(currentValue());
		int line = currentLine();
		int column = currentColumn();
		advance();

		expectAdvance(TokenType::LPAREN, "Expected '(' after '" + funcName + "'");
		auto arg = parseLogicalOr();
		expectAdvance(TokenType::RPAREN, "Expected ')' after argument");

		std::vector<std::unique_ptr<ExprAST>> args;
		args.push_back(std::move(arg));
		return std::make_unique<CallExprAST>(funcName, std::move(args), line, column);
	}

	if (check(TokenType::IDENTIFIER)) {
		std::string name = std::string(currentValue());
		int line = currentLine();
		int column = currentColumn();
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

			// If followed by '(', could be namespace call, static method call, or instance method call
			if (check(TokenType::LPAREN)) {
				// Check if this is a method call on a variable: simple variable name followed by method(args)
				// vs namespace/type call: Type.method(args) or namespace.function(args)
				// Method calls have no dots in the object name and are lowercase (variable names)
				// Type names and namespaces typically start with uppercase or are known patterns
				if (name.find('.') == std::string::npos) {
					// This could be a method call on a variable: arr.push(5) -> push(arr, 5)
					// Or a Type.method(args) call - we distinguish by checking if name looks like a type
					// Types start with uppercase; variables are lowercase
					// Note: For Type.method() static calls, we still treat as qualified function
					// Special case: 'string' is a type name even though it's lowercase
					bool looksLikeVariable = !name.empty() && (name[0] >= 'a' && name[0] <= 'z') && name != "string";

					if (looksLikeVariable) {
						// This is a method call on a variable: arr.push(5) -> push(arr, 5)
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

						expectAdvance(TokenType::RPAREN, "Expected ')' after method arguments");
						return std::make_unique<CallExprAST>(methodName, std::move(args), line, column);
					}
				}

				// This is Type.method() or namespace.function() - treat as qualified function call
				std::string qualifiedName = name + "." + member.value;

				// Continue building qualified name for multiple namespaces
				while (check(TokenType::DOT) && peekToken(1).type == TokenType::IDENTIFIER) {
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

				expectAdvance(TokenType::RPAREN, "Expected ')' after function arguments");
				return std::make_unique<CallExprAST>(qualifiedName, std::move(args), line, column);
			} else {
				// This is a member access (e.g., struct.field or struct.arrayField)
				auto memberExpr = std::make_unique<MemberAccessExprAST>(name, member.value, line, column);

				// Handle chained member access (e.g., self.data.length)
				while (check(TokenType::DOT)) {
					advance(); // consume '.'
					Token nextMember = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
					// Wrap the current expression in a new MemberAccessExprAST
					memberExpr = std::make_unique<MemberAccessExprAST>(std::move(memberExpr), nextMember.value, line, column);
				}

				// Check for array indexing on the member (e.g., struct.arrayField[i])
				if (check(TokenType::LBRACKET)) {
					advance(); // consume '['
					auto index = parseLogicalOr();
					expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");
					return std::make_unique<ArrayIndexExprAST>(std::move(memberExpr), std::move(index), line, column);
				}

				return memberExpr;
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

			expectAdvance(TokenType::RPAREN, "Expected ')' after function arguments");
			return std::make_unique<CallExprAST>(name, std::move(args), line, column);
		}

		// Check for array indexing or slicing
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['

			// Check for slice starting with .. (e.g., s[..end])
			if (check(TokenType::DOT_DOT)) {
				advance(); // consume '..'
				auto endExpr = parseLogicalOr();
				expectAdvance(TokenType::RBRACKET, "Expected ']' after slice end");
				return std::make_unique<SliceExprAST>(name, nullptr, std::move(endExpr), line, column);
			}

			// Parse first expression (could be index or slice start)
			auto firstExpr = parseLogicalOr();

			// Check for slice syntax: s[start..end] or s[start..]
			if (check(TokenType::DOT_DOT)) {
				advance(); // consume '..'

				// Check for open-ended slice: s[start..]
				if (check(TokenType::RBRACKET)) {
					advance(); // consume ']'
					return std::make_unique<SliceExprAST>(name, std::move(firstExpr), nullptr, line, column);
				}

				// Full slice: s[start..end]
				auto endExpr = parseLogicalOr();
				expectAdvance(TokenType::RBRACKET, "Expected ']' after slice end");
				return std::make_unique<SliceExprAST>(name, std::move(firstExpr), std::move(endExpr), line, column);
			}

			// Regular array index
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");
			auto arrayExpr = std::make_unique<ArrayIndexExprAST>(name, std::move(firstExpr), line, column);

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
		expectAdvance(TokenType::RPAREN, "Expected ')' to close parenthesized expression");
		return expr;
	}

	std::string foundStr;
	if (currentType() == TokenType::END_OF_FILE) {
		foundStr = "end of file";
	} else {
		foundStr = "'" + std::string(currentValue()) + "'";
	}

	throw std::runtime_error("Expected expression\n  Found: " + foundStr +
							 "\n  Location: line " + std::to_string(currentLine()) +
							 ", column " + std::to_string(currentColumn()) +
							 "\n  Note: An expression can be a number, variable, function call, or arithmetic/comparison operation");
}

std::unique_ptr<ExprAST> Parser::parseUnary() {
	// Handle unary operators: -, +, not
	if (check(TokenType::MINUS) || check(TokenType::PLUS)) {
		char op = currentValue()[0];
		int line = currentLine();
		int column = currentColumn();
		advance();

		auto operand = parseUnary(); // Allow chaining: --x, +-x, etc.
		return std::make_unique<UnaryExprAST>(op, std::move(operand), line, column);
	}

	// Handle 'not' keyword
	if (checkKeyword("not")) {
		int line = currentLine();
		int column = currentColumn();
		advance();

		auto operand = parseUnary(); // Allow chaining: not not x
		return std::make_unique<UnaryExprAST>('!', std::move(operand), line, column);
	}

	return parsePrimary();
}

std::unique_ptr<ExprAST> Parser::parseFactor() {
	auto expr = parseUnary();

	// Handle type cast: expr as type
	if (checkKeyword("as")) {
		int line = currentLine();
		int column = currentColumn();
		advance(); // consume 'as'

		// Expect a type keyword or struct name (identifier)
		std::string targetType;
		auto kd = currentKeywordData();
		if (kd && kd->category == KeywordCategory::Type) {
			targetType = std::string(currentValue());
			advance();
		} else if (check(TokenType::IDENTIFIER)) {
			// Allow struct type names for ExpressibleByStringLiteral etc.
			targetType = parseQualifiedName("cast target type");
		} else {
			throw std::runtime_error("Expected type after 'as' keyword (int, float, ptr, char, string, bool, or struct name)\n  Location: line " +
									 std::to_string(currentLine()) + ", column " +
									 std::to_string(currentColumn()));
		}
		expr = std::make_unique<CastExprAST>(std::move(expr), targetType, line, column);
	}

	return expr;
}

std::unique_ptr<ExprAST> Parser::parseTerm() {
	auto left = parseFactor();

	while (check(TokenType::MULTIPLY) || check(TokenType::DIVIDE) || checkKeyword("mod")) {
		char op;
		if (checkKeyword("mod")) {
			op = '%';
		} else {
			op = currentValue()[0];
		}
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseFactor();
		left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseAdditive() {
	auto left = parseTerm();

	while (check(TokenType::PLUS) || check(TokenType::MINUS)) {
		char op = currentValue()[0];
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseTerm();
		left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseShift() {
	auto left = parseAdditive();

	// Handle bitwise shift operators: <<, >>
	while (check(TokenType::LSHIFT) || check(TokenType::RSHIFT)) {
		char op = check(TokenType::LSHIFT) ? 'S' : 'H'; // S = shift left, H = shift right
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseAdditive();
		left = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseComparison() {
	auto left = parseShift();

	return left;
}

std::unique_ptr<ExprAST> Parser::parseExpression() {
	auto left = parseComparison();

	// Handle comparison operators using bitmask lookup
	if (ParseDecisionTable::is_comparison_op(currentType())) {

		char op;
		TokenType type = currentType();
		int line = currentLine();
		int column = currentColumn();

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

std::unique_ptr<ExprAST> Parser::parseBitwiseAnd() {
	auto left = parseExpression();

	// Handle bitwise AND operator: &
	while (check(TokenType::AMPERSAND)) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseExpression();
		left = std::make_unique<BinaryExprAST>('&', std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseBitwiseXor() {
	auto left = parseBitwiseAnd();

	// Handle bitwise XOR operator: ^
	while (check(TokenType::CARET)) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseBitwiseAnd();
		left = std::make_unique<BinaryExprAST>('^', std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseBitwiseOr() {
	auto left = parseBitwiseXor();

	// Handle bitwise OR operator: |
	while (check(TokenType::PIPE)) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseBitwiseXor();
		left = std::make_unique<BinaryExprAST>('|', std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseLogicalAnd() {
	auto left = parseBitwiseOr();

	// Handle 'and' operator (logical AND)
	while (checkKeyword("and")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseBitwiseOr();
		left = std::make_unique<BinaryExprAST>('A', std::move(left), std::move(right), line, column);
	}

	return left;
}

std::unique_ptr<ExprAST> Parser::parseLogicalOr() {
	auto left = parseLogicalAnd();

	// Handle 'or' operator (logical OR)
	while (checkKeyword("or")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto right = parseLogicalAnd();
		left = std::make_unique<BinaryExprAST>('O', std::move(left), std::move(right), line, column);
	}

	return left;
}
