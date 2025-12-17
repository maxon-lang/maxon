#pragma once

#include "ast.h"
#include <map>
#include <set>
#include <string>
#include <vector>

/**
 * CallGraphBuilder - Analyzes AST to determine which functions call which other functions.
 *
 * This is used to determine which stdlib functions are actually needed before importing files,
 * enabling selective import of only the functions that are transitively reachable from main().
 */
class CallGraphBuilder {
  public:
	/**
	 * Build a call graph from a program AST.
	 * Maps each function name to the set of function names it calls.
	 */
	void buildFromProgram(ProgramAST *program);

	/**
	 * Get all function names called by the given function.
	 */
	const std::set<std::string> &getCallees(const std::string &funcName) const;

	/**
	 * Get all functions transitively reachable from the given entry points.
	 * Entry points are typically "main", "_start", etc.
	 */
	std::set<std::string> getReachableFunctions(const std::set<std::string> &entryPoints) const;

	/**
	 * Get all functions transitively reachable from a single entry point.
	 */
	std::set<std::string> getReachableFunctions(const std::string &entryPoint) const;

	/**
	 * Extract all function calls from a single function AST.
	 * Returns set of callee names (may include Type.method format).
	 */
	static std::set<std::string> extractCallsFromFunction(FunctionAST *func);

	/**
	 * Get functions in reverse topological order (callees before callers).
	 * Functions in cycles are returned in arbitrary order within the cycle.
	 * This is useful for propagating information from callees to callers.
	 */
	std::vector<std::string> getReverseTopologicalOrder() const;

	/**
	 * Get all known function names in the call graph.
	 */
	std::set<std::string> getAllFunctions() const;

	/**
	 * Get possible qualified names for an unqualified function name.
	 * e.g., "push" might return {"array.push", "array<int>.push", ...}
	 */
	const std::set<std::string> &getAliases(const std::string &unqualifiedName) const;

  private:
	// Maps function name -> set of functions it calls
	std::map<std::string, std::set<std::string>> callGraph;

	// Maps unqualified name -> set of qualified names (aliases)
	// e.g., "print" -> {"stdlib.sys.print"}
	std::map<std::string, std::set<std::string>> nameAliases;

	// Empty set to return for unknown functions
	static const std::set<std::string> emptySet;

	// Helper to extract calls from a statement
	static void extractCallsFromStmt(StmtAST *stmt, std::set<std::string> &calls);

	// Helper to extract calls from an expression
	static void extractCallsFromExpr(ExprAST *expr, std::set<std::string> &calls);
};
