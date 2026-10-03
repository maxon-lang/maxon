---
feature: windows-wide-text
status: stable
keywords: [windows, utf-8, utf-16, unicode, command line, environment, file names, working directory, executable path, subprocess]
category: stdlib
---

# Text crossing the Windows boundary is UTF-8 inside a program and UTF-16 outside it

## Documentation

A Maxon `String` is UTF-8. Windows names files, directories, environment variables, command-line
arguments and the working directory in UTF-16, and a program that is not Maxon — a shell, an editor, a
.NET tool — reads and writes them in UTF-16. So every text value that crosses the boundary between a
Maxon program on Windows and the operating system is converted between the two: a name Maxon hands to
Windows reaches it as the same characters, and a name Windows hands to Maxon arrives as the same
characters in UTF-8.

The boundaries are:

| Boundary | Maxon side |
|---|---|
| the program's own arguments | `CommandLine.args()` |
| the program's own environment | `Process.environmentVariable`, `Process.currentEnvironmentEntries` |
| a child's arguments, environment and working directory | `Subprocess`, `Configuration` |
| file and directory names | `File.*`, `Directory.*` |
| the working directory | `Directory.currentPath()` |
| the program's own path | `Process.executablePath()` |

A conversion through the system's ANSI code page instead would round-trip between two Maxon programs,
because that code page maps every byte, and would still turn a character outside the code page into `?`
for every other program. So each case below has a UTF-16 program on the other side of the boundary:
`powershell.exe`. Its scripts are pure ASCII — they build the text `日本` (U+65E5 U+672C, outside every
Western code page) from its code points — and report what they see as code points in ASCII, so the
encoding of the pipe between the two programs cannot change the answer. Where a Maxon program relays
its own view through PowerShell's pipe it prints code points too, with the text's byte count; where it
prints directly it prints the text and its byte count.

## Targets

<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
Only the x64-windows lane runs these cases: each one drives `powershell.exe`, which only a Windows host
has, and the UTF-16 boundary they cross is Windows' own.

## Tests

<!-- test: an-argument-from-a-wide-parent-arrives-as-utf8 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell starts the program with the argument `日本`, and `CommandLine.args()` hands it over as the
six bytes of its UTF-8 spelling.
```maxon
function codepointsOf(text String) returns String
	var out = ""
	for cp in text.codepoints() 'each'
		if out.byteLength() > 0 'separator'
			out.append(",")
		end 'separator'
		out.append("{cp}")
	end 'each'
	return out
end 'codepointsOf'

function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		let received = try args.get(1) otherwise return 3
		print("{codepointsOf(received)} {received.byteLength()}\n")
		return 0
	end 'child'

	let me = try Process.executablePath() otherwise return 2
	printLines(powershell("& '{me.path}' ([string]([char]0x65E5) + [char]0x672C)"))
	return 0
end 'main'
```
```stdout
26085,26412 6
```
```exitcode
0
```

<!-- test: an-environment-variable-from-a-wide-parent-arrives-as-utf8 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell sets `MAXON_WIDE_TEXT` to `日本` and starts the program, and `Process.environmentVariable`
hands the value over as the six bytes of its UTF-8 spelling.
```maxon
function codepointsOf(text String) returns String
	var out = ""
	for cp in text.codepoints() 'each'
		if out.byteLength() > 0 'separator'
			out.append(",")
		end 'separator'
		out.append("{cp}")
	end 'each'
	return out
end 'codepointsOf'

function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		let value = try Process.environmentVariable("MAXON_WIDE_TEXT") otherwise "unset"
		print("{codepointsOf(value)} {value.byteLength()}\n")
		return 0
	end 'child'

	let me = try Process.executablePath() otherwise return 2
	printLines(powershell("$env:MAXON_WIDE_TEXT = [string]([char]0x65E5) + [char]0x672C; & '{me.path}' child"))
	return 0
end 'main'
```
```stdout
26085,26412 6
```
```exitcode
0
```

