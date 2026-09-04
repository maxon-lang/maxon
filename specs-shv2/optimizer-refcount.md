---
feature: optimizer-refcount
status: selfhosted
status-reason: 8 of its 9 cases pass here; the 9th pins the whole-program refcount baseline through a RequiredIR block in v1's dump format this runner's section comparer cannot read (measured 2026-08-06, BATCH29/A3a). shv2 also runs 8 of 9, failing that same case on E3102 (use of moved value), so the baseline itself needs a rung.
keywords: [refcount, incref, decref, optimization, mm-trace, managed-memory, whole-program]
category: compiler
---

# Refcount Optimization Baseline

## Documentation

This spec is the regression harness and visible scoreboard for the refcount
optimizer. It holds one whole-program test that exercises a wide variety of
patterns known to produce `mm_incref` / `mm_decref` traffic:

- struct aliasing
- short-lived temporaries passed into functions
- loop-carried container pushes
- nested containers
- function parameter passing (caller incref / callee scope-end decref)
- return-ownership transfer (factory pattern)
- struct field reassignment
- union-with-managed-payload matching
- closure capturing a managed value

The committed `stderr` block is the full `--mm-trace` output at the time the
baseline was generated; the `RequiredIR:x64-windows` block is the full IR
dump at every pipeline stage. Neither block should be hand-written — both are
regenerated via `maxon spec-test --filter=optimizer-refcount --update-required`.

When a refcount optimization lands, both blocks will change. The diff **is**
the measured impact: fewer lines in `stderr` means fewer runtime
increfs/decrefs; fewer `mm_incref` / `mm_decref` ops in the IR confirms the
optimizer (not just runtime folding) was responsible. Reviewing the diff is
how we keep the pass correct — the set of `mm_alloc` / `mm_free` must stay
identical, and every object must still reach `rc=0`.

The program is deliberately larger than a typical spec test: future
whole-program / interprocedural passes need multi-function call graphs,
cross-function ownership flow, and nested scopes all present at once to have
anything meaningful to optimize.

## Tests

<!-- test: refcount-baseline-whole-program -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer
typealias StringArray = Array with String
typealias Matrix = Array with IntArray
typealias PointArray = Array with Point

typealias FnTypeAlias1 = function(Integer) returns Integer

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

type Person
	export var name as String
	export var age as Integer

	static function create(name String, age Integer) returns Self
		return Self{name: name, age: age}
	end 'create'
end 'Person'

union Shape
	circle(label String)
	square(label String)
	blank
end 'Shape'

function sum_point(p Point) returns Integer
	return p.x + p.y
end 'sum_point'

function make_point(x Integer, y Integer) returns Point
	return Point.create(x, y: y)
end 'make_point'

function describe(s Shape) returns Integer
	return match s 'describe'
		circle(label) gives label.count() as Integer
		square(label) gives label.count() as Integer
		blank gives 0
	end 'describe'
end 'describe'

function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function names_total(arr StringArray) returns Integer
	return arr.count()
end 'names_total'

function row_total(arr IntArray) returns Integer
	var sum = 0
	for v in arr 'iter'
		sum = sum + v
	end 'iter'
	return sum
end 'row_total'

function matrix_total(m Matrix) returns Integer
	var sum = 0
	for row in m 'iter'
		sum = sum + row_total(row)
	end 'iter'
	return sum
end 'matrix_total'

function points_x_sum(pts PointArray) returns Integer
	var sum = 0
	for p in pts 'iter'
		sum = sum + p.x
	end 'iter'
	return sum
end 'points_x_sum'

