#include "debugger_test.h"
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iostream>

namespace fs = std::filesystem;
using namespace DebuggerTest;

int main(int argc, char *argv[]) {
	// Disable LLDB's Python scripting to avoid Python dependency issues
#ifdef _WIN32
	_putenv("LLDB_DISABLE_PYTHON=1");
#else
	putenv(const_cast<char *>("LLDB_DISABLE_PYTHON=1"));
#endif

	// Parse command line arguments
	int verboseLevel = 0;
	for (int i = 1; i < argc; i++) {
		if (strcmp(argv[i], "-v") == 0 || strcmp(argv[i], "--verbose") == 0) {
			verboseLevel = 1;
		} else if (strcmp(argv[i], "-vv") == 0) {
			verboseLevel = 2;
		} else if (strcmp(argv[i], "-h") == 0 || strcmp(argv[i], "--help") == 0) {
			std::cout << "Usage: " << argv[0] << " [options]" << std::endl;
			std::cout << "Options:" << std::endl;
			std::cout << "  -v, --verbose   Enable verbose output" << std::endl;
			std::cout << "  -vv             Enable extra verbose output" << std::endl;
			std::cout << "  -h, --help      Show this help message" << std::endl;
			return 0;
		}
	}

	if (verboseLevel >= 1) {
		std::cout << "Maxon Debugger Integration Tests" << std::endl;
		std::cout << "=================================" << std::endl;
	}

	// Determine paths - go up from bin/ to debugger-tests/
	// Use absolute + canonical path to properly resolve . and ..
	fs::path exePath = fs::canonical(fs::absolute(argv[0]));
	fs::path binDir = exePath.parent_path();
	fs::path debuggerTestsDir = binDir.parent_path();
	fs::path testProgramsDir = debuggerTestsDir / "test-programs";

	// Find maxon compiler - it's in the project's bin/ directory (sibling to debugger-tests/)
	fs::path projectDir = debuggerTestsDir.parent_path();
	fs::path compilerPath = projectDir / "bin" / "maxon.exe";

	if (!fs::exists(compilerPath)) {
		std::cerr << "ERROR: Maxon compiler not found at: " << compilerPath << std::endl;
		std::cerr << "Please build the compiler first with 'make compiler'" << std::endl;
		return 1;
	}

	// Create bin directory if it doesn't exist
	fs::create_directories(binDir);

	DebuggerTestRunner runner;
	runner.setVerbose(verboseLevel);
	runner.setCompilerPath(compilerPath.string());

	// Test 1: Simple Variables Test
	{
		TestCase test;
		test.name = "Simple Variables and Control Flow";
		test.sourceFile = (testProgramsDir / "simple-variables.maxon").string();
		test.executablePath = (testProgramsDir / "simple-variables.exe").string();

		// Set breakpoint at line 3 (let x = 10)
		test.breakpoints.push_back({
			"main",
			3,
			{} // Variables not yet initialized at this line
		});

		// Set breakpoint at line 5 (let sum = x + y)
		test.breakpoints.push_back({"main",
									5,
									{{"x", "10"}, {"y", "20"}}});

		// Set breakpoint at line 7 (if condition)
		test.breakpoints.push_back({"main",
									7,
									{{"x", "10"}, {"y", "20"}, {"sum", "30"}}});

		test.steps = {
			{StepExpectation::StepType::Continue, 3, "main"}, // Hit first breakpoint
			{StepExpectation::StepType::Continue, 5, "main"}, // Hit second breakpoint
			{StepExpectation::StepType::Continue, 7, "main"}, // Hit third breakpoint
			{StepExpectation::StepType::Continue, 0, ""}	  // Run to completion
		};

		runner.runTest(test);
	}

	// Test 2: Function Calls Test
	{
		TestCase test;
		test.name = "Function Calls and Stepping";
		test.sourceFile = (testProgramsDir / "function-calls.maxon").string();
		test.executablePath = (testProgramsDir / "function-calls.exe").string();

		// Breakpoint at line 13 in main (let a = 5)
		test.breakpoints.push_back({"main",
									13,
									{}});

		// Breakpoint at line 3 in add function (let result = a + b)
		test.breakpoints.push_back({"add",
									3,
									{{"a", "5"}, {"b", "10"}}});

		test.steps = {
			{StepExpectation::StepType::Continue, 13, "main"}, // Hit first breakpoint in main
			{StepExpectation::StepType::StepOver, 14, "main"}, // Step over let b = 10
			{StepExpectation::StepType::StepOver, 15, "main"}, // Step to function call
			{StepExpectation::StepType::StepIn, 2, "add"},	   // Step into add function
			{StepExpectation::StepType::Continue, 3, "add"},   // Hit breakpoint in add
			{StepExpectation::StepType::StepOut, 15, "main"},  // Step out back to main
			{StepExpectation::StepType::Continue, 0, ""}	   // Run to completion
		};

		runner.runTest(test);
	}

	// Test 3: Loop Iteration Test
	{
		TestCase test;
		test.name = "Loop Iteration";
		test.sourceFile = (testProgramsDir / "loop-iteration.maxon").string();
		test.executablePath = (testProgramsDir / "loop-iteration.exe").string();

		// Breakpoint at line 3 (var sum = 0)
		test.breakpoints.push_back({"main",
									3,
									{}});

		// Breakpoint at line 6 (inside loop: sum = sum + i)
		test.breakpoints.push_back({"main",
									6,
									{}});

		test.steps = {
			{StepExpectation::StepType::Continue, 3, "main"}, // Hit first breakpoint
			{StepExpectation::StepType::Continue, 6, "main"}, // Hit loop body (first iteration)
			{StepExpectation::StepType::StepOver, 6, "main"}, // Step over assignment
			{StepExpectation::StepType::Continue, 6, "main"}, // Next iteration
			{StepExpectation::StepType::Continue, 0, ""}	  // Run to completion
		};

		runner.runTest(test);
	}

	// Test 4: Nested Scopes Test
	{
		TestCase test;
		test.name = "Nested Scopes and Variable Shadowing";
		test.sourceFile = (testProgramsDir / "nested-scopes.maxon").string();
		test.executablePath = (testProgramsDir / "nested-scopes.exe").string();

		// Breakpoint at line 3 (let x = 10)
		test.breakpoints.push_back({"main",
									3,
									{}});

		// Breakpoint at line 6 (let y = 20, first if block)
		test.breakpoints.push_back({"main",
									6,
									{{"x", "10"}}});

		// Breakpoint at line 12 (let y = 30, second if block)
		test.breakpoints.push_back({"main",
									12,
									{{"x", "10"}}});

		test.steps = {
			{StepExpectation::StepType::Continue, 3, "main"},  // Hit first breakpoint
			{StepExpectation::StepType::Continue, 6, "main"},  // Hit first if block
			{StepExpectation::StepType::StepOver, 7, "main"},  // Step to let z = x + y
			{StepExpectation::StepType::Continue, 12, "main"}, // Hit second if block
			{StepExpectation::StepType::Continue, 0, ""}	   // Run to completion
		};

		runner.runTest(test);
	}

	// Print summary
	runner.printSummary();

	return runner.getFailedCount() > 0 ? 1 : 0;
}
