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

These fragments exercise the Windows runtime (the only host the
bootstrap compiler targets for spec tests). All cases use synchronous
`Subprocess.run` / `Configuration.run`; the async path is covered
elsewhere.

## Tests

These tests verify the Windows synchronous Subprocess path: spawn,
working directory, stdin bytes, output capture, timeout, exit code,
and stderr-only collection.

<!-- test: subprocess-run-collect -->
```maxon
function main() returns ExitCode
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("hello")
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
	let exe = Executable.path(try FilePath.from("C:/Windows/System32/cmd.exe") otherwise return 2)
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("via-path")
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
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("cd")
	let result = try Subprocess.run(exe, arguments: argv, workingDirectory: cwd) otherwise return 2
	if not result.succeeded() 'check-success'
		return 3
	end 'check-success'
	// `cmd /c cd` prints the working directory it inherited from us.
	if not result.stdout.contains(cwd.path) 'check-cwd'
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
	// `findstr .` echoes any line containing at least one character — i.e.
	// every non-empty line of stdin. We feed it two lines and expect both
	// back on stdout.
	var c = Configuration.create(Executable.name("findstr"))
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

<!-- test: subprocess-timeout-kill -->
```maxon
function main() returns ExitCode
	// `ping 127.0.0.1 -n 30` waits ~29 seconds; we kill it after 200ms.
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("ping")
	argv.push("127.0.0.1")
	argv.push("-n")
	argv.push("30")
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
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("exit 42")
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
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	// `echo err 1>&2` writes "err" to stderr only.
	argv.push("echo err 1>&2")
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
	let exe = Executable.name("cmd")
	var argv = StringArray.create()
	argv.push("/c")
	argv.push("echo")
	argv.push("hello")
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
typealias SubP = Promise with CollectedOutput
typealias SubPArray = Array with SubP

function main() returns ExitCode
	let exe = Executable.name("cmd")
	let count = 4
	var promises = SubPArray.create()

	var i = 0
	while i < count 'spawn'
		var argv = StringArray.create()
		argv.push("/c")
		argv.push("echo")
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
typealias SubP = Promise with CollectedOutput
typealias SubPArray = Array with SubP

function main() returns ExitCode
	let exe = Executable.name("cmd")
	let count = 4
	var promises = SubPArray.create()

	let start = Clock.nowMs()
	var i = 0
	while i < count 'spawn'
		var argv = StringArray.create()
		argv.push("/c")
		argv.push("ping")
		argv.push("127.0.0.1")
		argv.push("-n")
		argv.push("2")
		promises.push(async Subprocess.run(exe, arguments: argv))
		i = i + 1
	end 'spawn'
	for p in promises 'drain'
		let r = try await p otherwise return 2
		if not r.succeeded() 'check-success'
			return 3
		end 'check-success'
	end 'drain'
	let elapsed = Clock.elapsedMs(since: start)

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
