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
typealias Pixel = int(0 to u8.max)
typealias Temperature = int(-273 to 1000)
```

The `to` keyword makes the upper bound inclusive. The `upto` keyword makes it exclusive.

### Min/max bounds

Use `type.min` and `type.max` for a type's full range:

```maxon
typealias FullInt = int(i64.min to i64.max)
typealias FullFloat = float(f64.min to f64.max)
typealias FullByte = int(0 to u8.max)
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

### Float range check: literal out of range

A float alias narrows exactly as an integer one does, and an out-of-range float
LITERAL is the same compile-time E3005 an out-of-range integer literal gets.

<!-- test: error.float-cast-out-of-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

function main() returns ExitCode
	let p = 500.0 as Pct
	return trunc(p)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.float-cast-out-of-range.test:5:16: Value 500 is outside the range of 'Pct' (float(0 to 100))
```

### Float range check: NEGATIVE bounds

A double's BIT PATTERN does not order like its value once a sign bit is
involved — among negatives the integer order is REVERSED. Two positive bounds
would pass under an integer compare, so these two cases (an all-negative range
and one straddling zero) are what actually hold the float comparison honest.

<!-- test: error.float-negative-bound-out-of-range -->
```maxon
typealias Neg = float(-100.0 to -1.0)

function main() returns ExitCode
	let p = -200.0 as Neg
	return trunc(-p)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.float-negative-bound-out-of-range.test:5:17: Value -200 is outside the range of 'Neg' (float(-100 to -1))
```

<!-- test: float-negative-bound-in-range -->
```maxon
typealias Neg = float(-100.0 to -1.0)

function main() returns ExitCode
	let p = -50.0 as Neg
	return trunc(-p)
end 'main'
```
```exitcode
50
```

<!-- test: error.float-straddling-zero-out-of-range -->
```maxon
typealias Unit = float(-1.0 to 1.0)

function main() returns ExitCode
	let u = -2.5 as Unit
	return trunc(-u)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.float-straddling-zero-out-of-range.test:5:15: Value -2.5 is outside the range of 'Unit' (float(-1 to 1))
```

<!-- test: float-straddling-zero-in-range -->
```maxon
typealias Unit = float(-1.0 to 1.0)

function main() returns ExitCode
	let u = -0.5 as Unit
	return trunc(-u * 100.0)
end 'main'
```
```exitcode
50
```

### Float range check: runtime guard on a non-literal

<!-- test: float-runtime-range-panic -->
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
```maxon
typealias Pct = float(0.0 to 100.0)
typealias Wide = float(f64.min to f64.max)

function scale(x Wide) returns Wide
	return x * 2.0
end 'scale'

function main() returns ExitCode
	let big = scale(300.0)
	let p = big as Pct
	return trunc(p)
end 'main'
```
```exitcode
1
```
```stderr
panic at float-runtime-range-panic.test:11: Range check failed: value outside typealias 'Pct'
Stack trace:
  in main
  in mrt_start
```

<!-- test: float-runtime-negative-bound-panic -->
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
```maxon
typealias Neg = float(-100.0 to -1.0)
typealias Wide = float(f64.min to f64.max)

function scale(x Wide) returns Wide
	return x * 2.0
end 'scale'

function main() returns ExitCode
	let v = scale(-200.0)
	let n = v as Neg
	return trunc(-n)
end 'main'
```
```exitcode
1
```
```stderr
panic at float-runtime-negative-bound-panic.test:11: Range check failed: value outside typealias 'Neg'
Stack trace:
  in main
  in mrt_start
```

<!-- test: float-runtime-negative-bound-in-range -->
```maxon
typealias Neg = float(-100.0 to -1.0)
typealias Wide = float(f64.min to f64.max)

function scale(x Wide) returns Wide
	return x * 2.0
end 'scale'

function main() returns ExitCode
	let v = scale(-25.0)
	let n = v as Neg
	return trunc(-n)
end 'main'
```
```exitcode
50
```

### Float range check: f64 narrowed to f32 bounds

The narrowing an `f32`-bounded alias promises, checked at run time — a value an
f64 holds comfortably but an f32 cannot.

<!-- test: float-narrow-f64-to-f32 -->
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
```maxon
typealias Wide = float(f64.min to f64.max)
typealias Narrow = float(f32.min to f32.max)

function narrow(x Wide) returns Narrow
	return x
end 'narrow'

function main() returns ExitCode
	return trunc(narrow(1.0e300))
end 'main'
```
```exitcode
1
```
```stderr
panic at float-narrow-f64-to-f32.test:6: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in narrow
  in main
  in mrt_start
```

### Float range check: a full-range alias is NOT guarded

`float(f64.min to f64.max)` admits every finite double, so no guard is emitted
at all. A guard that compared the f64 BIT PATTERNS as signed integers would
reject this value; the elision is what stops it.

<!-- test: float-full-range-guard-elided -->
```maxon
typealias Pct = float(0.0 to 100.0)
typealias Wide = float(f64.min to f64.max)

