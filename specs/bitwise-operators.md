---
feature: bitwise-operators
status: implemented
keywords: [bitwise, and, or, xor, shift, not, shl, shr, operators]
category: operators
---

# Bitwise Operators

## Documentation

Maxon provides bitwise operators for manipulating individual bits of integer values. The `and`, `or`, `xor`, and `not` keywords are context-dependent: they perform bitwise operations on integers and logical operations on booleans.

### Bitwise AND (`and`)

Returns 1 for each bit position where both operands have 1:

```maxon
var a = 12       // 1100 in binary
var b = 10       // 1010 in binary
var c = a and b  // 1000 = 8
```

### Bitwise OR (`or`)

Returns 1 for each bit position where either operand has 1:

```maxon
var a = 12      // 1100 in binary
var b = 10      // 1010 in binary
var c = a or b  // 1110 = 14
```

### Bitwise XOR (`xor`)

Returns 1 for each bit position where operands differ:

```maxon
var a = 12       // 1100 in binary
var b = 10       // 1010 in binary
var c = a xor b  // 0110 = 6
```

### Bitwise NOT (`not`)

Flips all bits of an integer value:

```maxon
var a = 5        // ...0101 in binary
var b = not a    // ...1010 = -6
```

### Left Shift (`shl`)

Shifts bits left by the specified amount, filling with zeros:

```maxon
var a = 1
var b = a shl 3  // 1000 = 8
```

### Right Shift (`shr`)

Shifts bits right by the specified amount. `shr` is an **arithmetic** (sign-propagating) shift:
every Maxon `int` is a *signed* 64-bit integer, so the vacated high bits are filled with the sign
bit, not with zeros.

```maxon
var a = 16
var b = a shr 2       // 0100 = 4      — a non-negative value zero-fills naturally
var c = (0 - 8) shr 1 // -4, not 9223372036854775804
```

Only a value whose ranged type is *unsigned* (`int(0 to u64.max)`) zero-fills.

### Shift Count

Maxon's shift semantics are **Go's**, and they are deliberately not the hardware's.

**The count is not masked.** There is no upper limit on a shift count: a shift by `n` behaves as
if the value were shifted one place `n` times. So a count of 64 or more shifts *every bit out*,
and that is legal, not an error:

```maxon
let a = 1
let b = a shl 64      // 0 — every bit shifted out
let c = a shl 100     // 0
let d = (0 - 1) shr 100  // -1 — a sign-filling shift leaves the sign
let e = 8 shr 100        // 0
```

This matters because the hardware **masks** the count into `0..63` instead of rejecting it, which
produces a plausible-looking wrong answer: unguarded, `a shl 64` would compute `a shl 0` (leaving
`a` *unchanged*) and `a shl 100` would compute `a shl 36`. The compiler saturates the count so
that never happens — folding it when it can see it, and guarding it at run time when it cannot.

**A negative count is an error.** It is not a shift the other way: masked, `a shl -1` would
silently compute `a shl 63` — the *maximum left shift*, a wrong answer with the opposite sign. A
count the compiler can fold — a literal, a named `let`, or constant arithmetic — is a compile
error (**E2054**); a count that only appears at run time **panics**.

```maxon
let a = 1
let b = a shl -1      // E2054: shift count -1 is negative
let SHIFT = -1
let c = a shl SHIFT   // E2054 — a named constant is a constant
```

A shift by a **runtime value** is legal and is guarded, not masked:

```maxon
let count = 64
let d = a shl count   // 0 — the same answer the folded form gives
```

## Tests

<!-- test: bitwise-and -->
```maxon
function main() returns ExitCode
	let a = 12
	let b = 10
	return a and b
end 'main'
```
```exitcode
8
```

<!-- test: bitwise-or -->
```maxon
function main() returns ExitCode
	let a = 12
	let b = 10
	return a or b
end 'main'
```
```exitcode
14
```

<!-- test: bitwise-xor -->
```maxon
function main() returns ExitCode
	let a = 12
	let b = 10
	return a xor b
end 'main'
```
```exitcode
6
```

<!-- test: left-shift -->
```maxon
function main() returns ExitCode
	let a = 1
	return a shl 3
end 'main'
```
```exitcode
8
```

<!-- test: right-shift -->
```maxon
function main() returns ExitCode
	let a = 16
	return a shr 2
end 'main'
```
```exitcode
4
```

<!-- test: shift-chained -->
```maxon
function main() returns ExitCode
	let a = 1
	return a shl 4 shr 2
end 'main'
```
```exitcode
4
```

<!-- test: bitwise-and-or-precedence -->
```maxon
function main() returns ExitCode
	// and has higher precedence than or
	// 12 and 10 = 8, then 8 or 1 = 9
	return 12 and 10 or 1
end 'main'
```
```exitcode
9
```

