#include "../codegen.h"
#include "codegen_types.h"
#include "../lexer.h"
#include <llvm/IR/Constants.h>
#include <llvm/IR/DerivedTypes.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Instructions.h>
#include <llvm/IR/Intrinsics.h>

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
