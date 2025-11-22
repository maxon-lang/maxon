#include "test_runner.h"
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
#include <filesystem>
#include <thread>

#include <llvm/Support/raw_ostream.h>

#ifdef _WIN32
#include <windows.h>
#endif

namespace {

struct TestResult {
    std::string testName;
    bool passed;
    std::string failureReason;
    std::string failureExpected;
    std::string failureActual;
    double durationSeconds;
};

void runSingleTest(const std::filesystem::path& testPath, bool verbose, TestResult& result, int threadId = 0) {
    auto testStartTime = std::chrono::high_resolution_clock::now();
    
    result.testName = testPath.stem().string();
    result.passed = true;

    std::ifstream inFile(testPath);
    if (!inFile) {
        auto testEndTime = std::chrono::high_resolution_clock::now();
        auto testDuration = std::chrono::duration_cast<std::chrono::milliseconds>(testEndTime - testStartTime);
        result.durationSeconds = testDuration.count() / 1000.0;
        result.passed = false;
        result.failureReason = "Cannot read file";
        return;
    }

    std::string sourceCode;
    std::string line;
    while (std::getline(inFile, line)) {
        if (line == "---") break;
        sourceCode += line + "\n";
    }

    std::string expectedOptIR;
    while (std::getline(inFile, line)) {
        if (line == "---") break;
        expectedOptIR += line + "\n";
    }

    std::string expectedDebugIR;
    while (std::getline(inFile, line)) {
        if (line == "---") break;
        expectedDebugIR += line + "\n";
    }

    std::string metadata;
    while (std::getline(inFile, line)) {
        metadata += line + "\n";
    }
    inFile.close();

    std::string args;
    int expectedExitCode = 0;
    std::string expectedStdout;
    std::string expectedStderr;
    std::string expectedMaxoncStderr;

    std::istringstream metaStream(metadata);
    while (std::getline(metaStream, line)) {
        if (line.rfind("Args:", 0) == 0) {
            args = line.substr(5);
            args.erase(0, args.find_first_not_of(" \t"));
        } else if (line.rfind("ExitCode:", 0) == 0) {
            expectedExitCode = std::stoi(line.substr(10));
        } else if (line.rfind("Stdout:", 0) == 0) {
            if (line.find("```") != std::string::npos) {
                while (std::getline(metaStream, line)) {
                    if (line == "```") {
                        break;
                    }
                    if (line.length() >= 3 && line.substr(line.length() - 3) == "```") {
                        expectedStdout += line.substr(0, line.length() - 3);
                        break;
                    }
                    expectedStdout += line + "\n";
                }
            }
        } else if (line.rfind("Stderr:", 0) == 0) {
            if (line.find("```") != std::string::npos) {
                while (std::getline(metaStream, line)) {
                    if (line == "```") {
                        break;
                    }
                    if (line.length() >= 3 && line.substr(line.length() - 3) == "```") {
                        expectedStderr += line.substr(0, line.length() - 3);
                        break;
                    }
                    expectedStderr += line + "\n";
                }
            }
        } else if (line.rfind("MaxoncStderr:", 0) == 0) {
            if (line.find("```") != std::string::npos) {
                while (std::getline(metaStream, line)) {
                    if (line == "```") break;
                    expectedMaxoncStderr += line + "\n";
                }
            }
        }
    }

    auto trim = [](std::string& s) {
        s.erase(0, s.find_first_not_of(" \t\n\r"));
        s.erase(s.find_last_not_of(" \t\n\r") + 1);
    };
    trim(expectedOptIR);
    trim(expectedDebugIR);
    trim(expectedMaxoncStderr);

    // Use unique temp file names for parallel execution
    std::filesystem::path tempDir = "temp";
    std::filesystem::create_directories(tempDir);
    std::string tempSource = (tempDir / ("temp_test_fragment_" + std::to_string(threadId) + ".maxon")).string();
    std::ofstream tempOut(tempSource);
    if (!tempOut) {
        auto testEndTime = std::chrono::high_resolution_clock::now();
        auto testDuration = std::chrono::duration_cast<std::chrono::milliseconds>(testEndTime - testStartTime);
        result.durationSeconds = testDuration.count() / 1000.0;
        result.passed = false;
        result.failureReason = "Cannot write temp file";
        return;
    }
    tempOut << sourceCode;
    tempOut.close();

    std::string tempOptLL = (tempDir / ("temp-test-opt-" + std::to_string(threadId) + ".ll")).string();
    std::string tempDebugLL = (tempDir / ("temp-test-debug-" + std::to_string(threadId) + ".ll")).string();
    std::string tempExe = (tempDir / ("temp-test-" + std::to_string(threadId) + ".exe")).string();

    try {
        CompilationOptions optOpts;
        optOpts.inputFiles = {tempSource};
        optOpts.outputFile = tempOptLL;
        optOpts.optimize = true;
        optOpts.emitLLVM = true;
        optOpts.verbose = false;

        std::string actualOptIR;
        std::string compileError;
        std::string actualMaxoncStderr;

        try {
            std::string llvmErrStr;
            llvm::raw_string_ostream llvmErrCapture(llvmErrStr);

            try {
                std::stringstream stderrCapture;
                std::streambuf* oldCerr = std::cerr.rdbuf(stderrCapture.rdbuf());
                try {
                    compileProgram(optOpts, &llvmErrCapture);
                    std::cerr.rdbuf(oldCerr);
                    actualMaxoncStderr = stderrCapture.str();
                } catch (...) {
                    std::cerr.rdbuf(oldCerr);
                    actualMaxoncStderr = stderrCapture.str();
                    throw;
                }
            } catch (const std::exception& e) {
                llvmErrCapture.flush();
                compileError = llvmErrStr;
                
                // Format error message like test_regenerate.cpp does
                std::string exceptionMsg = e.what();
                
                // Combine LLVM errors and exception message
                std::string combinedError = actualMaxoncStderr;  // semantic errors from stderr
                if (!compileError.empty()) {
                    // LLVM/linker errors
                    if (!combinedError.empty()) {
                        combinedError += "\n";
                    }
                    combinedError += compileError;
                }
                
                // Normalize temp file references like test_regenerate does
                size_t pos = 0;
                std::string searchPattern = "temp-test-opt-" + std::to_string(threadId) + ".exe.tmp.obj";
                while ((pos = combinedError.find(searchPattern, pos)) != std::string::npos) {
                    combinedError.replace(pos, searchPattern.length(), "test.exe.tmp.obj");
                    pos += 16;  // length of "test.exe.tmp.obj"
                }
                pos = 0;
                searchPattern = "temp-test-debug-" + std::to_string(threadId) + ".exe.tmp.obj";
                while ((pos = combinedError.find(searchPattern, pos)) != std::string::npos) {
                    combinedError.replace(pos, searchPattern.length(), "test.exe.tmp.obj");
                    pos += 16;
                }
                
                // Normalize temp_test_fragment_{threadId}.maxon to temp_fragment.maxon
                pos = 0;
                searchPattern = "temp_test_fragment_" + std::to_string(threadId) + ".maxon";
                while ((pos = combinedError.find(searchPattern, pos)) != std::string::npos) {
                    combinedError.replace(pos, searchPattern.length(), "temp_fragment.maxon");
                    pos += 19;  // length of "temp_fragment.maxon"
                }
                
                // Add exception message if it's not already in the output
                if (!exceptionMsg.empty() && combinedError.find(exceptionMsg) == std::string::npos) {
                    bool isLinkError = (combinedError.find("lld-link:") != std::string::npos);
                    if (isLinkError) {
                        // Linking error - append exception message with single newline
                        if (!combinedError.empty() && combinedError.back() != '\n') {
                            combinedError += '\n';
                        }
                        combinedError += exceptionMsg;
                    } else {
                        // Other error - add exception at the end
                        if (!combinedError.empty() && combinedError.back() != '\n') {
                            combinedError += '\n';
                        }
                        combinedError += exceptionMsg;
                    }
                }
                
                // Format with header and footer like test_regenerate.cpp
                bool isLinkingError = combinedError.find("lld-link:") != std::string::npos;
                bool hasCompilationHeader = combinedError.find("=== Compilation Failed ===") != std::string::npos;
                bool hasTerminationFooter = combinedError.find("Compilation terminated due to errors.") != std::string::npos;
                
                if (isLinkingError) {
                    // For linking errors, insert header before "LLD linking failed"
                    size_t exMsgStart = combinedError.find("LLD linking failed");
                    if (exMsgStart != std::string::npos && !hasCompilationHeader) {
                        combinedError.insert(exMsgStart, "=== Compilation Failed ===\n");
                    }
                }
                
                // Add header if missing
                if (!hasCompilationHeader && !isLinkingError) {
                    combinedError = "=== Compilation Failed ===\n" + combinedError;
                }
                
                // Add footer if missing
                if (!hasTerminationFooter) {
                    if (!combinedError.empty() && combinedError.back() != '\n') {
                        combinedError += '\n';
                    }
                    combinedError += "\nCompilation terminated due to errors.";
                }
                
                actualMaxoncStderr = combinedError;
                throw;
            }

            std::ifstream irFile(tempOptLL);
            if (irFile) {
                actualOptIR = std::string(std::istreambuf_iterator<char>(irFile),
                                          std::istreambuf_iterator<char>());
                irFile.close();
                actualOptIR = normalizeIR(actualOptIR);
                trim(actualOptIR);
            }
        } catch (...) {
            if (expectedOptIR != "N/A" && expectedMaxoncStderr.empty()) {
                result.passed = false;
                result.failureReason = "Compilation failed unexpectedly";
            }
        }

        // Verify MaxoncStderr if expected
        if (!expectedMaxoncStderr.empty()) {
            // Normalize temp file names in actualMaxoncStderr
            size_t pos = 0;
            std::string searchPattern = "temp_test_fragment_" + std::to_string(threadId) + ".maxon";
            while ((pos = actualMaxoncStderr.find(searchPattern, pos)) != std::string::npos) {
                actualMaxoncStderr.replace(pos, searchPattern.length(), "temp_fragment.maxon");
                pos += 19;  // length of "temp_fragment.maxon"
            }
            
            trim(actualMaxoncStderr);
            if (actualMaxoncStderr != expectedMaxoncStderr) {
                result.passed = false;
                result.failureReason = "Compiler stderr mismatch";
                result.failureExpected = expectedMaxoncStderr;
                result.failureActual = actualMaxoncStderr;
            }
        }

        if (result.passed && expectedOptIR != "N/A" && actualOptIR != expectedOptIR) {
            result.passed = false;
            result.failureReason = "Optimized IR mismatch";
            result.failureExpected = "(See .test file)";
            result.failureActual = "(Regenerate to see actual IR)";
        }

        if (result.passed && expectedDebugIR != "N/A") {
            CompilationOptions debugOpts;
            debugOpts.inputFiles = {tempSource};
            debugOpts.outputFile = tempDebugLL;
            debugOpts.debugInfo = true;
            debugOpts.emitLLVM = true;
            debugOpts.verbose = false;

            std::string actualDebugIR;

            try {
                std::stringstream stderrCapture;
                std::streambuf* oldCerr = std::cerr.rdbuf(stderrCapture.rdbuf());
                try {
                    compileProgram(debugOpts);
                    std::cerr.rdbuf(oldCerr);
                } catch (...) {
                    std::cerr.rdbuf(oldCerr);
                    throw;
                }

                std::ifstream irFile(tempDebugLL);
                if (irFile) {
                    actualDebugIR = std::string(std::istreambuf_iterator<char>(irFile),
                                               std::istreambuf_iterator<char>());
                    irFile.close();
                    actualDebugIR = normalizeIR(actualDebugIR);
                    trim(actualDebugIR);
                }
            } catch (...) {
                result.passed = false;
                result.failureReason = "Debug compilation failed unexpectedly";
            }

            if (result.passed && actualDebugIR != expectedDebugIR) {
                result.passed = false;
                result.failureReason = "Debug IR mismatch";
            }
        }

        if (result.passed && expectedOptIR != "N/A" && (!expectedStdout.empty() || !expectedStderr.empty() || expectedExitCode != 0)) {
            CompilationOptions exeOpts;
            exeOpts.inputFiles = {tempSource};
            exeOpts.outputFile = tempExe;
            exeOpts.optimize = true;
            exeOpts.verbose = false;

            try {
                std::stringstream stderrCapture;
                std::streambuf* oldCerr = std::cerr.rdbuf(stderrCapture.rdbuf());
                try {
                    compileProgram(exeOpts);
                    std::cerr.rdbuf(oldCerr);
                } catch (...) {
                    std::cerr.rdbuf(oldCerr);
                    throw;
                }

                int exitCode = 0;
                std::string actualStdout;
                std::string actualStderr;

#ifdef _WIN32
                std::string tempOutput = (tempDir / ("maxon_test_output_" + std::to_string(threadId) + ".tmp")).string();
                std::string tempStderrFile = (tempDir / ("maxon_test_stderr_" + std::to_string(threadId) + ".tmp")).string();

                std::string cmdLine = tempExe;
                if (!args.empty()) {
                    cmdLine += " " + args;
                }
                cmdLine += " > \"" + tempOutput + "\" 2>\"" + tempStderrFile + "\"";

                exitCode = system(cmdLine.c_str());

                std::ifstream outFile(tempOutput);
                if (outFile) {
                    actualStdout = std::string(std::istreambuf_iterator<char>(outFile),
                                              std::istreambuf_iterator<char>());
                    outFile.close();
                }

                std::ifstream stderrFile(tempStderrFile);
                if (stderrFile) {
                    actualStderr = std::string(std::istreambuf_iterator<char>(stderrFile),
                                              std::istreambuf_iterator<char>());
                    stderrFile.close();
                }

                std::filesystem::remove(tempOutput);
                std::filesystem::remove(tempStderrFile);
#endif

                if (exitCode != expectedExitCode) {
                    result.passed = false;
                    result.failureReason = "Exit code mismatch";
                    result.failureExpected = std::to_string(expectedExitCode);
                    result.failureActual = std::to_string(exitCode);
                } else if (!expectedStdout.empty() && actualStdout != expectedStdout) {
                    result.passed = false;
                    result.failureReason = "Stdout mismatch";
                    result.failureExpected = expectedStdout;
                    result.failureActual = actualStdout;
                } else if (!expectedStderr.empty() && actualStderr != expectedStderr) {
                    result.passed = false;
                    result.failureReason = "Stderr mismatch";
                    result.failureExpected = expectedStderr;
                    result.failureActual = actualStderr;
                }
            } catch (...) {
                result.passed = false;
                result.failureReason = "Execution test failed";
            }
        }

        std::filesystem::remove(tempSource);
        std::filesystem::remove(tempOptLL);
        std::filesystem::remove(tempOptLL.substr(0, tempOptLL.length() - 3) + ".exe");
        std::filesystem::remove(tempOptLL.substr(0, tempOptLL.length() - 3) + ".pdb");
        std::filesystem::remove(tempDebugLL);
        std::filesystem::remove(tempDebugLL.substr(0, tempDebugLL.length() - 3) + ".exe");
        std::filesystem::remove(tempDebugLL.substr(0, tempDebugLL.length() - 3) + ".pdb");
        std::filesystem::remove(tempExe);
        std::filesystem::remove(tempExe.substr(0, tempExe.length() - 4) + ".pdb");

    } catch (const std::exception& e) {
        result.passed = false;
        result.failureReason = std::string("Exception: ") + e.what();
    }

    auto testEndTime = std::chrono::high_resolution_clock::now();
    auto testDuration = std::chrono::duration_cast<std::chrono::milliseconds>(testEndTime - testStartTime);
    result.durationSeconds = testDuration.count() / 1000.0;
}

} // namespace

