#include "optimizer.h"
#include <algorithm>
#include <queue>

namespace mir {

//==============================================================================
// Dominance Analysis Implementation
//==============================================================================

DominanceInfo::DominanceInfo(MIRFunction &func) {
	computeReachableBlocks(func);
	computeDominators(func);
	computeDominanceFrontiers(func);
}

void DominanceInfo::computeReachableBlocks(MIRFunction &func) {
	if (func.basicBlocks.empty())
		return;

	// BFS from entry to find all reachable blocks
	std::queue<MIRBasicBlock *> worklist;
	MIRBasicBlock *entry = func.basicBlocks[0].get();
	worklist.push(entry);
	reachable_.insert(entry);

	while (!worklist.empty()) {
		MIRBasicBlock *block = worklist.front();
		worklist.pop();

		for (MIRBasicBlock *succ : block->successors) {
			if (reachable_.find(succ) == reachable_.end()) {
				reachable_.insert(succ);
				worklist.push(succ);
			}
		}
	}
}

void DominanceInfo::computeDominators(MIRFunction &func) {
	if (func.basicBlocks.empty())
		return;

	MIRBasicBlock *entry = func.basicBlocks[0].get();

	// Initialize: entry dominates itself, all others dominated by all blocks
	std::unordered_map<MIRBasicBlock *, std::set<MIRBasicBlock *>> dom;
	dom[entry].insert(entry);

	for (auto &block : func.basicBlocks) {
		if (block.get() != entry) {
			// Initially, assume dominated by all blocks
			for (auto &b : func.basicBlocks) {
				dom[block.get()].insert(b.get());
			}
		}
	}

	// Iterative dataflow analysis - only process reachable blocks
	bool changed = true;
	while (changed) {
		changed = false;
		for (auto &block : func.basicBlocks) {
			// Skip unreachable blocks
			if (reachable_.find(block.get()) == reachable_.end())
				continue;
			if (block.get() == entry)
				continue;

			// Collect reachable predecessors only
			std::vector<MIRBasicBlock *> reachablePreds;
			for (auto *pred : block->predecessors) {
				if (reachable_.find(pred) != reachable_.end()) {
					reachablePreds.push_back(pred);
				}
			}

			// dom(n) = {n} U (intersection of dom(p) for all reachable predecessors p)
			std::set<MIRBasicBlock *> newDom;
			newDom.insert(block.get());

			if (!reachablePreds.empty()) {
				// Start with first reachable predecessor's dominators
				newDom.insert(dom[reachablePreds[0]].begin(),
							  dom[reachablePreds[0]].end());

				// Intersect with other reachable predecessors
				for (size_t i = 1; i < reachablePreds.size(); ++i) {
					std::set<MIRBasicBlock *> intersection;
					std::set_intersection(newDom.begin(), newDom.end(),
										  dom[reachablePreds[i]].begin(),
										  dom[reachablePreds[i]].end(),
										  std::inserter(intersection, intersection.begin()));
					newDom = intersection;
					newDom.insert(block.get());
				}
			}

			if (newDom != dom[block.get()]) {
				dom[block.get()] = newDom;
				changed = true;
			}
		}
	}

	dominators_ = dom;

	// Compute immediate dominators - only for reachable blocks
	for (auto &block : func.basicBlocks) {
		// Skip unreachable blocks
		if (reachable_.find(block.get()) == reachable_.end())
			continue;

		if (block.get() == entry) {
			idom_[entry] = nullptr;
			continue;
		}

		// idom is the unique block that dominates this block and is dominated by all other dominators
		std::set<MIRBasicBlock *> strictDom = dom[block.get()];
		strictDom.erase(block.get()); // Remove self

		for (auto *candidate : strictDom) {
			bool isIDom = true;
			for (auto *other : strictDom) {
				if (candidate != other && dominators_[candidate].find(other) == dominators_[candidate].end()) {
					isIDom = false;
					break;
				}
			}
			if (isIDom) {
				idom_[block.get()] = candidate;
				domTree_[candidate].push_back(block.get());
				break;
			}
		}
	}

	// Sort dominator tree children by block ID for deterministic traversal order
	// This ensures SSA renaming produces consistent virtual register numbering
	for (auto &entry : domTree_) {
		std::sort(entry.second.begin(), entry.second.end(),
				  [](MIRBasicBlock *a, MIRBasicBlock *b) { return a->id < b->id; });
	}
}

void DominanceInfo::computeDominanceFrontiers(MIRFunction &func) {
	// DF(X) = {Y | X dominates a predecessor of Y but doesn't strictly dominate Y}
	// Only process reachable blocks and reachable predecessors
	for (auto &block : func.basicBlocks) {
		// Skip unreachable blocks
		if (reachable_.find(block.get()) == reachable_.end())
			continue;

		// Collect reachable predecessors
		std::vector<MIRBasicBlock *> reachablePreds;
		for (auto *pred : block->predecessors) {
			if (reachable_.find(pred) != reachable_.end()) {
				reachablePreds.push_back(pred);
			}
		}

		if (reachablePreds.size() >= 2) {
			for (auto *pred : reachablePreds) {
				MIRBasicBlock *runner = pred;
				while (runner && runner != idom_[block.get()]) {
					domFrontier_[runner].insert(block.get());
					runner = idom_[runner];
				}
			}
		}
	}
}

bool DominanceInfo::dominates(MIRBasicBlock *a, MIRBasicBlock *b) const {
	auto it = dominators_.find(b);
	if (it == dominators_.end())
		return false;
	return it->second.find(a) != it->second.end();
}

MIRBasicBlock *DominanceInfo::getIDom(MIRBasicBlock *block) const {
	auto it = idom_.find(block);
	return it != idom_.end() ? it->second : nullptr;
}

const std::set<MIRBasicBlock *> &DominanceInfo::getDominanceFrontier(MIRBasicBlock *block) const {
	static std::set<MIRBasicBlock *> empty;
	auto it = domFrontier_.find(block);
	return it != domFrontier_.end() ? it->second : empty;
}

const std::vector<MIRBasicBlock *> &DominanceInfo::getDominatedBlocks(MIRBasicBlock *block) const {
	static std::vector<MIRBasicBlock *> empty;
	auto it = domTree_.find(block);
	return it != domTree_.end() ? it->second : empty;
}

} // namespace mir