function main() returns ExitCode
	var total = 0

	// --- section 1: struct literal + alias ---
	var a = Point.create(1, y: 2)
	var b = a
	b.x = 99
	a = b
	total = total + a.x

	// --- section 2: short-lived temp passed to function ---
	total = total + sum_point(Point.create(3, y: 4))
	total = total + sum_point(Point.create(5, y: 6))

	// --- section 3: loop-carried container pushes ---
	var names = StringArray.create()
	for i in 0 upto 5 'names_loop'
		names.push("name_{i}")
	end 'names_loop'
	total = total + names_total(names)

	// --- section 4: nested container ---
	var row1 = IntArray.create()
	row1.push(1)
	row1.push(2)
	var row2 = IntArray.create()
	row2.push(3)
	row2.push(4)
	var matrix = Matrix.create()
	matrix.push(row1)
	matrix.push(row2)
	total = total + matrix_total(matrix)

	// --- section 5: function parameter passing ---
	var origin = Point.create(0, y: 0)
	total = total + sum_point(origin)
	total = total + sum_point(origin)

	// --- section 6: return-ownership transfer (factory) ---
	let made = make_point(10, y: 20)
	total = total + made.x

	// --- section 7: struct field reassignment ---
	var person = Person.create("alice", age: 30)
	person.name = "bob"
	person.name = "carol"
	total = total + person.age

	// --- section 8: union with managed payload ---
	let shape1 = Shape.circle("ring")
	let shape2 = Shape.square("box")
	let shape3 = Shape.blank
	total = total + describe(shape1)
	total = total + describe(shape2)
	total = total + describe(shape3)

	// --- section 9: closure capturing a managed value ---
	let prefix = "pfx_"
	let builder = function(n Integer) gives "{prefix}{n}".count() as Integer
	total = total + apply(builder, x: 7)
	total = total + apply(builder, x: 8)

	// --- section 10: for-in over managed elements, primitive body ---
	// exercises the for-in lowering pattern (__forin_result + user var alias)
	var points = PointArray.create()
	points.push(Point.create(1, y: 2))
	points.push(Point.create(3, y: 4))
	points.push(Point.create(5, y: 6))
	total = total + points_x_sum(points)

	// --- section 11: in-loop try-alias + borrow-call bracket ---
	// Inside each iteration, a try-binding creates an implicit alias between
	// the try-result slot and the user-visible `p`. The emitter brackets the
	// subsequent borrowed use with an incref/decref on `p`. Loop-invariant
	// elimination collapses the bracket: the try-result owns the rc=1
	// transferred reference and the direct call is borrow-only, so the
	// extra +1/-1 is pure overhead.
	var triplet = PointArray.create()
	triplet.push(Point.create(7, y: 8))
	triplet.push(Point.create(9, y: 10))
	triplet.push(Point.create(11, y: 12))
	for i in 0 upto 3 'alias_loop'
		let p = try triplet.get(i) otherwise 'missErr'
			panic("alias_loop: triplet.get({i}) invariant violated")
		end 'missErr'
		total = total + sum_point(p)
	end 'alias_loop'

	// Prevent optimizer from eliminating the work — but exit 0.
	if total < 0 'guard'
		return 1
	end 'guard'
	return 0
end 'main'
```
```exitcode
0
```

⚠ THE `/specs` ORIGINAL PINS TWO `RequiredIR:<target>` BLOCKS HERE — one `x64-windows`, one
`wasm32-wasi` — AND NEITHER SURVIVES THE PORT. shv2's spec parser has an arm for neither, so both
would be read by nobody while reading as coverage — the shape BATCH29 exists to remove, and
`SpecParser.isUnimplementedFenceOpen` refuses the fence rather than walking past it. What pins the
emitted code here is this case's minted fragment golden, which records what THIS compiler emits
rather than what v1 did.

## Phase 3 regression tests — aliasFromStore prefix-kill relaxation

These fragments guard the relaxation in
`IsCrossBlockPairSafe` / `TryPrefixIsBenignSiblingCleanup` that accepts
a prefix containing sibling scope-end cleanup ops (load + decref of
unrelated slots, plus optionally a decref of srcVar) as safe under
Maxon's borrow convention. The relaxation unlocks for-in tuple
brackets and similar shapes where srcVar's own scope-end decref fires
in the same block before varName's decref.

<!-- test: prefix-kill-sibling-cleanup -->
Two aliased struct slots both scope-end-decreffed in the same block.
When `b`'s decref comes first in the prefix, the alias anchor for `a`
is already "killed" in the legacy sense — the relaxation recognises
this as sibling cleanup and eliminates the alias bracket.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function main() returns ExitCode
	@heap let a = Box.create(7)
	@heap let c = Box.create(11)
	var total = 0
	if true 'outer'
		let b = a
		let d = c
		total = b.value + d.value
	end 'outer'
	return total
end 'main'
```
```exitcode
18
```

## Phase 2 regression tests — multi-exit bracket elimination

These fragments guard the relaxation in `CancelCrossBlockRedundantRefcounts`
that allows an incref to pair with more than one reachable decref block
when the matched decrefs are on mutually-exclusive paths (e.g. match arms
that both scope-clean the same slot at their exits).

<!-- test: multi-exit-match-arm-brackets -->
An aliased slot whose scope-end decrefs sit on two mutually-exclusive
match arms. The incref in the pre-match block dominates both decref
blocks; each iteration from the incref hits exactly one of them. Phase 2
eliminates the bracket as a group.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

union Tag
	first
	second
end 'Tag'

function main() returns ExitCode
	@heap let a = Box.create(42)
	let tag = Tag.first
	var total = 0
	if true 'inner'
		let b = a
		match tag 'branch'
			first then total = b.value
			second then total = b.value + 1
		end 'branch'
	end 'inner'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: multi-exit-three-way-split -->