function widen(x Pct) returns Wide
	return x * 1.0e299
end 'widen'

function main() returns ExitCode
	let big = widen(10.0)
	if big > 1.0e299 'huge'
		return 42
	end 'huge'
	return 0
end 'main'
```
```exitcode
42
```

### Error: return float literal out of range

<!-- test: error.return-float-literal-out-of-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

function getPct() returns Pct
	return 500.0
end 'getPct'

function main() returns ExitCode
	return trunc(getPct())
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.return-float-literal-out-of-range.test:5:2: Value 500 is outside the range of 'Pct' (float(0 to 100))
```

### Error: top-level let float cast out of range

<!-- test: error.top-level-float-cast-out-of-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

let BAD = 500.0 as Pct

function main() returns ExitCode
	return trunc(BAD)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.top-level-float-cast-out-of-range.test:4:17: Value 500 is outside the range of 'Pct' (float(0 to 100))
```

<!-- test: top-level-float-cast-in-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

let GOOD = 50.0 as Pct

function main() returns ExitCode
	return trunc(GOOD)
end 'main'
```
```exitcode
50
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
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
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
  in makeAge
  in main
  in mrt_start
```

### Byte ranged typealias

<!-- test: byte-range -->
```maxon
typealias AsciiCode = int(0 to 127)

function main() returns ExitCode
	let c = 65 as AsciiCode
	return c
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
	export var name as String
	export var score as Score

	static function create(name String, score Score) returns Self
		return Self{name: name, score: score}
	end 'create'
end 'Player'

function main() returns ExitCode
	let p = Player.create("Alice", score: 42)
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
<!-- targets: x64-windows -->
<!-- x64-windows ONLY — a PANIC-RUNTIME restriction: this case pins the panic MESSAGE and the BACKTRACE, and `mrt_panic` (which prints both) is a hand-assembled Windows-only `.text` runtime chunk (`X64Runtime.maxon`). Everywhere else the range verdict is a bare exit 1 with EMPTY stderr — `lowerRangePanicLinux` says so for x64-linux, `RangePanicExitCode` for arm64 ("arm64 has no panic runtime"), and wasm the same. Measured 2026-07-26: identical empty-stderr failure on arm64-macos AND wasm32-wasi, which is what makes this the RUNTIME's absence rather than either backend's. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when a non-Windows panic runtime lands. -->
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
  in doubleScore
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
	let p = 200 as Port
	return p
end 'main'
```
```exitcode
200
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
<!-- E3062 unused-typealias check -->
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
<!-- P1.9 `as` cast + E3062 -->
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
<!-- E3005 range validation at declaration -->
```maxon
typealias Bad = int(i64.min to u64.max)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.unrepresentable-range.test:2:17: Mismatched type bounds: 'i64.min' and 'u64.max' must reference the same type
```

### Error: negative literal lower with u64.max upper

A negative-literal lower paired with `u64.max` upper cannot be represented in 64 bits — the upper bound exceeds `i64.max`, so no single 64-bit type can hold both ends. Without this check the parser would silently collapse the range to `-1..-1` (because `u64.max` is stored as the signed long `-1`).

<!-- test: error.negative-low-u64-max -->
<!-- E3005 range validation at declaration -->
```maxon
typealias Bad = int(-1 to u64.max)

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.negative-low-u64-max.test:2:17: Integer range cannot span both negative values and above i64.max: 'int(-1 to u64.max)' is not representable in 64 bits; use 'i64.min to i64.max' or '0 to u64.max' instead
```

### Error: mismatched type bounds

<!-- test: error.mismatched-type-bounds -->
<!-- E3005 range validation at declaration -->
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

### Error: otherwise value outside the callee's ranged return type

A `try … otherwise <literal>` default is bound to the callee's return type. When
that return type is a ranged alias, the default must lie inside the range —
otherwise the error path produces a binding that violates its own type's
invariant, which is the one thing a ranged type exists to guarantee. The literal
is therefore rejected at compile time, exactly as an out-of-range literal is at a
cast or a ranged `return`.

<!-- test: error.otherwise-outside-ranged-return -->
```maxon
typealias Score = int(0 to 100)

enum MyError implements Error
	failed
end 'MyError'

function getScore() returns Score throws MyError
	return 50 as Score
end 'getScore'

function main() returns ExitCode
	let v = try getScore() otherwise -1
	return v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.otherwise-outside-ranged-return.test:13:10: otherwise value -1 is outside the range of 'Score' (int(0 to 100))
```

### Error: bare sized type shorthand not allowed

<!-- test: error.bare-shorthand -->
<!-- E2003 bare-sized-type diagnostic (shv2 rejects it as a generic `unsupported`) -->
```maxon
typealias Integer = i64

