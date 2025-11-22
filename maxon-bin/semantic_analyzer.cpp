#include "semantic_analyzer.h"
#include "lexer.h"
#include <algorithm>

// Forward declaration of helper function
static std::string extractLiteralValue(ExprAST* expr);

SemanticAnalyzer::SemanticAnalyzer() : loopDepth(0) {}

const StructInfo* SemanticAnalyzer::lookupStruct(const std::string& name) const {
    // First try exact match
    auto it = structs.find(name);
    if (it != structs.end()) {
        return &it->second;
    }
    
    // If not found and name contains a dot, it's already qualified
    if (name.find('.') != std::string::npos) {
        return nullptr;
    }
    
    // For unqualified names, also try qualified lookups with all known namespaces
    // This allows unqualified access to exported structs
    for (const auto& pair : structs) {
        const std::string& structName = pair.first;
        // Check if this is a qualified name ending with the requested name
        if (structName.size() > name.size() + 1 && 
            structName.substr(structName.size() - name.size()) == name &&
            structName[structName.size() - name.size() - 1] == '.') {
            return &pair.second;
        }
    }
    
    return nullptr;
}

void SemanticAnalyzer::registerExternalFunction(const std::string& name, const std::string& returnType, 
                                                 const std::vector<FunctionParameter>& parameters) {
    functions.emplace(name, FunctionInfo(name, returnType, parameters));
}

