#include "../file_utils.h"
#include "../parser.h"
#include <filesystem>
#include <stdexcept>

// Search for a static library file (.lib) in common locations
static bool findStaticLibrary(const std::string &libName, std::string &outPath) {
	std::string libFileName = libName + ".lib";

	// 1. Check current directory
	if (std::filesystem::exists(libFileName)) {
		outPath = libFileName;
		return true;
	}

	// 2. Check relative to executable directory
	std::string exeDir = getExecutableDirectory();
	std::string path = exeDir + "/" + libFileName;
	if (std::filesystem::exists(path)) {
		outPath = path;
		return true;
	}

	// 3. Check in lib subdirectory relative to executable
	path = exeDir + "/lib/" + libFileName;
	if (std::filesystem::exists(path)) {
		outPath = path;
		return true;
	}

	// 4. Check parent directory's lib folder (for development)
	path = exeDir + "/../lib/" + libFileName;
	if (std::filesystem::exists(path)) {
		outPath = path;
		return true;
	}

	return false;
}

std::unique_ptr<FunctionAST> Parser::parseFunction() {
	// Check for export keyword
	bool isExported = false;
	if (checkKeyword("export")) {
		isExported = true;
		advance(); // consume 'export'
	}

	// Check for extern keyword
	bool isExtern = false;
	if (checkKeyword("extern")) {
		isExtern = true;
		advance(); // consume 'extern'
	}

	Token funcToken = expectKeyword("function", "Expected 'function'");
	Token name = expect(TokenType::IDENTIFIER, "Expected function name");

	// Check for method syntax: function Type.method(...)
	std::string receiverType = "";
	std::string methodName = name.value;
	if (check(TokenType::DOT)) {
		// This is a method definition: function ReceiverType.methodName(...)
		receiverType = name.value;
		advance(); // consume '.'
		Token methodToken = expect(TokenType::IDENTIFIER, "Expected method name after '.'");
		methodName = methodToken.value;
	}

	logTrace(std::string("Parsing ") + (receiverType.empty() ? "function" : "method") + " '" +
			 (receiverType.empty() ? methodName : receiverType + "." + methodName) + "'" +
			 (isExtern ? " (extern)" : "") +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(funcToken.line));

	expectAdvance(TokenType::LPAREN, "Expected '('");

	// Parse function parameters
	std::vector<FunctionParameter> parameters;
	if (!check(TokenType::RPAREN)) {
		do {
			Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");

			// Check for array type: []type only (sized arrays not allowed in parameters)
			std::string paramType;
			if (check(TokenType::LBRACKET)) {
				advance(); // consume '['

				// Array parameters must be unsized - reject [N]type syntax
				if (check(TokenType::NUMBER)) {
					throw std::runtime_error("Array parameters must be unsized: use []type, not [" + std::string(currentValue()) + "]type\n  Location: line " +
											 std::to_string(currentLine()) + ", column " +
											 std::to_string(currentColumn()));
				}

				expectAdvance(TokenType::RBRACKET, "Expected ']' after '['");

				// Get element type
				std::string elementType;
				auto kd = currentKeywordData();
				if (kd && kd->category == KeywordCategory::Type) {
					elementType = std::string(currentValue());
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					elementType = parseQualifiedName("array element type");
				} else {
					throw std::runtime_error("Expected array element type (int, float, ptr, char, string, bool, or struct name)\n  Location: line " +
											 std::to_string(currentLine()) + ", column " +
											 std::to_string(currentColumn()));
				}

				// All array parameters are unsized
				paramType = "[]" + elementType;
			} else {
				// Regular scalar type (or struct)
				auto kd2 = currentKeywordData();
				if (kd2 && kd2->category == KeywordCategory::Type) {
					paramType = std::string(currentValue());
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					paramType = parseQualifiedName("parameter type");
				} else {
					throw std::runtime_error("Expected parameter type (int, float, ptr, char, string, bool, struct name, or [size]type)\n  Location: line " +
											 std::to_string(currentLine()) + ", column " +
											 std::to_string(currentColumn()));
				}
			}

			parameters.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
		} while (match(TokenType::COMMA));
	}

	expectAdvance(TokenType::RPAREN, "Expected ')'");

	// Parse return type (optional - defaults to void)
	std::string returnType = "void";
	auto retKd = currentKeywordData();
	if ((retKd && retKd->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
		returnType = std::string(currentValue());
		advance();
	}

	std::vector<std::unique_ptr<StmtAST>> body;

	// External functions don't have bodies
	if (isExtern) {
		// Parse required library name for extern functions
		if (!check(TokenType::STRING)) {
			throw std::runtime_error("Expected library name as string after return type for extern function\n  Example: extern function foo(x int) int \"mydll\"\n  Location: line " +
									 std::to_string(currentLine()) + ", column " +
									 std::to_string(currentColumn()));
		}
		std::string libName = std::string(currentValue());
		advance();

		// Check if a static library (.lib) file exists - if so, use static linking
		// Otherwise, assume it's a DLL and use dynamic linking (Safe FFI)
		std::string libPath;
		bool isStaticLib = findStaticLibrary(libName, libPath);

		if (isStaticLib) {
			logTrace("Extern function '" + methodName + "' -> " + returnType + " from static lib '" + libPath + "' (" + std::to_string(parameters.size()) + " params)");
		} else {
			logTrace("Extern function '" + methodName + "' -> " + returnType + " from DLL '" + libName + "' (" + std::to_string(parameters.size()) + " params)");
		}
		return std::make_unique<FunctionAST>(methodName, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported, libName, isStaticLib, libPath, receiverType);
	}

	// Parse function body
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatement());
	}

	expectKeywordAdvance("end", "Expected 'end' to close function body");

	// Function/method has implicit block identifier which is the function/method name
	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected function name as block identifier after 'end'");
	if (endBlockIdToken.value != methodName) {
		throw std::runtime_error("Block identifier mismatch in function definition" +
								 std::string("\n  Expected: '") + methodName + "'" +
								 "\n  Found: '" + endBlockIdToken.value + "'" +
								 "\n  Location: line " + std::to_string(endBlockIdToken.line) +
								 ", column " + std::to_string(endBlockIdToken.column) +
								 "\n  Note: The 'end' block identifier must match the function name");
	}

	logTrace((receiverType.empty() ? "Function" : "Method") + std::string(" '") + methodName + "' -> " + returnType + " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	return std::make_unique<FunctionAST>(methodName, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported, "", false, "", receiverType);
}

