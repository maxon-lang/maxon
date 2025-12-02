#include "optimizer.h"

namespace mir {

//==============================================================================
// Constant Propagation Pass Implementation
//==============================================================================

bool ConstantPropagationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool ConstantPropagationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	std::unordered_map<uint32_t, MIRValue *> constMap;

	// First pass: collect all constants defined
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			// Check for instructions that assign a constant to a register
			// This happens when operands are all constants and result is assigned
			if (inst->result != nullptr && inst->result->kind == MIRValueKind::VirtualReg) {
				// Check if this is effectively a constant (single operand that is constant)
				// or if all operands are constants (result will be folded later)
				// Skip Call instructions - their result is not a copy of the argument!
				// Skip Neg/FNeg - those are transformations, not copies!
				// Skip type conversion instructions - they change the type, not just copy!
				if (inst->opcode != MIROpcode::Call &&
					inst->opcode != MIROpcode::Neg &&
					inst->opcode != MIROpcode::FNeg &&
					inst->opcode != MIROpcode::SIToFP &&
					inst->opcode != MIROpcode::FPToSI &&
					inst->opcode != MIROpcode::ZExt &&
					inst->opcode != MIROpcode::SExt &&
					inst->opcode != MIROpcode::Trunc &&
					inst->opcode != MIROpcode::PtrToInt &&
					inst->opcode != MIROpcode::IntToPtr &&
					inst->opcode != MIROpcode::Bitcast &&
					inst->operands.size() == 1 && inst->operands[0]->isConstant()) {
					// Copy of a constant
					constMap[inst->result->regId] = inst->operands[0];
				}
			}
		}
	}

	// Second pass: propagate constants
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block, constMap);
	}

	return changed;
}

bool ConstantPropagationPass::runOnBasicBlock(MIRBasicBlock &block,
											  std::unordered_map<uint32_t, MIRValue *> &constMap) {
	bool changed = false;

	for (auto &inst : block.instructions) {
		// Replace operands that are virtual registers with their constant values
		for (size_t i = 0; i < inst->operands.size(); ++i) {
			auto *operand = inst->operands[i];
			if (operand->kind == MIRValueKind::VirtualReg) {
				auto it = constMap.find(operand->regId);
				if (it != constMap.end()) {
					inst->operands[i] = it->second;
					changed = true;
					lastRunStats_++; // Track constants propagated
				}
			}
		}

		// Update constMap if this instruction produces a constant
		// Skip Call instructions - their result is not a copy of the argument!
		// Skip Neg/FNeg - those are transformations, not copies!
		// Skip type conversion instructions - they change the type, not just copy!
		if (inst->result != nullptr && inst->result->kind == MIRValueKind::VirtualReg) {
			if (inst->opcode != MIROpcode::Call &&
				inst->opcode != MIROpcode::Neg &&
				inst->opcode != MIROpcode::FNeg &&
				inst->opcode != MIROpcode::SIToFP &&
				inst->opcode != MIROpcode::FPToSI &&
				inst->opcode != MIROpcode::ZExt &&
				inst->opcode != MIROpcode::SExt &&
				inst->opcode != MIROpcode::Trunc &&
				inst->opcode != MIROpcode::PtrToInt &&
				inst->opcode != MIROpcode::IntToPtr &&
				inst->opcode != MIROpcode::Bitcast &&
				inst->operands.size() == 1 && inst->operands[0]->isConstant()) {
				constMap[inst->result->regId] = inst->operands[0];
			}
		}
	}

	return changed;
}

} // namespace mir
