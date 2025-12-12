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
	std::vector<std::unique_ptr<GlobalLetDeclAST>> globals;

	logDetail("Starting parse...");

	while (!check(TokenType::END_OF_FILE)) {
		if (check(TokenType::END_OF_FILE)) {
			break;
		}

		int startLine = currentLine();
		int startCol = currentColumn();

		try {
			// Check for struct, enum, interface, export, extern, or function keyword
			if (checkKeyword("type")) {
				logTrace("Parsing type definition");
				structs.push_back(parseStruct());
			} else if (checkKeyword("enum")) {
				logTrace("Parsing enum definition");
				enums.push_back(parseEnum());
			} else if (checkKeyword("interface")) {
				logTrace("Parsing interface definition");
				interfaces.push_back(parseInterface());
			} else if (checkKeyword("let")) {
				logTrace("Parsing top-level let declaration");
				globals.push_back(parseTopLevelLet(false));
			} else if (checkKeyword("export")) {
				// Peek at what comes after export (don't consume - let the parse functions handle export)
				// We need to look ahead to determine which parser to call
				size_t savedPos = position;
				advance(); // temporarily consume 'export' to peek
				if (checkKeyword("type")) {
					position = savedPos; // restore position
					cache_.set_position(savedPos);
					logTrace("Parsing exported type definition");
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
				} else if (checkKeyword("let")) {
					position = savedPos; // restore position
					cache_.set_position(savedPos);
					logTrace("Parsing exported top-level let declaration");
					globals.push_back(parseTopLevelLet(true));
				} else if (checkKeyword("extern") || checkKeyword("function")) {
					position = savedPos; // restore position
					cache_.set_position(savedPos);
					logTrace("Parsing exported function definition");
					functions.push_back(parseFunction());
				} else {
					reportError("Expected 'struct', 'enum', 'interface', 'let', 'extern function', or 'function' after 'export'",
								currentLine(), currentColumn());
				}
			} else if (checkKeyword("extern") || checkKeyword("function")) {
				logTrace("Parsing function definition");
				functions.push_back(parseFunction());
			} else {
				reportError("Expected 'struct', 'enum', 'interface', 'let', 'export', 'function', or 'extern function' at top level",
							currentLine(), currentColumn());
			}
		} catch (const std::runtime_error &e) {
			// Record the error
			std::string errorMsg = e.what();
			parseErrors_.push_back(ParseError(errorMsg, startLine, startCol));

			// Track position before sync to detect infinite loop
			size_t posBeforeSync = position;

			// Synchronize to next top-level declaration
			synchronize();

			// If we're still at the same position after sync, advance to prevent infinite loop
			// This can happen when the current token is a sync token but not valid at top-level
			// (e.g., 'end' keyword from an incomplete struct/function)
			if (position == posBeforeSync && !check(TokenType::END_OF_FILE)) {
				advance();
			}
		}
	}

	logDetail("Parse complete: " + std::to_string(functions.size()) + " functions, " +
			  std::to_string(structs.size()) + " structs, " +
			  std::to_string(enums.size()) + " enums, " +
			  std::to_string(interfaces.size()) + " interfaces, " +
			  std::to_string(globals.size()) + " globals" +
			  (parseErrors_.empty() ? "" : ", " + std::to_string(parseErrors_.size()) + " error(s)"));

	auto program = std::make_unique<ProgramAST>(std::move(functions), std::move(structs), std::move(interfaces), std::move(enums), std::move(globals));

	// Copy parse errors to ProgramAST for access after parsing
	for (const auto &err : parseErrors_) {
		program->parseErrors.push_back(ASTParseError(err.message, err.line, err.column));
	}

	return program;
}
