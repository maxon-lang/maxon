---
feature: console-stdin
status: stable
keywords: [console, stdin, StdinReader, StdinSource, readLine, readBytes, LineRead, eof, __Builtins, readStdin, intrinsics]
category: stdlib
---

# `Console.stdin()` — standard input through a `StreamReader`

## Documentation

`Console.stdin()` answers a `StdinReader`, the `StreamReader` over a `StdinSource` (`stream-reader.md`), so
standard input reads through the same reader as every other byte source: `readLine()` answers
`LineRead.line(text)` with the terminator stripped (`\r\n` and `\n` both) and `LineRead.ended` at EOF;
`readBytes(n)` answers exactly `n` bytes as a `ByteArray`, fewer only at EOF, and an empty array at EOF. A
read failure throws `StreamError`.

The source bottoms out in ONE compiler intrinsic, which only a source file under `stdlib/` or `runtime/` may
call (E3185 elsewhere) — the cases below that drive it directly compile in the library tier
(`<!-- tier: stdlib -->`):

| Intrinsic | Meaning |
|---|---|
| `__Builtins.readStdin(maxBytes)` | read up to `maxBytes` bytes from stdin into a FRESH owned `__ManagedMemory`; its `length()` is the count actually read, and **0 means EOF** |

`specs/builtins-type.md` states that contract in the same words — *"length reflects bytes
actually read; 0 on EOF"* — and it is what lets `StdinSource.readSome` keep `ByteSource`'s contract, an
empty answer only at the end of the stream: a NUL scan could not, because stdin is binary and a NUL is a
legal byte.

### EOF is what most of this spec pins, and that is a property of the HARNESS

A case without a `<!-- stdin: -->` marker gets the null device as its standard input, so it reads EOF on its
first syscall, deterministically, on every run. That is the half every consumer hits first — including the
compiler's own `Testing/SpecWorkerPool.maxon`, whose worker loop treats the end of stdin as *"the parent
vanished, shut down"*. A `readLine()` that answered an empty line instead of `ended` there would spin a
worker forever. `stdin-is-read-line-by-line-through-a-stream-reader` feeds one line through
`<!-- stdin: after "ready" -->`; the line splitter itself is the one `stream-reader.md` pins for every source.

### Private methods of the reader are refused

Only the reader's documented methods carry `public`; a user program naming one of the others, such as the
refill `fill()`, is refused with E3008. The refusal is pinned (`error.a-non-exported-method-is-refused`)
because it asks the visibility question of an INSTANCE METHOD. A method dispatch's callee head is the
RECEIVER'S RESOLVED TYPE, so it carries `CalleeMint.resolvedTypeQualifier` and
`SemanticCheck.mintSpellsTheWholeCallee` is what keeps it answerable — a minted callee is otherwise exempt
from the export rule, and read as "minted, therefore the compiler's" that arm would have exempted every
method call in every program.

### The end is latched

The first read that finds the end of the stream latches it in the reader, so repeated `readLine()` calls at
EOF each answer `ended` without another syscall, rather than blocking, faulting, or answering a stale buffer.
`repeat-reads-at-eof-are-idempotent` drives it three times.

### The substrate is every native lane

`__Builtins.readStdin` lowers to `__con_read_stdin`, which fetches the standard handle and reads it —
`GetStdHandle` + `ReadFile` on Windows, and on the POSIX lanes the same two ops with no handle call at all:
`osStdHandle` becomes ONE subtraction (Win32's three selectors and POSIX's three descriptors are
consecutive runs) and the read is `read(2)`. It shares the `TargetFacilities` gate with the file,
directory and command-line families, so a program that reaches it on wasm32-wasi is refused with `E3104`
at the span of the call the program wrote rather than panicking inside a backend — see
`Compiler/Runtime/ConsoleRuntime.maxon`'s header for why wasm32-wasi has no `fd_read` lowering: a
component holds an input-stream RESOURCE, which is neither a handle nor a descriptor, so there is no
arithmetic that produces one.

## Tests

<!-- test: console-stdin.read-line-at-eof-answers-ended -->
The first `readLine()` on a closed stdin answers `LineRead.ended`. If it instead answered a line — the
empty line a NUL-scanned length would produce — the program returns 1, and a read that threw returns 2.
```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	let read = try stdin.readLine() otherwise return 2

	return match read 'read'
		ended gives 9
		line or
		pastTheCap gives 1
	end 'read'
end 'main'
```
```exitcode
9
```

