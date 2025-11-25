/**
 * Unit tests for Phase 8: MIR Code Generator
 *
 * Tests the code generation from AST to MIR, verifying that
 * expressions, statements, and functions are correctly translated.
 */

#include "../../lsp-server/tests/catch_amalgamated.hpp"
#include "../ast.h"
#include "../codegen_mir.h"
#include "../lexer.h"
#include "../mir/mir.h"
#include "../parser.h"

using namespace mir;

//==============================================================================
// Helper Functions
//==============================================================================

// Parse Maxon source code and generate MIR
static std::unique_ptr<MIRCodeGenerator> compileToMIR(const std::string &source) {
	Lexer lexer(source);
	auto tokens = lexer.tokenize();

	Parser parser(tokens);
	auto program = parser.parse();

	auto codegen = std::make_unique<MIRCodeGenerator>("test_module", false, 0);
	codegen->generate(program.get(), false); // Don't generate entry point for unit tests

	return codegen;
}

// Parse and generate MIR with entry point
static std::unique_ptr<MIRCodeGenerator> compileToMIRWithEntry(const std::string &source) {
	Lexer lexer(source);
	auto tokens = lexer.tokenize();

	Parser parser(tokens);
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
                if 5 == 5 'cond'
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

	SECTION("typed variable") {
		auto codegen = compileToMIR(R"(
            function main() int
                var x int = 10
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
                else
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
                    if i == 5 'check'
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
                    if i == 5 'check'
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
