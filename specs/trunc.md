---
feature: trunc
status: stable
keywords: trunc, truncate, rounding, math, conversion
category: math-intrinsic
---
# trunc

## Documentation

Truncate a floating-point number to an integer (toward zero).

**Signature:** `trunc(x float) int`

**Parameters:**
- `x` - The floating-point number to truncate

**Returns:** The integer part of x (fractional part removed)

**Example:**

```maxon
var x = 3.7
var y = trunc(x)     // 3

var neg = -3.7
var z = trunc(neg)   // -3 (toward zero, removes fractional part)
```
**Notes:**
- Truncates toward zero (removes decimal part)
- Different from floor/ceil for negative numbers
- `trunc(3.9)` returns `3`
- `trunc(-3.9)` returns `-3` (not `-4`)
- Equivalent to casting to int in many languages

## Tests

<!-- test: trunc.basic -->
```maxon
function main() returns ExitCode
	let neg = -3.7
	let y = trunc(neg)
	return y + 10
end 'main'
```
```exitcode
7
```

<!-- test: trunc.positive -->
```maxon
function main() returns ExitCode
	let x = 7.9
	let y = trunc(x)
	return y
end 'main'
```
```exitcode
7
```

<!-- test: trunc.zero -->
```maxon
function main() returns ExitCode
	let x = 0.5
	let y = trunc(x)
	return y
end 'main'
```
```exitcode
0
```

<!-- test: trunc.rt-negative -->
<!-- Args: -3.7 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(x) + 10
end 'main'
```
```exitcode
7
```

<!-- test: trunc.rt-positive -->
<!-- Args: 7.9 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(x)
end 'main'
```
```exitcode
7
```

<!-- test: trunc.rt-zero -->
<!-- Args: 0.5 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(x)
end 'main'
```
```exitcode
0
```
