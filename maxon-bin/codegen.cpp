#include "codegen.h"
#include "lexer.h"
#include <llvm/IR/Verifier.h>
#include <llvm/IR/LegacyPassManager.h>
#include <llvm/Transforms/InstCombine/InstCombine.h>
#include <llvm/Transforms/Scalar.h>
#include <llvm/Transforms/Scalar/GVN.h>
#include <llvm/Transforms/Utils.h>
#include <llvm/Transforms/IPO/GlobalDCE.h>
#include <llvm/Passes/PassBuilder.h>
#include <llvm/Support/FileSystem.h>
#include <llvm/Support/TargetSelect.h>
#include <llvm/MC/TargetRegistry.h>
#include <llvm/Target/TargetMachine.h>
#include <llvm/Target/TargetOptions.h>
#include <llvm/Linker/Linker.h>
#include <llvm/Support/raw_ostream.h>
#include <lld/Common/Driver.h>
#include <lld/Common/CommonLinkerContext.h>
#include <stdexcept>
#include <iostream>

#ifdef _WIN32
#include <windows.h>
#endif

// Forward declare COFF driver function
namespace lld {
namespace coff {
bool link(llvm::ArrayRef<const char *> args, llvm::raw_ostream &stdoutOS,
          llvm::raw_ostream &stderrOS, bool exitEarly, bool disableOutput);
}
}

// Helper function to convert Maxon type string to LLVM type
static llvm::Type* getTypeFromString(llvm::LLVMContext& context, const std::string& typeStr,
                                     const std::map<std::string, llvm::StructType*>* structTypes = nullptr) {
    // Validate that type strings come from the keywords map
    if (!Lexer::isTypeString(typeStr) && typeStr != "void" && typeStr[0] != '[' && 
        (structTypes == nullptr || structTypes->find(typeStr) == structTypes->end())) {
        throw std::runtime_error("Unknown type: " + typeStr);
    }
    
    // Try to get LLVM type from keywords map first
    llvm::Type* llvmType = Lexer::getLLVMTypeForKeyword(typeStr, context);
    if (llvmType != nullptr) {
        return llvmType;
    }
    
    if (typeStr == "void") {
        return llvm::Type::getVoidTy(context);
    } else if (typeStr[0] == '[') {
        // Array type: [size]elementType
        // Parse the size and element type
        size_t closeBracket = typeStr.find(']');
        if (closeBracket == std::string::npos) {
            throw std::runtime_error("Invalid array type syntax: " + typeStr);
        }
        
        std::string sizeStr = typeStr.substr(1, closeBracket - 1);
        std::string elementTypeStr = typeStr.substr(closeBracket + 1);
        
        int arraySize = std::stoi(sizeStr);
        llvm::Type* elementType = getTypeFromString(context, elementTypeStr, structTypes);
        
        return llvm::ArrayType::get(elementType, arraySize);
    } else if (structTypes && structTypes->find(typeStr) != structTypes->end()) {
        // Struct type
        return structTypes->at(typeStr);
    } else {
        throw std::runtime_error("Unknown type: " + typeStr);
    }
}

// Convert a type to the form used for function parameters
// Arrays are passed as pointers
// Structs are passed as pointers (by reference)
static llvm::Type* getParamTypeFromString(llvm::LLVMContext& context, const std::string& typeStr,
                                          const std::map<std::string, llvm::StructType*>* structTypes = nullptr) {
    if (typeStr[0] == '[') {
        // Array parameter - pass as pointer (opaque pointer for arrays)
        return llvm::PointerType::get(context, 0);
    } else if (structTypes && structTypes->find(typeStr) != structTypes->end()) {
        // Struct parameter - pass as pointer (by reference)
        return llvm::PointerType::get(context, 0);
    } else {
        // Regular parameter type
        return getTypeFromString(context, typeStr, structTypes);
    }
}

// Check if a parameter type is an array
static bool isArrayParam(const std::string& typeStr) {
    // Only unsized arrays ([]type) need hidden length parameters
    // Fixed-size arrays ([N]type) don't need them
    return typeStr.size() >= 2 && typeStr[0] == '[' && typeStr[1] == ']';
}

CodeGenerator::CodeGenerator(const std::string& moduleName, bool debugInfo, bool verbose, bool profile)
    : builder(context), module(std::make_unique<llvm::Module>(moduleName, context)),
      generateDebugInfo(debugInfo), verbose(verbose), enableProfiling(profile), sourceFileName(moduleName) {
    if (generateDebugInfo) {
        initDebugInfo(moduleName);
    }
    if (enableProfiling) {
        initProfiling();
    }
}

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

llvm::Function* CodeGenerator::getOrDeclareMemset() {
    llvm::Function* memsetFunc = module->getFunction("memset");
    if (!memsetFunc) {
        llvm::FunctionType* memsetType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),
            {llvm::PointerType::get(context, 0), llvm::Type::getInt32Ty(context), llvm::Type::getInt64Ty(context)},
            false
        );
        memsetFunc = llvm::Function::Create(memsetType, llvm::Function::ExternalLinkage, "memset", module.get());
    }
    return memsetFunc;
}

void CodeGenerator::initHeapManagement() {
    // Check if malloc already exists
    if (module->getFunction("malloc")) {
        return;
    }
    
    // Declare Windows Heap API functions
    // ptr GetProcessHeap()
    llvm::FunctionType* getProcessHeapType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {},
        false
    );
    llvm::Function::Create(getProcessHeapType, llvm::Function::ExternalLinkage,
                          "GetProcessHeap", module.get());
    
    // ptr HeapAlloc(ptr heap, i32 flags, i64 bytes)
    llvm::FunctionType* heapAllocType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {
            llvm::PointerType::get(context, 0),  // hHeap
            llvm::Type::getInt32Ty(context),     // dwFlags
            llvm::Type::getInt64Ty(context)      // dwBytes
        },
        false
    );
    llvm::Function::Create(heapAllocType, llvm::Function::ExternalLinkage,
                          "HeapAlloc", module.get());
    
    // i32 HeapFree(ptr heap, i32 flags, ptr mem)
    llvm::FunctionType* heapFreeType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::PointerType::get(context, 0),  // hHeap
            llvm::Type::getInt32Ty(context),     // dwFlags
            llvm::PointerType::get(context, 0)   // lpMem
        },
        false
    );
    llvm::Function::Create(heapFreeType, llvm::Function::ExternalLinkage,
                          "HeapFree", module.get());
    
    // Create malloc wrapper function
    llvm::FunctionType* mallocType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {llvm::Type::getInt64Ty(context)},
        false
    );
    llvm::Function* mallocFunc = llvm::Function::Create(
        mallocType,
        llvm::Function::ExternalLinkage,
        "malloc",
        module.get()
    );
    
    llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", mallocFunc);
    llvm::BasicBlock* savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(entry);
    
    llvm::Value* sizeArg = mallocFunc->getArg(0);
    llvm::Function* getProcessHeap = module->getFunction("GetProcessHeap");
    llvm::Function* heapAlloc = module->getFunction("HeapAlloc");
    
    llvm::Value* heap = builder.CreateCall(getProcessHeap, {});
    llvm::Value* ptr = builder.CreateCall(heapAlloc, {
        heap,
        llvm::ConstantInt::get(context, llvm::APInt(32, 0, false)),  // flags = 0
        sizeArg
    });
    builder.CreateRet(ptr);
    
    if (savedBlock) {
        builder.SetInsertPoint(savedBlock);
    }
    
    // Create free wrapper function
    llvm::FunctionType* freeType = llvm::FunctionType::get(
        llvm::Type::getVoidTy(context),
        {llvm::PointerType::get(context, 0)},
        false
    );
    llvm::Function* freeFunc = llvm::Function::Create(
        freeType,
        llvm::Function::ExternalLinkage,
        "free",
        module.get()
    );
    
    entry = llvm::BasicBlock::Create(context, "entry", freeFunc);
    savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(entry);
    
    llvm::Value* ptrArg = freeFunc->getArg(0);
    llvm::Function* heapFree = module->getFunction("HeapFree");
    
    heap = builder.CreateCall(getProcessHeap, {});
    builder.CreateCall(heapFree, {
        heap,
        llvm::ConstantInt::get(context, llvm::APInt(32, 0, false)),  // flags = 0
        ptrArg
    });
    builder.CreateRetVoid();
    
    if (savedBlock) {
        builder.SetInsertPoint(savedBlock);
    }
}

