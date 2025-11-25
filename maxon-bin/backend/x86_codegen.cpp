#include "x86_codegen.h"
#include <algorithm>
#include <cstring>
#include <stdexcept>

namespace backend {

//==============================================================================
// Main generation entry point
//==============================================================================

void X86CodeGen::generate(mir::MIRModule *mod) {
	module = mod;

	// Generate data section for globals
	generateGlobals();

	// Generate code for each function
	for (auto &func : mod->functions) {
		if (!func->isExternal) {
			generateFunction(func.get());
		}
	}
}

void X86CodeGen::generateGlobals() {
	for (auto &global : module->globals) {
		size_t offset = dataSection.size();
		globalOffsets[global->name] = offset;

		if (global->isStringConstant) {
			// String constant
			dataSection.insert(dataSection.end(),
							   global->stringValue.begin(),
							   global->stringValue.end());
			dataSection.push_back(0); // Null terminator
		} else if (global->hasInitializer) {
			// Initialized data
			dataSection.insert(dataSection.end(),
							   global->initializer.begin(),
							   global->initializer.end());
		} else {
			// Zero-initialized (BSS)
			dataSection.resize(dataSection.size() + global->type->sizeInBytes, 0);
		}

		// Align to 8 bytes
		while (dataSection.size() % 8 != 0) {
			dataSection.push_back(0);
		}
	}
}

//==============================================================================
// Function generation
//==============================================================================

void X86CodeGen::generateFunction(mir::MIRFunction *func) {
	currentFunction = func;
	encoder.clear();
	blockOffsets.clear();
	pendingBranches.clear();
	relocations.clear();

	// Perform register allocation
	allocateRegisters(func);

	// Generate prologue
	generatePrologue();

	// Generate each basic block
	for (auto &block : func->basicBlocks) {
		generateBasicBlock(block.get());
	}

	// Resolve branch fixups
	resolveBranchFixups();

	// Store generated code
	FunctionCode fc;
	fc.name = func->name;
	fc.code = encoder.getCode();
	fc.relocations = relocations;
	functionCodes.push_back(std::move(fc));
}

void X86CodeGen::generatePrologue() {
	// Standard x64 prologue
	// push rbp
	encoder.pushR(X86Reg::RBP);
	// mov rbp, rsp
	encoder.movRR64(X86Reg::RBP, X86Reg::RSP);

	// Allocate stack space
	if (regAlloc.frameSize > 0) {
		encoder.subRI64(X86Reg::RSP, regAlloc.frameSize);
	}

	// Save callee-saved registers
	for (X86Reg reg : regAlloc.usedCalleeSaved) {
		encoder.pushR(reg);
	}

	// Windows x64: Allocate shadow space if needed
	if (callingConv == CallingConv::Win64) {
		// Shadow space is handled in frameSize calculation
	}
}

void X86CodeGen::generateEpilogue() {
	// Restore callee-saved registers (reverse order)
	for (auto it = regAlloc.usedCalleeSaved.rbegin();
		 it != regAlloc.usedCalleeSaved.rend(); ++it) {
		encoder.popR(*it);
	}

	// mov rsp, rbp
	encoder.movRR64(X86Reg::RSP, X86Reg::RBP);
	// pop rbp
	encoder.popR(X86Reg::RBP);
	// ret
	encoder.ret();
}

void X86CodeGen::generateBasicBlock(mir::MIRBasicBlock *block) {
	// Record block offset for branch resolution
	blockOffsets[block] = encoder.getOffset();

	// Generate each instruction
	for (auto &inst : block->instructions) {
		generateInstruction(inst.get());
	}
}

void X86CodeGen::generateInstruction(mir::MIRInstruction *inst) {
	switch (inst->opcode) {
	// Arithmetic (integer)
	case mir::MIROpcode::Add:
		genAdd(inst);
		break;
	case mir::MIROpcode::Sub:
		genSub(inst);
		break;
	case mir::MIROpcode::Mul:
		genMul(inst);
		break;
	case mir::MIROpcode::SDiv:
		genDiv(inst, true);
		break;
	case mir::MIROpcode::UDiv:
		genDiv(inst, false);
		break;
	case mir::MIROpcode::SRem:
		genRem(inst, true);
		break;
	case mir::MIROpcode::URem:
		genRem(inst, false);
		break;
	case mir::MIROpcode::Neg:
		genNeg(inst);
		break;

	// Arithmetic (floating-point)
	case mir::MIROpcode::FAdd:
		genFAdd(inst);
		break;
	case mir::MIROpcode::FSub:
		genFSub(inst);
		break;
	case mir::MIROpcode::FMul:
		genFMul(inst);
		break;
	case mir::MIROpcode::FDiv:
		genFDiv(inst);
		break;
	case mir::MIROpcode::FNeg:
		genFNeg(inst);
		break;

	// Bitwise
	case mir::MIROpcode::And:
		genAnd(inst);
		break;
	case mir::MIROpcode::Or:
		genOr(inst);
		break;
	case mir::MIROpcode::Xor:
		genXor(inst);
		break;
	case mir::MIROpcode::Shl:
		genShl(inst);
		break;
	case mir::MIROpcode::LShr:
		genShr(inst, false);
		break;
	case mir::MIROpcode::AShr:
		genShr(inst, true);
		break;

	// Comparisons
	case mir::MIROpcode::ICmpEq:
	case mir::MIROpcode::ICmpNe:
	case mir::MIROpcode::ICmpSLT:
	case mir::MIROpcode::ICmpSLE:
	case mir::MIROpcode::ICmpSGT:
	case mir::MIROpcode::ICmpSGE:
	case mir::MIROpcode::ICmpULT:
	case mir::MIROpcode::ICmpULE:
	case mir::MIROpcode::ICmpUGT:
	case mir::MIROpcode::ICmpUGE:
		genICmp(inst);
		break;

	case mir::MIROpcode::FCmpEq:
	case mir::MIROpcode::FCmpNe:
	case mir::MIROpcode::FCmpLT:
	case mir::MIROpcode::FCmpLE:
	case mir::MIROpcode::FCmpGT:
	case mir::MIROpcode::FCmpGE:
		genFCmp(inst);
		break;

	// Memory
	case mir::MIROpcode::Alloca:
		genAlloca(inst);
		break;
	case mir::MIROpcode::Load:
		genLoad(inst);
		break;
	case mir::MIROpcode::Store:
		genStore(inst);
		break;
	case mir::MIROpcode::GetElementPtr:
		genGEP(inst);
		break;

	// Conversions
	case mir::MIROpcode::Trunc:
		genTrunc(inst);
		break;
	case mir::MIROpcode::ZExt:
		genZExt(inst);
		break;
	case mir::MIROpcode::SExt:
		genSExt(inst);
		break;
	case mir::MIROpcode::FPToSI:
		genFPToSI(inst);
		break;
	case mir::MIROpcode::SIToFP:
		genSIToFP(inst);
		break;

	// Control flow
	case mir::MIROpcode::Br:
		genBr(inst);
		break;
	case mir::MIROpcode::CondBr:
		genCondBr(inst);
		break;
	case mir::MIROpcode::Ret:
		genRet(inst);
		break;
	case mir::MIROpcode::RetVoid:
		generateEpilogue();
		break;

	// Calls
	case mir::MIROpcode::Call:
		genCall(inst);
		break;

	// SSA
	case mir::MIROpcode::Phi:
		genPhi(inst);
		break;
	case mir::MIROpcode::Copy:
		genCopy(inst);
		break;

	default:
		throw std::runtime_error("Unhandled MIR opcode in code generation");
	}
}

//==============================================================================
// Simple Register Allocation
//==============================================================================

void X86CodeGen::allocateRegisters(mir::MIRFunction *func) {
	regAlloc = RegAllocInfo();

	// Available general-purpose registers (excluding RSP, RBP)
	std::vector<X86Reg> availableRegs;
	if (callingConv == CallingConv::Win64) {
		// Windows: RAX, RCX, RDX, R8-R11 are caller-saved (volatile)
		// RBX, RSI, RDI, R12-R15 are callee-saved
		availableRegs = {X86Reg::RAX, X86Reg::RCX, X86Reg::RDX,
						 X86Reg::R8, X86Reg::R9, X86Reg::R10, X86Reg::R11,
						 X86Reg::RBX, X86Reg::RSI, X86Reg::RDI,
						 X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
	} else {
		// System V: RAX, RCX, RDX, RSI, RDI, R8-R11 are caller-saved
		// RBX, R12-R15 are callee-saved
		availableRegs = {X86Reg::RAX, X86Reg::RCX, X86Reg::RDX,
						 X86Reg::RSI, X86Reg::RDI,
						 X86Reg::R8, X86Reg::R9, X86Reg::R10, X86Reg::R11,
						 X86Reg::RBX, X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
	}

	// Simple allocation: assign registers to virtual registers in order
	// This is a placeholder - real implementation would use linear scan
	size_t regIdx = 0;
	int32_t stackOffset = -8; // Start below RBP

	// Reserve stack space for parameters (moved to stack in prologue)
	for (size_t i = 0; i < func->parameters.size(); ++i) {
		auto *param = func->parameters[i];
		if (regIdx < availableRegs.size()) {
			regAlloc.regMap[param->regId] = availableRegs[regIdx++];
		} else {
			regAlloc.stackSlots[param->regId] = stackOffset;
			stackOffset -= 8;
		}
	}

	// Allocate registers/stack for virtual registers in basic blocks
	for (auto &block : func->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->result) {
				uint32_t regId = inst->result->regId;
				if (regAlloc.regMap.find(regId) == regAlloc.regMap.end() &&
					regAlloc.stackSlots.find(regId) == regAlloc.stackSlots.end()) {

					// Try to allocate a register
					if (regIdx < availableRegs.size()) {
						regAlloc.regMap[regId] = availableRegs[regIdx++];
					} else {
						// Spill to stack
						regAlloc.stackSlots[regId] = stackOffset;
						stackOffset -= 8;
					}
				}
			}
		}
	}

	// Calculate frame size (aligned to 16 bytes)
	regAlloc.frameSize = static_cast<uint32_t>(-stackOffset);
	if (regAlloc.frameSize % 16 != 0) {
		regAlloc.frameSize += 16 - (regAlloc.frameSize % 16);
	}

	// Add shadow space for Windows
	if (callingConv == CallingConv::Win64) {
		regAlloc.frameSize += 32; // 4 * 8 bytes shadow space
	}
}

X86Reg X86CodeGen::getAllocatedReg(mir::MIRValue *value) {
	if (value->kind == mir::MIRValueKind::VirtualReg ||
		value->kind == mir::MIRValueKind::Parameter) {
		auto it = regAlloc.regMap.find(value->regId);
		if (it != regAlloc.regMap.end()) {
			return it->second;
		}
	}
	return X86Reg::None;
}

X86Mem X86CodeGen::getStackSlot(mir::MIRValue *value) {
	auto it = regAlloc.stackSlots.find(value->regId);
	if (it != regAlloc.stackSlots.end()) {
		return X86Mem(X86Reg::RBP, it->second);
	}
	throw std::runtime_error("Value not found in stack slots");
}

//==============================================================================
// Value loading helpers
//==============================================================================

X86Reg X86CodeGen::loadValue(mir::MIRValue *value, X86Reg hint) {
	switch (value->kind) {
	case mir::MIRValueKind::ConstantInt: {
		encoder.movRI64(hint, static_cast<uint64_t>(value->intValue));
		return hint;
	}
	case mir::MIRValueKind::ConstantNull: {
		encoder.xorRR64(hint, hint); // XOR reg, reg to zero
		return hint;
	}
	case mir::MIRValueKind::VirtualReg:
	case mir::MIRValueKind::Parameter: {
		X86Reg reg = getAllocatedReg(value);
		if (reg != X86Reg::None) {
			if (reg != hint) {
				encoder.movRR64(hint, reg);
			}
			return hint;
		}
		// Load from stack
		X86Mem slot = getStackSlot(value);
		encoder.movRM64(hint, slot);
		return hint;
	}
	case mir::MIRValueKind::Global: {
		// Load address of global (will need relocation)
		encoder.lea64(hint, X86Mem::RipRel(0));
		relocations.push_back({Relocation::Type::GlobalRef,
							   encoder.getOffset() - 4,
							   value->name});
		return hint;
	}
	default:
		throw std::runtime_error("Cannot load value type");
	}
}

X86Reg X86CodeGen::loadValueFloat(mir::MIRValue *value, X86Reg hint) {
	if (value->kind == mir::MIRValueKind::ConstantFloat) {
		// Store constant in data section and load from there
		size_t offset = emitConstantFloat(value->floatValue);
		encoder.movsdRM(hint, X86Mem::RipRel(0));
		// Add relocation for the constant
		relocations.push_back({Relocation::Type::GlobalRef,
							   encoder.getOffset() - 4,
							   ".const" + std::to_string(offset)});
		return hint;
	}

	X86Reg reg = getAllocatedReg(value);
	if (reg != X86Reg::None) {
		if (reg != hint) {
			encoder.movsdRR(hint, reg);
		}
		return hint;
	}

	// Load from stack
	X86Mem slot = getStackSlot(value);
	encoder.movsdRM(hint, slot);
	return hint;
}

void X86CodeGen::storeResult(mir::MIRValue *result, X86Reg reg) {
	if (!result)
		return;

	X86Reg allocReg = getAllocatedReg(result);
	if (allocReg != X86Reg::None) {
		if (allocReg != reg) {
			encoder.movRR64(allocReg, reg);
		}
		return;
	}

	// Store to stack
	X86Mem slot = getStackSlot(result);
	encoder.movMR64(slot, reg);
}

void X86CodeGen::storeResultFloat(mir::MIRValue *result, X86Reg xmmReg) {
	if (!result)
		return;

	X86Reg allocReg = getAllocatedReg(result);
	if (allocReg != X86Reg::None) {
		if (allocReg != xmmReg) {
			encoder.movsdRR(allocReg, xmmReg);
		}
		return;
	}

	// Store to stack
	X86Mem slot = getStackSlot(result);
	encoder.movsdMR(slot, xmmReg);
}

//==============================================================================
// Integer Arithmetic
//==============================================================================

void X86CodeGen::genAdd(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.addRR32(lhs, rhs);
	} else {
		encoder.addRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genSub(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.subRR32(lhs, rhs);
	} else {
		encoder.subRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genMul(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.imulRR32(lhs, rhs);
	} else {
		encoder.imulRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genDiv(mir::MIRInstruction *inst, bool isSigned) {
	// Division uses RAX for dividend, RDX:RAX for 128-bit dividend
	// Result in RAX
	loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg divisor = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		if (isSigned) {
			encoder.cdq(); // Sign-extend EAX into EDX:EAX
			encoder.idivR32(divisor);
		} else {
			encoder.xorRR32(X86Reg::RDX, X86Reg::RDX); // Zero EDX
			encoder.divR32(divisor);
		}
	} else {
		if (isSigned) {
			encoder.cqo(); // Sign-extend RAX into RDX:RAX
			encoder.idivR64(divisor);
		} else {
			encoder.xorRR64(X86Reg::RDX, X86Reg::RDX); // Zero RDX
			encoder.divR64(divisor);
		}
	}

	storeResult(inst->result, X86Reg::RAX);
}

void X86CodeGen::genRem(mir::MIRInstruction *inst, bool isSigned) {
	// Remainder is in RDX after division
	loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg divisor = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		if (isSigned) {
			encoder.cdq();
			encoder.idivR32(divisor);
		} else {
			encoder.xorRR32(X86Reg::RDX, X86Reg::RDX);
			encoder.divR32(divisor);
		}
	} else {
		if (isSigned) {
			encoder.cqo();
			encoder.idivR64(divisor);
		} else {
			encoder.xorRR64(X86Reg::RDX, X86Reg::RDX);
			encoder.divR64(divisor);
		}
	}

	storeResult(inst->result, X86Reg::RDX);
}

void X86CodeGen::genNeg(mir::MIRInstruction *inst) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.negR32(val);
	} else {
		encoder.negR64(val);
	}

