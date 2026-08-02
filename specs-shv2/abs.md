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
	let neg = -5.5
	let x = abs(neg)
	let y = trunc(x)
	return y
end 'main'
```
```exitcode
5
```

<!-- test: abs.negative-int-as-float -->
```maxon
function main() returns ExitCode
	let neg = -42.0
	let x = abs(neg)
	return trunc(x)
end 'main'
```
```exitcode
42
```

<!-- test: abs.zero -->
```maxon
function main() returns ExitCode
	let x = abs(0.0)
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
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
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
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
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
	let x = try float.fromString(try args.get(1) otherwise "") otherwise 0.0
	let result = abs(x)
	if result == 0.0 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: abs.error-arg-named -->
The positional-argument rule is the MATH BUILTIN FAMILY's, not `min`'s alone: `abs`
has no declaration either, so its one argument has no parameter for `x:` to name.
Arity is irrelevant to the reason — a builtin has nothing to label at any position —
so the arity-1 intrinsics answer with the same E2067 the arity-2 `min` does rather
than with E2052, whose wording ("only the second and later arguments take 'name:'
labels") would invite the nonsense repair `abs(1.0, x: ...)` on a one-argument callee.

⚠ shv2-authored and ADDITIVE — not in `/specs/abs.md`, and no expectation is retracted.
Measured: the bootstrap answers this program `E2004: Undefined variable 'x'`, reading
the label as a variable reference, so the divergence is shv2 naming the real defect
where the bootstrap names a consequence of it.
```maxon
function main() returns ExitCode
	let x = abs(x: 1.0)
	return trunc(x)
end 'main'
```
```maxoncstderr
error E2067: <fragment>:3:14: a builtin's arguments are all positional and cannot be named ('name:' labels a parameter, and a builtin has no declaration to have one)
```
