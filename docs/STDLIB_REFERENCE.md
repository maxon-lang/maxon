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
| [I/O and processes](#file) | File, FilePath, Directory, Console, CommandLine, Log, Process, Subprocess, SharedMemory |
| [Network](#tcpclient) | TcpClient, TcpListener, HttpClient, URL |
| [Data](#json) | Json, Sha256, Hasher |
| [System](#clock) | Clock, Scheduler, Math, Primitive Extensions |
| [Testing](#testing) | Testing |
| [Build](#build) | Build |

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
| `SegmentByteCount`, `SegmentOffset`, `SegmentWord` | see [SharedMemory](#sharedmemory) | SharedMemory |

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

The rules for the runtime files themselves are in [The Runtime Tier](LANGUAGE_REFERENCE.md).

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

Trigonometry, logarithms and powers are in [Math](#math).

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
is `3.14`. See string interpolation in [LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md).

## String

`String` is an immutable-by-default UTF-8 string. Its unit of iteration and counting is the **grapheme
cluster**, a user-perceived character: `"é👍🏽".count()` is 2, although it is 10 bytes and 3 codepoints.
Positions are `StringIndex` values, which carry both a grapheme index and a byte position so stepping
never rescans the string.

`String` implements `Hashable`, `Equatable`, `Iterable` (over `Character`) and `Cloneable`.

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
| `String.fromCString(cs cstring)` | `String` | Copy the NUL-terminated bytes at `cs`; the string owns its copy. Stops at the first zero byte. |
| `String.init(managed)` | `String` | The compiler's literal initializer; programs write a literal instead. |
| `toByteArray()` | `ByteArray` | The UTF-8 bytes as a new, independent array. |
| `cstr()` | `cstring` | A NUL-terminated view of the bytes, for an intrinsic that takes `cstring`. Valid while the string is alive and unmodified. |
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

Each view has `count()` and `createIterator()`, and works in `for`-`in`. Constructing a view does not copy.
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
| `byteLength()` | `int(0 to u64.max)` | UTF-8 bytes in the cluster. |
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
| `CharacterSet.from(chars Set with Character)` | Exactly the characters given |

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
when its element is.

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
| `clone()` | `Array` | A second array over the same elements. Storage is shared copy-on-write and separates on the first write to either. An element that is a record holding a field at an interface type is copied through that field's conformer; an element held at an interface type itself is not supported (an element slot is one machine word). |
| `Array.from(source Iterable)` | `Array` | Collect every element of an iterable, called through an alias (`ScoreArray.from(range)`). The iterable must bind `Element` to the alias's own element (E3127). |
| `Array.init(managed)` | `Array` | Wrap raw compiler-managed storage; used by the compiler and the library. |
| `managed` | field | The array's raw storage, for `appendMemory` and library code. |

### Reading

| Member | Returns | Description |
|--------|---------|-------------|
| `count()` | `int(0 to u64.max)` | Number of elements. |
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
| `appendMemory(source)` | Append every element of raw storage (another array's `managed`) without wrapping it in an `Array`. |
| `pop()` | Remove and return the last element. Throws `indexOutOfBounds` when empty. |
| `insert(at, value Element)` | Insert, shifting later elements right. An `at` past the end appends. |
| `remove(at)` | Remove and return the element at `at`. Throws `indexOutOfBounds`. |
| `clear()` | Remove every element, keeping the capacity. |

### Capacity and length

| Member | Description |
|--------|-------------|
| `reserve(minCapacity)` | Ensure at least `minCapacity` slots. |
| `resize(newLength)` | Set the length. Growing exposes zero-valued elements, so it is refused at compile time (E3106) for an element type that is not a plain value — a struct, a `String`, a nested container. Use `growFilled` or `truncate` for those. |
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
| `count()` | `int(0 to u64.max)` | O(1) | Number of elements. |
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
| `clone()` | `List` | O(n) | A deep copy. |
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
| `count()` | `int(0 to 4611686018427387904)` | Number of entries. |
| `getCapacity()` | table capacity | Total slots in the table: `0` for a map from `create()` until its first insert, `16` after that for a small map. |
| `createIterator()` | `MapIterator` | Throws `IterationError.exhausted` when empty. |

`MapIterator` implements `Iterator with (Key, Value)`: `current()` and `advance()`.

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
| `count()` | `int(0 to 4611686018427387904)` | Number of elements. |
| `getCapacity()` | table capacity | Total slots in the table. |
| `createIterator()` | `SetIterator` | Throws `IterationError.exhausted` when empty. |

`SetIterator` implements `Iterator with Element`: `current()` and `advance()`.

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
| `Vector.create()` | `Vector` | Every element zero. |
| `Vector from [a, b, c]` | `Vector` | A literal; the count is the literal's length. |
| `count()` | `int(0 to u64.max)` | The fixed size, answered from the type. |
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
	var sum = 0

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
	var total = 0

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
| `File.readText(path FilePath)` | `String` | `FileReadError` | The whole file as text. |
| `File.readBinary(path FilePath)` | `ByteArray` | `FileReadError` | The whole file as bytes. |
| `File.writeText(path FilePath, content String, mode FilePermission = .normal)` | — | `FileWriteError` | Create or truncate, then write. |
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
| `Byte` | `int(0 to u8.max)` |
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

| Error enum | Case | Thrown when |
|------------|------|-------------|
| `FileReadError` | `notFound` | The file cannot be opened or read |
| `FileWriteError` | `failed` | The file cannot be created or written |
| `FileDeleteError` | `notFound` | The file cannot be deleted |
| `FileRenameError` | `failed` | The rename fails |
| `FileInfoError` | `notFound` | The path does not exist |

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
elsewhere) and accepts `file://` URLs, which are converted to paths. It implements `Equatable`, `Hashable`,
`Stringable` and `InitableFromStringLiteral`. Equality and hashing follow the host filesystem:
case-insensitive on Windows, byte-exact elsewhere.

### Construction

| Member | Returns | Description |
|--------|---------|-------------|
| `FilePath from "a/b.txt"` | `FilePath` | From a literal; an invalid path panics. |
| `FilePath.from(path String)` | `FilePath` | Throws `FilePathError.invalidCharacter` on Windows for a control character or one of `< > " \| ? *`, or `notFileURL` for a URL whose scheme is not `file`. |
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
| `equals(other FilePath)`, `hash()` | | Host filesystem semantics. |

```maxon
enum FilePathError implements Error
	invalidCharacter
	notFileURL
	noParent
end 'FilePathError'
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
| `Directory.list(path FilePath)` | `Array with FilePath` | `DirectoryListError` | The entries of a directory, each joined onto `path`. |
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
| `readLine()` | `String` | `ConsoleError.endOfFile` | The next line without its `\n`; a `\r` before it is removed too. A last line with no terminator is returned, and the call after it throws. |

```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	var lines = 0

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

## Log

`Log` records trace keys so a test can check which internal path ran. It is not a general logger: there are
no levels or outputs. While capture is off, `trace` does nothing.

Nothing in the standard library calls `trace`, so a capture holds only the keys the program emitted itself.
`Log` keeps its capture state in module-level `var`s, so a service message handler may not call any of
these methods ([E3143](../maxon-bin/Compiler/ErrorCodeRegistry.maxon#e3143)).

| Method | Returns | Description |
|--------|---------|-------------|
| `Log.trace(key String)` | — | Record `key` when capturing. Use a stable dotted name. |
| `Log.startCapture()` | — | Start recording, discarding earlier keys. |
| `Log.stopCapture()` | `StringArray` | Stop, and return the keys in the order they were emitted. |
| `Log.fired(capturedKeys StringArray, key String)` | `bool` | True when `key` was recorded at least once. |

```maxon
function main() returns ExitCode
	Log.startCapture()
	Log.trace("cache.miss")
	let keys = Log.stopCapture()
	print("{Log.fired(keys, key: "cache.miss")} {Log.fired(keys, key: "cache.hit")}\n")
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

On POSIX a `name` is searched for on `PATH` alone. On Windows the search follows `CreateProcessA`'s
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
| `bytes(data)` | `data`, then end of input |
| `file(path)` | The file's contents |
| `hold` | A pipe that stays open and silent until the child exits, so a read blocks |
| `delayed(data)` | Like `hold` until about one second after the child's first stdout byte, then `data` and end of input. Requires `standardOutput` to be `collect`; any other pairing throws `spawnFailed` before spawning. |

| OutputDestination | The stream |
|-------------------|------------|
| `discard` | Dropped |
| `inherit` | Passed through to this process's stream |
| `collect(limitBytes)` | Captured into `CollectedOutput`, truncated at the `limitBytes` limit |
| `file(path)` | Written to a file |

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

`exited` is how every child's end is reported: a child killed by a Unix signal exits with `128 + signal`, and
a Windows child that ended abnormally exits with its NTSTATUS code. No target currently reports `signalled`. `isSuccess()` is true for `exited(0)`; `code()` returns
the number either way.

### SubprocessError

```maxon
union SubprocessError implements Error
	executableNotFound(name String)
	spawnFailed(reason String)
	ioFailed(reason String)
	timeout(elapsedMs DurationMs, stdout String, stderr String)
	inputTooLarge
end 'SubprocessError'
```

`executableNotFound` is thrown on every target when the executable does not exist: a bare name no search
finds, or an `Executable.path` naming a missing file. `spawnFailed` is any other refusal to start the child —
a `file` stream that cannot be opened and a `workingDirectory` that does not exist included, though either
fails with a not-found code; its reason carries the OS error number (`os error 5`).

`timeout` carries the output the child had already produced when the deadline killed it, since that partial
text is usually the only evidence of why it hung. Both fields are empty when the layer that threw was not
collecting output, which is `StreamingSubprocess.waitWithTimeout`.

`displayReason()` renders any case as one line, such as
`timed out after 5000ms, and the kill was sent to the child's whole process tree`.

### StreamingSubprocess

A child whose standard streams stay open as pipes the caller drives, for a long-lived worker that answers
request after request. A read parks the calling green thread until data arrives.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `StreamingSubprocess.spawn(executable, arguments:)` | `StreamingSubprocess` | `SubprocessError` | Spawn in the parent's working directory. |
| `StreamingSubprocess.spawnWithCwd(executable, arguments:, workingDirectory:)` | `StreamingSubprocess` | `SubprocessError` | With a working directory. |
| `StreamingSubprocess.spawnWithEnvironment(executable, arguments:, workingDirectory:, environment Environment)` | `StreamingSubprocess` | `SubprocessError` | With a working directory (empty for the parent's) and an environment. |
| `writeStdinLine(line String)` | — | `SubprocessError` | Write `line` and a newline. Throws on a broken pipe. |
| `readStdoutLine()` | `String` | `SubprocessError` | The next line without its terminator (CRLF or LF). `""` means end of stream. Lines over 1 MiB arrive in pieces. |
| `readStdoutLineCapped(maxBytes)` | `String` | `SubprocessError` | With an explicit per-call cap. |
| `readStdoutBytes(count)` | `String` | `SubprocessError` | Exactly `count` bytes, fewer only at end of stream; nothing is stripped. For length-framed protocols. Shares a buffer with the line readers. |
| `readStderrLine()` | `String` | `SubprocessError` | As `readStdoutLine`, for stderr. |
| `readStderrLineCapped(maxBytes)` | `String` | `SubprocessError` | With a cap. |
| `tryReadStdoutLine()` | `LinePoll` | — | A line if one is already buffered; never blocks. |
| `tryReadStderrLine()` | `LinePoll` | — | As above, for stderr. |
| `pollExit()` | `ExitPoll` | — | Whether the child has exited, without blocking or killing it. A released handle answers `running`. |
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

	let line = try child.readStdoutLine() otherwise ""
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

A `SharedSegment` is a named block of memory that several processes map at once: one creates it under a
name, another maps the same name and sees the same bytes. Available on `x64-windows` (Win32 section
objects), `arm64-macos` (`shm_open` and `mmap`) and both Linux targets (a `/dev/shm` file and `mmap`);
refused at compile time elsewhere.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `SharedSegment.create(name String, bytes SegmentByteCount)` | `SharedSegment` | `SharedMemoryError` | Create a new section of exactly `bytes` bytes under `name` and map it. |
| `segmentName()` | `String` | — | The name another process maps it by. |
| `readWord(offset SegmentOffset)` | `SegmentWord` | `SharedMemoryError` | The 64-bit word `offset` bytes in. |
| `writeWord(offset SegmentOffset, value SegmentWord)` | — | `SharedMemoryError` | Write a 64-bit word `offset` bytes in. |
| `copyOut(offset SegmentOffset, byteCount SegmentByteCount)` | `ByteArray` | `SharedMemoryError` | An independent copy of a byte range. |
| `close()` | — | — | Unmap, release the section and withdraw its name. Idempotent. |

Offsets are in bytes, not words. Every access is checked against the section's size: a word (8 bytes) or a
`copyOut` range (`offset + byteCount`) that would reach past the end throws `SharedMemoryError.outOfBounds`
and touches nothing.

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
end 'SharedMemoryError'
```

`createFailed`: the name collides with an incompatible section, or the size cannot be backed.
`invalidName`: the name is empty, longer than 31 bytes, or holds a `/`, a `\` or a NUL byte. The rule is the
same on every target (31 bytes is macOS's limit), so a name that works on one host works on all of them.
`mapFailed`: no address space for the view. `outOfBounds`: a `readWord`, `writeWord` or `copyOut` would
reach past the end of the section.

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

## TcpClient

`TcpClient` is a TCP connection over IPv4. It closes its socket when the last reference to it goes away, so
`close()` is optional.

Available on `x64-windows`, `arm64-macos`, `arm64-linux` and `x64-linux`; refused at compile time (E3104) on
`wasm32-wasi`. Windows and macOS resolve host names through the platform resolver. The two Linux targets
link no C library and use a built-in resolver instead: it accepts a numeric address, reads `/etc/hosts`,
and otherwise sends an `A` query over TCP to the first `nameserver` in `/etc/resolv.conf`. It does not apply
`search`/`domain` suffixes, does not fall over to a second nameserver and has no timeout of its own.

| Member | Returns | Throws | Description |
|--------|---------|--------|-------------|
| `TcpClient.connect(host String, port NetworkPort)` | `TcpClient` | `NetworkError` | Resolve `host` and connect. `resolveFailed` or `connectFailed`. |
| `TcpClient.adopt(socket)` | `TcpClient` | — | Wrap an already-connected socket; how `TcpListener.accept()` returns its connections. |
| `send(data String)` | `int(0 to u64.max)` | `NetworkError` | Send every byte, looping over partial sends; returns the byte count. |
| `recv(bufferSize int(0 to u64.max))` | `String` | `NetworkError` | Up to `bufferSize` bytes. Throws `connectionClosed` when the peer has closed. |
| `setReadDeadline(milliseconds int(0 to 4294967295))` | — | `NetworkError` | Make a read that waits longer than this throw `timedOut`. Measured from this call; `0` clears it. |
| `setWriteDeadline(milliseconds int(0 to 4294967295))` | — | `NetworkError` | The same for sends. |
| `close()` | — | — | Close the connection. Idempotent. |

A deadline setter throws `connectionClosed` when the socket is already closed.

`NetworkPort` is `int(0 to 65535)`. Port `0` asks `TcpListener.bind` for any free port; it is never the port
of a live connection.

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
end 'NetworkError'
```

| Case | Meaning |
|------|---------|
| `resolveFailed` | The host name did not resolve |
| `connectFailed` | The connection was refused or could not be made |
| `sendFailed`, `recvFailed` | The OS reported an error |
| `connectionClosed` | The peer closed the connection |
| `timedOut` | A read or write deadline expired |
| `bindFailed` | The address or port could not be bound |
| `acceptFailed` | No connection could be accepted |

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
| `accept()` | `TcpClient` | `NetworkError` | The next connection. |
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
	let server = async echoOnce(listener)

	let client = try TcpClient.connect("127.0.0.1", port: listener.port()) otherwise return 2
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

An HTTP/1.1 client over `TcpClient`. Every request is sent with `Connection: close` and the response is read
until the server closes the connection.

Limitations: plain HTTP only (no TLS) — a URL whose scheme is anything but `http`, `https` included, throws
`HttpError.unsupportedScheme` before any connection is attempted; no chunked transfer decoding, no redirect following (a 3xx is
returned as is), and the whole response is held in memory. The URL's port defaults to 80.

### HttpClient

| Method | Returns | Description |
|--------|---------|-------------|
| `HttpClient.send(request HttpRequest)` | `HttpResponse` | Send a request. |
| `HttpClient.get(url String)` | `HttpResponse` | `GET`. |
| `HttpClient.post(url String, body String)` | `HttpResponse` | `POST` with a body. |
| `HttpClient.put(url String, body String)` | `HttpResponse` | `PUT` with a body. |
| `HttpClient.delete(url String)` | `HttpResponse` | `DELETE`. |

All throw `HttpError`.

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
| `HttpMethod` | `get`, `post`, `put`, `delete`, `head`, `patch` |
| `HttpError` | `invalidUrl`, `connectFailed`, `sendFailed`, `recvFailed`, `invalidResponse`, `unsupportedScheme` |
| `StatusCode` | `ok` 200, `created` 201, `noContent` 204, `movedPermanently` 301, `found` 302, `notModified` 304, `badRequest` 400, `unauthorized` 401, `forbidden` 403, `notFound` 404, `methodNotAllowed` 405, `conflict` 409, `gone` 410, `internalServerError` 500, `notImplemented` 501, `badGateway` 502, `serviceUnavailable` 503 |

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
	let server = async serveOnce(listener)

	let response = try HttpClient.get("http://127.0.0.1:{listener.port()}/hello") otherwise (e) 'failed'
		print("request failed: {e}\n")
		return 1
	end 'failed'

	_ = try await server otherwise 9
	let demo = try response.header("x-demo") otherwise "absent"
	print("{response.statusCode()} {response.reason()} {response.body()} {demo}\n")
	return 0
end 'main'
```

Output: `200 OK hi yes`.

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
| `invalidHost` | A malformed host, such as an unclosed IPv6 bracket |
| `invalidPort` | A port that is not a number or exceeds 65535 |
| `invalidEncoding` | Malformed percent-encoding, such as `%GG` |
| `relativeWithoutBase` | `resolve` was given a base with no scheme |
| `fieldNotPresent` | An accessor was called for a component the URL does not have |

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
| `Json.parse(text String)` | `JsonDoc` | `JsonError` | Parse a document; `doc.root` is the top-level value. |
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
| `getString(parent, key:)` | `String` | `JsonAccessError` | A string member; `wrongType` for another kind. |
| `getInt(parent, key:)` | `int(i64.min to i64.max)` | `JsonAccessError` | A number member, truncated toward zero. |
| `getBool(parent, key:)` | `bool` | `JsonAccessError` | A boolean member. |
| `arrayLength(id JsonNodeId)` | `int(0 to u64.max)` | `JsonAccessError` | `notArray` for another kind. |
| `arrayAt(id JsonNodeId, index)` | `JsonNodeId` | `JsonAccessError` | `outOfBounds` past the end. |

### JsonNode

| Member | Description |
|--------|-------------|
| `kind` | `JsonKind` |
| `boolValue` | Set for `jsonBool` |
| `numberValue` | Set for `jsonNumber` (a `float`) |
| `stringValue` | Set for `jsonString` |
| `children` | `JsonNodeIdArray`, for `jsonArray` and `jsonObject` |
| `keys` | `StringArray`, parallel to `children`, for `jsonObject` |
| `JsonNode.nullNode()` | A `null` |
| `JsonNode.boolNode(value bool)` | A boolean |
| `JsonNode.numberNode(value float)` | A number |
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

## Sha256

| Function | Returns | Description |
|----------|---------|-------------|
| `sha256(data ByteArray)` | `ByteArray` | The 32-byte SHA-256 digest (FIPS 180-4). |

```maxon
function main() returns ExitCode
	let digest = sha256("abc".toByteArray())
	var hex = ""

	for b in digest 'each'
		hex.append("{b:02x}")
	end 'each'

	print("{digest.count()} {hex}\n")
	return 0
end 'main'
```

Output: `32 ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`.

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

`since` is the first argument, so it is written without a label: `Clock.elapsedNanos(start)`.

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
| `WallClock.nowUnixSeconds()` | `UnixSeconds` | Whole seconds since 1970-01-01 00:00:00 UTC. No time zone is applied. |

`UnixSeconds` is `int(0 to u64.max)`. The wall clock can step backwards (an NTP correction, a resumed
virtual machine), so never measure a duration with it.

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

## Scheduler

`Scheduler` holds controls over the green-thread scheduler. It is a namespace with no fields.

| Method | Description |
|--------|-------------|
| `Scheduler.yield()` | Let the next runnable green thread run. The caller resumes behind everything that was already runnable. When nothing else is runnable it returns promptly, so a loop that yields is a busy wait that lets others progress. It uses no timer, unlike `sleep(0)`, and is safe in a program that never starts a green thread. |
| `Scheduler.processorCount()` | The number of processors the scheduler runs services on, as a `SchedulerProcessorCount` (`int(1 to i64.max)`): the machine's logical processor count, or the count `MAXON_MAX_PROCS` sets, clamped to between 1 and the machine's count. The count is resolved before `main` runs, so it is the same before the first `spawn` as after it. |

Both are refused on `wasm32-wasi` (E3104). Green threads, `async` and `await` are described under Concurrency in
[LANGUAGE_REFERENCE.md](LANGUAGE_REFERENCE.md).

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

## Math

`Math` provides the elementary functions over `Real`, which is `float(f64.min to f64.max)`. The rounding and
`sqrt`/`abs`/`min`/`max` builtins are in [Core Functions](#core-functions).

| Method | Returns | Description |
|--------|---------|-------------|
| `Math.sin(x Real)` | `Real` | Sine of radians. |
| `Math.cos(x Real)` | `Real` | Cosine of radians. |
| `Math.tan(x Real)` | `Real` | Tangent of radians. |
| `Math.atan(z Real)` | `Real` | Arc tangent. |
| `Math.atan2(y Real, x Real)` | `Real` | The angle of `(x, y)`, in `[-π, π]`. |
| `Math.exp(x Real)` | `Real` | e raised to `x`. |
| `Math.log(x Real)` | `Real` | Natural logarithm. |
| `Math.log2(x Real)` | `Real` | Base-2 logarithm. |
| `Math.log10(x Real)` | `Real` | Base-10 logarithm. Exact at every power of ten a double represents exactly: `log10(10^k)` is `k` for `k` in 0 to 22. |
| `Math.pow(base Real, exponent Real)` | `Real` | `base` raised to `exponent`, following IEEE 754's special cases (`pow(0.0, -1.0)` is infinity; `pow(x, 0)` and `pow(1, y)` are 1 even for NaN). |
| `Math.hasNegativeSignBit(z Real)` | `bool` | True when the sign bit is set, including `-0.0`, which no comparison can distinguish from `0.0`. |

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
`sort()` and in generic code.

| Type | Conforms to | Notes |
|------|-------------|-------|
| `int` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `hash()` is the low 32 bits of the value. |
| `float` | `Hashable`, `Equatable`, `Comparable`, `Stringable`, `Cloneable` | `compare` is a total order: NaN equals NaN and sorts below every other value. `hash()` folds the 64-bit IEEE-754 pattern into 32 bits, `(bits xor (bits shr 32)) and 0xFFFFFFFF`, except that `-0.0` hashes as `0.0` does (`0`). |
| `bool` | `Comparable`, `Stringable`, `Cloneable` | `false` sorts before `true`. |

| Method | Returns | Description |
|--------|---------|-------------|
| `hash()` | `HashValue` | Through `Hashable`. |
| `equals(other Self)` | `bool` | Through `Equatable`. |
| `compare(other Self)` | `Ordering` | Through `Comparable`. |
| `toString()` | `String` | The same text as interpolation. |
| `clone()` | `Self` | The value itself. |
| `int.fromString(input String)` | `int` | Throws `ParseError.invalidFormat`; likewise `float.fromString`, `bool.fromString`, `byte.fromString`. |

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
| `Expect.equal(actual, expected:)` | integer, `String`, `bool` | `actual == expected` |
| `Expect.notEqual(actual, expected:)` | integer, `String`, `bool` | `actual != expected` |
| `Expect.greaterThan(actual, than:)` | integer, float | `actual > than` |
| `Expect.lessThan(actual, than:)` | integer, float | `actual < than` |
| `Expect.atLeast(actual, than:)` | integer, float | `actual >= than` |
| `Expect.atMost(actual, than:)` | integer, float | `actual <= than` |
| `Expect.close(actual, expected:, within:)` | float | `abs(actual - expected) <= within` |
| `Expect.isTrue(actual)` | `bool` | `actual` is `true` |
| `Expect.isFalse(actual)` | `bool` | `actual` is `false` |
| `Expect.contains(haystack, needle:)` | `String` | `haystack` contains `needle` |
| `Expect.startsWith(haystack, needle:)` | `String` | prefix match |
| `Expect.endsWith(haystack, needle:)` | `String` | suffix match |
| `Expect.isEmpty(haystack)` | `String` | no characters |
| `Expect.fail(message)` | — | never; fails unconditionally |

Each name is one overload set chosen by argument type, including through a method call
(`Expect.equal(parts.count(), expected: 3)`) and through an enum's `name`, `ordinal` and `rawValue`.
`String` values are quoted in the report, so empty or space-padded values stay visible.

Floats have no `equal`: exact float equality can pass on one target and fail on another, so
`Expect.equal(1.5, expected: 1.5)` does not compile. Use `close(…, within:)`. NaN satisfies no comparison,
so it fails every float matcher.

For any other `Equatable` and `Stringable` type, `isTrue` is the general form:
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
| `Build.build(source String, output String = "", debugInfo bool = true, version String = "", defines StringArray = empty)` | — | Describe one source file or directory compiled to one output. An empty `output` builds `.maxon/<name>`, `<name>` being the project or task file's own name. |
| `Build.buildWithConfig(config BuildConfig)` | — | Describe one full `BuildConfig`, which may list several sources. |
| `Build.delegate(directory String, target String = "")` | — | Describe a build by handing it to a target of `directory`'s own `.maxproj` file, run with that directory as its working directory. `target` names one of its targets as `maxon build` does, each `_` written `-`; empty means its sole one. |
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

`BuildConfig.create(sources StringArray, output String = "", debug_info bool = true, version String = "",
defines StringArray = empty, directory String = "", delegateTarget String = "")` builds one.

`defines` is how a project file puts something it computed into the binary, such as a version derived
from git. A define whose name matches no constant, or more than one, is refused (E3149, E3150), as is a
constant whose initializer is something other than a plain string literal (E3151). A `--define` on the
command line wins over the described build's.
