/**
 * Unit tests for MIR Parser
 *
 * Tests parsing of textual MIR format into MIRModule data structures.
 * This parser is used to load the runtime library from .mir files.
 */

#include "../mir/mir.h"
#include "../mir/mir_parser.h"
#include "catch_amalgamated.hpp"

using namespace mir;

//==============================================================================
// MIR Parser Basic Tests
//==============================================================================

TEST_CASE("MIR Parser - Type Parsing", "[mir-parser][types]") {
	SECTION("Basic types") {
		auto result = MIRParser::parse(R"(
			define void @test_void() {
			entry:
				ret void
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("test_void");
		REQUIRE(func != nullptr);
		REQUIRE(func->returnType->kind == MIRTypeKind::Void);
	}

	SECTION("Integer types") {
		auto result = MIRParser::parse(R"(
			define i32 @test_i32(i32 %x, i64 %y) {
			entry:
				ret i32 %x
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("test_i32");
		REQUIRE(func != nullptr);
		REQUIRE(func->returnType->kind == MIRTypeKind::Int32);
		REQUIRE(func->parameters.size() == 2);
		REQUIRE(func->parameters[0]->type->kind == MIRTypeKind::Int32);
		REQUIRE(func->parameters[1]->type->kind == MIRTypeKind::Int64);
	}

	SECTION("Floating-point type") {
		auto result = MIRParser::parse(R"(
			define f64 @test_f64(f64 %x) {
			entry:
				ret f64 %x
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("test_f64");
		REQUIRE(func != nullptr);
		REQUIRE(func->returnType->kind == MIRTypeKind::Float64);
	}

	SECTION("Pointer type") {
		auto result = MIRParser::parse(R"(
			define ptr @test_ptr(ptr %p) {
			entry:
				ret ptr %p
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("test_ptr");
		REQUIRE(func != nullptr);
		REQUIRE(func->returnType->kind == MIRTypeKind::Ptr);
	}
}

TEST_CASE("MIR Parser - Arithmetic Instructions", "[mir-parser][arithmetic]") {
	SECTION("Integer arithmetic") {
		auto result = MIRParser::parse(R"(
			define i32 @add_test(i32 %a, i32 %b) {
			entry:
				%sum = add i32 %a, %b
				%diff = sub i32 %a, %b
				%prod = mul i32 %a, %b
				%quot = sdiv i32 %a, %b
				%rem = srem i32 %a, %b
				ret i32 %sum
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("add_test");
		REQUIRE(func != nullptr);
		REQUIRE(func->basicBlocks.size() == 1);
		auto &block = func->basicBlocks[0];
		REQUIRE(block->instructions.size() == 6); // 5 ops + ret
	}

	SECTION("Floating-point arithmetic") {
		auto result = MIRParser::parse(R"(
			define f64 @fadd_test(f64 %a, f64 %b) {
			entry:
				%sum = fadd f64 %a, %b
				%diff = fsub f64 %a, %b
				%prod = fmul f64 %a, %b
				%quot = fdiv f64 %a, %b
				ret f64 %sum
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("fadd_test");
		REQUIRE(func != nullptr);
		auto &block = func->basicBlocks[0];
		REQUIRE(block->instructions.size() == 5);
	}

	SECTION("Constants in arithmetic") {
		auto result = MIRParser::parse(R"(
			define i32 @const_test(i32 %x) {
			entry:
				%r1 = add i32 %x, 42
				%r2 = mul i32 %x, 10
				ret i32 %r1
			}
		)");
		REQUIRE(result.success());
	}

	SECTION("Floating-point constants") {
		auto result = MIRParser::parse(R"(
			define f64 @fconst_test(f64 %x) {
			entry:
				%r1 = fadd f64 %x, 3.14159
				%r2 = fmul f64 %x, 1.0e-10
				ret f64 %r1
			}
		)");
		REQUIRE(result.success());
	}
}

TEST_CASE("MIR Parser - Comparisons", "[mir-parser][comparisons]") {
	SECTION("Integer comparisons") {
		auto result = MIRParser::parse(R"(
			define i1 @icmp_test(i32 %a, i32 %b) {
			entry:
				%eq = icmp eq i32 %a, %b
				%ne = icmp ne i32 %a, %b
				%slt = icmp slt i32 %a, %b
				%sle = icmp sle i32 %a, %b
				%sgt = icmp sgt i32 %a, %b
				%sge = icmp sge i32 %a, %b
				ret i1 %eq
			}
		)");
		REQUIRE(result.success());
	}

	SECTION("Floating-point comparisons") {
		auto result = MIRParser::parse(R"(
			define i1 @fcmp_test(f64 %a, f64 %b) {
			entry:
				%eq = fcmp oeq f64 %a, %b
				%lt = fcmp olt f64 %a, %b
				%le = fcmp ole f64 %a, %b
				%gt = fcmp ogt f64 %a, %b
				%ge = fcmp oge f64 %a, %b
				ret i1 %eq
			}
		)");
		REQUIRE(result.success());
	}
}

TEST_CASE("MIR Parser - Control Flow", "[mir-parser][control-flow]") {
	SECTION("Unconditional branch") {
		auto result = MIRParser::parse(R"(
			define void @br_test() {
			entry:
				br label %exit
			exit:
				ret void
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("br_test");
		REQUIRE(func->basicBlocks.size() == 2);
	}

	SECTION("Conditional branch") {
		auto result = MIRParser::parse(R"(
			define i32 @cond_br_test(i1 %cond) {
			entry:
				br i1 %cond, label %then, label %else
			then:
				ret i32 1
			else:
				ret i32 0
			}
		)");
		REQUIRE(result.success());
		auto *func = result.module->getFunction("cond_br_test");
		REQUIRE(func->basicBlocks.size() == 3);
	}

	SECTION("Loop structure") {
		auto result = MIRParser::parse(R"(
			define i32 @loop_test(i32 %n) {
			entry:
				br label %loop
			loop:
				%i = phi i32 [0, %entry], [%next, %loop]
				%next = add i32 %i, 1
				%cond = icmp slt i32 %next, %n
				br i1 %cond, label %loop, label %exit
			exit:
				ret i32 %next
			}
		)");
		REQUIRE(result.success());
	}
}

TEST_CASE("MIR Parser - Memory Operations", "[mir-parser][memory]") {
	SECTION("Alloca and load/store") {
		auto result = MIRParser::parse(R"(
			define i32 @mem_test() {
			entry:
				%ptr = alloca i32
				store i32 42, ptr %ptr
				%val = load i32, ptr %ptr
				ret i32 %val
			}
		)");
		REQUIRE(result.success());
	}

	SECTION("GEP instruction") {
		auto result = MIRParser::parse(R"(
			define ptr @gep_test(ptr %arr, i64 %idx) {
			entry:
				%ptr = getelementptr i32, ptr %arr, i64 %idx
				ret ptr %ptr
			}
		)");
		REQUIRE(result.success());
	}
}

TEST_CASE("MIR Parser - Conversions", "[mir-parser][conversions]") {
	SECTION("Integer conversions") {
		auto result = MIRParser::parse(R"(
			define i64 @conv_test(i32 %x) {
			entry:
				%ext = sext i32 %x to i64
				%trunc = trunc i64 %ext to i32
				%zext = zext i32 %trunc to i64
				ret i64 %ext
			}
		)");
		REQUIRE(result.success());
	}

	SECTION("Float/int conversions") {
		auto result = MIRParser::parse(R"(
			define f64 @fconv_test(i64 %x) {
			entry:
				%f = sitofp i64 %x to f64
				%i = fptosi f64 %f to i64
				%f2 = sitofp i64 %i to f64
				ret f64 %f
			}
		)");
		REQUIRE(result.success());
	}

	SECTION("Bitcast") {
		auto result = MIRParser::parse(R"(
			define i64 @bitcast_test(f64 %x) {
			entry:
				%bits = bitcast f64 %x to i64
				ret i64 %bits
			}
		)");
		REQUIRE(result.success());
	}
}

TEST_CASE("MIR Parser - Function Calls", "[mir-parser][calls]") {
	SECTION("Simple call") {
		auto result = MIRParser::parse(R"(
			declare i32 @external_func(i32)

			define i32 @call_test(i32 %x) {
			entry:
				%result = call i32 @external_func(i32 %x)
				ret i32 %result
			}
		)");
		REQUIRE(result.success());
		REQUIRE(result.module->getFunction("external_func") != nullptr);
		REQUIRE(result.module->getFunction("external_func")->isExternal);
	}

	SECTION("Call with multiple args") {
		auto result = MIRParser::parse(R"(
			declare void @multi_arg(i32, i64, ptr)

			define void @call_multi_test() {
			entry:
				call void @multi_arg(i32 1, i64 2, ptr null)
				ret void
			}
		)");
		REQUIRE(result.success());
	}
}

TEST_CASE("MIR Parser - External Declarations", "[mir-parser][external]") {
	SECTION("Windows-style declarations") {
		auto result = MIRParser::parse(R"(
			declare ptr @GetProcessHeap()
			declare ptr @HeapAlloc(ptr, i32, i64)
			declare i1 @HeapFree(ptr, i32, ptr)
		)");
		REQUIRE(result.success());
		REQUIRE(result.module->getFunction("GetProcessHeap") != nullptr);
		REQUIRE(result.module->getFunction("HeapAlloc") != nullptr);
		REQUIRE(result.module->getFunction("HeapFree") != nullptr);
	}
}

//==============================================================================
// Runtime Function Tests
//==============================================================================

TEST_CASE("MIR Parser - Runtime: memset", "[mir-parser][runtime]") {
	auto result = MIRParser::parse(R"(
		define ptr @memset(ptr %dest, i32 %val, i64 %count) {
		entry:
			%byteVal = trunc i32 %val to i8
			%i = alloca i64
			store i64 0, ptr %i
			br label %loop.cond
		loop.cond:
			%iVal = load i64, ptr %i
			%cond = icmp ult i64 %iVal, %count
			br i1 %cond, label %loop.body, label %loop.end
		loop.body:
			%ptr = getelementptr i8, ptr %dest, i64 %iVal
			store i8 %byteVal, ptr %ptr
			%iNext = add i64 %iVal, 1
			store i64 %iNext, ptr %i
			br label %loop.cond
		loop.end:
			ret ptr %dest
		}
	)");
	REQUIRE(result.success());
	auto *func = result.module->getFunction("memset");
	REQUIRE(func != nullptr);
	REQUIRE(func->parameters.size() == 3);
	REQUIRE(func->basicBlocks.size() == 4);
}

TEST_CASE("MIR Parser - Runtime: floor", "[mir-parser][runtime]") {
	auto result = MIRParser::parse(R"(
		define f64 @floor(f64 %x) {
		entry:
			%x_bits = bitcast f64 %x to i64
			%x_as_i64 = fptosi f64 %x to i64
			%truncated = sitofp i64 %x_as_i64 to f64
			%is_negative = fcmp olt f64 %x, 0.0
			%has_frac = fcmp one f64 %x, %truncated
			%need_adjust = and i1 %is_negative, %has_frac
			br i1 %need_adjust, label %adjust, label %no_adjust
		adjust:
			%adjusted = fsub f64 %truncated, 1.0
			ret f64 %adjusted
		no_adjust:
			ret f64 %truncated
		}
	)");
	REQUIRE(result.success());
	auto *func = result.module->getFunction("floor");
	REQUIRE(func != nullptr);
}

TEST_CASE("MIR Parser - Runtime: sin kernel", "[mir-parser][runtime]") {
	auto result = MIRParser::parse(R"(
		define f64 @__sin_kernel(f64 %x, f64 %y) {
		entry:
			%z = fmul f64 %x, %x
			%w = fmul f64 %z, %z
			%t1 = fmul f64 %z, 1.58969099521155010221e-10
			%t2 = fadd f64 -2.50507602534068634195e-08, %t1
			%v = fmul f64 %z, %x
			%result = fadd f64 %x, %y
			ret f64 %result
		}
	)");
	REQUIRE(result.success());
	auto *func = result.module->getFunction("__sin_kernel");
	REQUIRE(func != nullptr);
	REQUIRE(func->parameters.size() == 2);
}

//==============================================================================
// Parse Error Tests
//==============================================================================

TEST_CASE("MIR Parser - Error handling", "[mir-parser][errors]") {
	SECTION("Unknown instruction") {
		auto result = MIRParser::parse(R"(
			define void @test() {
			entry:
				%x = unknown_instr i32 1, 2
				ret void
			}
		)");
		REQUIRE(!result.success());
		REQUIRE(!result.errors.empty());
	}

	SECTION("Missing return type") {
		auto result = MIRParser::parse(R"(
			define @test() {
			entry:
				ret void
			}
		)");
		// Parser should handle this gracefully
		REQUIRE(!result.errors.empty());
	}
}

//==============================================================================
// Module Merge Tests
//==============================================================================

TEST_CASE("MIR Parser - Module merge", "[mir-parser][merge]") {
	auto result1 = MIRParser::parse(R"(
		define i32 @func1() {
		entry:
			ret i32 1
		}
	)",
									"module1");

	auto result2 = MIRParser::parse(R"(
		define i32 @func2() {
		entry:
			ret i32 2
		}
	)",
									"module2");

	REQUIRE(result1.success());
	REQUIRE(result2.success());

	std::vector<std::unique_ptr<MIRModule>> modules;
	modules.push_back(std::move(result1.module));
	modules.push_back(std::move(result2.module));

	auto merged = MIRParser::merge(std::move(modules));
	REQUIRE(merged != nullptr);
	REQUIRE(merged->getFunction("func1") != nullptr);
	REQUIRE(merged->getFunction("func2") != nullptr);
}
