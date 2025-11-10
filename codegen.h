#ifndef CODEGEN_H
#define CODEGEN_H

#include "ast.h"
#include <llvm/IR/LLVMContext.h>
#include <llvm/IR/IRBuilder.h>
#include <llvm/IR/Module.h>
#include <llvm/IR/Value.h>
#include <map>
#include <string>

class CodeGenerator {
private:
    llvm::LLVMContext context;
    llvm::IRBuilder<> builder;
    std::unique_ptr<llvm::Module> module;
    std::map<std::string, llvm::AllocaInst*> namedValues;
    
    llvm::Value* generateExpr(ExprAST* expr);
    void generateStmt(StmtAST* stmt, llvm::Function* function);
    void generateFunction(FunctionAST* func);
    
    llvm::AllocaInst* createEntryBlockAlloca(llvm::Function* function,
                                              const std::string& varName);
    
public:
    CodeGenerator(const std::string& moduleName);
    void generate(ProgramAST* program);
    void printIR();
    void writeObjectFile(const std::string& filename);
    void linkExecutable(const std::string& objectFile, const std::string& exeFile);
    llvm::Module* getModule() { return module.get(); }
};

#endif // CODEGEN_H
