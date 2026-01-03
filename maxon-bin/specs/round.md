---
feature: round
status: stable
keywords: round, rounding, math, conversion
category: math-intrinsic
---
# round

## Documentation

Round a floating-point number to the nearest integer.

**Signature:** `round(x float) int`

**Parameters:**
- `x` - The floating-point number to round

**Returns:** The nearest integer value

**Example:**

```maxon
var x = 3.5
var y = round(x)     // 4 (rounds to nearest)

var z = 2.4
var w = round(z)     // 2
```
**Notes:**
- Rounds to the nearest integer
- For halfway cases (e.g., 2.5), rounds to nearest even number
- `round(3.7)` returns `4`
- `round(-2.3)` returns `-2`

## Tests

<!-- test: round.basic -->
```maxon
function main() returns int
    var x = 3.7
    var y = round(x)
    return y
end 'main'
```
```exitcode
4
```

<!-- test: round.negative -->
```maxon
function main() returns int
    var neg = 0.0 - 2.3
    var y = round(neg)
    print("{y}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
-2
```

<!-- test: round.halfway -->
```maxon
function main() returns int
    var x = 2.5
    var y = round(x)
    print("{y}\n")
    return 0
end 'main'
```
```exitcode
0
```
```stdout
3
```

