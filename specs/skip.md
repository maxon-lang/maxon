---
feature: skip
status: experimental
keywords: [skip, iterator, loop, for, continue]
category: control-flow
---

## Documentation

# skip

The `skip n` statement is used inside `for` loops to skip the current element and the next `n` elements, then continue with the next iteration.

It works like `continue` but additionally advances the loop by `n` positions before resuming. If skipping past the end, the loop exits normally.

## Syntax

```text
skip n            // Skip next n elements in innermost for loop
```

## Examples

```text
var items = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
for item in items 'loop'
    if item == 3 'check'
        skip 2    // Skip 4 and 5, resume with 6
    end 'check'
    print("{item}")
end 'loop'
// Prints: 1, 2, 3, 6, 7, 8, 9, 10
```

## Notes

- `skip 0` is equivalent to `continue` — it skips only the rest of the current iteration
- `n` can be any non-negative integer expression
- `skip` is only valid inside `for` loops (not `while` loops)
- `skip` always applies to the innermost `for` loop
- If skipping past the end, the loop exits gracefully

## Tests

<!-- test: skip.basic -->
Basic skip: skip 1 skips one element.
```maxon
function main() returns ExitCode
		let items = [10, 20, 30, 40, 50]
		var sum = 0
		for item in items 'loop'
				if item == 20 'check'
						skip 1
				end 'check'
				sum = sum + item
		end 'loop'
		return sum
end 'main'
```
```exitcode
100
```
```stdout
```

<!-- test: skip.multiple -->
Skip multiple: skip 2 skips rest of current iteration plus the next two elements.
```maxon
function main() returns ExitCode
		let items = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
		var sum = 0
		for item in items 'loop'
				if item == 3 'check'
						skip 2
				end 'check'
				sum = sum + item
		end 'loop'
		return sum
end 'main'
```
```exitcode
43
```
```stdout
```

<!-- test: skip.zero -->
Skip zero: skip 0 behaves like continue.
```maxon
function main() returns ExitCode
		let items = [10, 20, 30, 40, 50]
		var sum = 0
		for item in items 'loop'
				if item == 30 'check'
						skip 0
				end 'check'
				sum = sum + item
		end 'loop'
		return sum
end 'main'
```
```exitcode
120
```
```stdout
```

<!-- test: skip.past-end -->
Skip past end: skip n where n exceeds remaining elements exits the loop.
```maxon
function main() returns ExitCode
		let items = [10, 20, 30, 40, 50]
		var sum = 0
		for item in items 'loop'
				if item == 30 'check'
						skip 100
				end 'check'
				sum = sum + item
		end 'loop'
		return sum
end 'main'
```
```exitcode
30
```
```stdout
```

<!-- test: skip.variable -->
Skip with variable: skip someVar with runtime-computed skip count.
```maxon
function main() returns ExitCode
		let items = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]
		var sum = 0
		let skipCount = 3
		for item in items 'loop'
				if item == 2 'check'
						skip skipCount
				end 'check'
				sum = sum + item
		end 'loop'
		return sum
end 'main'
```
```exitcode
41
```
```stdout
```

<!-- test: skip.nested-loops -->
Skip in nested loops: skip in inner loop only affects inner loop.
```maxon
function main() returns ExitCode
		var sum = 0
		for _ in 1 to 2 'outer'
				let inner = [10, 20, 30, 40, 50]
				for i in inner 'inner'
						if i == 20 'check'
								skip 1
						end 'check'
						sum = sum + i
				end 'inner'
		end 'outer'
		return sum
end 'main'
```
```exitcode
200
```
```stdout
```

<!-- test: skip.enumerated -->
Skip with enumerated iterator: index advances correctly when skipping.
```maxon
function main() returns ExitCode
		let items = [10, 20, 30, 40, 50]
		var sum = 0
		for (i, item) in items.enumerated() 'loop'
				if i == 1 'check'
						skip 2
				end 'check'
				sum = sum + item
		end 'loop'
		return sum
end 'main'
```
```exitcode
60
```
```stdout
```

<!-- test: skip.error-while-loop -->
Error: skip in while loop produces a compile error.
```maxon
function main() returns ExitCode
		var i = 0
		while i < 10 'loop'
				skip 1
				i = i + 1
		end 'loop'
		return 0
end 'main'
```
```maxoncstderr
error E2047: specs/fragments/skip/skip.error-while-loop.test:5:5: 'skip' can only be used inside a for loop
```

<!-- test: skip.range-basic -->
Basic skip in range loop: skip 2 skips rest of current iteration and next two values.
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 to 10 'loop'
				if i == 3 'check'
						skip 2
				end 'check'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
43
```
```stdout
```

<!-- test: skip.range-zero -->
Skip 0 in range loop behaves like continue.
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 to 5 'loop'
				if i == 3 'check'
						skip 0
				end 'check'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
12
```
```stdout
```

<!-- test: skip.range-past-end -->
Skip past end in range loop exits the loop gracefully.
```maxon
function main() returns ExitCode
		var sum = 0
		for i in 1 to 5 'loop'
				if i == 3 'check'
						skip 100
				end 'check'
				sum = sum + i
		end 'loop'
		return sum
end 'main'
```
```exitcode
3
```
```stdout
```

<!-- test: skip.error-outside-loop -->
Error: skip outside any loop produces a compile error.
```maxon
function main() returns ExitCode
		skip 1
		return 0
end 'main'
```
```maxoncstderr
error E2001: specs/fragments/skip/skip.error-outside-loop.test:3:3: 'skip' can only be used inside a loop
```
