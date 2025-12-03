#include "semantic_analyzer.h"
#include "lexer.h"
#include "types/type_conversion.h"
#include <algorithm>
#include <sstream>

// Helper to convert simple literal expressions to string for display
static std::string exprToString(ExprAST *expr) {
	if (!expr)
		return "";

	if (auto *num = dynamic_cast<NumberExprAST *>(expr)) {
		return std::to_string(num->value);
	}
	if (auto *flt = dynamic_cast<FloatExprAST *>(expr)) {
		std::ostringstream ss;
		ss << flt->value;
		return ss.str();
	}
	if (auto *boolExpr = dynamic_cast<BooleanExprAST *>(expr)) {
		return boolExpr->value ? "true" : "false";
	}
	if (auto *ch = dynamic_cast<CharacterExprAST *>(expr)) {
		return std::string("'") + ch->value + "'";
	}
	if (auto *str = dynamic_cast<StringLiteralExprAST *>(expr)) {
		return "\"" + str->value + "\"";
	}
	if (auto *byteExpr = dynamic_cast<ByteExprAST *>(expr)) {
		return std::to_string(byteExpr->value) + "b";
	}
	// For complex expressions, just indicate there's a default
	return "<expr>";
}

SemanticAnalyzer::SemanticAnalyzer() : loopDepth(0) {}

// Logging helper for trace-level messages (level 3)
void SemanticAnalyzer::logTrace(const std::string &msg) {
	if (logger_ && logger_->isEnabled(3)) {
		logger_->trace(LogPhase::Semantic, msg);
	}
}

// Logging helper for detail-level messages (level 2)
void SemanticAnalyzer::logDetail(const std::string &msg) {
	if (logger_ && logger_->isEnabled(2)) {
		logger_->detail(LogPhase::Semantic, msg);
	}
}

const StructInfo *SemanticAnalyzer::lookupStruct(const std::string &name) const {
	// First try exact match
	auto it = structs.find(name);
	if (it != structs.end()) {
		return &it->second;
	}

	// If not found and name contains a dot, it's already qualified
	if (name.find('.') != std::string::npos) {
		return nullptr;
	}

	// For unqualified names, also try qualified lookups with all known namespaces
	// This allows unqualified access to exported structs
	for (const auto &pair : structs) {
		const std::string &structName = pair.first;
		// Check if this is a qualified name ending with the requested name
		if (structName.size() > name.size() + 1 &&
			structName.substr(structName.size() - name.size()) == name &&
			structName[structName.size() - name.size() - 1] == '.') {
			return &pair.second;
		}
	}

	return nullptr;
}

const EnumInfo *SemanticAnalyzer::lookupEnum(const std::string &name) const {
	// First try exact match
	auto it = enums.find(name);
	if (it != enums.end()) {
		return &it->second;
	}

	// If not found and name contains a dot, it's already qualified
	if (name.find('.') != std::string::npos) {
		return nullptr;
	}

	// For unqualified names, also try qualified lookups with all known namespaces
	// This allows unqualified access to exported enums
	for (const auto &pair : enums) {
		const std::string &enumName = pair.first;
		// Check if this is a qualified name ending with the requested name
		if (enumName.size() > name.size() + 1 &&
			enumName.substr(enumName.size() - name.size()) == name &&
			enumName[enumName.size() - name.size() - 1] == '.') {
			return &pair.second;
		}
	}

	return nullptr;
}

void SemanticAnalyzer::registerExternalFunction(const std::string &name, const std::string &returnType,
												const std::vector<FunctionParameter> &parameters) {
	functions.emplace(name, FunctionInfo(name, returnType, parameters));
	// External functions get indices starting from a high offset to avoid conflicts
	// We'll assign them sequential IDs starting from a large base
	static size_t externalFunctionIdBase = 1000000;
	functionIndices[name] = externalFunctionIdBase++;
}

void SemanticAnalyzer::registerExternalStruct(const std::string &name, const std::vector<StructFieldInfo> &fields,
											  const std::vector<std::string> &conformsTo,
											  const std::map<std::string, std::string> &typeAssignments) {
	// Only register if not already defined
	if (structs.find(name) == structs.end()) {
		structs.emplace(name, StructInfo(name, fields, 0, 0, conformsTo, typeAssignments));
		logTrace("Registered external struct: " + name);
	}
}

void SemanticAnalyzer::registerBuiltinFunctions() {
	// Register hash and equals methods for primitive hashable types
	// These are implemented as intrinsics in codegen

	// int.hash() -> int (multiplicative hash)
	functions.emplace("int.hash", FunctionInfo("int.hash", "int", {FunctionParameter("self", "int")}));
	// int.equals(other) -> bool
	functions.emplace("int.equals", FunctionInfo("int.equals", "bool", {FunctionParameter("self", "int"), FunctionParameter("other", "int")}));

	// byte.hash() -> int
	functions.emplace("byte.hash", FunctionInfo("byte.hash", "int", {FunctionParameter("self", "byte")}));
	// byte.equals(other) -> bool
	functions.emplace("byte.equals", FunctionInfo("byte.equals", "bool", {FunctionParameter("self", "byte"), FunctionParameter("other", "byte")}));

	// Note: char.hash() and char.equals() are now defined in stdlib/string/char.maxon
	// since char is a stdlib struct type (grapheme cluster), not a primitive
}

