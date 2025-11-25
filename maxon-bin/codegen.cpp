#include "codegen.h"
#include "codegen/codegen_types.h"
#include "lexer.h"
#include <llvm/IR/Constants.h>
#include <llvm/IR/DerivedTypes.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Instructions.h>
#include <llvm/IR/Verifier.h>
#include <stdexcept>

CodeGenerator::CodeGenerator(const std::string &moduleName, bool debugInfo, int verboseLevel, bool profile)
	: builder(context), module(std::make_unique<llvm::Module>(moduleName, context)),
	  generateDebugInfo(debugInfo), verboseLevel(verboseLevel), enableProfiling(profile), sourceFileName(moduleName)
{
	// Set target triple early so other code can query it
#ifdef _WIN32
	module->setTargetTriple(llvm::Triple("x86_64-pc-windows-msvc"));
#else
	module->setTargetTriple(llvm::Triple("x86_64-pc-linux-gnu"));
#endif

	if (generateDebugInfo)
	{
		initDebugInfo(moduleName);
	}
	if (enableProfiling)
	{
		initProfiling();
	}
}

void CodeGenerator::createMinimalEntryPoint()
{
	// Determine target platform from module's target triple
	std::string targetTriple = module->getTargetTriple().str();
	bool isWindows = (targetTriple.find("windows") != std::string::npos ||
					  targetTriple.find("msvc") != std::string::npos);

	// Declare exit - platform runtime provides the implementation
	// (Windows runtime wraps ExitProcess, Linux uses syscall)
	llvm::FunctionType *exitType = llvm::FunctionType::get(
		llvm::Type::getVoidTy(context),
		{llvm::Type::getInt32Ty(context)},
		false);
	llvm::Function::Create(
		exitType,
		llvm::Function::ExternalLinkage,
		"exit",
		module.get());

	// Get the main function - try simple name first, then look for any namespace::main
	llvm::Function *mainFunc = module->getFunction("main");
	if (!mainFunc)
	{
		// Look for main in any namespace (e.g., examples.main)
		for (auto &func : module->functions())
		{
			std::string funcName = func.getName().str();
			// Check if the function name ends with .main
			if (funcName == "main" ||
				(funcName.size() > 5 && funcName.substr(funcName.size() - 5) == ".main"))
			{
				mainFunc = &func;
				break;
			}
		}
	}

	if (!mainFunc)
	{
		throw std::runtime_error("main function not found");
	}

	// Check if main takes arguments: main(args []string) has 2 LLVM params (ptr, i32 hidden length)
	bool mainTakesArgs = (mainFunc->arg_size() == 2);

	if (isWindows && mainTakesArgs)
	{
		// Declare GetCommandLineW - returns wchar_t* (opaque pointer)
		llvm::FunctionType *getCommandLineType = llvm::FunctionType::get(
			llvm::PointerType::get(context, 0), // wchar_t* as opaque pointer
			false);
		llvm::Function::Create(
			getCommandLineType,
			llvm::Function::ExternalLinkage,
			"GetCommandLineW",
			module.get());

		// Declare CommandLineToArgvW - returns wchar_t** and fills pNumArgs
		llvm::FunctionType *commandLineToArgvType = llvm::FunctionType::get(
			llvm::PointerType::get(context, 0),	  // wchar_t** as opaque pointer
			{llvm::PointerType::get(context, 0),  // wchar_t* lpCmdLine
			 llvm::PointerType::get(context, 0)}, // int* pNumArgs
			false);
		llvm::Function::Create(
			commandLineToArgvType,
			llvm::Function::ExternalLinkage,
			"CommandLineToArgvW",
			module.get());
	}

	// Create _start function as the real entry point
	// On all platforms, _start takes no parameters
	llvm::FunctionType *startType = llvm::FunctionType::get(
		llvm::Type::getVoidTy(context),
		false);
	llvm::Function *startFunc = llvm::Function::Create(
		startType,
		llvm::Function::ExternalLinkage,
		"_start",
		module.get());

	// Generate body: call main, then exit with main's return value
	llvm::BasicBlock *entry = llvm::BasicBlock::Create(context, "entry", startFunc);
	llvm::IRBuilder<> tmpBuilder(entry);

	llvm::Value *mainRetVal = nullptr;

	if (mainTakesArgs)
	{
		if (isWindows)
		{
			// Get command line
			llvm::Function *getCommandLineFunc = module->getFunction("GetCommandLineW");
			llvm::Value *cmdLine = tmpBuilder.CreateCall(getCommandLineFunc);

			// Allocate space for argc
			llvm::Value *argcPtr = tmpBuilder.CreateAlloca(llvm::Type::getInt32Ty(context));

			// Parse command line to argv
			llvm::Function *commandLineToArgvFunc = module->getFunction("CommandLineToArgvW");
			llvm::Value *argvW = tmpBuilder.CreateCall(commandLineToArgvFunc, {cmdLine, argcPtr});

			// Load argc
			llvm::Value *argc = tmpBuilder.CreateLoad(llvm::Type::getInt32Ty(context), argcPtr);

			// argv is already an opaque pointer, no cast needed in opaque pointer model
			llvm::Value *argv = argvW;

			// Call main with argv and argc (main(args []string) gets argv as pointer, argc as hidden length)
			mainRetVal = tmpBuilder.CreateCall(mainFunc, {argv, argc});
		}
		else
		{
			// On Linux, argc/argv are NOT passed as parameters to _start
			// Instead, they are on the stack when _start is entered
			// Stack layout at _start entry: [argc][argv[0]][argv[1]]...[NULL][envp[0]]...
			// Since we can't easily capture the exact entry RSP in IR after allocas,
			// we'll use a simpler approach: assume main doesn't need args on Linux for now
			// TODO: Implement proper argc/argv retrieval from initial stack

			// For now, call main without arguments on Linux if it expects args
			// This is a limitation that will be fixed later
			mainRetVal = tmpBuilder.CreateCall(mainFunc);
		}
	}
	else
	{
		// Call main without arguments
		mainRetVal = tmpBuilder.CreateCall(mainFunc);
	}

	// Call exit with main's return value - runtime handles platform specifics
	llvm::Function *exitFunc = module->getFunction("exit");
	tmpBuilder.CreateCall(exitFunc, {mainRetVal});
	tmpBuilder.CreateUnreachable(); // exit never returns
}

