/**
 * MIR Code Generator - Statement Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

//==============================================================================
// Statement generate() implementations
//==============================================================================

void ReturnStmtAST::generate(MIRCodeGenerator &cg) const {
	if (value) {
		mir::MIRValue *retVal = value->generate(cg);
		cg.getBuilder()->createRet(retVal);
	} else {
		cg.getBuilder()->createRetVoid();
	}
}

void LetDeclStmtAST::generate(MIRCodeGenerator &cg) const {
	cg.generateLocalVariable(name, type, initializer.get(), nullptr);
}

void VarDeclStmtAST::generate(MIRCodeGenerator &cg) const {
	cg.generateLocalVariable(name, type, initializer.get(), nullptr);
}

void AssignStmtAST::generate(MIRCodeGenerator &cg) const {
	mir::MIRValue *alloca = cg.lookupVariable(name);
	if (!alloca) {
		throw std::runtime_error("Undefined variable in assignment: " + name +
								 " at line " + std::to_string(line));
	}
	mir::MIRValue *val = value->generate(cg);
	cg.getBuilder()->createStore(val, alloca);
}

void ExprStmtAST::generate(MIRCodeGenerator &cg) const {
	expression->generate(cg);
}

//==============================================================================
// MIRCodeGenerator::generateStmt - now delegates to AST
//==============================================================================

void MIRCodeGenerator::generateStmt(StmtAST *stmt, mir::MIRFunction *function) {
	(void)function;
	stmt->generate(*this);
}
