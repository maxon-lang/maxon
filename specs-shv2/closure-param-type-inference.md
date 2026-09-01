---
feature: closure-param-type-inference
status: experimental
keywords: [closure, map, parameters, inference, arity, diagnostics, E2003, E3122]
category: diagnostics
---

# One Position Types One Closure Parameter

## Documentation

shv2's parser does not INFER an omitted parameter type; one call position hands it down. A container's
`map` is that position, and it is the only one: its transform is `function(Element) returns Element` by
the declaration `map` is read from, so the type is in hand before the argument is parsed.

**The offer is for ONE parameter, and taking it spends it.** A transform's arity is 1, so a second
parameter has no second position to be typed from — and nothing to infer it by, since shv2 has no
inference pass. It is refused as un-inferrable (**E2003**), with the reference bootstrap's own wording,
positioned at the parameter's name.

**The ARITY is a second rule standing beside the first (E3122), and it is not a closure rule.** A
transform's arity is 1 by the very declaration the hint is read from, so a transform of any other arity is
refused where the function VALUE is bound — whatever produced it. Three shapes reach that position and all
three are refused: a closure literal that types every parameter, a closure that declares none, and a bare
reference to a NAMED function of the wrong shape. The last has no closure literal to look at, which is why
the check cannot live in the closure parse: it is decided whole-program, at the binding site, off the
value's own governing signature.

⚠ **THIS DIVERGES FROM THE REFERENCE BOOTSTRAP, DELIBERATELY (user ruling 2026-08-04).** The bootstrap
accepts `nums.map(function(a Integer, b Integer) gives a + b)`, and so did shv2 — both printing `sum=6`.
Both were reading an argument nobody passed. `map` calls its transform with ONE element plus the uniform
`__env` slot, so `b` is whatever the second argument slot happened to hold: change the body to `gives b`
and arm64-macOS prints `sum=0`, a value that is luck rather than an answer. On `wasm32-wasi` the same
program does not run at all — `call_indirect` type-checks the callee's signature, so `__managed_map` traps with
*"indirect call type mismatch"*. One target of four could see it, which is what makes the shape undefined
rather than merely unspecified, and agreeing with the reference about an undefined program is not
agreement worth keeping.

The two rules stay separate because they refuse different things. E2003 is about a parameter no position
can TYPE and fires in the parser at the parameter's name; E3122 is about how MANY parameters there are and
fires after merge, at the argument. A THIRD rule stands beside them since BATCH18 and is neither: the
transform's parameter TYPE must be the container's element, refused as **E3005** after merge at the same
argument. `nums.map(function(a String) gives 1)` over an int array used to be accepted — and
`gives a.count()`, the spelling that actually USES the parameter, compiled clean and SEGFAULTED, since the
walk hands the transform the element whatever it declares. It is pinned by
`collection.error-map-transform-param-type-mismatch`, with `collection.map-struct-element-preserved` as its
anti-false-refusal control.

⚠ **A shape this file exists to keep out.** Accepted, the untyped `b` typed itself from the same hint as
`a`, and the closure lifted with three ABI slots `(a, b, __env)`; the array runtime's `callIndirect`
passes exactly two, so `b` read the environment POINTER as its value and `a + b` degraded to `a`. It
compiled clean and printed a plausible number — `sum=6` for `[1, 2, 3]`, the right answer to a different
question.

⭐⭐ **THE THREE `Array` REFUSALS BELOW CHANGED PRODUCER AT X-array-retire, AND THEY ARE THE SAME VERDICT
IN THE ORDINARY VOICE.** `map` left `Parser.arraySurfaceMemberNames`, so `nums.map(f)` on an ARRAY is now
a call to `stdlib/Interfaces.maxon:199`'s `extension Iterable` — an ordinary declared function with an
ordinary `fn(Element) returns Element` parameter — and the ordinary argument check reads that signature and
refuses. E3122 becomes **E3005 `argument type mismatch for 'transform': expected 'fn(int) returns int',
got 'fn(int, int) returns int'`**, at the ARGUMENT's own column rather than four in. Same program refused,
same reason, no capability moved: the arity half and the parameter-TYPE half are both what a function-typed
argument's own agreement rule already answers.

This is `stdlib-loading.md`'s `print` finding one surface over, and the `sleep` precedent before it —
*"a builtin's bespoke argument rejection, replaced by the ordinary one"* — and it is the shape a retirement
takes every time: the bespoke sentence existed because there was no declaration to read.

⚠ **E3122 IS STILL LIVE, AND ON PURPOSE**: `Set` and `Map` are still SYNTHESIZED surfaces whose `map` goes
through `Parser.parseContainerMap`, which still raises it (`collection.map-set-with-declared-ranged-alias`
is on that path). So the two voices coexist until those surfaces retire too — which is a real, temporary
divergence between containers and is recorded here rather than left for a reader to discover from a
diff.

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
The blocker. `b` is past the one parameter the call position types.
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
The first parameter declares its own type, so the hint is not TAKEN — and it is spent all the same,
because the closure has still had the one position that types anything. Refused for the same reason and
with the same sentence; the reference bootstrap refuses it too (its anchor is the `)`, shv2's is the
parameter name).
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
The `none` refusal, unchanged and distinct: a NAMED function's parameter list is typed by no position at
all, so its later untyped parameter is E2015 and names the one construct that does infer. Pinned here
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
error E2015: <fragment>:4:27: Unsupported: parameter 'b' with no type — shv2 infers an omitted parameter type in exactly one position, a closure passed to a container's `map`, whose transform takes the container's own element; every other parameter declares its type
```

<!-- test: error-a-conformers-own-map-is-not-the-extensions-map -->
The FALSE-REJECT BOUNDARY of the corpus arm (W41). The offer widened from the builtin container roster to
any `map` an interface `extension` PUBLISHES onto a conformer, and the gate that keeps that from becoming
"any method spelled `map`" is `ProgramSignatures.methodPublishedByExtension`. `Counter` declares its OWN
`map`, whose transform takes an `Integer` and which returns one — so there is no container element to
offer, and the declared return's element (which the extension arm reads) would be a fabrication. The
inference must stay silent here and the refusal must be the unchanged `none` one, naming the construct
that does infer.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Doubler = function(Integer) returns Integer

type Counter
	var n as Integer

	export static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'

	export function map(transform Doubler) returns Integer
		return transform(self.n)
	end 'map'
end 'Counter'

function main() returns ExitCode
	let c = Counter.create(2)
	return c.map(function(x) gives x * 2) as ExitCode
end 'main'
```
```maxoncstderr
error E2015: <fragment>:19:24: Unsupported: parameter 'x' with no type — shv2 infers an omitted parameter type in exactly one position, a closure passed to a container's `map`, whose transform takes the container's own element; every other parameter declares its type
```
