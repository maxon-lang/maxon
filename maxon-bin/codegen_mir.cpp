/**
 * MIR Code Generator - Main Implementation
 *
 * This file contains the main CodeGenerator class implementation that
 * generates MIR from the Maxon AST.
 */

#include "codegen_mir.h"
#include "lexer.h"
#include <stdexcept>

MIRCodeGenerator::MIRCodeGenerator(const std::string &moduleName, bool debugInfo, int verboseLevel)
	: generateDebugInfo(debugInfo), verboseLevel(verboseLevel), sourceFileName(moduleName) {
	module = std::make_unique<mir::MIRModule>(moduleName);

	// Set target triple
#ifdef _WIN32
	module->targetTriple = "x86_64-pc-windows-msvc";
#else
	module->targetTriple = "x86_64-pc-linux-gnu";
#endif

	builder = std::make_unique<mir::MIRBuilder>(module.get());
}

MIRCodeGenerator::~MIRCodeGenerator() = default;

//==============================================================================
// Type Conversion Helpers
//==============================================================================

mir::MIRType *MIRCodeGenerator::getTypeFromString(const std::string &typeStr) {
	if (typeStr == "int") {
		return mir::MIRType::getInt32();
	} else if (typeStr == "float") {
		return mir::MIRType::getFloat64();
	} else if (typeStr == "bool") {
		return mir::MIRType::getInt1();
	} else if (typeStr == "char") {
		return mir::MIRType::getInt8();
	} else if (typeStr == "ptr") {
		return mir::MIRType::getPtr();
	} else if (typeStr == "void") {
		return mir::MIRType::getVoid();
	} else if (typeStr.empty()) {
		return mir::MIRType::getInt32(); // Default type
	}

	// Check for array type: [size]elementType
	if (typeStr.size() > 2 && typeStr[0] == '[') {
		size_t closeBracket = typeStr.find(']');
		if (closeBracket != std::string::npos) {
			std::string sizeStr = typeStr.substr(1, closeBracket - 1);
			std::string elemType = typeStr.substr(closeBracket + 1);
			int size = std::stoi(sizeStr);
			mir::MIRType *elementType = getTypeFromString(elemType);
			return mir::MIRType::getArray(elementType, size);
		}
	}

	// Check for struct type
	auto it = structTypes.find(typeStr);
	if (it != structTypes.end()) {
		return it->second;
	}

	throw std::runtime_error("Unknown type: " + typeStr);
}

mir::MIRType *MIRCodeGenerator::getParamTypeFromString(const std::string &typeStr) {
	// Array parameters are passed as pointers
	if (isArrayParam(typeStr)) {
		return mir::MIRType::getPtr();
	}

	// Struct parameters are passed by pointer
	if (structTypes.find(typeStr) != structTypes.end()) {
		return mir::MIRType::getPtr();
	}

	return getTypeFromString(typeStr);
}

bool MIRCodeGenerator::isArrayParam(const std::string &typeStr) {
	// Check for []type (dynamic array parameter) or [size]type
	return typeStr.size() > 2 && typeStr[0] == '[';
}

//==============================================================================
// Scope Management
//==============================================================================

void MIRCodeGenerator::pushScope() {
	scopeStack.push_back(ScopeInfo{});
}

void MIRCodeGenerator::popScope(mir::MIRFunction *function) {
	if (scopeStack.empty()) {
		return;
	}

	generateScopeCleanup(function);
	scopeStack.pop_back();
}

void MIRCodeGenerator::generateScopeCleanup(mir::MIRFunction *function) {
	if (scopeStack.empty()) {
		return;
	}

	// Free heap-allocated arrays in this scope
	for (const auto &[name, allocaVal] : scopeStack.back().heapAllocatedArrays) {
		// Load the pointer
		mir::MIRValue *ptr = builder->createLoad(mir::MIRType::getPtr(), allocaVal, name + ".ptr");

		// Call free
		mir::MIRFunction *freeFunc = getOrDeclareFunction(
			"free",
			mir::MIRType::getVoid(),
			{mir::MIRType::getPtr()});
		builder->createCall(freeFunc, {ptr});
	}
}

//==============================================================================
// Alloca Helper
//==============================================================================

mir::MIRValue *MIRCodeGenerator::createEntryBlockAlloca(mir::MIRFunction *function,
														const std::string &varName,
														mir::MIRType *type) {
	// In MIR, we create alloca at the current position
	// A more sophisticated implementation would move allocas to entry block
	if (!type) {
		type = mir::MIRType::getInt32();
	}
	return builder->createAlloca(type, varName);
}

//==============================================================================
// Runtime Function Helpers
//==============================================================================

mir::MIRFunction *MIRCodeGenerator::getOrDeclareFunction(const std::string &name,
														 mir::MIRType *returnType,
														 const std::vector<mir::MIRType *> &paramTypes) {
	mir::MIRFunction *func = module->getFunction(name);
	if (func) {
		return func;
	}

	return builder->declareFunction(name, returnType, paramTypes);
}

