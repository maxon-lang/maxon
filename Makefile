# Maxon Compiler and LSP Makefile
# Cross-platform Makefile using Clang and Ninja

# Platform detection
ifeq ($(OS),Windows_NT)
    PLATFORM := windows
    EXE_EXT := .exe
    OBJ_EXT := .obj
else
    PLATFORM := linux
    EXE_EXT :=
    OBJ_EXT := .o
endif

# LLVM paths (use local llvm-build by default, allow override via environment)
LLVM_DIR ?= ./llvm-build
BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"
CC = "$(LLVM_DIR)/bin/clang$(EXE_EXT)"
CXX = "$(LLVM_DIR)/bin/clang++$(EXE_EXT)"
LLC = "$(LLVM_DIR)/bin/llc$(EXE_EXT)"

# Windows-specific resource compiler (optional on Linux)
ifeq ($(PLATFORM),windows)
    RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"
endif

LSP_SERVER_BIN = bin/maxon-lsp-server$(EXE_EXT)
LSP_SERVER_BACKUP = $(LSP_SERVER_BIN).old

RUNTIME_LL = maxon-runtime/runtime.ll
RUNTIME_OBJ = bin/runtime$(OBJ_EXT)

.PHONY: all clean clean-llvm clean-all compiler lsp lsp-server extension extension-build extension-watch extension-test extension-package extension-install help configure lsp-test docs test runtime fragments debugger-test llvm

# Default target - build everything (download LLVM first if needed)
all: llvm compiler lsp-server extension-install
	@echo All components built successfully.

# Download LLVM if not present or version mismatch
llvm:
	@bash scripts/download-llvm.sh

help:
	@echo "Maxon Project Build Targets:"
	@echo "  all              - Build all components: compiler, LSP server, and VS Code extension (default)"
	@echo "  llvm             - Download LLVM if not present or version changed"
	@echo "  configure        - Configure CMake build"
	@echo "  runtime          - Build Maxon runtime library"
	@echo "  compiler         - Build only the Maxon compiler"
	@echo "  lsp              - Build LSP server and install VS Code extension"
	@echo "  lsp-server       - Build only the C++ LSP server"
	@echo "  extension        - Install dependencies and build VS Code extension"
	@echo "  extension-build  - Compile the VS Code extension"
	@echo "  extension-watch  - Watch and compile VS Code extension on changes"
	@echo "  extension-test   - Run VS Code extension tests"
	@echo "  extension-package - Package extension as .vsix"
	@echo "  extension-install - Install extension locally in VS Code"
	@echo "  lsp-test         - Build and run LSP C++ unit tests"
	@echo "  docs             - Generate HTML documentation from specs"
	@echo "  fragments        - Regenerate fragments, validate specs, and run fragment tests"
	@echo "  validate-specs   - Check for orphaned test fragments not in any spec"
	@echo "  test             - Run all test suites (compiler self-tests, fragment tests, LSP tests, extension tests, debugger tests)"
	@echo "  debugger-test    - Build and run debugger integration tests (requires LLDB)"
	@echo "  clean            - Clean maxon build artifacts (keep LLVM)"
	@echo "  clean-llvm       - Remove LLVM download"
	@echo "  clean-all        - Clean everything including LLVM"
	@echo "  help             - Show this help message"

# Build Maxon runtime library
runtime: $(RUNTIME_OBJ)

# Generate platform-specific runtime.ll from template
bin/runtime-platform.ll: maxon-runtime/runtime.ll.in
	@mkdir -p bin
ifeq ($(PLATFORM),windows)
	@sed 's/@TARGET_TRIPLE@/x86_64-pc-windows-msvc/g; s/@FLTUSED@/; _fltused - Windows floating-point support symbol\n; Required when using floating-point operations on Windows\n@_fltused = constant i32 39029/g' maxon-runtime/runtime.ll.in > bin/runtime-platform.ll
else
	@sed 's/@TARGET_TRIPLE@/x86_64-pc-linux-gnu/g; s/@FLTUSED@//g' maxon-runtime/runtime.ll.in > bin/runtime-platform.ll
endif

$(RUNTIME_OBJ): bin/runtime-platform.ll
	@echo "Building Maxon runtime library..."
	@$(LLC) -filetype=obj -o $(RUNTIME_OBJ) bin/runtime-platform.ll >/dev/null 2>&1

# Configure CMake
configure:
	@mkdir -p $(BUILD_DIR)
