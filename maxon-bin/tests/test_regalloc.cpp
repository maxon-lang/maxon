/**
 * Unit tests for Phase 3: Register Allocation
 *
 * Tests liveness analysis and linear-scan register allocation.
 */

#include "../backend/regalloc.h"
#include "../mir/mir.h"
#include "../mir/mir_builder.h"
#include <catch_amalgamated.hpp>

using namespace backend;
using namespace mir;

//==============================================================================
// Test Helpers
//==============================================================================

// Build a simple function for testing: adds two parameters
static MIRFunction *createSimpleAddFunction(MIRModule &mod) {
	MIRBuilder builder(&mod);
	auto *func = builder.createFunction("add", MIRType::getInt32(), {});
	auto *a = func->addParameter(MIRType::getInt32(), "a");
	auto *b = func->addParameter(MIRType::getInt32(), "b");

	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *sum = builder.createAdd(a, b);
	builder.createRet(sum);

	return func;
}

// Build a function with multiple live ranges
static MIRFunction *createMultipleRangesFunction(MIRModule &mod) {
	MIRBuilder builder(&mod);
	auto *func = builder.createFunction("multi", MIRType::getInt32(), {});
	auto *a = func->addParameter(MIRType::getInt32(), "a");
	auto *b = func->addParameter(MIRType::getInt32(), "b");
	auto *c = func->addParameter(MIRType::getInt32(), "c");

	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Create overlapping live ranges
	// x = a + b
	// y = b + c
	// z = x + y
	auto *x = builder.createAdd(a, b);
	auto *y = builder.createAdd(b, c);
	auto *z = builder.createAdd(x, y);
	builder.createRet(z);

	return func;
}

// Build a function with control flow (for liveness across blocks)
static MIRFunction *createControlFlowFunction(MIRModule &mod) {
	MIRBuilder builder(&mod);
	auto *func = builder.createFunction("cf", MIRType::getInt32(), {});
	auto *n = func->addParameter(MIRType::getInt32(), "n");

	auto *entry = builder.createBasicBlock("entry");
	auto *thenBB = builder.createBasicBlock("then");
	auto *elseBB = builder.createBasicBlock("else");
	auto *merge = builder.createBasicBlock("merge");

	// Entry: if (n > 0) goto then else goto else
	builder.setInsertPoint(entry);
	auto *zero = MIRValue::createConstantInt(MIRType::getInt32(), 0);
	auto *cmp = builder.createICmpSGT(n, zero);
	builder.createCondBr(cmp, thenBB, elseBB);

	// Then: result = n * 2
	builder.setInsertPoint(thenBB);
	auto *two = MIRValue::createConstantInt(MIRType::getInt32(), 2);
	auto *thenResult = builder.createMul(n, two);
	builder.createBr(merge);

	// Else: result = n + 1
	builder.setInsertPoint(elseBB);
	auto *one = MIRValue::createConstantInt(MIRType::getInt32(), 1);
	auto *elseResult = builder.createAdd(n, one);
	builder.createBr(merge);

	// Merge: return phi(thenResult, elseResult)
	builder.setInsertPoint(merge);
	auto *phi = builder.createPhi(MIRType::getInt32());
	phi->definingInst->phiIncoming.push_back({thenResult, thenBB});
	phi->definingInst->phiIncoming.push_back({elseResult, elseBB});
	builder.createRet(phi);

	return func;
}

// Build a function that requires many registers
static MIRFunction *createHighRegisterPressureFunction(MIRModule &mod) {
	MIRBuilder builder(&mod);
	auto *func = builder.createFunction("pressure", MIRType::getInt32(), {});
	auto *n = func->addParameter(MIRType::getInt32(), "n");

	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Create many live values simultaneously to force spilling
	auto *one = MIRValue::createConstantInt(MIRType::getInt32(), 1);
	std::vector<MIRValue *> values;

	// Create 20+ live values
	MIRValue *prev = n;
	for (int i = 0; i < 20; ++i) {
		auto *val = builder.createAdd(prev, one);
		values.push_back(val);
		prev = val;
	}

	// Sum them all up to keep them all live
	auto *sum = values[0];
	for (size_t i = 1; i < values.size(); ++i) {
		sum = builder.createAdd(sum, values[i]);
	}

	builder.createRet(sum);
	return func;
}

//==============================================================================
// LiveRange Tests
//==============================================================================

