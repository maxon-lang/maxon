#include "lexer.h"
#include "parser.h"
#include "codegen.h"
#include "semantic_analyzer.h"
#include "error_formatter.h"
#include <fstream>
#include <sstream>
#include <iostream>
#include <string>

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
        std::cerr << "Usage: " << argv[0] << " <input.maxon> [options]" << std::endl;
        std::cerr << "Options:" << std::endl;
        std::cerr << "  --emit-llvm    Print LLVM IR to stdout" << std::endl;
        std::cerr << "  -o <output>    Specify output executable (default: output.exe)" << std::endl;
        std::cerr << "  -c             Compile only (generate object file, don't link)" << std::endl;
        std::cerr << "  -O             Enable optimizations" << std::endl;
        std::cerr << "  --debug, -g    Generate debug information" << std::endl;
        std::cerr << "  --verbose, -v  Show compilation progress messages" << std::endl;
        return 1;
    }
    
    std::string inputFile = argv[1];
    std::string outputFile = "output.exe";
    bool emitLLVM = false;
    bool compileOnly = false;
    bool optimize = false;
    bool debugInfo = false;
    bool verbose = false;
    
    // Parse command line arguments
    for (int i = 2; i < argc; i++) {
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
        }
    }
    
    try {
        // Read source file
        std::string source = readFile(inputFile);
        if (verbose) {
            std::cout << "Compiling " << inputFile << "..." << std::endl;
        }
        
        // Lexical analysis
        Lexer lexer(source);
        std::vector<Token> tokens = lexer.tokenize();
        if (verbose) {
            std::cout << "Lexical analysis complete. Generated " << tokens.size() << " tokens." << std::endl;
        }
        
        // Parsing
        Parser parser(tokens);
        std::unique_ptr<ProgramAST> program = parser.parse();
        if (verbose) {
            std::cout << "Parsing complete." << std::endl;
        }
        
        // Semantic analysis
        SemanticAnalyzer analyzer;
        std::vector<SemanticError> semanticErrors = analyzer.analyze(program.get());
        
        if (!semanticErrors.empty()) {
            std::cerr << "\n=== Compilation Failed ===" << std::endl;
            std::cerr << "Found " << semanticErrors.size() << " error" 
                     << (semanticErrors.size() == 1 ? "" : "s") << ":\n" << std::endl;
            
            for (const auto& error : semanticErrors) {
                std::string formattedError = ErrorFormatter::formatError(
                    error.message,
                    source,
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
        
        // Code generation
        CodeGenerator codegen(inputFile, debugInfo, verbose);
        codegen.generate(program.get());
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
