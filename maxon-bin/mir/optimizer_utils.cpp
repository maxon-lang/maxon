#include "optimizer.h"

namespace mir {

//==============================================================================
// Utility Functions Implementation
//==============================================================================

namespace opt_utils {

void replaceAllUsesWith(MIRFunction &func, MIRValue *oldVal, MIRValue *newVal) {
	for (auto &block : func.basicBlocks) {
		replaceAllUsesInBlock(*block, oldVal, newVal);
	}
}

void replaceAllUsesInBlock(MIRBasicBlock &block, MIRValue *oldVal, MIRValue *newVal) {
	for (auto &inst : block.instructions) {
		// Replace in operands
		for (size_t i = 0; i < inst->operands.size(); ++i) {
			if (inst->operands[i] == oldVal) {
				inst->operands[i] = newVal;
			}
		}
		// Replace in phi incoming values
		for (auto &incoming : inst->phiIncoming) {
			if (incoming.first == oldVal) {
				incoming.first = newVal;
			}
		}
	}
}

bool isValueUsed(MIRFunction &func, MIRValue *val) {
	for (auto &block : func.basicBlocks) {
		if (isValueUsedInBlock(*block, val)) {
			return true;
		}
	}
	return false;
}

bool isValueUsedInBlock(MIRBasicBlock &block, MIRValue *val) {
	for (auto &inst : block.instructions) {
		for (auto *operand : inst->operands) {
			if (operand == val) {
				return true;
			}
		}
		for (auto &incoming : inst->phiIncoming) {
			if (incoming.first == val) {
				return true;
			}
		}
	}
	return false;
}

std::vector<MIRValue *> getDefinedValues(MIRBasicBlock &block) {
	std::vector<MIRValue *> defined;
	for (auto &inst : block.instructions) {
		if (inst->result != nullptr) {
			defined.push_back(inst->result);
		}
	}
	return defined;
}

std::vector<MIRValue *> getUsedValues(MIRInstruction *inst) {
	std::vector<MIRValue *> used;
	for (auto *operand : inst->operands) {
		if (operand != nullptr) {
			used.push_back(operand);
		}
	}
	for (auto &incoming : inst->phiIncoming) {
		if (incoming.first != nullptr) {
			used.push_back(incoming.first);
		}
	}
	return used;
}

} // namespace opt_utils

} // namespace mir
