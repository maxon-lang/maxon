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
  `maxon-bin/Compiler/Runtime/ManagedMemoryRuntime.maxon`).
- `shiftRight(index, count)` — RELEASE the occupant of slot `index+count`, slide the `count`
  elements at `[index, index+count)` UP one slot onto it, then ZERO the slot at `index` the
  slide vacated. The zeroing is what lets the caller's following `set(index, …)` overwrite
  rather than decref the stale duplicate the slide left there. Bound is **LENGTH**: the top
  slot it writes must be live, so `index + count < length`; anything else throws
  `shiftOutOfBounds`. Nothing is ever slid past the live length, so a caller that opens a hole
  publishes the longer length FIRST and shifts inside it — which is what `Array.insert` does.
- `shiftLeft(index, count)` — the counterpart, sliding `[index, index+count)` DOWN one slot.
  The occupant of `index - 1` that the slide overwrites is RELEASED first, and the slot
  `index + count - 1` the slide vacated is ZEROED, so no element is leaked or held twice; the
  length is the caller's to republish. Bound is **LENGTH** too: `index >= 1` and
  `index + count <= length`, so a zeroed slot above the live range is never pulled into it;
  anything else throws `shiftOutOfBounds`.
- `swap(i, j:)` — exchange the raw `element_size` bytes of two slots with **NO refcount
  traffic**. Bound is `length`; throws `indexOutOfBounds`.

Both shift range checks treat `index` and `count` as unsigned and compare them against the bound without
forming a sum that can overflow, so a negative argument, or a range whose end is past `i64.max` on either
operand, is `shiftOutOfBounds` rather than a wrapped, passing check. A shift whose range is in bounds and
whose `count` is 0 moves nothing, releases nothing and zeroes nothing.

Every member here is a `__ManagedMemory` member reached through `.managed`, which only a source
file under `stdlib/` or `runtime/` may write (E3185 anywhere else), so every case compiles in the
library tier (`<!-- tier: stdlib -->`).

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
<!-- tier: stdlib -->
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
	return ((gone as Count) + arr.managed.length()) as ExitCode
end 'main'
```
```exitcode
22
```

<!-- test: remove-out-of-range-is-index-out-of-bounds -->
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
`shiftRight` is bounded by LENGTH, so a caller grows the length first and shifts inside it —
which is exactly what `Array.insert` does. Slot 3 is the top slot written: `1 + 2 < 4`.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.resize(4)
	try arr.managed.shiftRight(1, 2) otherwise return 98
	try arr.managed.set(1, 99) otherwise return 97
	var total = 0 as ExitCode
	for v in arr 'sum'
		total = total + (v as ExitCode)
	end 'sum'
	return total
end 'main'
```
```exitcode
159
```

<!-- test: shift-left-closes-the-hole -->
<!-- tier: stdlib -->
`shiftLeft` zeroes the slot its slide vacated, and the caller republishes the length over it.
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
	var total = 0 as ExitCode
	for v in arr 'sum'
		total = total + (v as ExitCode)
	end 'sum'
	return total
end 'main'
```
```exitcode
40
```

<!-- test: shift-right-into-reserved-capacity-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
`index + count >= length` is refused even with capacity to spare, and the complaint is
`shiftOutOfBounds` rather than `indexOutOfBounds`: a shift blames the RANGE it was handed, not one
index. The bound is EXCLUSIVE — `index + count == length - 1` is the largest range that fits — so
this asks for exactly one slot more: slot 2, the first reserved slot past the live length.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.reserve(16)
	try arr.managed.shiftRight(1, 1) otherwise (e) 'refused'
		print("{e.name}\n")
		return 5
	end 'refused'
	return 0
end 'main'
```
```stdout
shiftOutOfBounds
```
```exitcode
5
```

