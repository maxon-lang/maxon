# Build script for Maxon LSP Server
# Run this from PowerShell in the lsp directory

Write-Host "Building Maxon LSP Server..." -ForegroundColor Green

# Step 1: Download nlohmann/json if not present
if (-not (Test-Path "include/json.hpp")) {
    Write-Host "Downloading nlohmann/json library..." -ForegroundColor Yellow
    try {
        Invoke-WebRequest -Uri "https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -OutFile "include/json.hpp"
        Write-Host "✓ Downloaded json.hpp" -ForegroundColor Green
    } catch {
        Write-Host "✗ Failed to download json.hpp" -ForegroundColor Red
        Write-Host "Please download manually from: https://github.com/nlohmann/json/releases/download/v3.11.3/json.hpp" -ForegroundColor Yellow
        exit 1
    }
}

# Step 2: Create build directory
if (-not (Test-Path "build")) {
    New-Item -ItemType Directory -Path "build" | Out-Null
}

Set-Location "build"

# Step 3: Configure CMake
Write-Host "Configuring CMake..." -ForegroundColor Yellow
cmake .. -G "Visual Studio 17 2022"
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ CMake configuration failed" -ForegroundColor Red
    Set-Location ..
    exit 1
}

# Step 4: Build
Write-Host "Building..." -ForegroundColor Yellow
cmake --build . --config Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Build failed" -ForegroundColor Red
    Set-Location ..
    exit 1
}

Set-Location ..

Write-Host "✓ Build completed successfully!" -ForegroundColor Green
Write-Host "Executable: build/Release/maxon-lsp.exe" -ForegroundColor Cyan

# Step 5: Build VS Code extension
Write-Host "`nBuilding VS Code extension..." -ForegroundColor Green
Set-Location "vscode-extension"

if (-not (Test-Path "node_modules")) {
    Write-Host "Installing npm dependencies..." -ForegroundColor Yellow
    npm install
}

Write-Host "Compiling TypeScript..." -ForegroundColor Yellow
npm run compile

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Extension built successfully!" -ForegroundColor Green
} else {
    Write-Host "✗ Extension build failed" -ForegroundColor Red
}

Set-Location ..

Write-Host "`n=== Build Complete ===" -ForegroundColor Green
Write-Host "To install the extension:" -ForegroundColor Cyan
Write-Host "1. Open vscode-extension folder in VS Code" -ForegroundColor White
Write-Host "2. Press F5 to launch Extension Development Host" -ForegroundColor White
Write-Host "3. Open a .maxon file to test" -ForegroundColor White
