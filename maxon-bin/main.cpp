#include "lexer.h"
#include "parser.h"
#include "codegen.h"
#include "semantic_analyzer.h"
#include "error_formatter.h"
#include <fstream>
#include <sstream>
#include <iostream>
#include <string>
#include <filesystem>
#include <algorithm>

#ifdef _WIN32
#include <windows.h>
#else
#include <linux/limits.h>
#include <unistd.h>
#endif

// Forward declarations
std::string readFile(const std::string& filename);

// Get the directory containing the compiler executable
std::string getExecutableDirectory() {
#ifdef _WIN32
    char buffer[MAX_PATH];
    GetModuleFileNameA(NULL, buffer, MAX_PATH);
    std::string execPath(buffer);
    size_t pos = execPath.find_last_of("\\/");
    return (pos != std::string::npos) ? execPath.substr(0, pos) : ".";
#else
    char buffer[PATH_MAX];
    ssize_t len = readlink("/proc/self/exe", buffer, sizeof(buffer)-1);
    if (len != -1) {
        buffer[len] = '\0';
        std::string execPath(buffer);
        size_t pos = execPath.find_last_of('/');
        return (pos != std::string::npos) ? execPath.substr(0, pos) : ".";
    }
    return ".";
#endif
}

// Find stdlib directory (try current dir first, then relative to executable)
std::string findStdlibDirectory() {
    // Try current directory first
    if (std::filesystem::exists("stdlib")) {
        return "stdlib";
    }
    
    // Try relative to executable (../../stdlib from build/bin/)
    std::string execDir = getExecutableDirectory();
    std::filesystem::path stdlibPath = std::filesystem::path(execDir) / ".." / ".." / "stdlib";
    if (std::filesystem::exists(stdlibPath)) {
        return stdlibPath.string();
    }
    
    // Fall back to current directory
    return "stdlib";
}

// Derive namespace from file path
// Example: "stdlib/fmt/integer.maxon" -> "stdlib::fmt"
// Example: "main.maxon" or "./main.maxon" -> "" (global namespace)
std::string deriveNamespace(const std::string& filePath) {
    std::filesystem::path p(filePath);
    
    // For stdlib files, extract only the stdlib-relative part
    std::string pathStr = p.string();
    size_t stdlibPos = pathStr.find("stdlib");
    if (stdlibPos != std::string::npos) {
        // Extract from "stdlib" onwards
        std::string stdlibRelative = pathStr.substr(stdlibPos);
        p = std::filesystem::path(stdlibRelative);
    }
    
    std::filesystem::path dir = p.parent_path();
    
    // Convert to string and normalize
    std::string ns = dir.string();
    
    // If empty or just ".", return empty (global namespace)
    if (ns.empty() || ns == ".") {
        return "";
    }
    
    // Replace path separators with :: (internal separator)
    std::replace(ns.begin(), ns.end(), '/', ':');
    std::replace(ns.begin(), ns.end(), '\\', ':');
    
    // Convert single : to :: (since we replaced / or \ with :)
    std::string result;
    for (size_t i = 0; i < ns.size(); i++) {
        if (ns[i] == ':') {
            result += "::";
            // Skip consecutive separators
            while (i + 1 < ns.size() && (ns[i + 1] == ':' || ns[i + 1] == '/' || ns[i + 1] == '\\')) {
                i++;
            }
        } else if (ns[i] != '.' || i != 0) {  // Skip leading dot
            result += ns[i];
        }
    }
    
    return result;
}

// Find all .maxon files in a directory recursively
std::vector<std::string> findMaxonFiles(const std::string& directory) {
    std::vector<std::string> files;
    
    if (!std::filesystem::exists(directory)) {
        return files;
    }
    
    for (const auto& entry : std::filesystem::recursive_directory_iterator(directory)) {
        if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
            files.push_back(entry.path().string());
        }
    }
    
    return files;
}

