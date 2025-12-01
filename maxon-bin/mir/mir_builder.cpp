#include "mir_builder.h"
#include <stdexcept>

namespace mir {

//==============================================================================
// Module-level operations
//==============================================================================

MIRFunction *MIRBuilder::declareFunction(const std::string &name, MIRType *returnType,
										 const std::vector<MIRType *> &paramTypes) {
	MIRFunction *func = module->createFunction(name, returnType);
	func->isExternal = true;
	for (size_t i = 0; i < paramTypes.size(); ++i) {
		func->addParameter(paramTypes[i], "arg" + std::to_string(i));
	}
	return func;
}

MIRFunction *MIRBuilder::createFunction(const std::string &name, MIRType *returnType,
										const std::vector<std::pair<MIRType *, std::string>> &params) {
	MIRFunction *func = module->createFunction(name, returnType);
	func->isExternal = false;
	for (const auto &[type, paramName] : params) {
		func->addParameter(type, paramName);
	}
	currentFunction = func;
	return func;
}

MIRGlobal *MIRBuilder::createGlobal(const std::string &name, MIRType *type, bool isConstant) {
	MIRGlobal *global = module->createGlobal(name, type);
	global->isConstant = isConstant;
	return global;
}

MIRGlobal *MIRBuilder::createStringConstant(const std::string &name, const std::string &value) {
	// String type is array of i8 with size = length + 1 (for null terminator)
	MIRType *stringType = MIRType::getArray(MIRType::getInt8(), value.size() + 1);
	MIRGlobal *global = module->createGlobal(name, stringType);
	global->isConstant = true;
	global->setStringInitializer(value);
	return global;
}

//==============================================================================
// Function/Block context
//==============================================================================

MIRBasicBlock *MIRBuilder::createBasicBlock(const std::string &name) {
	if (!currentFunction) {
		throw std::runtime_error("Cannot create basic block without a current function");
	}
	MIRBasicBlock *block = currentFunction->createBasicBlock(name);
	return block;
}

//==============================================================================
// Value creation
//==============================================================================

MIRValue *MIRBuilder::getInt1(bool value) {
	return MIRValue::createConstantInt(MIRType::getInt1(), value ? 1 : 0);
}

MIRValue *MIRBuilder::getInt8(int8_t value) {
	return MIRValue::createConstantInt(MIRType::getInt8(), value);
}

MIRValue *MIRBuilder::getInt32(int32_t value) {
	return MIRValue::createConstantInt(MIRType::getInt32(), value);
}

MIRValue *MIRBuilder::getInt64(int64_t value) {
	return MIRValue::createConstantInt(MIRType::getInt64(), value);
}

MIRValue *MIRBuilder::getFloat64(double value) {
	return MIRValue::createConstantFloat(value);
}

MIRValue *MIRBuilder::getNull() {
	return MIRValue::createConstantNull();
}

//==============================================================================
// Helper methods
//==============================================================================

MIRInstruction *MIRBuilder::insertInstruction(std::unique_ptr<MIRInstruction> inst) {
	if (!insertBlock) {
		throw std::runtime_error("No insert point set");
	}
	inst->sourceLine = currentLine;
	inst->sourceColumn = currentColumn;
	MIRInstruction *ptr = inst.get();

	// Link result value back to its defining instruction
	if (ptr->result) {
		ptr->result->definingInst = ptr;
	}

	insertBlock->addInstruction(std::move(inst));
	return ptr;
}

MIRValue *MIRBuilder::createBinaryOp(MIROpcode op, MIRValue *lhs, MIRValue *rhs,
									 const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(op);
	inst->result = currentFunction->createVirtualReg(lhs->type);
	inst->operands = {lhs, rhs};
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

MIRValue *MIRBuilder::createCmpOp(MIROpcode op, MIRValue *lhs, MIRValue *rhs,
								  const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(op);
	inst->result = currentFunction->createVirtualReg(MIRType::getInt1());
	inst->operands = {lhs, rhs};
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

MIRValue *MIRBuilder::createCastOp(MIROpcode op, MIRValue *val, MIRType *destType,
								   const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(op);
	inst->result = currentFunction->createVirtualReg(destType);
	inst->operands = {val};
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

//==============================================================================
// Arithmetic instructions (integer)
//==============================================================================

MIRValue *MIRBuilder::createAdd(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::Add, lhs, rhs, name);
}

MIRValue *MIRBuilder::createSub(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::Sub, lhs, rhs, name);
}

MIRValue *MIRBuilder::createMul(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::Mul, lhs, rhs, name);
}

MIRValue *MIRBuilder::createSDiv(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::SDiv, lhs, rhs, name);
}

MIRValue *MIRBuilder::createSRem(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::SRem, lhs, rhs, name);
}

MIRValue *MIRBuilder::createUDiv(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::UDiv, lhs, rhs, name);
}