Three-way exit (three match arms, each decrefing the aliased slot at
its scope end). Phase 2 eliminates the shared-source bracket across all
three.
```maxon
typealias Integer = int(i64.min to i64.max)

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

union Tag
	a
	b
	c
end 'Tag'

function main() returns ExitCode
	@heap let x = Box.create(7)
	let tag = Tag.b
	var total = 0
	if true 'inner'
		let alias = x
		match tag 'three'
			a then total = alias.value
			b then total = alias.value * 2
			c then total = alias.value * 3
		end 'three'
	end 'inner'
	return total
end 'main'
```
```exitcode
14
```

## Phase 1 regression tests — try-call borrow-awareness

These fragments are regression guards for the try-call relaxation of
`RefcountOptimizationPass.ClassifyAliasingOp`. Before Phase 1 they would
leave an incref/decref bracket on the aliased slot intact; after Phase 1
the bracket is eliminated because the try-call's callee is proven
borrow-only on every argument. The scoreboard stderr block is the
authoritative assertion — reviewing its diff after a future change
catches accidental regression of this optimization.

<!-- test: try-call-borrow-only-window -->
Alias assignment `let b = a` in an inner block, followed by a try-call
on a borrow-only callee inside the same block. The bracket on `b`
spans the try-call and `b`'s scope-end decref fires before `a`'s outer
scope-end decref — so `a`'s decref is not inside `b`'s window and the
firstStoreOf-safety check passes. Phase 1 eliminates `b`'s bracket.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function inspect(b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	return b.value
end 'inspect'

function main() returns ExitCode
	@heap let a = Box.create(42)
	var total = 0
	if true 'inner'
		let b = a
		let n = try inspect(b) otherwise 0
		total = n
	end 'inner'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: try-call-retaining-callee-preserved -->
Negative: same shape but the callee retains its argument (stores it
into a container field). The bracket must be preserved — the callee
holds its own ref independently and could outlive the caller's window.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

typealias BoxArray = Array with Box

function stash(arr BoxArray, b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	arr.push(b)
	return b.value
end 'stash'

function main() returns ExitCode
	var arr = BoxArray.create()
	@heap let a = Box.create(42)
	let b = a
	let n = try stash(arr, b: b) otherwise 0
	return n
end 'main'
```
```exitcode
42
```

<!-- test: try-call-aliasfromstore-window -->
The firstStoreOf alias shape (same SSA heap pointer stored into two
slots with a try-call between). Mirrors the for-in lowering that
stores `iter.current()` into both `__forin_result` and the user's
loop variable. Phase 1 eliminates the second slot's incref/decref
pair.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function peek(b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	return b.value
end 'peek'

function pair() returns Box
	return Box.create(42)
end 'pair'

function main() returns ExitCode
	@heap let primary = pair()
	var total = 0
	if true 'inner'
		let alias = primary
		let n = try peek(alias) otherwise 0
		total = n
	end 'inner'
	return total
end 'main'
```
```exitcode
42
```

<!-- test: try-call-inside-loop-body -->
Try-call inside a loop body where the alias source is stable across
iterations. The loop-invariant sub-pass eliminates the per-iteration
incref/decref on the alias slot. Mirrors the
`__ListIterator_OpIndex.advance` hot spot surfaced by the whole-compiler
baseline.
```maxon
typealias Integer = int(i64.min to i64.max)

union BoxError
	negative
end 'BoxError'

type Box
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Box'

function peek(b Box) returns Integer throws BoxError
	if b.value < 0 'neg'
		throw BoxError.negative
	end 'neg'
	return b.value
end 'peek'

function main() returns ExitCode
	@heap let boxed = Box.create(7)
	var total = 0
	for _ in 0 upto 3 'loop'
		let alias = boxed
		let n = try peek(alias) otherwise 0
		total = total + n
	end 'loop'
	return total
end 'main'
```
```exitcode
21
```

## Phase 4 regression tests — global-load anchor elimination

These fragments guard `CancelGlobalLoadOrphanBrackets` in
`RefcountOptimizationPass`. The sub-pass removes the `mm_incref` +
`mm_decref_if_nonnull` bracket emitted around a module-global load into
an orphan temp, when the function is proven borrow-only on that global
(no tainted-from-global SSA value reaches a retention event in the body).

<!-- test: global-struct-load-borrow -->
A module-level managed struct global is read borrow-only inside a
function — it reads a single field via `load_indirect` and returns a
scalar comparison. The emitter wraps the global load in incref+decref
brackets (orphan-temp pattern). After Phase 4, the brackets are gone:
`mm_incref Config [check]` and `mm_decref Config [check]` do not appear
in the trace.
```maxon
typealias Integer = int(i64.min to i64.max)

type Config
	export var threshold as Integer

	static function create(threshold Integer) returns Self
		return Self{threshold: threshold}
	end 'create'
end 'Config'

var cfg = Config.create(10)

function check(value Integer) returns Integer
	if value > cfg.threshold 'high'
		return value
	end 'high'
	return 0
end 'check'

function main() returns ExitCode
	return check(42)
end 'main'
```
```exitcode
42
```