<!-- test: console-stdin.error.a-non-exported-method-is-refused -->
⭐ **THE CASE THAT ASKS VISIBILITY OF AN INSTANCE METHOD.** The reader's refill `fill()` carries no
`public`, and a user program holding a `StdinReader` is refused it — the fact the section above states. It
is pinned here because the method-dispatch door is a different route into `SemanticCheck.calleeVisibleFrom`
than the free function and static every other E3008 case takes, and nothing else would notice if that route
stopped asking.
```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	try stdin.fill() otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E3008: <fragment>:4:12: function 'StreamReader.fill' is not exported
```

<!-- test: console-stdin.a-second-reader-also-sees-eof -->
A reader's state is PER INSTANCE: a second `Console.stdin()` is a fresh reader with its own empty buffer and
its own latched end, so it asks the OS itself and reaches the same answer. Bytes one reader has buffered are
that reader's alone, and the half a closed stdin can reach is that the second construction neither shares
the first reader's state nor faults on a spent stream.
```maxon
function atEnd(reader StdinReader) returns bool
	let read = try reader.readLine() otherwise return false

	return match read 'read'
		ended gives true
		line or
		pastTheCap gives false
	end 'read'
end 'atEnd'

function main() returns ExitCode
	let first = Console.stdin()

	if not atEnd(first) 'firstRead'
		return 2
	end 'firstRead'

	let second = Console.stdin()

	if not atEnd(second) 'secondRead'
		return 1
	end 'secondRead'

	return 3
end 'main'
```
```exitcode
3
```

<!-- test: console-stdin.repeat-reads-at-eof-are-idempotent -->
The reader latches the end once a read has found it, so the second and third `readLine()` answer `ended`
exactly as the first did. A reader that re-issued the read would still be correct here; one that answered a
stale buffer, or that stopped answering `ended`, would not.
```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	var endings = 0 as Count
	var i = 0 as Count

	while i < 3 'again'
		let read = try stdin.readLine() otherwise return 2

		match read 'read'
			ended then endings = endings + 1
			line or
			pastTheCap then return 1
		end 'read'

		i = i + 1
	end 'again'

	return endings as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: console-stdin.read-bytes-answers-an-empty-array-at-eof -->
`readBytes(n)` at EOF answers an EMPTY array, not a failure and not a 1-byte NUL buffer. A capacity
leaking into the count would answer 64.
```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	let bytes = try stdin.readBytes(64) otherwise return 2
	if bytes.count() == 0 'eof'
		return 5
	end 'eof'
	return 1
end 'main'
```
```exitcode
5
```

<!-- test: console-stdin.read-bytes-result-is-owned -->
Each call answers a FRESH owned `ByteArray`, dropped at the end of the iteration that bound it. A
hundred of them in a loop is the leak gate's shape: a missing drop is a non-zero mm balance at exit,
which the runtime reports as exit 101 rather than as a wrong number.
```maxon
function main() returns ExitCode
	let stdin = Console.stdin()
	var total = 0 as Count
	var i = 0 as Count
	while i < 100 'reads'
		let bytes = try stdin.readBytes(4096) otherwise return 2
		total = total + bytes.count()
		i = i + 1
	end 'reads'
	return (total + 6) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: console-stdin.builtin-takes-a-ranged-count -->
<!-- tier: stdlib -->
The byte budget may be spelled with a RANGED typealias, which carries the `named` tag until
TypeResolution collapses it. The narrow `tag == integer` test would refuse this argument by naming
one type twice; the operand check is `tagIsIntegral`, which is the correct predicate for
every builtin argument door.
```maxon
typealias ByteBudget = int(0 to 65535)

function main() returns ExitCode
	let budget = 4096 as ByteBudget
	let mm = __Builtins.readStdin(budget)
	if mm.length() == 0 'eof'
		return 8
	end 'eof'
	return 1
end 'main'
```
```exitcode
8
```

<!-- test: console-stdin.builtin-arity-checked -->
<!-- tier: stdlib -->
`readStdin` takes exactly one argument. An intrinsic has no signature for the ordinary arity check
to read, so it is refused by the same `builtinArity` check `trunc`/`sleep`/`commandLineArg` use.
```maxon
function main() returns ExitCode
	return __Builtins.readStdin() as ExitCode
end 'main'
```
```maxoncstderr
error E3036: <fragment>:3:20: '__Builtins.readStdin' takes exactly 1 argument, but 0 were given
```

<!-- test: console-stdin.builtin-operand-type -->
<!-- tier: stdlib -->
The byte budget is a COUNT. A String argument is refused at the call site rather than reaching a
runtime that would read a record pointer as a length.
```maxon
function main() returns ExitCode
	return __Builtins.readStdin("lots") as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:3:20: '__Builtins.readStdin' requires an argument of type int, but its argument is String
```

