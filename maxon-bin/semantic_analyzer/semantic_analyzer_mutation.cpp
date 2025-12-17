#include "../semantic_analyzer.h"

// === Ownership / Mutation Analysis Implementation ===
// This file implements the mutation analysis pass that determines which
// parameters each function mutates. This information is used during
// semantic analysis to track ownership transfers at call sites.

// Run the mutation analysis pass on all functions in the program
// This must be called before the main semantic analysis pass (Pass 3)
void SemanticAnalyzer::runMutationAnalysisPass(ProgramAST *program) {
	logTrace("Running mutation analysis pass...");

	// Analyze standalone functions
	for (const auto &func : program->functions) {
		analyzeFunctionMutations(func.get());
	}

	// Analyze struct methods
	for (const auto &structDef : program->structs) {
		for (const auto &method : structDef->methods) {
			analyzeFunctionMutations(method.get());
		}
	}

	mutationPassComplete_ = true;
	logTrace("Mutation analysis pass complete");
}

// Analyze a single function to determine which parameters it mutates
void SemanticAnalyzer::analyzeFunctionMutations(FunctionAST *func) {
	// Build function key (same format as used in semantic analysis)
	std::string funcKey;
	if (func->isMethod()) {
		funcKey = func->receiverType + "." + func->name;
	} else if (!func->namespaceName.empty()) {
		funcKey = func->namespaceName + "." + func->name;
	} else {
		funcKey = func->name;
	}

	// Skip if already analyzed
	auto &info = functionMutations_[funcKey];
	if (info.analyzed) {
		return;
	}

	logDetail("Analyzing mutations in function: " + funcKey);

	// Build parameter name -> index mapping
	std::map<std::string, size_t> paramNameToIndex;
	for (size_t i = 0; i < func->parameters.size(); i++) {
		paramNameToIndex[func->parameters[i].name] = i;
	}

	// Scan all statements for mutations
	for (const auto &stmt : func->body) {
		detectMutationsInStmt(stmt.get(), paramNameToIndex, info.mutatedParamIndices);
	}

	info.analyzed = true;

	if (!info.mutatedParamIndices.empty()) {
		std::string mutatedParams;
		for (size_t idx : info.mutatedParamIndices) {
			if (!mutatedParams.empty())
				mutatedParams += ", ";
			mutatedParams += func->parameters[idx].name;
		}
		logDetail("  Mutates parameters: " + mutatedParams);
	}
}

