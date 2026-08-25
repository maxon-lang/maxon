---
feature: array-refill
status: experimental
keywords: [array, refill, growFilled, fill, managed-memory, copy-on-write, bit-packing]
category: memory
---
# Array refill and growFilled

## Documentation

`Array` has two whole-range writers, and they divide by WHAT THEY WRITE rather than by what they
allocate. `growFilled(newLength, value:)` touches only the entries an extension ADDED and leaves
the standing prefix alone; `refill(newLength, value:)` sets the length and rewrites EVERY entry of
`[0, newLength)`, so it also SHRINKS. Both exist because a column that is extended needs its new
entries to carry a MEANING, not merely a defined bit pattern.

Every entry either writes goes through the buffer's own element store, so the rules the store keeps
are the rules these two keep, and every one of them is observable from outside:

- **A copy-on-write buffer is detached before it is written.** An array someone else is viewing (a
  slice is a zero-copy view of the same bytes) must not be written in place: the viewer keeps the
  bytes it was cut from, and the array being written gets a buffer of its own.
- **A SUB-BYTE-PACKED element is written as a FIELD, never as a byte.** An `Array with bool` packs 8
  elements per byte and an `Array with int(0 to 3)` packs 4, so a write that took whole bytes would
  either erase the neighbours a field shares its byte with or store the wrong width.
- **A MANAGED element's slot holds exactly one reference.** A `String` or struct element is a
  reference the array owns one of, so every entry these two overwrite must release what it held and
  take one for what it stores — a leak (exit 101) or a double free otherwise.
- **Slots outside `[0, length)` still read ZERO.** A refill that SHRINKS erases what it vacates, so
  a later grow exposes zeroed slots rather than the previous occupants.

Both are built on ONE buffer primitive — `managed.fill(from, count:, value:)`, which writes `value` into
every slot of `[from, from + count)` and ANSWERS WHETHER IT DID. It applies to a TRIVIAL element (an int, a
float, a bool, a byte, a ranged alias over one of those) and DECLINES for a MANAGED one, because a managed
element's slot holds a reference that has to be taken and released per slot, and that ownership belongs to
the store the compiler emits rather than to a bulk runtime loop. A caller that gets `false` back does the
per-element loop itself; `refill` and `growFilled` are exactly that caller. The window is checked against
the LIVE LENGTH, and `from < 0`, `count < 0` or `from + count > length` throws
`__ManagedMemoryError.indexOutOfBounds`.

## Tests

<!-- test: refill-packed-bool -->
### refill writes every field of a bit-packed bool array
8 bools share a byte, so a fill that wrote bytes rather than fields would set neighbours it was
never given — and the count below would not be the length.
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.push(false)
	arr.push(true)
	arr.push(false)
	arr.refill(11, value: true)
	var lit = 0
	for b in arr 'count'
		if b 'set'
			lit = lit + 1
		end 'set'
	end 'count'
	return lit
end 'main'
```
```exitcode
11
```

<!-- test: refill-packed-bool-to-false -->
### refill to false clears every field and nothing else
The other direction of the same rule: an element WIDER than the field would clear a neighbour, and
a fill that skipped the read-modify-write would leave one standing.
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var arr = BoolArray.create()
	arr.refill(9, value: true)
	arr.refill(9, value: false)
	var lit = 0
	for b in arr 'count'
		if b 'set'
			lit = lit + 1
		end 'set'
	end 'count'
	return lit + arr.count()
end 'main'
```
```exitcode
9
```

<!-- test: refill-packed-ranged-int -->
### refill writes every field of a 2-bit ranged-int array
`int(0 to 3)` packs 4 elements per byte, so the same field-granular write has to hold at a width
that is neither a byte nor a bit.
```maxon
typealias Quarter = int(0 to 3)
typealias QuarterArray = Array with Quarter

function main() returns ExitCode
	var arr = QuarterArray.create()
	arr.push(1 as Quarter)
	arr.refill(7, value: 3 as Quarter)
	var total = 0
	for q in arr 'sum'
		total = total + q
	end 'sum'
	return total + arr.managed.elementSize()
end 'main'
```
```exitcode
19
```

<!-- test: refill-through-a-live-view-keeps-the-old-bytes -->
### refill detaches from a buffer someone is viewing
A slice is a zero-copy VIEW of the array's own bytes. Filling in place would rewrite what the
viewer is looking at; the fill detaches first, so the view keeps what it was cut from.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	let view = try arr.slice(0, endIndex: 2) otherwise panic("slice: [0, 2) is inside a four-element array")
	arr.refill(4, value: 9)
	let v0 = try view.get(0) otherwise 0
	let v1 = try view.get(1) otherwise 0
	let a0 = try arr.get(0) otherwise 0
	return v0 + v1 + a0
