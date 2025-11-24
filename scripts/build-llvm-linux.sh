#!/usr/bin/env bash
set -e

# Build LLVM from source on Linux
# This script builds only the components needed for Maxon compiler:
# - X86 target backend
# - Clang compiler
# - LLD linker
# - LLDB debugger
# - Core LLVM libraries

echo "=================================================="
echo "Building LLVM from source for Linux"
echo "=================================================="

# Read LLVM version from config file
LLVM_VERSION=$(cat llvm-config.txt | tr -d '[:space:]')
LLVM_DIR="./llvm-project"
VERSION_FILE="$LLVM_DIR/.llvm-version"
BUILD_DIR="./llvm-build"
SOURCE_DIR="./llvm-source"

# Check for required tools
for tool in git cmake ninja; do
    if ! command -v $tool &> /dev/null; then
        echo "ERROR: $tool is not installed. Please install it first."
        exit 1
    fi
done

# Clone LLVM source if not present
if [ ! -d "$SOURCE_DIR" ]; then
    echo ""
    echo "Cloning LLVM $LLVM_VERSION..."
    git clone --depth 1 --branch "llvmorg-${LLVM_VERSION}" https://github.com/llvm/llvm-project.git "$SOURCE_DIR"
else
    # Check if the source directory has the correct version
    cd "$SOURCE_DIR"
    CURRENT_BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
    EXPECTED_TAG="llvmorg-${LLVM_VERSION}"
    
    if [ "$CURRENT_BRANCH" != "$EXPECTED_TAG" ]; then
        echo ""
        echo "Source directory exists but version mismatch detected."
        echo "Fetching and checking out $EXPECTED_TAG..."
        git fetch --depth 1 origin "refs/tags/$EXPECTED_TAG:refs/tags/$EXPECTED_TAG" 2>/dev/null || true
        git checkout "$EXPECTED_TAG" 2>/dev/null || {
            echo "Failed to checkout correct version. Removing source and cloning fresh..."
            cd ..
            rm -rf "$SOURCE_DIR"
            git clone --depth 1 --branch "$EXPECTED_TAG" https://github.com/llvm/llvm-project.git "$SOURCE_DIR"
            cd "$SOURCE_DIR"
        }
    else
        echo ""
        echo "Source directory already at correct version: $EXPECTED_TAG"
    fi
    cd ..
fi

cd "$SOURCE_DIR"

echo ""
echo "Configuring LLVM build (Release mode, X86 target only)..."
cmake -S llvm -B "../$BUILD_DIR" -G Ninja \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="../$LLVM_DIR" \
    -DLLVM_TARGETS_TO_BUILD=X86 \
    -DLLVM_ENABLE_PROJECTS="clang;lld;lldb" \
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

# Get number of CPU cores and limit to avoid memory issues
NPROC=$(nproc)
PARALLEL_JOBS=$(( NPROC / 2 ))
if [ $PARALLEL_JOBS -lt 1 ]; then
    PARALLEL_JOBS=1
fi

echo ""
echo "Building LLVM with $PARALLEL_JOBS parallel jobs (of $NPROC available cores)..."
echo "This will take approximately 15-20 minutes..."
cmake --build "../$BUILD_DIR" --parallel $PARALLEL_JOBS

echo ""
echo "Installing LLVM to $LLVM_DIR..."
cmake --install "../$BUILD_DIR"

# Create version marker file
echo "$LLVM_VERSION" > "../$VERSION_FILE"

cd ..

echo ""
echo "Verifying LLVM installation..."
REQUIRED_TOOLS=("clang" "llc" "lld" "llvm-objdump")
MISSING_TOOLS=()

for tool in "${REQUIRED_TOOLS[@]}"; do
    if [ ! -f "$LLVM_DIR/bin/$tool" ]; then
        MISSING_TOOLS+=("$tool")
    fi
done

if [ ${#MISSING_TOOLS[@]} -ne 0 ]; then
    echo "Error: The following required tools are missing:"
    for tool in "${MISSING_TOOLS[@]}"; do
        echo "  - $tool"
    done
    exit 1
fi

echo "All required LLVM tools found!"

echo ""
echo "=================================================="
echo "LLVM $LLVM_VERSION built successfully!"
echo "Installation directory: $LLVM_DIR"
echo "=================================================="
