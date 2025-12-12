---
feature: cos
status: stable
keywords: cos, cosine, trigonometry, math, radians
category: math-intrinsic
---

## Developer Notes

The `cos` function is implemented as a **runtime function** rather than an LLVM intrinsic.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:45)
- Intrinsic kind: `MathIntrinsicKind::RuntimeFunction`
- Links to external `cos` function from `maxon-runtime`
- Codegen: Uses `getRuntimeFunction()` to declare/link the runtime function (codegen.cpp:1115-1117)
- The runtime function has signature: `double cos(double)`

**Type System:**
- Input: `float` (Maxon's float maps to LLVM double)
- Output: `float`
- Participates in implicit int→float promotion

**Related Functions:**
- `sin` - sine function (also runtime)
- `tan` - tangent function (also runtime)

## Documentation

# cos

Calculate the cosine of an angle (in radians).

**Signature:** `cos(x float) float`

**Parameters:**
- `x` - The angle in radians

**Returns:** The cosine of the input angle

**Example:**

```maxon
var x = 0.0
var y = cos(x)       // 1.0

var pi = 3.14159265
var z = cos(pi)      // -1.0 (approximately)
```
**Notes:**
- The function works with radians, not degrees
- To convert degrees to radians: `radians = degrees * (π / 180)`
- `cos(0.0)` returns exactly `1.0`
- `cos(π)` returns approximately `-1.0`
- The cosine function oscillates between -1 and 1

## Tests

<!-- test: cos.basic -->
```maxon
function main() returns int
    var x1 = cos(0.0)
    var x2 = cos(0.5)
    var x3 = cos(1.0)
    var x4 = cos(1.5708)
    print("{x1}")
    print("{x2}")
    print("{x3}")
    print("{x4}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1.0
0.877582
0.540302
-0.000003
```


<!-- test: cos.zero -->
```maxon
function main() returns int
    var result = cos(0.0)
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
function main() returns int
    var x = 0  // int
    var result = cos(x)  // x promoted to 0.0
    if result == 1.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```
