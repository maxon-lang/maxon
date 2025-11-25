# Maxon Debugger Integration Tests

This directory contains automated tests for validating debug information generation in the Maxon compiler. The tests use platform-native debugging APIs to verify that compiled programs can be properly debugged.

## Platform Support

- **Windows**: Uses Win32 Debugging API (CreateProcess with DEBUG_PROCESS flag)
- **Linux**: Uses LLDB API (works reliably on Linux)
- **macOS**: Uses LLDB API (works reliably on macOS)

## Prerequisites

### Windows
- Windows SDK (for DbgHelp.lib and debugging APIs)
- Maxon compiler built and in PATH
- CMake and Ninja build system

### Linux/macOS
- LLDB library (typically included with LLVM installation)
- Maxon compiler built and in PATH
- CMake and Ninja build system

## Building and Running

```bash
# Build and run all debugger tests
make test-debugger
```

This will:
1. Configure the CMake build for the test runner
2. Build the `debugger-test-runner` executable
3. Compile test programs with debug information (`--debug`)
4. Run debugger integration tests

## Test Programs

Test programs are located in `test-programs/`:

- `simple-variables.maxon` - Basic variable tracking and control flow
- `function-calls.maxon` - Function calls with step-in/step-out
- `loop-iteration.maxon` - Loop execution and iteration
- `nested-scopes.maxon` - Variable scoping in nested blocks

## What Gets Tested

Each test verifies:

1. **Breakpoint Setting** - Breakpoints can be set at specific line numbers
2. **Stepping** - Step-in, step-over, step-out operations work correctly
3. **Variable Inspection** - Variables can be inspected and have correct values
4. **Line Numbers** - Debug locations match source line numbers
5. **Function Names** - Stack frames report correct function names

## Test Implementation

The test runner uses a platform-abstracted `IDebugger` interface with platform-specific implementations:

- **WindowsDebugger** - Uses Win32 Debugging APIs for native Windows process debugging
- **LLDBDebugger** - Uses LLDB SBDebugger API for Linux/macOS

The implementation automatically selects the appropriate debugger based on the build platform.
- Reports pass/fail for each test case

## Adding New Tests

To add a new debugger test:

1. Create a new `.maxon` file in `test-programs/`
2. Add a test case in `main.cpp` with:
   - Source file path
   - Breakpoint expectations (line numbers, expected variables)
   - Step sequence (step-in, step-over, continue, etc.)
   - Expected locations after each step

Example:
```cpp
TestCase test;
test.name = "Your Test Name";
test.sourceFile = (testProgramsDir / "your-test.maxon").string();
test.executablePath = (binDir / "your-test.exe").string();

test.breakpoints.push_back({
    "main",
    5,  // line number
    {{"x", "10"}, {"y", "20"}}  // expected variable values
});

test.steps = {
    {StepExpectation::StepType::Continue, 5, "main"},
    {StepExpectation::StepType::StepOver, 6, "main"},
    {StepExpectation::StepType::Continue, 0, ""}
};

runner.runTest(test);
```

## Known Limitations

### Current Status
- **Windows implementation**: Basic process launching and control working, but **source-level debugging requires DIA SDK** which is not yet implemented
  - Process can be launched and controlled
  - Symbols load but line-to-address mapping needs DIA SDK
  - For Windows debugging, **use Visual Studio debugger or VS Code with cppvsdbg** (both work perfectly)
- **Linux/macOS implementation**: Fully functional with LLDB
- Variable inspection works for simple types (int, float, bool, char) on Linux/macOS
- May require administrator privileges on some systems

### Windows Debugging Recommendation
**For debugging Maxon programs on Windows, use one of these fully-functional options:**
1. **VS Code** with `cppvsdbg` debugger (see `.vscode/launch.json` in the project root)
2. **Visual Studio** IDE debugger  
3. **WinDbg** from Windows SDK

These debuggers have full PDB support and work perfectly with Maxon executables. The integration tests in this directory demonstrate the debugger API but are primarily designed for CI/CD on Linux.

## Troubleshooting

### Windows
If tests fail to build:
- Verify Windows SDK is installed
- Check that dbghelp.lib is available

If tests fail to run:
- Ensure `maxon.exe` is in your PATH
- Check that debug information is being generated (`maxon compile -g --emit-ir` to inspect)
- For full debugging in development, use Visual Studio debugger or cppvsdbg in VS Code

### Linux/macOS
If tests fail to build:
- Verify LLDB is installed: `ldconfig -p | grep lldb` (Linux) or `lldb --version` (macOS)
- Set `LLVM_DIR` or `LLVM_HOME` environment variable if LLDB is in a non-standard location
- Install LLDB dev package: `sudo apt-get install lldb-dev` (Ubuntu/Debian)

If tests fail to run:
- Ensure `maxon` is in your PATH
- Check that debug information is being generated
- Verify LLDB can attach to processes (may require privileges)
