#include "debugger_interface.h"

#ifdef _WIN32
#include "debugger_windows.h"
#else
#include "debugger_lldb.h"
#endif

namespace DebuggerTest {

std::unique_ptr<IDebugger> createDebugger() {
#ifdef _WIN32
	return std::make_unique<WindowsDebugger>();
#else
	return std::make_unique<LLDBDebugger>();
#endif
}

} // namespace DebuggerTest
