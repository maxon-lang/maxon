#include "optimizer.h"
#include <algorithm>
#include <cmath>
#include <limits>
#include <queue>

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
			// Replace all uses of the result with the folded constant
			opt_utils::replaceAllUsesInBlock(block, inst->result, foldedValue);
			// Also replace in the parent function's other blocks
			if (block.parent != nullptr) {
				for (auto &otherBlock : block.parent->basicBlocks) {
					if (otherBlock.get() != &block) {
						opt_utils::replaceAllUsesInBlock(*otherBlock, inst->result, foldedValue);
					}
				}
			}
			changed = true;
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

//==============================================================================
// Dead Code Elimination Pass Implementation
//==============================================================================

bool DeadCodeEliminationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool DeadCodeEliminationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;

	// Compute the set of used values
	auto usedValues = computeUsedValues(func);

	// Remove dead instructions
	for (auto &block : func.basicBlocks) {
		auto &instructions = block->instructions;
		auto newEnd = std::remove_if(
			instructions.begin(), instructions.end(),
			[&](const std::unique_ptr<MIRInstruction> &inst) {
				// Don't remove instructions without results (side effects)
				if (inst->result == nullptr) {
					return false;
				}
				// Don't remove instructions with side effects
				if (hasSideEffects(inst.get())) {
					return false;
				}
				// Remove if result is not used
				if (usedValues.find(inst->result) == usedValues.end()) {
					changed = true;
					return true;
				}
				return false;
			});

		instructions.erase(newEnd, instructions.end());
	}

	return changed;
}

std::unordered_set<MIRValue *> DeadCodeEliminationPass::computeUsedValues(MIRFunction &func) {
	std::unordered_set<MIRValue *> used;

	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			// All operands are used
			for (auto *operand : inst->operands) {
				if (operand != nullptr) {
					used.insert(operand);
				}
			}
			// Phi incoming values are used
			for (auto &incoming : inst->phiIncoming) {
				if (incoming.first != nullptr) {
					used.insert(incoming.first);
				}
			}
		}
	}

	return used;
}

bool DeadCodeEliminationPass::hasSideEffects(MIRInstruction *inst) {
	switch (inst->opcode) {
	case MIROpcode::Store:
	case MIROpcode::Call:
	case MIROpcode::CallIndirect:
	case MIROpcode::Br:
	case MIROpcode::CondBr:
	case MIROpcode::Ret:
	case MIROpcode::RetVoid:
		return true;
	default:
		return false;
	}
}

//==============================================================================
// Unreachable Block Elimination Pass Implementation
//==============================================================================

bool UnreachableBlockEliminationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool UnreachableBlockEliminationPass::runOnFunction(MIRFunction &func) {
	if (func.basicBlocks.empty()) {
		return false;
	}

	auto reachable = computeReachableBlocks(func);

	// Remove unreachable blocks
	auto &blocks = func.basicBlocks;
	size_t originalSize = blocks.size();

	blocks.erase(
		std::remove_if(blocks.begin(), blocks.end(),
					   [&](const std::unique_ptr<MIRBasicBlock> &block) {
						   return reachable.find(block.get()) == reachable.end();
					   }),
		blocks.end());

	// Update predecessor/successor lists for remaining blocks
	for (auto &block : blocks) {
		block->predecessors.erase(
			std::remove_if(block->predecessors.begin(), block->predecessors.end(),
						   [&](MIRBasicBlock *pred) {
							   return reachable.find(pred) == reachable.end();
						   }),
			block->predecessors.end());

		block->successors.erase(
			std::remove_if(block->successors.begin(), block->successors.end(),
						   [&](MIRBasicBlock *succ) {
							   return reachable.find(succ) == reachable.end();
						   }),
			block->successors.end());
	}

	return blocks.size() != originalSize;
}

