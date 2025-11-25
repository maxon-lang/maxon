#include "temp_runner.h"
#include "compiler.h"

#include <cstdlib>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>

int compileAndRunTemporary(const std::string &sourceFile) {
	try {
		std::filesystem::path tempDir = "temp";
		std::filesystem::create_directories(tempDir);
		std::string tempExe = (tempDir / ("maxon_temp_" + std::to_string(std::time(nullptr)) + ".exe")).string();

		CompilationOptions options;
		options.inputFiles = {sourceFile};
		options.outputFile = tempExe;
		options.optimize = true;
		options.debugInfo = false;
		options.verboseLevel = 0;
		options.emitLLVM = false;
		options.compileOnly = false;

		std::string executablePath = compileProgram(options);

		int exitCode = system(executablePath.c_str());

		std::filesystem::remove(executablePath);

		return exitCode;
	} catch (const std::exception &e) {
		std::cerr << "Error: " << e.what() << std::endl;
		return 1;
	}
}
