#include "compiler.h"

#include "codegen.h"
#include "error_formatter.h"
#include "lexer.h"
#include "semantic_analyzer.h"

#include <iostream>
#include <set>
#include <vector>

std::unique_ptr<ProgramAST> parseFile(const std::string& filePath, bool verbose) {
    std::string source = readFile(filePath);

    if (verbose) {
        std::cout << "Compiling " << filePath << "..." << std::endl;
    }

    Lexer lexer(source);
    std::vector<Token> tokens = lexer.tokenize();
    if (verbose) {
        std::cout << "  Lexical analysis complete. Generated " << tokens.size() << " tokens." << std::endl;
    }

    Parser parser(tokens);
    std::string fileNamespace = deriveNamespace(filePath);
    parser.setDefaultNamespace(fileNamespace);
    std::unique_ptr<ProgramAST> program = parser.parse();

    if (verbose) {
        std::cout << "  Parsing complete." << std::endl;
        if (!fileNamespace.empty()) {
            std::cout << "    File namespace: " << fileNamespace << std::endl;
        }
    }

    return program;
}

std::string compileProgram(const CompilationOptions& options, llvm::raw_ostream* errorStream) {
    std::vector<std::unique_ptr<ProgramAST>> programs;
    std::vector<std::string> sources;
    std::vector<std::string> allFiles = options.inputFiles;
    std::set<std::string> processedFiles(options.inputFiles.begin(), options.inputFiles.end());

    for (const auto& inputFile : options.inputFiles) {
        sources.push_back(readFile(inputFile));
        programs.push_back(parseFile(inputFile, options.verbose));
    }

    int iteration = 0;
    const int maxIterations = 10;
    bool discoveredNewFiles = false;

    do {
        iteration++;
        discoveredNewFiles = false;

        std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
        for (auto& prog : programs) {
            for (auto& func : prog->functions) {
                mergedProgram->functions.push_back(std::move(func));
            }
            for (auto& ns : prog->namespaces) {
                mergedProgram->namespaces.push_back(std::move(ns));
            }
            for (auto& structDef : prog->structs) {
                mergedProgram->structs.push_back(std::move(structDef));
            }
        }

        programs.clear();

        SemanticAnalyzer analyzer;
        std::vector<SemanticError> semanticErrors = analyzer.analyze(mergedProgram.get());

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

                for (const auto& file : stdlibFiles) {
                    if (processedFiles.find(file) == processedFiles.end()) {
                        processedFiles.insert(file);
                        allFiles.push_back(file);
                        discoveredNewFiles = true;
                        std::string source = readFile(file);
                        sources.push_back(source);
                    }
                }

                if (discoveredNewFiles) {
                    programs.clear();
                    for (const auto& file : allFiles) {
                        programs.push_back(parseFile(file, false));
                    }
                }

                continue;
            }
        }

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
            throw std::runtime_error("");
        }

        if (options.verbose) {
            std::cout << "Semantic analysis complete." << std::endl;
        }

        programs.clear();
        for (const auto& file : allFiles) {
            programs.push_back(parseFile(file, false));
        }
        break;

    } while (discoveredNewFiles && iteration < maxIterations);

    std::unique_ptr<ProgramAST> mergedProgram = std::make_unique<ProgramAST>();
    for (auto& prog : programs) {
        for (auto& func : prog->functions) {
            mergedProgram->functions.push_back(std::move(func));
        }
        for (auto& ns : prog->namespaces) {
            mergedProgram->namespaces.push_back(std::move(ns));
        }
        for (auto& structDef : prog->structs) {
            mergedProgram->structs.push_back(std::move(structDef));
        }
    }

    std::string moduleName = options.inputFiles.size() == 1 ? options.inputFiles[0] : "merged";
    CodeGenerator codegen(moduleName, options.debugInfo, options.verbose, options.profile);
    codegen.generate(mergedProgram.get(), !options.compileOnly);
    if (options.verbose) {
        std::cout << "Code generation complete." << std::endl;
    }

    if (options.optimize) {
        codegen.optimize();
        if (options.verbose) {
            std::cout << "Optimization complete." << std::endl;
        }
    }

    std::string exeOutputFile = options.outputFile;

    if (options.emitLLVM) {
        if (options.outputFile != "output.exe") {
            codegen.writeIRToFile(options.outputFile);
            if (options.verbose) {
                std::cout << "\nLLVM IR written to: " << options.outputFile << std::endl;
            }

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
        codegen.writeExecutable(exeOutputFile, errorStream);
        if (options.verbose) {
            std::cout << "\nCompilation and linking successful!" << std::endl;
            std::cout << "Executable: " << exeOutputFile << std::endl;
        }
    }

    return exeOutputFile;
}
