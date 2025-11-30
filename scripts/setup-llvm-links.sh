#!/bin/bash
# Setup symbolic links for llvm-project and llvm-source directories
# Used for git worktrees to share these large directories with the main repo

set -e

MAIN_REPO_PATH="${1:-../maxon}"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Create a symlink if the target doesn't already exist
create_link() {
    local name="$1"
    local source="${MAIN_REPO_PATH}/${name}"

    # Check if already exists (as file, directory, or symlink)
    if [[ -e "$name" || -L "$name" ]]; then
        if [[ -L "$name" ]]; then
            echo -e "${GREEN}✓${NC} $name already linked"
        else
            echo -e "${YELLOW}⚠${NC} $name already exists (not a symlink, skipping)"
        fi
        return 0
    fi

    # Check if source exists
    if [[ ! -d "$source" ]]; then
        echo -e "${RED}✗${NC} Source not found: $source"
        return 1
    fi

    # Create the symlink
    if ln -s "$source" "$name"; then
        echo -e "${GREEN}✓${NC} Created symlink: $name -> $source"
    else
        echo -e "${RED}✗${NC} Failed to create symlink: $name"
        return 1
    fi
}

main() {
    echo "Setting up LLVM directory links..."
    echo "Main repo path: $MAIN_REPO_PATH"
    echo ""

    # Track if any links failed
    local failed=0

    # Create links for both directories
    create_link "llvm-project" || failed=1
    create_link "llvm-source" || failed=1

    # Renormalize line endings in language-tests/fragments to fix git index issues in worktrees
    if [[ -d "language-tests/fragments" ]]; then
        echo ""
        echo "Renormalizing line endings..."
        git add --renormalize . 2>/dev/null && \
            echo -e "${GREEN}✓${NC} Line endings renormalized" || \
            echo -e "${YELLOW}⚠${NC} Could not renormalize (may not be needed)"
    fi

    echo ""
    if [[ $failed -eq 0 ]]; then
        echo -e "${GREEN}Done!${NC}"
    else
        echo -e "${YELLOW}Completed with warnings.${NC}"
    fi

    exit $failed
}

main
