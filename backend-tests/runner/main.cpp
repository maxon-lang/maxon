// Backend Test Runner for Maxon Compiler
// Standalone test runner that compiles and runs backend unit tests
//
// For each test:
// 1. Compile with optimization (-O) and without (debug)
// 2. Run both executables and verify exit codes match expected
// 3. On failure: generate IR, objdump, and verbose compile output

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <regex>
#include <sstream>
#include <string>
#include <vector>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <signal.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

namespace fs = std::filesystem;

// ANSI color codes
#ifdef _WIN32
static bool enableAnsiColors() {
	HANDLE hOut = GetStdHandle(STD_OUTPUT_HANDLE);
	DWORD dwMode = 0;
	if (GetConsoleMode(hOut, &dwMode)) {
		SetConsoleMode(hOut, dwMode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
		return true;
	}
	return false;
}
#else
static bool enableAnsiColors() { return true; }
#endif

const char *RED = "\033[31m";
const char *GREEN = "\033[32m";
const char *YELLOW = "\033[33m";
const char *CYAN = "\033[36m";
const char *RESET = "\033[0m";
const char *BOLD = "\033[1m";

struct TestExpectation {
	int exitCode = 0;
	std::string expectedStdout;
	bool hasStdout = false;
};

struct TestResult {
	bool passed = false;
	std::string errorMessage;
	int optExitCode = -1;
	int debugExitCode = -1;
	std::string optStdout;
	std::string debugStdout;
};

// Parse expected values from test file header comments
TestExpectation parseExpectations(const fs::path &testFile) {
	TestExpectation exp;
	std::ifstream file(testFile);
	std::string line;
	bool inStdout = false;

	while (std::getline(file, line)) {
		// Stop at first non-comment line
		if (line.empty() || line[0] != '/') {
			if (!line.empty() && line.find("//") != 0) {
				break;
			}
			continue;
		}

		// Remove leading "//" and trim
		if (line.size() >= 2 && line[0] == '/' && line[1] == '/') {
			std::string content = line.substr(2);

			// Check for ExitCode
			std::regex exitCodeRe(R"(\s*ExitCode:\s*(\d+))");
			std::smatch match;
			if (std::regex_search(content, match, exitCodeRe)) {
				exp.exitCode = std::stoi(match[1].str());
				inStdout = false;
				continue;
			}

			// Check for Stdout: header
			std::regex stdoutRe(R"(\s*Stdout:\s*(.*))");
			if (std::regex_search(content, match, stdoutRe)) {
				exp.hasStdout = true;
				std::string rest = match[1].str();
				if (!rest.empty()) {
					exp.expectedStdout = rest + "\n";
				}
				inStdout = true;
				continue;
			}

			// Check for continuation line (indented with 2+ spaces after //)
			if (inStdout && content.size() >= 2 && content[0] == ' ' && content[1] == ' ') {
				exp.expectedStdout += content.substr(2) + "\n";
				continue;
			}

			// Any other comment line ends stdout continuation
			if (inStdout && !content.empty() && content[0] != ' ') {
				inStdout = false;
			}
		}
	}

	// Remove trailing newline from expectedStdout if present
	if (!exp.expectedStdout.empty() && exp.expectedStdout.back() == '\n') {
		exp.expectedStdout.pop_back();
	}

	return exp;
}

// Run a command and capture output
struct CommandResult {
	int exitCode;
	std::string output;
	std::string errOutput;
	bool timedOut = false;
};

// Timeout in seconds for running compiled tests
const int TIMEOUT_SECONDS = 10;

#ifdef _WIN32
// Windows implementation using Job Objects with timeout
CommandResult runCommandWithTimeout(const std::string &cmd, int timeoutSeconds) {
	CommandResult result;
	result.exitCode = -1;

	// Create temp file for capturing output
	fs::path tempDir = fs::temp_directory_path();
	fs::path outputFile = tempDir / "backend_test_output.txt";

	// Build command that redirects output to temp file
	std::string cmdLine = "cmd /c \"" + cmd + " > \"" + outputFile.string() + "\" 2>&1\"";

	STARTUPINFOA si = {};
	si.cb = sizeof(si);
	si.dwFlags = STARTF_USESHOWWINDOW;
	si.wShowWindow = SW_HIDE;
	PROCESS_INFORMATION pi = {};

	// Create job object to kill all child processes on timeout
	HANDLE hJob = CreateJobObjectA(NULL, NULL);
	if (hJob == NULL) {
		return result;
	}

	// Configure job to kill all processes when job handle is closed
	JOBOBJECT_EXTENDED_LIMIT_INFORMATION jeli = {};
	jeli.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
	if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, &jeli, sizeof(jeli))) {
		CloseHandle(hJob);
		return result;
	}

	// CreateProcess requires non-const command line
	std::vector<char> cmdBuffer(cmdLine.begin(), cmdLine.end());
	cmdBuffer.push_back('\0');

	// Create process suspended so we can add to job first
	if (!CreateProcessA(NULL, cmdBuffer.data(), NULL, NULL, FALSE,
						CREATE_SUSPENDED, NULL, NULL, &si, &pi)) {
		CloseHandle(hJob);
		return result;
	}

	// Add process to job
	if (!AssignProcessToJobObject(hJob, pi.hProcess)) {
		TerminateProcess(pi.hProcess, 1);
		CloseHandle(pi.hProcess);
		CloseHandle(pi.hThread);
		CloseHandle(hJob);
		return result;
	}

	// Resume the process
	ResumeThread(pi.hThread);

	// Wait for process with timeout
	DWORD waitResult = WaitForSingleObject(pi.hProcess, timeoutSeconds * 1000);

	if (waitResult == WAIT_TIMEOUT) {
		result.timedOut = true;
		result.exitCode = -1;
	} else if (waitResult == WAIT_OBJECT_0) {
		DWORD dwExitCode;
		if (GetExitCodeProcess(pi.hProcess, &dwExitCode)) {
			result.exitCode = static_cast<int>(dwExitCode);
		}
	}

	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);
	CloseHandle(hJob); // Kills all processes in job if still running

	// Give Windows time to release file locks
	if (result.timedOut) {
		Sleep(100);
	}

	// Read output from temp file
	if (fs::exists(outputFile)) {
		std::ifstream f(outputFile);
		std::stringstream ss;
		ss << f.rdbuf();
		result.output = ss.str();
		f.close();
		fs::remove(outputFile);
	}

	// Trim trailing whitespace
	while (!result.output.empty() &&
		   (result.output.back() == '\n' || result.output.back() == '\r')) {
		result.output.pop_back();
	}

	return result;
}
#else
// Linux implementation using fork/exec with timeout
CommandResult runCommandWithTimeout(const std::string &cmd, int timeoutSeconds) {
	CommandResult result;
	result.exitCode = -1;

	// Create temp file for capturing output
	fs::path tempDir = fs::temp_directory_path();
	fs::path outputFile = tempDir / "backend_test_output.txt";

	std::string fullCmd = cmd + " > \"" + outputFile.string() + "\" 2>&1";

	pid_t pid = fork();

	if (pid < 0) {
		return result;
	} else if (pid == 0) {
		// Child process
		execl("/bin/sh", "sh", "-c", fullCmd.c_str(), (char *)NULL);
		_exit(127);
	}

	// Parent process - wait with timeout
	int status;
	time_t startTime = time(NULL);

	while (true) {
		pid_t wpid = waitpid(pid, &status, WNOHANG);
		if (wpid == pid) {
			// Process finished
			if (WIFEXITED(status)) {
				result.exitCode = WEXITSTATUS(status);
			}
			break;
		} else if (wpid == 0) {
			// Still running, check timeout
			if (time(NULL) - startTime >= timeoutSeconds) {
				kill(pid, SIGKILL);
				waitpid(pid, &status, 0);
				result.timedOut = true;
				break;
			}
			usleep(10000); // 10ms
		} else {
			// Error
			break;
		}
	}

	// Read output from temp file
	if (fs::exists(outputFile)) {
		std::ifstream f(outputFile);
		std::stringstream ss;
		ss << f.rdbuf();
		result.output = ss.str();
		f.close();
		fs::remove(outputFile);
	}

	// Trim trailing whitespace
	while (!result.output.empty() &&
		   (result.output.back() == '\n' || result.output.back() == '\r')) {
		result.output.pop_back();
	}

	return result;
}
#endif

