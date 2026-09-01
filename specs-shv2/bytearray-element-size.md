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

<!-- test: bare-union-case-as-struct-field-init -->
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

### ⚠ AN ELEMENT MOVE IS NOT ONE LOOP — IT IS A RUN OF MACHINE WORDS AND A RUN OF BYTES, IN AN ORDER

`__managed_move_elems`' byte arm moves a WHOLE MACHINE WORD per trip and finishes the
`byteCount mod 8` left over one byte at a time (EC3). At a 1-byte stride that split is
reachable from ordinary source, and the three inserts above cannot see it: each shifts at
most five bytes, so the word run is EMPTY in all three and only the byte tail ever executes.
They pass with the word run deleted, with the two runs in either order, and with either run
walking the wrong way.

The two cases below shift TWENTY-ONE and TWENTY bytes across one buffer — the smallest sizes
at which the word run holds MORE THAN ONE chunk, the byte tail is non-empty, and the
destination overlaps the source. Each pins three separate facts at once: that the tail exists,
that each run walks in the direction the overlap needs, and that the two runs happen in the
right ORDER.

<!-- test: bytearray-insert-at-front-crosses-the-word-and-tail-boundary -->
### An `insert` that shifts two whole words AND a remainder, upward, onto itself
Twenty-one bytes shifted UP one is two machine words plus a five-byte remainder, and the
destination overlaps the source everywhere but its top byte. Three things have to be true and
each has its own way of going wrong:
* the remainder must be copied FIRST — it sits at the TOP of the range, and the highest word's
  store lands on `buffer[16]`, which is the remainder's own lowest source byte;
* the words must descend — the low word's store lands on `buffer[8]`, which the high word
  still has to read;
* the remainder must exist at all — without it the array keeps its old last five bytes.
```maxon
function main() returns ExitCode
	var bytes = b"abcdefghijklmnopqrstu"
	bytes.insert(0, value: 90)
	if bytes.count() != 22 'cnt'
		return 1
	end 'cnt'
	return 0 if String.from(bytes) == "Zabcdefghijklmnopqrstu" else 2
end 'main'
```
```exitcode
0
```

<!-- test: bytearray-remove-at-front-crosses-the-word-and-tail-boundary -->
### The same crossing DOWNWARD — a `remove` that shifts twenty bytes onto their own source
`remove(0)` moves the surviving twenty bytes DOWN one: two machine words followed by a
four-byte remainder, overlapping the source the other way. Every clause above is mirrored —
the WORDS must move first, because the remainder's first store lands on `buffer[16]`, which
the high word has not read yet; and the words must ASCEND, because the high word's store lands
on `buffer[8]`, which the low word still has to read.
```maxon
function main() returns ExitCode
	var bytes = b"abcdefghijklmnopqrstu"
	let gone = try bytes.remove(0) otherwise panic("test invariant: index 0 is in bounds")
	if gone != 97 'first'
		return 1
	end 'first'
	if bytes.count() != 20 'cnt'
		return 2
	end 'cnt'
	return 0 if String.from(bytes) == "bcdefghijklmnopqrstu" else 3
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
and the same one a widening `__managed_append` across differing strides would need — so until that exists the
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

<!-- test: byte-string-key-of-a-top-level-map-is-refused-when-byte-is-wider-than-one-byte -->
### The refusal reaches a byte-string KEY of a top-level map literal, which is a THIRD doorway
A top-level `[b"…": v]` is neither a function body's expression nor a bare `let G = b"…"`: the map
literal is a NODE the initializer sweep hands `__module_init`, so its key folds through
`evalConstFoldedValue` and nothing on the expression path is consulted. A route that admits a key
without asking the blob-fits-its-element rule would build a record striding 1 under a `Byte` striding
8 — the same incoherence the two cases above refuse, reached through the door this program opens.
```maxon
typealias Byte = int(0 to u64.max)
typealias Bytes = Array with Byte

var KEYWORDS = [b"hi": 1]

function main() returns ExitCode
	var made = Bytes.create()
	made.push(1)
	return made.count() + KEYWORDS.count()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:17: Unsupported: a `b"…"` byte-string literal in a program whose `Byte` is a 8-byte range: the literal's blob is byte-PACKED, so its record would stride 1 while every `Array with Byte` built by `.create()` strides 8 — two values of one type that behave differently. Declare `Byte` as `int(0 to u8.max)` (or any range that fits one byte), or build the array with `.create()` + `push`
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

### ⚠ THE STRIDE RULE HAS A NARROW TWIN, AND WITHOUT IT THE LITERAL IS A SILENT WRONG ANSWER

The section above is the WIDE half of one rule: a blob that is byte-PACKED by construction is a value of
`Array with Byte` only while `Byte` strides one byte. **The stride is only half of what a value has to
satisfy.** `rangedAliasStorageBytes` gives EVERY non-negative range that fits `u8.max` a one-byte slot, so
`typealias Byte = int(0 to 100)` strides 1 and passes that rule — and it cannot hold `223`.

⛔ **MEASURED on `origin/main` (A3r), on a compiler built without R4.7's boundary door at all, so this is
neither R4.7's nor N2's doing**: `takes(b"\xdf")` into `function takes(b Bytes)` returned **223** out of an
element declared `int(0 to 100)`. Exit 223, no diagnostic anywhere. It is the third sighting of one reading
in this file, and the first one that needs no compiler-synthesized buffer to reach it — a `b"…"` literal is
four keystrokes of ordinary source.

⇒ **EVERY BYTE OF THE BLOB IS A LITERAL VALUE BEING NARROWED INTO THE ELEMENT**, and it is checked exactly
as any other compile-time value narrowed into a ranged alias is: `TypeRules.literalInRange` against the
element's DECLARED bounds, reported as the same **E3005** a `300 as Byte` or a `made.push(2000)` earns. It
is deliberately NOT the wide side's E2015: that code says *"shv2 cannot emit this literal"*, which is true
of a wide `Byte` (an element-wise widening emission is a real mechanism and its own rung) and false here —
the emission is fine, the program is wrong.

⚠ **IT IS A PER-VALUE RULE AND NOT A PER-TYPE ONE.** `b"abc"` under `int(0 to 100)` is three bytes that all
fit, and it must keep compiling; refusing every literal a narrow `Byte` might not hold would be its own
wrong answer, pointing the other way. The three acceptance cases below are what hold that shut.

⚠ **AND IT IS ASKED AT BOTH OF THE LITERAL'S DOORS, WHICH IS WHY THE STRIDE RULE'S TWO THROW SITES BECAME
ONE RULE FUNCTION** (`Parser.requireByteStringBlobFitsItsElement`). A top-level `let`/`var` never reaches
the expression emitter — the initializer sweep folds it to bytes and `ModuleInit` builds the record — so a
rule wired to the emitter alone would let a stored byte-string global slip it, exactly as the stride rule's
own history records (measured: it did).

<!-- test: byte-string-literal-refused-when-a-byte-is-outside-the-elements-range -->
### A `b"…"` literal is refused when a byte does not fit this program's `Byte`
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	return takes(b"\xdf")
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/byte-string-literal-refused-when-a-byte-is-outside-the-elements-range.test:10:15: byte 223 at offset 0 of a `b"…"` byte-string literal is outside the range of 'Byte' (int(0 to 100))
```

<!-- test: byte-string-literal-checks-every-byte-not-only-the-first -->
### Every byte is checked, not just the first
The first byte fits and the second does not. A rule that looked at the blob's head — or at its WIDTH, which
`0xdf` passes since it is eight bits — reads this program as legal.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(1) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	return takes(b"\x41\xdf")
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/byte-string-literal-checks-every-byte-not-only-the-first.test:10:15: byte 223 at offset 1 of a `b"…"` byte-string literal is outside the range of 'Byte' (int(0 to 100))
```

<!-- test: byte-string-literal-accepted-at-the-elements-exact-maximum -->
### The element's exact maximum is IN range
The boundary is inclusive, and this is the case that separates `<=` from `<`.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	return 0 if takes(b"\x64") == 100 else 1
end 'main'
```
```exitcode
0
```

<!-- test: byte-string-literal-refused-one-past-the-elements-maximum -->
### …and one past it is not
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	return takes(b"\x65")
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/byte-string-literal-refused-one-past-the-elements-maximum.test:10:15: byte 101 at offset 0 of a `b"…"` byte-string literal is outside the range of 'Byte' (int(0 to 100))
```

