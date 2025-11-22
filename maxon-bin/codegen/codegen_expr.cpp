// Expression code generation for LLVM
#include "../codegen.h"
#include "codegen_types.h"
#include "../lexer.h"
#include <llvm/IR/Constants.h>
#include <stdexcept>

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