std::vector<SemanticError> SemanticAnalyzer::analyze(ProgramAST* program) {
    errors.clear();
    // Note: Don't clear functions here - we want to keep registered external functions
    structs.clear();
    variables.clear();
    scopeStack.clear();
    loopDepth = 0;
    undefinedFunctions.clear();
    allDeclaredVariables.clear(); // Clear persistent symbol table
    
    // Register standard library functions (built-in)
    // print(int) -> int
    if (functions.find("print") == functions.end()) {
        functions.emplace("print", FunctionInfo("print", "int", 
            {FunctionParameter("value", "int", 0, 0)}));
    }
    
    // print_float(float, int) -> int
    if (functions.find("print_float") == functions.end()) {
        functions.emplace("print_float", FunctionInfo("print_float", "int", 
            {FunctionParameter("value", "float", 0, 0), FunctionParameter("precision", "int", 0, 0)}));
    }
    
    // First pass: collect all struct definitions
    for (const auto& structDef : program->structs) {
        // Build the qualified name if the struct has a namespace and is exported
        std::string structKey = (structDef->isExported && !structDef->namespaceName.empty()) 
            ? structDef->namespaceName + "." + structDef->name 
            : structDef->name;
        
        if (structs.find(structKey) != structs.end()) {
            addError("Struct '" + structKey + "' is already defined",
                    structDef->line, structDef->column);
        } else {
            // Convert StructField to StructFieldInfo
            std::vector<StructFieldInfo> fields;
            std::set<std::string> fieldNames;
            for (const auto& field : structDef->fields) {
                // Check for duplicate field names
                if (fieldNames.find(field.name) != fieldNames.end()) {
                    addError("Duplicate field '" + field.name + "' in struct '" + structDef->name + "'",
                            field.line, field.column);
                } else {
                    fieldNames.insert(field.name);
                    fields.push_back(StructFieldInfo(field.name, field.type, field.line, field.column));
                }
            }
            structs.emplace(structKey, StructInfo(structDef->name, std::move(fields), 
                                                        structDef->line, structDef->column));
            
            // Also register the simple name for use within the same file/namespace
            if (structs.find(structDef->name) == structs.end()) {
                structs.emplace(structDef->name, StructInfo(structDef->name, fields, 
                                                            structDef->line, structDef->column));
            }
        }
    }
    
    // Second pass: collect all function declarations
    for (const auto& func : program->functions) {
        // Build the qualified name if the function has a namespace
        std::string functionKey = func->namespaceName.empty() ? func->name : func->namespaceName + "." + func->name;
        
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
    
    // Second pass: analyze each function
    for (const auto& func : program->functions) {
        analyzeFunction(func.get());
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
        if (!varDecl->type.empty() && (varDecl->type == "ptr" || varDecl->type == "string")) {
            addError("Pointer variables must be declared with 'let', not 'var'" +
                    std::string("\n  Note: For safety, pointers are immutable and must use 'let'"),
                    stmt->line, stmt->column);
        }
        
        // Determine the actual type - use declared type if provided, otherwise infer
        std::string actualType;
        if (!varDecl->type.empty()) {
            // Use declared type (explicit type annotation)
            actualType = varDecl->type;
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
            // Use declared type (explicit type annotation)
            actualType = letDecl->type;
        } else {
            // Type inference from initializer
            actualType = initType;
        }
        
        // Extract literal value for immutable variables (for LSP hover)
        std::string literalValue = extractLiteralValue(letDecl->initializer.get());
        
        // Declare immutable variable
        declareVariable(letDecl->name, actualType, true, stmt->line, stmt->column, false, literalValue);
        
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
        
    } else if (auto arrayMemberAssign = dynamic_cast<ArrayMemberAssignStmtAST*>(stmt)) {
        // Check if array variable exists
        auto varInfo = lookupVariable(arrayMemberAssign->arrayName);
        if (!varInfo.has_value()) {
            addError("Undefined variable: '" + arrayMemberAssign->arrayName + "'" +
                    std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
                    stmt->line, stmt->column);
        } else {
            // Mark array as used
            markVariableAsUsed(arrayMemberAssign->arrayName);
            
            // Check if variable is immutable
            if (varInfo->isImmutable) {
                addError("Cannot assign to read-only array element member '" + arrayMemberAssign->arrayName + "'" +
                        std::string("\n  Array declared with 'let' at line ") + std::to_string(varInfo->line) +
                        ", column " + std::to_string(varInfo->column),
                        stmt->line, stmt->column);
            }
            
            // Analyze index expression (should be int)
            std::string indexType = analyzeExpression(arrayMemberAssign->index.get());
            if (indexType != "int") {
                addError("Array index must be an integer" +
                        std::string("\n  Found type: ") + indexType,
                        stmt->line, stmt->column);
            }
            
            // Get element type and verify it's a struct
            std::string arrayType = varInfo->type;
            if (arrayType.size() > 2 && arrayType[0] == '[') {
                size_t closeBracket = arrayType.find(']');
                if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
                    std::string elementType = arrayType.substr(closeBracket + 1);
                    
                    if (lookupStruct(elementType) != nullptr) {
                        // Verify member exists
                        const auto& structInfo = structs.at(elementType);
                        bool memberFound = false;
                        for (const auto& field : structInfo.fields) {
                            if (field.name == arrayMemberAssign->memberName) {
                                memberFound = true;
                                break;
                            }
                        }
                        
                        if (!memberFound) {
                            addError("Struct '" + elementType + "' has no field named '" + arrayMemberAssign->memberName + "'",
                                    stmt->line, stmt->column);
                        }
                    } else {
                        addError("Cannot access member '" + arrayMemberAssign->memberName + "' on non-struct array element type '" + elementType + "'",
                                stmt->line, stmt->column);
                    }
                }
            }
            
            // Analyze value expression
            analyzeExpression(arrayMemberAssign->value.get());
        }
        
    } else if (auto derefAssign = dynamic_cast<DerefAssignStmtAST*>(stmt)) {
        // Analyze the pointer expression (should result in ptr or string type)
        std::string ptrType = analyzeExpression(derefAssign->pointer.get());
        if (ptrType != "ptr" && ptrType != "string") {
            addError("Cannot dereference non-pointer type: '" + ptrType + "'" +
                    std::string("\n  Note: Only pointer (ptr/string) types can be dereferenced with *"),
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
        
    } else if (auto forStmt = dynamic_cast<ForStmtAST*>(stmt)) {
        // For-loops require iterator functions from stdlib
        // Ensure iter.hasNext, iter.getCurrent, and iter.next are available
        std::vector<std::string> requiredFuncs = {"iter.hasNext", "iter.getCurrent", "iter.next"};
        for (const auto& funcName : requiredFuncs) {
            if (functions.find(funcName) == functions.end()) {
                undefinedFunctions.insert(funcName);
            }
        }
        
        // Analyze iterable expression first (before entering loop scope)
        std::string iterableType = analyzeExpression(forStmt->iterable.get());
        
        // For now, we accept function calls (like range()) or arrays
        // The iterable type will be validated during codegen
        // TODO: Add more strict type checking when we have better type system
        
        // Enter loop scope
        loopDepth++;
        enterScope();
        
        // Declare loop variable (immutable, like 'let')
        // The type is 'int' for now (will be inferred from iterator in future)
        declareVariable(forStmt->loopVar, "int", true, 
                       forStmt->line, forStmt->column, false, "");
        
        // Analyze loop body
        for (const auto& s : forStmt->body) {
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
        
    } else if (dynamic_cast<FloatExprAST*>(expr)) {
        return "float";
        
    } else if (dynamic_cast<BooleanExprAST*>(expr)) {
        return "bool";
        
    } else if (dynamic_cast<CharacterExprAST*>(expr)) {
        return "char";
        
    } else if (dynamic_cast<StringLiteralExprAST*>(expr)) {
        return "string";  // String literals are pointers to character data
        
    } else if (auto arrayLiteral = dynamic_cast<ArrayLiteralExprAST*>(expr)) {
        // Array literal: [5]int or [1,2,3]
        if (arrayLiteral->size > 0) {
            // [size]type form - zero-initialized array
            return "[" + std::to_string(arrayLiteral->size) + "]" + arrayLiteral->elementType;
        } else {
            // [val1, val2, ...] form - value-initialized array
            if (arrayLiteral->values.empty()) {
                addError("Array literal cannot be empty",
                        expr->line, expr->column);
                return "error";
            }
            
            // Infer element type from first element
            std::string elemType = analyzeExpression(arrayLiteral->values[0].get());
            
            // Validate all elements have same type
            for (size_t i = 1; i < arrayLiteral->values.size(); i++) {
                std::string valueType = analyzeExpression(arrayLiteral->values[i].get());
                if (!typesMatch(elemType, valueType)) {
                    addError("Array element type mismatch: expected '" + elemType + "', got '" + valueType + "'" +
                            std::string("\n  Note: All array elements must have the same type"),
                            expr->line, expr->column);
                }
            }
            
            return "[" + std::to_string(arrayLiteral->values.size()) + "]" + elemType;
        }
        
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
        
        // Should be a pointer type (ptr or string)
        if (ptrType != "ptr" && ptrType != "string" && ptrType != "error") {
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
            
            // Special handling for modulo: requires both operands to be int
            if (binExpr->op == '%') {
                if (leftType != "int" || rightType != "int") {
                    addError("Modulo operator (%) requires integer operands" +
                            std::string("\n  Left operand type: ") + leftType +
                            "\n  Right operand type: " + rightType,
                            expr->line, expr->column);
                    return "error";
                }
                return "int";
            }
            
            // For other arithmetic operators: allow int or float
            // Result is float if either operand is float (implicit promotion)
            if ((leftType == "int" || leftType == "float") && 
                (rightType == "int" || rightType == "float")) {
                if (leftType == "float" || rightType == "float") {
                    return "float";
                }
                return "int";
            }
            
            std::string opName;
            if (binExpr->op == '+') opName = "addition (+)";
            else if (binExpr->op == '-') opName = "subtraction (-)";
            else if (binExpr->op == '*') opName = "multiplication (*)";
            else opName = "division (/)";
            
            addError("Arithmetic operator " + opName + " requires numeric operands (int or float)" +
                    std::string("\n  Left operand type: ") + leftType +
                    "\n  Right operand type: " + rightType,
                    expr->line, expr->column);
            return "error";
        }
        
        // Comparison operators: <, >, L (<=), G (>=), E (==), N (!=)
        if (binExpr->op == '<' || binExpr->op == '>' || binExpr->op == 'L' || 
            binExpr->op == 'G' || binExpr->op == 'E' || binExpr->op == 'N') {
            // Allow comparison between int and float (implicit promotion)
            if ((leftType == "int" || leftType == "float") && 
                (rightType == "int" || rightType == "float")) {
                return "bool";
            }
            
            if (leftType != rightType) {
                addError("Comparison operators require operands of compatible types" +
                        std::string("\n  Left operand type: ") + leftType +
                        "\n  Right operand type: " + rightType,
                        expr->line, expr->column);
                return "error";
            }
            return "bool";
        }
        
        addError("Unknown binary operator", expr->line, expr->column);
        return "error";
        
    } else if (auto unaryExpr = dynamic_cast<UnaryExprAST*>(expr)) {
        std::string operandType = analyzeExpression(unaryExpr->operand.get());
        
        // Unary + and - work on numeric types
        if (unaryExpr->op == '+' || unaryExpr->op == '-') {
            if (operandType == "int" || operandType == "float") {
                return operandType;  // Result type is same as operand type
            }
            
            std::string opName = (unaryExpr->op == '+') ? "unary plus (+)" : "unary minus (-)";
            addError("Operator " + opName + " requires numeric operand (int or float)" +
                    std::string("\n  Operand type: ") + operandType,
                    expr->line, expr->column);
            return "error";
        }
        
        addError("Unknown unary operator", expr->line, expr->column);
        return "error";
        
    } else if (auto callExpr = dynamic_cast<CallExprAST*>(expr)) {
        // Check if this is a built-in math function (intrinsics only)
        // NOTE: The list of math intrinsics is defined in lexer.cpp's keywords map
        if (Lexer::isMathIntrinsic(callExpr->callee)) {
            // Validate argument count (all intrinsics take 1 argument)
            if (callExpr->args.size() != 1) {
                addError("Function '" + callExpr->callee + "' expects exactly 1 argument", 
                        expr->line, expr->column);
                return "float";
            }
            
            // Analyze arguments
            for (auto& arg : callExpr->args) {
                std::string argType = analyzeExpression(arg.get());
                // Allow both int and float (int will be promoted)
                if (argType != "int" && argType != "float" && argType != "error") {
                    addError("Math function '" + callExpr->callee + "' expects numeric argument, got " + argType,
                            expr->line, expr->column);
                }
            }
            
            // Return type from metadata
            const MathIntrinsicInfo* info = Lexer::getMathIntrinsicInfo(callExpr->callee);
            return info ? info->returnType : "float";
        }
        
        // Try to find the function - first exact match, then unqualified lookup
        auto funcIt = functions.find(callExpr->callee);
        
        // Check if this is a qualified call (contains .)
        bool isQualifiedCall = callExpr->callee.find(".") != std::string::npos;
        
        // If the call is qualified, check if it's unnecessary
        if (isQualifiedCall && funcIt != functions.end()) {
            // Extract the unqualified name (everything after the last .)
            size_t lastDotPos = callExpr->callee.rfind(".");
            std::string unqualifiedName = callExpr->callee.substr(lastDotPos + 1);
            
            // Check how many functions match the unqualified name
            std::string searchSuffix = "." + unqualifiedName;
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
        
        // If not found and the name is unqualified (no .), try suffix matching
        if (funcIt == functions.end() && callExpr->callee.find(".") == std::string::npos) {
            std::string searchSuffix = "." + callExpr->callee;
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
            std::string expectedType = funcInfo.parameters[i].type;
            
            // Allow implicit int-to-float conversion
            bool isValidType = typesMatch(expectedType, argType) ||
                              (expectedType == "float" && argType == "int");
            
            if (!isValidType) {
                addError("Function '" + callExpr->callee + "' argument type mismatch" +
                        std::string("\n  Parameter ") + std::to_string(i + 1) + " ('" + 
                        funcInfo.parameters[i].name + "')" +
                        "\n  Expected type: " + expectedType +
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
        
        // Extract element type from array type (e.g., "[]string" -> "string", "[5]int" -> "int")
        std::string arrayType = varInfo->type;
        if (arrayType.size() > 2 && arrayType[0] == '[') {
            // Find the closing bracket
            size_t closeBracket = arrayType.find(']');
            if (closeBracket != std::string::npos && closeBracket + 1 < arrayType.size()) {
                return arrayType.substr(closeBracket + 1);
            }
        }
        // Fallback if type parsing fails
        return "int";
    
    } else if (auto memberAccessExpr = dynamic_cast<MemberAccessExprAST*>(expr)) {
        std::string objectType;
        
        // Handle both simple variable access and complex expression access
        if (memberAccessExpr->object) {
            // Complex expression (e.g., arr[0].field)
            objectType = analyzeExpression(memberAccessExpr->object.get());
        } else {
            // Simple variable access (e.g., obj.field)
            auto varInfo = lookupVariable(memberAccessExpr->objectName);
            if (!varInfo.has_value()) {
                addError("Undefined variable: '" + memberAccessExpr->objectName + "'",
                        expr->line, expr->column);
                return "error";
            }
            
            // Mark object variable as used when its member is accessed
            markVariableAsUsed(memberAccessExpr->objectName);
            objectType = varInfo->type;
        }
        
        // Check if it's a struct type
        if (lookupStruct(objectType) != nullptr) {
            const auto& structInfo = *lookupStruct(objectType);
            
            // Find the field
            for (const auto& field : structInfo.fields) {
                if (field.name == memberAccessExpr->memberName) {
                    return field.type;
                }
            }
            
            addError("Struct '" + objectType + "' has no field named '" + memberAccessExpr->memberName + "'",
                    expr->line, expr->column);
            return "error";
        }
        
        // Currently only support .length on arrays
        if (memberAccessExpr->memberName == "length") {
            // .length always returns int
            return "int";
        } else {
            addError("Unknown member: " + memberAccessExpr->memberName,
                    expr->line, expr->column);
            return "error";
        }
    } else if (auto structInitExpr = dynamic_cast<StructInitExprAST*>(expr)) {
        // Check if struct type exists
        if (lookupStruct(structInitExpr->structName) == nullptr) {
            addError("Undefined struct type: '" + structInitExpr->structName + "'",
                    expr->line, expr->column);
            return "error";
        }
        
        const auto& structInfo = *lookupStruct(structInitExpr->structName);
        
        // Check that all required fields are initialized
        std::set<std::string> initializedFields;
        for (const auto& initField : structInitExpr->fields) {
            initializedFields.insert(initField.name);
            
            // Verify field exists in struct
            bool fieldFound = false;
            std::string expectedType;
            for (const auto& structField : structInfo.fields) {
                if (structField.name == initField.name) {
                    fieldFound = true;
                    expectedType = structField.type;
                    break;
                }
            }
            
            if (!fieldFound) {
                addError("Struct '" + structInitExpr->structName + "' has no field named '" + initField.name + "'",
                        initField.line, initField.column);
            } else {
                // Type check the initializer value
                std::string valueType = analyzeExpression(initField.value.get());
                if (!typesMatch(expectedType, valueType)) {
                    addError("Type mismatch for field '" + initField.name + "': expected '" + expectedType + "', got '" + valueType + "'",
                            initField.line, initField.column);
                }
            }
        }
        
        // Check for missing fields
        for (const auto& structField : structInfo.fields) {
            if (initializedFields.find(structField.name) == initializedFields.end()) {
                addError("Missing initialization for field '" + structField.name + "' in struct '" + structInitExpr->structName + "'",
                        expr->line, expr->column);
            }
        }
        
        return structInitExpr->structName;
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

void SemanticAnalyzer::declareVariable(const std::string& name, const std::string& type, bool isImmutable, int line, int column, bool isParameter, const std::string& initialValue) {
    VariableInfo varInfo(name, type, isImmutable, line, column, isParameter, initialValue);
    variables[name] = varInfo;
    // Also store in persistent symbol table for LSP
    allDeclaredVariables[name] = varInfo;
}

// Helper function to extract literal value from an expression for display
static std::string extractLiteralValue(ExprAST* expr) {
    if (!expr) return "";
    
    if (auto numExpr = dynamic_cast<NumberExprAST*>(expr)) {
        return std::to_string(numExpr->value);
    } else if (auto floatExpr = dynamic_cast<FloatExprAST*>(expr)) {
        // Use the original literal string if available, otherwise format the value
        if (!floatExpr->literalString.empty()) {
            return floatExpr->literalString;
        }
        return std::to_string(floatExpr->value);
    } else if (auto boolExpr = dynamic_cast<BooleanExprAST*>(expr)) {
        return boolExpr->value ? "true" : "false";
    } else if (auto strExpr = dynamic_cast<StringLiteralExprAST*>(expr)) {
        return "\"" + strExpr->value + "\"";
    } else if (auto charExpr = dynamic_cast<CharacterExprAST*>(expr)) {
        return "'" + std::string(1, charExpr->value) + "'";
    }
    
    return ""; // Not a simple literal
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
    
    // ptr and string are interchangeable (string is an alias for ptr)
    if ((type1 == "ptr" && type2 == "string") || (type1 == "string" && type2 == "ptr")) {
        return true;
    }
    
    // Check if both are array types and extract element types
    if (type1[0] == '[' && type2[0] == '[') {
        // Extract element types (everything after the last ']')
        size_t bracket1 = type1.find(']');
        size_t bracket2 = type2.find(']');
        
        if (bracket1 != std::string::npos && bracket2 != std::string::npos) {
            std::string elem1 = type1.substr(bracket1 + 1);
            std::string elem2 = type2.substr(bracket2 + 1);
            
            // Array types match if element types match, regardless of size
            // Use recursive call to handle ptr/string equivalence
            return typesMatch(elem1, elem2);
        }
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
        
        if (!varInfo.isUsed) {
            if (varInfo.isParameter) {
                addWarning("The parameter '" + varInfo.name + "' is declared but its value is never used",
                          varInfo.line, varInfo.column, "unused-parameter");
            } else {
                addWarning("The variable '" + varInfo.name + "' is assigned but its value is never used",
                          varInfo.line, varInfo.column, "unused-variable");
            }
        }
    }
}

std::map<std::string, VariableInfo> SemanticAnalyzer::getAllVariables() const {
    // Return the persistent symbol table that contains all declared variables
    return allDeclaredVariables;
}
