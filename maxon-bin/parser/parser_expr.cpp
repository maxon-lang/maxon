#include "../parser.h"
#include <stdexcept>

std::unique_ptr<ExprAST> Parser::parsePrimary() {
	if (check(TokenType::NUMBER)) {
		int value = std::stoi(std::string(currentValue()));
		int line = currentLine();
		int column = currentColumn();
		int endCol = column + static_cast<int>(currentValue().length()) - 1;
		advance();
		auto expr = std::make_unique<NumberExprAST>(value, line, column);
		expr->setEndPosition(line, endCol);
		return expr;
	}

	if (check(TokenType::BYTE_LITERAL)) {
		uint8_t value = static_cast<uint8_t>(std::stoi(std::string(currentValue())));
		int line = currentLine();
		int column = currentColumn();
		int endCol = column + static_cast<int>(currentValue().length()) - 1;
		advance();
		auto expr = std::make_unique<ByteExprAST>(value, line, column);
		expr->setEndPosition(line, endCol);
		return expr;
	}

	if (check(TokenType::FLOAT_LITERAL)) {
		double value = std::stod(std::string(currentValue()));
		int line = currentLine();
		int column = currentColumn();
		std::string literalString = std::string(currentValue());
		int endCol = column + static_cast<int>(literalString.length()) - 1;
		advance();
		auto expr = std::make_unique<FloatExprAST>(value, line, column, literalString);
		expr->setEndPosition(line, endCol);
		return expr;
	}

	if (checkKeyword("true")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto expr = std::make_unique<BooleanExprAST>(true, line, column);
		expr->setEndPosition(line, column + 3); // "true" is 4 chars
		return expr;
	}

	if (checkKeyword("false")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto expr = std::make_unique<BooleanExprAST>(false, line, column);
		expr->setEndPosition(line, column + 4); // "false" is 5 chars
		return expr;
	}

	if (checkKeyword("nil")) {
		int line = currentLine();
		int column = currentColumn();
		advance();
		auto expr = std::make_unique<NilExprAST>(line, column);
		expr->setEndPosition(line, column + 2); // "nil" is 3 chars
		return expr;
	}

	// Match expression (used in expression context, e.g., let x = match y 'id' ...)
	if (checkKeyword("match")) {
		return parseMatchExpr();
	}

	if (check(TokenType::CHARACTER)) {
		std::string value = std::string(currentValue()); // Get full grapheme cluster
		int line = currentLine();
		int column = currentColumn();
		// Character literals are 'x', so +2 for the quotes plus content
		int endCol = column + static_cast<int>(value.length()) + 1;
		advance();
		auto expr = std::make_unique<CharacterExprAST>(value, line, column);
		expr->setEndPosition(line, endCol);
		return expr;
	}

	if (check(TokenType::STRING)) {
		std::string value = std::string(currentValue());
		int line = currentLine();
		int column = currentColumn();
		// String literals are "...", so +2 for the quotes
		int endCol = column + static_cast<int>(value.length()) + 1;
		advance();
		auto expr = std::make_unique<StringLiteralExprAST>(value, line, column);
		expr->setEndPosition(line, endCol);
		return expr;
	}

	// Dictionary type pattern: TypeName from KeyType to ValueType
	// Works with any type that conforms to the Dictionary interface (e.g., map, HashMap, OrderedMap)
	if (check(TokenType::IDENTIFIER) && checkKeyword("from", 1)) {
		int line = currentLine();
		int column = currentColumn();
		std::string dictTypeName = std::string(currentValue());
		advance(); // consume type name (e.g., 'map')

		expectKeywordAdvance("from", "Expected 'from' after dictionary type name");

		// Parse key type
		std::string keyType;
		auto kd = currentKeywordData();
		if (kd && kd->category == KeywordCategory::Type) {
			keyType = std::string(currentValue());
			advance();
		} else if (check(TokenType::IDENTIFIER)) {
			keyType = parseQualifiedName("map key type");
		} else {
			reportError("Expected key type after 'from' in map declaration",
						currentLine(), currentColumn());
		}

		expectKeywordAdvance("to", "Expected 'to' after key type in map declaration");

		// Parse value type - capture end position
		std::string valueType;
		int endLine = currentLine();
		int endCol = currentColumn();
		kd = currentKeywordData();
		if (kd && kd->category == KeywordCategory::Type) {
			valueType = std::string(currentValue());
			endCol = currentColumn() + static_cast<int>(valueType.length()) - 1;
			advance();
		} else if (check(TokenType::IDENTIFIER)) {
			valueType = parseQualifiedName("map value type");
			// parseQualifiedName advances, so we need to look back at what was parsed
			endCol = currentColumn() - 1; // Approximate - after the identifier
		} else {
			reportError("Expected value type after 'to' in map declaration",
						currentLine(), currentColumn());
		}

		auto expr = std::make_unique<MapLiteralExprAST>(dictTypeName, keyType, valueType, line, column);
		expr->setEndPosition(endLine, endCol);
		return expr;
	}

	// Array literal: [5]int, [_len]byte, or [1,2,3]
	if (check(TokenType::LBRACKET)) {
		int line = currentLine();
		int column = currentColumn();
		advance(); // consume '['

		// Look ahead to determine which form:
		// - [size]type where size is a constant number
		// - [expr]type where expr is a runtime expression (variable, arithmetic, etc.)
		// - [val1, val2, ...] value-initialized array

		// Check for [size]type form with constant integer
		if (check(TokenType::NUMBER) && peekToken(1).type == TokenType::RBRACKET) {
			// [size]type form with constant size
			Token sizeToken = expect(TokenType::NUMBER, "Expected array size");
			int size = std::stoi(sizeToken.value);
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array size");

			// Now expect the element type (primitive or struct)
			std::string elementType;
			int endLine = currentLine();
			int endCol = currentColumn();
			auto kd = currentKeywordData();
			if (kd && kd->category == KeywordCategory::Type) {
				elementType = std::string(currentValue());
				endCol = currentColumn() + static_cast<int>(elementType.length()) - 1;
				advance();
			} else if (check(TokenType::IDENTIFIER)) {
				elementType = parseQualifiedName("array element type");
				endLine = currentLine();
				endCol = currentColumn() - 1;
			} else {
				reportError("Expected array element type (int, float, ptr, char, string, or struct name)",
							currentLine(), currentColumn());
			}

			auto expr = std::make_unique<ArrayLiteralExprAST>(size, elementType, line, column);
			expr->setEndPosition(endLine, endCol);
			return expr;
		}

		// Check for [expr]type form with variable size (e.g., [_len]byte, [n + 1]int)
		// This is an identifier (or expression) followed by ']' then a type
		if (check(TokenType::IDENTIFIER) && peekToken(1).type == TokenType::RBRACKET) {
			// Parse the size expression
			auto sizeExpr = parseLogicalOr();
			int rbracketLine = currentLine();
			int rbracketCol = currentColumn();
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array size expression");

			// Check if followed by a type (indicating [expr]type form)
			auto kd = currentKeywordData();
			if ((kd && kd->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
				// [expr]type form - variable-sized array
				std::string elementType;
				int endLine = currentLine();
				int endCol = currentColumn();
				if (kd && kd->category == KeywordCategory::Type) {
					elementType = std::string(currentValue());
					endCol = currentColumn() + static_cast<int>(elementType.length()) - 1;
					advance();
				} else {
					elementType = parseQualifiedName("array element type");
					endLine = currentLine();
					endCol = currentColumn() - 1;
				}
				auto expr = std::make_unique<ArrayLiteralExprAST>(std::move(sizeExpr), elementType, line, column);
				expr->setEndPosition(endLine, endCol);
				return expr;
			} else {
				// Not followed by type - this is a single-element array literal [val]
				// Re-wrap the expression as a value array
				std::vector<std::unique_ptr<ExprAST>> values;
				values.push_back(std::move(sizeExpr));
				auto expr = std::make_unique<ArrayLiteralExprAST>(std::move(values), line, column);
				expr->setEndPosition(rbracketLine, rbracketCol);
				return expr;
			}
		}

		// [val1, val2, ...] form or more complex [expr]type form
		std::vector<std::unique_ptr<ExprAST>> values;

		if (!check(TokenType::RBRACKET)) {
			values.push_back(parseLogicalOr());

			// Check if this might be [expr]type after parsing first expression
			if (check(TokenType::RBRACKET)) {
				size_t savedPos = position;
				advance(); // consume ']'

				// Check if followed by a type
				auto kd = currentKeywordData();
				if ((kd && kd->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
					// [expr]type form - variable-sized array
					std::string elementType;
					int endLine = currentLine();
					int endCol = currentColumn();
					if (kd && kd->category == KeywordCategory::Type) {
						elementType = std::string(currentValue());
						endCol = currentColumn() + static_cast<int>(elementType.length()) - 1;
						advance();
					} else {
						elementType = parseQualifiedName("array element type");
						endLine = currentLine();
						endCol = currentColumn() - 1;
					}
					auto expr = std::make_unique<ArrayLiteralExprAST>(std::move(values[0]), elementType, line, column);
					expr->setEndPosition(endLine, endCol);
					return expr;
				} else {
					// Not followed by type - restore and treat as single-element array
					position = savedPos;
					cache_.set_position(savedPos);
				}
			}

			while (match(TokenType::COMMA)) {
				values.push_back(parseLogicalOr());
			}
		}

		int endLine = currentLine();
		int endCol = currentColumn();
		expectAdvance(TokenType::RBRACKET, "Expected ']' after array values");
		auto expr = std::make_unique<ArrayLiteralExprAST>(std::move(values), line, column);
		expr->setEndPosition(endLine, endCol);
		return expr;
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
		int endLine = currentLine();
		int endCol = currentColumn();
		expectAdvance(TokenType::RPAREN, "Expected ')' after argument");

		std::vector<std::unique_ptr<ExprAST>> args;
		args.push_back(std::move(arg));
		auto expr = std::make_unique<CallExprAST>(funcName, std::move(args), line, column);
		expr->setEndPosition(endLine, endCol);
		return expr;
	}

	// Allow 'array' keyword as struct name in struct literals (stdlib collection type)
	if (checkKeyword("array") && check(TokenType::LBRACE, 1)) {
		advance(); // consume 'array'
		return parseStructInit("array");
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
						auto varExpr = std::make_unique<VariableExprAST>(name, line, column);
						varExpr->setEndPosition(line, column + static_cast<int>(name.length()) - 1);
						args.push_back(std::move(varExpr));

						// Parse remaining arguments
						if (!check(TokenType::RPAREN)) {
							args.push_back(parseLogicalOr());

							while (match(TokenType::COMMA)) {
								args.push_back(parseLogicalOr());
							}
						}

						int endLine = currentLine();
						int endCol = currentColumn();
						expectAdvance(TokenType::RPAREN, "Expected ')' after method arguments");
						auto callExpr = std::make_unique<CallExprAST>(methodName, std::move(args), line, column);
						callExpr->setEndPosition(endLine, endCol);
						return callExpr;
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

				int endLine = currentLine();
				int endCol = currentColumn();
				expectAdvance(TokenType::RPAREN, "Expected ')' after function arguments");
				auto callExpr = std::make_unique<CallExprAST>(qualifiedName, std::move(args), line, column);
				callExpr->setEndPosition(endLine, endCol);
				return callExpr;
			} else {
				// This is a member access (e.g., struct.field or struct.arrayField)
				auto memberExpr = std::make_unique<MemberAccessExprAST>(name, member.value, line, column);
				// Set initial end position to the member token
				memberExpr->setEndPosition(member.line, member.column + static_cast<int>(member.value.length()) - 1);

				// Handle chained member access (e.g., self.data.length)
				while (check(TokenType::DOT)) {
					advance(); // consume '.'
					Token nextMember = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
					// Wrap the current expression in a new MemberAccessExprAST
					memberExpr = std::make_unique<MemberAccessExprAST>(std::move(memberExpr), nextMember.value, line, column);
					memberExpr->setEndPosition(nextMember.line, nextMember.column + static_cast<int>(nextMember.value.length()) - 1);
				}

				// Check for array indexing on the member (e.g., struct.arrayField[i])
				if (check(TokenType::LBRACKET)) {
					advance(); // consume '['
					auto index = parseLogicalOr();
					int endLine = currentLine();
					int endCol = currentColumn();
					expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");
					auto arrayExpr = std::make_unique<ArrayIndexExprAST>(std::move(memberExpr), std::move(index), line, column);
					arrayExpr->setEndPosition(endLine, endCol);
					return arrayExpr;
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

			int endLine = currentLine();
			int endCol = currentColumn();
			expectAdvance(TokenType::RPAREN, "Expected ')' after function arguments");
			auto callExpr = std::make_unique<CallExprAST>(name, std::move(args), line, column);
			callExpr->setEndPosition(endLine, endCol);
			return callExpr;
		}

		// Check for array indexing or slicing
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['

			// Check for slice starting with .. (e.g., s[..end])
			if (check(TokenType::DOT_DOT)) {
				advance(); // consume '..'
				auto endExpr = parseLogicalOr();
				int sliceEndLine = currentLine();
				int sliceEndCol = currentColumn();
				expectAdvance(TokenType::RBRACKET, "Expected ']' after slice end");
				auto sliceExpr = std::make_unique<SliceExprAST>(name, nullptr, std::move(endExpr), line, column);
				sliceExpr->setEndPosition(sliceEndLine, sliceEndCol);
				return sliceExpr;
			}

			// Parse first expression (could be index or slice start)
			auto firstExpr = parseLogicalOr();

			// Check for slice syntax: s[start..end] or s[start..]
			if (check(TokenType::DOT_DOT)) {
				advance(); // consume '..'

				// Check for open-ended slice: s[start..]
				if (check(TokenType::RBRACKET)) {
					int sliceEndLine = currentLine();
					int sliceEndCol = currentColumn();
					advance(); // consume ']'
					auto sliceExpr = std::make_unique<SliceExprAST>(name, std::move(firstExpr), nullptr, line, column);
					sliceExpr->setEndPosition(sliceEndLine, sliceEndCol);
					return sliceExpr;
				}

				// Full slice: s[start..end]
				auto endExpr = parseLogicalOr();
				int sliceEndLine = currentLine();
				int sliceEndCol = currentColumn();
				expectAdvance(TokenType::RBRACKET, "Expected ']' after slice end");
				auto sliceExpr = std::make_unique<SliceExprAST>(name, std::move(firstExpr), std::move(endExpr), line, column);
				sliceExpr->setEndPosition(sliceEndLine, sliceEndCol);
				return sliceExpr;
			}

			// Regular array index
			int rbracketLine = currentLine();
			int rbracketCol = currentColumn();
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");
			auto arrayExpr = std::make_unique<ArrayIndexExprAST>(name, std::move(firstExpr), line, column);
			arrayExpr->setEndPosition(rbracketLine, rbracketCol);

			// Check for member access on array element (e.g., arr[0].field or arr[0].method())
			if (check(TokenType::DOT)) {
				advance(); // consume '.'
				Token member = expect(TokenType::IDENTIFIER, "Expected member name after '.'");

				// Check if this is a method call: arr[i].method(args)
				if (check(TokenType::LPAREN)) {
					advance(); // consume '('
					std::vector<std::unique_ptr<ExprAST>> args;

					// First argument is the array element (implicit self)
					args.push_back(std::move(arrayExpr));

					// Parse remaining arguments
					if (!check(TokenType::RPAREN)) {
						args.push_back(parseLogicalOr());

						while (match(TokenType::COMMA)) {
							args.push_back(parseLogicalOr());
						}
					}

					int callEndLine = currentLine();
					int callEndCol = currentColumn();
					expectAdvance(TokenType::RPAREN, "Expected ')' after method arguments");
					auto callExpr = std::make_unique<CallExprAST>(member.value, std::move(args), line, column);
					callExpr->setEndPosition(callEndLine, callEndCol);
					return callExpr;
				}

				// Create a member access expression with the array index as the object
				auto memberExpr = std::make_unique<MemberAccessExprAST>(std::move(arrayExpr), member.value, line, column);
				memberExpr->setEndPosition(member.line, member.column + static_cast<int>(member.value.length()) - 1);
				return memberExpr;
			}

			return arrayExpr;
		}

		// Just a variable reference
		auto varExpr = std::make_unique<VariableExprAST>(name, line, column);
		varExpr->setEndPosition(line, column + static_cast<int>(name.length()) - 1);
		return varExpr;
	}

	if (match(TokenType::LPAREN)) {
		auto expr = parseLogicalOr();
		int endLine = currentLine();
		int endCol = currentColumn();
		expectAdvance(TokenType::RPAREN, "Expected ')' to close parenthesized expression");
		// Set the end position to include the closing paren
		expr->setEndPosition(endLine, endCol);
		return expr;
	}

	std::string foundStr;
	if (currentType() == TokenType::END_OF_FILE) {
		foundStr = "end of file";
	} else {
		foundStr = "'" + std::string(currentValue()) + "'";
	}

	reportError("Expected expression\n  Found: " + foundStr +
					"\n  Note: An expression can be a number, variable, function call, or arithmetic/comparison operation",
				currentLine(), currentColumn());
}

std::unique_ptr<ExprAST> Parser::parseUnary() {
	// Handle unary operators: -, +, not
	if (check(TokenType::MINUS) || check(TokenType::PLUS)) {
		char op = currentValue()[0];
		int line = currentLine();
		int column = currentColumn();
		advance();

		auto operand = parseUnary(); // Allow chaining: --x, +-x, etc.
		auto expr = std::make_unique<UnaryExprAST>(op, std::move(operand), line, column);
		// End position is the end of the operand
		expr->setEndPosition(expr->operand->endLine, expr->operand->endColumn);
		return expr;
	}

	// Handle 'not' keyword
	if (checkKeyword("not")) {
		int line = currentLine();
		int column = currentColumn();
		advance();

		auto operand = parseUnary(); // Allow chaining: not not x
		auto expr = std::make_unique<UnaryExprAST>('!', std::move(operand), line, column);
		// End position is the end of the operand
		expr->setEndPosition(expr->operand->endLine, expr->operand->endColumn);
		return expr;
	}

	return parsePostfix();
}

std::unique_ptr<ExprAST> Parser::parsePostfix() {
	auto expr = parsePrimary();

	// Handle postfix operators: method calls, member access, array indexing
	while (true) {
		if (check(TokenType::DOT)) {
			advance(); // consume '.'
			Token member = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
			int line = expr->line;
			int column = expr->column;

			// Check if this is a method call: expr.method(args)
			if (check(TokenType::LPAREN)) {
				advance(); // consume '('
				std::vector<std::unique_ptr<ExprAST>> args;

				// First argument is the expression itself (implicit self)
				args.push_back(std::move(expr));

				// Parse remaining arguments
				if (!check(TokenType::RPAREN)) {
					args.push_back(parseLogicalOr());

					while (match(TokenType::COMMA)) {
						args.push_back(parseLogicalOr());
					}
				}

				int endLine = currentLine();
				int endCol = currentColumn();
				expectAdvance(TokenType::RPAREN, "Expected ')' after method arguments");
				expr = std::make_unique<CallExprAST>(member.value, std::move(args), line, column);
				expr->setEndPosition(endLine, endCol);
			} else {
				// Pure member access: expr.field
				expr = std::make_unique<MemberAccessExprAST>(std::move(expr), member.value, line, column);
				expr->setEndPosition(member.line, member.column + static_cast<int>(member.value.length()) - 1);
			}
		} else if (check(TokenType::LBRACKET)) {
			// Array indexing on expression result: expr[index]
			int line = expr->line;
			int column = expr->column;
			advance(); // consume '['
			auto index = parseLogicalOr();
			int endLine = currentLine();
			int endCol = currentColumn();
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");
			expr = std::make_unique<ArrayIndexExprAST>(std::move(expr), std::move(index), line, column);
			expr->setEndPosition(endLine, endCol);
		} else {
			break;
		}
	}

	return expr;
}

std::unique_ptr<ExprAST> Parser::parseFactor() {
	auto expr = parseUnary();

	// Handle type cast: expr as type
	if (checkKeyword("as")) {
		int line = expr->line;
		int column = expr->column;
		advance(); // consume 'as'

		// Parse target type using unified type parser
		std::string targetType = parseTypeString("cast target type");
		int endLine = currentLine();
		int endCol = currentColumn() - 1;

		expr = std::make_unique<CastExprAST>(std::move(expr), targetType, line, column);
		expr->setEndPosition(endLine, endCol);
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
		auto binExpr = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>(op, std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>('&', std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>('^', std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>('|', std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>('A', std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
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
		auto binExpr = std::make_unique<BinaryExprAST>('O', std::move(left), std::move(right), line, column);
		binExpr->setEndPosition(binExpr->right->endLine, binExpr->right->endColumn);
		left = std::move(binExpr);
	}

	return left;
}
