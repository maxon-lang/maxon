#pragma once

#include "mir.h"
#include <string>
#include <unordered_map>
#include <vector>

namespace mir {

//==============================================================================
// MIR SSA Verifier
//==============================================================================
// Validates MIR modules for correctness, similar to LLVM's verify pass.
// This is especially important for hand-written .mir files (runtime libraries)
// where cross-block value usage bugs are easy to introduce and hard to debug.
//
// Checks performed:
// 1. SSA Form:
//    - Every value use is dominated by its definition
//    - Only PHI nodes can be self-referential
//
// 2. PHI Nodes:
//    - Must have exactly one entry for each predecessor block
//    - Must be at the start of a basic block, grouped together
//    - Must have at least one entry
//
// 3. Terminators:
//    - All basic blocks must end with exactly one terminator
//    - Terminators must only appear at the end of a block
//    - Entry block must not have predecessors
//
// 4. Type Consistency:
//    - Binary operator operands must have the same type
//    - Function call argument types must match parameter types
//    - Return value must match function return type
//    - Conditional branch condition must be i1 type
//
// 5. CFG Consistency:
//    - Branch targets must reference valid basic blocks
//==============================================================================

//------------------------------------------------------------------------------
// Verification Error
//------------------------------------------------------------------------------

struct MIRVerifyError {
	std::string functionName;
	std::string blockName;
	std::string message;
	int instructionIndex = -1; // -1 means block-level error

	std::string toString() const;
};

//------------------------------------------------------------------------------
// Verification Result
//------------------------------------------------------------------------------

struct MIRVerifyResult {
	std::vector<MIRVerifyError> errors;

	bool success() const { return errors.empty(); }
};

//------------------------------------------------------------------------------
// MIR Verifier
//------------------------------------------------------------------------------

class MIRVerifier {
  public:
	// Verify an entire module
	static MIRVerifyResult verify(const MIRModule &module);

	// Verify a single function
	static MIRVerifyResult verifyFunction(const MIRFunction &func);

  private:
	MIRVerifier(const MIRFunction &func);

	void run();

	// CFG computation (predecessors may not be populated by parser)
	void buildCFG();

	// Dominator computation
	void computeDominators();
	bool dominates(const MIRBasicBlock *defBlock, const MIRBasicBlock *useBlock) const;
	bool strictlyDominates(const MIRBasicBlock *defBlock, const MIRBasicBlock *useBlock) const;
	const MIRBasicBlock *intersectDom(const MIRBasicBlock *a, const MIRBasicBlock *b) const;
	const MIRBasicBlock *intersect(const MIRBasicBlock *b1, const MIRBasicBlock *b2,
								   const std::unordered_map<const MIRBasicBlock *, int> &rpoIndex) const;

	// SSA validation
	void verifySSAForm();
	void verifyValueUse(MIRValue *value, MIRBasicBlock *useBlock, int instIndex);

	// PHI node validation
	void verifyPhiNodes();
	void verifyPhiNode(MIRInstruction *phi, MIRBasicBlock *block, int instIndex);

	// Terminator validation
	void verifyTerminators();

	// Type validation
	void verifyTypes();
	void verifyInstructionTypes(MIRInstruction *inst, MIRBasicBlock *block, int instIndex);

	// CFG validation
	void verifyCFG();

	// Error reporting
	void addError(const std::string &msg);
	void addError(const std::string &blockName, const std::string &msg);
	void addError(const std::string &blockName, int instIndex, const std::string &msg);

	const MIRFunction &function;
	std::vector<MIRVerifyError> errors;

	// Dominator tree: maps each block to its immediate dominator
	std::unordered_map<const MIRBasicBlock *, const MIRBasicBlock *> idom;

	// Maps value to its defining block (for SSA verification)
	std::unordered_map<const MIRValue *, const MIRBasicBlock *> valueDefBlock;

	// Local CFG (in case parser didn't populate predecessors/successors)
	std::unordered_map<const MIRBasicBlock *, std::vector<const MIRBasicBlock *>> cfgPredecessors;
	std::unordered_map<const MIRBasicBlock *, std::vector<const MIRBasicBlock *>> cfgSuccessors;
};

} // namespace mir
