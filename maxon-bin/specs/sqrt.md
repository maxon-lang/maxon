---
feature: sqrt
status: stable
keywords: sqrt, square root, math
category: math-intrinsic
---
## Documentation

# sqrt

Calculate the square root of a number.

**Signature:** `sqrt(x float) float`

**Parameters:**
- `x` - The number to take the square root of (must be non-negative)

**Returns:** The square root of the input

**Example:**

```maxon
var x = 16.0
var y = sqrt(x)      // 4.0

var z = 2.0
var w = sqrt(z)      // 1.414213... (√2)
```
**Notes:**
- Input must be non-negative (undefined behavior for negative values)
- `sqrt(0.0)` returns `0.0`
- `sqrt(1.0)` returns `1.0`
- For integer inputs, the value is automatically promoted to float

## Tests

<!-- test: sqrt.basic -->
```maxon
function main() returns int
    var x = sqrt(16.0)
    return trunc(x)
end 'main'
```
```exitcode
4
```

<!-- test: sqrt.precision -->
```maxon
function main() returns int
    var x = sqrt(2.0)
    print("{x}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
1.414213
```

<!-- test: sqrt.zero -->
```maxon
function main() returns int
    var result = sqrt(0.0)
    if result == 0.0 'check'
        return 0
    end 'check'
    return 1
end 'main'
```
```exitcode
0
```

<!-- test: sqrt.with-int-promotion -->
```maxon
function main() returns int
    var x = 16  // int
    var result = sqrt(x)  // x promoted to 16.0
    return trunc(result)
end 'main'
```
```exitcode
4
```