<!-- test: bitwise-xor-precedence -->
```maxon
function main() returns ExitCode
	// and has higher precedence than xor
	// 12 and 10 = 8, then 8 xor 3 = 11
	return 12 and 10 xor 3
end 'main'
```
```exitcode
11
```

<!-- test: shr-is-arithmetic -->
⚠ THE CASE NEITHER SPEC FILE PINNED, WHICH IS WHY THE TWO COMPILERS DIVERGED. Go: "the shift
operators implement arithmetic shifts if the left operand is a signed integer". Every Maxon `int`
IS a signed integer, so `shr` sign-propagates. Unpinned, the bootstrap zero-filled — `(0-8) shr 60`
was 15 — while shv2 emitted `sar` and answered -1.
```maxon
function main() returns ExitCode
	let neg = 0 - 8
	print("{neg shr 60}\n")
	print("{neg shr 1}\n")
	return 0
end 'main'
```
```stdout
-1
-4
```
```exitcode
0
```

<!-- test: shr-nonnegative-zero-fills -->
The other half of the same rule: an arithmetic shift of a NON-NEGATIVE value fills with a sign bit
that is 0, so it zero-fills naturally. Making `shr` arithmetic did not change this.
```maxon
function main() returns ExitCode
	return 8 shr 2
end 'main'
```
```exitcode
2
```

<!-- test: shl-count-negative -->
A negative shift count reads as "shift the other way" and is not that at all: masked, `shl -1`
silently became `shl 63` — the MAXIMUM left shift, a wrong answer with the opposite sign. The
compiler is holding the count, so it rejects it.
```maxon
function main() returns ExitCode
	let a = 1
	return a shl -1
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shl-count-negative.test:4:15: Shift count -1 is negative: a shift distance must be 0 or greater (a count of 64 or more is legal — it shifts every bit out)
```

<!-- test: shr-count-negative -->
The same rule, asked of `shr`.
```maxon
function main() returns ExitCode
	let a = 128
	return a shr -1
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shr-count-negative.test:4:15: Shift count -1 is negative: a shift distance must be 0 or greater (a count of 64 or more is legal — it shifts every bit out)
```

<!-- test: shl-count-negative-named -->
⚠ THE CASE THE CHECK'S OWN MOTIVATING EXAMPLE MISSED. The count is the same -1; only its SPELLING
differs. A check that asks "is this token span a literal?" does not see it. A check that asks the
CONSTANT FOLDER — which is holding the value — does.
```maxon
function main() returns ExitCode
	let a = 1
	let SHIFT = -1
	return a shl SHIFT
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shl-count-negative-named.test:5:15: Shift count -1 is negative: a shift distance must be 0 or greater (a count of 64 or more is legal — it shifts every bit out)
```

<!-- test: shl-count-negative-parenthesized -->
`-(1)` is -1 written so that a paren-stripping loop runs BEFORE the `-` is ever tested, and so
never finds a literal to reject. The folder has no such blind spot.
```maxon
function main() returns ExitCode
	let a = 1
	return a shl -(1)
end 'main'
```
```maxoncstderr
error E2054: specs/fragments/bitwise-operators/shl-count-negative-parenthesized.test:4:15: Shift count -1 is negative: a shift distance must be 0 or greater (a count of 64 or more is legal — it shifts every bit out)
```

<!-- test: shl-count-64 -->
⚠ NOT AN ERROR. 64 shifts every bit out, which is exactly what Go says it means — "there is no
upper limit on the shift count". Rejecting it (which this compiler briefly did) OVER-rejects a
correct program; MASKING it (which the hardware does) computes `a shl 0` and leaves `a` UNCHANGED,
the least likely thing the author meant. It is 0.
```maxon
function main() returns ExitCode
	let a = 1
	print("{a shl 64}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: shl-count-100 -->
The commit message's own example — `let MASK = 1 shl 100` — through both a named constant and a
literal. Masked, it was `1 shl 36` = 68719476736.
```maxon
let SHIFT = 100
let MASK = 1 shl SHIFT

function main() returns ExitCode
	let a = 1
	print("{MASK}\n")
	print("{a shl 100}\n")
	return 0
end 'main'
```
```stdout
0
0
```
```exitcode
0
```

<!-- test: shr-count-past-width -->
A right shift past the width saturates to the SIGN, not to zero — because a sign-filling shift
that moves every bit out leaves the sign behind. `x shr 63` already IS the sign, which is why the
compiler saturates the COUNT here rather than selecting the result.
```maxon
function main() returns ExitCode
	print("{(0 - 1) shr 100}\n")
	print("{8 shr 100}\n")
	return 0
