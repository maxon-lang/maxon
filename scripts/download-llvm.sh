#!/usr/bin/env bash
set -e

# Read LLVM version from config file
LLVM_VERSION=$(cat llvm-config.txt | tr -d '[:space:]')
LLVM_DIR="./llvm-project"
VERSION_FILE="$LLVM_DIR/.llvm-version"

# Check if LLVM is already downloaded with the correct version
if [ -f "$VERSION_FILE" ]; then
    INSTALLED_VERSION=$(cat "$VERSION_FILE" | tr -d '[:space:]')
    if [ "$INSTALLED_VERSION" = "$LLVM_VERSION" ]; then
        echo "LLVM $LLVM_VERSION is already downloaded. Skipping."
        exit 0
    else
        echo "LLVM version mismatch. Installed: $INSTALLED_VERSION, Required: $LLVM_VERSION"
        echo "Removing old LLVM installation..."
        rm -rf "$LLVM_DIR"
    fi
fi

# Detect platform
if [[ "$OSTYPE" == "msys" ]] || [[ "$OSTYPE" == "cygwin" ]] || [[ "$OSTYPE" == "win32" ]]; then
    PLATFORM="windows"
elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
    echo "On Linux, LLVM should be installed via apt packages:"
    echo "  sudo apt-get install llvm-21 llvm-21-dev clang-21 lld-21 liblld-21-dev libpolly-21-dev"
    echo ""
    echo "See README or docs for setup instructions."
    exit 0
else
    echo "Unsupported platform: $OSTYPE"
    exit 1
fi

echo "Downloading LLVM $LLVM_VERSION for $PLATFORM..."

# Create temporary directory
TEMP_DIR=$(mktemp -d)
trap "rm -rf $TEMP_DIR" EXIT

if [ "$PLATFORM" = "windows" ]; then
    # Download Windows pre-built LLVM (full archive with libraries and headers)
    LLVM_URL="https://github.com/llvm/llvm-project/releases/download/llvmorg-${LLVM_VERSION}/clang+llvm-${LLVM_VERSION}-x86_64-pc-windows-msvc.tar.xz"
    DOWNLOAD_FILE="$TEMP_DIR/llvm.tar.xz"

    echo "Downloading from: $LLVM_URL"
    curl -L -o "$DOWNLOAD_FILE" "$LLVM_URL"

    echo "Extracting LLVM..."
    mkdir -p "$LLVM_DIR"
    tar -xf "$DOWNLOAD_FILE" -C "$TEMP_DIR"

    # Move extracted contents to llvm-build (remove version-specific directory)
    EXTRACTED_DIR=$(find "$TEMP_DIR" -maxdepth 1 -type d -name "clang+llvm-*" | head -n 1)
    if [ -n "$EXTRACTED_DIR" ]; then
        mv "$EXTRACTED_DIR"/* "$LLVM_DIR/"
    else
        echo "Error: Could not find extracted LLVM directory"
        exit 1
    fi

elif [ "$PLATFORM" = "linux" ]; then
    # Download Linux pre-built LLVM (using new naming convention for 21.x)
    LLVM_URL="https://github.com/llvm/llvm-project/releases/download/llvmorg-${LLVM_VERSION}/LLVM-${LLVM_VERSION}-Linux-X64.tar.xz"
    DOWNLOAD_FILE="$TEMP_DIR/llvm.tar.xz"

    echo "Downloading from: $LLVM_URL"
    wget -O "$DOWNLOAD_FILE" "$LLVM_URL" || curl -L -o "$DOWNLOAD_FILE" "$LLVM_URL"

    echo "Extracting LLVM..."
    mkdir -p "$LLVM_DIR"
    tar -xf "$DOWNLOAD_FILE" -C "$TEMP_DIR"

    # Move extracted contents to llvm-build (newer releases extract directly)
    EXTRACTED_DIR=$(find "$TEMP_DIR" -maxdepth 1 -type d \( -name "clang+llvm-*" -o -name "LLVM-*" \) | head -n 1)
    if [ -n "$EXTRACTED_DIR" ]; then
        mv "$EXTRACTED_DIR"/* "$LLVM_DIR/"
    else
        echo "Error: Could not find extracted LLVM directory"
        exit 1
    fi
fi

# Verify installation
echo "Verifying LLVM installation..."
REQUIRED_TOOLS=("clang" "llc" "lld" "llvm-objdump")
MISSING_TOOLS=()

for tool in "${REQUIRED_TOOLS[@]}"; do
    TOOL_PATH="$LLVM_DIR/bin/$tool"
    if [ "$PLATFORM" = "windows" ]; then
        TOOL_PATH="${TOOL_PATH}.exe"
    fi

    if [ ! -f "$TOOL_PATH" ]; then
        MISSING_TOOLS+=("$tool")
    fi
done

if [ ${#MISSING_TOOLS[@]} -ne 0 ]; then
    echo "Error: Missing required LLVM tools: ${MISSING_TOOLS[*]}"
    echo "Installation may be incomplete. Please check the downloaded files."
    exit 1
fi

# Write version file
echo "$LLVM_VERSION" > "$VERSION_FILE"

echo "LLVM $LLVM_VERSION downloaded and verified successfully!"
echo "Installation location: $LLVM_DIR (pre-built binaries)"
