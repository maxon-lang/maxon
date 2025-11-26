// ssa_verifier.cpp - MIR SSA Verification Pass
// Validates MIR modules for correctness, catching common errors in hand-written .mir files.

#include "ssa_verifier.h"
#include <algorithm>
#include <functional>
#include <queue>
#include <set>
#include <sstream>
#include <unordered_set>

namespace mir {

//==============================================================================
// MIRVerifyError
//==============================================================================

std::string MIRVerifyError::toString() const {
	std::ostringstream ss;
	ss << "MIR verification error";
	if (!functionName.empty()) {
		ss << " in function @" << functionName;
	}
	if (!blockName.empty()) {
		ss << " in block '" << blockName << "'";
	}
	if (instructionIndex >= 0) {
		ss << " at instruction " << instructionIndex;
	}
	ss << ": " << message;
	return ss.str();
}

//==============================================================================
// MIRVerifier - Static Entry Points
//==============================================================================

MIRVerifyResult MIRVerifier::verify(const MIRModule &module) {
	MIRVerifyResult result;

	for (const auto &func : module.functions) {
		if (func->isExternal) {
			// Skip external declarations (no body to verify)
			continue;
		}

		MIRVerifyResult funcResult = verifyFunction(*func);
		result.errors.insert(result.errors.end(),
							 funcResult.errors.begin(),
							 funcResult.errors.end());
	}

	return result;
}

MIRVerifyResult MIRVerifier::verifyFunction(const MIRFunction &func) {
	MIRVerifier verifier(func);
	verifier.run();

	MIRVerifyResult result;
	result.errors = std::move(verifier.errors);
	return result;
}

//==============================================================================
// MIRVerifier - Constructor and Main Loop
//==============================================================================

MIRVerifier::MIRVerifier(const MIRFunction &func) : function(func) {}

void MIRVerifier::run() {
	if (function.basicBlocks.empty()) {
		addError("function has no basic blocks");
		return;
	}

	// Build CFG (predecessors/successors) - parser may not have populated them
	buildCFG();

	// Build value definition map
	// Parameters are defined at function entry
	for (auto *param : function.parameters) {
		valueDefBlock[param] = function.basicBlocks[0].get();
	}

	// Instructions define their results
	for (const auto &block : function.basicBlocks) {
		for (const auto &inst : block->instructions) {
			if (inst->result) {
				valueDefBlock[inst->result] = block.get();
			}
		}
	}

	// Compute dominators (needed for SSA verification)
	computeDominators();

	// Run all verification passes
	verifyCFG();
	verifyTerminators();
	verifyPhiNodes();
	verifySSAForm();
	verifyTypes();
}

//==============================================================================
// CFG Construction
//==============================================================================

void MIRVerifier::buildCFG() {
	// Build a block name -> block pointer map for fast lookup
	std::unordered_map<std::string, const MIRBasicBlock *> blockByName;
	for (const auto &block : function.basicBlocks) {
		blockByName[block->name] = block.get();
	}

	// Scan all terminators to build successors, then invert for predecessors
	for (const auto &block : function.basicBlocks) {
		cfgSuccessors[block.get()] = {};
		cfgPredecessors[block.get()] = {};
	}

	for (const auto &block : function.basicBlocks) {
		if (block->instructions.empty())
			continue;

		const auto &terminator = block->instructions.back();

		switch (terminator->opcode) {
		case MIROpcode::Br:
			// Unconditional branch: operand[0] is block ref
			if (!terminator->operands.empty() && terminator->operands[0]->blockRef) {
				const MIRBasicBlock *target = terminator->operands[0]->blockRef;
				cfgSuccessors[block.get()].push_back(target);
				cfgPredecessors[target].push_back(block.get());
			}
			break;

		case MIROpcode::CondBr:
			// Conditional branch: operand[1] is true block, operand[2] is false block
			if (terminator->operands.size() >= 3) {
				if (terminator->operands[1] && terminator->operands[1]->blockRef) {
					const MIRBasicBlock *trueTarget = terminator->operands[1]->blockRef;
					cfgSuccessors[block.get()].push_back(trueTarget);
					cfgPredecessors[trueTarget].push_back(block.get());
				}
				if (terminator->operands[2] && terminator->operands[2]->blockRef) {
					const MIRBasicBlock *falseTarget = terminator->operands[2]->blockRef;
					cfgSuccessors[block.get()].push_back(falseTarget);
					cfgPredecessors[falseTarget].push_back(block.get());
				}
			}
			break;

		case MIROpcode::Ret:
		case MIROpcode::RetVoid:
			// No successors
			break;

		default:
			// Non-terminator in last position will be caught by terminator verification
			break;
		}
	}
}

//==============================================================================
// Dominator Computation
//==============================================================================

// Dominator computation using iterative dataflow with reverse post-order traversal.
// For each block, compute its immediate dominator (idom).
// Block A dominates block B if every path from entry to B goes through A.

void MIRVerifier::computeDominators() {
	if (function.basicBlocks.empty())
		return;

	const MIRBasicBlock *entry = function.basicBlocks[0].get();

	// Build a reverse post-order (RPO) of the blocks
	// This ensures we process dominators before their dominated blocks
	std::vector<const MIRBasicBlock *> rpo;
	std::unordered_set<const MIRBasicBlock *> visited;
	std::function<void(const MIRBasicBlock *)> dfs = [&](const MIRBasicBlock *block) {
		if (visited.count(block))
			return;
		visited.insert(block);

		auto succIt = cfgSuccessors.find(block);
		if (succIt != cfgSuccessors.end()) {
			for (const auto *succ : succIt->second) {
				dfs(succ);
			}
		}
		rpo.push_back(block);
	};
	dfs(entry);
	std::reverse(rpo.begin(), rpo.end());

	// Create block to RPO index map for the intersect algorithm
	std::unordered_map<const MIRBasicBlock *, int> rpoIndex;
	for (size_t i = 0; i < rpo.size(); i++) {
		rpoIndex[rpo[i]] = static_cast<int>(i);
	}

	// Initialize: entry dominates itself, all others undefined
	idom.clear();
	idom[entry] = entry;

	// Iteratively compute dominators using Cooper-Harvey-Kennedy algorithm
	bool changed = true;
	while (changed) {
		changed = false;

		// Process in RPO (skip entry at index 0)
		for (size_t i = 1; i < rpo.size(); i++) {
			const MIRBasicBlock *block = rpo[i];

			auto predIt = cfgPredecessors.find(block);
			if (predIt == cfgPredecessors.end() || predIt->second.empty()) {
				continue;
			}

			// Find a processed predecessor (one with idom defined)
			const MIRBasicBlock *newIdom = nullptr;
			for (const auto *pred : predIt->second) {
				if (idom.count(pred)) {
					newIdom = pred;
					break;
				}
			}

			if (!newIdom)
				continue;

			// Intersect with other predecessors
			for (const auto *pred : predIt->second) {
				if (pred == newIdom)
					continue;
				if (idom.count(pred)) {
					newIdom = intersect(newIdom, pred, rpoIndex);
				}
			}

			if (idom[block] != newIdom) {
				idom[block] = newIdom;
				changed = true;
			}
		}
	}
}

// Cooper-Harvey-Kennedy intersect algorithm
const MIRBasicBlock *MIRVerifier::intersect(const MIRBasicBlock *b1,
											const MIRBasicBlock *b2,
											const std::unordered_map<const MIRBasicBlock *, int> &rpoIndex) const {
	const MIRBasicBlock *finger1 = b1;
	const MIRBasicBlock *finger2 = b2;

	while (finger1 != finger2) {
		auto it1 = rpoIndex.find(finger1);
		auto it2 = rpoIndex.find(finger2);
		if (it1 == rpoIndex.end() || it2 == rpoIndex.end())
			break;

		while (it1->second > it2->second) {
			auto idomIt = idom.find(finger1);
			if (idomIt == idom.end() || idomIt->second == nullptr)
				return finger1;
			finger1 = idomIt->second;
			it1 = rpoIndex.find(finger1);
			if (it1 == rpoIndex.end())
				break;
		}
		while (it2->second > it1->second) {
			auto idomIt = idom.find(finger2);
			if (idomIt == idom.end() || idomIt->second == nullptr)
				return finger2;
			finger2 = idomIt->second;
			it2 = rpoIndex.find(finger2);
			if (it2 == rpoIndex.end())
				break;
		}
	}
	return finger1;
}

// Helper: find nearest common dominator of two blocks (older version, kept for reference)
const MIRBasicBlock *MIRVerifier::intersectDom(const MIRBasicBlock *a,
											   const MIRBasicBlock *b) const {
	// Collect all dominators of 'a' (path to entry)
	std::unordered_set<const MIRBasicBlock *> aDoms;
	const MIRBasicBlock *cur = a;
	while (cur) {
		aDoms.insert(cur);
		auto it = idom.find(cur);
		cur = (it != idom.end()) ? it->second : nullptr;
	}

	// Walk up from 'b' until we find a block in aDoms
	cur = b;
	while (cur) {
		if (aDoms.count(cur)) {
			return cur;
		}
		auto it = idom.find(cur);
		cur = (it != idom.end()) ? it->second : nullptr;
	}

	// Should not happen in connected graph
	return function.basicBlocks[0].get();
}

bool MIRVerifier::dominates(const MIRBasicBlock *defBlock,
							const MIRBasicBlock *useBlock) const {
	// A block dominates itself
	if (defBlock == useBlock) {
		return true;
	}
	return strictlyDominates(defBlock, useBlock);
}

bool MIRVerifier::strictlyDominates(const MIRBasicBlock *defBlock,
									const MIRBasicBlock *useBlock) const {
	// Walk up the dominator tree from useBlock to see if we hit defBlock
	const MIRBasicBlock *cur = useBlock;
	while (cur) {
		auto it = idom.find(cur);
		if (it == idom.end()) {
			break;
		}
		// Entry's idom is itself - stop at entry
		if (it->second == cur) {
			break;
		}
		cur = it->second;
		if (cur == defBlock) {
			return true;
		}
	}
	return false;
}

//==============================================================================
// SSA Form Verification
//==============================================================================

void MIRVerifier::verifySSAForm() {
	for (const auto &block : function.basicBlocks) {
		int instIndex = 0;
		for (const auto &inst : block->instructions) {
			// Check each operand is properly dominated
			for (auto *operand : inst->operands) {
				verifyValueUse(operand, block.get(), instIndex);
			}

			// Check PHI incoming values (handled specially - must be dominated
			// at the END of the predecessor block, not the use site)
			if (inst->opcode == MIROpcode::Phi) {
				for (const auto &[value, predBlock] : inst->phiIncoming) {
					if (!value)
						continue;

					// The value must be available at the end of predBlock
					// This means it must be defined in a block that dominates predBlock,
					// OR it must be defined in predBlock itself
					auto it = valueDefBlock.find(value);
					if (it == valueDefBlock.end()) {
						// Constants and globals don't have a defining block
						if (value->kind != MIRValueKind::ConstantInt &&
							value->kind != MIRValueKind::ConstantFloat &&
							value->kind != MIRValueKind::ConstantNull &&
							value->kind != MIRValueKind::Global) {
							addError(block->name, instIndex,
									 "PHI operand %" + std::to_string(value->regId) +
										 " has no definition");
						}
						continue;
					}

					const MIRBasicBlock *defBlock = it->second;
					if (!dominates(defBlock, predBlock)) {
						addError(block->name, instIndex,
								 "PHI operand %" + std::to_string(value->regId) +
									 " from block '" + predBlock->name +
									 "' is not dominated by its definition in block '" +
									 defBlock->name + "'");
					}
				}
			}

			instIndex++;
		}
	}
}

void MIRVerifier::verifyValueUse(MIRValue *value, MIRBasicBlock *useBlock, int instIndex) {
	if (!value)
		return;

	// Constants and globals are always valid
	if (value->kind == MIRValueKind::ConstantInt ||
		value->kind == MIRValueKind::ConstantFloat ||
		value->kind == MIRValueKind::ConstantNull ||
		value->kind == MIRValueKind::Global ||
		value->kind == MIRValueKind::BasicBlockRef) {
		return;
	}

	// Parameters are defined at entry
	if (value->kind == MIRValueKind::Parameter) {
		return;
	}

	// Find the defining block
	auto it = valueDefBlock.find(value);
	if (it == valueDefBlock.end()) {
		addError(useBlock->name, instIndex,
				 "use of undefined value %" + std::to_string(value->regId));
		return;
	}

	const MIRBasicBlock *defBlock = it->second;

	// For uses within the same block, the definition must come before the use
	if (defBlock == useBlock) {
		// Find definition index
		int defIndex = -1;
		for (size_t i = 0; i < useBlock->instructions.size(); i++) {
			if (useBlock->instructions[i]->result == value) {
				defIndex = static_cast<int>(i);
				break;
			}
		}

		if (defIndex >= instIndex) {
			addError(useBlock->name, instIndex,
					 "use of %" + std::to_string(value->regId) +
						 " before its definition at instruction " + std::to_string(defIndex));
		}
		return;
	}

	// For uses in different blocks, definition must dominate use
	if (!dominates(defBlock, useBlock)) {
		addError(useBlock->name, instIndex,
				 "use of %" + std::to_string(value->regId) +
					 " defined in block '" + defBlock->name +
					 "' which does not dominate this block");
	}
}

//==============================================================================
// PHI Node Verification
//==============================================================================

void MIRVerifier::verifyPhiNodes() {
	for (const auto &block : function.basicBlocks) {
		bool seenNonPhi = false;
		int instIndex = 0;

		for (const auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Phi) {
				if (seenNonPhi) {
					addError(block->name, instIndex,
							 "PHI node must appear at the beginning of the block, "
							 "before any non-PHI instructions");
				}
				verifyPhiNode(inst.get(), block.get(), instIndex);
			} else {
				seenNonPhi = true;
			}
			instIndex++;
		}
	}
}

