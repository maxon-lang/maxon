---
feature: unsigned-bounds-guard
status: experimental
keywords: [optimizer, codegen, bounds, index, unsigned, guard, array, negative]
category: codegen
---
# One unsigned compare is the whole bounds guard

## Documentation

A bounds test asks two questions — *"is the index below zero?"* and *"is it at or above the
length?"* — and `ManagedMemoryRuntime.emitIndexOutOfRange` is the one home of both, shared by
`__managed_get` / `__managed_set` / `__managed_remove` / `__managed_mem_swap`, by `ListRuntime`'s
indexed entries, and by the fast arms `InlineManagedPrimitives` puts at every array read and write in
every program.

Asked SIGNED they are two compares. `cmp`/`setcc` twice, `or`, and then — because a `bitOr` is not a
compare and the branch on it cannot fuse — a third `cmp` against 0 and the `jcc`:

```
loadRegBaseDisp rax, [rbx + 8]   ; the length
cmpRegImm32     r14, 0
setccReg        less, rcx        ; index < 0 ?
cmpRegReg       r14, rax
setccReg        greaterEqual, rax ; index >= length ?
orRegReg        rcx, rcx, rax
cmpRegImm32     rcx, 0
jcc             notEqual, __im_slow
```

Asked UNSIGNED they are ONE compare, because a negative index reinterpreted as unsigned is larger
than any non-negative length:

```
loadRegBaseDisp rax, [rbx + 8]
cmpRegReg       r14, rax
jcc             aboveEqual, __im_slow
```

That is x64's standard bounds-check idiom, `cmp`+`jae`; arm64 spells it `cmp`/`cset hs`/`cbnz` and
wasm `i64.ge_u`. shv2 could not say it until upstream's X11 put signedness on a compare's
`operandType` — `emitIsNegative`'s header still claimed *"shv2's `StdCmpPred` has NO unsigned
compare"* two weeks after that stopped being true, and that sentence was the stated reason the guard
cost seven instructions.

### ⛔⛔ THE PRECONDITION IS `length >= 0`, AND IT IS THE WHOLE CORRECTNESS ARGUMENT

For a NEGATIVE limit the two readings are OPPOSITE rather than merely different. Signed, every index
is `>= length`, so the guard refuses EVERYTHING. Unsigned, the limit reads as enormous, so the guard
ADMITS everything — an out-of-bounds heap read or write with no diagnostic at all. This tree has
already paid that bill once in the other direction: `__mf_read`'s capacity bound was useless because
a logical shift turned `capacity = -1` into `0x1FFFFFFFFFFFFFFF`, and a 24-byte read into a 4-byte
zero-copy view was ACCEPTED and rewrote 20 bytes past the parent's allocation.

`BoundsCompareOperandType`'s header carries the argument in full. Its three parts: `length@8` carries
no sentinel (`BufferOwnership`'s negative values live in `capacity@16` alone, at a type whose range
stops at `-1`); every writer of `length@8` publishes a non-negative, the two that take the number
from the user refusing a negative one first; and the single guard whose limit IS `capacity@16` — the
inlined `__managed_mem_set` — is emitted BEHIND `emitBufferNotOwned`, which refuses every sentinel
capacity, so the bound runs on one already proven non-negative.

### ⛔⛔ WHERE A NEGATIVE INDEX IS STILL *REACHABLE* — AND IT IS NO LONGER THE `Array` SURFACE

**This row's subject is a NEGATIVE index arriving at the guard, and on `Array.get`/`set`/`resize` one
no longer can.** `stdlib/Array.maxon` declares those doors over `ElementIndex = int(0 to i64.max)`,
and a declared lower bound of 0 is ENFORCED: a foldable negative is `E3005` at compile time and a
laundered one is an uncatchable `Range check failed` panic at the door
(`Parser.recordArmServedIndexRangeCheck`). Neither ever reaches `emitIndexOutOfRange`. Written
against the `Array` surface, this row's controls would be testing the range check and calling it a
bounds guard — a case that cannot compile is not a control.

**The `__ManagedMemory` BUFFER surface is where they still arrive, and the exemption is deliberate
rather than an oversight to close.** A buffer member is a compiler BUILTIN whose index is a machine
word and not a stdlib alias, so `recordArmServedIndexRangeCheck` returns before recording anything
and `specs-shv2/managed-memory-methods.md` pins that `arr.managed.get(-1)` still THROWS — checked at
that file, whose `bounds-negative-index` case returns its `otherwise 7`. (The `set(-1)`
half of that exemption is pinned here rather than there, by the two cases below that use it.) So
`a.managed.get/set/remove/swap` is exactly one thing: the single unsigned compare, standing alone,
with a negative index in front of it. That is why every negative-index case below is written on it.

