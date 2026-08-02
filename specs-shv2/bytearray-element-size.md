---
feature: bytearray-element-size
status: stable
keywords: [array, byte, bytearray, element-size, push, string, union]
category: memory
---
# ByteArray Element Size and Byte-Slice Correctness

## Documentation

An `Array with Byte` (`ByteArray`) built via `ByteArray.create()` + `push`
must have its backing `__ManagedMemory` `element_size = 1`. The per-create
field-init stamp used to hardcode `element_size = 8` (correct only for the
pointer-width elements — int / float / string / struct — that dominate the
compiler's own containers). For a `Byte` element that stride is wrong: every
`push` writes 8 bytes apart, so `String.from(out)` reads only every 8th byte
and the reconstructed string is garbled.

This never surfaced under the C# bootstrap (which sizes the record per element
type) nor in most self-hosted spec tests (which round-trip strings through
stdlib helpers), so it only bit a *self-compiled* compiler: the type-resolver's
`byteSliceToString` (`ByteArray.create()` + per-byte `push`) is how a bare
`Union.caseName` read is split into `(unionName, caseName)`. A garbled slice
made every payload-free boxed-union case read (e.g. `Environment.inherit` as a
struct-literal field initializer) fail to resolve — a spurious "unknown enum
case" (E3034). The two behaviours below pin the root cause (byte-slice
reconstruction) and the shape that exposed it (a bare union case as a
struct-literal field init).

The array-*literal* twin of this hazard (`[a, b, c]`, `ByteArray from [...]`
built from non-constant narrow elements) is covered separately in
`array-literal-element-size.md` — the self-hosted compiler corrects it in a
post-TypeResolution pass, a capability the C# bootstrap's parse-time value-kind
front-end lacks, so that test is `status: selfhosted`.

### Every write is exactly `element_size` bytes wide — including the ERASES

The stride is only half of it. An operation that VACATES a slot must erase
exactly that slot, and an erase is a write like any other: at `element_size = 1`
a slot is ONE byte, so erasing it with a machine word destroys the seven
elements that follow. `insert` is where this shows, because it erases the slot
its right-shift duplicated before writing the new element into it:

```text
var a = b"hey"
a.insert(1, value: 88)   // -> b"hXey", NOT b"hX\0\0"
```

The tail bytes `e` and `y` had already been copied one slot right when the erase
runs, so an over-wide erase silently overwrites live data and the array's own
`count()` still reports the correct length — a wrong answer with no diagnostic.

## Tests

<!-- test: bytearray-slice-roundtrip -->
### Byte-by-byte slice reconstructs the correct substring
Pushes a `[start, end)` byte slice of a source string into a fresh `ByteArray`
one byte at a time, then rebuilds a `String`. With a wrong 8-byte stride the
reconstruction is garbage and the equality check fails.
```maxon
function main() returns ExitCode
	let src = "Environment.inherit"
	let bytes = src.toByteArray()
	var out = ByteArray.create()
	var i = 12
	while i < bytes.count() 'copy'
		let b = try bytes.get(i) otherwise 0
		out.push(b)
		i = i + 1
	end 'copy'
	let sliced = String.from(out)
	return 0 if sliced == "inherit" else 1
end 'main'
```
```exitcode
0
```

<!-- test: bytearray-slice-length -->
### Reconstructed byte-slice has the correct length and content
Rebuilds the whole source string byte-by-byte and returns its byte length. A
wrong stride would still push `count` bytes but pack them 8 apart, leaving a
`String` whose bytes decode to a different length than the original.
```maxon
function main() returns ExitCode
	let src = "hello world"
	let bytes = src.toByteArray()
	var out = ByteArray.create()
	for b in bytes 'copy'
		out.push(b)
	end 'copy'
	let rebuilt = String.from(out)
	return 0 if rebuilt == "hello world" else 1
end 'main'
```
```exitcode
0
```

