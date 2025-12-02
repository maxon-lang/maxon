#include "../parser.h"
#include <stdexcept>

std::unique_ptr<VarDeclStmtAST> Parser::parseVarDecl() {
	Token varToken = expectKeyword("var", "Expected 'var'");
	auto [name, type, initializer] = parseVariableDeclarationComponents();
	return std::make_unique<VarDeclStmtAST>(name.value, std::move(initializer), type, varToken.line, varToken.column);
}

std::unique_ptr<LetDeclStmtAST> Parser::parseLetDecl() {
	Token letToken = expectKeyword("let", "Expected 'let'");
	auto [name, type, initializer] = parseVariableDeclarationComponents();
	return std::make_unique<LetDeclStmtAST>(name.value, std::move(initializer), type, letToken.line, letToken.column);
}

std::tuple<Token, std::string, std::unique_ptr<ExprAST>> Parser::parseVariableDeclarationComponents() {
	Token name = expect(TokenType::IDENTIFIER, "Expected variable name");

	// Type is always inferred from initializer
	std::string type = "";

	expectAdvance(TokenType::ASSIGN, "Expected '='");
	auto initializer = parseLogicalOr();

	return {name, type, std::move(initializer)};
}

std::unique_ptr<AssignStmtAST> Parser::parseAssignment(const std::string &name) {
	Token assignToken = expect(TokenType::ASSIGN, "Expected '='");
	auto value = parseLogicalOr();
	return std::make_unique<AssignStmtAST>(name, std::move(value), assignToken.line, assignToken.column);
}

std::unique_ptr<ReturnStmtAST> Parser::parseReturn() {
	Token returnToken = expectKeyword("return", "Expected 'return'");

	// Check if this is a bare return (for void functions)
	// A bare return is followed by 'end', another statement keyword, or EOF
	std::unique_ptr<ExprAST> value = nullptr;
	if (!checkKeyword("end") && !checkKeyword("if") && !checkKeyword("while") &&
		!checkKeyword("for") && !checkKeyword("var") && !checkKeyword("let") &&
		!checkKeyword("return") && !checkKeyword("break") && !checkKeyword("continue") &&
		!check(TokenType::END_OF_FILE)) {
		value = parseLogicalOr();
	}

	return std::make_unique<ReturnStmtAST>(std::move(value), returnToken.line, returnToken.column);
}

std::unique_ptr<BreakStmtAST> Parser::parseBreak() {
	Token breakToken = expectKeyword("break", "Expected 'break'");

	// Check for optional label
	std::string label = "";
	if (check(TokenType::BLOCK_ID)) {
		Token labelToken = expect(TokenType::BLOCK_ID, "Expected block identifier");
		label = labelToken.value;
	}

	return std::make_unique<BreakStmtAST>(breakToken.line, breakToken.column, label);
}

std::unique_ptr<ContinueStmtAST> Parser::parseContinue() {
	Token continueToken = expectKeyword("continue", "Expected 'continue'");

	// Check for optional label
	std::string label = "";
	if (check(TokenType::BLOCK_ID)) {
		Token labelToken = expect(TokenType::BLOCK_ID, "Expected block identifier");
		label = labelToken.value;
	}

	return std::make_unique<ContinueStmtAST>(continueToken.line, continueToken.column, label);
}

