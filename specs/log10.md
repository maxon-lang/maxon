---
feature: log10
status: stable
keywords: log10, logarithm, base-10, math
category: stdlib
---
# log10

## Documentation

Calculate the base-10 logarithm of a number.

**Signature:** `Math.log10(x float) float`

**Parameters:**
- `x` - The number to take the base-10 logarithm of (must be positive)

**Returns:** The base-10 logarithm of the input

**Example:**

```maxon
var x = 100.0
var y = Math.log10(x)     // 2.0 (10^2 = 100)

var z = 1000.0
var w = Math.log10(z)     // 3.0 (10^3 = 1000)

var a = 10.0
var b = Math.log10(a)     // 1.0 (10^1 = 10)
```
**Notes:**
- Input must be positive (returns NaN for negative values)
- `log10(0.0)` returns negative infinity
- `log10(1.0)` returns `0.0`
- `log10(10.0)` returns `1.0`
- For integer inputs, the value is automatically promoted to float

## Tests

⭐ **`log10` IS EXACT AT EXACT POWERS OF TEN.** For `k` in `0..22` — every power of ten a double
represents exactly — `Math.log10(10^k)` is exactly `k`, so `log10(100.0)` prints `2.0`, never the
`1.9999999999999996` that dividing `log` by ln 10 rounds to. `log10.powers-of-ten-are-exact` walks the
whole range; the single-value cases below pin how those answers print.

<!-- test: log10.basic -->
```maxon
function main() returns ExitCode
	let x = Math.log10(100.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.0
```

<!-- test: log10.one-thousand -->
```maxon
function main() returns ExitCode
	let x = Math.log10(1000.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
3.0
```

<!-- test: log10.ten -->
```maxon
function main() returns ExitCode
	let x = Math.log10(10.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
1.0
```

<!-- test: log10.one -->
```maxon
function main() returns ExitCode
	let result = Math.log10(1.0)
	if result == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: log10.precision -->
```maxon
function main() returns ExitCode
	let x = Math.log10(2.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.30102999566398125
```

<!-- test: log10.with-int-promotion -->
```maxon
function main() returns ExitCode
	let x = 100  // int
	let result = Math.log10(x)  // x promoted to 100.0
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
2.0
```

<!-- test: log10.large-value -->
```maxon
function main() returns ExitCode
	let x = Math.log10(10000.0)
	print("{x}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4.0
```

<!-- test: log10.powers-of-ten-are-exact -->
⭐⭐ **EVERY EXACTLY-REPRESENTABLE POWER OF TEN, NOT A SAMPLE OF THEM.** `1e0` through `1e22` are the
powers of ten a double holds exactly (`10^23` is not), so for each one there is exactly one right answer
and it is an integer. They are written as literals rather than computed, so no `pow` rounding can stand
between the input and the power it names. Each way of missing has its own exit: 1 above `k`, 2 below
it, 3 neither (a NaN), 4 a table that lost an entry.
```maxon
function main() returns ExitCode
	let powers = [1.0, 10.0, 100.0, 1000.0, 10000.0, 100000.0, 1.0e6, 1.0e7, 1.0e8, 1.0e9, 1.0e10, 1.0e11, 1.0e12, 1.0e13, 1.0e14, 1.0e15, 1.0e16, 1.0e17, 1.0e18, 1.0e19, 1.0e20, 1.0e21, 1.0e22]
	if powers.count() != 23 'everyPower'
		return 4
	end 'everyPower'

	for (iter, power) in powers.withIterator() 'eachPower'
		let k = iter.index() as Real
		let answer = Math.log10(power)

		if answer > k 'above'
			print("log10(1e{iter.index()}) = {answer}\n")
			return 1
		end 'above'

		if answer < k 'below'
			print("log10(1e{iter.index()}) = {answer}\n")
			return 2
		end 'below'

		if answer != k 'unordered'
			print("log10(1e{iter.index()}) = {answer}\n")
			return 3
		end 'unordered'
	end 'eachPower'

	return 0
end 'main'
```
```exitcode
0
```

⭐⭐ **compiler-authored, and THIS FILE'S NOTES NEEDED NO RETRACTION ON THE NON-POSITIVE BEHAVIOUR — which
is the strongest evidence the ruling matches what the language always intended.** The Notes at the top
already say *"returns NaN for negative values"* and *"`log10(0.0)` returns negative infinity"*. **Both
sentences were FALSE when they were written** — the implementation returned the sentinel `0.0` for
every non-positive input, exactly as `log.md` and `log2.md` admitted in their own Notes — and both
**become true with this change**. On that question the code was wrong about the prose, and for once
the spec is the party that does not have to move.

The special answers are `log`'s: a zero of either sign is `-inf` and a negative input is `nan`, so
`log.md`, `log2.md` and this file agree on every input outside the positive reals.

<!-- test: log10.non-positive-is-ieee -->
```maxon
function main() returns ExitCode
	let negativeZero = -0.0
	print("{Math.log10(0.0)}\n")
	print("{Math.log10(negativeZero)}\n")
	print("{Math.log10(-1.0)}\n")
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

`+inf` is `+inf`, as `log.md`'s `positive-infinity` case answers for `log`.

<!-- test: log10.positive-infinity -->
```maxon
function main() returns ExitCode
	let positiveInfinity = -Math.log(0.0)
	print("{Math.log10(positiveInfinity)}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
inf
```
