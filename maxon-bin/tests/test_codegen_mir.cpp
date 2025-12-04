/**
 * Unit tests for Phase 8: MIR Code Generator
 *
 * Tests the code generation from AST to MIR, verifying that
 * expressions, statements, and functions are correctly translated.
 */

#include "../ast.h"
#include "../codegen_mir.h"
#include "../lexer.h"
#include "../mir/mir.h"
#include "../parser.h"
#include <catch_amalgamated.hpp>

using namespace mir;

//==============================================================================
// Helper Functions
//==============================================================================

// Parse Maxon source code and generate MIR
static std::unique_ptr<MIRCodeGenerator> compileToMIR(const std::string &source) {
	Lexer lexer(source);
	auto stream = lexer.tokenize_stream();

	Parser parser(std::move(stream));
	auto program = parser.parse();

	auto codegen = std::make_unique<MIRCodeGenerator>("test_module", false, 0);
	codegen->generate(program.get(), false); // Don't generate entry point for unit tests

	return codegen;
}

// Parse and generate MIR with entry point
static std::unique_ptr<MIRCodeGenerator> compileToMIRWithEntry(const std::string &source) {
	Lexer lexer(source);
	auto stream = lexer.tokenize_stream();

	Parser parser(std::move(stream));
	auto program = parser.parse();

	auto codegen = std::make_unique<MIRCodeGenerator>("test_module", false, 0);
	codegen->generate(program.get(), true);

	return codegen;
}

//==============================================================================
// Expression Codegen Tests
//==============================================================================

TEST_CASE("MIR codegen: integer literals", "[codegen_mir][expr]") {
	auto codegen = compileToMIR(R"(
        function main() int
            return 42
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);

	MIRFunction *main = mod->getFunction("main");
	REQUIRE(main != nullptr);
	REQUIRE(main->basicBlocks.size() >= 1);
}

TEST_CASE("MIR codegen: float literals", "[codegen_mir][expr]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let x = 3.14
            return 0
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);

	MIRFunction *main = mod->getFunction("main");
	REQUIRE(main != nullptr);
}

TEST_CASE("MIR codegen: boolean literals", "[codegen_mir][expr]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let t = true
            let f = false
            return 0
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);
}

