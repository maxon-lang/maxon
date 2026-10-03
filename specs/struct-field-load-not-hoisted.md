---
feature: struct-field-load-not-hoisted
status: stable
keywords: struct, field, loop, purity, hoist, loadIndirect
category: language
---
# A Struct Field's Read Is Not Hoisted Out Of A Loop That Writes It

## Documentation

A struct field read is `loadIndirect` at the field's offset — the SAME op a global's read
uses, with a real offset where that one passes zero. So it inherits that op's purity
declaration, `StdOp.loadIndirect`'s `isPure: false`, and it inherits it for exactly the same
reason: `isPure` licenses a pass to duplicate, reorder or DROP an op, and a load may do none
of those. What it reads is a mutable location some other op writes.

Declared pure, `p.x` inside a loop that writes `p.x` is loop-INVARIANT to any optimizer that
looks at it. It would be hoisted to the preheader, and the loop would read the value the field
held before it started — for ever. A **silent wrong answer**, not a crash: the program
compiles, runs, and returns a plausible number.

### ⚠ What this case pins

The case pins the **ANSWER** — 5, not 1 — so a wrong purity declaration is caught by a test rather
than by a program returning a plausible number in production.

`global-load-not-hoisted.md` is the GLOBAL twin. This file is the STRUCT twin, and it belongs beside
it: the two are one declaration, reached through two surfaces.

No other case writes a scalar struct field inside a loop — the suite covers field assignment
(`challenge-struct-field-assign.md`) and covers loops, and never crosses the two.

## Tests

<!-- test: loop-writes-a-field-it-reads -->
The accumulator IS the field, so the loop's every read of it is a load and its every write is
a store. Five iterations of `c.value = c.value + 1` from 0 give **5**. A hoisted load would
read 0 on every iteration, store 1 every time, and return **1** — a plausible number, and the
reason the exit code is the only gate that can see this.

`loopInvariantCodeMotion` hoists a load only out of a loop whose blocks write no memory, and this
loop stores to the field it reads, so the load stays in the body.

```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(0)
	var i = 0
	while i < 5 'loop'
		c.value = c.value + 1
		i = i + 1
	end 'loop'
	return c.value
end 'main'
```
```exitcode
5
```
