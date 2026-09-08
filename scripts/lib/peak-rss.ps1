# Peak memory of a child process, read from a WINDOWS JOB OBJECT.
#
# ⭐ WHY A JOB OBJECT AND NOT `$p.PeakWorkingSet64`. MEASURED, all three obvious spellings:
# `Start-Process -PassThru -Wait`, `Start-Process -PassThru` + `WaitForExit()`, and a raw
# `[System.Diagnostics.Process]::Start` + `WaitForExit()` ALL return an EMPTY PeakWorkingSet64
# once the child has exited — the counter is read through the process handle and the kernel has
# already torn its accounting down. `$p.ExitCode` survives, which is what makes the failure look
# like a working script that measures nothing.
#
# A Job Object's `PeakProcessMemoryUsed` is accounted by the KERNEL and OUTLIVES the process, so
# it can be read after exit. It is also a genuine high-water mark rather than a sample, so no
# polling loop can miss a spike between reads.
#
# ⚠ THE ONE HONEST CAVEAT. The child is created by .NET (which sets up the stdio redirection for
# us) and assigned to the job immediately afterwards, rather than being created suspended and
# assigned before its first instruction. The unmeasured window is the few hundred microseconds of
# loader initialisation before managed code runs — a period in which the child cannot yet have
# reached a peak that matters here. Creating it suspended would close the window at the cost of
# hand-rolling CreateProcess + STARTUPINFO redirection; that trade is not worth it for an
# instrument whose subject allocates hundreds of megabytes over seconds.
param(
	[Parameter(Mandatory=$true)][string]$Exe,
	# ⚠ Arguments arrive through a FILE, one per line, NOT as a [string[]] parameter. PowerShell
	# binds a bare `-o` in an array argument as a PARAMETER NAME ("ParameterAlreadyBound"), so any
	# child command carrying a dash-flag breaks the harness rather than the child. A file cannot be
	# reinterpreted as parameter syntax.
	[string]$ArgsFile = "",
	[Parameter(Mandatory=$true)][string]$OutFile,
	[Parameter(Mandatory=$true)][string]$ErrFile
)

$ErrorActionPreference = "Stop"

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class MaxonJob {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr CreateJobObject(IntPtr a, string name);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, int len, IntPtr ret);
    [DllImport("kernel32.dll", SetLastError=true)]
    public static extern bool CloseHandle(IntPtr h);
    // JOBOBJECT_EXTENDED_LIMIT_INFORMATION. Only PeakProcessMemoryUsed is read; the rest is
    // laid out exactly so that field lands at the right offset.
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct BASIC_LIMIT_INFORMATION {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct EXTENDED_LIMIT_INFORMATION {
        public BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    public const int ExtendedLimitInformation = 9;
}
"@

$job = [MaxonJob]::CreateJobObject([IntPtr]::Zero, $null)
if ($job -eq [IntPtr]::Zero) { Write-Output "ERROR=CreateJobObject failed"; exit 3 }

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $Exe
# ⚠ Windows PowerShell 5.1 runs on .NET Framework, where `ProcessStartInfo.ArgumentList` DOES NOT
# EXIST (it arrived in .NET Core 2.1). Using it fails with "You cannot call a method on a
# null-valued expression" — a null-reference error that names neither the property nor the
# edition, which is why this comment does. The command line is therefore built as ONE string,
# with each argument quoted here.
$ChildArgs = @()
if ($ArgsFile -ne "" -and (Test-Path $ArgsFile)) { $ChildArgs = @([System.IO.File]::ReadAllLines($ArgsFile)) }
$psi.Arguments = (($ChildArgs | ForEach-Object {
	if ($_ -match '[\s"]') { '"' + ($_ -replace '(\*)"', '$1$1\"') + '"' } else { $_ }
}) -join ' ')
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true

$p = [System.Diagnostics.Process]::Start($psi)
[void][MaxonJob]::AssignProcessToJobObject($job, $p.Handle)

# Read both pipes to completion BEFORE waiting. A child that fills a pipe buffer blocks
# forever if nobody drains it, and the wait would then never return.
$stdoutTask = $p.StandardOutput.ReadToEndAsync()
$stderrTask = $p.StandardError.ReadToEndAsync()
$p.WaitForExit()
[System.IO.File]::WriteAllText($OutFile, $stdoutTask.Result)
[System.IO.File]::WriteAllText($ErrFile, $stderrTask.Result)

$size = [System.Runtime.InteropServices.Marshal]::SizeOf([type][MaxonJob+EXTENDED_LIMIT_INFORMATION])
$buf = [System.Runtime.InteropServices.Marshal]::AllocHGlobal($size)
try {
	if (-not [MaxonJob]::QueryInformationJobObject($job, [MaxonJob]::ExtendedLimitInformation, $buf, $size, [IntPtr]::Zero)) {
		Write-Output "ERROR=QueryInformationJobObject failed"
		exit 3
	}
	$info = [System.Runtime.InteropServices.Marshal]::PtrToStructure($buf, [type][MaxonJob+EXTENDED_LIMIT_INFORMATION])
	Write-Output ("PEAK=" + [uint64]$info.PeakProcessMemoryUsed)
	Write-Output ("EXIT=" + $p.ExitCode)
} finally {
	[System.Runtime.InteropServices.Marshal]::FreeHGlobal($buf)
	[void][MaxonJob]::CloseHandle($job)
}
