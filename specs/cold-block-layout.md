---
feature: cold-block-layout
status: experimental
keywords: [codegen, layout, block-order, cold, fall-through, branch, slow-arm, loop]
category: codegen
---
# A cold block is laid out after every hot one

## Documentation

Every block carries a heat, `normal` or `cold` (`specs/cold-call-spilling.md` lists who sets it: an
inlined access's slow arm when the fast arm serves the common case, a range check's panic block, a
leaf inliner's panic redirect, a `try … otherwise panic` handler). The Target-tier branch cleanup
lays a function out with every cold block AFTER every normal one, each side keeping its relative
order, and the entry block first.

Why: a guard's conditional branch names its cold arm and its terminator names the hot continuation.
With the arm laid down between the two, the hot path takes the branch on every pass and the arm's
body sits in the instruction stream the front end has to jump over; with the arm sunk, the
continuation is the physically next block, and the cleanup's conditional inversion turns the guard
into a fall-through with the complemented condition aimed at the arm. In a versioned loop over two
arrays a body of ten instructions took three taken branches per iteration; it takes one, the back
edge.

The reorder is sound because it runs before the cleanup's fall-through elision, while every
else-edge is still a terminator op: the order of blocks that carry one is a layout choice and
nothing more. A block with no terminator op falls into whatever is laid down next, so it and its
physical successor move as one unit, cold only when every block in it is.

The controls below make a sunk block run — an out-of-bounds access takes its slow arm, a range
check fires, a panic handler fires — and read the answer back through the hot path the arm returns
to. Two sabotages were measured. The sink run AFTER the elision stays green: an elided terminator is
`Terminator.fallthrough`, which names no op, so the unit rule glues every elided block to its
successor and only self-contained cold units move. With the unit rule removed as well, a guard whose
terminator had become a fall-through falls into whatever is laid down next: the two panic controls
answer wrong (the sums 6 and 15 come back and no panic fires), while the two `otherwise <value>`
controls stay green because the block that follows the sunk arm in that layout is the handler
itself, which is the value the arm would have produced. The first case stays green under both: its
exit code cannot see layout, and the layout it describes is recorded in its fragment golden, which a
run compares and reports as drift rather than as a failure.

## Tests

<!-- test: a-guard-falls-through-to-its-fast-arm -->
The shape the row was opened for. In `copyTail`'s versioned fast copy each bound check is
`cmp / jcc aboveEqual, __im_slow` followed physically by its `__im_load` or `__im_store`, and every
`__im_slow` and `tryerr` block of the function sits after its last hot block.
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

<!-- test: a-sunk-slow-arm-returns-to-the-hot-path -->
Control. Four of nine indices are past the end, so the sunk slow arm runs four times and its
handler's value joins the sum on the hot path.
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
	for i in 1 to 5 'seed'
		a.push(i)
	end 'seed'
	return clampedSum(a, n: 9)
end 'main'
```
```exitcode
43
```

<!-- test: sunk-arms-of-a-nested-loop-keep-both-edges -->
Control. The inner loop's slow arm is sunk past the outer loop's body; its success edge rejoins the
inner loop and its handler edge feeds the outer accumulator.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function grid(a WordArray, rows Word, width Word) returns Word
	var t = 0
	for r in 0 upto rows 'row'
		var line = 0
		for c in 0 upto width 'col'
			line = line + (try a.get(r * width + c) otherwise 100)
		end 'col'
		t = t + line * (r + 1)
	end 'row'
	return t
end 'grid'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 0 upto 7 'seed'
		a.push(i)
	end 'seed'
	return grid(a, rows: 3, width: 3) - 600
end 'main'
```
```exitcode
45
```

<!-- test: a-sunk-range-check-still-fires -->
Control. The counter walks below zero, so the range check's sunk panic block runs.
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
	return walkDown(a, start: 2)
end 'main'
```
```exitcode
1
```
```stderr
panic at a-sunk-range-check-still-fires.test:9: Range check failed: value outside typealias 'ElementIndex'
Stack trace:
  in walkDown
  in main
  in mrt_start
```

<!-- test: a-sunk-panic-handler-still-fires -->
Control. The sixth index is past the end, so the sunk `otherwise panic` handler runs.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word

function total(a WordArray, n Word) returns Word
	var t = 0
	for i in 0 upto n 'each'
		t = t + (try a.get(i) otherwise panic("total: index {i} is past the end"))
	end 'each'
	return t
end 'total'

function main() returns ExitCode
	var a = WordArray.create()
	for i in 1 to 5 'seed'
		a.push(i)
	end 'seed'
	return total(a, n: 6)
end 'main'
```
```exitcode
1
```
```stderr
panic at a-sunk-panic-handler-still-fires.test:8: total: index 5 is past the end
Stack trace:
  in total
  in main
  in mrt_start
```
