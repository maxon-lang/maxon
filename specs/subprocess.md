---
feature: subprocess
status: stable
keywords: [subprocess, process, spawn, stdin, stdout, stderr, timeout, exit-code, streaming, async, stdlib]
category: stdlib
---

# Subprocess — the stdlib API

## Documentation

### Overview

`Subprocess` is the synchronous child-process API. The hot path is
`Subprocess.run(.name("git"), arguments: ["status"])`, which captures
stdout/stderr into a `CollectedOutput`. For more control, build a
`Configuration` and call `.run()`.

### What this file owns, and what it does not

`subprocess-builtins.md` and `streaming-subprocess.md` drive the RAW
INTRINSIC floor — `__Builtins.subprocessSpawn`, `subprocessWaitCollect`,
`subpReadLine` — and assert what the runtime does with a handle. This file
drives the layer ABOVE it, `stdlib/Subprocess.maxon`'s public surface:
`Subprocess.run` and its `workingDirectory:` / `timeoutMs:` overloads,
`Executable.name` / `Executable.path`, `Configuration` with `InputSource`,
`CollectedOutput` and `match TerminationStatus`, the `SubprocessError` arms,
and `StreamingSubprocess`. A spawn that works at the intrinsic and fails here
is a stdlib-layer defect, which is a distinction the intrinsic cases cannot
draw.

### Reading a streaming child line by line

`StreamingSubprocess.readStdoutLine()` / `readStderrLine()` and their `…Capped(maxBytes)` forms answer
one line. A blank line is `""`; the end of the stream throws `SubprocessError.endOfStream`.
`SubprocessError` answers `timedOut()` for `timeout` and `endedTheStream()` for `endOfStream`.

### Portability

Every case branches on `#if os(Windows)` only to name the shell, so one
program covers both host families. The `unsupported-targets:` restriction on each case
excludes `wasm32-wasi` alone, where the whole API is refused at compile time
with E3074 because WASI has no process-spawn primitive
(`subprocess-unsupported.md` pins that refusal).

### Two the compiler spellings

`__Builtins.writeStdout` / `writeStderr` take a `__ManagedMemory`, so a
`ByteArray` is handed over as `bytes.managed`, and the byte count they answer
is an impure result that a statement-position call must discard with `_ =`.

## Tests

These tests verify the synchronous Subprocess path — spawn, working
directory, stdin bytes, output capture, timeout, exit code, stderr-only
collection, and capture of a child that outruns the OS pipe buffer on both
streams at once — plus the streaming handle, the async spawn, and the
byte-transparency of both capture paths.

<!-- test: subprocess-run-collect -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("hello")
	#else
	let exe = Executable.path(try FilePath.from("/bin/echo") otherwise return 2)
	var argv = StringArray.create()
	argv.push("hello")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	if not result.stdout.contains("hello") 'check-stdout'
		return 4
	end 'check-stdout'
	if result.exitCode() != 0 'check-exit'
		return 5
	end 'check-exit'
	let statusCode = match result.status 'status'
		exited(c) gives c
		signalled(c) gives c
	end 'status'
	if statusCode != 0 'check-status'
		return 6
	end 'check-status'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-run-path -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.path(try FilePath.from("C:/Windows/System32/cmd.exe") otherwise return 2)
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("via-path")
	#else
	let exe = Executable.path(try FilePath.from("/bin/echo") otherwise return 2)
	var argv = StringArray.create()
	argv.push("via-path")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 3
	if not result.succeeded() 'check-success'
		return 4
	end 'check-success'
	if not result.stdout.contains("via-path") 'check-stdout'
		return 5
	end 'check-stdout'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-cwd -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	let cwd = Directory.currentPath()
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("cd")
	#else
	let exe = Executable.path(try FilePath.from("/bin/pwd") otherwise return 2)
	var argv = StringArray.create()
	#endif
	let result = try Subprocess.run(exe, arguments: argv, workingDirectory: cwd) otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	// `cmd /c cd` (Windows) / `pwd` (POSIX) prints the working directory it
	// inherited from us. Compare case-insensitively: Windows paths are
	// case-insensitive, and the child may report the on-disk casing while our
	// process inherited a differently-cased cwd string from its launcher (the
	// mismatch is real — e.g. a shell started in `...\dev\maxon` on a disk
	// whose directory is `...\Dev\maxon`).
	if not result.stdout.toLower().contains(cwd.path.toLower()) 'check-cwd'
		return 4
	end 'check-cwd'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-stdin-bytes -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	// `findstr .` (Windows) / `grep .` (POSIX) echoes any line containing at
	// least one character — i.e. every non-empty line of stdin. We feed it two
	// lines and expect both back on stdout.
	#if os(Windows)
	var c = Configuration.create(Executable.name("findstr"))
	#else
	var c = Configuration.create(Executable.path(try FilePath.from("/usr/bin/grep") otherwise return 2))
	#endif
	var argv = StringArray.create()
	argv.push(".")
	c.arguments = argv
	c.standardInput = InputSource.bytes("abc\ndef\n")
	let result = try c.run() otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	if not result.stdout.contains("abc") 'check-abc'
		return 4
	end 'check-abc'
	if not result.stdout.contains("def") 'check-def'
		return 5
	end 'check-def'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-delayed-stdin-needs-collected-stdout -->
<!-- unsupported-targets: wasm32-wasi -->
`InputSource.delayed` writes its line one second after the child's FIRST STDOUT BYTE, and the parent can
only see that byte when it is collecting stdout. Any other destination would leave the feed with nothing
to anchor on, so the configuration is refused up front, as `spawnFailed` with the reason below — BEFORE
any spawn, which the executable name proves: it exists nowhere, and a spawn attempt would have failed on
it with a different reason.
```maxon
function main() returns ExitCode
	var c = Configuration.create(Executable.name("definitely-not-a-real-binary-xyzzy"))
	c.standardInput = InputSource.delayed("hello\n")
	c.standardOutput = OutputDestination.discard
	try Subprocess.runConfiguration(c) otherwise (e) 'refused'
		print("{e.displayReason()}\n")
		return 0
	end 'refused'
	print("spawned\n")
	return 1
end 'main'
```
```stdout
spawn failed: InputSource.delayed needs OutputDestination.collect on stdout: the line is fed after the child's first stdout byte, which no other destination lets the parent observe
```
```exitcode
0
```

