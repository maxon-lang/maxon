/**
 * MIR Code Generator - Function Generation
 *
 * This file implements function code generation for MIR.
 */

#include "../codegen_mir.h"
#include "../types/type_conversion.h"
#include <stdexcept>

void MIRCodeGenerator::generateFunction(FunctionAST *func, const std::string &namespaceName) {
	// Determine the actual function name
	// For methods: ReceiverType.name
	// For namespaced functions: namespace.name
	// Otherwise: just name
	std::string functionName;
	if (!func->receiverType.empty()) {
		functionName = func->receiverType + "." + func->name;
	} else if (!namespaceName.empty()) {
		functionName = namespaceName + "." + func->name;
	} else {
		functionName = func->name;
	}
	// Debug: track which function is being generated
	logTrace("Generating function body: " + functionName);

	// Store parameter types for this function (used for optional parameter wrapping)
	std::vector<std::string> paramTypes;
	for (const auto &param : func->parameters) {
		paramTypes.push_back(param.type);
	}
	functionParameterTypes[functionName] = paramTypes;

	// Track receiver type for implicit self field access
	currentReceiverType = func->receiverType;

	// Get the function that was already declared
	mir::MIRFunction *function = module->getFunction(functionName);
	if (!function) {
		reportError("Function declaration not found: " + functionName,
					func->line, func->column);
	}

	// If this is an extern function, don't generate a body
	if (func->isExtern) {
		return;
	}

	// Set current function and create entry block
	builder->setFunction(function);
	mir::MIRBasicBlock *entry = function->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// Clear named values for new function
	namedValues.clear();
	variableTypes.clear();
	structParameters.clear();
	arrayParameters.clear();
	stackAllocatedArrays.clear();

	// Allocate stack space for parameters
	size_t argIdx = 0;
	for (size_t paramIdx = 0; paramIdx < func->parameters.size(); paramIdx++) {
		const auto &param = func->parameters[paramIdx];
		mir::MIRType *paramType = getParamTypeFromString(param.type);
		mir::MIRValue *alloca = builder->createAlloca(paramType, param.name);

		// Store the parameter value (get parameter from function)
		mir::MIRValue *paramVal = function->parameters[argIdx];
		builder->createStore(paramVal, alloca);
		namedValues[param.name] = alloca;
		variableTypes[param.name] = param.type;
		argIdx++;

		// Track if this is a struct parameter (passed by pointer)
		if (structTypes.find(param.type) != structTypes.end()) {
			structParameters.insert(param.name);
		}

		// If this is an array parameter, also store the hidden length parameter
		// and track it as an array parameter (uses old ABI)
		if (isArrayParam(param.type)) {
			arrayParameters.insert(param.name);
			std::string lengthVarName = param.name + ".__length";
			mir::MIRValue *lengthAlloca = builder->createAlloca(mir::MIRType::getInt32(), lengthVarName);
			mir::MIRValue *lengthParamVal = function->parameters[argIdx];
			builder->createStore(lengthParamVal, lengthAlloca);
			namedValues[lengthVarName] = lengthAlloca;
			argIdx++;
		}
	}

	// Push a scope for the function body
	pushScope();

	// Set source location
	if (generateDebugInfo) {
		builder->setLocation(func->line, func->column);
	}

	// Generate function body
	try {
		for (auto &stmt : func->body) {
			generateStmt(stmt.get(), function);
			// Stop generating after a terminator (return, break, continue)
			// Any subsequent code is unreachable
			if (builder->getInsertBlock()->hasTerminator()) {
				break;
			}
		}
	} catch (const std::exception &e) {
		throw std::runtime_error("Error in function '" + functionName + "': " + e.what());
	}

	// If function doesn't have a terminator (return), clean up and add one
	mir::MIRBasicBlock *currentBlock = builder->getInsertBlock();
	if (currentBlock && !currentBlock->hasTerminator()) {
		// Clean up the function scope
		popScope(function);

		// Add default return
		mir::MIRType *retType = function->returnType;
		if (retType->isInteger()) {
			builder->createRet(builder->getInt32(0));
		} else if (retType->isFloat()) {
			builder->createRet(builder->getFloat64(0.0));
		} else if (retType->isPointer()) {
			builder->createRet(builder->getNull());
		} else {
			builder->createRetVoid();
		}
	}

	// Clear receiver type after generating function
	currentReceiverType.clear();
}

