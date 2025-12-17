#include "../lexer.h"
#include "../semantic_analyzer.h"
#include "../types/type_conversion.h"
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
			// Store inferred type back into AST for codegen
			varDecl->type = actualType;
		}

		// For array<T> struct types, register array methods with the element type
		if (maxon::TypeConversion::isArrayStructType(actualType)) {
			std::string elemType = maxon::TypeConversion::getArrayStructElementType(actualType);
			// Also instantiate synthesized default methods from interface implementations (e.g., Collection.map)
			std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
			instantiateGenericStructMethods("array", actualType, typeBindings);
		}

		// For map<K,V> struct types, register map methods with the key/value types
		if (maxon::TypeConversion::isMapStructType(actualType)) {
			std::string keyType = maxon::TypeConversion::getMapKeyType(actualType);
			std::string valueType = maxon::TypeConversion::getMapValueType(actualType);
			std::map<std::string, std::string> typeBindings = {{"Key", keyType}, {"Value", valueType}};
			instantiateGenericStructMethods("map", actualType, typeBindings);
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
			// Store inferred type back into AST for codegen
			letDecl->type = actualType;
		}

		// For array types, instantiate generic methods (same as var declarations)
		if (maxon::TypeConversion::isArrayStructType(actualType)) {
			std::string elemType = maxon::TypeConversion::getArrayStructElementType(actualType);
			std::map<std::string, std::string> typeBindings = {{"Element", elemType}};
			instantiateGenericStructMethods("array", actualType, typeBindings);
		}

		// For map types, instantiate generic methods (same as var declarations)
		if (maxon::TypeConversion::isMapStructType(actualType)) {
			std::string keyType = maxon::TypeConversion::getMapKeyType(actualType);
			std::string valueType = maxon::TypeConversion::getMapValueType(actualType);
			std::map<std::string, std::string> typeBindings = {{"Key", keyType}, {"Value", valueType}};
			instantiateGenericStructMethods("map", actualType, typeBindings);
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
			// Check ownership state - cannot assign to a moved variable
			if (varInfo->ownershipState == OwnershipState::Moved) {
				std::string errorMsg = "Cannot assign to variable '" + assign->name + "' after ownership was transferred";
				if (varInfo->moveInfo.has_value()) {
					errorMsg += "\n  Ownership transferred to function '" + varInfo->moveInfo->targetFunction +
								"' at line " + std::to_string(varInfo->moveInfo->line);
				}
				errorMsg += "\n  Note: Once ownership is transferred, the variable cannot be used or reassigned";
				addError(errorMsg, stmt->line, stmt->column, "assign-after-move");
				// Skip further analysis to avoid cascading errors from RHS
				return;
			}

			// Check if variable is immutable
			if (varInfo->isImmutable) {
				if (varInfo->isLoopVariable) {
					addError("Cannot assign to loop variable '" + assign->name + "'" +
								 std::string("\n  Loop variable declared at line ") + std::to_string(varInfo->line) +
								 ", column " + std::to_string(varInfo->column) +
								 "\n  Note: Loop iteration variables are immutable and cannot be reassigned",
							 stmt->line, stmt->column);
				} else {
					addError("Cannot assign to read-only variable '" + assign->name + "'" +
								 std::string("\n  Variable declared with 'let' at line ") + std::to_string(varInfo->line) +
								 ", column " + std::to_string(varInfo->column) +
								 "\n  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable variables",
							 stmt->line, stmt->column);
				}
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
			if (maxon::TypeConversion::isArrayType(arrayType)) {
				std::string elementType = maxon::TypeConversion::getArrayElementType(arrayType);

				if (lookupStruct(elementType) != nullptr) {
					// Verify member exists and check immutability
					const auto &structInfo = structs.at(elementType);
					bool memberFound = false;
					bool fieldIsImmutable = false;
					int fieldLine = 0;
					for (const auto &field : structInfo.fields) {
						if (field.name == arrayMemberAssign->memberName) {
							memberFound = true;
							fieldIsImmutable = field.isImmutable;
							fieldLine = field.line;
							break;
						}
					}

					if (!memberFound) {
						addError("Type '" + elementType + "' has no field named '" + arrayMemberAssign->memberName + "'",
								 stmt->line, stmt->column);
					} else if (fieldIsImmutable) {
						addError("Cannot assign to immutable field '" + arrayMemberAssign->memberName +
									 "' of type '" + elementType + "'" +
									 "\n  Field declared with 'let' at line " + std::to_string(fieldLine) +
									 "\n  Note: Fields declared with 'let' are immutable. Use 'var' for mutable fields",
								 stmt->line, stmt->column);
					}
				} else {
					addError("Cannot access member '" + arrayMemberAssign->memberName + "' on non-type array element type '" + elementType + "'",
							 stmt->line, stmt->column);
				}
			}
		}

		// Analyze value expression
		analyzeExpression(arrayMemberAssign->value.get());
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
				addError("Cannot assign to field of read-only type '" + memberAssign->objectName + "'" +
							 std::string("\n  Variable declared with 'let' at line ") + std::to_string(varInfo->line) +
							 ", column " + std::to_string(varInfo->column) +
							 "\n  Note: Variables declared with 'let' are immutable (read-only). Use 'var' for mutable types",
						 stmt->line, stmt->column);
			}

			// Get the struct type and verify the member exists
			std::string structType = varInfo->type;
			auto *structInfo = lookupStruct(structType);
			if (structInfo == nullptr) {
				addError("Cannot access member '" + memberAssign->memberName + "' on non-type '" + structType + "'",
						 stmt->line, stmt->column);
			} else {
				// Verify member exists
				bool memberFound = false;
				std::string memberType;
				bool fieldIsImmutable = false;
				int fieldLine = 0;
				for (const auto &field : structInfo->fields) {
					if (field.name == memberAssign->memberName) {
						memberFound = true;
						memberType = field.type;
						fieldIsImmutable = field.isImmutable;
						fieldLine = field.line;
						break;
					}
				}

				if (!memberFound) {
					addError("Type '" + structType + "' has no field named '" + memberAssign->memberName + "'",
							 stmt->line, stmt->column);
				} else {
					// Check if the field itself is immutable (declared with 'let')
					if (fieldIsImmutable) {
						addError("Cannot assign to immutable field '" + memberAssign->memberName +
									 "' of type '" + structType + "'" +
									 "\n  Field declared with 'let' at line " + std::to_string(fieldLine) +
									 "\n  Note: Fields declared with 'let' are immutable. Use 'var' for mutable fields",
								 stmt->line, stmt->column);
					}

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
	} else if (auto memberArrayAssign = dynamic_cast<MemberArrayAssignStmtAST *>(stmt)) {
		// Struct member array element assignment: obj.arrayField[i] = value
		auto varInfo = lookupVariable(memberArrayAssign->objectName);
		if (!varInfo.has_value()) {
			addError("Undefined variable: '" + memberArrayAssign->objectName + "'" +
						 std::string("\n  Note: Variable must be declared with 'var' or 'let' before use"),
					 stmt->line, stmt->column);
		} else {
			markVariableAsUsed(memberArrayAssign->objectName);

			if (varInfo->isImmutable) {
				addError("Cannot assign to field of read-only type '" + memberArrayAssign->objectName + "'",
						 stmt->line, stmt->column);
			}

			std::string structType = varInfo->type;
			auto *structInfo = lookupStruct(structType);
			if (structInfo == nullptr) {
				addError("Cannot access member '" + memberArrayAssign->memberName + "' on non-type '" + structType + "'",
						 stmt->line, stmt->column);
			} else {
				// Find the array field
				bool memberFound = false;
				std::string memberType;
				bool fieldIsImmutable = false;
				int fieldLine = 0;
				for (const auto &field : structInfo->fields) {
					if (field.name == memberArrayAssign->memberName) {
						memberFound = true;
						memberType = field.type;
						fieldIsImmutable = field.isImmutable;
						fieldLine = field.line;
						break;
					}
				}

				if (!memberFound) {
					addError("Type '" + structType + "' has no field named '" + memberArrayAssign->memberName + "'",
							 stmt->line, stmt->column);
				} else {
					// Check if the field itself is immutable (declared with 'let')
					if (fieldIsImmutable) {
						addError("Cannot assign to immutable field '" + memberArrayAssign->memberName +
									 "' of type '" + structType + "'" +
									 "\n  Field declared with 'let' at line " + std::to_string(fieldLine) +
									 "\n  Note: Fields declared with 'let' are immutable. Use 'var' for mutable fields",
								 stmt->line, stmt->column);
					}

					// Verify it's an array type
					if (memberType.empty() || memberType[0] != '[') {
						addError("Field '" + memberArrayAssign->memberName + "' is not an array type",
								 stmt->line, stmt->column);
					} else {
						// Analyze index expression
						std::string indexType = analyzeExpression(memberArrayAssign->index.get());
						if (indexType != "int" && indexType != "error") {
							addError("Array index must be an integer, found '" + indexType + "'",
									 stmt->line, stmt->column);
						}

						// Extract element type and check value type
						size_t closeBracket = memberType.find(']');
						if (closeBracket != std::string::npos && closeBracket + 1 < memberType.size()) {
							std::string elemType = memberType.substr(closeBracket + 1);
							std::string valueType = analyzeExpression(memberArrayAssign->value.get());
							if (!typesMatch(elemType, valueType)) {
								addError("Type mismatch: cannot assign '" + valueType + "' to array element of type '" + elemType + "'",
										 stmt->line, stmt->column);
							}
						}
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

		// Capture ownership state before analyzing branches
		auto preIfOwnership = captureOwnershipStates();

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

		// Capture ownership state after then branch
		auto thenOwnership = captureOwnershipStates();

		// Restore to pre-if state for else analysis
		restoreOwnershipStates(preIfOwnership);

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

		// Capture ownership state after else branch
		auto elseOwnership = captureOwnershipStates();

		// Merge ownership: if moved in EITHER branch, consider it moved
		mergeOwnershipStates(thenOwnership, elseOwnership);
	} else if (auto ifLet = dynamic_cast<IfLetStmtAST *>(stmt)) {
		// Analyze optional expression
		std::string optionalType = analyzeExpression(ifLet->optionalExpr.get());

		if (!isOptionalType(optionalType)) {
			addError("'if let' requires optional type, got '" + optionalType + "'" +
						 std::string("\n  Note: Use 'if let' only with optional types (T or nil)"),
					 stmt->line, stmt->column);
		}

		std::string unwrappedType = unwrapOptionalType(optionalType);

		// Check for duplicate block identifiers at this scope level
		declareBlockId(ifLet->blockId, stmt->line, stmt->column);

		// Analyze then body with binding in scope
		enterScope();
		blockIdStack.push_back(std::set<std::string>());

		// Declare the unwrapped variable (immutable like 'let')
		declareVariable(ifLet->bindingName, unwrappedType, true, stmt->line, stmt->column);

		for (const auto &s : ifLet->thenBody) {
			analyzeStatement(s.get(), currentFunctionReturnType);
		}

		blockIdStack.pop_back();
		checkUnusedVariables();
		exitScope();

		// Analyze else body (binding NOT in scope)
		if (!ifLet->elseBody.empty()) {
			enterScope();
			blockIdStack.push_back(std::set<std::string>());

			for (const auto &s : ifLet->elseBody) {
				analyzeStatement(s.get(), currentFunctionReturnType);
			}

			blockIdStack.pop_back();
			checkUnusedVariables();
			exitScope();
		}
	} else if (auto elseUnwrap = dynamic_cast<ElseUnwrapStmtAST *>(stmt)) {
		// Analyze the optional expression
		std::string optionalType = analyzeExpression(elseUnwrap->optionalExpr.get());

		// Verify it's optional
		if (!isOptionalType(optionalType)) {
			addError("'else' unwrapping requires an optional type, got '" + optionalType + "'" +
						 std::string("\n  Note: Use 'var x = expr else ...' only with optional types (T or nil)"),
					 stmt->line, stmt->column);
			return;
		}

		std::string unwrappedType = unwrapOptionalType(optionalType);

		// If explicit type provided, verify it matches
		if (!elseUnwrap->declaredType.empty() &&
			!typesMatch(elseUnwrap->declaredType, unwrappedType)) {
			addError("Type mismatch: declared type '" + elseUnwrap->declaredType +
						 "' does not match unwrapped type '" + unwrappedType + "'",
					 stmt->line, stmt->column);
			return;
		}

		// PRE-DECLARE the variable in scope (mutable, initialized on both paths)
		// This allows the else block to assign to it
		declareVariable(elseUnwrap->name, unwrappedType, false, stmt->line, stmt->column);

		// Track that this variable needs initialization in else block
		std::string savedVarName = elseUnwrap->name;

		// Analyze else body
		enterScope();
		bool assignmentFound = false;

		for (const auto &s : elseUnwrap->elseBody) {
			// Check if this statement assigns to the variable
			if (auto assignStmt = dynamic_cast<AssignStmtAST *>(s.get())) {
				if (assignStmt->name == savedVarName) {
					assignmentFound = true;
				}
			}
			analyzeStatement(s.get(), currentFunctionReturnType);
		}
		exitScope();

		// CRITICAL CHECK: Verify the variable was assigned in else block
		if (!assignmentFound) {
			addError("Variable '" + savedVarName + "' must be assigned a value in the else block" +
						 std::string("\n  The else block is only executed when the optional is nil") +
						 "\n  Note: You must provide a default value by assigning to '" + savedVarName + "' in the else block",
					 stmt->line, stmt->column);
		}
	} else if (auto guardLet = dynamic_cast<GuardLetStmtAST *>(stmt)) {
		// Guard-let: let x = optionalExpr or 'label' ... end 'label'
		// The guard body MUST exit scope (return, break, continue)
		// If optional has value, it's unwrapped and bound to x

		// Analyze the optional expression
		std::string optionalType = analyzeExpression(guardLet->optionalExpr.get());

		// Verify it's optional
		if (!isOptionalType(optionalType)) {
			addError("Guard-let 'or' requires an optional type, got '" + optionalType + "'" +
						 std::string("\n  Note: Use 'let x = expr or ...' only with optional types (T or nil)"),
					 stmt->line, stmt->column);
			return;
		}

		std::string unwrappedType = unwrapOptionalType(optionalType);

		// Check for duplicate block identifiers at this scope level
		declareBlockId(guardLet->blockId, stmt->line, stmt->column);

		// Analyze guard body (must exit scope)
		enterScope();
		blockIdStack.push_back(std::set<std::string>());

		bool hasExit = false;
		for (const auto &s : guardLet->guardBody) {
			analyzeStatement(s.get(), currentFunctionReturnType);
			// Check if this statement is an exit (return, break, continue)
			if (dynamic_cast<ReturnStmtAST *>(s.get()) ||
				dynamic_cast<BreakStmtAST *>(s.get()) ||
				dynamic_cast<ContinueStmtAST *>(s.get())) {
				hasExit = true;
			}
		}

		blockIdStack.pop_back();
		checkUnusedVariables();
		exitScope();

		// CRITICAL CHECK: Verify guard body exits scope
		if (!hasExit) {
			addError("Guard block must exit scope with return, break, or continue" +
						 std::string("\n  The guard block is executed when the optional is nil") +
						 "\n  Note: Add 'return' at the end of the guard block to exit the function",
					 stmt->line, stmt->column);
		}

		// AFTER guard block, declare the unwrapped variable (immutable)
		// This is only available after the guard has verified the optional has a value
		declareVariable(guardLet->name, unwrappedType, true, stmt->line, stmt->column);

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

		// Capture ownership state before loop
		auto preLoopOwnership = captureOwnershipStates();

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

		// Capture ownership state after loop body
		auto postLoopOwnership = captureOwnershipStates();

		// Merge: if moved in loop body, it's moved after loop (loop may execute 0+ times)
		mergeOwnershipStates(preLoopOwnership, postLoopOwnership);
	} else if (auto forStmt = dynamic_cast<ForStmtAST *>(stmt)) {
		// For-loops require iterator next() method from stdlib (Iterable interface)
		// The method returns Element or nil
		std::vector<std::string> requiredFuncs = {"Iterator.next"};
		for (const auto &funcName : requiredFuncs) {
			if (functions.find(funcName) == functions.end()) {
				undefinedFunctions.insert(funcName);
			}
		}

		// Analyze iterable expression first (before entering loop scope)
		std::string iterableType = analyzeExpression(forStmt->iterable.get());

		// Validate that the iterable expression is actually iterable
		if (!isIterableType(iterableType, forStmt->iterable.get())) {
			addError("Cannot iterate over type '" + iterableType + "'" +
						 std::string("\n  For-loops require an iterable type: array, string, range()"),
					 forStmt->iterable->line, forStmt->iterable->column);
		}

		// Check for duplicate block identifiers at this scope level
		declareBlockId(forStmt->blockId, stmt->line, stmt->column);

		// Capture ownership state before loop
		auto preLoopOwnership = captureOwnershipStates();

		// Enter loop scope
		loopDepth++;
		loopLabelStack.push_back(forStmt->blockId);
		enterScope();

		// Enter nested block scope for nested blocks within the for block
		blockIdStack.push_back(std::set<std::string>());

		// Declare loop variable (immutable, like 'let')
		// Infer type from iterable: array element type, or Element associated type for Iterable structs
		std::string loopVarType = "int"; // Default for range() iteration
		if (maxon::TypeConversion::isArrayStructType(iterableType)) {
			// array<T> struct type - extract element type
			loopVarType = maxon::TypeConversion::getArrayStructElementType(iterableType);
		} else if (maxon::TypeConversion::isArrayType(iterableType)) {
			// Internal array types (_ManagedArray<T>, _StaticArray<N, T>) - extract element type
			loopVarType = maxon::TypeConversion::getArrayElementType(iterableType);
		} else {
			// Check if this is a struct type with an Element associated type
			auto structIt = structs.find(iterableType);
			if (structIt != structs.end()) {
				auto typeIt = structIt->second.typeAssignments.find("Element");
				if (typeIt != structIt->second.typeAssignments.end()) {
					loopVarType = typeIt->second;
				}
			}
		}
		declareVariable(forStmt->loopVar, loopVarType, true,
						forStmt->line, forStmt->column, false, "", true);

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

		// Capture ownership state after loop body
		auto postLoopOwnership = captureOwnershipStates();

		// Merge: if moved in loop body, it's moved after loop (loop may execute 0+ times)
		mergeOwnershipStates(preLoopOwnership, postLoopOwnership);
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
	} else if (auto matchStmt = dynamic_cast<MatchStmtAST *>(stmt)) {
		// Analyze scrutinee expression
		std::string scrutineeType = analyzeExpression(matchStmt->scrutinee.get());

		// Check for duplicate block identifiers at this scope level
		declareBlockId(matchStmt->blockId, stmt->line, stmt->column);

		// Track patterns for duplicate detection
		std::set<std::string> seenPatterns;
		bool hasDefault = false;
		bool defaultNotLast = false;

		// Check for exhaustiveness if matching on enum type
		const EnumInfo *enumInfo = lookupEnum(scrutineeType);
		std::set<std::string> coveredCases;

		for (size_t i = 0; i < matchStmt->cases.size(); i++) {
			const auto &matchCase = matchStmt->cases[i];

			// Check default case constraints
			if (matchCase.isDefault) {
				if (hasDefault) {
					addError("Duplicate 'default' case in match statement",
							 matchCase.line, matchCase.column);
				}
				hasDefault = true;
				if (i != matchStmt->cases.size() - 1) {
					defaultNotLast = true;
				}
			} else if (matchCase.isEnumCasePattern) {
				// Enum case pattern with bindings: case success(value) then ...
				if (!enumInfo) {
					addError("Cannot use 'case' pattern on non-enum type '" + scrutineeType + "'",
							 matchCase.line, matchCase.column);
				} else {
					// Find the case in the enum
					const EnumCaseInfo *caseInfo = nullptr;
					for (const auto &ec : enumInfo->cases) {
						if (ec.name == matchCase.enumCaseName) {
							caseInfo = &ec;
							break;
						}
					}

					if (!caseInfo) {
						addError("Unknown case '" + matchCase.enumCaseName + "' for enum '" + scrutineeType + "'",
								 matchCase.line, matchCase.column);
					} else {
						// Validate binding count matches associated values
						if (matchCase.bindings.size() != caseInfo->associatedValues.size()) {
							addError("Wrong number of bindings for case '" + matchCase.enumCaseName +
										 "': expected " + std::to_string(caseInfo->associatedValues.size()) +
										 ", got " + std::to_string(matchCase.bindings.size()),
									 matchCase.line, matchCase.column);
						}

						// Track for exhaustiveness
						coveredCases.insert(matchCase.enumCaseName);

						// Check for duplicate patterns
						std::string patternStr = scrutineeType + "." + matchCase.enumCaseName;
						if (seenPatterns.find(patternStr) != seenPatterns.end()) {
							addError("Duplicate pattern '" + patternStr + "' in match",
									 matchCase.line, matchCase.column);
						}
						seenPatterns.insert(patternStr);

						// Check fallthrough constraints
						if (matchCase.hasFallthrough) {
							if (dynamic_cast<ReturnStmtAST *>(matchCase.statement.get())) {
								addError("Cannot combine 'fallthrough' with 'return' statement",
										 matchCase.line, matchCase.column);
							}
						}

						// Analyze case body with bindings in scope
						enterScope();
						blockIdStack.push_back(std::set<std::string>());

						// Declare binding variables with their types from associated values
						for (size_t j = 0; j < matchCase.bindings.size() && j < caseInfo->associatedValues.size(); j++) {
							const std::string &bindingName = matchCase.bindings[j];
							const std::string &bindingType = caseInfo->associatedValues[j].type;
							declareVariable(bindingName, bindingType, true /* immutable */,
											matchCase.line, matchCase.column);
						}

						analyzeStatement(matchCase.statement.get(), currentFunctionReturnType);
						blockIdStack.pop_back();
						checkUnusedVariables();
						exitScope();
					}
				}
				continue; // Skip the regular analysis below
			} else {
				// Analyze each pattern
				for (const auto &pattern : matchCase.patterns) {
					std::string patternType = analyzeExpression(pattern.get());

					// Check pattern type matches scrutinee type
					if (!typesMatch(scrutineeType, patternType)) {
						addError("Pattern type '" + patternType + "' does not match scrutinee type '" + scrutineeType + "'",
								 pattern->line, pattern->column);
					}

					// Check for duplicate patterns (convert to string for comparison)
					std::string patternStr;
					if (auto numExpr = dynamic_cast<NumberExprAST *>(pattern.get())) {
						patternStr = std::to_string(numExpr->value);
					} else if (auto strExpr = dynamic_cast<StringLiteralExprAST *>(pattern.get())) {
						patternStr = "\"" + strExpr->value + "\"";
					} else if (auto memberExpr = dynamic_cast<MemberAccessExprAST *>(pattern.get())) {
						if (memberExpr->isEnumCase()) {
							patternStr = memberExpr->resolvedEnumName + "." + memberExpr->resolvedEnumCaseName;
							coveredCases.insert(memberExpr->resolvedEnumCaseName);
						} else {
							patternStr = memberExpr->objectName + "." + memberExpr->memberName;
							coveredCases.insert(memberExpr->memberName);
						}
					}

					if (!patternStr.empty() && seenPatterns.find(patternStr) != seenPatterns.end()) {
						addError("Duplicate pattern '" + patternStr + "' in match",
								 pattern->line, pattern->column);
					}
					seenPatterns.insert(patternStr);
				}
			}

			// Check fallthrough constraints
			if (matchCase.hasFallthrough) {
				// Check if statement is a return (not allowed with fallthrough)
				if (dynamic_cast<ReturnStmtAST *>(matchCase.statement.get())) {
					addError("Cannot combine 'fallthrough' with 'return' statement",
							 matchCase.line, matchCase.column);
				}
			}

			// Analyze the case statement
			enterScope();
			blockIdStack.push_back(std::set<std::string>());
			analyzeStatement(matchCase.statement.get(), currentFunctionReturnType);
			blockIdStack.pop_back();
			checkUnusedVariables();
			exitScope();
		}

		// Report default-not-last error
		if (defaultNotLast) {
			addError("'default' case must be the last case in match",
					 stmt->line, stmt->column);
		}

		// Exhaustiveness check for enum types
		if (enumInfo != nullptr && !hasDefault) {
			std::vector<std::string> missingCases;
			for (const auto &enumCase : enumInfo->cases) {
				if (coveredCases.find(enumCase.name) == coveredCases.end()) {
					missingCases.push_back(enumCase.name);
				}
			}
			if (!missingCases.empty()) {
				std::string missingList;
				for (size_t i = 0; i < missingCases.size(); i++) {
					if (i > 0)
						missingList += ", ";
					missingList += missingCases[i];
				}
				addError("Match on enum '" + scrutineeType + "' is not exhaustive\n  Missing cases: " + missingList,
						 stmt->line, stmt->column);
			} else {
				// All enum cases are covered - mark as exhaustive for return path validation
				matchStmt->isExhaustive = true;
			}
		}

		// If there's a default case, the match is also exhaustive
		if (hasDefault) {
			matchStmt->isExhaustive = true;
		}
	} else if (auto returnStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		// Analyze return value
		if (returnStmt->value) {
			std::string returnType = analyzeExpression(returnStmt->value.get());

			// Returning nil
			if (returnType == "nil") {
				if (!isOptionalType(currentFunctionReturnType)) {
					addError("Cannot return 'nil' from non-optional return type" +
								 std::string("\n  Function return type: ") + currentFunctionReturnType +
								 "\n  Note: To return nil, change the function return type to '" +
								 currentFunctionReturnType + " or nil'",
							 stmt->line, stmt->column);
				}
				// nil can be returned to any optional type
				return;
			}

			// Returning value to optional type (implicit wrap)
			if (isOptionalType(currentFunctionReturnType)) {
				std::string unwrapped = unwrapOptionalType(currentFunctionReturnType);
				if (typesMatch(unwrapped, returnType)) {
					// Allow implicit wrapping of non-nil value to optional
					return;
				}
				// Fall through to normal type checking for mismatch error
			}

			// Standard type checking
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
		return "'" + charExpr->value + "'";
	}

	return ""; // Not a simple literal
}
