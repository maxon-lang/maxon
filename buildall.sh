#!/usr/bin/env bash
set -e

echo "=== Building C# Compiler ==="
dotnet build maxon-sharp

echo ""
echo "=== Running C# Spec Tests ==="
bin/maxon spec-test

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
