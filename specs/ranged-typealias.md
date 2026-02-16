---
feature: Ranged Typealias
status: implemented
tracking_issue: null
---

## Notes

Ranged typealiases require every use of `int`, `float`, and `byte` in type positions to go through a `typealias` with mandatory range constraints. This creates a stronger type system where every numeric value has a documented domain.

- `bool` is exempt
- Syntax: `typealias Age = int(0 to 150)` or `int(0 upto 150)` (exclusive upper bound)
- `min`/`max` keywords for bounds: `typealias FullInt = int(min to max)`
- Construction: `Age{42}` (compile-time checked for literals, runtime checked for expressions)
- `int / int` produces `int` (truncating), not `float`
- Standard library defines general-purpose aliases (`Integer = int(min to max)`, `Float = float(min to max)`, `Byte = byte(0 to 255)`) and purpose-specific aliases (`Count`, `Index`, `HashValue`, `Codepoint`, `CompareResult`, `Offset`, `MathValue`, `ExitCode`)

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

Use `min` and `max` keywords for the type's full range:

```maxon
typealias Integer = int(min to max)
typealias Float = float(min to max)
typealias Byte = byte(0 to 255)
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

### Min/max keyword bounds

<!-- test: min-max-bounds -->
```maxon
typealias FullInt = int(min to max)

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
