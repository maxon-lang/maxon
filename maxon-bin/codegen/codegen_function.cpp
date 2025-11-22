#include "../codegen.h"
#include "codegen_types.h"

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
