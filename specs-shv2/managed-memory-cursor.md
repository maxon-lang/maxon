---
feature: managed-memory-cursor
status: stable
keywords: [managed-memory, cursor, iterator, bytearray, ownership]
category: dev
---

# `__ManagedMemoryCursor`

## Documentation

`__ManagedMemory.createCursor()` hands back a `__ManagedMemoryCursor with Element` — a
navigable position inside the buffer whose defining invariant is that it ALWAYS POINTS AT A
LIVE ELEMENT. That invariant is what makes `current()` and `index()` infallible: the only
way to reach an invalid position is to ask for one, and every navigation member
(`advance`, `retreat`, `seek`, `peek`) is throwing and refuses rather than moving.
`createCursor()` itself throws on an EMPTY buffer, because there is no valid position to
start at.

`stdlib/Array.maxon:490-531`'s `ArrayIterator` is the only consumer and therefore the spec:
it stores one in a field and forwards all six members, re-labelling the buffer's failures
as `IterationError`.

### The cursor RETAINS its source

⭐ A cursor is a second reader of a record it does not own. It therefore takes its own
reference to the source record, and releases it when the cursor is dropped. Without that
retain the source's textual last use is the `createCursor()` call itself, the record is
freed there, and the cursor reads through a reclaimed allocation — a measured SIGSEGV in
both reference compilers (`stdlib/Internals.maxon:3040-3050`,
`maxon-selfhosted/…/LowerMaxonToStd.maxon:9750-9816`).

⚠ **IT HOLDS THE RECORD, NOT THE RECORD'S FIELDS.** `length` and `element_size` are read
LIVE on every access rather than snapshotted at creation, so a cursor cannot come to
disagree with the buffer it is pointed at. Both references snapshot `buffer`/`length`/
`element_size` into the cursor and pay for it: a snapshot of `buffer@0` is precisely what
dangles across a reallocation, and a snapshot of `length` reports positions the buffer no
longer has.

### …and it LOCKS its source, which is a second guarantee

⚖ USER RULING (PLAN `S2l`): *"an array cursor locks its source."* Staying ALIVE is not the
same as staying UNCHANGED — `clear()` empties a record the retain is still holding — so a
cursor is additionally recorded as a standing BORROW of its source and a write under it is
E3070. The two error cases below are the programs; both faulted with `0xC0000005` before the
borrow was minted.

⚠ **THE LOCK IS ONE HOP, AND THAT LIMIT IS NOT THE CURSOR'S.** A value read OUT of a cursor
(`let s = cursor.current()`) borrows the CURSOR, and the borrow set does not compose: once
the cursor's own last use has passed, its lock on the source expires even though `s` still
points inside it. MEASURED, and MEASURED to be general — the identical shape over a nested
`Array with (Array with String)`, with no cursor anywhere, faults the same way. So it is a
transitivity gap in E3070 rather than anything this type introduces, and fixing it here
would have been a fix in the wrong place.

## Tests

<!-- test: cursor-reads-the-first-element -->
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
The cursor is refused rather than moved, so `index()` still reports the last valid
position afterwards — that is what "always points at a live element" means.
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
shv2 asks the empty question BEFORE it allocates, so the refused path allocates nothing and
owes nothing — where both references build the record, stamp every field and only then
raise, leaving the throw EDGE holding a release (v1's own header records the leak that went
with it). This runs the refusal a hundred times because a single stranded allocation is easy
to miss and a hundred are not; the leak gate is the assertion.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var refusals = 0
	for _ in 0 upto 100 'many'
		var arr = ByteArray.create()
		let cursor = try arr.managed.createCursor() otherwise 'empty'
			refusals = refusals + 1
			continue
		end 'empty'
		refusals = refusals + cursor.current()
	end 'many'
	return refusals as ExitCode
end 'main'
```
```exitcode
100
```

<!-- test: a-cursor-over-a-word-element-strides-by-the-word -->
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

<!-- test: a-cursor-over-strings-hands-back-a-borrow -->
`current()` reads the slot; it does not take ownership of what is in it. The buffer still
owns every element, and the run is leak-gated.
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
⭐ The source's textual last use is the `createCursor()` call. Without the retain the
record is released there and every read afterwards is through reclaimed memory — the
measured SIGSEGV both references carry a retain to avoid. The intervening allocations make
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
	var churn = 0
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
error E2015: <fragment>:9:16: Unsupported: `__ManagedMemoryCursor` member 'frobnicate' — shv2 provides current/index/advance/retreat/seek/peek; that list IS the surface, so nothing else is served here
```

<!-- test: error.clearing-the-source-under-a-live-cursor -->
⭐⭐ **A CURSOR IS A STANDING BORROW OF ITS SOURCE, AND THIS IS THE PROGRAM THAT SAYS SO.**
⚖ USER RULING (PLAN `S2l`): *"an array cursor locks its source."* The retain keeps the record
ALIVE, which is a different guarantee from keeping it UNCHANGED — `clear()` releases every
element and zeroes its slot while the cursor still names position 0. MEASURED before the
borrow was minted: this exact program compiled clean and faulted with **0xC0000005**.

⚠ The refusal is E3070 — the same one an outstanding `get` borrow already earns — because a
cursor is the same relationship written down once: a reference into storage someone else
owns.
```maxon
typealias StrArray = Array with String

function main() returns ExitCode
	var xs = StrArray.create()
	xs.push("alpha heap string long enough to require an allocation")
	xs.push("beta heap string long enough to require an allocation")
	let cursor = try xs.managed.createCursor() otherwise return 99
	xs.clear()
	print("{cursor.current()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3070: <fragment>:9:5: cannot mutate 'xs' via 'clear' while it is borrowed by 'cursor' (borrowed at line 8)
```

