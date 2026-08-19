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

**The LEFT operand's type decides how a right shift fills, and nothing else does.** This is Go's
rule: *arithmetic* (sign-propagating) when the left operand is signed, *logical* (zero-filling) when
it is unsigned. A shift is not symmetric in its operands — the right one is a **distance**, and its
type says nothing about how the shift fills.

A bare `int` is a signed 64-bit integer, so `shr` sign-propagates by default:

```maxon
var a = 16
var b = a shr 2       // 4       — a non-negative value fills with zeros either way
var c = (0 - 8) shr 1 // -4, not 9223372036854775804
```

**Maxon's unsigned type is a ranged alias whose low bound is 0.** `int(0 to u64.max)` is Maxon's
`uint64`: it holds values whose top bit is set while the type calls them non-negative. A right shift
of one **zero-fills**.

```maxon
typealias Wide = int(0 to u64.max)

function shiftIt(v Wide) returns Wide
	return v shr 60
end 'shiftIt'

shiftIt(u64.max)      // 15 — NOT -1. An unsigned left operand fills with zeros.
```

`int(0 to u64.max)` is the *only* range on which the two readings can be told apart: a value of
`int(0 to 100)` is provably non-negative, so an arithmetic and a logical shift of it agree bit for
bit. Any case that means to pin this rule has to use it — which is why `8 shr 2` cannot, and why
this went unnoticed in both compilers.

**A shift is 64 bits wide.** A ranged left operand decides a shift's *fill*; it never decides its
*width*. `x shl 29` on an `int(-2147483648 to 2147483647)` needs 61 bits to hold its answer, and it
gets them — the operand is not truncated to its declared type's storage size, and the count is not
masked to that size's shift width.


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

> ⚠ **The runtime panic is NOT YET IMPLEMENTED in maxon-shv2**: `emitGuardedShift` SATURATES a
> run-time negative count instead of raising, and making it raise is the outstanding work.
> ⚠ Its old companion citation is gone: `a / 0` no longer escapes as a raw hardware trap — A1 made
> division FALLIBLE (a constant zero divisor is E3103, a possibly-zero one throws `DivisionByZero`),
> so the two are no longer one blocker. (The retired `OPEN.md` this used to reference is also gone;
> `maxon-shv2/PLAN.md` owns what is outstanding.) Until the shift panic lands, a negative count that
> only appears at run time is *defined* rather than *diagnosed*: the guard reads it as out-of-range, so
> `x shl -1` is 0 and `x shr -1` is the sign. That is deterministic, and it is not the masked
> `x shl 63` this rule exists to kill — it is simply not yet the panic Go requires. The case is
> carried below as a `disabled-test`.

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
operators implement arithmetic shifts if the left operand is a signed integer". A bare Maxon `int`
IS signed, so `shr` sign-propagates on one. Unpinned, the bootstrap zero-filled — `(0-8) shr 60`
was 15 — while shv2 emitted `sar` and answered -1. shv2 was right *here*; nothing said so.

⚠ And "here" was the whole of it. The clause the rule turns on is *"if the left operand is a signed
integer"*, and shv2 read it as though every left operand were — see `shr-unsigned-operand-zero-fills`
below, which is the same sentence's OTHER half and which shv2 got wrong for exactly as long as this
one went unpinned.
```maxon
function main() returns ExitCode
	let neg = 0 - 8
	if neg shr 60 != 0 - 1 'sixtyWrong'
		return 1
	end 'sixtyWrong'
	if neg shr 1 != 0 - 4 'oneWrong'
		return 2
	end 'oneWrong'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shr-signedness-is-the-left-operand-only -->
A shift is NOT symmetric in its operands: the right one is a DISTANCE, and its declared type says
nothing about how the shift fills. Only the value being SHIFTED decides that.

This is pinned in BOTH suites because the BOOTSTRAP got it wrong: a shift took its optimal type from
`lhs ?? rhs`, so a count declared `int(0 to 63)` — an *unsigned* optimal type, and the most natural
way there is to declare a shift distance — reported the whole shift as unsigned and made a signed
`shr` ZERO-fill. `(0-8) shr 60` answered -1 for a plain count and **15** for that one.

⚠ This file used to claim shv2 "was right by construction (every int is a signed i64)". **That was
false, and it was false in the way that matters**: the sentence names the very premise —
*there is no unsigned integer type* — that `int(0 to u64.max)` refutes, and that shv2's parser
accepts. shv2 got the COUNT's signedness right for the same reason it got the OPERAND's wrong: it
never asked. "Right by construction" is exactly the claim that rots unpinned, and this is what it
rotted into.
```maxon
typealias Num = int(i64.min to i64.max)
typealias ShiftBits = int(0 to 63)

