---
feature: loop-invariant-code-motion
status: experimental
keywords: [optimizer, codegen, loop, licm, hoist, invariant, preheader, speculation]
category: codegen
---
# Loop-invariant code motion — computing once what the loop recomputed every trip

## Documentation

A `for v in a` over an `Array with` an 8-byte element used to reload two words of the array's header on
**every element**: the LENGTH, read in the loop header to test the index, and the BUFFER BASE, read in
the body to address the slot. Neither can change while the loop runs. `hoistLoopInvariants` moves both
into the block that runs once before the loop, which takes the anchor loop's executed body from eight
x64 instructions per element to **six**:

```
  entry:      load rdx, [rcx + 8]        ; length      — hoisted
              load rcx, [rcx + 0]        ; buffer base — hoisted
  forhdr:     cmp  rax, rdx
              jcc  greaterEqual, forexit
  loop:       load rsi, [rcx + rax*8]    ; the element
  __im_cont:  lea  r8, r8, rsi
  forstep:    lea  rax, rax, 1
              jmp  forhdr
```

### TWO PHASES, TWO DIFFERENT SAFETY ARGUMENTS

**Pure computation** needs none beyond the dialect's own flag. `StdOpMeta.isPure` says an op "can be
duplicated, reordered, or dropped", the preheader dominates every block of the loop, and `div`/`mod` —
the arithmetic that CAN fault — are `isPure: false` and never reach the pass. So an invariant `k * 31`
moves with no further question asked, out of ANY block of the loop and out of a whole loop NEST in one
run of the pass.

**A load** is `isPure: false` and carries two questions the pure case does not.

### RULE 1 — a load is invariant when the loop writes nothing

A loop none of whose ops is `isStore` or `isCall` cannot change any location, so every load in it reads
the same bytes every trip. That is a rule, not an alias analysis, and it is not an approximation of one:
a loop holding a store, a call, an atomic or an OS primitive is refused whole and no load in it moves.
`a-loop-that-writes-keeps-its-loads-inside` is the control, and it is a WRONG-ANSWER control rather than
a fragment pin — drop Rule 1 and its exit code changes.

The rule became sufficient for the anchor loop when `EC15` made that loop CALL-FREE by specializing the
element stride; before that, the `__managed_get_unchecked` in the slow arm was a call and Rule 1 would
have refused it.

### RULE 2 — a load may only be speculated where it was going to run anyway

A hoisted op executes even when the loop body never does. In the anchor the length is read in the
HEADER, which runs the moment the preheader does; the buffer base is read in the block the header's
guard branches to, which **for an empty array never runs at all**. Moving a load to a point where it did
not previously execute can fault, so a load needs one of two admissions:

- **2a — must-execute.** Its block dominates every block through which control leaves the loop, and
  every latch. Any execution that either iterates or exits has already run it.
- **2b — a dereferenceability witness.** The same loop is already hoisting, under 2a, a load through the
  SAME address value reaching at least as far past it. A `loadIndirect`'s `(addrId, offset)` pair names
  field `offset` of the object at `addrId` and `ByteOffset` is non-negative by type, so a load the
  program performs unconditionally at a further offset proves the nearer field is inside the same
  object. Only the smaller-or-equal extent is ever admitted — the rule can shrink a proven span, never
  extend one.

2b is what takes the anchor from seven instructions to six: the length load at `[a+8]` is admitted by 2a
and proves 16 bytes, the buffer load at `[a+0]` needs 8, and `a` is the same value.
`an-empty-container-still-runs-the-hoisted-load` is the case that says so: it sums an array of length
zero, so the loop body runs no iterations and the hoisted `[a+0]` is executed by a program that would
never otherwise have read it.

`a-conditionally-executed-load-is-not-speculated` is the other side: a load inside an `if` arm of a
`while` body satisfies Rule 1 (nothing in that loop writes) and neither 2a nor 2b, and stays exactly
where it was.

### RULE 3 — a loop holding a CALL hoists nothing, and this one is about registers

Rules 1 and 2 are correctness. **Rule 3 is a BOUND, it applies to BOTH phases** — arithmetic as well as
loads — and it is the largest thing this pass declines to do.

A hoisted value is live across the WHOLE loop where its computation used to live for a few ops, and a
range crossing a CALL is confined to the five callee-saved registers x64-windows leaves. `EC13` measured
one end of that: shv2 REFUSES rather than spills, and a single reused expression across a call took
`generic-hash-table-regalloc`'s pressured-loop case red with `E5001`. `EC14` measured the other end —
what happens when the allocator does *not* refuse: with the bound off, 48 `map` fragments gained **+96
`loadRegSlot` and +48 `storeSlotReg`** and every one of their frames grew, because the hoisted value was
cold-spilled and reloaded. In `Map.grow` the whole trade was one `leaRegRegImm32` replaced by one
`loadRegSlot` — an ALU op for a memory op, which is not a win at any instruction count.

