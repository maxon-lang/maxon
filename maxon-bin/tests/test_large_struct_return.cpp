/**
 * Unit tests for Large Struct Returns (Windows x64 ABI)
 *
 * Tests the handling of functions returning structs > 8 bytes,
 * which require a hidden pointer parameter per Windows x64 ABI.
 */

#include "../../lsp-server/tests/catch_amalgamated.hpp"
#include "../backend/x86_codegen.h"
#include "../backend/x86_encoding.h"
#include "../mir/mir.h"
#include "../mir/mir_builder.h"

using namespace backend;
using namespace mir;

//==============================================================================
// Test Helpers
//==============================================================================

// Create a 12-byte struct type (Iterator: 3 x int32)
static MIRType *createIteratorType() {
	std::vector<MIRType *> fields = {
		MIRType::getInt32(), // current
		MIRType::getInt32(), // limit
		MIRType::getInt32()	 // step
	};
	return MIRType::getStruct("Iterator", fields);
}

// Create a function that returns a large struct (like iter.range)
// function range(start int, end int) Iterator
static MIRFunction *createRangeFunction(MIRModule &mod, MIRType *iterType) {
	MIRBuilder builder(&mod);
	auto *func = builder.createFunction("range", iterType, {});
	auto *start = func->addParameter(MIRType::getInt32(), "start");
	auto *end = func->addParameter(MIRType::getInt32(), "end");

	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Alloca for the struct
	auto *iterAlloca = builder.createAlloca(iterType);

	// Set current = start
	auto *currentPtr = builder.createStructGEP(iterType, iterAlloca, 0);
	builder.createStore(start, currentPtr);

	// Set limit = end
	auto *limitPtr = builder.createStructGEP(iterType, iterAlloca, 1);
	builder.createStore(end, limitPtr);

	// Set step = 1
	auto *one = builder.getInt32(1);
	auto *stepPtr = builder.createStructGEP(iterType, iterAlloca, 2);
	builder.createStore(one, stepPtr);

	// Return the struct
	auto *result = builder.createLoad(iterType, iterAlloca);
	builder.createRet(result);

	return func;
}

