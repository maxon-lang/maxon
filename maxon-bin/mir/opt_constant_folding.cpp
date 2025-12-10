#include "optimizer.h"
#include <cmath>

namespace mir {

//==============================================================================
// Constant Folding Pass Implementation
//==============================================================================

bool ConstantFoldingPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool ConstantFoldingPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block);
	}
	return changed;
}

bool ConstantFoldingPass::runOnBasicBlock(MIRBasicBlock &block) {
	bool changed = false;

	for (auto &inst : block.instructions) {
		MIRValue *foldedValue = tryFold(inst.get());
		if (foldedValue != nullptr) {
			// Replace all uses of the result with the folded constant across the function
			if (block.parent != nullptr) {
				opt_utils::replaceAllUsesWith(*block.parent, inst->result, foldedValue);
			} else {
				opt_utils::replaceAllUsesInBlock(block, inst->result, foldedValue);
			}
			changed = true;
			lastRunStats_++; // Track constants folded
		}
	}

	return changed;
}

MIRValue *ConstantFoldingPass::tryFold(MIRInstruction *inst) {
	// Handle unary operations first (Neg, FNeg)
	if (inst->operands.size() == 1) {
		auto *operand = inst->operands[0];
		if (!operand->isConstant()) {
			return nullptr;
		}

		if (inst->opcode == MIROpcode::Neg && operand->kind == MIRValueKind::ConstantInt) {
			return MIRValue::createConstantInt(
				inst->result ? inst->result->type : MIRType::getInt32(),
				-operand->intValue);
		}
		if (inst->opcode == MIROpcode::FNeg && operand->kind == MIRValueKind::ConstantFloat) {
			return MIRValue::createConstantFloat(-operand->floatValue);
		}
		return nullptr;
	}

	// Check if all operands are constants (binary operations)
	if (inst->operands.size() < 2) {
		return nullptr;
	}

	auto *lhs = inst->operands[0];
	auto *rhs = inst->operands[1];

	if (!lhs->isConstant() || !rhs->isConstant()) {
		return nullptr;
	}

	// Integer operations
	if (lhs->kind == MIRValueKind::ConstantInt && rhs->kind == MIRValueKind::ConstantInt) {
		int64_t lhsVal = lhs->intValue;
		int64_t rhsVal = rhs->intValue;

		// Arithmetic operations
		switch (inst->opcode) {
		case MIROpcode::Add:
		case MIROpcode::Sub:
		case MIROpcode::Mul:
		case MIROpcode::SDiv:
		case MIROpcode::SRem:
		case MIROpcode::UDiv:
		case MIROpcode::URem:
		case MIROpcode::And:
		case MIROpcode::Or:
		case MIROpcode::Xor:
		case MIROpcode::Shl:
		case MIROpcode::AShr:
		case MIROpcode::LShr:
			return foldIntegerArithmetic(inst->opcode, lhsVal, rhsVal,
										 inst->result ? inst->result->type : MIRType::getInt64());

		// Comparison operations
		case MIROpcode::ICmpEq:
		case MIROpcode::ICmpNe:
		case MIROpcode::ICmpSLT:
		case MIROpcode::ICmpSLE:
		case MIROpcode::ICmpSGT:
		case MIROpcode::ICmpSGE:
		case MIROpcode::ICmpULT:
		case MIROpcode::ICmpULE:
		case MIROpcode::ICmpUGT:
		case MIROpcode::ICmpUGE:
			return foldIntegerComparison(inst->opcode, lhsVal, rhsVal);

		default:
			break;
		}
	}

	// Floating-point operations
	if (lhs->kind == MIRValueKind::ConstantFloat && rhs->kind == MIRValueKind::ConstantFloat) {
		double lhsVal = lhs->floatValue;
		double rhsVal = rhs->floatValue;

		switch (inst->opcode) {
		case MIROpcode::FAdd:
		case MIROpcode::FSub:
		case MIROpcode::FMul:
		case MIROpcode::FDiv:
		case MIROpcode::FRem:
			return foldFloatArithmetic(inst->opcode, lhsVal, rhsVal);

		case MIROpcode::FCmpEq:
		case MIROpcode::FCmpNe:
		case MIROpcode::FCmpLT:
		case MIROpcode::FCmpLE:
		case MIROpcode::FCmpGT:
		case MIROpcode::FCmpGE:
			return foldFloatComparison(inst->opcode, lhsVal, rhsVal);

		default:
			break;
		}
	}

	return nullptr;
}

