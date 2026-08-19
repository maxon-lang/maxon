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
under `## Deferred` below.

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

<!-- test: unary-minus-widened -->
**Negate of a NARROW value in a WIDER context — a wasm codegen regression test.** `r` is an
`ExitCode` (a narrow ranged int) but `-r` is evaluated in an `int`-wide expression, so the neg's
operand is one width and the neg itself a wider one. wasm materializes neg as `0 - operand`, and the
operand must be pushed at the neg's width — coerced when the frontend left it narrower (an int→int
width change carries no IR conversion op, the same gap as a mixed-width `+`). Without the coercion
the emitted core module faults (`i64.sub` over an i32 operand). `base()` is 5, so `-r + 12` = 7.
```maxon
function base() returns ExitCode
	return 5
end 'base'

function main() returns ExitCode
	let r = base()
	return -r + 12
end 'main'
```
```exitcode
7
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
error E2004: <fragment>:4:12: Expected expression but got '-'
```

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


<!-- test: negate-float 
NOTE: Float negation is not yet implemented in codegen
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
