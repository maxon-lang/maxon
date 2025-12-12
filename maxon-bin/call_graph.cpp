#include "call_graph.h"

// Note: Implicit method calls (iterator.next, key.hash, etc.) are handled by
// MIR-level DCE after generic instantiation when all concrete types are known.
// See compiler.cpp:486-489 for why AST-level DCE is disabled.

const std::set<std::string> CallGraphBuilder::emptySet;

void CallGraphBuilder::buildFromProgram(ProgramAST *program) {
	callGraph.clear();
	nameAliases.clear();

	// Process all top-level functions
	for (const auto &func : program->functions) {
		std::string funcName;
		if (!func->receiverType.empty()) {
			funcName = func->receiverType + "." + func->name;
		} else if (!func->namespaceName.empty()) {
			funcName = func->namespaceName + "." + func->name;
		} else {
			funcName = func->name;
		}

		callGraph[funcName] = extractCallsFromFunction(func.get());

		// Register unqualified name as alias if function has a namespace
		if (!func->namespaceName.empty()) {
			nameAliases[func->name].insert(funcName);
		}
	}

	// Process methods inside structs
	for (const auto &structDef : program->structs) {
		for (const auto &method : structDef->methods) {
			std::string methodName = structDef->name + "." + method->name;
			callGraph[methodName] = extractCallsFromFunction(method.get());

			// Register just method name as alias
			nameAliases[method->name].insert(methodName);

			// For interface methods like "Hashable.hash", also register the bare method name "hash"
			// This ensures that calls to element.hash() in generic code resolve to string.hash, etc.
			size_t dotPos = method->name.find('.');
			if (dotPos != std::string::npos) {
				std::string bareMethodName = method->name.substr(dotPos + 1);
				nameAliases[bareMethodName].insert(methodName);
			}
		}
	}
}

const std::set<std::string> &CallGraphBuilder::getCallees(const std::string &funcName) const {
	auto it = callGraph.find(funcName);
	if (it != callGraph.end()) {
		return it->second;
	}
	return emptySet;
}

std::set<std::string> CallGraphBuilder::getReachableFunctions(const std::set<std::string> &entryPoints) const {
	std::set<std::string> reachable;
	std::vector<std::string> worklist;

	// Start with entry points
	for (const auto &entry : entryPoints) {
		if (reachable.insert(entry).second) {
			worklist.push_back(entry);
		}
		// Also add any aliases (qualified names for unqualified entry points)
		auto aliasIt = nameAliases.find(entry);
		if (aliasIt != nameAliases.end()) {
			for (const auto &alias : aliasIt->second) {
				if (reachable.insert(alias).second) {
					worklist.push_back(alias);
				}
			}
		}
	}

	// BFS to find all reachable functions
	while (!worklist.empty()) {
		std::string current = worklist.back();
		worklist.pop_back();

		auto it = callGraph.find(current);
		if (it != callGraph.end()) {
			for (const auto &callee : it->second) {
				if (reachable.insert(callee).second) {
					worklist.push_back(callee);
				}
				// Also add any aliases for this callee
				auto aliasIt = nameAliases.find(callee);
				if (aliasIt != nameAliases.end()) {
					for (const auto &alias : aliasIt->second) {
						if (reachable.insert(alias).second) {
							worklist.push_back(alias);
						}
					}
				}
			}
		}
	}

	return reachable;
}

std::set<std::string> CallGraphBuilder::getReachableFunctions(const std::string &entryPoint) const {
	return getReachableFunctions(std::set<std::string>{entryPoint});
}

std::set<std::string> CallGraphBuilder::extractCallsFromFunction(FunctionAST *func) {
	std::set<std::string> calls;

	for (const auto &stmt : func->body) {
		extractCallsFromStmt(stmt.get(), calls);
	}

	return calls;
}

