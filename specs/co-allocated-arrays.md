---
feature: co-allocated-arrays
status: stable
keywords: [array, fixed array, buffer, allocation, co-allocation, copy-on-write, view, clone, leak, element destructor]
category: memory
---
# Co-allocated array buffers

## Documentation

An array created with a known capacity of at most 256 bytes holds its element buffer in the same allocation as
its record: creating it is one allocation and dropping it is one free. The deep copy of an array of managed
elements and a `__ManagedMemory` created with a capacity are such creations. A `FixedArray` created by `create()`
or `create(n)` never grows, so its buffer is in its record's allocation at every size. One built from a literal,
and the result of `clone`, `slice` or a copy-before-write of one, follows the 256-byte bound.

The buffer stays an ordinary buffer to everything that reads the record. Writing it in place, growing it past its
capacity (which moves it to an allocation of its own), detaching it and releasing it all behave as they do for an
array whose buffer is separate. A view is never cut from a co-allocated buffer: `clone` and `slice` of one copy
the window into a new array, so a view cannot outlive the allocation its bytes live in.

## Tests

<!-- test: a-fixed-array-is-one-allocation-and-one-free -->
`Planes.create()` allocates once and the drop frees once.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

function churn() returns Word
	var planes = Planes.create()
	planes.set(3, value: 40)

	return planes.get(3)
end 'churn'

function main() returns ExitCode
	let allocsBefore = HeapStats.threadAllocations()
	let freesBefore = HeapStats.threadFrees()
	let value = churn()
	let allocs = HeapStats.threadAllocations() - allocsBefore
	let frees = HeapStats.threadFrees() - freesBefore

	if value != 40 'value'
		return 1
	end 'value'

	if allocs != 1 'allocs'
		return 2
	end 'allocs'

	if frees != 1 'frees'
		return 3
	end 'frees'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-fixed-array-above-256-bytes-is-one-allocation -->
A fixed array created by `create()` never grows, so its buffer shares its record's allocation at 512 bytes as it
does at 64.
```maxon
typealias Word = bits(64)
typealias Wide = FixedArray with 64 Word

function churn() returns Word
	var wide = Wide.create()
	wide.set(63, value: 7)

	return wide.get(63)
end 'churn'

function main() returns ExitCode
	let allocsBefore = HeapStats.threadAllocations()
	let freesBefore = HeapStats.threadFrees()
	let value = churn()
	let allocs = HeapStats.threadAllocations() - allocsBefore
	let frees = HeapStats.threadFrees() - freesBefore

	if value != 7 'value'
		return 1
	end 'value'

	if allocs != 1 'allocs'
		return 2
	end 'allocs'

	if frees != 1 'frees'
		return 3
	end 'frees'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-runtime-sized-fixed-array-is-one-allocation-at-any-size -->
A runtime-sized fixed array is one allocation at 352 bytes, and at 40000 bytes, past the largest slab class, where the
record and its buffer live in one operating-system mapping. Both ends of the large one are zero and writable.
```maxon
typealias Bytes = FixedArray with Byte

function churn(count Count) returns Byte
	var bytes = Bytes.create(count)
	let first = try bytes.get(0) otherwise panic("first")
	let last = try bytes.get(count - 1) otherwise panic("last")
	try bytes.set(count - 1, value: 9) otherwise panic("set")

	return first + last + (try bytes.get(count - 1) otherwise panic("read"))
end 'churn'

function allocationsOf(count Count) returns (Byte, HeapCount, HeapCount)
	let allocsBefore = HeapStats.threadAllocations()
	let freesBefore = HeapStats.threadFrees()
	let value = churn(count)

	return (value, HeapStats.threadAllocations() - allocsBefore, HeapStats.threadFrees() - freesBefore)
end 'allocationsOf'

function main() returns ExitCode
	let (smallValue, smallAllocs, smallFrees) = allocationsOf(352)
	let (largeValue, largeAllocs, largeFrees) = allocationsOf(40000)

	if smallValue != 9 or largeValue != 9 'value'
		return 1
	end 'value'

	if smallAllocs != 1 or largeAllocs != 1 'allocs'
		return 2
	end 'allocs'

	if smallFrees != 1 or largeFrees != 1 'frees'
		return 3
	end 'frees'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-recycled-fixed-array-reads-zero-at-a-slab-class-and-past-the-slabs -->
A freed fixed array's allocation is handed to the next creation. At 400 bytes it is a slab class and at 40000 bytes
it is an operating-system mapping; either way the new array must not see the old one's elements.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

function dirty(count ElementIndex) returns Word
	var cells = Cells.create(count)
	try cells.set(0, value: 0xFFFF) otherwise panic("set")
	try cells.set(count - 1, value: 0xEEEE) otherwise panic("set")

	return try cells.get(0) otherwise panic("get")
end 'dirty'