void MIRCodeGenerator::initStandardLibrary() {
	// Declare print function
	getOrDeclareFunction("print", mir::MIRType::getVoid(), {mir::MIRType::getInt32()});

	// Declare print_float function
	getOrDeclareFunction("print_float", mir::MIRType::getVoid(), {mir::MIRType::getFloat64()});

	// Declare write_stdout function (used by print) - returns bytes written or error code
	getOrDeclareFunction("write_stdout",
						 mir::MIRType::getInt32(),
						 {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
}

void MIRCodeGenerator::initHeapManagement() {
	// Declare malloc
	getOrDeclareFunction("malloc", mir::MIRType::getPtr(), {mir::MIRType::getInt64()});

	// Declare free
	getOrDeclareFunction("free", mir::MIRType::getVoid(), {mir::MIRType::getPtr()});

	// Declare memset
	getOrDeclareFunction("memset",
						 mir::MIRType::getPtr(),
						 {mir::MIRType::getPtr(), mir::MIRType::getInt32(), mir::MIRType::getInt64()});
}

//==============================================================================
// Entry Point Creation
//==============================================================================

void MIRCodeGenerator::createMinimalEntryPoint() {
	// Determine target platform
	bool isWindows = (module->targetTriple.find("windows") != std::string::npos);

	// Declare exit - runtime provides implementation
	getOrDeclareFunction("exit", mir::MIRType::getVoid(), {mir::MIRType::getInt32()});

	// Find main function
	mir::MIRFunction *mainFunc = module->getFunction("main");
	if (!mainFunc) {
		// Look for namespaced main (e.g., examples.main)
		for (auto &func : module->functions) {
			const std::string &funcName = func->name;
			if (funcName == "main" ||
				(funcName.size() > 5 && funcName.substr(funcName.size() - 5) == ".main")) {
				mainFunc = func.get();
				break;
			}
		}
	}

	if (!mainFunc) {
		throw std::runtime_error("main function not found");
	}

	// Check if main takes arguments
	bool mainTakesArgs = (mainFunc->parameters.size() == 2);

	// Create _start function
	std::vector<std::pair<mir::MIRType *, std::string>> startParams;
	(void)builder->createFunction("_start", mir::MIRType::getVoid(), startParams);
	mir::MIRBasicBlock *entry = builder->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	mir::MIRValue *mainRetVal = nullptr;

	if (mainTakesArgs && isWindows) {
		// TODO: Implement Windows command line argument handling
		// For now, call main without arguments
		mainRetVal = builder->createCall(mainFunc, {});
	} else {
		// Call main without arguments
		mainRetVal = builder->createCall(mainFunc, {});
	}

	// Call exit with main's return value
	mir::MIRFunction *exitFunc = module->getFunction("exit");
	builder->createCall(exitFunc, {mainRetVal});

	// Return void (exit never returns, but we need a terminator)
	builder->createRetVoid();
}

//==============================================================================
// Main Generation
//==============================================================================

void MIRCodeGenerator::generate(ProgramAST *program, bool needsEntryPoint) {
	// First pass: Create all struct types
	for (const auto &structDef : program->structs) {
		std::vector<mir::MIRType *> fieldTypes;
		std::vector<std::pair<std::string, std::string>> fields;

		for (const auto &field : structDef->fields) {
			fieldTypes.push_back(getTypeFromString(field.type));
			fields.push_back({field.name, field.type});
		}

		mir::MIRType *structType = module->getOrCreateStructType(structDef->name, fieldTypes);
		structTypes[structDef->name] = structType;
		structFields[structDef->name] = fields;
	}

	// Second pass: Create all function declarations
	for (auto &func : program->functions) {
		mir::MIRType *returnType = getTypeFromString(func->returnType);

		std::vector<mir::MIRType *> paramTypes;
		for (const auto &param : func->parameters) {
			paramTypes.push_back(getParamTypeFromString(param.type));
			// Add hidden length parameter for arrays
			if (isArrayParam(param.type)) {
				paramTypes.push_back(mir::MIRType::getInt32());
			}
		}

		std::string functionName = func->namespaceName.empty() ? func->name : func->namespaceName + "." + func->name;

		if (func->isExtern) {
			// External declaration
			builder->declareFunction(functionName, returnType, paramTypes);
		} else {
			// Will be defined later - create declaration first
			mir::MIRFunction *mirFunc = module->createFunction(functionName, returnType);
			mirFunc->isExternal = false; // Will have body

			// Add parameters
			for (const auto &param : func->parameters) {
				mir::MIRType *paramType = getParamTypeFromString(param.type);
				mirFunc->addParameter(paramType, param.name);

				if (isArrayParam(param.type)) {
					mirFunc->addParameter(mir::MIRType::getInt32(), param.name + ".__length");
				}
			}
		}
	}

	// Third pass: Generate function bodies
	for (auto &func : program->functions) {
		if (!func->isExtern) {
			generateFunction(func.get(), func->namespaceName);
		}
	}

	// Create entry point if needed
	if (needsEntryPoint) {
		createMinimalEntryPoint();
	}
}
