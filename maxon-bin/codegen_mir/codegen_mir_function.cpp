/**
 * MIR Code Generator - Function Generation
 *
 * This file implements function code generation for MIR.
 */

#include "../codegen_mir.h"
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

	// Track receiver type for implicit self field access
	currentReceiverType = func->receiverType;

	// Get the function that was already declared
	mir::MIRFunction *function = module->getFunction(functionName);
	if (!function) {
		throw std::runtime_error("Function declaration not found: " + functionName);
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
		if (isArrayParam(param.type)) {
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
		// Handle array types: []KeyType -> []string
		if (type.length() > 2 && type[0] == '[' && type[1] == ']') {
			std::string elemType = type.substr(2);
			auto elemIt = typeBindings.find(elemType);
			if (elemIt != typeBindings.end()) {
				return "[]" + elemIt->second;
			}
		}
		return type;
	};

	// Get the function that was already declared
	mir::MIRFunction *function = module->getFunction(functionName);
	if (!function) {
		throw std::runtime_error("Specialized function declaration not found: " + functionName);
	}

	logTrace("Generating specialized function body: " + functionName);

	// Set current function and create entry block
	builder->setFunction(function);
	mir::MIRBasicBlock *entry = function->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// Clear named values for new function
	namedValues.clear();
	variableTypes.clear();
	structParameters.clear();
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
		if (isArrayParam(substitutedType)) {
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
	stackAllocatedArrays = savedStackAllocatedArrays;
	currentReceiverType = savedReceiverType;
	currentTypeBindings = savedTypeBindings;

	if (savedFunction && savedBlock) {
		builder->setFunction(savedFunction);
		builder->setInsertPoint(savedBlock);
	}
}
