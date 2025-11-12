#include "semantic_analyzer.h"
#include <algorithm>

SemanticAnalyzer::SemanticAnalyzer() : loopDepth(0) {}

void SemanticAnalyzer::registerExternalFunction(const std::string& name, const std::string& returnType, 
                                                 const std::vector<FunctionParameter>& parameters) {
    functions.emplace(name, FunctionInfo(name, returnType, parameters));
}

std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST* program) {
    errors.clear();
    // Note: Don't clear functions here - we want to keep registered external functions
    variables.clear();
    scopeStack.clear();
    loopDepth = 0;
    undefinedFunctions.clear();
    
    // Register standard library functions (built-in)
    // print(int) -> int
    if (functions.find("print") == functions.end()) {
        functions.emplace("print", FunctionInfo("print", "int", 
            {FunctionParameter("value", "int", 0, 0)}));
    }
    
    // First pass: collect all function declarations (including namespace functions)
    for (const auto& func : program->functions) {
        // Build the qualified name if the function has a namespace
        std::string functionKey = func->namespaceName.empty() ? func->name : func->namespaceName + "::" + func->name;
        
        if (functions.find(functionKey) != functions.end()) {
            addError("Function '" + functionKey + "' is already defined" +
                    std::string("\n  Note: Each function name must be unique in the program"),
                    func->line, func->column);
        } else {
            functions.emplace(functionKey, FunctionInfo(functionKey, func->returnType, func->parameters));
        }
        
        // Also register the simple name if in global namespace (for backward compatibility)
        if (func->namespaceName.empty() && functions.find(func->name) == functions.end()) {
            functions.emplace(func->name, FunctionInfo(func->name, func->returnType, func->parameters));
        }
    }
    
    // Collect namespace functions with qualified names (namespace::function)
    for (const auto& ns : program->namespaces) {
        for (const auto& func : ns->functions) {
            std::string qualifiedName = ns->name + "::" + func->name;
            if (functions.find(qualifiedName) != functions.end()) {
                addError("Function '" + qualifiedName + "' is already defined" +
                        std::string("\n  Note: Each function name must be unique in the namespace"),
                        func->line, func->column);
            } else {
                functions.emplace(qualifiedName, FunctionInfo(qualifiedName, func->returnType, func->parameters));
            }
        }
    }
    
    // Second pass: analyze each function
    for (const auto& func : program->functions) {
        analyzeFunction(func.get());
    }
    
    // Analyze namespace functions
    for (const auto& ns : program->namespaces) {
        for (const auto& func : ns->functions) {
            analyzeFunction(func.get());
        }
    }
    
    return errors;
}

