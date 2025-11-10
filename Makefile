# Maxon Compiler Makefile# Makefile for Maxon Compiler# Makefile for Maxon Compiler# Makefile for Maxon Compiler



all: build



build:BUILD_DIR := build# This Makefile uses CMake to build the compiler

	cmake -B build -G "Visual Studio 17 2022" -A x64

	cmake --build build --config Release --target maxonc -- /v:minimal /nologoBIN_DIR := $(BUILD_DIR)/bin/Release



test: buildCOMPILER := $(BIN_DIR)/maxonc.exe# Configuration

	build/bin/Release/maxonc.exe sample.maxon -o output.exe

	./output.exeCONFIG := Release



clean:LLVM_BUILD_DIR := llvm-buildBUILD_DIR := build# Configuration

	rm -rf build output.exe temp.o

LLVM_BUILD_TEMP := llvm-build-temp

.PHONY: all build test clean

LLVM_SOURCE := llvm-project/llvmBIN_DIR := $(BUILD_DIR)/bin/ReleaseBUILD_DIR = build

LLVM_CONFIG := $(LLVM_BUILD_DIR)/lib/cmake/llvm/LLVMConfig.cmake

COMPILER := $(BIN_DIR)/maxonc.exeBIN_DIR = $(BUILD_DIR)/bin

.PHONY: all

all: buildCONFIG := ReleaseCOMPILER_NAME = maxonc.exe



$(LLVM_CONFIG):LLVM_BUILD_DIR := llvm-buildCONFIG = Release

	@echo "Building LLVM from submodule (this will take 30-60 minutes)..."

	@test -f "$(LLVM_SOURCE)/CMakeLists.txt" || (echo "Error: LLVM submodule not found." && exit 1)LLVM_BUILD_TEMP := llvm-build-tempLLVM_BUILD_DIR = llvm-build

	@mkdir -p $(LLVM_BUILD_TEMP)

	cd $(LLVM_BUILD_TEMP) && cmake -G "Visual Studio 17 2022" -A x64 \LLVM_SOURCE := llvm-project/llvmLLVM_BUILD_TEMP = llvm-build-temp

		-DCMAKE_BUILD_TYPE=$(CONFIG) \

		-DCMAKE_INSTALL_PREFIX=../$(LLVM_BUILD_DIR) \LLVM_SOURCE = llvm-project/llvm

		-DLLVM_TARGETS_TO_BUILD=X86 \

		-DLLVM_ENABLE_PROJECTS= \# Default targetLLVM_DIR = $(LLVM_BUILD_DIR)/lib/cmake/llvm

		-DLLVM_ENABLE_RTTI=ON \

		-DLLVM_ENABLE_EH=ON \.PHONY: all

		-DLLVM_BUILD_EXAMPLES=OFF \

		-DLLVM_BUILD_TESTS=OFF \all: build# Default target

		-DLLVM_INCLUDE_EXAMPLES=OFF \

		-DLLVM_INCLUDE_TESTS=OFF \.PHONY: all

		-DLLVM_INCLUDE_DOCS=OFF \

		-DLLVM_ENABLE_ASSERTIONS=OFF \# Check if LLVM is builtall: build

		../$(LLVM_SOURCE)

	@echo "Building LLVM..."LLVM_CONFIG := $(LLVM_BUILD_DIR)/lib/cmake/llvm/LLVMConfig.cmake

	cd $(LLVM_BUILD_TEMP) && cmake --build . --config $(CONFIG) --parallel

	@echo "Installing LLVM..."# Build LLVM once

	cd $(LLVM_BUILD_TEMP) && cmake --install . --config $(CONFIG)

	@echo "LLVM build complete!"$(LLVM_CONFIG):.PHONY: llvm



.PHONY: llvm	@echo "Building LLVM from submodule (this will take 30-60 minutes)..."llvm:

llvm: $(LLVM_CONFIG)

	@if [ ! -f "$(LLVM_SOURCE)/CMakeLists.txt" ]; then \	@if not exist "$(LLVM_BUILD_DIR)\lib\cmake\llvm\LLVMConfig.cmake" ( \

.PHONY: configure

configure: $(LLVM_CONFIG)		echo "Error: LLVM submodule not found. Run: git submodule update --init --recursive"; \		echo Building LLVM from submodule (this will take 30-60 minutes)... && \

	@mkdir -p $(BUILD_DIR)

	cd $(BUILD_DIR) && cmake -G "Visual Studio 17 2022" -A x64 ..		exit 1; \		if not exist "$(LLVM_SOURCE)\CMakeLists.txt" ( \



.PHONY: build	fi			echo Error: LLVM submodule not found. Run: git submodule update --init --recursive && \

build: configure

	cd $(BUILD_DIR) && cmake --build . --config $(CONFIG) --target maxonc -- /v:minimal /nologo	@mkdir -p $(LLVM_BUILD_TEMP)			exit 1 \

	@echo ""

	@echo "Build complete! Compiler at $(COMPILER)"	cd $(LLVM_BUILD_TEMP) && \		) && \



.PHONY: clean	cmake -G "Visual Studio 17 2022" -A x64 \		if not exist $(LLVM_BUILD_TEMP) mkdir $(LLVM_BUILD_TEMP) && \

clean:

	rm -rf $(BUILD_DIR)		-DCMAKE_BUILD_TYPE=$(CONFIG) \		cd $(LLVM_BUILD_TEMP) && \



.PHONY: clean-all		-DCMAKE_INSTALL_PREFIX=../$(LLVM_BUILD_DIR) \		cmake -G "Visual Studio 17 2022" -A x64 \

clean-all:

	rm -rf $(BUILD_DIR) $(LLVM_BUILD_DIR) $(LLVM_BUILD_TEMP)		-DLLVM_TARGETS_TO_BUILD=X86 \			-DCMAKE_BUILD_TYPE=$(CONFIG) \



.PHONY: rebuild		-DLLVM_ENABLE_PROJECTS= \			-DCMAKE_INSTALL_PREFIX=..\$(LLVM_BUILD_DIR) \

rebuild: clean build

		-DLLVM_ENABLE_RTTI=ON \			-DLLVM_TARGETS_TO_BUILD=X86 \

.PHONY: test

test: build		-DLLVM_ENABLE_EH=ON \			-DLLVM_ENABLE_PROJECTS= \

	@echo "Compiling and linking sample.maxon..."

	$(COMPILER) sample.maxon -o output.exe		-DLLVM_BUILD_EXAMPLES=OFF \			-DLLVM_ENABLE_RTTI=ON \

	@echo ""

	@echo "Running output.exe..."		-DLLVM_BUILD_TESTS=OFF \			-DLLVM_ENABLE_EH=ON \

	./output.exe

		-DLLVM_INCLUDE_EXAMPLES=OFF \			-DLLVM_BUILD_EXAMPLES=OFF \

.PHONY: test-ir

test-ir: build		-DLLVM_INCLUDE_TESTS=OFF \			-DLLVM_BUILD_TESTS=OFF \

	@echo "Generating LLVM IR for sample.maxon..."

	$(COMPILER) sample.maxon --emit-llvm		-DLLVM_INCLUDE_DOCS=OFF \			-DLLVM_INCLUDE_EXAMPLES=OFF \



.PHONY: help		-DLLVM_ENABLE_ASSERTIONS=OFF \			-DLLVM_INCLUDE_TESTS=OFF \

help:

	@echo "Maxon Compiler Makefile"		../$(LLVM_SOURCE)			-DLLVM_INCLUDE_DOCS=OFF \

	@echo ""

	@echo "Available targets:"	@echo "Building LLVM..."			-DLLVM_ENABLE_ASSERTIONS=OFF \

	@echo "  make         - Build the compiler"

	@echo "  make test    - Build, compile and run sample.maxon"	cd $(LLVM_BUILD_TEMP) && cmake --build . --config $(CONFIG) --parallel			..\$(LLVM_SOURCE) && \

	@echo "  make clean   - Remove build directory"

	@echo "  make help    - Show this message"	@echo "Installing LLVM..."		echo Building LLVM... && \


	cd $(LLVM_BUILD_TEMP) && cmake --install . --config $(CONFIG)		cmake --build . --config $(CONFIG) --parallel && \

	@echo "LLVM build complete!"		echo Installing LLVM... && \

		cmake --install . --config $(CONFIG) && \

.PHONY: llvm		cd .. && \

llvm: $(LLVM_CONFIG)		echo LLVM build complete! \

	) else ( \

# Configure CMake		echo LLVM already built. \

.PHONY: configure	)

configure: $(LLVM_CONFIG)

	@mkdir -p $(BUILD_DIR)# Create build directory and configure with CMake

	cd $(BUILD_DIR) && cmake -G "Visual Studio 17 2022" -A x64 ...PHONY: configure