void CodeGenerator::initStandardLibrary() {
    // Check if print function already exists (to avoid double-initialization)
    if (module->getFunction("print")) {
        return;
    }
    
    // Get memset function declaration once for use throughout this function
    llvm::Function* memsetFunc = getOrDeclareMemset();
    
    // Declare Windows API functions for direct I/O (no CRT dependency)
    // ptr GetStdHandle(i32)
    llvm::FunctionType* getStdHandleType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {llvm::Type::getInt32Ty(context)},
        false
    );
    llvm::Function::Create(getStdHandleType, llvm::Function::ExternalLinkage,
                          "GetStdHandle", module.get());
    
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
    llvm::Function::Create(writeFileType, llvm::Function::ExternalLinkage,
                          "WriteFile", module.get());
    
    // Create print(int) -> int function
    llvm::FunctionType* printFuncType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {llvm::Type::getInt32Ty(context)},
        false
    );
    llvm::Function* printFunc = llvm::Function::Create(
        printFuncType,
        llvm::Function::ExternalLinkage,
        "print",
        module.get()
    );
    
    // Generate the body of the print function
    llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", printFunc);
    llvm::BasicBlock* savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(entry);
    
    llvm::Value* valueArg = printFunc->getArg(0);
    
    // Allocate buffers on stack
    llvm::Type* charType = llvm::Type::getInt8Ty(context);
    llvm::Type* bufferType = llvm::ArrayType::get(charType, 12);
    llvm::Type* tempType = llvm::ArrayType::get(charType, 12);
    
    llvm::Value* buffer = builder.CreateAlloca(bufferType, nullptr, "buffer");
    llvm::Value* temp = builder.CreateAlloca(tempType, nullptr, "temp");
    llvm::Value* bytesWritten = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "bytesWritten");
    
    // Initialize buffers to zero using runtime memset
    builder.CreateCall(memsetFunc, {buffer, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 12)});
    builder.CreateCall(memsetFunc, {temp, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 12)});
    
    // Check if value is zero
    llvm::BasicBlock* zeroCase = llvm::BasicBlock::Create(context, "zero_case", printFunc);
    llvm::BasicBlock* nonZeroCase = llvm::BasicBlock::Create(context, "non_zero", printFunc);
    llvm::BasicBlock* formatDone = llvm::BasicBlock::Create(context, "format_done", printFunc);
    
    llvm::Value* isZero = builder.CreateICmpEQ(valueArg, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(isZero, zeroCase, nonZeroCase);
    
    // Zero case: buffer[0] = '0', length = 1
    builder.SetInsertPoint(zeroCase);
    llvm::Value* bufferZeroPtr = builder.CreateInBoundsGEP(bufferType, buffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), bufferZeroPtr);
    builder.CreateBr(formatDone);
    
    // Non-zero case: format the integer
    builder.SetInsertPoint(nonZeroCase);
    
    // Handle negative numbers
    llvm::BasicBlock* negativeCase = llvm::BasicBlock::Create(context, "negative", printFunc);
    llvm::BasicBlock* positiveCase = llvm::BasicBlock::Create(context, "positive", printFunc);
    llvm::BasicBlock* extractDigits = llvm::BasicBlock::Create(context, "extract_digits", printFunc);
    
    llvm::Value* isNegative = builder.CreateICmpSLT(valueArg, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(isNegative, negativeCase, positiveCase);
    
    // Negative case: add '-' and negate value
    builder.SetInsertPoint(negativeCase);
    llvm::Value* bufferNegPtr = builder.CreateInBoundsGEP(bufferType, buffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '-'), bufferNegPtr);
    llvm::Value* absValue = builder.CreateNeg(valueArg);
    llvm::Value* startOffsetNeg = llvm::ConstantInt::get(context, llvm::APInt(32, 1, true));
    builder.CreateBr(extractDigits);
    
    // Positive case
    builder.SetInsertPoint(positiveCase);
    llvm::Value* startOffsetPos = llvm::ConstantInt::get(context, llvm::APInt(32, 0, true));
    builder.CreateBr(extractDigits);
    
    // Extract digits
    builder.SetInsertPoint(extractDigits);
    llvm::PHINode* valuePhi = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "value_abs");
    valuePhi->addIncoming(absValue, negativeCase);
    valuePhi->addIncoming(valueArg, positiveCase);
    llvm::PHINode* offsetPhi = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "start_offset");
    offsetPhi->addIncoming(startOffsetNeg, negativeCase);
    offsetPhi->addIncoming(startOffsetPos, positiveCase);
    
    // Digit extraction loop
    llvm::BasicBlock* digitLoop = llvm::BasicBlock::Create(context, "digit_loop", printFunc);
    llvm::BasicBlock* digitLoopBody = llvm::BasicBlock::Create(context, "digit_loop_body", printFunc);
    llvm::BasicBlock* reverseLoop = llvm::BasicBlock::Create(context, "reverse_loop", printFunc);
    llvm::BasicBlock* reverseBody = llvm::BasicBlock::Create(context, "reverse_body", printFunc);
    
    builder.CreateBr(digitLoop);
    
    // Digit loop header
    builder.SetInsertPoint(digitLoop);
    llvm::PHINode* remainingValue = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "remaining");
    remainingValue->addIncoming(valuePhi, extractDigits);
    llvm::PHINode* tempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "temp_idx");
    tempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), extractDigits);
    
    llvm::Value* loopCond = builder.CreateICmpNE(remainingValue, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(loopCond, digitLoopBody, reverseLoop);
    
    // Digit loop body: extract digit, store in temp
    builder.SetInsertPoint(digitLoopBody);
    llvm::Value* digit = builder.CreateSRem(remainingValue, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value* digitChar = builder.CreateAdd(digit, llvm::ConstantInt::get(context, llvm::APInt(32, '0', true)));
    llvm::Value* digitChar8 = builder.CreateTrunc(digitChar, charType);
    
    llvm::Value* tempPtr = builder.CreateInBoundsGEP(tempType, temp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), tempIdx});
    builder.CreateStore(digitChar8, tempPtr);
    
    llvm::Value* nextRemaining = builder.CreateSDiv(remainingValue, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value* nextTempIdx = builder.CreateAdd(tempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    
    remainingValue->addIncoming(nextRemaining, digitLoopBody);
    tempIdx->addIncoming(nextTempIdx, digitLoopBody);
    builder.CreateBr(digitLoop);
    
    // Reverse loop: copy from temp to buffer in reverse order
    builder.SetInsertPoint(reverseLoop);
    llvm::PHINode* reverseIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "reverse_idx");
    reverseIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), digitLoop);
    
    // Pass through offsetPhi and tempIdx via PHI nodes
    llvm::PHINode* offsetInReverse = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "offset_in_reverse");
    offsetInReverse->addIncoming(offsetPhi, digitLoop);
    llvm::PHINode* digitCountInReverse = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "digit_count_in_reverse");
    digitCountInReverse->addIncoming(tempIdx, digitLoop);
    
    llvm::Value* reverseCond = builder.CreateICmpSLT(reverseIdx, digitCountInReverse);
    builder.CreateCondBr(reverseCond, reverseBody, formatDone);
    
    // Reverse body
    builder.SetInsertPoint(reverseBody);
    llvm::Value* srcIdx = builder.CreateSub(digitCountInReverse, reverseIdx);
    srcIdx = builder.CreateSub(srcIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    
    llvm::Value* srcPtr = builder.CreateInBoundsGEP(tempType, temp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), srcIdx});
    llvm::Value* digitValue = builder.CreateLoad(charType, srcPtr);
    
    llvm::Value* dstIdx = builder.CreateAdd(offsetInReverse, reverseIdx);
    llvm::Value* dstPtr = builder.CreateInBoundsGEP(bufferType, buffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), dstIdx});
    builder.CreateStore(digitValue, dstPtr);
    
    llvm::Value* nextReverseIdx = builder.CreateAdd(reverseIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    reverseIdx->addIncoming(nextReverseIdx, reverseBody);
    offsetInReverse->addIncoming(offsetInReverse, reverseBody);
    digitCountInReverse->addIncoming(digitCountInReverse, reverseBody);
    builder.CreateBr(reverseLoop);
    
    // Format done - write to stdout
    builder.SetInsertPoint(formatDone);
    
    // Create PHI nodes for values from different predecessors
    llvm::PHINode* finalOffset = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_offset");
    finalOffset->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), zeroCase);  // Zero case has offset 0
    finalOffset->addIncoming(offsetInReverse, reverseLoop);
    
    llvm::PHINode* finalDigitCount = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_digit_count");
    finalDigitCount->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)), zeroCase);  // Zero case has 1 digit
    finalDigitCount->addIncoming(digitCountInReverse, reverseLoop);
    
    // Compute final length = offset + digit_count (works for both zero and non-zero cases)
    llvm::Value* finalLength = builder.CreateAdd(finalOffset, finalDigitCount);
    
    // Get stdout handle (-11)
    llvm::Function* getStdHandle = module->getFunction("GetStdHandle");
    llvm::Value* stdoutHandle = builder.CreateCall(getStdHandle,
        {llvm::ConstantInt::get(context, llvm::APInt(32, -11, true))});
    
    // Get buffer pointer
    llvm::Value* bufferPtr = builder.CreateBitCast(buffer, llvm::PointerType::get(context, 0));
    
    // Call WriteFile
    llvm::Function* writeFile = module->getFunction("WriteFile");
    builder.CreateCall(writeFile, {
        stdoutHandle,
        bufferPtr,
        finalLength,
        bytesWritten,
        llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))
    });
    
    // Write newline
    llvm::Constant* newlineStr = llvm::ConstantDataArray::getString(context, "\n", false);
    llvm::GlobalVariable* newlineVar = new llvm::GlobalVariable(
        *module,
        newlineStr->getType(),
        true,
        llvm::GlobalValue::PrivateLinkage,
        newlineStr,
        ".str.newline"
    );
    llvm::Value* newlinePtr = builder.CreateBitCast(newlineVar, llvm::PointerType::get(context, 0));
    builder.CreateCall(writeFile, {
        stdoutHandle,
        newlinePtr,
        llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)),
        bytesWritten,
        llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))
    });
    
    // Return 0
    builder.CreateRet(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    
    // Add attribute to prevent LLVM from converting memset calls to intrinsics
    printFunc->addFnAttr("no-builtin-memset");
    
    // Restore insert point
    if (savedBlock) {
        builder.SetInsertPoint(savedBlock);
    }
    
    // Create print_float(float, int) -> int function
    llvm::FunctionType* printFloatFuncType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {llvm::Type::getDoubleTy(context), llvm::Type::getInt32Ty(context)},
        false
    );
    llvm::Function* printFloatFunc = llvm::Function::Create(
        printFloatFuncType,
        llvm::Function::ExternalLinkage,
        "print_float",
        module.get()
    );
    
    // Generate the body of the print_float function
    llvm::BasicBlock* floatEntry = llvm::BasicBlock::Create(context, "entry", printFloatFunc);
    savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(floatEntry);
    
    llvm::Value* floatValueArg = printFloatFunc->getArg(0);
    llvm::Value* precisionArg = printFloatFunc->getArg(1);
    
    // Store parameter to local variable and load it back to ensure proper handling
    llvm::Value* floatValueLocal = builder.CreateAlloca(llvm::Type::getDoubleTy(context), nullptr, "float_value_local");
    builder.CreateStore(floatValueArg, floatValueLocal);
    llvm::Value* floatValue = builder.CreateLoad(llvm::Type::getDoubleTy(context), floatValueLocal, "float_value");
    
    // Allocate buffer on stack (32 bytes for float)
    llvm::Type* floatBufferType = llvm::ArrayType::get(charType, 32);
    llvm::Value* floatBuffer = builder.CreateAlloca(floatBufferType, nullptr, "buffer");
    llvm::Value* floatBytesWritten = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "bytesWritten");
    
    // Initialize buffer to zero using runtime memset
    builder.CreateCall(memsetFunc, {floatBuffer, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 32)});
    
    // Check if value is zero (exactly 0.0)
    llvm::BasicBlock* floatZeroCase = llvm::BasicBlock::Create(context, "zero_case", printFloatFunc);
    llvm::BasicBlock* floatNonZeroCase = llvm::BasicBlock::Create(context, "non_zero", printFloatFunc);
    llvm::BasicBlock* floatFormatDone = llvm::BasicBlock::Create(context, "format_done", printFloatFunc);
    
    llvm::Value* floatIsZero = builder.CreateFCmpOEQ(floatValue, llvm::ConstantFP::get(context, llvm::APFloat(0.0)));
    builder.CreateCondBr(floatIsZero, floatZeroCase, floatNonZeroCase);
    
    // Zero case: format as "0.000000" (with precision decimal places)
    builder.SetInsertPoint(floatZeroCase);
    llvm::Value* zeroPtr0 = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), zeroPtr0);
    llvm::Value* zeroPtr1 = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 1))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '.'), zeroPtr1);
    
    // Fill in precision decimal places with '0' (runtime loop)
    llvm::BasicBlock* zeroFillLoop = llvm::BasicBlock::Create(context, "zero_fill_loop", printFloatFunc);
    llvm::BasicBlock* zeroFillBody = llvm::BasicBlock::Create(context, "zero_fill_body", printFloatFunc);
    llvm::BasicBlock* zeroFillDone = llvm::BasicBlock::Create(context, "zero_fill_done", printFloatFunc);
    
    builder.CreateBr(zeroFillLoop);
    
    builder.SetInsertPoint(zeroFillLoop);
    llvm::PHINode* zeroFillIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "zero_fill_idx");
    zeroFillIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), floatZeroCase);
    
    llvm::Value* zeroFillCond = builder.CreateICmpSLT(zeroFillIdx, precisionArg);
    builder.CreateCondBr(zeroFillCond, zeroFillBody, zeroFillDone);
    
    builder.SetInsertPoint(zeroFillBody);
    llvm::Value* zeroFillOffset = builder.CreateAdd(llvm::ConstantInt::get(context, llvm::APInt(32, 2, true)), zeroFillIdx);
    llvm::Value* zeroDecPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), zeroFillOffset});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), zeroDecPtr);
    
    llvm::Value* nextZeroFillIdx = builder.CreateAdd(zeroFillIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    zeroFillIdx->addIncoming(nextZeroFillIdx, zeroFillBody);
    builder.CreateBr(zeroFillLoop);
    
    builder.SetInsertPoint(zeroFillDone);
    llvm::Value* zeroLength = builder.CreateAdd(llvm::ConstantInt::get(context, llvm::APInt(32, 2, true)), precisionArg);
    builder.CreateBr(floatFormatDone);
    
    // Non-zero case: format the float
    builder.SetInsertPoint(floatNonZeroCase);
    llvm::Value* floatPos = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "pos");
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), floatPos);
    
    // Handle negative numbers
    llvm::BasicBlock* floatNegativeCase = llvm::BasicBlock::Create(context, "negative", printFloatFunc);
    llvm::BasicBlock* floatPositiveCase = llvm::BasicBlock::Create(context, "positive", printFloatFunc);
    llvm::BasicBlock* floatExtractInt = llvm::BasicBlock::Create(context, "extract_int", printFloatFunc);
    
    // Check if value is negative using fcmp (handles -0.0 correctly and avoids optimizer issues)
    llvm::Value* floatIsNegative = builder.CreateFCmpOLT(floatValue, llvm::ConstantFP::get(context, llvm::APFloat(0.0)));
    builder.CreateCondBr(floatIsNegative, floatNegativeCase, floatPositiveCase);
    
    // Negative case: add '-' and take absolute value
    builder.SetInsertPoint(floatNegativeCase);
    llvm::Value* negPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '-'), negPtr);
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)), floatPos);
    llvm::Function* fabsFn = llvm::Intrinsic::getOrInsertDeclaration(
        module.get(),
        llvm::Intrinsic::fabs,
        {llvm::Type::getDoubleTy(context)}
    );
    llvm::Value* absFloatValue = builder.CreateCall(fabsFn, {floatValue});
    builder.CreateBr(floatExtractInt);
    
    // Positive case
    builder.SetInsertPoint(floatPositiveCase);
    builder.CreateBr(floatExtractInt);
    
    // Extract integer and fractional parts
    builder.SetInsertPoint(floatExtractInt);
    llvm::PHINode* workValue = builder.CreatePHI(llvm::Type::getDoubleTy(context), 2, "work_value");
    workValue->addIncoming(absFloatValue, floatNegativeCase);
    workValue->addIncoming(floatValue, floatPositiveCase);
    
    // Use llvm.round to handle precision issues where values like 1.9999999 should be 2.0
    // Check if value is very close to next integer (within epsilon)
    llvm::Value* intPartTrunc = builder.CreateFPToSI(workValue, llvm::Type::getInt32Ty(context), "int_part_trunc");
    llvm::Value* intPartTruncFloat = builder.CreateSIToFP(intPartTrunc, llvm::Type::getDoubleTy(context), "int_part_trunc_float");
    llvm::Value* fracPartInitial = builder.CreateFSub(workValue, intPartTruncFloat, "frac_part_initial");
    
    // If fractional part > 0.9999, it's likely a precision error and should round up
    llvm::Value* shouldRoundUp = builder.CreateFCmpOGT(fracPartInitial, llvm::ConstantFP::get(context, llvm::APFloat(0.9999)));
    llvm::Value* intPartRounded = builder.CreateAdd(intPartTrunc, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 1), "int_part_rounded");
    llvm::Value* intPart = builder.CreateSelect(shouldRoundUp, intPartRounded, intPartTrunc, "int_part");
    
    // Recalculate integer part as float and fractional part with correct integer
    llvm::Value* intPartFloat = builder.CreateSIToFP(intPart, llvm::Type::getDoubleTy(context), "int_part_float");
    llvm::Value* fracPart = builder.CreateFSub(workValue, intPartFloat, "frac_part");
    
    // Use intPart and fracPart for formatting
    // Format integer part (similar to print(int))
    llvm::Type* floatTempType = llvm::ArrayType::get(charType, 20);
    llvm::Value* floatTemp = builder.CreateAlloca(floatTempType, nullptr, "float_temp");
    builder.CreateCall(memsetFunc, {floatTemp, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 20)});
    
    llvm::Value* floatTempIdx = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "float_temp_idx");
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), floatTempIdx);
    
    // Check if integer part is zero
    llvm::BasicBlock* intZeroCase = llvm::BasicBlock::Create(context, "int_zero", printFloatFunc);
    llvm::BasicBlock* intNonZeroCase = llvm::BasicBlock::Create(context, "int_nonzero", printFloatFunc);
    llvm::BasicBlock* intDigitsDone = llvm::BasicBlock::Create(context, "int_digits_done", printFloatFunc);
    
    llvm::Value* intIsZero = builder.CreateICmpEQ(intPart, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(intIsZero, intZeroCase, intNonZeroCase);
    
    // Integer part is zero: just write '0'
    builder.SetInsertPoint(intZeroCase);
    llvm::Value* tempZeroPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 19))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), tempZeroPtr);
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 18, true)), floatTempIdx);
    builder.CreateBr(intDigitsDone);
    
    // Integer part is non-zero: extract digits
    builder.SetInsertPoint(intNonZeroCase);
    llvm::BasicBlock* intDigitLoop = llvm::BasicBlock::Create(context, "int_digit_loop", printFloatFunc);
    llvm::BasicBlock* intDigitBody = llvm::BasicBlock::Create(context, "int_digit_body", printFloatFunc);
    builder.CreateBr(intDigitLoop);
    
    builder.SetInsertPoint(intDigitLoop);
    llvm::PHINode* remainingInt = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "remaining_int");
    remainingInt->addIncoming(intPart, intNonZeroCase);
    llvm::PHINode* currentTempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "current_temp_idx");
    currentTempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), intNonZeroCase);
    
    llvm::Value* intLoopCond = builder.CreateICmpNE(remainingInt, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(intLoopCond, intDigitBody, intDigitsDone);
    
    builder.SetInsertPoint(intDigitBody);
    llvm::Value* intDigit = builder.CreateSRem(remainingInt, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value* intDigitChar = builder.CreateAdd(intDigit, llvm::ConstantInt::get(context, llvm::APInt(32, '0', true)));
    llvm::Value* intDigitChar8 = builder.CreateTrunc(intDigitChar, charType);
    
    llvm::Value* tempDigitPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), currentTempIdx});
    builder.CreateStore(intDigitChar8, tempDigitPtr);
    
    llvm::Value* nextRemainingInt = builder.CreateSDiv(remainingInt, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value* nextIntTempIdx = builder.CreateSub(currentTempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    
    remainingInt->addIncoming(nextRemainingInt, intDigitBody);
    currentTempIdx->addIncoming(nextIntTempIdx, intDigitBody);
    builder.CreateBr(intDigitLoop);
    
    // Integer digits done: copy to buffer
    builder.SetInsertPoint(intDigitsDone);
    llvm::PHINode* finalTempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_temp_idx");
    finalTempIdx->addIncoming(currentTempIdx, intDigitLoop);
    finalTempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 18, true)), intZeroCase);
    
    // Copy integer digits from temp to buffer
    llvm::Value* startIdx = builder.CreateAdd(finalTempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    llvm::Value* intLength = builder.CreateSub(llvm::ConstantInt::get(context, llvm::APInt(32, 20, true)), startIdx);
    
    llvm::BasicBlock* copyIntLoop = llvm::BasicBlock::Create(context, "copy_int_loop", printFloatFunc);
    llvm::BasicBlock* copyIntBody = llvm::BasicBlock::Create(context, "copy_int_body", printFloatFunc);
    llvm::BasicBlock* copyIntDone = llvm::BasicBlock::Create(context, "copy_int_done", printFloatFunc);
    
    builder.CreateBr(copyIntLoop);
    
    builder.SetInsertPoint(copyIntLoop);
    llvm::PHINode* copyIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "copy_idx");
    copyIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), intDigitsDone);
    
    llvm::Value* copyCond = builder.CreateICmpSLT(copyIdx, intLength);
    builder.CreateCondBr(copyCond, copyIntBody, copyIntDone);
    
    builder.SetInsertPoint(copyIntBody);
    llvm::Value* currentPos = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos, "current_pos");
    llvm::Value* intSrcIdx = builder.CreateAdd(startIdx, copyIdx);
    llvm::Value* intSrcPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), intSrcIdx});
    llvm::Value* charValue = builder.CreateLoad(charType, intSrcPtr);
    
    llvm::Value* intDstIdx = builder.CreateAdd(currentPos, copyIdx);
    llvm::Value* intDstPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), intDstIdx});
    builder.CreateStore(charValue, intDstPtr);
    
    llvm::Value* nextCopyIdx = builder.CreateAdd(copyIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    copyIdx->addIncoming(nextCopyIdx, copyIntBody);
    builder.CreateBr(copyIntLoop);
    
    // Update position after integer part
    builder.SetInsertPoint(copyIntDone);
    llvm::Value* currentPosAfterInt = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos, "pos_after_int");
    llvm::Value* newPos = builder.CreateAdd(currentPosAfterInt, intLength);
    builder.CreateStore(newPos, floatPos);
    
    // Add decimal point
    llvm::Value* decimalPos = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value* decimalPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), decimalPos});
    builder.CreateStore(llvm::ConstantInt::get(charType, '.'), decimalPtr);
    llvm::Value* posAfterDecimal = builder.CreateAdd(decimalPos, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    builder.CreateStore(posAfterDecimal, floatPos);
    
    // Compute scale = 10^precision dynamically
    llvm::BasicBlock* scaleLoop = llvm::BasicBlock::Create(context, "scale_loop", printFloatFunc);
    llvm::BasicBlock* scaleBody = llvm::BasicBlock::Create(context, "scale_body", printFloatFunc);
    llvm::BasicBlock* scaleDone = llvm::BasicBlock::Create(context, "scale_done", printFloatFunc);
    
    builder.CreateBr(scaleLoop);
    
    builder.SetInsertPoint(scaleLoop);
    llvm::PHINode* scaleIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "scale_idx");
    scaleIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), copyIntDone);
    llvm::PHINode* scaleAccum = builder.CreatePHI(llvm::Type::getDoubleTy(context), 2, "scale_accum");
    scaleAccum->addIncoming(llvm::ConstantFP::get(context, llvm::APFloat(1.0)), copyIntDone);
    
    llvm::Value* scaleCond = builder.CreateICmpSLT(scaleIdx, precisionArg);
    builder.CreateCondBr(scaleCond, scaleBody, scaleDone);
    
    builder.SetInsertPoint(scaleBody);
    llvm::Value* nextScale = builder.CreateFMul(scaleAccum, llvm::ConstantFP::get(context, llvm::APFloat(10.0)), "next_scale");
    llvm::Value* nextScaleIdx = builder.CreateAdd(scaleIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    
    scaleAccum->addIncoming(nextScale, scaleBody);
    scaleIdx->addIncoming(nextScaleIdx, scaleBody);
    builder.CreateBr(scaleLoop);
    
    builder.SetInsertPoint(scaleDone);
    llvm::Value* scale = scaleAccum;
    
    // Scale by 10^precision and round
    llvm::Value* fracScaled = builder.CreateFMul(fracPart, scale, "frac_scaled");
    llvm::Value* fracRounded = builder.CreateFAdd(fracScaled, llvm::ConstantFP::get(context, llvm::APFloat(0.5)), "frac_rounded");
    // Use int64 to avoid overflow for high precision values
    llvm::Value* fracInt = builder.CreateFPToSI(fracRounded, llvm::Type::getInt64Ty(context), "frac_int");
    
    // Handle overflow case: if fracInt >= scale (10^precision), need to carry to integer part
    // This happens when rounding causes fractional part to overflow, e.g., 0.9999995 with precision 6
    llvm::BasicBlock* fracOverflowCheck = llvm::BasicBlock::Create(context, "frac_overflow_check", printFloatFunc);
    llvm::BasicBlock* fracOverflowCase = llvm::BasicBlock::Create(context, "frac_overflow", printFloatFunc);
    llvm::BasicBlock* fracNoOverflow = llvm::BasicBlock::Create(context, "frac_no_overflow", printFloatFunc);
    
    builder.CreateBr(fracOverflowCheck);
    
    builder.SetInsertPoint(fracOverflowCheck);
    llvm::Value* scaleInt64 = builder.CreateFPToSI(scale, llvm::Type::getInt64Ty(context));
    llvm::Value* hasOverflow = builder.CreateICmpSGE(fracInt, scaleInt64);
    builder.CreateCondBr(hasOverflow, fracOverflowCase, fracNoOverflow);
    
    // Overflow case: need to increment integer part and regenerate buffer
    builder.SetInsertPoint(fracOverflowCase);
    // Increment the integer part
    llvm::Value* newIntPart = builder.CreateAdd(intPart, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    // Clear and regenerate integer part in buffer
    // Reset position to handle potential sign
    llvm::Value* signOffset = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value* overflowIsNegative = builder.CreateICmpEQ(signOffset, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    llvm::Value* overflowStartPos = builder.CreateSelect(overflowIsNegative, 
        llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)), 
        llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    
    // Clear temp buffer and regenerate digits
    builder.CreateCall(memsetFunc, {floatTemp, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 20)});
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), floatTempIdx);
    
    // Extract digits of newIntPart
    llvm::BasicBlock* overflowIntLoop = llvm::BasicBlock::Create(context, "overflow_int_loop", printFloatFunc);
    llvm::BasicBlock* overflowIntBody = llvm::BasicBlock::Create(context, "overflow_int_body", printFloatFunc);
    llvm::BasicBlock* overflowIntDone = llvm::BasicBlock::Create(context, "overflow_int_done", printFloatFunc);
    
    builder.CreateBr(overflowIntLoop);
    
    builder.SetInsertPoint(overflowIntLoop);
    llvm::PHINode* overflowRemaining = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "overflow_remaining");
    overflowRemaining->addIncoming(newIntPart, fracOverflowCase);
    llvm::PHINode* overflowTempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "overflow_temp_idx");
    overflowTempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), fracOverflowCase);
    
    llvm::Value* overflowLoopCond = builder.CreateICmpNE(overflowRemaining, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(overflowLoopCond, overflowIntBody, overflowIntDone);
    
    builder.SetInsertPoint(overflowIntBody);
    llvm::Value* overflowDigit = builder.CreateSRem(overflowRemaining, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value* overflowDigitChar = builder.CreateAdd(overflowDigit, llvm::ConstantInt::get(context, llvm::APInt(32, '0', true)));
    llvm::Value* overflowDigitChar8 = builder.CreateTrunc(overflowDigitChar, charType);
    llvm::Value* overflowTempPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowTempIdx});
    builder.CreateStore(overflowDigitChar8, overflowTempPtr);
    llvm::Value* nextOverflowRemaining = builder.CreateSDiv(overflowRemaining, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value* nextOverflowTempIdx = builder.CreateSub(overflowTempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    overflowRemaining->addIncoming(nextOverflowRemaining, overflowIntBody);
    overflowTempIdx->addIncoming(nextOverflowTempIdx, overflowIntBody);
    builder.CreateBr(overflowIntLoop);
    
    builder.SetInsertPoint(overflowIntDone);
    // Copy the new integer part to buffer
    llvm::Value* overflowTempFinalIdx = overflowTempIdx;
    llvm::Value* overflowStartIdx = builder.CreateAdd(overflowTempFinalIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    llvm::Value* overflowIntLen = builder.CreateSub(llvm::ConstantInt::get(context, llvm::APInt(32, 20, true)), overflowStartIdx);
    
    llvm::BasicBlock* overflowCopyLoop = llvm::BasicBlock::Create(context, "overflow_copy_loop", printFloatFunc);
    llvm::BasicBlock* overflowCopyBody = llvm::BasicBlock::Create(context, "overflow_copy_body", printFloatFunc);
    llvm::BasicBlock* overflowCopyDone = llvm::BasicBlock::Create(context, "overflow_copy_done", printFloatFunc);
    
    builder.CreateBr(overflowCopyLoop);
    
    builder.SetInsertPoint(overflowCopyLoop);
    llvm::PHINode* overflowCopyIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "overflow_copy_idx");
    overflowCopyIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), overflowIntDone);
    llvm::Value* overflowCopyCond = builder.CreateICmpSLT(overflowCopyIdx, overflowIntLen);
    builder.CreateCondBr(overflowCopyCond, overflowCopyBody, overflowCopyDone);
    
    builder.SetInsertPoint(overflowCopyBody);
    llvm::Value* overflowSrcIdx = builder.CreateAdd(overflowStartIdx, overflowCopyIdx);
    llvm::Value* overflowSrcPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowSrcIdx});
    llvm::Value* overflowSrcChar = builder.CreateLoad(charType, overflowSrcPtr);
    llvm::Value* overflowDstIdx = builder.CreateAdd(overflowStartPos, overflowCopyIdx);
    llvm::Value* overflowDstPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowDstIdx});
    builder.CreateStore(overflowSrcChar, overflowDstPtr);
    llvm::Value* nextOverflowCopyIdx = builder.CreateAdd(overflowCopyIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    overflowCopyIdx->addIncoming(nextOverflowCopyIdx, overflowCopyBody);
    builder.CreateBr(overflowCopyLoop);
    
    builder.SetInsertPoint(overflowCopyDone);
    llvm::Value* overflowNewPos = builder.CreateAdd(overflowStartPos, overflowIntLen);
    builder.CreateStore(overflowNewPos, floatPos);
    
    // Add decimal point
    llvm::Value* overflowDecimalPos = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value* overflowDecimalPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowDecimalPos});
    builder.CreateStore(llvm::ConstantInt::get(charType, '.'), overflowDecimalPtr);
    llvm::Value* overflowPosAfterDecimal = builder.CreateAdd(overflowDecimalPos, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    builder.CreateStore(overflowPosAfterDecimal, floatPos);
    
    // Set fracInt to 0 (all zeros in fractional part)
    llvm::Value* overflowFracInt = llvm::ConstantInt::get(context, llvm::APInt(64, 0, true));
    builder.CreateBr(fracNoOverflow);
    
    // No overflow case
    builder.SetInsertPoint(fracNoOverflow);
    llvm::PHINode* finalFracInt = builder.CreatePHI(llvm::Type::getInt64Ty(context), 2, "final_frac_int");
    finalFracInt->addIncoming(overflowFracInt, overflowCopyDone);
    finalFracInt->addIncoming(fracInt, fracOverflowCheck);
    
    // Compute precision - 1 for loop initialization
    llvm::Value* precisionMinus1 = builder.CreateSub(precisionArg, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    
    // Extract fractional digits (right to left)
    llvm::BasicBlock* fracDigitLoop = llvm::BasicBlock::Create(context, "frac_digit_loop", printFloatFunc);
    llvm::BasicBlock* fracDigitBody = llvm::BasicBlock::Create(context, "frac_digit_body", printFloatFunc);
    llvm::BasicBlock* fracDigitsDone = llvm::BasicBlock::Create(context, "frac_digits_done", printFloatFunc);
    
    builder.CreateBr(fracDigitLoop);
    
    builder.SetInsertPoint(fracDigitLoop);
    llvm::PHINode* fracIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "frac_idx");
    fracIdx->addIncoming(precisionMinus1, fracNoOverflow);
    llvm::PHINode* fracRemaining = builder.CreatePHI(llvm::Type::getInt64Ty(context), 2, "frac_remaining");
    fracRemaining->addIncoming(finalFracInt, fracNoOverflow);
    
    llvm::Value* fracCond = builder.CreateICmpSGE(fracIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(fracCond, fracDigitBody, fracDigitsDone);
    
    builder.SetInsertPoint(fracDigitBody);
    llvm::Value* fracDigit = builder.CreateSRem(fracRemaining, llvm::ConstantInt::get(context, llvm::APInt(64, 10, true)));
    llvm::Value* fracDigitChar = builder.CreateAdd(fracDigit, llvm::ConstantInt::get(context, llvm::APInt(64, '0', true)));
    llvm::Value* fracDigitChar8 = builder.CreateTrunc(fracDigitChar, charType);
    
    llvm::Value* fracPosValue = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value* fracDstIdx = builder.CreateAdd(fracPosValue, fracIdx);
    llvm::Value* fracDstPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), fracDstIdx});
    builder.CreateStore(fracDigitChar8, fracDstPtr);
    
    llvm::Value* nextFracRemaining = builder.CreateSDiv(fracRemaining, llvm::ConstantInt::get(context, llvm::APInt(64, 10, true)));
    llvm::Value* nextFracIdx = builder.CreateSub(fracIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    
    fracRemaining->addIncoming(nextFracRemaining, fracDigitBody);
    fracIdx->addIncoming(nextFracIdx, fracDigitBody);
    builder.CreateBr(fracDigitLoop);
    
    // Fractional digits done
    builder.SetInsertPoint(fracDigitsDone);
    llvm::Value* finalPosValue = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value* nonZeroLength = builder.CreateAdd(finalPosValue, precisionArg);
    builder.CreateBr(floatFormatDone);
    
    // Format done - write to stdout
    builder.SetInsertPoint(floatFormatDone);
    llvm::PHINode* finalFloatLength = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_float_length");
    finalFloatLength->addIncoming(zeroLength, zeroFillDone);
    finalFloatLength->addIncoming(nonZeroLength, fracDigitsDone);
    
    // Get stdout handle (-11)
    llvm::Value* floatStdoutHandle = builder.CreateCall(getStdHandle,
        {llvm::ConstantInt::get(context, llvm::APInt(32, -11, true))});
    
    // Get buffer pointer
    llvm::Value* floatBufferPtr = builder.CreateBitCast(floatBuffer, llvm::PointerType::get(context, 0));
    
    // Call WriteFile
    builder.CreateCall(writeFile, {
        floatStdoutHandle,
        floatBufferPtr,
        finalFloatLength,
        floatBytesWritten,
        llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))
    });
    
    // Write newline
    builder.CreateCall(writeFile, {
        floatStdoutHandle,
        newlinePtr,
        llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)),
        floatBytesWritten,
        llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))
    });
    
    // Return 0
    builder.CreateRet(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    
    // Add attribute to prevent LLVM from converting memset calls to intrinsics
    printFloatFunc->addFnAttr("no-builtin-memset");
    
    // Restore insert point
    if (savedBlock) {
        builder.SetInsertPoint(savedBlock);
    }
}

