#pragma once

#include <memory>
#include <string>
#include <vector>

namespace DebuggerTest {

// Abstract debugger interface for platform-independent debugging
class IDebugger {
  public:
	virtual ~IDebugger() = default;

	// Target and process management
	virtual bool createTarget(const std::string &executablePath) = 0;
	virtual bool setBreakpoint(const std::string &sourceFile, int lineNumber) = 0;
	virtual bool launch() = 0;
	virtual bool waitForStop(int timeoutSeconds = 5) = 0;
	virtual void kill() = 0;

	// Thread and frame access
	virtual bool selectThread(int threadIndex = 0) = 0;
	virtual int getThreadCount() = 0;

	// Location verification
	virtual int getCurrentLine() = 0;
	virtual std::string getCurrentFunction() = 0;
	virtual std::string getCurrentFile() = 0;

	// Variable inspection
	virtual bool getVariableValue(const std::string &varName, std::string &outValue) = 0;

	// Execution control
	virtual bool stepIn() = 0;
	virtual bool stepOver() = 0;
	virtual bool stepOut() = 0;
	virtual bool continueExecution() = 0;

	// Error handling
	virtual std::string getLastError() = 0;
};

// Factory function to create platform-specific debugger
std::unique_ptr<IDebugger> createDebugger();

} // namespace DebuggerTest