end 'main'
```
```exitcode
12
```

<!-- test: refill-writes-the-whole-element-not-its-low-byte -->
### refill writes every byte of a machine-word element
An `int` element is eight bytes and one memory access wide. A write (or a read back) that moved only
the LOW byte would round-trip every value under 256 unchanged and truncate everything above — so the
value here has bytes set above the first, which is the only kind that can tell the two apart.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.refill(3, value: 16909060)
	var total = 0
	for v in arr 'sum'
		total = total + v
	end 'sum'
	if total == 50727180 'everyByteSurvived'
		return 7
	end 'everyByteSurvived'
	return 1
end 'main'
```
```exitcode
7
```

<!-- test: refill-to-zero-length -->
### refill to length 0 empties the array
The degenerate range, which the fill must accept rather than treat as an out-of-bounds ask: the
window `[0, 0)` is empty, not invalid.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(5)
	arr.push(6)
	arr.refill(0, value: 9)
	return arr.count() + 7
end 'main'
```
```exitcode
7
```

<!-- test: grow-filled-preserves-the-prefix -->
### growFilled writes only the entries it added
Its whole difference from `refill` is the range it touches, so the standing prefix must come
through untouched.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.growFilled(5, value: 9)
	var total = 0
	for v in arr 'sum'
		total = total + v
	end 'sum'
	return total
end 'main'
```
```exitcode
30
```

<!-- test: grow-filled-does-nothing-when-it-adds-nothing -->
### growFilled to a length it already has writes nothing
`newLength <= count()` adds no entry, so there is nothing to fill and the existing entries keep
their values.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(4)
	arr.push(5)
	arr.growFilled(2, value: 9)
	var total = 0
	for v in arr 'sum'
		total = total + v
	end 'sum'
	return total + arr.count()
end 'main'
```
```exitcode
11
```

<!-- test: shrink-then-grow-reads-zero-past-the-old-length -->
### a refill that shrinks erases what it vacates
The `[length, capacity)`-reads-ZERO invariant, observed from the outside: the slots a shrinking
refill gave up must not hand their old contents back to a later grow.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.refill(4, value: 7)
	arr.refill(2, value: 7)
	arr.resize(4)
	let a2 = try arr.get(2) otherwise -1
	let a3 = try arr.get(3) otherwise -1
	return a2 + a3 + 5
end 'main'
```
```exitcode
5
```

<!-- test: refill-a-string-element-array -->
### refill on a String-element array holds exactly one reference per slot
A `String` element is a reference the array owns one of. Every slot the refill overwrites must
release what it held and take a reference to what it stores, or the program leaks (exit 101) or
double-frees.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("alpha")
	arr.push("beta")
	arr.refill(4, value: "zeta")
	var hits = 0
	for s in arr 'sum'
		if s == "zeta" 'match'
			hits = hits + 1
		end 'match'
	end 'sum'
	return hits + arr.count()
end 'main'
```
```exitcode
8
```

<!-- test: refill-a-struct-element-array -->
### refill on a struct-element array holds exactly one reference per slot
The same obligation for a boxed struct element, which is where a fill that wrote the pointer word
itself would double-free.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.push(Slot.create(1))
	arr.refill(3, value: Slot.create(4))
	var total = 0
	for s in arr 'sum'
		total = total + s.value
	end 'sum'
	return total
end 'main'
```
```exitcode
12
```

<!-- test: grow-filled-a-string-element-array -->
### growFilled on a String-element array keeps the prefix and owns the tail
`growFilled`'s own half of the managed-element obligation: the prefix keeps the references it
already holds and only the added slots take new ones.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("ab")
	arr.growFilled(4, value: "cde")
	var hits = 0
	for s in arr 'sum'
		if s == "cde" 'match'
			hits = hits + 1
		end 'match'
	end 'sum'
	let first = try arr.get(0) otherwise "??"
	if first == "ab" 'prefixKept'
		hits = hits + 10
	end 'prefixKept'
	return hits
end 'main'
```
```exitcode
13
```

<!-- test: grow-filled-a-struct-element-array -->
### growFilled on a struct-element array keeps the prefix and owns the tail
The struct twin, which is also the case `array-slots` reaches through `resize`'s refusal: a
managed element cannot be zero-initialized, so this is the grow that supplies one.
```maxon
typealias Integer = int(i64.min to i64.max)

type Slot
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var arr = SlotArray.create()
	arr.push(Slot.create(6))
	arr.growFilled(3, value: Slot.create(7))
	var total = 0
	for s in arr 'sum'
		total = total + s.value
	end 'sum'
	return total
