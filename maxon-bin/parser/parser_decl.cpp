#include "../file_utils.h"
#include "../parser.h"
#include "../types/type_conversion.h"
#include <filesystem>
#include <stdexcept>

// Compute the end column of a token (for setEndPosition)
static int tokenEndColumn(const Token &tok) {
	return tok.column + static_cast<int>(tok.value.length()) - 1;
}

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

// ============================================================================
// Declaration Parsing Helpers
// ============================================================================

// Parse optional 'export' keyword, returns true if present
bool Parser::parseOptionalExport() {
	if (checkKeyword("export")) {
		advance();
		return true;
	}
	return false;
}

// Validate block identifier matches expected name, report error if mismatch
Token Parser::expectMatchingBlockId(const std::string &expectedName, const std::string &contextType) {
	Token blockIdToken = expect(TokenType::BLOCK_ID,
								"Expected " + contextType + " name as block identifier after 'end'");
	if (blockIdToken.value != expectedName) {
		reportError("Block identifier mismatch in " + contextType + " definition\n  Expected: '" +
						expectedName + "'\n  Found: '" + blockIdToken.value + "'",
					blockIdToken.line, blockIdToken.column);
	}
	return blockIdToken;
}

// Parse parameter list with optional self injection for methods
std::vector<FunctionParameter> Parser::parseParameterList(const std::string *selfType,
														  int selfLine, int selfColumn) {
	std::vector<FunctionParameter> parameters;
	if (selfType) {
		parameters.push_back(FunctionParameter("self", *selfType, selfLine, selfColumn));
	}
	if (!check(TokenType::RPAREN)) {
		do {
			Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");
			std::string paramType = parseTypeStringWithOptional("parameter type");
			parameters.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
		} while (match(TokenType::COMMA));
	}
	return parameters;
}

// Parse statements until 'end' keyword
std::vector<std::unique_ptr<StmtAST>> Parser::parseStatementBody() {
	std::vector<std::unique_ptr<StmtAST>> body;
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatementWithRecovery());
	}
	return body;
}

// ============================================================================
// Return Type Parsing
// ============================================================================

// Parse return type (optional - defaults to void)
// Return type must be on the same line as the function signature
// allowSelfType: if true, check for 'Self' identifier before normal type parsing (for interfaces)
std::string Parser::parseOptionalReturnType(int rparenLine, bool allowSelfType) {
	if (currentLine() == rparenLine) {
		// Handle 'Self' type specially for interface methods
		if (allowSelfType && check(TokenType::IDENTIFIER) && std::string(currentValue()) == "Self") {
			advance();
			return "Self";
		}
		auto retKd = currentKeywordData();
		bool hasReturnType = (retKd && retKd->category == KeywordCategory::Type) ||
							 check(TokenType::IDENTIFIER);
		if (hasReturnType) {
			return parseTypeStringWithOptional("return type");
		}
	}
	return "void";
}

// ============================================================================
// Unified Method Parser
// ============================================================================

