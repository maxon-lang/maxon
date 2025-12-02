#include "optimizer.h"
#include <algorithm>
#include <functional>
#include <queue>

namespace mir {

//==============================================================================
// Mem2Reg Pass Implementation
//==============================================================================

bool Mem2RegPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool Mem2RegPass::runOnFunction(MIRFunction &func) {
	bool changed = false;

	// Collect promotable allocas
	std::vector<MIRInstruction *> promotableAllocas;
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Alloca && isAllocaPromotable(func, inst.get())) {
				promotableAllocas.push_back(inst.get());
			}
		}
	}

	if (verboseLevel_ >= 2) {
		std::cout << "[Mem2Reg] Found " << promotableAllocas.size()
				  << " promotable allocas in function " << func.name << std::endl;
	}

	// Promote each alloca
	for (auto *alloca : promotableAllocas) {
		if (promoteAlloca(func, alloca)) {
			lastRunStats_++;
			changed = true;
		}
	}

	return changed;
}

bool Mem2RegPass::isAllocaPromotable(MIRFunction &func, MIRInstruction *alloca) {
	// Must be an alloca of a scalar type (not array or struct)
	if (alloca->allocatedType->kind == MIRTypeKind::Array ||
		alloca->allocatedType->kind == MIRTypeKind::Struct) {
		return false;
	}

	// Check all uses: must only be loads and stores (no address taken)
	MIRValue *allocaVal = alloca->result;
	if (!allocaVal)
		return false;

	// Scan all blocks in this function for uses of this alloca
	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			// Check if this instruction uses the alloca
			for (size_t i = 0; i < inst->operands.size(); ++i) {
				if (inst->operands[i] == allocaVal) {
					// Only allow Load and Store
					if (inst->opcode == MIROpcode::Load) {
						// Load from alloca is OK
						continue;
					} else if (inst->opcode == MIROpcode::Store && i == 1) {
						// Store to alloca (operand 1 is destination) is OK
						continue;
					} else {
						// Address is taken or used in unexpected way
						return false;
					}
				}
			}
		}
	}

	return true;
}

