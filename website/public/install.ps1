# Install Maxon on Windows:
#
#   powershell -c "irm https://maxon.dev/install.ps1 | iex"
#
# A specific release:
#
#   & ([scriptblock]::Create((irm https://maxon.dev/install.ps1))) -Version 0.1.1
#
# Downloads the x64 Windows release from GitHub, checks it against the release's SHA256SUMS, and
# installs it into %USERPROFILE%\.maxon: the compiler in .maxon\bin, the standard library in
# .maxon\stdlib. Adds .maxon\bin to the user PATH; no administrator rights are needed. Running it
# again installs the latest release, or says the install is current.
#
# Parameters: -Version X.Y.Z, -Force (reinstall a current install), -NoPathUpdate.
# Environment: MAXON_INSTALL (default %USERPROFILE%\.maxon), MAXON_DOWNLOAD_BASE (a mirror laid out
# as <base>/v<version>/<asset>; needs -Version).
# Uninstall: delete %USERPROFILE%\.maxon and remove its bin directory from your user PATH.
#
# `maxon upgrade` RUNS THIS SCRIPT, SO ITS INTERFACE IS A CONTRACT WITH EVERY SHIPPED COMPILER: it
# honours MAXON_INSTALL and -NoPathUpdate, and sets $LASTEXITCODE to 0 when the install is current and
# 1 when it is not. Renaming or dropping any of those breaks `maxon upgrade` in every release that
# has it.
#
# ASCII ONLY: Windows PowerShell 5.1's `irm` decodes a response without a charset as ISO-8859-1.
# NEVER `exit`: under `irm | iex` this runs in the user's own session, and `exit` would close it. So
# `powershell -c "irm ... | iex"` exits 0 whatever happened; a caller that needs the status runs
#   & ([scriptblock]::Create((irm https://maxon.dev/install.ps1))); exit $LASTEXITCODE

param(
    [string]$Version = '',
    [switch]$Force,
    [switch]$NoPathUpdate
)

