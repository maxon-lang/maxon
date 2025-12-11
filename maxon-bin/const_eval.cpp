#include "const_eval.h"
#include <algorithm>
#include <cmath>
#include <queue>
#include <set>
#include <sstream>

void ConstExprEvaluator::registerGlobal(const std::string &name, GlobalLetDeclAST *decl) {
	globalDecls_[name] = decl;
	// Pre-register in globals_ with evaluated=false
	EvaluatedGlobalInfo info;
	info.name = name;
	info.evaluated = false;
	globals_[name] = info;
}

std::optional<ConstValue> ConstExprEvaluator::getGlobalValue(const std::string &name) const {
	auto it = globals_.find(name);
	if (it != globals_.end() && it->second.evaluated) {
		return it->second.value;
	}
	return std::nullopt;
}

std::optional<std::string> ConstExprEvaluator::getGlobalType(const std::string &name) const {
	auto it = globals_.find(name);
	if (it != globals_.end() && it->second.evaluated) {
		return it->second.type;
	}
	return std::nullopt;
}

void ConstExprEvaluator::collectGlobalRefs(ExprAST *expr, std::vector<std::string> &refs) {
	if (!expr)
		return;

	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		// Check if this variable is a registered global
		if (globalDecls_.count(varExpr->name)) {
			refs.push_back(varExpr->name);
		}
	} else if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		collectGlobalRefs(binExpr->left.get(), refs);
		collectGlobalRefs(binExpr->right.get(), refs);
	} else if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		collectGlobalRefs(unaryExpr->operand.get(), refs);
	} else if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		collectGlobalRefs(castExpr->expr.get(), refs);
	}
	// Literals have no global refs
}

bool ConstExprEvaluator::buildDependencyOrder(std::vector<std::string> &order, std::vector<std::string> &errors) {
	// Build adjacency list: for each global, list of globals it depends on
	std::map<std::string, std::vector<std::string>> deps;
	std::map<std::string, int> inDegree;

	for (const auto &[name, decl] : globalDecls_) {
		deps[name] = {};
		inDegree[name] = 0;
	}

	for (const auto &[name, decl] : globalDecls_) {
		std::vector<std::string> refs;
		collectGlobalRefs(decl->initializer.get(), refs);
		// Remove duplicates
		std::sort(refs.begin(), refs.end());
		refs.erase(std::unique(refs.begin(), refs.end()), refs.end());
		deps[name] = refs;
		inDegree[name] = static_cast<int>(refs.size());
	}

	// Kahn's algorithm for topological sort
	std::queue<std::string> ready;
	for (const auto &[name, degree] : inDegree) {
		if (degree == 0) {
			ready.push(name);
		}
	}

	while (!ready.empty()) {
		std::string curr = ready.front();
		ready.pop();
		order.push_back(curr);

		// For each global that depends on curr, decrement its in-degree
		for (const auto &[name, depList] : deps) {
			if (std::find(depList.begin(), depList.end(), curr) != depList.end()) {
				inDegree[name]--;
				if (inDegree[name] == 0) {
					ready.push(name);
				}
			}
		}
	}

	// Check for cycles
	if (order.size() != globalDecls_.size()) {
		// Find nodes involved in a cycle
		std::vector<std::string> cycleNodes;
		for (const auto &[name, degree] : inDegree) {
			if (degree > 0) {
				cycleNodes.push_back(name);
			}
		}

		std::stringstream ss;
		ss << "Circular dependency detected among global constants: ";
		for (size_t i = 0; i < cycleNodes.size(); ++i) {
			if (i > 0)
				ss << ", ";
			ss << cycleNodes[i];
		}
		errors.push_back(ss.str());
		return false;
	}

	return true;
}