std::unique_ptr<IfStmtAST> Parser::parseIf() {
	Token ifToken = expectKeyword("if", "Expected 'if'");
	auto condition = parseLogicalOr();

	// Check if this is a single-line if statement (uses 'then' keyword)
	// Single-line if: if <condition> then <statement>
	// Multi-line if: if <condition> 'blockId' ... end 'blockId'
	bool isSingleLine = false;
	std::string blockId;

	if (checkKeyword("then")) {
		// Single-line if
		isSingleLine = true;
		advance(); // consume 'then'
	} else {
		// Multi-line if requires block identifier
		Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'if' condition (use 'id' where id is any string)");
		blockId = blockIdToken.value;
	}

	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;

	if (isSingleLine) {
		// Single-line if: parse one statement after 'then'
		thenBody.push_back(parseStatement());

		// Check for else
		if (checkKeyword("else")) {
			advance(); // consume 'else'

			// Check if this is multi-line else (has block identifier) or single-line else
			if (check(TokenType::BLOCK_ID)) {
				// Multi-line else: else 'blockid' ... end 'blockid'
				Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else'");
				blockId = elseBlockIdToken.value;

				// Check if statement follows on same line (single-line else with block id)
				// or if statements are on following lines (multi-line else)
				Token nextToken = currentToken();
				if (nextToken.line == elseBlockIdToken.line && !checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
					// Single-line else: else 'id' <statement>
					elseBody.push_back(parseStatement());
				} else {
					// Multi-line else: else 'id' <newline> <statements> end 'id'
					while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
						elseBody.push_back(parseStatement());
					}

					expectKeywordAdvance("end", "Expected 'end' to close else block");
					Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
					if (endBlockIdToken.value != blockId) {
						throw std::runtime_error("Block identifier mismatch in else block" +
												 std::string("\n  Expected: '") + blockId + "'" +
												 "\n  Found: '" + endBlockIdToken.value + "'");
					}
				}
			} else {
				// Single-line else: else <statement>
				elseBody.push_back(parseStatement());
			}
		}

		return std::make_unique<IfStmtAST>(std::move(condition),
										   std::move(thenBody),
										   std::move(elseBody),
										   ifToken.line, ifToken.column, blockId);
	}

	// Multi-line if

	// Parse then body
	while (!checkKeyword("else") && !checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		thenBody.push_back(parseStatement());
	}

	// Parse optional else
	bool singleLineElse = false;
	if (checkKeyword("else")) {
		Token elseToken = currentToken();
		match(TokenType::KEYWORD); // consume "else"
		// Require same block identifier after else
		Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else' (must match the 'if' block identifier)");
		if (elseBlockIdToken.value != blockId) {
			throw std::runtime_error("Block identifier mismatch in if-else statement" +
									 std::string("\n  Expected: '") + blockId + "'" +
									 "\n  Found: '" + elseBlockIdToken.value + "'" +
									 "\n  Location: line " + std::to_string(elseBlockIdToken.line) +
									 ", column " + std::to_string(elseBlockIdToken.column) +
									 "\n  Note: The 'else' block identifier must match the 'if' block identifier");
		}

		// Check if this is a single-line else (statement on same line as else 'id')
		// or multi-line else (statements on following lines, ended with 'end')
		if (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
			// Check if next token is on the same line as the else block id
			Token nextToken = currentToken();
			if (nextToken.line == elseBlockIdToken.line) {
				// Single-line else: else 'id' <statement>
				// Parse one statement and we're done (no 'end' needed)
				singleLineElse = true;
				elseBody.push_back(parseStatement());
			} else {
				// Multi-line else: else 'id' <newline> <statements> end 'id'
				while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
					elseBody.push_back(parseStatement());
				}
			}
		}
	}

	if (!singleLineElse) {
		expectKeywordAdvance("end", "Expected 'end' to close if block");

		// Require matching block identifier after end
		Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the opening block identifier)");
		if (endBlockIdToken.value != blockId) {
			throw std::runtime_error("Block identifier mismatch in if statement" +
									 std::string("\n  Expected: '") + blockId + "'" +
									 "\n  Found: '" + endBlockIdToken.value + "'" +
									 "\n  Location: line " + std::to_string(endBlockIdToken.line) +
									 ", column " + std::to_string(endBlockIdToken.column) +
									 "\n  Note: The 'end' block identifier must match the opening 'if' block identifier");
		}
	}

	return std::make_unique<IfStmtAST>(std::move(condition),
									   std::move(thenBody),
									   std::move(elseBody),
									   ifToken.line, ifToken.column, blockId);
}
std::unique_ptr<WhileStmtAST> Parser::parseWhile() {
	Token whileToken = expectKeyword("while", "Expected 'while'");
	auto condition = parseLogicalOr();

	// Require block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'while' condition (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> body;

	// Parse body
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatement());
	}

	expectKeywordAdvance("end", "Expected 'end' to close while loop");

	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the opening block identifier)");
	if (endBlockIdToken.value != blockId) {
		throw std::runtime_error("Block identifier mismatch in while loop" +
								 std::string("\n  Expected: '") + blockId + "'" +
								 "\n  Found: '" + endBlockIdToken.value + "'" +
								 "\n  Location: line " + std::to_string(endBlockIdToken.line) +
								 ", column " + std::to_string(endBlockIdToken.column) +
								 "\n  Note: The 'end' block identifier must match the 'while' block identifier");
	}

	return std::make_unique<WhileStmtAST>(std::move(condition), std::move(body), whileToken.line, whileToken.column, blockId);
}

std::unique_ptr<ForStmtAST> Parser::parseFor() {
	Token forToken = expectKeyword("for", "Expected 'for'");

	// Parse loop variable name
	Token varToken = expect(TokenType::IDENTIFIER, "Expected loop variable name after 'for'");
	std::string loopVar = varToken.value;

	// Expect 'in' keyword
	expectKeywordAdvance("in", "Expected 'in' after loop variable");

	// Parse iterable expression (range call, array variable, etc.)
	auto iterable = parseLogicalOr();

	// Require block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'for' iterable (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> body;

	// Parse body
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatement());
	}

	expectKeywordAdvance("end", "Expected 'end' to close for loop");

	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the opening block identifier)");
	if (endBlockIdToken.value != blockId) {
		throw std::runtime_error("Block identifier mismatch in for loop" +
								 std::string("\n  Expected: '") + blockId + "'" +
								 "\n  Found: '" + endBlockIdToken.value + "'" +
								 "\n  Location: line " + std::to_string(endBlockIdToken.line) +
								 ", column " + std::to_string(endBlockIdToken.column) +
								 "\n  Note: The 'end' block identifier must match the 'for' block identifier");
	}

	return std::make_unique<ForStmtAST>(loopVar, std::move(iterable), std::move(body), forToken.line, forToken.column, blockId);
}

