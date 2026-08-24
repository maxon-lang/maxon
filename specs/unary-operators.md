---
feature: unary-operators
status: stable
keywords: [operators, unary, negate, plus, minus]
category: operators
---

# Unary Operators

## Documentation

Unary operators operate on a single value.

### Operators

- `-` - Negate (flip sign of number)
- `+` - Identity (no change, rarely used)

### Example

```maxon
function main() returns ExitCode
	let x = 42
	let y = -x      // y is -42
	let z = -y      // z is 42
	return z
end 'main'
```
```exitcode
42
```


## Tests

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
Negation of a `float`, brought back to an `ExitCode` by `trunc`. `-(-3.5)` is `3.5` and `trunc`
truncates toward zero, so the program exits 3.

⚠ **The note here used to read "Float negation is not yet implemented in codegen", and it was the
reason the whole case sat inside an HTML comment — with the closing `-->` below the fences, so this
runner's marker regex (which requires the end token) never matched it and the case did not run.**
Floats landed; measured 2026-08-23, this program exits 3.
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


<!-- test: double-negation -->
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