<!-- test: console-stdin.rejected-on-wasm -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The stdin substrate is every native lane. On wasm32-wasi a program reaching it is refused with `E3104` at
the stdlib call it wrote, naming the runtime entry that has no lowering there — never a panic from inside the
wasm backend. The program is blamed at `Console.stdin`, the call that builds the reader over the entry.
```maxon
function main() returns ExitCode
	let input = Console.stdin()
	let bytes = try input.readBytes(64) otherwise return 2
	return bytes.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3104: <fragment>:3:22: 'Console.stdin' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: stdin-is-read-line-by-line-through-a-stream-reader -->
<!-- stdin: after "ready" -->
`Console.stdin()` answers a `StdinReader`, so standard input reads through the same line reader as every other source: the harness feeds one line once the program prints `ready` and closes the pipe, and the reader answers that line without its terminator, then `ended`.
```maxon
function shown(read LineRead) returns String
	return match read 'read'
		line(text) gives text
		ended gives "ended"
		pastTheCap gives "pastTheCap"
	end 'read'
end 'shown'

function main() returns ExitCode
	let input = Console.stdin()
	print("ready\n")
	let first = shown(try input.readLine() otherwise return 2)
	let second = shown(try input.readLine() otherwise return 3)
	print("{first}|{second}\n")
	return 0
end 'main'
```
```stdout
ready
hello|ended
```
```exitcode
0
```

<!-- test: rejected-on-wasm.a-stream-reader-over-a-library-source-built-in-user-code -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library source whose `readSome` reads stdin, wrapped in a `StreamReader` by the user's own call, reaches `__con_read_stdin` through the reader's witness. On wasm32-wasi that is refused with `E3104` at the user's call, as when a library factory builds the reader, never a panic from inside the wasm backend.
```maxon
// --- stdlib-overlay: Console.maxon
public type ZSrc implements ByteSource
	public static function create() returns ZSrc
		return Self{}
	end 'create'

	public function readSome(most ReadCapacity) returns isolated ByteArray throws StreamError
		return ByteArray.init(__Builtins.readStdin(most))
	end 'readSome'
end 'ZSrc'
// --- file: main.maxon
function main() returns ExitCode
	let reader = StreamReader.over(ZSrc.create())
	let read = try reader.readLine() otherwise return 2

	match read 'read'
		line(text) then print("{text}\n")
		ended then print("ended\n")
		pastTheCap then print("pastTheCap\n")
	end 'read'

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:14:28: 'StreamReader.over' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:14:38: 'ZSrc.create' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-library-enum-widened-to-an-interface-reaches-its-witness -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library function hands back a library enum widened to an interface, and the user calls the requirement on it. The enum's witness reads stdin, so the program reaches `__con_read_stdin` through the interface value and is refused with `E3104` at the user's call of `zLoud`, the library function that does the widening, as it is when the function returns the enum itself.
```maxon
// --- stdlib-overlay: Console.maxon
public interface ZShouter
	function shout() returns Count
end 'ZShouter'

public enum ZVolume implements ZShouter
	low
	high

	public function shout() returns Count
		let heard = __Builtins.readStdin(8)

		return match self 'volume'
			low gives heard.length()
			high gives heard.length() + 1
		end 'volume'
	end 'shout'
end 'ZVolume'

public function zLoud() returns ZShouter
	return ZVolume.high
end 'zLoud'
// --- file: main.maxon
function main() returns ExitCode
	let n = zLoud().shout()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:26:10: 'zLoud' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-reader-named-only-as-an-element-type-reaches-its-source -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The user's program names `StreamReader with ZSrc` only as the element type of the array a library function answers, and reads a line from each element. The reader's `readLine` reaches `ZSrc.readSome` and through it `__con_read_stdin`, so the program is refused with `E3104` on wasm32-wasi.
```maxon
// --- stdlib-overlay: Console.maxon
public type ZSrc implements ByteSource
	static function create() returns ZSrc
		return Self{}
	end 'create'

	public function readSome(most ReadCapacity) returns isolated ByteArray throws StreamError
		return ByteArray.init(__Builtins.readStdin(most))
	end 'readSome'
end 'ZSrc'

public typealias ZReaderArray = Array with (StreamReader with ZSrc)

public function zReaders() returns ZReaderArray
	let readers = ZReaderArray.create(0)
	readers.push(StreamReader.over(ZSrc.create()))
	return readers