// Quick scan a file to extract function names (without full parsing)
std::vector<std::string> extractFunctionNames(const std::string& filePath) {
    std::vector<std::string> functionNames;
    
    try {
        std::string source = readFile(filePath);
        Lexer lexer(source);
        std::vector<Token> tokens = lexer.tokenize();
        
        // Simple scan for "function <name>" patterns
        for (size_t i = 0; i < tokens.size(); i++) {
            if (tokens[i].type == TokenType::FUNCTION && i + 1 < tokens.size()) {
                if (tokens[i + 1].type == TokenType::IDENTIFIER) {
                    functionNames.push_back(tokens[i + 1].value);
                }
            }
        }
    } catch (...) {
        // Ignore errors - just return empty list
    }
    
    return functionNames;
}

// Find stdlib files that define specific functions
std::vector<std::string> findStdlibFilesDefining(const std::set<std::string>& functionNames) {
    std::vector<std::string> resultFiles;
    std::string stdlibDir = findStdlibDirectory();
    std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);
    
    for (const auto& file : stdlibFiles) {
        std::vector<std::string> definedFunctions = extractFunctionNames(file);
        
        // Check if this file defines any of the functions we're looking for
        // The function names in the input set might be qualified (e.g., "stdlib::fmt::format_int_array")
        // but definedFunctions contains only unqualified names (e.g., "format_int_array")
        for (const auto& funcName : functionNames) {
            // Extract the unqualified part (after the last ::)
            std::string unqualifiedName = funcName;
            size_t lastSep = funcName.rfind("::");
            if (lastSep != std::string::npos) {
                unqualifiedName = funcName.substr(lastSep + 2);
            }
            
            if (std::find(definedFunctions.begin(), definedFunctions.end(), unqualifiedName) != definedFunctions.end()) {
                resultFiles.push_back(file);
                break;  // Only add each file once
            }
        }
    }
    
    return resultFiles;
}