It costs the anchor nothing, which is what makes it affordable: `EC15` made that loop call-free, and
`regalloc/many-call-crossing` — where nine invariant computations DO leave a loop — holds no call either.

⚠ **NO CASE BELOW GOES RED IF RULE 3 IS DELETED, and that is stated rather than left to be discovered.**
A pressure heuristic has no wrong answer to catch it with; its only pin is the committed `map` and `url`
fragments, which is the weaker kind of evidence by this file's own standard. `regalloc/many-call-crossing`
is the fragment that would move if the rule were *widened* to refuse call-free loops too.

### What is deliberately NOT hoisted

`const` (`classifyArithOperands` answers "not arithmetic" for it, and rematerializing a literal beats a
live range spanning the loop — v1's own header records that a hoisted canonical constant "conflicts with
ABI-constrained uses"); the pure ADDRESS ops `globalAddr` / `rdataAddr` / `funcAddr` / `stackRecordAddr`,
which are always invariant and whose hoist is therefore a pure register-pressure trade with no
instruction-count argument behind it; `div`/`mod`, which trap; **anything at all in a loop holding a
call** (Rule 3); and any loop with no single preheader, or whose preheader's branch could go somewhere
other than the loop header.

## Tests

<!-- test: an-invariant-load-leaves-the-loop -->
The anchor. `for v in a` reads the array's LENGTH in the loop header and its BUFFER BASE in the body,
neither of which can change while the loop runs; both move to `entry`, leaving the header a `cmp`/`jcc`
and the body a single indexed load. The committed fragment for `@total` is the whole reading — six
instructions on the executed path, against eight before this rung. The sum is checked so a wrong address
or a stale length is a wrong exit code rather than a silent pass.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray) returns Word
	var t = 0
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	if total(a) != 55 'sum'
		return 1
	end 'sum'
	if a.count() != 6 'count'
		return 2
	end 'count'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-empty-container-still-runs-the-hoisted-load -->
⭐ **THE SPECULATION CASE.** The buffer-base load lives in the block the loop header's guard branches
to, so for an array of length ZERO it never executed before this rung — and after it, hoisted into the
preheader, it does. Rule 2b is the whole of why that is safe: the length load at `[a+8]` is admitted
unconditionally by 2a and proves the object at `a` spans sixteen bytes, so the field at `[a+0]` is
inside it whether or not the loop ever iterates.

A freshly created array is exactly the shape that makes this worth pinning: it has a header but its
buffer pointer need not point anywhere, and the hoisted load READS that pointer rather than
dereferencing it. The one-element call beside it is the control that the loop still works.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray) returns Word
	var t = 0
	for v in a 'loop'
		t = t + v
	end 'loop'
	return t
end 'total'

function main() returns ExitCode
	var empty = WordArray.create()
	if total(empty) != 0 'emptyIsZero'
		return 1
	end 'emptyIsZero'

	var one = WordArray.create()
	one.push(7)
	if total(one) != 7 'oneElement'
		return 2
	end 'oneElement'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-invariant-computation-leaves-the-loop -->
Phase 1, and the case is written so a correct compiler cannot answer it by folding: `k` is fed from a
loop counter, so `k * 31` is real arithmetic over a runtime value. Only the MULTIPLY leaves — `t + k *
31` reads `t`, which changes every trip, and the `+ 7` after it reads that sum — so the fragment shows
one `imulRegRegImm32` in the loop's preheader and two `lea`s left in the body. That precision is the
point: the pass moves the invariant SUBEXPRESSION, not the statement that contains it.
```maxon
typealias Word = int(i64.min to i64.max)

function weighted(n Word, k Word) returns Word
	var t = 0
	for _ in 0 upto n 'loop'
		t = t + k * 31 + 7
	end 'loop'
	return t
end 'weighted'

function main() returns ExitCode
	var seed = 0
	for i in 0 upto 4 'feed'
		seed = seed + weighted(i, k: i + 1)
	end 'feed'
	// k*31+7 per trip, n trips: 0 + 69 + 2*100 + 3*131 = 662.
	if seed != 662 'value'
		return 1
	end 'value'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-loop-that-writes-keeps-its-loads-inside -->
⭐ **RULE 1's CONTROL, AND IT IS A WRONG ANSWER RATHER THAN A FRAGMENT DIFFERENCE.** The loop's exit test
READS a field the loop's body WRITES, so that load is not invariant at all: hoisted, the condition would
test the value the field held before the loop for ever. MEASURED by sabotage — make `loopWritesNoMemory`
answer `true` unconditionally and this program answers `trips=5 left=2` where `trips=3 left=0` is
correct, so the case returns 1.

⚠⚠ **TWO EARLIER SPELLINGS OF THIS CASE STAYED GREEN UNDER THAT SABOTAGE, AND BOTH WERE PASSING FOR
SOMETHING OTHER THAN THEIR SUBJECT.** A module-level `var` fails to reach Rule 1 because a global's read
is `globalAddr` + `loadIndirect` and the `globalAddr` is minted INSIDE the loop — the load is already
refused for having a loop-defined ADDRESS. A field read in the loop's BODY fails to reach it because
Rule 2a refuses a block that does not dominate the loop's exit, which in shv2's loop shapes is every
block but the header. ⇒ **the load has to be in the loop's own CONDITION, through an address computed
before the loop**, and that is what this program is. The two terms of `c.value + room > 0` are summed
rather than `and`ed on purpose: a short-circuit `and` would put the field read in its own block, which
Rule 2a then refuses and Rule 1 is never asked.
```maxon
typealias Word = int(i64.min to i64.max)

