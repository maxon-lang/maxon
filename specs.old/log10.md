---
feature: log10
status: stable
keywords: log10, logarithm, base-10, math
category: stdlib
---

## Developer Notes

The `log10` function is implemented as a **stdlib function** using the natural logarithm.

**Implementation Details:**
- Category: `stdlib` (stdlib/math/log10.maxon)
- Implementation: Uses the identity `log10(x) = log(x) / log(10)`
- Depends on: `log` function from stdlib

**Type System:**
- Input: `float`
- Output: `float`
- Participates in implicit int→float promotion

**Algorithm:**
- Uses the mathematical identity: log₁₀(x) = ln(x) / ln(10)
- Relies on the existing `log` (natural logarithm) implementation
- ln(10) ≈ 2.302585092994046

**Performance:**
- Performance depends on the `log` function implementation
- Single division operation after computing natural logarithm
- Handles special cases (zero, negative, one) directly

**Special Cases (IEEE 754 / Zig Reference):**
- `log10(+inf)` = `+inf`
- `log10(0)` = `-inf`
- `log10(x)` = `nan` if x < 0
- `log10(nan)` = `nan`

**Related Functions:**
- `log` - natural logarithm (base e)
- `exp` - exponential function (e^x)
- `pow` - general exponentiation

## Documentation

# log10

Calculate the base-10 logarithm of a number.

**Signature:** `log10(x float) float`

**Parameters:**
- `x` - The number to take the base-10 logarithm of (must be positive)

**Returns:** The base-10 logarithm of the input

**Example:**

```maxon
var x = 100.0
var y = log10(x)     // 2.0 (10^2 = 100)

var z = 1000.0
var w = log10(z)     // 3.0 (10^3 = 1000)

var a = 10.0
var b = log10(a)     // 1.0 (10^1 = 10)
```
**Notes:**
- Input must be positive (returns NaN for negative values)
- `log10(0.0)` returns negative infinity
- `log10(1.0)` returns `0.0`
- `log10(10.0)` returns `1.0`
- For integer inputs, the value is automatically promoted to float

## Tests

<!-- test: log10.basic -->
```maxon
function main() returns int
    var x = log10(100.0)
    print("{x}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1.999999
```

<!-- test: log10.one-thousand -->
```maxon
function main() returns int
    var x = log10(1000.0)
    print("{x}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
2.999999
```

<!-- test: log10.ten -->
```maxon
function main() returns int
    var x = log10(10.0)
    print("{x}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.999999
```

<!-- test: log10.one -->
```maxon
function main() returns int
    var result = log10(1.0)
    if result == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: log10.precision -->
```maxon
function main() returns int
    var x = log10(2.0)
    print("{x}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.301029
```

<!-- test: log10.with-int-promotion -->
```maxon
function main() returns int
    var x = 100  // int
    var result = log10(x)  // x promoted to 100.0
    print("{result}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1.999999
```

<!-- test: log10.large-value -->
```maxon
function main() returns int
    var x = log10(10000.0)
    print("{x}")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3.999999
```
