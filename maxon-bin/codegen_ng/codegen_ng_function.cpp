/**
 * MIR Code Generator - Function Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

void MIRCodeGenerator::generateFunction(FunctionAST *func) {
	logDetail("Generating function: " + func->name);

	mir::MIRType *returnType = getTypeFromString(func->returnType);

	// Build parameter list
	std::vector<std::pair<mir::MIRType *, std::string>> params;
	for (const auto &param : func->parameters) {
		params.push_back({getTypeFromString(param.type), param.name});
	}

	// Create function
	mir::MIRFunction *function = builder->createFunction(func->name, returnType, params);

	// Create entry block
	mir::MIRBasicBlock *entry = builder->createBasicBlock("entry");
	builder->setInsertPoint(entry);

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