void SemanticAnalyzer::analyzeFunction(FunctionAST* func) {
    // If this is an extern function, skip body analysis
    if (func->isExtern) {
        return;
    }
    
    // Enter function scope
    enterScope();
    
    // Declare parameters as variables
    for (const auto& param : func->parameters) {
        declareVariable(param.name, param.type, false, param.line, param.column, true);
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
    
    // Check for unused variables before exiting scope
    checkUnusedVariables();
    
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
        
        // Safety restriction: explicit pointer type with 'var' is not allowed
        // Only check explicit type declarations, not inferred types (e.g., string literals)
        if (!varDecl->type.empty() && varDecl->type == "ptr") {
            addError("Pointer variables must be declared with 'let', not 'var'" +
                    std::string("\n  Note: For safety, pointers are immutable and must use 'let'"),
                    stmt->line, stmt->column);
        }
        
        // Determine the actual type - use declared type if provided, otherwise infer
        std::string actualType;
        if (!varDecl->type.empty()) {
            // Use declared type
            if (varDecl->arraySize > 0) {
                // Array type: encode as [size]elementType
                actualType = "[" + std::to_string(varDecl->arraySize) + "]" + varDecl->type;
            } else {
                actualType = varDecl->type;
            }
        } else {
            // Type inference from initializer
            actualType = initType;
        }
        
        // Declare variable
        declareVariable(varDecl->name, actualType, false, stmt->line, stmt->column);
        
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
        
        // Determine the actual type - use declared type if provided, otherwise infer
        std::string actualType;
        if (!letDecl->type.empty()) {
            // Use declared type
            if (letDecl->arraySize > 0) {
                // Array type: encode as [size]elementType
                actualType = "[" + std::to_string(letDecl->arraySize) + "]" + letDecl->type;
            } else {
                actualType = letDecl->type;
            }
        } else {
            // Type inference from initializer
            actualType = initType;
        }
        
        // Declare immutable variable
        declareVariable(letDecl->name, actualType, true, stmt->line, stmt->column);
        
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
        
    } else if (auto arrayAssign = dynamic_cast<ArrayAssignStmtAST*>(stmt)) {
        // Check if array variable exists
        auto varInfo = lookupVariable(arrayAssign->arrayName);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + arrayAssign->arrayName + "'" +
                    std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
                    stmt->line, stmt->column);
        } else {
            // Mark array as used when assigned to
            markVariableAsUsed(arrayAssign->arrayName);
            
            // Check if variable is immutable
            if (varInfo->isImmutable) {
                addError("Cannot assign to read-only array '" + arrayAssign->arrayName + "'" +
                        std::string("\n  Array declared with 'let' at line ") + std::to_string(varInfo->line) +
                        ", column " + std::to_string(varInfo->column) +
                        "\n  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable arrays",
                        stmt->line, stmt->column);
            }
            
            // Analyze index expression (should be int)
            std::string indexType = analyzeExpression(arrayAssign->index.get());
            if (indexType != "int") {
                addError("Array index must be an integer" +
                        std::string("\n  Found type: ") + indexType,
                        stmt->line, stmt->column);
            }
            
            // Analyze value expression
            analyzeExpression(arrayAssign->value.get());
        }
        
    } else if (auto derefAssign = dynamic_cast<DerefAssignStmtAST*>(stmt)) {
        // Analyze the pointer expression (should result in ptr type)
        std::string ptrType = analyzeExpression(derefAssign->pointer.get());
        if (ptrType != "ptr") {
            addError("Cannot dereference non-pointer type: '" + ptrType + "'" +
                    std::string("\n  Note: Only pointer (ptr) types can be dereferenced with *"),
                    stmt->line, stmt->column);
        }
        
        // Analyze value expression
        analyzeExpression(derefAssign->value.get());
        
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
        
    } else if (dynamic_cast<CharacterExprAST*>(expr)) {
        return "char";
        
    } else if (dynamic_cast<StringLiteralExprAST*>(expr)) {
        return "ptr";  // String literals are pointers to character data
        
    } else if (auto castExpr = dynamic_cast<CastExprAST*>(expr)) {
        // Analyze the expression being cast
        std::string sourceType = analyzeExpression(castExpr->expr.get());
        
        // For now, allow any cast (we could add more strict checking later)
        // Valid casts: int <-> ptr, int <-> char, char <-> int, ptr <-> int
        return castExpr->targetType;
        
    } else if (auto addrExpr = dynamic_cast<AddressOfExprAST*>(expr)) {
        // Check if variable exists
        auto varInfo = lookupVariable(addrExpr->varName);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + addrExpr->varName + "'" +
                    std::string("\n  Note: Cannot take address of undefined variable"),
                    expr->line, expr->column);
            return "error";
        }
        // Mark variable as used when taking its address
        markVariableAsUsed(addrExpr->varName);
        // Address-of always returns a pointer type
        return "ptr";
        
    } else if (auto derefExpr = dynamic_cast<DerefExprAST*>(expr)) {
        // Analyze the expression being dereferenced
        std::string ptrType = analyzeExpression(derefExpr->expr.get());
        
        // Should be a pointer type
        if (ptrType != "ptr" && ptrType != "error") {
            addError("Dereference operator (*) requires a pointer type" +
                    std::string("\n  Found type: ") + ptrType,
                    expr->line, expr->column);
            return "error";
        }
        
        // Dereferencing a pointer gives us an int (for now, we assume pointers point to ints)
        // TODO: Add proper type tracking for what pointers point to
        return "int";
        
    } else if (auto varExpr = dynamic_cast<VariableExprAST*>(expr)) {
        auto varInfo = lookupVariable(varExpr->name);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + varExpr->name + "'" +
                    std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
                    expr->line, expr->column);
            return "error";
        }
        // Mark variable as used when it's referenced
        markVariableAsUsed(varExpr->name);
        return varInfo->type;
        
    } else if (auto binExpr = dynamic_cast<BinaryExprAST*>(expr)) {
        std::string leftType = analyzeExpression(binExpr->left.get());
        std::string rightType = analyzeExpression(binExpr->right.get());
        
        // Arithmetic operators: +, -, *, /, %
        if (binExpr->op == '+' || binExpr->op == '-' || binExpr->op == '*' || 
            binExpr->op == '/' || binExpr->op == '%') {
            // Safety restriction: no pointer arithmetic allowed
            if (leftType == "ptr" || rightType == "ptr") {
                std::string opName;
                if (binExpr->op == '+') opName = "addition (+)";
                else if (binExpr->op == '-') opName = "subtraction (-)";
                else if (binExpr->op == '*') opName = "multiplication (*)";
                else if (binExpr->op == '/') opName = "division (/)";
                else opName = "modulo (%)";
                
                addError("Pointer arithmetic is not allowed" +
                        std::string("\n  Operator: ") + opName +
                        "\n  Note: For safety, pointers cannot be used in arithmetic operations",
                        expr->line, expr->column);
                return "error";
            }
            
            if (leftType != "int" || rightType != "int") {
                std::string opName;
                if (binExpr->op == '+') opName = "addition (+)";
                else if (binExpr->op == '-') opName = "subtraction (-)";
                else if (binExpr->op == '*') opName = "multiplication (*)";
                else if (binExpr->op == '/') opName = "division (/)";
                else opName = "modulo (%)";
                
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
        // Try to find the function - first exact match, then unqualified lookup
        auto funcIt = functions.find(callExpr->callee);
        
        // Check if this is a qualified call (contains ::)
        bool isQualifiedCall = callExpr->callee.find("::") != std::string::npos;
        
        // If the call is qualified, check if it's unnecessary
        if (isQualifiedCall && funcIt != functions.end()) {
            // Extract the unqualified name (everything after the last ::)
            size_t lastColonPos = callExpr->callee.rfind("::");
            std::string unqualifiedName = callExpr->callee.substr(lastColonPos + 2);
            
            // Check how many functions match the unqualified name
            std::string searchSuffix = "::" + unqualifiedName;
            std::vector<std::string> matches;
            
            // Check for exact match with unqualified name (global function)
            if (functions.find(unqualifiedName) != functions.end()) {
                matches.push_back(unqualifiedName);
            }
            
            // Check for qualified matches
            for (const auto& pair : functions) {
                const std::string& funcName = pair.first;
                if (funcName.size() > searchSuffix.size() &&
                    funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
                    matches.push_back(funcName);
                }
            }
            
            // If there's only one match, the qualified name is unnecessary
            if (matches.size() == 1) {
                addWarning("Unnecessary qualified name: '" + callExpr->callee + "'" +
                          std::string("\n  The unqualified name '") + unqualifiedName + "' is unambiguous" +
                          "\n  Consider using '" + unqualifiedName + "' instead",
                          expr->line, expr->column, "unnecessary-qualified-name");
            }
        }
        
        // If not found and the name is unqualified (no ::), try suffix matching
        if (funcIt == functions.end() && callExpr->callee.find("::") == std::string::npos) {
            std::string searchSuffix = "::" + callExpr->callee;
            std::vector<std::string> matches;
            
            for (const auto& pair : functions) {
                const std::string& funcName = pair.first;
                if (funcName.size() > searchSuffix.size() &&
                    funcName.substr(funcName.size() - searchSuffix.size()) == searchSuffix) {
                    matches.push_back(funcName);
                }
            }
            
            if (matches.empty()) {
                // Track this as an undefined function for potential auto-discovery
                undefinedFunctions.insert(callExpr->callee);
                
                addError("Undefined function: '" + callExpr->callee + "'" +
                        std::string("\n  Note: Function must be defined before it can be called"),
                        expr->line, expr->column);
                return "error";
            } else if (matches.size() > 1) {
                // Ambiguous call
                std::string errorMsg = "Ambiguous function call: '" + callExpr->callee + "'" +
                                     std::string("\n  Multiple definitions found:");
                for (const auto& match : matches) {
                    errorMsg += "\n    - " + match;
                }
                errorMsg += "\n  Use a qualified name to disambiguate (e.g., namespace.function)";
                addError(errorMsg, expr->line, expr->column);
                return "error";
            }
            
            // Exactly one match - use it for validation (don't modify AST)
            // Note: We found the function, so it's not undefined
            funcIt = functions.find(matches[0]);
        }
        
        if (funcIt == functions.end()) {
            // Track this as an undefined function for potential auto-discovery
            undefinedFunctions.insert(callExpr->callee);
            
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
        
    } else if (auto arrayExpr = dynamic_cast<ArrayIndexExprAST*>(expr)) {
        // Check if array variable exists
        auto varInfo = lookupVariable(arrayExpr->arrayName);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + arrayExpr->arrayName + "'" +
                    std::string("\n  Note: Array must be declared before use"),
                    expr->line, expr->column);
            return "error";
        }
        
        // Mark array variable as used when accessed
        markVariableAsUsed(arrayExpr->arrayName);
        
        // Analyze the index expression
        std::string indexType = analyzeExpression(arrayExpr->index.get());
        if (indexType != "int" && indexType != "error") {
            addError("Array index must be an integer" +
                    std::string("\n  Found type: ") + indexType,
                    expr->line, expr->column);
        }
        
        // For now, assume all array elements are ints
        // TODO: Extract actual element type from array type (e.g., "[5]int" -> "int")
        return "int";
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
        // Before restoring parent scope, preserve usage information for parent scope variables
        auto childVariables = variables;
        variables = scopeStack.back();
        scopeStack.pop_back();
        
        // Propagate "isUsed" flag from child scope to parent scope for shared variables
        for (auto& parentVar : variables) {
            auto childIt = childVariables.find(parentVar.first);
            if (childIt != childVariables.end() && childIt->second.isUsed) {
                parentVar.second.isUsed = true;
            }
        }
    }
}

