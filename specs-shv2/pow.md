---
feature: pow
status: stable
keywords: [math, power, exponentiation]
category: stdlib
---

# Power Function

## Documentation

The `pow()` function raises a number to a power.

### Syntax

```maxon
Math.pow(base, exponent: exponent)
```
Parameters:
- `base` - The base number (float)
- `exponent` - The exponent (float)

Returns base raised to the power of exponent.

### Example

```maxon
function main() returns ExitCode
	let result = Math.pow(2.0, exponent: 3.0)  // 2^3 = 8
	return trunc(result)
end 'main'
```
```exitcode
8
```


### Notes

- Both parameters are floats
- Integer inputs are automatically promoted to float
- Returns float result
- Special cases:
  - `pow(x, 0.0)` returns 1.0 for any x
  - `pow(0.0, y)` returns 0.0 for positive y
  - `pow(1.0, y)` returns 1.0 for any y
- ⭐ **Those three are still true and are no longer the whole story.** By user ruling `pow` now answers
  as IEEE 754 defines it across every argument class, which changed one case from a WRONG ANSWER and
  several from sentinels. The full table is pinned at the bottom of this file; the headlines:
  - a **negative base with an integer exponent** is an ordinary computation — `pow(-2.0, 3.0)` is
    `-8.0`. It used to return `0.0`.
  - a **negative base with a non-integer exponent** is `nan`; there is no real answer to ask for.
  - `pow(0.0, y)` for **negative** y is an infinity, not `0.0` — it is a division by zero, and the
    sign of the zero comes back out when y is an odd integer: `pow(-0.0, -1.0)` is `-inf`.
  - the infinities and NaN follow the standard's own rows, including the two that OUTRANK the NaN
    rules: `pow(x, 0.0)` is `1.0` even for `x = nan`, and `pow(1.0, y)` is `1.0` even for `y = nan`.

## Tests

<!-- test: basic -->
```maxon
function main() returns ExitCode
	let result = Math.pow(2.0, exponent: 3.0)
	return trunc(result)
end 'main'
```
```exitcode
8
```


<!-- test: square -->
```maxon
function main() returns ExitCode
	let result = Math.pow(5.0, exponent: 2.0)
	return trunc(result)
end 'main'
```
```exitcode
25
```


<!-- test: zero-exponent -->
```maxon
function main() returns ExitCode
	let result = Math.pow(123.0, exponent: 0.0)
	return trunc(result)
end 'main'
```
```exitcode
1
```


<!-- test: fractional-exponent -->
The only case in this file that reads its result as TEXT rather than through `trunc`, so it is the
only one that pins what `Math.pow` actually computes rather than what it truncates to. It sat
`disabled-test:` awaiting float interpolation, and its expectation was written against the
bootstrap's fixed-six-decimal printer (`1.999999`). shv2 prints the SHORTEST decimal that reads back
as the same double, so the same value now spells out in full — `Math.pow(4.0, 0.5)` is a software
exp/log pair and lands one ulp under 2, which six decimals were rounding away.

⚠ **The pinned value moved from `1.9999999999999991` to `1.9999999999999998`, and the sentence above
became TRUE when it did.** `1.9999999999999991` is **four** ulp under 2; only `1.9999999999999998` is
one. `Math.pow` routes through `exp(exponent * log(base))`, and `stdlib/Math.maxon` had been carrying
**two spellings of ln 2** — `0.693147180559945`, some 2.7 ulp low, multiplied by the exponent (up to
~1000) inside `log`, alongside the correctly-rounded `0.6931471805599453` a few functions away. One fact,
two sites, nothing forcing agreement. Found by review during the `atan2` port and collapsed to a single
constant; `log`'s worst error over a 12-argument probe fell from ~7 ulp to ≤2, and this case is the only
committed expectation anywhere that the correction moves.
```maxon
function main() returns ExitCode
	let result = Math.pow(4.0, exponent: 0.5)  // Square root
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1.9999999999999998
```


<!-- test: int-promotion -->
```maxon
function main() returns ExitCode
	let result = Math.pow(3, exponent: 2)  // Ints promoted to float
	return trunc(result)
end 'main'
```
```exitcode
9
```


<!-- test: basic-with-stdlib-ir -->
<!-- IncludeStdlibIr -->
```maxon
function main() returns ExitCode
	let result = Math.pow(2.0, exponent: 3.0)
	return trunc(result)
end 'main'
```
```exitcode
8
```


⭐⭐ **shv2-authored, and the pin on the user ruling that took `Math.pow` to IEEE 754.** The case below
is the one that makes the ruling more than a tidy-up: **`Math.pow(-2.0, exponent: 3.0)` used to return
`0.0`.** `stdlib/Math.maxon` carried the line `return 0.0  // Simplified: return 0 for negative base`,
which reads like a sentinel for a rejected input and is nothing of the kind — a negative base raised to
an INTEGER power is an ordinary, completely well-defined computation, and `-8.0` is its answer. It was
a wrong answer to a valid question, not a refusal.

The sign is now the exponent's parity and the magnitude comes from the SAME positive-base path every
other call uses, so there is one numerical implementation here and one sign rule, rather than a second
series written out for the negative half of the domain.

