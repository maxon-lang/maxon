#pragma once

#include "memory_ssa.h"
#include "mir.h"
#include <functional>
#include <iostream>
#include <set>
#include <unordered_map>
#include <unordered_set>

namespace mir {

//==============================================================================
// Optimization Pass Infrastructure
//==============================================================================

/**
 * Base class for optimization passes.
 * Each pass operates on a MIRModule and returns true if any changes were made.
 */
class OptimizationPass {
  public:
	virtual ~OptimizationPass() = default;

	// Set verbosity level for logging (0=silent, 1=progress, 2=detailed, 3=trace)
	void setVerboseLevel(int level) { verboseLevel_ = level; }
	int getVerboseLevel() const { return verboseLevel_; }

	// Get the name of this pass (for debugging/logging)
	virtual const char *getName() const = 0;

	// Run the pass on the module
	// Returns true if any changes were made
	virtual bool run(MIRModule &module) = 0;

  protected:
	int verboseLevel_ = 0;

	// Logging helpers
	void logDetail(const std::string &msg) const {
		if (verboseLevel_ >= 2) {
			std::cout << "[Opt] " << getName() << ": " << msg << std::endl;
		}
	}
	void logTrace(const std::string &msg) const {
		if (verboseLevel_ >= 3) {
			std::cout << "[Opt]   " << getName() << ": " << msg << std::endl;
		}
	}
};

//==============================================================================
// Constant Folding Pass
//==============================================================================

/**
 * Fold arithmetic and comparison operations on constant values at compile time.
 *
 * Examples:
 *   add 3, 4 -> 7
 *   mul 5, 0 -> 0
 *   icmp_eq 5, 5 -> 1
 */
class ConstantFoldingPass : public OptimizationPass {
  public:
	const char *getName() const override { return "constant-folding"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool runOnBasicBlock(MIRBasicBlock &block);

	// Try to fold a single instruction
	// Returns the folded constant value, or nullptr if not foldable
	MIRValue *tryFold(MIRInstruction *inst);

	// Fold integer arithmetic
	MIRValue *foldIntegerArithmetic(MIROpcode op, int64_t lhs, int64_t rhs, MIRType *resultType);

	// Fold floating-point arithmetic
	MIRValue *foldFloatArithmetic(MIROpcode op, double lhs, double rhs);

	// Fold integer comparisons
	MIRValue *foldIntegerComparison(MIROpcode op, int64_t lhs, int64_t rhs);

	// Fold floating-point comparisons
	MIRValue *foldFloatComparison(MIROpcode op, double lhs, double rhs);
};

//==============================================================================
// Constant Propagation Pass
//==============================================================================

/**
 * Propagate constant values through the program.
 *
 * When a virtual register is assigned a constant value, replace all uses
 * of that register with the constant.
 *
 * Example:
 *   %0 = 5
 *   %1 = add %0, 1
 * Becomes:
 *   %0 = 5
 *   %1 = add 5, 1
 * (Then constant folding can fold %1 = 6)
 */
class ConstantPropagationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "constant-propagation"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool runOnBasicBlock(MIRBasicBlock &block,
						 std::unordered_map<uint32_t, MIRValue *> &constMap);
};

//==============================================================================
// Dead Code Elimination Pass
//==============================================================================

/**
 * Remove instructions whose results are never used.
 *
 * An instruction is dead if:
 *   1. It produces a result (not Store, Br, Ret, etc.)
 *   2. The result is never used by any other instruction
 *
 * Does not remove instructions with side effects.
 */
class DeadCodeEliminationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "dead-code-elimination"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool eliminateUnusedFunctions(MIRModule &module);

	// Compute the set of values that are used
	std::unordered_set<MIRValue *> computeUsedValues(MIRFunction &func);

	// Check if an instruction has side effects
	bool hasSideEffects(MIRInstruction *inst);
};

//==============================================================================
// Unreachable Block Elimination Pass
//==============================================================================

/**
 * Remove basic blocks that cannot be reached from the entry block.
 */
class UnreachableBlockEliminationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "unreachable-block-elimination"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);

	// Compute the set of reachable blocks starting from entry
	std::unordered_set<MIRBasicBlock *> computeReachableBlocks(MIRFunction &func);
};