<!-- test: byte-string-literal-of-in-range-bytes-still-compiles -->
### A narrow `Byte` that genuinely admits the literal's bytes keeps compiling
`a`, `b` and `c` are 97, 98 and 99, all inside `int(0 to 100)`. A refusal derived from the element's TYPE
rather than from the literal's VALUES would reject this program, which is a wrong answer pointing the
other way.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(2) otherwise 0)
end 'takes'

function main() returns ExitCode
	return 0 if takes(b"abc") == 99 else 1
end 'main'
```
```exitcode
0
```

<!-- test: an-empty-byte-string-literal-fits-any-byte-element -->
### An EMPTY literal has no byte to be out of range
The degenerate end of a per-value rule: nothing to check, so nothing to refuse.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return b.count() as ExitCode
end 'takes'

function main() returns ExitCode
	return takes(b"")
end 'main'
```
```exitcode
0
```

<!-- test: byte-string-global-refused-when-a-byte-is-outside-the-elements-range -->
### The refusal reaches a top-level byte-string global, which no function body ever parses
The narrow twin of `byte-string-global-refused-when-byte-is-wider-than-one-byte`, and it is here for the
same measured reason: a file-scope `let`/`var` is folded to bytes by the initializer sweep and its record
is built by `__module_init`, so it never reaches the expression-position emitter.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

var BUFFER = b"\xdf"

function main() returns ExitCode
	var made = Bytes.create()
	made.push(1)
	BUFFER.push(2)
	return made.count() + BUFFER.count()
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/bytearray-element-size/byte-string-global-refused-when-a-byte-is-outside-the-elements-range.test:5:14: byte 223 at offset 0 of a `b"…"` byte-string literal is outside the range of 'Byte' (int(0 to 100))
```

<!-- test: readers-own-byte-decides-which-literal-bytes-fit -->
### A byte legal under ONE file's `Byte` stays legal there while a sibling file's is narrower
The exact analogue of `readers-own-byte-decides-the-literal-not-the-whole-program-fold` for the RANGE half
of the rule: `wide.maxon`'s literal is checked against `wide.maxon`'s `int(0 to u8.max)`, and `main.maxon`'s
against its own `int(0 to 100)`. A whole-program fold over both declarations — or a check that read the
declaring file of whichever `Byte` was recorded last — gets one of the two wrong.
```maxon
// --- file: wide.maxon
typealias Byte = int(0 to u8.max)

export function anyByte(b Byte) returns Integer
	var a = b"\xdf"
	return (try a.get(0) otherwise 0) - b
end 'anyByte'

typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
typealias Byte = int(0 to 100)

function narrow(b Byte) returns Integer
	return b
end 'narrow'

