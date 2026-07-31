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

<!-- disabled-test: shrink-managed-elements-no-leak -->
<!-- E3106 `requireArrayResizeHasZeroElement` — `resize()` on a managed-element array is refused outright, SHRINK included: the check cannot see that the new length is smaller, and a grown slot would hold no element -->
Push three heap strings, then `resize(1)`. The two dropped strings must be freed.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("first heap string long enough to require an allocation")
	xs.push("second heap string long enough to require an allocation")
	xs.push("third heap string long enough to require an allocation")
	xs.resize(1)
	return xs.count() as ExitCode
end 'main'
```
```exitcode
1
```

<!-- disabled-test: shrink-struct-elements-no-leak -->
<!-- E3106 `requireArrayResizeHasZeroElement` — `resize()` on a managed-element array is refused outright, SHRINK included: the check cannot see that the new length is smaller, and a grown slot would hold no element -->
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
	boxes.resize(2)
	return boxes.count() as ExitCode
end 'main'
```
```exitcode
2
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

<!-- disabled-test: shrink-to-zero-no-leak -->
<!-- E3106 `requireArrayResizeHasZeroElement` — `resize()` on a managed-element array is refused outright, SHRINK included: the check cannot see that the new length is smaller, and a grown slot would hold no element -->
Shrinking all the way to zero frees every element (equivalent to `clear`).
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("only string payload long enough to need a heap allocation")
	xs.push("other string payload long enough to need a heap allocation")
	xs.resize(0)
	return xs.count() as ExitCode
end 'main'
```
```exitcode
0
```

<!-- disabled-test: shrink-then-regrow-empty-slots -->
<!-- E3106 `requireArrayResizeHasZeroElement` — `resize()` on a managed-element array is refused outright, SHRINK included: the check cannot see that the new length is smaller, and a grown slot would hold no element -->
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
	xs.resize(1)
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

Every operation that VACATES a slot erases it on the way out: `clear`, `remove`/`pop`,
and a shrinking `resize`. Without that, growing back over a vacated slot re-exposes
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

<!-- disabled-test: clear-then-resize-managed-no-double-free -->
<!-- TWO independent blockers, and the first hides the second. (1) E3106 `requireArrayResizeHasZeroElement` — `a.resize(4)` on a managed-element array is refused outright, exactly as its `shrink-then-resize-managed-no-double-free` sibling below. (2) With that line deleted, MEASURED: `error E2015: Unsupported: String method 'count'` — P1.2 wave D provides only `append`/`byteLength`. Fixing E3106 alone will NOT make this test run. The for-in loops in it do compile (measured separately) -->
`clear()` RELEASES the elements; `resize()` back over those slots must not restore the
length over the dead pointers, or the array's destructor decrefs each of them a second
time. Every regrown slot must read as EMPTY.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	for i in 0 upto 4 'fill'
		a.push("value-{i} padded out so this string needs a heap allocation")
	end 'fill'
	a.clear()
	a.resize(4)

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

<!-- disabled-test: shrink-then-resize-managed-no-double-free -->
<!-- E3106 `requireArrayResizeHasZeroElement` — `resize()` on a managed-element array is refused outright, SHRINK included: the check cannot see that the new length is smaller, and a grown slot would hold no element -->
The same shape through the SHRINK path rather than `clear`: `resize(1)` releases the
two dropped strings, `resize(3)` grows back over their slots. The dropped pointers must
not reappear.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var a = StrArray.create()
	a.push("alpha payload long enough to need a heap allocation here")
	a.push("beta payload long enough to need a heap allocation here")
	a.push("gamma payload long enough to need a heap allocation here")
	a.resize(1)
	a.resize(3)

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

<!-- disabled-test: pop-then-resize-managed-no-double-free -->
<!-- E3106 `requireArrayResizeHasZeroElement` — `resize()` on a managed-element array is refused outright, SHRINK included: the check cannot see that the new length is smaller, and a grown slot would hold no element -->
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
	a.resize(2)

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

<!-- disabled-test: clear-then-resize-bool-reads-false -->
<!-- `Array with bool` element-type fidelity — `try b.get(i) otherwise true` comes back typed `int`, so `if v` is E3005 "'if' requires a bool condition, got 'int'" -->
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
error E2015: <fragment>:4:8: Unsupported: `Array` method 'setLength' — P1.7 slice 1 provides create/push/get/set/count/capacity/isEmpty/reserve/resize/first/last/pop/clear/insert/remove and slice 4 adds slice/clone/append; the rest (map/contains/…) arrive later
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
error E2015: <fragment>:7:8: Unsupported: `Array` method 'setLength' — P1.7 slice 1 provides create/push/get/set/count/capacity/isEmpty/reserve/resize/first/last/pop/clear/insert/remove and slice 4 adds slice/clone/append; the rest (map/contains/…) arrive later
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
error E2015: <fragment>:10:12: Unsupported: `Array` method 'setByte' — P1.7 slice 1 provides create/push/get/set/count/capacity/isEmpty/reserve/resize/first/last/pop/clear/insert/remove and slice 4 adds slice/clone/append; the rest (map/contains/…) arrive later
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
`__arr_decref` destroys only `[0, length)`, `push`/`insert`/`append` store AT `length` without destroying the
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
`__arr_set` was length-bounded. The capacity ruling is what made it reachable, which is what put it under
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
`specs/arrays.md` and the whole array corpus are written against a length bound, and `__arr_set` is shared.
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
the managed move-in, not the throw). R4.6 fixes it because `__arr_mem_set` would otherwise have been written
with the identical leak on its first day.

