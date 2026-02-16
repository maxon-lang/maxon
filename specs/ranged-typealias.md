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
- Shorthand sized types: `typealias Handle = u32` (sugar for `int(0 to u32.max)`)
- Supported shorthands: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`
- Construction: `Age{42}` (compile-time checked for literals, runtime checked for expressions)
- `int / int` produces `int` (truncating), not `float`
- Standard library defines general-purpose aliases (`Integer = i64`, `Float = f64`, `Byte = u8`) and purpose-specific aliases (`Count`, `Index`, `HashValue`, `Codepoint`, `Offset`, `MathValue`, `ExitCode`)

## Docs

### Declaring ranged typealiases

```maxon
typealias Age = int(0 to 150)
typealias Percentage = float(0.0 to 100.0)
typealias Pixel = byte(0 to 255)
typealias Temperature = int(-273 to 1000)
```

The `to` keyword makes the upper bound inclusive. The `upto` keyword makes it exclusive.

### Min/max bounds

Use `type.min` and `type.max` for a type's full range:

```maxon
typealias Integer = int(i64.min to i64.max)
typealias Float = float(f64.min to f64.max)
typealias Byte = byte(u8.min to u8.max)
```

Or use shorthand aliases for the same effect:

```maxon
typealias Integer = i64
typealias Float = f64
typealias Byte = u8
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
typealias Age = int(0 to 150)
function makeAge(n Integer) returns Integer
  var a = Age{n}   // runtime check: panics if n < 0 or n > 150
  return a
end 'makeAge'
```

### Return value range checks

Functions with a ranged return type have their return values checked:

- **Compile time**: returning a literal outside the range is a compile error
- **Runtime**: returning a computed expression emits a range check that panics on violation
- Types whose range covers the full optimal representation (e.g., `Integer`, `ExitCode`) are exempt

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

### Shorthand sized type aliases

Use sized type names directly as typealias shorthand:

```maxon
typealias FileHandle = u32    // equivalent to int(0 to 4294967295)
typealias SmallSigned = i8    // equivalent to int(-128 to 127)
typealias SmallFloat = f32    // equivalent to float(-3.4028235E+38 to 3.4028235E+38)
```

Supported shorthands: `u8`, `u16`, `u32`, `u64`, `i8`, `i16`, `i32`, `i64`, `f32`, `f64`.

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
```output
ExitCode: 42
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
```output
ExitCode: 7
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
```output
ExitCode: 50
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
```output
ExitCode: 42
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
```output
ExitCode: 75
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
```output
ExitCode: 9
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
```output
ExitCode: 42
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
```output
ExitCode: 42
```

### Runtime range check passes

<!-- test: runtime-check-pass -->
```maxon
typealias Age = int(0 to 150)

function makeAge(n Integer) returns Integer
  var a = Age{n}
  return a
end 'makeAge'

function main() returns ExitCode
  return makeAge(50)
end 'main'
```
```output
ExitCode: 50
```

### Runtime range check fails (panic)

<!-- test: runtime-check-fail -->
```maxon
typealias Age = int(0 to 150)

function makeAge(n Integer) returns Integer
  var a = Age{n}
  return a
end 'makeAge'

function main() returns ExitCode
  return makeAge(200)
end 'main'
```
```output
ExitCode: 1
Stderr: Range check failed
```

### Byte ranged typealias

<!-- test: byte-range -->
```maxon
typealias AsciiCode = byte(0 to 127)

function main() returns ExitCode
  var c = AsciiCode{65}
  return c as Integer
end 'main'
```
```output
ExitCode: 65
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
```output
ExitCode: 3
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
```output
ExitCode: 42
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
```output
ExitCode: 42
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
```output
ExitCode: 42
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
```output
ExitCode: 1
Stderr: Range check failed
```

### Return value range check: float return

<!-- test: return-float-range-check -->
```maxon
typealias Pct = float(0.0 to 100.0)

function clampedPct(x Float) returns Pct
  return x
end 'clampedPct'

function main() returns ExitCode
  return trunc(clampedPct(42.5))
end 'main'
```
```output
ExitCode: 42
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
error E3005: specs/fragments/ranged-typealias/error.return-literal-out-of-range.test:5:3: Value 15 is outside the range of 'SmallInt' (int(0 to 10))
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
error E3005: specs/fragments/ranged-typealias/error.literal-out-of-range.test:5:11: Value 15 is outside the range of 'SmallInt' (int(0 to 10))
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
error E3005: specs/fragments/ranged-typealias/error.negative-out-of-range.test:5:11: Value -5 is outside the range of 'Positive' (int(1 to 100))
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
```output
ExitCode: 42
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
```output
ExitCode: 100
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
```output
ExitCode: 8080
```

### Shorthand u32 alias

<!-- test: shorthand-u32 -->
```maxon
typealias Handle = u32

function main() returns ExitCode
  var h = Handle{42}
  return h
end 'main'
```
```output
ExitCode: 42
```

### Shorthand i8 alias

<!-- test: shorthand-i8 -->
```maxon
typealias SmallInt = i8

function main() returns ExitCode
  var s = SmallInt{100}
  return s
end 'main'
```
```output
ExitCode: 100
```

### Shorthand f32 alias with float operations

<!-- test: shorthand-f32 -->
```maxon
typealias SmallFloat = f32

function main() returns ExitCode
  var x = SmallFloat{3.5}
  var y = SmallFloat{1.5}
  return trunc(x + y)
end 'main'
```
```output
ExitCode: 5
```

### F32 arithmetic

<!-- test: f32-arithmetic -->
```maxon
typealias F = f32

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
```output
ExitCode: 56
```

### F32 comparison

<!-- test: f32-comparison -->
```maxon
typealias F = f32

function main() returns ExitCode
  var a = F{3.0}
  var b = F{5.0}
  if a < b 'less'
    return 1
  end 'less'
  return 0
end 'main'
```
```output
ExitCode: 1
```

### F32 function parameter and return

<!-- test: f32-function-param-return -->
```maxon
typealias F = f32

function double(x F) returns F
  return x * 2.0
end 'double'

function main() returns ExitCode
  var x = F{21.0}
  return trunc(double(x))
end 'main'
```
```output
ExitCode: 42
```

### F32 truncation to int

<!-- test: f32-to-int -->
```maxon
typealias F = f32

function main() returns ExitCode
  var x = F{42.9}
  return trunc(x)
end 'main'
```
```output
ExitCode: 42
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
```output
ExitCode: 255
```
