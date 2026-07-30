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
