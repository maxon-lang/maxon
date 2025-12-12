#include "../parser.h"
#include <stdexcept>

std::unique_ptr<GlobalLetDeclAST> Parser::parseTopLevelLet(bool isExported) {
	// Handle optional 'export' keyword
	if (isExported) {
		expectKeywordAdvance("export", "Expected 'export'");
	}

	Token letToken = expectKeyword("let", "Expected 'let'");
	Token name = expect(TokenType::IDENTIFIER, "Expected constant name");

	// Type is inferred from initializer
	std::string type = "";

	expectAdvance(TokenType::ASSIGN, "Expected '='");
	auto initializer = parseLogicalOr();

	return std::make_unique<GlobalLetDeclAST>(name.value, std::move(initializer), type, isExported,
											  letToken.line, letToken.column);
}

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

	// Require block identifier for the if/then branch
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'if' condition (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;
	std::string elseBlockId;

	// Parse then body until 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		thenBody.push_back(parseStatementWithRecovery());
	}

	expectKeywordAdvance("end", "Expected 'end' to close if block");

	// Require matching block identifier after 'end'
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the 'if' block identifier)");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in if statement\n  Expected: '" + blockId +
						"'\n  Found: '" + endBlockIdToken.value +
						"'\n  Note: The 'end' block identifier must match the opening 'if' block identifier",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	int endPositionLine = endBlockIdToken.line;
	int endPositionColumn = endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1;

	// Check for 'else' on the SAME LINE as the end block identifier
	// New syntax: end 'blockId' else 'elseBlockId' OR end 'blockId' else if ...
	if (currentLine() == endBlockIdToken.line && checkKeyword("else")) {
		advance(); // consume 'else'

		// Check for 'else if' (else-if chain)
		if (checkKeyword("if")) {
			// Recursively parse the else-if as a nested if statement
			auto elseIfStmt = parseIf();
			elseBody.push_back(std::move(elseIfStmt));
			// Update end position from the nested if
			if (!elseBody.empty()) {
				auto &lastStmt = elseBody.back();
				endPositionLine = lastStmt->endLine;
				endPositionColumn = lastStmt->endColumn;
			}
		} else {
			// Regular else branch: else 'elseBlockId' ... end 'elseBlockId'
			Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else'");
			elseBlockId = elseBlockIdToken.value;

			// Parse else body until 'end'
			while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
				elseBody.push_back(parseStatementWithRecovery());
			}

			expectKeywordAdvance("end", "Expected 'end' to close else block");

			// Require matching block identifier after 'end'
			Token elseEndBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match the 'else' block identifier)");
			if (elseEndBlockIdToken.value != elseBlockId) {
				reportError("Block identifier mismatch in else block\n  Expected: '" + elseBlockId +
								"'\n  Found: '" + elseEndBlockIdToken.value +
								"'\n  Note: The 'end' block identifier must match the 'else' block identifier",
							elseEndBlockIdToken.line, elseEndBlockIdToken.column);
			}

			endPositionLine = elseEndBlockIdToken.line;
			endPositionColumn = elseEndBlockIdToken.column + static_cast<int>(elseEndBlockIdToken.value.length()) - 1;
		}
	}

	auto stmt = std::make_unique<IfStmtAST>(std::move(condition),
											std::move(thenBody),
											std::move(elseBody),
											ifToken.line, ifToken.column, blockId, elseBlockId);
	stmt->setEndPosition(endPositionLine, endPositionColumn);
	return stmt;
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
	std::string elseBlockId;

	// Parse then body until 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		thenBody.push_back(parseStatementWithRecovery());
	}

	expectKeywordAdvance("end", "Expected 'end' to close if-let block");
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in if-let statement\n  Expected: '" + blockId +
						"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	int endPositionLine = endBlockIdToken.line;
	int endPositionColumn = endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1;

	// Check for 'else' on the SAME LINE as the end block identifier
	// New syntax: end 'blockId' else 'elseBlockId' ... end 'elseBlockId'
	if (currentLine() == endBlockIdToken.line && checkKeyword("else")) {
		advance(); // consume 'else'

		Token elseBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'else'");
		elseBlockId = elseBlockIdToken.value;

		// Parse else body until 'end'
		while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
			elseBody.push_back(parseStatementWithRecovery());
		}

		expectKeywordAdvance("end", "Expected 'end' to close else block");

		Token elseEndBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
		if (elseEndBlockIdToken.value != elseBlockId) {
			reportError("Block identifier mismatch in if-let else block\n  Expected: '" + elseBlockId +
							"'\n  Found: '" + elseEndBlockIdToken.value + "'",
						elseEndBlockIdToken.line, elseEndBlockIdToken.column);
		}

		endPositionLine = elseEndBlockIdToken.line;
		endPositionColumn = elseEndBlockIdToken.column + static_cast<int>(elseEndBlockIdToken.value.length()) - 1;
	}

	auto stmt = std::make_unique<IfLetStmtAST>(bindingName.value, std::move(optionalExpr),
											   std::move(thenBody), std::move(elseBody),
											   ifToken.line, ifToken.column, blockId, elseBlockId);
	stmt->setEndPosition(endPositionLine, endPositionColumn);
	return stmt;
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
		elseBody.push_back(parseStatementWithRecovery());
	}

	expectKeywordAdvance("end", "Expected 'end' to close else block");
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in else-unwrap statement\n  Expected: '" + blockId +
						"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	auto stmt = std::make_unique<ElseUnwrapStmtAST>(nameToken.value, explicitType,
													std::move(optionalExpr),
													std::move(elseBody),
													varToken.line, varToken.column,
													blockId);
	// Set end position to the closing block identifier
	stmt->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return stmt;
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
		body.push_back(parseStatementWithRecovery());
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

	auto stmt = std::make_unique<WhileStmtAST>(std::move(condition), std::move(body), whileToken.line, whileToken.column, blockId);
	// Set end position to the closing block identifier
	stmt->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return stmt;
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
		body.push_back(parseStatementWithRecovery());
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

	auto stmt = std::make_unique<ForStmtAST>(loopVar, std::move(iterable), std::move(body), forToken.line, forToken.column, blockId);
	// Set end position to the closing block identifier
	stmt->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return stmt;
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

	if (checkKeyword("match")) {
		return parseMatch();
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

				std::vector<CallArgument> args;
				// First argument is the object itself (implicit self)
				args.push_back(CallArgument(std::make_unique<VariableExprAST>(name, idLine, idColumn), idLine, idColumn));

				// Parse remaining arguments (may be labeled: name: value)
				if (!check(TokenType::RPAREN)) {
					args.push_back(parseNamedArgument());
					while (match(TokenType::COMMA)) {
						args.push_back(parseNamedArgument());
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

			std::vector<CallArgument> args;
			if (!check(TokenType::RPAREN)) {
				do {
					args.push_back(parseNamedArgument());
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
					"\n  Note: Expected a statement (var, let, if, while, return, break, continue, match, or assignment)",
				currentLine(), currentColumn());
}

std::unique_ptr<MatchStmtAST> Parser::parseMatch() {
	Token matchToken = expectKeyword("match", "Expected 'match'");

	// Parse scrutinee expression
	auto scrutinee = parseLogicalOr();

	// Require block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after match expression");
	std::string blockId = blockIdToken.value;

	std::vector<MatchCaseAST> cases;

	// Parse cases until 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		int caseLine = currentLine();
		int caseColumn = currentColumn();

		std::vector<std::unique_ptr<ExprAST>> patterns;
		bool isDefault = false;

		// Check for 'default' case
		if (checkKeyword("default")) {
			advance(); // consume 'default'
			isDefault = true;
		} else if (check(TokenType::IDENTIFIER) &&
				   (check(TokenType::LPAREN, 1) || checkKeyword("then", 1))) {
			// Enum case pattern: caseName(binding1, binding2) then ... or caseName then ...
			Token caseNameToken = expect(TokenType::IDENTIFIER, "Expected case name");
			std::string caseName = caseNameToken.value;
			std::vector<std::string> bindings;

			// Check for bindings in parentheses
			if (check(TokenType::LPAREN)) {
				advance(); // consume '('
				if (!check(TokenType::RPAREN)) {
					do {
						Token bindingToken = expect(TokenType::IDENTIFIER, "Expected binding name");
						bindings.push_back(bindingToken.value);
					} while (match(TokenType::COMMA));
				}
				expect(TokenType::RPAREN, "Expected ')' after bindings");
				// Note: expect() already advances past the token
			}

			// Expect 'then' keyword
			expectKeywordAdvance("then", "Expected 'then' after case pattern");

			// Parse single statement
			auto stmt = parseMatchCaseStatement();

			// Check for 'and fallthrough'
			bool hasFallthrough = false;
			if (checkKeyword("and")) {
				advance(); // consume 'and'
				expectKeywordAdvance("fallthrough", "Expected 'fallthrough' after 'and'");
				hasFallthrough = true;
			}

			cases.push_back(MatchCaseAST(
				caseName,
				std::move(bindings),
				std::move(stmt),
				nullptr, // no result expression for match statement
				hasFallthrough,
				caseLine,
				caseColumn));
			continue; // Skip the regular case push below
		} else {
			// Parse first pattern (use parseLogicalAnd to stop before 'or' keyword)
			patterns.push_back(parseLogicalAnd());

			// Parse additional patterns joined by 'or'
			while (checkKeyword("or")) {
				advance(); // consume 'or'
				patterns.push_back(parseLogicalAnd());
			}
		}

		// Expect 'then' keyword
		expectKeywordAdvance("then", "Expected 'then' after pattern");

		// Parse single statement (use special parser that stops before 'and fallthrough')
		auto stmt = parseMatchCaseStatement();

		// Check for 'and fallthrough'
		bool hasFallthrough = false;
		if (checkKeyword("and")) {
			advance(); // consume 'and'
			expectKeywordAdvance("fallthrough", "Expected 'fallthrough' after 'and'");
			hasFallthrough = true;
		}

		cases.push_back(MatchCaseAST(
			std::move(patterns),
			std::move(stmt),
			nullptr, // no result expression for match statement
			isDefault,
			hasFallthrough,
			caseLine,
			caseColumn));
	}

	expectKeywordAdvance("end", "Expected 'end' to close match statement");

	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in match statement\n  Expected: '" + blockId +
						"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	auto stmt = std::make_unique<MatchStmtAST>(std::move(scrutinee), std::move(cases),
											   matchToken.line, matchToken.column, blockId);
	// Set end position to the closing block identifier
	stmt->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return stmt;
}

std::unique_ptr<MatchExprAST> Parser::parseMatchExpr() {
	Token matchToken = expectKeyword("match", "Expected 'match'");

	// Parse scrutinee expression
	auto scrutinee = parseLogicalOr();

	// Require block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after match expression");
	std::string blockId = blockIdToken.value;

	std::vector<MatchCaseAST> cases;

	// Parse cases until 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		int caseLine = currentLine();
		int caseColumn = currentColumn();

		std::vector<std::unique_ptr<ExprAST>> patterns;
		bool isDefault = false;

		// Check for 'default' case
		if (checkKeyword("default")) {
			advance(); // consume 'default'
			isDefault = true;
		} else if (check(TokenType::IDENTIFIER) &&
				   (check(TokenType::LPAREN, 1) || checkKeyword("gives", 1))) {
			// Enum case pattern: caseName(binding1, binding2) gives ... or caseName gives ...
			Token caseNameToken = expect(TokenType::IDENTIFIER, "Expected case name");
			std::string caseName = caseNameToken.value;
			std::vector<std::string> bindings;

			// Check for bindings in parentheses
			if (check(TokenType::LPAREN)) {
				advance(); // consume '('
				if (!check(TokenType::RPAREN)) {
					do {
						Token bindingToken = expect(TokenType::IDENTIFIER, "Expected binding name");
						bindings.push_back(bindingToken.value);
					} while (match(TokenType::COMMA));
				}
				expect(TokenType::RPAREN, "Expected ')' after bindings");
				// Note: expect() already advances past the token
			}

			// Expect 'gives' keyword
			expectKeywordAdvance("gives", "Expected 'gives' after case pattern");

			// Parse result expression
			auto resultExpr = parseLogicalOr();

			cases.push_back(MatchCaseAST(
				caseName,
				std::move(bindings),
				nullptr, // no statement for match expression
				std::move(resultExpr),
				false, // fallthrough not allowed in match expressions
				caseLine,
				caseColumn));
			continue; // Skip the regular case push below
		} else {
			// Parse first pattern (use parseLogicalAnd to stop before 'or' keyword)
			patterns.push_back(parseLogicalAnd());

			// Parse additional patterns joined by 'or'
			while (checkKeyword("or")) {
				advance(); // consume 'or'
				patterns.push_back(parseLogicalAnd());
			}
		}

		// Expect 'gives' keyword
		expectKeywordAdvance("gives", "Expected 'gives' after pattern");

		// Parse result expression
		auto resultExpr = parseLogicalOr();

		cases.push_back(MatchCaseAST(
			std::move(patterns),
			nullptr, // no statement for match expression
			std::move(resultExpr),
			isDefault,
			false, // fallthrough not allowed in match expressions
			caseLine,
			caseColumn));
	}

	expectKeywordAdvance("end", "Expected 'end' to close match expression");

	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end'");
	if (endBlockIdToken.value != blockId) {
		reportError("Block identifier mismatch in match expression\n  Expected: '" + blockId +
						"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	auto expr = std::make_unique<MatchExprAST>(std::move(scrutinee), std::move(cases),
											   matchToken.line, matchToken.column, blockId);
	// Set end position to the closing block identifier
	expr->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return expr;
}

std::unique_ptr<StmtAST> Parser::parseMatchCaseStatement() {
	// Parse match case statement which can be:
	// - return expr
	// - var/let decl
	// - assignment (identifier = expr)
	// - function call
	// The key difference from parseStatement is that we stop expression parsing
	// before 'and' (to support "stmt and fallthrough" syntax)
	// We use parseBitwiseOr() which is one level below parseLogicalAnd()

	if (checkKeyword("return")) {
		// Return statements need special handling to stop before 'and fallthrough'
		Token returnToken = expectKeyword("return", "Expected 'return'");

		// Check if this is a bare return (for void functions)
		std::unique_ptr<ExprAST> value = nullptr;
		if (!checkKeyword("end") && !checkKeyword("and") && !check(TokenType::END_OF_FILE)) {
			// Use parseBitwiseOr to stop before 'and fallthrough'
			value = parseBitwiseOr();
		}

		return std::make_unique<ReturnStmtAST>(std::move(value), returnToken.line, returnToken.column);
	}

	if (checkKeyword("var")) {
		// Var decl needs special handling to stop before 'and fallthrough'
		Token varToken = expectKeyword("var", "Expected 'var'");
		Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
		std::string type = "";
		expectAdvance(TokenType::ASSIGN, "Expected '='");
		auto initializer = parseBitwiseOr(); // Stop before 'and fallthrough'
		return std::make_unique<VarDeclStmtAST>(name.value, std::move(initializer), type, varToken.line, varToken.column);
	}

	if (checkKeyword("let")) {
		// Let decl needs special handling to stop before 'and fallthrough'
		Token letToken = expectKeyword("let", "Expected 'let'");
		Token name = expect(TokenType::IDENTIFIER, "Expected variable name");
		std::string type = "";
		expectAdvance(TokenType::ASSIGN, "Expected '='");
		auto initializer = parseBitwiseOr(); // Stop before 'and fallthrough'
		return std::make_unique<LetDeclStmtAST>(name.value, std::move(initializer), type, letToken.line, letToken.column);
	}

	// For identifier-based statements (assignments, function calls), we need to
	// parse expressions using parseBitwiseOr to stop before 'and' keyword
	if (check(TokenType::IDENTIFIER)) {
		std::string name = std::string(currentValue());
		int idLine = currentLine();
		int idColumn = currentColumn();
		advance();

		// Check for assignment
		if (check(TokenType::ASSIGN)) {
			Token assignToken = expect(TokenType::ASSIGN, "Expected '='");
			// Parse RHS using parseBitwiseOr to stop before 'and fallthrough'
			auto value = parseBitwiseOr();
			return std::make_unique<AssignStmtAST>(name, std::move(value), assignToken.line, assignToken.column);
		}

		// Check for member access (struct.field = value)
		if (check(TokenType::DOT)) {
			advance(); // consume '.'
			Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");

			// Check for array index on member
			if (check(TokenType::LBRACKET)) {
				advance(); // consume '['
				auto index = parseBitwiseOr();
				expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");

				if (check(TokenType::ASSIGN)) {
					advance(); // consume '='
					auto value = parseBitwiseOr();
					return std::make_unique<MemberArrayAssignStmtAST>(
						name, memberName.value, std::move(index), std::move(value),
						idLine, idColumn);
				}
			}

			// Check for member assignment
			if (check(TokenType::ASSIGN)) {
				advance(); // consume '='
				auto value = parseBitwiseOr();
				return std::make_unique<MemberAssignStmtAST>(
					name, memberName.value, std::move(value),
					idLine, idColumn);
			}
		}

		// Check for array index assignment
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['
			auto index = parseBitwiseOr();
			expectAdvance(TokenType::RBRACKET, "Expected ']' after array index");

			if (check(TokenType::ASSIGN)) {
				advance(); // consume '='
				auto value = parseBitwiseOr();
				return std::make_unique<ArrayAssignStmtAST>(
					name, std::move(index), std::move(value),
					idLine, idColumn);
			}
		}

		// Function call - create variable expression and wrap in expression statement
		// (not common in match cases but support it for completeness)
		auto varExpr = std::make_unique<VariableExprAST>(name, idLine, idColumn);
		if (check(TokenType::LPAREN)) {
			// It's a function call - need to handle this
			advance(); // consume '('
			std::vector<CallArgument> args;
			if (!check(TokenType::RPAREN)) {
				auto argExpr = parseBitwiseOr();
				int argLine = argExpr->line;
				int argCol = argExpr->column;
				args.push_back(CallArgument(std::move(argExpr), argLine, argCol));
				while (check(TokenType::COMMA)) {
					advance();
					auto nextArg = parseBitwiseOr();
					int nextLine = nextArg->line;
					int nextCol = nextArg->column;
					args.push_back(CallArgument(std::move(nextArg), nextLine, nextCol));
				}
			}
			expectAdvance(TokenType::RPAREN, "Expected ')' after function arguments");
			auto callExpr = std::make_unique<CallExprAST>(name, std::move(args), idLine, idColumn);
			return std::make_unique<ExprStmtAST>(std::move(callExpr), idLine, idColumn);
		}

		// Just a bare expression statement
		return std::make_unique<ExprStmtAST>(std::move(varExpr), idLine, idColumn);
	}

	// Fallback to generic expression statement
	auto expr = parseBitwiseOr();
	return std::make_unique<ExprStmtAST>(std::move(expr), expr->line, expr->column);
}
