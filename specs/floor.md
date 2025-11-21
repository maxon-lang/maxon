---
feature: floor
status: stable
keywords: floor, rounding, math, conversion
category: math-intrinsic
---

## Developer Notes

The `floor` function is implemented as an **LLVM intrinsic** for type conversion.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:30)
- Intrinsic kind: `MathIntrinsicKind::LLVMIntrinsic`
- LLVM intrinsic ID: `llvm::Intrinsic::floor`
- Returns integer type (converts float → int)
- Codegen: Generates LLVM floor intrinsic + fptosi conversion

**Type System:**
- Input: `float`
- Output: `int`
- Rounds down toward negative infinity

**Rounding Behavior:**
- Always rounds down (toward negative infinity)
- `floor(3.9)` = 3
- `floor(-3.1)` = -4 (rounds down, not toward zero)

## Documentation

# floor

Round a floating-point number down to the nearest integer (toward negative infinity).

**Signature:** `floor(x float) int`

**Parameters:**
- `x` - The floating-point number to round down

**Returns:** The largest integer less than or equal to x

**Example:**

```maxon
var x = 3.9
var y = floor(x)     // 3

var neg = -3.2
var z = floor(neg)   // -4 (rounds down toward negative infinity)
```

**Notes:**
- Always rounds toward negative infinity
- Different from truncation for negative numbers
- `floor(3.9)` returns `3`
- `floor(-3.2)` returns `-4` (not `-3`)

## Tests

<!-- test: floor.positive -->
```maxon
function main() int
    var x = 3.9
    var y = floor(x)
    return y
end 'main'
```
ExitCode: 3

<!-- test: floor.negative -->
```maxon
function main() int
    var neg = 0.0 - 3.2
    var y = floor(neg)
    return y
end 'main'
```
ExitCode: -4

<!-- test: floor.with-ceil -->
```maxon
function main() int
    var x = 3.7
    var a = floor(x)
    var b = ceil(x)
    return a + b
end 'main'
```
ExitCode: 7
