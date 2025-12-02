#include "optimizer.h"

namespace mir {

//==============================================================================
// Copy Propagation Pass Implementation
//==============================================================================

bool CopyPropagationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool CopyPropagationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	std::unordered_map<uint32_t, MIRValue *> copyMap;

	// First pass: collect all copy instructions
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Copy && inst->result != nullptr &&
				inst->result->kind == MIRValueKind::VirtualReg &&
				inst->operands.size() == 1) {
				copyMap[inst->result->regId] = inst->operands[0];
			}
		}
	}

	// Second pass: propagate copies
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block, copyMap);
	}

	return changed;
}

bool CopyPropagationPass::runOnBasicBlock(MIRBasicBlock &block,
										  std::unordered_map<uint32_t, MIRValue *> &copyMap) {
	bool changed = false;

	for (auto &inst : block.instructions) {
		// Don't modify the copy instruction's operands themselves
		if (inst->opcode == MIROpcode::Copy) {
			continue;
		}

		// Replace operands that are copies
		for (size_t i = 0; i < inst->operands.size(); ++i) {
			auto *operand = inst->operands[i];
			if (operand->kind == MIRValueKind::VirtualReg) {
				auto it = copyMap.find(operand->regId);
				if (it != copyMap.end()) {
					inst->operands[i] = it->second;
					lastRunStats_++;
					changed = true;
				}
			}
		}

		// Replace phi incoming values
		for (auto &incoming : inst->phiIncoming) {
			if (incoming.first->kind == MIRValueKind::VirtualReg) {
				auto it = copyMap.find(incoming.first->regId);
				if (it != copyMap.end()) {
					incoming.first = it->second;
					lastRunStats_++;
					changed = true;
				}
			}
		}
	}

	return changed;
}

} // namespace mir
