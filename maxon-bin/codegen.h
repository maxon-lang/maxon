#ifndef CODEGEN_H
#define CODEGEN_H

#include "ast.h"
#include <llvm/IR/LLVMContext.h>
#include <llvm/IR/IRBuilder.h>
#include <llvm/IR/Module.h>
#include <llvm/IR/Value.h>
#include <llvm/IR/DIBuilder.h>
#include <map>
#include <string>

class CodeGenerator {
private:
    llvm::LLVMContext context;
    llvm::IRBuilder<> builder;
    std::unique_ptr<llvm::Module> module;
    std::map<std::string, llvm::AllocaInst*> namedValues;
    
    // Debug information
    bool generateDebugInfo;
    bool verbose;
    std::unique_ptr<llvm::DIBuilder> debugBuilder;
    llvm::DICompileUnit* debugCompileUnit;
    llvm::DIFile* debugFile;
    std::string sourceFileName;
    
    // Current debug context
    std::vector<llvm::DIScope*> debugScopeStack;
    
    // Loop context for break/continue
    llvm::BasicBlock* currentLoopCond = nullptr;
    llvm::BasicBlock* currentLoopAfter = nullptr;
    
    llvm::Value* generateExpr(ExprAST* expr);
    void generateStmt(StmtAST* stmt, llvm::Function* function);
    void generateFunction(FunctionAST* func, const std::string& namespaceName = "");
    
    llvm::Value* generateMathIntrinsic(CallExprAST* callExpr);
    
    llvm::AllocaInst* createEntryBlockAlloca(llvm::Function* function,
                                              const std::string& varName,
                                              llvm::Type* type = nullptr);
    
    // Debug info helpers
    void initDebugInfo(const std::string& filename);
    void finalizeDebugInfo();
    void emitLocation(int line, int column);
    llvm::DISubroutineType* createFunctionDebugType(FunctionAST* func);
    
    // Standard library
    void initStandardLibrary();
    
    // Minimal CRT entry point
    void createMinimalEntryPoint();
    
public:
    CodeGenerator(const std::string& moduleName, bool debugInfo = false, bool verbose = false);
    void generate(ProgramAST* program, bool needsEntryPoint = true);
    void optimize();
    void printIR();
    void writeIRToFile(const std::string& filename);
    void writeObjectFile(const std::string& filename);
    void writeExecutable(const std::string& exeFile);
    llvm::Module* getModule() { return module.get(); }
};

#endif // CODEGEN_H