bool Mem2RegPass::promoteAlloca(MIRFunction &func, MIRInstruction *alloca) {
	MIRValue *allocaPtr = alloca->result;

	// Find all blocks that store to this alloca (definition blocks)
	std::set<MIRBasicBlock *> defBlocks;

	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Store &&
				inst->operands.size() >= 2 &&
				inst->operands[1] == allocaPtr) {
				defBlocks.insert(block.get());
			}
		}
	}

	// Compute dominance info once for this alloca
	DominanceInfo domInfo(func);

	// Compute iterated dominance frontier for PHI placement
	std::set<MIRBasicBlock *> phiBlocks;
	std::set<MIRBasicBlock *> processed;
	std::queue<MIRBasicBlock *> workList;

	// Initialize worklist with def blocks
	for (auto *block : defBlocks) {
		workList.push(block);
	}

	// Compute blocks needing PHI nodes using iterated dominance frontiers
	while (!workList.empty()) {
		auto *block = workList.front();
		workList.pop();

		for (auto *dfBlock : domInfo.getDominanceFrontier(block)) {
			if (phiBlocks.find(dfBlock) == phiBlocks.end()) {
				phiBlocks.insert(dfBlock);
				// PHI node is also a definition, so add to worklist if not processed
				if (processed.find(dfBlock) == processed.end()) {
					processed.insert(dfBlock);
					workList.push(dfBlock);
				}
			}
		}
	}

	// Map from block to the PHI node we inserted for this alloca
	std::unordered_map<MIRBasicBlock *, MIRInstruction *> blockToPhiMap;

	// Sort phiBlocks by block ID for deterministic PHI insertion order
	std::vector<MIRBasicBlock *> sortedPhiBlocks(phiBlocks.begin(), phiBlocks.end());
	std::sort(sortedPhiBlocks.begin(), sortedPhiBlocks.end(),
			  [](MIRBasicBlock *a, MIRBasicBlock *b) { return a->id < b->id; });

	// Insert PHI nodes at the computed blocks
	for (auto *block : sortedPhiBlocks) {
		auto phi = std::make_unique<MIRInstruction>(MIROpcode::Phi);
		phi->result = func.createVirtualReg(alloca->allocatedType);

		// Add incoming value slots for all predecessors (will be filled during renaming)
		for (auto *pred : block->predecessors) {
			phi->phiIncoming.push_back({nullptr, pred});
		}

		MIRInstruction *phiPtr = phi.get();
		block->instructions.insert(block->instructions.begin(), std::move(phi));
		blockToPhiMap[block] = phiPtr;
	}

	// SSA Renaming using dominator tree traversal with a definition stack
	// Stack of reaching definitions - push when we see a def, pop when leaving scope
	std::vector<MIRValue *> defStack;

	// Create an "undef" value (zero) for uses before any definition
	MIRValue *undefValue = MIRValue::createConstantInt(alloca->allocatedType, 0);

	// Map to collect load replacements (defer replacements to avoid iterator invalidation)
	std::unordered_map<MIRValue *, MIRValue *> loadReplacements;

	// Recursive function to rename variables in dominator tree order
	std::function<void(MIRBasicBlock *)> renameBlock = [&](MIRBasicBlock *block) {
		// Track how many definitions we push in this block (to pop when leaving)
		size_t defsBeforeBlock = defStack.size();

		// Process instructions in order
		for (auto &inst : block->instructions) {
			// PHI nodes we inserted define a new value
			if (inst->opcode == MIROpcode::Phi) {
				auto it = blockToPhiMap.find(block);
				if (it != blockToPhiMap.end() && it->second == inst.get()) {
					// This is our PHI - it defines the current value
					defStack.push_back(inst->result);
				}
			}
			// Load: replace with current reaching definition
			else if (inst->opcode == MIROpcode::Load &&
					 inst->operands.size() >= 1 &&
					 inst->operands[0] == allocaPtr) {
				MIRValue *reachingDef = defStack.empty() ? undefValue : defStack.back();
				loadReplacements[inst->result] = reachingDef;
			}
			// Store: push new definition
			else if (inst->opcode == MIROpcode::Store &&
					 inst->operands.size() >= 2 &&
					 inst->operands[1] == allocaPtr) {
				defStack.push_back(inst->operands[0]);
			}
		}

		// Fill in PHI incoming values for successor blocks
		MIRValue *currentDef = defStack.empty() ? undefValue : defStack.back();
		for (auto *succ : block->successors) {
			auto phiIt = blockToPhiMap.find(succ);
			if (phiIt != blockToPhiMap.end()) {
				MIRInstruction *phi = phiIt->second;
				// Find the incoming slot for this predecessor
				for (auto &incoming : phi->phiIncoming) {
					if (incoming.second == block) {
						incoming.first = currentDef;
						break;
					}
				}
			}
		}

		// Recurse into dominated children (in dominator tree order)
		for (auto *child : domInfo.getDominatedBlocks(block)) {
			renameBlock(child);
		}

		// Pop definitions added in this block
		while (defStack.size() > defsBeforeBlock) {
			defStack.pop_back();
		}
	};

	// Start renaming from the entry block
	if (!func.basicBlocks.empty()) {
		renameBlock(func.basicBlocks[0].get());
	}

	// Apply all load replacements
	for (auto &pair : loadReplacements) {
		opt_utils::replaceAllUsesWith(func, pair.first, pair.second);
	}

	// Remove all stores to this alloca
	for (auto &block : func.basicBlocks) {
		auto &insts = block->instructions;
		insts.erase(
			std::remove_if(insts.begin(), insts.end(),
						   [allocaPtr](const auto &inst) {
							   return inst->opcode == MIROpcode::Store &&
									  inst->operands.size() >= 2 &&
									  inst->operands[1] == allocaPtr;
						   }),
			insts.end());
	}

	// Remove all loads from this alloca
	for (auto &block : func.basicBlocks) {
		auto &insts = block->instructions;
		insts.erase(
			std::remove_if(insts.begin(), insts.end(),
						   [allocaPtr](const auto &inst) {
							   return inst->opcode == MIROpcode::Load &&
									  inst->operands.size() >= 1 &&
									  inst->operands[0] == allocaPtr;
						   }),
			insts.end());
	}

	// Remove the alloca itself
	for (auto &block : func.basicBlocks) {
		auto &insts = block->instructions;
		insts.erase(
			std::remove_if(insts.begin(), insts.end(),
						   [alloca](const auto &p) { return p.get() == alloca; }),
			insts.end());
	}

	return true;
}

} // namespace mir
