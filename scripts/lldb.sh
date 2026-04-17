#!/usr/bin/env bash
# Wrapper that launches lldb with the env vars required by its embedded Python 3.10.
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

PY_HOME="/c/Users/Eric/AppData/Local/Python/pythoncore-3.10-64"
LLVM_BIN="$REPO_ROOT/llvm-project/bin"
LLVM_SITE="$REPO_ROOT/llvm-project/Lib/site-packages"

export PYTHONHOME="$PY_HOME"
export PYTHONPATH="$LLVM_SITE"
export PATH="$PY_HOME:$LLVM_BIN:$PATH"

exec "$LLVM_BIN/lldb.exe" "$@"
