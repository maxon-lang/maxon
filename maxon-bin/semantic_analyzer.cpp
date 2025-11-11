#include "semantic_analyzer.h"
#include <algorithm>

SemanticAnalyzer::SemanticAnalyzer() : loopDepth(0) {}

std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST* program) {
    errors.clear();
    functions.clear();
    variables.clear();
    scopeStack.clear();
    loopDepth = 0;
    
    // First pass: collect all function declarations
    for (const auto& func : program->functions) {
        if (functions.find(func->name) != functions.end()) {
            addError("Function '" + func->name + "' is already defined");
        } else {
            functions.emplace(func->name, FunctionInfo(func->name, func->returnType, func->parameters));
        }
    }
    
    // Second pass: analyze each function
    for (const auto& func : program->functions) {
        analyzeFunction(func.get());
    }
    
    return errors;
}

void SemanticAnalyzer::analyzeFunction(FunctionAST* func) {
    // Enter function scope
    enterScope();
    
    // Declare parameters as variables
    for (const auto& param : func->parameters) {
        declareVariable(param.name, param.type, false);
    }
    
    // Analyze function body
    for (const auto& stmt : func->body) {
        analyzeStatement(stmt.get(), func->returnType);
    }
    
    // Validate return statement (for non-void functions)
    if (func->returnType != "void") {
        if (!validateReturn(func)) {
            addError("Function '" + func->name + "' must return a value of type '" + func->returnType + "'");
        }
    }
    
    // Exit function scope
    exitScope();
}

void SemanticAnalyzer::analyzeStatement(StmtAST* stmt, const std::string& currentFunctionReturnType) {
    if (auto varDecl = dynamic_cast<VarDeclStmtAST*>(stmt)) {
        // Check if variable already exists in current scope
        if (variables.find(varDecl->name) != variables.end()) {
            addError("Variable '" + varDecl->name + "' is already declared");
        }
        
        // Analyze initializer
        std::string initType = analyzeExpression(varDecl->initializer.get());
        
        // Declare variable
        declareVariable(varDecl->name, initType, false);
        
    } else if (auto letDecl = dynamic_cast<LetDeclStmtAST*>(stmt)) {
        // Check if variable already exists in current scope
        if (variables.find(letDecl->name) != variables.end()) {
            addError("Variable '" + letDecl->name + "' is already declared");
        }
        
        // Analyze initializer
        std::string initType = analyzeExpression(letDecl->initializer.get());
        
        // Declare immutable variable
        declareVariable(letDecl->name, initType, true);
        
    } else if (auto assign = dynamic_cast<AssignStmtAST*>(stmt)) {
        // Check if variable exists
        auto varInfo = lookupVariable(assign->name);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + assign->name + "'");
        } else {
            // Check if variable is immutable
            if (varInfo->isImmutable) {
                addError("Cannot assign to read-only variable '" + assign->name + "'");
            }
            
            // Check type compatibility
            std::string valueType = analyzeExpression(assign->value.get());
            if (!typesMatch(varInfo->type, valueType)) {
                addError("Type mismatch: cannot assign '" + valueType + "' to variable of type '" + varInfo->type + "'");
            }
        }
        
    } else if (auto ifStmt = dynamic_cast<IfStmtAST*>(stmt)) {
        // Analyze condition
        std::string condType = analyzeExpression(ifStmt->condition.get());
        if (condType != "bool" && condType != "int") {
            addError("If condition must be a boolean or integer expression");
        }
        
        // Analyze then block
        enterScope();
        for (const auto& s : ifStmt->thenBody) {
            analyzeStatement(s.get(), currentFunctionReturnType);
        }
        exitScope();
        
        // Analyze else block
        if (!ifStmt->elseBody.empty()) {
            enterScope();
            for (const auto& s : ifStmt->elseBody) {
                analyzeStatement(s.get(), currentFunctionReturnType);
            }
            exitScope();
        }
        
    } else if (auto whileStmt = dynamic_cast<WhileStmtAST*>(stmt)) {
        // Analyze condition
        std::string condType = analyzeExpression(whileStmt->condition.get());
        if (condType != "bool" && condType != "int") {
            addError("While condition must be a boolean or integer expression");
        }
        
        // Enter loop scope
        loopDepth++;
        enterScope();
        
        // Analyze loop body
        for (const auto& s : whileStmt->body) {
            analyzeStatement(s.get(), currentFunctionReturnType);
        }
        
        // Exit loop scope
        exitScope();
        loopDepth--;
        
    } else if (auto breakStmt = dynamic_cast<BreakStmtAST*>(stmt)) {
        // Validate break is inside a loop
        if (loopDepth == 0) {
            addError("'break' statement must be inside a loop");
        }
        
    } else if (auto continueStmt = dynamic_cast<ContinueStmtAST*>(stmt)) {
        // Validate continue is inside a loop
        if (loopDepth == 0) {
            addError("'continue' statement must be inside a loop");
        }
        
    } else if (auto returnStmt = dynamic_cast<ReturnStmtAST*>(stmt)) {
        // Analyze return value
        if (returnStmt->value) {
            std::string returnType = analyzeExpression(returnStmt->value.get());
            if (!typesMatch(currentFunctionReturnType, returnType)) {
                addError("Return type mismatch: expected '" + currentFunctionReturnType + 
                         "' but got '" + returnType + "'");
            }
        } else {
            if (currentFunctionReturnType != "void") {
                addError("Function must return a value of type '" + currentFunctionReturnType + "'");
            }
        }
    }
}