int runTestFragmentsSubset(const std::vector<std::string>& testFiles, 
                           const std::string& outputFile, 
                           bool verbose) {
    try {
        // Get a unique thread ID based on the output file name
        size_t hashVal = std::hash<std::string>{}(outputFile);
        int threadId = static_cast<int>(hashVal % 1000);

        std::vector<TestResult> results(testFiles.size());
        
        // Run tests in this subset
        for (size_t i = 0; i < testFiles.size(); ++i) {
            runSingleTest(std::filesystem::path(testFiles[i]), verbose, results[i], threadId);
        }

        // Write results to output file
        std::ofstream outFile(outputFile);
        if (!outFile) {
            std::cerr << "Error: Cannot write to " << outputFile << std::endl;
            return 1;
        }

        // Helper to escape pipes in strings
        auto escapePipes = [](const std::string& s) {
            std::string result;
            for (char c : s) {
                if (c == '|') {
                    result += "\\|";
                } else if (c == '\\') {
                    result += "\\\\";
                } else if (c == '\n') {
                    result += "\\n";
                } else if (c == '\r') {
                    result += "\\r";
                } else {
                    result += c;
                }
            }
            return result;
        };

        for (const auto& result : results) {
            outFile << (result.passed ? "PASS" : "FAIL") << "|"
                    << escapePipes(result.testName) << "|"
                    << result.durationSeconds << "|"
                    << escapePipes(result.failureReason) << "|"
                    << escapePipes(result.failureExpected) << "|"
                    << escapePipes(result.failureActual) << "\n";
        }
        outFile.flush();
        outFile.close();

        return 0;
    } catch (const std::exception& e) {
        std::cerr << "Error in subset runner: " << e.what() << std::endl;
        return 1;
    }
}

