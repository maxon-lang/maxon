---
feature: Ranged Typealias
status: implemented
tracking_issue: null
---

## Notes

Ranged typealiases require every use of `int`, `float`, and `byte` in type positions to go through a `typealias` with mandatory range constraints. This creates a stronger type system where every numeric value has a documented domain.

- `bool` is exempt
- Syntax: `typealias Age = int(0 to 150)` or `int(0 upto 150)` (exclusive upper bound)
- Type-qualified `min`/`max` bounds: `typealias FullInt = int(i64.min to i64.max)`
- Type-qualified bounds: `typealias Handle = int(0 to u32.max)`
- Construction: `42 as Age` (compile-time checked for literals, runtime checked for expressions)
- `int / int` produces `int` (truncating), not `float`
- Standard library defines purpose-specific aliases (`Count`, `Index`, `HashValue`, `Codepoint`, `Offset`, `MathValue`, `ExitCode`)

## Docs

### Declaring ranged typealiases

```maxon
typealias Age = int(0 to 150)
typealias Percentage = float(0.0 to 100.0)
typealias Pixel = byte(0 to u8.max)
typealias Temperature = int(-273 to 1000)
```

The `to` keyword makes the upper bound inclusive. The `upto` keyword makes it exclusive.

### Min/max bounds

Use `type.min` and `type.max` for a type's full range:

```maxon
typealias FullInt = int(i64.min to i64.max)
typealias FullFloat = float(f64.min to f64.max)
typealias FullByte = byte(0 to u8.max)
```

### Construction

Cast values into a ranged type with `as TypeName`:

```maxon
typealias Age = int(0 to 150)
var myAge = 25 as Age
```

In most cases the cast is unnecessary — when a literal flows into a slot
that already has a known ranged type (a function parameter, struct field,
or function return), the literal is checked against that target type
without an explicit cast. Use `as TypeName` when the type association
needs to be visible at the use site, or when narrowing a wider value to
a smaller range triggers a runtime check.

### Runtime range checks

When the value is a computed expression, a runtime check is emitted:

```maxon
typealias Year = int(i64.min to i64.max)
typealias Age = int(0 to 150)
function makeAge(n Year) returns Year
	let a = n as Age   // runtime check: panics if n < 0 or n > 150
	return a
end 'makeAge'
```

### Return value range checks

Functions with a ranged return type have their return values checked:

- **Compile time**: returning a literal outside the range is a compile error
- **Runtime**: returning a computed expression emits a range check that panics on violation
- Types whose range covers the full optimal representation (e.g., `ExitCode`) are exempt

```maxon
typealias Score = int(0 to 100)

function half(s Score) returns Score
	return s / 2    // runtime range check on return value
end 'half'

function bad() returns Score
	return 200       // compile error: outside range
end 'bad'
```

### Type-qualified min/max bounds

Use `type.min` and `type.max` to reference bounds of specific numeric types:

```maxon
typealias FileHandle = int(0 to u32.max)
typealias SmallSigned = int(i8.min to i8.max)
typealias Port = int(0 to u16.max)
```

Supported types: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`.

### Range validation

The compiler validates that ranges are representable:

- Lower bound must be less than upper bound
- When both bounds use type qualifiers, they must reference the same type (e.g., `i64.min to i64.max`, not `i8.min to i32.max`)
- A type-qualified bound paired with a literal must form a natural range — `0 to u32.max` is valid, but `0 to i64.max` is an error (use `i64.min to i64.max` or `0 to u64.max` instead)
- Integer ranges cannot span both negative and above `i64.max` (no single 64-bit type can represent this)
- Byte ranges must have bounds within 0 to u8.max


## Tests

### Basic ranged typealias declaration and construction

<!-- test: basic-declaration -->
```maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```exitcode
42
```

### Literal range check at compile time

