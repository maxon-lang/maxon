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
	let x = 3.9
	let y = trunc(floor(x))
	return y
end 'main'
```
```exitcode
3
```

<!-- test: floor.negative -->
```maxon
function main() returns ExitCode
	let neg = -3.2
	let y = trunc(floor(neg))
	return y + 10
end 'main'
```
```exitcode
6
```

<!-- test: floor.with-ceil -->
```maxon
function main() returns ExitCode
	let x = 3.7
	let a = trunc(floor(x))
	let b = trunc(ceil(x))
	return a + b
end 'main'
```
```exitcode
7
```

<!-- disabled-test: floor.rt-positive -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: 3.9 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(floor(x))
end 'main'
```
```exitcode
3
```

<!-- disabled-test: floor.rt-negative -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: -3.2 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(floor(x)) + 10
end 'main'
```
```exitcode
6
```

<!-- disabled-test: floor.rt-with-ceil -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: 3.7 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let a = trunc(floor(x))
	let b = trunc(ceil(x))
	return a + b
end 'main'
```
```exitcode
7
```