configure: llvm

# Build the compiler	@if not exist $(BUILD_DIR) mkdir $(BUILD_DIR)

.PHONY: build	@cd $(BUILD_DIR) && cmake .. -G "Visual Studio 17 2022"

build: configure

	cd $(BUILD_DIR) && cmake --build . --config $(CONFIG) --target maxonc -- /v:minimal /nologo# Build the compiler

	@echo "".PHONY: build

	@echo "Build complete! Compiler located at $(COMPILER)"build: configure

	@cd $(BUILD_DIR) && cmake --build . --config $(CONFIG) --target maxonc -- /v:minimal /nologo

# Clean build artifacts (not LLVM)	@echo.

.PHONY: clean	@echo Build complete! Compiler located at $(BIN_DIR)/$(CONFIG)/$(COMPILER_NAME)

clean:

	rm -rf $(BUILD_DIR)# Clean build artifacts (not LLVM)

	@echo "Build directory cleaned.".PHONY: clean

clean:

# Clean everything including LLVM	@if exist $(BUILD_DIR) rmdir /s /q $(BUILD_DIR)

.PHONY: clean-all	@echo Build directory cleaned.

clean-all:

	rm -rf $(BUILD_DIR) $(LLVM_BUILD_DIR) $(LLVM_BUILD_TEMP)# Clean everything including LLVM

	@echo "All build directories cleaned.".PHONY: clean-all

clean-all:

# Rebuild from scratch	@if exist $(BUILD_DIR) rmdir /s /q $(BUILD_DIR)

.PHONY: rebuild	@if exist $(LLVM_BUILD_DIR) rmdir /s /q $(LLVM_BUILD_DIR)

rebuild: clean build	@if exist llvm-build-temp rmdir /s /q llvm-build-temp

	@echo All build directories cleaned.

# Run the compiler on sample.maxon

.PHONY: test# Rebuild from scratch

test: build.PHONY: rebuild

	@echo "Compiling and linking sample.maxon..."rebuild: clean build

	$(COMPILER) sample.maxon -o output.exe

	@echo ""

	@echo "Running output.exe..."# Run the compiler on sample.maxon

	./output.exe.PHONY: test

test: build

# Show LLVM IR for sample.maxon	@echo Compiling and linking sample.maxon...

.PHONY: test-ir	@$(BIN_DIR)/$(CONFIG)/$(COMPILER_NAME) sample.maxon -o output.exe

test-ir: build	@echo.

	@echo "Generating LLVM IR for sample.maxon..."	@echo Running output.exe...

	$(COMPILER) sample.maxon --emit-llvm	@output.exe



# Install compiler to system (optional)# Show LLVM IR for sample.maxon

.PHONY: install.PHONY: test-ir

install: buildtest-ir: build

	cd $(BUILD_DIR) && cmake --install . --config $(CONFIG)	@echo Generating LLVM IR for sample.maxon...

	@$(BIN_DIR)/$(CONFIG)/$(COMPILER_NAME) sample.maxon --emit-llvm

# Help target

.PHONY: help# Install compiler to system (optional)

help:.PHONY: install

	@echo "Maxon Compiler Makefile"install: build

	@echo ""	@cd $(BUILD_DIR) && cmake --install . --config $(CONFIG)

	@echo "Available targets:"

	@echo "  make              - Build the compiler (default)"# Help target

	@echo "  make llvm         - Build LLVM once (takes 30-60 minutes)".PHONY: help

	@echo "  make configure    - Configure CMake build"help:

	@echo "  make build        - Build the compiler"	@echo Maxon Compiler Makefile

	@echo "  make clean        - Remove build artifacts (keeps LLVM)"	@echo.

	@echo "  make clean-all    - Remove all build artifacts including LLVM"	@echo Available targets:

	@echo "  make rebuild      - Clean and rebuild"	@echo   make              - Build the compiler (default)

	@echo "  make test         - Build, compile sample.maxon, and run it"	@echo   make llvm         - Build LLVM once (takes 30-60 minutes)

	@echo "  make test-ir      - Build and show LLVM IR for sample.maxon"	@echo   make configure    - Configure CMake build

	@echo "  make install      - Install compiler to system"	@echo   make build        - Build the compiler

	@echo "  make help         - Show this help message"	@echo   make clean        - Remove build artifacts (keeps LLVM)

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