TEST_CASE("MIR codegen: binary arithmetic", "[codegen_mir][expr]") {
	SECTION("integer addition") {
		auto codegen = compileToMIR(R"(
            function main() int
                return 1 + 2
            end 'main'
        )");
		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
		REQUIRE(mod->getFunction("main") != nullptr);
	}

	SECTION("integer subtraction") {
		auto codegen = compileToMIR(R"(
            function main() int
                return 5 - 3
            end 'main'
        )");
		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("integer multiplication") {
		auto codegen = compileToMIR(R"(
            function main() int
                return 6 * 7
            end 'main'
        )");
		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("integer division") {
		auto codegen = compileToMIR(R"(
            function main() int
                return 10 / 2
            end 'main'
        )");
		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("integer modulo") {
		auto codegen = compileToMIR(R"(
            function main() int
                return 10 % 3
            end 'main'
        )");
		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

TEST_CASE("MIR codegen: binary comparisons", "[codegen_mir][expr]") {
	SECTION("less than") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 1 < 2 'cond'
                    return 1
                end 'cond'
                return 0
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}

	SECTION("greater than") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 5 > 3 'cond'
                    return 1
                end 'cond'
                return 0
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}

	SECTION("equality") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 5 = 5 'cond'
                    return 1
                end 'cond'
                return 0
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}
}

TEST_CASE("MIR codegen: unary operators", "[codegen_mir][expr]") {
	SECTION("unary negation") {
		auto codegen = compileToMIR(R"(
            function main() int
                return -42
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}

	SECTION("unary plus") {
		auto codegen = compileToMIR(R"(
            function main() int
                return +42
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}
}

//==============================================================================
// Statement Codegen Tests
//==============================================================================

TEST_CASE("MIR codegen: variable declarations", "[codegen_mir][stmt]") {
	SECTION("var with initializer") {
		auto codegen = compileToMIR(R"(
            function main() int
                var x = 10
                return x
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}

	SECTION("let (immutable) declaration") {
		auto codegen = compileToMIR(R"(
            function main() int
                let x = 10
                return x
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}
}

TEST_CASE("MIR codegen: assignment", "[codegen_mir][stmt]") {
	auto codegen = compileToMIR(R"(
        function main() int
            var x = 10
            x = 20
            return x
        end 'main'
    )");
	REQUIRE(codegen->getModule() != nullptr);
}

TEST_CASE("MIR codegen: if statement", "[codegen_mir][stmt]") {
	SECTION("simple if") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 1 < 2 'test'
                    return 1
                end 'test'
                return 0
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}

	SECTION("if-else") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 1 > 2 'test'
                    return 1
                else 'test'
                    return 0
                end 'test'
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}
}

TEST_CASE("MIR codegen: while loop", "[codegen_mir][stmt]") {
	auto codegen = compileToMIR(R"(
        function main() int
            var i = 0
            while i < 10 'loop'
                i = i + 1
            end 'loop'
            return i
        end 'main'
    )");
	REQUIRE(codegen->getModule() != nullptr);
}

TEST_CASE("MIR codegen: break and continue", "[codegen_mir][stmt]") {
	SECTION("break") {
		auto codegen = compileToMIR(R"(
            function main() int
                var i = 0
                while i < 100 'loop'
                    if i = 5 'check'
                        break
                    end 'check'
                    i = i + 1
                end 'loop'
                return i
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}

	SECTION("continue") {
		auto codegen = compileToMIR(R"(
            function main() int
                var sum = 0
                var i = 0
                while i < 10 'loop'
                    i = i + 1
                    if i = 5 'check'
                        continue
                    end 'check'
                    sum = sum + i
                end 'loop'
                return sum
            end 'main'
        )");
		REQUIRE(codegen->getModule() != nullptr);
	}
}

//==============================================================================
// Function Codegen Tests
//==============================================================================

TEST_CASE("MIR codegen: function with parameters", "[codegen_mir][func]") {
	auto codegen = compileToMIR(R"(
        function add(a int, b int) int
            return a + b
        end 'add'

        function main() int
            return add(3, 4)
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);

	MIRFunction *add = mod->getFunction("add");
	REQUIRE(add != nullptr);
	REQUIRE(add->parameters.size() == 2);
	REQUIRE(add->returnType == MIRType::getInt32());
}

TEST_CASE("MIR codegen: function calls", "[codegen_mir][func]") {
	auto codegen = compileToMIR(R"(
        function double(x int) int
            return x * 2
        end 'double'

        function main() int
            return double(21)
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);
	REQUIRE(mod->getFunction("double") != nullptr);
	REQUIRE(mod->getFunction("main") != nullptr);
}

TEST_CASE("MIR codegen: recursive function", "[codegen_mir][func]") {
	auto codegen = compileToMIR(R"(
        function factorial(n int) int
            if n <= 1 'base'
                return 1
            end 'base'
            return n * factorial(n - 1)
        end 'factorial'

        function main() int
            return factorial(5)
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);
	REQUIRE(mod->getFunction("factorial") != nullptr);
}

//==============================================================================
// Entry Point Tests
//==============================================================================

TEST_CASE("MIR codegen: entry point generation", "[codegen_mir][entry]") {
	auto codegen = compileToMIRWithEntry(R"(
        function main() int
            return 42
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);

	// Should have _start function
	MIRFunction *start = mod->getFunction("_start");
	REQUIRE(start != nullptr);
	REQUIRE(start->returnType == MIRType::getVoid());
}

//==============================================================================
// Type System Tests
//==============================================================================

TEST_CASE("MIR codegen: float operations", "[codegen_mir][types]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let a = 1.5
            let b = 2.5
            let c = a + b
            return 0
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

TEST_CASE("MIR codegen: mixed type promotion", "[codegen_mir][types]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let x = 1 + 2.0
            return 0
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

//==============================================================================
// Array Tests
//==============================================================================

TEST_CASE("MIR codegen: array declaration", "[codegen_mir][array]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let arr = [5]int
            return 0
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

TEST_CASE("MIR codegen: array access", "[codegen_mir][array]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let arr = [5]int
            arr[0] = 42
            return arr[0]
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

TEST_CASE("MIR codegen: array length", "[codegen_mir][array]") {
	auto codegen = compileToMIR(R"(
        function main() int
            let arr = [10]int
            return arr.length
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

//==============================================================================
// Struct Tests
//==============================================================================

TEST_CASE("MIR codegen: struct definition", "[codegen_mir][struct]") {
	auto codegen = compileToMIR(R"(
        struct Point
            x int
            y int
        end 'Point'

        function main() int
            return 0
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

TEST_CASE("MIR codegen: struct initialization", "[codegen_mir][struct]") {
	auto codegen = compileToMIR(R"(
        struct Point
            x int
            y int
        end 'Point'

        function main() int
            var p = Point { x: 10, y: 20 }
            return p.x + p.y
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

//==============================================================================
// Integration Tests
//==============================================================================

TEST_CASE("MIR codegen: complex program", "[codegen_mir][integration]") {
	auto codegen = compileToMIR(R"(
        function sum_array(arr []int) int
            var total = 0
            var i = 0
            while i < arr.length 'loop'
                total = total + arr[i]
                i = i + 1
            end 'loop'
            return total
        end 'sum_array'

        function main() int
            let numbers = [1, 2, 3, 4, 5]
            return sum_array(numbers)
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);

	// sum_array should have 2 parameters (array ptr + length)
	MIRFunction *sum = mod->getFunction("sum_array");
	REQUIRE(sum != nullptr);
	REQUIRE(sum->parameters.size() == 2);
}

TEST_CASE("MIR codegen: fibonacci", "[codegen_mir][integration]") {
	auto codegen = compileToMIR(R"(
        function fib(n int) int
            if n <= 1 'base'
                return n
            end 'base'
            return fib(n - 1) + fib(n - 2)
        end 'fib'

        function main() int
            return fib(10)
        end 'main'
    )");

	REQUIRE(codegen->getModule() != nullptr);
}

//==============================================================================
// Struct Return From Function Tests
//==============================================================================

TEST_CASE("MIR codegen: struct return from function - member access", "[codegen_mir][struct][large-struct]") {
	// This test verifies that when a variable is initialized with a function call
	// that returns a struct, the variable type is correctly tracked so that
	// member access works correctly.
	SECTION("small struct (8 bytes) returned by value") {
		auto codegen = compileToMIR(R"(
            struct Point
                x int
                y int
            end 'Point'

            function makePoint(a int, b int) Point
                var p = Point { x: a, y: b }
                return p
            end 'makePoint'

            function main() int
                var p = makePoint(10, 20)
                return p.x + p.y
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);

		MIRFunction *main = mod->getFunction("main");
		REQUIRE(main != nullptr);
	}

	SECTION("large struct (12 bytes) returned via hidden pointer") {
		// This is the case that was failing - Point3D is 12 bytes (3 ints)
		// which requires Windows x64 ABI hidden pointer return
		auto codegen = compileToMIR(R"(
            struct Point3D
                x int
                y int
                z int
            end 'Point3D'

            function makePoint(a int, b int, c int) Point3D
                var p = Point3D { x: a, y: b, z: c }
                return p
            end 'makePoint'

            function main() int
                var p = makePoint(10, 20, 30)
                return p.x + p.y + p.z
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);

		MIRFunction *main = mod->getFunction("main");
		REQUIRE(main != nullptr);

		// The main function should have generated MIR successfully
		// This would have failed before the fix with "Unknown member: x"
		REQUIRE(main->basicBlocks.size() >= 1);
	}
}

TEST_CASE("MIR codegen: struct return - variable type tracking", "[codegen_mir][struct]") {
	// Test that variableTypes is correctly set when type is inferred from function return
	SECTION("inferred type from function call") {
		auto codegen = compileToMIR(R"(
            struct Vec2
                x int
                y int
            end 'Vec2'

            function createVec(a int, b int) Vec2
                var v = Vec2 { x: a, y: b }
                return v
            end 'createVec'

            function useVec(v Vec2) int
                return v.x * v.y
            end 'useVec'

            function main() int
                var v = createVec(3, 4)
                return useVec(v)
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("explicit type annotation") {
		auto codegen = compileToMIR(R"(
            struct Vec2
                x int
                y int
            end 'Vec2'

            function createVec(a int, b int) Vec2
                var v = Vec2 { x: a, y: b }
                return v
            end 'createVec'

            function main() int
                var v = createVec(3, 4)
                return v.x + v.y
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Additional Loop Tests
//==============================================================================

TEST_CASE("MIR codegen: nested while loops", "[codegen_mir][stmt][loop]") {
	SECTION("nested loops") {
		auto codegen = compileToMIR(R"(
            function main() int
                var total = 0
                var i = 0
                while i < 5 'outer'
                    var j = 0
                    while j < 5 'inner'
                        total = total + 1
                        j = j + 1
                    end 'inner'
                    i = i + 1
                end 'outer'
                return total
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("loop with early exit") {
		auto codegen = compileToMIR(R"(
            function main() int
                var sum = 0
                var i = 0
                while i < 100 'loop'
                    if i >= 10 'check'
                        break
                    end 'check'
                    sum = sum + i
                    i = i + 1
                end 'loop'
                return sum
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// String Literal Tests
//==============================================================================

TEST_CASE("MIR codegen: string literals", "[codegen_mir][expr][string]") {
	SECTION("simple string literal") {
		auto codegen = compileToMIR(R"(
            function main() int
                var s = "Hello"
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("string with escape sequences") {
		auto codegen = compileToMIR(R"(
            function main() int
                var s = "Line1\nLine2\tTabbed"
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("empty string") {
		auto codegen = compileToMIR(R"(
            function main() int
                var s = ""
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Character Literal Tests
//==============================================================================

TEST_CASE("MIR codegen: character literals", "[codegen_mir][expr][char]") {
	SECTION("simple character") {
		auto codegen = compileToMIR(R"(
            function main() int
                var c = 'A'
                return c as int
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("digit character") {
		auto codegen = compileToMIR(R"(
            function main() int
                var zero = '0'
                var nine = '9'
                return zero as int
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Type Cast Tests
//==============================================================================

TEST_CASE("MIR codegen: type casts", "[codegen_mir][expr][cast]") {
	SECTION("int to float") {
		auto codegen = compileToMIR(R"(
            function main() int
                var i = 42
                var f = i as float
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("int to char") {
		auto codegen = compileToMIR(R"(
            function main() int
                var i = 65
                var c = i as char
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("int to bool") {
		auto codegen = compileToMIR(R"(
            function main() int
                var i = 1
                var b = i as bool
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("char to int") {
		auto codegen = compileToMIR(R"(
            function main() int
                var c = 'A'
                var i = c as int
                return i
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Nested Struct Access Tests
//==============================================================================

TEST_CASE("MIR codegen: nested struct access", "[codegen_mir][struct][nested]") {
	SECTION("struct containing struct") {
		auto codegen = compileToMIR(R"(
            struct Point
                x int
                y int
            end 'Point'

            struct Rectangle
                origin Point
                width int
                height int
            end 'Rectangle'

            function main() int
                var p = Point { x: 10, y: 20 }
                var r = Rectangle { origin: p, width: 100, height: 50 }
                var o = r.origin
                return o.x + o.y
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("deeply nested struct access") {
		auto codegen = compileToMIR(R"(
            struct Inner
                value int
            end 'Inner'

            struct Middle
                inner Inner
            end 'Middle'

            struct Outer
                middle Middle
            end 'Outer'

            function main() int
                var i = Inner { value: 42 }
                var m = Middle { inner: i }
                var o = Outer { middle: m }
                var mid = o.middle
                var inn = mid.inner
                return inn.value
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Array of Structs Tests
//==============================================================================

TEST_CASE("MIR codegen: array of structs", "[codegen_mir][array][struct]") {
	SECTION("array element struct access") {
		auto codegen = compileToMIR(R"(
            struct Point
                x int
                y int
            end 'Point'

            function main() int
                var points = [3]Point
                points[0] = Point { x: 1, y: 2 }
                points[1] = Point { x: 3, y: 4 }
                points[2] = Point { x: 5, y: 6 }
                return points[1].x + points[1].y
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Comparison Operator Tests (Additional)
//==============================================================================

TEST_CASE("MIR codegen: all comparison operators", "[codegen_mir][expr][comparison]") {
	SECTION("less than or equal") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 5 <= 5 'check'
                    return 1
                end 'check'
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("greater than or equal") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 5 >= 5 'check'
                    return 1
                end 'check'
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("not equal") {
		auto codegen = compileToMIR(R"(
            function main() int
                if 5 != 3 'check'
                    return 1
                end 'check'
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("float comparison") {
		auto codegen = compileToMIR(R"(
            function main() int
                var a = 3.14
                var b = 2.71
                if a > b 'check'
                    return 1
                end 'check'
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Float Arithmetic Tests (Additional)
//==============================================================================

TEST_CASE("MIR codegen: float arithmetic operations", "[codegen_mir][expr][float]") {
	SECTION("float subtraction") {
		auto codegen = compileToMIR(R"(
            function main() int
                var a = 5.5
                var b = 2.5
                var c = a - b
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("float multiplication") {
		auto codegen = compileToMIR(R"(
            function main() int
                var a = 2.5
                var b = 4.0
                var c = a * b
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("float division") {
		auto codegen = compileToMIR(R"(
            function main() int
                var a = 10.0
                var b = 4.0
                var c = a / b
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}

	SECTION("float negation") {
		auto codegen = compileToMIR(R"(
            function main() int
                var a = 3.14
                var b = -a
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Multiple Function Tests
//==============================================================================

TEST_CASE("MIR codegen: multiple functions", "[codegen_mir][func]") {
	SECTION("calling multiple functions") {
		auto codegen = compileToMIR(R"(
            function add(a int, b int) int
                return a + b
            end 'add'

            function multiply(a int, b int) int
                return a * b
            end 'multiply'

            function compute(x int) int
                var sum = add(x, 10)
                var prod = multiply(sum, 2)
                return prod
            end 'compute'

            function main() int
                return compute(5)
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
		REQUIRE(mod->getFunction("add") != nullptr);
		REQUIRE(mod->getFunction("multiply") != nullptr);
		REQUIRE(mod->getFunction("compute") != nullptr);
		REQUIRE(mod->getFunction("main") != nullptr);
	}

	SECTION("mutually recursive functions") {
		auto codegen = compileToMIR(R"(
            function isEven(n int) bool
                if n = 0 'base'
                    return true
                end 'base'
                return isOdd(n - 1)
            end 'isEven'

            function isOdd(n int) bool
                if n = 0 'base'
                    return false
                end 'base'
                return isEven(n - 1)
            end 'isOdd'

            function main() int
                if isEven(10) 'check'
                    return 1
                end 'check'
                return 0
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Labeled Break/Continue Tests
//==============================================================================

TEST_CASE("MIR codegen: labeled break and continue", "[codegen_mir][stmt][control-flow]") {
	SECTION("labeled break from nested loop") {
		auto codegen = compileToMIR(R"(
            function main() int
                var count = 0
                while true 'outer'
                    var i = 0
                    while i < 10 'inner'
                        count = count + 1
                        if count >= 5 'check'
                            break 'outer'
                        end 'check'
                        i = i + 1
                    end 'inner'
                end 'outer'
                return count
            end 'main'
        )");

		MIRModule *mod = codegen->getModule();
		REQUIRE(mod != nullptr);
	}
}

//==============================================================================
// Complex Integration Tests
//==============================================================================

TEST_CASE("MIR codegen: complex integration - matrix operations", "[codegen_mir][integration]") {
	auto codegen = compileToMIR(R"(
        function matrixSum(matrix []int, rows int, cols int) int
            var total = 0
            var i = 0
            while i < rows 'rowLoop'
                var j = 0
                while j < cols 'colLoop'
                    var idx = i * cols + j
                    total = total + matrix[idx]
                    j = j + 1
                end 'colLoop'
                i = i + 1
            end 'rowLoop'
            return total
        end 'matrixSum'

        function main() int
            var m = [9]int
            m[0] = 1
            m[1] = 2
            m[2] = 3
            m[3] = 4
            m[4] = 5
            m[5] = 6
            m[6] = 7
            m[7] = 8
            m[8] = 9
            return matrixSum(m, 3, 3)
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);
}

TEST_CASE("MIR codegen: complex integration - struct with methods pattern", "[codegen_mir][integration]") {
	auto codegen = compileToMIR(R"(
        struct Counter
            value int
            step int
        end 'Counter'

        function Counter_new(initial int, step int) Counter
            var c = Counter { value: initial, step: step }
            return c
        end 'Counter_new'

        function Counter_increment(c Counter) Counter
            var newC = Counter { value: c.value + c.step, step: c.step }
            return newC
        end 'Counter_increment'

        function Counter_getValue(c Counter) int
            return c.value
        end 'Counter_getValue'

        function main() int
            var counter = Counter_new(0, 5)
            counter = Counter_increment(counter)
            counter = Counter_increment(counter)
            return Counter_getValue(counter)
        end 'main'
    )");

	MIRModule *mod = codegen->getModule();
	REQUIRE(mod != nullptr);
}
