#include "codegen.h"
#include <llvm/IR/Verifier.h>
#include <llvm/IR/LegacyPassManager.h>
#include <llvm/Transforms/InstCombine/InstCombine.h>
#include <llvm/Transforms/Scalar.h>
#include <llvm/Transforms/Scalar/GVN.h>
#include <llvm/Transforms/Utils.h>
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

// Forward declare COFF driver function
namespace lld {
namespace coff {
bool link(llvm::ArrayRef<const char *> args, llvm::raw_ostream &stdoutOS,
          llvm::raw_ostream &stderrOS, bool exitEarly, bool disableOutput);
}
}

// Helper function to convert Maxon type string to LLVM type
static llvm::Type* getTypeFromString(llvm::LLVMContext& context, const std::string& typeStr) {
    if (typeStr == "int") {
        return llvm::Type::getInt32Ty(context);
    } else if (typeStr == "float") {
        return llvm::Type::getDoubleTy(context);  // 64-bit float
    } else if (typeStr == "ptr") {
        return llvm::PointerType::get(context, 0);  // Opaque pointer
    } else if (typeStr == "char") {
        return llvm::Type::getInt8Ty(context);
    } else if (typeStr == "void") {
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
        llvm::Type* elementType = getTypeFromString(context, elementTypeStr);
        
        return llvm::ArrayType::get(elementType, arraySize);
    } else {
        throw std::runtime_error("Unknown type: " + typeStr);
    }
}

// Convert a type to the form used for function parameters
// Arrays are passed as pointers
static llvm::Type* getParamTypeFromString(llvm::LLVMContext& context, const std::string& typeStr) {
    if (typeStr[0] == '[') {
        // Array parameter - pass as pointer (opaque pointer for arrays)
        return llvm::PointerType::get(context, 0);
    } else {
        // Regular parameter type
        return getTypeFromString(context, typeStr);
    }
}

