#include "../codegen.h"
#include "../lexer.h"
#include "codegen_types.h"
#include <llvm/IR/Constants.h>
#include <llvm/IR/DerivedTypes.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Instructions.h>
#include <llvm/IR/Intrinsics.h>

void CodeGenerator::initStandardLibrary() {
	// Check if write_stdout already exists (to avoid double-initialization)
	if (module->getFunction("write_stdout")) {
		return;
	}

	// Declare write_stdout - provided by maxon-runtime
	// i32 write_stdout(ptr buf, i32 count)
	llvm::FunctionType *writeStdoutType = llvm::FunctionType::get(
		llvm::Type::getInt32Ty(context),
		{
			llvm::PointerType::get(context, 0), // buf
			llvm::Type::getInt32Ty(context)		// count
		},
		false);
	llvm::Function::Create(writeStdoutType, llvm::Function::ExternalLinkage,
						   "write_stdout", module.get());
}

llvm::Function *CodeGenerator::getOrDeclareMemset() {
	llvm::Function *memsetFunc = module->getFunction("memset");
	if (!memsetFunc) {
		llvm::FunctionType *memsetType = llvm::FunctionType::get(
			llvm::PointerType::get(context, 0),
			{llvm::PointerType::get(context, 0), llvm::Type::getInt32Ty(context), llvm::Type::getInt64Ty(context)},
			false);
		memsetFunc = llvm::Function::Create(memsetType, llvm::Function::ExternalLinkage, "memset", module.get());
	}
	return memsetFunc;
}

void CodeGenerator::initHeapManagement() {
	// Check if malloc already exists
	if (module->getFunction("malloc")) {
		return;
	}

	// Determine target platform from module's target triple
	std::string targetTriple = module->getTargetTriple().str();
	bool isWindows = (targetTriple.find("windows") != std::string::npos ||
					  targetTriple.find("msvc") != std::string::npos);

	if (isWindows) {
		// Windows: Declare malloc/free as external functions
		// They are provided by platform_windows.ll which uses Windows Heap API

		llvm::FunctionType *mallocType = llvm::FunctionType::get(
			llvm::PointerType::get(context, 0),
			{llvm::Type::getInt64Ty(context)},
			false);
		llvm::Function::Create(
			mallocType,
			llvm::Function::ExternalLinkage,
			"malloc",
			module.get());

		llvm::FunctionType *freeType = llvm::FunctionType::get(
			llvm::Type::getVoidTy(context),
			{llvm::PointerType::get(context, 0)},
			false);
		llvm::Function::Create(
			freeType,
			llvm::Function::ExternalLinkage,
			"free",
			module.get());
	} else {
		// Linux: Just declare malloc and free as external functions
		// They will be provided by runtime.ll (platform_linux.ll)
		llvm::FunctionType *mallocType = llvm::FunctionType::get(
			llvm::PointerType::get(context, 0),
			{llvm::Type::getInt64Ty(context)},
			false);
		llvm::Function::Create(
			mallocType,
			llvm::Function::ExternalLinkage,
			"malloc",
			module.get());

		llvm::FunctionType *freeType = llvm::FunctionType::get(
			llvm::Type::getVoidTy(context),
			{llvm::PointerType::get(context, 0)},
			false);
		llvm::Function::Create(
			freeType,
			llvm::Function::ExternalLinkage,
			"free",
			module.get());
	}
}
