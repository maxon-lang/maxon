# Maxon Compiler and LSP Makefile
# Windows-compatible Makefile using Clang and Ninja

BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"
CC = "C:/Program Files/LLVM/bin/clang.exe"
CXX = "C:/Program Files/LLVM/bin/clang++.exe"
RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"

.PHONY: all clean compiler lsp lsp-server extension extension-build extension-watch extension-test extension-package help configure test

# Default target
all: configure
	cmake --build $(BUILD_DIR)

help:
	@powershell -Command "Write-Host 'Maxon Project Build Targets:' -ForegroundColor Cyan; Write-Host '  all              - Build compiler and LSP (default)' -ForegroundColor White; Write-Host '  configure        - Configure CMake build' -ForegroundColor White; Write-Host '  compiler         - Build only the Maxon compiler' -ForegroundColor White; Write-Host '  lsp-server       - Build only the C++ LSP server' -ForegroundColor White; Write-Host '  extension        - Install dependencies and build VS Code extension' -ForegroundColor White; Write-Host '  extension-build  - Compile the VS Code extension' -ForegroundColor White; Write-Host '  extension-watch  - Watch and compile VS Code extension on changes' -ForegroundColor White; Write-Host '  extension-test   - Run VS Code extension tests' -ForegroundColor White; Write-Host '  extension-package - Package extension as .vsix' -ForegroundColor White; Write-Host '  test             - Build and run LSP tests' -ForegroundColor White; Write-Host '  clean            - Clean all build artifacts' -ForegroundColor White; Write-Host '  help             - Show this help message' -ForegroundColor White"

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
	@powershell -Command "if (-not (Test-Path 'lsp\\include\\json.hpp')) { Write-Host 'Downloading nlohmann/json library...' -ForegroundColor Yellow; Invoke-WebRequest -Uri 'https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp' -OutFile 'lsp/include/json.hpp' }"
	cmake --build $(BUILD_DIR) --target maxon-lsp

# Build the VS Code extension (install + compile)
extension:
	@powershell -Command "Write-Host 'Installing dependencies and building VS Code extension...' -ForegroundColor Yellow"
	@cd vscode-extension && npm install && npm run compile
	@powershell -Command "Write-Host 'VS Code extension built successfully.' -ForegroundColor Green"

# Compile the VS Code extension (assumes dependencies are installed)
extension-build:
	@powershell -Command "Write-Host 'Compiling VS Code extension...' -ForegroundColor Yellow"
	@cd vscode-extension && npm run compile
	@powershell -Command "Write-Host 'Extension compiled.' -ForegroundColor Green"

# Watch mode for VS Code extension development
extension-watch:
	@powershell -Command "Write-Host 'Starting watch mode for VS Code extension...' -ForegroundColor Yellow"
	@cd vscode-extension && npm run watch

# Run VS Code extension tests
extension-test:
	@powershell -Command "Write-Host 'Running VS Code extension tests...' -ForegroundColor Yellow"
	@cd vscode-extension && npm run test

# Package VS Code extension as .vsix
extension-package: extension-build
	@powershell -Command "Write-Host 'Packaging VS Code extension...' -ForegroundColor Yellow"
	@cd vscode-extension && npm run package
	@powershell -Command "Write-Host 'Extension packaged as .vsix file.' -ForegroundColor Green"

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
	@powershell -Command "Write-Host 'Cleaning build artifacts...' -ForegroundColor Yellow"
	@powershell -Command "if (Test-Path '$(BUILD_DIR)') { Remove-Item -Recurse -Force '$(BUILD_DIR)' }"
	@powershell -Command "if (Test-Path 'vscode-extension\\out') { Remove-Item -Recurse -Force 'vscode-extension\\out' }"
	@powershell -Command "if (Test-Path 'vscode-extension\\node_modules') { Remove-Item -Recurse -Force 'vscode-extension\\node_modules' }"
	@powershell -Command "if (Test-Path 'lsp\\tests\\build') { Remove-Item -Recurse -Force 'lsp\\tests\\build' }"
	@powershell -Command "Write-Host 'Clean complete.' -ForegroundColor Green"
