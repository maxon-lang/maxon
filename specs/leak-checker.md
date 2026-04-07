---
feature: leak-checker
status: experimental
keywords: [leak, memory, allocation, debug]
category: memory
---

# Leak Checker

## Documentation

The Maxon runtime tracks heap allocations and frees at program exit. When the program finishes, if any allocations were not freed, a diagnostic message is printed to stderr showing the number of leaked allocations.

This is a diagnostic tool for verifying that the compiler's automatic memory management is working correctly. Since Maxon manages memory automatically through reference counting and scope-based cleanup, leaks indicate a compiler bug rather than a user error.

The leak checker runs after `main()` returns and before the process exits. It does not change the program's exit code.

### Output Format

When leaks are detected, the following is printed to stderr:

```text
Leak detected: N allocation(s) not freed
```

When there are no leaks, nothing is printed.

## Tests

<!-- test: no-leak.trivial -->
### Trivial program has no leaks
A program with no heap allocations should produce no leak output.
```maxon
function main() returns ExitCode
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: no-leak.string-scope -->
### Strings cleaned up at scope exit
Heap-allocated strings should be properly freed when they go out of scope.
```maxon
function make_string()
	let s = "this string is long enough to require heap allocation for sure and even more text"
	print(s)
end 'make_string'

function main() returns ExitCode
	make_string()
	return 0
end 'main'
```
```exitcode
0
```
```stdout
this string is long enough to require heap allocation for sure and even more text
```

<!-- test: no-leak.array-scope -->
### Arrays cleaned up at scope exit
Arrays should be properly freed when they go out of scope.
```maxon
typealias Integer = int(i64.min to i64.max)
typealias IntArray = Array with Integer

function use_array()
	var arr = IntArray.create()
	arr.push(10)
	arr.push(20)
	arr.push(30)
end 'use_array'

function main() returns ExitCode
	use_array()
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: no-leak.string-return -->
### Returned strings transfer ownership
A string returned from a function should not leak.
```maxon
function greet(name String) returns String
	return "Hello, {name}!"
end 'greet'

function main() returns ExitCode
	let msg = greet("World")
	print(msg)
	return 0
end 'main'
```
```exitcode
0
```
```stdout
Hello, World!
```

<!-- test: no-leak.exit-code-preserved -->
### Exit code is preserved
The leak checker should not change the program's exit code.
```maxon
function main() returns ExitCode
	return 42
end 'main'
```
```exitcode
42
```

<!-- test: no-leak.enum-arg-conditional-use -->
### Enum arguments cleaned up when conditionally used
When a function receives two heap-allocated enum arguments but only uses one
based on a condition, the unused argument must still be freed by the caller.
```maxon
typealias Integer = int(i64.min to i64.max)

enum Op
	addOp(left Integer, right Integer)
	mulOp(left Integer, right Integer)
end 'Op'

function applyOp(useAdd bool, addArg Op, mulArg Op) returns Integer
	if useAdd 'branch'
		return match addArg 'matchAdd'
			addOp(left, right) gives left + right
			mulOp(left, right) gives left * right
		end 'matchAdd'
	end 'branch' else 'other'
		return match mulArg 'matchMul'
			addOp(left, right) gives left + right
			mulOp(left, right) gives left * right
		end 'matchMul'
	end 'other'
end 'applyOp'

function main() returns ExitCode
	let result = applyOp(true, addArg: Op.addOp(3, right: 4), mulArg: Op.mulOp(5, right: 6))
	print("{result}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
7
```