// Convenience wrapper for commands that shouldn't timeout (like compilation)
CommandResult runCommand(const std::string &cmd) {
	return runCommandWithTimeout(cmd, 60); // 60 second timeout for compilation
}

// Clean artifacts directory
void cleanTestDirectory(const fs::path &testDir) {
	fs::path artifactsDir = testDir / "artifacts";
	if (fs::exists(artifactsDir)) {
		fs::remove_all(artifactsDir);
	}
}

// Get sorted list of test files (only numbered test files: NNN-name.maxon)
std::vector<fs::path> discoverTests(const fs::path &testDir) {
	std::vector<fs::path> tests;

	for (const auto &entry : fs::directory_iterator(testDir)) {
		if (entry.is_regular_file() && entry.path().extension() == ".maxon") {
			std::string filename = entry.path().filename().string();
			// Exclude temp files (.opt.maxon, .debug.maxon)
			if (filename.find(".opt.maxon") != std::string::npos ||
				filename.find(".debug.maxon") != std::string::npos) {
				continue;
			}
			// Only include numbered test files (NNN-name.maxon)
			if (filename.size() >= 4 &&
				std::isdigit(filename[0]) &&
				std::isdigit(filename[1]) &&
				std::isdigit(filename[2]) &&
				filename[3] == '-') {
				tests.push_back(entry.path());
			}
		}
	}

	// Sort by filename (numeric prefix ensures correct order)
	std::sort(tests.begin(), tests.end(), [](const fs::path &a, const fs::path &b) {
		return a.filename().string() < b.filename().string();
	});

	return tests;
}

