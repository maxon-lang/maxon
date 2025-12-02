#include "optimizer.h"
#include <algorithm>
#include <functional>

namespace mir {

//==============================================================================
// Dead Code Elimination Pass Implementation
//==============================================================================

bool DeadCodeEliminationPass::run(MIRModule &module) {
	bool changed = false;

	// First, eliminate unused functions (function-level DCE)
	changed |= eliminateUnusedFunctions(module);

	// Eliminate unused globals (global-level DCE)
	changed |= eliminateUnusedGlobals(module);

	// Recalculate which types are actually used by remaining functions
	recalculateUsedTypes(module);

	// Then, eliminate dead instructions within remaining functions
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

void DeadCodeEliminationPass::recalculateUsedTypes(MIRModule &module) {
	// Reset all struct type used flags
	for (auto &[name, type] : module.structTypes) {
		type->used = false;
	}

	// Helper to mark a type and its dependencies as used
	std::function<void(MIRType *)> markTypeUsed = [&](MIRType *type) {
		if (!type)
			return;
		if (type->kind == MIRTypeKind::Struct) {
			if (type->used)
				return; // Already marked
			type->used = true;
			// Mark field types recursively
			for (MIRType *fieldType : type->fieldTypes) {
				markTypeUsed(fieldType);
			}
		} else if (type->kind == MIRTypeKind::Array) {
			markTypeUsed(type->elementType);
		}
	};

	// Scan all remaining functions for type usage
	for (auto &func : module.functions) {
		// Check return type
		markTypeUsed(func->returnType);

		// Check parameter types
		for (auto &param : func->parameters) {
			markTypeUsed(param->type);
		}

		// Scan instructions for type usage
		for (auto &block : func->basicBlocks) {
			for (auto &inst : block->instructions) {
				// Check result type
				if (inst->result) {
					markTypeUsed(inst->result->type);
				}
				// Check operand types
				for (auto &operand : inst->operands) {
					if (operand) {
						markTypeUsed(operand->type);
					}
				}
				// Check allocatedType for Alloca instructions
				if (inst->opcode == MIROpcode::Alloca && inst->allocatedType) {
					markTypeUsed(inst->allocatedType);
				}
				// Check elementType for GEP instructions
				if (inst->opcode == MIROpcode::GetElementPtr && inst->elementType) {
					markTypeUsed(inst->elementType);
				}
			}
		}
	}
}

bool DeadCodeEliminationPass::eliminateUnusedFunctions(MIRModule &module) {
	// Compute reachable functions starting from main and exported functions
	std::unordered_set<std::string> reachable;
	std::vector<std::string> worklist;

	// Find all entry points
	// - main and _start are obvious entry points
	// - __ffi_dispatch is special: it's called from __ffi_worker_main and dynamically
	//   dispatches to extern functions based on function ID. Since the calls are dynamic
	//   (not static Call instructions), we need to mark it as a root to prevent it and
	//   the functions it references from being eliminated.
	for (auto &func : module.functions) {
		if (func->name == "main" || func->name == "_start" || func->name == "__ffi_dispatch") {
			reachable.insert(func->name);
			worklist.push_back(func->name);
		}
	}

	// Build a map of function name to MIRFunction for quick lookup
	std::unordered_map<std::string, MIRFunction *> functionMap;
	for (auto &func : module.functions) {
		functionMap[func->name] = func.get();
	}

	// Traverse call graph to find all reachable functions
	while (!worklist.empty()) {
		std::string currentName = worklist.back();
		worklist.pop_back();

		auto it = functionMap.find(currentName);
		if (it == functionMap.end() || it->second->isExternal) {
			continue;
		}

		MIRFunction *currentFunc = it->second;

		// Scan all instructions for function calls
		for (auto &block : currentFunc->basicBlocks) {
			for (auto &inst : block->instructions) {
				if (inst->opcode == MIROpcode::Call && !inst->calleeName.empty()) {
					// The function being called is stored in calleeName
					if (reachable.find(inst->calleeName) == reachable.end()) {
						reachable.insert(inst->calleeName);
						worklist.push_back(inst->calleeName);
					}
				}
			}
		}
	}

	// Remove unreachable functions (including unused external declarations)
	size_t originalSize = module.functions.size();
	module.functions.erase(
		std::remove_if(module.functions.begin(), module.functions.end(),
					   [&](const std::unique_ptr<MIRFunction> &func) {
						   // Remove if not reachable from entry points
						   return reachable.find(func->name) == reachable.end();
					   }),
		module.functions.end());

	bool changed = (module.functions.size() < originalSize);
	if (changed && verboseLevel_ >= 2) {
		size_t eliminated = originalSize - module.functions.size();
		std::cout << "  Dead Code Elimination: removed " << eliminated
				  << " unused function(s)" << std::endl;
	}

	return changed;
}

bool DeadCodeEliminationPass::eliminateUnusedGlobals(MIRModule &module) {
	// Collect all global names that are referenced by remaining functions
	std::unordered_set<std::string> usedGlobals;

	// Helper to check if a value is a global reference
	auto isGlobalReference = [](MIRValue *v) {
		return v && !v->name.empty() &&
			   (v->isGlobalRef || v->kind == MIRValueKind::Global);
	};

	for (auto &func : module.functions) {
		for (auto &block : func->basicBlocks) {
			for (auto &inst : block->instructions) {
				// Check operands for global references
				for (auto *operand : inst->operands) {
					if (isGlobalReference(operand)) {
						usedGlobals.insert(operand->name);
					}
				}
				// Check phi incoming values
				for (auto &incoming : inst->phiIncoming) {
					if (isGlobalReference(incoming.first)) {
						usedGlobals.insert(incoming.first->name);
					}
				}
			}
		}
	}

	// Remove unused globals
	size_t originalSize = module.globals.size();
	module.globals.erase(
		std::remove_if(module.globals.begin(), module.globals.end(),
					   [&](const std::unique_ptr<MIRGlobal> &global) {
						   return usedGlobals.find(global->name) == usedGlobals.end();
					   }),
		module.globals.end());

	bool changed = (module.globals.size() < originalSize);
	if (changed && verboseLevel_ >= 2) {
		size_t eliminated = originalSize - module.globals.size();
		std::cout << "  Dead Code Elimination: removed " << eliminated
				  << " unused global(s)" << std::endl;
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
		size_t sizeBefore = instructions.size();
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
		lastRunStats_ += static_cast<int>(sizeBefore - instructions.size());
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

} // namespace mir
