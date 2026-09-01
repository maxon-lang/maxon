---
feature: subprocess
status: stable
keywords: [subprocess, process, spawn, stdin, stdout, stderr, timeout]
category: stdlib
---

# Subprocess

## Documentation

### Overview

`Subprocess` is the synchronous child-process API. The hot path is
`Subprocess.run(.name("git"), arguments: ["status"])`, which captures
stdout/stderr into a `CollectedOutput`. For more control, build a
`Configuration` and call `.run()`.

Every fragment below runs on both hosts the bootstrap compiler targets
(`x64-windows` and `arm64-macos`), branching on `#if os(Windows)` only
to name the shell. All cases use synchronous `Subprocess.run` /
`Configuration.run`; the async path is covered elsewhere.

## Tests

These tests verify the synchronous Subprocess path: spawn, working
directory, stdin bytes, output capture, timeout, exit code,
stderr-only collection, and capture of a child that outruns the OS
pipe buffer on both streams at once.

<!-- test: subprocess-run-collect -->
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

<!-- test: subprocess-streaming-roundtrip -->
`StreamingSubprocess` keeps the child's pipes open and drives stdio
by hand, line by line. We spawn a line-echo child (`findstr "x*"` on
Windows / `/bin/cat` on POSIX — both copy every stdin line straight
to stdout; `x*` is a regex matching any line, including empty ones,
where a bare `^` would be eaten by cmd.exe as its escape character and
fail with "Bad command line"), write three lines, then read each one
back. The reader
(`readStdoutLine`) PARKS on the OS until a line is available — on
arm64-macOS via a kqueue `EVFILT_READ` registration, on Windows via
the bootstrap's overlapped-I/O path — rather than busy-polling, so
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

<!-- test: subprocess-streaming-spawn-from-green-thread -->
The case above spawns from `main`, i.e. from the OS thread's own stack. This
one spawns the identical child from inside an `async` body, i.e. from a GREEN
THREAD — and that is a genuinely different code path, not a restatement.
Every heavyweight Win32 call the runtime makes is routed onto the P's 64 KB
system stack, and the switch is emitted as a *conditional*: a green thread
takes the switching arm, the main thread takes a straight-through arm. Any
register the switching arm disturbs and the straight-through arm does not is
therefore a defect that is INVISIBLE from `main` and fatal from a green
thread — which is exactly the shape the bug this case pins had (the switch
clobbered RAX, and the overlapped-pipe setup was holding its `CreateNamedPipeW`
open-mode there). Nothing in the suite drove a streaming spawn off the main
thread until this case existed.
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

<!-- test: subprocess-not-found -->
```maxon
function main() returns ExitCode
	// `definitely-not-a-real-binary-xyzzy` isn't on PATH and isn't a file.
	// The runtime's PATH resolver returns NULL, the stdlib falls through to
	// the bare name, and CreateProcessW fails with "file not found". The
	// stdlib surfaces that as `spawnFailed`.
	let exe = Executable.name("definitely-not-a-real-binary-xyzzy")
	var argv = StringArray.create()
	var sawSpawnFailed = false
	try Subprocess.run(exe, arguments: argv) otherwise (e) 'handler'
		match e 'kind'
			spawnFailed then sawSpawnFailed = true
			executableNotFound then sawSpawnFailed = false
			timeout then sawSpawnFailed = false
			ioFailed then sawSpawnFailed = false
			inputTooLarge then sawSpawnFailed = false
		end 'kind'
	end 'handler'
	if sawSpawnFailed 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-exit-code -->
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

<!-- test: subprocess-stderr-collect -->
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
```maxon
function main() returns ExitCode
	// A child writing FAR more than one OS pipe buffer (65,536 bytes on macOS,
	// ~4 KiB on Windows), on BOTH streams at once. 20,000 numbered lines is
	// ~108 KB per stream. The child blocks in write() the moment the buffer
	// fills, so the parent has to be reading both streams WHILE it waits for the
	// child to exit: a runtime that waits first and drains afterwards deadlocks
	// here, and one that drains stdout to EOF before touching stderr deadlocks on
	// whichever stream it left alone. Both were real (arm64-macOS reported the
	// deadlock as the caller's timeout).
	#if os(Windows)
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("(for /L %i in (1,1,20000) do @echo %i) & (for /L %j in (1,1,20000) do @echo %j 1>&2)")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sh") otherwise return 2)
	var argv = StringArray.create()
	argv.push("-c")
	argv.push("i=1; while [ $i -le 20000 ]; do echo $i; echo $i 1>&2; i=$((i+1)); done")
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
	if not result.stdout.contains("20000") 'check-stdout-tail'
		return 6
	end 'check-stdout-tail'
	if not result.stderr.contains("20000") 'check-stderr-tail'
		return 7
	end 'check-stderr-tail'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: subprocess-process-execute -->
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
		promises.push(async Subprocess.run(exe, arguments: argv))
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
<!-- TimeoutMs: 8000 -->
Spawn four `async Subprocess.run(...)` calls that each sleep ~1 second
(`ping 127.0.0.1 -n 2` does one ping immediately, waits ~1s, then a
second ping for ~1050ms total). With the local-queue length crossing
the work-stealing threshold (≥2), idle worker Ps lift the extra GTs
off P[0]'s queue and run their subprocess waits on their own OS
threads. Sequential dispatch would take ~4200ms (4×1050ms back-to-back);
the measured parallel time is ~2000-2200ms. The 3500ms threshold
catches a regression to sequential while tolerating cold-start jitter.
The 8000ms test timeout gives generous headroom for the harness
itself, well above the parallel-execution wall clock.
```maxon
typealias SubP = Promise with (CollectedOutput, SubprocessError)
typealias SubPArray = Array with SubP

function main() returns ExitCode
	#if os(Windows)
	let exe = Executable.name("cmd")
	#else
	let exe = Executable.path(try FilePath.from("/bin/sleep") otherwise return 7)
	#endif
	let count = 4
	var promises = SubPArray.create()

	let start = Clock.nowMs()
	var i = 0
	while i < count 'spawn'
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
		promises.push(async Subprocess.run(exe, arguments: argv))
		i = i + 1
	end 'spawn'
	for p in promises 'drain'
		let r = try await p otherwise return 2
		if not r.succeeded() 'check-success'
			return 3
		end 'check-success'
	end 'drain'
	let elapsed = Clock.elapsedMs(start)

	// Sequential: ~4200ms. Parallel: ~2000-2200ms. 3500ms catches a
	// regression to sequential dispatch (e.g. if subprocess_wait_internal
	// stops yielding to the scheduler or work-stealing breaks).
	if elapsed >= 3500 'check-parallel'
		return 4
	end 'check-parallel'
	return 0
end 'main'
```
```exitcode
0
```
