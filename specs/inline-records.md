---
feature: inline-records
status: experimental
keywords: [type, inline, packed, word, bits, ranged, sizeof, memory]
category: memory
---

## Documentation

# Inline packed records

A `type` whose whole shape fits one machine word is not a heap record: its value IS the word. The
qualification is inferred, with no new syntax and no annotation — a type qualifies when **every** field is
`let`, **every** field's declared type is an unsigned zero-based ranged alias (`int(0 to N)`), a `bits(n)`
alias or `bool`, the type takes no type parameters and carries no `where` clause, it has at least one
field, and the fields' storage widths sum to **64 bits or fewer**. A field's storage width is the array
element ladder the language already uses: `int(0 to 1)` and `bool` take 1 bit, `int(0 to 3)` 2,
`int(0 to 15)` 4, `int(0 to u8.max)` 8, `int(0 to u16.max)` 16, `int(0 to u32.max)` 32, anything wider 64,
and `bits(n)` takes its own `n`.

A field's alias is resolved **as the file declaring the `type` sees the name**, exactly as that field's
range check resolves it. A name two files declare differently is therefore two aliases, and whether a
type qualifies is decided by the declaration in scope where the type is written.

The word is laid out **first field in the low bits, each following field immediately above it**. The first
field occupies bits `0 .. w0-1`, the second `w0 .. w0+w1-1`, and so on. A `Self{…}` assembles the word by
shifting each field into its place — each field range-checked at its door exactly as a heap record's is —
and a field read extracts it with a shift and a mask.

Because the value is a word and not a box:

- `sizeof(T)` is **8**, whatever the fields sum to.
- A local, a parameter and a return are the word itself. A field of a heap record is one 8-byte slot, and
  `Array with T` is a dense 8-byte trivial element.
- Nothing is allocated, retained, released, cloned or destroyed: there is no `__clone_` and no
  `__destruct_`, and a module-level `let` of one is image data with nothing to run before `main`.
- `clone()` is the **identity** — the copy is the same word.
- `is` and `is not` are **refused with E3068**, the diagnostic every primitive already gets: there is no
  record for two names to share.
- `==` keeps the ordinary record rule — it calls the type's own `equals`, and a type without one is E3005.
  `Hashable` and `Equatable` conformance is unchanged, so an inline record is a `Map` key like any other.

**Enum fields do not qualify this stage.** A type with an enum field stays a heap record, as does every
type that fails any part of the rule above — a `var` field, a signed or non-zero-based range, a type
parameter, or fields summing past 64 bits. Nothing about those types changes.

## Tests

<!-- test: inline-records.two-u32-fields-are-one-word -->
RED: two 32-bit fields sum to exactly 64 bits, so `Pair` is one word and `sizeof` is 8; today it is a
two-slot heap record and the program exits 16.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function main() returns ExitCode
	return sizeof(Pair)
end 'main'
```
```exitcode
8
```

<!-- test: inline-records.no-allocation-for-an-inline-record -->
RED: a hundred inline records are a hundred words, so the live allocation count does not move; today each
`create` boxes and the program exits 100. The holder is reserved before the first reading so no buffer
growth lands between the two.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

typealias PairArray = Array with Pair

function main() returns ExitCode
	var holder = PairArray.create()
	holder.reserve(100)

	let before = __Builtins.mmAllocLive()
	for i in 0 upto 100 'build'
		holder.push(Pair.create(i, hi: i * 2))
	end 'build'
	let after = __Builtins.mmAllocLive()

	var sum = 0
	for p in holder 'read'
		sum = sum + p.lo
	end 'read'

	if sum != 4950 'values'
		return 1
	end 'values'

	return (after - before) as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.mixed-widths-round-trip -->
RED: five fields of five different widths sum to 31 bits and pack into one word, so the exit code is 8;
today the values read back correctly out of a five-slot heap record and the exit code is that record's
size instead.
```maxon
typealias Quad = int(0 to 3)
typealias Nib = int(0 to 15)
typealias Octet = int(0 to u8.max)
typealias Wide = int(0 to u16.max)

