#include "../codegen.h"
#include "codegen_types.h"
#include "../lexer.h"
#include <llvm/IR/Constants.h>
#include <llvm/IR/DerivedTypes.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Instructions.h>
#include <llvm/IR/Intrinsics.h>

void CodeGenerator::initStandardLibrary()
{
    // Check if write_stdout already exists (to avoid double-initialization)
    if (module->getFunction("write_stdout"))
    {
        return;
    }

    // Declare write_stdout - provided by maxon-runtime
    // i32 write_stdout(ptr buf, i32 count)
    llvm::FunctionType *writeStdoutType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::PointerType::get(context, 0), // buf
            llvm::Type::getInt32Ty(context)     // count
        },
        false);
    llvm::Function::Create(writeStdoutType, llvm::Function::ExternalLinkage,
                           "write_stdout", module.get());
}

llvm::Function *CodeGenerator::getOrDeclareMemset()
{
    llvm::Function *memsetFunc = module->getFunction("memset");
    if (!memsetFunc)
    {
        llvm::FunctionType *memsetType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),
            {llvm::PointerType::get(context, 0), llvm::Type::getInt32Ty(context), llvm::Type::getInt64Ty(context)},
            false);
        memsetFunc = llvm::Function::Create(memsetType, llvm::Function::ExternalLinkage, "memset", module.get());
    }
    return memsetFunc;
}

void CodeGenerator::initHeapManagement()
{
    // Check if malloc already exists
    if (module->getFunction("malloc"))
    {
        return;
    }

    // Determine target platform from module's target triple
    std::string targetTriple = module->getTargetTriple().str();
    bool isWindows = (targetTriple.find("windows") != std::string::npos ||
                      targetTriple.find("msvc") != std::string::npos);

    if (isWindows)
    {
        // Windows: Create malloc/free wrappers that use Windows Heap API

        // Declare Windows Heap API functions
        // ptr GetProcessHeap()
        llvm::FunctionType *getProcessHeapType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),
            {},
            false);
        llvm::Function::Create(getProcessHeapType, llvm::Function::ExternalLinkage,
                               "GetProcessHeap", module.get());

        // ptr HeapAlloc(ptr heap, i32 flags, i64 bytes)
        llvm::FunctionType *heapAllocType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),
            {
                llvm::PointerType::get(context, 0), // hHeap
                llvm::Type::getInt32Ty(context),    // dwFlags
                llvm::Type::getInt64Ty(context)     // dwBytes
            },
            false);
        llvm::Function::Create(heapAllocType, llvm::Function::ExternalLinkage,
                               "HeapAlloc", module.get());

        // i32 HeapFree(ptr heap, i32 flags, ptr mem)
        llvm::FunctionType *heapFreeType = llvm::FunctionType::get(
            llvm::Type::getInt32Ty(context),
            {
                llvm::PointerType::get(context, 0), // hHeap
                llvm::Type::getInt32Ty(context),    // dwFlags
                llvm::PointerType::get(context, 0)  // lpMem
            },
            false);
        llvm::Function::Create(heapFreeType, llvm::Function::ExternalLinkage,
                               "HeapFree", module.get());

        // Create malloc wrapper function
        llvm::FunctionType *mallocType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),
            {llvm::Type::getInt64Ty(context)},
            false);
        llvm::Function *mallocFunc = llvm::Function::Create(
            mallocType,
            llvm::Function::ExternalLinkage,
            "malloc",
            module.get());

        llvm::BasicBlock *entry = llvm::BasicBlock::Create(context, "entry", mallocFunc);
        llvm::BasicBlock *savedBlock = builder.GetInsertBlock();
        builder.SetInsertPoint(entry);

        llvm::Value *sizeArg = mallocFunc->getArg(0);
        llvm::Function *getProcessHeap = module->getFunction("GetProcessHeap");
        llvm::Function *heapAlloc = module->getFunction("HeapAlloc");

        llvm::Value *heap = builder.CreateCall(getProcessHeap, {});
        llvm::Value *ptr = builder.CreateCall(heapAlloc, {heap,
                                                          llvm::ConstantInt::get(context, llvm::APInt(32, 0, false)), // flags = 0
                                                          sizeArg});
        builder.CreateRet(ptr);

        if (savedBlock)
        {
            builder.SetInsertPoint(savedBlock);
        }

        // Create free wrapper function
        llvm::FunctionType *freeType = llvm::FunctionType::get(
            llvm::Type::getVoidTy(context),
            {llvm::PointerType::get(context, 0)},
            false);
        llvm::Function *freeFunc = llvm::Function::Create(
            freeType,
            llvm::Function::ExternalLinkage,
            "free",
            module.get());

        entry = llvm::BasicBlock::Create(context, "entry", freeFunc);
        savedBlock = builder.GetInsertBlock();
        builder.SetInsertPoint(entry);

        llvm::Value *ptrArg = freeFunc->getArg(0);
        llvm::Function *heapFree = module->getFunction("HeapFree");

        heap = builder.CreateCall(getProcessHeap, {});
        builder.CreateCall(heapFree, {heap,
                                      llvm::ConstantInt::get(context, llvm::APInt(32, 0, false)), // flags = 0
                                      ptrArg});
        builder.CreateRetVoid();

        if (savedBlock)
        {
            builder.SetInsertPoint(savedBlock);
        }
    }
    else
    {
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
