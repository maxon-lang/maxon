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

	// Save callee-saved registers BEFORE allocating the stack frame
	// This way, RSP-relative offsets in the function body are consistent
	for (X86Reg reg : regAlloc.usedCalleeSaved) {
		encoder.pushR(reg);
	}

	// Allocate stack space (after callee-saved pushes)
	if (regAlloc.frameSize > 0) {
		encoder.subRI64(X86Reg::RSP, regAlloc.frameSize);
	}

	// Save hidden return pointer if this function returns a large struct
	// Windows x64 ABI: The hidden pointer arrives in RCX
	if (regAlloc.hasHiddenRetPtr) {
		encoder.movMR64(X86Mem(X86Reg::RBP, regAlloc.hiddenRetPtrOffset), X86Reg::RCX);
	}

	// Save shifted parameters to stack (only for functions with hidden return pointer)
	// These arrive in RDX, R8, R9 which are volatile and may be clobbered
	for (const auto &save : regAlloc.shiftedParamSaves) {
		encoder.movMR64(X86Mem(X86Reg::RBP, save.second), save.first);
	}

	// Copy integer parameters from volatile RCX/RDX/R8/R9 to non-volatile GPRs
	// Must be done early before any calls can clobber the volatile regs
	for (const auto &save : regAlloc.intParamSaves) {
		encoder.movRR64(save.second, save.first);
	}

	// Copy float parameters from volatile XMM0-3 to non-volatile XMM6-15
	// Must be done early before any calls can clobber XMM0-3
	for (const auto &save : regAlloc.floatParamSaves) {
		encoder.movsdRR(save.second, save.first);
	}

	// Windows x64: Allocate shadow space if needed
	if (callingConv == CallingConv::Win64) {
		// Shadow space is handled in frameSize calculation
	}
}

