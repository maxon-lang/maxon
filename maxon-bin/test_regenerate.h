#pragma once

#include <string>
#include <vector>

// Extract test fragments from spec files
int extractSpecFragments();

// Regenerate all test fragment files with current compiler output
int regenerateFragments(bool verbose = false);

// Regenerate a subset of fragments (used for parallel processing)
int regenerateFragmentsSubset(const std::string &outputFile, const std::vector<std::string> &testFiles, bool verbose = false);
