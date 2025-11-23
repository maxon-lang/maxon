#!/usr/bin/env bash
set -e

# Build LLVM from source on Linux
# This script builds only the components needed for Maxon compiler:
# - X86 target backend
# - Clang compiler
# - LLD linker
# - Core LLVM libraries

echo "=================================================="
echo "Building LLVM from source for Linux"
echo "=================================================="

# Read LLVM version from config file
LLVM_VERSION=$(cat llvm-config.txt | tr -d '[:space:]')
LLVM_DIR="./llvm-project"
VERSION_FILE="$LLVM_DIR/.llvm-version"
BUILD_DIR="./llvm-build"

# Check if LLVM is already built with the correct version
if [ -f "$VERSION_FILE" ]; then
    INSTALLED_VERSION=$(cat "$VERSION_FILE" | tr -d '[:space:]')
    if [ "$INSTALLED_VERSION" = "$LLVM_VERSION" ]; then
        echo "LLVM $LLVM_VERSION is already built. Skipping."
        exit 0
    else
        echo "LLVM version mismatch. Installed: $INSTALLED_VERSION, Required: $LLVM_VERSION"
        echo "Removing old LLVM installation..."
        rm -rf "$LLVM_DIR"
        rm -rf "$BUILD_DIR"
    fi
fi

# Check for required tools
for tool in git cmake ninja; do
    if ! command -v $tool &> /dev/null; then
        echo "ERROR: $tool is not installed. Please install it first."
        exit 1
    fi
done

echo ""
echo "Cloning LLVM $LLVM_VERSION..."
git clone --depth 1 --branch "llvmorg-${LLVM_VERSION}" https://github.com/llvm/llvm-project.git llvm-source
cd llvm-source

echo ""
echo "Configuring LLVM build (Release mode, X86 target only)..."
cmake -S llvm -B "../$BUILD_DIR" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="../$LLVM_DIR" \
    -DLLVM_TARGETS_TO_BUILD=X86 \
    -DLLVM_ENABLE_PROJECTS="clang;lld" \
    -DLLVM_ENABLE_RTTI=ON \
    -DLLVM_ENABLE_EH=ON \
    -DLLVM_BUILD_TESTS=OFF \
    -DLLVM_BUILD_EXAMPLES=OFF \
    -DLLVM_BUILD_BENCHMARKS=OFF \
    -DLLVM_INCLUDE_TESTS=OFF \
    -DLLVM_INCLUDE_EXAMPLES=OFF \
    -DLLVM_INCLUDE_BENCHMARKS=OFF \
    -DLLVM_INCLUDE_DOCS=OFF \
    -DLLVM_ENABLE_BINDINGS=OFF \
    -DLLVM_ENABLE_OCAMLDOC=OFF \
    -DLLVM_ENABLE_Z3_SOLVER=OFF \
    -DLLVM_OPTIMIZED_TABLEGEN=ON

# Get number of CPU cores
NPROC=$(nproc)

echo ""
echo "Building LLVM with $NPROC parallel jobs..."
echo "This will take approximately 15-20 minutes..."
cmake --build "../$BUILD_DIR" --parallel $NPROC

echo ""
echo "Installing LLVM to $LLVM_DIR..."
cmake --install "../$BUILD_DIR"

# Create version marker file
echo "$LLVM_VERSION" > "../$VERSION_FILE"

# Clean up
cd ..
echo ""
echo "Cleaning up temporary files..."
rm -rf llvm-source
rm -rf "$BUILD_DIR"

echo ""
echo "=================================================="
echo "LLVM $LLVM_VERSION built successfully!"
echo "Installation directory: $LLVM_DIR"
echo "=================================================="