void MIRCodeGenerator::generateFunctionWithTypeBindings(FunctionAST *func, const std::string &namespaceName,
														const std::map<std::string, std::string> &typeBindings,
														const std::string &specializedReceiverType) {
	// Build the specialized function name
	std::string functionName = specializedReceiverType + "." + func->name;

	// Save the current builder state to restore after generating this method
	mir::MIRFunction *savedFunction = builder->getFunction();
	mir::MIRBasicBlock *savedBlock = builder->getInsertBlock();
	std::map<std::string, mir::MIRValue *> savedNamedValues = namedValues;
	std::map<std::string, std::string> savedVariableTypes = variableTypes;
	std::set<std::string> savedStructParameters = structParameters;
	std::set<std::string> savedArrayParameters = arrayParameters;
	std::set<std::string> savedStackAllocatedArrays = stackAllocatedArrays;
	std::string savedReceiverType = currentReceiverType;
	std::map<std::string, std::string> savedTypeBindings = currentTypeBindings;

	// Track receiver type for implicit self field access
	currentReceiverType = specializedReceiverType;

	// Store type bindings for use during codegen
	currentTypeBindings = typeBindings;

	// Helper to substitute type parameters
	// Also substitutes the template receiver type (e.g., "map" -> "map<string,int>")
	auto substituteType = [&](const std::string &type) -> std::string {
		// Handle Self type - substitute with the specialized receiver type
		if (type == "Self") {
			return specializedReceiverType;
		}

		// Substitute template receiver type with specialized type
		// Extract the base template name from specializedReceiverType (e.g., "map<string,int>" -> "map")
		std::string baseTemplateName = specializedReceiverType;
		size_t anglePos = specializedReceiverType.find('<');
		if (anglePos != std::string::npos) {
			baseTemplateName = specializedReceiverType.substr(0, anglePos);
		}
		if (type == baseTemplateName) {
			return specializedReceiverType;
		}

		auto it = typeBindings.find(type);
		if (it != typeBindings.end()) {
			return it->second;
		}

		// Handle optional types: "Element or nil" -> "int or nil"
		if (maxon::TypeConversion::isOptionalType(type)) {
			std::string baseType = maxon::TypeConversion::unwrapOptionalType(type);
			auto baseIt = typeBindings.find(baseType);
			if (baseIt != typeBindings.end()) {
				return maxon::TypeConversion::makeOptionalType(baseIt->second);
			}
			// Check if Self is the base type
			if (baseType == "Self") {
				return maxon::TypeConversion::makeOptionalType(specializedReceiverType);
			}
		}

		// Handle opaque _ManagedArray type (without angle brackets)
		// When the array struct is instantiated, _ManagedArray becomes _ManagedArray<Element>
		if (type == "_ManagedArray") {
			auto elemIt = typeBindings.find("Element");
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeManagedArrayType(elemIt->second);
			}
		}

		// Handle array types: _ManagedArray<KeyType> -> _ManagedArray<string>
		if (maxon::TypeConversion::isManagedArrayType(type)) {
			std::string elemType = maxon::TypeConversion::getArrayElementType(type);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeManagedArrayType(elemIt->second);
			}
		}

		// Handle array<T> struct types: array<Element> -> array<int>
		if (maxon::TypeConversion::isArrayStructType(type)) {
			std::string elemType = maxon::TypeConversion::getArrayStructElementType(type);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return maxon::TypeConversion::makeArrayStructType(elemIt->second);
			}
		}
		return type;
	};

	// Get the function that was already declared
	mir::MIRFunction *function = module->getFunction(functionName);
	if (!function) {
		reportError("Specialized function declaration not found: " + functionName,
					func->line, func->column);
	}

	logTrace("Generating specialized function body: " + functionName);

	// Store parameter types for this function with type substitution
	std::vector<std::string> paramTypes;
	for (const auto &param : func->parameters) {
		paramTypes.push_back(substituteType(param.type));
	}
	functionParameterTypes[functionName] = paramTypes;

	// Set current function and create entry block
	builder->setFunction(function);
	mir::MIRBasicBlock *entry = function->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// Clear named values for new function
	namedValues.clear();
	variableTypes.clear();
	structParameters.clear();
	arrayParameters.clear();
	stackAllocatedArrays.clear();

	// Allocate stack space for parameters with type substitution
	size_t argIdx = 0;
	for (size_t paramIdx = 0; paramIdx < func->parameters.size(); paramIdx++) {
		const auto &param = func->parameters[paramIdx];
		std::string substitutedType = substituteType(param.type);
		mir::MIRType *paramType = getParamTypeFromString(substitutedType);
		mir::MIRValue *alloca = builder->createAlloca(paramType, param.name);

		// Store the parameter value (get parameter from function)
		mir::MIRValue *paramVal = function->parameters[argIdx];
		builder->createStore(paramVal, alloca);
		namedValues[param.name] = alloca;
		variableTypes[param.name] = substitutedType;
		argIdx++;

		// Track if this is a struct parameter (passed by pointer)
		if (structTypes.find(substitutedType) != structTypes.end()) {
			structParameters.insert(param.name);
		}

		// If this is an array parameter, also store the hidden length parameter
		// and track it as an array parameter (uses old ABI)
		if (isArrayParam(substitutedType)) {
			arrayParameters.insert(param.name);
			std::string lengthVarName = param.name + ".__length";
			mir::MIRValue *lengthAlloca = builder->createAlloca(mir::MIRType::getInt32(), lengthVarName);
			mir::MIRValue *lengthParamVal = function->parameters[argIdx];
			builder->createStore(lengthParamVal, lengthAlloca);
			namedValues[lengthVarName] = lengthAlloca;
			argIdx++;
		}
	}

	// Push a scope for the function body
	pushScope();

	// Set source location
	if (generateDebugInfo) {
		builder->setLocation(func->line, func->column);
	}

	// Generate function body
	for (auto &stmt : func->body) {
		generateStmt(stmt.get(), function);
		// Stop generating after a terminator (return, break, continue)
		// Any subsequent code is unreachable
		if (builder->getInsertBlock()->hasTerminator()) {
			break;
		}
	}

	// If function doesn't have a terminator (return), clean up and add one
	mir::MIRBasicBlock *currentBlock = builder->getInsertBlock();
	if (currentBlock && !currentBlock->hasTerminator()) {
		// Clean up the function scope
		popScope(function);

		// Add default return
		mir::MIRType *retType = function->returnType;
		if (retType->isInteger()) {
			builder->createRet(builder->getInt32(0));
		} else if (retType->isFloat()) {
			builder->createRet(builder->getFloat64(0.0));
		} else if (retType->isPointer()) {
			builder->createRet(builder->getNull());
		} else {
			builder->createRetVoid();
		}
	}

	// Restore the previous builder state
	namedValues = savedNamedValues;
	variableTypes = savedVariableTypes;
	structParameters = savedStructParameters;
	arrayParameters = savedArrayParameters;
	stackAllocatedArrays = savedStackAllocatedArrays;
	currentReceiverType = savedReceiverType;
	currentTypeBindings = savedTypeBindings;

	if (savedFunction && savedBlock) {
		builder->setFunction(savedFunction);
		builder->setInsertPoint(savedBlock);
	}
}

