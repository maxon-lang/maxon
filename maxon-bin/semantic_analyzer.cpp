#include "semantic_analyzer.h"
#include "call_graph.h"
#include "compiler_api.h"
#include "lexer.h"
#include "types/type_conversion.h"
#include <algorithm>
#include <functional>
#include <queue>
#include <sstream>

// Helper to collect global constant references from an expression
static void collectGlobalRefs(ExprAST *expr, const std::map<std::string, GlobalConstInfo> &globals,
							  std::vector<std::string> &refs) {
	if (!expr)
		return;

	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		if (globals.find(varExpr->name) != globals.end()) {
			refs.push_back(varExpr->name);
		}
	} else if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		collectGlobalRefs(binExpr->left.get(), globals, refs);
		collectGlobalRefs(binExpr->right.get(), globals, refs);
	} else if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		collectGlobalRefs(unaryExpr->operand.get(), globals, refs);
	} else if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		collectGlobalRefs(castExpr->expr.get(), globals, refs);
	} else if (auto *arrayLit = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		for (const auto &elem : arrayLit->values) {
			collectGlobalRefs(elem.get(), globals, refs);
		}
	} else if (auto *mapLit = dynamic_cast<MapLiteralWithEntriesExprAST *>(expr)) {
		for (const auto &entry : mapLit->entries) {
			collectGlobalRefs(entry.key.get(), globals, refs);
			collectGlobalRefs(entry.value.get(), globals, refs);
		}
	}
	// MemberAccessExprAST (enum cases) don't reference other global constants
}

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

// Template helper for looking up types (structs, enums) by name with namespace fallback
// Works with both std::map and std::unordered_map
template <typename MapType>
static auto lookupByName(const MapType &collection, const std::string &name)
	-> const typename MapType::mapped_type * {
	// First try exact match
	auto it = collection.find(name);
	if (it != collection.end()) {
		return &it->second;
	}

	// If not found and name contains a dot, it's already qualified
	if (name.find('.') != std::string::npos) {
		return nullptr;
	}

	// For unqualified names, also try qualified lookups with all known namespaces
	for (const auto &pair : collection) {
		const std::string &qualifiedName = pair.first;
		// Check if this is a qualified name ending with the requested name
		if (qualifiedName.size() > name.size() + 1 &&
			qualifiedName.substr(qualifiedName.size() - name.size()) == name &&
			qualifiedName[qualifiedName.size() - name.size() - 1] == '.') {
			return &pair.second;
		}
	}

	return nullptr;
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
	return lookupByName(structs, name);
}

