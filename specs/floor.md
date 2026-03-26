---
feature: floor
status: stable
keywords: floor, rounding, math, conversion
category: math-intrinsic
---
# floor

## Documentation

Round a floating-point number down to the nearest integer (toward negative infinity).

**Signature:** `floor(x float) float`

**Parameters:**
- `x` - The floating-point number to round down

**Returns:** The largest integer value less than or equal to x (as a float)

**Example:**

```maxon
var x = 3.9
var y = floor(x)     // 3.0

var neg = -3.2
var z = floor(neg)   // -4.0 (rounds down toward negative infinity)

// To get an int result:
var i = trunc(floor(x))   // 3
```
**Notes:**
- Always rounds toward negative infinity
- Different from truncation for negative numbers
- `floor(3.9)` returns `3.0`
- `floor(-3.2)` returns `-4.0` (not `-3.0`)
- Use `trunc(floor(x))` to get an integer result

## Tests

<!-- test: floor.positive -->
```maxon
function main() returns ExitCode
	var x = 3.9
	var y = trunc(floor(x))
	return y
end 'main'
```
```exitcode
3
```

<!-- test: floor.negative -->
```maxon
function main() returns ExitCode
	var neg = -3.2
	var y = trunc(floor(neg))
	return y + 10
end 'main'
```
```exitcode
6
```

<!-- test: floor.with-ceil -->
```maxon
function main() returns ExitCode
	var x = 3.7
	var a = trunc(floor(x))
	var b = trunc(ceil(x))
	return a + b
end 'main'
```
```exitcode
7
```

<!-- test: floor.rt-positive -->
<!-- Args: 3.9 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
	return trunc(floor(x))
end 'main'
```
```exitcode
3
```

<!-- test: floor.rt-negative -->
<!-- Args: -3.2 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
	return trunc(floor(x)) + 10
end 'main'
```
```exitcode
6
```

<!-- test: floor.rt-with-ceil -->
<!-- Args: 3.7 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	var x = try float.fromString(try args.get(0) otherwise "") otherwise 0.0
	var a = trunc(floor(x))
	var b = trunc(ceil(x))
	return a + b
end 'main'
```
```exitcode
7
```
