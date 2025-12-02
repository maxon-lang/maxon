#include "optimizer.h"
#include <algorithm>
#include <map>

namespace mir {

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

	// Collect all PHI nodes grouped by their block
	// PHIs in the same block must be processed together to handle the "lost copy" problem
	std::unordered_map<MIRBasicBlock *, std::vector<MIRInstruction *>> blockToPhis;

	for (auto &block : func.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Phi) {
				blockToPhis[block.get()].push_back(inst.get());
			}
		}
	}

	if (blockToPhis.empty()) {
		return false;
	}

	// Count total PHIs for stats
	size_t totalPhis = 0;
	for (auto &pair : blockToPhis) {
		totalPhis += pair.second.size();
	}
	lastRunStats_ += static_cast<int>(totalPhis);

	logDetail("Processing " + std::to_string(totalPhis) + " PHI node(s) in " + func.name);

	// Map to track split blocks: (pred, succ) -> splitBlock
	std::map<std::pair<MIRBasicBlock *, MIRBasicBlock *>, MIRBasicBlock *> splitBlocks;

	// Build a set of all PHI result register IDs in each block for quick lookup
	std::unordered_map<MIRBasicBlock *, std::unordered_set<uint32_t>> blockPhiResults;
	for (auto &pair : blockToPhis) {
		for (auto *phi : pair.second) {
			if (phi->result && phi->result->kind == MIRValueKind::VirtualReg) {
				blockPhiResults[pair.first].insert(phi->result->regId);
			}
		}
	}

	// Process PHIs block by block
	for (auto &pair : blockToPhis) {
		MIRBasicBlock *phiBlock = pair.first;
		std::vector<MIRInstruction *> &phis = pair.second;

		// For each predecessor, we need to insert copies for all PHIs
		// Group by predecessor to handle the lost copy problem
		std::unordered_map<MIRBasicBlock *, std::vector<std::pair<MIRValue *, MIRValue *>>> predCopies;

		for (auto *phi : phis) {
			for (auto &incoming : phi->phiIncoming) {
				MIRValue *src = incoming.first;
				MIRValue *dest = phi->result;
				MIRBasicBlock *pred = incoming.second;
				predCopies[pred].push_back({dest, src});
			}
		}

		// For each predecessor, insert the copies with proper handling for inter-PHI dependencies
		for (auto &predPair : predCopies) {
			MIRBasicBlock *predBlock = predPair.first;
			std::vector<std::pair<MIRValue *, MIRValue *>> &copies = predPair.second;

			// Determine the insert block (split critical edges if needed)
			MIRBasicBlock *insertBlock = predBlock;
			if (isCriticalEdge(predBlock, phiBlock)) {
				auto key = std::make_pair(predBlock, phiBlock);
				auto it = splitBlocks.find(key);
				if (it != splitBlocks.end()) {
					insertBlock = it->second;
				} else {
					insertBlock = splitCriticalEdge(func, predBlock, phiBlock);
					splitBlocks[key] = insertBlock;
				}
			}

			// Check for the "lost copy" problem: does any copy's source refer to another
			// copy's destination (which is a PHI result in the same block)?
			// If so, we need to use temporaries for those sources.
			std::unordered_set<uint32_t> destRegs;
			for (auto &copy : copies) {
				if (copy.first && copy.first->kind == MIRValueKind::VirtualReg) {
					destRegs.insert(copy.first->regId);
				}
			}

			// Find copies whose source is another PHI's result (potential lost copy)
			std::unordered_map<uint32_t, MIRValue *> tempForReg;
			for (auto &copy : copies) {
				if (copy.second && copy.second->kind == MIRValueKind::VirtualReg) {
					// Source is a register - check if it's a PHI result that will be overwritten
					if (destRegs.count(copy.second->regId) > 0) {
						// This source will be overwritten by another copy!
						// We need to save it to a temporary first
						if (tempForReg.find(copy.second->regId) == tempForReg.end()) {
							// Create a temporary register for this value
							MIRValue *temp = func.createVirtualReg(copy.second->type);
							tempForReg[copy.second->regId] = temp;

							// Insert copy from original to temp (before terminator)
							insertCopyBeforeTerminator(insertBlock, temp, copy.second);
						}
					}
				}
			}

			// Now insert the actual PHI copies, using temporaries where needed
			for (auto &copy : copies) {
				MIRValue *src = copy.second;
				// If this source was saved to a temp, use the temp instead
				if (src && src->kind == MIRValueKind::VirtualReg) {
					auto tempIt = tempForReg.find(src->regId);
					if (tempIt != tempForReg.end()) {
						src = tempIt->second;
					}
				}
				insertCopyBeforeTerminator(insertBlock, copy.first, src);
			}

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

} // namespace mir
