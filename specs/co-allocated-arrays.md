---
feature: co-allocated-arrays
status: stable
keywords: [array, fixed array, buffer, allocation, co-allocation, copy-on-write, view, clone, leak, element destructor]
category: memory
---
# Co-allocated array buffers

## Documentation

An array created with a known capacity of at most 256 bytes holds its element buffer in the same allocation as
its record: creating it is one allocation and dropping it is one free. A `FixedArray`, the deep copy of an
array of managed elements, and a `__ManagedMemory` created with a capacity are such creations.

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

<!-- test: a-fixed-array-above-the-co-allocation-limit-keeps-two-allocations -->
512 bytes is past the limit, so the buffer is its own allocation and every other behaviour is the same.
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

	if allocs != 2 'allocs'
		return 2
	end 'allocs'

	if frees != 2 'frees'
		return 3
	end 'frees'

	return 0
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

<!-- test: a-sized-memory-is-one-allocation -->
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
	var names = Names.create()
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
	var names = Names.create()
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

	export function read(holder Holder) returns Word
		return holder.planes.get(1) + holder.planes.get(3)
	end 'read'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let holder = Holder.around()
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
typealias AtLimit = FixedArray with 32 Word
typealias PastLimit = FixedArray with 33 Word

function atLimit() returns Word
	let planes = AtLimit.create()

	return planes.get(31)
end 'atLimit'

function pastLimit() returns Word
	let planes = PastLimit.create()

	return planes.get(32)
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