<!-- test: shift-left-at-index-zero-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
⭐⭐ **THE SLIDE LEAVES A DUPLICATE POINTER AT `index`, AND ZEROING IT IS THE WHOLE OF WHY
THIS IS NOT A DOUBLE FREE.** The length is published first, exposing an empty slot 2 for the
shift to land in. After the shift, slot 0 must read NULL and slots 1 and 2 must hold the two
strings that were at 0 and 1 — so slot 0 is an EMPTY slot (which `get` reports as
`emptySlot`, not as a copy of `alpha`) and teardown destroys each string exactly once.
Without the zeroing slot 0 keeps a duplicate of `alpha`'s pointer, teardown decrefs it
twice, and the run dies or exits 101.

⚠ This stops one step short of `Array.insert`'s body, because the step that FILLS the hole
is `managed.set` and the compiler refuses that outright for a managed element (**E3109**,
`requireBufferSetOwnsWhatItStores` — a slot above `length` is owned by nobody). Leaving the
hole empty tests exactly the half this member owns.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	xs.reserve(3)
	try xs.managed.setLength(3) otherwise return 97
	try xs.managed.shiftRight(0, 2) otherwise return 98
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

<!-- test: shift-right-whose-range-end-overflows-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
⭐ **THE RANGE CHECK CANNOT BE WRAPPED.** `index + count` here is past `i64.max`, so a check that adds
the two in signed arithmetic sees a negative end, passes it, and slides memory far outside the buffer. The
check compares without forming a sum that can overflow, so this is `shiftOutOfBounds` like any other range
that reaches past the live length.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var xs = IntegerArray.create()
	xs.push(1)
	xs.push(2)
	xs.push(3)
	xs.push(4)
	try xs.managed.shiftRight(9223372036854775804, 4) otherwise (e) 'refused'
		match e 'why'
			shiftOutOfBounds then print("shiftOutOfBounds\n")
			default panic("shiftRight refused the range with another error")
		end 'why'
		return 0
	end 'refused'
	print("shifted\n")
	return 1
end 'main'
```
```stdout
shiftOutOfBounds
```
```exitcode
0
```

<!-- test: shift-left-over-strings-releases-the-overwritten-slot-and-zeroes-the-tail -->
<!-- tier: stdlib -->
⭐⭐ **A LEFT SLIDE OVERWRITES ONE OCCUPANT AND DUPLICATES ANOTHER, AND THE MEMBER OWNS BOTH.**
`shiftLeft(1, 2)` slides `[1, 3)` down onto `[0, 2)`: slot 0's `alpha` is released before it is
overwritten, and slot 2, whose pointer now also sits in slot 1, is zeroed. The length is the caller's and
stays 3, so slot 2 reads as an empty slot, and teardown releases `beta` and `gamma` once each. Without the
release `alpha` leaks (exit 101); without the zeroing `gamma` is released twice.
```maxon
function main() returns ExitCode
	var xs = ["alpha {40 + 2} heap string one", "beta {40 + 2} heap string two", "gamma {40 + 2} heap string three"]
	try xs.managed.shiftLeft(1, 2) otherwise return 3
	let first = try xs.get(0) otherwise "missing"
	let second = try xs.get(1) otherwise "missing"
	let third = try xs.get(2) otherwise "the tail is empty"
	print("{xs.count()}\n")
	print("{first}\n")
	print("{second}\n")
	print("{third}\n")
	return 0
