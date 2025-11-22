#include "compiler.h"

#include "codegen.h"
#include "error_formatter.h"
#include "lexer.h"
#include "semantic_analyzer.h"

#include <iostream>
#include <set>
#include <vector>

std::unique_ptr<ProgramAST> parseFile(const std::string& filePath, bool verbose) {
    try {
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
    } catch (const std::runtime_error& e) {
        // Re-throw with file context if not already in message
        std::string errorMsg = e.what();
        if (errorMsg.find("File:") == std::string::npos) {
            throw std::runtime_error("In file '" + filePath + "':\n" + errorMsg);
        }
        throw;
    }
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

    // Always run dead code elimination to remove unused internal functions
    codegen.runDeadCodeElimination();
    if (options.verbose) {
        std::cout << "Dead code elimination complete." << std::endl;
    }

    if (options.optimize) {
        codegen.optimize();
        if (options.verbose) {
            std::cout << "Optimization complete." << std::endl;
        }
    }

    // Generate output filename: use outputFile if set, otherwise derive from first input file
    std::string baseFilename;
    if (!options.outputFile.empty()) {
        // Use provided outputFile and derive base from it
        size_t lastDot = options.outputFile.find_last_of('.');
        if (lastDot != std::string::npos) {
            baseFilename = options.outputFile.substr(0, lastDot);
        } else {
            baseFilename = options.outputFile;
        }
    } else {
        // Derive from first input file, preserving the directory path
        size_t lastDot = options.inputFiles[0].find_last_of('.');
        if (lastDot != std::string::npos) {
            baseFilename = options.inputFiles[0].substr(0, lastDot);
        } else {
            baseFilename = options.inputFiles[0];
        }
    }

    std::string exeOutputFile = baseFilename + ".exe";

    if (options.emitLLVM) {
        std::string llOutputFile = baseFilename + ".ll";
        codegen.writeIRToFile(llOutputFile);
        if (options.verbose) {
            std::cout << "\nLLVM IR written to: " << llOutputFile << std::endl;
        }
    }

    if (options.compileOnly) {
        std::string objOutputFile = baseFilename + ".obj";
        codegen.writeObjectFile(objOutputFile);
        if (options.verbose) {
            std::cout << "\nCompilation successful!" << std::endl;
            std::cout << "Output: " << objOutputFile << std::endl;
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
