---
feature: cos
status: stable
keywords: cos, cosine, trigonometry, math, radians
category: math-intrinsic
---
# cos

## Documentation

Calculate the cosine of an angle (in radians).

**Signature:** `Math.cos(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The cosine of the input angle

**Example:**

```maxon
var x = 0.0
var y = Math.cos(x)       // 1.0

var pi = 3.14159265
var z = Math.cos(pi)      // -1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `Math.cos(0.0)` returns exactly `1.0`
- `Math.cos(π)` returns approximately `-1.0`
- The cosine function oscillates between -1 and 1

## Tests

<!-- test: cos.basic -->
```maxon
function main() returns ExitCode
	var x1 = Math.cos(0.0)
	var x2 = Math.cos(0.5)
	var x3 = Math.cos(1.0)
	var x4 = Math.cos(1.5708)
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
1.0
0.877583
0.540302
-0.000004
```


<!-- test: cos.zero -->
```maxon
function main() returns ExitCode
	var result = Math.cos(0.0)
	if result == 1.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: cos.with-int-promotion -->
```maxon
function main() returns ExitCode
	var x = 0  // int
	var result = Math.cos(x)  // x promoted to 0.0
	if result == 1.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
