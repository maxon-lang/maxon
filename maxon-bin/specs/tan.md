---
feature: tan
status: stable
keywords: tan, tangent, trigonometry, math, radians
category: math-intrinsic
---
## Documentation

# tan

Calculate the tangent of an angle (in radians).

**Signature:** `Math.tan(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The tangent of the input angle

**Example:**

```maxon
var x = 0.0
var y = Math.tan(x)       // 0.0

var quarterPi = 0.785398  // π/4
var z = Math.tan(quarterPi)    // 1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `Math.tan(0.0)` returns exactly `0.0`
- `Math.tan(π/4)` returns approximately `1.0`
- The tangent function has vertical asymptotes at odd multiples of π/2

## Tests

<!-- test: tan.zero -->
```maxon
function main() returns int
    var result = Math.tan(0.0)
    if result == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: tan.multiple-values -->
```maxon
function main() returns int
    var x1 = Math.tan(0.0)
    var x2 = Math.tan(0.5)
    var x3 = Math.tan(1.0)
    var x4 = Math.tan(0.7854)
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
0.546302
1.557407
1.000003
```

<!-- test: tan.quarter-pi -->
```maxon
function main() returns int
    var quarterPi = 0.785398163
    var result = Math.tan(quarterPi)
    // Should be approximately 1.0
    var diff = abs(result - 1.0)
    if diff < 0.01 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: tan.with-int-promotion -->
```maxon
function main() returns int
    var x = 0  // int
    var result = Math.tan(x)  // x promoted to 0.0
    if result == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```