	storeResult(inst->result, val);
}

//==============================================================================
// Floating-Point Arithmetic
//==============================================================================

void X86CodeGen::genFAdd(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	X86Reg rhs = loadValueFloat(inst->operands[1], X86Reg::XMM1);
	encoder.addsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFSub(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	X86Reg rhs = loadValueFloat(inst->operands[1], X86Reg::XMM1);
	encoder.subsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFMul(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	X86Reg rhs = loadValueFloat(inst->operands[1], X86Reg::XMM1);
	encoder.mulsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFDiv(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	X86Reg rhs = loadValueFloat(inst->operands[1], X86Reg::XMM1);
	encoder.divsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFNeg(mir::MIRInstruction *inst) {
	X86Reg val = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	// XOR with sign bit mask to negate
	// This requires a constant in memory - simplified version using subtraction from 0
	encoder.xorpdRR(X86Reg::XMM1, X86Reg::XMM1); // Zero XMM1
	encoder.subsdRR(X86Reg::XMM1, val);
	storeResultFloat(inst->result, X86Reg::XMM1);
}

//==============================================================================
// Bitwise Operations
//==============================================================================

void X86CodeGen::genAnd(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.andRR32(lhs, rhs);
	} else {
		encoder.andRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genOr(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.orRR32(lhs, rhs);
	} else {
		encoder.orRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genXor(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.xorRR32(lhs, rhs);
	} else {
		encoder.xorRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genShl(mir::MIRInstruction *inst) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);

	// Shift amount must be in CL
	if (inst->operands[1]->kind == mir::MIRValueKind::ConstantInt) {
		uint8_t imm = static_cast<uint8_t>(inst->operands[1]->intValue);
		if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
			encoder.shlR32_imm(val, imm);
		} else {
			encoder.shlR64_imm(val, imm);
		}
	} else {
		loadValue(inst->operands[1], X86Reg::RCX);
		if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
			encoder.shlR32_CL(val);
		} else {
			encoder.shlR64_CL(val);
		}
	}

	storeResult(inst->result, val);
}

void X86CodeGen::genShr(mir::MIRInstruction *inst, bool isArithmetic) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);

	if (inst->operands[1]->kind == mir::MIRValueKind::ConstantInt) {
		uint8_t imm = static_cast<uint8_t>(inst->operands[1]->intValue);
		if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
			if (isArithmetic) {
				encoder.sarR32_imm(val, imm);
			} else {
				encoder.shrR32_imm(val, imm);
			}
		} else {
			if (isArithmetic) {
				encoder.sarR64_imm(val, imm);
			} else {
				encoder.shrR64_imm(val, imm);
			}
		}
	} else {
		loadValue(inst->operands[1], X86Reg::RCX);
		if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
			if (isArithmetic) {
				encoder.sarR32_CL(val);
			} else {
				encoder.shrR32_CL(val);
			}
		} else {
			if (isArithmetic) {
				encoder.sarR64_CL(val);
			} else {
				encoder.shrR64_CL(val);
			}
		}
	}

	storeResult(inst->result, val);
}

