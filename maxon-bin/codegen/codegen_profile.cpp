#include "../codegen.h"
#include <llvm/IR/Constants.h>
#include <llvm/IR/DerivedTypes.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Instructions.h>
#include <llvm/IR/Intrinsics.h>
#include <llvm/IR/GlobalVariable.h>

void CodeGenerator::initProfiling() {
    // Create global counter for instruction count
    bbCountGlobal = new llvm::GlobalVariable(
        *module,
        llvm::Type::getInt64Ty(context),
        false,  // not constant
        llvm::GlobalValue::InternalLinkage,
        llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 0),
        "__maxon_instr_count"
    );
    
    // Declare Windows API functions for output (no CRT dependency)
    // ptr GetStdHandle(i32)
    llvm::FunctionType* getStdHandleType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {llvm::Type::getInt32Ty(context)},
        false
    );
    llvm::Function::Create(getStdHandleType, llvm::Function::ExternalLinkage, "GetStdHandle", module.get());
    
    // i32 WriteFile(ptr, ptr, i32, ptr, ptr)
    llvm::FunctionType* writeFileType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::PointerType::get(context, 0),  // hFile
            llvm::PointerType::get(context, 0),  // lpBuffer
            llvm::Type::getInt32Ty(context),     // nNumberOfBytesToWrite
            llvm::PointerType::get(context, 0),  // lpNumberOfBytesWritten
            llvm::PointerType::get(context, 0)   // lpOverlapped
        },
        false
    );
    llvm::Function::Create(writeFileType, llvm::Function::ExternalLinkage, "WriteFile", module.get());
    
    // Create helper function to output the count in binary format
    llvm::FunctionType* printFuncType = llvm::FunctionType::get(
        llvm::Type::getVoidTy(context),
        false
    );
    printBBCountFunc = llvm::Function::Create(
        printFuncType,
        llvm::Function::InternalLinkage,
        "__maxon_print_instr_count",
        module.get()
    );
    
    llvm::BasicBlock* printBB = llvm::BasicBlock::Create(context, "entry", printBBCountFunc);
    llvm::IRBuilder<> printBuilder(printBB);
    
    // Get stdout handle
    llvm::Function* getStdHandleFunc = module->getFunction("GetStdHandle");
    llvm::Value* stdoutHandle = printBuilder.CreateCall(
        getStdHandleFunc,
        {llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), -11)}  // STD_OUTPUT_HANDLE
    );
    
    // Create output buffer: "MAXON_PROFILE:" (14 bytes) + i64 count (8 bytes) = 22 bytes
    llvm::Constant* prefix = llvm::ConstantDataArray::getString(context, "MAXON_PROFILE:", false);
    llvm::GlobalVariable* prefixGlobal = new llvm::GlobalVariable(
        *module,
        prefix->getType(),
        true,
        llvm::GlobalValue::PrivateLinkage,
        prefix,
        ".str.profile_prefix"
    );
    
    // Allocate buffer on stack
    llvm::Value* buffer = printBuilder.CreateAlloca(llvm::Type::getInt8Ty(context), llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 22));
    
    // Copy prefix to buffer
    llvm::Function* memcpyFunc = llvm::Intrinsic::getOrInsertDeclaration(
        module.get(),
        llvm::Intrinsic::memcpy,
        {llvm::PointerType::get(context, 0), llvm::PointerType::get(context, 0), llvm::Type::getInt32Ty(context)}
    );
    printBuilder.CreateCall(memcpyFunc, {
        buffer,
        prefixGlobal,
        llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 14),
        llvm::ConstantInt::get(llvm::Type::getInt1Ty(context), false)
    });
    
    // Get pointer to the count portion (after prefix)
    llvm::Value* countPtr = printBuilder.CreateGEP(
        llvm::Type::getInt8Ty(context),
        buffer,
        llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 14)
    );
    
    // Load the instruction count
    llvm::Value* count = printBuilder.CreateLoad(llvm::Type::getInt64Ty(context), bbCountGlobal);
    
    // Store the count as 8 bytes at countPtr location
    // Use memcpy to avoid pointer type issues
    llvm::Value* countAlloca = printBuilder.CreateAlloca(llvm::Type::getInt64Ty(context));
    printBuilder.CreateStore(count, countAlloca);
    printBuilder.CreateCall(memcpyFunc, {
        countPtr,
        countAlloca,
        llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 8),
        llvm::ConstantInt::get(llvm::Type::getInt1Ty(context), false)
    });
    
    // Write the buffer to stdout
    llvm::Value* bytesWritten = printBuilder.CreateAlloca(llvm::Type::getInt32Ty(context));
    llvm::Function* writeFileFunc = module->getFunction("WriteFile");
    printBuilder.CreateCall(writeFileFunc, {
        stdoutHandle,
        buffer,
        llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 22),
        bytesWritten,
        llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))
    });
    
    printBuilder.CreateRetVoid();
}

void CodeGenerator::injectInstrCounter() {
    if (!enableProfiling || !bbCountGlobal) return;
    
    // This function instruments the entire module after generation
    // For each basic block, inject an increment at the start based on the number of instructions
    
    for (auto& func : *module) {
        if (func.isDeclaration()) continue;  // Skip declarations
        
        // Skip the profiling helper function itself to avoid self-instrumentation
        if (&func == printBBCountFunc) continue;
        
        for (auto& bb : func) {
            // Count instructions in this basic block (excluding the injected profiling code)
            int instrCount = 0;
            for (auto& instr : bb) {
                // Don't count terminator instructions or phi nodes
                if (!instr.isTerminator() && !llvm::isa<llvm::PHINode>(instr)) {
                    instrCount++;
                }
            }
            
            if (instrCount == 0) continue;  // Skip empty blocks
            
            // Inject counter increment at the start of the basic block
            llvm::IRBuilder<> bbBuilder(&bb, bb.getFirstInsertionPt());
            llvm::Value* current = bbBuilder.CreateLoad(llvm::Type::getInt64Ty(context), bbCountGlobal);
            llvm::Value* incremented = bbBuilder.CreateAdd(current, llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), instrCount));
            bbBuilder.CreateStore(incremented, bbCountGlobal);
        }
    }
}

void CodeGenerator::injectProfileOutput(llvm::Function* mainFunc) {
    if (!enableProfiling || !printBBCountFunc || !mainFunc) return;
    
    // Find all return instructions in main and inject the profile output before them
    for (auto& bb : *mainFunc) {
        for (auto it = bb.begin(); it != bb.end(); ++it) {
            if (llvm::isa<llvm::ReturnInst>(it)) {
                llvm::IRBuilder<> retBuilder(&bb, it);
                retBuilder.CreateCall(printBBCountFunc);
            }
        }
    }
}
