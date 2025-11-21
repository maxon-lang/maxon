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
#include <thread>
#include <mutex>

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

// Helper function to regenerate a single fragment
// Returns: 0 = success, 1 = compile error (expected), 2 = unexpected error
static int regenerateSingleFragment(const std::string& testPath, const std::string& testName, 
                                   std::string& statusMsg, int workerId = 0) {
    std::ifstream inFile(testPath);
    if (!inFile) {
        statusMsg = "ERROR: Cannot read file";
        return 2;
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

    // Use worker-specific temp files to avoid conflicts
    std::string workerSuffix = workerId > 0 ? ("_w" + std::to_string(workerId)) : "";
    std::string tempSource = "temp_fragment" + workerSuffix + ".maxon";
    std::string tempOptLL = "temp-opt" + workerSuffix + ".ll";
    std::string tempOptExe = "temp-opt" + workerSuffix + ".exe";
    std::string tempDebugLL = "temp-debug" + workerSuffix + ".ll";
    std::string tempDebugExe = "temp-debug" + workerSuffix + ".exe";
    std::string tempOptPdb = "temp-opt" + workerSuffix + ".pdb";
    std::string tempDebugPdb = "temp-debug" + workerSuffix + ".pdb";

    std::ofstream tempOut(tempSource);
    if (!tempOut) {
        statusMsg = "ERROR: Cannot write temp file";
        return 2;
    }
    tempOut << sourceCode;
    tempOut.close();

    try {
        CompilationOptions optOpts;
        optOpts.inputFiles = {tempSource};
        optOpts.outputFile = tempOptLL;
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

            std::ifstream irFile(tempOptLL);
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
                // Normalize worker-specific temp filenames to stable names
                size_t pos = 0;
                std::regex tempFragmentRegex(R"(temp_fragment_w\d+\.maxon)");
                compileError = std::regex_replace(compileError, tempFragmentRegex, "temp_fragment.maxon");
                
                std::regex tempOptRegex(R"(temp-opt_w\d+\.exe\.tmp\.obj)");
                compileError = std::regex_replace(compileError, tempOptRegex, "test.exe.tmp.obj");
                
                std::regex tempDebugRegex(R"(temp-debug_w\d+\.exe\.tmp\.obj)");
                compileError = std::regex_replace(compileError, tempDebugRegex, "test.exe.tmp.obj");
                
                // Also handle non-worker versions
                pos = 0;
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
            statusMsg = "COMPILE ERROR";
            
            std::filesystem::remove(tempSource);
            std::filesystem::remove(tempOptLL);
            return 1;
        }

        CompilationOptions optProfileOpts;
        optProfileOpts.inputFiles = {tempSource};
        optProfileOpts.outputFile = tempOptExe;
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
        std::string tempOutput = std::string(tempPath) + "maxon_output" + workerSuffix + ".tmp";
        std::string tempStderr = std::string(tempPath) + "maxon_stderr" + workerSuffix + ".tmp";

        std::string cmdLine = tempOptExe;
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
        debugOpts.outputFile = tempDebugLL;
        debugOpts.debugInfo = true;
        debugOpts.emitLLVM = true;
        debugOpts.verbose = false;

        std::string debugIR;
        std::ifstream debugIRFile;
        compileProgram(debugOpts);
        debugIRFile.open(tempDebugLL);
        if (debugIRFile) {
            debugIR = std::string(std::istreambuf_iterator<char>(debugIRFile),
                                 std::istreambuf_iterator<char>());
            debugIRFile.close();
            debugIR = normalizeIR(debugIR);
        }

        CompilationOptions debugProfileOpts;
        debugProfileOpts.inputFiles = {tempSource};
        debugProfileOpts.outputFile = tempDebugExe;
        debugProfileOpts.debugInfo = true;
        debugProfileOpts.profile = true;
        debugProfileOpts.verbose = false;
        compileProgram(debugProfileOpts);

        int64_t debugInstrCount = -1;
#ifdef _WIN32
        std::string debugCmdLine = tempDebugExe;
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
        std::filesystem::remove(tempOptLL);
        std::filesystem::remove(tempOptExe);
        std::filesystem::remove(tempDebugLL);
        std::filesystem::remove(tempDebugExe);
        std::filesystem::remove(tempOptPdb);
        std::filesystem::remove(tempDebugPdb);

        statusMsg = "OK";
        return 0;

    } catch (const std::exception& e) {
        statusMsg = std::string("ERROR: ") + e.what();
        // Clean up any temp files that may have been created
        std::filesystem::remove(tempSource);
        std::filesystem::remove(tempOptLL);
        std::filesystem::remove(tempOptExe);
        std::filesystem::remove(tempOptPdb);
        std::filesystem::remove(tempDebugLL);
        std::filesystem::remove(tempDebugExe);
        std::filesystem::remove(tempDebugPdb);
        return 2;
    }
}

// Regenerate a subset of fragments (used for parallel processing)
int regenerateFragmentsSubset(const std::string& outputFile, const std::vector<std::string>& testFiles) {
    std::ofstream out(outputFile);
    if (!out) {
        std::cerr << "Error: Cannot write to output file: " << outputFile << std::endl;
        return 1;
    }

    // Use process ID to generate unique worker ID
    int workerId = GetCurrentProcessId() % 100000;
    int failCount = 0;

    for (const auto& testPath : testFiles) {
        std::filesystem::path p(testPath);
        std::string testName = p.stem().string();
        
        std::string statusMsg;
        int result = regenerateSingleFragment(testPath, testName, statusMsg, workerId);
        
        if (result != 0 && result != 1) {
            failCount++;
        }
        
        // Write result to output file
        out << testName << "|" << statusMsg << "\n";
        out.flush();
    }

    out.close();
    return failCount > 0 ? 1 : 0;
}

