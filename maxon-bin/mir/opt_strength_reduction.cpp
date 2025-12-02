#include "optimizer.h"

namespace mir {

//==============================================================================
// Strength Reduction Pass Implementation
//==============================================================================

bool StrengthReductionPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool StrengthReductionPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block);
	}
	return changed;
}

bool StrengthReductionPass::runOnBasicBlock(MIRBasicBlock &block) {
	bool changed = false;

	for (auto &inst : block.instructions) {
		// mul x, 2^n -> shl x, n
		if (inst->opcode == MIROpcode::Mul && inst->operands.size() == 2) {
			auto *lhs = inst->operands[0];
			auto *rhs = inst->operands[1];

			// Check if rhs is a constant power of two
			if (rhs->kind == MIRValueKind::ConstantInt) {
				int exponent = isPowerOfTwo(rhs->intValue);
				if (exponent >= 0) {
					inst->opcode = MIROpcode::Shl;
					inst->operands[1] = MIRValue::createConstantInt(rhs->type, exponent);
					changed = true;
					lastRunStats_++;
					continue;
				}
			}
			// Check if lhs is a constant power of two (mul is commutative)
			if (lhs->kind == MIRValueKind::ConstantInt) {
				int exponent = isPowerOfTwo(lhs->intValue);
				if (exponent >= 0) {
					inst->opcode = MIROpcode::Shl;
					inst->operands[0] = rhs;
					inst->operands[1] = MIRValue::createConstantInt(lhs->type, exponent);
					changed = true;
					lastRunStats_++;
					continue;
				}
			}
		}

		// udiv x, 2^n -> lshr x, n (only for unsigned)
		if (inst->opcode == MIROpcode::UDiv && inst->operands.size() == 2) {
			auto *rhs = inst->operands[1];
			if (rhs->kind == MIRValueKind::ConstantInt) {
				int exponent = isPowerOfTwo(rhs->intValue);
				if (exponent >= 0) {
					inst->opcode = MIROpcode::LShr;
					inst->operands[1] = MIRValue::createConstantInt(rhs->type, exponent);
					changed = true;
					lastRunStats_++;
				}
			}
		}

		// urem x, 2^n -> and x, (2^n - 1) (only for unsigned)
		if (inst->opcode == MIROpcode::URem && inst->operands.size() == 2) {
			auto *rhs = inst->operands[1];
			if (rhs->kind == MIRValueKind::ConstantInt) {
				int exponent = isPowerOfTwo(rhs->intValue);
				if (exponent >= 0) {
					inst->opcode = MIROpcode::And;
					inst->operands[1] = MIRValue::createConstantInt(rhs->type, (1LL << exponent) - 1);
					changed = true;
					lastRunStats_++;
				}
			}
		}
	}

	return changed;
}

int StrengthReductionPass::isPowerOfTwo(int64_t value) {
	if (value <= 0) {
		return -1;
	}
	if ((value & (value - 1)) != 0) {
		return -1;
	}
	// Count trailing zeros to get exponent
	int exponent = 0;
	while ((value & 1) == 0) {
		value >>= 1;
		exponent++;
	}
	return exponent;
}

} // namespace mir
