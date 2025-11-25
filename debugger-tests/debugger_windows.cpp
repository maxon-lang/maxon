#ifdef _WIN32

#include "debugger_windows.h"
#include <iostream>
#include <sstream>

#pragma comment(lib, "dbghelp.lib")

namespace DebuggerTest {

WindowsDebugger::WindowsDebugger() {
	ZeroMemory(&processInfo_, sizeof(processInfo_));
	ZeroMemory(&debugEvent_, sizeof(debugEvent_));
}

WindowsDebugger::~WindowsDebugger() {
	if (isDebugging_) {
		kill();
	}
}

bool WindowsDebugger::createTarget(const std::string &executablePath) {
	executablePath_ = executablePath;
	return true;
}

bool WindowsDebugger::setBreakpoint(const std::string &sourceFile, int lineNumber) {
	Breakpoint bp;
	bp.sourceFile = sourceFile;
	bp.lineNumber = lineNumber;
	bp.enabled = false; // Will be enabled after process starts and symbols load
	bp.address = 0;
	bp.originalByte = 0;

	breakpoints_.push_back(bp);
	return true;
}

bool WindowsDebugger::launch() {
	if (executablePath_.empty()) {
		lastError_ = "No executable path specified";
		return false;
	}

	STARTUPINFOA si{};
	si.cb = sizeof(si);

	// Create process with DEBUG_PROCESS flag
	BOOL result = CreateProcessA(
		executablePath_.c_str(),
		nullptr,				 // Command line
		nullptr,				 // Process security attributes
		nullptr,				 // Thread security attributes
		FALSE,					 // Inherit handles
		DEBUG_ONLY_THIS_PROCESS, // Creation flags
		nullptr,				 // Environment
		nullptr,				 // Current directory
		&si,
		&processInfo_);

	if (!result) {
		lastError_ = "Failed to create process: " + std::to_string(GetLastError());
		return false;
	}

	isDebugging_ = true;

	// Initialize symbols after process creation
	if (!initializeSymbols()) {
		lastError_ = "Failed to initialize symbols";
		kill();
		return false;
	}

	return true;
}

bool WindowsDebugger::initializeSymbols() {
	// Set symbol options to load line information
	SymSetOptions(SYMOPT_LOAD_LINES | SYMOPT_DEBUG | SYMOPT_UNDNAME);

	// Set symbol search path to include the executable directory
	std::string exeDir = executablePath_.substr(0, executablePath_.find_last_of("\\/"));

	// Initialize symbol handler with search path
	if (!SymInitialize(processInfo_.hProcess, exeDir.c_str(), FALSE)) {
		lastError_ = "SymInitialize failed: " + std::to_string(GetLastError());
		return false;
	}

	// Note: We'll load symbols after the first DLL load event
	return true;
}

bool WindowsDebugger::waitForStop(int timeoutSeconds) {
	if (!isDebugging_) {
		lastError_ = "Process not being debugged";
		return false;
	}

	DWORD timeout = timeoutSeconds * 1000;
	DWORD startTime = GetTickCount();

	while (GetTickCount() - startTime < timeout) {
		if (WaitForDebugEvent(&debugEvent_, 100)) {
			if (!handleDebugEvent()) {
				return false;
			}

			if (isStopped_) {
				return true;
			}
		}
	}

	lastError_ = "Timeout waiting for process to stop";
	return false;
}

bool WindowsDebugger::handleDebugEvent() {
	switch (debugEvent_.dwDebugEventCode) {
	case CREATE_PROCESS_DEBUG_EVENT: {
		// Process created - symbols will be loaded next

		// Load symbols for the main executable
		DWORD64 baseAddress = (DWORD64)debugEvent_.u.CreateProcessInfo.lpBaseOfImage;
		moduleBaseAddress_ = SymLoadModuleEx(
			processInfo_.hProcess,
			debugEvent_.u.CreateProcessInfo.hFile,
			executablePath_.c_str(),
			nullptr,
			baseAddress,
			0,
			nullptr,
			0);

		// Symbol loading is silent - errors will show up later if breakpoints fail

		CloseHandle(debugEvent_.u.CreateProcessInfo.hFile);

		// Set breakpoints now that symbols are loaded
		for (auto &bp : breakpoints_) {
			if (!bp.enabled && findAddressForLine(bp.sourceFile, bp.lineNumber, bp.address)) {
				setBreakpointAtAddress(bp.address, bp);
			}
		}

		continueDebugEvent(DBG_CONTINUE);
		break;
	}

	case EXCEPTION_DEBUG_EVENT:
		if (debugEvent_.u.Exception.ExceptionRecord.ExceptionCode == EXCEPTION_BREAKPOINT) {
			isStopped_ = true;
			currentThread_ = OpenThread(THREAD_ALL_ACCESS, FALSE, debugEvent_.dwThreadId);
			updateThreadContext();
			return true; // Don't continue, let caller handle
		}
		continueDebugEvent(DBG_EXCEPTION_NOT_HANDLED);
		break;

	case EXIT_PROCESS_DEBUG_EVENT:
		// Process exited silently
		isDebugging_ = false;
		isStopped_ = false;
		continueDebugEvent(DBG_CONTINUE);
		break;

	case LOAD_DLL_DEBUG_EVENT:
		CloseHandle(debugEvent_.u.LoadDll.hFile);
		continueDebugEvent(DBG_CONTINUE);
		break;

	default:
		continueDebugEvent(DBG_CONTINUE);
		break;
	}

	return true;
}

bool WindowsDebugger::findAddressForLine(const std::string &sourceFile, [[maybe_unused]] int lineNumber, [[maybe_unused]] DWORD64 &outAddress) {
	// Use SymEnumLines to find address for source line
	// This is a simplified implementation - full version would enumerate all lines
	// For now, we'll store breakpoints and resolve them when we hit any breakpoint

	// Extract just the filename from the path
	size_t lastSlash = sourceFile.find_last_of("/\\");
	[[maybe_unused]] std::string fileName = (lastSlash != std::string::npos) ? sourceFile.substr(lastSlash + 1) : sourceFile;

	// We need to enumerate symbols and find the line
	// For now, return false and we'll handle breakpoints differently
	return false;
}

bool WindowsDebugger::setBreakpointAtAddress(DWORD64 address, Breakpoint &bp) {
	// Read original byte
	SIZE_T bytesRead;
	if (!ReadProcessMemory(processInfo_.hProcess, (LPCVOID)address, &bp.originalByte, 1, &bytesRead)) {
		return false;
	}

	// Write INT 3 (0xCC) instruction
	BYTE int3 = 0xCC;
	SIZE_T bytesWritten;
	if (!WriteProcessMemory(processInfo_.hProcess, (LPVOID)address, &int3, 1, &bytesWritten)) {
		return false;
	}

	bp.enabled = true;
	FlushInstructionCache(processInfo_.hProcess, (LPCVOID)address, 1);
	return true;
}

bool WindowsDebugger::continueDebugEvent(DWORD continueStatus) {
	return ContinueDebugEvent(debugEvent_.dwProcessId, debugEvent_.dwThreadId, continueStatus) != 0;
}

bool WindowsDebugger::updateThreadContext() {
	if (!currentThread_)
		return false;

	threadContext_.ContextFlags = CONTEXT_FULL;
	return GetThreadContext(currentThread_, &threadContext_) != 0;
}

void WindowsDebugger::kill() {
	if (isDebugging_) {
		TerminateProcess(processInfo_.hProcess, 1);
		DebugActiveProcessStop(processInfo_.dwProcessId);
		CloseHandle(processInfo_.hProcess);
		CloseHandle(processInfo_.hThread);
		if (currentThread_)
			CloseHandle(currentThread_);
		isDebugging_ = false;
		isStopped_ = false;
	}
}

bool WindowsDebugger::selectThread([[maybe_unused]] int threadIndex) {
	// Simplified - just use current thread
	return currentThread_ != nullptr;
}

int WindowsDebugger::getThreadCount() {
	return isDebugging_ ? 1 : 0;
}

int WindowsDebugger::getCurrentLine() {
	if (!currentThread_ || !isStopped_) {
		lastError_ = "No current thread or not stopped";
		return -1;
	}

	// Get instruction pointer from context
	DWORD64 pc = threadContext_.Rip;

	// Use SymGetLineFromAddr64 to get line information
	IMAGEHLP_LINE64 line{};
	line.SizeOfStruct = sizeof(IMAGEHLP_LINE64);
	DWORD displacement = 0;

	if (SymGetLineFromAddr64(processInfo_.hProcess, pc, &displacement, &line)) {
		return line.LineNumber;
	}

	DWORD err = GetLastError();
	lastError_ = "Failed to get line info: " + std::to_string(err);
	return -1;
}

std::string WindowsDebugger::getCurrentFunction() {
	if (!currentThread_ || !isStopped_) {
		lastError_ = "No current thread or not stopped";
		return "";
	}

	// Get instruction pointer from context
	DWORD64 pc = threadContext_.Rip;

	// Get symbol information
	char buffer[sizeof(SYMBOL_INFO) + MAX_SYM_NAME * sizeof(TCHAR)];
	PSYMBOL_INFO symbol = (PSYMBOL_INFO)buffer;
	symbol->SizeOfStruct = sizeof(SYMBOL_INFO);
	symbol->MaxNameLen = MAX_SYM_NAME;

	DWORD64 displacement = 0;
	if (SymFromAddr(processInfo_.hProcess, pc, &displacement, symbol)) {
		return std::string(symbol->Name, symbol->NameLen);
	}

	lastError_ = "Failed to get symbol info: " + std::to_string(GetLastError());
	return "";
}

std::string WindowsDebugger::getCurrentFile() {
	if (!currentThread_ || !isStopped_) {
		lastError_ = "No current thread or not stopped";
		return "";
	}

	// Get instruction pointer from context
	DWORD64 pc = threadContext_.Rip;

	// Use SymGetLineFromAddr64 to get file information
	IMAGEHLP_LINE64 line{};
	line.SizeOfStruct = sizeof(IMAGEHLP_LINE64);
	DWORD displacement = 0;

	if (SymGetLineFromAddr64(processInfo_.hProcess, pc, &displacement, &line)) {
		return line.FileName ? line.FileName : "";
	}

	lastError_ = "Failed to get file info: " + std::to_string(GetLastError());
	return "";
}

bool WindowsDebugger::getVariableValue([[maybe_unused]] const std::string &varName, [[maybe_unused]] std::string &outValue) {
	// Variable inspection requires walking the stack frame and reading local variables
	// This is complex and requires parsing debug info structures
	// For a basic implementation, we'd need to:
	// 1. Get the current frame base pointer
	// 2. Use SymEnumSymbols to find local variables
	// 3. Read memory at the variable's location
	// This is not trivial and would require significant code

	lastError_ = "Variable inspection not yet implemented";
	return false;
}

bool WindowsDebugger::stepIn() {
	return singleStep();
}

bool WindowsDebugger::stepOver() {
	return singleStep();
}

bool WindowsDebugger::stepOut() {
	lastError_ = "stepOut not fully implemented";
	return false;
}

bool WindowsDebugger::singleStep() {
	if (!currentThread_ || !isStopped_)
		return false;

	// Set trap flag for single step
	threadContext_.EFlags |= 0x100; // Trap flag
	if (!SetThreadContext(currentThread_, &threadContext_)) {
		lastError_ = "Failed to set thread context";
		return false;
	}

	isStopped_ = false;
	continueDebugEvent(DBG_CONTINUE);
	return true;
}

bool WindowsDebugger::continueExecution() {
	if (!isStopped_)
		return false;
	isStopped_ = false;
	return continueDebugEvent(DBG_CONTINUE);
}

std::string WindowsDebugger::getLastError() {
	return lastError_;
}

} // namespace DebuggerTest

#endif // _WIN32