int regenerateFragments() {
    try {
        auto startTime = std::chrono::high_resolution_clock::now();
        
        std::cout << "Regenerating test fragments..." << std::endl;
        
        // First, extract fragments from docs
        extractFragmentsFromDocs();

        std::vector<std::string> fragmentDirs = {
            "language-tests/fragments",
            "language-tests/doc-fragments"
        };

        // Collect all test files
        std::vector<std::filesystem::path> testFiles;
        for (const auto& fragmentsDir : fragmentDirs) {
            if (!std::filesystem::exists(fragmentsDir)) {
                std::cerr << "Warning: Directory not found: " << fragmentsDir << std::endl;
                continue;
            }

            std::cout << "\nProcessing " << fragmentsDir << "..." << std::endl;

            for (const auto& entry : std::filesystem::directory_iterator(fragmentsDir)) {
                if (entry.path().extension() == ".test") {
                    testFiles.push_back(entry.path());
                }
            }
        }

        int totalTests = testFiles.size();
        if (totalTests == 0) {
            std::cout << "No test files found." << std::endl;
            return 0;
        }

        // Determine number of worker processes
        unsigned int numWorkers = std::thread::hardware_concurrency();
        if (numWorkers == 0) numWorkers = 4;
        if (numWorkers > 8) numWorkers = numWorkers / 2;
        if (numWorkers > static_cast<unsigned int>(totalTests)) {
            numWorkers = totalTests;
        }

        std::cout << "Using " << numWorkers << " parallel workers..." << std::endl;

        // Split tests into groups
        std::vector<std::vector<std::string>> testGroups(numWorkers);
        for (size_t i = 0; i < testFiles.size(); ++i) {
            testGroups[i % numWorkers].push_back(testFiles[i].string());
        }

        // Create output files for each worker
        std::vector<std::string> outputFiles(numWorkers);
        for (unsigned int i = 0; i < numWorkers; ++i) {
            char tempPath[MAX_PATH];
            GetTempPathA(MAX_PATH, tempPath);
            outputFiles[i] = std::string(tempPath) + "maxon_regen_results_" + std::to_string(i) + ".tmp";
        }

#ifdef _WIN32
        // Launch child processes
        std::vector<PROCESS_INFORMATION> processes(numWorkers);
        std::vector<HANDLE> processHandles(numWorkers);

        // Get current executable path
        char exePath[MAX_PATH];
        GetModuleFileNameA(NULL, exePath, MAX_PATH);

        for (unsigned int i = 0; i < numWorkers; ++i) {
            if (testGroups[i].empty()) continue;

            // Build command line for child process
            std::string cmdLine = std::string("\"") + exePath + "\" regen-fragments-subset \"" + outputFiles[i] + "\"";
            for (const auto& testFile : testGroups[i]) {
                cmdLine += " \"" + testFile + "\"";
            }

            STARTUPINFOA si = {};
            si.cb = sizeof(si);
            si.dwFlags = STARTF_USESTDHANDLES;
            si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
            si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
            si.hStdError = GetStdHandle(STD_ERROR_HANDLE);

            // CreateProcess requires non-const command line
            std::vector<char> cmdLineBuffer(cmdLine.begin(), cmdLine.end());
            cmdLineBuffer.push_back('\0');

            if (!CreateProcessA(NULL, cmdLineBuffer.data(), NULL, NULL, TRUE, 0, NULL, NULL, &si, &processes[i])) {
                std::cerr << "Error: Failed to create worker process " << i << std::endl;
                return 1;
            }
            processHandles[i] = processes[i].hProcess;
        }

        // Wait for all processes to complete
        for (unsigned int i = 0; i < numWorkers; ++i) {
            if (testGroups[i].empty()) continue;
            WaitForSingleObject(processHandles[i], INFINITE);
            CloseHandle(processes[i].hProcess);
            CloseHandle(processes[i].hThread);
        }
#endif

        // Collect results from output files
        int successCount = 0;
        int failCount = 0;

        for (unsigned int i = 0; i < numWorkers; ++i) {
            std::ifstream inFile(outputFiles[i]);
            if (!inFile) continue;

            std::string line;
            while (std::getline(inFile, line)) {
                size_t pos = line.find('|');
                if (pos == std::string::npos) continue;
                
                std::string testName = line.substr(0, pos);
                std::string status = line.substr(pos + 1);
                
                std::cout << "  " << testName << "... " << status << std::endl;
                
                if (status == "OK" || status == "COMPILE ERROR") {
                    successCount++;
                } else {
                    failCount++;
                }
            }
            inFile.close();
            std::filesystem::remove(outputFiles[i]);
        }

        auto endTime = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime);
        double seconds = duration.count() / 1000.0;

        std::cout << "\nSummary: " << successCount << " succeeded, " << failCount << " failed, "
                  << totalTests << " total" << std::endl;
        std::cout << "Elapsed time: " << std::fixed << std::setprecision(2) << seconds << "s" << std::endl;

        return failCount > 0 ? 1 : 0;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
}
