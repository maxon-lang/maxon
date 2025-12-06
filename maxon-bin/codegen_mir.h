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

// Forward declaration for stats collection
class CompilerStats;

/**
 * MIR Code Generator
 *
 * This class generates Maxon IR (MIR) from the AST, replacing the LLVM-based
 * code generator. MIR is then lowered to x86-64 machine code using the custom
 * backend in maxon-bin/backend/.
 */
// Forward declaration for friend class
class IntrinsicCodegenRegistry;

class MIRCodeGenerator {
	friend class IntrinsicCodegenRegistry;

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

	// Function return types from semantic analyzer (name -> return type)
	std::map<std::string, std::string> functionReturnTypes;

	// Function parameter types from semantic analyzer (function name -> vector of param types)
	std::map<std::string, std::vector<std::string>> functionParameterTypes;

	// Track which variables are struct parameters (passed by pointer)
	std::set<std::string> structParameters;

	// Track which arrays are stack-allocated (direct access, no pointer indirection)
	std::set<std::string> stackAllocatedArrays;

	// Struct type definitions
	std::map<std::string, mir::MIRType *> structTypes;
	std::map<std::string, std::vector<std::pair<std::string, std::string>>> structFields;
	std::map<std::string, std::map<std::string, ExprAST *>> structFieldDefaults;	 // Field default value expressions
	std::map<std::string, std::vector<std::string>> structConformsTo;				 // Track interface conformance
	std::map<std::string, std::map<std::string, std::string>> structTypeAssignments; // Associated type assignments
	std::map<std::string, StructDefAST *> structDefinitions;						 // AST definitions for generic instantiation

	// Generic struct instantiation tracking
	std::set<std::string> instantiatedGenericStructs; // Track which generic structs have been instantiated

	// Enum type definitions
	struct EnumCodegenInfo {
		std::string name;
		std::string rawValueType;																 // "int", "string", or "" for simple enums
		bool hasAssociatedValues;																 // True if any case has associated values
		std::vector<std::string> caseNames;														 // Ordered list of case names
		std::map<std::string, int> caseTags;													 // Case name -> tag value
		std::map<std::string, std::vector<std::pair<std::string, std::string>>> caseAssocValues; // Case -> [(name, type)]
		std::map<std::string, int64_t> caseRawIntValues;										 // Case -> raw int value
		std::map<std::string, std::string> caseRawStrValues;									 // Case -> raw string value
		mir::MIRType *mirType = nullptr;														 // The MIR type for this enum
	};
	std::map<std::string, EnumCodegenInfo> enumTypes;

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
	bool trackAllocs; // Log memory allocations for debugging
	int verboseLevel; // 0 = silent, 1 = progress, 2 = detailed, 3 = trace
	std::string sourceFileName;
	Logger logger_; // Internal logger instance

	// Current method context for implicit self field access
	std::string currentReceiverType; // Set when generating a method, empty for free functions

	// Current type bindings for generic method instantiation
	std::map<std::string, std::string> currentTypeBindings; // e.g., {"KeyType": "string", "ValueType": "int"}

	// Logging helpers
	void logProgress(const std::string &msg);
	void logDetail(const std::string &msg);
	void logTrace(const std::string &msg);