llvm::AllocaInst* CodeGenerator::createEntryBlockAlloca(llvm::Function* function,
                                                         const std::string& varName,
                                                         llvm::Type* type) {
    // If no type specified, default to i32
    if (!type) {
        type = llvm::Type::getInt32Ty(context);
    }
    
    llvm::IRBuilder<> tmpBuilder(&function->getEntryBlock(),
                                  function->getEntryBlock().begin());
    return tmpBuilder.CreateAlloca(type, nullptr, varName);
}

void CodeGenerator::pushScope() {
    scopeStack.push_back(ScopeInfo());
}

void CodeGenerator::popScope(llvm::Function* function) {
    if (scopeStack.empty()) {
        return;
    }
    
    generateScopeCleanup(function);
    scopeStack.pop_back();
}

void CodeGenerator::generateScopeCleanup(llvm::Function* function) {
    if (scopeStack.empty()) {
        return;
    }
    
    ScopeInfo& currentScope = scopeStack.back();
    
    // Generate free calls for all heap-allocated arrays in this scope
    for (auto it = currentScope.heapAllocatedArrays.rbegin(); 
         it != currentScope.heapAllocatedArrays.rend(); ++it) {
        const std::string& arrayName = it->first;
        llvm::AllocaInst* ptrAlloca = it->second;
        
        // Load the pointer value
        llvm::Value* arrayPtr = builder.CreateLoad(
            llvm::PointerType::get(context, 0),
            ptrAlloca,
            arrayName + ".ptr"
        );
        
        // Declare free if not already declared
        llvm::Function* freeFunc = module->getFunction("free");
        if (!freeFunc) {
            llvm::FunctionType* freeFuncType = llvm::FunctionType::get(
                llvm::Type::getVoidTy(context),
                {llvm::PointerType::get(context, 0)},
                false
            );
            freeFunc = llvm::Function::Create(
                freeFuncType,
                llvm::Function::ExternalLinkage,
                "free",
                module.get()
            );
        }
        
        // Call free
        builder.CreateCall(freeFunc, {arrayPtr});
    }
}

llvm::Function* CodeGenerator::getRuntimeFunction(const std::string& name, llvm::Module* module, llvm::LLVMContext& context) {
    llvm::Function* func = module->getFunction(name);
    if (!func) {
        llvm::FunctionType* funcType = llvm::FunctionType::get(
            llvm::Type::getDoubleTy(context),
            {llvm::Type::getDoubleTy(context)},
            false
        );
        func = llvm::Function::Create(
            funcType,
            llvm::Function::ExternalLinkage,
            name,
            module
        );
    }
    return func;
}