<!-- test: an-argument-handed-to-a-child-reaches-it-in-utf16 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
The program hands PowerShell the argument `'日本'`, and PowerShell reads the two characters U+65E5 U+672C.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function main() returns ExitCode
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push("& \{ ([int[]][char[]]$args[0]) -join ',' \}")
	argv.push("'日本'")
	let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) otherwise return 2
	printLines(result.stdout)
	return result.exitCode() as ExitCode
end 'main'
```
```stdout
26085,26412
```
```exitcode
0
```

<!-- test: an-environment-variable-handed-to-a-child-reaches-it-in-utf16 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
The program starts PowerShell with `MAXON_WIDE_TEXT` set to `日本` through `Environment.inheritUpdating`,
and PowerShell reads the two characters U+65E5 U+672C.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function main() returns ExitCode
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push("([int[]][char[]]$env:MAXON_WIDE_TEXT) -join ','")
	var config = Configuration.create(Executable.name("powershell"))
	config.arguments = argv
	config.environment = Environment.inheritUpdating(["MAXON_WIDE_TEXT": "日本"])
	let result = try config.run() otherwise return 2
	printLines(result.stdout)
	return result.exitCode() as ExitCode
end 'main'
```
```stdout
26085,26412
```
```exitcode
0
```

<!-- test: a-file-maxon-names-is-so-named-on-disk -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
The program writes `日本.txt`, and PowerShell finds a file of exactly that name in the directory.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_maxon_names_a_file")
	removeTree(base)
	_ = Directory.create(base)
	try File.writeText(base.join("日本.txt"), content: "ok") otherwise 'writeFailed'
		print("write failed\n")
	end 'writeFailed'
	printLines(powershell("Get-ChildItem -LiteralPath '{base.path}' | ForEach-Object \{ ([int[]][char[]]$_.Name) -join ',' \}"))
	removeTree(base)
	return 0
end 'main'
```
```stdout
26085,26412,46,116,120,116
```
```exitcode
0
```

<!-- test: a-file-named-on-disk-is-listed-and-opened-in-utf8 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell makes a directory `日本` and a file `日本.txt` holding `ok`. `Directory.list` names both in
UTF-8, `File.exists`, `Directory.exists` and `File.readText` find them by their UTF-8 names, and
`File.delete` and `Directory.delete` remove them.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_disk_names_a_file")
	removeTree(base)
	printLines(powershell("$w = [string]([char]0x65E5) + [char]0x672C; New-Item -ItemType Directory -Force -Path (Join-Path '{base.path}' $w) | Out-Null; Set-Content -LiteralPath (Join-Path '{base.path}' ($w + '.txt')) -Value 'ok' -NoNewline"))

	let entries = try Directory.list(base) otherwise return 2
	for entry in entries 'each'
		let name = entry.filename()
		print("{name} {name.byteLength()}\n")
	end 'each'

	let file = base.join("日本.txt")
	let directory = base.join("日本")
	print("file exists {File.exists(file)}\n")
	print("directory exists {Directory.exists(directory)}\n")
	let content = try File.readText(file) otherwise "read failed"
	print("content {content}\n")
	try File.delete(file) otherwise 'fileDeleteFailed'
		print("file delete failed\n")
	end 'fileDeleteFailed'
	try Directory.delete(directory) otherwise 'directoryDeleteFailed'
		print("directory delete failed\n")
	end 'directoryDeleteFailed'
	printLines(powershell("(Get-ChildItem -LiteralPath '{base.path}' | Measure-Object).Count"))
	removeTree(base)
	return 0
end 'main'
```
```stdout
日本 6
日本.txt 10
file exists true
directory exists true
content ok
0
```
```exitcode
0
```

