---
feature: return-statement
status: stable
keywords: [return, control-flow]
category: statements
---

# Return Statement

## Documentation

The `return` statement exits the current function and optionally returns a value to the caller.

### Syntax

```maxon
return expression
```
### Example

```maxon
typealias Score = int(i64.min to i64.max)

function isPositive(x Score) returns bool
	if x > 0 'check'
		return true
	end 'check'
	return false
end 'isPositive'
```
## Tests

<!-- test: simple-return -->
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```


<!-- test: expression-return -->
```maxon
function main() returns ExitCode
	return 2 + 3 * 4
end 'main'
```
```exitcode
14
```


<!-- test: conditional-return -->
```maxon
function main() returns ExitCode
	let x = 5
	if x > 3 'check'
		return 1
	end 'check'
	return 0
end 'main'
```
```exitcode
1
```

