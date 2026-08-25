---
feature: managed-memory-methods
status: stable
keywords: [managed-memory, methods, builtin, buffer, setlength, shrink, leak]
category: dev
---

# __ManagedMemory Methods

## Documentation

`__ManagedMemory` is a compiler builtin type providing heap-backed buffer storage. It has instance methods for element access, mutation, and buffer management, as well as static methods for creation. All element-access and mutation methods perform runtime bounds checking and panic on invalid access.

### Instance Methods

- `length()` returns int
- `capacity()` returns int
- `elementSize()` returns int
- `setLength(n)` — set element count (panics if n > capacity). Shrinking (n < current length) VACATES the dropped slots [n, length): each managed element is released (so a shrink never leaks) and the slot is then erased. Growing exposes the slots [length, n) as-is, and they are always ZERO — every operation that vacates a slot (`clear`, `remove`, a shrinking `setLength`) erases it on the way out, and fresh capacity comes zeroed from the allocator. A grown slot therefore reads `0` for a scalar element and empty/null for a managed one, never a stale value or an already-released pointer. Growing must NOT initialize the exposed slots itself: its callers (`push`, `insert`, string building) stage the new elements FIRST and use `setLength` to publish them.
- `get(index)` returns Element (panics if index >= length)
- `set(index, value)` (panics if index >= capacity)
- `grow(newCapacity)` (panics if newCapacity < current capacity)
- `shiftRight(index, count)` (panics if index or index+count >= capacity)
- `shiftLeft(index, count)` (panics if index or index+count >= capacity)
- `byteAt(index)` returns int (panics if index >= length * elementSize)
- `setByte(index, value)` (panics if index >= length * elementSize)
- `append(other)` — append another buffer's data in-place
- `slice(start, end)` returns __ManagedMemory (panics if end > length or start > end)
- `toCString()` returns cstring — a NUL-terminated byte pointer view of the buffer
- `makeCharFromBytes(pos, len)` returns int

### Static Methods

- `__ManagedMemory.create(capacity, elementSize)` returns __ManagedMemory
- `__ManagedMemory.fromCString(cstr)` returns __ManagedMemory — copy a NUL-terminated `cstring` (including its terminator) into a fresh byte-element buffer

## Tests

<!-- test: array-via-methods -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	arr.push(50)
	return arr.count()
end 'main'
```
```exitcode
5
```

<!-- test: array-get-set -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	try arr.set(1, value: 99) otherwise panic("test invariant: set OOB")
	let v = try arr.get(1) otherwise 'err'
		return 0
	end 'err'
	return v
end 'main'
```
```exitcode
99
```

<!-- test: array-slice -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	arr.push(50)
	let sliced = try arr.slice(1, endIndex: 4) otherwise return 99
	return sliced.count()
end 'main'
```
```exitcode
3
```

<!-- test: array-insert-remove -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	arr.push(30)
	arr.insert(1, value: 20)
	let removed = try arr.remove(0) otherwise 'err'
		return 99
	end 'err'
	return removed + arr.count()
end 'main'
```
```exitcode
12
```

<!-- test: string-operations -->
```maxon
function main() returns ExitCode
	let s = "hello world"
	return s.byteLength()
end 'main'
```
```exitcode
11
```

<!-- test: string-append -->
```maxon
function main() returns ExitCode
	var s = "hello"
	s.append(" world")
	return s.byteLength()
end 'main'
```
```exitcode
11
```

<!-- disabled-test: cstring-round-trip -->
<!-- TWO blockers, and the note named only the SECOND. (1) `__ManagedMemory.fromCString(cstr)` is not built — it is the FIRST thing the case reaches, and it takes a `cstring`, a type shv2 has no producer for at all. (2) `String.cstr()` is not built either. Neither is R4.4's: a member whose only argument type does not exist cannot be given a reachable caller, so building it would be a mechanism no spec can exercise. They arrive together, with the rung that gives shv2 a `cstring`. -->
```maxon
function main() returns ExitCode
	let s = "hello world"
	let mm = __ManagedMemory.fromCString(s.cstr())
	let back = String.init(mm)
	return back.byteLength()
end 'main'
```
```exitcode
11
```

<!-- test: empty-bstring-push -->
```maxon
function main() returns ExitCode
	var v = b""
	v.push(7)
	v.push(8)
	return v.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: empty-string-bytes-push -->
```maxon
function main() returns ExitCode
	let s = ""
	var v = s.toByteArray()
	v.push(65)
	return v.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: array-literal -->
```maxon
function main() returns ExitCode
	let arr = [10, 20, 30, 40]
	let v = try arr.get(2) otherwise 'err'
		return 0
	end 'err'
	return v
end 'main'
```
```exitcode
30
```

<!-- test: array-growth -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	var i = 0
	while i < 100 'fill'
		arr.push(i)
		i = i + 1
	end 'fill'
	return arr.count()
end 'main'
```
```exitcode
100
```

### Bounds checking

<!-- test: bounds-get-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	let v = try arr.managed.get(10) otherwise 42
	return v
end 'main'
```
```exitcode
42
```

<!-- test: bounds-set-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	try arr.managed.set(10, 99) otherwise 'oob'
		return 7
	end 'oob'
	return 0
end 'main'
```
```exitcode
7
```

<!-- test: bounds-setlength-exceeds-capacity -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	try arr.managed.setLength(100) otherwise 'overlen'
		return 7
	end 'overlen'
	return 0
end 'main'
```
```exitcode
7
```

<!-- test: bounds-byte-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	let b = try arr.managed.byteAt(100) otherwise 7
	return b as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: bounds-slice-oob -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	let sliced = try arr.managed.slice(0, 10) otherwise 'oob'
		return 7
	end 'oob'
	return sliced.length() as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: bounds-valid-operations -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let arr = IntArray.create()
	try arr.managed.grow(8) otherwise panic("grow OOB")
	try arr.managed.setLength(4) otherwise panic("setLength OOB")
	try arr.managed.set(0, 10) otherwise panic("set OOB")
	try arr.managed.set(1, 20) otherwise panic("set OOB")
	try arr.managed.set(2, 30) otherwise panic("set OOB")
	try arr.managed.set(3, 40) otherwise panic("set OOB")
	let v0 = try arr.managed.get(0) otherwise panic("get OOB")
	let v1 = try arr.managed.get(1) otherwise panic("get OOB")
	let v2 = try arr.managed.get(2) otherwise panic("get OOB")
	let v3 = try arr.managed.get(3) otherwise panic("get OOB")
	let sum = v0 + v1 + v2 + v3
	return sum
end 'main'
```
```exitcode
100
```

<!-- test: bounds-negative-index -->
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	arr.push(3)
	arr.push(4)
	let v = try arr.managed.get(-1) otherwise 7
	return v as ExitCode
end 'main'
```
```exitcode
7
```

### Shrinking frees dropped elements

Shrinking a buffer of managed elements (via `setLength`/`Array.resize` to a smaller
length) must release the elements leaving the live range, or they leak. Every test
below runs under the leak gate (no compiler stderr expected → leak-checked), so a
dropped element that is not freed fails the test.

<!-- test: shrink-managed-elements-no-leak -->
Push three heap strings, then `resize(1)`. The two dropped strings must be freed.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("first heap string long enough to require an allocation")
	xs.push("second heap string long enough to require an allocation")
	xs.push("third heap string long enough to require an allocation")
	xs.truncate(1)
	return xs.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: shrink-setlength-direct-no-leak -->
Shrink directly through the `__ManagedMemory.setLength` builtin (the path
`Array.resize` lowers to). Dropping the tail must free those elements.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha string payload long enough to need a heap allocation")
	xs.push("beta string payload long enough to need a heap allocation")
	xs.push("gamma string payload long enough to need a heap allocation")
	try xs.managed.setLength(1) otherwise panic("setLength shrink cannot exceed capacity")
	return xs.managed.length() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: shrink-struct-elements-no-leak -->
Elements are structs carrying a heap `String` field; shrinking must cascade the
teardown into each dropped struct's field.
```maxon
type Box
	var payload as String

	static function create(payload String) returns Self
		return Self{payload: payload}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function main() returns ExitCode
	var boxes = BoxArray.create()
	boxes.push(Box.create("box one with a heap string payload long enough"))
	boxes.push(Box.create("box two with a heap string payload long enough"))
	boxes.push(Box.create("box three with a heap string payload long enough"))
	boxes.push(Box.create("box four with a heap string payload long enough"))
	boxes.truncate(2)
	return boxes.count() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: shrink-to-zero-no-leak -->
Shrinking all the way to zero frees every element (equivalent to `clear`).
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("only string payload long enough to need a heap allocation")
	xs.push("other string payload long enough to need a heap allocation")
	xs.truncate(0)
	return xs.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- test: shrink-then-regrow-empty-slots -->
After shrinking and regrowing over the same slots, the regrown slots read as empty
(the stale, already-freed pointers must not reappear). Pushing a fresh element after
the regrow must not double-free or leak.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("first string payload long enough to need a heap allocation")
	xs.push("second string payload long enough to need a heap allocation")
	xs.push("third string payload long enough to need a heap allocation")
	xs.truncate(1)
	xs.push("replacement payload long enough to need a heap allocation here")
	let v = try xs.get(1) otherwise ""
	return v.count() as ExitCode
end 'main'
```
```exitcode
62
```

### Regrowing exposes ZEROED slots

THE CAPACITY-SLOT INVARIANT: the slots in `[length, capacity)` are always zero. So
growing the length — `resize`, or `setLength` directly — can only ever expose zeroed
slots, whether they are fresh capacity or slots the array used before and gave up.
(A MANAGED element type reaches this only through `setLength`: `Array.resize` on one is
refused at compile time, E3106, because a zeroed slot there holds no element.)

Every operation that VACATES a slot erases it on the way out: `clear`, `remove`/`pop`,
and a shrinking `setLength` — which is what `resize` and `truncate` both go through. Without that, growing back over a vacated slot re-exposes
whatever it held — a stale scalar (silent garbage), or, far worse, a pointer the array
has already released, which its destructor then decrefs a SECOND time (a double free
reachable from ordinary, non-unsafe API).

The tests below drive each vacate site and then grow back over it. The managed ones
also run under the leak gate, so a slot that is erased without releasing its element
fails just as loudly as one released without being erased.

<!-- test: clear-then-resize-scalar-reads-zeros -->
`clear()` then `resize()` back over the SAME slots. Every slot must read 0, not the
value it held before the clear.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	var i = 0
	while i < 8 'fill'
		a.push(77)
		i = i + 1
	end 'fill'
	a.clear()
	a.resize(8)

	var stale = 0
	var j = 0
	while j < 8 'read'
		let v = try a.get(j) otherwise -1
		print("{v}\n")
		if v != 0 'garbage'
			stale = stale + 1
		end 'garbage'
		j = j + 1
	end 'read'
	return stale as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
0
0
0
0
0
0
0
0
```

<!-- test: clear-then-regrow-managed-no-double-free -->
`clear()` RELEASES the elements; restoring the length back over those slots must not restore
it over the dead pointers, or the array's destructor decrefs each of them a second time.
Every regrown slot must read as EMPTY.

The regrow goes through `setLength` rather than `Array.resize` because `resize` on a
managed-element array is refused at compile time (**E3106**) — a grown slot holds no element.
This buffer is exactly the case that refusal describes, and `__ManagedMemory` is the layer
that still offers the operation and owns the invariant being tested.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	for i in 0 upto 4 'fill'
		a.push("value-{i} padded out so this string needs a heap allocation")
	end 'fill'
	a.clear()
	try a.managed.setLength(4) otherwise panic("setLength: clear() keeps the capacity the four pushes bought")

	var empties = 0
	for j in 0 upto 4 'read'
		let v = try a.get(j) otherwise ""
		if v.count() == 0 'empty'
			empties = empties + 1
		end 'empty'
	end 'read'
	return empties as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: shrink-then-regrow-managed-no-double-free -->
The same shape through the SHRINK path rather than `clear`: `truncate(1)` releases the
two dropped strings, and the length then grows back over their slots. The dropped pointers
must not reappear.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha payload long enough to need a heap allocation here")
	a.push("beta payload long enough to need a heap allocation here")
	a.push("gamma payload long enough to need a heap allocation here")
	a.truncate(1)
	try a.managed.setLength(3) otherwise panic("setLength: truncate keeps the capacity the three pushes bought")

	var empties = 0
	for j in 1 upto 3 'read'
		let v = try a.get(j) otherwise ""
		if v.count() == 0 'empty'
			empties = empties + 1
		end 'empty'
	end 'read'
	let kept = try a.get(0) otherwise ""
	print("kept={kept.count()} empties={empties}\n")
	return empties as ExitCode
end 'main'
```
```exitcode
2
```
```stdout
kept=56 empties=2
```

<!-- test: pop-then-regrow-managed-no-double-free -->
`pop()` hands its element to the caller — the array no longer owns it — but the slot
still holds the pointer. Growing back over that slot must not re-adopt an element the
caller now owns, or it is freed twice.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("one payload long enough to need a heap allocation here")
	a.push("two payload long enough to need a heap allocation here")
	let popped = try a.pop() otherwise ""
	try a.managed.setLength(2) otherwise panic("setLength: pop leaves the capacity the two pushes bought")

	let regrown = try a.get(1) otherwise ""
	print("popped={popped.count()} regrown={regrown.count()}\n")
	return regrown.count() as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
popped=54 regrown=0
```

<!-- test: remove-then-resize-scalar-reads-zero -->
`remove()` shifts the tail down, which leaves the old last slot holding a stale
duplicate of the element now one position lower. Growing back over it must read 0.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	a.push(11)
	a.push(22)
	a.push(33)
	let removed = try a.remove(0) otherwise -1
	a.resize(3)

	var i = 0
	while i < 3 'read'
		let v = try a.get(i) otherwise -1
		print("{v}\n")
		i = i + 1
	end 'read'
	return removed as ExitCode
end 'main'
```
```exitcode
11
```
```stdout
22
33
0
```

<!-- test: clear-then-resize-bool-reads-false -->
Sub-byte-packed elements take the same invariant: a `bool` array's vacated bits are
cleared, so regrown slots read `false`.
```maxon
typealias BoolArray = Array with bool

function main() returns ExitCode
	var b = BoolArray.create()
	b.push(true)
	b.push(true)
	b.push(true)
	b.clear()
	b.resize(3)

	var stale = 0
	var i = 0
	while i < 3 'read'
		let v = try b.get(i) otherwise true
		print("{v}\n")
		if v 'set'
			stale = stale + 1
		end 'set'
		i = i + 1
	end 'read'
	return stale as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
false
false
false
```

### `__ManagedMemoryError` is DISCRIMINABLE, not just thrown

R4.2 gave the three delivered members (`create`/`setLength`/`setByte`) an error enum of their OWN rather
than `ArrayError`, because `invalidLength` and `invalidByteRange` name conditions `ArrayError` has no case
for — and it made the enum's case ORDER a wire format (the ordinal IS the flag the runtime returns) for
exactly that reason. **A wire format with no reader is a claim nothing checks**, and until the review of
that rung there was no reader: `ProgramSignatures.throwsOf` answered `none` for the three callees, so
`otherwise (e)` bound `e` with no type and the `match` below was refused as
`E2015: … a match pattern naming 'invalidAllocation' … enum-case patterns arrive in a later wave` — a
diagnostic about a MISSING FEATURE, for a program whose feature is present. (The same defect R4.1 had to
fix for `__ManagedFileError`; `runtimeThrowsClause` is now the one home both families answer through.)

So this case pins the ORDINALS, not merely that a throw happens: six refusals across three members, each
landing on the case the operation is documented to report. A shifted ordinal reroutes every arm at once and
the `default panic` says which.

<!-- test: managed-memory-error-variants -->
```maxon
function main() returns ExitCode
	var seen = 0
	try __ManagedMemory.create(4, 0) otherwise (e) 'zeroElementSize'
		match e 'k'
			invalidAllocation then seen = seen + 1
			default panic("create(4, 0) must report invalidAllocation")
		end 'k'
	end 'zeroElementSize'
	try __ManagedMemory.create(-1, 1) otherwise (e) 'negativeCount'
		match e 'k'
			invalidAllocation then seen = seen + 1
			default panic("create(-1, 1) must report invalidAllocation")
		end 'k'
	end 'negativeCount'

	var m = try __ManagedMemory.create(4, 1) otherwise 'createFail'
		return 1
	end 'createFail'
	try m.setLength(5) otherwise (e) 'aboveCapacity'
		match e 'k'
			invalidLength then seen = seen + 1
			default panic("setLength above capacity must report invalidLength")
		end 'k'
	end 'aboveCapacity'
	try m.setLength(-1) otherwise (e) 'negativeLength'
		match e 'k'
			invalidLength then seen = seen + 1
			default panic("setLength(-1) must report invalidLength")
		end 'k'
	end 'negativeLength'
	try m.setByte(4, 65) otherwise (e) 'pastLength'
		match e 'k'
			invalidByteRange then seen = seen + 1
			default panic("setByte at the live length must report invalidByteRange")
		end 'k'
	end 'pastLength'
	try m.setByte(-1, 65) otherwise (e) 'negativeOffset'
		match e 'k'
			invalidByteRange then seen = seen + 1
			default panic("setByte(-1) must report invalidByteRange")
		end 'k'
	end 'negativeOffset'

	if seen == 6 'allSix'
		return 42
	end 'allSix'
	return 1
end 'main'
```
```exitcode
42
```

### R4.4 probes — the buffer surface at its edges

These are shv2-authored, one per boundary the R4.4 implementation decides. Each was measured against the
implementation before it was committed; none is a restatement of a `/specs` case.

<!-- test: buffer-surface-does-not-leak-onto-a-byte-array -->

