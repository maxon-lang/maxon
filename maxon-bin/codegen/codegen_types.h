// Type conversion utilities for LLVM code generation
#ifndef CODEGEN_TYPES_H
#define CODEGEN_TYPES_H

#include <llvm/IR/Type.h>
#include <llvm/IR/LLVMContext.h>
#include <llvm/IR/DerivedTypes.h>
#include <map>
#include <string>

// Helper function to convert Maxon type string to LLVM type
llvm::Type* getTypeFromString(llvm::LLVMContext& context, const std::string& typeStr,
                              const std::map<std::string, llvm::StructType*>* structTypes = nullptr);

// Convert a type to the form used for function parameters
// Arrays are passed as pointers, Structs are passed as pointers (by reference)
llvm::Type* getParamTypeFromString(llvm::LLVMContext& context, const std::string& typeStr,
                                  const std::map<std::string, llvm::StructType*>* structTypes = nullptr);

// Check if a parameter type is an array
bool isArrayParam(const std::string& typeStr);

#endif // CODEGEN_TYPES_H
