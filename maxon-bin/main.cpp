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
#include <ctime>

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

// Build stdlib index: function name -> file path
// Cached in-memory index built on first use
std::map<std::string, std::string> getStdlibIndex() {
    static std::map<std::string, std::string> index;
    static bool initialized = false;
    
    if (!initialized) {
        std::string stdlibDir = findStdlibDirectory();
        std::vector<std::string> stdlibFiles = findMaxonFiles(stdlibDir);
        
        for (const auto& file : stdlibFiles) {
            std::vector<std::string> definedFunctions = extractFunctionNames(file);
            for (const auto& func : definedFunctions) {
                // Map function name to file path
                // If multiple files define same function, last one wins (shouldn't happen in well-organized stdlib)
                index[func] = file;
            }
        }
        
        initialized = true;
    }
    
    return index;
}

// Find stdlib files that define specific functions using the index
std::vector<std::string> findStdlibFilesDefining(const std::set<std::string>& functionNames) {
    std::vector<std::string> resultFiles;
    std::set<std::string> uniqueFiles; // Avoid duplicates
    const auto& index = getStdlibIndex();
    
    for (const auto& funcName : functionNames) {
        // Extract the unqualified part (after the last ::)
        std::string unqualifiedName = funcName;
        size_t lastSep = funcName.rfind("::");
        if (lastSep != std::string::npos) {
            unqualifiedName = funcName.substr(lastSep + 2);
        }
        
        // Look up in index
        auto it = index.find(unqualifiedName);
        if (it != index.end()) {
            if (uniqueFiles.insert(it->second).second) {
                resultFiles.push_back(it->second);
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

// Compilation options structure
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

// Shared compilation function for both compile modes
// Returns the output executable path on success, throws on error
std::string compileProgram(const CompilationOptions& options) {
    std::vector<std::unique_ptr<ProgramAST>> programs;
    std::vector<std::string> sources;  // Keep sources for error formatting
    std::vector<std::string> allFiles = options.inputFiles;
    std::set<std::string> processedFiles(options.inputFiles.begin(), options.inputFiles.end());
    
    // Initial compilation of input files
    for (const auto& inputFile : options.inputFiles) {
        std::string source = readFile(inputFile);
        sources.push_back(source);
        if (options.verbose) {
            std::cout << "Compiling " << inputFile << "..." << std::endl;
        }
        
        // Lexical analysis
        Lexer lexer(source);
        std::vector<Token> tokens = lexer.tokenize();
        if (options.verbose) {
            std::cout << "  Lexical analysis complete. Generated " << tokens.size() << " tokens." << std::endl;
        }
        
        // Parsing
        Parser parser(tokens);
        std::string fileNamespace = deriveNamespace(inputFile);
        parser.setDefaultNamespace(fileNamespace);
        std::unique_ptr<ProgramAST> program = parser.parse();
        if (options.verbose) {
            std::cout << "  Parsing complete." << std::endl;
            if (!fileNamespace.empty()) {
                std::cout << "    File namespace: " << fileNamespace << std::endl;
            }
        }
        
        programs.push_back(std::move(program));
    }
    
    // Iterative compilation with stdlib auto-discovery
    int iteration = 0;
    const int maxIterations = 10;
    bool discoveredNewFiles = false;
    
    do {
        iteration++;
        discoveredNewFiles = false;
        
        // Merge all programs into one for this iteration
        std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
        for (auto& prog : programs) {
            // Copy functions and namespaces (don't move them, so we can reuse in next iteration)
            for (auto& func : prog->functions) {
                // We need to move here to transfer ownership to mergedProgram
                // The programs will be rebuilt with new ASTs if we discover more files
                mergedProgram->functions.push_back(std::move(func));
            }
            for (auto& ns : prog->namespaces) {
                mergedProgram->namespaces.push_back(std::move(ns));
            }
        }
        // Note: We need to rebuild programs from source if we discover new files
        // So we clear programs here, but keep allFiles and sources for rebuilding
        programs.clear();
        
        // Semantic analysis on merged program
        SemanticAnalyzer analyzer;
        std::vector<SemanticError> semanticErrors = analyzer.analyze(mergedProgram.get());
        
        // Always check for undefined functions, even if semantic analysis passed
        // This allows us to discover transitive dependencies
        std::set<std::string> undefinedFunctions = analyzer.getUndefinedFunctions();
        
        if (!undefinedFunctions.empty()) {
                if (options.verbose) {
                    std::cout << "Looking for undefined functions in stdlib: ";
                    for (const auto& func : undefinedFunctions) {
                        std::cout << func << " ";
                    }
                    std::cout << std::endl;
                }
                
                std::vector<std::string> stdlibFiles = findStdlibFilesDefining(undefinedFunctions);
                
                if (!stdlibFiles.empty()) {
                    if (options.verbose) {
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
                            
                            std::string source = readFile(file);
                            sources.push_back(source);
                        }
                    }
                    
                    // If we discovered new files, rebuild all programs from sources
                    if (discoveredNewFiles) {
                        programs.clear();
                        for (size_t i = 0; i < allFiles.size(); i++) {
                            const auto& file = allFiles[i];
                            const auto& source = sources[i];
                            
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
        
        // Only report errors if they couldn't be resolved by stdlib discovery
        if (!semanticErrors.empty()) {
            std::cerr << "=== Compilation Failed ===" << std::endl;
            std::cerr << "Found " << semanticErrors.size() << " error" 
                     << (semanticErrors.size() == 1 ? "" : "s") << ":\n" << std::endl;
            
            for (const auto& error : semanticErrors) {
                std::string formattedError = ErrorFormatter::formatError(
                    error.message,
                    sources[0],
                    error.line,
                    error.column,
                    "Semantic Error"
                );
                std::cerr << formattedError << std::endl;
            }
            // Error has already been fully reported above
            throw std::runtime_error("");
        }
        
        if (options.verbose) {
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
        break;
        
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
    std::string moduleName = options.inputFiles.size() == 1 ? options.inputFiles[0] : "merged";
    CodeGenerator codegen(moduleName, options.debugInfo, options.verbose, options.profile);
    codegen.generate(mergedProgram.get(), !options.compileOnly);
    if (options.verbose) {
        std::cout << "Code generation complete." << std::endl;
    }
    
    // Run optimization passes if requested
    if (options.optimize) {
        codegen.optimize();
        if (options.verbose) {
            std::cout << "Optimization complete." << std::endl;
        }
    }
    
    // Determine executable output filename
    std::string exeOutputFile = options.outputFile;
    
    // Output
    if (options.emitLLVM) {
        if (options.outputFile != "output.exe") {
            codegen.writeIRToFile(options.outputFile);
            if (options.verbose) {
                std::cout << "\nLLVM IR written to: " << options.outputFile << std::endl;
            }
            
            // When emitting LLVM IR to a file, also generate executable with .exe extension
            size_t lastDot = options.outputFile.find_last_of('.');
            if (lastDot != std::string::npos) {
                exeOutputFile = options.outputFile.substr(0, lastDot) + ".exe";
            } else {
                exeOutputFile = options.outputFile + ".exe";
            }
        } else {
            std::cout << "\n=== LLVM IR ===" << std::endl;
            codegen.printIR();
        }
    }
    
    if (options.compileOnly) {
        codegen.writeObjectFile(options.outputFile);
        if (options.verbose) {
            std::cout << "\nCompilation successful!" << std::endl;
            std::cout << "Output: " << options.outputFile << std::endl;
        }
    } else {
        codegen.writeExecutable(exeOutputFile);
        if (options.verbose) {
            std::cout << "\nCompilation and linking successful!" << std::endl;
            std::cout << "Executable: " << exeOutputFile << std::endl;
        }
    }
    
    return exeOutputFile;
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <command> [options]" << std::endl;
        std::cerr << "\nCommands:" << std::endl;
        std::cerr << "  compile <input.maxon> [<input2.maxon> ...] [options]" << std::endl;
        std::cerr << "                 Compile Maxon source files" << std::endl;
        std::cerr << "  <input.maxon>  Compile and run source file (no artifacts left on disk)" << std::endl;
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
    
    // Check if single argument is a .maxon file - compile and run without leaving artifacts
    if (argc == 2 && command.length() >= 6 && command.substr(command.length() - 6) == ".maxon") {
        try {
            // Generate temporary executable name
            std::filesystem::path tempDir = std::filesystem::temp_directory_path();
            std::string tempExe = (tempDir / ("maxon_temp_" + std::to_string(std::time(nullptr)) + ".exe")).string();
            
            // Set up compilation options
            CompilationOptions options;
            options.inputFiles = {command};
            options.outputFile = tempExe;
            options.optimize = false;  // Don't optimize by default - user can use -O flag
            options.debugInfo = false;
            options.verbose = false;
            options.emitLLVM = false;
            options.compileOnly = false;
            
            // Compile using shared function
            std::string executablePath = compileProgram(options);
            
            // Run the temporary executable
            int exitCode;
#ifdef _WIN32
            exitCode = system(executablePath.c_str());
#else
            exitCode = system(executablePath.c_str());
#endif
            
            // Clean up temporary file
            std::filesystem::remove(executablePath);
            
            return exitCode;
            
        } catch (const std::exception& e) {
            std::cerr << "Error: " << e.what() << std::endl;
            return 1;
        }
    }
    
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
    
    // Parse command line arguments
    CompilationOptions options;
    
    for (int i = 1; i < argc; i++) {
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
        } else if (arg[0] != '-') {
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
        // If error message is empty, it means the error was already fully reported
        if (strlen(e.what()) > 0) {
            std::cerr << "=== Compilation Failed ===" << std::endl;
            std::cerr << e.what() << std::endl;
            std::cerr << "\nCompilation terminated due to errors." << std::endl;
        }
        return 1;
    }
    
    return 0;
}