void SemanticAnalyzer::declareVariable(const std::string& name, const std::string& type, bool isImmutable, int line, int column, bool isParameter) {
    variables[name] = VariableInfo(name, type, isImmutable, line, column, isParameter);
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

void SemanticAnalyzer::addError(const std::string& message, int line, int column, const std::string& errCode) {
    errors.emplace_back(message, line, column, 1, errCode); // Severity 1 = Error
}

void SemanticAnalyzer::addWarning(const std::string& message, int line, int column, const std::string& errCode) {
    errors.emplace_back(message, line, column, 2, errCode); // Severity 2 = Warning
}

void SemanticAnalyzer::markVariableAsUsed(const std::string& name) {
    // Check current scope
    auto it = variables.find(name);
    if (it != variables.end()) {
        it->second.isUsed = true;
        return;
    }
    
    // Check parent scopes
    for (auto& scope : scopeStack) {
        auto it = scope.find(name);
        if (it != scope.end()) {
            it->second.isUsed = true;
            return;
        }
    }
}

void SemanticAnalyzer::checkUnusedVariables() {
    // Check all variables in current scope
    for (const auto& pair : variables) {
        const VariableInfo& varInfo = pair.second;
        // Skip parameters - it's okay if they're unused
        if (!varInfo.isUsed && !varInfo.isParameter) {
            addWarning("The variable '" + varInfo.name + "' is assigned but its value is never used",
                      varInfo.line, varInfo.column, "unused-variable");
        }
    }
}
