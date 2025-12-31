---
feature: ceil
status: stable
keywords: ceil, ceiling, rounding, math, conversion
category: math-intrinsic
---

## Developer Notes

The `ceil` function is implemented as an **LLVM intrinsic** for type conversion.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:31)
- Intrinsic kind: `MathIntrinsicKind::LLVMIntrinsic`
- LLVM intrinsic ID: `llvm::Intrinsic::ceil`
- Returns integer type (converts float → int)
- Codegen: Generates LLVM ceil intrinsic + fptosi conversion

**Type System:**
- Input: `float`
- Output: `int`
- Rounds up toward positive infinity

**Rounding Behavior:**
- Always rounds up (toward positive infinity)
- `ceil(3.1)` = 4
- `ceil(-3.9)` = -3 (rounds up, not toward zero)

## Documentation

# ceil

Round a floating-point number up to the nearest integer (toward positive infinity).

**Signature:** `ceil(x float) int`

**Parameters:**
- `x` - The floating-point number to round up

**Returns:** The smallest integer greater than or equal to x

**Example:**

```maxon
var x = 3.1
var y = ceil(x)      // 4

var neg = -3.9
var z = ceil(neg)    // -3 (rounds up toward positive infinity)
```
**Notes:**
- Always rounds toward positive infinity
- Different from truncation for negative numbers
- `ceil(3.1)` returns `4`
- `ceil(-3.9)` returns `-3` (not `-4`)

## Tests

<!-- test: ceil.positive -->
```maxon
function main() returns int
    var x = 3.1
    var y = ceil(x)
    return y
end 'main'
```
```exitcode
4
```

<!-- test: ceil.negative -->
```maxon
function main() returns int
    var neg = 0.0 - 3.9
    var y = ceil(neg)
    print("{y}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
-3
```

<!-- test: ceil.exact -->
```maxon
function main() returns int
    var x = 5.0
    var y = ceil(x)
    return y
end 'main'
```
```exitcode
5
```