<!-- test: subprocess-streaming-roundtrip -->
<!-- unsupported-targets: wasm32-wasi -->
`StreamingSubprocess` keeps the child's pipes open and drives stdio
by hand, line by line. We spawn a line-echo child (`findstr "x*"` on
Windows / `/bin/cat` on POSIX — both copy every stdin line straight
to stdout; `x*` is a regex matching any line, including empty ones,
where a bare `^` would be eaten by cmd.exe as its escape character and
fail with "Bad command line"), write three lines, then read each one
back. The reader
(`readStdoutLine`) PARKS on the OS until a line is available — on
arm64-macOS via a kqueue `EVFILT_READ` registration, on Windows via
an overlapped-I/O path — rather than busy-polling, so
this test runs fast. We `closeStdin()` after the last write so the
child sees EOF and exits, drain the three echoed lines, then
`release()` the handle (the API's `close()`-equivalent) to free the
OS process slot.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.path(try FilePath.from("C:/Windows/System32/cmd.exe") otherwise return 2)
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("findstr")
	argv.push("x*")
	#else
	let exe = Executable.path(try FilePath.from("/bin/cat") otherwise return 2)
	var argv = StringArray.create()
	#endif

	var child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3

	try child.writeStdinLine("alpha") otherwise return 4
	try child.writeStdinLine("beta") otherwise return 4
	try child.writeStdinLine("gamma") otherwise return 4
	// Signal EOF so the child drains its input and exits.
	child.closeStdin()

	let lineA = try child.readStdoutLine() otherwise return 5
	let lineB = try child.readStdoutLine() otherwise return 5
	let lineC = try child.readStdoutLine() otherwise return 5

	if lineA != "alpha" 'check-a'
		child.release()
		return 6
	end 'check-a'
	if lineB != "beta" 'check-b'
		child.release()
		return 7
	end 'check-b'
	if lineC != "gamma" 'check-c'
		child.release()
		return 8
	end 'check-c'

	print("{lineA}\n")
	print("{lineB}\n")
	print("{lineC}\n")

	child.release()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha
beta
gamma
```

<!-- test: subprocess-streaming-a-blank-line-is-not-the-end-of-the-stream -->
<!-- unsupported-targets: wasm32-wasi -->
A child's own BLANK line and the end of its stream are different answers, and `readStdoutLine` gives
them differently: the blank line is a line whose text is `""`, and the end of the stream throws
`SubprocessError.endOfStream`. The line-echo child of the case above is handed `alpha`, an empty line and
`gamma`; the three come back as three lines, and the read past them is the end.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.path(try FilePath.from("C:/Windows/System32/cmd.exe") otherwise return 2)
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("findstr")
	argv.push("x*")
	#else
	let exe = Executable.path(try FilePath.from("/bin/cat") otherwise return 2)
	var argv = StringArray.create()
	#endif

	var child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3

	try child.writeStdinLine("alpha") otherwise return 4
	try child.writeStdinLine("") otherwise return 4
	try child.writeStdinLine("gamma") otherwise return 4
	child.closeStdin()

	let lineA = try child.readStdoutLine() otherwise return 5
	let blank = try child.readStdoutLine() otherwise return 6
	let lineC = try child.readStdoutLine() otherwise return 7
	let past = try child.readStdoutLine() otherwise (e) 'ended'
		child.release()
		print("[{lineA}]\n[{blank}]\n[{lineC}]\nthen: {e.displayReason()}\n")
		return 0
	end 'ended'

	child.release()
	print("[{lineA}]\n[{blank}]\n[{lineC}]\nthen: a line of {past.byteLength()} bytes\n")
	return 0
end 'main'
```
```stdout
[alpha]
[]
[gamma]
then: end of stream
```
```exitcode
0
```

<!-- test: subprocess-streaming-spawn-from-green-thread -->
<!-- unsupported-targets: wasm32-wasi -->
The case above spawns from `main`, i.e. from the OS thread's own stack. This
one spawns the identical child from inside an `async` body, i.e. from a GREEN
THREAD — and that is a genuinely different code path, not a restatement.
Every heavyweight Win32 call the runtime makes is routed onto the P's 64 KB
system stack, and the switch is emitted as a *conditional*: a green thread
takes the switching arm, the main thread takes a straight-through arm. Any
register the switching arm disturbs and the straight-through arm does not is
therefore a defect that is INVISIBLE from `main` and fatal from a green
thread — which is exactly the shape of the defect this case pins: a switch
that clobbers RAX while the overlapped-pipe setup holds its `CreateNamedPipeW`
open-mode there. This case is the one that drives a streaming spawn off the
main thread.
```maxon
typealias StepCode = int(0 to 9)

function echoFromGreenThread() returns StepCode
	#if os(Windows)
	let exe = Executable.path(try FilePath.from("C:/Windows/System32/cmd.exe") otherwise return 2)
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("findstr")
	argv.push("x*")
	#else
	let exe = Executable.path(try FilePath.from("/bin/cat") otherwise return 2)
	var argv = StringArray.create()
	#endif

	var child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3

	try child.writeStdinLine("alpha") otherwise return 4
	// Signal EOF so the child drains its input and exits.
	child.closeStdin()

	let lineA = try child.readStdoutLine() otherwise return 5
	child.release()

	if lineA != "alpha" 'check-a'
		return 6
	end 'check-a'

	print("{lineA}\n")
	return 0
end 'echoFromGreenThread'

function main() returns ExitCode
	let g = async echoFromGreenThread()
	let step = await g
	print("step={step}\n")
	if step != 0 'failed'
		return 1
	end 'failed'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
alpha
step=0
```

<!-- test: subprocess-timeout-kill -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	// `ping 127.0.0.1 -n 30` (Windows) / `sleep 30` (POSIX) waits ~30 seconds;
	// we kill it after 200ms.
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("ping")
	argv.push("127.0.0.1")
	argv.push("-n")
	argv.push("30")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sleep") otherwise return 1)
	var argv = StringArray.create()
	argv.push("30")
	#endif
	let cwd = Directory.currentPath()
	var sawTimeout = false
	try Subprocess.run(exe, arguments: argv, workingDirectory: cwd, timeoutMs: 200) otherwise (e) 'handler'
		match e 'kind'
			timeout then sawTimeout = true
			executableNotFound then sawTimeout = false
			spawnFailed then sawTimeout = false
			ioFailed then sawTimeout = false
			inputTooLarge then sawTimeout = false
			endOfStream then sawTimeout = false
		end 'kind'
	end 'handler'
	if sawTimeout 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-timeout-carries-the-partial-output -->
<!-- unsupported-targets: wasm32-wasi -->
⭐⭐ **A DEADLINE DOES NOT DESTROY WHAT THE CHILD ALREADY SAID.** The runtime hands back the bytes it
collected before the kill exactly as it does on a clean exit, so `SubprocessError.timeout` carries them:
`timeout(elapsedMs, stdout, stderr)`. Without that payload the collected buffers would be decoded and then
dropped on the floor when the throw fired, and every caller would render a timeout as a bare
"timed out after Nms" — which is the same text whether the child was wedged before it started or died
one line short of finishing, and tells a reader nothing about which.

The child here prints `alpha`, flushes, and then sleeps far past the deadline; only the `timeout` arm
can hand `alpha` back, so a green here is a statement about the payload and about the arm at once.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("alpha")
	argv.push("&")
	argv.push("ping")
	argv.push("127.0.0.1")
	argv.push("-n")
	argv.push("30")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 1)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("echo alpha; sleep 30")
	#endif
	let cwd = Directory.currentPath()
	var partial = ""
	try Subprocess.run(exe, arguments: argv, workingDirectory: cwd, timeoutMs: 2000) otherwise (e) 'handler'
		partial = match e 'kind'
			timeout(_, out, _) gives out
			executableNotFound or
				spawnFailed or
				ioFailed or
				inputTooLarge or
				endOfStream gives ""
		end 'kind'
	end 'handler'
	if partial.contains("alpha") 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-run-bare-name-found-on-path -->
<!-- unsupported-targets: wasm32-wasi -->
⭐⭐ **A BARE NAME IS FOUND THROUGH `PATH` ON EVERY LANE.** Every other case here spawns a POSIX tool by
absolute path (`/bin/echo`), so this is the case that sees a Windows-only resolver: on Linux one would
read the whole `:`-separated `PATH` as one directory, miss, and hand a bare name to `execve`, which never
searches `PATH` — `Executable.name("git")` would fail to spawn on x64-linux and arm64-linux while
succeeding on Windows and macOS, and the release pipeline, which reads its version out of `git`, would
build compilers that call themselves `dev`.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit")
	argv.push("7")
	#else
	let exe = Executable.name("sh")
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("exit 7")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	return result.exitCode() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: subprocess-bare-name-is-never-found-in-the-working-directory -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
⛔ **A BARE NAME MUST NOT RESOLVE AGAINST THE WORKING DIRECTORY ON POSIX.** Trying the name
"exactly as given" first is correct on Windows, where the current directory is part of the search, and a
hole on POSIX, where it lets a file in the working directory shadow the real tool: an executable
`./maxon-cwd-shadow-probe` that exits 99 would be RUN when spawned by its bare name, and this case would
answer 1. POSIX does not search the working directory for a name with no `/`,
so the name is not found.

Windows is excluded because searching the working directory IS that platform's behaviour.
```maxon
function main() returns ExitCode
	let probe = try FilePath.from("maxon-cwd-shadow-probe") otherwise return 2
	try File.writeText(probe, content: "#!/bin/sh\nexit 99\n", mode: FilePermission.executable) otherwise return 3

	var refused = false
	try Subprocess.run(Executable.name("maxon-cwd-shadow-probe"), arguments: StringArray.create()) otherwise (e) 'handler'
		match e 'kind'
			executableNotFound then refused = true
			spawnFailed or
				timeout or
				ioFailed or
				inputTooLarge or
				endOfStream then refused = false
		end 'kind'
	end 'handler'
	try File.delete(probe) otherwise return 4

	if refused 'refused'
		return 0
	end 'refused'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-environment-names-fold-case-on-windows -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **ON WINDOWS AN OVERRIDE REPLACES THE INHERITED VARIABLE UNDER ANY SPELLING.** Windows reads an
environment variable name without regard to case, so `MAXON_SPEC_ENV_FOLD` and `Maxon_Spec_Env_Fold` are
one variable. `Environment.inheritUpdating` compares an override's name with each inherited name the same
way: the override's entry takes the inherited one's place, and the child's environment carries that
variable once, under the override's spelling. Two names in one `inheritUpdating` or `custom` map that are
one variable to Windows leave no single value to give it, and the spawn is refused as `spawnFailed`.

The program runs itself three deep. The outer run gives the middle one the variable as
`Maxon_Spec_Env_Fold`; the middle run overrides it as `MAXON_SPEC_ENV_FOLD`, and the innermost run prints
every entry of its own environment whose name is that variable's.

Only Windows runs it: POSIX names are case-sensitive, and there two spellings are two variables.
```maxon
function outcomeOf(role String, environment Environment) returns String
	let me = try Process.executablePath() otherwise panic("the running program has a path")
	var argv = StringArray.create()
	argv.push(role)
	var config = Configuration.create(Executable.path(me))
	config.arguments = argv
	config.environment = environment

	var outcome = ""
	if let result = try config.run() 'ran'
		outcome = result.stdout
	end 'ran' else (e) 'refused'
		outcome = "{e.displayReason()}\n"
	end 'refused'
	return outcome
end 'outcomeOf'

function printFoldedEntries() returns ExitCode
	let entries = try Process.currentEnvironmentEntries() otherwise return 2
	for entry in entries 'each'
		if Process.envEntryName(entry).toLower() == "maxon_spec_env_fold" 'folded'
			print("{entry}\n")
		end 'folded'
	end 'each'
	return 0
end 'printFoldedEntries'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() == 1 'outer'
		print(outcomeOf("middle", environment: Environment.inheritUpdating(["Maxon_Spec_Env_Fold": "inherited"])))
		return 0
	end 'outer'

	let role = try args.get(1) otherwise return 3
	if role == "inner" 'inner'
		return printFoldedEntries()
	end 'inner'

	print(outcomeOf("inner", environment: Environment.inheritUpdating(["MAXON_SPEC_ENV_FOLD": "overridden"])))
	print(outcomeOf("inner", environment: Environment.inheritUpdating(["MAXON_SPEC_ENV_FOLD": "a", "maxon_spec_env_fold": "b"])))
	print(outcomeOf("inner", environment: Environment.custom(["MAXON_SPEC_ENV_FOLD": "a", "maxon_spec_env_fold": "b"])))
	return 0
