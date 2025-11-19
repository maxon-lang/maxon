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

# Helper function to count instructions dynamically using --profile flag
function Count-Instructions {
    param(
        [string]$SourceFile,
        [string]$OptimizeFlag,  # "-O" or "--debug"
        [string]$Args
    )
    
    # Compile with profiling
    $exePath = [System.IO.Path]::GetTempFileName() + ".exe"
    $compileOutput = & maxon.exe compile $SourceFile --profile $OptimizeFlag -o $exePath 2>&1
    
    if ($LASTEXITCODE -ne 0 -or !(Test-Path $exePath)) {
        # Compilation failed, fall back to static IR instruction counting
        # Compilation failed, fall back to static IR instruction counting
        $llPath = [System.IO.Path]::GetTempFileName() + ".ll"
        & maxon.exe compile $SourceFile --emit-llvm $OptimizeFlag -o $llPath 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0 -and (Test-Path $llPath)) {
            $irContent = Get-Content $llPath -Raw
            
            # Count actual LLVM instructions (not comments, labels, or declarations)
            $instructions = $irContent -split "`n" | Where-Object {
                $_ -match '^\s+%' -or                    # Instructions with results
                $_ -match '^\s+(store|br|ret|call|invoke)' -or  # Instructions without results
                $_ -match '^\s+(load|add|sub|mul|icmp|fcmp|alloca|getelementptr)'
            }
            
            Remove-Item $llPath -ErrorAction SilentlyContinue
            return $instructions.Count
        }
        
        return -1
    }
    
    # Run and capture binary output
    try {
        # Capture stdout as raw bytes
        $tempOutput = [System.IO.Path]::GetTempFileName()
        
        if ($Args) {
            $argArray = $Args -split '\s+'
            $process = Start-Process -FilePath $exePath -ArgumentList $argArray -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOutput
        } else {
            $process = Start-Process -FilePath $exePath -NoNewWindow -Wait -PassThru -RedirectStandardOutput $tempOutput
        }
        
        # Read the raw bytes (regardless of exit code, program may have printed before exiting)
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
                $count = [BitConverter]::ToInt64($countBytes, 0)
                
                Remove-Item $exePath -ErrorAction SilentlyContinue
                Remove-Item $tempOutput -ErrorAction SilentlyContinue
                return $count
            }
        }
        
        Remove-Item $tempOutput -ErrorAction SilentlyContinue
    } finally {
        Remove-Item $exePath -ErrorAction SilentlyContinue
    }
    
    return -1
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
    
    # Compile both optimized and unoptimized versions
    Write-Host "Compiling optimized version..." -ForegroundColor Yellow
        
        # Compile optimized
        $compilerArgs = @("compile", $tempSourceFile, "--emit-llvm", "-o", "temp-opt.ll", "-O")
        $compileProcess = Start-Process -FilePath $compilerPath -ArgumentList $compilerArgs -Wait -PassThru -NoNewWindow -RedirectStandardOutput "temp_compile_opt_out.txt" -RedirectStandardError "temp_compile_opt_err.txt"
        $compileOptExitCode = $compileProcess.ExitCode
        
        $compileOptStdout = ""
        $compileOptStderr = ""
        if (Test-Path "temp_compile_opt_out.txt") {
            $temp = Get-Content "temp_compile_opt_out.txt" -Raw
            if ($temp) { $compileOptStdout = $temp }
            Remove-Item "temp_compile_opt_out.txt"
        }
        if (Test-Path "temp_compile_opt_err.txt") {
            $temp = Get-Content "temp_compile_opt_err.txt" -Raw
            if ($temp) { $compileOptStderr = $temp }
            Remove-Item "temp_compile_opt_err.txt"
        }
        
        $compileOptOutput = $compileOptStdout + $compileOptStderr
        
        # Check if optimized compilation failed
        if ($compileOptExitCode -ne 0) {
            Write-Host "Optimized compilation failed!" -ForegroundColor Red
            Write-Host $compileOptOutput
            
            # Create error fragment with dual-IR format (both N/A)
            $fragmentContent = $SourceCode.TrimEnd()
            $fragmentContent += "`n---`nN/A`n---`nN/A`n---`n"
            
            if ($global:PreservedArgs) {
                $fragmentContent += "Args: $global:PreservedArgs`n"
            }
            
            $backticks = '```'
            if ($compileOptStderr) {
                $normalizedStderr = $compileOptStderr -replace '(>>>.*?)(temp-opt|temp-debug|output|test)\.exe\.tmp\.obj', '$1test.exe.tmp.obj'
                $fragmentContent += "MaxoncStderr: $backticks`n$($normalizedStderr.TrimEnd())`n$backticks"
            }
            else {
                $normalizedOutput = $compileOptOutput -replace '(>>>.*?)(temp-opt|temp-debug|output|test)\.exe\.tmp\.obj', '$1test.exe.tmp.obj'
                $fragmentContent += "MaxoncStderr: $backticks`n$($normalizedOutput.TrimEnd())`n$backticks"
            }
            
            $fragmentContent = $fragmentContent -replace "`r`n", "`n" -replace "`n", "`r`n"
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [System.IO.File]::WriteAllText($outputFragmentPath, $fragmentContent, $utf8NoBom)
            Write-Host "Test fragment created: $outputFragmentPath" -ForegroundColor Green
            exit 0
        }
        
        # Extract optimized IR from the generated .ll file
        $llOptFile = "temp-opt.ll"
        if (Test-Path $llOptFile) {
            $optimizedIR = Get-Content $llOptFile -Raw
            if ($optimizedIR) {
                $optimizedIR = $optimizedIR.Trim()
                $optimizedIR = $optimizedIR -replace 'source_filename = ".*?"', 'source_filename = "test.maxon"'
                $optimizedIR = $optimizedIR -replace "ModuleID = '.*?'", "ModuleID = 'test.maxon'"
                $optimizedIR = $optimizedIR -replace 'DIFile\(filename: ".*?"', 'DIFile(filename: "test.maxon"'
                
                # Count instructions using profiling
                $optimizedInstructionCount = Count-Instructions -SourceFile $tempSourceFile -OptimizeFlag "-O" -Args $global:PreservedArgs
                if ($optimizedInstructionCount -gt 0) {
                    Write-Host "Optimized instruction count: $optimizedInstructionCount" -ForegroundColor Gray
                }
            }
        }
        
        # Compile unoptimized (unless SkipUnoptimized)
        if (-not $SkipUnoptimized) {
            Write-Host "Compiling unoptimized version..." -ForegroundColor Yellow
            
            $compilerArgs = @("compile", $tempSourceFile, "--emit-llvm", "-o", "temp-debug.ll", "--debug")
            $compileProcess = Start-Process -FilePath $compilerPath -ArgumentList $compilerArgs -Wait -PassThru -NoNewWindow -RedirectStandardOutput "temp_compile_debug_out.txt" -RedirectStandardError "temp_compile_debug_err.txt"
            $compileDebugExitCode = $compileProcess.ExitCode
            
            $compileDebugStdout = ""
            $compileDebugStderr = ""
            if (Test-Path "temp_compile_debug_out.txt") {
                $temp = Get-Content "temp_compile_debug_out.txt" -Raw
                if ($temp) { $compileDebugStdout = $temp }
                Remove-Item "temp_compile_debug_out.txt"
            }
            if (Test-Path "temp_compile_debug_err.txt") {
                $temp = Get-Content "temp_compile_debug_err.txt" -Raw
                if ($temp) { $compileDebugStderr = $temp }
                Remove-Item "temp_compile_debug_err.txt"
            }
            
            $compileDebugOutput = $compileDebugStdout + $compileDebugStderr
            
            if ($compileDebugExitCode -eq 0) {
                # Extract unoptimized IR from the generated .ll file
                $llDebugFile = "temp-debug.ll"
                if (Test-Path $llDebugFile) {
                    $unoptimizedIR = Get-Content $llDebugFile -Raw
                    if ($unoptimizedIR) {
                        $unoptimizedIR = $unoptimizedIR.Trim()
                        $unoptimizedIR = $unoptimizedIR -replace 'source_filename = ".*?"', 'source_filename = "test.maxon"'
                        $unoptimizedIR = $unoptimizedIR -replace "ModuleID = '.*?'", "ModuleID = 'test.maxon'"
                        $unoptimizedIR = $unoptimizedIR -replace 'DIFile\(filename: ".*?"', 'DIFile(filename: "test.maxon"'
                        
                        # Count instructions using profiling
                        $unoptimizedInstructionCount = Count-Instructions -SourceFile $tempSourceFile -OptimizeFlag "--debug" -Args $global:PreservedArgs
                        if ($unoptimizedInstructionCount -gt 0) {
                            Write-Host "Unoptimized instruction count: $unoptimizedInstructionCount" -ForegroundColor Gray
                        }
                    }
                }
            }
        }
    
    # The compiler generates executables alongside the .ll files
    # For new dual-IR mode, we use the optimized executable
    $actualExeFile = if (Test-Path "temp-opt.exe") { 
        "temp-opt.exe" 
    } elseif (Test-Path "temp-debug.exe") { 
        "temp-debug.exe"
    } else {
        "output.exe"  # Fallback for old single-compile mode
    }
    
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
    Remove-Item "temp_stdout.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_stderr.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp-opt.ll" -ErrorAction SilentlyContinue
    Remove-Item "temp-debug.ll" -ErrorAction SilentlyContinue
    Remove-Item "temp_lli_out.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_lli_err.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_compile_opt_out.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_compile_opt_err.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_compile_debug_out.txt" -ErrorAction SilentlyContinue
    Remove-Item "temp_compile_debug_err.txt" -ErrorAction SilentlyContinue
}

Write-Host "`nDone!" -ForegroundColor Green