std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST *program) {
	errors.clear();
	// Note: Don't clear functions or structs here - we want to keep registered external ones
	// structs.clear(); // Preserve external structs registered before analysis
	interfaces.clear();
	variables.clear();
	scopeStack.clear();
	loopDepth = 0;
	blockIdStack.clear(); // Clear block ID stack for new analysis
	undefinedFunctions.clear();
	undefinedStructs.clear();
	undefinedInterfaces.clear();
	allDeclaredVariables.clear(); // Clear persistent symbol table

	logDetail("Starting semantic analysis");
	logTrace("Registered external functions: " + std::to_string(functions.size()));
	logTrace("Registered external structs: " + std::to_string(structs.size()));

	// First pass: collect all interface definitions
	logTrace("Pass 1a: Collecting interface definitions");
	for (const auto &interfaceDef : program->interfaces) {
		std::string interfaceKey = (interfaceDef->isExported && !interfaceDef->namespaceName.empty())
									   ? interfaceDef->namespaceName + "." + interfaceDef->name
									   : interfaceDef->name;

		logTrace("Registering interface: " + interfaceKey);
		if (interfaces.find(interfaceKey) != interfaces.end()) {
			addError("Interface '" + interfaceKey + "' is already defined",
					 interfaceDef->line, interfaceDef->column);
		} else {
			InterfaceInfo protoInfo(interfaceDef->name, interfaceDef->line, interfaceDef->column,
									interfaceDef->associatedTypes);
			for (const auto &method : interfaceDef->methods) {
				protoInfo.methods.push_back(InterfaceMethodInfo(method.name, method.returnType, method.parameters));
			}
			interfaces.emplace(interfaceKey, std::move(protoInfo));

			// Also register simple name
			if (interfaces.find(interfaceDef->name) == interfaces.end()) {
				InterfaceInfo protoInfoSimple(interfaceDef->name, interfaceDef->line, interfaceDef->column,
											  interfaceDef->associatedTypes);
				for (const auto &method : interfaceDef->methods) {
					protoInfoSimple.methods.push_back(InterfaceMethodInfo(method.name, method.returnType, method.parameters));
				}
				interfaces.emplace(interfaceDef->name, std::move(protoInfoSimple));
			}
		}
	}

	// First pass: collect all enum definitions
	logTrace("Pass 1b: Collecting enum definitions");
	for (const auto &enumDef : program->enums) {
		std::string enumKey = (enumDef->isExported && !enumDef->namespaceName.empty())
								  ? enumDef->namespaceName + "." + enumDef->name
								  : enumDef->name;

		logTrace("Registering enum: " + enumKey);
		if (enums.find(enumKey) != enums.end()) {
			addError("Enum '" + enumKey + "' is already defined",
					 enumDef->line, enumDef->column);
		} else {
			EnumInfo enumInfo(enumDef->name, enumDef->line, enumDef->column, enumDef->rawValueType);

			std::set<std::string> caseNames;
			std::set<int64_t> rawIntValues;
			std::set<std::string> rawStringValues;
			int tagValue = 0;

			for (const auto &caseDef : enumDef->cases) {
				// Check for duplicate case names
				if (caseNames.find(caseDef.name) != caseNames.end()) {
					addError("Duplicate enum case '" + caseDef.name + "' in enum '" + enumDef->name + "'",
							 caseDef.line, caseDef.column);
					continue;
				}
				caseNames.insert(caseDef.name);

				EnumCaseInfo caseInfo(caseDef.name, tagValue++, caseDef.line, caseDef.column);

				// Handle associated values
				for (const auto &assoc : caseDef.associatedValues) {
					caseInfo.associatedValues.push_back(
						EnumAssocValueInfo(assoc.name, assoc.type, assoc.line, assoc.column));
					enumInfo.hasAssociatedValues = true;
				}

				// Handle raw value
				if (caseDef.rawValue) {
					if (enumDef->rawValueType.empty()) {
						addError("Raw value specified for case '" + caseDef.name +
									 "' but enum '" + enumDef->name + "' has no raw value type\n"
									 "  Declare the enum with a raw value type: enum " + enumDef->name + " int",
								 caseDef.line, caseDef.column);
					} else {
						// Analyze raw value expression
						std::string rawValueType = analyzeExpression(caseDef.rawValue.get());
						if (!typesMatch(enumDef->rawValueType, rawValueType)) {
							addError("Raw value type '" + rawValueType +
										 "' does not match enum raw value type '" + enumDef->rawValueType + "'",
									 caseDef.rawValue->line, caseDef.rawValue->column);
						} else {
							caseInfo.hasRawValue = true;
							// Extract raw value for codegen
							if (enumDef->rawValueType == "int") {
								if (auto *numExpr = dynamic_cast<NumberExprAST *>(caseDef.rawValue.get())) {
									caseInfo.rawIntValue = numExpr->value;
									// Check for duplicate raw values
									if (rawIntValues.find(caseInfo.rawIntValue) != rawIntValues.end()) {
										addError("Duplicate raw value " + std::to_string(caseInfo.rawIntValue) +
													 " in enum '" + enumDef->name + "'",
												 caseDef.line, caseDef.column);
									}
									rawIntValues.insert(caseInfo.rawIntValue);
								}
							} else if (enumDef->rawValueType == "string") {
								if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(caseDef.rawValue.get())) {
									caseInfo.rawStringValue = strExpr->value;
									// Check for duplicate raw values
									if (rawStringValues.find(caseInfo.rawStringValue) != rawStringValues.end()) {
										addError("Duplicate raw value \"" + caseInfo.rawStringValue +
													 "\" in enum '" + enumDef->name + "'",
												 caseDef.line, caseDef.column);
									}
									rawStringValues.insert(caseInfo.rawStringValue);
								}
							}
						}
					}
				} else if (!enumDef->rawValueType.empty()) {
					// Raw value type declared but no value specified - error
					addError("Raw value required for case '" + caseDef.name +
								 "' in enum '" + enumDef->name + "' with raw value type '" + enumDef->rawValueType + "'",
							 caseDef.line, caseDef.column);
				}

				enumInfo.cases.push_back(std::move(caseInfo));
			}

			enums.emplace(enumKey, std::move(enumInfo));

			// Also register simple name
			if (enums.find(enumDef->name) == enums.end()) {
				EnumInfo enumInfoSimple(enumDef->name, enumDef->line, enumDef->column, enumDef->rawValueType);
				enumInfoSimple.cases = enums[enumKey].cases;
				enumInfoSimple.hasAssociatedValues = enums[enumKey].hasAssociatedValues;
				enums.emplace(enumDef->name, std::move(enumInfoSimple));
			}
		}
	}

	// First pass: collect all struct definitions
	logTrace("Pass 1c: Collecting struct definitions");
	for (const auto &structDef : program->structs) {
		// Build the qualified name if the struct has a namespace and is exported
		std::string structKey = (structDef->isExported && !structDef->namespaceName.empty())
									? structDef->namespaceName + "." + structDef->name
									: structDef->name;

		logTrace("Registering struct: " + structKey);

		if (structs.find(structKey) != structs.end()) {
			addError("Struct '" + structKey + "' is already defined",
					 structDef->line, structDef->column);
		} else {
			// Convert StructField to StructFieldInfo
			std::vector<StructFieldInfo> fields;
			std::set<std::string> fieldNames;
			for (const auto &field : structDef->fields) {
				// Check for duplicate field names
				if (fieldNames.find(field.name) != fieldNames.end()) {
					addError("Duplicate field '" + field.name + "' in struct '" + structDef->name + "'",
							 field.line, field.column);
				} else {
					fieldNames.insert(field.name);

					std::string fieldType = field.type;
					bool hasDefault = (field.defaultValue != nullptr);

					// Type inference from default value
					if (fieldType.empty() && hasDefault) {
						fieldType = analyzeExpression(field.defaultValue.get());
						if (fieldType == "error") {
							addError("Cannot infer type for field '" + field.name + "' from default value",
									 field.line, field.column);
							fieldType = "error";
						}
					}

					// Type check: default value must match declared type
					if (!fieldType.empty() && hasDefault && fieldType != "error") {
						std::string defaultType = analyzeExpression(field.defaultValue.get());
						if (!typesMatch(fieldType, defaultType)) {
							addError("Default value type '" + defaultType +
										 "' does not match field type '" + fieldType + "' for field '" + field.name + "'",
									 field.line, field.column);
						}
					}

					// Get string representation of default value for display
					std::string defaultValueStr = hasDefault ? exprToString(field.defaultValue.get()) : "";

					fields.push_back(StructFieldInfo(field.name, fieldType, field.isImmutable,
													 hasDefault, defaultValueStr, field.line, field.column));
				}
			}

			// Build typeAssignments from interfaceTypeBindings by resolving positionally
			// against interface associated types
			std::map<std::string, std::string> resolvedTypeAssignments = structDef->typeAssignments;
			for (const auto &binding : structDef->interfaceTypeBindings) {
				const std::string &interfaceName = binding.first;
				const std::vector<std::string> &withTypes = binding.second;

				// Look up the interface
				auto protoIt = interfaces.find(interfaceName);
				if (protoIt != interfaces.end()) {
					const InterfaceInfo &interface = protoIt->second;
					// Map positionally: withTypes[i] -> associatedTypes[i]
					for (size_t i = 0; i < withTypes.size() && i < interface.associatedTypes.size(); i++) {
						const std::string &assocTypeName = interface.associatedTypes[i];
						const std::string &concreteType = withTypes[i];
						resolvedTypeAssignments[assocTypeName] = concreteType;
						logTrace("  Type binding: " + assocTypeName + " = " + concreteType +
								 " (from interface " + interfaceName + ")");
					}
					// Check for count mismatch
					if (withTypes.size() != interface.associatedTypes.size()) {
						addError("Interface '" + interfaceName + "' requires " +
									 std::to_string(interface.associatedTypes.size()) + " type(s) in 'with' clause, but got " +
									 std::to_string(withTypes.size()),
								 structDef->line, structDef->column);
					}
				}
				// If interface not found, will be caught later during conformance check
			}

			// Register with qualified name
			structs.emplace(structKey, StructInfo(structDef->name, fields,
												  structDef->line, structDef->column, structDef->conformsTo,
												  resolvedTypeAssignments));

			// Also register the simple name for use within the same file/namespace
			if (structs.find(structDef->name) == structs.end()) {
				structs.emplace(structDef->name, StructInfo(structDef->name, fields,
															structDef->line, structDef->column, structDef->conformsTo,
															resolvedTypeAssignments));
			}
		}
	}

	// Second pass: collect all function declarations and build function index map
	// This includes both top-level functions and methods declared inside structs
	logTrace("Pass 2: Collecting function declarations");
	functionIndices.clear(); // Reset indices for new analysis
	size_t nextFunctionId = 0;

	// First, register methods from struct definitions (inline methods)
	for (const auto &structDef : program->structs) {
		for (const auto &method : structDef->methods) {
			// Method key is StructName.methodName
			std::string methodKey = structDef->name + "." + method->name;

			logTrace("Registering method: " + methodKey + " -> " + method->returnType +
					 (method->implementsInterface.empty() ? "" : " (implements " + method->implementsInterface + ")"));

			// Validate implementsInterface if specified
			if (!method->implementsInterface.empty()) {
				// Check that the struct declares conformance to this interface
				bool conformsToInterface = false;
				for (const auto &iface : structDef->conformsTo) {
					if (iface == method->implementsInterface) {
						conformsToInterface = true;
						break;
					}
				}
				if (!conformsToInterface) {
					addError("Method '" + method->name + "' declares implementation of interface '" +
								 method->implementsInterface + "' but struct '" + structDef->name +
								 "' does not conform to this interface\n  Add '" + method->implementsInterface +
								 "' to the struct's 'is' clause",
							 method->line, method->column);
				}
			}

			if (functions.find(methodKey) != functions.end()) {
				addError("Method '" + methodKey + "' is already defined" +
							 std::string("\n  Note: Each method name must be unique within its struct"),
						 method->line, method->column);
			} else {
				functions.emplace(methodKey, FunctionInfo(methodKey, method->returnType, method->parameters, method->implementsInterface, method->line, method->column));
				functionIndices[methodKey] = nextFunctionId++;
			}
		}
	}

	// Register methods from enum definitions
	for (const auto &enumDef : program->enums) {
		for (const auto &method : enumDef->methods) {
			// Method key is EnumName.methodName
			std::string methodKey = enumDef->name + "." + method->name;

			logTrace("Registering enum method: " + methodKey + " -> " + method->returnType);

			if (functions.find(methodKey) != functions.end()) {
				addError("Method '" + methodKey + "' is already defined" +
							 std::string("\n  Note: Each method name must be unique within its enum"),
						 method->line, method->column);
			} else {
				functions.emplace(methodKey, FunctionInfo(methodKey, method->returnType, method->parameters, "", method->line, method->column));
				functionIndices[methodKey] = nextFunctionId++;
			}
		}
	}

	// Then register top-level functions
	for (const auto &func : program->functions) {
		// Build the function key: for methods use ReceiverType.methodName, otherwise use namespace.name
		std::string functionKey;
		if (func->isMethod()) {
			// Method: use ReceiverType.methodName (this path shouldn't be hit anymore,
			// but kept for backward compatibility with any edge cases)
			functionKey = func->receiverType + "." + func->name;
		} else {
			// Regular function: use namespace.name
			functionKey = func->namespaceName.empty() ? func->name : func->namespaceName + "." + func->name;
		}

		logTrace(std::string("Registering ") + (func->isMethod() ? "method" : "function") + ": " + functionKey + " -> " + func->returnType);

		if (functions.find(functionKey) != functions.end()) {
			addError("Function '" + functionKey + "' is already defined" +
						 std::string("\n  Note: Each function name must be unique in the program"),
					 func->line, func->column);
		} else {
			functions.emplace(functionKey, FunctionInfo(functionKey, func->returnType, func->parameters, "", func->line, func->column));
			functionIndices[functionKey] = nextFunctionId++;
		}

		// Also register the simple name if in global namespace (for backward compatibility)
		// But NOT for methods - they should only be accessible via Type.method
		if (!func->isMethod() && func->namespaceName.empty() && functions.find(func->name) == functions.end()) {
			functions.emplace(func->name, FunctionInfo(func->name, func->returnType, func->parameters, "", func->line, func->column));
			functionIndices[func->name] = functionIndices[functionKey]; // Same ID for both names
		}
	}

	// Pass 2b: Check interface conformance for all structs
	// Skip generic templates - conformance will be checked when instantiated
	logTrace("Pass 2b: Checking interface conformance");
	for (const auto &structDef : program->structs) {
		if (!structDef->associatedTypeParams.empty()) {
			logTrace("Skipping interface conformance check for generic template: " + structDef->name);
			continue;
		}
		if (!structDef->conformsTo.empty()) {
			checkInterfaceConformance(structDef->name, structDef->conformsTo, structDef->line, structDef->column);
		}
	}

	// Third pass: analyze each function and method body
	logTrace("Pass 3: Analyzing function bodies");

	// Analyze methods inside structs first
	// Skip generic template structs (those with associatedTypeParams) - they'll be analyzed when instantiated
	for (const auto &structDef : program->structs) {
		if (!structDef->associatedTypeParams.empty()) {
			logTrace("Skipping generic template struct methods: " + structDef->name);
			continue;
		}
		for (const auto &method : structDef->methods) {
			analyzeFunction(method.get());
		}
	}

	// Analyze methods inside enums
	for (const auto &enumDef : program->enums) {
		for (const auto &method : enumDef->methods) {
			analyzeFunction(method.get());
		}
	}

	// Then analyze top-level functions
	for (const auto &func : program->functions) {
		analyzeFunction(func.get());
	}

	logDetail("Analysis complete: " + std::to_string(structs.size()) + " structs, " +
			  std::to_string(enums.size()) + " enums, " +
			  std::to_string(functions.size()) + " functions, " +
			  std::to_string(errors.size()) + " error(s)");

	return errors;
}

