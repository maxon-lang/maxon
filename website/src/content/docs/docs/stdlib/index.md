---
title: Standard Library
description: An overview of Maxon's standard library and its core functions.
sidebar:
  order: 1
---

## Overview

The standard library is a directory of ordinary Maxon source files, `stdlib/`, that ships beside the
compiler. Every program is compiled together with it, so there is nothing to import: `print`, `String`,
`Array`, `File`, `Json` and everything else on these pages are in scope in every file.

Most modules are a type used as a namespace (`File.readText`, `Clock.nowNanos`, `Json.parse`) or a type
with instance methods (`String`, `Array`, `FilePath`). A handful of names are free functions (`print`,
`printError`, `sleep`, `sha256`, `spreadHash`) and a handful are type aliases used across the library
(`ByteArray`, `StringArray`, `ExitCode`).

Generic types are used through a type alias that names the element type:

```maxon
typealias Score = int(0 to 100)
typealias ScoreArray = Array with Score

function main() returns ExitCode
	var scores = ScoreArray.create()
	scores.push(90)
	print("{scores.count()}\n")
	return 0
end 'main'
```

### Pages

| Page | Sections |
|------|----------|
| [Core](#overview) | [Core Functions](#core-functions) |
| [Text](/docs/stdlib/text/#string) | String, Character, Ascii, Unicode, CharacterSet |
| [Collections](/docs/stdlib/collections/#array) | Array, List, Map, Set, Vector, Range, Iterators, Interfaces |
| [I/O and processes](/docs/stdlib/io/#file) | File, FilePath, Directory, Console, CommandLine, Log, Process, Subprocess, SharedMemory |
| [Network](/docs/stdlib/network/#tcpclient) | TcpClient, TcpListener, HttpClient, URL |
| [Data](/docs/stdlib/data/#json) | Json, Sha256, Hasher |
| [System](/docs/stdlib/runtime/#clock) | Clock, Scheduler, Math, Primitive Extensions |
| [Testing](/docs/stdlib/testing/) | Testing |
| [Build](/docs/stdlib/build/) | Build |

### Names available in every file

| Name | Definition | Declared by |
|------|------------|-------------|
| `ExitCode` | `int(0 to u32.max)` on Windows, `int(0 to 255)` elsewhere | Process |
| `Byte` | `int(0 to u8.max)` | File |
| `ByteArray` | `Array with Byte` | File |
| `StringArray` | `Array with String` | Json |
| `BytePos`, `GraphemeIndex` | `int(0 to u64.max)` | String |
| `Codepoint` | `int(0 to 1114111)` | Character |
| `CodepointDelta` | `int(-1114111 to 1114111)` | Character |
| `AsciiValue` | `int(0 to 127)` | Character |
| `HashValue` | `int(0 to u32.max)` | Interfaces |
| `IterStep` | `int(0 to u64.max)` | Interfaces |
| `RangeBound` | `int(i64.min to i64.max)` | Range |
| `Real` | `float(f64.min to f64.max)` | Math |
| `HashDigest` | `bits(64)` | Hasher |
| `SourceLineNumber` | `int(1 to i32.max)` | Builtins |
| `FileSize`, `Timestamp` | `int(0 to u64.max)` | File |
| `DurationMs`, `InstantMs`, `DurationNanos`, `InstantNanos`, `UnixSeconds` | `int(0 to u64.max)` | Clock |
| `SchedulerProcessorCount` | `int(1 to i64.max)` | Scheduler |
| `NetworkPort` | `int(0 to 65535)` | TcpClient |
| `EnvMap` | `Map with String, String` | Subprocess |
| `JsonNodeId` / `JsonNodeIdArray` | `int(0 to u64.max)` / `Array with JsonNodeId` | Json |
| `SegmentByteCount`, `SegmentOffset`, `SegmentWord` | see [SharedMemory](/docs/stdlib/io/#sharedmemory) | SharedMemory |

### Names a library signature asks for

A signature may not name a type less visible than the function itself, so every alias and type a `public`
library signature mentions is `public` too and can be written down in your own code — as a cast target, or
to declare a value you are about to pass in. They are listed here because they are part of the surface, not
because a program normally spells them: a value cast to the alias the signature asks for is the usual reason
to name one.

| Name | Definition | Declared by |
|------|------------|-------------|
| `ElementIndex` | `int(0 to u64.max)` | Array, Vector |
| `ReportedCapacity` | `int(i64.min to i64.max)` | Array |
| `NodeCount`, `NodeIndex` | `int(0 to u64.max)` | List |
| `EntryCount` | `int(0 to 4611686018427387904)` | Map |
| `MemberCount` | `int(0 to 4611686018427387904)` | Set |
| `IterPos` | `int(0 to u64.max)` | Range |
| `ElementTransform`, `ElementPredicate` | `function(Element) returns Element` / `returns bool`, on the `Iterable` extension | Interfaces |
| `Utf8ByteCount` | `int(0 to u64.max)` | Character |
| `JsonInt` | `int(i64.min to i64.max)` | Json |
| `JsonFloat` | `float(f64.min to f64.max)` | Json |
| `ChildCount`, `ChildIndex` | `int(0 to u64.max)` | Json |
| `Milliseconds` | `int(0 to u64.max)` | Sleep |
| `Milliseconds` | `int(0 to 4294967295)` — a socket timeout | TcpClient |
| `Pid`, `ByteLimit` | `int(0 to u64.max)` | Subprocess |
| `ExitInt` | `int(0 to u32.max)` | Subprocess |
| `EnvSourceValue` | `int(0 to 1)` | Subprocess |
| `StdioKindValue` | `int(0 to 5)` | Subprocess |
| `SpawnEnvironment`, `StdioRuntimeTriple` | the records `Subprocess` hands the runtime | Subprocess |
| `PortNumber` | `int(0 to 65535)` | URL |
| `AssertedInt` | `int(i64.min to i64.max)` | Testing |
| `AssertedReal` | `float(f64.min to f64.max)` | Testing |
| `Tolerance` | `float(0.0 to f64.max)` | Testing |

`Byte` and `BytePos` are declared by several modules at one definition each; see the table above.

### Target support

Everything that is pure computation (strings, collections, `Json`, `Sha256`, `Hasher`, `Math`, `URL`
parsing) works on every target. Operating-system facilities are available on `x64-windows`,
`arm64-macos`, `arm64-linux` and `x64-linux`.

On `wasm32-wasi` a call that needs a facility the target does not provide is refused **at compile time**,
at the call site, rather than failing at run time:

| Refused on `wasm32-wasi` | Error |
|--------------------------|-------|
| `File`, `Directory`, `Console`, `CommandLine` | E3104 |
| `Clock`, `WallClock`, `sleep`, `Scheduler.yield`, `Scheduler.processorCount` | E3104 |
| `TcpClient`, `TcpListener`, `HttpClient` | E3104 |
| `Process.executablePath`, `SharedSegment` | E3104 |
| `Subprocess`, `StreamingSubprocess`, `Configuration`, `Process.environmentVariable` | E3074 |

E3104 means this compiler has not implemented the facility for the target; E3074 means the platform has
no process-spawn primitive at all. Guard such calls with `#if not os(Wasi)`.

### Compiler-managed resources

Files, directory searches and sockets are held by compiler-managed handle types (`__ManagedFile`,
`__ManagedDirectory`, `__ManagedSocket`) that release the operating-system resource when their last
reference goes out of scope. Programs use them only through the library types (`File`, `Directory`,
`TcpClient`, `TcpListener`); names beginning with `__` are reserved for the compiler and the library.

### The runtime tier

A `runtime/` directory ships beside `stdlib/`, and the compiler loads both on every compile; a compiler
with only one of them beside it cannot compile anything. The runtime that every program links against is
written there in Maxon, with privileges no other source has: it may declare `__`-prefixed names and call the
raw `__Raw.*` intrinsics. A program cannot reach into it:

| Rule | Error |
|------|-------|
| A `__Raw.*` call outside `runtime/` | E3152 |
| Calling a runtime entry from outside the tier | E3004 |
| Naming a runtime entry as a function value | E3155 |

The rules for the runtime files themselves are in [The Runtime Tier](/docs/language/overview/).

## Core Functions

These are free functions and compiler builtins available everywhere.

### Output and waiting

| Function | Description |
|----------|-------------|
| `print(value String)` | Write `value` to standard output. No newline is added. |
| `printError(value String)` | Write `value` to standard error. |
| `sleep(milliseconds int(0 to u64.max))` | Suspend the current green thread; other green threads run meanwhile. |
| `panic(message String)` | Stop the program: prints `panic at <file>:<line>: <message>` and a stack trace to stderr, exits with code 1. |

To print something that is not a `String`, interpolate it: `print("{count}\n")`.

### Numeric builtins

| Function | Returns | Description |
|----------|---------|-------------|
| `abs(x float)` | `float` | Absolute value. An integer argument is promoted to `float`. |
| `sqrt(x float)` | `float` | Square root. |
| `floor(x float)` | `float` | Round toward negative infinity. |
| `ceil(x float)` | `float` | Round toward positive infinity. |
| `round(x float)` | `float` | Round to nearest; halfway cases round to even (`round(2.5)` is `2.0`). |
| `trunc(x float)` | `int` | Truncate toward zero; the way to turn a `float` into an `int`. |
| `min(a float, b float)` | `float` | The smaller value. Integer arguments are promoted. |
| `max(a float, b float)` | `float` | The larger value. Integer arguments are promoted. |

Trigonometry, logarithms and powers are in [Math](/docs/stdlib/runtime/#math).

### Compile-time builtins

| Builtin | Description |
|---------|-------------|
| `sizeof(TypeName)` | Storage size of a type in bytes, as a constant: `int` and `float` are 8, `bool` and `byte` are 1. |
| `countof(TypeName)` | How many elements a fixed-size container type holds, such as `Vector with 3 Coord`. A type with no fixed element count is refused; ask an `Array` value for `count()`. |
| `__file__`, `__line__` | Legal only as a parameter's default value; each expands at the **call site**. |

`__line__` parameters are declared `SourceLineNumber` and `__file__` parameters `String`. The file is the
calling file's path relative to the compile root, `/`-separated on every host. Using either anywhere other
than a parameter default is error E2060.

```maxon
function check(ok bool, from String = __file__, at SourceLineNumber = __line__) returns bool
	if not ok 'bad'
		printError("{from}:{at}: check failed\n")
	end 'bad'

	return ok
end 'check'

function main() returns ExitCode
	_ = check(1 + 1 == 2)
	return 0
end 'main'
```

### Parsing text into numbers

`int`, `float`, `bool` and `byte` each have a static `fromString` that throws `ParseError.invalidFormat`
on malformed input. A type of your own can offer the same shape by implementing `Parsable`
(`static function fromString(input String) returns Self throws <error>`).

```maxon
function main() returns ExitCode
	let n = try int.fromString("42") otherwise 0
	let f = try float.fromString("3.25") otherwise 0.0
	let b = try bool.fromString("true") otherwise false
	let y = try byte.fromString("255") otherwise 0
	let bad = try int.fromString("x1") otherwise -1

	print("{n} {f} {b} {y} {bad}\n")
	print("{abs(-5.5)} {floor(-3.2)} {round(2.5)} {trunc(-3.7)} {min(3.0, 5.0)}\n")
	sleep(1)
	printError("done\n")
	return 0
end 'main'
```

Output: `42 3.25 true 255 -1` and `5.5 -4.0 2.0 -3 3.0` on stdout, `done` on stderr.

Interpolation also takes format specifiers — `"{255:x}"` is `ff`, `"{7:04}"` is `0007`, `"{3.14159:.2}"`
is `3.14`. See string interpolation in [LANGUAGE_REFERENCE.md](/docs/language/overview/).