std::unordered_set<MIRBasicBlock *>
UnreachableBlockEliminationPass::computeReachableBlocks(MIRFunction &func) {
	std::unordered_set<MIRBasicBlock *> reachable;
	std::queue<MIRBasicBlock *> worklist;

	// Start from entry block
	if (!func.basicBlocks.empty()) {
		worklist.push(func.basicBlocks[0].get());
		reachable.insert(func.basicBlocks[0].get());
	}

	// BFS to find all reachable blocks
	while (!worklist.empty()) {
		MIRBasicBlock *block = worklist.front();
		worklist.pop();

		for (auto *succ : block->successors) {
			if (reachable.insert(succ).second) {
				worklist.push(succ);
			}
		}
	}

	return reachable;
}

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
			// Replace all uses of the result with the simplified value
			opt_utils::replaceAllUsesInBlock(block, inst->result, simplified);
			if (block.parent != nullptr) {
				for (auto &otherBlock : block.parent->basicBlocks) {
					if (otherBlock.get() != &block) {
						opt_utils::replaceAllUsesInBlock(*otherBlock, inst->result, simplified);
					}
				}
			}
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

//==============================================================================
// Simple Function Inlining Pass Implementation
//==============================================================================

bool SimpleFunctionInliningPass::run(MIRModule &module) {
	bool changed = false;

	// Find functions that can be inlined
	std::unordered_map<std::string, MIRFunction *> inlineCandidates;

	for (auto &func : module.functions) {
		if (func->isExternal || func->name == "main") {
			continue; // Don't inline external functions or main
		}

		if (canInline(*func)) {
			size_t callCount = countCallSites(module, func->name);
			size_t instCount = countInstructions(*func);

			// Inline if:
			// 1. Called only once, or
			// 2. Small leaf function (< half the max threshold)
			if (callCount == 1 || (isLeafFunction(*func) && instCount <= maxInlineInstructions / 2)) {
				inlineCandidates[func->name] = func.get();
			}
		}
	}

	// Inline call sites
	for (auto &func : module.functions) {
		if (func->isExternal) {
			continue;
		}

		for (auto &block : func->basicBlocks) {
			for (size_t i = 0; i < block->instructions.size(); ++i) {
				auto *inst = block->instructions[i].get();
				if (inst->opcode == MIROpcode::Call) {
					auto it = inlineCandidates.find(inst->calleeName);
					if (it != inlineCandidates.end() && it->second != func.get()) {
						if (inlineCallSite(*func, *block, inst, *it->second)) {
							changed = true;
							// Don't increment i - we need to reprocess this position
							--i;
						}
					}
				}
			}
		}
	}

	return changed;
}

size_t SimpleFunctionInliningPass::countInstructions(MIRFunction &func) {
	size_t count = 0;
	for (auto &block : func.basicBlocks) {
		count += block->instructions.size();
	}
	return count;
}

bool SimpleFunctionInliningPass::isLeafFunction(MIRFunction &func) {
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Call) {
				return false;
			}
		}
	}
	return true;
}

size_t SimpleFunctionInliningPass::countCallSites(MIRModule &module, const std::string &funcName) {
	size_t count = 0;
	for (auto &func : module.functions) {
		if (func->isExternal) {
			continue;
		}
		for (auto &block : func->basicBlocks) {
			for (auto &inst : block->instructions) {
				if (inst->opcode == MIROpcode::Call && inst->calleeName == funcName) {
					count++;
				}
			}
		}
	}
	return count;
}

bool SimpleFunctionInliningPass::canInline(MIRFunction &func) {
	// Don't inline if too large
	if (countInstructions(func) > maxInlineInstructions) {
		return false;
	}

	// Don't inline functions with alloca (stack allocations)
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Alloca) {
				return false;
			}
		}
	}

	// Must have exactly one return statement for simple inlining
	int returnCount = 0;
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Ret || inst->opcode == MIROpcode::RetVoid) {
				returnCount++;
			}
		}
	}

	return returnCount == 1;
}