bool ConstExprEvaluator::evaluateAll(std::vector<std::string> &errors) {
	std::vector<std::string> order;
	if (!buildDependencyOrder(order, errors)) {
		return false;
	}

	// Evaluate in dependency order
	for (const auto &name : order) {
		auto *decl = globalDecls_[name];
		std::vector<std::string> evalStack;
		evalStack.push_back(name);

		auto result = evaluateWithContext(decl->initializer.get(), evalStack);
		if (!result.success) {
			std::stringstream ss;
			ss << "Error evaluating global constant '" << name << "': " << result.errorMsg;
			if (result.errorLine > 0) {
				ss << " at line " << result.errorLine << ", column " << result.errorColumn;
			}
			errors.push_back(ss.str());
			return false;
		}

		// Store the evaluated value
		globals_[name].value = result.value;
		globals_[name].type = result.type;
		globals_[name].evaluated = true;
	}

	return true;
}

ConstEvalResult ConstExprEvaluator::evaluate(ExprAST *expr) {
	std::vector<std::string> evalStack;
	return evaluateWithContext(expr, evalStack);
}

ConstEvalResult ConstExprEvaluator::evaluateWithContext(ExprAST *expr, std::vector<std::string> &evalStack) {
	if (!expr) {
		return ConstEvalResult::Error("Null expression");
	}

	if (auto *numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return evaluateNumber(numExpr);
	}
	if (auto *floatExpr = dynamic_cast<FloatExprAST *>(expr)) {
		return evaluateFloat(floatExpr);
	}
	if (auto *boolExpr = dynamic_cast<BooleanExprAST *>(expr)) {
		return evaluateBool(boolExpr);
	}
	if (auto *strExpr = dynamic_cast<StringLiteralExprAST *>(expr)) {
		return evaluateString(strExpr);
	}
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		return evaluateVariable(varExpr, evalStack);
	}
	if (auto *binExpr = dynamic_cast<BinaryExprAST *>(expr)) {
		return evaluateBinary(binExpr, evalStack);
	}
	if (auto *unaryExpr = dynamic_cast<UnaryExprAST *>(expr)) {
		return evaluateUnary(unaryExpr, evalStack);
	}
	if (auto *castExpr = dynamic_cast<CastExprAST *>(expr)) {
		return evaluateCast(castExpr, evalStack);
	}
	if (auto *byteExpr = dynamic_cast<ByteExprAST *>(expr)) {
		return ConstEvalResult::Int(byteExpr->value);
	}
	if (auto *charExpr = dynamic_cast<CharacterExprAST *>(expr)) {
		return ConstEvalResult::String(charExpr->value);
	}

	return ConstEvalResult::Error("Expression is not a constant expression", expr->line, expr->column);
}

ConstEvalResult ConstExprEvaluator::evaluateNumber(NumberExprAST *expr) {
	return ConstEvalResult::Int(expr->value);
}

ConstEvalResult ConstExprEvaluator::evaluateFloat(FloatExprAST *expr) {
	return ConstEvalResult::Float(expr->value);
}

ConstEvalResult ConstExprEvaluator::evaluateBool(BooleanExprAST *expr) {
	return ConstEvalResult::Bool(expr->value);
}

ConstEvalResult ConstExprEvaluator::evaluateString(StringLiteralExprAST *expr) {
	return ConstEvalResult::String(expr->value);
}

ConstEvalResult ConstExprEvaluator::evaluateVariable(VariableExprAST *expr, std::vector<std::string> &evalStack) {
	const std::string &name = expr->name;

	// Check if it's a registered global
	auto it = globals_.find(name);
	if (it == globals_.end()) {
		return ConstEvalResult::Error("Unknown identifier '" + name + "' in constant expression", expr->line,
									  expr->column);
	}

	// Check if already evaluated
	if (it->second.evaluated) {
		ConstEvalResult r;
		r.success = true;
		r.value = it->second.value;
		r.type = it->second.type;
		return r;
	}

	// Should not happen if we evaluate in dependency order, but check for cycles anyway
	if (std::find(evalStack.begin(), evalStack.end(), name) != evalStack.end()) {
		return ConstEvalResult::Error("Circular reference to '" + name + "'", expr->line, expr->column);
	}

	// Should have been evaluated already in dependency order
	return ConstEvalResult::Error("Global '" + name + "' has not been evaluated yet", expr->line, expr->column);
}