end 'main'
```
```stdout
MAXON_SPEC_ENV_FOLD=overridden
spawn failed: two names in the environment are the variable 'maxon_spec_env_fold', and Windows reads a variable name without regard to case
spawn failed: two names in the environment are the variable 'maxon_spec_env_fold', and Windows reads a variable name without regard to case
```
```exitcode
0
```

<!-- test: subprocess-pathext-shim-a-name-with-no-extension-is-tried-only-with-pathext -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **ON WINDOWS A NAME WITH NO EXTENSION IS TRIED ONLY WITH EACH `PATHEXT` EXTENSION.** Windows never runs
an extensionless file by name: `CreateProcessW` appends `.exe` to a name that has no extension, and `cmd`'s
search tries only the `PATHEXT` extensions. Tools such as `npm`, `npx` and `yarn` install an extensionless
POSIX shell script beside their `.cmd`, and that script is no Windows executable — a spawn of it fails with
`ERROR_BAD_EXE_FORMAT` (193). So the bare name here reaches the `.cmd` beside the script, which exits 7. A
name that already carries an extension is tried exactly as given, so naming the `.cmd` reaches it too, and
naming a `.bat` that is not there finds nothing, though the same name with `.cmd` added sits beside it.

The pair sits in a directory of its own at the head of a child's `PATH`, and the child resolves both names.
A `PATH` directory is searched on every Windows host, where the working directory is not: a host that
defines `NoDefaultCurrentDirectoryInExePath` leaves it out of the search. The child inherits every other
variable, and its `PATH` is the parent's with that directory in front.

Only Windows runs it: `PATHEXT` is that platform's convention, and POSIX tries a name only as given.
```maxon
function outcomeOf(name String) returns String
	var outcome = ""
	if let result = try Subprocess.run(Executable.name(name), arguments: StringArray.create()) 'ran'
		outcome = "exit {result.exitCode()}"
	end 'ran' else (e) 'failed'
		outcome = e.displayReason()
	end 'failed'
	return outcome
end 'outcomeOf'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("bare name: {outcomeOf("maxon-spec-pathext-shim")}, name with its extension: {outcomeOf("maxon-spec-pathext-shim.cmd")}\n")
		print("name with another extension: {outcomeOf("maxon-spec-pathext-shim.bat")}\n")
		return 0
	end 'childMode'

	let shimDir = Directory.currentPath().join("maxon-spec-pathext-shim-dir")
	_ = Directory.create(shimDir)
	let shim = shimDir.join("maxon-spec-pathext-shim")
	let batch = shimDir.join("maxon-spec-pathext-shim.cmd")
	let extended = shimDir.join("maxon-spec-pathext-shim.bat.cmd")
	try File.writeText(shim, content: "#!/bin/sh\nexit 99\n") otherwise return 2
	try File.writeText(batch, content: "@exit /b 7\r\n") otherwise return 2
	try File.writeText(extended, content: "@exit /b 9\r\n") otherwise return 2

	let inheritedPath = try Process.environmentVariable("PATH") otherwise return 3
	var overrides = EnvMap.create()
	overrides.upsert("PATH", value: "{shimDir.path};{inheritedPath}")

	var config = Configuration.create(Executable.path(try Process.executablePath() otherwise return 4))
	var argv = StringArray.create()
	argv.push("resolve")
	config.arguments = argv
	config.environment = Environment.inheritUpdating(overrides)
	let result = try config.run() otherwise return 5

	try File.delete(shim) otherwise return 6
	try File.delete(batch) otherwise return 6
	try File.delete(extended) otherwise return 6
	try Directory.delete(shimDir) otherwise return 6

	print(result.stdout)
	return result.exitCode() as ExitCode
end 'main'
```
```stdout
bare name: exit 7, name with its extension: exit 7
name with another extension: executable not found: maxon-spec-pathext-shim.bat
```
```exitcode
0
```

<!-- test: subprocess-a-bare-name-is-searched-in-createprocess-order-on-windows -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐⭐ **ON WINDOWS A BARE NAME IS SEARCHED IN `CreateProcessW`'s ORDER, AND THE ANSWER IS AN ABSOLUTE PATH TO
THE FILE THAT RUNS.** `CreateProcessW` looks for a name with no path in the directory the application
loaded from, then the working directory, the 32-bit system directory, the 16-bit system directory
(`System` under the Windows directory), the Windows directory, and last each directory of `PATH`. The
walk behind `Executable.name` searches the same directories in the same order, so the file it resolves is
the file `CreateProcessW` would find, and it hands the spawn that file's absolute path, which no later
search and no working directory can reinterpret. A name that carries a path — `sub\tool`, `.\tool`,
`\tool`, `C:\bin\tool`, `C:tool` — is searched for nowhere: it is tried only where it points, and a relative
one points into the child's working directory, exactly as a relative `Executable.path` does.

The program runs itself with a working directory and a `PATH` directory of its own. Probes that exit with
distinct codes sit in the application directory, the working directory and the `PATH` directory, and a
`PATH` directory probe shadows `hostname`, which lives in the system directory, and `regedit`, which lives in
the Windows directory. Each line names the probe's exit code and whether `subprocessResolveOnPath` answered
the absolute path of the file expected to win. A probe under `sub\` sits in the application directory, the
working directory and the `PATH` directory, and in a second working directory the grandchild is given; named
as `sub\…` it runs from the working directory each time. A probe in the working directory and the second one,
named drive-relative (`C:…`, with the working directory's drive), runs from the second. The last probe sits only in the `PATH` directory and is
named from the root of the drive, where no such file is, so it is not found. The child's environment is this process's with that
directory at the head of `PATH` and without `NoDefaultCurrentDirectoryInExePath`, whose presence takes the
working directory out of the search for a name with no path.

Only Windows runs it: POSIX searches `PATH` alone, and `subprocess-bare-name-is-never-found-in-the-working-directory`
pins the working directory POSIX leaves out.
```maxon
typealias FilePathArray = Array with FilePath

function writeProbe(dir FilePath, name String, code ExitCode, placed FilePathArray)
	let probe = dir.join("{name}.cmd")
	try File.writeText(probe, content: "@exit /b {code}\r\n") otherwise panic("the probe's directory is writable")
	placed.push(probe)
end 'writeProbe'

function resolvesTo(name String, expected FilePath) returns bool
	let resolved = String.init(__Builtins.subprocessResolveOnPath(name.cstr()))
	return resolved.toLower() == expected.path.toLower()
end 'resolvesTo'

function makeDirectory(dir FilePath, made FilePathArray)
	_ = Directory.create(dir)
	made.push(dir)
end 'makeDirectory'

function exitOf(name String, workingDirectory FilePath) returns String
	var config = Configuration.create(Executable.name(name))
	config.workingDirectory = workingDirectory
	var outcome = ""
	if let result = try config.run() 'ran'
		outcome = "exit {result.exitCode()}"
	end 'ran' else (e) 'failed'
		outcome = e.displayReason()
	end 'failed'
	return outcome
end 'exitOf'

function report(pathDir FilePath) returns ExitCode
	let appDir = try (try Process.executablePath() otherwise return 2).parent() otherwise return 2
	let workingDir = Directory.currentPath()
	let windowsDir = try FilePath.from(try Process.environmentVariable("SystemRoot") otherwise return 3) otherwise return 3
	let systemDir = windowsDir.join("System32")
	let inherited = FilePath.empty()

	print("first: {exitOf("maxon-spec-order-first", workingDirectory: inherited)}, application directory: {resolvesTo("maxon-spec-order-first", expected: appDir.join("maxon-spec-order-first.cmd"))}\n")
	print("second: {exitOf("maxon-spec-order-second", workingDirectory: inherited)}, working directory: {resolvesTo("maxon-spec-order-second", expected: workingDir.join("maxon-spec-order-second.cmd"))}\n")
	print("hostname: {exitOf("hostname", workingDirectory: inherited)}, system directory: {resolvesTo("hostname", expected: systemDir.join("hostname.exe"))}\n")
	print("regedit: windows directory: {resolvesTo("regedit", expected: windowsDir.join("regedit.exe"))}\n")
	print("third: {exitOf("maxon-spec-order-third", workingDirectory: inherited)}, PATH directory: {resolvesTo("maxon-spec-order-third", expected: pathDir.join("maxon-spec-order-third.cmd"))}\n")
	print("relative: {exitOf("sub\\maxon-spec-order-relative", workingDirectory: inherited)}, working directory: {resolvesTo("sub\\maxon-spec-order-relative", expected: workingDir.join("sub").join("maxon-spec-order-relative.cmd"))}\n")
	print("relative in a given working directory: {exitOf("sub\\maxon-spec-order-relative", workingDirectory: workingDir.join("elsewhere"))}\n")
	let drive = try workingDir.path.split(":").get(0) otherwise return 2
	print("drive-relative in a given working directory: {exitOf("{drive}:maxon-spec-order-drive", workingDirectory: workingDir.join("elsewhere"))}\n")
	print("rooted: {exitOf("\\maxon-spec-order-rooted", workingDirectory: inherited)}\n")
	return 0
end 'report'

function childEnvironment(pathDir FilePath) returns Environment
	let entries = try Process.currentEnvironmentEntries() otherwise panic("this process's environment is readable")
	let pathKey = Process.envNameKey("PATH")
	let noCwdSearchKey = Process.envNameKey("NoDefaultCurrentDirectoryInExePath")
	var vars = EnvMap.create()

	for entry in entries 'each'
		let name = Process.envEntryName(entry)
		let key = Process.envNameKey(name)

		if key == pathKey 'path'
			vars.upsert(name, value: "{pathDir.path};{Process.envEntryValue(entry)}")
		end 'path' else if key != noCwdSearchKey 'kept'
			vars.upsert(name, value: Process.envEntryValue(entry))
		end 'kept'
	end 'each'

	return Environment.custom(vars)
end 'childEnvironment'

