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

### The closed-end twin moves with it, and its overflow guard survives

`emitBoundaryPositionOutOfRange` bounds a POSITION BETWEEN elements — `[0, length]`, because
one-past-the-end is a legal place to stand — so its unsigned form is `cmp` + `ja` rather than `jae`.
Its second job is to refuse a COMPUTED boundary that overflowed: `__managed_fill`'s `start + count`
for two non-negative operands either fits an `i64` or wraps into `[-2^63, -2]`, never into a small
positive number. Under the old signed test the wrapped end was caught by the NEGATIVE disjunct; under
the unsigned one it is `>= 2^63` while every length is below `2^63`, so it is caught by the compare
itself. Same values refused, one instruction instead of six.

## Tests

<!-- test: a-negative-index-is-still-refused -->
⭐ **THE CONTROL FOR THE WHOLE ROW.** The unsigned rewrite is only equivalent because a negative
index wraps ABOVE the length; write the compare signed and `-1` reads as in bounds, the guard admits
it, and `buffer + index·8` addresses the word BEFORE the buffer. Every one of `get`, `set` and
`remove` is checked here because they are three entries through the one guard, and the array is read
back afterwards so an admitted write would be a wrong answer rather than a silent pass.

⛔ **SABOTAGE-VERIFIED, AND THE VERDICTS ARE FAULTS RATHER THAN GOLDEN DRIFT.** With
`BoundsCompareOperandType` set to `StdType.i64` — the one-token change that drops the negative half —
**six of this spec's eight cases go red**: this one at exit 1, `a-runtime-negative-index-is-refused`
at 1, `a-byte-strided-element-is-guarded-the-same-way` at 1, `the-two-extreme-indices-are-refused` at
2, `a-fill-window-that-overflows-is-refused` at 2, and `an-empty-container-refuses-every-index` at
**3221225477 — `0xC0000005`, a Windows ACCESS VIOLATION**, which is the admitted read landing outside
the allocation. The two that stay green are the two with no negative index in them
(`the-last-element-is-in-and-the-length-is-not` and the fill window), which is what says the split is
the negative half and not something else. ⚠ Measured TWICE, and the verdicts MOVED between them: with
`otherwise 0` this case answered **exit 101** — a leak, because the admitted `set(-1)` overwrote the
allocation header — and with the distinct `otherwise 0 - 5` it answers exit 1, a plain wrong answer
caught one check earlier. Both are red; the second is the one this file pins.
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
	if (try a.get(-1) otherwise 0 - 5) != 0 - 5 'negativeGet'
		return 1
	end 'negativeGet'

	try a.set(-1, value: 99) otherwise 'negativeSet'
		if (try a.get(-2) otherwise 0 - 5) != 0 - 5 'negativeGetAgain'
			return 2
		end 'negativeGetAgain'
		if (try a.remove(-1) otherwise 0 - 5) != 0 - 5 'negativeRemove'
			return 3
		end 'negativeRemove'
		// Nothing above may have touched the array.
		if a.count() != 3 'countUnchanged'
			return 4
		end 'countUnchanged'
		if (try a.get(0) otherwise 0 - 5) != 10 'firstUnchanged'
			return 5
		end 'firstUnchanged'
		if (try a.get(2) otherwise 0 - 5) != 30 'lastUnchanged'
			return 6
		end 'lastUnchanged'
		return 0
	end 'negativeSet'

	// The `set` was supposed to throw.
	return 7
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

⛔ Sabotage-verified together with the case above: `BoundsCompareOperandType = StdType.i64` takes
BOTH red, this one at exit 1 — a WRONG ANSWER out of `readAt`, not a fault, because the admitted read
of `buffer[-3]` lands inside the allocation's own header rather than off the page.
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function readAt(a IntArray, i Int) returns Int
	return try a.get(i) otherwise 0 - 7
end 'readAt'

function main() returns ExitCode
	var a = IntArray.create()
	a.push(11)
	a.push(22)
	a.push(33)

	let n = a.count()
	if readAt(a, i: 0 - n) != 0 - 7 'minusCount'
		return 1
	end 'minusCount'
	if readAt(a, i: 0 - n - 1) != 0 - 7 'belowThat'
		return 2
	end 'belowThat'
	if readAt(a, i: 1 - n) != 0 - 7 'minusTwo'
		return 3
	end 'minusTwo'
	if readAt(a, i: n - 1) != 33 'lastStillReadable'
		return 4
	end 'lastStillReadable'
	if readAt(a, i: n) != 0 - 7 'atLength'
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
	let bottom = (0 - 9223372036854775807) - 1

	if (try a.get(top) otherwise 0 - 5) != 0 - 5 'topGet'
		return 1
	end 'topGet'
	if (try a.get(bottom) otherwise 0 - 5) != 0 - 5 'bottomGet'
		return 2
	end 'bottomGet'
	if (try a.remove(bottom) otherwise 0 - 5) != 0 - 5 'bottomRemove'
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
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function probe(a IntArray) returns Int
	var hits = 0
	if (try a.get(0) otherwise 0 - 1) != 0 - 1 'zero'
		hits = hits + 1
	end 'zero'
	if (try a.get(1) otherwise 0 - 1) != 0 - 1 'one'
		hits = hits + 1
	end 'one'
	if (try a.get(-1) otherwise 0 - 1) != 0 - 1 'negative'
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
```maxon
typealias Int = int(i64.min to i64.max)
typealias IntArray = Array with Int

function main() returns ExitCode
	var a = IntArray.create()
	for i in 0 upto 5 'seed'
		a.push(i * 10)
	end 'seed'

	let n = a.count()
	if (try a.get(n - 1) otherwise 0 - 1) != 40 'lastIn'
		return 1
	end 'lastIn'
	if (try a.get(n) otherwise 0 - 1) != 0 - 1 'lengthOut'
		return 2
	end 'lengthOut'
	if (try a.get(n + 1) otherwise 0 - 1) != 0 - 1 'pastLengthOut'
		return 3
	end 'pastLengthOut'

	// The same three through the WRITE entry, which shares the guard.
	try a.set(n - 1, value: 44) otherwise 'lastInWrite'
		return 4
	end 'lastInWrite'
	try a.set(n, value: 55) otherwise 'lengthOutWrite'
		if (try a.get(n - 1) otherwise 0 - 1) != 44 'writeLanded'
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
every String element on the old seven-instruction guard. The fragment shows the byte arm's
`cmp`/`jae` beside the word arm's.
```maxon
typealias Byte = int(0 to 255)
typealias Bytes = Array with Byte

function main() returns ExitCode
	var b = Bytes.create()
	b.push(1)
	b.push(2)
	b.push(3)

	if (try b.get(-1) otherwise 200) != 200 'negative'
		return 1
	end 'negative'
	if (try b.get(3) otherwise 200) != 200 'atLength'
		return 2
	end 'atLength'
	if (try b.get(2) otherwise 200) != 3 'lastIn'
		return 3
	end 'lastIn'

	try b.set(-1, value: 9) otherwise 'negativeSet'
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
