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
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY — a PANIC-RUNTIME restriction, and this file's CANONICAL statement of it (every other panic-text case here points back at this one rather than restating it): the case pins the panic MESSAGE and the BACKTRACE, which only a panic runtime prints. **Both x64 lanes have one** — `mrt_panic` in `X64Runtime.maxon`, one hand-assembled chunk over whichever stderr writer and process-exit route the OS uses — so both are pinned. arm64 and wasm have NONE: their range verdict is a bare exit 1 with EMPTY stderr, named `RangePanicExitCode` at `StdToArm64Conversion.lowerRangePanic` and `StdToWasm.emitRangePanic`. Measured 2026-07-26 for arm64-macos and wasm32-wasi; x64-linux was measured silent then too and joined the message side on 2026-07-31 (rung A1j), which is why the exclusion now names the two backends that lack the runtime rather than the one OS that had it. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when an arm64 or wasm panic runtime lands. -->
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
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. -->
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
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. -->
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
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. -->
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
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. -->
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

The same check must fire on a value that overruns the range's **upper** end. This twin is not
redundant with the one above: a first cut of the check reached the literal through a path that only
handled a *negated* literal, so `otherwise -1` was rejected while `otherwise 101` compiled clean and
returned 50. A single-signed case cannot see that.

<!-- test: error.otherwise-above-ranged-return -->
```maxon
typealias Score = int(0 to 100)

enum MyError implements Error
	failed
end 'MyError'

function getScore() returns Score throws MyError
	return 50 as Score
end 'getScore'

function main() returns ExitCode
	let v = try getScore() otherwise 101
	return v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.otherwise-above-ranged-return.test:13:10: otherwise value 101 is outside the range of 'Score' (int(0 to 100))
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

### Every position a value meets a ranged alias

`range-check-panic.md` states the rule: wherever a value reaches a place declared with a ranged
typealias, the bounds are enforced — a compile-time E3005 when the value is known, a runtime check
where the value lands when it is not. The cases below cover the STORAGE positions (a struct field's
declared default, a field store, an array element) and the u64-upper shape that the extra check
sites must not regress.

### Error: field default out of range

A declared default is a literal in a slot the alias governs, so it is refused at compile time. The
diagnostic is anchored on the struct literal that took the default (`Self`), not on the field
declaration — a default may be declared in another file, and a source span is an offset into the
file being parsed.

<!-- test: error.field-default-out-of-range -->
```maxon
typealias Percent = int(0 to 100)

type Box
	export var v as Percent = 500

	static function create() returns Self
		return Self{}
	end 'create'
end 'Box'

function main() returns ExitCode
	let b = Box.create()
	return b.v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.field-default-out-of-range.test:8:10: Value 500 is outside the range of 'Percent' (int(0 to 100))
```

### Error: field store out of range

<!-- test: error.field-store-out-of-range -->
```maxon
typealias Percent = int(0 to 100)

type Box
	export var v as Percent

	static function create() returns Self
		return Self{v: 1}
	end 'create'
end 'Box'

function main() returns ExitCode
	var b = Box.create()
	b.v = 500
	return b.v
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.field-store-out-of-range.test:14:4: Value 500 is outside the range of 'Percent' (int(0 to 100))
```

### Error: array element out of range

An `Array with Percent` element is a storage slot the alias governs exactly as a struct field is,
so `push` of an out-of-range literal is the same compile error.

<!-- test: error.array-push-out-of-range -->
```maxon
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

function main() returns ExitCode
	var a = PA.create()
	a.push(500)
	return a.count()
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.array-push-out-of-range.test:7:4: Value 500 is outside the range of 'Percent' (int(0 to 100))
```

### Field store: runtime panic

The half a literal cannot reach. `grow(5)` is a call result, so nothing folds it — the check lands
where the value does, at the store.

<!-- test: field-store-runtime-panic -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. The CHECK is target-neutral and the compile-time cases beside this one cover it on every target. -->
```maxon
typealias Wide = int(0 to 1000)
typealias Percent = int(0 to 100)

