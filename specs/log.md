---
feature: log
status: stable
keywords: [math, logarithm, natural-log]
category: stdlib
---

# Natural Logarithm Function

## Documentation

The `log()` function calculates the natural logarithm (ln) of a number.

### Syntax

```maxon
Math.log(x)
```
Parameters:
- `x` - The value to take the logarithm of (must be positive)

Returns the natural logarithm of x.

### Example

```maxon
function main() returns ExitCode
	let e = 2.71828
	let result = Math.log(e)  // ln(e) ≈ 1.0
	return trunc(result)
end 'main'
```
```exitcode
0
```


### Notes

- Input must be positive (x > 0)
- Returns 0.0 for x = 1.0
- ⚠ **A non-positive input answers as IEEE 754 defines it, never with a sentinel.** A sentinel `0.0`
  would collide with `log(1.0)`, so a caller receiving `0.0` could not tell a correct answer from a
  rejected argument. By user ruling the three logarithms answer as IEEE 754 defines them, which is what
  every mainstream language does and which collides with nothing: `log(+0.0)` and `log(-0.0)` are both
  `-inf`, and `log(x)` for `x < 0` is `nan`.
- `log(+inf)` is `+inf`, and `log(nan)` is `nan` — the rest of what IEEE 754 says about the non-finite
  arguments, pinned below. The `+inf` case is not decorative: without it the normalization loop halves
  an infinity forever.
- Uses natural logarithm (base e), not base 10

## Tests

⚠ **Every `stdout` block in this file is the SHORTEST ROUND-TRIP representation of its double** —
the fewest digits that parse back to the same bit pattern, by user ruling (`specs/sin.md` sets out why).

<!-- test: ln-of-e -->
```maxon
function main() returns ExitCode
	let e = 2.71828
	let result = Math.log(e)
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.999999327347282
```


<!-- test: ln-of-one -->
```maxon
function main() returns ExitCode
	let result = Math.log(1.0)
	return trunc(result)
end 'main'
```
```exitcode
0
```


<!-- test: ln-of-large -->
```maxon
function main() returns ExitCode
	let result = Math.log(100.0)  // ln(100) ≈ 4.6
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4.605170185988091
```


<!-- test: int-promotion -->
```maxon
function main() returns ExitCode
	let result = Math.log(10)  // Int promoted to float
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.3025850929940455
```

⭐ **The pin on the ruling the Notes above state.** A sentinel `0.0` for any
of these three would be the same thing `Math.log(1.0)` prints, which is exactly the collision the ruling
rules out. The two zeros are BOTH `-inf` and only the negative is `nan`, so
this case is also the pin on the guard's shape. The door has to be `<=`: spelled `x < 0.0` it would
not catch either zero AT ALL — `-0.0 < 0.0` is false, and so is `0.0 < 0.0` — and both would fall
through to the general path, whose `normalize_up` loop multiplies a zero by 2.0 forever. That failure
is a **HANG, not a wrong number**, which is the harder kind to read off a test run. The sign question
is then settled INSIDE the guard by `x == 0.0`, which is true of both zeros; `stdlib/Math.maxon`'s
`nonPositiveLogResult` is written that way and this case is what holds it there.

<!-- test: non-positive-is-ieee -->
```maxon
function main() returns ExitCode
	let negativeZero = -0.0
	print("{Math.log(0.0)}\n")
	print("{Math.log(negativeZero)}\n")
	print("{Math.log(-1.0)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
-inf
-inf
nan
```


⭐ **The case exists because the alternative is a HANG rather than a wrong number.**
IEEE 754 gives `log(+inf)` as `+inf`. `log` normalizes by halving x until it drops under 1.0, and
`inf / 2.0` is `inf`, so without the guard in `stdlib/Math.maxon` that loop never exits. Without the
guard this program does not print something wrong, it runs until it is killed — which is why the
pin is worth more here than on an arm that merely returns the wrong digits.

⚠ **The argument is constructible only BECAUSE of the ruling above.** Maxon has no infinity literal;
`Math.log(0.0)` answering `-inf` is what puts one within reach of a program, and negating it is the
whole construction.

<!-- test: positive-infinity -->
```maxon
function main() returns ExitCode
	let positiveInfinity = -Math.log(0.0)
	print("{Math.log(positiveInfinity)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
inf
```
