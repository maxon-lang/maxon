---
feature: log
status: stable
keywords: [math, logarithm, natural-log]
category: stdlib
---

# Natural Logarithm Function

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