function main() returns ExitCode
	let args = CommandLine.args()
	if args.count() > 1 'child'
		let pathDir = try FilePath.from(try args.get(1) otherwise return 2) otherwise return 2
		return report(pathDir)
	end 'child'

	let me = try Process.executablePath() otherwise return 4
	let appDir = try me.parent() otherwise return 4
	let workingDir = Directory.currentPath().join("maxon-spec-order-cwd")
	let pathDir = Directory.currentPath().join("maxon-spec-order-path")
	let elsewhere = workingDir.join("elsewhere")
	var made = FilePathArray.create()
	makeDirectory(workingDir, made: made)
	makeDirectory(pathDir, made: made)
	makeDirectory(elsewhere, made: made)
	makeDirectory(appDir.join("sub"), made: made)
	makeDirectory(workingDir.join("sub"), made: made)
	makeDirectory(pathDir.join("sub"), made: made)
	makeDirectory(elsewhere.join("sub"), made: made)

	var placed = FilePathArray.create()
	writeProbe(appDir.join("sub"), name: "maxon-spec-order-relative", code: 68, placed: placed)
	writeProbe(workingDir.join("sub"), name: "maxon-spec-order-relative", code: 66, placed: placed)
	writeProbe(pathDir.join("sub"), name: "maxon-spec-order-relative", code: 67, placed: placed)
	writeProbe(elsewhere.join("sub"), name: "maxon-spec-order-relative", code: 77, placed: placed)
	writeProbe(appDir, name: "maxon-spec-order-first", code: 11, placed: placed)
	writeProbe(workingDir, name: "maxon-spec-order-first", code: 12, placed: placed)
	writeProbe(pathDir, name: "maxon-spec-order-first", code: 13, placed: placed)
	writeProbe(workingDir, name: "maxon-spec-order-second", code: 22, placed: placed)
	writeProbe(pathDir, name: "maxon-spec-order-second", code: 23, placed: placed)
	writeProbe(pathDir, name: "hostname", code: 44, placed: placed)
	writeProbe(pathDir, name: "regedit", code: 45, placed: placed)
	writeProbe(pathDir, name: "maxon-spec-order-rooted", code: 55, placed: placed)
	writeProbe(workingDir, name: "maxon-spec-order-drive", code: 88, placed: placed)
	writeProbe(elsewhere, name: "maxon-spec-order-drive", code: 99, placed: placed)
	writeProbe(pathDir, name: "maxon-spec-order-third", code: 33, placed: placed)

	var config = Configuration.create(Executable.path(me))
	var argv = StringArray.create()
	argv.push(pathDir.path)
	config.arguments = argv
	config.workingDirectory = workingDir
	config.environment = childEnvironment(pathDir)
	let result = try config.run() otherwise return 5

	for probe in placed 'each'
		try File.delete(probe) otherwise return 6
	end 'each'
	while made.count() > 0 'unmake'
		let dir = try made.pop() otherwise return 6
		try Directory.delete(dir) otherwise return 6
	end 'unmake'

	print(result.stdout)
	return result.exitCode() as ExitCode
end 'main'
```
```stdout
first: exit 11, application directory: true
second: exit 22, working directory: true
hostname: exit 0, system directory: true
regedit: windows directory: true
third: exit 33, PATH directory: true
relative: exit 66, working directory: true
relative in a given working directory: exit 77
drive-relative in a given working directory: exit 99
rooted: executable not found: \maxon-spec-order-rooted
```
```exitcode
0
```

<!-- test: subprocess-not-found -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ **A MISSING BINARY IS `executableNotFound` ON EVERY OS.** POSIX's spawn searches nothing, so a bare name
the `PATH` walk misses is known not to exist before any spawn. Windows cannot know that from the walk —
`CreateProcessW` searches further — so it learns it from the OS error code the spawn leaves behind
(`__Builtins.subprocessLastErrorCode`), and a file-not-found code is the same `executableNotFound` rather
than a generic `spawnFailed`.
```maxon
function main() returns ExitCode
	let exe = Executable.name("definitely-not-a-real-binary-xyzzy")
	var argv = StringArray.create()
	var sawNotFound = false
	try Subprocess.run(exe, arguments: argv) otherwise (e) 'handler'
		match e 'kind'
			executableNotFound then sawNotFound = true
			spawnFailed or
				timeout or
				ioFailed or
				inputTooLarge or
				endOfStream then sawNotFound = false
		end 'kind'
	end 'handler'
	if sawNotFound 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-a-path-to-a-missing-file-is-not-found -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ **`Executable.path` NAMING A FILE THAT DOES NOT EXIST IS `executableNotFound` TOO.** No search is
involved — the path is taken as given — so the only evidence is the spawn's own failure, and the answer
must not depend on which road reached it. The path is absolute, under the working directory, so no
`PATH` entry can supply a file of that name.
```maxon
function main() returns ExitCode
	let missing = Directory.currentPath().join("maxon-spec-no-such-executable-xyzzy")
	if File.exists(missing) 'present'
		return 2
	end 'present'

	var sawNotFound = false
	try Subprocess.run(Executable.path(missing), arguments: StringArray.create()) otherwise (e) 'handler'
		match e 'kind'
			executableNotFound then sawNotFound = true
			spawnFailed or
				timeout or
				ioFailed or
				inputTooLarge or
				endOfStream then sawNotFound = false
		end 'kind'
	end 'handler'
	if sawNotFound 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-a-missing-redirect-file-is-spawn-failed -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ **A STANDARD STREAM THAT CANNOT BE OPENED IS `spawnFailed`, NOT `executableNotFound`, ON EVERY OS.** The
spawn opens a `file` redirect before it launches anything, and a missing file fails with the same
file-not-found code a missing executable does, so only the runtime can say which step failed. The
executable here is one the spawn really finds — the control line proves it runs — and on Windows it is one
`CreateProcessW` finds in the application's own directory, where the `PATH` walk never looks, so no check of
the executable's file can stand in for the runtime's answer.
```maxon
function verdictOf(config Configuration) returns String
	var verdict = "ran"
	try config.run() otherwise (e) 'handler'
		match e 'kind'
			executableNotFound then verdict = "executableNotFound"
			spawnFailed then verdict = "spawnFailed"
			timeout or
				ioFailed or
				inputTooLarge or
				endOfStream then verdict = "other"
		end 'kind'
	end 'handler'
	return verdict
end 'verdictOf'

function main() returns ExitCode
	#if os(Windows)
	let systemTool = try FilePath.from("C:\\Windows\\System32\\hostname.exe") otherwise panic("a literal path is well formed")
	let appDir = try (try Process.executablePath() otherwise panic("the running program has a path")).parent() otherwise panic("an executable's path has a directory")
	let tool = appDir.join("maxon-spec-app-dir-tool.exe")
	let image = try File.readBinary(systemTool) otherwise panic("hostname.exe is readable")
	try File.writeBinary(tool, content: image) otherwise panic("the application directory is writable")
	let exe = Executable.name("maxon-spec-app-dir-tool")
	#else
	let exe = Executable.name("sh")
	#endif

	var control = Configuration.create(exe)
	control.standardOutput = OutputDestination.discard

	var missingStdin = Configuration.create(exe)
	missingStdin.standardInput = InputSource.file(Directory.currentPath().join("maxon-spec-no-such-stdin-xyzzy.txt"))

	var missingStdoutDir = Configuration.create(exe)
	missingStdoutDir.standardOutput = OutputDestination.file(Directory.currentPath().join("maxon-spec-no-such-dir-xyzzy").join("out.txt"))

	print("control={verdictOf(control)} stdin={verdictOf(missingStdin)} stdout={verdictOf(missingStdoutDir)}\n")

	#if os(Windows)
	try File.delete(tool) otherwise panic("the copied tool can be removed")
	#endif
	return 0
end 'main'
```
```exitcode
0
```
```stdout
control=ran stdin=spawnFailed stdout=spawnFailed
```

<!-- test: subprocess-a-relative-executable-resolves-against-the-working-directory -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ **A RELATIVE `Executable.path` IS RELATIVE TO THE CHILD'S `workingDirectory` ON EVERY OS.** Hosts
disagree on whether a launch path is read before or after the child enters its directory —
`CreateProcessW` and macOS 15's `posix_spawn` read it first, against the PARENT's directory — so the
library launches the path joined onto the working directory made absolute, which reads the same either
way. A file that is only in the parent's directory is `executableNotFound` everywhere, and a RELATIVE
working directory is anchored once, to the parent's directory, rather than applied twice by a child that
has already entered it. An `Executable.name` that carries a directory part names a place rather than
something to search for, so it is resolved exactly as the same relative `Executable.path` is, and the second
line answers what the first does.
```maxon
function verdictOf(executable Executable, workingDirectory FilePath) returns String
	var config = Configuration.create(executable)
	config.workingDirectory = workingDirectory
	var verdict = "ran"
	try config.run() otherwise (e) 'handler'
		match e 'kind'
			executableNotFound then verdict = "executableNotFound"
			spawnFailed then verdict = "spawnFailed"
			timeout or
				ioFailed or
				inputTooLarge or
				endOfStream then verdict = "other"
		end 'kind'
	end 'handler'
	return verdict
end 'verdictOf'

function placeTool(dir FilePath, name String)
	#if os(Windows)
	let systemTool = try FilePath.from("C:\\Windows\\System32\\hostname.exe") otherwise panic("a literal path is well formed")
	let image = try File.readBinary(systemTool) otherwise panic("hostname.exe is readable")
	try File.writeBinary(dir.join(name), content: image) otherwise panic("the tool's directory is writable")
	#else
	try File.writeText(dir.join(name), content: "#!/bin/sh\nexit 0\n", mode: FilePermission.executable) otherwise panic("the tool's directory is writable")
	#endif
end 'placeTool'

function dotRelative(name String) returns String
	#if os(Windows)
	return ".\\{name}"
	#else
	return "./{name}"
	#endif
end 'dotRelative'

function pathTo(name String) returns Executable
	return Executable.path(try FilePath.from(dotRelative(name)) otherwise panic("a dot-relative file name is a well-formed path"))
end 'pathTo'

