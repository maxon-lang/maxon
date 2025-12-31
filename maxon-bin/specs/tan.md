---
feature: tan
status: stable
keywords: tan, tangent, trigonometry, math, radians
category: math-intrinsic
---

## Developer Notes

The `tan` function is implemented as a **runtime function** rather than an LLVM intrinsic. This is because LLVM does not provide a direct intrinsic for tangent.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:46)
- Intrinsic kind: `MathIntrinsicKind::RuntimeFunction`
- Links to external `tan` function from `maxon-runtime`
- Codegen: Uses `getRuntimeFunction()` to declare/link the runtime function (codegen.cpp:1115-1117)
- The runtime function has signature: `double tan(double)`

**Type System:**
- Input: `float` (Maxon's float maps to LLVM double)
- Output: `float`
- Participates in implicit int→float promotion

**Related Functions:**
- `sin` - sine function (also runtime)
- `cos` - cosine function (also runtime)

## Documentation

# tan

Calculate the tangent of an angle (in radians).

**Signature:** `tan(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The tangent of the input angle

**Example:**

```maxon
var x = 0.0
var y = tan(x)       // 0.0

var quarterPi = 0.785398  // π/4
var z = tan(quarterPi)    // 1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `tan(0.0)` returns exactly `0.0`
- `tan(π/4)` returns approximately `1.0`
- The tangent function has vertical asymptotes at odd multiples of π/2

## Tests

<!-- test: tan.zero -->
```maxon
function main() returns int
    var result = tan(0.0)
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
    var x1 = tan(0.0)
    var x2 = tan(0.5)
    var x3 = tan(1.0)
    var x4 = tan(0.7854)
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
    var result = tan(quarterPi)
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
    var result = tan(x)  // x promoted to 0.0
    if result == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```