llvm::Value* CodeGenerator::generateMathIntrinsic(CallExprAST* callExpr) {
    // NOTE: Math intrinsic metadata is defined in lexer.cpp's keywords map.
    // This function uses that metadata to generate the appropriate LLVM IR.
    
    // Get math intrinsic info from keywords map
    const MathIntrinsicInfo* info = Lexer::getMathIntrinsicInfo(callExpr->callee);
    if (!info) {
        throw std::runtime_error("Internal error: " + callExpr->callee + " is not a recognized math intrinsic");
    }
    
    // Generate the argument(s)
    std::vector<llvm::Value*> args;
    for (auto& arg : callExpr->args) {
        llvm::Value* argVal = generateExpr(arg.get());
        if (!argVal) {
            throw std::runtime_error("Failed to generate argument for " + callExpr->callee);
        }
        
        // Promote int to float if needed
        if (argVal->getType()->isIntegerTy(32)) {
            argVal = builder.CreateSIToFP(argVal, llvm::Type::getDoubleTy(context), "inttofp");
        }
        
        args.push_back(argVal);
    }
    
    llvm::Value* result = nullptr;
    
    // Generate code based on intrinsic kind
    switch (info->kind) {
        case MathIntrinsicKind::LLVMIntrinsic: {
            // Use the intrinsic ID directly from metadata
            llvm::Function* intrinsicFn = llvm::Intrinsic::getOrInsertDeclaration(
                module.get(),
                info->intrinsicID,
                {llvm::Type::getDoubleTy(context)}
            );
            
            if (!intrinsicFn) {
                throw std::runtime_error("Failed to get intrinsic declaration for " + callExpr->callee);
            }
            
            // Call the intrinsic
            result = builder.CreateCall(intrinsicFn, args, callExpr->callee + ".result");
            break;
        }
        
        case MathIntrinsicKind::RuntimeFunction: {
            // Call runtime library function by name
            llvm::Function* runtimeFn = getRuntimeFunction(info->runtimeFunctionName, module.get(), context);
            if (args.size() != 1) {
                throw std::runtime_error(callExpr->callee + " expects exactly 1 argument");
            }
            result = builder.CreateCall(runtimeFn, args, callExpr->callee + "_result");
            break;
        }
        
        case MathIntrinsicKind::DirectCast: {
            // Direct IR operation (e.g., trunc: float to int)
            if (args.size() != 1) {
                throw std::runtime_error(callExpr->callee + " expects exactly 1 argument");
            }
            result = builder.CreateFPToSI(args[0], llvm::Type::getInt32Ty(context), "trunc");
            break;
        }
    }
    
    // Convert to int if return type is int
    if (info->returnType == "int" && result->getType()->isDoubleTy()) {
        result = builder.CreateFPToSI(result, llvm::Type::getInt32Ty(context), "toint");
    }
    
    return result;
}

llvm::Value* CodeGenerator::generateExpr(ExprAST* expr) {
    if (auto* numExpr = dynamic_cast<NumberExprAST*>(expr)) {
        return llvm::ConstantInt::get(context, llvm::APInt(32, numExpr->value, true));
    }
    
    if (auto* floatExpr = dynamic_cast<FloatExprAST*>(expr)) {
        return llvm::ConstantFP::get(context, llvm::APFloat(floatExpr->value));
    }
    
    if (auto* boolExpr = dynamic_cast<BooleanExprAST*>(expr)) {
        return llvm::ConstantInt::get(context, llvm::APInt(1, boolExpr->value ? 1 : 0, false));
    }
    
    if (auto* charExpr = dynamic_cast<CharacterExprAST*>(expr)) {
        // Characters are represented as 8-bit integers
        return llvm::ConstantInt::get(context, llvm::APInt(8, (uint8_t)charExpr->value, false));
    }
    
    if (auto* strExpr = dynamic_cast<StringLiteralExprAST*>(expr)) {
        // Create a global constant for the string
        llvm::Constant* strConstant = llvm::ConstantDataArray::getString(context, strExpr->value, false);
        llvm::GlobalVariable* strGlobal = new llvm::GlobalVariable(
            *module,
            strConstant->getType(),
            true,  // isConstant
            llvm::GlobalValue::PrivateLinkage,
            strConstant,
            ".str"
        );
        
        // Return pointer to the string (cast to opaque pointer)
        return builder.CreateBitCast(strGlobal, llvm::PointerType::get(context, 0));
    }
    
    if (dynamic_cast<ArrayLiteralExprAST*>(expr)) {
        // Array literals cannot be generated as standalone expressions
        // They should only appear as initializers in var/let declarations
        throw std::runtime_error("Array literal can only be used as an initializer in variable declarations");
    }
    
    if (auto* castExpr = dynamic_cast<CastExprAST*>(expr)) {
        llvm::Value* value = generateExpr(castExpr->expr.get());
        if (!value) {
            throw std::runtime_error("Failed to generate expression for cast");
        }
        
        // Validate that target type is a valid keyword type
        if (!Lexer::isTypeString(castExpr->targetType)) {
            throw std::runtime_error("Invalid cast target type: " + castExpr->targetType);
        }
        
        llvm::Type* targetType = nullptr;
        if (castExpr->targetType == "int") {
            targetType = llvm::Type::getInt32Ty(context);
        } else if (castExpr->targetType == "float") {
            targetType = llvm::Type::getDoubleTy(context);
        } else if (castExpr->targetType == "ptr") {
            targetType = llvm::PointerType::get(context, 0);  // Opaque pointer
        } else if (castExpr->targetType == "char") {
            targetType = llvm::Type::getInt8Ty(context);
        } else if (castExpr->targetType == "bool") {
            targetType = llvm::Type::getInt1Ty(context);
        } else {
            throw std::runtime_error("Unknown cast target type: " + castExpr->targetType);
        }
        
        llvm::Type* sourceType = value->getType();
        
        // Handle different cast scenarios
        if (sourceType == targetType) {
            // No-op cast
            return value;
        } else if (sourceType->isIntegerTy() && targetType->isIntegerTy()) {
            // Integer to integer (e.g., i8 -> i32, i32 -> i8)
            unsigned sourceBits = sourceType->getIntegerBitWidth();
            unsigned targetBits = targetType->getIntegerBitWidth();
            if (sourceBits < targetBits) {
                // Zero-extend (unsigned extension)
                return builder.CreateZExt(value, targetType, "zexttmp");
            } else if (sourceBits > targetBits) {
                // Truncate
                return builder.CreateTrunc(value, targetType, "trunctmp");
            }
            return value;
        } else if (sourceType->isIntegerTy() && targetType->isFloatingPointTy()) {
            // Integer to float (signed)
            return builder.CreateSIToFP(value, targetType, "int2floattmp");
        } else if (sourceType->isFloatingPointTy() && targetType->isIntegerTy()) {
            // Float to integer - truncate (as per spec)
            return builder.CreateFPToSI(value, targetType, "float2inttmp");
        } else if (sourceType->isIntegerTy() && targetType->isPointerTy()) {
            // Integer to pointer
            return builder.CreateIntToPtr(value, targetType, "int2ptrtmp");
        } else if (sourceType->isPointerTy() && targetType->isIntegerTy()) {
            // Pointer to integer
            return builder.CreatePtrToInt(value, targetType, "ptr2inttmp");
        } else if (sourceType->isPointerTy() && targetType->isPointerTy()) {
            // Pointer to pointer (bitcast)
            return builder.CreateBitCast(value, targetType, "ptrcasttmp");
        } else {
            throw std::runtime_error("Unsupported cast from " + 
                                   std::string(sourceType->isIntegerTy() ? "integer" : 
                                              sourceType->isPointerTy() ? "pointer" : "unknown") +
                                   " to " +
                                   std::string(targetType->isIntegerTy() ? "integer" :
                                              targetType->isPointerTy() ? "pointer" : "unknown"));
        }
    }
    
    if (auto* addrExpr = dynamic_cast<AddressOfExprAST*>(expr)) {
        // Get the alloca for the variable
        llvm::AllocaInst* alloca = namedValues[addrExpr->varName];
        if (!alloca) {
            throw std::runtime_error("Unknown variable name: " + addrExpr->varName);
        }
        // The alloca itself is already a pointer, so just return it
        return alloca;
    }
    
    if (auto* derefExpr = dynamic_cast<DerefExprAST*>(expr)) {
        // Generate the pointer expression
        llvm::Value* ptr = generateExpr(derefExpr->expr.get());
        if (!ptr) {
            throw std::runtime_error("Failed to generate pointer expression for dereference");
        }
        
        // Load the value from the pointer
        // For now, assume we're loading an i32
        return builder.CreateLoad(llvm::Type::getInt32Ty(context), ptr, "dereftmp");
    }
    
    if (auto* varExpr = dynamic_cast<VariableExprAST*>(expr)) {
        llvm::AllocaInst* alloca = namedValues[varExpr->name];
        if (!alloca) {
            throw std::runtime_error("Unknown variable name: " + varExpr->name);
        }
        // Get the type from the alloca's allocated type
        llvm::Type* varType = alloca->getAllocatedType();
        return builder.CreateLoad(varType, alloca, varExpr->name);
    }
    
    if (dynamic_cast<StructInitExprAST*>(expr)) {
        // Struct literals should only appear as initializers in variable declarations
        throw std::runtime_error("Struct literal can only be used as an initializer in variable declarations");
    }
    
    if (auto* memberAccessExpr = dynamic_cast<MemberAccessExprAST*>(expr)) {
        std::string varType;
        llvm::Value* objectPtr = nullptr;
        
        // Handle both simple variable access and complex expression access
        if (memberAccessExpr->object) {
            // Complex expression (e.g., arr[0].field)
            // First evaluate the expression to get the object
            llvm::Value* objectValue = generateExpr(memberAccessExpr->object.get());
            
            // For array indexing, objectValue is already a loaded value
            // We need to determine the type from the expression
            // For now, we need to track the type through the expression evaluation
            // This is a limitation - we need type information from semantic analysis
            
            // Try to determine if it's a struct type from array subscript
            if (auto* arrayIndexExpr = dynamic_cast<ArrayIndexExprAST*>(memberAccessExpr->object.get())) {
                // Get the array variable type
                std::string arrayType = variableTypes[arrayIndexExpr->arrayName];
                // Extract element type (e.g., "[5]Point" -> "Point")
                if (arrayType.size() > 2 && arrayType[0] == '[') {
                    size_t closeBracket = arrayType.find(']');
                    if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
                        varType = arrayType.substr(closeBracket + 1);
                    }
                }
                
                // For array element access, objectValue is a pointer to the element
                objectPtr = objectValue;
            }
        } else {
            // Simple variable access (e.g., obj.field)
            varType = variableTypes[memberAccessExpr->objectName];
            llvm::AllocaInst* structAlloca = namedValues[memberAccessExpr->objectName];
            if (!structAlloca) {
                throw std::runtime_error("Unknown variable: " + memberAccessExpr->objectName);
            }
            
            llvm::Type* allocatedType = structAlloca->getAllocatedType();
            
            if (allocatedType->isPointerTy()) {
                // This is a parameter - load the pointer first
                objectPtr = builder.CreateLoad(allocatedType, structAlloca, "struct.ptr");
            } else {
                // This is a local variable - use directly
                objectPtr = structAlloca;
            }
        }
        
        // Check if this is struct member access
        if (structTypes.find(varType) != structTypes.end()) {
            // This is a struct member access
            llvm::StructType* structType = structTypes[varType];
            
            // Find field index
            const auto& fields = structFields[varType];
            int fieldIndex = -1;
            for (size_t i = 0; i < fields.size(); i++) {
                if (fields[i].first == memberAccessExpr->memberName) {
                    fieldIndex = static_cast<int>(i);
                    break;
                }
            }
            
            if (fieldIndex < 0) {
                throw std::runtime_error("Unknown field '" + memberAccessExpr->memberName + "' in struct '" + varType + "'");
            }
            
            // Get pointer to field using GEP
            llvm::Value* fieldPtr = builder.CreateStructGEP(structType, objectPtr, fieldIndex, memberAccessExpr->memberName);
            
            // Load and return the field value
            llvm::Type* fieldType = structType->getElementType(fieldIndex);
            return builder.CreateLoad(fieldType, fieldPtr, memberAccessExpr->memberName + ".val");
        }
        
        // Currently only support array.length for simple variables
        if (memberAccessExpr->memberName == "length" && !memberAccessExpr->object) {
            // Check for the hidden __length variable
            std::string lengthVar = memberAccessExpr->objectName + ".__length";
            llvm::AllocaInst* lengthAlloca = namedValues[lengthVar];
            
            if (lengthAlloca) {
                // Load and return the stored length
                return builder.CreateLoad(llvm::Type::getInt32Ty(context), lengthAlloca, "length");
            }
            
            // Fall back to checking if it's a stack array
            llvm::AllocaInst* alloca = namedValues[memberAccessExpr->objectName];
            if (!alloca) {
                throw std::runtime_error("Unknown variable name: " + memberAccessExpr->objectName);
            }
            
            llvm::Type* allocatedType = alloca->getAllocatedType();
            
            // Check if this is a stack-allocated array (old style, shouldn't happen anymore)
            if (allocatedType->isArrayTy()) {
                llvm::ArrayType* arrayType = llvm::cast<llvm::ArrayType>(allocatedType);
                uint64_t arraySize = arrayType->getNumElements();
                return llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), arraySize);
            } else {
                throw std::runtime_error("Variable is not an array: " + memberAccessExpr->objectName);
            }
        } else {
            throw std::runtime_error("Unknown member: " + memberAccessExpr->memberName);
        }
    }
    
    if (auto* arrayIndexExpr = dynamic_cast<ArrayIndexExprAST*>(expr)) {
        llvm::AllocaInst* alloca = namedValues[arrayIndexExpr->arrayName];
        if (!alloca) {
            throw std::runtime_error("Unknown array name: " + arrayIndexExpr->arrayName);
        }
        
        llvm::Value* indexVal = generateExpr(arrayIndexExpr->index.get());
        if (!indexVal) {
            throw std::runtime_error("Failed to generate array index");
        }
        
        llvm::Type* allocatedType = alloca->getAllocatedType();
        
        // Determine element type and whether it's a struct
        std::string elementTypeStr = "int"; // default
        std::string varType = variableTypes[arrayIndexExpr->arrayName];
        if (varType.size() > 2 && varType[0] == '[') {
            size_t closeBracket = varType.find(']');
            if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
                elementTypeStr = varType.substr(closeBracket + 1);
            }
        }
        llvm::Type* elementType = getTypeFromString(context, elementTypeStr, &structTypes);
        bool isStructElement = structTypes.find(elementTypeStr) != structTypes.end();
        
        // Check if this is an array or a pointer (array parameter)
        if (allocatedType->isPointerTy()) {
            // This is a pointer to an array (parameter case)
            // First load the pointer, then use GEP with the index
            llvm::Value* arrayPtr = builder.CreateLoad(allocatedType, alloca, "arrayptr");
            llvm::Value* elementPtr = builder.CreateInBoundsGEP(
                elementType,
                arrayPtr,
                indexVal,
                "arrayidx"
            );
            
            // For struct elements, return pointer; for primitives, load the value
            if (isStructElement) {
                return elementPtr;
            } else {
                return builder.CreateLoad(elementType, elementPtr, "arrayelem");
            }
        } else if (allocatedType->isArrayTy()) {
            // This is a local array variable
            // Get element pointer: GEP array, 0, index
            llvm::Value* zero = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
            llvm::Value* elementPtr = builder.CreateInBoundsGEP(
                allocatedType,
                alloca,
                {zero, indexVal},
                "arrayidx"
            );
            
            // For struct elements, return pointer; for primitives, load the value
            if (isStructElement) {
                return elementPtr;
            } else {
                llvm::ArrayType* arrayType = llvm::cast<llvm::ArrayType>(allocatedType);
                llvm::Type* elementType = arrayType->getElementType();
                return builder.CreateLoad(elementType, elementPtr, "arrayelem");
            }
        } else {
            throw std::runtime_error("Variable is not an array: " + arrayIndexExpr->arrayName);
        }
    }
    
    if (auto* binExpr = dynamic_cast<BinaryExprAST*>(expr)) {
        llvm::Value* left = generateExpr(binExpr->left.get());
        llvm::Value* right = generateExpr(binExpr->right.get());
        
        if (!left || !right) {
            throw std::runtime_error("Failed to generate binary expression operands");
        }
        
        // Determine if we need to promote to float
        bool leftIsFloat = left->getType()->isFloatingPointTy();
        bool rightIsFloat = right->getType()->isFloatingPointTy();
        bool needsFloatOp = leftIsFloat || rightIsFloat;
        
        // Promote int to float if mixed types
        if (needsFloatOp) {
            if (!leftIsFloat) {
                left = builder.CreateSIToFP(left, llvm::Type::getDoubleTy(context), "promotetmp");
            }
            if (!rightIsFloat) {
                right = builder.CreateSIToFP(right, llvm::Type::getDoubleTy(context), "promotetmp");
            }
        }
        
        switch (binExpr->op) {
            case '+':
                return needsFloatOp ? builder.CreateFAdd(left, right, "faddtmp") 
                                    : builder.CreateAdd(left, right, "addtmp");
            case '-':
                return needsFloatOp ? builder.CreateFSub(left, right, "fsubtmp") 
                                    : builder.CreateSub(left, right, "subtmp");
            case '*':
                return needsFloatOp ? builder.CreateFMul(left, right, "fmultmp") 
                                    : builder.CreateMul(left, right, "multmp");
            case '/':
                return needsFloatOp ? builder.CreateFDiv(left, right, "fdivtmp") 
                                    : builder.CreateSDiv(left, right, "divtmp");
            case '%':
                // Modulo only works with integers
                return builder.CreateSRem(left, right, "modtmp");
            case '>':
                return needsFloatOp ? builder.CreateFCmpOGT(left, right, "fcmptmp") 
                                    : builder.CreateICmpSGT(left, right, "cmptmp");
            case '<':
                return needsFloatOp ? builder.CreateFCmpOLT(left, right, "fcmptmp") 
                                    : builder.CreateICmpSLT(left, right, "cmptmp");
            case 'G': // >=
                return needsFloatOp ? builder.CreateFCmpOGE(left, right, "fcmptmp") 
                                    : builder.CreateICmpSGE(left, right, "cmptmp");
            case 'L': // <=
                return needsFloatOp ? builder.CreateFCmpOLE(left, right, "fcmptmp") 
                                    : builder.CreateICmpSLE(left, right, "cmptmp");
            case 'E': // == (equality)
                return needsFloatOp ? builder.CreateFCmpOEQ(left, right, "fcmptmp") 
                                    : builder.CreateICmpEQ(left, right, "cmptmp");
            case 'N': // != (not equal)
                return needsFloatOp ? builder.CreateFCmpONE(left, right, "fcmptmp") 
                                    : builder.CreateICmpNE(left, right, "cmptmp");
            default:
                throw std::runtime_error("Unknown binary operator");
        }
    }
    
    if (auto* unaryExpr = dynamic_cast<UnaryExprAST*>(expr)) {
        llvm::Value* operand = generateExpr(unaryExpr->operand.get());
        
        if (!operand) {
            throw std::runtime_error("Failed to generate unary expression operand");
        }
        
        bool isFloat = operand->getType()->isFloatingPointTy();
        
        switch (unaryExpr->op) {
            case '-':
                // Negate the operand
                if (isFloat) {
                    return builder.CreateFNeg(operand, "fnegtmp");
                } else {
                    // For integers: 0 - operand
                    llvm::Value* zero = llvm::ConstantInt::get(operand->getType(), 0);
                    return builder.CreateSub(zero, operand, "negtmp");
                }
            case '+':
                // Unary plus: just return the operand unchanged
                return operand;
            default:
                throw std::runtime_error("Unknown unary operator");
        }
    }
    
    if (auto* callExpr = dynamic_cast<CallExprAST*>(expr)) {
        // Handle math intrinsic functions (built into LLVM)
        if (Lexer::isMathIntrinsic(callExpr->callee)) {
            return generateMathIntrinsic(callExpr);
        }
        
        // Initialize standard library if calling a standard library function
        if (callExpr->callee == "print" || callExpr->callee == "print_float") {
            initStandardLibrary();
        }
        
        // Look up the function in the module
        // Try exact match first
        llvm::Function* calleeF = module->getFunction(callExpr->callee);
        
        // If not found and unqualified, try suffix matching
        if (!calleeF && callExpr->callee.find(".") == std::string::npos) {
            std::string searchSuffix = "." + callExpr->callee;
            
            for (auto& func : module->functions()) {
                std::string funcName = func.getName().str();
                if (funcName.size() > searchSuffix.size() &&
                    funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
                    calleeF = &func;
                    break; // Take first match (semantic analyzer already validated uniqueness)
                }
            }
        }
        
        if (!calleeF) {
            throw std::runtime_error("Unknown function referenced: " + callExpr->callee);
        }
        
        // Generate code for arguments (including hidden length parameters for arrays)
        std::vector<llvm::Value*> argsV;
        size_t argIdx = 0;
        for (size_t i = 0; i < callExpr->args.size(); i++) {
            auto& arg = callExpr->args[i];
            // Check if this parameter expects a pointer (array parameter)
            llvm::Type* paramType = calleeF->getFunctionType()->getParamType(argIdx);
            
            // If the parameter is a pointer and the argument is a variable,
            // check if it's an array (pass address) or pointer variable (load value)
            if (paramType->isPointerTy()) {
                if (auto* varExpr = dynamic_cast<VariableExprAST*>(arg.get())) {
                    llvm::AllocaInst* alloca = namedValues[varExpr->name];
                    if (!alloca) {
                        throw std::runtime_error("Unknown variable name: " + varExpr->name);
                    }
                    
                    // Check the alloca's allocated type
                    llvm::Type* allocatedType = alloca->getAllocatedType();
                    
                    // If it's an array type, pass pointer to first element
                    if (allocatedType->isArrayTy()) {
                        llvm::Value* zero = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
                        llvm::Value* arrayPtr = builder.CreateInBoundsGEP(
                            allocatedType,
                            alloca,
                            {zero, zero},
                            varExpr->name + ".ptr"
                        );
                        argsV.push_back(arrayPtr);
                        argIdx++;
                        
                        // Check if the next parameter is an int32 (hidden length parameter)
                        if (argIdx < calleeF->getFunctionType()->getNumParams()) {
                            llvm::Type* nextParamType = calleeF->getFunctionType()->getParamType(argIdx);
                            if (nextParamType->isIntegerTy(32)) {
                                // This is a hidden length parameter, pass the array length
                                std::string lengthVarName = varExpr->name + ".__length";
                                if (namedValues.find(lengthVarName) != namedValues.end()) {
                                    llvm::AllocaInst* lengthAlloca = namedValues[lengthVarName];
                                    llvm::Value* lengthVal = builder.CreateLoad(llvm::Type::getInt32Ty(context), lengthAlloca, "length");
                                    argsV.push_back(lengthVal);
                                    argIdx++;
                                }
                            }
                        }
                        continue;
                    }
                    // Check if this is a pointer variable that has a .__length (array parameter)
                    if (allocatedType->isPointerTy()) {
                        std::string lengthVarName = varExpr->name + ".__length";
                        if (namedValues.find(lengthVarName) != namedValues.end()) {
                            // This is an array parameter being passed to another function
                            llvm::Value* ptrVal = builder.CreateLoad(allocatedType, alloca, varExpr->name);
                            argsV.push_back(ptrVal);
                            argIdx++;
                            
                            // Check if the next parameter is an int32 (hidden length parameter)
                            if (argIdx < calleeF->getFunctionType()->getNumParams()) {
                                llvm::Type* nextParamType = calleeF->getFunctionType()->getParamType(argIdx);
                                if (nextParamType->isIntegerTy(32)) {
                                    // Pass the length
                                    llvm::AllocaInst* lengthAlloca = namedValues[lengthVarName];
                                    llvm::Value* lengthVal = builder.CreateLoad(llvm::Type::getInt32Ty(context), lengthAlloca, "length");
                                    argsV.push_back(lengthVal);
                                    argIdx++;
                                }
                            }
                            continue;
                        }
                    }
                    // Otherwise fall through to normal handling (will load the value)
                }
            }
            
            // Normal argument - generate and potentially load the value
            llvm::Value* argVal = generateExpr(arg.get());
            if (!argVal) {
                throw std::runtime_error("Failed to generate function argument");
            }
            
            // Check if this is a struct parameter that should be passed by reference
            // If the parameter type is a pointer and the argument is a struct variable,
            // we need to pass the alloca (pointer) instead of the loaded value
            if (paramType->isPointerTy()) {
                if (auto* varExpr = dynamic_cast<VariableExprAST*>(arg.get())) {
                    llvm::AllocaInst* alloca = namedValues[varExpr->name];
                    if (alloca) {
                        llvm::Type* allocatedType = alloca->getAllocatedType();
                        // If it's a struct type, pass the pointer instead of the loaded value
                        if (allocatedType->isStructTy()) {
                            argsV.push_back(alloca);
                            argIdx++;
                            continue;
                        }
                    }
                }
            }
            
            // Promote int to float if needed
            if (paramType->isDoubleTy() && argVal->getType()->isIntegerTy(32)) {
                argVal = builder.CreateSIToFP(argVal, llvm::Type::getDoubleTy(context), "inttofp");
            }
            
            argsV.push_back(argVal);
            argIdx++;
        }
        
        return builder.CreateCall(calleeF, argsV);
    }
    
    throw std::runtime_error("Unknown expression type");
}

