---
feature: value-range-analysis
status: experimental
keywords: [optimizer, codegen, range-check, bounds-check, interval, branch, loop, induction, control-flow]
category: codegen
---
# A check the values could never fail

## Documentation

`refineValueRanges` is a Std-tier pass that runs after `threadConstantBranches` and before
`commonSubexpressionElimination`. It computes a signed 64-bit interval for every integer value in
a function — constants, `add`/`sub`/`mul` on known intervals (to the whole range whenever a corner
could wrap: the language's integers wrap, so an interval that ignored it would be a lie), block
arguments as the join of their incoming edges — iterated to a fixpoint with widening, and it
REFINES an interval below a branch: in the blocks a compare's true edge dominates, its operands are
narrowed by the predicate, and by its negation below the false edge. The refinement is relational
through the other operand's own interval, which is how `high` inside `while low < high` is known to be
at least `low + 1`.

A compare whose outcome the intervals decide becomes a constant, the branch that read it becomes an
unconditional branch, and the arm it can never take is dropped with everything only it reaches. A
refinement that empties an interval marks the subtree below it dead: its ops write nothing and its
edges feed no join, so a loop body a decided guard never enters cannot widen the counter it guards.

### The shape it exists for

`InsertRangeChecks` puts a cascade before every checked array access on an `ElementIndex =
int(0 to u64.max)`: `cmp idx, 0, lt` and a branch to `__rc_panic`. On a counted loop's index that
test is dead — the counter starts at its constant and only ever grows below the loop bound — and so is
every later check of a value one check has already proven non-negative. Every checked read and write
in every loop paid it: a compare, a branch, and where CSE reused the compare for a later access, a
`setcc` into a callee-saved register and the register pressure that spills a loaded element.

### The loop counter, worked

`for i in 1 upto n`: the header's block argument joins `1` from the preheader with `i + 1` from the
latch. The body is dominated by the true edge of the header's `i < n`, so inside it `i ≤ n − 1 ≤
i64.max − 1` and the increment cannot wrap; the latch contributes `[2, i64.max]`, the join is
`[1, i64.max]`, and the fixpoint holds. Without the refinement the increment could wrap and the
counter would be unknown — the refinement is the whole of what proves a loop counter.

### What the pass refuses, and why

- **Any operand type but `i64`, with one exception.** A byte load is zero-extended, a float is not
  an integer, a narrow signed constant is a width the backends disagree about; none is modelled. A
  `u64` compare's operands may exceed `i64.max`, so it is refined and decided only when both
  operands' intervals already lie within `[0, i64.max]`, where the two orders agree.
- **A refinement under a target with a second predecessor.** The narrowing holds only on the edge,
  so it is pushed for exactly the blocks that edge's sole-predecessor target dominates.
- **Any arithmetic that could wrap.** `add`, `sub` and `mul` go to the whole range unless every corner
  of the result fits; `bitAnd` with a non-negative constant and a right shift by a constant are
  bounded; everything else — loads, calls, parameters, division, other bit operations — is the whole
  range.

⚠ A green case here proves nothing on its own — a check that is deleted was one the program never
failed. The evidence is the committed fragments of the shape cases (no `__rc_panic` block where a check
was proven) and the CONTROLS, each a program that must still panic because its value is NOT proven:
a counter walked below zero, an increment that wraps past `i64.max`, a value refined on one edge and
used on the other, a two-sided alias whose upper bound a runtime limit can exceed. Measured under
sabotage — `add` saturating its corners instead of going to the whole range — exactly one case moves:
`an-increment-that-wraps-still-panics` exits 3 through its `otherwise` where the panic is right.

## Tests

<!-- test: a-counted-loop-index-reads-with-no-range-check -->
The shape the pass was opened for. `total`'s loop reads `a.get(i)` for a counter from 0: the fragment
holds no `__rc_panic` block in `total` (the `__rc_ok` label survives as the continuation's name), and
the bound check is the first test the access makes.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'sum'
		t = t + (try a.get(i) otherwise panic("total: i < n = a.count()"))
	end 'sum'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 6 'seed'
		a.push(i * i)
	end 'seed'
	if total(a, n: a.count()) != 55 'sum'
		return 1
	end 'sum'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-value-checked-once-is-known-non-negative-after -->
`i` is a plain `Word`, so its first access must check it. The second and third accesses sit in blocks
the first check's `__rc_ok` dominates, where `i ≥ 0` is known: `bump`'s fragment holds exactly one
`__rc_panic` block.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function bump(a WordArray, i Word) returns Word
	let before = try a.get(i) otherwise panic("bump: i is in range")
	try a.set(i, value: before + 1) otherwise panic("bump: i is in range")
	return try a.get(i) otherwise panic("bump: i is in range")
end 'bump'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(5)
	if bump(a, i: 0) != 6 'first'
		return 1
	end 'first'
	if bump(a, i: 0) != 7 'second'
		return 2
	end 'second'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-reverse-loop-proves-both-bounds -->
The relational shape: `low` starts at 1 and grows, `high` starts at a value only known at run time
and shrinks, and the loop runs while `low < high`. Below that test `high ≥ low + 1 ≥ 2`, so all
four accesses need no range check: `reverseMiddle`'s fragment holds no `__rc_panic` block.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function reverseMiddle(a WordArray, first Word)
	var low = 1
	var high = first - 1

	while low < high 'swap'
		let atLow = try a.get(low) otherwise panic("reverseMiddle: low < high < first <= a.count()")
		let atHigh = try a.get(high) otherwise panic("reverseMiddle: high < first <= a.count()")
		try a.set(low, value: atHigh) otherwise panic("reverseMiddle: low is in range")
		try a.set(high, value: atLow) otherwise panic("reverseMiddle: high is in range")
		low = low + 1
		high = high - 1
	end 'swap'
end 'reverseMiddle'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 8 'seed'
		a.push(i)
	end 'seed'
	let first = try a.get(7) otherwise panic("main: a holds 8 elements")
	reverseMiddle(a, first: first)
	if (try a.get(1) otherwise -1) != 6 'one'
		return 1
	end 'one'
	if (try a.get(6) otherwise -1) != 1 'six'
		return 2
	end 'six'
	if (try a.get(3) otherwise -1) != 4 'three'
		return 3
	end 'three'
	if (try a.get(0) otherwise -1) != 0 'zeroUntouched'
		return 4
	end 'zeroUntouched'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-user-compare-decided-by-a-counter-folds -->
The general form, without an array: a user's own `i >= 0` inside a counted loop is decided true.
`countNonNegative` is a leaf and is spliced into `main`, so the reading is `main`'s fragment: each
inlined loop's body follows its `forhdr` with no compare against 0 between them.
```maxon
typealias Word = int(i64.min to i64.max)

function countNonNegative(n Word) returns Word
	var c = 0
	for i in 0 upto n 'each'
		if i >= 0 'yes'
			c = c + 1
		end 'yes'
	end 'each'
	return c
end 'countNonNegative'

function main() returns ExitCode
	if countNonNegative(5) != 5 'five'
		return 1
	end 'five'
	if countNonNegative(0) != 0 'none'
		return 2
	end 'none'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-counter-walked-below-zero-still-panics -->
Control. `i` is a loop-carried value that the loop's own test bounds to `[-1, 2]`, so its check is
undecided and stays; the fourth iteration reaches the door with `-1` and the door refuses.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(1)
	a.push(2)
	a.push(3)
	var i = 2
	var t = 0
	while i > -2 'walk'
		t = t + (try a.get(i) otherwise 0)
		i = i - 1
	end 'walk'
	if t != 6 'total'
		return 2
	end 'total'
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at a-counter-walked-below-zero-still-panics.test:13: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: an-increment-that-wraps-still-panics -->
Control for wrapping. `big` is loaded, then refined to `[1, i64.max]` by the branch; `big + 1` has a
corner outside the range, so it is the whole range and the check on `wrapped` stays. The value is
`i64.min`, and the door refuses it. An interval arithmetic that clamped instead of widening would
prove `wrapped ≥ 0`, delete the check, and the program would exit 3 through the `otherwise`.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(9223372036854775807)
	let big = try a.get(0) otherwise 0
	if big <= 0 'small'
		return 2
	end 'small'
	let wrapped = big + 1
	let v = try a.get(wrapped) otherwise 5
	return 3 if v == 5 else 4
end 'main'
```
```exitcode
1
```
```stderr
panic at an-increment-that-wraps-still-panics.test:13: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-refinement-stays-on-its-own-edge -->
Control for refinement scope. `v` is non-negative only below the true edge of `v >= 0`; the access
sits below the FALSE edge, where `v ≤ -1` is what is known, and the door refuses. A refinement that
leaked past its edge would delete the check and the program would exit 3.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function main() returns ExitCode
	var a = WordArray.create()
	a.push(-1)
	a.push(7)
	let v = try a.get(0) otherwise 0
	if v >= 0 'nonNegative'
		return 2
	end 'nonNegative'
	let w = try a.get(v) otherwise 5
	return 3 if w == 5 else 4
end 'main'
```
```exitcode
1
```
```stderr
panic at a-refinement-stays-on-its-own-edge.test:13: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in main
  in mrt_start
```

<!-- test: a-two-sided-alias-keeps-its-upper-check-under-a-runtime-bound -->
Control for the upper bound. `Small = int(0 to 100)` is two-sided; the counter proves the lower
check and nothing proves the upper one under a bound only known at run time. With `n = 150` the
call at `i = 101` is refused, so the program never reaches its return.
```maxon
typealias Word = int(i64.min to i64.max)
typealias Small = int(0 to 100)
typealias WordArray = Array with Word

function weigh(x Small) returns Word
	return x * 2
end 'weigh'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(150)
	let n = try a.get(0) otherwise 0
	var t = 0
	for i in 0 upto n 'each'
		t = t + weigh(i)
	end 'each'
	return 3 if t == 0 else 4
end 'main'
```
```exitcode
1
```
```stderr
panic at a-two-sided-alias-keeps-its-upper-check-under-a-runtime-bound.test:6: Range check failed: value outside typealias 'Small'
Stack trace:
  in weigh
  in main
  in mrt_start
```
