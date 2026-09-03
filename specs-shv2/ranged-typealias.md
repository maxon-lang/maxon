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
- A check is elided only where the value's declared range provably fits the destination's
  (`range-check-panic.md`), which a type whose range covers its full representation satisfies against
  everything. ⚠ **`ExitCode` is NOT such a type in general** — its range is the compile TARGET's,
  `int(0 to u32.max)` on Windows but `int(0 to 255)` on Linux, macOS and WASI (`stdlib/Process.maxon`),
  and on those three it is far narrower than the `u32` it rides in. A computed `returns ExitCode` is
  checked there like any other narrow alias.

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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
<!-- x64 ONLY — a PANIC-RUNTIME restriction, and this file's CANONICAL statement of it (every other panic-text case here points back at this one rather than restating it): the case pins the panic MESSAGE and the BACKTRACE, which only a panic runtime prints. **Both x64 lanes have one** — `mrt_panic` in `X64Runtime.maxon`, one hand-assembled chunk over whichever stderr writer and process-exit route the OS uses — so both are pinned. arm64 and wasm have NONE: their range verdict is a bare exit with EMPTY stderr, at `StdToArm64Conversion.lowerRangePanic` and `StdToWasm.emitRangePanic` — both over the one `PanicExitCode` in `Targets/Shared/PanicExitCode.maxon` that x64's `mrt_panic` also exits with (rung A1y; it was three declarations of that number, in three files under two names). Measured 2026-07-26 for arm64-macos and wasm32-wasi; x64-linux was measured silent then too and joined the message side on 2026-07-31 (rung A1j), which is why the exclusion now names the two backends that lack the runtime rather than the one OS that had it. The range CHECK itself is target-neutral and is covered everywhere by the in-range and compile-time-rejection cases beside this one; only the message is gated. Un-gate when an arm64 or wasm panic runtime lands. -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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

### A `/` runs at a width and signedness valid for BOTH operands

⭐ **A RANGED TYPEALIAS ON ONE OPERAND MAY NOT DECIDE THE ARITHMETIC DONE TO THE OTHER.** A ranged
type is what lets the compiler select a narrower or an unsigned machine operation, and for `+`, `-`,
`and`, `or` the answer survives taking it from whichever operand happens to carry one — a "32-bit"
binop is emitted at 64 bits on both backends. `idiv`/`div`/`sdiv`/`udiv` is the one family whose
width and signedness are REAL, so it is the one family that shortcut could reach.

**No ranged type does not mean "no constraint" — it means "the whole of `i64`".** Reading it as
"no constraint" is what let the OTHER operand narrow a 64-bit dividend into a 32-bit divide, and read
a negative one as unsigned.

`stdlib`'s own integer formatter is what found it. It divides a 64-bit pattern by a
`HalfRadix = int(1 to 8)` — a range chosen so the divisor is provably non-zero, see
`specs/safety.md` — and got `0xFFFFFFFF / 8`, so `"{u:x}"` printed `1ffffffff` for the same
`u64.max` that `"{u}"` printed in full.

<!-- test: div-divisor-range-does-not-narrow-the-dividend -->
#### A narrow divisor does not narrow the dividend
```maxon
typealias Integer = int(i64.min to i64.max)
typealias HalfRadix = int(1 to 8)

function ident(v Integer) returns Integer
	return v
end 'ident'

function quotient(bits Integer, halfRadix HalfRadix) returns Integer
	// `bits shr 1` carries no ranged type of its own, which is the whole of how the divisor's
	// `int(1 to 8)` came to supply the division's width.
	let half = bits shr 1
	return half / halfRadix
end 'quotient'

function main() returns ExitCode
	print("{quotient(ident(i64.max), halfRadix: 8)}\n")
	return 0
end 'main'
```
```stdout
576460752303423487
```
```exitcode
0
```

<!-- test: div-divisor-range-does-not-make-the-divide-unsigned -->
#### A non-negative divisor range does not make a NEGATIVE dividend unsigned
`/` truncates toward zero and a remainder takes the DIVIDEND's sign, so `-10 / 3` is `-3` and
`-10 mod 3` is `-1`. Read unsigned, that same dividend is `18446744073709551606`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias HalfRadix = int(1 to 8)

function ident(v Integer) returns Integer
	return v
end 'ident'

function quotient(bits Integer, halfRadix HalfRadix) returns Integer
	let half = bits shr 1
	return half / halfRadix
end 'quotient'

function remainder(bits Integer, halfRadix HalfRadix) returns Integer
	let half = bits shr 1
	return half mod halfRadix
end 'remainder'

function main() returns ExitCode
	let n = ident(-20)
	print("q={quotient(n, halfRadix: 3)} r={remainder(n, halfRadix: 3)}\n")
	return 0
end 'main'
```
```stdout
q=-3 r=-1
```
```exitcode
0
```

<!-- test: div-dividend-range-does-not-make-a-negative-divisor-unsigned -->
#### The same rule read from the other end — a non-negative DIVIDEND range
An operand's range bounds ITS operand and nothing else, so the direction the knowledge travels in
cannot change the answer. Here the dividend is the one with the non-negative range and the divisor
is the one that would be misread.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 255)
typealias NegativeOnly = int(i64.min to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function quotient(a Small, b NegativeOnly) returns Integer
	return a / b
end 'quotient'

function remainder(a Small, b NegativeOnly) returns Integer
	return a mod b
end 'remainder'

function main() returns ExitCode
	let a = ident(100) as Small
	let b = ident(-3) as NegativeOnly
	print("q={quotient(a, b: b)} r={remainder(a, b: b)}\n")
	return 0
end 'main'
```
```stdout
q=-33 r=1
```
```exitcode
0
```

<!-- test: div-both-operands-non-negative-stays-unsigned -->
#### Two non-negative ranges still buy the UNSIGNED divide
Answering the width question by giving up on the signedness one would be the same defect wearing a
fix's clothes. An `int(0 to u64.max)` dividend over a non-negative divisor is an unsigned divide,
and a value with bit 63 set is what says so: read signed, `u64.max / 8` is `0`.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias MachineWord = int(0 to u64.max)
typealias HalfRadix = int(1 to 8)

function ident(v Integer) returns Integer
	return v
end 'ident'

function quotient(w MachineWord, halfRadix HalfRadix) returns MachineWord
	return w / halfRadix
end 'quotient'

function main() returns ExitCode
	let w = ident(-1) as MachineWord
	print("{quotient(w, halfRadix: 8)} {w / 8}\n")
	return 0