function shiftRight(value Num, distance ShiftBits) returns Num
	return value shr distance
end 'shiftRight'

function main() returns ExitCode
	if shiftRight(0 - 8, distance: 60) != 0 - 1 'rangedDistance'
		return 1
	end 'rangedDistance'
	if shiftRight(0 - 1, distance: 63) != 0 - 1 'allSign'
		return 2
	end 'allSign'
	if shiftRight(8, distance: 2) != 2 'nonNegative'
		return 3
	end 'nonNegative'
	return 42
end 'main'
```
```exitcode
42
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
	if a shl 64 != 0 'wrong'
		return 1
	end 'wrong'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shl-count-100 -->
The commit message's own example — `1 shl 100` — through both a named constant and a literal.
Masked, it was `1 shl 36` = 68719476736.
```maxon
function main() returns ExitCode
	let a = 1
	let SHIFT = 100
	if a shl SHIFT != 0 'namedWrong'
		return 1
	end 'namedWrong'
	if a shl 100 != 0 'literalWrong'
		return 2
	end 'literalWrong'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shr-count-past-width -->
A right shift past the width saturates to the SIGN, not to zero — because a sign-filling shift that
moves every bit out leaves the sign behind. `x shr 63` already IS the sign, which is why the
compiler saturates the COUNT here rather than selecting the result.
```maxon
function main() returns ExitCode
	if (0 - 1) shr 100 != 0 - 1 'negWrong'
		return 1
	end 'negWrong'
	if 8 shr 100 != 0 'posWrong'
		return 2
	end 'posWrong'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shift-count-arithmetic -->
A count the compiler can fold is a count it can check, however it is spelled. All three of these
are a count of 64, and none of them is a literal.
```maxon
function main() returns ExitCode
	let a = 1
	if a shl 63 + 1 != 0 'addWrong'
		return 1
	end 'addWrong'
	if a shl 2 * 32 != 0 'mulWrong'
		return 2
	end 'mulWrong'
	if a shl 128 / 2 != 0 'divWrong'
		return 3
	end 'divWrong'
	return 42
end 'main'
```
```exitcode
42
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
	if a shl count != 0 'wrong'
		return 1
	end 'wrong'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shift-by-parameter-count -->
The same, with a count NO pass can see: a parameter. This is the case the GUARD exists for —
unguarded, 65 masks to 1 and `7 shl 65` is 14; 100 masks to 36. Every answer here matches the
constant-folded one above, which is the whole point: one rule, two readings.
```maxon
typealias Num = int(i64.min to i64.max)

function shiftLeft(value Num, count Num) returns Num
	return value shl count
end 'shiftLeft'

function shiftRight(value Num, count Num) returns Num
	return value shr count
end 'shiftRight'

function main() returns ExitCode
	if shiftLeft(7, count: 65) != 0 'shlPastWidth'
		return 1
	end 'shlPastWidth'
	if shiftLeft(7, count: 3) != 56 'shlInRange'
		return 2
	end 'shlInRange'
	if shiftRight(0 - 1, count: 100) != 0 - 1 'shrNegPastWidth'
		return 3
	end 'shrNegPastWidth'
	if shiftRight(8, count: 100) != 0 'shrPosPastWidth'
		return 4
	end 'shrPosPastWidth'
	if shiftRight(0 - 8, count: 1) != 0 - 4 'shrInRange'
		return 5
	end 'shrInRange'
	return 42
