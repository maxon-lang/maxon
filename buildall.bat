@echo off
setlocal

echo === Building C# Compiler ===
dotnet build maxon-sharp
if errorlevel 1 exit /b 1

echo.
echo === Running C# Spec Tests ===
bin\maxon.exe spec-test
if errorlevel 1 exit /b 1

echo.
echo === Building Self-Hosted Compiler ===
bin\maxon.exe build maxon-selfhosted
if errorlevel 1 exit /b 1

echo.
echo === Running Self-Hosted Spec Tests ===
maxon-selfhosted\.maxon\maxon-selfhosted.exe spec-test
if errorlevel 1 exit /b 1

echo.
echo === Running Self-Hosted WASM Spec Tests ===
maxon-selfhosted\.maxon\maxon-selfhosted.exe spec-test --target=wasm32-wasi
if errorlevel 1 exit /b 1

echo.
echo === Building maxon-dev MCP Server ===
taskkill /F /IM maxon-dev-mcp.exe >nul 2>&1
bin\maxon.exe build maxon-dev-mcp\mcp
if errorlevel 1 exit /b 1

echo.
echo === Building maxon-dev MCP Test Runner ===
bin\maxon.exe build maxon-dev-mcp\test
if errorlevel 1 exit /b 1

echo.
echo === Running maxon-dev MCP Tests ===
maxon-dev-mcp\test\.maxon\maxon-dev-mcp-test.exe
if errorlevel 1 exit /b 1

echo.
echo === All steps completed successfully ===
