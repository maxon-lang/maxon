#!/bin/bash
# validate-fragment-outputs.sh
#
# Validates that git-modified test fragments only have changes to IR sections,
# not to expected outputs (ExitCode, Stdout, Stderr, MaxoncStderr).
# Also reports if instruction counts have increased in either IR section.
#
# Usage: ./scripts/validate-fragment-outputs.sh

set -e

FRAGMENTS_DIR="language-tests/fragments"
ERRORS=()
INCREASED_INSTRUCTIONS=()

# Get list of modified .test files in the fragments directory
# Use git diff without glob to avoid "Argument list too long" on Windows
MODIFIED_FILES=$(git diff --name-only HEAD -- "$FRAGMENTS_DIR" 2>/dev/null | grep '\.test$' || true)

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

# Function to extract instruction counts from IR sections
# Returns two numbers: optimized_count unoptimized_count
extract_instruction_counts() {
    local content="$1"
    echo "$content" | awk '
        BEGIN { section = 0; opt_count = 0; unopt_count = 0 }
        /^---$/ { section++; next }
        section == 1 && /^; Instructions: / { opt_count = $3 }
        section == 2 && /^; Instructions: / { unopt_count = $3 }
        END { print opt_count, unopt_count }
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

    # Check instruction counts
    read HEAD_OPT HEAD_UNOPT <<< "$(extract_instruction_counts "$HEAD_CONTENT")"
    read WORK_OPT WORK_UNOPT <<< "$(extract_instruction_counts "$WORK_CONTENT")"

    # Report if instruction counts increased
    if [ "$WORK_OPT" -gt "$HEAD_OPT" ] 2>/dev/null; then
        echo "WARNING: $file: optimized IR increased from $HEAD_OPT to $WORK_OPT instructions"
        INCREASED_INSTRUCTIONS+=("$file")
    fi
    # if [ "$WORK_UNOPT" -gt "$HEAD_UNOPT" ] 2>/dev/null; then
    #     echo "WARNING: $file: unoptimized IR increased from $HEAD_UNOPT to $WORK_UNOPT instructions"
    #     INCREASED_INSTRUCTIONS+=("$file")
    # fi
done

if [ ${#ERRORS[@]} -gt 0 ]; then
    echo ""
    echo "ERROR: Expected outputs modified in ${#ERRORS[@]} fragment(s):"
    for err in "${ERRORS[@]}"; do
        echo "  $err"
    done
    exit 1
fi

echo "OK: $COUNT fragment(s) checked, only IR changed."
exit 0
