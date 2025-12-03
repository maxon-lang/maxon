---
feature: trunc
status: stable
keywords: trunc, truncate, rounding, math, conversion
category: math-intrinsic
---

## Developer Notes

The `trunc` function is implemented as an **LLVM intrinsic** for type conversion.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:28)
- Intrinsic kind: `MathIntrinsicKind::LLVMIntrinsic`
- LLVM intrinsic ID: `llvm::Intrinsic::trunc`
- Returns integer type (converts float → int)
- Codegen: Generates LLVM trunc intrinsic + fptosi conversion

**Type System:**
- Input: `float`
- Output: `int`
- Truncates toward zero (removes fractional part)

**Rounding Behavior:**
- Always rounds toward zero
- Equivalent to removing the decimal part
- `trunc(3.9)` = 3
- `trunc(-3.9)` = -3 (toward zero, not down)

### Integer Division Optimization

When `trunc()` is applied to integer division, the compiler optimizes the pattern:
- Pattern: `trunc(int / int)`
- MIR: `FPToSI(FDiv(SIToFP(a), SIToFP(b)))` -> `SDiv(a, b)`
- Backend: Direct IDIV instruction (no float conversion overhead)
- Implemented in: `IntegerDivisionOptimizationPass`

This allows natural syntax `trunc(a/b)` to compile to efficient integer division.

## Documentation

# trunc

Truncate a floating-point number to an integer (toward zero).

**Signature:** `trunc(x float) int`

**Parameters:**
- `x` - The floating-point number to truncate

**Returns:** The integer part of x (fractional part removed)

**Example:**

```maxon
var x = 3.7
var y = trunc(x)     // 3

var neg = -3.7
var z = trunc(neg)   // -3 (toward zero, removes fractional part)
```
**Notes:**
- Truncates toward zero (removes decimal part)
- Different from floor/ceil for negative numbers
- `trunc(3.9)` returns `3`
- `trunc(-3.9)` returns `-3` (not `-4`)
- Equivalent to casting to int in many languages

## Tests

<!-- test: trunc.basic -->
```maxon
function main() int
    var neg = 0.0 - 3.7
    var y = trunc(neg)
    print_int(y)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
-3
```

<!-- test: trunc.positive -->
```maxon
function main() int
    var x = 7.9
    var y = trunc(x)
    return y
end 'main'
```
```exitcode
7
```

<!-- test: trunc.zero -->
```maxon
function main() int
    var x = 0.5
    var y = trunc(x)
    return y
end 'main'
```
```exitcode
0
```
