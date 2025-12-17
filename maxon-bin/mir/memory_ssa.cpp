// memory_ssa.cpp - MemorySSA Implementation
// Provides memory state tracking in SSA form for optimization passes.

#include "memory_ssa.h"
#include "../logger.h"
#include <algorithm>
#include <queue>
#include <sstream>

namespace mir {

//==============================================================================
// MemoryAccess::toString
//==============================================================================

std::string MemoryAccess::toString() const {
	std::ostringstream ss;
	ss << "MemoryAccess(" << id << ")";
	return ss.str();
}

std::string MemoryDef::toString() const {
	std::ostringstream ss;
	ss << id << " = MemoryDef(";
	if (definingAccess) {
		ss << definingAccess->id;
	} else {
		ss << "liveOnEntry";
	}
	ss << ")";
	if (memoryInst) {
		ss << " ; " << MIRInstruction::opcodeToString(memoryInst->opcode);
	}
	return ss.str();
}

std::string MemoryUse::toString() const {
	std::ostringstream ss;
	ss << "MemoryUse(";
	if (definingAccess) {
		ss << definingAccess->id;
	} else {
		ss << "liveOnEntry";
	}
	ss << ")";
	if (memoryInst) {
		ss << " ; " << MIRInstruction::opcodeToString(memoryInst->opcode);
	}
	return ss.str();
}

std::string MemoryPhi::toString() const {
	std::ostringstream ss;
	ss << id << " = MemoryPhi(";
	bool first = true;
	for (const auto &[access, pred] : incomingValues) {
		if (!first)
			ss << ", ";
		first = false;
		ss << "[";
		if (access) {
			ss << access->id;
		} else {
			ss << "?";
		}
		ss << ", " << pred->name << "]";
	}
	ss << ")";
	return ss.str();
}

//==============================================================================
// MemorySSA Constructor
//==============================================================================

MemorySSA::MemorySSA(MIRFunction &func) : function_(func) {
	liveOnEntry_ = std::make_unique<LiveOnEntry>();
}

//==============================================================================
// Build MemorySSA
//==============================================================================

void MemorySSA::build() {
	if (valid_)
		return;

	// Clear any previous state
	accesses_.clear();
	instToAccess_.clear();
	blockToMemoryPhi_.clear();
	currentDef_.clear();
	idom_.clear();
	domFrontier_.clear();
	predecessors_.clear();
	successors_.clear();
	nextId_ = 1;

	// Build CFG if needed
	buildCFG();

	// Compute dominators
	computeDominators();

	// Compute dominance frontier
	computeDominanceFrontier();

	// Place MemoryPhi nodes at dominance frontier
	placeMemoryPhis();

	// Rename pass to connect defs and uses
	if (!function_.basicBlocks.empty()) {
		renamePass();
	}

	valid_ = true;
}

//==============================================================================
// Build CFG
//==============================================================================

void MemorySSA::buildCFG() {
	// Initialize empty lists for all blocks
	for (auto &block : function_.basicBlocks) {
		predecessors_[block.get()] = {};
		successors_[block.get()] = {};
	}

	// Scan terminators to build CFG
	for (auto &block : function_.basicBlocks) {
		if (block->instructions.empty())
			continue;

		auto &terminator = block->instructions.back();

		switch (terminator->opcode) {
		case MIROpcode::Br:
			if (!terminator->operands.empty() && terminator->operands[0]->blockRef) {
				MIRBasicBlock *target = terminator->operands[0]->blockRef;
				successors_[block.get()].push_back(target);
				predecessors_[target].push_back(block.get());
			}
			break;

		case MIROpcode::CondBr:
			if (terminator->operands.size() >= 3) {
				if (terminator->operands[1] && terminator->operands[1]->blockRef) {
					MIRBasicBlock *trueTarget = terminator->operands[1]->blockRef;
					successors_[block.get()].push_back(trueTarget);
					predecessors_[trueTarget].push_back(block.get());
				}
				if (terminator->operands[2] && terminator->operands[2]->blockRef) {
					MIRBasicBlock *falseTarget = terminator->operands[2]->blockRef;
					successors_[block.get()].push_back(falseTarget);
					predecessors_[falseTarget].push_back(block.get());
				}
			}
			break;

		default:
			// Ret, RetVoid have no successors
			break;
		}
	}
}

//==============================================================================
// Compute Dominators (Cooper, Harvey, Kennedy algorithm)
//==============================================================================

void MemorySSA::computeDominators() {
	if (function_.basicBlocks.empty())
		return;

	MIRBasicBlock *entry = function_.basicBlocks[0].get();

	// Build reverse postorder
	std::vector<MIRBasicBlock *> rpo;
	std::unordered_set<MIRBasicBlock *> visited;

	std::function<void(MIRBasicBlock *)> postorderDFS = [&](MIRBasicBlock *block) {
		if (visited.count(block))
			return;
		visited.insert(block);
		for (auto *succ : successors_[block]) {
			postorderDFS(succ);
		}
		rpo.push_back(block);
	};

	postorderDFS(entry);
	std::reverse(rpo.begin(), rpo.end());

	// Map blocks to their RPO index
	std::unordered_map<MIRBasicBlock *, int> rpoIndex;
	for (int i = 0; i < static_cast<int>(rpo.size()); i++) {
		rpoIndex[rpo[i]] = i;
	}

	// Initialize dominators
	for (auto *block : rpo) {
		idom_[block] = nullptr;
	}
	idom_[entry] = entry;

	// Intersect function for dominator computation
	auto intersect = [&](MIRBasicBlock *b1, MIRBasicBlock *b2) -> MIRBasicBlock * {
		MIRBasicBlock *finger1 = b1;
		MIRBasicBlock *finger2 = b2;
		while (finger1 != finger2) {
			while (rpoIndex[finger1] > rpoIndex[finger2]) {
				finger1 = idom_[finger1];
			}
			while (rpoIndex[finger2] > rpoIndex[finger1]) {
				finger2 = idom_[finger2];
			}
		}
		return finger1;
	};

	// Iterate until convergence
	bool changed = true;
	while (changed) {
		changed = false;
		for (auto *block : rpo) {
			if (block == entry)
				continue;

			MIRBasicBlock *newIdom = nullptr;
			for (auto *pred : predecessors_[block]) {
				if (idom_[pred] != nullptr) {
					if (newIdom == nullptr) {
						newIdom = pred;
					} else {
						newIdom = intersect(newIdom, pred);
					}
				}
			}

			if (idom_[block] != newIdom) {
				idom_[block] = newIdom;
				changed = true;
			}
		}
	}
}

//==============================================================================
// Compute Dominance Frontier
//==============================================================================

void MemorySSA::computeDominanceFrontier() {
	// Initialize empty frontiers
	for (auto &block : function_.basicBlocks) {
		domFrontier_[block.get()] = {};
	}

	// For each block, check if it's in the dominance frontier of any predecessor
	for (auto &block : function_.basicBlocks) {
		MIRBasicBlock *b = block.get();
		auto &preds = predecessors_[b];

		if (preds.size() >= 2) {
			// b is a join point
			for (auto *pred : preds) {
				MIRBasicBlock *runner = pred;
				while (runner != nullptr && runner != idom_[b]) {
					domFrontier_[runner].insert(b);
					runner = idom_[runner];
				}
			}
		}
	}
}

//==============================================================================
// Place Memory Phis at Dominance Frontier
//==============================================================================

void MemorySSA::placeMemoryPhis() {
	// Collect all blocks that contain memory-writing instructions
	std::unordered_set<MIRBasicBlock *> defBlocks;

	for (auto &block : function_.basicBlocks) {
		for (auto &inst : block->instructions) {
			if (isMemoryWrite(inst.get())) {
				defBlocks.insert(block.get());
				break;
			}
		}
	}

	// Iteratively add MemoryPhi nodes at dominance frontier
	std::unordered_set<MIRBasicBlock *> hasPhiFor;
	std::queue<MIRBasicBlock *> worklist;

	for (auto *block : defBlocks) {
		worklist.push(block);
	}

	while (!worklist.empty()) {
		MIRBasicBlock *block = worklist.front();
		worklist.pop();

		for (auto *dfBlock : domFrontier_[block]) {
			if (hasPhiFor.find(dfBlock) == hasPhiFor.end()) {
				// Create MemoryPhi for this block
				createMemoryPhi(dfBlock);
				hasPhiFor.insert(dfBlock);

				// If this block wasn't already a def block, add it to worklist
				if (defBlocks.find(dfBlock) == defBlocks.end()) {
					worklist.push(dfBlock);
				}
			}
		}
	}
}

//==============================================================================
// Rename Pass - Connect defs and uses
//==============================================================================

void MemorySSA::renamePass() {
	// Start with LiveOnEntry
	MemoryAccess *startDef = liveOnEntry_.get();

	// Process entry block
	if (!function_.basicBlocks.empty()) {
		renameBlock(function_.basicBlocks[0].get(), startDef);
	}
}

void MemorySSA::renameBlock(MIRBasicBlock *block, MemoryAccess *incomingDef) {
	MemoryAccess *currentDef = incomingDef;

	// If this block has a MemoryPhi, it becomes the current def
	auto phiIt = blockToMemoryPhi_.find(block);
	if (phiIt != blockToMemoryPhi_.end()) {
		currentDef = phiIt->second;
	}

	// Process each instruction
	for (auto &inst : block->instructions) {
		if (isMemoryRead(inst.get())) {
			// Create MemoryUse
			MemoryUse *use = createMemoryUse(inst.get());
			use->definingAccess = currentDef;

			// Extract loaded pointer
			if (inst->opcode == MIROpcode::Load && !inst->operands.empty()) {
				use->loadedPointer = inst->operands[0];
			}
		}

		if (isMemoryWrite(inst.get())) {
			// Create MemoryDef
			MemoryDef *def = createMemoryDef(inst.get());
			def->definingAccess = currentDef;

			// Extract stored pointer and value for stores
			if (inst->opcode == MIROpcode::Store && inst->operands.size() >= 2) {
				def->storedValue = inst->operands[0];
				def->storedPointer = inst->operands[1];
			}

			currentDef = def;
		}
	}

	// Record the current def for this block (for phi operands)
	currentDef_[block] = currentDef;

	// Process successors in dominator tree
	for (auto &otherBlock : function_.basicBlocks) {
		if (idom_[otherBlock.get()] == block && otherBlock.get() != block) {
			renameBlock(otherBlock.get(), currentDef);
		}
	}

	// Fill in MemoryPhi operands in successors
	for (auto *succ : successors_[block]) {
		auto succPhiIt = blockToMemoryPhi_.find(succ);
		if (succPhiIt != blockToMemoryPhi_.end()) {
			succPhiIt->second->addIncoming(currentDef, block);
		}
	}
}

//==============================================================================
// Create Memory Access Nodes
//==============================================================================

MemoryDef *MemorySSA::createMemoryDef(MIRInstruction *inst) {
	auto def = std::make_unique<MemoryDef>(nextId_++);
	def->memoryInst = inst;
	def->block = inst->parent;

	MemoryDef *ptr = def.get();
	instToAccess_[inst] = ptr;
	accesses_.push_back(std::move(def));

	return ptr;
}

MemoryUse *MemorySSA::createMemoryUse(MIRInstruction *inst) {
	auto use = std::make_unique<MemoryUse>(nextId_++);
	use->memoryInst = inst;
	use->block = inst->parent;

	MemoryUse *ptr = use.get();
	instToAccess_[inst] = ptr;
	accesses_.push_back(std::move(use));

	return ptr;
}

MemoryPhi *MemorySSA::createMemoryPhi(MIRBasicBlock *block) {
	auto phi = std::make_unique<MemoryPhi>(nextId_++);
	phi->block = block;

	MemoryPhi *ptr = phi.get();
	blockToMemoryPhi_[block] = ptr;
	accesses_.push_back(std::move(phi));

	return ptr;
}

//==============================================================================
// Memory Read/Write Classification
//==============================================================================

bool MemorySSA::isMemoryWrite(MIRInstruction *inst) const {
	switch (inst->opcode) {
	case MIROpcode::Store:
		return true;
	case MIROpcode::Call:
	case MIROpcode::CallIndirect:
		return callMayWriteMemory(inst);
	default:
		return false;
	}
}

bool MemorySSA::isMemoryRead(MIRInstruction *inst) const {
	switch (inst->opcode) {
	case MIROpcode::Load:
		return true;
	case MIROpcode::Call:
	case MIROpcode::CallIndirect:
		return callMayReadMemory(inst);
	default:
		return false;
	}
}

bool MemorySSA::callMayWriteMemory(MIRInstruction *callInst) const {
	// Check per-call attributes first
	if (callInst->callDoesNotWriteMemory) {
		return false;
	}

	// Check callee function attributes
	if (callInst->calleeFunc) {
		if (callInst->calleeFunc->doesNotWriteMemory) {
			return false;
		}
	}

	// Conservative: assume calls may write memory
	return true;
}

bool MemorySSA::callMayReadMemory(MIRInstruction *callInst) const {
	// Check per-call attributes first
	if (callInst->callDoesNotReadMemory) {
		return false;
	}

	// Check callee function attributes
	if (callInst->calleeFunc) {
		if (callInst->calleeFunc->doesNotReadMemory) {
			return false;
		}
	}

	// Conservative: assume calls may read memory
	return true;
}

//==============================================================================
// Query Methods
//==============================================================================

MemoryAccess *MemorySSA::getMemoryAccess(MIRInstruction *inst) const {
	auto it = instToAccess_.find(inst);
	return (it != instToAccess_.end()) ? it->second : nullptr;
}

MemoryAccess *MemorySSA::getDefiningAccess(MemoryUse *use) const {
	return use ? use->definingAccess : nullptr;
}

MemoryDef *MemorySSA::getClobberingMemoryAccess(MemoryUse *use) const {
	if (!use || !use->loadedPointer)
		return nullptr;

	// Walk the def chain looking for a store to the same location
	MemoryAccess *current = use->definingAccess;
	MIRValue *loadPtr = use->loadedPointer;
	MIRValue *loadObj = getUnderlyingObject(loadPtr);

	while (current) {
		if (current->isLiveOnEntry()) {
			// Reached function entry - no clobbering store found
			return nullptr;
		}

		if (current->isDef()) {
			MemoryDef *def = static_cast<MemoryDef *>(current);

			// Check if this def writes to the same location
			if (def->storedPointer) {
				MIRValue *storeObj = getUnderlyingObject(def->storedPointer);
				if (storeObj == loadObj) {
					// Found a store to the same underlying object
					return def;
				}
			}

			// For calls, conservatively assume they clobber if they may write
			if (def->memoryInst &&
				(def->memoryInst->opcode == MIROpcode::Call ||
				 def->memoryInst->opcode == MIROpcode::CallIndirect)) {
				// Check if call may alias with our load
				// Conservative: if call may write any memory, it clobbers
				return def;
			}

			// Continue walking
			current = def->definingAccess;
		} else if (current->isPhi()) {
			// For phis, we'd need to check all incoming paths
			// For now, conservatively return nullptr (unknown)
			// A more sophisticated implementation would check all paths
			return nullptr;
		} else {
			break;
		}
	}

	return nullptr;
}

//==============================================================================
// Alias Analysis
//==============================================================================

MIRValue *MemorySSA::getUnderlyingObject(MIRValue *ptr) const {
	if (!ptr)
		return nullptr;

	// Walk through GEP instructions to find the base pointer
	while (ptr) {
		// Check if this is a virtual register defined by a GEP
		if (ptr->kind == MIRValueKind::VirtualReg && ptr->definingInst) {
			MIRInstruction *defInst = ptr->definingInst;
			if (defInst->opcode == MIROpcode::GetElementPtr && !defInst->operands.empty()) {
				// GEP's first operand is the base pointer
				ptr = defInst->operands[0];
				continue;
			}
		}

		// Not a GEP - this is the underlying object
		break;
	}

	return ptr;
}

bool MemorySSA::mayAlias(MIRValue *ptr1, MIRValue *ptr2) const {
	if (!ptr1 || !ptr2)
		return true; // Conservative

	MIRValue *obj1 = getUnderlyingObject(ptr1);
	MIRValue *obj2 = getUnderlyingObject(ptr2);

	// If both trace to the same underlying object, they may alias
	if (obj1 == obj2)
		return true;

	// If both are distinct allocas, they don't alias
	if (obj1 && obj2 &&
		obj1->kind == MIRValueKind::VirtualReg && obj1->definingInst &&
		obj2->kind == MIRValueKind::VirtualReg && obj2->definingInst) {
		MIRInstruction *def1 = obj1->definingInst;
		MIRInstruction *def2 = obj2->definingInst;
		if (def1->opcode == MIROpcode::Alloca && def2->opcode == MIROpcode::Alloca) {
			// Two different allocas don't alias
			return false;
		}
	}

	// Conservative: may alias
	return true;
}

bool MemorySSA::mustAlias(MIRValue *ptr1, MIRValue *ptr2) const {
	if (!ptr1 || !ptr2)
		return false;

	// Same pointer value - definitely alias
	if (ptr1 == ptr2)
		return true;

	// Collect GEP chain for each pointer
	// We need to compare: base pointer and all indices must be identical
	struct GEPInfo {
		MIRValue *base = nullptr;
		std::vector<MIRValue *> indices;
	};

	auto collectGEPChain = [](MIRValue *ptr) -> GEPInfo {
		GEPInfo info;
		std::vector<MIRValue *> indices;

		while (ptr) {
			if (ptr->kind == MIRValueKind::VirtualReg && ptr->definingInst) {
				MIRInstruction *defInst = ptr->definingInst;
				if (defInst->opcode == MIROpcode::GetElementPtr && !defInst->operands.empty()) {
					// Collect indices (all operands after the first are indices)
					for (size_t i = 1; i < defInst->operands.size(); ++i) {
						indices.push_back(defInst->operands[i]);
					}
					ptr = defInst->operands[0];
					continue;
				}
			}
			// Found base pointer
			info.base = ptr;
			break;
		}

		// Reverse indices (we collected them backwards)
		info.indices.assign(indices.rbegin(), indices.rend());
		return info;
	};

	GEPInfo info1 = collectGEPChain(ptr1);
	GEPInfo info2 = collectGEPChain(ptr2);

	// Must have same base pointer
	if (info1.base != info2.base)
		return false;

	// Must have same number of indices
	if (info1.indices.size() != info2.indices.size())
		return false;

	// All indices must be identical (same MIRValue or same constant value)
	for (size_t i = 0; i < info1.indices.size(); ++i) {
		MIRValue *idx1 = info1.indices[i];
		MIRValue *idx2 = info2.indices[i];

		if (idx1 == idx2)
			continue;

		// Check if both are the same constant
		if (idx1->isConstant() && idx2->isConstant() &&
			idx1->intValue == idx2->intValue) {
			continue;
		}

		// Indices differ - not a must-alias
		return false;
	}

	return true;
}

//==============================================================================
// Debug Printing
//==============================================================================

void MemorySSA::print() const {
	Logger &logger = GlobalLogger::instance();
	logger.trace(LogPhase::Opt, "MemorySSA for function @", function_.name, ":");

	for (auto &block : function_.basicBlocks) {
		logger.trace(LogPhase::Opt, "  ", block->name, ":");

		// Print MemoryPhi if present
		auto phiIt = blockToMemoryPhi_.find(block.get());
		if (phiIt != blockToMemoryPhi_.end()) {
			logger.trace(LogPhase::Opt, "    ", phiIt->second->toString());
		}

		// Print memory accesses for each instruction
		for (auto &inst : block->instructions) {
			auto accessIt = instToAccess_.find(inst.get());
			if (accessIt != instToAccess_.end()) {
				logger.trace(LogPhase::Opt, "    ", accessIt->second->toString());
			}
		}
	}
}

//==============================================================================
// MemorySSACache
//==============================================================================

MemorySSA &MemorySSACache::getMemorySSA(MIRFunction &func) {
	auto it = cache_.find(&func);
	if (it == cache_.end() || !it->second->isValid()) {
		auto mssa = std::make_unique<MemorySSA>(func);
		mssa->build();
		cache_[&func] = std::move(mssa);
	}
	return *cache_[&func];
}

void MemorySSACache::invalidate(MIRFunction &func) {
	auto it = cache_.find(&func);
	if (it != cache_.end()) {
		it->second->invalidate();
	}
}

void MemorySSACache::clear() {
	cache_.clear();
}

} // namespace mir