<!-- test: a-directory-maxon-creates-renames-and-removes-is-named-so-on-disk -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
The program makes a directory `日本`, writes `日本.txt` inside it and renames that to `本日.txt`, and
PowerShell finds exactly those names on disk. The program then removes both, and PowerShell finds
nothing left.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_maxon_names_a_directory")
	removeTree(base)
	let directory = base.join("日本")
	_ = Directory.create(directory)
	let original = directory.join("日本.txt")
	let renamed = directory.join("本日.txt")
	try File.writeText(original, content: "ok") otherwise 'writeFailed'
		print("write failed\n")
	end 'writeFailed'
	try File.rename(original, to: renamed) otherwise 'renameFailed'
		print("rename failed\n")
	end 'renameFailed'
	printLines(powershell("Get-ChildItem -LiteralPath '{base.path}' -Recurse | ForEach-Object \{ ([int[]][char[]]$_.Name) -join ',' \}"))
	try File.delete(renamed) otherwise 'fileDeleteFailed'
		print("file delete failed\n")
	end 'fileDeleteFailed'
	try Directory.delete(directory) otherwise 'directoryDeleteFailed'
		print("directory delete failed\n")
	end 'directoryDeleteFailed'
	printLines(powershell("(Get-ChildItem -LiteralPath '{base.path}' -Recurse | Measure-Object).Count"))
	removeTree(base)
	return 0
end 'main'
```
```stdout
26085,26412
26412,26085,46,116,120,116
0
```
```exitcode
0
```

<!-- test: the-current-directory-and-the-executable-path-come-back-in-utf8 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell copies the program into a directory `日本` and runs the copy from inside it.
`Directory.currentPath()` and `Process.executablePath()` both name that directory in UTF-8.
```maxon
function codepointsOf(text String) returns String
	var out = ""
	for cp in text.codepoints() 'each'
		if out.byteLength() > 0 'separator'
			out.append(",")
		end 'separator'
		out.append("{cp}")
	end 'each'
	return out
end 'codepointsOf'

function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		if let exe = try Process.executablePath() 'found'
			let exeDirectory = (try exe.parent() otherwise return 4).filename()
			print("executable directory {codepointsOf(exeDirectory)} {exeDirectory.byteLength()}\n")
		end 'found' else 'unavailable'
			print("executable path unavailable\n")
		end 'unavailable'
		let current = Directory.currentPath().filename()
		print("current directory {codepointsOf(current)} {current.byteLength()}\n")
		return 0
	end 'child'

	let me = try Process.executablePath() otherwise return 2
	let base = Directory.currentPath().join("wide_text_current_directory")
	removeTree(base)
	printLines(powershell("$d = Join-Path '{base.path}' ([string]([char]0x65E5) + [char]0x672C); New-Item -ItemType Directory -Force -Path $d | Out-Null; $copy = Join-Path $d 'copy.exe'; Copy-Item -LiteralPath '{me.path}' -Destination $copy; Set-Location -LiteralPath $d; & $copy child"))
	removeTree(base)
	return 0
end 'main'
```
```stdout
executable directory 26085,26412 6
current directory 26085,26412 6
```
```exitcode
0
```

<!-- test: a-child-started-in-a-utf8-working-directory-sees-it-in-utf16 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell makes a directory `日本`, and the program starts PowerShell again with that directory as its
working directory. PowerShell finds itself in a directory named U+65E5 U+672C.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String, workingDirectory FilePath) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var config = Configuration.create(Executable.name("powershell"))
	config.arguments = argv
	config.workingDirectory = workingDirectory
	var outcome = ""
	if let result = try config.run() 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function main() returns ExitCode
	let here = Directory.currentPath()
	let base = here.join("wide_text_child_working_directory")
	let cleanup = "if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"
	printLines(powershell(cleanup, workingDirectory: here))
	printLines(powershell("New-Item -ItemType Directory -Force -Path (Join-Path '{base.path}' ([string]([char]0x65E5) + [char]0x672C)) | Out-Null", workingDirectory: here))
	printLines(powershell("([int[]][char[]](Split-Path -Leaf (Get-Location).Path)) -join ','", workingDirectory: base.join("日本")))
	printLines(powershell(cleanup, workingDirectory: here))
	return 0
end 'main'
```
```stdout
26085,26412
```
```exitcode
0
```

