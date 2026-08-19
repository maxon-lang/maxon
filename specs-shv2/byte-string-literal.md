---
feature: byte-string-literal
status: stable
keywords: [byte, string, literal, bytebuffer]
category: types
---

# Byte String Literal

## Documentation

A byte string literal uses the `b"..."` prefix to create a `ByteArray` (`Array with Byte`) directly from a string, without allocating a `String`. This is useful when working with raw bytes or APIs that expect byte arrays.

### Syntax

```text
let bytes = b"hello"           // ByteArray containing [104, 101, 108, 108, 111]
let empty = b""                // Empty ByteArray
let escaped = b"line\n"        // Supports escape sequences
```

The byte string literal supports the same escape sequences as regular string literals (`\n`, `\t`, `\\`, `\0`, etc.), including the `\xNN` hex escape (see the `hex-escape` spec) and the `\uNNNN` unicode escape.

### Non-ASCII characters and byte values

Unlike a regular string literal — where a source character is stored as its multi-byte UTF-8 encoding — a byte string literal is a raw byte sequence. A literal character (or a `\uNNNN` escape) whose codepoint is in the range `0x00`–`0xFF` (Latin-1) contributes exactly **one** byte equal to that codepoint. So `b"À"` (U+00C0) and `b"\xc0"` produce the identical single byte `0xC0` (192), and `b"é"` (U+00E9) produces the single byte `0xE9` (233).

```text
let a = b"À"        // one byte: [192]
let b = b"\xc0"     // one byte: [192] — identical to b"À"
```

A codepoint above `0xFF` cannot be represented as a single byte, so a literal character or `\uNNNN` escape that names one is rejected at compile time with **E1004**. Use a `\xNN` hex escape to embed a specific raw byte regardless of its Unicode interpretation.

### Use Cases

Byte string literals are particularly useful as map keys when the map uses `ByteArray` keys, avoiding the overhead of `String` construction and `toByteArray()` conversion:

```text
typealias KeywordMap = Map with (ByteArray, int)
let keywords = [b"if": 1, b"else": 2, b"while": 3]
```

### Methods

Byte string literals produce a standard `ByteArray`, so all `Array` methods are available:

```text
let data = b"hello"
data.count()       // 5
data.get(0)        // 104 (ASCII 'h')
```

### Identical to `String.toByteArray()`

A byte string literal has *exactly* the type `String.toByteArray()` returns — `ByteArray`, whose
element is the ranged `Byte` (`int(0 to u8.max)`). The two are therefore interchangeable in every
position, including when `let`-bound, and elements read out of one may be ordered-compared against
elements read out of the other.

### At module scope

A byte string literal is a compile-time constant, so it may initialize a module-scope `let` or
`var`. The array is materialized ONCE at startup and every reference loads it — so a byte string
constant on a hot path costs no allocation per use, unlike a literal written inline.

```text
let Keyword = b"critsplit"     // one ByteArray for the whole program
var Buffer = b""
```

A module-scope initializer must be constant in its entirety. A method call is not, so
`let X = "lit".toByteArray()` is rejected (**E2045**) rather than silently folded to a `String` —
write `let X = b"lit"` instead.

## Tests

<!-- test: byte-string-literal.basic -->

```maxon
function main() returns ExitCode
		let bytes = b"hello"
		return bytes.count()
end 'main'
```
```exitcode
5
```

<!-- test: byte-string-literal.empty -->

```maxon
function main() returns ExitCode
		let bytes = b""
		return bytes.count()
end 'main'
```
```exitcode
0
```

<!-- test: byte-string-literal.escape-sequences -->

```maxon
function main() returns ExitCode
		let bytes = b"a\nb"
		return bytes.count()
end 'main'
```
```exitcode
3
```

<!-- test: byte-string-literal.content -->

```maxon
function main() returns ExitCode
		let bytes = b"AB"
		let a = try bytes.get(0) otherwise 0
		let b = try bytes.get(1) otherwise 0
		print("{a} {b}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
65 66
```

<!-- test: byte-string-literal.map-key -->


```maxon
function main() returns ExitCode
		let m = [b"hello": 1, b"world": 2]
		let v1 = try m.get(b"hello") otherwise 0
		let v2 = try m.get(b"world") otherwise 0
		print("{v1} {v2}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- disabled-test: byte-string-literal.top-level-map -->
<!-- P2 Map: byte-array-keyed Map is not yet built -->


```maxon
var m = [b"hello": 1, b"world": 2]

