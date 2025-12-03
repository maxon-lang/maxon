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

// Error reporting helper - throws runtime_error with location info
void Parser::reportError(const std::string &message, int line, int column) {
	throw std::runtime_error(message + "\n  Location: line " +
							 std::to_string(line) + ", column " +
							 std::to_string(column));
}

std::unique_ptr<ProgramAST> Parser::parse() {
	std::vector<std::unique_ptr<FunctionAST>> functions;
	std::vector<std::unique_ptr<StructDefAST>> structs;
	std::vector<std::unique_ptr<EnumDefAST>> enums;
	std::vector<std::unique_ptr<InterfaceDefAST>> interfaces;

	logDetail("Starting parse...");

	while (!check(TokenType::END_OF_FILE)) {
		if (check(TokenType::END_OF_FILE)) {
			break;
		}

		// Check for struct, enum, interface, export, extern, or function keyword
		if (checkKeyword("struct")) {
			logTrace("Parsing struct definition");
			structs.push_back(parseStruct());
		} else if (checkKeyword("enum")) {
			logTrace("Parsing enum definition");
			enums.push_back(parseEnum());
		} else if (checkKeyword("interface")) {
			logTrace("Parsing interface definition");
			interfaces.push_back(parseInterface());
		} else if (checkKeyword("export")) {
			// Peek at what comes after export (don't consume - let the parse functions handle export)
			// We need to look ahead to determine which parser to call
			size_t savedPos = position;
			advance(); // temporarily consume 'export' to peek
			if (checkKeyword("struct")) {
				position = savedPos; // restore position
				cache_.set_position(savedPos);
				logTrace("Parsing exported struct definition");
				structs.push_back(parseStruct());
			} else if (checkKeyword("enum")) {
				position = savedPos; // restore position
				cache_.set_position(savedPos);
				logTrace("Parsing exported enum definition");
				enums.push_back(parseEnum());
			} else if (checkKeyword("interface")) {
				position = savedPos; // restore position
				cache_.set_position(savedPos);
				logTrace("Parsing exported interface definition");
				interfaces.push_back(parseInterface());
			} else if (checkKeyword("extern") || checkKeyword("function")) {
				position = savedPos; // restore position
				cache_.set_position(savedPos);
				logTrace("Parsing exported function definition");
				functions.push_back(parseFunction());
			} else {
				reportError("Expected 'struct', 'enum', 'interface', 'extern function', or 'function' after 'export'",
							currentLine(), currentColumn());
			}
		} else if (checkKeyword("extern") || checkKeyword("function")) {
			logTrace("Parsing function definition");
			functions.push_back(parseFunction());
		} else {
			reportError("Expected 'struct', 'enum', 'interface', 'export', 'function', or 'extern function' at top level",
						currentLine(), currentColumn());
		}
	}

	logDetail("Parse complete: " + std::to_string(functions.size()) + " functions, " +
			  std::to_string(structs.size()) + " structs, " +
			  std::to_string(enums.size()) + " enums, " +
			  std::to_string(interfaces.size()) + " interfaces");

	return std::make_unique<ProgramAST>(std::move(functions), std::move(structs), std::move(interfaces), std::move(enums));
}
