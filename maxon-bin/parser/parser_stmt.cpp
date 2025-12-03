#include "../parser.h"
#include <stdexcept>

std::unique_ptr<StmtAST> Parser::parseVarDecl() {
	Token varToken = expectKeyword("var", "Expected 'var'");
	Token name = expect(TokenType::IDENTIFIER, "Expected variable name");

	// Type is always inferred from initializer
	std::string type = "";

	expectAdvance(TokenType::ASSIGN, "Expected '='");
	auto initializer = parseLogicalOr();

	// Check for else-unwrap
	if (checkKeyword("else")) {
		return parseElseUnwrap(varToken, name, type, std::move(initializer));
	}

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

std::unique_ptr<StmtAST> Parser::parseIf() {
	Token ifToken = expectKeyword("if", "Expected 'if'");

	// Check for "if let" pattern
	if (checkKeyword("let")) {
		return parseIfLet(ifToken);
	}

	auto condition = parseLogicalOr();

	// Require block identifier (no more 'then' keyword support)
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'if' condition (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;

	// Parse then body
	while (!checkKeyword("else") && !checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		thenBody.push_back(parseStatement());
	}

	// Parse optional else
	if (checkKeyword("else")) {
		match(TokenType::KEYWORD); // consume "else"
		// Require same block identifier after else
		Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else' (must match the 'if' block identifier)");
		if (elseBlockIdToken.value != blockId) {
			reportError("Block identifier mismatch in if-else statement\n  Expected: '" + blockId +
						"'\n  Found: '" + elseBlockIdToken.value +
						"'\n  Note: The 'else' block identifier must match the 'if' block identifier",
						elseBlockIdToken.line, elseBlockIdToken.column);
		}

		// Parse else body
		while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
			elseBody.push_back(parseStatement());
		}
	}

	expectKeywordAdvance("end", "Expected 'end' to close if block");

	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the opening block identifier)");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in if statement\n  Expected: '" + blockId +
					"'\n  Found: '" + endBlockIdToken.value +
					"'\n  Note: The 'end' block identifier must match the opening 'if' block identifier",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	return std::make_unique<IfStmtAST>(std::move(condition),
									   std::move(thenBody),
									   std::move(elseBody),
									   ifToken.line, ifToken.column, blockId);
}

std::unique_ptr<IfLetStmtAST> Parser::parseIfLet(Token ifToken) {
	expectKeywordAdvance("let", "Expected 'let'");

	Token bindingName = expect(TokenType::IDENTIFIER, "Expected variable name after 'if let'");
	expectAdvance(TokenType::ASSIGN, "Expected '=' after variable name in 'if let'");

	auto optionalExpr = parseLogicalOr();

	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'if let' expression");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;

	// Parse then body (value is present)
	while (!checkKeyword("else") && !checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		thenBody.push_back(parseStatement());
	}

	// Parse optional else (nil case)
	if (checkKeyword("else")) {
		advance(); // consume "else"
		Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else'");
		if (elseBlockIdToken.value != blockId) {
			reportError("Block identifier mismatch in if-let statement\n  Expected: '" + blockId +
						"'\n  Found: '" + elseBlockIdToken.value + "'",
						elseBlockIdToken.line, elseBlockIdToken.column);
		}

		while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
			elseBody.push_back(parseStatement());
		}
	}

	expectKeywordAdvance("end", "Expected 'end' to close if-let block");
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in if-let statement\n  Expected: '" + blockId +
					"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	return std::make_unique<IfLetStmtAST>(bindingName.value, std::move(optionalExpr),
										   std::move(thenBody), std::move(elseBody),
										   ifToken.line, ifToken.column, blockId);
}

std::unique_ptr<ElseUnwrapStmtAST> Parser::parseElseUnwrap(Token varToken, Token nameToken,
															 const std::string &explicitType,
															 std::unique_ptr<ExprAST> optionalExpr) {
	expectKeywordAdvance("else", "Expected 'else'");
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else'");
	std::string blockId = blockIdToken.value;

	// Parse else body
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		elseBody.push_back(parseStatement());
	}

	expectKeywordAdvance("end", "Expected 'end' to close else block");
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in else-unwrap statement\n  Expected: '" + blockId +
					"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	return std::make_unique<ElseUnwrapStmtAST>(nameToken.value, explicitType,
												 std::move(optionalExpr),
												 std::move(elseBody),
												 varToken.line, varToken.column,
												 blockId);
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
		reportError("Block identifier mismatch in while loop\n  Expected: '" + blockId +
					"'\n  Found: '" + endBlockIdToken.value +
					"'\n  Note: The 'end' block identifier must match the 'while' block identifier",
					endBlockIdToken.line, endBlockIdToken.column);
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
		reportError("Block identifier mismatch in for loop\n  Expected: '" + blockId +
					"'\n  Found: '" + endBlockIdToken.value +
					"'\n  Note: The 'end' block identifier must match the 'for' block identifier",
					endBlockIdToken.line, endBlockIdToken.column);
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

		reportError("Unexpected identifier '" + name +
					"'\n  Note: Did you forget an assignment (=), function call (), or keyword?",
					idLine, idColumn);
	}

	std::string foundStr;
	if (currentType() == TokenType::END_OF_FILE) {
		foundStr = "end of file";
	} else {
		foundStr = "'" + std::string(currentValue()) + "'";
	}

	reportError("Unexpected token: " + foundStr +
				"\n  Note: Expected a statement (var, let, if, while, return, break, continue, or assignment)",
				currentLine(), currentColumn());
}