void CodeGenerator::generateStmt(StmtAST* stmt, llvm::Function* function) {
    if (auto* varDecl = dynamic_cast<VarDeclStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(varDecl->line, varDecl->column);
        }
        
        // Check if initializer is an array literal
        if (auto* arrayLiteral = dynamic_cast<ArrayLiteralExprAST*>(varDecl->initializer.get())) {
            // Handle array declaration
            int arraySize;
            llvm::Type* elementType;
            std::string elementTypeName;
            std::vector<llvm::Value*> initValues;
            
            if (arrayLiteral->size > 0) {
                // [size]type form - zero-initialized
                arraySize = arrayLiteral->size;
                elementTypeName = arrayLiteral->elementType;
                elementType = getTypeFromString(context, elementTypeName, &structTypes);
            } else {
                // [val1, val2, ...] form - value-initialized
                arraySize = arrayLiteral->values.size();
                
                // Generate all values and infer type from first element
                for (auto& valExpr : arrayLiteral->values) {
                    llvm::Value* val = generateExpr(valExpr.get());
                    if (!val) {
                        throw std::runtime_error("Failed to generate array element value");
                    }
                    initValues.push_back(val);
                }
                
                // Infer element type from first value
                elementType = initValues[0]->getType();
                if (elementType->isIntegerTy(32)) {
                    elementTypeName = "int";
                } else if (elementType->isDoubleTy()) {
                    elementTypeName = "float";
                } else if (elementType->isIntegerTy(8)) {
                    elementTypeName = "char";
                } else if (elementType->isPointerTy()) {
                    elementTypeName = "ptr";
                } else {
                    throw std::runtime_error("Unsupported array element type");
                }
            }
            
            // Initialize heap management functions
            initHeapManagement();
            
            // Declare malloc if not already declared
            llvm::Function* mallocFunc = module->getFunction("malloc");
            if (!mallocFunc) {
                llvm::FunctionType* mallocFuncType = llvm::FunctionType::get(
                    llvm::PointerType::get(context, 0),
                    {llvm::Type::getInt64Ty(context)},
                    false
                );
                mallocFunc = llvm::Function::Create(
                    mallocFuncType,
                    llvm::Function::ExternalLinkage,
                    "malloc",
                    module.get()
                );
            }
            
            // Calculate size: arraySize * sizeof(elementType)
            const llvm::DataLayout& dataLayout = module->getDataLayout();
            uint64_t elementSize = dataLayout.getTypeAllocSize(elementType);
            uint64_t totalSize = arraySize * elementSize;
            llvm::Value* sizeVal = llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), totalSize);
            
            // Call malloc
            llvm::Value* arrayPtr = builder.CreateCall(mallocFunc, {sizeVal}, varDecl->name + ".malloc");
            
            // Create alloca to store the pointer
            llvm::AllocaInst* ptrAlloca = createEntryBlockAlloca(
                function,
                varDecl->name,
                llvm::PointerType::get(context, 0)
            );
            builder.CreateStore(arrayPtr, ptrAlloca);
            
            // Store the array size as a hidden variable for .length access
            llvm::AllocaInst* sizeAlloca = createEntryBlockAlloca(
                function,
                varDecl->name + ".__length",
                llvm::Type::getInt32Ty(context)
            );
            llvm::Value* arraySizeVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), arraySize);
            builder.CreateStore(arraySizeVal, sizeAlloca);
            namedValues[varDecl->name + ".__length"] = sizeAlloca;
            
            // Initialize array elements
            if (initValues.empty()) {
                // Zero-initialize using memset
                llvm::Function* memsetFunc = getOrDeclareMemset();
                llvm::Value* zeroVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
                llvm::Value* memsetSizeVal = llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), totalSize);
                builder.CreateCall(memsetFunc, {arrayPtr, zeroVal, memsetSizeVal});
            } else {
                // Store each value
                for (int i = 0; i < arraySize; i++) {
                    llvm::Value* indexVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), i);
                    llvm::Value* elementPtr = builder.CreateGEP(elementType, arrayPtr, indexVal, "arrayidx");
                    builder.CreateStore(initValues[i], elementPtr);
                }
            }
            
            namedValues[varDecl->name] = ptrAlloca;
            variableTypes[varDecl->name] = "[" + std::to_string(arraySize) + "]" + elementTypeName;
            
            // Track this array for cleanup
            if (!scopeStack.empty()) {
                scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, ptrAlloca});
            }
            
            // Create debug info
            if (generateDebugInfo && !debugScopeStack.empty()) {
                llvm::DILocalVariable* debugVar = debugBuilder->createAutoVariable(
                    debugScopeStack.back(),
                    varDecl->name,
                    debugFile,
                    varDecl->line,
                    debugBuilder->createBasicType("ptr", 64, llvm::dwarf::DW_ATE_address),
                    false,
                    llvm::DINode::FlagZero,
                    64
                );
                
                debugBuilder->insertDeclare(
                    ptrAlloca,
                    debugVar,
                    debugBuilder->createExpression(),
                    llvm::DILocation::get(context, varDecl->line, varDecl->column, debugScopeStack.back()),
                    builder.GetInsertBlock()
                );
            }
            
            return;
        }
        
        // Check if initializer is a struct initialization
        if (auto* structInitExpr = dynamic_cast<StructInitExprAST*>(varDecl->initializer.get())) {
            // Get struct type
            llvm::StructType* structType = structTypes[structInitExpr->structName];
            if (!structType) {
                throw std::runtime_error("Unknown struct type: " + structInitExpr->structName);
            }
            
            // Create alloca for the struct
            llvm::AllocaInst* structAlloca = createEntryBlockAlloca(function, varDecl->name, structType);
            namedValues[varDecl->name] = structAlloca;
            variableTypes[varDecl->name] = structInitExpr->structName;
            
            // Initialize each field
            const auto& fields = structFields[structInitExpr->structName];
            for (const auto& initField : structInitExpr->fields) {
                // Find field index
                int fieldIndex = -1;
                for (size_t j = 0; j < fields.size(); j++) {
                    if (fields[j].first == initField.name) {
                        fieldIndex = static_cast<int>(j);
                        break;
                    }
                }
                
                if (fieldIndex < 0) {
                    throw std::runtime_error("Unknown field '" + initField.name + "' in struct '" + structInitExpr->structName + "'");
                }
                
                // Generate value for this field
                llvm::Value* fieldValue = generateExpr(initField.value.get());
                
                // Get pointer to field using GEP
                llvm::Value* fieldPtr = builder.CreateStructGEP(structType, structAlloca, fieldIndex, initField.name);
                
                // Store the value
                builder.CreateStore(fieldValue, fieldPtr);
            }
            
            return;
        }
        
        // Non-array, non-struct variable
        llvm::Value* initVal = nullptr;
        if (varDecl->initializer) {
            initVal = generateExpr(varDecl->initializer.get());
            if (!initVal) {
                throw std::runtime_error("Failed to generate variable initializer");
            }
        }
        
        // Determine the type for the alloca
        llvm::Type* allocaType;
        if (!varDecl->type.empty()) {
            // Use explicit type annotation
            allocaType = getTypeFromString(context, varDecl->type, &structTypes);
        } else if (initVal) {
            // Infer from initializer type
            allocaType = initVal->getType();
        } else {
            // No type annotation and no initializer - default to i32
            allocaType = llvm::Type::getInt32Ty(context);
        }
        
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, varDecl->name, allocaType);
        
        // Store initializer value, or zero-initialize if no initializer provided
        if (initVal) {
            builder.CreateStore(initVal, alloca);
        } else {
            // Zero-initialize
            llvm::Value* zeroVal;
            if (allocaType->isIntegerTy()) {
                zeroVal = llvm::ConstantInt::get(allocaType, 0);
            } else if (allocaType->isFloatingPointTy()) {
                zeroVal = llvm::ConstantFP::get(allocaType, 0.0);
            } else {
                zeroVal = llvm::Constant::getNullValue(allocaType);
            }
            builder.CreateStore(zeroVal, alloca);
        }
        
        namedValues[varDecl->name] = alloca;
        variableTypes[varDecl->name] = varDecl->type;
        
        // Create debug info for variable
        if (generateDebugInfo && !debugScopeStack.empty()) {
            llvm::DILocalVariable* debugVar = debugBuilder->createAutoVariable(
                debugScopeStack.back(),      // Scope
                varDecl->name,               // Name
                debugFile,                   // File
                varDecl->line,               // Line
                debugBuilder->createBasicType("int", 32, llvm::dwarf::DW_ATE_signed), // Type
                false,                       // Always preserve
                llvm::DINode::FlagZero,      // Flags
                32                           // Align in bits
            );
            
            debugBuilder->insertDeclare(
                alloca,
                debugVar,
                debugBuilder->createExpression(),
                llvm::DILocation::get(context, varDecl->line, varDecl->column, debugScopeStack.back()),
                builder.GetInsertBlock()
            );
        }
        
        return;
    }
    
    if (auto* letDecl = dynamic_cast<LetDeclStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(letDecl->line, letDecl->column);
        }
        
        // Check if initializer is an array literal
        if (auto* arrayLiteral = dynamic_cast<ArrayLiteralExprAST*>(letDecl->initializer.get())) {
            // Handle array declaration
            int arraySize;
            llvm::Type* elementType;
            std::string elementTypeName;
            std::vector<llvm::Value*> initValues;
            
            if (arrayLiteral->size > 0) {
                // [size]type form - zero-initialized
                arraySize = arrayLiteral->size;
                elementTypeName = arrayLiteral->elementType;
                elementType = getTypeFromString(context, elementTypeName, &structTypes);
            } else {
                // [val1, val2, ...] form - value-initialized
                arraySize = arrayLiteral->values.size();
                
                // Generate all values and infer type from first element
                for (auto& valExpr : arrayLiteral->values) {
                    llvm::Value* val = generateExpr(valExpr.get());
                    if (!val) {
                        throw std::runtime_error("Failed to generate array element value");
                    }
                    initValues.push_back(val);
                }
                
                // Infer element type from first value
                elementType = initValues[0]->getType();
                if (elementType->isIntegerTy(32)) {
                    elementTypeName = "int";
                } else if (elementType->isDoubleTy()) {
                    elementTypeName = "float";
                } else if (elementType->isIntegerTy(8)) {
                    elementTypeName = "char";
                } else if (elementType->isPointerTy()) {
                    elementTypeName = "ptr";
                } else {
                    throw std::runtime_error("Unsupported array element type");
                }
            }
            
            // Initialize heap management functions
            initHeapManagement();
            
            // Declare malloc if not already declared
            llvm::Function* mallocFunc = module->getFunction("malloc");
            if (!mallocFunc) {
                llvm::FunctionType* mallocFuncType = llvm::FunctionType::get(
                    llvm::PointerType::get(context, 0),
                    {llvm::Type::getInt64Ty(context)},
                    false
                );
                mallocFunc = llvm::Function::Create(
                    mallocFuncType,
                    llvm::Function::ExternalLinkage,
                    "malloc",
                    module.get()
                );
            }
            
            // Calculate size: arraySize * sizeof(elementType)
            const llvm::DataLayout& dataLayout = module->getDataLayout();
            uint64_t elementSize = dataLayout.getTypeAllocSize(elementType);
            uint64_t totalSize = arraySize * elementSize;
            llvm::Value* sizeVal = llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), totalSize);
            
            // Call malloc
            llvm::Value* arrayPtr = builder.CreateCall(mallocFunc, {sizeVal}, letDecl->name + ".malloc");
            
            // Create alloca to store the pointer
            llvm::AllocaInst* ptrAlloca = createEntryBlockAlloca(
                function,
                letDecl->name,
                llvm::PointerType::get(context, 0)
            );
            builder.CreateStore(arrayPtr, ptrAlloca);
            
            // Store the array size as a hidden variable for .length access
            llvm::AllocaInst* sizeAlloca = createEntryBlockAlloca(
                function,
                letDecl->name + ".__length",
                llvm::Type::getInt32Ty(context)
            );
            llvm::Value* arraySizeVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), arraySize);
            builder.CreateStore(arraySizeVal, sizeAlloca);
            namedValues[letDecl->name + ".__length"] = sizeAlloca;
            
            // Initialize array elements
            if (initValues.empty()) {
                // Zero-initialize using memset
                llvm::Function* memsetFunc = getOrDeclareMemset();
                llvm::Value* zeroVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
                llvm::Value* memsetSizeVal = llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), totalSize);
                builder.CreateCall(memsetFunc, {arrayPtr, zeroVal, memsetSizeVal});
            } else {
                // Store each value
                for (int i = 0; i < arraySize; i++) {
                    llvm::Value* indexVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), i);
                    llvm::Value* elementPtr = builder.CreateGEP(elementType, arrayPtr, indexVal, "arrayidx");
                    builder.CreateStore(initValues[i], elementPtr);
                }
            }
            
            namedValues[letDecl->name] = ptrAlloca;
            variableTypes[letDecl->name] = "[" + std::to_string(arraySize) + "]" + elementTypeName;
            
            // Track this array for cleanup
            if (!scopeStack.empty()) {
                scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, ptrAlloca});
            }
            
            // Create debug info
            if (generateDebugInfo && !debugScopeStack.empty()) {
                llvm::DILocalVariable* debugVar = debugBuilder->createAutoVariable(
                    debugScopeStack.back(),
                    letDecl->name,
                    debugFile,
                    letDecl->line,
                    debugBuilder->createBasicType("ptr", 64, llvm::dwarf::DW_ATE_address),
                    false,
                    llvm::DINode::FlagZero,
                    64
                );
                
                debugBuilder->insertDeclare(
                    ptrAlloca,
                    debugVar,
                    debugBuilder->createExpression(),
                    llvm::DILocation::get(context, letDecl->line, letDecl->column, debugScopeStack.back()),
                    builder.GetInsertBlock()
                );
            }
            
            return;
        }
        
        // Non-array variable
        llvm::Value* initVal = nullptr;
        if (letDecl->initializer) {
            initVal = generateExpr(letDecl->initializer.get());
            if (!initVal) {
                throw std::runtime_error("Failed to generate let initializer");
            }
        }
        
        // Determine the type for the alloca
        llvm::Type* allocaType;
        if (!letDecl->type.empty()) {
            // Use explicit type annotation
            allocaType = getTypeFromString(context, letDecl->type, &structTypes);
        } else if (initVal) {
            // Infer from initializer
            allocaType = initVal->getType();
        } else {
            // No type annotation and no initializer - default to i32
            allocaType = llvm::Type::getInt32Ty(context);
        }
        
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, letDecl->name, allocaType);
        
        // Store initializer value, or zero-initialize if no initializer provided
        if (initVal) {
            builder.CreateStore(initVal, alloca);
        } else {
            // Zero-initialize
            llvm::Value* zeroVal;
            if (allocaType->isIntegerTy()) {
                zeroVal = llvm::ConstantInt::get(allocaType, 0);
            } else if (allocaType->isFloatingPointTy()) {
                zeroVal = llvm::ConstantFP::get(allocaType, 0.0);
            } else {
                zeroVal = llvm::Constant::getNullValue(allocaType);
            }
            builder.CreateStore(zeroVal, alloca);
        }
        namedValues[letDecl->name] = alloca;
        variableTypes[letDecl->name] = letDecl->type;
        
        // Create debug info for let variable
        if (generateDebugInfo && !debugScopeStack.empty()) {
            llvm::DILocalVariable* debugVar = debugBuilder->createAutoVariable(
                debugScopeStack.back(),      // Scope
                letDecl->name,               // Name
                debugFile,                   // File
                letDecl->line,               // Line
                debugBuilder->createBasicType("int", 32, llvm::dwarf::DW_ATE_signed), // Type
                false,                       // Always preserve
                llvm::DINode::FlagZero,      // Flags
                32                           // Align in bits
            );
            
            debugBuilder->insertDeclare(
                alloca,
                debugVar,
                debugBuilder->createExpression(),
                llvm::DILocation::get(context, letDecl->line, letDecl->column, debugScopeStack.back()),
                builder.GetInsertBlock()
            );
        }
        
        return;
    }
    
    if (auto* assign = dynamic_cast<AssignStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(assign->line, assign->column);
        }
        
        llvm::Value* val = generateExpr(assign->value.get());
        if (!val) {
            throw std::runtime_error("Failed to generate assignment value");
        }
        
        llvm::AllocaInst* alloca = namedValues[assign->name];
        if (!alloca) {
            throw std::runtime_error("Unknown variable name: " + assign->name);
        }
        
        builder.CreateStore(val, alloca);
        return;
    }
    
    if (auto* arrayAssign = dynamic_cast<ArrayAssignStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(arrayAssign->line, arrayAssign->column);
        }
        
        llvm::Value* indexVal = generateExpr(arrayAssign->index.get());
        if (!indexVal) {
            throw std::runtime_error("Failed to generate array index");
        }
        
        llvm::AllocaInst* alloca = namedValues[arrayAssign->arrayName];
        if (!alloca) {
            throw std::runtime_error("Unknown array name: " + arrayAssign->arrayName);
        }
        
        llvm::Type* allocatedType = alloca->getAllocatedType();
        llvm::Value* elementPtr = nullptr;
        
        // Check if this is an array or a pointer (array parameter)
        if (allocatedType->isPointerTy()) {
            // This is a pointer to an array (parameter case)
            // Determine the element type from the tracked variable type
            std::string varType = variableTypes[arrayAssign->arrayName];
            std::string elementTypeStr = "int"; // default
            if (varType.size() > 2 && varType[0] == '[') {
                size_t closeBracket = varType.find(']');
                if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
                    elementTypeStr = varType.substr(closeBracket + 1);
                }
            }
            llvm::Type* elementType = getTypeFromString(context, elementTypeStr, &structTypes);
            
            // First load the pointer, then use GEP with the index
            llvm::Value* arrayPtr = builder.CreateLoad(allocatedType, alloca, "arrayptr");
            elementPtr = builder.CreateInBoundsGEP(
                elementType,
                arrayPtr,
                indexVal,
                "arrayidx"
            );
        } else if (allocatedType->isArrayTy()) {
            // This is a local array variable
            // Get element pointer: GEP array, 0, index
            llvm::Value* zero = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
            elementPtr = builder.CreateInBoundsGEP(
                allocatedType,
                alloca,
                {zero, indexVal},
                "arrayidx"
            );
        } else {
            throw std::runtime_error("Variable is not an array: " + arrayAssign->arrayName);
        }
        
        // Check if assigning a struct literal
        if (auto* structInit = dynamic_cast<StructInitExprAST*>(arrayAssign->value.get())) {
            // Get the struct type
            llvm::StructType* structType = structTypes[structInit->structName];
            if (!structType) {
                throw std::runtime_error("Unknown struct type: " + structInit->structName);
            }
            
            // Initialize fields in the array element
            for (size_t i = 0; i < structInit->fields.size(); i++) {
                const auto& field = structInit->fields[i];
                
                // Find field index
                const auto& fieldList = structFields[structInit->structName];
                int fieldIndex = -1;
                for (size_t j = 0; j < fieldList.size(); j++) {
                    if (fieldList[j].first == field.name) {
                        fieldIndex = static_cast<int>(j);
                        break;
                    }
                }
                
                if (fieldIndex < 0) {
                    throw std::runtime_error("Unknown field '" + field.name + "' in struct '" + structInit->structName + "'");
                }
                
                // Get pointer to field
                llvm::Value* fieldPtr = builder.CreateStructGEP(
                    structType,
                    elementPtr,
                    fieldIndex,
                    field.name
                );
                
                // Generate and store field value
                llvm::Value* fieldVal = generateExpr(field.value.get());
                builder.CreateStore(fieldVal, fieldPtr);
            }
        } else {
            // Normal value assignment
            llvm::Value* val = generateExpr(arrayAssign->value.get());
            if (!val) {
                throw std::runtime_error("Failed to generate array assignment value");
            }
            builder.CreateStore(val, elementPtr);
        }
        
        return;
    }
    
    if (auto* arrayMemberAssign = dynamic_cast<ArrayMemberAssignStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(arrayMemberAssign->line, arrayMemberAssign->column);
        }
        
        llvm::Value* indexVal = generateExpr(arrayMemberAssign->index.get());
        if (!indexVal) {
            throw std::runtime_error("Failed to generate array index");
        }
        
        llvm::AllocaInst* alloca = namedValues[arrayMemberAssign->arrayName];
        if (!alloca) {
            throw std::runtime_error("Unknown array name: " + arrayMemberAssign->arrayName);
        }
        
        llvm::Type* allocatedType = alloca->getAllocatedType();
        
        // Determine element type (must be struct)
        std::string varType = variableTypes[arrayMemberAssign->arrayName];
        std::string elementTypeStr;
        if (varType.size() > 2 && varType[0] == '[') {
            size_t closeBracket = varType.find(']');
            if (closeBracket != std::string::npos && closeBracket + 1 < varType.size()) {
                elementTypeStr = varType.substr(closeBracket + 1);
            }
        }
        
        llvm::StructType* structType = structTypes[elementTypeStr];
        if (!structType) {
            throw std::runtime_error("Element type is not a struct: " + elementTypeStr);
        }
        
        // Get pointer to array element
        llvm::Value* elementPtr;
        if (allocatedType->isPointerTy()) {
            // Array parameter case
            llvm::Value* arrayPtr = builder.CreateLoad(allocatedType, alloca, "arrayptr");
            elementPtr = builder.CreateInBoundsGEP(
                structType,
                arrayPtr,
                indexVal,
                "arrayidx"
            );
        } else if (allocatedType->isArrayTy()) {
            // Local array case
            llvm::Value* zero = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
            elementPtr = builder.CreateInBoundsGEP(
                allocatedType,
                alloca,
                {zero, indexVal},
                "arrayidx"
            );
        } else {
            throw std::runtime_error("Variable is not an array: " + arrayMemberAssign->arrayName);
        }
        
        // Find field index
        const auto& fields = structFields[elementTypeStr];
        int fieldIndex = -1;
        for (size_t i = 0; i < fields.size(); i++) {
            if (fields[i].first == arrayMemberAssign->memberName) {
                fieldIndex = static_cast<int>(i);
                break;
            }
        }
        
        if (fieldIndex < 0) {
            throw std::runtime_error("Unknown field '" + arrayMemberAssign->memberName + "' in struct '" + elementTypeStr + "'");
        }
        
        // Get pointer to field
        llvm::Value* fieldPtr = builder.CreateStructGEP(
            structType,
            elementPtr,
            fieldIndex,
            arrayMemberAssign->memberName
        );
        
        // Generate and store value
        llvm::Value* val = generateExpr(arrayMemberAssign->value.get());
        builder.CreateStore(val, fieldPtr);
        
        return;
    }
    
    if (auto* derefAssign = dynamic_cast<DerefAssignStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(derefAssign->line, derefAssign->column);
        }
        
        // Generate the pointer expression
        llvm::Value* ptr = generateExpr(derefAssign->pointer.get());
        if (!ptr) {
            throw std::runtime_error("Failed to generate pointer for dereference assignment");
        }
        
        // Generate the value to store
        llvm::Value* val = generateExpr(derefAssign->value.get());
        if (!val) {
            throw std::runtime_error("Failed to generate value for dereference assignment");
        }
        
        // Store the value through the pointer
        builder.CreateStore(val, ptr);
        return;
    }
    
    if (auto* exprStmt = dynamic_cast<ExprStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(exprStmt->line, exprStmt->column);
        }
        
        // Generate the expression (likely a function call)
        generateExpr(exprStmt->expression.get());
        return;
    }
    
    if (auto* ifStmt = dynamic_cast<IfStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(ifStmt->line, ifStmt->column);
        }
        
        llvm::Value* condVal = generateExpr(ifStmt->condition.get());
        if (!condVal) {
            throw std::runtime_error("Failed to generate if condition");
        }
        
        // Convert condition to bool if it's not already an i1
        if (condVal->getType() != builder.getInt1Ty()) {
            condVal = builder.CreateICmpNE(
                condVal, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), "ifcond");
        }
        
        llvm::BasicBlock* thenBB = llvm::BasicBlock::Create(context, "then", function);
        llvm::BasicBlock* elseBB = nullptr;
        llvm::BasicBlock* mergeBB = nullptr;
        
        bool hasElse = !ifStmt->elseBody.empty();
        
        if (hasElse) {
            elseBB = llvm::BasicBlock::Create(context, "else");
            mergeBB = llvm::BasicBlock::Create(context, "ifcont");
            builder.CreateCondBr(condVal, thenBB, elseBB);
        } else {
            mergeBB = llvm::BasicBlock::Create(context, "ifcont");
            builder.CreateCondBr(condVal, thenBB, mergeBB);
        }
        
        // Generate then block
        builder.SetInsertPoint(thenBB);
        for (auto& s : ifStmt->thenBody) {
            generateStmt(s.get(), function);
        }
        bool thenTerminated = builder.GetInsertBlock()->getTerminator() != nullptr;
        if (!thenTerminated) {
            builder.CreateBr(mergeBB);
        }
        
        // Generate else block
        bool elseTerminated = false;
        if (hasElse) {
            function->insert(function->end(), elseBB);
            builder.SetInsertPoint(elseBB);
            for (auto& s : ifStmt->elseBody) {
                generateStmt(s.get(), function);
            }
            elseTerminated = builder.GetInsertBlock()->getTerminator() != nullptr;
            if (!elseTerminated) {
                builder.CreateBr(mergeBB);
            }
        }
        
        // Only add merge block if at least one branch needs it
        bool needMerge = !thenTerminated || (hasElse && !elseTerminated) || !hasElse;
        if (needMerge) {
            function->insert(function->end(), mergeBB);
            builder.SetInsertPoint(mergeBB);
        }
        // If both branches terminated, don't set insert point (function ends here)
        
        return;
    }
    
    if (auto* whileStmt = dynamic_cast<WhileStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(whileStmt->line, whileStmt->column);
        }
        
        llvm::BasicBlock* condBB = llvm::BasicBlock::Create(context, "whilecond", function);
        llvm::BasicBlock* loopBB = llvm::BasicBlock::Create(context, "loop");
        llvm::BasicBlock* afterBB = llvm::BasicBlock::Create(context, "afterloop");
        
        // Save previous loop context
        llvm::BasicBlock* prevLoopCond = currentLoopCond;
        llvm::BasicBlock* prevLoopAfter = currentLoopAfter;
        
        // Set current loop context
        currentLoopCond = condBB;
        currentLoopAfter = afterBB;
        
        // Jump to condition block
        builder.CreateBr(condBB);
        
        // Generate condition block
        builder.SetInsertPoint(condBB);
        llvm::Value* condVal = generateExpr(whileStmt->condition.get());
        if (!condVal) {
            throw std::runtime_error("Failed to generate while condition");
        }
        
        // Convert condition to bool if it's not already an i1
        if (condVal->getType() != builder.getInt1Ty()) {
            condVal = builder.CreateICmpNE(
                condVal, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), "loopcond");
        }
        
        builder.CreateCondBr(condVal, loopBB, afterBB);
        
        // Generate loop body
        function->insert(function->end(), loopBB);
        builder.SetInsertPoint(loopBB);
        for (auto& s : whileStmt->body) {
            generateStmt(s.get(), function);
        }
        // Only create branch if block doesn't already have a terminator (from break/continue)
        if (!builder.GetInsertBlock()->getTerminator()) {
            builder.CreateBr(condBB);
        }
        
        // Restore previous loop context
        currentLoopCond = prevLoopCond;
        currentLoopAfter = prevLoopAfter;
        
        // Generate after block
        function->insert(function->end(), afterBB);
        builder.SetInsertPoint(afterBB);
        return;
    }
    
    if (auto* breakStmt = dynamic_cast<BreakStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(breakStmt->line, breakStmt->column);
        }
        
        if (!currentLoopAfter) {
            throw std::runtime_error("Break statement outside of loop");
        }
        
        // Note: We don't clean up scopes here because break only exits the loop,
        // not the entire function. The loop's scope will be cleaned up when the
        // loop ends normally.
        
        builder.CreateBr(currentLoopAfter);
        // Create a new basic block for any code after the break (dead code)
        llvm::BasicBlock* deadBB = llvm::BasicBlock::Create(context, "afterbreak", builder.GetInsertBlock()->getParent());
        builder.SetInsertPoint(deadBB);
        return;
    }
    
    if (auto* continueStmt = dynamic_cast<ContinueStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(continueStmt->line, continueStmt->column);
        }
        
        if (!currentLoopCond) {
            throw std::runtime_error("Continue statement outside of loop");
        }
        builder.CreateBr(currentLoopCond);
        // Create a new basic block for any code after the continue (dead code)
        llvm::BasicBlock* deadBB = llvm::BasicBlock::Create(context, "aftercontinue", builder.GetInsertBlock()->getParent());
        builder.SetInsertPoint(deadBB);
        return;
    }
    
    if (auto* retStmt = dynamic_cast<ReturnStmtAST*>(stmt)) {
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(retStmt->line, retStmt->column);
        }
        
        llvm::Value* retVal = generateExpr(retStmt->value.get());
        if (!retVal) {
            throw std::runtime_error("Failed to generate return value");
        }
        
        // Clean up all scopes before returning
        while (!scopeStack.empty()) {
            generateScopeCleanup(function);
            scopeStack.pop_back();
        }
        
        builder.CreateRet(retVal);
        return;
    }
    
    throw std::runtime_error("Unknown statement type");
}