void MIRVerifier::verifyPhiNode(MIRInstruction *phi, MIRBasicBlock *block, int instIndex) {
	// PHI must have at least one incoming value
	if (phi->phiIncoming.empty()) {
		addError(block->name, instIndex, "PHI node has no incoming values");
		return;
	}

	// Collect predecessor blocks from PHI
	std::set<const MIRBasicBlock *> phiPreds;
	for (const auto &[value, predBlock] : phi->phiIncoming) {
		if (!predBlock) {
			addError(block->name, instIndex, "PHI node has null predecessor block");
			continue;
		}
		if (phiPreds.count(predBlock)) {
			addError(block->name, instIndex,
					 "PHI node has duplicate entry for predecessor '" + predBlock->name + "'");
		}
		phiPreds.insert(predBlock);
	}

	// PHI must have exactly one entry for each predecessor
	// Use our computed predecessors, not block->predecessors
	auto predIt = cfgPredecessors.find(block);
	std::set<const MIRBasicBlock *> actualPreds;
	if (predIt != cfgPredecessors.end()) {
		actualPreds.insert(predIt->second.begin(), predIt->second.end());
	}

	// Check for missing predecessors
	for (const auto *pred : actualPreds) {
		if (!phiPreds.count(pred)) {
			addError(block->name, instIndex,
					 "PHI node missing entry for predecessor '" + pred->name + "'");
		}
	}

	// Check for extra predecessors (not actual predecessors)
	for (const auto *pred : phiPreds) {
		if (!actualPreds.count(pred)) {
			addError(block->name, instIndex,
					 "PHI node has entry for non-predecessor '" + pred->name + "'");
		}
	}
}

