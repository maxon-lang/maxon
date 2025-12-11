---
feature: abs
status: stable
keywords: abs, absolute value, math
category: math-intrinsic
---

## Developer Notes

The `abs` function is implemented as an **LLVM intrinsic** for optimal performance.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:35)
- Intrinsic kind: `MathIntrinsicKind::LLVMIntrinsic`
- LLVM intrinsic ID: `llvm::Intrinsic::fabs` (for float)
- Codegen: Directly generates LLVM intrinsic call (codegen.cpp:1107-1113)
- Overloaded for both int and float types

**Type System:**
- Float version: `abs(x float) float`
- Int version: Not yet implemented (will be added in future)
- Currently only accepts float inputs

**Performance:**
- Float version compiled to native CPU instruction
- Int version optimized by LLVM to bitwise operations
- No function call overhead

## Documentation

# abs

Calculate the absolute value of a number.

**Signature (float):** `abs(x float) float`

**Parameters:**
- `x` - The number to take the absolute value of (must be float)

**Returns:** The absolute value of the input (always non-negative)

**Example:**

```maxon
var x = -5.5
var y = abs(x)       // 5.5

var i = -42.0
var j = abs(i)       // 42.0
```
**Notes:**
- `abs(0.0)` returns `0.0`
- `abs(x)` always returns a non-negative value
- Currently only works with float type
- For integer values, convert to float first: `abs(-42.0)`

## Tests

<!-- test: abs.float -->
```maxon
function main() returns int
    var neg = 0.0 - 5.5
    var x = abs(neg)
    var y = trunc(x)
    return y
end 'main'
```
```exitcode
5
```

<!-- test: abs.negative-int-as-float -->
```maxon
function main() returns int
    var neg = 0.0 - 42.0
    var x = abs(neg)
    return trunc(x)
end 'main'
```
```exitcode
42
```

<!-- test: abs.zero -->
```maxon
function main() returns int
    var x = abs(0.0)
    if x == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```