// ===== MIR Verifier Tests =====

struct MIRTestExpectation {
	bool expectPass = true; // true = should pass, false = should fail
};

// Parse EXPECT marker from MIR file
MIRTestExpectation parseMIRExpectation(const fs::path &mirFile) {
	MIRTestExpectation exp;
	std::ifstream file(mirFile);
	std::string line;

	while (std::getline(file, line)) {
		// Look for ; EXPECT: pass or ; EXPECT: fail
		if (line.find("; EXPECT:") != std::string::npos) {
			if (line.find("fail") != std::string::npos) {
				exp.expectPass = false;
			} else if (line.find("pass") != std::string::npos) {
				exp.expectPass = true;
			}
			break;
		}
		// Stop at first non-comment line
		if (!line.empty() && line[0] != ';') {
			break;
		}
	}

	return exp;
}

// Discover MIR verifier test files
std::vector<fs::path> discoverMIRVerifierTests(const fs::path &testDir) {
	std::vector<fs::path> tests;
	fs::path mirDir = testDir / "mir-verifier";

	if (!fs::exists(mirDir) || !fs::is_directory(mirDir)) {
		return tests;
	}

	for (const auto &entry : fs::directory_iterator(mirDir)) {
		if (entry.is_regular_file() && entry.path().extension() == ".mir") {
			tests.push_back(entry.path());
		}
	}

	// Sort by filename
	std::sort(tests.begin(), tests.end(), [](const fs::path &a, const fs::path &b) {
		return a.filename().string() < b.filename().string();
	});

	return tests;
}

