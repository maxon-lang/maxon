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

That makes a zero the one value for which "subtract from zero" and "flip the sign bit" give
different answers: `0.0 - 0.0` is `+0.0`, while negation is a SIGN-BIT FLIP and answers `-0.0`.
Maxon's `-` is the flip, everywhere it can appear. A leading `-` on a float literal is folded INTO
the literal (`Parser.parseNegatedFloatLiteral`, via `negatedFloatBits`); `-x` over a variable, a
parameter or a top-level constant is a genuine negation (`MaxonUnaryOp.fneg` — arm64 `fneg`, wasm
`f64.neg`, and on x64 an `xorpd` against a sign mask, `TargetOp.negF64RegReg`). Until 2026-08-30 the
variable form was compiled as `0.0 - x`, so `let z = 0.0; print("{-z}")` printed `0.0` and
`Json.parse("-0")` answered `+0.0`; the cases below pin the flip at every entrance.

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
field-default, range-bound and interpolation cases below. Observing the sign of a zero DIRECTLY is
done with `__Builtins.floatToBits`, which user code MAY call — see `specs-shv2/atan2.md`'s
`atan2.signed-zero`, which pins seven signed-zero argument combinations, the three with a zero RESULT
through that builtin. (An earlier revision of this
paragraph said shv2 does not expose that builtin to user code. It was wrong, and it is corrected
here rather than deleted because it is the sentence a reader reaches for when deciding whether a
sign of zero is observable at all.) **The verdict above is untouched by that correction**: the case
below is invalidated because E3057 makes `1.0 / x` a throwing operation, not because its result
could not be observed — so no new instrument re-enables it.

<!-- disabled-test: negative-zero-literal-keeps-its-sign -->
<!-- ⚠ RE-SHELVED WITH ITS REAL REASON. It was filed as blocked on float interpolation; that landed,
     and this case still does not compile — the blocker is E3057, a bare `/` whose divisor is not
     provably non-zero ("throwing division requires try"). Nothing about printing floats is involved.
     Measured by flipping the marker and reading the error, not by re-reading the old note. -->
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

### Negating a float VARIABLE flips its sign bit

`-x` over a value the compiler cannot see is a genuine negation, not `0.0 - x`. For every input
but a zero the two agree, which is why the ordinary case below was green under both spellings;
the zero cases after it are the ones that tell them apart, and they are what `0.0 - x` failed.

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

A positive zero negated is a NEGATIVE zero, and negating that gives the positive one back. The
sign is read two ways: through `print`, whose float formatter is sign-bit based, and through
`Math.hasNegativeSignBit`, which is `__Builtins.floatToBits` — the print-independent instrument.
Under `0.0 - x` both prints said `0.0` and the bit was never set.

<!-- test: negating-a-zero-variable -->
```maxon
function main() returns ExitCode
	let z = 0.0
	let negated = -z
	print("{negated}\n")
	print("{-negated}\n")
	if not Math.hasNegativeSignBit(negated) 'signBitNotSet'
		return 1
	end 'signBitNotSet'
	if Math.hasNegativeSignBit(-negated) 'signBitNotCleared'
		return 2
	end 'signBitNotCleared'
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-0.0
0.0
```

Negation flips the sign bit of EVERY pattern, a NaN's included — IEEE-754's `negate`, which the
standard itself distinguishes from `subtraction(0, x)`. A NaN prints without a sign, so the bit
is the only way to observe it.

<!-- test: negating-nan-flips-its-sign-bit -->
```maxon
function main() returns ExitCode
	let quietNan = __Builtins.bitsToFloat(0x7FF8000000000000)
	if Math.hasNegativeSignBit(quietNan) 'arrivedNegative'
		return 1
	end 'arrivedNegative'
	let negated = -quietNan
	if not Math.hasNegativeSignBit(negated) 'signBitNotFlipped'
		return 2
	end 'signBitNotFlipped'
	if negated == negated 'notANan'
		return 3
	end 'notANan'
	return 42
end 'main'
```
```exitcode
42
```

The compile-time evaluator makes the same flip: `-` over a float CONSTANT reference is folded to
`negatedFloatBits` of it, so a top-level `let` and a body agree about one line of source. (It was
`0.0 - x` while the body was, and folded `-Zero` to `+0.0`.)

<!-- test: const-negation-of-a-float-constant -->
```maxon
let Zero = 0.0
let NegatedZero = -Zero

function main() returns ExitCode
	print("{NegatedZero}\n")
	print("{-NegatedZero}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-0.0
0.0
```

A value spelled with a ranged float alias reaches the same op through `numberTagOf` — the door
that resolves a declared alias to its domain — so the flip does not depend on how the operand was
declared.

<!-- test: negation-through-a-ranged-float-alias -->
```maxon
typealias Real = float(f64.min to f64.max)

function negate(x Real) returns float
	return -x
end 'negate'

function main() returns ExitCode
	print("{negate(0.0)}\n")
	print("{negate(-2.5)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-0.0
2.5
```
