#include "compiler.h"
#include "self_test.h"
#include "temp_runner.h"
#include "test_regenerate.h"
#include "test_runner.h"

#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <string>

#include <llvm/Support/TargetSelect.h>

#ifdef _WIN32
#include <io.h>
#define isatty _isatty
#define fileno _fileno
#else
#include <unistd.h>
#endif

namespace fs = std::filesystem;

void printHelp(const char *programName) {
	std::cerr << "Usage: " << programName << " <command> [options]" << std::endl;
	std::cerr << "\nCommands:" << std::endl;
	std::cerr << "  compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
	std::cerr << "                 Compile Maxon source files" << std::endl;
	std::cerr << "  self-test [--verbose]" << std::endl;
	std::cerr << "                 Run compiler self-tests" << std::endl;
	std::cerr << "  extract-specs" << std::endl;
	std::cerr << "                 Extract test fragments from spec files" << std::endl;
	std::cerr << "  regen-fragments [--verbose|-v]" << std::endl;
	std::cerr << "                 Regenerate all test fragments" << std::endl;
	std::cerr << "  test-fragments [options]" << std::endl;
	std::cerr << "                 Run all test fragments (shows only failures and summary)" << std::endl;
	std::cerr << "  test <file.test>" << std::endl;
	std::cerr << "                 Run a single test file and verify expected output" << std::endl;
	std::cerr << "  <input.maxon|.test>  Compile and run source file (no artifacts left on disk)" << std::endl;
	std::cerr << "\nOptions for self-test:" << std::endl;
	std::cerr << "  --verbose, -v  Show detailed test output" << std::endl;
	std::cerr << "\nOptions for test-fragments:" << std::endl;
	std::cerr << "  --verbose, -v  Show all tests including passes" << std::endl;
	std::cerr << "\nOptions for test:" << std::endl;
	std::cerr << "  --verbose, -v  Show detailed test output" << std::endl;
	std::cerr << "  --update, -u   Update the test file with actual MaxoncStderr output" << std::endl;
	std::cerr << "\nOptions for compile:" << std::endl;
	std::cerr << "  --emit-llvm    Generate .ll file alongside executable" << std::endl;
	std::cerr << "  -c             Compile only (generate object file, don't link)" << std::endl;
	std::cerr << "  -O             Enable optimizations" << std::endl;
	std::cerr << "  --debug, -g    Generate debug information" << std::endl;
	std::cerr << "  --verbose, -v  Show compilation progress messages" << std::endl;
	std::cerr << "\nGeneral options:" << std::endl;
	std::cerr << "  --help, -h     Show this help message" << std::endl;
}

int main(int argc, char *argv[]) {
	// Initialize LLVM targets once at program startup
	// This prevents race conditions when multiple worker processes compile in parallel
	llvm::InitializeAllTargetInfos();
	llvm::InitializeAllTargets();
	llvm::InitializeAllTargetMCs();
	llvm::InitializeAllAsmParsers();
	llvm::InitializeAllAsmPrinters();

	// Check for help flag first
	for (int i = 1; i < argc; ++i) {
		std::string arg = argv[i];
		if (arg == "--help" || arg == "-h") {
			printHelp(argv[0]);
			return 0;
		}
	}

	if (argc < 2) {
		// Check if stdin has data (not a TTY)
		if (!isatty(fileno(stdin))) {
			return compileAndRunFromStdin();
		}

		printHelp(argv[0]);
		return 1;
	}

	std::string command = argv[1];

	if (command == "self-test") {
		bool verbose = false;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--verbose" || arg == "-v") {
				verbose = true;
			}
		}
		return runSelfTest(verbose);
	}

	if (command == "extract-specs") {
		return extractSpecFragments();
	}

	if (command == "regen-fragments") {
		bool verbose = false;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--verbose" || arg == "-v") {
				verbose = true;
			}
		}
		return regenerateFragments(verbose);
	}

	// Internal command used by parallel regen runner
	if (command == "regen-fragments-subset") {
		if (argc < 4) {
			std::cerr << "Error: regen-fragments-subset requires output file and test files" << std::endl;
			return 1;
		}

		std::string outputFile = argv[2];
		bool verbose = false;
		std::vector<std::string> testFiles;

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--verbose" || arg == "-v") {
				verbose = true;
			} else {
				testFiles.push_back(arg);
			}
		}

		return regenerateFragmentsSubset(outputFile, testFiles, verbose);
	}

	if (command == "test-fragments") {
		bool verbose = false;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--verbose" || arg == "-v") {
				verbose = true;
			}
		}
		return runTestFragments(verbose);
	}

	if (command == "test") {
		if (argc < 3) {
			std::cerr << "Error: test command requires a test file" << std::endl;
			std::cerr << "Usage: maxon test <file.test> [options]" << std::endl;
			return 1;
		}

		bool verbose = false;
		bool update = false;
		std::string testFile = argv[2];

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--verbose" || arg == "-v") {
				verbose = true;
			} else if (arg == "--update" || arg == "-u") {
				update = true;
			}
		}

		return runSingleTestFile(testFile, verbose, update);
	}

	// Internal command used by parallel test runner
	if (command == "test-fragments-subset") {
		if (argc < 4) {
			std::cerr << "Error: test-fragments-subset requires output file and test files" << std::endl;
			return 1;
		}

		std::string outputFile = argv[2];
		bool verbose = false;
		std::vector<std::string> testFiles;

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--verbose" || arg == "-v") {
				verbose = true;
			} else {
				testFiles.push_back(arg);
			}
		}

		return runTestFragmentsSubset(testFiles, outputFile, verbose);
	}

	if (argc == 2) {
		size_t len = command.length();
		bool isMaxonFile = (len >= 6 && command.substr(len - 6) == ".maxon");
		bool isTestFile = (len >= 5 && command.substr(len - 5) == ".test");
		if (isMaxonFile || isTestFile) {
			return compileAndRunTemporary(command);
		}
	}

	if (command != "compile") {
		std::cerr << "Error: Unknown command '" << command << "'" << std::endl;
		std::cerr << "Available commands: compile" << std::endl;
		return 1;
	}

	argc--;
	argv++;

	if (argc < 1) {
		std::cerr << "Error: No input files specified" << std::endl;
		std::cerr << "Usage: maxon compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
		return 1;
	}

	CompilationOptions options;

	for (int i = 1; i < argc; ++i) {
		std::string arg = argv[i];
		if (arg == "--emit-llvm") {
			options.emitLLVM = true;
		} else if (arg == "-c") {
			options.compileOnly = true;
		} else if (arg == "-O") {
			options.optimize = true;
		} else if (arg == "--debug" || arg == "-g") {
			options.debugInfo = true;
		} else if (arg == "--profile") {
			options.profile = true;
		} else if (arg == "--verbose" || arg == "-v") {
			options.verbose = true;
		} else if (!arg.empty() && arg[0] != '-') {
			options.inputFiles.push_back(arg);
		}
	}

	if (options.inputFiles.empty()) {
		std::cerr << "Error: No input files specified" << std::endl;
		return 1;
	}

	try {
		compileProgram(options);
	} catch (const std::exception &e) {
		return 1;
	}

	return 0;
}
