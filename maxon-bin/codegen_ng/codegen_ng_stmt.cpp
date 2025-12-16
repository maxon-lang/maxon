/**
 * MIR Code Generator - Statement Generation
 */

#include "../codegen_ng.h"

#include <stdexcept>

void MIRCodeGenerator::generateStmt(StmtAST *stmt, mir::MIRFunction *function) {
	(void)function; // Not used yet

	// Return statement
	if (auto *retStmt = dynamic_cast<ReturnStmtAST *>(stmt)) {
		if (retStmt->value) {
			mir::MIRValue *retVal = generateExpr(retStmt->value.get());
			builder->createRet(retVal);
		} else {
			builder->createRetVoid();
		}
		return;
	}

	// Let declaration (immutable variable)
	if (auto *letDecl = dynamic_cast<LetDeclStmtAST *>(stmt)) {
		mir::MIRType *type = getTypeFromString(letDecl->type);

		// Create alloca for the variable
		mir::MIRValue *alloca = builder->createAlloca(type, letDecl->name);

		// Generate initializer and store
		if (letDecl->initializer) {
			mir::MIRValue *initVal = generateExpr(letDecl->initializer.get());
			builder->createStore(initVal, alloca);
		}

		// Track the variable
		namedValues[letDecl->name] = alloca;
		return;
	}

	// Var declaration (mutable variable)
	if (auto *varDecl = dynamic_cast<VarDeclStmtAST *>(stmt)) {
		mir::MIRType *type = getTypeFromString(varDecl->type);

		// Create alloca for the variable
		mir::MIRValue *alloca = builder->createAlloca(type, varDecl->name);

		// Generate initializer and store
		if (varDecl->initializer) {
			mir::MIRValue *initVal = generateExpr(varDecl->initializer.get());
			builder->createStore(initVal, alloca);
		}

		// Track the variable
		namedValues[varDecl->name] = alloca;
		return;
	}

	throw std::runtime_error("Unsupported statement type");
}