end 'main'
```
```stdout
2305843009213693951 2305843009213693951
```
```exitcode
0
```

<!-- test: div-a-cast-to-a-zero-admitting-unsigned-alias-is-still-a-declaration -->
#### A cast to a non-negative alias that ADMITS ZERO still asks for the unsigned divide
⭐⭐ **THE OPERAND THAT CANNOT ITSELF BE MISREAD IS THE ONE THAT MISREADS THE OTHER.** `int(0 to 255)`
cannot reach bit 63, so nothing about *that* operand depends on the reading — but a divide runs at ONE
signedness for BOTH operands, so an `int(0 to 255)` read as "the whole of `i64`" drags the divide
signed and the `int(0 to u64.max)` beside it is then read as `-1`.

That matters here because a cast to such an alias is a representational NO-OP: it moves no bits, so it
mints no value and the operand's type column still says what the SOURCE was. The alias has to be found
some other way — and both spellings must find it, the cast of a CALL RESULT and the cast of a BARE
LOCAL, which are recorded differently because the second leaves one value under two names.
`ExitCode` is the same shape with a BUILTIN's name, and both spellings are exercised against it too, for
that reason. ⚠ Its range is the compile TARGET's — `int(0 to u32.max)` on Windows, `int(0 to 255)` on
Linux, macOS and WASI (`stdlib/Process.maxon`) — and this case is portable across that difference
because what it needs from the alias is the SHAPE and not the bounds: non-negative, admitting zero, and
unable to reach bit 63. Every one of those is true of both ranges, and the value cast through it is `8`.
**MEASURED before the fix: every line below printed `0`.**
```maxon
typealias Integer = int(i64.min to i64.max)
typealias MachineWord = int(0 to u64.max)
typealias Small = int(0 to 255)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let w = ident(-1) as MachineWord

	// The operand is a CALL RESULT — a value no binding shares.
	let s = ident(8) as Small
	print("q={try (w / s) otherwise 999} r={try (w mod s) otherwise 999}\n")

	// The operand is a BARE LOCAL — `b` and `a` would be one value, so the cast has to mint.
	let a = ident(8)
	let b = a as Small
	print("q={try (w / b) otherwise 999} r={try (w mod b) otherwise 999}\n")

	// ⚠ And `a` itself must be UNTOUCHED by that mint: it is still a plain signed `int`.
	print("a={ident(-20) / 3}\n")

	let e = ident(8) as ExitCode
	print("q={try (w / e) otherwise 999}\n")
	let a2 = ident(8)
	let e2 = a2 as ExitCode
	print("q={try (w / e2) otherwise 999}\n")

	return 0
end 'main'
```
```stdout
q=2305843009213693951 r=7
q=2305843009213693951 r=7
a=-6
q=2305843009213693951
q=2305843009213693951
```
```exitcode
0
```

<!-- test: div-narrowed-signed-divide-sign-extends-its-result -->
#### A narrowing that IS licensed still owes a SIGN-EXTENDED result
Two ranges that both fit `i32` genuinely do license the narrower divide, and on x64 that is a real
`idiv r/m32` — an instruction that defines only `EAX`/`EDX`. Every 32-bit write ZERO-extends into its
64-bit register, so a NEGATIVE quotient comes back as `0x00000000FFFFFFF8`: `4294967288`, not `-8`,
for anything that reads the value at 64 bits. A remainder is the same instruction and the same hazard.

⚠ **THE SAME VALUE CAN READ CORRECTLY AT ONE CONSUMER AND WRONGLY AT THE NEXT**, which is what kept
this hidden: an interpolation sign-extends before formatting, so the number PRINTS as `-8` while the
call one line later receives `4294967288`. That is why these cases hand the quotient to a 64-bit
consumer instead of asserting on what it prints.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Imm32 = int(i32.min to i32.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function widen(v Integer) returns Integer
	return v
end 'widen'

function quotient(byteOffset Imm32) returns Integer
	return widen(byteOffset / 8)
end 'quotient'

function remainder(byteOffset Imm32) returns Integer
	return widen(byteOffset mod 8)
end 'remainder'

function main() returns ExitCode
	let offset = ident(-65) as Imm32
	print("q={quotient(offset)} r={remainder(offset)}\n")
	return 0
end 'main'
```
```stdout
q=-8 r=-1
```
```exitcode
0
```

