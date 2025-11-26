#include "../lexer.h"
#include "../semantic_analyzer.h"
#include <algorithm>

// Forward declaration of helper function
static std::string extractLiteralValue(ExprAST *expr);

// Statement analysis implementation
void SemanticAnalyzer::analyzeStatement(StmtAST *stmt, const std::string &currentFunctionReturnType) {
	if (auto varDecl = dynamic_cast<VarDeclStmtAST *>(stmt)) {
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

	} else if (auto letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
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

	} else if (auto assign = dynamic_cast<AssignStmtAST *>(stmt)) {
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

	} else if (auto arrayAssign = dynamic_cast<ArrayAssignStmtAST *>(stmt)) {
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

	} else if (auto arrayMemberAssign = dynamic_cast<ArrayMemberAssignStmtAST *>(stmt)) {
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
						const auto &structInfo = structs.at(elementType);
						bool memberFound = false;
						for (const auto &field : structInfo.fields) {
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

	} else if (auto memberAssign = dynamic_cast<MemberAssignStmtAST *>(stmt)) {
		// Struct member assignment: obj.field = value
		auto varInfo = lookupVariable(memberAssign->objectName);
		if (!varInfo.has_value()) {
			addError("Undefined variable: '" + memberAssign->objectName + "'" +
						 std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
					 stmt->line, stmt->column);
		} else {
			// Mark variable as used
			markVariableAsUsed(memberAssign->objectName);

			// Check if variable is immutable
			if (varInfo->isImmutable) {
				addError("Cannot assign to field of read-only struct '" + memberAssign->objectName + "'" +
							 std::string("\n  Variable declared with 'let' at line ") + std::to_string(varInfo->line) +
							 ", column " + std::to_string(varInfo->column) +
							 "\n  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable structs",
						 stmt->line, stmt->column);
			}

			// Get the struct type and verify the member exists
			std::string structType = varInfo->type;
			auto *structInfo = lookupStruct(structType);
			if (structInfo == nullptr) {
				addError("Cannot access member '" + memberAssign->memberName + "' on non-struct type '" + structType + "'",
						 stmt->line, stmt->column);
			} else {
				// Verify member exists
				bool memberFound = false;
				std::string memberType;
				for (const auto &field : structInfo->fields) {
					if (field.name == memberAssign->memberName) {
						memberFound = true;
						memberType = field.type;
						break;
					}
				}

				if (!memberFound) {
					addError("Struct '" + structType + "' has no field named '" + memberAssign->memberName + "'",
							 stmt->line, stmt->column);
				} else {
					// Check type compatibility
					std::string valueType = analyzeExpression(memberAssign->value.get());
					if (!typesMatch(memberType, valueType)) {
						addError("Type mismatch: cannot assign '" + valueType + "' to field '" +
									 memberAssign->memberName + "' of type '" + memberType + "'",
								 stmt->line, stmt->column);
					}
				}
			}
		}

	} else if (auto exprStmt = dynamic_cast<ExprStmtAST *>(stmt)) {
		// Analyze the expression (e.g., function call)
		analyzeExpression(exprStmt->expression.get());

	} else if (auto ifStmt = dynamic_cast<IfStmtAST *>(stmt)) {
		// Analyze condition
		std::string condType = analyzeExpression(ifStmt->condition.get());
		if (condType != "bool" && condType != "int") {
			addError("If condition must be a boolean or integer expression" +
						 std::string("\n  Found type: ") + condType +
						 "\n  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)",
					 stmt->line, stmt->column);
		}

		// Check for duplicate block identifiers at this scope level
		declareBlockId(ifStmt->blockId, stmt->line, stmt->column);

		// Analyze then block
		enterScope();

		// Enter nested block scope for nested blocks within the if block
		blockIdStack.push_back(std::set<std::string>());

		for (const auto &s : ifStmt->thenBody) {
			analyzeStatement(s.get(), currentFunctionReturnType);
		}

		// Exit nested block scope
		blockIdStack.pop_back();

		// Check for unused variables before exiting scope
		checkUnusedVariables();
		exitScope();

		// Analyze else block
		if (!ifStmt->elseBody.empty()) {
			enterScope();

			// Enter nested block scope for else block
			blockIdStack.push_back(std::set<std::string>());

			for (const auto &s : ifStmt->elseBody) {
				analyzeStatement(s.get(), currentFunctionReturnType);
			}

			// Exit nested block scope
			blockIdStack.pop_back();

			// Check for unused variables before exiting scope
			checkUnusedVariables();
			exitScope();
		}

	} else if (auto whileStmt = dynamic_cast<WhileStmtAST *>(stmt)) {
		// Analyze condition
		std::string condType = analyzeExpression(whileStmt->condition.get());
		if (condType != "bool" && condType != "int") {
			addError("While condition must be a boolean or integer expression" +
						 std::string("\n  Found type: ") + condType +
						 "\n  Note: Conditions should evaluate to true/false or use comparison operators (=, !=, <, >, <=, >=)",
					 stmt->line, stmt->column);
		}

		// Check for duplicate block identifiers at this scope level
		declareBlockId(whileStmt->blockId, stmt->line, stmt->column);

		// Enter loop scope
		loopDepth++;
		loopLabelStack.push_back(whileStmt->blockId);
		enterScope();

		// Enter nested block scope for nested blocks within the while block
		blockIdStack.push_back(std::set<std::string>());

		// Analyze loop body
		for (const auto &s : whileStmt->body) {
			analyzeStatement(s.get(), currentFunctionReturnType);
		}

		// Exit nested block scope
		blockIdStack.pop_back();

		// Check for unused variables before exiting scope
		checkUnusedVariables();

		// Exit loop scope
		exitScope();
		loopDepth--;
		loopLabelStack.pop_back();

	} else if (auto forStmt = dynamic_cast<ForStmtAST *>(stmt)) {
		// For-loops require iterator functions from stdlib
		// Ensure iter.hasNext, iter.getCurrent, and iter.next are available
		std::vector<std::string> requiredFuncs = {"iter.hasNext", "iter.getCurrent", "iter.next"};
		for (const auto &funcName : requiredFuncs) {
			if (functions.find(funcName) == functions.end()) {
				undefinedFunctions.insert(funcName);
			}
		}

		// Analyze iterable expression first (before entering loop scope)
		std::string iterableType = analyzeExpression(forStmt->iterable.get());

		// For now, we accept function calls (like range()) or arrays
		// The iterable type will be validated during codegen
		// TODO: Add more strict type checking when we have better type system

		// Check for duplicate block identifiers at this scope level
		declareBlockId(forStmt->blockId, stmt->line, stmt->column);

		// Enter loop scope
		loopDepth++;
		loopLabelStack.push_back(forStmt->blockId);
		enterScope();

		// Enter nested block scope for nested blocks within the for block
		blockIdStack.push_back(std::set<std::string>());

		// Declare loop variable (immutable, like 'let')
		// The type is 'int' for now (will be inferred from iterator in future)
		declareVariable(forStmt->loopVar, "int", true,
						forStmt->line, forStmt->column, false, "");

		// Analyze loop body
		for (const auto &s : forStmt->body) {
			analyzeStatement(s.get(), currentFunctionReturnType);
		}

		// Exit nested block scope
		blockIdStack.pop_back();

		// Check for unused variables before exiting scope
		checkUnusedVariables();

		// Exit loop scope
		exitScope();
		loopDepth--;
		loopLabelStack.pop_back();

	} else if (auto breakStmt = dynamic_cast<BreakStmtAST *>(stmt)) {
		// Validate break is inside a loop
		if (loopDepth == 0) {
			addError("'break' statement must be inside a loop" +
						 std::string("\n  Note: 'break' can only be used within 'while' loops"),
					 stmt->line, stmt->column);
		} else if (!breakStmt->targetLabel.empty()) {
			// Verify label exists in enclosing loops
			bool found = false;
			for (const auto &label : loopLabelStack) {
				if (label == breakStmt->targetLabel) {
					found = true;
					break;
				}
			}
			if (!found) {
				addError("Break target label '" + breakStmt->targetLabel +
							 "' does not match any enclosing loop",
						 stmt->line, stmt->column);
			}
		}

	} else if (auto continueStmt = dynamic_cast<ContinueStmtAST *>(stmt)) {
		// Validate continue is inside a loop
		if (loopDepth == 0) {
			addError("'continue' statement must be inside a loop" +
						 std::string("\n  Note: 'continue' can only be used within 'while' loops"),
					 stmt->line, stmt->column);
		} else if (!continueStmt->targetLabel.empty()) {
			// Verify label exists in enclosing loops
			bool found = false;
			for (const auto &label : loopLabelStack) {
				if (label == continueStmt->targetLabel) {
					found = true;
					break;
				}
			}
			if (!found) {
				addError("Continue target label '" + continueStmt->targetLabel +
							 "' does not match any enclosing loop",
						 stmt->line, stmt->column);
			}
		}

	} else if (auto returnStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
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

static std::string extractLiteralValue(ExprAST *expr) {
	if (!expr)
		return "";

	if (auto numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return std::to_string(numExpr->value);
	} else if (auto floatExpr = dynamic_cast<FloatExprAST *>(expr)) {
		// Use the original literal string if available, otherwise format the value
		if (!floatExpr->literalString.empty()) {
			return floatExpr->literalString;
		}
		return std::to_string(floatExpr->value);
	} else if (auto boolExpr = dynamic_cast<BooleanExprAST *>(expr)) {
		return boolExpr->value ? "true" : "false";
	} else if (auto strExpr = dynamic_cast<StringLiteralExprAST *>(expr)) {
		return "\"" + strExpr->value + "\"";
	} else if (auto charExpr = dynamic_cast<CharacterExprAST *>(expr)) {
		return "'" + std::string(1, charExpr->value) + "'";
	}

	return ""; // Not a simple literal
}
