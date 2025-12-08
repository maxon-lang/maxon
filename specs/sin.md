---
feature: sin
status: stable
keywords: sin, sine, trigonometry, math, radians
category: math-intrinsic
---

## Developer Notes

The `sin` function is implemented as a **runtime function** rather than an LLVM intrinsic.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:44)
- Intrinsic kind: `MathIntrinsicKind::RuntimeFunction`
- Links to external `sin` function from `maxon-runtime`
- Codegen: Uses `getRuntimeFunction()` to declare/link the runtime function (codegen.cpp:1115-1117)
- The runtime function has signature: `double sin(double)`

**Type System:**
- Input: `float` (Maxon's float maps to LLVM double)
- Output: `float`
- Participates in implicit int→float promotion

**Related Functions:**
- `cos` - cosine function (also runtime)
- `tan` - tangent function (also runtime)

## Documentation

# sin

Calculate the sine of an angle (in radians).

**Signature:** `sin(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The sine of the input angle

**Example:**

```maxon
var x = 0.0
var y = sin(x)       // 0.0

// Note: π ≈ 3.14159265
var halfPi = 1.5708  // π/2
var z = sin(halfPi)  // 1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `sin(0.0)` returns exactly `0.0`
- `sin(π/2)` returns approximately `1.0`
- The sine function oscillates between -1 and 1

## Tests

<!-- test: sin.basic -->
```maxon
function main() int
    var x1 = sin(0.0)
    var x2 = sin(0.5)
    var x3 = sin(1.0)
    var x4 = sin(1.5708)
    printFloat(x1, 6)
    printFloat(x2, 6)
    printFloat(x3, 6)
    printFloat(x4, 6)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.000000
0.479426
0.841471
1.000000
```

<!-- test: sin.zero -->
```maxon
function main() int
    var result = sin(0.0)
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
function main() int
    var x = 0  // int
    var result = sin(x)  // x promoted to 0.0
    if result == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```
