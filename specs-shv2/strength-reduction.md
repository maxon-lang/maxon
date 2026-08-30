---
feature: strength-reduction
status: experimental
keywords: [optimizer, codegen, division, remainder, magic-number, shift, idiv]
category: codegen
---
# Division by a constant, without a divide

## Documentation

`strengthReduceDivision` is the Std→Std pass that replaces `x / K` and `x mod K`, for a compile-time
constant `K`, with a multiply-and-shift sequence computing the same answer. An x64 `idiv` is 20–40
cycles and is not pipelined; the sequence is one `imul` and three or four single-cycle ALU ops, or —
for an unsigned power of two — a single `shr` or `and`.

### ⭐⭐ EVERY CASE HERE IS A DIFFERENTIAL TEST, AND THE ORACLE IS THE HARDWARE `idiv`

A table of expected quotients would be a second transcription of the same arithmetic, and a wrong
magic number would then have to be wrong in a way the table's author happened to anticipate. So each
case computes the answer **twice**: once by dividing by the LITERAL, which the pass reduces, and once
by dividing by a **runtime value of the same magnitude**, which it cannot see through and therefore
leaves as a real `idiv`. The two must agree.

The reference divisor's ranged type (`int(2 to …)`, `int(… to -2)`) is what keeps that reference a
*bare* divide: `Parser.emitDivOrMod` proves such a divisor non-zero and emits no guard, no throw and
no `try` — so the comparison is against the instruction itself and against nothing this pass wrote.

⚠ **The reference must be a real CALL, not an inlined body.** A reference small enough for
`inlineLeaves` to splice with its literal argument would have its divisor folded to a constant and be
reduced too — and the case would then compare a reduction against itself and pass however wrong both
were. The reference functions below carry a ranged-parameter guard, which keeps them out of the
budget; the committed fragments show `callDirect` at every reference site and `idivReg` inside every
reference body, which is what makes that visible rather than assumed.

### The dividends are the edges — and the edges are TWO different sets

`i64.min` and `i64.max` (the two ends), `0`, `±1`, `±2`, `±7`, `±10^9+7` and `-2^62` catch every
error in the emitted SEQUENCE. Truncation toward zero is what they are about: an arithmetic shift
rounds toward NEGATIVE INFINITY, so `-7 / 2` is `-3` for the language and `-4` for a bare `sar`, and
every negative dividend the divisor does not divide exactly is off by one without the bias
correction.

⛔⛔ **THEY CATCH NOTHING IN THE DERIVATION, AND THAT WAS MEASURED RATHER THAN ASSUMED.** With the
`anc` in `deriveSignedMagic` halved — or replaced by `i64.max` — every one of those cases stays
GREEN. A fixed-point reciprocal that is a little too coarse is right for almost every dividend and
wrong only near `anc`, the largest positive `n` that is `-1` modulo the divisor, which is exactly the
value the derivation's exit condition is stated in terms of. So there is a SECOND case below whose
dividends are `anc` and its neighbours, COMPUTED per divisor from a runtime `mod` the compiler cannot
fold. Without it this spec would test the sequence and take the constants on trust.

### The divisors it REFUSES, each for a correctness reason

`StdOp.div`/`mod` carry `isPure: false` because `idiv` FAULTS, and reducing one deletes that fault.
That is sound only where the fault provably cannot happen:

- **`0`** — the fault IS the answer. `x / 0` written in source is E3103 at compile time.
- **`|K| == 1`** — an algebraic identity rather than a strength reduction, and `x / -1` must keep
  faulting at `i64.min`, which `specs-shv2/division.md` pins as deliberate.
- **`i64.min`** — the derivation works in `|K|`, which is not representable.
- **an UNSIGNED divisor that is not a power of two** — the unsigned magic sequence needs a 65-bit
  multiplier and a fixup this rung did not build. Its answers must still be right, which is what the
  unsigned case below checks by including `10` beside `2` and `32`.