end 'main'
```
```exitcode
42
```

<!-- disabled-test: shift-by-negative-runtime-count-panics -->
⚠ BLOCKED ON THE SHIFT GUARD RAISING. Go: "if the shift count is negative at run time, a run-time
panic occurs". The bootstrap does exactly that (`specs/bitwise-operators.md` pins it). **maxon-shv2
does not yet**: `emitGuardedShift` saturates the count instead, so the answer is defined and the
DIAGNOSTIC is what is missing.

⚠ Its old companion — `OPEN.md` #2, `a / 0` escaping as a raw `0xC0000094` hardware trap — is
**CLOSED**, and by a different mechanism than a panic: A1 made division fallible in the TYPE system
(E3103 for a constant zero divisor, a thrown `DivisionByZero` for a possibly-zero one), so there is no
trap left to route. The two were never one blocker; only the retired `OPEN.md` entry made them look
like one.

Meanwhile the answer here is 0, not a masked `4 shl 63`: the guard reads a negative count as
out-of-range, exactly as it reads 64. So this is a MISSING DIAGNOSTIC, not a wrong answer.

**Enable this test in the rung that lands the panic runtime.**
```maxon
typealias Num = int(i64.min to i64.max)

function shiftLeft(value Num, count Num) returns Num
	return value shl count
end 'shiftLeft'

function main() returns ExitCode
	let bad = 0 - 1
	return shiftLeft(4, count: bad)
end 'main'
```
```stderr
panic at shift-by-negative-runtime-count-panics.test:5: negative shift count
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

<!-- test: shr-unsigned-operand-zero-fills -->
⭐ **THE CASE THAT DISCRIMINATES.** `u64.max shr 60` is **15** under the logical reading and **-1**
under the arithmetic one, so it is the one shift that can tell the two compilers apart — and until
it was written down, they *were* apart: the bootstrap answered 15 and shv2 answered -1, for this
program.

`8 shr 2` cannot catch this, and neither can any shift of a value that fits in 63 bits: such a value
is non-negative, so both readings agree bit for bit and **the test passes under the wrong rule**. A
spec that cannot fail is what let this through, and `int(0 to u64.max)` is the only range that fixes
that.

The count is shifted both as a **constant** (which the compiler folds) and as a **parameter** (which
it cannot see, and must guard at run time). Both must give 15: the fold and the codegen are two
readings of one rule, and a rule with two readers is a rule that will be read two ways.
```maxon
typealias Wide = int(0 to u64.max)
typealias Num = int(i64.min to i64.max)

function shrConst(v Wide) returns Wide
	return v shr 60
end 'shrConst'

function shrDynamic(v Wide, d Num) returns Wide
	return v shr d
end 'shrDynamic'

function shrPastWidth(v Wide) returns Wide
	return v shr 64
end 'shrPastWidth'

function main() returns ExitCode
	// An UNSIGNED left operand zero-fills. Folded count, then a count the compiler cannot see.
	if shrConst(u64.max) != 15 'foldedCount'
		return 1
	end 'foldedCount'
	if shrDynamic(u64.max, d: 60) != 15 'dynamicCount'
		return 2
	end 'dynamicCount'

	// Zero-filling, so shifting every bit out leaves 0 — NOT the sign, and NOT the masked shift
	// the hardware would have performed.
	if shrPastWidth(u64.max) != 0 'foldedPastWidth'
		return 3
	end 'foldedPastWidth'
	if shrDynamic(u64.max, d: 100) != 0 'dynamicPastWidth'
		return 4
	end 'dynamicPastWidth'

	// A count of 0 is the identity, under either fill.
	if shrDynamic(u64.max, d: 0) != u64.max 'zeroCount'
		return 5
	end 'zeroCount'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shr-signed-operand-still-sign-fills -->
The other half of the same sentence, and the reason the unsigned case above is a *narrowing* of the
rule rather than a reversal of it: a **signed** left operand still shifts arithmetically. Both
compilers must keep answering -1 here while answering 15 above — the operand's type is the only
thing that changed.
```maxon
typealias Num = int(i64.min to i64.max)

function shrConst(v Num) returns Num
	return v shr 60
end 'shrConst'

function shrDynamic(v Num, d Num) returns Num
	return v shr d
end 'shrDynamic'

function shrPastWidth(v Num) returns Num
	return v shr 100
end 'shrPastWidth'

