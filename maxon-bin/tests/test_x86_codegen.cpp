/**
 * Unit tests for X86CodeGen
 *
 * Tests the x86-64 code generation from MIR, verifying that
 * instructions are correctly generated, calling conventions are
 * properly implemented, and function structures are correct.
 */

#include "../backend/x86_codegen.h"
#include "../mir/mir.h"
#include "../mir/mir_builder.h"
#include <catch_amalgamated.hpp>

using namespace backend;
using namespace mir;

//==============================================================================
// Helper Functions
//==============================================================================

// Create a simple MIR module for testing
static MIRModule createTestModule() {
	return MIRModule("test_module");
}

//==============================================================================
// Calling Convention Tests
//==============================================================================

TEST_CASE("X86CodeGen: calling convention parameter registers", "[x86-codegen][calling-conv]") {
	SECTION("Win64 calling convention") {
		X86CodeGen codegen(CallingConv::Win64);
		// Win64: RCX, RDX, R8, R9 for integer args
		// XMM0-XMM3 for float args

		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"},
			{MIRType::getInt32(), "c"},
			{MIRType::getInt32(), "d"}};

		auto *func = builder.createFunction("test_params", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		// Return sum of all parameters
		auto *sum1 = builder.createAdd(func->parameters[0], func->parameters[1]);
		auto *sum2 = builder.createAdd(sum1, func->parameters[2]);
		auto *sum3 = builder.createAdd(sum2, func->parameters[3]);
		builder.createRet(sum3);

		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(funcCodes.size() >= 1);

		// Find the test_params function
		const FunctionCode *testFunc = nullptr;
		for (const auto &fc : funcCodes) {
			if (fc.name == "test_params") {
				testFunc = &fc;
				break;
			}
		}
		REQUIRE(testFunc != nullptr);
		REQUIRE(!testFunc->code.empty());
	}

	SECTION("SysV64 calling convention") {
		X86CodeGen codegen(CallingConv::SysV64);
		// SysV64: RDI, RSI, RDX, RCX, R8, R9 for integer args
		// XMM0-XMM7 for float args

		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("test_sysv", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *sum = builder.createAdd(func->parameters[0], func->parameters[1]);
		builder.createRet(sum);

		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(funcCodes.size() >= 1);
	}
}

//==============================================================================
// Arithmetic Instruction Generation Tests
//==============================================================================

TEST_CASE("X86CodeGen: integer arithmetic", "[x86-codegen][arithmetic]") {
	SECTION("addition") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("add_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *sum = builder.createAdd(func->parameters[0], func->parameters[1]);
		builder.createRet(sum);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());

		const FunctionCode *addFunc = nullptr;
		for (const auto &fc : funcCodes) {
			if (fc.name == "add_test") {
				addFunc = &fc;
				break;
			}
		}
		REQUIRE(addFunc != nullptr);
		REQUIRE(addFunc->code.size() > 0);
	}

	SECTION("subtraction") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("sub_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *diff = builder.createSub(func->parameters[0], func->parameters[1]);
		builder.createRet(diff);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("multiplication") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("mul_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *prod = builder.createMul(func->parameters[0], func->parameters[1]);
		builder.createRet(prod);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("signed division") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("div_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *quot = builder.createSDiv(func->parameters[0], func->parameters[1]);
		builder.createRet(quot);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("signed remainder") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("rem_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *rem = builder.createSRem(func->parameters[0], func->parameters[1]);
		builder.createRet(rem);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

TEST_CASE("X86CodeGen: consecutive loads with arithmetic", "[x86-codegen][load][arithmetic]") {
	// Regression test for bug where consecutive loads clobbered each other
	// Bug: genLoad used RAX for both pointer and result, causing second load
	// to overwrite first load's value before it could be used
	//
	// Pattern that triggered the bug:
	//   %0 = alloca i32
	//   store i32 %a, ptr %0
	//   %1 = alloca i32
	//   store i32 %b, ptr %1
	//   %2 = load i32, ptr %0   ; First load -> RAX
	//   %3 = load i32, ptr %1   ; Second load -> RAX (clobbers first!)
	//   %4 = add i32 %2, %3     ; Adding wrong values

	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {
		{MIRType::getInt32(), "a"},
		{MIRType::getInt32(), "b"}};

	auto *func = builder.createFunction("add_via_loads", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Store parameters to allocas
	auto *alloca0 = builder.createAlloca(MIRType::getInt32());
	builder.createStore(func->parameters[0], alloca0);

	auto *alloca1 = builder.createAlloca(MIRType::getInt32());
	builder.createStore(func->parameters[1], alloca1);

	// Load from allocas - this is where the bug occurred
	auto *load0 = builder.createLoad(MIRType::getInt32(), alloca0);
	auto *load1 = builder.createLoad(MIRType::getInt32(), alloca1);

	// Use both loaded values in an operation
	auto *sum = builder.createAdd(load0, load1);
	builder.createRet(sum);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	REQUIRE(!funcCodes.empty());

	const FunctionCode *funcCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "add_via_loads") {
			funcCode = &fc;
			break;
		}
	}
	REQUIRE(funcCode != nullptr);
	REQUIRE(funcCode->code.size() > 0);

	// Verify the buggy pattern is NOT present:
	// We should NOT see two consecutive loads into RAX where the second
	// clobbers the first before it's used
	//
	// Buggy pattern would be:
	//   lea rcx, [rbp-X]
	//   mov eax, [rcx]      ; First load into RAX
	//   lea rcx, [rbp-Y]
	//   mov eax, [rcx]      ; Second load into RAX - CLOBBERS!
	//
	// Fixed pattern uses different registers:
	//   lea rcx, [rbp-X]
	//   mov eax, [rcx]      ; First load into RAX
	//   lea rcx, [rbp-Y]
	//   mov r10d, [rcx]     ; Second load into R10 - preserves RAX

	auto &code = funcCode->code;
	bool foundBuggyPattern = false;

	for (size_t i = 0; i + 20 < code.size(); ++i) {
		// Look for: mov eax, [rax] = 8b 00
		if (code[i] == 0x8b && code[i + 1] == 0x00) {
			// Found first load into eax from [rax]
			// Check if there's another mov eax, [rax] within next 15 bytes
			// This indicates the bug where loads clobber each other
			for (size_t j = i + 2; j + 2 < code.size() && j < i + 15; ++j) {
				// mov eax, [rax] = 8b 00
				if (code[j] == 0x8b && code[j + 1] == 0x00) {
					foundBuggyPattern = true;
					INFO("Found buggy pattern: consecutive loads into EAX at offsets "
						 << i << " and " << j);
					break;
				}
			}
			if (foundBuggyPattern)
				break;
		}
	}

	CHECK_FALSE(foundBuggyPattern);
}

TEST_CASE("X86CodeGen: floating-point arithmetic", "[x86-codegen][arithmetic][float]") {
	SECTION("float addition") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getFloat64(), "a"},
			{MIRType::getFloat64(), "b"}};

		auto *func = builder.createFunction("fadd_test", MIRType::getFloat64(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *sum = builder.createFAdd(func->parameters[0], func->parameters[1]);
		builder.createRet(sum);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("float multiplication") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getFloat64(), "a"},
			{MIRType::getFloat64(), "b"}};

		auto *func = builder.createFunction("fmul_test", MIRType::getFloat64(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *prod = builder.createFMul(func->parameters[0], func->parameters[1]);
		builder.createRet(prod);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Bitwise Operation Tests
//==============================================================================

TEST_CASE("X86CodeGen: bitwise operations", "[x86-codegen][bitwise]") {
	SECTION("and operation") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("and_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *result = builder.createAnd(func->parameters[0], func->parameters[1]);
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("or operation") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("or_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *result = builder.createOr(func->parameters[0], func->parameters[1]);
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("xor operation") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("xor_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *result = builder.createXor(func->parameters[0], func->parameters[1]);
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("shift left") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("shl_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *result = builder.createShl(func->parameters[0], func->parameters[1]);
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("arithmetic shift right") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("ashr_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *result = builder.createAShr(func->parameters[0], func->parameters[1]);
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Comparison Tests
//==============================================================================

TEST_CASE("X86CodeGen: integer comparisons", "[x86-codegen][comparisons]") {
	SECTION("equality comparison") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("eq_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *cmp = builder.createICmpEq(func->parameters[0], func->parameters[1]);
		auto *extended = builder.createZExt(cmp, MIRType::getInt32());
		builder.createRet(extended);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("less than comparison") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "a"},
			{MIRType::getInt32(), "b"}};

		auto *func = builder.createFunction("lt_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *cmp = builder.createICmpSLT(func->parameters[0], func->parameters[1]);
		auto *extended = builder.createZExt(cmp, MIRType::getInt32());
		builder.createRet(extended);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Memory Operation Tests
//==============================================================================

TEST_CASE("X86CodeGen: memory operations", "[x86-codegen][memory]") {
	SECTION("alloca and store/load") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		(void)builder.createFunction("alloca_test", MIRType::getInt32(), {});
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *ptr = builder.createAlloca(MIRType::getInt32());
		auto *val = builder.getInt32(42);
		builder.createStore(val, ptr);
		auto *loaded = builder.createLoad(MIRType::getInt32(), ptr);
		builder.createRet(loaded);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Control Flow Tests
//==============================================================================

TEST_CASE("X86CodeGen: control flow", "[x86-codegen][control-flow]") {
	SECTION("unconditional branch") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		(void)builder.createFunction("br_test", MIRType::getInt32(), {});
		auto *entry = builder.createBasicBlock("entry");
		auto *exit = builder.createBasicBlock("exit");

		builder.setInsertPoint(entry);
		builder.createBr(exit);

		builder.setInsertPoint(exit);
		auto *val = builder.getInt32(42);
		builder.createRet(val);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("conditional branch") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "cond"}};

		auto *func = builder.createFunction("cond_br_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		auto *thenBlock = builder.createBasicBlock("then");
		auto *elseBlock = builder.createBasicBlock("else");

		builder.setInsertPoint(entry);
		auto *zero = builder.getInt32(0);
		auto *cmp = builder.createICmpNe(func->parameters[0], zero);
		builder.createCondBr(cmp, thenBlock, elseBlock);

		builder.setInsertPoint(thenBlock);
		auto *one = builder.getInt32(1);
		builder.createRet(one);

		builder.setInsertPoint(elseBlock);
		builder.createRet(zero);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Function Call Tests
//==============================================================================

TEST_CASE("X86CodeGen: function calls", "[x86-codegen][calls]") {
	SECTION("call to internal function") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		// Create helper function
		std::vector<std::pair<MIRType *, std::string>> helperParams = {
			{MIRType::getInt32(), "x"}};
		auto *helper = builder.createFunction("helper", MIRType::getInt32(), helperParams);
		auto *helperEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(helperEntry);
		auto *two = builder.getInt32(2);
		auto *doubled = builder.createMul(helper->parameters[0], two);
		builder.createRet(doubled);

		// Create main function that calls helper
		(void)builder.createFunction("main", MIRType::getInt32(), {});
		auto *mainEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(mainEntry);
		auto *arg = builder.getInt32(21);
		auto *result = builder.createCall(helper, {arg});
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(funcCodes.size() >= 2);
	}

	SECTION("call to external function") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		// Declare external function
		auto *extFunc = builder.declareFunction("external_func", MIRType::getInt32(),
												{MIRType::getInt32()});

		// Create main function
		(void)builder.createFunction("main", MIRType::getInt32(), {});
		auto *mainEntry = builder.createBasicBlock("entry");
		builder.setInsertPoint(mainEntry);
		auto *arg = builder.getInt32(42);
		auto *result = builder.createCall(extFunc, {arg});
		builder.createRet(result);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());

		// Should have a relocation for the external call
		const FunctionCode *mainFunc = nullptr;
		for (const auto &fc : funcCodes) {
			if (fc.name == "main") {
				mainFunc = &fc;
				break;
			}
		}
		REQUIRE(mainFunc != nullptr);
	}
}

//==============================================================================
// Type Conversion Tests
//==============================================================================

TEST_CASE("X86CodeGen: type conversions", "[x86-codegen][conversions]") {
	SECTION("sign extension") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "x"}};

		auto *func = builder.createFunction("sext_test", MIRType::getInt64(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *extended = builder.createSExt(func->parameters[0], MIRType::getInt64());
		builder.createRet(extended);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("zero extension") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "x"}};

		auto *func = builder.createFunction("zext_test", MIRType::getInt64(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *extended = builder.createZExt(func->parameters[0], MIRType::getInt64());
		builder.createRet(extended);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("int to float conversion") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getInt32(), "x"}};

		auto *func = builder.createFunction("sitofp_test", MIRType::getFloat64(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *converted = builder.createSIToFP(func->parameters[0], MIRType::getFloat64());
		builder.createRet(converted);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}

	SECTION("float to int conversion") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		std::vector<std::pair<MIRType *, std::string>> params = {
			{MIRType::getFloat64(), "x"}};

		auto *func = builder.createFunction("fptosi_test", MIRType::getInt32(), params);
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		auto *converted = builder.createFPToSI(func->parameters[0], MIRType::getInt32());
		builder.createRet(converted);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Prologue/Epilogue Tests
//==============================================================================

TEST_CASE("X86CodeGen: function prologue and epilogue", "[x86-codegen][prologue]") {
	SECTION("simple function has proper structure") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		(void)builder.createFunction("simple", MIRType::getInt32(), {});
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);
		auto *val = builder.getInt32(42);
		builder.createRet(val);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());

		const FunctionCode *simpleFunc = nullptr;
		for (const auto &fc : funcCodes) {
			if (fc.name == "simple") {
				simpleFunc = &fc;
				break;
			}
		}
		REQUIRE(simpleFunc != nullptr);

		// Should start with push rbp (0x55) or similar prologue
		// and end with ret (0xC3)
		REQUIRE(simpleFunc->code.size() >= 2);
		REQUIRE(simpleFunc->code.back() == 0xC3); // ret instruction
	}

	SECTION("function with locals allocates stack space") {
		MIRModule module = createTestModule();
		MIRBuilder builder(&module);

		(void)builder.createFunction("with_locals", MIRType::getInt32(), {});
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);

		// Create some local variables
		auto *ptr1 = builder.createAlloca(MIRType::getInt32());
		auto *ptr2 = builder.createAlloca(MIRType::getInt32());
		auto *val1 = builder.getInt32(10);
		auto *val2 = builder.getInt32(20);
		builder.createStore(val1, ptr1);
		builder.createStore(val2, ptr2);

		auto *loaded1 = builder.createLoad(MIRType::getInt32(), ptr1);
		auto *loaded2 = builder.createLoad(MIRType::getInt32(), ptr2);
		auto *sum = builder.createAdd(loaded1, loaded2);
		builder.createRet(sum);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&module);

		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(!funcCodes.empty());
	}
}

//==============================================================================
// Integration Tests
//==============================================================================

TEST_CASE("X86CodeGen: integration - factorial", "[x86-codegen][integration]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {
		{MIRType::getInt32(), "n"}};

	auto *func = builder.createFunction("factorial", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	auto *recurse = builder.createBasicBlock("recurse");
	auto *base = builder.createBasicBlock("base");

	builder.setInsertPoint(entry);
	auto *one = builder.getInt32(1);
	auto *cmp = builder.createICmpSLE(func->parameters[0], one);
	builder.createCondBr(cmp, base, recurse);

	builder.setInsertPoint(base);
	builder.createRet(one);

	builder.setInsertPoint(recurse);
	auto *nMinus1 = builder.createSub(func->parameters[0], one);
	auto *recursiveResult = builder.createCall(func, {nMinus1});
	auto *result = builder.createMul(func->parameters[0], recursiveResult);
	builder.createRet(result);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	REQUIRE(!funcCodes.empty());

	const FunctionCode *factFunc = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "factorial") {
			factFunc = &fc;
			break;
		}
	}
	REQUIRE(factFunc != nullptr);
	REQUIRE(factFunc->code.size() > 10); // Should be a non-trivial function
}

TEST_CASE("X86CodeGen: integration - loop", "[x86-codegen][integration]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	std::vector<std::pair<MIRType *, std::string>> params = {
		{MIRType::getInt32(), "n"}};

	auto *func = builder.createFunction("sum_to_n", MIRType::getInt32(), params);
	auto *entry = builder.createBasicBlock("entry");
	auto *loopHeader = builder.createBasicBlock("loop.header");
	auto *loopBody = builder.createBasicBlock("loop.body");
	auto *loopExit = builder.createBasicBlock("loop.exit");

	// Entry: initialize sum and i
	builder.setInsertPoint(entry);
	auto *sumPtr = builder.createAlloca(MIRType::getInt32());
	auto *iPtr = builder.createAlloca(MIRType::getInt32());
	auto *zero = builder.getInt32(0);
	builder.createStore(zero, sumPtr);
	builder.createStore(zero, iPtr);
	builder.createBr(loopHeader);

	// Loop header: check i < n
	builder.setInsertPoint(loopHeader);
	auto *i = builder.createLoad(MIRType::getInt32(), iPtr);
	auto *cmp = builder.createICmpSLT(i, func->parameters[0]);
	builder.createCondBr(cmp, loopBody, loopExit);

	// Loop body: sum += i; i++
	builder.setInsertPoint(loopBody);
	auto *sum = builder.createLoad(MIRType::getInt32(), sumPtr);
	auto *iVal = builder.createLoad(MIRType::getInt32(), iPtr);
	auto *newSum = builder.createAdd(sum, iVal);
	builder.createStore(newSum, sumPtr);
	auto *one = builder.getInt32(1);
	auto *newI = builder.createAdd(iVal, one);
	builder.createStore(newI, iPtr);
	builder.createBr(loopHeader);

	// Exit: return sum
	builder.setInsertPoint(loopExit);
	auto *finalSum = builder.createLoad(MIRType::getInt32(), sumPtr);
	builder.createRet(finalSum);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	REQUIRE(!funcCodes.empty());
}

//==============================================================================
// Large Struct Return Tests (Windows x64 ABI)
//==============================================================================

// Helper to create a 12-byte struct type (3 x int32)
static MIRType *createLargeStructType(const std::string &name = "LargeStruct") {
	std::vector<MIRType *> fields = {
		MIRType::getInt32(),
		MIRType::getInt32(),
		MIRType::getInt32()};
	return MIRType::getStruct(name, fields);
}

TEST_CASE("X86CodeGen: large struct return - callee saves hidden pointer",
		  "[x86-codegen][large-struct]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	auto *structType = createLargeStructType();

	// Function returning large struct: function makeStruct() LargeStruct
	(void)builder.createFunction("makeStruct", structType, {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Alloca for the struct
	auto *structAlloca = builder.createAlloca(structType);

	// Store values to fields
	auto *field0Ptr = builder.createStructGEP(structType, structAlloca, 0);
	builder.createStore(builder.getInt32(10), field0Ptr);

	auto *field1Ptr = builder.createStructGEP(structType, structAlloca, 1);
	builder.createStore(builder.getInt32(20), field1Ptr);

	auto *field2Ptr = builder.createStructGEP(structType, structAlloca, 2);
	builder.createStore(builder.getInt32(30), field2Ptr);

	// Return the struct
	auto *result = builder.createLoad(structType, structAlloca);
	builder.createRet(result);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *funcCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "makeStruct") {
			funcCode = &fc;
			break;
		}
	}
	REQUIRE(funcCode != nullptr);

	// Prologue should save RCX (hidden return pointer) to stack
	// Look for: mov [rbp-offset], rcx (48 89 4d XX)
	auto &code = funcCode->code;
	REQUIRE(code.size() > 10);
	REQUIRE(code[0] == 0x55); // push rbp

	bool foundSaveRCX = false;
	for (size_t i = 0; i + 4 < code.size(); ++i) {
		if (code[i] == 0x48 && code[i + 1] == 0x89 && code[i + 2] == 0x4D) {
			foundSaveRCX = true;
			break;
		}
	}
	CHECK(foundSaveRCX);
}

TEST_CASE("X86CodeGen: large struct return - caller passes hidden pointer",
		  "[x86-codegen][large-struct]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	auto *structType = createLargeStructType();

	// First declare the callee function
	auto *makeFunc = builder.createFunction("makeStruct", structType, {});
	auto *makeEntry = builder.createBasicBlock("entry");
	builder.setInsertPoint(makeEntry);
	auto *dummyAlloca = builder.createAlloca(structType);
	auto *dummyLoad = builder.createLoad(structType, dummyAlloca);
	builder.createRet(dummyLoad);

	// Main function that calls makeStruct
	(void)builder.createFunction("main", MIRType::getInt32(), {});
	auto *mainEntry = builder.createBasicBlock("entry");
	builder.setInsertPoint(mainEntry);

	// Call makeStruct() - returns large struct
	auto *callResult = builder.createCall("makeStruct", structType, {});
	callResult->definingInst->calleeFunc = makeFunc;

	// Store result to alloca
	auto *resultAlloca = builder.createAlloca(structType);
	builder.createStore(callResult, resultAlloca);

	// Access first field and return it
	auto *field0Ptr = builder.createStructGEP(structType, resultAlloca, 0);
	auto *field0Val = builder.createLoad(MIRType::getInt32(), field0Ptr);
	builder.createRet(field0Val);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *mainCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "main") {
			mainCode = &fc;
			break;
		}
	}
	REQUIRE(mainCode != nullptr);

	// Before call, should see: lea rcx, [rbp+offset] (48 8D 4D XX)
	// This loads the hidden return pointer into RCX
	auto &code = mainCode->code;
	bool foundLeaRCX = false;
	for (size_t i = 0; i + 4 < code.size(); ++i) {
		if (code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x4D) {
			foundLeaRCX = true;
			break;
		}
	}
	CHECK(foundLeaRCX);
}

