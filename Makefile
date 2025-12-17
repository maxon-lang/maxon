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

RUNTIME_MIR_WINDOWS = bin/runtime_windows.mir
RUNTIME_MIR_LINUX = bin/runtime_linux.mir
ifeq ($(PLATFORM),windows)
    RUNTIME_MIR = $(RUNTIME_MIR_WINDOWS)
else
    RUNTIME_MIR = $(RUNTIME_MIR_LINUX)
endif

# Git branch detection - only install extension on main branch
GIT_BRANCH := $(shell git rev-parse --abbrev-ref HEAD 2>/dev/null)
ifeq ($(GIT_BRANCH),main)
    EXTENSION_TARGET := extension-install
else
    EXTENSION_TARGET :=
endif

.PHONY: all clean clean-all compiler extension extension-build extension-watch extension-test extension-package extension-install help configure lsp-test docs test runtime fragments debugger-test-build debugger-test ffi-test-lib

# Default target - build everything
all: compiler $(EXTENSION_TARGET) debugger-test-build docs
	@echo All components built successfully.

help:
	@echo ""
	@echo "Maxon Project Build System"
	@echo "=========================="
	@echo ""
	@echo "  all                Build everything (default)"
	@echo "  compiler           Build the Maxon compiler and grammar generator"
	@echo "  runtime            Build Maxon runtime library"
	@echo "  backend-test       Build and run backend tests"
	@echo ""
	@echo "  lsp-test           Build and run LSP unit tests"
	@echo ""
	@echo "  extension          Install dependencies and build VS Code extension"
	@echo "  extension-build    Compile the VS Code extension"
	@echo "  extension-watch    Watch and compile extension on changes"
	@echo "  extension-test     Run VS Code extension tests"
	@echo "  extension-package  Package extension as .vsix"
	@echo "  extension-install  Install extension locally in VS Code"
	@echo ""
	@echo "  debugger-test      Run debugger integration tests"
	@echo ""
	@echo "  docs               Generate HTML documentation from specs"
	@echo "  fragments          Regenerate fragments, validate specs, run fragment tests"
	@echo "  validate-specs     Check for orphaned test fragments"
	@echo ""
	@echo "  test               Run all test suites"
	@echo "  configure          Force CMake reconfiguration"
	@echo "  clean              Clean build artifacts"
	@echo "  clean-all          Clean everything including generated files"
	@echo ""

# Build Maxon runtime library
runtime: $(RUNTIME_MIR)
	@echo "Maxon runtime library ready."

# Build Windows runtime
$(RUNTIME_MIR_WINDOWS): maxon-runtime/runtime.mir maxon-runtime/runtime_windows.mir
	@mkdir -p bin
	@echo "Combining Maxon runtime library for Windows..."
	@cat maxon-runtime/runtime_windows.mir maxon-runtime/runtime.mir > bin/runtime_windows.mir

# Build Linux runtime
$(RUNTIME_MIR_LINUX): maxon-runtime/runtime.mir maxon-runtime/runtime_linux.mir
	@mkdir -p bin
	@echo "Combining Maxon runtime library for Linux..."
	@cat maxon-runtime/runtime_linux.mir maxon-runtime/runtime.mir > bin/runtime_linux.mir

# Configure CMake (only if build.ninja doesn't exist)
$(BUILD_DIR)/build.ninja: CMakeLists.txt
	@mkdir -p $(BUILD_DIR)
ifeq ($(PLATFORM),windows)
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release >/dev/null 2>&1
else
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Release >/dev/null 2>&1
endif

# Force reconfigure
configure:
	@rm -f $(BUILD_DIR)/build.ninja
	@$(MAKE) $(BUILD_DIR)/build.ninja

# Build the Maxon compiler
compiler: $(BUILD_DIR)/build.ninja runtime
	cmake --build $(BUILD_DIR) --target maxon
	@if [ bin/maxon$(EXE_EXT) -nt vscode-extension/syntaxes/maxon.tmLanguage.json ]; then echo "Generating TextMate grammar..."; ./bin/maxon$(EXE_EXT) generate-grammar vscode-extension/syntaxes/maxon.tmLanguage.json; fi

# Build the VS Code extension (install + compile)
extension: compiler
	@echo Installing dependencies and building VS Code extension...
	@cd vscode-extension && npm install && npm run compile
	@echo VS Code extension built successfully.

# Extension source files for dependency tracking
EXTENSION_SOURCES := $(shell find vscode-extension/src -name '*.ts' 2>/dev/null)