function main() returns ExitCode
	var mine = b"\x41"
	return anyByte(narrow(try mine.get(0) otherwise 0)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
158
```

<!-- test: the-narrow-files-own-byte-literal-is-still-refused -->
### …and the identical literal moved into the NARROW file meets its own declaration's wall
The half that proves the case above is not simply a lost refusal. One program, two files, two answers.
```maxon
// --- file: wide.maxon
typealias Byte = int(0 to u8.max)

export function anyByte(b Byte) returns Integer
	var a = b"\xdf"
	return (try a.get(0) otherwise 0) - b
end 'anyByte'

typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
typealias Byte = int(0 to 100)

function narrow(b Byte) returns Integer
	return b
end 'narrow'

function main() returns ExitCode
	var mine = b"\xdf"
	return anyByte(narrow(try mine.get(0) otherwise 0)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:19:13: byte 223 at offset 0 of a `b"…"` byte-string literal is outside the range of 'Byte' (int(0 to 100))
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
error E3005: specs/fragments/bytearray-element-size/byte-packed-alias-is-not-interchangeable-with-byte.test:14:9: argument type mismatch for 'b': expected 'Bytes', got 'Smalls'
```

### ⚠ A `Byte` TWO FILES DISAGREE ABOUT IS TWO ELEMENT TYPES, NOT ONE WIDE ONE

`Array with Byte` is interned on the element's NAME, so one interned instance carries one
`element_size@24` — which is why the whole-program stride is a fold over every declaration
(`RangedAliasRegistry.storageBytesInEveryFile`, "a record created in one file is pushed, appended, read
and dropped in another, and they must stride identically"). That fold is correct and it is not enough.

**MEASURED**: with `stdlib/File.maxon` loaded, a program declaring `typealias Byte = int(0 to
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

⚠ **THE MINT IS STILL THE INSTANCE'S IDENTITY; IT IS NO LONGER WHAT AN E3005 *SAYS* (user ruling,
2026-08-04).** A diagnostic names a type by the `typealias` the AUTHOR declared for it
(`ProgramSignatures.instanceDisplayName`), so the two cases below now read `expected 'Bytes', got
'ByteArray'` rather than `expected 'Array_Byte$0_1000', got 'Array_Byte$0_255'` — two spellings a person
wrote, naming the same two instances the mint named. What they pin is unchanged and is the point: whether
the program holds ONE element type or TWO. The mint is quoted only where no source line names the instance
at all, which is the `Array___ManagedByte` and `Array_Byte$0_1000` halves further down.

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
<!-- targets: x64-windows, arm64-macos -->
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
<!-- targets: x64-windows, arm64-macos -->
### A buffer the COMPILER minted answers to no file's `Byte` — not even the file that asked for it
The case above proves a STDLIB MODULE's buffer survives a wide `Byte`. This one proves the
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
error E3005: specs/fragments/bytearray-element-size/a-byte-two-files-disagree-about-is-two-types.test:12:9: argument type mismatch for 'b': expected 'Bytes', got 'ByteArray'
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
error E3005: specs/fragments/bytearray-element-size/an-agreeing-byte-keeps-the-bare-element-name.test:10:9: argument type mismatch for 'b': expected 'Bytes', got 'int'
```

### ⚠ WHEN ONE NAME IS TWO TYPES, THE BARE NAME IS NOT AN ANSWER — IT IS THE ABSENCE OF ONE

**The two cases above rest on the bare spelling `Bytes` naming exactly one type in the program.** The
user ruling of 2026-08-04 says what happens when it does not: *"if a name is possibly ambiguous it
needs to contain its full namespace"*. Without a qualifier the refusal reads **`expected 'Bytes', got
'Bytes'`** — a sentence with no content, which is the one outcome the whole display door exists to
prevent, and which no case in this file could see because every case above is single-file or names
two DIFFERENT aliases.

The two cases below are the two shapes the qualifier takes, and they are separate because the tier is
chosen by a MEASUREMENT of the program (`contestedAliasNamespacesAreDistinct`) rather than fixed: a
namespace only tells the claimants apart when the claimants are in different modules, and a
non-exported `typealias` is FILE-local, so two files in one directory can contest a name while sharing
a namespace. Each case would go CONTENTLESS — both sides spelling the same word — if its tier
regressed.

<!-- test: error.a-contested-alias-is-qualified-by-its-namespace -->
### Claimants in different modules: the NAMESPACE is the qualifier
`api/lib.maxon` and `app/main.maxon` each declare `Bytes` over their own `Byte`, and those are two
element types. The namespaces `api` and `app` are distinct, so they are what an author recognizes and
what the refusal quotes.
```maxon
// --- file: api/lib.maxon
export typealias Byte = int(0 to 1000)
export typealias Bytes = Array with Byte

export function takesWide(b Bytes) returns ExitCode
	return b.count() as ExitCode
end 'takesWide'

// --- file: app/main.maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var mine = Bytes.create()
	mine.push(65)
	return takesWide(mine)
end 'main'
```
```maxoncstderr
error E3005: app/<fragment>:17:9: argument type mismatch for 'b': expected 'api.Bytes', got 'app.Bytes'
```

<!-- test: error.a-contested-alias-in-one-module-is-qualified-by-its-file -->
### Claimants in ONE module: the DECLARING FILE is the qualifier
The same contest with both declarations in `pkg/`, where a namespace-qualified spelling would render
`expected 'pkg.Bytes'` on both sides — the contentless refusal — so the qualifier falls back to the
file, which IS the scope that resolves a file-local `typealias`.

⚠ **THE WRONG ARGUMENT HERE IS AN `int`, NOT THE OTHER CLAIMANT, AND THAT IS WHAT MAKES THE CASE
PINNABLE.** This runner normalizes every staged file's NAME to the `<fragment>` token and keeps only
its directory (`FragmentPathMapping`), so a refusal quoting both claimants' file paths reads
`'pkg/<fragment>.Bytes'` on both sides here whatever the compiler emitted — the tier would be
invisible. Quoting ONE side against an `int` keeps the whole qualifier observable: a regression to the
bare name reads `expected 'Bytes'` and a regression to the namespace tier reads `expected 'pkg.Bytes'`,
and both fail this expectation. **MEASURED outside the harness, where file names survive:
`expected 'temp/amb2/a.maxon.Bytes', got 'temp/amb2/main.maxon.Bytes'` — the two paths are distinct
and it is the runner's normalization, not the compiler, that collapses them.**
```maxon
// --- file: pkg/lib.maxon
export typealias Byte = int(0 to 1000)
export typealias Bytes = Array with Byte

export function takesWide(b Bytes) returns ExitCode
	return b.count() as ExitCode
end 'takesWide'

// --- file: pkg/main.maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var mine = Bytes.create()
	mine.push(65)
	return takesWide(7) + mine.count() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: pkg/<fragment>:17:9: argument type mismatch for 'b': expected 'pkg/<fragment>.Bytes', got 'int'
```

<!-- test: error.a-returned-bytes-answers-to-the-declaring-file-not-the-caller -->
### A RETURNED `Bytes` is the callee's, not the caller's
⛔ **THIS PROGRAM COMPILED AND RAN, AND ITS ANSWER WAS WRONG — MEASURED.** `wide.maxon` builds an
`Array with int(0 to 1000)` (two-byte stride) holding **300**; `main.maxon` reads it through a
parameter declared over its OWN `int(0 to u8.max)` (one-byte stride) and got **44** — the low byte —
on a program the whole element-identity family exists to refuse. Both other directions of the same
contest were already refused (`a-byte-two-files-disagree-about-is-two-types`, and the two cases
above), so what this pins is not the RULE but the one door the rule could not reach.

The declaration sweep records `returns Bytes` before any generic alias is interned, so it stores a
bare `named("Bytes")` — and the CALLER's parse then resolved that name as ITS file means
(`Parser.resolveNamedAlias` asks `genericAliasInstanceFrom(name, readerFilePath: self.filePath)`).
A struct FIELD and a union PAYLOAD were already re-resolved against their own declaring file
(`resolveRecordedGenericAliasTypes`, whose header states the rule: *the scope file is the SLOT's
declaring file and never a reader's*); a return type is a slot of the declaring FUNCTION and was the
one recorded declared type that pass did not reach.
The two files sit in different modules so the refusal spells both sides apart — the runner normalizes a
staged file's NAME away but keeps its directory, so a same-directory pair would read `<fragment>.Bytes`
on both sides and say nothing about which instance won.
```maxon
// --- file: api/wide.maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

export function makeWide() returns Bytes
	var b = Bytes.create()
	b.push(300)
	return b
end 'makeWide'

// --- file: app/main.maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function takesNarrow(b Bytes) returns ExitCode
	return try b.get(0) otherwise 0
end 'takesNarrow'

function main() returns ExitCode
	return takesNarrow(makeWide())
end 'main'
```
```maxoncstderr
error E3005: app/<fragment>:21:9: argument type mismatch for 'b': expected 'app.Bytes', got 'api.Bytes'
```

### ⚠ THE READER'S OWN `Byte` DECIDES, AND THE MINT IS NEVER QUOTED BACK AT THE AUTHOR

**EVERY CASE ABOVE IS SINGLE-FILE, AND A SINGLE-FILE PROGRAM CANNOT TELL THE TWO RULES APART.** When
the only user declaration of `Byte` is the one the literal is written beside, the reader's range and
the whole-program fold (`RangedAliasRegistry.storageBytesInEveryFile`, the MAX over every
declaration) are the SAME number — so the two wide-`Byte` refusals above pass identically against a
compiler that scopes the stride to the reader and against one that folds it, and neither pins which
is running. The four cases here are the two-file shapes that separate them, and they are the ones
that would go quiet if `Parser.requireByteStringBlobFitsItsElement` ever stopped resolving through
`ProgramSignatures.internArrayByteInstance(readerFilePath)`. The RANGE half of that rule has the same
exposure and its own pair — `readers-own-byte-decides-which-literal-bytes-fit` and its twin, above.

<!-- test: readers-own-byte-decides-the-literal-not-the-whole-program-fold -->
### A `b"…"` in a file with no `Byte` of its own stays packed while a SIBLING file is wide
`main.maxon` declares no `Byte`, so it means `stdlib/File.maxon`'s `int(0 to u8.max)` and its literal
is legal. Under a whole-program fold `wide.maxon`'s `int(0 to 1000)` would widen the one shared
`Array with Byte` to stride 2 and this program would be E2015 — the exact "adding a declaration
breaks a file that never mentions it" failure the scoping exists to end, in its smallest form.
```maxon
// --- file: wide.maxon
typealias Byte = int(0 to 1000)

export function widen(b Byte) returns Integer
	return b
end 'widen'

typealias Integer = int(i64.min to i64.max)
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

export function widen() returns Integer
	var a = b"hi"
	return try a.get(0) otherwise 0
end 'widen'

typealias Integer = int(i64.min to i64.max)
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

	export function first() returns Integer
		return try self.buf.get(0) otherwise 0
	end 'first'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
typealias Byte = int(0 to 1000)

function widen(b Byte) returns Integer
	return b
end 'widen'

function main() returns ExitCode
	let h = Holder.ofText("ab")
	return (h.first() - widen(50)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
47
```

<!-- test: a-buffer-field-refuses-another-files-byte-at-the-construction -->
### …and the disagreement lands LOUDLY, at the construction
The same two files with the buffer handed IN rather than built inside `holder.maxon`. `main.maxon`'s
`c.bytes()` is its OWN `Byte`, two bytes wide, and the field is one byte wide — so the two
files meet nominally, at the line that hands the record over, instead of silently striding one
record two ways. This is the refusal that pays for the case above.

⚠ **THE PRODUCER WAS `"ab".toByteArray()` UNTIL W49 WAVE 6 AND IS NOW A `Character`'s BYTES**, because a
`String`'s bytes are `stdlib/String.maxon`'s `ByteArray` and answer to the CORPUS's canonical `Byte`,
which is the field's own one byte — so the two files would agree and there would be no disagreement to
land. `Character.bytes()` is the producer still typed at the reading file's `Byte`, which is the property
this case is about. The case above keeps its `toByteArray()` and keeps passing, because it builds the
buffer inside `holder.maxon` where the corpus's `Byte` and that file's `Byte` are the same one byte.
```maxon
// --- file: holder.maxon
export type Holder
	export var buf as __ManagedMemory

	export static function of(b __ManagedMemory) returns Holder
		return Holder{buf: b}
	end 'of'

	export function first() returns Integer
		return try self.buf.get(0) otherwise 0
	end 'first'
end 'Holder'

typealias Integer = int(i64.min to i64.max)
// --- file: main.maxon
typealias Byte = int(0 to 1000)

function widen(b Byte) returns Integer
	return b
end 'widen'

function main() returns ExitCode
	let c = 'a'
	let h = Holder.of(c.bytes())
	return (h.first() - widen(50)) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```maxoncstderr
error E3005: <fragment>:25:17: argument type mismatch for 'b': expected 'ByteArray', got 'Array_Byte$0_1000'
```

### The MINT is an internal spelling, and a diagnostic quotes it only when nothing else can tell two types apart

`Byte$0_255` is the compiler's name for ONE declaration of `Byte`; it is unspellable in source
(`SignatureIndex.RangeQualifiedAliasSeparator`) and no author ever wrote it. **A TYPE-IDENTITY message
prefers the `typealias` the author DID write** (user ruling, 2026-08-04 — `ProgramSignatures.instanceDisplayName`),
which is why the case above reads `expected 'ByteArray'`: `stdlib/File.maxon` declares
`export typealias ByteArray = Array with Byte` over the instance the parameter is typed at, and that is a
name the program contains. The `got` half has no such name — `"ab".toByteArray()` mints
`Array with Byte$0_1000` and no line of either file declares an alias for it — so the mint stands there,
and it must: the bare spelling would read `expected 'Array_Byte', got 'Array_Byte'`, a refusal with no
content. **That is the whole rule: an author's spelling where one exists, the mint where none does, and
never a message whose two halves are the same string.**
A RANGE message quotes the mint in neither case: it quotes a `typealias` back at the author, and its
bounds are printed in the same sentence, so the suffix carried nothing the reader had not already been
told.

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

function feed(v Integer) returns Integer
	var made = Bytes.create()
	made.push(v)
	return try made.get(0) otherwise 0
end 'feed'

function main() returns ExitCode
	return feed(2000) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
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

⚠ Those six sentences are QUOTED AS MEASURED and predate the display rule of 2026-08-04, so the DECLARED side
of each still reads `Array_Byte` where it would now read the `typealias` its program wrote. The `got` side is
unchanged in every one — a compiler-synthesized buffer has no declaration to quote, so its canonical mint IS
its display name. The reading here is about WHICH DOORS asked the boundary, which no spelling changes.

The BOOTSTRAP compiles every one of them. The cure is the door asked ONCE — `aggregatesConflict` now takes
the (tag, nameId) pair beside each side's aggregate name and folds the byte boundary in, so a site cannot
ask the identity question without asking the boundary one. Overload SCORING is the eighth site, it carries
the door for its own stated promise (*"a candidate this function accepts cannot then be rejected
downstream"*), and it too has a distinguishing case —
`overload-scoring-admits-a-synthesized-buffer-at-a-byte-buffer-candidate`, at the end of this file.

<!-- test: a-synthesized-buffer-crosses-every-declared-byte-buffer-door -->
<!-- targets: x64-windows, arm64-macos -->
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

function sizeOf(m __ManagedMemory) returns Integer
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
typealias Integer = int(i64.min to i64.max)
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
<!-- targets: x64-windows, arm64-macos -->
### A one-byte slot that cannot hold 255 is refused at the boundary
```maxon
typealias Byte = int(0 to 200)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	return takes(cwd)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:9: argument type mismatch for 'b': expected 'Bytes', got 'Array___ManagedByte'
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
the parameter of the overload the call was never written against. (That measurement predates the display
rule of 2026-08-04; the same refusal would now name the author's `typealias Ints` on the expected side. The
reading is about WHICH parameter is quoted, which no spelling changes.) With the shared door in place it compiles
and picks the `__ManagedMemory` candidate. So the eighth site is load-bearing exactly like the other seven,
and it is now pinned rather than argued about.

<!-- test: overload-scoring-admits-a-synthesized-buffer-at-a-byte-buffer-candidate -->
<!-- targets: x64-windows, arm64-macos -->
### Overload scoring picks the byte-buffer candidate for a compiler-synthesized buffer
x64-windows only for `a-wide-byte-still-reads-a-compiler-synthesized-buffer`'s reason: `currentPath` lowers
to `GetCurrentDirectoryA`. The `Ints` candidate is declared FIRST, so a resolver that scores the
`__ManagedMemory` one `incompatible` reports against `x` rather than selecting it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Ints = Array with Integer

function which(x Ints) returns Integer
	return 11 if x.count() >= 0 else 12
end 'which'

function which(m __ManagedMemory) returns Integer
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

### ⛔ A `Byte` THAT CANNOT HOLD EVERY BYTE MAY NOT RECEIVE A RAW-BYTE FILL (A4a)

Everything above this line is about a value the source SPELLS — a `b"…"` blob, whose every byte is
checked against the element's declared bounds (A3r) — or about a buffer the COMPILER minted, which
wears `__ManagedByte` and crosses the boundary door on its own terms (R4.7). **Neither reaches a
producer that fills a `Array with Byte` with bytes that exist only at RUN TIME.**

⛔ **MEASURED on `origin/main` WITH A3r's fix already in it**: `typealias Byte = int(0 to 100)`, and
`takes("ß".toByteArray())` into `function takes(b Bytes)` returned **195** — a UTF-8 continuation byte
read back through an accessor declared `int(0 to 100)`. Exit 195, no diagnostic. `.bytes()` is the same
emitter and returned the same 195. Two more spellings reach the identical reading with no String in
sight: `__ManagedMemory.create(8, 1)` then `setByte(0, 223)`, and `b"abc".managed.setByte(0, 223)` —
both **223**, both silent.

⇒ **THE RULE IS PER-TYPE, WHERE A3r's IS PER-VALUE, AND THE TWO MUST NOT BE MERGED.** There is no
literal here to inspect: `__str_to_bytes` blits the receiver's UTF-8 and `__mf_read` blits a file's
contents, so what has to be asked is a question about the ELEMENT — *can it hold every value a byte
has?* — and it is asked at the RAW-BYTE WRITE. A3r must stay per-value for the reason its own section
gives: `b"abc"` under `int(0 to 100)` is three bytes that all fit and must keep compiling.

⛔ **AND THE ELEMENT MAY NOT BE MOVED TO `__ManagedByte` INSTEAD, WHICH IS THE OBVIOUS CURE AND IS
WRONG.** A byte view is NOT a compiler-minted buffer of the `__ManagedByte` kind: `emitArrayCreateOp`
stamps its `element_size@24` from the very instance it is typed as, so a wide `Byte` gives a genuinely
stride-2 record and `__str_to_bytes` fills it at that stride — **MEASURED correct**, `b0=97 b1=98 b2=99`
under `typealias Byte = int(0 to 1000)`. Retyping the view `Array with __ManagedByte` would make it
stride 1 and `byteBufferBoundaryAdmits`'s stride test would then REFUSE it at every declared `Bytes`
position — turning a program that answers correctly today into a compile error. The interning
(`internArrayByteInstance`) is deliberate and it is right; only the BOUNDS were never asked.
`a-wide-byte-still-materializes-a-byte-view` below is what holds that shut.

⚠⚠ **THE SUBJECT OF ALL OF THIS IS NOW `Character.bytes()` AND NOT A `String`'s BYTES, AND THAT MOVE IS
W49 WAVE 6 RETIRING THE THREE `String` VIEWS ONTO `stdlib/String.maxon`.** Every case below used to write
`"ß".toByteArray()`; that call is now `stdlib/String.maxon:156`, which answers with the CORPUS's own
`ByteArray` over the corpus's canonical `Byte = int(0 to u8.max)` — a type fixed by the module that
declares it, not by the file that reads it. So a narrow or wide `Byte` in the READING file no longer
reaches a `String`'s bytes at all, and the rule has nothing to gate there.

⭐ **THE SILENT WRONG ANSWER A4a CLOSED IS STILL CLOSED, BY A STRICTLY EARLIER REFUSAL, AND THAT IS
MEASURED RATHER THAN INFERRED.** Under `typealias Byte = int(0 to 100)`, `takes("ß".toByteArray())` into
`takes(b Bytes)` is now `E3005 … expected 'Bytes', got 'ByteArray'` and `takes("ß".bytes())` is
`E3005 … got 'ByteView'`. There is no fill into a narrow element to gate because the value was never typed
at that element. ⚠ **The ORACLE still returns the silent 195 for the first of those** (measured on the same
tree), so shv2 is not converging onto the reference here — it is ahead of it, by a different route than
E3117 took.

⇒ **E3117 IS NOT DEAD; ITS SURFACE NARROWED TO THE PRODUCERS THAT STILL MINT THE READING FILE'S `Byte`** —
`Character.bytes()` (`Parser.parseByteView`, the emitter's one remaining caller) and
`__ManagedFile.read`. The cases below are retargeted onto the first of those rather than deleted, because
the RULE is unchanged and only its reachable producers moved.

⚠ **THE READER IS UNTOUCHED, AND UNLIKE E3110's PAIR THAT IS NOT AN OVERSIGHT.** `byteAt` yields
`ValueTypeTag.integer` with no name — a plain unranged `int`, never the element — so a raw byte read
back is honest whatever `Byte` was declared to be. Only the WRITE puts a value into a slot the array
surface reads through the element's declared range. `raw-byte-reads-survive-a-narrow-byte` pins it.

<!-- test: a-byte-view-is-refused-when-byte-cannot-hold-every-byte -->
### `bytes()` is refused when this program's `Byte` cannot hold every byte
The receiver is a `Character` because that is the one receiver still served by `Parser.parseByteView`,
and therefore the one whose byte view is typed at the READING file's `Byte`. `'ß'` is two bytes and its
continuation byte is 159, well past `int(0 to 100)`.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return try b.get(0) otherwise 0
end 'takes'

function main() returns ExitCode
	let c = 'ß'
	return takes(c.bytes())
end 'main'
```
```maxoncstderr
error E3117: <fragment>:11:17: 'bytes' stores RAW bytes into an element declared 'Byte' (int(0 to 100)), which does not hold every byte value 0 to 255 — widen the element's declared range to store raw bytes through it
```

<!-- test: error.the-two-string-spellings-name-their-own-corpus-types -->
### The two `String` spellings are refused too — at the TYPE, and each names its own corpus return
⚠ **THIS CASE WAS `the-bytes-spelling-is-refused-identically` AND PINNED E3117 ON `"ß".bytes()` UNTIL W49
WAVE 6, ON THE GROUND THAT `bytes` AND `toByteArray` REACHED ONE EMITTER "precisely so the two spellings
cannot come to disagree about what a byte view IS". The RENAME is the point rather than tidying:**
They no longer do, and they no longer are one thing: `stdlib/String.maxon:156` copies into a `ByteArray`
and `:481` hands back a LAZY `ByteView` holding the String, which is the distinction the reference always
drew and shv2 could not. So the refusal moved a whole stage earlier and the two halves now differ in
exactly the way that is worth reading — the message names which one you wrote.

⭐ **The A4a reading is still refused, which is the load-bearing half**: the silent **195** this section
opens with cannot be reached through either spelling, and it needs no rule of its own to say so.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takesArray(b Bytes) returns ExitCode
	return try b.get(0) otherwise 0
end 'takesArray'

function main() returns ExitCode
	let viaCopy = takesArray("ß".toByteArray())
	let viaView = takesArray("ß".bytes())
	return viaCopy + viaView
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:16: argument type mismatch for 'b': expected 'Bytes', got 'ByteArray'
error E3005: <fragment>:11:16: argument type mismatch for 'b': expected 'Bytes', got 'ByteView'
```

<!-- test: a-byte-view-is-accepted-at-the-canonical-byte -->
### The canonical `Byte` holds every byte, so the view is untouched
⚠ Since W49 wave 6 this case passes for a SECOND reason as well as its original one, and both are worth
having: `Byte = int(0 to u8.max)` is the corpus's own canonical `Byte`, so `Bytes` and
`stdlib/String.maxon`'s `ByteArray` intern to ONE `GenericInstanceId` (`genericInstances.intern` is keyed
on `(typeNameId, args)` program-wide) and the argument is accepted nominally — as well as holding every
byte, which is what the case was written to say.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	return takes("abc".toByteArray())
end 'main'
```
```exitcode
97
```

<!-- test: a-wide-byte-still-materializes-a-byte-view -->
### A WIDE `Byte` strides two and the view fills it correctly — this is what rules out `__ManagedByte`
`int(0 to 1000)` HOLDS every byte, so the rule says nothing about it; the record strides two and
`__str_to_bytes` writes at that stride. The reads below are the measurement, and they are the
reason the byte view keeps the program's own `Byte` as its element rather than the compiler's.

⚠ **THE RECEIVER IS A `Character` SINCE W49 WAVE 6, AND WITH A `String` THE PROGRAM IS NOW REFUSED.**
`takes("abc".toByteArray())` under this same wide `Byte` is `E3005 … expected 'Bytes', got 'ByteArray'`
— **and that is what the ORACLE answers on the identical program, byte for byte**, so the refusal is
convergence and not a loss. What must not be lost is the argument this case exists for, and it is about
the emitter rather than about the receiver: retyping the view `Array with __ManagedByte` would stride it
1 and `byteBufferBoundaryAdmits` would refuse it at every declared `Bytes` position. `'ß'` is two bytes,
both past 127, so a stride-1 record could not hold them apart.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	let first = try b.get(0) otherwise 0
	let second = try b.get(1) otherwise 0
	return 0 if b.count() == 2 and first == 195 and second == 159 else 1
end 'takes'

function main() returns ExitCode
	let c = 'ß'
	return takes(c.bytes())
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-view-round-trips-through-string-from -->
### A program that declares no `Byte` round-trips a String through its bytes
```maxon
function main() returns ExitCode
	print(String.from("hi".toByteArray()))
	return 0 as ExitCode
end 'main'
```
```stdout
hi
```
```exitcode
0
```

<!-- test: a-narrow-byte-refuses-a-raw-byte-write-through-the-buffer-surface -->
### `setByte` is a raw-byte write, so a narrow `Byte` refuses it too
No String anywhere: `__ManagedMemory` IS `Array with Byte`, so a buffer built in this file carries this
file's element and `setByte` writes past its declared range.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	var buf = try __ManagedMemory.create(8, 1) otherwise return 1
	try buf.setLength(4) otherwise return 2
	try buf.setByte(0, 223) otherwise return 3
	return takes(buf)
end 'main'
```
```maxoncstderr
error E3118: <fragment>:12:10: 'setByte' writes a RAW BYTE at a byte OFFSET, so what the element's own accessors read back is any bit pattern of its 1-byte slot — every value of int(0 to 255). The element 'Byte' (int(0 to 100)) does not admit all of them. Widen the element's declared range to cover its whole slot, or store through the `Array` surface's `set`, which range-checks each value against the element
```

<!-- test: a-narrow-byte-refuses-a-raw-byte-write-into-a-literals-buffer -->
### The buffer surface of a `b"…"` literal is the same write, and A3r could not reach it
A3r checks the blob's own bytes and they all fit. `.managed` then hands the very same record a raw-byte
writer, which puts 223 into an element declared `int(0 to 100)` after the literal has been approved.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	var v = b"abc"
	try v.managed.setByte(0, 223) otherwise return 3
	return takes(v)
end 'main'
```
```maxoncstderr
error E3118: <fragment>:11:16: 'setByte' writes a RAW BYTE at a byte OFFSET, so what the element's own accessors read back is any bit pattern of its 1-byte slot — every value of int(0 to 255). The element 'Byte' (int(0 to 100)) does not admit all of them. Widen the element's declared range to cover its whole slot, or store through the `Array` surface's `set`, which range-checks each value against the element
```

<!-- test: a-wide-byte-still-writes-raw-bytes-through-the-buffer-surface -->
### A `Byte` WIDER than a byte keeps its raw writer, as long as it fills its slot
⚠ **THE PROGRAM THIS CASE PINNED AT A4a IS NOW REFUSED, TWICE OVER, AND DELIBERATELY** — it declared
`int(0 to 1000)` and built its buffer with `__ManagedMemory.create(8, 1)`, which is the byte-PACKED
producer this file's `create`/literal section now refuses, and its `setByte` then wrote into a 2-byte slot
the element does not fill. Both refusals are pinned below in their own cases. What the case was FOR — that
a range wider than a byte is not by itself a reason to refuse a raw write, which is the over-refusal A4a
had to be pulled back from — survives unchanged here: `int(0 to u16.max)` is wider than a byte and admits
every value of the two-byte slot it was given.

⚠ **THE RETURN NEEDS NEITHER A MASK NOR A CAST — BUT THE REASON IS NOW THE PLATFORM'S, AND AN EARLIER
DRAFT OF THIS PARAGRAPH STATED ONE PLATFORM'S REASON AS THE TYPE'S.** It read *"`ExitCode` now carries
`int(0 to u32.max)`, so `int(0 to u16.max)` fits it outright"* — true on Windows and **inverted
everywhere else** since BATCH27, where `ExitCode` is `int(0 to 255)` and `int(0 to u16.max)` does not
fit it at all. The two lanes reach "no cast" by different routes, and the case behaves identically on
both:

- on **x64-windows**, `rangeCoversRange` proves the containment, so the return is elided and writing
  `as ExitCode` here would be E3010 — an unneeded cast, exactly like the five X6 removed from this file;
- on **Linux, macOS and WASI**, nothing is proved and the return carries the ordinary runtime guard
  (`range-check-panic.md`). `223` is inside `int(0 to 255)`, so it passes — a guard is not a refusal, and
  the assertion below is the same **223** on every target.

The `and u8.max` this case carried for one draft is dead for a second, arithmetic reason —
`push(0)` then `setByte(0, …)` leaves the HIGH byte zero, so there is nothing above `u8.max` to mask off.
The case that genuinely reads a two-byte slot back is `a-raw-byte-write-is-accepted-when-the-element-fills-
its-two-byte-slot`, which writes at offset 1 and asserts **57089**.

⚠ **THIS CASE IS NOT THE CONTROL AGAINST A WIDENED E3117, AND AN EARLIER DRAFT OF THIS PARAGRAPH SAID IT
WAS — MEASURED FALSE (A4c review).** `rangeHoldsEveryByte(0, 65535)` is TRUE, so a naively widened E3117
would accept this program exactly as E3118 does; the case that separates the two rules is
`a-raw-byte-write-still-stages-a-machine-word-element` below, whose `int(i64.min to i64.max)` fails the
every-byte question and passes the slot question. E3117's own live site agrees: under this same
`typealias Byte = int(0 to u16.max)`, `"abc".toByteArray()` compiles and reads back `97 98 99`.
```maxon
typealias Byte = int(0 to u16.max)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	var buf = Bytes.create()
	buf.push(0)
	try buf.managed.setByte(0, 223) otherwise return 3
	return takes(buf)
end 'main'
```
```exitcode
223
```

<!-- test: raw-byte-reads-survive-a-narrow-byte -->
### `byteAt` yields a plain `int`, never the element, so a narrow `Byte` does not touch it
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	var buf = try __ManagedMemory.create(8, 1) otherwise return 1
	try buf.setLength(4) otherwise return 2
	let raw = try buf.byteAt(0) otherwise return 3
	return 0 if raw == 0 and takes(buf) == 0 else 4
end 'main'
```
```exitcode
0
```

<!-- test: a-narrow-byte-refuses-a-file-read-into-its-buffer -->
### `__ManagedFile.read` blits a file's contents, so it answers to the element too
The third raw-byte fill, and the one with no String and no literal in it: `__mf_read` writes whatever is
in the file into the caller's buffer, and that buffer is `Array with Byte` over THIS file's element.
`write` is deliberately not gated beside it — it reads the buffer OUT and stores nothing, which is the
same reason `byteAt` is untouched.

⚠ **NO `targets:` MARKER, AND THAT IS MEASURED RATHER THAN ASSUMED.** `__ManagedFile` is x64-windows-only
at this rung, so an off-Windows compile of this program normally reports six `E3104`s. It reports NONE
here: the refusal is a parse-time `ParseError`, which lands before the target-support pass runs, so this
stderr is byte-identical on every target. The path literal is UPPERCASE for A3r's rule, not for style —
`b"data.bin"` holds `t` (116), which this file's `Byte` does not.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	let f = try __ManagedFile.openRead(b"DATA.BIN".managed) otherwise return 3
	var buf = try __ManagedMemory.create(8, 1) otherwise return 1
	_ = try f.read(buf, 4) otherwise return 4
	f.close()
	return takes(buf)
end 'main'
```
```maxoncstderr
error E3117: <fragment>:12:12: 'read' stores RAW bytes into an element declared 'Byte' (int(0 to 100)), which does not hold every byte value 0 to 255 — widen the element's declared range to store raw bytes through it
```

<!-- test: a-raw-byte-write-answers-to-the-element-not-to-the-name-byte -->
### The rule reads the element's RANGE, never the name `Byte`
`isByteElementName`'s roster is a question about the NAME, and this rule is not: `Small` is byte-PACKED
(`rangedAliasStorageBytes` gives every non-negative range fitting `u8.max` a one-byte slot), so one raw
byte is one element here exactly as it is for a `Byte`, and 223 is exactly as far outside `int(0 to 100)`.
The `Byte` spelling of this same write was MEASURED at **223** before the rule existed
(`a-narrow-byte-refuses-a-raw-byte-write-through-the-buffer-surface`); this case differs from it only in
the element's name, which the rule never reads.
```maxon
typealias Small = int(0 to 100)
typealias Smalls = Array with Small

function takes(b Smalls) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	var a = Smalls.create()
	a.push(1)
	try a.managed.setByte(0, 223) otherwise return 3
	return takes(a)
end 'main'
```
```maxoncstderr
error E3118: <fragment>:12:16: 'setByte' writes a RAW BYTE at a byte OFFSET, so what the element's own accessors read back is any bit pattern of its 1-byte slot — every value of int(0 to 255). The element 'Small' (int(0 to 100)) does not admit all of them. Widen the element's declared range to cover its whole slot, or store through the `Array` surface's `set`, which range-checks each value against the element
```

### `append`'s argument is a coercion site, and a synthesized buffer crosses it like any other

A compiler-synthesized buffer wears `__ManagedByte` (`SynthesizedByteElementName`) and a declared
`Array with Byte` wears the program's own element. `append` asked a bare STRIDE test between them:
`__ManagedByte` strides one byte, so it walked into a `Byte` declared `int(0 to 100)` and a raw path
byte read back out of that element with no diagnostic.

⚠ **IT IS THE NINTH COERCION SITE BUT IT DOES *NOT* ASK `byteBufferBoundaryAdmits`, AND THE DIFFERENCE
IS THE WHOLE OF WHY THESE FOUR CASES ARE FOUR AND NOT TWO.** That door is a NOMINAL whole-container test
and is one-way by design — `__ManagedByte` may be ADOPTED where a byte-holding `Byte` is declared, never
the reverse — which is right at the eight sites that adopt a container and wrong here, where `append`
adopts nothing and merely copies values. Asking it made the receiver-side cases below stop compiling.
`append` asks the VALUE DOMAIN instead, in both directions, which it can because the compiler-owned
element has a value set of its own (`ProgramSignatures.elementIntegerValueRange`): a byte's.

<!-- test: a-narrow-byte-refuses-a-synthesized-buffer-appended-to-it -->
### A narrow `Byte` refuses a synthesized buffer appended into its array
⚠ **NO `targets:` MARKER**, for `a-narrow-byte-refuses-a-file-read-into-its-buffer`'s measured reason:
`__ManagedDirectory.currentPath` is x64-windows-only at this rung, but the refusal is a parse-time
`ParseError` and lands before the target-support pass runs, so this stderr is byte-identical on every
target.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	var b = Bytes.create()
	b.push(1)
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	b.append(cwd)
	return takes(b)
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:4: argument type mismatch for 'other': expected 'Bytes', got 'Array___ManagedByte'
```

<!-- test: a-byte-that-holds-every-byte-still-appends-a-synthesized-buffer -->
<!-- targets: x64-windows, arm64-macos -->
### A `Byte` that holds every byte still takes one
The other half of the boundary, and the case that keeps the door LIVE: refusing this would convert a
correct program into a compile error. x64-windows only for
`a-synthesized-buffer-crosses-every-declared-byte-buffer-door`'s reason — `currentPath` lowers to
`GetCurrentDirectoryA`, and the cwd differs per machine, so the assertion is on a LENGTH.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	b.push(65)
	let cwd = try __ManagedDirectory.currentPath() otherwise return 1
	b.append(cwd)
	return 0 if b.count() > 1 else 2
end 'main'
```
```exitcode
0
```

### And the MIRROR of it, which the first cut of this rule refused

The two cases above put the synthesized buffer on the ARGUMENT side. Putting it on the RECEIVER side is
the same operation with the same answer — a buffer the compiler minted is a run of raw bytes, and every
value of an element declared `int(0 to 100)` or `int(0 to u8.max)` is a byte — but the first cut answered
it through `byteBufferBoundaryAdmits`, which is a NOMINAL door and one-way on purpose, so it refused. Both
of these compiled at `59b56fda1` and stopped compiling; the cure was to give the compiler-owned element
the value set it has always had (`ProgramSignatures.elementIntegerValueRange`), which the domain test then
answers in both directions on its own.

<!-- test: a-synthesized-buffer-takes-a-byte-array-appended-to-it -->
<!-- targets: x64-windows, arm64-macos -->
### A synthesized buffer takes a declared byte array appended to it
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	b.push(65)
	var cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let before = cwd.length()
	cwd.append(b)
	return 0 if cwd.length() == before + 1 else 2
end 'main'
```
```exitcode
0
```

<!-- test: a-synthesized-buffer-takes-a-NARROWER-byte-array-appended-to-it -->
<!-- targets: x64-windows, arm64-macos -->
### …and a NARROWER one too, because every one of its values is a byte
The asymmetry that makes `error.append-narrower-element-array` a refusal runs the other way here: an
`int(0 to 100)` value stored into a raw byte buffer can never be read back out of range, because the
buffer's element admits every byte. Refusing this direction as well would be over-refusal.
```maxon
typealias Byte = int(0 to 100)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	b.push(65)
	var cwd = try __ManagedDirectory.currentPath() otherwise return 1
	let before = cwd.length()
	cwd.append(b)
	return 0 if cwd.length() == before + 1 else 2
end 'main'
```
```exitcode
0
```

### ⚠ THE `b"…"` LITERAL AND `__ManagedMemory.create(n, 1)` ARE ONE INCOHERENCE, AND ONLY ONE WAS REFUSED

The stride section at the top of this file refuses a `b"…"` literal in a program whose `Byte` does not
stride one byte, because the blob is byte-PACKED by construction and would give a record striding 1 under a
static type striding N. **`__ManagedMemory.create(count, elementSize: 1)` builds the identical record for
the identical reason** — the runtime stamps `element_size@24` from the ARGUMENT while the front end types
the result as this file's `Array with Byte` (`managedMemoryInstanceForElementSize`) — and it was accepted.

⛔ **MEASURED on the tree this rung starts from** (`typealias Byte = int(0 to 1000)`,
`__ManagedMemory.create(8, 1)`, `set(0, value: 300)`): the store type-checks against `int(0 to 1000)`, the
runtime strides the record's stamped 1, and `get(0)` reads back **44**. The same file's `b"CD"` is refused.
**One producer refused and its twin accepted is the defect**, and it is settled the way the literal already
was: a byte-PACKED record is a value of `Array with Byte` only where this file's `Byte` strides one byte.

⚠ **THE READER'S OWN `Byte` IS WHAT DECIDES IT (N2), WHICH IS WHY `stdlib/File.maxon` IS UNAFFECTED.** That
module writes `__ManagedMemory.create(size + extraBytes, 1)` and is parsed by every program;
`wide-byte-program-still-round-trips-a-file` below is what holds that shut.

<!-- test: error.a-byte-packed-buffer-is-refused-when-byte-is-wider-than-one-byte -->
### `__ManagedMemory.create(n, 1)` is refused where the `b"…"` literal already was
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	var buf = try __ManagedMemory.create(8, 1) otherwise return 1
	try buf.setLength(4) otherwise return 2
	try buf.set(0, value: 300) otherwise return 3
	return takes(buf)
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:38: Unsupported: `__ManagedMemory.create(count, elementSize: 1)` in a program whose `Byte` is a 2-byte range: the buffer it asks for is byte-PACKED, so its record would stride 1 while every `Array with Byte` built by `.create()` strides 2 — two values of one type that behave differently. Declare `Byte` as `int(0 to u8.max)` (or any range that fits one byte), or build the array with `.create()` + `push`
```

<!-- test: a-byte-packed-buffer-is-accepted-at-the-canonical-byte -->
### The canonical `Byte` keeps the two producers agreeing, so the buffer is built and read normally
The acceptance half, and it must pass both before and after the rule exists: the refusal is about the
DISAGREEMENT and not about `elementSize: 1`.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	var buf = try __ManagedMemory.create(8, 1) otherwise return 1
	try buf.setLength(4) otherwise return 2
	try buf.set(0, value: 200) otherwise return 3
	return 0 if takes(buf) == 200 else 1
end 'main'
```
```exitcode
0
```

<!-- test: a-word-buffer-is-untouched-by-a-wide-byte -->
### A MACHINE-WORD buffer in the same wide-`Byte` program is untouched
Only the byte arm can disagree: `create(n, 8)` takes the bare-`int` instance, whose stride is the machine
word by construction whatever `Byte` was declared to be. A rule that read the `elementSize` alone rather
than the instance it picks would refuse this program too.
```maxon
typealias Byte = int(0 to 1000)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var buf = try __ManagedMemory.create(2, 8) otherwise return 1
	try buf.setLength(1) otherwise return 2
	try buf.set(0, value: 300) otherwise return 3
	var made = Bytes.create()
	made.push(300)
	return 0 if (try buf.get(0) otherwise 0) == 300 and (try made.get(0) otherwise 0) == 300 else 4
end 'main'
```
```exitcode
0
```

### ⚠ THE RAW-BYTE WRITER ANSWERS TO ITS **SLOT**, AND A BYTE IS ONLY THE ONE-BYTE CASE OF THAT

`setByte` addresses a byte OFFSET, so on a record that strides more than a byte it writes a FRAGMENT of an
element — and what the ARRAY surface reads back afterwards is an arbitrary bit pattern of that element's
whole SLOT. A4a gave the writer a stride PRECONDITION so its every-byte rule (E3117) fired only where one
byte is one element; above that slot the surface was assumed to be byte-level staging, which is true of an
`Array with Int` and false of everything whose element does not fill its slot.

⛔ **FOUR SILENT WRONG ANSWERS MEASURED on the tree this rung starts from**, each a value read back through
an accessor whose declared type cannot hold it:

| program | reads back |
|---|---|
| `typealias Wide = int(0 to 1000)`, `a.managed.setByte(1, 223)` | **57089** |
| `typealias Small = int(-5 to 5)`, `a.managed.setByte(0, 223)` | **223** |
| `Array with bool`, `setByte(0, 223)` | `v` **and** `not v` both true, `v == true` false |
| `Array with <an enum>`, `setByte(0, 223)` | the program **HANGS** — the ordinal matches no case |

⇒ the writer asks ONE question at every stride — *does the element admit every bit pattern of its slot?* —
under its own code (**E3118**), and A4a's stride precondition is gone because the general rule subsumes it.
**E3117 is NOT widened**: it keeps the two fills that are byte-per-element by contract (the byte view and
`__ManagedFile.read`) and loses only this site, where "every byte" was the one-byte instance of a question
the construct asks at every width. Asking "every byte" at a wide slot is what would refuse a correct
`Array with Int`, which A4a measured once already.

⚠ **AN `ExitCode` ELEMENT IS REFUSED, AND THE ROW FILED IT AS A 4-BYTE SLOT — MEASURED, IT IS EIGHT.**
`ExitCode` reaches an `Array` instance as a NAMED leaf, so `trivialElementStorageBytes` misses the ranged
registry and `undeclaredNamedElementStorageBytes` hands it the machine WORD; the measured `3741319169` is
`223` at byte 3 of an EIGHT-byte slot, not of a four-byte one. Its domain is at widest `0 to u32.max`
(and is PLATFORM-DEPENDENT below that — `0 to 255` on Linux, macOS and WASI), so it cannot hold every
value of the slot it actually occupies whichever reading you take, and shv2's own value-set door answers
`notIntegerValued` for it besides. A door that ADMITS raw bytes into a declared element settles an
ambiguity by refusing — `rangeHoldsEveryByte`'s argument, one quantifier out.

<!-- test: error.a-raw-byte-write-is-refused-at-a-slot-the-element-does-not-fill -->
### A ranged element WIDER than a byte that does not fill its slot
`rangedAliasStorageBytes` gives `int(0 to 1000)` a TWO-byte slot, so a raw byte at offset 1 is the slot's
HIGH byte and the element reads back 57089 — outside its own declared range, with no diagnostic before.
```maxon
typealias Wide = int(0 to 1000)
typealias Wides = Array with Wide

function takes(b Wides) returns ExitCode
	return ((try b.get(0) otherwise 0) and u8.max) as ExitCode
end 'takes'

function main() returns ExitCode
	var a = Wides.create()
	a.push(1)
	try a.managed.setByte(1, 223) otherwise return 3
	return takes(a)
end 'main'
```
```maxoncstderr
error E3118: <fragment>:12:16: 'setByte' writes a RAW BYTE at a byte OFFSET, so what the element's own accessors read back is any bit pattern of its 2-byte slot — every value of int(0 to 65535). The element 'Wide' (int(0 to 1000)) does not admit all of them. Widen the element's declared range to cover its whole slot, or store through the `Array` surface's `set`, which range-checks each value against the element
```

<!-- test: error.a-raw-byte-write-is-refused-on-a-negative-lower-bound-element -->
### An element with a NEGATIVE lower bound keeps the machine word and does not fill it
`rangedAliasStorageBytes` is the UNSIGNED ladder, so `int(-5 to 5)` takes the whole word — and one raw byte
put **223** into an element declared `int(-5 to 5)`.
```maxon
typealias Small = int(-5 to 5)
typealias Smalls = Array with Small

function takes(b Smalls) returns ExitCode
	return (try b.get(0) otherwise 0) as ExitCode
end 'takes'

function main() returns ExitCode
	var a = Smalls.create()
	a.push(1)
	try a.managed.setByte(0, 223) otherwise return 3
	return takes(a)
end 'main'
```
```maxoncstderr
error E3118: <fragment>:12:16: 'setByte' writes a RAW BYTE at a byte OFFSET, so what the element's own accessors read back is any bit pattern of its 8-byte slot — every value of int(0 to 18446744073709551615). The element 'Small' (int(-5 to 5)) does not admit all of them. Widen the element's declared range to cover its whole slot, or store through the `Array` surface's `set`, which range-checks each value against the element
```

<!-- test: error.a-raw-byte-write-is-refused-on-a-bool-element -->
### A `bool` element is a ONE-byte slot holding exactly two values
The slot is one byte and the element admits `0` and `1`, so this is the case that separates *"admits every
value of its slot"* from *"strides one byte"*. Measured before the rule: `v` and `not v` were BOTH true
while `v == true` was false, which is not a boolean.
```maxon
typealias Flags = Array with bool

function main() returns ExitCode
	var a = Flags.create()
	a.push(false)
	try a.managed.setByte(0, 223) otherwise return 3
	return 0 if (try a.get(0) otherwise false) else 1
end 'main'
```
```maxoncstderr
error E3118: <fragment>:7:16: 'setByte' writes a RAW BYTE at a byte OFFSET, and the element 'bool' is SUB-BYTE PACKED at 1 bit(s) — it has no byte of its own, so one raw byte spans this element and the several after it. Store through the `Array` surface's `set`, which writes one element's bits and range-checks the value against it
```

<!-- test: error.a-raw-byte-write-is-refused-on-an-enum-element -->
### An ENUM element stores an ORDINAL, and a raw byte is not one
An enum's value set is the ordinals its declaration lists, which is never every pattern of the slot holding
them. Measured before the rule: the program compiled and then **hung**, matching an ordinal no arm names.

⚠ **THE SLOT IN THE MESSAGE IS DERIVED, AND IT MOVED WITH `enum-narrow-storage`.** This case pinned an
`8-byte slot` until a payload-free enum's array element was narrowed to the width its raw values need
(`u8` for `red`/`green`), so the sentence now says `1-byte slot` — the SAME verdict, code, line and column,
over two numbers the diagnostic reads off the element's storage. ⚠ Its own prose used to cite
`array-enum-element-size.md` as pinning "an enum element at the machine word", and that was a misreading
worth removing: that spec's `element_size = 8` is for a union WITH ASSOCIATED VALUES, whose element is a
heap POINTER — a different door, and one this narrowing deliberately leaves alone.
```maxon
enum Color
	red
	green
end 'Color'

typealias Colors = Array with Color

function main() returns ExitCode
	var a = Colors.create()
	a.push(Color.red)
	try a.managed.setByte(0, 223) otherwise return 3
	return match (try a.get(0) otherwise Color.red) 'v'
		red gives 0
		green gives 1
	end 'v'
end 'main'
```
```maxoncstderr
error E3118: <fragment>:12:16: 'setByte' writes a RAW BYTE at a byte OFFSET, so what the element's own accessors read back is any bit pattern of its 1-byte slot — every value of int(0 to 255). The element 'Color' does not admit all of them. Widen the element's declared range to cover its whole slot, or store through the `Array` surface's `set`, which range-checks each value against the element
```

<!-- test: an-exit-code-element-fills-its-own-slot-and-takes-a-raw-byte-write -->
<!-- targets: x64-windows -->
### An `ExitCode` element FILLS its slot, so a raw byte write is legal — and this case USED TO PIN THE OPPOSITE
⛔⛔ **THIS CASE WAS `error.a-raw-byte-write-is-refused-on-an-exit-code-element`, AND THE REFUSAL IT PINNED
WAS A FALSE ONE shv2 SHIPPED — MEASURED AGAINST THE RUNNABLE ORACLE AT W5.** The bootstrap compiles this
exact program and runs it to **3741319169**; shv2 refused it with `E3118`. The disagreement was not about
the RULE — a raw byte write is legal exactly when the element admits every bit pattern of its own slot —
it was that shv2 held **two answers about `ExitCode` and used a different one for each half of the rule**:
it knew the RANGE (`int(0 to u32.max)` here, which is why the old refusal could print it) while giving the
element the machine-word SLOT of a name no file declares. An element compared against 8 bytes it does not
occupy fails a test it should never have been given.

⭐ **W5 removed the second answer rather than the check.** `stdlib/Process.maxon` is now loaded like every other stdlib module,
so its `export typealias ExitCode` is an ORDINARY declaration in the alias registry and the slot and the
range come from the one place — the same convergence W3 performed for `Ordering` and `IterationError`. The
element is `int(0 to u32.max)`, its slot is therefore 4 bytes, it admits every one of them, and the write
is accepted. **Both compilers now answer 3741319169** (`0xDF000001` — the pushed `1` with byte 3 set to
223, which is what a 4-byte element makes of it).

⚠ It is restricted to **x64-windows** by the marker line above, because `ExitCode`'s range IS the compile
target's (`int(0 to 255)` on Linux, macOS and WASI), so the element's WIDTH — and hence which element byte 3
belongs to, and whether `3741319169` is even in range for the value `main` returns — differs per lane. The
verdict does not: a narrower range fills a narrower slot just as exactly. The three siblings above already
pin the rule at 1, 2 and 8 bytes on every target; what this case adds is the compiler-owned name, and that
is the part that is platform-shaped.
⛔ **THE MARKER IS A LINE, NOT A SENTENCE, AND FOR ONE REVISION OF THIS CASE IT WAS A SENTENCE (W5 review).**
`SpecParser.scanTestFromMarker` reads the directive with `line.contains`, so the prose that *quoted* the
marker WAS the marker — the case really was x64-windows-only, by accident, and rewording this paragraph
would have silently released it onto the arm64 and wasm lanes where its element is one byte wide. Quote the
marker's NAME here, never its text.
```maxon
typealias Codes = Array with ExitCode

function main() returns ExitCode
	var a = Codes.create()
	a.push(1)
	try a.managed.setByte(3, 223) otherwise return 3
	return try a.get(0) otherwise 4
end 'main'
```
```exitcode
3741319169
```

<!-- test: a-raw-byte-write-is-accepted-when-the-element-fills-its-two-byte-slot -->
### An element that DOES fill its two-byte slot keeps its raw writer
`int(0 to u16.max)` admits every value of the two-byte slot it was given, so a raw byte at either offset is
honest. This program answers **57089** before and after.

⚠ **IT IS NOT THE CONTROL AGAINST A WIDENED E3117 EITHER, AND ITS FIRST DRAFT CLAIMED TO BE (A4c review).**
This element DOES hold every byte — `rangeHoldsEveryByte(0, 65535)` is true — so a widened E3117 would
accept it too. The control is `a-raw-byte-write-still-stages-a-machine-word-element` below. What this case
pins is the OFFSET-1 half: a raw byte written at the slot's HIGH byte is honest here and is the very write
`error.a-raw-byte-write-is-refused-at-a-slot-the-element-does-not-fill` refuses one range narrower.
```maxon
typealias Byte = int(0 to u16.max)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var a = Bytes.create()
	a.push(1)
	try a.managed.setByte(1, 223) otherwise return 3
	return 0 if (try a.get(0) otherwise 0) == 57089 else 4
end 'main'
```
```exitcode
0
```

<!-- test: a-raw-byte-write-still-stages-a-machine-word-element -->
### A machine-word `int` element admits every bit pattern of its slot, so staging still works
The case A4a's first cut broke and had to be pulled back from. It is the reason the rule may not be *"does
the element hold every BYTE"*: `int(i64.min to i64.max)` does not, and it holds every value of its slot.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var xs = IntArray.create()
	xs.push(7)
	try xs.managed.setByte(0, 3) otherwise return 1
	return 0 if (try xs.get(0) otherwise 0) == 3 else 2
end 'main'
```
```exitcode
0
```

<!-- test: a-raw-byte-write-into-a-byte-no-file-declares-is-still-accepted -->
### A program that declares no `Byte` at all keeps its raw writer
The literal mints its own `Array with Byte` (`undeclaredNamedElementStorageBytes`'s byte arm), which strides
one byte and has no declared range for a raw byte to fall outside of — exactly the case E3117 returns on.
```maxon
function main() returns ExitCode
	var v = b"abc"
	try v.managed.setByte(0, 223) otherwise return 3
	return 0 if (try v.managed.byteAt(0) otherwise 0) == 223 else 4
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-packed-buffer-under-the-canonical-byte-still-takes-a-raw-byte-write -->
### The two rungs COMPOSED: a byte-packed `create` buffer under the canonical `Byte`, written raw
⭐ **THE ONE CASE THAT CROSSES BOTH RULES, added by A4c's review because the rewrite above removed the
only case that did.** `a-wide-byte-still-writes-raw-bytes-through-the-buffer-surface` used to build its
receiver with `__ManagedMemory.create(8, 1)` and then `setByte` it; rewriting it onto `Bytes.create()` +
`push` was right for its own purpose and left NOTHING exercising the composition. Here the stride rule
accepts the producer (this file's `Byte` strides one byte) and the slot rule then accepts the write (a
one-byte slot whose element admits every byte), which is the whole of what a byte buffer is for — and a
regression in either rung alone reddens it.
```maxon
typealias Byte = int(0 to u8.max)
typealias Bytes = Array with Byte

function takes(b Bytes) returns ExitCode
	return (try b.get(0) otherwise 0)
end 'takes'

function main() returns ExitCode
	var buf = try __ManagedMemory.create(8, 1) otherwise return 1
	try buf.setLength(4) otherwise return 2
	try buf.setByte(0, 223) otherwise return 3
	return 0 if takes(buf) == 223 else 4
end 'main'
```
```exitcode
0
```
