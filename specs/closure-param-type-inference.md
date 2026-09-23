---
feature: closure-param-type-inference
status: experimental
keywords: [closure, map, sort, overload, parameters, inference, arity, diagnostics, E2003, E2015, E3005]
category: diagnostics
---

# A Function-Typed Parameter Types the Closure Written at It

## Documentation

The compiler's parser does not INFER an omitted closure parameter type; the call argument the closure is
written at hands it down. Any argument whose parameter is declared with a function type offers one type per
parameter of that function type, in order: `Array.sort(cmp SortComparator)` offers
`function(Element, Element) returns Ordering`, a container's `map` offers `function(Element) returns
Element`, and a user function's `step Combine` offers whatever `Combine` declares. The types are in hand
before the argument is parsed, because the callee's declaration names them.

**The offer is positional, and its arity is the declared one.** Parameter k of the closure takes type k. A
parameter past the end of the offer has no declared slot to be typed from — and nothing to infer it by, since
the compiler has no inference pass. It is refused as un-inferrable (**E2003**), with the reference
bootstrap's own wording, positioned at the parameter's name. A closure that declares an earlier parameter's
type itself is still counted positionally, so `function(a Integer, b)` at a one-parameter slot is refused
exactly as `function(a, b)` is.

**Only a closure literal written AT the argument takes the offer.** A closure nested inside another
argument's expression, or inside the closure's own body, sits at a different position and is offered
nothing there.

**An overloaded callee offers only what every member that could take a closure agrees on.** Which member a
call means is decided a pass later, and it may depend on the closure's own types, so no member is preferred
while the argument is parsed. A member with no parameter at this argument, or whose parameter there is a
concrete type no closure is, cannot be the one a closure resolves to and is set aside — `sort()` beside
`sort(cmp)`. Two members declaring two different function types there withhold the offer, and the untyped
parameter is refused as if nothing offered a type.

A position that offers nothing — including every NAMED function's own parameter list — keeps the E2015
refusal, which names the construct that does infer.

**The ARITY is a second rule standing beside the first, and it is not a closure rule.** A function value
bound at a function-typed parameter must have that parameter's shape, whatever produced the value. Three
shapes reach a `map` transform position with the wrong arity and all three are refused: a closure literal
that types every parameter, a closure that declares none, and a bare reference to a NAMED function of the
wrong shape. The last has no closure literal to look at, which is why the check cannot live in the closure
parse: it is the ordinary argument agreement check (**E3005**), decided whole-program at the argument.

⚠ **THIS DIVERGES FROM THE REFERENCE BOOTSTRAP, DELIBERATELY (user ruling 2026-08-04).** The bootstrap
accepts `nums.map(function(a Integer, b Integer) gives a + b)`, and so did the compiler — both printing `sum=6`.
Both were reading an argument nobody passed. `map` calls its transform with ONE element plus the uniform
`__env` slot, so `b` is whatever the second argument slot happened to hold: change the body to `gives b`
and arm64-macOS prints `sum=0`, a value that is luck rather than an answer. On `wasm32-wasi` the same
program does not run at all — `call_indirect` type-checks the callee's signature, so the call to the
transform traps with *"indirect call type mismatch"*. One target of four could see it, which is what makes
the shape undefined rather than merely unspecified, and agreeing with the reference about an undefined program is not
agreement worth keeping.

The rules stay separate because they refuse different things. E2003 is about a parameter no declared slot
can TYPE and fires in the parser at the parameter's name; E3005 is about a function value whose SHAPE — its
arity or its parameter types — disagrees with the parameter, and fires after merge, at the argument.
`nums.map(function(a String) gives 1)` over an int array used to be accepted — and `gives a.count()`, the
spelling that actually USES the parameter, compiled clean and SEGFAULTED, since the walk hands the transform
the element whatever it declares. It is pinned by `collection.error-map-transform-param-type-mismatch`, with
`collection.map-struct-element-preserved` as its anti-false-refusal control.

⚠ **A shape this file exists to keep out.** Accepted, an untyped `b` past the offer would type itself from
some other slot, and the closure would lift with three ABI slots `(a, b, __env)`; `map`'s call to its
transform passes exactly two, so `b` would read the environment POINTER as its value and `a + b` would
degrade to `a`. It compiled clean and printed a plausible number — `sum=6` for `[1, 2, 3]`, the right answer
to a different question.

⭐ **EVERY CONTAINER'S `map` IS AN ORDINARY DECLARED FUNCTION.** `nums.map(f)` is a call to
`stdlib/Interfaces.maxon`'s `extension Iterable` — `map(transform ElementTransform)` — so the arity half and
the parameter-TYPE half are both what a function-typed argument's own agreement rule answers:
**E3005 `argument type mismatch for 'transform': expected 'fn(int) returns int', got 'fn(int, int) returns
int'`**, at the ARGUMENT's own column. No container synthesizes a `map` of its own, so there is no
transform-specific arity diagnostic: a wrong-arity transform is the same mistake as any other function value
of the wrong shape at a function-typed parameter, and it reads the same.