int runTestFragments(bool verbose) {
    try {
        auto startTime = std::chrono::high_resolution_clock::now();

        std::cout << "Running test fragments" << (verbose ? " (verbose mode)" : "") << " in parallel..." << std::endl;

        std::string fragmentsDir = "language-tests/fragments";
        
        if (!std::filesystem::exists(fragmentsDir)) {
            std::cerr << "Error: Directory not found: " << fragmentsDir << std::endl;
            return 1;
        }

        // Collect all test files
        std::vector<std::filesystem::path> testFiles;
        for (const auto& entry : std::filesystem::directory_iterator(fragmentsDir)) {
            if (entry.path().extension() == ".test") {
                testFiles.push_back(entry.path());
            }
        }

        int totalTests = testFiles.size();
        if (totalTests == 0) {
            std::cout << "No test files found." << std::endl;
            return 0;
        }

        // Determine number of worker processes
        // Use number of physical cores (hardware_concurrency / 2 for hyperthreaded CPUs)
        unsigned int numWorkers = std::thread::hardware_concurrency();
        if (numWorkers == 0) numWorkers = 4; // fallback
        if (numWorkers > 8) numWorkers = numWorkers / 2; // use physical cores only
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
        std::filesystem::path tempDir = "temp";
        std::filesystem::create_directories(tempDir);
        std::vector<std::string> outputFiles(numWorkers);
        for (unsigned int i = 0; i < numWorkers; ++i) {
            outputFiles[i] = (tempDir / ("maxon_test_results_" + std::to_string(i) + ".tmp")).string();
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
            // Format: maxon.exe test-fragments-subset <outputFile> <testFile1> <testFile2> ...
            std::string cmdLine = std::string("\"") + exePath + "\" test-fragments-subset \"" + outputFiles[i] + "\"";
            if (verbose) {
                cmdLine += " --verbose";
            }
            for (const auto& testFile : testGroups[i]) {
                cmdLine += " \"" + testFile + "\"";
            }

            STARTUPINFOA si = {};
            si.cb = sizeof(si);
            // Don't inherit handles to avoid stderr/stdout contention between workers
            // si.dwFlags = STARTF_USESTDHANDLES;
            // si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
            // si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
            // si.hStdError = GetStdHandle(STD_ERROR_HANDLE);

            // CreateProcess requires non-const command line
            std::vector<char> cmdLineBuffer(cmdLine.begin(), cmdLine.end());
            cmdLineBuffer.push_back('\0');

            // Don't inherit handles (FALSE) to prevent handle contention
            if (!CreateProcessA(NULL, cmdLineBuffer.data(), NULL, NULL, FALSE, 0, NULL, NULL, &si, &processes[i])) {
                std::cerr << "Error: Failed to create worker process " << i << std::endl;
                return 1;
            }
            processHandles[i] = processes[i].hProcess;
        }

        // Wait for all processes to complete
        for (unsigned int i = 0; i < numWorkers; ++i) {
            if (testGroups[i].empty()) continue;
            DWORD exitCode = 0;
            WaitForSingleObject(processHandles[i], INFINITE);
            GetExitCodeProcess(processHandles[i], &exitCode);
            if (exitCode != 0 && verbose) {
                std::cerr << "Warning: Worker " << i << " exited with code " << exitCode << std::endl;
            }
            CloseHandle(processes[i].hProcess);
            CloseHandle(processes[i].hThread);
        }
#endif

        // Collect results from output files
        int passedTests = 0;
        int failedTests = 0;
        std::vector<TestResult> allResults;

        // Helper to unescape pipes in strings
        auto unescapePipes = [](const std::string& s) {
            std::string result;
            for (size_t i = 0; i < s.length(); ++i) {
                if (s[i] == '\\' && i + 1 < s.length()) {
                    if (s[i + 1] == '|') {
                        result += '|';
                        ++i;
                    } else if (s[i + 1] == '\\') {
                        result += '\\';
                        ++i;
                    } else if (s[i + 1] == 'n') {
                        result += '\n';
                        ++i;
                    } else if (s[i + 1] == 'r') {
                        result += '\r';
                        ++i;
                    } else {
                        result += s[i];
                    }
                } else {
                    result += s[i];
                }
            }
            return result;
        };

        // Helper to find next unescaped pipe
        auto findNextPipe = [](const std::string& s, size_t start) -> size_t {
            for (size_t i = start; i < s.length(); ++i) {
                if (s[i] == '|') {
                    // Check if it's escaped
                    size_t backslashes = 0;
                    size_t j = i;
                    while (j > 0 && s[j - 1] == '\\') {
                        ++backslashes;
                        --j;
                    }
                    // If even number of backslashes (or zero), pipe is not escaped
                    if (backslashes % 2 == 0) {
                        return i;
                    }
                }
            }
            return std::string::npos;
        };

        for (unsigned int i = 0; i < numWorkers; ++i) {
            std::ifstream inFile(outputFiles[i]);
            if (!inFile) {
                if (verbose && !testGroups[i].empty()) {
                    std::cerr << "Warning: Could not read output file for worker " << i << ": " << outputFiles[i] << std::endl;
                }
                continue;
            }

            std::string line;
            while (std::getline(inFile, line)) {
                TestResult result;
                
                // Parse: PASS|testName|duration|failureReason|expected|actual
                size_t pos1 = findNextPipe(line, 0);
                if (pos1 == std::string::npos) continue;
                
                std::string status = line.substr(0, pos1);
                result.passed = (status == "PASS");
                
                size_t pos2 = findNextPipe(line, pos1 + 1);
                if (pos2 == std::string::npos) continue;
                result.testName = unescapePipes(line.substr(pos1 + 1, pos2 - pos1 - 1));
                
                size_t pos3 = findNextPipe(line, pos2 + 1);
                if (pos3 == std::string::npos) continue;
                result.durationSeconds = std::stod(line.substr(pos2 + 1, pos3 - pos2 - 1));
                
                size_t pos4 = findNextPipe(line, pos3 + 1);
                if (pos4 == std::string::npos) continue;
                result.failureReason = unescapePipes(line.substr(pos3 + 1, pos4 - pos3 - 1));
                
                size_t pos5 = findNextPipe(line, pos4 + 1);
                if (pos5 == std::string::npos) continue;
                result.failureExpected = unescapePipes(line.substr(pos4 + 1, pos5 - pos4 - 1));
                
                result.failureActual = unescapePipes(line.substr(pos5 + 1));
                
                allResults.push_back(result);
                
                if (result.passed) {
                    passedTests++;
                    if (verbose) {
                        std::cout << "  PASS: " << result.testName << " (" 
                                  << std::fixed << std::setprecision(2) << result.durationSeconds << "s)" << std::endl;
                    }
                } else {
                    failedTests++;
                }
            }
            inFile.close();
            
            // Clean up temp file
            std::filesystem::remove(outputFiles[i]);
        }

        // Print failures after all tests complete
        for (const auto& result : allResults) {
            if (!result.passed) {
                std::cerr << "  FAIL: " << result.testName << ": " << result.failureReason << " (" 
                          << std::fixed << std::setprecision(2) << result.durationSeconds << "s)" << std::endl;
                if (!result.failureExpected.empty() && !result.failureActual.empty()) {
                    std::cerr << "    Expected: \"" << showWithEscapes(result.failureExpected) << "\"" << std::endl;
                    std::cerr << "    Actual:   \"" << showWithEscapes(result.failureActual) << "\"" << std::endl;
                }
            }
        }

        auto endTime = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime);
        double seconds = duration.count() / 1000.0;

        std::cout << "\n" << passedTests << " passed, " << failedTests << " failed, " 
                  << totalTests << " total (" << std::fixed << std::setprecision(2) << seconds << "s)" << std::endl;

        std::cout.flush();
        std::cerr.flush();

        return failedTests > 0 ? 1 : 0;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
}
