# Test Runner for Maxon LSP Server
# Run this script from the lsp directory

Write-Host "Maxon LSP Server Test Suite" -ForegroundColor Cyan
Write-Host "============================`n" -ForegroundColor Cyan

$testsPassed = 0
$testsFailed = 0
$failedTests = @()

# Build tests
Write-Host "Building tests..." -ForegroundColor Yellow
cd tests
if (-not (Test-Path "build")) {
    New-Item -ItemType Directory -Path "build" | Out-Null
}

cd build
cmake .. -G "Ninja" -DCMAKE_BUILD_TYPE=Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ CMake configuration failed" -ForegroundColor Red
    exit 1
}

cmake --build .
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Build failed" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Tests built successfully`n" -ForegroundColor Green

cd ..

# Run each test
$tests = @(
    "test_lsp_types",
    "test_document_manager",
    "test_json_rpc",
    "test_analyzer",
    "test_lsp_server"
)

foreach ($test in $tests) {
    Write-Host "Running $test..." -ForegroundColor Yellow
    $testPath = "build\$test.exe"
    
    if (Test-Path $testPath) {
        & $testPath
        if ($LASTEXITCODE -eq 0) {
            $testsPassed++
            Write-Host ""
        } else {
            $testsFailed++
            $failedTests += $test
            Write-Host ""
        }
    } else {
        Write-Host "✗ Test executable not found: $testPath" -ForegroundColor Red
        $testsFailed++
        $failedTests += $test
    }
}

# Summary
Write-Host "`n============================`n" -ForegroundColor Cyan
Write-Host "Test Results:" -ForegroundColor Cyan
Write-Host "  Passed: $testsPassed" -ForegroundColor Green
Write-Host "  Failed: $testsFailed" -ForegroundColor $(if ($testsFailed -eq 0) { "Green" } else { "Red" })

if ($testsFailed -gt 0) {
    Write-Host "`nFailed tests:" -ForegroundColor Red
    foreach ($test in $failedTests) {
        Write-Host "  - $test" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "`n✓ All tests passed!" -ForegroundColor Green
    exit 0
}