void SemanticAnalyzer::analyzeFunction(FunctionAST *func) {
	// If this is an extern function, skip body analysis
	if (func->isExtern) {
		logTrace("Skipping extern function: " + func->name);
		return;
	}

	logTrace("Analyzing function body: " + func->name);

	// Set current receiver type for method field resolution (implicit self)
	currentReceiverType = func->receiverType;

	// Validate return type - track undefined struct types for auto-import
	if (func->returnType != "void" && func->returnType != "int" &&
		func->returnType != "float" && func->returnType != "bool" &&
		func->returnType != "string" && func->returnType != "cstring" &&
		func->returnType != "character" &&
		func->returnType[0] != '[') { // Not an array type
		// Could be a struct type - check if it exists
		if (lookupStruct(func->returnType) == nullptr) {
			undefinedStructs.insert(func->returnType);
		}
	}

	// Validate parameter types - track undefined struct types for auto-import
	for (const auto &param : func->parameters) {
		std::string paramType = param.type;
		// Strip array brackets if present
		if (paramType.size() > 2 && paramType[0] == '[') {
			size_t closeBracket = paramType.find(']');
			if (closeBracket != std::string::npos && closeBracket + 1 < paramType.size()) {
				paramType = paramType.substr(closeBracket + 1);
			}
		}
		if (paramType != "void" && paramType != "int" &&
			paramType != "float" && paramType != "bool" &&
			paramType != "string" && paramType != "cstring" &&
			paramType != "character") {
			// Could be a struct type - check if it exists
			if (lookupStruct(paramType) == nullptr) {
				undefinedStructs.insert(paramType);
			}
		}
	}

	// Initialize block ID tracking for this function
	blockIdStack.clear();
	blockIdStack.push_back(std::set<std::string>()); // Top-level block scope for the function

	// Enter function scope
	enterScope();

	// Declare parameters as variables
	for (const auto &param : func->parameters) {
		logTrace("  Parameter: " + param.name + " : " + param.type);
		declareVariable(param.name, param.type, false, param.line, param.column, true);
	}

	// Analyze function body
	for (const auto &stmt : func->body) {
		analyzeStatement(stmt.get(), func->returnType);
	}

	// Validate return statement (for non-void functions)
	if (func->returnType != "void") {
		if (!validateReturn(func)) {
			addError("Function '" + func->name + "' must return a value of type '" + func->returnType + "'" +
						 std::string("\n  Note: All execution paths through the function must end with a return statement"),
					 func->line, func->column);
		}
	}

	// Check for unused variables before exiting scope
	checkUnusedVariables();

	// Exit function scope
	exitScope();

	// Clear block ID stack for this function
	blockIdStack.clear();

	// Clear receiver type after analyzing method
	currentReceiverType.clear();
}

