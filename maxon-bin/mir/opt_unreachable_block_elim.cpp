#include "optimizer.h"
#include <algorithm>
#include <queue>

namespace mir {

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

	// Track blocks removed
	lastRunStats_ += static_cast<int>(originalSize - blocks.size());

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

} // namespace mir
