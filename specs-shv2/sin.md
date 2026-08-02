---
feature: sin
status: stable
keywords: sin, sine, trigonometry, math, radians
category: math-intrinsic
---
# sin

## Documentation

Calculate the sine of an angle (in radians).

**Signature:** `Math.sin(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The sine of the input angle

**Example:**

```maxon
var x = 0.0
var y = Math.sin(x)       // 0.0

// Note: π ≈ 3.14159265
var halfPi = 1.5708  // π/2
var z = Math.sin(halfPi)  // 1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `Math.sin(0.0)` returns exactly `0.0`
- `Math.sin(π/2)` returns approximately `1.0`
- The sine function oscillates between -1 and 1

## Tests

<!-- test: sin.basic -->
⚠ **THE `stdout` BLOCK IS RETRACTED, AND IT IS THE ONLY RETRACTION IN THIS FILE.** `/specs` expects
`0.479426` / `0.841471` / `0.999999` — the bootstrap's format, which prints a fixed 6 decimal places and
trims trailing zeros. **That formatter has a measured arithmetic defect**: it rounds the fraction to six
decimals but never carries into the integer part, so `1.9999999` prints `1.999999` rather than `2.0`, and
`3.9999996` prints `3.999999`. The third line here is one of its victims — `Math.sin(1.5708)` is
`0.9999999999932534`, and `0.999999` is what carry-loss makes of it, not what six decimals make of it.
(The same defect is baked into `/specs/log.md` and five expectations in `/specs/log10.md`, where
`log10(10)`, `log10(100)`, `log10(1000)` and `log10(10000)` — true values 1, 2, 3 and 4 — are written
`0.999999`, `1.999999`, `2.999999` and `3.999999`.)

**USER RULING: shv2 prints the SHORTEST ROUND-TRIP representation** — the fewest digits that uniquely
identify the double, as Python 3, JavaScript, Rust, Go, Swift, Java and .NET Core all do. That makes the
whole class of defect unreachable rather than fixing one instance of it: a value that is not exactly 1.0
can never print as `1.0`, and a value that is cannot print as `0.999999`. The digits below are not "what
shv2 happens to emit" — each is verified to parse back to the identical bit pattern, and dropping any
last digit breaks that round-trip. `Math.sin` itself is bit-identical across both compilers for all four
inputs, so only the rendering differs.
```maxon
function main() returns ExitCode
	let x1 = Math.sin(0.0)
	let x2 = Math.sin(0.5)
	let x3 = Math.sin(1.0)
	let x4 = Math.sin(1.5708)
	print("{x1}\n")
	print("{x2}\n")
	print("{x3}\n")
	print("{x4}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
0.0
0.479425538604203
0.8414709848078965
0.9999999999932534
```

<!-- test: sin.zero -->
```maxon
function main() returns ExitCode
	let result = Math.sin(0.0)
	if result == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: sin.with-int-promotion -->
```maxon
function main() returns ExitCode
	let x = 0  // int
	let result = Math.sin(x)  // x promoted to 0.0
	if result == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