end 'zReaders'
// --- file: main.maxon
function main() returns ExitCode
	let readers = zReaders()
	var reader = try readers.get(0) otherwise return 2
	let read = try reader.readLine() otherwise return 3

	match read 'read'
		line(text) then print("{text}\n")
		ended then print("ended\n")
		pastTheCap then print("pastTheCap\n")
	end 'read'

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:21:10: 'main' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:22:16: 'zReaders' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-reader-type-named-only-inside-another-type-reaches-its-source -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
`StreamReader with ZSrc` appears only as the element type inside `ZReaderArray`, the type the library functions take and answer, and no body anywhere calls `StreamReader.over`. Building and counting that array instantiates the reader type, whose `readLine` reaches `ZSrc.readSome` and through it `__con_read_stdin`, so both of the user's calls are refused with `E3104` on wasm32-wasi.
```maxon
// --- stdlib-overlay: Console.maxon
public type ZSrc implements ByteSource
	public function readSome(most ReadCapacity) returns isolated ByteArray throws StreamError
		return ByteArray.init(__Builtins.readStdin(most))
	end 'readSome'
end 'ZSrc'

public typealias ZReaderArray = Array with (StreamReader with ZSrc)

public function zNew() returns ZReaderArray
	return ZReaderArray.create(0)
end 'zNew'

public function zSize(readers ZReaderArray) returns Count
	return readers.count()
end 'zSize'
// --- file: main.maxon
function main() returns ExitCode
	let readers = zNew()
	let n = zSize(readers)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:20:16: 'zNew' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:21:10: 'zSize' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-library-struct-widened-to-an-interface-reaches-its-witness -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library function hands back a library struct widened to an interface, and the user calls the requirement on it. The struct's witness reads stdin, so the program reaches `__con_read_stdin` through the interface value and is refused with `E3104` at the user's call of `zLoud`, the library function that does the widening.
```maxon
// --- stdlib-overlay: Console.maxon
public interface ZShouter
	function shout() returns Count
end 'ZShouter'

public type ZThing implements ZShouter
	static function create() returns ZThing
		return Self{}
	end 'create'

	public function shout() returns Count
		let heard = __Builtins.readStdin(8)
		return heard.length() + 1
	end 'shout'
end 'ZThing'

public function zLoud() returns ZShouter
	return ZThing.create()
end 'zLoud'
// --- file: main.maxon
function main() returns ExitCode
	let n = zLoud().shout()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:23:10: 'zLoud' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-host-bound-reader-answered-in-a-tuple-reaches-its-source -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The user's function answers a `FileStreamReader`, whose source is host-bound file IO, as element 0 of a tuple, and the caller reads a line from that element. The program is refused with `E3104` on wasm32-wasi at the host-bound `File.openRead`, never a panic from inside the wasm backend.
```maxon
function openPair(path FilePath) returns (FileStreamReader, Count) throws FileReadError
	let reader = try File.openRead(path)
	return (reader, 1)
end 'openPair'

function main() returns ExitCode
	let pair = try openPair(FilePath from "input.txt") otherwise return 2
	let read = try pair.0.readLine() otherwise return 3

	match read 'read'
		line(text) then print("{text}\n")
		ended then print("ended\n")
		pastTheCap then print("pastTheCap\n")
	end 'read'

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:2:10: 'openPair' lowers to the runtime entry '__mf_read_append', which has no wasm32-wasi implementation
error E3104: <fragment>:3:24: 'File.openRead' lowers to the runtime entry '__mf_open_read', which has no wasm32-wasi implementation
error E3104: <fragment>:7:10: 'main' lowers to the runtime entry '__mf_read_append', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-library-reader-answered-in-a-tuple-reaches-its-source -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library function answers a `StreamReader` over a library source whose `readSome` reads stdin as element 0 of a tuple, and the user reads a line from that element. The user's program names the reader only through the tuple, so the reach walk must follow the tuple element's type to `ZSrc.readSome` and `__con_read_stdin`: the program is refused with `E3104` on wasm32-wasi at the user's call of `zPair`, never a panic from inside the wasm backend.
```maxon
// --- stdlib-overlay: Console.maxon
public type ZSrc implements ByteSource
	static function create() returns ZSrc
		return Self{}
	end 'create'

	public function readSome(most ReadCapacity) returns isolated ByteArray throws StreamError
		return ByteArray.init(__Builtins.readStdin(most))
	end 'readSome'
end 'ZSrc'

public typealias ZReader = StreamReader with ZSrc

public function zPair() returns (ZReader, Count)
	return (StreamReader.over(ZSrc.create()), 1)