function main() returns ExitCode
	return 0
end 'main'
```
```maxoncstderr
error E2003: specs/fragments/ranged-typealias/error.bare-shorthand.test:2:21: Bare sized type 'i64' is not allowed. Use explicit range syntax, e.g. 'int(i64.min to i64.max)'
```

### Negative sentinel cast into an unsigned-domain alias

A const-expression `(-1) as Alias` where `Alias = int(0 to u64.max)` is a
deliberate sentinel: the unsigned upper bound is stored as a wrapped negative,
so the cast wraps -1 to the max unsigned value (`u64.max`) rather than being
out of range. The const-eval cast check must compare both bounds in unsigned
space when `Alias`'s lower bound is non-negative, matching the runtime cast and
the value-load range check. Mirrors the compiler's own
`CONSUME_NO_VALUE = (-1) as ValueId` sentinel.

<!-- test: unsigned-domain-negative-sentinel-cast -->
```maxon
typealias Slot = int(0 to u64.max)

let NO_SLOT = (-1) as Slot

function isSentinel(s Slot) returns bool
	return s == NO_SLOT
end 'isSentinel'

function main() returns ExitCode
	if isSentinel(NO_SLOT) 'yes'
		return 7
	end 'yes'
	return 0
end 'main'
```
```exitcode
7
```

<!-- test: cast-to-stdlib-internal-typealias -->
<!-- stdlib whitelist: Array.maxon, which declares ElementCount — see cast-target-type-resolution.md -->
A typealias declared inside the stdlib is reachable as a cast target from any
file, regardless of its source-level visibility modifier. The stdlib's internal
ranged aliases (`ElementCount`, `NodeIndex`, …) appear in the public collection
API, so user code must be able to name them in an `as` cast — `5 as ElementCount`
resolves rather than failing with "Expected type name after 'as'".
```maxon
function main() returns ExitCode
	let n = 5 as ElementCount
	return n as ExitCode
end 'main'
```
```exitcode
5
```

### Unsigned-max upper: runtime cast of a bit-63-set value

An `int(N>0 to u64.max)` range is UNSIGNED — a value with bit 63 set is a huge
unsigned that the range admits, not a negative below the low bound. The runtime
cascade tests `value < 0 → in range` before `value < N → panic`, so a bit-63-set
value passes rather than tripping the naive signed lower check. (The bootstrap's
own LITERAL check agrees the value is in range; only its RUNTIME check keeps the
signed lower bound and panics — a bootstrap inconsistency this does not copy.)

<!-- test: unsigned-max-upper-runtime-pass -->
```maxon
typealias Big = int(5 to u64.max)

function main() returns ExitCode
	let big = 1 shl 63
	let r = big as Big
	if r == 1 shl 63 'ok'
		return 7
	end 'ok'
	return 3
end 'main'
```
```exitcode
7
```

### Unsigned-max upper: negative literal is in range

A negative literal into `int(5 to u64.max)` wraps to a huge unsigned inside the
range, so `-1 as Big` is `u64.max` — in range, no compile error.

<!-- test: unsigned-max-upper-literal-in-range -->
```maxon
typealias Big = int(5 to u64.max)

function main() returns ExitCode
	let x = -1 as Big
	if x == u64.max 'ok'
		return 7
	end 'ok'
	return 3
end 'main'
```
```exitcode
7
```

### Error: unsigned-max upper literal below the low bound

A small non-negative literal below the low bound is still out of range for
`int(5 to u64.max)` — the unsigned upper does not admit `3`.

<!-- test: error.unsigned-max-upper-literal-out-of-range -->
```maxon
typealias Big = int(5 to u64.max)

function main() returns ExitCode
	let x = 3 as Big
	return x as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.unsigned-max-upper-literal-out-of-range.test:5:12: Value 3 is outside the range of 'Big' (int(5 to 18446744073709551615))
```

### Error: top-level let cast out of range

A top-level `let` is a compile-time constant, so an out-of-range cast in its
initializer is caught with the same E3005 a body cast gets — even though a
top-level `let` is not a function and records no runtime-guard site.

<!-- test: error.top-level-cast-out-of-range -->
```maxon
typealias SmallByte = int(0 to 10)

let BAD = 300 as SmallByte

function main() returns ExitCode
	return BAD as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.top-level-cast-out-of-range.test:4:15: Value 300 is outside the range of 'SmallByte' (int(0 to 10))
```

### Top-level let cast in range

An in-range top-level `let` cast compiles and its value is usable.

<!-- test: top-level-cast-in-range -->
```maxon
typealias SmallByte = int(0 to 10)

let GOOD = 7 as SmallByte

function main() returns ExitCode
	return GOOD as ExitCode
end 'main'
```
```exitcode
7
```
