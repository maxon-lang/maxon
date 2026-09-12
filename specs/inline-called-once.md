---
feature: inline-called-once
status: experimental
keywords: [optimizer, inliner, called-once, codegen, panic, runtime, backtrace, symtable]
category: codegen
---
# Inlining Functions Called Exactly Once

## Documentation

The inliner's second admission rule: a function with **exactly one direct `call` site in the whole
program** is spliced into its caller **regardless of size** — loops, stores, panics, `/` and `mod`, and
calls inside its body are all admitted. The leaf rule (`specs/inline-leaves.md`) buys a copy per site
because the copy is tiny; this rule buys ONE copy because the call it removes was the function's only
reason to exist as a frame, and the body moves rather than multiplies.

A called-once function is refused when:

- it is **recursive**, or has any other call site (a self-call is a second site);
- it **throws** — an `errorReturn` body, or a site spelled `tryCall`;
- it takes a **by-reference** (reassigned) parameter;
- it carries the **green-thread stack guard** — it runs user code on its caller's stack;
- the splice would exceed the caller's **register-pressure budget**.

### WHY THE STACK TRACE DOES NOT MOVE

A panic or a hardware fault inside an inlined body prints exactly the frames the call would have
printed: `in <callee>` then `in <caller>`, and a nested inlining prints every level. Three pieces
carry that:

- **every IR block carries its inline site** — the callee it was copied out of, and the site that
  copy is nested under, so a block copied through two splices still names both;
- **the backend emits inline range records into the `__inlframes` table behind `__symtable`** — for
  each spliced body, the address ranges its blocks occupy and the callee's name; a nested body's blocks
  carry the inner site, so the ranges stay disjoint and nesting lives in each site's parent;
- **the x64 and arm64 frame printers walk the records innermost-first** — for a return address inside
  a range they print the innermost callee's line, then each enclosing callee, then the frame's own
  function, so the trace reads as if the call still existed.

Panic blocks are therefore **copied with their inline site** rather than redirected: the leaf
inliner's slow-arm re-run is gone, and a leaf with a store and a panic is inlined whole.

**Goldens are the evidence of the spliced shape.** The fragments under
`specs/fragments/<target>/inline-called-once/` show whether a case's `main` holds a `callDirect` to the
callee or the callee's blocks; the exit codes below are the same either way, which is what makes them
a control on the answer and not on the shape.

**`wasm32-wasi` has no inline frame records, so the rule fails closed there.** A wasm panic executes
`unreachable` and wasmtime prints its OWN frames from the engine's call stack (`StdToWasm.maxon`,
`mrt_panic` ON WASM) — there is no saved-frame-pointer chain to walk and no printer of ours to teach, so a
trace raised inside a spliced body would lose the callee's frame with no record to restore it. On a target
whose backend emits no inline frame records (`TargetFacilities.targetEmitsInlineFrameRecords`) the
called-once rule refuses every callee (refusal `noInlineFrameRecords`, tallied and logged like the others),
and the leaf rule refuses any body holding a panic op or a `div`/`mod` — the shapes whose diagnostic names
a frame — while otherwise running as before. The stderr cases here therefore print the same trace on every
target: the call still stands where the splice would have moved its frame.

## Tests

<!-- test: a-called-once-function-with-a-loop-stores-and-panics-is-spliced -->
⭐ **THE SHAPE CASE.** `flipOnce` is `fannkuch`'s flip step — a copy loop, then a prefix reverse, both
full of stores and `otherwise panic` arms — and `main` calls it ONCE, from inside a loop. Every rule
the leaf inliner has refuses it (calls, size, stores beside panics); the called-once rule splices it.
The golden is the pin; the exit code is the same as with the pass off.

Each round writes `round` into slot 0, so the reversed prefix is `0 to round` and the answer is the
first two elements after the flip: 1+1, 2+1, 3+2, 4+3.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function flipOnce(current IntArray, temp IntArray, n Integer) returns Integer
	for i in 0 upto n 'copy'
		let element = try current.get(i) otherwise panic("flipOnce: i < n = current.count()")
		try temp.set(i, value: element) otherwise panic("flipOnce: i < n = temp.count()")
	end 'copy'

	let firstValue = try temp.get(0) otherwise panic("flipOnce: n >= 1")
	var low = 0
	var high = firstValue

	while low < high 'reverse'
		let atLow = try temp.get(low) otherwise panic("flipOnce: low < high <= firstValue < n")
		let atHigh = try temp.get(high) otherwise panic("flipOnce: high <= firstValue < n")
		try temp.set(low, value: atHigh) otherwise panic("flipOnce: low < n")
		try temp.set(high, value: atLow) otherwise panic("flipOnce: high < n")
		low = low + 1
		high = high - 1
	end 'reverse'

	let first = try temp.get(0) otherwise panic("flipOnce: n >= 1")
	let second = try temp.get(1) otherwise panic("flipOnce: n >= 2")
	return first + second
