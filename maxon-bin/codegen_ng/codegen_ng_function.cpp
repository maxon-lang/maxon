/**
 * MIR Code Generator - Function Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

void MIRCodeGenerator::declareFunction(FunctionAST *func) {
	logDetail("Declaring function: " + func->name);

	mir::MIRType *returnType = mir::MIRType::fromName(func->returnType);

	// Create function directly on module (avoid builder->createFunction which sets currentFunction)
	mir::MIRFunction *function = module->createFunction(func->name, returnType);
	function->isExternal = false;

	// Add parameters directly
	for (const auto &param : func->parameters) {
		function->addParameter(mir::MIRType::fromName(param.type), param.name);
	}
}

void MIRCodeGenerator::generateFunction(FunctionAST *func) {
	logDetail("Generating function body: " + func->name);

	// Clear variable tracking for new function scope
	namedValues.clear();

	// Get the previously declared function
	mir::MIRFunction *function = module->getFunction(func->name);
	if (!function) {
		throw std::runtime_error("Function not declared: " + func->name);
	}

	mir::MIRType *returnType = mir::MIRType::fromName(func->returnType);

	// Set current function in builder so createBasicBlock works
	builder->setFunction(function);

	// Create entry block and set insert point
	mir::MIRBasicBlock *entry = builder->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// Create allocas for function parameters and store incoming values
	for (size_t i = 0; i < func->parameters.size(); i++) {
		const auto &param = func->parameters[i];
		mir::MIRValue *argVal = function->parameters[i];
		generateLocalVariable(param.name, param.type, nullptr, argVal);
	}

	// Generate body statements
	for (auto &stmt : func->body) {
		generateStmt(stmt.get(), function);

		// Stop if we hit a terminator
		if (builder->getInsertBlock()->hasTerminator()) {
			break;
		}
	}

	// Add default return if needed
	if (!builder->getInsertBlock()->hasTerminator()) {
		if (returnType->isVoid()) {
			builder->createRetVoid();
		} else if (returnType->isInteger()) {
			builder->createRet(builder->getInt64(0));
		}
	}
}

void MIRCodeGenerator::createEntryPoint() {
	logDetail("Creating entry point (_start)");

	// Declare exit from runtime
	std::vector<std::pair<mir::MIRType *, std::string>> exitParams = {
		{mir::MIRType::getInt64(), "code"}};
	mir::MIRFunction *exitFunc = builder->createFunction("exit", mir::MIRType::getVoid(), exitParams);
	exitFunc->isExternal = true;

	// Find main function
	mir::MIRFunction *mainFunc = module->getFunction("main");
	if (!mainFunc) {
		throw std::runtime_error("main function not found");
	}

	// Create _start function
	std::vector<std::pair<mir::MIRType *, std::string>> startParams;
	builder->createFunction("_start", mir::MIRType::getVoid(), startParams);
	mir::MIRBasicBlock *entry = builder->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// Call main
	mir::MIRValue *mainRetVal = builder->createCall(mainFunc, {});

	// Call exit with main's return value
	builder->createCall(exitFunc, {mainRetVal});

	// Return void (exit never returns, but we need a terminator)
	builder->createRetVoid();
}