<!-- test: div-narrowed-signed-divide-result-is-in-range-for-its-own-type -->
#### …and `-8` is inside the `int(i32.min to i32.max)` it was divided out of
The zero-extended reading is not, so a consumer that RANGE-CHECKS the quotient rejects a value
plainly inside the range. That is the shape this was first found as — a compiler that printed the
offset as `-8` and panicked on the very next parameter bind.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Imm32 = int(i32.min to i32.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function magnitude(imm Imm32) returns Integer
	if imm >= 0 'nonNeg'
		return imm
	end 'nonNeg'
	return -imm
end 'magnitude'

function main() returns ExitCode
	let offset = ident(-64) as Imm32
	let units = (offset / 8) as Imm32
	print("units={units}\n")
	print("magnitude={magnitude(units)}\n")
	return 0
end 'main'
```
```stdout
units=-8
magnitude=8
```
```exitcode
0
```

### A narrow value SURVIVES A VARIABLE, in both signednesses

⭐ **A LOCAL'S SLOT RECORDS A WIDTH AND NOT A SIGNEDNESS, SO A NARROW VALUE IS WIDENED ON THE WAY
IN.** The rule above chooses the machine operation a `/` runs at; it does not license a narrow PLACE
to keep the answer. A slot that held 32 bits was a slot whose contents were neither reading of
themselves, and it answered wrongly in BOTH directions: the 4-byte store paired with a ZERO-extending
4-byte load (`mov r32, [rbp+d]` on x64, `ldr w` on arm64) lost a negative value's sign, and the load's
result could not say an UNSIGNED value had been stored, so the next consumer that widens sign-extended
a `u32` above `i32.max`.

⚠ **AND IT IS NOT ABOUT DIVISION.** `/` is the one operator whose emitted WIDTH is real, but `+`,
`-`, `*`, `and`, `or` and `xor` between narrow operands produce a narrow VALUE just the same, and it
reached the same slot. The second case below is a subtraction.

⚠ **WHICH CONSUMER READS THE VALUE DECIDES WHETHER THE ANSWER LOOKS RIGHT**, which is what kept
this hidden and is why the two signednesses need opposite cases. A consumer that sign-extends first
— an interpolation does — undoes the zero-extending load and prints a NEGATIVE narrow value
correctly, so the signed cases hand the value to a 64-bit consumer instead. For an UNSIGNED value that
same interpolation is the consumer that gets it wrong, so the third case prints directly.

<!-- test: narrow-local-keeps-a-negative-quotient -->
#### A quotient assigned to a local is still `-8` when the local is read
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Imm32 = int(i32.min to i32.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function widen(v Integer) returns Integer
	return v
end 'widen'

function main() returns ExitCode
	let offset = ident(-65) as Imm32
	let q = offset / 8
	let r = offset mod 8
	if offset < 0 'neg'
		print("q={widen(q)} r={widen(r)}\n")
	end 'neg'
	return 0
end 'main'
```
```stdout
q=-8 r=-1
```
```exitcode
0
```

<!-- test: narrow-local-keeps-a-negative-difference -->
#### …and a SUBTRACTION owes the same, with no narrowed instruction involved at all
`-` is emitted at 64 bits whatever its operands' ranged types say, so the register holding
`offset - 1` already reads `-66`. The slot is what lost it.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Imm32 = int(i32.min to i32.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function widen(v Integer) returns Integer
	return v
end 'widen'

function main() returns ExitCode
	let offset = ident(-65) as Imm32
	let difference = offset - 1
	if offset < 0 'neg'
		print("difference={widen(difference)}\n")
	end 'neg'
	return 0
end 'main'
```
```stdout
difference=-66
```
```exitcode
0
```

<!-- test: narrow-local-keeps-an-unsigned-value-above-i32-max -->
#### The other direction — a value the slot must NOT sign-extend
`int(0 to u32.max)` admits values with bit 31 set. Sign-extending one is as wrong as failing to
sign-extend a negative, and the same slot did both, so an unsigned case has to stand beside a signed
one.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Unsigned32 = int(0 to u32.max)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	let big = ident(4000000000) as Unsigned32
	let difference = big - 1
	if big > 0 'pos'
		print("difference={difference}\n")
	end 'pos'
	return 0
end 'main'
```
```stdout
difference=3999999999
```
```exitcode
0
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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

### ⭐⭐ A DECLARED LOWER BOUND OF 0 REFUSES A WRITTEN NEGATIVE — the upper being `u64.max` does not repeal it

`int(0 to u64.max)` admits every 64-bit PATTERN, so no RUNTIME guard on it can ever fail. It still
declares `≥ 0`, and a literal the source wrote as a negative number is below that bound. The two
questions are different and the compiler used to conflate them: the full-range test short-circuited
the COMPILE-TIME literal check as well as the runtime one, so `take(-1)` compiled clean into such a
parameter and printed **18446744073709551615**, on both compilers.

⚠⚠ **NO TEST OF THE VALUE CAN DECIDE THIS.** `-1`, `u64.max` and `0xFFFFFFFFFFFFFFFE` are the SAME
64 bits; the first denotes a negative number and the other two denote the two largest non-negative
ones. The verdict therefore comes from what the SOURCE WROTE — `Parser.writtenNegativeLiterals`, the
companion of the `unsignedMaxLiterals` mark the ordering rule already reads — and the two acceptances
below are as load-bearing as the refusal: they are what says the check reads the source and not the
bit pattern.

⛔ **THIS SECTION USED TO PIN THE OPPOSITE.** `unsigned-domain-negative-sentinel-cast` asserted that
`(-1) as Slot` "wraps to `u64.max` rather than being out of range" and was a deliberate sentinel
idiom. It was the defect written down as the rule. The honest spelling of that sentinel is `u64.max`,
which is what the source means and what the case below now writes.

<!-- test: error.written-negative-into-unsigned-full-alias -->
```maxon
typealias Slot = int(0 to u64.max)

function take(s Slot) returns Slot
	return s
end 'take'

function main() returns ExitCode
	return take(-1) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.written-negative-into-unsigned-full-alias.test:9:9: Value -1 is outside the range of 'Slot' (int(0 to 18446744073709551615))
```

### The unsigned extreme, written as `u64.max`, is admitted

The half of the rule that stops it being too wide: `u64.max` rides as the same wrapped `-1` pattern
the case above refuses, and it is in range.

<!-- test: unsigned-full-alias-admits-the-unsigned-extreme -->
```maxon
typealias Slot = int(0 to u64.max)

let NO_SLOT = u64.max as Slot

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

### A bit-63-set HEX literal is admitted

The other half. `0xFFFFFFFFFFFFFFFE` is 18446744073709551614 — a non-negative number whose stored
pattern is the negative `-2`. Nothing about it was written with a sign, so it is in range.

<!-- test: unsigned-full-alias-admits-a-bit-63-hex-literal -->
```maxon
typealias Slot = int(0 to u64.max)

function take(s Slot) returns Slot
	return s
end 'take'

function main() returns ExitCode
	if take(0xFFFFFFFFFFFFFFFE) == 0xFFFFFFFFFFFFFFFE 'ok'
		return 7
	end 'ok'
	return 3
end 'main'
```
```exitcode
7
```

### ⛔⛔ The mark is PER FUNCTION, and a synthesized default helper is a function

`writtenNegativeLiterals` is keyed by ValueId, and ValueIds are function-local — so the set must be
cleared at the start of every function body or a mark names a DIFFERENT value in the next one.
`parseFunction` cleared it; **`parseDefaultHelperBody` did not**, and a parameter default's helper is
the one body that cannot be reached by re-entering `parseFunction`: the helpers are drained at
end-of-file (`parseDefaultHelpers`), so what one inherits is the LAST function in the file, marks and
all.

⛔ **MEASURED**: with `let written = -1` present in `main`, the bit-63 hex default two functions above
it was refused — *"`error E3005: Value -9223372036854775808 is outside the range of 'Wide'`"* — because
both literals are ValueId 0 in their own function. **Delete that one line and the identical program
compiled.** The legal default this case pins is the same one
`unsigned-full-alias-admits-a-bit-63-hex-literal` above pins as an argument; what was new is that a
line in an UNRELATED function could take it away.

⚠ The bootstrap has the same two marks and is NOT affected: its `MaxonValue` ids are monotonic across
the whole compile (`IrContext.NextId`), so its sets need no per-function reset and have none to omit.

<!-- test: a-written-negative-in-one-function-does-not-reach-another-functions-default -->
```maxon
typealias Wide = int(0 to u64.max)

function withDefault(p Wide = 0x8000000000000000) returns Wide
	return p
end 'withDefault'

function main() returns ExitCode
	// ⚠ THE SUBJECT OF THIS CASE IS THIS LINE. It must be in the LAST function of the file, because
	// that is the one whose marks a drained default helper inherits.
	let written = -1
	print("default={withDefault()} written={written}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
default=9223372036854775808 written=-1
```

### The refusal is about the LOWER bound, not about the number 0

A partial unsigned range refuses a written negative for the same reason, and its low bound is 5.

<!-- test: error.written-negative-into-a-partial-unsigned-alias -->
```maxon
typealias Big = int(5 to u64.max)

function main() returns ExitCode
	let x = -1 as Big
	return x as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.written-negative-into-a-partial-unsigned-alias.test:5:13: Value -1 is outside the range of 'Big' (int(5 to 18446744073709551615))
```

### Every door owes the same verdict — the RETURN, and a TOP-LEVEL `let`

The compile-time half is one decision asked at every position that names a ranged alias, so a
`return` into an unsigned-full alias refuses a written negative exactly as a call argument does.

<!-- test: error.written-negative-returned-into-unsigned-full-alias -->
```maxon
typealias Slot = int(0 to u64.max)

function make() returns Slot
	return -1
end 'make'

function main() returns ExitCode
	return make() as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.written-negative-returned-into-unsigned-full-alias.test:5:2: Value -1 is outside the range of 'Slot' (int(0 to 18446744073709551615))
```

⛔ **THE TOP-LEVEL `let` DOOR REACHED THE SAME VERDICT BY A DIFFERENT REPAIR, AND IT WAS BROKEN FOR
EVERY RANGE — not just the unsigned ones.** The constant evaluator bound a trailing `as` to the
OPERAND of a unary minus (`-(1 as Narrow)`) where a body binds it to the whole negated literal
(`(-1) as Narrow`), so **`let A = -1 as int(0 to 100)` compiled clean and produced -1** while the
identical cast inside a function was E3005. Both compilers agreed on the wrong answer, so no oracle
could see it. A negated int literal is now ONE literal in the evaluator, exactly as it already was
for a negated FLOAT literal and exactly as it is in a body.

<!-- test: error.written-negative-in-a-top-level-let -->
```maxon
typealias Slot = int(0 to u64.max)

let NO_SLOT = -1 as Slot

function main() returns ExitCode
	let s = NO_SLOT
	return s as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.written-negative-in-a-top-level-let.test:4:18: Value -1 is outside the range of 'Slot' (int(0 to 18446744073709551615))
```

### A NARROW range's top-level `let` gets the same repair

The precedence fix is not about the unsigned shape — it is about where the cast binds. `int(0 to 100)`
refuses a written `-1` in a top-level `let` for the ordinary lower-bound reason.

<!-- test: error.written-negative-in-a-top-level-let-narrow -->
```maxon
typealias Narrow = int(0 to 100)

let LOW = -1 as Narrow

function main() returns ExitCode
	let n = LOW
	return n as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.written-negative-in-a-top-level-let-narrow.test:4:14: Value -1 is outside the range of 'Narrow' (int(0 to 100))
```

### A SIGNED range still takes its negatives

The rule is the declared LOWER bound, and `int(-10 to 10)` declares one that admits `-5`.

<!-- test: signed-range-admits-a-written-negative -->
```maxon
typealias Offset = int(-10 to 10)

function take(o Offset) returns Offset
	return o
end 'take'

function main() returns ExitCode
	if take(-5) == -5 'ok'
		return 7
	end 'ok'
	return 3
end 'main'
```
```exitcode
7
```

<!-- test: cast-to-stdlib-internal-typealias -->
<!-- needs stdlib/Array.maxon, which declares ElementCount — see cast-target-type-resolution.md -->
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

### Unsigned-max upper: the unsigned extreme is in range, and a written negative is not

A bit-63-set value is a huge unsigned that `int(5 to u64.max)` admits — which is what makes the
RUNTIME cascade above sign-plus-lower rather than a signed upper compare. Spelling that value
`u64.max` is in range; spelling it `-1` is not, and the two are the same 64 bits. See the
lower-bound section above for the mark that tells them apart, and
`error.written-negative-into-a-partial-unsigned-alias` for this alias's refusal.

⛔ **THIS CASE PINNED `-1 as Big` AS IN RANGE**, on the argument that it "wraps to a huge unsigned
inside the range". The wrap is real; the claim that the source asked for it was not.

<!-- test: unsigned-max-upper-literal-in-range -->
```maxon
typealias Big = int(5 to u64.max)

function main() returns ExitCode
	let x = u64.max as Big
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

### ⭐⭐ A NEGATIVE UPPER BOUND IS A REAL BOUND — only the UNSIGNED-MAX shape has an unbounded one

⚠⚠ **A `-1` STORED UPPER MEANS `u64.max` IN EXACTLY ONE SHAPE, AND THE RUNTIME CHECK USED TO READ
IT THAT WAY IN ALL OF THEM.** `int(N>=0 to u64.max)` above rides its upper as the signed `-1` and is
genuinely unbounded upwards, so its upper compare is elided on purpose. A **wholly NEGATIVE** range —
`int(-100 to -1)`, `int(i64.min to -2)` — also stores a negative upper, and there the bound is
ordinary: `-1` is the largest value it admits and `0` is out of range. `rangeIsUnsignedMaxUpper` (low
`>= 0` **and** high `== -1`) is what tells the two apart, and the COMPILE-TIME literal check has always
asked it while the RUNTIME check tested only the bound's sign — so the two halves of one rule disagreed
about the same alias: a literal `0 as int(-100 to -1)` was **E3005** while a runtime `0` cast into it
was admitted, and `-1` reached an `int(i64.min to -2)` binding through a plain `as`.

⚠ It matters beyond the binding, because `specs-shv2/safety.md`'s division proof reads a divisor's
DECLARED range: `int(-100 to -1)` excludes `0` (so `/` earns a bare `idiv`) and `int(i64.min to -2)`
excludes `-1` as well (so `mod` earns one too). Both consequences are pinned there.

<!-- test: negative-upper-bound-cast-is-checked -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
#### A runtime cast into a wholly-negative range tests its upper bound
`0` is above `-1`, so the cast is the violation and the guard must fire at the cast's own line — as it
does for the positive `int(0 to 150)` in `runtime-check-fail` above. Before the fix `a` simply became
`0`, a value its declared type does not admit.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOnly = int(-100 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function main() returns ExitCode
	print("start\n")
	let a = ident(0) as NegativeOnly
	print("a={a}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stdout
start
```
```stderr
panic at negative-upper-bound-cast-is-checked.test:11: Range check failed: value outside typealias 'NegativeOnly'
Stack trace:
  in main
  in mrt_start
```

<!-- test: negative-upper-bound-return-is-checked -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
#### The ranged-RETURN door tests it too, and `low == i64.min` is what left it with no check at all
`int(i64.min to -2)` needs NO lower compare (`i64.min` cannot be violated), so eliding the upper as well
left the range with an EMPTY check list — the one shape where the bug was total rather than partial.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BelowMinusOne = int(i64.min to -2)

function ident(v Integer) returns Integer
	return v
end 'ident'

function narrow() returns BelowMinusOne
	return ident(-1)
end 'narrow'

function main() returns ExitCode
	print("v={narrow()}\n")
	return 0
end 'main'
```
```exitcode
1
```
```stderr
panic at negative-upper-bound-return-is-checked.test:10: Range check failed: value outside typealias 'BelowMinusOne'
Stack trace:
  in narrow
  in main
  in mrt_start
```

<!-- test: negative-upper-bound-in-range-control -->
#### The in-range control — the new check must not fire on a value the range admits
Both bounds of a wholly-negative range, exercised through the cast and the return door with admissible
values. A guard added to the shape must cost nothing where the value is legal.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOnly = int(-100 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function pick() returns NegativeOnly
	return ident(-100)
end 'pick'

function main() returns ExitCode
	let a = ident(-1) as NegativeOnly
	let b = ident(-50) as NegativeOnly
	print("a={a} b={b} c={pick()}\n")
	return 0
end 'main'
```
```stdout
a=-1 b=-50 c=-100
```
```exitcode
0
```

<!-- test: error.negative-bound-renders-signed -->
#### The message names the bound the author WROTE, on both bounds and on the value
The `-1`-means-`u64.max` reinterpretation is the same one-shape fact, and E3005's renderer applied it to
any `-1` it was handed. `int(-1 to -1)` therefore reported itself as
`int(18446744073709551615 to 18446744073709551615)` — a sentence about a declaration nobody wrote — and a
`-1` VALUE out of range reported `Value 18446744073709551615`, contradicting the signed bounds printed
in the same line.
```maxon
typealias NegativeOne = int(-1 to -1)

function main() returns ExitCode
	let a = 3 as NegativeOne
	return a as ExitCode
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:12: Value 3 is outside the range of 'NegativeOne' (int(-1 to -1))
```

<!-- test: error.negative-value-renders-signed -->
The second diagnostic is `ExitCode`'s, and its range is the compile TARGET's — `int(0 to u32.max)` on
Windows, `int(0 to 255)` on Linux, macOS and WASI (`stdlib/Process.maxon`). Only the rendered bounds
move; **what this case pins is the SIGNED rendering of the first line's negative bounds**, which no
target argues about.
```maxon
typealias BelowMinusOne = int(i64.min to -2)

function main() returns ExitCode
	let a = -1 as BelowMinusOne
	return a
end 'main'
```
```maxoncstderr
error E3005: <fragment>:5:13: Value -1 is outside the range of 'BelowMinusOne' (int(-9223372036854775808 to -2))
error E3005: <fragment>:6:2: Value -1 is outside the range of 'ExitCode' (int(0 to 255))
```
```Maxoncstderr:x64-windows
error E3005: <fragment>:5:13: Value -1 is outside the range of 'BelowMinusOne' (int(-9223372036854775808 to -2))
error E3005: <fragment>:6:2: Value -1 is outside the range of 'ExitCode' (int(0 to 4294967295))
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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

### Array element: runtime panic

The array-element door's other half, and the one that was missing until `A1f-arrayelem`. An element
is a storage slot the alias governs exactly as a field is, so an UNFOLDABLE element owes the runtime
check a folded one owes the compile error — and until this rung it owed neither: `push` of an opaque
out-of-range value was admitted in silence and the element read back out was a `Percent` the compiler
still believed. The guard goes at the STORE, in the caller, where the element type is still known; the
panic names the `push` that wrote it.

<!-- test: array-element-runtime-panic -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
<!-- x64 ONLY, for `field-store-runtime-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. -->
```maxon
typealias Wide = int(0 to 1000)
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

function grow(n Wide) returns Wide
	return n * 101
end 'grow'

function main() returns ExitCode
	var a = PA.create()
	a.push(grow(5))
	return a.count()
end 'main'
```
```exitcode
1
```
```stderr
panic at array-element-runtime-panic.test:12: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

### Array `set`: runtime panic

`push` is not the only way in. `set` and `insert` reach the same slot through the same door
(`requireArrayElementType`), so all three owe the same two halves — a rule that held for one spelling
and not the others would be the door half-shut.

⚠ **AND THE COMPILE-TIME HALF OF `set` WAS THE ONE STILL MISSING (ARR1).** `push` and `insert` each have
their literal refusal above; `set` had only the runtime panic below, so the prose *"all three owe the same
two halves"* was two-thirds tested for a second time, one half over. This is the missing sixth.

<!-- test: error.array-set-out-of-range -->
```maxon
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

function main() returns ExitCode
	var a = PA.create()
	a.push(3)
	try a.set(0, value: 500) otherwise 'oob'
		return 9
	end 'oob'
	return a.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:8: Value 500 is outside the range of 'Percent' (int(0 to 100))
```

<!-- test: array-set-runtime-panic -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
<!-- x64 ONLY, for the reason above. -->
```maxon
typealias Wide = int(0 to 1000)
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

function grow(n Wide) returns Wide
	return n * 101
end 'grow'

function main() returns ExitCode
	var a = PA.create()
	a.push(3)
	try a.set(0, value: grow(5)) otherwise 'oob'
		return 9
	end 'oob'
	return a.count()
end 'main'
```
```exitcode
1
```
```stderr
panic at array-set-runtime-panic.test:13: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

### Array `insert`: the third spelling, and the one the prose above claimed without testing

⚠ The paragraph above says all three spellings owe the same two halves, and until ARR3c only `push`
and `set` had cases — a prose invariant with no case for its third member. **MEASURED (ARR3c): with
`insert` struck from `Parser.arraySurfaceMemberNames` and served from `stdlib/Array.maxon` instead,
BOTH halves vanish and the suite stays GREEN.** `insert(0, value: 300)` into an `Array with
int(0 to 255)` compiles, runs, and reads back **44**; a runtime-computed 300 exits 0 reading 44 too.
That is the corpus declaring `value Element`, so the shared callee knows no range and the call site has
no site kind for the substituted one — `set`'s blocker, on a second member. These two cases are what
would have said so.

<!-- test: error.array-insert-out-of-range -->
```maxon
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

function main() returns ExitCode
	var a = PA.create()
	a.push(3)
	a.insert(0, value: 500)
	return a.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:8:4: Value 500 is outside the range of 'Percent' (int(0 to 100))
```

<!-- test: array-insert-runtime-panic -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
<!-- x64 ONLY, for the reason above. -->
```maxon
typealias Wide = int(0 to 1000)
typealias Percent = int(0 to 100)
typealias PA = Array with Percent

function grow(n Wide) returns Wide
	return n * 101
end 'grow'

function main() returns ExitCode
	var a = PA.create()
	a.push(3)
	a.insert(0, value: grow(5))
	return a.count()
end 'main'
```
```exitcode
1
```
```stderr
panic at array-insert-runtime-panic.test:13: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

### An argument at an OPAQUE type parameter is checked against what the INSTANCE bound it to

⭐⭐ The rule the three `Array` spellings above are one instance of, stated where it actually lives: **it
is not about containers at all.** A generic body is compiled ONCE, so a parameter declared `value T`
carries no range and the callee can hold no entry guard for it — the cure every CONCRETE ranged
parameter gets (`RangeCheckParamSite`). The CALL SITE is the one place that knows what `T` was bound to,
so both halves are owed there: the compile-time E3005 for a literal, and the runtime guard for anything
else. The guard stands immediately in front of the call, which is exactly where the callee's entry guard
would have stood.

`Box with Percent` has no container in it and `b.put(505)` stored **505** in silence until this rung —
the same loss `insert` measured through `stdlib/Array.maxon`, one door up.

<!-- test: error.type-parameter-argument-out-of-range -->
```maxon
typealias Percent = int(0 to 100)

type Box uses T
	export var v as T

	static function create(seed T) returns Self
		return Self{v: seed}
	end 'create'

	export function put(value T)
		self.v = value
	end 'put'
end 'Box'

typealias PB = Box with Percent

function main() returns ExitCode
	var b = PB.create(1)
	b.put(505)
	return b.v
end 'main'
```
```maxoncstderr
error E3005: <fragment>:20:4: Value 505 is outside the range of 'Percent' (int(0 to 100))
```

### A STATIC factory's type-parameter argument is the same door

The factory has no receiver, so the instance rides its RESULT rather than its argument 0
(`retypeGenericAliasConstructorResult`) — a different way to reach the same substitution, and the rule
may not depend on which one a call took.

<!-- test: error.type-parameter-constructor-argument-out-of-range -->
```maxon
typealias Percent = int(0 to 100)

type Box uses T
	export var v as T

	static function create(seed T) returns Self
		return Self{v: seed}
	end 'create'
end 'Box'

typealias PB = Box with Percent

function main() returns ExitCode
	let b = PB.create(505)
	return b.v
end 'main'
```
```maxoncstderr
error E3005: <fragment>:15:13: Value 505 is outside the range of 'Percent' (int(0 to 100))
```

### Type-parameter argument: runtime panic

The half a literal cannot reach, at the position that has no callee entry guard to fall back on. ⚠ The
RUNNABLE ORACLE IS WRONG HERE and the case is written against the language rather than against it: the
bootstrap MONOMORPHIZES, so it refuses the literal above — but its runtime guard is still keyed on the
parameter's DECLARED type, which is the opaque `T`, so `b.put(grow(5))` compiles there and **exits 505**.
Every other position owes both halves (`RangeCheckGuard`); this one has no exemption available, because
the exemption a call argument does have is precisely the callee entry guard a shared body cannot hold.

<!-- test: type-parameter-argument-runtime-panic -->
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
<!-- x64 ONLY, for `field-store-runtime-panic`'s reason: this case pins the panic MESSAGE and the BACKTRACE, and only the two x64 lanes have a panic runtime to print them. The CHECK is target-neutral and the compile-time cases beside this one cover it on every target. -->
```maxon
typealias Wide = int(0 to 1000)
typealias Percent = int(0 to 100)

type Box uses T
	export var v as T

	static function create(seed T) returns Self
		return Self{v: seed}
	end 'create'

	export function put(value T)
		self.v = value
	end 'put'
end 'Box'

typealias PB = Box with Percent

function grow(n Wide) returns Wide
	return n * 101
end 'grow'

function main() returns ExitCode
	var b = PB.create(1)
	b.put(grow(5))
	return b.v
end 'main'
```
```exitcode
1
```
```stderr
panic at type-parameter-argument-runtime-panic.test:25: Range check failed: value outside typealias 'Percent'
Stack trace:
  in main
  in mrt_start
```

### An IN-RANGE value at an opaque type parameter still compiles and runs

The control for the three cases above: the guard must refuse the values the alias forbids and nothing
else. A check that passed by rejecting everything would be green on all three and would be caught here.

<!-- test: type-parameter-argument-in-range -->
```maxon
typealias Wide = int(0 to 1000)
typealias Percent = int(0 to 100)

type Box uses T
	export var v as T

	static function create(seed T) returns Self
		return Self{v: seed}
	end 'create'

	export function put(value T)
		self.v = value
	end 'put'
end 'Box'

typealias PB = Box with Percent

function grow(n Wide) returns Wide
	return n * 11
end 'grow'

function main() returns ExitCode
	var b = PB.create(1)
	b.put(grow(5))
	return b.v
end 'main'
```
```exitcode
55
```

### A type argument with NO narrowed range is untouched

`FullInt` spans the whole `int` domain, so there is nothing for a guard to test and nothing for a
literal to violate — the same `noGuard` verdict a full-range alias gets at every other position
(`RangeGuardVerdict`). This is the case that says the new site is decided by the RANGE and not merely by
the parameter being opaque.

<!-- test: type-parameter-argument-unranged-type-argument -->
```maxon
typealias FullInt = int(i64.min to i64.max)

type Box uses T
	export var v as T

	static function create(seed T) returns Self
		return Self{v: seed}
	end 'create'

	export function put(value T)
		self.v = value
	end 'put'
end 'Box'

typealias IB = Box with FullInt

function main() returns ExitCode
	var b = IB.create(1)
	b.put(1000000)
	return 42
end 'main'
```
```exitcode
42
```

### A MANAGED type argument reaches no range rule at all

A `String` type argument names no ranged alias, so the door declines before it looks for bounds — the
site must not fire on a type argument that carries no numeric domain.

<!-- test: type-parameter-argument-managed-type-argument -->
```maxon
type Box uses T
	export var v as T

	static function create(seed T) returns Self
		return Self{v: seed}
	end 'create'

	export function put(value T)
		self.v = value
	end 'put'
end 'Box'

typealias SB = Box with String

function main() returns ExitCode
	var b = SB.create("a")
	b.put("hello")
	return b.v.byteLength()
end 'main'
```
```exitcode
5
```

### The `Array` half: an element reached through a shared body's `Element` parameter

⭐ This is `push`/`set`/`insert`'s shape with nothing retired to reach it. An `extension Array` method
declaring `value Element` is compiled ONCE — exactly as `stdlib/Array.maxon`'s own three mutators are —
so the element's range is invisible inside it and visible only at the call. It measured the identical
loss: **compiled clean, stored 300 twice, exit 2.** The roster-served `insert` beside it refuses the same
literal today; when `insert` is struck, this is the door it arrives at.

<!-- test: error.array-extension-element-argument-out-of-range -->
```maxon
typealias Byte = int(0 to 255)
typealias BA = Array with Byte

export extension Array
	export function pushTwice(value Element)
		push(value)
		push(value)
	end 'pushTwice'
end 'Array'

function main() returns ExitCode
	var a = BA.create()
	a.pushTwice(300)
	return a.count()
end 'main'
```
```maxoncstderr
error E3005: <fragment>:14:4: Value 300 is outside the range of 'Byte' (int(0 to 255))
```

### An OVERLOADED method is declined, not checked against the member the call did not resolve to

⛔ The boundary, pinned by the program that fell through it. The declaration sweep keys by BARE NAME — the
limitation `Parser.requireOverloadableName` states in full — so `stdlib/Array.maxon`'s two `contains`
declarations, `contains(element Element)` and `contains(sequence ElementArray)`, arrive under one key.
Answering for the set from either member checks the OTHER member's argument: **MEASURED in this rung's own
first build, `a.contains(needle)` — which compiles and runs — had its ARRAY POINTER range-checked against
`0 to 255` and died `panic … value outside typealias 'Byte'`.** A disagreeing pair is therefore CONTESTED
and gets no check at all, which costs a missed refusal on an overloaded generic method and can never cost
a wrong one. This case is that program, and it must stay green.

<!-- test: overloaded-generic-method-is-not-range-checked -->
```maxon
typealias Byte = int(0 to 255)
typealias BA = Array with Byte

function main() returns ExitCode
	var a = BA.create()
	a.push(3)
	a.push(4)
	var needle = BA.create()
	needle.push(4)
	if a.contains(needle) 'has'
		return 7
	end 'has'
	return 9
end 'main'
```
```exitcode
7
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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
<!-- targets: x64-windows, x64-linux, arm64-macos, arm64-linux -->
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

## A RANGED ALIAS'S TYPE IDENTITY IS ITS RANGE, NOT ITS NAME (R-1)

⭐⭐ **TWO RANGED ALIASES OVER ONE RANGE ARE ONE TYPE, SO THEIR GENERIC INSTANCES ARE ONE INSTANCE**
(user ruling, 2026-08-22). `typealias DenseInt = int(0 to u64.max)` and `typealias RegCount = int(0 to
u64.max)` differ only in what the author called them, so `Array with DenseInt` and `Array with RegCount`
name one type. Both compilers have always agreed about the VALUES; until R-1 shv2 called them two types and
refused three sites in its own source.

⚠ **THE RULE IS THE RANGE, AND THE CONTROL BELOW IS WHAT SAYS SO.** `e4146cf8e` made a generic's ranged
element part of its type, and that is untouched: two aliases over DIFFERENT ranges are still two types and
are still refused. R-1 narrows that rule to what it was always meant to say -- the RANGE is part of the
type, and the NAME is not. Without the control this section would be indistinguishable from having deleted
the rule outright.

⚠ **A COMPILER-RESERVED ELEMENT IS NEVER IDENTIFIED THIS WAY, and it is unspellable in source so it
cannot be pinned here.** `__ManagedByte` carries a byte's range and is minted expressly to be a DIFFERENT
instance from the user-visible `Byte`, because the admission between them is one-way (see
`SignatureIndex.byteBufferBoundaryAdmits`). `RangedAliasRegistry.identifiableRangeName` excludes the `__`
prefix for that reason; `array-hashable/byte-array-hash` is the case that measured it, having gone red for
exactly this when the exclusion was absent.

<!-- test: two-aliases-over-one-range-are-one-instance -->
The shape from shv2's own source: a value built through one alias reaches a parameter declared with the
other, and is READ THROUGH that parameter -- which is what proves it is the same container and not a
conversion.
```maxon
typealias DenseInt = int(0 to u64.max)
typealias RegCount = int(0 to u64.max)
typealias DenseColumn = Array with DenseInt
typealias RegCountColumn = Array with RegCount

function widthOf(c RegCountColumn) returns DenseInt
	return c.count()
end 'widthOf'

function main() returns ExitCode
	var d = DenseColumn.create()
	d.push(7)
	d.push(9)
	print("width={widthOf(d)}")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
width=2
```

<!-- test: error.two-aliases-over-different-ranges-are-still-two-types -->
⭐ **THE CONTROL.** The only difference from the case above is that the two ranges differ. `e4146cf8e`'s
rule stands, the two columns are two types, and the bootstrap answers with this message character for
character.
```maxon
typealias Wide = int(0 to u64.max)
typealias Narrow = int(0 to 63)
typealias WideColumn = Array with Wide
typealias NarrowColumn = Array with Narrow

function takesNarrow(c NarrowColumn) returns Wide
	return c.count()
end 'takesNarrow'

function main() returns ExitCode
	var w = WideColumn.create()
	w.push(7)
	return takesNarrow(w) as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.two-aliases-over-different-ranges-are-still-two-types.test:14:9: argument type mismatch for 'c': expected 'NarrowColumn', got 'WideColumn'
```

<!-- test: error.a-diagnostic-names-the-alias-the-site-wrote -->
### A diagnostic names the alias the SITE wrote, not the first one declared
⛔⛔ **R-1 MADE THE `GenericInstanceId` AN INSUFFICIENT ANSWER TO "WHAT IS THIS VALUE'S TYPE CALLED?"** `WideCol`
and `OtherCol` are one instance here, so a per-gid display can only pick one of them — and it picked the one
declared FIRST, naming `WideCol` in a statement that says `OtherCol`. The bootstrap names the site's spelling,
and this case pins that shv2 now does too.

⭐ **THE PROVENANCE COLUMN ALREADY EXISTED AND THIS DOOR NEVER WROTE IT.** `valueInstanceAlias` was built for
P1.6-C to carry *"the provenance the shared `GenericInstanceId` cannot carry (`WrapperA` and `WrapperB` share
one gid)"* — the identical sentence, one type-family over. It was stamped only by
`retypeGenericAliasConstructorResult`, which a BUILTIN container's factory never reaches: its result is already
an instance rather than the shared body's base `structRef`, so it falls out of that door's first guard.
MEASURED with a probe: ZERO calls to it for `OtherCol.create()`. `emitOwnedContainerCreate` stamps it now.

⚠ **THE VALUE IS READ BACK THROUGH A `var`, DELIBERATELY.** A case that passed the factory result straight into
the call would not say whether the provenance survives a binding — and the binding is the shape a real program
writes.
```maxon
typealias Wide = int(0 to u64.max)
typealias Other = int(0 to u64.max)
typealias WideCol = Array with Wide
typealias OtherCol = Array with Other
typealias Narrow = int(0 to 63)
typealias NarrowCol = Array with Narrow

function widthOf(c WideCol) returns ExitCode
	return c.count() as ExitCode
end 'widthOf'

function takesNarrow(c NarrowCol) returns ExitCode
	return c.count() as ExitCode
end 'takesNarrow'

function main() returns ExitCode
	var o = OtherCol.create()
	o.push(2)
	let w = widthOf(o)
	return takesNarrow(o) + w
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.a-diagnostic-names-the-alias-the-site-wrote.test:21:9: argument type mismatch for 'c': expected 'NarrowCol', got 'OtherCol'
```

<!-- test: error.a-call-results-type-is-named-by-its-callees-returns-clause -->
### A CALL RESULT is named by the alias its callee's `returns` clause wrote
⭐⭐ **THE OTHER HALF OF THE PROVENANCE, AND THE ONE shv2's OWN SOURCE NEEDED.** The case above stamps a value
built by a factory; this one arrives from a plain call, and its only spelling is the one the CALLEE's signature
wrote. `filledColumn(…) returns DenseColumn` in `Targets/Shared/TargetLiveness.maxon` hands back a gid every
`Array` over an `int(0 to u64.max)` element shares, and without this the diagnostic at
`RegisterAllocator.maxon:93` could only fall back to the element RANGE.

⚠ **THE EXAMPLE THIS PARAGRAPH USED TO NAME NO LONGER BELONGS TO THAT SET, WHICH IS WHY IT NAMES THE
SET AND NOT A MEMBER.** It read *"a gid that `DurationNanosArray` and 26 other `int(0 to u64.max)`
aliases share"*; `stdlib/Clock.maxon`'s `DurationNanos` has since been narrowed to
`int(0 to i64.max)`, so `DurationNanosArray` is a DIFFERENT instance now and the 26 was a census of a
tree that has moved. The mechanism is unchanged — several distinct alias names still collapse onto one
`Array` instance whenever their element ranges agree — and it is the mechanism, not the census, that
this test pins.

⚠ **THE SPELLING IS CAPTURED AS THE TOKEN'S TEXT AND NOTHING IS ASKED ABOUT IT AT CAPTURE TIME.** Whether the
name is a generic alias is a WHOLE-PROGRAM question and the capture runs inside the declaration sweep that
answers it — asking there is R-1's own first mistake one layer up. `recordCallResultInstanceAlias` asks at parse
time, with the index complete.

⚠ `returns Array with Integer` writes no alias and is left EMPTY rather than being stamped with the base name —
`Array` names no instance, and a value stamped with it would file a per-instance question under a name no
per-instance registry answers.
```maxon
typealias Wide = int(0 to u64.max)
typealias Other = int(0 to u64.max)
typealias WideCol = Array with Wide
typealias OtherCol = Array with Other
typealias Narrow = int(0 to 63)
typealias NarrowCol = Array with Narrow

function makeOther() returns OtherCol
	return OtherCol.create()
end 'makeOther'

function widthOf(c WideCol) returns ExitCode
	return c.count() as ExitCode
end 'widthOf'

function takesNarrow(c NarrowCol) returns ExitCode
	return c.count() as ExitCode
end 'takesNarrow'

function main() returns ExitCode
	let w = widthOf(WideCol.create())
	return takesNarrow(makeOther()) + w
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.a-call-results-type-is-named-by-its-callees-returns-clause.test:23:9: argument type mismatch for 'c': expected 'NarrowCol', got 'OtherCol'
```

## A bare `int`/`float` is not a type — the ranged-typealias rule at every position

`int` and `float` name a DOMAIN and no RANGE, and every reader in this language asks a ranged alias for
its bounds. So the keyword is legal in exactly one place — the RHS of a `typealias` — and every other
type position must name a declared alias. `bool` and `cstring` are exempt: they are already constrained
types with nothing to range.

This is not a new rule. `docs/LANGUAGE_REFERENCE.md` has stated it since long before it was enforced
everywhere, and the C# bootstrap has enforced it at every type position all along. What these cases pin
is that **both** compilers now do, at **both** cast doors — the body cast and the top-level `let`'s,
which are two different walks over two different code paths.

<!-- test: error.bare-int-parameter -->
A parameter is an ordinary type position, so the keyword is refused there and the diagnostic is anchored
on the keyword itself — the token that has to change, not the name to its left.
```maxon
function add(a int, b int) returns ExitCode
	return 0
end 'add'

function main() returns ExitCode
	return add(1, b: 2)
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.bare-int-parameter.test:2:16: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: error.bare-float-return -->
The float half, which carries its own example range (`float(0.0 to 1.0)`) rather than the int's.
```maxon
function half() returns float
	return 0.5
end 'half'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.bare-float-return.test:2:25: Cannot use bare 'float' as a type. Define a typealias with range constraints, e.g., typealias MyFloat = float(0.0 to 1.0)
```

<!-- test: error.bare-int-struct-field -->
A struct field is the third spelling of a declared type, and it takes the same refusal. (`var x as int`
is the FIELD form; a local is always `let x = expr` and has no annotation to refuse.)
```maxon
type Holder
	export var v as int
end 'Holder'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.bare-int-struct-field.test:3:18: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: error.cast-to-bare-int -->
A CAST TARGET gets its own sentence, because the rewrite it needs is shaped differently: a declaration
site is told to declare an alias, a cast site is shown the cast that would work.
```maxon
typealias Score = int(0 to 100)

function main() returns ExitCode
	let s = 5 as Score
	let n = s as int
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.cast-to-bare-int.test:6:15: Cannot cast to bare 'int'. Define a typealias with range constraints, e.g., 'value as MyInt' where 'typealias MyInt = int(0 to 100)'
```

<!-- test: error.top-level-const-cast-to-bare-int -->
⭐ **THE SECOND CAST DOOR, AND THE ONE THAT WAS WRONG IN THE BOOTSTRAP FOR MONTHS.** A top-level `let`'s
initializer never reaches the expression path — it is folded by the const evaluator, a different walk
over different code. The bootstrap's body cast refused the bare keyword while its const walk accepted it
as an "identity cast", so this exact program COMPILED while the identical cast inside a function did not:
one mistake with two verdicts, decided by nothing but where the `let` was written. Both doors now ask the
same question and give the same sentence at the same anchor.
```maxon
let SENTINEL = 5 as int

function main() returns ExitCode
	return SENTINEL as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.top-level-const-cast-to-bare-int.test:2:21: Cannot cast to bare 'int'. Define a typealias with range constraints, e.g., 'value as MyInt' where 'typealias MyInt = int(0 to 100)'
```

<!-- test: the-typealias-rhs-admits-the-keyword -->
⭐ **THE ONE POSITION THAT STAYS LEGAL, AND IT IS THE WHOLE RHS — not just the ranged form.** A FUNCTION
alias's parameters and return and a TUPLE alias's elements are inside the RHS too, and all three are
accepted. `bool` stays bare everywhere. Read this with the case below: the tuple spelling that is legal
HERE is refused one position over, which is what says the exemption is the RHS rather than the construct.
```maxon
typealias Small = int(0 to 100)
typealias IntOp = function(n int) returns int
typealias Pair = (int, int)

function dbl(n Small) returns Small
	return n + n
end 'dbl'

function apply(f IntOp, p Pair, flag bool) returns Small
	return f(p.0) + p.1 if flag else 0
end 'apply'

function main() returns ExitCode
	let p = (3, 4)
	return apply(dbl, p: p, flag: true)
end 'main'
```
```exitcode
10
```

<!-- test: error.a-tuple-in-a-parameter-is-not-an-rhs -->
The negative half of the pair above, and the reason the exemption had to be stated as a POSITION rather
than as a construct: the identical `(int, int)` that a `typealias` RHS accepts is refused in a parameter,
because a parameter is not an RHS.
```maxon
typealias Small = int(0 to 100)

function first(t (int, int)) returns Small
	return t.0
end 'first'

function main() returns ExitCode
	return 0 as ExitCode
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/ranged-typealias/error.a-tuple-in-a-parameter-is-not-an-rhs.test:4:19: Cannot use bare 'int' as a type. Define a typealias with range constraints, e.g., typealias MyInt = int(0 to 100)
```

<!-- test: sizeof-admits-the-keyword -->
`sizeof` asks about a REPRESENTATION, not about a value's domain, so there is no range for an alias to
carry and the keyword is admitted. 8 + 8 = 16.
```maxon
function main() returns ExitCode
	return (sizeof(int) + sizeof(float)) as ExitCode
end 'main'
```
```exitcode
16
```
