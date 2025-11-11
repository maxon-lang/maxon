#include "lexer.h"
#include "parser.h"
#include "codegen.h"
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
    buffer << file.rdbuf();
    return buffer.str();
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <input.maxon> [options]" << std::endl;
        std::cerr << "Options:" << std::endl;
        std::cerr << "  --emit-llvm    Print LLVM IR to stdout" << std::endl;
        std::cerr << "  -o <output>    Specify output executable (default: output.exe)" << std::endl;
        std::cerr << "  -c             Compile only (generate object file, don't link)" << std::endl;
        return 1;
    }
    
    std::string inputFile = argv[1];
    std::string outputFile = "output.exe";
    bool emitLLVM = false;
    bool compileOnly = false;
    
    // Parse command line arguments
    for (int i = 2; i < argc; i++) {
        std::string arg = argv[i];
        if (arg == "--emit-llvm") {
            emitLLVM = true;
        } else if (arg == "-c") {
            compileOnly = true;
        } else if (arg == "-o" && i + 1 < argc) {
            outputFile = argv[++i];
        }
    }
    
    try {
        // Read source file
        std::string source = readFile(inputFile);
        std::cout << "Compiling " << inputFile << "..." << std::endl;
        
        // Lexical analysis
        Lexer lexer(source);
        std::vector<Token> tokens = lexer.tokenize();
        std::cout << "Lexical analysis complete. Generated " << tokens.size() << " tokens." << std::endl;
        
        // Parsing
        Parser parser(tokens);
        std::unique_ptr<ProgramAST> program = parser.parse();
        std::cout << "Parsing complete." << std::endl;
        
        // Code generation
        CodeGenerator codegen(inputFile);
        codegen.generate(program.get());
        std::cout << "Code generation complete." << std::endl;
        
        // Output
        if (emitLLVM) {
            std::cout << "\n=== LLVM IR ===" << std::endl;
            codegen.printIR();
        }
        
        if (compileOnly) {
            // Just compile to object file
            codegen.writeObjectFile(outputFile);
            std::cout << "\nCompilation successful!" << std::endl;
            std::cout << "Output: " << outputFile << std::endl;
        } else {
            // Compile and link to executable using LLVM's linker
            codegen.writeExecutable(outputFile);
            std::cout << "\nCompilation and linking successful!" << std::endl;
            std::cout << "Output: " << outputFile << std::endl;
        }
        
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
    
    return 0;
}
