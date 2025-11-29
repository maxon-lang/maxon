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
	bool isImmutable; // true for 'let' variables
	bool isUsed;	  // true if variable is read/referenced
	bool isParameter; // true if this is a function parameter
	int line;
	int column;
	std::string initialValue; // For immutable variables, stores the literal value if available

	VariableInfo() : isImmutable(false), isUsed(false), isParameter(false), line(0), column(0) {}

	VariableInfo(const std::string &n, const std::string &t, bool immutable, int l = 0, int c = 0, bool param = false, const std::string &initVal = "")
		: name(n), type(t), isImmutable(immutable), isUsed(false), isParameter(param), line(l), column(c), initialValue(initVal) {}
};

// Function information
struct FunctionInfo {
	std::string name;
	std::string returnType;
	std::vector<FunctionParameter> parameters;
	std::string implementsInterface; // For interface methods: which interface this method implements

	FunctionInfo(const std::string &n, const std::string &ret, std::vector<FunctionParameter> params, const std::string &implInterface = "")
		: name(n), returnType(ret), parameters(std::move(params)), implementsInterface(implInterface) {}
};

// Struct field information
struct StructFieldInfo {
	std::string name;
	std::string type;
	int line;
	int column;

	StructFieldInfo(const std::string &n, const std::string &t, int l = 0, int c = 0)
		: name(n), type(t), line(l), column(c) {}
};

// Struct information
struct StructInfo {
	std::string name;
	std::vector<StructFieldInfo> fields;
	std::vector<std::string> conformsTo;				// Interfaces this struct conforms to
	std::map<std::string, std::string> typeAssignments; // Associated type assignments (e.g., "Element" -> "char")
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

	InterfaceMethodInfo(const std::string &n, const std::string &ret, std::vector<FunctionParameter> params)
		: name(n), returnType(ret), parameters(std::move(params)) {}
};

// Interface information
struct InterfaceInfo {
	std::string name;
	std::vector<InterfaceMethodInfo> methods;
	std::vector<std::string> associatedTypes; // Associated type declarations (e.g., "Element")
	int line;
	int column;

	InterfaceInfo(const std::string &n, int l = 0, int c = 0,
				  std::vector<std::string> assocTypes = {})
		: name(n), associatedTypes(std::move(assocTypes)), line(l), column(c) {}
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
	void registerExternalStruct(const std::string &name, const std::vector<StructFieldInfo> &fields);

	// Register all built-in functions (string methods, runtime functions, etc.)
	// Call this before analyze() to make built-ins available
	void registerBuiltinFunctions();

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

	// Get function indices for codegen optimization
	const std::map<std::string, size_t> &getFunctionIndices() const { return functionIndices; }

	// Get all structs
	const std::map<std::string, StructInfo> &getStructs() const { return structs; }

	// Get all interfaces
	const std::map<std::string, InterfaceInfo> &getInterfaces() const { return interfaces; }

	// Get persistent symbol table (all variables ever declared, for LSP)
	const std::map<std::string, VariableInfo> &getAllDeclaredVariables() const { return allDeclaredVariables; }

  private:
	Logger *logger_ = nullptr; // Optional logger for detailed tracing
	const StructInfo *lookupStruct(const std::string &name) const;
	std::vector<SemanticError> errors;
	std::map<std::string, FunctionInfo> functions;
	std::map<std::string, size_t> functionIndices;				 // Map function name to index for O(1) codegen lookup
	std::map<std::string, StructInfo> structs;					 // Struct definitions
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
	void declareVariable(const std::string &name, const std::string &type, bool isImmutable, int line = 0, int column = 0, bool isParameter = false, const std::string &initialValue = "");
	std::optional<VariableInfo> lookupVariable(const std::string &name);

	// Type checking
	std::string getExpressionType(ExprAST *expr);
	bool typesMatch(const std::string &type1, const std::string &type2);

	// Helper methods
	void addError(const std::string &message, int line = 0, int column = 0, const std::string &errCode = "");
	void addWarning(const std::string &message, int line = 0, int column = 0, const std::string &errCode = "");
	bool hasReturnInPath(const std::vector<std::unique_ptr<StmtAST>> &statements);
	void markVariableAsUsed(const std::string &name);
	void checkUnusedVariables();
	void checkInterfaceConformance(const std::string &structName, const std::vector<std::string> &conformsTo, int line, int column);
};

#endif // SEMANTIC_ANALYZER_H
