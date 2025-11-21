# Run all Maxon test suites and report summary
param()

$ErrorActionPreference = "Continue"
$results = @{}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Running all test suites..." -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Compiler self-tests
Write-Host "[1/4] Running compiler self-tests..." -ForegroundColor Yellow
Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
maxon self-test
$results['self-tests'] = $LASTEXITCODE
Write-Host ""

# Test 2: Language fragment tests
Write-Host "[2/4] Running language fragment tests..." -ForegroundColor Yellow
Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
maxon extract-specs | Out-Null
maxon regen-fragments | Out-Null
maxon test-fragments
$results['fragment-tests'] = $LASTEXITCODE
Write-Host ""

# Test 3: LSP C++ unit tests
Write-Host "[3/4] Running LSP C++ unit tests..." -ForegroundColor Yellow
Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
if (!(Test-Path "lsp-server\tests\build")) {
    New-Item -ItemType Directory -Path "lsp-server\tests\build" | Out-Null
}
Push-Location "lsp-server\tests\build"
cmake .. -G "Ninja" -DCMAKE_C_COMPILER="C:/Program Files/LLVM/bin/clang.exe" -DCMAKE_CXX_COMPILER="C:/Program Files/LLVM/bin/clang++.exe" -DCMAKE_BUILD_TYPE=Debug 2>&1 | Out-Null
cmake --build . 2>&1 | Out-Null
ctest --output-on-failure
$results['lsp-tests'] = $LASTEXITCODE
Pop-Location
Write-Host ""

# Test 4: VS Code extension tests
Write-Host "[4/4] Running VS Code extension tests..." -ForegroundColor Yellow
Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
Push-Location "vscode-extension"
npm run test
$results['extension-tests'] = $LASTEXITCODE
Pop-Location
Write-Host ""

# Summary
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Test Summary:" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$failed = 0
if ($results['self-tests'] -ne 0) {
    Write-Host "[FAILED] Compiler self-tests" -ForegroundColor Red
    $failed++
} else {
    Write-Host "[PASSED] Compiler self-tests" -ForegroundColor Green
}

if ($results['fragment-tests'] -ne 0) {
    Write-Host "[FAILED] Language fragment tests" -ForegroundColor Red
    $failed++
} else {
    Write-Host "[PASSED] Language fragment tests" -ForegroundColor Green
}

if ($results['lsp-tests'] -ne 0) {
    Write-Host "[FAILED] LSP C++ unit tests" -ForegroundColor Red
    $failed++
} else {
    Write-Host "[PASSED] LSP C++ unit tests" -ForegroundColor Green
}

if ($results['extension-tests'] -ne 0) {
    Write-Host "[FAILED] VS Code extension tests" -ForegroundColor Red
    $failed++
} else {
    Write-Host "[PASSED] VS Code extension tests" -ForegroundColor Green
}

Write-Host ""
if ($failed -gt 0) {
    Write-Host "$failed test suite(s) failed" -ForegroundColor Red
    exit 1
} else {
    Write-Host "All test suites passed!" -ForegroundColor Green
    exit 0
}