type Mixed
	export let flag as bool
	export let q as Quad
	export let nib as Nib
	export let b as Octet
	export let w as Wide

	export static function create(flag bool, q Quad, nib Nib, b Octet, w Wide) returns Self
		return Self{flag: flag, q: q, nib: nib, b: b, w: w}
	end 'create'
end 'Mixed'

function main() returns ExitCode
	let top = Mixed.create(true, q: 3, nib: 15, b: 255, w: 65535)
	let bottom = Mixed.create(false, q: 0, nib: 0, b: 0, w: 0)

	print("{top.flag} {top.q} {top.nib} {top.b} {top.w}\n")
	print("{bottom.flag} {bottom.q} {bottom.nib} {bottom.b} {bottom.w}\n")

	return sizeof(Mixed)
end 'main'
```
```exitcode
8
```
```stdout
true 3 15 255 65535
false 0 0 0 0
```

<!-- test: inline-records.bits-alias-field -->
RED: four `bits(n)` fields sum to 56 bits, so `Packed` is one word and the exit code is 8; today it is a
four-slot heap record whose size is what the program exits with instead. The two wide fields carry every bit set, so a field read
that sign-extended or masked short would print the wrong number.
```maxon
typealias Nib4 = bits(4)
typealias Word32 = bits(32)
typealias Half16 = bits(16)

type Packed
	export let a as Nib4
	export let b as Nib4
	export let w as Word32
	export let h as Half16

	export static function create(a Nib4, b Nib4, w Word32, h Half16) returns Self
		return Self{a: a, b: b, w: w, h: h}
	end 'create'
end 'Packed'

function main() returns ExitCode
	let p = Packed.create(15 as Nib4, b: 9 as Nib4, w: 0xFFFFFFFF as Word32, h: 0xFFFF as Half16)

	print("{p.a} {p.b} {p.w} {p.h}\n")

	return sizeof(Packed)
end 'main'
```
```exitcode
8
```
```stdout
15 9 4294967295 65535
```

<!-- test: inline-records.array-of-inline-records-is-eight-byte-strided -->
RED: an inline record is a trivial 8-byte element, so 500 pushes into a reserved array allocate nothing;
today the stride is already 8 (a pointer) but every element is a box, so the program exits 3.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

typealias PairArray = Array with Pair

function main() returns ExitCode
	var arr = PairArray.create()
	arr.reserve(500)

	let before = __Builtins.mmAllocLive()
	for i in 0 upto 500 'push'
		arr.push(Pair.create(i, hi: i))
	end 'push'
	let after = __Builtins.mmAllocLive()

	if arr.managed.elementSize() != 8 'stride'
		return 2
	end 'stride'

	if after != before 'alloc'
		return 3
	end 'alloc'

	var sum = 0
	for p in arr 'read'
		sum = sum + p.lo
	end 'read'

	if sum != 124750 'values'
		return 4
	end 'values'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.map-key-survives-rehash -->
RED: an inline record on the primitive road is a `Map` key that is copied rather than retained, and
building 500 of them allocates nothing — today each key is a box and the program exits 2. The keys are
built into a reserved holder and measured there, apart from the inserts, because the map's own columns
allocate whatever the key is.
```maxon
typealias Coord = int(0 to u16.max)

type Point implements Hashable, Equatable
	export let x as Coord
	export let y as Coord

	export static function create(x Coord, y Coord) returns Self
		return Self{x: x, y: y}
	end 'create'

	export function hash() returns HashValue
		return x * 31 + y
	end 'hash'

	export function equals(other Self) returns bool
		return x == other.x and y == other.y
	end 'equals'
end 'Point'

typealias PointArray = Array with Point
typealias PointMap = Map with (Point, Coord)

