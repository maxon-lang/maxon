#pragma once

#ifdef _WIN32

#include "debugger_interface.h"
#define WIN32_LEAN_AND_MEAN
#include <dbghelp.h>
#include <memory>
#include <string>
#include <vector>
#include <windows.h>

namespace DebuggerTest {

// Windows-native debugger using Win32 Debugging API
class WindowsDebugger : public IDebugger {
  public:
	WindowsDebugger();
	~WindowsDebugger() override;

	bool createTarget(const std::string &executablePath) override;
	bool setBreakpoint(const std::string &sourceFile, int lineNumber) override;
	bool launch() override;
	bool waitForStop(int timeoutSeconds = 5) override;
	void kill() override;

	bool selectThread(int threadIndex = 0) override;
	int getThreadCount() override;

	int getCurrentLine() override;
	std::string getCurrentFunction() override;
	std::string getCurrentFile() override;

	bool getVariableValue(const std::string &varName, std::string &outValue) override;

	bool stepIn() override;
	bool stepOver() override;
	bool stepOut() override;
	bool continueExecution() override;

	std::string getLastError() override;

  private:
	struct Breakpoint {
		std::string sourceFile;
		int lineNumber;
		DWORD64 address;
		BYTE originalByte;
		bool enabled;
	};

	std::string executablePath_;
	std::string lastError_;
	PROCESS_INFORMATION processInfo_{};
	DEBUG_EVENT debugEvent_{};
	std::vector<Breakpoint> breakpoints_;
	bool isDebugging_ = false;
	bool isStopped_ = false;
	HANDLE currentThread_ = nullptr;
	CONTEXT threadContext_{};
	DWORD64 moduleBaseAddress_ = 0;

	bool initializeSymbols();
	bool findAddressForLine(const std::string &sourceFile, int lineNumber, DWORD64 &outAddress);
	bool setBreakpointAtAddress(DWORD64 address, Breakpoint &bp);
	bool removeBreakpointAtAddress(DWORD64 address);
	bool continueDebugEvent(DWORD continueStatus);
	bool handleDebugEvent();
	bool updateThreadContext();
	bool singleStep();
};

} // namespace DebuggerTest

#endif // _WIN32