⭐⭐ **THE GUARD R4.2 COULD NOT WRITE.** R4.2 answered "is this a `__ManagedMemory`?" with an ELEMENT test
(`giid == internArrayByteInstance()`), so every buffer member was visible on a user's own `ByteArray` too — a
cost its own comment recorded and could not remove, because a receiver carried no memory of the surface it was
reached through. R4.4 answers with PROVENANCE instead (`Parser.bufferSurfaceValues`), and this program is what
says so: `"ab".toByteArray()` is byte-elemented, so the old gate ADMITTED `setLength` on it. The sibling case
below pins the same refusal for a non-byte array, which the old gate also refused — together they show the
gate moved rather than merely narrowed.
```maxon
function main() returns ExitCode
	var b = "ab".toByteArray()
	try b.setLength(1) otherwise return 1
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:8: Unsupported: `Array` member 'setLength' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: buffer-of-a-slice-is-a-buffer-and-detaches-before-it-writes -->

A `slice` taken THROUGH the buffer surface is itself a buffer — the surface rides the VALUE, so `sub.managed`
resolves on a binding the slice produced. And that slice is a zero-copy VIEW, whose `capacity@16` is the
NEGATIVE `ViewBufferCapacity` sentinel: `setLength` and `setByte` must therefore detach it to a private buffer
before they read a bound or write a byte, or the first refuses every length and the second rewrites the
parent's bytes. The parent is read back afterwards to prove it did not move.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(11)
	arr.push(22)
	arr.push(33)
	let sub = try arr.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	try sub.managed.setLength(1) otherwise panic("setLength: a detached view publishes its live length")
	try sub.managed.setByte(0, 99) otherwise panic("setByte: offset 0 of a detached 8-byte slot")
	let parent = try arr.get(0) otherwise panic("get: index 0 of a length-3 array")
	return (sub.managed.length() + parent) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: byte-at-reads-the-word-buffer-a-byte-at-a-time -->

`byteAt` addresses BYTES where `get` addresses ELEMENTS, and at element size 8 the two differ — which is the
whole reason `byteAt` is not `get`'s alias. 258 is `0x0102`, so its low byte is 2 and its second byte is 1 on
a little-endian target. It also pins the element TYPING `create`'s literal element size decides: 258 does not
fit a `Byte`, so under R4.2's byte-only binding this program was refused outright.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(2, elementSize: 8) otherwise return 1
	try mm.setLength(1) otherwise return 2
	try mm.set(0, value: 258) otherwise return 3
	let lo = try mm.byteAt(0) otherwise return 4
	let hi = try mm.byteAt(1) otherwise return 5
	return (lo + hi) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: byte-at-stops-at-the-live-length -->

`byteAt`'s bound is `length · element_size` — the LIVE length, not the capacity that `setByte` is bounded by.
The asymmetry is v1's and it is deliberate (`stdlib/Internals.maxon:3507-3530`): a write stages bytes into
allocated slots BEFORE a length publishes them, a read has nothing to see there. This asks for the byte at
exactly the limit.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	let v = try mm.byteAt(2) otherwise 7
	return v as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: grow-to-the-same-capacity-then-below-it -->

`grow` raises the capacity to EXACTLY what it is asked for. Asked for what it already has it is a no-op;
asked to LOWER it, it throws `invalidCapacity` rather than silently keeping the larger buffer — which is the
one thing that separates it from `reserve` (v1 `stdlib/Internals.maxon:3485-3503`).
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(8, elementSize: 8) otherwise return 1
	try mm.grow(8) otherwise return 2
	if mm.capacity() != 8 'cap'
		return 3
	end 'cap'
	try mm.grow(4) otherwise (e) 'shrink'
		match e 'k'
			invalidCapacity then return 42
			default panic("grow below the current capacity must report invalidCapacity")
		end 'k'
	end 'shrink'
	return 5
end 'main'
```
```exitcode
42
```

### The `__ManagedMemory` mutators are not `Array` methods, and a `let` receiver says so

<!-- test: error.managed-memory-mutator-is-not-an-array-method -->

`__ManagedMemory` IS an `Array with Byte` at this rung, so a `__ManagedMemory` member necessarily arrives on
an array receiver and can only be gated on that INSTANCE. `setLength`/`setByte` are therefore dispatched only
for the byte instance — and the immutable-receiver rule (E3019) has to be gated on the same answer, or it
complains about MUTABILITY for a method the receiver has no dispatch arm for. R4.2 shipped it ungated: this
program reported `E3019 cannot pass 'a' to function that mutates parameter 'self'`, while the identical
program with `var` reported the unknown-method refusal below. Refused either way — but a program must be
refused for the REASON that is true of it, and the true reason is that `Array with Int` has no `setLength`.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	let a = IntArray.create()
	try a.setLength(2) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:7:8: Unsupported: `Array` member 'setLength' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

### An element size shv2 has no element TYPE for is refused, not silently truncated

<!-- test: error.create-element-size-with-no-element-type -->

`create`'s `elementSize` is read TWICE: the runtime stores it into `element_size@24` — the stride every
accessor moves by — and the front end picks the result's `Array` instance from it, because `get`/`set` are
typed off the ELEMENT. Those two readings must describe the same width. shv2 has exactly two trivial element
widths (byte-PACKED and a machine WORD), so a source that writes any other positive size makes them disagree,
and the disagreement is invisible: `create(4, elementSize: 4)` took the word instance, accepted
`set(0, value: 5000000000)` against `int`'s range, stored four bytes of it, and `get(0)` read back
**705032704**. Found in review of R4.4, which is the rung that introduced the element-size typing. The
refusal names the width and the two that work.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 4) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.set(0, value: 5000000000) otherwise return 3
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:37: Unsupported: `__ManagedMemory.create`'s `elementSize` is 4, and shv2 has no element TYPE of that width — a buffer must be byte-packed (1) or machine-word (8). Typing it as a word anyway would check `get`/`set` against a range the buffer's stride cannot hold, and store the value truncated.
```

### A buffer mark minted in one function does not reach another function's value

<!-- test: error.buffer-mark-does-not-leak-across-functions -->

The buffer surface rides the VALUE (`Parser.bufferSurfaceValues`), and `ValueId`s restart at 0 in every
function (`resetPerFunction` re-creates the minter), so the mark set is per-function and its shared empty
anchor is MODULE-level. If a mark were ever inserted into the anchor itself, it would name an id in an
unrelated function's SSA space — and the failure is an over-ACCEPTANCE, which no diagnostic reports.

MEASURED, by removing the copy-on-write detach: this exact program COMPILED AND RAN, `bytes.setByte(0, 67)`
writing a byte through a `String`'s own buffer because `mm`'s id in `makeBuf` collided with `bytes`'s id
here. The whole 2665-case suite was green over that removal, which is why this case exists — the guard's
two prose statements of the invariant had no test between them. The padding is load-bearing: it is what
aligns the two ids, and the case is worth nothing without it.
```maxon
function makeBuf() returns ExitCode
	let mm = try __ManagedMemory.create(4 + 0, 1) otherwise return 1
	return 0
end 'makeBuf'

function main() returns ExitCode
	let pad1 = 1
	let bytes = "ab".toByteArray()
	try bytes.setByte(0, 67) otherwise return 3
	return makeBuf()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:12: Unsupported: `Array` member 'setByte' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

### R4.6 — the buffer's `set` is bounded by CAPACITY, and the `Array`'s is not

⚖ **USER RULING, 2026-07-30: `__ManagedMemory`'s element and byte writes are bounded by CAPACITY, not by
length.** Three sources disagreed. The `setByte` line of the Documentation above (`panics if index >= length *
elementSize`) says LENGTH; `stdlib/File.maxon:117-127` behaves as though it did, doing `setLength(len+1)`
before `setByte(len, 0)` and commenting *"at the length boundary, so temporarily extend length to allow
setByte at that index"*; v1 says CAPACITY, in a comment that gives the reason
(`stdlib/Internals.maxon:3527-3530`, *"Byte writes are bounded by CAPACITY (the allocated region), NOT length
— mirroring `__managed_mem_set`"*); and the runnable oracle could arbitrate neither, crashing with `E9001` on
the discriminating program. **The ruling is CAPACITY. v1 is followed and the `setByte` documentation line above
is treated as stale — do not "fix" shv2 back toward it.**

⭐ **The ruling is what makes `setLength`'s OWN documented contract implementable.** That bullet says growing
"must NOT initialize the exposed slots itself: its callers … **stage the new elements FIRST and use
`setLength` to publish them**". Staging is only possible if a write may land in `[length, capacity)` — exactly
what a capacity bound permits and a length bound forbids. Nothing in either corpus tested that round trip; the
first case below is it.

<!-- test: set-stages-into-capacity-then-set-length-publishes -->

The round trip `setLength`'s contract names: four elements written while `length()` is still 0, then ONE
`setLength` publishing all four at once. Under a length bound the first `set` is refused (`0 >= 0`) and the
idiom has no spelling at all.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	if mm.length() != 0 'createPublishesNothing'
		return 2
	end 'createPublishesNothing'
	try mm.set(0, value: 10) otherwise return 3
	try mm.set(1, value: 20) otherwise return 3
	try mm.set(2, value: 30) otherwise return 3
	try mm.set(3, value: 40) otherwise return 3
	try mm.setLength(4) otherwise return 4
	let a = try mm.get(0) otherwise return 5
	let b = try mm.get(1) otherwise return 5
	let c = try mm.get(2) otherwise return 5
	let d = try mm.get(3) otherwise return 5
	return (a + b + c + d) as ExitCode
end 'main'
```
```exitcode
100
```

<!-- test: set-past-the-live-length-lands-and-reads-back-published -->

The same rule with a NON-zero length, so the staged slot is genuinely past a live range rather than past an
empty one: index 2 is written while the length is 2, and reads back once the length reaches 3.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.set(0, value: 1) otherwise return 3
	try mm.set(1, value: 2) otherwise return 3
	try mm.set(2, value: 39) otherwise return 4
	try mm.setLength(3) otherwise return 5
	let v = try mm.get(2) otherwise return 6
	return v as ExitCode
end 'main'
```
```exitcode
39
```

<!-- test: set-at-the-capacity-and-below-zero-is-refused -->

The bound is the capacity, so the LAST slot inside it takes a write and the first slot outside it does not.
The pairing is what makes this case discriminate: an `at capacity → throws` assertion on its own is satisfied
by a LENGTH bound too, and settles nothing (`managed-memory-error-variants`'s `setByte(4, …)` on a capacity-4
buffer is exactly that shape). The negative index is a separate compare — `StdCmpPred` is signed, so `-1 >= cap`
is FALSE and an at-or-over test alone reads it as in range.
```maxon
function main() returns ExitCode
	var seen = 0
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	let cap = mm.capacity()
	try mm.set(cap - 1, value: 7) otherwise return 2
	try mm.set(cap, value: 7) otherwise (e) 'atCapacity'
		match e 'k'
			indexOutOfBounds then seen = seen + 1
			default panic("set at the capacity must report indexOutOfBounds")
		end 'k'
	end 'atCapacity'
	try mm.set(cap + 1, value: 7) otherwise (e) 'pastCapacity'
		match e 'k'
			indexOutOfBounds then seen = seen + 1
			default panic("set past the capacity must report indexOutOfBounds")
		end 'k'
	end 'pastCapacity'
	try mm.set(-1, value: 7) otherwise (e) 'negativeIndex'
		match e 'k'
			indexOutOfBounds then seen = seen + 1
			default panic("set at a negative index must report indexOutOfBounds")
		end 'k'
	end 'negativeIndex'
	if seen == 3 'allThree'
		return 42
	end 'allThree'
	return 5
end 'main'
```
```exitcode
42
```

<!-- test: get-stops-at-the-live-length-where-set-does-not -->

The ASYMMETRY, pinned in one program so the two bounds cannot silently converge: index 1 accepts a write while
the length is 1 and refuses a read at the same instant, and the read starts working the moment `setLength`
publishes it. Reading an unpublished slot has nothing to see; writing one is the whole point.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(1) otherwise return 2
	try mm.set(1, value: 77) otherwise return 3
	let unpublished = try mm.get(1) otherwise 42
	if unpublished != 42 'readPastLength'
		return 4
	end 'readPastLength'
	try mm.setLength(2) otherwise return 5
	let published = try mm.get(1) otherwise return 6
	return published as ExitCode
end 'main'
```
```exitcode
77
```

<!-- test: error.staging-a-managed-element-into-unpublished-capacity -->

⚖ **USER RULING, 2026-07-30 (the SECOND one on this member): staging a MANAGED element is REFUSED — E3109.**
This case asserted the opposite until R4.6 review, and it was testing a capability that should not exist.

The capacity bound is what makes `[length, capacity)` writable, and that region carries **no ownership**:
`__managed_decref` destroys only `[0, length)`, `push`/`insert`/`append` store AT `length` without destroying the
occupant (before the ruling that slot was PROVABLY NULL), and a grow or a detach copies only the live bytes
and abandons the rest. So a staged managed element is owned by nobody until `setLength` publishes it — and by
nobody at all if one never does. MEASURED at exit **101** with the gate lifted, for a `String` element AND for
a `Slot` STRUCT element (a struct is boxed, so it leaks identically — which is why the rule is "managed", not
"String").

⭐ **REJECTED rather than fixed, and the reason is the invariant.** Making the staged slot owned puts a
destructor gate on every managed `push` — a hot path — and makes `[length, capacity)`-reads-ZERO conditional,
which five array operations rely on. Refusing costs neither and costs no capability: `[0, length)` is exactly
what the ARRAY surface's length-bounded `set` covers, and the message says so.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("first published string, long enough to require an allocation")
	xs.push("second published string, long enough to require an allocation")
	try xs.managed.set(2, value: "staged heap string number one, long enough to require an allocation") otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E3109: <fragment>:8:17: 'managed.set' cannot store an element of 'String': it is bounded by the CAPACITY, so it writes slots no length has published, and nothing owns a managed element staged there until 'setLength' publishes it. Use the array's own 'set(index, value:)', which is bounded by the length
```

<!-- test: error.staging-a-managed-struct-element-is-refused-the-same-way -->

⭐ **THE RULE IS "MANAGED", NOT "`String`" — and this is the case that says so.** A struct element is boxed on
the heap exactly as a `String` is, so it leaks exactly as one: MEASURED at exit **101** with the gate lifted,
staging a `Slot` past the live length. Without this case the refusal could be narrowed to text elements and
every other boxed element would silently regain the leak. It is the same relationship
`array-slots.md`'s `error.resize-struct-element` has with its `error.resize-string-element`.
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
	var xs = SlotArray.create()
	xs.push(Slot.create(7))
	xs.reserve(4)
	try xs.managed.set(1, value: Slot.create(9)) otherwise return 1
	return 0
end 'main'
```
```maxoncstderr
error E3109: <fragment>:18:17: 'managed.set' cannot store an element of 'Slot': it is bounded by the CAPACITY, so it writes slots no length has published, and nothing owns a managed element staged there until 'setLength' publishes it. Use the array's own 'set(index, value:)', which is bounded by the length
```

<!-- test: error.reserve-then-stage-without-publishing-is-refused -->

⭐⭐ **THE FIVE LINES THAT DECIDED THE RULING (coordinator, 2026-07-30).** Nothing here looks like a contract
violation — `reserve` then `set(0, …)` then return — and before R4.6 that `set` was simply refused, because
`__managed_set` was length-bounded. The capacity ruling is what made it reachable, which is what put it under
"a reachable leak a rung ENABLES is fixed or the causing construct REJECTED before merge". MEASURED at exit
**101** before the refusal landed. It is the shortest program in the family and the one to keep.
```maxon
typealias StringArray = Array with String

function main() returns ExitCode
	var a = StringArray.create()
	a.reserve(4)
	try a.managed.set(0, value: "staged") otherwise return 1
	return 42
end 'main'
```
```maxoncstderr
error E3109: <fragment>:7:16: 'managed.set' cannot store an element of 'String': it is bounded by the CAPACITY, so it writes slots no length has published, and nothing owns a managed element staged there until 'setLength' publishes it. Use the array's own 'set(index, value:)', which is bounded by the length
```

<!-- test: staging-a-trivial-element-twice-releases-nothing-and-still-publishes -->

⭐ **THE RULING'S MAIN LINE, KEPT ALIVE FOR THE ELEMENT KIND THAT STILL HAS IT.** The refusal above takes the
managed half of the staging story away; the TRIVIAL half is the whole reason the capacity bound was ruled, and
`__ManagedMemory.create` can only ever produce a trivial element, so this is the path the corpus actually
walks. It is the managed case's exact shape — stage three times into one slot, publish, shrink it away,
restage, publish again — which is what pins the destroy walk's per-slot NULL GUARD rather than v1's
`idx < length` gate: a shrink destroys AND zeroes, so the restage sees a null and releases nothing.
```maxon
function main() returns ExitCode
	var total = 0
	let mm = try __ManagedMemory.create(4, elementSize: 8) otherwise return 1
	try mm.setLength(1) otherwise return 2
	try mm.set(1, value: 5) otherwise return 3
	try mm.set(1, value: 6) otherwise return 4
	try mm.set(1, value: 7) otherwise return 5
	try mm.setLength(2) otherwise return 6
	total = total + (try mm.get(1) otherwise return 7)
	try mm.setLength(1) otherwise return 8
	try mm.set(1, value: 30) otherwise return 9
	try mm.setLength(2) otherwise return 10
	total = total + (try mm.get(1) otherwise return 11)
	return total as ExitCode
end 'main'
```
```exitcode
37
```

<!-- test: set-byte-past-the-live-length-is-allowed-within-capacity -->

⚖ The USER RULING of 2026-07-30 itself, pinned as behaviour rather than as prose: `setByte` at an offset past
the live length but inside the capacity is ALLOWED. This case is GREEN before the rung as well as after — the
ruling keeps R4.4's `setByte` exactly as it shipped — so it is a regression guard, not an unlock. It exists
because the Documentation section above still carries the stale LENGTH wording and a future reader will find it.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.setByte(3, value: 65) otherwise return 3
	try mm.setLength(4) otherwise return 4
	let v = try mm.byteAt(3) otherwise return 5
	return v as ExitCode
end 'main'
```
```exitcode
65
```

<!-- test: array-set-is-still-bounded-by-the-live-length -->

⭐ **THE NEGATIVE CONTROL, and the case that makes the six above trustworthy.** `Array.set` must NOT move:
`specs/arrays.md` and the whole array corpus are written against a length bound, and `__managed_set` is shared.
Without this case a change that made BOTH surfaces capacity-bounded would satisfy every other case here.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	arr.push(2)
	if arr.capacity() < 4 'needsASlotPastTheLength'
		return 1
	end 'needsASlotPastTheLength'
	try arr.set(2, value: 3) otherwise 'pastLength'
		return 42
	end 'pastLength'
	return 5
end 'main'
```
```exitcode
42
```

<!-- test: set-on-a-view-detaches-before-it-reads-a-capacity -->

⚠ **A VIEW CARRIES A NEGATIVE CAPACITY** (`BufferOwnership.ViewBufferCapacity` is exactly `-1`), so a capacity
bound read off one before the detach refuses EVERY index — `0 >= -1`. The detach republishes `capacity@16` as
the live length, so on a detached view the two bounds coincide: index 1 lands, index 2 does not. The parent is
read back to prove the write went to the view's own private buffer.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(11)
	arr.push(22)
	arr.push(33)
	let sub = try arr.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	try sub.managed.set(1, value: 99) otherwise return 1
	try sub.managed.set(2, value: 99) otherwise 'pastTheDetachedCapacity'
		let parent = try arr.get(1) otherwise panic("get: index 1 of a length-3 array")
		return (sub.managed.length() + parent) as ExitCode
	end 'pastTheDetachedCapacity'
	return 5
end 'main'
```
```exitcode
24
```

<!-- test: a-refused-set-does-not-leak-the-element-it-was-given -->

⭐⭐ **A SECOND BUG R4.6 FOUND AND FIXED, THIS ONE ON THE `Array` SURFACE (P1.7 slice 3a).** The parser MOVES
a managed element into `set` (`moveElementIntoArray`), so the callee owns it from the call onward and the
caller's scope-exit drop is suppressed. On the success path the array becomes the owner — and on the
out-of-range path nothing did, so the element simply leaked. MEASURED before the fix: the first `try` below
exited **101**, while the identical program over an `Array with Int` exited 0 (the control that says this is
the managed move-in, not the throw). R4.6 fixes it because `__managed_mem_set` would otherwise have been written
with the identical leak on its first day.

⚠ **IT ASSERTS ONLY THE ARRAY SURFACE, AND THE MISSING HALF IS NOT AN OMISSION.** It asserted both until R4.6
review; the buffer half is now a COMPILE error (E3109 — a managed element cannot reach `managed.set` at all),
which is a strictly stronger guarantee than "the refusal does not leak". The consequence is worth stating
where a sabotage-runner will look: `__managed_mem_set`'s own `emitDestroyRejectedElement` call is now unreachable
from any spelling the front end admits, so no test can redden it. It is kept deliberately — see
`buildManagedMemSet` — and this line is why breaking it is silent.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var refused = 0
	var xs = StrArray.create()
	xs.push("a published string, long enough to require an allocation")
	try xs.set(99, value: "a string the array setter refuses, long enough to allocate") otherwise 'arraySurface'
		refused = refused + 1
	end 'arraySurface'
	try xs.set(-1, value: "a string refused for a negative index, long enough to allocate") otherwise 'negativeIndex'
		refused = refused + 1
	end 'negativeIndex'
	if refused == 2 'both'
		return 42
	end 'both'
	return 5
end 'main'
```
```exitcode
42
```

<!-- test: set-byte-through-a-viewed-owner-detaches-first -->

⭐⭐ **A BUG R4.6 FOUND AND FIXED IN R4.4's WRITE GUARD.** `__managed_cow_detach` has TWO arms — a buffer that is
not this record's (`capacity < 0`), and one that IS but is being read by a view — and R4.4's guard called the
detach only under a hand-written copy of the FIRST arm (`emitBufferNotOwned`). So a `setByte` through the
OWNER of a viewed buffer wrote straight through the sharing. MEASURED before the fix: this program returned
**198**, the view reading back the owner's 99. The guard now calls `__managed_cow_detach` unconditionally and lets
it answer both arms, which is the gating rule `buildManagedCowDetach`'s own header states.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(11)
	arr.push(22)
	arr.push(33)
	let sub = try arr.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	try arr.managed.setByte(0, value: 99) otherwise panic("setByte: offset 0 of a length-3 buffer")
	let viewed = try sub.get(0) otherwise panic("get: index 0 of a length-2 view")
	let owner = try arr.get(0) otherwise panic("get: index 0 of a length-3 array")
	return (viewed + owner) as ExitCode
end 'main'
```
```exitcode
110
```

<!-- test: set-through-a-viewed-owner-detaches-first -->

⭐⭐ **THE SAME ARM-2 HOLE ON THE MEMBER R4.6 ADDED, WHICH THE `setByte` CASE ABOVE DOES NOT COVER (added in
review).** `__managed_mem_set` and `__managed_set_byte` reach `__managed_cow_detach` through the one
`emitBufferAccessGuard`, so it is tempting to read one case as covering both — it does not. MEASURED, by
re-applying R4.6's own sabotage (restore R4.4's conditional `capacity < 0` detach and rebuild): the suite went
**53 passed / 1 failed**, red on the `setByte` case ALONE, while this program — never committed — returned the
identical wrong **198**. One guard with two callers needs one case per caller, because the day the guard
sprouts a third parameter is the day the two callers stop taking the same path through it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(11)
	arr.push(22)
	arr.push(33)
	let sub = try arr.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-3 array")
	try arr.managed.set(0, value: 99) otherwise panic("set: index 0 of a length-3 buffer")
	let viewed = try sub.get(0) otherwise panic("get: index 0 of a length-2 view")
	let owner = try arr.get(0) otherwise panic("get: index 0 of a length-3 array")
	return (viewed + owner) as ExitCode
