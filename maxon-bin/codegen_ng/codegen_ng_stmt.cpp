/**
 * MIR Code Generator - Statement Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

void MIRCodeGenerator::generateStmt(StmtAST *stmt, mir::MIRFunction *function) {
	(void)function; // Not used yet

	if (auto *retStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		if (retStmt->value) {
			mir::MIRValue *retVal = generateExpr(retStmt->value.get());
			builder->createRet(retVal);
		} else {
			builder->createRetVoid();
		}
		return;
	}

	throw std::runtime_error("Unsupported statement type");
}
