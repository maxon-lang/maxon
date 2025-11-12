#ifndef SEMANTIC_ANALYZER_H
#define SEMANTIC_ANALYZER_H

#include "ast.h"
#include <string>
#include <vector>
#include <map>
#include <set>
#include <memory>
#include <optional>

// Semantic error structure
struct SemanticError {
    std::string message;
    int line;
    int column;
    int severity; // 1 = Error, 2 = Warning
    
    SemanticError(const std::string& msg, int l = 0, int c = 0, int sev = 1)
        : message(msg), line(l), column(c), severity(sev) {}
};

// Variable information
struct VariableInfo {
    std::string name;
    std::string type;
    bool isImmutable; // true for 'let' variables
    bool isUsed;      // true if variable is read/referenced
    int line;
    int column;
    
    VariableInfo() : isImmutable(false), isUsed(false), line(0), column(0) {}
    
    VariableInfo(const std::string& n, const std::string& t, bool immutable, int l = 0, int c = 0)
        : name(n), type(t), isImmutable(immutable), isUsed(false), line(l), column(c) {}
};

// Function information
struct FunctionInfo {
    std::string name;
    std::string returnType;
    std::vector<FunctionParameter> parameters;
    
    FunctionInfo(const std::string& n, const std::string& ret, std::vector<FunctionParameter> params)
        : name(n), returnType(ret), parameters(std::move(params)) {}
};

class SemanticAnalyzer {
public:
    SemanticAnalyzer();
    
    // Analyze entire program and return errors
    std::vector<SemanticError> analyze(ProgramAST* program);
    
    // Register external/stdlib functions
    void registerExternalFunction(const std::string& name, const std::string& returnType, 
                                   const std::vector<FunctionParameter>& parameters);
    
    // Get errors from last analysis
    const std::vector<SemanticError>& getErrors() const { return errors; }
    
    // Check if there are errors
    bool hasErrors() const { return !errors.empty(); }
    
    // Get list of undefined functions from last analysis
    const std::set<std::string>& getUndefinedFunctions() const { return undefinedFunctions; }
    
private:
    std::vector<SemanticError> errors;
    std::map<std::string, FunctionInfo> functions;
    std::map<std::string, VariableInfo> variables; // Current scope variables
    std::vector<std::map<std::string, VariableInfo>> scopeStack; // Stack of variable scopes
    int loopDepth; // Track nested loop depth
    std::set<std::string> undefinedFunctions; // Track undefined function calls
    
    // Analysis methods
    void analyzeFunction(FunctionAST* func);
    void analyzeStatement(StmtAST* stmt, const std::string& currentFunctionReturnType);
    std::string analyzeExpression(ExprAST* expr);
    
    // Validation methods
    bool validateReturn(FunctionAST* func);
    bool validateBreakContinue(StmtAST* stmt);
    bool validateVariableUse(const std::string& name);
    bool validateAssignment(const std::string& name, const std::string& valueType);
    
    // Scope management
    void enterScope();
    void exitScope();
    void declareVariable(const std::string& name, const std::string& type, bool isImmutable, int line = 0, int column = 0);
    std::optional<VariableInfo> lookupVariable(const std::string& name);
    
    // Type checking
    std::string getExpressionType(ExprAST* expr);
    bool typesMatch(const std::string& type1, const std::string& type2);
    
    // Helper methods
    void addError(const std::string& message, int line = 0, int column = 0);
    void addWarning(const std::string& message, int line = 0, int column = 0);
    bool hasReturnInPath(const std::vector<std::unique_ptr<StmtAST>>& statements);
    void markVariableAsUsed(const std::string& name);
    void checkUnusedVariables();
};

#endif // SEMANTIC_ANALYZER_H
