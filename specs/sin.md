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
```maxon
function main() returns int
  var x1 = Math.sin(0.0)
  var x2 = Math.sin(0.5)
  var x3 = Math.sin(1.0)
  var x4 = Math.sin(1.5708)
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
0.479426
0.841471
0.999999
```

<!-- test: sin.zero -->
```maxon
function main() returns int
  var result = Math.sin(0.0)
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
function main() returns int
  var x = 0  // int
  var result = Math.sin(x)  // x promoted to 0.0
  if result == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```
