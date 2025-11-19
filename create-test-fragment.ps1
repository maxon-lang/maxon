#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates a test fragment file for Maxon language tests with dual IR (optimized and unoptimized).

.DESCRIPTION
    Takes Maxon source code, compiles it with both optimization (-O) and debug (--debug) modes,
    captures both LLVM IRs, counts dynamic instructions using lli, runs the executable to get 
    the exit code and stdout, and creates a properly formatted test fragment file with performance
    regression detection support.

.PARAMETER TestName
    The name of the test (without .test extension)

.PARAMETER SourceCode
    The Maxon source code to test

.PARAMETER SourceFile
    Path to a .maxon file containing the source code (alternative to -SourceCode)

.PARAMETER OutputDir
    Output directory (defaults to language-tests/fragments/)

.PARAMETER SkipUnoptimized
    Skip generating unoptimized IR (for error tests or minimal fragments)

.EXAMPLE
    .\create-test-fragment.ps1 -TestName "my-test" -SourceCode @"
    function main() int
        return 42
    end 'main'
    "@

.EXAMPLE
    .\create-test-fragment.ps1 -TestName "my-test" -SourceFile "examples/my-test.maxon"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$TestName,
    
    [Parameter(Mandatory=$false)]
    [string]$SourceCode,
    
    [Parameter(Mandatory=$false)]
    [string]$SourceFile,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipUnoptimized,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

# Helper function to compile and extract IR
function Compile-MaxonIR {
    param(
        [string]$SourceFile,
        [string]$OutputLL,
        [string]$OptFlag,  # "-O" or "--debug"
        [string]$CompilerPath,
        [switch]$WithProfile
    )
    
    $compilerArgs = @("compile", $SourceFile, "--emit-llvm", "-o", $OutputLL)
    if ($WithProfile) {
        $compilerArgs += "--profile"
    }
    $compilerArgs += $OptFlag
    
    $process = Start-Process -FilePath $CompilerPath -ArgumentList $compilerArgs -Wait -PassThru -NoNewWindow -RedirectStandardOutput "temp_compile_out.txt" -RedirectStandardError "temp_compile_err.txt"
    
    $stdout = ""
    $stderr = ""
    if (Test-Path "temp_compile_out.txt") {
        $temp = Get-Content "temp_compile_out.txt" -Raw
        if ($temp) { $stdout = $temp }
        Remove-Item "temp_compile_out.txt"
    }
    if (Test-Path "temp_compile_err.txt") {
        $temp = Get-Content "temp_compile_err.txt" -Raw
        if ($temp) { $stderr = $temp }
        Remove-Item "temp_compile_err.txt"
    }
    
    $ir = ""
    $exePath = ""
    if ($process.ExitCode -eq 0 -and (Test-Path $OutputLL)) {
        $ir = Get-Content $OutputLL -Raw
        if ($ir) {
            $ir = $ir.Trim()
            $ir = $ir -replace 'source_filename = ".*?"', 'source_filename = "test.maxon"'
            $ir = $ir -replace "ModuleID = '.*?'", "ModuleID = 'test.maxon'"
            $ir = $ir -replace 'DIFile\(filename: ".*?"', 'DIFile(filename: "test.maxon"'
        }
        
        # Find the executable (same base name as .ll file)
        $exePath = $OutputLL -replace '\.ll$', '.exe'
        if (-not (Test-Path $exePath)) {
            $exePath = ""
        }
    }
    
    return @{
        ExitCode = $process.ExitCode
        IR = $ir
        ExePath = $exePath
        Stdout = $stdout
        Stderr = $stderr
    }
}

# Helper function to normalize error messages
function Normalize-ErrorMessage {
    param([string]$Message)
    
    return $Message -replace '(>>>.*?)(temp-opt|temp-debug|output|test)\.exe\.tmp\.obj', '$1test.exe.tmp.obj'
}