end 'main'
```
```exitcode
110
```

### R4.6 review — a managed element's bytes are a POINTER, so raw byte access is refused

⚖ **USER RULING, 2026-07-30.** `setByte` and `byteAt` address the buffer at BYTE granularity. A managed
element does not live there as data — it lives there as a POINTER to a heap allocation — so those two members
address the bytes of an ADDRESS. **This is not E3109's reason and the two are not collapsed:** that one is
about OWNERSHIP of a staged element, applies only past the published length, and has an exact replacement;
this one is about the element's REPRESENTATION, holds at EVERY offset, and has no replacement, because byte
access to a pointer is not something a correct program wants. One predicate, two reasons, two messages.

<!-- test: error.set-byte-on-a-managed-element-buffer-is-refused -->

MEASURED before the refusal: this program exited **101**. The write went into a live `String`'s pointer, after
which the element it named was unreachable and unreleasable.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("a published string, long enough to require an allocation")
	try xs.managed.setByte(0, value: 65) otherwise return 1
	return 42
end 'main'
```
```maxoncstderr
error E3110: <fragment>:7:17: 'managed.setByte' cannot address the bytes of an element of 'String': a managed element is stored as a POINTER, so those bytes are a heap ADDRESS, not data — reading them discloses one and writing them corrupts it. Raw byte access is for a buffer of trivial elements
```

<!-- test: error.byte-at-on-a-managed-element-buffer-is-refused -->

⭐⭐ **THE HALF THAT WOULD HAVE SURVIVED A FIX TO THE WRITER ALONE, WHICH IS WHY IT HAS ITS OWN CASE.** `byteAt`
returns a value: it corrupts nothing, leaks nothing, and raises no error, so no gate in this project could see
it. MEASURED before the refusal: offsets 0 and 1 of a live `String` pointer both returned **NONZERO** — the
program was handed fragments of a heap address as though they were data. A silent wrong answer, and an
information disclosure. It was found only because the writer's fix prompted the question "what does its dual
do?", and it is pinned separately so a future narrowing of the rule to writers cannot pass.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("a published string, long enough to require an allocation")
	let b = try xs.managed.byteAt(0) otherwise return 1
	return b as ExitCode
end 'main'
```
```maxoncstderr
error E3110: <fragment>:7:25: 'managed.byteAt' cannot address the bytes of an element of 'String': a managed element is stored as a POINTER, so those bytes are a heap ADDRESS, not data — reading them discloses one and writing them corrupts it. Raw byte access is for a buffer of trivial elements
```

<!-- test: raw-byte-access-still-works-for-every-trivial-element-kind -->

⭐ **THE FALSE-REJECT GUARD, and the case that makes the two refusals above safe to keep.** Byte access is the
buffer surface's whole reason for existing, and refusing it for the wrong receiver would be a far larger
regression than the bug. Four trivial receivers in one program: a 1-byte-element `__ManagedMemory` (the
`create`-then-`setByte`-then-`setLength` idiom, which is how `stdlib/File.maxon` writes its NUL terminator at
the length boundary), an 8-byte WORD buffer, an `Array with Int` reached through `.managed`, and an
`.rdata`-backed byte-string literal. `__ManagedMemory.create` can only ever yield a trivial element, so this
is the path the corpus actually walks and none of it may move.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var total = 0
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	try mm.setByte(3, value: 65) otherwise return 3
	try mm.setLength(4) otherwise return 4
	total = total + (try mm.byteAt(3) otherwise return 5)
	let words = try __ManagedMemory.create(2, elementSize: 8) otherwise return 6
	try words.setByte(0, value: 9) otherwise return 7
	try words.setLength(1) otherwise return 8
	total = total + (try words.byteAt(0) otherwise return 9)
	var xs = IntArray.create()
	xs.push(7)
	try xs.managed.setByte(0, value: 3) otherwise return 10
	total = total + (try xs.managed.byteAt(0) otherwise return 11)
	var s = b"hello"
	total = total + (try s.managed.byteAt(1) otherwise return 12)
	return (total - 136) as ExitCode
end 'main'
```
```exitcode
42
```

### A throwing buffer read written without `try` names the buffer method

<!-- test: error.byte-at-without-try -->

⭐ **THE SAME E3057 RULE, THE `__ManagedMemory` FAMILY'S NOUN (D12).** `byteAt` throws
`__ManagedMemoryError.indexOutOfBounds`, so a bare call drops the flag and hands back a dummy 0 — a wrong
answer. The family reaches the diagnostic THROUGH `isThrowingManagedMemoryRuntimeCallee`, which is why it used to
inherit "throwing array accessor" although the receiver is a buffer and the method is the buffer's own.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	_ = arr.managed.byteAt(100)
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/managed-memory-methods/error.byte-at-without-try.test:8:18: throwing function requires try: 'byteAt'
```

### D11b — the buffer surface IS the roster its refusal names, in both directions

⚖ **USER RULING, 2026-07-31: the legitimate surface is EXACTLY what the roster names.** Before D11b the
roster was a description that guarded nothing: eleven of the `Array` arms were reachable on a buffer because
nothing gated them, so `mm.push(7)` and `try mm.remove(0)` **compiled, linked and ran** while the very
message a reader is handed for a typo denied `remove` outright. The message is now the SPECIFICATION — one
list (`bufferSurfaceMemberNames`) which the refusal renders and the dispatch consults — so a value the
compiler KNOWS is a buffer cannot be handed a member the message denies, and the fall-through past the arms is
a compiler PANIC rather than a second opinion about what exists.

⭐⭐ **A2j — AND A *DECLARED* SPELLING IS A FOURTH PRODUCER, WHICH THE PRODUCER SWEEP BELOW STRUCTURALLY
COULD NOT SEE.** D11b's review measured the gap and this rung closed it. `__ManagedMemory` is a generic ALIAS
of `Array with Byte` (`ProgramSignatures.registerManagedFileType`), so a value bound by a **parameter**, a
**struct field** or a **return type** spelled `__ManagedMemory` carried no buffer mark and took the `Array`
surface — the roster exactly INVERTED. All three compiled, linked and RAN:

```text
function abuse(m __ManagedMemory) returns int      -- push/reserve/resize/insert/pop/first/last/
    m.push(7) … m.count()                          -- remove/clone/isEmpty/count all ACCEPTED, exit 13
function useBuffer(m __ManagedMemory) returns int  -- while the roster's own first member is REFUSED:
    return m.length()                              -- "`Array` member 'length' — … arrive later"
function make() returns __ManagedMemory            -- m.count() ACCEPTED, exit 5
type Holder  var buf as __ManagedMemory            -- h.buf.count() ACCEPTED, exit 5
```

⚖ **THE RULING IS THAT THE SPELLING WINS: a value whose DECLARED TYPE is written `__ManagedMemory` denotes
the BUFFER and gets exactly the buffer roster.** The alternative — a declared spelling deliberately exposing
the `Array` — would make the roster message false at three spellings, which is the whole defect. Each of the
three now carries its *pre-erasure spelling* from the declaration site to the site that BINDS the value, and
marks it through the SAME `Parser.markBufferSurface`: no second surface mechanism, and `dispatchArrayMethod`
needed no change at all. Type RESOLUTION is untouched — `__ManagedMemory` still resolves to `Array with
Byte`, which is what keeps a `ByteArray` argument assignable to a `__ManagedMemory` parameter, as every case
in the section at the end of this file does.

⚠ **WHY THE ENUMERATION MISSED IT, and it generalises past this type**: the sweep below enumerated the
PRODUCERS OF A BUFFER VALUE, and a DECLARED TYPE is an entrance that a value-producer sweep cannot reach — it
mints no value of its own, it *annotates* one. No live case in `specs-shv2/` wrote any of the three (the only
one was a `disabled-test` in `interface-conformance.md`), so an enumeration of *what the corpus calls* was
bounding *what a program may write*. The six cases under "A2j" at the end of this file are the fix for that
too: each of the three spellings now has a case in both directions.

⚠ **THE MEASUREMENT THE RULING TURNED ON, and it is why the roster gained nothing.** Every buffer-surface
call the corpus makes was enumerated from the four producers of a buffer VALUE (`__ManagedMemory.create`, a
`slice` through the surface, `__ManagedDirectory.filename`/`currentPath`, and the `.managed` field) rather
than probed: `length`, `capacity`, `get`, `set`, `setLength`, `setByte`, `byteAt`, `grow`, `append`,
`slice`, `clear` — the roster exactly. The one off-roster member any `/specs` case reaches is
`elementSize` (`ranged-int-bit-packing.md`, which needs ranged-int bit packing shv2 does not have), and
`stdlib/Array.maxon` additionally calls `remove`/`swap`/`shiftRight`/`elementSize`/`toCString`/
`makeCharFromBytes` — a file no whitelisted module imports and which shv2 cannot yet compile at all. Those
arrive with the rung that gives the buffer those members for real, and the roster is what will say so.

<!-- test: buffer-surface-serves-every-member-its-roster-names -->

⭐⭐ **THE FALSE-REJECT GUARD — every one of the eleven roster members, in one program, returning a
computed value.** A new refusal hides its false rejects one nesting level below where it is tested, so the
acceptance criterion for D11b is not "the refusal fires" but "nothing legitimate stopped compiling": if the
gate ever loses a name, or the roster and the arms drift apart, this case is the first thing that reddens.
Two receivers because two of the members are element-width-bound: the word buffer takes `grow`'s exact
capacity (a byte buffer's slab slack can exceed the requested count, which would make `grow` a shrink and
throw), and the byte buffer is where a `setByte`/`byteAt` pair addresses a whole element.
```maxon
function main() returns ExitCode
	var total = 0
	let words = try __ManagedMemory.create(2, elementSize: 8) otherwise return 1
	try words.setLength(2) otherwise return 2
	try words.set(0, value: 11) otherwise return 3
	try words.set(1, value: 22) otherwise return 4
	try words.grow(8) otherwise return 5
	if words.capacity() != 8 'grownExactly'
		return 6
	end 'grownExactly'
	total = total + (try words.get(0) otherwise return 7)
	let part = try words.slice(1, 2) otherwise return 8
	total = total + (try part.get(0) otherwise return 9)
	total = total + part.length()
	let more = try __ManagedMemory.create(1, elementSize: 8) otherwise return 10
	try more.setLength(1) otherwise return 11
	try more.set(0, value: 33) otherwise return 12
	words.append(more)
	total = total + words.length()
	words.clear()
	total = total + words.length()
	let bytes = try __ManagedMemory.create(4, elementSize: 1) otherwise return 14
	try bytes.setLength(2) otherwise return 15
	try bytes.setByte(1, value: 5) otherwise return 16
	total = total + (try bytes.byteAt(1) otherwise return 17)
	return total as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: error.try-on-the-buffers-append -->

⚖ **`append` IS THE ONE BUFFER MUTATOR THAT CANNOT FAIL, SO A `try` ON IT IS E3055 — USER RULING,
2026-08-07.** It reads as an exception beside `set`/`setLength`/`setByte`/`grow`/`slice`, which all throw, and
it is not: those five take an INDEX or a CAPACITY the source wrote and must refuse the ones the record cannot
serve, while `append` is handed another record and asks the growth policy for whatever capacity that implies.
Both references agree — the bootstrap registers it as the only `__ManagedMemory` mutator with no `throwsType`
(`maxon-sharp/Compiler/2-Parser.cs:1670`) and v1's `__managed_mem_append` says *"Non-throwing"* in its header
and swallows the grow's error (`stdlib/Internals.maxon:3743-3766`) — and `stdlib/String.maxon:414` declares a
NON-throwing `append` whose whole body is a bare `managed.append(other.managed)`, which is unwritable under
any other answer.

⚠ **THE `try a.append(b) otherwise …` IN `specs/managed-memory-builtin.md` IS NOT EVIDENCE AGAINST IT**, and
was read as such for a rung. That file is `status: selfhosted`: v1 merely TOLERATES a redundant `try` on a
builtin where shv2 refuses one, so the line records v1's permissiveness rather than the callee's contract.
```maxon
function main() returns ExitCode
	let a = try __ManagedMemory.create(2, elementSize: 8) otherwise return 1
	let b = try __ManagedMemory.create(2, elementSize: 8) otherwise return 2
	try a.append(b) otherwise return 3
	return 0
end 'main'
```
```maxoncstderr
error E3055: <fragment>:5:2: try requires a throwing function: this builtin call cannot fail
```

<!-- test: buffer-append-deep-clones-a-managed-element -->

⭐⭐ **THE BUFFER'S `append` AND THE `Array`'s ARE ONE OPERATION, AND WHILE THEY WERE TWO THIS PROGRAM
SEGFAULTED.** The buffer had its own callee (`__managed_mem_append`) whose only distinguishing property was a
throwing-ness the ruling above removed — and that callee byte-blitted the receiver's elements whatever they
were, so appending through the `.managed` surface of an `Array with String` duplicated every heap pointer and
double-freed it at teardown. The `Array` spelling had always picked its emission by element kind; folding the
two onto that one door is what makes the surfaces agree. MEASURED before the fold: correct output, then
SIGSEGV on the way out.

