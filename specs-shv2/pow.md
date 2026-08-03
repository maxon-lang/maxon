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

