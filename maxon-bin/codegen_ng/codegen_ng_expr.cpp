/**
 * MIR Code Generator - Expression Generation
 */

#include "../codegen_ng.h"

#include "../ast.h"
#include <stdexcept>

//==============================================================================
// Expression generate() implementations
//==============================================================================

mir::MIRValue *NumberExprAST::generate(MIRCodeGenerator &cg) const {
	return cg.getBuilder()->getInt64(static_cast<int64_t>(value));
}

mir::MIRValue *VariableExprAST::generate(MIRCodeGenerator &cg) const {
	mir::MIRValue *alloca = cg.lookupVariable(name);
	if (!alloca) {
		throw std::runtime_error("Unknown variable: " + name +
								 " at line " + std::to_string(line));
	}
	return cg.getBuilder()->createLoad(mir::MIRType::getInt64(), alloca, name);
}

mir::MIRValue *BinaryExprAST::generate(MIRCodeGenerator &cg) const {
	mir::MIRValue *leftVal = left->generate(cg);
	mir::MIRValue *rightVal = right->generate(cg);

	switch (op) {
	case '+':
		return cg.getBuilder()->createAdd(leftVal, rightVal);
	case '-':
		return cg.getBuilder()->createSub(leftVal, rightVal);
	case '*':
		return cg.getBuilder()->createMul(leftVal, rightVal);
	default:
		throw std::runtime_error("Unsupported binary operator: '" + std::string(1, op) +
								 "' at line " + std::to_string(line));
	}
}

mir::MIRValue *CallExprAST::generate(MIRCodeGenerator &cg) const {
	mir::MIRFunction *calleeFunc = cg.getModule()->getFunction(callee);
	if (!calleeFunc) {
		throw std::runtime_error("Unknown function: " + callee +
								 " at line " + std::to_string(line));
	}

	std::vector<mir::MIRValue *> argValues;
	for (const auto &arg : args) {
		argValues.push_back(arg.value->generate(cg));
	}

	return cg.getBuilder()->createCall(calleeFunc, argValues);
}

//==============================================================================
// MIRCodeGenerator::generateExpr - now delegates to AST
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateExpr(ExprAST *expr) {
	return expr->generate(*this);
}
