#include "../semantic_analyzer.h"
#include <algorithm>
#include <functional>
#include <sstream>

// === Incremental Analysis Implementation ===
// This file implements per-function semantic caching for efficient re-analysis
// when only part of a document changes.

// Compute a hash of a function's signature for detecting changes
// When a function's signature changes, its callers need to be re-analyzed
std::string SemanticAnalyzer::computeSignatureHash(const FunctionInfo &funcInfo) const {
	std::ostringstream ss;
	ss << funcInfo.returnType << "(";
	for (size_t i = 0; i < funcInfo.parameters.size(); i++) {
		if (i > 0)
			ss << ",";
		ss << funcInfo.parameters[i].type;
	}
	ss << ")";
	return ss.str();
}

// Get the canonical key for a function in the cache
std::string SemanticAnalyzer::getFunctionKey(FunctionAST *func) const {
	if (func->isMethod()) {
		return func->receiverType + "." + func->name;
	} else if (!func->namespaceName.empty()) {
		return func->namespaceName + "." + func->name;
	}
	return func->name;
}

// Record that the current function being analyzed depends on a type
void SemanticAnalyzer::recordTypeDependency(const std::string &typeName) {
	if (!currentFunctionName_.empty() && !typeName.empty()) {
		currentFunctionTypeDeps_.insert(typeName);
	}
}

// Record that the current function being analyzed calls another function
void SemanticAnalyzer::recordCallDependency(const std::string &functionName) {
	if (!currentFunctionName_.empty() && !functionName.empty()) {
		currentFunctionCallDeps_.insert(functionName);
	}
}

// Update the dependency maps after analyzing a function
void SemanticAnalyzer::updateDependencyMaps(const std::string &functionName) {
	// Clear old dependencies for this function
	for (auto &pair : typeToFunctionDeps_) {
		pair.second.erase(functionName);
	}
	for (auto &pair : functionToCallerDeps_) {
		pair.second.erase(functionName);
	}

	// Add new type dependencies
	for (const auto &typeName : currentFunctionTypeDeps_) {
		typeToFunctionDeps_[typeName].insert(functionName);
	}

	// Add new call dependencies
	for (const auto &calledFunc : currentFunctionCallDeps_) {
		functionToCallerDeps_[calledFunc].insert(functionName);
	}

	// Update the cache entry's dependencies
	auto cacheIt = functionCache_.find(functionName);
	if (cacheIt != functionCache_.end()) {
		cacheIt->second.referencedTypes = currentFunctionTypeDeps_;
		cacheIt->second.referencedFunctions = currentFunctionCallDeps_;
	}
}

// Mark a specific function as needing re-analysis
void SemanticAnalyzer::markFunctionDirty(const std::string &functionName) {
	dirtyFunctions_.insert(functionName);
	logTrace("Marked function dirty: " + functionName);
}

// Mark all functions that overlap with the given edit range as dirty
void SemanticAnalyzer::markFunctionsInRange(const SourceRange &editRange) {
	for (const auto &pair : functionCache_) {
		if (pair.second.sourceRange.overlaps(editRange)) {
			dirtyFunctions_.insert(pair.first);
			logTrace("Marked function dirty (range overlap): " + pair.first);
		}
	}
}

// Clear all cached results
void SemanticAnalyzer::clearCache() {
	functionCache_.clear();
	dirtyFunctions_.clear();
	typeToFunctionDeps_.clear();
	functionToCallerDeps_.clear();
	logTrace("Cleared semantic analysis cache");
}

// Check if a function's signature has changed
bool SemanticAnalyzer::hasSignatureChanged(const std::string &functionName, const FunctionInfo &newInfo) const {
	auto cacheIt = functionCache_.find(functionName);
	if (cacheIt == functionCache_.end()) {
		// Not in cache, treat as changed
		return true;
	}

	std::string newHash = computeSignatureHash(newInfo);
	return cacheIt->second.signatureHash != newHash;
}

// Invalidate all functions that reference a given type
void SemanticAnalyzer::invalidateTypeUsers(const std::string &typeName) {
	auto it = typeToFunctionDeps_.find(typeName);
	if (it != typeToFunctionDeps_.end()) {
		for (const auto &funcName : it->second) {
			markFunctionDirty(funcName);
		}
		logTrace("Invalidated " + std::to_string(it->second.size()) + " functions using type: " + typeName);
	}
}

