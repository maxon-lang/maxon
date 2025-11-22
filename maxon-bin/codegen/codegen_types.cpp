// Type conversion utilities for LLVM code generation
#include "../codegen.h"
#include "../lexer.h"
#include <llvm/IR/DerivedTypes.h>
#include <stdexcept>

// Helper function to convert Maxon type string to LLVM type
llvm::Type* getTypeFromString(llvm::LLVMContext& context, const std::string& typeStr,
                                     const std::map<std::string, llvm::StructType*>* structTypes) {
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
llvm::Type* getParamTypeFromString(llvm::LLVMContext& context, const std::string& typeStr,
                                          const std::map<std::string, llvm::StructType*>* structTypes) {
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
bool isArrayParam(const std::string& typeStr) {
    // Only unsized arrays ([]type) need hidden length parameters
    // Fixed-size arrays ([N]type) don't need them
    return typeStr.size() >= 2 && typeStr[0] == '[' && typeStr[1] == ']';
}
