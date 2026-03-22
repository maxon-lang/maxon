---
feature: abs
status: stable
keywords: abs, absolute value, math
category: math-intrinsic
---
# abs

## Documentation

Calculate the absolute value of a number.

**Signature:** `abs(x float) float`

**Parameters:**
- `x` - The number to take the absolute value of (must be float)

**Returns:** The absolute value of the input (always non-negative)

**Example:**

```maxon
var x = -5.5
var y = abs(x)       // 5.5

var i = -42.0
var j = abs(i)       // 42.0

// To get an int result:
var k = trunc(abs(-3.7))   // 3
```
**Notes:**
- `abs(0.0)` returns `0.0`
- `abs(x)` always returns a non-negative value
- Currently only works with float type
- For integer values, convert to float first: `abs(-42.0)`
- Use `trunc(abs(x))` to get an integer result

## Tests

<!-- test: abs.float -->
```maxon
function main() returns ExitCode
  var neg = -5.5
  var x = abs(neg)
  var y = trunc(x)
  return y
end 'main'
```
```exitcode
5
```

<!-- test: abs.negative-int-as-float -->
```maxon
function main() returns ExitCode
  var neg = -42.0
  var x = abs(neg)
  return trunc(x)
end 'main'
```
```exitcode
42
```

<!-- test: abs.zero -->
```maxon
function main() returns ExitCode
  var x = abs(0.0)
  if x == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```

<!-- test: abs.rt-float -->
<!-- Args: -5.5 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(abs(x))
end 'main'
```
```exitcode
5
```

<!-- test: abs.rt-negative-int -->
<!-- Args: -42.0 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(abs(x))
end 'main'
```
```exitcode
42
```

<!-- test: abs.rt-zero -->
<!-- Args: 0.0 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  var result = abs(x)
  if result == 0.0 'check'
    return 0
  end 'check'
  return 1
end 'main'
```
```exitcode
0
```