bool SimpleFunctionInliningPass::inlineCallSite(MIRFunction &caller, MIRBasicBlock &block,
												MIRInstruction *callInst, MIRFunction &callee) {
	// Simple inlining: only handle single-block functions with one return
	if (callee.basicBlocks.size() != 1) {
		return false;
	}

	auto &calleeBlock = callee.basicBlocks[0];

	// Build parameter mapping: callee parameter -> call argument
	std::unordered_map<MIRValue *, MIRValue *> valueMap;
	for (size_t i = 0; i < callee.parameters.size() && i < callInst->operands.size(); ++i) {
		valueMap[callee.parameters[i]] = callInst->operands[i];
	}

	// Find the call instruction in the block
	size_t callIndex = 0;
	for (size_t i = 0; i < block.instructions.size(); ++i) {
		if (block.instructions[i].get() == callInst) {
			callIndex = i;
			break;
		}
	}

	// Clone callee instructions into caller, remapping values
	std::vector<std::unique_ptr<MIRInstruction>> newInstructions;
	MIRValue *returnValue = nullptr;

	for (auto &inst : calleeBlock->instructions) {
		if (inst->opcode == MIROpcode::Ret) {
			// Capture the return value
			if (!inst->operands.empty()) {
				auto *retVal = inst->operands[0];
				auto it = valueMap.find(retVal);
				returnValue = (it != valueMap.end()) ? it->second : retVal;
			}
			continue; // Don't copy the return instruction
		}
		if (inst->opcode == MIROpcode::RetVoid) {
			continue; // Don't copy void returns
		}

		// Clone the instruction
		auto newInst = std::make_unique<MIRInstruction>(inst->opcode);

		// Clone result (create new virtual register in caller)
		if (inst->result != nullptr) {
			newInst->result = caller.createVirtualReg(inst->result->type);
			valueMap[inst->result] = newInst->result;
		}

		// Remap operands
		for (auto *operand : inst->operands) {
			auto it = valueMap.find(operand);
			newInst->operands.push_back(it != valueMap.end() ? it->second : operand);
		}

		// Copy other fields
		newInst->calleeName = inst->calleeName;
		newInst->calleeFunc = inst->calleeFunc;
		newInst->sourceLine = inst->sourceLine;
		newInst->sourceColumn = inst->sourceColumn;
		newInst->parent = &block;

		newInstructions.push_back(std::move(newInst));
	}

	// Replace uses of call result with return value
	if (callInst->result != nullptr && returnValue != nullptr) {
		opt_utils::replaceAllUsesWith(caller, callInst->result, returnValue);
	}

	// Remove call instruction and insert inlined instructions
	block.instructions.erase(block.instructions.begin() + static_cast<long>(callIndex));

	// Insert new instructions at the call site
	for (size_t i = 0; i < newInstructions.size(); ++i) {
		block.instructions.insert(
			block.instructions.begin() + static_cast<long>(callIndex + i),
			std::move(newInstructions[i]));
	}

	return true;
}

//==============================================================================
// Redundant Load/Store Elimination Pass Implementation
//==============================================================================

bool RedundantLoadStoreEliminationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool RedundantLoadStoreEliminationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block);
	}
	return changed;
}