void CallGraphBuilder::extractCallsFromStmt(StmtAST *stmt, std::set<std::string> &calls) {
	if (!stmt)
		return;

	if (auto *varDecl = dynamic_cast<VarDeclStmtAST *>(stmt)) {
		if (varDecl->initializer) {
			extractCallsFromExpr(varDecl->initializer.get(), calls);
		}
	} else if (auto *letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
		if (letDecl->initializer) {
			extractCallsFromExpr(letDecl->initializer.get(), calls);
		}
	} else if (auto *assign = dynamic_cast<AssignStmtAST *>(stmt)) {
		if (assign->value) {
			extractCallsFromExpr(assign->value.get(), calls);
		}
	} else if (auto *arrayAssign = dynamic_cast<ArrayAssignStmtAST *>(stmt)) {
		if (arrayAssign->index) {
			extractCallsFromExpr(arrayAssign->index.get(), calls);
		}
		if (arrayAssign->value) {
			extractCallsFromExpr(arrayAssign->value.get(), calls);
		}
	} else if (auto *memberAssign = dynamic_cast<MemberAssignStmtAST *>(stmt)) {
		if (memberAssign->value) {
			extractCallsFromExpr(memberAssign->value.get(), calls);
		}
	} else if (auto *memberArrayAssign = dynamic_cast<MemberArrayAssignStmtAST *>(stmt)) {
		if (memberArrayAssign->index) {
			extractCallsFromExpr(memberArrayAssign->index.get(), calls);
		}
		if (memberArrayAssign->value) {
			extractCallsFromExpr(memberArrayAssign->value.get(), calls);
		}
	} else if (auto *arrayMemberAssign = dynamic_cast<ArrayMemberAssignStmtAST *>(stmt)) {
		if (arrayMemberAssign->index) {
			extractCallsFromExpr(arrayMemberAssign->index.get(), calls);
		}
		if (arrayMemberAssign->value) {
			extractCallsFromExpr(arrayMemberAssign->value.get(), calls);
		}
	} else if (auto *ifStmt = dynamic_cast<IfStmtAST *>(stmt)) {
		if (ifStmt->condition) {
			extractCallsFromExpr(ifStmt->condition.get(), calls);
		}
		for (const auto &s : ifStmt->thenBody) {
			extractCallsFromStmt(s.get(), calls);
		}
		for (const auto &s : ifStmt->elseBody) {
			extractCallsFromStmt(s.get(), calls);
		}
	} else if (auto *whileStmt = dynamic_cast<WhileStmtAST *>(stmt)) {
		if (whileStmt->condition) {
			extractCallsFromExpr(whileStmt->condition.get(), calls);
		}
		for (const auto &s : whileStmt->body) {
			extractCallsFromStmt(s.get(), calls);
		}
	} else if (auto *forStmt = dynamic_cast<ForStmtAST *>(stmt)) {
		if (forStmt->iterable) {
			extractCallsFromExpr(forStmt->iterable.get(), calls);
		}
		for (const auto &s : forStmt->body) {
			extractCallsFromStmt(s.get(), calls);
		}
	} else if (auto *returnStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		if (returnStmt->value) {
			extractCallsFromExpr(returnStmt->value.get(), calls);
		}
	} else if (auto *exprStmt = dynamic_cast<ExprStmtAST *>(stmt)) {
		if (exprStmt->expression) {
			extractCallsFromExpr(exprStmt->expression.get(), calls);
		}
	} else if (auto *matchStmt = dynamic_cast<MatchStmtAST *>(stmt)) {
		// Extract calls from scrutinee expression
		if (matchStmt->scrutinee) {
			extractCallsFromExpr(matchStmt->scrutinee.get(), calls);
		}
		// Process each match case
		for (const auto &matchCase : matchStmt->cases) {
			// Extract calls from patterns
			for (const auto &pattern : matchCase.patterns) {
				extractCallsFromExpr(pattern.get(), calls);
			}
			// Extract calls from case statement
			if (matchCase.statement) {
				extractCallsFromStmt(matchCase.statement.get(), calls);
			}
		}
	}
	// BreakStmtAST and ContinueStmtAST don't contain expressions
}