MIRValue *MIRBuilder::createURem(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::URem, lhs, rhs, name);
}

MIRValue *MIRBuilder::createNeg(MIRValue *val, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Neg);
	inst->result = currentFunction->createVirtualReg(val->type);
	inst->operands = {val};
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

//==============================================================================
// Bitwise instructions
//==============================================================================

MIRValue *MIRBuilder::createAnd(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::And, lhs, rhs, name);
}

MIRValue *MIRBuilder::createOr(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::Or, lhs, rhs, name);
}

MIRValue *MIRBuilder::createXor(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::Xor, lhs, rhs, name);
}

MIRValue *MIRBuilder::createShl(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::Shl, lhs, rhs, name);
}

MIRValue *MIRBuilder::createAShr(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::AShr, lhs, rhs, name);
}

MIRValue *MIRBuilder::createLShr(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::LShr, lhs, rhs, name);
}

//==============================================================================
// Arithmetic instructions (floating-point)
//==============================================================================

MIRValue *MIRBuilder::createFAdd(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::FAdd, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFSub(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::FSub, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFMul(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::FMul, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFDiv(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::FDiv, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFRem(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createBinaryOp(MIROpcode::FRem, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFNeg(MIRValue *val, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::FNeg);
	inst->result = currentFunction->createVirtualReg(val->type);
	inst->operands = {val};
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

//==============================================================================
// Comparison instructions
//==============================================================================

MIRValue *MIRBuilder::createICmpEq(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpEq, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpNe(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpNe, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpSLT(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpSLT, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpSLE(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpSLE, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpSGT(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpSGT, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpSGE(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpSGE, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpULT(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpULT, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpULE(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpULE, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpUGT(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpUGT, lhs, rhs, name);
}

MIRValue *MIRBuilder::createICmpUGE(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::ICmpUGE, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFCmpEq(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::FCmpEq, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFCmpNe(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::FCmpNe, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFCmpLT(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::FCmpLT, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFCmpLE(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::FCmpLE, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFCmpGT(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::FCmpGT, lhs, rhs, name);
}

MIRValue *MIRBuilder::createFCmpGE(MIRValue *lhs, MIRValue *rhs, const std::string &name) {
	return createCmpOp(MIROpcode::FCmpGE, lhs, rhs, name);
}

//==============================================================================
// Memory instructions
//==============================================================================

MIRValue *MIRBuilder::createAlloca(MIRType *type, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Alloca);
	// Alloca returns a pointer
	inst->result = currentFunction->createVirtualReg(MIRType::getPtr());
	// Store the allocated type for serialization and code generation
	inst->allocatedType = type;
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

MIRValue *MIRBuilder::createLoad(MIRType *type, MIRValue *ptr, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Load);
	inst->result = currentFunction->createVirtualReg(type);
	inst->operands = {ptr};
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

void MIRBuilder::createStore(MIRValue *value, MIRValue *ptr) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Store);
	inst->operands = {value, ptr};
	insertInstruction(std::move(inst));
}

MIRValue *MIRBuilder::createGEP(MIRType *baseType, MIRValue *ptr,
								const std::vector<MIRValue *> &indices, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::GetElementPtr);
	inst->result = currentFunction->createVirtualReg(MIRType::getPtr());
	inst->elementType = baseType; // Store the element type for code generation
	inst->operands.push_back(ptr);
	for (auto *idx : indices) {
		inst->operands.push_back(idx);
	}
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

MIRValue *MIRBuilder::createArrayGEP(MIRType *elemType, MIRValue *arrayPtr, MIRValue *index,
									 const std::string &name) {
	// Ensure index is i64 for consistent GEP indexing
	MIRValue *index64 = index;
	if (index->type != MIRType::getInt64()) {
		if (index->kind == MIRValueKind::ConstantInt) {
			// For constant indices, create an i64 constant directly
			index64 = getInt64(index->intValue);
		} else if (index->type->isInteger()) {
			// For variable indices, sign-extend to i64
			index64 = createSExt(index, MIRType::getInt64(), "idx.ext");
		}
	}

	// For struct/complex element types accessed via raw pointer (unsized arrays),
	// use a single index. For simple types (int, char, etc.) via array types,
	// use two indices {0, idx} for LLVM-style array GEP.
	// The single-index form is: getelementptr %T, ptr %p, i64 <idx>
	// which computes: %p + idx * sizeof(%T)
	if (elemType->isStruct()) {
		return createGEP(elemType, arrayPtr, {index64}, name);
	}
	return createGEP(elemType, arrayPtr, {getInt64(0), index64}, name);
}

MIRValue *MIRBuilder::createStructGEP(MIRType *structType, MIRValue *structPtr, uint32_t fieldIndex,
									  const std::string &name) {
	return createGEP(structType, structPtr, {getInt32(0), getInt32(fieldIndex)}, name);
}

//==============================================================================
// Conversion instructions
//==============================================================================

MIRValue *MIRBuilder::createTrunc(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::Trunc, val, destType, name);
}

MIRValue *MIRBuilder::createZExt(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::ZExt, val, destType, name);
}

MIRValue *MIRBuilder::createSExt(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::SExt, val, destType, name);
}

MIRValue *MIRBuilder::createFPToSI(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::FPToSI, val, destType, name);
}

MIRValue *MIRBuilder::createSIToFP(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::SIToFP, val, destType, name);
}

MIRValue *MIRBuilder::createPtrToInt(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::PtrToInt, val, destType, name);
}

MIRValue *MIRBuilder::createIntToPtr(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::IntToPtr, val, destType, name);
}

MIRValue *MIRBuilder::createBitcast(MIRValue *val, MIRType *destType, const std::string &name) {
	return createCastOp(MIROpcode::Bitcast, val, destType, name);
}

//==============================================================================
// Control flow instructions
//==============================================================================

void MIRBuilder::createBr(MIRBasicBlock *dest) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Br);
	inst->operands = {MIRValue::createBlockRef(dest)};

	// Update CFG
	insertBlock->successors.push_back(dest);
	dest->predecessors.push_back(insertBlock);

	insertInstruction(std::move(inst));
}

void MIRBuilder::createCondBr(MIRValue *cond, MIRBasicBlock *trueDest, MIRBasicBlock *falseDest) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::CondBr);
	inst->operands = {cond, MIRValue::createBlockRef(trueDest), MIRValue::createBlockRef(falseDest)};

	// Update CFG
	insertBlock->successors.push_back(trueDest);
	insertBlock->successors.push_back(falseDest);
	trueDest->predecessors.push_back(insertBlock);
	falseDest->predecessors.push_back(insertBlock);

	insertInstruction(std::move(inst));
}

void MIRBuilder::createRet(MIRValue *value) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Ret);
	inst->operands = {value};
	insertInstruction(std::move(inst));
}

void MIRBuilder::createRetVoid() {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::RetVoid);
	insertInstruction(std::move(inst));
}

//==============================================================================
// Call instructions
//==============================================================================

MIRValue *MIRBuilder::createCall(MIRFunction *callee, const std::vector<MIRValue *> &args,
								 const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Call);
	inst->calleeName = callee->name;
	inst->calleeFunc = callee;
	inst->operands = args;

	MIRValue *result = nullptr;
	if (!callee->returnType->isVoid()) {
		inst->result = currentFunction->createVirtualReg(callee->returnType);
		result = inst->result;
	}

	insertInstruction(std::move(inst));
	return result;
}