⚠ The receiver keeps its own copies, which is the deep clone's whole point: `b` is dropped at its last use
here and `a`'s second element must survive it.
```maxon
typealias Strings = Array with String

function main() returns ExitCode
	var a = Strings.create()
	a.push("alpha is long enough to be heap allocated")
	var b = Strings.create()
	b.push("beta is also long enough to be heap allocated")
	a.managed.append(b.managed)
	if a.count() != 2 'badCount'
		return 90
	end 'badCount'
	let second = try a.get(1) otherwise return 91
	print("{second}\n")
	return 0
end 'main'
```
```stdout
beta is also long enough to be heap allocated
```
```exitcode
0
```

<!-- test: error.buffer-has-no-push -->

⭐ **THE PROGRAM THE D11 REVIEW RAN: it compiled, linked and exited 0.** `push` is an `Array` method and
the buffer has no member of that name — the two grow differently (`__managed_push` raises the capacity by the
doubling policy and publishes a length; the buffer stages into capacity and publishes with `setLength`), so
accepting it was not a convenience, it was a second length policy on a surface whose whole contract is that
the author publishes the length.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	mm.push(7)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:5: Unsupported: `__ManagedMemory` member 'push' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-push-in-value-position -->

⭐⭐ **THE VALUE POSITION IS THE D11 SHAPE ONE SURFACE OVER, AND IT IS WHY THIS GATE MUST PRECEDE THE
ARMS.** `let x = mm.push(7)` used to reach `push`'s own `requireVoidMethodIsStatement` and answer
`E2004: Function 'push' does not return a value` — a claim that the buffer HAS a void `push`, which is
exactly the false assertion D11 removed from the unknown-`Array`-method path. A refusal about existence has
to be asked before any refusal about how the result is used.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.push(7)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'push' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: the-buffer-does-have-remove -->

⭐⭐ **THE SECOND PROGRAM THE D11 REVIEW RAN, AND THE ONE THAT FLIPPED.** It was
`error.buffer-has-no-remove`: the roster said `remove` "is not built" while the arm accepted it and emitted
the `Array`'s length-bounded `__managed_remove`, so D11b made both halves say ABSENT. The Array-retirement rung
that BUILDS the member makes both halves say PRESENT instead — the arm is the same `__managed_remove` it always
was (same `length` bound, same tail slide, same erase of the vacated slot), and what changed is that the
roster now claims it.

⚠ The program below is the D11 review's, extended by the two lines a `remove` needs: `create(4, …)` RESERVES
four slots and leaves the length at 0 (see `create-and-length`), so the buffer has to publish a length and
write an element before there is anything for index 0 to name.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(3) otherwise return 2
	try mm.setByte(0, 7) otherwise return 3
	let gone = try mm.remove(0) otherwise return 4
	return gone + mm.length()
end 'main'
```
```exitcode
9
```

<!-- test: error.buffer-has-no-count -->

⭐⭐ **THE FOLDED NAME IS THE SHARPEST CASE IN THIS BLOCK: `length` IS the buffer's spelling and `count` is
the `Array`'s, and one arm serves both.** `__ManagedMemory.length()` is folded onto `Array.count()` because
the two read the same slot — but folding the EMISSION must not fold the SURFACE, or the buffer answers to a
name the reference never gave it and the roster's own first entry becomes decorative.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.count()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-appendMemory -->

⭐⭐ **`count`'s ARGUMENT, ONE MEMBER OVER — AND THE DIRECTION THAT NEEDED ITS OWN CASE.** `appendMemory` is
declared on `Array` (`stdlib/Array.maxon:272`) and NOWHERE else: not in the corpus, not in v1's
`__ManagedMemory` member table (`LowerMaxonToStd.maxon:1618-1660`), not in the bootstrap's
(`2-Parser.cs:1470-1512`), and no corpus caller writes `mm.appendMemory(…)`. The envelope collapse makes the
two surfaces ONE record and the `Array` spelling therefore folds onto the buffer's `append` EMISSION — which
is exactly the fold `count` proves must not reach the SURFACE. Served here it cost nothing and refused
nothing, which is the whole hazard: a buffer advertising a member the language does not give it, with no
program able to notice.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let piece = try __ManagedMemory.create(2, elementSize: 1) otherwise return 2
	try mm.appendMemory(piece) otherwise return 3
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:9: Unsupported: `__ManagedMemory` member 'appendMemory' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-managed-field -->

⭐ **`.managed` IS THE `Array`'s FIELD, SO A BUFFER DOES NOT HAVE IT — AND THE CHAIN IS WHAT MADE THAT
REACHABLE.** The field is an identity that re-enters this dispatch with the buffer surface already set, so
`mm.managed.setLength(1)` was a second door onto the buffer's own mutators through a member the buffer does
not have. It is refused at `managed`, where the untruth is.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.managed.setLength(1) otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:9: Unsupported: `__ManagedMemory` member 'managed' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: buffer-reports-its-element-size -->

⚠ **THIS CASE USED TO BE `error.buffer-has-no-element-size`, AND IT WAS THE CONTROL FOR A HALF-SENTENCE
THAT NO LONGER EXISTS.** It refused `elementSize` and existed to prove the roster's *"are not built"*
clause stayed reachable for a name the references really do provide. Sub-byte bit-packing built the
member — the packed stride is read through exactly this door — and the same change deleted the absent
clause outright, because a hand-written list of what is MISSING rots the moment anyone builds one of its
entries (this one was quoted verbatim by 21 expectations when it went).

So it is **converted, not deleted**: the door is the same, the assertion is inverted, and what it now
pins is that a directly-created buffer reports the stride it was created with — the byte-strided answer,
where `ranged-int-bit-packing` pins the negative packed widths through `Array.managed`.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	return mm.elementSize() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.buffer-has-no-pop -->

The move-out accessors are the `Array`'s alone: `pop` and `remove` shorten a LENGTH the array owns, where a
buffer's length is the author's to publish. One case per name, because each is one arm and a gate that lost
a single name would otherwise be caught by nothing.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.pop() otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:17: Unsupported: `__ManagedMemory` member 'pop' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-first -->
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.first() otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:17: Unsupported: `__ManagedMemory` member 'first' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-last -->
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = try mm.last() otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:17: Unsupported: `__ManagedMemory` member 'last' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-insert -->

⭐⭐ **THE SECOND DIAGNOSTIC THAT ASSERTED A BUFFER MEMBER INTO EXISTENCE, and it is why the `try` here is
deliberate.** `insert` is a NON-throwing `Array` method, so `try mm.insert(…)` used to answer
`E3055: try requires a throwing function: this builtin call cannot fail` — true of `Array.insert` and a
claim about a member the buffer has never had. Two refusals, two different invented properties (D11's was
"it is void", this one's is "it cannot fail"), one cause: a rule about HOW a member may be used, asked
before anything established that it exists.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.insert(0, value: 1) otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:9: Unsupported: `__ManagedMemory` member 'insert' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-reserve -->

⚠ `reserve` and `grow` are the pair most easily mistaken for one another, and refusing `reserve` is what
keeps them distinguishable: `grow` sets the capacity to EXACTLY what it is asked for and throws when asked
to lower it, `reserve` raises it by the doubling policy and never complains. A buffer that answered to both
would have two capacity policies and no way for an author to say which one they meant.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	mm.reserve(8)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:5: Unsupported: `__ManagedMemory` member 'reserve' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-resize -->

⚠ And `resize`/`setLength` are the other such pair, already stated one block up: `resize` GROWS the
capacity to fit, `setLength` must REFUSE a length above it. `stdlib/Array.maxon` builds `resize` out of
`reserve` THEN `setLength` for exactly that reason, which is only expressible while the buffer has the
second and not the first.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	mm.resize(2)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:5: Unsupported: `__ManagedMemory` member 'resize' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-is-empty -->
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.isEmpty()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'isEmpty' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-has-no-clone -->

⚠ `slice` is the buffer's copy, and it is on the roster — so refusing `clone` takes nothing away. What it
prevents is a SECOND owned-copy door whose bound is the whole receiver, on a surface where the interesting
copy is always a range.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.clone()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'clone' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.buffer-of-a-slice-has-no-array-members-either -->

⭐⭐ **ONE NESTING LEVEL DOWN — a buffer the SOURCE never named, reached as the result of a `slice` through
the surface.** The mark rides the VALUE, so the slice of a buffer is a buffer, and its surface has to be
the same one its parent has: a gate that read the receiver's SPELLING rather than its provenance would let
every `Array` member back in through one hop.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(4) otherwise return 2
	let part = try mm.slice(0, 2) otherwise return 3
	let x = part.count()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:15: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: array-members-still-work-through-a-managed-field-hop -->

⭐ **THE OTHER HALF OF THE NESTING GUARD: the surface must not leak the OTHER way.** `.managed` sets the
buffer surface for the ONE hop the source wrote it on, so the `Array` around it keeps every `Array` member
— including on the same binding, in the same statement sequence, before and after a buffer call. If the
mark ever outlived its hop, this program's `push`/`count`/`pop` would be refused with the buffer's roster.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	try arr.managed.set(0, value: 12) otherwise return 1
	arr.push(30)
	let popped = try arr.pop() otherwise return 2
	let head = try arr.first() otherwise return 3
	return (arr.count() + popped + head) as ExitCode
end 'main'
```
```exitcode
44
```


### A2j — a value DECLARED `__ManagedMemory` denotes the BUFFER, at all three spellings

⚖ **USER RULING, 2026-07-31 — the ruling of D11b applied to the spelling that NAMES the buffer.** The surface
used to follow a value's PROVENANCE alone, and `__ManagedMemory` is registered as a generic ALIAS of `Array
with Byte`, so the spelling was gone before any `MaxonType` existed: a parameter, a return type or a struct
field written `__ManagedMemory` bound a value indistinguishable from a `ByteArray` and got the `Array`
surface — the roster exactly inverted, with `count()` accepted and `length()` refused.

Six cases, three spellings × two directions. **The refusal direction alone would be worth little**: a new
refusal hides its false rejects one level below where it is tested, so each spelling also has a case that
COMPUTES a value through a roster member and asserts the number. The receiver in every one is a plain
`"hello".toByteArray()` — an ordinary `Array with Byte` — which is the point: nothing about the VALUE says
buffer, and the declaration is doing all the work.

<!-- test: error.declared-parameter-has-the-buffer-surface -->

`count` is an `Array` member and not a buffer one, so a parameter declared `__ManagedMemory` is refused it —
and refused it with the BUFFER's roster, which is the half that says the message answers for the type it is
shown about. This program compiled, linked and ran (exit 13) before A2j.
```maxon
function abuse(m __ManagedMemory) returns int
	let n = m.count()
	let e = m.capacity()
	return n + e
end 'abuse'

function main() returns ExitCode
	let bytes = "hello".toByteArray()
	return abuse(bytes) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:3:12: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: declared-parameter-serves-the-roster -->

⭐ **THE FALSE-REJECT HALF, and it is the one that was WRONG before rather than merely permissive**:
`length()` is the buffer roster's FIRST member, and a declared parameter was refused it as an unknown `Array`
method. It now answers, through an argument that is an ordinary byte array at the call site.
```maxon
function shown(m __ManagedMemory) returns int
	return m.length()
end 'shown'

function main() returns ExitCode
	let bytes = "hello".toByteArray()
	return shown(bytes) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.declared-return-type-has-the-buffer-surface -->

The RETURN spelling, refused the same member for the same reason. It is the one of the three that cannot be
answered from the file the call is in — the callee may be declared anywhere — so the fact travels on the
whole-program declaration index (`ProgramSignatures.bufferSurfaceReturns`), keyed and hashed beside the
return type it describes.
```maxon
function makeBuf() returns __ManagedMemory
	return "hello".toByteArray()
end 'makeBuf'

function main() returns ExitCode
	let m = makeBuf()
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:11: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: declared-return-type-serves-the-roster -->

The return spelling's false-reject half. `slice` is on BOTH rosters, so it is deliberately taken through the
buffer's own bounds and then measured with `length()`, which is on neither the `Array`'s nor reachable
before: the whole chain would have been refused at `length` a moment ago.
```maxon
function makeBuf() returns __ManagedMemory
	return "hello".toByteArray()
end 'makeBuf'

function main() returns ExitCode
	let m = makeBuf()
	let part = try m.slice(1, 4) otherwise return 1
	return (m.length() + part.length()) as ExitCode
end 'main'
```
```exitcode
8
```

<!-- test: error.declared-field-has-the-buffer-surface -->

The FIELD spelling. The bit rides `StructLayout` beside the field's declared type, in the column the SWEEP
records — the whole-program layout every read door consults — and is read at the one site a field read mints
its value, so `h.buf`, `self.buf` and a bare `buf` inside a method all answer alike.
```maxon
type Holder
	export var buf as __ManagedMemory

	export static function create(buf __ManagedMemory) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return h.buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:15: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: declared-field-serves-the-roster -->

The field spelling's false-reject half, and it reaches the buffer surface through TWO different doors on one
layout: `h.buf` from outside the type, and a bare `self.buf` from inside a method. Both mint their value at
the same place, which is why one column answers for both.
```maxon
type Holder
	export var buf as __ManagedMemory

	export static function create(buf __ManagedMemory) returns Self
		return Self{buf: buf}
	end 'create'

	export function size() returns int
		return self.buf.length()
	end 'size'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return (h.buf.length() + h.size()) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: error.a-buffer-returning-function-cannot-be-overloaded -->

⭐⭐ **THE THIRD FACT THE DECLARATION SWEEP PUBLISHES PER BARE NAME (found reviewing A2j).** The sweep is
keyed by the name the source WROTE, so an overload set leaves one entry — and the return spelling decides
which roster a call's result carries. The mark is made when the call is PARSED and `resolveOverloadedCalls`
rebinds the callee a whole pass later, so nothing downstream can repair a surface taken from the wrong
member. **MEASURED before this refusal existed, on this exact program: with the buffer member written FIRST,
`make().length()` was refused — the member that genuinely returns the buffer, denied the buffer's roster;
with it written LAST, `make(1).count()` was refused instead — the member that returns an `Array`, handed the
buffer's.** A wrong surface either way, decided by declaration order. It joins the consume bits and the
`throws` clause under `requireOverloadableName`, refused with the same E2015 and for the same reason: the
cure is per-member facts in the sweep, and until then a refusal beats a silent wrong answer. A set with only
ONE declaration is untouched — that is the whole corpus, and the cases above.

⚠ **A2m widened the refusal from `returns` to NAMES, and the sentence with it.** A tuple slot and an array
element name the buffer through the same one-entry-per-bare-name sweep, are chosen at the same moment, and
are just as unrepairable — see the case below, which was ACCEPTED with a silently wrong surface before A2m.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function make() returns __ManagedMemory
	return "hello".toByteArray()
end 'make'

function make(n Int) returns ByteArray
	return "abc".toByteArray()
end 'make'

function main() returns ExitCode
	let a = make()
	return a.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: overloading 'make' — one of its declarations NAMES `__ManagedMemory` in its return type, and the whole-program declaration sweep publishes that SPELLING under the name the source wrote, so a call to this name cannot be told whether its result carries the buffer's member roster or the `Array`'s. The surface is chosen when the call is PARSED and the overload is resolved a whole pass later, so nothing downstream can repair it. Give the overloads distinct names
```

### A2k — the `Array` roster is DERIVED from the arms it describes

<!-- test: error.array-roster-names-managed-and-not-create -->

⭐⭐ **THE CASE WHOSE PURPOSE IS THE LIST ITSELF**, so a future edit to `arraySurfaceMemberNames` has a
test that speaks for it rather than only goldens that happen to quote it. Until A2k the `Array` refusal was a
HAND-WRITTEN literal, and it was false in both directions at once: it named **`create`**, which
`dispatchArrayMethod` has never served as a member, and it omitted **`managed`**, which that dispatch does
serve. It is now joined from the very constants the arms match on — so it names `managed`, does not name
`create` among the members, and says separately where `create` actually lives — and the fall-through past the
arms is a compiler PANIC naming the list, verified red by pushing a name onto the roster with no arm behind
it.

⭐⭐ **THE PROBE MOVED FROM `create` TO AN UNKNOWN MEMBER WHEN `stdlib/Array.maxon` WAS LISTED, AND THE
ASSERTION DID NOT.** `create` used to reach this roster because nothing else claimed it; the corpus declares
`export static function create() returns Self` (`stdlib/Array.maxon:125`), so `memberBelongsToTheCorpus` now
answers first and the name never reaches `requireSurfaceMember` at all. What this case exists to pin — that
the sentence is JOINED from the roster constants, names `managed`, and omits `create` — is unchanged and is
still read straight off the message below. An unknown member is in fact the BETTER vehicle for it: `create`
could only ever probe the roster while no declaration claimed it, so the old case was one listing away from
silently testing a different door, which is exactly what happened.

⚠ **`create`'s own answer is pinned by the case directly below**, so nothing was lost by moving this probe —
the roster claim and the static-through-instance claim are two facts and now have one case each.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	return arr.frobnicate() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:13: Unsupported: `Array` member 'frobnicate' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.a-static-through-an-instance-answers-the-same-for-array-and-for-a-user-type -->

⭐⭐ **`Array` AND A USER `type` GIVE THE IDENTICAL ANSWER TO A STATIC REACHED THROUGH AN INSTANCE, AND THIS
CASE EXISTS BECAUSE THEY WERE BELIEVED NOT TO.** Once `stdlib/Array.maxon` is listed, `arr.create()` is an
ordinary call to a corpus `static` and takes the ordinary door: the receiver is prepended like any other
member call's and the arity check refuses it. MEASURED, both spellings, side by side:

| program | answer |
|---|---|
| `arr.create()` on an `Array with Int` | `E3036 'Array.create' expects 0 argument(s) but 1 were provided` |
| `p.make()` on a user `type P` with a 0-arg static | `E3036 'P.make' expects 0 argument(s) but 1 were provided` |
| `p.make(9)` on a 1-arg static | `E3036 'P.make' expects 1 argument(s) but 2 were provided` |

⛔⛔ **THE APPARENT DISAGREEMENT THAT MOTIVATED THIS CASE WAS TWO DIFFERENT PROGRAMS, AND IT IS WORTH KEEPING
BECAUSE THE TRAP IS GENERAL.** `Array` was reported as *resolving* `arr.create()` and failing later at
`E3009`, against a user type's `E3036` — one wrong, make them agree. They already agreed: the `E3009` comes
from the `as ExitCode` in the ARRAY probe, which the user-type probe did not have. `return p.make() as
ExitCode` is `E3009: Cannot cast from struct to int` too, on the nose. A cast error is a `ParseError`, which
stops the file before the pipeline and discards its artifact diagnostics, so it PRE-EMPTS the semantic arity
check — the comparison was measuring the cast, not the dispatch.

⚠ **NEITHER ANSWER IS IDEAL AND THAT IS A SEPARATE, DOCUMENTED RULING.** E3036 blames an argument COUNT for
what is really "a `static` has no receiver"; `ErrorCodeRegistry.maxon`'s `E3116` entry reasons about exactly
this and deliberately scopes the honest refusal to the type-parameter case, naming E3036 as what a CONCRETE
receiver gets. This case pins the AGREEMENT, not the wording — so a rung that improves the wording moves one
expectation here and is told immediately if it moves only one of the two.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

type P
	export var x as Int

	static function make() returns Self
		return Self{x: 1}
	end 'make'
end 'P'

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1)
	_ = arr.create()
	var p = P.make()
	_ = p.make()
	return 0
end 'main'
```
```maxoncstderr
error E3036: <fragment>:16:10: 'Array.create' expects 0 argument(s) but 1 were provided
error E3036: <fragment>:18:8: 'P.make' expects 0 argument(s) but 1 were provided
```