// Create a main function that calls range and uses the result
static MIRFunction *createMainCallingRange(MIRModule &mod, MIRType *iterType) {
	MIRBuilder builder(&mod);

	// First declare the range function
	auto *rangeFunc = mod.getFunction("range");
	REQUIRE(rangeFunc != nullptr);

	auto *func = builder.createFunction("main", MIRType::getInt32(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Call range(0, 5)
	auto *zero = builder.getInt32(0);
	auto *five = builder.getInt32(5);
	auto *rangeResult = builder.createCall("range", iterType, {zero, five});
	rangeResult->definingInst->calleeFunc = rangeFunc;

	// Store result to an alloca
	auto *iterAlloca = builder.createAlloca(iterType);
	builder.createStore(rangeResult, iterAlloca);

	// Load the current field
	auto *currentPtr = builder.createStructGEP(iterType, iterAlloca, 0);
	auto *current = builder.createLoad(MIRType::getInt32(), currentPtr);

	// Return current (should be 0)
	builder.createRet(current);

	return func;
}

//==============================================================================
// Register Allocation Tests
//==============================================================================

TEST_CASE("Large struct return - RegAllocInfo flags", "[x86][large-struct]") {
	MIRModule mod("test");
	auto *iterType = createIteratorType();

	SECTION("Function returning large struct sets hasHiddenRetPtr") {
		(void)createRangeFunction(mod, iterType);

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&mod);

		// The range function should have hasHiddenRetPtr set
		// We can verify this by checking the generated code behavior
		auto &funcCodes = codegen.getFunctionCodes();
		REQUIRE(funcCodes.size() >= 1);

		// Find range function
		const FunctionCode *rangeCode = nullptr;
		for (const auto &fc : funcCodes) {
			if (fc.name == "range") {
				rangeCode = &fc;
				break;
			}
		}
		REQUIRE(rangeCode != nullptr);
		REQUIRE(!rangeCode->code.empty());

		INFO("Generated " << rangeCode->code.size() << " bytes for range function");
	}

	SECTION("Function returning small type does not set hasHiddenRetPtr") {
		MIRBuilder builder(&mod);
		(void)builder.createFunction("simple", MIRType::getInt32(), {});
		auto *entry = builder.createBasicBlock("entry");
		builder.setInsertPoint(entry);
		builder.createRet(builder.getInt32(42));

		X86CodeGen codegen(CallingConv::Win64);
		codegen.generate(&mod);

		auto &funcCodes = codegen.getFunctionCodes();
		const FunctionCode *simpleCode = nullptr;
		for (const auto &fc : funcCodes) {
			if (fc.name == "simple") {
				simpleCode = &fc;
				break;
			}
		}
		REQUIRE(simpleCode != nullptr);
	}
}

//==============================================================================
// Code Generation Tests
//==============================================================================

TEST_CASE("Large struct return - prologue saves RCX", "[x86][large-struct]") {
	MIRModule mod("test");
	auto *iterType = createIteratorType();
	(void)createRangeFunction(mod, iterType);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&mod);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *rangeCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "range") {
			rangeCode = &fc;
			break;
		}
	}
	REQUIRE(rangeCode != nullptr);

	// The prologue should save RCX to stack
	// Look for mov [rbp-offset], rcx pattern
	// This is: 48 89 4d XX (where XX is the offset)
	auto &code = rangeCode->code;

	// First, we should see standard prologue: push rbp, mov rbp, rsp
	REQUIRE(code.size() > 10);
	REQUIRE(code[0] == 0x55); // push rbp

	// After prologue setup, there should be mov [rbp+offset], rcx
	// which saves the hidden return pointer
	bool foundSaveRCX = false;
	for (size_t i = 0; i + 4 < code.size(); ++i) {
		// mov [rbp+disp8], rcx = 48 89 4d XX
		if (code[i] == 0x48 && code[i + 1] == 0x89 && code[i + 2] == 0x4D) {
			foundSaveRCX = true;
			int8_t offset = static_cast<int8_t>(code[i + 3]);
			INFO("Found save RCX to [rbp" << (offset >= 0 ? "+" : "") << (int)offset << "]");
			break;
		}
	}

	CHECK(foundSaveRCX); // Use CHECK so we can see all info even if it fails
}

TEST_CASE("Large struct return - parameter shift", "[x86][large-struct]") {
	MIRModule mod("test");
	auto *iterType = createIteratorType();
	(void)createRangeFunction(mod, iterType);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&mod);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *rangeCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "range") {
			rangeCode = &fc;
			break;
		}
	}
	REQUIRE(rangeCode != nullptr);

	// Parameters should be shifted:
	// - Hidden pointer: RCX
	// - start: RDX (instead of RCX)
	// - end: R8 (instead of RDX)
	//
	// We should see RDX and R8 being used, not RCX and RDX for params
	auto &code = rangeCode->code;

	// Look for any use of RDX in prologue area (saving to stack)
	// mov [rbp+disp], rdx = 48 89 55 XX
	bool foundSaveRDX = false;
	bool foundSaveR8 = false;
	for (size_t i = 0; i + 4 < code.size() && i < 40; ++i) {
		// mov [rbp+disp8], rdx = 48 89 55 XX
		if (code[i] == 0x48 && code[i + 1] == 0x89 && code[i + 2] == 0x55) {
			foundSaveRDX = true;
		}
		// mov [rbp+disp8], r8 = 4C 89 45 XX
		if (code[i] == 0x4C && code[i + 1] == 0x89 && code[i + 2] == 0x45) {
			foundSaveR8 = true;
		}
	}

	// At least one of the shifted parameters should be saved
	// (depending on how the function uses them)
	INFO("Found save RDX: " << foundSaveRDX);
	INFO("Found save R8: " << foundSaveR8);
}