- ⛔ **an UNSIGNED divisor at or above `2^63`** — and this one is not about a deleted fault at all, it
  is the only refusal here that stands between the pass and a WRONG ANSWER. The pass reads a divisor
  as a signed `ParsedInt`, so `18446744073709551600` arrives as `-16`, whose *magnitude* is a power of
  two. `x /u 18446744073709551600` is `0` for every dividend below it; `x shrLogical 4` is not. FOUND
  by review rather than by any gate, and the case below is its pin — it was a live wrong answer, not a
  hypothetical, and it is reachable from source through a `let` whose declared type is
  `int(0 to u64.max)` and whose folded value has wrapped past `i64.max`.

### What the emitted code looks like

The committed fragments are the record. `x / 8` is `sar 63` / `shr 61` / `lea` / `sar 3`; `x /u 8` is
one `shr`; `x modu 8` is one `and`; `x / 10` is `mov rax, 7378697629483820647` / `imulHighReg` /
`sar 2` / `shr 63` / `lea`, and `x / 15` carries one more `lea` for the negative-multiplier fixup.

A `mod` is derived from its own quotient (`x - (x / |K|) * |K|`), so the two can never disagree about
a sign or an edge — and where a program computes BOTH by a power of two, the quotient chain is emitted
ONCE because CSE merges them. ⚠ **That merge does NOT happen for a magic divisor**, and the reason is
`EC13`'s filed one: there is no constant interning, so two sites mint two `const` ops for one 64-bit
multiplier and no expression comparison can call the two `mulHighSigned`s equal.

## Tests

<!-- test: a-constant-divisor-gives-the-same-answer-as-a-runtime-one -->
THE GATE. Twelve edge dividends against eleven divisors, chosen so that every arm of both sequences
is taken by at least one of them: powers of two; magic multipliers whose 64-bit value is POSITIVE
(`7`, `10`, `1000`, `28` — the last is what `graphemeBreakProperty` divides by); two whose value has
its TOP BIT SET and which therefore need the dividend added back (`15` and `100` — without that
correction the `imul` reads the multiplier as `M - 2^64` and every answer is wrong); a divisor whose
post-shift is ZERO so no shift op is emitted at all (`3`, reached here through `-3`); and two negative
divisors, which negate the quotient and leave the remainder alone.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias PosDivisor = int(2 to 1000000)
typealias NegDivisor = int(-1000000 to -2)

function refDiv(n Word, d PosDivisor) returns Word
	return n / d
end 'refDiv'

function refMod(n Word, d PosDivisor) returns Word
	return n mod d
end 'refMod'

function refDivNeg(n Word, d NegDivisor) returns Word
	return n / d
end 'refDivNeg'

function refModNeg(n Word, d NegDivisor) returns Word
	return n mod d
end 'refModNeg'

function edgeDividends() returns WordArray
	var ns = WordArray.create()
	ns.push(0)
	ns.push(1)
	ns.push(-1)
	ns.push(2)
	ns.push(-2)
	ns.push(7)
	ns.push(-7)
	ns.push(i64.max)
	ns.push(i64.min)
	ns.push(-4611686018427387904)
	ns.push(1000000007)
	ns.push(-1000000007)
	return ns
end 'edgeDividends'

