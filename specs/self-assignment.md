---
feature: self-assignment
status: stable
keywords: [assignment, diagnostics, errors]
category: diagnostics
---

# Self-Assignment Detection

## Documentation

Maxon detects self-assignment — assigning a variable to itself — and reports it as a compile error, since it has no effect and is likely a bug. Similarly, `let _ =` requires a function call on the right-hand side; using it with any other expression is an error.

### Example Errors

```maxon
function main() returns ExitCode
	var x = 42
	x = x  // Error: self-assignment has no effect
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/docs-example-1.test:4:2: self-assignment has no effect: 'x = x'
```

```maxon
function main() returns ExitCode
	let _ = 0  // Error: expected a function call
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/docs-example-2.test:3:6: expected a function call
```

## Tests

<!-- test: self-assignment.basic -->
```maxon

function main() returns ExitCode
	var x = 10
	x = x
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/self-assignment.basic.test:5:2: self-assignment has no effect: 'x = x'
```

<!-- test: self-assignment.different-var -->
```maxon

function main() returns ExitCode
	var x = 10
	let y = 20
	x = y
	return x
end 'main'
```
```exitcode
20
```

<!-- test: self-assignment.expr-not-flagged -->
```maxon

function main() returns ExitCode
	var x = 10
	x = x + 1
	return x
end 'main'
```
```exitcode
11
```

<!-- test: self-assignment.field-self-assign -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x Integer
	export var y Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	var p = Point.create(x: 1, y: 2)
	p.x = p.x
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/self-assignment.field-self-assign.test:16:2: self-assignment has no effect: 'p.x = p.x'
```

<!-- test: self-assignment.discard-literal -->
```maxon

function main() returns ExitCode
	let _ = 42
	return 0
end 'main'
```
```maxoncstderr
error E3067: specs/fragments/self-assignment/self-assignment.discard-literal.test:4:6: expected a function call
```
