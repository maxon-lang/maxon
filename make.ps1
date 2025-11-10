#!/usr/bin/env pwsh
# Simple build script for Maxon Compiler

param(
    [string]$Target = "build"
)

$ErrorActionPreference = "Stop"

function Build-Compiler {
    Write-Host "Building Maxon Compiler..." -ForegroundColor Cyan
    cmake -B build -G "Visual Studio 17 2022" -A x64
    cmake --build build --config Release --target maxonc -- /v:minimal /nologo
    Write-Host "Build complete!" -ForegroundColor Green
}

function Test-Compiler {
    Build-Compiler
    Write-Host ""
    Write-Host "Compiling sample.maxon..." -ForegroundColor Cyan
    & build\bin\Release\maxonc.exe sample.maxon -o output.exe
    Write-Host ""
    Write-Host "Running output.exe..." -ForegroundColor Cyan
    & .\output.exe
}

function Clean-Build {
    Remove-Item -Recurse -Force build -ErrorAction SilentlyContinue
    Remove-Item output.exe -ErrorAction SilentlyContinue
    Remove-Item temp.o -ErrorAction SilentlyContinue
    Write-Host "Cleaned build artifacts" -ForegroundColor Green
}

switch ($Target) {
    "build" { Build-Compiler }
    "test" { Test-Compiler }
    "clean" { Clean-Build }
    default {
        Write-Host "Usage: .\make.ps1 [build|test|clean]"
        Write-Host "  build - Build the compiler (default)"
        Write-Host "  test  - Build and run test"
        Write-Host "  clean - Clean build artifacts"
    }
}