// Recursively detect mutations in a statement
void SemanticAnalyzer::detectMutationsInStmt(StmtAST *stmt,
											 const std::map<std::string, size_t> &paramNameToIndex,
											 std::set<size_t> &mutatedIndices) {
	if (!stmt)
		return;

	// Direct assignment to a variable
	if (auto *assign = dynamic_cast<AssignStmtAST *>(stmt)) {
		auto it = paramNameToIndex.find(assign->name);
		if (it != paramNameToIndex.end()) {
			mutatedIndices.insert(it->second);
		}
		// Also check RHS for calls that might mutate parameters transitively
		detectMutationsInExpr(assign->value.get(), paramNameToIndex, mutatedIndices);
	}
	// Array element assignment
	else if (auto *arrAssign = dynamic_cast<ArrayAssignStmtAST *>(stmt)) {
		auto it = paramNameToIndex.find(arrAssign->arrayName);
		if (it != paramNameToIndex.end()) {
			mutatedIndices.insert(it->second);
		}
		detectMutationsInExpr(arrAssign->index.get(), paramNameToIndex, mutatedIndices);
		detectMutationsInExpr(arrAssign->value.get(), paramNameToIndex, mutatedIndices);
	}
	// Member assignment (obj.field = value)
	else if (auto *memberAssign = dynamic_cast<MemberAssignStmtAST *>(stmt)) {
		auto it = paramNameToIndex.find(memberAssign->objectName);
		if (it != paramNameToIndex.end()) {
			mutatedIndices.insert(it->second);
		}
		detectMutationsInExpr(memberAssign->value.get(), paramNameToIndex, mutatedIndices);
	}
	// Array element member assignment (arr[i].field = value)
	else if (auto *arrMemAssign = dynamic_cast<ArrayMemberAssignStmtAST *>(stmt)) {
		auto it = paramNameToIndex.find(arrMemAssign->arrayName);
		if (it != paramNameToIndex.end()) {
			mutatedIndices.insert(it->second);
		}
		detectMutationsInExpr(arrMemAssign->index.get(), paramNameToIndex, mutatedIndices);
		detectMutationsInExpr(arrMemAssign->value.get(), paramNameToIndex, mutatedIndices);
	}
	// Member array element assignment (obj.arr[i] = value)
	else if (auto *memArrAssign = dynamic_cast<MemberArrayAssignStmtAST *>(stmt)) {
		auto it = paramNameToIndex.find(memArrAssign->objectName);
		if (it != paramNameToIndex.end()) {
			mutatedIndices.insert(it->second);
		}
		detectMutationsInExpr(memArrAssign->index.get(), paramNameToIndex, mutatedIndices);
		detectMutationsInExpr(memArrAssign->value.get(), paramNameToIndex, mutatedIndices);
	}
	// If statement
	else if (auto *ifStmt = dynamic_cast<IfStmtAST *>(stmt)) {
		detectMutationsInExpr(ifStmt->condition.get(), paramNameToIndex, mutatedIndices);
		for (const auto &s : ifStmt->thenBody) {
			detectMutationsInStmt(s.get(), paramNameToIndex, mutatedIndices);
		}
		for (const auto &s : ifStmt->elseBody) {
			detectMutationsInStmt(s.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// While loop
	else if (auto *whileStmt = dynamic_cast<WhileStmtAST *>(stmt)) {
		detectMutationsInExpr(whileStmt->condition.get(), paramNameToIndex, mutatedIndices);
		for (const auto &s : whileStmt->body) {
			detectMutationsInStmt(s.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// For loop
	else if (auto *forStmt = dynamic_cast<ForStmtAST *>(stmt)) {
		detectMutationsInExpr(forStmt->iterable.get(), paramNameToIndex, mutatedIndices);
		for (const auto &s : forStmt->body) {
			detectMutationsInStmt(s.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// Match statement
	else if (auto *matchStmt = dynamic_cast<MatchStmtAST *>(stmt)) {
		detectMutationsInExpr(matchStmt->scrutinee.get(), paramNameToIndex, mutatedIndices);
		for (const auto &matchCase : matchStmt->cases) {
			if (matchCase.statement) {
				detectMutationsInStmt(matchCase.statement.get(), paramNameToIndex, mutatedIndices);
			}
			if (matchCase.resultExpr) {
				detectMutationsInExpr(matchCase.resultExpr.get(), paramNameToIndex, mutatedIndices);
			}
		}
	}
	// Expression statement (function calls, etc.)
	else if (auto *exprStmt = dynamic_cast<ExprStmtAST *>(stmt)) {
		detectMutationsInExpr(exprStmt->expression.get(), paramNameToIndex, mutatedIndices);
	}
	// Return statement
	else if (auto *retStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		if (retStmt->value) {
			detectMutationsInExpr(retStmt->value.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// Variable declarations
	else if (auto *varDecl = dynamic_cast<VarDeclStmtAST *>(stmt)) {
		if (varDecl->initializer) {
			detectMutationsInExpr(varDecl->initializer.get(), paramNameToIndex, mutatedIndices);
		}
	} else if (auto *letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
		if (letDecl->initializer) {
			detectMutationsInExpr(letDecl->initializer.get(), paramNameToIndex, mutatedIndices);
		}
	}
}

// Recursively detect mutations in an expression
// This checks for function calls that might transitively mutate a parameter
void SemanticAnalyzer::detectMutationsInExpr(ExprAST *expr,
											 const std::map<std::string, size_t> &paramNameToIndex,
											 std::set<size_t> &mutatedIndices) {
	if (!expr)
		return;

	// Function call - check if any parameter argument is passed to a mutating function
	if (auto *call = dynamic_cast<CallExprAST *>(expr)) {
		// Build the resolved function name to lookup mutation info
		std::string funcName = call->resolvedCallee.empty() ? call->callee : call->resolvedCallee;

		// Check each argument - if it's a parameter and the called function mutates that position
		for (size_t argIdx = 0; argIdx < call->args.size(); argIdx++) {
			// Recursively check the argument expression
			detectMutationsInExpr(call->args[argIdx].value.get(), paramNameToIndex, mutatedIndices);

			// If the argument is a direct variable reference to a parameter
			if (auto *varExpr = dynamic_cast<VariableExprAST *>(call->args[argIdx].value.get())) {
				auto paramIt = paramNameToIndex.find(varExpr->name);
				if (paramIt != paramNameToIndex.end()) {
					// Check if the called function mutates this parameter position
					// Use the argToParamMapping if available, otherwise use argIdx
					size_t targetParamIdx = argIdx;
					if (argIdx < call->argToParamMapping.size()) {
						targetParamIdx = call->argToParamMapping[argIdx];
					}

					if (doesFunctionMutateParam(funcName, targetParamIdx)) {
						mutatedIndices.insert(paramIt->second);
					}
				}
			}
		}
	}
	// Binary expression
	else if (auto *binOp = dynamic_cast<BinaryExprAST *>(expr)) {
		detectMutationsInExpr(binOp->left.get(), paramNameToIndex, mutatedIndices);
		detectMutationsInExpr(binOp->right.get(), paramNameToIndex, mutatedIndices);
	}
	// Unary expression
	else if (auto *unaryOp = dynamic_cast<UnaryExprAST *>(expr)) {
		detectMutationsInExpr(unaryOp->operand.get(), paramNameToIndex, mutatedIndices);
	}
	// Array index
	else if (auto *arrIdx = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		detectMutationsInExpr(arrIdx->index.get(), paramNameToIndex, mutatedIndices);
		if (arrIdx->arrayExpr) {
			detectMutationsInExpr(arrIdx->arrayExpr.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// Member access
	else if (auto *memberAccess = dynamic_cast<MemberAccessExprAST *>(expr)) {
		if (memberAccess->object) {
			detectMutationsInExpr(memberAccess->object.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// Struct initializer
	else if (auto *structInit = dynamic_cast<StructInitExprAST *>(expr)) {
		for (const auto &field : structInit->fields) {
			detectMutationsInExpr(field.value.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// Array literal
	else if (auto *arrLit = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		for (const auto &elem : arrLit->values) {
			detectMutationsInExpr(elem.get(), paramNameToIndex, mutatedIndices);
		}
	}
	// Cast expression
	else if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		detectMutationsInExpr(castExpr->expr.get(), paramNameToIndex, mutatedIndices);
	}
	// Slice expression
	else if (auto *sliceExpr = dynamic_cast<SliceExprAST *>(expr)) {
		if (sliceExpr->start)
			detectMutationsInExpr(sliceExpr->start.get(), paramNameToIndex, mutatedIndices);
		if (sliceExpr->end)
			detectMutationsInExpr(sliceExpr->end.get(), paramNameToIndex, mutatedIndices);
	}
	// Other expression types (literals, variables) don't need recursion
}

// Check if a function mutates a specific parameter
bool SemanticAnalyzer::doesFunctionMutateParam(const std::string &funcName, size_t paramIdx) const {
	auto it = functionMutations_.find(funcName);
	if (it == functionMutations_.end()) {
		// Unknown function - conservatively assume no mutation
		// (external functions would need annotations, but for now assume they don't mutate)
		return false;
	}
	return it->second.mutatedParamIndices.count(paramIdx) > 0;
}

// Mark a variable as having its ownership transferred
void SemanticAnalyzer::markVariableMoved(const std::string &name, int line, int column,
										 const std::string &targetFunction) {
	// Check current scope first (variables map)
	auto varIt = variables.find(name);
	if (varIt != variables.end()) {
		varIt->second.ownershipState = OwnershipState::Moved;
		varIt->second.moveInfo = MoveInfo(line, column, targetFunction);
		return;
	}

	// Search through parent scopes
	for (auto it = scopeStack.rbegin(); it != scopeStack.rend(); ++it) {
		auto varIt = it->find(name);
		if (varIt != it->end()) {
			varIt->second.ownershipState = OwnershipState::Moved;
			varIt->second.moveInfo = MoveInfo(line, column, targetFunction);
			return;
		}
	}
}

// Capture current ownership states for all variables in scope
std::map<std::string, OwnershipState> SemanticAnalyzer::captureOwnershipStates() const {
	std::map<std::string, OwnershipState> states;
	for (const auto &scope : scopeStack) {
		for (const auto &[name, info] : scope) {
			states[name] = info.ownershipState;
		}
	}
	return states;
}

// Restore ownership states from a captured snapshot
void SemanticAnalyzer::restoreOwnershipStates(const std::map<std::string, OwnershipState> &states) {
	for (auto &scope : scopeStack) {
		for (auto &[name, info] : scope) {
			auto it = states.find(name);
			if (it != states.end()) {
				info.ownershipState = it->second;
				// Clear moveInfo if restoring to Owned state
				if (it->second == OwnershipState::Owned) {
					info.moveInfo.reset();
				}
			}
		}
	}
}

// Merge ownership states from two branches
// Conservative approach: if moved in EITHER branch, consider it moved
void SemanticAnalyzer::mergeOwnershipStates(const std::map<std::string, OwnershipState> &branchA,
											const std::map<std::string, OwnershipState> &branchB) {
	for (auto &scope : scopeStack) {
		for (auto &[name, info] : scope) {
			auto itA = branchA.find(name);
			auto itB = branchB.find(name);

			// If moved in either branch, mark as moved
			bool movedInA = (itA != branchA.end() && itA->second == OwnershipState::Moved);
			bool movedInB = (itB != branchB.end() && itB->second == OwnershipState::Moved);

			if (movedInA || movedInB) {
				info.ownershipState = OwnershipState::Moved;
				// Keep existing moveInfo if already set, or set a generic one
				if (!info.moveInfo.has_value()) {
					info.moveInfo = MoveInfo(0, 0, "(conditional branch)");
				}
			}
		}
	}
}