### A2m — the buffer surface rides a SLOT, so a tuple element and an array element carry it

⚖ **USER RULING, 2026-07-31 (D11b), reached one container deeper.** A2j closed the three WHOLE-VALUE
declared spellings. A slot's is the same fact and the same mechanism — `__ManagedMemory` is a generic ALIAS
of `Array with Byte`, so `(__ManagedMemory, Int)` and `(ByteArray, Int)` intern to ONE tuple type sharing ONE
`StructLayout`, and `Array with __ManagedMemory` and `Array with ByteArray` share ONE `GenericInstanceId`.
Neither slot's spelling survives into any `MaxonType`, so it must ride the VALUE.

**MEASURED before A2m, on the programs below**: `p.0.length()` was refused as an unknown `Array` method
while `p.0.count()` compiled and RAN — the roster exactly inverted, at five entrances (a tuple return, a
tuple destructuring, a tuple parameter, an array element, an array element behind a struct field).

**The over-acceptance controls are the load-bearing half.** The layout and the instance are SHARED, so the
one way to get this wrong is to hand the buffer surface to `(ByteArray, Int)` and `Array with ByteArray` as
well — a wrong ACCEPTANCE no diagnostic reports, decided by whichever spelling interned first. The four
`…-keeps-the-array-surface` / `…-still-serves-count` cases are what prove the per-value carriers were not
quietly written onto the shared layout column.

<!-- test: tuple-element-serves-the-roster -->

The tuple RETURN spelling's false-reject half. `p.0` is rewritten to a positional field token and reaches the
same `emitFieldLoad` a struct field does, so the slot mask minted on the call result is what the read
consults. The element is an ordinary `"hello".toByteArray()` — nothing about the VALUE says buffer.
```maxon
typealias Int = int(i64.min to i64.max)

function pair() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let p = pair()
	return p.0.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: tuple-destructuring-serves-the-roster -->

`let (m, n) = pair()` binds each name to a field load off the hidden temp, so a destructured name inherits
the bit for ITS position and nothing else. This entrance was not on the defect row; it was found by walking
what reads a tuple.
```maxon
typealias Int = int(i64.min to i64.max)

function pair() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let (m, _) = pair()
	return m.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: tuple-parameter-serves-the-roster -->

The tuple PARAMETER spelling. The mask is read off the annotation's TOKENS at the same moment A2j's
whole-value bit is, and travels the same parse-local column into `bindParameters`.
```maxon
typealias Int = int(i64.min to i64.max)

function shown(t (__ManagedMemory, Int)) returns Int
	return t.0.length()
end 'shown'

function main() returns ExitCode
	return shown(("hello".toByteArray(), 7)) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: tuple-through-a-struct-field-serves-the-roster -->

A struct FIELD whose declared type is a tuple — the composed shape, which falls out of the two mechanisms
rather than needing a third: the field's declared surface carries the mask, `h.p` mints it onto the loaded
tuple, and `.0` reads it there.
```maxon
typealias Int = int(i64.min to i64.max)

type Holder
	export var p as (__ManagedMemory, Int)

	export static function create(p (__ManagedMemory, Int)) returns Self
		return Self{p: p}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create(("hello".toByteArray(), 7))
	return h.p.0.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: array-element-serves-the-roster -->

The generic ARRAY ELEMENT spelling. `Array with T` is writable only as a `typealias` RHS, so the element's
spelling is recorded per ALIAS NAME at the one place a generic instantiation's arguments are read, and a
value whose declared type names that alias carries "my elements are buffers". The `push` proves the type is
NOT forked: an ordinary `Array with Byte` still goes into it.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let m = try a.get(0) otherwise return 1
	return m.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: array-element-through-a-struct-field-serves-the-roster -->

The same element mark reached through a struct FIELD declared with the alias. The field column is written by
the declaration SWEEP, which runs before any alias is interned, so the element half cannot ride it — it is
DERIVED at the read from the alias name the swept `named` type still holds.
```maxon
typealias BufArray = Array with __ManagedMemory

type Holder
	export var bufs as BufArray

	export static function create(bufs BufArray) returns Self
		return Self{bufs: bufs}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let h = Holder.create(a)
	let m = try h.bufs.get(0) otherwise return 1
	return m.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.tuple-element-has-the-buffer-surface -->

The refusal half of the tuple slot. `count` is an `Array` member and not a buffer one, and this program
compiled, linked and RAN (exit 5) before A2m.
```maxon
typealias Int = int(i64.min to i64.max)

function pair() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let p = pair()
	return p.0.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:13: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.array-element-has-the-buffer-surface -->

The refusal half of the array element, which likewise ran (exit 5) before A2m.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let m = try a.get(0) otherwise return 1
	return m.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:11: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.a-tuple-of-byte-arrays-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL FOR THE TUPLE, and it is the case this rung is graded on.**
`(ByteArray, Int)` and `(__ManagedMemory, Int)` are ONE interned tuple type sharing ONE `StructLayout`, so
populating that layout's surface column would hand the buffer's roster to BOTH — the direction no diagnostic
reports, decided by whichever spelling interned first. The mask rides the VALUE instead, so this stays
refused with the `Array` roster.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function pair() returns (ByteArray, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let p = pair()
	return p.0.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:13: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-tuple-of-byte-arrays-still-serves-count -->

The control's positive half — the same tuple type, still answering the `Array` roster it belongs to. A
refusal case alone would pass just as well if the element had lost BOTH surfaces.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function pair() returns (ByteArray, Int)
	return ("hello".toByteArray(), 7)
end 'pair'

function main() returns ExitCode
	let p = pair()
	return p.0.count() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.an-array-of-byte-arrays-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL FOR THE ARRAY ELEMENT.** `Array with ByteArray` and
`Array with __ManagedMemory` share one `GenericInstanceId`, so the element surface may not be keyed on the
instance. It is keyed on the ALIAS NAME the declaration wrote, and this alias did not write the buffer's.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BufArray = Array with ByteArray

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let m = try a.get(0) otherwise return 1
	return m.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:11: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: an-array-of-byte-arrays-still-serves-count -->

The array control's positive half, for the reason its tuple twin has one.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BufArray = Array with ByteArray

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let m = try a.get(0) otherwise return 1
	return m.count() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.tuple-slot-mark-does-not-leak-across-functions -->

The slot mask's copy-on-write twin of `error.buffer-mark-does-not-leak-across-functions`, and it is needed
for the identical reason: `ValueId`s restart at 0 in every function, the mask table's empty anchor is
MODULE-level, and a write into the anchor would name an id in an unrelated function's SSA space.

**The two functions are shaped ALIKE ON PURPOSE, and that is what makes the case work rather than a
coincidence of padding**: each binds its tuple from a bare call as its first statement, so `p` and `q` are
both ValueId 0 and a leaked mark lands exactly on top. **MEASURED, by removing the copy-on-write detach: this
program then COMPILED, LINKED AND RAN, exit 7** — `q.0.length()` accepted on a slot declared
`Array with Byte`, which is the over-acceptance no diagnostic reports.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function bufferPair() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'bufferPair'

function arrayPair() returns (ByteArray, Int)
	return ("hi".toByteArray(), 3)
end 'arrayPair'

function useBufferPair() returns Int
	let p = bufferPair()
	return p.0.length()
end 'useBufferPair'

function main() returns ExitCode
	let q = arrayPair()
	return (q.0.length() + useBufferPair()) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:21:14: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

### A2m pins — four behaviours that were already RIGHT and had nothing holding them there

Each of these was MEASURED correct on the tree A2m started from, and none had a test. They are the
regressions a rung that moves the buffer mark is most likely to cause, so they are pinned before it moves.

<!-- test: managed-field-chained-serves-the-buffer-roster -->

The CHAINED `arr.managed.<member>()` is an IDENTITY read that passes the buffer surface along the dispatch
rather than minting a value, so it reaches the buffer's roster with no allocation and no runtime call. (The
bare VALUE form, `let m = arr.managed`, is a different door and mints a value — see the cases below it.)
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	arr.push(2 as Byte)
	return arr.managed.length() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: error.managed-field-chain-does-not-mark-the-array -->

And the surface does NOT flow back onto the receiver: after `arr.managed.length()`, `arr` is still an
`Array` and still refused `length`. The identity read passes the surface to ONE dispatch, not to the value.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	arr.push(2 as Byte)
	let n = arr.managed.length()
	return (n + arr.length()) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:18: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: managed-field-as-a-value-binds-serves-the-roster-and-drops-once -->

**`arr.managed` IN VALUE POSITION (BATCH2 slice 6).** Four doors in one program, all on the SAME record: a
`let` binding, a call ARGUMENT, a `return` out of a function whose receiver is a borrowed PARAMETER, and a
LOOP that takes the buffer once per iteration. `.managed` hands back the record the array already owns, so
every one of them must become a second OWNER (`__mm_retain`) rather than a second name for one owner — with
a binding and an array both enrolled to drop one box, this program double-frees. The exit code is the leak
gate: a missed retain corrupts the allocator, a missed drop exits 101.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function bufLength(m __ManagedMemory) returns int
	return m.length()
end 'bufLength'

function bufferOf(a ByteArray) returns __ManagedMemory
	return a.managed
end 'bufferOf'

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	arr.push(2 as Byte)
	let m = arr.managed
	var total = m.length() + bufLength(arr.managed) + bufferOf(arr).length() + arr.count()
	var i = 0
	while i < 4 'round'
		total = total + bufLength(arr.managed)
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
16
```

<!-- test: error.managed-field-as-a-value-does-not-mark-the-array -->

The VALUE form's surface mark rides the value `.managed` MINTS, never the receiver — the same property the
chained form has, for the same reason, and the one a pass-through `ValueId` would break in both directions at
once (the array would answer the buffer's roster, and the two would be one owner). After `let m =
arr.managed`, `arr` is still an `Array` and still refused `length`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	let m = arr.managed
	return (m.length() + arr.length()) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:27: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: managed-field-on-a-value-receiver-binds-and-serves-the-roster -->

The OTHER door onto the same arm: a VALUE receiver, whose record the STATEMENT already owns. It takes no
retain — a second owner of a temporary nobody else will drop is a retain and a release that cancel — and the
binding takes the statement's one obligation over exactly as it would for any other owned temporary. The
SURFACE is the same either way, which is what makes the two doors agree: `m.length()` is the buffer's member,
not the `Array`'s.
```maxon
function main() returns ExitCode
	let m = "abcd".toByteArray().managed
	return m.length() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.managed-field-alone-is-not-a-statement -->

The value form is claimed in EXPRESSION position only. A statement of it would build a buffer reference and
discard it on the same line, so the statement dispatcher keeps asking for the chained `.<member>(` spelling
(`Parser.arrayManagedMemberCallAt`) and a bare one falls through refused.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1 as Byte)
	arr.managed
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:8:2: Unsupported: identifier statement
```

<!-- test: managed-field-as-a-value-releases-in-every-forking-region -->

⭐ **THE VALUE FORM TAKES A STATEMENT-SCOPED DROP, SO IT IS SUBJECT TO THE REGION DISCIPLINE**
(`coOwnAggregateAsTemp` → `trackOwnedTemp` → `drainPendingTemps`), and that discipline has no automatic
check behind it — every construct that FORKS must release its own temporaries in a block their definitions
dominate, and the list of such constructs is maintained by hand. So this walks the regions a `.managed`
value can now be built in, which the four cases above do not reach: an `if` CONDITION, a `while` CONDITION
(re-entered per iteration), a `for` SOURCE, and a receiver that is a `var` REASSIGNED under all of them. A
release on too few paths is a leak (exit 101); one on a path that never built the value is a dominance
failure several passes later. Each receiver is read back as an `Array` (`count`) afterwards, which is what
says the buffer mark never reached it.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function bufLength(m __ManagedMemory) returns int
	return m.length()
end 'bufLength'

function main() returns ExitCode
	var a = ByteArray.create()
	a.push(1 as Byte)
	a.push(2 as Byte)
	var total = 0

	if bufLength(a.managed) > 1 'big'
		total = total + 1
	end 'big'

	while bufLength(a.managed) > total 'spin'
		total = total + 1
	end 'spin'

	for b in a.managed 'each'
		total = total + b
	end 'each'

	a = "xyz".toByteArray()
	total = total + bufLength(a.managed) + a.count()

	print("{total}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
11
```

<!-- test: buffer-surface-survives-a-var-reassignment -->

A `var` reassigned from another buffer keeps the buffer surface. The mark rides the VALUE, and a reassignment
rebinds the NAME to a new value — so this holds only because the new value is itself marked, which is exactly
what a future change to how a `var` merges its values could break silently.
```maxon
function main() returns ExitCode
	var v = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try v.setLength(4) otherwise return 2
	var w = try __ManagedMemory.create(2, elementSize: 1) otherwise return 3
	try w.setLength(2) otherwise return 4
	v = w
	return v.length() as ExitCode
end 'main'
```
```exitcode
2
```

<!-- test: buffer-surface-survives-a-var-reassigned-from-a-slice -->

The same rebinding, from a `slice` of the buffer's OWN value — the one producer whose result is marked by
`parseArraySlice` rather than by a declaration.
```maxon
function main() returns ExitCode
	var v = try __ManagedMemory.create(6, elementSize: 1) otherwise return 1
	try v.setLength(6) otherwise return 2
	v = try v.slice(1, 4) otherwise return 3
	return v.length() as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: error.a-tuple-returning-overload-is-refused-the-same-way -->

⭐ **THE WIDENED HALF (A2m).** Neither declaration RETURNS `__ManagedMemory`; one of them names it at a tuple
SLOT, which is the identical ambiguity through the identical channel. Before A2m this program compiled, and
the surface `make()`'s result carried was decided by which member was written first.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function make() returns (__ManagedMemory, Int)
	return ("hello".toByteArray(), 7)
end 'make'

function make(n Int) returns (ByteArray, Int)
	return ("abc".toByteArray(), n)
end 'make'

function main() returns ExitCode
	let a = make()
	return a.0.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:10: Unsupported: overloading 'make' — one of its declarations NAMES `__ManagedMemory` in its return type, and the whole-program declaration sweep publishes that SPELLING under the name the source wrote, so a call to this name cannot be told whether its result carries the buffer's member roster or the `Array`'s. The surface is chosen when the call is PARSED and the overload is resolved a whole pass later, so nothing downstream can repair it. Give the overloads distinct names
```

### A2m — every door out of an array of buffers, and every copy of one

Found by probing the element mark rather than by the defect row, which named only `get`. An element read has
SIX spellings and a whole-array copy has TWO, and a mark that reached some of them would be the same
inverted roster at the spellings it missed.

<!-- test: every-borrowing-element-member-serves-the-buffer-roster -->

`get`, `first` and `last` all BORROW an element, and all three funnel through the ONE accessor mint — so the
mark is made there rather than per arm.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	a.push("ab".toByteArray())
	let g = try a.get(0) otherwise return 1
	let f = try a.first() otherwise return 2
	let l = try a.last() otherwise return 3
	return (g.length() + f.length() + l.length()) as ExitCode
end 'main'
```
```exitcode
12
```

<!-- test: a-popped-element-serves-the-buffer-roster -->

`pop` MOVES the element out instead of borrowing it, so it gets its own case for two reasons: it may not
share a function with a live borrow of the same array (E3070), and the OWNED buffer must reach the roster
AND still be dropped exactly once — the exit code proves the first, the leak gate the second.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let p = try a.pop() otherwise return 1
	return p.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-for-loop-element-serves-the-buffer-roster -->

The fifth spelling: `for m in bufs` reads its element through the same accessor, with the loop's own
unchecked callee. It is a BORROW, so nothing here is dropped twice.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	a.push("ab".toByteArray())
	var total = 0
	for m in a 'each'
		total = total + m.length()
	end 'each'
	return total as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-clone-of-an-array-of-buffers-still-holds-buffers -->

⭐ **FOUND BY PROBING, AND IT WAS BROKEN.** `clone` hands back a whole array of the receiver's OWN instance,
and that instance cannot carry the element spelling — `Array with __ManagedMemory` and `Array with ByteArray`
are one `GenericInstanceId`. MEASURED before the fix: `a.clone()` then `.get(0).length()` was refused as an
unknown `Array` method while `a.get(0).length()` answered, on the same array in the same function.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let b = a.clone()
	let m = try b.get(0) otherwise return 1
	return m.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-slice-of-an-array-of-buffers-still-holds-buffers -->

The `Array`-surface `slice` is the other whole-array producer, and it carries the element surface for the
reason `clone` does — one container out from the rule that already made a slice of a BUFFER a buffer.
```maxon
typealias BufArray = Array with __ManagedMemory

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	a.push("ab".toByteArray())
	let b = try a.slice(1, endIndex: 2) otherwise return 1
	let m = try b.get(0) otherwise return 2
	return m.length() as ExitCode
