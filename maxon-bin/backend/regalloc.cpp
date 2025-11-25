#include "regalloc.h"
#include <algorithm>
#include <stdexcept>

namespace backend {

//==============================================================================
// Liveness Analysis Implementation
//==============================================================================

LivenessAnalysis::LivenessAnalysis(mir::MIRFunction *func) : function(func) {}

void LivenessAnalysis::compute() {
	assignInstructionIndices();
	computeLocalLiveness();
	computeGlobalLiveness();
}

void LivenessAnalysis::assignInstructionIndices() {
	uint32_t index = 0;
	for (auto &block : function->basicBlocks) {
		blockStartIndex[block.get()] = index;
		index += block->instructions.size();
		blockEndIndex[block.get()] = index - 1;
	}
}

uint32_t LivenessAnalysis::getInstructionIndex(mir::MIRBasicBlock *block, size_t instIdx) const {
	auto it = blockStartIndex.find(block);
	if (it == blockStartIndex.end()) {
		throw std::runtime_error("Block not found in instruction index map");
	}
	return it->second + static_cast<uint32_t>(instIdx);
}

void LivenessAnalysis::computeLocalLiveness() {
	for (auto &block : function->basicBlocks) {
		BlockLiveness &bl = blockLiveness[block.get()];

		for (auto &inst : block->instructions) {
			// Record uses (operands that are virtual registers)
			for (auto *operand : inst->operands) {
				if (operand->kind == mir::MIRValueKind::VirtualReg ||
					operand->kind == mir::MIRValueKind::Parameter) {
					// Use before def in this block
					if (bl.def.find(operand->regId) == bl.def.end()) {
						bl.use.insert(operand->regId);
					}
				}
			}

			// Record defs (results)
			if (inst->result && inst->result->kind == mir::MIRValueKind::VirtualReg) {
				bl.def.insert(inst->result->regId);
			}
		}
	}
}

void LivenessAnalysis::computeGlobalLiveness() {
	// Initialize liveIn with use sets
	for (auto &block : function->basicBlocks) {
		blockLiveness[block.get()].liveIn = blockLiveness[block.get()].use;
	}

	// Iterate until fixed point
	bool changed = true;
	while (changed) {
		changed = false;

		// Process blocks in reverse order for faster convergence
		for (auto it = function->basicBlocks.rbegin();
			 it != function->basicBlocks.rend(); ++it) {
			auto *block = it->get();
			BlockLiveness &bl = blockLiveness[block];

			// liveOut = union of liveIn of all successors
			std::unordered_set<uint32_t> newLiveOut;
			for (auto *succ : block->successors) {
				for (uint32_t reg : blockLiveness[succ].liveIn) {
					newLiveOut.insert(reg);
				}
			}

			if (newLiveOut != bl.liveOut) {
				changed = true;
				bl.liveOut = newLiveOut;
			}

			// liveIn = use ∪ (liveOut - def)
			std::unordered_set<uint32_t> newLiveIn = bl.use;
			for (uint32_t reg : bl.liveOut) {
				if (bl.def.find(reg) == bl.def.end()) {
					newLiveIn.insert(reg);
				}
			}

			if (newLiveIn != bl.liveIn) {
				changed = true;
				bl.liveIn = newLiveIn;
			}
		}
	}
}

std::vector<LiveRange> LivenessAnalysis::buildLiveRanges() {
	std::unordered_map<uint32_t, LiveRange> ranges;

	// Initialize ranges for parameters
	for (auto *param : function->parameters) {
		LiveRange lr;
		lr.regId = param->regId;
		lr.type = param->type;
		lr.start = 0;
		lr.end = 0;
		lr.isFloat = param->type->isFloat();
		ranges[param->regId] = lr;
	}

	// Process each block
	for (auto &block : function->basicBlocks) {
		uint32_t blockStart = blockStartIndex[block.get()];
		uint32_t blockEnd = blockEndIndex[block.get()];

		// Extend ranges for values live at block entry
		for (uint32_t regId : blockLiveness[block.get()].liveIn) {
			if (ranges.find(regId) != ranges.end()) {
				ranges[regId].end = std::max(ranges[regId].end, blockEnd);
			}
		}

		// Process instructions
		for (size_t i = 0; i < block->instructions.size(); ++i) {
			auto &inst = block->instructions[i];
			uint32_t instIndex = blockStart + static_cast<uint32_t>(i);

			// Extend range for each use
			for (auto *operand : inst->operands) {
				if (operand->kind == mir::MIRValueKind::VirtualReg ||
					operand->kind == mir::MIRValueKind::Parameter) {
					if (ranges.find(operand->regId) != ranges.end()) {
						ranges[operand->regId].end = std::max(
							ranges[operand->regId].end, instIndex);
					}
				}
			}

			// Create/update range for def
			if (inst->result && inst->result->kind == mir::MIRValueKind::VirtualReg) {
				uint32_t regId = inst->result->regId;
				if (ranges.find(regId) == ranges.end()) {
					LiveRange lr;
					lr.regId = regId;
					lr.type = inst->result->type;
					lr.start = instIndex;
					lr.end = instIndex;
					lr.isFloat = inst->result->type->isFloat();
					ranges[regId] = lr;
				} else {
					ranges[regId].start = std::min(ranges[regId].start, instIndex);
				}
			}
		}

		// Extend ranges for values live at block exit
		for (uint32_t regId : blockLiveness[block.get()].liveOut) {
			if (ranges.find(regId) != ranges.end()) {
				ranges[regId].end = std::max(ranges[regId].end, blockEnd);
			}
		}
	}

	// Convert to vector
	std::vector<LiveRange> result;
	result.reserve(ranges.size());
	for (auto &[regId, lr] : ranges) {
		result.push_back(lr);
	}

	return result;
}

//==============================================================================
// Linear-Scan Allocator Implementation
//==============================================================================

LinearScanAllocator::LinearScanAllocator(mir::MIRFunction *func, bool windows)
	: function(func), isWindows(windows) {
	initializeRegisters();
}

void LinearScanAllocator::initializeRegisters() {
	if (isWindows) {
		// Windows x64 ABI
		// Volatile (caller-saved): RAX, RCX, RDX, R8-R11
		// Non-volatile (callee-saved): RBX, RSI, RDI, R12-R15, RBP, RSP
		availableGPRegs = {
			X86Reg::RAX, X86Reg::RCX, X86Reg::RDX,
			X86Reg::R8, X86Reg::R9, X86Reg::R10, X86Reg::R11,
			X86Reg::RBX, X86Reg::RSI, X86Reg::RDI,
			X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
		calleeSavedGP = {
			X86Reg::RBX, X86Reg::RSI, X86Reg::RDI,
			X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};

		// XMM0-5 are volatile, XMM6-15 are non-volatile
		availableXMMRegs = {
			X86Reg::XMM0, X86Reg::XMM1, X86Reg::XMM2, X86Reg::XMM3,
			X86Reg::XMM4, X86Reg::XMM5,
			X86Reg::XMM6, X86Reg::XMM7, X86Reg::XMM8, X86Reg::XMM9,
			X86Reg::XMM10, X86Reg::XMM11, X86Reg::XMM12, X86Reg::XMM13,
			X86Reg::XMM14, X86Reg::XMM15};
		calleeSavedXMM = {
			X86Reg::XMM6, X86Reg::XMM7, X86Reg::XMM8, X86Reg::XMM9,
			X86Reg::XMM10, X86Reg::XMM11, X86Reg::XMM12, X86Reg::XMM13,
			X86Reg::XMM14, X86Reg::XMM15};
	} else {
		// System V AMD64 ABI
		// Volatile: RAX, RCX, RDX, RSI, RDI, R8-R11
		// Non-volatile: RBX, R12-R15, RBP, RSP
		availableGPRegs = {
			X86Reg::RAX, X86Reg::RCX, X86Reg::RDX,
			X86Reg::RSI, X86Reg::RDI,
			X86Reg::R8, X86Reg::R9, X86Reg::R10, X86Reg::R11,
			X86Reg::RBX, X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};
		calleeSavedGP = {
			X86Reg::RBX, X86Reg::R12, X86Reg::R13, X86Reg::R14, X86Reg::R15};

		// All XMM registers are volatile in SysV
		availableXMMRegs = {
			X86Reg::XMM0, X86Reg::XMM1, X86Reg::XMM2, X86Reg::XMM3,
			X86Reg::XMM4, X86Reg::XMM5, X86Reg::XMM6, X86Reg::XMM7,
			X86Reg::XMM8, X86Reg::XMM9, X86Reg::XMM10, X86Reg::XMM11,
			X86Reg::XMM12, X86Reg::XMM13, X86Reg::XMM14, X86Reg::XMM15};
		calleeSavedXMM = {};
	}

	// Initialize free register sets
	freeGPRegs.insert(availableGPRegs.begin(), availableGPRegs.end());
	freeXMMRegs.insert(availableXMMRegs.begin(), availableXMMRegs.end());
}

RegisterAllocResult LinearScanAllocator::allocate() {
	// Compute liveness
	LivenessAnalysis liveness(function);
	liveness.compute();
	liveRanges = liveness.buildLiveRanges();

	// Sort by start position
	sortLiveRanges();

	// Allocate each range
	for (auto &range : liveRanges) {
		expireOldIntervals(range.start);
		allocateRange(range);
	}

	// Build result
	for (const auto &range : liveRanges) {
		if (range.physReg != X86Reg::None) {
			result.regAssignment[range.regId] = range.physReg;

			// Track callee-saved usage
			if (isCalleeSaved(range.physReg)) {
				if (std::find(result.usedCalleeSaved.begin(),
							  result.usedCalleeSaved.end(),
							  range.physReg) == result.usedCalleeSaved.end()) {
					result.usedCalleeSaved.push_back(range.physReg);
				}
			}
		} else if (range.spillSlot >= 0) {
			result.spillSlots[range.regId] = range.spillSlot;
		}
	}

	result.totalSpillSize = static_cast<uint32_t>(-nextSpillOffset - 8);

	return result;
}

void LinearScanAllocator::sortLiveRanges() {
	std::sort(liveRanges.begin(), liveRanges.end(),
			  [](const LiveRange &a, const LiveRange &b) {
				  return a.start < b.start;
			  });
}

void LinearScanAllocator::allocateRange(LiveRange &range) {
	// Try to allocate a physical register
	X86Reg reg = allocatePhysReg(range.isFloat, range.preferredReg);

	if (reg != X86Reg::None) {
		range.physReg = reg;
		active.push_back(&range);

		// Keep active list sorted by end position
		std::sort(active.begin(), active.end(),
				  [](const LiveRange *a, const LiveRange *b) {
					  return a->end < b->end;
				  });
	} else {
		// Need to spill
		spillAtInterval(range);
	}
}

void LinearScanAllocator::expireOldIntervals(uint32_t currentStart) {
	auto it = active.begin();
	while (it != active.end()) {
		if ((*it)->end < currentStart) {
			// This interval has expired, free its register
			freePhysReg((*it)->physReg, (*it)->isFloat);
			it = active.erase(it);
		} else {
			++it;
		}
	}
}

void LinearScanAllocator::spillAtInterval(LiveRange &range) {
	if (active.empty()) {
		// No active intervals, must spill the current range
		range.spillSlot = allocateSpillSlot(8);
		return;
	}

	// Find the interval with the latest end point
	LiveRange *spillCandidate = active.back();

	if (spillCandidate->end > range.end) {
		// Spill the interval that ends latest
		range.physReg = spillCandidate->physReg;
		spillCandidate->physReg = X86Reg::None;
		spillCandidate->spillSlot = allocateSpillSlot(8);

		// Update active list
		active.pop_back();
		active.push_back(&range);

		std::sort(active.begin(), active.end(),
				  [](const LiveRange *a, const LiveRange *b) {
					  return a->end < b->end;
				  });
	} else {
		// Spill the current interval
		range.spillSlot = allocateSpillSlot(8);
	}
}

X86Reg LinearScanAllocator::allocatePhysReg(bool isFloat, X86Reg preferred) {
	auto &freeSet = isFloat ? freeXMMRegs : freeGPRegs;

	if (freeSet.empty()) {
		return X86Reg::None;
	}

	// Try preferred register first
	if (preferred != X86Reg::None && freeSet.count(preferred)) {
		freeSet.erase(preferred);
		return preferred;
	}

	// Otherwise, pick any free register
	// Prefer caller-saved to avoid save/restore overhead
	auto &available = isFloat ? availableXMMRegs : availableGPRegs;
	auto &calleeSaved = isFloat ? calleeSavedXMM : calleeSavedGP;

	for (X86Reg reg : available) {
		if (freeSet.count(reg) &&
			std::find(calleeSaved.begin(), calleeSaved.end(), reg) == calleeSaved.end()) {
			freeSet.erase(reg);
			return reg;
		}
	}

	// Fall back to any available register
	X86Reg reg = *freeSet.begin();
	freeSet.erase(reg);
	return reg;
}

void LinearScanAllocator::freePhysReg(X86Reg reg, bool isFloat) {
	if (reg == X86Reg::None)
		return;

	if (isFloat) {
		freeXMMRegs.insert(reg);
	} else {
		freeGPRegs.insert(reg);
	}
}

int32_t LinearScanAllocator::allocateSpillSlot(uint32_t size) {
	int32_t slot = nextSpillOffset;
	nextSpillOffset -= static_cast<int32_t>((size + 7) & ~7); // 8-byte aligned
	return slot;
}

bool LinearScanAllocator::isCalleeSaved(X86Reg reg) {
	for (X86Reg cs : calleeSavedGP) {
		if (cs == reg)
			return true;
	}
	for (X86Reg cs : calleeSavedXMM) {
		if (cs == reg)
			return true;
	}
	return false;
}

//==============================================================================
// Register Coalescer Implementation
//==============================================================================

void RegisterCoalescer::coalesce() {
	// Initialize each register as its own representative
	for (auto &block : function->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->result) {
				coalescedRegs[inst->result->regId] = inst->result->regId;
			}
			for (auto *op : inst->operands) {
				if (op->kind == mir::MIRValueKind::VirtualReg) {
					coalescedRegs[op->regId] = op->regId;
				}
			}
		}
	}

