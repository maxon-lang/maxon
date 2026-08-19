---
feature: implicit-type-conversion
status: stable
keywords: [types, conversion, implicit, coercion, int, float]
category: type-system
---

# Implicit Type Conversion

## Documentation

Maxon implicitly converts a numeric value to the type its context demands — **but only when the
conversion loses nothing.** A conversion that would discard information is never performed behind
your back; the compiler rejects it and asks you to say what you meant.

### Supported Implicit Conversions

| From    | To      | Behavior                                                          |
|---------|---------|-------------------------------------------------------------------|
| `int`   | `float` | Convert integer to floating point — **widening, loses nothing**   |
| `float` | `int`   | **Compile error (E3009)** — lossy; use `trunc`/`round`/`floor`/`ceil` |

### Why `float` -> `int` is an error

A `float` holds a fraction and an `int` does not, so every `float` -> `int` conversion throws part
of the value away. Which part is a *decision* — truncate toward zero, round to nearest, floor,
ceil — and Maxon makes you state it, because the four answers disagree (`trunc(-2.5)` is -2,
`floor(-2.5)` is -3) and no compiler should pick for you silently:

```maxon
function takeInt(x Integer) returns Integer ... end

takeInt(3.7)          // ERROR E3009: lossy, and which rounding did you mean?
takeInt(trunc(3.7))   // OK -- 3, and it says so
```

The other direction is fine and needs no ceremony: every `int` is exactly representable as a
`float`, so `int` -> `float` loses nothing and happens automatically.

### This is the same rule as an explicit cast -- see `type-casting.md`

**There is one conversion rule, and `as` does not change it.** `type-casting.md` rejects the
explicit `5.0 as int` with the same E3009 and the same advice, and blesses the explicit
`100 as float`. The implicit and explicit forms agree exactly, in both directions:

| Conversion       | Explicit (`as`)          | Implicit (argument/return/assignment) |
|------------------|--------------------------|----------------------------------------|
| `int` -> `float` | `100 as float` -- OK     | `takeFloat(100)` -- OK                 |
| `float` -> `int` | `5.0 as int` -- **E3009** | `takeInt(5.0)` -- **E3009**            |

That agreement is the point, and it was once broken: this file used to say `float` -> `int`
truncated implicitly while `type-casting.md` rejected the identical explicit cast — so writing
`as` got you an error telling you to use `trunc`, and writing nothing at all got you the
truncation the error had just forbidden. One rule stated twice, with opposite answers, and the
silent one won whenever an argument crossed a function boundary. Fixed in P1.0d.4; both compilers
now emit E3009 for both spellings.

### Where implicit conversion applies

Everywhere a value meets a **declared type** — the rule is not special to arguments:

- a call argument, against its parameter's type
- a `return` value, against the function's declared return type
- an assignment, against the binding's declared type

The same E3009 is reported at each; a conversion that is refused as an argument is refused as a
`return`.

### Function Arguments

Function arguments are implicitly converted to match parameter types:

```maxon
typealias Score = int(i64.min to i64.max)
typealias Weight = float(f64.min to f64.max)

function takeFloat(x Weight) returns Score
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	return takeFloat(42)
end 'main'
```
```exitcode
42
```

## Tests

<!-- test: int-literal-to-float-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

function takeFloat(x Float) returns Integer
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	return takeFloat(42)
end 'main'
```
```exitcode
42
```

<!-- test: int-var-to-float-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

function takeFloat(x Float) returns Integer
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	let i = 42
	return takeFloat(i)
end 'main'
```
```exitcode
42
```

<!-- test: float-to-int-param-rejected -->
A `float` argument to an `int` parameter is a compile error, not a silent truncation. The fix the
message names is `trunc(f)` -- which `float-to-int-param-explicit-trunc` below then compiles.
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	let f = 3.7
	return takeInt(f)
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-param-rejected.test:11:9: argument 'x': cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-param-explicit-trunc -->
The half that still works, and the reason the rejection above costs nothing: say which rounding you
meant and the same program compiles. `trunc` truncates toward zero, so `3.7` is `3` -- the very
value the implicit form used to produce silently.
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	let f = 3.7
	return takeInt(trunc(f))