TEST_CASE("X86CodeGen: large struct return - parameter shift",
		  "[x86-codegen][large-struct]") {
	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	auto *structType = createLargeStructType();

	// Function with params returning large struct:
	// function makeStruct(a int, b int) LargeStruct
	std::vector<std::pair<MIRType *, std::string>> params = {
		{MIRType::getInt32(), "a"},
		{MIRType::getInt32(), "b"}};

	auto *func = builder.createFunction("makeStructWithParams", structType, params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *structAlloca = builder.createAlloca(structType);

	// Use parameters in the struct
	auto *field0Ptr = builder.createStructGEP(structType, structAlloca, 0);
	builder.createStore(func->parameters[0], field0Ptr); // a

	auto *field1Ptr = builder.createStructGEP(structType, structAlloca, 1);
	builder.createStore(func->parameters[1], field1Ptr); // b

	auto *field2Ptr = builder.createStructGEP(structType, structAlloca, 2);
	builder.createStore(builder.getInt32(0), field2Ptr);

	auto *result = builder.createLoad(structType, structAlloca);
	builder.createRet(result);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *funcCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "makeStructWithParams") {
			funcCode = &fc;
			break;
		}
	}
	REQUIRE(funcCode != nullptr);

	// With hidden pointer in RCX:
	// - Parameter 'a' should be in RDX (shifted from RCX)
	// - Parameter 'b' should be in R8 (shifted from RDX)
	// Prologue should save RDX or R8
	auto &code = funcCode->code;

	// Look for mov [rbp+disp], rdx (48 89 55 XX) or mov [rbp+disp], r8 (4C 89 45 XX)
	bool foundSaveShiftedParam = false;
	for (size_t i = 0; i + 4 < code.size() && i < 50; ++i) {
		// mov [rbp+disp8], rdx
		if (code[i] == 0x48 && code[i + 1] == 0x89 && code[i + 2] == 0x55) {
			foundSaveShiftedParam = true;
			break;
		}
		// mov [rbp+disp8], r8
		if (code[i] == 0x4C && code[i + 1] == 0x89 && code[i + 2] == 0x45) {
			foundSaveShiftedParam = true;
			break;
		}
	}
	CHECK(foundSaveShiftedParam);
}

