#include "semantic_analyzer.h"
#include <algorithm>

SemanticAnalyzer::SemanticAnalyzer() : loopDepth(0) {}

std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST* program) {
    errors.clear();
    functions.clear();
    variables.clear();
    scopeStack.clear();
    loopDepth = 0;
    
    // Register standard library functions
    // print(int) -> int
    functions.emplace("print", FunctionInfo("print", "int", 
        {FunctionParameter("value", "int", 0, 0)}));
    
    // First pass: collect all function declarations
    for (const auto& func : program->functions) {
        if (functions.find(func->name) != functions.end()) {
            addError("Function '" + func->name + "' is already defined" +
                    std::string("\n  Note: Each function name must be unique in the program"),
                    func->line, func->column);
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
        declareVariable(param.name, param.type, false, param.line, param.column);
    }
    
    // Analyze function body
    for (const auto& stmt : func->body) {
        analyzeStatement(stmt.get(), func->returnType);
    }
    
    // Validate return statement (for non-void functions)
    if (func->returnType != "void") {
        if (!validateReturn(func)) {
            addError("Function '" + func->name + "' must return a value of type '" + func->returnType + "'" +
                    std::string("\n  Note: All execution paths through the function must end with a return statement"),
                    func->line, func->column);
        }
    }
    
    // Exit function scope
    exitScope();
}

void SemanticAnalyzer::analyzeStatement(StmtAST* stmt, const std::string& currentFunctionReturnType) {
    if (auto varDecl = dynamic_cast<VarDeclStmtAST*>(stmt)) {
        // Check if variable already exists in current scope
        if (variables.find(varDecl->name) != variables.end()) {
            auto existing = variables[varDecl->name];
            addError("Variable '" + varDecl->name + "' is already declared" +
                    std::string("\n  Previous declaration at line ") + std::to_string(existing.line) +
                    ", column " + std::to_string(existing.column) +
                    "\n  Note: Variable names must be unique within their scope",
                    stmt->line, stmt->column);
        }
        
        // Analyze initializer
        std::string initType = analyzeExpression(varDecl->initializer.get());
        
        // Declare variable
        declareVariable(varDecl->name, initType, false, stmt->line, stmt->column);
        
    } else if (auto letDecl = dynamic_cast<LetDeclStmtAST*>(stmt)) {
        // Check if variable already exists in current scope
        if (variables.find(letDecl->name) != variables.end()) {
            auto existing = variables[letDecl->name];
            addError("Variable '" + letDecl->name + "' is already declared" +
                    std::string("\n  Previous declaration at line ") + std::to_string(existing.line) +
                    ", column " + std::to_string(existing.column) +
                    "\n  Note: Variable names must be unique within their scope",
                    stmt->line, stmt->column);
        }
        
        // Analyze initializer
        std::string initType = analyzeExpression(letDecl->initializer.get());
        
        // Declare immutable variable
        declareVariable(letDecl->name, initType, true, stmt->line, stmt->column);
        
    } else if (auto assign = dynamic_cast<AssignStmtAST*>(stmt)) {
        // Check if variable exists
        auto varInfo = lookupVariable(assign->name);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + assign->name + "'" +
                    std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
                    stmt->line, stmt->column);
        } else {
            // Check if variable is immutable
            if (varInfo->isImmutable) {
                addError("Cannot assign to read-only variable '" + assign->name + "'" +
                        std::string("\n  Variable declared with 'let' at line ") + std::to_string(varInfo->line) +
                        ", column " + std::to_string(varInfo->column) +
                        "\n  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable variables",
                        stmt->line, stmt->column);
            }
            
            // Check type compatibility
            std::string valueType = analyzeExpression(assign->value.get());
            if (!typesMatch(varInfo->type, valueType)) {
                addError("Type mismatch: cannot assign '" + valueType + "' to variable of type '" + varInfo->type + "'" +
                        std::string("\n  Variable '") + assign->name + "' declared at line " + 
                        std::to_string(varInfo->line) + ", column " + std::to_string(varInfo->column),
                        stmt->line, stmt->column);
            }
        }
        
    } else if (auto exprStmt = dynamic_cast<ExprStmtAST*>(stmt)) {
        // Analyze the expression (e.g., function call)
        analyzeExpression(exprStmt->expression.get());
        
    } else if (auto ifStmt = dynamic_cast<IfStmtAST*>(stmt)) {
        // Analyze condition
        std::string condType = analyzeExpression(ifStmt->condition.get());
        if (condType != "bool" && condType != "int") {
            addError("If condition must be a boolean or integer expression" +
                    std::string("\n  Found type: ") + condType +
                    "\n  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)",
                    stmt->line, stmt->column);
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
            addError("While condition must be a boolean or integer expression" +
                    std::string("\n  Found type: ") + condType +
                    "\n  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)",
                    stmt->line, stmt->column);
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
        
    } else if (dynamic_cast<BreakStmtAST*>(stmt)) {
        // Validate break is inside a loop
        if (loopDepth == 0) {
            addError("'break' statement must be inside a loop" +
                    std::string("\n  Note: 'break' can only be used within 'while' loops"),
                    stmt->line, stmt->column);
        }
        
    } else if (dynamic_cast<ContinueStmtAST*>(stmt)) {
        // Validate continue is inside a loop
        if (loopDepth == 0) {
            addError("'continue' statement must be inside a loop" +
                    std::string("\n  Note: 'continue' can only be used within 'while' loops"),
                    stmt->line, stmt->column);
        }
        
    } else if (auto returnStmt = dynamic_cast<ReturnStmtAST*>(stmt)) {
        // Analyze return value
        if (returnStmt->value) {
            std::string returnType = analyzeExpression(returnStmt->value.get());
            if (!typesMatch(currentFunctionReturnType, returnType)) {
                addError("Return type mismatch" +
                        std::string("\n  Expected: ") + currentFunctionReturnType +
                        "\n  Found: " + returnType +
                        "\n  Note: The return value type must match the function's declared return type",
                        stmt->line, stmt->column);
            }
        } else {
            if (currentFunctionReturnType != "void") {
                addError("Function must return a value of type '" + currentFunctionReturnType + "'" +
                        std::string("\n  Note: Use 'return <expression>' to return a value"),
                        stmt->line, stmt->column);
            }
        }
    }
}