std::unique_ptr<StmtAST> Parser::parseStatement() {
	if (checkKeyword("var")) {
		return parseVarDecl();
	}

	if (checkKeyword("let")) {
		return parseLetDecl();
	}

	if (checkKeyword("if")) {
		return parseIf();
	}

	if (checkKeyword("while")) {
		return parseWhile();
	}

	if (checkKeyword("for")) {
		return parseFor();
	}

	if (checkKeyword("return")) {
		return parseReturn();
	}

	if (checkKeyword("break")) {
		return parseBreak();
	}

	if (checkKeyword("continue")) {
		return parseContinue();
	}

	if (check(TokenType::IDENTIFIER)) {
		std::string name = std::string(currentValue());
		int idLine = currentLine();
		int idColumn = currentColumn();
		advance();

		// Check for member access (struct.field = value) or namespace qualification or method call
		if (check(TokenType::DOT)) {
			advance(); // consume '.'
			Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");

			// Check for array index on member (struct.field[i] = value)
			if (check(TokenType::LBRACKET)) {
				advance(); // consume '['
				auto index = parseLogicalOr();
				expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");
				expectAdvance(TokenType::ASSIGN, "Expected '=' in array member assignment");
				auto value = parseLogicalOr();
				return std::make_unique<MemberArrayAssignStmtAST>(name, memberName.value, std::move(index), std::move(value), idLine, idColumn);
			}

			// If followed by assignment, this is struct member assignment
			if (check(TokenType::ASSIGN)) {
				advance(); // consume '='
				auto value = parseLogicalOr();
				return std::make_unique<MemberAssignStmtAST>(name, memberName.value, std::move(value), idLine, idColumn);
			}

			// Check for method call: arr.push(5) -> push(arr, 5)
			// This is method call if name is a simple identifier (no dots) and NOT a known type
			// Special case: 'string' is a type name even though it's lowercase
			if (check(TokenType::LPAREN) && name.find('.') == std::string::npos && name != "string") {
				std::string methodName = memberName.value;
				advance(); // consume '('

				std::vector<std::unique_ptr<ExprAST>> args;
				// First argument is the object itself
				args.push_back(std::make_unique<VariableExprAST>(name, idLine, idColumn));

				// Parse remaining arguments
				if (!check(TokenType::RPAREN)) {
					args.push_back(parseLogicalOr());
					while (match(TokenType::COMMA)) {
						args.push_back(parseLogicalOr());
					}
				}

				expectAdvance(TokenType::RPAREN, "Expected ')' after method arguments");
				auto callExpr = std::make_unique<CallExprAST>(methodName, std::move(args), idLine, idColumn);
				return std::make_unique<ExprStmtAST>(std::move(callExpr), idLine, idColumn);
			}

			// Otherwise, continue treating as namespace qualification for function calls
			name = name + "." + memberName.value;
		}

		// Check for array indexing assignment: array[index] = value or array[index].member = value
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['
			auto index = parseLogicalOr();
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");

			// Check for member access on array element
			if (check(TokenType::DOT)) {
				advance(); // consume '.'
				Token memberName = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
				expectAdvance(TokenType::ASSIGN, "Expected '=' in member assignment");
				auto value = parseLogicalOr();
				// Create ArrayMemberAssignStmtAST for arr[i].field = value
				return std::make_unique<ArrayMemberAssignStmtAST>(name, std::move(index), memberName.value, std::move(value), idLine, idColumn);
			}

			expectAdvance(TokenType::ASSIGN, "Expected '=' in array assignment");
			auto value = parseLogicalOr();
			return std::make_unique<ArrayAssignStmtAST>(name, std::move(index), std::move(value), idLine, idColumn);
		}

		if (check(TokenType::ASSIGN)) {
			return parseAssignment(name);
		}

		if (check(TokenType::LPAREN)) {
			// Parse function call as statement
			advance(); // consume '('

			std::vector<std::unique_ptr<ExprAST>> args;
			if (!check(TokenType::RPAREN)) {
				do {
					args.push_back(parseLogicalOr());
				} while (match(TokenType::COMMA));
			}

			expectAdvance(TokenType::RPAREN, "Expected ')' after function arguments");

			auto callExpr = std::make_unique<CallExprAST>(name, std::move(args), idLine, idColumn);
			return std::make_unique<ExprStmtAST>(std::move(callExpr), idLine, idColumn);
		}

		throw std::runtime_error("Unexpected identifier '" + name + "'" +
								 std::string("\n  Location: line ") + std::to_string(idLine) +
								 ", column " + std::to_string(idColumn) +
								 "\n  Note: Did you forget an assignment (=), function call (), or keyword?");
	}

	std::string foundStr;
	if (currentType() == TokenType::END_OF_FILE) {
		foundStr = "end of file";
	} else {
		foundStr = "'" + std::string(currentValue()) + "'";
	}

	throw std::runtime_error("Unexpected token: " + foundStr +
							 "\n  Location: line " + std::to_string(currentLine()) +
							 ", column " + std::to_string(currentColumn()) +
							 "\n  Note: Expected a statement (var, let, if, while, return, break, continue, or assignment)");
}