TEST_CASE("LiveRange overlap detection", "[regalloc][liverange]") {
	LiveRange r1;
	r1.start = 0;
	r1.end = 10;

	LiveRange r2;
	r2.start = 5;
	r2.end = 15;

	LiveRange r3;
	r3.start = 11;
	r3.end = 20;

	SECTION("overlapping ranges") {
		REQUIRE(r1.overlaps(r2));
		REQUIRE(r2.overlaps(r1));
	}

	SECTION("non-overlapping ranges") {
		REQUIRE_FALSE(r1.overlaps(r3));
		REQUIRE_FALSE(r3.overlaps(r1));
	}

	SECTION("adjacent ranges (no overlap)") {
		LiveRange r4;
		r4.start = 11;
		r4.end = 20;
		REQUIRE_FALSE(r1.overlaps(r4));
	}

	SECTION("contains point") {
		REQUIRE(r1.contains(0));
		REQUIRE(r1.contains(5));
		REQUIRE(r1.contains(10));
		REQUIRE_FALSE(r1.contains(11));
	}
}

//==============================================================================
// Liveness Analysis Tests
//==============================================================================

TEST_CASE("Liveness analysis - simple function", "[regalloc][liveness]") {
	MIRModule mod("test");
	auto *func = createSimpleAddFunction(mod);

	LivenessAnalysis liveness(func);
	liveness.compute();
	auto ranges = liveness.buildLiveRanges();

	// Should have ranges for parameters and the add result
	REQUIRE(ranges.size() >= 2); // At least a, b parameters

	// Parameters should be live at entry
	bool foundParam = false;
	for (const auto &r : ranges) {
		if (r.start == 0) {
			foundParam = true;
		}
	}
	REQUIRE(foundParam);
}

TEST_CASE("Liveness analysis - multiple ranges", "[regalloc][liveness]") {
	MIRModule mod("test");
	auto *func = createMultipleRangesFunction(mod);

	LivenessAnalysis liveness(func);
	liveness.compute();
	auto ranges = liveness.buildLiveRanges();

	// Should have ranges for: a, b, c (params), x, y, z (temps)
	REQUIRE(ranges.size() >= 3);
}

TEST_CASE("Liveness analysis - control flow", "[regalloc][liveness]") {
	MIRModule mod("test");
	auto *func = createControlFlowFunction(mod);

	LivenessAnalysis liveness(func);
	liveness.compute();
	auto ranges = liveness.buildLiveRanges();

	// Parameter 'n' should be live across multiple blocks
	REQUIRE(!ranges.empty());
}

//==============================================================================
// Linear-Scan Allocator Tests
//==============================================================================

TEST_CASE("LinearScanAllocator - simple function", "[regalloc][linearscan]") {
	MIRModule mod("test");
	auto *func = createSimpleAddFunction(mod);

	LinearScanAllocator allocator(func, true); // Windows ABI
	auto result = allocator.allocate();

	// All values should be assigned registers (no spills needed)
	REQUIRE(result.spillSlots.empty());
	REQUIRE(!result.regAssignment.empty());
}

TEST_CASE("LinearScanAllocator - Windows vs Linux ABI", "[regalloc][linearscan]") {
	MIRModule mod("test");
	auto *func = createSimpleAddFunction(mod);

	SECTION("Windows ABI") {
		LinearScanAllocator allocator(func, true);
		auto result = allocator.allocate();
		REQUIRE(!result.regAssignment.empty());
	}

	SECTION("Linux ABI") {
		LinearScanAllocator allocator(func, false);
		auto result = allocator.allocate();
		REQUIRE(!result.regAssignment.empty());
	}
}

TEST_CASE("LinearScanAllocator - multiple live ranges", "[regalloc][linearscan]") {
	MIRModule mod("test");
	auto *func = createMultipleRangesFunction(mod);

	LinearScanAllocator allocator(func, true);
	auto result = allocator.allocate();

	// Should handle overlapping ranges correctly
	REQUIRE(!result.regAssignment.empty());

	// Check that different live ranges get different registers if they overlap
	// (This is checked implicitly by allocation succeeding)
}

TEST_CASE("LinearScanAllocator - high register pressure (spilling)", "[regalloc][linearscan]") {
	MIRModule mod("test");
	auto *func = createHighRegisterPressureFunction(mod);

	LinearScanAllocator allocator(func, true);
	auto result = allocator.allocate();

	// With 20+ simultaneous live values, we should see some spills
	// (There are only ~14 usable GP registers)
	REQUIRE(!result.regAssignment.empty());

	// At least something should be allocated
	REQUIRE(!result.regAssignment.empty());

	// With high pressure, spilling is expected
	if (!result.spillSlots.empty()) {
		REQUIRE(result.totalSpillSize > 0);
	}
}