void CodeGenerator::generate(ProgramAST *program, bool needsEntryPoint)
{
	// First pass: Create all struct types
	for (const auto &structDef : program->structs)
	{
		// Create opaque struct type
		llvm::StructType *structType = llvm::StructType::create(context, structDef->name);
		structTypes[structDef->name] = structType;

		// Store field information for later use
		std::vector<std::pair<std::string, std::string>> fields;
		for (const auto &field : structDef->fields)
		{
			fields.push_back({field.name, field.type});
		}
		structFields[structDef->name] = fields;
	}

	// Second pass: Set struct body (now that all struct types are declared)
	for (const auto &structDef : program->structs)
	{
		std::vector<llvm::Type *> fieldTypes;
		for (const auto &field : structDef->fields)
		{
			fieldTypes.push_back(getTypeFromString(context, field.type, &structTypes));
		}
		structTypes[structDef->name]->setBody(fieldTypes);
	}

	// Third pass: Create all function declarations
	for (auto &func : program->functions)
	{
		// Get return type
		llvm::Type *returnType = getTypeFromString(context, func->returnType, &structTypes);

		// Create parameter types (including hidden length parameters for arrays)
		std::vector<llvm::Type *> paramTypes;
		for (const auto &param : func->parameters)
		{
			paramTypes.push_back(getParamTypeFromString(context, param.type, &structTypes));
			// Add hidden length parameter for array parameters
			if (isArrayParam(param.type))
			{
				paramTypes.push_back(llvm::Type::getInt32Ty(context));
			}
		}

		// Create function type
		llvm::FunctionType *funcType = llvm::FunctionType::get(returnType, paramTypes, false);

		// Determine the actual function name (with namespace if applicable)
		std::string functionName = func->namespaceName.empty() ? func->name : func->namespaceName + "." + func->name;

		// Determine linkage type:
		// - ExternalLinkage for main (always exported)
		// - ExternalLinkage for exported functions
		// - ExternalLinkage for extern functions
		// - InternalLinkage for non-exported functions (enables dead code elimination)
		llvm::GlobalValue::LinkageTypes linkage =
			(func->name == "main" || func->isExported || func->isExtern)
				? llvm::Function::ExternalLinkage
				: llvm::Function::InternalLinkage;

		// Create function
		llvm::Function::Create(funcType, linkage, functionName, module.get());
	}

	// Second pass: Generate function bodies
	for (auto &func : program->functions)
	{
		generateFunction(func.get(), func->namespaceName);
	}

	// Create minimal CRT entry point only if needed (for executables)
	if (needsEntryPoint)
	{
		createMinimalEntryPoint();
	}

	// Inject profiling instrumentation if enabled
	if (enableProfiling)
	{
		// First inject counters in all basic blocks
		injectInstrCounter();

		// Then add profile output before main returns
		// Find the main function (it might be mangled with namespace)
		llvm::Function *mainFunc = nullptr;
		for (auto &func : *module)
		{
			if (func.getName().contains("::main") || func.getName() == "main")
			{
				mainFunc = &func;
				break;
			}
		}

		if (mainFunc)
		{
			injectProfileOutput(mainFunc);
		}
	}

	// Finalize debug info if enabled
	finalizeDebugInfo();
}
