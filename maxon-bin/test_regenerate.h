#pragma once

#include <string>
#include <vector>

// Extract test fragments from spec files
int extractSpecFragments(int verboseLevel = 0);

// Regenerate all test fragment files with current compiler output
int regenerateFragments(int verboseLevel = 0);

// Regenerate a subset of fragments (used for parallel processing)
int regenerateFragmentsSubset(const std::string &outputFile, const std::vector<std::string> &testFiles, int verboseLevel = 0);