// Parse a method declaration inside a struct or enum body
// receiverType: the struct/enum name that owns this method
// allowInterfacePrefix: if true, allow 'Interface.methodName' syntax (struct methods only)
std::unique_ptr<FunctionAST> Parser::parseMethodImpl(const std::string &receiverType, bool allowInterfacePrefix) {
	bool isExported = parseOptionalExport();

	Token funcToken = expectKeyword("function", "Expected 'function'");
	Token firstIdent = expect(TokenType::IDENTIFIER, "Expected method name");

	std::string methodName;
	std::string implementsInterface;

	// Check for Interface.methodName syntax (struct methods only)
	if (allowInterfacePrefix && check(TokenType::DOT)) {
		advance(); // consume '.'
		implementsInterface = firstIdent.value;
		Token methodNameToken = expect(TokenType::IDENTIFIER, "Expected method name after interface name");
		methodName = methodNameToken.value;
	} else {
		methodName = firstIdent.value;
	}

	logTrace(std::string("Parsing method '") + receiverType + "." + methodName + "'" +
			 (implementsInterface.empty() ? "" : " (implements " + implementsInterface + ")") +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(funcToken.line));

	expectAdvance(TokenType::LPAREN, "Expected '('");

	std::vector<FunctionParameter> parameters = parseParameterList(&receiverType, funcToken.line, funcToken.column);

	int rparenLine = currentLine();
	expectAdvance(TokenType::RPAREN, "Expected ')'");

	std::string returnType = parseOptionalReturnType(rparenLine);

	std::vector<std::unique_ptr<StmtAST>> body = parseStatementBody();

	expectKeywordAdvance("end", "Expected 'end' to close method body");

	Token endBlockIdToken = expectMatchingBlockId(methodName, "method");

	logTrace(std::string("Method '") + receiverType + "." + methodName + "' -> " + returnType +
			 (implementsInterface.empty() ? "" : " (implements " + implementsInterface + ")") +
			 " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	auto method = std::make_unique<FunctionAST>(methodName, std::move(parameters), returnType, std::move(body),
												false, funcToken.line, funcToken.column, defaultNamespace, isExported,
												"", false, "", receiverType, implementsInterface);
	method->setEndPosition(endBlockIdToken.line, tokenEndColumn(endBlockIdToken));
	return method;
}

// Parse a method declaration inside a struct body (allows Interface.method syntax)
std::unique_ptr<FunctionAST> Parser::parseMethod(const std::string &structName) {
	return parseMethodImpl(structName, true);
}

// Parse a method declaration inside an enum body (no Interface.method syntax)
std::unique_ptr<FunctionAST> Parser::parseEnumMethod(const std::string &enumName) {
	return parseMethodImpl(enumName, false);
}