std::string SemanticAnalyzer::analyzeExpression(ExprAST* expr) {
    if (auto numExpr = dynamic_cast<NumberExprAST*>(expr)) {
        return "int";
        
    } else if (auto boolExpr = dynamic_cast<BooleanExprAST*>(expr)) {
        return "bool";
        
    } else if (auto varExpr = dynamic_cast<VariableExprAST*>(expr)) {
        auto varInfo = lookupVariable(varExpr->name);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + varExpr->name + "'");
            return "error";
        }
        return varInfo->type;
        
    } else if (auto binExpr = dynamic_cast<BinaryExprAST*>(expr)) {
        std::string leftType = analyzeExpression(binExpr->left.get());
        std::string rightType = analyzeExpression(binExpr->right.get());
        
        // Arithmetic operators: +, -, *, /
        if (binExpr->op == '+' || binExpr->op == '-' || binExpr->op == '*' || binExpr->op == '/') {
            if (leftType != "int" || rightType != "int") {
                addError("Arithmetic operators require integer operands");
                return "error";
            }
            return "int";
        }
        
        // Comparison operators: <, >, L (<=), G (>=), E (==), N (!=)
        if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || 
            binExpr->op == 'G' || binExpr->op == 'E' || binExpr->op == 'N') {
            if (leftType != rightType) {
                addError("Comparison operators require operands of the same type");
                return "error";
            }
            return "bool";
        }
        
        addError("Unknown binary operator");
        return "error";
        
    } else if (auto callExpr = dynamic_cast<CallExprAST*>(expr)) {
        // Check if function exists
        auto funcIt = functions.find(callExpr->callee);
        if (funcIt == functions.end()) {
            addError("Undefined function: '" + callExpr->callee + "'");
            return "error";
        }
        
        const FunctionInfo& funcInfo = funcIt->second;
        
        // Check argument count
        if (callExpr->args.size() != funcInfo.parameters.size()) {
            addError("Function '" + callExpr->callee + "' expects " + 
                     std::to_string(funcInfo.parameters.size()) + " arguments but got " +
                     std::to_string(callExpr->args.size()));
            return funcInfo.returnType;
        }
        
        // Check argument types
        for (size_t i = 0; i < callExpr->args.size(); i++) {
            std::string argType = analyzeExpression(callExpr->args[i].get());
            if (!typesMatch(funcInfo.parameters[i].type, argType)) {
                addError("Function '" + callExpr->callee + "' parameter " + std::to_string(i + 1) +
                         " expects type '" + funcInfo.parameters[i].type + 
                         "' but got '" + argType + "'");
            }
        }
        
        return funcInfo.returnType;
    }
    
    return "error";
}

bool SemanticAnalyzer::validateReturn(FunctionAST* func) {
    return hasReturnInPath(func->body);
}

bool SemanticAnalyzer::hasReturnInPath(const std::vector<std::unique_ptr<StmtAST>>& statements) {
    for (const auto& stmt : statements) {
        // Direct return statement
        if (dynamic_cast<ReturnStmtAST*>(stmt.get())) {
            return true;
        }
        
        // If statement with return in both branches
        if (auto ifStmt = dynamic_cast<IfStmtAST*>(stmt.get())) {
            if (!ifStmt->elseBody.empty()) {
                bool thenHasReturn = hasReturnInPath(ifStmt->thenBody);
                bool elseHasReturn = hasReturnInPath(ifStmt->elseBody);
                if (thenHasReturn && elseHasReturn) {
                    return true;
                }
            }
        }
    }
    
    return false;
}

void SemanticAnalyzer::enterScope() {
    scopeStack.push_back(variables);
}

void SemanticAnalyzer::exitScope() {
    if (!scopeStack.empty()) {
        variables = scopeStack.back();
        scopeStack.pop_back();
    }
}

void SemanticAnalyzer::declareVariable(const std::string& name, const std::string& type, bool isImmutable) {
    variables[name] = VariableInfo(name, type, isImmutable);
}

std::optional<VariableInfo> SemanticAnalyzer::lookupVariable(const std::string& name) {
    // Check current scope
    auto it = variables.find(name);
    if (it != variables.end()) {
        return it->second;
    }
    
    // Check parent scopes
    for (auto rit = scopeStack.rbegin(); rit != scopeStack.rend(); ++rit) {
        auto it = rit->find(name);
        if (it != rit->end()) {
            return it->second;
        }
    }
    
    return std::nullopt;
}

bool SemanticAnalyzer::typesMatch(const std::string& type1, const std::string& type2) {
    // Error type matches anything (to avoid cascading errors)
    if (type1 == "error" || type2 == "error") {
        return true;
    }
    
    return type1 == type2;
}

void SemanticAnalyzer::addError(const std::string& message, int line, int column) {
    errors.emplace_back(message, line, column);
}
