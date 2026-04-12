---
feature: min
status: stable
keywords: min, minimum, math
category: math-intrinsic
---
# min

## Documentation

Returns the smaller of two floating-point values.

**Signature:** `min(a float, b float) float`

**Parameters:**
- `a` - First value to compare
- `b` - Second value to compare

**Returns:** The smaller of the two input values

**Example:**

```maxon
var x = min(3.0, 5.0)    // 3.0
var y = min(10.0, 2.5)   // 2.5
var z = min(-1.0, 1.0)   // -1.0
```

**Notes:**
- For integer inputs, values are automatically promoted to float
- If both values are equal, returns that value
- Works with negative numbers

## Tests

<!-- test: min.basic -->
```maxon
function main() returns ExitCode
	let x = min(3.0, 5.0)
	return trunc(x)
end 'main'
```
```exitcode
3
```

<!-- test: min.second-smaller -->
```maxon
function main() returns ExitCode
	let x = min(10.0, 2.0)
	return trunc(x)
end 'main'
```
```exitcode
2
```

<!-- test: min.negative -->
```maxon
function main() returns ExitCode
	let x = min(-5.0, 3.0)
	print("{trunc(x)}\n")
	return 0
end 'main'
```
```stdout
-5
```

<!-- test: min.both-negative -->
```maxon
function main() returns ExitCode
	let x = min(-2.0, -8.0)
	print("{trunc(x)}\n")
	return 0
end 'main'
```
```stdout
-8
```

<!-- test: min.equal-values -->
```maxon
function main() returns ExitCode
	let x = min(7.0, 7.0)
	return trunc(x)
end 'main'
```
```exitcode
7
```

<!-- test: min.fractional -->
```maxon
function main() returns ExitCode
	let x = min(3.5, 5.2)
	// min of 3.5 and 5.2 is 3.5, trunc gives 3
	return trunc(x)
end 'main'
```
```exitcode
3
```

<!-- test: min.zero -->
```maxon
function main() returns ExitCode
	let x = min(0.0, 5.0)
	if x == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: min.rt-basic -->
<!-- Args: 3.0 5.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	return trunc(min(a, b))
end 'main'
```
```exitcode
3
```

<!-- test: min.rt-second-smaller -->
<!-- Args: 10.0 2.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	return trunc(min(a, b))
end 'main'
```
```exitcode
2
```

<!-- test: min.rt-negative -->
<!-- Args: -5.0 3.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	return trunc(min(a, b)) + 10
end 'main'
```
```exitcode
5
```

<!-- test: min.rt-both-negative -->
<!-- Args: -2.0 -8.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	return trunc(min(a, b)) + 10
end 'main'
```
```exitcode
2
```

<!-- test: min.rt-equal -->
<!-- Args: 7.0 7.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	return trunc(min(a, b))
end 'main'
```
```exitcode
7
```

<!-- test: min.rt-fractional -->
<!-- Args: 3.5 5.2 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	return trunc(min(a, b))
end 'main'
```
```exitcode
3
```

<!-- test: min.rt-zero -->
<!-- Args: 0.0 5.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let a = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let b = try float.fromString(try args.get(2) otherwise "") otherwise 0.0
	let result = min(a, b)
	if result == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```
