# Maxon Compiler and LSP Makefile
# Windows-compatible Makefile using Clang and Ninja

BUILD_DIR = build
CMAKE_GENERATOR = "Ninja"
CC = "C:/Program Files/LLVM/bin/clang.exe"
CXX = "C:/Program Files/LLVM/bin/clang++.exe"
RC = "C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe"
LLC = "C:/Users/Eric/Dev/llvm-project/build/Release/bin/llc.exe"
LSP_SERVER_BIN = bin/maxon-lsp-server.exe
LSP_SERVER_BACKUP = $(LSP_SERVER_BIN).old

RUNTIME_LL = maxon-runtime/runtime.ll
RUNTIME_OBJ = maxon-runtime/runtime.obj

.PHONY: all clean compiler lsp lsp-server extension extension-build extension-watch extension-test extension-package extension-install help configure lsp-test docs test runtime fragments

# Default target - build everything
all: compiler lsp-server extension-install
	@echo All components built successfully.

help:
	@echo "Maxon Project Build Targets:"
	@echo "  all              - Build compiler, runtime library, and LSP (default)"
	@echo "  configure        - Configure CMake build"
	@echo "  runtime          - Build Maxon runtime library"
	@echo "  compiler         - Build only the Maxon compiler"
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
	@echo "  test             - Run all test suites (compiler self-tests, fragment tests, LSP tests, extension tests)"
	@echo "  clean            - Clean all build artifacts"
	@echo "  help             - Show this help message"

# Build Maxon runtime library
runtime: bin/runtime.obj

bin/runtime.obj: $(RUNTIME_LL)
	@echo "Building Maxon runtime library..."
	@powershell -Command "if (-not (Test-Path 'bin')) { New-Item -ItemType Directory -Path 'bin' | Out-Null }"
	@$(LLC) -filetype=obj -o bin/runtime.obj $(RUNTIME_LL) >/dev/null 2>&1

# Configure CMake
configure:
	@powershell -Command "if (-not (Test-Path '$(BUILD_DIR)')) { New-Item -ItemType Directory -Path '$(BUILD_DIR)' | Out-Null }"
	@cd $(BUILD_DIR) && cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER=$(CC) -DCMAKE_CXX_COMPILER=$(CXX) -DCMAKE_RC_COMPILER=$(RC) -DCMAKE_BUILD_TYPE=Release >/dev/null 2>&1

# Build the Maxon compiler (depends on runtime)
compiler: configure runtime
	cmake --build $(BUILD_DIR) --target maxon
	cmake --build $(BUILD_DIR) --target grammar_generator
	@if [ bin/grammar_generator.exe -nt vscode-extension/syntaxes/maxon.tmLanguage.json ]; then echo "Generating TextMate grammar..."; ./bin/grammar_generator.exe vscode-extension/syntaxes/maxon.tmLanguage.json; fi

# Build both LSP server and extension
lsp: lsp-server extension-install

# Build the C++ LSP server (depends on compiler sources)
lsp-server: compiler
	cmake --build $(BUILD_DIR) --target maxon-lsp-server

# Build the VS Code extension (install + compile)
extension: lsp-server
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
	@echo Cleaning up temporary test files...
	@powershell -Command "if (Test-Path 'temp') { Remove-Item -Path 'temp\test_*.maxon' -Force -ErrorAction SilentlyContinue }"
	@echo Extension tests complete.

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
	@powershell -Command "if (!(Test-Path 'lsp-server\tests\build')) { New-Item -ItemType Directory -Path 'lsp-server\tests\build' | Out-Null }"
	@powershell -Command "cd lsp-server\tests\build; cmake .. -G $(CMAKE_GENERATOR) -DCMAKE_C_COMPILER='$(CC)' -DCMAKE_CXX_COMPILER='$(CXX)' -DCMAKE_BUILD_TYPE=Debug"
	@powershell -Command "cd lsp-server\tests\build; cmake --build ."
	@echo Running LSP tests...
	@powershell -Command "cd lsp-server\tests\build; ctest --output-on-failure"

# Generate documentation from spec files
docs: compiler
	@echo Generating documentation from specs...
	@powershell -Command "cd maxon-docs; dotnet run"
	@echo Regenerating documentation fragments...
	@powershell -Command "maxon regen-fragments"
	@echo Documentation generated in maxon-docs/Output/

# Validate that all fragments are defined in spec files
validate-specs: compiler
	@echo Validating spec coverage...
	@powershell -ExecutionPolicy Bypass -File scripts/validate-specs.ps1

# Regenerate fragments, validate specs, and run fragment tests
fragments: compiler
	@echo Extracting test fragments from specs...
	@powershell -Command "maxon extract-specs"
	@echo Validating spec coverage...
	@powershell -ExecutionPolicy Bypass -File scripts/validate-specs.ps1
	@echo Regenerating test fragments...
	@powershell -Command "maxon regen-fragments"
	@echo Running fragment tests...
	@powershell -Command "maxon test-fragments"

# Run all test suites
test: compiler lsp-server extension-build
	@powershell -ExecutionPolicy Bypass -File scripts/run-all-tests.ps1

# Clean build artifacts
clean:
	@echo Cleaning build artifacts...
	@powershell -Command "if (Test-Path '$(BUILD_DIR)') { Remove-Item -Recurse -Force '$(BUILD_DIR)' }"
	@powershell -Command "if (Test-Path 'bin') { Remove-Item -Recurse -Force 'bin' }"
	@powershell -Command "if (Test-Path '$(RUNTIME_OBJ)') { Remove-Item -Force '$(RUNTIME_OBJ)' }"
	@powershell -Command "if (Test-Path 'vscode-extension/out') { Remove-Item -Recurse -Force 'vscode-extension/out' }"
	@powershell -Command "if (Test-Path 'vscode-extension/node_modules') { Remove-Item -Recurse -Force 'vscode-extension/node_modules' }"
	@powershell -Command "if (Test-Path 'lsp-server/tests/build') { Remove-Item -Recurse -Force 'lsp-server/tests/build' }"
	@echo Clean complete.
