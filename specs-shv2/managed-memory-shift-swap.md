---
feature: managed-memory-shift-swap
status: stable
keywords: [managed-memory, buffer, swap, shiftLeft, shiftRight, remove, ownership, sort]
category: dev
---

# `__ManagedMemory` — `remove`, `swap`, `shiftLeft`, `shiftRight`

## Documentation

Four `__ManagedMemory` members that RELOCATE elements inside the buffer rather than
producing or consuming them. `stdlib/Array.maxon` builds `pop`/`remove`/`insert` out of
the first three and `stdlib/helpers/sort/smallSort.maxon` builds every sort out of the
fourth, so the whole growable-array surface rests on them.

- `remove(index)` returns Element — load the element at `index`, slide the tail down one
  slot, ERASE the slot the slide vacated at the top, and decrement `length`. Bound is
  `length`; throws `indexOutOfBounds`. **No teardown runs**: the element is HANDED OUT, so
  its `+1` leaves with it. The vacated top slot must still be erased, because it holds a
  duplicate of a pointer this record no longer owns (see THE CAPACITY-SLOT INVARIANT in
  `stdlib/Internals.maxon`).
- `shiftRight(index, count)` — slide the `count` elements at `[index, index+count)` UP one
  slot, then ZERO the slot at `index` the slide vacated. The zeroing is what lets the
  caller's following `set(index, …)` overwrite rather than decref the stale duplicate the
  slide left there. Bound is **CAPACITY**, not length; throws `shiftOutOfBounds`.
- `shiftLeft(index, count)` — the counterpart, sliding `[index, index+count)` DOWN one slot.
  It zeroes nothing: the caller owns the trailing slot. `index >= 1`, `index + count <=
  capacity`; throws `shiftOutOfBounds`.
- `swap(i, j:)` — exchange the raw `element_size` bytes of two slots with **NO refcount
  traffic**. Bound is `length`; throws `indexOutOfBounds`.

### Why `swap` cannot be `get` + `set`

⭐⭐ **A SWAP MOVES OWNERSHIP BETWEEN SLOTS; IT NEITHER CREATES NOR DESTROYS ONE.** Both
occupants stay live inside the container and both refcounts are unchanged. Routing it
through `get`+`set` instead makes `set` do what `set` correctly does for an OVERWRITE —
release the displaced occupant — while a borrowed copy of that very element is still
waiting to be stored into the other slot. The element is freed mid-swap and the other slot
is left pointing at reclaimed memory: the sort-of-managed-elements use-after-free named at
`stdlib/helpers/sort/smallSort.maxon:24-31`.

## Tests

<!-- test: remove-from-a-buffer-of-bytes -->
`remove` on the BUFFER surface, whose bound is the same `length` the `Array` spelling uses.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	let gone = try arr.managed.remove(1) otherwise return 99
	return gone + arr.managed.length()
end 'main'
```
```exitcode
22
```

<!-- test: remove-out-of-range-is-index-out-of-bounds -->
The bound is `length`, so an index at `length` is refused even though the slot is inside
capacity.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.reserve(16)
	let gone = try arr.managed.remove(1) otherwise 7
	return gone
end 'main'
```
```exitcode
7
```

<!-- test: remove-hands-the-string-out-and-erases-the-slot -->
The removed element is HANDED OUT, so nothing is torn down here — and the slot the tail
slide vacated still holds a duplicate of a pointer this record no longer owns, so it must
be erased. A leak or a double free is the failure; the run is leak-gated.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("first heap string long enough to require an allocation")
	xs.push("second heap string long enough to require an allocation")
	xs.push("third heap string long enough to require an allocation")
	let gone = try xs.managed.remove(0) otherwise return 99
	let first = try xs.get(0) otherwise "missing"
	let second = try xs.get(1) otherwise "missing"
	print("{gone}\n")
	print("{first}\n")
	print("{second}\n")
	return xs.managed.length()
end 'main'
```
```stdout
first heap string long enough to require an allocation
second heap string long enough to require an allocation
third heap string long enough to require an allocation
```
```exitcode
2
```

<!-- test: shift-right-opens-a-hole-and-zeroes-it -->
`shiftRight` is bounded by CAPACITY, so a caller reserves first and publishes the new
length afterwards — which is exactly what `Array.insert` does.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.reserve(4)
	try arr.managed.shiftRight(1, 2) otherwise return 98
	try arr.managed.set(1, 99) otherwise return 97
	try arr.managed.setLength(4) otherwise return 96
	var total = 0
	for v in arr 'sum'
		total = total + v
	end 'sum'
	return total
end 'main'
```
```exitcode
159
```

<!-- test: shift-left-closes-the-hole -->
`shiftLeft` zeroes nothing — the caller owns the trailing slot and republishes the length.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	try arr.managed.shiftLeft(2, 1) otherwise return 98
	try arr.managed.setLength(2) otherwise return 97
	var total = 0
	for v in arr 'sum'
		total = total + v
	end 'sum'
	return total
