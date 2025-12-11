#ifndef CONST_EVAL_H
#define CONST_EVAL_H

#include "ast.h"
#include <cstdint>
#include <map>
#include <optional>
#include <string>
#include <variant>
#include <vector>

// Constant value types for compile-time evaluation
using ConstValue = std::variant<int64_t, double, bool, std::string>;

// Result of constant evaluation
struct ConstEvalResult {
	bool success = false;
	ConstValue value;
	std::string type;	  // "int", "float", "bool", "string"
	std::string errorMsg; // Error message if !success
	int errorLine = 0;
	int errorColumn = 0;

	static ConstEvalResult Error(const std::string &msg, int line = 0, int col = 0) {
		ConstEvalResult r;
		r.success = false;
		r.errorMsg = msg;
		r.errorLine = line;
		r.errorColumn = col;
		return r;
	}

	static ConstEvalResult Int(int64_t v) {
		ConstEvalResult r;
		r.success = true;
		r.value = v;
		r.type = "int";
		return r;
	}

	static ConstEvalResult Float(double v) {
		ConstEvalResult r;
		r.success = true;
		r.value = v;
		r.type = "float";
		return r;
	}

	static ConstEvalResult Bool(bool v) {
		ConstEvalResult r;
		r.success = true;
		r.value = v;
		r.type = "bool";
		return r;
	}

	static ConstEvalResult String(const std::string &v) {
		ConstEvalResult r;
		r.success = true;
		r.value = v;
		r.type = "string";
		return r;
	}
};

// Evaluated global constant information (for const_eval internal use)
struct EvaluatedGlobalInfo {
	std::string name;
	std::string type;
	ConstValue value;
	bool evaluated = false; // True once value has been computed
};

// Constant expression evaluator for compile-time evaluation of global let initializers
class ConstExprEvaluator {
  public:
	ConstExprEvaluator() = default;

	// Register a global constant (before evaluation, for forward reference support)
	void registerGlobal(const std::string &name, GlobalLetDeclAST *decl);

	// Evaluate all registered globals in dependency order
	// Returns true if all globals were evaluated successfully
	// On failure, errors contains the error messages
	bool evaluateAll(std::vector<std::string> &errors);

	// Get evaluated global constants (name -> info)
	const std::map<std::string, EvaluatedGlobalInfo> &getGlobals() const { return globals_; }

	// Get a specific global's value (must have been evaluated)
	std::optional<ConstValue> getGlobalValue(const std::string &name) const;

	// Get a specific global's type (must have been evaluated)
	std::optional<std::string> getGlobalType(const std::string &name) const;

  private:
	// Evaluate a single expression
	ConstEvalResult evaluate(ExprAST *expr);

	// Evaluate with current evaluation context (for cycle detection)
	ConstEvalResult evaluateWithContext(ExprAST *expr, std::vector<std::string> &evalStack);

	// Build dependency graph and topologically sort globals
	bool buildDependencyOrder(std::vector<std::string> &order, std::vector<std::string> &errors);

	// Collect global references from an expression
	void collectGlobalRefs(ExprAST *expr, std::vector<std::string> &refs);

	// Helper evaluation methods for specific expression types
	ConstEvalResult evaluateNumber(NumberExprAST *expr);
	ConstEvalResult evaluateFloat(FloatExprAST *expr);
	ConstEvalResult evaluateBool(BooleanExprAST *expr);
	ConstEvalResult evaluateString(StringLiteralExprAST *expr);
	ConstEvalResult evaluateVariable(VariableExprAST *expr, std::vector<std::string> &evalStack);
	ConstEvalResult evaluateBinary(BinaryExprAST *expr, std::vector<std::string> &evalStack);
	ConstEvalResult evaluateUnary(UnaryExprAST *expr, std::vector<std::string> &evalStack);
	ConstEvalResult evaluateCast(CastExprAST *expr, std::vector<std::string> &evalStack);

	// Storage for registered globals
	std::map<std::string, GlobalLetDeclAST *> globalDecls_;
	std::map<std::string, EvaluatedGlobalInfo> globals_;
};

#endif // CONST_EVAL_H
