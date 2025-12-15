/**
 * x86-64 Code Generator - Register Allocation
 *
 * This file implements register allocation for the x86-64 backend.
 * Includes liveness analysis, callee-saved register management, and stack slot assignment.
 */

#include "x86_codegen.h"
#include <algorithm>
#include <stdexcept>

namespace backend {

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

// Helper to check if an XMM register is callee-saved (XMM6-XMM15 on Windows x64)
static bool isCalleeSavedXMM(X86Reg reg, CallingConv conv) {
	if (conv == CallingConv::Win64) {
		int regNum = static_cast<int>(reg);
		int xmm6 = static_cast<int>(X86Reg::XMM6);
		int xmm15 = static_cast<int>(X86Reg::XMM15);
		return regNum >= xmm6 && regNum <= xmm15;
	}
	// System V ABI: XMM registers are caller-saved
	return false;
}

void X86CodeGen::allocateRegisters(mir::MIRFunction *func) {
	regAlloc = RegAllocInfo();

	// Check if this function returns a large struct or Optional (> 8 bytes)
	// Windows x64 ABI: Large structs/optionals are returned via hidden pointer in RCX
	// This shifts all other parameters right by one register
	bool hasLargeReturn = (func->returnType->isStruct() ||
						   func->returnType->kind == mir::MIRTypeKind::Optional) &&
						  func->returnType->getSizeInBytes() > 8;
	regAlloc.hasHiddenRetPtr = hasLargeReturn;

	// =========================================================================
	// Liveness analysis: find values that are live across call instructions
	// These values MUST be in callee-saved registers or on the stack
	// =========================================================================
	std::unordered_set<uint64_t> liveAcrossCalls;

	// For each basic block, compute which values are defined before a call
	// and used after a call (within the same block or in successor blocks)
	for (auto &block : func->basicBlocks) {
		// Find all call instruction indices
		std::vector<size_t> callIndices;
		for (size_t i = 0; i < block->instructions.size(); ++i) {
			if (block->instructions[i]->opcode == mir::MIROpcode::Call) {
				callIndices.push_back(i);
			}
		}

		if (callIndices.empty())
			continue;

		// For each value defined before a call, check if it's used after any call
		std::unordered_map<uint64_t, size_t> defIndex; // value key -> instruction index where defined

		for (size_t i = 0; i < block->instructions.size(); ++i) {
			auto &inst = block->instructions[i];

			// Record where values are defined
			if (inst->result) {
				uint64_t key = makeAllocKey(inst->result);
				defIndex[key] = i;
			}

			// For each operand use, check if it crosses a call
			for (auto *op : inst->operands) {
				if (op->kind != mir::MIRValueKind::VirtualReg &&
					op->kind != mir::MIRValueKind::Parameter) {
					continue;
				}
				uint64_t key = makeAllocKey(op);

				// Check if this value was defined before a call that's before this use
				auto defIt = defIndex.find(key);
				if (defIt != defIndex.end()) {
					size_t defIdx = defIt->second;
					// Check if any call is between definition and this use
					for (size_t callIdx : callIndices) {
						if (defIdx < callIdx && callIdx < i) {
							// Value is live across this call
							liveAcrossCalls.insert(key);
							break;
						}
					}
				} else {
					// Value defined in a previous block or is a parameter
					// If there's any call before this use, the value crosses it
					for (size_t callIdx : callIndices) {
						if (callIdx < i) {
							liveAcrossCalls.insert(key);
							break;
						}
					}
				}
			}
		}

		// Values used in successor blocks that are defined before the last call
		// are also live across that call
		if (!callIndices.empty()) {
			size_t lastCallIdx = callIndices.back();
			for (auto &[key, defIdx] : defIndex) {
				if (defIdx < lastCallIdx) {
					// Check if this value is used in any successor block
					// For simplicity, assume values defined before a call and not
					// used after it in the same block might still be used later
					// This is conservative but safe
					bool usedAfterCall = false;
					for (size_t i = lastCallIdx + 1; i < block->instructions.size(); ++i) {
						for (auto *op : block->instructions[i]->operands) {
							if (op->kind == mir::MIRValueKind::VirtualReg ||
								op->kind == mir::MIRValueKind::Parameter) {
								if (makeAllocKey(op) == key) {
									usedAfterCall = true;
									break;
								}
							}
						}
						if (usedAfterCall)
							break;
					}
					// If used after call in same block, already handled above
					// For cross-block uses, we'd need full dataflow analysis
					// For now, be conservative: if a block has calls and terminates
					// with a branch (not ret), assume values might be used later
					if (!usedAfterCall && !block->instructions.empty()) {
						auto &lastInst = block->instructions.back();
						if (lastInst->opcode != mir::MIROpcode::Ret) {
							// Conservative: mark as live across call
							liveAcrossCalls.insert(key);
						}
					}
				}
			}
		}
	}

	// Also mark all parameters as live across calls (conservative)
	for (auto *param : func->parameters) {
		uint64_t key = makeAllocKey(param);
		liveAcrossCalls.insert(key);
	}

	// =========================================================================
	// Register allocation with liveness information
	// =========================================================================

	// Available general-purpose registers (excluding RSP, RBP)
	// Split into callee-saved (safe across calls) and caller-saved (clobbered by calls)
	std::vector<X86Reg> calleeSavedRegs;
	std::vector<X86Reg> callerSavedRegs;
	std::vector<X86Reg> calleeSavedFloatRegs;
	std::vector<X86Reg> callerSavedFloatRegs;
	std::vector<X86Reg> paramRegs; // Registers used for passing parameters (ABI)

	if (callingConv == CallingConv::Win64) {
		// Windows x64 ABI: first 4 integer params in RCX, RDX, R8, R9
		paramRegs = {X86Reg::RCX, X86Reg::RDX, X86Reg::R8, X86Reg::R9};
		// Windows: RAX, RCX, RDX, R8-R11 are caller-saved (volatile)
		// RBX, RSI, RDI, R12-R15 are callee-saved (non-volatile)
		calleeSavedRegs = {X86Reg::RBX, X86Reg::RSI, X86Reg::RDI,
						   X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
		// R10/R11 reserved as scratch registers for codegen (used by genStore, etc.)
		// RAX reserved for return values
		callerSavedRegs = {};
		// XMM registers: XMM6-XMM15 are callee-saved on Windows
		calleeSavedFloatRegs = {X86Reg::XMM6, X86Reg::XMM7, X86Reg::XMM8, X86Reg::XMM9,
								X86Reg::XMM10, X86Reg::XMM11, X86Reg::XMM12, X86Reg::XMM13,
								X86Reg::XMM14, X86Reg::XMM15};
		callerSavedFloatRegs = {}; // XMM0-5 used for params/returns
	} else {
		// System V: first 6 integer params in RDI, RSI, RDX, RCX, R8, R9
		paramRegs = {X86Reg::RDI, X86Reg::RSI, X86Reg::RDX, X86Reg::RCX, X86Reg::R8, X86Reg::R9};
		// System V: RBX, R12-R15 are callee-saved
		calleeSavedRegs = {X86Reg::RBX, X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
		// R10/R11 reserved as scratch registers for codegen (used by genStore, etc.)
		// RAX reserved for return values
		callerSavedRegs = {};
		// All XMM registers are caller-saved on System V
		calleeSavedFloatRegs = {};
		callerSavedFloatRegs = {X86Reg::XMM8, X86Reg::XMM9, X86Reg::XMM10, X86Reg::XMM11,
								X86Reg::XMM12, X86Reg::XMM13, X86Reg::XMM14, X86Reg::XMM15};
	}

	// Combined lists for allocation (callee-saved first, then caller-saved)
	std::vector<X86Reg> availableRegs = calleeSavedRegs;
	availableRegs.insert(availableRegs.end(), callerSavedRegs.begin(), callerSavedRegs.end());

	std::vector<X86Reg> availableFloatRegs = calleeSavedFloatRegs;
	availableFloatRegs.insert(availableFloatRegs.end(), callerSavedFloatRegs.begin(), callerSavedFloatRegs.end());

	// Indices for parameter allocation (parameters always use callee-saved registers)
	size_t paramRegIdx = 0;
	size_t paramFloatRegIdx = 0;
	int32_t stackOffset = 0; // Will be decremented before each allocation

	// Reserve stack slot for hidden return pointer if needed
	// This must be done before allocating other stack slots
	if (hasLargeReturn) {
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
		size_t regIndex = hasLargeReturn ? i + 1 : i;
		if (regIndex < paramRegs.size()) {
			if (hasLargeReturn) {
				// Shifted params must be saved to stack (volatile regs may be clobbered)
				stackOffset -= 8;
				regAlloc.stackSlots[key] = stackOffset;
				if (param->type->isFloat()) {
					// Float arrives in XMM at regIndex position
					regAlloc.floatParamSpills.push_back({floatParamRegs[regIndex], stackOffset});
				} else {
					// Integer arrives in GPR at regIndex position
					regAlloc.shiftedParamSaves.push_back({paramRegs[regIndex], stackOffset});
				}
			} else {
				// Normal case: parameter arrives in volatile register
				// Copy to non-volatile register to survive across function calls
				if (param->type->isFloat()) {
					// Float param arrives in volatile XMM0-3, allocate a non-volatile XMM
					if (paramFloatRegIdx < calleeSavedFloatRegs.size()) {
						X86Reg destReg = calleeSavedFloatRegs[paramFloatRegIdx++];
						regAlloc.regMap[key] = destReg;
						regAlloc.floatParamSaves.push_back({floatParamRegs[regIndex], destReg});
						// Track callee-saved XMM register usage (XMM6-15 on Windows)
						// Note: we'll allocate stack slots for these after counting all uses
						if (isCalleeSavedXMM(destReg, callingConv)) {
							bool found = false;
							for (const auto &entry : regAlloc.usedCalleeSavedXMM) {
								if (entry.first == destReg) {
									found = true;
									break;
								}
							}
							if (!found) {
								// Stack offset will be assigned later
								regAlloc.usedCalleeSavedXMM.push_back({destReg, 0});
							}
						}
					} else {
						// No more XMM registers available, spill to stack
						stackOffset -= 8;
						regAlloc.stackSlots[key] = stackOffset;
						// Record that this float param needs to be saved from volatile XMM to stack
						regAlloc.floatParamSpills.push_back({floatParamRegs[regIndex], stackOffset});
					}
				} else {
					// Integer param arrives in volatile RCX/RDX/R8/R9
					// Allocate a non-volatile GPR to hold it
					if (paramRegIdx < calleeSavedRegs.size()) {
						X86Reg destReg = calleeSavedRegs[paramRegIdx++];
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
					allocSize = static_cast<int>(inst->allocatedType->getSizeInBytes());
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
	// Values live across calls MUST use callee-saved registers or stack
	// Start from where parameter allocation left off
	size_t calleeSavedRegIdx = paramRegIdx;
	size_t callerSavedRegIdx = 0;
	size_t calleeSavedFloatIdx = paramFloatRegIdx;
	size_t callerSavedFloatIdx = 0;

	for (auto &block : func->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->result && inst->opcode != mir::MIROpcode::Alloca) {
				uint64_t key = makeAllocKey(inst->result);
				if (regAlloc.regMap.find(key) == regAlloc.regMap.end() &&
					regAlloc.stackSlots.find(key) == regAlloc.stackSlots.end()) {

					// Force large structs/optionals to stack (for Windows x64 ABI compatibility)
					bool forceStack = (inst->result->type->isStruct() ||
									   inst->result->type->kind == mir::MIRTypeKind::Optional) &&
									  inst->result->type->getSizeInBytes() > 8;

					// Check if this value is live across a call
					bool needsCalleeSaved = liveAcrossCalls.count(key) > 0;

					// Check if this is a float type
					bool isFloat = inst->result->type->isFloat();

					// Try to allocate a register
					if (!forceStack) {
						if (isFloat) {
							// Float allocation
							X86Reg allocatedReg = X86Reg::None;
							if (needsCalleeSaved) {
								// Must use callee-saved float register
								if (calleeSavedFloatIdx < calleeSavedFloatRegs.size()) {
									allocatedReg = calleeSavedFloatRegs[calleeSavedFloatIdx++];
								}
								// If no callee-saved available, will spill to stack below
							} else {
								// Can use any float register (try callee-saved first, then caller-saved)
								if (calleeSavedFloatIdx < calleeSavedFloatRegs.size()) {
									allocatedReg = calleeSavedFloatRegs[calleeSavedFloatIdx++];
								} else if (callerSavedFloatIdx < callerSavedFloatRegs.size()) {
									allocatedReg = callerSavedFloatRegs[callerSavedFloatIdx++];
								}
							}

							if (allocatedReg != X86Reg::None) {
								regAlloc.regMap[key] = allocatedReg;
								if (isCalleeSavedXMM(allocatedReg, callingConv)) {
									bool found = false;
									for (const auto &entry : regAlloc.usedCalleeSavedXMM) {
										if (entry.first == allocatedReg) {
											found = true;
											break;
										}
									}
									if (!found) {
										regAlloc.usedCalleeSavedXMM.push_back({allocatedReg, 0});
									}
								}
							} else {
								// Spill to stack
								stackOffset -= 8;
								regAlloc.stackSlots[key] = stackOffset;
							}
						} else {
							// Integer allocation
							X86Reg allocatedReg = X86Reg::None;
							if (needsCalleeSaved) {
								// Must use callee-saved GPR
								if (calleeSavedRegIdx < calleeSavedRegs.size()) {
									allocatedReg = calleeSavedRegs[calleeSavedRegIdx++];
								}
								// If no callee-saved available, will spill to stack below
							} else {
								// Can use any GPR (try callee-saved first, then caller-saved)
								if (calleeSavedRegIdx < calleeSavedRegs.size()) {
									allocatedReg = calleeSavedRegs[calleeSavedRegIdx++];
								} else if (callerSavedRegIdx < callerSavedRegs.size()) {
									allocatedReg = callerSavedRegs[callerSavedRegIdx++];
								}
							}

							if (allocatedReg != X86Reg::None) {
								regAlloc.regMap[key] = allocatedReg;
								if (isCalleeSaved(allocatedReg, callingConv)) {
									if (std::find(regAlloc.usedCalleeSaved.begin(),
												  regAlloc.usedCalleeSaved.end(),
												  allocatedReg) == regAlloc.usedCalleeSaved.end()) {
										regAlloc.usedCalleeSaved.push_back(allocatedReg);
									}
								}
							} else {
								// Spill to stack
								stackOffset -= 8;
								regAlloc.stackSlots[key] = stackOffset;
							}
						}
					} else {
						// Large struct - spill to stack
						int allocSize = static_cast<int>(inst->result->type->getSizeInBytes());
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
					inst->result->type->getSizeInBytes() > 8) {
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

	// Allocate stack space for callee-saved XMM registers
	// These need to be saved via movsd (can't push XMM registers)
	// Assign stack offsets to each XMM register that needs saving
	size_t numXMMSaves = regAlloc.usedCalleeSavedXMM.size();
	if (numXMMSaves > 0) {
		// Each XMM save takes 8 bytes (we only save the low 64-bits via movsd)
		// Allocate them at the end of the frame, before alignment padding
		int32_t xmmSaveOffset = -static_cast<int32_t>(baseFrameSize + numCalleeSaved * 8);
		for (size_t i = 0; i < numXMMSaves; ++i) {
			xmmSaveOffset -= 8;
			regAlloc.usedCalleeSavedXMM[i].second = xmmSaveOffset;
		}
		baseFrameSize += static_cast<uint32_t>(numXMMSaves * 8);

		// Re-align after adding XMM save space
		if (numCalleeSaved % 2 == 1) {
			if (baseFrameSize % 16 == 0) {
				baseFrameSize += 8;
			} else if (baseFrameSize % 16 != 8) {
				baseFrameSize += (24 - (baseFrameSize % 16)) % 16;
			}
		} else {
			if (baseFrameSize % 16 != 0) {
				baseFrameSize += 16 - (baseFrameSize % 16);
			}
		}
	}

	regAlloc.frameSize = baseFrameSize;

	// Adjust stack slot offsets to account for callee-saved register pushes
	// Callee-saved registers are pushed BETWEEN setting RBP and allocating the frame,
	// so they occupy [RBP-8], [RBP-16], etc.
	// Our local stack slots (allocas, spilled values) need to be below these.
	// Shift all negative offsets by -numCalleeSaved*8
	if (numCalleeSaved > 0) {
		int32_t adjustment = -static_cast<int32_t>(numCalleeSaved * 8);
		for (auto &slot : regAlloc.stackSlots) {
			if (slot.second < 0) {
				slot.second += adjustment;
			}
		}
		// Also adjust hiddenRetPtrOffset and shiftedParamSaves
		if (regAlloc.hiddenRetPtrOffset < 0) {
			regAlloc.hiddenRetPtrOffset += adjustment;
		}
		for (auto &save : regAlloc.shiftedParamSaves) {
			if (save.second < 0) {
				save.second += adjustment;
			}
		}
		// Adjust floatParamSpills offsets for spilled float parameters
		for (auto &spill : regAlloc.floatParamSpills) {
			if (spill.second < 0) {
				spill.second += adjustment;
			}
		}
	}
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

} // namespace backend
