#ifndef SEMANTIC_ANALYZER_H
#define SEMANTIC_ANALYZER_H

#include "ast.h"
#include "logger.h"
#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <vector>

// Semantic error structure
struct SemanticError {
	std::string message;
	int line;
	int column;
	int severity;	  // 1 = Error, 2 = Warning
	std::string code; // Error/warning code for identification

	SemanticError(const std::string &msg, int l = 0, int c = 0, int sev = 1, const std::string &errCode = "")
		: message(msg), line(l), column(c), severity(sev), code(errCode) {}
};

// Variable information
struct VariableInfo {
	std::string name;
	std::string type;
	bool isImmutable;	 // true for 'let' variables
	bool isUsed;		 // true if variable is read/referenced
	bool isParameter;	 // true if this is a function parameter
	bool isLoopVariable; // true if this is a for-loop iteration variable
	int line;
	int column;
	std::string initialValue; // For immutable variables, stores the literal value if available

	VariableInfo() : isImmutable(false), isUsed(false), isParameter(false), isLoopVariable(false), line(0), column(0) {}

	VariableInfo(const std::string &n, const std::string &t, bool immutable, int l = 0, int c = 0, bool param = false, const std::string &initVal = "", bool loopVar = false)
		: name(n), type(t), isImmutable(immutable), isUsed(false), isParameter(param), isLoopVariable(loopVar), line(l), column(c), initialValue(initVal) {}
};

// Function information
struct FunctionInfo {
	std::string name;
	std::string returnType;
	std::vector<FunctionParameter> parameters;
	std::string implementsInterface;									// For interface methods: which interface this method implements
	bool isSynthesizedDefault = false;									// True if synthesized from interface default implementation
	const std::vector<std::unique_ptr<StmtAST>> *defaultBody = nullptr; // Non-owning ptr to default impl body
	std::string selfType;												// For synthesized methods: concrete struct type to substitute for Self
	std::map<std::string, std::string> typeSubstitutions;				// For synthesized methods: associated type substitutions
	int line;
	int column;

	FunctionInfo(const std::string &n, const std::string &ret, std::vector<FunctionParameter> params, const std::string &implInterface = "", int l = 0, int c = 0)
		: name(n), returnType(ret), parameters(std::move(params)), implementsInterface(implInterface), line(l), column(c) {}

	// Check if this is a synthesized default method that needs code generation
	bool needsCodeGeneration() const { return isSynthesizedDefault && defaultBody != nullptr; }
};

// Struct field information
struct StructFieldInfo {
	std::string name;
	std::string type;
	bool isImmutable;		  // true for 'let', false for 'var'
	bool hasDefault;		  // true if field has a default value
	std::string defaultValue; // string representation of default value (for hover display)
	int line;
	int column;

	StructFieldInfo(const std::string &n, const std::string &t, bool immutable = false,
					bool hasDef = false, const std::string &defVal = "", int l = 0, int c = 0)
		: name(n), type(t), isImmutable(immutable), hasDefault(hasDef), defaultValue(defVal), line(l), column(c) {}
};

// Struct information
struct StructInfo {
	std::string name;
	std::vector<StructFieldInfo> fields;
	std::vector<std::string> conformsTo;				// Interfaces this struct conforms to
	std::map<std::string, std::string> typeAssignments; // Associated type assignments (e.g., "Element" -> "character")
	int line;
	int column;

	StructInfo(const std::string &n, std::vector<StructFieldInfo> f, int l = 0, int c = 0,
			   std::vector<std::string> conforms = {},
			   std::map<std::string, std::string> typeAssigns = {})
		: name(n), fields(std::move(f)), conformsTo(std::move(conforms)),
		  typeAssignments(std::move(typeAssigns)), line(l), column(c) {}
};

// Interface method signature information
struct InterfaceMethodInfo {
	std::string name;
	std::string returnType;
	std::vector<FunctionParameter> parameters;
	bool hasDefaultImplementation = false;
	const std::vector<std::unique_ptr<StmtAST>> *defaultBody = nullptr; // Non-owning ptr to AST body

	InterfaceMethodInfo(const std::string &n, const std::string &ret, std::vector<FunctionParameter> params,
						bool hasDefault = false, const std::vector<std::unique_ptr<StmtAST>> *defBody = nullptr)
		: name(n), returnType(ret), parameters(std::move(params)),
		  hasDefaultImplementation(hasDefault), defaultBody(defBody) {}
};

