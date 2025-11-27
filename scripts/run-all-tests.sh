#!/usr/bin/env bash
# Run all Maxon test suites and report summary

set -e  # Stop on first error

# Platform detection
if [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]] || [[ "$OSTYPE" == "win32" ]]; then
    EXE_EXT=".exe"
else
    EXE_EXT=""
fi

# Maxon executable
MAXON="bin/maxon${EXE_EXT}"

# LLVM directory (convert to absolute path)
LLVM_DIR_ABS="$(cd "${LLVM_DIR:-./llvm-project}" && pwd)"

# Colors
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Results tracking
declare -A results

echo -e "${CYAN}============================================================${NC}"
echo -e "${CYAN}Running all test suites...${NC}"
echo -e "${CYAN}============================================================${NC}"
echo ""

# Test 1: Compiler self-tests
echo -e "${YELLOW}[1/5] Running compiler self-tests...${NC}"
echo -e "${YELLOW}------------------------------------------------------------${NC}"
$MAXON self-test
results[self-tests]=$?
echo ""

# Test 2: Backend MIR tests
echo -e "${YELLOW}[2/5] Running backend MIR tests...${NC}"
echo -e "${YELLOW}------------------------------------------------------------${NC}"
./backend-tests/runner/build/backend-test-runner${EXE_EXT} -v
results[backend-tests]=$?
echo ""

# Test 3: Language fragment tests
echo -e "${YELLOW}[3/5] Running language fragment tests...${NC}"
echo -e "${YELLOW}------------------------------------------------------------${NC}"
$MAXON extract-specs >/dev/null
$MAXON regen-fragments >/dev/null
$MAXON test-fragments
results[fragment-tests]=$?
echo ""

# Test 4: LSP C++ unit tests
echo -e "${YELLOW}[4/5] Running LSP C++ unit tests...${NC}"
echo -e "${YELLOW}------------------------------------------------------------${NC}"
mkdir -p lsp-server/tests/build
pushd lsp-server/tests/build > /dev/null
# Only configure if not already configured
if [ ! -f "CMakeCache.txt" ]; then
	if [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]] || [[ "$OSTYPE" == "win32" ]]; then
		cmake .. -G "Ninja" -DCMAKE_C_COMPILER="${LLVM_DIR_ABS}/bin/clang${EXE_EXT}" -DCMAKE_CXX_COMPILER="${LLVM_DIR_ABS}/bin/clang++${EXE_EXT}" -DCMAKE_RC_COMPILER="C:/Program Files (x86)/Windows Kits/10/bin/10.0.22621.0/x64/rc.exe" -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR="${LLVM_DIR_ABS}" 2>&1 >/dev/null
	else
		cmake .. -G "Ninja" -DCMAKE_C_COMPILER="${LLVM_DIR_ABS}/bin/clang${EXE_EXT}" -DCMAKE_CXX_COMPILER="${LLVM_DIR_ABS}/bin/clang++${EXE_EXT}" -DCMAKE_BUILD_TYPE=Debug -DMAXON_LLVM_DIR="${LLVM_DIR_ABS}" 2>&1 >/dev/null
	fi
fi
cmake --build . 2>&1 >/dev/null
ctest --output-on-failure
results[lsp-tests]=$?
popd > /dev/null
echo ""

# Test 5: VS Code extension tests
echo -e "${YELLOW}[5/5] Running VS Code extension tests...${NC}"
echo -e "${YELLOW}------------------------------------------------------------${NC}"
pushd vscode-extension > /dev/null
npm run test
results[extension-tests]=$?
popd > /dev/null
echo ""

# Summary
echo -e "${CYAN}============================================================${NC}"
echo -e "${CYAN}Test Summary:${NC}"
echo -e "${CYAN}============================================================${NC}"

failed=0
if [ "${results[self-tests]}" -ne 0 ]; then
    echo -e "${RED}[FAILED] Compiler self-tests${NC}"
    ((failed++))
else
    echo -e "${GREEN}[PASSED] Compiler self-tests${NC}"
fi

if [ "${results[backend-tests]}" -ne 0 ]; then
    echo -e "${RED}[FAILED] Backend MIR tests${NC}"
    ((failed++))
else
    echo -e "${GREEN}[PASSED] Backend MIR tests${NC}"
fi

if [ "${results[fragment-tests]}" -ne 0 ]; then
    echo -e "${RED}[FAILED] Language fragment tests${NC}"
    ((failed++))
else
    echo -e "${GREEN}[PASSED] Language fragment tests${NC}"
fi

if [ "${results[lsp-tests]}" -ne 0 ]; then
    echo -e "${RED}[FAILED] LSP C++ unit tests${NC}"
    ((failed++))
else
    echo -e "${GREEN}[PASSED] LSP C++ unit tests${NC}"
fi

if [ "${results[extension-tests]}" -ne 0 ]; then
    echo -e "${RED}[FAILED] VS Code extension tests${NC}"
    ((failed++))
else
    echo -e "${GREEN}[PASSED] VS Code extension tests${NC}"
fi

echo ""
if [ $failed -gt 0 ]; then
    echo -e "${RED}$failed test suite(s) failed${NC}"
    exit 1
else
    echo -e "${GREEN}All test suites passed!${NC}"
    exit 0
fi