bool SemanticAnalyzer::validateReturn(FunctionAST *func) {
	return hasReturnInPath(func->body);
}

bool SemanticAnalyzer::hasReturnInPath(const std::vector<std::unique_ptr<StmtAST>> &statements) {
	for (const auto &stmt : statements) {
		// Direct return statement
		if (dynamic_cast<ReturnStmtAST *>(stmt.get())) {
			return true;
		}

		// If statement with return in both branches
		if (auto ifStmt = dynamic_cast<IfStmtAST *>(stmt.get())) {
			if (!ifStmt->elseBody.empty()) {
				bool thenHasReturn = hasReturnInPath(ifStmt->thenBody);
				bool elseHasReturn = hasReturnInPath(ifStmt->elseBody);
				if (thenHasReturn && elseHasReturn) {
					return true;
				}
			}
		}

		// If-let statement with return in both branches
		if (auto ifLet = dynamic_cast<IfLetStmtAST *>(stmt.get())) {
			if (!ifLet->elseBody.empty()) {
				bool thenHasReturn = hasReturnInPath(ifLet->thenBody);
				bool elseHasReturn = hasReturnInPath(ifLet->elseBody);
				if (thenHasReturn && elseHasReturn) {
					return true;
				}
			}
		}
	}

	return false;
}