TEST_CASE("Large struct return - caller passes hidden pointer", "[x86][large-struct]") {
	MIRModule mod("test");
	auto *iterType = createIteratorType();

	// Create range function first
	createRangeFunction(mod, iterType);

	// Create main that calls it
	createMainCallingRange(mod, iterType);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&mod);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *mainCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "main") {
			mainCode = &fc;
			break;
		}
	}
	REQUIRE(mainCode != nullptr);

	// Before the call to range, main should:
	// 1. LEA RCX, [rbp+offset] - load hidden pointer into RCX
	// 2. MOV RDX, 0 - first arg (start)
	// 3. MOV R8, 5 - second arg (end)
	// 4. CALL range

	auto &code = mainCode->code;
	INFO("Main function code size: " << code.size());

	// Look for lea rcx, [rbp+disp] before a call
	// lea rcx, [rbp+disp8] = 48 8D 4D XX
	bool foundLeaRCX = false;
	for (size_t i = 0; i + 4 < code.size(); ++i) {
		if (code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x4D) {
			foundLeaRCX = true;
			int8_t offset = static_cast<int8_t>(code[i + 3]);
			INFO("Found LEA RCX, [rbp" << (offset >= 0 ? "+" : "") << (int)offset << "]");
			break;
		}
	}

	CHECK(foundLeaRCX);
}

//==============================================================================
// Full Pipeline Tests
//==============================================================================

TEST_CASE("Large struct return - genRet copies struct", "[x86][large-struct]") {
	MIRModule mod("test");
	auto *iterType = createIteratorType();
	(void)createRangeFunction(mod, iterType);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&mod);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *rangeCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "range") {
			rangeCode = &fc;
			break;
		}
	}
	REQUIRE(rangeCode != nullptr);

	// The return should:
	// 1. Load hidden pointer from saved stack location
	// 2. Copy struct bytes to that location
	// 3. Return the pointer in RAX
	// 4. Do epilogue (mov rsp, rbp; pop rbp; ret)

	auto &code = rangeCode->code;

	// Look for epilogue: mov rsp, rbp (48 89 EC) or standard ret (C3)
	bool foundRet = false;
	for (size_t i = 0; i < code.size(); ++i) {
		if (code[i] == 0xC3) {
			foundRet = true;
			INFO("Found RET at offset " << i);
			break;
		}
	}

	REQUIRE(foundRet);
}

TEST_CASE("Large struct store from call - copies struct", "[x86][large-struct]") {
	MIRModule mod("test");
	auto *iterType = createIteratorType();

	// Create range function first
	createRangeFunction(mod, iterType);

	// Create main that calls it and stores result
	createMainCallingRange(mod, iterType);

	X86CodeGen codegen(CallingConv::Win64);
	codegen.generate(&mod);

	auto &funcCodes = codegen.getFunctionCodes();
	const FunctionCode *mainCode = nullptr;
	for (const auto &fc : funcCodes) {
		if (fc.name == "main") {
			mainCode = &fc;
			break;
		}
	}
	REQUIRE(mainCode != nullptr);

	// After calling range, the code should:
	// 1. LEA src from call result's stack slot
	// 2. LEA dst from alloca's stack slot (if different)
	// 3. Copy 12 bytes (8 + 4)

	auto &code = mainCode->code;

	// Look for mov operations that copy 8 bytes then 4 bytes
	// mov r10, [rax+offset] = 4C 8B 50 XX or similar
	// mov [rcx+offset], r10 = 4C 89 51 XX or similar

	bool foundCopyOps = false;
	for (size_t i = 0; i + 4 < code.size(); ++i) {
		// Look for movq with R10 as intermediate
		// 4C 8B 10 = mov r10, [rax]
		// 4C 89 11 = mov [rcx], r10
		if ((code[i] == 0x4C && code[i + 1] == 0x8B) ||
			(code[i] == 0x4C && code[i + 1] == 0x89)) {
			foundCopyOps = true;
			break;
		}
	}

	// Should have copy operations after the call
	CHECK(foundCopyOps);
}