//==============================================================================
// Comparisons
//==============================================================================

X86Cond X86CodeGen::getConditionCode(mir::MIROpcode op) {
	switch (op) {
	case mir::MIROpcode::ICmpEq:
		return X86Cond::E;
	case mir::MIROpcode::ICmpNe:
		return X86Cond::NE;
	case mir::MIROpcode::ICmpSLT:
		return X86Cond::L;
	case mir::MIROpcode::ICmpSLE:
		return X86Cond::LE;
	case mir::MIROpcode::ICmpSGT:
		return X86Cond::G;
	case mir::MIROpcode::ICmpSGE:
		return X86Cond::GE;
	case mir::MIROpcode::ICmpULT:
		return X86Cond::B;
	case mir::MIROpcode::ICmpULE:
		return X86Cond::BE;
	case mir::MIROpcode::ICmpUGT:
		return X86Cond::A;
	case mir::MIROpcode::ICmpUGE:
		return X86Cond::AE;
	case mir::MIROpcode::FCmpEq:
		return X86Cond::E;
	case mir::MIROpcode::FCmpNe:
		return X86Cond::NE;
	case mir::MIROpcode::FCmpLT:
		return X86Cond::B; // For unordered compare
	case mir::MIROpcode::FCmpLE:
		return X86Cond::BE;
	case mir::MIROpcode::FCmpGT:
		return X86Cond::A;
	case mir::MIROpcode::FCmpGE:
		return X86Cond::AE;
	default:
		throw std::runtime_error("Invalid comparison opcode");
	}
}

