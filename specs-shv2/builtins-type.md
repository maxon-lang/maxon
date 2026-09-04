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
- `__Builtins.subprocessStdoutState(handle)` returns __SubprocessStreamState - What a streaming child's stdout reader will answer next: `open`, `atEof`, `readFailed`, or `noSuchChild` for a handle naming no live child
- `__Builtins.subprocessStderrState(handle)` returns __SubprocessStreamState - The same for its stderr. A reader's short answer is the same bytes at end of stream and on a refusal; these two say which, and `stdlib/Subprocess.maxon` throws on the difference
- `__Builtins.managedIsNull(managed)` returns int - 1 if a __ManagedMemory carries an empty (NUL-terminated) buffer, else 0

**Primitive:**
- `__Builtins.floatToBits(value)` returns int - Bitcast float to int
- `__Builtins.bitsToFloat(bits)` returns float - Bitcast int to float (the exact inverse of `floatToBits`)

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

<!-- test: builtins-type.bits-to-float -->
```maxon
function main() returns ExitCode
	let value = __Builtins.bitsToFloat(4607182418800017408)
	// IEEE 754: 0x3FF0000000000000 = 4607182418800017408 is exactly 1.0.
	// A numeric conversion would instead yield ~4.6e18, so this distinguishes
	// a bitcast from a widening int-to-float conversion.
	if value == 1.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: builtins-type.bits-to-float-round-trip -->
`bitsToFloat` and `floatToBits` are exact inverses, so composing them in either
order is the identity. Round-tripping bit patterns that no numeric conversion
could reproduce — the smallest subnormal, the largest subnormal, negative zero —
proves neither direction is converting numerically.
```maxon
// Negative zero's pattern is 0x8000000000000000, so the alias must span the
// full signed 64-bit range to carry every IEEE 754 double's bits.
typealias FloatBits = int(i64.min to i64.max)

function roundTripBits(bits FloatBits) returns FloatBits
	return __Builtins.floatToBits(__Builtins.bitsToFloat(bits))
end 'roundTripBits'

function main() returns ExitCode
	// Smallest positive subnormal (0x0000000000000001). Numeric conversion of
	// the integer 1 would give 1.0, whose bits are 4607182418800017408.
	if roundTripBits(1) != 1 'subnormalMin'
		return 1
	end 'subnormalMin'

	// Largest subnormal (0x000FFFFFFFFFFFFF).
	if roundTripBits(4503599627370495) != 4503599627370495 'subnormalMax'
		return 2
	end 'subnormalMax'

	// Positive zero (0x0000000000000000).
	if roundTripBits(0) != 0 'positiveZero'
		return 3
	end 'positiveZero'

	// 1.0 (0x3FF0000000000000).
	if roundTripBits(4607182418800017408) != 4607182418800017408 'one'
		return 4
	end 'one'

	// Negative zero: its bit pattern is 0x8000000000000000, which `==` on
	// floats cannot detect because -0.0 == 0.0. Comparing bits does.
	let negativeZeroBits = __Builtins.floatToBits(-0.0)
	if roundTripBits(negativeZeroBits) != negativeZeroBits 'negativeZero'
		return 5
	end 'negativeZero'

	// Sign bit through the float-first composition.
	if __Builtins.bitsToFloat(__Builtins.floatToBits(-2.5)) != -2.5 'negativeValue'
		return 6
	end 'negativeValue'

	if __Builtins.bitsToFloat(__Builtins.floatToBits(0.5)) != 0.5 'half'
		return 7
	end 'half'

	return 0
end 'main'
```
```exitcode
0
```

### Bitcast argument types — rejections

The two bitcast intrinsics are each other's inverse, so their argument types are
not interchangeable. Passing the wrong one is always a mistake, and is rejected
rather than reinterpreted.

<!-- disabled-test: builtins-type.error.bits-to-float-float-arg -->
<!-- E3005 voice: shv2 says "'__Builtins.bitsToFloat' requires a int, but its argument is float" at the CALLEE column; the oracle says "type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'float'" at the ARGUMENT column. Same verdict, same code, different voice — aligning `ParseError.builtinOperandType` moves every `__Builtins.*` and `subp*` refusal at once and is pinned by builtins-clock.md, builtins-sleep.md, console-stdin.md and process-executable-path.md, so it is its own rung -->
A `float` argument to `bitsToFloat` is almost always a `floatToBits` that was
meant instead.
```maxon
function main() returns ExitCode
	let v = __Builtins.bitsToFloat(1.0)
	return trunc(v)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtins-type/builtins-type.error.bits-to-float-float-arg.test:3:33: type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'float'
```

<!-- disabled-test: builtins-type.error.bits-to-float-managed-arg -->
<!-- E3005 voice: shv2 says "'__Builtins.bitsToFloat' requires a int, but its argument is float" at the CALLEE column; the oracle says "type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'float'" at the ARGUMENT column. Same verdict, same code, different voice — aligning `ParseError.builtinOperandType` moves every `__Builtins.*` and `subp*` refusal at once and is pinned by builtins-clock.md, builtins-sleep.md, console-stdin.md and process-executable-path.md, so it is its own rung -->
A managed value is a heap pointer, and a heap pointer is not a float's bit
pattern. This one matters most: the pointer shares the integer representation,
so before the check existed this program compiled clean and bitcast the String's
handle into a garbage double.
```maxon
function main() returns ExitCode
	let v = __Builtins.bitsToFloat("x")
	return trunc(v)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtins-type/builtins-type.error.bits-to-float-managed-arg.test:3:33: type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'String'
```

<!-- disabled-test: builtins-type.error.bits-to-float-bool-arg -->
<!-- E3005 voice: shv2 says "'__Builtins.bitsToFloat' requires a int, but its argument is float" at the CALLEE column; the oracle says "type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'float'" at the ARGUMENT column. Same verdict, same code, different voice — aligning `ParseError.builtinOperandType` moves every `__Builtins.*` and `subp*` refusal at once and is pinned by builtins-clock.md, builtins-sleep.md, console-stdin.md and process-executable-path.md, so it is its own rung -->
```maxon
function main() returns ExitCode
	let v = __Builtins.bitsToFloat(true)
	return trunc(v)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtins-type/builtins-type.error.bits-to-float-bool-arg.test:3:33: type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'bool'
```

<!-- disabled-test: builtins-type.error.float-to-bits-int-arg -->
<!-- E3005 voice: shv2 says "'__Builtins.bitsToFloat' requires a int, but its argument is float" at the CALLEE column; the oracle says "type mismatch: __Builtins.bitsToFloat argument 0 expects 'i64' but got 'float'" at the ARGUMENT column. Same verdict, same code, different voice — aligning `ParseError.builtinOperandType` moves every `__Builtins.*` and `subp*` refusal at once and is pinned by builtins-clock.md, builtins-sleep.md, console-stdin.md and process-executable-path.md, so it is its own rung -->
The mirror rejection: `floatToBits` takes the float, not the pattern.
```maxon
function main() returns ExitCode
	let v = __Builtins.floatToBits(7)
	return v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/builtins-type/builtins-type.error.float-to-bits-int-arg.test:3:33: type mismatch: __Builtins.floatToBits argument 0 expects 'f64' but got 'int'
```

<!-- test: builtins-type.direct-write-stdout -->
`__Builtins.writeStdout` returns the byte count written — an impure result like
any other function's, so a bare statement-position call is rejected and the
result must be explicitly discarded with `_ =`.
```maxon
function main() returns ExitCode
	let s = "direct\n"
	_ = __Builtins.writeStdout(s.toByteArray().managed)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
direct
```
