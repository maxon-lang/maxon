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

	expect(TokenType::EQUALS, "Expected '='");
	auto initializer = parseExpression();

	return {name, type, std::move(initializer)};
}

std::unique_ptr<AssignStmtAST> Parser::parseAssignment(const std::string &name) {
	Token assignToken = expect(TokenType::EQUALS, "Expected '='");
	auto value = parseExpression();
	return std::make_unique<AssignStmtAST>(name, std::move(value), assignToken.line, assignToken.column);
}

std::unique_ptr<ReturnStmtAST> Parser::parseReturn() {
	Token returnToken = expectKeyword("return", "Expected 'return'");
	auto value = parseExpression();
	return std::make_unique<ReturnStmtAST>(std::move(value), returnToken.line, returnToken.column);
}

std::unique_ptr<BreakStmtAST> Parser::parseBreak() {
	Token breakToken = expectKeyword("break", "Expected 'break'");
	return std::make_unique<BreakStmtAST>(breakToken.line, breakToken.column);
}

std::unique_ptr<ContinueStmtAST> Parser::parseContinue() {
	Token continueToken = expectKeyword("continue", "Expected 'continue'");
	return std::make_unique<ContinueStmtAST>(continueToken.line, continueToken.column);
}

std::unique_ptr<IfStmtAST> Parser::parseIf() {
	Token ifToken = expectKeyword("if", "Expected 'if'");
	auto condition = parseExpression();

	int conditionLine = ifToken.line;

	// Check if this is a single-line if statement (no block identifier)
	// Single-line if: if <condition> <statement>
	// Multi-line if: if <condition> 'blockId' ... end 'blockId'
	bool isSingleLine = false;
	if (!check(TokenType::BLOCK_ID)) {
		// No block identifier means single-line if
		isSingleLine = true;
	}

	std::vector<std::unique_ptr<StmtAST>> thenBody;
	std::vector<std::unique_ptr<StmtAST>> elseBody;

	if (isSingleLine) {
		// Single-line if: parse one statement that must be on the same line
		if (currentToken().line != conditionLine) {
			throw std::runtime_error("Single-line if statement must have statement on same line" +
									 std::string("\n  Location: line ") + std::to_string(conditionLine) +
									 ", column " + std::to_string(ifToken.column) +
									 "\n  Note: For multi-line if blocks, use: if <condition> 'id' ... end 'id'");
		}
		thenBody.push_back(parseStatement());

		// Single-line if doesn't support else
		return std::make_unique<IfStmtAST>(std::move(condition),
										   std::move(thenBody),
										   std::move(elseBody),
										   ifToken.line, ifToken.column, "");
	}

	// Multi-line if with block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'if' condition (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	// Parse then body
	while (!(check(TokenType::KEYWORD) && currentToken().value == "else") &&
		   !(check(TokenType::KEYWORD) && currentToken().value == "end") &&
		   !check(TokenType::END_OF_FILE)) {
		thenBody.push_back(parseStatement());
	}

	// Parse optional else
	if (check(TokenType::KEYWORD) && currentToken().value == "else") {
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

		while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
			elseBody.push_back(parseStatement());
		}
	}

	expectKeyword("end", "Expected 'end' to close if block");

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

	return std::make_unique<IfStmtAST>(std::move(condition),
									   std::move(thenBody),
									   std::move(elseBody),
									   ifToken.line, ifToken.column, blockId);
}
std::unique_ptr<WhileStmtAST> Parser::parseWhile() {
	Token whileToken = expectKeyword("while", "Expected 'while'");
	auto condition = parseExpression();

	// Require block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'while' condition (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> body;

	// Parse body
	while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatement());
	}

	expectKeyword("end", "Expected 'end' to close while loop");

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
	expectKeyword("in", "Expected 'in' after loop variable");

	// Parse iterable expression (range call, array variable, etc.)
	auto iterable = parseExpression();

	// Require block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'for' iterable (use 'id' where id is any string)");
	std::string blockId = blockIdToken.value;

	std::vector<std::unique_ptr<StmtAST>> body;

	// Parse body
	while (!(check(TokenType::KEYWORD) && currentToken().value == "end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatement());
	}

	expectKeyword("end", "Expected 'end' to close for loop");

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
	if (check(TokenType::KEYWORD) && currentToken().value == "var") {
		return parseVarDecl();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "let") {
		return parseLetDecl();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "if") {
		return parseIf();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "while") {
		return parseWhile();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "for") {
		return parseFor();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "return") {
		return parseReturn();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "break") {
		return parseBreak();
	}

	if (check(TokenType::KEYWORD) && currentToken().value == "continue") {
		return parseContinue();
	}

	// Check for pointer dereference assignment: *ptr = value
	if (check(TokenType::MULTIPLY)) {
		int line = currentToken().line;
		int column = currentToken().column;
		advance(); // consume '*'

		// Parse just the pointer identifier/expression (not a full expression that includes =)
		auto ptrExpr = parsePrimary();

		// Expect '='
		if (!check(TokenType::EQUALS)) {
			throw std::runtime_error("Expected '=' after pointer dereference in assignment at line " +
									 std::to_string(line) + ", column " + std::to_string(column));
		}
		advance(); // consume '='

		// Parse the value to assign
		auto value = parseExpression();

		return std::make_unique<DerefAssignStmtAST>(std::move(ptrExpr), std::move(value), line, column);
	}

	if (check(TokenType::IDENTIFIER)) {
		std::string name = currentToken().value;
		int idLine = currentToken().line;
		int idColumn = currentToken().column;
		advance();

		// Check for namespace qualification
		if (check(TokenType::DOT)) {
			advance(); // consume '.'
			Token memberName = expect(TokenType::IDENTIFIER, "Expected identifier after '.'");
			name = name + "." + memberName.value;
		}

		// Check for array indexing assignment: array[index] = value or array[index].member = value
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['
			auto index = parseExpression();
			expect(TokenType::RBRACKET, "Expected ']' after array index");

			// Check for member access on array element
			if (check(TokenType::DOT)) {
				advance(); // consume '.'
				Token memberName = expect(TokenType::IDENTIFIER, "Expected member name after '.'");
				expect(TokenType::EQUALS, "Expected '=' in member assignment");
				auto value = parseExpression();
				// Create ArrayMemberAssignStmtAST for arr[i].field = value
				return std::make_unique<ArrayMemberAssignStmtAST>(name, std::move(index), memberName.value, std::move(value), idLine, idColumn);
			}

			expect(TokenType::EQUALS, "Expected '=' in array assignment");
			auto value = parseExpression();
			return std::make_unique<ArrayAssignStmtAST>(name, std::move(index), std::move(value), idLine, idColumn);
		}

		if (check(TokenType::EQUALS)) {
			return parseAssignment(name);
		}

		if (check(TokenType::LPAREN)) {
			// Parse function call as statement
			advance(); // consume '('

			std::vector<std::unique_ptr<ExprAST>> args;
			if (!check(TokenType::RPAREN)) {
				do {
					args.push_back(parseExpression());
				} while (match(TokenType::COMMA));
			}

			expect(TokenType::RPAREN, "Expected ')' after function arguments");

			auto callExpr = std::make_unique<CallExprAST>(name, std::move(args), idLine, idColumn);
			return std::make_unique<ExprStmtAST>(std::move(callExpr), idLine, idColumn);
		}

		throw std::runtime_error("Unexpected identifier '" + name + "'" +
								 std::string("\n  Location: line ") + std::to_string(idLine) +
								 ", column " + std::to_string(idColumn) +
								 "\n  Note: Did you forget an assignment (=), function call (), or keyword?");
	}

	std::string foundStr;
	if (currentToken().type == TokenType::END_OF_FILE) {
		foundStr = "end of file";
	} else {
		foundStr = "'" + currentToken().value + "'";
	}

	throw std::runtime_error("Unexpected token: " + foundStr +
							 "\n  Location: line " + std::to_string(currentToken().line) +
							 ", column " + std::to_string(currentToken().column) +
							 "\n  Note: Expected a statement (var, let, if, while, return, break, continue, or assignment)");
}