function Install-Maxon {
    param([string]$RequestedVersion, [bool]$Reinstall, [bool]$UpdatePath)

    $ErrorActionPreference = 'Stop'
    # The progress bar slows Invoke-WebRequest by an order of magnitude on Windows PowerShell 5.1.
    $ProgressPreference = 'SilentlyContinue'
    $repo = 'maxon-lang/maxon'
    $target = 'x64-windows'

    function Say([string]$message) { Write-Host "maxon-install: $message" }

    # A version is interpolated into URLs and file names, so anything but a version's own characters is refused.
    function Test-ReleaseVersion([string]$candidate) {
        if ($candidate -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$') {
            throw "'$candidate' is not a release version (expected X.Y.Z)"
        }
    }

    function Get-InstallRoot {
        $dir = $env:MAXON_INSTALL
        if ([string]::IsNullOrEmpty($dir)) {
            $dir = Join-Path $HOME '.maxon'
        }
        # A relative path would resolve against the process directory, which PowerShell does not keep in step with $PWD.
        if (-not [IO.Path]::IsPathRooted($dir)) {
            throw "MAXON_INSTALL must be an absolute path, not '$dir'"
        }
        $dir = [IO.Path]::GetFullPath($dir).TrimEnd('\')
        if ($dir.Length -le 2) {
            throw "MAXON_INSTALL cannot be the root of a drive"
        }
        return $dir
    }

    function Get-Architecture {
        # The registry answers for the machine; $env:PROCESSOR_ARCHITECTURE answers for this process,
        # which reports AMD64 under emulation on an ARM64 machine.
        $key = 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
        return (Get-ItemProperty -Path $key -Name PROCESSOR_ARCHITECTURE).PROCESSOR_ARCHITECTURE
    }

    # The newest release, from where GitHub redirects /releases/latest: no API call, so no rate limit.
    function Get-LatestVersion {
        $request = [Net.HttpWebRequest]::Create("https://github.com/$repo/releases/latest")
        $request.Method = 'HEAD'
        $request.AllowAutoRedirect = $false
        $request.UserAgent = 'maxon-install'
        $response = $request.GetResponse()
        try {
            $location = $response.Headers['Location']
        } finally {
            $response.Close()
        }
        if ($location -notmatch '/tag/v([^/]+)$') {
            throw "could not read a version from GitHub's latest-release redirect ('$location')"
        }
        Test-ReleaseVersion $Matches[1]
        return $Matches[1]
    }

    # Runs a program to completion and returns its exit code and standard output.
    function Invoke-Captured([string]$program, [string]$arguments) {
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = $program
        $info.Arguments = $arguments
        $info.UseShellExecute = $false
        $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true
        $info.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($info)
        # Both pipes drain at once, so a child filling one while this waits on the other cannot deadlock.
        $errors = $process.StandardError.ReadToEndAsync()
        $output = $process.StandardOutput.ReadToEnd()
        $process.WaitForExit()
        $null = $errors.Result
        return @{ ExitCode = $process.ExitCode; Output = $output }
    }

    # The version an installed compiler reports, or $null when there is none or it answers in a form
    # this script does not know.
    function Get-InstalledVersion([string]$exe) {
        if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
            return $null
        }
        try {
            $result = Invoke-Captured $exe 'version'
        } catch {
            return $null
        }
        if ($result.Output -match '^maxon (\S+) ') {
            return $Matches[1]
        }
        return $null
    }

    function Get-Download([string]$url, [string]$path) {
        try {
            Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
        } catch {
            throw "could not download $url ($($_.Exception.Message))"
        }
    }

    function Test-Checksum([string]$sums, [string]$file) {
        $name = Split-Path -Leaf $file
        $expected = $null
        foreach ($line in Get-Content -LiteralPath $sums) {
            # `*` before a name is sha256sum's binary-mode marker.
            if ($line -match '^([0-9a-fA-F]{64}) [ *]?(.+)$' -and $Matches[2] -eq $name) {
                $expected = $Matches[1].ToLowerInvariant()
            }
        }
        if ($null -eq $expected) {
            throw "SHA256SUMS lists no $name"
        }
        $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) {
            throw "$name does not match its published checksum; nothing was installed"
        }
    }

    # Antivirus scanners hold a freshly written file open for a moment, so a rename is retried briefly.
    function Move-Entry([string]$from, [string]$to) {
        for ($attempt = 1; ; $attempt++) {
            try {
                if (Test-Path -LiteralPath $from -PathType Container) {
                    [IO.Directory]::Move($from, $to)
                } else {
                    [IO.File]::Move($from, $to)
                }
                return
            } catch {
                if ($attempt -ge 5) {
                    # The .NET exception inside PowerShell's "Exception calling Move" wrapper is the one that names the cause.
                    if ($null -ne $_.Exception.InnerException) {
                        throw $_.Exception.InnerException
                    }
                    throw
                }
                Start-Sleep -Milliseconds 200
            }
        }
    }

    function Clear-Leftover([string]$path) {
        try {
            Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
        } catch {
            # A leftover still in use (a compiler that is running) is removed by the next run instead.
            Write-Verbose "left $path for the next run to remove"
        }
    }

    # A running maxon.exe cannot be replaced or deleted, but it can be renamed, and bin\ itself cannot
    # be renamed while it runs. So stdlib\ and examples\ are swapped whole, the old exe is renamed
    # aside, and the new one moved into its place.
    function Install-Release([string]$root, [string]$unpacked, [string]$id) {
        $bin = Join-Path $root 'bin'
        $exe = Join-Path $bin 'maxon.exe'
        $retired = Join-Path $root ".retired-$id"
        $null = New-Item -ItemType Directory -Path $retired
        $placed = New-Object Collections.Generic.List[string]
        $aside = $null

        try {
            foreach ($entry in @('stdlib', 'examples')) {
                $current = Join-Path $root $entry
                if (Test-Path -LiteralPath $current) {
                    Move-Entry $current (Join-Path $retired $entry)
                }
                Move-Entry (Join-Path $unpacked $entry) $current
                $placed.Add($entry)
            }

            if (Test-Path -LiteralPath $exe) {
                # Not maxon.old.exe: bin\ is on PATH, and PATHEXT would make that a runnable `maxon.old`.
                $aside = "$exe.old"
                for ($n = 1; Test-Path -LiteralPath $aside; $n++) {
                    $aside = "$exe.old$n"
                }
                Move-Entry $exe $aside
            }
            Move-Entry (Join-Path $unpacked 'maxon.exe') $exe
        } catch {
            $reason = $_.Exception.Message
            if ($null -ne $aside -and -not (Test-Path -LiteralPath $exe)) {
                Move-Entry $aside $exe
            }
            for ($i = $placed.Count - 1; $i -ge 0; $i--) {
                Remove-Item -LiteralPath (Join-Path $root $placed[$i]) -Recurse -Force
            }
            foreach ($entry in @('stdlib', 'examples')) {
                $saved = Join-Path $retired $entry
                if (Test-Path -LiteralPath $saved) {
                    Move-Entry $saved (Join-Path $root $entry)
                }
            }
            Clear-Leftover $retired
            throw "could not move the new release into $root, so the previous install is unchanged. Close running Maxon processes, the VS Code language server included, and run this again. ($reason)"
        }

        Clear-Leftover $retired
        if ($null -ne $aside) {
            Clear-Leftover $aside
        }
    }

    function Add-ToUserPath([string]$dir) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
        try {
            $names = $key.GetValueNames()
            $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
            $current = ''
            if ($names -contains 'Path') {
                $kind = $key.GetValueKind('Path')
                # Read unexpanded, so %VARIABLE% entries survive the rewrite.
                $current = $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            }

            $wanted = $dir.TrimEnd('\')
            foreach ($entry in ($current -split ';')) {
                $expanded = [Environment]::ExpandEnvironmentVariables($entry.Trim()).TrimEnd('\')
                if ($expanded -ieq $wanted) {
                    return $false
                }
            }

            $updated = if ($current.Trim(';').Length -eq 0) { $dir } else { $current.TrimEnd(';') + ';' + $dir }
            $key.SetValue('Path', $updated, $kind)
        } finally {
            $key.Close()
        }

        # Setting any user variable through .NET broadcasts WM_SETTINGCHANGE, which is what makes new
        # terminals started from Explorer read the new PATH. The registry write above does not, and
        # writing Path itself through .NET would flatten a REG_EXPAND_SZ value to REG_SZ.
        [Environment]::SetEnvironmentVariable('MAXON_INSTALL_REFRESH', '1', 'User')
        [Environment]::SetEnvironmentVariable('MAXON_INSTALL_REFRESH', $null, 'User')
        return $true
    }

    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
        throw 'this installer is for Windows; on macOS or Linux run: curl -fsSL https://maxon.dev/install.sh | sh'
    }
    $architecture = Get-Architecture
    if ($architecture -ne 'AMD64') {
        throw "there is no Maxon build for Windows on $architecture"
    }

    $root = Get-InstallRoot
    $bin = Join-Path $root 'bin'
    $exe = Join-Path $bin 'maxon.exe'

    $pinned = -not [string]::IsNullOrEmpty($RequestedVersion)
    if ($pinned) {
        $RequestedVersion = $RequestedVersion -replace '^v', ''
        Test-ReleaseVersion $RequestedVersion
    }

    if (-not [string]::IsNullOrEmpty($env:MAXON_DOWNLOAD_BASE)) {
        $base = $env:MAXON_DOWNLOAD_BASE.TrimEnd('/')
        if (-not $pinned) {
            throw "MAXON_DOWNLOAD_BASE needs -Version: a mirror has no 'latest' to ask"
        }
    } else {
        $base = "https://github.com/$repo/releases/download"
    }
    $release = if ($pinned) { $RequestedVersion } else { Get-LatestVersion }

    if (-not $Reinstall -and (Test-Path -LiteralPath (Join-Path $root 'stdlib') -PathType Container) -and (Get-InstalledVersion $exe) -eq $release) {
        if ($pinned) {
            Say "Maxon $release is already installed in $root (-Force reinstalls it)"
        } else {
            Say "Maxon $release, the latest release, is already installed in $root (-Force reinstalls it)"
        }
    } else {
        # A directory that already holds a stdlib\ or examples\ of its own is not ours to replace.
        if (-not (Test-Path -LiteralPath $exe)) {
            foreach ($entry in @('stdlib', 'examples')) {
                if (Test-Path -LiteralPath (Join-Path $root $entry)) {
                    throw "$root\$entry exists and $root holds no bin\maxon.exe, so it is not a Maxon install; set MAXON_INSTALL to a new directory"
                }
            }
        }

        Say "installing Maxon $release for $target into $root"
        $null = New-Item -ItemType Directory -Path $bin -Force

        # Leftovers from an earlier run, including an old maxon.exe that was still running then.
        Get-ChildItem -LiteralPath $bin -Filter 'maxon.exe.old*' -Force | ForEach-Object { Clear-Leftover $_.FullName }
        Get-ChildItem -LiteralPath $root -Force | Where-Object { $_.Name -like '.staging-*' -or $_.Name -like '.retired-*' } |
            ForEach-Object { Clear-Leftover $_.FullName }

        # Staged beside the install so every move below stays on one volume, where it is a rename.
        $id = [Guid]::NewGuid().ToString('N').Substring(0, 8)
        $staging = Join-Path $root ".staging-$id"
        $null = New-Item -ItemType Directory -Path $staging
        try {
            $asset = "maxon-$release-$target.zip"
            $zip = Join-Path $staging $asset
            $sums = Join-Path $staging 'SHA256SUMS'
            Get-Download "$base/v$release/$asset" $zip
            Get-Download "$base/v$release/SHA256SUMS" $sums
            Test-Checksum $sums $zip

            $expanded = Join-Path $staging 'expanded'
            # ZipFile rather than Expand-Archive, which takes minutes over the stdlib on Windows PowerShell 5.1.
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $expanded)
            $unpacked = Join-Path $expanded "maxon-$release-$target"
            foreach ($required in @('maxon.exe', 'stdlib', 'examples')) {
                if (-not (Test-Path -LiteralPath (Join-Path $unpacked $required))) {
                    throw "$asset does not hold maxon.exe, stdlib\ and examples\ in maxon-$release-$target\"
                }
            }

            # A negative exit code is an NTSTATUS from the loader: the image did not start at all. Any
            # other status is the compiler running and answering, and older releases answer `version` differently.
            try {
                $smoke = Invoke-Captured (Join-Path $unpacked 'maxon.exe') 'version'
            } catch {
                throw "the downloaded compiler does not run on this machine ($($_.Exception.Message))"
            }
            if ($smoke.ExitCode -lt 0) {
                throw ('the downloaded compiler does not run on this machine (exit 0x{0:X8})' -f $smoke.ExitCode)
            }

            Install-Release $root $unpacked $id
        } finally {
            Clear-Leftover $staging
        }

        # $null is a release that answers `version` in a form this script does not read.
        $reported = Get-InstalledVersion $exe
        if ($null -ne $reported -and $reported -ne $release) {
            throw "the installed compiler reports $reported, not $release"
        }
        Say "installed Maxon $release in $root"
    }

    $onSessionPath = $false
    foreach ($entry in ($env:Path -split ';')) {
        if ($entry.Trim().TrimEnd('\') -ieq $bin) {
            $onSessionPath = $true
        }
    }

    if ($UpdatePath) {
        if (Add-ToUserPath $bin) {
            Say "added $bin to your user PATH; terminals opened from now on find ``maxon``"
        }
        if (-not $onSessionPath) {
            # Appended, not prepended: the same precedence a new terminal will give it.
            $env:Path = $env:Path.TrimEnd(';') + ';' + $bin
        }
    } elseif (-not $onSessionPath) {
        Say "$bin is not on your PATH; add it to run ``maxon`` by name"
    }

    $found = Get-Command maxon -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $found -and $found.Source -ine $exe) {
        Say "warning: $($found.Source) comes first on your PATH, so ``maxon`` runs that one"
    }
}

& {
    try {
        Install-Maxon -RequestedVersion $Version -Reinstall $Force.IsPresent -UpdatePath (-not $NoPathUpdate.IsPresent)
        $global:LASTEXITCODE = 0
    } catch {
        $global:LASTEXITCODE = 1
        [Console]::Error.WriteLine("maxon-install: $($_.Exception.Message)")
    }
}
Remove-Item -Path Function:\Install-Maxon
