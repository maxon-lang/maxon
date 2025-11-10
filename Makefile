# Makefile for Maxon Compiler
# This Makefile uses CMake to build the compiler

# Configuration
BUILD_DIR = build
BIN_DIR = $(BUILD_DIR)/bin
COMPILER_NAME = maxonc.exe
CONFIG = Release
LLVM_BUILD_DIR = llvm-build
LLVM_BUILD_TEMP = llvm-build-temp
LLVM_SOURCE = llvm-project/llvm
LLVM_DIR = $(LLVM_BUILD_DIR)/lib/cmake/llvm

# Default target
.PHONY: all
all: build

# Build LLVM once
.PHONY: llvm
llvm:
	@if not exist "$(LLVM_BUILD_DIR)\lib\cmake\llvm\LLVMConfig.cmake" ( \
		echo Building LLVM from submodule (this will take 30-60 minutes)... && \
		if not exist "$(LLVM_SOURCE)\CMakeLists.txt" ( \
			echo Error: LLVM submodule not found. Run: git submodule update --init --recursive && \
			exit 1 \
		) && \
		if not exist $(LLVM_BUILD_TEMP) mkdir $(LLVM_BUILD_TEMP) && \
		cd $(LLVM_BUILD_TEMP) && \
		cmake -G "Visual Studio 17 2022" -A x64 \
			-DCMAKE_BUILD_TYPE=$(CONFIG) \
			-DCMAKE_INSTALL_PREFIX=..\$(LLVM_BUILD_DIR) \
			-DLLVM_TARGETS_TO_BUILD=X86 \
			-DLLVM_ENABLE_PROJECTS= \
			-DLLVM_ENABLE_RTTI=ON \
			-DLLVM_ENABLE_EH=ON \
			-DLLVM_BUILD_EXAMPLES=OFF \
			-DLLVM_BUILD_TESTS=OFF \
			-DLLVM_INCLUDE_EXAMPLES=OFF \
			-DLLVM_INCLUDE_TESTS=OFF \
			-DLLVM_INCLUDE_DOCS=OFF \
			-DLLVM_ENABLE_ASSERTIONS=OFF \
			..\$(LLVM_SOURCE) && \
		echo Building LLVM... && \
		cmake --build . --config $(CONFIG) --parallel && \
		echo Installing LLVM... && \
		cmake --install . --config $(CONFIG) && \
		cd .. && \
		echo LLVM build complete! \
	) else ( \
		echo LLVM already built. \
	)

# Create build directory and configure with CMake
.PHONY: configure
configure: llvm
	@if not exist $(BUILD_DIR) mkdir $(BUILD_DIR)
	@cd $(BUILD_DIR) && cmake .. -G "Visual Studio 17 2022"

# Build the compiler
.PHONY: build
build: configure
	@cd $(BUILD_DIR) && cmake --build . --config $(CONFIG) --target maxonc -- /v:minimal /nologo
	@echo.
	@echo Build complete! Compiler located at $(BIN_DIR)/$(CONFIG)/$(COMPILER_NAME)

# Clean build artifacts (not LLVM)
.PHONY: clean
clean:
	@if exist $(BUILD_DIR) rmdir /s /q $(BUILD_DIR)
	@echo Build directory cleaned.

# Clean everything including LLVM
.PHONY: clean-all
clean-all:
	@if exist $(BUILD_DIR) rmdir /s /q $(BUILD_DIR)
	@if exist $(LLVM_BUILD_DIR) rmdir /s /q $(LLVM_BUILD_DIR)
	@if exist llvm-build-temp rmdir /s /q llvm-build-temp
	@echo All build directories cleaned.

# Rebuild from scratch
.PHONY: rebuild
rebuild: clean build


# Run the compiler on sample.maxon
.PHONY: test
test: build
	@echo Compiling sample.maxon...
	@$(BIN_DIR)/$(CONFIG)/$(COMPILER_NAME) sample.maxon -o output.o
	@echo.
	@echo Compilation successful! Generated output.o

# Show LLVM IR for sample.maxon
.PHONY: test-ir
test-ir: build
	@echo Generating LLVM IR for sample.maxon...
	@$(BIN_DIR)/$(CONFIG)/$(COMPILER_NAME) sample.maxon --emit-llvm

# Install compiler to system (optional)
.PHONY: install
install: build
	@cd $(BUILD_DIR) && cmake --install . --config $(CONFIG)

# Help target
.PHONY: help
help:
	@echo Maxon Compiler Makefile
	@echo.
	@echo Available targets:
	@echo   make              - Build the compiler (default)
	@echo   make llvm         - Build LLVM once (takes 30-60 minutes)
	@echo   make configure    - Configure CMake build
	@echo   make build        - Build the compiler
	@echo   make clean        - Remove build artifacts (keeps LLVM)
	@echo   make clean-all    - Remove all build artifacts including LLVM
	@echo   make rebuild      - Clean and rebuild
	@echo   make test         - Build and run compiler on sample.maxon
	@echo   make test-ir      - Build and show LLVM IR for sample.maxon

	@echo   make install      - Install compiler to system
	@echo   make help         - Show this help message
	@echo.
	@echo Configuration:
	@echo   BUILD_DIR = $(BUILD_DIR)
	@echo   CONFIG = $(CONFIG)
	@echo   LLVM_DIR = $(LLVM_DIR)
	@echo.
	@echo Note: Modify LLVM_DIR in the Makefile to match your LLVM installation path.
