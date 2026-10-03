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
⚖ **USER RULING: The compiler prints the SHORTEST ROUND-TRIP representation** — the fewest digits that
uniquely identify the double, as Python 3, JavaScript, Rust, Go, Swift, Java and .NET Core all do. A fixed
six-decimal format is the alternative it rules out, and the third line here shows why: a formatter that
rounds the fraction to six decimals without carrying into the integer part prints `Math.sin(1.5708)`
(`0.9999999999932534`) as `0.999999`, and `1.9999999` as `1.999999` rather than `2.0`. Shortest round-trip
makes that whole class of defect unreachable: a value that is not exactly 1.0 can never print as `1.0`, and
a value that is cannot print as `0.999999`. The digits below are not "what the compiler happens to emit" —
each is verified to parse back to the identical bit pattern, and dropping any last digit breaks that
round-trip.
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
