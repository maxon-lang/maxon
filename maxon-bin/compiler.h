#pragma once

#include "file_utils.h"
#include "parser.h"

#include <memory>
#include <string>
#include <vector>

struct CompilationOptions {
	std::vector<std::string> inputFiles;
	std::string outputFile; // Internal use only, not exposed via CLI
	bool emitLLVM = false;	// Now emits MIR instead of LLVM IR
	bool compileOnly = false;
	bool optimize = false;
	bool debugInfo = false;
	bool profile = false;
	int verboseLevel = 0; // 0 = silent, 1 = verbose, 2 = debug
};

std::unique_ptr<ProgramAST> parseFile(const std::string &filePath, int verboseLevel = 0);
std::string compileProgram(const CompilationOptions &options);
