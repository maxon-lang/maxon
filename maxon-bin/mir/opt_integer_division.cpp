#include "optimizer.h"

namespace mir {

//==============================================================================
// Integer Division Optimization Pass Implementation
//==============================================================================
// This pass pattern-matches the sequence:
//   fptosi(call @trunc(fdiv(sitofp(a), sitofp(b)))) -> sdiv(a, b)
//
// This pattern arises when integer division is lowered through floating
// point operations (int/int now returns float). When wrapped in trunc(),
// we can optimize back to a direct integer division.

bool IntegerDivisionOptimizationPass::run(MIRModule &module) {
	bool changed = false;
	for (auto &func : module.functions) {
		if (!func->isExternal) {
			changed |= runOnFunction(*func);
		}
	}
	return changed;
}

bool IntegerDivisionOptimizationPass::runOnFunction(MIRFunction &func) {
	bool changed = false;
	for (auto &block : func.basicBlocks) {
		changed |= runOnBasicBlock(*block);
	}
	return changed;
}

bool IntegerDivisionOptimizationPass::runOnBasicBlock(MIRBasicBlock &block) {
	bool changed = false;

	for (auto it = block.instructions.begin(); it != block.instructions.end(); ++it) {
		auto &inst = *it;

		// Pattern: fptosi(call @trunc(fdiv(sitofp(a), sitofp(b)))) -> sdiv(a, b)
		if (inst->opcode == MIROpcode::FPToSI && inst->operands.size() == 1) {
			auto *operand = inst->operands[0];
			MIRValue *divResult = operand;

			// Check if there's a call to trunc in between
			if (operand->definingInst && operand->definingInst->opcode == MIROpcode::Call) {
				auto *callInst = operand->definingInst;
				// Check if calling trunc
				if (callInst->calleeName == "trunc" || (callInst->calleeFunc && callInst->calleeFunc->name == "trunc")) {
					// Get the argument to trunc (first operand)
					if (callInst->operands.size() > 0) {
						divResult = callInst->operands[0];
					}
				}
			}

			// Check: operand is result of FDiv
			if (divResult && divResult->definingInst &&
				divResult->definingInst->opcode == MIROpcode::FDiv &&
				divResult->definingInst->operands.size() == 2) {

				auto *fdivInst = divResult->definingInst;
				auto *lhs = fdivInst->operands[0];
				auto *rhs = fdivInst->operands[1];

				// Check: both operands are SIToFP
				if (lhs->definingInst && lhs->definingInst->opcode == MIROpcode::SIToFP &&
					lhs->definingInst->operands.size() == 1 &&
					rhs->definingInst && rhs->definingInst->opcode == MIROpcode::SIToFP &&
					rhs->definingInst->operands.size() == 1) {

					// Extract original integer operands
					auto *originalLhs = lhs->definingInst->operands[0];
					auto *originalRhs = rhs->definingInst->operands[0];

					// Verify both original operands are integers
					if (originalLhs->type && originalLhs->type->isInteger() &&
						originalRhs->type && originalRhs->type->isInteger()) {

						// Create new SDiv instruction
						auto newInst = std::make_unique<MIRInstruction>(MIROpcode::SDiv);
						newInst->operands.push_back(originalLhs);
						newInst->operands.push_back(originalRhs);
						newInst->result = inst->result; // Reuse result register
						newInst->parent = inst->parent;

						// Update the defining instruction for the result
						if (newInst->result) {
							newInst->result->definingInst = newInst.get();
						}

						// Copy source location for debugging
						newInst->sourceLine = inst->sourceLine;
						newInst->sourceColumn = inst->sourceColumn;

						// Replace FPToSI instruction with SDiv
						*it = std::move(newInst);

						changed = true;
						lastRunStats_++;
					}
				}
			}
		}
	}

	return changed;
}

} // namespace mir