//==============================================================================
// Terminator Verification
//==============================================================================

void MIRVerifier::verifyTerminators() {
	for (const auto &block : function.basicBlocks) {
		if (block->instructions.empty()) {
			addError(block->name, "basic block has no instructions");
			continue;
		}

		// Check that the last instruction is a terminator
		const auto &lastInst = block->instructions.back();
		if (!lastInst->isTerminator()) {
			addError(block->name,
					 "basic block does not end with a terminator (ends with " +
						 std::string(MIRInstruction::opcodeToString(lastInst->opcode)) + ")");
		}

		// Check that no instruction before the last is a terminator
		for (size_t i = 0; i + 1 < block->instructions.size(); i++) {
			if (block->instructions[i]->isTerminator()) {
				addError(block->name, static_cast<int>(i),
						 "terminator instruction in middle of basic block");
			}
		}
	}

	// Entry block should not have predecessors
	if (!function.basicBlocks.empty()) {
		const auto &entryBlock = function.basicBlocks[0];
		auto predIt = cfgPredecessors.find(entryBlock.get());
		if (predIt != cfgPredecessors.end() && !predIt->second.empty()) {
			addError(entryBlock->name,
					 "entry block should not have predecessors (has " +
						 std::to_string(predIt->second.size()) + ")");
		}
	}
}

