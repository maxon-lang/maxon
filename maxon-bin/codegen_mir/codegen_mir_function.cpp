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