<!-- disabled-test: bare-union-case-as-struct-field-init -->
<!-- stdlib whitelist: `StringArray` (`stdlib/Json.maxon`'s `export typealias StringArray = Array with
     String`). `stdlib/Json.maxon` is not on shv2's whitelist, so the `set(vars StringArray)` payload
     resolves to a bare `int` and `v.count()` is rejected before the byte-slice path is reached.
     ⚠ Unlike its three siblings here, this case was NOT unlocked by `stdlib/File.maxon` (R4.7): the name
     it wants is Json's, not File's. MEASURED on the tree that listed `File.maxon` —
     `E3011 Unknown type 'StringArray'`, raised at the `set(v) gives v.count()` ARM (where the payload
     binding's type must resolve), not at the `union` declaration, which accepts the unknown name -->
### Bare payload-free boxed-union case read as a struct-literal field initializer
Mirrors `stdlib/Subprocess.maxon`'s `Configuration.create`: a boxed
(payload-bearing) union's payload-free case is read bare (`Env.inherit`) as a
struct-literal field initializer. Resolving that read runs the compiler's
byte-slice path; a mis-sized `ByteArray` there makes it spuriously unresolvable.
```maxon
union Env
	inherit
	set(vars StringArray)
end 'Env'

union In
	none
	bytes(data String)
end 'In'

type Cfg
	export var env as Env
	export var input as In

	export static function create() returns Cfg
		return Cfg{env: Env.inherit, input: In.none}
	end 'create'
end 'Cfg'

function main() returns ExitCode
	let c = Cfg.create()
	let e = match c.env 'e'
		inherit gives 0
		set(v) gives v.count()
	end 'e'
	let i = match c.input 'i'
		none gives 0
		bytes(d) gives d.byteLength()
	end 'i'
	return e + i
end 'main'
```
```exitcode
0
```

<!-- test: bytearray-insert-preserves-tail -->
### `insert` into a byte-string literal keeps the elements it shifted
`b"hey"` is backed by read-only rdata, so the insert COWs it to the heap first;
the shift and the erase then run against a 1-byte stride. The erase must clear
one byte. An 8-byte erase wipes the just-shifted `e` and `y` and the array reads
back `h X \0 \0` with `count() == 4` — corrupt data behind a successful exit.
```maxon
function main() returns ExitCode
	var bytes = b"hey"
	bytes.insert(1, value: 88)
	if bytes.count() != 4 'cnt'
		return 1
	end 'cnt'
	return 0 if String.from(bytes) == "hXey" else 2
end 'main'
```
```exitcode
0
```

<!-- test: bytearray-insert-preserves-tail-heap-backed -->
### The same `insert`, on a `ByteArray` that was never a literal
The twin of the test above with the bytes pushed one at a time, so the buffer is
heap-owned from birth and no copy-on-write ever runs. It must give the identical
answer: the erase width follows `element_size`, not where the buffer came from.
```maxon
function main() returns ExitCode
	var bytes = ByteArray.create()
	bytes.push(104)
	bytes.push(101)
	bytes.push(121)
	bytes.insert(1, value: 88)
	if bytes.count() != 4 'cnt'
		return 1
	end 'cnt'
	return 0 if String.from(bytes) == "hXey" else 2
end 'main'
```
```exitcode
0
```

<!-- test: bytearray-insert-at-front-preserves-all -->
### `insert(0, ...)` shifts the whole array and keeps all of it
Inserting at the front shifts every element, so the erased slot is followed by
the maximum number of live neighbours — five here, so an over-wide erase loses
all five rather than the two `insert(1, ...)` loses.
```maxon
function main() returns ExitCode
	var bytes = b"axbxc"
	bytes.insert(0, value: 90)
	if bytes.count() != 6 'cnt'
		return 1
	end 'cnt'
	return 0 if String.from(bytes) == "Zaxbxc" else 2
end 'main'
```
```exitcode
0
```

### ⚠ THE STRIDE HAS TWO PRODUCERS, AND THEY ARE ONLY ALLOWED TO EXIST WHILE THEY AGREE

`element_size@24` is stamped from `ProgramSignatures.arrayElementSize`, which sizes a ranged element from
its DECLARED range — except for a `b"…"` literal, whose blob is byte-PACKED by construction and whose
record `LowerMaxonToStd.lowerByteStringLiteral` stamps `1` directly. The two agree for the canonical
`Byte = int(0 to u8.max)` and for anything narrower, which is the whole of what a byte string is.

They do not agree for a WIDER `Byte`, and the cost of letting that compile is not an abort — it is a
**silent wrong answer**. Measured with `typealias Byte = int(0 to 1000)`: two `Bytes` values, the same
`push(300)` then `get(2)`, read back **44** from the literal-produced record and **300** from the
`.create()`-produced one. One static type, two behaviours, no diagnostic. (`append` between them *does*
abort — `RuntimeAbort.arrayAppendElementSizeMismatch` — but it is the ONE array operation that compares
the two records' strides; every other one just uses whichever it was handed.)

There is no reference behaviour to match here: the C# bootstrap mis-answers the same program its own way,
emitting the blob byte-packed and then TYPING the array `u16`, so `b"CD".get(0)` reads **17475**
(`0x4443`). Emitting the blob at a wider stride is an element-wise widening emission — a real mechanism,
and the same one a widening `__arr_append` across differing strides would need — so until that exists the
literal is refused at its own position.

<!-- test: byte-string-literal-refused-when-byte-is-wider-than-one-byte -->
### A `b"…"` literal is refused when this program's `Byte` does not fit one byte
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var made = Bytes.create()
	made.push(65)
	made.append(b"CD")
	return made.count()
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/bytearray-element-size/byte-string-literal-refused-when-byte-is-wider-than-one-byte.test:8:14: Unsupported: a `b"…"` byte-string literal in a program whose `Byte` is a 2-byte range: the literal's blob is byte-PACKED, so its record would stride 1 while every `Array with Byte` built by `.create()` strides 2 — two values of one type that behave differently. Declare `Byte` as `int(0 to u8.max)` (or any range that fits one byte), or build the array with `.create()` + `push`
```

<!-- test: byte-string-global-refused-when-byte-is-wider-than-one-byte -->
### The refusal reaches a top-level byte-string global, which no function body ever parses
A `let`/`var` at file scope is folded to bytes by the initializer sweep and its record is built by
`__module_init`, so it never reaches the expression-position emitter — it needs the sweep's own throw
site or it slips the gate entirely (measured: it did).
```maxon
typealias Byte = int(0 to u64.max)
typealias Bytes = Array with Byte

var BUFFER = b"hi"

function main() returns ExitCode
	var made = Bytes.create()
	made.push(1)
	BUFFER.push(2)
	return made.count() + BUFFER.count()
end 'main'
```
```maxoncstderr
error E2015: specs/fragments/bytearray-element-size/byte-string-global-refused-when-byte-is-wider-than-one-byte.test:5:14: Unsupported: a `b"…"` byte-string literal in a program whose `Byte` is a 8-byte range: the literal's blob is byte-PACKED, so its record would stride 1 while every `Array with Byte` built by `.create()` strides 8 — two values of one type that behave differently. Declare `Byte` as `int(0 to u8.max)` (or any range that fits one byte), or build the array with `.create()` + `push`
```

<!-- test: byte-string-literal-accepted-at-the-canonical-byte -->
### The canonical `Byte` keeps both producers agreeing, and they interoperate
`.create()` and `b"…"` both stride 1, so a literal appends into a heap-grown array — which is the whole
point of the unification, and the thing that aborted before it.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var made = Bytes.create()
	made.push(65)
	made.append(b"CD")
	return 0 if made.count() == 3 and (try made.get(1) otherwise 0) == 67 and (try made.get(2) otherwise 0) == 68 else 1
end 'main'
```
```exitcode
0
```

### ⚠ A ONE-BYTE SLOT IS NOT A BYTE — the stride is what the RECORD says, the RANGE is what it does not

`rangedAliasStorageBytes` gives EVERY non-negative range that fits `u8.max` a one-byte slot, so `Byte` is
not the only byte-packed element a program can have: `typealias Small = int(0 to 200)` makes
`Array with Small` stride 1 as well. MEASURED — `String.from(<a Smalls>)`, a BUILTIN parameter checked by
the stride rule alone (`Parser.valueIsByteArray`), accepts one and prints its two bytes straight back.

That is right for a builtin `__ManagedMemory` parameter, which means *"a raw byte buffer"*. It is NOT right
for an ordinary DECLARED parameter, where the element's name is part of the type. MEASURED on a compiler
built with the symmetric "both sides byte-packed" rule: `takesBytes(b Bytes)` accepted a `Smalls`, pushed
**250** into it through the wider parameter, and `s.get(0)` — an accessor typed `int(0 to 200)` — read that
250 straight back, exit 0, no diagnostic.

So the byte-element boundary door R4.7 opened for `String.addressableBytes()` is an IDENTITY door on BOTH
sides, over the two element names shv2 actually gives a byte (`SignatureIndex.isByteElementName`:
`Byte`, `__ManagedByte`), and NOT "anything that strides one" — the arriving side admits exactly the
compiler-owned, `__`-reserved `__ManagedByte`, which no source can declare and which carries no range of its
own, and the declared side admits exactly a one-byte-strided `Array` over one of those same two names
(`ProgramSignatures.byteBufferBoundaryAdmits`). The case below is what holds the arriving side shut.

⚠ **THE DECLARED SIDE NEEDED THE SAME ROSTER, AND IT WAS THE SAME HOLE POINTING THE OTHER WAY** (R4.7
review). Shipped with a bare stride test there, the door admitted a String's byte view into a parameter
declared `Array with Small`, and `b.get(0)` read back **223** from an element declared `int(0 to 200)` —
exit 223, no diagnostic. Both sides now ask the roster; the declared side ALSO keeps the stride test, which
is live and independently reachable (`typealias Byte = int(0 to 1000)` puts the NAME on the roster while the
record strides two, and the door refuses there).

⛔ **AND THE ROSTER DID NOT CLOSE THE 223 — IT MOVED IT ONE RENAME AWAY.** A roster is a question about the
NAME, so `typealias Byte = int(0 to 200)` passes it; `rangedAliasStorageBytes` gives `0 to 200` a one-byte
slot, so it passes the stride test too. MEASURED on `origin/main` (N2): the identical **223**, out of an
element declared `int(0 to 200)`, under the most natural name a byte alias has. The paragraph above had
recorded that reading as cured. The third question — the element's declared BOUNDS
(`RangedAliasRegistry.holdsEveryByteInEveryFile`) — is what actually closes it, and
`a-narrow-byte-does-not-admit-a-compiler-synthesized-buffer` at the end of this file is the case.

⚠ **THE VIEW-SIDE BEHAVIOUR WAS ONCE UNREACHABLE FROM A SPEC, AND IS NOT ANY MORE.** Producing an
`Array with __ManagedByte` used to require `String.addressableBytes()`, which
`Parser.requireStdlibOnlyStringMethod` refuses to any file not physically under `stdlib/`, and the spec
runner stages every fragment outside `stdlib/` — so the three measurements above were made by building
purpose-written files under `stdlib/`. The command-line rung widened the element to EVERY
compiler-synthesized byte buffer, and three of its producers are written in ORDINARY USER SOURCE
(`__ManagedDirectory.filename`/`currentPath`, `__Builtins.commandLineArg`), so an arriving
`Array with __ManagedByte` is now four keystrokes away from any spec fragment. The last two sections of
this file are the cases that came with that.

<!-- test: byte-packed-alias-is-not-interchangeable-with-byte -->
### Two byte-packed aliases are still two types
```maxon
typealias Byte = int(0 to u8.max)
typealias Small = int(0 to 200)
typealias Bytes = Array with Byte
typealias Smalls = Array with Small

function takesBytes(b Bytes) returns ExitCode
	return b.count()
end 'takesBytes'

function main() returns ExitCode
	var s = Smalls.create()
	s.push(7)
	return takesBytes(s)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/byte-packed-alias-is-not-interchangeable-with-byte.test:14:9: argument type mismatch for 'b': expected 'Array_Byte', got 'Array_Small'
```

### ⚠ A `Byte` TWO FILES DISAGREE ABOUT IS TWO ELEMENT TYPES, NOT ONE WIDE ONE

`Array with Byte` is interned on the element's NAME, so one interned instance carries one
`element_size@24` — which is why the whole-program stride is a fold over every declaration
(`RangedAliasRegistry.storageBytesInEveryFile`, "a record created in one file is pushed, appended, read
and dropped in another, and they must stride identically"). That fold is correct and it is not enough.

**MEASURED**: with `stdlib/File.maxon` on the whitelist, a program declaring `typealias Byte = int(0 to
1000)` and **never mentioning `File` at all** was REFUSED — `E3005 stdlib/File.maxon:74:28: argument type
mismatch for 'managed': expected '__ManagedMemory', got 'Array_Byte'`. The stdlib builds its read buffer
with `__ManagedMemory.create(size + extraBytes, 1)` (`stdlib/File.maxon:71`), i.e. through the very
instance the user's alias had widened to two bytes, and `file.read(managed, …)` three lines later requires
a byte-PACKED one. **No width rule can fix that**: the two `Array with Byte` have to become two INSTANCES.
It also broke `StdlibLoader`'s own stated invariant — *"adding a module changes NOTHING for a program that
does not use it"* — in the loudest possible way, on programs whose only sin was the word `Byte`.

⇒ **A name whose declarations disagree about the RANGE gets one element type per distinct range**, spelled
`Byte$0_255` / `Byte$0_1000`, and an instance's element is spelled the way its READER resolves the name
(`RangedAliasRegistry.rangeScopedName`, which routes through the same file-scoped `lookup` a cast or a
parameter resolves through). The suffix is derived from the RANGE and not from a declaration ordinal,
which is what keeps it free of filesystem enumeration order; `$` is unspellable in an identifier, so the
minted half of the element namespace and the declarable half are disjoint by construction.

⚠ **AN AGREEING NAME IS NOT CONTESTED, AND THAT IS THE LOAD-BEARING HALF.** `int(0 to 255)` and
`int(0 to u8.max)` are one range, so the seven `stdlib/` files that declare `Byte` and the eighty spec
files that declare it are ONE claimant between them: the element keeps its bare name, `Array_Byte` stays
`Array_Byte`, and no emitted symbol moves. The last two cases below pin both directions of that.

<!-- test: wide-byte-program-that-never-mentions-file-still-runs -->
### A wide `Byte` does not break a program that never touches the stdlib's byte buffers
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var made = Bytes.create()
	made.push(300)
	made.push(700)
	return 0 if made.count() == 2 and (try made.get(1) otherwise 0) == 700 else 1
end 'main'
```
```exitcode
0
```

<!-- test: wide-byte-program-still-round-trips-a-file -->
<!-- targets: x64-windows -->
### The stdlib's own byte buffer stays byte-PACKED while the program's `Byte` is two bytes wide
x64-windows only, and it is the ONE case here that genuinely needs a file: it is the RUNTIME proof
that `stdlib/File.maxon`'s own read buffer strides 1 while the caller's `Byte` strides 2, so it has
to actually read one. `File.writeText` lowers to `__mf_open_write`, which has no x64-linux or
wasm32-wasi implementation — see `file-io.md`'s Targets section for the whole statement of that
gate and for what unblocks it.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var made = Bytes.create()
	made.push(300)

	let path = FilePath from "test_widebyte_roundtrip.txt"
	try File.writeText(path, content: "Hello") otherwise 'writeErr'
		return 1
	end 'writeErr'
	let content = try File.readText(path) otherwise 'readErr'
		return 2
	end 'readErr'
	try File.delete(path) otherwise 'delErr'
		return 3
	end 'delErr'

	print("{content} {made.count()}")
	return 0 if content.count() == 5 else 4
end 'main'
```
```stdout
Hello 1
```
```exitcode
0
```

<!-- test: a-wide-byte-still-reads-a-compiler-synthesized-buffer -->
<!-- targets: x64-windows -->
### A buffer the COMPILER minted answers to no file's `Byte` — not even the file that asked for it
The case above proves a WHITELISTED MODULE's buffer survives a wide `Byte`. This one proves the
harder half: a buffer that no source line describes at all. `__ManagedDirectory.currentPath()` is a
`mm_alloc`'d run of bytes whose `element_size@24` is stamped from the literal `ByteStringElementSize`
(`ManagedDirectoryRuntime.emitCStringToManaged`), so its stride is 1 in every program ever compiled —
there is nothing here for a declaration to decide, and it wears the compiler's own
`__ManagedByte` element for exactly that reason.

⚠ **PER-FILE SCOPING IS NOT THE WEAKER FORM OF THAT CURE, IT IS A DIFFERENT ANSWER, AND IT IS WRONG.**
Contest-scoping the element to the PARSING file relocates the defect rather than removing it: this
program is the user's own file, so the buffer would take the user's `Byte` and
`String.init(managed)` refuses it — MEASURED, with only that one call site changed:
`E3005 argument type mismatch for 'managed': expected '__ManagedMemory', got 'Array_Byte$0_1000'`,
the same sentence `stdlib/CommandLine.maxon` raised when the element was whole-program, moved one
file over. Only an element the program cannot name at all ends it.

x64-windows only for `managed-directory.md`'s reason: `currentPath` lowers to `GetCurrentDirectoryA`.
The cwd differs per machine, so the assertion is on its LENGTH, which is all this case needs — the
failure it guards is a refusal to compile.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var made = Bytes.create()
	made.push(300)

	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let cwdStr = String.init(cwd)
	return 0 if cwdStr.count() > 0 and made.count() == 1 else 2
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-two-files-disagree-about-is-two-types -->
### Two ranges for one name are two element types, and they are not interchangeable
`ByteArray` is `stdlib/File.maxon`'s own `export typealias ByteArray = Array with Byte`, resolved
against the file that DECLARED it, so it is that file's `int(0 to u8.max)`; `Bytes` is this file's
`int(0 to 1000)`. Two ranges, two element types, and the arrow between them does not exist.

⚠ **IT REACHES THE STDLIB'S BYTE ARRAY THROUGH THE *TYPE*, NOT THROUGH A FILE READ, AND THAT IS THE
POINT OF THE EDIT (N2 review).** It was written as `File.readBinary(path)`, which produces the same
`Array_Byte$0_255` and made this a **x64-windows-only** case for no reason its own assertion needs:
`File.writeText`/`readBinary` lower to `__mf_open_write`/`__mf_open_read`, and the two `E3104`s that
raises off-Windows landed AHEAD of the E3005 in the captured stderr. The assertion here is a
compile-time type identity, decided long before lowering and identical on every target — so a
`targets:` marker would have been hiding a green lane rather than describing a red one
(`file-io.md`'s Targets section states that rule). **The expected diagnostic is unchanged, to the
byte.**
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function takesMine(b Bytes) returns ExitCode
	return b.count()
end 'takesMine'

function main() returns ExitCode
	var stdlibs = ByteArray.create()
	stdlibs.push(65)
	return takesMine(stdlibs)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/a-byte-two-files-disagree-about-is-two-types.test:12:9: argument type mismatch for 'b': expected 'Array_Byte$0_1000', got 'Array_Byte$0_255'
```

<!-- test: an-agreeing-byte-keeps-the-bare-element-name -->
### One range for one name is one element type, under the bare name
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function takesMine(b Bytes) returns ExitCode
	return b.count()
end 'takesMine'

function main() returns ExitCode
	return takesMine(7)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/an-agreeing-byte-keeps-the-bare-element-name.test:10:9: argument type mismatch for 'b': expected 'Array_Byte', got 'int'
```

### ⚠ THE READER'S OWN `Byte` DECIDES, AND THE MINT IS NEVER QUOTED BACK AT THE AUTHOR

**EVERY CASE ABOVE IS SINGLE-FILE, AND A SINGLE-FILE PROGRAM CANNOT TELL THE TWO RULES APART.** When
the only user declaration of `Byte` is the one the literal is written beside, the reader's range and
the whole-program fold (`RangedAliasRegistry.storageBytesInEveryFile`, the MAX over every
declaration) are the SAME number — so the two wide-`Byte` refusals above pass identically against a
compiler that scopes the stride to the reader and against one that folds it, and neither pins which
is running. The four cases here are the two-file shapes that separate them, and they are the ones
that would go quiet if `Parser.byteElementDeclaredStride` ever stopped resolving through
`ProgramSignatures.internArrayByteInstance(readerFilePath)`.

<!-- test: readers-own-byte-decides-the-literal-not-the-whole-program-fold -->
### A `b"…"` in a file with no `Byte` of its own stays packed while a SIBLING file is wide
`main.maxon` declares no `Byte`, so it means `stdlib/File.maxon`'s `int(0 to u8.max)` and its literal
is legal. Under a whole-program fold `wide.maxon`'s `int(0 to 1000)` would widen the one shared
`Array with Byte` to stride 2 and this program would be E2015 — the exact "adding a declaration
breaks a file that never mentions it" failure the scoping exists to end, in its smallest form.
```maxon
// --- file: wide.maxon
typealias Byte = int(0 to 1000)

export function widen(b Byte) returns int
	return b
end 'widen'

// --- file: main.maxon
function main() returns ExitCode
	var a = b"hi"
	let n = try a.get(0) otherwise 0
	return (n - widen(56)) as ExitCode
end 'main'
```
```exitcode
48
```

<!-- test: the-wide-files-own-byte-literal-is-still-refused -->
### …and the WIDE file's own `b"…"` is still refused, in the same program
The other half, and the half that proves the case above is not simply a lost refusal: the identical
literal moved into `wide.maxon` meets the wall its own declaration builds. One program, two files,
two answers — which is precisely what "one instance cannot have two strides" buys once the two
instances are distinct.
```maxon
// --- file: wide.maxon
typealias Byte = int(0 to 1000)

export function widen() returns int
	var a = b"hi"
	return try a.get(0) otherwise 0
end 'widen'

// --- file: main.maxon
function main() returns ExitCode
	return widen() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:10: Unsupported: a `b"…"` byte-string literal in a program whose `Byte` is a 2-byte range: the literal's blob is byte-PACKED, so its record would stride 1 while every `Array with Byte` built by `.create()` strides 2 — two values of one type that behave differently. Declare `Byte` as `int(0 to u8.max)` (or any range that fits one byte), or build the array with `.create()` + `push`
```

<!-- test: a-buffer-field-strides-its-declaring-files-byte -->
### A `__ManagedMemory` FIELD strides its DECLARING file's `Byte`, not its reader's
`ProgramSignatures.declaredSlotType` resolves a slot's compiler-minted alias against the file that
DECLARED the slot. `holder.maxon` declares no `Byte`, so `Holder.buf` is a byte-PACKED buffer for
every reader — including `main.maxon`, whose own `Byte` is two bytes wide. Scoped to the reader
instead, `main.maxon` would read a record `holder.maxon` stamped at stride 1 as though it strode 2,
with no diagnostic anywhere; `first()` returning `a` is what says it did not.
```maxon
// --- file: holder.maxon
export type Holder
	export var buf as __ManagedMemory

	export static function ofText(t String) returns Holder
		return Holder{buf: t.toByteArray()}
	end 'ofText'

	export function first() returns int
		return try self.buf.get(0) otherwise 0
	end 'first'
end 'Holder'

// --- file: main.maxon
typealias Byte = int(0 to 1000)

function widen(b Byte) returns int
	return b
end 'widen'

function main() returns ExitCode
	let h = Holder.ofText("ab")
	return (h.first() - widen(50)) as ExitCode
end 'main'
```
```exitcode
47
```

<!-- test: a-buffer-field-refuses-another-files-byte-at-the-construction -->
### …and the disagreement lands LOUDLY, at the construction
The same two files with the buffer handed IN rather than built inside `holder.maxon`. `main.maxon`'s
`"ab".toByteArray()` is its OWN `Byte`, two bytes wide, and the field is one byte wide — so the two
files meet nominally, at the line that hands the record over, instead of silently striding one
record two ways. This is the refusal that pays for the case above.
```maxon
// --- file: holder.maxon
export type Holder
	export var buf as __ManagedMemory

	export static function of(b __ManagedMemory) returns Holder
		return Holder{buf: b}
	end 'of'

	export function first() returns int
		return try self.buf.get(0) otherwise 0
	end 'first'
end 'Holder'

// --- file: main.maxon
typealias Byte = int(0 to 1000)

function widen(b Byte) returns int
	return b
end 'widen'

function main() returns ExitCode
	let h = Holder.of("ab".toByteArray())
	return (h.first() - widen(50)) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:23:10: argument type mismatch for 'b': expected 'Array_Byte$0_255', got 'Array_Byte$0_1000'
```

### The MINT is an internal spelling, and no diagnostic may quote it

`Byte$0_255` is the compiler's name for ONE declaration of `Byte`; it is unspellable in source
(`SignatureIndex.RangeQualifiedAliasSeparator`) and no author ever wrote it. A TYPE-IDENTITY message
must still show it — `expected 'Array_Byte$0_1000', got 'Array_Byte$0_255'` above is the whole
content of that refusal, and the bare spelling would read `expected 'Array_Byte', got 'Array_Byte'`.
A RANGE message must not: it quotes a `typealias` back at the author, and its bounds are printed in
the same sentence, so the suffix carried nothing the reader had not already been told.

⛔ **MEASURED (N2 review) before `SignatureIndex.sourceSpelledAliasName` existed**, on the two cases
below: `Value 2000 is outside the range of 'Byte$0_1000' (int(0 to 1000))` and
`value outside typealias 'Byte$0_1000'` — naming a declaration the program does not contain. It was
also inconsistent with itself, because only a generic instance's ELEMENT is ever qualified: the very
same alias guarding a plain PARAMETER printed the bare `Byte`. One alias, two spellings, decided by
which slot the value happened to flow into.

<!-- test: a-contested-alias-is-quoted-as-source-spells-it -->
### The compile-time narrowing quotes the name the file declares
The program declares `Byte` and so does `stdlib/File.maxon`, over a different range — so the element
of `Bytes` is a mint. The diagnostic is about the RANGE, and the range belongs to the declaration on
line 1.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var made = Bytes.create()
	made.push(2000)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:7:7: Value 2000 is outside the range of 'Byte' (int(0 to 1000))
```

<!-- test: a-contested-alias-panics-under-the-name-source-spells -->
<!-- targets: x64-windows, x64-linux -->
### …and so does the runtime guard, through the same speller
`InsertRangeChecks.rangeCheckMessage` and the compile-time twin apply the one strip, so a program
cannot be refused under one spelling and panic under another.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function feed(v int) returns int
	var made = Bytes.create()
	made.push(v)
	return try made.get(0) otherwise 0
end 'feed'

function main() returns ExitCode
	return feed(2000) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-contested-alias-panics-under-the-name-source-spells.test:7: Range check failed: value outside typealias 'Byte'
Stack trace:
  in feed
  in main
  in mrt_start
```

### ⚠ THE BOUNDARY IS ONE DOOR, AND EVERY COERCION SITE HAS TO ASK IT

`byteBufferBoundaryAdmits` is the whole of *"the two byte element types stay DISTINCT and convert at the
BOUNDARY"*, and for one rung only the CALL-ARGUMENT site asked it. The other seven compared the aggregate
NAMES alone — `return`, reassignment, `otherwise`, a match arm's merge, a struct-literal / union-payload
store, overload candidate scoring, and a generic call's type-parameter argument — so a program that a call
accepted was refused the moment the same value crossed any other one of them.

**MEASURED — one probe per door, on this branch's base commit `bf328745a`; the `return` one and the 223
below also reproduce on `origin/main` with none of N2 applied, so neither is N2's doing**
(`Array___ManagedByte` is the element every compiler-synthesized byte buffer wears; `Array_Byte` is what
`__ManagedMemory` declares):

* `return` — `E3005 Cannot return 'Array___ManagedByte' from function declared to return 'Array_Byte'`
* reassignment — `E3005 cannot assign 'Array___ManagedByte' to variable 'm' of type 'Array_Byte'`
* `otherwise` — `E3059 otherwise type 'Array___ManagedByte' does not match expected type 'Array_Byte'`
* a match arm — `E3005 match arms give incompatible types: 'Array___ManagedByte' vs 'Array_Byte'`
* a struct-literal field store — `E3005 cannot assign 'Array___ManagedByte' to variable 'Holder.mem' of type 'Array_Byte'`
* a generic type-parameter argument — `E3005 argument type mismatch for 'item': expected 'Bytes', got 'Array___ManagedByte'`

The BOOTSTRAP compiles every one of them. The cure is the door asked ONCE — `aggregatesConflict` now takes
the (tag, nameId) pair beside each side's aggregate name and folds the byte boundary in, so a site cannot
ask the identity question without asking the boundary one. Overload SCORING is the eighth site, it carries
the door for its own stated promise (*"a candidate this function accepts cannot then be rejected
downstream"*), and it too has a distinguishing case —
`overload-scoring-admits-a-synthesized-buffer-at-a-byte-buffer-candidate`, at the end of this file.

<!-- test: a-synthesized-buffer-crosses-every-declared-byte-buffer-door -->
<!-- targets: x64-windows -->
### One synthesized buffer, through six declared byte-buffer doors
x64-windows only for `a-wide-byte-still-reads-a-compiler-synthesized-buffer`'s reason: `currentPath`
lowers to `GetCurrentDirectoryA`. The cwd differs per machine, so every assertion is on a LENGTH — the
failure this case guards is a refusal to compile.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

type Holder
	export var mem as __ManagedMemory

	export static function create() returns Holder
		return Holder{mem: try __ManagedDirectory.currentPath() otherwise panic("Holder.create: currentPath")}
	end 'create'
end 'Holder'

type Box uses T
	export var item as T

	export static function create(item T) returns Self
		return Self{item: item}
	end 'create'
end 'Box'

typealias MemBox = Box with Bytes

function cwd() returns __ManagedMemory
	return try __ManagedDirectory.currentPath() otherwise panic("cwd: currentPath")
end 'cwd'

function owned() returns __ManagedMemory
	return try __ManagedMemory.create(4, 1) otherwise panic("owned: create")
end 'owned'

function alwaysFails() returns __ManagedMemory throws FileReadError
	throw FileReadError.notFound
end 'alwaysFails'

function sizeOf(m __ManagedMemory) returns int
	return m.length()
end 'sizeOf'

function main() returns ExitCode
	let viaReturn = cwd()

	var viaReassign = try __ManagedMemory.create(4, 1) otherwise return 1
	viaReassign = try __ManagedDirectory.currentPath() otherwise return 1

	let rawForOtherwise = try __ManagedDirectory.currentPath() otherwise return 1
	let viaOtherwise = try alwaysFails() otherwise rawForOtherwise

	let rawForMatch = try __ManagedDirectory.currentPath() otherwise return 1
	let pick = 1
	let viaMatchArm = match pick 'arm'
		0 gives owned()
		default gives rawForMatch
	end 'arm'

	let viaFieldStore = Holder.create()

	let rawForBox = try __ManagedDirectory.currentPath() otherwise return 1
	let viaTypeArgument = MemBox.create(rawForBox)
	_ = viaTypeArgument

	return 0 if sizeOf(viaReturn) > 0 and sizeOf(viaReassign) > 0 and sizeOf(viaOtherwise) > 0 and sizeOf(viaMatchArm) > 0 and sizeOf(viaFieldStore.mem) > 0 else 2
end 'main'
```
```exitcode
0
```

### ⚠ A `Byte` TOO NARROW TO HOLD A BYTE IS NOT A BYTE — the ROSTER and the STRIDE both said yes

The declared side of the door asks three questions and every one of them is load-bearing. The roster
(`isByteElementName`) says whether the element is CALLED a byte; the stride
(`isBytePackedArrayInstance`) says whether the RECORD moves one byte at a time. Neither says whether the
element can HOLD one.

⛔ **MEASURED on `origin/main`: `typealias Byte = int(0 to 200)` passes the roster** — a roster is a
question about the name, and the base-name strip is what keeps `stdlib/File.maxon` compiling under a
contested `Byte` — **and `rangedAliasStorageBytes` gives `0 to 200` a ONE-BYTE slot**, so it passes the
stride test too. The door opened, `takes(cwd)` was accepted with no diagnostic, and `b.get(0)` — an
accessor typed `int(0 to 200)` — read back **223** out of a path byte. Exit 223, silently.
The R4.7 review had recorded this exact reading as CURED by the roster; it was cured for
`typealias Small = int(0 to 200)` and not for `typealias Byte = int(0 to 200)`, which is the more natural
name of the two. ⇒ the declared element's DECLARED BOUNDS are the third question
(`RangedAliasRegistry.holdsEveryByteInEveryFile`): a compiler-synthesized buffer is a run of raw bytes, so
what receives one must admit every value a byte has.

<!-- test: a-narrow-byte-does-not-admit-a-compiler-synthesized-buffer -->
<!-- targets: x64-windows -->
### A one-byte slot that cannot hold 255 is refused at the boundary
```maxon
typealias Byte = int(0 to 200)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	return takes(cwd)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'b': expected 'Array_Byte$0_200', got 'Array___ManagedByte'
```

### ⛔ THE EIGHTH SITE — OVERLOAD SCORING — DOES HAVE A DISTINGUISHING CASE

This file, and `ProgramSignatures.byteBufferBoundaryAdmits`, both once said that `overloadArgTypeFit`'s
answer was UNOBSERVABLE: *"a candidate it wrongly refuses is refused a second time by every sibling
candidate and the call resolves anyway"*. **The second half is false. When NO candidate fits, the call does
not resolve anyway — it resolves to the FIRST declared overload and reports the refusal against that one's
parameter.**

⚠ MEASURED on this branch, by putting the bare `namedAggregatesConflict` back into `overloadArgTypeFit`
alone and rebuilding: the program below stops compiling with
`E3005 argument type mismatch for 'x': expected 'Array_Integer', got 'Array___ManagedByte'` — quoting `x`,
the parameter of the overload the call was never written against. With the shared door in place it compiles
and picks the `__ManagedMemory` candidate. So the eighth site is load-bearing exactly like the other seven,
and it is now pinned rather than argued about.

<!-- test: overload-scoring-admits-a-synthesized-buffer-at-a-byte-buffer-candidate -->
<!-- targets: x64-windows -->
### Overload scoring picks the byte-buffer candidate for a compiler-synthesized buffer
x64-windows only for `a-wide-byte-still-reads-a-compiler-synthesized-buffer`'s reason: `currentPath` lowers
to `GetCurrentDirectoryA`. The `Ints` candidate is declared FIRST, so a resolver that scores the
`__ManagedMemory` one `incompatible` reports against `x` rather than selecting it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer

function which(x Ints) returns int
	return 11 if x.count() >= 0 else 12
end 'which'

function which(m __ManagedMemory) returns int
	return 37 if m.length() > 0 else 12
end 'which'

function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	return which(cwd) as ExitCode
end 'main'
```
```exitcode
37
```