	// Build live ranges for interference checking
	LivenessAnalysis liveness(function);
	liveness.compute();
	auto ranges = liveness.buildLiveRanges();

	// Find copy instructions and try to coalesce
	for (auto &block : function->basicBlocks) {
		for (auto &inst : block->instructions) {
			if (inst->opcode == mir::MIROpcode::Copy && inst->result) {
				auto *src = inst->operands[0];
				if (src->kind == mir::MIRValueKind::VirtualReg) {
					uint32_t dstReg = inst->result->regId;
					uint32_t srcReg = src->regId;

					if (canCoalesce(dstReg, srcReg, ranges)) {
						unionRegs(dstReg, srcReg);
					}
				}
			}
		}
	}
}

uint32_t RegisterCoalescer::getRepresentative(uint32_t regId) {
	if (coalescedRegs.find(regId) == coalescedRegs.end()) {
		return regId;
	}

	// Path compression
	if (coalescedRegs[regId] != regId) {
		coalescedRegs[regId] = getRepresentative(coalescedRegs[regId]);
	}
	return coalescedRegs[regId];
}

bool RegisterCoalescer::canCoalesce(uint32_t reg1, uint32_t reg2,
									const std::vector<LiveRange> &ranges) {
	// Find the live ranges
	const LiveRange *lr1 = nullptr;
	const LiveRange *lr2 = nullptr;

	for (const auto &lr : ranges) {
		if (lr.regId == reg1)
			lr1 = &lr;
		if (lr.regId == reg2)
			lr2 = &lr;
	}

	if (!lr1 || !lr2)
		return false;

	// Can't coalesce if ranges overlap (except at a single point for copies)
	if (lr1->overlaps(*lr2)) {
		// Allow if they only overlap at the copy point
		// This is a simplified check
		return false;
	}

	return true;
}

void RegisterCoalescer::unionRegs(uint32_t reg1, uint32_t reg2) {
	uint32_t rep1 = getRepresentative(reg1);
	uint32_t rep2 = getRepresentative(reg2);

	if (rep1 != rep2) {
		// Union by choosing smaller representative
		if (rep1 < rep2) {
			coalescedRegs[rep2] = rep1;
		} else {
			coalescedRegs[rep1] = rep2;
		}
	}
}

//==============================================================================
// Spill Code Inserter Implementation
//==============================================================================

void SpillCodeInserter::insertSpillCode() {
	for (auto &block : function->basicBlocks) {
		insertLoadsForBlock(block.get());
		insertStoresForBlock(block.get());
	}
}

void SpillCodeInserter::insertLoadsForBlock(mir::MIRBasicBlock *block) {
	// For each instruction, check if any operand is spilled
	// If so, we need to load it before the instruction
	// (This is handled in code generation now, but could be done here for cleaner separation)
}

void SpillCodeInserter::insertStoresForBlock(mir::MIRBasicBlock *block) {
	// For each instruction that defines a spilled value,
	// we need to store it after the instruction
	// (This is handled in code generation now, but could be done here for cleaner separation)
}

} // namespace backend
