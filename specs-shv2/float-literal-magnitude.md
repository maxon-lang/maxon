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
