#include "../file_utils.h"
#include "../parser.h"
#include "../types/type_conversion.h"
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

	// Reject external method syntax: function Type.method(...)
	// Methods must be declared inside struct bodies
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
					reportError("Array parameters must be unsized: use []type, not [" + std::string(currentValue()) + "]type",
								currentLine(), currentColumn());
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
					reportError("Expected array element type (int, float, ptr, char, string, bool, or struct name)",
								currentLine(), currentColumn());
				}

				// All array parameters are unsized (managed arrays)
				paramType = maxon::TypeConversion::makeManagedArrayType(elementType);
			} else {
				// Regular scalar type (or struct)
				auto kd2 = currentKeywordData();
				if (kd2 && kd2->category == KeywordCategory::Type) {
					paramType = std::string(currentValue());
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					paramType = parseQualifiedName("parameter type");
				} else {
					reportError("Expected parameter type (int, float, ptr, char, string, bool, struct name, or [size]type)",
								currentLine(), currentColumn());
				}
			}

			// Check for "or nil" suffix for optional parameters
			if (checkKeyword("or")) {
				advance(); // consume 'or'
				if (!checkKeyword("nil")) {
					reportError("Expected 'nil' after 'or' in parameter type",
								currentLine(), currentColumn());
				}
				advance(); // consume 'nil'

				// Reject nested optionals
				if (paramType.find(" or nil") != std::string::npos) {
					reportError("Cannot make optional type '" + paramType + "' optional again",
								currentLine(), currentColumn());
				}

				paramType = paramType + " or nil";
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

		// Check for "or nil" suffix for optional return types
		if (checkKeyword("or")) {
			advance(); // consume 'or'
			if (!checkKeyword("nil")) {
				reportError("Expected 'nil' after 'or' in return type",
							currentLine(), currentColumn());
			}
			advance(); // consume 'nil'

			// Reject nested optionals
			if (returnType.find(" or nil") != std::string::npos) {
				reportError("Cannot make optional type '" + returnType + "' optional again",
							currentLine(), currentColumn());
			}

			returnType = returnType + " or nil";
		}
	}

	std::vector<std::unique_ptr<StmtAST>> body;

	// External functions don't have bodies
	if (isExtern) {
		// Parse required library name for extern functions
		if (!check(TokenType::STRING)) {
			reportError("Expected library name as string after return type for extern function\n  Example: extern function foo(x int) int \"mydll\"",
						currentLine(), currentColumn());
		}
		std::string libName = std::string(currentValue());
		advance();

		// Check if a static library (.lib) file exists - if so, use static linking
		// Otherwise, assume it's a DLL and use dynamic linking (Safe FFI)
		std::string libPath;
		bool isStaticLib = findStaticLibrary(libName, libPath);

		if (isStaticLib) {
			logTrace("Extern function '" + functionName + "' -> " + returnType + " from static lib '" + libPath + "' (" + std::to_string(parameters.size()) + " params)");
		} else {
			logTrace("Extern function '" + functionName + "' -> " + returnType + " from DLL '" + libName + "' (" + std::to_string(parameters.size()) + " params)");
		}
		return std::make_unique<FunctionAST>(functionName, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported, libName, isStaticLib, libPath, "");
	}

	// Parse function body
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatementWithRecovery());
	}

	expectKeywordAdvance("end", "Expected 'end' to close function body");

	// Function has implicit block identifier which is the function name
	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected function name as block identifier after 'end'");
	if (endBlockIdToken.value != functionName) {
		reportError("Block identifier mismatch in function definition\n  Expected: '" + functionName +
						"'\n  Found: '" + endBlockIdToken.value +
						"'\n  Note: The 'end' block identifier must match the function name",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	logTrace(std::string("Function '") + functionName + "' -> " + returnType + " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	auto func = std::make_unique<FunctionAST>(functionName, std::move(parameters), returnType, std::move(body), isExtern, funcToken.line, funcToken.column, defaultNamespace, isExported, "", false, "", "");
	// Set end position to the closing block identifier
	func->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return func;
}

// Parse a method declaration inside a struct body
// The receiverType is implicitly the struct name (passed in)
// Methods have an implicit 'self' parameter auto-injected
// Syntax: function methodName(...) or function InterfaceName.methodName(...)
std::unique_ptr<FunctionAST> Parser::parseMethod(const std::string &structName) {
	// Check for export keyword
	bool isExported = false;
	if (checkKeyword("export")) {
		isExported = true;
		advance(); // consume 'export'
	}

	Token funcToken = expectKeyword("function", "Expected 'function'");
	Token firstIdent = expect(TokenType::IDENTIFIER, "Expected method name or interface name");

	std::string methodName;
	std::string implementsInterface;

	// Check for Interface.methodName syntax
	if (check(TokenType::DOT)) {
		advance(); // consume '.'
		implementsInterface = firstIdent.value;
		Token methodNameToken = expect(TokenType::IDENTIFIER, "Expected method name after interface name");
		methodName = methodNameToken.value;
	} else {
		methodName = firstIdent.value;
	}

	logTrace(std::string("Parsing method '") + structName + "." + methodName + "'" +
			 (implementsInterface.empty() ? "" : " (implements " + implementsInterface + ")") +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(funcToken.line));

	expectAdvance(TokenType::LPAREN, "Expected '('");

	// Auto-inject implicit 'self' parameter as first parameter
	std::vector<FunctionParameter> parameters;
	parameters.push_back(FunctionParameter("self", structName, funcToken.line, funcToken.column));

	// Parse explicit method parameters (after implicit self)
	if (!check(TokenType::RPAREN)) {
		do {
			Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");

			// Check for array type: []type only (sized arrays not allowed in parameters)
			std::string paramType;
			if (check(TokenType::LBRACKET)) {
				advance(); // consume '['

				// Array parameters must be unsized - reject [N]type syntax
				if (check(TokenType::NUMBER)) {
					reportError("Array parameters must be unsized: use []type, not [" + std::string(currentValue()) + "]type",
								currentLine(), currentColumn());
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
					reportError("Expected array element type (int, float, ptr, char, string, bool, or struct name)",
								currentLine(), currentColumn());
				}

				// All array parameters are unsized (managed arrays)
				paramType = maxon::TypeConversion::makeManagedArrayType(elementType);
			} else {
				// Regular scalar type (or struct)
				auto kd2 = currentKeywordData();
				if (kd2 && kd2->category == KeywordCategory::Type) {
					paramType = std::string(currentValue());
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					paramType = parseQualifiedName("parameter type");
				} else {
					reportError("Expected parameter type (int, float, ptr, char, string, bool, struct name, or [size]type)",
								currentLine(), currentColumn());
				}
			}

			// Check for "or nil" suffix for optional parameters
			if (checkKeyword("or")) {
				advance(); // consume 'or'
				if (!checkKeyword("nil")) {
					reportError("Expected 'nil' after 'or' in parameter type",
								currentLine(), currentColumn());
				}
				advance(); // consume 'nil'

				// Reject nested optionals
				if (paramType.find(" or nil") != std::string::npos) {
					reportError("Cannot make optional type '" + paramType + "' optional again",
								currentLine(), currentColumn());
				}

				paramType = paramType + " or nil";
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

		// Check for "or nil" suffix for optional return types
		if (checkKeyword("or")) {
			advance(); // consume 'or'
			if (!checkKeyword("nil")) {
				reportError("Expected 'nil' after 'or' in return type",
							currentLine(), currentColumn());
			}
			advance(); // consume 'nil'

			// Reject nested optionals
			if (returnType.find(" or nil") != std::string::npos) {
				reportError("Cannot make optional type '" + returnType + "' optional again",
							currentLine(), currentColumn());
			}

			returnType = returnType + " or nil";
		}
	}

	std::vector<std::unique_ptr<StmtAST>> body;

	// Parse method body
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatementWithRecovery());
	}

	expectKeywordAdvance("end", "Expected 'end' to close method body");

	// Method has implicit block identifier which is the method name
	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected method name as block identifier after 'end'");
	if (endBlockIdToken.value != methodName) {
		reportError("Block identifier mismatch in method definition\n  Expected: '" + methodName +
						"'\n  Found: '" + endBlockIdToken.value +
						"'\n  Note: The 'end' block identifier must match the method name",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	logTrace(std::string("Method '") + structName + "." + methodName + "' -> " + returnType +
			 (implementsInterface.empty() ? "" : " (implements " + implementsInterface + ")") +
			 " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	// receiverType is set to structName, implementsInterface tracks interface conformance
	auto method = std::make_unique<FunctionAST>(methodName, std::move(parameters), returnType, std::move(body), false, funcToken.line, funcToken.column, defaultNamespace, isExported, "", false, "", structName, implementsInterface);
	// Set end position to the closing block identifier
	method->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return method;
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

				// Parse first type
				bool parsingTypes = true;
				while (parsingTypes) {
					std::string concreteType;
					if (check(TokenType::LBRACKET)) {
						// Array type: [size]type or []type
						advance(); // consume '['
						std::string sizeStr = "";
						if (check(TokenType::NUMBER)) {
							sizeStr = std::string(currentValue());
							advance();
						}
						expectAdvance(TokenType::RBRACKET, "Expected ']' after array size");
						std::string elementType;
						if (Lexer::isTypeToken(currentToken())) {
							elementType = std::string(currentValue());
							advance();
						} else if (check(TokenType::IDENTIFIER)) {
							elementType = parseQualifiedName("array element type");
						} else {
							reportError("Expected array element type after ']'",
										currentLine(), currentColumn());
						}
						// Use new internal array type format
						if (sizeStr.empty()) {
							concreteType = maxon::TypeConversion::makeManagedArrayType(elementType);
						} else {
							concreteType = maxon::TypeConversion::makeStaticArrayType(std::stoi(sizeStr), elementType);
						}
					} else if (Lexer::isTypeToken(currentToken())) {
						concreteType = std::string(currentValue());
						advance();
					} else if (check(TokenType::IDENTIFIER)) {
						concreteType = parseQualifiedName("concrete type");
					} else {
						reportError("Expected type in 'with' clause",
									currentLine(), currentColumn());
					}
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
		std::string fieldType;
		// Check for array type: [size]type or []type
		if (check(TokenType::LBRACKET)) {
			advance(); // consume '['

			std::string sizeStr = "";
			if (check(TokenType::NUMBER)) {
				// Sized array: [16]byte
				sizeStr = std::string(currentValue());
				advance();
			}
			// else: unsized array []type

			expectAdvance(TokenType::RBRACKET, "Expected ']' after array size");

			// Get element type
			std::string elementType;
			if (Lexer::isTypeToken(currentToken())) {
				elementType = std::string(currentValue());
				advance();
			} else if (check(TokenType::IDENTIFIER)) {
				elementType = parseQualifiedName("array element type");
			} else {
				reportError("Expected array element type after ']'",
							currentLine(), currentColumn());
			}

			// Build array type string using new internal format
			if (sizeStr.empty()) {
				fieldType = maxon::TypeConversion::makeManagedArrayType(elementType);
			} else {
				fieldType = maxon::TypeConversion::makeStaticArrayType(std::stoi(sizeStr), elementType);
			}
		} else if (Lexer::isTypeToken(currentToken())) {
			fieldType = std::string(currentValue());
			advance();
		} else if (check(TokenType::IDENTIFIER) && !check(TokenType::ASSIGN)) {
			// It's a type (struct name), not an assignment
			fieldType = parseQualifiedName("struct field type");
		}
		// else: no type, must have default value for type inference

		// Check for "or nil" suffix for optional struct fields
		if (!fieldType.empty() && checkKeyword("or")) {
			advance(); // consume 'or'
			if (!checkKeyword("nil")) {
				reportError("Expected 'nil' after 'or' in field type",
							currentLine(), currentColumn());
			}
			advance(); // consume 'nil'

			// Reject nested optionals
			if (fieldType.find(" or nil") != std::string::npos) {
				reportError("Cannot make optional type '" + fieldType + "' optional again",
							currentLine(), currentColumn());
			}

			fieldType = fieldType + " or nil";
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

	// Require matching block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match struct name)");
	if (blockIdToken.value != structName) {
		reportError("Block identifier mismatch in struct definition\n  Expected: '" + structName +
						"'\n  Got: '" + blockIdToken.value + "'",
					blockIdToken.line, blockIdToken.column);
	}

	auto structDef = std::make_unique<StructDefAST>(structName, std::move(fields), line, column, defaultNamespace, isExported, std::move(conformsTo), std::move(methods), std::move(typeAssignments), std::move(interfaceTypeBindings), std::move(associatedTypeParams));
	// Set end position to the closing block identifier
	structDef->setEndPosition(blockIdToken.line, blockIdToken.column + static_cast<int>(blockIdToken.value.length()) - 1);
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
	// Check for export keyword
	bool isExported = false;
	if (checkKeyword("export")) {
		isExported = true;
		advance(); // consume 'export'
	}

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

	// Require matching block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match enum name)");
	if (blockIdToken.value != enumName) {
		reportError("Block identifier mismatch in enum definition\n  Expected: '" + enumName +
						"'\n  Got: '" + blockIdToken.value + "'",
					blockIdToken.line, blockIdToken.column);
	}

	logTrace("Enum '" + enumName + "' with " + std::to_string(cases.size()) + " cases" +
			 (methods.empty() ? "" : ", " + std::to_string(methods.size()) + " method(s)"));

	auto enumDef = std::make_unique<EnumDefAST>(enumName, std::move(cases), line, column,
												defaultNamespace, isExported, rawValueType, std::move(methods));
	// Set end position to the closing block identifier
	enumDef->setEndPosition(blockIdToken.line, blockIdToken.column + static_cast<int>(blockIdToken.value.length()) - 1);
	return enumDef;
}

// Parse a method declaration inside an enum body
// Same as struct methods but with enum as receiver type
std::unique_ptr<FunctionAST> Parser::parseEnumMethod(const std::string &enumName) {
	// Check for export keyword
	bool isExported = false;
	if (checkKeyword("export")) {
		isExported = true;
		advance(); // consume 'export'
	}

	Token funcToken = expectKeyword("function", "Expected 'function'");
	Token methodNameToken = expect(TokenType::IDENTIFIER, "Expected method name");
	std::string methodName = methodNameToken.value;

	logTrace(std::string("Parsing enum method '") + enumName + "." + methodName + "'" +
			 (isExported ? " (exported)" : "") +
			 " at line " + std::to_string(funcToken.line));

	expectAdvance(TokenType::LPAREN, "Expected '('");

	// Auto-inject implicit 'self' parameter as first parameter
	std::vector<FunctionParameter> parameters;
	parameters.push_back(FunctionParameter("self", enumName, funcToken.line, funcToken.column));

	// Parse explicit method parameters (after implicit self)
	if (!check(TokenType::RPAREN)) {
		do {
			Token paramName = expect(TokenType::IDENTIFIER, "Expected parameter name");

			// Parse parameter type
			std::string paramType;
			auto kd = currentKeywordData();
			if (kd && kd->category == KeywordCategory::Type) {
				paramType = std::string(currentValue());
				advance();
			} else if (check(TokenType::IDENTIFIER)) {
				paramType = parseQualifiedName("parameter type");
			} else {
				reportError("Expected parameter type",
							currentLine(), currentColumn());
			}

			// Check for "or nil" suffix for optional parameters
			if (checkKeyword("or")) {
				advance(); // consume 'or'
				if (!checkKeyword("nil")) {
					reportError("Expected 'nil' after 'or' in parameter type",
								currentLine(), currentColumn());
				}
				advance(); // consume 'nil'
				paramType = paramType + " or nil";
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

		// Check for "or nil" suffix for optional return types
		if (checkKeyword("or")) {
			advance(); // consume 'or'
			if (!checkKeyword("nil")) {
				reportError("Expected 'nil' after 'or' in return type",
							currentLine(), currentColumn());
			}
			advance(); // consume 'nil'
			returnType = returnType + " or nil";
		}
	}

	std::vector<std::unique_ptr<StmtAST>> body;

	// Parse method body
	while (!checkKeyword("end") && !check(TokenType::END_OF_FILE)) {
		body.push_back(parseStatementWithRecovery());
	}

	expectKeywordAdvance("end", "Expected 'end' to close method body");

	// Require matching block identifier after end
	Token endBlockIdToken = expect(TokenType::BLOCK_ID, "Expected method name as block identifier after 'end'");
	if (endBlockIdToken.value != methodName) {
		reportError("Block identifier mismatch in method definition\n  Expected: '" + methodName +
						"'\n  Found: '" + endBlockIdToken.value + "'",
					endBlockIdToken.line, endBlockIdToken.column);
	}

	logTrace(std::string("Enum method '") + enumName + "." + methodName + "' -> " + returnType +
			 " (" + std::to_string(parameters.size()) + " params, " + std::to_string(body.size()) + " statements)");

	// receiverType is set to enumName
	auto method = std::make_unique<FunctionAST>(methodName, std::move(parameters), returnType, std::move(body),
												false, funcToken.line, funcToken.column, defaultNamespace, isExported,
												"", false, "", enumName, "");
	// Set end position to the closing block identifier
	method->setEndPosition(endBlockIdToken.line, endBlockIdToken.column + static_cast<int>(endBlockIdToken.value.length()) - 1);
	return method;
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

				// Handle Self type specially
				std::string paramType;
				if (check(TokenType::IDENTIFIER) && std::string(currentValue()) == "Self") {
					paramType = "Self";
					advance();
				} else if (check(TokenType::LBRACKET)) {
					// Array type: [size]type or []type
					advance(); // consume '['

					std::string sizeStr = "";
					if (check(TokenType::NUMBER)) {
						// Sized array: [16]byte
						sizeStr = std::string(currentValue());
						advance();
					}
					// else: unsized array []type

					expectAdvance(TokenType::RBRACKET, "Expected ']' after array size");

					// Get element type
					std::string elementType;
					if (Lexer::isTypeToken(currentToken())) {
						elementType = std::string(currentValue());
						advance();
					} else if (check(TokenType::IDENTIFIER)) {
						elementType = parseQualifiedName("array element type");
					} else {
						reportError("Expected array element type after ']' in interface method signature",
									currentLine(), currentColumn());
					}

					// Build array type string using new internal format
					if (sizeStr.empty()) {
						paramType = maxon::TypeConversion::makeManagedArrayType(elementType);
					} else {
						paramType = maxon::TypeConversion::makeStaticArrayType(std::stoi(sizeStr), elementType);
					}
				} else if (Lexer::isTypeToken(currentToken())) {
					paramType = std::string(currentValue());
					advance();
				} else if (check(TokenType::IDENTIFIER)) {
					paramType = parseQualifiedName("parameter type");
				} else {
					reportError("Expected parameter type in interface method signature",
								currentLine(), currentColumn());
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

		// Check for "or nil" suffix for optional return types
		if (checkKeyword("or")) {
			advance(); // consume 'or'
			if (!checkKeyword("nil")) {
				reportError("Expected 'nil' after 'or' in return type",
							currentLine(), currentColumn());
			}
			advance(); // consume 'nil'

			// Reject nested optionals
			if (returnType.find(" or nil") != std::string::npos) {
				reportError("Cannot make optional type '" + returnType + "' optional again",
							currentLine(), currentColumn());
			}

			returnType = returnType + " or nil";
		}

		methods.push_back(InterfaceMethodSignature(methodName, std::move(parameters), returnType,
												   methodNameToken.line, methodNameToken.column));

		logTrace("  Method '" + methodName + "' -> " + returnType);
	}

	expectKeywordAdvance("end", "Expected 'end' to close interface");

	// Require matching block identifier
	Token blockIdToken = expect(TokenType::BLOCK_ID, "Expected block identifier after 'end' (must match interface name)");
	if (blockIdToken.value != interfaceName) {
		reportError("Block identifier mismatch in interface definition\n  Expected: '" + interfaceName +
						"'\n  Got: '" + blockIdToken.value + "'",
					blockIdToken.line, blockIdToken.column);
	}

	logTrace("Interface '" + interfaceName + "' with " + std::to_string(methods.size()) + " methods" +
			 (associatedTypes.empty() ? "" : ", " + std::to_string(associatedTypes.size()) + " associated type(s)"));

	auto interfaceDef = std::make_unique<InterfaceDefAST>(interfaceName, std::move(methods), line, column, defaultNamespace, isExported, std::move(associatedTypes));
	// Set end position to the closing block identifier
	interfaceDef->setEndPosition(blockIdToken.line, blockIdToken.column + static_cast<int>(blockIdToken.value.length()) - 1);
	return interfaceDef;
}
