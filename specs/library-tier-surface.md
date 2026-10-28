---
feature: library-tier-surface
status: stable
keywords: [library tier, raw memory, __Builtins, __ManagedMemory, managed, cstring, cstr, fromCString, Promise, inner, __ManagedFile, __ManagedDirectory, __ManagedSocket, E3185, stdlib, runtime]
category: memory-safety
---

# The raw surface is library-tier

## Documentation

Some of what the compiler offers reaches memory without the ownership model standing between the program
and the bytes: a runtime intrinsic, a container's raw buffer, a NUL-terminated pointer, a promise's
green-thread handle. The standard library is built on them, and they are usable ONLY from a source file
under `stdlib/` or `runtime/`. Every other file — a program, a test project, the compiler's own sources —
reaches the same facilities through the safe standard-library API, and naming the raw surface there is
**E3185**. There is no opt-in that lifts it.

| Raw surface | What a program uses instead |
|---|---|
| any `__Builtins.<member>(…)` call | the stdlib API over it — `Process.id()`, `Clock.nowNanos()`, `HeapStats.*`, `Scheduler.*`, `Math.bitsOf`, `Stdin.readBytes`, … |
| `value.managed` | the `Array` methods |
| the `__ManagedMemory` and `__ManagedMemoryCursor` types, their statics, and a method called on such a value | `Array`, `ByteArray`, `String` |
| `String.cstr()` and the `cstring` type | no public replacement |
| `promise.inner` | `promise.isComplete()`, or `await` |
| the `__ManagedFile`, `__ManagedDirectory`, `__ManagedSocket`, `__ManagedPages` and `__ManagedHandle` handle types and their statics | `File`, `Directory`, `TcpClient` and `TcpListener`; `DebugPort`, `ProcessTrace` and `SampleTimer` |

The refusal names the door and, where one exists, the replacement:

```text
call to '__Builtins.<member>' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — use '<replacement>'
'.<member>' read from outside the library tiers: <what it reaches> is reachable only from a source file under stdlib/ or runtime/ — <replacement>
'<type>' named outside the library tiers: <what it is> is nameable only from a source file under stdlib/ or runtime/ — <replacement>
```

A surface with no public replacement says `it has no public replacement`. A static called on a raw type
is refused at the type it is called on, in the type's form. A body reports its first raw door, and every
other body and signature of the file reports its own.

`String.fromCString(…)` takes a `cstring`, which only the library tiers can produce, so it is `module`-scoped: a
program that calls it is refused at the call with E3088, whatever it passes, and it is not part of the E3185 surface.
A program that names `cstring` or calls `.cstr()` is refused at that door first, ahead of the `fromCString` call.

`__Raw` stays runtime-only (E3152), which is narrower: a `stdlib/` file may not name it either.

A spec case whose SUBJECT is the raw surface itself carries `<!-- tier: stdlib -->`, which compiles it as
a file under `stdlib/`. A refusal below is paired with such a twin wherever the same program means something in the
library tier: the same program, compiled there, builds and runs. The `fromCString` refusal has none, because the
library tier calls `fromCString` in every `cstr()` twin above.

## Tests

<!-- test: error.a-builtins-call-is-refused-outside-the-library-tiers -->
A `__Builtins` call outside `stdlib/` and `runtime/` is refused at the member it names, with the stdlib
API that replaces it.
```maxon
function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	print("{pid > 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:23: call to '__Builtins.currentProcessId' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — use 'Process.id()'
```