void CodeGenerator::generateFunction(FunctionAST* func, const std::string& namespaceName) {
    // Determine the actual function name (with namespace if applicable)
    std::string functionName = namespaceName.empty() ? func->name : namespaceName + "." + func->name;
    
    // Get the function that was already declared
    llvm::Function* function = module->getFunction(functionName);
    if (!function) {
        throw std::runtime_error("Function declaration not found: " + functionName);
    }
    
    // If this is an extern function, don't generate a body
    if (func->isExtern) {
        return;
    }
    
    // Create debug info for function if enabled
    llvm::DISubprogram* debugSubprogram = nullptr;
    if (generateDebugInfo) {
        llvm::DISubroutineType* debugFuncType = createFunctionDebugType(func);
        debugSubprogram = debugBuilder->createFunction(
            debugFile,                  // Scope
            func->name,                 // Name
            func->name,                 // Linkage name
            debugFile,                  // File
            func->line,                 // Line number
            debugFuncType,              // Type
            func->line,                 // Scope line
            llvm::DINode::FlagPrototyped, // Flags
            llvm::DISubprogram::SPFlagDefinition  // SP Flags
        );
        function->setSubprogram(debugSubprogram);
        debugScopeStack.push_back(debugSubprogram);
    }
    
    // Create entry block
    llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", function);
    builder.SetInsertPoint(entry);
    
    // Clear named values for new function
    namedValues.clear();
    variableTypes.clear();
    variableTypes.clear();
    
    // Allocate stack space for parameters
    auto argIter = function->args().begin();
    for (size_t paramIdx = 0; paramIdx < func->parameters.size(); paramIdx++) {
        llvm::Type* paramType = getParamTypeFromString(context, func->parameters[paramIdx].type, &structTypes);
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, func->parameters[paramIdx].name, paramType);
        
        // Emit debug location for parameter (before store)
        if (generateDebugInfo) {
            emitLocation(func->line, func->parameters[paramIdx].column);
        }
        
        // Store the parameter value
        builder.CreateStore(&(*argIter), alloca);
        namedValues[func->parameters[paramIdx].name] = alloca;
        variableTypes[func->parameters[paramIdx].name] = func->parameters[paramIdx].type;
        argIter++;
        
        // If this is an array parameter, also store the hidden length parameter
        if (isArrayParam(func->parameters[paramIdx].type)) {
            std::string lengthVarName = func->parameters[paramIdx].name + ".__length";
            llvm::AllocaInst* lengthAlloca = createEntryBlockAlloca(function, lengthVarName, llvm::Type::getInt32Ty(context));
            builder.CreateStore(&(*argIter), lengthAlloca);
            namedValues[lengthVarName] = lengthAlloca;
            argIter++;
        }
        
        // Create debug info for parameter
        if (generateDebugInfo) {
            
            // Create debug info for parameter
            llvm::DILocalVariable* debugParam = debugBuilder->createParameterVariable(
                debugSubprogram,                    // Scope
                func->parameters[paramIdx].name,    // Name
                paramIdx + 1,                       // Arg number (1-indexed)
                debugFile,                          // File
                func->line,                         // Line
                debugBuilder->createBasicType("int", 32, llvm::dwarf::DW_ATE_signed) // Type
            );
            
            debugBuilder->insertDeclare(
                alloca,
                debugParam,
                debugBuilder->createExpression(),
                llvm::DILocation::get(context, func->line, func->parameters[paramIdx].column, debugSubprogram),
                builder.GetInsertBlock()
            );
        }
    }
    
    // Push a scope for the function body
    pushScope();
    
    // Generate function body
    for (auto& stmt : func->body) {
        generateStmt(stmt.get(), function);
    }
    
    // If function doesn't have a terminator (return), clean up and add one
    if (!builder.GetInsertBlock()->getTerminator()) {
        // Clean up the function scope
        popScope(function);
        
        // Add a default return value (0 for int, 0.0 for float, etc.)
        llvm::Type* retType = function->getReturnType();
        if (retType->isIntegerTy()) {
            builder.CreateRet(llvm::ConstantInt::get(retType, 0));
        } else if (retType->isFloatingPointTy()) {
            builder.CreateRet(llvm::ConstantFP::get(retType, 0.0));
        } else if (retType->isPointerTy()) {
            builder.CreateRet(llvm::ConstantPointerNull::get(llvm::cast<llvm::PointerType>(retType)));
        } else {
            builder.CreateRetVoid();
        }
    }
    
    // Pop debug scope
    if (generateDebugInfo) {
        debugScopeStack.pop_back();
    }
    
    // Check if this function calls memset and add no-builtin attribute if needed
    llvm::Function* memsetFunc = module->getFunction("memset");
    if (memsetFunc) {
        for (auto& bb : *function) {
            for (auto& inst : bb) {
                if (auto* callInst = llvm::dyn_cast<llvm::CallInst>(&inst)) {
                    if (callInst->getCalledFunction() == memsetFunc) {
                        function->addFnAttr("no-builtin-memset");
                        goto done_checking;  // Break out of nested loops
                    }
                }
            }
        }
    }