std::unique_ptr<StructDefAST> Parser::parseStruct() {
	// Check for export keyword
	bool isExported = false;
	if (checkKeyword("export")) {
		isExported = true;
		advance(); // consume 'export'
	}

	Token structToken = expectKeyword("struct", "Expected 'struct'");
	int line = structToken.line;
	int column = structToken.column;

	Token nameToken = expect(TokenType::IDENTIFIER, "Expected struct name after 'struct'");
	std::string structName = nameToken.value;

	// Parse interface conformance: struct Foo is Interface1, Interface2
	std::vector<std::string> conformsTo;
	if (checkKeyword("is")) {
		advance(); // consume 'is'
		do {
			Token protoToken = expect(TokenType::IDENTIFIER, "Expected interface name after 'is'");
			conformsTo.push_back(protoToken.value);
		} while (match(TokenType::COMMA));
	}

	std::vector<StructField> fields;

	// Parse fields until we hit 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name");
		std::string fieldName = fieldNameToken.value;

		std::string fieldType;
		if (Lexer::isTypeToken(currentToken())) {
			fieldType = std::string(currentValue());
			advance();
		} else if (check(TokenType::IDENTIFIER)) {
			fieldType = parseQualifiedName("struct field type");
		} else {
			throw std::runtime_error("Expected type after field name in struct field at line " +
									 std::to_string(currentLine()) + ", column " +
									 std::to_string(currentColumn()));
		}

		fields.push_back(StructField(fieldName, fieldType, fieldNameToken.line, fieldNameToken.column));
	}

	expectKeywordAdvance("end", "Expected 'end' to close struct");

	// Require matching block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match struct name)");
	if (blockIdToken.value != structName) {
		throw std::runtime_error("Block identifier mismatch in struct definition" +
								 std::string("\n  Expected: '") + structName + "'" +
								 std::string("\n  Got: '") + blockIdToken.value + "'" +
								 std::string("\n  at line ") + std::to_string(blockIdToken.line) +
								 std::string(", column ") + std::to_string(blockIdToken.column));
	}

	return std::make_unique<StructDefAST>(structName, std::move(fields), line, column, defaultNamespace, isExported, std::move(conformsTo));
}

