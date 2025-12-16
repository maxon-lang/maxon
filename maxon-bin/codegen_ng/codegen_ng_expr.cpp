/**
 * MIR Code Generator - Expression Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

mir::MIRValue *MIRCodeGenerator::generateExpr(ExprAST *expr) {
	if (auto *numExpr = dynamic_cast<NumberExprAST *>(expr)) {
		return builder->getInt64(static_cast<int64_t>(numExpr->value));
	}

	throw std::runtime_error("Unsupported expression type");
}