function main() returns ExitCode
	let parentDir = Directory.currentPath()
	let childDirName = "maxon-spec-relative-child-cwd"
	let childDir = parentDir.join(childDirName)
	let relativeChildDir = try FilePath.from(childDirName) otherwise panic("a bare directory name is a well-formed path")
	_ = Directory.create(childDir)

	#if os(Windows)
	let parentOnly = "maxon-spec-parent-cwd-only-tool.exe"
	let childOnly = "maxon-spec-child-cwd-only-tool.exe"
	#else
	let parentOnly = "maxon-spec-parent-cwd-only-tool"
	let childOnly = "maxon-spec-child-cwd-only-tool"
	#endif

	placeTool(parentDir, name: parentOnly)
	placeTool(childDir, name: childOnly)

	print("parent-cwd-only={verdictOf(pathTo(parentOnly), workingDirectory: childDir)} child-cwd-only={verdictOf(pathTo(childOnly), workingDirectory: childDir)} relative-directory={verdictOf(pathTo(childOnly), workingDirectory: relativeChildDir)}\n")
	print("name parent-cwd-only={verdictOf(Executable.name(dotRelative(parentOnly)), workingDirectory: childDir)} name child-cwd-only={verdictOf(Executable.name(dotRelative(childOnly)), workingDirectory: childDir)} name relative-directory={verdictOf(Executable.name(dotRelative(childOnly)), workingDirectory: relativeChildDir)}\n")

	try File.delete(parentDir.join(parentOnly)) otherwise panic("the parent-side tool can be removed")
	try File.delete(childDir.join(childOnly)) otherwise panic("the child-side tool can be removed")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
parent-cwd-only=executableNotFound child-cwd-only=ran relative-directory=ran
name parent-cwd-only=executableNotFound name child-cwd-only=ran name relative-directory=ran
```

<!-- test: subprocess-a-missing-working-directory-is-spawn-failed -->
<!-- unsupported-targets: wasm32-wasi -->
⭐ **A `workingDirectory` THAT DOES NOT EXIST IS `spawnFailed` ON EVERY OS, AND THE CHILD NEVER RUNS.** It
fails with a not-found code on POSIX, so it too is a step only the runtime can tell apart from the launch.
The executable is one every lane finds, so `executableNotFound` would be a wrong answer about it.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit 0")
	#else
	let exe = Executable.name("sh")
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("exit 0")
	#endif

	var verdict = "ran"
	try Subprocess.run(exe, arguments: argv, workingDirectory: Directory.currentPath().join("maxon-spec-no-such-working-directory-xyzzy")) otherwise (e) 'handler'
		match e 'kind'
			executableNotFound then verdict = "executableNotFound"
			spawnFailed then verdict = "spawnFailed"
			timeout or
				ioFailed or
				inputTooLarge or
				endOfStream then verdict = "other"
		end 'kind'
	end 'handler'

	print("{verdict}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
spawnFailed
```

<!-- test: subprocess-exit-code -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit 42")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("exit 42")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	if result.exitCode() != 42 'check-exit'
		return 3
	end 'check-exit'
	if result.succeeded() 'check-success'
		return 4
	end 'check-success'
	let statusCode = match result.status 'status'
		exited(c) gives c
		signalled(c) gives c
	end 'status'
	if statusCode != 42 'check-status'
		return 5
	end 'check-status'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-a-child-a-signal-killed-is-signalled -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A child that a signal ends is reported as `signalled` with the signal's number, never as `exited` with
`128 + signal`: the shell below kills itself with SIGKILL, so the status is `signalled 9`. A runtime that
reported only exit codes answers `exited 137` here.
```maxon
function main() returns ExitCode
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("kill -9 $$")
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 3
	let described = match result.status 'status'
		exited(c) gives "exited {c}"
		signalled(c) gives "signalled {c}"
	end 'status'
	print("{described}\n")
	return 0
end 'main'
```
```stdout
signalled 9
```
```exitcode
0
```

<!-- test: subprocess-streaming-poll-termination-reports-how-the-child-ended -->
<!-- unsupported-targets: wasm32-wasi -->
`pollTermination()` answers how a streaming child ended once it has: on Windows the child exits 42 and is
`exited 42`; on a POSIX host the shell kills itself with SIGKILL and is `signalled 9`, where `pollExit()`
can only say `exited 137`.
```maxon
function describeStatus(status TerminationStatus) returns String
	return match status 'status'
		exited(c) gives "exited {c}"
		signalled(c) gives "signalled {c}"
	end 'status'
end 'describeStatus'

function describePoll(poll TerminationPoll) returns String
	return match poll 'poll'
		running gives "running"
		ended(status) gives describeStatus(status)
	end 'poll'
end 'describePoll'

function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit 42")
	let expected = "exited 42"
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("kill -9 $$")
	let expected = "signalled 9"
	#endif
	var child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	_ = try child.wait() otherwise return 4
	let seen = describePoll(child.pollTermination())
	child.release()

	if seen != expected 'unexpected'
		print("{seen}\n")
		return 5
	end 'unexpected'

	print("ended as expected\n")
	return 0
end 'main'
```
```stdout
ended as expected
```
```exitcode
0
```

<!-- test: subprocess-stderr-collect -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	// `echo err 1>&2` writes "err" to stderr only (shell redirection works the
	// same in cmd and sh).
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo err 1>&2")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("echo err 1>&2")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	if not result.stderr.contains("err") 'check-stderr'
		return 4
	end 'check-stderr'
	// Trim the stdout because some shells emit a trailing CRLF even for empty
	// commands. Empty/whitespace-only stdout is the success criterion.
	if not result.stdout.trim().isEmpty() 'check-stdout-empty'
		return 5
	end 'check-stdout-empty'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-collect-exceeds-pipe-buffer -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	// A child writing FAR more than one OS pipe buffer (65,536 bytes on macOS,
	// ~4 KiB on Windows), on BOTH streams at once: 1,100 lines of 100 characters
	// is over 110 KB per stream. The child blocks in write() the moment the buffer
	// fills, so the parent has to be reading both streams WHILE it waits for the
	// child to exit: a runtime that waits first and drains afterwards deadlocks
	// here, and one that drains stdout to EOF before touching stderr deadlocks on
	// whichever stream it left alone. The volume comes from long lines rather than
	// many iterations because the suite runs below normal priority, where a shell
	// loop of tens of thousands of iterations can starve past the run deadline.
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("(for /L %i in (1,1,1100) do @echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789) & echo stdout-tail & (for /L %j in (1,1,1100) do @echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789 1>&2) & echo stderr-tail 1>&2")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("i=1; while [ $i -le 1100 ]; do echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789; echo 0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789 1>&2; i=$((i+1)); done; echo stdout-tail; echo stderr-tail 1>&2")
	#endif
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	// Well past the largest pipe buffer either host uses, so neither count can be
	// satisfied by a single buffer's worth of output.
	if result.stdout.byteLength() < 100000 'check-stdout-size'
		return 4
	end 'check-stdout-size'
	if result.stderr.byteLength() < 100000 'check-stderr-size'
		return 5
	end 'check-stderr-size'
	// The LAST line, which only arrives if the capture ran to the child's exit
	// rather than stopping at the first buffer.
	if not result.stdout.contains("stdout-tail") 'check-stdout-tail'
		return 6
	end 'check-stdout-tail'
	if not result.stderr.contains("stderr-tail") 'check-stderr-tail'
		return 7
	end 'check-stderr-tail'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-process-execute -->
<!-- unsupported-targets: wasm32-wasi -->
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("ok")
	#else
	let exe = Executable.path(try FilePath.from("/bin/echo") otherwise return 2)
	var argv = StringArray.create()
	argv.push("ok")
	#endif
	let result = try Subprocess.run(exe, arguments: argv, workingDirectory: Directory.currentPath(), timeoutMs: 5000) otherwise return 3
	if result.succeeded() 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-async-await -->
<!-- unsupported-targets: wasm32-wasi -->
Async round-trip: spawn `cmd /c echo hello` as a green thread via
`async Subprocess.run(...)` and consume the result through `try await`.
The fact that this compiles proves the function passes the async-yield
check (E3073 would fire if the body contained no yield point — but
`subprocessWaitCollect` is registered in `IoStubs`). Successful runtime
behaviour proves the trampoline's managed-arg incref + the TIB
save/restore in `EmitCallImportOnSystemStack` keep state consistent
across the green-thread entry to Win32.
```maxon
function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("hello")
	#else
	let exe = Executable.path(try FilePath.from("/bin/echo") otherwise return 2)
	var argv = StringArray.create()
	argv.push("hello")
	#endif
	let p = async Subprocess.run(exe, arguments: argv)
	let result = try await p otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	if not result.stdout.contains("hello") 'check-stdout'
		return 4
	end 'check-stdout'
	if result.exitCode() != 0 'check-exit'
		return 5
	end 'check-exit'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-async-multi -->
<!-- unsupported-targets: wasm32-wasi -->
Spawn four `async Subprocess.run(...)` calls back-to-back, store the
promises in an array, then drain them in order. Each child is a
trivial `cmd /c echo` so the test stays well under the harness's
per-test timeout, but the pattern exercises the parts of the async
runtime that are easy to regress: managed-arg incref through the
async-spawn site (each promise holds a String + StringArray that
the caller's scope would otherwise decref); the trampoline's
mask-driven decref after the spawned function returns; the await
loop's interaction with multiple in-flight promises sitting on the
P's local queue; and the TIB save/restore around each child's
Win32 calls.
```maxon
typealias SubP = Promise with (CollectedOutput, SubprocessError)
typealias SubPArray = Array with SubP

