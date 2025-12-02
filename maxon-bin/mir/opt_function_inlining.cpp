#include "optimizer.h"

namespace mir {

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
							lastRunStats_++;
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

		// Copy type fields (critical for GEP and Alloca)
		newInst->elementType = inst->elementType;
		newInst->allocatedType = inst->allocatedType;

		// Copy indirect call type information
		newInst->indirectReturnType = inst->indirectReturnType;
		newInst->indirectParamTypes = inst->indirectParamTypes;

		// Copy memory attributes
		newInst->callDoesNotReadMemory = inst->callDoesNotReadMemory;
		newInst->callDoesNotWriteMemory = inst->callDoesNotWriteMemory;
		newInst->callOnlyAccessesArgMemory = inst->callOnlyAccessesArgMemory;

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

} // namespace mir
