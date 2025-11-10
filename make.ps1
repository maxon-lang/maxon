#!/usr/bin/env pwsh
# Simple build script for Maxon Compiler

param(
    [string]$Target = "build",
    [switch]$UseClang
)

$ErrorActionPreference = "Stop"

function Build-Compiler {
    Write-Host "Building Maxon Compiler..." -ForegroundColor Cyan
    
    # Check if clang is available
    $clangPath = $null
    if (Get-Command clang -ErrorAction SilentlyContinue) {
        $clangPath = (Get-Command clang).Source
        Write-Host "Using Clang compiler: $clangPath" -ForegroundColor Yellow
    }
    
    # Check if Ninja is available for faster builds
    $useNinja = $false
    if (Get-Command ninja -ErrorAction SilentlyContinue) {
        $useNinja = $true
        Write-Host "Using Ninja build system" -ForegroundColor Yellow
        
        if ($clangPath) {
            $clangxxPath = $clangPath -replace 'clang\.exe$', 'clang++.exe'
            cmake -B build -G "Ninja" -DCMAKE_BUILD_TYPE=Release `
                -DCMAKE_C_COMPILER="$clangPath" `
                -DCMAKE_CXX_COMPILER="$clangxxPath"
        } else {
            cmake -B build -G "Ninja" -DCMAKE_BUILD_TYPE=Release
        }
        cmake --build build
    } elseif ($clangPath -or $UseClang) {
        # Direct compilation with Clang - no build system needed
        Write-Host "Compiling directly with Clang" -ForegroundColor Yellow
        
        # Ensure output directory exists
        New-Item -ItemType Directory -Force -Path "build\bin" | Out-Null
        
        # Get LLVM paths
        $llvmInclude = "C:\Users\Eric\Dev\Maxon2\llvm-build\include"
        $llvmLibDir = "C:\Users\Eric\Dev\Maxon2\llvm-build\lib"
        
        # Build list of LLVM libraries with full paths
        $llvmLibs = @(
            "LLVMCore", "LLVMSupport", "LLVMMC", "LLVMMCParser",
            "LLVMBitWriter", "LLVMTarget", "LLVMCodeGen",
            "LLVMX86CodeGen", "LLVMX86AsmParser", 
            "LLVMX86Desc", "LLVMX86Info", "LLVMAsmPrinter",
            "LLVMSelectionDAG", "LLVMScalarOpts", "LLVMInstCombine",
            "LLVMTransformUtils", "LLVMAnalysis", "LLVMObject",
            "LLVMBitReader", "LLVMCore", "LLVMBinaryFormat",
            "LLVMRemarks", "LLVMBitstreamReader", "LLVMTargetParser",
            "LLVMTextAPI", "LLVMProfileData", "LLVMSymbolize",
            "LLVMDebugInfoDWARF", "LLVMDebugInfoPDB",
            "LLVMDebugInfoCodeView", "LLVMDemangle"
        ) | ForEach-Object { "`"$llvmLibDir\$_.lib`"" }
        
        $sourceFiles = "main.cpp", "lexer.cpp", "parser.cpp", "codegen.cpp"
        
        Write-Host "Compiling source files..." -ForegroundColor Cyan
        $compileCmd = "clang++ -std=c++17 -O2 -I`"$llvmInclude`" -D_CRT_SECURE_NO_WARNINGS $($sourceFiles -join ' ') -o build\bin\maxonc.exe $($llvmLibs -join ' ')"
        Invoke-Expression $compileCmd
    } else {
        # Use MSVC with Visual Studio generator
        cmake -B build -G "Visual Studio 17 2022" -A x64
        cmake --build build --config Release --target maxonc -- /v:minimal /nologo /m
    }
    
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
        Write-Host "Usage: .\make.ps1 [build|test|clean] [-UseClang]"
        Write-Host "  build - Build the compiler (default)"
        Write-Host "  test  - Build and run test"
        Write-Host "  clean - Clean build artifacts"
        Write-Host ""
        Write-Host "Options:"
        Write-Host "  -UseClang - Force use of Clang-CL toolset"
    }
}
