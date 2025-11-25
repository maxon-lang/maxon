#ifndef CODEGEN_MIR_H
#define CODEGEN_MIR_H

#include "ast.h"
#include "mir/mir.h"
#include "mir/mir_builder.h"
#include <map>
#include <memory>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

/**
 * MIR Code Generator
 *
 * This class generates Maxon IR (MIR) from the AST, replacing the LLVM-based
 * code generator. MIR is then lowered to x86-64 machine code using the custom
 * backend in maxon-bin/backend/.
 */
class MIRCodeGenerator {
  private:
	std::unique_ptr<mir::MIRModule> module;
	std::unique_ptr<mir::MIRBuilder> builder;

	// Variable tracking: name -> alloca value
	std::map<std::string, mir::MIRValue *> namedValues;

	// Type tracking for variables (needed for struct/array access)
	std::map<std::string, std::string> variableTypes;

	// Track which variables are struct parameters (passed by pointer)
	std::set<std::string> structParameters;

	// Struct type definitions
	std::map<std::string, mir::MIRType *> structTypes;
	std::map<std::string, std::vector<std::pair<std::string, std::string>>> structFields;

	// Debug information
	bool generateDebugInfo;
	int verboseLevel; // 0 = silent, 1 = verbose, 2 = debug
	std::string sourceFileName;

	// Loop context for break/continue
	struct LoopContext {
		std::string label;
		mir::MIRBasicBlock *condBlock;	// For continue
		mir::MIRBasicBlock *afterBlock; // For break
	};
	std::vector<LoopContext> loopStack;

	// Scope tracking for automatic array cleanup
	struct ScopeInfo {
		std::vector<std::pair<std::string, mir::MIRValue *>> heapAllocatedArrays;
	};
	std::vector<ScopeInfo> scopeStack;

	void pushScope();
	void popScope(mir::MIRFunction *function);
	void generateScopeCleanup(mir::MIRFunction *function);

	// Code generation methods
	mir::MIRValue *generateExpr(ExprAST *expr);
	void generateStmt(StmtAST *stmt, mir::MIRFunction *function);
	void generateFunction(FunctionAST *func, const std::string &namespaceName = "");

	// Type conversion helpers
	mir::MIRType *getTypeFromString(const std::string &typeStr);
	mir::MIRType *getParamTypeFromString(const std::string &typeStr);
	bool isArrayParam(const std::string &typeStr);
	bool isStructParameter(const std::string &varName);

	// Alloca creation helper
	mir::MIRValue *createEntryBlockAlloca(mir::MIRFunction *function,
										  const std::string &varName,
										  mir::MIRType *type = nullptr);

	// Runtime function helpers
	mir::MIRFunction *getOrDeclareFunction(const std::string &name,
										   mir::MIRType *returnType,
										   const std::vector<mir::MIRType *> &paramTypes);

	// Standard library initialization
	void initStandardLibrary();
	void initHeapManagement();

	// Math intrinsic generation
	mir::MIRValue *generateMathIntrinsic(CallExprAST *callExpr);

	// Minimal CRT entry point
	void createMinimalEntryPoint();

	// Platform-specific executable generation
#ifdef _WIN32
	void writeWindowsExecutable(
		const std::string &exeFile,
		std::vector<uint8_t> &code,
		const std::vector<uint8_t> &data,
		const std::unordered_map<std::string, size_t> &funcOffsets,
		const std::vector<std::pair<size_t, std::string>> &importRelocs);
#else
	void writeLinuxExecutable(
		const std::string &exeFile,
		std::vector<uint8_t> &code,
		const std::vector<uint8_t> &data,
		const std::unordered_map<std::string, size_t> &funcOffsets,
		const std::vector<std::pair<size_t, std::string>> &importRelocs);
#endif

  public:
	MIRCodeGenerator(const std::string &moduleName, bool debugInfo = false, int verboseLevel = 0);
	~MIRCodeGenerator();

	// Main generation entry point
	void generate(ProgramAST *program, bool needsEntryPoint = true);

	// Optimization
	void optimize();
	void runDeadCodeElimination();

	// Output methods
	void printIR();
	void writeIRToFile(const std::string &filename);
	void writeObjectFile(const std::string &filename);
	void writeExecutable(const std::string &exeFile);

	// Access to underlying module (for testing)
	mir::MIRModule *getModule() { return module.get(); }
};

#endif // CODEGEN_MIR_H