<!-- test: a-supplementary-plane-argument-arrives-as-utf8 -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell starts the program with the argument U+1F600, which UTF-16 spells as the surrogate pair
D83D DE00. `CommandLine.args()` hands it over as one code point in four UTF-8 bytes.
```maxon
function codepointsOf(text String) returns String
	var out = ""
	for cp in text.codepoints() 'each'
		if out.byteLength() > 0 'separator'
			out.append(",")
		end 'separator'
		out.append("{cp}")
	end 'each'
	return out
end 'codepointsOf'

function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		let received = try args.get(1) otherwise return 3
		print("{codepointsOf(received)} {received.byteLength()}\n")
		return 0
	end 'child'

	let me = try Process.executablePath() otherwise return 2
	printLines(powershell("& '{me.path}' ([string]([char]0xD83D) + [char]0xDE00)"))
	return 0
end 'main'
```
```stdout
128512 4
```
```exitcode
0
```

<!-- test: a-file-name-with-a-lone-surrogate-is-listed-as-the-replacement-character -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
NTFS stores a name as UTF-16 units without checking that they pair, so PowerShell can make a file named
`a`, a lone high surrogate D800, then `b.txt`. UTF-8 has no spelling for a lone surrogate, so
`Directory.list` names it with U+FFFD REPLACEMENT CHARACTER in its place.
```maxon
function codepointsOf(text String) returns String
	var out = ""
	for cp in text.codepoints() 'each'
		if out.byteLength() > 0 'separator'
			out.append(",")
		end 'separator'
		out.append("{cp}")
	end 'each'
	return out
end 'codepointsOf'

function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_lone_surrogate")
	removeTree(base)
	printLines(powershell("New-Item -ItemType Directory -Force -Path '{base.path}' | Out-Null; [System.IO.File]::WriteAllText((Join-Path '{base.path}' ('a' + [char]0xD800 + 'b.txt')), 'ok')"))
	let entries = try Directory.list(base) otherwise return 2
	for entry in entries 'each'
		let name = entry.filename()
		print("{codepointsOf(name)} {name.byteLength()}\n")
	end 'each'
	removeTree(base)
	return 0
end 'main'
```
```stdout
97,65533,98,46,116,120,116 9
```
```exitcode
0
```

<!-- test: a-name-of-the-longest-component-is-listed-whole -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
NTFS allows a name component of 255 UTF-16 units. PowerShell makes a file named with 255 of U+65E5, and
`Directory.list` names it whole: 255 code points in 765 UTF-8 bytes. The full path is longer than
`MAX_PATH`, so PowerShell reaches it through the `\\?\` prefix; the directory `Directory.list` opens is
short.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ [System.IO.Directory]::Delete('\\\\?\\{base.path}', $true) \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_longest_component")
	removeTree(base)
	printLines(powershell("New-Item -ItemType Directory -Force -Path '{base.path}' | Out-Null; [System.IO.File]::WriteAllText('\\\\?\\{base.path}\\' + ([string][char]0x65E5 * 255), 'ok')"))
	let entries = try Directory.list(base) otherwise return 2
	for entry in entries 'each'
		let name = entry.filename()
		var units = 0
		for _ in name.codepoints() 'count'
			units = units + 1
		end 'count'
		print("{units} code points {name.byteLength()} bytes\n")
	end 'each'
	removeTree(base)
	return 0
end 'main'
```
```stdout
255 code points 765 bytes
```
```exitcode
0
```