std::unique_ptr<StructInitExprAST> Parser::parseStructInit(const std::string &structName) {
	int line = currentLine();
	int column = currentColumn();

	expectAdvance(TokenType::LBRACE, "Expected '{' for struct initialization");

	std::vector<StructInitField> fields;

	// Parse field initializers: fieldName: value
	while (!check(TokenType::RBRACE) && !check(TokenType::END_OF_FILE)) {
		Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name");
		std::string fieldName = fieldNameToken.value;

		expectAdvance(TokenType::COLON, "Expected ':' after field name");

		auto value = parseExpression();

		fields.push_back(StructInitField(fieldName, std::move(value),
										 fieldNameToken.line, fieldNameToken.column));

		// Check for comma (more fields) or closing brace
		if (check(TokenType::COMMA)) {
			advance();
		} else if (!check(TokenType::RBRACE)) {
			throw std::runtime_error("Expected ',' or '}' in struct initialization at line " +
									 std::to_string(currentLine()) + ", column " +
									 std::to_string(currentColumn()));
		}
	}

	expectAdvance(TokenType::RBRACE, "Expected '}' to close struct initialization");

	return std::make_unique<StructInitExprAST>(structName, std::move(fields), line, column);
}

std::unique_ptr<InterfaceDefAST> Parser::parseInterface() {
	// Check for export keyword
	bool isExported = false;
	if (checkKeyword("export")) {
		isExported = true;
		advance(); // consume 'export'
	}

	Token protoToken = expectKeyword("interface", "Expected 'interface'");
	int line = protoToken.line;
	int column = protoToken.column;

	Token nameToken = expect(TokenType::IDENTIFIER, "Expected interface name after 'interface'");
	std::string interfaceName = nameToken.value;

	logTrace("Parsing interface '" + interfaceName + "'" +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(line));

	std::vector<InterfaceMethodSignature> methods;

	// Parse method signatures until we hit 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		// Each method starts with 'function'
		Token funcToken = expectKeyword("function", "Expected 'function' for method signature in interface");
		Token methodNameToken = expect(TokenType::IDENTIFIER, "Expected method name");
		std::string methodName = methodNameToken.value;

		expectAdvance(TokenType::LPAREN, "Expected '(' after method name");

		// Parse parameters
		std::vector<FunctionParameter> parameters;
		if (!check(TokenType::RPAREN)) {
			do {
				Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");

				// Handle Self type specially
				std::string paramType;
				if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "Self") {
					paramType = "Self";
					advance();
				} else if (Lexer::isTypeToken(currentToken())) {
					paramType = std::string(currentValue());
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					paramType = parseQualifiedName("parameter type");
				} else {
					throw std::runtime_error("Expected parameter type in interface method signature\n  Location: line " +
											 std::to_string(currentLine()) + ", column " +
											 std::to_string(currentColumn()));
				}

				parameters.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
			} while (match(TokenType::COMMA));
		}

		expectAdvance(TokenType::RPAREN, "Expected ')' after method parameters");

		// Parse return type (optional - defaults to void)
		std::string returnType = "void";
		if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "Self") {
			returnType = "Self";
			advance();
		} else {
			auto retKd = currentKeywordData();
			if ((retKd && retKd->category == KeywordCategory::Type) || check(TokenType::IDENTIFIER)) {
				returnType = std::string(currentValue());
				advance();
			}
		}

		methods.push_back(InterfaceMethodSignature(methodName, std::move(parameters), returnType,
												  methodNameToken.line, methodNameToken.column));

		logTrace("  Method '" + methodName + "' -> " + returnType);
	}

	expectKeywordAdvance("end", "Expected 'end' to close interface");

	// Require matching block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match interface name)");
	if (blockIdToken.value != interfaceName) {
		throw std::runtime_error("Block identifier mismatch in interface definition" +
								 std::string("\n  Expected: '") + interfaceName + "'" +
								 std::string("\n  Got: '") + blockIdToken.value + "'" +
								 std::string("\n  at line ") + std::to_string(blockIdToken.line) +
								 std::string(", column ") + std::to_string(blockIdToken.column));
	}

	logTrace("Interface '" + interfaceName + "' with " + std::to_string(methods.size()) + " methods");

	return std::make_unique<InterfaceDefAST>(interfaceName, std::move(methods), line, column, defaultNamespace, isExported);
}