done_checking:
    
    // Verify function
    if (llvm::verifyFunction(*function, &llvm::errs())) {
        throw std::runtime_error("Function verification failed for: " + func->name);
    }
}

void CodeGenerator::createMinimalEntryPoint() {
    // Declare ExitProcess from kernel32
    llvm::FunctionType* exitProcessType = llvm::FunctionType::get(
        llvm::Type::getVoidTy(context),
        {llvm::Type::getInt32Ty(context)},
        false
    );
    llvm::Function* exitProcessFunc = llvm::Function::Create(
        exitProcessType,
        llvm::Function::ExternalLinkage,
        "ExitProcess",
        module.get()
    );
    
    // Get the main function - try simple name first, then look for any namespace::main
    llvm::Function* mainFunc = module->getFunction("main");
    if (!mainFunc) {
        // Look for main in any namespace (e.g., examples.main)
        for (auto& func : module->functions()) {
            std::string funcName = func.getName().str();
            // Check if the function name ends with .main
            if (funcName == "main" || 
                (funcName.size() > 5 && funcName.substr(funcName.size() - 5) == ".main")) {
                mainFunc = &func;
                break;
            }
        }
    }
    
    if (!mainFunc) {
        throw std::runtime_error("main function not found");
    }
    
    // Check if main takes arguments: main(args []string) has 2 LLVM params (ptr, i32 hidden length)
    bool mainTakesArgs = (mainFunc->arg_size() == 2);
    
    // Declare Windows command-line API functions if main takes arguments
    llvm::Function* getCommandLineFunc = nullptr;
    llvm::Function* commandLineToArgvFunc = nullptr;
    
    if (mainTakesArgs) {
        // Declare GetCommandLineW - returns wchar_t* (opaque pointer)
        llvm::FunctionType* getCommandLineType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),  // wchar_t* as opaque pointer
            false
        );
        getCommandLineFunc = llvm::Function::Create(
            getCommandLineType,
            llvm::Function::ExternalLinkage,
            "GetCommandLineW",
            module.get()
        );
        
        // Declare CommandLineToArgvW - returns wchar_t** and fills pNumArgs
        llvm::FunctionType* commandLineToArgvType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),  // wchar_t** as opaque pointer
            {llvm::PointerType::get(context, 0),  // wchar_t* lpCmdLine
             llvm::PointerType::get(context, 0)}, // int* pNumArgs
            false
        );
        commandLineToArgvFunc = llvm::Function::Create(
            commandLineToArgvType,
            llvm::Function::ExternalLinkage,
            "CommandLineToArgvW",
            module.get()
        );
    }
    
    // Create _start function as the real entry point
    llvm::FunctionType* startType = llvm::FunctionType::get(
        llvm::Type::getVoidTy(context),
        false
    );
    llvm::Function* startFunc = llvm::Function::Create(
        startType,
        llvm::Function::ExternalLinkage,
        "_start",
        module.get()
    );
    
    // Generate body: call main, then ExitProcess with main's return value
    llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", startFunc);
    llvm::IRBuilder<> tmpBuilder(entry);
    
    llvm::Value* mainRetVal = nullptr;
    
    if (mainTakesArgs) {
        // Get command line
        llvm::Value* cmdLine = tmpBuilder.CreateCall(getCommandLineFunc);
        
        // Allocate space for argc
        llvm::Value* argcPtr = tmpBuilder.CreateAlloca(llvm::Type::getInt32Ty(context));
        
        // Parse command line to argv
        llvm::Value* argvW = tmpBuilder.CreateCall(commandLineToArgvFunc, {cmdLine, argcPtr});
        
        // Load argc
        llvm::Value* argc = tmpBuilder.CreateLoad(llvm::Type::getInt32Ty(context), argcPtr);
        
        // argv is already an opaque pointer, no cast needed in opaque pointer model
        llvm::Value* argv = argvW;
        
        // Call main with argv and argc (main(args []string) gets argv as pointer, argc as hidden length)
        mainRetVal = tmpBuilder.CreateCall(mainFunc, {argv, argc});
    } else {
        // Call main without arguments
        mainRetVal = tmpBuilder.CreateCall(mainFunc);
    }
    
    tmpBuilder.CreateCall(exitProcessFunc, {mainRetVal});
    tmpBuilder.CreateUnreachable();  // ExitProcess never returns
}