function seenInFresh(count ElementIndex) returns Word
	let cells = Cells.create(count)
	var seen = 0 as Word

	for at in 0 upto count 'each'
		seen = seen or (try cells.get(at) otherwise panic("get"))
	end 'each'

	return seen
end 'seenInFresh'

function check(count ElementIndex) returns ExitCode
	var round = 0 as Count

	while round < 20 'rounds'
		if dirty(count) != 0xFFFF 'dirty'
			return 1
		end 'dirty'

		if seenInFresh(count) != 0 'fresh'
			return 2
		end 'fresh'

		round = round + 1
	end 'rounds'

	return 0
end 'check'

function main() returns ExitCode
	let slabClass = check(50)

	if slabClass != 0 'slabClass'
		return slabClass
	end 'slabClass'

	return check(5000)
end 'main'
```
```exitcode
0
```

<!-- test: a-clone-and-a-slice-of-a-large-fixed-array-copy-their-window -->
<!-- tier: stdlib -->
A clone of a record holding a fixed array, and a slice of the fixed array, copy rather than view, at 400 bytes and at
40000: writing or clearing either side leaves the other alone, and the slice outlives the array it was cut from.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

extension FixedArray
	function windowCount(start ElementIndex, stop ElementIndex) returns ElementIndex
		let window = try managed.slice(start, stop) otherwise panic("slice")

		return window.length() as ElementIndex
	end 'windowCount'

	function clearedWindowLeavesTheArray(start ElementIndex, stop ElementIndex) returns bool
		var window = try managed.slice(start, stop) otherwise panic("slice")
		let before = managed.length()
		window.clear()

		return window.length() == 0 and managed.length() == before
	end 'clearedWindowLeavesTheArray'
end 'FixedArray'

type Holder
	export var cells as Cells

	static function around(count ElementIndex) returns Self
		var cells = Cells.create(count)
		try cells.set(1, value: 5) otherwise panic("set")
		try cells.set(count - 1, value: 6) otherwise panic("set")

		return Self{cells: cells}
	end 'around'
end 'Holder'

function check(count ElementIndex) returns ExitCode
	var original = Holder.around(count)
	var copy = original.clone()

	try copy.cells.set(1, value: 99) otherwise panic("set")
	try original.cells.set(count - 1, value: 77) otherwise panic("set")

	if (try original.cells.get(1) otherwise 0) != 5 or (try original.cells.get(count - 1) otherwise 0) != 77 'original'
		return 1
	end 'original'

	if (try copy.cells.get(1) otherwise 0) != 99 or (try copy.cells.get(count - 1) otherwise 0) != 6 'copy'
		return 2
	end 'copy'

	if original.cells.windowCount(2, stop: count) != count - 2 'sliceLength'
		return 3
	end 'sliceLength'

	if not original.cells.clearedWindowLeavesTheArray(2, stop: count) 'sliceIndependent'
		return 4
	end 'sliceIndependent'

	if (try original.cells.get(count - 1) otherwise 0) != 77 'untouched'
		return 5
	end 'untouched'

	return 0
end 'check'

function main() returns ExitCode
	let slabClass = check(50)

	if slabClass != 0 'slabClass'
		return slabClass
	end 'slabClass'

	return check(5000)
end 'main'
```
```exitcode
0
```

<!-- test: a-published-large-fixed-array-is-copied-before-it-is-written -->
A fixed array published through a `SharedValue` is frozen at 400 bytes and at 40000: a reader that wants to write
copies it first, and the published one keeps its elements.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

type Config
	export var cells as Cells

	static function create(count ElementIndex) returns Self
		var cells = Cells.create(count)
		try cells.set(1, value: 5) otherwise panic("set")

		return Self{cells: cells}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function check(count ElementIndex) returns ExitCode
	let cell = ConfigCell.create(Config.create(count))
	var mine = cell.current().clone()

	try mine.cells.set(1, value: 9) otherwise panic("set")
	try mine.cells.set(2, value: 4) otherwise panic("set")

	let published = cell.current()

	if (try published.cells.get(1) otherwise 0) != 5 or (try published.cells.get(2) otherwise 1) != 0 'frozen'
		return 1
	end 'frozen'

	if (try mine.cells.get(1) otherwise 0) != 9 or (try mine.cells.get(2) otherwise 0) != 4 'copy'
		return 2
	end 'copy'

	return 0
end 'check'

function main() returns ExitCode
	let slabClass = check(50)

	if slabClass != 0 'slabClass'
		return slabClass
	end 'slabClass'

	return check(5000)
end 'main'
```
```exitcode
0
```

<!-- test: a-large-fixed-array-crosses-to-a-service-and-is-written-there -->
A move proves the record sole, so a fixed array of 400 bytes or of 40000 crosses to a service with its elements, the
service writes it in place, and it is released there once.
```maxon
typealias Word = bits(64)
typealias Cells = FixedArray with Word