function main() returns ExitCode
	for n in edgeDividends() 'each'
		if n / 2 != refDiv(n, d: 2) 'divTwo'
			return 1
		end 'divTwo'
		if n mod 2 != refMod(n, d: 2) 'modTwo'
			return 2
		end 'modTwo'
		if n / 8 != refDiv(n, d: 8) 'divEight'
			return 3
		end 'divEight'
		if n mod 8 != refMod(n, d: 8) 'modEight'
			return 4
		end 'modEight'
		if n / 10 != refDiv(n, d: 10) 'divTen'
			return 5
		end 'divTen'
		if n mod 10 != refMod(n, d: 10) 'modTen'
			return 6
		end 'modTen'
		if n / 7 != refDiv(n, d: 7) 'divSeven'
			return 7
		end 'divSeven'
		if n mod 28 != refMod(n, d: 28) 'modTwentyEight'
			return 8
		end 'modTwentyEight'
		if n / 1000 != refDiv(n, d: 1000) 'divThousand'
			return 9
		end 'divThousand'
		// `15` and `100` are the two whose magic has its top bit set — the `+ dividend` fixup arm.
		if n / 15 != refDiv(n, d: 15) 'divFifteen'
			return 10
		end 'divFifteen'
		if n mod 15 != refMod(n, d: 15) 'modFifteen'
			return 11
		end 'modFifteen'
		if n / 100 != refDiv(n, d: 100) 'divHundred'
			return 12
		end 'divHundred'
		// `-3` reaches magnitude 3, whose post-shift is 0 — the arm that emits no shift at all.
		if n / -3 != refDivNeg(n, d: -3) 'divMinusThree'
			return 13
		end 'divMinusThree'
		if n mod -3 != refModNeg(n, d: -3) 'modMinusThree'
			return 14
		end 'modMinusThree'
		if n / -15 != refDivNeg(n, d: -15) 'divMinusFifteen'
			return 15
		end 'divMinusFifteen'
		if n / -8 != refDivNeg(n, d: -8) 'divMinusEight'
			return 16
		end 'divMinusEight'
		if n mod -8 != refModNeg(n, d: -8) 'modMinusEight'
			return 17
		end 'modMinusEight'
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-hardest-dividend-for-a-divisor-is-the-one-the-derivation-is-ABOUT -->
⛔⛔ **THE CASE THAT WAS ADDED BECAUSE THE GATE ABOVE COULD NOT SEE A MIS-DERIVED MAGIC.** HALVE
`deriveSignedMagic`'s `anc` and every one of the other six cases stays GREEN — six cases over twelve
dividends and eleven divisors, all passing on a reciprocal that is provably too coarse. The reason is
exact: a fixed-point reciprocal that is slightly wrong is still right for almost every dividend, and
wrong only near the value the derivation is stated in terms of — **`anc`, the largest positive `n`
that is `-1` modulo the divisor.** A dividend list that does not contain it tests the EMITTED SEQUENCE
and takes the CONSTANT it carries on trust.

⚠ **AND THE OTHER DIRECTION IS NOT A BUG, WHICH IS WHY THE SABOTAGE HAS TO BE THE HALVING ONE.**
Setting `anc` to `i64.max` — too LARGE — leaves all seven cases green, and that is correct rather than
missed: the refinement then runs longer and stops at a LATER `p`, whose multiplier is still exact (the
loop finds the SMALLEST such `p`, not the only one), or it trips the `q1` bound and the site simply
declines. Only a too-SMALL `anc` exits early, and only an early exit is a wrong answer.

