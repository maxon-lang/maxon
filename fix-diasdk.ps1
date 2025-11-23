# Maxon DIA SDK Path Fix
#
# This script fixes a Windows-specific build issue where LLVM looks for the DIA SDK
# (Debug Interface Access SDK) in the Visual Studio 2019 Professional path, but you
# may have a different Visual Studio edition/version installed.
#
# USAGE:
#   1. Right-click on this file in Windows Explorer
#   2. Select "Run with PowerShell"
#   3. When prompted, select "Run as Administrator"
#
# This is a one-time setup. After running this script, you can build the project with 'make all'.
# For more information, see docs/WINDOWS_SETUP.md

$ErrorActionPreference = "Stop"

# Check if running as administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click on the script and select 'Run as Administrator'" -ForegroundColor Yellow
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

# Find Visual Studio installation with DIA SDK
Write-Host "Searching for Visual Studio installations..." -ForegroundColor Yellow
$vsBasePath = 'C:\Program Files\Microsoft Visual Studio'
$sourcePath = $null

if (Test-Path $vsBasePath) {
    # Search for DIA SDK in all Visual Studio versions and editions
    $diaSearchPath = Join-Path $vsBasePath '*\*\DIA SDK\lib\amd64\diaguids.lib'
    $foundPaths = Get-ChildItem -Path $diaSearchPath -ErrorAction SilentlyContinue
    
    if ($foundPaths) {
        # Use the first match (typically the newest version)
        $sourcePath = $foundPaths[0].FullName
        $vsVersion = $foundPaths[0].FullName -replace '.*\\Microsoft Visual Studio\\(\d+)\\.*', '$1'
        $vsEdition = $foundPaths[0].FullName -replace '.*\\Microsoft Visual Studio\\\d+\\([^\\]+)\\.*', '$1'
        Write-Host "  [OK] Found Visual Studio $vsVersion $vsEdition" -ForegroundColor Green
        Write-Host "  [OK] DIA SDK located at: $sourcePath" -ForegroundColor Green
    }
}

if (-not $sourcePath -or -not (Test-Path $sourcePath)) {
    Write-Host "ERROR: Could not find DIA SDK in any Visual Studio installation!" -ForegroundColor Red
    Write-Host "" -ForegroundColor Yellow
    Write-Host "Please ensure Visual Studio with C++ development tools is installed." -ForegroundColor Yellow
    Write-Host "The DIA SDK should be located at:" -ForegroundColor Yellow
    Write-Host "  C:\Program Files\Microsoft Visual Studio\<year>\<edition>\DIA SDK\lib\amd64\diaguids.lib" -ForegroundColor Yellow
    Write-Host "" -ForegroundColor Yellow
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

# Create directory
$targetDir = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\Professional\DIA SDK\lib\amd64'
Write-Host "" -ForegroundColor Yellow
Write-Host "Creating DIA SDK directory structure..." -ForegroundColor Yellow
try {
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
    Write-Host "  [OK] Directory created: $targetDir" -ForegroundColor Green
} catch {
    Write-Host "  [FAILED] Could not create directory: $_" -ForegroundColor Red
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

# Copy file
$targetPath = Join-Path $targetDir "diaguids.lib"
Write-Host "Copying diaguids.lib to LLVM's expected location..." -ForegroundColor Yellow
Write-Host "  From: $sourcePath" -ForegroundColor Gray
Write-Host "  To:   $targetPath" -ForegroundColor Gray
try {
    Copy-Item $sourcePath $targetPath -Force
    Write-Host "  [OK] File copied successfully" -ForegroundColor Green
} catch {
    Write-Host "  [FAILED] Could not copy file: $_" -ForegroundColor Red
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

# Verify file was copied
if (Test-Path $targetPath) {
    $fileSize = (Get-Item $targetPath).Length
    Write-Host "  [OK] Verified: diaguids.lib ($fileSize bytes)" -ForegroundColor Green
} else {
    Write-Host "  [FAILED] File verification failed" -ForegroundColor Red
    Write-Host "Press any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
    exit 1
}

Write-Host ""
Write-Host "SUCCESS! DIA SDK setup complete." -ForegroundColor Green
Write-Host "You can now run 'make all' in Git Bash to build the project." -ForegroundColor Cyan
Write-Host ""
Write-Host "Press any key to close..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