void X86CodeGen::generateEpilogue() {
	// Callee-saved registers were pushed after setting RBP
	// So they're at [rbp - 8], [rbp - 16], etc.
	// We need to position RSP to pop them in reverse order
	size_t numCalleeSaved = regAlloc.usedCalleeSaved.size();
	if (numCalleeSaved > 0) {
		// Position RSP to point to the last pushed callee-saved register
		// lea rsp, [rbp - numCalleeSaved * 8]
		encoder.lea64(X86Reg::RSP, X86Mem(X86Reg::RBP, -static_cast<int32_t>(numCalleeSaved * 8)));
	} else {
		// No callee-saved registers, just restore RSP from RBP
		encoder.movRR64(X86Reg::RSP, X86Reg::RBP);
	}

	// Restore callee-saved registers (reverse order of push)
	for (auto it = regAlloc.usedCalleeSaved.rbegin();
		 it != regAlloc.usedCalleeSaved.rend(); ++it) {
		encoder.popR(*it);
	}

	// Now RSP points to saved RBP
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
	case mir::MIROpcode::Bitcast:
		genBitcast(inst);
		break;
	case mir::MIROpcode::PtrToInt:
	case mir::MIROpcode::IntToPtr:
		// These are no-ops on x86-64 since pointers and i64 are the same size
		genCopyConversion(inst);
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

// Helper to create unique keys for register allocation maps
// Parameters and VirtualRegs can have the same regId, so we encode the kind
static uint64_t makeAllocKey(mir::MIRValue *value) {
	// Use high bit to distinguish parameters from virtual regs
	uint64_t kindBit = (value->kind == mir::MIRValueKind::Parameter) ? (1ULL << 32) : 0;
	return kindBit | value->regId;
}

// Helper to check if a register is callee-saved for the given calling convention
static bool isCalleeSaved(X86Reg reg, CallingConv conv) {
	if (conv == CallingConv::Win64) {
		// Windows x64: RBX, RSI, RDI, R12-R15 are callee-saved
		switch (reg) {
		case X86Reg::RBX:
		case X86Reg::RSI:
		case X86Reg::RDI:
		case X86Reg::R12:
		case X86Reg::R13:
		case X86Reg::R14:
		case X86Reg::R15:
			return true;
		default:
			return false;
		}
	} else {
		// System V: RBX, R12-R15 are callee-saved
		switch (reg) {
		case X86Reg::RBX:
		case X86Reg::R12:
		case X86Reg::R13:
		case X86Reg::R14:
		case X86Reg::R15:
			return true;
		default:
			return false;
		}
	}
}

void X86CodeGen::allocateRegisters(mir::MIRFunction *func) {
	regAlloc = RegAllocInfo();

	// Check if this function returns a large struct (> 8 bytes)
	// Windows x64 ABI: Large structs are returned via hidden pointer in RCX
	// This shifts all other parameters right by one register
	bool hasLargeStructReturn = func->returnType->isStruct() &&
								func->returnType->sizeInBytes > 8;
	regAlloc.hasHiddenRetPtr = hasLargeStructReturn;

	// Available general-purpose registers (excluding RSP, RBP)
	std::vector<X86Reg> availableRegs;
	std::vector<X86Reg> availableFloatRegs;
	std::vector<X86Reg> paramRegs; // Registers used for passing parameters (ABI)
	if (callingConv == CallingConv::Win64) {
		// Windows x64 ABI: first 4 integer params in RCX, RDX, R8, R9
		paramRegs = {X86Reg::RCX, X86Reg::RDX, X86Reg::R8, X86Reg::R9};
		// Windows: RAX, RCX, RDX, R8-R11 are caller-saved (volatile)
		// RBX, RSI, RDI, R12-R15 are callee-saved
		// Prioritize callee-saved registers to avoid issues with calls.
		// RAX is kept for return values, R10/R11 for scratch/temps.
		availableRegs = {X86Reg::RBX, X86Reg::RSI, X86Reg::RDI,
						 X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15,
						 X86Reg::RAX, X86Reg::R10, X86Reg::R11};
		// XMM registers for float values (XMM6-XMM15 are non-volatile on Windows)
		availableFloatRegs = {X86Reg::XMM6, X86Reg::XMM7, X86Reg::XMM8, X86Reg::XMM9,
							  X86Reg::XMM10, X86Reg::XMM11, X86Reg::XMM12, X86Reg::XMM13,
							  X86Reg::XMM14, X86Reg::XMM15};
	} else {
		// System V: first 6 integer params in RDI, RSI, RDX, RCX, R8, R9
		paramRegs = {X86Reg::RDI, X86Reg::RSI, X86Reg::RDX, X86Reg::RCX, X86Reg::R8, X86Reg::R9};
		// RAX, RCX, RDX, RSI, RDI, R8-R11 are caller-saved
		// RBX, R12-R15 are callee-saved
		// Prioritize callee-saved registers to avoid issues with calls.
		availableRegs = {X86Reg::RBX, X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15,
						 X86Reg::RAX, X86Reg::R10, X86Reg::R11};
		// XMM registers for float values (all volatile on System V)
		availableFloatRegs = {X86Reg::XMM8, X86Reg::XMM9, X86Reg::XMM10, X86Reg::XMM11,
							  X86Reg::XMM12, X86Reg::XMM13, X86Reg::XMM14, X86Reg::XMM15};
	}

	// Simple allocation: assign registers to virtual registers in order
	// This is a placeholder - real implementation would use linear scan
	size_t regIdx = 0;
	size_t floatRegIdx = 0;
	int32_t stackOffset = 0; // Will be decremented before each allocation

	// Reserve stack slot for hidden return pointer if needed
	// This must be done before allocating other stack slots
	if (hasLargeStructReturn) {
		stackOffset -= 8; // 8 bytes for the pointer
		regAlloc.hiddenRetPtrOffset = stackOffset;
	}

	// Allocate parameters to their ABI registers (where they arrive from caller)
	// If we have a hidden return pointer, parameters are shifted right by 1
	// (hidden ptr in RCX, first real param in RDX, etc.)
	// For functions with hidden return pointer, shifted parameters must be saved
	// to stack because RDX, R8, R9 are volatile and may be clobbered.
	//
	// Windows x64 ABI: Parameters are passed based on position, not type:
	// - Position 0: RCX or XMM0 depending on type
	// - Position 1: RDX or XMM1 depending on type
	// - Position 2: R8 or XMM2 depending on type
	// - Position 3: R9 or XMM3 depending on type
	// - Position 4+: on stack
	//
	// Float parameters arrive in volatile XMM0-3 and must be copied to non-volatile
	// registers (XMM6-15) in the prologue to survive across function calls.
	// Integer parameters arrive in volatile RCX/RDX/R8/R9 and must be copied to
	// non-volatile registers (RBX, RSI, RDI, R12-R15) in the prologue.
	std::vector<X86Reg> floatParamRegs = {X86Reg::XMM0, X86Reg::XMM1, X86Reg::XMM2, X86Reg::XMM3};

	for (size_t i = 0; i < func->parameters.size(); ++i) {
		auto *param = func->parameters[i];
		uint64_t key = makeAllocKey(param);
		// Shift parameter index if hidden return pointer takes RCX
		size_t regIndex = hasLargeStructReturn ? i + 1 : i;
		if (regIndex < paramRegs.size()) {
			if (hasLargeStructReturn) {
				// Shifted params must be saved to stack (volatile regs may be clobbered)
				stackOffset -= 8;
				regAlloc.stackSlots[key] = stackOffset;
				regAlloc.shiftedParamSaves.push_back({paramRegs[regIndex], stackOffset});
			} else {
				// Normal case: parameter arrives in volatile register
				// Copy to non-volatile register to survive across function calls
				if (param->type->isFloat()) {
					// Float param arrives in volatile XMM0-3, allocate a non-volatile XMM
					if (floatRegIdx < availableFloatRegs.size()) {
						X86Reg destReg = availableFloatRegs[floatRegIdx++];
						regAlloc.regMap[key] = destReg;
						regAlloc.floatParamSaves.push_back({floatParamRegs[regIndex], destReg});
						// Track callee-saved register usage (XMM6-15 on Windows)
						if (callingConv == CallingConv::Win64 && isCalleeSaved(destReg, callingConv)) {
							if (std::find(regAlloc.usedCalleeSaved.begin(),
										  regAlloc.usedCalleeSaved.end(),
										  destReg) == regAlloc.usedCalleeSaved.end()) {
								regAlloc.usedCalleeSaved.push_back(destReg);
							}
						}
					} else {
						// No more XMM registers available, spill to stack
						stackOffset -= 8;
						regAlloc.stackSlots[key] = stackOffset;
						// TODO: Add support for saving float params to stack
					}
				} else {
					// Integer param arrives in volatile RCX/RDX/R8/R9
					// Allocate a non-volatile GPR to hold it
					if (regIdx < availableRegs.size()) {
						X86Reg destReg = availableRegs[regIdx++];
						regAlloc.regMap[key] = destReg;
						regAlloc.intParamSaves.push_back({paramRegs[regIndex], destReg});
						// Track callee-saved register usage
						if (isCalleeSaved(destReg, callingConv)) {
							if (std::find(regAlloc.usedCalleeSaved.begin(),
										  regAlloc.usedCalleeSaved.end(),
										  destReg) == regAlloc.usedCalleeSaved.end()) {
								regAlloc.usedCalleeSaved.push_back(destReg);
							}
						}
					} else {
						// No more GPRs available, spill to stack
						stackOffset -= 8;
						regAlloc.stackSlots[key] = stackOffset;
					}
				}
			}
		} else {
			// Parameter is passed on the stack by caller
			// Stack layout after callee prologue (push rbp; mov rsp, rbp):
			//   RBP+0:  saved RBP
			//   RBP+8:  return address
			//   RBP+16: shadow space (32 bytes on Win64, 0 on SysV)
			//   RBP+48: first stack argument (on Win64)
			// Formula: 16 (saved RBP + ret addr) + shadowSpace + (stackArgIndex * 8)
			int32_t shadowSpace = (callingConv == CallingConv::Win64) ? 32 : 0;
			int32_t stackArgOffset = 16 + shadowSpace +
									 static_cast<int32_t>((regIndex - paramRegs.size()) * 8);
			regAlloc.stackSlots[key] = stackArgOffset;
		}
	}

	// First pass: allocate stack slots for alloca instructions
	// Alloca results MUST be on stack since they represent addresses of stack memory
	for (auto &block : func->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == mir::MIROpcode::Alloca && inst->result) {
				uint32_t regId = inst->result->regId;
				// Use the allocated type's size if available, otherwise default to 8 bytes
				int allocSize = 8;
				if (inst->allocatedType) {
					allocSize = static_cast<int>(inst->allocatedType->sizeInBytes);
					if (allocSize == 0)
						allocSize = 8; // Fallback
					// Align to 8 bytes
					allocSize = (allocSize + 7) & ~7;
				}
				// Decrement stackOffset FIRST, then store
				// This ensures the alloca base is at the LOWEST address of its region
				// and fields at positive offsets won't overlap with previous allocas
				stackOffset -= allocSize;
				// Allocas are always VirtualReg, use key with kind=0
				uint64_t key = static_cast<uint64_t>(regId); // VirtualReg has kind bit = 0
				regAlloc.stackSlots[key] = stackOffset;
				regAlloc.allocaRegs.insert(regId); // Track this as an alloca
			}
		}
	}

	// Second pass: allocate registers/stack for other virtual registers
	for (auto &block : func->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->result && inst->opcode != mir::MIROpcode::Alloca) {
				uint64_t key = makeAllocKey(inst->result);
				if (regAlloc.regMap.find(key) == regAlloc.regMap.end() &&
					regAlloc.stackSlots.find(key) == regAlloc.stackSlots.end()) {

					// Force large structs to stack (for Windows x64 ABI compatibility)
					bool forceStack = inst->result->type->isStruct() &&
									  inst->result->type->sizeInBytes > 8;

					// Check if this is a float type
					bool isFloat = inst->result->type->isFloat();

					// Try to allocate a register
					if (!forceStack) {
						if (isFloat && floatRegIdx < availableFloatRegs.size()) {
							// Allocate XMM register for float types
							X86Reg allocatedReg = availableFloatRegs[floatRegIdx++];
							regAlloc.regMap[key] = allocatedReg;
							// XMM6-XMM15 are callee-saved on Windows
							if (callingConv == CallingConv::Win64 && isCalleeSaved(allocatedReg, callingConv)) {
								if (std::find(regAlloc.usedCalleeSaved.begin(),
											  regAlloc.usedCalleeSaved.end(),
											  allocatedReg) == regAlloc.usedCalleeSaved.end()) {
									regAlloc.usedCalleeSaved.push_back(allocatedReg);
								}
							}
						} else if (!isFloat && regIdx < availableRegs.size()) {
							// Allocate GPR for integer types
							X86Reg allocatedReg = availableRegs[regIdx++];
							regAlloc.regMap[key] = allocatedReg;
							// Track callee-saved register usage
							if (isCalleeSaved(allocatedReg, callingConv)) {
								if (std::find(regAlloc.usedCalleeSaved.begin(),
											  regAlloc.usedCalleeSaved.end(),
											  allocatedReg) == regAlloc.usedCalleeSaved.end()) {
									regAlloc.usedCalleeSaved.push_back(allocatedReg);
								}
							}
						} else {
							// Spill to stack - decrement first, then store
							int allocSize = 8;
							stackOffset -= allocSize;
							regAlloc.stackSlots[key] = stackOffset;
						}
					} else {
						// Large struct - spill to stack
						int allocSize = static_cast<int>(inst->result->type->sizeInBytes);
						allocSize = (allocSize + 7) & ~7;
						stackOffset -= allocSize;
						regAlloc.stackSlots[key] = stackOffset;
					}
				}
			}
		}
	}

	// Third pass: Calculate maximum outgoing stack arguments for any call
	// This determines how much extra space we need beyond shadow space
	size_t maxOutgoingStackArgs = 0;
	size_t numRegisterArgs = (callingConv == CallingConv::Win64) ? 4 : 6;
	for (auto &block : func->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == mir::MIROpcode::Call) {
				size_t numArgs = inst->operands.size();
				// Account for hidden return pointer if callee returns large struct
				if (inst->result && inst->result->type->isStruct() &&
					inst->result->type->sizeInBytes > 8) {
					numArgs++; // Hidden return pointer takes one register slot
				}
				if (numArgs > numRegisterArgs) {
					size_t stackArgs = numArgs - numRegisterArgs;
					if (stackArgs > maxOutgoingStackArgs) {
						maxOutgoingStackArgs = stackArgs;
					}
				}
			}
		}
	}
	regAlloc.outgoingStackArgsSize = static_cast<uint32_t>(maxOutgoingStackArgs * 8);

	// Calculate frame size considering callee-saved register pushes for alignment
	// After push rbp: RSP % 16 == 0
	// After N callee-saved pushes: RSP % 16 == (N % 2) * 8
	// We need the final RSP (after sub) to be 16-aligned for calls
	size_t numCalleeSaved = regAlloc.usedCalleeSaved.size();
	uint32_t baseFrameSize = static_cast<uint32_t>(-stackOffset);

	// Add shadow space for Windows first
	if (callingConv == CallingConv::Win64) {
		baseFrameSize += 32; // 4 * 8 bytes shadow space
	}

	// Add space for outgoing stack arguments (beyond register args)
	baseFrameSize += regAlloc.outgoingStackArgsSize;

	// Now align considering the callee-saved pushes
	// After push rbp + N callee-saved pushes, RSP is offset by (N % 2) * 8 from 16-alignment
	// If N is odd, we need frameSize to be 8 mod 16 to restore 16-alignment
	// If N is even, we need frameSize to be 0 mod 16
	if (numCalleeSaved % 2 == 1) {
		// Odd number of callee-saved pushes, need frameSize % 16 == 8
		if (baseFrameSize % 16 == 0) {
			baseFrameSize += 8;
		} else if (baseFrameSize % 16 != 8) {
			baseFrameSize += (24 - (baseFrameSize % 16)) % 16;
		}
	} else {
		// Even number of callee-saved pushes, need frameSize % 16 == 0
		if (baseFrameSize % 16 != 0) {
			baseFrameSize += 16 - (baseFrameSize % 16);
		}
	}

	regAlloc.frameSize = baseFrameSize;
}

