---
feature: exp
status: stable
keywords: [math, exponential, euler]
category: stdlib
---

# Exponential Function

## Developer Notes

The `exp()` function calculates e raised to a power (e^x) where e is Euler's number.

Implementation:
- Defined in `stdlib/Math.maxon`
- Signature: `Math.exp(x float) float`
- Uses Taylor series: e^x = 1 + x + x^2/2! + x^3/3! + ...
- Reduces large x values using exp(x) = exp(x/2)^2
- Handles negative x using exp(-x) = 1/exp(x)
- Auto-discovered by compiler stdlib system

Algorithm:
1. Handle special case (x = 0 returns 1)
2. Handle negative x by computing exp(-x) and inverting
3. Reduce x to smaller range by halving repeatedly
4. Compute exp(x) using Taylor series for small x
5. Square result for each reduction step

## Documentation

The `exp()` function calculates e raised to a power (e^x).

### Syntax

```maxon
Math.exp(x)
```
Parameters:
- `x` - The exponent (float)

Returns e raised to the power of x, where e ≈ 2.71828 (Euler's number).

### Example

```maxon
function main() returns int
    var result = Math.exp(0.0)  // e^0 = 1
    return trunc(result)
end 'main'
```
```exitcode
1
```


### Notes

- Returns 1.0 for x = 0
- Can handle both positive and negative exponents
- Result is always positive
- Integer inputs are automatically promoted to float

## Tests

<!-- test: exp-zero -->
```maxon
function main() returns int
    var result = Math.exp(0.0)
    return trunc(result)
end 'main'
```
```exitcode
1
```


<!-- test: exp-one -->
```maxon
function main() returns int
    var result = Math.exp(1.0)  // e^1 ≈ 2.71828
    return trunc(result)
end 'main'
```
```exitcode
2
```


<!-- test: exp-two -->
```maxon
function main() returns int
    var result = Math.exp(2.0)  // e^2 ≈ 7.389
    return trunc(result)
end 'main'
```
```exitcode
7
```


<!-- test: int-promotion -->
```maxon
function main() returns int
    var result = Math.exp(3)  // Int promoted to float
    return trunc(result)
end 'main'
```
```exitcode
20
```


<!-- test: negative -->
```maxon
function main() returns int
    var result = Math.exp(-1.0)  // e^-1 ≈ 0.368
    return trunc(result)
end 'main'
```
```exitcode
0
```