bool RedundantLoadStoreEliminationPass::runOnBasicBlock(MIRBasicBlock &block) {
	// TODO: This pass has a bug where it incorrectly correlates stores and loads
	// across different allocas, causing incorrect optimization.
	// Example: var x = 10; var y = 20; x = y; y = 30; return x + y
	// Gets incorrectly optimized. Disabling until fixed.
	return false;

	bool changed = false;

	// Track the last stored value for each pointer
	// Key: pointer value, Value: stored value
	std::unordered_map<MIRValue *, MIRValue *> lastStore;

	// Track which stores to remove (dead stores)
	std::unordered_set<MIRInstruction *> deadStores;

	// Track which instructions to mark for value replacement
	std::unordered_map<MIRInstruction *, MIRValue *> loadReplacements;

	for (auto &inst : block.instructions) {
		if (inst->opcode == MIROpcode::Store) {
			// store value, ptr
			if (inst->operands.size() >= 2) {
				auto *value = inst->operands[0];
				auto *ptr = inst->operands[1];

				// If we have a previous store to the same pointer with no
				// intervening load, the previous store is dead
				auto it = lastStore.find(ptr);
				if (it != lastStore.end()) {
					// Find the store instruction and mark it for removal
					for (auto &prevInst : block.instructions) {
						if (prevInst->opcode == MIROpcode::Store &&
							prevInst->operands.size() >= 2 &&
							prevInst->operands[1] == ptr &&
							prevInst.get() != inst.get() &&
							deadStores.find(prevInst.get()) == deadStores.end()) {
							// Check if it's the most recent store
							if (prevInst->operands[0] == it->second) {
								deadStores.insert(prevInst.get());
								changed = true;
							}
						}
					}
				}

				lastStore[ptr] = value;
			}
		} else if (inst->opcode == MIROpcode::Load) {
			// load ptr
			if (inst->operands.size() >= 1) {
				auto *ptr = inst->operands[0];

				// If we have a recent store to this pointer, use the stored value
				auto it = lastStore.find(ptr);
				if (it != lastStore.end()) {
					loadReplacements[inst.get()] = it->second;
					changed = true;
				}
			}
		} else if (inst->opcode == MIROpcode::Call) {
			// Function calls may modify memory - invalidate all tracked stores
			lastStore.clear();
		}
	}

	// Apply load replacements
	for (auto &[loadInst, replacement] : loadReplacements) {
		if (loadInst->result != nullptr) {
			opt_utils::replaceAllUsesInBlock(block, loadInst->result, replacement);
			if (block.parent != nullptr) {
				for (auto &otherBlock : block.parent->basicBlocks) {
					if (otherBlock.get() != &block) {
						opt_utils::replaceAllUsesInBlock(*otherBlock, loadInst->result, replacement);
					}
				}
			}
		}
	}

	// Remove dead stores
	auto &instructions = block.instructions;
	instructions.erase(
		std::remove_if(instructions.begin(), instructions.end(),
					   [&](const std::unique_ptr<MIRInstruction> &inst) {
						   return deadStores.find(inst.get()) != deadStores.end();
					   }),
		instructions.end());

	return changed;
}

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
					changed = true;
				}
			}
		}
	}

	return changed;
}

//==============================================================================
// PHI Elimination Pass Implementation
//==============================================================================

bool PhiEliminationPass::run(MIRModule &module) {
	bool changed = false;
	splitBlockCounter_ = 0;

	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool PhiEliminationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;

	// Collect all PHI nodes and their blocks first
	// (we'll modify blocks during iteration, so collect upfront)
	struct PhiInfo {
		MIRBasicBlock *block;
		MIRInstruction *phi;
	};
	std::vector<PhiInfo> phiNodes;

	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Phi) {
				phiNodes.push_back({block.get(), inst.get()});
			}
		}
	}

	if (phiNodes.empty()) {
		return false;
	}

	logDetail("Processing " + std::to_string(phiNodes.size()) + " PHI node(s) in " + func.name);

	// Map to track split blocks: (pred, succ) -> splitBlock
	std::map<std::pair<MIRBasicBlock *, MIRBasicBlock *>, MIRBasicBlock *> splitBlocks;

	// Process each PHI node
	for (auto &phiInfo : phiNodes) {
		MIRBasicBlock *phiBlock = phiInfo.block;
		MIRInstruction *phi = phiInfo.phi;
		MIRValue *phiResult = phi->result;

		logTrace("Processing PHI: %" + std::to_string(phiResult->regId) +
				 " in block " + phiBlock->name);

		// For each incoming value/block pair
		for (auto &incoming : phi->phiIncoming) {
			MIRValue *incomingValue = incoming.first;
			MIRBasicBlock *predBlock = incoming.second;

			logTrace("  Incoming: value from " + predBlock->name);

			// Check if this is a critical edge
			MIRBasicBlock *insertBlock = predBlock;
			if (isCriticalEdge(predBlock, phiBlock)) {
				// Check if we already split this edge
				auto key = std::make_pair(predBlock, phiBlock);
				auto it = splitBlocks.find(key);
				if (it != splitBlocks.end()) {
					insertBlock = it->second;
				} else {
					insertBlock = splitCriticalEdge(func, predBlock, phiBlock);
					splitBlocks[key] = insertBlock;
					logTrace("    Split critical edge: " + predBlock->name + " -> " +
							 insertBlock->name + " -> " + phiBlock->name);
				}
			}

			// Insert copy instruction at the end of insertBlock (before terminator)
			insertCopyBeforeTerminator(insertBlock, phiResult, incomingValue);
			changed = true;
		}
	}

	// Remove all PHI instructions
	for (auto &block : func.basicBlocks) {
		auto &instructions = block->instructions;
		instructions.erase(
			std::remove_if(instructions.begin(), instructions.end(),
						   [](const std::unique_ptr<MIRInstruction> &inst) {
							   return inst->opcode == MIROpcode::Phi;
						   }),
			instructions.end());
	}

	return changed;
}

