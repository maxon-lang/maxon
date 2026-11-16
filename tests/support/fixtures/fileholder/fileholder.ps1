$ErrorActionPreference = 'Stop'

$path = $args[0]
$hold = $args[1]

$held = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)

try
{
	[Console]::Out.WriteLine("holding $PID")
	[Console]::Out.Flush()

	if ($hold -eq 'untilReleased')
	{
		[void][Console]::In.ReadToEnd()
	}
	else
	{
		Start-Sleep -Milliseconds ([int]$hold)
	}
}
finally
{
	$held.Dispose()
}