// Discover MIR execution test files (in mir/ subdirectory)
std::vector<fs::path> discoverMIRExecutionTests(const fs::path &testDir) {
	std::vector<fs::path> tests;
	fs::path mirDir = testDir / "mir";

	if (!fs::exists(mirDir) || !fs::is_directory(mirDir)) {
		return tests;
	}

	for (const auto &entry : fs::directory_iterator(mirDir)) {
		if (entry.is_regular_file() && entry.path().extension() == ".mir") {
			tests.push_back(entry.path());
		}
	}

	// Sort by filename
	std::sort(tests.begin(), tests.end(), [](const fs::path &a, const fs::path &b) {
		return a.filename().string() < b.filename().string();
	});

	return tests;
}

// Parse expected exit code from MIR execution test
// Looks for: ; ExitCode: N
int parseMIRExitCode(const fs::path &mirFile) {
	std::ifstream file(mirFile);
	std::string line;
	while (std::getline(file, line)) {
		// Trim whitespace
		size_t start = line.find_first_not_of(" \t");
		if (start == std::string::npos)
			continue;
		line = line.substr(start);

		// Check for ExitCode comment
		if (line.rfind("; ExitCode:", 0) == 0) {
			std::string value = line.substr(11);
			size_t numStart = value.find_first_not_of(" \t");
			if (numStart != std::string::npos) {
				return std::stoi(value.substr(numStart));
			}
		}
	}
	return 0; // Default to 0 if not specified
}

// Run MIR execution test (compile .mir to .exe and run it)
// Returns true if test passed
bool runMIRExecutionTest(const fs::path &mirFile, bool verbose) {
	std::string testName = mirFile.stem().string();
	int expectedExitCode = parseMIRExitCode(mirFile);

	// Create temp directory for output
	fs::path tempDir = mirFile.parent_path() / "temp";
	fs::create_directories(tempDir);
	fs::path exePath = tempDir / (testName + ".exe");

	// Compile MIR to executable using compile-mir command
	std::string compileCmd = "maxon compile-mir \"" + mirFile.string() + "\" -o \"" + exePath.string() + "\"";
	auto compileResult = runCommand(compileCmd);

	bool testPassed = false;
	std::string failReason;

	if (compileResult.exitCode != 0) {
		failReason = "compilation failed: " + compileResult.output;
	} else {
		// Run the compiled executable
		auto runResult = runCommand("\"" + exePath.string() + "\"");
		if (runResult.exitCode == expectedExitCode) {
			testPassed = true;
		} else {
			failReason = "exit code " + std::to_string(runResult.exitCode) +
						 " (expected " + std::to_string(expectedExitCode) + ")";
		}
	}

	// Clean up temp files
	if (fs::exists(exePath)) {
		fs::remove(exePath);
	}

	if (verbose) {
		std::cout << CYAN << "Running: " << RESET << testName << "... ";
		if (testPassed) {
			std::cout << GREEN << "PASSED" << RESET << "\n";
		} else {
			std::cout << RED << "FAILED" << RESET << " - " << failReason << "\n";
		}
	}

	return testPassed;
}

// Run all MIR execution tests
// Returns pair<passed, failed>
std::pair<int, int> runMIRExecutionTests(const std::vector<fs::path> &mirTests, bool verbose) {
	int passed = 0;
	int failed = 0;

	if (mirTests.empty()) {
		return {0, 0};
	}

	for (const auto &mirFile : mirTests) {
		if (runMIRExecutionTest(mirFile, verbose)) {
			passed++;
			if (!verbose) {
				std::cout << GREEN << "." << RESET;
				std::cout.flush();
			}
		} else {
			failed++;
			if (!verbose) {
				std::cout << RED << "F" << RESET;
				std::cout.flush();
			}
		}
	}

	return {passed, failed};
}