void X86CodeGen::genICmp(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValue(inst->operands[0], X86Reg::RAX);
	X86Reg rhs = loadValue(inst->operands[1], X86Reg::RCX);

	if (inst->operands[0]->type->kind == mir::MIRTypeKind::Int32) {
		encoder.cmpRR32(lhs, rhs);
	} else {
		encoder.cmpRR64(lhs, rhs);
	}

	X86Cond cond = getConditionCode(inst->opcode);
	encoder.setcc(cond, X86Reg::AL);
	encoder.movzxRR32_8(X86Reg::RAX, X86Reg::AL);

	storeResult(inst->result, X86Reg::RAX);
}

void X86CodeGen::genFCmp(mir::MIRInstruction *inst) {
	X86Reg lhs = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	X86Reg rhs = loadValueFloat(inst->operands[1], X86Reg::XMM1);

	encoder.ucomisdRR(lhs, rhs);

	X86Cond cond = getConditionCode(inst->opcode);
	encoder.setcc(cond, X86Reg::AL);
	encoder.movzxRR32_8(X86Reg::RAX, X86Reg::AL);

	storeResult(inst->result, X86Reg::RAX);
}

//==============================================================================
// Memory Operations
//==============================================================================

void X86CodeGen::genAlloca(mir::MIRInstruction *inst) {
	// Alloca result is already assigned a stack slot during register allocation
	// Just compute the address
	X86Mem slot = getStackSlot(inst->result);
	encoder.lea64(X86Reg::RAX, slot);
	storeResult(inst->result, X86Reg::RAX);
}

