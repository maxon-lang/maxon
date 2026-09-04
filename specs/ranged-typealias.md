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
	let n = ident(0 - 20)
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
	let b = ident(0 - 3) as NegativeOnly
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
	let w = ident(0 - 1) as MachineWord
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
	let offset = ident(0 - 65) as Imm32
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
	return 0 - imm
end 'magnitude'

function main() returns ExitCode
	let offset = ident(0 - 64) as Imm32
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
	let offset = ident(0 - 65) as Imm32
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
	let offset = ident(0 - 65) as Imm32
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

### Unsigned-max upper: runtime cast of a bit-63-set value

An `int(N>0 to u64.max)` range is UNSIGNED — a value with bit 63 set is a huge
unsigned that the range admits, not a negative below the low bound. The runtime
check treats out-of-range as `value >= 0 AND value < N`, so a bit-63-set value
(negative in signed terms) passes rather than tripping a naive signed lower
check. This makes the runtime check agree with the compile-time LITERAL check,
which already reads the bound unsigned.

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

### A use that follows a recovered error still counts as a use

<!-- test: unused-typealias-after-recovered-error -->
```maxon
typealias Tally = int(0 to 100)

function main() returns ExitCode
	var arr = [1, 2, 3]
	arr.set(0, value: 100)
	let t = 5 as Tally
	print("{t}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3057: specs/fragments/ranged-typealias/unused-typealias-after-recovered-error.test:6:6: throwing function requires try: 'stdlib.Array.set'
```

### An unused typealias does not suppress the other diagnostics

<!-- test: unused-typealias-does-not-suppress-other-errors -->
```maxon
typealias Tally = int(0 to 100)
typealias Spare = int(0 to 50)

function main() returns ExitCode
	let t = 5 as Tally
	let u = t as Tally
	print("{u}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3010: specs/fragments/ranged-typealias/unused-typealias-does-not-suppress-other-errors.test:7:12: unneeded cast: 'Tally' already fits in 'Tally'
error E3062: specs/fragments/ranged-typealias/unused-typealias-does-not-suppress-other-errors.test:3:11: unused typealias: 'Spare'
```

### A use inside a lazy static initializer counts as a use

<!-- test: unused-typealias-in-lazy-static-initializer -->
```maxon
typealias Tally = int(0 to 100)

type Box
	static var v = [5 as Tally, 6 as Tally]

	export static function ready() returns ExitCode
		return 0
	end 'ready'
end 'Box'

function main() returns ExitCode
	return Box.ready()
end 'main'
```
```exitcode
0
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

### Error: negative literal lower with u64.max upper

A negative-literal lower paired with `u64.max` upper cannot be represented in 64 bits — the upper bound exceeds `i64.max`, so no single 64-bit type can hold both ends. Without this check the parser would silently collapse the range to `-1..-1` (because `u64.max` is stored as the signed long `-1`).

<!-- test: error.negative-low-u64-max -->
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

### ⭐⭐ A DECLARED LOWER BOUND OF 0 REFUSES A WRITTEN NEGATIVE — the upper being `u64.max` does not repeal it

`int(0 to u64.max)` admits every 64-bit PATTERN, so no RUNTIME guard on it can ever fail. It still
declares `≥ 0`, and a literal the source wrote as a negative number is below that bound. The two
questions are different and the compiler used to conflate them: the full-range test short-circuited
the COMPILE-TIME literal check as well as the runtime one, so `take(-1)` compiled clean into such a
parameter and printed **18446744073709551615**, on both compilers.

⚠⚠ **NO TEST OF THE VALUE CAN DECIDE THIS.** `-1`, `u64.max` and `0xFFFFFFFFFFFFFFFE` are the SAME
64 bits; the first denotes a negative number and the other two denote the two largest non-negative
ones. The verdict therefore comes from what the SOURCE WROTE — `_writtenNegativeLiterals`, the
companion of the `_unsignedMaxLiterals` mark the ordering rule already reads — and the two
acceptances below are as load-bearing as the refusal: they are what says the check reads the source
and not the bit pattern.

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
(`(-1) as Narrow`, the `as` loop sitting outside `ParsePrimary`), so **`let A = -1 as int(0 to 100)`
compiled clean and produced -1** while the identical cast inside a function was E3005. Both
compilers agreed on the wrong answer, so no oracle could see it.

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

### A wholly-negative range's UPPER bound is a real bound, and is checked

A stored upper of `-1` means `u64.max` in exactly ONE shape: an UNSIGNED range, whose low bound is
non-negative (`int(5 to u64.max)`). Such a range is genuinely unbounded upwards in signed terms, so
its upper compare is elided on purpose and replaced by the sign-plus-lower cascade above. A wholly
NEGATIVE range — `int(-100 to -1)`, `int(i64.min to -2)` — also stores a negative upper, and there
the bound is ordinary: `-1` is the largest value it admits and `0` is outside it.

The LITERAL check has always told the two apart by the LOW bound; the runtime check tested only the
upper's sign, so the two halves of one rule disagreed about one alias — a literal
`0 as int(-100 to -1)` was E3005 (see `error.negative-out-of-range` above) while a runtime `0` cast
into it was admitted.

⚠ It matters beyond the binding: `specs/safety.md`'s division proof reads a divisor's DECLARED
range, so both of `idiv`'s hazards were reachable through this hole. Both are pinned there.

<!-- test: negative-upper-bound-cast-is-checked -->
#### A runtime cast into a wholly-negative range tests its upper bound
`0` is above `-1`, so the cast is the violation and the guard must fire at the cast's own line — as
it does for the positive `int(0 to 150)` in `runtime-check-fail` above. Before the fix `a` simply
became `0`, a value its declared type does not admit.
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
```stdout
start
```
```exitcode
1
```
```stderr
panic at negative-upper-bound-cast-is-checked.test:11: Range check failed: value outside typealias 'NegativeOnly'
Stack trace:
  in main
  in mrt_start
```

<!-- test: negative-upper-bound-return-is-checked -->
#### The ranged-RETURN door tests it too, and `low == i64.min` is what left it with no check at all
`int(i64.min to -2)` needs NO lower compare (`i64.min` cannot be violated), so eliding the upper as
well left the range with an EMPTY check list — the one shape where the hole was total rather than
partial, and a plain narrowing admitted anything.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias BelowMinusOne = int(i64.min to -2)

function ident(v Integer) returns Integer
	return v
end 'ident'

function narrow() returns BelowMinusOne
	return ident(0 - 1)
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
#### The in-range control — the restored check must not fire on a value the range admits
Both bounds of a wholly-negative range, through the cast door and the return door, with admissible
values. A check restored to a shape must cost nothing where the value is legal.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias NegativeOnly = int(-100 to -1)

function ident(v Integer) returns Integer
	return v
end 'ident'

function pick() returns NegativeOnly
	return ident(0 - 100)
end 'pick'

function main() returns ExitCode
	let a = ident(0 - 1) as NegativeOnly
	let b = ident(0 - 50) as NegativeOnly
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

<!-- test: cast-to-stdlib-internal-typealias -->
A typealias declared inside the stdlib is reachable as a cast target from any
file, regardless of its source-level visibility modifier. The stdlib's internal
ranged aliases (`ElementIndex`, `NodeIndex`, …) appear in the public collection
API, so user code must be able to name them in an `as` cast — `5 as ElementIndex`
resolves rather than failing with "Expected type name after 'as'".
```maxon
function main() returns ExitCode
	let n = 5 as ElementIndex
	return n as ExitCode
end 'main'
```
```exitcode
5
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
