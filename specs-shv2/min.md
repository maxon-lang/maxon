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

<!-- test: min.error-second-arg-named -->
A builtin's arguments are ALL positional, so the ordinary rule that every argument
after the first must carry a `name:` label does not apply here — E2053 is not raised
for `min(3.0, 5.0)`, which is the spelling this whole file uses. Labelling that second
argument is refused instead, by a code of its own: a label names a PARAMETER OF A
DECLARATION, and `min` has no declaration, so there is no parameter for `b:` to name
and nothing downstream that could ever check it.

⚠ This case, `min.error-first-arg-named` below it, and `abs.error-arg-named` in
`specs-shv2/abs.md` are the three shv2-authored cases pinning E2067 — none of them is in
`/specs`, and all three are ADDITIVE coverage rather than a changed expectation. (The
third lives in `abs.md` because the rule is the whole MATH-BUILTIN family's, not `min`'s:
`abs` has no declaration either, so its one argument has no parameter to name.) The bootstrap does
not report E2067 at all: measured, it answers this exact program with
`E2004: Undefined variable 'b'`, because its builtin path parses arguments with a bare
expression parser and reads the label as a variable reference. That names the wrong
thing entirely — the defect is the label, not a missing binding — which is why shv2
registers its own code. `parameter-labels.md` records the same kind of divergence for
the E2052 pair.
```maxon
function main() returns ExitCode
	let x = min(3.0, b: 5.0)
	return trunc(x)
end 'main'
```
```maxoncstderr
error E2067: <fragment>:3:19: a builtin's arguments are all positional and cannot be named ('name:' labels a parameter, and a builtin has no declaration to have one)
```

<!-- test: min.error-first-arg-named -->
The SAME code at the first argument, and that is the point of having a code of its
own rather than reusing E2052. E2052's sentence is *"the first argument cannot be
named; only the second and later arguments take 'name:' labels"* — true of an ordinary
call, and false here in a way that misleads: it invites the reader to move the label to
the second argument, where it is equally meaningless. A builtin takes no label at ANY
position, and one sentence says so at every position.
```maxon
function main() returns ExitCode
	let x = min(a: 3.0, 5.0)
	return trunc(x)
end 'main'
```
```maxoncstderr
error E2067: <fragment>:3:14: a builtin's arguments are all positional and cannot be named ('name:' labels a parameter, and a builtin has no declaration to have one)
```