end 'main'
```
```exitcode
40
```

<!-- test: shift-right-past-capacity-is-shift-out-of-bounds -->
`index + count > capacity` is refused, and the complaint is `shiftOutOfBounds` rather than
`indexOutOfBounds`: a shift blames the RANGE it was handed, not one index. The bound is
INCLUSIVE — `index + count == capacity` is the largest range that fits — so this asks for
exactly one slot more.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	try arr.managed.shiftRight(1, arr.managed.capacity()) otherwise return 5
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: shift-left-at-index-zero-is-shift-out-of-bounds -->
`shiftLeft` needs `index >= 1`, because `index - 1` is where the first element lands.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	try arr.managed.shiftLeft(0, 2) otherwise return 5
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: shift-right-over-strings-does-not-double-free -->
⭐⭐ **THE SLIDE LEAVES A DUPLICATE POINTER AT `index`, AND ZEROING IT IS THE WHOLE OF WHY
THIS IS NOT A DOUBLE FREE.** After the shift, slot 0 must read NULL and slots 1 and 2 must
hold the two strings that were at 0 and 1 — so publishing the length makes slot 0 an EMPTY
slot (which `get` reports as `emptySlot`, not as a copy of `alpha`) and teardown destroys
each string exactly once. Without the zeroing slot 0 keeps a duplicate of `alpha`'s
pointer, teardown decrefs it twice, and the run dies or exits 101.

⚠ This stops one step short of `Array.insert`'s body, because the step that FILLS the hole
is `managed.set` and shv2 refuses that outright for a managed element (**E3109**,
`requireBufferSetOwnsWhatItStores` — a slot above `length` is owned by nobody). Leaving the
hole empty tests exactly the half this member owns.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	xs.reserve(3)
	try xs.managed.shiftRight(0, 2) otherwise return 98
	try xs.managed.setLength(3) otherwise return 97
	let hole = try xs.get(0) otherwise "the hole is empty"
	let moved = try xs.get(1) otherwise "missing"
	let alsoMoved = try xs.get(2) otherwise "missing"
	print("{hole}\n")
	print("{moved}\n")
	print("{alsoMoved}\n")
	return xs.count()
end 'main'
```
```stdout
the hole is empty
alpha heap string long enough to require an allocation
beta heap string long enough to require an allocation
```
```exitcode
3
```

<!-- test: swap-two-byte-slots -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	try arr.managed.swap(0, j: 2) otherwise return 99
	return (try arr.get(0) otherwise 0) * 2 + (try arr.get(2) otherwise 0)
end 'main'
```
```exitcode
70
```

<!-- test: swap-a-slot-with-itself-is-a-no-op -->
`i == j` returns before touching the buffer. Under a byte-exchange loop that read both
slots first it would still be correct; under a get+set implementation it would decref the
element and store the pointer it just released.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("only heap string long enough to require an allocation")
	try xs.managed.swap(0, j: 0) otherwise return 99
	let only = try xs.get(0) otherwise "missing"
	print("{only}\n")
	return xs.count()
end 'main'
```
```stdout
only heap string long enough to require an allocation
```
```exitcode
1
```

<!-- test: swap-out-of-range-is-index-out-of-bounds -->
Both indices are bounded by `length`, not capacity.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.reserve(16)
	try arr.managed.swap(0, j: 4) otherwise return 5
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: swap-managed-elements-keeps-both-alive -->
⭐⭐ **THE CASE THE CORPUS COMMENT NAMES.** Under a `get`+`set` implementation the `set`
into slot 0 releases the string it displaces while the borrowed copy of that string is
still in flight toward slot 1 — so slot 1 ends up holding reclaimed memory and printing it
is a use-after-free. With a raw byte exchange both refcounts are untouched and both
strings print. A wrong answer here is a corrupted string; a double free is exit 101.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	try xs.managed.swap(0, j: 1) otherwise return 99
	let first = try xs.get(0) otherwise "missing"
	let second = try xs.get(1) otherwise "missing"
	print("{first}\n")
	print("{second}\n")
	return xs.count()
end 'main'
```
```stdout
beta heap string long enough to require an allocation
alpha heap string long enough to require an allocation
```
```exitcode
2
```

<!-- test: swapping-managed-elements-through-a-whole-reversal -->
A reversal is the sort's inner move, and it runs the swap once per pair. Every element is
relocated and none is created or destroyed, so the element count at teardown must be
exactly what was pushed — the leak gate is the assertion.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("one heap string long enough to require an allocation")
	xs.push("two heap string long enough to require an allocation")
	xs.push("three heap string long enough to require an allocation")
	xs.push("four heap string long enough to require an allocation")

	var lo = 0
	var hi = xs.count() - 1
	while lo < hi 'reverse'
		try xs.managed.swap(lo, j: hi) otherwise return 99
		lo = lo + 1
		hi = hi - 1
	end 'reverse'

	for s in xs 'each'
		print("{s}\n")
	end 'each'
	return xs.count()
end 'main'
```
```stdout
four heap string long enough to require an allocation
three heap string long enough to require an allocation
two heap string long enough to require an allocation
one heap string long enough to require an allocation
```
```exitcode
4
```

<!-- test: swap-bit-packed-bool-slots -->
A `bool` element is SUB-BYTE PACKED (`element_size@24` is `-1`), so the two slots can share
one byte and a whole-byte exchange would be wrong. Both fields are read before either is
written.
```maxon
typealias Flags = Array with bool

function main() returns ExitCode
	var flags = Flags.create()
	flags.push(true)
	flags.push(false)
	flags.push(false)
	try flags.managed.swap(0, j: 1) otherwise return 99
	let atZero = try flags.get(0) otherwise true
	let atOne = try flags.get(1) otherwise false
	var total = 0
	if atZero 'zero'
		total = total + 1
	end 'zero'
	if atOne 'one'
		total = total + 2
	end 'one'
	return total
end 'main'
```
```exitcode
2
```
