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

void runSingleTest(const std::filesystem::path& testPath, bool verbose, TestResult& result) {
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

    // Use simple fixed temp file names (sequential execution)
    std::string tempSource = "temp_test_fragment.maxon";
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

    std::string tempOptLL = "temp-test-opt.ll";
    std::string tempDebugLL = "temp-test-debug.ll";
    std::string tempExe = "temp-test.exe";

    try {
        CompilationOptions optOpts;
        optOpts.inputFiles = {tempSource};
        optOpts.outputFile = tempOptLL;
        optOpts.optimize = true;
        optOpts.emitLLVM = true;
        optOpts.verbose = false;

        std::string actualOptIR;
        std::string compileError;

        try {
            std::string llvmErrStr;
            llvm::raw_string_ostream llvmErrCapture(llvmErrStr);

            try {
                std::stringstream stderrCapture;
                std::streambuf* oldCerr = std::cerr.rdbuf(stderrCapture.rdbuf());
                try {
                    compileProgram(optOpts, &llvmErrCapture);
                    std::cerr.rdbuf(oldCerr);
                } catch (...) {
                    std::cerr.rdbuf(oldCerr);
                    throw;
                }
            } catch (...) {
                llvmErrCapture.flush();
                compileError = llvmErrStr;
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
            if (expectedOptIR != "N/A") {
                result.passed = false;
                result.failureReason = "Compilation failed unexpectedly";
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
                char tempPath[MAX_PATH];
                GetTempPathA(MAX_PATH, tempPath);
                std::string tempOutput = std::string(tempPath) + "maxon_test_output.tmp";
                std::string tempStderrFile = std::string(tempPath) + "maxon_test_stderr.tmp";

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
        std::filesystem::remove(tempDebugLL);
        std::filesystem::remove(tempDebugLL.substr(0, tempDebugLL.length() - 3) + ".exe");
        std::filesystem::remove(tempExe);

    } catch (const std::exception& e) {
        result.passed = false;
        result.failureReason = std::string("Exception: ") + e.what();
    }

    auto testEndTime = std::chrono::high_resolution_clock::now();
    auto testDuration = std::chrono::duration_cast<std::chrono::milliseconds>(testEndTime - testStartTime);
    result.durationSeconds = testDuration.count() / 1000.0;
}

} // namespace

int runTestFragments(bool verbose) {
    try {
        auto startTime = std::chrono::high_resolution_clock::now();

        std::cout << "Running test fragments" << (verbose ? " (verbose mode)" : "") << "..." << std::endl;

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
        int passedTests = 0;
        int failedTests = 0;
        std::vector<TestResult> results(totalTests);

        // Run tests sequentially
        for (size_t i = 0; i < testFiles.size(); ++i) {
            runSingleTest(testFiles[i], verbose, results[i]);

            if (results[i].passed) {
                passedTests++;
                if (verbose) {
                    std::cout << "  PASS: " << results[i].testName << " (" 
                              << std::fixed << std::setprecision(2) << results[i].durationSeconds << "s)" << std::endl;
                }
            } else {
                failedTests++;
            }
        }

        // Print failures after all tests complete
        for (const auto& result : results) {
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
