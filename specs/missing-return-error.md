---
feature: missing-return-error
status: stable
keywords: [return, error, semantic, validation]
category: diagnostics
---

# Missing Return Statement Error

## Documentation

Functions that declare a return type must return a value on all code paths.

### Error Example

```maxon
typealias Score = int(i64.min to i64.max)

function test() returns Score
	// Error: No return statement
end 'test'
```
Error message:
```
Semantic Error: Function 'test' must return a value of type 'int'
Note: All execution paths through the function must end with a return statement
```

### Solution

Ensure every path returns:

```maxon
typealias Score = int(i64.min to i64.max)

function test(x Score) returns Score
	if x > 0 'check'
		return 1
	end 'check'
	return 0  // Handles else case
end 'test'
```
## Tests

<!-- test: no-return -->
```maxon
function main() returns ExitCode
end 'main'
```
```maxoncstderr
error E3013: specs/fragments/missing-return-error/no-return.test:2:10: missing return statement: 'main'
```

<!-- test: missing-else-return -->
```maxon

typealias Integer = int(i64.min to i64.max)

function test(x Integer) returns Integer
	if x > 0 'check'
		return 1
	end 'check'
	// Missing return for else path
end 'test'

function main() returns ExitCode
	return test(5)
end 'main'
```
```maxoncstderr
error E3013: specs/fragments/missing-return-error/missing-else-return.test:5:10: missing return statement: 'test'
```

<!-- test: valid-all-paths -->
```maxon

typealias Integer = int(i64.min to i64.max)

function test(x Integer) returns Integer
	if x > 0 'check'
		return 1
	end 'check'
	return 0
end 'test'

function main() returns ExitCode
	return test(5)
end 'main'
```
```exitcode
1
```

<!-- test: match-binding-return-plus-panic-arm -->
A function whose body is a single `match` with a payload-binding arm that
returns and a sibling `panic(...)` arm — every path diverges or returns and
there is no `default` arm. The unreachable merge block must be treated as
terminated so no missing-return (E3013) is reported.

```maxon
typealias Code = int(0 to 125)

union Op
	systemOp(x Code)
	callOp
end 'Op'

function pick(o Op) returns Code
	match o 'm'
		systemOp(v) then return v
		callOp then panic("no")
	end 'm'
end 'pick'

function main() returns ExitCode
	return pick(Op.systemOp(3))
end 'main'
```
```exitcode
3
```

