#pragma once

#include <string>
#include <vector>

// Run all test fragment files and verify against expected output
int runTestFragments(bool verbose);

// Run a subset of tests (for parallel execution)
// Used internally by parallel test runner
int runTestFragmentsSubset(const std::vector<std::string>& testFiles, 
                           const std::string& outputFile, 
                           bool verbose);