MIRValue *ConstantFoldingPass::foldIntegerArithmetic(MIROpcode op, int64_t lhs, int64_t rhs,
													 MIRType *resultType) {
	int64_t result;

	switch (op) {
	case MIROpcode::Add:
		result = lhs + rhs;
		break;
	case MIROpcode::Sub:
		result = lhs - rhs;
		break;
	case MIROpcode::Mul:
		result = lhs * rhs;
		break;
	case MIROpcode::SDiv:
		if (rhs == 0)
			return nullptr; // Can't fold division by zero
		result = lhs / rhs;
		break;
	case MIROpcode::SRem:
		if (rhs == 0)
			return nullptr;
		result = lhs % rhs;
		break;
	case MIROpcode::UDiv:
		if (rhs == 0)
			return nullptr;
		result = static_cast<int64_t>(static_cast<uint64_t>(lhs) / static_cast<uint64_t>(rhs));
		break;
	case MIROpcode::URem:
		if (rhs == 0)
			return nullptr;
		result = static_cast<int64_t>(static_cast<uint64_t>(lhs) % static_cast<uint64_t>(rhs));
		break;
	case MIROpcode::And:
		result = lhs & rhs;
		break;
	case MIROpcode::Or:
		result = lhs | rhs;
		break;
	case MIROpcode::Xor:
		result = lhs ^ rhs;
		break;
	case MIROpcode::Shl:
		result = lhs << rhs;
		break;
	case MIROpcode::AShr:
		result = lhs >> rhs; // Arithmetic shift (preserves sign)
		break;
	case MIROpcode::LShr:
		result = static_cast<int64_t>(static_cast<uint64_t>(lhs) >> rhs);
		break;
	default:
		return nullptr;
	}

	return MIRValue::createConstantInt(resultType, result);
}

MIRValue *ConstantFoldingPass::foldFloatArithmetic(MIROpcode op, double lhs, double rhs) {
	double result;

	switch (op) {
	case MIROpcode::FAdd:
		result = lhs + rhs;
		break;
	case MIROpcode::FSub:
		result = lhs - rhs;
		break;
	case MIROpcode::FMul:
		result = lhs * rhs;
		break;
	case MIROpcode::FDiv:
		result = lhs / rhs; // IEEE 754 handles division by zero
		break;
	case MIROpcode::FRem:
		result = std::fmod(lhs, rhs);
		break;
	default:
		return nullptr;
	}

	return MIRValue::createConstantFloat(result);
}

MIRValue *ConstantFoldingPass::foldIntegerComparison(MIROpcode op, int64_t lhs, int64_t rhs) {
	bool result;

	switch (op) {
	case MIROpcode::ICmpEq:
		result = (lhs == rhs);
		break;
	case MIROpcode::ICmpNe:
		result = (lhs != rhs);
		break;
	case MIROpcode::ICmpSLT:
		result = (lhs < rhs);
		break;
	case MIROpcode::ICmpSLE:
		result = (lhs <= rhs);
		break;
	case MIROpcode::ICmpSGT:
		result = (lhs > rhs);
		break;
	case MIROpcode::ICmpSGE:
		result = (lhs >= rhs);
		break;
	case MIROpcode::ICmpULT:
		result = (static_cast<uint64_t>(lhs) < static_cast<uint64_t>(rhs));
		break;
	case MIROpcode::ICmpULE:
		result = (static_cast<uint64_t>(lhs) <= static_cast<uint64_t>(rhs));
		break;
	case MIROpcode::ICmpUGT:
		result = (static_cast<uint64_t>(lhs) > static_cast<uint64_t>(rhs));
		break;
	case MIROpcode::ICmpUGE:
		result = (static_cast<uint64_t>(lhs) >= static_cast<uint64_t>(rhs));
		break;
	default:
		return nullptr;
	}

	return MIRValue::createConstantInt(MIRType::getInt1(), result ? 1 : 0);
}

MIRValue *ConstantFoldingPass::foldFloatComparison(MIROpcode op, double lhs, double rhs) {
	bool result;

	switch (op) {
	case MIROpcode::FCmpEq:
		result = (lhs == rhs);
		break;
	case MIROpcode::FCmpNe:
		result = (lhs != rhs);
		break;
	case MIROpcode::FCmpLT:
		result = (lhs < rhs);
		break;
	case MIROpcode::FCmpLE:
		result = (lhs <= rhs);
		break;
	case MIROpcode::FCmpGT:
		result = (lhs > rhs);
		break;
	case MIROpcode::FCmpGE:
		result = (lhs >= rhs);
		break;
	default:
		return nullptr;
	}

	return MIRValue::createConstantInt(MIRType::getInt1(), result ? 1 : 0);
}

} // namespace mir