end 'main'
```
```stdout
3
beta 42 heap string two
gamma 42 heap string three
the tail is empty
```
```exitcode
0
```

<!-- test: shift-right-to-the-live-end-over-strings-does-not-leak -->
<!-- tier: stdlib -->
`index + count == length` would slide the last live element into the first slot past the live range,
where no length owns it. It is refused with `shiftOutOfBounds` even with capacity reserved, and the
refusal moves nothing: every string stays in its slot, teardown releases each once, and the run exits 0,
never 101.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha {40 + 2} heap string one")
	xs.push("beta {40 + 2} heap string two")
	xs.push("gamma {40 + 2} heap string three")
	xs.reserve(4)
	try xs.managed.shiftRight(1, 2) otherwise (e) 'refused'
		match e 'why'
			shiftOutOfBounds then print("shiftOutOfBounds\n")
			default panic("shiftRight refused the live end with another error")
		end 'why'
	end 'refused'
	let first = try xs.get(0) otherwise "missing"
	let second = try xs.get(1) otherwise "missing"
	let third = try xs.get(2) otherwise "missing"
	print("{xs.count()}\n")
	print("{first}\n")
	print("{second}\n")
	print("{third}\n")
	return 0
end 'main'
```
```stdout
shiftOutOfBounds
3
alpha 42 heap string one
beta 42 heap string two
gamma 42 heap string three
```
```exitcode
0
```

<!-- test: shift-right-whose-count-overflows-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
The overflow on the COUNT side: `1 + i64.max` wraps just as `(i64.max - 3) + 4` does above, and the check
refuses it without forming the sum. The refusal moves nothing.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var xs = IntegerArray.create()
	xs.push(1)
	xs.push(2)
	xs.push(3)
	xs.push(4)
	try xs.managed.shiftRight(1, 9223372036854775807) otherwise (e) 'refused'
		print("{e.name}\n")
	end 'refused'
	for x in xs 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
shiftOutOfBounds
1
2
3
4
```
```exitcode
0
```

<!-- test: shift-right-with-a-negative-argument-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
A negative `index` or `count` read as unsigned is past every length, so both are `shiftOutOfBounds`. Read as
signed, `-1 + 1` is 0 and `0 + -1` is -1, both below the length, and the slide would read the word before the
buffer or run backwards through it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var xs = IntegerArray.create()
	xs.push(1)
	xs.push(2)
	xs.push(3)
	try xs.managed.shiftRight(-1, 1) otherwise (e) 'negativeIndex'
		print("index: {e.name}\n")
	end 'negativeIndex'
	try xs.managed.shiftRight(0, -1) otherwise (e) 'negativeCount'
		print("count: {e.name}\n")
	end 'negativeCount'
	for x in xs 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
index: shiftOutOfBounds
count: shiftOutOfBounds
1
2
3
```
```exitcode
0
```

<!-- test: shift-left-with-a-negative-argument-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
The same unsigned reading on the left slide: `-1` as an index is past every length, not one below slot 0, and
`-1` as a count is past every length too.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var xs = IntegerArray.create()
	xs.push(1)
	xs.push(2)
	xs.push(3)
	try xs.managed.shiftLeft(-1, 1) otherwise (e) 'negativeIndex'
		print("index: {e.name}\n")
	end 'negativeIndex'
	try xs.managed.shiftLeft(1, -1) otherwise (e) 'negativeCount'
		print("count: {e.name}\n")
	end 'negativeCount'
	for x in xs 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
index: shiftOutOfBounds
count: shiftOutOfBounds
1
2
3
```
```exitcode
0
```

<!-- test: shift-left-past-the-live-length-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
⭐ **`shiftLeft` IS BOUNDED BY LENGTH, NOT CAPACITY.** `shiftLeft(1, 3)` over three live bytes asks to slide
slot 3 — a reserved slot past the live length — down into slot 2. Bounded by capacity it would pass and pull
a zeroed slot into the live range, where a managed element type reads it as an empty slot that no caller
emptied. Bounded by length it is `shiftOutOfBounds`, and so is a count the size of the whole capacity. The
refusals move nothing.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.reserve(16)
	try arr.managed.shiftLeft(1, 3) otherwise (e) 'pastLength'
		print("past the length: {e.name}\n")
	end 'pastLength'
	let capacity = arr.managed.capacity()
	try arr.managed.shiftLeft(1, capacity) otherwise (e) 'wholeCapacity'
		print("the whole capacity: {e.name}\n")
	end 'wholeCapacity'
	for b in arr 'each'
		print("{b}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
past the length: shiftOutOfBounds
the whole capacity: shiftOutOfBounds
10
20
30
```
```exitcode
0
```

