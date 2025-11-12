# Maxon Compiler and LSP Makefile
# Windows-compatible Makefile using Clang and Ninja

BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"
CC = "C:/Program Files/LLVM/bin/clang.exe"
CXX = "C:/Program Files/LLVM/bin/clang++.exe"
RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"

.PHONY: all clean compiler lsp lsp-server extension extension-build extension-watch extension-test extension-package extension-install help configure lsp-test language-tests language-tests-update docs test

# Default target
all: configure
	cmake --build $(BUILD_DIR)

help:
	@echo "Maxon Project Build Targets:"
	@echo "  all              - Build compiler and LSP (default)"
	@echo "  configure        - Configure CMake build"
	@echo "  compiler         - Build only the Maxon compiler"
	@echo "  lsp-server       - Build only the C++ LSP server"
	@echo "  extension        - Install dependencies and build VS Code extension"
	@echo "  extension-build  - Compile the VS Code extension"
	@echo "  extension-watch  - Watch and compile VS Code extension on changes"
	@echo "  extension-test   - Run VS Code extension tests"
	@echo "  extension-package - Package extension as .vsix"
	@echo "  extension-install - Install extension locally in VS Code"
	@echo "  lsp-test         - Build and run LSP C++ unit tests"
	@echo "  language-tests   - Run Maxon language fragment tests"
	@echo "  language-tests-update - Update all test fragments with current compiler output"
	@echo "  docs             - Generate HTML documentation and test fragments"
	@echo "  test FILE=<file> - Compile and run a test program (e.g., make test FILE=test-cast)"
	@echo "  clean            - Clean all build artifacts"
	@echo "  help             - Show this help message"

# Configure CMake
configure:
	@powershell -Command "if (-not (Test-Path '$(BUILD_DIR)')) { New-Item -ItemType Directory -Path '$(BUILD_DIR)' | Out-Null }"
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release

# Build the Maxon compiler
compiler: configure
	cmake --build $(BUILD_DIR) --target maxonc

# Build both LSP server and extension
lsp: lsp-server extension

# Build the C++ LSP server
lsp-server: configure
	@if not exist "lsp\include\json.hpp" (echo Downloading nlohmann/json library... && powershell -Command "Invoke-WebRequest -Uri 'https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp' -OutFile 'lsp/include/json.hpp'")
	cmake --build $(BUILD_DIR) --target maxon-lsp

# Build the VS Code extension (install + compile)
extension:
	@echo Installing dependencies and building VS Code extension...
	@powershell -Command "cd vscode-extension; npm install; npm run compile"
	@echo VS Code extension built successfully.

# Compile the VS Code extension (assumes dependencies are installed)
extension-build:
	@echo Compiling VS Code extension...
	@powershell -Command "cd vscode-extension; npm run compile"
	@echo Extension compiled.

# Watch mode for VS Code extension development
extension-watch:
	@echo Starting watch mode for VS Code extension...
	@powershell -Command "cd vscode-extension; npm run watch"

# Run VS Code extension tests
extension-test:
	@echo Running VS Code extension tests...
	@powershell -Command "cd vscode-extension; npm run test"

# Package VS Code extension as .vsix
extension-package: extension-build
	@echo Packaging VS Code extension...
	@powershell -Command "cd vscode-extension; npm run package"
	@echo Extension packaged as .vsix file.

# Install VS Code extension locally
extension-install: extension-package
	@echo Installing VS Code extension locally...
	@powershell -Command "cd vscode-extension; npm run install-extension"
	@echo Extension installed. Reload VS Code to activate.

# Build and run LSP C++ unit tests
lsp-test:
	@echo Configuring and building LSP tests...
	@if not exist "lsp\tests\build" mkdir "lsp\tests\build"
	@cd lsp\tests\build && cmake .. -G "Ninja" -DCMAKE_BUILD_TYPE=Debug
	@cd lsp\tests\build && cmake --build .
	@echo Running LSP tests...
	@cd lsp\tests\build && ctest --output-on-failure

# Run Maxon language fragment tests
language-tests: compiler
	@echo Running Maxon language fragment tests...
	@powershell -Command "cd language-tests; dotnet test --verbosity normal"

# Update all test fragments with current compiler output
language-tests-update: compiler
	@echo Updating all test fragments with current compiler output...
	@powershell -Command "cd language-tests; $env:UPDATE_FRAGMENTS='1'; dotnet test --verbosity normal"
	@echo Test fragments updated. Please inspect the changes carefully.

# Generate documentation (HTML output + test fragments)
docs:
	@echo Generating documentation...
	@powershell -Command "cd docs; dotnet run"
	@echo Documentation generated in docs/Output/
	@echo Test fragments created in language-tests/doc-fragments/

# Compile and run a test program
# Usage: make test FILE=test-cast
# This will compile examples/<FILE>.maxon and run it
test: compiler
ifndef FILE
	@echo Error: Please specify FILE parameter
	@echo Usage: make test FILE=test-cast
	@echo This will compile and run examples/test-cast.maxon
	@exit 1
endif
	@echo Compiling examples/$(FILE).maxon...
	@./build/bin/maxonc.exe examples/$(FILE).maxon -o examples/$(FILE).exe
	@echo === Running examples/$(FILE).exe ===
	@./examples/$(FILE).exe
	@echo === Test complete ===

# Clean build artifacts
clean:
	@echo Cleaning build artifacts...
	@powershell -Command "if (Test-Path '$(BUILD_DIR)') { Remove-Item -Recurse -Force '$(BUILD_DIR)' }"
	@powershell -Command "if (Test-Path 'vscode-extension/out') { Remove-Item -Recurse -Force 'vscode-extension/out' }"
	@powershell -Command "if (Test-Path 'vscode-extension/node_modules') { Remove-Item -Recurse -Force 'vscode-extension/node_modules' }"
	@powershell -Command "if (Test-Path 'lsp/tests/build') { Remove-Item -Recurse -Force 'lsp/tests/build' }"
	@echo Clean complete.
