#!/bin/bash
# Compile a Maxon source file with and without optimization, emitting IR

if [ -z "$1" ]; then
    echo "Usage: $0 <filename.maxon>" >&2
    exit 1
fi

# remove previous outputs
rm -f temp/unoptimized.ir temp/unoptimized.exe temp/optimized.ir temp/optimized.exe

SOURCE="$1"

# Compile unoptimized with IR
./bin/maxon compile "$SOURCE" --emit-ir -o "temp/unoptimized.exe"
if [ $? -ne 0 ]; then
	echo "unopt Compilation failed"
	exit 1
fi
echo "wrote temp/unoptimized.ir"
echo "wrote temp/unoptimized.exe"

# Run unoptimized
echo "Running unoptimized:"
./temp/unoptimized.exe
echo "unoptimized exit code: $?"

echo ""
# Compile optimized with IR
./bin/maxon compile "$SOURCE" --emit-ir -O -o "temp/optimized.exe"
if [ $? -ne 0 ]; then
	echo "optimized Compilation failed"
	exit 1
fi
echo "wrote temp/optimized.ir"
echo "wrote temp/optimized.exe"

# Run optimized
echo "Running optimized:"
./temp/optimized.exe
echo "optimized exit code: $?"