std::unique_ptr<FunctionAST> Parser::parseFunction() {
	bool isExported = parseOptionalExport();

	// Check for extern keyword
	bool isExtern = false;
	if (checkKeyword("extern")) {
		isExtern = true;
		advance();
	}

	Token funcToken = expectKeyword("function", "Expected 'function'");
	Token name = expect(TokenType::IDENTIFIER, "Expected function name");

	// Reject external method syntax: function Type.method(...)
	if (check(TokenType::DOT)) {
		reportError("Methods must be declared inside struct bodies, not using 'function Type.method()' syntax\n"
					"  Move this method inside the '" +
						name.value + "' struct definition",
					funcToken.line, funcToken.column);
	}

	std::string functionName = name.value;

	logTrace(std::string("Parsing function '") + functionName + "'" +
			 (isExtern ? " (extern)" : "") +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(funcToken.line));

	expectAdvance(TokenType::LPAREN, "Expected '('");

	std::vector<FunctionParameter> parameters = parseParameterList(nullptr, 0, 0);

	int rparenLine = currentLine();
	expectAdvance(TokenType::RPAREN, "Expected ')'");

	std::string returnType = parseOptionalReturnType(rparenLine);

	// External functions don't have bodies
	if (isExtern) {
		if (!check(TokenType::STRING)) {
			reportError("Expected library name as string after return type for extern function\n  Example: extern function foo(x int) int \"mydll\"",
						currentLine(), currentColumn());
		}
		std::string libName = std::string(currentValue());
		advance();

		std::string libPath;
		bool isStaticLib = findStaticLibrary(libName, libPath);

		if (isStaticLib) {
			logTrace("Extern function '" + functionName + "' -> " + returnType + " from static lib '" + libPath + "' (" + std::to_string(parameters.size()) + " params)");
		} else {
			logTrace("Extern function '" + functionName + "' -> " + returnType + " from DLL '" + libName + "' (" + std::to_string(parameters.size()) + " params)");
		}
		return std::make_unique<FunctionAST>(functionName, std::move(parameters), returnType, std::vector<std::unique_ptr<StmtAST>>{}, isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported, libName, isStaticLib, libPath, "");
	}

	std::vector<std::unique_ptr<StmtAST>> body = parseStatementBody();

	expectKeywordAdvance("end", "Expected 'end' to close function body");

	Token endBlockIdToken = expectMatchingBlockId(functionName, "function");

	logTrace(std::string("Function '") + functionName + "' -> " + returnType + " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	auto func = std::make_unique<FunctionAST>(functionName, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported, "", false, "", "");
	func->setEndPosition(endBlockIdToken.line, tokenEndColumn(endBlockIdToken));
	return func;
}

std::unique_ptr<StructDefAST> Parser::parseStruct() {
	bool isExported = parseOptionalExport();

	Token structToken = expectKeyword("struct", "Expected 'struct'");
	int line = structToken.line;
	int column = structToken.column;

	// Allow 'array' keyword as struct name (stdlib collection type)
	Token nameToken = checkKeyword("array")
						  ? (advance(), Token(TokenType::IDENTIFIER, "array", currentLine(), currentColumn()))
						  : expect(TokenType::IDENTIFIER, "Expected struct name after 'struct'");
	std::string structName = nameToken.value;

	// Parse associated type parameters: struct Name uses TypeParam1, TypeParam2
	std::vector<std::string> associatedTypeParams;
	if (checkKeyword("uses")) {
		advance(); // consume 'uses'
		do {
			Token typeParamToken = expect(TokenType::IDENTIFIER, "Expected associated type parameter name after 'uses'");
			associatedTypeParams.push_back(typeParamToken.value);
		} while (check(TokenType::COMMA) && (advance(), true));
	}

	// Parse interface conformance with associated type bindings:
	// Syntax: struct Name uses TypeParams is Interface1 with Type1, Type2, Interface2 with Type3
	// - 'with' clause can have multiple types separated by commas
	// - After types, a comma followed by an identifier that's not a built-in type starts a new interface
	std::vector<std::string> conformsTo;
	std::map<std::string, std::vector<std::string>> interfaceTypeBindings;

	if (checkKeyword("is")) {
		advance(); // consume 'is'

		bool expectingInterface = true;
		while (expectingInterface) {
			Token protoToken = expect(TokenType::IDENTIFIER, "Expected interface name");
			std::string interfaceName = protoToken.value;
			conformsTo.push_back(interfaceName);

			// Parse optional 'with' clause for this interface
			if (checkKeyword("with")) {
				advance(); // consume 'with'
				std::vector<std::string> withTypes;

				// Parse types in 'with' clause
				bool parsingTypes = true;
				while (parsingTypes) {
					std::string concreteType = parseTypeString("concrete type");
					withTypes.push_back(concreteType);

					// Check if comma followed by another type (continue) or interface (stop)
					if (check(TokenType::COMMA)) {
						// Peek at the token after the comma to decide
						Token nextTok = peekToken(1);
						bool isAnotherType = Lexer::isTypeToken(nextTok) || nextTok.type == TokenType::LBRACKET;
						// Also check if it's one of the generic type parameters from 'uses' clause
						if (!isAnotherType && nextTok.type == TokenType::IDENTIFIER) {
							std::string nextIdent = nextTok.value;
							for (const auto &typeParam : associatedTypeParams) {
								if (typeParam == nextIdent) {
									isAnotherType = true;
									break;
								}
							}
						}
						if (isAnotherType) {
							advance(); // consume comma, continue parsing types
						} else {
							parsingTypes = false; // comma is for interface list
						}
					} else {
						parsingTypes = false; // No comma, done with this 'with' clause
					}
				}

				interfaceTypeBindings[interfaceName] = std::move(withTypes);
			}

			// Check for comma to continue with more interfaces
			if (match(TokenType::COMMA)) {
				// Expect another interface after comma
				expectingInterface = true;
			} else {
				// No comma - done with interface list
				expectingInterface = false;
			}
		}
	}

	std::vector<StructField> fields;
	std::vector<std::unique_ptr<FunctionAST>> methods;
	std::map<std::string, std::string> typeAssignments; // Will be populated by semantic analyzer from interfaceTypeBindings
	bool parsingFields = true;							// Fields must come before methods

	// Parse fields and methods until we hit 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		// Check for method: 'function' or 'export function'
		if (checkKeyword("function") || (checkKeyword("export") && checkKeyword("function", 1))) {
			parsingFields = false; // Once we see a method, no more fields allowed
			methods.push_back(parseMethod(structName));
			continue;
		}

		// If we're past fields section and see something that's not a method, error
		if (!parsingFields) {
			reportError("Fields must be declared before methods in struct '" + structName + "'",
						currentLine(), currentColumn());
		}

		// Parse field: (let|var) name [type] [= defaultValue]
		bool isImmutable;
		if (checkKeyword("let")) {
			advance();
			isImmutable = true;
		} else if (checkKeyword("var")) {
			advance();
			isImmutable = false;
		} else {
			reportError("Expected 'let' or 'var' for struct field declaration\n  Note: Struct fields must be declared with 'let' (immutable) or 'var' (mutable)",
						currentLine(), currentColumn());
		}

		Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected field name after 'let' or 'var'");
		std::string fieldName = fieldNameToken.value;

		// Parse optional type - type is required unless we have a default value
		// Type can start with: type keyword, '[' (legacy), or identifier (struct name)
		// But identifier must not be followed by '=' (which would be type inference)
		std::string fieldType;
		bool hasType = Lexer::isTypeToken(currentToken()) ||
					   check(TokenType::LBRACKET) ||
					   (check(TokenType::IDENTIFIER) && !check(TokenType::ASSIGN, 1));
		if (hasType) {
			fieldType = parseTypeStringWithOptional("field type");
		}

		// Parse optional default value
		std::unique_ptr<ExprAST> defaultValue = nullptr;
		if (check(TokenType::ASSIGN)) {
			advance(); // consume '='
			defaultValue = parseLogicalOr();
		}

		// Validate: must have either type or default value (or both)
		if (fieldType.empty() && defaultValue == nullptr) {
			reportError("Field '" + fieldName + "' must have a type annotation or default value",
						fieldNameToken.line, fieldNameToken.column);
		}

		fields.push_back(StructField(fieldName, fieldType, isImmutable, std::move(defaultValue),
									 fieldNameToken.line, fieldNameToken.column));
	}

	expectKeywordAdvance("end", "Expected 'end' to close struct");

	Token blockIdToken = expectMatchingBlockId(structName, "struct");

	auto structDef = std::make_unique<StructDefAST>(structName, std::move(fields), line, column, defaultNamespace, isExported, std::move(conformsTo), std::move(methods), std::move(typeAssignments), std::move(interfaceTypeBindings), std::move(associatedTypeParams));
	structDef->setEndPosition(blockIdToken.line, tokenEndColumn(blockIdToken));
	return structDef;
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
			reportError("Expected ',' or '}' in struct initialization",
						currentLine(), currentColumn());
		}
	}

	// Capture end position before consuming the closing brace
	int endLine = currentLine();
	int endCol = currentColumn();
	expectAdvance(TokenType::RBRACE, "Expected '}' to close struct initialization");

	auto expr = std::make_unique<StructInitExprAST>(structName, std::move(fields), line, column);
	expr->setEndPosition(endLine, endCol);
	return expr;
}

