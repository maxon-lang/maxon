#include "../parser.h"
#include <stdexcept>

// Parse a potentially named argument: name = expr or just expr
// Returns a CallArgument with name set if present (named arguments)
CallArgument Parser::parseNamedArgument() {
	// Check for named argument pattern: identifier followed by '='
	// But NOT '==' (comparison) - need to check second token is ASSIGN not EQ
	if (check(TokenType::IDENTIFIER) && peekToken(1).type == TokenType::ASSIGN) {
		std::string argName(currentValue());
		int argLine = currentLine();
		int argCol = currentColumn();
		advance(); // consume name
		advance(); // consume '='
		auto valueExpr = parseNilCoalesce();
		return CallArgument(std::move(valueExpr), argLine, argCol, argName);
	}

	// No name - positional argument
	auto argExpr = parseNilCoalesce();
	int argLine = argExpr->line;
	int argCol = argExpr->column;
	return CallArgument(std::move(argExpr), argLine, argCol);
}
std::unique_ptr<ExprAST> Parser::parsePrimary() {
	if (check(TokenType::NUMBER)) {
		int line = currentLine();
		int column = currentColumn();
		int64_t value;
		try {
			value = std::stoll(std::string(currentValue()));
		} catch (const std::out_of_range &) {
			reportError("Integer literal overflow: value exceeds INT64_MAX (9223372036854775807)", line, column);
		}
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

		// Check if string contains interpolation (unescaped {)
		// Escaped braces are stored as \x01{ and \x01}
		bool hasInterpolation = false;
		for (size_t i = 0; i < value.length(); i++) {
			if (value[i] == '\x01') {
				// Skip escaped character
				i++;
				continue;
			}
			if (value[i] == '{') {
				hasInterpolation = true;
				break;
			}
		}

		if (hasInterpolation) {
			return parseInterpolatedString(value, line, column);
		}

		// Convert escaped braces back to regular braces for plain strings
		std::string cleanValue;
		for (size_t i = 0; i < value.length(); i++) {
			if (value[i] == '\x01' && i + 1 < value.length()) {
				cleanValue += value[i + 1];
				i++; // Skip the escaped char
			} else {
				cleanValue += value[i];
			}
		}

		auto expr = std::make_unique<StringLiteralExprAST>(cleanValue, line, column);
		expr->setEndPosition(line, endCol);
		return expr;
	}

	// Set from array pattern: set from [values] or set from arrayExpr
	// Must be checked before the generic dictionary pattern
	if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "set" && checkKeyword("from", 1)) {
		int line = currentLine();
		int column = currentColumn();
		std::string setTypeName = std::string(currentValue());
		advance(); // consume 'set'

		expectKeywordAdvance("from", "Expected 'from' after 'set'");

		// Parse the array expression (could be array literal or variable)
		auto arrayExpr = parsePrimary();

		int endLine = currentLine();
		int endCol = currentColumn() - 1;

		auto expr = std::make_unique<SetFromExprAST>(setTypeName, std::move(arrayExpr), line, column);
		expr->setEndPosition(endLine, endCol);
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

	// Sized array expression: array of N T (e.g., array of 5 int, array of int, array of count int)
	if (checkKeyword("array")) {
		int line = currentLine();
		int column = currentColumn();
		advance(); // consume 'array'

		if (!checkKeyword("of")) {
			reportError("Expected 'of' after 'array' in array expression\n"
						"  Use: array of int       (for empty array)\n"
						"  Or:  array of 5 int     (for sized array)\n"
						"  Or:  [value1, value2]   (for initialized array)",
						currentLine(), currentColumn());
		}
		advance(); // consume 'of'

		int constSize = 0;
		std::unique_ptr<ExprAST> sizeExpr = nullptr;
		int endLine = currentLine();
		int endCol = currentColumn();

		// Check for sized array: array of N T or array of expr T
		// Parse a size expression if the current token is a number
		if (check(TokenType::NUMBER)) {
			Token sizeToken = currentToken();
			constSize = std::stoi(sizeToken.value);
			advance();
		} else {
			// Check if we have an identifier followed by a type - this means the identifier is the size
			// e.g., "array of count int" where count is a variable
			auto kd = currentKeywordData();
			if (!kd || kd->category != KeywordCategory::Type) {
				// Not a type keyword, could be a size variable or struct type
				if (check(TokenType::IDENTIFIER)) {
					// Save the identifier
					std::string potentialSize = std::string(currentValue());
					int identLine = currentLine();
					int identCol = currentColumn();
					advance();

					// Check if followed by a type keyword or another identifier ON THE SAME LINE
				// If on a different line, the identifier we consumed is the type (e.g., "array of Token\n")
					auto nextKd = currentKeywordData();
					bool nextIsSameLine = (currentLine() == identLine);
					bool nextIsTypeOnSameLine = nextIsSameLine &&
						((nextKd && nextKd->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER));
					if (nextIsTypeOnSameLine) {
						// The first identifier was the size, create a variable reference for it
						sizeExpr = std::make_unique<VariableExprAST>(potentialSize, identLine, identCol);
					} else {
						// The identifier is the type itself, return with constSize=0
						endLine = identLine;
						endCol = identCol + static_cast<int>(potentialSize.length()) - 1;
						auto expr = std::make_unique<SizedArrayExprAST>(constSize, potentialSize, line, column);
						expr->setEndPosition(endLine, endCol);
						return expr;
					}
				}
			}
		}

		// Parse element type
		std::string elementType;
		auto kd = currentKeywordData();
		if (kd && kd->category == KeywordCategory::Type) {
			elementType = std::string(currentValue());
			endLine = currentLine();
			endCol = currentColumn() + static_cast<int>(elementType.length()) - 1;
			advance();
		} else if (check(TokenType::IDENTIFIER)) {
			elementType = parseQualifiedName("array element type");
			endLine = currentLine();
			endCol = currentColumn() - 1;
		} else {
			reportError("Expected element type in array expression\n"
						"  Use: array of int       (for empty array)\n"
						"  Or:  array of 5 int     (for sized array)",
						currentLine(), currentColumn());
		}

		std::unique_ptr<SizedArrayExprAST> expr;
		if (sizeExpr) {
			expr = std::make_unique<SizedArrayExprAST>(std::move(sizeExpr), elementType, line, column);
		} else {
			expr = std::make_unique<SizedArrayExprAST>(constSize, elementType, line, column);
		}
		expr->setEndPosition(endLine, endCol);
		return expr;
	}

	// Array literal: [val1, val2, ...] for value-initialized arrays
	// Map literal: [key1: val1, key2: val2, ...] for key-value maps
	// Note: [size]type and [expr]type syntax is no longer supported
	// Use 'array of N T' syntax instead (e.g., array of 5 int)
	if (check(TokenType::LBRACKET)) {
		int line = currentLine();
		int column = currentColumn();
		advance(); // consume '['

		// Check for empty literal
		if (check(TokenType::RBRACKET)) {
			advance(); // consume ']'
			reportError("Empty array literal [] is not allowed\n"
						"  Use: array of T       (for empty array)\n"
						"  Or:  [value1, value2] (for initialized array)\n"
						"  Or:  map from K to V  (for empty map)",
						line, column);
			// Return a placeholder to continue parsing
			return std::make_unique<ArrayLiteralExprAST>(std::vector<std::unique_ptr<ExprAST>>{}, line, column);
		}

		// Parse the first expression
		auto firstExpr = parseNilCoalesce();

		// Check if this is a map literal (has ':' after first expression)
		if (check(TokenType::COLON)) {
			// This is a map literal: [key: value, ...]
			advance(); // consume ':'
			auto firstValue = parseNilCoalesce();

			std::vector<MapLiteralWithEntriesExprAST::Entry> entries;
			MapLiteralWithEntriesExprAST::Entry firstEntry;
			firstEntry.key = std::move(firstExpr);
			firstEntry.value = std::move(firstValue);
			entries.push_back(std::move(firstEntry));

			// Parse remaining key-value pairs
			while (match(TokenType::COMMA)) {
				auto key = parseNilCoalesce();
				expectAdvance(TokenType::COLON, "Expected ':' after map key");
				auto value = parseNilCoalesce();

				MapLiteralWithEntriesExprAST::Entry entry;
				entry.key = std::move(key);
				entry.value = std::move(value);
				entries.push_back(std::move(entry));
			}

			int endLine = currentLine();
			int endCol = currentColumn();
			expectAdvance(TokenType::RBRACKET, "Expected ']' after map entries");

			auto expr = std::make_unique<MapLiteralWithEntriesExprAST>(std::move(entries), line, column);
			expr->setEndPosition(endLine, endCol);
			return expr;
		}

		// This is an array literal: [val1, val2, ...]
		std::vector<std::unique_ptr<ExprAST>> values;
		values.push_back(std::move(firstExpr));

		while (match(TokenType::COMMA)) {
			values.push_back(parseNilCoalesce());
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
		auto arg = parseNilCoalesce();
		int argLine = arg->line;
		int argCol = arg->column;
		int endLine = currentLine();
		int endCol = currentColumn();
		expectAdvance(TokenType::RPAREN, "Expected ')' after argument");

		std::vector<CallArgument> args;
		args.push_back(CallArgument(std::move(arg), argLine, argCol));
		auto expr = std::make_unique<CallExprAST>(funcName, std::move(args), line, column);
		expr->setEndPosition(endLine, endCol);
		return expr;
	}

	// Handle static method calls on type keywords: int.parse(), float.parse(), etc.
	// Pattern: TypeKeyword.method(args)
	auto typeKd = currentKeywordData();
	if (typeKd && typeKd->category == KeywordCategory::Type && check(TokenType::DOT, 1)) {
		std::string typeName = std::string(currentValue());
		int line = currentLine();
		int column = currentColumn();
		advance(); // consume type keyword (e.g., 'int')
		advance(); // consume '.'

		Token methodToken = expect(TokenType::IDENTIFIER, "Expected method name after '" + typeName + ".'");
		std::string qualifiedName = typeName + "." + methodToken.value;

		expectAdvance(TokenType::LPAREN, "Expected '(' after '" + qualifiedName + "'");

		std::vector<CallArgument> args;
		if (!check(TokenType::RPAREN)) {
			args.push_back(parseNamedArgument());
			while (match(TokenType::COMMA)) {
				args.push_back(parseNamedArgument());
			}
		}

		int endLine = currentLine();
		int endCol = currentColumn();
		expectAdvance(TokenType::RPAREN, "Expected ')' after arguments");

		auto callExpr = std::make_unique<CallExprAST>(qualifiedName, std::move(args), line, column);
		callExpr->setEndPosition(endLine, endCol);
		return callExpr;
	}

	// Allow 'array' keyword as struct name in struct literals (stdlib collection type)
	if (checkKeyword("array") && check(TokenType::LBRACE, 1)) {
		advance(); // consume 'array'
		return parseStructInit("array");
	}

	// Closure/lambda expression with new syntax:
	// Single expression: x gives x * 2
	//
	// Detection: IDENTIFIER followed by 'gives' keyword indicates closure
	// Note: We only support 'gives' for single-parameter closures with inferred type.
	// For multi-statement closures, use parenthesized form: (x int) 'label' ... end 'label'
	// Must check BEFORE general identifier handling
	if (check(TokenType::IDENTIFIER) && checkKeyword("gives", 1)) {
		int line = currentLine();
		int column = currentColumn();

		// Parse single parameter (type inferred)
		Token paramName = currentToken();
		advance(); // consume parameter name
		std::vector<FunctionParameter> params;
		params.push_back(FunctionParameter(paramName.value, "", paramName.line, paramName.column));

		// Single expression form: x gives expr
		advance(); // consume 'gives'
		auto bodyExpr = parseLogicalOr();

		int endLine = bodyExpr->endLine;
		int endCol = bodyExpr->endColumn;

		auto closureExpr = std::make_unique<ClosureExprAST>(
			std::move(params),
			"", // return type inferred
			std::vector<std::unique_ptr<StmtAST>>{},
			std::move(bodyExpr),
			true, // isSingleExpression
			line, column);
		closureExpr->setEndPosition(endLine, endCol);
		return closureExpr;
	}

	// Parenthesized closure parameters: (x int) gives expr or (x int, y int) 'label' ... end 'label'
	// Detection: ( followed by IDENTIFIER and then a type (keyword or identifier)
	// Need to distinguish from regular parenthesized expression
	// Must check BEFORE general parenthesized expression handling
	if (check(TokenType::LPAREN)) {
		// Look ahead to detect closure parameter list
		// Pattern: ( IDENTIFIER TYPE ... ) followed by 'gives' or BLOCK_ID
		// vs regular expression: ( expr )
		bool isClosure = false;
		int lookahead = 1;

		if (check(TokenType::IDENTIFIER, 1)) {
			// Check if followed by a type (closure param) or operator (expression)
			auto kd2 = peekToken(2);
			// Type keywords or identifiers followed by comma/rparen indicate closure params
			if (kd2.type == TokenType::KEYWORD || kd2.type == TokenType::IDENTIFIER) {
				// Scan to find matching ')' and check what follows
				int parenDepth = 1;
				lookahead = 1;
				while (parenDepth > 0 && lookahead < 50) { // safety limit
					Token tok = peekToken(lookahead);
					if (tok.type == TokenType::LPAREN)
						parenDepth++;
					else if (tok.type == TokenType::RPAREN)
						parenDepth--;
					else if (tok.type == TokenType::END_OF_FILE)
						break;
					lookahead++;
				}
				// lookahead now points past the closing ')'
				// Check if followed by 'gives' keyword or BLOCK_ID
				Token afterParen = peekToken(lookahead);
				if (afterParen.type == TokenType::BLOCK_ID) {
					isClosure = true;
				} else if (afterParen.type == TokenType::KEYWORD && afterParen.value == "gives") {
					isClosure = true;
				}
			}
		}

		if (isClosure) {
			int line = currentLine();
			int column = currentColumn();
			advance(); // consume '('

			std::vector<FunctionParameter> params;
			if (!check(TokenType::RPAREN)) {
				do {
					Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name in closure");
					std::string paramType = parseTypeString("closure parameter type");
					params.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
				} while (match(TokenType::COMMA));
			}

			expectAdvance(TokenType::RPAREN, "Expected ')' after closure parameters");

			if (checkKeyword("gives")) {
				// Single expression form: (x int) gives expr
				advance(); // consume 'gives'
				auto bodyExpr = parseLogicalOr();

				int endLine = bodyExpr->endLine;
				int endCol = bodyExpr->endColumn;

				auto closureExpr = std::make_unique<ClosureExprAST>(
					std::move(params),
					"", // return type inferred
					std::vector<std::unique_ptr<StmtAST>>{},
					std::move(bodyExpr),
					true, // isSingleExpression
					line, column);
				closureExpr->setEndPosition(endLine, endCol);
				return closureExpr;
			} else {
				// Multi-statement form: (x int, y int) 'label' ... end 'label'
				Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier or 'gives' after closure parameters");
				std::string blockId = blockIdToken.value;

				std::vector<std::unique_ptr<StmtAST>> body;
				while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
					body.push_back(parseStatementWithRecovery());
				}

				expectKeywordAdvance("end", "Expected 'end' to close closure block");
				Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
				if (endBlockIdToken.value != blockId) {
					reportError("Block identifier mismatch in closure\n  Expected: '" + blockId +
									"'\n  Found: '" + endBlockIdToken.value + "'",
								endBlockIdToken.line, endBlockIdToken.column);
				}

				int endLine = endBlockIdToken.line;
				int endCol = endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1;

				auto closureExpr = std::make_unique<ClosureExprAST>(
					std::move(params),
					"", // return type inferred
					std::move(body),
					nullptr, // no single expression
					false,	 // not single expression
					line, column,
					blockId);
				closureExpr->setEndPosition(endLine, endCol);
				return closureExpr;
			}
		}
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
						std::vector<CallArgument> args;

						// First argument is the object itself (self - implicit, no label)
						auto varExpr = std::make_unique<VariableExprAST>(name, line, column);
						varExpr->setEndPosition(line, column + static_cast<int>(name.length()) - 1);
						args.push_back(CallArgument(std::move(varExpr), line, column));

						// Parse remaining arguments (may be labeled: name: value)
						if (!check(TokenType::RPAREN)) {
							args.push_back(parseNamedArgument());

							while (match(TokenType::COMMA)) {
								args.push_back(parseNamedArgument());
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
				std::vector<CallArgument> args;

				// Parse arguments (may be labeled: name: value)
				if (!check(TokenType::RPAREN)) {
					args.push_back(parseNamedArgument());

					while (match(TokenType::COMMA)) {
						args.push_back(parseNamedArgument());
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

				// Handle chained member access and method calls (e.g., self.data.length, config.sources.count())
				while (check(TokenType::DOT)) {
					advance(); // consume '.'
					Token nextMember = expect(TokenType::IDENTIFIER, "Expected member name after '.'");

					// Check if this is a method call: expr.field.method()
					if (check(TokenType::LPAREN)) {
						advance(); // consume '('
						std::vector<CallArgument> args;

						// First argument is the current member expression (implicit self)
						args.push_back(CallArgument(std::move(memberExpr), line, column));

						// Parse remaining arguments
						if (!check(TokenType::RPAREN)) {
							args.push_back(parseNamedArgument());
							while (match(TokenType::COMMA)) {
								args.push_back(parseNamedArgument());
							}
						}

						int endLine = currentLine();
						int endCol = currentColumn();
						expectAdvance(TokenType::RPAREN, "Expected ')' after method arguments");
						auto callExpr = std::make_unique<CallExprAST>(nextMember.value, std::move(args), line, column);
						callExpr->setEndPosition(endLine, endCol);
						return callExpr;
					}

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
			std::vector<CallArgument> args;

			// Parse arguments (may be labeled: name: value)
			if (!check(TokenType::RPAREN)) {
				args.push_back(parseNamedArgument());

				while (match(TokenType::COMMA)) {
					args.push_back(parseNamedArgument());
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
					std::vector<CallArgument> args;

					// First argument is the array element (implicit self)
					args.push_back(CallArgument(std::move(arrayExpr), line, column));

					// Parse remaining arguments (may be labeled: name: value)
					if (!check(TokenType::RPAREN)) {
						args.push_back(parseNamedArgument());

						while (match(TokenType::COMMA)) {
							args.push_back(parseNamedArgument());
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
				std::vector<CallArgument> args;

				// First argument is the expression itself (implicit self)
				args.push_back(CallArgument(std::move(expr), line, column));

				// Parse remaining arguments (may be labeled: name: value)
				if (!check(TokenType::RPAREN)) {
					args.push_back(parseNamedArgument());

					while (match(TokenType::COMMA)) {
						args.push_back(parseNamedArgument());
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
		// Peek ahead: if next token after 'or' is a BLOCK_ID, this is guard-let, not logical or
		// Let the statement parser handle that case
		if (check(TokenType::BLOCK_ID, 1)) {
			break;
		}
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

// Nil coalescing: lowest precedence expression operator
// Syntax: optionalExpr or defaultExpr
// Returns the unwrapped value if optional has a value, otherwise the default
std::unique_ptr<ExprAST> Parser::parseNilCoalesce() {
	auto left = parseLogicalOr();

	// Check for 'or' followed by non-BLOCK_ID (nil coalescing)
	// If followed by BLOCK_ID, it's guard-let syntax handled by statement parser
	if (checkKeyword("or") && !check(TokenType::BLOCK_ID, 1)) {
		int line = currentLine();
		int column = currentColumn();
		advance();					   // consume 'or'
		auto right = parseLogicalOr(); // Parse default value (no chaining - parseLogicalOr not parseNilCoalesce)
		auto coalesceExpr = std::make_unique<OrCoalesceExprAST>(std::move(left), std::move(right), line, column);
		coalesceExpr->setEndPosition(coalesceExpr->defaultExpr->endLine, coalesceExpr->defaultExpr->endColumn);
		return coalesceExpr;
	}

	return left;
}

// Parse an interpolated string like "Hello {name}!" or "Value: {x:format}"
// The string has already been consumed from the token stream
// str contains the raw string content with interpolation markers
std::unique_ptr<ExprAST> Parser::parseInterpolatedString(const std::string &str, int line, int column) {
	auto result = std::make_unique<InterpolatedStringExprAST>(line, column);

	size_t pos = 0;
	while (pos < str.length()) {
		// Find next unescaped { (skip \x01{ which is escaped brace)
		size_t braceStart = std::string::npos;
		for (size_t i = pos; i < str.length(); i++) {
			if (str[i] == '\x01') {
				// Skip escaped character
				i++;
				continue;
			}
			if (str[i] == '{') {
				braceStart = i;
				break;
			}
		}

		if (braceStart == std::string::npos) {
			// No more interpolations - add remaining literal
			std::string literal;
			for (size_t i = pos; i < str.length(); i++) {
				if (str[i] == '\x01' && i + 1 < str.length()) {
					literal += str[i + 1];
					i++; // Skip escaped char
				} else {
					literal += str[i];
				}
			}
			if (!literal.empty()) {
				InterpolatedStringPart part;
				part.isExpression = false;
				part.literalValue = literal;
				result->parts.push_back(std::move(part));
			}
			break;
		}

		// Add literal part before the {
		if (braceStart > pos) {
			std::string literal;
			for (size_t i = pos; i < braceStart; i++) {
				if (str[i] == '\x01' && i + 1 < str.length()) {
					literal += str[i + 1];
					i++; // Skip escaped char
				} else {
					literal += str[i];
				}
			}
			InterpolatedStringPart part;
			part.isExpression = false;
			part.literalValue = literal;
			result->parts.push_back(std::move(part));
		}

		// Find matching } while tracking nesting depth
		size_t braceEnd = std::string::npos;
		int depth = 1;
		bool inString = false;
		size_t formatSpecStart = std::string::npos;

		for (size_t i = braceStart + 1; i < str.length() && depth > 0; i++) {
			char c = str[i];

			// Handle string literals inside the expression
			if (c == '"' && (i == 0 || str[i - 1] != '\\')) {
				inString = !inString;
				continue;
			}

			if (inString)
				continue;

			if (c == '{' || c == '(' || c == '[') {
				depth++;
			} else if (c == '}') {
				depth--;
				if (depth == 0) {
					braceEnd = i;
				}
			} else if (c == ')' || c == ']') {
				depth--;
			} else if (c == ':' && depth == 1 && formatSpecStart == std::string::npos) {
				// Format specifier starts here (first : at depth 1)
				formatSpecStart = i;
			}
		}

		if (braceEnd == std::string::npos) {
			reportError("Unterminated string interpolation: missing '}'\n"
						"  Hint: Make sure all { have matching }",
						line, column);
		}

		// Extract expression and optional format spec
		std::string exprStr;
		std::string formatSpec;

		if (formatSpecStart != std::string::npos) {
			exprStr = str.substr(braceStart + 1, formatSpecStart - braceStart - 1);
			formatSpec = str.substr(formatSpecStart + 1, braceEnd - formatSpecStart - 1);
		} else {
			exprStr = str.substr(braceStart + 1, braceEnd - braceStart - 1);
		}

		// Parse the expression string using a sub-lexer and sub-parser
		Lexer exprLexer(exprStr);
		auto exprTokens = exprLexer.tokenize();

		// Remove EOF token from the end for sub-parsing
		if (!exprTokens.empty() && exprTokens.back().type == TokenType::END_OF_FILE) {
			exprTokens.pop_back();
		}

		// Add back EOF for the sub-parser
		exprTokens.push_back(Token(TokenType::END_OF_FILE, "", 1, 1));

		Parser exprParser(exprTokens);
		auto expr = exprParser.parseNilCoalesce();

		if (exprParser.hasErrors()) {
			reportError("Invalid expression in string interpolation: " + exprStr,
						line, column);
		}

		// Fix up the expression's line/column to reflect actual source position
		// The expression is inside the string at position braceStart+1 (after the {)
		// Column offset: column (start of string) + 1 (opening quote) + braceStart + 1 (opening brace)
		if (expr) {
			expr->line = line;
			expr->column = column + 1 + static_cast<int>(braceStart) + 1;
		}

		InterpolatedStringPart part;
		part.isExpression = true;
		part.expr = std::move(expr);
		part.formatSpec = formatSpec;
		result->parts.push_back(std::move(part));

		pos = braceEnd + 1;
	}

	result->setEndPosition(line, column + static_cast<int>(str.length()) + 1);
	return result;
}
