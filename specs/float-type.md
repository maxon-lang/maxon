---
feature: float-type
status: stable
keywords: [float, floating-point, double, f64]
category: types
---

# Float Type

## Developer Notes

The `float` type represents double-precision (64-bit) floating-point numbers, mapped to LLVM `double` type.

Key implementation:
- Represented as LLVM `double` (IEEE 754 binary64)
- Float literals must contain a decimal point
- Parsed in `Lexer::readNumber()` when '.' is encountered
- Supports arithmetic: `+`, `-`, `*`, `/` (no modulo)
- Supports comparison: `=`, `!=`, `<`, `>`, `<=`, `>=`
- Can be cast to `int` (truncates toward zero)
- Math intrinsics operate on floats: `sin`, `cos`, `tan`, `sqrt`, `abs`, `floor`, `ceil`, `round`, `trunc`
- Integer values are promoted to float in mixed operations

Used extensively for mathematical computations and scientific calculations.

## Documentation

The `float` type stores 64-bit double-precision floating-point numbers.

### Syntax

```maxon
var pi = 3.14159
let ratio float = 2.5
```
Float literals must include a decimal point:
- Valid: `3.14`, `2.0`, `0.5`
- Invalid: `3` (this is an int)

### Example

```maxon
function circleArea(radius float) float
    return 3.14159 * radius * radius
end 'circleArea'

function main() int
    var area = circleArea(5.0)
    return trunc(area)  // Returns 78
end 'main'
```
```exitcode
78
```


## Tests

<!-- test: basic-float -->
```maxon
function main() int
    var x = 3.14
    var y = 2.0
    var z = x + y
    var result = trunc(z)
    return result
end 'main'
```
```exitcode
5
```


<!-- test: float-comparison -->
```maxon
function main() int
    var x = 3.5
    var y = 2.1
    if x > y 'check'
        return 1
    end 'check'
    return 0
end 'main'
```
```exitcode
1
```


<!-- test: float-arithmetic -->
```maxon
function main() int
    var a = 10.0
    var b = 3.0
    var result = a / b
    return trunc(result)
end 'main'
```
```exitcode
3
```


<!-- test: float-promotion -->
```maxon
function main() int
    var x = 5
    var y = 2.0
    var result = x + y
    return trunc(result)
end 'main'
```
```exitcode
7
```

