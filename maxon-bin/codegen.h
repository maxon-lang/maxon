#ifndef CODEGEN_H
#define CODEGEN_H

#include "ast.h"
#include <llvm/IR/DIBuilder.h>
#include <llvm/IR/IRBuilder.h>
#include <llvm/IR/LLVMContext.h>
#include <llvm/IR/Module.h>
#include <llvm/IR/Value.h>
#include <map>
#include <string>

class CodeGenerator {
  private:
	llvm::LLVMContext context;
	llvm::IRBuilder<> builder;
	std::unique_ptr<llvm::Module> module;
	std::map<std::string, llvm::AllocaInst *> namedValues;
	std::map<std::string, std::string> variableTypes;									  // Track Maxon type strings for variables
	std::map<std::string, llvm::StructType *> structTypes;								  // LLVM struct types
	std::map<std::string, std::vector<std::pair<std::string, std::string>>> structFields; // Struct field names and types

	// Debug information
	bool generateDebugInfo;
	int verboseLevel; // 0 = silent, 1 = verbose, 2 = debug
	bool enableProfiling;
	std::unique_ptr<llvm::DIBuilder> debugBuilder;
	llvm::DICompileUnit *debugCompileUnit;
	llvm::DIFile *debugFile;
	std::string sourceFileName;

	// Current debug context
	std::vector<llvm::DIScope *> debugScopeStack;

	// Loop context for break/continue
	llvm::BasicBlock *currentLoopCond = nullptr;
	llvm::BasicBlock *currentLoopAfter = nullptr;

	// Scope tracking for automatic array cleanup
	struct ScopeInfo {
		std::vector<std::pair<std::string, llvm::AllocaInst *>> heapAllocatedArrays;
	};
	std::vector<ScopeInfo> scopeStack;

	void pushScope();
	void popScope(llvm::Function *function);
	void generateScopeCleanup(llvm::Function *function);

	llvm::Value *generateExpr(ExprAST *expr);
	void generateStmt(StmtAST *stmt, llvm::Function *function);
	void generateFunction(FunctionAST *func, const std::string &namespaceName = "");

	llvm::Function *getRuntimeFunction(const std::string &name, llvm::Module *module, llvm::LLVMContext &context);
	llvm::Value *generateMathIntrinsic(CallExprAST *callExpr);

	llvm::AllocaInst *createEntryBlockAlloca(llvm::Function *function,
											 const std::string &varName,
											 llvm::Type *type = nullptr);

	// Debug info helpers
	void initDebugInfo(const std::string &filename);
	void finalizeDebugInfo();
	void emitLocation(int line, int column);
	llvm::DISubroutineType *createFunctionDebugType(FunctionAST *func);

	// Standard library
	void initStandardLibrary();
	void initHeapManagement();

	// Runtime library helpers
	llvm::Function *getOrDeclareMemset();

	// Profiling support
	llvm::GlobalVariable *bbCountGlobal = nullptr;
	llvm::Function *printBBCountFunc = nullptr;
	void initProfiling();
	void injectInstrCounter();
	void injectProfileOutput(llvm::Function *mainFunc);

	// Minimal CRT entry point
	void createMinimalEntryPoint();

  public:
	CodeGenerator(const std::string &moduleName, bool debugInfo = false, int verboseLevel = 0, bool profile = false);
	void generate(ProgramAST *program, bool needsEntryPoint = true);
	void optimize();
	void runDeadCodeElimination(); // Always run to remove unused internal functions
	void printIR();
	void writeIRToFile(const std::string &filename);
	void writeObjectFile(const std::string &filename);
	void writeExecutable(const std::string &exeFile, llvm::raw_ostream *errorStream = nullptr);
	llvm::Module *getModule() { return module.get(); }
};

#endif // CODEGEN_H
