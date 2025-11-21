---
feature: pow
status: stable
keywords: [math, power, exponentiation]
category: stdlib
---

# Power Function

## Developer Notes

The `pow()` function calculates base raised to an exponent (base^exponent).

Implementation:
- Defined in `stdlib/math/pow.maxon`
- Signature: `pow(base float, exponent float) float`
- Uses binary exponentiation for integer exponents (efficient)
- Falls back to exp/log for fractional exponents
- Handles special cases: 0^0, negative bases, etc.
- Auto-discovered by compiler stdlib system

Algorithm:
1. Handle special cases (exponent=0, base=0, base=1)
2. For integer exponents: use binary exponentiation
3. For fractional exponents: use exp(exponent * log(base))
4. Handle negative bases and negative exponents

## Documentation

The `pow()` function raises a number to a power.

### Syntax

```maxon
pow(base, exponent)
```

Parameters:
- `base` - The base number (float)
- `exponent` - The exponent (float)

Returns base raised to the power of exponent.

### Example

```maxon
function main() int
    var result = pow(2.0, 3.0)  // 2^3 = 8
    return trunc(result)
end 'main'
```

### Notes

- Both parameters are floats
- Integer inputs are automatically promoted to float
- Returns float result
- Special cases:
  - `pow(x, 0.0)` returns 1.0 for any x
  - `pow(0.0, y)` returns 0.0 for positive y
  - `pow(1.0, y)` returns 1.0 for any y

## Tests

<!-- test: basic -->
```maxon
function main() int
    var result = pow(2.0, 3.0)
    return trunc(result)
end 'main'
```
```
ExitCode: 8
```

<!-- test: square -->
```maxon
function main() int
    var result = pow(5.0, 2.0)
    return trunc(result)
end 'main'
```
```
ExitCode: 25
```

<!-- test: zero-exponent -->
```maxon
function main() int
    var result = pow(123.0, 0.0)
    return trunc(result)
end 'main'
```
```
ExitCode: 1
```

<!-- test: fractional-exponent -->
```maxon
function main() int
    var result = pow(4.0, 0.5)  // Square root
    print_float(result, 1)
    return 0
end 'main'
```
```
Stdout: 2.0
ExitCode: 0
```

<!-- test: int-promotion -->
```maxon
function main() int
    var result = pow(3, 2)  // Ints promoted to float
    return trunc(result)
end 'main'
```
```
ExitCode: 9
```
