# Maxon Compiler and LSP Makefile
# Windows-compatible Makefile using Clang and Ninja

BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"
CC = "C:/Program Files/LLVM/bin/clang.exe"
CXX = "C:/Program Files/LLVM/bin/clang++.exe"
RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"

.PHONY: all clean compiler lsp lsp-server extension extension-build extension-watch extension-test extension-package extension-install help configure test

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
	@echo "  test             - Build and run LSP tests"
	@echo "  clean            - Clean all build artifacts"
	@echo "  help             - Show this help message"

# Configure CMake
configure:
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

# Build and run LSP tests
test:
	@echo Configuring and building LSP tests...
	@if not exist "lsp\tests\build" mkdir "lsp\tests\build"
	@cd lsp\tests\build && cmake .. -G "Ninja" -DCMAKE_BUILD_TYPE=Debug
	@cd lsp\tests\build && cmake --build .
	@echo Running LSP tests...
	@cd lsp\tests\build && ctest --output-on-failure

# Clean build artifacts
clean:
	@echo Cleaning build artifacts...
	@if exist "$(BUILD_DIR)" rmdir /s /q "$(BUILD_DIR)"
	@if exist "vscode-extension\out" rmdir /s /q "vscode-extension\out"
	@if exist "vscode-extension\node_modules" rmdir /s /q "vscode-extension\node_modules"
	@if exist "lsp\tests\build" rmdir /s /q "lsp\tests\build"
	@echo Clean complete.