<!-- test: a-program-in-a-utf8-path-directory-is-found-by-name -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell copies the program into a directory `日本` as `maxon-wide-probe.exe`. The program starts
itself with that directory at the head of `PATH`, and that child starts `maxon-wide-probe` by its bare
name: the `PATH` search walks the `日本` directory and finds it.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function outcomeOf(config Configuration) returns String
	var outcome = ""
	if let result = try config.run() 'ran'
		outcome = result.stdout
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'outcomeOf'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		let role = try args.get(1) otherwise return 3
		if role == "probe" 'probe'
			print("found\n")
			return 0
		end 'probe'

		var argv = StringArray.create()
		argv.push("probe")
		var probe = Configuration.create(Executable.name("maxon-wide-probe"))
		probe.arguments = argv
		print(outcomeOf(probe))
		return 0
	end 'child'

	let me = try Process.executablePath() otherwise return 2
	let base = Directory.currentPath().join("wide_text_path_search")
	let directory = base.join("日本")
	removeTree(base)
	printLines(powershell("$d = Join-Path '{base.path}' ([string]([char]0x65E5) + [char]0x672C); New-Item -ItemType Directory -Force -Path $d | Out-Null; Copy-Item -LiteralPath '{me.path}' -Destination (Join-Path $d 'maxon-wide-probe.exe')"))

	let inheritedPath = try Process.environmentVariable("PATH") otherwise return 4
	var overrides = EnvMap.create()
	overrides.upsert("PATH", value: "{directory.path};{inheritedPath}")
	var argv = StringArray.create()
	argv.push("resolve")
	var resolver = Configuration.create(Executable.path(me))
	resolver.arguments = argv
	resolver.environment = Environment.inheritUpdating(overrides)
	print(outcomeOf(resolver))
	removeTree(base)
	return 0
end 'main'
```
```stdout
found
```
```exitcode
0
```

<!-- test: a-redirect-file-with-a-utf8-name-is-written-and-read -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell writes `ping` into `本日.txt`. The program starts PowerShell again with its standard input
read from `本日.txt` (`InputSource.file`) and its standard output written to `日本.txt`
(`OutputDestination.file`), and the child copies one to the other. PowerShell then finds both names on
disk, and `ping` in `日本.txt`.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershellConfiguration(script String) returns Configuration
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var config = Configuration.create(Executable.name("powershell"))
	config.arguments = argv
	return config
end 'powershellConfiguration'

function outcomeOf(config Configuration) returns String
	var outcome = ""
	if let result = try config.run() 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'outcomeOf'

function powershell(script String) returns String
	return outcomeOf(powershellConfiguration(script))
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_redirect_files")
	removeTree(base)
	printLines(powershell("New-Item -ItemType Directory -Force -Path '{base.path}' | Out-Null; Set-Content -LiteralPath (Join-Path '{base.path}' ([string]([char]0x672C) + [char]0x65E5 + '.txt')) -Value 'ping' -NoNewline"))

	var copier = powershellConfiguration("[Console]::Out.Write([Console]::In.ReadToEnd())")
	copier.standardInput = InputSource.file(base.join("本日.txt"))
	copier.standardOutput = OutputDestination.file(base.join("日本.txt"))
	printLines(outcomeOf(copier))

	printLines(powershell("Get-ChildItem -LiteralPath '{base.path}' | ForEach-Object \{ ([int[]][char[]]$_.Name) -join ',' \}; Get-Content -LiteralPath (Join-Path '{base.path}' ([string]([char]0x65E5) + [char]0x672C + '.txt'))"))
	removeTree(base)
	return 0
end 'main'
```
```stdout
26085,26412,46,116,120,116
26412,26085,46,116,120,116
ping
```
```exitcode
0
```

<!-- test: a-redirect-to-a-utf8-name-that-cannot-open-reports-the-failure -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell makes a directory `日本` holding two directories, `in.txt` and `out.txt`. A standard input
read from `日本\in.txt` and a standard output written to `日本\out.txt` both name a directory, which no
stream can open: the spawn fails as `spawnFailed`, carrying the access-denied code (5) Windows answers
for a directory opened as a file, and launches nothing.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershellConfiguration(script String) returns Configuration
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var config = Configuration.create(Executable.name("powershell"))
	config.arguments = argv
	return config
end 'powershellConfiguration'

function outcomeOf(config Configuration) returns String
	var outcome = ""
	if let result = try config.run() 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'outcomeOf'

function powershell(script String) returns String
	return outcomeOf(powershellConfiguration(script))
end 'powershell'

function removeTree(base FilePath)
	printLines(powershell("if (Test-Path -LiteralPath '{base.path}') \{ Remove-Item -LiteralPath '{base.path}' -Recurse -Force \}"))
end 'removeTree'

