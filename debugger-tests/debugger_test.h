#pragma once

#include <functional>
#include <string>
#include <vector>

namespace DebuggerTest {

struct BreakpointExpectation {
	std::string functionName;
	int lineNumber;
	std::vector<std::pair<std::string, std::string>> expectedVariables; // variable name -> expected value
};

struct StepExpectation {
	enum class StepType {
		StepIn,
		StepOver,
		StepOut,
		Continue
	};

	StepType type;
	int expectedLine;
	std::string expectedFunction;
};

struct TestCase {
	std::string name;
	std::string sourceFile;		// Path to .maxon source file
	std::string executablePath; // Path to compiled executable
	std::vector<BreakpointExpectation> breakpoints;
	std::vector<StepExpectation> steps;
	std::function<bool()> customValidation; // Optional custom validation
};

class DebuggerTestRunner {
  public:
	DebuggerTestRunner();
	~DebuggerTestRunner();

	bool runTest(const TestCase &testCase);
	void printSummary();

  private:
	int testsRun = 0;
	int testsPassed = 0;
	int testsFailed = 0;

	bool compileTestProgram(const std::string &sourceFile, const std::string &outputExe);
};

} // namespace DebuggerTest
