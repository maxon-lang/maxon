---
feature: regions-byref-types
status: experimental
keywords: [by-reference, mutable, typealias, range, variable type, parameter type, E3005]
category: semantics
---

# A by-reference argument's variable has the parameter's own type

## Documentation

A variable handed to a `mutable` parameter is a flow in both directions: the callee reads the variable's value and may
write any value of the parameter's type back. So the variable's declared type must be the parameter's type, alias
identity included: a `Narrow` variable at a `mutable Wide` parameter is refused, because the callee can write a `Wide`
value the variable cannot hold, and a `Wide` variable at a `mutable Narrow` parameter is refused too. Declare the
variable at the parameter's type, or pass a local of that type. The rule holds for direct calls, method calls and
coroutines alike.

## Tests

<!-- test: a-narrow-variable-at-a-wider-mutable-parameter-is-refused -->
`widen` may write any `Wide` value, which the `Narrow` variable `x` cannot hold.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function widen(n mutable Wide)
	n = 50
end 'widen'

function main() returns ExitCode
	var x = 3 as Narrow
	widen(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:8: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-wide-variable-at-a-narrower-mutable-parameter-is-refused -->
The variable's type must equal the parameter's in this direction too.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function shrink(n mutable Narrow)
	n = 5
end 'shrink'

function main() returns ExitCode
	var x = 40 as Wide
	shrink(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: argument type mismatch for 'n': expected 'Narrow', got 'Wide' — cast the argument with 'as Narrow'
```

<!-- test: a-narrow-variable-at-a-wider-mutable-parameter-of-a-method-is-refused -->
A method call is a call like any other.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Settler
	var total as Wide

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	function settle(n mutable Wide)
		n = n + self.total
	end 'settle'
end 'Settler'

function main() returns ExitCode
	let settler = Settler.create()
	var x = 3 as Narrow
	settler.settle(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:17: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-narrow-variable-at-a-wider-mutable-parameter-of-an-async-callee-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The coroutine writes the variable itself, so the same rule holds.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide) returns Wide
	Scheduler.yield()
	n = 50

	return n
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	let p = async bumpWide(x)
	let r = await p
	print("{x} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:25: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-narrow-variable-at-a-wider-mutable-parameter-of-a-generic-async-callee-is-refused -->
<!-- unsupported-targets: wasm32-wasi -->
The generic instance a coroutine runs is checked as a direct call's is: `bumpWide` writes any `Wide` into the caller's variable.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function bumpWide(n mutable Wide, tag T) uses T returns T
	Scheduler.yield()
	n = 50

	return tag
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Narrow
	let p = async bumpWide(x, tag: "t")
	let tag = await p
	print("{x}{tag}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:25: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: an-inline-range-at-a-mutable-parameter-is-refused-at-its-declaration -->
A `mutable` parameter has a named alias for its type, so no formal is an unnamed range the variable could be compared to.

```maxon
typealias Narrow = int(0 to 10)

function widen(n mutable int(0 to 100))
	n = 50
end 'widen'

function main() returns ExitCode
	var x = 3 as Narrow
	widen(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:4:26: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: a-variable-of-the-parameters-own-type-at-a-generic-async-callee-is-accepted -->
<!-- unsupported-targets: wasm32-wasi -->
The control: a `Wide` variable at the generic coroutine's `mutable Wide` parameter.

```maxon
typealias Wide = int(0 to 100)

function bumpWide(n mutable Wide, tag T) uses T returns T
	Scheduler.yield()
	n = 50

	return tag
end 'bumpWide'

function main() returns ExitCode
	var x = 3 as Wide
	let p = async bumpWide(x, tag: "t")
	let tag = await p
	print("{x}{tag}\n")
	return 0
end 'main'
```
```stdout
50t
```
```exitcode
0
```

<!-- test: a-variable-of-the-parameters-own-type-is-accepted -->
The control: a `Wide` variable at a `mutable Wide` parameter.

```maxon
typealias Wide = int(0 to 100)

function widen(n mutable Wide)
	n = 50
end 'widen'

function main() returns ExitCode
	var x = 3 as Wide
	widen(x)
	print("{x}\n")
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: a-narrow-field-at-a-wider-mutable-parameter-is-refused -->
A field cell handed by reference has the field's declared type.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Gauge
	export var level as Narrow

	static function create() returns Self
		return Self{level: 3}
	end 'create'
end 'Gauge'

function widen(n mutable Wide)
	n = 50
end 'widen'

function main() returns ExitCode
	var gauge = Gauge.create()
	widen(gauge.level)
	print("{gauge.level}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:19:8: argument type mismatch for 'n': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-parameter-relayed-at-its-own-type-is-accepted -->
A `mutable Wide` parameter handed on to another `mutable Wide` parameter has the right type.

```maxon
typealias Wide = int(0 to 100)

function widen(n mutable Wide)
	n = 50
end 'widen'

function relay(n mutable Wide)
	widen(n)
end 'relay'

function main() returns ExitCode
	var x = 3 as Wide
	relay(x)
	print("{x}\n")
	return 0
end 'main'
```
```stdout
50
```
```exitcode
0
```

<!-- test: a-write-through-a-type-parameter-by-reference-is-refused-in-the-generic-body-async -->
<!-- unsupported-targets: wasm32-wasi -->
A generic body cannot rebind a `mutable T` parameter, and the instance's own formal is `T` bound to its argument, so a variable handed there has exactly that type.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function setTo(slot mutable T, value T) uses T
	Scheduler.yield()
	slot = value
end 'setTo'

function main() returns ExitCode
	var x = 3 as Narrow
	let w = 90 as Wide
	let p = async setTo(x, value: w)
	await p
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:7:2: 'slot' is a generic parameter and cannot be reassigned; return the new value
```

<!-- test: a-write-through-a-type-parameter-by-reference-is-refused-in-the-generic-body-sync -->
The direct call's twin of the coroutine above.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function setTo(slot mutable T, value T) uses T
	slot = value
end 'setTo'

function main() returns ExitCode
	var x = 3 as Narrow
	let w = 90 as Wide
	setTo(x, value: w)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2013: <fragment>:6:2: 'slot' is a generic parameter and cannot be reassigned; return the new value
```

<!-- test: a-narrow-variable-at-a-type-parameter-by-reference-of-a-wider-instance-is-refused-async -->
<!-- unsupported-targets: wasm32-wasi -->
The other arguments make `T` `Wide`, and the instance may overwrite the `Narrow` variable with any `Wide`.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function pick(slot mutable T, other T, first bool) uses T returns T
	Scheduler.yield()

	return slot if first else other
end 'pick'

function main() returns ExitCode
	var x = 3 as Narrow
	let w = 90 as Wide
	let p = async pick(x, other: w, first: false)
	let r = await p
	print("{x} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:21: argument type mismatch for 'slot': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-narrow-variable-at-a-type-parameter-by-reference-of-a-wider-instance-is-refused-sync -->
The direct call is refused as the coroutine is.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function pick(slot mutable T, other T, first bool) uses T returns T
	return slot if first else other
end 'pick'

function main() returns ExitCode
	var x = 3 as Narrow
	let w = 90 as Wide
	let r = pick(x, other: w, first: false)
	print("{x} {r}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:12:15: argument type mismatch for 'slot': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-variable-of-the-instances-own-type-at-a-type-parameter-by-reference-is-accepted-async -->
<!-- unsupported-targets: wasm32-wasi -->
The control: the variable and the value are both `Narrow`, so `T` is `Narrow`.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function pick(slot mutable T, other T, first bool) uses T returns T
	Scheduler.yield()

	return slot if first else other
end 'pick'

function main() returns ExitCode
	var x = 3 as Narrow
	let n = 7 as Narrow
	let p = async pick(x, other: n, first: false)
	let r = await p
	print("{x} {r}\n")
	return 0
end 'main'
```
```stdout
3 7
```
```exitcode
0
```

<!-- test: a-variable-of-the-instances-own-type-at-a-type-parameter-by-reference-is-accepted-sync -->
The control: the variable and the value are both `Narrow`, so `T` is `Narrow`.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function pick(slot mutable T, other T, first bool) uses T returns T
	return slot if first else other
end 'pick'

function main() returns ExitCode
	var x = 3 as Narrow
	let n = 7 as Narrow
	let r = pick(x, other: n, first: false)
	print("{x} {r}\n")
	return 0
end 'main'
```
```stdout
3 7
```
```exitcode
0
```

<!-- test: a-narrow-variable-in-a-generic-body-at-a-nested-generic-by-reference-is-refused-sync -->
The `inner` instance is requested only while the `outer` instance's body is parsed, and its `T` is `Wide` through `other`.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function inner(slot mutable T, other T, first bool) uses T returns T
	return slot if first else other
end 'inner'

function outer(other T) uses T returns T
	var y = 3 as Narrow
	return inner(y, other: other, first: false)
end 'outer'

function main() returns ExitCode
	let w = 90 as Wide
	print("{outer(w)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:15: argument type mismatch for 'slot': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-narrow-variable-in-a-generic-body-at-a-nested-generic-by-reference-is-refused-async -->
<!-- unsupported-targets: wasm32-wasi -->
The coroutine twin of the nested instance above.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function inner(slot mutable T, other T, first bool) uses T returns T
	Scheduler.yield()

	return slot if first else other
end 'inner'

function outer(other T) uses T returns T
	var y = 3 as Narrow
	let p = async inner(y, other: other, first: false)

	return await p
end 'outer'

function main() returns ExitCode
	let w = 90 as Wide
	print("{outer(w)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:13:22: argument type mismatch for 'slot': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-mutable-type-parameter-handed-on-to-a-wider-formal-is-refused-at-the-caller-sync -->
`pass` hands its `slot` on to `widen`, whose formal is `mutable Wide`, so the instance `pass` with `T` as `Wide` may write any `Wide` through the `Narrow` variable.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function widen(n mutable Wide)
	n = 50
end 'widen'

function pass(slot mutable T, other T) uses T
	widen(slot)
end 'pass'

function main() returns ExitCode
	var x = 3 as Narrow
	let w = 90 as Wide
	pass(x, other: w)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:16:7: argument type mismatch for 'slot': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-mutable-type-parameter-handed-on-to-a-wider-formal-is-refused-at-the-caller-async -->
<!-- unsupported-targets: wasm32-wasi -->
The coroutine twin of the relay above.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function widen(n mutable Wide)
	n = 50
end 'widen'

function pass(slot mutable T, other T) uses T
	Scheduler.yield()
	widen(slot)
end 'pass'

function main() returns ExitCode
	var x = 3 as Narrow
	let w = 90 as Wide
	let p = async pass(x, other: w)
	await p
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:21: argument type mismatch for 'slot': expected 'Wide', got 'Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-generic-types-method-with-a-type-parameter-formal-writes-nothing-through-it -->
A method of a generic type has one body for every instance, where `T` is opaque: it cannot rebind `v`, cannot hand it to a function whose formal is a concrete alias, and a generic function cannot take an opaque `T`. A `Narrow` variable handed there is never overwritten.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

type Box uses T
	var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	function fill(v mutable T)
		self.value = v
	end 'fill'
end 'Box'

typealias WideBox = Box with Wide

function main() returns ExitCode
	let b = WideBox.create(2)
	var x = 3 as Narrow
	b.fill(x)
	print("{x}\n")
	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: an-opaque-type-parameter-cannot-be-handed-to-a-generic-function-by-reference -->
The generic type's method cannot relay its `mutable T` formal to a generic function either.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function widen(n mutable Wide)
	n = 50
end 'widen'

function pass(slot mutable T, other T) uses T
	widen(slot)
end 'pass'

type Box uses T
	var value as T

	static function create(v T) returns Self
		return Self{value: v}
	end 'create'

	function fill(v mutable T)
		pass(v, other: self.value)
	end 'fill'
end 'Box'

typealias WideBox = Box with Wide

function main() returns ExitCode
	var b = WideBox.create(2)
	var x = 3 as Narrow
	b.fill(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3181: <fragment>:21:3: type parameter 'T' of 'pass' cannot be 'type parameter': a generic function's type parameter is inferred as a number, `String`, `bool`, `Character`, a typealias, a record or an enum
```

<!-- test: a-method-of-a-primitive-cannot-assign-its-receiver -->
A method's receiver is read-only, so a `Narrow` variable it is called on is never overwritten.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

extension int
	function bump()
		self = 99
	end 'bump'
end 'int'

function main() returns ExitCode
	var x = 3 as Narrow
	x.bump()
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2001: <fragment>:7:3: unexpected token: 'self'
```

<!-- test: a-field-cell-declared-in-another-file-keeps-its-own-alias-when-the-reader-declares-the-name -->
The field's `Narrow` is the declaring file's, whatever `app/main.maxon` means by the same name, so it is compared with `Wide` as that declaration.

```maxon
// --- file: a/gauge.maxon
export typealias Wide = int(0 to 100)
export typealias Narrow = int(0 to 10) implements Wide

export type Gauge
	export var level as Narrow

	export static function create() returns Self
		return Self{level: 3}
	end 'create'
end 'Gauge'

// --- file: app/main.maxon
typealias Narrow = int(0 to 5)

function widen(n mutable Wide)
	n = 50
end 'widen'

function main() returns ExitCode
	var gauge = Gauge.create()
	var other = 2 as Narrow
	widen(gauge.level)
	print("{gauge.level} {other}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: app/<fragment>:24:8: argument type mismatch for 'n': expected 'Wide', got 'a.Narrow': the callee may write any 'Wide' through a by-reference parameter, which a 'a.Narrow' variable cannot hold. Declare the variable or field it names as Wide, or pass a Wide local
```

<!-- test: a-function-type-cannot-name-a-type-parameter -->
A function type is a typealias, and a typealias names no type parameter, so no function value has a `T` formal for a `Narrow` variable to be handed to.

```maxon
typealias Wide = int(0 to 100)

typealias Step = function(mutable T)

function widen(n mutable Wide)
	n = 50
end 'widen'

function apply(step Step, n mutable Wide)
	step(n)
end 'apply'

function main() returns ExitCode
	var x = 3 as Wide
	apply(widen, n: x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3011: <fragment>:4:35: Unknown type 'T'
```

<!-- test: a-generic-function-is-not-a-function-value -->
A generic function has no single formal for a function value to carry, so it cannot be passed where a `mutable Wide` function is expected.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide
typealias Step = function(mutable Wide)

function widen(n mutable Wide)
	n = 50
end 'widen'

function relay(n mutable T) uses T
	widen(n)
end 'relay'

function apply(step Step, n mutable Wide)
	step(n)
end 'apply'

function main() returns ExitCode
	var x = 3 as Narrow
	apply(relay, n: x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E2004: <fragment>:20:8: Undefined variable 'relay'
```

<!-- test: a-narrow-tuple-variable-at-a-wider-mutable-tuple-parameter-is-refused -->
A tuple's elements are ranged too, so the callee may write a `Wide` element a `Narrow` element cannot hold.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide
typealias WidePair = (Wide, Wide)
typealias NarrowPair = (Narrow, Narrow)

function widen(p mutable WidePair)
	p.0 = 50
end 'widen'

function make() returns NarrowPair
	return (3, 4)
end 'make'

function main() returns ExitCode
	var x = make()
	widen(x)
	print("{x.0}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:17:2: argument type mismatch for 'p': expected 'WidePair', got '(Narrow, Narrow)'
```

<!-- test: a-narrow-variable-at-a-mutable-exitcode-is-refused -->
`ExitCode` is a ranged alias of its own, so a `Narrow` variable is not an `ExitCode` variable.

```maxon
typealias Wide = int(0 to 100)
typealias Narrow = int(0 to 10) implements Wide

function reset(code mutable ExitCode)
	code = 77
end 'reset'

function main() returns ExitCode
	var x = 3 as Narrow
	reset(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3005: <fragment>:11:2: argument type mismatch for 'code': expected 'ExitCode', got 'Narrow' — cast the argument with 'as ExitCode'
```

<!-- test: a-generic-and-a-non-generic-function-of-one-name-bind-in-their-own-directories -->
The two `bump`s have different parameter names and live in different directories, so each call binds the declaration its reader names and the label fills that declaration's parameter.

```maxon
// --- file: a/a.maxon
export typealias Wide = int(0 to 100)

function widen(n mutable Wide)
	n = 50
end 'widen'

export function bump(slot mutable T, other T) uses T
	widen(slot)
	print("{other}\n")
end 'bump'

// --- file: a/call.maxon
export function callA() returns Wide
	var x = 3 as Wide
	bump(x, other: 5)

	return x
end 'callA'

// --- file: b/b.maxon
export function bump(n mutable Wide, amount Wide)
	n = amount
end 'bump'

// --- file: main.maxon
function main() returns ExitCode
	var y = 4 as Wide
	b.bump(y, amount: 7)
	print("{y} {callA()}\n")
	return 0
end 'main'
```
```stdout
5
7 50
```
```exitcode
0
```

<!-- test: a-generic-and-a-non-generic-function-cannot-share-a-name-in-one-directory -->
One directory declares a function name once, so a generic and a non-generic declaration never share a key.

```maxon
typealias Wide = int(0 to 100)

function bump(n mutable Wide, amount Wide)
	n = amount
end 'bump'

function bump(slot mutable T, other T) uses T
	slot = other
end 'bump'

function main() returns ExitCode
	var x = 3 as Wide
	bump(x, other: 5)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3006: <fragment>:8:10: duplicate definition of 'bump' — already declared as `function bump`
```

<!-- test: a-private-generic-function-of-another-file-is-refused-as-not-exported-before-its-instance-is-judged -->
The instance of `relay` is declared in `app/lib.maxon` and the call names it from `app/main.maxon`, so the call is refused as naming a function that is not exported.

```maxon
// --- file: app/lib.maxon
export typealias Wide = int(0 to 100)

function widen(n mutable Wide)
	n = 50
end 'widen'

function relay(slot mutable T, other T) uses T
	widen(slot)
	print("{other}\n")
end 'relay'

export function inside() returns Wide
	return 3
end 'inside'

// --- file: app/main.maxon
function main() returns ExitCode
	var x = inside()
	relay(x, other: 5)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: app/<fragment>:21:2: function 'relay' is not exported
```

<!-- test: a-private-function-of-another-file-is-refused-as-not-exported-before-its-parameters-are-judged -->
`widen` is not exported, so the call is refused as naming it; the callee's `Wide` formal is not compared with the `Narrow` variable.

```maxon
// --- file: app/lib.maxon
export typealias Wide = int(0 to 100)
export typealias Narrow = int(0 to 10) implements Wide

function widen(n mutable Wide)
	n = 50
end 'widen'

export function inside() returns Narrow
	return 3
end 'inside'

// --- file: app/main.maxon
function main() returns ExitCode
	var x = inside()
	widen(x)
	print("{x}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3008: app/<fragment>:17:2: function 'widen' is not exported
```