type Keeper
	var cells as Cells

	static function create() returns Self
		return Self{cells: Cells.create(1)}
	end 'create'

	export function adopt(incoming isolated Cells, last ElementIndex) returns Word
		self.cells = merge incoming
		try self.cells.set(last, value: 7) otherwise panic("set")

		return (try self.cells.get(1) otherwise 0) + (try self.cells.get(last) otherwise 0)
	end 'adopt'
end 'Keeper'

function check(count ElementIndex) returns ExitCode
	let keeper = spawn Keeper.create()
	var cells = Cells.create(count) as isolated Cells
	try cells.set(1, value: 5) otherwise panic("set")

	let total = try await keeper.adopt(cells, last: count - 1) otherwise 0

	if total != 12 'total'
		return 1
	end 'total'

	return 0
end 'check'

function main() returns ExitCode
	let slabClass = check(50)

	if slabClass != 0 'slabClass'
		return slabClass
	end 'slabClass'

	return check(5000)
end 'main'
```
```exitcode
0
```

<!-- test: a-recycled-co-allocated-array-reads-zero -->
A freed array's box is handed to the next creation; the new array must not see the old one's elements, in the
buffer or in the record.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 8 Word

function dirty() returns Word
	var planes = Planes.create()
	planes.set(0, value: 0xFFFF)
	planes.set(7, value: 0xEEEE)

	return planes.get(0)
end 'dirty'

function sumOfFresh() returns Word
	let planes = Planes.create()

	return planes.get(0) or planes.get(1) or planes.get(2) or planes.get(3) or planes.get(4) or planes.get(5) or planes.get(6) or planes.get(7)
end 'sumOfFresh'

function main() returns ExitCode
	var round = 0 as Word

	while round < 200 'rounds'
		if dirty() != 0xFFFF 'dirty'
			return 1
		end 'dirty'

		if sumOfFresh() != 0 'fresh'
			return 2
		end 'fresh'

		round = round + 1
	end 'rounds'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-packed-fixed-array-is-one-allocation -->
A sub-byte-packed element sizes the buffer in bits, and the single allocation holds all of them.
```maxon
typealias Flags = FixedArray with 16 bool

function churn() returns bool
	var flags = Flags.create()
	flags.set(3, value: true)
	flags.set(15, value: true)

	return flags.get(3) and flags.get(15) and not flags.get(4)
end 'churn'

function main() returns ExitCode
	let allocsBefore = HeapStats.threadAllocations()
	let ok = churn()
	let allocs = HeapStats.threadAllocations() - allocsBefore

	if not ok 'bits'
		return 1
	end 'bits'

	if allocs != 1 'allocs'
		return 2
	end 'allocs'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-sized-byte-fixed-array-is-as-large-as-its-bytes -->
A fixed array whose type states its length of 300 bytes is one record of 64 bytes of header and 300 bytes of elements,
the size a runtime-sized `create(300)` has. **If the sized form's `ArrayRecord` is larger than the runtime-sized
one's, its elements are not one byte each.**
<!-- MmTrace -->
```maxon
typealias Wide = FixedArray with 300 Byte
typealias Runtime = FixedArray with Byte

function sized() returns Byte
	var wide = Wide.create()
	wide.set(299, value: 5)

	return wide.get(299)
end 'sized'

function launder(n ElementIndex) returns ElementIndex
	return n
end 'launder'

function runtime() returns Byte
	var bytes = Runtime.create(launder(300))
	try bytes.set(299, value: 5) otherwise panic("set")

	return try bytes.get(299) otherwise panic("get")
end 'runtime'

function main() returns ExitCode
	if sized() + runtime() != 10 'values'
		return 1
	end 'values'

	return 0
end 'main'
```
```exitcode
0
```

```mm-trace
mm_alloc ArrayRecord #1 size=364
mm_decref ArrayRecord #1 rc=0
mm_free ArrayRecord #1
mm_alloc ArrayRecord #2 size=364
mm_decref ArrayRecord #2 rc=0
mm_free ArrayRecord #2
```

