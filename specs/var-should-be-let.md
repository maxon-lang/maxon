---
feature: var-should-be-let
status: stable
keywords: [variables, var, let, diagnostics, errors, mutability]
category: diagnostics
---

# Var Should Be Let Detection

## Documentation

Maxon requires that `var` declarations are actually mutated. If a variable is declared with `var` but never reassigned, it should be declared with `let` instead.

### Example Error

```maxon
function main() returns ExitCode
	var x = 10
	return x
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/docs-example-1.test:3:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

## Tests

<!-- test: var-never-reassigned -->
```maxon

function main() returns ExitCode
	var x = 10
	return x
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/var-never-reassigned.test:4:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

<!-- test: var-reassigned -->
```maxon

function main() returns ExitCode
	var x = 10
	x = 20
	return x
end 'main'
```
```exitcode
20
```

<!-- test: let-no-error -->
```maxon

function main() returns ExitCode
	let x = 10
	return x
end 'main'
```
```exitcode
10
```

<!-- test: var-reassigned-in-if -->
```maxon

function main() returns ExitCode
	var x = 0
	if x == 0 'check'
		x = 42
	end 'check'
	return x
end 'main'
```
```exitcode
42
```

<!-- test: multiple-var-first-reported -->
```maxon

function main() returns ExitCode
	var x = 1
	var y = 2
	return x + y
end 'main'
```
```maxoncstderr
error E3077: specs/fragments/var-should-be-let/multiple-var-first-reported.test:4:6: variable 'x' is never reassigned; use 'let' instead of 'var'
```

<!-- test: var-from-immutable-integer-ok -->
```maxon

function main() returns ExitCode
	let x = 10
	var y = x
	y = 20
	return y
end 'main'
```
```exitcode
20
```

<!-- test: var-from-immutable-struct -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let a = Point.create(1, y: 2)
	var b = a
	b.x = 99
	return b.x
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/var-from-immutable-struct.test:16:6: cannot assign immutable variable 'a' to mutable binding 'b'; use 'let' instead of 'var', or use clone()
```

<!-- test: var-from-immutable-function -->
```maxon

typealias Integer = int(i64.min to i64.max)

function double(x Integer) returns Integer
	return x * 2
end 'double'

function triple(x Integer) returns Integer
	return x * 3
end 'triple'

function main() returns ExitCode
	let f = double
	var g = f
	g = triple
	return g(10)
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/var-from-immutable-function.test:15:6: cannot assign immutable variable 'f' to mutable binding 'g'; use 'let' instead of 'var', or use clone()
```

<!-- test: var-from-immutable-struct-field -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var value as Integer

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Inner'

type Outer
	export var inner as Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let o = Outer.create(Inner.create(42))
	var i = o.inner
	i.value = 99
	return i.value
end 'main'
```
```maxoncstderr
error E3078: specs/fragments/var-should-be-let/var-from-immutable-struct-field.test:23:6: cannot assign from immutable variable to mutable binding 'i'; use 'let' instead of 'var', or use clone()
```

<!-- test: var-from-immutable-value-field-ok -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Point
	export var x as Integer
	export var y as Integer

	static function create(x Integer, y Integer) returns Self
		return Self{x: x, y: y}
	end 'create'
end 'Point'

function main() returns ExitCode
	let p = Point.create(1, y: 2)
	var x = p.x
	x = 99
	return x
end 'main'
```
```exitcode
99
```

<!-- test: var-from-mutable-ok -->
```maxon

function main() returns ExitCode
	var x = 10
	x = 20
	var y = x
	y = 30
	return y
end 'main'
```
```exitcode
30
```

<!-- test: unused-takes-precedence -->
```maxon

function main() returns ExitCode
	var x = 10
	return 0
end 'main'
```
```maxoncstderr
error E3012: specs/fragments/var-should-be-let/unused-takes-precedence.test:4:6: unused variable: 'x'
```
