#include "../call_graph.h"
#include "../semantic_analyzer.h"

// === Ownership / Mutation Analysis Implementation ===
// This file implements the mutation analysis pass that determines which
// parameters each function mutates. This information is used during
// semantic analysis to track ownership transfers at call sites.
//
// The pass uses a call graph to analyze functions in reverse topological order,
// ensuring that callee mutation information is available when analyzing callers.
// This enables transitive propagation of mutation info through the call graph.

// Run the mutation analysis pass on all functions in the program
// This must be called before the main semantic analysis pass (Pass 3)
void SemanticAnalyzer::runMutationAnalysisPass(ProgramAST *program) {
	logTrace("Running mutation analysis pass...");

	// Build call graph to determine analysis order
	CallGraphBuilder callGraph;
	callGraph.buildFromProgram(program);

	// Store the call graph for use in doesFunctionMutateParam lookups
	mutationCallGraph_ = std::make_unique<CallGraphBuilder>(std::move(callGraph));

	// Get functions in reverse topological order (callees before callers)
	// This ensures mutation info propagates correctly through the call graph
	std::vector<std::string> analysisOrder = mutationCallGraph_->getReverseTopologicalOrder();

	// Build a map from function key to FunctionAST* for quick lookup
	std::map<std::string, FunctionAST *> funcMap;
	for (const auto &func : program->functions) {
		std::string key = getFunctionKeyStatic(func.get());
		funcMap[key] = func.get();
	}
	for (const auto &structDef : program->structs) {
		for (const auto &method : structDef->methods) {
			std::string key = getMethodKey(structDef->name, method->name);
			funcMap[key] = method.get();
		}
	}

	// Analyze functions in topological order
	for (const auto &funcKey : analysisOrder) {
		auto it = funcMap.find(funcKey);
		if (it != funcMap.end()) {
			analyzeFunctionMutations(it->second);
		}
	}

	// Also analyze any functions not in the call graph (unreachable code)
	for (const auto &func : program->functions) {
		analyzeFunctionMutations(func.get());
	}
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
	// Build function key using static helper
	std::string funcKey = getFunctionKeyStatic(func);

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

	// Delegate to the virtual method on StmtAST
	// The callback allows the AST nodes to query mutation info without direct access to the analyzer
	stmt->collectMutatedParams(paramNameToIndex, mutatedIndices,
							   [this](const std::string &funcName, size_t paramIdx) {
								   return doesFunctionMutateParam(funcName, paramIdx);
							   });
}

// Recursively detect mutations in an expression
// This checks for function calls that might transitively mutate a parameter
void SemanticAnalyzer::detectMutationsInExpr(ExprAST *expr,
											 const std::map<std::string, size_t> &paramNameToIndex,
											 std::set<size_t> &mutatedIndices) {
	if (!expr)
		return;

	// Delegate to the virtual method on ExprAST
	// The callback allows the AST nodes to query mutation info without direct access to the analyzer
	expr->collectMutatedParams(paramNameToIndex, mutatedIndices,
							   [this](const std::string &funcName, size_t paramIdx) {
								   return doesFunctionMutateParam(funcName, paramIdx);
							   });
}

// Check if a function mutates a specific parameter
// Handles both qualified names (array.push) and unqualified names (push)
bool SemanticAnalyzer::doesFunctionMutateParam(const std::string &funcName, size_t paramIdx) const {
	// First, try direct lookup
	auto it = functionMutations_.find(funcName);
	if (it != functionMutations_.end()) {
		return it->second.mutatedParamIndices.count(paramIdx) > 0;
	}

	// If direct lookup failed and we have a call graph, try looking up aliases
	// This handles cases where funcName is unqualified (e.g., "push" instead of "array.push")
	if (mutationCallGraph_) {
		const auto &aliases = mutationCallGraph_->getAliases(funcName);
		for (const auto &alias : aliases) {
			auto aliasIt = functionMutations_.find(alias);
			if (aliasIt != functionMutations_.end()) {
				if (aliasIt->second.mutatedParamIndices.count(paramIdx) > 0) {
					return true;
				}
			}
		}
	}

	// Unknown function - conservatively assume no mutation
	// (external functions would need annotations, but for now assume they don't mutate)
	return false;
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
