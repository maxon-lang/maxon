---
feature: round
status: stable
keywords: round, rounding, math, conversion
category: math-intrinsic
---

## Developer Notes

The `round` function is implemented as an **LLVM intrinsic** for type conversion.

**Implementation Details:**
- Keyword category: `MathIntrinsic` (lexer.cpp:29)
- Intrinsic kind: `MathIntrinsicKind::LLVMIntrinsic`
- LLVM intrinsic ID: `llvm::Intrinsic::round`
- Returns integer type (converts float → int)
- Codegen: Generates LLVM round intrinsic + fptosi conversion

**Type System:**
- Input: `float`
- Output: `int`
- Rounds to nearest integer value

**Rounding Behavior:**
- Rounds to nearest integer
- Halfway cases (e.g., 2.5) round to nearest even number
- Negative numbers round away from zero for exact halfway cases

## Documentation

# round

Round a floating-point number to the nearest integer.

**Signature:** `round(x float) int`

**Parameters:**
- `x` - The floating-point number to round

**Returns:** The nearest integer value

**Example:**

```maxon
var x = 3.5
var y = round(x)     // 4 (rounds to nearest)

var z = 2.4
var w = round(z)     // 2
```
**Notes:**
- Rounds to the nearest integer
- For halfway cases (e.g., 2.5), rounds to nearest even number
- `round(3.7)` returns `4`
- `round(-2.3)` returns `-2`

## Tests

<!-- test: round.basic -->
```maxon
function main() int
    var x = 3.7
    var y = round(x)
    return y
end 'main'
```
```exitcode
4
```

<!-- test: round.negative -->
```maxon
function main() int
    var neg = 0.0 - 2.3
    var y = round(neg)
    return y
end 'main'
```
```exitcode
-2
```

<!-- test: round.halfway -->
```maxon
function main() int
    var x = 2.5
    var y = round(x)
    print_float(y, 1)
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3.0
```

