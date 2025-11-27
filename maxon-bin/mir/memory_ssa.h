#pragma once

#include "mir.h"
#include <functional>
#include <memory>
#include <unordered_map>
#include <unordered_set>
#include <vector>

namespace mir {

//==============================================================================
// MemorySSA - Memory State Tracking in SSA Form
//==============================================================================
//
// MemorySSA provides a use-def chain representation for memory operations,
// enabling precise tracking of which store a load reads from. This is used
// by optimization passes like RedundantLoadStoreElimination to safely
// propagate values through memory.
//
// Key concepts:
// - MemoryDef: Created for Store and Call instructions that may modify memory
// - MemoryUse: Created for Load instructions that read from memory
// - MemoryPhi: Created at control flow join points to merge memory states
//
// Each MemoryAccess has a "defining access" that represents the memory state
// it depends on. For loads, this is the most recent store that could have
// written to the location being read.
//
//==============================================================================

class MemorySSA;

//------------------------------------------------------------------------------
// MemoryAccess - Base class for memory SSA nodes
//------------------------------------------------------------------------------

enum class MemoryAccessKind {
	Def,
	Use,
	Phi,
	LiveOnEntry // Special: represents memory state at function entry
};

class MemoryAccess {
  public:
	MemoryAccessKind kind;
	uint32_t id; // Unique ID within the function

	// The instruction this access is attached to (nullptr for Phi/LiveOnEntry)
	MIRInstruction *memoryInst = nullptr;

	// The block containing this access
	MIRBasicBlock *block = nullptr;

	MemoryAccess(MemoryAccessKind k, uint32_t id) : kind(k), id(id) {}
	virtual ~MemoryAccess() = default;

	bool isDef() const { return kind == MemoryAccessKind::Def; }
	bool isUse() const { return kind == MemoryAccessKind::Use; }
	bool isPhi() const { return kind == MemoryAccessKind::Phi; }
	bool isLiveOnEntry() const { return kind == MemoryAccessKind::LiveOnEntry; }

	virtual std::string toString() const;
};

//------------------------------------------------------------------------------
// MemoryDef - Represents a memory-writing instruction (Store, Call)
//------------------------------------------------------------------------------

class MemoryDef : public MemoryAccess {
  public:
	// The previous memory state this def depends on
	MemoryAccess *definingAccess = nullptr;

	// For stores: the pointer being written to
	MIRValue *storedPointer = nullptr;

	// For stores: the value being stored
	MIRValue *storedValue = nullptr;

	MemoryDef(uint32_t id) : MemoryAccess(MemoryAccessKind::Def, id) {}

	std::string toString() const override;
};

//------------------------------------------------------------------------------
// MemoryUse - Represents a memory-reading instruction (Load)
//------------------------------------------------------------------------------

class MemoryUse : public MemoryAccess {
  public:
	// The memory state this use reads from
	MemoryAccess *definingAccess = nullptr;

	// The pointer being read from
	MIRValue *loadedPointer = nullptr;

	MemoryUse(uint32_t id) : MemoryAccess(MemoryAccessKind::Use, id) {}

	std::string toString() const override;
};

//------------------------------------------------------------------------------
// MemoryPhi - Merges memory states at control flow join points
//------------------------------------------------------------------------------

class MemoryPhi : public MemoryAccess {
  public:
	// Incoming memory states paired with predecessor blocks
	std::vector<std::pair<MemoryAccess *, MIRBasicBlock *>> incomingValues;

	MemoryPhi(uint32_t id) : MemoryAccess(MemoryAccessKind::Phi, id) {}

	void addIncoming(MemoryAccess *access, MIRBasicBlock *pred) {
		incomingValues.emplace_back(access, pred);
	}

	std::string toString() const override;
};

//------------------------------------------------------------------------------
// LiveOnEntry - Special node representing memory state at function entry
//------------------------------------------------------------------------------

class LiveOnEntry : public MemoryAccess {
  public:
	LiveOnEntry() : MemoryAccess(MemoryAccessKind::LiveOnEntry, 0) {}

	std::string toString() const override { return "liveOnEntry"; }
};

//------------------------------------------------------------------------------
// MemorySSA - Main class for building and querying memory SSA
//------------------------------------------------------------------------------

class MemorySSA {
  public:
	explicit MemorySSA(MIRFunction &func);
	~MemorySSA() = default;