function main() returns ExitCode
	var keys = PointArray.create()
	keys.reserve(500)

	let before = __Builtins.mmAllocLive()
	for i in 0 upto 500 'build'
		keys.push(Point.create(i, y: i + 1))
	end 'build'
	let after = __Builtins.mmAllocLive()

	var m = PointMap.create()
	for k in keys 'insert'
		m.upsert(k, value: k.x)
	end 'insert'

	var found = 0
	for k in keys 'lookup'
		if m.contains(k) 'hit'
			found = found + 1
		end 'hit'
	end 'lookup'

	if found != 500 'missing'
		return 1
	end 'missing'

	if after != before 'alloc'
		return 2
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.module-level-let-is-image-data -->
CONTROL: a module-level `let` of an inline record is a constant word laid down with the program, so
nothing is live when `main` starts. The case is green before the change as well as after it — a top-level
`let` record is already image data, with nothing for `__module_init` to run — and it is here to hold that
property while the value stops being a record at all.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

let origin = Pair.create(1, hi: 2)

function main() returns ExitCode
	let live = __Builtins.mmAllocLive()

	if origin.lo != 1 'lo'
		return 90
	end 'lo'

	if origin.hi != 2 'hi'
		return 91
	end 'hi'

	return live as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.passed-and-returned-through-functions -->
RED: an inline record crosses a call door as a word in a register, so neither call allocates; today each
`create` in the callee boxes and the program exits 2.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function bumped(p Pair) returns Pair
	return Pair.create(p.lo + 1, hi: p.hi + 1)
end 'bumped'

function main() returns ExitCode
	let a = Pair.create(1, hi: 10)

	let before = __Builtins.mmAllocLive()
	let b = bumped(a)
	let c = bumped(b)
	let after = __Builtins.mmAllocLive()

	if c.lo != 3 'lo'
		return 1
	end 'lo'

	if c.hi != 12 'hi'
		return 1
	end 'hi'

	if after != before 'alloc'
		return 2
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.captured-by-a-closure -->
CONTROL: a captured inline record is a word in the environment, so calling the closure allocates nothing.
The case is green before the change too — the captured record does not escape `main` and the promoter
already puts it on the stack — and it is here to hold the reading once the promoter has nothing to do.
The bracket is around the CALLS, not around the closure literal, because the environment itself is a box
whatever it holds.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(3, hi: 4)
	let sum = function() gives p.lo + p.hi

	let before = __Builtins.mmAllocLive()
	let first = sum()
	let second = sum()
	let after = __Builtins.mmAllocLive()

	if first != 7 'firstValue'
		return 1
	end 'firstValue'

	if second != 7 'secondValue'
		return 1
	end 'secondValue'

	if after != before 'alloc'
		return 2
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.clone-is-the-identity -->
RED: cloning a word is copying it, so `clone()` allocates nothing; today it mints a second box and the
program exits 2.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(5, hi: 6)

	let before = __Builtins.mmAllocLive()
	let q = p.clone()
	let after = __Builtins.mmAllocLive()

	if q.lo != 5 'lo'
		return 1
	end 'lo'

	if q.hi != 6 'hi'
		return 1
	end 'hi'

	if after != before 'alloc'
		return 2
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.is-on-an-inline-record-is-refused -->
RED: two inline records are two words and there is no record for the two names to share, so `is` is
refused with the diagnostic every primitive already gets; today the program compiles and runs.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let a = Pair.create(1, hi: 2)
	let b = Pair.create(1, hi: 2)
	if a is b 'same'
		return 1
	end 'same'
	return 0
end 'main'
```
```maxoncstderr
error E3068: specs/fragments/inline-records/inline-records.is-on-an-inline-record-is-refused.test:16:7: 'is' requires reference types (structs), not primitive values
```

<!-- test: inline-records.try-otherwise-over-a-factory-returning-one -->
RED: a throwing factory hands back a word on the success path and the `otherwise` arm supplies another, so
neither arm allocates; today both branches box and the program exits 2.
```maxon
typealias Half = int(0 to u32.max)

enum MakeError implements Error
	tooBig
end 'MakeError'

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'

	export static function make(n Half) returns Self throws MakeError
		if n > 2 'refuse'
			throw MakeError.tooBig
		end 'refuse'
		return Self{lo: n, hi: n + 1}
	end 'make'
end 'Pair'