`anc` is different for every divisor, so it is COMPUTED here from the runtime divisor rather than
tabulated — which keeps this file free of magic numbers and makes the arithmetic the hardware's again.
With it, halving `anc` turns this case RED with exit 5.
```maxon
typealias Word = int(i64.min to i64.max)
typealias WordArray = Array with Word
typealias PosDivisor = int(2 to 1000000)

function refDiv(n Word, d PosDivisor) returns Word
	return n / d
end 'refDiv'

function refMod(n Word, d PosDivisor) returns Word
	return n mod d
end 'refMod'

// The largest positive `n` with `n mod d == d - 1`. Both divisions here read a RUNTIME divisor, so
// neither is reduced and neither can be folded — the value is the hardware's answer, not the pass's.
function hardestDividend(d PosDivisor) returns Word
	return i64.max - ((refMod(i64.max, d: d) + 1) mod d)
end 'hardestDividend'

function neighbourhood(d PosDivisor) returns WordArray
	let anc = hardestDividend(d)
	var ns = WordArray.create()
	ns.push(anc)
	ns.push(anc - 1)
	ns.push(anc + 1)
	ns.push(-anc)
	ns.push(-anc - 1)
	ns.push(-anc + 1)
	return ns
end 'neighbourhood'

function main() returns ExitCode
	for n in neighbourhood(3) 'three'
		if n / 3 != refDiv(n, d: 3) 'divThree'
			return 1
		end 'divThree'
		if n mod 3 != refMod(n, d: 3) 'modThree'
			return 2
		end 'modThree'
	end 'three'
	for n in neighbourhood(7) 'seven'
		if n / 7 != refDiv(n, d: 7) 'divSeven'
			return 3
		end 'divSeven'
		if n mod 7 != refMod(n, d: 7) 'modSeven'
			return 4
		end 'modSeven'
	end 'seven'
	for n in neighbourhood(10) 'ten'
		if n / 10 != refDiv(n, d: 10) 'divTen'
			return 5
		end 'divTen'
		if n mod 10 != refMod(n, d: 10) 'modTen'
			return 6
		end 'modTen'
	end 'ten'
	for n in neighbourhood(15) 'fifteen'
		if n / 15 != refDiv(n, d: 15) 'divFifteen'
			return 7
		end 'divFifteen'
		if n mod 15 != refMod(n, d: 15) 'modFifteen'
			return 8
		end 'modFifteen'
	end 'fifteen'
	for n in neighbourhood(28) 'twentyEight'
		if n / 28 != refDiv(n, d: 28) 'divTwentyEight'
			return 9
		end 'divTwentyEight'
		if n mod 28 != refMod(n, d: 28) 'modTwentyEight'
			return 10
		end 'modTwentyEight'
	end 'twentyEight'
	for n in neighbourhood(100) 'hundred'
		if n / 100 != refDiv(n, d: 100) 'divHundred'
			return 11
		end 'divHundred'
		if n mod 100 != refMod(n, d: 100) 'modHundred'
			return 12
		end 'modHundred'
	end 'hundred'
	for n in neighbourhood(1000) 'thousand'
		if n / 1000 != refDiv(n, d: 1000) 'divThousand'
			return 13
		end 'divThousand'
		if n mod 1000 != refMod(n, d: 1000) 'modThousand'
			return 14
		end 'modThousand'
	end 'thousand'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-signed-power-of-two-truncates-toward-zero -->
⭐ **THE ONE CASE A BARE SHIFT FAILS, ISOLATED.** `sar` rounds toward negative infinity and `/`
truncates toward zero, so every negative dividend the divisor does not divide exactly is off by one
without the sign bias. Removing that bias from `emitPowerOfTwoQuotient` turns this case RED with a
wrong answer while the gate above stays green for its non-negative dividends — the four values here
are chosen so no other case has to carry the argument.
```maxon
typealias Word = int(i64.min to i64.max)

function main() returns ExitCode
	if -7 / 2 != -3 'minusSevenHalved'
		return 1
	end 'minusSevenHalved'
	if -1 / 2 != 0 'minusOneHalved'
		return 2
	end 'minusOneHalved'
	if -9 / 8 != -1 'minusNineEighthed'
		return 3
	end 'minusNineEighthed'
	if -7 mod 2 != -1 'remainderTakesTheDividendSign'
		return 4
	end 'remainderTakesTheDividendSign'
	let n = i64.min as Word
	if n / 2 != -4611686018427387904 'minHalved'
		return 5
	end 'minHalved'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-unsigned-power-of-two-reads-the-whole-bit-pattern -->
⭐ The unsigned path has no sign correction at all — `x /u 2^k` is one `shr` and `x modu 2^k` is one
`and` — and its whole risk is the OTHER direction: a dividend above `i64.max` has its top bit set, so
answering it with the SIGNED sequence would give a negative quotient. The three dividends past
`i64.max` are built by wrapping arithmetic, because an integer literal is signed 64-bit and cannot
name them. `10` is included to check the divisor the pass REFUSES on this path still divides right.
```maxon
typealias Unsigned = int(0 to u64.max)
typealias UnsignedArray = Array with Unsigned
typealias UPosDivisor = int(2 to 1000000)

function refUDiv(n Unsigned, d UPosDivisor) returns Unsigned
	return n / d
end 'refUDiv'

function refUMod(n Unsigned, d UPosDivisor) returns Unsigned
	return n mod d
end 'refUMod'

function main() returns ExitCode
	var ns = UnsignedArray.create()
	ns.push(0)
	ns.push(1)
	ns.push(31)
	ns.push(9223372036854775807)
	let top = (9223372036854775807 as Unsigned) + 1
	ns.push(top)
	ns.push(top + 9223372036854775807)
	ns.push(top + 3122306832379791762)

	for u in ns 'each'
		if u / 2 != refUDiv(u, d: 2) 'divTwo'
			return 1
		end 'divTwo'
		if u mod 2 != refUMod(u, d: 2) 'modTwo'
			return 2
		end 'modTwo'
		if u / 32 != refUDiv(u, d: 32) 'divThirtyTwo'
			return 3
		end 'divThirtyTwo'
		if u mod 32 != refUMod(u, d: 32) 'modThirtyTwo'
			return 4
		end 'modThirtyTwo'
		if u / 10 != refUDiv(u, d: 10) 'divTenIsRefused'
			return 5
		end 'divTenIsRefused'
	end 'each'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: an-unsigned-divisor-above-the-signed-range-is-refused -->
⛔ **THE CASE THAT WAS A LIVE WRONG ANSWER.** `huge` is `2^64 - 16`; a `ParsedInt` holds it as `-16`,
whose magnitude is `16` — a power of two the pass would have answered with `shrLogical 4`. The right
answer is `0`, because the dividend is smaller than the divisor. Both operands' declared types are
`int(0 to u64.max)`, so the division is genuinely UNSIGNED, and the divisor is folded, so it genuinely
reaches the classifier as a constant. Restore the reduction for a negative unsigned divisor and this
case answers `576460752303423550`.
```maxon
typealias Unsigned = int(0 to u64.max)
typealias UPosDivisor = int(2 to u64.max)