type Box
	export var v as Percent

	static function create() returns Self
		return Self{v: 1}
	end 'create'
end 'Box'

function grow(n Wide) returns Wide
	return n * 101
end 'grow'

function main() returns ExitCode
	var b = Box.create()
	b.v = grow(5)
	return b.v
end 'main'
```
```exitcode
1
```
```stderr
panic at field-store-runtime-panic.test:19: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

### Error: a call argument is checked against the CALLEE's declaration of the alias

Two files each declare `Limit`, over different ranges — which
`crossfile-alias-same-underlying-different-range-still-legal` establishes is legal. A parameter's range
is the one visible where the FUNCTION was written, so `narrow(500)` is refused against `lib.maxon`'s
`int(0 to 10)` even though the file that wrote the call has a `Limit` that would admit it. The
diagnostic still points at the line that wrote the argument: the range comes from one file and the
error belongs to the other, and conflating them reported a caller's line and column against the
callee's path.

<!-- test: error.crossfile-call-argument-uses-callee-range -->
```maxon
// --- file: lib.maxon
typealias Limit = int(0 to 10)

export function narrow(x Limit) returns Limit
	return x
end 'narrow'

// --- file: main.maxon
typealias Limit = int(0 to 1000)

function wide(x Limit) returns Limit
	return x
end 'wide'

function main() returns ExitCode
	let w = wide(500)
	if w == 500 'ok'
		return narrow(500)
	end 'ok'
	return 1
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.crossfile-call-argument-uses-callee-range.test:19:10: Value 500 is outside the range of 'Limit' (int(0 to 10))
```

### Error: float literal call argument out of range

The call-argument door in the FLOAT domain. It is a separate case because the parser's constant view is
integer-only — a float literal is not in `valueConstKnown`, so a float argument is recorded on its tag
and the domain-partitioned const map in `InsertRangeChecks` decides. Without that arm the value reached
the callee and was caught only by its ranged `return`, at run time, where the reference refuses it at
compile time.

<!-- test: error.float-call-argument-out-of-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

function take(p Pct) returns Pct
	return p
end 'take'

function main() returns ExitCode
	return trunc(take(500.0))
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.float-call-argument-out-of-range.test:9:15: Value 500 is outside the range of 'Pct' (float(0 to 100))
```

### Float call argument in range

The control for the case above: an in-range float literal at the same door still compiles and runs.

<!-- test: float-call-argument-in-range -->
```maxon
typealias Pct = float(0.0 to 100.0)

function take(p Pct) returns Pct
	return p
end 'take'

function main() returns ExitCode
	return trunc(take(42.5))
end 'main'
```
```exitcode
42
```

### Unsigned-max upper: a call argument and a return are unguarded

⚠ **THE REGRESSION GUARD FOR `int(0 to u64.max)`.** The upper bound stores as `-1`, so a signed
`value > u64.max` test compares against `-1` and every valid value fails it. v1 shipped exactly that
bug. `rangeIsFull` elides the whole check for this shape — and it has to keep doing so at every
position a check site is recorded, not only at the two that had one when it was written.

<!-- test: unsigned-max-upper-call-argument -->
```maxon
typealias Idx = int(0 to u64.max)

function identity(i Idx) returns Idx
	return i
end 'identity'

function main() returns ExitCode
	return identity(7)
end 'main'
```
```exitcode
7
```

### In range at every position

The control the out-of-range cases above are only meaningful against: one program that puts an
IN-RANGE value at each of the five positions — a declared default (`Self{}` takes `v = 7`), a
struct-literal field (`Self{v: p}`), a field store (`b.v = 42`), an array element (`a.push(3)`) and
a call argument, both as a literal (`Box.make(5)`) and as a computed value (`take(b.v)`). It must
compile and run: `42 + 1 + 5`.

<!-- test: in-range-at-every-position -->
```maxon
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

type Box
	export var v as Percent = 7

	static function create() returns Self
		return Self{}
	end 'create'

	static function make(p Percent) returns Self
		return Self{v: p}
	end 'make'
