---
feature: float-negative-zero
status: stable
keywords: [float, ieee754, negative-zero, unary-minus, literal, sign-bit]
category: expressions
---

# Negative zero

## Documentation

IEEE-754 has **two** zeros, and `-0.0` is the one with the sign bit set. They compare
EQUAL to each other, so `==` can never tell them apart — but they are not
interchangeable: `1.0 / -0.0` is `-inf` while `1.0 / 0.0` is `+inf`.

That makes `-0.0` the one literal for which "negate the value" and "set the sign bit" give
different answers, and the reason a leading `-` on a float literal is folded INTO the
literal (`Parser.parseNegatedNumericLiteral`, via `negatedFloatBits`) rather than compiled
as an operator over it. Float negation over a non-literal is still `0.0 - x`, which x64
spells with the `subsd` already in the instruction set; `0.0 - 0.0` is `+0.0`, so a literal
`-0.0` compiled that way silently lost its sign.

## Tests

### A literal `-0.0` keeps its sign

The reciprocal is what makes the sign observable without printing: `1.0 / -0.0` is `-inf`
and `1.0 / 0.0` is `+inf`, so the comparison is true only if the literal really carried the
sign bit. Compiled as `0.0 - 0.0` both sides are `+inf` and the comparison is false.

⚠⚠ **THIS CASE IS INVALIDATED BY THE RULING, NOT BLOCKED BY A MISSING MECHANISM, AND NOTHING
LATER WILL RE-ENABLE IT (A1).** The paragraph below already documented the divergence it now
dies of: "the bootstrap rejects an unguarded `a / b` over floats with E3057 … and wrapping it
in `try … otherwise` substitutes a value for the infinity the test is looking at." shv2 has
adopted that rule, so `1.0 / x` over a possibly-zero `x` is a throwing operation here too and
`x / 0.0` is an error rather than an infinity — the behaviour this case observes no longer
exists in the language. There is no ranged type that would restore it either: the reciprocal
must accept a ZERO to have a sign to reveal, and a range containing zero is exactly what makes
the divide fallible.

⚠ **The FILE's subject survives intact and every other case in it stays green.** What is lost
is only the RECIPROCAL as the instrument: `-0.0`'s sign bit is still pinned by the negated-literal,
field-default, range-bound and interpolation cases below. Observing the sign of a zero DIRECTLY
needs `print` of a signed infinity, or a `floatToBits` builtin shv2 does not expose to user code.

<!-- disabled-test: negative-zero-literal-keeps-its-sign -->
<!-- NEVER — invalidated by A1's ruling, not deferred by it. See the two ⚠ paragraphs above. -->
```maxon
typealias Wide = float(f64.min to f64.max)

function reciprocal(x Wide) returns Wide
	return 1.0 / x
end 'reciprocal'

function main() returns ExitCode
	let negZero = -0.0
	let posZero = 0.0
	if reciprocal(negZero) < reciprocal(posZero) 'signed'
		return 42
	end 'signed'
	return 0
end 'main'
```
```exitcode
42
```

### Negating a float VARIABLE is still an arithmetic negation

`-x` over a value the compiler cannot see is `0.0 - x`, which is correct for every input
except a positive zero — the trade x64 makes to avoid materializing a sign-bit mask in
`.rdata`. Pinned here so the literal fold above is not mistaken for a change to it.

<!-- test: negating-a-float-variable -->
```maxon
function main() returns ExitCode
	let f = 3.5
	let g = -f
	return trunc(g + 45.5)
end 'main'
```
```exitcode
42
```