ConstEvalResult ConstExprEvaluator::evaluateBinary(BinaryExprAST *expr, std::vector<std::string> &evalStack) {
	auto leftResult = evaluateWithContext(expr->left.get(), evalStack);
	if (!leftResult.success)
		return leftResult;

	auto rightResult = evaluateWithContext(expr->right.get(), evalStack);
	if (!rightResult.success)
		return rightResult;

	char op = expr->op;

	// Handle string concatenation
	if (leftResult.type == "string" && rightResult.type == "string" && op == '+') {
		return ConstEvalResult::String(std::get<std::string>(leftResult.value) +
									   std::get<std::string>(rightResult.value));
	}

	// Handle numeric operations
	bool leftIsFloat = leftResult.type == "float";
	bool rightIsFloat = rightResult.type == "float";

	// For arithmetic, promote int to float if needed
	if ((leftResult.type == "int" || leftResult.type == "float") &&
		(rightResult.type == "int" || rightResult.type == "float")) {

		if (leftIsFloat || rightIsFloat) {
			// Float arithmetic
			double lv = leftIsFloat ? std::get<double>(leftResult.value)
									: static_cast<double>(std::get<int64_t>(leftResult.value));
			double rv = rightIsFloat ? std::get<double>(rightResult.value)
									 : static_cast<double>(std::get<int64_t>(rightResult.value));

			switch (op) {
			case '+':
				return ConstEvalResult::Float(lv + rv);
			case '-':
				return ConstEvalResult::Float(lv - rv);
			case '*':
				return ConstEvalResult::Float(lv * rv);
			case '/':
				if (rv == 0.0)
					return ConstEvalResult::Error("Division by zero", expr->line, expr->column);
				return ConstEvalResult::Float(lv / rv);
			case '<':
				return ConstEvalResult::Bool(lv < rv);
			case '>':
				return ConstEvalResult::Bool(lv > rv);
			case 'L': // <=
				return ConstEvalResult::Bool(lv <= rv);
			case 'G': // >=
				return ConstEvalResult::Bool(lv >= rv);
			case '=': // ==
				return ConstEvalResult::Bool(lv == rv);
			case '!': // !=
				return ConstEvalResult::Bool(lv != rv);
			default:
				return ConstEvalResult::Error("Invalid operator for float operands", expr->line, expr->column);
			}
		} else {
			// Integer arithmetic
			int64_t lv = std::get<int64_t>(leftResult.value);
			int64_t rv = std::get<int64_t>(rightResult.value);

			switch (op) {
			case '+':
				return ConstEvalResult::Int(lv + rv);
			case '-':
				return ConstEvalResult::Int(lv - rv);
			case '*':
				return ConstEvalResult::Int(lv * rv);
			case '/':
				if (rv == 0)
					return ConstEvalResult::Error("Division by zero", expr->line, expr->column);
				return ConstEvalResult::Int(lv / rv);
			case '%': // mod
				if (rv == 0)
					return ConstEvalResult::Error("Modulo by zero", expr->line, expr->column);
				return ConstEvalResult::Int(lv % rv);
			case '<':
				return ConstEvalResult::Bool(lv < rv);
			case '>':
				return ConstEvalResult::Bool(lv > rv);
			case 'L': // <=
				return ConstEvalResult::Bool(lv <= rv);
			case 'G': // >=
				return ConstEvalResult::Bool(lv >= rv);
			case '=': // ==
				return ConstEvalResult::Bool(lv == rv);
			case '!': // !=
				return ConstEvalResult::Bool(lv != rv);
			case '&': // bitwise and
				return ConstEvalResult::Int(lv & rv);
			case '|': // bitwise or
				return ConstEvalResult::Int(lv | rv);
			case '^': // bitwise xor
				return ConstEvalResult::Int(lv ^ rv);
			case 'l': // left shift
				return ConstEvalResult::Int(lv << rv);
			case 'r': // right shift
				return ConstEvalResult::Int(lv >> rv);
			default:
				return ConstEvalResult::Error("Invalid operator for integer operands", expr->line, expr->column);
			}
		}
	}

	// Handle boolean operations
	if (leftResult.type == "bool" && rightResult.type == "bool") {
		bool lv = std::get<bool>(leftResult.value);
		bool rv = std::get<bool>(rightResult.value);

		switch (op) {
		case 'A': // and
			return ConstEvalResult::Bool(lv && rv);
		case 'O': // or
			return ConstEvalResult::Bool(lv || rv);
		case '=': // ==
			return ConstEvalResult::Bool(lv == rv);
		case '!': // !=
			return ConstEvalResult::Bool(lv != rv);
		default:
			return ConstEvalResult::Error("Invalid operator for boolean operands", expr->line, expr->column);
		}
	}

	// String comparison
	if (leftResult.type == "string" && rightResult.type == "string") {
		const std::string &lv = std::get<std::string>(leftResult.value);
		const std::string &rv = std::get<std::string>(rightResult.value);

		switch (op) {
		case '=': // ==
			return ConstEvalResult::Bool(lv == rv);
		case '!': // !=
			return ConstEvalResult::Bool(lv != rv);
		default:
			return ConstEvalResult::Error("Invalid operator for string operands", expr->line, expr->column);
		}
	}

	return ConstEvalResult::Error("Type mismatch in binary operation", expr->line, expr->column);
}