end 'main'
```
```exitcode
20
```

<!-- test: fill-detaches-from-a-buffer-someone-is-viewing -->
### the buffer fill detaches from a buffer someone is viewing
`refill` reaches `fill` only after `resize`, which has already detached — so this calls the primitive
DIRECTLY, which is the only program in which the fill's own copy-on-write detach is the one doing the work.
A slice is a zero-copy VIEW of the same bytes; filling in place would rewrite what the viewer sees.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	let view = try arr.slice(0, endIndex: 2) otherwise panic("slice: [0, 2) is inside a four-element array")
	let applied = try arr.managed.fill(0, count: 4, value: 9) otherwise panic("fill: [0, 4) is exactly the live range")
	if not applied 'aTrivialElementMustApply'
		return 1
	end 'aTrivialElementMustApply'
	let v0 = try view.get(0) otherwise 0
	let v1 = try view.get(1) otherwise 0
	let a0 = try arr.get(0) otherwise 0
	return v0 + v1 + a0
end 'main'
```
```exitcode
12
```

<!-- test: fill-declines-a-managed-element-and-writes-nothing -->
### the buffer fill declines a managed element and writes nothing
The answer `refill` reads. A `String` element's slot holds exactly one reference, and the retain/release
per slot is the compiler's to emit at each store — so the runtime loop refuses the range outright rather
than respelling that ownership. Nothing is written, nothing leaks (a leak here is exit 101), and the value
it was handed is still the caller's to drop.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var arr = StringArray.create()
	arr.push("alpha")
	arr.push("beta")
	let applied = try arr.managed.fill(0, count: 2, value: "zeta") otherwise panic("fill: [0, 2) is exactly the live range")
	if applied 'aManagedElementMustDecline'
		return 1
	end 'aManagedElementMustDecline'
	let first = try arr.get(0) otherwise "??"
	if first == "alpha" 'theSlotIsUntouched'
		return 6
	end 'theSlotIsUntouched'
	return 2
end 'main'
```
```exitcode
6
```

<!-- test: fill-refuses-a-window-past-the-live-length -->
### the buffer fill refuses a window past the live length
The window is `[from, from + count)` and the bound is the LIVE LENGTH, not the capacity: a fill writes
slots a reader is entitled to read back, so a window ending above the length would publish bytes nothing
had length for.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(3) otherwise return 2
	let applied = try mm.fill(1, count: 3, value: 7) otherwise return 8
	if applied 'itMustNotHaveApplied'
		return 3
	end 'itMustNotHaveApplied'
	return 4
end 'main'
```
```exitcode
8
```

<!-- test: fill-refuses-a-negative-start -->
### the buffer fill refuses a negative start
`StdCmpPred` is signed, so `from + count > length` is FALSE for a negative `from` and reads as in range —
the address it would then compute sits BEFORE the buffer. The negative test is its own compare.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(3) otherwise return 2
	let applied = try mm.fill(-1, count: 1, value: 7) otherwise return 8
	if applied 'itMustNotHaveApplied'
		return 3
	end 'itMustNotHaveApplied'
	return 4
end 'main'
```
```exitcode
8
```

<!-- test: fill-refuses-a-negative-count -->
### the buffer fill refuses a negative count
The other half of the same signed-compare hazard, and the one whose silent answer would be a LIE rather
than a fault: `from + count` is below the length for every negative count, the loop then runs zero times,
and the call would report that it had applied.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(3) otherwise return 2
	let applied = try mm.fill(0, count: -1, value: 7) otherwise return 8
	if applied 'itMustNotHaveApplied'
		return 3
	end 'itMustNotHaveApplied'
	return 4
end 'main'
```
```exitcode
8
```

<!-- test: fill-writes-only-its-own-window -->
### the buffer fill writes only its own window
`growFilled` reaches the primitive with a non-zero `from`, so the window's LOW end has to bind as well as
its high one — a fill that started at 0 whatever it was asked would rewrite the prefix `growFilled` exists
to preserve.
```maxon
function main() returns ExitCode
	var mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(4) otherwise return 2
	try mm.set(0, value: 5) otherwise return 3
	let applied = try mm.fill(1, count: 3, value: 2) otherwise return 4
	if not applied 'aTrivialElementMustApply'
		return 5
	end 'aTrivialElementMustApply'
	var total = 0
	var i = 0
	while i < 4 'sum'
		total = total + (try mm.get(i) otherwise return 6)
		i = i + 1
	end 'sum'
	return total as ExitCode
end 'main'
```
```exitcode
11
```
