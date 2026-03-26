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
	var x = 42
	var y = -x      // y is -42
	var z = -y      // z is 42
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
	var x = -42
	var y = -x
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
	var x = -3.5
	var y = -x
	var result = trunc(y)
	return result
end 'main'
```
```exitcode
3
```
-->


<!-- test: double-negation -->
```maxon
function main() returns ExitCode
	var x = 10
	var y = - -x
	return y
end 'main'
```
```exitcode
10
```
