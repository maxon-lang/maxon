#include "../codegen.h"
#include "codegen_types.h"
#include "../lexer.h"

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
    
    if (auto* forStmt = dynamic_cast<ForStmtAST*>(stmt)) {
        // Desugar for loop to iterator-based while loop
        // for i in <iterable> 'label'
        //     <body>
        // end 'label'
        //
        // Becomes:
        // var __iter = <iterable>
        // while hasNext(__iter) = 1 'label'
        //     let i = getCurrent(__iter)
        //     <body>
        //     __iter = next(__iter)
        // end 'label'
        
        // Emit debug location
        if (generateDebugInfo) {
            emitLocation(forStmt->line, forStmt->column);
        }
        
        // Generate iterator initialization
        llvm::Value* iteratorVal = generateExpr(forStmt->iterable.get());
        if (!iteratorVal) {
            throw std::runtime_error("Failed to generate for-loop iterable expression");
        }
        
        // Create alloca for iterator variable
        llvm::Type* iteratorType = iteratorVal->getType();
        llvm::AllocaInst* iteratorAlloca = builder.CreateAlloca(iteratorType, nullptr, "__iter");
        builder.CreateStore(iteratorVal, iteratorAlloca);
        
        // Create basic blocks for loop
        llvm::BasicBlock* condBB = llvm::BasicBlock::Create(context, "forcond", function);
        llvm::BasicBlock* loopBB = llvm::BasicBlock::Create(context, "forloop");
        llvm::BasicBlock* incrementBB = llvm::BasicBlock::Create(context, "forincrement");
        llvm::BasicBlock* afterBB = llvm::BasicBlock::Create(context, "afterfor");
        
        // Save previous loop context
        llvm::BasicBlock* prevLoopCond = currentLoopCond;
        llvm::BasicBlock* prevLoopAfter = currentLoopAfter;
        
        // Set current loop context - continue should jump to increment block
        currentLoopCond = incrementBB;
        currentLoopAfter = afterBB;
        
        // Jump to condition block
        builder.CreateBr(condBB);
        
        // Generate condition block: hasNext(__iter) = 1
        builder.SetInsertPoint(condBB);
        
        // Pass iterator by pointer (structs are passed by reference in Maxon)
        llvm::Value* iterPtr = iteratorAlloca;
        
        // Call hasNext(iterator) from stdlib - use suffix matching like regular calls
        llvm::Function* hasNextFunc = module->getFunction("hasNext");
        if (!hasNextFunc) {
            // Try suffix matching for namespaced function
            std::string searchSuffix = ".hasNext";
            for (auto& func : module->functions()) {
                std::string funcName = func.getName().str();
                if (funcName.size() > searchSuffix.size() &&
                    funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
                    hasNextFunc = &func;
                    break;
                }
            }
        }
        if (!hasNextFunc) {
            throw std::runtime_error("For-loop requires 'hasNext' function from stdlib (iter.hasNext)");
        }
        
        llvm::Value* hasNextResult = builder.CreateCall(hasNextFunc, {iterPtr}, "hasNext.result");
        
        // Check if hasNext returned 1 (true)
        llvm::Value* condVal = builder.CreateICmpEQ(
            hasNextResult, 
            llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)), 
            "forcond");
        
        builder.CreateCondBr(condVal, loopBB, afterBB);
        
        // Generate loop body
        function->insert(function->end(), loopBB);
        builder.SetInsertPoint(loopBB);
        
        // Get current value: let <loopVar> = getCurrent(__iter)
        llvm::Function* getCurrentFunc = module->getFunction("getCurrent");
        if (!getCurrentFunc) {
            // Try suffix matching for namespaced function
            std::string searchSuffix = ".getCurrent";
            for (auto& func : module->functions()) {
                std::string funcName = func.getName().str();
                if (funcName.size() > searchSuffix.size() &&
                    funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
                    getCurrentFunc = &func;
                    break;
                }
            }
        }
        if (!getCurrentFunc) {
            throw std::runtime_error("For-loop requires 'getCurrent' function from stdlib (iter.getCurrent)");
        }
        llvm::Value* currentVal = builder.CreateCall(getCurrentFunc, {iterPtr}, forStmt->loopVar);
        
        // Create alloca for loop variable
        llvm::AllocaInst* loopVarAlloca = builder.CreateAlloca(
            llvm::Type::getInt32Ty(context), nullptr, forStmt->loopVar);
        builder.CreateStore(currentVal, loopVarAlloca);
        
        // Register loop variable in scope
        namedValues[forStmt->loopVar] = loopVarAlloca;
        
        // Generate body statements
        for (auto& s : forStmt->body) {
            generateStmt(s.get(), function);
        }
        
        // After body, branch to increment block (if no terminator already)
        if (!builder.GetInsertBlock()->getTerminator()) {
            builder.CreateBr(incrementBB);
        }
        
        // Generate increment block: __iter = next(__iter)
        function->insert(function->end(), incrementBB);
        builder.SetInsertPoint(incrementBB);
        
        llvm::Function* nextFunc = module->getFunction("next");
        if (!nextFunc) {
            // Try suffix matching for namespaced function
            std::string searchSuffix = ".next";
            for (auto& func : module->functions()) {
                std::string funcName = func.getName().str();
                if (funcName.size() > searchSuffix.size() &&
                    funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
                    nextFunc = &func;
                    break;
                }
            }
        }
        if (!nextFunc) {
            throw std::runtime_error("For-loop requires 'next' function from stdlib (iter.next)");
        }
        
        // next() returns a new Iterator struct by value, store it back
        llvm::Value* nextResult = builder.CreateCall(nextFunc, {iterPtr}, "__iter.next");
        builder.CreateStore(nextResult, iteratorAlloca);
        
        // Jump back to condition
        builder.CreateBr(condBB);
        
        // Clean up loop variable from scope
        namedValues.erase(forStmt->loopVar);
        
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
