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

<!-- test: round.rt-basic -->
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

<!-- test: round.rt-negative -->
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

<!-- test: round.rt-halfway -->
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

<!-- test: round.a-declaration-takes-the-name-back -->
⭐⭐ **A FILE THAT DECLARES `round` OWNS THE NAME.** The eight bare math intrinsics have no declaration of
their own, so nothing downstream can notice that a user's `function round` was never linked: the call
emitted the machine instruction, the declaration sat unreachable, and there was no diagnostic anywhere.
The declaration wins, and every bare-name builtin follows the same rule from one gate in
`Parser.parseCallNamed` — the ordinary argument-label rule comes back with it, since a declaration has
real parameters for a label to name.

⚠ **BOTH REFERENCE COMPILERS RESERVE THE NAME UNCONDITIONALLY** and leave the shadowed declaration
unreachable and undiagnosed; this is a deliberate divergence, taken because a shadowed declaration has no
other symptom. A QUALIFIED callee was never affected — `Point.round` is not `round` — and nothing in
`stdlib/`, `specs/` or `specs-shv2/` declares one of the eight, so no working program moves.
```maxon
typealias Tally = int(0 to 1000)

function round(n Tally) returns Tally
	return n + 1
end 'round'

function main() returns ExitCode
	return round(10)
end 'main'
```
```exitcode
11
```