end 'Box'

function take(p Percent) returns Percent
	return p
end 'take'

function main() returns ExitCode
	var b = Box.create()
	b.v = 42
	var a = PA.create()
	a.push(3)
	let s = Box.make(5)
	return take(b.v) + a.count() + s.v
end 'main'
```
```exitcode
48
```

### Guards in one block run in SOURCE order

Two out-of-range casts, one line apart. The FIRST one is the one that fires, because a guard is
emitted at the end of the chain its block has already grown and not at the end of the ORIGINAL block
— which threaded the cascades in REVERSE and blamed the second cast for a program the first one
already fails. (The compiled fragment carries a `// test:` line ahead of the source, so `let x` is
its line 12 and `let y` its line 13 — the number below names the FIRST of the two.)

<!-- test: guards-run-in-source-order -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. On arm64 and wasm the bare exit 1 cannot tell the two lines apart, so the ORDER this case exists to pin is observable only on x64. -->
```maxon
typealias Small = int(0 to 10)
typealias Wide = int(0 to 100000)

function widen(n Wide) returns Wide
	return n + 1
end 'widen'

function main() returns ExitCode
	let a = widen(500)
	let b = widen(900)
	let x = a as Small
	let y = b as Small
	return x + y
end 'main'
```
```exitcode
1
```
```stderr
panic at guards-run-in-source-order.test:12: Range check failed: value outside typealias 'Small'
Stack trace:
  in main
  in mrt_start
```

### A guard fires AT ITS SITE, so nothing after the site runs

The four cases below pin the one thing an exit code cannot see: WHERE in the body the guard runs. A
guard anchored at the END of its block still exits 1, so an exit-code-only case passes either way —
what separates them is the output the program produced before dying, and the fault that killed it.

The first three each print through `unpinned` stdout or a pinned one, and every line of that output is
a statement that must NOT have run. They are x64 only for `float-runtime-range-panic`'s reason: each
pins the panic MESSAGE, and `mrt_panic` — the hand-assembled `.text` chunk that prints it — is appended
to the two x64 lanes and to neither of the others, where a range verdict is a bare exit 1 with EMPTY
stderr. The fourth is the in-range control and runs on every target.

#### The store's guard runs BEFORE the store

`bad` is 500 and the slot is a `Percent`, so the store must never happen. Anchored at the block end it
did: the program printed, and then read the number 500 back out of a `Percent` field — a value that
slot cannot legally hold, observed by the program that owns it. There is no ```stdout block, which is
itself the assertion (`SpecParser.SpecStdout`): an unpinned stdout asserts the program printed NOTHING.

<!-- test: store-guard-fires-before-the-store -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias Percent = int(0 to 100)
typealias Wide = int(0 to 100000)

type Box
	export var v as Percent = 1

	export static function make() returns Box
		return Self{}
	end 'make'
end 'Box'

function widen(n Wide) returns Wide
	return n + 400
end 'widen'

function main() returns ExitCode
	var b = Box.make()
	let bad = widen(100)
	b.v = bad
	print("stored\n")
	print("v={b.v}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at store-guard-fires-before-the-store.test:20: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

#### The language's own check is not pre-empted by the fault its violation causes

`z` is 0 and `NonZero` starts at 1, so the cast is the violation. Anchored at the block end the
DIVISION ran first and the program died `integer divide by zero` — the same exit code, a different
diagnostic, and the check the language promises never ran at all.

<!-- test: cast-guard-fires-before-the-division -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias NonZero = int(1 to 1000)
typealias Wide = int(0 to 100000)

function pick(n Wide) returns Wide
	return n - 7
end 'pick'

function main() returns ExitCode
	let z = pick(7)
	let d = z as NonZero
	let q = 100 / d
	print("q={q}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at cast-guard-fires-before-the-division.test:11: Range check failed: value outside typealias 'NonZero'
Stack trace:
  in main
  in mrt_start
```

#### Three guards in ONE block, each at its own site

