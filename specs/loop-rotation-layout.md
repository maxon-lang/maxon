---
feature: loop-rotation-layout
status: experimental
keywords: [codegen, layout, block-order, loop, rotation, back-edge, fall-through, branch, latch]
category: codegen
---
# A loop's latch falls into its header

## Documentation

A structured loop is laid out header first: the header tests the loop condition and branches out to
the exit, the body follows, and the latch closes the loop with an unconditional branch back to the
header. After cold-block sinking (`specs/cold-block-layout.md`) that back-edge `jmp` is the one
taken branch per iteration, and the header's exit test is a not-taken conditional in front of it.

The Target-tier branch cleanup rotates the loop by layout alone. For every natural loop it takes
the header CHAIN — the header and the blocks it falls through, each with one hot in-loop successor
laid down next and only cold arms beside it, up to the first block with a hot exit edge (the
loop's exit test) — and lays it down immediately after the loop's hot latch, the physically last
hot block whose terminator is an unconditional branch to the header. The latch then falls into the
header and the cleanup's jump elision drops its back-edge `jmp`. What the exit test becomes depends
on its shape, and a loop is rotated only where the result is one instruction fewer per iteration.
A `for` or `while cond` header is `jcc body; jmp exit`: its conditional branch INTO THE BODY is now
the taken back branch — a taken `jmp` behind a not-taken `jcc` became one taken `jcc` — and the
elision also drops the `jmp exit` when the exit lands next. A header whose conditional aims at the
exit, such as a folded `while true` ending in its `break` test, pays only when that exit block is
the one physically after the latch, so that the conditional inversion turns `jcc exit; jmp body`
into the complemented conditional into the body with the exit falling through. Entering the loop
costs one branch to the header where the preheader used to fall into it; that branch is paid once
per loop entry, the saving once per iteration.

The reorder is sound because it runs after the sinking and before the fall-through elision, while
every else-edge is still a terminator op: the order of blocks that carry one is a layout choice and
nothing more. The one implicit edge is a block with no terminator op falling into whatever is laid
down next, so a loop is not rotated when its rotation would separate such a block from its physical
successor — the block laid down before the header, and the chain's last block, must both carry a
terminator; the latch does by construction. A loop is refused when the chain finds no exit test (a
header that branches into two hot in-loop arms before any exit test), when its exit test would not
pay (its conditional aims at an exit block that does not sit right after the latch), or when the
walk reaches the latch or another loop's header.

A rotated loop's exit code cannot see its layout; the shape case below records it in its fragment
golden, which a run compares and reports as drift. The controls run every path the reorder moves:
the guard failing before the first iteration, one iteration, a nest of two rotated loops, a `while`
with `continue` (a conditional back edge left in place beside the rotated jump latch), a `while`
whose `continue` arm carries work (two jump latches, the physically last one rotated), a nest whose
inner header is the outer loop's latch, a guard's sunk slow arm re-entering the chain, a range
check's sunk panic block leaving it, a header chain of guarded loads, a `break` out of the body, an
early `return` from the body, a `while true` whose exit sits above its latch, which the transform
refuses, and a `while true` whose first block branches into two hot arms, which it also refuses.

## Tests

<!-- test: a-latch-falls-into-its-header -->
The shape the row was opened for. In `copyTail`'s versioned fast copy the step block falls into the
loop header, whose `cmp / jcc less` is the taken back edge, and the exit is its fall-through.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function copyTail(source WordArray, scratch WordArray, n Word)
	for i in 1 upto n 'copy'
		let element = try source.get(i) otherwise panic("copyTail: i < n = source.count()")
		try scratch.set(i, value: element) otherwise panic("copyTail: i < n = scratch.count()")
	end 'copy'
end 'copyTail'

function main() returns ExitCode
	var source = WordArray.create()
	var scratch = WordArray.create()
	for i in 0 upto 6 'seed'
		source.push(i * 3)
		scratch.push(0)
	end 'seed'
	copyTail(source, scratch: scratch, n: 6)
	var total = 0
	for v in scratch 'sum'
		total = total + v
	end 'sum'
	return total
end 'main'
```
```exitcode
45
```

<!-- test: a-zero-iteration-loop-exits-through-its-guard -->
Control. The bound is zero, so the header runs once and leaves through the exit edge that the
rotation made a fall-through; the body never runs.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function sumFrom(a WordArray, n Word, start Word) returns Word
	var t = start
	for i in 0 upto n 'each'
		t = t + (try a.get(i) otherwise panic("sumFrom: i < n = a.count()"))
	end 'each'
	return t
end 'sumFrom'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 1 to 3 'seed'
		a.push(i)
	end 'seed'
	return sumFrom(a, n: 0, start: 100)
end 'main'
```
```exitcode
100
```

<!-- test: a-one-iteration-loop-runs-its-body-once -->
Control. The body runs once and the header's back edge is not taken.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function sumFrom(a WordArray, n Word, start Word) returns Word
	var t = start
	for i in 0 upto n 'each'
		t = t + (try a.get(i) otherwise panic("sumFrom: i < n = a.count()"))
	end 'each'
	return t
end 'sumFrom'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 7 to 9 'seed'
		a.push(i)
	end 'seed'
	return sumFrom(a, n: 1, start: 100)
end 'main'
```
```exitcode
107
```

<!-- test: nested-loops-each-rotate -->
Control. The inner loop's header lands after the inner step block, the outer loop's after the outer
step block that follows the inner exit; both back edges and both exits keep their values.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function grid(a WordArray, rows Word, width Word) returns Word
	var t = 0
	for r in 0 upto rows 'row'
		var line = 0
		for c in 0 upto width 'col'
			line = line + (try a.get(r * width + c) otherwise panic("grid: r * width + c < rows * width = a.count()"))
		end 'col'
		t = t + line * (r + 1)
	end 'row'
	return t
end 'grid'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 9 'seed'
		a.push(i)
	end 'seed'
	return grid(a, rows: 3, width: 3)
end 'main'
```
```exitcode
90
```

<!-- test: a-while-with-continue-keeps-both-latches -->
Control. `continue` in a `while` is a second back edge to the header — and here, once the empty
`continue` block is threaded away, it is the `even` test's own CONDITIONAL branch, not an
unconditional latch, so it is left where it is. The loop's one jump latch, the block after the test,
is rotated onto the header; the conditional back edge still reaches it.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function oddSum(a WordArray, n Word) returns Word
	var t = 0
	var i = 0
	while i < n 'each'
		let v = try a.get(i) otherwise panic("oddSum: i < n = a.count()")
		i = i + 1
		if v mod 2 == 0 'even'
			continue
		end 'even'
		t = t + v
	end 'each'
	return t
end 'oddSum'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 1 to 8 'seed'
		a.push(i)
	end 'seed'
	return oddSum(a, n: 8)
end 'main'
```
```exitcode
16
```

<!-- test: a-while-with-two-jump-latches-rotates-the-last -->
Control. The `continue` arm carries work, so it is not threaded away and both arms end in an
unconditional branch to the header: two jump latches. The physically last one — the block that
closes the loop body, not the `even` arm — is the one the header is laid down after; the `even`
arm's branch still reaches the header. Evens add 10 each, odds add themselves: 40 + 16.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function weightedSum(a WordArray, n Word) returns Word
	var t = 0
	var i = 0
	while i < n 'each'
		let v = try a.get(i) otherwise panic("weightedSum: i < n = a.count()")
		i = i + 1
		if v mod 2 == 0 'even'
			t = t + 10
			continue
		end 'even'
		t = t + v
	end 'each'
	return t
end 'weightedSum'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 1 to 8 'seed'
		a.push(i)
	end 'seed'
	return weightedSum(a, n: 8)
end 'main'
```
```exitcode
56
```

<!-- test: an-inner-header-that-is-the-outer-latch -->
Control. Nothing follows the inner loop in the outer body, so once the inner loop's empty exit
block is threaded away the inner header's exit edge IS the outer back edge: the inner header is
both the end of its own chain and the outer loop's latch, and the outer chain is laid down behind
it as part of the inner chain. Each outer step adds `j` from where the inner loop last stopped:
0 + 1 + 2 + 3 + 4, plus 30.
```maxon
typealias Word = int(i64.min to i64.max)

function staircase(n Word) returns Word
	var i = 0
	var j = 0
	var t = 0
	while i < n 'outer'
		i = i + 1
		while j < i 'inner'
			t = t + j
			j = j + 1
		end 'inner'
	end 'outer'
	return t + 30
end 'staircase'

function main() returns ExitCode
	return staircase(5)
end 'main'
```
```exitcode
40
```

<!-- test: a-sunk-slow-arm-re-enters-a-rotated-loop -->
Control. Three of seven indices are past the end, so the guard's sunk slow arm runs three times and
its handler's value rejoins the body below the header chain.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function clampedSum(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		t = t + (try a.get(i) otherwise 7)
	end 'each'
	return t
end 'clampedSum'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 1 to 4 'seed'
		a.push(i)
	end 'seed'
	return clampedSum(a, n: 7)
end 'main'
```
```exitcode
31
```

<!-- test: a-range-check-fires-inside-a-rotated-loop -->
Control. The counter walks below zero, so the range check's sunk panic block runs from inside the
rotated loop.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function walkDown(a WordArray, start Word) returns Word
	var t = 0
	var i = start
	while i > -2 'down'
		t = t + (try a.get(i) otherwise 0)
		i = i - 1
	end 'down'
	return t
end 'walkDown'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 1 to 3 'seed'
		a.push(i)
	end 'seed'
	return walkDown(a, start: 1)
end 'main'
```
```exitcode
1
```
```stderr
panic at a-range-check-fires-inside-a-rotated-loop.test:9: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in walkDown
  in main
  in mrt_start
```

<!-- test: a-header-chain-of-guarded-loads-rotates -->
Control. The loop condition is a guarded load, so the header chain is several blocks with cold arms
beside it before the exit test. The first array stops at its third element; the second stops before
its first, so that loop leaves through the chain without an iteration.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function sumUntilZero(a WordArray) returns Word
	var i = 0
	var t = 0
	while (try a.get(i) otherwise panic("sumUntilZero: a ends with a 0")) > 0 'scan'
		t = t + (try a.get(i) otherwise panic("sumUntilZero: i < a.count()"))
		i = i + 1
	end 'scan'
	return t
end 'sumUntilZero'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(6)
	a.push(5)
	a.push(0)
	a.push(9)
	var b = WordArray.create()
	b.push(0)
	b.push(3)
	return sumUntilZero(a) * 10 + sumUntilZero(b)
end 'main'
```
```exitcode
110
```

<!-- test: a-break-leaves-a-rotated-loop-from-its-body -->
Control. The `break` is a hot exit edge from a body block, not from the header chain; the rotation
moves the chain around it.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function firstNegative(a WordArray, n Word) returns Word
	var i = 0
	var found = -1
	while i < n 'scan'
		let v = try a.get(i) otherwise panic("firstNegative: i < n = a.count()")
		if v < 0 'hit'
			found = i
			break
		end 'hit'
		i = i + 1
	end 'scan'
	return found + 100
end 'firstNegative'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(3)
	a.push(4)
	a.push(-1)
	a.push(5)
	return firstNegative(a, n: 4)
end 'main'
```
```exitcode
102
```

<!-- test: an-early-return-leaves-a-rotated-loop-from-its-body -->
Control. A `return` inside the body leaves a dead continuation behind it; the loop still rotates
and both the found and the not-found paths answer.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function indexOf(a WordArray, n Word, target Word) returns Word
	for i in 0 upto n 'scan'
		if (try a.get(i) otherwise panic("indexOf: i < n = a.count()")) == target 'hit'
			return i
		end 'hit'
	end 'scan'
	return -1
end 'indexOf'

function main() returns ExitCode
	var a = WordArray.create()
	a.push(4)
	a.push(8)
	a.push(15)
	return indexOf(a, n: 3, target: 15) * 10 + indexOf(a, n: 3, target: 99) + 5
end 'main'
```
```exitcode
24
```

<!-- test: a-while-true-whose-exit-sits-above-its-latch-is-not-rotated -->
Control. The parser folds a constant condition, so the header of this `while true` is the body's
first block and the `break` test at its end is the loop's exit test — `jcc exit; jmp body`, its
conditional aimed at the exit. The exit block is laid out between the header and the latch, not
right after the latch, so moving the header behind the latch would leave both branch instructions
in the iteration and add an entry branch: the loop is refused and laid out as it was, its latch
still branching to the header. In that unrotated layout the exit block IS physically next after the
header, so the golden shows the inversion already fired there — `spin: jcc notEqual, ifcont` with
`whileexit` falling through — and the `jcc exit; jmp body` above is the shape the transform read
before the elision ran.
```maxon
typealias Small = int(0 to 100)

function spinTo(limit Small) returns Small
	var i = 0 as Small
	while true 'spin'
		i = i + 1
		if i == limit 'done'
			break
		end 'done'
	end 'spin'
	return i
end 'spinTo'

function main() returns ExitCode
	return spinTo(5) + spinTo(37)
end 'main'
```
```exitcode
42
```

<!-- test: a-header-branching-into-two-hot-arms-is-not-rotated -->
Control. The first block of this `while true` branches into two hot in-loop arms, so the chain stops
before any exit test is reached and the loop is laid out as it was: its latch still branches to the
header.
```maxon
typealias Small = int(0 to 100)

function stepTo(limit Small) returns Small
	var i = 0 as Small
	while true 'spin'
		if i mod 2 == 0 'evenStep'
			i = i + 3
		end 'evenStep' else 'oddStep'
			i = i + 1
		end 'oddStep'
		if i >= limit 'done'
			break
		end 'done'
	end 'spin'
	return i
end 'stepTo'

function main() returns ExitCode
	return stepTo(10) + stepTo(5)
end 'main'
```
```exitcode
18
```
