---
feature: ceil
status: stable
keywords: ceil, ceiling, rounding, math, conversion
category: math-intrinsic
---
# ceil

## Documentation

Round a floating-point number up to the nearest integer (toward positive infinity).

**Signature:** `ceil(x float) float`

**Parameters:**
- `x` - The floating-point number to round up

**Returns:** The smallest integer value greater than or equal to x (as a float)

**Example:**

```maxon
var x = 3.1
var y = ceil(x)      // 4.0

var neg = -3.9
var z = ceil(neg)    // -3.0 (rounds up toward positive infinity)

// To get an int result:
var i = trunc(ceil(x))   // 4
```
**Notes:**
- Always rounds toward positive infinity
- Different from truncation for negative numbers
- `ceil(3.1)` returns `4.0`
- `ceil(-3.9)` returns `-3.0` (not `-4.0`)
- Use `trunc(ceil(x))` to get an integer result

## Tests

<!-- test: ceil.positive -->
```maxon
function main() returns ExitCode
  var x = 3.1
  var y = trunc(ceil(x))
  return y
end 'main'
```
```exitcode
4
```

<!-- test: ceil.negative -->
```maxon
function main() returns ExitCode
  var neg = 0.0 - 3.9
  var y = trunc(ceil(neg))
  return y + 10
end 'main'
```
```exitcode
7
```

<!-- test: ceil.exact -->
```maxon
function main() returns ExitCode
  var x = 5.0
  var y = trunc(ceil(x))
  return y
end 'main'
```
```exitcode
5
```

<!-- test: ceil.rt-positive -->
<!-- Args: 3.1 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(ceil(x))
end 'main'
```
```exitcode
4
```

<!-- test: ceil.rt-negative -->
<!-- Args: -3.9 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(ceil(x)) + 10
end 'main'
```
```exitcode
7
```

<!-- test: ceil.rt-exact -->
<!-- Args: 5.0 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(ceil(x))
end 'main'
```
```exitcode
5
```
