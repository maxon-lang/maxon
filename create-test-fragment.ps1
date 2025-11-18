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
    
    # Check if this is a .test fragment file
    if ($SourceFile.EndsWith(".test")) {
        # Parse the existing fragment to extract source code and metadata
        $fragmentContent = Get-Content $SourceFile -Raw
        $firstSeparator = $fragmentContent.IndexOf("`n---")
        if ($firstSeparator -eq -1) {
            Write-Error "Invalid fragment file format: missing --- separator"
            exit 1
        }
        $SourceCode = $fragmentContent.Substring(0, $firstSeparator)
        
        # Find the second separator (after IR)
        $secondSeparator = $fragmentContent.IndexOf("`n---", $firstSeparator + 4)
        if ($secondSeparator -ne -1) {
            # Extract metadata section (everything after second ---)
            $metadataSection = $fragmentContent.Substring($secondSeparator + 5).Trim()
            
            # Parse metadata fields - only preserve Args (command line arguments)
            $global:PreservedArgs = ""
            
            $lines = $metadataSection -split "`r?`n"
            for ($i = 0; $i -lt $lines.Length; $i++) {
                $line = $lines[$i]
                if ($line -match '^Args:\s*(.*)$') {
                    $global:PreservedArgs = $matches[1].Trim()
                    break  # Only need Args, skip the rest
                }
            }
        }
    } else {
        $SourceCode = Get-Content $SourceFile -Raw
    }
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

# If updating an existing fragment that is actually a full path, use it directly
if ($SourceFile -and (Test-Path $SourceFile) -and $SourceFile.EndsWith(".test")) {
    $outputFragmentPath = $SourceFile
}

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
        
        # For compilation errors, create fragment with N/A for IR and capture compiler stderr
        Write-Host "Creating error test fragment (compilation should fail)..." -ForegroundColor Yellow
        
        $fragmentContent = $SourceCode.TrimEnd()
        $fragmentContent += "`n---`nN/A`n---`n"
        
        # Preserve Args if it was specified
        if ($global:PreservedArgs) {
            $fragmentContent += "Args: $global:PreservedArgs`n"
        }
        
        # Add compiler stderr if present (use triple backticks for multiline)
        # Normalize temp file names in linker errors for consistent test results
        $backticks = '```'
        if ($compileStderr) {
            $normalizedStderr = $compileStderr -replace '(>>>.*?)(output|test)\.exe\.tmp\.obj', '$1test.exe.tmp.obj'
            $fragmentContent += "MaxoncStderr: $backticks`n$($normalizedStderr.TrimEnd())`n$backticks"
        }
        else {
            # If stderr is empty but we have output, use that
            $normalizedOutput = $compileOutput -replace '(>>>.*?)(output|test)\.exe\.tmp\.obj', '$1test.exe.tmp.obj'
            $fragmentContent += "MaxoncStderr: $backticks`n$($normalizedOutput.TrimEnd())`n$backticks"
        }
        
        # Normalize line endings to CRLF for Windows
        $fragmentContent = $fragmentContent -replace "`r`n", "`n" -replace "`n", "`r`n"
        
        # Write with UTF-8 no BOM
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($outputFragmentPath, $fragmentContent, $utf8NoBom)
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
    # Also replace DIFile filename in debug info
    $llvmIR = $llvmIR -replace 'DIFile\(filename: ".*?"', 'DIFile(filename: "test.maxon"'
    
    # The compiler generates output.exe by default
    $actualExeFile = "output.exe"
    
    # Run the executable and capture exit code, stdout, and stderr
    Write-Host "Running executable..." -ForegroundColor Yellow
    $exitCode = 0
    $stdoutCapture = ""
    $stderrCapture = ""
    
    if (Test-Path $actualExeFile) {
        try {
            # Build argument list from preserved Args if available
            $exeArgs = @()
            if ($global:PreservedArgs) {
                # Simple space-split - may need more sophisticated parsing for quoted args
                $exeArgs = $global:PreservedArgs -split '\s+'
            }
            
            # Run with arguments and capture output
            $processArgs = @{
                FilePath = ".\$actualExeFile"
                Wait = $true
                PassThru = $true
                NoNewWindow = $true
                RedirectStandardOutput = "temp_stdout.txt"
                RedirectStandardError = "temp_stderr.txt"
            }
            
            if ($exeArgs.Count -gt 0) {
                $processArgs['ArgumentList'] = $exeArgs
            }
            
            $process = Start-Process @processArgs
            $exitCode = $process.ExitCode
            
            if (Test-Path "temp_stdout.txt") {
                $stdoutCapture = Get-Content "temp_stdout.txt" -Raw
                Remove-Item "temp_stdout.txt" -ErrorAction SilentlyContinue
            }
            if (Test-Path "temp_stderr.txt") {
                $stderrCapture = Get-Content "temp_stderr.txt" -Raw
                Remove-Item "temp_stderr.txt" -ErrorAction SilentlyContinue
            }
        } catch {
            Write-Warning "Could not run executable: $_"
            $exitCode = 1
        }
        
        Write-Host "Exit code: $exitCode" -ForegroundColor $(if ($exitCode -eq 0) { "Green" } else { "Yellow" })
        if ($stdoutCapture) {
            Write-Host "Stdout: $stdoutCapture" -ForegroundColor Gray
        }
        if ($stderrCapture) {
            Write-Host "Stderr: $stderrCapture" -ForegroundColor Gray
        }
    } else {
        Write-Warning "Executable not found, assuming exit code 0"
        $exitCode = 0
    }
    
    # Create the fragment content
    $fragmentContent = $SourceCode.TrimEnd()
    $fragmentContent += "`n---`n"
    $fragmentContent += $llvmIR
    $fragmentContent += "`n---`n"
    
    # Add metadata fields in order: ExitCode first, then Args, then Stdout/Stderr
    $fragmentContent += "ExitCode: $exitCode`n"
    
    # Always preserve Args if it was specified
    if ($global:PreservedArgs) {
        $fragmentContent += "Args: $global:PreservedArgs`n"
    }
    
    # Use a variable for triple backticks
    $backticks = '```'
    
    # Add stdout/stderr from executable if present (use triple backticks for multiline)
    if ($stdoutCapture) {
        # Check if it's multiline
        if ($stdoutCapture.Contains("`n")) {
            $fragmentContent += "Stdout: $backticks`n$($stdoutCapture.TrimEnd())`n$backticks`n"
        } else {
            $fragmentContent += "Stdout: $stdoutCapture`n"
        }
    }
    
    if ($stderrCapture) {
        # Check if it's multiline
        if ($stderrCapture.Contains("`n")) {
            $fragmentContent += "Stderr: $backticks`n$($stderrCapture.TrimEnd())`n$backticks`n"
        } else {
            $fragmentContent += "Stderr: $stderrCapture`n"
        }
    }
    
    # If no Stdout was captured but we have old-style Output, keep it for compatibility
    if (-not $stdoutCapture -and $outputCapture) {
        if ($outputCapture.Contains("`n")) {
            $fragmentContent += "Output: ``````n$outputCapture`n``````n"
        } else {
            $fragmentContent += "Output: $outputCapture`n"
        }
    }
    
    # Write fragment file (with CRLF line endings for Windows)
    # Normalize line endings to CRLF
    $fragmentContent = $fragmentContent -replace "`r`n", "`n" -replace "`n", "`r`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($outputFragmentPath, $fragmentContent, $utf8NoBom)
    
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