MIRValue *MIRBuilder::createCall(const std::string &calleeName, MIRType *returnType,
								 const std::vector<MIRValue *> &args, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Call);
	inst->calleeName = calleeName;
	inst->operands = args;

	MIRValue *result = nullptr;
	if (!returnType->isVoid()) {
		inst->result = currentFunction->createVirtualReg(returnType);
		result = inst->result;
	}

	insertInstruction(std::move(inst));
	return result;
}

MIRValue *MIRBuilder::createCallIndirect(MIRValue *funcPtr, MIRType *returnType,
										 const std::vector<MIRType *> &paramTypes,
										 const std::vector<MIRValue *> &args,
										 const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::CallIndirect);

	// First operand is the function pointer
	inst->operands.push_back(funcPtr);
	// Remaining operands are the call arguments
	for (auto *arg : args) {
		inst->operands.push_back(arg);
	}

	// Store type information for the call
	inst->indirectReturnType = returnType;
	inst->indirectParamTypes = paramTypes;

	MIRValue *result = nullptr;
	if (!returnType->isVoid()) {
		inst->result = currentFunction->createVirtualReg(returnType);
		result = inst->result;
	}

	insertInstruction(std::move(inst));
	return result;
}

//==============================================================================
// SSA Phi node
//==============================================================================

MIRValue *MIRBuilder::createPhi(MIRType *type, const std::string &name) {
	auto inst = std::make_unique<MIRInstruction>(MIROpcode::Phi);
	inst->result = currentFunction->createVirtualReg(type);
	MIRValue *result = inst->result;
	insertInstruction(std::move(inst));
	return result;
}

void MIRBuilder::addPhiIncoming(MIRValue *phi, MIRValue *value, MIRBasicBlock *block) {
	// Find the phi instruction
	if (phi->kind != MIRValueKind::VirtualReg || !phi->definingInst) {
		throw std::runtime_error("Invalid phi value");
	}
	MIRInstruction *inst = phi->definingInst;
	if (inst->opcode != MIROpcode::Phi) {
		throw std::runtime_error("Value is not a phi node");
	}
	inst->phiIncoming.push_back({value, block});
}

} // namespace mir
