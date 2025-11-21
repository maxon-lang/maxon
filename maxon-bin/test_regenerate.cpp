#include "test_regenerate.h"
#include "test_utils.h"
#include "compiler.h"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <iterator>
#include <sstream>
#include <streambuf>
#include <string>
#include <vector>
#include <set>
#include <map>
#include <filesystem>
#include <ctime>
#include <regex>

#include <llvm/Support/raw_ostream.h>

#ifdef _WIN32
#include <windows.h>
#endif

// Structure to hold spec manifest data
struct SpecManifest {
    struct FragmentEntry {
        std::string specFile;
        std::string testName;
        int index;
    };
    std::map<std::string, FragmentEntry> fragments; // key: fragment filename
};

// Extract test fragments from spec files in specs/ directory
int extractSpecFragments() {
    std::string specsDir = "specs";
    std::string outputDir = "language-tests/fragments";
    std::string manifestPath = "language-tests/.spec-manifest.json";
    
    if (!std::filesystem::exists(specsDir)) {
        std::cerr << "Error: Directory not found: " << specsDir << std::endl;
        std::cerr << "Please create the specs/ directory with spec files." << std::endl;
        return 1;
    }
    
    // Create output directory if it doesn't exist
    std::filesystem::create_directories(outputDir);
    
    std::cout << "Extracting test fragments from spec files..." << std::endl;
    
    SpecManifest manifest;
    int totalFragmentsExtracted = 0;
    
    // Process each spec file
    for (const auto& entry : std::filesystem::directory_iterator(specsDir)) {
        if (entry.path().extension() != ".md") continue;
        
        std::string specPath = entry.path().string();
        std::string specBaseName = entry.path().stem().string();
        
        // Skip README
        if (specBaseName == "README" || specBaseName == "readme") {
            continue;
        }
        
        std::cout << "\nProcessing " << specBaseName << ".md..." << std::endl;
        
        std::ifstream specFile(specPath);
        if (!specFile) {
            std::cerr << "  Warning: Cannot read " << specPath << std::endl;
            continue;
        }
        
        std::string line;
        bool inTestSection = false;
        bool inCodeBlock = false;
        std::string currentTestName;
        std::string currentCode;
        std::string currentMetadata;
        std::map<std::string, int> testNameCounts; // Track test indices per name
        bool collectingMetadata = false;
        bool inMetadataBlock = false; // For multi-line Stdout/Stderr
        
        while (std::getline(specFile, line)) {
            // Check if we're entering the Tests section
            if (line == "## Tests") {
                inTestSection = true;
                continue;
            }
            
            if (!inTestSection) continue;
            
            // Check for test marker comment
            std::regex testMarkerRegex(R"(<!--\s*test:\s*([a-zA-Z0-9_.-]+)\s*-->)");
            std::smatch match;
            if (std::regex_search(line, match, testMarkerRegex)) {
                // Save previous test if exists
                if (!currentTestName.empty() && !currentCode.empty()) {
                    int index = testNameCounts[currentTestName];
                    std::string fragmentName = currentTestName + "." + std::to_string(index);
                    std::string testFileName = outputDir + "/" + fragmentName + ".test";
                    
                    std::ofstream testFile(testFileName);
                    testFile << currentCode;
                    testFile << "---\nN/A\n---\nN/A\n---\n";
                    if (!currentMetadata.empty()) {
                        testFile << currentMetadata;
                    }
                    testFile.close();
                    
                    // Add to manifest
                    SpecManifest::FragmentEntry entry;
                    entry.specFile = specBaseName + ".md";
                    entry.testName = currentTestName;
                    entry.index = index;
                    manifest.fragments[fragmentName + ".test"] = entry;
                    
                    std::cout << "  Extracted " << fragmentName << std::endl;
                    totalFragmentsExtracted++;
                    
                    currentCode.clear();
                    currentMetadata.clear();
                }
                
                currentTestName = match[1].str();
                testNameCounts[currentTestName]++;
                collectingMetadata = false;
                inMetadataBlock = false;
                continue;
            }
            
            // Check for code block fence
            if (line == "```maxon" || (line == "```" && !inMetadataBlock)) {
                if (!inCodeBlock && line == "```maxon") {
                    // Start of code block
                    inCodeBlock = true;
                    currentCode.clear();
                } else if (inCodeBlock && line == "```") {
                    // End of code block - start collecting metadata
                    inCodeBlock = false;
                    collectingMetadata = true;
                }
                continue;
            }
            
            if (inCodeBlock) {
                // Inside code block - accumulate source
                currentCode += line + "\n";
            } else if (collectingMetadata) {
                // Check for metadata lines
                if (line.rfind("ExitCode:", 0) == 0) {
                    currentMetadata += line + "\n";
                } else if (line.rfind("Args:", 0) == 0) {
                    currentMetadata += line + "\n";
                } else if (line.rfind("Stdout:", 0) == 0 || line.rfind("Stderr:", 0) == 0 || line.rfind("MaxoncStderr:", 0) == 0) {
                    currentMetadata += line + "\n";
                    // Check if this starts a multi-line block
                    if (line.find("```") != std::string::npos) {
                        inMetadataBlock = true;
                    }
                } else if (inMetadataBlock) {
                    // Inside multi-line metadata block
                    currentMetadata += line + "\n";
                    if (line.find("```") != std::string::npos && currentMetadata.find("```\n" + line) != std::string::npos) {
                        // This is the closing fence
                        inMetadataBlock = false;
                    }
                }
            }
        }
        
        // Save last test if exists
        if (!currentTestName.empty() && !currentCode.empty()) {
            int index = testNameCounts[currentTestName];
            std::string fragmentName = currentTestName + "." + std::to_string(index);
            std::string testFileName = outputDir + "/" + fragmentName + ".test";
            
            std::ofstream testFile(testFileName);
            testFile << currentCode;
            testFile << "---\nN/A\n---\nN/A\n---\n";
            if (!currentMetadata.empty()) {
                testFile << currentMetadata;
            }
            testFile.close();
            
            // Add to manifest
            SpecManifest::FragmentEntry entry;
            entry.specFile = specBaseName + ".md";
            entry.testName = currentTestName;
            entry.index = index;
            manifest.fragments[fragmentName + ".test"] = entry;
            
            std::cout << "  Extracted " << fragmentName << std::endl;
            totalFragmentsExtracted++;
        }
        
        specFile.close();
    }
    
    // Write manifest file
    std::ofstream manifestFile(manifestPath);
    if (manifestFile) {
        manifestFile << "{\n";
        manifestFile << "  \"version\": \"1.0\",\n";
        manifestFile << "  \"fragments\": {\n";
        
        bool first = true;
        for (const auto& [fragmentName, entry] : manifest.fragments) {
            if (!first) manifestFile << ",\n";
            first = false;
            
            manifestFile << "    \"" << fragmentName << "\": {\n";
            manifestFile << "      \"spec\": \"" << entry.specFile << "\",\n";
            manifestFile << "      \"test\": \"" << entry.testName << "\",\n";
            manifestFile << "      \"index\": " << entry.index << "\n";
            manifestFile << "    }";
        }
        
        manifestFile << "\n  }\n";
        manifestFile << "}\n";
        manifestFile.close();
        
        std::cout << "\nWrote manifest: " << manifestPath << std::endl;
    }
    
    std::cout << "\nExtraction complete: " << totalFragmentsExtracted << " fragments extracted" << std::endl;
    
    return 0;
}