void SemanticAnalyzer::enterScope() {
	scopeStack.push_back(variables);
	// Note: DO NOT push a new blockIdStack here - we manage it separately
}

void SemanticAnalyzer::exitScope() {
	if (!scopeStack.empty()) {
		// Before restoring parent scope, preserve usage information for parent scope variables
		auto childVariables = variables;
		variables = scopeStack.back();
		scopeStack.pop_back();

		// Propagate "isUsed" flag from child scope to parent scope for shared variables
		for (auto &parentVar : variables) {
			auto childIt = childVariables.find(parentVar.first);
			if (childIt != childVariables.end() && childIt->second.isUsed) {
				parentVar.second.isUsed = true;
			}
		}
	}
}

void SemanticAnalyzer::declareBlockId(const std::string &blockId, int line, int column) {
	// Skip empty block identifiers (for single-line if statements)
	if (blockId.empty()) {
		return;
	}

	if (!blockIdStack.empty()) {
		std::set<std::string> &currentBlockIds = blockIdStack.back();
		if (currentBlockIds.find(blockId) != currentBlockIds.end()) {
			addError("Duplicate block identifier '" + blockId + "' in nested blocks",
					 line, column);
		} else {
			currentBlockIds.insert(blockId);
		}
	}
}

void SemanticAnalyzer::declareVariable(const std::string &name, const std::string &type, bool isImmutable, int line, int column, bool isParameter, const std::string &initialValue, bool isLoopVariable) {
	VariableInfo varInfo(name, type, isImmutable, line, column, isParameter, initialValue, isLoopVariable);
	variables[name] = varInfo;
	// Also store in persistent symbol table for LSP
	allDeclaredVariables[name] = varInfo;
}

