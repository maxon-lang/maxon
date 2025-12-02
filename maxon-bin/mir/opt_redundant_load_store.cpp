#include "optimizer.h"
#include "memory_ssa.h"
#include <algorithm>

namespace mir {

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
	// Build MemorySSA for the function
	MemorySSA &mssa = memorySSACache_.getMemorySSA(func);

	bool changed = false;

	// Track load replacements: load instruction -> value to replace with
	std::unordered_map<MIRInstruction *, MIRValue *> loadReplacements;

	// Track dead stores: store instructions that are overwritten before being read
	std::unordered_set<MIRInstruction *> deadStores;

	// Process each block
	for (auto &block : func.basicBlocks) {
		// Track stores in this block
		// We store a list of (pointer, store instruction, stored value) tuples
		// and use mustAlias to find exact matches
		struct StoreInfo {
			MIRValue *ptr;
			MIRInstruction *inst;
			MIRValue *value;
		};
		std::vector<StoreInfo> storesInBlock;

		for (auto &inst : block->instructions) {
			if (inst->opcode == MIROpcode::Store) {
				if (inst->operands.size() >= 2) {
					auto *value = inst->operands[0];
					auto *ptr = inst->operands[1];

					// Check if there's a previous store to the EXACT same location
					for (auto &storeInfo : storesInBlock) {
						if (mssa.mustAlias(ptr, storeInfo.ptr)) {
							// Previous store to exact same location is dead
							deadStores.insert(storeInfo.inst);
							changed = true;
							// Update the existing entry instead of adding new one
							storeInfo.inst = inst.get();
							storeInfo.value = value;
							goto nextInst;
						}
					}

					// No exact match found - add new store info
					storesInBlock.push_back({ptr, inst.get(), value});
				}
			} else if (inst->opcode == MIROpcode::Load) {
				if (!inst->operands.empty()) {
					auto *ptr = inst->operands[0];
					auto *underlyingObj = mssa.getUnderlyingObject(ptr);

					// Look for a store to the EXACT same location
					StoreInfo *matchingStore = nullptr;
					for (auto &storeInfo : storesInBlock) {
						if (mssa.mustAlias(ptr, storeInfo.ptr)) {
							matchingStore = &storeInfo;
							break;
						}
					}

					if (matchingStore) {
						MIRValue *storedValue = matchingStore->value;

						// Check if the stored value is "safe" to propagate:
						// - Constants are always safe
						// - Values from the same underlying object are safe
						// - Load results from OTHER allocas may become stale
						bool safeToPropagate = storedValue->isConstant();

						if (!safeToPropagate && storedValue->definingInst) {
							// Check if this is a load from the same alloca
							if (storedValue->definingInst->opcode == MIROpcode::Load &&
								!storedValue->definingInst->operands.empty()) {
								auto *sourcePtr = storedValue->definingInst->operands[0];
								auto *sourceObj = mssa.getUnderlyingObject(sourcePtr);
								// Only safe if same underlying object (self-reference)
								safeToPropagate = (sourceObj == underlyingObj);
							} else if (storedValue->definingInst->opcode != MIROpcode::Load) {
								// Arithmetic results, etc. are safe
								safeToPropagate = true;
							}
						} else if (!storedValue->definingInst) {
							// Parameters, globals - be conservative
							safeToPropagate = (storedValue->kind == MIRValueKind::Parameter);
						}

						if (safeToPropagate) {
							loadReplacements[inst.get()] = storedValue;
							changed = true;
						}
					}

					// This load reads from a location - remove stores to locations that
					// may-alias with the load from the "dead store" candidates
					// (since their value might have been needed)
					storesInBlock.erase(
						std::remove_if(storesInBlock.begin(), storesInBlock.end(),
									   [&](const StoreInfo &si) {
										   return mssa.mayAlias(ptr, si.ptr);
									   }),
						storesInBlock.end());
				}
			} else if (inst->opcode == MIROpcode::Call || inst->opcode == MIROpcode::CallIndirect) {
				// Check if call may write memory
				bool mayWrite = true;
				if (inst->callDoesNotWriteMemory) {
					mayWrite = false;
				} else if (inst->calleeFunc && inst->calleeFunc->doesNotWriteMemory) {
					mayWrite = false;
				}

				if (mayWrite) {
					// Conservative: function call may modify any memory
					// Clear all tracked stores (they're no longer known to be dead)
					storesInBlock.clear();
				}
			}
		nextInst:;
		}
	}

	// Apply load replacements
	for (auto &[loadInst, replacement] : loadReplacements) {
		if (loadInst->result != nullptr) {
			opt_utils::replaceAllUsesWith(func, loadInst->result, replacement);
		}
	}

	// Remove dead stores
	for (auto &block : func.basicBlocks) {
		auto &instructions = block->instructions;
		instructions.erase(
			std::remove_if(instructions.begin(), instructions.end(),
						   [&](const std::unique_ptr<MIRInstruction> &inst) {
							   return deadStores.count(inst.get()) > 0;
						   }),
			instructions.end());
	}

	// Track stats: count of loads forwarded + dead stores eliminated
	lastRunStats_ += static_cast<int>(loadReplacements.size() + deadStores.size());

	// Invalidate MemorySSA cache if we made changes
	if (changed) {
		memorySSACache_.invalidate(func);
	}

	return changed;
}

bool RedundantLoadStoreEliminationPass::runOnBasicBlock(MIRBasicBlock &block) {
	// This method is no longer used - processing is done in runOnFunction
	// Keep it for API compatibility
	return false;
}

} // namespace mir
