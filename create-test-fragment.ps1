#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates a test fragment file for Maxon language tests.

.DESCRIPTION
    Takes Maxon source code, compiles it with optimization, captures the LLVM IR,
    runs the executable to get the exit code and stdout, and creates a properly
    formatted test fragment file.

.PARAMETER TestName
    The name of the test (without .test extension)

.PARAMETER SourceCode
    The Maxon source code to test

.PARAMETER SourceFile
    Path to a .maxon file containing the source code (alternative to -SourceCode)

.PARAMETER UseDebug
    Create a debug fragment (no optimization, in debug-fragments/ directory)

.PARAMETER OutputDir
    Output directory (defaults to language-tests/fragments/ or language-tests/debug-fragments/)

.EXAMPLE
    .\create-test-fragment.ps1 -TestName "my-test" -SourceCode @"
    function main() int
        return 42
    end 'main'
    "@

.EXAMPLE
    .\create-test-fragment.ps1 -TestName "my-test" -SourceFile "examples/my-test.maxon"

.EXAMPLE
    .\create-test-fragment.ps1 -TestName "my-debug-test" -SourceFile "test.maxon" -UseDebug
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$TestName,
    
    [Parameter(Mandatory=$false)]
    [string]$SourceCode,
    
    [Parameter(Mandatory=$false)]
    [string]$SourceFile,
    
    [Parameter(Mandatory=$false)]
    [switch]$UseDebug,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

# Validate inputs
if (-not $SourceCode -and -not $SourceFile) {
    Write-Error "Either -SourceCode or -SourceFile must be provided"
    exit 1
}

if ($SourceCode -and $SourceFile) {
    Write-Error "Cannot specify both -SourceCode and -SourceFile"
    exit 1
}

# Read source from file if provided
if ($SourceFile) {
    if (-not (Test-Path $SourceFile)) {
        Write-Error "Source file not found: $SourceFile"
        exit 1
    }
    $SourceCode = Get-Content $SourceFile -Raw
}

# Determine output directory
if (-not $OutputDir) {
    if ($UseDebug) {
        $OutputDir = "language-tests/debug-fragments"
    } else {
        $OutputDir = "language-tests/fragments"
    }
}

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Paths
$tempSourceFile = "temp_fragment.maxon"
$tempExeFile = "temp_fragment.exe"
$compilerPath = ".\build\bin\maxon.exe"
$outputFragmentPath = Join-Path $OutputDir "$TestName.test"

# Check if compiler exists
if (-not (Test-Path $compilerPath)) {
    Write-Error "Compiler not found at: $compilerPath. Run 'make compiler' first."
    exit 1
}

Write-Host "Creating test fragment: $TestName" -ForegroundColor Cyan
Write-Host "Debug mode: $UseDebug" -ForegroundColor Gray