<!-- test: error.pushing-to-the-source-under-a-live-cursor -->
The refusal is about the CONTAINER and not about its elements, so a trivial element earns it
too. `push` past the capacity reallocates, and although this cursor would survive that (it
reads `buffer@0` live rather than snapshotting it), what it cannot survive is a `length` the
write moves out from under its position — and which of the two a given `push` does is a
run-time fact no parser holds.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	let cursor = try arr.managed.createCursor() otherwise return 99
	arr.push(20)
	return cursor.current()
end 'main'
```
```maxoncstderr
error E3070: <fragment>:9:6: cannot mutate 'arr' via 'push' while it is borrowed by 'cursor' (borrowed at line 8)
```

<!-- test: writing-the-source-after-the-cursors-last-use-is-allowed -->
⭐ The borrow ends at the borrower's LAST USE, not at a scope boundary — which is why this is
accepted where a lexical lock would refuse it. Nothing reads the cursor after the write, so
nothing can observe the change.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(10)
	arr.push(20)
	let cursor = try arr.managed.createCursor() otherwise return 99
	let first = cursor.current()
	arr.clear()
	return first + arr.count()
end 'main'
```
```exitcode
10
```

<!-- test: a-cursor-stored-in-a-generic-struct-field -->
⭐⭐ **THIS IS `stdlib/Array.maxon:490-531`'s `ArrayIterator`, AND IT IS WHY THIS TYPE EXISTS.**
Naming `__ManagedMemoryCursor with Element` inside a generic type, storing one in a field and
forwarding its members is the exact shape that module needs, and the measured blocker on
listing it was `E2055: Type '__ManagedMemoryCursor' has no associated types` at that very
`typealias`. The iterator owns the cursor, the cursor holds the source, and the whole chain
is released when the iterator goes out of scope — the leak gate is what says so.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

type SeqIterator uses Element
	typealias ElementMemory = __ManagedMemory with Element
	typealias RawCursor = __ManagedMemoryCursor with Element
	var raw as RawCursor

	export static function create(source ElementMemory) returns Self throws ArrayError
		let cursor = try source.createCursor() otherwise throw ArrayError.indexOutOfBounds
		return Self{raw: cursor}
	end 'create'

	export function current() returns Element
		return raw.current()
	end 'current'

	export function index() returns Integer
		return raw.index()
	end 'index'

	export function advance() throws ArrayError
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
	var total = 0
	total = total + it.current()
	try it.advance() otherwise return 98
	total = total + it.current()
	try it.advance() otherwise return 97
	total = total + it.current()
	return total + it.index()
end 'main'
typealias Integer = int(i64.min to i64.max)
```
```exitcode
62
```

<!-- test: a-generic-iterator-over-a-managed-element -->
The same generic body instantiated at a MANAGED element. `current()` hands back a BORROW —
the buffer still owns every string — so nothing here is cloned and nothing is released
twice; a wrong answer on either side is a corrupted string and a double release is exit 101.
```maxon
typealias StrArray = Array with String

type SeqIterator uses Element
	typealias ElementMemory = __ManagedMemory with Element
	typealias RawCursor = __ManagedMemoryCursor with Element
	var raw as RawCursor

	export static function create(source ElementMemory) returns Self throws ArrayError
		let cursor = try source.createCursor() otherwise throw ArrayError.indexOutOfBounds
		return Self{raw: cursor}
	end 'create'

	export function current() returns Element
		return raw.current()
	end 'current'

	export function advance() throws ArrayError
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
One cursor per trip, each dropped at the loop body's scope exit. A missing release is a
leak the gate catches; a double release is a crash.
```maxon
typealias Byte = int(0 to u8.max)
typealias ByteArray = Array with Byte

function main() returns ExitCode
	var arr = ByteArray.create()
	arr.push(1)
	arr.push(2)
	var total = 0
	for _ in 0 upto 200 'many'
		let cursor = try arr.managed.createCursor() otherwise return 99
		total = total + cursor.current()
	end 'many'
	return (total / 200) as ExitCode
end 'main'
```
```exitcode
1
```

<!-- test: error.a-cursor-alias-cannot-be-created -->
### `Cur.create()` is REFUSED, and it used to PANIC THE COMPILER
⛔⛔ **FOUND BY THE `W153` REVIEW, MEASURED AS A STACK TRACE IN FRONT OF AN AUTHOR.** A cursor is
`ManagedMemoryCursorBuiltinBaseName`'s *"SECOND type no source can construct"* — only
`__ManagedMemory.createCursor()` mints one — but `ProgramSignatures.instanceHasBuiltinCreate` named only
the node handle as the builtin without a `create`, so this program was ADMITTED at that gate and then met
`Parser.requireContainerColumnTypes`' *"a container admitted there owes an arm here"* **panic**, which a
cursor can never satisfy because it has no columns to gate.

⇒ The predicate now names both uncreatable builtins, so the refusal is the one the node handle already
got: the sentence `containerStaticSurfaceSentence` owns, anchored on the alias the author wrote.
```maxon
typealias Int = int(i64.min to i64.max)
typealias Cur = __ManagedMemoryCursor with Int

function main() returns ExitCode
	var c = Cur.create()
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:6:10: Unsupported: `__ManagedMemoryCursor` static method 'create' — a cursor has no static surface at all, not even `create()`: one exists only because a `__ManagedMemory` handed it out, and `createCursor()` is what mints one
```

<!-- test: error.a-cursor-alias-has-no-other-static-either -->
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
error E2015: <fragment>:6:14: Unsupported: `__ManagedMemoryCursor` static method 'frobnicate' — a cursor has no static surface at all, not even `create()`: one exists only because a `__ManagedMemory` handed it out, and `createCursor()` is what mints one
```
