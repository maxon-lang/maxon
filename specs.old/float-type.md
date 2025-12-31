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
- Supports comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
- Cannot be cast to `int`; use `trunc()` for truncation toward zero
- Math intrinsics require float arguments: `sin`, `cos`, `tan`, `sqrt`, `abs`, `floor`, `ceil`, `round`, `trunc`
- Arithmetic operations support mixed int/float (int promoted to float)
- Comparisons require exact type match (no implicit promotion between int and float)

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
function circleArea(radius float) returns float
    return 3.14159 * radius * radius
end 'circleArea'

function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
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
function main() returns int
    var x = 5
    var y = 2.0
    var result = x + y
    return trunc(result)
end 'main'
```
```exitcode
7
```


<!-- test: many-float-params -->
```maxon
// Test function with 12 float parameters
// On Windows x64: first 4 arrive in XMM0-3, rest on caller's stack
// This tests both register and stack-passed float parameters
function sumFloats(a float, b float, c float, d float, e float, f float, g float, h float, i float, j float, k float, l float) returns float
    return a + b + c + d + e + f + g + h + i + j + k + l
end 'sumFloats'

function main() returns int
    var result = sumFloats(1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0, 11.0, 12.0)
    return trunc(result)
end 'main'
```
```exitcode
78
```


<!-- test: float-params-with-call -->
```maxon
// Test float parameters that must survive across a function call
// The float params are live across the call to identity(), requiring callee-saved XMMs
function identity(x float) returns float
    return x
end 'identity'

function useFloats(a float, b float, c float, d float) returns float
    // The call to identity() may clobber XMM0-3
    // So a, b, c, d must be saved to callee-saved XMM6-9 or stack
    var temp = identity(1.0)
    return a + b + c + d + temp
end 'useFloats'

function main() returns int
    var result = useFloats(10.0, 20.0, 30.0, 40.0)
    return trunc(result)
end 'main'
```
```exitcode
101
```

