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
<!-- unused BODY `let`s. shv2 checks body `var`s (E3077 owes that half — an unmentioned `var` reports as unused rather than as should-be-let); the `let` half is its own rung, and its cost is EXPRESSIVENESS rather than tidiness: MEASURED, it refuses 82 cases of this suite whose shape IS the test — a leak gate binds an owned value and deliberately never touches it, so scope exit is the only thing that can free it, and `register-pressure` needs a def that is never read. The E2001/E3064 reason this note used to give was the ORACLE's and does not transfer: shv2 has no purity analysis, so `_ = <any call>` is already legal here -->
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
<!-- TUPLE-DESTRUCTURING bindings — `let (a, b) = …` binds through a path that mints no unused candidate. Its own rung -->
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
<!-- two unused BODY `let`s — the same missing rung as `unused-let` above. (The "first reported" property itself is already live: `reportUnusedBindings` scans `unusedCandidates` in declaration order and returns on the first hit, and `unused-var` above now exercises it for a `var`.) -->
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

<!-- disabled-test: unused-match-binding -->
<!-- MATCH PAYLOAD bindings — `value(n)` binds through `createPayloadBinding`, which mints no unused candidate. Its own rung -->
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

<!-- disabled-test: unused-closure-param -->
<!-- CLOSURE PARAMETERS — `parseClosureExpression` deliberately drains no E3012 (`Parser.maxon`, the "NO E3012 DRAIN HERE" note), so a closure's own candidates are collected and discarded. Its own rung -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(function(n Integer) gives 42, x: 10)
	return result
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-closure-param.test:11:30: unused variable: 'n'
```

<!-- test: used-closure-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(function(n Integer) gives n + 1, x: 10)
	return result
end 'main'
```
```exitcode
11
```

<!-- test: discard-closure-param -->
```maxon

typealias Integer = int(i64.min to i64.max)

typealias FnTypeAlias1 = function(Integer) returns Integer
function apply(f FnTypeAlias1, x Integer) returns Integer
	return f(x)
end 'apply'

function main() returns ExitCode
	let result = apply(function(_ Integer) gives 42, x: 10)
	return result
end 'main'
```
```exitcode
42
```

<!-- disabled-test: unused-otherwise-binding -->
<!-- `otherwise (e)` ERROR bindings — the handler's binding mints no unused candidate. Its own rung -->
The `(e)` binding on `try expr otherwise (e) 'h' ... end 'h'` is a local variable.
Declaring it without referencing it inside the handler body is a compile error.
```maxon

typealias Score = int(0 to 100)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Score throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var x = 0
	try mayFail() otherwise (e) 'handler'
		x = 42
	end 'handler'
	return x
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/unused-variables/unused-otherwise-binding.test:15:27: unused variable: 'e'
```

<!-- test: used-otherwise-binding -->
A `try expr otherwise (e) 'h' ... end 'h'` handler that matches on the binding is allowed.
```maxon

typealias Score = int(0 to 100)

enum MyError implements Error
	failed
end 'MyError'

function mayFail() returns Score throws MyError
	throw MyError.failed
end 'mayFail'

function main() returns ExitCode
	var x = 0
	try mayFail() otherwise (e) 'handler'
		match e 'kind'
			failed then x = 42
		end 'kind'
	end 'handler'
	return x
end 'main'
```
```exitcode
42
```