This is `stdlib-loading.md`'s `print` finding one surface over, and the `sleep` precedent before it —
*"a builtin's bespoke argument rejection, replaced by the ordinary one"* — and it is the shape a retirement
takes every time: the bespoke sentence existed because there was no declaration to read.

## Tests

<!-- test: the-transform-parameter-takes-the-container-element -->
The positive half. One untyped parameter, typed from the array's element, and the transform is actually
applied — so this fails on its VALUE if the refusal below ever over-reaches.
```maxon

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function(a) gives a * 10)
	var sum = 0
	for n in out 'loop'
		sum = sum + n
	end 'loop'
	print("sum={sum}\n")
	return 0
end 'main'
```
```stdout
sum=60
```

<!-- test: error-a-fully-typed-transform-of-the-wrong-arity-is-still-refused -->
Nothing is INFERRED here — every parameter declares its type — so E2003 has nothing to say and the arity
rule is what refuses it. This case used to be pinned as accepted with `sum=6`, on the reference bootstrap's
agreement; the agreement was two compilers reading the same uninitialised second argument. `wasm32-wasi`
does not read it — its `call_indirect` type-checks the signature and traps — and on arm64-macOS a body of
`gives b` prints `sum=0`, the second argument slot's leftovers. The anchor is the argument, not a
parameter: the value is what disagrees with the position.
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function(a Integer, b Integer) gives a + b)
	var sum = 0
	for n in out 'loop'
		sum = sum + n
	end 'loop'
	print("sum={sum}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:6:17: argument type mismatch for 'transform': expected 'fn(int) returns int', got 'fn(int, int) returns int'
```

<!-- test: error-a-transform-that-declares-no-parameter-is-refused-too -->
The UNDER-arity half, and the reason the rule is stated as an equality rather than a ceiling. A
zero-parameter transform is handed the element anyway and simply ignores it, which reads harmless and is
the same undefined call: `wasm32-wasi` traps on it exactly as it traps on the over-arity shape.
```maxon

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function() gives 7)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:17: argument type mismatch for 'transform': expected 'fn(int) returns int', got 'fn() returns int'
```

<!-- test: error-a-named-function-of-the-wrong-arity-is-refused-at-the-argument -->
⭐ **The shape a closure-literal check structurally cannot see.** There is no closure here at all — a bare
reference to a function declared elsewhere — so the arity is not a fact the parse of this line holds. It is
pinned so a later refactor cannot quietly narrow the rule back to closure literals and keep the suite
green.
```maxon
typealias Integer = int(i64.min to i64.max)

function twoArg(a Integer, b Integer) returns Integer
	return a + b