function main() returns ExitCode
	let base = Directory.currentPath().join("wide_text_redirect_refused")
	let directory = base.join("日本")
	removeTree(base)
	printLines(powershell("$d = Join-Path '{base.path}' ([string]([char]0x65E5) + [char]0x672C); New-Item -ItemType Directory -Force -Path (Join-Path $d 'in.txt') | Out-Null; New-Item -ItemType Directory -Force -Path (Join-Path $d 'out.txt') | Out-Null"))

	var reader = powershellConfiguration("'launched'")
	reader.standardInput = InputSource.file(directory.join("in.txt"))
	print("stdin {outcomeOf(reader)}")

	var writer = powershellConfiguration("'launched'")
	writer.standardOutput = OutputDestination.file(directory.join("out.txt"))
	print("stdout {outcomeOf(writer)}")
	removeTree(base)
	return 0
end 'main'
```
```stdout
stdin spawn failed: os error 5
stdout spawn failed: os error 5
```
```exitcode
0
```

<!-- test: a-shared-memory-segment-named-in-utf8-is-adopted-under-that-name -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
PowerShell publishes a named section `maxon-wide-日本` through .NET's `MemoryMappedFile`, writes 42 into
its first word and starts the program. `SharedSegment.create` under the UTF-8 name adopts that section:
it reads the 42 and writes 7 into the second word, which PowerShell reads back once the program exits.
```maxon
function printLines(text String)
	for line in text.split("\n") 'each'
		let trimmed = line.trim()
		if trimmed.byteLength() > 0 'nonEmpty'
			print("{trimmed}\n")
		end 'nonEmpty'
	end 'each'
end 'printLines'

function powershell(script String) returns String
	var argv = StringArray.create()
	argv.push("-NoProfile")
	argv.push("-NonInteractive")
	argv.push("-Command")
	argv.push(script)
	var outcome = ""
	if let result = try Subprocess.run(Executable.name("powershell"), arguments: argv) 'ran'
		outcome = result.stdout
		if not result.succeeded() 'failed'
			outcome.append("powershell exit {result.exitCode()}: {result.stderr}\n")
		end 'failed'
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'powershell'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		var segment = try SharedSegment.create("maxon-wide-日本", bytes: 4096) otherwise return 3
		let first = try segment.readWord(0) otherwise return 4
		try segment.writeWord(8, value: 7) otherwise return 5
		segment.close()
		print("adopted {first}\n")
		return 0
	end 'child'

	let me = try Process.executablePath() otherwise return 2
	printLines(powershell("$m = [System.IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew('maxon-wide-' + [char]0x65E5 + [char]0x672C, 4096); $v = $m.CreateViewAccessor(); $v.Write(0, [long]42); & '{me.path}' child; 'published ' + $v.ReadInt64(8); $v.Dispose(); $m.Dispose()"))
	return 0
end 'main'
```
```stdout
adopted 42
published 7
```
```exitcode
0
```

<!-- test: a-path-holding-a-nul-names-no-file -->
<!-- unsupported-targets: x64-linux, arm64-linux, arm64-macos, wasm32-wasi -->
Windows names no file with a NUL in it, so a path holding one names nothing at all — never the file named by
the characters before the NUL. `FilePath.from` refuses such a spelling as an invalid character, and a path
that reaches one through `join` is a file that does not exist.
```maxon
function main() returns ExitCode
	if let accepted = try FilePath.from("windows-wide-nul\0probe.txt") 'accepted'
		print("accepted {accepted.path.byteLength()}\n")
	end 'accepted' else (e) 'refused'
		match e 'why'
			invalidCharacter then print("invalidCharacter\n")
			default panic("FilePath.from refused a NUL for another reason")
		end 'why'
	end 'refused'

	let probe = try FilePath.from("windows-wide-nul") otherwise return 2
	try File.writeText(probe, content: "x") otherwise return 3
	let here = try FilePath.from(".") otherwise return 4
	print("{File.exists(here.join("windows-wide-nul"))}\n")
	print("{File.exists(here.join("windows-wide-nul\0probe.txt"))}\n")
	try File.delete(probe) otherwise return 5
	return 0
end 'main'
```
```stdout
invalidCharacter
true
false
```
```exitcode
0
```
