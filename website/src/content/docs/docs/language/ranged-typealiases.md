---
title: Ranged Type Aliases
description: Named numeric subranges that move domain constraints into the type system.
sidebar:
  order: 3
---

Every use of `int`, `float`, and `byte` in type positions must go through a `typealias` with mandatory range constraints. This creates a stronger type system where every numeric value has a documented domain. `bool` and `cstring` are exempt from this requirement — `bool` is unranged by nature, and `cstring` is a pointer type (a NUL-terminated UTF-8 byte pointer used to interoperate with `__Builtins.*` runtime intrinsics).

**Restriction in `with` clauses:** Bare primitive types (`int`, `float`, `byte`) cannot be used as type arguments in `with` clauses on `typealias` or `type` declarations. You must create a ranged typealias first. `bool`, `String`, and other struct types are not affected.

```maxon
// INVALID — bare primitives in with clauses
typealias IntArray = Array with int          // ERROR
type IntBox implements Container with int    // ERROR

// VALID — use a ranged typealias
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer      // OK
type IntBox implements Container with Integer // OK
```

**Declaration:**

```maxon
typealias Port = int(0 to 65535)
typealias Percentage = float(0.0 to 100.0)
typealias Pixel = int(0 to u8.max)
typealias Temperature = int(-273 to 1000)
```

The `to` keyword makes the upper bound inclusive. The `upto` keyword makes it exclusive:

```maxon
typealias Score = int(0 upto 100)   // 0 to 99
```

**Type-qualified bounds:**

Use `type.min` and `type.max` to reference bounds of specific numeric types:

```maxon
typealias FileHandle = int(0 to u32.max)
typealias SmallSigned = int(i8.min to i8.max)
```

Supported types: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`.

When both bounds use type qualifiers, they must reference the same type (e.g., `i64.min to i64.max`, not `i8.min to i32.max`). A type-qualified bound paired with a literal must form a natural range — `0 to u32.max` is valid, but `0 to i64.max` is an error (use `i64.min to i64.max` or `0 to u64.max` instead). A negative-literal lower paired with `u64.max` upper (e.g., `int(-1 to u64.max)`) is also rejected — no single 64-bit type can represent both ends; use `i64.min to i64.max` or `0 to u64.max`. Byte ranges must have bounds within 0 to u8.max.

**Range identifiers as expressions:**

`type.min` and `type.max` can also be used as expressions anywhere an integer literal is valid — in variable assignments, comparisons, arithmetic, function arguments, etc.:

```maxon
var x = u16.max            // 65535
if value == i32.max 'check'
	// ...
end 'check'
var y = u8.max + 1         // 256
```

**Construction:**

Cast a value into a ranged type with `as`:

```maxon
typealias Port = int(0 to 65535)
var p = 8080 as Port
```

In most cases the cast is unnecessary — when a literal flows into a slot whose type is already a ranged alias (a parameter, a struct field, a function return), the literal is checked against that target type directly. Use `as` when the target type needs to be visible at the use site, or when narrowing a wider value to a smaller range.

**Compile-time range checks:**

Literal values are checked at compile time. This is a compile error:

```maxon
typealias SmallInt = int(0 to 10)
var x = 15 as SmallInt   // error: Value 15 is outside the range of 'SmallInt'
```

**Runtime range checks:**

When the value is a computed expression, a runtime range check is emitted that panics on violation:

```maxon
typealias Port = int(0 to 65535)
typealias RawValue = int(i64.min to i64.max)
function makePort(n RawValue) returns RawValue
	var p = n as Port   // runtime check: panics if n < 0 or n > 65535
	return p
end 'makePort'
```

**Return value range checks:**

Functions with a ranged return type have their return values checked:
- Returning a literal outside the range is a compile error
- Returning a computed expression emits a runtime range check
- Types whose range covers the full representation (e.g., `ExitCode`) are exempt

```maxon
typealias Score = int(0 to 100)

function half(s Score) returns Score
	return s / 2    // runtime range check on return value
end 'half'
```

**Arithmetic:**

Ranged types support standard arithmetic. The result of arithmetic between ranged values is the underlying primitive type:

```maxon
typealias Score = int(0 to 100)
var a = 30 as Score
var b = 12 as Score
var sum = a + b    // result is int
```

All arithmetic on ranged integer types uses 64-bit operations regardless of storage type.

**Storage:**

The compiler automatically selects the smallest natural integer width that can represent the declared range for storage in arrays and global variables. All arithmetic still uses 64-bit operations.

| Range fits in | Storage used |
|---------------|-------------|
| 0 to u8.max | u8 (1 byte) |
| -128 to 127 | i8 (1 byte) |
| 0 to 65535 | u16 (2 bytes) |
| -32768 to 32767 | i16 (2 bytes) |
| 0 to 4294967295 | u32 (4 bytes) |
| -2147483648 to 2147483647 | i32 (4 bytes) |
| anything wider | i64 (8 bytes) |

```maxon
typealias Pixel = int(0 to 65535)        // stored as u16 in arrays and globals
typealias Delta = int(-32768 to 32767)   // stored as i16 in arrays and globals
typealias Percent = int(0 to 100)        // stored as u8 in arrays and globals
```

Local variables always use 64-bit registers regardless of the ranged type's storage class.

**Standard library aliases:**

The standard library exports a small set of cross-cutting aliases that don't belong to any one domain:

| Alias | Definition | Purpose |
|-------|-----------|---------|
| `ExitCode` | platform-dependent | Process exit codes |
| `HashValue` | `u32` | Hash function results |
| `Codepoint` | `int(0 to 1114111)` | Unicode codepoints |

Domain-specific quantities (counts, indices, byte offsets, math values) are declared as typealiases inside the module they belong to — for example `String` exports `ByteCount` and `GraphemeCount`, `Math` exports `Real`, and `Array` keeps `ElementCount`/`ElementIndex` private. Application code should follow the same pattern: declare a typealias that names the *purpose* (e.g. `Tally`, `BytePos`, `Coord`) rather than reaching for a generic `Count`/`Index`.

**Assignment and rebinding:**

Assigning one ranged integer variable to another initially creates an alias — both variables refer to the same underlying value. However, reassigning with arithmetic produces a **new value** and rebinds the variable without affecting the original:

```maxon
typealias Pos = int(0 to i64.max)
var startPos = Pos{10}
var pos = startPos      // pos and startPos initially share the same value

pos = pos + 1           // rebinds pos to a new value (11) -- startPos is unaffected
print("{startPos}")     // 10 -- startPos is unchanged
print("{pos}")          // 11
```

This behavior means that using a ranged integer as a loop cursor is safe — advancing `pos` never mutates `startPos`:

```maxon
function skipSpaces(src ByteArray, startPos Pos) returns Pos
		var pos = startPos          // pos starts at the same value as startPos
		while pos < src.length 'loop'
				if src[pos] != b' ' 'notSpace'
						break 'loop'
				end 'notSpace'
				pos = pos + 1           // advances pos; startPos is unaffected
		end 'loop'
		return pos
end 'skipSpaces'
```

This is in contrast to struct assignment, where field mutations through an alias affect the original. See [Reference-by-Default Assignment](/docs/language/memory-model/#reference-by-default-assignment).

---
