#!/bin/bash
# Build script for FFI Test Library (Linux/macOS)
# Usage: ./build.sh [debug|release]

set -e

CONFIG=${1:-debug}

echo "Building FFI Test Library ($CONFIG)..."

if [ "$CONFIG" = "debug" ]; then
    CFLAGS="-g -O0 -Wall -Wextra"
else
    CFLAGS="-O2 -Wall -Wextra"
fi

# Detect OS
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS
    SHARED_EXT="dylib"
    SHARED_FLAGS="-dynamiclib"
else
    # Linux
    SHARED_EXT="so"
    SHARED_FLAGS="-shared -fPIC"
fi

# Try gcc first, then clang
if command -v gcc &> /dev/null; then
    CC=gcc
elif command -v clang &> /dev/null; then
    CC=clang
else
    echo "ERROR: No C compiler found! Please install gcc or clang."
    exit 1
fi

echo "Using $CC compiler..."

$CC $CFLAGS $SHARED_FLAGS -DFFI_TEST_LIB_EXPORTS -o libffi_test_lib.$SHARED_EXT ffi_test_lib.c

echo "Built: libffi_test_lib.$SHARED_EXT"
echo "Build complete!"
