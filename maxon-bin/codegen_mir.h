#ifndef CODEGEN_MIR_H
#define CODEGEN_MIR_H

#include "ast.h"
#include "logger.h"
#include "mir/mir.h"
#include "mir/mir_builder.h"
#include "safe_ffi.h"
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

	// Counter for unique string literal names
	int stringLiteralCounter = 0;

	// Variable tracking: name -> alloca value
	std::map<std::string, mir::MIRValue *> namedValues;

	// Type tracking for variables (needed for struct/array access)
	std::map<std::string, std::string> variableTypes;

	// Function ID to MIR function mapping (for fast lookups during codegen)
	std::map<size_t, mir::MIRFunction *> functionIdToMIR;

	// Track which variables are struct parameters (passed by pointer)
	std::set<std::string> structParameters;

	// Track which arrays are stack-allocated (direct access, no pointer indirection)
	std::set<std::string> stackAllocatedArrays;

	// Struct type definitions
	std::map<std::string, mir::MIRType *> structTypes;
	std::map<std::string, std::vector<std::pair<std::string, std::string>>> structFields;
	std::map<std::string, std::vector<std::string>> structConformsTo; // Track interface conformance

	// Safe FFI: Track extern functions for subprocess isolation
	struct ExternFuncInfo {
		uint32_t id;							  // Function ID in extern table
		std::string name;						  // Function name (possibly with namespace prefix)
		std::string exportName;					  // Raw function name for DLL lookup (no namespace)
		std::string dllName;					  // DLL/lib name (without extension)
		std::vector<safeffi::TypeTag> paramTypes; // Parameter types
		safeffi::TypeTag returnType;			  // Return type
		bool isStaticLib;						  // true if linking against static library
		std::string libPath;					  // Full path to static library (if isStaticLib)
	};
	std::map<std::string, ExternFuncInfo> externFunctions;
	uint32_t nextExternId = 0;
	bool hasExternCalls = false; // Track if any extern calls exist (DLLs only)

	// Static library paths for linking
	std::set<std::string> staticLibPaths;

	// Debug information
	bool generateDebugInfo;
	int verboseLevel; // 0 = silent, 1 = progress, 2 = detailed, 3 = trace
	std::string sourceFileName;
	Logger logger_; // Internal logger instance

	// Logging helpers
	void logProgress(const std::string &msg);
	void logDetail(const std::string &msg);
	void logTrace(const std::string &msg);

	// Loop context for break/continue
	struct LoopContext {
		std::string label;
		mir::MIRBasicBlock *condBlock;	// For continue
		mir::MIRBasicBlock *afterBlock; // For break
	};
	std::vector<LoopContext> loopStack;

	// Scope tracking for automatic array/string cleanup
	struct ScopeInfo {
		std::vector<std::pair<std::string, mir::MIRValue *>> heapAllocatedArrays;
		std::vector<std::pair<std::string, mir::MIRValue *>> heapAllocatedStrings; // Data pointer for heap strings
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
	std::string getMaxonTypeFromMIRType(mir::MIRType *type);
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

	// Array intrinsic generation (push, pop)
	mir::MIRValue *generateArrayIntrinsic(CallExprAST *callExpr);

	// String literal generation
	mir::MIRValue *generateStringLiteral(StringLiteralExprAST *strExpr);
	mir::MIRValue *generateStringLiteralAsSlice(StringLiteralExprAST *strExpr);

	// Safe FFI generation
	void registerExternFunction(FunctionAST *func);
	safeffi::TypeTag getTypeTagFromString(const std::string &typeStr);
	mir::MIRValue *generateSafeFFICall(const std::string &funcName,
									   const std::vector<mir::MIRValue *> &args,
									   mir::MIRType *returnType);
	void generateFFIGlobals();		// Generate FFI state globals
	void generateFFIInitFunction(); // Generate __ffi_init function
	void generateFFICallFunction(); // Generate __ffi_call wrapper
	void generateFFICleanup();		// Generate cleanup at program exit
	void generateFFIWorkerMain();	// Generate __ffi_worker_main function
	void generateFFIDispatch();		// Generate __ffi_dispatch function
	bool isFFIWorkerMode();			// Check if running as FFI worker

	// Minimal CRT entry point
	void createMinimalEntryPoint();

	// Platform-specific executable generation
#ifdef _WIN32
	void writeWindowsExecutable(
		const std::string &exeFile,
		std::vector<uint8_t> &code,
		const std::vector<uint8_t> &data,
		const std::unordered_map<std::string, size_t> &funcOffsets,
		const std::vector<std::pair<size_t, std::string>> &importRelocs,
		const std::vector<std::pair<size_t, size_t>> &dataRelocs);
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
	void generate(ProgramAST *program, bool needsEntryPoint = true,
				  const std::map<std::string, size_t> *functionIndices = nullptr);

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
