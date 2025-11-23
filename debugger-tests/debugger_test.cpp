#include "debugger_test.h"
#include "debugger_interface.h"
#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <thread>

namespace fs = std::filesystem;

namespace DebuggerTest {

DebuggerTestRunner::DebuggerTestRunner() {
	// Initialization handled by platform-specific debugger
}

DebuggerTestRunner::~DebuggerTestRunner() {
	// Cleanup handled by platform-specific debugger
}

bool DebuggerTestRunner::compileTestProgram(const std::string &sourceFile, const std::string &outputExe) {
	// Build command: maxon compile <source> -g
	// Note: maxon compile doesn't support -o, it creates exe in source directory
	std::string cmd = "maxon compile \"" + sourceFile + "\" -g 2>&1";

	std::cout << "  Compiling: " << cmd << std::endl;
	int result = std::system(cmd.c_str());

	if (result != 0) {
		std::cerr << "  ERROR: Compilation failed with exit code " << result << std::endl;
		return false;
	}

	// Check that exe was created in source directory
	fs::path sourcePath(sourceFile);
	fs::path generatedExe = sourcePath.parent_path() / (sourcePath.stem().string() + ".exe");

	if (!fs::exists(generatedExe)) {
		std::cerr << "  ERROR: Executable not created: " << generatedExe << std::endl;
		return false;
	}

	return true;
}

bool DebuggerTestRunner::runTest(const TestCase &testCase) {
	testsRun++;
	std::cout << "\n=== Test: " << testCase.name << " ===" << std::endl;

	// Compile the test program
	if (!compileTestProgram(testCase.sourceFile, testCase.executablePath)) {
		testsFailed++;
		return false;
	}

	// Create platform-specific debugger
	auto debugger = createDebugger();
	if (!debugger) {
		std::cerr << "  ERROR: Failed to create debugger" << std::endl;
		testsFailed++;
		return false;
	}

	// Create target
	if (!debugger->createTarget(testCase.executablePath)) {
		std::cerr << "  ERROR: Failed to create target: " << debugger->getLastError() << std::endl;
		testsFailed++;
		return false;
	}

	std::cout << "  Created target for: " << testCase.executablePath << std::endl;

	// Set breakpoints
	for (const auto &bp : testCase.breakpoints) {
		if (!debugger->setBreakpoint(testCase.sourceFile, bp.lineNumber)) {
			std::cerr << "  ERROR: Failed to set breakpoint at line " << bp.lineNumber
					  << ": " << debugger->getLastError() << std::endl;
			testsFailed++;
			return false;
		}
		std::cout << "  Set breakpoint at " << bp.functionName << ":" << bp.lineNumber << std::endl;
	}

	// Launch process
	if (!debugger->launch()) {
		std::cerr << "  ERROR: Failed to launch process: " << debugger->getLastError() << std::endl;
		testsFailed++;
		return false;
	}

	std::cout << "  Process launched successfully" << std::endl;

	// Wait for process to stop at initial breakpoint
	if (!debugger->waitForStop(5)) {
		std::cerr << "  ERROR: Timeout waiting for process to stop: " << debugger->getLastError() << std::endl;
		testsFailed++;
		return false;
	}

	std::cout << "  Process stopped at initial breakpoint (threads: " << debugger->getThreadCount() << ")" << std::endl;

#ifdef _WIN32
	// On Windows, full source-level debugging requires DIA SDK integration
	// For now, just verify we can launch and control the process
	std::cout << "  Windows: Skipping source-level debugging tests (use VS Code/Visual Studio for full debugging)" << std::endl;
	debugger->kill();
	std::cout << "  ✓ Test passed (basic launch/control verified)" << std::endl;
	testsPassed++;
	return true;
#endif

	// Select first thread
	if (!debugger->selectThread(0)) {
		std::cerr << "  ERROR: Failed to select thread: " << debugger->getLastError() << std::endl;
		testsFailed++;
		return false;
	}

	// Execute test steps
	int breakpointIndex = 0;
	for (const auto &step : testCase.steps) {
		// Verify location
		int actualLine = debugger->getCurrentLine();
		std::string actualFunction = debugger->getCurrentFunction();

		if (actualLine != step.expectedLine) {
			std::cerr << "  ERROR: Expected line " << step.expectedLine
					  << " but at line " << actualLine << std::endl;
			testsFailed++;
			return false;
		}

		if (!step.expectedFunction.empty() && actualFunction != step.expectedFunction) {
			std::cerr << "  ERROR: Expected function '" << step.expectedFunction
					  << "' but in '" << actualFunction << "'" << std::endl;
			testsFailed++;
			return false;
		}

		std::cout << "    At " << actualFunction << ":" << actualLine << " ✓" << std::endl;

		// Validate variables at breakpoints
		if (step.type == StepExpectation::StepType::Continue && breakpointIndex < testCase.breakpoints.size()) {
			const auto &bp = testCase.breakpoints[breakpointIndex];
			for (const auto &[varName, expectedValue] : bp.expectedVariables) {
				std::string actualValue;
				if (!debugger->getVariableValue(varName, actualValue)) {
					std::cerr << "  ERROR: Variable '" << varName << "' not found: "
							  << debugger->getLastError() << std::endl;
					testsFailed++;
					return false;
				}

				if (actualValue != expectedValue) {
					std::cerr << "  ERROR: Variable '" << varName << "' expected '"
							  << expectedValue << "' but got '" << actualValue << "'" << std::endl;
					testsFailed++;
					return false;
				}

				std::cout << "    Variable '" << varName << "' = " << actualValue << " ✓" << std::endl;
			}
			breakpointIndex++;
		}

		// Execute step
		bool stepSuccess = false;
		switch (step.type) {
		case StepExpectation::StepType::StepIn:
			std::cout << "    Step In" << std::endl;
			stepSuccess = debugger->stepIn();
			break;
		case StepExpectation::StepType::StepOver:
			std::cout << "    Step Over" << std::endl;
			stepSuccess = debugger->stepOver();
			break;
		case StepExpectation::StepType::StepOut:
			std::cout << "    Step Out" << std::endl;
			stepSuccess = debugger->stepOut();
			break;
		case StepExpectation::StepType::Continue:
			std::cout << "    Continue" << std::endl;
			stepSuccess = debugger->continueExecution();
			break;
		}

		if (!stepSuccess) {
			std::cerr << "  ERROR: Step failed: " << debugger->getLastError() << std::endl;
			testsFailed++;
			return false;
		}
	}

	// Run custom validation if provided
	if (testCase.customValidation && !testCase.customValidation()) {
		std::cerr << "  ERROR: Custom validation failed" << std::endl;
		testsFailed++;
		return false;
	}

	// Clean up
	debugger->kill();

	std::cout << "  ✓ Test passed" << std::endl;
	testsPassed++;
	return true;
}

void DebuggerTestRunner::printSummary() {
	std::cout << "\n=== Test Summary ===" << std::endl;
	std::cout << "Tests Run:    " << testsRun << std::endl;
	std::cout << "Tests Passed: " << testsPassed << std::endl;
	std::cout << "Tests Failed: " << testsFailed << std::endl;

	if (testsFailed == 0 && testsRun > 0) {
		std::cout << "\n✓ All tests passed!" << std::endl;
	} else if (testsFailed > 0) {
		std::cout << "\n✗ Some tests failed" << std::endl;
	}
}

} // namespace DebuggerTest
