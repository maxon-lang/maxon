---
feature: invalid-cast
status: stable
keywords: [cast, as, invalid, incompatible, kind, E3009]
category: type-system
---

# Invalid `as` Casts — Incompatible Kinds

## Documentation

The `as` operator converts a value between compatible types: a range cast between ranged-int
typealiases, the widening `int` -> `float`, an `int` to `ExitCode`. It is **not** a
reinterpretation. A cast between two fundamentally-incompatible kinds — a managed aggregate (a
`String`, a `struct`, a function value) crossing to or from a scalar number — describes no
conversion at all: a `String` is a pointer to a heap record, and there is no number it *is*.

Such a cast is rejected at the cast site with **E3009**, the same code the lossy `5.0 as int`
gets, because it is the same fact: an `as` that names no legal conversion. The two reference
compilers agree byte-for-byte on the diagnostic.

```text
"hi" as Age      // ERROR E3009 — a String is not a number
p as Age         // ERROR E3009 — a struct is not a number
someFn as Age    // ERROR E3009 — a function value is not a number
```

Before this rule, the bootstrap CRASHED on the combination (an internal `E9001`, in
`Parser.ValidateCast`) and shv2 let the cast through as a representational no-op, so the
aggregate's pointer flowed on typed as the numeric target — miscompiling into a range check on a
struct pointer, or surfacing only downstream at the next type-checked position.

## Tests

<!-- test: error.string-to-int -->
```maxon
typealias Age = int(0 to 200)

function main() returns ExitCode
	let s = "hi"
	let bad = s as Age
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/invalid-cast/error.string-to-int.test:6:14: Cannot cast from String to int
```

<!-- test: error.struct-to-int -->
```maxon
typealias Age = int(0 to 200)
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	export static function make() returns Self
		return Self{x: 1}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make()
	let bad = p as Age
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/invalid-cast/error.struct-to-int.test:15:14: Cannot cast from struct to int
```

<!-- test: error.struct-to-string -->
```maxon
typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer

	export static function make() returns Self
		return Self{x: 1}
	end 'make'
end 'Point'

function main() returns ExitCode
	let p = Point.make()
	let bad = p as String
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/invalid-cast/error.struct-to-string.test:14:14: Cannot cast from struct to String
```

<!-- test: error.function-to-int -->
```maxon
typealias Age = int(0 to 200)
typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function main() returns ExitCode
	let f = double
	let bad = f as Age
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/invalid-cast/error.function-to-int.test:11:14: Cannot cast from function to int
```

<!-- test: valid.compatible-casts-still-compile -->
```maxon
typealias Small = int(0 to 100)
typealias Big = int(0 to 1000)
typealias Ratio = float(f64.min to f64.max)

function isPositive(r Ratio) returns bool
	return r > 0.0
end 'isPositive'

function main() returns ExitCode
	let big = 42 as Big
	let small = big as Small
	let r = 42 as Ratio
	if isPositive(r) 'ok'
		return small
	end 'ok'
	return 0
end 'main'
```
```exitcode
42
```
