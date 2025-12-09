#!/bin/bash
# Compile a Maxon source file with and without optimization, emitting IR

if [ -z "$1" ]; then
    echo "Usage: $0 <filename.maxon>" >&2
    exit 1
fi

# remove previous outputs
rm -f temp/unoptimized.ir temp/unoptimized.asm temp/unoptimized.exe temp/optimized.ir temp/optimized.asm temp/optimized.exe

SOURCE="$1"

# Compile unoptimized with IR
./bin/maxon compile "$SOURCE" --emit-ir --emit-asm -vvv -o "temp/unoptimized.exe" > temp/unoptimized.compiler.log 2>&1
if [ $? -ne 0 ]; then
	echo "unopt Compilation failed"
	exit 1
fi
echo "wrote temp/unoptimized.ir"
echo "wrote temp/unoptimized.asm"
echo "wrote temp/unoptimized.exe"
echo "wrote temp/unoptimized.compiler.log"

# Run unoptimized
echo "Running unoptimized:"
./temp/unoptimized.exe
echo "unoptimized exit code: $?"

echo ""
# Compile optimized with IR
./bin/maxon compile "$SOURCE" --emit-ir --emit-asm -O -vvv -o "temp/optimized.exe" > temp/optimized.compiler.log 2>&1
if [ $? -ne 0 ]; then
	echo "optimized Compilation failed"
	exit 1
fi
echo "wrote temp/optimized.ir"
echo "wrote temp/optimized.asm"
echo "wrote temp/optimized.exe"
echo "wrote temp/optimized.compiler.log"

# Run optimized
echo "Running optimized:"
./temp/optimized.exe
echo "optimized exit code: $?"