<!-- test: literal-in-range -->
```maxon
typealias SmallInt = int(0 to 10)

function main() returns ExitCode
	let x = 7 as SmallInt
	return x
end 'main'
```
```exitcode
7
```

### Negative range bounds

<!-- test: negative-range -->
```maxon
typealias Temp = int(-50 to 50)

function main() returns ExitCode
	let t = -10 as Temp
	return t + 60
end 'main'
```
```exitcode
50
```

### Type-qualified min/max keyword bounds

<!-- test: min-max-bounds -->
```maxon
typealias FullInt = int(i64.min to i64.max)

function main() returns ExitCode
	let x = 42 as FullInt
	return x
end 'main'
```
```exitcode
42
```


### Float ranged typealias

<!-- test: float-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

function main() returns ExitCode
	let p = 75.5 as Pct
	return trunc(p)
end 'main'
```
```exitcode
75
```

### Exclusive upper bound with upto

<!-- test: upto-exclusive -->
```maxon
typealias Idx = int(0 upto 10)

function main() returns ExitCode
	let i = 9 as Idx
	return i
end 'main'
```
```exitcode
9
```

### Arithmetic between same-type ranged values

<!-- test: same-type-arithmetic -->
```maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	let a = 30 as Score
	let b = 12 as Score
	return a + b
end 'main'
```
```exitcode
42
```

### Ranged type as function parameter and return

<!-- test: function-param-return -->
```maxon
typealias Score = int(0 to 100)

function double(s Score) returns Score
	return s * 2
end 'double'

function main() returns ExitCode
	return double(21)
end 'main'
```
```exitcode
42
```

### Runtime range check passes

<!-- test: runtime-check-pass -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Age = int(0 to 150)

function makeAge(n Integer) returns Integer
	let a = n as Age
	return a
end 'makeAge'

function main() returns ExitCode
	return makeAge(50)
end 'main'
```
```exitcode
50
```

### Runtime range check fails (panic)

<!-- test: runtime-check-fail -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Age = int(0 to 150)

function makeAge(n Integer) returns Integer
	let a = n as Age
	return a
end 'makeAge'

function main() returns ExitCode
	return makeAge(200)
end 'main'
```
```exitcode
1
```
```stderr
panic at runtime-check-fail.test:6: Range check failed: value outside typealias 'Age'
Stack trace:
  in ranged-typealias.makeAge
  in main
  in mrt_start
```

### Byte ranged typealias

<!-- test: byte-range -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias AsciiCode = byte(0 to 127)

function main() returns ExitCode
	let c = 65 as AsciiCode
	return c as Integer
end 'main'
```
```exitcode
65
```

### Integer division truncates

<!-- test: int-division-truncates -->
```maxon
function main() returns ExitCode
	let a = 7
	let b = 2
	return a / b
end 'main'
```
```exitcode
3
```

### Ranged type in struct field

<!-- test: struct-field -->
```maxon
typealias Score = int(0 to 100)

type Player
	export var name String
	export var score Score

	static function create(name String, score Score) returns Self
		return Self{name: name, score: score}
	end 'create'
end 'Player'

function main() returns ExitCode
	let p = Player.create(name: "Alice", score: 42)
	return p.score
end 'main'
```
```exitcode
42
```

### Return value range check: literal in range

<!-- test: return-literal-in-range -->
```maxon
typealias Score = int(0 to 100)

function getScore() returns Score
	return 42
end 'getScore'

function main() returns ExitCode
	return getScore()
end 'main'
```
```exitcode
42
```

### Return value range check: runtime pass

<!-- test: return-runtime-check-pass -->
```maxon
typealias Score = int(0 to 100)

function half(s Score) returns Score
	return s / 2
end 'half'

function main() returns ExitCode
	return half(84)
end 'main'
```
```exitcode
42
```

### Return value range check: runtime panic