end 'main'
```
```exitcode
3
```

<!-- test: float-to-int-return-rejected -->
The rule is not special to arguments: a `float` meeting a declared `int` RETURN type is the same
E3009. A rule drawn only at the call boundary would leave this one silent.
```maxon

typealias Integer = int(i64.min to i64.max)

function narrow() returns Integer
	let f = 3.7
	return f
end 'narrow'

function main() returns ExitCode
	return narrow()
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-return-rejected.test:7:2: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: expression-to-float-param -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

function takeFloat(x Float) returns Integer
	return trunc(x)
end 'takeFloat'

function main() returns ExitCode
	let a = 20
	let b = 22
	return takeFloat(a + b)
end 'main'
```
```exitcode
42
```

<!-- test: math-intrinsic-int-promotion -->
```maxon
function main() returns ExitCode
	let result = sqrt(16)
	return trunc(result)
end 'main'
```
```exitcode
4
```

<!-- test: no-string-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	let s = "hello"
	return takeInt(s)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/implicit-type-conversion/no-string-to-int.test:11:9: argument type mismatch for 'x': expected 'Integer', got 'String'
```

<!-- test: no-bool-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeInt(x Integer) returns Integer
	return x
end 'takeInt'

function main() returns ExitCode
	let b = true
	return takeInt(b)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/implicit-type-conversion/no-bool-to-int.test:11:9: argument type mismatch for 'x': expected 'int', got 'bool'
```

<!-- test: no-int-to-bool -->
```maxon

typealias Integer = int(i64.min to i64.max)

function takeBool(x bool) returns Integer
	if x 'check'
		return 1
	end 'check'
	return 0
end 'takeBool'

function main() returns ExitCode
	let i = 1
	return takeBool(i)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/implicit-type-conversion/no-int-to-bool.test:14:9: argument type mismatch for 'x': expected 'bool', got 'int'
```

<!-- test: int-literal-to-float-return -->
⭐ **THE DOC STATES THE RULE FOR THREE SITES AND EVERY CASE ABOVE EXERCISES ONE.** Each widening test
above goes through a call ARGUMENT; the one return-direction case is the NARROWING half. So the
`return` half of "the rule is not special to arguments" was stated and never run — and it did not
work: the value agreed, nothing converted it, and the raw i64 reached the backend where the f64
return register is. Measured before this case existed: `panic … a register-to-register move from rax
to xmm0 crosses register files`.

Every case below asserts a computed VALUE and not merely that the program compiles, because that is
the shape of the bug — `42` here is `3.0 * 14.0`, which an unconverted `3` cannot produce.
```maxon

typealias Float = float(f64.min to f64.max)

function widen() returns Float
	return 3
end 'widen'

function main() returns ExitCode
	return trunc(widen() * 14.0)
end 'main'
```
```exitcode
42
```

<!-- test: int-expression-to-float-return -->
The returned value need not be a literal: an integer EXPRESSION meets the declared `float` under the
same rule, exactly as `expression-to-float-param` does at the argument site.
```maxon

typealias Float = float(f64.min to f64.max)

function widen(a int, b int) returns Float
	return a + b
end 'widen'

function main() returns ExitCode
	return trunc(widen(20, b: 22) + 0.5)
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float-field-literal -->
A struct LITERAL is a coercion site: the field declares the type and the value has to meet it. This
compiled and RAN before the conversion existed, storing the integer's raw bytes in an f64 slot —
`3` read back as 1.5e-323, so `r.raw() == 3.0` was false and the program returned 7.
```maxon
type Reading
	export var value as float

	export static function make() returns Self
		return Self{value: 3}
	end 'make'

	export function raw() returns float
		return self.value
	end 'raw'
end 'Reading'

function main() returns ExitCode
	let r = Reading.make()
	if r.raw() == 3.0 'exact'
		return trunc(r.raw() * 14.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float-field-write -->
The same field, reached by a WRITE rather than by construction. It is the same rule and it must not
answer differently for the way the source spelled it.
```maxon
type Reading
	export var value as float = 0.0

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Reading'

