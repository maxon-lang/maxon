---
feature: log10
status: stable
keywords: log10, logarithm, base-10, math
category: stdlib
---
# log10

## Documentation

Calculate the base-10 logarithm of a number.

**Signature:** `Math.log10(x float) float`

**Parameters:**
- `x` - The number to take the base-10 logarithm of (must be positive)

**Returns:** The base-10 logarithm of the input

**Example:**

```maxon
var x = 100.0
var y = Math.log10(x)     // 2.0 (10^2 = 100)

var z = 1000.0
var w = Math.log10(z)     // 3.0 (10^3 = 1000)

var a = 10.0
var b = Math.log10(a)     // 1.0 (10^1 = 10)
```
**Notes:**
- Input must be positive (returns NaN for negative values)
- `log10(0.0)` returns negative infinity
- `log10(1.0)` returns `0.0`
- `log10(10.0)` returns `1.0`
- For integer inputs, the value is automatically promoted to float

## Tests

<!-- test: log10.basic -->
```maxon
function main() returns int
  var x = Math.log10(100.0)
  print("{x}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
1.999999
```

<!-- test: log10.one-thousand -->
```maxon
function main() returns int
  var x = Math.log10(1000.0)
  print("{x}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
2.999999
```

<!-- test: log10.ten -->
```maxon
function main() returns int
  var x = Math.log10(10.0)
  print("{x}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0.999999
```

<!-- test: log10.one -->
```maxon
function main() returns int
  var result = Math.log10(1.0)
  if result == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: log10.precision -->
```maxon
function main() returns int
  var x = Math.log10(2.0)
  print("{x}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
0.301029
```

<!-- test: log10.with-int-promotion -->
```maxon
function main() returns int
  var x = 100  // int
  var result = Math.log10(x)  // x promoted to 100.0
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

<!-- test: log10.large-value -->
```maxon
function main() returns int
  var x = Math.log10(10000.0)
  print("{x}\n")
  return 0
end 'main'
```
```exitcode
0
```
```stdout
3.999999
```
