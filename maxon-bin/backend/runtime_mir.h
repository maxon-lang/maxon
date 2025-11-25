#pragma once

#include "../mir/mir.h"
#include <memory>
#include <string>

namespace backend {
namespace runtime {

//==============================================================================
// MIR Runtime Library Loader
//==============================================================================
// Loads Maxon runtime library from textual MIR files.
// The runtime is written directly in MIR text format (similar to LLVM IR .ll),
// then parsed into MIR data structures for compilation.
//
// Runtime files:
// - runtime.mir         : Core functions (memset, floor, sin, cos, tan, etc.)
// - runtime_windows.mir : Windows platform (malloc via HeapAlloc, etc.)
// - runtime_linux.mir   : Linux platform (malloc via mmap syscall, etc.)
//==============================================================================

//------------------------------------------------------------------------------
// Target Platform
//------------------------------------------------------------------------------

enum class Platform {
	Windows, // Uses Win32 API (HeapAlloc, WriteFile, ExitProcess)
	Linux	 // Uses Linux syscalls (mmap, write, exit)
};

//------------------------------------------------------------------------------
// Runtime Loading
//------------------------------------------------------------------------------

// Load complete runtime module for the given platform
// Parses runtime.mir + platform-specific .mir file
std::unique_ptr<mir::MIRModule> loadRuntimeModule(Platform platform);

// Load runtime from a specific file path (for testing)
std::unique_ptr<mir::MIRModule> loadRuntimeFromFile(const std::string &path);

// Load runtime from string (for testing/embedding)
std::unique_ptr<mir::MIRModule> loadRuntimeFromString(const std::string &source);

//------------------------------------------------------------------------------
// Embedded Runtime (compiled into binary)
//------------------------------------------------------------------------------

// Get embedded runtime source for the given platform
// Returns the combined runtime.mir + platform .mir content
const char *getEmbeddedRuntimeSource(Platform platform);

} // namespace runtime
} // namespace backend
