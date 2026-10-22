---
feature: managed-memory-cursor
status: stable
keywords: [managed-memory, cursor, iterator, bytearray, ownership]
category: dev
---

# `__ManagedMemoryCursor`

## Documentation

`__ManagedMemory.createCursor()` hands back a `__ManagedMemoryCursor with Element` — a
navigable position inside the buffer. Navigation never moves it to an invalid position: every
navigation member (`advance`, `retreat`, `seek`, `peek`) is throwing and refuses rather than
moving. `createCursor()` itself throws on an EMPTY buffer, because there is no valid position to
start at. A write to the source can still shorten the buffer under the cursor, so `current()`
checks its position against the live length and panics with an index out of range past the end.

`stdlib/Array.maxon:490-531`'s `ArrayIterator` is the only consumer and therefore the spec:
it stores one in a field and forwards all six members, re-labelling the buffer's failures
as `IterationError`. The type is library-tier surface, so every case here compiles as a
library-tier source (`<!-- tier: stdlib -->`).

### The cursor RETAINS its source

⭐ A cursor is a second reader of a record it does not own. It therefore takes its own
reference to the source record, and releases it when the cursor is dropped. Without that
retain the source's textual last use is the `createCursor()` call itself, the record is
freed there, and the cursor reads through a reclaimed allocation — a SIGSEGV
(`maxon-bin/Compiler/Runtime/ManagedMemoryRuntime.maxon`, the `__ManagedMemoryCursor` section).

⚠ **IT HOLDS THE RECORD, NOT THE RECORD'S FIELDS.** `length` and `element_size` are read
LIVE on every access rather than snapshotted at creation, so a cursor cannot come to
disagree with the buffer it is pointed at. Both references snapshot `buffer`/`length`/
`element_size` into the cursor and pay for it: a snapshot of `buffer@0` is precisely what
dangles across a reallocation, and a snapshot of `length` reports positions the buffer no
longer has.

### …and every value read from it is counted

`current()` and `peek` hand back their own reference to the element they read, so a value read
from a cursor outlives any later write to the source. The source stays writable under a live
cursor: a `push` is visible to the cursor, and a `clear` leaves it past the end, where
`current()` panics.

## Tests

<!-- test: cursor-reads-the-first-element -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	let cursor = try arr.managed.createCursor() otherwise return 99
	return cursor.current()
end 'main'
```
```exitcode
10
```

<!-- test: cursor-advances-across-the-whole-buffer -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	var cursor = try arr.managed.createCursor() otherwise return 99
	var total = cursor.current()
	try cursor.advance() otherwise return 98
	total = total + cursor.current()
	try cursor.advance() otherwise return 97
	total = total + cursor.current()
	return total
end 'main'
```
```exitcode
60
```

<!-- test: cursor-index-reports-the-position -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.advance() otherwise return 98
	try cursor.advance() otherwise return 97
	return cursor.index()
end 'main'
```
```exitcode
2
```

<!-- test: advancing-past-the-last-element-throws-and-does-not-move -->
<!-- tier: stdlib -->
The cursor is refused rather than moved, so `index()` still reports the last valid
position afterwards.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.advance() otherwise return 98
	try cursor.advance() otherwise 'atEnd'
		return cursor.index() + cursor.current()
	end 'atEnd'
	return 0
end 'main'
```
```exitcode
21
```

<!-- test: cursor-retreats -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.advance() otherwise return 98
	try cursor.advance() otherwise return 97
	try cursor.retreat() otherwise return 96
	return cursor.current()
end 'main'
```
```exitcode
20
```

<!-- test: retreating-from-the-first-element-throws -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.retreat() otherwise return 5
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: cursor-seeks-to-an-absolute-position -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	arr.push(40)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.seek(3) otherwise return 98
	return cursor.current() + cursor.index()
end 'main'
```
```exitcode
43
```

<!-- test: seeking-out-of-range-throws -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.seek(2) otherwise return 5
	return 0
end 'main'
```
```exitcode
5
```

<!-- test: cursor-peeks-ahead-without-moving -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	let cursor = try arr.managed.createCursor() otherwise return 99
	let ahead = try cursor.peek(2) otherwise return 98
	return ahead + cursor.index()
end 'main'
```
```exitcode
30
```

<!-- test: peeking-past-the-end-throws -->
<!-- tier: stdlib -->
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	let cursor = try arr.managed.createCursor() otherwise return 99
	let ahead = try cursor.peek(2) otherwise 5
	return ahead