function main() returns ExitCode
		let v1 = try m.get(b"hello") otherwise 0
		let v2 = try m.get(b"world") otherwise 0
		print("{v1} {v2}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 2
```

<!-- disabled-test: byte-string-literal.top-level-map-struct -->
<!-- P2 Map: byte-array-keyed Map is not yet built -->


```maxon
typealias Integer = int(i64.min to i64.max)

type Info
		export var value as Integer

		static function create(value Integer) returns Self
			return Self{value: value}
		end 'create'
end 'Info'

var m = [b"hello": Info.create(10), b"world": Info.create(20)]

function main() returns ExitCode
		let v1 = try m.get(b"hello") otherwise Info.create(0)
		let v2 = try m.get(b"world") otherwise Info.create(0)
		print("{v1.value} {v2.value}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
10 20
```

<!-- test: byte-string-literal.top-level-let -->

A byte string literal initializes a module-scope `let`, and the global is a `ByteArray`.
```maxon
let Keyword = b"critsplit"

function takes(b ByteArray) returns ExitCode
		return 0 if b.count() == 9 else 1
end 'takes'

function main() returns ExitCode
		return takes(Keyword)
end 'main'
```
```exitcode
0
```

<!-- test: byte-string-literal.top-level-var -->

A module-scope `var` holds a byte string literal and can be reassigned to another one.
```maxon
var Buffer = b""

function main() returns ExitCode
		if Buffer.count() != 0 'notEmpty'
				return 1
		end 'notEmpty'
		Buffer = b"filled"
		return Buffer.count()
end 'main'
```
```exitcode
6
```

<!-- test: byte-string-literal.tobytearray-ordered-compare -->

A `let`-bound byte string literal has the same element type as `String.toByteArray()`, so bytes
read out of the two can be ordered-compared.
```maxon
function main() returns ExitCode
		let fromString = "9223372036854775807".toByteArray()
		let fromLiteral = b"9223372036854775807"
		let a = try fromString.get(0) otherwise panic("oob")
		let b = try fromLiteral.get(0) otherwise panic("oob")
		if a > b or a < b 'differ'
				return 1
		end 'differ'
		return 0
end 'main'
```
```exitcode
0
```

<!-- test: byte-string-literal.dead-global-not-leaked -->

A byte string global whose only reader is eliminated as dead code must not be allocated and
then left unreleased — dead-code elimination drops the literal along with the global it fed.
```maxon
typealias Integer = int(i64.min to i64.max)

let Live = b"aa"
let Dead = b"bbbb"

function readDead() returns Integer
		return Dead.count()
end 'readDead'

function main() returns ExitCode
		return Live.count()
end 'main'
```
```exitcode
2
```

<!-- test: byte-string-literal.tobytearray-global-error -->

A method call is not a constant expression, so it cannot initialize a global — it is rejected
rather than silently folded back to a `String`.
```maxon
let B = "critsplit".toByteArray()

function main() returns ExitCode
		return B.count()
end 'main'
```
```maxoncstderr
error E2045: specs/fragments/byte-string-literal/byte-string-literal.tobytearray-global-error.test:2:20: Global initializer for 'B' is not a constant expression: '.toByteArray()' cannot be evaluated at compile time
```

<!-- test: byte-string-literal.field-access -->

```maxon
function main() returns ExitCode
		let len = b"test".count()
		return len
end 'main'
```
```exitcode
4
```

<!-- test: byte-string-literal.try-literal-accessor -->

A throwing `.get()` applied directly to a byte-string literal, with no intermediate binding — the
`try` target parse must accept the byte-string literal exactly as it accepts an array literal, and the
literal's owned temp must drop on both the in-bounds and the caught out-of-bounds path.
```maxon
function main() returns ExitCode
		let ok = try b"AB".get(1) otherwise 0
		let oob = try b"AB".get(9) otherwise 200
		print("{ok} {oob}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
66 200
```

<!-- test: byte-string-literal.push-grows-off-rdata -->

Pushing onto a byte-string literal grows it off its immortal `.rdata` buffer: the original bytes are
copied into a fresh heap buffer and the rdata blob is left unfreed — `__managed_grow` must honor the same
`capacity < 0` rdata sentinel the drop path does, or it `__mm_free`s a never-allocated `.rdata` address
and the run reports a leak. The grown array then drops leak-free.
```maxon
function main() returns ExitCode
		var a = b"hi"
		a.push(88)
		let g = try a.get(2) otherwise 0
		print("{a.count()} {g}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
3 88
```

<!-- test: byte-string-literal.latin1-char -->

```maxon
function main() returns ExitCode
		let bytes = b"À"
		let b0 = try bytes.get(0) otherwise 0
		print("{bytes.count()} {b0}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 192
```

<!-- test: byte-string-literal.latin1-two -->

```maxon
function main() returns ExitCode
		let bytes = b"Àé"
		let b0 = try bytes.get(0) otherwise 0
		let b1 = try bytes.get(1) otherwise 0
		print("{bytes.count()} {b0} {b1}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
2 192 233
```

<!-- test: byte-string-literal.latin1-hex-equivalent -->

```maxon
function main() returns ExitCode
		let literal = b"À"
		let hex = b"\xc0"
		let a = try literal.get(0) otherwise 0
		let b = try hex.get(0) otherwise 1
		if a == b 'equal'
				print("{a}")
				return 0
		end 'equal'
		return 1
end 'main'
```
```exitcode
0
```
```stdout
192
```

<!-- test: byte-string-literal.latin1-mixed -->

```maxon
function main() returns ExitCode
		let bytes = b"AÀB"
		let b0 = try bytes.get(0) otherwise 0
		let b1 = try bytes.get(1) otherwise 0
		let b2 = try bytes.get(2) otherwise 0
		print("{bytes.count()} {b0} {b1} {b2}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
3 65 192 66
```

<!-- test: byte-string-literal.set-detaches-from-rdata -->

An in-place `set` on a byte-string literal writes through `buffer@0`, which points into read-only
`.rdata` — so it must first DETACH: give the record a private, writable copy of its live bytes. Without
the detach the store faults (`0xC0000005`, no stderr), because `.rdata` is mapped read-only. Detaching
copies only `length · element_size` live bytes, so the other elements survive the move.
```maxon
function main() returns ExitCode
		var a = b"hi"
		try a.set(0, value: 88) otherwise panic("test invariant: index 0 is in bounds")
		let v = try a.get(0) otherwise 0
		let w = try a.get(1) otherwise 0
		print("{v} {w} {a.count()}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
88 105 2
```

<!-- test: byte-string-literal.clear-detaches-from-rdata -->

`clear` zeroes the whole live region in place, so on a byte-string literal it writes into `.rdata` and
faults unless the record detaches first. The cleared array must remain a usable, writable array — the
push below lands in the detached buffer, not back in the blob.
```maxon
function main() returns ExitCode
		var a = b"hey"
		a.clear()
		a.push(65)
		let z = try a.get(0) otherwise 0
		print("{a.count()} {z}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
1 65
```

<!-- test: byte-string-literal.remove-detaches-from-rdata -->

`remove` shifts the tail down one slot — an in-place write — so it detaches before the shift. The
removed element is read out of the buffer and the survivors are compacted in the private copy.
```maxon
function main() returns ExitCode
		var a = b"hey"
		let r = try a.remove(0) otherwise panic("test invariant: index 0 is in bounds")
		let f = try a.get(0) otherwise 0
		print("{r} {a.count()} {f}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
104 2 101
```

<!-- test: byte-string-literal.pop-detaches-from-rdata -->

`pop` zeroes the slot it vacates (the `[length, capacity)`-is-zero invariant), so it too writes the
buffer and must detach first.
```maxon
function main() returns ExitCode
		var a = b"yo"
		let p = try a.pop() otherwise panic("test invariant: a two-element array is not empty")
		let f = try a.get(0) otherwise 0
		print("{p} {a.count()} {f}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
111 1 121
```

<!-- test: byte-string-literal.detach-happens-once -->

The detach is CONDITIONAL: once the record owns a private buffer it must be left alone. A second write
that detached again would allocate a fresh buffer and abandon the first one — the leak check would exit
101 — and the grow below would then be reallocating a buffer that had just been replaced. Both writes
must land in the same private buffer, and the grow must carry all three earlier bytes forward.
```maxon
function main() returns ExitCode
		var a = b"abc"
		try a.set(0, value: 88) otherwise panic("test invariant: index 0 is in bounds")
		try a.set(2, value: 90) otherwise panic("test invariant: index 2 is in bounds")
		a.push(89)
		let v0 = try a.get(0) otherwise 0
		let v1 = try a.get(1) otherwise 0
		let v2 = try a.get(2) otherwise 0
		let v3 = try a.get(3) otherwise 0
		print("{v0} {v1} {v2} {v3} {a.count()}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
88 98 90 89 4
```

<!-- test: byte-string-literal.detached-literal-drops-clean -->

A detached literal's `capacity@16` is no longer the rdata sentinel, so its drop now legitimately frees
the buffer — the one it allocated, never the blob. Five detach-and-drop rounds make either mistake loud:
a missed free leaks (exit 101) and a freed blob corrupts the allocator.

⚠ **ITS GOLDEN MOVED WHEN `stdlib/File.maxon` WAS WHITELISTED (R4.7), IN A PROGRAM THAT NEVER MENTIONS
`File` — AND THE MOVE IS A MISSING CHECK NOW EMITTED, NOT A CODEGEN CHANGE.** MEASURED by an A/B with one
variable, `detachAndRead` textually identical in both: with `Byte` UNDECLARED the slot before `__managed_set`
holds `movRegReg rcx, r12`; with `typealias Byte = int(0 to u8.max)` declared it holds
`cmpRegImm32 rbx, 0` / `cmpRegImm32 rbx, 255` / `mrt_panic`. Whitelisting `File.maxon` supplies
`export typealias Byte = int(0 to u8.max)` (`:45`), so a `b"…"` literal's element has a DECLARED RANGE for
the first time and `a.set(0, value: n)` — an `int` into a `Byte` slot — gets the narrowing guard it was
silently missing. The golden diff is a PURE INSERTION: nothing is removed, nothing reordered, and the
original `movRegReg rcx, r12` / `__managed_set` sequence survives verbatim under the new `__rc_ok` label.
```maxon
function detachAndRead(n int) returns int
		var a = b"hi"
		try a.set(0, value: n) otherwise panic("test invariant: index 0 is in bounds")
		return try a.get(0) otherwise 0
end 'detachAndRead'

function main() returns ExitCode
		var total = 0
		var i = 0
		while i < 5 'round'
				total = total + detachAndRead(i)
				i = i + 1
		end 'round'
		print("{total}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
10
```

<!-- test: byte-string-literal.untouched-literal-drops-clean -->

The other half of the same rule: a literal that is only READ never detaches, so its `capacity@16` stays
the sentinel and its drop must still skip the buffer free. `__mm_free` on a `.rdata` address that was
never allocated corrupts the allocator; three rounds make it loud.
```maxon
function main() returns ExitCode
		var total = 0
		var i = 0
		while i < 3 'round'
				var a = b"hi"
				let v = try a.get(0) otherwise 0
				total = total + v
				i = i + 1
		end 'round'
		print("{total}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
312
```

<!-- test: byte-string-literal.empty-literal-detaches -->

A ZERO-LENGTH detach must still happen. Skipping it as an optimization leaves the rdata pointer in the
record with the sentinel capacity still in place, and the next reallocation then reads a header that
does not exist. `b""` has no writable in-place mutator (`set`/`remove`/`pop` all throw on empty), so the
zero-byte detach is reached through the grow path: `resize` publishes two zero-filled slots that the
`set`s below then write.
```maxon
function main() returns ExitCode
		var a = b""
		a.resize(2)
		try a.set(0, value: 65) otherwise panic("test invariant: index 0 is in bounds")
		try a.set(1, value: 66) otherwise panic("test invariant: index 1 is in bounds")
		let v0 = try a.get(0) otherwise 0
		let v1 = try a.get(1) otherwise 0
		print("{a.count()} {v0} {v1}")
		return 0
end 'main'
```
```exitcode
0
```
```stdout
2 65 66
```

<!-- test: byte-string-literal.negative-resize-aborts -->

A NEGATIVE `resize` is the one mutator that could write outside its buffer entirely, and the one the detach
cannot rescue — detaching moves the write to a different allocation, it does not bring it back inside one.
Without this guard, `resize(-2)` on a byte-string literal DOES detach (the detach at the head of the grow is
unconditional), the growth check is then satisfied (`3 >= -2`) so nothing reallocates, and the shrink zeroes
from `buffer + n·element_size` — two bytes BELOW the freshly allocated private buffer — over the allocation
header. Measured with the guard stubbed out: the process exits 0, the leak gate stays green, `capacity()`
reads 3 and `count()` publishes -2. It aborts instead: a length can never be negative, and a corrupt
operation must never proceed (the `__managed_create` zero-element-size and `__managed_append` element-size-mismatch
shape).
```maxon
function main() returns ExitCode
		var a = b"hey"
		a.resize(-2)
		return 0
end 'main'
```
```exitcode
73
```
