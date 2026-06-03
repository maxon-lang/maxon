---
feature: builtins-type
status: stable
keywords: [builtins, __Builtins, compiler intrinsics, stdlib]
category: type-system
---

# __Builtins Type

## Documentation

### Overview

`__Builtins` is a compiler builtin type that provides static methods for low-level system operations. While accessible from any code, most users should prefer the stdlib wrappers: `print()`, `File`, `Directory`, `Subprocess`, `CommandLine`.

All methods include safety checks to prevent crashes and memory corruption:
- Buffer reads are clamped to capacity
- Out-of-bounds argument indices return empty values
- Null/invalid handles are handled gracefully

### Available Static Methods

**I/O:**
- `__Builtins.writeStdout(managed)` returns int - Write managed buffer to stdout
- `__Builtins.writeStderr(managed)` returns int - Write managed buffer to stderr
- `__Builtins.readStdin(maxBytes)` returns __ManagedMemory - Read up to `maxBytes` bytes from stdin into a fresh managed-memory buffer (length reflects bytes actually read; 0 on EOF)

**Command Line:**
- `__Builtins.commandLineCount()` returns int - Get argument count
- `__Builtins.commandLineArg(index)` returns __ManagedMemory - Get argument at index

**Process / Subprocess:**

User code should use the `Subprocess` stdlib type rather than calling these builtins directly. The `subprocess*` builtins back `stdlib/Subprocess.maxon`; the table below documents what is present today.

- `__Builtins.executablePath()` returns __ManagedMemory - Absolute path to the current executable (empty buffer when unavailable; `Process.executablePath` surfaces this as `ProcessIntrospectionError.pathUnavailable`)
- `__Builtins.currentProcessId()` returns int - Pid of the current process
- `__Builtins.subprocessResolveOnPath(nameManaged)` returns __ManagedMemory - Resolve a bare executable name via PATH lookup; empty buffer on miss
- `__Builtins.subprocessSpawn(argv, argc, cwd, envBlock, envInherit, stdinKind, stdinData, stdoutKind, stdoutData, stdoutLimit, stderrKind, stderrData, stderrLimit, flags)` returns int - Spawn a child process; returns a handle, -1 on failure
- `__Builtins.subprocessDetach(... same args as spawn ...)` returns int - Like spawn but with the detach flag; returns pid, -1 on failure
- `__Builtins.subprocessLastErrorMessage()` returns __ManagedMemory - Last spawn/wait error message from this thread
- `__Builtins.subprocessGetPid(handle)` returns int - Pid of a spawned child
- `__Builtins.subprocessWaitCollect(handle, timeoutMs)` returns int - Wait for the child, drain stdout/stderr, return a result-struct pointer; -1 on error
- `__Builtins.subprocessKill(handle, force)` returns int - Terminate the child
- `__Builtins.subprocessSendSignal(handle, signum)` returns int - Send a console-control signal (Windows: SIGINT/SIGBREAK)
- `__Builtins.subprocessReleaseHandle(handle)` - Free the handle struct and its OS handles
- `__Builtins.subprocessResultStatusKind(resultPtr)` returns int - 0=exited, 1=signalled, 2=timedOut
- `__Builtins.subprocessResultStatusCode(resultPtr)` returns int - Exit/signal code from the result struct
- `__Builtins.subprocessResultStdout(resultPtr)` returns __ManagedMemory - Captured stdout
- `__Builtins.subprocessResultStderr(resultPtr)` returns __ManagedMemory - Captured stderr
- `__Builtins.subprocessResultDurationMs(resultPtr)` returns int - Elapsed wall-clock time of the child
- `__Builtins.subprocessResultRelease(resultPtr)` - Free the result struct and its captured buffers
- `__Builtins.managedIsNull(managed)` returns int - 1 if a __ManagedMemory carries an empty (NUL-terminated) buffer, else 0

**Primitive:**
- `__Builtins.floatToBits(value)` returns int - Bitcast float to int

## Tests

These tests verify the __Builtins type works both through stdlib wrappers and directly.

<!-- test: builtins-type.print-via-stdlib -->
```maxon
function main() returns ExitCode
	print("hello\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
hello
```

<!-- test: builtins-type.command-line-count -->
<!-- Args: arg1 arg2 arg3 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	return args.count()
end 'main'
```
```exitcode
4
```

<!-- test: builtins-type.directory-exists -->
```maxon
function main() returns ExitCode
	let cwd = Directory.currentPath()
	if Directory.exists(cwd) 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.command-line-arg-out-of-bounds -->
<!-- Args: one -->
```maxon
function main() returns ExitCode
	let managed = __Builtins.commandLineArg(9999)
	let s = String.init(managed)
	if s == "" 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.float-to-bits -->
```maxon
function main() returns ExitCode
	let bits = __Builtins.floatToBits(1.0)
	// IEEE 754: 1.0 = 0x3FF0000000000000 = 4607182418800017408
	if bits == 4607182418800017408 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.direct-write-stdout -->
`__Builtins.writeStdout` returns the byte count written — an impure result like
any other function's, so a bare statement-position call is rejected and the
result must be explicitly discarded with `_ =`.
```maxon
function main() returns ExitCode
	let s = "direct\n"
	_ = __Builtins.writeStdout(s.managed)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
direct
```
