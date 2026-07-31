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
	let x = 3.7
	let y = trunc(round(x))
	return y
end 'main'
```
```exitcode
4
```

<!-- test: round.negative -->
```maxon
function main() returns ExitCode
	let neg = -2.3
	let y = trunc(round(neg))
	return y + 10
end 'main'
```
```exitcode
8
```

<!-- test: round.halfway -->
```maxon
function main() returns ExitCode
	let x = 2.5
	let y = trunc(round(x))
	return y
end 'main'
```
```exitcode
2
```

<!-- disabled-test: round.rt-basic -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: 3.7 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(round(x))
end 'main'
```
```exitcode
4
```

<!-- disabled-test: round.rt-negative -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: -2.3 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(round(x)) + 10
end 'main'
```
```exitcode
8
```

<!-- disabled-test: round.rt-halfway -->
<!-- `CommandLine.args()` + the spec harness's missing `Args:` directive — this case needs BOTH: shv2's Testing/SpecParser has no handler for `<!-- Args: … -->` and SpecTestRunner spawns the program with an empty argv, and `CommandLine.args()` is undeclared here (`E2015: a member access 'get' on a 'unknown' value`). `float.fromString` is NO LONGER a blocker — A1s-prim landed the `<primitive>.<method>` rewrite and `parsable-interface.md` exercises it. -->
<!-- Args: 2.5 -->
```maxon
function main() returns ExitCode
	let args = CommandLine.args()
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	return trunc(round(x))
end 'main'
```
```exitcode
2
```
