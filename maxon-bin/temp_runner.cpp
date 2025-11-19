#include "temp_runner.h"
#include "compiler.h"

#include <cstdlib>
#include <ctime>
#include <filesystem>
#include <iostream>
#include <string>

int compileAndRunTemporary(const std::string& sourceFile) {
    try {
        std::filesystem::path tempDir = std::filesystem::temp_directory_path();
        std::string tempExe = (tempDir / ("maxon_temp_" + std::to_string(std::time(nullptr)) + ".exe")).string();

        CompilationOptions options;
        options.inputFiles = {sourceFile};
        options.outputFile = tempExe;
        options.optimize = false;
        options.debugInfo = false;
        options.verbose = false;
        options.emitLLVM = false;
        options.compileOnly = false;

        std::string executablePath = compileProgram(options);

        int exitCode;
#ifdef _WIN32
        exitCode = system(executablePath.c_str());
#else
        exitCode = system(executablePath.c_str());
#endif

        std::filesystem::remove(executablePath);

        return exitCode;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
}