// Helper function to extract literal value from an expression for display

std::optional<VariableInfo> SemanticAnalyzer::lookupVariable(const std::string &name) {
	// Check current scope
	auto it = variables.find(name);
	if (it != variables.end()) {
		return it->second;
	}

	// Check parent scopes
	for (auto rit = scopeStack.rbegin(); rit != scopeStack.rend(); ++rit) {
		auto it = rit->find(name);
		if (it != rit->end()) {
			return it->second;
		}
	}

	// If we're in a method (have a receiver type), check struct fields
	// This enables implicit self field access: 'count' instead of 'self.count'
	if (!currentReceiverType.empty()) {
		auto structIt = structs.find(currentReceiverType);
		if (structIt != structs.end()) {
			for (const auto &field : structIt->second.fields) {
				if (field.name == name) {
					// Return field as if it were a variable
					// Mark as used through 'self' parameter
					markVariableAsUsed("self");
					// Return field type - this is a synthetic variable reference
					return VariableInfo(name, field.type, false, field.line, field.column, false);
				}
			}
		}
	}

	return std::nullopt;
}

bool SemanticAnalyzer::typesMatch(const std::string &type1, const std::string &type2) {
	// Delegate to centralized type conversion module
	return maxon::TypeConversion::typesMatch(type1, type2);
}

bool SemanticAnalyzer::isOptionalType(const std::string &type) {
	return maxon::TypeConversion::isOptionalType(type);
}

std::string SemanticAnalyzer::unwrapOptionalType(const std::string &type) {
	return maxon::TypeConversion::unwrapOptionalType(type);
}

std::string SemanticAnalyzer::makeOptionalType(const std::string &type) {
	return maxon::TypeConversion::makeOptionalType(type);
}

bool SemanticAnalyzer::isIterableType(const std::string &type, ExprAST *iterableExpr) {
	// Error type is always iterable (avoid cascading errors)
	if (type == "error") {
		return true;
	}

	// Array types are iterable: [N]T (fixed-size) or []T (dynamic/slice)
	if (type.size() > 2 && type[0] == '[') {
		return true;
	}

	// String type is iterable (iterates over characters)
	if (type == "string") {
		return true;
	}

	// Check for range() call - returns Iterator which is iterable
	// The Iterator struct from stdlib is iterable
	if (type == "Iterator") {
		return true;
	}

	// Check if this is a struct that conforms to Sequence interface
	auto structIt = structs.find(type);
	if (structIt != structs.end()) {
		for (const auto &iface : structIt->second.conformsTo) {
			if (iface == "Iterable") {
				return true;
			}
		}
		// Structs with an Element associated type are considered iterable
		if (structIt->second.typeAssignments.find("Element") != structIt->second.typeAssignments.end()) {
			return true;
		}
	}

	// Non-iterable types: int, float, bool, char, byte, void, pointers, etc.
	return false;
}

void SemanticAnalyzer::addError(const std::string &message, int line, int column, const std::string &errCode) {
	errors.emplace_back(message, line, column, 1, errCode); // Severity 1 = Error
}