std::string SemanticAnalyzer::analyzeExpression(ExprAST* expr) {
    if (dynamic_cast<NumberExprAST*>(expr)) {
        return "int";
        
    } else if (dynamic_cast<BooleanExprAST*>(expr)) {
        return "bool";
        
    } else if (auto varExpr = dynamic_cast<VariableExprAST*>(expr)) {
        auto varInfo = lookupVariable(varExpr->name);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + varExpr->name + "'" +
                    std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
                    expr->line, expr->column);
            return "error";
        }
        return varInfo->type;
        
    } else if (auto binExpr = dynamic_cast<BinaryExprAST*>(expr)) {
        std::string leftType = analyzeExpression(binExpr->left.get());
        std::string rightType = analyzeExpression(binExpr->right.get());
        
        // Arithmetic operators: +, -, *, /
        if (binExpr->op == '+' || binExpr->op == '-' || binExpr->op == '*' || binExpr->op == '/') {
            if (leftType != "int" || rightType != "int") {
                std::string opName;
                if (binExpr->op == '+') opName = "addition (+)";
                else if (binExpr->op == '-') opName = "subtraction (-)";
                else if (binExpr->op == '*') opName = "multiplication (*)";
                else opName = "division (/)";
                
                addError("Arithmetic operator " + opName + " requires integer operands" +
                        std::string("\n  Left operand type: ") + leftType +
                        "\n  Right operand type: " + rightType,
                        expr->line, expr->column);
                return "error";
            }
            return "int";
        }
        
        // Comparison operators: <, >, L (<=), G (>=), E (==), N (!=)
        if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || 
            binExpr->op == 'G' || binExpr->op == 'E' || binExpr->op == 'N') {
            if (leftType != rightType) {
                addError("Comparison operators require operands of the same type" +
                        std::string("\n  Left operand type: ") + leftType +
                        "\n  Right operand type: " + rightType,
                        expr->line, expr->column);
                return "error";
            }
            return "bool";
        }
        
        addError("Unknown binary operator", expr->line, expr->column);
        return "error";
        
    } else if (auto callExpr = dynamic_cast<CallExprAST*>(expr)) {
        // Check if function exists
        auto funcIt = functions.find(callExpr->callee);
        if (funcIt == functions.end()) {
            addError("Undefined function: '" + callExpr->callee + "'" +
                    std::string("\n  Note: Function must be defined before it can be called"),
                    expr->line, expr->column);
            return "error";
        }
        
        const FunctionInfo& funcInfo = funcIt->second;
        
        // Check argument count
        if (callExpr->args.size() != funcInfo.parameters.size()) {
            addError("Function '" + callExpr->callee + "' argument count mismatch" +
                    std::string("\n  Expected: ") + std::to_string(funcInfo.parameters.size()) + " argument" +
                    (funcInfo.parameters.size() == 1 ? "" : "s") +
                    "\n  Found: " + std::to_string(callExpr->args.size()) + " argument" +
                    (callExpr->args.size() == 1 ? "" : "s"),
                    expr->line, expr->column);
            return funcInfo.returnType;
        }
        
        // Check argument types
        for (size_t i = 0; i < callExpr->args.size(); i++) {
            std::string argType = analyzeExpression(callExpr->args[i].get());
            if (!typesMatch(funcInfo.parameters[i].type, argType)) {
                addError("Function '" + callExpr->callee + "' argument type mismatch" +
                        std::string("\n  Parameter ") + std::to_string(i + 1) + " ('" + 
                        funcInfo.parameters[i].name + "')" +
                        "\n  Expected type: " + funcInfo.parameters[i].type +
                        "\n  Found type: " + argType,
                        expr->line, expr->column);
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

void SemanticAnalyzer::declareVariable(const std::string& name, const std::string& type, bool isImmutable, int line, int column) {
    variables[name] = VariableInfo(name, type, isImmutable, line, column);
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
