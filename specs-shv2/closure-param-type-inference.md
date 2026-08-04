---
feature: closure-param-type-inference
status: experimental
keywords: [closure, map, parameters, inference, diagnostics, E2003]
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

The rule is keyed on INFERENCE, not on arity. A closure that declares every parameter's type is not this
diagnostic's business: shv2 does not check a function value's arity at an indirect call — for a closure
literal or for a named function — and the reference bootstrap does not either, so a transform written
`function(a Integer, b Integer)` is accepted by both and answers the same value. That is a separate hole
in a separate mechanism; refusing it here would make shv2 diverge from the reference in the other
direction. What IS refused is the parameter no position can type, which is exactly what the reference
refuses.

⚠ **A shape this file exists to keep out.** Accepted, the untyped `b` typed itself from the same hint as
`a`, and the closure lifted with three ABI slots `(a, b, __env)`; the array runtime's `callIndirect`
passes exactly two, so `b` read the environment POINTER as its value and `a + b` degraded to `a`. It
compiled clean and printed a plausible number — `sum=6` for `[1, 2, 3]`, the right answer to a different
question.

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

<!-- test: a-fully-typed-transform-is-not-this-diagnostics-business -->
Every parameter declares its type, so nothing is inferred and nothing is refused — measured identical on
the reference bootstrap, which also accepts it and also prints `sum=6`. This case is what keeps the fix
inference-keyed: an arity check here would be a false rejection and a divergence from the reference.
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
```stdout
sum=6
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