function main() returns ExitCode
	let before = __Builtins.mmAllocLive()
	let good = try Pair.make(1) otherwise Pair.create(0, hi: 0)
	let bad = try Pair.make(3) otherwise Pair.create(0, hi: 0)
	let after = __Builtins.mmAllocLive()

	if good.lo != 1 'goodLo'
		return 1
	end 'goodLo'

	if good.hi != 2 'goodHi'
		return 1
	end 'goodHi'

	if bad.lo != 0 'badLo'
		return 1
	end 'badLo'

	if bad.hi != 0 'badHi'
		return 1
	end 'badHi'

	if after != before 'alloc'
		return 2
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.a-var-field-stays-a-heap-record -->
CONTROL: a mutable field disqualifies the type — a word has nowhere to be written through — so `Pair` is
a two-slot heap record before the change and after it.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export var lo as Half
	export var hi as Half
end 'Pair'

function main() returns ExitCode
	return sizeof(Pair)
end 'main'
```
```exitcode
16
```

<!-- test: inline-records.a-signed-field-stays-a-heap-record -->
CONTROL: a signed range is not zero-based, so it has no storage width on the packing ladder and the type
stays a heap record.
```maxon
typealias Signed = int(i64.min to i64.max)

type Span
	export let low as Signed
	export let high as Signed

	export static function create(low Signed, high Signed) returns Self
		return Self{low: low, high: high}
	end 'create'
end 'Span'

function main() returns ExitCode
	return sizeof(Span)
end 'main'
```
```exitcode
16
```

<!-- test: inline-records.sixty-five-bits-stays-a-heap-record -->
CONTROL: 64 bits plus one is past the word, and the bound is the SUM rather than any single field, so a
whole-word pattern beside a single flag stays a heap record.
```maxon
typealias Word = bits(64)

type Flagged
	export let w as Word
	export let flag as bool

	export static function create(w Word, flag bool) returns Self
		return Self{w: w, flag: flag}
	end 'create'
end 'Flagged'

function main() returns ExitCode
	return sizeof(Flagged)
end 'main'
```
```exitcode
16
```

<!-- test: inline-records.a-generic-type-stays-a-heap-record -->
CONTROL: a type with a type parameter never qualifies, even at an instance whose arguments would qualify
on their own, so constructing one still takes a box. The control is read as an allocation rather than as a
`sizeof`, because `sizeof` on a generic instance answers the SLOT that holds it — 8 either way — and says
nothing about whether a box was minted. The box is pushed into a reserved array so the value is HELD: a
held element escapes, and an escaping record is never stack-promoted, so the one allocation is the
generic's own and the reading cannot be flattened by the promoter.
```maxon
typealias Half = int(0 to u32.max)

type Box uses T
	export let a as T
	export let b as T

	export static function create(a T, b T) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Box'

typealias HalfBox = Box with Half
typealias HalfBoxArray = Array with HalfBox

function main() returns ExitCode
	var holder = HalfBoxArray.create()
	holder.reserve(4)

	let before = __Builtins.mmAllocLive()
	holder.push(HalfBox.create(1, b: 2))
	let after = __Builtins.mmAllocLive()

	if holder.count() != 1 'held'
		return 1
	end 'held'

	if (after - before) != 1 'alloc'
		return 2
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.a-nonzero-lower-bound-stays-a-heap-record -->
CONTROL: the ladder is defined on zero-based ranges only — a range starting at 1 carries an offset a shift
and mask cannot express — so the type stays a heap record.
```maxon
typealias Tri = int(1 to 3)

type Duo
	export let a as Tri
	export let b as Tri

	export static function create(a Tri, b Tri) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Duo'

function main() returns ExitCode
	return sizeof(Duo)
end 'main'
```
```exitcode
16
```

<!-- test: inline-records.field-range-check-at-the-door -->
CONTROL: a field's declared range is checked where the value arrives whether the record is a word or a
box, and a literal past the range is refused before the program runs.
```maxon
typealias Quad = int(0 to 3)

type Packed
	export let q as Quad
	export let r as Quad

	export static function create() returns Self
		return Self{q: 4, r: 0}
	end 'create'
end 'Packed'

function main() returns ExitCode
	let p = Packed.create()
	return p.q + p.r
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/inline-records/inline-records.field-range-check-at-the-door.test:9:15: Value 4 is outside the range of 'Quad' (int(0 to 3))
```

