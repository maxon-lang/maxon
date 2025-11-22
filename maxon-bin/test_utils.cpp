#include "test_utils.h"

#include <cstdio>
#include <cstdlib>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#else
#include <errno.h>
#include <signal.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

std::string normalizeIR(const std::string &ir) {
	std::string normalized = ir;

	size_t pos = 0;
	while ((pos = normalized.find("source_filename = \"", pos)) != std::string::npos) {
		size_t start = pos + 19;
		size_t end = normalized.find("\"", start);
		if (end != std::string::npos) {
			normalized.replace(start, end - start, "test.maxon");
			pos = end + 1;
		} else {
			break;
		}
	}

	pos = 0;
	while ((pos = normalized.find("ModuleID = '", pos)) != std::string::npos) {
		size_t start = pos + 12;
		size_t end = normalized.find("'", start);
		if (end != std::string::npos) {
			normalized.replace(start, end - start, "test.maxon");
			pos = end + 1;
		} else {
			break;
		}
	}

	pos = 0;
	while ((pos = normalized.find("DIFile(filename: \"", pos)) != std::string::npos) {
		size_t start = pos + 18;
		size_t end = normalized.find("\"", start);
		if (end != std::string::npos) {
			normalized.replace(start, end - start, "test.maxon");
			pos = end + 1;
		} else {
			break;
		}
	}

	return normalized;
}

std::string showWithEscapes(const std::string &s, size_t maxLen) {
	std::string result;
	for (size_t i = 0; i < s.length() && result.length() < maxLen; ++i) {
		unsigned char c = s[i];
		if (c == '\n')
			result += "\\n";
		else if (c == '\r')
			result += "\\r";
		else if (c == '\t')
			result += "\\t";
		else if (c == '\\')
			result += "\\\\";
		else if (c >= 32 && c < 127)
			result += c;
		else {
			char buf[5];
			sprintf(buf, "\\x%02x", c);
			result += buf;
		}
	}
	if (s.length() > maxLen) {
		result += "...";
	}
	return result;
}

std::string showDiff(const std::string &expected, const std::string &actual) {
	// Find first difference
	size_t diffPos = 0;
	size_t minLen = (expected.length() < actual.length()) ? expected.length() : actual.length();
	while (diffPos < minLen && expected[diffPos] == actual[diffPos]) {
		diffPos++;
	}

	// If strings are identical up to min length, the difference is the length
	if (diffPos == minLen && expected.length() != actual.length()) {
		diffPos = minLen;
	}

	// Show context: 10 chars before the diff, then show everything from there
	size_t contextSize = 10;
	size_t startPos = (diffPos > contextSize) ? (diffPos - contextSize) : 0;

	// Build the result showing both strings from the diff point with context
	std::string result;

	// Show expected (with indentation for multi-line output)
	result += "    Expected: \"";
	if (startPos > 0) {
		result += "...";
	}
	result += showWithEscapes(expected.substr(startPos), SIZE_MAX);
	result += "\"\n";

	// Show actual (align with "Expected", with indentation for multi-line output)
	result += "    Actual:   \"";
	if (startPos > 0) {
		result += "...";
	}
	result += showWithEscapes(actual.substr(startPos), SIZE_MAX);
	result += "\"\n";

	return result;
}

int executeWithTimeout(const std::string &command, int timeoutSeconds) {
#ifdef _WIN32
	// Windows implementation using Job Objects to kill all child processes
	STARTUPINFOA si = {};
	si.cb = sizeof(si);
	si.dwFlags = STARTF_USESHOWWINDOW;
	si.wShowWindow = SW_HIDE; // Hide the console window
	PROCESS_INFORMATION pi = {};

	// Create a job object to ensure all child processes are killed on timeout
	HANDLE hJob = CreateJobObjectA(NULL, NULL);
	if (hJob == NULL) {
		return -1;
	}

	// Configure job to kill all processes when job handle is closed
	JOBOBJECT_EXTENDED_LIMIT_INFORMATION jeli = {};
	jeli.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
	if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, &jeli, sizeof(jeli))) {
		CloseHandle(hJob);
		return -1;
	}

	// Use cmd /c to execute the command through the shell
	std::string cmdLine = "cmd /c " + command;

	// CreateProcess requires non-const command line
	std::vector<char> cmdBuffer(cmdLine.begin(), cmdLine.end());
	cmdBuffer.push_back('\0');

	// Create process (suspended to add to job first)
	if (!CreateProcessA(NULL, cmdBuffer.data(), NULL, NULL, FALSE,
						CREATE_SUSPENDED, NULL, NULL, &si, &pi)) {
		CloseHandle(hJob);
		return -1;
	}

	// Add process to job
	if (!AssignProcessToJobObject(hJob, pi.hProcess)) {
		TerminateProcess(pi.hProcess, 1);
		CloseHandle(pi.hProcess);
		CloseHandle(pi.hThread);
		CloseHandle(hJob);
		return -1;
	}

	// Resume the process
	ResumeThread(pi.hThread);

	// Wait for process with timeout
	DWORD waitResult = WaitForSingleObject(pi.hProcess, timeoutSeconds * 1000);

	int exitCode = -1;
	if (waitResult == WAIT_TIMEOUT) {
		// Timeout - closing job will kill all processes in it
		exitCode = -1;
	} else if (waitResult == WAIT_OBJECT_0) {
		// Process completed - get exit code
		DWORD dwExitCode;
		if (GetExitCodeProcess(pi.hProcess, &dwExitCode)) {
			exitCode = static_cast<int>(dwExitCode);
		}
	}

	CloseHandle(pi.hProcess);
	CloseHandle(pi.hThread);
	CloseHandle(hJob); // This kills all processes in the job if still running

	// Give Windows time to release file locks after killing processes
	if (waitResult == WAIT_TIMEOUT) {
		Sleep(100);
	}

	return exitCode;

#else
	// Linux implementation using fork/exec with alarm
	pid_t pid = fork();

	if (pid < 0) {
		// Fork failed
		return -1;
	} else if (pid == 0) {
		// Child process - execute command
		execl("/bin/sh", "sh", "-c", command.c_str(), (char *)NULL);
		_exit(127); // exec failed
	} else {
		// Parent process - wait with timeout
		int status;
		time_t startTime = time(NULL);

		while (true) {
			pid_t result = waitpid(pid, &status, WNOHANG);

			if (result == pid) {
				// Process completed
				if (WIFEXITED(status)) {
					return WEXITSTATUS(status);
				} else {
					return -1;
				}
			} else if (result < 0) {
				// Error
				return -1;
			}

			// Check timeout
			if (time(NULL) - startTime >= timeoutSeconds) {
				// Timeout - kill the process
				kill(pid, SIGKILL);
				waitpid(pid, &status, 0);
				return -1;
			}

			// Sleep briefly before checking again
			usleep(100000); // 100ms
		}
	}
#endif
}