function refUDiv(n Unsigned, d UPosDivisor) returns Unsigned
	return n / d
end 'refUDiv'

function refUMod(n Unsigned, d UPosDivisor) returns Unsigned
	return n mod d
end 'refUMod'

function main() returns ExitCode
	let huge = ((9223372036854775807 as Unsigned) + (9223372036854775793 as Unsigned)) as Unsigned
	let a = ((9223372036854775807 as Unsigned) + 1000) as Unsigned
	if a / huge != refUDiv(a, d: huge) 'quotient'
		return 1
	end 'quotient'
	if a mod huge != refUMod(a, d: huge) 'remainder'
		return 2
	end 'remainder'
	if a / huge != 0 'quotientIsZero'
		return 3
	end 'quotientIsZero'
	if a mod huge != a 'remainderIsTheDividend'
		return 4
	end 'remainderIsTheDividend'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-refused-divisors-keep-their-answers -->
The three magnitudes the pass declines, each still answered by the instruction it left in place.
`i64.min mod -1` is `0` — the parser's own overflow guard is what makes it so, and a reduction that
took `-1` would have had to reproduce it. `x / 1` and `x mod 1` are identities the pass deliberately
does not spell, and `x / i64.min` is `0` for every dividend but one.
```maxon
typealias Word = int(i64.min to i64.max)

function main() returns ExitCode
	let lo = i64.min as Word
	if lo mod -1 != 0 'minModMinusOne'
		return 1
	end 'minModMinusOne'
	if lo / 1 != lo 'divByOne'
		return 2
	end 'divByOne'
	if lo mod 1 != 0 'modByOne'
		return 3
	end 'modByOne'
	if lo / i64.min != 1 'minDividedByItself'
		return 4
	end 'minDividedByItself'
	if 7 / i64.min != 0 'smallDividedByMin'
		return 5
	end 'smallDividedByMin'
	if 7 mod i64.min != 7 'smallModMin'
		return 6
	end 'smallModMin'
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-reduced-division-still-meets-its-range-check -->
<!-- targets: x64-windows, x64-linux -->
CONTROL. The rewrite replaces the `div` op but keeps its RESULT VALUE ID, which is what the guard
`insertRangeChecks` had already emitted names. Give the reduced quotient back a value its ranged
typealias excludes and that guard must still fire, at the same line and out of the same frame — a
rewrite that minted a fresh result id instead would leave the guard reading a value nothing writes.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias Small = int(0 to 10)

function narrow(x Small) returns Small
	return x
end 'narrow'

function quotient(n Integer) returns Integer
	return n / 3
end 'quotient'

function main() returns ExitCode
	let big = quotient(1000)
	return narrow(big)
end 'main'
```
```exitcode
1
```
```stderr
panic at a-reduced-division-still-meets-its-range-check.test:5: Range check failed: value outside typealias 'Small'
Stack trace:
  in narrow
  in main
  in mrt_start
```