// Run MIR verifier test
// Returns true if test passed
bool runMIRVerifierTest(const fs::path &mirFile, bool verbose) {
	MIRTestExpectation exp = parseMIRExpectation(mirFile);
	std::string testName = mirFile.stem().string();

	// Run maxon verify-mir
	std::string cmd = "maxon verify-mir \"" + mirFile.string() + "\"";
	auto result = runCommand(cmd);

	bool testPassed = false;
	if (exp.expectPass) {
		// Should pass verification
		testPassed = (result.exitCode == 0);
	} else {
		// Should fail verification
		testPassed = (result.exitCode != 0);
	}

	if (verbose) {
		std::cout << CYAN << "Running: " << RESET << testName << "... ";
		if (testPassed) {
			std::cout << GREEN << "PASSED" << RESET << "\n";
		} else {
			std::cout << RED << "FAILED" << RESET;
			if (exp.expectPass) {
				std::cout << " (should have been accepted)";
			} else {
				std::cout << " (should have been rejected)";
			}
			std::cout << "\n";
			if (!result.output.empty()) {
				std::cout << "  Output: " << result.output << "\n";
			}
		}
	}

	return testPassed;
}

// Run all MIR verifier tests
// Returns pair<passed, failed>
std::pair<int, int> runMIRVerifierTests(const std::vector<fs::path> &mirTests, bool verbose) {
	int passed = 0;
	int failed = 0;

	if (mirTests.empty()) {
		return {0, 0};
	}

	for (const auto &mirFile : mirTests) {
		if (runMIRVerifierTest(mirFile, verbose)) {
			passed++;
			if (!verbose) {
				std::cout << GREEN << "." << RESET;
				std::cout.flush();
			}
		} else {
			failed++;
			if (!verbose) {
				std::cout << RED << "F" << RESET;
				std::cout.flush();
			}
		}
	}

	return {passed, failed};
}