⚠ **The two surfaces answer DIFFERENTLY and that difference is itself pinned**, by
`a-laundered-negative-is-a-panic-at-the-array-door-and-a-throw-at-the-buffers` below — one program
holding both, so nothing can quietly move one to match the other. The `Array` half of that split has
its own home in `specs-shv2/arrays.md`
(`get-laundered-negative-index-panics-at-the-door` and its `set`/`insert` siblings).

### The closed-end twin moves with it, and its overflow guard survives

`emitBoundaryPositionOutOfRange` bounds a POSITION BETWEEN elements — `[0, length]`, because
one-past-the-end is a legal place to stand — so its unsigned form is `cmp` + `ja` rather than `jae`.
Its second job is to refuse a COMPUTED boundary that overflowed: `__managed_fill`'s `start + count`
for two non-negative operands either fits an `i64` or wraps into `[-2^63, -2]`, never into a small
positive number. Under the old signed test the wrapped end was caught by the NEGATIVE disjunct; under
the unsigned one it is `>= 2^63` while every length is below `2^63`, so it is caught by the compare
itself. Same values refused, one instruction instead of six.

### ⛔ SABOTAGE-VERIFIED — RE-MEASURED 2026-08-30, AND THE EARLIER VERDICTS ARE SUPERSEDED

The one-token change is `ManagedMemoryRuntime.BoundsCompareOperandType`, `StdType.u64` → `StdType.i64`:
it drops the negative half and leaves the past-the-end half standing.

⛔ **THE VERDICT PARAGRAPH THAT STOOD HERE IS DEAD AND MUST NOT BE RESTORED.** It read *"six of this
spec's eight cases go red"* and named exit codes including a `0xC0000005` ACCESS VIOLATION — measured
honestly, against cases that indexed the `Array` surface. Three of those six then stopped COMPILING
when `ElementIndex` gained its lower bound, so the recorded verdict was describing programs that no
longer existed. A verdict nobody can re-run is a claim; this one is re-run and re-stated with the
cases as they are now.

**MEASURED 2026-08-30 against the cases as they stand below: SEVEN of this spec's nine go red.**

| case | verdict |
|---|---|
| `a-negative-index-is-still-refused` | exit **1** |
| `a-runtime-negative-index-is-refused` | exit **1** |
| `the-two-extreme-indices-are-refused` | exit **2** |
| `an-empty-container-refuses-every-index` | **3221225477** — `0xC0000005`, a Windows ACCESS VIOLATION |
| `a-laundered-negative-is-a-panic-…-and-a-throw-at-the-buffers` | stderr mismatch: **no panic at all**, the run exits 1 out of `bufferThrows` |
| `a-fill-window-that-overflows-is-refused` | exit **2** |
| `a-byte-strided-element-is-guarded-the-same-way` | exit **1** |

⚠ **The two that stay green are the two with no negative and no overflowed boundary in them**:
`the-last-element-is-in-and-the-length-is-not` (every index non-negative, so the two readings agree)
and `one-past-the-end-is-a-legal-fill-window-and-one-more-is-not` (both windows in range). That
partition IS the claim: what the sabotage removes is the negative half, and nothing else.

⛔ **TWO THINGS A PREDICTION GOT WRONG HERE, BOTH CAUGHT ONLY BY RUNNING IT** — recorded because the
paragraph this replaced was itself a prediction that outlived its cases:

  • **`a-fill-window-that-overflows-is-refused` GOES RED, and it was expected to stay green** on the
    reasoning that it holds no negative index. It holds no negative *index* and its window END is
    negative: `start + count` wraps into `[-2^63, -2]`, and with the disjunct gone a single SIGNED
    compare reads that as far below the length and ADMITS it. The overflow guard is not an extra the
    unsigned reading merely keeps — the unsigned reading is the ONLY thing catching it now.
  • **The `0xC0000005` did NOT go away when the case moved to the buffer surface**, though it was
    predicted to become an ordinary wrong answer. A freshly `create()`d container has no allocation
    for an admitted read to land inside, on either surface, so the read is still off the page.

⚖ **Do not re-state a verdict here without re-running it.** These numbers are a MEASUREMENT of one
tree on one day, and the case list they range over has already changed once underneath them.

## Tests