end 'main'
```
```exitcode
5
```

<!-- test: a-cursor-over-an-empty-buffer-is-refused -->
<!-- tier: stdlib -->
There is no valid position to start at, so the cursor is never minted.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	let cursor = try arr.managed.createCursor() otherwise return 5
	return cursor.current()
end 'main'
```
```exitcode
5
```

<!-- test: the-refused-cursor-strands-nothing -->
<!-- tier: stdlib -->
The empty question is asked BEFORE anything is allocated, so the refused path allocates
nothing and owes nothing: building the record, stamping every field and only then raising
would leave the throw EDGE holding a release, which leaks. This runs the refusal a hundred
times because a single stranded allocation is easy
to miss and a hundred are not; the leak gate is the assertion.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var refusals = 0 as Count
	for _ in 0 upto 100 'many'
		var arr = ByteArray.create()
		let cursor = try arr.managed.createCursor() otherwise 'empty'
			refusals = refusals + 1
			continue
		end 'empty'
		refusals = refusals + (cursor.current() as Count)
	end 'many'
	return refusals as ExitCode
end 'main'
```
```exitcode
100
```

<!-- test: a-cursor-over-a-word-element-strides-by-the-word -->
<!-- tier: stdlib -->
The stride is the RECORD's `element_size@24`, read live, so one cursor serves every
element width.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var arr = IntArray.create()
	arr.push(1000)
	arr.push(2000)
	arr.push(3000)
	var cursor = try arr.managed.createCursor() otherwise return 99
	try cursor.seek(2) otherwise return 98
	return (cursor.current() / 1000) as ExitCode
end 'main'
```
```exitcode
3
```

<!-- test: a-cursor-over-strings-reads-each-element -->
<!-- tier: stdlib -->
`current()` takes its own reference to the element it reads, and releases it when the read value
is dropped. The buffer still owns every element, and the run is leak-gated.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	var cursor = try xs.managed.createCursor() otherwise return 99
	print("{cursor.current()}\n")
	try cursor.advance() otherwise return 98
	print("{cursor.current()}\n")
	return xs.count()
end 'main'
```
```stdout
alpha heap string long enough to require an allocation
beta heap string long enough to require an allocation
```
```exitcode
2
```

<!-- test: the-cursor-keeps-its-source-alive -->
<!-- tier: stdlib -->
⭐ The source's textual last use is the `createCursor()` call. Without the retain the
record is released there and every read afterwards is through reclaimed memory — the
SIGSEGV both references carry a retain to avoid. The intervening allocations make
the reclaimed slot certain to be reused, so a missing retain is a WRONG ANSWER here rather
than a silent pass.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function makeBuffer() returns ByteArray
	var arr = ByteArray.create()
	arr.push(4)
	arr.push(9)
	return arr
end 'makeBuffer'

function main() returns ExitCode
	let cursor = try makeBuffer().managed.createCursor() otherwise return 99
	var churn = 0 as Count
	for _ in 0 upto 500 'churn'
		var scratch = ByteArray.create()
		scratch.push(200)
		churn = churn + scratch.count()
	end 'churn'
	return cursor.current() + (try cursor.peek(1) otherwise 0)
end 'main'
```
```exitcode
13
```

<!-- test: error.the-cursor-roster-is-the-surface -->
<!-- tier: stdlib -->
The refusal RENDERS the roster the dispatch consults, so the sentence cannot claim a member
the arms do not serve — and a roster member with no arm reaches a panic naming the list
instead of being handed this sentence. Six members, and `advanceBy`/`retreatBy` are
deliberately not among them: they are `Iterator`/`BidirectionalIterator` extension methods
written in the corpus in terms of `advance`/`retreat`, so they never reach a compiler arm.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	let cursor = try arr.managed.createCursor() otherwise return 99
	return cursor.frobnicate()
end 'main'
```
```maxoncstderr
error E2015: <fragment>:9:16: Unsupported: `__ManagedMemoryCursor` member 'frobnicate' — the compiler provides current/index/advance/retreat/seek/peek; that list IS the surface, so nothing else is served here
```

<!-- test: current-after-the-source-is-cleared-panics -->
<!-- tier: stdlib -->
⭐⭐ **THE RETAIN KEEPS THE RECORD ALIVE, NOT UNCHANGED.** `clear()` releases every element and
shortens the buffer to nothing while the cursor still names position 0. `current()` reads the
live length, finds its position past the end, and panics with an index out of range — it never
reads the released slot.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	view cursor = try xs.managed.createCursor() otherwise return 99
	xs.clear()
	print("{cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
```
```stderr
panic: index out of range: the cursor's position is past the end of its array
Stack trace:
  in __managed_cursor_current
  in main
  in mrt_start
```