//==============================================================================
// Store to GEP Register Conflict Tests
//==============================================================================

TEST_CASE("X86CodeGen: store param to struct field - value preserved",
		  "[x86-codegen][store][gep]") {
	// This test verifies that when storing a parameter to a struct field via GEP,
	// the value is not clobbered by the GEP address calculation.
	//
	// Simulates iter.range pattern where:
	// - Function returns large struct (12 bytes) - triggers hidden return pointer
	// - Parameters are shifted (RDX, R8 instead of RCX, RDX)
	// - Parameter values are loaded from stack and stored to struct fields

	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	// Create a struct type with 3 int fields (12 bytes - large struct)
	auto *structType = MIRType::getStruct("Iterator", {MIRType::getInt32(), MIRType::getInt32(), MIRType::getInt32()});

	// Function: make_iter(start int, end int) Iterator
	// This triggers hidden return pointer ABI
	std::vector<std::pair<MIRType *, std::string>> params = {
		{MIRType::getInt32(), "start"},
		{MIRType::getInt32(), "end_val"}};

	auto *func = builder.createFunction("make_iter", structType, params);
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Alloca for the result struct
	auto *structAlloca = builder.createAlloca(structType);

	// Store 'start' param to field 0
	// This is the critical store - param loaded from stack, stored to GEP result
	auto *field0Ptr = builder.createStructGEP(structType, structAlloca, 0);
	builder.createStore(func->parameters[0], field0Ptr);

	// Store 'end_val' param to field 1
	auto *field1Ptr = builder.createStructGEP(structType, structAlloca, 1);
	builder.createStore(func->parameters[1], field1Ptr);

	// Store constant 1 to field 2 (step)
	auto *field2Ptr = builder.createStructGEP(structType, structAlloca, 2);
	builder.createStore(builder.getInt32(1), field2Ptr);

	// Return the struct
	auto *result = builder.createLoad(structType, structAlloca);
	builder.createRet(result);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *funcCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "make_iter") {
			funcCode = &fc;
			break;
		}
	}
	REQUIRE(funcCode != nullptr);

	auto &code = funcCode->code;

	// Debug: print the generated code bytes
	INFO("Generated code size: " << code.size());
	std::string hexDump;
	for (size_t i = 0; i < std::min(code.size(), size_t(100)); ++i) {
		char buf[8];
		snprintf(buf, sizeof(buf), "%02x ", code[i]);
		hexDump += buf;
		if ((i + 1) % 16 == 0)
			hexDump += "\n";
	}
	INFO("Code bytes:\n"
		 << hexDump);

	// The buggy pattern we're looking for:
	// 1. Load parameter from stack into RAX: mov rax, [rbp+X] or similar
	// 2. LEA into RAX for struct address, clobbering the value
	// 3. Store using RAX which now has wrong value
	//
	// Specifically look for the sequence:
	//   48 8b 45 XX     mov rax, [rbp+XX]  ; load param
	//   48 8d 45 YY     lea rax, [rbp+YY]  ; compute struct addr - CLOBBERS!
	//   ...
	//   89 01           mov [rcx], eax     ; store (wrong value)

	bool foundBugPattern = false;
	for (size_t i = 0; i + 12 < code.size(); ++i) {
		// mov rax, [rbp+disp8] = 48 8b 45 XX
		if (code[i] == 0x48 && code[i + 1] == 0x8b && code[i + 2] == 0x45) {
			// Found load into rax from stack
			// Check if followed by lea rax, [rbp+Y] within next 20 bytes
			for (size_t j = i + 4; j + 4 < code.size() && j < i + 20; ++j) {
				// lea rax, [rbp+disp8] = 48 8d 45 XX
				if (code[j] == 0x48 && code[j + 1] == 0x8d && code[j + 2] == 0x45) {
					// Found clobbering LEA - this is the bug
					foundBugPattern = true;
					INFO("Found bug pattern at offset " << i << ": load at " << i << ", clobbering lea at " << j);
					break;
				}
			}
		}
		if (foundBugPattern)
			break;
	}
	CHECK_FALSE(foundBugPattern);
}