end 'flipOnce'

function main() returns ExitCode
	let n = 5
	var current = IntArray.create()
	current.resize(n)
	var temp = IntArray.create()
	temp.resize(n)

	for i in 0 upto n 'fill'
		try current.set(i, value: i) otherwise panic("main: i < n = current.count()")
	end 'fill'

	var total = 0

	for round in 1 to 4 'rounds'
		try current.set(0, value: round) otherwise panic("main: n >= 1")
		total = total + flipOnce(current, temp: temp, n: n)
	end 'rounds'

	return total as ExitCode
end 'main'
```
```exitcode
17
```

<!-- test: a-chain-of-called-once-functions-is-spliced-through -->
`main` calls `outer` once and `outer` calls `inner` once. Both bodies loop and store, so both are
admitted, and the call-free `inner` blocks end up two levels deep inside `main`. `outer` fills
1..4, `inner` sums them (10) and doubles them in place, and `outer` adds the doubled last (8).

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function inner(a IntArray, n Integer) returns Integer
	var sum = 0

	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("inner: i < n = a.count()")
		try a.set(i, value: v * 2) otherwise panic("inner: i < n = a.count()")
		sum = sum + v
	end 'each'

	return sum
end 'inner'

function outer(a IntArray, n Integer) returns Integer
	for i in 0 upto n 'fill'
		try a.set(i, value: i + 1) otherwise panic("outer: i < n = a.count()")
	end 'fill'

	let before = inner(a, n: n)
	let last = try a.get(n - 1) otherwise panic("outer: n >= 1")
	return before + last
end 'outer'

function main() returns ExitCode
	let n = 4
	var a = IntArray.create()
	a.resize(n)
	return outer(a, n: n) as ExitCode
end 'main'
```
```exitcode
18
```

<!-- test: a-panic-inside-a-spliced-body-names-the-callee -->
⭐ **THE TRACE GATE.** The same flip step as the shape case, but `temp` is one slot shorter than `n`,
so the `set` in the copy loop's LAST iteration takes its `otherwise panic` arm — inside a loop body
that now lives in `main`'s frame. The panic block carries its inline site, the range record names
`flipOnce`, and the printer reads `in flipOnce / in main` off a frame that belongs to `main`. This
stderr is byte-identical to what the same program prints with the call left standing.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function flipOnce(current IntArray, temp IntArray, n Integer) returns Integer
	for i in 0 upto n 'copy'
		let element = try current.get(i) otherwise panic("flipOnce: current is shorter than n")
		try temp.set(i, value: element) otherwise panic("flipOnce: temp is shorter than n")
	end 'copy'

	let firstValue = try temp.get(0) otherwise panic("flipOnce: n >= 1")
	var low = 0
	var high = firstValue

	while low < high 'reverse'
		let atLow = try temp.get(low) otherwise panic("flipOnce: low < high <= firstValue < n")
		let atHigh = try temp.get(high) otherwise panic("flipOnce: high <= firstValue < n")
		try temp.set(low, value: atHigh) otherwise panic("flipOnce: low < n")
		try temp.set(high, value: atLow) otherwise panic("flipOnce: high < n")
		low = low + 1
		high = high - 1
	end 'reverse'

	return try temp.get(0) otherwise panic("flipOnce: n >= 1")
end 'flipOnce'

function main() returns ExitCode
	let n = 5
	var current = IntArray.create()
	current.resize(n)
	var temp = IntArray.create()
	temp.resize(n - 1)

	for i in 0 upto n 'fill'
		try current.set(i, value: i) otherwise panic("main: i < n = current.count()")
	end 'fill'

	var total = 0

	for round in 1 to 2 'rounds'
		try current.set(0, value: round) otherwise panic("main: n >= 1")
		total = total + flipOnce(current, temp: temp, n: n)
	end 'rounds'

	return total as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-panic-inside-a-spliced-body-names-the-callee.test:8: flipOnce: temp is shorter than n
Stack trace:
  in flipOnce
  in main
  in mrt_start