function main() returns ExitCode
	var r = Reading.make()
	r.value = 3
	if r.value == 3.0 'exact'
		return trunc(r.value * 14.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float-self-field -->
And the third spelling of that one write: the bare field name inside an instance method.
```maxon
type Reading
	export var value as float = 0.0

	export function bump() returns int
		value = 3
		return 0
	end 'bump'

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Reading'

function main() returns ExitCode
	var r = Reading.make()
	let done = r.bump()
	if r.value == 3.0 'exact'
		return trunc(r.value * 14.0) + done
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float-field-default -->
A field DEFAULT is a coercion site too, and the one that holds a parse-time constant rather than a
value: `as float = 3` records the f64 bit pattern of 3.0, not the integer 3.
```maxon
type Reading
	export var value as float = 3

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Reading'

function main() returns ExitCode
	let r = Reading.make()
	if r.value == 3.0 'exact'
		return trunc(r.value * 14.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float-global -->
A top-level `var` keeps the type its initializer gave it, so a later store of an integer widens into
the slot rather than overwriting an f64 with eight bytes of two's complement.
```maxon
var scale = 0.0

function main() returns ExitCode
	scale = 3
	if scale == 3.0 'exact'
		return trunc(scale * 14.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float-local -->
A LOCAL `var` keeps its declared type across a rebind for the same reason. Before the coercion the
binding silently became an int — `scale == 3.0` on the very next line reported "cannot compare int
with float" against a legal program, and the same rebind inside a loop fed an i64 and an f64 into one
header phi (a cross-register-file panic in the x64 emitter).
```maxon
function main() returns ExitCode
	var scale = 0.0
	scale = 3
	if scale == 3.0 'exact'
		return trunc(scale * 14.0)
	end 'exact'
	return 7
end 'main'
```
```exitcode
42
```

<!-- test: float-to-int-field-literal-rejected -->
⭐ **THE OTHER DIRECTION IS STILL REFUSED AT EVERY ONE OF THOSE DOORS**, and that is what makes the
widening above safe to add: a rule that promoted in both directions would be a far worse bug than the
one it replaced. Seven rejections, one message.
```maxon
type Reading
	export var value as int

	export static function make() returns Self
		return Self{value: 3.7}
	end 'make'
end 'Reading'

function main() returns ExitCode
	let r = Reading.make()
	return r.value
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-field-literal-rejected.test:6:15: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-field-write-rejected -->
```maxon
type Reading
	export var value as int = 0

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Reading'

function main() returns ExitCode
	var r = Reading.make()
	r.value = 3.7
	return r.value
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-field-write-rejected.test:12:4: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-self-field-rejected -->
```maxon
type Reading
	export var value as int = 0

	export function bump() returns int
		value = 3.7
		return 0
	end 'bump'

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Reading'

function main() returns ExitCode
	var r = Reading.make()
	return r.bump()
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-self-field-rejected.test:6:3: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-field-default-rejected -->
```maxon
type Reading
	export var value as int = 3.7

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Reading'

function main() returns ExitCode
	let r = Reading.make()
	return r.value
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-field-default-rejected.test:3:13: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-alias-field-default-rejected -->
The field-default door reached through a ranged INT alias. It is the case the widening one is most
easily confused with — the same syntax, the same unresolved NAME at the declaration — and it must
still refuse: `Count` is an `int` alias, so a float default is the lossy direction however the type
was spelled. A field default is exactly where a silent narrowing would hide, because nothing at the
construction site mentions the value at all.
```maxon

typealias Count = int(i64.min to i64.max)

type Bag
	export var n as Count = 2.5

	export static function make() returns Self
		return Self{}
	end 'make'
end 'Bag'

function main() returns ExitCode
	let b = Bag.make()
	return b.n
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-alias-field-default-rejected.test:6:13: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-global-rejected -->
```maxon
var counter = 0

function main() returns ExitCode
	counter = 3.7
	return counter
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-global-rejected.test:5:2: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```

<!-- test: float-to-int-local-rejected -->
```maxon
function main() returns ExitCode
	var counter = 0
	counter = 3.7
	return counter
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-local-rejected.test:4:2: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
```