end 'zPair'
// --- file: main.maxon
function main() returns ExitCode
	let pair = zPair()
	let read = try pair.0.readLine() otherwise return 2

	match read 'read'
		line(text) then print("{text}\n")
		ended then print("ended\n")
		pastTheCap then print("pastTheCap\n")
	end 'read'

	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:19:10: 'main' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:20:13: 'zPair' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-user-function-widening-a-library-source-in-its-own-body -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The user's own function widens a library enum, whose witness reads stdin, to the library interface in its own body, and `main` calls the requirement through the interface value. No library function does the widening and no library factory is called: an enum case is a tag literal, so the only library names in the program are the type and its interface. The program reaches `__con_read_stdin` through the witness and is refused with `E3104` on wasm32-wasi, never a panic from inside the wasm backend.
```maxon
// --- stdlib-overlay: Console.maxon
public interface ZShouter
	function shout() returns Count
end 'ZShouter'

public enum ZVolume implements ZShouter
	low
	high

	public function shout() returns Count
		let heard = __Builtins.readStdin(8)

		return match self 'volume'
			low gives heard.length()
			high gives heard.length() + 1
		end 'volume'
	end 'shout'
end 'ZVolume'
// --- file: main.maxon
function louder() returns ZShouter
	return ZVolume.high
end 'louder'

function main() returns ExitCode
	let n = louder().shout()
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:21:10: 'louder' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-for-loop-over-a-library-iterable-reaches-its-cursor-witness -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
A library collection's cursor reads stdin in its `advance`, and the user's program only loops over the collection. The loop reaches the cursor's `Iterator` witness through the `Iterable` constraint and not through any name the user wrote, so the reach walk must credit the cursor's impls implicitly: the program reaches `__con_read_stdin` and is refused with `E3104` on wasm32-wasi, never a panic from inside the wasm backend.
```maxon
// --- stdlib-overlay: Console.maxon
public type ZCursor implements Iterator with Count
	var steps as Count

	static function create() returns ZCursor
		return Self{steps: 0}
	end 'create'

	public function current() returns Count
		return self.steps
	end 'current'

	public function advance(mutable self) throws IterationError
		let heard = __Builtins.readStdin(8)

		if heard.length() == 0 'quiet'
			throw IterationError.exhausted
		end 'quiet'

		self.steps = self.steps + 1
	end 'advance'
end 'ZCursor'

public type ZBag implements Iterable with (Count, ZCursor)
	public static function create() returns ZBag
		return Self{}
	end 'create'

	public function createIterator() returns ZCursor throws IterationError
		return ZCursor.create()
	end 'createIterator'
end 'ZBag'
// --- file: main.maxon
function main() returns ExitCode
	var total = 0 as Count

	for step in ZBag.create() 'steps'
		total = total + step
	end 'steps'

	print("{total}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:38:2: 'ZBag.createIterator' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:38:2: 'ZCursor.advance' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:38:6: 'ZCursor.current' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:38:19: 'ZBag.create' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```

<!-- test: rejected-on-wasm.a-where-constrained-user-type-over-a-library-enum-reaches-its-witness -->
<!-- unsupported-targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
The user's own generic type constrains its parameter to a library interface and is instantiated at a library enum. An enum case is a tag literal, so no function of the enum is reached for the value to exist, and the only route to the enum's witness is the `where` constraint of the user's method. The enum's requirement reads stdin, so the program reaches `__con_read_stdin` and is refused with `E3104` on wasm32-wasi, never a panic from inside the wasm backend.
```maxon
// --- stdlib-overlay: Console.maxon
public interface ZShouter
	function shout() returns Count
end 'ZShouter'

public enum ZVolume implements ZShouter
	low
	high

	public function shout() returns Count
		let heard = __Builtins.readStdin(8)

		return match self 'volume'
			low gives heard.length()
			high gives heard.length() + 1
		end 'volume'
	end 'shout'
end 'ZVolume'
// --- file: main.maxon
type Amp uses Source where Source is ZShouter
	var gain as Count

	static function create() returns Amp
		return Self{gain: 2}
	end 'create'

	function boost(source Source) returns Count
		return source.shout() * self.gain
	end 'boost'
end 'Amp'

typealias VolumeAmp = Amp with ZVolume

function main() returns ExitCode
	let amp = VolumeAmp.create()
	let n = amp.boost(ZVolume.high)
	print("{n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3104: <fragment>:24:18: 'Amp.create' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:28:11: 'Amp.boost' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
error E3104: <fragment>:35:10: 'main' lowers to the runtime entry '__con_read_stdin', which has no wasm32-wasi implementation
```
