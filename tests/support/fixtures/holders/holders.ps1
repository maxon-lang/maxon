$ErrorActionPreference = 'Stop'

$probeSource = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using ComFileTime = System.Runtime.InteropServices.ComTypes.FILETIME;

public static class MaxonHolderProbe
{
	[StructLayout(LayoutKind.Sequential)]
	public struct UniqueProcess
	{
		public int ProcessId;
		public ComFileTime StartTime;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct ProcessInfo
	{
		public UniqueProcess Process;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string AppName;
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ServiceShortName;
		public int ApplicationType;
		public uint AppStatus;
		public uint SessionId;
		[MarshalAs(UnmanagedType.Bool)] public bool Restartable;
	}

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	static extern int RmStartSession(out uint session, int flags, StringBuilder key);

	[DllImport("rstrtmgr.dll")]
	static extern int RmEndSession(uint session);

	[DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
	static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint applicationCount, IntPtr applications, uint serviceCount, string[] services);

	[DllImport("rstrtmgr.dll")]
	static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] ProcessInfo[] infos, ref uint rebootReasons);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint size);

	const int MoreData = 234;
	const int SessionKeyCapacity = 33;
	const int LongPathCapacity = 32768;

	public static ProcessInfo[] Holders(string path)
	{
		uint session;
		int answer = RmStartSession(out session, 0, new StringBuilder(SessionKeyCapacity));

		if (answer != 0)
		{
			throw new Win32Exception(answer, "RmStartSession refused: " + new Win32Exception(answer).Message);
		}

		try
		{
			answer = RmRegisterResources(session, 1, new string[] { path }, 0, IntPtr.Zero, 0, null);

			if (answer != 0)
			{
				throw new Win32Exception(answer, "RmRegisterResources refused: " + new Win32Exception(answer).Message);
			}

			uint count = 0;
			uint needed;
			uint rebootReasons = 0;
			ProcessInfo[] infos = new ProcessInfo[0];

			while (true)
			{
				answer = RmGetList(session, out needed, ref count, infos, ref rebootReasons);

				if (answer == 0)
				{
					Array.Resize(ref infos, (int)count);
					return infos;
				}

				if (answer != MoreData)
				{
					throw new Win32Exception(answer, "RmGetList refused: " + new Win32Exception(answer).Message);
				}

				infos = new ProcessInfo[needed];
				count = needed;
			}
		}
		finally
		{
			RmEndSession(session);
		}
	}

	public static string StartTimeOf(ProcessInfo info)
	{
		long ticks = ((long)(uint)info.Process.StartTime.dwHighDateTime << 32) | (long)(uint)info.Process.StartTime.dwLowDateTime;
		return DateTime.FromFileTimeUtc(ticks).ToString("o");
	}

	public static string LongPathOf(string path)
	{
		StringBuilder buffer = new StringBuilder(LongPathCapacity);
		uint length = GetLongPathName(path, buffer, (uint)buffer.Capacity);

		if (length == 0 || length >= buffer.Capacity)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "GetLongPathName could not expand " + path);
		}

		return buffer.ToString();
	}
}
'@

$probeTypeFailure = $null

try
{
	Add-Type -TypeDefinition $probeSource
}
catch
{
	$probeTypeFailure = $_.Exception.Message
}

$processes = @()
$processListFailure = $null

try
{
	$processes = @(Get-CimInstance -ClassName Win32_Process)
}
catch
{
	$processListFailure = $_.Exception.Message
}

function Get-ComparablePath([string]$path)
{
	try
	{
		return [MaxonHolderProbe]::LongPathOf([System.IO.Path]::GetFullPath($path))
	}
	catch
	{
		return $path
	}
}

function Format-StartTime($process)
{
	if ($process.CreationDate)
	{
		return $process.CreationDate.ToUniversalTime().ToString('o')
	}

	return 'unreadable'
}

function Get-ImageOf([int]$processId)
{
	$listed = @($processes | Where-Object { $_.ProcessId -eq $processId })

	if ($listed.Count -eq 0)
	{
		return 'not in the process list read before the restart manager was asked'
	}

	if (-not $listed[0].ExecutablePath)
	{
		return 'unreadable'
	}

	return $listed[0].ExecutablePath
}

foreach ($path in $args)
{
	Write-Output $path

	if ($probeTypeFailure)
	{
		Write-Output "  restart manager: could not run ($probeTypeFailure)"
	}
	else
	{
		try
		{
			$holders = [MaxonHolderProbe]::Holders($path)

			if ($holders.Count -eq 0)
			{
				Write-Output '  restart manager: lists no process'
			}

			foreach ($holder in $holders)
			{
				$holderId = $holder.Process.ProcessId
				Write-Output "  restart manager: pid $holderId, started $([MaxonHolderProbe]::StartTimeOf($holder)), image $(Get-ImageOf $holderId), application '$($holder.AppName)'"
			}
		}
		catch
		{
			Write-Output "  restart manager: could not run ($($_.Exception.Message))"
		}
	}

	if ($processListFailure)
	{
		Write-Output "  image: could not list the processes ($processListFailure)"
		continue
	}

	try
	{
		$target = Get-ComparablePath $path
		$running = @($processes | Where-Object { $_.ExecutablePath -and [string]::Equals((Get-ComparablePath $_.ExecutablePath), $target, [StringComparison]::OrdinalIgnoreCase) })

		if ($running.Count -eq 0)
		{
			Write-Output '  image: no process runs it'
		}

		foreach ($process in $running)
		{
			Write-Output "  image: pid $($process.ProcessId), parent $($process.ParentProcessId), started $(Format-StartTime $process), command line $($process.CommandLine)"
		}
	}
	catch
	{
		Write-Output "  image: could not match the processes ($($_.Exception.Message))"
	}
}

if (Get-Command -Name Get-MpComputerStatus -ErrorAction SilentlyContinue)
{
	try
	{
		$defender = Get-MpComputerStatus
		$monitoring = if ($defender.RealTimeProtectionEnabled) { 'on' } else { 'off' }
		Write-Output "defender real-time monitoring: $monitoring"
	}
	catch
	{
		Write-Output "defender real-time monitoring: could not be read ($($_.Exception.Message))"
	}
}
else
{
	Write-Output 'defender real-time monitoring: this host has no Defender cmdlets'
}
