#pragma once

#include "mir.h"

namespace mir {

//==============================================================================
// MIR Builder - Convenient API for constructing MIR
//==============================================================================

class MIRBuilder {
  private:
	MIRModule *module;
	MIRFunction *currentFunction = nullptr;
	MIRBasicBlock *insertBlock = nullptr;

	// Source location tracking
	int currentLine = 0;
	int currentColumn = 0;

  public:
	explicit MIRBuilder(MIRModule *mod) : module(mod) {}

	//--------------------------------------------------------------------------
	// Module-level operations
	//--------------------------------------------------------------------------

	MIRModule *getModule() { return module; }

	// Create a function declaration (external)
	MIRFunction *declareFunction(const std::string &name, MIRType *returnType,
								 const std::vector<MIRType *> &paramTypes);

	// Create a function definition
	MIRFunction *createFunction(const std::string &name, MIRType *returnType,
								const std::vector<std::pair<MIRType *, std::string>> &params);

	// Create a global variable
	MIRGlobal *createGlobal(const std::string &name, MIRType *type, bool isConstant = false);

	// Create a global string constant
	MIRGlobal *createStringConstant(const std::string &name, const std::string &value);

	//--------------------------------------------------------------------------
	// Function/Block context
	//--------------------------------------------------------------------------

	void setFunction(MIRFunction *func) { currentFunction = func; }
	MIRFunction *getFunction() { return currentFunction; }

	MIRBasicBlock *createBasicBlock(const std::string &name);
	void setInsertPoint(MIRBasicBlock *block) { insertBlock = block; }
	MIRBasicBlock *getInsertBlock() { return insertBlock; }

	// Source location for debug info
	void setLocation(int line, int column) {
		currentLine = line;
		currentColumn = column;
	}

	//--------------------------------------------------------------------------
	// Value creation
	//--------------------------------------------------------------------------

	MIRValue *getInt1(bool value);
	MIRValue *getInt8(int8_t value);
	MIRValue *getInt32(int32_t value);
	MIRValue *getInt64(int64_t value);
	MIRValue *getFloat64(double value);
	MIRValue *getNull();

	//--------------------------------------------------------------------------
	// Arithmetic instructions (integer)
	//--------------------------------------------------------------------------

	MIRValue *createAdd(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createSub(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createMul(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createSDiv(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createSRem(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createUDiv(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createURem(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createNeg(MIRValue *val, const std::string &name = "");

	//--------------------------------------------------------------------------
	// Bitwise instructions
	//--------------------------------------------------------------------------

	MIRValue *createAnd(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createOr(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createXor(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createShl(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createAShr(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createLShr(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");

	//--------------------------------------------------------------------------
	// Arithmetic instructions (floating-point)
	//--------------------------------------------------------------------------

	MIRValue *createFAdd(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFSub(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFMul(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFDiv(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFRem(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFNeg(MIRValue *val, const std::string &name = "");

	//--------------------------------------------------------------------------
	// Comparison instructions
	//--------------------------------------------------------------------------

	// Integer comparisons
	MIRValue *createICmpEq(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpNe(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpSLT(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpSLE(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpSGT(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpSGE(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpULT(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpULE(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpUGT(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createICmpUGE(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");

	// Floating-point comparisons
	MIRValue *createFCmpEq(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFCmpNe(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFCmpLT(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFCmpLE(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFCmpGT(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");
	MIRValue *createFCmpGE(MIRValue *lhs, MIRValue *rhs, const std::string &name = "");

	//--------------------------------------------------------------------------
	// Memory instructions
	//--------------------------------------------------------------------------

	// Allocate stack space
	MIRValue *createAlloca(MIRType *type, const std::string &name = "");

	// Load from memory
	MIRValue *createLoad(MIRType *type, MIRValue *ptr, const std::string &name = "");

	// Store to memory
	void createStore(MIRValue *value, MIRValue *ptr);

	// Get element pointer (for arrays and structs)
	MIRValue *createGEP(MIRType *baseType, MIRValue *ptr,
						const std::vector<MIRValue *> &indices, const std::string &name = "");

	// Convenience: array element pointer
	MIRValue *createArrayGEP(MIRType *elemType, MIRValue *arrayPtr, MIRValue *index,
							 const std::string &name = "");

	// Convenience: struct field pointer
	MIRValue *createStructGEP(MIRType *structType, MIRValue *structPtr, uint32_t fieldIndex,
							  const std::string &name = "");

	//--------------------------------------------------------------------------
	// Conversion instructions
	//--------------------------------------------------------------------------

	MIRValue *createTrunc(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createZExt(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createSExt(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createFPToSI(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createSIToFP(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createPtrToInt(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createIntToPtr(MIRValue *val, MIRType *destType, const std::string &name = "");
	MIRValue *createBitcast(MIRValue *val, MIRType *destType, const std::string &name = "");

	//--------------------------------------------------------------------------
	// Control flow instructions
	//--------------------------------------------------------------------------

	// Unconditional branch
	void createBr(MIRBasicBlock *dest);

	// Conditional branch
	void createCondBr(MIRValue *cond, MIRBasicBlock *trueDest, MIRBasicBlock *falseDest);

	// Return
	void createRet(MIRValue *value);
	void createRetVoid();

	//--------------------------------------------------------------------------
	// Call instructions
	//--------------------------------------------------------------------------

	MIRValue *createCall(MIRFunction *callee, const std::vector<MIRValue *> &args,
						 const std::string &name = "");
	MIRValue *createCall(const std::string &calleeName, MIRType *returnType,
						 const std::vector<MIRValue *> &args, const std::string &name = "");

	//--------------------------------------------------------------------------
	// SSA Phi node
	//--------------------------------------------------------------------------

	MIRValue *createPhi(MIRType *type, const std::string &name = "");
	void addPhiIncoming(MIRValue *phi, MIRValue *value, MIRBasicBlock *block);

  private:
	// Helper to create a binary instruction
	MIRValue *createBinaryOp(MIROpcode op, MIRValue *lhs, MIRValue *rhs, const std::string &name);

	// Helper to create a comparison instruction
	MIRValue *createCmpOp(MIROpcode op, MIRValue *lhs, MIRValue *rhs, const std::string &name);

	// Helper to create a conversion instruction
	MIRValue *createCastOp(MIROpcode op, MIRValue *val, MIRType *destType, const std::string &name);

	// Helper to insert instruction at current insert point
	MIRInstruction *insertInstruction(std::unique_ptr<MIRInstruction> inst);
};

} // namespace mir
