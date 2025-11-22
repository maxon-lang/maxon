#pragma once

#include <string>

// Normalize LLVM IR by replacing file paths with a standard name
std::string normalizeIR(const std::string& ir);

// Display a string with escape sequences visible (for debugging)
std::string showWithEscapes(const std::string& s, size_t maxLen = 100);

// Show diff between two strings, highlighting where they differ
std::string showDiff(const std::string& expected, const std::string& actual);

// Execute a command with a timeout (in seconds)
// Returns: exit code of the process, or -1 if timeout occurred
int executeWithTimeout(const std::string& command, int timeoutSeconds);
