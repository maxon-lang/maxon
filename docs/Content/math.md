# Mathematical Functions

Maxon provides built-in mathematical functions as language keywords. These functions are compiled to efficient LLVM intrinsics.

## Type Conversion Functions

Convert floating-point numbers to integers using different rounding strategies.

### trunc

Truncate a float to an integer (toward zero).

**Signature:** `trunc(x float) int`

```maxon
var x = 3.7
var y = trunc(x)     // 3

var neg = -3.7
var z = trunc(neg)   // -3 (toward zero)
```

ExitCode: 3
```maxon
function main() int
    var x = 3.9
    return trunc(x)  // Returns 3
end 'main'
```

### round

Round a float to the nearest integer.

**Signature:** `round(x float) int`

```maxon
var x = 3.5
var y = round(x)     // 4 (rounds to nearest)

var z = 2.4
var w = round(z)     // 2
```

ExitCode: 4
```maxon
function main() int
    var x = 3.6
    return round(x)  // Returns 4
end 'main'
```

### floor

Round down to the nearest integer (toward negative infinity).

**Signature:** `floor(x float) int`

```maxon
var x = 3.9
var y = floor(x)     // 3

var neg = -3.2
var z = floor(neg)   // -4 (rounds down)
```

ExitCode: 3
```maxon
function main() int
    var x = 3.9
    return floor(x)  // Returns 3
end 'main'
```

### ceil

Round up to the nearest integer (toward positive infinity).

**Signature:** `ceil(x float) int`

```maxon
var x = 3.1
var y = ceil(x)      // 4

var neg = -3.9
var z = ceil(neg)    // -3 (rounds up)
```

ExitCode: 4
```maxon
function main() int
    var x = 3.1
    return ceil(x)   // Returns 4
end 'main'
```

### Conversion Comparison

```maxon
var x = 3.7

trunc(x)   // 3  - truncate toward zero
round(x)   // 4  - round to nearest
floor(x)   // 3  - round down
ceil(x)    // 4  - round up

var neg = -3.7

trunc(neg) // -3 - toward zero
round(neg) // -4 - nearest (tie toward even)
floor(neg) // -4 - down (more negative)
ceil(neg)  // -3 - up (less negative)
```

ExitCode: 0
```maxon
function main() int
    var x = 3.7
    var sum = trunc(x) + round(x) + floor(x) + ceil(x)
    // 3 + 4 + 3 + 4 = 14
    return sum - 14
end 'main'
```

## Arithmetic Functions

### sqrt

Calculate the square root of a number.

**Signature:** `sqrt(x float) float`

```maxon
var x = 16.0
var y = sqrt(x)      // 4.0

var z = 2.0
var w = sqrt(z)      // 1.414213...
```

ExitCode: 4
```maxon
function main() int
    var x = 16.0
    var result = sqrt(x)
    return trunc(result)  // Returns 4
end 'main'
```

### abs

Calculate the absolute value of a number.

**Signature (float):** `abs(x float) float`  
**Signature (int):** `abs(x int) int`

```maxon
var x = -5.5
var y = abs(x)       // 5.5

var i = -42
var j = abs(i)       // 42
```

ExitCode: 5
```maxon
function main() int
    var x = -5.3
    var result = abs(x)
    return trunc(result)  // Returns 5
end 'main'
```

### pow

Raise a number to a power.

**Signature:** `pow(base float, exponent float) float`

```maxon
var x = pow(2.0, 3.0)    // 8.0
var y = pow(10.0, 2.0)   // 100.0
var z = pow(4.0, 0.5)    // 2.0 (square root)
```

ExitCode: 8
```maxon
function main() int
    var result = pow(2.0, 3.0)
    return trunc(result)  // Returns 8
end 'main'
```

## Trigonometric Functions

All trigonometric functions work with radians.

### sin

Calculate the sine of an angle (in radians).

**Signature:** `sin(x float) float`

```maxon
var x = 0.0
var y = sin(x)       // 0.0

// Note: π ≈ 3.14159265
var halfPi = 1.5708  // π/2
var z = sin(halfPi)  // 1.0
```

### cos

Calculate the cosine of an angle (in radians).

**Signature:** `cos(x float) float`

```maxon
var x = 0.0
var y = cos(x)       // 1.0

var pi = 3.14159265
var z = cos(pi)      // -1.0
```

### tan

Calculate the tangent of an angle (in radians).

**Signature:** `tan(x float) float`

