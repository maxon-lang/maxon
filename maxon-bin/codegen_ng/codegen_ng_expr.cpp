/**
 * MIR Code Generator - Expression Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

mir::MIRValue *MIRCodeGenerator::generateExpr(ExprAST *expr) {
	// Number literal
	if (auto *numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return builder->getInt64(static_cast<int64_t>(numExpr->value));
	}

	// Variable reference
	if (auto *varExpr = dynamic_cast<VariableExprAST *>(expr)) {
		auto it = namedValues.find(varExpr->name);
		if (it == namedValues.end()) {
			throw std::runtime_error("Unknown variable: " + varExpr->name);
		}
		// Load the value from the alloca
		return builder->createLoad(mir::MIRType::getInt64(), it->second, varExpr->name);
	}

	throw std::runtime_error("Unsupported expression type");
}