end 'main'
```
```exitcode
2
```

### A2m — a tuple SLOT carries a whole surface, so the mechanism has no depth

⚖ **COORDINATOR RULING.** A2m first shipped the slot payload as one BIT of a flat 62-slot mask, which gave
the mechanism an arbitrary depth a reader of the language could see and could not state: `(__ManagedMemory,
Int)` worked while `(BufArray, Int)` and `((__ManagedMemory, Int), Int)` did not. **A slot is a declared
position like any other, so it now carries what any other carries — a whole `DeclaredSurface`, recursively.**

That also **removed** the arity question rather than answering it again: the mask needed a ceiling (62) and a
refusal to stop a 63rd slot silently losing its bit. A pre-order TREE has one node per named position, no
width and no depth, nothing to truncate and so nothing to refuse.

⚠ **ONE SHAPE IS NOT CLOSED AND IS NOT DISABLED — it is closed at the door that can answer it and stated
here.** A slot spelled with an array-of-buffers ALIAS works through a PARAMETER (below) and does NOT work
through a return type or a struct field. Those two are captured by the declaration SWEEP, which asks the
alias registry at a moment it cannot guarantee an answer, and unlike the ROOT's element bit there is nothing
left to derive it from later: a tuple's element types are CANONICAL by the time anything could ask, so the
alias name is gone. It is blocked on the alias-visibility work, not on this mechanism.

<!-- test: a-nested-tuple-slot-serves-the-roster -->

A tuple inside a tuple. `p.0` binds a value carrying ITS slots, and `p.0.0` reads one off it — the same line
of `emitFieldLoad` running twice, one container in each time. Nothing in the mechanism knows how deep it is.
```maxon
typealias Int = int(i64.min to i64.max)

function nested() returns ((__ManagedMemory, Int), Int)
	return (("hello".toByteArray(), 7), 9)
end 'nested'

function main() returns ExitCode
	let p = nested()
	return p.0.0.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.a-nested-tuple-of-byte-arrays-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL AT DEPTH, and it matters more than the positive case above.**
`((ByteArray, Int), Int)` and `((__ManagedMemory, Int), Int)` canonicalize to the SAME two interned tuples
sharing the same two layouts, so a surface written onto either layout would reach both spellings at both
depths. It rides the value, so this stays refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function nested() returns ((ByteArray, Int), Int)
	return (("hello".toByteArray(), 7), 9)
end 'nested'

function main() returns ExitCode
	let p = nested()
	return p.0.0.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:15: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-nested-tuple-of-byte-arrays-still-serves-count -->

The depth control's positive half — the same nested slot, still answering the `Array` roster it belongs to.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function nested() returns ((ByteArray, Int), Int)
	return (("hello".toByteArray(), 7), 9)
end 'nested'

function main() returns ExitCode
	let p = nested()
	return p.0.0.count() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-tuple-slot-of-buffers-serves-the-roster -->

A slot whose declared type is an array-of-buffers ALIAS — the surface a slot could not express at all while
its payload was one bit. The element bit is asked of the alias registry, so this is the PARAMETER door: it is
read in the real parse, where every file is folded. See this section's ⚠ for why the return and field doors
cannot yet answer it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

function slotted(t (BufArray, Int)) returns Int
	let m = try t.0.get(0) otherwise return 0
	return m.length()
end 'slotted'

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	return slotted((a, 7)) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.a-tuple-slot-of-byte-arrays-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL FOR THE ALIAS SLOT.** `Array with ByteArray` and
`Array with __ManagedMemory` share one `GenericInstanceId`, so the slot's element surface may not be keyed on
the tuple, the layout or the instance — only on the ALIAS NAME the declaration wrote, and this one did not
write the buffer's.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BufList = Array with ByteArray

function slotted(t (BufList, Int)) returns Int
	let m = try t.0.get(0) otherwise return 0
	return m.length()
end 'slotted'

function main() returns ExitCode
	var a = BufList.create()
	a.push("hello".toByteArray())
	return slotted((a, 7)) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:11: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-tuple-slot-of-byte-arrays-still-serves-count -->

The alias-slot control's positive half, for the reason its two siblings have one.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BufList = Array with ByteArray

function slotted(t (BufList, Int)) returns Int
	let m = try t.0.get(0) otherwise return 0
	return m.count()
end 'slotted'

function main() returns ExitCode
	var a = BufList.create()
	a.push("hello".toByteArray())
	return slotted((a, 7)) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-nested-tuple-parameter-serves-the-roster -->

The nested shape through the PARAMETER door, where the mask is read off the annotation's own tokens rather
than off the whole-program index — the two capture sites must agree, and a case each is how they are held to
it.
```maxon
typealias Int = int(i64.min to i64.max)

function shownNested(t ((__ManagedMemory, Int), Int)) returns Int
	return t.0.0.length()
end 'shownNested'

function main() returns ExitCode
	return shownNested((("hello".toByteArray(), 7), 9)) as ExitCode
end 'main'
```
```exitcode
5
```

### A2m — the array-of-buffers alias across a FILE BOUNDARY, in BOTH orders

⭐⭐ **THE ELEMENT BIT IS THE ONE SURFACE FACT THAT CONSULTS A REGISTRY, AND A REGISTRY HAS A FILLING
ORDER — SO EVERY CASE HERE IS WRITTEN TWICE, WITH THE TWO FILES SWAPPED.** A single-file fragment cannot see
this at all: a file's own `typealias` folds only when that file's sweep ENDS, so within one file the answer is
always "not yet registered". The question only becomes askable — and only becomes ORDER-dependent — once the
alias arrives from a SIBLING file.

⚠ **MEASURED, REVIEW OF A2m** — before the fix below, the third case here COMPILED AND RAN while the fourth,
the identical program with its two files renamed, was refused E2015. A member roster that depends on which
file is walked first is a wrong answer in one of the two orders, whichever one it is, and no diagnostic
reports which one you got.

⚠⚠ **THE DECLARED FILE ORDER IS THE COMPILED ORDER (A3m) — WHICH IS WHAT LETS A PAIR MEAN TWO ORDERS
RATHER THAN ONE ORDER TWICE.** It was not always. Until A3m `build` took a single path, the loader walked
whatever raw `Directory.list` handed back, and the walk order was therefore a property of the STAGING
DIRECTORY's on-disk state — so both halves of a pair like this one got whichever order the host happened
to serve, and the pair was two tickets in one lottery. The `aaa-`/`zzz-` names these cases used to carry
were a bet on that ticket and are gone: what a half declares FIRST is now what the compiler compiles
first, stated by the order its `// --- file:` sections appear in and handed to the compiler as an ordered
argument list (`SpecTestRunner.stageSourceFiles`).

⚠ **THE LOADER STILL DOES NOT SORT, AND THAT RULING IS UNTOUCHED** (`StdlibLoader`'s header, user ruling
2026-07-24). The cure is the opposite of a sort: a canonical order chosen by the LOADER would HIDE an
order dependence, while an order STATED by the caller surfaces one — which is the entire reason each of
these programs is written twice.

⚠ **THE ROOT AND A SLOT ANSWER DIFFERENTLY, AND THE FIRST TWO CASES ARE WHY THAT IS NOT AN INCONSISTENCY.**
A ROOT position — `var bufs as BufArray` — is repaired at the read door: `ProgramSignatures.fieldSurfaceOf`
ORs in `declaredElementIsBufferSurface`, which reads the alias NAME off a swept type that is still `named`,
and a swept type is still `named` in exactly the order where the token test finds nothing (both conditions
are `recordGenericAlias`'s ONE write). The two halves cover the two orders between them. A SLOT has no second
half — a tuple's element types are canonical by the time anything could ask — so it must not read the
registry at a sweep-fed door at all, and is refused in both orders instead of accepted in one.

<!-- test: a-sibling-files-array-of-buffers-alias-serves-the-roster -->

⭐ **THE ROOT, ALIAS FILE FIRST.** `Holder.bufs` is declared with an alias the OTHER file writes, so by the
time this file is swept the alias is registered and the field type has already resolved to
`genericInstance` — the read-door derivation cannot fire, and the declaration's own token bit is what carries
it.
```maxon
// --- file: alias.maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

export function seed(x BufArray) returns Int
	return x.count() as Int
end 'seed'

// --- file: main.maxon
type Holder
	export var bufs as BufArray

	export static function create() returns Holder
		var a = BufArray.create()
		a.push("hello".toByteArray())
		return Self{bufs: a}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	let m = try h.bufs.get(0) otherwise return 1
	return (m.length() + seed(BufArray.create())) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-sibling-files-array-of-buffers-alias-serves-the-roster-either-order -->

⭐ **THE ROOT, ALIAS FILE LAST — the identical program, its two files declared the other way round.** Now the alias is NOT yet
registered when the holder is swept, so the field type stays `named("BufArray")` and it is the read-door
DERIVATION that carries the surface. Both halves are pinned because either one alone leaves one order wrong,
and this pair is what caught a review fix that had suppressed the first half.
```maxon
// --- file: main.maxon
type Holder
	export var bufs as BufArray

	export static function create() returns Holder
		var a = BufArray.create()
		a.push("hello".toByteArray())
		return Self{bufs: a}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create()
	let m = try h.bufs.get(0) otherwise return 1
	return (m.length() + seed(BufArray.create())) as ExitCode
end 'main'

// --- file: alias.maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

export function seed(x BufArray) returns Int
	return x.count() as Int
end 'seed'
```
```exitcode
5
```

<!-- test: error.a-sibling-files-alias-in-a-tuple-slot-is-refused-alias-file-first -->

⭐⭐ **THE HEADLINE REGRESSION CASE. This program COMPILED AND RAN before the fix** — `make().0` served the
buffer's roster because the alias file was compiled first — which this half now DECLARES rather than hoping
the staging directory serves it (see this section's header). A RETURN clause is read by
the tolerant declaration SWEEP as well as by the real parse, and the sweep's copy is the one the whole-program
index stores; asked there, a slot's element bit answers how far the sweep had got. It is refused now, in this
order and in the next case's, which is the accepted gap (a tuple slot spelled with an array-of-buffers alias
works at the PARAMETER door only) rather than a coin toss between the gap and the feature.
```maxon
// --- file: alias.maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

export function seed(x BufArray) returns Int
	return x.count() as Int
end 'seed'

// --- file: main.maxon
function make() returns (BufArray, Int)
	var a = BufArray.create()
	a.push("hello".toByteArray())
	return (a, 7)
end 'make'

function main() returns ExitCode
	let t = make()
	let m = try t.0.get(0) otherwise return 0
	return (m.length() + seed(BufArray.create())) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:20:12: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: error.a-sibling-files-alias-in-a-tuple-slot-is-refused-alias-file-last -->

⚠ **THE OTHER ORDER — the identical program, its two files declared the other way round.** It was already refused before the fix;
it is kept because a pair is what makes "order-independent" a claim a test can fail, and half a pair is just
the answer that happened to be right.
```maxon
// --- file: main.maxon
function make() returns (BufArray, Int)
	var a = BufArray.create()
	a.push("hello".toByteArray())
	return (a, 7)
end 'make'

function main() returns ExitCode
	let t = make()
	let m = try t.0.get(0) otherwise return 0
	return (m.length() + seed(BufArray.create())) as ExitCode
end 'main'

// --- file: alias.maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

export function seed(x BufArray) returns Int
	return x.count() as Int
end 'seed'
```
```maxoncstderr
error E2015: <fragment>:12:12: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

### A2m — probing the tree walk: depth, sibling order, and width

The pre-order tree is read by skipping whole SUBTREES to reach a later sibling, which is where an off-by-one
would live and where nothing above would find it: every case so far reads slot 0 of a tuple whose earlier
siblings are absent. These four were written to break that walk, and are kept because a walk with no test
over a LATER sibling of a NESTED slot is a walk nobody has read.

<!-- test: a-buffer-three-tuples-deep-serves-the-roster -->

Slot 1 of slot 1 of slot 0 — three levels, and every step past a sibling the walk has to skip.
```maxon
typealias Int = int(i64.min to i64.max)

function deep() returns (Int, (Int, (__ManagedMemory, Int)), Int)
	return (1, (2, ("hello".toByteArray(), 7)), 9)
end 'deep'

function main() returns ExitCode
	let p = deep()
	return p.1.1.0.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: two-nested-slots-of-one-tuple-take-their-own-surfaces -->

⭐ **THE STRONGEST FORM OF THE OVER-ACCEPTANCE CONTROL: both spellings in ONE tuple, one statement.** Slot 1's
slot 1 is a buffer and answers `length()` (4); slot 0's slot 0 is a `ByteArray` and answers `count()` (5).
If the surface had leaked onto either shared tuple layout — and the two nested tuples ARE separate shared
layouts — this program could not give 9, because one of the two calls would have been refused.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function mixed() returns ((ByteArray, Int), (Int, __ManagedMemory), Int)
	return (("hello".toByteArray(), 1), (2, "abcd".toByteArray()), 9)
end 'mixed'

function main() returns ExitCode
	let p = mixed()
	return (p.1.1.length() + p.0.0.count()) as ExitCode
end 'main'
```
```exitcode
9
```

<!-- test: error.a-byte-array-slot-beside-a-buffer-slot-keeps-the-array-surface -->

And the refusal half of that same tuple: the `ByteArray`-spelled nested slot is still refused `length`, in a
type that carries a buffer at another slot. A mask that set the wrong bit, or a walk that skipped the wrong
subtree, would accept this.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function mixed() returns ((ByteArray, Int), (Int, __ManagedMemory), Int)
	return (("hello".toByteArray(), 1), (2, "abcd".toByteArray()), 9)
end 'mixed'

function main() returns ExitCode
	let p = mixed()
	return p.0.0.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:12:15: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-seventy-element-tuple-carries-its-last-slots-surface -->

⭐ **THE ARITY CEILING IS GONE, AND THIS IS WHAT SAYS SO.** A2m's first mask held 62 slots and REFUSED a
wider tuple type, because a 63rd slot would have silently lost its bit. A tree has one node per named
position, so seventy is not a special number and neither is any other: the buffer at slot 69 answers.
```maxon
typealias Int = int(i64.min to i64.max)

function wide() returns (Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, Int, __ManagedMemory)
	return (1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, "hello".toByteArray())
end 'wide'

function main() returns ExitCode
	let p = wide()
	return p.69.length() as ExitCode
end 'main'
```
```exitcode
5
```

### BATCH22 — a union-case PAYLOAD is the FIFTH declared position, and it never joined the discipline

⚖ **THE SAME USER RULING (2026-07-31), at the one declared position A2j and A2m both missed.** A declared
spelling reaches the value it describes through exactly one bridge (`Parser.markDeclaredSurface`), and it
had four producers: a PARAMETER, a struct FIELD, a tuple SLOT and a declared RETURN. A `union` case's
payload is a fifth — `mem(m __ManagedMemory)` is a declared position spelled by the very type reader the
other four use (`readPayloadFieldsInto` calls `parseTypeReference`) — and a `match` binding destructured
out of it was minted with no surface at all, i.e. the `Array` one, which is the absence of the mark.

**MEASURED before this fix, on the first two programs below**: `x.length()` on a payload declared
`__ManagedMemory` was refused as an unknown `Array` member, while `x.count()` compiled and RAN — the
roster exactly inverted, exactly as A2j measured it at the other three spellings.

⚠ **THE GAP WAS ONE LEVEL BELOW THE MISSING CALL.** `StructLayout` has carried a per-field declared-surface
column since A2j (`fieldDeclaredSurfaces`, read through `ProgramSignatures.fieldSurfaceOf`); `PayloadField`
carried only a name and a declared type, so the declaration SWEEP that captures *"this position spelled
`__ManagedMemory`"* had nowhere to write a payload's answer and adding the mark alone would have read
nothing. The column is the fix; the mark is what reads it.

<!-- test: union-payload-declared-managed-memory-serves-the-roster -->

The false-reject half, and the one that was WRONG rather than merely permissive: `length()` is the buffer
roster's FIRST member. The payload value is an ordinary `"hello".toByteArray()` — nothing about the VALUE
says buffer, and the case declaration is doing all the work.
```maxon
typealias Int = int(i64.min to i64.max)

union Held
	mem(m __ManagedMemory)
	nothing
end 'Held'

function size(h Held) returns Int
	return match h 'k'
		mem(x) gives x.length()
		nothing gives 0
	end 'k'
end 'size'

function main() returns ExitCode
	let h = Held.mem("hello".toByteArray())
	return size(h) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.union-payload-declared-managed-memory-refuses-an-array-member -->

The refusal half. `count` is an `Array` member and not a buffer one, so the binding is refused it — and
refused it with the BUFFER's roster, which is what says the message answers for the type it is shown about.
**This exact program COMPILED before this fix** — the runner's own words were *"expected a compile error
but compilation succeeded"* — which is the wrong answer no diagnostic reported.
```maxon
typealias Int = int(i64.min to i64.max)

union Held
	mem(m __ManagedMemory)
	nothing
end 'Held'

function size(h Held) returns Int
	return match h 'k'
		mem(x) gives x.count()
		nothing gives 0
	end 'k'
end 'size'

function main() returns ExitCode
	let h = Held.mem("hello".toByteArray())
	return size(h) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:11:18: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: union-payload-declared-managed-memory-serves-a-shared-member -->

The POSITIVE CONTROL — a member BOTH rosters name, so it answers on either side of the flip and pins that
the payload binding stayed usable rather than merely changing which refusal it gets. `capacity` is on the
`Array` roster and on the buffer's, and with `elementSize: 1` the two readings of it are the same number.
```maxon
typealias Int = int(i64.min to i64.max)

union Held
	mem(m __ManagedMemory)
	nothing
end 'Held'

function room(h Held) returns Int
	return match h 'k'
		mem(x) gives x.capacity()
		nothing gives 0
	end 'k'
end 'room'

function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	let h = Held.mem(mm)
	return room(h) as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: error.a-byte-array-payload-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL, and it is the load-bearing one.** `__ManagedMemory` is a generic ALIAS
of `Array with Byte`, so a payload declared `ByteArray` resolves to the IDENTICAL `MaxonType` — the surface
cannot be keyed on it. A fix that handed the buffer roster to every payload would accept this program, and
nothing would report it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Held
	mem(m ByteArray)
	nothing
end 'Held'

function size(h Held) returns Int
	return match h 'k'
		mem(x) gives x.length()
		nothing gives 0
	end 'k'
end 'size'

function main() returns ExitCode
	let h = Held.mem("hello".toByteArray())
	return size(h) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:13:18: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-byte-array-payload-still-serves-count -->

The over-acceptance control's positive half, for the reason its A2m twins have one: a refusal alone would
also be satisfied by a payload binding that lost its `Array` surface without gaining the buffer's.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

union Held
	mem(m ByteArray)
	nothing
end 'Held'

function size(h Held) returns Int
	return match h 'k'
		mem(x) gives x.count()
		nothing gives 0
	end 'k'
end 'size'

function main() returns ExitCode
	let h = Held.mem("hello".toByteArray())
	return size(h) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: union-payload-declared-an-array-of-buffers-serves-the-roster -->

⭐ **A2m's DEPTH AT THE NEW DOOR, and it comes free rather than needing a second mechanism**: a payload
declared `BufArray` hands its ELEMENTS the buffer surface, so `x.get(0).length()` answers. The element bit
is the one of the three the declaration SWEEP cannot store — at sweep time `BufArray` may not be interned
yet — so it is DERIVED at the read from the alias NAME the swept `named` still carries, through the one
fold a struct field and a declared return already came out of (`surfaceWithDerivedElement`).

⚠ Its RED is STRUCTURAL rather than measured: before this fix `emitUnionPayloadLoad` called
`markDeclaredSurface` not at all, so no payload binding could carry ANY of the three marks — which is the
same absence the two cases above measured directly on the outright bit.
```maxon
typealias BufArray = Array with __ManagedMemory

union Held
	bufs(b BufArray)
	nothing
end 'Held'

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let h = Held.bufs(a)
	match h 'k'
		bufs(x) then return (try x.get(0) otherwise return 1).length() as ExitCode
		nothing then return 0
	end 'k'
end 'main'
```
```exitcode
5
```

<!-- test: error.a-tuple-slot-mark-does-not-leak-into-a-parameter-default-helper -->

⭐⭐ **THE SIXTH VALUE SPACE — a PARAMETER DEFAULT's synthesized helper — reset one of the three surface
columns and not the other two (found by BATCH22's REVIEW).** `error.tuple-slot-mark-does-not-leak-across-functions`
above pins the ordinary function→function boundary, and it is carried by `markBufferSurfaceSlots`'
copy-on-write detach off the module-level anchor. That detach cannot help here: a default helper is a whole
FUNCTION parsed at the end of `parseModule`, so what reaches it is the previous function's own PRIVATE map,
and only an explicit reset can drop it. `parseParamDefaultHelper` reset `bufferSurfaceValues` alone —
`parseFunction`'s own comment says all THREE reset together "because they describe one value space", and
`parseClosureExpression` resets all three — so `bufferSurfaceSlots` and `bufferSurfaceElements` crossed.

**The shapes line up on purpose, exactly as the case above lines them up**: `useBufferPair`'s tuple PARAMETER
is ValueId 0 and carries the slot mask; the helper's `valueIds` restart at 0 and `arrayPair()` is its first
mint, so the leaked mask lands exactly on top. **MEASURED before the fix: this program COMPILED, LINKED AND
RAN, exit 2** — `length()` served on a slot declared `Array with Byte`, from a mark minted in a function
whose text the helper does not contain.

⚠ `useBufferPair` is deliberately the LAST declaration and is never called: the drain runs after the last
top-level declaration has closed, so the state that reaches the helper is the last function's.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function arrayPair() returns (ByteArray, Int)
	return ("hi".toByteArray(), 3)
end 'arrayPair'

function withDefault(k Int = arrayPair().0.length()) returns Int
	return k
end 'withDefault'

function main() returns ExitCode
	return withDefault() as ExitCode
end 'main'

function useBufferPair(p (__ManagedMemory, Int)) returns Int
	return p.0.length()
end 'useBufferPair'
```
```maxoncstderr
error E2015: <fragment>:10:44: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-sibling-files-buffer-array-payload-serves-the-roster-alias-file-first -->

⭐ **A2m's FILE-ORDER PAIR AT THE NEW DOOR (BATCH22 review).** `union-payload-declared-an-array-of-buffers-serves-the-roster`
above is single-file, and A2m's own measured hazard is that a member roster can depend on FILE ORDER — the
identical two-file program compiled one way round and was refused E2015 the other. The payload's element bit
is carried by the same complementary pair the struct FIELD's is (`a-sibling-files-array-of-buffers-alias-serves-the-roster`
and its `-either-order` twin), so it owes the same two halves. **Alias file FIRST**: the alias is registered
by the time the union is swept, the payload's declared type has already resolved to `genericInstance`, the
read-door derivation cannot fire, and the declaration's own token bit is what carries it.
```maxon
// --- file: alias.maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

export function seed(x BufArray) returns Int
	return x.count() as Int
end 'seed'

// --- file: main.maxon
union Held
	bufs(b BufArray)
	nothing
end 'Held'

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let h = Held.bufs(a)
	match h 'k'
		bufs(x) then return ((try x.get(0) otherwise return 1).length() + seed(BufArray.create())) as ExitCode
		nothing then return 0
	end 'k'
end 'main'
```
```exitcode
5
```

<!-- test: a-sibling-files-buffer-array-payload-serves-the-roster-alias-file-last -->

⭐ **The other half — the identical program, its two files declared the other way round.** Now the alias is
NOT yet registered when the union is swept, so the payload type stays `named("BufArray")`, the sweep's stored
element bit is clear, and it is `ProgramSignatures.surfaceWithDerivedElement`'s read-door DERIVATION that
carries the surface. Both halves are pinned because either one alone leaves one order wrong.
```maxon
// --- file: main.maxon
union Held
	bufs(b BufArray)
	nothing
end 'Held'

function main() returns ExitCode
	var a = BufArray.create()
	a.push("hello".toByteArray())
	let h = Held.bufs(a)
	match h 'k'
		bufs(x) then return ((try x.get(0) otherwise return 1).length() + seed(BufArray.create())) as ExitCode
		nothing then return 0
	end 'k'
end 'main'

// --- file: alias.maxon
typealias Int = int(i64.min to i64.max)
typealias BufArray = Array with __ManagedMemory

export function seed(x BufArray) returns Int
	return x.count() as Int
end 'seed'
```
```exitcode
5
```

### A `typealias` FOR the buffer carries the surface at every SWEPT declared position (W49)

⭐⭐ **THE BIT THE SWEEP STORES IS NOT A BIT THE SWEEP CAN ANSWER.** W43 made
`typealias KeyMemory = __ManagedMemory with Key` denote the buffer, filed by NAME in
`ProgramSignatures.bufferSurfaceAliases` because a `GenericInstanceId` structurally cannot carry it
(`__ManagedMemory with T` and `Array with T` intern to ONE instance). But `recordGenericAlias` — the one
writer of that set — is called from `foldFile`, i.e. AFTER the whole file has been swept, while
`Parser.declaredSurfaceAt` asks `aliasDenotesBufferSurface` DURING the sweep and writes its answer into the
stored column. **So a position's surface was decided one pass before the fact that decides it existed**, and
the answer stored for every same-file alias spelling was the `Array` one.

**MEASURED before this fix**, on a ten-line program with no `String` anywhere: a file-scope
`typealias ProbeBuf = __ManagedMemory with Byte` and a `var bytes as ProbeBuf` SEVEN LINES BELOW it, whose
`bytes.length()` — the buffer roster's FIRST member — was refused as an unknown `Array` member. A PARAMETER
spelled with the same alias compiled and ran, and that is what located the defect: a parameter's surface is
read in the REAL parse, where the registry is complete, and the other positions are read by the SWEEP.

⭐ **THE CURE IS THE ONE THE ELEMENT BIT ALREADY HAD, EXTENDED TO ITS TWIN** — not a second mechanism.
`SurfaceBitElementIsBuffer` was recognised at A2m as underivable at sweep time and is therefore DERIVED at
the read door from the alias NAME the swept `named` type still carries
(`ProgramSignatures.surfaceWithDerivedAliasBits`). `SurfaceBitIsBuffer` has the identical problem, the
identical registry — written on the adjacent line of the same function — and the identical carrier, so it is
now derived through the identical door. The two halves are complementary by construction and cover the two
file orders between them: a swept type is still `named` EXACTLY when the alias was not yet registered, which
is EXACTLY when the token test found nothing, because both conditions are `recordGenericAlias`'s ONE write.

<!-- test: a-buffer-alias-serves-the-roster-at-a-struct-field -->

The ten-line repro, through TWO doors on one layout — `h.buf` from outside the type and a bare `self.buf`
from inside a method — for the reason `declared-field-serves-the-roster` states about the bare spelling:
both mint their value at the same place, so one column answers for both.
```maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)
typealias ByteBuffer = __ManagedMemory with Byte

type Holder
	export var buf as ByteBuffer

	export static function create(buf ByteBuffer) returns Self
		return Self{buf: buf}
	end 'create'

	export function size() returns Int
		return self.buf.length()
	end 'size'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return (h.buf.length() + h.size()) as ExitCode
end 'main'
```
```exitcode
10
```

<!-- test: error.a-buffer-alias-at-a-struct-field-refuses-an-array-member -->

The refusal half. `count` is an `Array` member and not a buffer one, so the field is refused it — and refused
it with the BUFFER's roster, which is what says the message answers for the type it is shown about. **This
exact program COMPILED before this fix**, which is the wrong answer no diagnostic reported.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteBuffer = __ManagedMemory with Byte

type Holder
	export var buf as ByteBuffer

	export static function create(buf ByteBuffer) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return h.buf.count() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:15: Unsupported: `__ManagedMemory` member 'count' — shv2 provides length/capacity/get/set/setLength/setByte/byteAt/elementSize/grow/fill/toCString/makeCharFromBytes/append/slice/clear/remove/swap/shiftRight/shiftLeft/createCursor; that list IS the surface, so nothing else is served here
```

<!-- test: error.an-array-alias-at-a-struct-field-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL, and it is the load-bearing one.** `__ManagedMemory with Byte` and
`Array with Byte` intern to the IDENTICAL instance, so this field's `MaxonType` is the very one the case
above declares — the surface cannot be keyed on it, and only the alias NAME tells the two apart. A fix that
derived the bit from the resolved type, or that handed the buffer roster to every alias-typed field, would
accept this program and nothing would report it.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Holder
	export var buf as ByteArray

	export static function create(buf ByteArray) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return h.buf.length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:15: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: an-array-alias-at-a-struct-field-still-serves-count -->

The positive control for the case above — the `Array`-aliased field is still USABLE, so its refusal of
`length` is a surface decision and not a broken field read.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type Holder
	export var buf as ByteArray

	export static function create(buf ByteArray) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return h.buf.count() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-buffer-alias-serves-the-roster-at-a-declared-return -->

The RETURN clause is the second swept position, and it is read through the same one fold
(`ProgramSignatures.declaredReturnSurface`). Nothing about the VALUE says buffer — it is an ordinary
`"hello".toByteArray()` — so the return spelling is doing all the work.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteBuffer = __ManagedMemory with Byte

function makeBuf() returns ByteBuffer
	return "hello".toByteArray()
end 'makeBuf'

function main() returns ExitCode
	return makeBuf().length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.an-array-alias-at-a-declared-return-keeps-the-array-surface -->

The return door's over-acceptance control, for the field control's exact reason: the two return types are one
`MaxonType`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function makeBuf() returns ByteArray
	return "hello".toByteArray()
end 'makeBuf'

function main() returns ExitCode
	return makeBuf().length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:10:19: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-buffer-alias-serves-the-roster-at-a-union-payload -->

The union PAYLOAD is the third swept position (BATCH22's door), and it reaches the same derivation through
`payloadSurfaceOf`. Three positions, one fold — which is what says this is ONE defect and not three.
```maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)
typealias ByteBuffer = __ManagedMemory with Byte