bool PhiEliminationPass::isCriticalEdge(MIRBasicBlock *pred, MIRBasicBlock *succ) {
	// Critical edge: pred has multiple successors AND succ has multiple predecessors
	return pred->successors.size() > 1 && succ->predecessors.size() > 1;
}

MIRBasicBlock *PhiEliminationPass::splitCriticalEdge(MIRFunction &func,
													 MIRBasicBlock *pred,
													 MIRBasicBlock *succ) {
	// Create a new basic block for the split edge
	std::string splitName = "split." + pred->name + "." + succ->name + "." +
							std::to_string(splitBlockCounter_++);

	auto splitBlock = std::make_unique<MIRBasicBlock>(splitName);
	MIRBasicBlock *splitPtr = splitBlock.get();

	// The split block just branches unconditionally to succ
	auto brInst = std::make_unique<MIRInstruction>(MIROpcode::Br);
	brInst->operands.push_back(MIRValue::createBlockRef(succ));
	splitBlock->instructions.push_back(std::move(brInst));

	// Update predecessor's terminator to branch to splitBlock instead of succ
	if (!pred->instructions.empty()) {
		auto &terminator = pred->instructions.back();
		for (auto &operand : terminator->operands) {
			if (operand->kind == MIRValueKind::BasicBlockRef &&
				operand->blockRef == succ) {
				operand->blockRef = splitPtr;
			}
		}
	}

	// Update CFG: pred -> split -> succ
	// Remove succ from pred's successors, add splitBlock
	pred->successors.erase(
		std::remove(pred->successors.begin(), pred->successors.end(), succ),
		pred->successors.end());
	pred->successors.push_back(splitPtr);

	// Remove pred from succ's predecessors, add splitBlock
	succ->predecessors.erase(
		std::remove(succ->predecessors.begin(), succ->predecessors.end(), pred),
		succ->predecessors.end());
	succ->predecessors.push_back(splitPtr);

	// Set up split block's CFG
	splitPtr->predecessors.push_back(pred);
	splitPtr->successors.push_back(succ);

	// Add split block to function (insert after pred for better ordering)
	auto it = std::find_if(func.basicBlocks.begin(), func.basicBlocks.end(),
						   [pred](const std::unique_ptr<MIRBasicBlock> &b) { return b.get() == pred; });
	if (it != func.basicBlocks.end()) {
		++it; // Insert after pred
	}
	func.basicBlocks.insert(it, std::move(splitBlock));

	return splitPtr;
}

