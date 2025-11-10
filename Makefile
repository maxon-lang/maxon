# Maxon Compiler and LSP Makefile
# Windows-compatible Makefile using Clang and Ninja

BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"
CC = "C:/Program Files/LLVM/bin/clang.exe"
CXX = "C:/Program Files/LLVM/bin/clang++.exe"
RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"

.PHONY: all clean compiler lsp lsp-server lsp-extension help configure

# Default target
all: configure
	cmake --build $(BUILD_DIR)

help:
	@powershell -Command "Write-Host 'Maxon Project Build Targets:' -ForegroundColor Cyan"
	@powershell -Command "Write-Host '  all              - Build compiler and LSP (default)' -ForegroundColor White"
	@powershell -Command "Write-Host '  configure        - Configure CMake build' -ForegroundColor White"
	@powershell -Command "Write-Host '  compiler         - Build only the Maxon compiler' -ForegroundColor White"
	@powershell -Command "Write-Host '  lsp-server       - Build only the C++ LSP server' -ForegroundColor White"
	@powershell -Command "Write-Host '  lsp-extension    - Build only the VS Code extension' -ForegroundColor White"
	@powershell -Command "Write-Host '  clean            - Clean all build artifacts' -ForegroundColor White"
	@powershell -Command "Write-Host '  help             - Show this help message' -ForegroundColor White"

# Configure CMake
configure:
	@powershell -Command "if (-not (Test-Path '$(BUILD_DIR)')) { New-Item -ItemType Directory -Path '$(BUILD_DIR)' | Out-Null }"
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release

# Build the Maxon compiler
compiler: configure
	cmake --build $(BUILD_DIR) --target maxonc

# Build both LSP server and extension
lsp: lsp-server lsp-extension

# Build the C++ LSP server
lsp-server: configure
	@powershell -Command "if (-not (Test-Path 'lsp\\include\\json.hpp')) { Write-Host 'Downloading nlohmann/json library...' -ForegroundColor Yellow; Invoke-WebRequest -Uri 'https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp' -OutFile 'lsp/include/json.hpp' }"
	cmake --build $(BUILD_DIR) --target maxon-lsp

# Build the VS Code extension
lsp-extension:
	@powershell -Command "Write-Host 'Building VS Code extension...' -ForegroundColor Yellow"
	@cd lsp\vscode-extension && npm install && npm run compile

# Clean build artifacts
clean:
	@powershell -Command "Write-Host 'Cleaning build artifacts...' -ForegroundColor Yellow"
	@powershell -Command "if (Test-Path '$(BUILD_DIR)') { Remove-Item -Recurse -Force '$(BUILD_DIR)' }"
	@powershell -Command "if (Test-Path 'lsp\\vscode-extension\\out') { Remove-Item -Recurse -Force 'lsp\\vscode-extension\\out' }"
	@powershell -Command "if (Test-Path 'lsp\\vscode-extension\\node_modules') { Remove-Item -Recurse -Force 'lsp\\vscode-extension\\node_modules' }"
	@powershell -Command "Write-Host 'Clean complete.' -ForegroundColor Green"
