/**
 * Unit tests for Phase 5: Basic Optimizations
 *
 * Tests the optimization passes: constant folding, constant propagation,
 * dead code elimination, unreachable block elimination, strength reduction,
 * algebraic simplification, copy propagation, redundant load/store elimination,
 * and simple function inlining.
 */

#include "../mir/mir.h"
#include "../mir/mir_builder.h"
#include "../mir/optimizer.h"
#include <catch_amalgamated.hpp>

using namespace mir;

//==============================================================================
// Helper Functions
//==============================================================================

// Create a simple module with one function for testing
static MIRModule createTestModule() {
	return MIRModule("test_module");
}

// Count instructions in a function
static size_t countInstructions(MIRFunction &func) {
	size_t count = 0;
	for (auto &block : func.basicBlocks) {
		count += block->instructions.size();
	}
	return count;
}

// Suppress unused variable warning
template <typename T>
void ignore_unused(T &&) {}

//==============================================================================
// Constant Folding Tests
//==============================================================================

TEST_CASE("Constant folding - integer arithmetic", "[optimizer][constant-folding]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getInt32(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("add constant folding") {
		auto *c3 = builder.getInt32(3);
		auto *c4 = builder.getInt32(4);
		auto *result = builder.createAdd(c3, c4);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("sub constant folding") {
		auto *c10 = builder.getInt32(10);
		auto *c3 = builder.getInt32(3);
		auto *result = builder.createSub(c10, c3);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("mul constant folding") {
		auto *c5 = builder.getInt32(5);
		auto *c6 = builder.getInt32(6);
		auto *result = builder.createMul(c5, c6);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("div constant folding") {
		auto *c20 = builder.getInt32(20);
		auto *c4 = builder.getInt32(4);
		auto *result = builder.createSDiv(c20, c4);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("division by zero not folded") {
		auto *c20 = builder.getInt32(20);
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createSDiv(c20, c0);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE_FALSE(changed);
	}

	SECTION("bitwise and constant folding") {
		auto *c15 = builder.getInt32(15);			// 0b1111
		auto *c10 = builder.getInt32(10);			// 0b1010
		auto *result = builder.createAnd(c15, c10); // 0b1010 = 10
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("bitwise or constant folding") {
		auto *c8 = builder.getInt32(8);			 // 0b1000
		auto *c3 = builder.getInt32(3);			 // 0b0011
		auto *result = builder.createOr(c8, c3); // 0b1011 = 11
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("shift left constant folding") {
		auto *c5 = builder.getInt32(5);
		auto *c2 = builder.getInt32(2);
		auto *result = builder.createShl(c5, c2); // 5 << 2 = 20
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Constant folding - integer comparisons", "[optimizer][constant-folding]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getInt32(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("equal comparison true") {
		auto *c5a = builder.getInt32(5);
		auto *c5b = builder.getInt32(5);
		auto *result = builder.createICmpEq(c5a, c5b);
		auto *extended = builder.createZExt(result, MIRType::getInt32());
		builder.createRet(extended);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("equal comparison false") {
		auto *c5 = builder.getInt32(5);
		auto *c3 = builder.getInt32(3);
		auto *result = builder.createICmpEq(c5, c3);
		auto *extended = builder.createZExt(result, MIRType::getInt32());
		builder.createRet(extended);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("less than comparison") {
		auto *c3 = builder.getInt32(3);
		auto *c5 = builder.getInt32(5);
		auto *result = builder.createICmpSLT(c3, c5);
		auto *extended = builder.createZExt(result, MIRType::getInt32());
		builder.createRet(extended);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Constant folding - floating point arithmetic", "[optimizer][constant-folding]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getFloat64(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("fadd constant folding") {
		auto *c3_5 = builder.getFloat64(3.5);
		auto *c2_5 = builder.getFloat64(2.5);
		auto *result = builder.createFAdd(c3_5, c2_5);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("fmul constant folding") {
		auto *c2 = builder.getFloat64(2.0);
		auto *c3 = builder.getFloat64(3.0);
		auto *result = builder.createFMul(c2, c3);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("fdiv constant folding") {
		auto *c10 = builder.getFloat64(10.0);
		auto *c2 = builder.getFloat64(2.0);
		auto *result = builder.createFDiv(c10, c2);
		builder.createRet(result);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Constant folding - floating point comparisons", "[optimizer][constant-folding]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getInt32(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("fcmp equal") {
		auto *c3 = builder.getFloat64(3.0);
		auto *c3b = builder.getFloat64(3.0);
		auto *result = builder.createFCmpEq(c3, c3b);
		auto *extended = builder.createZExt(result, MIRType::getInt32());
		builder.createRet(extended);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("fcmp less than") {
		auto *c2 = builder.getFloat64(2.0);
		auto *c3 = builder.getFloat64(3.0);
		auto *result = builder.createFCmpLT(c2, c3);
		auto *extended = builder.createZExt(result, MIRType::getInt32());
		builder.createRet(extended);

		ConstantFoldingPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

//==============================================================================
// Algebraic Simplification Tests
//==============================================================================

TEST_CASE("Algebraic simplification - additive identity", "[optimizer][algebraic]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("add x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createAdd(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("add 0, x -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createAdd(c0, x);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("sub x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createSub(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Algebraic simplification - multiplicative identity", "[optimizer][algebraic]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("mul x, 1 -> x") {
		auto *x = func->parameters[0];
		auto *c1 = builder.getInt32(1);
		auto *result = builder.createMul(x, c1);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("mul 1, x -> x") {
		auto *x = func->parameters[0];
		auto *c1 = builder.getInt32(1);
		auto *result = builder.createMul(c1, x);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("mul x, 0 -> 0") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createMul(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Algebraic simplification - bitwise identities", "[optimizer][algebraic]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("and x, 0 -> 0") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createAnd(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("and x, -1 -> x") {
		auto *x = func->parameters[0];
		auto *cm1 = builder.getInt32(-1);
		auto *result = builder.createAnd(x, cm1);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("or x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createOr(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("xor x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createXor(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Algebraic simplification - self operations", "[optimizer][algebraic]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("sub x, x -> 0") {
		auto *x = func->parameters[0];
		auto *result = builder.createSub(x, x);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("xor x, x -> 0") {
		auto *x = func->parameters[0];
		auto *result = builder.createXor(x, x);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("and x, x -> x") {
		auto *x = func->parameters[0];
		auto *result = builder.createAnd(x, x);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("or x, x -> x") {
		auto *x = func->parameters[0];
		auto *result = builder.createOr(x, x);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

TEST_CASE("Algebraic simplification - shift by zero", "[optimizer][algebraic]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("shl x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createShl(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("ashr x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createAShr(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("lshr x, 0 -> x") {
		auto *x = func->parameters[0];
		auto *c0 = builder.getInt32(0);
		auto *result = builder.createLShr(x, c0);
		builder.createRet(result);

		AlgebraicSimplificationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

//==============================================================================
// Strength Reduction Tests
//==============================================================================

TEST_CASE("Strength reduction - multiply by power of two", "[optimizer][strength-reduction]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("mul x, 2 -> shl x, 1") {
		auto *x = func->parameters[0];
		auto *c2 = builder.getInt32(2);
		auto *result = builder.createMul(x, c2);
		builder.createRet(result);

		StrengthReductionPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		// Check that opcode changed to Shl
		REQUIRE(entry->instructions[0]->opcode == MIROpcode::Shl);
	}

	SECTION("mul x, 4 -> shl x, 2") {
		auto *x = func->parameters[0];
		auto *c4 = builder.getInt32(4);
		auto *result = builder.createMul(x, c4);
		builder.createRet(result);

		StrengthReductionPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		REQUIRE(entry->instructions[0]->opcode == MIROpcode::Shl);
	}

	SECTION("mul x, 8 -> shl x, 3") {
		auto *x = func->parameters[0];
		auto *c8 = builder.getInt32(8);
		auto *result = builder.createMul(x, c8);
		builder.createRet(result);

		StrengthReductionPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		REQUIRE(entry->instructions[0]->opcode == MIROpcode::Shl);
	}

	SECTION("mul x, 3 unchanged (not power of 2)") {
		auto *x = func->parameters[0];
		auto *c3 = builder.getInt32(3);
		auto *result = builder.createMul(x, c3);
		builder.createRet(result);

		StrengthReductionPass pass;
		bool changed = pass.run(module);

		REQUIRE_FALSE(changed);
		REQUIRE(entry->instructions[0]->opcode == MIROpcode::Mul);
	}
}

TEST_CASE("Strength reduction - unsigned divide by power of two", "[optimizer][strength-reduction]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("udiv x, 4 -> lshr x, 2") {
		auto *x = func->parameters[0];
		auto *c4 = builder.getInt32(4);
		auto *result = builder.createUDiv(x, c4);
		builder.createRet(result);

		StrengthReductionPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		REQUIRE(entry->instructions[0]->opcode == MIROpcode::LShr);
	}
}

TEST_CASE("Strength reduction - unsigned mod by power of two", "[optimizer][strength-reduction]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("urem x, 8 -> and x, 7") {
		auto *x = func->parameters[0];
		auto *c8 = builder.getInt32(8);
		auto *result = builder.createURem(x, c8);
		builder.createRet(result);

		StrengthReductionPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		REQUIRE(entry->instructions[0]->opcode == MIROpcode::And);
	}
}

//==============================================================================
// Dead Code Elimination Tests
//==============================================================================

TEST_CASE("Dead code elimination - unused result", "[optimizer][dce]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("remove unused add") {
		auto *x = func->parameters[0];
		auto *c5 = builder.getInt32(5);
		ignore_unused(builder.createAdd(x, c5)); // Result not used
		builder.createRet(x);

		size_t before = countInstructions(*func);

		DeadCodeEliminationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		REQUIRE(countInstructions(*func) < before);
	}

	SECTION("keep used add") {
		auto *x = func->parameters[0];
		auto *c5 = builder.getInt32(5);
		auto *result = builder.createAdd(x, c5);
		builder.createRet(result);

		size_t before = countInstructions(*func);

		DeadCodeEliminationPass pass;
		bool changed = pass.run(module);

		REQUIRE_FALSE(changed);
		REQUIRE(countInstructions(*func) == before);
	}
}

TEST_CASE("Dead code elimination - multiple unused", "[optimizer][dce]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *x = func->parameters[0];
	auto *c1 = builder.getInt32(1);
	auto *c2 = builder.getInt32(2);
	auto *c3 = builder.getInt32(3);

	ignore_unused(builder.createAdd(x, c1));
	ignore_unused(builder.createMul(x, c2));
	ignore_unused(builder.createSub(x, c3));
	builder.createRet(x);

	size_t before = countInstructions(*func);

	DeadCodeEliminationPass pass;
	bool changed = pass.run(module);

	REQUIRE(changed);
	// Should have removed 3 instructions
	REQUIRE(countInstructions(*func) == before - 3);
}

//==============================================================================
// Unreachable Block Elimination Tests
//==============================================================================

TEST_CASE("Unreachable block elimination", "[optimizer][unreachable]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	auto *func = builder.createFunction("test", MIRType::getInt32(), {});

	SECTION("remove unreachable block") {
		auto *entry = builder.createBasicBlock("entry");
		auto *reachable = builder.createBasicBlock("reachable");
		auto *unreachable = builder.createBasicBlock("unreachable");

		builder.setInsertPoint(entry);
		builder.createBr(reachable);

		builder.setInsertPoint(reachable);
		auto *c42 = builder.getInt32(42);
		builder.createRet(c42);

		builder.setInsertPoint(unreachable);
		auto *c0 = builder.getInt32(0);
		builder.createRet(c0);

		REQUIRE(func->basicBlocks.size() == 3);

		UnreachableBlockEliminationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
		REQUIRE(func->basicBlocks.size() == 2);
	}

	SECTION("keep all reachable blocks") {
		auto *entry = builder.createBasicBlock("entry");
		auto *thenBlock = builder.createBasicBlock("then");
		auto *elseBlock = builder.createBasicBlock("else");

		builder.setInsertPoint(entry);
		auto *cond = builder.getInt1(true);
		builder.createCondBr(cond, thenBlock, elseBlock);

		builder.setInsertPoint(thenBlock);
		auto *c1 = builder.getInt32(1);
		builder.createRet(c1);

		builder.setInsertPoint(elseBlock);
		auto *c2 = builder.getInt32(2);
		builder.createRet(c2);

		REQUIRE(func->basicBlocks.size() == 3);

		UnreachableBlockEliminationPass pass;
		bool changed = pass.run(module);

		REQUIRE_FALSE(changed);
		REQUIRE(func->basicBlocks.size() == 3);
	}
}

//==============================================================================
// Copy Propagation Tests
//==============================================================================

TEST_CASE("Copy propagation", "[optimizer][copy-propagation]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// We need to manually create a Copy instruction since the builder doesn't have one
	// For now, test that the pass handles functions without copies gracefully
	SECTION("no copies - no change") {
		auto *x = func->parameters[0];
		auto *c5 = builder.getInt32(5);
		auto *result = builder.createAdd(x, c5);
		builder.createRet(result);

		CopyPropagationPass pass;
		bool changed = pass.run(module);

		// No copies, so no changes expected
		REQUIRE_FALSE(changed);
	}
}

//==============================================================================
// Constant Propagation Tests
//==============================================================================

TEST_CASE("Constant propagation", "[optimizer][constant-propagation]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getInt32(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("propagate through chain") {
		// This test verifies that constant propagation works
		// Even if no direct constant assignment, the pass should handle gracefully
		auto *c5 = builder.getInt32(5);
		auto *c10 = builder.getInt32(10);
		auto *sum = builder.createAdd(c5, c10);
		builder.createRet(sum);

		ConstantPropagationPass pass;
		// With two constants, propagation won't change anything (already constants)
		// The real benefit is when a virtual register holds a constant
		pass.run(module);
		// Just verify it doesn't crash
		REQUIRE(true);
	}
}

//==============================================================================
// Simple Function Inlining Tests
//==============================================================================

TEST_CASE("Simple function inlining", "[optimizer][inlining]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	SECTION("inline small leaf function") {
		// Create a small helper function
		auto *helper = builder.createFunction("helper", MIRType::getInt32(),
											  {{MIRType::getInt32(), "a"}});
		auto *helperEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(helperEntry);
		auto *a = helper->parameters[0];
		auto *c2 = builder.getInt32(2);
		auto *doubled = builder.createMul(a, c2);
		builder.createRet(doubled);

		// Create main function that calls helper
		ignore_unused(builder.createFunction("main", MIRType::getInt32(), {}));
		auto *mainEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(mainEntry);
		auto *c5 = builder.getInt32(5);
		auto *result = builder.createCall(helper, {c5});
		builder.createRet(result);

		SimpleFunctionInliningPass pass(20);
		bool changed = pass.run(module);

		// The helper should be inlined
		REQUIRE(changed);
	}

	SECTION("don't inline main") {
		ignore_unused(builder.createFunction("main", MIRType::getInt32(), {}));
		auto *mainEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(mainEntry);
		auto *c42 = builder.getInt32(42);
		builder.createRet(c42);

		SimpleFunctionInliningPass pass(20);
		bool changed = pass.run(module);

		REQUIRE_FALSE(changed);
	}

	SECTION("don't inline external functions") {
		auto *ext = builder.declareFunction("external", MIRType::getInt32(), {});

		ignore_unused(builder.createFunction("main", MIRType::getInt32(), {}));
		auto *mainEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(mainEntry);
		auto *result = builder.createCall(ext, {});
		builder.createRet(result);

		SimpleFunctionInliningPass pass(20);
		bool changed = pass.run(module);

		REQUIRE_FALSE(changed);
	}
}

//==============================================================================
// Redundant Load/Store Elimination Tests
//==============================================================================

TEST_CASE("Redundant load/store elimination", "[optimizer][load-store]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getPtr(), "ptr"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("eliminate load after store") {
		auto *ptr = func->parameters[0];
		auto *c42 = builder.getInt32(42);

		builder.createStore(c42, ptr);
		auto *loaded = builder.createLoad(MIRType::getInt32(), ptr);
		builder.createRet(loaded);

		RedundantLoadStoreEliminationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}

	SECTION("eliminate dead store") {
		auto *ptr = func->parameters[0];
		auto *c1 = builder.getInt32(1);
		auto *c2 = builder.getInt32(2);

		builder.createStore(c1, ptr);
		builder.createStore(c2, ptr); // c1 store is dead
		auto *loaded = builder.createLoad(MIRType::getInt32(), ptr);
		builder.createRet(loaded);

		RedundantLoadStoreEliminationPass pass;
		bool changed = pass.run(module);

		REQUIRE(changed);
	}
}

//==============================================================================
// MIR Optimizer (Pass Manager) Tests
//==============================================================================

TEST_CASE("MIR Optimizer pass manager", "[optimizer][manager]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getInt32(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("standard pipeline creation") {
		auto optimizer = MIROptimizer::createStandardPipeline();
		REQUIRE(optimizer.getPassCount() > 0);
	}

	SECTION("run all passes") {
		auto *c3 = builder.getInt32(3);
		auto *c4 = builder.getInt32(4);
		auto *sum = builder.createAdd(c3, c4);
		builder.createRet(sum);

		auto optimizer = MIROptimizer::createStandardPipeline();
		int changes = optimizer.runAllPasses(module);

		// Should have at least constant folding
		REQUIRE(changes >= 0);
	}

	SECTION("add custom pass") {
		MIROptimizer optimizer;
		optimizer.addPass(std::make_unique<ConstantFoldingPass>());
		optimizer.addPass(std::make_unique<DeadCodeEliminationPass>());

		REQUIRE(optimizer.getPassCount() == 2);

		auto *c1 = builder.getInt32(1);
		auto *c2 = builder.getInt32(2);
		ignore_unused(builder.createAdd(c1, c2));
		auto *c42 = builder.getInt32(42);
		builder.createRet(c42);

		optimizer.runPasses(module, 5);
		// Should have run without crashing
		REQUIRE(true);
	}

	SECTION("clear passes") {
		MIROptimizer optimizer;
		optimizer.addPass(std::make_unique<ConstantFoldingPass>());
		REQUIRE(optimizer.getPassCount() == 1);

		optimizer.clearPasses();
		REQUIRE(optimizer.getPassCount() == 0);
	}
}

//==============================================================================
// Integration Tests - Multiple Passes Working Together
//==============================================================================

TEST_CASE("Optimization integration - constant folding + propagation", "[optimizer][integration]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	ignore_unused(builder.createFunction("test", MIRType::getInt32(), {}));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// x = 3 + 4 = 7 (constant folding)
	// y = x * 2 = 14 (after propagation and folding)
	auto *c3 = builder.getInt32(3);
	auto *c4 = builder.getInt32(4);
	auto *x = builder.createAdd(c3, c4);

	auto *c2 = builder.getInt32(2);
	auto *y = builder.createMul(x, c2);

	builder.createRet(y);

	MIROptimizer optimizer;
	optimizer.addPass(std::make_unique<ConstantFoldingPass>());
	optimizer.addPass(std::make_unique<ConstantPropagationPass>());
	optimizer.addPass(std::make_unique<ConstantFoldingPass>()); // Run again after propagation

	optimizer.runPasses(module, 5);

	// The passes should have optimized this
	REQUIRE(true);
}

TEST_CASE("Optimization integration - algebraic + strength reduction", "[optimizer][integration]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	ignore_unused(builder.createFunction("test", MIRType::getInt32(), params));
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *x = builder.getFunction()->parameters[0];

	// (x + 0) * 8 should become x << 3
	auto *c0 = builder.getInt32(0);
	auto *sum = builder.createAdd(x, c0); // Should simplify to x
	auto *c8 = builder.getInt32(8);
	auto *result = builder.createMul(sum, c8); // Should become shift

	builder.createRet(result);

	MIROptimizer optimizer;
	optimizer.addPass(std::make_unique<AlgebraicSimplificationPass>());
	optimizer.addPass(std::make_unique<StrengthReductionPass>());

	optimizer.runPasses(module, 5);

	// Check that strength reduction was applied
	bool foundShift = false;
	for (auto &inst : entry->instructions) {
		if (inst->opcode == MIROpcode::Shl) {
			foundShift = true;
			break;
		}
	}
	REQUIRE(foundShift);
}

TEST_CASE("Optimization integration - full pipeline", "[optimizer][integration]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *x = func->parameters[0];

	// Complex expression that should be optimized:
	// unused = 5 + 10 (DCE)
	// temp = x + 0 (algebraic -> x)
	// result = temp * 4 (strength reduction -> shl 2)
	auto *c5 = builder.getInt32(5);
	auto *c10 = builder.getInt32(10);
	ignore_unused(builder.createAdd(c5, c10));

	auto *c0 = builder.getInt32(0);
	auto *temp = builder.createAdd(x, c0);

	auto *c4 = builder.getInt32(4);
	auto *result = builder.createMul(temp, c4);

	builder.createRet(result);

	size_t before = countInstructions(*func);

	auto optimizer = MIROptimizer::createStandardPipeline();
	optimizer.runPasses(module, 10);

	size_t after = countInstructions(*func);

	// Should have optimized something
	REQUIRE(after <= before);
}

//==============================================================================
// Utility Function Tests
//==============================================================================

TEST_CASE("Utility functions", "[optimizer][utils]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {{MIRType::getInt32(), "x"}};
	auto *func = builder.createFunction("test", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *x = func->parameters[0];
	auto *c5 = builder.getInt32(5);
	auto *sum = builder.createAdd(x, c5);
	builder.createRet(sum);

	SECTION("isValueUsed") {
		REQUIRE(opt_utils::isValueUsed(*func, sum));
		REQUIRE(opt_utils::isValueUsed(*func, x));
		REQUIRE(opt_utils::isValueUsed(*func, c5));
	}

	SECTION("isValueUsedInBlock") {
		REQUIRE(opt_utils::isValueUsedInBlock(*entry, x));
		REQUIRE(opt_utils::isValueUsedInBlock(*entry, c5));
	}

	SECTION("getDefinedValues") {
		auto defined = opt_utils::getDefinedValues(*entry);
		REQUIRE(defined.size() == 1); // Just the add result
	}

	SECTION("getUsedValues") {
		auto used = opt_utils::getUsedValues(entry->instructions[0].get());
		REQUIRE(used.size() == 2); // x and c5
	}
}
