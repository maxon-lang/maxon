# Maxon Standard Library Reference

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
| [Text](#string) | String, Character, Ascii, Unicode, CharacterSet |
| [Collections](#array) | Array, List, Map, Set, Vector, Range, Iterators, Interfaces |
| [I/O and processes](#file) | File, FilePath, Directory, Console, CommandLine, Log, TraceCapture, Process, Subprocess, SharedMemory, Debuggee, Sampler |
| [Network](#tcpclient) | TcpClient, TcpListener, HttpClient, HttpServer, URL |
| [Data](#json) | Json, Base64, Pem, Der, Sha2, Hmac, Hkdf, Rsa, Aead, AesGcm, ChaCha20Poly1305, Aes, ChaCha20, Poly1305, X25519, Ed25519, EcdhP256, Ecdsa, EcdsaP256, Hasher |
| [System](#clock) | Clock, Scheduler, HeapStats, SharedValue, Random, Math, Primitive Extensions |
| [Testing](#testing) | Testing |
| [Build](#build) | Build |

### Names available in every file

| Name | Definition | Declared by |
|------|------------|-------------|
| `ExitCode` | `int(0 to u32.max)` on Windows, `int(0 to 255)` elsewhere | Process |
| `Byte` | `int(0 to u8.max)` | String |
| `ByteArray` | `Array with Byte` | File |
| `Count` | `int(0 to u64.max) implements ElementIndex` — what a collection's `count()` returns | Array |
| `StringArray` | `Array with String` | Json |
| `CharSet` | `Set with Character` | CharacterSet |
| `FilePathArray` | `Array with FilePath` | Directory |
| `FullRangeInt` / `FullRangeIntArray` | `int(i64.min to i64.max)` / `Array with FullRangeInt` | Builtins |
| `FullRangeFloat` | `float(f64.min to f64.max)` | Builtins |
| `IndentDepth` | `int(0 to u64.max)` — a nesting depth while `Json` renders | Json |
| `RandomDraw` / `RandomBound` / `RandomByteCount` | `int(0 to u64.max)` / `int(1 to u64.max)` / `int(0 to u64.max)` | Random |
| `BytePos`, `GraphemeIndex` | `int(0 to u64.max)` | String |
| `Codepoint` | `int(0 to 1114111)` | Character |
| `CodepointDelta` | `int(-1114111 to 1114111)` | Character |
| `AsciiValue` | `int(0 to 127)` | Character |
| `HashValue` | `int(0 to u32.max)` | Interfaces |
| `IterStep` | `int(0 to u64.max)` | Interfaces |
| `RangeBound` | `int(i64.min to i64.max)` | Range |
| `HashDigest` | `bits(64)` | Hasher |
| `SourceLineNumber` | `int(1 to i32.max)` | Builtins |
| `FileSize`, `Timestamp` | `int(0 to u64.max)` | File |
| `DurationMs`, `InstantMs`, `DurationNanos`, `InstantNanos` | `int(0 to u64.max)` | Clock |
| `UnixSeconds`, `UnixNanos`, `UnixDays` | `int(i64.min to i64.max)` | Clock |
| `SchedulerProcessorCount` | `int(1 to u64.max)` | Scheduler |
| `MailboxCapacity` | `int(1 to 2147483648)` — how many messages a service's mailbox may queue, set by `spawn(capacity: n)` ([Services](LANGUAGE_REFERENCE.md#mailboxes-and-failed-sends)) | Builtins |
| `DefaultMailboxCapacity` | the constant `1024 as MailboxCapacity` — the capacity a bare `spawn` gives a mailbox | Builtins |
| `ForeverMs` | the constant `u64.max as Milliseconds` — a `sendTimeout:` that waits for room as long as it takes, untimed ([Services](LANGUAGE_REFERENCE.md#mailboxes-and-failed-sends)) | Sleep |
| `NetworkPort` | `int(0 to 65535)` | TcpClient; also `URL.port()` |
| `EnvMap` | `Map with String, String` | Subprocess |
| `JsonNodeId` / `JsonNodeIdArray` | `int(0 to u64.max)` / `Array with JsonNodeId` | Json |
| `SegmentByteCount`, `SegmentOffset`, `SegmentWord` | see [SharedMemory](#sharedmemory) | SharedMemory |

A program may declare one of these names itself. Its declaration is then what the bare name means in every
file of the program, and the library's is reached as `stdlib.ByteArray` (see
[Qualified Names](LANGUAGE_REFERENCE.md#qualified-names)); the two are two types, and a value crossing
between them needs a cast. Prefer the library's alias where it fits, and otherwise give your own a
different name. Two project declarations of one name a file can both see are **E3063** in every file,
the declaring ones included.

### Names a library signature asks for

A signature may not name a type less visible than the function itself, so every alias and type a `public`
library signature mentions is `public` too and can be written down in your own code — as a cast target, or
to declare a value you are about to pass in. They are listed here because they are part of the surface, not
because a program normally spells them: a value cast to the alias the signature asks for is the usual reason
to name one. Float aliases are distinct types as integer aliases are, so a value of a program's own float
alias reaches a `FullRangeFloat` or `JsonFloat` parameter through `as`; a float literal needs no cast.

| Name | Definition | Declared by |
|------|------------|-------------|
| `ElementIndex` | `int(0 to u64.max)` | Array |
| `ReportedCapacity` | `int(i64.min to i64.max)` | Array |
| `EntryCount` | `int(0 to 9223372036854775808)` | Map |
| `MemberCount` | `int(0 to 9223372036854775808)` | Set |
| `IterPos` | `int(0 to u64.max)` | Range |
| `ElementTransform`, `ElementPredicate` | `function(Element) returns Element` / `returns bool`, on the `Iterable` extension | Interfaces |
| `TableCapacity` | `int(0 to 9223372036854775808)` | Map, Set |
| `GreenThreadHandle` | `int(0 to u64.max)` — the handle a `Promise` holds | Builtins |
| `JsonInt` | `int(i64.min to i64.max)` | Json |
| `JsonFloat` | `float(f64.min to f64.max)` | Json |
| `Milliseconds` | `int(0 to u64.max)` | Sleep |
| `CivilYear` / `CivilMonth` / `CivilDay` | `int(i64.min to i64.max)` / `int(1 to 12)` / `int(1 to 31)` | Clock |
| `LogInteger`, `LogRank` | `int(i64.min to i64.max)` / `int(i32.min to i32.max)` | Log |
| `LogReal` | `float(f64.min to f64.max)` | Log |
| `LogGroupPath`, `LogFieldArray` | `Array with String` / `Array with LogField` | Log |
| `TraceKeyArray` | `Array with String` | TraceCapture |
| `SocketDeadlineMs` | `int(0 to 4294967295)` — a socket deadline | TcpClient |
| `RecvCapacity` | `int(1 to i64.max)` | TcpClient |
| `HttpByteCap` | `int(0 to 1099511627776)` | HttpServer |
| `HttpAcceptRetryMs` | `int(0 to 1000)` | HttpServer |
| `Pid`, `ByteLimit` | `int(0 to u64.max)` | Subprocess |
| `ExitInt` | `int(0 to u32.max)` | Subprocess |
| `EnvSourceValue` | `int(0 to 1)` | Subprocess |
| `StdioKindValue` | `int(0 to 5)` | Subprocess |
| `SpawnEnvironment`, `StdioRuntimeTriple` | the records `Subprocess` hands the runtime | Subprocess |
| `AssertedReal` | `float(f64.min to f64.max)` | Testing |
| `Tolerance` | `float(0.0 to f64.max)` | Testing |

`Byte` and `BytePos` are declared once, in `String` (see the table above). `BytePos`, `GraphemeIndex`,
`JsonNodeId` and `Count` implement `ElementIndex`; `EntryCount`, `TableCapacity` and
`SchedulerProcessorCount` implement `Count`; and `MemberCount` implements `EntryCount`. So each indexes an
`Array` with no cast, and any two of them compare with no cast through their common ancestor; an index of
any other alias, or of a non-integer type, is **E3005** (see
[Subtypes With `implements`](LANGUAGE_REFERENCE.md#subtypes-with-implements)). An `ElementIndex` goes where
a `Count` is declared only by cast.

`Array`, `List` and `Vector` `count()`, `JsonDoc.arrayLength`, `Character.byteLength()` and the `count()` of a
string's codepoint and UTF-16 views return `Count`. `String.count()` returns a `GraphemeIndex` and a byte
view's `count()` a `BytePos`.

The inner typealiases a library type's `public` members name are `public` as well and can be written
qualified: `Array.ElementIterator`, `Array.ElementMemory`, `Map.Entry`, `List.ListIter`, `Set.SetIter`,
`Vector.VectorIter` and the like. The others are `Array.ElementIterable`, `Array.ArrayIter`, `Array.ElementArray`,
`HttpHeaders.CaseInsensitiveHeaders`, `Iterable.WithIterSelf`, `List.EManagedList`,
`Map.IterKeyArray`, `Map.IterValueArray`, `Map.IterStateArray`, `Set.IterElementArray` and `String.ByteMemory`.

### Target support

Everything that is pure computation (strings, collections, `Json`, `Base64`, `Pem`, `DerReader`, the SHA-2, HMAC
and HKDF functions, `Hasher`, `Math`, `URL` parsing) works on every target, and so does `Random`. Operating-system facilities are available on `x64-windows`,
`arm64-macos`, `arm64-linux` and `x64-linux`.

On `wasm32-wasi` a call that needs a facility the target does not provide is refused **at compile time**,
at the call site, rather than failing at run time:

| Refused on `wasm32-wasi` | Error |
|--------------------------|-------|
| `File`, `Directory`, `Console`, `CommandLine` | E3104 |
| `Clock`, `WallClock`, `sleep`, `MachinePin`, every `Scheduler` member but `parallelBoundary` | E3104 |
| `Log`, `Logger`, `LogSink` and the log handlers: logging runs on services, which need green threads | E3104 |
| `TcpClient`, `TcpListener`, `HttpClient`, `HttpServer` | E3104 |
| `Process.executablePath`, `FilePath.resolvedPhysically`, `SharedSegment` | E3104 |
| `Subprocess`, `StreamingSubprocess`, `Configuration`, `Process.environmentVariable` | E3074 |

E3104 means this compiler has not implemented the facility for the target; E3074 means the platform has
no process-spawn primitive at all. Guard such calls with `#if not os(Wasi)`.

### Text at the operating-system boundary

Text the operating system hands a program, or takes from it, is UTF-8 on every target: command-line
arguments, environment variable names and values, file and directory names and the names a directory
listing answers, the working directory, the executable path and shared-memory section names. On
`x64-windows` the runtime converts that text to and from UTF-16 at each call, so a name holding any
Unicode character reaches the OS intact, and a `Subprocess` child receives its arguments, environment
and working directory as UTF-16. Invalid UTF-8 handed to the OS, and an unpaired UTF-16 surrogate handed
back, each become U+FFFD.

### Compiler-managed resources

Files, directory searches and sockets are held by compiler-managed handle types (`__ManagedFile`,
`__ManagedDirectory`, `__ManagedSocket`) that release the operating-system resource when their last
reference goes out of scope. Programs use them through the library types (`File`, `Directory`,
`TcpClient`, `TcpListener`); names beginning with `__` are reserved for the compiler and the library.

### The library tier

The raw surface the library is written against belongs to the library tier: a source file under `stdlib/` or
`runtime/`. Anywhere else each of these is
[E3185](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3185) (see
[Raw Memory and the Library Tier](LANGUAGE_REFERENCE.md#raw-memory-and-the-library-tier)):

- every `__Builtins.*` intrinsic;
- `.managed`, `__ManagedMemory` and `__ManagedMemoryCursor`;
- `String.cstr()` and `cstring`;
- `Promise.inner`;
- the raw handle types `__ManagedFile`, `__ManagedDirectory`, `__ManagedSocket`, `__ManagedPages`,
  `__ManagedHandle` and `__ManagedPin`.

A program reaches the same facilities through the library:

| Reading or action | API |
|-------------------|-----|
| The allocator's counters, levels, census and scavenger | [HeapStats](#heapstats) |
| The machine's uptime, a thread's CPU time | `Clock.osTickMs()`, `Clock.threadCpuTicks()` |
| This process's id, namespace and priority; another process's liveness | `Process.id()`, `Process.namespace()`, `Process.enterBackgroundPriority()`, `Process.isAlive(pid)` |
| Logical CPUs, scheduler counters, stack size, pinning | `Scheduler.machineCpuCount()`, `Scheduler.counters()`, `Scheduler.currentStackBytes()`, `Scheduler.pinToMachine()`, [`MachinePin`](#machinepin) |
| A float's IEEE 754 bits | `Math.bitsOf(x)`, `Math.fromBits(bits)` |
| Terminal colour, raw standard input | `Console.stdoutWantsColor()`, `Console.stdin().readBytes(n)` |
| Whether a promise has finished; the first of several | `p.isComplete()`, `promises.awaitAny()` |
| Driving or sampling another process | [Debuggee](#debuggee), [Sampler](#sampler) |

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

The rules for the runtime files themselves are in [The Runtime Tier](LANGUAGE_REFERENCE.md).

### The scriptlib tier

A `scriptlib/` directory beside `stdlib/` holds your own packages, and a program whose root is one file
compiles them with the standard library. Every file in it is yours. A package is compiled as the library
is, with the raw surface above closed to it as it is to a program
([E3185](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3185)). See
[The scriptlib directory](CLI_REFERENCE.md#the-scriptlib-directory).

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
| `abs(x float)` | `float` | Absolute value. A value of an integer alias is converted to `float`. |
| `sqrt(x float)` | `float` | Square root. A value of an integer alias is converted. |
| `floor(x float)` | `float` | Round toward negative infinity. |
| `ceil(x float)` | `float` | Round toward positive infinity. |
| `round(x float)` | `float` | Round to nearest; halfway cases round to even (`round(2.5)` is `2.0`). |
| `trunc(x float)` | `FullRangeInt` | Truncate toward zero; the way to turn a `float` into an integer. Cast the result to reach a narrower alias. |
| `min(a float, b float)` | `float` | The smaller value. Integer arguments are converted. |
| `max(a float, b float)` | `float` | The larger value. Integer arguments are converted. |

A float result whose range the compiler knows — from a literal, a `let` constant or another range-known
value — carries that range and fits any float alias admitting it; any other is a `FullRangeFloat`.

Trigonometry, logarithms and powers are in [Math](#math).

### Compile-time builtins

| Builtin | Description |
|---------|-------------|
| `sizeof(TypeName)` | Storage size of a type in bytes, as a constant `Count`: `int` and `float` are 8, `bool` and `byte` are 1. |
| `countof(TypeName)` | How many elements a fixed-size container type holds, as a `Count`, such as `Vector with 3 Coord`. A type with no fixed element count is refused; ask an `Array` value for `count()`. |
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
is `3.14`. See string interpolation in [LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md).

## String

`String` is an immutable-by-default UTF-8 string. Its unit of iteration and counting is the **grapheme
cluster**, a user-perceived character: `"é👍🏽".count()` is 2, although it is 10 bytes and 3 codepoints.
Positions are `StringIndex` values, which carry both a grapheme index and a byte position so stepping
never rescans the string.

`String` implements `Hashable`, `Equatable`, `Comparable` (byte order, a shorter prefix first), `Iterable`
(over `Character`) and `Cloneable`.

```maxon
function main() returns ExitCode
	let s = "Hello, World"
	let parts = "a,b,,c".split(",")
	print("{parts.count()}\n")
	print("{"x-y-z".replace("-", with: "+")} {"x-y-z".replaceFirst("-", with: "+")}\n")

	let comma = try s.findFirst(",") otherwise s.endIndex()
	let head = s.slice(s.startIndex(), endIndex: comma)
	let five = s.slice(s.startIndex(), length: 5)
	print("{comma.charIndex()} {head} {five}\n")

	for c in "héllo" 'each'
		print("[{c}]")
	end 'each'

	print("\n{"  padded  ".trim()}|{"Hello".toUpper()}\n")
	return 0
end 'main'
```

Output: `4`, `x+y+z x+y-z`, `5 Hello Hello`, `[h][é][l][l][o]`, `padded|HELLO`.

### Construction and conversion

| Member | Returns | Description |
|--------|---------|-------------|
| `String.from(bytes ByteArray)` | `String` | A string over the bytes. The bytes are not validated as UTF-8. |
| `String.init(managed)` | `String` | The compiler's literal initializer, over library-tier storage; programs write a literal. |
| `toByteArray()` | `ByteArray` | The UTF-8 bytes as a new, independent array. |
| `cstr()` | `cstring` | Library tier ([E3185](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3185) elsewhere): a NUL-terminated view of the bytes, for an intrinsic that takes `cstring`. Valid while the string is alive and unmodified. |
| `clone()` | `String` | An independent copy; writes to either never show through the other. |

### Properties

| Member | Returns | Description |
|--------|---------|-------------|
| `count()` | `GraphemeIndex` | Number of grapheme clusters. O(n) for non-ASCII text; cache it if you need it repeatedly. |
| `byteLength()` | `BytePos` | Number of UTF-8 bytes. |
| `isEmpty()` | `bool` | True when the string has no bytes. |
| `isAscii()` | `bool` | True when every byte is below 128. |
| `hash()` | `HashValue` | Through `Hashable`. |
| `equals(other String)` | `bool` | Byte-wise equality; `==` calls it. |

### Search

| Member | Returns | Description |
|--------|---------|-------------|
| `contains(needle String)` | `bool` | Substring test. |
| `contains(character Character)` | `bool` | Character test. |
| `startsWith(prefix String)` | `bool` | Prefix test. |
| `endsWith(suffix String)` | `bool` | Suffix test. |
| `findFirst(needle String)` | `StringIndex` | First occurrence. Throws `StringError.notFound`. |
| `findLast(needle String)` | `StringIndex` | Last occurrence. Throws `StringError.notFound`. |

### Indexing and Slicing

| Member | Returns | Description |
|--------|---------|-------------|
| `startIndex()` | `StringIndex` | Index of the first grapheme. |
| `endIndex()` | `StringIndex` | One past the last grapheme. |
| `indexAfter(idx StringIndex)` | `StringIndex` | Next grapheme boundary. Throws `StringError` at `endIndex()`. |
| `indexBefore(idx StringIndex)` | `StringIndex` | Previous grapheme boundary. Throws `StringError` at `startIndex()`. |
| `charAt(idx StringIndex)` | `Character` | The grapheme at `idx`. |
| `slice(start StringIndex, endIndex StringIndex)` | `String` | The graphemes in `[start, endIndex)`. |
| `slice(start StringIndex, length GraphemeIndex)` | `String` | `length` graphemes starting at `start`. |

`StringIndex` implements `Equatable` and `Comparable`:

| Member | Returns | Description |
|--------|---------|-------------|
| `StringIndex.create(charIndex GraphemeIndex, bytePos BytePos)` | `StringIndex` | Build an index from both coordinates. |
| `charIndex()` | `GraphemeIndex` | Grapheme position. |
| `bytePos()` | `BytePos` | UTF-8 byte offset. |

```maxon
function main() returns ExitCode
	let s = "héllo"
	var i = s.endIndex()

	while i != s.startIndex() 'backwards'
		i = try s.indexBefore(i) otherwise break
		print("{s.charAt(i)}")
	end 'backwards'

	print("\n")
	return 0
end 'main'
```

Output: `olléh`.

### Transforming

| Member | Returns | Description |
|--------|---------|-------------|
| `split(delimiter String)` | `StringArray` | The pieces between delimiters, empty pieces included. An empty delimiter, or an empty string, gives a one-element array. |
| `replace(old String, with String)` | `String` | Every occurrence replaced. An empty `old` returns a copy. |
| `replaceFirst(old String, with String)` | `String` | The first occurrence replaced. |
| `toLower()` | `String` | ASCII `A`–`Z` lowered; other characters are unchanged. |
| `toUpper()` | `String` | ASCII `a`–`z` raised; other characters are unchanged (`"Héllo".toUpper()` is `"HéLLO"`). |
| `append(other String)` | — | Append in place. |

### Trimming

| Member | Returns | Description |
|--------|---------|-------------|
| `trim()` | `String` | Remove Unicode whitespace and newlines from both ends. |
| `trimStart()` | `String` | From the start only. |
| `trimEnd()` | `String` | From the end only. |
| `trim(chars CharacterSet)` | `String` | Remove characters in `chars` from both ends. |
| `trimStart(chars CharacterSet)` | `String` | From the start only. |
| `trimEnd(chars CharacterSet)` | `String` | From the end only. |

The no-argument forms use `CharacterSet.whitespacesAndNewlines()`. Trimming walks grapheme clusters, so
`"\r\n"` is one unit: a set holding `'\r'` but not `"\r\n"` trims neither.

### Views

| Member | Returns | Iterates |
|--------|---------|----------|
| `bytes()` | `ByteView` | Each UTF-8 `Byte` |
| `codepoints()` | `CodepointView` | Each `Codepoint` |
| `utf16()` | `UTF16View` | Each UTF-16 code unit |
| `createIterator()` | `StringIterator` | Each `Character` (what `for c in s` uses) |

Each view has `count()` and `createIterator()`, and works in `for`-`in`. A `ByteView`'s `count()` is a
`BytePos`; a `CodepointView`'s and a `UTF16View`'s is a `Count`. A view reads the string's own bytes in place.
`StringIterator` implements `Iterator with Character`: `StringIterator.create(s)`, `current()`,
`advance()`.

### StringBuilder

`String.append` and `StringBuilder` both grow their buffer geometrically, so appending in a loop is amortized
constant time per byte with either. `StringBuilder` adds `reserve` for a known final size and a `clear` that
keeps the capacity for reuse; build with it and take the finished `String` at the end.

| Member | Returns | Description |
|--------|---------|-------------|
| `StringBuilder.create()` | `StringBuilder` | An empty builder. |
| `reserve(byteCount BytePos)` | — | Allocate room up front when the final size is known. |
| `append(other String)` | — | Append a piece. |
| `byteLength()` | `BytePos` | Bytes accumulated so far. |
| `isEmpty()` | `bool` | True when nothing has been appended. |
| `clear()` | — | Discard the contents, keeping the capacity. |
| `build()` | `String` | Hand the bytes over as a `String`. The builder is empty afterwards. |

```maxon
function main() returns ExitCode
	var out = StringBuilder.create()

	for i in 1 to 3 'each'
		out.append("item {i};")
	end 'each'

	let text = out.build()
	print("{text} {out.isEmpty()}\n")
	return 0
end 'main'
```

Output: `item 1;item 2;item 3; true`.

### Errors and aliases

```maxon
enum StringError implements Error
	notFound
	invalidIndex
end 'StringError'
```

`BytePos` and `GraphemeIndex` are both `int(0 to u64.max)`.

## Character

`Character` is one grapheme cluster: a character literal such as `'é'` or `'👍🏽'`, or what iterating a
`String` yields. It implements `Hashable`, `Equatable`, `Comparable` (byte-wise), `Stringable` and
`Cloneable`, and it works as the bound of a range: `for c in 'a' to 'e'`.

| Member | Returns | Description |
|--------|---------|-------------|
| `byteLength()` | `Count` | UTF-8 bytes in the cluster. |
| `codepoint()` | `Codepoint` | The first codepoint of the cluster. |
| `codepoints()` | `CodepointView` | Every codepoint of the cluster. |
| `bytes()` | `ByteView` | The UTF-8 bytes. |
| `asciiValue()` | `AsciiValue` | The value 0–127 of a single-byte ASCII character. Throws `CharacterError.notAscii` otherwise. |
| `advanceBy(n CodepointDelta)` | `Character` | The character `n` codepoints away, in either direction. |
| `toString()` | `String` | The cluster as a string. |
| `clone()` | `Character` | An independent copy. |
| `equals(other Character)`, `compare(other Character)`, `hash()` | | Interface conformances. |

```maxon
enum CharacterError implements Error
	notAscii
end 'CharacterError'
```

`AsciiValue` is `int(0 to 127)`; `Codepoint` is `int(0 to 1114111)`.

```maxon
function main() returns ExitCode
	let e = 'é'
	let a = try 'A'.asciiValue() otherwise 0
	let refused = try e.asciiValue() otherwise 0
	print("{e.codepoint()} {e.byteLength()} {a} {refused}\n")
	return 0
end 'main'
```

Output: `233 2 65 0`.

## Ascii

`Ascii` classifies a `Character` by ASCII rules only; any character outside ASCII answers `false`. For
Unicode classification use [CharacterSet](#characterset).

| Function | True when the character is |
|----------|----------------------------|
| `Ascii.isDigit(c Character)` | `0`–`9` |
| `Ascii.isAlpha(c Character)` | `a`–`z` or `A`–`Z` |
| `Ascii.isAlphanumeric(c Character)` | a letter or a digit |
| `Ascii.isUpper(c Character)` | `A`–`Z` |
| `Ascii.isLower(c Character)` | `a`–`z` |
| `Ascii.isWhitespace(c Character)` | space, tab, `\n` or `\r` |

```maxon
function main() returns ExitCode
	print("{Ascii.isDigit('7')} {Ascii.isAlpha('é')} {Ascii.isUpper('Q')} {Ascii.isWhitespace('\t')}\n")
	return 0
end 'main'
```

Output: `true false true true`.

## Unicode

| Function | Returns | Description |
|----------|---------|-------------|
| `Unicode.isWhitespace(cp Codepoint)` | `bool` | True for every codepoint with the Unicode `White_Space` property: tab through carriage return, space, NEL, no-break space, the U+2000 space block, line and paragraph separators, and the other spaces. |

```maxon
function main() returns ExitCode
	print("{Unicode.isWhitespace(32)} {Unicode.isWhitespace(160)} {Unicode.isWhitespace(65)}\n")
	return 0
end 'main'
```

Output: `true true false`.

## CharacterSet

A `CharacterSet` is a set of characters defined by Unicode general categories, explicit members, or both.
It is what the `String` trimming methods take.

| Factory | Contents |
|---------|----------|
| `CharacterSet.whitespaces()` | Space separators (Zs) and tab — no newlines |
| `CharacterSet.newlines()` | LF, CR, CRLF, VT, FF, NEL, line and paragraph separators |
| `CharacterSet.whitespacesAndNewlines()` | Both of the above |
| `CharacterSet.decimalDigits()` | Decimal digits (Nd) |
| `CharacterSet.letters()` | Letters and marks (L\*, M\*) |
| `CharacterSet.lowercaseLetters()` | Lowercase letters (Ll) |
| `CharacterSet.uppercaseLetters()` | Uppercase and titlecase letters (Lu, Lt) |
| `CharacterSet.alphanumerics()` | Letters, marks and numbers (L\*, M\*, N\*) |
| `CharacterSet.punctuation()` | Punctuation (P\*) |
| `CharacterSet.symbols()` | Symbols (S\*) |
| `CharacterSet.controlCharacters()` | Control and format characters (Cc, Cf) |
| `CharacterSet.from(chars CharSet)` | Exactly the characters given; `CharSet` is `Set with Character` |

| Member | Returns | Description |
|--------|---------|-------------|
| `contains(c Character)` | `bool` | True when `c` is a member or falls in one of the set's categories. |

```maxon
typealias Letters = Set with Character

function main() returns ExitCode
	var members = Letters.create()
	members.insert('x')
	members.insert('y')
	let xy = CharacterSet.from(members)

	print("[{"xyhixy".trim(xy)}] [{"123abc456".trim(CharacterSet.decimalDigits())}]\n")
	print("{CharacterSet.symbols().contains('$')} {CharacterSet.lowercaseLetters().contains('ß')}\n")
	return 0
end 'main'
```

Output: `[hi] [abc]` and `true true`.

## Array

`Array` is a growable, contiguous, generic sequence. Declare a concrete type with `typealias`, or write a
literal: `[1, 2, 3]`. `Array` implements `Iterable` and `Cloneable`; it is also `Hashable` and `Equatable`
when its element is. `equals` (and `==`) compares two arrays element by element through the element's own
`equals`, and `hash()` folds every element's own `hash()`, so equal arrays hash equal.

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreArray = Array with Score

function main() returns ExitCode
	var a = ScoreArray.create()
	a.push(5)
	a.push(1)
	a.push(4)

	let last = try a.pop() otherwise 0
	a.insert(0, value: 9)
	a.sort()

	for x in a 'each'
		print("{x} ")
	end 'each'

	a.sort(function(x Score, y Score) gives y.compare(x))
	let first = try a.first() otherwise 0
	let missing = try a.get(50) otherwise -1
	print("\n{last} {first} {missing} {a.contains(9)}\n")
	return 0
end 'main'
```

Output: `1 5 9` and `4 9 -1 true`.

### Creating

| Member | Returns | Description |
|--------|---------|-------------|
| `Array.create()` | `Array` | An empty array. |
| `[a, b, c]` | `Array` | A literal; its element type is inferred from context or from the first element. |
| `clone()` | `Array` | An independent copy. Each record, `String` or union element is cloned; an array of scalars shares its storage copy-on-write, which separates on the first write to either. An element that is a record holding a field at an interface type is copied through that field's conformer; an element held at an interface type itself is refused (an element slot is one machine word). A copy of an element that is or holds a promise is E3141, here and at `slice` and `append`. |
| `Array.from(source Iterable)` | `Array` | Collect every element of an iterable, called through an alias (`ScoreArray.from(range)`). The iterable must bind `Element` to the alias's own element (E3127). |
| `Array.init(managed)` | `Array` | Wrap raw compiler-managed storage; used by the compiler and the library. |
| `managed` | field | The array's raw storage, for `appendMemory` and library code. Library tier: reading it elsewhere is [E3185](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3185). |

### Reading

| Member | Returns | Description |
|--------|---------|-------------|
| `count()` | `Count` | Number of elements. |
| `isEmpty()` | `bool` | True when `count()` is 0. |
| `capacity()` | `int(i64.min to i64.max)` | Slots allocated. Negative when the storage is not this array's own yet: `-1` for a slice view that has not been written, other negative values for a literal's or a module-level constant's read-only storage. Treat any negative value as "not owned yet". |
| `get(index)` | `Element` | Throws `ArrayError.indexOutOfBounds` at or past `count()`, `ArrayError.emptySlot` for a slot that was never written. |
| `first()` | `Element` | Throws like `get(0)`. |
| `last()` | `Element` | Throws `indexOutOfBounds` when empty. |
| `slice(start, endIndex)` | `Array` | Elements `[start, endIndex)`. Throws `indexOutOfBounds` for a reversed or out-of-range pair; nothing is clamped. |
| `contains(element Element)` | `bool` | Element test. Requires `Element is Equatable`. |
| `contains(sequence Array)` | `bool` | True when `sequence` occurs contiguously. An empty sequence is always contained. Requires `Element is Equatable`. |

### Writing

| Member | Description |
|--------|-------------|
| `set(index, value Element)` | Replace an element. Throws `indexOutOfBounds` at or past `count()`, even when capacity exists there. |
| `push(value Element)` | Append one element. |
| `append(other Array)` | Append every element of another array. |
| `appendMemory(source)` | Append every element of raw storage (another array's `managed`) without wrapping it in an `Array`. Its argument is library-tier storage, so library code calls it; a program appends another array with `append`. |
| `pop()` | Remove and return the last element. Throws `indexOutOfBounds` when empty. |
| `insert(at, value Element)` | Insert, shifting later elements right. An `at` past the end appends. |
| `remove(at)` | Remove and return the element at `at`. Throws `indexOutOfBounds`. |
| `clear()` | Remove every element, keeping the capacity. |

### Capacity and length

| Member | Description |
|--------|-------------|
| `reserve(minCapacity)` | Ensure at least `minCapacity` slots. A capacity no allocation can satisfy (`u64.max`) panics. |
| `resize(newLength)` | Set the length. Growing exposes zero-valued elements, so it is refused at compile time (E3106) for an element type that is not a plain value — a struct, a `String`, a nested container — and for a ranged alias that excludes 0. Use `growFilled` or `truncate` for those. |
| `truncate(newLength)` | Shorten to `newLength`, releasing the removed elements. A longer `newLength` does nothing. |
| `growFilled(newLength, value Element)` | Lengthen to `newLength`, setting only the added elements to `value`. Grows capacity geometrically, so repeated small extensions stay amortized linear. A shorter `newLength` does nothing. |
| `refill(newLength, value Element)` | Set the length to `newLength` and write `value` into every element, growing or shrinking. |

An array grows by doubling while small and eases toward 1.25x once it is large.

### Sorting

| Member | Description |
|--------|-------------|
| `sort()` | Stable sort by `Comparable.compare`. Requires `Element is Comparable`. |
| `sortUnstable()` | Unstable sort (pattern-defeating quicksort). Requires `Element is Comparable`. |
| `sort(cmp function(Element, Element) returns Ordering)` | Stable sort by a comparator; any element type. |
| `sortUnstable(cmp function(Element, Element) returns Ordering)` | Unstable sort by a comparator. |

A sort reads and writes no module-level state, so it may run inside a service message handler without
tripping [E3143](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3143).

### Iterating

| Member | Returns | Description |
|--------|---------|-------------|
| `createIterator()` | `ArrayIterator` | Positioned at the first element. Throws `IterationError.exhausted` when empty. |
| `cursor()` | `ArrayIterator` | The same as `createIterator()`. |

`map`, `filter`, `contains(predicate)` and `withIterator` come from [Iterable](#interfaces).

### ArrayError

```maxon
enum ArrayError implements Error
	indexOutOfBounds
	emptySlot
end 'ArrayError'
```

`indexOutOfBounds`: the index was outside `[0, count())`. `emptySlot`: the index was inside and the slot
there was never filled.

## List

`List` is a doubly linked list: O(1) insertion and removal at both ends, O(n) access by index. It implements
`Iterable`, `Cloneable` and array-literal construction (`List from [1, 2, 3]`).

| Member | Returns | Complexity | Description |
|--------|---------|-----------|-------------|
| `List.create()` | `List` | O(1) | An empty list. |
| `count()` | `Count` | O(1) | Number of elements. |
| `isEmpty()` | `bool` | O(1) | True when empty. |
| `first()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `last()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `get(index)` | `Element` | O(n) | Throws `ArrayError.indexOutOfBounds`. |
| `prepend(value Element)` | — | O(1) | Add to the front. |
| `append(value Element)` | — | O(1) | Add to the back. |
| `insert(at, value Element)` | — | O(n) | `at` may be `0` through `count()`. Past `count()` throws `ArrayError.indexOutOfBounds`. |
| `removeFirst()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `removeLast()` | `Element` | O(1) | Throws `ArrayError` when empty. |
| `remove(at)` | `Element` | O(n) | Throws `ArrayError.indexOutOfBounds`. |
| `clear()` | — | O(n) | Remove every element. |
| `clone()` | `List` | O(n) | A deep copy. A copy of an element that is or holds a promise is E3141. |
| `createIterator()` | `ListIterator` | O(1) | Throws `IterationError.exhausted` when empty. |

`ListIterator` implements `Iterator with Element`: `current()` and `advance()`.

The list's throwing methods throw `ArrayError`.

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreList = List with Score

function main() returns ExitCode
	var list = ScoreList.create()
	list.append(2)
	list.prepend(1)
	try list.insert(2, value: 3) otherwise panic("index is within [0, count]")

	let front = try list.removeFirst() otherwise 0

	for value in list 'each'
		print("{value} ")
	end 'each'

	print("\n{front} {list.count()}\n")
	return 0
end 'main'
```

Output: `2 3` and `1 2`.

## Map

`Map` is a hash table from keys to values. The key type must be `Hashable` and `Equatable`. Maps are built
with `create()` or a dictionary literal `["a": 1, "b": 2]`, and iterate as `(key, value)` tuples in table
order, which is unspecified. The table resizes when it is three-quarters full.

| Member | Returns | Description |
|--------|---------|-------------|
| `Map.create()` | `Map` | An empty map. |
| `insert(key Key, value Value)` | — | Add a new entry. Throws `MapError.keyAlreadyExists` when the key is present. |
| `upsert(key Key, value Value)` | — | Add the entry, or replace the value of an existing key. |
| `get(key Key)` | `Value` | Throws `MapError.keyNotFound`. |
| `contains(key Key)` | `bool` | Key test. |
| `remove(key Key)` | `bool` | Remove the entry; true when the key was present. |
| `count()` | `EntryCount` | Number of entries. |
| `getCapacity()` | table capacity | Total slots in the table: `0` for a map from `create()` until its first insert, `16` after that for a small map. |
| `clone()` | `Map` | An independent copy of every key and value; writing either map leaves the other unchanged. A map whose values are or hold promises is E3141. |
| `createIterator()` | `MapIterator` | Throws `IterationError.exhausted` when empty. |

`MapIterator` implements `Iterator with (Key, Value)`: `current()` and `advance()`.

A stored key is read-only: a key read back from the map, by iteration or otherwise, takes no writes
([E3159](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3159)), so a key's hash stays the one it was filed under.
`clone()` gives a key to change.

```maxon
enum MapError implements Error
	keyNotFound
	keyAlreadyExists
end 'MapError'
```

```maxon
typealias Age = int(0 to 150)
typealias Ages = Map with String, Age

function main() returns ExitCode
	var ages = Ages.create()
	try ages.insert("ann", value: 30) otherwise panic("new key")

	try ages.insert("ann", value: 31) otherwise 'duplicate'
		print("ann is already present\n")
	end 'duplicate'

	ages.upsert("bob", value: 25)
	ages.upsert("ann", value: 32)
	let ann = try ages.get("ann") otherwise 0
	print("{ann} {ages.contains("bob")} {ages.remove("bob")} {ages.count()}\n")

	for (name, age) in ages 'each'
		print("{name}: {age}\n")
	end 'each'

	return 0
end 'main'
```

Output: `ann is already present`, `32 true true 1`, `ann: 32`.

## Set

`Set` is a hash set. The element type must be `Hashable` and `Equatable`. Build one with `create()` or
`Set from [1, 2, 3]`. Iteration order is unspecified.

| Member | Returns | Description |
|--------|---------|-------------|
| `Set.create()` | `Set` | An empty set. |
| `insert(element Element)` | — | Add an element; inserting a present element does nothing. |
| `contains(element Element)` | `bool` | Membership test. |
| `remove(element Element)` | `bool` | Remove; true when the element was present. |
| `count()` | `MemberCount` | Number of elements. |
| `getCapacity()` | table capacity | Total slots in the table. |
| `clone()` | `Set` | An independent copy of every element; changing either set leaves the other unchanged. |
| `createIterator()` | `SetIterator` | Throws `IterationError.exhausted` when empty. |

`SetIterator` implements `Iterator with Element`: `current()` and `advance()`.

A stored element is read-only, as a `Map` key is: an element read back from the set takes no writes
([E3159](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3159)); `clone()` gives one to change.

```maxon
typealias Tags = Set with String

function main() returns ExitCode
	var tags = Tags.create()
	tags.insert("red")
	tags.insert("red")
	tags.insert("blue")
	print("{tags.count()} {tags.contains("red")} {tags.remove("red")} {tags.count()}\n")
	return 0
end 'main'
```

Output: `2 true true 1`.

## Vector

`Vector` is a fixed-size array whose element count is part of its type: `Vector with 3 Coord` holds exactly
three elements, and `countof(Vec3)` is the constant `3`. It implements `Iterable`.

| Member | Returns | Description |
|--------|---------|-------------|
| `Vector.create()` | `Vector` | Every element zero; refused (E3106) for an element alias whose range excludes 0. |
| `Vector from [a, b, c]` | `Vector` | A literal; the count is the literal's length. |
| `count()` | `Count` | The fixed size, answered from the type. |
| `get(index)` | `Element` | Throws `ArrayError.indexOutOfBounds`. |
| `set(index, value Element)` | — | Throws `ArrayError.indexOutOfBounds` at or past the fixed size. |
| `createIterator()` | `ArrayIterator` | Throws `IterationError.exhausted` when the vector is empty. |

A vector never changes length: it has no `push`, `insert` or `remove`.

```maxon
typealias Coord = int(i64.min to i64.max)
typealias Vec3 = Vector with 3 Coord

function main() returns ExitCode
	var v = Vec3.create()
	try v.set(1, value: 7) otherwise panic("inside the fixed size")

	try v.set(3, value: 1) otherwise 'outside'
		print("index 3 refused\n")
	end 'outside'

	let literal = Vector from [1, 2, 3]
	var sum = 0 as Count

	for x in literal 'each'
		sum = sum + x
	end 'each'

	print("{v.count()} {countof(Vec3)} {try v.get(1) otherwise 0} {sum}\n")
	return 0
end 'main'
```

Output: `index 3 refused` and `3 3 7 6`.

## Range

In a `for` header, `start to end` and `start upto end` compile to a counted loop with no allocation.
Anywhere else they are values:

| Expression | Type | Visits |
|------------|------|--------|
| `start to end` | `Range` | `start` through `end`, both included |
| `start upto end` | `OpenRange` | `start` up to but excluding `end` |

Both implement `Iterable with (RangeBound, RangeIterator)`; `RangeBound` is `int(i64.min to i64.max)`.

| Member | Returns | Description |
|--------|---------|-------------|
| `Range.create(start RangeBound, finish RangeBound)` | `Range` | The same as `start to finish`. |
| `OpenRange.create(start RangeBound, endExclusive RangeBound)` | `OpenRange` | The same as `start upto endExclusive`. |
| `createIterator()` | `RangeIterator` | Throws `IterationError.exhausted` for an empty range (`finish < start`, or `endExclusive <= start`). |

`RangeIterator` implements `Iterator with RangeBound` and `BidirectionalIterator`:

| Member | Returns | Description |
|--------|---------|-------------|
| `RangeIterator.create(first RangeBound, last RangeBound)` | `RangeIterator` | Both inclusive. Throws `exhausted` when `last < first`. |
| `current()` | `RangeBound` | The current value. |
| `index()` | `int(0 to u64.max)` | Steps taken from `first`. |
| `advance()` | — | Throws `exhausted` at `last`. |
| `retreat()` | — | Throws `atStart` at `first`. |

```maxon
function main() returns ExitCode
	let inclusive = 1 to 4
	var total = 0 as RangeBound

	for x in inclusive 'sum'
		total = total + x
	end 'sum'

	for (iter, x) in (10 upto 13).withIterator() 'each'
		print("{iter.index()}:{x} ")
	end 'each'

	print("total={total}\n")
	return 0
end 'main'
```

Output: `0:10 1:11 2:12 total=10`.

## Iterators

A live iterator always points at a valid element, so `current()` cannot fail; navigation throws
`IterationError`. `createIterator()` on an empty collection throws `IterationError.exhausted` instead of
returning an iterator with nothing under it.

```maxon
enum IterationError implements Error
	exhausted
	atStart
end 'IterationError'
```

`exhausted`: an `advance` past the last element. `atStart`: a `retreat` before the first. A failed move
leaves the position unchanged.

### ArrayIterator

`ArrayIterator` (what `Array.createIterator()`, `Array.cursor()` and `Vector.createIterator()` return)
implements `BidirectionalIterator with Element` and adds random access:

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `ArrayIterator.create(source)` | `ArrayIterator` | `IterationError` | Over raw array storage; `exhausted` when it is empty. |
| `current()` | `Element` | — | The element at the position. |
| `index()` | `int(0 to u64.max)` | — | The position. |
| `advance()` | — | `IterationError` | Forward one; `exhausted` at the end. |
| `retreat()` | — | `IterationError` | Back one; `atStart` at position 0. |
| `seek(index)` | — | `IterationError` | Jump to an absolute position; `exhausted` when out of bounds. |
| `peek(ahead)` | `Element` | `IterationError` | The element `ahead` positions on, without moving. |
| `advanceBy(n IterStep)` | — | `IterationError` | Forward `n` (from `Iterator`). |
| `retreatBy(n IterStep)` | — | `IterationError` | Back `n` (from `BidirectionalIterator`). |

`advanceBy` and `retreatBy` step one at a time, so a move that fails part-way leaves the iterator where the
throw happened. `IterStep` is unsigned: the direction is the method you call, so a negative step is refused
at the argument door rather than read as a move the other way.

```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreArray = Array with Score

function main() returns ExitCode
	var values = ScoreArray.create()

	for i in 1 to 5 'fill'
		values.push(i * 10)
	end 'fill'

	var c = try values.cursor() otherwise panic("not empty")
	try c.advance() otherwise panic("has a second element")
	let ahead = try c.peek(2) otherwise 0
	print("{c.index()} {c.current()} {ahead}\n")

	try c.advanceBy(3) otherwise panic("in range")
	let beyond = try c.peek(1) otherwise -1
	print("{c.current()} {beyond}\n")
	return 0
end 'main'
```

Output: `1 20 40` and `50 -1`.

### The other iterators

| Iterator | Produced by | Element |
|----------|-------------|---------|
| `ListIterator` | `List.createIterator()` | the list's element |
| `MapIterator` | `Map.createIterator()` | `(Key, Value)` |
| `SetIterator` | `Set.createIterator()` | the set's element |
| `StringIterator` | `String.createIterator()` | `Character` |
| `RangeIterator` | `Range`/`OpenRange.createIterator()` | `RangeBound` |

Each has a static `create`, `current()` and `advance()`.

## Interfaces

The protocols the library and the language are built on.

| Interface | Requirement | Used by |
|-----------|-------------|---------|
| `Error` | none | Every throwable type: `enum E implements Error` |
| `Equatable` | `function equals(other Self) returns bool` | `==`, `!=`, `contains` |
| `Comparable` | `function compare(other Self) returns Ordering` | `sort()` |
| `Hashable` | `function hash() returns HashValue` | `Map` keys, `Set` elements |
| `Cloneable` | `function clone() returns Self` | `clone()` |
| `Stringable` | `function toString() returns String` | `"{value}"` |
| `FormattedStringable` | `function toString(format String) returns String` | `"{value:format}"` — the text after the colon is passed as `format` |
| `Iterator with Element` | `current() returns Element`, `advance() throws IterationError` | iterators |
| `BidirectionalIterator` | extends `Iterator`; `retreat() throws IterationError` | reversible iterators |
| `Iterable with (Element, Iter)` | `createIterator() returns Iter throws IterationError` | `for`-`in` |
| `Parsable` | `static fromString(input String) returns Self throws Error` | `int.fromString` and friends |

The literal-initialization interfaces (`InitableFromStringLiteral`, `InitableFromCharLiteral`,
`InitableFromArrayLiteral`, `InitableFromDictionaryLiteral`) let a type be written as `MyType from <literal>`;
see Initializing From Literals in [LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md).

### Supporting types

```maxon
enum Ordering
	lessThan
	equalTo
	greaterThan
end 'Ordering'
```

`HashValue` is `int(0 to u32.max)`; `IterStep` is `int(0 to u64.max)`.

| Function | Returns | Description |
|----------|---------|-------------|
| `spreadHash(hash HashValue)` | `HashValue` | Mix a hash's bits (the MurmurHash3 32-bit finalizer). `Map` and `Set` apply it before choosing a slot, so keys with regular structure do not cluster. A `hash()` implementation does not need to call it. |

### Default methods

| On | Method | Returns | Description |
|----|--------|---------|-------------|
| `Iterator` | `advanceBy(n IterStep)` | — | Call `advance()` `n` times. |
| `BidirectionalIterator` | `retreatBy(n IterStep)` | — | Call `retreat()` `n` times. |
| `Iterable` | `map(transform function(Element) returns Element)` | `Array with Element` | Each element transformed. |
| `Iterable` | `filter(keep function(Element) returns bool)` | `Array with Element` | The elements `keep` accepts. |
| `Iterable` | `contains(predicate function(Element) returns bool)` | `bool` | True when any element matches. |
| `Iterable` | `withIterator()` | iterator pairs | Iterate as `for (iter, element) in collection.withIterator()`, with the iterator in hand. |

```maxon
typealias Cents = int(0 to u32.max)

type Money implements Stringable, FormattedStringable, Equatable, Comparable, Hashable, Cloneable
	let cents as Cents

	static function create(cents Cents) returns Money
		return Money{cents: cents}
	end 'create'

	function toString() returns String
		return "{cents} cents"
	end 'toString'

	function toString(format String) returns String
		return "{format} {cents}"
	end 'toString'

	function equals(other Money) returns bool
		return cents == other.cents
	end 'equals'

	function compare(other Money) returns Ordering
		return cents.compare(other.cents)
	end 'compare'

	function hash() returns HashValue
		return cents.hash()
	end 'hash'

	function clone() returns Money
		return Money{cents: cents}
	end 'clone'
end 'Money'

typealias Prices = Set with Money

function main() returns ExitCode
	let price = Money.create(250)
	var prices = Prices.create()
	prices.insert(price)
	prices.insert(price.clone())
	print("{price} | {price:EUR} | {price == Money.create(250)} {price.compare(Money.create(1))} {prices.count()}\n")
	return 0
end 'main'
```

Output: `250 cents | EUR 250 | true greaterThan 1`.

## File

`File` reads, writes, renames, deletes and inspects files. Every method is static and takes a
[FilePath](#filepath).

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `File.readText(path FilePath)` | `String` | `FileReadError` | The whole file as text, read to its end — bytes appended while it is read are included. |
| `File.readBinary(path FilePath)` | `ByteArray` | `FileReadError` | The whole file as bytes, read the same way. |
| `File.writeText(path FilePath, content String, mode FilePermission = .normal)` | — | `FileWriteError` | Create or truncate, then write. |
| `File.createText(path FilePath, content String)` | — | `FileWriteError` | Create a file that must not exist yet, then write. An existing file is `alreadyExists` and is left as it was. A failed write deletes the new file and throws the write's error; when that delete fails too, it throws `partialFileLeft`. |
| `File.writeBinary(path FilePath, content ByteArray, mode FilePermission = .normal)` | — | `FileWriteError` | Create or truncate, then write. |
| `File.exists(path FilePath)` | `bool` | — | True when a file exists at `path`. |
| `File.delete(path FilePath)` | — | `FileDeleteError` | Delete a file. |
| `File.rename(from FilePath, to FilePath)` | — | `FileRenameError` | Move a file, replacing any existing destination in one step, so a reader of `to` never sees a partly written file. |
| `File.info(path FilePath)` | `FileInfo` | `FileInfoError` | Size, timestamps and attributes, from one OS call. |

### Types

| Type | Definition |
|------|------------|
| `FileSize` | `int(0 to u64.max)` — bytes |
| `Timestamp` | `int(0 to u64.max)` — whole seconds since the Unix epoch |
| `ByteArray` | `Array with Byte` |

`FileInfo` has read-only fields and a factory, `FileInfo.create(size, modifiedTime:, createdTime:,
accessedTime:, isDirectory:, isReadOnly:)`:

| Field | Type | Description |
|-------|------|-------------|
| `size` | `FileSize` | Size in bytes |
| `modifiedTime` | `Timestamp` | Last modification |
| `createdTime` | `Timestamp` | Creation |
| `accessedTime` | `Timestamp` | Last access |
| `isDirectory` | `bool` | The path is a directory |
| `isReadOnly` | `bool` | The file is read-only |

`FilePermission` is `normal` (0666) or `executable` (0755 on Unix).

Each operation throws its own error enum. They share these cases:

| Case | Thrown when |
|------|-------------|
| `notFound` | The file, or a directory on its path, does not exist |
| `accessDenied` | The operating system refuses access to the path |
| `busy` | Another process holds the file open in a way that excludes this operation |
| `failed` | Any other failure |

On every target a path holding a NUL byte is a missing file: each operation on it answers as it does for a
file that is not there, and the file named by the bytes before the NUL is left as it was.

| Error enum | Cases |
|------------|-------|
| `FileReadError` | `notFound`, `accessDenied`, `busy`, `failed` |
| `FileWriteError` | `failed`, `notFound`, `accessDenied`, `alreadyExists`, `busy`, `partialFileLeft` |
| `FileDeleteError` | `notFound`, `accessDenied`, `busy`, `failed` |
| `FileRenameError` | `failed`, `notFound`, `accessDenied`, `busy` |
| `FileInfoError` | `notFound`, `accessDenied`, `busy`, `failed` |

`alreadyExists` and `partialFileLeft` come only from `File.createText`. Every one of these enums has
`failure()`, which returns the cause as a `FileFailure` — `notFound`, `accessDenied`, `alreadyExists`, `busy`
or `failed` (`partialFileLeft` gives `failed`) — so one `match` handles the causes of any file operation.

```maxon
function main() returns ExitCode
	let dir = FilePath from "scratch"
	_ = Directory.create(dir)

	let notes = dir.join("notes.txt")
	try File.writeText(notes, content: "hello") otherwise panic("cannot write {notes}")

	let text = try File.readText(notes) otherwise ""
	let info = try File.info(notes) otherwise panic("just written")
	print("{text} {info.size} {info.isDirectory}\n")

	let moved = dir.join("moved.txt")
	try File.rename(notes, to: moved) otherwise panic("rename failed")
	print("{File.exists(notes)} {File.exists(moved)}\n")

	try File.delete(moved) otherwise panic("delete failed")
	let gone = try File.readText(moved) otherwise "no such file"
	print("{gone}\n")
	return 0
end 'main'
```

Output: `hello 5 false`, `false true`, `no such file`.

## FilePath

`FilePath` is a filesystem path. Construction normalizes separators to the host's (`\` on Windows, `/`
elsewhere) and accepts `file://` URLs, which are converted to paths with their percent escapes decoded
(`file:///tmp/a%20b` is `/tmp/a b`). It implements `Equatable`, `Hashable`,
`Stringable` and `InitableFromStringLiteral`. Equality and hashing follow the host filesystem:
case-insensitive on Windows, byte-exact elsewhere.

### Construction

| Member | Returns | Description |
|--------|---------|-------------|
| `FilePath from "a/b.txt"` | `FilePath` | From a literal; an invalid path panics. |
| `FilePath.from(path String)` | `FilePath` | Throws `FilePathError.invalidCharacter` on Windows for a control character (NUL included) or one of `< > " \| ? *`, `notFileURL` for a URL whose scheme is other than `file`, or `malformedURL` for text holding `://` that `URL.parse` refuses. |
| `FilePath.empty()` | `FilePath` | The empty path. |
| `FilePath.separator()` | `String` | The host separator. |
| `path` | field, `String` | The normalized text. |

### Components

| Member | Returns | Description |
|--------|---------|-------------|
| `filename()` | `String` | The last component (the whole path when there is no separator). |
| `fileExtension()` | `String` | The extension with its dot, or `""`. A leading dot is not an extension (`.gitignore` has none). |
| `stem()` | `String` | The filename without its extension. |
| `parent()` | `FilePath` | The containing directory. A path directly under a root gives that root, spelled as one (`/foo` gives `/`, `C:\foo` gives `C:\`), so walking upwards keeps answering absolute paths. Throws `FilePathError.noParent` for a root and for a relative path with no separator. |

### Building paths

| Member | Returns | Description |
|--------|---------|-------------|
| `join(component String)` | `FilePath` | Append a component with the host separator. |
| `join(component FilePath)` | `FilePath` | Append another path. |
| `changeExtension(newExt String)` | `FilePath` | Replace or add an extension; include the dot (`".exe"`). |
| `anchoredAt(base FilePath)` | `FilePath` | A relative path joined onto `base`, or an absolute path kept. Only what cannot change the file named is folded — `.` components, repeated separators and a trailing one — and every `..` is left for the filesystem, so through a symbolic link it names the file the spelling names. On Windows a drive-relative path (`C:foo`) is joined onto a `base` rooted on the same drive; against any other `base` it stays drive-relative, because it names that drive's current directory. |
| `resolve(base FilePath)` | `FilePath` | `anchoredAt(base)`, then folded as `normalize()` folds it — lexically, so a `..` after a symbolic link cancels the link's name. |
| `resolvedPhysically()` | `FilePath` | The path's one physical spelling, which the filesystem answers: every symbolic link along it is followed, every `..` is taken against the directory the walk has reached, and a relative path is taken against the working directory. Two spellings of one file answer the same path, so it is the path to key a cache or a lock by; the lexical `normalize()` gives each spelling a key of its own. On Windows the `\\?\` prefix is dropped and a network path comes back as `\\server\share\…`; a volume reachable only as `\\?\Volume{…}\` throws `failed`. Throws `PathResolveError.notFound` when the path names nothing, `accessDenied` when the process may not look it up, and `failed` for any other refusal, which on Linux includes an unmounted `/proc`. Refused on `wasm32-wasi` with E3104 ([Target support](#target-support)). |
| `relativeTo(base FilePath)` | `FilePath` | The part after `base`. Throws `FilePathError.noParent` when the path is not inside `base`. |
| `normalize()` | `FilePath` | The path folded lexically, without asking the filesystem: `.` components removed, each `..` cancelling the component before it, repeated separators collapsed and a trailing one dropped. A `..` above an absolute path's root is dropped; a leading one in a relative path is kept. A relative path that folds away entirely is `.`. |
| `toString()` | `String` | The path text. |

### Queries

| Member | Returns | Description |
|--------|---------|-------------|
| `isEmpty()` | `bool` | The path is `""`. |
| `isAbsolute()` | `bool` | On Windows a drive path (`C:\`), a UNC path (`\\server`) or a rooted path (`\dir`); a leading `/` elsewhere. |
| `isRelative()` | `bool` | Not absolute. |
| `isInside(dir FilePath)` | `bool` | The path equals `dir` or lies beneath it, compared by whole components (`/foo/bar` is not inside `/foo/ba`). |
| `startsWith(prefix FilePath)` | `bool` | Component-wise prefix test. |
| `equals(other FilePath)`, `hash()` | | Host filesystem semantics: they compare `comparisonSpelling()`. |
| `comparisonSpelling()` | `String` | The path as the host compares paths: lower-cased on Windows, as written on macOS and Linux. `equals`, `hash` and `isInside` compare this spelling, so it is the one to key a map or a digest by. |

```maxon
enum FilePathError implements Error
	invalidCharacter
	notFileURL
	malformedURL
	noParent
end 'FilePathError'
```

```maxon
enum PathResolveError implements Error
	notFound
	accessDenied
	failed
end 'PathResolveError'
```

```maxon
function main() returns ExitCode
	let base = FilePath from "project"
	let source = base.join("src").join("main.maxon")

	let relative = try source.relativeTo(base) otherwise FilePath.empty()
	let parent = try source.parent() otherwise FilePath.empty()
	let web = try FilePath.from("https://example.com/x") otherwise FilePath.empty()

	print("{source.filename()} {source.stem()} {source.fileExtension()} {parent.filename()}\n")
	print("{source.isInside(base)} {source.isRelative()} {relative.join("x").isEmpty()} {web.isEmpty()}\n")
	print("{source.changeExtension(".txt").filename()}\n")
	return 0
end 'main'
```

Output: `main.maxon main .maxon src`, `true true false true`, `main.txt`.

## Directory

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Directory.list(path FilePath)` | `FilePathArray` | `DirectoryListError` | The entries of a directory, each joined onto `path`. |
| `Directory.exists(path FilePath)` | `bool` | — | True when `path` is an existing directory. |
| `Directory.isDirectory(path FilePath)` | `bool` | — | The same as `exists`. |
| `Directory.create(path FilePath)` | `bool` | — | Create the directory and any missing parents. True when the directory exists afterwards. |
| `Directory.delete(path FilePath)` | — | `DirectoryDeleteError` | Remove an empty directory. A directory that still holds an entry is refused, not emptied. |
| `Directory.currentPath()` | `FilePath` | — | The working directory. |

```maxon
enum DirectoryListError implements Error
	notFound
	accessDenied
	listFailed
end 'DirectoryListError'
```

```maxon
enum DirectoryDeleteError implements Error
	notFound
	notEmpty
	deleteFailed
end 'DirectoryDeleteError'
```

| Error enum | Case | Thrown when |
|------------|------|-------------|
| `DirectoryDeleteError` | `notFound` | Nothing exists at `path` |
| `DirectoryDeleteError` | `notEmpty` | The directory still holds an entry |
| `DirectoryDeleteError` | `deleteFailed` | The removal fails for any other reason |

On every target a path holding a NUL byte is a missing directory: `Directory.create` answers false, and the
other methods answer as they do for a directory that is not there.

```maxon
function main() returns ExitCode
	let dir = FilePath from "listing-demo"
	print("{Directory.create(dir.join("nested"))} {Directory.exists(dir)}\n")
	try File.writeText(dir.join("a.txt"), content: "a") otherwise panic("write")

	for entry in try Directory.list(dir) otherwise panic("list") 'each'
		print("{entry.filename()} {Directory.isDirectory(entry)}\n")
	end 'each'

	print("{Directory.currentPath().isAbsolute()}\n")
	return 0
end 'main'
```

It prints `true true`, then `a.txt false` and `nested true` in the order the OS lists them, then `true`.
`Directory.list` does not report `.` or `..`.

## Console

`Console.stdin()` returns a buffered reader over standard input. Keep one reader for the life of the
program: a new reader does not see input an earlier one already buffered.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Console.stdin()` | `Stdin` | — | The standard-input reader. |
| `Console.stdoutWantsColor()` | `bool` | — | True when standard output is a terminal, `NO_COLOR` is unset and `TERM` is other than `dumb` (compared case-insensitively): output may then carry ANSI escapes. `NO_COLOR` set to any value turns colour off; Windows reads an empty value as unset. `false` on `wasm32-wasi`. |
| `readLine()` | `String` | `ConsoleError.endOfFile` | The next line without its `\n`; a `\r` before it is removed too. A last line with no terminator is returned, and the call after it throws. |
| `readBytes(n BytePos)` | `ByteArray` | — | Up to `n` bytes, unchanged. Fewer than `n` only when input ends first; an empty array at the end of input. It reads from the same buffer as `readLine`. |

```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	var lines = 0 as Count

	while true 'read'
		let line = try stdin.readLine() otherwise break
		lines = lines + 1
		print("[{line}]\n")
	end 'read'

	print("{lines} lines\n")
	return 0
end 'main'
```

Given `one\r\ntwo\nthree` on stdin, it prints `[one]`, `[two]`, `[three]`, `3 lines`.

## CommandLine

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `CommandLine.args()` | `StringArray` | — | Every argument, the program path first. A fresh array each call. |
| `CommandLine.optionValue(arg String)` | `String` | `StringError.notFound` | Everything after the first `=` of `--key=value`, later `=` included. |
| `CommandLine.getOptionValue(name String)` | `String` | `StringError.notFound` | The value of the first `--name=value` argument. |
| `CommandLine.readOptionalIntArgument(position Count, otherwise Count)` | `Count` | `ParseError.invalidFormat` | The non-negative integer in the argument at `position` (0 is the program path, 1 the first argument after it), or `otherwise` when there is no argument at `position`. An argument present but not a non-negative integer, or beyond `i64.max`, throws. |

```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let mode = try CommandLine.getOptionValue("mode") otherwise "default"
	let note = try CommandLine.optionValue("--note=a=b") otherwise ""
	print("{args.count()} {mode} {note}\n")
	return 0
end 'main'
```

Run as `program --mode=fast`, it prints `2 fast a=b`.

```maxon
function main() returns ExitCode
	let rounds = try CommandLine.readOptionalIntArgument(1, otherwise: 1000) otherwise return 2
	print("{rounds} rounds
")
	return 0
end 'main'
```

Run as `program 50` it prints `50 rounds`, run bare it prints `1000 rounds`, and run as `program -3` or `program many` it returns 2.

## Log

`Log` is a structured logger modelled on Go's `log/slog`: a record is a level, a message and typed attributes,
and a handler service formats and writes it. A program logs with its first call:

```maxon
function main() returns ExitCode
	Log.info("started", attrs: [LogAttr.string("mode", value: "fast"), LogAttr.int("workers", value: 4)])
	return 0
end 'main'
```

writes one line to stderr:

```
time=2026-09-27T12:34:56.789Z level=INFO msg=started mode=fast workers=4
```

The standard library's default sink is a `TextLogHandler` on stderr with the time on (RFC 3339, UTC,
milliseconds) and a minimum level of INFO.

Only an enabled call builds a record: the level is checked at the call, before anything reaches the handler.
Below ERROR, a call sends its record to the handler and returns, and the handler writes it. When the
handler's mailbox is full, the call waits up to 10 seconds for room and then discards the record; a record
logged after the handler has stopped is discarded. At ERROR and above the call waits for room as long as it
takes. `Log.error` —
and any call at a rank of ERROR or above — returns once the handler has written its record, and so every
record sent before it too, because the handler takes records in order; a line logged just before a `panic` is
on the stream. When the program exits normally, every record still queued is written first.

Logging runs on services, so it is refused on `wasm32-wasi`
([E3104](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3104)), at the call.

### Writing records

Each writer takes `message String`, then `attrs LogAttrArray` (default empty), then `file String` and
`line SourceLineNumber`, which default to the caller's location and feed the handler's `addSource` output.

| Member | Returns | Description |
|--------|---------|-------------|
| `Log.trace`, `Log.debug`, `Log.info`, `Log.warn`, `Log.error` | — | Log `message` at that level through the default sink. |
| `Log.log(level LogLevel, message String, …)` | — | Log at any level, a custom one included. |
| `Log.enabled(level LogLevel)` | `bool` | True when the default sink's minimum admits `level` — a guard for attributes that are costly to compute. |
| `Log.default()` | `Logger` | A logger over the current default sink. |
| `Log.setDefault(sink LogSink)` | — | Make `sink` the default for every thread. |

A helper of your own forwards its caller's location by declaring the same two defaulted parameters and
passing them on:

```maxon
extension Log
	public static function audit(message String, attrs LogAttrArray = LogAttrArray.create(), file String = __file__, line SourceLineNumber = __line__)
		Log.log(LogLevel.named("AUDIT", rank: 2), message: message, attrs: attrs, file: file, line: line)
	end 'audit'
end 'Log'
```

### Levels

A `LogLevel` is a rank; a record is written when its rank is at or above the minimum.

| Member | Returns | Description |
|--------|---------|-------------|
| `LogLevel.trace`, `.debug`, `.info`, `.warn`, `.error` | `LogLevel` | Ranks -8, -4, 0, 4 and 8. |
| `LogLevel.create(rank LogRank)` | `LogLevel` | A custom level, named relative to a standard level near it: rank 2 prints `INFO+2`, rank -6 `DEBUG-2`, rank 12 `ERROR+4`. |
| `LogLevel.named(name String, rank LogRank)` | `LogLevel` | A custom level that prints `name`. |
| `rank` | `LogRank` | The rank. |
| `name()` | `String` | The printed name. |

### Attributes

| Member | Returns | Description |
|--------|---------|-------------|
| `LogAttr.string(key String, value String)` | `LogAttr` | A text attribute. |
| `LogAttr.int(key String, value LogInteger)` | `LogAttr` | An integer attribute. |
| `LogAttr.float(key String, value LogReal)` | `LogAttr` | A float attribute, printed in its shortest round-trip form. |
| `LogAttr.bool(key String, value bool)` | `LogAttr` | A boolean attribute. |
| `LogAttr.group(name String, attrs LogAttrArray)` | `LogAttr` | `attrs` qualified by `name`. |
| `fields` | `LogFieldArray` | The attribute flattened to fields. |

`LogAttrArray` is `Array with LogAttr`, written as an array literal at the call. Groups flatten: each attribute
becomes a `LogField`, which is what a handler receives.

| Member | Returns | Description |
|--------|---------|-------------|
| `LogField.create(key String, value LogValue, groups LogGroupPath)` | `LogField` | A field. |
| `groups` | `LogGroupPath` | The enclosing group names, outermost first. |
| `key`, `value` | `String`, `LogValue` | The key within its innermost group, and the value. |
| `dottedKey()` | `String` | The groups and the key joined with `.`: `req.method`. |

`LogValue` is a union with the cases `text(value String)`, `integer(value LogInteger)`, `real(value LogReal)`
and `boolean(value bool)`.

### Loggers

A `Logger` carries attributes and a group to every record it writes. It keeps the sink it was built on and
obeys that sink's level changes, including the ones made after it was built.

| Member | Returns | Description |
|--------|---------|-------------|
| `withAttrs(attrs LogAttrArray)` | `Logger` | A logger that adds `attrs` before each record's own, under the logger's current group. |
| `withGroup(name String)` | `Logger` | A logger whose later attributes, bound or passed, are qualified by `name`. |
| `trace`, `debug`, `info`, `warn`, `error`, `log` | — | As on `Log`, through this logger. |
| `enabled(level LogLevel)` | `bool` | True when this logger would write `level`, its group's override included. |
| `level()`, `setLevel`, `groupLevel`, `setGroupLevel`, `clearGroupLevel` | | As on `Log`, on this logger's sink. |

### Changing levels at run time

The levels belong to the sink. A change takes effect on the next call from any thread, for every `Logger`
on that sink; concurrent changes each land.

| Member | Returns | Description |
|--------|---------|-------------|
| `Log.level()` | `LogLevel` | The minimum. |
| `Log.setLevel(level LogLevel)` | — | Set the minimum. |
| `Log.setGroupLevel(group String, level LogLevel)` | — | Give loggers in `group` their own minimum. |
| `Log.clearGroupLevel(group String)` | — | Return `group` to the minimum. |
| `Log.groupLevel(group String)` | `LogLevel` | The level in effect for `group`: its override, else the minimum. |

A group is named by its path joined with `.`: a logger made by `withGroup("db").withGroup("pool")` obeys the
override for `db.pool`, else the one for `db`, else the minimum. `Log`'s own writers are outside every group.

`LogLevels` is the immutable value those calls replace: `LogLevels.minimum(level)`, then `level()`,
`groupLevel(group)`, `enables(level)`, and the copies `withLevel(level)`, `withGroupLevel(group, level:)` and
`withoutGroupLevel(group)`. `LogLevelVar` is `SharedValue with LogLevels` — the live cell a sink reads (see
[SharedValue](#sharedvalue)).

### Handlers and sinks

A `LogHandler` is a service interface with one requirement, `handle(record LogRecord) returns bool`. A sink
pairs a handler with its levels:

| Member | Returns | Description |
|--------|---------|-------------|
| `LogSink.create(handler LogHandler.handle, levels LogLevelVar)` | `LogSink` | A sink; `handler` is a spawned service. |
| `handler`, `levels` | `LogHandler.handle`, `LogLevelVar` | Its parts. |
| `enabled`, `level`, `groupLevel`, `setLevel`, `setGroupLevel`, `clearGroupLevel` | | As on `Log`, on this sink. |

The default sink is a registry key: `Log.setDefault(sink)` registers `sink` for every later lookup on every
thread, and a program's own top-level `default LogSink = …` declaration takes precedence over the standard library's
([Program-Wide Defaults](LANGUAGE_REFERENCE.md#program-wide-defaults--default)).

The two handlers write each record with one write, so lines from different threads stay whole:

| Member | Returns | Description |
|--------|---------|-------------|
| `TextLogHandler.create(options LogHandlerOptions)` | `TextLogHandler` | `key=value` pairs in the order `time`, `level`, `source` (`file:line`), `msg`, then the attributes; the time is RFC 3339 with milliseconds. A text value is quoted with `Json.quote`'s escapes when it is empty or holds a space, a control byte, `"` or `=`. A group's attributes print with dotted keys. |
| `JsonLogHandler.create(options LogHandlerOptions)` | `JsonLogHandler` | One JSON object per line: `time` (RFC 3339 with nanoseconds), `level`, `source` as `{"file":…,"line":…}`, `msg`, then the attributes, a group as a nested object. A non-finite float is a string. |
| `LogHandlerOptions.create(stream LogStream, addTime bool, addSource bool)` | `LogHandlerOptions` | Where to write, and whether to include the time and the caller's location. |
| `LogHandlerOptions.standard()` | `LogHandlerOptions` | Stderr, time on, source off. |
| `stream`, `addTime`, `addSource` | | The options' fields. |

`LogStream` has the cases `standardError` and `standardOutput`.

A handler of your own implements `LogHandler` and receives a `LogRecord`:

| Member | Returns | Description |
|--------|---------|-------------|
| `time` | `UnixNanos` | When the record was made. |
| `level`, `message` | `LogLevel`, `String` | |
| `attrs` | `LogFieldArray` | The logger's bound fields, then the call's own. |
| `file`, `line` | `String`, `SourceLineNumber` | The caller's location. |
| `LogRecord.create(time, level:, message:, attrs:, file:, line:)` | `LogRecord` | A record. |

```maxon
function main() returns ExitCode
	let options = LogHandlerOptions.create(LogStream.standardOutput, addTime: false, addSource: false)
	let levels = LogLevelVar.create(LogLevels.minimum(LogLevel.debug))
	Log.setDefault(LogSink.create(spawn JsonLogHandler.create(options), levels: levels))

	let requests = Log.default().withAttrs([LogAttr.string("service", value: "api")]).withGroup("req")
	requests.debug("served", attrs: [LogAttr.string("method", value: "GET"), LogAttr.int("status", value: 200)])
	Log.setGroupLevel("req", level: LogLevel.info)
	requests.debug("filtered")
	Log.error("failed", attrs: [LogAttr.bool("retry", value: false)])
	return 0
end 'main'
```

Output:

```
{"level":"DEBUG","msg":"served","service":"api","req":{"method":"GET","status":200}}
{"level":"ERROR","msg":"failed","retry":false}
```

## TraceCapture

`TraceCapture` records trace keys so a test can check which internal path ran. `trace` records only between
`startCapture` and `stopCapture`; for levelled output see [Log](#log).

A capture holds only the keys the program emitted itself. The capture lives in a recorder service, the
library's `default` for its capture control: a program that calls `TraceCapture` spawns that service before
`main` runs, and every thread and service traces into the one capture.

| Method | Returns | Description |
|--------|---------|-------------|
| `TraceCapture.trace(key String)` | — | Send `key` to the recorder when capturing. Use a stable dotted name. |
| `TraceCapture.startCapture()` | — | Clear the recorder, then switch capturing on. |
| `TraceCapture.stopCapture()` | `TraceKeyArray` | Switch capturing off, then await the recorder's keys, in the order it received them; the recorder starts empty again. |
| `TraceCapture.fired(capturedKeys TraceKeyArray, key String)` | `bool` | True when `key` was recorded at least once. |

`trace`, `startCapture` and `stopCapture` wait for room when the recorder's mailbox is full, as long as it
takes (`sendTimeout: ForeverMs`). In a cancelled coroutine the message is dropped, and `stopCapture` returns
an empty array.

```maxon
function main() returns ExitCode
	TraceCapture.startCapture()
	TraceCapture.trace("cache.miss")
	let keys = TraceCapture.stopCapture()
	print("{TraceCapture.fired(keys, key: "cache.miss")} {TraceCapture.fired(keys, key: "cache.hit")}\n")
	return 0
end 'main'
```

Output: `true false`.

## Process

`Process` describes the running program. To start other programs see [Subprocess](#subprocess).

### ExitCode

`ExitCode` is the return type of `main`: `int(0 to u32.max)` on Windows and `int(0 to 255)` on Linux, macOS
and WASI, matching what each platform can report.

### Members

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Process.executablePath()` | `FilePath` | `ProcessIntrospectionError.pathUnavailable` | Absolute path of the running executable. |
| `Process.id()` | `Pid` | — | This process's id. |
| `Process.namespace()` | `String` | — | The pid space this process belongs to: the `/proc/self/ns/pid` link target on Linux, the boot-session UUID on macOS, the machine's `MachineGuid` on Windows; `""` when the host withholds it. Two processes may probe each other's ids when their namespaces are equal. |
| `Process.isAlive(pid Pid)` | `bool` | — | True while a process with id `pid` exists; `false` once it has ended and been reaped, and for an id no host hands out (0, or one past 32 bits). |
| `Process.enterBackgroundPriority()` | `BackgroundPriority` | — | Put this process at background scheduling priority and answer the priority the OS reports afterwards: a priority class on Windows (`BELOW_NORMAL_PRIORITY_CLASS`, 16384), a nice value under POSIX (10). Children spawned afterwards inherit it. `BackgroundPriority` is `int(i64.min to i64.max)`. |
| `Process.environmentVariable(name String)` | `String` | `ProcessIntrospectionError.variableUnset`, `.environmentUnreadable` | The variable's value. Names are case-insensitive on Windows (`Path` answers to `PATH`) and exact elsewhere. An unset variable throws; a variable set to `""` returns `""`. |
| `Process.currentEnvironmentEntries()` | `StringArray` | `ProcessIntrospectionError.environmentUnreadable` | Every `NAME=VALUE` entry, in the order the OS reports them. Throws when the OS fails to hand the environment over. |
| `Process.envEntryName(entry String)` | `String` | — | The text before the first `=` (searching from the second byte, so Windows' `=C:=C:\dir` entries keep their name). |
| `Process.envEntryValue(entry String)` | `String` | — | The text after that `=`, or `""`. |
| `Process.envNameKey(name String)` | `String` | — | The key a variable name is compared by: lower-cased on Windows, `name` as given elsewhere. Two names with equal keys are one variable. |
| `EnvNameValueSeparator` | `Byte` | — | The separator byte, `=` (61). |

```maxon
enum ProcessIntrospectionError implements Error
	pathUnavailable
	variableUnset
	environmentUnreadable
end 'ProcessIntrospectionError'
```

```maxon
function main() returns ExitCode
	let exe = try Process.executablePath() otherwise FilePath.empty()
	let unset = try Process.environmentVariable("SURELY_NOT_SET_ANYWHERE") otherwise "unset"
	print("{exe.isAbsolute()} {unset}\n")
	print("{Process.envEntryName("A=b=c")} {Process.envEntryValue("A=b=c")}\n")
	return 0
end 'main'
```

Output: `true unset` and `A b=c`.

## Subprocess

`Subprocess` starts child processes. `Subprocess.run` waits for the child and collects its output;
`Configuration` gives full control; `StreamingSubprocess` keeps the child's pipes open for line-by-line
conversation. A spawn from a green thread parks that green thread, so others keep running.

Not available on `wasm32-wasi`, which has no process spawning: every call is refused at compile time with
E3074.

```maxon
function main() returns ExitCode
	var args = StringArray.create()
	args.push("--version")

	let result = try Subprocess.run(Executable.name("git"), arguments: args) otherwise (e) 'failed'
		print("could not run git: {e.displayReason()}\n")
		return 1
	end 'failed'

	print("{result.succeeded()} {result.exitCode()} {result.stdout.startsWith("git version")}\n")
	return 0
end 'main'
```

With `git` on `PATH`, it prints `true 0 true`.

### Subprocess

| Method | Returns | Description |
|--------|---------|-------------|
| `Subprocess.run(executable Executable, arguments StringArray)` | `CollectedOutput` | Inherit the working directory and environment, no stdin, collect stdout and stderr up to 16 MiB each, no timeout. |
| `Subprocess.run(executable, arguments:, workingDirectory FilePath)` | `CollectedOutput` | With a working directory. |
| `Subprocess.run(executable, arguments:, workingDirectory:, timeoutMs DurationMs)` | `CollectedOutput` | With a deadline after which the child's whole process tree is killed; `0` waits forever. |
| `Subprocess.runConfiguration(config Configuration)` | `CollectedOutput` | Run a configuration; `config.run()` calls this. |
| `Subprocess.runDetachedConfiguration(config Configuration)` | `Pid` | Start detached; `config.runDetached()` calls this. |

All of them throw `SubprocessError`.

### Executable

```maxon
union Executable
	name(value String)
	path(value FilePath)
end 'Executable'
```

`name` is searched for when the child is spawned; `path` is used as given. A relative `path` is relative
to the child's `workingDirectory` on every OS, and to the parent's working directory when none is set. A
`name` that carries a directory part (`sub/tool`, `./tool`, or on Windows `C:tool`) is anchored the same
way and looked for in that one directory.

On POSIX a `name` is searched for on `PATH` alone. On Windows the search follows `CreateProcessW`'s
order: the directory the running program was loaded from, this program's working directory while
`NoDefaultCurrentDirectoryInExePath` is unset, the system directory, the 16-bit system directory, the
Windows directory, then `PATH`. On Windows a name with an extension is tried as given, and a name without
one is tried with each `PATHEXT` extension in turn.

| Member | Returns | Description |
|--------|---------|-------------|
| `resolve()` | `FilePath` | The arm as a path, without searching `PATH`. Throws `ExecutableError.notFound` only for a name that is not a valid path; it is not an "is this installed" check. |
| `displayName()` | `String` | A readable form for messages. |

`ExecutableError` has one case, `notFound`.

### Configuration

Create one with `Configuration.create(executable)`, assign the fields you need, then run it.

| Field | Type | Default |
|-------|------|---------|
| `executable` | `Executable` | as given |
| `arguments` | `StringArray` | empty |
| `workingDirectory` | `FilePath` | empty, meaning the parent's |
| `environment` | `Environment` | `inherit` |
| `standardInput` | `InputSource` | `none` |
| `standardOutput` | `OutputDestination` | `collect` up to 16 MiB |
| `standardError` | `OutputDestination` | `collect` up to 16 MiB |
| `timeoutMs` | `DurationMs` | `0`, no deadline. Non-zero kills the child's whole process tree. |
| `platformOptions` | `PlatformOptions` | `PlatformOptions.defaults()` |

| Method | Returns | Description |
|--------|---------|-------------|
| `run()` | `CollectedOutput` | Run and collect. Throws `SubprocessError`. |
| `runDetached()` | `Pid` | Start without waiting and return the process id. Standard streams are discarded. Throws `SubprocessError`. |

`PlatformOptions` has two `bool` fields, `windowsHideWindow` and `windowsCreateNewProcessGroup`;
`PlatformOptions.defaults()` sets both false.

### Environment, input and output

```maxon
union Environment
	inherit
	inheritUpdating(overrides EnvMap)
	custom(vars EnvMap)
end 'Environment'

union InputSource
	none
	inherit
	bytes(data String)
	file(path FilePath)
	hold
	delayed(data String)
end 'InputSource'

union OutputDestination
	discard
	inherit
	collect(limitBytes int(0 to u64.max))
	file(path FilePath)
end 'OutputDestination'
```

| Environment | The child sees |
|-------------|----------------|
| `inherit` | This process's environment |
| `inheritUpdating(overrides)` | This process's environment with `overrides` applied |
| `custom(vars)` | Exactly `vars` |

`EnvMap` is `Map with String, String`. On Windows variable names compare as `Process.envNameKey` folds
them, ignoring case: an override replaces the inherited variable of any spelling and the child sees the
override's spelling, and a map that names one variable in two spellings makes the spawn throw
`SubprocessError.spawnFailed`.

| InputSource | The child's stdin |
|-------------|-------------------|
| `none` | Closed; reads see end of input immediately |
| `inherit` | This process's stdin |
| `bytes(data)` | `data` whole, NUL bytes included, then end of input |
| `file(path)` | The file's contents |
| `hold` | A pipe that stays open and silent until the child exits, so a read blocks |
| `delayed(data)` | Like `hold` until about one second after the child's first stdout byte, then `data` whole and end of input. Requires `standardOutput` to be `collect`; any other pairing throws `spawnFailed` before spawning. |

| OutputDestination | The stream |
|-------------------|------------|
| `discard` | Dropped |
| `inherit` | Passed through to this process's stream |
| `collect(limitBytes)` | Captured into `CollectedOutput`, truncated at the `limitBytes` limit |
| `file(path)` | Written to a file |

A run feeds `bytes` and `delayed` input while it drains the child's output, so a child that writes before it
reads still receives its whole input, and a `timeoutMs` deadline fires while input is still pending.

A child inherits only the three standard streams its own configuration gives it; the files this process
holds open and the pipes of other spawns in progress at the same moment stay with this process.

### Results

`CollectedOutput` has public fields and a factory, `CollectedOutput.create(status, stdout:, stderr:, pid:,
durationMs:)`:

| Field / method | Type | Description |
|----------------|------|-------------|
| `status` | `TerminationStatus` | How the child ended |
| `stdout`, `stderr` | `String` | Collected output (empty unless collected) |
| `pid` | `int(0 to u64.max)` | The child's process id |
| `durationMs` | `DurationMs` | Wall time the run took |
| `succeeded()` | `bool` | Exited with code 0 |
| `exitCode()` | `int(0 to u32.max)` | The raw code |

```maxon
union TerminationStatus
	exited(code int(0 to u32.max))
	signalled(code int(0 to u32.max))
end 'TerminationStatus'
```

On Linux and macOS a child that a signal killed is `signalled(signal)`, carrying the signal number; every other
end is `exited(code)`. A Windows child that ended abnormally is `exited` with its NTSTATUS code. `isSuccess()`
is true for `exited(0)`; `code()` returns the number either way.

### SubprocessError

```maxon
union SubprocessError implements Error
	executableNotFound(name String)
	spawnFailed(reason String)
	ioFailed(reason String)
	timeout(elapsedMs DurationMs, stdout String, stderr String)
	inputTooLarge
	endOfStream
end 'SubprocessError'
```

`executableNotFound` is thrown on every target when the executable does not exist: a bare name no search
finds, or an `Executable.path` naming a missing file. `spawnFailed` is any other refusal to start the child —
a `file` stream that cannot be opened and a `workingDirectory` that does not exist included, though either
fails with a not-found code; its reason carries the OS error number (`os error 5`). A spawn whose pipe
cannot be made throws it with the error of the call that failed, `os error 4` when the process has too many
files open.

A NUL byte in the program name or path, an argument, the working directory, an environment variable's name
or value, or a `file` stream's path throws `spawnFailed` before any child starts, with a reason naming that
operand.

`timeout` carries the output the child had already produced when the deadline killed it, since that partial
text is usually the only evidence of why it hung. Both fields are empty when the layer that threw was not
collecting output, which is `StreamingSubprocess.waitWithTimeout`.

`endOfStream` is what a `StreamingSubprocess` line reader throws when the stream it reads has ended.

`displayReason()` renders any case as one line, such as
`timed out after 5000ms, and the kill was sent to the child's whole process tree`. `timedOut()` is true for
`timeout` and `endedTheStream()` for `endOfStream`, so a caller can test for either without a `match`.

`SpawnPreparation` — `none`, `standardStream`, `workingDirectory` — names the step of a spawn the runtime
records a failure at; a not-found code at `none` is what makes a failure `executableNotFound`.

### RunProcessError

```maxon
enum RunProcessError implements Error
	spawnFailed
end 'RunProcessError'
```

The error the library-tier intrinsic `__Builtins.runProcess` throws when its command names nothing the host
can start. A program starts processes through `Subprocess`, which reports its failures as `SubprocessError`.

### StreamingSubprocess

A child whose standard streams stay open as pipes the caller drives, for a long-lived worker that answers
request after request. A read parks the calling green thread until data arrives.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `StreamingSubprocess.spawn(executable, arguments:)` | `StreamingSubprocess` | `SubprocessError` | Spawn in the parent's working directory. |
| `StreamingSubprocess.spawnWithCwd(executable, arguments:, workingDirectory:)` | `StreamingSubprocess` | `SubprocessError` | With a working directory. |
| `StreamingSubprocess.spawnWithEnvironment(executable, arguments:, workingDirectory:, environment Environment)` | `StreamingSubprocess` | `SubprocessError` | With a working directory (empty for the parent's) and an environment. |
| `StreamingSubprocess.spawnTraceable(executable, arguments:, workingDirectory:, environment Environment, traced bool)` | `StreamingSubprocess` | `SubprocessError` | As `spawnWithEnvironment`; with `traced`, the child is created for a debugger: on Windows and Linux this process becomes its debugger, and on macOS the child is created suspended, for a debugger to attach to by its `processId()`. |
| `processId()` | `Pid` | `SubprocessError` | The child's operating-system process id. Throws once the handle is released. |
| `writeStdinLine(line String)` | — | `SubprocessError` | Write `line` and a newline, NUL bytes included. Throws on a broken pipe. |
| `readStdoutLine()` | `String` | `SubprocessError` | The next line without its terminator (CRLF or LF); `""` is a blank line. Throws `endOfStream` when the stream has ended. Lines over 1 MiB arrive in pieces. |
| `readStdoutLineCapped(maxBytes)` | `String` | `SubprocessError` | With an explicit per-call cap. |
| `readStdoutBytes(count)` | `String` | `SubprocessError` | Exactly `count` bytes, fewer only at end of stream; nothing is stripped. For length-framed protocols. Shares a buffer with the line readers. |
| `readStderrLine()` | `String` | `SubprocessError` | As `readStdoutLine`, for stderr. |
| `readStderrLineCapped(maxBytes)` | `String` | `SubprocessError` | With a cap. |
| `tryReadStdoutLine()` | `LinePoll` | — | A line if one is already buffered; never blocks. |
| `tryReadStderrLine()` | `LinePoll` | — | As above, for stderr. |
| `pollExit()` | `ExitPoll` | — | Whether the child has exited, without blocking or killing it. A released handle answers `running`. On Linux and macOS a child a signal killed reports `exited(128 + signal)`. |
| `pollTermination()` | `TerminationPoll` | — | As `pollExit()`, answering how the child ended: `signalled(signal)` for a child a signal killed on Linux and macOS, `exited(code)` otherwise. |
| `closeStdin()` | — | — | Close the child's stdin so it sees end of input. Idempotent. |
| `wait()` | exit code | `SubprocessError` | Block until the child exits. |
| `waitWithTimeout(timeoutMs DurationMs)` | exit code | `SubprocessError` | Throws `timeout` and kills the child's whole process tree when the deadline passes; `0` waits forever. The thrown `timeout` carries empty output fields — a streaming child's bytes belong to the caller draining the streams. |
| `release()` | — | — | Free the OS handle. Idempotent. Forgetting it leaks the handle and a process slot. |
| `handle`, `released` | fields | — | The raw handle and whether it has been released. |

```maxon
union LinePoll
	line(text String)
	none
end 'LinePoll'

union ExitPoll
	running
	exited(code int(0 to u32.max))
end 'ExitPoll'

union TerminationPoll
	running
	ended(status TerminationStatus)
end 'TerminationPoll'
```

`LinePoll` exists because a blank line from the child and "nothing buffered" are both `""` once the
terminator is removed. `none` says nothing about whether the stream has ended; ask `pollExit()`.

```maxon
function main() returns ExitCode
	var args = StringArray.create()
	args.push("--version")

	var child = try StreamingSubprocess.spawn(Executable.name("git"), arguments: args) otherwise (e) 'spawn'
		print("{e.displayReason()}\n")
		return 1
	end 'spawn'

	let line = try child.readStdoutLine() otherwise (e) 'read'
		print("{e.displayReason()}\n")
		return 1
	end 'read'

	let code = try child.wait() otherwise -1

	let state = match child.pollExit() 'poll'
		running gives "running"
		exited(c) gives "exited {c}"
	end 'poll'

	child.release()
	print("{line.startsWith("git version")} {code} {state}\n")
	return 0
end 'main'
```

With `git` on `PATH`, it prints `true 0 exited 0`.

## SharedMemory

A `SharedSegment` is a named block of memory that several processes map at once: one publishes it under a
name, another adopts the same name and sees the same bytes. Available on `x64-windows` (Win32 section
objects), `arm64-macos` (`shm_open` and `mmap`) and both Linux targets (a `/dev/shm` file and `mmap`);
refused at compile time elsewhere.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `SharedSegment.create(name String, bytes SegmentByteCount)` | `SharedSegment` | `SharedMemoryError` | Publish a section of `bytes` bytes under `name` and map it, adopting one already published under that name. |
| `SharedSegment.publish(name String, bytes SegmentByteCount)` | `SharedSegment` | `SharedMemoryError` | Publish a NEW section of `bytes` bytes under `name` and map it; a name already published is refused with `createFailed`. |
| `segmentName()` | `String` | — | The name another process maps it by. |
| `readWord(offset SegmentOffset)` | `SegmentWord` | `SharedMemoryError` | The 64-bit word `offset` bytes in. |
| `writeWord(offset SegmentOffset, value SegmentWord)` | — | `SharedMemoryError` | Write a 64-bit word `offset` bytes in. |
| `copyOut(offset SegmentOffset, byteCount SegmentByteCount)` | `ByteArray` | `SharedMemoryError` | An independent copy of a byte range. |
| `close()` | — | — | Unmap and release the section, and withdraw its name if this segment published it. Idempotent. A `readWord`, `writeWord` or `copyOut` afterwards throws `SharedMemoryError.closed`. |

`readWord` is an acquire load and `writeWord` a release store, so a word is how one process announces
bytes to another: once `readWord` returns a value another process wrote with `writeWord`, every write that
process made before it is visible, including a range read afterwards with `copyOut`.

Offsets are in bytes, not words. Every access is checked against the section's size: a word (8 bytes) or a
`copyOut` range (`offset + byteCount`) that would reach past the end throws `SharedMemoryError.outOfBounds`
and touches nothing.

**`create` adopts a name that is already published**, keeping the size that section already has; on every
target one smaller than `bytes` is refused with `SharedMemoryError.createFailed`. Two processes creating one
name at once both succeed, one publishing and one adopting. On Linux and macOS a section whose publisher exited
without `close()` is still published, and `create` adopts it. **`publish` only publishes**: a caller that needs
a fresh section gets one, or `createFailed` when the name is taken. **`close` withdraws the name only when this
segment published it**: an adopter's `close` leaves the name published for as long as the publisher holds it.

| Type | Definition |
|------|------------|
| `SegmentByteCount` | `int(1 to u32.max)` — never zero |
| `SegmentOffset` | `int(0 to u32.max)` |
| `SegmentWord` | `int(i64.min to i64.max)` |

```maxon
union SharedMemoryError implements Error
	createFailed
	mapFailed
	invalidName
	outOfBounds
	closed
end 'SharedMemoryError'
```

`createFailed`: the name is published by a section smaller than `bytes` (`create`) or is published at all
(`publish`), or the host refuses the name or the size.
`invalidName`: the name is empty, longer than 31 bytes, or holds a `/`, a `\` or a NUL byte. The rule is the
same on every target (31 bytes is macOS's limit), so a name that works on one host works on all of them.
`mapFailed`: no address space for the view. `outOfBounds`: a `readWord`, `writeWord` or `copyOut` would
reach past the end of the section. `closed`: a `readWord`, `writeWord` or `copyOut` on a segment after its
`close()`.

Always `close()` a segment. A section stays alive while any view of it is mapped, and on Linux and macOS the
name outlives the process until it is withdrawn or the machine restarts.

```maxon
function main() returns ExitCode
	var segment = try SharedSegment.create("docs-demo-segment", bytes: 4096) otherwise (e) 'failed'
		print("no segment: {e}\n")
		return 1
	end 'failed'

	try segment.writeWord(8, value: 42) otherwise return 2
	let word = try segment.readWord(8) otherwise return 2
	let copied = try segment.copyOut(8, byteCount: 8) otherwise return 2
	print("{segment.segmentName()} {word} {copied.count()}\n")
	segment.close()
	return 0
end 'main'
```

Output: `docs-demo-segment 42 8`.

## Debuggee

The debugger surface: `DebugPort` drives a debuggee through the Win32 debug API on Windows, and
`ProcessTrace` drives one through `ptrace` on Linux. Both types compile on every target; an operation that
reaches the debug channel of the other host panics. Each owns a scratch region it reserves at `create()`
(`isReady()` reports whether it did) and releases at `close()`; an operation after `close()` panics.

A transfer through `readMemory` or `writeCode` moves at most `DebugTransferBytes` (512) bytes. A longer one
throws `unreadable` or `unwritable` before the debuggee is touched, and a transfer of 0 bytes returns at once.
`transferSizeOf(count TransferByteCount)` answers which of the three a count is: a `TransferSize` of
`nothing`, `withinTheWindow` or `pastTheWindow`.

```maxon
enum DebuggeeError implements Error
	unreadable
	unwritable
	outOfBounds
end 'DebuggeeError'
```

`unreadable`: memory or registers the OS refused to read, or a read past the window.
`unwritable`: a write the debuggee refused, a write past the window, or a register block of the wrong size.
`outOfBounds`: a `RegisterBlock` offset off a word boundary or past the end.

### RegisterBlock

Every register set a debuggee hands back is a `RegisterBlock`: a record of 64-bit words read at word-aligned
byte offsets.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `word(offset RegisterOffset)` | `RegisterWord` | `DebuggeeError.outOfBounds` | The word at byte `offset`, a multiple of 8 below the block's size. |
| `with(offset RegisterOffset, word RegisterWord)` | `RegisterBlock` | `DebuggeeError.outOfBounds` | A new block with that word replaced; the receiver keeps its words. |
| `programCounter()`, `stackPointer()`, `framePointer()` | `RegisterWord` | `DebuggeeError.outOfBounds` | `rip`, `rsp` and `rbp` of a Windows x64 `CONTEXT`. |
| `flags()` | `RegisterWord` | `DebuggeeError.outOfBounds` | `EFlags` of a Windows x64 `CONTEXT`. |
| `gpr(index Win64GprIndex)` | `RegisterWord` | `DebuggeeError.outOfBounds` | General-purpose register `index` of a `CONTEXT`, in its order `rax`, `rcx`, `rdx`, `rbx`, `rsp`, `rbp`, `rsi`, `rdi`, `r8` to `r15`. |
| `xmmLane(index Win64XmmIndex)` | `RegisterWord` | `DebuggeeError.outOfBounds` | The low 64 bits of `xmm<index>` of a `CONTEXT`. |
| `withProgramCounter(address RegisterWord)` | `RegisterBlock` | `DebuggeeError.outOfBounds` | A new `CONTEXT` with `rip` replaced. |
| `withFlags(flags RegisterWord)` | `RegisterBlock` | `DebuggeeError.outOfBounds` | A new `CONTEXT` with `EFlags` replaced. |

The `CONTEXT` accessors throw `outOfBounds` for a block of any other size.

### DebugPort

`DebugPort` holds the event the debuggee last reported; `continueEvent` and `eventThread` refer to it.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `DebugPort.create()` | `DebugPort` | — | A port with its scratch region reserved. |
| `isReady()` | `bool` | — | True while the scratch region is held. |
| `close()` | — | — | Release the scratch region. |
| `waitEvent(budgetMs DebuggeeWaitMs)` | `DebugPortEvent` | — | Wait up to `budgetMs` for the next debug event. |
| `eventThread()` | `DebuggeeThreadId` | — | The thread that reported the last event. |
| `continueEvent(disposition DebugDisposition)` | `bool` | — | Let the debuggee run on from the last event, `handled` or `unhandled`; true when the OS accepted it. |
| `readMemory(process DebugHandle, address DebuggeeAddress, count DebuggeeByteCount)` | `ByteArray` | `DebuggeeError.unreadable` | `count` bytes of the debuggee's memory; a short read throws. |
| `writeCode(process DebugHandle, address DebuggeeAddress, content ByteArray)` | — | `DebuggeeError.unwritable` | Write `content` into code: lift the page protection, write, restore it and flush the instruction cache. |
| `readContext(thread DebugHandle)` | `RegisterBlock` | `DebuggeeError.unreadable` | The thread's `CONTEXT`: control, integer and floating-point registers. |
| `writeContext(thread DebugHandle, context RegisterBlock)` | — | `DebuggeeError.unwritable` | Set the thread's `CONTEXT` from a block of `CONTEXT` size. |
| `suspend(thread DebugHandle)`, `resume(thread DebugHandle)` | `bool` | — | Suspend or resume the thread; true on success. |
| `breakIn(process DebugHandle)` | `bool` | — | Make the debuggee raise a breakpoint. |
| `detach(processId DebuggeeProcessId)` | `bool` | — | Stop debugging the process and let it run. |

`DebugHandle` is an OS handle a `DebugPortEvent` carries. `DebugDisposition` is `handled` or `unhandled`.

| `DebugPortEvent` case | Meaning |
|-----------------------|---------|
| `timedOut` | The budget passed with the debuggee silent. |
| `processCreated(processId, thread, process, threadHandle, imageBase)` | The debuggee started; `process` and `threadHandle` are its `DebugHandle`s. |
| `threadCreated(thread, threadHandle, startAddress)` | A thread started. |
| `threadExited(thread)` | A thread ended. |
| `processExited(thread, exitCode)` | The debuggee ended with `exitCode` (`DebuggeeExitCode`). |
| `libraryLoaded(thread)` | A DLL loaded. |
| `exception(thread, code, address)` | An exception, with its `DebuggeeFaultCode` and address. |
| `other(thread)` | Any other debug event. |

### ProcessTrace

`ProcessTrace` addresses the debuggee by process and thread id.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `ProcessTrace.create()` | `ProcessTrace` | — | A tracer with its scratch region reserved. |
| `isReady()` | `bool` | — | True while the scratch region is held. |
| `close()` | — | — | Release the scratch region. |
| `configure(process DebuggeeProcessId)` | `bool` | — | Trace new threads of `process` too, and kill it when the tracer exits. |
| `wait(budgetMs DebuggeeWaitMs, process DebuggeeProcessId)` | `TraceReport` | — | Wait up to `budgetMs` for a thread of `process` to report. |
| `resume(thread DebuggeeThreadId, signal DebuggeeSignal, stepping bool)` | `bool` | — | Continue the thread, or single-step it when `stepping`, delivering `signal`; `NoTraceSignal` (0) resumes it silently. |
| `detach(thread DebuggeeThreadId, signal DebuggeeSignal)` | `bool` | — | Stop tracing the thread, delivering `signal`. |
| `signalThread(process DebuggeeProcessId, thread DebuggeeThreadId, signal DebuggeeSignal)` | `bool` | — | Send `signal` to one thread. |
| `signalProcess(process DebuggeeProcessId, signal DebuggeeSignal)` | `bool` | — | Send `signal` to the process. |
| `signalWasSentByKill(thread DebuggeeThreadId)` | `bool` | — | True when the signal that stopped the thread came from `kill`. |
| `readMemory(process DebuggeeProcessId, address DebuggeeAddress, count DebuggeeByteCount)` | `ByteArray` | `DebuggeeError.unreadable` | `count` bytes of the debuggee's memory. |
| `writeCode(thread DebuggeeThreadId, address DebuggeeAddress, content ByteArray)` | — | `DebuggeeError.unwritable` | Write `content` a word at a time, keeping the bytes around it. |
| `registers(thread DebuggeeThreadId)` | `RegisterBlock` | `DebuggeeError.unreadable` | The thread's general registers, as the kernel lays them out for the architecture. |
| `setRegisters(thread DebuggeeThreadId, block RegisterBlock)` | — | `DebuggeeError.unwritable` | Set them from a block of that size. |
| `vectorRegisters(thread DebuggeeThreadId)` | `RegisterBlock` | `DebuggeeError.unreadable` | The thread's floating-point and vector registers. |

Three public constants name where the general-register block keeps the registers a frame walk starts from,
as byte offsets for the architecture the program is built for:

| Constant | arm64 | x64 |
|----------|-------|-----|
| `TraceFramePointerOffset` | 232 (`x29`) | 32 (`rbp`) |
| `TraceStackPointerOffset` | 248 (`sp`) | 152 (`rsp`) |
| `TraceProgramCounterOffset` | 256 (`pc`) | 128 (`rip`) |

| `TraceReport` case | Meaning |
|--------------------|---------|
| `nothing` | The budget passed with the debuggee silent. |
| `exited(thread, code)` | The thread exited with `code`. |
| `killed(thread, status)` | A signal ended the thread; `status` is the raw wait status. |
| `stopped(thread, signal, event)` | The thread stopped with `signal`; `event` is a `TraceEvent`: `signalStop`, `cloneEvent` (it started a thread) or `otherEvent`. |

`arrived()` is true for every case but `nothing`.

| Type | Definition |
|------|------------|
| `DebuggeeAddress`, `DebuggeeThreadId`, `DebuggeeProcessId`, `TransferByteCount` | `int(0 to u64.max)` |
| `DebuggeeByteCount` | `int(0 to 0x40000000)` |
| `DebuggeeWaitMs` | `int(0 to 0xFFFFFFFF)` |
| `DebuggeeExitCode`, `RegisterWord` | `int(i64.min to i64.max)` |
| `DebuggeeFaultCode` | `int(0 to u32.max)` |
| `DebuggeeSignal` | `int(0 to 255)` |
| `RegisterOffset` | `int(0 to 0xFFFF)` |
| `Win64GprIndex`, `Win64XmmIndex` | `int(0 to 15)` |

## Sampler

A sampling profiler's view of another process, on every native target. `SampledProcess` stops a thread
briefly, copies its registers and the top of its stack, and lets it run on; `SampleTimer` paces the samples.
A thread is stopped through its thread handle on `x64-windows`, its Mach thread port on `arm64-macos`, and
`ptrace` on `arm64-linux` and `x64-linux`.

| `SampledProcess` member | Returns | Throws | Description |
|-------------------------|---------|--------|-------------|
| `SampledProcess.open(pid Pid)` | `SampledProcess` | `SamplerError.noSuchProcess`, `.noScratch`, `.accessDenied`, `.threadNotPinned` | Open process `pid` for sampling and reserve its scratch regions. `accessDenied`: macOS refused the process's task port, or Linux refused to trace it. `threadNotPinned`: on Linux the calling green thread could not be pinned to its machine, which `ptrace` needs because it answers only the OS thread that attached. On Linux, a main thread in uninterruptible sleep is waited on for up to 10 seconds and then taken as traceable. |
| `SampledProcess.cyclesAreNanoseconds()` | `bool` | — | True on macOS and Linux, where `cycles` and `suspendedCycles` are CPU nanoseconds; false on Windows, where they are the thread's cycle counter. |
| `SampledProcess.foreignCodeKeepsTheFrameChain()` | `bool` | — | True on macOS and Linux, where a frame-pointer walk may continue through code outside the sampled program; false on Windows. |
| `threads()` | `SampledThreadArray` | — | Open every thread the process has now. Each stays open until `release` or `close`. |
| `release(thread SampledThread)` | — | — | Close one thread this sampler opened. |
| `cycles(thread SampledThread)` | `SampleCycles` | `SamplerError.unknownThread` | CPU time the thread has consumed, in the unit `cyclesAreNanoseconds()` names. On macOS and Linux a running thread is stopped to read it, and the next `capture` of that thread answers from the same stop. |
| `capture(thread SampledThread)` | `SampleCapture` | `SamplerError.unknownThread`, `.callingThread` | Sample one thread. `unknownThread` refuses a thread `threads()` did not hand out or `release` let go; `callingThread` refuses the thread making the call, which would stop itself. On Linux a thread in uninterruptible sleep answers `unreadable`. |
| `modules()` | `SampledModuleArray` | `SamplerError.moduleListUnavailable` | Every module loaded in the process: the module snapshot on Windows and the dyld image list on macOS, each retried while the OS cannot answer yet. On Linux, whose images are static, an empty array while the process lives. |
| `close()` | — | — | Close every open thread, the process and the scratch regions. On Linux it detaches every thread it stopped, then unpins the calling green thread; call it on the green thread that opened the sampler. |
| `clone()` | — | — | Panics: a sampler owns one process's handles, so it is held in one place. |

On Linux a thread that cannot be detached within 10 seconds has its process killed rather than left
stopped, and stderr carries one line per process:
`sampler: thread <tid> of process <pid> could not be detached within 10000 ms, so the process was killed rather than left stopped`.

| `SampleCapture` case | Meaning |
|----------------------|---------|
| `threadGone` | The thread exited. |
| `unreadable(reservation, suspendedCycles)` | The thread was stopped, and its registers were unreadable. |
| `captured(stack)` | A `SampledStack`. |

A `SampledStack` has `programCounter`, `framePointer` and `stackPointer`; `reservation`, a
`StackReservation`; `suspendedCycles`, the thread's CPU time read while it was stopped; and `window`, a
`StackWindow` holding up to 64 KiB copied from the thread's stack. A `StackWindow` stays readable until the
sampler's next `capture`: `byteCount()` is the bytes copied, 0 once a later capture has replaced them, and
`word(offset StackWindowOffset)` is the word `offset` bytes in, throwing `SamplerError.outOfWindow` outside the
copy or after a later capture.

| `StackReservation` case | Meaning |
|-------------------------|---------|
| `base(address)` | The base of the reservation the stack lives in, unique to each live stack. Windows reports it. |
| `unreported` | The reservation is unknown, as on macOS and Linux. |

`SampledThread` has a public `id` (`SampledThreadId`). `SampledModule` has `base`, `limit`, `name` and
`fileType`. On Windows each UTF-16 unit above ASCII in the name reads as `?`, and `fileType` is 0; on macOS
the name is the last component of the image's path and `fileType` is its Mach-O header's file type (2 for
the executable).

| `SampleTimer` member | Returns | Throws | Description |
|----------------------|---------|--------|-------------|
| `SampleTimer.create(interval SampleIntervalNanos)` | `SampleTimer` | — | A periodic timer. On Windows its steps are rounded to 100 ns; elsewhere each wait sleeps to its deadline, rounded up to a microsecond. |
| `ready()` | `bool` | — | True when the host gave the timer `create` asked for; on Windows a refused timer reads false. |
| `wait()` | — | — | Sleep until the next tick: each deadline is the previous one plus the interval, so the samples keep their rate. Panics after `close()`, and on a timer that is not `ready()`. |
| `close()` | — | — | Release the timer. |

```maxon
enum SamplerError implements Error
	noSuchProcess
	noScratch
	unknownThread
	outOfWindow
	moduleListUnavailable
	callingThread
	accessDenied
	threadNotPinned
end 'SamplerError'
```

`SampleCycles`, `SampledThreadId` are `int(0 to u64.max)`; `SampleIntervalNanos` is `int(1 to u64.max)`;
`StackWindowOffset` is `int(0 to i64.max)`; `SampledThreadArray` and `SampledModuleArray` are arrays of
`SampledThread` and `SampledModule`. The module is declared under `#if not os(Wasi)`, so its names exist on
every native target.

## TcpClient

`TcpClient` is a TCP connection over IPv4. It closes its socket when the last reference to it goes away, so
`close()` is optional.

Available on `x64-windows`, `arm64-macos`, `arm64-linux` and `x64-linux`; refused at compile time (E3104) on
`wasm32-wasi`. Windows and macOS resolve host names through the platform resolver. On Windows a non-ASCII
host name reaches the resolver in its Unicode form, and Windows applies IDNA to it; the other targets hand
the name's UTF-8 bytes to their resolver as they are. The two Linux targets link a built-in resolver: it
accepts a numeric address, reads `/etc/hosts`, and otherwise sends an `A` query over TCP to the first
`nameserver` in `/etc/resolv.conf`. It looks the name up exactly as written, ignoring `search`/`domain`
lines, asks that one nameserver only, and waits on it for as long as it takes to answer.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpClient.connect(host String, port NetworkPort)` | `TcpClient` | `NetworkError` | Resolve `host` and connect. `resolveFailed` or `connectFailed`. |
| `TcpClient.connect(host String, port NetworkPort, deadlineMs SocketDeadlineMs)` | `TcpClient` | `NetworkError` | The same, throwing `timedOut` when the connection is not made within `deadlineMs`; `0` waits with no deadline. |
| `TcpClient.adopt(socket)` | `TcpClient` | — | Wrap an already-connected socket; how `TcpListener.accept()` returns its connections. |
| `send(data String)` | `int(0 to u64.max)` | `NetworkError` | Send every byte, looping over partial sends; returns the byte count. |
| `recv(bufferSize RecvCapacity)` | `String` | `NetworkError` | Between one and `bufferSize` bytes. Throws `connectionClosed` when the peer has closed. |
| `setReadDeadline(milliseconds SocketDeadlineMs)` | — | `NetworkError` | Make a read that waits longer than this throw `timedOut`. Measured from this call; `0` clears it. |
| `setWriteDeadline(milliseconds SocketDeadlineMs)` | — | `NetworkError` | The same for sends. A send goes to the OS in 1 MiB slices, so the deadline also ends a large send to a peer that has stopped reading. |
| `shutdownWrite()` | — | `NetworkError` | Stop sending: the peer reads end-of-stream while this side can still `recv`. |
| `close()` | — | — | Close the connection. Idempotent. |

A deadline setter and `shutdownWrite` throw `connectionClosed` when the socket is already closed.

`NetworkPort` is `int(0 to 65535)`. Port `0` asks `TcpListener.bind` for any free port; a live connection
always has a nonzero one. `SocketDeadlineMs` is `int(0 to 4294967295)`, the milliseconds a deadline setter takes.
`RecvCapacity` is `int(1 to i64.max)`: a read of zero bytes is how a closed peer is reported, so a buffer
holds at least one.

```maxon
enum NetworkError implements Error
	resolveFailed
	connectFailed
	sendFailed
	recvFailed
	connectionClosed
	timedOut
	bindFailed
	acceptFailed
	acceptInterrupted
end 'NetworkError'
```

| Case | Meaning |
|------|---------|
| `resolveFailed` | The host name did not resolve. An empty host, or one holding a NUL byte, is always this, for `connect` and `bind` alike |
| `connectFailed` | The connection was refused or could not be made |
| `sendFailed`, `recvFailed` | The OS reported an error |
| `connectionClosed` | The peer closed the connection |
| `timedOut` | A connect, read, write or accept deadline expired |
| `bindFailed` | The address or port could not be bound |
| `acceptFailed` | The listener can accept nothing more: it is closed, or the OS refused it for good |
| `acceptInterrupted` | The OS refused one connection, for example with the process out of descriptors; the listener can take the next |

These also arise without any OS error: a coroutine whose promise has been dropped or `cancel()`ed throws the
variant of the next operation that would wait on a far end rather than starting it — see
[Cancellation and Dropped Promises](LANGUAGE_REFERENCE.md#cancellation-and-dropped-promises). `close()` is
unaffected.

## TcpListener

`TcpListener` is a listening socket. It has no `send` or `recv`; `accept()` returns a `TcpClient` for each
connection. `accept()` parks its green thread until a connection arrives, so a waiting server blocks no OS
thread. It closes when the last reference goes away. Same targets as `TcpClient`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpListener.bind(host String, port NetworkPort)` | `TcpListener` | `NetworkError` | Bind and listen. Port `0` takes any free port. |
| `port()` | `NetworkPort` | — | The bound port, as the OS reports it. |
| `accept()` | `TcpClient` | `NetworkError` | The next connection. Throws `timedOut` past the accept deadline, `acceptInterrupted` when the OS refused one connection, and `acceptFailed` when the listener is closed or has failed. |
| `setAcceptDeadline(milliseconds SocketDeadlineMs)` | — | `NetworkError` | Make an `accept()` still waiting `milliseconds` after this call throw `timedOut`; `0` clears it. Throws `acceptFailed` when the listener is closed. |
| `close()` | — | — | Stop listening and release the port. Idempotent. |

`SO_REUSEADDR` is not set, so binding a port a live listener holds throws `bindFailed`.

```maxon
function echoOnce(listener TcpListener) returns ExitCode throws NetworkError
	let conn = try listener.accept()
	let data = try conn.recv(1024)
	_ = try conn.send(data)
	return 0
end 'echoOnce'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let port = listener.port()
	let server = async echoOnce(listener)

	let client = try TcpClient.connect("127.0.0.1", port: port) otherwise return 2
	try client.setReadDeadline(5000) otherwise return 3
	_ = try client.send("ping") otherwise return 4
	let reply = try client.recv(1024) otherwise return 5
	client.close()

	_ = try await server otherwise 9
	print("{reply}\n")
	return 0
end 'main'
```

Output: `ping`.

## HttpClient

An HTTP/1.1 client over `TcpClient`. A request carries `Host` and `Connection: close` unless it sets its own,
and a `Content-Length` whenever it has a body.

A response body is framed by `Transfer-Encoding` when the response has one: a final `chunked` coding is
decoded (a chunk extension after `;` is ignored, and the trailer section is discarded), and any other coding
is read until the server closes the connection. Without `Transfer-Encoding` the body is `Content-Length`
bytes, or is read until the close when that is absent too. A response to `HEAD`, and a `204` or `304`,
ends at its head. Interim `1xx` responses before the final one are skipped, at most 8 of them.
`HttpError.invalidResponse` is thrown for a `101 Switching Protocols`, a response head over 64 KiB, a body over 1 TiB
(`HttpLargestBodyBytes`), and a connection that closes before the body is complete.

Every exchange has a deadline: `HttpClientLimits.timeoutMs`, 30000 by default, covers the whole exchange from
connect to the last body byte, and throws `HttpError.timedOut` when it passes. A body longer than
`HttpClientLimits.maxBodyBytes` throws `HttpError.bodyTooLarge`, however it is framed; the default is
`HttpBodyCap.unbounded`.

Limitations: plain HTTP only (no TLS) — a URL whose scheme is anything but `http`, `https` included, throws
`HttpError.unsupportedScheme` before any connection is attempted. A 3xx is returned as is, and the whole
response is held in memory. The URL's port defaults to 80.

### HttpClient

| Method | Returns | Description |
|--------|---------|-------------|
| `HttpClient.send(request HttpRequest, limits HttpClientLimits = HttpClientLimits.create())` | `HttpResponse` | Send a request within `limits`. |
| `HttpClient.get(url String)` | `HttpResponse` | `GET`. |
| `HttpClient.post(url String, body String)` | `HttpResponse` | `POST` with a body. |
| `HttpClient.put(url String, body String)` | `HttpResponse` | `PUT` with a body. |
| `HttpClient.delete(url String)` | `HttpResponse` | `DELETE`. |

All throw `HttpError`. `get`, `post`, `put` and `delete` use the default limits.

### HttpClientLimits

| Member | Type | Description |
|--------|------|-------------|
| `HttpClientLimits.create()` | `HttpClientLimits` | The defaults: `timeoutMs` 30000, `maxBodyBytes` `unbounded`. |
| `timeoutMs` | `HttpExchangeDeadlineMs` | Milliseconds for the whole exchange, connect included. `int(1 to 4294967295)`. |
| `maxBodyBytes` | `HttpBodyCap` | `unbounded`, or `atMost(bytes HttpByteCap)`. |

### HttpRequest

| Member | Returns | Description |
|--------|---------|-------------|
| `HttpRequest.create(method HttpMethod, url String)` | `HttpRequest` | Throws `HttpError.invalidUrl`. |
| `setHeader(name String, value String)` | — | Set a header. |
| `setBody(body String)` | — | Set the body. |
| `method()` | `HttpMethod` | |
| `url()` | `URL` | |
| `headers()` | `HttpHeaders` | |
| `body()` | `String` | |

### HttpResponse

| Member | Returns | Description |
|--------|---------|-------------|
| `HttpResponse.create(code StatusCode, reasonPhrase String, responseHeaders HttpHeaders, responseBody String)` | `HttpResponse` | Build a response. |
| `statusCode()` | `StatusCode` | |
| `reason()` | `String` | The reason phrase, such as `OK`. |
| `headers()` | `HttpHeaders` | |
| `body()` | `String` | |
| `header(name String)` | `String` | Throws `HttpError` when absent. |

### HttpHeaders

A case-insensitive header map; names are stored lowercased. The underlying map is the public `headers`
field.

| Member | Returns | Description |
|--------|---------|-------------|
| `HttpHeaders.create()` | `HttpHeaders` | An empty map. |
| `set(name String, value String)` | — | Set a header. |
| `get(name String)` | `String` | Throws `HttpError` when absent. |
| `has(name String)` | `bool` | Presence test. |

### Enums

| Enum | Cases |
|------|-------|
| `HttpMethod` | `get`, `post`, `put`, `delete`, `head`, `patch`, `options` |
| `HttpError` | `invalidUrl`, `connectFailed`, `sendFailed`, `recvFailed`, `invalidResponse`, `unsupportedScheme`, `timedOut`, `bodyTooLarge` |
| `StatusCode` | A union; `number()` gives the code as an `HttpStatusNumber` (`int(100 to 999)`). Any status without a named case is `other(code HttpStatusNumber)`. Named cases: `ok` 200, `created` 201, `accepted` 202, `noContent` 204, `movedPermanently` 301, `found` 302, `notModified` 304, `badRequest` 400, `unauthorized` 401, `forbidden` 403, `notFound` 404, `methodNotAllowed` 405, `requestTimeout` 408, `conflict` 409, `gone` 410, `lengthRequired` 411, `payloadTooLarge` 413, `tooManyRequests` 429, `headerFieldsTooLarge` 431, `internalServerError` 500, `notImplemented` 501, `badGateway` 502, `serviceUnavailable` 503, `httpVersionNotSupported` 505 |

```maxon
function serveOnce(listener TcpListener) returns ExitCode throws NetworkError
	let conn = try listener.accept()
	_ = try conn.recv(4096)
	_ = try conn.send("HTTP/1.1 200 OK\r\nX-Demo: yes\r\nContent-Length: 2\r\n\r\nhi")
	conn.close()
	return 0
end 'serveOnce'

function main() returns ExitCode
	let listener = try TcpListener.bind("127.0.0.1", port: 0) otherwise return 1
	let port = listener.port()
	let server = async serveOnce(listener)

	let response = try HttpClient.get("http://127.0.0.1:{port}/hello") otherwise (e) 'failed'
		print("request failed: {e}\n")
		return 1
	end 'failed'

	_ = try await server otherwise 9
	let demo = try response.header("x-demo") otherwise "absent"
	print("{response.statusCode().number()} {response.reason()} {response.body()} {demo}\n")
	return 0
end 'main'
```

Output: `200 OK hi yes`.

### Free functions

| Function | Returns | Description |
|----------|---------|-------------|
| `reasonPhraseOf(code StatusCode)` | `String` | The reason phrase HTTP/1.1 pairs with a status code, such as `Not Found` for 404; empty for `other`. |
| `statusCodeOf(number HttpStatusNumber)` | `StatusCode` | The named case for `number`, or `other(number)`. |

### Header parsing

A header line is `name:value`. The name is an HTTP token, and the value is trimmed of spaces and tabs at both
ends, so a line with no space after its colon is a valid header. A value may hold any byte but a control
character, tab excepted. A line that breaks one of these rules, or has no colon, is malformed: `HttpClient`
reading such a response throws `HttpError.invalidResponse`, and `HttpServer` answers such a request `400`.

A header that arrives more than once is read as one, its values joined by `, ` in arrival order.
`Set-Cookie` values are joined by a line feed, since a cookie's own value can hold a comma.
`Content-Length` repeated with a different value is malformed, and a repeat of the same value reads as one.
A request with more than one `Host` line is malformed.

A response line that opens with a space or tab continues the header above it (obsolete line folding), and
`HttpClient` joins the two with one space. In a request that line is malformed.

A `Host` header set with `HttpRequest.setHeader` is sent as set: the URL's authority supplies `Host` only
when the caller set none.

## HttpServer

An HTTP/1.1 server over `TcpListener`, on the same green-thread scheduler `TcpClient` and `HttpClient`
run on. It implements a documented subset: bodies framed by `Content-Length`, one request per connection,
and every response sent with `Connection: close`, after which the connection closes. Keep-alive, chunked
transfer, TLS and `Expect: 100-continue` are outside it.

### HttpServer

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `HttpServer.bind(host String, port NetworkPort)` | `HttpServer` | `NetworkError` | Bind and listen. `port: 0` takes whichever free port the kernel gives. |
| `port()` | `NetworkPort` | — | The port this server is bound to. |
| `setLimits(caps HttpServerLimits)` | — | — | Replace the caps later requests are parsed under. |
| `setAcceptDeadline(milliseconds SocketDeadlineMs)` | — | `HttpServerError` | Make an `accept()` still waiting `milliseconds` after this call throw `acceptTimedOut`; `0` clears it. Throws `listenerClosed` once the server is closed. |
| `accept()` | `HttpExchange` | `HttpServerError` | Take the next connection and the one request on it. An `OPTIONS *` is answered here, and the next connection taken. |
| `serve(handler HttpHandler)` | `HttpServeEnd` | — | Accept and answer until `close()`, a listener failure or the accept deadline. |
| `close()` | — | — | Stop listening: a parked or later `accept()` throws `listenerClosed`, which ends `serve`. |

`close()` writes the server, so the server it is called on is held in a `var`; through a `let` it is
[E3159](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3159).

`serve` hands each exchange to `handler.handle`. A handler that throws, or returns without answering, has
its exchange answered `500` with an empty body, and `handler.faulted` is told. A request `accept()` refuses
has already been answered with its status (see [Protocol errors](#protocol-errors)), and `serve` goes on
to the next connection. When the OS refuses one connection (`acceptInterrupted`), `serve` tells
`handler.faulted`, waits 5 ms and accepts again; each consecutive interruption doubles the wait, up to
1 s, and the next connection taken resets it. `HttpAcceptRetryMs` is `int(0 to 1000)`, that wait.

`serve` returns an `HttpServeEnd` naming why it stopped:

| Case | Meaning |
|------|---------|
| `closed` | `close()` was called |
| `listenerFailed` | The listener failed (`acceptFailed`) |
| `acceptDeadlinePassed` | The accept deadline passed (`acceptTimedOut`) |

### Requests

A request line is `METHOD target HTTP/x.y`, its three parts separated by single spaces. The method is one
of `HttpMethod`'s, in upper case. The target takes one of three forms:

- origin-form, `/path?query`;
- absolute-form, `http://host:port/path?query`, the scheme in any case. The URL's host and port come from
  the target, and an HTTP/1.1 request carries a `Host` header beside it as well;
- `*`, with `OPTIONS` alone. `accept()` answers `OPTIONS *` itself with `200`, an `Allow` header listing
  every `HttpMethod` and an empty body, and goes on to the next connection.

A path and query hold RFC 3986 characters and percent escapes, and a host is checked as `URL.parse` checks
one. An HTTP/1.1 request carries a `Host` header; an HTTP/1.0 request may leave it out. Up to four empty
lines ahead of the request line are skipped. Header lines follow [Header parsing](#header-parsing).

### HttpExchange

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `request()` | `HttpRequest` | — | The request that arrived, with its body. For an origin-form target `url()` is built as `http://<Host><target>`, so `path()` and `query()` read the target; an HTTP/1.0 request that sent no `Host` has a hostless `url()`, whose `host()` throws `fieldNotPresent`. For an absolute-form target `url()` is read from the target. |
| `respond(response HttpResponse)` | — | `HttpServerError` | Write the response and close the connection. Throws `alreadyResponded` on a second answer, `invalidResponse` for a response the rules below refuse, and `sendFailed` when the write fails or outlasts `writeDeadlineMs`. |
| `respondWith(code StatusCode, body String, contentType String)` | — | `HttpServerError` | Answer a status, a body and its content type, as `respond` does. |

`respond` and `respondWith` record that the exchange is answered, so an exchange from `accept()` is held in
a `var` to answer it; through a `let` it is [E3159](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3159).
A handler answers the `exchange` parameter `serve` hands it directly.

Every response carries `Connection: close`, and a `Content-Length` computed from its body whenever its
status allows a body: every status but 204 and 304, which end at their headers. The server writes both
headers itself, in place of any `Connection` or `Content-Length` the handler set. `respond` throws
`invalidResponse` for a 204 or 304 given a body, a header name that is an invalid HTTP token, or a reason
phrase or header value holding a control character other than tab. The answer to a `HEAD` request is the
head alone, its `Content-Length` the length of the body it would have carried.

When the peer sent bytes past the request, beyond its `Content-Length`, the server answers, shuts its
write side, and reads and discards what the peer still sends — for up to 500 ms or 256 KiB — before it
closes. Closing with those bytes unread would reset the connection, which can destroy the answer before
the peer reads it. A refused request's answer closes the same way, unless the peer has already gone.

### HttpHandler

The interface `serve` calls for each exchange.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `handle(exchange HttpExchange)` | — | `HttpServerError` | Answer one exchange. |
| `faulted(fault HttpHandlerFault)` | — | — | Told of each exchange `serve` answered in the handler's place, and of each interrupted accept. |

`HttpHandlerFault` says what happened. Every case but the last carries the `request`, and the `threw`
cases the `fault` the handler threw.

| Case | Meaning |
|------|---------|
| `threw(request, fault)` | The handler threw before answering; the exchange was answered `500` |
| `threwAfterResponding(request, fault)` | The handler threw after its answer was sent |
| `threwAfterAFailedResponse(request, fault)` | The handler threw after its own answer failed to send |
| `threwWithoutAnyAnswer(request, fault)` | The handler threw before answering, and the `500` failed to send too |
| `returnedWithoutResponding(request)` | The handler returned without answering; the exchange was answered `500` |
| `returnedWithoutAnyAnswer(request)` | The handler returned without answering, and the `500` failed to send too |
| `acceptInterrupted(retryMs)` | The OS refused one connection; `serve` accepts again in `retryMs` milliseconds |

`describe()` renders a fault as one line, such as `POST /items failed with invalidResponse and was
answered 500`.

### HttpServerLimits

`HttpServerLimits.create()` gives `maxHeaderBytes` 16384, `maxBodyBytes` 1048576, and `readDeadlineMs`
and `writeDeadlineMs` 10000 each. All four are public fields.

| Member | Type | Description |
|--------|------|-------------|
| `HttpServerLimits.create()` | `HttpServerLimits` | The defaults above. |
| `maxHeaderBytes` | `HttpByteCap` | How much of a header block is read before `headersTooLarge`. |
| `maxBodyBytes` | `HttpByteCap` | The largest `Content-Length` accepted. |
| `readDeadlineMs` | `SocketDeadlineMs` | How long a request may take to arrive whole, from the moment its connection is taken, before `readTimedOut`. |
| `writeDeadlineMs` | `SocketDeadlineMs` | How long each answer may take to send before the send fails. |

`HttpByteCap` is `int(0 to 1099511627776)`, 1 TiB.

### Protocol errors

A request `accept()` refuses is answered on its connection with the status below and an empty body before
its error is thrown, so the peer hears why.

| Condition | Status | `HttpServerError` |
|-----------|--------|-------------------|
| A request line of other than three parts, a method or target that breaks its grammar, a version spelled other than `HTTP/<digit>.<digit>`, a malformed header line, an HTTP/1.1 request without `Host`, a repeated `Host`, a `Content-Length` other than a run of digits, or one that repeats with a different value, or `*` with a method other than `OPTIONS` | 400 | `malformedRequest` |
| The connection closed or failed before the request was whole | 400, when it can still be sent | `connectionClosed` |
| A header block longer than `maxHeaderBytes` | 431 | `headersTooLarge` |
| `Content-Length` above `maxBodyBytes`, checked before the body is read | 413 | `bodyTooLarge` |
| `Transfer-Encoding` present | 501 | `unsupportedTransferEncoding` |
| A method outside `HttpMethod` | 501 | `unsupportedMethod` |
| An HTTP major version other than 1 | 505 | `unsupportedVersion` |
| The request incomplete when `readDeadlineMs` has passed | 408 | `readTimedOut` |

`accept()` also throws `listenerClosed`, `acceptFailed`, `acceptTimedOut` and `acceptInterrupted`, which
concern the listener alone. `respond` throws `alreadyResponded`, `invalidResponse` and
`sendFailed`.

```maxon
type HelloHandler implements HttpHandler
	var greeting as String

	static function create(greeting String) returns HelloHandler
		return HelloHandler{greeting: greeting}
	end 'create'

	function handle(exchange HttpExchange) throws HttpServerError
		try exchange.respondWith(StatusCode.ok, body: greeting, contentType: "text/plain")
	end 'handle'

	function faulted(fault HttpHandlerFault)
		printError("{fault.describe()}\n")
	end 'faulted'
end 'HelloHandler'

function main() returns ExitCode
	let server = try HttpServer.bind("127.0.0.1", port: 8080) otherwise return 1
	_ = server.serve(HelloHandler.create("hello\n"))

	return 0
end 'main'
```

## URL

`URL` parses, prints and resolves URI references following RFC 3986. It implements `Equatable` and
`Stringable`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `URL.parse(input String)` | `URL` | `URLError` | Parse an absolute URL or a relative reference. |
| `URL.resolve(base URL, reference String)` | `URL` | `URLError` | Resolve `reference` against `base` (RFC 3986 section 5). |
| `scheme()` | `String` | — | `""` for a relative reference. |
| `path()` | `String` | — | Always present, possibly empty. |
| `host()` | `String` | `URLError.fieldNotPresent` | |
| `port()` | `int(0 to 65535)` | `URLError.fieldNotPresent` | |
| `userinfo()` | `String` | `URLError.fieldNotPresent` | The part before `@`. |
| `query()` | `String` | `URLError.fieldNotPresent` | Without the `?`. |
| `fragment()` | `String` | `URLError.fieldNotPresent` | Without the `#`. |
| `toString()` | `String` | — | The serialized URL. |
| `equals(other URL)` | `bool` | — | Component-wise equality. |

| URLError | Meaning |
|----------|---------|
| `emptyInput` | The input is empty or only whitespace |
| `invalidScheme` | The scheme does not start with a letter or has invalid characters |
| `invalidHost` | A host outside RFC 3986's grammar: a character a host cannot hold (a space, say), an empty host before `:port`, an IP literal that is unclosed or an invalid IPv6 or IPvFuture address, or text between `]` and the port |
| `invalidPort` | A port holding a non-digit, or above 65535 |
| `invalidEncoding` | Malformed percent-encoding, such as `%GG` |
| `relativeWithoutBase` | `resolve` was given a schemeless base |
| `fieldNotPresent` | An accessor was called for a component the URL lacks |
| `invalidPath` | Raised while `HttpServer` reads a request target, for a path that must open with `/` and opens with something else |

```maxon
function main() returns ExitCode
	let url = try URL.parse("https://user@example.com:8080/a/b?q=1#top") otherwise return 1
	let host = try url.host() otherwise ""
	let port = try url.port() otherwise 0
	let query = try url.query() otherwise ""
	print("{url.scheme()} {host} {port} {url.path()} {query} {url}\n")

	let base = try URL.parse("http://a/b/c/d?q") otherwise return 1
	let resolved = try URL.resolve(base, reference: "../g") otherwise return 1
	let mail = try URL.parse("mailto:someone@example.com") otherwise return 1
	let mailHost = try mail.host() otherwise "no host"
	print("{resolved} {mailHost}\n")
	return 0
end 'main'
```

Output: `https example.com 8080 /a/b q=1 https://user@example.com:8080/a/b?q=1#top` and `http://a/b/g no host`.

## Json

An RFC 8259 JSON parser and serializer. A `JsonDoc` owns a flat arena of `JsonNode`s; an array or object
refers to its children by `JsonNodeId`. Walk a document through the `JsonDoc` accessors.

### Json

| Method | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Json.parse(text String)` | `JsonDoc` | `JsonError` | Parse a document; `doc.root` is the top-level value. A number reads as the float nearest its decimal value; one too large for a float is `invalidNumber`, and one too small reads as zero. |
| `Json.stringify(doc JsonDoc)` | `String` | — | Compact output of the tree at `doc.root`. NaN and infinities are written as `null`. |
| `Json.stringifyPrettyNode(doc JsonDoc, root JsonNodeId)` | `String` | — | Indented output (two spaces per level) of the subtree at `root`. |
| `Json.quote(text String)` | `String` | — | `text` as one quoted JSON string value, with `"`, `\` and control characters escaped. |

### JsonDoc

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `JsonDoc.create()` | `JsonDoc` | — | An empty document. |
| `nodes` | field, `Array with JsonNode` | — | The arena. |
| `root` | field, `JsonNodeId` | — | The top-level node's id, set by a parse. For an array or object it is the last node added, because children are added before their parent. Assign it when building. |
| `add(node JsonNode)` | `JsonNodeId` | — | Append a node and return its id. |
| `get(id JsonNodeId)` | `JsonNode` | — | The node; an id outside the arena panics. |
| `rootKind()` | `JsonKind` | — | The kind of the root node. |
| `getChild(parent JsonNodeId, key String)` | `JsonNodeId` | `JsonAccessError` | An object member's id: `notObject` or `missingKey`. |
| `hasChild(parent JsonNodeId, key String)` | `bool` | — | Whether `parent` is an object holding `key`; `false` for any other kind of node. |
| `getString(parent, key:)` | `String` | `JsonAccessError` | A string member; `wrongType` for another kind. |
| `getInt(parent, key:)` | `int(i64.min to i64.max)` | `JsonAccessError` | A number member, truncated toward zero. |
| `getBool(parent, key:)` | `bool` | `JsonAccessError` | A boolean member. |
| `arrayLength(id JsonNodeId)` | `Count` | `JsonAccessError` | `notArray` for another kind. |
| `arrayAt(id JsonNodeId, index)` | `JsonNodeId` | `JsonAccessError` | `outOfBounds` past the end. |

### JsonNode

| Member | Description |
|--------|-------------|
| `kind` | `JsonKind` |
| `boolValue` | Set for `jsonBool` |
| `numberValue` | Set for `jsonNumber` (a `JsonFloat`) |
| `stringValue` | Set for `jsonString` |
| `children` | `JsonNodeIdArray`, for `jsonArray` and `jsonObject` |
| `keys` | `StringArray`, parallel to `children`, for `jsonObject` |
| `JsonNode.nullNode()` | A `null` |
| `JsonNode.boolNode(value bool)` | A boolean |
| `JsonNode.numberNode(value JsonFloat)` | A number |
| `JsonNode.stringNode(value String)` | A string |
| `JsonNode.arrayNode(children JsonNodeIdArray)` | An array of already-added nodes |
| `JsonNode.objectNode(keys StringArray, children JsonNodeIdArray)` | An object; `keys[i]` names `children[i]` |

### Kinds and errors

| Enum | Cases |
|------|-------|
| `JsonKind` | `jsonNull`, `jsonBool`, `jsonNumber`, `jsonString`, `jsonArray`, `jsonObject` |
| `JsonError` (from `parse`) | `unexpectedChar`, `unexpectedEof`, `invalidEscape`, `invalidNumber`, `invalidSurrogate`, `trailingContent` |
| `JsonAccessError` (from accessors) | `notObject`, `notArray`, `missingKey`, `wrongType`, `outOfBounds` |

`JsonNodeId` is `int(0 to u64.max)` and `JsonNodeIdArray` is `Array with JsonNodeId`.

```maxon
function main() returns ExitCode
	let doc = try Json.parse("\{\"name\": \"maxon\", \"stars\": 42, \"tags\": [\"a\", \"b\"]\}") otherwise (e) 'bad'
		print("invalid JSON: {e}\n")
		return 1
	end 'bad'

	let name = try doc.getString(doc.root, key: "name") otherwise "?"
	let stars = try doc.getInt(doc.root, key: "stars") otherwise 0
	let tags = try doc.getChild(doc.root, key: "tags") otherwise 0
	let second = try doc.arrayAt(tags, index: 1) otherwise 0
	print("{doc.rootKind()} {name} {stars} {doc.get(second).stringValue}\n")

	var built = JsonDoc.create()
	var keys = StringArray.create()
	var children = JsonNodeIdArray.create()
	keys.push("ok")
	children.push(built.add(JsonNode.boolNode(true)))
	keys.push("score")
	children.push(built.add(JsonNode.numberNode(1.5)))
	built.root = built.add(JsonNode.objectNode(keys, children: children))
	print("{Json.stringify(built)}\n")
	return 0
end 'main'
```

Output: `jsonObject maxon 42 b` and `{"ok":true,"score":1.5}`.

## Base64

`Base64` converts between bytes and base64 text with the standard alphabet and `=` padding (RFC 4648
section 4).

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Base64.encode(data ByteArray)` | `String` | — | The canonical encoding: padded to a multiple of 4 characters, on one line, with the unused bits of the last symbol zero. |
| `Base64.decode(text String)` | `ByteArray` | `Base64Error` | The bytes `text` encodes. It accepts exactly the strings `encode` produces. |

Decoding is strict, and its checks run in a fixed order, so every input has exactly one error:

| `Base64Error` case | When |
|--------------------|------|
| `invalidSymbol` | A character outside the alphabet, whitespace and line breaks included. |
| `invalidLength` | A length other than a multiple of 4, unpadded input included. |
| `invalidPadding` | `=` anywhere but as one or two final characters, or non-zero pad bits in the last symbol. |

```maxon
function main() returns ExitCode
	let text = Base64.encode("Maxon".toByteArray())
	let back = try Base64.decode(text) otherwise return 1
	print("{text} {String.from(back)}\n")

	let unpadded = try Base64.decode("TWF4b24") otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	print("{unpadded.count()}\n")
	return 1
end 'main'
```

Output: `TWF4b24= Maxon` and `invalidLength`.

## Pem

`Pem` reads and writes PEM text (RFC 7468): the `-----BEGIN CERTIFICATE-----` blocks that carry
certificates and keys, each a label and a base64 body.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Pem.decode(text String)` | `PemBlocks` | `PemError` | Every block in `text`, in order, duplicate labels kept. A text with no blocks gives an empty list. |
| `Pem.encode(label String, data ByteArray)` | `String` | `PemError` | One block: the BEGIN line, the base64 of `data` in lines of 64 characters, and the END line, each ending in LF. |

A `PemBlock` has `label()`, the text between `-----BEGIN ` and `-----` (such as `CERTIFICATE`), and
`bytes()`, the decoded body. `PemBlocks` is `Array with PemBlock`.

`decode` accepts LF and CRLF line endings and a last line with no line break. Text before, between and
after blocks is skipped, including a stray END line and a malformed BEGIN line. Trailing spaces and tabs
after a boundary's `-----` are allowed, and whitespace anywhere in a body is removed before the body is
decoded with `Base64.decode`.

| `PemError` case | When |
|-----------------|------|
| `unterminatedBlock` | A BEGIN line with no END line before the next BEGIN line or the end of the text. |
| `labelMismatch` | A line inside a block that starts with `-----END ` and names anything but the block's exact label. |
| `invalidBody` | A body that fails strict base64 decoding once whitespace is removed. |
| `invalidLabel` | `encode` only: a label RFC 7468 refuses. A label is empty, or printable ASCII characters joined by single spaces or hyphens. |

```maxon
function main() returns ExitCode
	let text = try Pem.encode("PUBLIC KEY", data: "key bytes".toByteArray()) otherwise return 1
	print(text)

	let blocks = try Pem.decode("a comment line\n{text}") otherwise return 1

	for block in blocks 'each'
		print("{block.label()}: {String.from(block.bytes())}\n")
	end 'each'

	return 0
end 'main'
```

Output:

```
-----BEGIN PUBLIC KEY-----
a2V5IGJ5dGVz
-----END PUBLIC KEY-----
PUBLIC KEY: key bytes
```

## Der

`DerReader` reads DER, the strict binary encoding of ASN.1 (ITU-T X.690 section 10) that certificates and
keys use, one element at a time. Each read consumes the next element, checks its tag, and checks it against
the DER rules; a constructed element is entered as a new reader over its content.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `DerReader.create(bytes ByteArray)` | `DerReader` | — | A reader positioned at the first element of `bytes`. |
| `isAtEnd()` | `bool` | — | Whether every element has been read. |
| `finish()` | — | `DerError` | `trailingData` unless every element has been read. |
| `peekHeader()` | `DerHeader` | `DerError` | The next element's header, leaving the element unread. |
| `enterSequence()` | `DerReader` | `DerError` | A reader over the content of the next element, a SEQUENCE. |
| `enterSet()` | `DerReader` | `DerError` | A reader over the content of the next element, a SET. The order of a SET OF is not checked. |
| `enterExplicit(tagNumber DerTagNumber)` | `DerReader` | `DerError` | A reader over the content of the next element, a constructed context-specific `[tagNumber]`, the explicit tag X.509 writes. |
| `readInteger()` | `ByteArray` | `DerError` | A non-negative INTEGER's big-endian magnitude, without its sign octet. Zero is one zero octet. |
| `readObjectIdentifier()` | `ByteArray` | `DerError` | An OBJECT IDENTIFIER's content octets, such as `2a864886f70d010101` for `rsaEncryption`. |
| `readNull()` | — | `DerError` | Consumes a NULL. |
| `readBitString()` | `DerBitString` | `DerError` | A primitive BIT STRING. |
| `readOctetString()` | `ByteArray` | `DerError` | A primitive OCTET STRING's content. |
| `readUtcTime()` | `UnixSeconds` | `DerError` | A UTCTime `YYMMDDHHMMSSZ`. Years `50` to `99` are 1950 to 1999 and `00` to `49` are 2000 to 2049 (RFC 5280 section 4.1.2.5.1). |
| `readGeneralizedTime()` | `UnixSeconds` | `DerError` | A GeneralizedTime `YYYYMMDDHHMMSSZ`. Fractional seconds, which RFC 5280 forbids, are refused. |
| `readSignedInteger()` | `ByteArray` | `DerError` | An INTEGER of either sign, as its content octets: minimal big-endian two's complement. |
| `readBoolean()` | `bool` | `DerError` | A BOOLEAN, whose one content octet is `00` or `ff`. |
| `readString()` | `String` | `DerError` | A UTF8String, PrintableString, IA5String, TeletexString (each octet one ISO 8859-1 character), BMPString (UTF-16BE) or UniversalString (UTF-32BE), decoded. |
| `readElement()` | `ByteArray` | `DerError` | The next element, whatever its tag, as its whole encoding: identifier, length and content octets. |
| `readImplicit(tagNumber DerTagNumber)` | `ByteArray` | `DerError` | The content of a primitive context-specific `[tagNumber]`, the implicit tag X.509 writes on a key identifier or a dNSName. |
| `readImplicitBitString(tagNumber DerTagNumber)` | `DerBitString` | `DerError` | A primitive context-specific `[tagNumber]` holding an implicitly tagged BIT STRING. |
| `readImplicitSignedInteger(tagNumber DerTagNumber)` | `ByteArray` | `DerError` | A primitive context-specific `[tagNumber]` holding an implicitly tagged INTEGER, as `readSignedInteger` answers it. |

A `DerHeader` has `tagClass()`, a `DerTagClass` (`universal`, `application`, `contextSpecific` or
`private`), `isConstructed()`, `tagNumber()` and `length()`, the number of content octets. A `DerBitString`
has `bytes()`, the octets after the unused-bits octet, and `unusedBits()`, how many low bits of the last
octet are not part of the string.

| Name | Definition |
|------|------------|
| `DerTagNumber` | `int(0 to u32.max)` |
| `DerLength` | `int(0 to u64.max)`, implementing `Count` |
| `DerUnusedBits` | `int(0 to 7)` |

| `DerError` case | When |
|-----------------|------|
| `truncated` | The input ends inside a header, or an element's content runs past the input or past the element holding it. |
| `indefiniteLength` | The length octet `0x80`, BER's indefinite form. |
| `nonMinimalLength` | A long-form length the short form or fewer octets could carry. |
| `nonMinimalTagNumber` | A high-tag-number form holding a number below 31, or one with a leading `0x80` octet. |
| `tagNumberTooLarge` | A tag number above `DerTagNumber`. |
| `unexpectedTag` | The element is not the type the read asked for, the constructed form of a primitive type included. |
| `trailingData` | `finish` found an element left. |
| `emptyInteger` | An INTEGER with no content octets. |
| `nonMinimalInteger` | An INTEGER with a redundant leading `00` or `ff` octet. |
| `negativeInteger` | An INTEGER whose sign bit is set. |
| `invalidNull` | A NULL with content. |
| `invalidBitString` | A BIT STRING with no unused-bits octet, more than 7 unused bits, unused bits and no data, or a set unused bit. |
| `invalidObjectIdentifier` | An empty OBJECT IDENTIFIER, a subidentifier with a leading `0x80` octet, or a last octet with its continuation bit set. |
| `invalidTime` | A time not in the form above, or a date or clock time that does not exist. |
| `invalidBoolean` | A BOOLEAN that is not one octet of `00` or `ff`. |
| `invalidString` | Text outside its string type: malformed UTF-8, a PrintableString character outside `A-Z a-z 0-9`, space and `' ( ) + , - . / : = ?`, an IA5String octet above `7f`, a BMPString or UniversalString of partial units, or a surrogate or a value above U+10FFFF. |

```maxon
function hex(bytes ByteArray) returns String
	var text = ""

	for b in bytes 'each'
		text.append("{b:02x}")
	end 'each'

	return text
end 'hex'

function main() returns ExitCode
	var outer = DerReader.create(ByteArray from [0x30, 0x06, 0x02, 0x01, 0x05, 0x02, 0x01, 0x80])
	var numbers = try outer.enterSequence() otherwise return 1
	let first = try numbers.readInteger() otherwise return 1
	print("{hex(first)} {numbers.isAtEnd()}\n")

	_ = try numbers.readInteger() otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `05 false` and `negativeInteger`.

## Sha2

The SHA-2 hash functions of FIPS 180-4. `sha256`, `sha384` and `sha512` hash a whole `ByteArray`; a
`Digest` takes the message in chunks.

| Function | Returns | Description |
|----------|---------|-------------|
| `sha256(data ByteArray)` | `ByteArray` | The 32-byte SHA-256 digest. |
| `sha384(data ByteArray)` | `ByteArray` | The 48-byte SHA-384 digest. |
| `sha512(data ByteArray)` | `ByteArray` | The 64-byte SHA-512 digest. |

`HashAlgorithm` names one of them — `sha256`, `sha384` or `sha512` — for `Digest`, [`hmac`](#hmac) and
the [HKDF](#hkdf) functions.

| Member | Returns | Description |
|--------|---------|-------------|
| `blockBytes()` | `HashBlockBytes` | The block the function compresses: 64 bytes for `sha256`, 128 for `sha384` and `sha512`. |
| `digestBytes()` | `HashDigestBytes` | The digest size: 32, 48 or 64 bytes. |

| Name | Definition |
|------|------------|
| `HashBlockBytes` | `int(64 to 128)`, implementing `Count` |
| `HashDigestBytes` | `int(32 to 64)`, implementing `Count` |

A `Digest` hashes a message fed in chunks. The digest depends only on the bytes passed to `update`, so
every way of splitting a message gives its one-call digest.

| Member | Returns | Description |
|--------|---------|-------------|
| `Digest.create(algorithm HashAlgorithm)` | `Digest` | A new hash of the empty message, sharing nothing with any other. |
| `update(data ByteArray)` | — | Feed `data` into the hash. An empty `data` leaves it unchanged. |
| `finalize()` | `ByteArray` | The digest of every byte passed to `update`, in order. |

`finalize` ends a `Digest`: a later `update` or `finalize` on it panics.

```maxon
function hex(bytes ByteArray) returns String
	var text = ""

	for b in bytes 'each'
		text.append("{b:02x}")
	end 'each'

	return text
end 'hex'

function main() returns ExitCode
	var digest = Digest.create(HashAlgorithm.sha256)
	digest.update("a".toByteArray())
	digest.update("bc".toByteArray())

	print("{hex(digest.finalize())}\n")
	print("{sha384("abc".toByteArray()).count()} {sha512("abc".toByteArray()).count()}\n")
	return 0
end 'main'
```

Output: `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`, the SHA-256 digest of `abc`,
and `48 64`.

## Hmac

`hmac` computes a message authentication code (RFC 2104, FIPS 198-1) over a SHA-2 function, and
`constantTimeEqual` checks a received code against the computed one.

| Function | Returns | Description |
|----------|---------|-------------|
| `hmac(algorithm HashAlgorithm, key ByteArray, message ByteArray)` | `ByteArray` | The full-length HMAC of `message` under `key`: `algorithm.digestBytes()` bytes. A key of any length is accepted; one longer than `algorithm.blockBytes()` is hashed first. |
| `constantTimeEqual(a ByteArray, b ByteArray)` | `bool` | Whether `a` and `b` hold the same bytes. For equal lengths every byte pair is compared, so the running time reveals the length and nothing else. |

Compare codes with `constantTimeEqual`: a comparison that stops at the first differing byte tells an
attacker how much of a forged code was right.

```maxon
function main() returns ExitCode
	let key = "Jefe".toByteArray()
	let tag = hmac(HashAlgorithm.sha256, key: key, message: "what do ya want for nothing?".toByteArray())
	let received = hmac(HashAlgorithm.sha256, key: key, message: "what do ya want for nothing?".toByteArray())
	var hex = ""

	for b in tag 'each'
		hex.append("{b:02x}")
	end 'each'

	print("{hex} {constantTimeEqual(tag, b: received)}\n")
	return 0
end 'main'
```

Output: `5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843 true`.

## Hkdf

HKDF (RFC 5869) derives keys from secret input: `hkdfExtract` concentrates it into a pseudorandom key,
and `hkdfExpand` stretches that key into the bytes asked for, bound to an `info` string. `hkdf` does
both.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `hkdfExtract(algorithm HashAlgorithm, salt ByteArray, inputKeyMaterial ByteArray)` | `ByteArray` | — | The pseudorandom key: the HMAC of `inputKeyMaterial` keyed by `salt`. An empty salt gives the same key as `algorithm.digestBytes()` zero bytes. |
| `hkdfExpand(algorithm HashAlgorithm, pseudoRandomKey ByteArray, info ByteArray, length HkdfOutputLength)` | `ByteArray` | `HkdfError` | `length` bytes of key material. A shorter output is a prefix of a longer one from the same inputs. |
| `hkdf(algorithm HashAlgorithm, salt ByteArray, inputKeyMaterial ByteArray, info ByteArray, length HkdfOutputLength)` | `ByteArray` | `HkdfError` | `hkdfExpand` of `hkdfExtract`, with `length` checked before anything is derived. |

`length` reaches 255 times `algorithm.digestBytes()`: 8160 bytes for SHA-256, 12240 for SHA-384 and
16320 for SHA-512. A longer one throws `HkdfError.outputTooLong`, the one case of `HkdfError`.
`HkdfOutputLength` is `int(0 to 16321)`, implementing `Count`.

```maxon
function main() returns ExitCode
	let secret = "shared secret".toByteArray()
	let key = try hkdf(HashAlgorithm.sha256, salt: ByteArray.create(), inputKeyMaterial: secret, info: "client key".toByteArray(), length: 16) otherwise return 1
	print("{key.count()}\n")

	_ = try hkdf(HashAlgorithm.sha256, salt: ByteArray.create(), inputKeyMaterial: secret, info: ByteArray.create(), length: 8161) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `16` and `outputTooLong`.

## Rsa

`Rsa` verifies RSA signatures under RSASSA-PKCS1-v1_5 and RSASSA-PSS, and makes RSASSA-PSS signatures
(RFC 8017), over SHA-256, SHA-384 or SHA-512. PSS uses MGF1 over the same hash and a salt as long as the
digest, the parameters of TLS 1.3's `rsa_pss_rsae_*` schemes.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Rsa.verifyPkcs1v15(publicKey ByteArray, hash HashAlgorithm, message ByteArray, signature ByteArray)` | `bool` | `RsaError` | Whether `signature` is the PKCS #1 v1.5 signature of `message`. The expected encoded block is built and compared whole, so no lenient parse of the signature's block can accept a forgery. |
| `Rsa.verifyPss(publicKey ByteArray, hash HashAlgorithm, message ByteArray, signature ByteArray)` | `bool` | `RsaError` | Whether `signature` is the PSS signature of `message`. A signature made with another salt length is `false`. |
| `Rsa.signPss(privateKey ByteArray, hash HashAlgorithm, message ByteArray, salt ByteArray)` | `ByteArray` | `RsaError` | The PSS signature of `message`, exactly as long as the modulus. Pass `hash.digestBytes()` fresh bytes from `Random.bytes` as `salt` for every signature. |

A public key is a DER SubjectPublicKeyInfo (RFC 5280), the form a certificate carries: `rsaEncryption` with
NULL parameters, a modulus of 2048 to 4096 bits that is odd, and an odd exponent `e` with `3 <= e < n`. A
private key is a DER PKCS #8 `PrivateKeyInfo` without attributes, holding a two-prime `RSAPrivateKey` with
the same modulus and exponent rules whose values agree: `p * q == n`, `e * d == 1` modulo `p - 1` and
`q - 1`, `dP` and `dQ` are `d` reduced modulo `p - 1` and `q - 1`, and `qInv * q == 1` modulo `p`. PEM keys
are decoded with [`Pem`](#pem) first.

The checks run in order and the first that fails is thrown:

| `RsaError` case | When |
|-----------------|------|
| `invalidPublicKey` | The public key breaks any rule above, or is not strict DER. |
| `invalidPrivateKey` | The private key breaks any rule above, or is not strict DER. |
| `invalidSignatureLength` | Verification only: the signature is not exactly as long as the modulus. |
| `invalidSaltLength` | `signPss` only: the salt is not exactly `hash.digestBytes()` long. |

Every other wrong signature, including one whose value is not below the modulus, verifies as `false`.

`signPss` takes time independent of the private key: the exponentiation walks the exponent in fixed
4-bit windows, picks each table entry by masking every entry, and never branches or indexes memory on a
secret value. The input is blinded by a random factor drawn from `Random.bytes`, and the signature is
checked with the public exponent before it is returned. When the operating system cannot supply random
bytes, `signPss` panics.

```maxon
function main() returns ExitCode
	let message = "hello".toByteArray()

	_ = try Rsa.verifyPss(ByteArray.create(), hash: HashAlgorithm.sha256, message: message, signature: ByteArray.create()) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `invalidPublicKey`.

## Aead

`Aead` is authenticated encryption with associated data: `seal` encrypts a message and appends a 16-byte
tag over it and the associated data, and `open` checks the tag before decrypting. [`AesGcm`](#aesgcm) and
[`ChaCha20Poly1305`](#chacha20poly1305) implement it. Every key, nonce and message is a `ByteArray`, and
the work takes the same time whatever the key and the data hold.

| Requirement | Returns | Throws | Description |
|-------------|---------|--------|-------------|
| `seal(nonce ByteArray, plaintext ByteArray, additionalData ByteArray)` | `ByteArray` | `AeadError` | The ciphertext of `plaintext`, as long as it, followed by the tag. `additionalData` is authenticated and not encrypted. |
| `open(nonce ByteArray, sealed ByteArray, additionalData ByteArray)` | `ByteArray` | `AeadError` | The plaintext of what `seal` returned, given the same nonce and associated data. |

`AeadError` has four cases: `invalidKeyLength` and `invalidNonceLength` for a key or a 12-byte nonce of
the wrong size, `plaintextTooLong` from `seal` past the cipher's limit, and `authenticationFailed` from
`open` when the tag does not match or `sealed` is shorter than a tag. `open` returns no plaintext unless
the tag matches.

A nonce must never be used twice under one key: two messages sealed under one key and nonce reveal the
XOR of their plaintexts, and let an attacker forge tags.

```maxon
function main() returns ExitCode
	var key = ByteArray.create()
	key.resize(32)
	var nonce = ByteArray.create()
	nonce.resize(12)

	let aead = try ChaCha20Poly1305.create(key) otherwise return 1
	let sealed = try aead.seal(nonce, plaintext: "hello".toByteArray(), additionalData: "v1".toByteArray()) otherwise return 1
	let opened = try aead.open(nonce, sealed: sealed, additionalData: "v1".toByteArray()) otherwise return 1
	print("{sealed.count()} {opened.count()}\n")

	_ = try aead.open(nonce, sealed: sealed, additionalData: "v2".toByteArray()) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	return 1
end 'main'
```

Output: `21 5` and `authenticationFailed`.

## AesGcm

AES-GCM (NIST SP 800-38D) with a 12-byte nonce and a 16-byte tag, implementing [`Aead`](#aead).
`AesGcm.create(key ByteArray)` takes a 16-, 24- or 32-byte key and throws `AeadError.invalidKeyLength`
for any other. `seal` accepts up to 2^36 − 32 bytes of plaintext, the most its 32-bit block counter
covers.

## ChaCha20Poly1305

ChaCha20-Poly1305 (RFC 8439) with a 12-byte nonce and a 16-byte tag, implementing [`Aead`](#aead).
`ChaCha20Poly1305.create(key ByteArray)` takes a 32-byte key, keeping its own copy, and throws
`AeadError.invalidKeyLength` for any other. `seal` accepts up to 2^38 − 64 bytes of plaintext.

## Aes

The AES block cipher (FIPS 197), the primitive under [`AesGcm`](#aesgcm). It encrypts one 16-byte block
at a time, or a message in counter mode. The key schedule is computed once by `Aes.create`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Aes.create(key ByteArray)` | `Aes` | `AesError` | The cipher for a 16-, 24- or 32-byte key: AES-128, AES-192 or AES-256. |
| `encryptBlock(block ByteArray)` | `ByteArray` | `AesError` | The encryption of one 16-byte block. |
| `decryptBlock(block ByteArray)` | `ByteArray` | `AesError` | The decryption of one 16-byte block. |
| `xorKeyStream(nonce ByteArray, counter AesCounter, data ByteArray)` | `ByteArray` | `AesKeyStreamError` | `data` XORed with the counter-mode key stream: each block's input is the 12-byte nonce followed by a 32-bit big-endian counter, `counter` for the first block. Encrypting and decrypting are the same call. |

`AesError` is `invalidKeyLength` or `invalidBlockLength`. `AesKeyStreamError` is `invalidNonceLength`, or
`counterOverflow` when `data` needs a block counter past 2^32 − 1. `AesCounter` is `int(0 to u32.max)`.

A bare block cipher is not authenticated: use [`AesGcm`](#aesgcm) to encrypt messages.

## ChaCha20

The ChaCha20 stream cipher (RFC 8439), the primitive under [`ChaCha20Poly1305`](#chacha20poly1305).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `ChaCha20.xorKeyStream(key ByteArray, nonce ByteArray, counter ChaCha20Counter, data ByteArray)` | `ByteArray` | `ChaCha20Error` | `data` XORed with the key stream of a 32-byte key and a 12-byte nonce, starting at 64-byte block `counter`. Encrypting and decrypting are the same call. |

`ChaCha20Error` is `invalidKeyLength`, `invalidNonceLength`, or `counterOverflow` when `data` needs a block
counter past 2^32 − 1. `ChaCha20Counter` is `int(0 to u32.max)`.

## Poly1305

The Poly1305 one-time authenticator (RFC 8439), the tag under [`ChaCha20Poly1305`](#chacha20poly1305).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `Poly1305.mac(key ByteArray, message ByteArray)` | `ByteArray` | `Poly1305Error` | The 16-byte tag of `message` under a 32-byte key. |

`Poly1305Error` has one case, `invalidKeyLength`. A key tags exactly one message: two tags under one key
let an attacker forge a third. Compare tags with [`constantTimeEqual`](#hmac).

## X25519

X25519 Diffie-Hellman key agreement (RFC 7748). Keys and the shared secret are 32 bytes, little-endian.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `X25519.publicKey(privateKey ByteArray)` | `ByteArray` | `X25519Error` | The public key of a 32-byte private key: the clamped key times the base point `u = 9`. |
| `X25519.sharedSecret(privateKey ByteArray, peerPublicKey ByteArray)` | `ByteArray` | `X25519Error` | The secret shared by a private key and a peer's public key. |

`X25519Error` is `invalidLength` when a key is not exactly 32 bytes, or `allZeroSharedSecret` when the
secret is 32 zero bytes, which a low-order public key forces (TLS 1.3 requires the check). The private key
is clamped, bit 255 of the public key is ignored, a public key at or above 2^255 − 19 is reduced, and a
public key on the curve's twist is accepted. Both functions run in time independent of the private key.
Derive keys from the shared secret with [`hkdfExtract`](#hkdf), never use it directly.

## Ed25519

Ed25519 signature verification (RFC 8032).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `Ed25519.verify(publicKey ByteArray, message ByteArray, signature ByteArray)` | `bool` | `Ed25519Error` | Whether a 64-byte signature of `message` is valid under a 32-byte public key. |

`Ed25519Error` has one case, `invalidLength`, for a public key that is not 32 bytes or a signature that
is not 64. Any other input is answered with a `bool`: the check is the cofactorless `[S]B = R + [k]A`, with
`k` the SHA-512 digest of `R`, the public key and the message reduced mod `L`. A public key that does not
decode, and an `S` at or above `L`, are `false`. Verification handles public data only and runs in
variable time.

## EcdhP256

Elliptic-curve Diffie-Hellman over NIST P-256 (secp256r1): the ECC CDH primitive of SP 800-56A with
cofactor 1, as the TLS 1.3 `secp256r1` key share uses it.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `EcdhP256.publicKey(privateKey ByteArray)` | `ByteArray` | `EcdhError` | The 65-byte uncompressed SEC 1 point `04 ‖ x ‖ y` of `d · G`, for a 32-byte big-endian private key `d`. |
| `EcdhP256.sharedSecret(privateKey ByteArray, peerPublicKey ByteArray)` | `ByteArray` | `EcdhError` | The shared secret: the 32-byte big-endian x-coordinate of `d · Q`, leading zero bytes kept. |

`EcdhError` is `invalidPrivateKey` for a key that is not 32 bytes or whose value is 0 or at least the group
order `n` (it is never reduced), or `invalidPublicKey` for a peer key that is not 65 bytes starting with
`04` (compressed points included), has a coordinate at least `p`, or is not on the curve. The private key is
checked first. The private key is the caller's to choose, uniformly from `[1, n − 1]`.

Both calls run in time independent of the private key. Derive keys from the shared secret with
[`hkdfExtract`](#hkdf); never use it as a key directly.

```maxon
function main() returns ExitCode
	var alice = ByteArray.create()
	alice.growFilled(32, value: 1)
	var bob = ByteArray.create()
	bob.growFilled(32, value: 2)

	try 'agree'
		let fromAlice = EcdhP256.sharedSecret(alice, peerPublicKey: EcdhP256.publicKey(bob))
		let fromBob = EcdhP256.sharedSecret(bob, peerPublicKey: EcdhP256.publicKey(alice))
		print("{constantTimeEqual(fromAlice, b: fromBob)}\n")
	end 'agree' otherwise (failure) 'refused'
		match failure 'why'
			invalidPrivateKey then print("bad private key\n")
			invalidPublicKey then print("bad public key\n")
		end 'why'
	end 'refused'

	return 0
end 'main'
```

Output: `true`.

## Ecdsa

ECDSA signature verification (FIPS 186-5) for the two pairs TLS 1.3 uses, `ecdsa_secp256r1_sha256` and
`ecdsa_secp384r1_sha384`.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `Ecdsa.verify(curve EcdsaCurve, publicKey ByteArray, message ByteArray, signature ByteArray)` | `bool` | `EcdsaError` | Whether `signature`, a DER `ECDSA-Sig-Value`, is valid for `message` under the uncompressed point `publicKey`. The message is hashed with the curve's hash. |

`EcdsaCurve` is `p256` (P-256 with SHA-256, 65-byte keys) or `p384` (P-384 with SHA-384, 97-byte keys).
`EcdsaError` is the first failure of, in order:

1. `invalidPublicKey`: the key is not `04 ‖ x ‖ y` at the curve's size, has a coordinate at least `p`, or
   is not on the curve;
2. `invalidSignatureEncoding`: the signature is not exactly one strict-DER `SEQUENCE` of two `INTEGER`s,
   each minimal and non-negative, with minimal definite lengths. A negative `INTEGER` is an encoding error.

A well-formed signature whose `r` or `s` is 0 or at least `n` returns `false`; neither is reduced. Both
`s` and `n − s` verify. Verification handles only public data.

## EcdsaP256

Deterministic ECDSA signing on P-256 with SHA-256 (RFC 6979), as a TLS 1.3 server with a P-256 certificate
signs `CertificateVerify`.

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `EcdsaP256.sign(privateKey ByteArray, message ByteArray)` | `ByteArray` | `EcdsaSignError` | The DER signature of `message` under a 32-byte big-endian private key, with the nonce of RFC 6979 section 3.2 over HMAC-SHA-256. |

`EcdsaSignError` has one case, `invalidPrivateKey`: a key that is not 32 bytes or whose value is 0 or at
least `n`. The signature is a function of the key and the message alone; `s` is not normalized to the low
half, and each `INTEGER` is minimal. The nonce, its inverse and the private key are handled in constant
time. [`Ecdsa.verify`](#ecdsa) with `EcdsaCurve.p256` accepts every signature it makes.

## X509

`X509` decodes X.509 certificates (RFC 5280) and validates the chain a TLS server presents, the way a TLS
client decides whether to trust it. Everything is strict DER, read with [`DerReader`](#der); signatures
are checked with [`Ecdsa`](#ecdsa), [`Rsa`](#rsa) and [`Ed25519`](#ed25519).

| Function | Returns | Throws | Description |
|----------|---------|--------|-------------|
| `X509.parse(der ByteArray)` | `Certificate` | `X509Error` | The certificate one DER X.509 v1 or v3 certificate encodes. |
| `X509.verify(leaf ByteArray, intermediates ByteArrays, roots ByteArrays, host String, now UnixSeconds)` | `ByteArrays` | `X509VerifyError` | The chain from `leaf` to one of `roots`, leaf first, each certificate exactly as passed in, valid at `now` for the TLS server `host`. |

`parse` runs its steps in order and throws the first failure: the outer structure, the version (v1 or
v3), the serial, the signature algorithm (the TBSCertificate's copy must equal the outer one byte for
byte), issuer, validity, subject, the public key, the unique identifiers, the extensions, and the signature.

| `X509Error` case | When |
|------------------|------|
| `invalidEncoding` | Anything that is not strict DER (definite minimal lengths, single-octet tags, minimal INTEGERs, no encoded DEFAULT, a SET OF in order), an OBJECT IDENTIFIER arc above 2^63 - 1, or not the RFC 5280 structure. RSASSA-PSS parameters are read whole first, so an encoded DEFAULT there is `invalidEncoding` even when the parameters are also unsupported. |
| `unsupportedVersion` | A version other than v1 or v3. |
| `unsupportedAlgorithm` | A signature or key algorithm outside the tables below, or RSASSA-PSS parameters other than SHA-2 with MGF1 over the same hash and a salt as long as the digest. |
| `unhandledCriticalExtension` | A critical extension not listed below, or a critical nameConstraints holding a subtree that is neither a dNSName nor an iPAddress. |
| `duplicateExtension` | Two extensions with the same OID. |

The signature algorithms are `X509SignatureAlgorithm` cases: `ecdsaWithSha256`, `ecdsaWithSha384`,
`sha256WithRsaEncryption`, `sha384WithRsaEncryption`, `sha512WithRsaEncryption` (with NULL or absent
parameters), `rsassaPssSha256`, `rsassaPssSha384`, `rsassaPssSha512` and `ed25519`. The keys are
`X509KeyAlgorithm` cases: `rsa`, `ecP256` and `ecP384` (an uncompressed point) and `ed25519`. Key sizes and
whether a point is on its curve are not checked by `parse`.

A `Certificate` answers each field:

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `version()` | `CertificateVersion` | — | 1 or 3. |
| `serialNumber()` | `ByteArray` | — | The serial's content octets as encoded: two's complement, of any sign and length. |
| `signatureAlgorithm()` | `X509SignatureAlgorithm` | — | The algorithm the issuer signed with. |
| `issuer()`, `subject()` | `X509Name` | — | `der()`, the whole Name DER names are compared by, and `attributes()`, an `X509Attributes` in encoded order, each with `oid()` (dotted decimal), `value()` (the decoded text) and `shortName()` (`CN`, `O`, `OU` or `C`, throwing `X509FieldError.absent` for other types). |
| `notBefore()`, `notAfter()` | `UnixSeconds` | — | The validity period, both ends inclusive. |
| `publicKey()` | `X509PublicKey` | — | `algorithm()`, `der()` (the SubjectPublicKeyInfo, the form `Rsa` takes) and `keyBytes()` (the point, the Ed25519 key or the `RSAPublicKey` DER). |
| `basicConstraints()` | `X509BasicConstraints` | `X509FieldError` | `isCertificateAuthority()` and `pathLength()`, an `X509PathLength` that throws `absent` when there is none. |
| `keyUsage()` | `X509KeyUsages` | `X509FieldError` | The `X509KeyUsage` bits that are set, in bit order: `digitalSignature`, `contentCommitment`, `keyEncipherment`, `dataEncipherment`, `keyAgreement`, `keyCertSign`, `crlSign`, `encipherOnly`, `decipherOnly`. |
| `extendedKeyUsage()` | `StringArray` | `X509FieldError` | The purposes, dotted decimal, in encoded order. |
| `subjectAltName()` | `X509SubjectAltName` | `X509FieldError` | `dnsNames()`, a `StringArray`, and `ipAddresses()`, 4- or 16-byte `ByteArrays`. Other name forms are skipped. |
| `authorityKeyId()`, `subjectKeyId()` | `ByteArray` | `X509FieldError` | The key identifiers. |
| `nameConstraints()` | `X509NameConstraints` | `X509FieldError` | `permittedDnsNames()` and `excludedDnsNames()`, the dNSName subtrees as encoded, and `permittedIpRanges()` and `excludedIpRanges()`, each an address followed by its mask. |
| `tbsCertificate()` | `ByteArray` | — | The whole TBSCertificate DER: the signed message. |
| `signature()` | `ByteArray` | — | The signature octets. |

An extension-backed member throws `X509FieldError.absent` when the certificate does not carry the
extension.

`verify` first checks the leaf: it parses (otherwise `invalidCertificate`, or `unsupportedAlgorithm` when
that is why), is valid at `now`, its extended key usage allows TLS server authentication, and one of its
subjectAltName entries names `host` — the Common Name is never read. `host` is an IPv4 or IPv6 literal,
matched against `ipAddresses()` byte for byte, or a DNS name, matched case-insensitively against
`dnsNames()`, where `*` as a whole first label stands for exactly one label and must be followed by at least
two. Then it builds candidate chains depth first, roots before intermediates, each in the order given, at
most 10 certificates long, linking a certificate to an issuer whose subject DER equals its issuer DER and
whose subjectKeyIdentifier does not contradict its authorityKeyIdentifier. Certificates that do not parse
are left out of the pools. Each link is checked in order: the issuer is valid at `now`, is a CA whose
keyUsage (when present) allows certificate signing, has not exceeded its path length, holds the leaf's names
inside its name constraints, allows TLS server authentication, and its key verifies the child's signature.
The root is checked like any issuer, except that its own signature is not. The first candidate that passes
is returned; when none does, the first candidate's error is thrown. Every candidate issuer the search
considers counts against a budget of 100, as Go's `maxChainSignatureChecks` does, so a pool of same-named
intermediates cannot make the search exponential; each signature is verified at most once per call.

| `X509VerifyError` case | When |
|------------------------|------|
| `invalidCertificate` | The leaf does not parse. |
| `unsupportedAlgorithm` | The leaf's algorithm is not supported, a P-256 key signs with SHA-384 or a P-384 key with SHA-256, or an issuer's RSA modulus is outside 2048 to 4096 bits. |
| `expired`, `notYetValid` | A certificate in the chain is not valid at `now`. |
| `incompatibleKeyUsage` | A certificate's extended key usage lists neither serverAuth nor anyExtendedKeyUsage. |
| `hostnameMismatch` | No subjectAltName entry of the leaf names `host`. |
| `unknownAuthority` | No chain links the leaf to a root. |
| `notAuthorizedToSign` | An issuer is not a CA, or its keyUsage lacks keyCertSign. |
| `pathLengthExceeded` | More intermediates sit below an issuer than its pathLenConstraint allows. |
| `nameConstraintViolation` | A dNSName or iPAddress of the leaf, or `host`, falls outside an issuer's name constraints. |
| `badSignature` | A signature does not verify under its issuer's key. |
| `signatureCheckLimitExceeded` | The search considered more than 100 candidate issuers before a chain passed. |

Name constraints apply to every dNSName and iPAddress of the leaf and to `host`, from every CA in the chain,
the root included. A DNS name matches a subtree when its last labels equal the subtree's; a subtree with a
leading `.` needs at least one more label, and an empty one matches every name. A wildcard leaf name is
refused when an excluded subtree lies under it. An address matches a range of its own family whose masked
bits it shares. A CA with only dNSName subtrees leaves addresses unconstrained, and the reverse.

`verify` does not check revocation, certificate policies or Certificate Transparency. Pass the wall clock as
`now` in production; `X509.verify` takes the trusted roots as an argument.

| Name | Definition |
|------|------------|
| `ByteArrays` | `Array with ByteArray` |
| `CertificateVersion` | `int(1 to 3)` |
| `X509PathLength` | `int(0 to i64.max)` |
| `X509KeyUsages` | `Array with X509KeyUsage` |
| `X509Attributes` | `Array with X509Attribute` |

```maxon
function main() returns ExitCode
	let chain = try X509.verify(ByteArray.create(), intermediates: ByteArrays.create(), roots: ByteArrays.create(), host: "example.com", now: 1767225600) otherwise (e) 'refused'
		print("{e}\n")
		return 0
	end 'refused'

	print("{chain.count()}\n")
	return 1
end 'main'
```

Output: `invalidCertificate`.

## Hasher

`Hasher` is an incremental FNV-1a 64-bit hash, for content keys and cache digests. It is not cryptographic.
Its result is a `HashDigest`, `bits(64)`.

| Member | Returns | Description |
|--------|---------|-------------|
| `Hasher.create()` | `Hasher` | A hasher at the FNV-1a offset basis. |
| `Hasher.resume(state HashDigest)` | `Hasher` | Continue from a value `finalize()` returned. |
| `combine(value HashDigest)` | — | Fold in one value. |
| `combine(bytes ByteArray)` | — | Fold in each byte. |
| `finalize()` | `HashDigest` | The running state, not post-processed, so `resume(finalize())` continues exactly. |
| `Hasher.empty()` | `HashDigest` | The state of an empty fold. |
| `Hasher.combined(state HashDigest, value HashDigest)` | `HashDigest` | One step, as an expression. |
| `Hasher.combined(state HashDigest, bytes ByteArray)` | `HashDigest` | Each byte, as an expression. |

FNV-1a folds a flat byte sequence and records no lengths, so `"ab"` then `"c"` hashes the same as `"a"`
then `"bc"`. When hashing a sequence of parts, combine each part's length too.

```maxon
function main() returns ExitCode
	var hasher = Hasher.create()
	hasher.combine("ab".toByteArray())
	hasher.combine(7)

	let direct = Hasher.combined(Hasher.combined(Hasher.empty(), bytes: "ab".toByteArray()), value: 7)
	print("{hasher.finalize() == direct}\n")
	return 0
end 'main'
```

Output: `true`.

## Clock

`Clock` is a monotonic stopwatch: only the difference between two readings means anything. For the date
and time of day use `WallClock`.

| Method | Returns | Description |
|--------|---------|-------------|
| `Clock.nowNanos()` | `InstantNanos` | A high-resolution reading in nanoseconds. |
| `Clock.elapsedNanos(since InstantNanos)` | `DurationNanos` | Nanoseconds since a `nowNanos()` reading; `0` if the clock appears to go backwards. |
| `Clock.nowMs()` | `InstantMs` | The same clock in milliseconds. |
| `Clock.elapsedMs(since InstantMs)` | `DurationMs` | Milliseconds since a `nowMs()` reading. |
| `Clock.osTickMs()` | `InstantMs` | How long the machine has been up, in milliseconds, from the operating system's tick count. It moves in host ticks, up to 16 ms apart on `x64-windows`. |
| `Clock.threadCpuTicks()` | `ThreadCpuTicks` | CPU time the calling thread has consumed. It advances only while the thread runs on a core, so a difference of two readings is that thread's cost alone, whatever else the machine runs. |

`since` is the first argument and has no default, so it is written without a label: `Clock.elapsedNanos(start)`.

The unit of `threadCpuTicks()` belongs to the platform: TSC cycles on Windows (`QueryThreadCycleTime`) and
nanoseconds elsewhere (`CLOCK_THREAD_CPUTIME_ID`). Compare two readings from one platform, or ratios of
differences. `ThreadCpuTicks` is `int(0 to u64.max)`. `osTickMs()` and `threadCpuTicks()` are refused on
`wasm32-wasi` (E3104).

| Target | Source | Period |
|--------|--------|--------|
| `x64-windows` | `QueryPerformanceCounter` | 100 ns |
| `arm64-macos`, `arm64-linux`, `x64-linux` | the kernel's monotonic clock | 1 ns |
| `wasm32-wasi` | refused at compile time (E3104) | |

Readings are always in nanoseconds, but two back-to-back readings can be equal when the period is coarser.

`InstantMs`, `DurationMs`, `InstantNanos` and `DurationNanos` are all `int(0 to u64.max)`.

### WallClock

| Method | Returns | Description |
|--------|---------|-------------|
| `WallClock.nowUnixSeconds()` | `UnixSeconds` | Whole seconds since 1970-01-01 00:00:00 UTC. |
| `WallClock.nowUnixNanos()` | `UnixNanos` | Nanoseconds since 1970-01-01 00:00:00 UTC. |
| `WallClock.rfc3339Millis(nanos UnixNanos)` | `String` | `nanos` as an RFC 3339 UTC time with milliseconds: `2023-11-14T22:13:20.123Z`. |
| `WallClock.rfc3339Nanos(nanos UnixNanos)` | `String` | The same with nanoseconds: `2023-11-14T22:13:20.123456789Z`. |

Every reading and format is in UTC. `UnixSeconds` and `UnixNanos` are signed, negative
before 1970. The wall clock can step backwards (an NTP correction, a resumed virtual machine), so measure
durations with `Clock`.

```maxon
function main() returns ExitCode
	let start = Clock.nowNanos()
	sleep(20)
	let took = Clock.elapsedNanos(start)
	print("{took >= 20000000} {WallClock.nowUnixSeconds() > 1700000000}\n")
	return 0
end 'main'
```

Output: `true true`.

### CivilDate

A `CivilDate` is a date in the proleptic Gregorian calendar, converted to and from a count of days since
1970-01-01 (`UnixDays`, signed).

| Member | Returns | Description |
|--------|---------|-------------|
| `CivilDate.create(year CivilYear, month CivilMonth, day CivilDay)` | `CivilDate` | A date from its parts. Throws `CivilDateError.impossibleDay` for a day past the end of its month (February 30, or February 29 in a common year). |
| `CivilDate.fromDays(days UnixDays)` | `CivilDate` | The date `days` after 1970-01-01. |
| `days()` | `UnixDays` | The inverse of `fromDays`. |
| `year`, `month`, `day` | `CivilYear`, `CivilMonth`, `CivilDay` | The parts; `month` and `day` count from 1. |

`CivilDateError` is the error `create` throws; its one case is `impossibleDay`.

```maxon
function main() returns ExitCode
	let date = CivilDate.fromDays(20000)
	let march = try CivilDate.create(2000, month: 3, day: 1) otherwise panic("2000-03-01 is a real date")
	print("{date.year}-{date.month:02}-{date.day:02} {march.days()}\n")
	print("{WallClock.rfc3339Millis(1700000000123456789)}\n")
	return 0
end 'main'
```

Output: `2024-10-04 11017`, then `2023-11-14T22:13:20.123Z`.

## Scheduler

`Scheduler` holds controls over the green-thread scheduler. It is a namespace with no fields.

| Method | Description |
|--------|-------------|
| `Scheduler.yield()` | Let the next runnable green thread run. The caller resumes behind everything that was already runnable. When nothing else is runnable it returns promptly, so a loop that yields is a busy wait that lets others progress. It uses no timer, unlike `sleep(0)`, and is safe in a program that never starts a green thread. |
| `Scheduler.processorCount()` | The number of processors the scheduler runs services on, as a `SchedulerProcessorCount` (`int(1 to u64.max)`, a `Count`, so it compares with a collection's `count()` with no cast): the machine's logical processor count, or the count `MAXON_MAX_PROCS` sets (1 to 2147483647, above the machine's count as well as below it). The count is resolved before `main` runs, so it is the same before the first `spawn` as after it. |
| `Scheduler.machineCpuCount()` | The machine's logical processor count, at least 1, whatever `MAXON_MAX_PROCS` sets: every active processor in every processor group on Windows, the processors this process may run on under Linux. A `SchedulerProcessorCount`. |
| `Scheduler.counters()` | The scheduler's event counters since the program started, as a `SchedulerCounters`, each read once. |
| `Scheduler.currentStackBytes()` | The calling green thread's current stack size in bytes, a `StackByteCount` (`int(0 to u64.max)`). A stack grows on demand and halves once the thread has been idle with most of it unused. |
| `Scheduler.quiesce()` | Park the caller until every machine is idle and nothing pending can run again, and answer how many other green threads are still owned — a service still running, or a promise neither awaited nor dropped — as a `GreenThreadCount` (`int(0 to u64.max)`): `0` when every one has finished, or the count left. A machine waiting for its pinned thread's next turn counts as idle, and a coroutine still unwinding after its promise was dropped keeps the wait open until it finishes. A coroutine whose promise has been dropped answers at once, with the count at that moment. The program carries on afterwards unchanged; services stay running. |
| `Scheduler.parallelBoundary()` | A compile-time marker with no run-time effect. A CPU-bound function calls it to mark a spawn of itself as deliberate, which satisfies [E3073](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3073) for a function that only computes. |
| `Scheduler.pinToMachine()` | Bind the calling green thread to the machine (OS thread) running it, and answer whether it did. A pinned thread that parks resumes on the same machine. Pins nest: each one taken is released once. |
| `Scheduler.unpinFromMachine()` | Release one pin the calling green thread took; the last release frees it from its machine. Called by a green thread that holds no pin, it is a fatal runtime error. |
| `Scheduler.currentMachine()` | The machine running the calling green thread, as a `MachineIndex` (`int(0 to u64.max)`); `0` is no machine. |

On `wasm32-wasi` `parallelBoundary()` compiles and every other member is refused (E3104). Green threads,
`async` and `await` are described under Concurrency in [LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md).

```maxon
function worker() returns ExitCode
	Scheduler.yield()
	print("worker\n")
	return 0
end 'worker'

function main() returns ExitCode
	let p = async worker()
	Scheduler.yield()
	_ = await p
	print("main\n")
	return 0
end 'main'
```

Output: `worker` then `main`.

### MachinePin

`MachinePin` is a pin of the calling green thread to its machine, held as a value: it takes one pin the way
`Scheduler.pinToMachine()` does, and releases it when it is released or dropped, whichever comes first.

| Member | Returns | Description |
|--------|---------|-------------|
| `MachinePin.acquire()` | `MachinePin` | Pin the calling green thread to the machine running it. |
| `held()` | `bool` | True while the pin is held; false when there was no machine to pin to, and after `release()`. |
| `release()` | — | Release the pin. A later `release()` or drop finds it released and returns. |
| `clone()` | — | Panics: a pin is released once, so it is held in one place. |

Releasing a held pin is a fatal runtime error in three cases, each with its own message: from code that is
not running on a green-thread machine, from a green thread other than the one that took it, and when that
green thread holds no pin any longer (a `Scheduler.unpinFromMachine()` already released it).

### SchedulerCounters

Every field is a `SchedulerEventCount` (`int(0 to u64.max)`) summed over every processor. All but
`maxActiveWorkers` and `idleProcessors` are totals that only rise.

| Field | Counts |
|-------|--------|
| `steals` | Green threads a worker took from another processor's run queue. |
| `retakes` | Processors the system monitor took back from a machine stuck in a long kernel call. |
| `maxActiveWorkers` | The most worker machines ever active at once; at least 1. |
| `recycledThreads` | Green-thread records a spawn took from the free list, in place of a fresh one. |
| `timerStarts` | Machines the monitor started for a timer no running machine would fire in time. |
| `syscalls` | Kernel calls entered by a machine holding a processor, where the monitor could retake it. |
| `slices` | Time slices the scheduler started. |
| `preempts` | Preemption requests green threads honoured, after 10 ms on one processor. |
| `parkWakes` | Times a parked machine's wait returned, for any reason. An idle program keeps it near zero. |
| `netpollBlocks` | Times a green thread suspended on the network poller waiting for a socket. |
| `idleProcessors` | Processors idle with no machine right now. |
| `threadRecordsCarved` | Green-thread records and reply cells ever carved from the scheduler's arena. |
| `timerScanSteps` | Timer-heap entries inspected while cancelling deadlines. |
| `globalPushes` | Green threads ever pushed onto the global run queue. |

## HeapStats

`HeapStats` reads the allocator's own counters and levels. It is a namespace with no fields. `thread()`,
`process()`, `slab()` and `census()` answer heap records, and a record still live at a later `slab()` or
`census()` is a slot that reading counts. The scalar readings (`threadAllocations()` and its five siblings)
allocate nothing, so two of them bracket a stretch of code without counting the reads themselves.

| Method | Returns | Description |
|--------|---------|-------------|
| `HeapStats.allocationsMade()` | `HeapCount` | Tracked (managed-box) allocations since the process started. |
| `HeapStats.allocationsLive()` | `HeapCount` | Tracked allocations live now. |
| `HeapStats.bytesAllocated()` | `HeapBytes` | Bytes those tracked allocations handed out, cumulative. |
| `HeapStats.rawAllocationsMade()` | `HeapCount` | Raw (header-free) allocations since the process started: element buffers, string bytes, closure environments. |
| `HeapStats.rawAllocationsLive()` | `HeapCount` | Raw allocations live now. |
| `HeapStats.rawBytesAllocated()` | `HeapBytes` | Bytes those raw allocations handed out, cumulative. |
| `HeapStats.allocationsTagged(tag HeapBucketIndex)` | `HeapCount` | Tracked allocations whose allocation tag is `tag`, cumulative. Bucket 0 holds the untagged ones and 2047 every tag from 2047 up; the buckets sum to `allocationsMade()`. Tags exist in a `--debugstream` build; every other build answers 0. |
| `HeapStats.bytesTagged(tag HeapBucketIndex)` | `HeapBytes` | Bytes those tagged allocations asked for, cumulative. |
| `HeapStats.threadAllocations()`, `HeapStats.threadFrees()` | `HeapCount` | The calling green thread's allocations and frees, both layers together. |
| `HeapStats.threadBytes()` | `HeapBytes` | The bytes the calling green thread's allocations asked for, both layers together. |
| `HeapStats.processAllocations()`, `HeapStats.processFrees()` | `HeapCount` | Every thread's allocations and frees, both layers together. |
| `HeapStats.processBytes()` | `HeapBytes` | The bytes every thread's allocations asked for, both layers together, a box's header included. |
| `HeapStats.thread()` | `HeapActivity` | The calling green thread's allocations, frees and bytes, both layers together. |
| `HeapStats.process()` | `HeapActivity` | Every thread's allocations, frees and bytes, both layers together, a box's header included. Each figure only rises, so two readings bracket a phase while other threads allocate. |
| `HeapStats.slab()` | `SlabUsage` | The slab's levels now. |
| `HeapStats.census(mode HeapCensusMode)` | `SlabCensus` | Walk every live slot once and tally it into 2048 buckets. |
| `HeapStats.scavenge()` | `HeapBytes` | Return idle memory to the operating system, and answer the bytes handed back by this call. |

`HeapActivity` has three fields: `allocations` and `frees` (`HeapCount`) and `bytes` (`HeapBytes`). A
program with no green threads answers `thread()` from the process-wide totals.

`SlabUsage` holds bytes of slot, a size class's rounding included:

| Field | Type | Holds |
|-------|------|-------|
| `live` | `HeapBytes` | Slots handed out. |
| `free` | `HeapBytes` | Slots on some span's free list, a slot queued by a free on another processor included. `live + free` is every slot of every live span. |
| `cachedFree` | `HeapBytes` | The part of `free` a processor's cache can still reach; `free - cachedFree` is free memory stranded in spans a live slot keeps alive. |
| `parked` | `HeapBytes` | Whole spans holding no live slot: the backlog `scavenge()` reclaims. |
| `committed` | `HeapBytes` | Arena memory backed by physical pages, plus live direct OS mappings. |
| `remoteFrees` | `HeapCount` | Frees performed on a processor other than the one caching the slot's span, cumulative. |

`HeapCensusMode` chooses what a bucket of `census` holds: `byTag` (0), the slots whose allocation tag is the
bucket index (in a `--debugstream` build), or `bySizeClass` (1), the slots of that size class. In `byTag`
mode bucket 2046 holds slots with no plausible tag and 2047 tags past the table.

| `SlabCensus` member | Returns | Description |
|---------------------|---------|-------------|
| `mode` | `HeapCensusMode` | The mode it was taken in. |
| `tallyBytes` | `HeapBytes` | The live bytes the walk visited. |
| `liveBytes` | `HeapBytes` | The slab's `live` level read right after the walk, before the snapshot's own arrays exist; it equals `tallyBytes` when nothing else allocated in between. |
| `bucketCount(index HeapBucketIndex)` | `HeapCount` | Live slots in bucket `index`. |
| `bucketBytes(index HeapBucketIndex)` | `HeapBytes` | Their bytes. |

`scavenge()` destroys every span with no live slot, so its chunks serve any size class again, and hands the
operating system the physical pages of every 64 KiB granule that stayed wholly free across two calls. The
first call after a population is dropped answers 0 and the second answers the bytes; a granule reused in
between stays committed. On `wasm32-wasi` the chunks return to the arena and linear memory keeps its size.

`HeapCount` and `HeapBytes` are `int(0 to u64.max)`; `HeapBucketIndex` is `int(0 to 2047)`.

```maxon
typealias Payload = Array with Byte

function main() returns ExitCode
	let before = HeapStats.processAllocations()
	var kept = Payload.create()
	kept.push(1)
	let after = HeapStats.processAllocations()
	print("{after > before} {HeapStats.slab().live > 0} {kept.count()}\n")
	return 0
end 'main'
```

Output: `true true 1`.

## SharedValue

`SharedValue with T` is a live cell every thread can read: a reader takes an immutable snapshot lock-free,
and a writer publishes a whole new value. Use it through an alias:
`typealias ConfigCell = SharedValue with Config`. `T` is any type whose value graph reaches no OS handle,
function value or promise ([E3171](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3171)): records with `var`
fields, `Array` and `Map` publish as well as scalars, strings and service handles. It is the run-time form of
a registry key's `default` ([`SharedValue`](LANGUAGE_REFERENCE.md#sharedvalue--a-live-value-made-at-run-time)).

A value `current()` returns, like a registry `default`'s, is read-only all the way down: a write through it is
[E3159](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3159) and binding it to a `var` is
[E3078](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3078); `clone()` gives a mutable copy. `publish` takes a
value with no other owner ([E3174](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3174)); publish a fresh value
or a `clone()`. A tuple of heap-free elements is published as a copy
([Moves and Sharing](LANGUAGE_REFERENCE.md#moves-and-sharing)).

| Member | Returns | Description |
|--------|---------|-------------|
| `SharedValue.create(value T)` | `SharedValue with T` | A cell holding `value`. A `clone()` of the cell is the same cell, so hand one to a service and it sees every publish. |
| `current()` | `T` | The value published last. A snapshot stays valid after later publishes. |
| `publish(value T)` | — | Make `value` current for every later `current()` on every thread. |
| `publish(value T, replacing T)` | `bool` | Publish only while `replacing` — a value `current()` returned — is still current. On `true` it is published; on `false` the current value stays and `value` is dropped. |

A read-modify-write re-reads `current()` and retries `publish(…, replacing:)` until it answers `true`, so
concurrent writers each land.

```maxon
type Config
	export let n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Config'

typealias Integer = int(i64.min to i64.max)
typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create(1))
	let seen = cell.current()
	cell.publish(Config.create(3))
	let stale = cell.publish(Config.create(2), replacing: seen)
	let latest = cell.current()
	let fresh = cell.publish(Config.create(5), replacing: latest)
	print("{seen.n} {stale} {fresh} {cell.current().n}\n")
	return 0
end 'main'
```

Output: `1 false true 5`.

## Random

`Random` draws integers and bytes from the operating system's cryptographic random source, on every
target — `wasm32-wasi` included, through `wasi:random`.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `Random.draw()` | `RandomDraw` | `RandomError` | A uniformly distributed integer from 0 to `u64.max` — all 64 bits random. |
| `Random.below(bound RandomBound)` | `RandomDraw` | `RandomError` | A uniformly distributed integer from 0 to `bound - 1`, for any bound up to `u64.max`. |
| `Random.bytes(count RandomByteCount)` | `ByteArray` | `RandomError` | `count` fresh random bytes, for keys and nonces. `Random.bytes(0)` is empty and asks the operating system for nothing. |

| Name | Definition |
|------|------------|
| `RandomDraw` | `int(0 to u64.max)` |
| `RandomBound` | `int(1 to u64.max)`, implementing `RandomDraw` |
| `RandomByteCount` | `int(0 to u64.max)`, implementing `ElementIndex` |

`RandomError` has one case, `unavailable`: the operating system refused to supply random bytes.

```maxon
function main() returns ExitCode
	let roll = try Random.below(6) otherwise panic("no random source")
	print("{roll + 1}\n")
	return 0
end 'main'
```

It prints a number from 1 to 6.

## Math

`Math` provides the elementary functions over `FullRangeFloat`, which is `float(f64.min to f64.max)`. The rounding and
`sqrt`/`abs`/`min`/`max` builtins are in [Core Functions](#core-functions).

| Method | Returns | Description |
|--------|---------|-------------|
| `Math.sin(x FullRangeFloat)` | `FullRangeFloat` | Sine of radians. |
| `Math.cos(x FullRangeFloat)` | `FullRangeFloat` | Cosine of radians. |
| `Math.tan(x FullRangeFloat)` | `FullRangeFloat` | Tangent of radians. |
| `Math.atan(z FullRangeFloat)` | `FullRangeFloat` | Arc tangent. |
| `Math.atan2(y FullRangeFloat, x FullRangeFloat)` | `FullRangeFloat` | The angle of `(x, y)`, in `[-π, π]`. |
| `Math.exp(x FullRangeFloat)` | `FullRangeFloat` | e raised to `x`. |
| `Math.log(x FullRangeFloat)` | `FullRangeFloat` | Natural logarithm. |
| `Math.log2(x FullRangeFloat)` | `FullRangeFloat` | Base-2 logarithm. |
| `Math.log10(x FullRangeFloat)` | `FullRangeFloat` | Base-10 logarithm. Exact at every power of ten a double represents exactly: `log10(10^k)` is `k` for `k` in 0 to 22. |
| `Math.pow(base FullRangeFloat, exponent FullRangeFloat)` | `FullRangeFloat` | `base` raised to `exponent`, following IEEE 754's special cases (`pow(0.0, -1.0)` is infinity; `pow(x, 0)` and `pow(1, y)` are 1 even for NaN). |
| `Math.hasNegativeSignBit(z FullRangeFloat)` | `bool` | True when the sign bit is set, including `-0.0`, which no comparison can distinguish from `0.0`. |
| `Math.isNaN(z FullRangeFloat)` | `bool` | True for every NaN, whatever its bit pattern. |
| `Math.positiveInfinity()` | `FullRangeFloat` | +∞. |
| `Math.negativeInfinity()` | `FullRangeFloat` | −∞. |
| `Math.quietNaN()` | `FullRangeFloat` | A quiet NaN. |
| `Math.bitsOf(x FullRangeFloat)` | `FloatBits` | The 64-bit IEEE 754 pattern of `x`, as a signed word: `Math.bitsOf(1.0)` is `0x3FF0000000000000`, and every negative value, `-0.0` included, has a negative pattern. |
| `Math.fromBits(bits FloatBits)` | `FullRangeFloat` | The double whose IEEE 754 pattern is `bits`; the inverse of `bitsOf`. |

`FloatBits` is `int(i64.min to i64.max)`.

These are implemented in Maxon from series expansions, so results can differ from a C library in the last
bit.

```maxon
function main() returns ExitCode
	print("{Math.sin(0.0)} {Math.exp(0.0)} {Math.log2(8.0)} {Math.pow(2.0, exponent: 10.0)}\n")
	print("{Math.atan2(1.0, x: 1.0)} {Math.hasNegativeSignBit(-0.0)}\n")
	return 0
end 'main'
```

Output: `0.0 1.0 3.0 1024.0` and `0.7853981633974483 true`.

## Primitive Extensions

`int`, `float` and `bool` conform to the core interfaces, so they work as `Map` keys, `Set` elements, in
`sort()` and in generic code. Every alias of `int` or `float` conforms as its primitive does.

| Type | Conforms to | Notes |
|------|-------------|-------|
| `int` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `hash()` is `(self xor (wide * 0x9E3779B1)) and 0xFFFFFFFF`, where `wide = (self shr 32) - (self shr 63)`. `wide` is 0 from -2^32 up to 2^32 (exclusive), so there the hash is the low 32 bits of the value; outside it the high half reaches the hash too. |
| `float` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `compare` is a total order: NaN equals NaN and sorts below every other value. `hash()` folds the 64-bit IEEE-754 pattern into 32 bits, `(bits xor (bits shr 32)) and 0xFFFFFFFF`, except that `-0.0` hashes as `0.0` does (`0`). |
| `bool` | `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `false` sorts before `true`. |

| Method | Returns | Description |
|--------|---------|-------------|
| `hash()` | `HashValue` | Through `Hashable`. |
| `equals(other Self)` | `bool` | Through `Equatable`. |
| `compare(other Self)` | `Ordering` | Through `Comparable`. |
| `toString()` | `String` | The same text as interpolation. |
| `clone()` | `Self` | The value itself. |
| `int.fromString(input String)` | `FullRangeInt` | Takes one optional leading `-` and decimal digits; throws `ParseError.invalidFormat` for anything else and for a value outside `i64.min` to `i64.max`. Likewise `float.fromString`, `bool.fromString`, `byte.fromString`. |

```maxon
function main() returns ExitCode
	let five = 5
	let half = 2.5
	let yes = true
	print("{five.compare(3)} {five.hash()} {half.compare(2.5)} {yes.compare(false)} {five.toString()}\n")
	return 0
end 'main'
```

Output: `greaterThan 5 equalTo greaterThan 5`.

## Testing

`Expect` is the assertion library a `test` declaration uses. Every matcher throws `TestFailure.assertion`
when it does not hold, so a test stops at its first failed assertion. A test body calls matchers without
`try`; in a helper function a test calls, a matcher needs `try` like any throwing call.
`maxon test` runs a project's tests — see [CLI_REFERENCE.md](CLI_REFERENCE.md).

```maxon
test 'splits on commas'
	let parts = "a,b,c".split(",")
	Expect.equal(parts.count(), expected: 3)
	Expect.equal("a,b".replace(",", with: ";"), expected: "a;b")
	Expect.close(0.1 + 0.2, expected: 0.3, within: 0.000001)
	Expect.isTrue(parts.count() == 3, message: "three parts")
	Expect.contains("haystack", needle: "st")
end 'splits on commas'
```

A failure prints a report to stderr at the assertion, naming the caller's file and line:

```text
FAIL split.maxtest:20: Expect.equal
  expected: 5
  received: 4
  message: two plus two
```

The `message:` line appears only when a message was given.

### Matchers

Every matcher also takes `message String = ""`, `file String = __file__` and
`line SourceLineNumber = __line__`; only `fail` requires its message.

| Matcher | Argument types | Holds when |
|---------|----------------|------------|
| `Expect.equal(actual, expected:)` | any `Equatable` type | `actual == expected` |
| `Expect.notEqual(actual, expected:)` | any `Equatable` type | `actual != expected` |
| `Expect.greaterThan(actual, than:)` | any `Comparable` type | `actual > than` |
| `Expect.lessThan(actual, than:)` | any `Comparable` type | `actual < than` |
| `Expect.atLeast(actual, than:)` | any `Comparable` type | `actual >= than` |
| `Expect.atMost(actual, than:)` | any `Comparable` type | `actual <= than` |
| `Expect.close(actual, expected:, within:)` | `AssertedReal` | `abs(actual - expected) <= within` |
| `Expect.isTrue(actual)` | `bool` | `actual` is `true` |
| `Expect.isFalse(actual)` | `bool` | `actual` is `false` |
| `Expect.contains(haystack, needle:)` | `String` | `haystack` contains `needle` |
| `Expect.startsWith(haystack, needle:)` | `String` | prefix match |
| `Expect.endsWith(haystack, needle:)` | `String` | suffix match |
| `Expect.isEmpty(haystack)` | `String` | no characters |
| `Expect.fail(message)` | — | never; fails unconditionally |

`equal`, `notEqual` and the four ordering matchers are each one
[generic function](LANGUAGE_REFERENCE.md#generic-functions): the type is inferred from the two arguments, so
`Expect.equal(parts.count(), expected: 3)` compares two `Count` values and a value of any alias needs
no cast. Each value is printed as its interpolation prints it; `String` values are quoted, so empty or
space-padded values stay visible.

`close` compares two floats within a tolerance and takes `AssertedReal`, so a non-float argument is refused
at the call. NaN satisfies no comparison, so it fails `close` and every ordering matcher.

`isTrue` is the general form for any predicate:
`Expect.isTrue(a == b, message: "expected {b}, got {a}")`.

```maxon
enum TestFailure implements Error
	assertion
end 'TestFailure'
```

A `test` declaration is implicitly `throws TestFailure`. An error of any other type raised in a test body is
reported as a failure naming the error and the line of the operation that threw it.

## Build

`Build` is how a target of a `.maxproj` project file, or a task of a `.maxtasks` file, describes what
`maxon build` compiles. A target is an exported function of no parameters returning `ExitCode`; these
calls write the description as JSON to the file the driver names in `MAXON_BUILD_DESCRIPTION`, and the
compiler reads it back. With that variable unset the description goes to stdout, so a project file run
by hand can be inspected. A target describes one build, and a second description in the same run is
refused. The contract with the command line is documented under `maxon build` in
[CLI_REFERENCE.md](CLI_REFERENCE.md).

```maxon
// app.maxproj
export function app() returns ExitCode
	Build.build("src")
	return 0
end 'app'

export function gen_tool() returns ExitCode
	Build.build("tools/gen.maxon", output: ".maxon/gen", debugInfo: false)
	return 0
end 'gen_tool'
```

### Build

| Function | Returns | Description |
|----------|---------|-------------|
| `Build.build(source String, output String = "", debugInfo bool = true, version String = "", defines StringArray = empty, rebuildWithOutput bool = false, inputs StringArray = empty)` | — | Describe one source file or directory compiled to one output. An empty `output` builds `.maxon/<name>`, `<name>` being the project or task file's own name. |
| `Build.buildWithConfig(config BuildConfig)` | — | Describe one full `BuildConfig`, which may list several sources. |
| `Build.delegate(directory String, target String = "", inputs StringArray = empty)` | — | Describe a build by handing it to a target of `directory`'s own `.maxproj` file, run with that directory as its working directory. `target` names one of its targets as `maxon build` does, each `_` written `-`; empty means its sole one. `inputs` joins the delegated project's own. |
| `Build.emitBuildConfig(config BuildConfig)` | — | Write one configuration as a JSON object, every string JSON-escaped; `build`, `buildWithConfig` and `delegate` use it. |

`output` omits the extension; the compiler adds `.exe` on Windows and `.wasm` for `wasm32-wasi`.
`debugInfo` controls the `<output>.mxdbg` sidecar that `maxon debug` and `maxon profile` read, and
`maxon build --no-debug-info` turns it off whatever the description says.

### BuildConfig

| Field | Type | Description |
|-------|------|-------------|
| `output` | `String` | Where the executable goes, without an extension. Empty means `.maxon/<name>`, as for `Build.build`. |
| `sources` | `StringArray` | Files and directories compiled as one program, in order. An empty list is refused. |
| `debug_info` | `bool` | Write the `.mxdbg` sidecar. |
| `version` | `String` | A dotted product version stamped into the binary (a `VS_VERSIONINFO` resource on Windows, `LC_SOURCE_VERSION` on macOS); empty means unversioned. A component that is not a number, or that the target's version field cannot hold, refuses the build. |
| `defines` | `StringArray` | `name=value` pairs, each replacing a top-level `String` constant's default, as `maxon build --define` does. |
| `directory` | `String` | The directory whose own `.maxproj` file describes this build. Empty for an ordinary build; stating it alongside `sources` is refused. |
| `delegateTarget` | `String` | With `directory`, which of the delegated project's targets to build. |
| `rebuildWithOutput` | `bool` | Once a build for the host succeeds, run the written program with the same command line in the same directory, adding `MAXON_SECOND_STAGE=1`, and exit with its exit code; a build that finds `MAXON_SECOND_STAGE` set ends at its own output. Stating it alongside `directory` is refused. See [Building again with the written program](CLI_REFERENCE.md#building-again-with-the-written-program). |
| `inputs` | `StringArray` | Files besides the sources whose contents the description depends on, resolved against the project file's directory. The language server runs the project file again when one of them changes; a directory is refused. See [`maxon lsp-server`](CLI_REFERENCE.md#maxon-lsp-server). |

`BuildConfig.create(sources StringArray, output String = "", debug_info bool = true, version String = "",
defines StringArray = empty, directory String = "", delegateTarget String = "", rebuildWithOutput bool = false,
inputs StringArray = empty)` builds
one.

`defines` is how a project file puts something it computed into the binary, such as a version derived
from git. A define whose name matches no constant, or more than one, is refused (E3149, E3150), as is a
constant whose initializer is something other than a plain string literal (E3151). A `--define` on the
command line wins over the described build's.