const EnumInfo *SemanticAnalyzer::lookupEnum(const std::string &name) const {
	return lookupByName(enums, name);
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

void SemanticAnalyzer::registerExternalInterface(const std::string &name,
												 const std::vector<InterfaceMethodInfo> &methods,
												 const std::vector<std::string> &associatedTypes,
												 const std::string &extendsInterface) {
	// Only register if not already defined
	if (interfaces.find(name) == interfaces.end()) {
		InterfaceInfo ifaceInfo(name, 0, 0, associatedTypes, extendsInterface);
		for (const auto &method : methods) {
			ifaceInfo.methods.push_back(method);
		}
		interfaces.emplace(name, std::move(ifaceInfo));
		logTrace("Registered external interface: " + name +
				 (extendsInterface.empty() ? "" : " extends " + extendsInterface));
	}
}

void SemanticAnalyzer::registerExternalEnum(const std::string &name, const std::string &rawValueType) {
	// Only register if not already defined
	if (enums.find(name) == enums.end()) {
		EnumInfo enumInfo(name, 0, 0, rawValueType);
		enums.emplace(name, std::move(enumInfo));
		logTrace("Registered external enum: " + name);
	}
}

void SemanticAnalyzer::registerExternalEnum(const std::string &name, const std::string &rawValueType,
											const std::vector<EnumCaseInfo> &cases) {
	// Only register if not already defined
	if (enums.find(name) == enums.end()) {
		EnumInfo enumInfo(name, 0, 0, rawValueType);
		enumInfo.cases = cases;
		// Check if any case has associated values
		for (const auto &c : cases) {
			if (!c.associatedValues.empty()) {
				enumInfo.hasAssociatedValues = true;
				break;
			}
		}
		enums.emplace(name, std::move(enumInfo));
		logTrace("Registered external enum with cases: " + name + " (" + std::to_string(cases.size()) + " cases)");
	}
}

void SemanticAnalyzer::registerBuiltinFunctions() {
	// Register hash and equals methods for primitive hashable types
	// These are implemented as intrinsics in codegen

	// int.hash() -> int (multiplicative hash)
	functions.emplace("int.hash", FunctionInfo("int.hash", "int", {FunctionParameter("self", "int", 0, 0)}));
	// int.equals(other) -> bool
	functions.emplace("int.equals", FunctionInfo("int.equals", "bool", {FunctionParameter("self", "int", 0, 0), FunctionParameter("other", "int", 0, 0)}));

	// byte.hash() -> int
	functions.emplace("byte.hash", FunctionInfo("byte.hash", "int", {FunctionParameter("self", "byte", 0, 0)}));
	// byte.equals(other) -> bool
	functions.emplace("byte.equals", FunctionInfo("byte.equals", "bool", {FunctionParameter("self", "byte", 0, 0), FunctionParameter("other", "byte", 0, 0)}));

	// Note: char.hash() and char.equals() are now defined in stdlib/string/char.maxon
	// since char is a stdlib struct type (grapheme cluster), not a primitive
}

void SemanticAnalyzer::setSourceContext(const std::string &source, const std::string &filePath) {
	sourceContent_ = source;
	currentFilePath_ = filePath;
}
std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST *program) {
	errors.clear();
	// Note: Don't clear functions, structs, or interfaces here - we want to keep registered external ones
	// structs.clear(); // Preserve external structs registered before analysis
	// interfaces.clear(); // Preserve external interfaces registered before analysis
	globalConstants.clear(); // Clear global constants for new analysis
	variables.clear();
	scopeStack.clear();
	loopDepth = 0;
	blockIdStack.clear(); // Clear block ID stack for new analysis
	undefinedFunctions.clear();
	undefinedStructs.clear();
	undefinedInterfaces.clear();
	allDeclaredVariables.clear(); // Clear persistent symbol table
	currentProgram = program;	  // Store for generic struct method instantiation

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
									interfaceDef->associatedTypes, interfaceDef->extendsInterface);
			for (const auto &method : interfaceDef->methods) {
				const std::vector<std::unique_ptr<StmtAST>> *bodyPtr =
					method.hasDefaultImplementation ? &method.defaultBody : nullptr;
				protoInfo.methods.push_back(InterfaceMethodInfo(method.name, method.returnType, method.parameters,
																method.hasDefaultImplementation, bodyPtr));
			}
			interfaces.emplace(interfaceKey, std::move(protoInfo));

			// Also register simple name
			if (interfaces.find(interfaceDef->name) == interfaces.end()) {
				InterfaceInfo protoInfoSimple(interfaceDef->name, interfaceDef->line, interfaceDef->column,
											  interfaceDef->associatedTypes, interfaceDef->extendsInterface);
				for (const auto &method : interfaceDef->methods) {
					const std::vector<std::unique_ptr<StmtAST>> *bodyPtr =
						method.hasDefaultImplementation ? &method.defaultBody : nullptr;
					protoInfoSimple.methods.push_back(InterfaceMethodInfo(method.name, method.returnType, method.parameters,
																		  method.hasDefaultImplementation, bodyPtr));
				}
				interfaces.emplace(interfaceDef->name, std::move(protoInfoSimple));
			}
		}
	}

	// Validate that extended interfaces exist
	for (const auto &interfaceDef : program->interfaces) {
		if (!interfaceDef->extendsInterface.empty()) {
			if (interfaces.find(interfaceDef->extendsInterface) == interfaces.end()) {
				addError("Interface '" + interfaceDef->name + "' extends unknown interface '" +
							 interfaceDef->extendsInterface + "'",
						 interfaceDef->line, interfaceDef->column);
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
																	  "  Declare the enum with a raw value type: enum " +
									 enumDef->name + " int",
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
			addError("Type '" + structKey + "' is already defined",
					 structDef->line, structDef->column);
		} else {
			// Convert StructField to StructFieldInfo
			std::vector<StructFieldInfo> fields;
			std::set<std::string> fieldNames;
			for (const auto &field : structDef->fields) {
				// Check for duplicate field names
				if (fieldNames.find(field.name) != fieldNames.end()) {
					addError("Duplicate field '" + field.name + "' in type '" + structDef->name + "'",
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
				// Check that the struct declares conformance to this interface (directly or transitively)
				bool conformsToInterface = false;

				// Helper to extract base interface name (strip " with ..." type parameters)
				auto getBaseInterfaceName = [](const std::string &ifaceName) -> std::string {
					size_t withPos = ifaceName.find(" with ");
					if (withPos != std::string::npos) {
						return ifaceName.substr(0, withPos);
					}
					return ifaceName;
				};

				// Helper to check if an interface extends the target interface (recursive)
				std::function<bool(const std::string &)> checkExtendsInterface = [&](const std::string &ifaceName) -> bool {
					std::string baseName = getBaseInterfaceName(ifaceName);
					if (baseName == method->implementsInterface) {
						return true;
					}
					auto it = interfaces.find(baseName);
					if (it != interfaces.end() && !it->second.extendsInterface.empty()) {
						return checkExtendsInterface(it->second.extendsInterface);
					}
					return false;
				};

				for (const auto &iface : structDef->conformsTo) {
					if (checkExtendsInterface(iface)) {
						conformsToInterface = true;
						break;
					}
				}
				if (!conformsToInterface) {
					addError("Method '" + method->name + "' declares implementation of interface '" +
								 method->implementsInterface + "' but type '" + structDef->name +
								 "' does not conform to this interface\n  Add '" + method->implementsInterface +
								 "' to the type's 'is' clause",
							 method->line, method->column);
				}
			}

			if (functions.find(methodKey) != functions.end()) {
				addError("Method '" + methodKey + "' is already defined" +
							 std::string("\n  Note: Each method name must be unique within its type"),
						 method->line, method->column);
			} else {
				functions.emplace(methodKey, FunctionInfo(methodKey, method->returnType, method->parameters, method->implementsInterface, method->line, method->column, method->isStaticMethod));
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
				functions.emplace(methodKey, FunctionInfo(methodKey, method->returnType, method->parameters, "", method->line, method->column, method->isStaticMethod));
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
	// Generic templates are also checked to ensure all required methods are defined
	logTrace("Pass 2b: Checking interface conformance");
	for (const auto &structDef : program->structs) {
		if (!structDef->conformsTo.empty()) {
			bool isGenericTemplate = !structDef->associatedTypeParams.empty();
			checkInterfaceConformance(structDef->name, structDef->conformsTo, structDef->line, structDef->column, isGenericTemplate);
		}
	}

	// Pass 2c: Register all global constants (names only, for forward reference support)
	logTrace("Pass 2c: Registering global constants");
	for (const auto &global : program->globals) {
		std::string globalKey = (global->isExported && !global->name.empty())
									? global->name // TODO: support namespace prefix for exports
									: global->name;

		logTrace("Registering global constant: " + globalKey);
		if (globalConstants.find(globalKey) != globalConstants.end()) {
			addError("Global constant '" + globalKey + "' is already defined",
					 global->line, global->column);
		} else {
			// Type will be filled in during evaluation
			globalConstants.emplace(globalKey, GlobalConstInfo(global->name, "", global->isExported, global->line, global->column));
		}
	}

	// Pass 2d: Analyze global constant initializers and infer types
	// We need to evaluate in dependency order to handle forward references
	logTrace("Pass 2d: Analyzing global constant initializers");

	// Build dependency graph for topological sort
	std::unordered_map<std::string, std::vector<std::string>> dependencies;
	std::unordered_map<std::string, int> inDegree;
	std::unordered_map<std::string, GlobalLetDeclAST *> globalMap;

	for (const auto &global : program->globals) {
		if (globalConstants.find(global->name) == globalConstants.end()) {
			continue; // Skip duplicates
		}
		globalMap[global->name] = global.get();
		inDegree[global->name] = 0;
		dependencies[global->name] = {};
	}

	// Collect dependencies for each global
	for (const auto &global : program->globals) {
		if (globalMap.find(global->name) == globalMap.end())
			continue;

		std::vector<std::string> refs;
		collectGlobalRefs(global->initializer.get(), globalConstants, refs);
		for (const auto &ref : refs) {
			if (globalMap.find(ref) != globalMap.end() && ref != global->name) {
				dependencies[ref].push_back(global->name);
				inDegree[global->name]++;
			}
		}
	}

	// Kahn's algorithm for topological sort
	std::queue<std::string> ready;
	for (const auto &[name, degree] : inDegree) {
		if (degree == 0) {
			ready.push(name);
		}
	}

	std::vector<std::string> sortedGlobals;
	while (!ready.empty()) {
		std::string current = ready.front();
		ready.pop();
		sortedGlobals.push_back(current);

		for (const auto &dependent : dependencies[current]) {
			if (--inDegree[dependent] == 0) {
				ready.push(dependent);
			}
		}
	}

	// Check for circular dependencies
	if (sortedGlobals.size() != globalMap.size()) {
		std::vector<std::string> cycle;
		for (const auto &[name, degree] : inDegree) {
			if (degree > 0)
				cycle.push_back(name);
		}
		std::string cycleStr;
		for (size_t i = 0; i < cycle.size(); ++i) {
			if (i > 0)
				cycleStr += ", ";
			cycleStr += cycle[i];
		}
		addError("Circular dependency detected among global constants: " + cycleStr,
				 globalMap.begin()->second->line, globalMap.begin()->second->column);
	} else {
		// Analyze globals in dependency order
		for (const auto &name : sortedGlobals) {
			auto *global = globalMap[name];

			// Analyze the initializer expression to get its type
			std::string initType = analyzeExpression(global->initializer.get());
			if (initType == "error") {
				addError("Cannot determine type of global constant '" + global->name + "'",
						 global->line, global->column);
				continue;
			}

			// Check that initializer is a constant expression
			auto *expr = global->initializer.get();
			std::string nonConstReason;
			bool isConstant = isConstantExpression(expr, nonConstReason);

			if (!isConstant) {
				std::string msg = "Global constant '" + global->name + "' initializer must be a constant expression";
				if (!nonConstReason.empty()) {
					msg += "\n  " + nonConstReason;
				}
				addError(msg, global->line, global->column);
				continue;
			}

			// Update the type in globalConstants
			globalConstants[global->name].type = initType;
			logTrace("  " + global->name + " : " + initType);
		}
	}

	// Third pass: analyze each function and method body
	logTrace("Pass 3: Analyzing function bodies");

	// Analyze methods inside structs first
	// Skip generic template structs (those with associatedTypeParams) - they'll be analyzed when instantiated
	// BUT still check for doc comments on exported methods
	for (const auto &structDef : program->structs) {
		bool isGenericTemplate = !structDef->associatedTypeParams.empty();

		// Check doc comments on exported methods even for generic templates
		for (const auto &method : structDef->methods) {
			if (method->isExported && !sourceContent_.empty() &&
				currentFilePath_.find("stdlib") != std::string::npos &&
				!method->name.empty() && method->name[0] != '_') {
				std::string docComment = extractDocComment(sourceContent_, method->line);
				if (docComment.empty()) {
					addError("Exported stdlib function '" + method->name + "' is missing a doc comment (///)",
							 method->line, method->column);
				}
			}
		}

		if (isGenericTemplate) {
			// Even though we skip full analysis, scan method bodies for function calls
			// so we can discover stdlib dependencies for auto-importing
			for (const auto &method : structDef->methods) {
				std::set<std::string> calls = CallGraphBuilder::extractCallsFromFunction(method.get());
				for (const auto &callee : calls) {
					// Only mark as undefined if we don't already know this function
					// Skip method calls on self (they'll have a dot) and built-in intrinsics
					if (callee.find('.') == std::string::npos &&
						functions.find(callee) == functions.end()) {
						logTrace("  Generic method " + structDef->name + "." + method->name +
								 " calls undefined function: " + callee);
						undefinedFunctions.insert(callee);
					}
				}
			}
			continue;
		}
		for (const auto &method : structDef->methods) {
			currentFunctionName_ = structDef->name + "." + method->name;
			analyzeFunction(method.get());
			currentFunctionName_.clear();
		}
	}

	// Analyze methods inside enums
	for (const auto &enumDef : program->enums) {
		for (const auto &method : enumDef->methods) {
			currentFunctionName_ = enumDef->name + "." + method->name;
			analyzeFunction(method.get());
			currentFunctionName_.clear();
		}
	}

	// Then analyze top-level functions
	for (const auto &func : program->functions) {
		// Build function key: namespace.name or just name
		if (!func->namespaceName.empty()) {
			currentFunctionName_ = func->namespaceName + "." + func->name;
		} else {
			currentFunctionName_ = func->name;
		}
		analyzeFunction(func.get());
		currentFunctionName_.clear();
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

	// Check for missing doc comments on exported functions in stdlib files
	// Skip internal functions (starting with _) as they are implementation details
	if (func->isExported && !sourceContent_.empty() &&
		currentFilePath_.find("stdlib") != std::string::npos &&
		!func->name.empty() && func->name[0] != '_') {
		std::string docComment = extractDocComment(sourceContent_, func->line);
		if (docComment.empty()) {
			addError("Exported stdlib function '" + func->name + "' is missing a doc comment (///)",
					 func->line, func->column);
		}
	}

	logTrace("Analyzing function body: " + func->name); // Set current receiver type for method field resolution (implicit self)
	currentReceiverType = func->receiverType;

	// Validate return type - track undefined struct types for auto-import
	if (func->returnType != "void" && func->returnType != "int" &&
		func->returnType != "float" && func->returnType != "bool" &&
		func->returnType != "string" && func->returnType != "cstring" &&
		func->returnType != "character" &&
		!maxon::TypeConversion::isArrayType(func->returnType) &&
		!maxon::TypeConversion::isFunctionType(func->returnType)) { // Not an array or function type
		// Could be a struct type - check if it exists
		if (lookupStruct(func->returnType) == nullptr) {
			undefinedStructs.insert(func->returnType);
		}
	}

	// Validate parameter types - track undefined struct types for auto-import
	for (const auto &param : func->parameters) {
		std::string paramType = param.type;
		// Strip array type if present - get element type
		if (maxon::TypeConversion::isArrayType(paramType)) {
			paramType = maxon::TypeConversion::getArrayElementType(paramType);
		}
		// Handle array<T> struct type - need to import 'array' template
		else if (maxon::TypeConversion::isArrayStructType(paramType)) {
			// Add 'array' to undefined structs so stdlib/collections/array.maxon is imported
			if (lookupStruct("array") == nullptr) {
				undefinedStructs.insert("array");
			}
			// Instantiate generic struct methods including synthesized defaults
			std::string elemType = maxon::TypeConversion::getArrayStructElementType(param.type);
			std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
			instantiateGenericStructMethods("array", param.type, typeBindings);
			// Continue to check the element type
			paramType = elemType;
		}
		// Check if this is a struct type that needs to be imported
		// Note: 'string' is a stdlib struct, not a primitive type
		if (paramType != "void" && paramType != "int" &&
			paramType != "float" && paramType != "bool" &&
			paramType != "cstring" &&
			paramType != "character" && paramType != "byte") {
			// Function types are valid parameter types
			if (maxon::TypeConversion::isFunctionType(paramType)) {
				continue;
			}
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

		// Match statement with return in all cases
		if (auto matchStmt = dynamic_cast<MatchStmtAST *>(stmt.get())) {
			bool allCasesReturn = true;

			for (const auto &matchCase : matchStmt->cases) {
				// Check if this case returns
				if (matchCase.statement) {
					if (!dynamic_cast<ReturnStmtAST *>(matchCase.statement.get())) {
						allCasesReturn = false;
					}
				} else {
					allCasesReturn = false;
				}
			}

			// If the match is exhaustive (has default OR covers all enum cases) and all cases return
			if (matchStmt->isExhaustive && allCasesReturn) {
				return true;
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
	// Also store in persistent symbol table for LSP with function-qualified key
	// This prevents collisions when same variable name appears in different functions
	std::string qualifiedKey = currentFunctionName_.empty() ? name : currentFunctionName_ + "::" + name;
	allDeclaredVariables[qualifiedKey] = varInfo;
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

	// Check global constants
	auto globalIt = globalConstants.find(name);
	if (globalIt != globalConstants.end()) {
		// Global constants are always immutable
		return VariableInfo(name, globalIt->second.type, true, globalIt->second.line, globalIt->second.column, false);
	}

	return std::nullopt;
}

bool SemanticAnalyzer::typesMatch(const std::string &type1, const std::string &type2) {
	// Delegate to centralized type conversion module
	return maxon::TypeConversion::typesMatch(type1, type2);
}

bool SemanticAnalyzer::isOptionalType(const std::string &type) const {
	return maxon::TypeConversion::isOptionalType(type);
}

std::string SemanticAnalyzer::unwrapOptionalType(const std::string &type) const {
	return maxon::TypeConversion::unwrapOptionalType(type);
}

std::string SemanticAnalyzer::makeOptionalType(const std::string &type) const {
	return maxon::TypeConversion::makeOptionalType(type);
}

bool SemanticAnalyzer::typeHasMethod(const std::string &typeName, const std::string &methodName,
									 const std::string &returnType,
									 const std::vector<std::string> &paramTypes) const {
	// Check if there's a function registered for Type.methodName
	std::string funcKey = typeName + "." + methodName;
	auto funcIt = functions.find(funcKey);
	if (funcIt != functions.end()) {
		const FunctionInfo &func = funcIt->second;
		// Check return type matches
		if (func.returnType != returnType)
			return false;
		// Check parameter count (skip self which is first parameter)
		if (func.parameters.size() - 1 != paramTypes.size())
			return false;
		// Check each parameter type
		for (size_t i = 0; i < paramTypes.size(); i++) {
			std::string expectedType = paramTypes[i];
			if (expectedType == "Self")
				expectedType = typeName;
			if (func.parameters[i + 1].type != expectedType)
				return false;
		}
		return true;
	}
	return false;
}

bool SemanticAnalyzer::typeIsHashable(const std::string &typeName) const {
	return typeHasMethod(typeName, "hash", "int", {});
}

bool SemanticAnalyzer::typeIsEquatable(const std::string &typeName) const {
	return typeHasMethod(typeName, "equals", "bool", {"Self"});
}

bool SemanticAnalyzer::typeIsIterable(const std::string &typeName) const {
	// Check if type has next() method returning an optional type
	std::string funcKey = typeName + ".next";
	auto funcIt = functions.find(funcKey);
	if (funcIt != functions.end()) {
		// Return type should be "T or nil" (optional type)
		return isOptionalType(funcIt->second.returnType);
	}
	return false;
}

bool SemanticAnalyzer::isIterableType(const std::string &type, ExprAST *iterableExpr) {
	// Error type is always iterable (avoid cascading errors)
	if (type == "error") {
		return true;
	}

	// array<T> struct type is iterable (implements Iterable interface)
	if (maxon::TypeConversion::isArrayStructType(type)) {
		return true;
	}

	// Internal array types are iterable: _ManagedArray<T> or _StaticArray<N, T>
	if (maxon::TypeConversion::isArrayType(type)) {
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

	// Check if type has next() method returning optional (duck typing for Iterable)
	if (typeIsIterable(type)) {
		return true;
	}

	// Structs with an Element associated type are considered iterable
	auto structIt = structs.find(type);
	if (structIt != structs.end()) {
		if (structIt->second.typeAssignments.find("Element") != structIt->second.typeAssignments.end()) {
			return true;
		}
	}

	// Non-iterable types: int, float, bool, char, byte, void, pointers, etc.
	return false;
}

// Check if an expression is a valid constant expression for top-level let declarations.
// Returns true if constant, false otherwise. Sets nonConstReason with explanation if not constant.
bool SemanticAnalyzer::isConstantExpression(ExprAST *expr, std::string &nonConstReason) {
	if (!expr) {
		nonConstReason = "Missing initializer";
		return false;
	}

	// Primitive literals are always constant
	if (dynamic_cast<NumberExprAST *>(expr) ||
		dynamic_cast<FloatExprAST *>(expr) ||
		dynamic_cast<BooleanExprAST *>(expr) ||
		dynamic_cast<StringLiteralExprAST *>(expr) ||
		dynamic_cast<ByteExprAST *>(expr) ||
		dynamic_cast<CharacterExprAST *>(expr)) {
		return true;
	}

	// References to other global constants are constant
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		if (globalConstants.find(varExpr->name) != globalConstants.end()) {
			return true;
		}
		nonConstReason = "'" + varExpr->name + "' is not a global constant";
		return false;
	}

	// Binary, unary, and cast expressions are constant if their operands are
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		std::string leftReason, rightReason;
		if (!isConstantExpression(binExpr->left.get(), leftReason)) {
			nonConstReason = leftReason;
			return false;
		}
		if (!isConstantExpression(binExpr->right.get(), rightReason)) {
			nonConstReason = rightReason;
			return false;
		}
		return true;
	}

	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		return isConstantExpression(unaryExpr->operand.get(), nonConstReason);
	}

	if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		return isConstantExpression(castExpr->expr.get(), nonConstReason);
	}

	// Enum case access (e.g., TokenType.kwFunction) is constant
	if (auto *memberExpr = dynamic_cast<MemberAccessExprAST *>(expr)) {
		// Check if this has already been resolved as an enum case
		if (memberExpr->isEnumCase()) {
			return true;
		}
		// Check if this is an enum case access (Type.case) that hasn't been resolved yet
		if (!memberExpr->object && !memberExpr->objectName.empty()) {
			const EnumInfo *enumInfo = lookupEnum(memberExpr->objectName);
			if (enumInfo != nullptr) {
				const EnumCaseInfo *caseInfo = enumInfo->findCase(memberExpr->memberName);
				if (caseInfo != nullptr && caseInfo->associatedValues.empty()) {
					return true;
				}
			}
		}
		nonConstReason = "Member access is not a constant enum case";
		return false;
	}

	// Array literals are constant if all elements are constant
	if (auto *arrayLit = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		for (size_t i = 0; i < arrayLit->values.size(); i++) {
			if (!isConstantExpression(arrayLit->values[i].get(), nonConstReason)) {
				nonConstReason = "Array element " + std::to_string(i) + " is not constant: " + nonConstReason;
				return false;
			}
		}
		return true;
	}

	// Map literals are constant if all keys and values are constant
	if (auto *mapLit = dynamic_cast<MapLiteralWithEntriesExprAST *>(expr)) {
		for (size_t i = 0; i < mapLit->entries.size(); i++) {
			if (!isConstantExpression(mapLit->entries[i].key.get(), nonConstReason)) {
				nonConstReason = "Map key " + std::to_string(i) + " is not constant: " + nonConstReason;
				return false;
			}
			if (!isConstantExpression(mapLit->entries[i].value.get(), nonConstReason)) {
				nonConstReason = "Map value " + std::to_string(i) + " is not constant: " + nonConstReason;
				return false;
			}
		}
		return true;
	}

	// All other expression types are not constant
	nonConstReason = "Expression type is not allowed in constant context";
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
												 int line, int column, bool isGenericTemplate) {
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
	// For generic templates, we don't resolve types - just check structural conformance
	std::function<std::string(const std::string &)> resolveType = [&](const std::string &type) -> std::string {
		// For generic templates, don't resolve Self or associated types
		if (isGenericTemplate) {
			return type;
		}

		if (type == "Self") {
			return structName;
		}

		// Handle function types (e.g., "fn(Element)->Element" -> "fn(int)->int")
		if (type.rfind("fn(", 0) == 0) {
			// Parse function type: fn(ParamType)->ReturnType
			size_t arrowPos = type.find(")->");
			if (arrowPos != std::string::npos) {
				std::string paramPart = type.substr(3, arrowPos - 3); // extract between "fn(" and ")->"
				std::string returnType = type.substr(arrowPos + 3);	  // extract after ")->"
				// Resolve the param and return types
				std::string resolvedParam = resolveType(paramPart);
				std::string resolvedReturn = resolveType(returnType);
				return "fn(" + resolvedParam + ")->" + resolvedReturn;
			}
		}

		// Handle optional types (e.g., "Element or nil" -> "int or nil")
		if (maxon::TypeConversion::isOptionalType(type)) {
			std::string baseType = maxon::TypeConversion::unwrapOptionalType(type);
			// Recursively resolve the base type
			std::string resolvedBase = resolveType(baseType);
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

		logTrace("Checking conformance of " + structName + " to " + interfaceName);

		// Collect all methods to check (including from base interfaces)
		// Each entry is (method, source interface name)
		std::vector<std::pair<InterfaceMethodInfo, std::string>> allMethods;
		std::vector<std::string> allAssociatedTypes;

		// Helper to collect methods and associated types recursively
		std::function<void(const std::string &)> collectFromInterface = [&](const std::string &ifaceName) {
			auto it = interfaces.find(ifaceName);
			if (it == interfaces.end())
				return;

			const InterfaceInfo &iface = it->second;

			// First add base interface methods and associated types (if any)
			if (!iface.extendsInterface.empty()) {
				collectFromInterface(iface.extendsInterface);
			}

			// Then add this interface's associated types
			for (const auto &assocType : iface.associatedTypes) {
				allAssociatedTypes.push_back(assocType);
			}

			// Then add this interface's methods with source interface name
			for (const auto &method : iface.methods) {
				allMethods.push_back({method, ifaceName});
			}
		};

		collectFromInterface(interfaceName);

		// Check that all associated types are defined (including from base interfaces)
		for (const auto &assocType : allAssociatedTypes) {
			if (!typeAssignments || typeAssignments->find(assocType) == typeAssignments->end()) {
				addError("Type '" + structName + "' does not define required associated type '" + assocType +
							 "' from interface '" + interfaceName + "'",
						 line, column);
			}
		}

		// Check each method in the interface (including inherited ones)
		for (const auto &methodPair : allMethods) {
			const InterfaceMethodInfo &protoMethod = methodPair.first;
			const std::string &sourceInterface = methodPair.second;

			// Build expected method name: StructName.methodName
			std::string expectedMethodName = structName + "." + protoMethod.name;

			auto funcIt = functions.find(expectedMethodName);
			if (funcIt == functions.end()) {
				// Check if interface provides a default implementation
				if (protoMethod.hasDefaultImplementation) {
					// If we have the body, synthesize the method
					if (protoMethod.defaultBody != nullptr) {
						// Synthesize method from default implementation
						// Build parameter list with implicit self
						std::vector<FunctionParameter> params;
						params.push_back(FunctionParameter("self", structName, 0, 0));

						for (const auto &param : protoMethod.parameters) {
							std::string resolvedType = resolveType(param.type);
							params.push_back(FunctionParameter(param.name, resolvedType, param.line, param.column,
															   param.defaultValue));
						}

						std::string resolvedReturnType = resolveType(protoMethod.returnType);

						// Register as function with synthesized default flag
						FunctionInfo funcInfo(expectedMethodName, resolvedReturnType, std::move(params), sourceInterface);
						funcInfo.isSynthesizedDefault = true;
						funcInfo.defaultBody = protoMethod.defaultBody;
						funcInfo.selfType = structName;
						// Copy type assignments for resolving associated types in codegen
						if (typeAssignments) {
							funcInfo.typeSubstitutions = *typeAssignments;
						}
						funcInfo.typeSubstitutions["Self"] = structName;
						functions.emplace(expectedMethodName, std::move(funcInfo));

						logTrace("Synthesized default method: " + expectedMethodName + " from " + sourceInterface);
					}
					// Either way, don't report as missing - it has a default implementation
					// (even if we can't synthesize it here because the body is in another module)
					continue;
				}

				// Build parameter string for error message (without implicit self)
				std::string paramStr;
				for (size_t i = 0; i < protoMethod.parameters.size(); i++) {
					if (i > 0)
						paramStr += ", ";
					std::string paramType = resolveType(protoMethod.parameters[i].type);
					paramStr += protoMethod.parameters[i].name + " " + paramType;
				}
				std::string returnType = resolveType(protoMethod.returnType);

				missingMethods.push_back(protoMethod.name + "(" + paramStr + ") returns " + returnType);
				continue;
			}

			// Check that the method has the required interface prefix
			// Methods should be tagged with the interface they were originally declared in
			const FunctionInfo &implFunc = funcIt->second;
			if (implFunc.implementsInterface != sourceInterface) {
				addError("Method '" + protoMethod.name + "' implements interface '" + sourceInterface +
							 "' but is missing the required prefix\n  Use: function " + sourceInterface + "." +
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
		addError("Partial interface implementation: type '" + structName + "' is missing " +
					 std::to_string(missingMethods.size()) + " method(s):\n" + missingList,
				 line, column);
	}
}

void SemanticAnalyzer::instantiateGenericStructMethods(const std::string &templateName,
													   const std::string &specializedName,
													   const std::map<std::string, std::string> &typeBindings) {
	logTrace("instantiateGenericStructMethods: " + templateName + " -> " + specializedName);

	// Check if struct methods already instantiated
	std::string checkMethodName = specializedName + ".count"; // Use common method as marker
	bool structMethodsInstantiated = (functions.find(checkMethodName) != functions.end());
	if (structMethodsInstantiated) {
		logTrace("  Struct methods already instantiated (found " + checkMethodName + ")");
		// Don't return - we still need to check for synthesized default methods
		// that may have been created after the initial instantiation
	}

	// Look up the struct info to get its methods
	auto structIt = structs.find(templateName);
	if (structIt == structs.end()) {
		// Template struct not yet loaded - mark as undefined for auto-import
		logTrace("  Struct '" + templateName + "' not found in structs map, marking as undefined");
		undefinedStructs.insert(templateName);
		return;
	}
	logTrace("  Found struct '" + templateName + "' in structs map");

	// We need access to the AST methods, which are stored in program->structs
	// Look up the struct definition in the current program
	StructDefAST *structDef = nullptr;
	if (currentProgram) {
		logTrace("  Searching " + std::to_string(currentProgram->structs.size()) + " structs in currentProgram for '" + templateName + "'");
		for (const auto &s : currentProgram->structs) {
			logTrace("    Checking struct: " + s->name);
			if (s->name == templateName) {
				structDef = s.get();
				break;
			}
		}
	}

	// Helper to substitute type parameters (declared before use for recursion)
	std::function<std::string(const std::string &)> substituteType;
	substituteType = [&](const std::string &type) -> std::string {
		auto it = typeBindings.find(type);
		if (it != typeBindings.end()) {
			return it->second;
		}
		// Handle array types: array<Element> -> array<int>
		if (type.length() > 6 && type.substr(0, 6) == "array<") {
			std::string elemType = type.substr(6, type.length() - 7);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return "array<" + elemIt->second + ">";
			}
		}
		// Handle function types: (Element) Element -> (int) int or fn(Element)->Element -> fn(int)->int
		if (type.length() > 2 && type[0] == '(' && type.find(')') != std::string::npos) {
			// Parse (ParamType) ReturnType format
			size_t closePos = type.find(')');
			std::string paramType = type.substr(1, closePos - 1);
			std::string returnType = type.substr(closePos + 2); // skip ") "
			std::string resolvedParam = substituteType(paramType);
			std::string resolvedReturn = substituteType(returnType);
			return "(" + resolvedParam + ") " + resolvedReturn;
		}
		if (type.rfind("fn(", 0) == 0) {
			// Parse fn(ParamType)->ReturnType format
			size_t arrowPos = type.find(")->");
			if (arrowPos != std::string::npos) {
				std::string paramType = type.substr(3, arrowPos - 3);
				std::string returnType = type.substr(arrowPos + 3);
				std::string resolvedParam = substituteType(paramType);
				std::string resolvedReturn = substituteType(returnType);
				return "fn(" + resolvedParam + ")->" + resolvedReturn;
			}
		}
		// Handle optional types: "Element or nil" -> "int or nil"
		if (maxon::TypeConversion::isOptionalType(type)) {
			std::string baseType = maxon::TypeConversion::unwrapOptionalType(type);
			std::string resolvedBase = substituteType(baseType);
			return maxon::TypeConversion::makeOptionalType(resolvedBase);
		}
		return type;
	};

	if (!structDef) {
		// Struct definition not found in currentProgram - try to instantiate from externally
		// registered functions (e.g., stdlib methods registered via registerExternalFunction)
		logTrace("  StructDefAST for '" + templateName + "' not found in currentProgram->structs");
		logTrace("  Attempting to instantiate from externally registered functions");

		// Look for methods with templateName prefix (e.g., "array.push", "array.count")
		std::string templatePrefix = templateName + ".";
		for (const auto &[funcName, funcInfo] : functions) {
			if (funcName.find(templatePrefix) == 0 && funcName.find('<') == std::string::npos) {
				// Found a template method like "array.push"
				std::string methodName = funcName.substr(templatePrefix.length());
				std::string specializedMethodKey = specializedName + "." + methodName;

				// Skip if already registered
				if (functions.find(specializedMethodKey) != functions.end()) {
					continue;
				}

				// Substitute return type
				std::string returnType = substituteType(funcInfo.returnType);

				// Substitute parameter types, preserving labels
				std::vector<FunctionParameter> params;
				for (const auto &param : funcInfo.parameters) {
					if (param.name == "self") {
						// Replace self's type with the specialized type
						params.emplace_back("self", specializedName, param.line, param.column);
					} else {
						params.emplace_back(param.name, substituteType(param.type), param.line, param.column,
											param.defaultValue);
					}
				}

				// Create the specialized function info
				FunctionInfo newFuncInfo(specializedMethodKey, returnType, params,
										 funcInfo.implementsInterface, funcInfo.line, funcInfo.column);
				newFuncInfo.typeSubstitutions = typeBindings;

				// Copy synthesized default info if applicable
				if (funcInfo.isSynthesizedDefault) {
					newFuncInfo.isSynthesizedDefault = true;
					newFuncInfo.defaultBody = funcInfo.defaultBody;
				}

				// Don't insert directly while iterating - collect first
				// Note: We'll use a separate vector and insert after the loop
				logTrace("  Will instantiate from external: " + specializedMethodKey + " -> " + returnType);
			}
		}

		// Second pass: actually insert the new methods (can't modify while iterating)
		std::vector<std::pair<std::string, FunctionInfo>> toAdd;
		for (const auto &[funcName, funcInfo] : functions) {
			if (funcName.find(templatePrefix) == 0 && funcName.find('<') == std::string::npos) {
				std::string methodName = funcName.substr(templatePrefix.length());
				std::string specializedMethodKey = specializedName + "." + methodName;

				if (functions.find(specializedMethodKey) != functions.end()) {
					continue;
				}

				std::string returnType = substituteType(funcInfo.returnType);
				std::vector<FunctionParameter> params;
				for (const auto &param : funcInfo.parameters) {
					if (param.name == "self") {
						params.emplace_back("self", specializedName, param.line, param.column);
					} else {
						params.emplace_back(param.name, substituteType(param.type), param.line, param.column,
											param.defaultValue);
					}
				}

				FunctionInfo newFuncInfo(specializedMethodKey, returnType, params,
										 funcInfo.implementsInterface, funcInfo.line, funcInfo.column);
				newFuncInfo.typeSubstitutions = typeBindings;

				if (funcInfo.isSynthesizedDefault) {
					newFuncInfo.isSynthesizedDefault = true;
					newFuncInfo.defaultBody = funcInfo.defaultBody;
				}

				toAdd.emplace_back(specializedMethodKey, std::move(newFuncInfo));
			}
		}

		for (auto &[key, info] : toAdd) {
			functions.emplace(key, std::move(info));
			logTrace("  Instantiated from external: " + key);
		}

		// Don't mark as undefined - we've handled it
		return;
	}

	logTrace("  Found StructDefAST for '" + templateName + "' with " + std::to_string(structDef->methods.size()) + " methods");

	// Register each method with substituted types
	for (const auto &method : structDef->methods) {
		std::string methodKey = specializedName + "." + method->name;

		// Skip if already registered
		if (functions.find(methodKey) != functions.end()) {
			continue;
		}

		// Substitute return type
		std::string returnType = substituteType(method->returnType);

		// Substitute parameter types, replacing self's type with the specialized type
		// The parser already added self as the first parameter, we just need to update its type
		// Preserve parameter names
		std::vector<FunctionParameter> params;
		for (const auto &param : method->parameters) {
			if (param.name == "self") {
				// Replace self's type with the specialized type
				params.emplace_back("self", specializedName, param.line, param.column);
			} else {
				params.emplace_back(param.name, substituteType(param.type), param.line, param.column,
									param.defaultValue);
			}
		}

		// Register the method
		functions.emplace(methodKey, FunctionInfo(methodKey, returnType, params,
												  method->implementsInterface, method->line, method->column));

		logTrace("Instantiated generic method: " + methodKey + " -> " + returnType);
	}

	// Also instantiate synthesized default methods from interface implementations
	// These are methods synthesized from interface default implementations (e.g., Collection.map)
	// that were registered when checking interface conformance for the template
	std::string templatePrefix = templateName + ".";
	std::vector<std::pair<std::string, FunctionInfo>> synthesizedToAdd;
	for (const auto &[funcName, funcInfo] : functions) {
		// Check if this is a synthesized method from the template
		if (funcInfo.isSynthesizedDefault && funcName.find(templatePrefix) == 0) {
			// Extract the method name (e.g., "array.map" -> "map")
			std::string methodName = funcName.substr(templatePrefix.length());
			std::string specializedMethodKey = specializedName + "." + methodName;

			// Skip if already registered with proper synthesized default info
			auto existingIt = functions.find(specializedMethodKey);
			if (existingIt != functions.end()) {
				// If existing entry already has synthesized default info, skip
				if (existingIt->second.isSynthesizedDefault && existingIt->second.defaultBody != nullptr) {
					continue;
				}
				// Otherwise, we need to update it with the synthesized default info
				logTrace("  Updating existing method with synthesized default info: " + specializedMethodKey);
			}

			// Substitute return type
			std::string returnType = substituteType(funcInfo.returnType);
			if (returnType == "Self") {
				returnType = specializedName;
			}

			// Substitute parameter types
			std::vector<FunctionParameter> params;
			for (const auto &param : funcInfo.parameters) {
				std::string paramType;
				if (param.name == "self") {
					paramType = specializedName;
				} else if (param.type == "Self") {
					paramType = specializedName;
				} else {
					paramType = substituteType(param.type);
				}
				params.emplace_back(param.name, paramType, param.line, param.column, param.defaultValue);
			}

			// Create the instantiated function info with synthesized default info preserved
			FunctionInfo newFuncInfo(specializedMethodKey, returnType, params,
									 funcInfo.implementsInterface, funcInfo.line, funcInfo.column);
			newFuncInfo.isSynthesizedDefault = true;
			newFuncInfo.defaultBody = funcInfo.defaultBody;
			newFuncInfo.selfType = specializedName;
			// Build type substitutions for codegen
			newFuncInfo.typeSubstitutions = typeBindings;
			newFuncInfo.typeSubstitutions["Self"] = specializedName;

			synthesizedToAdd.emplace_back(specializedMethodKey, std::move(newFuncInfo));
			logTrace("Instantiated synthesized default method: " + specializedMethodKey + " -> " + returnType);
		}
	}

	// Add or update the synthesized methods after iteration (to avoid modifying map while iterating)
	for (auto &[key, info] : synthesizedToAdd) {
		auto it = functions.find(key);
		if (it != functions.end()) {
			// Update existing entry with synthesized default info
			it->second.isSynthesizedDefault = true;
			it->second.defaultBody = info.defaultBody;
			it->second.selfType = info.selfType;
			it->second.typeSubstitutions = info.typeSubstitutions;
		} else {
			functions.emplace(key, std::move(info));
		}
	}
}