<!-- test: return-runtime-check-fail -->
```maxon
typealias Score = int(0 to 100)

function doubleScore(s Score) returns Score
	return s * 2
end 'doubleScore'

function main() returns ExitCode
	return doubleScore(60)
end 'main'
```
```exitcode
1
```
```stderr
panic at return-runtime-check-fail.test:5: Range check failed: value outside typealias 'Score'
Stack trace:
  in ranged-typealias.doubleScore
  in main
  in mrt_start
```

### Return value range check: float return

<!-- test: return-float-range-check -->
```maxon
typealias Float = float(f64.min to f64.max)
typealias Pct = float(0.0 to 100.0)

function clampedPct(x Float) returns Pct
	return x
end 'clampedPct'

function main() returns ExitCode
	return trunc(clampedPct(42.5))
end 'main'
```
```exitcode
42
```

### Error: return literal out of range

<!-- test: error.return-literal-out-of-range -->
```maxon
typealias SmallInt = int(0 to 10)

function getVal() returns SmallInt
	return 15
end 'getVal'

function main() returns ExitCode
	return getVal()
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.return-literal-out-of-range.test:5:2: Value 15 is outside the range of 'SmallInt' (int(0 to 10))
```

### Error: literal out of range

<!-- test: error.literal-out-of-range -->
```maxon
typealias SmallInt = int(0 to 10)

function main() returns ExitCode
	let x = 15 as SmallInt
	return x
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.literal-out-of-range.test:5:13: Value 15 is outside the range of 'SmallInt' (int(0 to 10))
```

### Error: negative literal out of range

<!-- test: error.negative-out-of-range -->
```maxon
typealias Positive = int(1 to 100)

function main() returns ExitCode
	let x = -5 as Positive
	return x
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.negative-out-of-range.test:5:13: Value -5 is outside the range of 'Positive' (int(1 to 100))
```

### Type-qualified bound: u32.max

<!-- test: type-qualified-u32-max -->
```maxon
typealias Handle = int(0 to u32.max)

function main() returns ExitCode
	let h = 42 as Handle
	return h
end 'main'
```
```exitcode
42
```

### Type-qualified bound: u64.max

A typealias with `int(0 to u64.max)` covers all 64-bit values and should not emit runtime range checks.

<!-- test: type-qualified-u64-max -->
```maxon
typealias BigId = int(0 to u64.max)

function getValue() returns BigId
	return u64.max
end 'getValue'

function main() returns ExitCode
	let v = getValue()
	if v == u64.max 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

### Type-qualified bound: i8 range

<!-- test: type-qualified-i8-range -->
```maxon
typealias SmallSigned = int(i8.min to i8.max)

function main() returns ExitCode
	let s = 100 as SmallSigned
	return s
end 'main'
```
```exitcode
100
```

### Type-qualified bound: u16.max

<!-- test: type-qualified-u16-max -->
```maxon
typealias Port = int(0 to u16.max)

function main() returns ExitCode
	let p = 8080 as Port
	return p
end 'main'
```
```exitcode
8080
```

### u32 range alias

<!-- test: u32-range -->
```maxon
typealias Handle = int(0 to u32.max)

function main() returns ExitCode
	let h = 42 as Handle
	return h
end 'main'
```
```exitcode
42
```

### i8 range alias

<!-- test: i8-range -->
```maxon
typealias SmallInt = int(i8.min to i8.max)

function main() returns ExitCode
	let s = 100 as SmallInt
	return s
end 'main'
```
```exitcode
100
```

### f32 range alias with float operations

<!-- test: f32-range -->
```maxon
typealias SmallFloat = float(f32.min to f32.max)

function main() returns ExitCode
	let x = 3.5 as SmallFloat
	let y = 1.5 as SmallFloat
	return trunc(x + y)
end 'main'
```
```exitcode
5
```

### F32 arithmetic

<!-- test: f32-arithmetic -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 10.0 as F
	let b = 3.0 as F
	let sum = a + b
	let diff = a - b
	let prod = a * b
	let quot = a / b
	return trunc(sum + diff + prod + quot)
end 'main'
```
```exitcode
53
```

