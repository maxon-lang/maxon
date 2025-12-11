# Maxon Test Harnesses

This document outlines the test infrastructure for the Maxon compiler project.

## Overview

The Maxon project uses 6 test harnesses organized by component and test type:

1. **Compiler Self-Tests** - Built-in compiler unit tests
2. **Language Fragment Tests** - Spec-driven language feature tests
3. **Backend Tests** - MIR codegen and execution verification
4. **LSP Tests** - C++ unit tests for Language Server Protocol
5. **VS Code Extension Tests** - TypeScript integration tests
6. **Debugger Integration Tests** - DWARF debug info validation

## Test Execution

### Run All Tests
```bash
make test
# or
bash scripts/run-all-tests.sh
```

### Individual Test Suites
```bash
make compiler && maxon self-test          # Compiler self-tests
make fragments                             # Fragment tests
make backend-test                          # Backend tests
make lsp-test                             # LSP tests
make extension-test                        # Extension tests
make debugger-test                         # Debugger tests
```

---

## 1. Compiler Self-Tests

**Location**: `maxon-bin/self_test.cpp`
**Language**: C++
**Runner**: Built into compiler binary
**Command**: `maxon self-test`

### Purpose
Unit tests for core compiler components: lexer, parser, semantic analyzer, and dead code elimination.

### Test Structure
Tests are defined as `TestCase` structs with:
- `name`: Test identifier
- `code`: Maxon source code
- `shouldPass`: Expected compile result (true/false)
- `description`: Test purpose

### Test Categories
- **Lexer Tests**: Token recognition, keywords, operators, literals
- **Parser Tests**: AST construction, syntax validation
- **Semantic Tests**: Type checking, scope resolution, invalid operations
- **Optimization Tests**: Dead code elimination, function counting

### Example Test
```cpp
{
    "parser_simple_function",
    "returns int\n"
    "    return a + b\n"
    "end 'add'",
    true,
    "Parse simple function with parameters and return"
}
```

### Output
- Color-coded results (green=pass, red=fail)
- Summary: `X/Y tests passed`
- Exit code: 0 (all pass), 1 (any fail)

---

## 2. Language Fragment Tests

**Location**: `language-tests/fragments/*.test`
**Language**: Maxon
**Runner**: `maxon-bin/test_runner.cpp`
**Command**: `maxon test-fragments`

### Purpose
Comprehensive testing of language features extracted from specification documents. Each test validates source code, optimized MIR, debug MIR, and runtime behavior.

### Test File Format
```
<Maxon source code>
---
<Expected optimized MIR>
---
<Expected debug MIR>
---
ExitCode: <expected exit code>
Stdout: ```
<expected stdout>
```
MaxoncStderr: <expected compiler errors>
```

### Test Discovery
- Tests are extracted from `specs/*.md` using `maxon extract-specs`
- Spec manifest: `language-tests/.spec-manifest.json`
- Validation: `bash scripts/validate-specs.sh` (checks for orphaned tests)

### Test Naming Convention
`<feature>.<subfeature>.<description>.<number>.test`

Examples:
- `for-loops.basic-range.1.test`
- `arrays.dynamic.heap-allocation.1.test`
- `safe-ffi.ffi-worker-crash-div-zero.1.test`

### Verification Steps
1. **Compile Phase**: Generate MIR with optimization
2. **MIR Comparison**: Match against expected optimized MIR
3. **Debug MIR**: Generate and compare debug (unoptimized) MIR
4. **Execution**: Run compiled binary, verify exit code and output
5. **Instruction Count**: Validate MIR instruction counts

### Special Features
- **FFI Testing**: Tests use `language-tests/ffi-test-lib/` shared library
- **Update Mode**: `maxon test-fragments --update` regenerates expected outputs
- **Parallel Execution**: Multi-threaded test runner
- **Verbose Modes**: `-v` (basic), `-vv` (detailed), `-vvv` (exhaustive)

### Example Test Categories
- Arithmetic operators, control flow, loops
- Arrays (static/dynamic), structs, pointers
- Function calls, namespaces, type system
- Safe FFI, extern functions, worker crash handling
- Optimizations (constant folding, dead code elimination, MemorySSA)