// Interface information
struct InterfaceInfo {
	std::string name;
	std::string extendsInterface; // Base interface this extends (empty if none)
	std::vector<InterfaceMethodInfo> methods;
	std::vector<std::string> associatedTypes; // Associated type declarations (e.g., "Element")
	int line;
	int column;

	InterfaceInfo(const std::string &n, int l = 0, int c = 0,
				  std::vector<std::string> assocTypes = {},
				  const std::string &extends = "")
		: name(n), extendsInterface(extends), associatedTypes(std::move(assocTypes)), line(l), column(c) {}
};

// Enum case associated value information
struct EnumAssocValueInfo {
	std::string name;
	std::string type;
	int line;
	int column;

	EnumAssocValueInfo(const std::string &n, const std::string &t, int l = 0, int c = 0)
		: name(n), type(t), line(l), column(c) {}
};

// Enum case information
struct EnumCaseInfo {
	std::string name;
	std::vector<EnumAssocValueInfo> associatedValues;
	int tagValue;				// Runtime tag value (0, 1, 2, ...)
	bool hasRawValue;			// True if this case has a raw value
	int64_t rawIntValue;		// Raw value if rawValueType is int
	std::string rawStringValue; // Raw value if rawValueType is string
	int line;
	int column;

	EnumCaseInfo(const std::string &n, int tag, int l = 0, int c = 0)
		: name(n), tagValue(tag), hasRawValue(false), rawIntValue(0), line(l), column(c) {}
};

// Enum information
struct EnumInfo {
	std::string name;
	std::string rawValueType; // "int", "string", or "" for simple enums
	std::vector<EnumCaseInfo> cases;
	bool hasAssociatedValues; // True if any case has associated values
	int line;
	int column;

	EnumInfo() : hasAssociatedValues(false), line(0), column(0) {}

	EnumInfo(const std::string &n, int l = 0, int c = 0, const std::string &rawType = "")
		: name(n), rawValueType(rawType), hasAssociatedValues(false), line(l), column(c) {}

	// Find a case by name, returns nullptr if not found
	const EnumCaseInfo *findCase(const std::string &caseName) const {
		for (const auto &c : cases) {
			if (c.name == caseName)
				return &c;
		}
		return nullptr;
	}
};

// Cached semantic analysis result for a single function
// Used for incremental re-analysis when only part of a document changes
struct FunctionSemanticResult {
	std::vector<VariableInfo> localVariables; // Variables declared in this function
	std::vector<SemanticError> errors;		  // Errors found in this function
	std::vector<SemanticError> warnings;	  // Warnings found in this function
	bool isValid;							  // Whether the function passed semantic checks

	// Source range of the function for overlap detection
	SourceRange sourceRange;

	// Dependencies: types and functions this function references
	std::set<std::string> referencedTypes;	   // Struct/enum types used
	std::set<std::string> referencedFunctions; // Functions called

	// Signature hash for detecting when callers need invalidation
	std::string signatureHash;

	FunctionSemanticResult() : isValid(false) {}
};

class SemanticAnalyzer {
  public:
	SemanticAnalyzer();

	// Set optional logger for detailed tracing
	void setLogger(Logger *logger) { logger_ = logger; }

	// Analyze entire program and return errors
	std::vector<SemanticError> analyze(ProgramAST *program);

	// Register external/stdlib functions
	void registerExternalFunction(const std::string &name, const std::string &returnType,
								  const std::vector<FunctionParameter> &parameters);

	// Register external/stdlib struct types
	// Call this before analyze() to make external structs available
	void registerExternalStruct(const std::string &name, const std::vector<StructFieldInfo> &fields,
								const std::vector<std::string> &conformsTo = {},
								const std::map<std::string, std::string> &typeAssignments = {});

	// Register external/stdlib interface types
	// Call this before analyze() to make external interfaces available for "is Interface" checks
	void registerExternalInterface(const std::string &name,
								   const std::vector<InterfaceMethodInfo> &methods = {},
								   const std::vector<std::string> &associatedTypes = {},
								   const std::string &extendsInterface = "");

	// Register external/stdlib enum types
	// Call this before analyze() to make external enums available as valid types
	void registerExternalEnum(const std::string &name, const std::string &rawValueType = "");

