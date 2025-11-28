#include "semantic_analyzer.h"
#include "lexer.h"
#include <algorithm>

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

void SemanticAnalyzer::registerExternalFunction(const std::string &name, const std::string &returnType,
												const std::vector<FunctionParameter> &parameters) {
	functions.emplace(name, FunctionInfo(name, returnType, parameters));
	// External functions get indices starting from a high offset to avoid conflicts
	// We'll assign them sequential IDs starting from a large base
	static size_t externalFunctionIdBase = 1000000;
	functionIndices[name] = externalFunctionIdBase++;
}

std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST *program) {
	errors.clear();
	// Note: Don't clear functions here - we want to keep registered external functions
	structs.clear();
	variables.clear();
	scopeStack.clear();
	loopDepth = 0;
	blockIdStack.clear(); // Clear block ID stack for new analysis
	undefinedFunctions.clear();
	allDeclaredVariables.clear(); // Clear persistent symbol table

	logDetail("Starting semantic analysis");
	logTrace("Registered external functions: " + std::to_string(functions.size()));

	// First pass: collect all struct definitions
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
					fields.push_back(StructFieldInfo(field.name, field.type, field.line, field.column));
				}
			}
			structs.emplace(structKey, StructInfo(structDef->name, std::move(fields),
												  structDef->line, structDef->column));

			// Also register the simple name for use within the same file/namespace
			if (structs.find(structDef->name) == structs.end()) {
				structs.emplace(structDef->name, StructInfo(structDef->name, fields,
															structDef->line, structDef->column));
			}
		}
	}

	// Second pass: collect all function declarations and build function index map
	logTrace("Pass 2: Collecting function declarations");
	functionIndices.clear(); // Reset indices for new analysis
	size_t nextFunctionId = 0;

	for (const auto &func : program->functions) {
		// Build the qualified name if the function has a namespace
		std::string functionKey = func->namespaceName.empty() ? func->name : func->namespaceName + "." + func->name;

		logTrace("Registering function: " + functionKey + " -> " + func->returnType);

		if (functions.find(functionKey) != functions.end()) {
			addError("Function '" + functionKey + "' is already defined" +
						 std::string("\n  Note: Each function name must be unique in the program"),
					 func->line, func->column);
		} else {
			functions.emplace(functionKey, FunctionInfo(functionKey, func->returnType, func->parameters));
			functionIndices[functionKey] = nextFunctionId++;
		}

		// Also register the simple name if in global namespace (for backward compatibility)
		if (func->namespaceName.empty() && functions.find(func->name) == functions.end()) {
			functions.emplace(func->name, FunctionInfo(func->name, func->returnType, func->parameters));
			functionIndices[func->name] = functionIndices[functionKey]; // Same ID for both names
		}
	}

	// Second pass: analyze each function
	logTrace("Pass 3: Analyzing function bodies");
	for (const auto &func : program->functions) {
		analyzeFunction(func.get());
	}

	logDetail("Analysis complete: " + std::to_string(structs.size()) + " structs, " +
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

void SemanticAnalyzer::declareVariable(const std::string &name, const std::string &type, bool isImmutable, int line, int column, bool isParameter, const std::string &initialValue) {
	VariableInfo varInfo(name, type, isImmutable, line, column, isParameter, initialValue);
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

	return std::nullopt;
}

bool SemanticAnalyzer::typesMatch(const std::string &type1, const std::string &type2) {
	// Error type matches anything (to avoid cascading errors)
	if (type1 == "error" || type2 == "error") {
		return true;
	}

	// Check if both are array types and extract element types
	if (type1[0] == '[' && type2[0] == '[') {
		// Extract element types (everything after the last ']')
		size_t bracket1 = type1.find(']');
		size_t bracket2 = type2.find(']');

		if (bracket1 != std::string::npos && bracket2 != std::string::npos) {
			std::string elem1 = type1.substr(bracket1 + 1);
			std::string elem2 = type2.substr(bracket2 + 1);

			// Array types match if element types match, regardless of size
			// Use recursive call
			return typesMatch(elem1, elem2);
		}
	}

	return type1 == type2;
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
