---
feature: type-casting
status: stable
keywords: [cast, as, type, conversion, widening, narrowing]
category: type-system
---

# Type Casting

## Documentation

The `as` keyword performs safe type casting between Maxon's primitive types (`int`, `float`, `byte`, `bool`).

### Safe Casts (Allowed)

Only widening casts that never lose data are permitted:

```text
byte -> int       // 8-bit unsigned to 64-bit signed (always fits)
byte -> float     // 8-bit unsigned to 64-bit double (always fits)
int -> float      // 64-bit signed to 64-bit double (may lose precision for large values)
int literal 0-255 -> byte   // Compile-time range-checked narrowing
same -> same      // No-op (any type to itself)
```

### Syntax

```text
expression as TargetType
```

### Examples

```text
var b = 42 as byte       // int literal in range -> byte (OK)
var i = b as int         // byte -> int widening (OK)
var f = b as float       // byte -> float widening (OK)
var g = 100 as float     // int -> float widening (OK)
```

### Unsafe Casts (Compile Error E3008)

Narrowing casts and lossy conversions are not allowed. The compiler reports error E3008:

```text
var x = 5
var b = x as byte        // ERROR: int variable -> byte (narrowing)
var b = 256 as byte      // ERROR: int literal out of range
var i = 5.0 as int       // ERROR: use trunc/round/floor/ceil instead
var i = 5.0 as byte      // ERROR: float -> byte not allowed
var i = true as int      // ERROR: bool -> int not allowed
var b = true as byte     // ERROR: bool -> byte not allowed
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
function main() returns int
  var b = 42 as byte
  return b as int
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-zero-to-byte -->
```maxon
function main() returns int
  var b = 0 as byte
  return b as int
end 'main'
```
```exitcode
0
```

<!-- test: int-literal-max-to-byte -->
```maxon
function main() returns int
  var b = 255 as byte
  return b as int
end 'main'
```
```exitcode
255
```

<!-- test: byte-to-int -->
```maxon
function main() returns int
  var b = 100 as byte
  return b as int
end 'main'
```
```exitcode
100
```

<!-- test: byte-to-float -->
```maxon
function main() returns int
  var b = 50 as byte
  var f = b as float
  return trunc(f)
end 'main'
```
```exitcode
50
```

<!-- test: int-to-float -->
```maxon
function main() returns int
  var x = 42
  var f = x as float
  return trunc(f)
end 'main'
```
```exitcode
42
```

<!-- test: int-literal-to-float -->
```maxon
function main() returns int
  var f = 99 as float
  return trunc(f)
end 'main'
```
```exitcode
99
```

<!-- test: same-type-int -->
```maxon
function main() returns int
  var x = 42
  return x as int
end 'main'
```
```exitcode
42
```

<!-- test: same-type-float -->
```maxon
function main() returns int
  var f = 42.0
  var g = f as float
  return trunc(g)
end 'main'
```
```exitcode
42
```

<!-- test: same-type-byte -->
```maxon
function main() returns int
  var b = 42 as byte
  var c = b as byte
  return c as int
end 'main'
```
```exitcode
42
```

<!-- test: cast-in-expression -->
```maxon
function main() returns int
  var b = 10 as byte
  var result = b as int + 32
  return result
end 'main'
```
```exitcode
42
```

<!-- test: chained-byte-int-float -->
```maxon
function main() returns int
  var b = 25 as byte
  var i = b as int
  var f = i as float
  return trunc(f)
end 'main'
```
```exitcode
25
```

### Unsafe Casts (Compile Errors)

<!-- test: error.int-var-to-byte -->
```maxon
function main() returns int
  var x = 5
  var b = x as byte
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.int-var-to-byte.test:4:13: Cannot cast from int to byte
```

<!-- test: error.int-literal-out-of-range -->
```maxon
function main() returns int
  var x = 256 as byte
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.int-literal-out-of-range.test:3:15: Cannot cast from int to byte
```

<!-- test: error.negative-literal-to-byte -->
```maxon
function main() returns int
  var x = -1 as byte
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.negative-literal-to-byte.test:3:14: Cannot cast from int to byte
```

<!-- test: error.float-to-int -->
```maxon
function main() returns int
  var x = 5.0 as int
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.float-to-int.test:3:15: Cannot cast from float to int
```

<!-- test: error.float-to-byte -->
```maxon
function main() returns int
  var x = 5.0 as byte
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.float-to-byte.test:3:15: Cannot cast from float to byte
```

<!-- test: error.bool-to-int -->
```maxon
function main() returns int
  var b = true
  var x = b as int
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.bool-to-int.test:4:13: Cannot cast from bool to int
```

<!-- test: error.bool-to-float -->
```maxon
function main() returns int
  var b = true
  var x = b as float
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.bool-to-float.test:4:13: Cannot cast from bool to float
```

<!-- test: error.bool-to-byte -->
```maxon
function main() returns int
  var b = true
  var x = b as byte
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.bool-to-byte.test:4:13: Cannot cast from bool to byte
```

<!-- test: error.int-to-bool -->
```maxon
function main() returns int
  var x = 0 as bool
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.int-to-bool.test:3:13: Cannot cast from int to bool
```

<!-- test: error.float-to-bool -->
```maxon
function main() returns int
  var x = 0.0 as bool
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.float-to-bool.test:3:15: Cannot cast from float to bool
```

<!-- test: error.byte-to-bool -->
```maxon
function main() returns int
  var b = 42 as byte
  var x = b as bool
  return 0
end 'main'
```
```maxoncstderr
error E3008: specs/fragments/type-casting/error.byte-to-bool.test:4:13: Cannot cast from byte to bool
```
