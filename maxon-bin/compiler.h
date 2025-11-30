#pragma once

#include "file_utils.h"
#include "logger.h"
#include "parser.h"

#include <memory>
#include <string>
#include <vector>

// Forward declaration
class CompilerStats;

struct CompilationOptions {
	std::vector<std::string> inputFiles;
	std::string outputFile; // Internal use only, not exposed via CLI
	bool emitIR = false;
	bool compileOnly = false;
	bool optimize = false;
	bool debugInfo = false;
	bool profile = false;
	bool trackAllocs = false; // Log memory allocations for debugging
	bool showStats = false;	  // Show compilation statistics
	int verboseLevel = 0;	  // 0 = silent, 1 = progress, 2 = detailed, 3 = trace
};

std::unique_ptr<ProgramAST> parseFile(const std::string &filePath, Logger &logger, CompilerStats *stats = nullptr);
std::string compileProgram(const CompilationOptions &options);