CodeGenerator::CodeGenerator(const std::string& moduleName, bool debugInfo, bool verbose)
    : builder(context), module(std::make_unique<llvm::Module>(moduleName, context)),
      generateDebugInfo(debugInfo), verbose(verbose), sourceFileName(moduleName) {
    if (generateDebugInfo) {
        initDebugInfo(moduleName);
    }
    
    // Add _fltused symbol for Windows floating-point support
    // This is required when using floating-point operations on Windows
    new llvm::GlobalVariable(
        *module,
        llvm::Type::getInt32Ty(context),
        true,  // isConstant
        llvm::GlobalValue::ExternalLinkage,
        llvm::ConstantInt::get(context, llvm::APInt(32, 0x9875, false)),
        "_fltused"
    );
    
    // Don't initialize standard library here - it will be done on demand
    // Don't initialize heap management here - it will be done on demand when arrays are allocated
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
    
    // Initialize buffers to zero
    builder.CreateMemSet(buffer, llvm::ConstantInt::get(charType, 0), 12, llvm::MaybeAlign(1));
    builder.CreateMemSet(temp, llvm::ConstantInt::get(charType, 0), 12, llvm::MaybeAlign(1));
    
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

llvm::Value* CodeGenerator::generateMathIntrinsic(CallExprAST* callExpr) {
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
    
    // Map function name to LLVM intrinsic
    llvm::Intrinsic::ID intrinsicID = llvm::Intrinsic::not_intrinsic;
    bool returnsInt = false;  // For floor, ceil, round, trunc
    
    if (callExpr->callee == "sqrt") {
        intrinsicID = llvm::Intrinsic::sqrt;
    } else if (callExpr->callee == "abs") {
        intrinsicID = llvm::Intrinsic::fabs;
    } else if (callExpr->callee == "floor") {
        intrinsicID = llvm::Intrinsic::floor;
        returnsInt = true;
    } else if (callExpr->callee == "ceil") {
        intrinsicID = llvm::Intrinsic::ceil;
        returnsInt = true;
    } else if (callExpr->callee == "sin") {
        intrinsicID = llvm::Intrinsic::sin;
    } else if (callExpr->callee == "cos") {
        intrinsicID = llvm::Intrinsic::cos;
    } else if (callExpr->callee == "round") {
        intrinsicID = llvm::Intrinsic::round;
        returnsInt = true;
    } else if (callExpr->callee == "trunc") {
        // trunc is special - just convert float to int
        if (args.size() != 1) {
            throw std::runtime_error("trunc expects exactly 1 argument");
        }
        return builder.CreateFPToSI(args[0], llvm::Type::getInt32Ty(context), "trunc");
    }
    
    // Get the intrinsic declaration
    llvm::Function* intrinsicFn = llvm::Intrinsic::getOrInsertDeclaration(
        module.get(),
        intrinsicID,
        {llvm::Type::getDoubleTy(context)}
    );
    
    if (!intrinsicFn) {
        throw std::runtime_error("Failed to get intrinsic declaration for " + callExpr->callee);
    }
    
    // Call the intrinsic
    llvm::Value* result = builder.CreateCall(intrinsicFn, args, callExpr->callee + ".result");
    
    // Convert to int if needed
    if (returnsInt) {
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
    
    if (auto* castExpr = dynamic_cast<CastExprAST*>(expr)) {
        llvm::Value* value = generateExpr(castExpr->expr.get());
        if (!value) {
            throw std::runtime_error("Failed to generate expression for cast");
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
            // Float to integer (truncate toward zero)
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
        
        // Check if this is an array or a pointer (array parameter)
        if (allocatedType->isPointerTy()) {
            // This is a pointer to an array (parameter case)
            // Need to determine the element type from the parameter
            // For now, assume i32 (int) as the default element type
            llvm::Type* elementType = llvm::Type::getInt32Ty(context);
            
            // First load the pointer, then use GEP with the index
            llvm::Value* arrayPtr = builder.CreateLoad(allocatedType, alloca, "arrayptr");
            llvm::Value* elementPtr = builder.CreateInBoundsGEP(
                elementType,
                arrayPtr,
                indexVal,
                "arrayidx"
            );
            // Load the element
            return builder.CreateLoad(elementType, elementPtr, "arrayelem");
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
            
            // Load the element
            llvm::ArrayType* arrayType = llvm::cast<llvm::ArrayType>(allocatedType);
            llvm::Type* elementType = arrayType->getElementType();
            return builder.CreateLoad(elementType, elementPtr, "arrayelem");
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
        // Note: log, exp, pow, tan are stdlib functions, not intrinsics
        if (callExpr->callee == "sqrt" || callExpr->callee == "abs" || 
            callExpr->callee == "floor" || callExpr->callee == "ceil" ||
            callExpr->callee == "sin" || callExpr->callee == "cos" ||
            callExpr->callee == "round" || callExpr->callee == "trunc") {
            return generateMathIntrinsic(callExpr);
        }
        
        // Initialize standard library if calling a standard library function
        if (callExpr->callee == "print") {
            initStandardLibrary();
        }
        
        // Look up the function in the module
        // Try exact match first
        llvm::Function* calleeF = module->getFunction(callExpr->callee);
        
        // If not found and unqualified, try suffix matching
        if (!calleeF && callExpr->callee.find("::") == std::string::npos) {
            std::string searchSuffix = "::" + callExpr->callee;
            
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
        
        // Check argument count
        if (calleeF->arg_size() != callExpr->args.size()) {
            throw std::runtime_error("Incorrect number of arguments passed to function: " + callExpr->callee);
        }
        
        // Generate code for arguments
        std::vector<llvm::Value*> argsV;
        size_t argIdx = 0;
        for (auto& arg : callExpr->args) {
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
                        continue;
                    }
                    // Otherwise fall through to normal handling (will load the value)
                }
            }
            
            // Normal argument - generate and potentially load the value
            llvm::Value* argVal = generateExpr(arg.get());
            if (!argVal) {
                throw std::runtime_error("Failed to generate function argument");
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
        
        llvm::Value* initVal = generateExpr(varDecl->initializer.get());
        if (!initVal) {
            throw std::runtime_error("Failed to generate variable initializer");
        }
        
        // Determine the type for the alloca
        llvm::Type* allocaType;
        if (varDecl->arraySize > 0) {
            // Array type: allocate on heap using malloc
            llvm::Type* elementType;
            if (!varDecl->type.empty()) {
                elementType = getTypeFromString(context, varDecl->type);
            } else {
                throw std::runtime_error("Array declaration requires explicit element type at line " + 
                                       std::to_string(varDecl->line));
            }
            
            // Initialize heap management functions on first use
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
            uint64_t totalSize = varDecl->arraySize * elementSize;
            llvm::Value* sizeVal = llvm::ConstantInt::get(
                llvm::Type::getInt64Ty(context), 
                totalSize
            );
            
            // Call malloc
            llvm::Value* arrayPtr = builder.CreateCall(mallocFunc, {sizeVal}, varDecl->name + ".malloc");
            
            // Create alloca to store the pointer
            llvm::AllocaInst* ptrAlloca = createEntryBlockAlloca(
                function, 
                varDecl->name, 
                llvm::PointerType::get(context, 0)
            );
            builder.CreateStore(arrayPtr, ptrAlloca);
            
            // Initialize array elements with the initializer value
            if (initVal) {
                for (int i = 0; i < varDecl->arraySize; i++) {
                    llvm::Value* indexVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), i);
                    llvm::Value* elementPtr = builder.CreateGEP(
                        elementType,
                        arrayPtr,
                        indexVal,
                        "arrayidx"
                    );
                    builder.CreateStore(initVal, elementPtr);
                }
            }
            
            namedValues[varDecl->name] = ptrAlloca;
            
            // Track this array for cleanup
            if (!scopeStack.empty()) {
                scopeStack.back().heapAllocatedArrays.push_back({varDecl->name, ptrAlloca});
            }
            
            // Create debug info for array pointer variable
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
        } else if (!varDecl->type.empty()) {
            // Use explicit type annotation
            allocaType = getTypeFromString(context, varDecl->type);
        } else {
            // Infer from initializer type
            allocaType = initVal->getType();
        }
        
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, varDecl->name, allocaType);
        
        // Only store initializer if not an array (arrays are typically initialized element by element)
        if (varDecl->arraySize == 0) {
            builder.CreateStore(initVal, alloca);
        }
        
        namedValues[varDecl->name] = alloca;
        
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
        
        llvm::Value* initVal = generateExpr(letDecl->initializer.get());
        if (!initVal) {
            throw std::runtime_error("Failed to generate let initializer");
        }
        
        // Determine the type for the alloca
        llvm::Type* allocaType;
        if (letDecl->arraySize > 0) {
            // Array type: allocate on heap using malloc
            llvm::Type* elementType;
            if (!letDecl->type.empty()) {
                elementType = getTypeFromString(context, letDecl->type);
            } else {
                throw std::runtime_error("Array declaration requires explicit element type at line " + 
                                       std::to_string(letDecl->line));
            }
            
            // Initialize heap management functions on first use
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
            uint64_t totalSize = letDecl->arraySize * elementSize;
            llvm::Value* sizeVal = llvm::ConstantInt::get(
                llvm::Type::getInt64Ty(context), 
                totalSize
            );
            
            // Call malloc
            llvm::Value* arrayPtr = builder.CreateCall(mallocFunc, {sizeVal}, letDecl->name + ".malloc");
            
            // Create alloca to store the pointer
            llvm::AllocaInst* ptrAlloca = createEntryBlockAlloca(
                function, 
                letDecl->name, 
                llvm::PointerType::get(context, 0)
            );
            builder.CreateStore(arrayPtr, ptrAlloca);
            
            // Initialize array elements with the initializer value
            if (initVal) {
                for (int i = 0; i < letDecl->arraySize; i++) {
                    llvm::Value* indexVal = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), i);
                    llvm::Value* elementPtr = builder.CreateGEP(
                        elementType,
                        arrayPtr,
                        indexVal,
                        "arrayidx"
                    );
                    builder.CreateStore(initVal, elementPtr);
                }
            }
            
            namedValues[letDecl->name] = ptrAlloca;
            
            // Track this array for cleanup
            if (!scopeStack.empty()) {
                scopeStack.back().heapAllocatedArrays.push_back({letDecl->name, ptrAlloca});
            }
            
            // Create debug info for array pointer variable
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
            
            // Note: immutability is enforced at semantic analysis level
            return;
        } else if (!letDecl->type.empty()) {
            // Use explicit type annotation
            allocaType = getTypeFromString(context, letDecl->type);
        } else {
            // Infer from initializer
            allocaType = initVal->getType();
        }
        
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, letDecl->name, allocaType);
        
        // Only store initializer if not an array
        if (letDecl->arraySize == 0) {
            builder.CreateStore(initVal, alloca);
        }
        namedValues[letDecl->name] = alloca;
        
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
        
        // Note: immutability is enforced at semantic analysis level
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
        
        llvm::Value* val = generateExpr(arrayAssign->value.get());
        if (!val) {
            throw std::runtime_error("Failed to generate array assignment value");
        }
        
        llvm::AllocaInst* alloca = namedValues[arrayAssign->arrayName];
        if (!alloca) {
            throw std::runtime_error("Unknown array name: " + arrayAssign->arrayName);
        }
        
        llvm::Type* allocatedType = alloca->getAllocatedType();
        
        // Check if this is an array or a pointer (array parameter)
        if (allocatedType->isPointerTy()) {
            // This is a pointer to an array (parameter case)
            // First load the pointer, then use GEP with one index
            llvm::Value* arrayPtr = builder.CreateLoad(allocatedType, alloca, "arrayptr");
            llvm::Value* elementPtr = builder.CreateInBoundsGEP(
                val->getType(),  // Use the type of the value being stored
                arrayPtr,
                indexVal,
                "arrayidx"
            );
            builder.CreateStore(val, elementPtr);
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
            builder.CreateStore(val, elementPtr);
        } else {
            throw std::runtime_error("Variable is not an array: " + arrayAssign->arrayName);
        }
        
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
    std::string functionName = namespaceName.empty() ? func->name : namespaceName + "::" + func->name;
    
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
    
    // Allocate stack space for parameters
    size_t idx = 0;
    for (auto& arg : function->args()) {
        llvm::Type* paramType = getParamTypeFromString(context, func->parameters[idx].type);
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, func->parameters[idx].name, paramType);
        
        // Emit debug location for parameter
        if (generateDebugInfo) {
            emitLocation(func->line, func->parameters[idx].column);
            
            // Create debug info for parameter
            llvm::DILocalVariable* debugParam = debugBuilder->createParameterVariable(
                debugSubprogram,             // Scope
                func->parameters[idx].name,  // Name
                idx + 1,                     // Arg number (1-indexed)
                debugFile,                   // File
                func->line,                  // Line
                debugBuilder->createBasicType("int", 32, llvm::dwarf::DW_ATE_signed) // Type
            );
            
            debugBuilder->insertDeclare(
                alloca,
                debugParam,
                debugBuilder->createExpression(),
                llvm::DILocation::get(context, func->line, func->parameters[idx].column, debugSubprogram),
                builder.GetInsertBlock()
            );
        }
        
        builder.CreateStore(&arg, alloca);
        namedValues[func->parameters[idx].name] = alloca;
        idx++;
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
        // Look for main in any namespace (e.g., examples::main)
        for (auto& func : module->functions()) {
            std::string funcName = func.getName().str();
            // Check if the function name ends with ::main
            if (funcName == "main" || 
                (funcName.size() > 6 && funcName.substr(funcName.size() - 6) == "::main")) {
                mainFunc = &func;
                break;
            }
        }
    }
    
    if (!mainFunc) {
        throw std::runtime_error("main function not found");
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
    
    llvm::Value* mainRetVal = tmpBuilder.CreateCall(mainFunc);
    tmpBuilder.CreateCall(exitProcessFunc, {mainRetVal});
    tmpBuilder.CreateUnreachable();  // ExitProcess never returns
}