function main() returns ExitCode
	if shrConst(0 - 8) != 0 - 1 'foldedCount'
		return 1
	end 'foldedCount'
	if shrDynamic(0 - 8, d: 60) != 0 - 1 'dynamicCount'
		return 2
	end 'dynamicCount'

	// A sign-filling shift saturates to the SIGN, not to 0: `x shr 63` already IS the sign.
	if shrPastWidth(0 - 8) != 0 - 1 'foldedPastWidth'
		return 3
	end 'foldedPastWidth'
	if shrDynamic(0 - 8, d: 100) != 0 - 1 'dynamicPastWidth'
		return 4
	end 'dynamicPastWidth'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shl-zero-fills-whatever-the-operand-signedness -->
A LEFT shift vacates the *low* bits and always fills them with zeros — an unsigned left operand
changes nothing about it. Pinned so that "the left operand's type decides the fill" is not
over-applied to the one shift whose fill is not in question.
```maxon
typealias Wide = int(0 to u64.max)
typealias Num = int(i64.min to i64.max)

function shlDynamic(v Wide, d Num) returns Wide
	return v shl d
end 'shlDynamic'

function shlPastWidth(v Wide) returns Wide
	return v shl 64
end 'shlPastWidth'

function main() returns ExitCode
	if shlDynamic(u64.max, d: 63) != i64.min 'topBitOnly'
		return 1
	end 'topBitOnly'
	if shlPastWidth(u64.max) != 0 'everyBitOut'
		return 2
	end 'everyBitOut'
	if shlDynamic(u64.max, d: 100) != 0 'everyBitOutDynamic'
		return 3
	end 'everyBitOutDynamic'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shift-signedness-survives-a-branch -->
⚠ A shift's fill is a property of its LEFT OPERAND'S TYPE, so it cannot depend on **which block the
operand is read in**. It did, in the bootstrap: reading a variable outside the block that assigned
it mints a fresh SSA value, and the compiler resolved a ranged type by *searching for a variable
currently bound to that value* — which found nothing, concluded "not a ranged type", and therefore
"signed". `u64.max shr 60` was **15** in the entry block and **-1** after an `if`.

The same shift, on the same variable, in two blocks. It must give one answer.
```maxon
typealias Wide = int(0 to u64.max)
typealias Num = int(i64.min to i64.max)

function afterBranch(v Wide, flag bool) returns Wide
	if flag 'taken'
		return v shr 60
	end 'taken'
	return 0
end 'afterBranch'

function insideLoop(v Wide, n Num) returns Wide
	var acc = 0
	var i = 0
	while i < n 'each'
		acc = v shr 60
		i = i + 1
	end 'each'
	return acc
end 'insideLoop'

function main() returns ExitCode
	if afterBranch(u64.max, flag: true) != 15 'branch'
		return 1
	end 'branch'
	if insideLoop(u64.max, n: 3) != 15 'loop'
		return 2
	end 'loop'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shift-ranged-operand-is-64-bit -->
⭐ **A RANGED LEFT OPERAND DECIDES A SHIFT'S FILL, NEVER ITS WIDTH.** The compiler used to conflate
the two: a shift whose operands fit a narrow ranged type was lowered as a **32-bit** op, which
truncated the shift's *value*. `(0-8) shl 29` on an `int(-2147483648 to 2147483647)` needs 61 bits
to hold its answer and got 32, so it answered **0** — while the identical shift by a count the
compiler could not fold (and so lowered at 64 bits) answered **-4294967296**.

**Same value, same count, same program, two answers.** The folded path and the emitted path had
become two opinions.

