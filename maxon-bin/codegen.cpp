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
    } else if (typeStr == "ptr") {
        return llvm::PointerType::get(context, 0);  // Opaque pointer
    } else if (typeStr == "char") {
        return llvm::Type::getInt8Ty(context);
    } else if (typeStr == "void") {
        return llvm::Type::getVoidTy(context);
    } else {
        throw std::runtime_error("Unknown type: " + typeStr);
    }
}

CodeGenerator::CodeGenerator(const std::string& moduleName, bool debugInfo)
    : builder(context), module(std::make_unique<llvm::Module>(moduleName, context)),
      generateDebugInfo(debugInfo), sourceFileName(moduleName) {
    if (generateDebugInfo) {
        initDebugInfo(moduleName);
    }
    // Don't initialize standard library here - it will be done on demand
}

void CodeGenerator::initStandardLibrary() {
    // Check if print function already exists (to avoid double-initialization)
    if (module->getFunction("print")) {
        return;
    }
    
    // Declare printf from C standard library
    // int printf(const char* format, ...)
    llvm::FunctionType* printfType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {llvm::PointerType::get(context, 0)},  // Use opaque pointer
        true  // varargs
    );
    llvm::Function::Create(printfType, llvm::Function::ExternalLinkage,
                          "printf", module.get());
    
    // Create a Maxon wrapper function: print(int) -> int
    // This will call printf with "%d\n" format
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
    
    // Save current insert point
    llvm::BasicBlock* savedBlock = builder.GetInsertBlock();
    
    builder.SetInsertPoint(entry);
    
    // Get the printf function
    llvm::Function* printfFunc = module->getFunction("printf");
    
    // Create the format string "%d\n"
    llvm::Constant* formatStr = llvm::ConstantDataArray::getString(context, "%d\n", true);
    llvm::GlobalVariable* formatStrVar = new llvm::GlobalVariable(
        *module,
        formatStr->getType(),
        true,  // constant
        llvm::GlobalValue::PrivateLinkage,
        formatStr,
        ".str"
    );
    
    // Get pointer to the format string (as opaque pointer)
    llvm::Value* formatStrPtr = builder.CreateBitCast(
        formatStrVar,
        llvm::PointerType::get(context, 0)
    );
    
    // Get the argument
    llvm::Value* arg = printFunc->getArg(0);
    
    // Call printf(formatStr, arg)
    builder.CreateCall(printfFunc, {formatStrPtr, arg});
    
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

