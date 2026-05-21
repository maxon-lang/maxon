---
feature: type-casting
status: stable
keywords: [cast, as, type, conversion, widening, narrowing]
category: type-system
---

# Type Casting

## Documentation

The `as` keyword performs safe type casting between Maxon's primitive types (`int`, `float`, `bool`).

### Safe Casts (Allowed)

Only widening casts that never lose data are permitted:

```text
int -> float      // 64-bit signed to 64-bit double (may lose precision for large values)
same -> same      // No-op (any type to itself)
```

Casts between ranged-int typealiases (e.g. `int(0 to u8.max)` to `int(i64.min to i64.max)`) are
always permitted; out-of-range literals are rejected at compile time, and out-of-range expressions
are rejected at runtime by the function-return range check.

### Syntax

```text
expression as TargetType
```

### Examples

```text
typealias Byte = int(0 to u8.max)
var b = 42 as Byte       // int literal in range (OK)
var i = b as int         // ranged int -> int (OK)
var g = 100 as float     // int -> float widening (OK)
```

### Unsafe Casts (Compile Error E3009)

Lossy conversions are not allowed. The compiler reports error E3009:

```text
var i = 5.0 as int       // ERROR: use trunc/round/floor/ceil instead
var i = true as int      // ERROR: bool -> int not allowed
var f = true as float    // ERROR: bool -> float not allowed
var b = 0 as bool        // ERROR: int -> bool not allowed
var b = 0.0 as bool      // ERROR: float -> bool not allowed
```

For float-to-integer conversion, use the explicit conversion functions:
- `trunc(x)` -- truncate toward zero
- `round(x)` -- round to nearest
- `floor(x)` -- round toward negative infinity
- `ceil(x)` -- round toward positive infinity

## Tests

### Safe Casts

<!-- test: int-literal-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	return b
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-zero-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 0 as Byte
	return b
end 'main'
```
```exitcode
0
```

<!-- test: int-literal-max-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 255 as Byte
	return b
end 'main'
```
```exitcode
255
```

<!-- test: byte-to-int -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 100 as Byte
	return b
end 'main'
```
```exitcode
100
```

<!-- test: byte-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)

function toFloat(b Byte) returns Float
	return b + 0.0
end 'toFloat'

function main() returns ExitCode
	let b = 50 as Byte
	let f = toFloat(b)
	return trunc(f)
end 'main'
```
```exitcode
50
```

<!-- test: int-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let x = 42
	let f = x as Float
	return trunc(f)
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let f = 99 as Float
	return trunc(f)
end 'main'
```
```exitcode
99
```

<!-- test: cast-in-expression -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 10 as Byte
	let result = b + 32
	return result
end 'main'
```
```exitcode
42
```

<!-- test: chained-byte-int-float -->
```maxon

typealias Float = float(f64.min to f64.max)
typealias Byte = int(0 to u8.max)

function toFloat(b Byte) returns Float
	return b + 0.0
end 'toFloat'

function main() returns ExitCode
	let b = 25 as Byte
	let f = toFloat(b)
	return trunc(f)
end 'main'
```
```exitcode
25
```

### Unsafe Casts (Compile Errors)

<!-- test: error.int-literal-out-of-range -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let x = 256 as Byte
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-casting/error.int-literal-out-of-range.test:6:14: Value 256 is outside the range of 'Byte' (int(0 to 255))
```

<!-- test: error.negative-literal-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let x = -1 as Byte
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/type-casting/error.negative-literal-to-byte.test:6:13: Value -1 is outside the range of 'Byte' (int(0 to 255))
```

<!-- test: error.float-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let x = 5.0 as Integer
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.float-to-int.test:6:14: Cannot cast from float to int
```

<!-- test: error.bool-to-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function main() returns ExitCode
	let b = true
	let x = b as Integer
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.bool-to-int.test:7:12: Cannot cast from bool to int
```