---

## 3. Backend Tests

**Location**: `backend-tests/*.maxon`, `backend-tests/mir/*.mir`
**Language**: Maxon and raw MIR
**Runner**: `backend-tests/runner/main.cpp`
**Command**: `./backend-tests/runner/build/backend-test-runner -v`

### Purpose
Low-level testing of MIR code generation, x86-64 codegen, and binary execution. Tests basic language constructs through complete programs.

### Test Types

#### A. Maxon Source Tests
Sequential numbered tests (`001-return-zero.maxon` through `076-ffi-memset-regression.maxon`):
- Basic arithmetic and control flow
- Variables (let/var), functions, loops
- Arrays, structs, characters
- Float operations, math functions
- For-loops with iterators
- FFI regression tests

#### B. MIR Verifier Tests
Located in `backend-tests/mir-verifier/*.mir`:
- SSA form validation
- PHI node placement
- Terminator correctness
- Type system verification
- Control flow validation

Examples:
- `phi-valid.mir` - Correct PHI usage
- `use-before-def.mir` - Invalid SSA (should fail)
- `missing-terminator.mir` - Incomplete block (should fail)

### Workflow
1. Compile Maxon source to MIR
2. Generate x86-64 assembly and binary
3. Execute binary and capture exit code
4. Compare result with expected value

### Output
- Per-test results with timing
- Summary statistics
- Verbose mode shows compilation/execution details

---

## 4. LSP Tests

**Location**: `lsp-server/tests/*.cpp`
**Language**: C++
**Framework**: Catch2 (amalgamated)
**Build**: CMake + Ninja
**Command**: `make lsp-test` or `ctest --output-on-failure`

### Purpose
Unit tests for the Maxon Language Server Protocol implementation.

### Test Files
- `test_lsp_types.cpp` - LSP type serialization/deserialization
- `test_document_manager.cpp` - Document state management
- `test_json_rpc.cpp` - JSON-RPC protocol handling
- `test_analyzer.cpp` - Semantic analysis features
- `test_hover.cpp` - Hover info provider
- `test_code_actions.cpp` - Quick fixes and refactorings
- `test_rename.cpp` - Symbol renaming
- `test_formatter.cpp` - Code formatting
- `test_lsp_server.cpp` - Server lifecycle and integration

### Build Configuration
- Shared library: `lsp_lib` (LSP sources + Maxon compiler components)
- Dependencies: LLVM Core, Support
- Individual test executables for each test suite
- CTest integration for automated runs

### Test Structure (Catch2)
```cpp
TEST_CASE("Feature description", "[tag]") {
    REQUIRE(condition);
    CHECK(condition);
}
```

### Running Tests
```bash
cd lsp-server/tests/build
cmake .. -G Ninja -DCMAKE_BUILD_TYPE=Debug
cmake --build .
ctest --output-on-failure
```

---

## 5. VS Code Extension Tests

**Location**: `vscode-extension/src/test/suite/*.test.ts`
**Language**: TypeScript
**Framework**: Mocha
**Command**: `make extension-test` or `npm run test`

### Purpose
Integration tests for the Maxon VS Code extension.

### Test Files
- `index.ts` - Test suite runner configuration
- `configuration.test.ts` - Extension settings and preferences
- `lsp-integration.test.ts` - LSP client/server communication

### Test Environment
- Uses VSCode Test API (`@vscode/test-electron`)
- Downloads VS Code instance to `.vscode-test/`
- Timeout: 10 seconds per test
- Color output enabled

### Test Structure (Mocha TDD)
```typescript
suite('Test Suite Name', () => {
    test('Test case', async () => {
        assert.strictEqual(actual, expected);
    });
});
```

### Running Tests
```bash
cd vscode-extension
npm install
npm run test
```

### Cleanup
- Temporary test files created in `temp/test_*.maxon`
- Automatically deleted after test runs

---

## 6. Debugger Integration Tests

**Location**: `debugger-tests/`
**Language**: C++
**Framework**: Custom (LLDB/Windows Debugger API)
**Command**: `make debugger-test`

### Purpose
Validates DWARF debug information generation and debugger integration (breakpoints, stepping, variable inspection).

