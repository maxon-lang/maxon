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

# LLVM paths (use local llvm-project on both platforms)
LLVM_DIR ?= ./llvm-project
LLVM_DIR_ABS := $(shell realpath $(LLVM_DIR) 2>/dev/null || echo $(LLVM_DIR))
ifeq ($(PLATFORM),linux)
    CC = "$(LLVM_DIR_ABS)/bin/clang$(EXE_EXT)"
    CXX = "$(LLVM_DIR_ABS)/bin/clang++$(EXE_EXT)"
    LLC = "$(LLVM_DIR_ABS)/bin/llc$(EXE_EXT)"
else
    CC = "$(LLVM_DIR_ABS)/bin/clang$(EXE_EXT)"
    CXX = "$(LLVM_DIR_ABS)/bin/clang++$(EXE_EXT)"
    LLC = "$(LLVM_DIR_ABS)/bin/llc$(EXE_EXT)"
endif
BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"

# Windows-specific resource compiler (optional on Linux)
ifeq ($(PLATFORM),windows)
    RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"
endif

MAXON = bin/maxon$(EXE_EXT)
LSP_SERVER_BIN = bin/maxon-lsp-server$(EXE_EXT)
LSP_SERVER_BACKUP = $(LSP_SERVER_BIN).old

RUNTIME_LL = maxon-runtime/runtime.ll
RUNTIME_OBJ_WINDOWS = bin/runtime-windows.obj
RUNTIME_OBJ_LINUX = bin/runtime-linux.o
ifeq ($(PLATFORM),windows)
    RUNTIME_OBJ = $(RUNTIME_OBJ_WINDOWS)
else
    RUNTIME_OBJ = $(RUNTIME_OBJ_LINUX)
endif

.PHONY: all clean clean-all compiler lsp lsp-server extension extension-build extension-watch extension-test extension-package extension-install help configure lsp-test docs test runtime fragments debugger-test-build debugger-test

# Default target - build everything
all: compiler lsp-server extension-install debugger-test-build
	@echo All components built successfully.

help:
	@echo "Maxon Project Build Targets:"
	@echo "  all              - Build all components: compiler, LSP server, and VS Code extension (default)"
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
	@echo "  backend-test     - Build and run backend C++ unit tests"
	@echo "  docs             - Generate HTML documentation from specs"
	@echo "  fragments        - Regenerate fragments, validate specs, and run fragment tests"
	@echo "  validate-specs   - Check for orphaned test fragments not in any spec"
	@echo "  test             - Run all test suites (compiler self-tests, fragment tests, LSP tests, extension tests, debugger tests)"
	@echo "  debugger-test-build   - Build debugger integration tests (requires LLDB)"
	@echo "  debugger-test    - Run debugger integration tests (requires debugger-test-build first)"
	@echo "  clean            - Clean build artifacts"
	@echo "  clean-all        - Clean everything including generated files"
	@echo "  help             - Show this help message"

# Build Maxon runtime library
runtime: $(RUNTIME_OBJ)
	@echo "Maxon runtime library ready."

# Build Windows runtime
$(RUNTIME_OBJ_WINDOWS): maxon-runtime/runtime.ll maxon-runtime/platform_windows.ll
	@mkdir -p bin
	@cat maxon-runtime/platform_windows.ll maxon-runtime/runtime.ll > bin/runtime-windows.tmp
	@echo "Building Maxon runtime library for Windows..."
	@$(LLC) -filetype=obj -o $(RUNTIME_OBJ_WINDOWS) bin/runtime-windows.tmp >/dev/null 2>&1
	@rm bin/runtime-windows.tmp

# Build Linux runtime
$(RUNTIME_OBJ_LINUX): maxon-runtime/runtime.ll maxon-runtime/platform_linux.ll
	@mkdir -p bin
	@cat maxon-runtime/platform_linux.ll maxon-runtime/runtime.ll > bin/runtime-linux.tmp
	@echo "Building Maxon runtime library for Linux..."
	@$(LLC) -filetype=obj -o $(RUNTIME_OBJ_LINUX) bin/runtime-linux.tmp >/dev/null 2>&1
	@rm bin/runtime-linux.tmp

# Configure CMake
configure:
	@mkdir -p $(BUILD_DIR)
ifeq ($(PLATFORM),windows)
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS) >/dev/null 2>&1
else
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Release -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS) >/dev/null 2>&1
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

# Compile the VS Code extension
extension-build:
	@echo Compiling VS Code extension...
	@cd vscode-extension && npm install && npm run compile
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
ifeq ($(PLATFORM),windows)
	@cd lsp-server/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
else
	@cd lsp-server/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
endif
	@cd lsp-server/tests/build && cmake --build .
	@echo Running LSP tests...
	@cd lsp-server/tests/build && ctest --output-on-failure

# Build and run backend C++ unit tests
backend-test:
	@echo Configuring and building backend tests...
	@mkdir -p maxon-bin/tests/build
ifeq ($(PLATFORM),windows)
	@cd maxon-bin/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Debug
else
	@cd maxon-bin/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug
endif
	@cd maxon-bin/tests/build && cmake --build .
	@echo Running backend tests...
	@cd maxon-bin/tests/build && ctest --output-on-failure | grep -E '^\s*[0-9]+/|passed|failed|^Total'

# Generate documentation from spec files
docs: compiler
	@echo Generating documentation from specs...
	@maxon generate-docs
	@echo Documentation generated in maxon-docs/Output/
	@echo Documentation generated in maxon-docs/Output/

# Validate that all fragments are defined in spec files
validate-specs: compiler
	@echo Validating spec coverage...
	@bash scripts/validate-specs.sh

# Regenerate fragments, validate specs, and run fragment tests
fragments: compiler
	@echo
	@$(MAXON) extract-specs
	@echo
	@echo Validating spec coverage...
	@bash scripts/validate-specs.sh
	@echo
	@$(MAXON) regen-fragments
	@echo Running fragment tests...
	@$(MAXON) test-fragments

# Run all test suites
test: compiler lsp-server extension-build debugger-test
	@bash scripts/run-all-tests.sh

# Build debugger integration tests
debugger-test-build: compiler
	@echo Configuring and building debugger integration tests...
	@mkdir -p debugger-tests/build
ifeq ($(PLATFORM),windows)
	@cd debugger-tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
else
	@cd debugger-tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
endif
	@cd debugger-tests/build && cmake --build .
	@echo Debugger integration tests built successfully.

# Run debugger integration tests
debugger-test: debugger-test-build
	@echo Running debugger integration tests...
	cd debugger-tests/bin && ./debugger-test-runner$(EXE_EXT); \

# Clean build artifacts
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

# Clean everything
clean-all: clean
	@echo All build artifacts removed.