Positions and the guard CHAIN have to compose: the k-th guard splits its block, so the (k+1)-th must
land at its own site measured in the continuation the split just made, not at that continuation's end
and not back in the original head. Only the third cast is out of range, so the first two statements
after a guard must have printed and the third must not — which is the whole claim, and it is invisible
to an exit code.

<!-- test: guards-fire-at-their-own-site-in-source-order -->
<!-- targets: x64-windows, x64-linux -->
```maxon
typealias Small = int(0 to 10)
typealias Wide = int(0 to 100000)

function widen(n Wide) returns Wide
	return n + 1
end 'widen'

function main() returns ExitCode
	let a = widen(1)
	let b = widen(2)
	let c = widen(5000)
	print("start\n")
	let x = a as Small
	print("after-x\n")
	let y = b as Small
	print("after-y\n")
	let z = c as Small
	print("after-z\n")
	return x + y + z
end 'main'
```
```exitcode
1
```
```stdout
start
after-x
after-y
```
```stderr
panic at guards-fire-at-their-own-site-in-source-order.test:18: Range check failed: value outside typealias 'Small'
Stack trace:
  in main
  in mrt_start
```

#### The in-range control through every guarded position

The same five shapes the three cases above violate — a field DEFAULT, a field STORE, a struct-LITERAL
field, a cast feeding a division, and a plain cast — with every value inside its range. Moving a guard
to its site must not make an admissible value fire one, and every line of output must still appear, in
order, with a clean exit.

<!-- test: in-range-through-every-guarded-position -->
```maxon
typealias Percent = int(0 to 100)
typealias NonZero = int(1 to 1000)
typealias Wide = int(0 to 100000)

type Box
	export var v as Percent = 7

	export static function make() returns Box
		return Self{}
	end 'make'

	export static function holding(n Percent) returns Box
		return Self{v: n}
	end 'holding'
end 'Box'

function widen(n Wide) returns Wide
	return n + 1
end 'widen'

function main() returns ExitCode
	var b = Box.make()
	print("default={b.v}\n")
	let a = widen(9)
	b.v = a
	print("stored={b.v}\n")
	let c = Box.holding(a)
	print("literal={c.v}\n")
	let d = widen(3) as NonZero
	print("q={100 / d}\n")
	let e = widen(41) as Percent
	print("cast={e}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
default=7
stored=10
literal=10
q=25
cast=42
```

### Many guarded casts of SIMULTANEOUSLY LIVE values

24 distinct values, each guarded and each still live afterwards (the sum reads every one), so all 24
are in registers together across all 24 guards. Every guard's `cmp` must be the last op before the
branch that reads it — otherwise it materializes through `setcc` and holds a register across the rest
of the chain, and 24 compiler-introduced values overflow the 14-GPR file. That overflowed at 16 and
tripped the register allocator's RULE 3 backstop, on a program with nothing wrong with it. 24 is
chosen for headroom above that 16.

`widen(i)` returns `i + 1`, so the sum is 1 + 2 + ... + 24 = 300.