void CodeGenerator::generate(ProgramAST* program, bool needsEntryPoint) {
    // First pass: Create all struct types
    for (const auto& structDef : program->structs) {
        // Create opaque struct type
        llvm::StructType* structType = llvm::StructType::create(context, structDef->name);
        structTypes[structDef->name] = structType;
        
        // Store field information for later use
        std::vector<std::pair<std::string, std::string>> fields;
        for (const auto& field : structDef->fields) {
            fields.push_back({field.name, field.type});
        }
        structFields[structDef->name] = fields;
    }
    
    // Second pass: Set struct body (now that all struct types are declared)
    for (const auto& structDef : program->structs) {
        std::vector<llvm::Type*> fieldTypes;
        for (const auto& field : structDef->fields) {
            fieldTypes.push_back(getTypeFromString(context, field.type, &structTypes));
        }
        structTypes[structDef->name]->setBody(fieldTypes);
    }
    
    // Third pass: Create all function declarations (including namespace functions)
    for (auto& func : program->functions) {
        // Get return type
        llvm::Type* returnType = getTypeFromString(context, func->returnType, &structTypes);
        
        // Create parameter types (including hidden length parameters for arrays)
        std::vector<llvm::Type*> paramTypes;
        for (const auto& param : func->parameters) {
            paramTypes.push_back(getParamTypeFromString(context, param.type, &structTypes));
            // Add hidden length parameter for array parameters
            if (isArrayParam(param.type)) {
                paramTypes.push_back(llvm::Type::getInt32Ty(context));
            }
        }
        
        // Create function type
        llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, paramTypes, false);
        
        // Determine the actual function name (with namespace if applicable)
        std::string functionName = func->namespaceName.empty() ? func->name : func->namespaceName + "." + func->name;
        
        // Determine linkage type: ExternalLinkage for main and extern functions, InternalLinkage for others
        llvm::GlobalValue::LinkageTypes linkage = 
            (func->name == "main" || func->isExtern) ? llvm::Function::ExternalLinkage : llvm::Function::InternalLinkage;
        
        // Create function
        llvm::Function::Create(funcType, linkage, functionName, module.get());
    }
    
    // Create namespace functions with qualified names
    for (auto& ns : program->namespaces) {
        for (auto& func : ns->functions) {
            // Get return type
            llvm::Type* returnType = getTypeFromString(context, func->returnType, &structTypes);
            
            // Create parameter types (including hidden length parameters for arrays)
            std::vector<llvm::Type*> paramTypes;
            for (const auto& param : func->parameters) {
                paramTypes.push_back(getParamTypeFromString(context, param.type, &structTypes));
                // Add hidden length parameter for array parameters
                if (isArrayParam(param.type)) {
                    paramTypes.push_back(llvm::Type::getInt32Ty(context));
                }
            }
            
            // Create function type
            llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, paramTypes, false);
            
            std::string qualifiedName = ns->name + "." + func->name;
            
            if (func->isExtern) {
                // For extern functions in namespaces:
                // 1. Create the actual extern with simple name (for linker)
                llvm::Function::Create(funcType, llvm::Function::ExternalLinkage,
                                     func->name, module.get());
                
                // 2. Create a wrapper with qualified name that calls the extern
                llvm::Function* wrapperFunc = llvm::Function::Create(funcType, llvm::Function::ExternalLinkage,
                                     qualifiedName, module.get());
                
                // 3. Generate wrapper body that just forwards to the extern
                llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", wrapperFunc);
                builder.SetInsertPoint(entry);
                
                // Get the extern function
                llvm::Function* externFunc = module->getFunction(func->name);
                
                // Collect arguments
                std::vector<llvm::Value*> args;
                for (auto& arg : wrapperFunc->args()) {
                    args.push_back(&arg);
                }
                
                // Call extern and return result
                if (returnType->isVoidTy()) {
                    builder.CreateCall(externFunc, args);
                    builder.CreateRetVoid();
                } else {
                    llvm::Value* result = builder.CreateCall(externFunc, args);
                    builder.CreateRet(result);
                }
            } else {
                // Regular namespace function - use InternalLinkage for dead code elimination
                // (main is always top-level, never in a namespace)
                llvm::Function::Create(funcType, llvm::Function::InternalLinkage,
                                     qualifiedName, module.get());
            }
        }
    }
    
    // Second pass: Generate function bodies
    for (auto& func : program->functions) {
        generateFunction(func.get(), func->namespaceName);
    }
    
    // Generate namespace function bodies
    for (auto& ns : program->namespaces) {
        for (auto& func : ns->functions) {
            generateFunction(func.get(), ns->name);
        }
    }
    
    // Create minimal CRT entry point only if needed (for executables)
    if (needsEntryPoint) {
        createMinimalEntryPoint();
    }
    
    // Inject profiling instrumentation if enabled
    if (enableProfiling) {
        // First inject counters in all basic blocks
        injectInstrCounter();
        
        // Then add profile output before main returns
        // Find the main function (it might be mangled with namespace)
        llvm::Function* mainFunc = nullptr;
        for (auto& func : *module) {
            if (func.getName().contains("::main") || func.getName() == "main") {
                mainFunc = &func;
                break;
            }
        }
        
        if (mainFunc) {
            injectProfileOutput(mainFunc);
        }
    }
    
    // Finalize debug info if enabled
    finalizeDebugInfo();
}

void CodeGenerator::optimize() {
    // Use LLVM's PassBuilder to run an O3-style pipeline over the module.
    llvm::LoopAnalysisManager     loopAM;
    llvm::FunctionAnalysisManager funcAM;
    llvm::CGSCCAnalysisManager    cgsccAM;
    llvm::ModuleAnalysisManager   moduleAM;

    llvm::PassBuilder pb;

    pb.registerModuleAnalyses(moduleAM);
    pb.registerCGSCCAnalyses(cgsccAM);
    pb.registerFunctionAnalyses(funcAM);
    pb.registerLoopAnalyses(loopAM);
    pb.crossRegisterProxies(loopAM, funcAM, cgsccAM, moduleAM);

    llvm::ModulePassManager mpm = pb.buildPerModuleDefaultPipeline(llvm::OptimizationLevel::O3);
    mpm.run(*module, moduleAM);
}

void CodeGenerator::runDeadCodeElimination() {
    // Run minimal dead code elimination to remove unused internal functions
    // This ensures the linker's /OPT:REF can work effectively
    llvm::LoopAnalysisManager     loopAM;
    llvm::FunctionAnalysisManager funcAM;
    llvm::CGSCCAnalysisManager    cgsccAM;
    llvm::ModuleAnalysisManager   moduleAM;

    llvm::PassBuilder pb;

    pb.registerModuleAnalyses(moduleAM);
    pb.registerCGSCCAnalyses(cgsccAM);
    pb.registerFunctionAnalyses(funcAM);
    pb.registerLoopAnalyses(loopAM);
    pb.crossRegisterProxies(loopAM, funcAM, cgsccAM, moduleAM);

    // Run only GlobalDCE pass to remove unused internal functions
    llvm::ModulePassManager mpm;
    mpm.addPass(llvm::GlobalDCEPass());
    mpm.run(*module, moduleAM);
}

void CodeGenerator::printIR() {
    module->print(llvm::outs(), nullptr);
}

void CodeGenerator::writeIRToFile(const std::string& filename) {
    std::error_code EC;
    llvm::raw_fd_ostream dest(filename, EC);
    
    if (EC) {
        throw std::runtime_error("Could not open file: " + EC.message());
    }
    
    module->print(dest, nullptr);
    dest.flush();
}

void CodeGenerator::writeObjectFile(const std::string& filename) {
    // Initialize targets
    llvm::InitializeAllTargetInfos();
    llvm::InitializeAllTargets();
    llvm::InitializeAllTargetMCs();
    llvm::InitializeAllAsmParsers();
    llvm::InitializeAllAsmPrinters();
    
    // Get target triple - hardcode for x86-64 since that's what we're building for
    llvm::Triple targetTriple("x86_64-pc-windows-msvc");
    module->setTargetTriple(targetTriple);
    
    // Get target
    std::string error;
    auto target = llvm::TargetRegistry::lookupTarget(targetTriple.str(), error);
    
    if (!target) {
        throw std::runtime_error("Failed to lookup target: " + error);
    }
    
    // Configure target machine
    auto CPU = "generic";
    auto features = "";
    llvm::TargetOptions opt;
    auto targetMachine = target->createTargetMachine(
        targetTriple, CPU, features, opt, llvm::Reloc::PIC_, std::nullopt, llvm::CodeGenOptLevel::Aggressive);
    
    module->setDataLayout(targetMachine->createDataLayout());
    
    // Open output file
    std::error_code ec;
    llvm::raw_fd_ostream dest(filename, ec, llvm::sys::fs::OF_None);
    
    if (ec) {
        throw std::runtime_error("Could not open file: " + ec.message());
    }
    
    // Emit object file
    llvm::legacy::PassManager pass;
    auto fileType = llvm::CodeGenFileType::ObjectFile;
    
    if (targetMachine->addPassesToEmitFile(pass, dest, nullptr, fileType)) {
        throw std::runtime_error("TargetMachine can't emit a file of this type");
    }
    
    pass.run(*module);
    dest.flush();
    
    if (verbose) {
        std::cout << "Object file written to " << filename << std::endl;
    }
}

void CodeGenerator::writeExecutable(const std::string& exeFile, llvm::raw_ostream* errorStream) {
    // Initialize targets
    llvm::InitializeAllTargetInfos();
    llvm::InitializeAllTargets();
    llvm::InitializeAllTargetMCs();
    llvm::InitializeAllAsmParsers();
    llvm::InitializeAllAsmPrinters();
    
    // Get target triple for x86-64 Windows
    llvm::Triple targetTriple("x86_64-pc-windows-msvc");
    module->setTargetTriple(targetTriple);
    
    // Get target
    std::string error;
    auto target = llvm::TargetRegistry::lookupTarget(targetTriple.str(), error);
    
    if (!target) {
        throw std::runtime_error("Failed to lookup target: " + error);
    }
    
    // Configure target machine
    auto CPU = "generic";
    auto features = "";
    llvm::TargetOptions opt;
    auto targetMachine = target->createTargetMachine(
        targetTriple, CPU, features, opt, llvm::Reloc::PIC_, std::nullopt, llvm::CodeGenOptLevel::Aggressive);
    
    module->setDataLayout(targetMachine->createDataLayout());
    
    // First, write to a temporary object file
    std::string tempObjFile = exeFile + ".tmp.obj";
    std::error_code ec;
    llvm::raw_fd_ostream dest(tempObjFile, ec, llvm::sys::fs::OF_None);
    
    if (ec) {
        throw std::runtime_error("Could not create temporary object file: " + ec.message());
    }
    
    // Emit object file
    llvm::legacy::PassManager pass;
    auto fileType = llvm::CodeGenFileType::ObjectFile;
    
    if (targetMachine->addPassesToEmitFile(pass, dest, nullptr, fileType)) {
        throw std::runtime_error("TargetMachine can't emit a file of this type");
    }
    
    pass.run(*module);
    dest.flush();
    dest.close();
    
    if (verbose) {
        std::cout << "Object file generated." << std::endl;
    }
    
    // Use LLD as a library (in-process linking)
    if (verbose) {
        std::cout << "Linking with LLD library (in-process)..." << std::endl;
    }
    
    // Prepare arguments for LLD driver
    std::vector<std::string> argStorage;  // Store strings so pointers remain valid
    std::vector<const char*> lldArgs;
    
    argStorage.push_back("lld-link");  // Program name
    argStorage.push_back("/NOLOGO");
    argStorage.push_back("/MACHINE:X64");
    argStorage.push_back("/SUBSYSTEM:CONSOLE");
    
    // Add debug info if enabled
    if (generateDebugInfo) {
        argStorage.push_back("/DEBUG");
    }
    
    // Add library path for Windows SDK (needed for kernel32.lib)
    argStorage.push_back("/LIBPATH:C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\um\\x64");
	argStorage.push_back("/DEFAULTLIB:kernel32.lib");
	argStorage.push_back("/NODEFAULTLIB"); // Don't link default CRT libraries
	argStorage.push_back("/ENTRY:_start");	// Set entry point to _start (our minimal wrapper)
	
	// Size optimization flags (only when not generating debug info)
	if (!generateDebugInfo) {
		argStorage.push_back("/OPT:REF");    // Remove unreferenced functions/data
		argStorage.push_back("/OPT:ICF");    // Identical COMDAT folding
		argStorage.push_back("/MERGE:.rdata=.text"); // Merge read-only data into code section
	}

	// Output file
    argStorage.push_back("/OUT:" + exeFile);
    
    // Input object file (program)
    argStorage.push_back(tempObjFile);
    
    // Convert to const char* pointers
    for (const auto& arg : argStorage) {
        lldArgs.push_back(arg.c_str());
    }
    
    // Add Maxon runtime library
    // Find runtime.obj in the same directory as the executable
    std::string execPath;
    #ifdef _WIN32
    char buffer[MAX_PATH];
    GetModuleFileNameA(NULL, buffer, MAX_PATH);
    execPath = buffer;
    #endif
    size_t lastSlash = execPath.find_last_of("\\/");
    std::string execDir = (lastSlash != std::string::npos) ? execPath.substr(0, lastSlash) : ".";
    std::string runtimeObj = execDir + "/runtime.obj";
    
    // Check if runtime.obj exists
    if (llvm::sys::fs::exists(runtimeObj)) {
        argStorage.push_back(runtimeObj);
        if (verbose) {
            std::cout << "Linking with Maxon runtime library: " << runtimeObj << std::endl;
        }
    } else if (verbose) {
        std::cout << "Warning: Maxon runtime library not found at " << runtimeObj << std::endl;
    }
    
    // Explicitly link required Windows libraries
    argStorage.push_back("kernel32.lib");
    argStorage.push_back("shell32.lib");  // For CommandLineToArgvW
    
    // Rebuild lldArgs with final pointers
    lldArgs.clear();
    for (const auto& arg : argStorage) {
        lldArgs.push_back(arg.c_str());
    }
    
    // Call LLD driver directly (in-process)
    llvm::raw_ostream& errStream = errorStream ? *errorStream : llvm::errs();
    bool success = lld::coff::link(lldArgs, llvm::outs(), errStream, false, false);
    
    // Clean up temporary object file
    llvm::sys::fs::remove(tempObjFile);
    
    if (!success) {
        throw std::runtime_error("LLD linking failed");
    }
    
    if (verbose) {
        std::cout << "Executable written to " << exeFile << std::endl;
    }
}

void CodeGenerator::initDebugInfo(const std::string& filename) {
    // Create debug builder
    debugBuilder = std::make_unique<llvm::DIBuilder>(*module);
    
    // Extract just the filename from the path
    size_t lastSlash = filename.find_last_of("/\\");
    std::string file = (lastSlash != std::string::npos) ? filename.substr(lastSlash + 1) : filename;
    
    // Get directory (or use relative path)
    std::string directory = (lastSlash != std::string::npos) ? filename.substr(0, lastSlash) : ".";
    
    // Create debug file
    debugFile = debugBuilder->createFile(file, directory);
    
    // Create compile unit
    debugCompileUnit = debugBuilder->createCompileUnit(
        llvm::dwarf::DW_LANG_C,  // Using C as the language
        debugFile,
        "Maxon Compiler",         // Producer
        false,                    // isOptimized
        "",                       // Flags
        0                         // Runtime version
    );
    
    // Add module flags for debug info
    module->addModuleFlag(llvm::Module::Warning, "CodeView", 1);
    module->addModuleFlag(llvm::Module::Warning, "Debug Info Version",
                         llvm::DEBUG_METADATA_VERSION);
}

void CodeGenerator::finalizeDebugInfo() {
    if (generateDebugInfo && debugBuilder) {
        debugBuilder->finalize();
    }
}

void CodeGenerator::emitLocation(int line, int column) {
    if (!generateDebugInfo || debugScopeStack.empty()) {
        return;
    }
    
    llvm::DIScope* scope = debugScopeStack.back();
    llvm::DILocation* loc = llvm::DILocation::get(
        scope->getContext(), line, column, scope);
    builder.SetCurrentDebugLocation(loc);
}

llvm::DISubroutineType* CodeGenerator::createFunctionDebugType(FunctionAST* func) {
    if (!generateDebugInfo) {
        return nullptr;
    }
    
    // Create basic int type for now
    llvm::DIType* intType = debugBuilder->createBasicType("int", 32, llvm::dwarf::DW_ATE_signed);
    
    // Build parameter types
    llvm::SmallVector<llvm::Metadata*, 8> paramTypes;
    paramTypes.push_back(intType);  // Return type
    
    for (size_t i = 0; i < func->parameters.size(); i++) {
        paramTypes.push_back(intType);  // All params are int for now
    }
    
    return debugBuilder->createSubroutineType(
        debugBuilder->getOrCreateTypeArray(paramTypes));
}