llvm::Value* CodeGenerator::generateExpr(ExprAST* expr) {
    if (auto* numExpr = dynamic_cast<NumberExprAST*>(expr)) {
        return llvm::ConstantInt::get(context, llvm::APInt(32, numExpr->value, true));
    }
    
    if (auto* boolExpr = dynamic_cast<BooleanExprAST*>(expr)) {
        return llvm::ConstantInt::get(context, llvm::APInt(1, boolExpr->value ? 1 : 0, false));
    }
    
    if (auto* charExpr = dynamic_cast<CharacterExprAST*>(expr)) {
        // Characters are represented as 8-bit integers
        return llvm::ConstantInt::get(context, llvm::APInt(8, (uint8_t)charExpr->value, false));
    }
    
    if (auto* castExpr = dynamic_cast<CastExprAST*>(expr)) {
        llvm::Value* value = generateExpr(castExpr->expr.get());
        if (!value) {
            throw std::runtime_error("Failed to generate expression for cast");
        }
        
        llvm::Type* targetType = nullptr;
        if (castExpr->targetType == "int") {
            targetType = llvm::Type::getInt32Ty(context);
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
        
        // Get element pointer: GEP array, 0, index
        llvm::Value* zero = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
        llvm::Value* elementPtr = builder.CreateInBoundsGEP(
            alloca->getAllocatedType(),
            alloca,
            {zero, indexVal},
            "arrayidx"
        );
        
        // Load the element
        llvm::ArrayType* arrayType = llvm::dyn_cast<llvm::ArrayType>(alloca->getAllocatedType());
        if (!arrayType) {
            throw std::runtime_error("Variable is not an array: " + arrayIndexExpr->arrayName);
        }
        llvm::Type* elementType = arrayType->getElementType();
        return builder.CreateLoad(elementType, elementPtr, "arrayelem");
    }
    
    if (auto* binExpr = dynamic_cast<BinaryExprAST*>(expr)) {
        llvm::Value* left = generateExpr(binExpr->left.get());
        llvm::Value* right = generateExpr(binExpr->right.get());
        
        if (!left || !right) {
            throw std::runtime_error("Failed to generate binary expression operands");
        }
        
        switch (binExpr->op) {
            case '+':
                return builder.CreateAdd(left, right, "addtmp");
            case '-':
                return builder.CreateSub(left, right, "subtmp");
            case '*':
                return builder.CreateMul(left, right, "multmp");
            case '/':
                return builder.CreateSDiv(left, right, "divtmp");
            case '%':
                return builder.CreateSRem(left, right, "modtmp");
            case '>':
                return builder.CreateICmpSGT(left, right, "cmptmp");
            case '<':
                return builder.CreateICmpSLT(left, right, "cmptmp");
            case 'G': // >=
                return builder.CreateICmpSGE(left, right, "cmptmp");
            case 'L': // <=
                return builder.CreateICmpSLE(left, right, "cmptmp");
            case 'E': // == (equality)
                return builder.CreateICmpEQ(left, right, "cmptmp");
            case 'N': // != (not equal)
                return builder.CreateICmpNE(left, right, "cmptmp");
            default:
                throw std::runtime_error("Unknown binary operator");
        }
    }
    
    if (auto* callExpr = dynamic_cast<CallExprAST*>(expr)) {
        // Initialize standard library if calling a standard library function
        if (callExpr->callee == "print") {
            initStandardLibrary();
        }
        
        // Look up the function in the module
        llvm::Function* calleeF = module->getFunction(callExpr->callee);
        if (!calleeF) {
            throw std::runtime_error("Unknown function referenced: " + callExpr->callee);
        }
        
        // Check argument count
        if (calleeF->arg_size() != callExpr->args.size()) {
            throw std::runtime_error("Incorrect number of arguments passed to function: " + callExpr->callee);
        }
        
        // Generate code for arguments
        std::vector<llvm::Value*> argsV;
        for (auto& arg : callExpr->args) {
            llvm::Value* argVal = generateExpr(arg.get());
            if (!argVal) {
                throw std::runtime_error("Failed to generate function argument");
            }
            argsV.push_back(argVal);
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
            // Array type: [size x elementType]
            llvm::Type* elementType;
            if (!varDecl->type.empty()) {
                elementType = getTypeFromString(context, varDecl->type);
            } else {
                throw std::runtime_error("Array declaration requires explicit element type at line " + 
                                       std::to_string(varDecl->line));
            }
            allocaType = llvm::ArrayType::get(elementType, varDecl->arraySize);
        } else if (!varDecl->type.empty()) {
            // Use explicit type annotation
            allocaType = getTypeFromString(context, varDecl->type);
        } else {
            // Infer from initializer (currently defaults to i32)
            allocaType = llvm::Type::getInt32Ty(context);
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
            // Array type: [size x elementType]
            llvm::Type* elementType;
            if (!letDecl->type.empty()) {
                elementType = getTypeFromString(context, letDecl->type);
            } else {
                throw std::runtime_error("Array declaration requires explicit element type at line " + 
                                       std::to_string(letDecl->line));
            }
            allocaType = llvm::ArrayType::get(elementType, letDecl->arraySize);
        } else if (!letDecl->type.empty()) {
            // Use explicit type annotation
            allocaType = getTypeFromString(context, letDecl->type);
        } else {
            // Infer from initializer (currently defaults to i32)
            allocaType = llvm::Type::getInt32Ty(context);
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
        
        // Get element pointer: GEP array, 0, index
        llvm::Value* zero = llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0);
        llvm::Value* elementPtr = builder.CreateInBoundsGEP(
            alloca->getAllocatedType(),
            alloca,
            {zero, indexVal},
            "arrayidx"
        );
        
        builder.CreateStore(val, elementPtr);
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
        llvm::Type* paramType = getTypeFromString(context, func->parameters[idx].type);
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
    
    // Generate function body
    for (auto& stmt : func->body) {
        generateStmt(stmt.get(), function);
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

void CodeGenerator::generate(ProgramAST* program) {
    // First pass: Create all function declarations (including namespace functions)
    for (auto& func : program->functions) {
        // Get return type
        llvm::Type* returnType = getTypeFromString(context, func->returnType);
        
        // Create parameter types
        std::vector<llvm::Type*> paramTypes;
        for (const auto& param : func->parameters) {
            paramTypes.push_back(getTypeFromString(context, param.type));
        }
        
        // Create function type
        llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, paramTypes, false);
        
        // Create function
        llvm::Function::Create(funcType, llvm::Function::ExternalLinkage,
                             func->name, module.get());
    }
    
    // Create namespace functions with qualified names
    for (auto& ns : program->namespaces) {
        for (auto& func : ns->functions) {
            // Get return type
            llvm::Type* returnType = getTypeFromString(context, func->returnType);
            
            // Create parameter types
            std::vector<llvm::Type*> paramTypes;
            for (const auto& param : func->parameters) {
                paramTypes.push_back(getTypeFromString(context, param.type));
            }
            
            // Create function type
            llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, paramTypes, false);
            
            // Create function with qualified name (namespace::function)
            std::string qualifiedName = ns->name + "::" + func->name;
            llvm::Function::Create(funcType, llvm::Function::ExternalLinkage,
                                 qualifiedName, module.get());
        }
    }
    
    // Second pass: Generate function bodies
    for (auto& func : program->functions) {
        generateFunction(func.get());
    }
    
    // Generate namespace function bodies
    for (auto& ns : program->namespaces) {
        for (auto& func : ns->functions) {
            generateFunction(func.get(), ns->name);
        }
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
    
    std::cout << "Object file written to " << filename << std::endl;
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
    
    std::cout << "Object file generated." << std::endl;
    
    // Use LLD as a library (in-process linking)
    std::cout << "Linking with LLD library (in-process)..." << std::endl;
    
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
    
    // Add library paths
    std::string vsPath = "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\VC\\Tools\\MSVC\\14.44.35207";
    std::string libPath = "/LIBPATH:" + vsPath + "\\lib\\x64";
    std::string sdkLibPath = "/LIBPATH:C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\um\\x64";
    std::string ucrtLibPath = "/LIBPATH:C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\ucrt\\x64";
    
    lldArgs.push_back(libPath.c_str());
    lldArgs.push_back(sdkLibPath.c_str());
    lldArgs.push_back(ucrtLibPath.c_str());
    
    // Output file
    std::string outArg = "/OUT:" + exeFile;
    lldArgs.push_back(outArg.c_str());
    
    // Input object file
    lldArgs.push_back(tempObjFile.c_str());
    
    // Default libraries
    lldArgs.push_back("/DEFAULTLIB:libcmt.lib");
    lldArgs.push_back("/DEFAULTLIB:oldnames.lib");
    lldArgs.push_back("/DEFAULTLIB:kernel32.lib");
    lldArgs.push_back("/DEFAULTLIB:user32.lib");
    
    // Call LLD driver directly (in-process)
    bool success = lld::coff::link(lldArgs, llvm::outs(), llvm::errs(), false, false);
    
    // Clean up temporary object file
    llvm::sys::fs::remove(tempObjFile);
    
    if (!success) {
        throw std::runtime_error("LLD linking failed");
    }
    
    std::cout << "Executable written to " << exeFile << std::endl;
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
