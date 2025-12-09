#include "backend/pe_writer.h"
#include "backend/x86_codegen.h"
#include "compiler.h"
#include "docs_generator.h"
#include "lexer.h"
#include "lsp/lsp_server.h"
#include "lsp/transport.h"
#include "mir/mir_parser.h"
#include "mir/optimizer.h"
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
#include <unordered_map>

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
	std::cerr << "  lsp-server [options]" << std::endl;
	std::cerr << "                 Start language server protocol server (stdio)" << std::endl;
	std::cerr << "  benchmark <input.maxon> [options]" << std::endl;
	std::cerr << "                 Benchmark SIMD vs scalar lexer performance" << std::endl;
	std::cerr << "  <input.maxon|.test>  Compile and run source file (no artifacts left on disk)" << std::endl;
	std::cerr << "\nVerbosity (applies to most commands):" << std::endl;
	std::cerr << "  -v             Level 1 verbosity (progress, basic output)" << std::endl;
	std::cerr << "  -vv            Level 2 verbosity (detailed information)" << std::endl;
	std::cerr << "  -vvv           Level 3 verbosity (trace, deep debugging)" << std::endl;
	std::cerr << "Options for compile:" << std::endl;
	std::cerr << "  --emit-ir      Generate .ir file alongside executable" << std::endl;
	std::cerr << "  --emit-asm     Generate .asm file alongside executable" << std::endl;
	std::cerr << "  -c             Compile only (generate object file, don't link)" << std::endl;
	std::cerr << "  -O             Enable optimizations" << std::endl;
	std::cerr << "  --debug, -g    Generate debug information" << std::endl;
	std::cerr << "  --stats        Show compilation statistics" << std::endl;
	std::cerr << "\nOptions for benchmark:" << std::endl;
	std::cerr << "  -n <count>     Number of iterations (default: 100)" << std::endl;
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
		printHelp(argv[0]);
		return 1;
	}

	std::string command = argv[1];

	if (command == "self-test") {
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
			}
		}
		return runSelfTest(verboseLevel);
	}
	if (command == "extract-specs") {
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
			}
		}
		return extractSpecFragments(verboseLevel);
	}

	if (command == "generate-docs") {
		return DocsGenerator::generateDocumentation();
	}

	if (command == "lsp-server") {
		// Start the embedded LSP server using stdio transport
		auto transport = std::make_unique<maxon::lsp::StdioTransport>();
		maxon_lsp::LSPServer server(std::move(transport));
		return server.run();
	}

	if (command == "benchmark") {
		if (argc < 3) {
			std::cerr << "Usage: " << argv[0] << " benchmark <input.maxon> [-n iterations] [--pipeline]" << std::endl;
			return 1;
		}

		std::string inputFile = argv[2];
		int iterations = 100; // Default iterations
		bool pipelineMode = false;

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-n" && i + 1 < argc) {
				iterations = std::stoi(argv[++i]);
			} else if (arg == "--pipeline" || arg == "-p") {
				pipelineMode = true;
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
			}
		}

		// Read the source file
		std::ifstream file(inputFile);
		if (!file) {
			std::cerr << "Error: Cannot open file: " << inputFile << std::endl;
			return 1;
		}
		std::string source((std::istreambuf_iterator<char>(file)),
						   std::istreambuf_iterator<char>());

		// Run benchmark
		run_lexer_benchmark(source, inputFile, iterations);

		// Also run pipeline benchmark if requested
		if (pipelineMode) {
			run_pipeline_benchmark(source, inputFile, iterations);
		}
		return 0;
	}

	if (command == "verify-mir") {
		if (argc < 3) {
			std::cerr << "Usage: " << argv[0] << " verify-mir <file.mir>" << std::endl;
			return 1;
		}
		std::string mirFile = argv[2];
		auto result = mir::MIRParser::parseFile(mirFile);
		if (!result.errors.empty()) {
			for (const auto &err : result.errors) {
				std::cerr << err.toString() << std::endl;
			}
			return 1;
		}
		std::cout << "MIR file verified successfully: " << mirFile << std::endl;
		return 0;
	}

	// Compile a standalone MIR file directly to an executable
	if (command == "compile-mir") {
		if (argc < 3) {
			std::cerr << "Usage: " << argv[0] << " compile-mir <file.mir> [-o output.exe]" << std::endl;
			return 1;
		}
		std::string mirFile = argv[2];
		std::string outputFile;
		int verboseLevel = 0;

		for (int i = 3; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-o" && i + 1 < argc) {
				outputFile = argv[++i];
			} else if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
			}
		}

		// Default output file: same name as input but .exe
		if (outputFile.empty()) {
			size_t lastDot = mirFile.find_last_of('.');
			if (lastDot != std::string::npos) {
				outputFile = mirFile.substr(0, lastDot) + ".exe";
			} else {
				outputFile = mirFile + ".exe";
			}
		}

		// Parse MIR file
		auto result = mir::MIRParser::parseFile(mirFile);
		if (!result.errors.empty()) {
			for (const auto &err : result.errors) {
				std::cerr << err.toString() << std::endl;
			}
			return 1;
		}
		if (!result.module) {
			std::cerr << "Error: Failed to parse MIR file" << std::endl;
			return 1;
		}

		// Run PHI elimination pass before code generation
		mir::PhiEliminationPass phiElim;
		phiElim.run(*result.module);

		// Generate x86-64 code
		backend::X86CodeGen codegen(backend::CallingConv::Win64);
		codegen.generate(result.module.get());

		// Get generated code
		const auto &functionCodes = codegen.getFunctionCodes();
		const auto &dataSection = codegen.getDataSection();

		// Collect code and function offsets
		std::vector<uint8_t> codeBuffer;
		std::unordered_map<std::string, size_t> functionOffsets;

		for (const auto &func : functionCodes) {
			size_t funcOffset = codeBuffer.size();
			functionOffsets[func.name] = funcOffset;
			codeBuffer.insert(codeBuffer.end(), func.code.begin(), func.code.end());
		}

		// Apply function-to-function relocations
		for (const auto &func : functionCodes) {
			size_t funcBaseOffset = functionOffsets[func.name];
			for (const auto &reloc : func.relocations) {
				if (reloc.type == backend::Relocation::Type::FunctionCall) {
					auto it = functionOffsets.find(reloc.symbolName);
					if (it != functionOffsets.end()) {
						size_t patchOffset = funcBaseOffset + reloc.offset;
						size_t callEnd = patchOffset + 4;
						int32_t rel = static_cast<int32_t>(it->second - callEnd);
						codeBuffer[patchOffset] = rel & 0xFF;
						codeBuffer[patchOffset + 1] = (rel >> 8) & 0xFF;
						codeBuffer[patchOffset + 2] = (rel >> 16) & 0xFF;
						codeBuffer[patchOffset + 3] = (rel >> 24) & 0xFF;
					}
				}
			}
		}

		// Find entry point - look for main or _start
		size_t entryPoint = 0;
		auto mainIt = functionOffsets.find("main");
		if (mainIt != functionOffsets.end()) {
			entryPoint = mainIt->second;
		} else {
			auto startIt = functionOffsets.find("_start");
			if (startIt != functionOffsets.end()) {
				entryPoint = startIt->second;
			} else {
				std::cerr << "Error: No 'main' or '_start' function found in MIR" << std::endl;
				return 1;
			}
		}

		// Write PE executable
		backend::PeWriter pe;
		pe.addTextSection(codeBuffer);
		if (!dataSection.empty()) {
			pe.addDataSection(std::vector<uint8_t>(dataSection));
		}
		// Entry point RVA = base of .text (0x1000) + offset within code
		pe.setEntryPoint(0x1000 + static_cast<uint32_t>(entryPoint));

		// Add data relocations
		for (const auto &func : functionCodes) {
			size_t funcBaseOffset = functionOffsets[func.name];
			for (const auto &reloc : func.relocations) {
				if (reloc.type == backend::Relocation::Type::GlobalRef) {
					size_t patchOffset = funcBaseOffset + reloc.offset;
					// The addend contains the data offset
					pe.addDataRelocation(static_cast<uint32_t>(patchOffset),
										 static_cast<uint32_t>(reloc.addend));
				}
			}
		}

		if (!pe.write(outputFile)) {
			std::cerr << "Error: Failed to write executable" << std::endl;
			return 1;
		}

		if (verboseLevel >= 1) {
			std::cout << "Compiled " << mirFile << " -> " << outputFile << std::endl;
		}

		return 0;
	}
	if (command == "regen-fragments") {
		int verboseLevel = 0;
		for (int i = 2; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
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
			if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
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
			if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
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
			if (arg == "-vvv") {
				verboseLevel = 3;
			} else if (arg == "-vv") {
				verboseLevel = std::max(verboseLevel, 2);
			} else if (arg == "-v" || arg == "--verbose") {
				verboseLevel = std::max(verboseLevel, 1);
			} else if (arg == "--update" || arg == "-u") {
				update = true;
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
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

	// Handle shortcut: maxon <file.maxon|.test> [--track-allocs] [--stats]
	// Only applies when the first argument is NOT a known command
	if (command != "compile" && command != "self-test" && command != "extract-specs" &&
		command != "regen-fragments" && command != "generate-docs" && command != "test-fragments" &&
		command != "test" && command != "benchmark" && command != "compile-mir" &&
		command != "validate-specs" && command != "lsp") {
		std::string inputFile;
		bool trackAllocs = false;
		bool showStats = false;
		bool isValidShortcut = false;

		for (int i = 1; i < argc; ++i) {
			std::string arg = argv[i];
			if (arg == "--track-allocs") {
				trackAllocs = true;
			} else if (arg == "--stats") {
				showStats = true;
			} else if (!arg.empty() && arg[0] != '-') {
				size_t len = arg.length();
				bool isMaxonFile = (len >= 6 && arg.substr(len - 6) == ".maxon");
				bool isTestFile = (len >= 5 && arg.substr(len - 5) == ".test");
				if (isMaxonFile || isTestFile) {
					inputFile = arg;
					isValidShortcut = true;
				}
			} else {
				std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
				return 1;
			}
		}

		if (isValidShortcut && !inputFile.empty()) {
			return compileAndRunTemporary(inputFile, trackAllocs, showStats);
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
		if (arg == "--emit-ir") {
			options.emitIR = true;
		} else if (arg == "--emit-asm") {
			options.emitAsm = true;
		} else if (arg == "-c") {
			options.compileOnly = true;
		} else if (arg == "-O") {
			options.optimize = true;
		} else if (arg == "--debug" || arg == "-g") {
			options.debugInfo = true;
		} else if (arg == "--profile") {
			options.profile = true;
		} else if (arg == "--track-allocs") {
			options.trackAllocs = true;
		} else if (arg == "--stats") {
			options.showStats = true;
		} else if (arg == "-o" || arg == "--output") {
			if (i + 1 >= argc) {
				std::cerr << "Error: " << arg << " requires an argument" << std::endl;
				return 1;
			}
			options.outputFile = argv[++i];
		} else if (arg == "-vvv") {
			options.verboseLevel = 3;
		} else if (arg == "-vv") {
			options.verboseLevel = std::max(options.verboseLevel, 2);
		} else if (arg == "-v" || arg == "--verbose") {
			options.verboseLevel = std::max(options.verboseLevel, 1);
		} else if (!arg.empty() && arg[0] != '-') {
			options.inputFiles.push_back(arg);
		} else {
			std::cerr << "Error: Unknown option '" << arg << "'" << std::endl;
			return 1;
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