<!-- test: a-negative-index-is-still-refused -->
⭐ **THE CONTROL FOR THE WHOLE ROW.** The unsigned rewrite is only equivalent because a negative
index wraps ABOVE the length; write the compare signed and `-1` reads as in bounds, the guard admits
it, and `buffer + index·8` addresses the word BEFORE the buffer. All FOUR entries through the one
guard are checked — `get`, `set`, `remove` and `swap`, which is `emitIndexOutOfRange`'s whole caller
list — and the array is read back afterwards, through the `Array` surface at non-negative indices, so
an admitted write is a wrong answer rather than a silent pass.

⚠ **Written on the BUFFER surface because that is the only surface a negative index still reaches** —
see the ⛔⛔ section above. `a.count()` and `a.get(0)` read the SAME record the buffer members wrote
through, so "nothing was touched" is still checked against the array the program actually holds.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)

	// ⚠ The `otherwise` value is NOT 0. An admitted out-of-bounds read returns whatever word sits
	// before the buffer, and 0 is a value it could plausibly hold — a fallback the read can collide
	// with is a case that would pass under the very rewrite it exists to refuse.
	if (try a.managed.get(-1) otherwise -5) != -5 'negativeGet'
		return 1
	end 'negativeGet'
	if (try a.managed.remove(-1) otherwise -5) != -5 'negativeRemove'
		return 2
	end 'negativeRemove'

	try a.managed.swap(-1, 0) otherwise 'negativeSwap'
		try a.managed.set(-1, value: 99) otherwise 'negativeSet'
			if (try a.managed.get(-2) otherwise -5) != -5 'negativeGetAgain'
				return 3
			end 'negativeGetAgain'
			// Nothing above may have touched the array.
			if a.count() != 3 'countUnchanged'
				return 4
			end 'countUnchanged'
			if (try a.get(0) otherwise -5) != 10 'firstUnchanged'
				return 5
			end 'firstUnchanged'
			if (try a.get(2) otherwise -5) != 30 'lastUnchanged'
				return 6
			end 'lastUnchanged'
			return 0
		end 'negativeSet'

		// The `set` was supposed to throw.
		return 7
	end 'negativeSwap'

	// The `swap` was supposed to throw.
	return 8
