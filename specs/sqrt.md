---
feature: sqrt
status: stable
keywords: sqrt, square root, math
category: math-intrinsic
---
# sqrt

## Documentation

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
- Use `trunc(sqrt(x))` to get an integer result

## Tests

<!-- test: sqrt.basic -->
```maxon
function main() returns ExitCode
  var x = sqrt(16.0)
  return trunc(x)
end 'main'
```
```exitcode
4
```

<!-- test: sqrt.precision -->
```maxon
function main() returns ExitCode
  var x = sqrt(2.0)
  // sqrt(2) * sqrt(2) should be approximately 2
  var check = x * x
  // Trunc to get integer 2
  return trunc(check)
end 'main'
```
```exitcode
2
```

<!-- test: sqrt.zero -->
```maxon
function main() returns ExitCode
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

<!-- test: sqrt.rt-basic -->
<!-- Args: 16.0 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(sqrt(x))
end 'main'
```
```exitcode
4
```

<!-- test: sqrt.rt-precision -->
<!-- Args: 2.0 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  var check = sqrt(x) * sqrt(x)
  return trunc(check)
end 'main'
```
```exitcode
2
```

<!-- test: sqrt.rt-zero -->
<!-- Args: 0.0 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  var result = sqrt(x)
  if result == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```