void SemanticAnalyzer::addWarning(const std::string &message, int line, int column, const std::string &errCode) {
	errors.emplace_back(message, line, column, 2, errCode); // Severity 2 = Warning
}

void SemanticAnalyzer::markVariableAsUsed(const std::string &name) {
	// Check current scope
	auto it = variables.find(name);
	if (it != variables.end()) {
		it->second.isUsed = true;
		return;
	}

	// Check parent scopes
	for (auto &scope : scopeStack) {
		auto it = scope.find(name);
		if (it != scope.end()) {
			it->second.isUsed = true;
			return;
		}
	}
}

void SemanticAnalyzer::checkUnusedVariables() {
	// Get the parent scope (if any) to check which variables are inherited vs declared locally
	std::map<std::string, VariableInfo> *parentScope = nullptr;
	if (!scopeStack.empty()) {
		parentScope = &scopeStack.back();
	}

	// Check all variables in current scope
	for (const auto &pair : variables) {
		const VariableInfo &varInfo = pair.second;

		// Skip variables that were inherited from parent scope (not declared in this scope)
		// A variable is inherited if it exists in the parent scope
		if (parentScope && parentScope->find(pair.first) != parentScope->end()) {
			continue;
		}

		if (!varInfo.isUsed) {
			// Skip unused 'self' parameters - they're auto-injected and may not be used
			// in static factory methods like init
			if (varInfo.isParameter && varInfo.name == "self") {
				continue;
			}

			if (varInfo.isParameter) {
				addWarning("The parameter '" + varInfo.name + "' is declared but its value is never used",
						   varInfo.line, varInfo.column, "unused-parameter");
			} else {
				addWarning("The variable '" + varInfo.name + "' is assigned but its value is never used",
						   varInfo.line, varInfo.column, "unused-variable");
			}
		}
	}
}

std::map<std::string, VariableInfo> SemanticAnalyzer::getAllVariables() const {
	// Return the persistent symbol table that contains all declared variables
	return allDeclaredVariables;
}

