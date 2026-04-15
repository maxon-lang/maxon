---
feature: unused-variables
status: stable
keywords: [variables, unused, diagnostics, errors]
category: diagnostics
---

# Unused Variable Detection

## Documentation

Maxon requires all local variables to be used. Declaring a variable with `var` or `let` and never referencing it causes a compilation error.

### Example Error

```maxon
function main() returns ExitCode
	var x = 42  // Error: 'x' is unused
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/docs-example-1.test:3:6: unused variable: 'x'
```

### Discarding Return Values

Use `_ =` to discard a function's return value:

```maxon
typealias Integer = int(i64.min to i64.max)

function sideEffect() returns Integer
	print("hello\n")
	return 42
end 'sideEffect'

function main() returns ExitCode
	_ = sideEffect()  // OK: underscore discards return value
	return 0
end 'main'
```

## Tests

<!-- test: unused-var -->
```maxon

function main() returns ExitCode
	var x = 42
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-var.test:4:6: unused variable: 'x'
```

<!-- test: unused-let -->
```maxon

function main() returns ExitCode
	let x = 42
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-let.test:4:6: unused variable: 'x'
```

<!-- test: used-var -->
```maxon

function main() returns ExitCode
	let x = 42
	return x
end 'main'
```
```exitcode
42
```

<!-- test: used-let -->
```maxon

function main() returns ExitCode
	let x = 10
	return x
end 'main'
```
```exitcode
10
```

<!-- test: underscore-discard -->
```maxon

function main() returns ExitCode
	_ = 42
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/unused-variables/underscore-discard.test:4:2: expected a function call
```

<!-- test: used-in-nested-scope -->
```maxon

function main() returns ExitCode
	let x = 42
	if x > 0 'check'
		return x
	end 'check'
	return 0
end 'main'
```
```exitcode
42
```

<!-- test: tuple-destructuring-unused -->
```maxon

typealias Small = int(0 to 100)

function makePair() returns (Small, Small)
	return (10, 20)
end 'makePair'

function main() returns ExitCode
	let (a, b) = makePair()
	return a
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/tuple-destructuring-unused.test:10:10: unused variable: 'b'
```

<!-- test: multiple-unused-first-reported -->
```maxon

function main() returns ExitCode
	let x = 1
	let y = 2
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/multiple-unused-first-reported.test:4:6: unused variable: 'x'
```

<!-- test: unused-for-in-variable -->
```maxon

function main() returns ExitCode
	let arr = [1, 2, 3]
	var count = 0
	for s in arr 'loop'
		count = count + 1
	end 'loop'
	return count
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-for-in-variable.test:6:6: unused variable: 's'
```

<!-- test: used-for-in-variable -->
```maxon

function main() returns ExitCode
	let arr = [10, 20, 30]
	var total = 0
	for s in arr 'loop'
		total = total + s
	end 'loop'
	return total
end 'main'
```
```exitcode
60
```

<!-- test: discard-for-in-variable -->
```maxon

function main() returns ExitCode
	let arr = [1, 2, 3]
	var count = 0
	for _ in arr 'loop'
		count = count + 1
	end 'loop'
	return count
end 'main'
```
```exitcode
3
```

<!-- test: unused-for-range-variable -->
```maxon

function main() returns ExitCode
	var count = 0
	for i in 0 upto 3 'loop'
		count = count + 1
	end 'loop'
	return count
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-for-range-variable.test:5:6: unused variable: 'i'
```

<!-- test: unused-match-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.value(42)
	match c 'check'
		empty then return 1
		value(n) then return 0
	end 'check'
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-match-binding.test:14:9: unused variable: 'n'
```

<!-- test: used-match-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.value(42)
	match c 'check'
		empty then return 1
		value(n) then return n
	end 'check'
end 'main'
```
```exitcode
42
```

<!-- test: discard-match-binding -->
```maxon

typealias Integer = int(i64.min to i64.max)

union Container
	empty
	value(n Integer)
end 'Container'

function main() returns ExitCode
	let c = Container.value(42)
	match c 'check'
		empty then return 1
		value then return 0
	end 'check'
end 'main'
```
```exitcode
0
```

<!-- test: unused-closure-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(f: (n Integer) gives 42, x: 10)
	return result
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-closure-param.test:10:25: unused variable: 'n'
```

<!-- test: used-closure-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(f: (n Integer) gives n + 1, x: 10)
	return result
end 'main'
```
```exitcode
11
```

<!-- test: discard-closure-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

function apply(f (Integer) returns Integer, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(f: (_ Integer) gives 42, x: 10)
	return result
end 'main'
```
```exitcode
42
```
