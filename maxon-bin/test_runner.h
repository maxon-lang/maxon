#pragma once

#include <string>
#include <vector>

// Run all test fragment files and verify against expected output
int runTestFragments(int verboseLevel);

// Run a subset of tests (for parallel execution)
// Used internally by parallel test runner
int runTestFragmentsSubset(const std::vector<std::string> &testFiles,
						   const std::string &outputFile,
						   int verboseLevel);

// Run a single test file and display results
int runSingleTestFile(const std::string &testFile, int verboseLevel, bool update = false);
