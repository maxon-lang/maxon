#!/usr/bin/env bash
set -e

echo "=== Building C# Compiler ==="
dotnet build maxon-sharp

echo ""
echo "=== Running C# Spec Tests ==="
bin/maxon spec-test

echo ""
echo "=== Checking Debugger Goldens ==="
# Gates the debugger/profiler/coverage sample transcripts, which the spec suite does not cover:
# their acceptance is a golden transcript, not a spec test. Runs here because it needs only the
# bootstrap — placing it before the (long) shv2 build means a drifted transcript fails fast.
bash scripts/check-debug-goldens.sh

echo ""
echo "=== Building shv2 Compiler ==="
bin/maxon build maxon-shv2

echo ""
echo "=== Running shv2 Spec Tests ==="
maxon-shv2/.maxon/maxon-shv2 spec-test

echo ""
echo "=== Building maxon-dev MCP Server ==="
pkill -f maxon-dev-mcp 2>/dev/null || true
bin/maxon build maxon-dev-mcp/mcp

echo ""
echo "=== Building maxon-dev MCP Test Runner ==="
bin/maxon build maxon-dev-mcp/test

echo ""
echo "=== Running maxon-dev MCP Tests ==="
maxon-dev-mcp/test/.maxon/maxon-dev-mcp-test

echo ""
echo "=== All steps completed successfully ==="
