#pragma once

#include "../mir/mir.h"
#include "x86_encoding.h"
#include <algorithm>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace backend {

//==============================================================================
// Live Range Representation
//==============================================================================

struct LiveRange {
	uint32_t regId;				   // Virtual register ID
	mir::MIRType *type;			   // Type for size determination
	uint32_t start;				   // Start instruction index
	uint32_t end;				   // End instruction index (inclusive)
	X86Reg physReg = X86Reg::None; // Assigned physical register
	int32_t spillSlot = -1;		   // Stack offset if spilled
	bool isFloat = false;		   // Whether this needs an XMM register

	// Preference hints
	X86Reg preferredReg = X86Reg::None;

	bool overlaps(const LiveRange &other) const {
		return !(end < other.start || start > other.end);
	}

	bool contains(uint32_t point) const {
		return point >= start && point <= end;
	}
};

//==============================================================================
// Liveness Analysis
//==============================================================================

struct BlockLiveness {
	std::unordered_set<uint32_t> liveIn;
	std::unordered_set<uint32_t> liveOut;
	std::unordered_set<uint32_t> def;
	std::unordered_set<uint32_t> use;
};

class LivenessAnalysis {
  private:
	mir::MIRFunction *function;
	std::unordered_map<mir::MIRBasicBlock *, BlockLiveness> blockLiveness;
	std::unordered_map<mir::MIRBasicBlock *, uint32_t> blockStartIndex;
	std::unordered_map<mir::MIRBasicBlock *, uint32_t> blockEndIndex;

  public:
	explicit LivenessAnalysis(mir::MIRFunction *func);

	void compute();
	std::vector<LiveRange> buildLiveRanges();

  private:
	void computeLocalLiveness();
	void computeGlobalLiveness();
	void assignInstructionIndices();

	uint32_t getInstructionIndex(mir::MIRBasicBlock *block, size_t instIdx) const;
};

//==============================================================================
// Linear-Scan Register Allocator
//==============================================================================

struct RegisterAllocResult {
	std::unordered_map<uint32_t, X86Reg> regAssignment; // vreg -> phys reg
	std::unordered_map<uint32_t, int32_t> spillSlots;	// vreg -> stack offset
	uint32_t totalSpillSize = 0;
	std::vector<X86Reg> usedCalleeSaved;
};

class LinearScanAllocator {
  private:
	mir::MIRFunction *function;
	bool isWindows;

	// Available registers
	std::vector<X86Reg> availableGPRegs;
	std::vector<X86Reg> availableXMMRegs;
	std::vector<X86Reg> calleeSavedGP;
	std::vector<X86Reg> calleeSavedXMM;

	// Allocation state
	std::vector<LiveRange> liveRanges;
	std::vector<LiveRange *> active; // Currently active (allocated) ranges
	std::unordered_set<X86Reg> freeGPRegs;
	std::unordered_set<X86Reg> freeXMMRegs;

	// Results
	RegisterAllocResult result;
	int32_t nextSpillOffset = -8;

  public:
	LinearScanAllocator(mir::MIRFunction *func, bool windows = true);

	RegisterAllocResult allocate();

  private:
	void initializeRegisters();
	void sortLiveRanges();
	void allocateRange(LiveRange &range);
	void expireOldIntervals(uint32_t currentStart);
	void spillAtInterval(LiveRange &range);

	X86Reg allocatePhysReg(bool isFloat, X86Reg preferred = X86Reg::None);
	void freePhysReg(X86Reg reg, bool isFloat);
	int32_t allocateSpillSlot(uint32_t size);

	bool isCalleeSaved(X86Reg reg);
};

//==============================================================================
// Register Coalescing (Optional optimization)
//==============================================================================

class RegisterCoalescer {
  private:
	mir::MIRFunction *function;
	std::unordered_map<uint32_t, uint32_t> coalescedRegs; // vreg -> representative

  public:
	explicit RegisterCoalescer(mir::MIRFunction *func) : function(func) {}

	// Attempt to coalesce copy instructions
	void coalesce();

	// Get representative register (follows union-find)
	uint32_t getRepresentative(uint32_t regId);

  private:
	bool canCoalesce(uint32_t reg1, uint32_t reg2, const std::vector<LiveRange> &ranges);
	void unionRegs(uint32_t reg1, uint32_t reg2);
};

//==============================================================================
// Spill Code Inserter
//==============================================================================

class SpillCodeInserter {
  private:
	mir::MIRFunction *function;
	const RegisterAllocResult &allocResult;

  public:
	SpillCodeInserter(mir::MIRFunction *func, const RegisterAllocResult &result)
		: function(func), allocResult(result) {}

	// Insert load/store instructions for spilled registers
	void insertSpillCode();

  private:
	void insertLoadsForBlock(mir::MIRBasicBlock *block);
	void insertStoresForBlock(mir::MIRBasicBlock *block);
};

} // namespace backend