<!-- test: error.bool-to-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function main() returns ExitCode
	let b = true
	let x = b as Float
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.bool-to-float.test:7:12: Cannot cast from bool to float
```

<!-- test: error.bool-to-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = true
	let x = b as Byte
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.bool-to-byte.test:7:12: Cannot cast from bool to int
```

<!-- test: error.int-to-bool -->
```maxon
function main() returns ExitCode
	let x = 0 as bool
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.int-to-bool.test:3:12: Cannot cast from int to bool
```

<!-- test: error.float-to-bool -->
```maxon
function main() returns ExitCode
	let x = 0.0 as bool
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.float-to-bool.test:3:14: Cannot cast from float to bool
```

<!-- test: error.byte-to-bool -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	let x = b as bool
	return 0
end 'main'
```
```maxoncstderr
error E3009: specs/fragments/type-casting/error.byte-to-bool.test:7:12: Cannot cast from int to bool
```

### Unneeded Casts (Compile Error E3010)

A cast that does not narrow the source range is rejected: the surrounding context
already auto-widens, so the explicit cast contributes nothing.

<!-- test: error.unneeded.same-type-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let x = identity(42)
	let y = x as Integer
	return y
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-type-int.test:11:12: unneeded cast: 'Integer' already fits in 'Integer'
```

<!-- test: error.unneeded.same-type-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function identity(x Float) returns Float
	return x
end 'identity'

function main() returns ExitCode
	let f = identity(42.0)
	let g = f as Float
	return trunc(g)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-type-float.test:11:12: unneeded cast: 'Float' already fits in 'Float'
```

<!-- test: error.unneeded.same-type-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	let c = b as Byte
	return c
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-type-byte.test:7:12: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: error.unneeded.same-alias-byte -->
```maxon

typealias Byte = int(0 to u8.max)

function main() returns ExitCode
	let b = 42 as Byte
	let c = b as Byte
	return c
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-alias-byte.test:7:12: unneeded cast: 'Byte' already fits in 'Byte'
```

<!-- test: error.unneeded.same-alias-int -->
```maxon

typealias Integer = int(i64.min to i64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let x = identity(42)
	let y = x as Integer
	return y
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-alias-int.test:11:12: unneeded cast: 'Integer' already fits in 'Integer'
```

<!-- test: error.unneeded.same-alias-float -->
```maxon

typealias Float = float(f64.min to f64.max)

function identity(x Float) returns Float
	return x
end 'identity'

function main() returns ExitCode
	let f = identity(42.0)
	let g = f as Float
	return trunc(g)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.same-alias-float.test:11:12: unneeded cast: 'Float' already fits in 'Float'
```

<!-- test: error.unneeded.widening-byte-to-integer -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias Byte = int(0 to u8.max)

function asInteger(x Integer) returns Integer
	return x
end 'asInteger'

function main() returns ExitCode
	let b = 42 as Byte
	let i = b as Integer
	return asInteger(i)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.widening-byte-to-integer.test:12:12: unneeded cast: 'Byte' already fits in 'Integer'
```

<!-- test: error.unneeded.widening-int-to-float -->
```maxon

typealias Integer = int(i64.min to i64.max)
typealias JsonFloat = float(f64.min to f64.max)

function identity(x Integer) returns Integer
	return x
end 'identity'

function main() returns ExitCode
	let i = identity(42)
	let f = i as JsonFloat
	return trunc(f)
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.widening-int-to-float.test:12:12: unneeded cast: 'Integer' already fits in 'JsonFloat'
```

<!-- test: error.unneeded.call-result-same-alias -->
```maxon

typealias Score = int(0 to 100)

function getScore() returns Score
	return 42
end 'getScore'

function main() returns ExitCode
	let result = getScore() as Score
	return result
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/type-casting/error.unneeded.call-result-same-alias.test:10:26: unneeded cast: 'Score' already fits in 'Score'
```