end 'twoArg'

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(twoArg)
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:10:17: argument type mismatch for 'transform': expected 'fn(int) returns int', got 'fn(int, int) returns int'
```

<!-- test: error-a-second-untyped-closure-parameter-is-uninferrable -->
The blocker. `b` is past the one parameter the transform's declared function type offers.
```maxon

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function(a, b) gives a + b)
	var sum = 0
	for n in out 'loop'
		sum = sum + n
	end 'loop'
	print("sum={sum}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2003: <fragment>:5:33: Cannot infer type for closure parameter 'b'. Add an explicit type annotation.
```

<!-- test: error-the-offer-is-spent-even-when-the-first-parameter-declined-it -->
The first parameter declares its own type, so it does not TAKE the offer — and it still occupies the first
position, because the offer is positional. `b` is the second parameter of a one-parameter slot, refused for
the same reason and with the same sentence as above; the reference bootstrap refuses it too (its anchor is
the `)`, the compiler's is the parameter name).
```maxon
typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let nums = [1, 2, 3]
	let out = nums.map(function(a Integer, b) gives a + b)
	var sum = 0
	for n in out 'loop'
		sum = sum + n
	end 'loop'
	print("sum={sum}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2003: <fragment>:6:41: Cannot infer type for closure parameter 'b'. Add an explicit type annotation.
```

<!-- test: error-a-position-that-offers-nothing-still-names-the-one-that-does -->
The `none` refusal, distinct from E2003: a NAMED function's parameter list is typed by no call argument at
all, so its later untyped parameter is E2015 and names the construct that does infer. Pinned here
because the E2003 arm is reached by narrowing this one, and a mistake in that narrowing would show up as
this program changing its answer.
```maxon
typealias Integer = int(i64.min to i64.max)

function twice(a Integer, b) returns Integer
	return a
end 'twice'

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:4:27: Unsupported: parameter 'b' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
```

<!-- test: a-conformers-own-map-types-its-closure-from-its-own-parameter -->
The offer is read off the DECLARATION the call names, never off the method's spelling. `Counter` declares
its OWN `map`, whose transform is `Doubler` — so `x` takes `Doubler`'s `Integer`, and the call runs
`Counter`'s body, not the `extension Iterable` one: `4`, where an element-preserving `map` would have
produced an array.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Doubler = function(Integer) returns Integer

type Counter
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	function map(transform Doubler) returns Integer
		return transform(self.n)
	end 'map'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(2)
	print("{c.map(function(x) gives x * 2)}\n")
	return 0
end 'main'
```
```stdout
4
```

<!-- test: the-comparator-parameters-take-the-container-element -->
`sort`'s comparator is a function-typed parameter of two elements, so a closure passed there takes both
parameter types from it — every position whose declared type is a function type offers one type per
parameter, and a comparator has two.
```maxon
typealias Score = int(i64.min to i64.max)
typealias ScoreArray = Array with Score

function main() returns ExitCode
	var scores = ScoreArray.create()
	scores.push(3)
	scores.push(1)
	scores.push(2)
	scores.sort(function(left, right) gives right.compare(left))

	for score in scores 'each'
		print("{score}\n")
	end 'each'

	return 0
end 'main'
```
```stdout
3
2
1
```

<!-- test: a-user-functions-function-typed-parameter-types-its-closure -->
The offer is not a container's: any call argument whose parameter is declared with a function type types
the closure written there, a user function's as much as the standard library's.
```maxon
typealias Tally = int(0 to 1000)
typealias Combine = function(Tally, Tally) returns Tally

function fold(start Tally, step Combine, times Tally) returns Tally
	var total = start

	for _ in 0 upto times 'each'
		total = step(total, 3)
	end 'each'

	return total
end 'fold'

function main() returns ExitCode
	let total = fold(1, step: function(acc, n) gives acc + n, times: 4)
	print("{total}\n")
	return 0
end 'main'
```
```stdout
13
```

<!-- test: error-overloads-declaring-two-function-types-offer-neither -->
The fail-closed half of the overload rule. Both members of `apply` take a closure at the argument, and they
declare two DIFFERENT function types there — so which one the call means turns on the closure's own
parameter type, which is exactly what is missing. Neither member's types are offered, and the untyped `n`
is refused as though nothing offered a type, rather than typed from a member the call may not resolve to.
```maxon
typealias Tally = int(0 to 1000)
typealias Grow = function(Tally) returns Tally
typealias Judge = function(bool) returns Tally

function apply(step Grow) returns Tally
	return step(3)
end 'apply'

function apply(step Judge) returns Tally
	return step(true)
end 'apply'

function main() returns ExitCode
	print("{apply(function(n) gives n)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:15:25: Unsupported: parameter 'n' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
```

<!-- test: error-a-type-parameter-no-receiver-fixes-is-not-offered-even-as-a-type-argument -->
A static call has no receiver to fix the generic type's own parameter, so `Inspect`'s parameter is still
`Array with T` where the closure is written — a type `main` has no `T` for. The offer is withheld, as it is
for a bare `T`, rather than typing `items` against a parameter nothing at this call binds.
```maxon
typealias Tally = int(0 to 1000)

type Batch uses T
	export typealias Items = Array with T
	export typealias Inspect = function(Items) returns Tally

	static function measure(seed Items, inspect Inspect) returns Tally
		return inspect(seed)
	end 'measure'
end 'Batch'

typealias TallyBatch = Batch with Tally
typealias TallyArray = Array with Tally

function main() returns ExitCode
	var seed = TallyArray.create()
	seed.push(4)
	print("{TallyBatch.measure(seed, inspect: function(items) gives items.count())}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:53: Unsupported: parameter 'items' with no type — the compiler infers an omitted parameter type only for a closure literal passed where the parameter is declared with a function type; every other parameter declares its type
```