// Extract code fragments from markdown files in docs/Content and create .test files
static void extractFragmentsFromDocs() {
    std::string docsDir = "docs/Content";
    std::string outputDir = "language-tests/doc-fragments";
    
    if (!std::filesystem::exists(docsDir)) {
        std::cerr << "Warning: Directory not found: " << docsDir << std::endl;
        return;
    }
    
    // Create output directory if it doesn't exist
    std::filesystem::create_directories(outputDir);
    
    std::cout << "\nExtracting fragments from " << docsDir << "..." << std::endl;
    
    for (const auto& entry : std::filesystem::directory_iterator(docsDir)) {
        if (entry.path().extension() != ".md") continue;
        
        std::string mdPath = entry.path().string();
        std::string baseName = entry.path().stem().string();
        
        std::ifstream mdFile(mdPath);
        if (!mdFile) {
            std::cerr << "  Warning: Cannot read " << mdPath << std::endl;
            continue;
        }
        
        std::string line;
        int fragmentIndex = 1;
        bool inCodeBlock = false;
        std::string currentFragment;
        std::string currentMetadata;
        
        while (std::getline(mdFile, line)) {
            if (line == "~~~") {
                if (inCodeBlock) {
                    // End of code block - write fragment to file
                    std::string testFileName = outputDir + "/" + baseName + "." + std::to_string(fragmentIndex) + ".test";
                    std::ofstream testFile(testFileName);
                    testFile << currentFragment;
                    testFile << "---\nN/A\n---\nN/A\n---\n";
                    if (!currentMetadata.empty()) {
                        testFile << currentMetadata;
                    }
                    testFile.close();
                    
                    std::cout << "  Extracted " << baseName << "." << fragmentIndex << std::endl;
                    fragmentIndex++;
                    currentFragment.clear();
                    currentMetadata.clear();
                } else {
                    // Start of code block
                    currentFragment.clear();
                    currentMetadata.clear();
                }
                inCodeBlock = !inCodeBlock;
            } else if (inCodeBlock) {
                // Check for metadata lines (ExitCode:, Args:, etc.)
                if (line.rfind("ExitCode:", 0) == 0 || 
                    line.rfind("Args:", 0) == 0 ||
                    line.rfind("Stdout:", 0) == 0 ||
                    line.rfind("Stderr:", 0) == 0) {
                    currentMetadata += line + "\n";
                } else {
                    currentFragment += line + "\n";
                }
            }
        }
        
        mdFile.close();
    }
}