	// Error reporting helper - throws runtime_error with location info
	[[noreturn]] void reportError(const std::string &message, int line, int column);

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
		std::vector<std::pair<std::string, mir::MIRValue *>> substringAllocas;	   // Substring struct allocas (need parent release)
		std::vector<std::pair<std::string, mir::MIRValue *>> cstringAllocas;	   // Cstring struct allocas (need managed release)
	};
	std::vector<ScopeInfo> scopeStack;

	void pushScope();
	void popScope(mir::MIRFunction *function);
	void generateScopeCleanup(mir::MIRFunction *function);

	// Generic struct instantiation
	// Creates a specialized version of a generic struct template with concrete types
	// Returns the specialized struct type name (e.g., "map<string,int>")
	std::string instantiateGenericStruct(const std::string &templateName,
										 const std::map<std::string, std::string> &typeBindings);

	// Code generation methods
	mir::MIRValue *generateExpr(ExprAST *expr);
	void generateStmt(StmtAST *stmt, mir::MIRFunction *function);
	void generateFunction(FunctionAST *func, const std::string &namespaceName = "");

	// Statement generation helpers (in separate files)
	void generateVarDecl(VarDeclStmtAST *varDecl, mir::MIRFunction *function);
	void generateLetDecl(LetDeclStmtAST *letDecl, mir::MIRFunction *function);
	void generateAssign(AssignStmtAST *assign, mir::MIRFunction *function);
	void generateArrayAssign(ArrayAssignStmtAST *arrayAssign, mir::MIRFunction *function);
	void generateArrayMemberAssign(ArrayMemberAssignStmtAST *arrayMemberAssign, mir::MIRFunction *function);
	void generateMemberAssign(MemberAssignStmtAST *memberAssign, mir::MIRFunction *function);
	void generateMemberArrayAssign(MemberArrayAssignStmtAST *memberArrayAssign, mir::MIRFunction *function);
	void generateIf(IfStmtAST *ifStmt, mir::MIRFunction *function);
	void generateWhile(WhileStmtAST *whileStmt, mir::MIRFunction *function);
	void generateFor(ForStmtAST *forStmt, mir::MIRFunction *function);
	void generateBreak(BreakStmtAST *breakStmt, mir::MIRFunction *function);
	void generateContinue(ContinueStmtAST *continueStmt, mir::MIRFunction *function);
	void generateMatch(MatchStmtAST *matchStmt, mir::MIRFunction *function);
	mir::MIRValue *generateMatchExpr(MatchExprAST *matchExpr);
	void generateFunctionWithTypeBindings(FunctionAST *func, const std::string &namespaceName,
										  const std::map<std::string, std::string> &typeBindings,
										  const std::string &specializedReceiverType);

	// Type conversion helpers
	mir::MIRType *getTypeFromString(const std::string &typeStr);
	mir::MIRType *getTypeFromStringNoMark(const std::string &typeStr); // For struct field definitions
	mir::MIRType *getParamTypeFromString(const std::string &typeStr);
	std::string getMaxonTypeFromMIRType(mir::MIRType *type);
	std::string inferExprType(ExprAST *expr); // For struct field type inference
	void markFieldTypesUsed(mir::MIRType *type);
	bool isArrayParam(const std::string &typeStr);
	bool isStructParameter(const std::string &varName);
	bool isStringConcatExpr(BinaryExprAST *binExpr);

	// Alloca creation helper
	mir::MIRValue *createEntryBlockAlloca(mir::MIRFunction *function,
										  const std::string &varName,
										  mir::MIRType *type = nullptr);

	// Runtime function helpers
	mir::MIRFunction *getOrDeclareFunction(const std::string &name,
										   mir::MIRType *returnType,
										   const std::vector<mir::MIRType *> &paramTypes);

	// Heap management initialization
	void initHeapManagement();

	// Math intrinsic generation
	mir::MIRValue *generateMathIntrinsic(CallExprAST *callExpr);

	// Array intrinsic generation (push, pop)
	mir::MIRValue *generateArrayIntrinsic(CallExprAST *callExpr);

	// Map method generation (insert, get, contains, remove, count, capacity)
	bool isMapMethodCall(const std::string &callee);
	mir::MIRValue *generateMapMethod(CallExprAST *callExpr);
	mir::MIRValue *generateMapContains(mir::MIRValue *mapAlloca, mir::MIRValue *key,
									   mir::MIRType *keyType, mir::MIRType *valueType,
									   const std::string &keyTypeStr, mir::MIRType *mapStructType);
	mir::MIRValue *generateMapGet(mir::MIRValue *mapAlloca, mir::MIRValue *key,
								  mir::MIRType *keyType, mir::MIRType *valueType,
								  const std::string &keyTypeStr, mir::MIRType *mapStructType);
	mir::MIRValue *generateMapInsert(mir::MIRValue *mapAlloca, mir::MIRValue *key,
									 mir::MIRValue *value, mir::MIRType *keyType,
									 mir::MIRType *valueType, const std::string &keyTypeStr,
									 const std::string &valueTypeStr, mir::MIRType *mapStructType);
	mir::MIRValue *generateMapRemove(mir::MIRValue *mapAlloca, mir::MIRValue *key,
									 mir::MIRType *keyType, mir::MIRType *valueType,
									 const std::string &keyTypeStr, mir::MIRType *mapStructType);
	mir::MIRValue *generateHashForKey(mir::MIRValue *key, const std::string &keyTypeStr);

	// String intrinsic generation (__string_* functions)
	mir::MIRValue *generateStringIntrinsic(CallExprAST *callExpr);
	bool isStringIntrinsic(const std::string &name);

	// Substring intrinsic generation (__substring_* functions)
	mir::MIRValue *generateSubstringIntrinsic(CallExprAST *callExpr);
	bool isSubstringIntrinsic(const std::string &name);

	// Cstring intrinsic generation (__cstring_* functions)
	mir::MIRValue *generateCstringIntrinsic(CallExprAST *callExpr);
	bool isCstringIntrinsic(const std::string &name);

	// Individual intrinsic codegen methods (registered in IntrinsicCodegenRegistry)
	// String intrinsics
	mir::MIRValue *intrinsic_string_len(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_byte_at(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_slice(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_concat(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_make_unique(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_set_byte(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_get_refcount(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_to_cstring(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_string_from_chars(CallExprAST *callExpr);
	// Cstring intrinsics
	mir::MIRValue *intrinsic_cstring_len(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_cstring_write_stdout(CallExprAST *callExpr);
	// Substring intrinsics
	mir::MIRValue *intrinsic_substring_len(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_byte_at(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_iter_pos(CallExprAST *callExpr);
	// Array intrinsics
	mir::MIRValue *intrinsic_array_len(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_array_capacity(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_array_set_length(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_array_grow(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_array_set_at(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_array_shift_right(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_array_shift_left(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_with_iter_pos(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_to_string(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_slice(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_parent_managed(CallExprAST *callExpr);
	mir::MIRValue *intrinsic_substring_byte_offset(CallExprAST *callExpr);

	// Helper methods for intrinsic codegen
	mir::MIRValue *getManagedStringPtr(ExprAST *arg);
	mir::MIRValue *getCstringPtr(ExprAST *arg);
	mir::MIRValue *getSubstringPtr(ExprAST *arg);
	mir::MIRType *getOrCreateManagedStringType();
	mir::MIRType *getOrCreateUnsizedArrayType();
	mir::MIRType *getOrCreateCstringType();
	mir::MIRType *getOrCreateSubstringType();

	// Array field info for intrinsics - handles both regular variables and struct fields
	struct ArrayFieldInfo {
		mir::MIRValue *dataPtr;		   // Pointer to array data (or alloca for heap arrays)
		mir::MIRValue *lengthAlloca;   // Hidden length alloca (may be null for struct fields)
		mir::MIRValue *capacityAlloca; // Hidden capacity alloca (may be null for struct fields)
		std::string elementType;	   // Element type string (e.g., "int", "string")
		bool isStackArray;			   // True if stack-allocated array
		bool isStructField;			   // True if this is a struct field access
		int fieldIndex;				   // Field index if isStructField is true
	};
	ArrayFieldInfo getArrayFieldInfo(ExprAST *arrayArg, int line, int column);

	// String and char literal generation
	mir::MIRValue *generateStringLiteral(StringLiteralExprAST *strExpr);
	mir::MIRValue *generateStringLiteralAsSlice(StringLiteralExprAST *strExpr);
	mir::MIRValue *generateCharLiteral(CharacterExprAST *charExpr);

	// Optional type helpers
	mir::MIRValue *createNilOptional(mir::MIRType *optionalType);
	mir::MIRValue *createSomeOptional(mir::MIRType *optionalType, mir::MIRValue *value);
	void generateIfLet(IfLetStmtAST *ifLet, mir::MIRFunction *function);
	void generateElseUnwrap(ElseUnwrapStmtAST *elseUnwrap, mir::MIRFunction *function);

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
	MIRCodeGenerator(const std::string &moduleName, bool debugInfo = false, int verboseLevel = 0,
					 bool trackAllocs = false);
	~MIRCodeGenerator();

	// Main generation entry point
	void generate(ProgramAST *program, bool needsEntryPoint = true,
				  const std::map<std::string, size_t> *functionIndices = nullptr,
				  const std::map<std::string, std::string> *functionReturnTypesIn = nullptr);

	// Get Maxon type of an expression (used for type-aware codegen)
	std::string getExpressionMaxonType(ExprAST *expr);

	// Optimization
	void optimize(CompilerStats *stats = nullptr);
	void runDeadCodeElimination();

	// Get instruction count (for stats)
	size_t getInstructionCount() const;

	// Output methods
	void printIR();
	void writeIRToFile(const std::string &filename);
	void writeObjectFile(const std::string &filename);
	void writeExecutable(const std::string &exeFile);

	// Access to underlying module (for testing)
	mir::MIRModule *getModule() { return module.get(); }
};

#endif // CODEGEN_MIR_H
