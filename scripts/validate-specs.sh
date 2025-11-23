#!/usr/bin/env bash
# Validate spec coverage - check for orphaned test fragments

set -e

# Platform detection
if [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]] || [[ "$OSTYPE" == "win32" ]]; then
    EXE_EXT=".exe"
else
    EXE_EXT=""
fi

# Maxon executable
MAXON="bin/maxon${EXE_EXT}"

# Colors
CYAN='\033[0;36m'
YELLOW='\033[1;33m'
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Extract specs first
$MAXON extract-specs >/dev/null

# Load manifest
MANIFEST_PATH="language-tests/.spec-manifest.json"
if [ ! -f "$MANIFEST_PATH" ]; then
    echo -e "${RED}ERROR: Manifest file not found at $MANIFEST_PATH${NC}"
    exit 1
fi

# Check if jq is available, otherwise use python
if command -v jq &> /dev/null; then
    # Use jq to extract fragment names
    spec_fragments=$(jq -r '.fragments | keys[]' "$MANIFEST_PATH")
else
    # Fallback to python
    spec_fragments=$(python3 -c "import json; import sys; data = json.load(open('$MANIFEST_PATH')); print('\n'.join(data['fragments'].keys()))")
fi

# Get all fragment files
all_fragments=$(ls -1 language-tests/fragments/*.test 2>/dev/null | xargs -n 1 basename)

# Find orphans
orphans=()
while IFS= read -r fragment; do
    # Skip control flow and variables fragments (legacy)
    if [[ "$fragment" == "control flow"* ]] || [[ "$fragment" == "variables"* ]]; then
        continue
    fi

    # Check if fragment is in spec manifest
    if ! echo "$spec_fragments" | grep -q "^${fragment}$"; then
        orphans+=("$fragment")
    fi
done <<< "$all_fragments"

# Count fragments
total_count=$(echo "$all_fragments" | wc -l)
spec_count=$(echo "$spec_fragments" | wc -l)
orphan_count=${#orphans[@]}

echo -e "\n${CYAN}Spec Coverage Validation Results:${NC}"
echo -e "${CYAN}=================================${NC}"
echo "Total fragments: $total_count"
echo "Defined in specs: $spec_count"
echo "Orphaned (not in specs): $orphan_count"

if [ $orphan_count -gt 0 ]; then
    echo -e "\n${YELLOW}WARNING: The following fragments are not defined in any spec file:${NC}"
    for orphan in "${orphans[@]}"; do
        echo -e "  ${YELLOW}- $orphan${NC}"
    done | sort
    echo -e "\n${YELLOW}These fragments should be converted to spec files in specs/${NC}"
else
    echo -e "\n${GREEN}All fragments are defined in spec files!${NC}"
fi

exit 0