<!-- test: a-sized-fixed-array-is-as-large-as-its-elements-at-every-width -->
A sized fixed array's record is the 64-byte header and its elements at their own width: 200 bytes for a hundred 16-bit
elements, 400 for a hundred 32-bit ones, and a hundred bools, 2-bit and 4-bit elements packed into 13, 25 and 50.
<!-- MmTrace -->
```maxon
typealias Half = int(0 to 65535)
typealias Wide = int(0 to u32.max)
typealias Crumb = int(0 to 3)
typealias Nibble = int(0 to 15)
typealias Halves = FixedArray with 100 Half
typealias Wides = FixedArray with 100 Wide
typealias Flags = FixedArray with 100 bool
typealias Crumbs = FixedArray with 100 Crumb
typealias Nibbles = FixedArray with 100 Nibble

function halves() returns Half
	var cells = Halves.create()
	cells.set(99, value: 7)

	return cells.get(99)
end 'halves'

function wides() returns Wide
	var cells = Wides.create()
	cells.set(99, value: 7)

	return cells.get(99)
end 'wides'

function flags() returns bool
	var cells = Flags.create()
	cells.set(99, value: true)

	return cells.get(99)
end 'flags'

function crumbs() returns Crumb
	var cells = Crumbs.create()
	cells.set(99, value: 3)

	return cells.get(99)
end 'crumbs'

function nibbles() returns Nibble
	var cells = Nibbles.create()
	cells.set(99, value: 15)

	return cells.get(99)
end 'nibbles'

function main() returns ExitCode
	if halves() != 7 or wides() != 7 or not flags() or crumbs() != 3 or nibbles() != 15 'values'
		return 1
	end 'values'

	return 0
end 'main'
```
```exitcode
0
```

```mm-trace
mm_alloc ArrayRecord #1 size=264
mm_decref ArrayRecord #1 rc=0
mm_free ArrayRecord #1
mm_alloc ArrayRecord #2 size=464
mm_decref ArrayRecord #2 rc=0
mm_free ArrayRecord #2
mm_alloc ArrayRecord #3 size=77
mm_decref ArrayRecord #3 rc=0
mm_free ArrayRecord #3
mm_alloc ArrayRecord #4 size=89
mm_decref ArrayRecord #4 rc=0
mm_free ArrayRecord #4
mm_alloc ArrayRecord #5 size=114
mm_decref ArrayRecord #5 rc=0
mm_free ArrayRecord #5
```

<!-- test: a-generic-born-array-is-as-large-as-its-elements-at-every-width -->
An array a shared generic body creates with a known capacity is the 64-byte header and its elements at the width the
instantiation binds, the size the same array has when a concrete body creates it: a hundred bytes, a hundred 16-bit
elements, a hundred `bool`s and a hundred 2-bit elements take 164, 264, 77 and 89 bytes. **If a shared body stamps a
machine word per element instead, each record is eight times as large in its elements and the buffer leaves the
record's allocation.**
<!-- MmTrace -->
```maxon
typealias Half = int(0 to 65535)
typealias Crumb = int(0 to 3)

type Cells uses Element
	typealias Items = Array with Element
	export var items as Items

	static function create() returns Self
		return Self{items: Items.create(100, zeroed: false)}
	end 'create'
end 'Cells'

typealias ByteCells = Cells with Byte
typealias HalfCells = Cells with Half
typealias FlagCells = Cells with bool
typealias CrumbCells = Cells with Crumb

function bytes() returns bool
	let cells = ByteCells.create()

	return cells.items.capacity() >= 100
end 'bytes'

function halves() returns bool
	let cells = HalfCells.create()

	return cells.items.capacity() >= 100
end 'halves'

function flags() returns bool
	let cells = FlagCells.create()

	return cells.items.capacity() >= 100
end 'flags'

function crumbs() returns bool
	let cells = CrumbCells.create()

	return cells.items.capacity() >= 100
end 'crumbs'

function main() returns ExitCode
	if not bytes() or not halves() or not flags() or not crumbs() 'capacities'
		return 1
	end 'capacities'

	return 0
end 'main'
```
```exitcode
0
```

```mm-trace
mm_alloc ArrayRecord #1 size=164
mm_alloc Cells #2 size=8
mm_decref Cells #2 rc=0
mm_decref ArrayRecord #1 rc=0
mm_free ArrayRecord #1
mm_free Cells #2
mm_alloc ArrayRecord #3 size=264
mm_alloc Cells #4 size=8
mm_decref Cells #4 rc=0
mm_decref ArrayRecord #3 rc=0
mm_free ArrayRecord #3
mm_free Cells #4
mm_alloc ArrayRecord #5 size=77
mm_alloc Cells #6 size=8
mm_decref Cells #6 rc=0
mm_decref ArrayRecord #5 rc=0
mm_free ArrayRecord #5
mm_free Cells #6
mm_alloc ArrayRecord #7 size=89
mm_alloc Cells #8 size=8
mm_decref Cells #8 rc=0
mm_decref ArrayRecord #7 rc=0
mm_free ArrayRecord #7
mm_free Cells #8
```