void X86CodeGen::genLoad(mir::MIRInstruction *inst) {
	X86Reg ptr = loadValue(inst->operands[0], X86Reg::RAX);
	X86Mem mem(ptr);

	mir::MIRType *loadType = inst->result->type;
	if (loadType->isFloat()) {
		encoder.movsdRM(X86Reg::XMM0, mem);
		storeResultFloat(inst->result, X86Reg::XMM0);
	} else if (loadType->kind == mir::MIRTypeKind::Int8 ||
			   loadType->kind == mir::MIRTypeKind::Int1) {
		encoder.movzxRM32_8(X86Reg::RAX, mem);
		storeResult(inst->result, X86Reg::RAX);
	} else if (loadType->kind == mir::MIRTypeKind::Int32) {
		encoder.movRM32(X86Reg::RAX, mem);
		storeResult(inst->result, X86Reg::RAX);
	} else {
		encoder.movRM64(X86Reg::RAX, mem);
		storeResult(inst->result, X86Reg::RAX);
	}
}

void X86CodeGen::genStore(mir::MIRInstruction *inst) {
	mir::MIRValue *value = inst->operands[0];
	mir::MIRValue *ptr = inst->operands[1];

	X86Reg ptrReg = loadValue(ptr, X86Reg::RCX);
	X86Mem mem(ptrReg);

	if (value->type->isFloat()) {
		X86Reg valReg = loadValueFloat(value, X86Reg::XMM0);
		encoder.movsdMR(mem, valReg);
	} else if (value->type->kind == mir::MIRTypeKind::Int8 ||
			   value->type->kind == mir::MIRTypeKind::Int1) {
		X86Reg valReg = loadValue(value, X86Reg::RAX);
		encoder.movMR8(mem, valReg);
	} else if (value->type->kind == mir::MIRTypeKind::Int32) {
		X86Reg valReg = loadValue(value, X86Reg::RAX);
		encoder.movMR32(mem, valReg);
	} else {
		X86Reg valReg = loadValue(value, X86Reg::RAX);
		encoder.movMR64(mem, valReg);
	}
}