### Test Programs
Located in `debugger-tests/test-programs/*.maxon`:
1. **simple-variables.maxon** - Variables and control flow
2. **function-calls.maxon** - Function stepping (in/out/over)
3. **loop-iteration.maxon** - Loop iteration stepping
4. **nested-scopes.maxon** - Variable scoping and shadowing

### Test Structure
Each test case defines:
- **Source file**: Maxon program path
- **Executable**: Compiled binary path
- **Breakpoints**: `{function, line, expectedVars{name, value}}`
- **Steps**: Sequence of debugger actions

### Step Types
- `Continue` - Run to next breakpoint
- `StepOver` - Execute next line (don't enter functions)
- `StepIn` - Step into function call
- `StepOut` - Return from current function

### Example Test Case
```cpp
TestCase test;
test.name = "Function Calls and Stepping";
test.breakpoints.push_back({"add", 3, {{"a", "5"}, {"b", "10"}}});
test.steps = {
    {StepType::Continue, 13, "main"},
    {StepType::StepIn, 2, "add"},
    {StepType::StepOut, 15, "main"}
};
```

### Debugger Implementations
- **Windows**: `debugger_windows.cpp` (Windows Debug API)
- **Linux/macOS**: `debugger_lldb.cpp` (LLDB API)
- **Factory**: `debugger_factory.cpp` (platform selection)

### Build
```bash
mkdir -p debugger-tests/build
cd debugger-tests/build
cmake .. -G Ninja -DCMAKE_BUILD_TYPE=Debug
cmake --build .
```

### Output
- Per-test results (PASSED/FAILED)
- Verbose mode shows breakpoint hits and variable values
- Summary: `X/Y tests passed`

---

## Test Workflow Summary

### Full Test Suite
```bash
make all          # Build everything
make test         # Run all 6 test harnesses
```

### Development Workflow
```bash
# After language changes:
make compiler
maxon extract-specs    # Update fragments from specs
maxon regen-fragments  # Regenerate expected outputs
maxon test-fragments   # Validate

# After LSP changes:
make lsp-test

# After extension changes:
make extension-test

# After backend changes:
make backend-test

# After debug info changes:
make debugger-test
```

### Continuous Integration
The `scripts/run-all-tests.sh` script:
1. Runs compiler self-tests
2. Runs backend MIR tests
3. Runs language fragment tests
4. Runs LSP C++ unit tests
5. Runs VS Code extension tests
6. Reports summary with pass/fail counts

Exit code: 0 (all pass), 1 (any failures)

---

## Test Coverage

| Component | Test Harness | Test Count | Coverage |
|-----------|--------------|------------|----------|
| Lexer | Self-tests | ~20 | Comprehensive |
| Parser | Self-tests | ~30 | Comprehensive |
| Semantic Analyzer | Self-tests + Fragments | ~40 + 200+ | Extensive |
| MIR Codegen | Backend + Fragments | 76 + 200+ | Extensive |
| Optimizations | Fragments | ~50 | Good |
| LSP Server | LSP Tests | ~60 | Comprehensive |
| VS Code Extension | Extension Tests | ~10 | Basic |
| Debugger Integration | Debugger Tests | 4 | Basic |
| FFI System | Fragments | ~15 | Good |

---

## Adding New Tests

### Self-Test
Add to `maxon-bin/self_test.cpp` in appropriate category.

### Fragment Test
1. Add code block to spec file in `specs/`
2. Run `maxon extract-specs`
3. Run `maxon test-fragments --update` to generate expected outputs
4. Validate with `maxon test-fragments`

### Backend Test
1. Create `backend-tests/NNN-description.maxon`
2. Add expected exit code as comment or run test to discover
3. Run `make backend-test`

### LSP Test
1. Add test to appropriate file in `lsp-server/tests/`
2. Build and run: `make lsp-test`

### Extension Test
1. Add test to `vscode-extension/src/test/suite/`
2. Run: `make extension-test`

### Debugger Test
1. Create test program in `debugger-tests/test-programs/`
2. Add test case to `debugger-tests/main.cpp`
3. Build and run: `make debugger-test`