<!-- test: many-live-guarded-casts -->
```maxon
typealias Small = int(0 to 1000)
typealias Wide = int(0 to 100000)

function widen(n Wide) returns Wide
	return n + 1
end 'widen'

function main() returns ExitCode
	var acc = 0
	let w0 = widen(0)
	let w1 = widen(1)
	let w2 = widen(2)
	let w3 = widen(3)
	let w4 = widen(4)
	let w5 = widen(5)
	let w6 = widen(6)
	let w7 = widen(7)
	let w8 = widen(8)
	let w9 = widen(9)
	let w10 = widen(10)
	let w11 = widen(11)
	let w12 = widen(12)
	let w13 = widen(13)
	let w14 = widen(14)
	let w15 = widen(15)
	let w16 = widen(16)
	let w17 = widen(17)
	let w18 = widen(18)
	let w19 = widen(19)
	let w20 = widen(20)
	let w21 = widen(21)
	let w22 = widen(22)
	let w23 = widen(23)
	let s0 = w0 as Small
	let s1 = w1 as Small
	let s2 = w2 as Small
	let s3 = w3 as Small
	let s4 = w4 as Small
	let s5 = w5 as Small
	let s6 = w6 as Small
	let s7 = w7 as Small
	let s8 = w8 as Small
	let s9 = w9 as Small
	let s10 = w10 as Small
	let s11 = w11 as Small
	let s12 = w12 as Small
	let s13 = w13 as Small
	let s14 = w14 as Small
	let s15 = w15 as Small
	let s16 = w16 as Small
	let s17 = w17 as Small
	let s18 = w18 as Small
	let s19 = w19 as Small
	let s20 = w20 as Small
	let s21 = w21 as Small
	let s22 = w22 as Small
	let s23 = w23 as Small
	acc = acc + s0
	acc = acc + s1
	acc = acc + s2
	acc = acc + s3
	acc = acc + s4
	acc = acc + s5
	acc = acc + s6
	acc = acc + s7
	acc = acc + s8
	acc = acc + s9
	acc = acc + s10
	acc = acc + s11
	acc = acc + s12
	acc = acc + s13
	acc = acc + s14
	acc = acc + s15
	acc = acc + s16
	acc = acc + s17
	acc = acc + s18
	acc = acc + s19
	acc = acc + s20
	acc = acc + s21
	acc = acc + s22
	acc = acc + s23
	if acc == 300 'summed'
		return 24
	end 'summed'
	return 1
end 'main'
```
```exitcode
24
```

### Many guarded field stores of SIMULTANEOUSLY LIVE values

The same pressure through the FIELD STORE door rather than the `as` cast: 24 distinct call results,
all live until their stores. Both doors reach the same guard emitter, and both must stay clear of the
register file.

<!-- test: many-live-guarded-field-stores -->
```maxon
typealias Small = int(0 to 1000)
typealias Wide = int(0 to 100000)

type Box
	export var f0 as Small = 1
	export var f1 as Small = 1
	export var f2 as Small = 1
	export var f3 as Small = 1
	export var f4 as Small = 1
	export var f5 as Small = 1
	export var f6 as Small = 1
	export var f7 as Small = 1
	export var f8 as Small = 1
	export var f9 as Small = 1
	export var f10 as Small = 1
	export var f11 as Small = 1
	export var f12 as Small = 1
	export var f13 as Small = 1
	export var f14 as Small = 1
	export var f15 as Small = 1
	export var f16 as Small = 1
	export var f17 as Small = 1
	export var f18 as Small = 1
	export var f19 as Small = 1
	export var f20 as Small = 1
	export var f21 as Small = 1
	export var f22 as Small = 1
	export var f23 as Small = 1

	export static function make() returns Box
		return Self{}
	end 'make'
end 'Box'

function widen(n Wide) returns Wide
	return n + 1
end 'widen'

function main() returns ExitCode
	var b = Box.make()
	let v0 = widen(0)
	let v1 = widen(1)
	let v2 = widen(2)
	let v3 = widen(3)
	let v4 = widen(4)
	let v5 = widen(5)
	let v6 = widen(6)
	let v7 = widen(7)
	let v8 = widen(8)
	let v9 = widen(9)
	let v10 = widen(10)
	let v11 = widen(11)
	let v12 = widen(12)
	let v13 = widen(13)
	let v14 = widen(14)
	let v15 = widen(15)
	let v16 = widen(16)
	let v17 = widen(17)
	let v18 = widen(18)
	let v19 = widen(19)
	let v20 = widen(20)
	let v21 = widen(21)
	let v22 = widen(22)
	let v23 = widen(23)
	b.f0 = v0
	b.f1 = v1
	b.f2 = v2
	b.f3 = v3
	b.f4 = v4
	b.f5 = v5
	b.f6 = v6
	b.f7 = v7
	b.f8 = v8
	b.f9 = v9
	b.f10 = v10
	b.f11 = v11
	b.f12 = v12
	b.f13 = v13
	b.f14 = v14
	b.f15 = v15
	b.f16 = v16
	b.f17 = v17
	b.f18 = v18
	b.f19 = v19
	b.f20 = v20
	b.f21 = v21
	b.f22 = v22
	b.f23 = v23
	return b.f23
end 'main'
```
```exitcode
24
```