# Compile the VS Code extension only if sources changed
vscode-extension/out/.build-stamp: $(EXTENSION_SOURCES) vscode-extension/package.json vscode-extension/tsconfig.json
	@echo Compiling VS Code extension...
	@cd vscode-extension && npm install --silent && npm run compile && npm run bundle
	@touch vscode-extension/out/.build-stamp
	@echo Extension compiled.

# Compile the VS Code extension
extension-build: vscode-extension/out/.build-stamp

# Watch mode for VS Code extension development
extension-watch:
	@echo Starting watch mode for VS Code extension...
	@cd vscode-extension && npm run watch

# Run VS Code extension tests (depends on compiler; npm pretest handles extension compilation)
extension-test: compiler
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
maxon-bin/lsp/tests/build/build.ninja: maxon-bin/lsp/tests/CMakeLists.txt
	@mkdir -p maxon-bin/lsp/tests/build
ifeq ($(PLATFORM),windows)
	@cd maxon-bin/lsp/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
else
	@cd maxon-bin/lsp/tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
endif

lsp-test: compiler maxon-bin/lsp/tests/build/build.ninja
	@echo Building and running LSP tests...
	@cd maxon-bin/lsp/tests/build && cmake --build .
	@echo Running LSP tests...
	@cd maxon-bin/lsp/tests/build && ./test_lsp_server --reporter Automake

# Build and run backend test runner (standalone executable)
backend-tests/runner/build/build.ninja: backend-tests/runner/CMakeLists.txt
	@mkdir -p backend-tests/runner/build
ifeq ($(PLATFORM),windows)
	@cd backend-tests/runner/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release -DLLVM_DIR=$(LLVM_DIR_ABS)
else
	@cd backend-tests/runner/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Release -DLLVM_DIR=$(LLVM_DIR_ABS)
endif

backend-test-build: compiler backend-tests/runner/build/build.ninja
	@echo Building backend test runner...
	@cd backend-tests/runner/build && cmake --build .

# Run backend tests
backend-test: backend-test-build ffi-test-lib
	@echo Running backend tests...
	@./backend-tests/runner/build/backend-test-runner$(EXE_EXT) -v

# Generate documentation from spec files
docs: compiler
	@maxon generate-docs

# Build FFI test library
ffi-test-lib:
	@echo Building FFI test library...
ifeq ($(PLATFORM),windows)
	@cd language-tests/ffi-test-lib && "$(LLVM_DIR_ABS)/bin/clang.exe" -O2 -Wall -Wextra -Werror -shared -DFFI_TEST_LIB_EXPORTS -o ffi_test_lib.dll ffi_test_lib.c
else
	@cd language-tests/ffi-test-lib && ./build.sh release
endif
	@mkdir -p temp
	@cp language-tests/ffi-test-lib/ffi_test_lib.dll temp/ 2>/dev/null || cp language-tests/ffi-test-lib/libffi_test_lib.so temp/ 2>/dev/null || true
	@cp language-tests/ffi-test-lib/ffi_test_lib.dll backend-tests/ 2>/dev/null || cp language-tests/ffi-test-lib/libffi_test_lib.so backend-tests/ 2>/dev/null || true
	@echo FFI test library ready.

# Validate that all fragments are defined in spec files
validate-specs: compiler
	@echo Validating spec coverage...
	@bash scripts/validate-specs.sh

# Regenerate fragments, validate specs, and run fragment tests
fragments: compiler ffi-test-lib
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
test: compiler backend-test-build extension-build debugger-test-build ffi-test-lib
	@bash scripts/run-all-tests.sh

# Build debugger integration tests
debugger-tests/build/build.ninja: debugger-tests/CMakeLists.txt
	@mkdir -p debugger-tests/build
ifeq ($(PLATFORM),windows)
	@cd debugger-tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Debug
else
	@cd debugger-tests/build && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR=$(LLVM_DIR_ABS)
endif

debugger-test-build: compiler debugger-tests/build/build.ninja
	@echo Building debugger integration tests...
	@cd debugger-tests/build && cmake --build .
	@echo Debugger integration tests built successfully.

# Run debugger integration tests
debugger-test: debugger-test-build
	@echo Running debugger integration tests...
	cd debugger-tests/bin && ./debugger-test-runner$(EXE_EXT)

# Clean build artifacts
clean:
	@echo Cleaning build artifacts...
	@rm -rf $(BUILD_DIR)
	@rm -rf bin
	@rm -rf vscode-extension/out
	@rm -rf vscode-extension/node_modules
	@rm -rf maxon-bin/lsp/tests/build
	@rm -rf debugger-tests/build
	@rm -rf backend-tests/runner/build
	@rm -f debugger-tests/bin/*$(EXE_EXT)
	@echo Clean complete.

# Clean everything
clean-all: clean
	@echo All build artifacts removed.
