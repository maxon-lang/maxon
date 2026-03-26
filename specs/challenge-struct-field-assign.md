---
feature: challenge-struct-field-assign
status: stable
keywords: struct, field, assignment, mutation
category: semantics
---
# Challenge Struct Field Assign

## Documentation

## Struct Field Assignment

Struct fields can be modified after the struct is created.

## Tests

<!-- test: struct-field-reassignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Counter
	export var value Integer
end 'Counter'

function main() returns ExitCode
	var c = Counter{value: 10}
	c.value = 42
	return c.value
end 'main'
```
```exitcode
42
```

<!-- test: immutable-field-assign-error -->
Assigning to an immutable (`let`) field should be a compile-time error.

```maxon

typealias Integer = int(i64.min to i64.max)

type Config
	export let id Integer
	export var count Integer
end 'Config'

function main() returns ExitCode
	var c = Config{id: 1, count: 0}
	c.id = 2
	return c.id
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/challenge-struct-field-assign/immutable-field-assign-error.test:12:2: cannot assign to field 'Config.id' because it is immutable (declare with 'var' to make it mutable)
```

<!-- test: nested-struct-field-reassignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var x Integer
end 'Inner'

type Outer
	export var inner Inner
end 'Outer'

function main() returns ExitCode
	var i = Inner{x: 10}
	var o = Outer{inner: i}
	o.inner.x = 42
	return o.inner.x
end 'main'
```
```exitcode
42
```
