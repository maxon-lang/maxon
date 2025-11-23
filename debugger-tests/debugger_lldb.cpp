#if !defined(_WIN32) || defined(USE_LLDB_ON_WINDOWS)

#include "debugger_lldb.h"
#include <chrono>
#include <filesystem>
#include <iostream>
#include <thread>

namespace fs = std::filesystem;

namespace DebuggerTest {

LLDBDebugger::LLDBDebugger() {
	lldb::SBDebugger::Initialize();
	debugger_ = lldb::SBDebugger::Create(false); // Disable source init files
}

LLDBDebugger::~LLDBDebugger() {
	if (process_.IsValid()) {
		process_.Kill();
	}
	if (target_.IsValid()) {
		debugger_.DeleteTarget(target_);
	}
	lldb::SBDebugger::Terminate();
}

bool LLDBDebugger::createTarget(const std::string &executablePath) {
	lldb::SBError error;

	// Create target
	target_ = debugger_.CreateTarget(executablePath.c_str(), nullptr, nullptr, true, error);

	if (!target_.IsValid() || error.Fail()) {
		lastError_ = std::string("Failed to create target: ") + error.GetCString();
		return false;
	}

	return true;
}

bool LLDBDebugger::setBreakpoint(const std::string &sourceFile, int lineNumber) {
	if (!target_.IsValid()) {
		lastError_ = "No valid target";
		return false;
	}

	lldb::SBBreakpoint breakpoint = target_.BreakpointCreateByLocation(
		sourceFile.c_str(),
		lineNumber);

	if (!breakpoint.IsValid()) {
		lastError_ = "Failed to set breakpoint at line " + std::to_string(lineNumber);
		return false;
	}

	return true;
}

bool LLDBDebugger::launch() {
	if (!target_.IsValid()) {
		lastError_ = "No valid target";
		return false;
	}

	lldb::SBError error;
	lldb::SBLaunchInfo launchInfo(nullptr);
	launchInfo.SetWorkingDirectory(fs::current_path().string().c_str());

	process_ = target_.Launch(launchInfo, error);

	if (!process_.IsValid() || error.Fail()) {
		lastError_ = std::string("Failed to launch process: ") + error.GetCString();
		return false;
	}

	return true;
}

bool LLDBDebugger::waitForStop(int timeoutSeconds) {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	return waitForEvent(timeoutSeconds);
}

bool LLDBDebugger::waitForEvent(int timeoutSeconds) {
	lldb::SBEvent event;

	if (!debugger_.GetListener().WaitForEventForBroadcasterWithType(
			timeoutSeconds,
			process_.GetBroadcaster(),
			lldb::SBProcess::eBroadcastBitStateChanged,
			event)) {
		lastError_ = "Timeout waiting for process event";
		return false;
	}

	lldb::StateType state = lldb::SBProcess::GetStateFromEvent(event);

	if (state == lldb::eStateStopped) {
		return true;
	} else if (state == lldb::eStateExited) {
		lastError_ = "Process exited";
		return false;
	} else {
		lastError_ = "Unexpected process state: " + std::to_string(state);
		return false;
	}
}

void LLDBDebugger::kill() {
	if (process_.IsValid()) {
		process_.Kill();
	}
}

bool LLDBDebugger::selectThread(int threadIndex) {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	lldb::SBThread thread = process_.GetThreadAtIndex(threadIndex);
	if (!thread.IsValid()) {
		lastError_ = "Invalid thread index";
		return false;
	}

	process_.SetSelectedThread(thread);
	return true;
}

int LLDBDebugger::getThreadCount() {
	if (!process_.IsValid())
		return 0;
	return process_.GetNumThreads();
}

int LLDBDebugger::getCurrentLine() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return -1;
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return -1;
	}

	lldb::SBFrame frame = thread.GetSelectedFrame();
	if (!frame.IsValid()) {
		lastError_ = "No valid frame";
		return -1;
	}

	lldb::SBLineEntry lineEntry = frame.GetLineEntry();
	return lineEntry.GetLine();
}

std::string LLDBDebugger::getCurrentFunction() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return "";
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return "";
	}

	lldb::SBFrame frame = thread.GetSelectedFrame();
	if (!frame.IsValid()) {
		lastError_ = "No valid frame";
		return "";
	}

	lldb::SBSymbol symbol = frame.GetSymbol();
	const char *name = symbol.GetName();
	return name ? name : "";
}

std::string LLDBDebugger::getCurrentFile() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return "";
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return "";
	}

	lldb::SBFrame frame = thread.GetSelectedFrame();
	if (!frame.IsValid()) {
		lastError_ = "No valid frame";
		return "";
	}

	lldb::SBLineEntry lineEntry = frame.GetLineEntry();
	lldb::SBFileSpec fileSpec = lineEntry.GetFileSpec();
	return fileSpec.GetFilename() ? fileSpec.GetFilename() : "";
}

bool LLDBDebugger::getVariableValue(const std::string &varName, std::string &outValue) {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return false;
	}

	lldb::SBFrame frame = thread.GetSelectedFrame();
	if (!frame.IsValid()) {
		lastError_ = "No valid frame";
		return false;
	}

	lldb::SBValue variable = frame.FindVariable(varName.c_str());
	if (!variable.IsValid()) {
		lastError_ = "Variable not found: " + varName;
		return false;
	}

	const char *value = variable.GetValue();
	if (value) {
		outValue = value;
		return true;
	}

	lastError_ = "Failed to get variable value";
	return false;
}

bool LLDBDebugger::stepIn() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return false;
	}

	thread.StepInto();
	return waitForEvent(5);
}

bool LLDBDebugger::stepOver() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return false;
	}

	thread.StepOver();
	return waitForEvent(5);
}

bool LLDBDebugger::stepOut() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	lldb::SBThread thread = process_.GetSelectedThread();
	if (!thread.IsValid()) {
		lastError_ = "No valid thread";
		return false;
	}

	thread.StepOut();
	return waitForEvent(5);
}

bool LLDBDebugger::continueExecution() {
	if (!process_.IsValid()) {
		lastError_ = "No valid process";
		return false;
	}

	lldb::SBError error = process_.Continue();
	if (error.Fail()) {
		lastError_ = std::string("Failed to continue: ") + error.GetCString();
		return false;
	}

	return waitForEvent(5);
}

std::string LLDBDebugger::getLastError() {
	return lastError_;
}

} // namespace DebuggerTest

#endif // !_WIN32 || USE_LLDB_ON_WINDOWS