```

<!-- test: a-panic-in-the-innermost-of-a-chain-names-every-level -->
`main` → `outer` → `inner`, each called once, so `inner`'s blocks are spliced through two levels, and
the panic fires in `inner`'s loop. Its block's inline site names `inner` nested under `outer`'s site,
so the range records nest and the printer walks them innermost-first: three lines for one frame.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function inner(a IntArray, n Integer) returns Integer
	var sum = 0

	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("inner: a is shorter than n")
		try a.set(i, value: v * 2) otherwise panic("inner: a is shorter than n")
		sum = sum + v
	end 'each'

	return sum
end 'inner'

function outer(a IntArray, n Integer) returns Integer
	for i in 0 upto a.count() 'fill'
		try a.set(i, value: i + 1) otherwise panic("outer: i < a.count()")
	end 'fill'

	let before = inner(a, n: n)
	let last = try a.get(a.count() - 1) otherwise panic("outer: a is not empty")
	return before + last
end 'outer'

function main() returns ExitCode
	var a = IntArray.create()
	a.resize(3)
	return outer(a, n: 4) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-panic-in-the-innermost-of-a-chain-names-every-level.test:9: inner: a is shorter than n
Stack trace:
  in inner
  in outer
  in main
  in mrt_start
```

<!-- test: a-panic-in-a-loop-the-unswitcher-versioned-names-the-callee -->
A `for` over an index range doing `get` + `set` on one `Array with Integer` is the shape
`unswitchInvariantGuards` versions into a fast copy (the bound proven inside the array) and a slow
copy (the guards kept). The panic fires from the SLOW copy's blocks — the versioned loop is copied
into `main` twice over, and every copy carries the callee's inline site, so the trace still names
`scale`.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function scale(a IntArray, n Integer, factor Integer) returns Integer
	var total = 0

	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("scale: a is shorter than n")
		try a.set(i, value: v * factor) otherwise panic("scale: a is shorter than n")
		total = total + v * factor
	end 'each'

	return total
end 'scale'

function main() returns ExitCode
	var a = IntArray.create()
	a.resize(3)

	for i in 0 upto 3 'fill'
		try a.set(i, value: i + 1) otherwise panic("main: i < 3 = a.count()")
	end 'fill'

	return scale(a, n: 5, factor: 3) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-panic-in-a-loop-the-unswitcher-versioned-names-the-callee.test:9: scale: a is shorter than n
Stack trace:
  in scale
  in main
  in mrt_start
```

<!-- test: a-hardware-fault-inside-a-spliced-body-names-the-callee -->
<!-- unsupported-targets: x64-linux, arm64-macos, arm64-linux, wasm32-wasi -->
⭐ **A FAULT HAS NO PANIC BLOCK TO TAG — ONLY AN ADDRESS.** `specs/safety.md`'s
`integer-overflow-fault-from-int-min-over-minus-one`, with the dividing function given a store loop
so it is no leaf and called exactly once, so it is spliced. `i64.min / -1` raises `#DE` from an
`idiv` that now sits in `main`'s code; the fault handler has nothing but the faulting address, and the
range record covering it is what names `divide`. The stderr is the neighbouring case's, frame for frame.