# Helper function to run profiled executable and extract instruction count and program output
function Run-ProfiledExecutable {
    param(
        [string]$ExePath,
        [string]$Args
    )
    
    if (-not $ExePath -or -not (Test-Path $ExePath)) {
        return @{
            InstructionCount = -1
            ExitCode = -1
            Stdout = ""
            Stderr = ""
        }
    }
    
    # Run and capture binary output
    try {
        # Capture stdout as raw bytes
        $tempOutput = [System.IO.Path]::GetTempFileName()
        $tempError = [System.IO.Path]::GetTempFileName()
        
        $processArgs = @{
            FilePath = $ExePath
            NoNewWindow = $true
            Wait = $true
            PassThru = $true
            RedirectStandardOutput = $tempOutput
            RedirectStandardError = $tempError
        }
        
        if ($Args) {
            $argArray = $Args -split '\s+'
            $processArgs['ArgumentList'] = $argArray
        }
        
        $process = Start-Process @processArgs
        $exitCode = $process.ExitCode
        
        # Read stderr as text
        $stderrText = ""
        if (Test-Path $tempError) {
            $stderrText = Get-Content $tempError -Raw
            Remove-Item $tempError -ErrorAction SilentlyContinue
        }
        
        # Read stdout as raw bytes to extract both text and binary marker
        $stdoutText = ""
        $instructionCount = -1
        
        if (Test-Path $tempOutput) {
            $bytes = [System.IO.File]::ReadAllBytes($tempOutput)
            
            # Look for "MAXON_PROFILE:" marker (14 bytes) followed by 8 bytes of i64
            $marker = [System.Text.Encoding]::ASCII.GetBytes("MAXON_PROFILE:")
            $markerIndex = -1
            
            for ($i = 0; $i -le $bytes.Length - 22; $i++) {
                $match = $true
                for ($j = 0; $j -lt 14; $j++) {
                    if ($bytes[$i + $j] -ne $marker[$j]) {
                        $match = $false
                        break
                    }
                }
                if ($match) {
                    $markerIndex = $i
                    break
                }
            }
            
            if ($markerIndex -ge 0) {
                # Extract the 8 bytes after the marker
                $countBytes = $bytes[($markerIndex + 14)..($markerIndex + 21)]
                $instructionCount = [BitConverter]::ToInt64($countBytes, 0)
                
                # Extract text before the marker (the actual program output)
                if ($markerIndex -gt 0) {
                    $textBytes = $bytes[0..($markerIndex - 1)]
                    $stdoutText = [System.Text.Encoding]::UTF8.GetString($textBytes)
                }
            } else {
                # No marker found, treat entire output as text
                $stdoutText = [System.Text.Encoding]::UTF8.GetString($bytes)
            }
            
            Remove-Item $tempOutput -ErrorAction SilentlyContinue
        }
        
        return @{
            InstructionCount = $instructionCount
            ExitCode = $exitCode
            Stdout = $stdoutText
            Stderr = $stderrText
        }
    } catch {
        return @{
            InstructionCount = -1
            ExitCode = -1
            Stdout = ""
            Stderr = ""
        }
    }
}

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
    $OutputDir = "language-tests/fragments"
}

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

# Paths
$tempSourceFile = "temp_fragment.maxon"
$tempExeFile = "temp_fragment.exe"
$compilerPath = "maxon.exe"  # Use from PATH
$outputFragmentPath = Join-Path $OutputDir "$TestName.test"

# If updating an existing fragment that is actually a full path, use it directly
if ($SourceFile -and (Test-Path $SourceFile) -and $SourceFile.EndsWith(".test")) {
    $outputFragmentPath = $SourceFile
}

Write-Host "Creating test fragment: $TestName" -ForegroundColor Cyan