void CallGraphBuilder::extractCallsFromExpr(ExprAST *expr, std::set<std::string> &calls) {
	if (!expr)
		return;

	if (auto *callExpr = dynamic_cast<CallExprAST *>(expr)) {
		// Add the callee name
		calls.insert(callExpr->callee);

		// Also extract calls from arguments
		for (const auto &arg : callExpr->args) {
			extractCallsFromExpr(arg.value.get(), calls);
		}
	} else if (auto *binaryExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		extractCallsFromExpr(binaryExpr->left.get(), calls);
		extractCallsFromExpr(binaryExpr->right.get(), calls);
	} else if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		extractCallsFromExpr(unaryExpr->operand.get(), calls);
	} else if (auto *arrayIndex = dynamic_cast<ArrayIndexExprAST *>(expr)) {
		if (arrayIndex->arrayExpr) {
			extractCallsFromExpr(arrayIndex->arrayExpr.get(), calls);
		}
		if (arrayIndex->index) {
			extractCallsFromExpr(arrayIndex->index.get(), calls);
		}
	} else if (auto *memberAccess = dynamic_cast<MemberAccessExprAST *>(expr)) {
		if (memberAccess->object) {
			extractCallsFromExpr(memberAccess->object.get(), calls);
		}
	} else if (auto *structInit = dynamic_cast<StructInitExprAST *>(expr)) {
		for (const auto &field : structInit->fields) {
			extractCallsFromExpr(field.value.get(), calls);
		}
	} else if (auto *arrayLiteral = dynamic_cast<ArrayLiteralExprAST *>(expr)) {
		for (const auto &val : arrayLiteral->values) {
			extractCallsFromExpr(val.get(), calls);
		}
	} else if (auto *cast = dynamic_cast<CastExprAST *>(expr)) {
		extractCallsFromExpr(cast->expr.get(), calls);
	} else if (auto *slice = dynamic_cast<SliceExprAST *>(expr)) {
		if (slice->start) {
			extractCallsFromExpr(slice->start.get(), calls);
		}
		if (slice->end) {
			extractCallsFromExpr(slice->end.get(), calls);
		}
	} else if (auto *matchExpr = dynamic_cast<MatchExprAST *>(expr)) {
		// Extract calls from scrutinee expression
		if (matchExpr->scrutinee) {
			extractCallsFromExpr(matchExpr->scrutinee.get(), calls);
		}
		// Process each match case
		for (const auto &matchCase : matchExpr->cases) {
			// Extract calls from patterns
			for (const auto &pattern : matchCase.patterns) {
				extractCallsFromExpr(pattern.get(), calls);
			}
			// Extract calls from result expression
			if (matchCase.resultExpr) {
				extractCallsFromExpr(matchCase.resultExpr.get(), calls);
			}
		}
	} else if (auto *setFrom = dynamic_cast<SetFromExprAST *>(expr)) {
		if (setFrom->arrayExpr) {
			extractCallsFromExpr(setFrom->arrayExpr.get(), calls);
		}
	} else if (auto *mapLiteral = dynamic_cast<MapLiteralWithEntriesExprAST *>(expr)) {
		for (const auto &entry : mapLiteral->entries) {
			extractCallsFromExpr(entry.key.get(), calls);
			extractCallsFromExpr(entry.value.get(), calls);
		}
	}
	// NumberExprAST, FloatExprAST, BooleanExprAST,
	// CharacterExprAST, StringLiteralExprAST, ByteExprAST don't contain calls

	// VariableExprAST may be a function reference (function passed as argument)
	// If isFunctionReference is set, add the resolved function name to calls
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		if (varExpr->isFunctionReference && !varExpr->resolvedFunctionName.empty()) {
			calls.insert(varExpr->resolvedFunctionName);
		}
	}
}