end 'main'
```
```exitcode
0
```

<!-- test: a-runtime-negative-index-is-refused -->
⭐⭐ **THE SECOND CONTROL, AND THE ONE THAT PROVES THE FIRST IS NOT PASSING FOR A DIFFERENT REASON.**
A literal `-1` is a CONSTANT the operand fold moves into the compare and swaps the operands of, so
the case above pins `cmp length, -1` / `jbe` rather than `cmp index, length` / `jae` — a different
instruction pair reaching the same slow arm. Here the index is derived from `count()`, which is a
runtime load, so the guard is the two-register form the row is actually about. Every access is
negative by a different amount, and the array is read back so an admitted one is a wrong answer.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function readAt(a IntArray, i Int) returns Int
	return try a.managed.get(i) otherwise -7
end 'readAt'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(11)
	a.push(22)
	a.push(33)

	let n = a.count()
	if readAt(a, i: -n) != -7 'minusCount'
		return 1
	end 'minusCount'
	if readAt(a, i: -n - 1) != -7 'belowThat'
		return 2
	end 'belowThat'
	if readAt(a, i: 1 - n) != -7 'minusTwo'
		return 3
	end 'minusTwo'
	if readAt(a, i: n - 1) != 33 'lastStillReadable'
		return 4
	end 'lastStillReadable'
	if readAt(a, i: n) != -7 'atLength'
		return 5
	end 'atLength'
	if a.count() != 3 'countUnchanged'
		return 6
	end 'countUnchanged'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-two-extreme-indices-are-refused -->
`i64.max` and `i64.min` — the two ends of the index space, and the two the unsigned reading treats
most differently from the signed one. `i64.max` is enormous under BOTH readings; `i64.min` is the
most negative signed value and `2^63` unsigned, which is above every length a buffer can have. Both
must be refused, and both are computed rather than written as literals so nothing folds the access
away.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.push(2)

	let top = 9223372036854775807
	let bottom = (-9223372036854775807) - 1

	if (try a.managed.get(top) otherwise -5) != -5 'topGet'
		return 1
	end 'topGet'
	if (try a.managed.get(bottom) otherwise -5) != -5 'bottomGet'
		return 2
	end 'bottomGet'
	if (try a.managed.remove(bottom) otherwise -5) != -5 'bottomRemove'
		return 3
	end 'bottomRemove'
	if a.count() != 2 'countUnchanged'
		return 4
	end 'countUnchanged'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-empty-container-refuses-every-index -->
`length == 0` is the edge where the unsigned compare has nothing to admit: `index >=u 0` is TRUE for
every index there is, including 0 itself. Read through a container that has been filled and then
cleared as well as one that was never filled, because `clear` publishes the 0 through a different
writer than `create` does.

⚠ **`managed.get` is bounded by the LENGTH, not the capacity** — the cleared container keeps the
capacity its two pushes bought, so if the buffer read were capacity-bounded this case would admit
index 0 and go red for a reason that has nothing to do with signedness. `managed.set` is the one
buffer member whose limit is `capacity@16`, which is why it is not used here.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function probe(a IntArray) returns Int
	var hits = 0
	if (try a.managed.get(0) otherwise -1) != -1 'zero'
		hits = hits + 1
	end 'zero'
	if (try a.managed.get(1) otherwise -1) != -1 'one'
		hits = hits + 1
	end 'one'
	if (try a.managed.get(-1) otherwise -1) != -1 'negative'
		hits = hits + 1
	end 'negative'
	return hits
end 'probe'

function main() returns ExitCode
	var fresh = IntArray.create()
	if probe(fresh) != 0 'freshAdmittedSomething'
		return 1
	end 'freshAdmittedSomething'

	var emptied = IntArray.create()
	emptied.push(7)
	emptied.push(8)
	emptied.clear()
	if emptied.count() != 0 'clearedCount'
		return 2
	end 'clearedCount'
	if probe(emptied) != 0 'clearedAdmittedSomething'
		return 3
	end 'clearedAdmittedSomething'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-last-element-is-in-and-the-length-is-not -->
The half-open end itself: `length - 1` is the last legal index, `length` is the first illegal one and
`length + 1` is past it. `jae` is what puts the boundary exactly there — `ja` would admit `length`,
which is the mistake the closed-end twin below exists to make deliberately.

⚠ This one stays on the `Array` surface, and it is the case that says why the others could not: every
index in it is NON-NEGATIVE, so `ElementIndex`'s lower bound has nothing to refuse and the access
reaches the bounds guard exactly as it always did. The surface a case is written on is decided by
whether it needs a negative, not by preference.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	for i in 0 upto 5 'seed'
		a.push(i * 10)
	end 'seed'

	let n = a.count()
	if (try a.get(n - 1) otherwise -1) != 40 'lastIn'
		return 1
	end 'lastIn'
	if (try a.get(n) otherwise -1) != -1 'lengthOut'
		return 2
	end 'lengthOut'
	if (try a.get(n + 1) otherwise -1) != -1 'pastLengthOut'
		return 3
	end 'pastLengthOut'

	// The same three through the WRITE entry, which shares the guard.
	try a.set(n - 1, value: 44) otherwise 'lastInWrite'
		return 4
	end 'lastInWrite'
	try a.set(n, value: 55) otherwise 'lengthOutWrite'
		if (try a.get(n - 1) otherwise -1) != 44 'writeLanded'
			return 5
		end 'writeLanded'
		if a.count() != 5 'countUnchanged'
			return 6
		end 'countUnchanged'
		return 0
	end 'lengthOutWrite'

	// The out-of-range `set` was supposed to throw.
	return 7
end 'main'
```
```exitcode
0
```

<!-- test: a-laundered-negative-is-a-panic-at-the-array-door-and-a-throw-at-the-buffers -->
⭐⭐ **THE TWO SURFACES, IN ONE PROGRAM, DISAGREEING ON PURPOSE.** The same laundered `-1` is a
CATCHABLE `indexOutOfBounds` at `a.managed.get` — which is this row's guard doing its job — and an
UNCATCHABLE `Range check failed` panic at `a.get`, which is `ElementIndex`'s declared lower bound
refusing before the guard is ever reached. Both halves are in one `main` so that moving either
surface toward the other reddens this case rather than quietly rewriting what the row means.

The `Array` half is pinned in its own file too — `arrays/get-laundered-negative-index-panics-at-the-door`
and its `set`/`insert` siblings — which is where an author looking for the array door's contract will
be. What that file cannot show is the CONTRAST, and the contrast is what stops a future rewrite from
"fixing" the buffer to panic as well and taking this row's only remaining subject with it.

⚠ The negative is laundered through a call so it is not a foldable constant: written `a.get(-1)` the
array half would be `E3005` at COMPILE time and this case would have no runtime behaviour to pin at
all.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function launder(n Int) returns Int
	return n
end 'launder'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(10)
	a.push(20)
	a.push(30)

	// The BUFFER surface: caught, and the program keeps running.
	if (try a.managed.get(launder(-1)) otherwise -5) != -5 'bufferThrows'
		return 1
	end 'bufferThrows'
	if a.count() != 3 'countUnchanged'
		return 2
	end 'countUnchanged'

	// The ARRAY surface: the `otherwise` is unreachable — the door panics first.
	return try a.get(launder(-1)) otherwise 99