union Held
	mem(m ByteBuffer)
	nothing
end 'Held'

function size(h Held) returns Int
	return match h 'k'
		mem(x) gives x.length()
		nothing gives 0
	end 'k'
end 'size'

function main() returns ExitCode
	let h = Held.mem("hello".toByteArray())
	return size(h) as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: an-inner-buffer-alias-serves-the-roster-at-a-struct-field -->

⭐ **AN ALIAS DECLARED INSIDE THE TYPE, which is the spelling `stdlib/Character.maxon` and
`stdlib/String.maxon` actually write** (`typealias ByteMemory = __ManagedMemory with Byte` inside
`type Character`). It needs NO second key handling, and that is the point: `parseTypeReference`'s
inner-alias arm stamps the swept field type with the BASE-QUALIFIED name (`Holder.Mem`), and
`qualifiedInnerGenericAlias` files the alias in `bufferSurfaceAliases` under that same one string — so the
read-door derivation asks the name it was filed under without needing to know an inner alias exists.
```maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)

type Holder
	typealias Mem = __ManagedMemory with Byte

	export var buf as Mem

	export static function create(buf Mem) returns Self
		return Self{buf: buf}
	end 'create'

	export function size() returns Int
		return self.buf.length()
	end 'size'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return h.size() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-sibling-files-buffer-alias-serves-the-roster-alias-file-first -->

⭐ **THE FILE-ORDER PAIR, which is what proves the cure is a COMPLEMENTARY half and not a replacement.**
**Alias file FIRST**: the alias is already registered when `main.maxon` is swept, so the field type has
resolved to `genericInstance` — the name is gone, the read-door derivation cannot fire, and the sweep's own
TOKEN bit is what carries the surface. Deleting the stored bit in favour of the derivation would leave this
half wrong.
```maxon
// --- file: alias.maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)
typealias ByteBuffer = __ManagedMemory with Byte

export function seed(x ByteBuffer) returns Int
	return x.length()
end 'seed'

// --- file: main.maxon
type Holder
	export var buf as ByteBuffer

	export static function create(buf ByteBuffer) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return (h.buf.length() + seed("hi".toByteArray())) as ExitCode
end 'main'
```
```exitcode
7
```

<!-- test: a-sibling-files-buffer-alias-serves-the-roster-alias-file-last -->

⭐ **The other half — the identical program, its two files declared the other way round.** Now the alias is
NOT yet registered when `main.maxon` is swept, so the field type stays `named("ByteBuffer")`, the stored
token bit is clear, and it is the read-door DERIVATION that carries the surface. Both halves are pinned
because either one alone leaves one order wrong — and a member roster that depends on file order is a wrong
answer in one of the two orders, whichever one that is.
```maxon
// --- file: main.maxon
type Holder
	export var buf as ByteBuffer

	export static function create(buf ByteBuffer) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let h = Holder.create("hello".toByteArray())
	return (h.buf.length() + seed("hi".toByteArray())) as ExitCode
end 'main'

// --- file: alias.maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)
typealias ByteBuffer = __ManagedMemory with Byte

export function seed(x ByteBuffer) returns Int
	return x.length()
end 'seed'
```
```exitcode
7
```

### A buffer alias's surface is FILE-SCOPED, exactly as its instance is (W49)

⭐⭐ **THE TWO SPELLING REGISTRIES ARE WHOLE-PROGRAM NAME SETS, AND A GENERIC ALIAS NAME IS NOT
WHOLE-PROGRAM.** A plain `typealias` is file-local, so two files may each declare `B` and mean different
things; N3 built the whole three-tier resolution
(`ProgramSignatures.scopedGenericAliasInstance`) to answer *"which declaration does THIS file mean?"*. But
`bufferSurfaceAliases` and `bufferElementAliases` are keyed by the bare name with no file in the key, so
`contains("B")` answers *"did ANY file's declaration of `B` spell the buffer?"* — and a contested name is
precisely the case where the files DISAGREE.

**MEASURED, both file orders**: with `typealias B = __ManagedMemory with Byte` in one file and
`typealias B = Array with Int` in another, the SECOND file's own `B` wore the buffer's surface — a parameter
declared `B` refused `count` with the `__ManagedMemory` roster, and `B.create()` routed to the buffer's
two-argument static and reported `E2004 Expected expression but got ')'` against a call the author wrote
correctly. Neither diagnostic named the real cause, and both blamed the line that was right.

⚠ **IT PREDATES THE READ-DOOR DERIVATION ABOVE** — the flat sets and the flat reads are W43's and A2m's —
but it is the same registry, so the two are settled together: the SPELLING is resolved to the reader's own
declaration before either set is asked (`ProgramSignatures.readerScopedAliasName`), through the very tiers
N3 already resolves the INSTANCE by. An uncontested name — every name in the corpus — costs one miss on an
empty map and allocates nothing.

<!-- test: a-contested-alias-takes-each-files-own-buffer-spelling -->

Both halves in one program, so neither can be satisfied by handing everything one surface: `bufferWidth`'s
parameter must reach `length` (its file spelled the buffer) and `arrayWidth`'s must reach `count` (its file
did not), under one alias name.
```maxon
// --- file: buffer.maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)
typealias B = __ManagedMemory with Byte

export function bufferWidth(m B) returns Int
	return m.length()
end 'bufferWidth'

// --- file: main.maxon
typealias Int2 = int(i64.min to i64.max)
typealias B = Array with Int2

function arrayWidth(xs B) returns Int2
	return xs.count()
end 'arrayWidth'

function main() returns ExitCode
	var xs = B.create()
	xs.push(7 as Int2)
	return (bufferWidth("hello".toByteArray()) + arrayWidth(xs)) as ExitCode
end 'main'
```
```exitcode
6
```

<!-- test: a-contested-alias-takes-each-files-own-buffer-spelling-at-a-struct-field -->

⭐ **THE FIELD DOOR'S HALF, WHICH TAKES A DIFFERENT ROUTE TO THE SAME ANSWER AND SO NEEDS ITS OWN CASE.** A
contested name has its recorded field type REWRITTEN to a per-instance mint
(`resolveRecordedGenericAliasTypes`), and the read-door derivation then asks the registries for that mint —
so the surface reaches this position only if `recordGenericAliasContest` filed each mint under the spelling
*its own* declarations wrote, rather than under the contested name's union. **MEASURED before that was so,
in both file orders**: `b.items.count()`, on a field of the `Array` kind, was refused with the buffer's
roster.
```maxon
// --- file: buffer.maxon
typealias Byte = int(0 to u8.max)
typealias Int = int(i64.min to i64.max)
typealias B = __ManagedMemory with Byte

type Holder
	export var buf as B

	export static function create(buf B) returns Self
		return Self{buf: buf}
	end 'create'
end 'Holder'

export function seed() returns Int
	let h = Holder.create("hello".toByteArray())
	return h.buf.length()
end 'seed'

// --- file: main.maxon
typealias Int2 = int(i64.min to i64.max)
typealias B = Array with Int2
typealias Nums = Array with Int2