void X86CodeGen::genGEP(mir::MIRInstruction *inst) {
	// Load base pointer
	X86Reg base = loadValue(inst->operands[0], X86Reg::RAX);

	// For now, simple GEP: base + index * element_size
	if (inst->operands.size() >= 2) {
		mir::MIRValue *index = inst->operands[1];

		if (index->kind == mir::MIRValueKind::ConstantInt) {
			int32_t offset = static_cast<int32_t>(index->intValue * 8); // Assume 8-byte elements
			encoder.lea64(X86Reg::RAX, X86Mem(base, offset));
		} else {
			X86Reg idxReg = loadValue(index, X86Reg::RCX);
			// lea rax, [base + index*8]
			encoder.lea64(X86Reg::RAX, X86Mem(base, idxReg, 8, 0));
		}
	}

	storeResult(inst->result, X86Reg::RAX);
}

//==============================================================================
// Conversions
//==============================================================================

void X86CodeGen::genTrunc(mir::MIRInstruction *inst) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);
	// Truncation is automatic on x86 - just use lower bits
	// May need to mask for smaller types
	if (inst->result->type->kind == mir::MIRTypeKind::Int8 ||
		inst->result->type->kind == mir::MIRTypeKind::Int1) {
		encoder.andRI32(val, 0xFF);
	} else if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		// 32-bit operations automatically zero upper 32 bits
	}
	storeResult(inst->result, val);
}

void X86CodeGen::genZExt(mir::MIRInstruction *inst) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);
	// Zero extension
	if (inst->operands[0]->type->kind == mir::MIRTypeKind::Int8 ||
		inst->operands[0]->type->kind == mir::MIRTypeKind::Int1) {
		encoder.movzxRR32_8(val, val);
	} else if (inst->operands[0]->type->kind == mir::MIRTypeKind::Int32) {
		// 32-bit move automatically zero-extends to 64-bit
		encoder.movRR32(val, val);
	}
	storeResult(inst->result, val);
}

void X86CodeGen::genSExt(mir::MIRInstruction *inst) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);
	// Sign extension
	if (inst->operands[0]->type->kind == mir::MIRTypeKind::Int8 ||
		inst->operands[0]->type->kind == mir::MIRTypeKind::Int1) {
		encoder.movsxRR32_8(val, val);
	} else if (inst->operands[0]->type->kind == mir::MIRTypeKind::Int32) {
		encoder.movsxRR64_32(val, val);
	}
	storeResult(inst->result, val);
}