<!-- test: pushing-to-the-source-under-a-live-cursor-is-seen -->
<!-- tier: stdlib -->
Sixty-four pushes past a one-element buffer reallocate it more than once; the cursor reads `buffer@0` and
`length` live rather than snapshotting them, so it reads the element it named before the pushes and can
advance onto the first element they added.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	var cursor = try arr.managed.createCursor() otherwise return 99
	for _ in 0 upto 64 'grow'
		arr.push(20)
	end 'grow'
	let first = cursor.current()
	try cursor.advance() otherwise return 98
	return first + cursor.current()
end 'main'
```
```exitcode
30
```

<!-- test: a-value-read-from-the-cursor-outlives-a-clear-of-its-source -->
<!-- tier: stdlib -->
⭐ A value read from the cursor holds its own reference, so clearing the source afterwards releases only
the array's references and leaves the value intact. The strings are built at run time, so each is a heap
record whose last array reference the `clear` drops: without the cursor's own reference `first` would be
read through a freed record.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	for i in 0 upto 2 'fill'
		xs.push("element {i} of the source, long enough to need a heap record")
	end 'fill'
	view cursor = try xs.managed.createCursor() otherwise return 99
	let first = cursor.current()
	xs.clear()
	print("{first}\n")
	return xs.count()
end 'main'
```
```stdout
element 0 of the source, long enough to need a heap record
```
```exitcode
0
```

<!-- test: current-on-an-empty-slot-panics -->
<!-- tier: stdlib -->
⭐ **AN EMPTY SLOT OF A MANAGED ELEMENT IS AN ABSENCE, AND `current()` NEVER HANDS IT OUT.** `setLength`
publishes two slots nothing filled. `current()` reads element 0, and after the advance meets slot 1, whose
null it would otherwise hand back as a `String`; it panics with the empty-slot message instead, naming the
position and the line of the `current()` call, exactly as a `for` element read of an empty slot does.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.reserve(3)
	try xs.managed.setLength(3) otherwise panic("setLength: capacity just reserved for 3")
	var cursor = try xs.managed.createCursor() otherwise return 99
	print("{cursor.current()}\n")
	try cursor.advance() otherwise return 98
	print("{cursor.current()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
alpha heap string long enough to require an allocation
```
```stderr
panic at current-on-an-empty-slot-panics.test:12: empty slot: element 1 of the array was never filled
Stack trace:
  in __managed_cursor_current
  in main
  in mrt_start
```

<!-- test: a-cursor-stored-in-a-generic-struct-field -->
<!-- tier: stdlib -->
⭐⭐ **THIS IS `stdlib/Array.maxon:490-531`'s `ArrayIterator`, AND IT IS WHY THIS TYPE EXISTS.**
Naming `__ManagedMemoryCursor with Element` inside a generic type, storing one in a field and
forwarding its members is the exact shape that module needs. The iterator owns the cursor, the cursor holds the source, and the whole chain
is released when the iterator goes out of scope — the leak gate is what says so.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type SeqIterator uses Element
	export typealias ElementMemory = __ManagedMemory with Element
	typealias RawCursor = __ManagedMemoryCursor with Element
	var raw as RawCursor

	static function create(source ElementMemory) returns Self throws ArrayError
		let cursor = try source.createCursor() otherwise throw ArrayError.indexOutOfBounds
		return Self{raw: cursor}
	end 'create'

	function current() returns Element
		return raw.current()
	end 'current'

	function index() returns Integer
		return raw.index()
	end 'index'

	function advance() throws ArrayError
		try raw.advance() otherwise throw ArrayError.indexOutOfBounds
	end 'advance'
end 'SeqIterator'

typealias ByteIter = SeqIterator with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
	var it = try ByteIter.create(arr.managed) otherwise return 99
	var total = 0 as Count
	total = total + (it.current() as Count)
	try it.advance() otherwise return 98
	total = total + (it.current() as Count)
	try it.advance() otherwise return 97
	total = total + (it.current() as Count)
	return ((total as Integer) + it.index()) as ExitCode
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
62
```

<!-- test: a-generic-iterator-over-a-managed-element -->
<!-- tier: stdlib -->
The same generic body instantiated at a MANAGED element. `current()` hands back its own
reference to each string, which the reader releases; a reference too few is a corrupted string
or a crash, and one too many is a leak at exit 101.
```maxon
typealias StrArray = Array with String

type SeqIterator uses Element
	export typealias ElementMemory = __ManagedMemory with Element
	typealias RawCursor = __ManagedMemoryCursor with Element
	var raw as RawCursor

	static function create(source ElementMemory) returns Self throws ArrayError
		let cursor = try source.createCursor() otherwise throw ArrayError.indexOutOfBounds
		return Self{raw: cursor}
	end 'create'

	function current() returns Element
		return raw.current()
	end 'current'

	function advance() throws ArrayError
		try raw.advance() otherwise throw ArrayError.indexOutOfBounds
	end 'advance'
end 'SeqIterator'

typealias StrIter = SeqIterator with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	var it = try StrIter.create(xs.managed) otherwise return 99
	print("{it.current()}\n")
	try it.advance() otherwise return 98
	print("{it.current()}\n")
	return xs.count()
end 'main'
```
```stdout
alpha heap string long enough to require an allocation
beta heap string long enough to require an allocation
```
```exitcode
2
```

<!-- test: a-cursor-in-a-loop-strands-nothing -->
<!-- tier: stdlib -->
One cursor per trip, each dropped at the loop body's scope exit. A missing release is a
leak the gate catches; a double release is a crash.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1)
	arr.push(2)
	var total = 0 as Count
	for _ in 0 upto 200 'many'
		let cursor = try arr.managed.createCursor() otherwise return 99
		total = total + (cursor.current() as Count)
	end 'many'
	return (total / 200) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-cursor-alias-cannot-be-created -->