// Invalidate all functions that call a given function
void SemanticAnalyzer::invalidateFunctionCallers(const std::string &functionName) {
	auto it = functionToCallerDeps_.find(functionName);
	if (it != functionToCallerDeps_.end()) {
		for (const auto &callerName : it->second) {
			markFunctionDirty(callerName);
		}
		logTrace("Invalidated " + std::to_string(it->second.size()) + " callers of: " + functionName);
	}
}

// Analyze a single function and store results in the cache
void SemanticAnalyzer::analyzeFunctionForCache(FunctionAST *func, FunctionSemanticResult &result) {
	std::string functionKey = getFunctionKey(func);

	// Set up dependency tracking for this function
	currentFunctionName_ = functionKey;
	currentFunctionTypeDeps_.clear();
	currentFunctionCallDeps_.clear();

	// Remember current error count to extract function-specific errors
	size_t errorStartIdx = errors.size();

	// Analyze the function (this populates errors and variables)
	analyzeFunction(func);

	// Extract errors/warnings that were added during this function's analysis
	for (size_t i = errorStartIdx; i < errors.size(); i++) {
		if (errors[i].severity == 1) {
			result.errors.push_back(errors[i]);
		} else {
			result.warnings.push_back(errors[i]);
		}
	}

	// Collect local variables declared in this function
	// Note: variables is the current scope which includes function locals
	for (const auto &pair : variables) {
		result.localVariables.push_back(pair.second);
	}

	// Store source range for overlap detection
	result.sourceRange = func->getSourceRange();

	// Compute and store signature hash
	auto funcIt = functions.find(functionKey);
	if (funcIt != functions.end()) {
		result.signatureHash = computeSignatureHash(funcIt->second);
	}

	// Check validity
	result.isValid = result.errors.empty();

	// Store dependencies
	result.referencedTypes = currentFunctionTypeDeps_;
	result.referencedFunctions = currentFunctionCallDeps_;

	// Update dependency maps
	updateDependencyMaps(functionKey);

	// Clear tracking state
	currentFunctionName_.clear();
}

