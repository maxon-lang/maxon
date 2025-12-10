#include "optimizer.h"

namespace mir {

//==============================================================================
// Algebraic Simplification Pass Implementation
//==============================================================================

bool AlgebraicSimplificationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool AlgebraicSimplificationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block);
	}
	return changed;
}

bool AlgebraicSimplificationPass::runOnBasicBlock(MIRBasicBlock &block) {
	bool changed = false;

	for (auto &inst : block.instructions) {
		MIRValue *simplified = trySimplify(inst.get());
		if (simplified != nullptr) {
			// Replace all uses of the result with the simplified value across the function
			if (block.parent != nullptr) {
				opt_utils::replaceAllUsesWith(*block.parent, inst->result, simplified);
			} else {
				opt_utils::replaceAllUsesInBlock(block, inst->result, simplified);
			}
			lastRunStats_++;
			changed = true;
		}
	}

	return changed;
}

MIRValue *AlgebraicSimplificationPass::trySimplify(MIRInstruction *inst) {
	if (inst->operands.size() < 2) {
		return nullptr;
	}

	auto *lhs = inst->operands[0];
	auto *rhs = inst->operands[1];

	// Check for identity and zero element operations
	bool lhsIsZero = (lhs->kind == MIRValueKind::ConstantInt && lhs->intValue == 0);
	bool rhsIsZero = (rhs->kind == MIRValueKind::ConstantInt && rhs->intValue == 0);
	bool lhsIsOne = (lhs->kind == MIRValueKind::ConstantInt && lhs->intValue == 1);
	bool rhsIsOne = (rhs->kind == MIRValueKind::ConstantInt && rhs->intValue == 1);
	bool lhsIsAllOnes = (lhs->kind == MIRValueKind::ConstantInt && lhs->intValue == -1);
	bool rhsIsAllOnes = (rhs->kind == MIRValueKind::ConstantInt && rhs->intValue == -1);

	switch (inst->opcode) {
	case MIROpcode::Add:
		// add x, 0 -> x
		if (rhsIsZero)
			return lhs;
		// add 0, x -> x
		if (lhsIsZero)
			return rhs;
		break;

	case MIROpcode::Sub:
		// sub x, 0 -> x
		if (rhsIsZero)
			return lhs;
		// sub x, x -> 0
		if (lhs == rhs)
			return MIRValue::createConstantInt(inst->result->type, 0);
		break;

	case MIROpcode::Mul:
		// mul x, 0 -> 0
		if (rhsIsZero)
			return MIRValue::createConstantInt(inst->result->type, 0);
		// mul 0, x -> 0
		if (lhsIsZero)
			return MIRValue::createConstantInt(inst->result->type, 0);
		// mul x, 1 -> x
		if (rhsIsOne)
			return lhs;
		// mul 1, x -> x
		if (lhsIsOne)
			return rhs;
		break;

	case MIROpcode::SDiv:
	case MIROpcode::UDiv:
		// div x, 1 -> x
		if (rhsIsOne)
			return lhs;
		// div 0, x -> 0 (if x != 0, but we can't check runtime values)
		break;

	case MIROpcode::And:
		// and x, 0 -> 0
		if (rhsIsZero)
			return MIRValue::createConstantInt(inst->result->type, 0);
		// and 0, x -> 0
		if (lhsIsZero)
			return MIRValue::createConstantInt(inst->result->type, 0);
		// and x, -1 -> x
		if (rhsIsAllOnes)
			return lhs;
		// and -1, x -> x
		if (lhsIsAllOnes)
			return rhs;
		// and x, x -> x
		if (lhs == rhs)
			return lhs;
		break;

	case MIROpcode::Or:
		// or x, 0 -> x
		if (rhsIsZero)
			return lhs;
		// or 0, x -> x
		if (lhsIsZero)
			return rhs;
		// or x, -1 -> -1
		if (rhsIsAllOnes)
			return MIRValue::createConstantInt(inst->result->type, -1);
		// or -1, x -> -1
		if (lhsIsAllOnes)
			return MIRValue::createConstantInt(inst->result->type, -1);
		// or x, x -> x
		if (lhs == rhs)
			return lhs;
		break;

	case MIROpcode::Xor:
		// xor x, 0 -> x
		if (rhsIsZero)
			return lhs;
		// xor 0, x -> x
		if (lhsIsZero)
			return rhs;
		// xor x, x -> 0
		if (lhs == rhs)
			return MIRValue::createConstantInt(inst->result->type, 0);
		break;

	case MIROpcode::Shl:
	case MIROpcode::AShr:
	case MIROpcode::LShr:
		// shift x, 0 -> x
		if (rhsIsZero)
			return lhs;
		// shift 0, x -> 0
		if (lhsIsZero)
			return MIRValue::createConstantInt(inst->result->type, 0);
		break;

	// Floating-point operations
	case MIROpcode::FAdd: {
		bool lhsIsZeroF = (lhs->kind == MIRValueKind::ConstantFloat && lhs->floatValue == 0.0);
		bool rhsIsZeroF = (rhs->kind == MIRValueKind::ConstantFloat && rhs->floatValue == 0.0);
		if (rhsIsZeroF)
			return lhs;
		if (lhsIsZeroF)
			return rhs;
		break;
	}

	case MIROpcode::FSub: {
		bool rhsIsZeroF = (rhs->kind == MIRValueKind::ConstantFloat && rhs->floatValue == 0.0);
		if (rhsIsZeroF)
			return lhs;
		break;
	}

	case MIROpcode::FMul: {
		bool lhsIsZeroF = (lhs->kind == MIRValueKind::ConstantFloat && lhs->floatValue == 0.0);
		bool rhsIsZeroF = (rhs->kind == MIRValueKind::ConstantFloat && rhs->floatValue == 0.0);
		bool lhsIsOneF = (lhs->kind == MIRValueKind::ConstantFloat && lhs->floatValue == 1.0);
		bool rhsIsOneF = (rhs->kind == MIRValueKind::ConstantFloat && rhs->floatValue == 1.0);
		if (rhsIsZeroF)
			return MIRValue::createConstantFloat(0.0);
		if (lhsIsZeroF)
			return MIRValue::createConstantFloat(0.0);
		if (rhsIsOneF)
			return lhs;
		if (lhsIsOneF)
			return rhs;
		break;
	}

	case MIROpcode::FDiv: {
		bool rhsIsOneF = (rhs->kind == MIRValueKind::ConstantFloat && rhs->floatValue == 1.0);
		if (rhsIsOneF)
			return lhs;
		break;
	}

	default:
		break;
	}

	return nullptr;
}

} // namespace mir