void X86CodeGen::genFPToSI(mir::MIRInstruction *inst) {
	X86Reg val = loadValueFloat(inst->operands[0], X86Reg::XMM0);
	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.cvttsd2siRR32(X86Reg::RAX, val);
	} else {
		encoder.cvttsd2siRR64(X86Reg::RAX, val);
	}
	storeResult(inst->result, X86Reg::RAX);
}

void X86CodeGen::genSIToFP(mir::MIRInstruction *inst) {
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);
	if (inst->operands[0]->type->kind == mir::MIRTypeKind::Int32) {
		encoder.cvtsi2sdRR32(X86Reg::XMM0, val);
	} else {
		encoder.cvtsi2sdRR64(X86Reg::XMM0, val);
	}
	storeResultFloat(inst->result, X86Reg::XMM0);
}

//==============================================================================
// Control Flow
//==============================================================================

void X86CodeGen::genBr(mir::MIRInstruction *inst) {
	mir::MIRBasicBlock *target = inst->operands[0]->blockRef;

	auto it = blockOffsets.find(target);
	if (it != blockOffsets.end()) {
		// Backward branch - we know the offset
		int32_t offset = X86Encoder::calcRel32(encoder.getOffset() + 5, it->second);
		encoder.jmpRel32(offset);
	} else {
		// Forward branch - need fixup
		encoder.jmpRel32(0); // Placeholder
		pendingBranches.push_back({encoder.getOffset() - 4, target});
	}
}

void X86CodeGen::genCondBr(mir::MIRInstruction *inst) {
	mir::MIRValue *cond = inst->operands[0];
	mir::MIRBasicBlock *trueDest = inst->operands[1]->blockRef;
	mir::MIRBasicBlock *falseDest = inst->operands[2]->blockRef;

	// Test condition
	X86Reg condReg = loadValue(cond, X86Reg::RAX);
	encoder.testRR32(condReg, condReg);

	// Jump to true destination if non-zero
	auto trueIt = blockOffsets.find(trueDest);
	if (trueIt != blockOffsets.end()) {
		int32_t offset = X86Encoder::calcRel32(encoder.getOffset() + 6, trueIt->second);
		encoder.jccRel32(X86Cond::NE, offset);
	} else {
		encoder.jccRel32(X86Cond::NE, 0); // Placeholder
		pendingBranches.push_back({encoder.getOffset() - 4, trueDest});
	}

	// Fall through or jump to false destination
	auto falseIt = blockOffsets.find(falseDest);
	if (falseIt != blockOffsets.end()) {
		int32_t offset = X86Encoder::calcRel32(encoder.getOffset() + 5, falseIt->second);
		encoder.jmpRel32(offset);
	} else {
		encoder.jmpRel32(0); // Placeholder
		pendingBranches.push_back({encoder.getOffset() - 4, falseDest});
	}
}

void X86CodeGen::genRet(mir::MIRInstruction *inst) {
	mir::MIRValue *retVal = inst->operands[0];

	if (retVal->type->isFloat()) {
		loadValueFloat(retVal, X86Reg::XMM0);
	} else {
		loadValue(retVal, X86Reg::RAX);
	}

	generateEpilogue();
}

void X86CodeGen::genCall(mir::MIRInstruction *inst) {
	// Set up arguments according to calling convention
	std::vector<X86Reg> argRegs;
	if (callingConv == CallingConv::Win64) {
		argRegs = {X86Reg::RCX, X86Reg::RDX, X86Reg::R8, X86Reg::R9};
	} else {
		argRegs = {X86Reg::RDI, X86Reg::RSI, X86Reg::RDX, X86Reg::RCX, X86Reg::R8, X86Reg::R9};
	}

	// Load arguments into registers
	for (size_t i = 0; i < inst->operands.size() && i < argRegs.size(); ++i) {
		mir::MIRValue *arg = inst->operands[i];
		if (arg->type->isFloat()) {
			loadValueFloat(arg, static_cast<X86Reg>(static_cast<int>(X86Reg::XMM0) + i));
		} else {
			loadValue(arg, argRegs[i]);
		}
	}

	// TODO: Handle stack arguments for more than 4/6 args

	// Call the function
	encoder.callRel32(0); // Placeholder
	relocations.push_back({Relocation::Type::FunctionCall,
						   encoder.getOffset() - 4,
						   inst->calleeName});

	// Store result
	if (inst->result) {
		if (inst->result->type->isFloat()) {
			storeResultFloat(inst->result, X86Reg::XMM0);
		} else {
			storeResult(inst->result, X86Reg::RAX);
		}
	}
}

//==============================================================================
// SSA Operations
//==============================================================================

