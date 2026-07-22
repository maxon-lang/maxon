---
feature: float-literal-magnitude
status: stable
keywords: [float, literal, exponent, scientific-notation, subnormal, ieee754, overflow, E2011]
category: expressions
---

# Float Literal Magnitude

## Documentation

A float literal is converted to its IEEE-754 binary64 bits at PARSE time, by exact rational
arithmetic: the digits and the decimal exponent give a numerator and a denominator, and the
significand is long-divided out of them with round-to-nearest-even. "Exact" is the whole
requirement — the scaled numerator of `1.0e300` is a 1000-bit integer, so the arithmetic has to be
wide enough to hold one.

The magnitudes a literal may name run from the least positive subnormal (~`4.94e-324`) to
`f64.max` (~`1.7977e308`):

- a literal above `f64.max` is **E2011**, `Float literal out of range`;
- a literal below half the least subnormal is silently `0.0` — that is not an error, it is what
  round-to-nearest gives, and it is what `strtod` returns;
- a literal in between is representable, and one below `2^-1022` is representable **subnormally**:
  the exponent field pins at zero and the significand loses one bit per further halving.

```maxon
let big = 1.0e300      // finite
let small = 1.0e-320   // finite, subnormal
let zero = 1.0e-400    // exactly 0.0, no diagnostic
```

The conversion is **correctly rounded for every input**, not merely for short ones. A literal may
carry any number of significant digits; the first 768 are kept and anything past them is dropped,
but the fact that a dropped digit was non-zero is remembered. That is enough, and the bound is not a
guess: a rounding boundary for a double is a midpoint `m * 2^e` with `e >= -1075`, so written out in
full it terminates within 752 significant digits — a 768-digit prefix therefore either sits exactly
on a midpoint (where the remembered bit decides) or is far enough from one that the dropped tail
cannot reach it.

The corpus otherwise never leaves `e3` — `specs/literals.md`'s largest exponent is `1.0E3` — so
nothing before this file reached the scaling at all. What it was hiding: the scaling ran in an
i64, and `10^k` carries the factor `2^k`, so `10^19` wrapped, `10^63` landed on `i64.min`
(negative, which sent the normalization loop doubling a negative value forever and **hung the
compiler**), and `10^64` was exactly zero. `1.0e300 / 1.0e299` evaluated to `0`.

**Every expected value in this file was taken from the C# bootstrap**, which parses a float literal
with `double.Parse` and therefore delegates to a correctly-rounded `strtod`. It is the oracle here;
none of these numbers were derived by hand.

## Tests

<!-- test: ratio-of-adjacent-decades-e20 -->
`10^20` needs 67 bits. In an i64 the numerator wrapped, and the quotient came out `2966733824`.
```maxon
function main() returns ExitCode
	let a = 1.0e20
	let b = 1.0e19
	return trunc(a / b)
end 'main'
```
```exitcode
10
```

<!-- test: ratio-of-adjacent-decades-e63 -->
`10^63` is the pathological one: in an i64 it is exactly `i64.min`, and a NEGATIVE numerator made
the normalization loop double a negative value with no fixed point to reach — the compiler spun
forever on this literal.
```maxon
function main() returns ExitCode
	let a = 1.0e63
	let b = 1.0e62
	return trunc(a / b)
end 'main'
```
```exitcode
10
```

<!-- test: ratio-of-adjacent-decades-e64 -->
`10^64` is exactly zero in an i64 — `1.0e64` silently became `0.0`.
```maxon
function main() returns ExitCode
	let a = 1.0e64
	let b = 1.0e63
	return trunc(a / b)
end 'main'
```
```exitcode
10
```

<!-- test: ratio-of-adjacent-decades-negative-exponent -->
The negative side scales the DENOMINATOR, and wrapped the same way; this quotient came out `9`.
```maxon
function main() returns ExitCode
	let a = 1.0e-30
	let b = 1.0e-31
	return trunc(a / b)
end 'main'
```
```exitcode
10
```

<!-- test: large-magnitudes-relate-correctly -->
Four independent facts about the top of the range, one bit each: `1.0e300` and `1.0e-300` are
positive; `1.0e150` squared is the same decade as `1.0e300`; and a value times its reciprocal is
one. A conversion that merely produced *some* large number would fail the last two.
```maxon
function main() returns ExitCode
	var r = 0
	if 1.0e300 > 0.0 'positiveBig'
		r = r + 1
	end 'positiveBig'
	if 1.0e-300 > 0.0 'positiveSmall'
		r = r + 2
	end 'positiveSmall'
	if 1.0e150 * 1.0e150 > 1.0e300 / 2.0 'squareIsSameDecade'
		r = r + 4
	end 'squareIsSameDecade'
	if 1.0e300 * 1.0e-300 > 0.5 'reciprocalIsOne'
		r = r + 8
	end 'reciprocalIsOne'
	return r
end 'main'
```
```exitcode
15
```