void SemanticAnalyzer::checkInterfaceConformance(const std::string &structName,
												 const std::vector<std::string> &conformsTo,
												 int line, int column) {
	// Skip conformance checking for 'string' - its methods are compiler-intrinsic
	// The compiler generates calls to runtime functions (__string_count, __string_print, etc.)
	if (structName == "string") {
		logTrace("Skipping interface conformance check for built-in 'string' type");
		return;
	}

	// Get the struct's type assignments for resolving associated types
	auto structIt = structs.find(structName);
	const std::map<std::string, std::string> *typeAssignments = nullptr;
	if (structIt != structs.end()) {
		typeAssignments = &structIt->second.typeAssignments;
	}

	// Helper lambda to resolve associated types in a type string
	auto resolveType = [&](const std::string &type) -> std::string {
		if (type == "Self") {
			return structName;
		}

		// Handle optional types (e.g., "Element or nil" -> "int or nil")
		if (maxon::TypeConversion::isOptionalType(type)) {
			std::string baseType = maxon::TypeConversion::unwrapOptionalType(type);
			// Recursively resolve the base type
			std::string resolvedBase = [&]() -> std::string {
				if (baseType == "Self") {
					return structName;
				}
				if (typeAssignments) {
					auto assignIt = typeAssignments->find(baseType);
					if (assignIt != typeAssignments->end()) {
						return assignIt->second;
					}
				}
				return baseType;
			}();
			return maxon::TypeConversion::makeOptionalType(resolvedBase);
		}

		// Check if this type name is an associated type
		if (typeAssignments) {
			auto assignIt = typeAssignments->find(type);
			if (assignIt != typeAssignments->end()) {
				return assignIt->second;
			}
		}
		return type;
	};

	// Collect all missing methods for partial implementation error
	std::vector<std::string> missingMethods;

	for (const auto &interfaceName : conformsTo) {
		// Find the interface
		auto protoIt = interfaces.find(interfaceName);
		if (protoIt == interfaces.end()) {
			// Track as undefined for auto-discovery from stdlib
			undefinedInterfaces.insert(interfaceName);
			logTrace("Interface '" + interfaceName + "' not found, marking for auto-discovery");
			continue;
		}

		const InterfaceInfo &interface = protoIt->second;
		logTrace("Checking conformance of " + structName + " to " + interfaceName);

		// First, check that all associated types are defined
		for (const auto &assocType : interface.associatedTypes) {
			if (!typeAssignments || typeAssignments->find(assocType) == typeAssignments->end()) {
				addError("Struct '" + structName + "' does not define required associated type '" + assocType +
							 "' from interface '" + interfaceName + "'",
						 line, column);
			}
		}

		// Check each method in the interface
		for (const auto &protoMethod : interface.methods) {
			// Build expected method name: StructName.methodName
			std::string expectedMethodName = structName + "." + protoMethod.name;

			auto funcIt = functions.find(expectedMethodName);
			if (funcIt == functions.end()) {
				// Build parameter string for error message (without implicit self)
				std::string paramStr;
				for (size_t i = 0; i < protoMethod.parameters.size(); i++) {
					if (i > 0)
						paramStr += ", ";
					std::string paramType = resolveType(protoMethod.parameters[i].type);
					paramStr += protoMethod.parameters[i].name + " " + paramType;
				}
				std::string returnType = resolveType(protoMethod.returnType);

				missingMethods.push_back(protoMethod.name + "(" + paramStr + ") " + returnType);
				continue;
			}

			// Check that the method has the required interface prefix
			const FunctionInfo &implFunc = funcIt->second;
			if (implFunc.implementsInterface != interfaceName) {
				addError("Method '" + protoMethod.name + "' implements interface '" + interfaceName +
							 "' but is missing the required prefix\n  Use: function " + interfaceName + "." +
							 protoMethod.name + "(...) instead of: function " + protoMethod.name + "(...)",
						 line, column);
			}

			// Check return type (substituting Self and associated types)
			std::string expectedReturnType = resolveType(protoMethod.returnType);
			if (implFunc.returnType != expectedReturnType) {
				addError("Method '" + expectedMethodName + "' has return type '" + implFunc.returnType +
							 "' but interface '" + interfaceName + "' requires '" + expectedReturnType + "'",
						 line, column);
			}

			// Check parameter count - impl has implicit 'self' as first param (+1)
			// Interface params don't include self (it's implicit)
			size_t expectedParamCount = protoMethod.parameters.size() + 1; // +1 for implicit self
			if (implFunc.parameters.size() != expectedParamCount) {
				addError("Method '" + expectedMethodName + "' has " + std::to_string(implFunc.parameters.size() - 1) +
							 " explicit parameter(s) but interface '" + interfaceName + "' requires " +
							 std::to_string(protoMethod.parameters.size()),
						 line, column);
				continue;
			}

			// Verify first param is 'self' with correct type
			if (implFunc.parameters.empty() || implFunc.parameters[0].name != "self") {
				addError("Method '" + expectedMethodName + "' is missing implicit 'self' parameter",
						 line, column);
				continue;
			}
			if (implFunc.parameters[0].type != structName) {
				addError("Method '" + expectedMethodName + "' has self type '" + implFunc.parameters[0].type +
							 "' but expected '" + structName + "'",
						 line, column);
			}

			// Check parameter types (skip first param which is implicit self)
			for (size_t i = 0; i < protoMethod.parameters.size(); i++) {
				std::string expectedParamType = resolveType(protoMethod.parameters[i].type);
				// implFunc params are offset by 1 due to implicit self
				if (implFunc.parameters[i + 1].type != expectedParamType) {
					addError("Method '" + expectedMethodName + "' parameter " + std::to_string(i + 1) +
								 " has type '" + implFunc.parameters[i + 1].type +
								 "' but interface '" + interfaceName + "' requires '" + expectedParamType + "'",
							 line, column);
				}
			}
		}
	}

	// Report all missing methods at once for partial implementation error
	if (!missingMethods.empty()) {
		std::string missingList;
		for (size_t i = 0; i < missingMethods.size(); i++) {
			if (i > 0)
				missingList += "\n  - ";
			else
				missingList += "  - ";
			missingList += missingMethods[i];
		}
		addError("Partial interface implementation: struct '" + structName + "' is missing " +
					 std::to_string(missingMethods.size()) + " method(s):\n" + missingList,
				 line, column);
	}
}

void SemanticAnalyzer::registerMapMethods(const std::string &mapType, const std::string &keyType, const std::string &valueType) {
	// Only register methods once per map type
	// Use qualified method names: mapType.methodName (e.g., "map<int,int>.insert")
	std::string insertMethodName = mapType + ".insert";
	if (functions.find(insertMethodName) != functions.end()) {
		return; // Already registered for this map type
	}

	// Register map methods with qualified names for proper method resolution
	// insert(map, key, value) -> void
	functions.emplace(mapType + ".insert", FunctionInfo(mapType + ".insert", "void", {FunctionParameter("self", mapType), FunctionParameter("key", keyType), FunctionParameter("value", valueType)}));

	// get(map, key) -> valueType
	functions.emplace(mapType + ".get", FunctionInfo(mapType + ".get", valueType, {FunctionParameter("self", mapType), FunctionParameter("key", keyType)}));

	// contains(map, key) -> bool
	functions.emplace(mapType + ".contains", FunctionInfo(mapType + ".contains", "bool", {FunctionParameter("self", mapType), FunctionParameter("key", keyType)}));

	// remove(map, key) -> bool
	functions.emplace(mapType + ".remove", FunctionInfo(mapType + ".remove", "bool", {FunctionParameter("self", mapType), FunctionParameter("key", keyType)}));

	// count(map) -> int
	functions.emplace(mapType + ".count", FunctionInfo(mapType + ".count", "int", {FunctionParameter("self", mapType)}));

	// capacity(map) -> int
	functions.emplace(mapType + ".capacity", FunctionInfo(mapType + ".capacity", "int", {FunctionParameter("self", mapType)}));
}