function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	#else
	let exe = Executable.path(try FilePath.from("/bin/echo") otherwise return 7)
	#endif
	let count = 4
	var promises = SubPArray.create()

	var i = 0
	while i < count 'spawn'
		var argv = StringArray.create()
		#if os(Windows)
		argv.push("/c")
		argv.push("echo")
		#endif
		argv.push("child-{i}")
		promises.push(async Subprocess.run(exe.clone(), arguments: argv))
		i = i + 1
	end 'spawn'

	var j = 0
	for p in promises 'drain'
		let r = try await p otherwise return 2
		if not r.succeeded() 'check-success'
			return 3
		end 'check-success'
		if not r.stdout.contains("child-{j}") 'check-stdout'
			return 4
		end 'check-stdout'
		if r.exitCode() != 0 'check-exit'
			return 5
		end 'check-exit'
		j = j + 1
	end 'drain'

	if j != count 'check-count'
		return 6
	end 'check-count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-async-parallel -->
<!-- unsupported-targets: wasm32-wasi -->
Spawn four `async Subprocess.run(...)` calls that each sleep ~1 second
(`ping 127.0.0.1 -n 2` does one ping immediately, waits ~1s, then a
second ping for ~1050ms total). With the local-queue length crossing
the work-stealing threshold (≥2), idle worker Ps lift the extra GTs
off P[0]'s queue and run their subprocess waits on their own OS
threads.

⭐ **THE ASSERTION IS ABOUT WHEN THE FOUR FINISH RELATIVE TO EACH OTHER,
INSIDE ONE BATCH, SO A CHANGE IN MACHINE LOAD CANNOT MOVE IT.** The
program records the time, since the batch started, at which each await
returns. Overlapped children all finish about one child-duration after
the start, so the four times sit close together. Children run one after
another finish one child-duration apart, so the last finishes about
three child-durations after the first.

The yardstick is the FIRST completion time, measured in the same window:
no child can finish before its own duration has passed, so the first
completion is at least one child-duration, on any host and at any load.
The case fails when the spread between the first and last completion
reaches TWICE that first completion. Sequential dispatch spreads them
about three durations, which that bound refuses; a loaded parallel run
spreads them only by its spawn and wake-up skew, which stays far below
two full child-durations.
```maxon
typealias SubP = Promise with (CollectedOutput, SubprocessError)
typealias SubPArray = Array with SubP
typealias ChildTally = int(0 to 4)

let ChildCount = 4 as ChildTally
let SpreadAllowanceInFirstCompletions = 2 as DurationMs

function sleepArgs() returns StringArray
	var argv = StringArray.create()
	#if os(Windows)
	argv.push("/c")
	argv.push("ping")
	argv.push("127.0.0.1")
	argv.push("-n")
	argv.push("2")
	#else
	argv.push("1")
	#endif
	return argv
end 'sleepArgs'

function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sleep") otherwise return 7)
	#endif
	var promises = SubPArray.create()

	let start = Clock.nowMs()
	var spawned = 0 as ChildTally
	while spawned < ChildCount 'spawn'
		promises.push(async Subprocess.run(exe.clone(), arguments: sleepArgs()))
		spawned = spawned + 1
	end 'spawn'

	var awaited = 0 as ChildTally
	var firstCompletion = 0 as DurationMs
	var lastCompletion = 0 as DurationMs
	for p in promises 'drain'
		let r = try await p otherwise return 2
		if not r.succeeded() 'check-success'
			return 3
		end 'check-success'

		let completedAt = Clock.elapsedMs(start)
		if awaited == 0 'isFirst'
			firstCompletion = completedAt
		end 'isFirst'

		lastCompletion = completedAt
		awaited = awaited + 1
	end 'drain'

	if lastCompletion - firstCompletion >= firstCompletion * SpreadAllowanceInFirstCompletions 'check-overlap'
		return 4
	end 'check-overlap'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-collected-output-keeps-an-embedded-nul -->
<!-- unsupported-targets: wasm32-wasi -->
**A CHILD'S OUTPUT IS BYTES, AND A ZERO BYTE IS ONE OF THEM.** Every reader in this API answers a
`String` built over a `__ManagedMemory`, and that record carries an explicit length — so the byte
`0x00` is ordinary content in the middle of a capture, not a terminator. A compiler that recovered
the length with `strlen` instead would end the answer at the first one and report a SHORT capture
with no error, no diagnostic and exit 0: the caller cannot tell a truncated read from a short child.

The child is THIS TEST'S OWN EXECUTABLE, re-run with one argument. Nothing portable in `cmd.exe` or
`/bin/sh` writes a NUL to a pipe, and a case that cannot produce the byte cannot assert anything
about it — so the program plays both parts, and the same two `Executable`/`CommandLine` doors that
make that possible are also on trial: `Process.executablePath()` must answer a path the spawn can
run, and `CommandLine.args()` must deliver the argument that selects the child branch.

Both captured streams are asserted, with DIFFERENT content and different lengths, so a stdout answer
standing in for stderr is a visible failure rather than a coincidence. The bytes either side of the
NUL are printed individually: a length alone would pass for a reader that answered five bytes of
the wrong thing.
```maxon
function describe(label String, text String) returns String
	let bytes = text.toByteArray()
	var line = "{label} len={bytes.count()} ["
	for i in 0 upto bytes.count() 'byteLoop'
		let value = try bytes.get(i) otherwise panic("describe: index is in range")
		if i > 0 'separator'
			line = "{line} "
		end 'separator'
		line = "{line}{value}"
	end 'byteLoop'
	return "{line}]"
end 'describe'