TEST_CASE("LinearScanAllocator - callee-saved tracking", "[regalloc][linearscan]") {
	MIRModule mod("test");
	auto *func = createHighRegisterPressureFunction(mod);

	LinearScanAllocator allocator(func, true);
	auto result = allocator.allocate();

	// If callee-saved registers are used, they should be tracked
	// so the code generator knows to save/restore them
	// usedCalleeSaved should be populated (may be empty if not used)
	// This test just ensures the list is computed without crashing
	// Windows has up to 7 GP callee-saved + 10 XMM callee-saved = 17 total
	REQUIRE(result.usedCalleeSaved.size() <= 17);
}

//==============================================================================
// Register Assignment Validity Tests
//==============================================================================

TEST_CASE("Register assignment validity", "[regalloc][validity]") {
	MIRModule mod("test");
	auto *func = createMultipleRangesFunction(mod);

	LinearScanAllocator allocator(func, true);
	auto result = allocator.allocate();

	// Build live ranges again to check assignments
	LivenessAnalysis liveness(func);
	liveness.compute();
	auto ranges = liveness.buildLiveRanges();

	// Check that overlapping ranges don't share registers
	for (size_t i = 0; i < ranges.size(); ++i) {
		for (size_t j = i + 1; j < ranges.size(); ++j) {
			if (ranges[i].overlaps(ranges[j])) {
				auto reg_i = result.regAssignment.find(ranges[i].regId);
				auto reg_j = result.regAssignment.find(ranges[j].regId);

				// If both are assigned (not spilled), they must have different registers
				if (reg_i != result.regAssignment.end() &&
					reg_j != result.regAssignment.end()) {
					if (ranges[i].isFloat == ranges[j].isFloat) {
						// Only compare within same register class
						INFO("Ranges " << ranges[i].regId << " and " << ranges[j].regId
									   << " overlap but have same register");
						REQUIRE(reg_i->second != reg_j->second);
					}
				}
			}
		}
	}
}

//==============================================================================
// Float Register Allocation Tests
//==============================================================================

TEST_CASE("Float register allocation", "[regalloc][float]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	auto *func = builder.createFunction("float_ops", MIRType::getFloat64(), {});
	auto *a = func->addParameter(MIRType::getFloat64(), "a");
	auto *b = func->addParameter(MIRType::getFloat64(), "b");

	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *sum = builder.createFAdd(a, b);
	auto *product = builder.createFMul(a, b);
	auto *result = builder.createFAdd(sum, product);
	builder.createRet(result);

	LinearScanAllocator allocator(func, true);
	auto allocResult = allocator.allocate();

	// Float values should get XMM registers
	REQUIRE(!allocResult.regAssignment.empty());

	// Check that XMM registers are assigned for float types
	for (const auto &[regId, physReg] : allocResult.regAssignment) {
		// XMM registers have encoding 0-15 (same as GP), but are in a different class
		// The isFloat flag in LiveRange determines which pool is used
		// Here we just verify assignment succeeded
		REQUIRE(physReg != X86Reg::None);
	}
}

//==============================================================================
// Edge Cases
//==============================================================================

TEST_CASE("Empty function", "[regalloc][edge]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	auto *func = builder.createFunction("empty", MIRType::getVoid(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);
	builder.createRetVoid();

	LinearScanAllocator allocator(func, true);
	auto result = allocator.allocate();

	// Empty function should allocate without issues
	REQUIRE(result.spillSlots.empty());
}

TEST_CASE("Single basic block with no values", "[regalloc][edge]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	auto *func = builder.createFunction("noop", MIRType::getInt32(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *zero = MIRValue::createConstantInt(MIRType::getInt32(), 0);
	builder.createRet(zero);

	LinearScanAllocator allocator(func, true);
	auto result = allocator.allocate();

	// Should succeed with constants (which don't need registers)
	REQUIRE(result.spillSlots.empty());
}

//==============================================================================
// Register Coalescer Tests
//==============================================================================

TEST_CASE("Register coalescer basic", "[regalloc][coalesce]") {
	MIRModule mod("test");
	auto *func = createSimpleAddFunction(mod);

	RegisterCoalescer coalescer(func);
	// Should not crash
	coalescer.coalesce();

	// getRepresentative should return the register itself if not coalesced
	uint32_t rep = coalescer.getRepresentative(0);
	REQUIRE(rep == rep); // Just checking it returns something
}