<!-- test: a-sized-memory-is-one-allocation -->
<!-- unsupported-targets: wasm32-wasi -->
<!-- tier: stdlib -->
`__ManagedMemory.create(capacity, elementSize)` is the sized creation the standard library's own buffers use.
```maxon
function churn() returns ExitCode
	var memory = try __ManagedMemory.create(16, 1) otherwise return 1
	try memory.setLength(16) otherwise return 2

	let bytes = ByteArray.init(memory)

	return bytes.count() as ExitCode
end 'churn'

function main() returns ExitCode
	let allocsBefore = HeapStats.threadAllocations()
	let freesBefore = HeapStats.threadFrees()
	let result = churn()
	let allocs = HeapStats.threadAllocations() - allocsBefore
	let frees = HeapStats.threadFrees() - freesBefore

	if result != 16 'length'
		return 1
	end 'length'

	if allocs != 1 'allocs'
		return 2
	end 'allocs'

	if frees != 1 'frees'
		return 3
	end 'frees'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: growing-past-the-co-allocated-capacity-moves-the-buffer-and-frees-cleanly -->
<!-- tier: stdlib -->
The record keeps its identity while its buffer leaves the allocation it was born in; the elements survive the move
and the whole thing is released with no leak.
```maxon
typealias Total = int(0 to u64.max)

function main() returns ExitCode
	var memory = try __ManagedMemory.create(8, 1) otherwise return 1
	try memory.setLength(8) otherwise return 2

	var bytes = ByteArray.init(memory)
	var index = 0 as Total

	while index < 8 'fill'
		try bytes.set(index as ElementIndex, value: index as Byte) otherwise return 3
		index = index + 1
	end 'fill'

	while index < 200 'grow'
		bytes.push(index as Byte)
		index = index + 1
	end 'grow'

	if bytes.count() != 200 'count'
		return 4
	end 'count'

	var total = 0 as Total

	for byte in bytes 'sum'
		total = total + (byte as Total)
	end 'sum'

	if total != 19900 'total'
		return 5
	end 'total'

	bytes.reserve(5000)
	bytes.push(1)

	if bytes.count() != 201 'last'
		return 6
	end 'last'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-clone-of-a-co-allocated-array-is-independent-in-both-directions -->
<!-- tier: stdlib -->
`clone` copies a co-allocated array rather than viewing it, so a write on either side never reaches the other.
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(8, 1) otherwise return 1
	try memory.setLength(8) otherwise return 2

	var original = ByteArray.init(memory)

	try original.set(0, value: 10) otherwise return 3
	try original.set(1, value: 11) otherwise return 3

	var copy = original.clone()

	try copy.set(0, value: 99) otherwise return 4
	try original.set(1, value: 77) otherwise return 5

	let originalFirst = try original.get(0) otherwise return 6
	let originalSecond = try original.get(1) otherwise return 6
	let copyFirst = try copy.get(0) otherwise return 7
	let copySecond = try copy.get(1) otherwise return 7

	if originalFirst != 10 or originalSecond != 77 'original'
		return 8
	end 'original'

	if copyFirst != 99 or copySecond != 11 'copy'
		return 9
	end 'copy'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-clone-of-a-co-allocated-array-is-one-allocation -->
<!-- tier: stdlib -->
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(8, 1) otherwise return 1
	try memory.setLength(8) otherwise return 2

	let original = ByteArray.init(memory)
	let allocsBefore = HeapStats.threadAllocations()
	let copy = original.clone()
	let allocs = HeapStats.threadAllocations() - allocsBefore

	if copy.count() != 8 'count'
		return 3
	end 'count'

	if allocs != 1 'allocs'
		return 4
	end 'allocs'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-slice-of-a-co-allocated-array-outlives-the-array -->
<!-- tier: stdlib -->
The array that the slice was cut from is released before the slice is read and written.
```maxon
function cut() returns ByteArray
	var memory = try __ManagedMemory.create(8, 1) otherwise panic("create")
	try memory.setLength(8) otherwise panic("setLength")

	var source = ByteArray.init(memory)

	try source.set(2, value: 20) otherwise panic("set")
	try source.set(3, value: 30) otherwise panic("set")
	try source.set(4, value: 40) otherwise panic("set")

	return try source.slice(2, endIndex: 5) otherwise panic("slice")
end 'cut'

function main() returns ExitCode
	var part = cut()

	if part.count() != 3 'count'
		return 1
	end 'count'

	let first = try part.get(0) otherwise return 2
	let last = try part.get(2) otherwise return 2

	if first != 20 or last != 40 'bytes'
		return 3
	end 'bytes'

	part.push(50)
	try part.set(0, value: 21) otherwise return 4

	let pushed = try part.get(3) otherwise return 5
	let rewritten = try part.get(0) otherwise return 5

	if pushed != 50 or rewritten != 21 'written'
		return 6
	end 'written'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: writing-a-co-allocated-array-after-a-slice-leaves-the-slice-intact -->
<!-- tier: stdlib -->
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(8, 1) otherwise return 1
	try memory.setLength(8) otherwise return 2

	var source = ByteArray.init(memory)

	try source.set(1, value: 5) otherwise return 3

	let part = try source.slice(0, endIndex: 4) otherwise return 4

	try source.set(1, value: 6) otherwise return 5
	source.push(9)

	let seen = try part.get(1) otherwise return 6

	if seen != 5 'sliceUnchanged'
		return 7
	end 'sliceUnchanged'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-immortal-literal-array-clones-and-detaches-as-before -->
A constant array literal is image data, not a co-allocation; cloning it and writing the clone still copies.
```maxon
function main() returns ExitCode
	let literal = b"hello"
	var copy = literal.clone()

	try copy.set(0, value: 72) otherwise return 1

	let original = try literal.get(0) otherwise return 2
	let changed = try copy.get(0) otherwise return 3

	if original != 104 or changed != 72 'bytes'
		return 4
	end 'bytes'

	copy.push(33)

	if copy.count() != 6 'count'
		return 5
	end 'count'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-deep-clone-of-managed-elements-is-one-allocation-plus-its-elements -->