std::string readFile(const std::string& filename) {
    std::ifstream file(filename);
    if (!file) {
        throw std::runtime_error("Could not open file: " + filename);
    }
    
    std::stringstream buffer;
    std::string line;
    
    // Read line by line and stop at "---"
    while (std::getline(file, line)) {
        if (line == "---") {
            break;
        }
        buffer << line << '\n';
    }
    
    return buffer.str();
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <command> [options]" << std::endl;
        std::cerr << "\nCommands:" << std::endl;
        std::cerr << "  compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
        std::cerr << "                 Compile Maxon source files" << std::endl;
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
    
    // Check if the command is "compile"
    if (command != "compile") {
        std::cerr << "Error: Unknown command '" << command << "'" << std::endl;
        std::cerr << "Available commands: compile" << std::endl;
        return 1;
    }
    
    // Shift arguments to skip the command
    argc--;
    argv++;
    
    if (argc < 1) {
        std::cerr << "Error: No input files specified" << std::endl;
        std::cerr << "Usage: maxon compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
        return 1;
    }
    
    std::vector<std::string> inputFiles;
    std::string outputFile = "output.exe";
    bool emitLLVM = false;
    bool compileOnly = false;
    bool optimize = false;
    bool debugInfo = false;
    bool verbose = false;
    
    // Parse command line arguments
    for (int i = 1; i < argc; i++) {
        std::string arg = argv[i];
        if (arg == "--emit-llvm") {
            emitLLVM = true;
        } else if (arg == "-c") {
            compileOnly = true;
        } else if (arg == "-O") {
            optimize = true;
        } else if (arg == "--debug" || arg == "-g") {
            debugInfo = true;
        } else if (arg == "--verbose" || arg == "-v") {
            verbose = true;
        } else if (arg == "-o" && i + 1 < argc) {
            outputFile = argv[++i];
        } else if (arg[0] != '-') {
            // This is an input file
            inputFiles.push_back(arg);
        }
    }
    
    if (inputFiles.empty()) {
        std::cerr << "Error: No input files specified" << std::endl;
        return 1;
    }
    
    try {
        // Compile each input file
        std::vector<std::unique_ptr<ProgramAST>> programs;
        std::vector<std::string> sources;  // Keep sources for error formatting
        
        for (const auto& inputFile : inputFiles) {
            // Read source file
            std::string source = readFile(inputFile);
            sources.push_back(source);
            if (verbose) {
                std::cout << "Compiling " << inputFile << "..." << std::endl;
            }
            
            // Lexical analysis
            Lexer lexer(source);
            std::vector<Token> tokens = lexer.tokenize();
            if (verbose) {
                std::cout << "  Lexical analysis complete. Generated " << tokens.size() << " tokens." << std::endl;
            }
            
            // Parsing
            Parser parser(tokens);
            std::string fileNamespace = deriveNamespace(inputFile);
            parser.setDefaultNamespace(fileNamespace);
            std::unique_ptr<ProgramAST> program = parser.parse();
            if (verbose) {
                std::cout << "  Parsing complete." << std::endl;
                if (!fileNamespace.empty()) {
                    std::cout << "    File namespace: " << fileNamespace << std::endl;
                }
            }
            
            programs.push_back(std::move(program));
        }
        
        // Iterative compilation with stdlib auto-discovery
        // Keep track of all files and their parsed ASTs
        std::vector<std::string> allFiles = inputFiles;
        std::set<std::string> processedFiles(inputFiles.begin(), inputFiles.end());
        int iteration = 0;
        const int maxIterations = 10;  // Prevent infinite loops
        bool discoveredNewFiles = false;
        
        do {
            iteration++;
            discoveredNewFiles = false;
            
            // Merge all programs into one for this iteration
            std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
            for (auto& prog : programs) {
                for (auto& func : prog->functions) {
                    mergedProgram->functions.push_back(std::move(func));
                }
                for (auto& ns : prog->namespaces) {
                    mergedProgram->namespaces.push_back(std::move(ns));
                }
            }
            programs.clear();  // We've moved everything
            
            // Semantic analysis on merged program
            SemanticAnalyzer analyzer;
            std::vector<SemanticError> semanticErrors = analyzer.analyze(mergedProgram.get());
            
            if (!semanticErrors.empty()) {
                // Check if any errors are for undefined functions that might be in stdlib
                std::set<std::string> undefinedFunctions = analyzer.getUndefinedFunctions();
                
                if (!undefinedFunctions.empty() && iteration == 1) {
                    // First iteration - try to auto-discover stdlib files
                    if (verbose) {
                        std::cout << "Looking for undefined functions in stdlib: ";
                        for (const auto& func : undefinedFunctions) {
                            std::cout << func << " ";
                        }
                        std::cout << std::endl;
                    }
                    
                    std::vector<std::string> stdlibFiles = findStdlibFilesDefining(undefinedFunctions);
                    
                    if (!stdlibFiles.empty()) {
                        if (verbose) {
                            std::cout << "Found " << stdlibFiles.size() << " stdlib file(s) to auto-compile:" << std::endl;
                            for (const auto& file : stdlibFiles) {
                                std::cout << "  " << file << std::endl;
                            }
                        }
                        
                        // Add stdlib files to compilation
                        for (const auto& file : stdlibFiles) {
                            if (processedFiles.find(file) == processedFiles.end()) {
                                processedFiles.insert(file);
                                allFiles.push_back(file);
                                discoveredNewFiles = true;
                                
                                // Read and parse stdlib file
                                std::string source = readFile(file);
                                sources.push_back(source);
                                
                                Lexer lexer(source);
                                std::vector<Token> tokens = lexer.tokenize();
                                
                                Parser parser(tokens);
                                std::string fileNamespace = deriveNamespace(file);
                                parser.setDefaultNamespace(fileNamespace);
                                std::unique_ptr<ProgramAST> program = parser.parse();
                                
                                programs.push_back(std::move(program));
                            }
                        }
                        
                        // Retry compilation with new files
                        continue;
                    }
                }
                
                // Errors that couldn't be resolved by auto-discovery
                std::cerr << "\n=== Compilation Failed ===" << std::endl;
                std::cerr << "Found " << semanticErrors.size() << " error" 
                         << (semanticErrors.size() == 1 ? "" : "s") << ":\n" << std::endl;
                
                for (const auto& error : semanticErrors) {
                    // Find which source file the error came from
                    // For now, just use the first source
                    std::string formattedError = ErrorFormatter::formatError(
                        error.message,
                        sources[0],
                        error.line,
                        error.column,
                        "Semantic Error"
                    );
                    std::cerr << formattedError << std::endl;
                }
                return 1;
            }
            
            if (verbose) {
                std::cout << "Semantic analysis complete." << std::endl;
            }
            
            // Success - rebuild programs from all sources for codegen
            programs.clear();
            for (const auto& file : allFiles) {
                std::string source = readFile(file);
                Lexer lexer(source);
                std::vector<Token> tokens = lexer.tokenize();
                Parser parser(tokens);
                std::string fileNamespace = deriveNamespace(file);
                parser.setDefaultNamespace(fileNamespace);
                std::unique_ptr<ProgramAST> program = parser.parse();
                programs.push_back(std::move(program));
            }
            break;  // Exit iteration loop on success
            
        } while (discoveredNewFiles && iteration < maxIterations);
        
        // Final merge for codegen
        std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
        for (auto& prog : programs) {
            for (auto& func : prog->functions) {
                mergedProgram->functions.push_back(std::move(func));
            }
            for (auto& ns : prog->namespaces) {
                mergedProgram->namespaces.push_back(std::move(ns));
            }
        }
        
        // Code generation
        std::string moduleName = inputFiles.size() == 1 ? inputFiles[0] : "merged";
        CodeGenerator codegen(moduleName, debugInfo, verbose);
        codegen.generate(mergedProgram.get(), !compileOnly);  // Don't need entry point if just compiling
        if (verbose) {
            std::cout << "Code generation complete." << std::endl;
        }
        
        // Run optimization passes if requested
        if (optimize) {
            codegen.optimize();
            if (verbose) {
                std::cout << "Optimization complete." << std::endl;
            }
        }
        
        // Determine executable output filename
        std::string exeOutputFile = outputFile;
        
        // Output
        if (emitLLVM) {
            // Write LLVM IR to file if -o specified, otherwise print to stdout
            if (outputFile != "output.exe") {
                codegen.writeIRToFile(outputFile);
                if (verbose) {
                    std::cout << "\nLLVM IR written to: " << outputFile << std::endl;
                }
                
                // When emitting LLVM IR to a file, also generate executable with .exe extension
                // Remove any existing extension and add .exe
                size_t lastDot = outputFile.find_last_of('.');
                if (lastDot != std::string::npos) {
                    exeOutputFile = outputFile.substr(0, lastDot) + ".exe";
                } else {
                    exeOutputFile = outputFile + ".exe";
                }
            } else {
                std::cout << "\n=== LLVM IR ===" << std::endl;
                codegen.printIR();
            }
        }
        
        if (compileOnly) {
            // Just compile to object file
            codegen.writeObjectFile(outputFile);
            if (verbose) {
                std::cout << "\nCompilation successful!" << std::endl;
                std::cout << "Output: " << outputFile << std::endl;
            }
        } else {
            // Compile and link to executable using LLVM's linker
            codegen.writeExecutable(exeOutputFile);
            if (verbose) {
                std::cout << "\nCompilation and linking successful!" << std::endl;
                std::cout << "Executable: " << exeOutputFile << std::endl;
            }
        }
        
    } catch (const std::exception& e) {
        std::cerr << "\n=== Compilation Failed ===" << std::endl;
        std::cerr << e.what() << std::endl;
        std::cerr << "\nCompilation terminated due to errors." << std::endl;
        return 1;
    }
    
    return 0;
}