<!-- test: shift-left-whose-range-end-overflows-is-shift-out-of-bounds -->
<!-- tier: stdlib -->
`i64.max + 2` wraps negative, below every length; the check refuses it without forming the sum.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntegerArray = Array with Integer

function main() returns ExitCode
	var xs = IntegerArray.create()
	xs.push(1)
	xs.push(2)
	xs.push(3)
	try xs.managed.shiftLeft(9223372036854775807, 2) otherwise (e) 'refused'
		print("{e.name}\n")
	end 'refused'
	for x in xs 'each'
		print("{x}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
shiftOutOfBounds
1
2
3
```
```exitcode
0
```

<!-- test: shift-right-releases-the-live-occupant-at-the-top-of-its-range -->
<!-- tier: stdlib -->
⭐⭐ **THE SLOT THE SLIDE LANDS ON MAY HOLD A LIVE ELEMENT, AND THE MEMBER RELEASES IT.** `shiftRight(0, 2)`
over three live strings writes `alpha` and `beta` onto slots 1 and 2, so `gamma` in slot 2 is overwritten —
it is released first, exactly once. Without the release `gamma` leaks and the run exits 101; the hole at
slot 0 reads as an empty slot.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha {40 + 2} heap string one")
	xs.push("beta {40 + 2} heap string two")
	xs.push("gamma {40 + 2} heap string three")
	try xs.managed.shiftRight(0, 2) otherwise return 98
	let hole = try xs.get(0) otherwise "the hole is empty"
	let moved = try xs.get(1) otherwise "missing"
	let alsoMoved = try xs.get(2) otherwise "missing"
	print("{hole}\n")
	print("{moved}\n")
	print("{alsoMoved}\n")
	return 0
end 'main'
```
```stdout
the hole is empty
alpha 42 heap string one
beta 42 heap string two
```
```exitcode
0
```

<!-- test: shift-right-of-no-elements-moves-nothing -->
<!-- tier: stdlib -->
⛔ **A COUNT OF 0 TOUCHES NO SLOT.** The occupant of `index + count` is the one a slide overwrites, and with
`count` 0 that is the element AT `index` — which no slide overwrites. Released and then zeroed as the hole, it
would be destroyed while the array still counts it: `beta` would read as an empty slot and the record would
have freed a string it still owns.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha {40 + 2} heap string one")
	xs.push("beta {40 + 2} heap string two")
	xs.push("gamma {40 + 2} heap string three")
	try xs.managed.shiftRight(1, 0) otherwise return 98
	for s in xs 'each'
		print("{s}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
alpha 42 heap string one
beta 42 heap string two
gamma 42 heap string three
```
```exitcode
0
```

<!-- test: shift-left-of-no-elements-moves-nothing -->
<!-- tier: stdlib -->
⛔ **THE SAME ON THE LEFT.** With `count` 0 the slot `index - 1` receives nothing and the slot
`index + count - 1` the slide would vacate is that same slot, so releasing one and zeroing the other would
destroy `alpha` while the array still counts it.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha {40 + 2} heap string one")
	xs.push("beta {40 + 2} heap string two")
	xs.push("gamma {40 + 2} heap string three")
	try xs.managed.shiftLeft(1, 0) otherwise return 98
	for s in xs 'each'
		print("{s}\n")
	end 'each'
	return 0
end 'main'
```
```stdout
alpha 42 heap string one
beta 42 heap string two
gamma 42 heap string three
```
```exitcode
0
```

<!-- test: swap-two-byte-slots -->
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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
<!-- tier: stdlib -->
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

	var lo = 0 as Count
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
<!-- tier: stdlib -->
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
	var total = 0 as ExitCode
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