void CodeGenerator::generate(ProgramAST* program, bool needsEntryPoint) {
    // First pass: Create all function declarations (including namespace functions)
    for (auto& func : program->functions) {
        // Get return type
        llvm::Type* returnType = getTypeFromString(context, func->returnType);
        
        // Create parameter types
        std::vector<llvm::Type*> paramTypes;
        for (const auto& param : func->parameters) {
            paramTypes.push_back(getParamTypeFromString(context, param.type));
        }
        
        // Create function type
        llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, paramTypes, false);
        
        // Determine the actual function name (with namespace if applicable)
        std::string functionName = func->namespaceName.empty() ? func->name : func->namespaceName + "::" + func->name;
        
        // Create function
        llvm::Function::Create(funcType, llvm::Function::ExternalLinkage,
                             functionName, module.get());
    }
    
    // Create namespace functions with qualified names
    for (auto& ns : program->namespaces) {
        for (auto& func : ns->functions) {
            // Get return type
            llvm::Type* returnType = getTypeFromString(context, func->returnType);
            
            // Create parameter types
            std::vector<llvm::Type*> paramTypes;
            for (const auto& param : func->parameters) {
                paramTypes.push_back(getParamTypeFromString(context, param.type));
            }
            
            // Create function type
            llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, paramTypes, false);
            
            std::string qualifiedName = ns->name + "::" + func->name;
            
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
                // Regular namespace function
                llvm::Function::Create(funcType, llvm::Function::ExternalLinkage,
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
    
    // Finalize debug info if enabled
    finalizeDebugInfo();
}

void CodeGenerator::optimize() {
    llvm::legacy::FunctionPassManager fpm(module.get());
    
    // Add optimization passes
    fpm.add(llvm::createPromoteMemoryToRegisterPass()); // mem2reg - promote allocas to registers (must be first)
    fpm.add(llvm::createInstructionCombiningPass()); // Combine redundant instructions
    fpm.add(llvm::createReassociatePass());           // Reassociate expressions
    fpm.add(llvm::createGVNPass());                   // Eliminate common subexpressions
    fpm.add(llvm::createCFGSimplificationPass());     // Simplify control flow
    
    fpm.doInitialization();
    
    // Run passes on all functions
    for (auto& func : module->functions()) {
        fpm.run(func);
    }
    
    fpm.doFinalization();
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
        targetTriple, CPU, features, opt, llvm::Reloc::PIC_);
    
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

void CodeGenerator::writeExecutable(const std::string& exeFile) {
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
        targetTriple, CPU, features, opt, llvm::Reloc::PIC_);
    
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
    std::vector<const char*> lldArgs;
    lldArgs.push_back("lld-link");  // Program name
    lldArgs.push_back("/NOLOGO");
    lldArgs.push_back("/MACHINE:X64");
    lldArgs.push_back("/SUBSYSTEM:CONSOLE");
    
    // Add debug info if enabled
    if (generateDebugInfo) {
        lldArgs.push_back("/DEBUG");
    }
    
    // Add library path for Windows SDK (needed for kernel32.lib)
    std::string sdkLibPath = "/LIBPATH:C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\um\\x64";
    lldArgs.push_back(sdkLibPath.c_str());
	lldArgs.push_back("/DEFAULTLIB:kernel32.lib");
	lldArgs.push_back("/NODEFAULTLIB"); // Don't link default CRT libraries
	lldArgs.push_back("/ENTRY:_start");	// Set entry point to _start (our minimal wrapper)
	
	// Size optimization flags (only when not generating debug info)
	if (!generateDebugInfo) {
		lldArgs.push_back("/OPT:REF");    // Remove unreferenced functions/data
		lldArgs.push_back("/OPT:ICF");    // Identical COMDAT folding
		lldArgs.push_back("/MERGE:.rdata=.text"); // Merge read-only data into code section
	}

	// Output file
    std::string outArg = "/OUT:" + exeFile;
    lldArgs.push_back(outArg.c_str());
    
    // Input object file
    lldArgs.push_back(tempObjFile.c_str());
    
    // Explicitly link kernel32.lib
    lldArgs.push_back("kernel32.lib");
    
    // Call LLD driver directly (in-process)
    bool success = lld::coff::link(lldArgs, llvm::outs(), llvm::errs(), false, false);
    
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