type Bag
	export var items as B

	export static function create(items Nums) returns Self
		return Self{items: items}
	end 'create'
end 'Bag'

function main() returns ExitCode
	var xs = Nums.create()
	xs.push(7 as Int2)
	let b = Bag.create(xs)
	return (seed() + b.items.count()) as ExitCode
end 'main'
```
```exitcode
6
```

### A GENERIC TYPE ARGUMENT — the fourth declared position, and the only one that never had a word

⭐⭐ A return type, a struct field and a union payload each carry their pre-erasure spelling because
`__ManagedMemory` and `ByteArray` resolve to the SAME `genericInstance`. A generic type ARGUMENT is the
fourth position where that is true, and it is the one nothing recorded: `Cell with __ManagedMemory` and
`Cell with ByteArray` are one `GenericInstanceId`, so a `returns U` read through either receiver came back
identical — and `substitutedInstanceArg`, the reader every substituted slot goes through, has no spelling
left to hand on.

⇒ The spelling rides the VALUE (`Parser.bufferSurfaceElements`, *argument 0 of this value's instance was
spelled the buffer*) and crosses the instance wherever the TYPE does. Each pair below is the same program
in the two spellings, because a rule about a roster is only pinned by BOTH directions: the buffer spelling
must reach `length`, and the `ByteArray` spelling must still be refused it and still answer `count`.

<!-- test: a-generic-arguments-buffer-spelling-reaches-a-substituted-return -->

The base case, and it needs two things at once: the FACTORY has to stamp the spelling the call site wrote
(`gid` cannot answer, and `BufCell.make(…)` mints a value with no declared type to read), and the
`returns U` has to inherit it from the argument the receiver's instance binds.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

typealias BufCell = Cell with __ManagedMemory

function main() returns ExitCode
	let c = BufCell.make("hello".toByteArray())
	return c.get().length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.a-generic-arguments-array-spelling-keeps-the-array-surface -->

⭐⭐ **THE OVER-ACCEPTANCE CONTROL, and the whole reason the fact may not live on the instance.** This is
the identical program at the identical `GenericInstanceId`; only the ARGUMENT's spelling differs, and it
alone decides the roster.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BytesCell = Cell with ByteArray

function main() returns ExitCode
	let c = BytesCell.make("hello".toByteArray())
	return c.get().length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:20:17: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-byte-array-generic-argument-still-serves-count -->

The refusal's positive half — the `Array` roster is not merely withheld from the other spelling, it is
served to this one.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BytesCell = Cell with ByteArray

function main() returns ExitCode
	let c = BytesCell.make("hello".toByteArray())
	return c.get().count() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: a-declared-parameter-carries-the-argument-spelling-into-a-substituted-return -->

The receiver's mark reaching the call from a DECLARED position rather than from the factory — a parameter
typed with the alias, in a function that never sees where the value was built.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

typealias BufCell = Cell with __ManagedMemory

function peek(c BufCell) returns ExitCode
	return c.get().length() as ExitCode
end 'peek'

function main() returns ExitCode
	let c = BufCell.make("hello".toByteArray())
	return peek(c)
end 'main'
```
```exitcode
5
```

<!-- test: a-generic-field-read-carries-the-argument-spelling -->

The FIELD door, which is the same crossing one op earlier: `var slot as U` names the buffer nowhere, so a
declaration-side surface can never answer for it, and the read has to take the receiver's argument spelling.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

typealias BufCell = Cell with __ManagedMemory

function main() returns ExitCode
	let c = BufCell.make("hello".toByteArray())
	return c.slot.length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: an-inner-alias-return-carries-the-argument-spelling-one-hop-further -->

⭐⭐ **TWO HOPS, WHICH IS THE SHAPE A `for m in bufs` OVER A CORPUS `Array` IS.** `inner()` returns the
body's own `typealias TCell = Cell with T`, so the argument lands at the RESULT's own argument 0 rather than
being the result — the value's ELEMENTS are buffers — and `get()` on THAT value takes the first rule one hop
later. `createIterator()` / `current()` is this program with the names changed.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

type Holder uses T
	typealias TCell = Cell with T
	export var cell as TCell

	export static function make(cell TCell) returns Self
		return Self{cell: cell}
	end 'make'

	export function inner() returns TCell
		return cell
	end 'inner'
end 'Holder'

typealias BufCell = Cell with __ManagedMemory
typealias BufHolder = Holder with __ManagedMemory

function main() returns ExitCode
	let h = BufHolder.make(BufCell.make("hello".toByteArray()))
	return h.inner().get().length() as ExitCode
end 'main'
```
```exitcode
5
```

<!-- test: error.an-inner-alias-return-over-a-byte-array-keeps-the-array-surface -->

The two-hop over-acceptance control. Both hops are the same one `GenericInstanceId` pair as the one-hop
case, so a rule that leaked at either of them would serve the buffer's roster here.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

type Holder uses T
	typealias TCell = Cell with T
	export var cell as TCell

	export static function make(cell TCell) returns Self
		return Self{cell: cell}
	end 'make'

	export function inner() returns TCell
		return cell
	end 'inner'
end 'Holder'

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BufCell = Cell with ByteArray
typealias BufHolder = Holder with ByteArray

function main() returns ExitCode
	let h = BufHolder.make(BufCell.make("hello".toByteArray()))
	return h.inner().get().length() as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:34:25: Unsupported: `Array` member 'length' — P1.7 provides managed/get/set/first/count/push/resize/append/appendMemory; that list IS the surface, so nothing else is served here
```

<!-- test: a-two-hop-byte-array-argument-still-serves-count -->

And its positive half, so the two-hop refusal is a roster boundary rather than a lost value.
```maxon
type Cell uses U
	export var slot as U

	export static function make(slot U) returns Self
		return Self{slot: slot}
	end 'make'

	export function get() returns U
		return slot
	end 'get'
end 'Cell'

type Holder uses T
	typealias TCell = Cell with T
	export var cell as TCell

	export static function make(cell TCell) returns Self
		return Self{cell: cell}
	end 'make'

	export function inner() returns TCell
		return cell
	end 'inner'
end 'Holder'

typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte
typealias BufCell = Cell with ByteArray
typealias BufHolder = Holder with ByteArray

function main() returns ExitCode
	let h = BufHolder.make(BufCell.make("hello".toByteArray()))
	return h.inner().get().count() as ExitCode
end 'main'
```
```exitcode
5
```

## The two buffer members the corpus keeps to itself

<!-- test: error.buffer-toCString-is-stdlib-only -->
### `toCString()` is STDLIB-ONLY

⭐⭐ **A `cstring` IS A RAW ADDRESS WITH NO OWNER, AND THAT IS THE TYPE'S CONTRACT RATHER THAN A GAP** —
no length, no capacity, no refcount, and no drop that could reclaim it. `__mm_to_cstring` hands the
receiver's own buffer back when `buffer[length]` is already 0 and COPIES otherwise, and the copy is the
half nothing owns. A zero-copy view is exactly the receiver whose next byte belongs to its parent, so a
program reaching this door through `arr.managed` takes the copy path on ordinary input: measured on the
program below, **exit 101** — the leak gate — where the `.length()` control on the same slice exits 0.

⇒ The member stays on the buffer roster, because the corpus DOES call it (`stdlib/String.maxon:195`'s
`cstr()`), and the visibility that declaration carries is enforced the only way shv2 can enforce it: on
the file's physical location, exactly as the four stdlib-only `String` byte doors are
(`Parser.requireStdlibOnlyStringMethod`). The ownership question belongs to the rung that gives `cstring`
a producer and a consumer; until then no user program can be the one that asks it.
```maxon
function main() returns ExitCode
	let bytes = "abcd".toByteArray()
	let sub = try bytes.slice(0, endIndex: 2) otherwise panic("slice: 0..2 of a length-4 array")
	let p = sub.managed.toCString()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:22: Unsupported: `__ManagedMemory` member 'toCString' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `toCString` hands back a raw address with no length, no owner and no refcount, so the copy it makes when the receiver's bytes are not already NUL-terminated is reclaimed by nothing; `makeCharFromBytes` trusts its caller for `pos + len <= length()` and reads off the end of the buffer otherwise. User code reaches a buffer's bytes through `byteAt`, which is bounds-checked and throwing
```

<!-- test: error.buffer-makeCharFromBytes-is-stdlib-only -->
### `makeCharFromBytes(pos, len)` is STDLIB-ONLY

The same gate, and the second member behind it. Its corpus declaration is a `module function`
(`stdlib/String.maxon:301`) whose stated contract is *"callers must guarantee `pos + len <=
byteLength()`"* — a PRECONDITION, so the graph it forwards to (`__char_at`, the one door a `Character` is
born through) copies the window without checking it. The reference compiler does check, and refuses with
`__ManagedMemory: byte index out of bounds`; shv2's `__char_at` is shared with `for c in s`, whose window
comes from the segmenter and is in range by construction, so the check does not belong inside it. Handed
a window from USER code the copy walks off the buffer: measured on the program below, **0xC0000005**.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.setLength(2) otherwise return 2
	let c = mm.makeCharFromBytes(0, len: 1000000000)
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:5:13: Unsupported: `__ManagedMemory` member 'makeCharFromBytes' — it is STDLIB-ONLY (`module function` in `stdlib/String.maxon`, which the reference compiler refuses to user code as a not-exported error) and this file is not under `stdlib/`. `toCString` hands back a raw address with no length, no owner and no refcount, so the copy it makes when the receiver's bytes are not already NUL-terminated is reclaimed by nothing; `makeCharFromBytes` trusts its caller for `pos + len <= length()` and reads off the end of the buffer otherwise. User code reaches a buffer's bytes through `byteAt`, which is bounds-checked and throwing
```

## The FUSED Record Path Is A Whitelist Of MEMBERS Whose Safety Argument Is About The RECORD (W194)

⛔⛔ **`managed.append(x)` INSIDE AN `extension Array` PANICS THE COMPILER, AND `self.managed.append(x)` —
THE SAME OPERATION ON THE SAME RECEIVER — RUNS.** Measured on `W194`'s base:

    managed.append(managed)              panic at LayoutDescriptor.maxon:571: primitiveTypeByteSize:
                                         a `typeParameter`'s size is a runtime layout-descriptor read,
                                         not a compile-time constant
    self.managed.append(self.managed)    exit 4
    a.managed.append(b.managed)          exit 3   (concrete, from outside)

⭐ **THE DIFFERENCE IS THE ROUTE, NOT THE ELEMENT.** `fusedManagedMemberTakesTheRecord` lists `append`, so
the bare spelling takes `dispatchMethodOnBinding`'s `inlineManagedServesTheRecord` fast path, which hands
`dispatchArrayMethod` the receiver's own record **under the synthesized BYTE instance**. The other two
spellings carry the receiver's real instance. `arrayAppendArgAdmits` then asks
`containerElementIsOpaque` of the BYTE instance — false, bytes are trivial — falls through to
`sameTrivialArrayShape`, and asks `arrayElementSize` of the ARGUMENT, whose element is the extension's
opaque type parameter. `trivialElementSlot` → `primitiveTypeByteSize(typeParameter)` → panic.

⭐⭐ **THE CURE IS THAT THE FAST PATH STOPS LYING ABOUT THE RECEIVER, NOT THAT IT TURNS ANYONE AWAY.** The
door now hands each wrapper its OWN buffer-surface instance — the compiler's synthesized byte buffer for a
`String` or a `Character`, the array's own instance for an `Array`, and `Array with T` built from the
receiver's element for a `Vector` — which is the same answer the value door retypes its minted surface to,
so the two spellings of one field genuinely *"answer to one element rule"* instead of merely claiming to.
⛔ W194 first tried the other cure, ROUTING every non-byte record to the value door. It was correct and it
cost **1.65–1.9x on `Array.hash`**: that body is not compiler-served, it loops `byteAt` once per byte, and
every iteration then paid a refcount pair. Same three members fixed; nothing emitted.

⭐⭐ **THE LIST'S OWN CENSUS SAYS WHY, AND IT IS THIS PROJECT'S SIGNATURE BUG.** The header justifies the
`append` entry with *"reads both records' `@24` and ABORTS on a mismatch, **which two String records pass
(1 == 1)**"*. That argument is about a **byte record**. But the gate that selects the fast path is
`conformsToBuiltinManagedWrapper`, which admits `Array` and `Vector` too — whose `@24` is 8, not 1, and
whose element may be a type parameter. **The whitelist is keyed on the MEMBER; its safety is keyed on the
RECORD; and nothing checked that the two agree.**

⚠ `append` is the entry that BITES, not the only one mis-served: the header's own census marks
`makeCharFromBytes` as *"the ONE ENTRY THAT READS THE SEVENTH SLOT"* (`@48`), which on a plain 48-byte
`Array` record is eight bytes past the end. That door is unreached today only because the member is
refused to user code and its single caller is `String`'s — a REACHABILITY argument, not a structural one.

⛔⛔ **AND A THIRD ENTRY, FOUND AT THIS RUNG'S REVIEW AND MEASURED AS A *LEAK* RATHER THAN A STOP —
`setByte`.** Its arm asks `requireBufferBytesAreNotAPointer` (E3110, "may a raw byte be written into this
element?") and `requireRawByteWriteFitsItsSlot` (E3118, "does the write fit the stride?"), and BOTH read
the **giid**, not the record. Handed the synthesized byte instance they answer about a `Byte` — trivial,
one byte wide — whatever the receiver's real element is. On this rung's RED baseline this program
**COMPILED CLEAN and EXITED 101, a memory leak**, because the byte landed in a `String` POINTER slot that
E3110 exists to refuse:

```text
typealias Strs = Array with String

extension Array
	export function poke()
		try managed.setByte(0, 65) otherwise ignore
	end 'poke'
end 'Array'

function main() returns ExitCode
	var a = Strs.create()
	a.push("hi")
	a.poke()
	return 0 as ExitCode
end 'main'
```

The identical body written `self.managed.setByte(…)` — the value path, carrying the real instance —
did not compile at all on that same baseline. **One member, two spellings, a leak and a stop**: exactly
the asymmetry the three cases below pin for `append`, one member over and one severity worse.

✅ **THE REAL INSTANCE CLOSES THE LEAK, AND WHAT REPLACES IT DEPENDS ON WHETHER THE ELEMENT IS KNOWN.**
Measured on the reworked tip, all three spellings of that write:

    a.managed.setByte(0, 65)   on `Array with String`, concrete   E3110 at 6:16 — "a managed element is
                                                                  stored as a POINTER … writing them
                                                                  corrupts it"
    managed.setByte(0, 65)     bare, inside `extension Array`     panic … primitiveTypeByteSize
    self.managed.setByte(…)    inside `extension Array`           the SAME panic, same frames below

⚠ **A BARE FUSED `setByte` ON A NON-BYTE RECORD IS ALWAYS THE OPAQUE CASE**, because the only bodies that
can spell it are the declaration's own and its extensions, where the element IS the type parameter — so
E3110 is unreachable from that door by construction, and a generic body meets the panic instead. **That
panic is PRE-EXISTING on the value path and present identically on the merge base**, which is why
replacing it with a positioned refusal — or with a deferral of the stride question to instantiation — is a
row of its own that needs a spec to choose between them. **This rung changed which ELEMENT the door on
the other side is asked about, never what it asks** — so no case here may be read as fixing that.

<!-- test: the-fused-append-path-agrees-with-the-value-path -->
**THE SUBJECT.** The bare, fused spelling must answer what the value spelling answers. Two elements
appended to themselves is four.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

extension Array
	export function doubled() returns Int
		self.managed.append(managed)
		return managed.length() as Int
	end 'doubled'
end 'Array'

function main() returns ExitCode
	var a = Ints.create()
	a.push(1)
	a.push(2)
	return a.doubled() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: the-bare-fused-append-answers-the-same -->
⭐⭐ **THE CASE THE RUNG EXISTS FOR — the receiver written BARE, which is the spelling that takes the fused
record path.** On the base this program did not produce a wrong answer, it **killed the compiler**; the
control above compiled and ran to 4 the whole time, which is what says the operation was always
expressible and only this route could not carry it.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

extension Array
	export function doubled() returns Int
		managed.append(managed)
		return managed.length() as Int
	end 'doubled'
end 'Array'

function main() returns ExitCode
	var a = Ints.create()
	a.push(1)
	a.push(2)
	return a.doubled() as ExitCode
end 'main'
```
```exitcode
4
```

<!-- test: the-other-fused-readers-already-agreed-and-must-go-on-agreeing -->
⭐⭐ **THE NEUTRALITY CONTROL, AND IT IS WHAT BOUNDS THE BLAST RADIUS.** `length` and `byteAt` are on the
same whitelist and were MEASURED answering identically through both routes on the base
(`bareLen=2 selfLen=2 bareByteAt=7 selfByteAt=7`) — so `append` was the only entry the byte-instance
lie corrupted, and a cure that moves the route must leave these two where they were. `byteAt` reads the
first byte of the first element, which on a little-endian machine is the low byte of `7`.

⛔ **IT PRINTS THE FOUR VALUES RATHER THAN PACKING THEM INTO AN EXIT CODE, AND THAT IS NOT A STYLE
CHOICE.** The first version of this case returned `2277` — all four answers in one number, which is
exactly what a control of this shape wants. It passed on x64-windows and **FAILED ON x64-linux AND
wasm32-wasi**, caught by the cross-target gate: an exit status is EIGHT BITS off Windows, so any
answer above 255 is unrepresentable and the lane read `1`. The compiler was right on all three
lanes — the other two cases in this section passed everywhere and this one COMPILED and RAN — the
ENCODING was the defect. **A case that must carry more than one small number carries it on stdout.**
```maxon
typealias Int = int(i64.min to i64.max)
typealias Ints = Array with Int

extension Array
	export function survey() returns Int
		let bare = managed.length() as Int
		let viaSelf = self.managed.length() as Int
		let bareByte = (try managed.byteAt(0) otherwise 255) as Int
		let selfByte = (try self.managed.byteAt(0) otherwise 255) as Int
		print("len={bare}/{viaSelf} byte={bareByte}/{selfByte}\n")
		return 0
	end 'survey'
end 'Array'

function main() returns ExitCode
	var a = Ints.create()
	a.push(7)
	a.push(9)
	return a.survey() as ExitCode
end 'main'
```
```exitcode
0
```
```stdout
len=2/2 byte=7/7
```