try {
    # Write source to temp file (UTF8 without BOM)
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tempSourceFile, $SourceCode, $utf8NoBom)
    
    # Variables to store both IRs and instruction counts
    $optimizedIR = ""
    $unoptimizedIR = ""
    $optimizedInstructionCount = -1
    $unoptimizedInstructionCount = -1
    $exitCode = 0
    $stdoutCapture = ""
    $stderrCapture = ""
    
    # Compile optimized version for IR (without profiling to keep IR clean)
    Write-Host "Compiling optimized version..." -ForegroundColor Yellow
    
    $optResult = Compile-MaxonIR -SourceFile $tempSourceFile -OutputLL "temp-opt.ll" -OptFlag "-O" -CompilerPath $compilerPath
    
    # Check if optimized compilation failed
    if ($optResult.ExitCode -ne 0) {
        Write-Host "Optimized compilation failed!" -ForegroundColor Red
        Write-Host ($optResult.Stdout + $optResult.Stderr)
        
        # Create error fragment with dual-IR format (both N/A)
        $fragmentContent = $SourceCode.TrimEnd()
        $fragmentContent += "`n---`nN/A`n---`nN/A`n---`n"
        
        if ($global:PreservedArgs) {
            $fragmentContent += "Args: $global:PreservedArgs`n"
        }
        
        $backticks = '```'
        $errorMessage = if ($optResult.Stderr) { $optResult.Stderr } else { $optResult.Stdout + $optResult.Stderr }
        $normalizedStderr = Normalize-ErrorMessage $errorMessage
        $fragmentContent += "MaxoncStderr: $backticks`n$($normalizedStderr.TrimEnd())`n$backticks"
        
        $fragmentContent = $fragmentContent -replace "`r`n", "`n" -replace "`n", "`r`n"
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [System.IO.File]::WriteAllText($outputFragmentPath, $fragmentContent, $utf8NoBom)
        Write-Host "Test fragment created: $outputFragmentPath" -ForegroundColor Green
        exit 0
    }
    
    $optimizedIR = $optResult.IR
    
    # Now compile with profiling to get executable and instruction count
    if ($optimizedIR) {
        Write-Host "Compiling with profiling for execution..." -ForegroundColor Yellow
        $profiledResult = Compile-MaxonIR -SourceFile $tempSourceFile -OutputLL "temp-opt-profiled.ll" -OptFlag "-O" -CompilerPath $compilerPath -WithProfile
        
        if ($profiledResult.ExePath) {
            Write-Host "Running optimized executable..." -ForegroundColor Yellow
            $runResult = Run-ProfiledExecutable -ExePath $profiledResult.ExePath -Args $global:PreservedArgs
            
            $optimizedInstructionCount = $runResult.InstructionCount
            $exitCode = $runResult.ExitCode
            $stdoutCapture = $runResult.Stdout
            $stderrCapture = $runResult.Stderr
            
            if ($optimizedInstructionCount -gt 0) {
                Write-Host "Optimized instruction count: $optimizedInstructionCount" -ForegroundColor Gray
            }
            Write-Host "Exit code: $exitCode" -ForegroundColor $(if ($exitCode -eq 0) { "Green" } else { "Yellow" })
            if ($stdoutCapture) {
                Write-Host "Stdout: $stdoutCapture" -ForegroundColor Gray
            }
            if ($stderrCapture) {
                Write-Host "Stderr: $stderrCapture" -ForegroundColor Gray
            }
        }
    }
    
    # Compile unoptimized version for IR (unless SkipUnoptimized)
    if (-not $SkipUnoptimized) {
        Write-Host "Compiling unoptimized version..." -ForegroundColor Yellow
        
        $debugResult = Compile-MaxonIR -SourceFile $tempSourceFile -OutputLL "temp-debug.ll" -OptFlag "--debug" -CompilerPath $compilerPath
        
        if ($debugResult.ExitCode -eq 0) {
            $unoptimizedIR = $debugResult.IR
            
            # Compile with profiling for instruction count
            $debugProfiledResult = Compile-MaxonIR -SourceFile $tempSourceFile -OutputLL "temp-debug-profiled.ll" -OptFlag "--debug" -CompilerPath $compilerPath -WithProfile
            
            if ($debugProfiledResult.ExePath) {
                $debugRunResult = Run-ProfiledExecutable -ExePath $debugProfiledResult.ExePath -Args $global:PreservedArgs
                $unoptimizedInstructionCount = $debugRunResult.InstructionCount
                
                if ($unoptimizedInstructionCount -gt 0) {
                    Write-Host "Unoptimized instruction count: $unoptimizedInstructionCount" -ForegroundColor Gray
                }
            }
        }
    }
    
    # Create the fragment content
    $fragmentContent = $SourceCode.TrimEnd()
    $fragmentContent += "`n---`n"
    
    # Write IR sections - write what we have, or N/A if compilation failed
    if ($optimizedIR) {
        $fragmentContent += $optimizedIR
    } else {
        $fragmentContent += "N/A"
    }
    $fragmentContent += "`n---`n"
    
    if ($unoptimizedIR) {
        $fragmentContent += $unoptimizedIR
    } else {
        $fragmentContent += "N/A"
    }
    
    $fragmentContent += "`n---`n"
    
    # Add metadata fields in order: ExitCode, instruction counts, Args, then Stdout/Stderr
    $fragmentContent += "ExitCode: $exitCode`n"
    
    # Add instruction counts if available
    if ($optimizedInstructionCount -gt 0) {
        $fragmentContent += "OptimizedInstructionCount: $optimizedInstructionCount`n"
    }
    if ($unoptimizedInstructionCount -gt 0) {
        $fragmentContent += "UnoptimizedInstructionCount: $unoptimizedInstructionCount`n"
    }
    
    # Always preserve Args if it was specified
    if ($global:PreservedArgs) {
        $fragmentContent += "Args: $global:PreservedArgs`n"
    }
    
    # Use a variable for triple backticks
    $backticks = '```'
    
    # Add stdout/stderr from executable if present (use triple backticks for multiline)
    if ($stdoutCapture) {
        # Check if it's multiline
        if ($stdoutCapture -match "`n") {
            $fragmentContent += "Stdout: $backticks`n$($stdoutCapture.TrimEnd())`n$backticks`n"
        } else {
            $fragmentContent += "Stdout: $stdoutCapture`n"
        }
    }
    
    if ($stderrCapture) {
        # Check if it's multiline
        if ($stderrCapture -match "`n") {
            $fragmentContent += "Stderr: $backticks`n$($stderrCapture.TrimEnd())`n$backticks`n"
        } else {
            $fragmentContent += "Stderr: $stderrCapture`n"
        }
    }
    
    # If no Stdout was captured but we have old-style Output, keep it for compatibility
    if (-not $stdoutCapture -and $outputCapture) {
        if ($outputCapture -match "`n") {
            $fragmentContent += "Output: ``````n$outputCapture`n``````n"
        } else {
            $fragmentContent += "Output: $outputCapture`n"
        }
    }
    
    # Write fragment file (with CRLF line endings for Windows)
    # Normalize line endings to CRLF and ensure file ends with exactly one newline
    $fragmentContent = $fragmentContent.TrimEnd()
    $fragmentContent = $fragmentContent -replace "`r`n", "`n" -replace "`n", "`r`n"
    $fragmentContent += "`r`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($outputFragmentPath, $fragmentContent, $utf8NoBom)
    
    Write-Host "`nTest fragment created successfully!" -ForegroundColor Green
    Write-Host "Location: $outputFragmentPath" -ForegroundColor Cyan
} finally {
    # Cleanup temp files
    Remove-Item $tempSourceFile -ErrorAction SilentlyContinue
    Remove-Item $tempExeFile -ErrorAction SilentlyContinue
    Remove-Item "temp_fragment.pdb" -ErrorAction SilentlyContinue
    Remove-Item "output.exe" -ErrorAction SilentlyContinue
    Remove-Item "output.pdb" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt.ll" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt.exe" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt.pdb" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt-profiled.ll" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt-profiled.exe" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt-profiled.pdb" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug.ll" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug.exe" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug.pdb" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug-profiled.ll" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug-profiled.exe" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug-profiled.pdb" -ErrorAction SilentlyContinue
    Remove-Item "temp_compile_out.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_compile_err.txt" -ErrorAction SilentlyContinue
}

Write-Host "`nDone!" -ForegroundColor Green