`x64-linux` is excluded for that case's measured reason: its kernel reports `FPE_INTDIV` for this
fault too, so the wording it prints is the divide-by-zero one.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias NegativeOne = int(-1 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function divide(scratch IntArray, d NegativeOne) returns Integer
	for i in 0 upto scratch.count() 'fill'
		try scratch.set(i, value: i) otherwise panic("divide: i < scratch.count()")
	end 'fill'

	return (dividend / d) as Integer
end 'divide'

function main() returns ExitCode
	dividend = ident(i64.min)
	let d = ident(-1)
	var scratch = IntArray.create()
	scratch.resize(4)
	return divide(scratch, d: d as NegativeOne) as ExitCode
end 'main'
var dividend = 0
```
```exitcode
1
```
```stderr
panic: integer overflow
Stack trace:
  in divide
  in main
  in mrt_start
```

<!-- test: a-function-with-two-call-sites-stays-a-call -->
⛔ **THE SHAPE CONTROL.** `fill` is well past the leaf inliner's 24 ops and has TWO sites, so neither
rule admits it and the golden keeps both `callDirect`s. Same source shape as the spliced cases with
one call added, which is what makes the two goldens a matched pair.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function fill(a IntArray, n Integer, base Integer) returns Integer
	var total = 0

	for i in 0 upto n 'each'
		let v = base + i * i
		try a.set(i, value: v) otherwise panic("fill: i < n = a.count()")
		total = total + v
	end 'each'

	return total
end 'fill'

function main() returns ExitCode
	let n = 4
	var a = IntArray.create()
	a.resize(n)
	let first = fill(a, n: n, base: 1)
	let second = fill(a, n: n, base: 2)
	let last = try a.get(n - 1) otherwise panic("main: n >= 1")
	return (first + second + last) as ExitCode
end 'main'
```
```exitcode
51
```

<!-- test: a-recursive-called-once-function-stays-a-call -->
`main` calls `depth` once, but `depth` calls itself, so it has two sites and stays a call — into
`main` and into itself. The trace is the ordinary one: a frame per level of the recursion.

```maxon
typealias Integer = int(i64.min to i64.max)

function depth(n Integer) returns Integer
	if n == 1 'bottom'
		panic("depth: reached the bottom")
	end 'bottom'

	return depth(n - 1) + 1
end 'depth'

function main() returns ExitCode
	return depth(3) as ExitCode
end 'main'
```
```exitcode
1
```
```stderr
panic at a-recursive-called-once-function-stays-a-call.test:6: depth: reached the bottom
Stack trace:
  in depth
  in depth
  in depth
  in main
  in mrt_start
```

<!-- test: a-throwing-called-once-function-stays-a-call -->
A throwing body leaves through `errorReturn` and its one site is a `tryCall`; either half refuses
it. The loop and its stores are there so the size rule is not what decides.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

enum FillError
	tooShort
end 'FillError'

function fillChecked(a IntArray, n Integer) returns Integer throws FillError
	if a.count() < n 'short'
		throw FillError.tooShort
	end 'short'

	var total = 0

	for i in 0 upto n 'each'
		try a.set(i, value: i * 3) otherwise panic("fillChecked: i < n <= a.count()")
		total = total + i * 3
	end 'each'

	return total
end 'fillChecked'

function main() returns ExitCode
	var a = IntArray.create()
	a.resize(5)
	let total = try fillChecked(a, n: 5) otherwise 0
	let last = try a.get(4) otherwise panic("main: a.count() = 5")
	return (total + last) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-by-reference-parameter-refuses-the-splice -->
`bumpAll` reassigns `bumps`, so the parameter is passed as the address of the caller's cell and the
caller reads the new value back after the call. The splice is refused and the answer is the
by-reference one: the returned 14 plus `main`'s own `bumps`, which is 14 too.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function bumpAll(a IntArray, n Integer, bumps Integer) returns Integer
	for i in 0 upto n 'each'
		let v = try a.get(i) otherwise panic("bumpAll: i < n = a.count()")
		try a.set(i, value: v + 1) otherwise panic("bumpAll: i < n = a.count()")
		bumps = bumps + 1
	end 'each'

	return bumps
end 'bumpAll'

function main() returns ExitCode
	let n = 4
	var a = IntArray.create()
	a.resize(n)
	var bumps = 10
	let returned = bumpAll(a, n: n, bumps: bumps)
	return (returned + bumps) as ExitCode
end 'main'
```
```exitcode
28
```

<!-- test: two-returns-from-inside-a-loop-join-at-the-continuation -->
`firstIndexOf` returns from INSIDE its loop on a hit and falls through to `return n` otherwise, so the
continuation has two incoming edges from two different loop exits, each carrying its own value. The
one site sits in a loop of its own and takes both paths: 9 is at index 2, 100 is nowhere (4).

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function firstIndexOf(a IntArray, n Integer, needle Integer) returns Integer
	for i in 0 upto n 'scan'
		let v = try a.get(i) otherwise panic("firstIndexOf: i < n = a.count()")

		if v == needle 'hit'
			return i
		end 'hit'
	end 'scan'

	return n
end 'firstIndexOf'

function main() returns ExitCode
	let n = 4
	var a = IntArray.create()
	a.resize(n)

	for i in 0 upto n 'fill'
		try a.set(i, value: 5 + 2 * i) otherwise panic("main: i < n = a.count()")
	end 'fill'

	var total = 0

	for round in 0 to 1 'rounds'
		let needle = 9 if round == 0 else 100
		total = total + firstIndexOf(a, n: n, needle: needle)
	end 'rounds'

	return (total + 36) as ExitCode
end 'main'
```
```exitcode
42
```

<!-- test: a-void-called-once-function-with-stores -->
A void callee writes the caller's array and returns nothing, so its continuation takes no block arg
and its `retVoid` becomes a bare branch. The caller reads what it wrote: 16 + 9.

```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function fillSquares(a IntArray, n Integer)
	for i in 0 upto n 'each'
		try a.set(i, value: i * i) otherwise panic("fillSquares: i < n = a.count()")
	end 'each'
end 'fillSquares'

function main() returns ExitCode
	let n = 5
	var a = IntArray.create()
	a.resize(n)
	fillSquares(a, n: n)
	let fourth = try a.get(4) otherwise panic("main: n = 5")
	let third = try a.get(3) otherwise panic("main: n = 5")
	return (fourth + third) as ExitCode
end 'main'
```
```exitcode
25
```