void PhiEliminationPass::insertCopyBeforeTerminator(MIRBasicBlock *block,
													MIRValue *dest,
													MIRValue *src) {
	// Create a copy instruction
	auto copyInst = std::make_unique<MIRInstruction>(MIROpcode::Copy);
	copyInst->result = dest;
	copyInst->operands.push_back(src);

	// Find the terminator instruction (should be at the end)
	auto &instructions = block->instructions;
	if (instructions.empty()) {
		instructions.push_back(std::move(copyInst));
		return;
	}

	// Insert before the last instruction (assumed to be terminator)
	auto insertPos = instructions.end();
	--insertPos; // Point to the terminator

	// Verify the last instruction is actually a terminator
	if ((*insertPos)->isTerminator()) {
		instructions.insert(insertPos, std::move(copyInst));
	} else {
		// No terminator? Just append
		instructions.push_back(std::move(copyInst));
	}
}

//==============================================================================
// MIR Optimizer (Pass Manager) Implementation
//==============================================================================

void MIROptimizer::addPass(std::unique_ptr<OptimizationPass> pass) {
	pass->setVerboseLevel(verboseLevel_);
	passes.push_back(std::move(pass));
}

int MIROptimizer::runAllPasses(MIRModule &module) {
	int totalChanges = 0;
	bool anyChange;
	int iteration = 0;

	if (verboseLevel_ >= 1) {
		std::cout << "[Opt] Starting optimization passes (" << passes.size() << " passes)" << std::endl;
	}

	do {
		anyChange = false;
		iteration++;
		if (verboseLevel_ >= 2) {
			std::cout << "[Opt] Iteration " << iteration << std::endl;
		}
		for (auto &pass : passes) {
			if (pass->run(module)) {
				anyChange = true;
				totalChanges++;
				if (verboseLevel_ >= 2) {
					std::cout << "[Opt]   Pass '" << pass->getName() << "' made changes" << std::endl;
				}
			}
		}
	} while (anyChange);

	if (verboseLevel_ >= 1) {
		std::cout << "[Opt] Optimization complete after " << iteration << " iteration(s), "
				  << totalChanges << " total changes" << std::endl;
	}

	return totalChanges;
}

void MIROptimizer::runPasses(MIRModule &module, int maxIterations) {
	if (verboseLevel_ >= 1) {
		std::cout << "[Opt] Running passes (max " << maxIterations << " iterations)" << std::endl;
	}

	for (int i = 0; i < maxIterations; ++i) {
		bool anyChange = false;
		for (auto &pass : passes) {
			if (pass->run(module)) {
				anyChange = true;
				if (verboseLevel_ >= 2) {
					std::cout << "[Opt]   Pass '" << pass->getName() << "' made changes" << std::endl;
				}
			}
		}
		if (!anyChange) {
			if (verboseLevel_ >= 1) {
				std::cout << "[Opt] Converged after " << (i + 1) << " iteration(s)" << std::endl;
			}
			break;
		}
	}
}

void MIROptimizer::clearPasses() {
	passes.clear();
}

MIROptimizer MIROptimizer::createStandardPipeline(int verboseLevel) {
	MIROptimizer optimizer;
	optimizer.setVerboseLevel(verboseLevel);

	// Order matters: run simpler passes first, then more complex ones
	optimizer.addPass(std::make_unique<ConstantFoldingPass>());
	optimizer.addPass(std::make_unique<ConstantPropagationPass>());
	optimizer.addPass(std::make_unique<AlgebraicSimplificationPass>());
	optimizer.addPass(std::make_unique<StrengthReductionPass>());
	optimizer.addPass(std::make_unique<CopyPropagationPass>());
	optimizer.addPass(std::make_unique<RedundantLoadStoreEliminationPass>());
	optimizer.addPass(std::make_unique<DeadCodeEliminationPass>());
	optimizer.addPass(std::make_unique<UnreachableBlockEliminationPass>());
	optimizer.addPass(std::make_unique<SimpleFunctionInliningPass>());

	return optimizer;
}

} // namespace mir