void MIRCodeGenerator::generateSynthesizedMethod(const FunctionInfo &funcInfo) {
	// Synthesized methods are generated from interface default implementations
	// The funcInfo contains the method signature and a pointer to the default body AST
	if (!funcInfo.defaultBody || funcInfo.defaultBody->empty()) {
		reportError("Synthesized method '" + funcInfo.name + "' has no default body", 0, 0);
		return;
	}

	std::string functionName = funcInfo.name;
	logTrace("Generating synthesized method: " + functionName);

	// Get the function that was already declared
	mir::MIRFunction *function = module->getFunction(functionName);
	if (!function) {
		reportError("Synthesized function declaration not found: " + functionName, 0, 0);
		return;
	}

	// Save the current builder state to restore after generating this method
	mir::MIRFunction *savedFunction = builder->getFunction();
	mir::MIRBasicBlock *savedBlock = builder->getInsertBlock();
	std::map<std::string, mir::MIRValue *> savedNamedValues = namedValues;
	std::map<std::string, std::string> savedVariableTypes = variableTypes;
	std::set<std::string> savedStructParameters = structParameters;
	std::set<std::string> savedArrayParameters = arrayParameters;
	std::set<std::string> savedStackAllocatedArrays = stackAllocatedArrays;
	std::string savedReceiverType = currentReceiverType;
	std::map<std::string, std::string> savedTypeBindings = currentTypeBindings;

	// Track receiver type for implicit self field access (first parameter is self)
	currentReceiverType = funcInfo.selfType;

	// Store type substitutions for use during codegen
	currentTypeBindings = funcInfo.typeSubstitutions;

	// Set current function and create entry block
	builder->setFunction(function);
	mir::MIRBasicBlock *entry = function->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// Clear named values for new function
	namedValues.clear();
	variableTypes.clear();
	structParameters.clear();
	arrayParameters.clear();
	stackAllocatedArrays.clear();

	// Store parameter types for this function
	std::vector<std::string> paramTypes;
	for (const auto &param : funcInfo.parameters) {
		paramTypes.push_back(param.type);
	}
	functionParameterTypes[functionName] = paramTypes;

	// Allocate stack space for parameters
	size_t argIdx = 0;
	for (size_t paramIdx = 0; paramIdx < funcInfo.parameters.size(); paramIdx++) {
		const auto &param = funcInfo.parameters[paramIdx];
		mir::MIRType *paramType = getParamTypeFromString(param.type);
		mir::MIRValue *alloca = builder->createAlloca(paramType, param.name);

		// Store the parameter value (get parameter from function)
		mir::MIRValue *paramVal = function->parameters[argIdx];
		builder->createStore(paramVal, alloca);
		namedValues[param.name] = alloca;
		variableTypes[param.name] = param.type;
		argIdx++;

		// Track if this is a struct parameter (passed by pointer)
		if (structTypes.find(param.type) != structTypes.end()) {
			structParameters.insert(param.name);
		}

		// If this is an array parameter, also store the hidden length parameter
		if (isArrayParam(param.type)) {
			arrayParameters.insert(param.name);
			std::string lengthVarName = param.name + ".__length";
			mir::MIRValue *lengthAlloca = builder->createAlloca(mir::MIRType::getInt32(), lengthVarName);
			mir::MIRValue *lengthParamVal = function->parameters[argIdx];
			builder->createStore(lengthParamVal, lengthAlloca);
			namedValues[lengthVarName] = lengthAlloca;
			argIdx++;
		}
	}

	// Push a scope for the function body
	pushScope();

	// Generate function body from the default implementation
	for (const auto &stmt : *funcInfo.defaultBody) {
		generateStmt(stmt.get(), function);
		// Stop generating after a terminator (return, break, continue)
		if (builder->getInsertBlock()->hasTerminator()) {
			break;
		}
	}

	// If function doesn't have a terminator (return), clean up and add one
	mir::MIRBasicBlock *currentBlock = builder->getInsertBlock();
	if (currentBlock && !currentBlock->hasTerminator()) {
		// Clean up the function scope
		popScope(function);

		// Add default return
		mir::MIRType *retType = function->returnType;
		if (retType->isInteger()) {
			builder->createRet(builder->getInt32(0));
		} else if (retType->isFloat()) {
			builder->createRet(builder->getFloat64(0.0));
		} else if (retType->isPointer()) {
			builder->createRet(builder->getNull());
		} else {
			builder->createRetVoid();
		}
	}

	// Restore the previous builder state
	namedValues = savedNamedValues;
	variableTypes = savedVariableTypes;
	structParameters = savedStructParameters;
	arrayParameters = savedArrayParameters;
	stackAllocatedArrays = savedStackAllocatedArrays;
	currentReceiverType = savedReceiverType;
	currentTypeBindings = savedTypeBindings;

	if (savedFunction && savedBlock) {
		builder->setFunction(savedFunction);
		builder->setInsertPoint(savedBlock);
	}
}
