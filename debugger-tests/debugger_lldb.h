#pragma once

#if !defined(_WIN32) || defined(USE_LLDB_ON_WINDOWS)

#include "debugger_interface.h"
#include <lldb/API/LLDB.h>
#include <memory>
#include <string>

namespace DebuggerTest {

// LLDB-based debugger for Linux/macOS (and optionally Windows if LLDB works)
class LLDBDebugger : public IDebugger {
  public:
	LLDBDebugger();
	~LLDBDebugger() override;

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
	lldb::SBDebugger debugger_;
	lldb::SBTarget target_;
	lldb::SBProcess process_;
	std::string lastError_;

	bool waitForEvent(int timeoutSeconds);
};

} // namespace DebuggerTest

#endif // !_WIN32 || USE_LLDB_ON_WINDOWS