//==============================================================================
// Type Verification
//==============================================================================

void MIRVerifier::verifyTypes() {
	for (const auto &block : function.basicBlocks) {
		int instIndex = 0;
		for (const auto &inst : block->instructions) {
			verifyInstructionTypes(inst.get(), block.get(), instIndex);
			instIndex++;
		}
	}
}

void MIRVerifier::verifyInstructionTypes(MIRInstruction *inst, MIRBasicBlock *block,
										 int instIndex) {
	switch (inst->opcode) {
	// Binary arithmetic - operands must have same type
	case MIROpcode::Add:
	case MIROpcode::Sub:
	case MIROpcode::Mul:
	case MIROpcode::SDiv:
	case MIROpcode::UDiv:
	case MIROpcode::SRem:
	case MIROpcode::URem:
	case MIROpcode::And:
	case MIROpcode::Or:
	case MIROpcode::Xor:
	case MIROpcode::Shl:
	case MIROpcode::AShr:
	case MIROpcode::LShr:
		if (inst->operands.size() >= 2 && inst->operands[0] && inst->operands[1]) {
			if (inst->operands[0]->type != inst->operands[1]->type) {
				addError(block->name, instIndex,
						 "binary operator operands have different types: " +
							 inst->operands[0]->type->toString() + " vs " +
							 inst->operands[1]->type->toString());
			}
			if (!inst->operands[0]->type->isInteger()) {
				addError(block->name, instIndex,
						 "integer binary operator requires integer operands, got " +
							 inst->operands[0]->type->toString());
			}
		}
		break;

	case MIROpcode::FAdd:
	case MIROpcode::FSub:
	case MIROpcode::FMul:
	case MIROpcode::FDiv:
	case MIROpcode::FRem:
		if (inst->operands.size() >= 2 && inst->operands[0] && inst->operands[1]) {
			if (inst->operands[0]->type != inst->operands[1]->type) {
				addError(block->name, instIndex,
						 "binary operator operands have different types: " +
							 inst->operands[0]->type->toString() + " vs " +
							 inst->operands[1]->type->toString());
			}
			if (!inst->operands[0]->type->isFloat()) {
				addError(block->name, instIndex,
						 "floating-point binary operator requires float operands, got " +
							 inst->operands[0]->type->toString());
			}
		}
		break;

	// Integer comparisons
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
		if (inst->operands.size() >= 2 && inst->operands[0] && inst->operands[1]) {
			if (inst->operands[0]->type != inst->operands[1]->type) {
				addError(block->name, instIndex,
						 "comparison operands have different types: " +
							 inst->operands[0]->type->toString() + " vs " +
							 inst->operands[1]->type->toString());
			}
		}
		break;

	// Float comparisons
	case MIROpcode::FCmpEq:
	case MIROpcode::FCmpNe:
	case MIROpcode::FCmpLT:
	case MIROpcode::FCmpLE:
	case MIROpcode::FCmpGT:
	case MIROpcode::FCmpGE:
		if (inst->operands.size() >= 2 && inst->operands[0] && inst->operands[1]) {
			if (!inst->operands[0]->type->isFloat()) {
				addError(block->name, instIndex,
						 "float comparison requires float operands, got " +
							 inst->operands[0]->type->toString());
			}
		}
		break;

	// Conditional branch - condition must be i1
	case MIROpcode::CondBr:
		if (!inst->operands.empty() && inst->operands[0]) {
			if (inst->operands[0]->type->kind != MIRTypeKind::Int1) {
				addError(block->name, instIndex,
						 "conditional branch condition must be i1, got " +
							 inst->operands[0]->type->toString());
			}
		}
		break;

	// Return - must match function return type
	case MIROpcode::Ret:
		if (!inst->operands.empty() && inst->operands[0]) {
			if (inst->operands[0]->type != function.returnType) {
				addError(block->name, instIndex,
						 "return type mismatch: returning " +
							 inst->operands[0]->type->toString() + " from function returning " +
							 function.returnType->toString());
			}
		}
		break;

	case MIROpcode::RetVoid:
		if (!function.returnType->isVoid()) {
			addError(block->name, instIndex,
					 "void return in non-void function returning " +
						 function.returnType->toString());
		}
		break;

	// PHI - all incoming values must have same type as result
	case MIROpcode::Phi:
		if (inst->result) {
			for (const auto &[value, predBlock] : inst->phiIncoming) {
				if (value && value->type != inst->result->type) {
					addError(block->name, instIndex,
							 "PHI incoming value type mismatch: got " +
								 value->type->toString() + " from block '" + predBlock->name +
								 "', expected " + inst->result->type->toString());
				}
			}
		}
		break;

	default:
		// Other instructions don't need special type checking
		break;
	}
}