int regenerateFragments() {
    try {
        std::cout << "Regenerating test fragments..." << std::endl;
        
        // First, extract fragments from docs
        extractFragmentsFromDocs();

        std::vector<std::string> fragmentDirs = {
            "language-tests/fragments",
            "language-tests/doc-fragments"
        };

        int totalTests = 0;
        int successCount = 0;
        int failCount = 0;

        for (const auto& fragmentsDir : fragmentDirs) {
            if (!std::filesystem::exists(fragmentsDir)) {
                std::cerr << "Warning: Directory not found: " << fragmentsDir << std::endl;
                continue;
            }

            std::cout << "\nProcessing " << fragmentsDir << "..." << std::endl;

            for (const auto& entry : std::filesystem::directory_iterator(fragmentsDir)) {
                if (entry.path().extension() == ".test") {
                    totalTests++;
                    std::string testPath = entry.path().string();
                    std::string testName = entry.path().stem().string();

                    std::cout << "  " << testName << "..." << std::flush;

                std::ifstream inFile(testPath);
                if (!inFile) {
                    std::cerr << " ERROR: Cannot read file" << std::endl;
                    failCount++;
                    continue;
                }

                std::string sourceCode;
                std::string line;
                while (std::getline(inFile, line)) {
                    if (line == "---") break;
                    sourceCode += line + "\n";
                }

                std::string metadata;
                bool inMetadata = false;
                int separatorCount = 1;
                while (std::getline(inFile, line)) {
                    if (line == "---") {
                        separatorCount++;
                        if (separatorCount == 3) {
                            inMetadata = true;
                            continue;
                        }
                    }
                    if (inMetadata) {
                        metadata += line + "\n";
                    }
                }
                inFile.close();

                std::string args;
                std::istringstream metaStream(metadata);
                while (std::getline(metaStream, line)) {
                    if (line.rfind("Args:", 0) == 0) {
                        args = line.substr(5);
                        args.erase(0, args.find_first_not_of(" \t"));
                        break;
                    }
                }

                std::string tempSource = "temp_fragment.maxon";
                std::ofstream tempOut(tempSource);
                if (!tempOut) {
                    std::cerr << " ERROR: Cannot write temp file" << std::endl;
                    failCount++;
                    continue;
                }
                tempOut << sourceCode;
                tempOut.close();

                try {
                    CompilationOptions optOpts;
                    optOpts.inputFiles = {tempSource};
                    optOpts.outputFile = "temp-opt.ll";
                    optOpts.optimize = true;
                    optOpts.emitLLVM = true;
                    optOpts.verbose = false;

                    std::string optIR;
                    std::string compileError;
                    std::string compileStdout;
                    std::string llvmErrStr;

                    try {
                        std::stringstream stdoutCapture;
                        std::stringstream stderrCapture;
                        llvm::raw_string_ostream llvmErrCapture(llvmErrStr);
                        std::streambuf* oldCout = std::cout.rdbuf(stdoutCapture.rdbuf());
                        std::streambuf* oldCerr = std::cerr.rdbuf(stderrCapture.rdbuf());

                        try {
                            compileProgram(optOpts, &llvmErrCapture);
                        } catch (const std::exception& e) {
                            std::cout.rdbuf(oldCout);
                            std::cerr.rdbuf(oldCerr);
                            llvmErrCapture.flush();

                            compileStdout = stdoutCapture.str();
                            std::string stderrOutput = stderrCapture.str();

                            compileError = llvmErrStr;
                            if (!compileError.empty() && compileError.back() != '\n') {
                                compileError += '\n';
                            }
                            if (!stderrOutput.empty()) {
                                compileError += stderrOutput;
                                if (compileError.back() != '\n') {
                                    compileError += '\n';
                                }
                            }

                            std::string exMsg = e.what();
                            if (!exMsg.empty()) {
                                compileError += exMsg;
                                if (compileError.back() != '\n') {
                                    compileError += '\n';
                                }
                            }
                            throw;
                        }

                        std::cout.rdbuf(oldCout);
                        std::cerr.rdbuf(oldCerr);
                        llvmErrCapture.flush();
                        compileStdout = stdoutCapture.str();

                        std::ifstream irFile("temp-opt.ll");
                        if (irFile) {
                            optIR = std::string(std::istreambuf_iterator<char>(irFile), 
                                               std::istreambuf_iterator<char>());
                            irFile.close();
                            optIR = normalizeIR(optIR);
                        }
                    } catch (...) {
                        std::ofstream outFile(testPath);
                        outFile << sourceCode << "---\nN/A\n---\nN/A\n---\n";
                        if (!args.empty()) {
                            outFile << "Args: " << args << "\n";
                        }
                        if (!compileError.empty()) {
                            size_t pos = 0;
                            while ((pos = compileError.find("temp-opt.exe.tmp.obj", pos)) != std::string::npos) {
                                compileError.replace(pos, 20, "test.exe.tmp.obj");
                                pos += 16;
                            }
                            pos = 0;
                            while ((pos = compileError.find("temp-debug.exe.tmp.obj", pos)) != std::string::npos) {
                                compileError.replace(pos, 22, "test.exe.tmp.obj");
                                pos += 16;
                            }

                            bool isLinkingError = compileError.find("lld-link:") != std::string::npos;

                            if (isLinkingError) {
                                size_t exMsgStart = compileError.find("\nLLD linking failed");
                                if (exMsgStart == std::string::npos) {
                                    exMsgStart = compileError.find("LLD linking failed");
                                }
                                if (exMsgStart != std::string::npos) {
                                    if (compileError[exMsgStart] == '\n') {
                                        exMsgStart++;
                                    }
                                    compileError.insert(exMsgStart, "=== Compilation Failed ===\n");
                                    compileError += "\nCompilation terminated due to errors.\n";
                                }
                            } else if (compileError.find("=== Compilation Failed ===") == std::string::npos) {
                                compileError = "=== Compilation Failed ===\n" + compileError;
                                if (compileError.back() != '\n') {
                                    compileError += '\n';
                                }
                                compileError += "\nCompilation terminated due to errors.\n";
                            }
                            outFile << "MaxoncStderr: ```\n" << compileError << "```\n";
                        }
                        outFile.close();
                        std::cout << " COMPILE ERROR" << std::endl;
                        successCount++;
                        std::filesystem::remove(tempSource);
                        std::filesystem::remove("temp-opt.ll");
                        continue;
                    }

                    CompilationOptions optProfileOpts;
                    optProfileOpts.inputFiles = {tempSource};
                    optProfileOpts.outputFile = "temp-opt-profiled.exe";
                    optProfileOpts.optimize = true;
                    optProfileOpts.profile = true;
                    optProfileOpts.verbose = false;
                    compileProgram(optProfileOpts);

                    int exitCode = 0;
                    std::string stdout_output;
                    std::string stderr_output;
                    int64_t optInstrCount = -1;

#ifdef _WIN32
                    char tempPath[MAX_PATH];
                    GetTempPathA(MAX_PATH, tempPath);
                    std::string tempOutput = std::string(tempPath) + "maxon_output.tmp";
                    std::string tempStderr = std::string(tempPath) + "maxon_stderr.tmp";

                    std::string cmdLine = "temp-opt-profiled.exe";
                    if (!args.empty()) {
                        cmdLine += " " + args;
                    }
                    cmdLine += " > \"" + tempOutput + "\" 2>\"" + tempStderr + "\"";

                    exitCode = system(cmdLine.c_str());

                    std::ifstream outFile(tempOutput, std::ios::binary);
                    if (outFile) {
                        std::vector<char> buffer(std::istreambuf_iterator<char>(outFile), {});
                        outFile.close();

                        const char* marker = "MAXON_PROFILE:";
                        size_t markerLen = 14;
                        auto it = std::search(buffer.begin(), buffer.end(), marker, marker + markerLen);

                        if (it != buffer.end() && std::distance(it, buffer.end()) >= static_cast<ptrdiff_t>(markerLen + 8)) {
                            std::memcpy(&optInstrCount, &*(it + markerLen), 8);
                            stdout_output = std::string(buffer.begin(), it);
                        } else {
                            stdout_output = std::string(buffer.begin(), buffer.end());
                        }
                    }

                    std::ifstream stderrFile(tempStderr);
                    if (stderrFile) {
                        stderr_output = std::string(std::istreambuf_iterator<char>(stderrFile),
                                                   std::istreambuf_iterator<char>());
                        stderrFile.close();
                    }

                    std::filesystem::remove(tempOutput);
                    std::filesystem::remove(tempStderr);
#endif

                    CompilationOptions debugOpts;
                    debugOpts.inputFiles = {tempSource};
                    debugOpts.outputFile = "temp-debug.ll";
                    debugOpts.debugInfo = true;
                    debugOpts.emitLLVM = true;
                    debugOpts.verbose = false;

                    std::string debugIR;
                    std::ifstream debugIRFile;
                    compileProgram(debugOpts);
                    debugIRFile.open("temp-debug.ll");
                    if (debugIRFile) {
                        debugIR = std::string(std::istreambuf_iterator<char>(debugIRFile),
                                             std::istreambuf_iterator<char>());
                        debugIRFile.close();
                        debugIR = normalizeIR(debugIR);
                    }

                    CompilationOptions debugProfileOpts;
                    debugProfileOpts.inputFiles = {tempSource};
                    debugProfileOpts.outputFile = "temp-debug-profiled.exe";
                    debugProfileOpts.debugInfo = true;
                    debugProfileOpts.profile = true;
                    debugProfileOpts.verbose = false;
                    compileProgram(debugProfileOpts);

                    int64_t debugInstrCount = -1;
#ifdef _WIN32
                    std::string debugCmdLine = "temp-debug-profiled.exe";
                    if (!args.empty()) {
                        debugCmdLine += " " + args;
                    }
                    debugCmdLine += " > \"" + tempOutput + "\" 2>&1";

                    system(debugCmdLine.c_str());

                    std::ifstream debugOutFile(tempOutput, std::ios::binary);
                    if (debugOutFile) {
                        std::vector<char> dbuffer(std::istreambuf_iterator<char>(debugOutFile), {});
                        debugOutFile.close();

                        const char* marker = "MAXON_PROFILE:";
                        size_t markerLen = 14;
                        auto it = std::search(dbuffer.begin(), dbuffer.end(), marker, marker + markerLen);

                        if (it != dbuffer.end() && std::distance(it, dbuffer.end()) >= static_cast<ptrdiff_t>(markerLen + 8)) {
                            std::memcpy(&debugInstrCount, &*(it + markerLen), 8);
                        }
                    }
                    std::filesystem::remove(tempOutput);
#endif

                    std::ofstream testFile(testPath);
                    testFile << sourceCode;
                    testFile << "---\n" << optIR;
                    testFile << "---\n" << debugIR;
                    testFile << "---\n";
                    if (!args.empty()) {
                        testFile << "Args: " << args << "\n";
                    }
                    testFile << "ExitCode: " << exitCode << "\n";
                    if (optInstrCount > 0) {
                        testFile << "OptimizedInstructionCount: " << optInstrCount << "\n";
                    }
                    if (debugInstrCount > 0) {
                        testFile << "UnoptimizedInstructionCount: " << debugInstrCount << "\n";
                    }
                    if (!stdout_output.empty()) {
                        testFile << "Stdout: ```\n" << stdout_output;
                        testFile << "```\n";
                    }
                    if (!stderr_output.empty()) {
                        testFile << "Stderr: ```\n" << stderr_output;
                        testFile << "```\n";
                    }
                    testFile.close();

                    std::filesystem::remove(tempSource);
                    std::filesystem::remove("temp-opt.ll");
                    std::filesystem::remove("temp-opt.exe");
                    std::filesystem::remove("temp-opt-profiled.ll");
                    std::filesystem::remove("temp-opt-profiled.exe");
                    std::filesystem::remove("temp-debug.ll");
                    std::filesystem::remove("temp-debug.exe");
                    std::filesystem::remove("temp-debug-profiled.ll");
                    std::filesystem::remove("temp-debug-profiled.exe");
                    std::filesystem::remove("temp-opt-profiled.pdb");
                    std::filesystem::remove("temp-debug-profiled.pdb");
                    std::filesystem::remove("temp-debug.pdb");

                    std::cout << " OK" << std::endl;
                    successCount++;

                } catch (const std::exception& e) {
                    std::cerr << " ERROR: " << e.what() << std::endl;
                    // Clean up any temp files that may have been created
                    std::filesystem::remove(tempSource);
                    std::filesystem::remove("temp-opt.ll");
                    std::filesystem::remove("temp-opt.exe");
                    std::filesystem::remove("temp-opt-profiled.ll");
                    std::filesystem::remove("temp-opt-profiled.exe");
                    std::filesystem::remove("temp-opt-profiled.pdb");
                    std::filesystem::remove("temp-debug.ll");
                    std::filesystem::remove("temp-debug.exe");
                    std::filesystem::remove("temp-debug-profiled.ll");
                    std::filesystem::remove("temp-debug-profiled.exe");
                    std::filesystem::remove("temp-debug-profiled.pdb");
                    std::filesystem::remove("temp-debug.pdb");
                    failCount++;
                }
                }
            }
        }

        std::cout << "\nSummary: " << successCount << " succeeded, " << failCount << " failed, "
                  << totalTests << " total" << std::endl;

        return failCount > 0 ? 1 : 0;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
}