<!-- test: inline-records.runtime-field-range-check -->
CONTROL: a value the compiler cannot fold panics at the field's door, the second field's as readily as the
first's. The panic is reported at the factory's own door — the parameter is where the laundered value
arrives — so the frame is the static method's.
```maxon
typealias Nib = int(0 to 15)

type Duo
	export let a as Nib
	export let b as Nib

	export static function create(a Nib, b Nib) returns Self
		return Self{a: a, b: b}
	end 'create'
end 'Duo'

function main() returns ExitCode
	var bad = 4
	bad = bad * 5
	let d = Duo.create(1, b: bad)
	return d.a + d.b
end 'main'
```
```exitcode
1
```
```stderr
panic at inline-records.runtime-field-range-check.test:8: Range check failed: value outside typealias 'Nib'
Stack trace:
  in Duo.create
  in main
  in mrt_start
```

<!-- test: inline-records.as-a-field-of-a-heap-record -->
CONTROL: a qualifying type held by a heap record is one 8-byte slot either way — a pointer today, the word
itself afterwards — so the outer record's size does not move.
```maxon
typealias Half = int(0 to u32.max)
typealias Integer = int(i64.min to i64.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

type Outer
	export var p as Pair
	export var n as Integer

	export static function create(p Pair, n Integer) returns Self
		return Self{p: p, n: n}
	end 'create'
end 'Outer'

function main() returns ExitCode
	return sizeof(Outer)
end 'main'
```
```exitcode
16
```

<!-- test: inline-records.equals-still-dispatches -->
CONTROL: `==` on a record calls the type's own `equals`, and an inline record is no exception.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'

	export function equals(other Self) returns bool
		return lo == other.lo and hi == other.hi
	end 'equals'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(7, hi: 8)
	let q = Pair.create(7, hi: 8)

	if p == q 'same'
		return 0
	end 'same'

	return 1
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.no-equals-is-still-refused -->
CONTROL: a type with no `equals` cannot be compared, and packing it into a word does not silently give it
word equality.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

function main() returns ExitCode
	let p = Pair.create(1, hi: 2)
	let q = Pair.create(1, hi: 2)
	if p == q 'eq'
		return 1
	end 'eq'
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/inline-records/inline-records.no-equals-is-still-refused.test:16:7: type mismatch: 'cannot compare struct with struct'
```

<!-- test: inline-records.as-an-existential -->
CONTROL: an inline record widens into an interface the way a primitive with an extension conformance does,
and the method dispatches through the table.
```maxon
typealias Half = int(0 to u32.max)

interface Describable
	function describe() returns String
end 'Describable'

type Pair implements Describable
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'

	function describe() returns String
		return "{lo}/{hi}"
	end 'describe'
end 'Pair'

function show(item Describable) returns String
	return item.describe()
end 'show'