### F32 comparison

<!-- test: f32-comparison -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let a = 3.0 as F
	let b = 5.0 as F
	if a < b 'less'
		return 1
	end 'less'
	return 0
end 'main'
```
```exitcode
1
```

### F32 function parameter and return

<!-- test: f32-function-param-return -->
```maxon
typealias F = float(f32.min to f32.max)

function double(x F) returns F
	return x * 2.0
end 'double'

function main() returns ExitCode
	return trunc(double(21.0))
end 'main'
```
```exitcode
42
```

### F32 truncation to int

<!-- test: f32-to-int -->
```maxon
typealias F = float(f32.min to f32.max)

function main() returns ExitCode
	let x = 42.9 as F
	return trunc(x)
end 'main'
```
```exitcode
42
```

### Hex literal in range bound

<!-- test: hex-range-bound -->
```maxon
typealias Handle = int(0 to 0xFFFF)

function main() returns ExitCode
	let h = 255 as Handle
	return h
end 'main'
```
```exitcode
255
```

### Unused local typealias

<!-- test: unused-typealias -->
```maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3062: specs/fragments/ranged-typealias/unused-typealias.test:2:11: unused typealias: 'Score'
```

### Unused typealias with used typealias

<!-- test: unused-typealias-with-used -->
```maxon
typealias Score = int(0 to 100)
typealias Age = int(0 to 150)

function main() returns ExitCode
	let s = 42 as Score
	return s
end 'main'
```
```maxoncstderr
error E3062: specs/fragments/ranged-typealias/unused-typealias-with-used.test:3:11: unused typealias: 'Age'
```

### Error: unrepresentable range

<!-- test: error.unrepresentable-range -->
```maxon
typealias Bad = int(i64.min to u64.max)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.unrepresentable-range.test:2:17: Mismatched type bounds: 'i64.min' and 'u64.max' must reference the same type
```

### Error: byte range below zero

<!-- test: error.byte-range-negative -->
```maxon
typealias BadByte = byte(-1 to u8.max)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.byte-range-negative.test:2:21: Invalid byte range: bounds must be within 0 to u8.max
```

### Error: byte range above 255

<!-- test: error.byte-range-overflow -->
```maxon
typealias BadByte = byte(0 to 256)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.byte-range-overflow.test:2:21: Invalid byte range: bounds must be within 0 to u8.max
```

### Error: mismatched type bounds

<!-- test: error.mismatched-type-bounds -->
```maxon
typealias Bad = int(i8.min to i32.max)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.mismatched-type-bounds.test:2:17: Mismatched type bounds: 'i8.min' and 'i32.max' must reference the same type
```

### Range identifier in variable assignment

<!-- test: range-id-assign -->
```maxon
function main() returns ExitCode
	let x = u16.max
	return x - 65500
end 'main'
```
```exitcode
35
```

### Range identifier in comparison

<!-- test: range-id-comparison -->
```maxon
function main() returns ExitCode
	let x = i32.max
	if x == 2147483647 'isMax'
		return 1
	end 'isMax'
	return 0
end 'main'
```
```exitcode
1
```

### Range identifier i8.min in expression

<!-- test: range-id-i8-min -->
```maxon
function main() returns ExitCode
	let x = i8.min
	return x + 178
end 'main'
```
```exitcode
50
```

### Range identifier in arithmetic

<!-- test: range-id-arithmetic -->
```maxon
function main() returns ExitCode
	let x = u8.max + 1
	return x - 206
end 'main'
```
```exitcode
50
```

### Error: bare sized type shorthand not allowed

<!-- test: error.bare-shorthand -->
```maxon
typealias Integer = i64

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/ranged-typealias/error.bare-shorthand.test:2:21: Bare sized type 'i64' is not allowed. Use explicit range syntax, e.g. 'int(i64.min to i64.max)'
```
