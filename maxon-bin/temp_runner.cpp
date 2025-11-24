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

int compileAndRunFromStdin() {
	try {
		// Read all input from stdin
		std::string sourceCode;
		std::string line;
		while (std::getline(std::cin, line)) {
			sourceCode += line + "\n";
		}

		if (sourceCode.empty()) {
			std::cerr << "Error: No input provided on stdin" << std::endl;
			return 1;
		}

		// Create a temporary source file
		std::filesystem::path tempDir = "temp";
		std::filesystem::create_directories(tempDir);
		std::string timeStr = std::to_string(std::time(nullptr));
		std::string tempSource = (tempDir / ("maxon_stdin_" + timeStr + ".maxon")).string();
		std::string tempExe = (tempDir / ("maxon_temp_" + timeStr + ".exe")).string();

		// Write stdin to temporary source file
		std::ofstream sourceFile(tempSource);
		if (!sourceFile) {
			std::cerr << "Error: Could not create temporary source file" << std::endl;
			return 1;
		}
		sourceFile << sourceCode;
		sourceFile.close();

		// Compile the temporary source file
		CompilationOptions options;
		options.inputFiles = {tempSource};
		options.outputFile = tempExe;
		options.optimize = true;
		options.debugInfo = false;
		options.verboseLevel = 0;
		options.emitLLVM = false;
		options.compileOnly = false;

		std::string executablePath = compileProgram(options);

	// Run the executable
	int exitCode = system(executablePath.c_str());		// Clean up temporary files
		std::filesystem::remove(executablePath);
		std::filesystem::remove(tempSource);

		return exitCode;

	} catch (const std::exception &e) {
		std::cerr << "Error: " << e.what() << std::endl;
		return 1;
	}
}