### A closure body's site belongs to the CLOSURE, not to the function that wrote it

A closure is lifted into its own function with its own dense SSA numbering and its own blocks. A range
check recorded inside a closure body therefore names a ValueId and a BlockId in the CLOSURE's space, and
the guard has to be emitted there — resolving it against the ENCLOSING function makes those numbers name
unrelated values.

Both directions were live before the P1.9 review, and each is a wrong answer on its own:

- **A correct program was killed.** Here every value is in range — `605 - 600 = 5` — and the enclosing
  `j` is 600. The site leaked into `main`, the guard read `main`'s `j` through the id the closure had
  numbered, and the program panicked against `Small`. `maxon-sharp` runs it to completion.
- **An out-of-range closure cast went unchecked**, because the guard that should have been in the
  closure was somewhere else entirely.

<!-- test: closure-body-site-guards-the-closures-own-value -->
```maxon
typealias Small = int(0 to 10)
typealias Wide = int(0 to 100000)
typealias Fn1 = function(Wide) returns Wide

function apply(f Fn1, x Wide) returns Wide
	return f(x)
end 'apply'

function pad(a Wide, b Wide, c Wide) returns Wide
	return a + b + c
end 'pad'

function main() returns ExitCode
	let j = pad(100, b: 200, c: 300)
	return apply(function(n Wide) gives (n - j) as Small, x: 605)
end 'main'
```
```exitcode
5
```

<!-- test: closure-body-out-of-range-cast-panics-in-the-closure -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. The FRAME NAME is the whole point of this case, and arm64/wasm's bare exit 1 carries none; the check itself is target-neutral and the in-range case above covers it everywhere. -->
The frame the trace names is the load-bearing part: the guard runs inside the lifted closure, so
`main$closure_0` is on the stack when it fires. `maxon-sharp` names its own spelling of the same frame.
```maxon
typealias Small = int(0 to 10)
typealias Wide = int(0 to 100000)
typealias Fn1 = function(Wide) returns Wide

function apply(f Fn1, x Wide) returns Wide
	return f(x)
end 'apply'

function main() returns ExitCode
	return apply(function(n Wide) gives (n + 1) as Small, x: 605)
end 'main'
```
```exitcode
1
```
```stderr
panic at closure-body-out-of-range-cast-panics-in-the-closure.test:11: Range check failed: value outside typealias 'Small'
Stack trace:
  in main$closure_0
  in apply
  in main
  in mrt_start
```

### Many guarded `return` sites in ONE function

The `return` twin of *Three guards in ONE block, each at its own site*, and the axis every case above
holds at ONE: a ranged return type is declared once and a body may `return` through it from as many
places as it has branches, so N sites in one function each owe a guard in front of THEIR OWN `ret`.

⭐ **The two out-of-range cases below are what an exit code cannot see.** A guard attached to the wrong
`ret` still exits 1 — it panics on a path the site is not on, or checks a value that block does not
return, and leaves the site it was for UNGUARDED. What separates those from a correct emission is WHICH
LINE the panic names and WHICH values the program had already produced, so each pins both.

#### Six guarded `return` sites, every value in range

The control, and the only one of the four that runs on every target: six sites, six distinct unfoldable
values, each selected in turn. `pick(n)` returns `n + 1` for n = 0, `n + 2` for n = 1, and so on through
`n + 6` for anything past 4, so the sum is 1 + 3 + 5 + 7 + 9 + 15 = 40. Six admissible values must
produce six clean returns: an index that paired a site with another site's `ret` would check one of
these against a range it does not satisfy and panic on a program with nothing wrong with it.