//==============================================================================
// Strength Reduction Pass
//==============================================================================

/**
 * Replace expensive operations with cheaper equivalents.
 *
 * Examples:
 *   mul x, 2  -> shl x, 1
 *   mul x, 4  -> shl x, 2
 *   mul x, 8  -> shl x, 3
 *   sdiv x, 2 -> ashr x, 1 (for unsigned)
 */
class StrengthReductionPass : public OptimizationPass {
  public:
	const char *getName() const override { return "strength-reduction"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool runOnBasicBlock(MIRBasicBlock &block);

	// Check if value is a power of two, return the exponent
	// Returns -1 if not a power of two
	int isPowerOfTwo(int64_t value);
};

//==============================================================================
// Algebraic Simplification Pass
//==============================================================================

/**
 * Apply algebraic identities to simplify expressions.
 *
 * Examples:
 *   add x, 0 -> x
 *   sub x, 0 -> x
 *   mul x, 1 -> x
 *   mul x, 0 -> 0
 *   and x, 0 -> 0
 *   and x, -1 -> x
 *   or x, 0 -> x
 *   or x, -1 -> -1
 *   xor x, 0 -> x
 *   shl x, 0 -> x
 *   ashr x, 0 -> x
 *   lshr x, 0 -> x
 */
class AlgebraicSimplificationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "algebraic-simplification"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool runOnBasicBlock(MIRBasicBlock &block);

	// Try to simplify an instruction using algebraic identities
	// Returns the simplified value, or nullptr if no simplification
	MIRValue *trySimplify(MIRInstruction *inst);
};

//==============================================================================
// Simple Function Inlining Pass
//==============================================================================

/**
 * Inline small leaf functions.
 *
 * A function is inlined if:
 *   1. It has a small number of instructions (configurable)
 *   2. It is called only once, OR
 *   3. It is a leaf function (makes no calls)
 */
class SimpleFunctionInliningPass : public OptimizationPass {
  public:
	// Maximum instructions in a function to consider for inlining
	static constexpr size_t DEFAULT_MAX_INLINE_INSTRUCTIONS = 20;

	explicit SimpleFunctionInliningPass(size_t maxInlineInstructions = DEFAULT_MAX_INLINE_INSTRUCTIONS)
		: maxInlineInstructions(maxInlineInstructions) {}

	const char *getName() const override { return "simple-function-inlining"; }
	bool run(MIRModule &module) override;

  private:
	size_t maxInlineInstructions;

	// Count instructions in a function
	size_t countInstructions(MIRFunction &func);

	// Check if function is a leaf (makes no calls)
	bool isLeafFunction(MIRFunction &func);

	// Count how many times a function is called in the module
	size_t countCallSites(MIRModule &module, const std::string &funcName);

	// Check if a function is safe to inline
	bool canInline(MIRFunction &func);

	// Inline a call site
	bool inlineCallSite(MIRFunction &caller, MIRBasicBlock &block,
						MIRInstruction *callInst, MIRFunction &callee);
};

//==============================================================================
// Redundant Load/Store Elimination Pass
//==============================================================================

/**
 * Eliminate redundant loads and stores using MemorySSA.
 *
 * This pass uses MemorySSA to precisely track memory dependencies, avoiding
 * incorrect optimizations when values are copied between allocas.
 *
 * Examples:
 *   store x, [ptr]
 *   load [ptr] -> x  (eliminate load, use x directly)
 *
 *   store x, [ptr]
 *   store y, [ptr]  (eliminate first store)
 *
 * Safe handling of copies:
 *   var x = 10; var y = 20; x = y; y = 30; return x + y
 *   Correctly returns 50 (x gets 20, y gets 30)
 */
class RedundantLoadStoreEliminationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "redundant-load-store-elimination"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool runOnBasicBlock(MIRBasicBlock &block);

	// Cached MemorySSA instances
	MemorySSACache memorySSACache_;
};

//==============================================================================
// Copy Propagation Pass
//==============================================================================

/**
 * Propagate copy instructions to eliminate unnecessary moves.
 *
 * Example:
 *   %1 = copy %0
 *   %2 = add %1, 5
 * Becomes:
 *   %1 = copy %0
 *   %2 = add %0, 5
 * (Then DCE can remove the copy if %1 is not used elsewhere)
 */
class CopyPropagationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "copy-propagation"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);
	bool runOnBasicBlock(MIRBasicBlock &block,
						 std::unordered_map<uint32_t, MIRValue *> &copyMap);
};

//==============================================================================
// PHI Elimination Pass
//==============================================================================

/**
 * Eliminate PHI nodes by inserting copy instructions at predecessor block ends.
 * This pass converts SSA form to a form suitable for register allocation.
 *
 * PHI elimination works by:
 * 1. For each PHI node in a basic block
 * 2. For each incoming value/block pair in the PHI
 * 3. Insert a Copy instruction at the end of the predecessor block (before terminator)
 *    that copies the incoming value to the PHI result
 * 4. Remove the PHI instruction
 *
 * Critical edges are handled by inserting a new intermediate block when:
 * - The predecessor has multiple successors AND
 * - The PHI's block has multiple predecessors
 *
 * Example:
 *   merge:
 *     %result = phi i32 [%a, %left], [%b, %right]
 *
 * Becomes:
 *   left:
 *     ...
 *     %result = copy %a   ; inserted before branch
 *     br merge
 *   right:
 *     ...
 *     %result = copy %b   ; inserted before branch
 *     br merge
 *   merge:
 *     ; PHI removed, %result already has correct value
 */
class PhiEliminationPass : public OptimizationPass {
  public:
	const char *getName() const override { return "phi-elimination"; }
	bool run(MIRModule &module) override;

  private:
	bool runOnFunction(MIRFunction &func);

	// Check if an edge is critical (pred has multiple succs, succ has multiple preds)
	bool isCriticalEdge(MIRBasicBlock *pred, MIRBasicBlock *succ);

	// Split a critical edge by inserting a new block
	MIRBasicBlock *splitCriticalEdge(MIRFunction &func, MIRBasicBlock *pred, MIRBasicBlock *succ);

	// Insert copy instruction at the end of a block (before terminator)
	void insertCopyBeforeTerminator(MIRBasicBlock *block, MIRValue *dest, MIRValue *src);

	// Counter for generating unique split block names
	int splitBlockCounter_ = 0;
};

//==============================================================================
// MIR Optimizer (Pass Manager)
//==============================================================================

/**
 * Manages and runs optimization passes on a MIRModule.
 */
class MIROptimizer {
  public:
	MIROptimizer() = default;

	// Add a pass to the pipeline
	void addPass(std::unique_ptr<OptimizationPass> pass);

	// Set verbosity level for all passes
	void setVerboseLevel(int level) { verboseLevel_ = level; }

	// Run all passes until no more changes are made
	// Returns the total number of passes that made changes
	int runAllPasses(MIRModule &module);

	// Run a fixed number of iterations
	void runPasses(MIRModule &module, int maxIterations = 10);

	// Clear all passes
	void clearPasses();

	// Get pass count
	size_t getPassCount() const { return passes.size(); }

	// Create a standard optimization pipeline
	static MIROptimizer createStandardPipeline(int verboseLevel = 0);

  private:
	std::vector<std::unique_ptr<OptimizationPass>> passes;
	int verboseLevel_ = 0;
};

//==============================================================================
// Utility Functions
//==============================================================================

namespace opt_utils {

// Replace all uses of 'oldVal' with 'newVal' in a function
void replaceAllUsesWith(MIRFunction &func, MIRValue *oldVal, MIRValue *newVal);

// Replace all uses of 'oldVal' with 'newVal' in a basic block
void replaceAllUsesInBlock(MIRBasicBlock &block, MIRValue *oldVal, MIRValue *newVal);

// Check if a value is used in a function
bool isValueUsed(MIRFunction &func, MIRValue *val);

// Check if a value is used in a basic block
bool isValueUsedInBlock(MIRBasicBlock &block, MIRValue *val);

// Get all values defined in a basic block
std::vector<MIRValue *> getDefinedValues(MIRBasicBlock &block);

// Get all values used in an instruction
std::vector<MIRValue *> getUsedValues(MIRInstruction *inst);

} // namespace opt_utils

} // namespace mir