function main() returns ExitCode
	print("{show(Pair.create(2, hi: 5))}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2/5
```

<!-- test: inline-records.a-sole-whole-word-field -->
CONTROL: one 64-bit field is the whole word, so the literal is the field's own value retagged and the read
is the word unmasked — the two branches a multi-field record never reaches. Every bit is set, so a read
that masked, shifted or sign-extended would print a different number.
```maxon
typealias Word = bits(64)

type Handle
	export let w as Word

	export static function create(w Word) returns Self
		return Self{w: w}
	end 'create'
end 'Handle'

function main() returns ExitCode
	let h = Handle.create(0xFFFFFFFFFFFFFFFF as Word)

	print("{h.w}\n")

	return sizeof(Handle)
end 'main'
```
```exitcode
8
```
```stdout
18446744073709551615
```

<!-- test: inline-records.as-a-union-payload -->
CONTROL: a union payload at an inline record is a SCALAR payload — the word sits in the case's slot with
nothing to retain or drop — so the case round-trips through a `match` and both fields read back.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

union Slot
	empty
	filled(p Pair)
end 'Slot'

function main() returns ExitCode
	let s = Slot.filled(Pair.create(7, hi: 3))

	match s 'slot'
		empty then return 1
		filled(p) then return (p.lo + p.hi) as ExitCode
	end 'slot'
end 'main'
```
```exitcode
10
```

<!-- test: inline-records.a-module-level-var-built-by-a-factory -->
CONTROL: a module-level `var` is filled by `__module_init` from a real call, and an inline record's slot
holds the word rather than a pointer — so the global reads back its fields and the program owes no cleanup.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

var origin = Pair.create(4, hi: 5)

function main() returns ExitCode
	return (origin.lo + origin.hi) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: inline-records.a-module-level-var-is-reassigned -->
RED: a module-level `var` of an inline record holds the WORD in its `.data` slot, so a whole new value
assigned to it is a scalar store and neither the read nor the write allocates.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Pair'

var slot = Pair.create(1, hi: 2)

function main() returns ExitCode
	let before = __Builtins.mmAllocLive()

	if slot.lo != 1 'firstLo'
		return 1
	end 'firstLo'

	slot = Pair.create(7, hi: 9)

	if slot.lo != 7 'secondLo'
		return 2
	end 'secondLo'

	if slot.hi != 9 'secondHi'
		return 3
	end 'secondHi'

	let after = __Builtins.mmAllocLive()

	if after != before 'alloc'
		return 4
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: inline-records.a-contested-alias-is-read-in-the-declaring-file -->
RED: a field's storage width is read through the alias the DECLARING file means, not through a fold over
every file's declaration. `wide.maxon` declares `Wide` as the whole signed span and never names `Packed`;
`packed.maxon` declares its own `Wide` and the type built on it, so `Packed` is two 32-bit fields in one
word. A width folded across both files would take the machine word for `Wide`, put the pair at 128 bits
and leave `Packed` a heap record.
```maxon
// --- file: wide.maxon
typealias Wide = int(i64.min to i64.max)

export function widest(v Wide) returns Wide
	return v - 1
end 'widest'

// --- file: packed.maxon
typealias Wide = int(0 to u32.max)

export type Packed
	export let lo as Wide
	export let hi as Wide

	export static function create(lo Wide, hi Wide) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'
end 'Packed'

export function packedSize() returns Wide
	return sizeof(Packed) as Wide
end 'packedSize'

// --- file: main.maxon
function main() returns ExitCode
	let p = Packed.create(4294967295, hi: 7)

	if p.lo != 4294967295 'lo'
		return 1
	end 'lo'

	if p.hi != 7 'hi'
		return 2
	end 'hi'

	if widest(100) != 99 'wide'
		return 3
	end 'wide'

	return packedSize() as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: inline-records.a-module-level-var-whose-factory-is-not-folded -->
RED: a factory the constant evaluator cannot fold — its body computes, and one argument rides a declared
default — leaves the binding to `__module_init`, which RUNS the call before `main`. An inline record's
slot is still one 8-byte word holding the VALUE, so the stored word reads back, a whole new value
assigned over it is a scalar store, and nothing is allocated or released either side of `main`.
```maxon
typealias Half = int(0 to u32.max)

type Pair
	export let lo as Half
	export let hi as Half

	export static function create(lo Half, hi Half) returns Self
		return Self{lo: lo, hi: hi}
	end 'create'

	export static function scaled(lo Half, by Half = 3) returns Self
		return Self{lo: lo * by, hi: lo + by}
	end 'scaled'
end 'Pair'

var built = Pair.scaled(5)

function main() returns ExitCode
	let before = __Builtins.mmAllocLive()

	if built.lo != 15 'initialLo'
		return 1
	end 'initialLo'

	if built.hi != 8 'initialHi'
		return 2
	end 'initialHi'

	built = Pair.create(20, hi: 21)

	if built.lo != 20 'secondLo'
		return 3
	end 'secondLo'

	if built.hi != 21 'secondHi'
		return 4
	end 'secondHi'

	let after = __Builtins.mmAllocLive()

	if after != before 'alloc'
		return 5
	end 'alloc'

	return 0
end 'main'
```
```exitcode
0
```
