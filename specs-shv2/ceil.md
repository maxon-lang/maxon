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
	let x = 3.1
	let y = trunc(ceil(x))
	return y
end 'main'
```
```exitcode
4
```

<!-- test: ceil.negative -->
```maxon
function main() returns ExitCode
	let neg = -3.9
	let y = trunc(ceil(neg))
	return y + 10
end 'main'
```
```exitcode
7
```

<!-- test: ceil.exact -->
```maxon
function main() returns ExitCode
	let x = 5.0
	let y = trunc(ceil(x))
	return y
end 'main'
```
```exitcode
5
```

<!-- disabled-test: ceil.rt-positive -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: 3.1 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(ceil(x))
end 'main'
```
```exitcode
4
```

<!-- disabled-test: ceil.rt-negative -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: -3.9 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(ceil(x)) + 10
end 'main'
```
```exitcode
7
```

<!-- disabled-test: ceil.rt-exact -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: 5.0 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(ceil(x))
end 'main'
```
```exitcode
5
```
