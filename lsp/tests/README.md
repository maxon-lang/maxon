# Maxon LSP Server Tests

This directory contains unit tests and integration tests for the Maxon Language Server Protocol implementation.

## Test Structure

- **test_lsp_types.cpp** - Tests for LSP data structures (Position, Range, Diagnostic, etc.)
- **test_document_manager.cpp** - Tests for document lifecycle management (open, update, close)
- **test_json_rpc.cpp** - Tests for JSON-RPC message handling and protocol implementation
- **test_analyzer.cpp** - Tests for code analysis, completions, hover, and diagnostics
- **test_lsp_server.cpp** - Integration tests for the LSP server components

## Building Tests

### Using CMake directly:

```powershell
cd tests
mkdir build
cd build
cmake .. -G "Ninja"
cmake --build .
```

### Using the test runner script:

```powershell
cd tests
.\run_tests.ps1
```

The test runner script will:
1. Build all tests
2. Run each test executable
3. Report results and summary

## Running Individual Tests

After building, you can run individual tests:

```powershell
cd tests\build
.\test_lsp_types.exe
.\test_document_manager.exe
.\test_json_rpc.exe
.\test_analyzer.exe
.\test_lsp_server.exe
```

## Test Coverage

The test suite covers:

- **Protocol Layer**: JSON-RPC message handling, request/response, notifications
- **Document Management**: Opening, updating, closing, and versioning documents
- **Analysis**: Lexical analysis, parsing, diagnostics, and error detection
- **Language Features**: Completions, hover information, go-to-definition, document symbols
- **Data Structures**: All LSP types and their proper construction

## Adding New Tests

To add a new test:

1. Create a new `.cpp` file in the `tests` directory
2. Include the necessary headers from `../include/`
3. Write test functions using assertions
4. Add a main function that runs all tests
5. Update `CMakeLists.txt` to add the new test executable
6. Update `run_tests.ps1` to include the new test in the test array

## Test Guidelines

- Each test should be independent and not rely on other tests
- Use descriptive test names that explain what is being tested
- Test both success and failure cases
- Use assertions to verify expected behavior
- Print clear messages for test progress and results

## Example Test Pattern

```cpp
#include "../include/your_header.h"
#include <cassert>
#include <iostream>

void test_feature_name() {
    std::cout << "Testing feature..." << std::endl;
    
    // Setup
    YourClass obj;
    
    // Exercise
    auto result = obj.method();
    
    // Verify
    assert(result == expected_value);
    
    std::cout << "✓ Feature test passed" << std::endl;
}

int main() {
    std::cout << "Running Tests...\n" << std::endl;
    
    try {
        test_feature_name();
        
        std::cout << "\n✓ All tests passed!" << std::endl;
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "\n✗ Test failed: " << e.what() << std::endl;
        return 1;
    }
}
```

## Future Improvements

- Add more comprehensive edge case testing
- Implement mocking for better isolation
- Add performance benchmarks
- Add integration tests with actual LSP clients
- Add code coverage reporting