<!-- test: subnormals -->
`1.0e-310` and `1.0e-320` are below `2^-1022`, so neither has a normal encoding: the exponent field
is zero and the leading one is spelled out inside the significand. Both are distinct from zero and
ordered.
```maxon
function main() returns ExitCode
	var r = 0
	if 1.0e-310 > 0.0 'e310IsPositive'
		r = r + 1
	end 'e310IsPositive'
	if 1.0e-320 > 0.0 'e320IsPositive'
		r = r + 2
	end 'e320IsPositive'
	if 1.0e-310 > 1.0e-320 'ordered'
		r = r + 4
	end 'ordered'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: underflow-to-zero-is-not-an-error -->
`1.0e-400` is under half the least subnormal, so round-to-nearest gives `+0`. The bootstrap accepts
it silently — an underflow is a representable answer, unlike an overflow.
```maxon
function main() returns ExitCode
	if 1.0e-400 == 0.0 'roundsToZero'
		return 1
	end 'roundsToZero'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: f64-max-is-representable -->
`1.7976931348623157e308` is `f64.max` exactly: 17 significant digits, a binary exponent of 1023 and
a significand of all ones. It must round to the largest finite double and not to infinity — and
doubling it must then leave it, which is the check that it did not silently arrive as infinity
already.
```maxon
function main() returns ExitCode
	var r = 0
	if 1.7976931348623157e308 > 1.0e308 'aboveOneE308'
		r = r + 1
	end 'aboveOneE308'
	if 1.7976931348623157e308 * 2.0 > 1.7976931348623157e308 'doublingEscapes'
		r = r + 2
	end 'doublingEscapes'
	if 1.0e308 * 10.0 > 1.7976931348623157e308 'tenTimesOverflows'
		r = r + 4
	end 'tenTimesOverflows'
	return r
end 'main'
```
```exitcode
7
```

<!-- test: error.just-above-f64-max -->
`1.8e308` is above `f64.max` but its floor(log10) is 308, so the decimal magnitude window admits it
and only the CONVERTED value can reject it. Reaching the infinity pattern is the rejection.
```maxon
function main() returns ExitCode
	let x = 1.8e308
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/float-literal-magnitude/error.just-above-f64-max.test:3:10: Float literal out of range (a float is an IEEE-754 double; its magnitude cannot exceed f64.max)
```

<!-- test: error.exponent-far-above-range -->
```maxon
function main() returns ExitCode
	let x = 1.0e999
	return 0
end 'main'
```
```maxoncstderr
error E2011: specs/fragments/float-literal-magnitude/error.exponent-far-above-range.test:3:10: Float literal out of range (a float is an IEEE-754 double; its magnitude cannot exceed f64.max)
```

<!-- test: zero-mantissa-ignores-a-wild-exponent -->
`0.0e999` has no significant digit, so it is zero however it is scaled — the magnitude guard must
not see it as an overflow.
```maxon
function main() returns ExitCode
	if 0.0e999 == 0.0 'isZero'
		return 1
	end 'isZero'
	return 0
end 'main'
```
```exitcode
1
```

<!-- test: range-alias-bounds-scale-too -->
A float typealias decodes its bounds through the same converter (`parseRangeBound`), so a bound
written in scientific notation has to scale correctly as well.
```maxon
typealias Astronomical = float(1.0e-30 to 1.0e30)

function main() returns ExitCode
	let x = 1.0e20 as Astronomical
	let y = 1.0e19 as Astronomical
	return trunc(x / y)
end 'main'
```
```exitcode
10
```

<!-- test: correctly-rounded-past-eighteen-digits -->
A literal carrying MORE significant digits than a double can distinguish must still land on the
correctly-rounded double — the same one its shortest round-trip spelling names. Each comparison here
puts a long literal against a <=17-digit form of the same value, so the short side was always exact
and only the long side is under test.

Regression for a real wrong answer: while the converter kept just 18 significant digits and dropped
the rest, the first three of these landed a full ULP off and this returned 24 instead of 31.
```maxon
function main() returns ExitCode
	var r = 0
	if 7.46658662666203984963311900685e13 == 74665866266620.4 'thirtyDigits'
		r = r + 1
	end 'thirtyDigits'
	if 2.3744998132743940991053240e-19 == 2.3744998132743943e-19 'twentyFiveDigits'
		r = r + 2
	end 'twentyFiveDigits'
	if 2.755371525302782724e-4 == 0.0002755371525302783 'nineteenDigits'
		r = r + 4
	end 'nineteenDigits'
	if 3.14159265358979323846 == 3.141592653589793 'piAsWrittenInTheTree'
		r = r + 8
	end 'piAsWrittenInTheTree'
	if 2.71828182845904523536 == 2.718281828459045 'eAsWrittenInTheTree'
		r = r + 16
	end 'eAsWrittenInTheTree'
	return r
end 'main'
```
```exitcode
31
```

<!-- test: over-budget-digits-round-through-the-sticky-bit -->
`9007199254740993` is 2^53 + 1 — EXACTLY the midpoint between two adjacent doubles, so it is the one
place a dropped digit can change the answer. Both literals below carry ~796 significant digits, far
past the 768 the converter keeps, so their tails are dropped and nothing survives them but the
record that a non-zero digit WAS dropped. The first is exactly the midpoint and must round to even
(down); the second differs from it only in its 796th digit and must round up. Telling those two
apart is the entire job of that one bit.
```maxon
function main() returns ExitCode
	var r = 0
	if 9007199254740993.000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000 == 9007199254740992.0 'tieRoundsToEven'
		r = r + 1
	end 'tieRoundsToEven'
	if 9007199254740993.000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001 == 9007199254740994.0 'stickyBitRoundsUp'
		r = r + 2
	end 'stickyBitRoundsUp'
	return r
end 'main'
```
```exitcode
3
```