ConstEvalResult ConstExprEvaluator::evaluateUnary(UnaryExprAST *expr, std::vector<std::string> &evalStack) {
	auto operandResult = evaluateWithContext(expr->operand.get(), evalStack);
	if (!operandResult.success)
		return operandResult;

	char op = expr->op;

	if (op == '-') {
		if (operandResult.type == "int") {
			return ConstEvalResult::Int(-std::get<int64_t>(operandResult.value));
		}
		if (operandResult.type == "float") {
			return ConstEvalResult::Float(-std::get<double>(operandResult.value));
		}
		return ConstEvalResult::Error("Unary minus requires numeric operand", expr->line, expr->column);
	}

	if (op == '+') {
		if (operandResult.type == "int" || operandResult.type == "float") {
			return operandResult;
		}
		return ConstEvalResult::Error("Unary plus requires numeric operand", expr->line, expr->column);
	}

	if (op == '!') { // 'not' (logical negation)
		if (operandResult.type == "bool") {
			return ConstEvalResult::Bool(!std::get<bool>(operandResult.value));
		}
		return ConstEvalResult::Error("'not' requires boolean operand", expr->line, expr->column);
	}

	if (op == '~') { // bitwise not
		if (operandResult.type == "int") {
			return ConstEvalResult::Int(~std::get<int64_t>(operandResult.value));
		}
		return ConstEvalResult::Error("Bitwise not requires integer operand", expr->line, expr->column);
	}

	return ConstEvalResult::Error("Unknown unary operator", expr->line, expr->column);
}

ConstEvalResult ConstExprEvaluator::evaluateCast(CastExprAST *expr, std::vector<std::string> &evalStack) {
	auto operandResult = evaluateWithContext(expr->expr.get(), evalStack);
	if (!operandResult.success)
		return operandResult;

	const std::string &targetType = expr->targetType;

	if (targetType == "int") {
		if (operandResult.type == "int") {
			return operandResult;
		}
		if (operandResult.type == "float") {
			return ConstEvalResult::Int(static_cast<int64_t>(std::get<double>(operandResult.value)));
		}
		if (operandResult.type == "bool") {
			return ConstEvalResult::Int(std::get<bool>(operandResult.value) ? 1 : 0);
		}
		return ConstEvalResult::Error("Cannot cast " + operandResult.type + " to int", expr->line, expr->column);
	}

	if (targetType == "float") {
		if (operandResult.type == "float") {
			return operandResult;
		}
		if (operandResult.type == "int") {
			return ConstEvalResult::Float(static_cast<double>(std::get<int64_t>(operandResult.value)));
		}
		return ConstEvalResult::Error("Cannot cast " + operandResult.type + " to float", expr->line, expr->column);
	}

	if (targetType == "bool") {
		if (operandResult.type == "bool") {
			return operandResult;
		}
		if (operandResult.type == "int") {
			return ConstEvalResult::Bool(std::get<int64_t>(operandResult.value) != 0);
		}
		return ConstEvalResult::Error("Cannot cast " + operandResult.type + " to bool", expr->line, expr->column);
	}

	return ConstEvalResult::Error("Cannot cast to " + targetType + " in constant expression", expr->line, expr->column);
}
