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

<!-- disabled-test: array-via-methods -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: array-get-set -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: array-slice -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: array-insert-remove -->
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7+: ManagedMemory intrinsics (.cstr()/String.init/fromCString) -->
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

<!-- disabled-test: empty-bstring-push -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: empty-string-bytes-push -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: array-literal -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: array-growth -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-get-oob -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-set-oob -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-setlength-exceeds-capacity -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-byte-oob -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-slice-oob -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-valid-operations -->
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: bounds-negative-index -->
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: shrink-setlength-direct-no-leak -->
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: clear-then-resize-scalar-reads-zeros -->
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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

<!-- disabled-test: remove-then-resize-scalar-reads-zero -->
<!-- P1.7: Array / ManagedMemory -->
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
<!-- P1.7: Array / ManagedMemory -->
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