function runChild()
	// "ab\0cd" on stdout, "w\0x" on stderr — a NUL in the middle of each, and two different lengths.
	var out = ByteArray.create()
	out.push(97)
	out.push(98)
	out.push(0)
	out.push(99)
	out.push(100)
	_ = __Builtins.writeStdout(out.managed)

	var err = ByteArray.create()
	err.push(119)
	err.push(0)
	err.push(120)
	_ = __Builtins.writeStderr(err.managed)
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		runChild()
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	var argv = StringArray.create()
	argv.push("emit")
	let result = try Subprocess.run(exe, arguments: argv) otherwise return 3

	print("{describe("stdout", text: result.stdout)}\n")
	print("{describe("stderr", text: result.stderr)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stdout len=5 [97 98 0 99 100]
stderr len=3 [119 0 120]
```

<!-- test: subprocess-streaming-readers-keep-an-embedded-nul -->
<!-- unsupported-targets: wasm32-wasi -->
`subprocess-collected-output-keeps-an-embedded-nul`'s subject on the STREAMING path, where the three
readers are separate code and could each lose the byte on their own. All three are exercised against
one child, because they are three different questions about the same stream:

- `readStdoutBytes(5)` asks for a COUNT, and the count is the whole of its answer: the child writes
  the line below straight after the payload, so a reader that measured its answer any other way
  would run into `ef\0g` rather than stop at five.
- `readStdoutLine()` stops at a newline, so its answer's length is decided by the terminator rather
  than by the request, and it must still carry the zero byte in the middle.
- `readStderrLine()` is the second stream, and a separate body in the runtime: its content differs
  from stdout's so an answer copied from the wrong pipe reads as a wrong answer, not as a pass.

The child writes stdout twice — a bare five-byte payload for the count reader, then a terminated
line — and the readers are called in that order, which also asserts the two share one buffer: a
byte reader with a buffer of its own would leave the line reader waiting on bytes already pulled off
the pipe.
```maxon
function describe(label String, text String) returns String
	let bytes = text.toByteArray()
	var line = "{label} len={bytes.count()} ["
	for i in 0 upto bytes.count() 'byteLoop'
		let value = try bytes.get(i) otherwise panic("describe: index is in range")
		if i > 0 'separator'
			line = "{line} "
		end 'separator'
		line = "{line}{value}"
	end 'byteLoop'
	return "{line}]"
end 'describe'

function runChild()
	// Payload for the count reader: "ab\0cd", no terminator.
	var payload = ByteArray.create()
	payload.push(97)
	payload.push(98)
	payload.push(0)
	payload.push(99)
	payload.push(100)
	_ = __Builtins.writeStdout(payload.managed)

	// Then a line for the line reader: "ef\0g\n".
	var line = ByteArray.create()
	line.push(101)
	line.push(102)
	line.push(0)
	line.push(103)
	line.push(10)
	_ = __Builtins.writeStdout(line.managed)

	// And one on the other stream: "w\0xy\n".
	var errLine = ByteArray.create()
	errLine.push(119)
	errLine.push(0)
	errLine.push(120)
	errLine.push(121)
	errLine.push(10)
	_ = __Builtins.writeStderr(errLine.managed)
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		runChild()
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	var argv = StringArray.create()
	argv.push("emit")
	var child = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 3
	child.closeStdin()

	let counted = try child.readStdoutBytes(5) otherwise return 4
	let lined = try child.readStdoutLine() otherwise return 5
	let errored = try child.readStderrLine() otherwise return 6
	child.release()

	print("{describe("bytes", text: counted)}\n")
	print("{describe("line", text: lined)}\n")
	print("{describe("errline", text: errored)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bytes len=5 [97 98 0 99 100]
line len=4 [101 102 0 103]
errline len=4 [119 0 120 121]
```

<!-- test: subprocess-an-argument-holding-a-nul-is-refused-before-the-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
An argument holding a NUL cannot reach a child intact on any OS, so the spawn is refused as `spawnFailed`
before anything starts, and the child never runs. The child is this case's own executable, which prints
every argument it was given, so a spawn that went ahead shows exactly how the argument list arrived.
```maxon
function runChild()
	let args = CommandLine.args()
	var line = "child ran"
	for (iter, arg) in args.withIterator() 'each'
		if iter.index() > 0 'afterProgram'
			line = "{line} [{arg}]"
		end 'afterProgram'
	end 'each'
	print("{line}\n")
end 'runChild'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		runChild()
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	var argv = StringArray.create()
	argv.push("child")
	argv.push("a\0b")
	argv.push("after")

	if let result = try Subprocess.run(exe, arguments: argv) 'ran'
		print("ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("refused {e.name}\n")
	end 'refused'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
refused spawnFailed
```

<!-- test: subprocess-an-environment-entry-holding-a-nul-is-refused-before-the-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
An environment variable whose name or value holds a NUL cannot reach a child intact on any OS, so the spawn
is refused as `spawnFailed` before anything starts, for every arm that hands the child variables of its own.
```maxon
function attempt(label String, config Configuration)
	if let result = try config.run() 'ran'
		print("{label}: ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("{label}: refused {e.name}\n")
	end 'refused'
end 'attempt'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("child ran\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	var argv = StringArray.create()
	argv.push("child")

	var value = Configuration.create(exe)
	value.arguments = argv.clone()
	var valueVars = EnvMap.create()
	valueVars.upsert("MAXON_SPEC_NUL_VALUE", value: "a\0b")
	value.environment = Environment.inheritUpdating(valueVars)
	attempt("value", config: value)

	var name = Configuration.create(exe)
	name.arguments = argv.clone()
	var nameVars = EnvMap.create()
	nameVars.upsert("MAXON_SPEC\0NUL_NAME", value: "v")
	name.environment = Environment.inheritUpdating(nameVars)
	attempt("name", config: name)

	var custom = Configuration.create(exe)
	custom.arguments = argv
	var customVars = EnvMap.create()
	customVars.upsert("MAXON_SPEC_NUL_CUSTOM", value: "a\0b")
	custom.environment = Environment.custom(customVars)
	attempt("custom", config: custom)

	return 0
end 'main'
```
```exitcode
0
```
```stdout
value: refused spawnFailed
name: refused spawnFailed
custom: refused spawnFailed
```

<!-- test: subprocess-a-working-directory-holding-a-nul-is-refused-before-the-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
A working directory holding a NUL names no directory, never the one named by the bytes before the NUL — so
the spawn is refused as `spawnFailed` and the child never runs, even when that prefix is a directory that
exists.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("child ran in {Directory.currentPath().filename()}\n")
		return 0
	end 'childMode'

	let prefix = Directory.currentPath().join("subprocess-nul-working-directory")

	if not Directory.create(prefix) 'setup'
		return 2
	end 'setup'

	let exe = Executable.path(try Process.executablePath() otherwise return 3)
	var argv = StringArray.create()
	argv.push("child")

	if let result = try Subprocess.run(exe, arguments: argv, workingDirectory: Directory.currentPath().join("subprocess-nul-working-directory\0x")) 'ran'
		print("ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("refused {e.name}\n")
	end 'refused'

	try Directory.delete(prefix) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
```stdout
refused spawnFailed
```

<!-- test: subprocess-a-program-path-holding-a-nul-is-refused-before-the-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
A program path holding a NUL names no program, never the one named by the bytes before the NUL — so the
spawn is refused as `spawnFailed` and nothing runs, even when that prefix is this case's own executable.
```maxon
function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("child ran\n")
		return 0
	end 'childMode'

	let me = try Process.executablePath() otherwise return 2
	let folder = try me.parent() otherwise return 3
	let exe = Executable.path(folder.join("{me.filename()}\0x"))
	var argv = StringArray.create()
	argv.push("child")

	if let result = try Subprocess.run(exe, arguments: argv) 'ran'
		print("ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("refused {e.name}\n")
	end 'refused'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
refused spawnFailed
```

<!-- test: subprocess-a-redirect-file-path-holding-a-nul-is-refused-before-the-spawn -->
<!-- unsupported-targets: wasm32-wasi -->
A redirect file whose path holds a NUL names no file, never the one named by the bytes before the NUL — so
a spawn that reads stdin from one, or writes stdout or stderr to one, is refused as `spawnFailed` and the
child never runs. The prefix is a file that exists, and it is neither read from nor written to.
```maxon
function attempt(label String, config Configuration)
	if let result = try config.run() 'ran'
		print("{label}: ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("{label}: refused {e.name}\n")
	end 'refused'
end 'attempt'

function main() returns ExitCode
	if CommandLine.args().count() > 1 'childMode'
		print("child ran\n")
		printError("child ran\n")
		return 0
	end 'childMode'

	let prefix = Directory.currentPath().join("subprocess-nul-redirect")
	let holding = Directory.currentPath().join("subprocess-nul-redirect\0x")
	try File.writeText(prefix, content: "x") otherwise return 2

	let exe = Executable.path(try Process.executablePath() otherwise return 3)
	var argv = StringArray.create()
	argv.push("child")

	var input = Configuration.create(exe)
	input.arguments = argv.clone()
	input.standardInput = InputSource.file(holding)
	attempt("stdin", config: input)

	var output = Configuration.create(exe)
	output.arguments = argv.clone()
	output.standardOutput = OutputDestination.file(holding)
	attempt("stdout", config: output)

	var errors = Configuration.create(exe)
	errors.arguments = argv
	errors.standardError = OutputDestination.file(holding)
	attempt("stderr", config: errors)

	let kept = try File.readText(prefix) otherwise "(gone)"
	print("prefix holds {kept}\n")
	try File.delete(prefix) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```
```stdout
stdin: refused spawnFailed
stdout: refused spawnFailed
stderr: refused spawnFailed
prefix holds x
```

<!-- test: subprocess-stdin-bytes-holding-a-nul-reach-the-child-whole -->
<!-- unsupported-targets: wasm32-wasi -->
A stdin payload is bytes, and a zero byte is one of them: every door that feeds a child's stdin delivers a
payload holding a NUL whole, never cut at the NUL. The child is this case's own executable, which reads its
stdin to the end and prints the count and the value of every byte it received. The `delayed` child prints a
line first, because that door feeds nothing until the child's first stdout byte.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function readAllStdin() returns String
	var received = ByteArray.create()

	while true 'drain'
		let chunk = __Builtins.readStdin(4096)

		if chunk.length() == 0 'ended'
			break
		end 'ended'

		received.append(ByteArray.init(chunk))
	end 'drain'

	var line = "{received.count()} bytes ["
	for (iter, value) in received.withIterator() 'each'
		if iter.index() > 0 'separator'
			line = "{line} "
		end 'separator'
		line = "{line}{value}"
	end 'each'
	return "{line}]"
end 'readAllStdin'

function main() returns ExitCode
	let args = CommandLine.args()

	if args.count() > 1 'childMode'
		if (try args.get(1) otherwise "") == "ready" 'announce'
			print("ready\n")
		end 'announce'

		print("{readAllStdin()}\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	var argv = StringArray.create()
	argv.push("child")

	var fed = Configuration.create(exe)
	fed.arguments = argv.clone()
	fed.standardInput = InputSource.bytes("a\0b")
	let fedResult = try fed.run() otherwise return 3
	print("bytes: {fedResult.stdout}")

	var readyArgv = StringArray.create()
	readyArgv.push("ready")
	var held = Configuration.create(exe)
	held.arguments = readyArgv
	held.standardInput = InputSource.delayed("a\0b")
	let heldResult = try held.run() otherwise return 4
	print("delayed: {heldResult.stdout}")

	var streamed = try StreamingSubprocess.spawn(exe, arguments: argv) otherwise return 5
	try streamed.writeStdinLine("a\0b") otherwise return 6
	streamed.closeStdin()
	let streamedLine = try streamed.readStdoutLine() otherwise return 7
	_ = try streamed.wait() otherwise return 8
	streamed.release()
	print("streamed: {streamedLine}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
bytes: 3 bytes [97 0 98]
delayed: ready
3 bytes [97 0 98]
streamed: 4 bytes [97 0 98 10]
```

<!-- test: subprocess-a-spawn-whose-pipes-cannot-be-made-fails-instead-of-panicking -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A spawn whose stdin or stdout pipe cannot be made fails as `spawnFailed`, and nothing panics. The case runs
itself under `/bin/sh` with `ulimit -n` lowered, and that child opens its own executable until the open
fails, so every descriptor the process may hold is in use; then it spawns once feeding stdin and once
collecting stdout, every other stream inherited so that pipe is the only descriptor the spawn needs. The
case is POSIX-only because Windows has no per-process handle limit a program can lower and reach in a test.
```maxon
function attempt(label String, config Configuration)
	if let result = try config.run() 'ran'
		print("{label}: ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("{label}: refused {e.displayReason()}\n")
	end 'refused'
end 'attempt'

function spawnWithoutDescriptors(exe Executable)
	var argv = StringArray.create()
	argv.push("grandchild")

	var fed = Configuration.create(exe)
	fed.arguments = argv.clone()
	fed.standardInput = InputSource.bytes("x")
	fed.standardOutput = OutputDestination.inherit
	fed.standardError = OutputDestination.inherit
	attempt("stdin pipe", config: fed)

	var collected = Configuration.create(exe)
	collected.arguments = argv
	collected.standardInput = InputSource.inherit
	collected.standardError = OutputDestination.inherit
	attempt("stdout pipe", config: collected)
end 'spawnWithoutDescriptors'

function exhaustThenSpawn(me FilePath)
	var held = try __ManagedFile.openRead(me.path.toByteArray().managed) otherwise 'exhausted'
		print("exhausted\n")
		spawnWithoutDescriptors(Executable.path(me))
		return
	end 'exhausted'

	exhaustThenSpawn(me)
	held.close()
end 'exhaustThenSpawn'

function main() returns ExitCode
	let me = try Process.executablePath() otherwise return 2
	let args = CommandLine.args()

	if args.count() > 1 'childMode'
		if (try args.get(1) otherwise "") == "grandchild" 'ranAnyway'
			print("grandchild ran\n")
			return 0
		end 'ranAnyway'

		exhaustThenSpawn(me)
		return 0
	end 'childMode'

	var argv = StringArray.create()
	argv.push("-c")
	argv.push("ulimit -n 64; exec \"$0\" child")
	argv.push(me.path)
	let result = try Subprocess.run(Executable.path(try FilePath.from("/bin/sh") otherwise return 3), arguments: argv) otherwise return 4
	print(result.stdout)
	print("child exit {result.exitCode()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
exhausted
stdin pipe: refused spawn failed: os error 4
stdout pipe: refused spawn failed: os error 4
child exit 0
```

<!-- test: subprocess-a-stdin-pipe-that-cannot-be-created-reports-the-creations-own-error -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
<!-- process: own -->
A spawn whose stdin pipe cannot be created fails with the error the creation itself reported, not one from a
later call made on the handle that was never created. A Windows pipe is named `mx` followed by sixteen digits
spelling the spawning process's id and a per-process sequence number, so a PowerShell child this program starts
occupies every one of the first sixty-four such names with a single-instance server before it says `ready`,
spelling the digits 10 to 15 as `a` to `f`. The next
spawn's stdin pipe takes one of those names, and Windows refuses a second instance of a single-instance pipe with
`ERROR_PIPE_BUSY`, 231.
```maxon
function main() returns ExitCode
	var holderArgv = StringArray.create()
	holderArgv.push("-NoProfile")
	holderArgv.push("-Command")
	holderArgv.push("$parent = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID)).ParentProcessId; $held = @(); foreach ($letters in 87) \{ foreach ($seq in 0..63) \{ $v = ([int64]$parent -shl 32) -bor $seq; $name = 'mx' + (-join (15..0 | ForEach-Object \{ $n = ($v -shr (4 * $_)) -band 15; [char]($n + $(if ($n -lt 10) \{ 48 \} else \{ $letters \})) \})); try \{ $held += New-Object System.IO.Pipes.NamedPipeServerStream($name, 'InOut', 1) \} catch \{ \} \} \}; [Console]::Out.WriteLine('ready'); [Console]::Out.Flush(); [void][Console]::In.ReadLine()")
	var holder = try StreamingSubprocess.spawn(Executable.name("powershell"), arguments: holderArgv) otherwise return 2
	let announced = try holder.readStdoutLine() otherwise return 3
	print("holder ready: {announced.startsWith("ready")}\n")

	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit")
	var fed = Configuration.create(Executable.name("cmd"))
	fed.arguments = argv
	fed.standardInput = InputSource.bytes("x")

	if let result = try fed.run() 'ran'
		print("fed: ran with exit {result.exitCode()}\n")
	end 'ran' else (e) 'refused'
		print("fed: refused {e.displayReason()}\n")
	end 'refused'

	holder.closeStdin()
	_ = try holder.wait() otherwise return 4
	holder.release()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
holder ready: true
fed: refused spawn failed: os error 231
```

<!-- test: subprocess-a-program-started-with-its-stdin-closed-reads-and-hands-on-the-null-device -->
<!-- unsupported-targets: x64-windows, wasm32-wasi -->
A program started with its standard input closed reads its standard input as the null device, and a child that
inherits it does too. Such a program's first new descriptor is 0, so unless the runtime reopens a closed standard
descriptor on the null device at start-up, whatever it opens first — the poller, or the first pipe of a spawn —
becomes its standard input. The case runs itself under `/bin/sh` with `<&-`, and that child spawns a grandchild
feeding its stdin, then runs `/bin/cat` with stdin inherited: on the null device `cat` reads end of file at once
and exits 0 having written nothing to either stream.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function spawnWithStdinClosed(exe Executable)
	var fedArgv = StringArray.create()
	fedArgv.push("fed")
	var fed = Configuration.create(exe)
	fed.arguments = fedArgv
	fed.standardInput = InputSource.bytes("xyz")

	if let result = try fed.run() 'ran'
		print("fed: ran: {result.stdout}")
	end 'ran' else (e) 'refused'
		print("fed: refused {e.displayReason()}\n")
	end 'refused'

	var inherited = Configuration.create(Executable.path(try FilePath.from("/bin/cat") otherwise panic("/bin/cat is a valid path")))
	inherited.standardInput = InputSource.inherit

	if let result = try inherited.run() 'catRan'
		print("inherited: cat exit {result.exitCode()} stdout {result.stdout.byteLength()} bytes stderr {result.stderr.byteLength()} bytes\n")
	end 'catRan' else (e) 'catRefused'
		print("inherited: refused {e.displayReason()}\n")
	end 'catRefused'
end 'spawnWithStdinClosed'

function main() returns ExitCode
	let me = try Process.executablePath() otherwise return 2
	let args = CommandLine.args()

	if args.count() > 1 'childMode'
		let mode = try args.get(1) otherwise ""

		if mode == "fed" 'readFed'
			var received = ByteArray.create()

			while true 'drain'
				let chunk = __Builtins.readStdin(4096)

				if chunk.length() == 0 'ended'
					break
				end 'ended'

				received.append(ByteArray.init(chunk))
			end 'drain'

			print("{received.count()} bytes\n")
			return 0
		end 'readFed'

		spawnWithStdinClosed(Executable.path(me))
		return 0
	end 'childMode'

	var argv = StringArray.create()
	argv.push("-c")
	argv.push("exec \"$0\" child <&-")
	argv.push(me.path)
	let result = try Subprocess.run(Executable.path(try FilePath.from("/bin/sh") otherwise return 3), arguments: argv) otherwise return 4
	print(result.stdout)
	print("child exit {result.exitCode()}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
fed: ran: 3 bytes
inherited: cat exit 0 stdout 0 bytes stderr 0 bytes
child exit 0
```

<!-- test: subprocess-a-child-that-writes-before-it-reads-gets-its-whole-stdin -->
<!-- unsupported-targets: wasm32-wasi -->
A collected run feeds a child's stdin and drains its stdout together, so a child that writes more than a pipe
holds before it reads anything still receives its whole input. The child is this case's own executable: it
writes one mebibyte to stdout, then reads its stdin to the end and reports the count on stderr. The payload is
four times a pipe's capacity, so a push that waited for the child to read would wait on a child that is itself
waiting for its stdout to be drained. The run carries a ten-second deadline, which a healthy run never nears.
```maxon
typealias Tally = int(0 to i64.max)

let stdoutBlocks = 16
let stdinBlocks = 4
let deadlineMs = 10000

function block() returns String
	var text = ""
	for _ in 0 upto 65536 'byte'
		text.append("a")
	end 'byte'
	return text
end 'block'

function main() returns ExitCode
	let args = CommandLine.args()

	if args.count() > 1 'childMode'
		let chunk = block()
		for _ in 0 upto stdoutBlocks 'write'
			print(chunk)
		end 'write'

		var received = 0 as Tally

		while true 'drain'
			let read = __Builtins.readStdin(4096)

			if read.length() == 0 'ended'
				break
			end 'ended'

			received = received + read.length()
		end 'drain'

		printError("child read {received} bytes\n")
		return 0
	end 'childMode'

	let exe = Executable.path(try Process.executablePath() otherwise return 2)
	let chunk = block()
	var payload = ""
	for _ in 0 upto stdinBlocks 'fill'
		payload.append(chunk)
	end 'fill'

	var argv = StringArray.create()
	argv.push("child")
	var config = Configuration.create(exe)
	config.arguments = argv
	config.standardInput = InputSource.bytes(payload)
	config.timeoutMs = deadlineMs

	if let result = try config.run() 'ran'
		print("exit {result.exitCode()}\nstdout {result.stdout.byteLength()} bytes\n{result.stderr}")
	end 'ran' else (e) 'refused'
		print("refused {e.displayReason()}\n")
	end 'refused'

	return 0
end 'main'
```
```exitcode
0
```
```stdout
exit 0
stdout 1048576 bytes
child read 262144 bytes
```

<!-- test: subprocess-a-child-inherits-no-other-spawns-pipe-ends -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
A child inherits only the three standard handles its own spawn gives it, never the pipe ends another spawn is
setting up at the same moment. A service starts thirty detached `ping` children back to back, each living about
two seconds, while `main` collects `cmd /c echo hi` thirty times. A collected child's stdout ends when the last
copy of its write end is closed, so if a `ping` child inherited one of those write ends, that collect would last
until the `ping` exits. Every collect must finish well inside the `ping` children's lifetime.
```maxon
typealias Tally = int(0 to 30)

let rounds = 30 as Tally
let slowMs = 1500

type Spawner
	var started as Tally

	static function create() returns Self
		return Self{started: 0}
	end 'create'

	export function burst()
		var argv = StringArray.create()
		argv.push("-n")
		argv.push("3")
		argv.push("127.0.0.1")
		var config = Configuration.create(Executable.name("ping"))
		config.arguments = argv

		for _ in 0 upto rounds 'spawn'
			_ = try config.runDetached() otherwise panic("ping starts")
			self.started = self.started + 1
		end 'spawn'
	end 'burst'

	export function count() returns Tally
		return self.started
	end 'count'
end 'Spawner'

function main() returns ExitCode
	let spawner = spawn Spawner.create()
	spawner.burst()

	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo hi")
	var echo = Configuration.create(Executable.name("cmd"))
	echo.arguments = argv
	var slow = 0 as Tally
	var answered = 0 as Tally

	for _ in 0 upto rounds 'collect'
		let result = try echo.run() otherwise return 2

		if result.stdout.trim() == "hi" 'said'
			answered = answered + 1
		end 'said'

		if result.durationMs >= slowMs 'late'
			slow = slow + 1
		end 'late'
	end 'collect'

	let started = try await spawner.count() otherwise return 3
	print("pings={started} collects={rounds} answered={answered} slow={slow}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pings=30 collects=30 answered=30 slow=0
```