#!/usr/bin/env bash
set -e

echo "=== Building C# Compiler ==="
dotnet build maxon-sharp

echo ""
echo "=== Running C# Spec Tests ==="
bin/maxon spec-test

echo ""
echo "=== Building Self-Hosted Compiler ==="
bin/maxon build maxon-selfhosted

echo ""
echo "=== Running Self-Hosted Spec Tests ==="
maxon-selfhosted/.maxon/maxon-selfhosted spec-test

echo ""
echo "=== Running Self-Hosted WASM Spec Tests ==="
maxon-selfhosted/.maxon/maxon-selfhosted spec-test --target=wasm32-wasi

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
