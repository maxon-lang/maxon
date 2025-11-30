#!/bin/bash
# validate-fragment-outputs.sh
#
# Validates that git-modified test fragments only have changes to IR sections,
# not to expected outputs (ExitCode, Stdout, Stderr, MaxoncStderr).
#
# Usage: ./scripts/validate-fragment-outputs.sh

set -e

FRAGMENTS_DIR="language-tests/fragments"
ERRORS=()

# Get list of modified .test files in the fragments directory
MODIFIED_FILES=$(git diff --name-only HEAD -- "$FRAGMENTS_DIR"/*.test 2>/dev/null || true)

if [ -z "$MODIFIED_FILES" ]; then
    echo "No modified test fragments."
    exit 0
fi

# Function to extract section 4 (expected outputs) from a test file
# Section 4 is everything after the third "---" delimiter
extract_expected_outputs() {
    local content="$1"
    echo "$content" | awk '
        BEGIN { section = 0 }
        /^---$/ { section++; next }
        section >= 3 { print }
    '
}

COUNT=0
for file in $MODIFIED_FILES; do
    # Skip if file is newly added (not in HEAD)
    if ! git show HEAD:"$file" &>/dev/null; then
        continue
    fi

    COUNT=$((COUNT + 1))

    # Get the HEAD version and working copy version
    HEAD_CONTENT=$(git show HEAD:"$file")
    WORK_CONTENT=$(cat "$file")

    # Extract expected outputs (section 4) from both versions
    HEAD_OUTPUTS=$(extract_expected_outputs "$HEAD_CONTENT")
    WORK_OUTPUTS=$(extract_expected_outputs "$WORK_CONTENT")

    # Compare the expected outputs
    if [ "$HEAD_OUTPUTS" != "$WORK_OUTPUTS" ]; then
        ERRORS+=("$file")
    fi
done

if [ ${#ERRORS[@]} -gt 0 ]; then
    echo "ERROR: Expected outputs modified in ${#ERRORS[@]} fragment(s):"
    for err in "${ERRORS[@]}"; do
        echo "  $err"
    done
    echo ""
    echo "Expected outputs should only change via spec files."
    echo "To fix: maxon extract-specs && maxon regen-fragments"
    exit 1
fi

echo "OK: $COUNT fragment(s) checked, only IR changed."
exit 0