<!-- test: negative-base-is-ieee -->
```maxon
function main() returns ExitCode
	print("pow(-2,3)     {Math.pow(-2.0, exponent: 3.0)}\n")
	print("pow(-2,2)     {Math.pow(-2.0, exponent: 2.0)}\n")
	print("pow(-2,-3)    {Math.pow(-2.0, exponent: -3.0)}\n")
	print("pow(-2,0.5)   {Math.pow(-2.0, exponent: 0.5)}\n")
	print("pow(-2,0)     {Math.pow(-2.0, exponent: 0.0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pow(-2,3)     -8.0
pow(-2,2)     4.0
pow(-2,-3)    -0.125
pow(-2,0.5)   nan
pow(-2,0)     1.0
```

⭐ **The zero base, which is where the other sentinel was.** `pow(0.0, y)` for a NEGATIVE y is a
division by zero, so IEEE 754 answers an infinity; the old code returned `0.0` for every y, positive or
negative, from two arms that both said `return 0.0`. The sign of the zero survives into the answer
exactly when the exponent is an odd integer — which is why `-0.0` has to be told apart from `0.0`, and
that cannot be done with a comparison (`-0.0 < 0.0` is false). `stdlib/Math.maxon` reads the sign bit.

<!-- test: zero-base-is-ieee -->
```maxon
function main() returns ExitCode
	let negativeZero = -0.0
	print("pow(0,-1)     {Math.pow(0.0, exponent: -1.0)}\n")
	print("pow(-0,-1)    {Math.pow(negativeZero, exponent: -1.0)}\n")
	print("pow(-0,-2)    {Math.pow(negativeZero, exponent: -2.0)}\n")
	print("pow(-0,3)     {Math.pow(negativeZero, exponent: 3.0)}\n")
	print("pow(-0,2)     {Math.pow(negativeZero, exponent: 2.0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pow(0,-1)     inf
pow(-0,-1)    -inf
pow(-0,-2)    inf
pow(-0,3)     -0.0
pow(-0,2)     0.0
```

⭐ **The non-finite classes, including the two rows that OUTRANK everything else.** IEEE 754 makes
`pow(x, ±0)` and `pow(1.0, y)` beat every other rule *including the NaN rules* — so a NaN base with a
zero exponent is `1.0`, not `nan`. That is why those two guards come first in `stdlib/Math.maxon` and
why their order is the specification rather than a convenience: move either below the NaN test and two
of the lines below change.

⚠ **THE LAST TWO LINES HAVE NO INFINITE ARGUMENT AT ALL — every operand is finite**, and they are the
only two here reachable without naming an infinity. `Math.pow` computes the general case as
`exp(exponent * log(base))`, and that PRODUCT overflows: `1e308 * ln 10` is `+inf` and `-1e308 * ln 10`
is `-inf`, so both land in `Math.exp` as infinities that no argument of `pow` ever was.

`exp` reduces by halving until its argument is <= 1.0, and `inf / 2.0` is `inf`, so **each infinity
needs its OWN guard and each guard needs its own case here.** MEASURED, one at a time: remove the
`+inf` guard and `pow(10,1e308)` runs until it is killed; remove the `-inf` guard and `pow(10,-1e308)`
does, from `exp`'s `val = -val` on the negative path. Neither prints a wrong number — both hang.

⚠ `pow(10,-1e308)` was added because the `-inf` guard had NO pin: every other non-finite line here is
answered by `powOfInfiniteExponent` or `powOfExtremeBase` before `exp` is ever called, so the guard
could have been deleted with the whole suite still green. A `neg_exp` of `1e308` is far past the
repeated-multiplication threshold, which is what drops it into the general path in the first place.

⚠ The infinities here are constructed from `Math.log(0.0)`, which is `-inf` by the companion ruling in
`specs-shv2/log.md`. Maxon has no infinity literal, so that is the only way a spec case can name one.

<!-- test: non-finite-is-ieee -->
```maxon
function main() returns ExitCode
	let negInf = Math.log(0.0)
	let inf = -negInf
	let nan = Math.log(-1.0)
	print("pow(nan,0)    {Math.pow(nan, exponent: 0.0)}\n")
	print("pow(1,nan)    {Math.pow(1.0, exponent: nan)}\n")
	print("pow(nan,2)    {Math.pow(nan, exponent: 2.0)}\n")
	print("pow(2,nan)    {Math.pow(2.0, exponent: nan)}\n")
	print("pow(2,inf)    {Math.pow(2.0, exponent: inf)}\n")
	print("pow(0.5,inf)  {Math.pow(0.5, exponent: inf)}\n")
	print("pow(2,-inf)   {Math.pow(2.0, exponent: negInf)}\n")
	print("pow(-1,inf)   {Math.pow(-1.0, exponent: inf)}\n")
	print("pow(-inf,3)   {Math.pow(negInf, exponent: 3.0)}\n")
	print("pow(-inf,2)   {Math.pow(negInf, exponent: 2.0)}\n")
	print("pow(-inf,-3)  {Math.pow(negInf, exponent: -3.0)}\n")
	print("pow(inf,-2)   {Math.pow(inf, exponent: -2.0)}\n")
	print("pow(10,1e308) {Math.pow(10.0, exponent: 1.0e308)}\n")
	print("pow(10,-1e308) {Math.pow(10.0, exponent: -1.0e308)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
pow(nan,0)    1.0
pow(1,nan)    1.0
pow(nan,2)    nan
pow(2,nan)    nan
pow(2,inf)    inf
pow(0.5,inf)  0.0
pow(2,-inf)   0.0
pow(-1,inf)   1.0
pow(-inf,3)   -inf
pow(-inf,2)   inf
pow(-inf,-3)  -0.0
pow(inf,-2)   0.0
pow(10,1e308) inf
pow(10,-1e308) 0.0
```