// Perform incremental analysis
std::vector<SemanticError> SemanticAnalyzer::analyzeIncremental(
	ProgramAST *program,
	const std::set<std::string> &dirtyFunctions) {

	logDetail("Starting incremental semantic analysis");
	logTrace("Dirty functions: " + std::to_string(dirtyFunctions.size()));

	// If there are many dirty functions or global scope changes,
	// fall back to full re-analysis
	size_t totalFunctions = 0;
	for (const auto &structDef : program->structs) {
		totalFunctions += structDef->methods.size();
	}
	for (const auto &enumDef : program->enums) {
		totalFunctions += enumDef->methods.size();
	}
	totalFunctions += program->functions.size();

	// If more than 50% of functions are dirty, do full analysis
	if (dirtyFunctions.size() > totalFunctions / 2) {
		logDetail("Too many dirty functions, falling back to full analysis");
		return analyze(program);
	}

	// Clear previous errors
	errors.clear();

	// First pass: collect all type definitions (same as full analysis)
	// This is necessary because type info is global
	logTrace("Pass 1: Collecting type definitions");

	// Clear and re-register interfaces
	interfaces.clear();
	for (const auto &interfaceDef : program->interfaces) {
		std::string interfaceKey = (interfaceDef->isExported && !interfaceDef->namespaceName.empty())
									   ? interfaceDef->namespaceName + "." + interfaceDef->name
									   : interfaceDef->name;

		if (interfaces.find(interfaceKey) == interfaces.end()) {
			InterfaceInfo protoInfo(interfaceDef->name, interfaceDef->line, interfaceDef->column,
									interfaceDef->associatedTypes);
			for (const auto &method : interfaceDef->methods) {
				const std::vector<std::unique_ptr<StmtAST>> *bodyPtr =
					method.hasDefaultImplementation ? &method.defaultBody : nullptr;
				protoInfo.methods.push_back(InterfaceMethodInfo(method.name, method.returnType, method.parameters,
																method.hasDefaultImplementation, bodyPtr));
			}
			interfaces.emplace(interfaceKey, std::move(protoInfo));

			if (interfaces.find(interfaceDef->name) == interfaces.end()) {
				InterfaceInfo protoInfoSimple(interfaceDef->name, interfaceDef->line, interfaceDef->column,
											  interfaceDef->associatedTypes);
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

	// Collect enum definitions (check for changes and invalidate users)
	for (const auto &enumDef : program->enums) {
		std::string enumKey = (enumDef->isExported && !enumDef->namespaceName.empty())
								  ? enumDef->namespaceName + "." + enumDef->name
								  : enumDef->name;

		// Check if enum has changed - for now, always re-register
		// A more sophisticated approach would compare definitions
		auto oldIt = enums.find(enumKey);
		bool enumChanged = (oldIt == enums.end());
		// TODO: Compare enum definitions for change detection

		if (enumChanged) {
			invalidateTypeUsers(enumKey);
			invalidateTypeUsers(enumDef->name);
		}
	}

	// Collect struct definitions (check for changes and invalidate users)
	for (const auto &structDef : program->structs) {
		std::string structKey = (structDef->isExported && !structDef->namespaceName.empty())
									? structDef->namespaceName + "." + structDef->name
									: structDef->name;

		auto oldIt = structs.find(structKey);
		bool structChanged = (oldIt == structs.end());
		// TODO: Compare struct definitions for change detection

		if (structChanged) {
			invalidateTypeUsers(structKey);
			invalidateTypeUsers(structDef->name);
		}
	}

	// Second pass: collect function declarations and check for signature changes
	logTrace("Pass 2: Checking function signatures");

	for (const auto &structDef : program->structs) {
		for (const auto &method : structDef->methods) {
			std::string methodKey = structDef->name + "." + method->name;
			auto funcIt = functions.find(methodKey);
			if (funcIt != functions.end()) {
				if (hasSignatureChanged(methodKey, funcIt->second)) {
					invalidateFunctionCallers(methodKey);
				}
			}
		}
	}

	for (const auto &func : program->functions) {
		std::string functionKey = getFunctionKey(func.get());
		auto funcIt = functions.find(functionKey);
		if (funcIt != functions.end()) {
			if (hasSignatureChanged(functionKey, funcIt->second)) {
				invalidateFunctionCallers(functionKey);
			}
		}
	}

	// Merge the provided dirty functions with our computed ones
	std::set<std::string> allDirty = dirtyFunctions;
	allDirty.insert(dirtyFunctions_.begin(), dirtyFunctions_.end());

	// Reset state for function analysis
	undefinedFunctions.clear();
	undefinedStructs.clear();
	undefinedInterfaces.clear();
	allDeclaredVariables.clear();

	// Re-run the full declaration collection passes from analyze()
	// This ensures we have all type and function declarations available

	// Re-collect all enum definitions
	enums.clear();
	for (const auto &enumDef : program->enums) {
		std::string enumKey = (enumDef->isExported && !enumDef->namespaceName.empty())
								  ? enumDef->namespaceName + "." + enumDef->name
								  : enumDef->name;

		EnumInfo enumInfo(enumDef->name, enumDef->line, enumDef->column, enumDef->rawValueType);
		int tagValue = 0;
		for (const auto &caseDef : enumDef->cases) {
			EnumCaseInfo caseInfo(caseDef.name, tagValue++, caseDef.line, caseDef.column);
			for (const auto &assoc : caseDef.associatedValues) {
				caseInfo.associatedValues.push_back(
					EnumAssocValueInfo(assoc.name, assoc.type, assoc.line, assoc.column));
				enumInfo.hasAssociatedValues = true;
			}
			enumInfo.cases.push_back(std::move(caseInfo));
		}
		enums.emplace(enumKey, std::move(enumInfo));
		if (enums.find(enumDef->name) == enums.end()) {
			EnumInfo enumInfoSimple(enumDef->name, enumDef->line, enumDef->column, enumDef->rawValueType);
			enumInfoSimple.cases = enums[enumKey].cases;
			enumInfoSimple.hasAssociatedValues = enums[enumKey].hasAssociatedValues;
			enums.emplace(enumDef->name, std::move(enumInfoSimple));
		}
	}

	// Re-collect all struct definitions (simplified - without validation)
	structs.clear();
	for (const auto &structDef : program->structs) {
		std::string structKey = (structDef->isExported && !structDef->namespaceName.empty())
									? structDef->namespaceName + "." + structDef->name
									: structDef->name;

		std::vector<StructFieldInfo> fields;
		for (const auto &field : structDef->fields) {
			std::string fieldType = field.type;
			bool hasDefault = (field.defaultValue != nullptr);
			if (fieldType.empty() && hasDefault) {
				fieldType = analyzeExpression(field.defaultValue.get());
			}
			fields.push_back(StructFieldInfo(field.name, fieldType, field.isImmutable,
											 hasDefault, "", field.line, field.column));
		}
		structs.emplace(structKey, StructInfo(structDef->name, fields,
											  structDef->line, structDef->column,
											  structDef->conformsTo, structDef->typeAssignments));
		if (structs.find(structDef->name) == structs.end()) {
			structs.emplace(structDef->name, StructInfo(structDef->name, fields,
														structDef->line, structDef->column,
														structDef->conformsTo, structDef->typeAssignments));
		}
	}

	// Re-collect all function declarations
	functionIndices.clear();
	size_t nextFunctionId = 0;

	// Collect struct methods
	for (const auto &structDef : program->structs) {
		for (const auto &method : structDef->methods) {
			std::string methodKey = structDef->name + "." + method->name;
			if (functions.find(methodKey) == functions.end()) {
				functions.emplace(methodKey, FunctionInfo(methodKey, method->returnType,
														  method->parameters, method->implementsInterface,
														  method->line, method->column));
			}
			functionIndices[methodKey] = nextFunctionId++;
		}
	}

	// Collect enum methods
	for (const auto &enumDef : program->enums) {
		for (const auto &method : enumDef->methods) {
			std::string methodKey = enumDef->name + "." + method->name;
			if (functions.find(methodKey) == functions.end()) {
				functions.emplace(methodKey, FunctionInfo(methodKey, method->returnType,
														  method->parameters, "",
														  method->line, method->column));
			}
			functionIndices[methodKey] = nextFunctionId++;
		}
	}

	// Collect top-level functions
	for (const auto &func : program->functions) {
		std::string functionKey = getFunctionKey(func.get());
		if (functions.find(functionKey) == functions.end()) {
			functions.emplace(functionKey, FunctionInfo(functionKey, func->returnType,
														func->parameters, "",
														func->line, func->column));
		}
		functionIndices[functionKey] = nextFunctionId++;

		if (!func->isMethod() && func->namespaceName.empty() &&
			functions.find(func->name) == functions.end()) {
			functions.emplace(func->name, FunctionInfo(func->name, func->returnType,
													   func->parameters, "",
													   func->line, func->column));
			functionIndices[func->name] = functionIndices[functionKey];
		}
	}

	// Third pass: analyze function bodies
	logTrace("Pass 3: Analyzing function bodies (incremental)");

	std::vector<SemanticError> combinedErrors;

	// Helper to process a function
	auto processFunction = [&](FunctionAST *func, const std::string &containingType = "") {
		std::string functionKey;
		if (!containingType.empty()) {
			functionKey = containingType + "." + func->name;
		} else {
			functionKey = getFunctionKey(func);
		}

		if (allDirty.find(functionKey) != allDirty.end()) {
			// This function is dirty - re-analyze it
			logTrace("Re-analyzing dirty function: " + functionKey);

			FunctionSemanticResult result;
			analyzeFunctionForCache(func, result);
			functionCache_[functionKey] = result;

			// Add errors to combined results
			combinedErrors.insert(combinedErrors.end(), result.errors.begin(), result.errors.end());
			combinedErrors.insert(combinedErrors.end(), result.warnings.begin(), result.warnings.end());
		} else {
			// Check if we have cached results
			auto cacheIt = functionCache_.find(functionKey);
			if (cacheIt != functionCache_.end()) {
				// Use cached results
				logTrace("Using cached results for: " + functionKey);
				combinedErrors.insert(combinedErrors.end(),
									  cacheIt->second.errors.begin(),
									  cacheIt->second.errors.end());
				combinedErrors.insert(combinedErrors.end(),
									  cacheIt->second.warnings.begin(),
									  cacheIt->second.warnings.end());
			} else {
				// Not in cache and not dirty - analyze and cache
				logTrace("Analyzing and caching: " + functionKey);

				FunctionSemanticResult result;
				analyzeFunctionForCache(func, result);
				functionCache_[functionKey] = result;

				combinedErrors.insert(combinedErrors.end(), result.errors.begin(), result.errors.end());
				combinedErrors.insert(combinedErrors.end(), result.warnings.begin(), result.warnings.end());
			}
		}
	};

	// Process struct methods
	for (const auto &structDef : program->structs) {
		if (!structDef->associatedTypeParams.empty()) {
			continue; // Skip generic templates
		}
		for (const auto &method : structDef->methods) {
			processFunction(method.get(), structDef->name);
		}
	}

	// Process enum methods
	for (const auto &enumDef : program->enums) {
		for (const auto &method : enumDef->methods) {
			processFunction(method.get(), enumDef->name);
		}
	}

	// Process top-level functions
	for (const auto &func : program->functions) {
		processFunction(func.get());
	}

	// Clear the dirty set after analysis
	dirtyFunctions_.clear();

	logDetail("Incremental analysis complete: " + std::to_string(combinedErrors.size()) + " error(s)");

	return combinedErrors;
}
