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

	static function create(value Integer) returns Self
		return Self{value: value}
	end 'create'
end 'Counter'

function main() returns ExitCode
	var c = Counter.create(10)
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

	static function create(id Integer, count Integer) returns Self
		return Self{id: id, count: count}
	end 'create'
end 'Config'

function main() returns ExitCode
	var c = Config.create(1, count: 0)
	c.id = 2
	return c.id
end 'main'
```
```maxoncstderr
error E2013: specs/fragments/challenge-struct-field-assign/immutable-field-assign-error.test:16:2: cannot assign to field 'Config.id' because it is immutable (declare with 'var' to make it mutable)
```

<!-- test: nested-struct-field-reassignment -->
```maxon

typealias Integer = int(i64.min to i64.max)

type Inner
	export var x Integer

	static function create(x Integer) returns Self
		return Self{x: x}
	end 'create'
end 'Inner'

type Outer
	export var inner Inner

	static function create(inner Inner) returns Self
		return Self{inner: inner}
	end 'create'
end 'Outer'

function main() returns ExitCode
	let i = Inner.create(10)
	var o = Outer.create(i)
	o.inner.x = 42
	return o.inner.x
end 'main'
```
```exitcode
42
```
