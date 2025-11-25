#include "parser.h"
#include <stdexcept>

Parser::Parser(const std::vector<Token> &toks)
	: tokens(toks), position(0), defaultNamespace("") {}

void Parser::setDefaultNamespace(const std::string &ns) {
	defaultNamespace = ns;
}

// Logging helper for trace-level messages (level 3)
void Parser::logTrace(const std::string &msg) {
	if (logger_ && logger_->isEnabled(3)) {
		logger_->trace(LogPhase::Parser, msg);
	}
}

// Logging helper for detail-level messages (level 2)
void Parser::logDetail(const std::string &msg) {
	if (logger_ && logger_->isEnabled(2)) {
		logger_->detail(LogPhase::Parser, msg);
	}
}

std::unique_ptr<ProgramAST> Parser::parse() {
	std::vector<std::unique_ptr<FunctionAST>> functions;
	std::vector<std::unique_ptr<StructDefAST>> structs;

	logDetail("Starting parse...");

	while (!check(TokenType::END_OF_FILE)) {
		if (check(TokenType::END_OF_FILE)) {
			break;
		}

		// Check for struct, export, extern, or function keyword
		if (check(TokenType::KEYWORD) && currentToken().value == "struct") {
			logTrace("Parsing struct definition");
			structs.push_back(parseStruct());
		} else if (check(TokenType::KEYWORD) && currentToken().value == "export") {
			// Check what comes after export
			Token exportToken = currentToken();
			advance(); // consume 'export'
			if (check(TokenType::KEYWORD) && currentToken().value == "struct") {
				logTrace("Parsing exported struct definition");
				structs.push_back(parseStruct());
			} else if ((check(TokenType::KEYWORD) && currentToken().value == "extern") ||
					   (check(TokenType::KEYWORD) && currentToken().value == "function")) {
				logTrace("Parsing exported function definition");
				functions.push_back(parseFunction());
			} else {
				throw std::runtime_error("Expected 'struct', 'extern function', or 'function' after 'export'\n  Location: line " +
										 std::to_string(currentToken().line) + ", column " +
										 std::to_string(currentToken().column));
			}
		} else if ((check(TokenType::KEYWORD) && currentToken().value == "extern") ||
				   (check(TokenType::KEYWORD) && currentToken().value == "function")) {
			logTrace("Parsing function definition");
			functions.push_back(parseFunction());
		} else {
			throw std::runtime_error("Expected 'struct', 'export', 'function', or 'extern function' at top level\n  Location: line " +
									 std::to_string(currentToken().line) + ", column " +
									 std::to_string(currentToken().column));
		}
	}

	logDetail("Parse complete: " + std::to_string(functions.size()) + " functions, " + std::to_string(structs.size()) + " structs");

	return std::make_unique<ProgramAST>(std::move(functions), std::move(structs));
}