std::unique_ptr<EnumDefAST> Parser::parseEnum() {
	bool isExported = parseOptionalExport();

	Token enumToken = expectKeyword("enum", "Expected 'enum'");
	int line = enumToken.line;
	int column = enumToken.column;

	Token nameToken = expect(TokenType::IDENTIFIER, "Expected enum name after 'enum'");
	std::string enumName = nameToken.value;

	logTrace("Parsing enum '" + enumName + "'" +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(line));

	// Check for optional raw value type (int or string)
	std::string rawValueType;
	if (checkKeyword("int")) {
		rawValueType = "int";
		advance();
		logTrace("  Raw value type: int");
	} else if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "string") {
		rawValueType = "string";
		advance();
		logTrace("  Raw value type: string");
	}

	std::vector<EnumCaseAST> cases;
	std::vector<std::unique_ptr<FunctionAST>> methods;

	// Parse cases and methods until we hit 'end'
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		// Check for method: 'function' or 'export function'
		if (checkKeyword("function") || (checkKeyword("export") && checkKeyword("function", 1))) {
			methods.push_back(parseEnumMethod(enumName));
			continue;
		}

		// Parse case: case caseName or case caseName = value or case caseName(args)
		expectKeywordAdvance("case", "Expected 'case' or 'function' in enum");

		Token caseNameToken = expect(TokenType::IDENTIFIER, "Expected case name after 'case'");
		std::string caseName = caseNameToken.value;

		std::vector<EnumAssocValue> associatedValues;
		std::unique_ptr<ExprAST> rawValue = nullptr;

		// Check for associated values: case name(field1 Type1, field2 Type2)
		if (check(TokenType::LPAREN)) {
			advance(); // consume '('

			if (!check(TokenType::RPAREN)) {
				do {
					Token fieldNameToken = expect(TokenType::IDENTIFIER, "Expected associated value name");
					std::string fieldName = fieldNameToken.value;

					// Parse type
					std::string fieldType;
					auto kd = currentKeywordData();
					if (kd && kd->category == KeywordCategory::Type) {
						fieldType = std::string(currentValue());
						advance();
					} else if (check(TokenType::IDENTIFIER)) {
						fieldType = parseQualifiedName("associated value type");
					} else {
						reportError("Expected type for associated value '" + fieldName + "'",
									currentLine(), currentColumn());
					}

					associatedValues.push_back(EnumAssocValue(fieldName, fieldType,
															  fieldNameToken.line, fieldNameToken.column));
				} while (match(TokenType::COMMA));
			}

			expectAdvance(TokenType::RPAREN, "Expected ')' after associated values");
		}

		// Check for raw value assignment: case name = value
		if (check(TokenType::ASSIGN)) {
			advance(); // consume '='
			rawValue = parseExpression();
		}

		cases.push_back(EnumCaseAST(caseName, caseNameToken.line, caseNameToken.column,
									std::move(associatedValues), std::move(rawValue)));
		logTrace("  Case '" + caseName + "'");
	}

	expectKeywordAdvance("end", "Expected 'end' to close enum");

	Token blockIdToken = expectMatchingBlockId(enumName, "enum");

	logTrace("Enum '" + enumName + "' with " + std::to_string(cases.size()) + " cases" +
			 (methods.empty() ? "" : ", " + std::to_string(methods.size()) + " method(s)"));

	auto enumDef = std::make_unique<EnumDefAST>(enumName, std::move(cases), line, column,
												defaultNamespace, isExported, rawValueType, std::move(methods));
	enumDef->setEndPosition(blockIdToken.line, tokenEndColumn(blockIdToken));
	return enumDef;
}

