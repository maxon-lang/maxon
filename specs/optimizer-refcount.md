---
feature: optimizer-refcount
status: selfhosted
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

The scoreboard is each case's minted fragment golden: the emitted code, with
every `__mm_incref` / `__mm_decref` the compiler places. It is never
hand-written — it is regenerated via
`maxon spec-test --filter=optimizer-refcount --update-required`.

When a refcount optimization fires, the golden changes. The diff **is** the
measured impact: fewer `__mm_incref` / `__mm_decref` calls means less runtime
refcount traffic. Reviewing the diff is how the optimization is kept correct —
the allocations must stay the same, and the suite's leak gate (exit 101) fails
any case whose objects are not all freed exactly once.

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

⚠ THIS CASE CARRIES NO `RequiredIR:<target>` BLOCK. The compiler's spec parser has an arm for neither
`x64-windows` nor `wasm32-wasi`, so such a block would be read by nobody while reading as coverage, and
`SpecParser.isUnimplementedFenceOpen` refuses the fence rather than walking past it. What pins the
emitted code here is this case's minted fragment golden, which records what this compiler emits.

## Regression tests — sibling aliases cleaned up in one block

An immutable `let` of an immutable binding is an ALIAS: it borrows the
source and takes no reference of its own, so no incref/decref bracket
surrounds its scope. These fragments pin that when several such aliases
end in the same block.

<!-- test: prefix-kill-sibling-cleanup -->
Two aliases, `b` of `a` and `d` of `c`, whose scopes end in the same
block. Neither takes a reference, so neither needs a bracket.
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

## Regression tests — an alias read on mutually-exclusive exits

An alias read on several mutually-exclusive paths (e.g. match arms) still
takes no reference, so none of the paths owes a decref for it.

<!-- test: multi-exit-match-arm-brackets -->
An alias read on two mutually-exclusive match arms. It takes no
reference, so neither arm carries a decref for it; the source's own
single decref is the only one.
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
Three-way exit: the alias is read on three match arms, and none of
them carries a decref for it.
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

## Regression tests — try-call borrow-awareness

An alias passed to a throwing callee through `try` stays a borrow: the
call does not make it take a reference, so no incref/decref bracket
surrounds the alias. A callee that stores its argument durably takes its
own reference instead. The fragment golden is the authoritative
assertion — its diff after a future change catches an accidental
bracket.

<!-- test: try-call-borrow-only-window -->
Alias assignment `let b = a` in an inner block, followed by a try-call
on a borrow-only callee inside the same block. `b` takes no reference,
so the only decref is `a`'s own at its scope end.
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
Same shape, but the callee retains its argument (pushes it into an
array). A durable store co-owns, so the callee takes its own reference
(`__mm_own`) and the array's drop releases it, independently of the
caller's single decref.
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
The same heap pointer held by two bindings — a function's result and an
alias of it — with a try-call between. The alias takes no reference, so
there is no second incref/decref pair.
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
iterations. The alias takes no reference, so no iteration carries an
incref/decref for it.
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

## Regression tests — a module-global read borrow-only

A module-global load that a function only reads — no value derived from
it reaches a retention — borrows the global and takes no reference.

<!-- test: global-struct-load-borrow -->
A module-level managed struct global is read borrow-only inside a
function — it reads a single field and returns a scalar comparison. The
load carries no incref/decref bracket: neither call appears in the
golden.
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