type Cell
	export var value as Word

	export static function of(value Word) returns Cell
		return Self{value: value}
	end 'of'
end 'Cell'

function drain(c Cell, budget Word) returns Word
	var trips = 0
	var room = budget
	while c.value + room > 0 'loop'
		c.value = c.value - 1
		room = room - 1
		trips = trips + 1
	end 'loop'
	return trips
end 'drain'

function main() returns ExitCode
	var c = Cell.of(3)
	// 3+2, 2+1, 1+0, then 0+(-1) stops: three trips, and the field reaches zero.
	if drain(c, budget: 2) != 3 'trips'
		return 1
	end 'trips'
	if c.value != 0 'drained'
		return 2
	end 'drained'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-conditionally-executed-load-is-not-speculated -->
⭐ **RULE 2's CONTROL.** Nothing in this loop writes memory, so Rule 1 admits it and the refusal is
entirely Rule 2's: the field read sits inside an `if` arm that does not dominate the loop's exit, and no
load through the same address is being hoisted unconditionally, so there is no witness either. The
committed fragment therefore shows the `loadRegBaseDisp` still inside the guarded block — a fold that
"helpfully" moved it would be speculating a dereference on the strength of nothing.
```maxon
typealias Word = int(i64.min to i64.max)

type Cell
	export var value as Word

	export static function of(value Word) returns Cell
		return Self{value: value}
	end 'of'
end 'Cell'

function conditionalRead(c Cell, n Word) returns Word
	var t = 0
	var i = 0
	while i < n 'loop'
		if i == 3 'once'
			t = t + c.value
		end 'once'
		i = i + 1
	end 'loop'
	return t
end 'conditionalRead'

function main() returns ExitCode
	var trips = 0
	for _ in 0 upto 9 'feed'
		trips = trips + 1
	end 'feed'

	var c = Cell.of(9)
	if conditionalRead(c, n: trips) != 9 'readOnce'
		return 1
	end 'readOnce'
	if conditionalRead(c, n: 0) != 0 'noTrips'
		return 2
	end 'noTrips'

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-nested-loop-hoists-to-the-nearest-preheader -->
Loops are processed innermost first and each loop's hoists are applied before the next is analysed, so
an op can leave a whole NEST in one run — and the fragment shows exactly how far each kind gets.

`k * 5` is pure, so it needs no speculation argument and climbs out of BOTH loops to `entry`. The inner
array's length and buffer loads reach the inner loop's preheader — the outer loop's body — and stop
there, because that block does not dominate the outer loop's exit and no load through `a` is being
hoisted out of the outer loop to witness for it. ⚠ That is the honest limit of Rule 2 on a nest, and it
is a pin rather than a defect: hoisting them further would be speculating a read on an execution where
the outer loop runs zero trips.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function grid(a WordArray, rows Word, k Word) returns Word
	var t = 0
	for _ in 0 upto rows 'outer'
		for v in a 'inner'
			t = t + v + k * 5
		end 'inner'
	end 'outer'
	return t
end 'grid'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(2)
	a.push(3)
	a.push(5)

	// Each row adds (2 + 3 + 5) + 3 * (2 * 5) = 40; three rows = 120.
	if grid(a, rows: 3, k: 2) != 120 'nested'
		return 1
	end 'nested'
	if grid(a, rows: 0, k: 2) != 0 'noRows'
		return 2
	end 'noRows'

	return 0
end 'main'
```
```exitcode
0
```
