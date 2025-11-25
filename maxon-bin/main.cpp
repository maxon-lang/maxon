#include "compiler.h"
#include "docs_generator.h"
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
	std::cerr << "  self-test [options]" << std::endl;
	std::cerr << "                 Run compiler self-tests" << std::endl;
	std::cerr << "  extract-specs [options]" << std::endl;
	std::cerr << "                 Extract test fragments from spec files" << std::endl;
	std::cerr << "  regen-fragments [options]" << std::endl;
	std::cerr << "                 Regenerate all test fragments" << std::endl;
	std::cerr << "  generate-docs [options]" << std::endl;
	std::cerr << "                 Generate HTML documentation from spec files" << std::endl;
	std::cerr << "  test-fragments [options]" << std::endl;
	std::cerr << "                 Run all test fragments (shows only failures and summary)" << std::endl;
	std::cerr << "  test <file.test> [options]" << std::endl;
	std::cerr << "                 Run a single test file and verify expected output" << std::endl;
	std::cerr << "  <input.maxon|.test>  Compile and run source file (no artifacts left on disk)" << std::endl;
	std::cerr << "\nVerbosity (applies to most commands):" << std::endl;
	std::cerr << "  -v             Level 1 verbosity (progress, basic output)" << std::endl;
	std::cerr << "  -vv            Level 2 verbosity (detailed information)" << std::endl;
	std::cerr << "\nOptions for compile:" << std::endl;
	std::cerr << "  --emit-llvm    Generate .ll file alongside executable" << std::endl;
	std::cerr << "  -c             Compile only (generate object file, don't link)" << std::endl;
	std::cerr << "  -O             Enable optimizations" << std::endl;
	std::cerr << "  --debug, -g    Generate debug information" << std::endl;
	std::cerr << "\nOptions for test:" << std::endl;
	std::cerr << "  --update, -u   Update the test file with actual MaxoncStderr output" << std::endl;
	std::cerr << "\nGeneral options:" << std::endl;
	std::cerr << "  --help, -h     Show this help message" << std::endl;
}

int main(int argc, char *argv[]) {
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
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			}
		}
		return runSelfTest(verboseLevel);
	}
	if (command == "extract-specs") {
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			}
		}
		return extractSpecFragments(verboseLevel);
	}

	if (command == "generate-docs") {
		return DocsGenerator::generateDocumentation();
	}

	if (command == "regen-fragments") {
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			}
		}
		return regenerateFragments(verboseLevel);
	} // Internal command used by parallel regen runner
	if (command == "regen-fragments-subset") {
		if (argc < 4) {
			std::cerr << "Error: regen-fragments-subset requires output file and test files" << std::endl;
			return 1;
		}

		std::string outputFile = argv[2];
		int verboseLevel = 0;
		std::vector<std::string> testFiles;

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				testFiles.push_back(arg);
			}
		}

		return regenerateFragmentsSubset(outputFile, testFiles, verboseLevel);
	}

	if (command == "test-fragments") {
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			}
		}
		return runTestFragments(verboseLevel);
	}
	if (command == "test") {
		if (argc < 3) {
			std::cerr << "Error: test command requires a test file" << std::endl;
			std::cerr << "Usage: maxon test <file.test> [options]" << std::endl;
			return 1;
		}

		int verboseLevel = 0;
		bool update = false;
		std::string testFile = argv[2];

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else if (arg == "--update" || arg == "-u") {
				update = true;
			}
		}

		return runSingleTestFile(testFile, verboseLevel, update);
	}

	// Internal command used by parallel test runner
	if (command == "test-fragments-subset") {
		if (argc < 4) {
			std::cerr << "Error: test-fragments-subset requires output file and test files" << std::endl;
			return 1;
		}

		std::string outputFile = argv[2];
		int verboseLevel = 0;
		std::vector<std::string> testFiles;

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vv") {
				verboseLevel = 2;
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				testFiles.push_back(arg);
			}
		}

		return runTestFragmentsSubset(testFiles, outputFile, verboseLevel);
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
		} else if (arg == "-vv") {
			options.verboseLevel = 2;
		} else if (arg == "-v" || arg == "--verbose") {
			options.verboseLevel = std::max(options.verboseLevel, 1);
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
		std::cerr << "Error: " << e.what() << std::endl;
		return 1;
	}

	return 0;
}