<!-- test: a-builtins-call-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	print("{pid > 0}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.a-scalar-builtins-member-is-refused-too -->
The whole namespace is library-tier, not only the members that hand out memory: a clock read is refused the
same way, and the hint names the clock that replaces it.
```maxon
function main() returns ExitCode
	let now = __Builtins.currentTimeNanos()
	print("{now > 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:23: call to '__Builtins.currentTimeNanos' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — use 'Clock.nowNanos()'
```

<!-- test: a-scalar-builtins-member-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	let now = __Builtins.currentTimeNanos()
	print("{now > 0}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.a-managed-read-is-refused-outside-the-library-tiers -->
`.managed` hands out an array's raw buffer, whose members move elements without the ownership model's
bookkeeping. It is refused at the member.
```maxon
typealias Small = int(0 to 255)
typealias SmallArray = Array with Small

function main() returns ExitCode
	var xs = SmallArray.create()
	xs.push(10)
	xs.push(20)
	xs.push(30)
	try xs.managed.shiftRight(1, 1) otherwise return 98
	for x in xs 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:10:9: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: a-managed-read-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier. `shiftRight(1, 1)` releases slot 2, slides slot 1 up into it and zeroes slot 1;
the length stays 3.
```maxon
typealias Small = int(0 to 255)
typealias SmallArray = Array with Small

function main() returns ExitCode
	var xs = SmallArray.create()
	xs.push(10)
	xs.push(20)
	xs.push(30)
	try xs.managed.shiftRight(1, 1) otherwise return 98
	for x in xs 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
10
0
20
```
```exitcode
0
```

<!-- test: error.a-promise-inner-read-is-refused-outside-the-library-tiers -->
`.inner` is a promise's raw green-thread handle. A program asks `isComplete()` instead, or awaits.
```maxon
typealias Integer = int(i64.min to i64.max)

function work() returns Integer
	Scheduler.yield()
	return 7
end 'work'

function main() returns ExitCode
	let p = async work()
	let handle = p.inner
	print("{handle > 0}\n")
	return (await p) as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:11:17: '.inner' read from outside the library tiers: a promise's raw green-thread handle is reachable only from a source file under stdlib/ or runtime/ — use 'isComplete()' or 'await'
```

<!-- test: a-promise-inner-read-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)

function work() returns Integer
	Scheduler.yield()
	return 7
end 'work'

function main() returns ExitCode
	let p = async work()
	let handle = p.inner
	print("{handle > 0}\n")
	return (await p) as ExitCode
end 'main'
```
```stdout
true
```
```exitcode
7
```

<!-- test: error.a-cstr-call-is-refused-outside-the-library-tiers -->
`String.cstr()` hands out a pointer into the String's bytes that outlives nothing the ownership model can
see. It is refused, and it has no public replacement.
```maxon
function main() returns ExitCode
	let s = "hello {40 + 2}"
	let back = String.fromCString(s.cstr())
	print("{back}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:34: '.cstr()' read from outside the library tiers: a String's raw NUL-terminated 'cstring' view is reachable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: a-cstr-call-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	let s = "hello {40 + 2}"
	let back = String.fromCString(s.cstr())
	print("{back}\n")
	return 0
end 'main'
```
```stdout
hello 42
```
```exitcode
0
```

<!-- test: error.a-managed-memory-type-is-refused-outside-the-library-tiers -->
Naming the buffer type in a declaration is refused at the type, before any value of it exists.
```maxon
typealias Integer = int(i64.min to i64.max)

function bufferLength(m __ManagedMemory) returns Integer
	return m.length()
end 'bufferLength'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:25: '__ManagedMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: a-managed-memory-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)

function bufferLength(m __ManagedMemory) returns Integer
	return m.length()
end 'bufferLength'

function main() returns ExitCode
	let bytes = "abcd".toByteArray()
	print("{bufferLength(bytes.managed)}\n")
	return 0
end 'main'
```
```stdout
4
```
```exitcode
0
```

<!-- test: error.a-managed-memory-static-is-refused-outside-the-library-tiers -->
A static called on the raw buffer type is refused at the type it is called on, in the type's form.
```maxon
function main() returns ExitCode
	var buffer = try __ManagedMemory.create(16, 1) otherwise return 1
	print("{buffer.length()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:19: '__ManagedMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: a-managed-memory-static-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	var buffer = try __ManagedMemory.create(16, 1) otherwise return 1
	print("{buffer.length()}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: error.a-cstring-type-is-refused-outside-the-library-tiers -->
The `cstring` type is refused the same way.
```maxon
typealias Integer = int(i64.min to i64.max)

function lengthOf(c cstring) returns Integer
	return String.fromCString(c).byteLength()
end 'lengthOf'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:21: 'cstring' named outside the library tiers: the raw NUL-terminated byte pointer type is nameable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: a-cstring-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)

function lengthOf(c cstring) returns Integer
	return String.fromCString(c).byteLength()
end 'lengthOf'

function main() returns ExitCode
	let s = "abc"
	print("{lengthOf(s.cstr())}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: error.a-user-call-to-string-from-cstring-is-refused -->
`String.fromCString` takes a `cstring`, which only the library tiers can produce, so it is declared
`module`-scoped: a program calling it is refused at the call (E3088), whatever it passes.
```maxon
function main() returns ExitCode
	let back = String.fromCString(0)
	print("{back}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3088: <fragment>:3:20: function 'String.fromCString' is module-scoped and not visible from this directory
```

<!-- test: error.the-cursor-type-is-refused-outside-the-library-tiers -->
`__ManagedMemoryCursor` is the raw buffer's cursor, which reads a slot without the `for` loop's count of
its source. Naming it is refused at the type.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntCursor = __ManagedMemoryCursor with Integer
typealias IntArray = Array with Integer

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:23: '__ManagedMemoryCursor' named outside the library tiers: the raw buffer cursor type is nameable only from a source file under stdlib/ or runtime/ — use a 'for' loop over the 'Array'
```

<!-- test: the-cursor-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntCursor = __ManagedMemoryCursor with Integer
typealias IntArray = Array with Integer

function firstOf(c IntCursor) returns Integer
	return c.current()
end 'firstOf'

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	let c = try xs.managed.createCursor() otherwise return 1
	print("{firstOf(c)}\n")
	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: error.a-cursor-member-is-refused-outside-the-library-tiers -->
A method called on a raw cursor is a door of its own. Here it is the first door in the file: the call
sits in `main`, above the function that hands the cursor out and the alias that names its type.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	let c = cursorOf(xs)
	print("{c.current()}\n")
	return 0
end 'main'

function cursorOf(xs IntArray) returns IntCursor
	return try xs.managed.createCursor() otherwise panic("empty")
end 'cursorOf'

typealias IntCursor = __ManagedMemoryCursor with Integer
```
```maxoncstderr
error E3185: <fragment>:9:12: '.current()' read from outside the library tiers: a raw '__ManagedMemoryCursor' is reachable only from a source file under stdlib/ or runtime/ — use a 'for' loop over the 'Array'
error E3185: <fragment>:14:16: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
error E3185: <fragment>:17:23: '__ManagedMemoryCursor' named outside the library tiers: the raw buffer cursor type is nameable only from a source file under stdlib/ or runtime/ — use a 'for' loop over the 'Array'
```

<!-- test: a-cursor-member-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	let c = cursorOf(xs)
	print("{c.current()}\n")
	return 0
end 'main'

function cursorOf(xs IntArray) returns IntCursor
	return try xs.managed.createCursor() otherwise panic("empty")
end 'cursorOf'

typealias IntCursor = __ManagedMemoryCursor with Integer
```
```stdout
7
```
```exitcode
0
```

<!-- test: error.a-buffer-member-is-refused-outside-the-library-tiers -->
A method called on a raw buffer is refused at the method, however the program came to hold the buffer.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	xs.push(8)
	let m = bufferOf(xs)
	print("{m.length()}\n")
	return 0
end 'main'

function bufferOf(xs IntArray) returns IntBuffer
	return xs.managed
end 'bufferOf'

typealias IntBuffer = __ManagedMemory with Integer
```
```maxoncstderr
error E3185: <fragment>:10:12: '.length()' read from outside the library tiers: a raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
error E3185: <fragment>:15:12: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
error E3185: <fragment>:18:23: '__ManagedMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: a-buffer-member-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	xs.push(8)
	view m = bufferOf(xs)
	print("{m.length()}\n")
	return 0
end 'main'

function bufferOf(xs IntArray) returns IntBuffer
	return xs.managed
end 'bufferOf'

typealias IntBuffer = __ManagedMemory with Integer
```
```stdout
2
```
```exitcode
0
```

<!-- test: error.an-async-builtins-call-is-refused-outside-the-library-tiers -->
`async` is a second call door, and it asks the same question at the member it names.
```maxon
function main() returns ExitCode
	let p = async __Builtins.currentProcessId()
	print("{(await p) > 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:27: call to '__Builtins.currentProcessId' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — use 'Process.id()'
```

<!-- test: an-async-builtins-call-in-the-library-tier-is-not-a-spawnable-function -->
<!-- tier: stdlib -->
In the library tier the door lets the call through, and the spawn then finds no function to start: an
intrinsic is lowered in place and has no address a green thread could begin at.
```maxon
function main() returns ExitCode
	let p = async __Builtins.currentProcessId()
	print("{(await p) > 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:27: Unsupported: `async __Builtins.currentProcessId(…)` — `__Builtins.currentProcessId` is a runtime intrinsic the compiler lowers in place, so it names no function a green thread could start at; call it directly, or spawn a function that calls it
```

<!-- test: error.a-bare-managed-in-a-user-array-extension-is-refused -->
Inside an `extension Array`, `managed` names the receiver's buffer field without a `self.`; it is the same
door as `.managed`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

extension Array
	function rawLength() returns Count
		return managed.length()
	end 'rawLength'
end 'Array'

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	xs.push(8)
	print("{xs.rawLength()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:7:10: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: a-bare-managed-in-an-array-extension-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

extension Array
	function rawLength() returns Count
		return managed.length()
	end 'rawLength'
end 'Array'

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	xs.push(8)
	print("{xs.rawLength()}\n")
	return 0
end 'main'
```
```stdout
2
```
```exitcode
0
```

<!-- test: error.the-first-raw-door-in-source-order-is-the-one-reported -->
A body reports its first raw door, and the file's other doors follow in source order. The `__Builtins` call in
`main` comes first and is the one named for that body; the `__ManagedMemory` read inside `main` is not
reported, because the body is abandoned at its first door, and the signature of `lengthOf` is reported
beside it.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	print("{pid > 0} {lengthOf("abcd".toByteArray().managed)}\n")
	return 0
end 'main'

function lengthOf(m __ManagedMemory) returns Integer
	return m.length()
end 'lengthOf'
```
```maxoncstderr
error E3185: <fragment>:5:23: call to '__Builtins.currentProcessId' from outside the library tiers: '__Builtins' names a raw runtime intrinsic, callable only from a source file under stdlib/ or runtime/ — use 'Process.id()'
error E3185: <fragment>:10:21: '__ManagedMemory' named outside the library tiers: the raw buffer type is nameable only from a source file under stdlib/ or runtime/ — use 'Array'
```

<!-- test: several-raw-doors-compile-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let pid = __Builtins.currentProcessId()
	print("{pid > 0} {lengthOf("abcd".toByteArray().managed)}\n")
	return 0
end 'main'

function lengthOf(m __ManagedMemory) returns Integer
	return m.length()
end 'lengthOf'
```
```stdout
true 4
```
```exitcode
0
```

<!-- test: error.a-managed-directory-static-is-refused-outside-the-library-tiers -->
`__ManagedDirectory`, `__ManagedFile` and `__ManagedSocket` are the runtime's OS handles, and their
statics hand out raw `__ManagedMemory` buffers. Calling one is refused at the type it is called on, with
the public API that replaces it.
```maxon
function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	print("{cwd.length() > 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:16: '__ManagedDirectory' named outside the library tiers: the raw OS directory handle type is nameable only from a source file under stdlib/ or runtime/ — use 'Directory'
```

<!-- test: a-managed-directory-static-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	print("{cwd.length() > 0}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.the-managed-directory-type-is-refused-outside-the-library-tiers -->
Naming the handle type in a signature is refused at the type.
```maxon
function nameLength(d __ManagedDirectory) returns Count
	return d.filename().length()
end 'nameLength'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:2:23: '__ManagedDirectory' named outside the library tiers: the raw OS directory handle type is nameable only from a source file under stdlib/ or runtime/ — use 'Directory'
```

<!-- test: the-managed-directory-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function nameLength(d __ManagedDirectory) returns Count
	return d.filename().length()
end 'nameLength'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```stdout
declared
```
```exitcode
0
```

<!-- test: error.a-managed-file-static-is-refused-outside-the-library-tiers -->
```maxon
function main() returns ExitCode
	let there = __ManagedFile.exists(b"no-such-file-for-this-case.bin".managed)
	print("{there}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:14: '__ManagedFile' named outside the library tiers: the raw OS file handle type is nameable only from a source file under stdlib/ or runtime/ — use 'File'
```

<!-- test: a-managed-file-static-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	let there = __ManagedFile.exists(b"no-such-file-for-this-case.bin".managed)
	print("{there}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: error.the-managed-file-type-is-refused-outside-the-library-tiers -->
A field of the handle type is refused at the field's type, the first door in the file.
```maxon
type Reader
	let file as __ManagedFile

	static function open(path ByteArray) returns Self throws __ManagedFileError
		return Self{file: try __ManagedFile.openRead(path.managed)}
	end 'open'
end 'Reader'

function opens(path ByteArray) returns bool
	_ = try Reader.open(path) otherwise return false
	return true
end 'opens'

function main() returns ExitCode
	print("{opens(b"no-such-file-for-this-case.bin")}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:14: '__ManagedFile' named outside the library tiers: the raw OS file handle type is nameable only from a source file under stdlib/ or runtime/ — use 'File'
```

<!-- test: the-managed-file-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
type Reader
	let file as __ManagedFile

	static function open(path ByteArray) returns Self throws __ManagedFileError
		return Self{file: try __ManagedFile.openRead(path.managed)}
	end 'open'
end 'Reader'

function opens(path ByteArray) returns bool
	_ = try Reader.open(path) otherwise return false
	return true
end 'opens'

function main() returns ExitCode
	print("{opens(b"no-such-file-for-this-case.bin")}\n")
	return 0
end 'main'
```
```stdout
false
```
```exitcode
0
```

<!-- test: error.a-managed-socket-static-is-refused-outside-the-library-tiers -->
```maxon
function main() returns ExitCode
	let listener = try __ManagedSocket.bindListener(b"127.0.0.1".managed, port: 0) otherwise return 1
	let port = try listener.boundPort() otherwise return 2
	print("{port > 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:21: '__ManagedSocket' named outside the library tiers: the raw OS socket handle type is nameable only from a source file under stdlib/ or runtime/ — use 'TcpClient' or 'TcpListener'
```

<!-- test: a-managed-socket-static-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier: it binds a listener on a port the kernel picks.
```maxon
function main() returns ExitCode
	let listener = try __ManagedSocket.bindListener(b"127.0.0.1".managed, port: 0) otherwise return 1
	let port = try listener.boundPort() otherwise return 2
	print("{port > 0}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.the-managed-socket-type-is-refused-outside-the-library-tiers -->
```maxon
function isLate(s __ManagedSocket) returns bool
	return s.timedOut()
end 'isLate'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:2:19: '__ManagedSocket' named outside the library tiers: the raw OS socket handle type is nameable only from a source file under stdlib/ or runtime/ — use 'TcpClient' or 'TcpListener'
```

<!-- test: the-managed-socket-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function isLate(s __ManagedSocket) returns bool
	return s.timedOut()
end 'isLate'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```stdout
declared
```
```exitcode
0
```

<!-- test: error.a-self-managed-read-in-an-array-extension-is-refused-at-the-field -->
An `extension Array` in a program file is not the standard library: `self.managed` is the raw buffer, and the refusal
lands on the field itself, not on a later member.

```maxon
extension Array
	function probe() returns bool
		let m = self.managed
		return m is self.managed
	end 'probe'
end 'Array'

function main() returns ExitCode
	let bytes = b"hello".clone()
	return 1 if bytes.probe() else 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:16: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: a-self-managed-read-in-an-array-extension-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.

```maxon
extension Array
	function probe() returns bool
		let m = self.managed
		return m is self.managed
	end 'probe'
end 'Array'

function main() returns ExitCode
	let bytes = b"hello".clone()
	return 1 if bytes.probe() else 0
end 'main'
```
```exitcode
1
```

<!-- test: error.a-self-managed-call-in-an-array-extension-is-refused-at-the-field -->
A member call on `self.managed` is refused at `managed`, the first raw door in source order, and not at the member.

```maxon
extension Array
	function rawLength() returns Count
		return self.managed.count()
	end 'rawLength'
end 'Array'

function main() returns ExitCode
	let bytes = b"hello".clone()
	return bytes.rawLength() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:15: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: error.a-bare-managed-read-in-an-array-extension-is-refused-at-the-field -->
The bare spelling of the same field.

```maxon
extension Array
	function rawLength() returns Count
		let m = managed
		return m.count()
	end 'rawLength'
end 'Array'

function main() returns ExitCode
	let bytes = b"hello".clone()
	return bytes.rawLength() as ExitCode
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:11: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: error.a-self-managed-read-in-a-string-extension-is-refused-at-the-field -->
A `String`'s buffer is the same raw surface.

```maxon
extension String
	function probe() returns bool
		let m = self.managed
		return m is self.managed
	end 'probe'
end 'String'

function main() returns ExitCode
	let s = "hello"
	return 1 if s.probe() else 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:4:16: '.managed' read from outside the library tiers: a value's raw '__ManagedMemory' buffer is reachable only from a source file under stdlib/ or runtime/ — use the 'Array' methods
```

<!-- test: an-array-extension-that-uses-only-the-public-methods-compiles -->
The twin that needs no raw surface: an extension that reads through `get`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

extension Array
	function second() returns Element throws ArrayError
		return try self.get(1)
	end 'second'
end 'Array'

function main() returns ExitCode
	var items = IntArray.create()
	items.push(3)
	items.push(9)
	let value = try items.second() otherwise return 1
	return value as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: error.a-managed-pages-static-is-refused-outside-the-library-tiers -->
```maxon
function main() returns ExitCode
	let pages = __ManagedPages.reserve(0x1000)
	print("{pages.base() != 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:14: '__ManagedPages' named outside the library tiers: the raw page-reservation handle type is nameable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: a-managed-pages-static-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function main() returns ExitCode
	let pages = __ManagedPages.reserve(0x1000)
	print("{pages.base() != 0}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```

<!-- test: error.the-managed-pages-type-is-refused-outside-the-library-tiers -->
```maxon
function isHeld(pages __ManagedPages) returns bool
	return pages.base() != 0
end 'isHeld'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:2:23: '__ManagedPages' named outside the library tiers: the raw page-reservation handle type is nameable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: the-managed-pages-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function isHeld(pages __ManagedPages) returns bool
	return pages.base() != 0
end 'isHeld'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```stdout
declared
```
```exitcode
0
```

<!-- test: error.a-managed-handle-static-is-refused-outside-the-library-tiers -->
```maxon
function main() returns ExitCode
	let timer = __ManagedHandle.timer()
	print("{timer.handle() != 0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:3:14: '__ManagedHandle' named outside the library tiers: the raw OS handle type is nameable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: error.the-managed-handle-type-is-refused-outside-the-library-tiers -->
```maxon
function isHeld(timer __ManagedHandle) returns bool
	return timer.handle() != 0
end 'isHeld'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```maxoncstderr
error E3185: <fragment>:2:23: '__ManagedHandle' named outside the library tiers: the raw OS handle type is nameable only from a source file under stdlib/ or runtime/ — it has no public replacement
```

<!-- test: the-managed-handle-type-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
The same program in the library tier.
```maxon
function isHeld(timer __ManagedHandle) returns bool
	return timer.handle() != 0
end 'isHeld'

function main() returns ExitCode
	print("declared\n")
	return 0
end 'main'
```
```stdout
declared
```
```exitcode
0
```

<!-- test: a-managed-handle-static-compiles-in-the-library-tier -->
<!-- tier: stdlib -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux -->
The same program in the library tier.
`__ManagedHandle.timer()` opens a Win32 timer, which only x64-windows provides, so the other lanes do not run the program.
```maxon
function main() returns ExitCode
	let timer = __ManagedHandle.timer()
	print("{timer.handle() != 0}\n")
	return 0
end 'main'
```
```stdout
true
```
```exitcode
0
```