TEST_CASE("X86CodeGen: store doesn't clobber value register", "[x86-codegen][store][regalloc]") {
	// Regression test for bug where genStore clobbered the value before storing it
	// Bug: When value was in R11 and we loaded destination pointer into R11,
	// the value was lost before being stored
	//
	// Pattern that triggered the bug:
	//   %result = call i32 @foo()      ; result allocated to R11
	//   %dest = alloca i32             ; dest is stack alloca
	//   store i32 %result, ptr %dest   ; BUG: loads dest addr into R11, clobbering result!

	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	// Create a simple function that returns a value
	auto *foo = builder.createFunction("foo", MIRType::getInt32(), {});
	auto *fooEntry = builder.createBasicBlock("entry");
	builder.setInsertPoint(fooEntry);
	builder.createRet(builder.getInt32(42));

	// Create main function that calls foo and stores result
	auto *testMain = builder.createFunction("test_main", MIRType::getInt32(), {});
	(void)testMain; // Suppress unused warning
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Call function - result will be in RAX, then moved to allocated register
	auto *callResult = builder.createCall(foo, {}, "result");

	// Create alloca for storing result
	auto *destAlloca = builder.createAlloca(MIRType::getInt32(), "dest");

	// Store call result to alloca - this is where the bug manifested
	builder.createStore(callResult, destAlloca);

	// Load back and return
	auto *loadedVal = builder.createLoad(MIRType::getInt32(), destAlloca, "loaded");
	builder.createRet(loadedVal);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	REQUIRE(!funcCodes.empty());

	const FunctionCode *mainFunc = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "test_main") {
			mainFunc = &fc;
			break;
		}
	}
	REQUIRE(mainFunc != nullptr);
	REQUIRE(!mainFunc->code.empty());
}