	// Register all built-in functions (string methods, runtime functions, etc.)
	// Call this before analyze() to make built-ins available
	void registerBuiltinFunctions();

	// Set source context for doc comment checking (call before analyze)
	// Required for checking that exported stdlib functions have doc comments
	void setSourceContext(const std::string &source, const std::string &filePath);

	// Get errors from last analysis
	const std::vector<SemanticError> &getErrors() const { return errors; }

	// Check if there are errors
	bool hasErrors() const { return !errors.empty(); }

	// Get list of undefined functions from last analysis
	const std::set<std::string> &getUndefinedFunctions() const { return undefinedFunctions; }

	// Get list of undefined structs from last analysis
	const std::set<std::string> &getUndefinedStructs() const { return undefinedStructs; }

	// Get list of undefined interfaces from last analysis
	const std::set<std::string> &getUndefinedInterfaces() const { return undefinedInterfaces; }

	// Get all variables from all scopes (for LSP hover/completion)
	std::map<std::string, VariableInfo> getAllVariables() const;

	// Get all functions
	const std::map<std::string, FunctionInfo> &getFunctions() const { return functions; }

	// Get synthesized default methods (methods generated from interface default implementations)
	std::vector<const FunctionInfo *> getSynthesizedMethods() const {
		std::vector<const FunctionInfo *> result;
		for (const auto &[name, info] : functions) {
			if (info.needsCodeGeneration()) {
				result.push_back(&info);
			}
		}
		return result;
	}

	// Get function indices for codegen optimization
	const std::map<std::string, size_t> &getFunctionIndices() const { return functionIndices; }

	// Get all structs
	const std::map<std::string, StructInfo> &getStructs() const { return structs; }

	// Get all interfaces
	const std::map<std::string, InterfaceInfo> &getInterfaces() const { return interfaces; }

	// Get all enums
	const std::map<std::string, EnumInfo> &getEnums() const { return enums; }

	// Get persistent symbol table (all variables ever declared, for LSP)
	const std::map<std::string, VariableInfo> &getAllDeclaredVariables() const { return allDeclaredVariables; }

	// === Incremental Analysis API ===

	// Mark a specific function as needing re-analysis
	void markFunctionDirty(const std::string &functionName);

	// Mark all functions that overlap with the given edit range as dirty
	void markFunctionsInRange(const SourceRange &editRange);

	// Perform incremental analysis, only re-analyzing dirty functions
	// Uses cached results for clean functions
	// Returns combined errors from all functions
	std::vector<SemanticError> analyzeIncremental(ProgramAST *program, const std::set<std::string> &dirtyFunctions);

	// Clear all cached results (call when document is fully re-parsed)
	void clearCache();

	// Get the current function cache (for inspection/debugging)
	const std::map<std::string, FunctionSemanticResult> &getFunctionCache() const { return functionCache_; }

	// Check if a function's signature has changed (for caller invalidation)
	bool hasSignatureChanged(const std::string &functionName, const FunctionInfo &newInfo) const;

	// Invalidate dependents when a type definition changes
	void invalidateTypeUsers(const std::string &typeName);

	// Invalidate dependents when a function signature changes
	void invalidateFunctionCallers(const std::string &functionName);

  private:
	Logger *logger_ = nullptr;			  // Optional logger for detailed tracing
	std::string sourceContent_;			  // Source code for doc comment extraction
	std::string currentFilePath_;		  // Current file path for stdlib detection
	ProgramAST *currentProgram = nullptr; // Current program being analyzed (for generic struct lookup)
	const StructInfo *lookupStruct(const std::string &name) const;
	const EnumInfo *lookupEnum(const std::string &name) const;
	std::vector<SemanticError> errors;
	std::map<std::string, FunctionInfo> functions;
	std::map<std::string, size_t> functionIndices;				 // Map function name to index for O(1) codegen lookup
	std::map<std::string, StructInfo> structs;					 // Struct definitions
	std::map<std::string, EnumInfo> enums;						 // Enum definitions
	std::map<std::string, InterfaceInfo> interfaces;			 // Interface definitions
	std::map<std::string, VariableInfo> variables;				 // Current scope variables
	std::vector<std::map<std::string, VariableInfo>> scopeStack; // Stack of variable scopes
	int loopDepth;												 // Track nested loop depth
	std::vector<std::set<std::string>> blockIdStack;			 // Stack of block identifier sets per nesting level
	std::vector<std::string> loopLabelStack;					 // Stack of loop labels for break/continue validation
	std::set<std::string> undefinedFunctions;					 // Track undefined function calls
	std::set<std::string> undefinedStructs;						 // Track undefined struct types
	std::set<std::string> undefinedInterfaces;					 // Track undefined interfaces in conformance
	std::string currentReceiverType;							 // Current struct type when analyzing a method (empty for free functions)