// Generate diagnostic artifacts for a failed test
// Returns list of generated artifact filenames
std::vector<std::string> generateDiagnostics(const fs::path &testFile, const fs::path &testDir,
											 const std::string &llvmBinDir) {
	std::vector<std::string> artifacts;
	std::string basename = testFile.stem().string();

	// Create artifacts directory
	fs::path artifactsDir = testDir / "artifacts";
	fs::create_directories(artifactsDir);

	// Temp source files in artifacts directory
	fs::path optSource = artifactsDir / (basename + ".opt.maxon");
	fs::path debugSource = artifactsDir / (basename + ".debug.maxon");

	// Copy source to temp files for compilation
	fs::copy_file(testFile, optSource, fs::copy_options::overwrite_existing);
	fs::copy_file(testFile, debugSource, fs::copy_options::overwrite_existing);

	std::string optExe = (artifactsDir / (basename + ".opt.exe")).string();
	std::string debugExe = (artifactsDir / (basename + ".debug.exe")).string();

	// Compile optimized with verbose and IR
	std::string cmdOpt = "maxon compile \"" + optSource.string() + "\" -O --emit-ir -vvv";
	auto resultOpt = runCommand(cmdOpt);

	// Save verbose output
	std::string errorFile = (artifactsDir / (basename + ".compile-error.txt")).string();
	{
		std::ofstream f(errorFile);
		f << "=== Optimized Compilation (-O -vvv) ===\n";
		f << "Command: " << cmdOpt << "\n";
		f << "Exit code: " << resultOpt.exitCode << "\n";
		f << "\n=== STDOUT ===\n"
		  << resultOpt.output;
		f << "\n=== STDERR ===\n"
		  << resultOpt.errOutput;
	}
	artifacts.push_back(basename + ".compile-error.txt");

	// Compile debug with verbose and IR
	std::string cmdDebug = "maxon compile \"" + debugSource.string() + "\" --emit-ir -vvv";
	auto resultDebug = runCommand(cmdDebug);

	{
		std::ofstream f(errorFile, std::ios::app);
		f << "\n\n=== Debug Compilation (-vvv) ===\n";
		f << "Command: " << cmdDebug << "\n";
		f << "Exit code: " << resultDebug.exitCode << "\n";
		f << "\n=== STDOUT ===\n"
		  << resultDebug.output;
		f << "\n=== STDERR ===\n"
		  << resultDebug.errOutput;
	}

	// Track generated IR files
	fs::path optIR = artifactsDir / (basename + ".opt.ir");
	fs::path debugIR = artifactsDir / (basename + ".debug.ir");
	if (fs::exists(optIR)) {
		artifacts.push_back(basename + ".opt.ir");
	}
	if (fs::exists(debugIR)) {
		artifacts.push_back(basename + ".debug.ir");
	}

	// Track executables
	if (fs::exists(optExe)) {
		artifacts.push_back(basename + ".opt.exe");
	}
	if (fs::exists(debugExe)) {
		artifacts.push_back(basename + ".debug.exe");
	}

	// Run llvm-objdump on executables if they exist
	std::string objdump = llvmBinDir + "/llvm-objdump";
#ifdef _WIN32
	objdump += ".exe";
#endif

	if (fs::exists(optExe)) {
		std::string objdumpCmd = "\"" + objdump + "\" --disassemble \"" + optExe + "\"";
		auto objResult = runCommand(objdumpCmd);
		std::string objdumpFile = (artifactsDir / (basename + ".opt.objdump")).string();
		std::ofstream f(objdumpFile);
		f << objResult.output;
		artifacts.push_back(basename + ".opt.objdump");
	}

	if (fs::exists(debugExe)) {
		std::string objdumpCmd = "\"" + objdump + "\" --disassemble \"" + debugExe + "\"";
		auto objResult = runCommand(objdumpCmd);
		std::string objdumpFile = (artifactsDir / (basename + ".debug.objdump")).string();
		std::ofstream f(objdumpFile);
		f << objResult.output;
		artifacts.push_back(basename + ".debug.objdump");
	}

	// Clean up temp source files (they're identical to the original)
	fs::remove(optSource);
	fs::remove(debugSource);

	return artifacts;
}