X86Reg X86CodeGen::getAllocatedReg(mir::MIRValue *value) {
	if (value->kind == mir::MIRValueKind::VirtualReg ||
		value->kind == mir::MIRValueKind::Parameter) {
		uint64_t key = makeAllocKey(value);
		auto it = regAlloc.regMap.find(key);
		if (it != regAlloc.regMap.end()) {
			return it->second;
		}
	}
	return X86Reg::None;
}

X86Mem X86CodeGen::getStackSlot(mir::MIRValue *value) {
	uint64_t key = makeAllocKey(value);
	auto it = regAlloc.stackSlots.find(key);
	if (it != regAlloc.stackSlots.end()) {
		return X86Mem(X86Reg::RBP, it->second);
	}
	throw std::runtime_error("Value not found in stack slots: regId=" + std::to_string(value->regId) +
							 ", kind=" + std::to_string(static_cast<int>(value->kind)));
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
	case mir::MIRValueKind::Parameter: {
		// Parameters are never allocas - they're passed in registers or on stack
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
	case mir::MIRValueKind::VirtualReg: {
		// Check if this is an alloca result - if so, compute address via lea
		if (regAlloc.allocaRegs.count(value->regId)) {
			X86Mem slot = getStackSlot(value);
			encoder.lea64(hint, slot);
			return hint;
		}

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

X86Reg X86CodeGen::getValueLocation(mir::MIRValue *value) {
	// Returns the physical register where a value is currently located,
	// or X86Reg::None if it's not in a register (e.g., constant or on stack)
	if (value->kind == mir::MIRValueKind::ConstantInt ||
		value->kind == mir::MIRValueKind::ConstantFloat ||
		value->kind == mir::MIRValueKind::ConstantNull ||
		value->kind == mir::MIRValueKind::Global) {
		return X86Reg::None; // Not in a register
	}
	return getAllocatedReg(value);
}

std::pair<X86Reg, X86Reg> X86CodeGen::loadBinaryOperands(
	mir::MIRValue *lhs, mir::MIRValue *rhs, X86Reg lhsHint, X86Reg rhsHint) {
	// Check if loading lhs into lhsHint would clobber rhs
	X86Reg rhsLocation = getValueLocation(rhs);

	if (rhsLocation == lhsHint) {
		// Loading lhs into lhsHint would clobber rhs, so load rhs first
		X86Reg rhsReg = loadValue(rhs, rhsHint);
		X86Reg lhsReg = loadValue(lhs, lhsHint);
		return {lhsReg, rhsReg};
	}

	// Safe to load in normal order
	X86Reg lhsReg = loadValue(lhs, lhsHint);
	X86Reg rhsReg = loadValue(rhs, rhsHint);
	return {lhsReg, rhsReg};
}

std::pair<X86Reg, X86Reg> X86CodeGen::loadBinaryOperandsFloat(
	mir::MIRValue *lhs, mir::MIRValue *rhs, X86Reg lhsHint, X86Reg rhsHint) {
	// Check if loading lhs into lhsHint would clobber rhs
	X86Reg rhsLocation = getValueLocation(rhs);

	if (rhsLocation == lhsHint) {
		// Loading lhs into lhsHint would clobber rhs, so load rhs first
		X86Reg rhsReg = loadValueFloat(rhs, rhsHint);
		X86Reg lhsReg = loadValueFloat(lhs, lhsHint);
		return {lhsReg, rhsReg};
	}

	// Safe to load in normal order
	X86Reg lhsReg = loadValueFloat(lhs, lhsHint);
	X86Reg rhsReg = loadValueFloat(rhs, rhsHint);
	return {lhsReg, rhsReg};
}

//==============================================================================
// Integer Arithmetic
//==============================================================================

void X86CodeGen::genAdd(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.addRR32(lhs, rhs);
	} else {
		encoder.addRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genSub(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.subRR32(lhs, rhs);
	} else {
		encoder.subRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genMul(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

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
	auto [dividend, divisor] = loadBinaryOperands(inst->operands[0], inst->operands[1],
												  X86Reg::RAX, X86Reg::RCX);

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
	auto [dividend, divisor] = loadBinaryOperands(inst->operands[0], inst->operands[1],
												  X86Reg::RAX, X86Reg::RCX);

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
	auto [lhs, rhs] = loadBinaryOperandsFloat(inst->operands[0], inst->operands[1],
											  X86Reg::XMM0, X86Reg::XMM1);
	encoder.addsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFSub(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperandsFloat(inst->operands[0], inst->operands[1],
											  X86Reg::XMM0, X86Reg::XMM1);
	encoder.subsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFMul(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperandsFloat(inst->operands[0], inst->operands[1],
											  X86Reg::XMM0, X86Reg::XMM1);
	encoder.mulsdRR(lhs, rhs);
	storeResultFloat(inst->result, lhs);
}

void X86CodeGen::genFDiv(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperandsFloat(inst->operands[0], inst->operands[1],
											  X86Reg::XMM0, X86Reg::XMM1);
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
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.andRR32(lhs, rhs);
	} else {
		encoder.andRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genOr(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

	if (inst->result->type->kind == mir::MIRTypeKind::Int32) {
		encoder.orRR32(lhs, rhs);
	} else {
		encoder.orRR64(lhs, rhs);
	}

	storeResult(inst->result, lhs);
}

void X86CodeGen::genXor(mir::MIRInstruction *inst) {
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

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
	auto [lhs, rhs] = loadBinaryOperands(inst->operands[0], inst->operands[1],
										 X86Reg::RAX, X86Reg::RCX);

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
	auto [lhs, rhs] = loadBinaryOperandsFloat(inst->operands[0], inst->operands[1],
											  X86Reg::XMM0, X86Reg::XMM1);

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
	// Alloca results are always assigned stack slots during register allocation
	// The stack slot holds the allocated memory
	// NO CODE is generated here - the address is computed lazily when the
	// alloca result is used (via lea in loadValue for allocaRegs)
	(void)inst; // Suppress unused parameter warning
}

void X86CodeGen::genLoad(mir::MIRInstruction *inst) {
	mir::MIRType *loadType = inst->result->type;

	// Handle large struct loads specially
	if (loadType->isStruct() && loadType->sizeInBytes > 8) {
		// For large structs, we can't load into a register
		// Instead, copy from source to result's stack slot
		X86Reg srcPtr = loadValue(inst->operands[0], X86Reg::RSI);

		// Get destination (result's stack slot)
		X86Mem dstSlot = getStackSlot(inst->result);
		encoder.lea64(X86Reg::RDI, dstSlot);

		// Copy the struct
		uint64_t structSize = loadType->sizeInBytes;
		uint64_t offset = 0;
		for (; offset + 8 <= structSize; offset += 8) {
			encoder.movRM64(X86Reg::RAX, X86Mem(srcPtr, static_cast<int32_t>(offset)));
			encoder.movMR64(X86Mem(X86Reg::RDI, static_cast<int32_t>(offset)), X86Reg::RAX);
		}
		if (offset + 4 <= structSize) {
			encoder.movRM32(X86Reg::EAX, X86Mem(srcPtr, static_cast<int32_t>(offset)));
			encoder.movMR32(X86Mem(X86Reg::RDI, static_cast<int32_t>(offset)), X86Reg::EAX);
			offset += 4;
		}
		// Result is already in its stack slot, nothing more to do
		return;
	}

	// Check if result has an allocated register, and load directly into it
	X86Reg targetReg = getAllocatedReg(inst->result);
	if (targetReg == X86Reg::None) {
		targetReg = X86Reg::RAX; // Use RAX as temporary if no register allocated
	}

	// Use a different register for the pointer to avoid clobbering the target
	X86Reg ptrReg = (targetReg == X86Reg::RCX) ? X86Reg::RDX : X86Reg::RCX;
	X86Reg ptr = loadValue(inst->operands[0], ptrReg);
	X86Mem mem(ptr);

	if (loadType->isFloat()) {
		encoder.movsdRM(X86Reg::XMM0, mem);
		storeResultFloat(inst->result, X86Reg::XMM0);
	} else if (loadType->kind == mir::MIRTypeKind::Int8 ||
			   loadType->kind == mir::MIRTypeKind::Int1) {
		encoder.movzxRM32_8(targetReg, mem);
		storeResult(inst->result, targetReg);
	} else if (loadType->kind == mir::MIRTypeKind::Int32) {
		encoder.movRM32(targetReg, mem);
		storeResult(inst->result, targetReg);
	} else {
		encoder.movRM64(targetReg, mem);
		storeResult(inst->result, targetReg);
	}
}

void X86CodeGen::genStore(mir::MIRInstruction *inst) {
	mir::MIRValue *value = inst->operands[0];
	mir::MIRValue *ptr = inst->operands[1];

	if (value->type->isFloat()) {
		X86Reg valReg = loadValueFloat(value, X86Reg::XMM0);
		X86Reg ptrReg = loadValue(ptr, X86Reg::RCX);
		X86Mem mem(ptrReg);
		encoder.movsdMR(mem, valReg);
	} else if (value->type->kind == mir::MIRTypeKind::Struct) {
		// Check if this is a large struct from a call (Windows x64 ABI hidden pointer case)
		// AND the destination is the same as the call result's stack slot
		bool canSkipCopy = false;
		if (value->type->sizeInBytes > 8 &&
			value->kind == mir::MIRValueKind::VirtualReg &&
			value->definingInst &&
			value->definingInst->opcode == mir::MIROpcode::Call) {

			// Large struct from call - check if source and dest are the same stack slot
			// Get source (call result) and dest (store ptr) stack offsets
			uint64_t srcKey = makeAllocKey(value);
			auto srcIt = regAlloc.stackSlots.find(srcKey);

			// For the destination, if it's an alloca result, get its stack slot
			if (ptr->kind == mir::MIRValueKind::VirtualReg &&
				regAlloc.allocaRegs.count(ptr->regId)) {
				uint64_t dstKey = static_cast<uint64_t>(ptr->regId);
				auto dstIt = regAlloc.stackSlots.find(dstKey);

				if (srcIt != regAlloc.stackSlots.end() &&
					dstIt != regAlloc.stackSlots.end() &&
					srcIt->second == dstIt->second) {
					// Source and dest are the same stack slot - skip copy
					canSkipCopy = true;
				}
			}
		}

		if (canSkipCopy) {
			// Struct is already at the destination via hidden pointer mechanism
			return;
		}

		// Struct store - need to copy all bytes
		// For large structs stored directly on the stack (e.g., from call results),
		// we need the ADDRESS of the stack slot, not the value at the stack slot.
		// For struct pointers (from GEP or alloca), loadValue gives us the address.
		X86Reg srcReg;
		bool isLargeStructOnStack = value->type->sizeInBytes > 8 &&
									value->kind == mir::MIRValueKind::VirtualReg &&
									!regAlloc.allocaRegs.count(value->regId) &&
									getAllocatedReg(value) == X86Reg::None;

		if (isLargeStructOnStack) {
			// Large struct is stored directly on stack - use lea to get address
			X86Mem slot = getStackSlot(value);
			encoder.lea64(X86Reg::RAX, slot);
			srcReg = X86Reg::RAX;
		} else {
			// Struct pointer or small struct in register - loadValue gives address/value
			srcReg = loadValue(value, X86Reg::RAX);
		}

		X86Reg dstReg = loadValue(ptr, X86Reg::RCX); // Dest ptr

		size_t structSize = value->type->sizeInBytes;
		size_t offset = 0;

		// Copy 8 bytes at a time
		while (offset + 8 <= structSize) {
			encoder.movRM64(X86Reg::R10, X86Mem(srcReg, static_cast<int32_t>(offset)));
			encoder.movMR64(X86Mem(dstReg, static_cast<int32_t>(offset)), X86Reg::R10);
			offset += 8;
		}
		// Copy remaining 4 bytes if any
		if (offset + 4 <= structSize) {
			encoder.movRM32(X86Reg::R10, X86Mem(srcReg, static_cast<int32_t>(offset)));
			encoder.movMR32(X86Mem(dstReg, static_cast<int32_t>(offset)), X86Reg::R10);
			offset += 4;
		}
		// Copy remaining bytes one at a time
		while (offset < structSize) {
			encoder.movRM8(X86Reg::R10, X86Mem(srcReg, static_cast<int32_t>(offset)));
			encoder.movMR8(X86Mem(dstReg, static_cast<int32_t>(offset)), X86Reg::R10);
			offset += 1;
		}
	} else if (value->type->kind == mir::MIRTypeKind::Int8 ||
			   value->type->kind == mir::MIRTypeKind::Int1) {
		// Use R10/R11 to avoid conflicts with parameter registers (RCX/RDX/R8/R9)
		// Check if ptr is in R10 - if so, load it first to avoid clobbering
		auto [valReg, ptrReg] = loadBinaryOperands(value, ptr, X86Reg::R10, X86Reg::R11);
		X86Mem mem(ptrReg);
		encoder.movMR8(mem, valReg);
	} else if (value->type->kind == mir::MIRTypeKind::Int32) {
		// Use R10/R11 to avoid conflicts with parameter registers (RCX/RDX/R8/R9)
		auto [valReg, ptrReg] = loadBinaryOperands(value, ptr, X86Reg::R10, X86Reg::R11);
		X86Mem mem(ptrReg);
		encoder.movMR32(mem, valReg);
	} else {
		// Use R10/R11 to avoid conflicts with parameter registers (RCX/RDX/R8/R9)
		auto [valReg, ptrReg] = loadBinaryOperands(value, ptr, X86Reg::R10, X86Reg::R11);
		X86Mem mem(ptrReg);
		encoder.movMR64(mem, valReg);
	}
}

void X86CodeGen::genGEP(mir::MIRInstruction *inst) {
	// Load base pointer
	X86Reg base = loadValue(inst->operands[0], X86Reg::RAX);

	// GEP format: base, index0, [index1, ...]
	// - For arrays: index0 is element index (or 0 if accessing first element)
	// - For structs: index0 is 0, index1 is field index

	if (!inst->elementType) {
		// No element type - just return the base pointer
		storeResult(inst->result, base);
		return;
	}

	int64_t offset = 0;

	if (inst->elementType->kind == mir::MIRTypeKind::Struct && inst->operands.size() >= 3) {
		// Struct field access: getelementptr %Struct, ptr %base, i32 0, i32 <field_idx>
		// The second index is the field index within the struct
		mir::MIRValue *fieldIdx = inst->operands[2];

		if (fieldIdx->kind == mir::MIRValueKind::ConstantInt) {
			// Calculate byte offset to the field
			int fieldIndex = static_cast<int>(fieldIdx->intValue);
			for (int i = 0; i < fieldIndex && i < (int)inst->elementType->fieldTypes.size(); ++i) {
				offset += inst->elementType->fieldTypes[i]->sizeInBytes;
			}
			encoder.lea64(X86Reg::RAX, X86Mem(base, static_cast<int32_t>(offset)));
		} else {
			// Dynamic field index - not commonly used, fall back to 4-byte fields
			X86Reg idxReg = loadValue(fieldIdx, X86Reg::RCX);
			encoder.lea64(X86Reg::RAX, X86Mem(base, idxReg, 4, 0));
		}
	} else {
		// Array element access or simple pointer arithmetic
		int elementSize = 8; // Default to 8 bytes (pointer size)
		switch (inst->elementType->kind) {
		case mir::MIRTypeKind::Int1:
		case mir::MIRTypeKind::Int8:
			elementSize = 1;
			break;
		case mir::MIRTypeKind::Int32:
			elementSize = 4;
			break;
		case mir::MIRTypeKind::Int64:
		case mir::MIRTypeKind::Ptr:
		case mir::MIRTypeKind::Float64:
			elementSize = 8;
			break;
		case mir::MIRTypeKind::Struct:
			elementSize = static_cast<int>(inst->elementType->sizeInBytes);
			break;
		default:
			elementSize = 8;
			break;
		}

		// For array access: base + index * element_size
		// We use the last meaningful index (skip leading 0 index)
		mir::MIRValue *index = nullptr;
		if (inst->operands.size() >= 3) {
			index = inst->operands[2]; // Second index for array element
		} else if (inst->operands.size() >= 2) {
			index = inst->operands[1]; // Single index
		}

		if (index) {
			if (index->kind == mir::MIRValueKind::ConstantInt) {
				int32_t offsetBytes = static_cast<int32_t>(index->intValue * elementSize);
				encoder.lea64(X86Reg::RAX, X86Mem(base, offsetBytes));
			} else {
				X86Reg idxReg = loadValue(index, X86Reg::RCX);
				if (elementSize == 1 || elementSize == 2 || elementSize == 4 || elementSize == 8) {
					encoder.lea64(X86Reg::RAX, X86Mem(base, idxReg, elementSize, 0));
				} else {
					encoder.imulRRI64(idxReg, idxReg, elementSize);
					encoder.lea64(X86Reg::RAX, X86Mem(base, idxReg, 1, 0));
				}
			}
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

void X86CodeGen::genBitcast(mir::MIRInstruction *inst) {
	mir::MIRValue *src = inst->operands[0];
	mir::MIRType *srcType = src->type;
	mir::MIRType *dstType = inst->result->type;

	// f64 -> i64: MOVQ r64, xmm
	if (srcType->kind == mir::MIRTypeKind::Float64 && dstType->kind == mir::MIRTypeKind::Int64) {
		X86Reg val = loadValueFloat(src, X86Reg::XMM0);
		encoder.movqXmmToGpr(X86Reg::RAX, val);
		storeResult(inst->result, X86Reg::RAX);
	}
	// i64 -> f64: MOVQ xmm, r64
	else if (srcType->kind == mir::MIRTypeKind::Int64 && dstType->kind == mir::MIRTypeKind::Float64) {
		X86Reg val = loadValue(src, X86Reg::RAX);
		encoder.movqGprToXmm(X86Reg::XMM0, val);
		storeResultFloat(inst->result, X86Reg::XMM0);
	}
	// ptr -> ptr or same-size int: just copy
	else {
		X86Reg val = loadValue(src, X86Reg::RAX);
		storeResult(inst->result, val);
	}
}

void X86CodeGen::genCopyConversion(mir::MIRInstruction *inst) {
	// For PtrToInt and IntToPtr on x86-64, these are no-ops
	// since pointers and i64 are the same size
	X86Reg val = loadValue(inst->operands[0], X86Reg::RAX);
	storeResult(inst->result, val);
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

	// Check if returning a struct larger than 8 bytes
	if (retVal->type->isStruct() && retVal->type->sizeInBytes > 8) {
		// Windows x64 ABI: Large structs are returned via hidden pointer
		// The hidden pointer was saved to stack in prologue (from RCX)
		// We need to copy the struct to that location and return the pointer in RAX

		// Load the hidden pointer from the saved stack location into RDI
		encoder.movRM64(X86Reg::RDI, X86Mem(X86Reg::RBP, regAlloc.hiddenRetPtrOffset));

		// Get the ADDRESS of the return value struct into RSI
		// For large structs, the value is stored on the stack - we need LEA, not MOV
		X86Reg srcReg;
		if (retVal->kind == mir::MIRValueKind::VirtualReg &&
			!regAlloc.allocaRegs.count(retVal->regId) &&
			getAllocatedReg(retVal) == X86Reg::None) {
			// Large struct is stored directly on stack - use lea to get address
			X86Mem slot = getStackSlot(retVal);
			encoder.lea64(X86Reg::RSI, slot);
			srcReg = X86Reg::RSI;
		} else {
			// For alloca results or other pointer values, loadValue gives address
			srcReg = loadValue(retVal, X86Reg::RSI);
		}

		// Copy the struct (use 8-byte moves when possible for efficiency)
		uint64_t structSize = retVal->type->sizeInBytes;
		uint64_t offset = 0;
		// Copy 8 bytes at a time
		for (; offset + 8 <= structSize; offset += 8) {
			encoder.movRM64(X86Reg::RAX, X86Mem(srcReg, offset));
			encoder.movMR64(X86Mem(X86Reg::RDI, offset), X86Reg::RAX);
		}
		// Copy remaining 4 bytes if any
		if (offset + 4 <= structSize) {
			encoder.movRM32(X86Reg::EAX, X86Mem(srcReg, offset));
			encoder.movMR32(X86Mem(X86Reg::RDI, offset), X86Reg::EAX);
			offset += 4;
		}
		// Note: Remaining 1-3 bytes would need byte moves, but structs are typically aligned

		// Return the pointer in RAX per ABI
		encoder.movRR64(X86Reg::RAX, X86Reg::RDI);
	} else if (retVal->type->isFloat()) {
		loadValueFloat(retVal, X86Reg::XMM0);
	} else {
		loadValue(retVal, X86Reg::RAX);
	}

	generateEpilogue();
}

void X86CodeGen::genCall(mir::MIRInstruction *inst) {
	// Check for intrinsic math functions that can be inlined with native instructions
	if (inst->calleeName == "sqrt" && inst->operands.size() == 1 &&
		inst->operands[0]->type->isFloat()) {
		// Use native SQRTSD instruction instead of calling runtime function
		X86Reg src = loadValueFloat(inst->operands[0], X86Reg::XMM0);
		encoder.sqrtsdRR(X86Reg::XMM0, src);
		if (inst->result) {
			storeResultFloat(inst->result, X86Reg::XMM0);
		}
		return;
	}

	// Set up arguments according to calling convention
	std::vector<X86Reg> argRegs;
	if (callingConv == CallingConv::Win64) {
		argRegs = {X86Reg::RCX, X86Reg::RDX, X86Reg::R8, X86Reg::R9};
	} else {
		argRegs = {X86Reg::RDI, X86Reg::RSI, X86Reg::RDX, X86Reg::RCX, X86Reg::R8, X86Reg::R9};
	}

	// Check if this function returns a large struct (> 8 bytes)
	bool hasHiddenRetPtr = false;
	if (inst->result && inst->result->type->isStruct() && inst->result->type->sizeInBytes > 8) {
		// Windows x64 ABI: Caller allocates space and passes pointer as first arg
		hasHiddenRetPtr = true;

		// The struct result needs a stack location - get its stack slot
		X86Mem slot = getStackSlot(inst->result);

		// Load the address of that slot into the first argument register
		encoder.lea64(argRegs[0], slot);
	}

	// First, handle stack arguments (args beyond register count)
	// Stack args go at RSP+32+offset on Win64 (after shadow space)
	size_t argOffset = hasHiddenRetPtr ? 1 : 0;
	size_t numRegisterArgs = argRegs.size();
	int32_t shadowSpaceSize = (callingConv == CallingConv::Win64) ? 32 : 0;

	for (size_t i = 0; i < inst->operands.size(); ++i) {
		size_t regIndex = i + argOffset;
		if (regIndex >= numRegisterArgs) {
			// This argument goes on the stack
			mir::MIRValue *arg = inst->operands[i];
			size_t stackArgIndex = regIndex - numRegisterArgs;
			int32_t stackOffset = shadowSpaceSize + static_cast<int32_t>(stackArgIndex * 8);

			if (arg->type->isFloat()) {
				// Load float to XMM0, then store to stack
				loadValueFloat(arg, X86Reg::XMM0);
				encoder.movsdMR(X86Mem(X86Reg::RSP, stackOffset), X86Reg::XMM0);
			} else {
				// Load to R10 (scratch register), then store to stack
				loadValue(arg, X86Reg::R10);
				encoder.movMR64(X86Mem(X86Reg::RSP, stackOffset), X86Reg::R10);
			}
		}
	}

	// Load register arguments (args that fit in registers)
	for (size_t i = 0; i < inst->operands.size(); ++i) {
		size_t regIndex = i + argOffset;
		if (regIndex >= argRegs.size()) {
			break; // Remaining args are on stack, already handled above
		}

		mir::MIRValue *arg = inst->operands[i];
		if (arg->type->isFloat()) {
			loadValueFloat(arg, static_cast<X86Reg>(static_cast<int>(X86Reg::XMM0) + i));
		} else {
			loadValue(arg, argRegs[regIndex]);
		}
	}

	// Check if this is an external function (needs indirect call through IAT)
	bool isExternal = inst->calleeFunc && inst->calleeFunc->isExternal;

	if (isExternal) {
		// External call via IAT - generate indirect call with RIP-relative addressing
		// CALL [RIP + disp32] - will be patched with actual IAT offset
		encoder.callM(X86Mem::RipRel(0)); // Placeholder displacement
		relocations.push_back({Relocation::Type::ImportCall,
							   encoder.getOffset() - 4,
							   inst->calleeName});
	} else {
		// Internal call - direct relative call
		encoder.callRel32(0); // Placeholder
		relocations.push_back({Relocation::Type::FunctionCall,
							   encoder.getOffset() - 4,
							   inst->calleeName});
	}

	// Store result
	if (inst->result) {
		if (hasHiddenRetPtr) {
			// Result was written to the hidden pointer location, nothing to do
			// The struct is already in the right place
		} else if (inst->result->type->isFloat()) {
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