	// Build the MemorySSA representation
	void build();

	// Invalidate the cache (call when function is modified)
	void invalidate() { valid_ = false; }

	// Check if the cache is valid
	bool isValid() const { return valid_; }

	// Get the MemoryAccess for an instruction (nullptr if none)
	MemoryAccess *getMemoryAccess(MIRInstruction *inst) const;

	// Get the defining access for a MemoryUse (walks through phis if needed)
	// Returns the MemoryDef that defines the value read by this use
	MemoryAccess *getDefiningAccess(MemoryUse *use) const;

	// Get the clobbering memory access for a load
	// This walks the def chain to find the store that actually wrote the value
	// Returns nullptr if the value comes from LiveOnEntry
	MemoryDef *getClobberingMemoryAccess(MemoryUse *use) const;

	// Check if two pointers may alias (conservative: same underlying object)
	bool mayAlias(MIRValue *ptr1, MIRValue *ptr2) const;

	// Check if two pointers must alias (definitely same memory location)
	// This is stronger than mayAlias - requires identical pointer expressions
	bool mustAlias(MIRValue *ptr1, MIRValue *ptr2) const;

	// Get the underlying object (base alloca) for a pointer
	MIRValue *getUnderlyingObject(MIRValue *ptr) const;

	// Get the LiveOnEntry node
	LiveOnEntry *getLiveOnEntry() const { return liveOnEntry_.get(); }

	// Debug: print the MemorySSA representation
	void print() const;

  private:
	MIRFunction &function_;
	bool valid_ = false;

	// All memory accesses owned by this MemorySSA
	std::vector<std::unique_ptr<MemoryAccess>> accesses_;

	// Map from instruction to its MemoryAccess
	std::unordered_map<MIRInstruction *, MemoryAccess *> instToAccess_;

	// Map from block to its MemoryPhi (if any)
	std::unordered_map<MIRBasicBlock *, MemoryPhi *> blockToMemoryPhi_;

	// The LiveOnEntry node
	std::unique_ptr<LiveOnEntry> liveOnEntry_;

	// Next ID for memory accesses
	uint32_t nextId_ = 1;

	// Dominator tree: maps each block to its immediate dominator
	std::unordered_map<MIRBasicBlock *, MIRBasicBlock *> idom_;

	// Dominance frontier: maps each block to its dominance frontier set
	std::unordered_map<MIRBasicBlock *, std::unordered_set<MIRBasicBlock *>> domFrontier_;

	// CFG (may need to rebuild if not populated)
	std::unordered_map<MIRBasicBlock *, std::vector<MIRBasicBlock *>> predecessors_;
	std::unordered_map<MIRBasicBlock *, std::vector<MIRBasicBlock *>> successors_;

	// Current memory state per block (used during construction)
	std::unordered_map<MIRBasicBlock *, MemoryAccess *> currentDef_;

	// Build helpers
	void buildCFG();
	void computeDominators();
	void computeDominanceFrontier();
	void placeMemoryPhis();
	void renamePass();
	void renameBlock(MIRBasicBlock *block, MemoryAccess *incomingDef);

	// Create new memory access nodes
	MemoryDef *createMemoryDef(MIRInstruction *inst);
	MemoryUse *createMemoryUse(MIRInstruction *inst);
	MemoryPhi *createMemoryPhi(MIRBasicBlock *block);

	// Check if an instruction reads or writes memory
	bool isMemoryWrite(MIRInstruction *inst) const;
	bool isMemoryRead(MIRInstruction *inst) const;

	// Check if a call may write memory (considering attributes)
	bool callMayWriteMemory(MIRInstruction *callInst) const;
	bool callMayReadMemory(MIRInstruction *callInst) const;
};

//------------------------------------------------------------------------------
// MemorySSACache - Manages cached MemorySSA instances per function
//------------------------------------------------------------------------------

class MemorySSACache {
  public:
	// Get or build MemorySSA for a function
	MemorySSA &getMemorySSA(MIRFunction &func);

	// Invalidate cache for a function
	void invalidate(MIRFunction &func);

	// Clear all cached data
	void clear();

  private:
	std::unordered_map<MIRFunction *, std::unique_ptr<MemorySSA>> cache_;
};

} // namespace mir
