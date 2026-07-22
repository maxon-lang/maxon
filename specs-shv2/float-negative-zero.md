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

⚠ This case is shv2-only in the sense that the C# bootstrap cannot express it: the
bootstrap rejects an unguarded `a / b` over floats with E3057 (its division-by-zero rule
does not exempt the float domain, where IEEE-754 defines the result), and wrapping it in
`try … otherwise` substitutes a value for the infinity the test is looking at. The expected
answer here is IEEE-754's, not a transcription of the oracle's.

<!-- test: negative-zero-literal-keeps-its-sign -->
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
