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

<!-- disabled-test: math-intrinsic-int-promotion -->
<!-- P1.0d.4 deferred: `sqrt` — the float REGISTER CLASS lands at P1.0d.4, but sqrt/abs/floor/ceil/round are each their own spec file and their own decision (abs needs a 16-byte andpd sign mask; floor/ceil/round need roundsd, i.e. SSE4.1 and a three-byte 0F 3A escape). Unlocked by whichever rung takes specs/math-functions.md. -->
```maxon
function main() returns ExitCode
	let result = sqrt(16)
	return trunc(result)
end 'main'
```
```exitcode
4
```

<!-- disabled-test: no-string-to-int -->
<!-- P1.2 String — ⚠ and note the reference's own inconsistency before enabling it: this case expects the ALIAS name (`expected 'Integer'`) while `no-bool-to-int` below expects the RESOLVED primitive (`expected 'int'`) for that same `Integer` alias. TypeRules.typeTagName prints the resolved primitive, which is what makes the two enabled cases pass; whoever enables this one has to reconcile the two. -->
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