The deep copy of an array of Strings is its record, its buffer and one record per String; the first two are one
allocation. The clone owns its Strings: replacing one leaves the original's untouched, and every String is released
once.
```maxon
typealias Names = Array with String

function build() returns Names
	var names = Names.create(0)
	var index = 0 as ElementIndex

	while index < 6 'each'
		names.push("name number {index}")
		index = index + 1
	end 'each'

	return names
end 'build'

function main() returns ExitCode
	var original = build()
	let allocsBefore = HeapStats.threadAllocations()
	var copy = original.clone()
	let allocs = HeapStats.threadAllocations() - allocsBefore

	if allocs != 7 'allocs'
		return 1
	end 'allocs'

	try copy.set(0, value: "replaced {original.count()}") otherwise return 2

	let first = try original.get(0) otherwise return 3
	let changed = try copy.get(0) otherwise return 4

	if first != "name number 0" 'untouched'
		return 5
	end 'untouched'

	if changed != "replaced 6" 'replaced'
		return 6
	end 'replaced'

	let last = try copy.pop() otherwise return 7

	if last != "name number 5" or copy.count() != 5 'popped'
		return 8
	end 'popped'

	copy.clear()

	if original.count() != 6 'originalCount'
		return 9
	end 'originalCount'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-slice-of-managed-elements-owns-its-elements-and-frees-them-once -->
```maxon
typealias Names = Array with String

function build() returns Names
	var names = Names.create(0)
	var index = 0 as ElementIndex

	while index < 6 'each'
		names.push("name number {index}")
		index = index + 1
	end 'each'

	return names
end 'build'

function cut() returns Names
	let source = build()

	return try source.slice(1, endIndex: 4) otherwise panic("slice")
end 'cut'

function main() returns ExitCode
	var part = cut()

	if part.count() != 3 'count'
		return 1
	end 'count'

	let first = try part.get(0) otherwise return 2

	if first != "name number 1" 'first'
		return 3
	end 'first'

	part.push("tail")

	if part.count() != 4 'grown'
		return 4
	end 'grown'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-co-allocated-array-crosses-to-a-service-and-is-released-there -->
A move proves the record sole and detaches it; a co-allocated buffer is already private, so the record crosses with
its elements and the service releases it once.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 4 Word

type Holder
	export let planes as Planes

	static function around() returns Self
		var planes = Planes.create()
		planes.set(1, value: 5)
		planes.set(3, value: 9)

		return Self{planes: planes}
	end 'around'
end 'Holder'

type Reader
	var reads as Word

	static function create() returns Self
		return Self{reads: 0}
	end 'create'

	export function read(holder isolated Holder) returns Word
		return holder.planes.get(1) + holder.planes.get(3)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let holder = Holder.around() as isolated Holder
	let total = try await reader.read(holder) otherwise 0

	if total != 14 'total'
		return 1
	end 'total'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-co-allocation-limit-is-256-bytes-of-words -->
32 words, 256 bytes, are one allocation; one word more is two.
```maxon
typealias Word = bits(64)
typealias Planes = Array with Word

function atLimit() returns Word
	let planes = Planes.create(32)

	return try planes.get(31) otherwise 1
end 'atLimit'

function pastLimit() returns Word
	let planes = Planes.create(33)

	return try planes.get(32) otherwise 1
end 'pastLimit'

function main() returns ExitCode
	var before = HeapStats.threadAllocations()
	let a = atLimit()
	let atLimitAllocations = HeapStats.threadAllocations() - before

	before = HeapStats.threadAllocations()
	let b = pastLimit()
	let pastLimitAllocations = HeapStats.threadAllocations() - before

	if a != 0 or b != 0 'zero'
		return 1
	end 'zero'

	if atLimitAllocations != 1 'atLimit'
		return 2
	end 'atLimit'

	if pastLimitAllocations != 2 'pastLimit'
		return 3
	end 'pastLimit'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-co-allocation-limit-is-256-bytes-of-bytes -->
<!-- tier: stdlib -->
The same boundary at a one-byte element: 256 bytes are one allocation and 257 are two.
```maxon
function main() returns ExitCode
	var before = HeapStats.threadAllocations()
	let atLimit = try __ManagedMemory.create(256, 1) otherwise return 1
	let atLimitAllocations = HeapStats.threadAllocations() - before

	before = HeapStats.threadAllocations()
	let pastLimit = try __ManagedMemory.create(257, 1) otherwise return 2
	let pastLimitAllocations = HeapStats.threadAllocations() - before

	if atLimitAllocations != 1 or atLimit.capacity() != 256 'atLimit'
		return 3
	end 'atLimit'

	if pastLimitAllocations != 2 or pastLimit.capacity() != 257 'pastLimit'
		return 4
	end 'pastLimit'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-capacity-below-the-minimum-is-floored-and-still-one-allocation -->