<!-- test: many-guarded-return-sites-in-range -->
```maxon
typealias Small = int(0 to 100)
typealias Wide = int(0 to 100000)

function pick(n Wide) returns Small
	if n == 0 'a'
		return n + 1
	end 'a'

	if n == 1 'b'
		return n + 2
	end 'b'

	if n == 2 'c'
		return n + 3
	end 'c'

	if n == 3 'd'
		return n + 4
	end 'd'

	if n == 4 'e'
		return n + 5
	end 'e'

	return n + 6
end 'pick'

function main() returns ExitCode
	var acc = 0
	acc = acc + pick(0)
	acc = acc + pick(1)
	acc = acc + pick(2)
	acc = acc + pick(3)
	acc = acc + pick(4)
	acc = acc + pick(9)
	return acc as ExitCode
end 'main'
```
```exitcode
40
```

#### The FOURTH site's guard fires, at the fourth site, after three sites have already returned

Every printed line is a value a guard admitted, and the panic must name the line of the site that
produced the value it refused — the fourth `return`, whose `n * 60` is 540. Pair that site with an
earlier `ret` and one of the three earlier lines never prints; pair an earlier site with this one and
540 is returned unchecked, through a `Small`.

<!-- test: fourth-guarded-return-site-fires-at-its-own-site -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE, and `mrt_panic` is appended to the two x64 lanes and to neither of the others, where a range verdict is a bare exit 1 with EMPTY stderr. The in-range control above covers the mechanism on every target. -->
```maxon
typealias Small = int(0 to 100)
typealias Wide = int(0 to 100000)

function pick(n Wide) returns Small
	if n == 0 'a'
		return n + 1
	end 'a'

	if n == 1 'b'
		return n + 2
	end 'b'

	if n == 2 'c'
		return n + 3
	end 'c'

	return n * 60
end 'pick'

function main() returns ExitCode
	print("a={pick(0)}\n")
	print("b={pick(1)}\n")
	print("c={pick(2)}\n")
	print("d={pick(9)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
a=1
b=3
c=5
```
```stderr
panic at fourth-guarded-return-site-fires-at-its-own-site.test:18: Range check failed: value outside typealias 'Small'
Stack trace:
  in pick
  in main
  in mrt_start
```

#### THE SAME VALUE returned from three places is three sites, and each owes its own guard

⭐⭐ **The case the return-site index exists to get right, and the one a value-keyed lookup gets wrong
in silence.** All three `return n` name the SAME ValueId — `n` is one parameter — so a lookup that
answered "the block returning `n`" would answer with the FIRST one three times: the first `ret` would be
guarded three times over and the other two not at all. Here `n` is 200, the `'mid'` site is the one it
leaves through, and the panic must name THAT line. Unguarded, 200 is printed back out through a `Small`
and the program exits 0.

Together with the next case this pins the whole pairing rather than one end of it: with three sites and
three `ret`s, a permutation that gets both the second and the third right has nothing left to get the
first wrong with.

<!-- test: repeated-return-value-guards-the-site-it-leaves-through -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for the reason given two cases up: it pins the panic MESSAGE. -->
```maxon
typealias Small = int(0 to 100)
typealias Wide = int(0 to 100000)

function pick(n Wide) returns Small
	if n > 1000 'high'
		return n
	end 'high'

	if n > 100 'mid'
		return n
	end 'mid'

	return n
end 'pick'

function main() returns ExitCode
	print("low={pick(7)}\n")
	print("mid={pick(200)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
low=7
```
```stderr
panic at repeated-return-value-guards-the-site-it-leaves-through.test:11: Range check failed: value outside typealias 'Small'
Stack trace:
  in pick
  in main
  in mrt_start
```

#### And the LAST of those three sites, which is what pins the chain's ORDER

The same three same-value sites, refused at the third: `Narrow` starts at 10 and 7 is below it, so the
value leaves through the final `return`. This is the case that fails if the sites' `ret`s are paired in
REVERSE — the guard would carry the first site's line — which is a live risk, because the index is built
by walking the blocks BACK TO FRONT so that each one prepends onto its value's chain.