end 'main'
```
```stdout
-1
0
```
```exitcode
0
```

<!-- test: shift-count-arithmetic -->
A count the compiler can fold is a count it can check, however it is spelled. All three of these
are a count of 64, and none of them is a literal.
```maxon
function main() returns ExitCode
	let a = 1
	print("{a shl 63 + 1}\n")
	print("{a shl 2 * 32}\n")
	print("{a shl 128 / 2}\n")
	return 0
end 'main'
```
```stdout
0
0
0
```
```exitcode
0
```

<!-- test: shift-by-variable-count -->
⚠ THE GUARD AGAINST OVER-REJECTING, AND THE PROOF THE FOLD AND THE CODEGEN AGREE. A count that
arrives as a VALUE is still legal — and it must give the SAME answer the folded form gives. The
hardware masks the `cl` count to its low 6 bits, so an unguarded `7 shl 64` would be `7 shl 0` = 7.
It is 0.
```maxon
function main() returns ExitCode
	let a = 7
	let count = 64
	print("{a shl count}\n")
	return 0
end 'main'
```
```stdout
0
```
```exitcode
0
```

<!-- test: shift-by-parameter-count -->
The same, with a count NO pass can see: a parameter. Unguarded, 65 masks to 1 and `7 shl 65` is
14; 100 masks to 36. The guard is what makes the runtime answer match the constant-folded one.
```maxon
typealias Num = int(i64.min to i64.max)

function shiftLeft(value Num, count Num) returns Num
	return value shl count
end 'shiftLeft'

function shiftRight(value Num, count Num) returns Num
	return value shr count
end 'shiftRight'

function main() returns ExitCode
	print("{shiftLeft(7, count: 65)}\n")
	print("{shiftLeft(7, count: 3)}\n")
	print("{shiftRight(0 - 1, count: 100)}\n")
	print("{shiftRight(8, count: 100)}\n")
	print("{shiftRight(0 - 8, count: 1)}\n")
	return 0
end 'main'
```
```stdout
0
56
-1
0
-4
```
```exitcode
0
```

<!-- test: shr-signedness-is-the-left-operand-only -->
A shift is NOT symmetric in its operands: the right one is a DISTANCE, and its declared type says
nothing about how the shift fills. Only the value being SHIFTED decides that.

This is pinned because the compiler got it wrong. A shift took its `OptimalType` from `lhs ?? rhs`,
so a count declared `int(0 to 63)` — an *unsigned* optimal type, and the most natural way there is
to declare a shift distance — reported the whole shift as unsigned and made a SIGNED `shr`
zero-fill. `(0-8) shr 60` answered -1 for a plain count and **15** for that one: the same shift,
two answers, chosen by an irrelevant property of the other operand.

Every line below is the same arithmetic shift and must print -1.
```maxon
typealias Num = int(i64.min to i64.max)
typealias ShiftBits = int(0 to 63)
typealias Unsigned64 = int(0 to u64.max)

function plainCount(n Num) returns Num
	return n
end 'plainCount'

function bitsCount(n ShiftBits) returns ShiftBits
	return n
end 'bitsCount'

function wideCount(n Unsigned64) returns Unsigned64
	return n
end 'wideCount'

function shiftRight(value Num, distance ShiftBits) returns Num
	return value shr distance
end 'shiftRight'

function main() returns ExitCode
	let neg = 0 - 8
	print("{neg shr plainCount(60)}\n")
	print("{neg shr bitsCount(60)}\n")
	print("{neg shr wideCount(60)}\n")
	print("{neg shr 60}\n")
	// The realest shape of all, and the one a programmer would actually write: BOTH operands are
	// parameters, and the distance is declared with the range a distance has. Neither the fold nor
	// the guard may look at `distance`'s type to decide how `value` fills.
	print("{shiftRight(0 - 8, distance: 60)}\n")
	print("{shiftRight(0 - 1, distance: 63)}\n")
	return 0
end 'main'
```
```stdout
-1
-1
-1
-1
-1
-1
```
```exitcode
0
```

<!-- test: shift-by-negative-runtime-count-panics -->
Go: "if the shift count is negative at run time, a run-time panic occurs". The compiler could not
fold this count, so the program proves it — and refuses to compute the masked `4 shl 63` the
hardware would otherwise have handed back.
```maxon
typealias Num = int(i64.min to i64.max)

function shiftLeft(value Num, count Num) returns Num
	return value shl count
end 'shiftLeft'

function main() returns ExitCode
	let bad = 0 - 1
	print("{shiftLeft(4, count: bad)}\n")
	return 0
end 'main'
```
```stderr
panic at shift-by-negative-runtime-count-panics.test:5: negative shift count
Stack trace:
  in shiftLeft
  in main
  in mrt_start
```
```exitcode
1
```

<!-- test: shift-vs-comparison -->
```maxon
function main() returns ExitCode
	// Shift has higher precedence than comparison
	// 1 shl 3 = 8, then 8 > 5 = true
	if 1 shl 3 > 5 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: bitwise-with-logical -->