ifeq ($(PLATFORM),windows)
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release -DLLVM_DIR=$(LLVM_DIR) >/dev/null 2>&1
else
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Release -DLLVM_DIR=$(LLVM_DIR) >/dev/null 2>&1
endif

# Build the Maxon compiler (depends on runtime)
compiler: configure runtime
	cmake --build $(BUILD_DIR) --target maxon
	cmake --build $(BUILD_DIR) --target grammar_generator
	@if [ bin/grammar_generator$(EXE_EXT) -nt vscode-extension/syntaxes/maxon.tmLanguage.json ]; then echo "Generating TextMate grammar..."; ./bin/grammar_generator$(EXE_EXT) vscode-extension/syntaxes/maxon.tmLanguage.json; fi

# Build both LSP server and extension
lsp: lsp-server extension-install

# Build the C++ LSP server (depends on compiler sources)
lsp-server: compiler
	cmake --build $(BUILD_DIR) --target maxon-lsp-server

# Build the VS Code extension (install + compile)
extension: lsp-server
	@echo Installing dependencies and building VS Code extension...
	@cd vscode-extension && npm install && npm run compile
	@echo VS Code extension built successfully.

# Compile the VS Code extension (assumes dependencies are installed)
extension-build:
	@echo Compiling VS Code extension...
	@cd vscode-extension && npm run compile
	@echo Extension compiled.

# Watch mode for VS Code extension development
extension-watch:
	@echo Starting watch mode for VS Code extension...
	@cd vscode-extension && npm run watch

# Run VS Code extension tests
extension-test:
	@echo Running VS Code extension tests...
	@cd vscode-extension && npm run test
	@echo Cleaning up temporary test files...
	@rm -f temp/test_*.maxon 2>/dev/null || true
	@echo Extension tests complete.

# Package VS Code extension as .vsix
extension-package: extension-build
	@echo Packaging VS Code extension...
	@cd vscode-extension && npm run package
	@echo Extension packaged as .vsix file.

# Install VS Code extension locally
extension-install: extension-package
	@echo Installing VS Code extension locally...
	@cd vscode-extension && npm run install-extension
	@echo Extension installed. Reload VS Code to activate.

# Build and run LSP C++ unit tests
lsp-test:
	@echo Configuring and building LSP tests...
	@mkdir -p lsp-server/tests/build
	@cd lsp-server/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug -DLLVM_DIR=$(LLVM_DIR)
	@cd lsp-server/tests/build && cmake --build .
	@echo Running LSP tests...
	@cd lsp-server/tests/build && ctest --output-on-failure

# Generate documentation from spec files
docs: compiler
	@echo Generating documentation from specs...
	@cd maxon-docs && dotnet run
	@echo Documentation generated in maxon-docs/Output/

# Validate that all fragments are defined in spec files
validate-specs: compiler
	@echo Validating spec coverage...
	@bash scripts/validate-specs.sh

# Regenerate fragments, validate specs, and run fragment tests
fragments: compiler
	@echo
	@maxon extract-specs
	@echo
	@echo Validating spec coverage...
	@bash scripts/validate-specs.sh
	@echo
	@maxon regen-fragments
	@echo Running fragment tests...
	@maxon test-fragments

# Run all test suites
test: compiler lsp-server extension-build debugger-test
	@bash scripts/run-all-tests.sh

# Build and run debugger integration tests
debugger-test: compiler
	@echo Configuring and building debugger integration tests...
	@mkdir -p debugger-tests/build
	@cd debugger-tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug -DLLVM_DIR=$(LLVM_DIR)
	@cd debugger-tests/build && cmake --build .
	@echo Running debugger integration tests...
	@cd debugger-tests/bin && ./debugger-test-runner$(EXE_EXT)

# Clean build artifacts (keep LLVM)
clean:
	@echo Cleaning build artifacts...
	@rm -rf $(BUILD_DIR)
	@rm -rf bin
	@rm -rf vscode-extension/out
	@rm -rf vscode-extension/node_modules
	@rm -rf lsp-server/tests/build
	@rm -rf debugger-tests/build
	@rm -f debugger-tests/bin/*$(EXE_EXT)
	@echo Clean complete.

# Clean LLVM download
clean-llvm:
	@echo Removing LLVM download...
	@rm -rf llvm-build
	@echo LLVM removed.

# Clean everything including LLVM
clean-all: clean clean-llvm
	@echo All build artifacts and LLVM removed.