try {
    # Write source to temp file (UTF8 without BOM)
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tempSourceFile, $SourceCode, $utf8NoBom)
    
    # Compile with appropriate flags
    $compilerArgs = @("compile", $tempSourceFile, "--emit-llvm")
    if ($UseDebug) {
        $compilerArgs += "--debug"
    } else {
        $compilerArgs += "-O"
    }
    
    Write-Host "Compiling..." -ForegroundColor Yellow
    
    # Run compiler and capture all output
    $compileProcess = Start-Process -FilePath $compilerPath -ArgumentList $compilerArgs -Wait -PassThru -NoNewWindow -RedirectStandardOutput "temp_compile_out.txt" -RedirectStandardError "temp_compile_err.txt"
    $compileExitCode = $compileProcess.ExitCode
    
    $compileStdout = ""
    $compileStderr = ""
    if (Test-Path "temp_compile_out.txt") {
        $compileStdout = Get-Content "temp_compile_out.txt" -Raw
        Remove-Item "temp_compile_out.txt"
    }
    if (Test-Path "temp_compile_err.txt") {
        $compileStderr = Get-Content "temp_compile_err.txt" -Raw
        Remove-Item "temp_compile_err.txt"
    }
    
    $compileOutput = $compileStdout + $compileStderr
    
    # Check if compilation succeeded
    if ($compileExitCode -ne 0) {
        Write-Host "Compilation failed!" -ForegroundColor Red
        Write-Host $compileOutput
        
        # For compilation errors, create fragment with N/A for IR
        Write-Host "Creating error test fragment (compilation should fail)..." -ForegroundColor Yellow
        
        $fragmentContent = $SourceCode.TrimEnd()
        $fragmentContent += "`n---`nN/A`n---`nExitCode: 1"
        
        $fragmentContent | Set-Content -Path $outputFragmentPath -NoNewline -Encoding UTF8
        Write-Host "Test fragment created: $outputFragmentPath" -ForegroundColor Green
        Write-Host "(This is an error test - compilation is expected to fail)" -ForegroundColor Yellow
        exit 0
    }
    
    # Extract LLVM IR from output
    $irStartIndex = $compileOutput.IndexOf("=== LLVM IR ===")
    if ($irStartIndex -eq -1) {
        Write-Error "Could not find LLVM IR in compiler output"
        exit 1
    }
    
    $irStart = $compileOutput.IndexOf("`n", $irStartIndex) + 1
    $irEnd = $compileOutput.IndexOf("Object file generated.", $irStart)
    if ($irEnd -eq -1) {
        $irEnd = $compileOutput.IndexOf("Linking with LLD", $irStart)
    }
    if ($irEnd -eq -1) {
        $irEnd = $compileOutput.IndexOf("Code generation complete.", $irStart)
    }
    if ($irEnd -eq -1) {
        # Use end of string if no marker found
        $irEnd = $compileOutput.Length
    }
    
    if ($irEnd -le $irStart) {
        Write-Error "Could not extract LLVM IR properly (invalid indices: start=$irStart, end=$irEnd)"
        exit 1
    }
    
    $llvmIR = $compileOutput.Substring($irStart, $irEnd - $irStart).Trim()
    
    # Replace source filename with "test.maxon" in IR
    $llvmIR = $llvmIR -replace 'source_filename = ".*?"', 'source_filename = "test.maxon"'
    $llvmIR = $llvmIR -replace "ModuleID = '.*?'", "ModuleID = 'test.maxon'"
    
    # The compiler generates output.exe by default
    $actualExeFile = "output.exe"
    
    # Run the executable and capture exit code and stdout
    Write-Host "Running executable..." -ForegroundColor Yellow
    if (Test-Path $actualExeFile) {
        $outputCapture = ""
        $exitCode = 0
        
        try {
            # Run and capture output
            $process = Start-Process -FilePath ".\$actualExeFile" -Wait -PassThru -NoNewWindow -RedirectStandardOutput "temp_stdout.txt" -RedirectStandardError "temp_stderr.txt"
            $exitCode = $process.ExitCode
            
            if (Test-Path "temp_stdout.txt") {
                $outputCapture = Get-Content "temp_stdout.txt" -Raw
                Remove-Item "temp_stdout.txt" -ErrorAction SilentlyContinue
            }
            if (Test-Path "temp_stderr.txt") {
                Remove-Item "temp_stderr.txt" -ErrorAction SilentlyContinue
            }
        } catch {
            Write-Warning "Could not run executable: $_"
            $exitCode = 1
        }
        
        Write-Host "Exit code: $exitCode" -ForegroundColor $(if ($exitCode -eq 0) { "Green" } else { "Yellow" })
        if ($outputCapture) {
            Write-Host "Output: $outputCapture" -ForegroundColor Gray
        }
    } else {
        Write-Warning "Executable not found, assuming exit code 0"
        $exitCode = 0
        $outputCapture = ""
    }
    
    # Create the fragment content
    $fragmentContent = $SourceCode.TrimEnd()
    $fragmentContent += "`n---`n"
    $fragmentContent += $llvmIR
    $fragmentContent += "`n---`n"
    
    if ($outputCapture) {
        $fragmentContent += "Output:`n$outputCapture`n"
    }
    
    $fragmentContent += "ExitCode: $exitCode"
    
    # Write fragment file
    $fragmentContent | Set-Content -Path $outputFragmentPath -NoNewline -Encoding UTF8
    
    Write-Host "`nTest fragment created successfully!" -ForegroundColor Green
    Write-Host "Location: $outputFragmentPath" -ForegroundColor Cyan
    Write-Host "`nFragment preview:" -ForegroundColor Yellow
    Write-Host "=================" -ForegroundColor Gray
    Write-Host $fragmentContent.Substring(0, [Math]::Min(500, $fragmentContent.Length))
    if ($fragmentContent.Length -gt 500) {
        Write-Host "..." -ForegroundColor Gray
        Write-Host "(truncated, see full file at $outputFragmentPath)" -ForegroundColor Gray
    }
    
} finally {
    # Cleanup temp files
    Remove-Item $tempSourceFile -ErrorAction SilentlyContinue
    Remove-Item $tempExeFile -ErrorAction SilentlyContinue
    Remove-Item "temp_fragment.pdb" -ErrorAction SilentlyContinue
    Remove-Item "output.exe" -ErrorAction SilentlyContinue
    Remove-Item "output.pdb" -ErrorAction SilentlyContinue
    Remove-Item "temp_stdout.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_stderr.txt" -ErrorAction SilentlyContinue
}

Write-Host "`nDone!" -ForegroundColor Green