// Run a single test
TestResult runTest(const fs::path &testFile, const fs::path &testDir, bool verbose) {
	TestResult result;
	TestExpectation expected = parseExpectations(testFile);

	std::string basename = testFile.stem().string();

	// Create artifacts directory for temp files
	fs::path artifactsDir = testDir / "artifacts";
	fs::create_directories(artifactsDir);

	// Copy any DLLs from test directory to artifacts (for FFI tests)
	for (const auto &entry : fs::directory_iterator(testDir)) {
		if (entry.is_regular_file() && entry.path().extension() == ".dll") {
			fs::path destDll = artifactsDir / entry.path().filename();
			fs::copy_file(entry.path(), destDll, fs::copy_options::skip_existing);
		}
	}

	// Copy source to temp files (maxon has no -o option, output name is based on input name)
	fs::path optSource = artifactsDir / (basename + ".opt.maxon");
	fs::path debugSource = artifactsDir / (basename + ".debug.maxon");
	fs::copy_file(testFile, optSource, fs::copy_options::overwrite_existing);
	fs::copy_file(testFile, debugSource, fs::copy_options::overwrite_existing);

	std::string optExe = (artifactsDir / (basename + ".opt.exe")).string();
	std::string debugExe = (artifactsDir / (basename + ".debug.exe")).string();

	// Compile optimized
	std::string cmdOpt = "maxon compile \"" + optSource.string() + "\" -O";
	auto compileOpt = runCommand(cmdOpt);
	if (compileOpt.exitCode != 0) {
		result.errorMessage = "Optimized compilation failed:\n" + compileOpt.output;
		return result; // Leave temp files for diagnostics
	}

	// Compile debug
	std::string cmdDebug = "maxon compile \"" + debugSource.string() + "\"";
	auto compileDebug = runCommand(cmdDebug);
	if (compileDebug.exitCode != 0) {
		result.errorMessage = "Debug compilation failed:\n" + compileDebug.output;
		fs::remove(optExe);
		return result; // Leave temp files for diagnostics
	}

	// Run optimized executable with timeout
	auto runOpt = runCommandWithTimeout("\"" + optExe + "\"", TIMEOUT_SECONDS);
	if (runOpt.timedOut) {
		result.errorMessage = "Optimized executable timed out (>" + std::to_string(TIMEOUT_SECONDS) + "s)";
		return result;
	}
	result.optExitCode = runOpt.exitCode;
	result.optStdout = runOpt.output;

	// Run debug executable with timeout
	auto runDebug = runCommandWithTimeout("\"" + debugExe + "\"", TIMEOUT_SECONDS);
	if (runDebug.timedOut) {
		result.errorMessage = "Debug executable timed out (>" + std::to_string(TIMEOUT_SECONDS) + "s)";
		return result;
	}
	result.debugExitCode = runDebug.exitCode;
	result.debugStdout = runDebug.output;

	// Check results
	bool exitCodeMatch = (result.optExitCode == expected.exitCode &&
						  result.debugExitCode == expected.exitCode);
	bool stdoutMatch = true;

	if (expected.hasStdout) {
		stdoutMatch = (result.optStdout == expected.expectedStdout &&
					   result.debugStdout == expected.expectedStdout);
	}

	if (exitCodeMatch && stdoutMatch) {
		result.passed = true;
		// Clean up all temp files on success
		fs::remove(optSource);
		fs::remove(debugSource);
		fs::remove(optExe);
		fs::remove(debugExe);
	} else {
		std::stringstream ss;
		if (!exitCodeMatch) {
			ss << "Exit code mismatch:\n";
			ss << "  Expected: " << expected.exitCode << "\n";
			ss << "  Optimized: " << result.optExitCode << "\n";
			ss << "  Debug: " << result.debugExitCode;
		}
		if (!stdoutMatch) {
			if (!ss.str().empty())
				ss << "\n";
			ss << "Stdout mismatch:\n";
			ss << "  Expected: \"" << expected.expectedStdout << "\"\n";
			ss << "  Optimized: \"" << result.optStdout << "\"\n";
			ss << "  Debug: \"" << result.debugStdout << "\"";
		}
		result.errorMessage = ss.str();
		// Leave temp files for generateDiagnostics
	}

	return result;
}

void printUsage(const char *progname) {
	std::cout << "Usage: " << progname << " [options]\n";
	std::cout << "\nOptions:\n";
	std::cout << "  -v, --verbose    Show detailed output for each test\n";
	std::cout << "  -h, --help       Show this help message\n";
	std::cout << "\nRuns all backend tests in backend-tests/ directory.\n";
	std::cout << "Tests are run in order; stops at first failure.\n";
}

