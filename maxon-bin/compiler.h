#pragma once

#include "file_utils.h"
#include "parser.h"

#include <memory>
#include <string>
#include <vector>

#include <llvm/Support/raw_ostream.h>

struct CompilationOptions {
    std::vector<std::string> inputFiles;
    std::string outputFile = "output.exe";
    bool emitLLVM = false;
    bool compileOnly = false;
    bool optimize = false;
    bool debugInfo = false;
    bool profile = false;
    bool verbose = false;
};

std::unique_ptr<ProgramAST> parseFile(const std::string& filePath, bool verbose);
std::string compileProgram(const CompilationOptions& options, llvm::raw_ostream* errorStream = nullptr);
