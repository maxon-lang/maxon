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
explicit `5.0 as Count` (a declared int alias) with the same E3009 and the same advice, and blesses the explicit
`100 as Real` (over a declared `typealias Real = float(...)` — the bare keyword is not a cast target).
The implicit and explicit forms agree exactly, in both directions:

| Conversion       | Explicit (`as`)          | Implicit (argument/return/assignment) |
|------------------|--------------------------|----------------------------------------|
| `int` -> `float` | `100 as Real` -- OK      | `takeFloat(100)` -- OK                 |
| `float` -> `int` | `5.0 as Count` -- **E3009** | `takeInt(5.0)` -- **E3009**          |

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
- a **struct-literal field initializer**, against the field's declared type — `Self{v: 3}` where
  `v` is a float field. This includes a field **default** (`var v as Float = 0`), which is the same
  store written at the declaration instead of at the literal.

The same E3009 is reported at each; a conversion that is refused as an argument is refused as a
`return`.

⚠ **This list was once a closed enumeration of three, and the fourth entry was the one that was
broken.** Argument, `return` and assignment each coerced; a struct-literal field initializer was
never type-checked against its field's declared type *at all*, so an `int` reached the backend
where a `float` was expected and the compiler died `E9001: RegisterManager: float value %0 has no
FP register and no stack home` — an internal error naming no source position, for the widening the
three named contexts perform silently. The rule is *"everywhere a value meets a declared type"*;
the bullets are examples of it, and a context missing from the list is still governed by it.

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

<!-- test: int-literal-to-float-struct-field -->
An `int` literal initializing a `float` field widens, exactly as it does at a parameter. This is the
context the rule's own list used to omit: before it was fixed, this program did not produce a wrong
answer, it crashed the compiler with an internal `E9001` from the register allocator.
```maxon

typealias Float = float(f64.min to f64.max)

type P
	export var v as Float

	static function create() returns Self
		return Self{v: 3}
	end 'create'
end 'P'

function main() returns ExitCode
	let p = P.create()
	return trunc(p.v)
end 'main'
```
```exitcode
3
```

<!-- test: int-var-to-float-struct-field -->
Not special to literals: an `int`-typed *value* initializing a `float` field widens too. The literal
form alone would leave the constant-folded path as the only one tested.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

type P
	export var v as Float

	static function create(n Integer) returns Self
		return Self{v: n}
	end 'create'
end 'P'

function main() returns ExitCode
	let p = P.create(3)
	return trunc(p.v)
end 'main'
```
```exitcode
3
```

<!-- test: int-default-to-float-struct-field -->
A field DEFAULT is the same store, written at the declaration instead of at the literal, and it
carries the same rule. `v` is never mentioned in the `Self{...}` below -- the widening has to happen
on the path that synthesises the omitted field, which is a different door in the compiler from the
two tests above and crashed independently of them.
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)

type P
	export var v as Float = 3
	export var w as Integer

	static function create(w Integer) returns Self
		return Self{w: w}
	end 'create'
end 'P'

function main() returns ExitCode
	let p = P.create(4)
	return trunc(p.v) + p.w
end 'main'
```
```exitcode
7
```

<!-- test: float-to-int-struct-field-rejected -->
The other direction, at the same context: a `float` into an `int` field is the same E3009 with the
same `trunc` advice it gets as an argument, a `return` and an assignment. It too was an `E9001`
crash before -- the missing check cost BOTH halves of the rule, not just the widening one.
```maxon

typealias Integer = int(i64.min to i64.max)

type P
	export var v as Integer

	static function create() returns Self
		return Self{v: 3.7}
	end 'create'
end 'P'

function main() returns ExitCode
	let p = P.create()
	return p.v
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/implicit-type-conversion/float-to-int-struct-field-rejected.test:9:15: cannot implicitly convert 'float' to 'int': the conversion is lossy and must be explicit — use trunc(x) to truncate toward zero (or round/floor/ceil)
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