⚠ **IT ASSERTS ONLY THE ARRAY SURFACE, AND THE MISSING HALF IS NOT AN OMISSION.** It asserted both until R4.6
review; the buffer half is now a COMPILE error (E3109 — a managed element cannot reach `managed.set` at all),
which is a strictly stronger guarantee than "the refusal does not leak". The consequence is worth stating
where a sabotage-runner will look: `__arr_mem_set`'s own `emitDestroyRejectedElement` call is now unreachable
from any spelling the front end admits, so no test can redden it. It is kept deliberately — see
`buildArrMemSet` — and this line is why breaking it is silent.
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

⭐⭐ **A BUG R4.6 FOUND AND FIXED IN R4.4's WRITE GUARD.** `__arr_cow_detach` has TWO arms — a buffer that is
not this record's (`capacity < 0`), and one that IS but is being read by a view — and R4.4's guard called the
detach only under a hand-written copy of the FIRST arm (`emitBufferNotOwned`). So a `setByte` through the
OWNER of a viewed buffer wrote straight through the sharing. MEASURED before the fix: this program returned
**198**, the view reading back the owner's 99. The guard now calls `__arr_cow_detach` unconditionally and lets
it answer both arms, which is the gating rule `buildArrCowDetach`'s own header states.
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
review).** `__arr_mem_set` and `__arr_set_byte` reach `__arr_cow_detach` through the one
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
answer. The family reaches the diagnostic THROUGH `isThrowingArrayRuntimeCallee`, which is why it used to
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

⛔ **WHAT THIS DOES *NOT* CLOSE, MEASURED IN REVIEW 2026-07-31 — THE SURFACE FOLLOWS THE VALUE'S PROVENANCE,
NOT THE DECLARED TYPE.** `__ManagedMemory` is a generic ALIAS of `Array with Byte`
(`ProgramSignatures.registerManagedFileType`), so a value bound by a **parameter**, a **struct field** or a
**return type** spelled `__ManagedMemory` carries no buffer mark and takes the `Array` surface — the roster
exactly INVERTED. All three compile, link and RUN today:

```text
function abuse(m __ManagedMemory) returns int      -- push/reserve/resize/insert/pop/first/last/
    m.push(7) … m.count()                          -- remove/clone/isEmpty/count all ACCEPTED, exit 24
function useBuffer(m __ManagedMemory) returns int  -- while the roster's own first member is REFUSED:
    return m.length()                              -- "`Array` method 'length' — … arrive later"
function make() returns __ManagedMemory            -- b.push(7); b.count() ACCEPTED, exit 2
type Box  var buf as __ManagedMemory               -- buf.push(7); buf.count() ACCEPTED, exit 2
```

No live case in `specs-shv2/` writes any of the three (the only one is a `disabled-test` in
`interface-conformance.md`), which is why the producer-based enumeration below did not see them — an
enumeration of *what the corpus calls* cannot bound *what a program may write*. It is not a regression (the
behaviour is R4.4's and predates D11b) and it is not a wrong ANSWER — the record is the same `Array` record,
so `__arr_push` on it is well-defined — but the ruling "the legitimate surface is EXACTLY what the roster
names" is not yet true at those three spellings. Closing it needs a declared-as-the-alias bit carried per
parameter / field / return through `SignatureIndex` (the spelling is gone by the time `bindParameters` sees a
`MaxonType`), which is a rung, not a mark.

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
	try words.append(more) otherwise return 13
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

<!-- test: error.buffer-has-no-push -->

⭐ **THE PROGRAM THE D11 REVIEW RAN: it compiled, linked and exited 0.** `push` is an `Array` method and
the buffer has no member of that name — the two grow differently (`__arr_push` raises the capacity by the
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
error E2015: <fragment>:4:5: Unsupported: `__ManagedMemory` member 'push' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'push' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
```

<!-- test: error.buffer-has-no-remove -->

⭐ **THE SECOND PROGRAM THE D11 REVIEW RAN, and the one the message named outright.** The roster has always
said `remove` "is not built"; the arm accepted it and emitted the `Array`'s length-bounded `__arr_remove`.
Both halves of that are now true at once.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	try mm.remove(0) otherwise return 2
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:9: Unsupported: `__ManagedMemory` member 'remove' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'count' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:9: Unsupported: `__ManagedMemory` member 'managed' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
```

<!-- test: error.buffer-has-no-element-size -->

⚠ **THE CONTROL FOR THE OTHER HALF OF THE SENTENCE.** `elementSize` is a real `__ManagedMemory` member in
both references and it is genuinely NOT BUILT here, so it was already refused — by falling all the way
past the arms. It must keep the same message now that the gate answers first, or the roster's "are not
built" clause would be reachable only for names nothing has ever declared.
```maxon
function main() returns ExitCode
	let mm = try __ManagedMemory.create(4, elementSize: 1) otherwise return 1
	let x = mm.elementSize()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'elementSize' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:17: Unsupported: `__ManagedMemory` member 'pop' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:17: Unsupported: `__ManagedMemory` member 'first' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:17: Unsupported: `__ManagedMemory` member 'last' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:9: Unsupported: `__ManagedMemory` member 'insert' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:5: Unsupported: `__ManagedMemory` member 'reserve' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:5: Unsupported: `__ManagedMemory` member 'resize' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'isEmpty' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:4:13: Unsupported: `__ManagedMemory` member 'clone' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
error E2015: <fragment>:6:15: Unsupported: `__ManagedMemory` member 'count' — R4.4 provides length/capacity/get/set/setLength/setByte/byteAt/grow/append/slice/clear; `elementSize`/`remove`/`swap`/`shiftLeft`/`shiftRight` and the cstring/cursor members (`toCString`, `fromCString`, `createCursor`, `makeCharFromBytes`) are not built — no spec reaches them, and the cstring family needs a `cstring` type shv2 has no producer for
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
