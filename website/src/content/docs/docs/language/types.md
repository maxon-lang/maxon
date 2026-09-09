---
title: Types
description: Maxon's primitive types and the explicit conversions allowed between them.
sidebar:
  order: 2
---

### Type Conversions

**Implicit Conversions**
- `int` → `float` (in mixed arithmetic)
- `character` literal → `int` (in binary operations with an integer operand, coerced to Unicode codepoint value)

**Explicit Conversions** (using `as` operator)

Only safe (widening) casts are allowed. The compiler rejects casts that could lose data.

Cast targets MUST be either a named ranged typealias (for `int`, `float`, or
`byte`) or the bare keyword `bool`. Bare `int`/`float`/`byte` as cast targets
are rejected — every primitive cast must travel through a typealias so the
range-narrowing intent is explicit.

```maxon
typealias Octet = byte(0 to u8.max)
typealias Tally = int(0 to i64.max)
typealias Real = float(f64.min to f64.max)

var b = 42 as Octet        // int literal 0-255 to byte (OK)
var i = b as Tally         // byte to int (OK)
var f = b as Real          // byte to float (OK)
var g = 100 as Real        // int to float (OK)
```

Supported casts:
- `byte` → `int` (widening)
- `byte` → `float` (widening)
- `int` → `float` (widening)
- `int` literal 0-255 → `byte` (compile-time range-checked)

Casts to or from `bool` are not allowed. Narrowing casts (`int` variable → `byte`, `float` → `int`, `float` → `byte`) are not allowed. Attempting an unsupported cast produces error **E3009**.

A cast whose target alias already covers the source alias's full range — for
example `let x Integer = ...; let y = x as Integer` — is rejected with **E3010
"unneeded cast"**. The compiler auto-widens at use sites, so any explicit cast
must actually narrow or refine. Bare-literal sources (`42 as Byte`) are exempt
because the literal has no source alias to compare against.

**Converting floats to integers:**
The `float → int` cast is not supported because it silently truncates. Use explicit functions instead to make your intent clear:
- `trunc(x)` - Truncate toward zero (removes fractional part)
- `round(x)` - Round to nearest integer
- `floor(x)` - Round down to nearest integer
- `ceil(x)` - Round up to nearest integer

Example:
```maxon
var f = 3.7
var i1 = trunc(f)  // 3 (toward zero)
var i2 = round(f)  // 4 (nearest)
var i3 = floor(f)  // 3 (down)
var i4 = ceil(f)   // 4 (up)
```
