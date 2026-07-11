---
feature: unary-operators
status: selfhosted
keywords: [operators, unary, negate, minus]
category: operators
milestone: M3
---

# Unary Operators

## Documentation

Unary operators operate on a single value.

### Operators

- `-` — Negate (flip the sign of a number)

At M3 unary minus is a prefix operator that binds tighter than any binary
operator (it is the leaf of the Pratt precedence climber). Its operand is a
PRIMARY, not another unary expression, so `- -x` is a parse error at the second
`-` (E2004): a unary operand must be an operand, and a leading `-` is not one.
Unary `+` (identity) is not yet parsed — no test needs it.

## Tests

The M3 slice of `specs/unary-operators.md`: prefix `-` on a literal and on a
variable, plus the double-negation parse error. `negate-int` (needs `if` + `==`,
M4) and `negate-float` (needs floats + `trunc`, later) are DEFERRED and recorded
(commented out) at the end of this section.

<!-- test: unary-minus -->
```maxon
function main() returns ExitCode
	let x = -42
	let y = -x
	return y
end 'main'
```
```exitcode
42
```

<!-- test: double-negation -->
`- -x` fails at the second `-`: a unary operand is a primary, and a leading `-` is
not a primary.
```maxon
function main() returns ExitCode
	let x = 10
	let y = - -x
	return y
end 'main'
```
```maxoncstderr
error E2004: specs/fragments/unary-operators/double-negation.test:4:12: Expected expression but got '-'
```

<!--
DEFERRED — from `specs/unary-operators.md`. Re-enable once their prerequisites
land:

  - negate-int — needs `if` (M4) and `==` (M4, comparison feeds a branch).
  - negate-float — needs float literals + float negation (XMM) + `trunc`, later.

<!-- test: negate-int -->
```maxon
function main() returns ExitCode
	let x = -42
	let y = -x
	if y == 42 'check'
		return 0
	end 'check'
	return 1
end 'main'
```
```exitcode
0
```

<!-- test: negate-float -->
```maxon
function main() returns ExitCode
	let x = -3.5
	let y = -x
	let result = trunc(y)
	return result
end 'main'
```
```exitcode
3
```
-->