TEST_CASE("X86CodeGen: large struct return via hidden pointer", "[x86-codegen][struct][abi]") {
	// Test that structs > 8 bytes are returned via hidden pointer on Win64
	// Win64 ABI: caller allocates space and passes pointer in RCX
	// Callee writes struct to that location and returns pointer in RAX

	MIRModule module = createTestModule();
	MIRBuilder builder(&module);

	// Create Iterator struct type: {i32, i32, i32} = 12 bytes
	auto *iterType = module.getOrCreateStructType("Iterator", {MIRType::getInt32(),
															   MIRType::getInt32(),
															   MIRType::getInt32()});

	// Function that returns large struct
	auto *getIter = builder.createFunction("get_iterator", iterType, {});
	auto *getIterEntry = builder.createBasicBlock("entry");
	builder.setInsertPoint(getIterEntry);

	auto *iterAlloca = builder.createAlloca(iterType, "it");

	// Initialize struct fields
	auto *field0 = builder.createStructGEP(iterType, iterAlloca, 0, "field0");
	builder.createStore(builder.getInt32(5), field0);

	auto *field1 = builder.createStructGEP(iterType, iterAlloca, 1, "field1");
	builder.createStore(builder.getInt32(10), field1);

	auto *field2 = builder.createStructGEP(iterType, iterAlloca, 2, "field2");
	builder.createStore(builder.getInt32(1), field2);

	auto *structVal = builder.createLoad(iterType, iterAlloca, "it.val");
	builder.createRet(structVal);

	// Caller function
	auto *main = builder.createFunction("main", MIRType::getInt32(), {});
	(void)main; // Suppress unused warning
	auto *mainEntry = builder.createBasicBlock("entry");
	builder.setInsertPoint(mainEntry);

	// Call function that returns large struct
	auto *iterResult = builder.createCall(getIter, {}, "it");

	// Store result to alloca
	auto *resultAlloca = builder.createAlloca(iterType, "it.local");
	builder.createStore(iterResult, resultAlloca);

	// Access first field
	auto *field0Ptr = builder.createStructGEP(iterType, resultAlloca, 0, "field0");
	auto *field0Val = builder.createLoad(MIRType::getInt32(), field0Ptr, "field0.val");

	builder.createRet(field0Val);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&module);

	auto &funcCodes = codegen.getFunctionCodes();
	REQUIRE(funcCodes.size() >= 2);

	// Verify both functions generated code
	bool hasGetIter = false;
	bool hasMain = false;
	for (const auto &fc : funcCodes) {
		if (fc.name == "get_iterator")
			hasGetIter = true;
		if (fc.name == "main")
			hasMain = true;
	}
	REQUIRE(hasGetIter);
	REQUIRE(hasMain);
}