	// Persistent symbol table for LSP - stores all variables ever declared
	std::map<std::string, VariableInfo> allDeclaredVariables;

	// === Incremental Analysis Cache ===
	// Cache of semantic analysis results per function
	std::map<std::string, FunctionSemanticResult> functionCache_;

	// Set of functions that need re-analysis
	std::set<std::string> dirtyFunctions_;

	// Map from type name to functions that reference it (for invalidation)
	std::map<std::string, std::set<std::string>> typeToFunctionDeps_;

	// Map from function name to functions that call it (for caller invalidation)
	std::map<std::string, std::set<std::string>> functionToCallerDeps_;

	// Track dependencies during function analysis for building dependency maps
	std::set<std::string> currentFunctionTypeDeps_;
	std::set<std::string> currentFunctionCallDeps_;
	std::string currentFunctionName_;

	// Logging helpers
	void logTrace(const std::string &msg);
	void logDetail(const std::string &msg);

	// Analysis methods
	void analyzeFunction(FunctionAST *func);
	void analyzeStatement(StmtAST *stmt, const std::string &currentFunctionReturnType);
	std::string analyzeExpression(ExprAST *expr);

	// Validation methods
	bool validateReturn(FunctionAST *func);
	bool validateBreakContinue(StmtAST *stmt);
	bool validateVariableUse(const std::string &name);
	bool validateAssignment(const std::string &name, const std::string &valueType);

	// Scope management
	void enterScope();
	void exitScope();
	void declareBlockId(const std::string &blockId, int line = 0, int column = 0); // Register a block identifier
	void declareVariable(const std::string &name, const std::string &type, bool isImmutable, int line = 0, int column = 0, bool isParameter = false, const std::string &initialValue = "", bool isLoopVariable = false);
	std::optional<VariableInfo> lookupVariable(const std::string &name);

	// Type checking
	std::string getExpressionType(ExprAST *expr);
	bool typesMatch(const std::string &type1, const std::string &type2);
	bool isIterableType(const std::string &type, ExprAST *iterableExpr);

	// Optional type helpers
	bool isOptionalType(const std::string &type) const;
	std::string unwrapOptionalType(const std::string &type) const;
	std::string makeOptionalType(const std::string &type) const;

	// Capability-based interface checks (replaces hardcoded interface name checks)
	bool typeHasMethod(const std::string &typeName, const std::string &methodName,
					   const std::string &returnType,
					   const std::vector<std::string> &paramTypes = {}) const;
	bool typeIsHashable(const std::string &typeName) const;
	bool typeIsEquatable(const std::string &typeName) const;
	bool typeIsIterable(const std::string &typeName) const;

	// Helper methods
	void addError(const std::string &message, int line = 0, int column = 0, const std::string &errCode = "");
	void addWarning(const std::string &message, int line = 0, int column = 0, const std::string &errCode = "");
	bool hasReturnInPath(const std::vector<std::unique_ptr<StmtAST>> &statements);
	void markVariableAsUsed(const std::string &name);
	void checkUnusedVariables();
	void checkInterfaceConformance(const std::string &structName, const std::vector<std::string> &conformsTo, int line, int column, bool isGenericTemplate = false);
	void instantiateGenericStructMethods(const std::string &templateName, const std::string &specializedName,
										 const std::map<std::string, std::string> &typeBindings);

	// Incremental analysis helpers
	std::string computeSignatureHash(const FunctionInfo &funcInfo) const;
	void recordTypeDependency(const std::string &typeName);
	void recordCallDependency(const std::string &functionName);
	void updateDependencyMaps(const std::string &functionName);
	void analyzeFunctionForCache(FunctionAST *func, FunctionSemanticResult &result);
	std::string getFunctionKey(FunctionAST *func) const;
};

#endif // SEMANTIC_ANALYZER_H
