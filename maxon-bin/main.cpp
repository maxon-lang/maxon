#include "compiler.h"
#include "test_regenerate.h"
#include "test_runner.h"
#include "temp_runner.h"
#include "self_test.h"

#include <cstring>
#include <iostream>
#include <string>
#include <fstream>
#include <filesystem>
#include <map>
#include <fstream>
#include <filesystem>

namespace fs = std::filesystem;

int generateDocs() {
    // Get keyword metadata
    auto keywordInfo = Lexer::getKeywordInfo();
    
    // Group keywords by category
    std::map<KeywordCategory, std::vector<Lexer::KeywordInfo>> categorizedKeywords;
    for (const auto& info : keywordInfo) {
        categorizedKeywords[info.category].push_back(info);
    }
    
    // Create docs/Content directory if it doesn't exist
    fs::create_directories("../docs/Content");
    
    // Generate types.md
    {
        std::ofstream out("../docs/Content/types.md");
        out << "# Types\n\n";
        out << "Maxon supports the following built-in types:\n\n";
        for (const auto& info : categorizedKeywords[KeywordCategory::Type]) {
            out << "## " << info.name << "\n\n";
            out << info.description << "\n\n";
        }
    }
    
    // Generate control-flow.md
    {
        std::ofstream out("../docs/Content/control-flow.md");
        out << "# Control Flow\n\n";
        out << "Maxon provides the following control flow statements:\n\n";
        for (const auto& info : categorizedKeywords[KeywordCategory::ControlFlow]) {
            out << "## " << info.name << "\n\n";
            out << info.description << "\n\n";
        }
    }
    
    // Generate math.md (update existing file)
    {
        std::ofstream out("../docs/Content/math.md");
        out << "# Mathematical Functions\n\n";
        out << "Maxon provides built-in mathematical functions as language keywords. These functions are compiled to efficient LLVM intrinsics.\n\n";
        out << "## Type Conversion Functions\n\n";
        out << "Convert floating-point numbers to integers using different rounding strategies.\n\n";
        
        for (const auto& info : categorizedKeywords[KeywordCategory::MathIntrinsic]) {
            out << "### " << info.name << "\n\n";
            out << info.description << "\n\n";
            out << "**Signature:** `" << info.name << "(x float) ";
            // Get return type from MathIntrinsicInfo
            auto mathInfo = Lexer::getMathIntrinsicInfo(info.name);
            if (mathInfo) {
                out << mathInfo->returnType;
            } else {
                out << "float"; // fallback
            }
            out << "`\n\n";
        }
    }
    
    // Generate variables.md
    {
        std::ofstream out("../docs/Content/variables.md");
        out << "# Variables and Declarations\n\n";
        out << "Maxon supports variable declarations with different storage semantics:\n\n";
        for (const auto& info : categorizedKeywords[KeywordCategory::Declaration]) {
            out << "## " << info.name << "\n\n";
            out << info.description << "\n\n";
        }
    }
    
    std::cout << "Generated documentation in docs/Content/" << std::endl;
    return 0;
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <command> [options]" << std::endl;
        std::cerr << "\nCommands:" << std::endl;
        std::cerr << "  compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
        std::cerr << "                 Compile Maxon source files" << std::endl;
        std::cerr << "  self-test [--verbose]" << std::endl;
        std::cerr << "                 Run compiler self-tests" << std::endl;
        std::cerr << "  regen-fragments" << std::endl;
        std::cerr << "                 Regenerate all test fragments" << std::endl;
        std::cerr << "  test-fragments [options]" << std::endl;
        std::cerr << "                 Run all test fragments (shows only failures and summary)" << std::endl;
        std::cerr << "  generate-docs" << std::endl;
        std::cerr << "                 Generate documentation from keyword metadata" << std::endl;
        std::cerr << "  <input.maxon>  Compile and run source file (no artifacts left on disk)" << std::endl;
        std::cerr << "\nOptions for self-test:" << std::endl;
        std::cerr << "  --verbose, -v  Show detailed test output" << std::endl;
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

    if (command == "generate-docs") {
        return generateDocs();
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
