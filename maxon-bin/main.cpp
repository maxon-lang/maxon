#include "compiler.h"
#include "commands.h"

#include <cstring>
#include <iostream>
#include <string>

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <command> [options]" << std::endl;
        std::cerr << "\nCommands:" << std::endl;
        std::cerr << "  compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
        std::cerr << "                 Compile Maxon source files" << std::endl;
        std::cerr << "  regen-fragments" << std::endl;
        std::cerr << "                 Regenerate all test fragments" << std::endl;
        std::cerr << "  test-fragments [options]" << std::endl;
        std::cerr << "                 Run all test fragments (shows only failures and summary)" << std::endl;
        std::cerr << "  <input.maxon>  Compile and run source file (no artifacts left on disk)" << std::endl;
        std::cerr << "\nOptions for test-fragments:" << std::endl;
        std::cerr << "  --verbose, -v  Show all tests including passes" << std::endl;
        std::cerr << "\nOptions for compile:" << std::endl;
        std::cerr << "  --emit-llvm    Print LLVM IR to stdout" << std::endl;
        std::cerr << "  -o <output>    Specify output executable (default: output.exe)" << std::endl;
        std::cerr << "  -c             Compile only (generate object file, don't link)" << std::endl;
        std::cerr << "  -O             Enable optimizations" << std::endl;
        std::cerr << "  --debug, -g    Generate debug information" << std::endl;
        std::cerr << "  --verbose, -v  Show compilation progress messages" << std::endl;
        return 1;
    }

    std::string command = argv[1];

    if (command == "regen-fragments") {
        return regenerateFragments();
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

    if (argc == 2 && command.length() >= 6 && command.substr(command.length() - 6) == ".maxon") {
        return compileAndRunTemporary(command);
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
        } else if (arg == "-o" && i + 1 < argc) {
            options.outputFile = argv[++i];
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
    } catch (const std::exception& e) {
        if (std::strlen(e.what()) > 0) {
            std::cerr << "=== Compilation Failed ===" << std::endl;
            std::cerr << e.what() << std::endl;
            std::cerr << "\nCompilation terminated due to errors." << std::endl;
        }
        return 1;
    }

    return 0;
}