//==============================================================================
// CFG Verification
//==============================================================================

void MIRVerifier::verifyCFG() {
	// Verify that branch targets are valid blocks in this function
	std::unordered_set<const MIRBasicBlock *> validBlocks;
	for (const auto &block : function.basicBlocks) {
		validBlocks.insert(block.get());
	}

	for (const auto &block : function.basicBlocks) {
		auto succIt = cfgSuccessors.find(block.get());
		if (succIt == cfgSuccessors.end())
			continue;

		for (const auto *succ : succIt->second) {
			if (!validBlocks.count(succ)) {
				addError(block->name,
						 "branch target is not a valid block in this function");
			}
		}
	}
}

//==============================================================================
// Error Reporting
//==============================================================================

void MIRVerifier::addError(const std::string &msg) {
	MIRVerifyError err;
	err.functionName = function.name;
	err.message = msg;
	errors.push_back(std::move(err));
}

void MIRVerifier::addError(const std::string &blockName, const std::string &msg) {
	MIRVerifyError err;
	err.functionName = function.name;
	err.blockName = blockName;
	err.message = msg;
	errors.push_back(std::move(err));
}

void MIRVerifier::addError(const std::string &blockName, int instIndex, const std::string &msg) {
	MIRVerifyError err;
	err.functionName = function.name;
	err.blockName = blockName;
	err.instructionIndex = instIndex;
	err.message = msg;
	errors.push_back(std::move(err));
}

} // namespace mir
