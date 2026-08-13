---
feature: tuple-assign
status: stable
keywords: [tuple, assignment, destructuring, multi-return]
category: statements
---

# Tuple Assignment

## Documentation

### Overview

Tuple assignment allows assigning multiple return values from a function call (or a tuple expression) to existing mutable variables in a single statement.

```text
var pos = 0
var block = ""
(pos, block) = parseNext(input, pos)
```

This is different from tuple destructuring declaration (`var (a, b) = ...`), which creates new variables. Tuple assignment requires the variables to already exist and be mutable (`var`).

### Syntax

```text
(name1, name2, ...) = expression
```

Where `expression` evaluates to a tuple with the same number of elements as there are names.

### Discard with `_`

Use `_` to discard individual elements:

```text
(result, _) = compute()
(_, status) = fetch()
(_, _) = sideEffect()
```

### Rules

- All named variables must already be declared with `var`
- Immutable (`let`) variables cannot be targets
- The number of names must match the tuple size
- `_` discards the corresponding element without assignment

## Tests

<!-- test: basic-tuple-assign -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var x = 0
	var y = 0
	(x, y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: tuple-assign-in-loop -->
```maxon
typealias Integer = int(i64.min to i64.max)

function step(n Integer) returns (Integer, Integer)
	return (n + 1, n * 2)
end 'step'

function main() returns ExitCode
	var a = 0
	var b = 0
	var i = 0
	while i < 3 'loop'
		(a, b) = step(a)
		i = i + 1
	end 'loop'
	return b
end 'main'
```
```exitcode
4
```

<!-- test: tuple-assign-discard -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var x = 0
	(x, _) = makePair(42, b: 99)
	return x
end 'main'
```
```exitcode
42
```

<!-- test: tuple-assign-discard-all -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	(_, _) = makePair(10, b: 32)
	return 0
end 'main'
```
```maxoncstderr
error E3064: specs/fragments/tuple-assign/tuple-assign-discard-all.test:9:2: result of pure function 'makePair' must be used
```

<!-- test: tuple-assign-error-immutable -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	let x = 0
	let y = 0
	(x, y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/tuple-assign/tuple-assign-error-immutable.test:11:3: cannot assign to immutable variable: 'x'
```

<!-- test: tuple-assign-mixed-var-decl -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var x = 0
	(x, let y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: tuple-assign-all-new-var-decl -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	(let x, let y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: tuple-assign-let-decl -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var x = 0
	(x, let y) = makePair(10, b: 32)
	return x + y
end 'main'
```
```exitcode
42
```

<!-- test: tuple-assign-error-assign-to-let-decl -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var x = 0
	(x, let y) = makePair(10, b: 32)
	(x, y) = makePair(1, b: 2)
	return x + y
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/tuple-assign/tuple-assign-error-assign-to-let-decl.test:11:6: cannot assign to immutable variable: 'y'
```

<!-- test: tuple-assign-error-count-mismatch -->
```maxon
typealias Integer = int(i64.min to i64.max)

function makePair(a Integer, b Integer) returns (Integer, Integer)
	return (a, b)
end 'makePair'

function main() returns ExitCode
	var x = 0
	(x) = makePair(10, b: 32)
	return x
end 'main'
```
```maxoncstderr
error E3005: specs/fragments/tuple-assign/tuple-assign-error-count-mismatch.test:10:2: Tuple has 2 elements but destructuring has 1 bindings
```