std::unique_ptr<InterfaceDefAST> Parser::parseInterface() {
	bool isExported = parseOptionalExport();

	Token protoToken = expectKeyword("interface", "Expected 'interface'");
	int line = protoToken.line;
	int column = protoToken.column;

	Token nameToken = expect(TokenType::IDENTIFIER, "Expected interface name after 'interface'");
	std::string interfaceName = nameToken.value;

	logTrace("Parsing interface '" + interfaceName + "'" +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(line));

	std::vector<InterfaceMethodSignature> methods;
	std::vector<std::string> associatedTypes;

	// Parse associated types from 'uses' clause: interface Name uses Type1, Type2
	if (checkKeyword("uses")) {
		advance(); // consume 'uses'
		do {
			Token typeNameToken = expect(TokenType::IDENTIFIER, "Expected associated type name after 'uses'");
			associatedTypes.push_back(typeNameToken.value);
			logTrace("  Associated type '" + typeNameToken.value + "'");
		} while (match(TokenType::COMMA));
	}

	// Parse extends clause: interface Name uses Type extends OtherInterface
	std::string extendsInterface;
	if (checkKeyword("extends")) {
		advance(); // consume 'extends'
		Token baseToken = expect(TokenType::IDENTIFIER, "Expected interface name after 'extends'");
		extendsInterface = baseToken.value;
		logTrace("  Extends interface '" + extendsInterface + "'");
	}

	// Parse method signatures until we hit 'end'
	// Methods have implicit self parameter - not declared in signature
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		// Each method starts with 'function'
		Token funcToken = expectKeyword("function", "Expected 'function' in interface");
		Token methodNameToken = expect(TokenType::IDENTIFIER, "Expected method name");
		std::string methodName = methodNameToken.value;

		expectAdvance(TokenType::LPAREN, "Expected '(' after method name");

		// Parse parameters (no self parameter - it's implicit)
		std::vector<FunctionParameter> parameters;
		if (!check(TokenType::RPAREN)) {
			do {
				Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");

				// Handle Self type specially, otherwise use unified type parser
				std::string paramType;
				if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "Self") {
					paramType = "Self";
					advance();
				} else {
					paramType = parseTypeString("parameter type");
				}

				parameters.push_back(FunctionParameter(paramName.value, paramType, paramName.line, paramName.column));
			} while (match(TokenType::COMMA));
		}

		expectAdvance(TokenType::RPAREN, "Expected ')' after method parameters");

		// Parse return type (optional - defaults to void)
		// Handle Self type specially
		std::string returnType = "void";
		if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "Self") {
			returnType = "Self";
			advance();
		} else {
			auto retKd = currentKeywordData();
			bool hasReturnType = (retKd && retKd->category == KeywordCategory::Type) ||
								 check(TokenType::IDENTIFIER) ||
								 check(TokenType::LBRACKET);
			if (hasReturnType) {
				returnType = parseTypeStringWithOptional("return type");
			}
		}

		// Check if this method has a default implementation body
		// If next token is not 'function' or 'end', it must be the start of a body
		std::vector<std::unique_ptr<StmtAST>> defaultBody;
		bool hasDefaultImpl = !checkKeyword("function") && !checkKeyword("end") && !check(TokenType::END_OF_FILE);

		if (hasDefaultImpl) {
			defaultBody = parseStatementBody();

			expectKeywordAdvance("end", "Expected 'end' to close default implementation");

			expectMatchingBlockId(methodName, "default implementation");
		}

		methods.push_back(InterfaceMethodSignature(methodName, std::move(parameters), returnType,
												   methodNameToken.line, methodNameToken.column,
												   hasDefaultImpl, std::move(defaultBody)));

		logTrace("  Method '" + methodName + "' -> " + returnType + (hasDefaultImpl ? " (default)" : ""));
	}

	expectKeywordAdvance("end", "Expected 'end' to close interface");

	Token blockIdToken = expectMatchingBlockId(interfaceName, "interface");

	logTrace("Interface '" + interfaceName + "' with " + std::to_string(methods.size()) + " methods" +
			 (associatedTypes.empty() ? "" : ", " + std::to_string(associatedTypes.size()) + " associated type(s)") +
			 (extendsInterface.empty() ? "" : ", extends '" + extendsInterface + "'"));

	auto interfaceDef = std::make_unique<InterfaceDefAST>(interfaceName, std::move(methods), line, column, defaultNamespace, isExported, std::move(associatedTypes), extendsInterface);
	interfaceDef->setEndPosition(blockIdToken.line, tokenEndColumn(blockIdToken));
	return interfaceDef;
}
