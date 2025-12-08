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

	// Control verbosity level (0 = quiet, 1 = normal, 2 = verbose)
	void setVerbose(int level) { verboseLevel_ = level; }
	int getVerbose() const { return verboseLevel_; }

	// Set the path to the maxon compiler
	void setCompilerPath(const std::string &path) { compilerPath_ = path; }

	// Get the number of failed tests
	int getFailedCount() const { return testsFailed; }

  private:
	int testsRun = 0;
	int testsPassed = 0;
	int testsFailed = 0;
	int verboseLevel_ = 0;				 // Default to quiet
	std::string compilerPath_ = "maxon"; // Default to PATH lookup

	bool compileTestProgram(const std::string &sourceFile, const std::string &outputExe);
};

} // namespace DebuggerTest
