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
`Byte`, `__StringByte`), and NOT "anything that strides one" — the arriving side admits exactly the
compiler-owned, `__`-reserved `__StringByte`, which no source can declare and which carries no range of its
own, and the declared side admits exactly a one-byte-strided `Array` over one of those same two names
(`ProgramSignatures.byteBufferBoundaryAdmits`). The case below is what holds the arriving side shut.

⚠ **THE DECLARED SIDE NEEDED THE SAME ROSTER, AND IT WAS THE SAME HOLE POINTING THE OTHER WAY** (R4.7
review). Shipped with a bare stride test there, the door admitted a String's byte view into a parameter
declared `Array with Small`, and `b.get(0)` read back **223** from an element declared `int(0 to 200)` —
exit 223, no diagnostic. Both sides now ask the roster; the declared side ALSO keeps the stride test, which
is live and independently reachable (`typealias Byte = int(0 to 1000)` puts the NAME on the roster while the
record strides two, and the door refuses there).

⚠ **NONE OF THE VIEW-SIDE BEHAVIOUR CAN BE A CASE IN THIS FILE, AND THAT IS STRUCTURAL, NOT AN OMISSION.**
Producing an `Array with __StringByte` at all requires `String.addressableBytes()`, which
`Parser.requireStdlibOnlyStringMethod` refuses to any file not physically under `stdlib/`, and the spec
runner stages every fragment outside `stdlib/`. So the three measurements above were made by building
purpose-written files under `stdlib/`, and the suite could only reach the door once `stdlib/File.maxon` was
whitelisted — which is the acceptance half of R4.7, and which landed with the RANGE-scoped generic-instance
identity below. The case below is the part that IS reachable: it pins that two byte-packed aliases are
still two types.

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
### The stdlib's own byte buffer stays byte-PACKED while the program's `Byte` is two bytes wide
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

<!-- test: a-byte-two-files-disagree-about-is-two-types -->
### Two ranges for one name are two element types, and they are not interchangeable
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function takesMine(b Bytes) returns ExitCode
	return b.count()
end 'takesMine'

function main() returns ExitCode
	let path = FilePath from "test_widebyte_notmine.txt"
	try File.writeText(path, content: "Hi") otherwise 'writeErr'
		return 1
	end 'writeErr'
	let raw = try File.readBinary(path) otherwise 'readErr'
		return 2
	end 'readErr'
	return takesMine(raw)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/a-byte-two-files-disagree-about-is-two-types.test:17:9: argument type mismatch for 'b': expected 'Array_Byte$0_1000', got 'Array_Byte$0_255'
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