<!-- test: last-repeated-return-site-fires-with-its-own-line -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for the reason given three cases up: it pins the panic MESSAGE. -->
```maxon
typealias Narrow = int(10 to 100)
typealias Wide = int(0 to 100000)

function pick(n Wide) returns Narrow
	if n > 1000 'high'
		return n
	end 'high'

	if n > 100 'mid'
		return n
	end 'mid'

	return n
end 'pick'

function main() returns ExitCode
	print("first={pick(7)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at last-repeated-return-site-fires-with-its-own-line.test:14: Range check failed: value outside typealias 'Narrow'
Stack trace:
  in pick
  in main
  in mrt_start
```

### A `return` site's guard belongs to ITS OWN `return`, not to the k-th `ret` in block order

⭐⭐ **The axis the four cases above cannot see, because in all four the blocks happen to be registered
in the same order the `return`s are written.** A site records the BLOCK its `ret` went into
(`RangeCheckReturnSite.block`), so the pairing is an identity; pair the sites with the `ret`s by ORDINAL
instead — the first live `ret` for the first site, and so on — and every case above still passes while
the two below report the line of a DIFFERENT `return` than the one the value left through.

Both shapes were MEASURED WRONG against `maxon-sharp` before the block was recorded, and both are
ordinary code rather than corner cases: nothing about a loop or a cast is unusual in a function with a
ranged return type.

#### A `return` inside a LOOP BODY, whose block is registered AFTER the block that follows the loop

A `while` mints its exit block before its body, so the `return` written FIRST lives in the block
registered SECOND. Under an ordinal pairing the loop's guard carries the line of the `return` after the
loop, and the two are on different paths — `pick(7)` proves the after-loop `return` is reachable and
admits its value, and `pick(400)` must then name the LOOP's line, which is the only line the value it
refused ever passed through.

<!-- test: return-in-a-loop-body-guards-its-own-line -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for `float-runtime-range-panic`'s reason: this case pins the panic MESSAGE, and `mrt_panic` is appended to the two x64 lanes and to neither of the others, where a range verdict is a bare exit 1 with EMPTY stderr. -->
```maxon
typealias Small = int(0 to 100)
typealias Wide = int(0 to 100000)

function pick(n Wide) returns Small
	var i = 0
	while i < 3 'spin'
		if n > 100 'big'
			return n
		end 'big'

		i = i + 1
	end 'spin'

	return n
end 'pick'

function main() returns ExitCode
	print("small={pick(7)}\n")
	print("big={pick(400)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
small=7
```
```stderr
panic at return-in-a-loop-body-guards-its-own-line.test:9: Range check failed: value outside typealias 'Small'
Stack trace:
  in pick
  in main
  in mrt_start
```

#### A `return` whose block an `as` CAST IN THE SAME BRANCH has already split

⭐⭐ **The one an ordinal pairing gets wrong even when the source order and the block order agree.** The
cast owes its own guard, that guard SPLITS the branch's block, and the split hands the `ret` to an
`__rc_ok` block appended at the END of `blockRefs` — so this `return`'s `ret`, written second of three,
becomes the LAST one in block order. The printed `c=5` is what proves the cast's own guard ran and
admitted its value, so the panic that follows can only be the `return` on the next line.

<!-- test: return-guarded-behind-a-cast-in-the-same-branch -->
<!-- targets: x64-windows, x64-linux -->
<!-- x64 ONLY, for the reason given one case up: it pins the panic MESSAGE. -->
```maxon
typealias Small = int(0 to 100)
typealias Wide = int(0 to 100000)

function pick(n Wide, m Wide) returns Small
	if n > 1000 'high'
		return n
	end 'high'

	if n > 100 'mid'
		let c = m as Small
		print("c={c}\n")
		return n
	end 'mid'

	return n
end 'pick'

function main() returns ExitCode
	print("low={pick(7, m: 5)}\n")
	print("mid={pick(200, m: 5)}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
low=7
c=5
```
```stderr
panic at return-guarded-behind-a-cast-in-the-same-branch.test:13: Range check failed: value outside typealias 'Small'
Stack trace:
  in pick
  in main
  in mrt_start
```
