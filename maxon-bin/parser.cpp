#include "parser.h"
#include <stdexcept>

// Primary constructor: accepts TokenStream directly
Parser::Parser(TokenStream &&stream)
	: stream_(std::move(stream)), position(0), defaultNamespace("") {
	// Initialize lookahead cache
	cache_.initialize(&stream_);
	cache_.set_position(0);

	// Analyze block boundaries for O(1) end-matching
	boundary_.analyze(stream_);
}

// Legacy constructor for compatibility (converts to TokenStream internally)
Parser::Parser(const std::vector<Token> &toks)
	: position(0), defaultNamespace("") {
	// Import tokens into SIMD-optimized stream
	stream_.import_tokens(toks);

	// Initialize lookahead cache
	cache_.initialize(&stream_);
	cache_.set_position(0);

	// Analyze block boundaries
	boundary_.analyze(stream_);
}

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
		if (checkKeyword("struct")) {
			logTrace("Parsing struct definition");
			structs.push_back(parseStruct());
		} else if (checkKeyword("export")) {
			// Check what comes after export
			Token exportToken = currentToken();
			advance(); // consume 'export'
			if (checkKeyword("struct")) {
				logTrace("Parsing exported struct definition");
				structs.push_back(parseStruct());
			} else if (checkKeyword("extern") || checkKeyword("function")) {
				logTrace("Parsing exported function definition");
				functions.push_back(parseFunction());
			} else {
				throw std::runtime_error("Expected 'struct', 'extern function', or 'function' after 'export'\n  Location: line " +
										 std::to_string(currentLine()) + ", column " +
										 std::to_string(currentColumn()));
			}
		} else if (checkKeyword("extern") || checkKeyword("function")) {
			logTrace("Parsing function definition");
			functions.push_back(parseFunction());
		} else {
			throw std::runtime_error("Expected 'struct', 'export', 'function', or 'extern function' at top level\n  Location: line " +
									 std::to_string(currentLine()) + ", column " +
									 std::to_string(currentColumn()));
		}
	}

	logDetail("Parse complete: " + std::to_string(functions.size()) + " functions, " + std::to_string(structs.size()) + " structs");

	return std::make_unique<ProgramAST>(std::move(functions), std::move(structs));
}