```maxon
var x = 0.0
var y = tan(x)       // 0.0

var quarterPi = 0.785398  // π/4
var z = tan(quarterPi)    // 1.0
```

## Logarithmic and Exponential Functions

### log

Calculate the natural logarithm (base e).

**Signature:** `log(x float) float`

```maxon
var e = 2.71828
var x = log(e)       // 1.0

var y = log(1.0)     // 0.0
```

### exp

Calculate e raised to a power.

**Signature:** `exp(x float) float`

```maxon
var x = exp(0.0)     // 1.0
var y = exp(1.0)     // 2.71828... (e)
var z = exp(2.0)     // 7.38906... (e²)
```

## Implicit Type Promotion

In mixed int/float expressions, integers are automatically promoted to floats:

```maxon
var x = 5            // int
var y = sqrt(x)      // 5 promoted to 5.0, y is float (2.236...)
var z = pow(2, 3.0)  // 2 promoted to 2.0
```

ExitCode: 7
```maxon
function main() int
    var x = 5            // int
    var y = 2.5          // float
    var sum = x + y      // x promoted to 5.0, sum is 7.5
    return trunc(sum)    // Returns 7
end 'main'
```

## Float Literal Requirements

Float literals **must** include a decimal point and use a leading zero:

```maxon
var valid = 0.5      // ✓ Valid
var invalid = .5     // ✗ Invalid - must use 0.5

var x = 42           // int
var y = 42.0         // float
```

ExitCode: 42
```maxon
function main() int
    var x = 42.0         // float
    return trunc(x)      // Returns 42
end 'main'
```

## Practical Examples

### Calculate Hypotenuse

```maxon
function hypotenuse(a float, b float) float
    var a_squared = a * a
    var b_squared = b * b
    var c_squared = a_squared + b_squared
    return sqrt(c_squared)
end 'hypotenuse'
```

ExitCode: 5
```maxon
function hypotenuse(a float, b float) float
    var a_sq = a * a
    var b_sq = b * b
    return sqrt(a_sq + b_sq)
end 'hypotenuse'

function main() int
    var result = hypotenuse(3.0, 4.0)
    return trunc(result)  // Returns 5
end 'main'
```

### Distance Between Points

```maxon
function distance(x1 float, y1 float, x2 float, y2 float) float
    var dx = x2 - x1
    var dy = y2 - y1
    return sqrt((dx * dx) + (dy * dy))
end 'distance'
```

### Rounding to Decimal Places

```maxon
function roundToDecimal(value float, places int) float
    // Round to N decimal places
    var multiplier = pow(10.0, places)
    var shifted = value * multiplier
    var rounded = round(shifted)
    return rounded / multiplier
end 'roundToDecimal'
```

ExitCode: 0
```maxon
function roundToDecimal(value float, places int) float
    var mult = pow(10.0, places)
    var shifted = value * mult
    var rounded_val = round(shifted)
    return rounded_val / mult
end 'roundToDecimal'

function main() int
    var x = 3.14159
    var rounded = roundToDecimal(x, 2)  // 3.14
    var expected = 3.14
    var diff = abs(rounded - expected)
    if diff < 0.01
        return 0
    end 'check'
    return 1
end 'main'
```

### Clamp Value to Range

```maxon
function clamp(value float, min float, max float) float
    if value < min
        return min
    end 'low'
    if value > max
        return max
    end 'high'
    return value
end 'clamp'
```

ExitCode: 5
```maxon
function clamp(value float, min float, max float) float
    if value < min return min
    if value > max return max
    return value
end 'clamp'

function main() int
    var x = 10.0
    var clamped = clamp(x, 0.0, 5.0)
    return trunc(clamped)  // Returns 5
end 'main'
```

## Function Summary

| Function | Signature | Description |
|----------|-----------|-------------|
| `trunc` | `(float) int` | Truncate to integer (toward zero) |
| `round` | `(float) int` | Round to nearest integer |
| `floor` | `(float) int` | Round down to integer |
| `ceil` | `(float) int` | Round up to integer |
| `sqrt` | `(float) float` | Square root |
| `abs` | `(float) float` or `(int) int` | Absolute value |
| `pow` | `(float, float) float` | Power function |
| `sin` | `(float) float` | Sine (radians) |
| `cos` | `(float) float` | Cosine (radians) |
| `tan` | `(float) float` | Tangent (radians) |
| `log` | `(float) float` | Natural logarithm |
| `exp` | `(float) float` | Exponential (e^x) |

All math functions are compiled to efficient LLVM intrinsics for optimal performance.