<!-- tier: stdlib -->
A buffer asked for fewer elements than the growth policy's minimum of 4 reports the minimum, and a capacity of 0
is a record with no buffer at all.
```maxon
function main() returns ExitCode
	let before = HeapStats.threadAllocations()
	let one = try __ManagedMemory.create(1, 1) otherwise return 1
	let oneAllocations = HeapStats.threadAllocations() - before

	let beforeEmpty = HeapStats.threadAllocations()
	let none = try __ManagedMemory.create(0, 1) otherwise return 2
	let noneAllocations = HeapStats.threadAllocations() - beforeEmpty

	if one.capacity() != 4 or oneAllocations != 1 'floor'
		return 3
	end 'floor'

	if none.capacity() != 0 or noneAllocations != 1 'empty'
		return 4
	end 'empty'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: growing-a-co-allocated-memory-keeps-its-bytes-and-frees-cleanly -->
<!-- tier: stdlib -->
`grow` is the exact-capacity reallocation of the buffer surface; the bytes written before it are still there after.
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(8, 1) otherwise return 1
	try memory.setLength(8) otherwise return 2

	var index = 0 as ElementIndex

	while index < 8 'fill'
		try memory.set(index, value: (index + 1) as Byte) otherwise return 3
		index = index + 1
	end 'fill'

	try memory.grow(1000) otherwise return 4

	if memory.capacity() != 1000 or memory.length() != 8 'shape'
		return 5
	end 'shape'

	let first = try memory.get(0) otherwise return 6
	let last = try memory.get(7) otherwise return 6

	if first != 1 or last != 8 'bytes'
		return 7
	end 'bytes'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: swap-and-shift-work-in-place-on-a-co-allocated-buffer -->
<!-- tier: stdlib -->
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(8, 1) otherwise return 1
	try memory.setLength(8) otherwise return 2

	var index = 0 as ElementIndex

	while index < 8 'fill'
		try memory.set(index, value: (index + 1) as Byte) otherwise return 3
		index = index + 1
	end 'fill'

	try memory.swap(0, j: 7) otherwise return 4
	try memory.shiftRight(0, 3) otherwise return 5

	let zero = try memory.get(0) otherwise return 6
	let one = try memory.get(1) otherwise return 6
	let two = try memory.get(2) otherwise return 6
	let three = try memory.get(3) otherwise return 6
	let four = try memory.get(4) otherwise return 6

	if zero != 0 or one != 8 or two != 2 or three != 3 or four != 5 'shifted'
		return 7
	end 'shifted'

	try memory.shiftLeft(5, 2) otherwise return 8

	let slotFour = try memory.get(4) otherwise return 9
	let slotFive = try memory.get(5) otherwise return 9
	let slotSix = try memory.get(6) otherwise return 9
	let slotSeven = try memory.get(7) otherwise return 9

	if slotFour != 6 or slotFive != 7 or slotSix != 0 or slotSeven != 1 'shiftedLeft'
		return 10
	end 'shiftedLeft'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-packed-fixed-array-slice-and-clone-copy-their-window -->
<!-- tier: stdlib -->
A slice of a sub-byte-packed fixed array is a copy, and so is the clone that is a slice of the whole: writing
either leaves the fixed array's bits alone.
```maxon
typealias Flags = FixedArray with 16 bool

extension FixedArray
	function windowOnes(start ElementIndex, stop ElementIndex) returns ElementIndex
		let window = try managed.slice(start, stop) otherwise panic("slice")
		var ones = 0 as ElementIndex
		var at = 0 as ElementIndex

		while at < (window.length() as ElementIndex) 'each'
			let bit = try window.get(at) otherwise panic("get")

			if bit 'set'
				ones = ones + 1
			end 'set'

			at = at + 1
		end 'each'

		return ones
	end 'windowOnes'

	function clearedWindowLeavesTheArray(start ElementIndex, stop ElementIndex) returns bool
		var window = try managed.slice(start, stop) otherwise panic("slice")
		window.clear()

		return window.length() == 0 and managed.length() == 16
	end 'clearedWindowLeavesTheArray'
end 'FixedArray'