<!-- tier: stdlib -->
### `Cur.create()` is REFUSED, never a compiler PANIC
⛔⛔ **A REFUSAL, NOT A STACK TRACE IN FRONT OF AN AUTHOR.** A cursor is
`ManagedMemoryCursorBuiltinBaseName`'s *"SECOND type no source can construct"* — only
`__ManagedMemory.createCursor()` mints one — so `ProgramSignatures.instanceHasBuiltinCreate` names it beside
the node handle as a builtin without a `create`. Admitted at that gate, this program would meet
`Parser.requireContainerColumnTypes`' *"a container admitted there owes an arm here"* **panic**, which a
cursor can never satisfy because it has no columns to gate.

⇒ The predicate names both uncreatable builtins, so the refusal is the one the node handle
gets: the sentence `containerStaticSurfaceSentence` owns, anchored on the member the author wrote.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Cur = __ManagedMemoryCursor with Int

function main() returns ExitCode
	var c = Cur.create()
	return 0
end 'main'
```
```maxoncstderr
error E2073: <fragment>:6:14: `__ManagedMemoryCursor` static method 'create' — a cursor has no static surface at all, not even `create()`: one exists only because a `__ManagedMemory` handed it out, and `createCursor()` is what mints one
```

<!-- test: error.a-cursor-alias-has-no-other-static-either -->
<!-- tier: stdlib -->
### The same sentence for a static that was never going to exist
The second half of the same defect, through the OTHER door: an unknown static reaches
`containerStaticSurfaceSentence` directly (`parseBuiltinContainerStaticCall`), and that router had no
cursor arm either — so this program met **its** panic rather than `Cur.create()`'s. Two doors, one missing
arm apiece, and neither could be reached from the other's fix.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Cur = __ManagedMemoryCursor with Int

function main() returns ExitCode
	var c = Cur.frobnicate()
	return 0
end 'main'
```
```maxoncstderr
error E2073: <fragment>:6:14: `__ManagedMemoryCursor` static method 'frobnicate' — a cursor has no static surface at all, not even `create()`: one exists only because a `__ManagedMemory` handed it out, and `createCursor()` is what mints one
```

<!-- test: error.a-cursor-alias-create-is-refused-as-a-value -->
<!-- tier: stdlib -->
The READ door asks the same roster as the call door, so `Cur.create` is the cursor's own refusal and never a
claim that `create` is a method.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Cur = __ManagedMemoryCursor with Int

function main() returns ExitCode
	let f = Cur.create
	return 0
end 'main'
```
```maxoncstderr
error E2073: <fragment>:6:14: `__ManagedMemoryCursor` static method 'create' — a cursor has no static surface at all, not even `create()`: one exists only because a `__ManagedMemory` handed it out, and `createCursor()` is what mints one
```

<!-- test: error.a-cursor-alias-create-is-refused-as-a-store -->
<!-- tier: stdlib -->
The STORE door is the third reader of the roster.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Cur = __ManagedMemoryCursor with Int

function main() returns ExitCode
	Cur.create = 5
	return 0
end 'main'
```
```maxoncstderr
error E2073: <fragment>:6:6: `__ManagedMemoryCursor` static method 'create' — a cursor has no static surface at all, not even `create()`: one exists only because a `__ManagedMemory` handed it out, and `createCursor()` is what mints one
```
