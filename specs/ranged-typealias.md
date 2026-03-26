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
- Construction: `Age{42}` (compile-time checked for literals, runtime checked for expressions)
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

Create values using the `TypeName{value}` syntax:

```maxon
typealias Age = int(0 to 150)
var myAge = Age{25}
```

### Runtime range checks

When the value is a computed expression, a runtime check is emitted:

```maxon
typealias Year = int(i64.min to i64.max)
typealias Age = int(0 to 150)
function makeAge(n Year) returns Year
	var a = Age{n}   // runtime check: panics if n < 0 or n > 150
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
	var s = Score{42}
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
	var x = SmallInt{7}
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
	var t = Temp{-10}
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
	var x = FullInt{42}
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
	var p = Pct{75.5}
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
	var i = Idx{9}
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
	var a = Score{30}
	var b = Score{12}
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
	var s = Score{21}
	return double(s)
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
	var a = Age{n}
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
	var a = Age{n}
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
panic at runtime-check-fail.test:6: Range check failed for type 'Age': value outside int(0 to 150)
Stack trace:
  in ranged-typealias.makeAge
  in main
  in _start
```

### Byte ranged typealias

<!-- test: byte-range -->
```maxon
typealias Integer = int(i64.min to i64.max)
typealias AsciiCode = byte(0 to 127)

function main() returns ExitCode
	var c = AsciiCode{65}
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
	var a = 7
	var b = 2
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
end 'Player'

function main() returns ExitCode
	var p = Player{name: "Alice", score: Score{42}}
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
	var s = Score{84}
	return half(s)
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
	var s = Score{60}
	return doubleScore(s)
end 'main'
```
```exitcode
1
```
```stderr
panic at return-runtime-check-fail.test:5: Range check failed for type 'Score': value outside int(0 to 100)
Stack trace:
  in ranged-typealias.doubleScore
  in main
  in _start
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
	var x = SmallInt{15}
	return x
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.literal-out-of-range.test:5:10: Value 15 is outside the range of 'SmallInt' (int(0 to 10))
```

### Error: negative literal out of range

<!-- test: error.negative-out-of-range -->
```maxon
typealias Positive = int(1 to 100)

function main() returns ExitCode
	var x = Positive{-5}
	return x
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.negative-out-of-range.test:5:10: Value -5 is outside the range of 'Positive' (int(1 to 100))
```

### Type-qualified bound: u32.max

<!-- test: type-qualified-u32-max -->
```maxon
typealias Handle = int(0 to u32.max)

function main() returns ExitCode
	var h = Handle{42}
	return h
end 'main'
```
```exitcode
42
```

### Type-qualified bound: i8 range

<!-- test: type-qualified-i8-range -->
```maxon
typealias SmallSigned = int(i8.min to i8.max)

function main() returns ExitCode
	var s = SmallSigned{100}
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
	var p = Port{8080}
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
	var h = Handle{42}
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
	var s = SmallInt{100}
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
	var x = SmallFloat{3.5}
	var y = SmallFloat{1.5}
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
	var a = F{10.0}
	var b = F{3.0}
	var sum = a + b
	var diff = a - b
	var prod = a * b
	var quot = a / b
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
	var a = F{3.0}
	var b = F{5.0}
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
	var x = F{21.0}
	return trunc(double(x))
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
	var x = F{42.9}
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
	var h = Handle{255}
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
	var s = Score{42}
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
error E3005: specs/fragments/ranged-typealias/error.unrepresentable-range.test:2:17: Invalid range: range -9.223372036854776E+18 to 1.8446744073709552E+19 exceeds any representable integer type
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

### Error: literal with signed type max

<!-- test: error.literal-with-signed-max -->
```maxon
typealias Bad = int(0 to i64.max)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.literal-with-signed-max.test:2:17: Suspicious range: literal lower bound with 'i64.max' — did you mean 'i64.min to i64.max' or '0 to u64.max'?
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
