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
#include <filesystem>
#include <ctime>

#include <llvm/Support/raw_ostream.h>

#ifdef _WIN32
#include <windows.h>
#endif

int regenerateFragments() {
    try {
        std::cout << "Regenerating test fragments..." << std::endl;

        std::string fragmentsDir = "language-tests/fragments";
        if (!std::filesystem::exists(fragmentsDir)) {
            std::cerr << "Error: Directory not found: " << fragmentsDir << std::endl;
            return 1;
        }

        int totalTests = 0;
        int successCount = 0;
        int failCount = 0;

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

                    std::cout << " OK" << std::endl;
                    successCount++;

                } catch (const std::exception& e) {
                    std::cerr << " ERROR: " << e.what() << std::endl;
                    failCount++;
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