void X86CodeGen::genPhi(mir::MIRInstruction *inst) {
	// Phi nodes are handled during register allocation and
	// translated to copy instructions at predecessor block ends
	// For now, this is a no-op as we assume proper SSA destruction
}

void X86CodeGen::genCopy(mir::MIRInstruction *inst) {
	mir::MIRValue *src = inst->operands[0];

	if (src->type->isFloat()) {
		X86Reg srcReg = loadValueFloat(src, X86Reg::XMM0);
		storeResultFloat(inst->result, srcReg);
	} else {
		X86Reg srcReg = loadValue(src, X86Reg::RAX);
		storeResult(inst->result, srcReg);
	}
}

//==============================================================================
// Fixup Handling
//==============================================================================

void X86CodeGen::resolveBranchFixups() {
	for (auto &[offset, target] : pendingBranches) {
		auto it = blockOffsets.find(target);
		if (it != blockOffsets.end()) {
			int32_t rel = X86Encoder::calcRel32(offset + 4, it->second);
			encoder.patchRel32(offset, rel);
		}
	}
}

void X86CodeGen::addRelocation(Relocation::Type type, size_t offset, const std::string &symbol) {
	relocations.push_back({type, offset, symbol});
}

//==============================================================================
// Data Section Helpers
//==============================================================================

size_t X86CodeGen::emitConstantFloat(double value) {
	size_t offset = dataSection.size();
	uint64_t bits;
	std::memcpy(&bits, &value, sizeof(double));
	for (int i = 0; i < 8; ++i) {
		dataSection.push_back((bits >> (i * 8)) & 0xFF);
	}
	return offset;
}

size_t X86CodeGen::emitConstantInt64(int64_t value) {
	size_t offset = dataSection.size();
	for (int i = 0; i < 8; ++i) {
		dataSection.push_back((value >> (i * 8)) & 0xFF);
	}
	return offset;
}

size_t X86CodeGen::emitStringConstant(const std::string &str) {
	size_t offset = dataSection.size();
	dataSection.insert(dataSection.end(), str.begin(), str.end());
	dataSection.push_back(0); // Null terminator
	// Align to 8 bytes
	while (dataSection.size() % 8 != 0) {
		dataSection.push_back(0);
	}
	return offset;
}

//==============================================================================
// Calling Convention Helpers
//==============================================================================

X86Reg X86CodeGen::getArgReg(unsigned index, bool isFloat) {
	if (callingConv == CallingConv::Win64) {
		if (isFloat) {
			X86Reg floatRegs[] = {X86Reg::XMM0, X86Reg::XMM1, X86Reg::XMM2, X86Reg::XMM3};
			return index < 4 ? floatRegs[index] : X86Reg::None;
		} else {
			X86Reg intRegs[] = {X86Reg::RCX, X86Reg::RDX, X86Reg::R8, X86Reg::R9};
			return index < 4 ? intRegs[index] : X86Reg::None;
		}
	} else {
		if (isFloat) {
			if (index < 8) {
				return static_cast<X86Reg>(static_cast<int>(X86Reg::XMM0) + index);
			}
			return X86Reg::None;
		} else {
			X86Reg intRegs[] = {X86Reg::RDI, X86Reg::RSI, X86Reg::RDX,
								X86Reg::RCX, X86Reg::R8, X86Reg::R9};
			return index < 6 ? intRegs[index] : X86Reg::None;
		}
	}
}

X86Reg X86CodeGen::getReturnReg(bool isFloat) {
	return isFloat ? X86Reg::XMM0 : X86Reg::RAX;
}

std::vector<X86Reg> X86CodeGen::getCallerSavedRegs() {
	if (callingConv == CallingConv::Win64) {
		return {X86Reg::RAX, X86Reg::RCX, X86Reg::RDX,
				X86Reg::R8, X86Reg::R9, X86Reg::R10, X86Reg::R11};
	} else {
		return {X86Reg::RAX, X86Reg::RCX, X86Reg::RDX, X86Reg::RSI, X86Reg::RDI,
				X86Reg::R8, X86Reg::R9, X86Reg::R10, X86Reg::R11};
	}
}

std::vector<X86Reg> X86CodeGen::getCalleeSavedRegs() {
	if (callingConv == CallingConv::Win64) {
		return {X86Reg::RBX, X86Reg::RSI, X86Reg::RDI,
				X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
	} else {
		return {X86Reg::RBX, X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
	}
}

unsigned X86CodeGen::getShadowSpaceSize() {
	return callingConv == CallingConv::Win64 ? 32 : 0;
}

} // namespace backend