Nothing in the suite shifted a ranged operand, which is why nothing caught it: every existing shift
case uses a bare `int`, and a bare `int` never narrows. Each case below therefore compares the two
paths *against each other* — a folded count against a count passed as a parameter — so it fails if
they ever diverge again, whatever they diverge to.
```maxon
typealias I32 = int(-2147483648 to 2147483647)
typealias U32 = int(0 to 4294967295)
typealias Num = int(i64.min to i64.max)

function shlBy(v Num, d Num) returns Num
	return v shl d
end 'shlBy'

function shrBy(v Num, d Num) returns Num
	return v shr d
end 'shrBy'

function main() returns ExitCode
	// NARROW SIGNED. The shift moves bits out of the declared type's width; that is legal, and the
	// answer needs all 64.
	let narrow = 0 - 8 as I32
	if narrow shl 29 != shlBy(0 - 8, d: 29) 'signedFoldVsEmitted'
		return 1
	end 'signedFoldVsEmitted'
	if narrow shl 29 != 0 - 4294967296 'signedValue'
		return 2
	end 'signedValue'

	// A count above the narrow type's width. A 32-bit shift instruction takes its count mod 32, so
	// `shr 33` would have quietly become `shr 1`.
	if narrow shr 33 != shrBy(0 - 8, d: 33) 'signedCountFoldVsEmitted'
		return 3
	end 'signedCountFoldVsEmitted'
	if narrow shr 33 != 0 - 1 'signedCountValue'
		return 4
	end 'signedCountValue'

	// NARROW UNSIGNED. Zero-filling, and still 64 bits wide.
	let wide = 4294967295 as U32
	if wide shl 33 != shlBy(4294967295, d: 33) 'unsignedFoldVsEmitted'
		return 5
	end 'unsignedFoldVsEmitted'
	if wide shr 4 != shrBy(4294967295, d: 4) 'unsignedShrFoldVsEmitted'
		return 6
	end 'unsignedShrFoldVsEmitted'
	if wide shr 4 != 268435455 'unsignedShrValue'
		return 7
	end 'unsignedShrValue'

	// A bare `int`, which never narrowed and so was always right. Here to show the ranged cases now
	// agree with it rather than merely with each other.
	let plain = 0 - 8 as Num
	if plain shl 29 != narrow shl 29 'rangedAgreesWithPlain'
		return 8
	end 'rangedAgreesWithPlain'

	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shift-signedness-follows-the-cast -->
⚠ A cast to a ranged type emits no op, so `let w = p as Wide` leaves `w` and `p` **sharing one SSA
value** with two different declared types. The bootstrap resolved a shift's ranged type by searching
for a variable bound to that value, and returned whichever the search reached first — `p`'s. So
`w shr 60` sign-filled a type declared unsigned, and answered **-1** where the same shift on a
parameter declared `Wide` answered **15**.

The cast is the whole of what the programmer wrote to say "treat this as unsigned". It has to be
what the shift reads.
```maxon
typealias Wide = int(0 to u64.max)
typealias Num = int(i64.min to i64.max)

function viaCast(p Num) returns Wide
	let w = p as Wide
	return w shr 60
end 'viaCast'

function viaParam(w Wide) returns Wide
	return w shr 60
end 'viaParam'

function main() returns ExitCode
	if viaParam(u64.max) != 15 'declaredParam'
		return 1
	end 'declaredParam'
	if viaCast(u64.max) != 15 'castLocal'
		return 2
	end 'castLocal'
	if viaCast(u64.max) != viaParam(u64.max) 'twoRoutesOneType'
		return 3
	end 'twoRoutesOneType'
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: shift-signedness-follows-the-cast-in-both-directions -->
The case above pins that the CAST decides the fill; these two pin what that costs, and shv2 got both
wrong in opposite directions for the same reason — `as` produced no value at all, so `w` and `p`
shared one SSA value and one tag.

**The SOURCE keeps its own type.** Making the cast decide the fill must not make it decide the
source's: after `let w = p as Wide`, `p` is still `Num`, and `p shr 60` must still sign-fill. An
in-place re-tag passes `shift-signedness-follows-the-cast` and fails here, which is exactly why that
case cannot pin this one — `15 - (-1)` is 16, `15 - 15` is 0, and `-1 - (-1)` is 0 too.

**And the cast runs the other way.** `v as Num` on an unsigned parameter has to make the shift
*sign*-fill, not merely stop it zero-filling: a rule stated only as "an unsigned target zero-fills"
leaves the unsigned→signed direction reading the SOURCE, and shv2 answered 15 where the bootstrap
answered -1.
```maxon
typealias Wide = int(0 to u64.max)
typealias Num = int(i64.min to i64.max)

function sourceKeepsItsOwnType(p Num) returns Num
	let w = p as Wide
	return (w shr 60) - (p shr 60)
end 'sourceKeepsItsOwnType'

function castToSigned(v Wide) returns Num
	let n = v as Num
	return n shr 60
end 'castToSigned'

function main() returns ExitCode
	if sourceKeepsItsOwnType(u64.max) != 16 'sourceKeeps'
		return 1
	end 'sourceKeeps'
	if castToSigned(u64.max) != 0 - 1 'toSigned'
		return 2
	end 'toSigned'
	return 42
end 'main'
```
```exitcode
42
```
