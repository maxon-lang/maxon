---
feature: pow
status: stable
keywords: [math, power, exponentiation]
category: stdlib
---

# Power Function

## Documentation

The `pow()` function raises a number to a power.

### Syntax

```maxon
Math.pow(base, exponent: exponent)
```
Parameters:
- `base` - The base number (float)
- `exponent` - The exponent (float)

Returns base raised to the power of exponent.

### Example

```maxon
function main() returns int
  var result = Math.pow(2.0, exponent: 3.0)  // 2^3 = 8
  return trunc(result)
end 'main'
```
```exitcode
8
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
function main() returns int
  var result = Math.pow(2.0, exponent: 3.0)
  return trunc(result)
end 'main'
```
```exitcode
8
```


<!-- test: square -->
```maxon
function main() returns int
  var result = Math.pow(5.0, exponent: 2.0)
  return trunc(result)
end 'main'
```
```exitcode
25
```


<!-- test: zero-exponent -->
```maxon
function main() returns int
  var result = Math.pow(123.0, exponent: 0.0)
  return trunc(result)
end 'main'
```
```exitcode
1
```


<!-- test: fractional-exponent -->
```maxon
function main() returns int
  var result = Math.pow(4.0, exponent: 0.5)  // Square root
  print("{result}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1.999999
```


<!-- test: int-promotion -->
```maxon
function main() returns int
  var result = Math.pow(3, exponent: 2)  // Ints promoted to float
  return trunc(result)
end 'main'
```
```exitcode
9
```