int main(int argc, char *argv[]) {
	enableAnsiColors();

	bool verbose = false;

	// Parse arguments
	for (int i = 1; i < argc; i++) {
		std::string arg = argv[i];
		if (arg == "-v" || arg == "--verbose") {
			verbose = true;
		} else if (arg == "-h" || arg == "--help") {
			printUsage(argv[0]);
			return 0;
		} else {
			std::cerr << "Unknown option: " << arg << "\n";
			printUsage(argv[0]);
			return 1;
		}
	}

	// Find test directory (relative to executable or current directory)
	fs::path testDir;
	fs::path exePath = fs::path(argv[0]).parent_path();

	// Try various locations
	std::vector<fs::path> searchPaths = {
		fs::current_path() / "backend-tests",
		exePath / ".." / "backend-tests",
		exePath / ".." / ".." / "backend-tests",
	};

	for (const auto &path : searchPaths) {
		if (fs::exists(path) && fs::is_directory(path)) {
			testDir = fs::canonical(path);
			break;
		}
	}

	if (testDir.empty()) {
		std::cerr << RED << "Error: Could not find backend-tests directory" << RESET << "\n";
		return 1;
	}

	// Get LLVM bin directory
	std::string llvmBinDir = LLVM_BIN_DIR;

	std::cout << BOLD << "Backend Test Runner" << RESET << "\n";
	std::cout << "Test directory: " << testDir << "\n";
	std::cout << "\n";

	// Clean test directory
	std::cout << "Cleaning test directory...\n";
	cleanTestDirectory(testDir);

	// Discover tests
	auto tests = discoverTests(testDir);
	auto mirVerifierTests = discoverMIRVerifierTests(testDir);
	auto mirExecTests = discoverMIRExecutionTests(testDir);

	if (tests.empty() && mirVerifierTests.empty() && mirExecTests.empty()) {
		std::cerr << YELLOW << "No test files found in " << testDir << RESET << "\n";
		return 1;
	}

	size_t totalTests = tests.size() + mirVerifierTests.size() + mirExecTests.size();
	std::cout << "Found " << totalTests << " tests";
	if (!mirExecTests.empty()) {
		std::cout << " (" << mirExecTests.size() << " MIR execution)";
	}
	std::cout << "\n\n";

	// Run tests
	int passed = 0;
	int failed = 0;

	for (const auto &testFile : tests) {
		std::string testName = testFile.stem().string();

		if (verbose) {
			std::cout << CYAN << "Running: " << RESET << testName << "... ";
			std::cout.flush();
		}

		TestResult result = runTest(testFile, testDir, verbose);

		if (result.passed) {
			passed++;
			if (verbose) {
				std::cout << GREEN << "PASSED" << RESET << "\n";
			} else {
				std::cout << GREEN << "." << RESET;
				std::cout.flush();
			}
		} else {
			failed++;
			if (!verbose) {
				std::cout << "\n";
			}
			std::cout << RED << "FAILED: " << RESET << testName << "\n";
			std::cout << result.errorMessage << "\n\n";

			// Generate diagnostics
			std::cout << "Generating diagnostic artifacts...\n";
			auto artifacts = generateDiagnostics(testFile, testDir, llvmBinDir);
			fs::path artifactsDir = testDir / "artifacts";
			std::cout << "Artifacts generated in " << artifactsDir << ":\n";
			for (const auto &artifact : artifacts) {
				std::cout << "  " << artifact << "\n";
			}

			// Stop at first failure
			break;
		}
	}

	if (!verbose && failed == 0) {
		std::cout << "\n";
	}

	// Run MIR verifier tests (continues seamlessly from backend tests)
	auto [mirVerifierPassed, mirVerifierFailed] = runMIRVerifierTests(mirVerifierTests, verbose);

	if (!verbose && mirVerifierPassed + mirVerifierFailed > 0) {
		std::cout << "\n";
	}

	// Run MIR execution tests
	auto [mirExecPassed, mirExecFailed] = runMIRExecutionTests(mirExecTests, verbose);

	if (!verbose && mirExecPassed + mirExecFailed > 0) {
		std::cout << "\n";
	}

	// Overall summary
	int totalPassed = passed + mirVerifierPassed + mirExecPassed;
	int totalFailed = failed + mirVerifierFailed + mirExecFailed;
	int totalTestCount = tests.size() + mirVerifierTests.size() + mirExecTests.size();

	std::cout << "\n"
			  << BOLD << "Results: " << RESET;
	std::cout << GREEN << totalPassed << " passed" << RESET;
	if (totalFailed > 0) {
		std::cout << ", " << RED << totalFailed << " failed" << RESET;
	}
	std::cout << " / " << totalTestCount << " total\n";

	return totalFailed > 0 ? 1 : 0;
}