function main() returns ExitCode
	var flags = Flags.create()
	flags.set(2, value: true)
	flags.set(3, value: true)
	flags.set(9, value: true)
	flags.set(15, value: true)

	if flags.windowOnes(0, stop: 16) != 4 'clone'
		return 1
	end 'clone'

	if flags.windowOnes(2, stop: 10) != 3 'slice'
		return 2
	end 'slice'

	if not flags.clearedWindowLeavesTheArray(2, stop: 10) 'independent'
		return 3
	end 'independent'

	if flags.windowOnes(0, stop: 16) != 4 'untouched'
		return 4
	end 'untouched'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-published-co-allocated-array-is-copied-before-it-is-written -->
A value published through a `SharedValue` is frozen: a reader that wants to write copies it first, and the copy of
a co-allocated array owns a buffer of its own while the published one keeps its elements.
```maxon
typealias Word = bits(64)
typealias Planes = FixedArray with 4 Word

type Config
	export var planes as Planes

	static function create() returns Self
		var planes = Planes.create()
		planes.set(1, value: 5)

		return Self{planes: planes}
	end 'create'
end 'Config'

typealias ConfigCell = SharedValue with Config

function main() returns ExitCode
	let cell = ConfigCell.create(Config.create())
	var mine = cell.current().clone()

	mine.planes.set(1, value: 9)
	mine.planes.set(2, value: 4)

	let published = cell.current()

	if published.planes.get(1) != 5 or published.planes.get(2) != 0 'frozen'
		return 1
	end 'frozen'

	if mine.planes.get(1) != 9 or mine.planes.get(2) != 4 'copy'
		return 2
	end 'copy'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-co-allocated-array-grown-by-resize-keeps-its-prefix -->
<!-- tier: stdlib -->
`resize` past the capacity reallocates; the part below the old length is carried across.
```maxon
function main() returns ExitCode
	var memory = try __ManagedMemory.create(4, 1) otherwise return 1
	try memory.setLength(4) otherwise return 2

	var bytes = ByteArray.init(memory)

	try bytes.set(0, value: 1) otherwise return 3
	try bytes.set(3, value: 4) otherwise return 3

	bytes.resize(100)

	let first = try bytes.get(0) otherwise return 4
	let fourth = try bytes.get(3) otherwise return 4
	let tail = try bytes.get(99) otherwise return 4

	if first != 1 or fourth != 4 or tail != 0 'bytes'
		return 5
	end 'bytes'

	bytes.resize(2)
	bytes.resize(4)

	let cleared = try bytes.get(3) otherwise return 6

	if cleared != 0 'shrunk'
		return 7
	end 'shrunk'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-separate-buffer-allocated-right-behind-its-record-is-released-with-it -->
A growable array whose buffer was allocated separately, in the same size class as its record and right behind it,
is released as a separate buffer: the record tells a co-allocated buffer from a separate one by a field it carries, and
never by where the buffer sits. **If the buffer behind a record is taken for a co-allocated one, dropping the array
leaks it and the run exits 101.**
```maxon
typealias Word = bits(64)
typealias Words = Array with Word
typealias Rows = Array with Words

function main() returns ExitCode
	var rows = Rows.create(0)

	for row in 0 upto 64 'eachRow'
		var words = Words.create(0)
		words.reserve(6)
		words.push(row as Word)
		rows.push(words)
	end 'eachRow'

	var total = 0 as Word

	for words in rows 'eachWords'
		total = total + (try words.get(0) otherwise return 1)
	end 'eachWords'

	if total != 2016 'total'
		return 2
	end 'total'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-array-created-at-a-constant-capacity-reads-zero-in-a-slot-an-earlier-array-left-filled -->
An array created at a capacity the compiler knows takes its record and its buffer from the allocator at the creating
function, with no call. The slot it takes may be one an earlier array released with every byte set, and it still reads
zero: the zeroing contract holds on that road as on the call's. **If the recycled slot is not cleared, the second array
reads the first one's bytes.**
```maxon
function leaveFilled() returns Count
	var bytes = ByteArray.create(16)

	for i in 0 upto 16 'fill'
		try bytes.set(i, value: 255) otherwise panic("set")
	end 'fill'

	return bytes.count()
end 'leaveFilled'

function sumOfFresh() returns Count
	let fresh = ByteArray.create(16)
	var total = 0 as Count

	for i in 0 upto 16 'eachByte'
		total = total + (try fresh.get(i) otherwise panic("get")) as Count
	end 'eachByte'

	return total + fresh.count() + (fresh.capacity() as Count)
end 'sumOfFresh'

function main() returns ExitCode
	for _ in 0 upto 4 'eachRound'
		if leaveFilled() != 16 'filled'
			return 1
		end 'filled'

		if sumOfFresh() != 32 'zero'
			return 2
		end 'zero'
	end 'eachRound'

	return 0
end 'main'
```
```exitcode
0
```