```maxon
function main() returns ExitCode
	let a = 5 and 3        // 1
	if a > 0 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: bit-masking -->
```maxon
function main() returns ExitCode
	let flags = 5         // binary 101 (bit 0 and bit 2 set)
	return flags and 4    // returns 4 (bit 2 is set)
end 'main'
```
```exitcode
4
```

<!-- test: bit-clear -->
```maxon
function main() returns ExitCode
	var flags = 7        // binary 111
	// Clear bit 1 using xor
	flags = flags xor 2
	return flags         // 5 (binary 101)
end 'main'
```
```exitcode
5
```

<!-- test: power-of-two -->
```maxon
function main() returns ExitCode
	// Calculate 2^n using shift
	let n = 5
	return 1 shl n        // 32
end 'main'
```
```exitcode
32
```

<!-- test: divide-by-power-of-two -->
```maxon
function main() returns ExitCode
	// Divide by 4 using shift
	let value = 100
	return value shr 2    // 25
end 'main'
```
```exitcode
25
```

<!-- test: multiply-by-power-of-two -->
```maxon
function main() returns ExitCode
	// Multiply by 8 using shift
	let value = 13
	return value shl 3    // 104
end 'main'
```
```exitcode
104
```

<!-- test: bitwise-not-basic -->
```maxon
function main() returns ExitCode
	print("{not 0}\n")
	return 0
end 'main'
```
```stdout
-1
```
```exitcode
0
```

<!-- test: bitwise-not-value -->
```maxon
function main() returns ExitCode
	let a = 5
	print("{not a}\n")
	return 0
end 'main'
```
```stdout
-6
```
```exitcode
0
```

<!-- test: bitwise-not-double -->
```maxon
function main() returns ExitCode
	let a = 42
	return not not a
end 'main'
```
```exitcode
42
```

<!-- test: bitwise-not-masking -->
```maxon
function main() returns ExitCode
	let value = 125    // 0x7D
	// Clear lower 4 bits: 125 and not 15 = 112
	return value and not 15
end 'main'
```
```exitcode
112
```

<!-- test: bitwise-not-const -->
```maxon
let MASK = not 0xFF

function main() returns ExitCode
	print("{MASK}\n")
	return 0
end 'main'
```
```stdout
-256
```
```exitcode
0
```

<!-- test: shr-in-method-call-arg -->
```maxon
function main() returns ExitCode
	var buf = [0, 0]
	buf.push(42)
	let x = 0xABCD
	buf.push(x shr 9)
	return try buf.get(3) otherwise 0
end 'main'
```
```exitcode
85
```

<!-- test: shr-consecutive-method-calls -->
```maxon
function main() returns ExitCode
	var buf = [0]
	let value = 0xAABBCCDD
	buf.push(value and 0xFF)
	buf.push((value shr 8) and 0xFF)
	buf.push((value shr 16) and 0xFF)
	buf.push((value shr 24) and 0xFF)
	let b0 = try buf.get(1) otherwise 0
	let b1 = try buf.get(2) otherwise 0
	let b2 = try buf.get(3) otherwise 0
	let b3 = try buf.get(4) otherwise 0
	if b0 != 0xDD 'c0'
		return 10
	end 'c0'
	if b1 != 0xCC 'c1'
		return 20
	end 'c1'
	if b2 != 0xBB 'c2'
		return 30
	end 'c2'
	if b3 != 0xAA 'c3'
		return 40
	end 'c3'
	return 0
end 'main'
```
```exitcode
0
```


<!-- test: logical-or-and-on-bool-fields -->
`or` / `and` / `xor` are LOGICAL when their operands are `bool` (and bitwise on
ints). A logical word-op over `bool` struct fields produces a `bool`, so its
result flows into a `bool` parameter — the op must NOT silently type as `int`.
Here `flags.a or flags.b` and `flags.a and flags.c` are passed to
`consume(flag bool)`; both must type-check and dispatch the right branch.
```maxon
typealias Tag = int(0 to 100)

type Flags
	export var a as bool
	export var b as bool
	export var c as bool

	export static function make(a bool, b bool, c bool) returns Flags
		return Flags{a: a, b: b, c: c}
	end 'make'
end 'Flags'

function consume(flag bool) returns Tag
	if flag 'isTrue'
		return 1
	end 'isTrue'
	return 0
end 'consume'

function main() returns ExitCode
	let f = Flags.make(true, b: false, c: true)
	let orResult = consume(f.a or f.b)
	let andResult = consume(f.a and f.c)
	let bothFalse = consume(f.b or f.b)
	return orResult * 4 + andResult * 2 + bothFalse
end 'main'
```
```exitcode
6
```
