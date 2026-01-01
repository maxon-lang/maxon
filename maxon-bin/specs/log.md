---
feature: log
status: stable
keywords: [math, logarithm, natural-log]
category: stdlib
---

# Natural Logarithm Function

## Developer Notes

The `log()` function calculates the natural logarithm (base e) of a number.

Implementation:
- Defined in `stdlib/Math.maxon`
- Signature: `Math.log(x float) float`
- Uses Taylor series approximation
- Normalizes input to range [0.5, 1.0) for faster convergence
- Uses transformation: log(x) = log(x/2^n) + n*log(2)
- Auto-discovered by compiler stdlib system

Algorithm:
1. Handle special cases (x <= 0, x = 1)
2. Normalize x to [0.5, 1.0) by dividing/multiplying by 2
3. Use Taylor series: log(y) = 2 * sum((z^(2k+1))/(2k+1)) where z = (y-1)/(y+1)
4. Add back n*log(2) from normalization step

## Documentation

The `log()` function calculates the natural logarithm (ln) of a number.

### Syntax

```maxon
Math.log(x)
```
Parameters:
- `x` - The value to take the logarithm of (must be positive)

Returns the natural logarithm of x.

### Example

```maxon
function main() returns int
    var e = 2.71828
    var result = Math.log(e)  // ln(e) ≈ 1.0
    return trunc(result)
end 'main'
```
```exitcode
0
```


### Notes

- Input must be positive (x > 0)
- Returns 0.0 for x = 1.0
- Returns 0.0 for invalid inputs (x <= 0)
- Uses natural logarithm (base e), not base 10

## Tests

<!-- test: ln-of-e -->
```maxon
function main() returns int
    var e = 2.71828
    var result = Math.log(e)
    print("{result}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
0.999999
```


<!-- test: ln-of-one -->
```maxon
function main() returns int
    var result = Math.log(1.0)
    return trunc(result)
end 'main'
```
```exitcode
0
```


<!-- test: ln-of-large -->
```maxon
function main() returns int
    var result = Math.log(100.0)  // ln(100) ≈ 4.6
    print("{result}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
4.60517
```


<!-- test: int-promotion -->
```maxon
function main() returns int
    var result = Math.log(10)  // Int promoted to float
    print("{result}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
2.302585
```

