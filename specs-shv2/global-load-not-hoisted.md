---
feature: global-load-not-hoisted
status: stable
keywords: var, global, loop, purity, hoist, loadIndirect, data-section
category: language
---
# A Global's Read Is Not Hoisted Out Of A Loop That Writes It

## Documentation

A top-level `var` lives in the `.data` section, and every read of it is a real load
(`globalAddr` + `loadIndirect`). Inside a loop that also WRITES the global, that load must
happen on **every iteration**: what it reads is a mutable location the loop itself changes.

This is a property of one declaration — `StdOp.loadIndirect`'s `isPure: false`. `isPure`
licenses a pass to duplicate, reorder or DROP an op, and the tempting reading of a load is
that it is "side-effect-free" and therefore pure. It writes nothing, so that reading is true
and useless: purity asks *may this op be moved?*, and a load may not be. Declared pure, the
loop's read of the global is loop-INVARIANT to every optimizer that looks at it, gets hoisted
to the preheader, and the loop then reads the value the global held before it started — for
ever.

The failure is a **silent wrong answer**, not a crash, and no gate below the exit code can
see it: the program compiles, runs, and returns a plausible number. So the property is
pinned here, by a program whose answer is only reachable if the load really happens each
time round.

These cases are shv2-authored rather than ported. `/specs` has no test for this because the
reference compilers reached globals long before they had an optimizer that could hoist one —
the property was never at risk there, and is at risk here from the first Std pass that
treats `isPure` as licence to move.

## Tests

<!-- test: loop-reads-a-global-it-writes -->
The accumulator and the induction variable are BOTH globals, so the loop's every read is a
load. `0+1+2+3+4 = 10`. A hoisted read of `i` would make the condition permanently true and
the program would never terminate; a hoisted read of `acc` would return 4.

```maxon
var acc = 0
var i = 0

function main() returns ExitCode
	while i < 5 'loop'
		acc = acc + i
		i = i + 1
	end 'loop'
	return acc
end 'main'
```
```exitcode
10
```

<!-- test: loop-calls-a-function-that-writes-the-global -->
The write is not even in this function: `bump` stores to `counter`, and `main`'s loop reads
it back through a call boundary. A load hoisted out of the loop would read 0 five times and
return 0.

```maxon


var counter = 0

function bump()
	counter = counter + 1
end 'bump'

function main() returns ExitCode
	var seen = 0
	var n = 0
	while n < 5 'loop'
		bump()
		seen = seen + counter
		n = n + 1
	end 'loop'
	return seen
end 'main'
```
```exitcode
15
```

<!-- test: global-read-twice-around-a-write -->
The same global is read, written, and read again in one straight-line block. The two reads
must produce DIFFERENT values, so the second cannot be CSE'd onto the first — the same
`isPure: false` that forbids the hoist forbids the fold.

```maxon


var slot = 3

function main() returns ExitCode
	let before = slot
	slot = 40
	let after = slot
	return before + after - 1
end 'main'
```
```exitcode
42
```
