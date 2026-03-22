---
feature: round
status: stable
keywords: round, rounding, math, conversion
category: math-intrinsic
---
# round

## Documentation

Round a floating-point number to the nearest integer.

**Signature:** `round(x float) float`

**Parameters:**
- `x` - The floating-point number to round

**Returns:** The nearest integer value (as a float)

**Example:**

```maxon
var x = 3.5
var y = round(x)     // 4.0 (rounds to nearest)

var z = 2.4
var w = round(z)     // 2.0

// To get an int result:
var i = trunc(round(x))   // 4
```
**Notes:**
- Rounds to the nearest integer
- For halfway cases (e.g., 2.5), rounds to nearest even number (banker's rounding)
- `round(3.7)` returns `4.0`
- `round(-2.3)` returns `-2.0`
- Use `trunc(round(x))` to get an integer result

## Tests

<!-- test: round.basic -->
```maxon
function main() returns ExitCode
  var x = 3.7
  var y = trunc(round(x))
  return y
end 'main'
```
```exitcode
4
```

<!-- test: round.negative -->
```maxon
function main() returns ExitCode
  var neg = -2.3
  var y = trunc(round(neg))
  return y + 10
end 'main'
```
```exitcode
8
```

<!-- test: round.halfway -->
```maxon
function main() returns ExitCode
  var x = 2.5
  var y = trunc(round(x))
  return y
end 'main'
```
```exitcode
2
```

<!-- test: round.rt-basic -->
<!-- Args: 3.7 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(round(x))
end 'main'
```
```exitcode
4
```

<!-- test: round.rt-negative -->
<!-- Args: -2.3 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(round(x)) + 10
end 'main'
```
```exitcode
8
```

<!-- test: round.rt-halfway -->
<!-- Args: 2.5 -->
```maxon
function main() returns ExitCode
  let args = CommandLine.args()
  var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
  return trunc(round(x))
end 'main'
```
```exitcode
2
```