end 'main'
```
```exitcode
1
```
```stderr
panic at a-laundered-negative-is-a-panic-at-the-array-door-and-a-throw-at-the-buffers.test:24: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: one-past-the-end-is-a-legal-fill-window-and-one-more-is-not -->
The CLOSED-end twin, `emitBoundaryPositionOutOfRange`, reached through `__managed_fill`'s window end
`start + count`. `stop == length` is legal — a window may reach the length exactly — and `ja` rather
than `jae` is the whole of what makes it so. `stop == length + 1` is not.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	for _ in 0 upto 4 'seed'
		a.push(0)
	end 'seed'

	// stop == length: the window reaches the end exactly, and applies.
	let whole = try a.managed.fill(0, count: 4, value: 3) otherwise 'wholeErr'
		return 1
	end 'wholeErr'
	if not whole 'wholeDeclined'
		return 2
	end 'wholeDeclined'

	// stop == length + 1: one element past, refused, and nothing written.
	try a.managed.fill(1, count: 4, value: 9) otherwise 'pastEnd'
		var sum = 0
		for v in a 'check'
			sum = sum + v
		end 'check'
		if sum != 12 'fillLanded'
			return 3
		end 'fillLanded'
		return 0
	end 'pastEnd'

	// The past-the-end fill was supposed to throw.
	return 4
end 'main'
```
```exitcode
0
```

<!-- test: a-fill-window-that-overflows-is-refused -->
⭐ **THE OVERFLOW GUARD, WHICH THE UNSIGNED READING KEEPS RATHER THAN INHERITS.** `start + count` for
two non-negative operands lands in `[0, 2^64-2]`, so it either fits an `i64` or wraps into
`[-2^63, -2]` — never into a small positive number. The old signed test caught the wrapped end
through its NEGATIVE disjunct; the unsigned one catches it because a wrapped end is at or above
`2^63` while every length is below it. Measured on both compilers before the guard existed:
`fill(1, count: i64.max, …)` on a length-3 buffer answered `applied` while writing nothing.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	a.push(1)
	a.push(1)
	a.push(1)

	let huge = 9223372036854775807

	try a.managed.fill(1, count: huge, value: 5) otherwise 'wrapped'
		var sum = 0
		for v in a 'check'
			sum = sum + v
		end 'check'
		if sum != 3 'somethingWasWritten'
			return 1
		end 'somethingWasWritten'
		return 0
	end 'wrapped'

	// The overflowing window was supposed to throw, not report that it applied.
	return 2
end 'main'
```
```exitcode
0
```

<!-- test: a-byte-strided-element-is-guarded-the-same-way -->
The other single-op arm. `InlineManagedPrimitives` emits one fast arm per stride, and both call
`guardIndexInRange`, so a rewrite that reached only the word arm would leave every `ByteArray` and
every String element on the old seven-instruction guard.

⛔ **THIS PARAGRAPH SAID *"the fragment shows the byte arm's `cmp`/`jae`"*, AND THE FRAGMENT HAS NEVER
CONTAINED ONE** — checked at the file, on this version and on the one before it: zero `aboveEqual`,
ten `belowEqual`. It is still the UNSIGNED guard, spelled the other way round. Every index here is a
LITERAL, so the operand fold moves the constant into the compare and swaps it, and `__im_byte`'s guard
comes out `cmp length, -1` / `jbe` — the same slow arm reached by the same unsigned reading. The
two-register `cmp index, length` / `jae` is what `a-runtime-negative-index-is-refused` pins, and its
fragment is the one that carries it. **What this case pins is the STRIDE — that the byte arm has a
guard of this family at all — not a mnemonic.**
```maxon
typealias Byte = int(0 to 255)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	b.push(1)
	b.push(2)
	b.push(3)

	if (try b.managed.get(-1) otherwise 200) != 200 'negative'
		return 1
	end 'negative'
	if (try b.managed.get(3) otherwise 200) != 200 'atLength'
		return 2
	end 'atLength'
	if (try b.managed.get(2) otherwise 200) != 3 'lastIn'
		return 3
	end 'lastIn'

	try b.managed.set(-1, value: 9) otherwise 'negativeSet'
		if (try b.get(0) otherwise 200) != 1 'firstUnchanged'
			return 4
		end 'firstUnchanged'
		return 0
	end 'negativeSet'

	// The negative `set` was supposed to throw.
	return 5
end 'main'
```
```exitcode
0
```
